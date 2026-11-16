using Microsoft.EntityFrameworkCore;
using Shop.Application.Catalog;
using Shop.Domain.Catalog;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Catalog;

/// <summary>
/// Historique des synchronisations. Chaque opération utilise son propre contexte court, indépendant de la
/// transaction du catalogue : un échec de synchronisation n'efface jamais la trace de cet échec.
/// Les fins de run sont des mises à jour CONDITIONNELLES (« encore en cours ») : un run terminé n'est jamais réécrit,
/// ce qui rend ces écritures idempotentes et sûres face à une autre instance.
/// </summary>
internal sealed class SyncRunStore(IDbContextFactory<ShopDbContext> contexts, TransientRetry retry) : ISyncRunStore
{
    public async Task AddAsync(SyncRun run, CancellationToken cancellationToken)
    {
        // Pas de rejeu : une insertion rejouée après un commit dont l'accusé est perdu créerait un doublon.
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.Add(run);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task CompleteAsync(SyncRun run, CancellationToken cancellationToken) =>
        retry.ExecuteAsync(async token =>
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            return await db.Set<SyncRun>()
                .Where(stored => stored.Id == run.Id && stored.Status == SyncRunStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(stored => stored.Status, run.Status)
                    .SetProperty(stored => stored.FailureReason, run.FailureReason)
                    .SetProperty(stored => stored.Error, run.Error)
                    .SetProperty(stored => stored.FinishedAt, run.FinishedAt)
                    .SetProperty(stored => stored.Fetched, run.Fetched)
                    .SetProperty(stored => stored.Inserted, run.Inserted)
                    .SetProperty(stored => stored.Updated, run.Updated)
                    .SetProperty(stored => stored.Discontinued, run.Discontinued)
                    .SetProperty(stored => stored.Reactivated, run.Reactivated)
                    .SetProperty(stored => stored.ActiveBefore, run.ActiveBefore)
                    .SetProperty(stored => stored.WouldDiscontinue, run.WouldDiscontinue), token);
        }, cancellationToken);

    public Task<int> InterruptOrphanedRunsAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        retry.ExecuteAsync(async token =>
        {
            // Ensembliste : la condition « encore en cours » est réévaluée par SQL Server au moment de l'écriture.
            // Si une instance est en train de valider son run (ligne verrouillée par sa transaction), cette mise à jour
            // attend son commit puis ne touche plus ce run, devenu « réussi ».
            await using var db = await contexts.CreateDbContextAsync(token);
            return await db.Set<SyncRun>()
                .Where(run => run.Status == SyncRunStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, SyncRunStatus.Failed)
                    .SetProperty(run => run.FailureReason, SyncFailureReason.Interrupted)
                    .SetProperty(run => run.Error, SyncRun.InterruptedMessage)
                    .SetProperty(run => run.FinishedAt, now), token);
        }, cancellationToken);

    public Task<SyncScheduleState> GetScheduleStateAsync(CancellationToken cancellationToken) =>
        ReadAsync(async (db, token) =>
        {
            var runs = db.Set<SyncRun>().AsNoTracking();
            var lastSuccess = await runs
                .Where(run => run.Status == SyncRunStatus.Succeeded)
                .OrderByDescending(run => run.Id)
                .Select(run => new { run.Id, run.FinishedAt })
                .FirstOrDefaultAsync(token);

            var lastSuccessId = lastSuccess?.Id ?? 0;
            var failures = runs.Where(run => run.Trigger == SyncTrigger.Scheduled
                                             && run.Status == SyncRunStatus.Failed
                                             && run.Id > lastSuccessId);
            var failureCount = await failures.CountAsync(token);
            var lastFailureAt = failureCount == 0 ? null : await failures.MaxAsync(run => run.FinishedAt, token);

            return new SyncScheduleState(lastSuccess?.FinishedAt, failureCount, lastFailureAt);
        }, cancellationToken);

    public Task<SyncRun?> GetLastSuccessAsync(CancellationToken cancellationToken) =>
        ReadAsync((db, token) => db.Set<SyncRun>().AsNoTracking()
            .Where(run => run.Status == SyncRunStatus.Succeeded)
            .OrderByDescending(run => run.Id)
            .FirstOrDefaultAsync(token), cancellationToken);

    public Task<SyncRun?> GetLastFinishedAsync(CancellationToken cancellationToken) =>
        ReadAsync((db, token) => db.Set<SyncRun>().AsNoTracking()
            .Where(run => run.Status == SyncRunStatus.Succeeded || run.Status == SyncRunStatus.Failed)
            .OrderByDescending(run => run.Id)
            .FirstOrDefaultAsync(token), cancellationToken);

    public Task<IReadOnlyList<SyncRun>> ListAsync(long? beforeId, int take, CancellationToken cancellationToken) =>
        ReadAsync<IReadOnlyList<SyncRun>>(async (db, token) => await db.Set<SyncRun>().AsNoTracking()
            .Where(run => beforeId == null || run.Id < beforeId)
            .OrderByDescending(run => run.Id)
            .Take(take)
            .ToListAsync(token), cancellationToken);

    private Task<T> ReadAsync<T>(Func<ShopDbContext, CancellationToken, Task<T>> query, CancellationToken cancellationToken) =>
        retry.ExecuteAsync(async token =>
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            return await query(db, token);
        }, cancellationToken);
}
