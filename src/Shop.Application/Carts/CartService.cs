using Shop.Domain.Carts;
using Shop.Domain.Common;

namespace Shop.Application.Carts;

/// <summary>
/// Cas d'usage du panier personnel (CART-R13 à CART-R17). L'utilisateur est toujours passé explicitement : aucune
/// opération n'accepte d'identifiant de panier, chacune vise le panier de l'utilisateur courant (CART-R2).
/// Les règles métier vivent dans l'agrégat <see cref="Cart"/> ; la concurrence est gérée par <see cref="ICartStore"/>.
/// </summary>
public sealed class CartService(ICartStore store, ICartReader reader, TimeProvider clock)
{
    /// <summary>Crée le panier ou renvoie l'existant, sans le modifier (CART-R13).</summary>
    public Task<(CartView Cart, bool Created)> GetOrCreateAsync(string userId, CancellationToken cancellationToken) =>
        store.GetOrCreateAsync(userId, cancellationToken);

    /// <summary>Consulte le panier (CART-R14).</summary>
    public async Task<Result<CartView>> GetAsync(string userId, CancellationToken cancellationToken) =>
        await reader.FindAsync(userId, cancellationToken) is { } cart ? cart : CartErrors.CartNotFound();

    /// <summary>Ajoute un produit ou cumule sa quantité ; crée le panier implicitement si besoin (CART-R15).</summary>
    public Task<Result<CartView>> AddItemAsync(string userId, int productId, int quantity, CancellationToken cancellationToken) =>
        store.ExecuteAsync(userId, createIfMissing: true, async (cart, products, token) =>
        {
            var product = await products.FindAsync(productId, token);
            var now = clock.GetUtcNow();
            return cart.AddItem(productId, product, quantity, Guid.CreateVersion7(now), now);
        }, cancellationToken);

    /// <summary>Remplace la quantité d'une ligne, à partir de la version consultée (CART-R8, CART-R16).</summary>
    public Task<Result<CartView>> ChangeQuantityAsync(
        string userId, Guid lineId, int quantity, long expectedVersion, CancellationToken cancellationToken) =>
        store.ExecuteAsync(userId, createIfMissing: false, async (cart, products, token) =>
        {
            // La disponibilité du produit n'importe que pour une AUGMENTATION acceptable (CART-R11.3) : on ne lit (et ne
            // verrouille) le produit que dans ce cas, pour ne pas attendre inutilement une synchronisation en cours.
            var line = cart.FindLine(lineId);
            var mayIncrease = line is not null && line.Version == expectedVersion && quantity > line.Quantity;
            var productActive = !mayIncrease || (await products.FindAsync(line!.ProductId, token))?.IsActive == true;
            return cart.ChangeQuantity(lineId, quantity, expectedVersion, productActive, clock.GetUtcNow());
        }, cancellationToken);

    /// <summary>Supprime une ligne, à partir de la version consultée (CART-R8, CART-R17).</summary>
    public Task<Result<CartView>> RemoveLineAsync(string userId, Guid lineId, long expectedVersion, CancellationToken cancellationToken) =>
        store.ExecuteAsync(userId, createIfMissing: false, (cart, _, _) =>
            Task.FromResult(cart.RemoveLine(lineId, expectedVersion, clock.GetUtcNow())), cancellationToken);
}
