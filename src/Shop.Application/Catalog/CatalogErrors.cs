using Shop.Domain.Common;

namespace Shop.Application.Catalog;

/// <summary>Erreurs des cas d'usage du catalogue, avec des messages en français.</summary>
public static class CatalogErrors
{
    public const string ProviderErrorReason = "provider_error";
    public const string InvalidSnapshotReason = "invalid_snapshot";

    public static DomainError SyncAlreadyRunning() =>
        new(ErrorCodes.SyncAlreadyRunning,
            "Une synchronisation du catalogue est déjà en cours (sur cette instance ou une autre). Réessayez une fois celle-ci terminée.");

    public static DomainError ProviderUnavailable(string detail) =>
        new(ErrorCodes.ProviderUnavailable,
            $"Le fournisseur de catalogue est indisponible : {detail}. Le catalogue local est inchangé.",
            new Dictionary<string, object?> { ["reason"] = ProviderErrorReason });

    public static DomainError GuardTriggered(int activeProducts, int wouldDiscontinue) =>
        new(ErrorCodes.SyncGuardTriggered,
            $"Synchronisation bloquée par le garde-fou : {wouldDiscontinue} produits sur {activeProducts} seraient retirés. " +
            "Le catalogue local est inchangé. Vérifiez le fournisseur ; si le retrait est légitime, forcez la synchronisation (force=true).",
            new Dictionary<string, object?>
            {
                ["activeProducts"] = activeProducts,
                ["wouldDiscontinue"] = wouldDiscontinue,
            });

    public const string InterruptedReason = "interrupted";

    public static DomainError SyncInterrupted() =>
        new(ErrorCodes.ServiceUnavailable,
            "La synchronisation a été interrompue : le verrou de synchronisation a été perdu (connexion rompue). " +
            "Aucune modification n'a été enregistrée par ce run ; réessayez.",
            new Dictionary<string, object?> { ["reason"] = InterruptedReason });

    public static DomainError DatabaseFailure() =>
        new(ErrorCodes.ServiceUnavailable,
            "La base de données a échoué pendant l'application de la synchronisation : aucune modification n'a été enregistrée.");

    public static DomainError InvalidLimit(int min, int max) =>
        DomainError.Validation(
            $"Le paramètre limit doit être compris entre {min} et {max}.",
            ValidationDetails("limit", $"Valeur attendue entre {min} et {max}."));

    public static DomainError InvalidCursor() =>
        DomainError.Validation(
            "Le curseur de pagination est invalide : utilisez la valeur nextCursor renvoyée par la page précédente.",
            ValidationDetails("cursor", "Curseur invalide."));

    private static Dictionary<string, object?> ValidationDetails(string field, string message) =>
        new() { ["errors"] = new Dictionary<string, string[]> { [field] = [message] } };
}
