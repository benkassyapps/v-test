using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
internal sealed class CartStore(
    IDbContextFactory<ShopDbContext> contexts,
    IOptions<CartOptions> options,
    TimeProvider clock) : ICartStore
{
    /// <summary>
    /// Deux contraintes d'unicité peuvent faire reprendre une commande : la création du panier (UX_Carts_UserId) puis
    /// l'enregistrement de la clé d'idempotence (PK_IdempotencyKeys). Une collision sur chacune, successivement,
    /// nécessite deux reprises ; après elles, le panier et la clé de l'autre appel existent forcément.
    /// </summary>
    private const int MaxAttempts = 3;

    public async Task<CartCommandResponse> ExecuteAsync(
        string userId,
        bool createIfMissing,
        CartCommand command,
        IdempotencyRequest? idempotency,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteOnceAsync(userId, createIfMissing, command, idempotency, cancellationToken);
            }
            catch (DbUpdateException exception) when (attempt < MaxAttempts && IsUniqueViolation(exception))
            {
                // Création concurrente du même panier, ou enregistrement concurrent de la même clé d'idempotence :
                // rien n'a été validé par cette tentative, on la reprend entièrement ; elle trouvera le panier et la clé.
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
            var lockedId = await LockAsync(db, userId, cancellationToken);
            var (cart, created) = await LoadOrCreateAsync(db, lockedId, userId, createIfMissing: true, cancellationToken);
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

    private async Task<CartCommandResponse> ExecuteOnceAsync(
        string userId,
        bool createIfMissing,
        CartCommand command,
        IdempotencyRequest? idempotency,
        CancellationToken cancellationToken)
    {
        // Volontairement SANS rejeu sur erreur transitoire : si le commit réussit mais que son accusé est perdu,
        // un rejeu appliquerait la commande deux fois (quantité doublée). Une erreur transitoire remonte en 503 ;
        // c'est la clé d'idempotence qui permet au CLIENT de réessayer sans risque (CART-R18).
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var lockedId = await LockAsync(db, userId, cancellationToken);

        // Clé d'idempotence lue APRÈS le verrou du panier et AVANT tout chargement : deux envois de la même clé sont
        // sérialisés, le second voit la clé enregistrée par le premier. Rejeu ou refus : rien n'est chargé ni écrit.
        if (idempotency is not null && await FindReplayAsync(db, userId, idempotency, cancellationToken) is { } replay)
        {
            return replay;
        }

        var (cart, created) = await LoadOrCreateAsync(db, lockedId, userId, createIfMissing, cancellationToken);
        Result<CartChange> change = cart is null ? CartErrors.CartNotFound() : await command(cart, new LockingProductLookup(db), cancellationToken);

        var mustCommit = false;
        if (change.IsSuccess && (change.Value == CartChange.Changed || created))
        {
            await db.SaveChangesAsync(cancellationToken);
            mustCommit = true;
        }

        // Avec une clé, la réponse doit être connue AVANT le commit pour être enregistrée avec lui. Sans clé, la vue est
        // construite après le commit, une fois les verrous libérés (simple lecture de la disponibilité des produits).
        Result<CartView>? result = null;
        if (idempotency is not null)
        {
            result = change.IsSuccess ? await ViewAsync(db, cart!, cancellationToken) : change.Error;
            if (!change.IsSuccess)
            {
                // Refus métier : seule la clé est enregistrée, jamais les modifications annulées (ni une création implicite).
                db.ChangeTracker.Clear();
            }

            var now = clock.GetUtcNow();
            db.Add(new IdempotencyRecord
            {
                UserId = userId,
                Key = idempotency.Key,
                RequestHash = idempotency.RequestHash,
                Outcome = StoredOutcome.Serialize(result),
                CreatedAt = now,
                ExpiresAt = now + options.Value.IdempotencyKeyTtl,
            });
            await db.SaveChangesAsync(cancellationToken);
            mustCommit = true;
        }

        if (mustCommit)
        {
            // Panier et clé validés ENSEMBLE : un accusé de commit perdu n'entraînera jamais de double application.
            await transaction.CommitAsync(cancellationToken);
        }

        // Sinon (refus sans clé, ou aucune modification effective) : rollback à la libération de la transaction.
        return result ?? (change.IsSuccess ? await ViewAsync(db, cart!, cancellationToken) : change.Error);
    }

    /// <summary>
    /// Réponse à rejouer si la clé a déjà été utilisée (même requête), refus si elle l'a été pour une autre requête,
    /// null sinon. Une clé expirée est supprimée et la commande traitée normalement.
    /// </summary>
    private async Task<CartCommandResponse?> FindReplayAsync(
        ShopDbContext db,
        string userId,
        IdempotencyRequest idempotency,
        CancellationToken cancellationToken)
    {
        var existing = await db.Set<IdempotencyRecord>().AsNoTracking()
            .SingleOrDefaultAsync(record => record.UserId == userId && record.Key == idempotency.Key, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (existing.ExpiresAt <= clock.GetUtcNow())
        {
            await db.Set<IdempotencyRecord>()
                .Where(record => record.UserId == userId && record.Key == idempotency.Key)
                .ExecuteDeleteAsync(cancellationToken);
            return null;
        }

        return existing.RequestHash == idempotency.RequestHash
            ? new CartCommandResponse(StoredOutcome.Deserialize(existing.Outcome), Replayed: true)
            : new CartCommandResponse(CartErrors.IdempotencyKeyReused(), Replayed: false);
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
    private static async Task<Guid?> LockAsync(ShopDbContext db, string userId, CancellationToken cancellationToken)
    {
        var lockedIds = await db.Database
            .SqlQuery<Guid>($"SELECT Id AS [Value] FROM dbo.Carts WITH (UPDLOCK, ROWLOCK) WHERE UserId = {userId}")
            .ToListAsync(cancellationToken);
        return lockedIds is [var id] ? id : null;
    }

    /// <summary>Charge le panier verrouillé (instruction distincte, après le verrou) ou le crée de façon optimiste.</summary>
    private async Task<(Cart? Cart, bool Created)> LoadOrCreateAsync(
        ShopDbContext db,
        Guid? lockedId,
        string userId,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        if (lockedId is { } id)
        {
            var existing = await db.Set<Cart>().Include(cart => cart.Lines).SingleAsync(cart => cart.Id == id, cancellationToken);
            return (existing, false);
        }

        if (!createIfMissing)
        {
            return (null, false);
        }

        var now = clock.GetUtcNow();
        var created = Cart.Create(Guid.CreateVersion7(now), userId, now);
        db.Add(created);
        return (created, true);
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
