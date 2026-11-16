namespace Shop.Domain.Carts;

/// <summary>
/// Ligne de panier : un produit, sa quantité, et le nom et le prix conservés au premier ajout (CART-R6).
/// Modifiée uniquement par l'agrégat <see cref="Cart"/>.
/// </summary>
public sealed class CartLine
{
    private CartLine()
    {
    }

    /// <summary>Identifiant de la ligne (lineId) : nouveau à chaque création, même pour un produit réajouté (CART-R10.2).</summary>
    public Guid Id { get; private set; }

    public Guid CartId { get; private set; }

    public int ProductId { get; private set; }

    /// <summary>Nom du produit conservé au premier ajout.</summary>
    public string ProductName { get; private set; } = string.Empty;

    /// <summary>Prix unitaire conservé au premier ajout.</summary>
    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Version propre de la ligne : 1 à la création, +1 à chaque modification (CART-R8).</summary>
    public long Version { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;

    internal static CartLine Create(Guid id, Guid cartId, ProductSnapshot product, int quantity) =>
        new()
        {
            Id = id,
            CartId = cartId,
            ProductId = product.Id,
            ProductName = product.Name,
            UnitPrice = product.Price,
            Quantity = quantity,
            Version = 1,
        };

    internal void ChangeQuantity(int quantity)
    {
        Quantity = quantity;
        Version++;
    }
}
