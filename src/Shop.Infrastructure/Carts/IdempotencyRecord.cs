using System.Text.Json;
using Shop.Application.Carts;
using Shop.Domain.Common;

namespace Shop.Infrastructure.Carts;

/// <summary>
/// Clé d'idempotence enregistrée avec la réponse qu'elle a produite (CART-R18). Modèle de persistance, sans règle
/// métier : enregistrée dans la même transaction que la commande du panier.
/// </summary>
internal sealed class IdempotencyRecord
{
    public string UserId { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>Empreinte de la requête (SHA-256 hexadécimal du contenu canonique de la commande).</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Réponse produite, sérialisée (panier rendu ou erreur métier).</summary>
    public string Outcome { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Réponse enregistrée pour être rejouée à l'identique.</summary>
internal sealed record StoredOutcome(
    CartView? Cart,
    string? ErrorCode,
    string? ErrorMessage,
    Dictionary<string, JsonElement>? ErrorDetails)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(Result<CartView> result) =>
        JsonSerializer.Serialize(
            result.IsSuccess
                ? new StoredOutcome(result.Value, null, null, null)
                : new StoredOutcome(
                    null,
                    result.Error.Code,
                    result.Error.Message,
                    result.Error.Details?.ToDictionary(entry => entry.Key, entry => JsonSerializer.SerializeToElement(entry.Value, Json))),
            Json);

    public static Result<CartView> Deserialize(string outcome)
    {
        var stored = JsonSerializer.Deserialize<StoredOutcome>(outcome, Json)
                     ?? throw new InvalidOperationException("Réponse enregistrée illisible.");
        return stored.Cart is { } cart
            ? cart
            : new DomainError(
                stored.ErrorCode!,
                stored.ErrorMessage!,
                stored.ErrorDetails?.ToDictionary(entry => entry.Key, entry => (object?)entry.Value));
    }
}
