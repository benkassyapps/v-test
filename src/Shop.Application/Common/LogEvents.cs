using Microsoft.Extensions.Logging;

namespace Shop.Application.Common;

/// <summary>
/// Registre UNIQUE des identifiants d'événements de log stables (PLT-6, CAT-R12) : un outil de supervision peut
/// déclencher une alerte dessus. Toute méthode <c>[LoggerMessage]</c> utilise un identifiant déclaré ici, avec son nom.
/// Ne jamais renuméroter ni réutiliser un identifiant.
/// </summary>
public static class LogEvents
{
    // 1000–1099 : synchronisation du catalogue.
    public static readonly EventId CatalogSyncStarted = new(1000, nameof(CatalogSyncStarted));
    public static readonly EventId CatalogSyncSucceeded = new(1001, nameof(CatalogSyncSucceeded));
    public static readonly EventId CatalogSyncFailed = new(1002, nameof(CatalogSyncFailed));
    public static readonly EventId CatalogSyncGuardTriggered = new(1003, nameof(CatalogSyncGuardTriggered));
    public static readonly EventId CatalogSyncSkipped = new(1004, nameof(CatalogSyncSkipped));
    public static readonly EventId CatalogSyncLockLost = new(1005, nameof(CatalogSyncLockLost));
    public static readonly EventId CatalogSyncOrphansInterrupted = new(1006, nameof(CatalogSyncOrphansInterrupted));
    public static readonly EventId CatalogSchedulerTickFailed = new(1007, nameof(CatalogSchedulerTickFailed));
    public static readonly EventId CatalogSchedulerDisabled = new(1008, nameof(CatalogSchedulerDisabled));

    // 2000–2099 : base de données.
    public static readonly EventId ReadCommittedSnapshotDisabled = new(2000, nameof(ReadCommittedSnapshotDisabled));
    public static readonly EventId DatabaseMigrated = new(2001, nameof(DatabaseMigrated));
    public static readonly EventId DatabaseMigrationFailed = new(2002, nameof(DatabaseMigrationFailed));
    public static readonly EventId ReadCommittedSnapshotCheckFailed = new(2003, nameof(ReadCommittedSnapshotCheckFailed));

    // 9000–9099 : API.
    public static readonly EventId UnhandledException = new(9000, nameof(UnhandledException));
    public static readonly EventId BadRequestRejected = new(9001, nameof(BadRequestRejected));
}
