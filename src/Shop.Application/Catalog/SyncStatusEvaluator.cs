using System.Globalization;
using Shop.Domain.Catalog;

namespace Shop.Application.Catalog;

/// <summary>Diagnostic destiné à l'exploitant : faut-il agir, pourquoi, et comment.</summary>
public sealed record SyncActionAdvice(bool ActionRequired, string? Reason, string? RecommendedAction);

/// <summary>
/// Règle PURE de <c>actionRequired</c> pour GET /api/admin/catalog/sync-status (CAT-R12, specs/catalog-sync/design.md §5).
/// </summary>
public static class SyncStatusEvaluator
{
    public const string ForceSyncRoute = "POST /api/admin/catalog/sync?force=true";

    /// <param name="now">Instant présent.</param>
    /// <param name="lastSuccessAt">Fin de la dernière synchronisation réussie, s'il y en a une.</param>
    /// <param name="lastRun">Dernier run terminé hors « Skipped », s'il y en a un.</param>
    /// <param name="staleAfter">Durée maximale sans réussite.</param>
    public static SyncActionAdvice Evaluate(DateTimeOffset now, DateTimeOffset? lastSuccessAt, SyncRun? lastRun, TimeSpan staleAfter)
    {
        if (lastRun is { Status: SyncRunStatus.Failed, FailureReason: SyncFailureReason.GuardTriggered })
        {
            return new SyncActionAdvice(
                true,
                $"garde-fou de retrait massif déclenché : {lastRun.WouldDiscontinue} produits sur {lastRun.ActiveBefore} seraient retirés",
                $"Vérifier le fournisseur avant de forcer la synchronisation ; si le retrait est légitime : {ForceSyncRoute}.");
        }

        if (lastSuccessAt is null)
        {
            return lastRun is { Status: SyncRunStatus.Failed }
                ? new SyncActionAdvice(
                    true,
                    "aucune synchronisation n'a encore réussi",
                    "Consulter la cause de l'échec dans l'historique des synchronisations, corriger puis déclencher une synchronisation manuelle.")
                : new SyncActionAdvice(false, null, null);
        }

        if (now - lastSuccessAt.Value > staleAfter)
        {
            return new SyncActionAdvice(
                true,
                $"aucune synchronisation réussie depuis plus de {FormatHours(staleAfter)}",
                "Consulter la cause des échecs dans l'historique des synchronisations, corriger puis déclencher une synchronisation manuelle.");
        }

        return new SyncActionAdvice(false, null, null);
    }

    private static string FormatHours(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"{duration.TotalHours:0.##} h");
}
