using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shop.Application.Carts;
using Shop.Domain.Carts;
using Shop.Domain.Catalog;
using Shop.Domain.Common;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Carts;

/// <summary>
/// Unité de travail transactionnelle du panier (specs/cart/design.md §3). Invariant (constitution §3.1) : toute
/// écriture d'un panier existant commence par verrouiller sa ligne <c>Carts</c> (UPDLOCK) ; les écritures d'un même
/// panier sont donc sérialisées, toutes instances confondues, et chaque commande décide sur l'état validé le plus récent.
/// </summary>
internal sealed class CartStore(IDbContextFactory<ShopDbContext> contexts, TimeProvider clock) : ICartStore
{
    /// <summary>Une reprise suffit : après une collision de création, le panier existe forcément.</summary>
    private const int MaxAttempts = 2;

    public async Task<Result<CartView>> ExecuteAsync(
        string userId,
        bool createIfMissing,
        CartCommand command,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(userId, createIfMissing, command, cancellationToken);
            }
            catch (DbUpdateException exception) when (attempt < MaxAttempts && IsUniqueViolation(exception))
            {
                // Création concurrente du même panier (création optimiste, voir LockOrCreateAsync) : rien n'a été validé
                // par cette tentative, on la reprend entièrement ; elle trouvera et verrouillera le panier créé par l'autre.
            }
        }
    }

    public async Task<(CartView Cart, bool Created)> GetOrCreateAsync(string userId, CancellationToken cancellationToken)
    {
        // Pas de rejeu automatique : après un commit dont l'accusé est perdu, un rejeu répondrait « existant » (200)
        // pour un panier que cet appel a pourtant créé (201). Une erreur transitoire remonte en 503.
        try
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var (cart, created) = await LockOrCreateAsync(db, userId, createIfMissing: true, cancellationToken);
            if (created)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return (await ViewAsync(db, cart!, cancellationToken), created);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Un appel concurrent vient de créer le panier : c'est donc un panier existant.
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            var existing = await db.Set<Cart>().AsNoTracking().Include(cart => cart.Lines).SingleAsync(cart => cart.UserId == userId, cancellationToken);
            return (await ViewAsync(db, existing, cancellationToken), false);
        }
    }

    private async Task<Result<CartView>> ExecuteOnceAsync(
        string userId,
        bool createIfMissing,
        CartCommand command,
        CancellationToken cancellationToken)
    {
        // Volontairement SANS rejeu sur erreur transitoire : si le commit réussit mais que son accusé est perdu,
        // un rejeu appliquerait la commande deux fois (quantité doublée). Une erreur transitoire remonte en 503.
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var (cart, created) = await LockOrCreateAsync(db, userId, createIfMissing, cancellationToken);
        if (cart is null)
        {
            return CartErrors.CartNotFound();
        }

        var result = await command(cart, new LockingProductLookup(db), cancellationToken);
        if (!result.IsSuccess)
        {
            // Rollback à la libération de la transaction : rien n'est modifié, pas même la création implicite.
            return result.Error;
        }

        if (result.Value == CartChange.Changed || created)
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Vue construite après le commit (verrous déjà libérés) : la disponibilité des produits est une simple lecture.
        return await ViewAsync(db, cart, cancellationToken);
    }

    /// <summary>
    /// Verrouille puis charge le panier existant, en DEUX instructions : les lignes sont lues une fois le verrou obtenu,
    /// donc dans l'état validé le plus récent, quel que soit le plan d'exécution (RCSI : une lecture non verrouillante
    /// voit l'instantané de son instruction).
    /// Panier absent : création OPTIMISTE (simple ajout, inséré au SaveChanges). Si un appel concurrent crée le même
    /// panier, l'index unique UX_Carts_UserId refuse le second et la commande est reprise (voir ExecuteAsync).
    /// Pas de HOLDLOCK : son verrou de plage sur une clé absente bloquerait aussi la clé SUIVANTE de l'index,
    /// c'est-à-dire le panier d'un autre utilisateur, pendant toute la transaction.
    /// </summary>
    private async Task<(Cart? Cart, bool Created)> LockOrCreateAsync(
        ShopDbContext db,
        string userId,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var lockedIds = await db.Database
            .SqlQuery<Guid>($"SELECT Id AS [Value] FROM dbo.Carts WITH (UPDLOCK, ROWLOCK) WHERE UserId = {userId}")
            .ToListAsync(cancellationToken);

        if (lockedIds.Count == 0)
        {
            if (!createIfMissing)
            {
                return (null, false);
            }

            var now = clock.GetUtcNow();
            var cart = Cart.Create(Guid.CreateVersion7(now), userId, now);
            db.Add(cart);
            return (cart, true);
        }

        var existing = await db.Set<Cart>().Include(cart => cart.Lines).SingleAsync(cart => cart.Id == lockedIds[0], cancellationToken);
        return (existing, false);
    }

    /// <summary>Vue du panier avec la disponibilité actuelle des produits (même construction que la lecture).</summary>
    internal static async Task<CartView> ViewAsync(ShopDbContext db, Cart cart, CancellationToken cancellationToken)
    {
        var productIds = cart.Lines.Select(line => line.ProductId).Distinct().ToList();
        var active = productIds.Count == 0
            ? []
            : await db.Set<Product>().AsNoTracking()
                .Where(product => productIds.Contains(product.Id) && product.Status == ProductStatus.Active)
                .Select(product => product.Id)
                .ToListAsync(cancellationToken);

        return CartView.From(cart, active.ToHashSet());
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    /// <summary>
    /// Lecture d'un produit avec verrou partagé conservé jusqu'au commit (REPEATABLEREAD) : une synchronisation ne peut
    /// pas retirer ce produit entre la vérification et le commit de la commande. Ordre des verrous : Carts puis Products,
    /// la synchronisation ne verrouillant que Products : pas de cycle, donc pas d'interblocage.
    /// </summary>
    private sealed class LockingProductLookup(ShopDbContext db) : ICartProductLookup
    {
        public async Task<ProductSnapshot?> FindAsync(int productId, CancellationToken cancellationToken)
        {
            var rows = await db.Database
                .SqlQuery<ProductRow>($"SELECT Id, Name, Price, Status FROM dbo.Products WITH (REPEATABLEREAD) WHERE Id = {productId}")
                .ToListAsync(cancellationToken);

            return rows is [var row]
                ? new ProductSnapshot(row.Id, row.Name, row.Price, row.Status == (byte)ProductStatus.Active)
                : null;
        }
    }

#pragma warning disable CA1812 // Instancié par EF Core (SqlQuery).
    private sealed class ProductRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public decimal Price { get; init; }

        public byte Status { get; init; }
    }
#pragma warning restore CA1812
}
