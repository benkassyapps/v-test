using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Shop.Api.Errors;
using Shop.Domain.Common;

namespace Shop.Api.Auth;

/// <summary>Configuration de l'accès administrateur (section « Admin »).</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>Clé attendue dans l'en-tête X-Admin-Key. Vide : endpoints d'administration désactivés.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>Comparaison de la clé d'administration, en temps constant.</summary>
public static class AdminKey
{
    public const string HeaderName = "X-Admin-Key";

    public static bool Matches(string? configured, string? provided)
    {
        if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        // Hachage préalable : longueurs égales, donc comparaison en temps constant sans fuite de longueur.
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

/// <summary>Filtre des endpoints d'administration : 403 admin_key_invalid si la clé est absente ou invalide.</summary>
internal sealed class AdminKeyFilter(IOptionsMonitor<AdminOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var provided = context.HttpContext.Request.Headers[AdminKey.HeaderName].ToString();
        if (!AdminKey.Matches(options.CurrentValue.ApiKey, provided))
        {
            return new DomainError(ErrorCodes.AdminKeyInvalid, "Clé d'administration absente ou invalide (en-tête X-Admin-Key).")
                .ToProblem();
        }

        return await next(context);
    }
}
