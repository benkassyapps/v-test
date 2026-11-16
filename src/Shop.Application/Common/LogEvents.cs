using Microsoft.Extensions.Logging;

namespace Shop.Application.Common;

/// <summary>
/// Identifiants d'événements de log stables (PLT-6, CAT-R12) : un outil de supervision peut déclencher une alerte dessus.
/// Ne jamais renuméroter un événement existant.
/// </summary>
public static class LogEvents
{
    public static readonly EventId CatalogSyncStarted = new(1000, nameof(CatalogSyncStarted));
    public static readonly EventId CatalogSyncSucceeded = new(1001, nameof(CatalogSyncSucceeded));
    public static readonly EventId CatalogSyncFailed = new(1002, nameof(CatalogSyncFailed));
    public static readonly EventId CatalogSyncGuardTriggered = new(1003, nameof(CatalogSyncGuardTriggered));
    public static readonly EventId CatalogSyncSkipped = new(1004, nameof(CatalogSyncSkipped));
    public static readonly EventId CatalogSyncLockLost = new(1005, nameof(CatalogSyncLockLost));
    public static readonly EventId ReadCommittedSnapshotDisabled = new(2000, nameof(ReadCommittedSnapshotDisabled));
    public static readonly EventId DatabaseMigrated = new(2001, nameof(DatabaseMigrated));
    public static readonly EventId DatabaseMigrationFailed = new(2002, nameof(DatabaseMigrationFailed));
    public static readonly EventId UnhandledException = new(9000, nameof(UnhandledException));
}
