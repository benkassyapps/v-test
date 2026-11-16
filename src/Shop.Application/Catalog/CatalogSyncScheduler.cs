namespace Shop.Application.Catalog;

/// <summary>Issue d'une vérification du planificateur.</summary>
public enum SchedulerCheckOutcome
{
    /// <summary>Le créneau est couvert ou une nouvelle tentative n'est pas encore permise.</summary>
    NotDue,

    /// <summary>Une autre synchronisation était en cours : run « Skipped » enregistré.</summary>
    Skipped,

    /// <summary>Une synchronisation planifiée a été exécutée (réussie ou non).</summary>
    Ran,
}

/// <summary>
/// Vérification périodique, exécutée sur CHAQUE instance (CAT-R2, CAT-R10) : lecture peu coûteuse de l'historique,
/// puis synchronisation si elle est due. Le verrou et la revérification sous verrou garantissent une seule
/// synchronisation réussie par créneau, quel que soit le nombre d'instances.
/// </summary>
public sealed class CatalogSyncScheduler(ISyncRunStore runs, SyncSchedulePolicy schedule, CatalogSyncService sync, TimeProvider clock)
{
    public async Task<SchedulerCheckOutcome> CheckAsync(CancellationToken cancellationToken)
    {
        var decision = schedule.Evaluate(clock.GetUtcNow(), await runs.GetScheduleStateAsync(cancellationToken));
        if (!decision.IsDue)
        {
            return SchedulerCheckOutcome.NotDue;
        }

        var execution = await sync.RunScheduledAsync(cancellationToken);
        return execution.Kind switch
        {
            SyncExecutionKind.AlreadyRunning => SchedulerCheckOutcome.Skipped,
            SyncExecutionKind.NotDue => SchedulerCheckOutcome.NotDue,
            _ => SchedulerCheckOutcome.Ran,
        };
    }
}
