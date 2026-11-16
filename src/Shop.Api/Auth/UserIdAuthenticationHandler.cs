using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shop.Api.Errors;
using Shop.Domain.Common;

namespace Shop.Api.Auth;

/// <summary>
/// Identification de l'utilisateur courant par l'en-tête X-User-Id (PLT-2).
/// Ce n'est PAS une authentification : limite assumée de l'exercice. Le schéma est isolé ici pour pouvoir
/// être remplacé (ex. AddJwtBearer) sans toucher aux endpoints, qui ne lisent que le claim « sub ».
/// </summary>
internal sealed class UserIdAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IProblemDetailsService problemDetails)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "UserIdHeader";
    public const string HeaderName = "X-User-Id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = values.Count == 1 ? values[0] : null;
        if (!UserIdFormat.IsValid(userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Identifiant utilisateur invalide."));
        }

        var identity = new ClaimsIdentity([new Claim(CurrentUser.UserIdClaim, userId!)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var present = Request.Headers.ContainsKey(HeaderName);
        var detail = present
            ? $"L'en-tête {HeaderName} est invalide : 1 à {UserIdFormat.MaxLength} caractères parmi lettres, chiffres et « . _ - @ »."
            : $"L'en-tête {HeaderName} est obligatoire pour identifier l'utilisateur.";

        var (status, title) = ProblemMapping.Describe(ErrorCodes.UserNotIdentified);
        Response.StatusCode = status;
        // TryWriteAsync : si le client n'accepte pas le JSON (Accept: text/plain…), on renvoie le 401 sans corps
        // plutôt que de lever une exception qui le transformerait en 500.
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = ProblemMapping.TypeFor(ErrorCodes.UserNotIdentified),
                Detail = detail,
                Extensions = { ["code"] = ErrorCodes.UserNotIdentified },
            },
        });
    }
}
