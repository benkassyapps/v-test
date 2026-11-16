using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shop.Application.Common;
using Shop.Domain.Catalog;
using Shop.Domain.Common;

namespace Shop.Application.Catalog;

/// <summary>Issue d'une tentative de synchronisation.</summary>
public enum SyncExecutionKind
{
    /// <summary>Synchronisation planifiée plus due (une autre instance vient de réussir) : rien n'est fait.</summary>
    NotDue,

    /// <summary>Une autre synchronisation est en cours (verrou détenu ailleurs).</summary>
    AlreadyRunning,

    /// <summary>La synchronisation a été exécutée (réussie ou en échec, voir le résultat).</summary>
    Executed,
}

public sealed record SyncExecution(SyncExecutionKind Kind, Result<SyncRun> Result);

/// <summary>
/// Synchronisation du catalogue (specs/catalog-sync/design.md §3). Les déclenchements manuel et planifié suivent
/// exactement le même chemin (CAT-R3.2) : verrou distribué, instantané complet AVANT toute écriture, application en
/// une seule transaction protégée par une barrière sur le run, run historisé à chaque étape.
/// </summary>
public sealed class CatalogSyncService(
    ICatalogSyncLock syncLock,
    ISyncRunStore runs,
    ICatalogProvider provider,
    ICatalogStore store,
    SyncSchedulePolicy schedule,
    IOptions<CatalogSyncOptions> options,
    InstanceIdentity instance,
    TimeProvider clock,
    ILogger<CatalogSyncService> logger)
{
    /// <summary>Déclenchement manuel (CAT-R3) : s'exécute même si le créneau du jour est déjà couvert.</summary>
    public async Task<Result<SyncRun>> RunManualAsync(bool force, CancellationToken cancellationToken)
    {
        var execution = await ExecuteAsync(SyncTrigger.Manual, force, cancellationToken);
        return execution.Kind == SyncExecutionKind.AlreadyRunning ? CatalogErrors.SyncAlreadyRunning() : execution.Result;
    }

    /// <summary>Déclenchement planifié (CAT-R2) : revérifie sous le verrou que la synchronisation est toujours due ; jamais forcé.</summary>
    public Task<SyncExecution> RunScheduledAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(SyncTrigger.Scheduled, force: false, cancellationToken);

    private async Task<SyncExecution> ExecuteAsync(SyncTrigger trigger, bool force, CancellationToken cancellationToken)
    {
        await using var handle = await syncLock.TryAcquireAsync(cancellationToken);
        if (handle is null)
        {
            return await RecordAlreadyRunningAsync(trigger, cancellationToken);
        }

        // Double vérification sous le verrou : une autre instance a peut-être réussi entre-temps (CAT-R10.3).
        if (trigger == SyncTrigger.Scheduled
            && !schedule.Evaluate(clock.GetUtcNow(), await runs.GetScheduleStateAsync(cancellationToken)).IsDue)
        {
            return new SyncExecution(SyncExecutionKind.NotDue, CatalogErrors.SyncAlreadyRunning());
        }

        // On détient le verrou : un run encore « en cours » vient forcément d'une instance arrêtée brutalement.
        var orphans = await runs.InterruptOrphanedRunsAsync(clock.GetUtcNow(), cancellationToken);
        if (orphans > 0)
        {
            CatalogSyncLog.OrphansInterrupted(logger, orphans);
        }

        var run = SyncRun.Start(trigger, instance.Id, force, clock.GetUtcNow());
        await runs.AddAsync(run, cancellationToken);
        CatalogSyncLog.Started(logger, run.Id, trigger, instance.Id, force);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.LostToken);
        var result = await SynchronizeAsync(run, force, linked.Token, handle.LostToken, cancellationToken);
        return new SyncExecution(SyncExecutionKind.Executed, result);
    }

    private async Task<Result<SyncRun>> SynchronizeAsync(
        SyncRun run,
        bool force,
        CancellationToken token,
        CancellationToken lockLost,
        CancellationToken callerToken)
    {
        try
        {
            // 1. Instantané complet, sans aucune écriture dans le catalogue (CAT-R1, CAT-R8).
            var fetched = await provider.FetchSnapshotAsync(token);
            if (!fetched.IsSuccess)
            {
                var reason = fetched.Error.Details?.GetValueOrDefault("reason") as string == CatalogErrors.InvalidSnapshotReason
                    ? SyncFailureReason.InvalidSnapshot
                    : SyncFailureReason.ProviderError;
                return await FailAsync(run, reason, fetched.Error.Message, fetched.Error);
            }

            var snapshot = fetched.Value;
            var guard = force ? null : new DiscontinuationGuard(options.Value.MaxDiscontinuedRatio);

            // 2. Application en une seule transaction ; le garde-fou annule tout avant écriture (CAT-R11).
            var applied = await store.ApplyAsync(run.Id, products =>
            {
                var plan = SyncPlanner.Plan(products, snapshot);
                if (guard?.IsTriggered(plan) == true)
                {
                    return CatalogErrors.GuardTriggered(plan.ActiveBefore, plan.ToDiscontinue.Count);
                }

                var newProducts = plan.ApplyTo(clock.GetUtcNow());
                return new CatalogChanges(plan.Counts, newProducts);
            }, token);

            if (!applied.IsSuccess)
            {
                return applied.Error.Code == ErrorCodes.SyncGuardTriggered
                    ? await BlockByGuardAsync(run, applied.Error, snapshot.Products.Count)
                    : await InterruptedAsync(run); // barrière : le run a été repris par une autre instance
            }

            // 3. Réussite : le run a été validé en base dans la même transaction que le catalogue.
            var counts = applied.Value.Counts;
            run.Succeed(counts, applied.Value.FinishedAt);
            CatalogSyncLog.Succeeded(logger, run.Id, counts.Fetched, counts.Inserted, counts.Updated, counts.Discontinued, counts.Reactivated);
            return run;
        }
        catch (Exception) when (lockLost.IsCancellationRequested && !callerToken.IsCancellationRequested)
        {
            // Annulation due à la perte du verrou : selon le moment, SqlClient lève OperationCanceledException ou SqlException.
            return await InterruptedAsync(run);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            const string cause = "erreur de base de données pendant l'application de la synchronisation (aucune modification enregistrée)";
            return await FailAsync(run, SyncFailureReason.DatabaseError, cause, CatalogErrors.DatabaseFailure(), exception);
        }
    }

    private async Task<SyncExecution> RecordAlreadyRunningAsync(SyncTrigger trigger, CancellationToken cancellationToken)
    {
        CatalogSyncLog.Skipped(logger, trigger, instance.Id);
        if (trigger == SyncTrigger.Scheduled)
        {
            var skipped = SyncRun.Skipped(trigger, instance.Id, "Une autre synchronisation était en cours.", clock.GetUtcNow());
            await runs.AddAsync(skipped, cancellationToken);
        }

        return new SyncExecution(SyncExecutionKind.AlreadyRunning, CatalogErrors.SyncAlreadyRunning());
    }

    private async Task<Result<SyncRun>> BlockByGuardAsync(SyncRun run, DomainError error, int fetched)
    {
        var active = (int)error.Details!["activeProducts"]!;
        var wouldDiscontinue = (int)error.Details!["wouldDiscontinue"]!;
        run.BlockByGuard(active, wouldDiscontinue, fetched, clock.GetUtcNow());
        await TryCompleteAsync(run);
        CatalogSyncLog.GuardTriggered(logger, run.Id, run.Trigger, run.InstanceId, active, wouldDiscontinue, options.Value.MaxDiscontinuedRatio);
        return error;
    }

    private async Task<Result<SyncRun>> FailAsync(
        SyncRun run,
        SyncFailureReason reason,
        string cause,
        DomainError error,
        Exception? exception = null)
    {
        run.Fail(reason, cause, clock.GetUtcNow(), run.Fetched);
        await TryCompleteAsync(run);
        CatalogSyncLog.Failed(logger, run.Id, run.Trigger, run.InstanceId, reason, cause, exception);
        return error;
    }

    private async Task<Result<SyncRun>> InterruptedAsync(SyncRun run)
    {
        run.Fail(SyncFailureReason.Interrupted, SyncRun.InterruptedMessage, clock.GetUtcNow());
        await TryCompleteAsync(run);
        CatalogSyncLog.LockLost(logger, run.Id);
        return CatalogErrors.SyncInterrupted();
    }

    /// <summary>
    /// Enregistre l'issue du run sans masquer l'erreur d'origine. L'écriture est conditionnelle (run encore en cours) :
    /// elle ne réécrit jamais un run terminé. Si la base est indisponible, le run restera « en cours » et sera marqué
    /// interrompu par la prochaine synchronisation.
    /// </summary>
    private async Task TryCompleteAsync(SyncRun run)
    {
        try
        {
            await runs.CompleteAsync(run, CancellationToken.None);
        }
#pragma warning disable CA1031 // Échec secondaire : l'erreur principale est déjà traitée et journalisée.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            CatalogSyncLog.Failed(logger, run.Id, run.Trigger, run.InstanceId, run.FailureReason ?? SyncFailureReason.DatabaseError,
                "impossible d'enregistrer l'issue du run", exception);
        }
    }
}
