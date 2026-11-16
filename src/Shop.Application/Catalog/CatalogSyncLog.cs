using Microsoft.Extensions.Logging;
using Shop.Application.Common;
using Shop.Domain.Catalog;

namespace Shop.Application.Catalog;

/// <summary>Événements de log de la synchronisation, avec identifiants stables (PLT-6, CAT-R12).</summary>
internal static partial class CatalogSyncLog
{
    [LoggerMessage(EventId = 1000, EventName = nameof(LogEvents.CatalogSyncStarted), Level = LogLevel.Information,
        Message = "Synchronisation du catalogue démarrée (run {RunId}, déclencheur {Trigger}, instance {InstanceId}, forcée : {Forced}).")]
    public static partial void Started(ILogger logger, long runId, SyncTrigger trigger, string instanceId, bool forced);

    [LoggerMessage(EventId = 1001, EventName = nameof(LogEvents.CatalogSyncSucceeded), Level = LogLevel.Information,
        Message = "Synchronisation réussie (run {RunId}) : {Fetched} récupérés, {Inserted} insérés, {Updated} mis à jour, {Discontinued} retirés, {Reactivated} réactivés.")]
    public static partial void Succeeded(ILogger logger, long runId, int fetched, int inserted, int updated, int discontinued, int reactivated);

    [LoggerMessage(EventId = 1002, EventName = nameof(LogEvents.CatalogSyncFailed), Level = LogLevel.Error,
        Message = "Échec de la synchronisation (run {RunId}, déclencheur {Trigger}, instance {InstanceId}) : {Reason} — {Cause}")]
    public static partial void Failed(ILogger logger, long runId, SyncTrigger trigger, string instanceId, SyncFailureReason reason, string cause, Exception? exception = null);

    [LoggerMessage(EventId = 1003, EventName = nameof(LogEvents.CatalogSyncGuardTriggered), Level = LogLevel.Error,
        Message = "Garde-fou de retrait massif déclenché (run {RunId}, déclencheur {Trigger}, instance {InstanceId}) : {WouldDiscontinue} produits sur {ActiveProducts} seraient retirés (seuil {Threshold:P0}). Catalogue inchangé ; vérifier le fournisseur puis forcer si le retrait est légitime.")]
    public static partial void GuardTriggered(ILogger logger, long runId, SyncTrigger trigger, string instanceId, int activeProducts, int wouldDiscontinue, decimal threshold);

    [LoggerMessage(EventId = 1004, EventName = nameof(LogEvents.CatalogSyncSkipped), Level = LogLevel.Information,
        Message = "Synchronisation {Trigger} ignorée sur l'instance {InstanceId} : une autre synchronisation est en cours.")]
    public static partial void Skipped(ILogger logger, SyncTrigger trigger, string instanceId);

    [LoggerMessage(EventId = 1005, EventName = nameof(LogEvents.CatalogSyncLockLost), Level = LogLevel.Error,
        Message = "Verrou de synchronisation perdu pendant le run {RunId} : synchronisation interrompue, catalogue inchangé.")]
    public static partial void LockLost(ILogger logger, long runId);

    [LoggerMessage(EventId = 1006, EventName = nameof(LogEvents.CatalogSyncOrphansInterrupted), Level = LogLevel.Warning,
        Message = "{Count} run(s) de synchronisation resté(s) en cours après l'arrêt d'une instance, marqué(s) comme interrompu(s).")]
    public static partial void OrphansInterrupted(ILogger logger, int count);
}
