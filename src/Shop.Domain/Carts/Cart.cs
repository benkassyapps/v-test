using Shop.Domain.Common;

namespace Shop.Domain.Carts;

/// <summary>
/// Agrégat panier : seule porte d'entrée pour modifier ses lignes (specs/cart/design.md §2).
/// Chaque commande vérifie toutes ses règles AVANT de modifier quoi que ce soit : une commande rejetée
/// laisse l'agrégat strictement identique (CART-R7.3). Seule la version de la LIGNE visée est comparée,
/// jamais la version globale du panier (CART-R9).
/// </summary>
public sealed class Cart
{
    private readonly List<CartLine> _lines = [];

    private Cart() => Lines = _lines.AsReadOnly();

    public Guid Id { get; private set; }

    /// <summary>Propriétaire du panier (CART-R2).</summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Version globale : 0 à la création, +1 exactement par modification effective acceptée (CART-R7).</summary>
    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Vue en lecture seule (non convertible en List) : seules les commandes de l'agrégat modifient les lignes.</summary>
    public IReadOnlyList<CartLine> Lines { get; }

    /// <summary>Montant total calculé sur les valeurs conservées, produits retirés compris (CART-R6.3, CART-R11.1).</summary>
    public decimal Total => _lines.Sum(line => line.LineTotal);

    public static Cart Create(Guid id, string userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new Cart { Id = id, UserId = userId, Version = 0, CreatedAt = now, UpdatedAt = now };
    }

    public CartLine? FindLine(Guid lineId) => _lines.Find(line => line.Id == lineId);

    /// <summary>
    /// Ajoute un produit ou cumule sa quantité sur la ligne existante (CART-R3, R4, R5, R6, R11).
    /// </summary>
    /// <param name="productId">Identifiant du produit demandé.</param>
    /// <param name="product">Produit lu dans le catalogue, ou null s'il n'existe pas.</param>
    /// <param name="quantity">Quantité à ajouter.</param>
    /// <param name="newLineId">Identifiant à utiliser si une nouvelle ligne est créée.</param>
    /// <param name="now">Horodatage de la modification.</param>
    public Result<CartChange> AddItem(int productId, ProductSnapshot? product, int quantity, Guid newLineId, DateTimeOffset now)
    {
        if (product is not null && product.Id != productId)
        {
            throw new ArgumentException("Le produit lu ne correspond pas au produit demandé.", nameof(product));
        }

        if (!CartRules.IsValidQuantity(quantity))
        {
            return CartErrors.QuantityOutOfRange(quantity);
        }

        if (product is null || !product.IsActive)
        {
            return CartErrors.ProductUnavailable(productId);
        }

        var existing = _lines.Find(line => line.ProductId == product.Id);
        if (existing is not null)
        {
            if (existing.Quantity + quantity > CartRules.MaxQuantity)
            {
                return CartErrors.QuantityLimitExceeded(existing.Quantity, quantity);
            }

            // Cumul : le nom et le prix conservés au premier ajout ne changent pas (CART-R6.2).
            existing.ChangeQuantity(existing.Quantity + quantity);
        }
        else
        {
            _lines.Add(CartLine.Create(newLineId, Id, product, quantity));
        }

        Touch(now);
        return CartChange.Changed;
    }

    /// <summary>
    /// Remplace la quantité d'une ligne (CART-R16), à condition que la version consultée soit toujours la bonne (CART-R8).
    /// </summary>
    /// <param name="lineId">Ligne visée.</param>
    /// <param name="quantity">Nouvelle quantité (remplace l'ancienne).</param>
    /// <param name="expectedVersion">Version de la ligne lue par le client.</param>
    /// <param name="productActive">Le produit de la ligne est-il encore actif dans le catalogue ?</param>
    /// <param name="now">Horodatage de la modification.</param>
    public Result<CartChange> ChangeQuantity(Guid lineId, int quantity, long expectedVersion, bool productActive, DateTimeOffset now)
    {
        if (!CartRules.IsValidQuantity(quantity))
        {
            return CartErrors.QuantityOutOfRange(quantity);
        }

        var line = FindLine(lineId);
        if (line is null)
        {
            return CartErrors.LineNotFound(lineId);
        }

        if (line.Version != expectedVersion)
        {
            return CartErrors.VersionConflict(lineId, line.Version);
        }

        if (quantity == line.Quantity)
        {
            // Pas de modification effective : aucune version ne change (CART-R7.4).
            return CartChange.Unchanged;
        }

        if (!productActive && quantity > line.Quantity)
        {
            // Produit retiré : réduire est permis, augmenter revient à en ajouter (CART-R11.3).
            return CartErrors.ProductUnavailable(line.ProductId);
        }

        line.ChangeQuantity(quantity);
        Touch(now);
        return CartChange.Changed;
    }

    /// <summary>Retire une ligne (CART-R17) si la version consultée est toujours la bonne (CART-R8).</summary>
    public Result<CartChange> RemoveLine(Guid lineId, long expectedVersion, DateTimeOffset now)
    {
        var line = FindLine(lineId);
        if (line is null)
        {
            return CartErrors.LineNotFound(lineId);
        }

        if (line.Version != expectedVersion)
        {
            return CartErrors.VersionConflict(lineId, line.Version);
        }

        _lines.Remove(line);
        Touch(now);
        return CartChange.Changed;
    }

    private void Touch(DateTimeOffset now)
    {
        Version++;
        UpdatedAt = now;
    }
}
