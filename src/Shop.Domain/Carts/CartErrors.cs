using Shop.Domain.Common;

namespace Shop.Domain.Carts;

/// <summary>Erreurs métier du panier, avec des messages en français destinés à l'utilisateur.</summary>
public static class CartErrors
{
    public static DomainError QuantityOutOfRange(int quantity) =>
        DomainError.Validation(
            $"La quantité doit être un entier compris entre {CartRules.MinQuantity} et {CartRules.MaxQuantity} (reçu : {quantity}).",
            new Dictionary<string, object?>
            {
                ["errors"] = new Dictionary<string, string[]>
                {
                    ["quantity"] = [$"La quantité doit être comprise entre {CartRules.MinQuantity} et {CartRules.MaxQuantity}."],
                },
            });

    public static DomainError ProductUnavailable(int productId) =>
        new(ErrorCodes.ProductUnavailable,
            $"Le produit {productId} n'existe pas ou n'est plus disponible dans le catalogue.",
            new Dictionary<string, object?> { ["productId"] = productId });

    public static DomainError QuantityLimitExceeded(int currentQuantity, int requestedQuantity) =>
        new(ErrorCodes.QuantityLimitExceeded,
            $"La quantité totale ({currentQuantity} + {requestedQuantity}) dépasserait le maximum de {CartRules.MaxQuantity} par article. Le panier n'a pas été modifié.",
            new Dictionary<string, object?>
            {
                ["currentQuantity"] = currentQuantity,
                ["maxQuantity"] = CartRules.MaxQuantity,
            });

    public static DomainError CartNotFound() =>
        new(ErrorCodes.CartNotFound, "Vous n'avez pas encore de panier.");

    public static DomainError LineNotFound(Guid lineId) =>
        new(ErrorCodes.CartLineNotFound,
            "Cette ligne n'existe pas (ou plus) dans votre panier.",
            new Dictionary<string, object?> { ["lineId"] = lineId });

    public static DomainError VersionConflict(Guid lineId, long currentVersion) =>
        new(ErrorCodes.CartLineVersionConflict,
            $"La ligne a été modifiée depuis votre dernière lecture (version actuelle : {currentVersion}). Rechargez le panier puis réessayez.",
            new Dictionary<string, object?>
            {
                ["lineId"] = lineId,
                ["currentVersion"] = currentVersion,
            });
}

/// <summary>Bornes des quantités (CART-R4).</summary>
public static class CartRules
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 999;

    public static bool IsValidQuantity(int quantity) => quantity is >= MinQuantity and <= MaxQuantity;
}
