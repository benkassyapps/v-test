using System.Text.RegularExpressions;

namespace Shop.Api.Auth;

/// <summary>Format accepté pour l'identifiant utilisateur de l'en-tête X-User-Id (PLT-2).</summary>
public static partial class UserIdFormat
{
    public const int MaxLength = 64;

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    // \z et non $ : en .NET, $ accepte un saut de ligne final (« alice\n » serait valide).
    [GeneratedRegex(@"\A[A-Za-z0-9._@-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
