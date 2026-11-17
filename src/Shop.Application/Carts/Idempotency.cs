using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Shop.Domain.Common;

namespace Shop.Application.Carts;

/// <summary>Clé d'idempotence d'une commande et empreinte de la requête qu'elle désigne (CART-R18).</summary>
public sealed record IdempotencyRequest(string Key, string RequestHash);

/// <summary>Réponse d'une commande de panier : résultat, et indication d'un rejeu de la réponse d'origine.</summary>
public sealed record CartCommandResponse(Result<CartView> Result, bool Replayed)
{
    public static implicit operator CartCommandResponse(Result<CartView> result) => new(result, Replayed: false);
}

/// <summary>Configuration du panier (section « Cart »).</summary>
public sealed class CartOptions
{
    public const string SectionName = "Cart";

    /// <summary>Durée de conservation d'une clé d'idempotence (CART-R18.7).</summary>
    public TimeSpan IdempotencyKeyTtl { get; set; } = TimeSpan.FromHours(24);
}

/// <summary>Format accepté pour l'en-tête Idempotency-Key : 1 à 100 caractères parmi lettres, chiffres et « . _ : - ».</summary>
public static partial class IdempotencyKeyFormat
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";
    public const int MaxLength = 100;

    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    [GeneratedRegex(@"\A[A-Za-z0-9._:-]{1,100}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>
/// Empreinte canonique d'une commande (SHA-256) : une clé réutilisée pour une requête différente — autre commande,
/// autre produit, autre quantité ou autre version attendue — est détectée et refusée (CART-R18.4).
/// </summary>
public static class CartRequestFingerprint
{
    public static string Add(int productId, int quantity) =>
        Hash(FormattableString.Invariant($"add|{productId}|{quantity}"));

    public static string Change(Guid lineId, int quantity, long expectedVersion) =>
        Hash(FormattableString.Invariant($"change|{lineId:N}|{quantity}|{expectedVersion}"));

    public static string Remove(Guid lineId, long expectedVersion) =>
        Hash(FormattableString.Invariant($"remove|{lineId:N}|{expectedVersion}"));

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLower(CultureInfo.InvariantCulture);
}
