using Microsoft.AspNetCore.Http.HttpResults;
using Shop.Domain.Common;

namespace Shop.Api.Errors;

/// <summary>
/// Table unique code d'erreur → statut HTTP et titre (PLT-3, specs/platform/design.md §5).
/// Une même situation produit toujours le même statut.
/// </summary>
public static class ProblemMapping
{
    private const string TypeBaseUri = "https://errors.shop.local/";

    private static readonly Dictionary<string, (int Status, string Title)> Table = new(StringComparer.Ordinal)
    {
        [ErrorCodes.ValidationFailed] = (StatusCodes.Status400BadRequest, "Requête invalide"),
        [ErrorCodes.UserNotIdentified] = (StatusCodes.Status401Unauthorized, "Utilisateur non identifié"),
        [ErrorCodes.AdminKeyInvalid] = (StatusCodes.Status403Forbidden, "Accès administrateur refusé"),
        [ErrorCodes.NotFound] = (StatusCodes.Status404NotFound, "Ressource introuvable"),
        [ErrorCodes.CartNotFound] = (StatusCodes.Status404NotFound, "Panier introuvable"),
        [ErrorCodes.CartLineNotFound] = (StatusCodes.Status404NotFound, "Ligne de panier introuvable"),
        [ErrorCodes.MethodNotAllowed] = (StatusCodes.Status405MethodNotAllowed, "Méthode non autorisée"),
        [ErrorCodes.CartLineVersionConflict] = (StatusCodes.Status409Conflict, "Conflit de version"),
        [ErrorCodes.SyncAlreadyRunning] = (StatusCodes.Status409Conflict, "Synchronisation déjà en cours"),
        [ErrorCodes.ProductUnavailable] = (StatusCodes.Status422UnprocessableEntity, "Produit indisponible"),
        [ErrorCodes.QuantityLimitExceeded] = (StatusCodes.Status422UnprocessableEntity, "Quantité maximale dépassée"),
        [ErrorCodes.SyncGuardTriggered] = (StatusCodes.Status422UnprocessableEntity, "Synchronisation bloquée par le garde-fou"),
        [ErrorCodes.InternalError] = (StatusCodes.Status500InternalServerError, "Erreur interne"),
        [ErrorCodes.ProviderUnavailable] = (StatusCodes.Status502BadGateway, "Fournisseur indisponible"),
        [ErrorCodes.ServiceUnavailable] = (StatusCodes.Status503ServiceUnavailable, "Service temporairement indisponible"),
    };

    public static IReadOnlyCollection<string> KnownCodes => Table.Keys;

    public static (int Status, string Title) Describe(string code) =>
        Table.TryGetValue(code, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Code d'erreur absent de la table ProblemMapping.");

    public static string TypeFor(string code) => TypeBaseUri + code;

    /// <summary>Code attribué à une réponse d'erreur produite hors du code métier (routage, validation, authentification).</summary>
    public static string DefaultCodeFor(int status) =>
        status switch
        {
            StatusCodes.Status401Unauthorized => ErrorCodes.UserNotIdentified,
            StatusCodes.Status403Forbidden => ErrorCodes.AdminKeyInvalid,
            StatusCodes.Status404NotFound => ErrorCodes.NotFound,
            StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
            StatusCodes.Status503ServiceUnavailable => ErrorCodes.ServiceUnavailable,
            >= 500 => ErrorCodes.InternalError,
            _ => ErrorCodes.ValidationFailed,
        };

    /// <summary>Convertit une erreur métier en réponse ProblemDetails (les détails deviennent des extensions).</summary>
    public static ProblemHttpResult ToProblem(this DomainError error)
    {
        var (status, title) = Describe(error.Code);
        var extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = error.Code };
        if (error.Details is not null)
        {
            foreach (var (key, value) in error.Details)
            {
                extensions[key] = value;
            }
        }

        return TypedResults.Problem(
            detail: error.Message,
            statusCode: status,
            title: title,
            type: TypeFor(error.Code),
            extensions: extensions);
    }
}
