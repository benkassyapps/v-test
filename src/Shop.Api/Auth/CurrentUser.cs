using System.Security.Claims;

namespace Shop.Api.Auth;

/// <summary>Lecture de l'utilisateur courant à partir du principal authentifié.</summary>
public static class CurrentUser
{
    public const string UserIdClaim = "sub";

    /// <summary>
    /// Identifiant de l'utilisateur courant. N'est appelé que sur des endpoints protégés par RequireAuthorization :
    /// son absence est une erreur de programmation.
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(UserIdClaim)
        ?? throw new InvalidOperationException("Endpoint sans utilisateur identifié : RequireAuthorization manquant ?");
}
