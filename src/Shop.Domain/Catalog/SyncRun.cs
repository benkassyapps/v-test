namespace Shop.Domain.Catalog;

public enum SyncTrigger : byte
{
    Scheduled = 1,
    Manual = 2,
}

public enum SyncRunStatus : byte
{
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
}

public enum SyncFailureReason : byte
{
    ProviderError = 1,
    InvalidSnapshot = 2,
    GuardTriggered = 3,
    DatabaseError = 4,
    Interrupted = 5,
}

/// <summary>
/// Exécution historisée d'une synchronisation (CAT-R3.5, CAT-R12) : déclencheur, instance, statut, compteurs et cause.
/// </summary>
public sealed class SyncRun
{
    public const int MaxErrorLength = 1000;

    /// <summary>Cause enregistrée pour un run resté « en cours » après l'arrêt brutal de son instance, ou dont le verrou a été perdu.</summary>
    public const string InterruptedMessage = "Synchronisation interrompue (arrêt de l'instance qui l'exécutait ou verrou perdu) : aucune modification enregistrée par ce run.";

    private SyncRun()
    {
    }

    public long Id { get; private set; }

    public SyncTrigger Trigger { get; private set; }

    public SyncRunStatus Status { get; private set; }

    public SyncFailureReason? FailureReason { get; private set; }

    /// <summary>Le garde-fou de retrait massif a été levé par l'exploitant (CAT-R3.6).</summary>
    public bool Forced { get; private set; }

    public string InstanceId { get; private set; } = string.Empty;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public int Fetched { get; private set; }

    public int Inserted { get; private set; }

    public int Updated { get; private set; }

    public int Discontinued { get; private set; }

    public int Reactivated { get; private set; }

    /// <summary>Renseigné si le garde-fou a bloqué : produits actifs avant la synchronisation.</summary>
    public int? ActiveBefore { get; private set; }

    /// <summary>Renseigné si le garde-fou a bloqué : produits qui auraient été retirés.</summary>
    public int? WouldDiscontinue { get; private set; }

    /// <summary>Cause de l'échec, assainie (pas de pile d'appels ni de SQL).</summary>
    public string? Error { get; private set; }

    public static SyncRun Start(SyncTrigger trigger, string instanceId, bool forced, DateTimeOffset now) =>
        new() { Trigger = trigger, InstanceId = instanceId, Forced = forced, Status = SyncRunStatus.Running, StartedAt = now };

    /// <summary>Run non exécuté car une autre synchronisation était en cours (CAT-R10.2).</summary>
    public static SyncRun Skipped(SyncTrigger trigger, string instanceId, string reason, DateTimeOffset now) =>
        new()
        {
            Trigger = trigger,
            InstanceId = instanceId,
            Status = SyncRunStatus.Skipped,
            StartedAt = now,
            FinishedAt = now,
            Error = Truncate(reason),
        };

    public void Succeed(SyncCounts counts, DateTimeOffset now)
    {
        EnsureRunning();
        Status = SyncRunStatus.Succeeded;
        FinishedAt = now;
        (Fetched, Inserted, Updated, Discontinued, Reactivated) =
            (counts.Fetched, counts.Inserted, counts.Updated, counts.Discontinued, counts.Reactivated);
    }

    public void Fail(SyncFailureReason reason, string message, DateTimeOffset now, int fetched = 0)
    {
        EnsureRunning();
        Status = SyncRunStatus.Failed;
        FailureReason = reason;
        Error = Truncate(message);
        FinishedAt = now;
        Fetched = fetched;
    }

    public void BlockByGuard(int activeBefore, int wouldDiscontinue, int fetched, DateTimeOffset now)
    {
        Fail(
            SyncFailureReason.GuardTriggered,
            $"Garde-fou de retrait massif déclenché : {wouldDiscontinue} produits sur {activeBefore} seraient retirés.",
            now,
            fetched);
        ActiveBefore = activeBefore;
        WouldDiscontinue = wouldDiscontinue;
    }

    private void EnsureRunning()
    {
        if (Status != SyncRunStatus.Running)
        {
            throw new InvalidOperationException($"Le run {Id} n'est plus en cours (statut {Status}).");
        }
    }

    private static string Truncate(string message) =>
        message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];
}
