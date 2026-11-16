using Microsoft.EntityFrameworkCore;
using Shop.Application.Carts;
using Shop.Domain.Carts;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Carts;

/// <summary>
/// Lecture du panier (CART-R14), sans verrou. Le panier et ses lignes sont lus en UNE instruction : sous
/// READ_COMMITTED_SNAPSHOT, la version et les lignes correspondent toujours au même commit. La vue est construite par
/// le même code que celle des réponses de commandes (<see cref="CartView.From"/>) : une seule définition des montants
/// et de la disponibilité. Lecture idempotente : rejouée en cas d'erreur transitoire.
/// </summary>
internal sealed class CartReader(IDbContextFactory<ShopDbContext> contexts, TransientRetry retry) : ICartReader
{
    public Task<CartView?> FindAsync(string userId, CancellationToken cancellationToken) =>
        retry.ExecuteAsync<CartView?>(async token =>
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var cart = await db.Set<Cart>().AsNoTracking()
                .Include(cart => cart.Lines)
                .SingleOrDefaultAsync(cart => cart.UserId == userId, token);

            return cart is null ? null : await CartStore.ViewAsync(db, cart, token);
        }, cancellationToken);
}
