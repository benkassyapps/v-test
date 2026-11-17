using Shop.Domain.Carts;
using Shop.Domain.Common;

namespace Shop.Application.Carts;

/// <summary>Ligne de panier telle que la voit le client (CART-R14).</summary>
public sealed record CartLineView(
    Guid LineId,
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    long Version,
    bool ProductAvailable);

/// <summary>Panier complet tel que le voit le client (CART-R14).</summary>
public sealed record CartView(Guid Id, long Version, decimal Total, IReadOnlyList<CartLineView> Lines)
{
    /// <summary>
    /// Seule construction de la vue (lecture et réponses des commandes) : montants issus de l'agrégat, lignes dans un
    /// ordre stable (nom conservé, puis identifiant) quelle que soit la requête qui les a chargées.
    /// </summary>
    public static CartView From(Cart cart, IReadOnlySet<int> activeProductIds) =>
        new(
            cart.Id,
            cart.Version,
            cart.Total,
            [.. cart.Lines
                .OrderBy(line => line.ProductName, StringComparer.Ordinal)
                .ThenBy(line => line.Id)
                .Select(line => new CartLineView(
                line.Id,
                line.ProductId,
                line.ProductName,
                line.UnitPrice,
                line.Quantity,
                line.LineTotal,
                line.Version,
                activeProductIds.Contains(line.ProductId)))]);
}

/// <summary>Accès aux produits du catalogue PENDANT la transaction d'une commande de panier.</summary>
public interface ICartProductLookup
{
    /// <summary>
    /// Lit le produit et le verrouille en lecture jusqu'à la fin de la transaction : une synchronisation ne peut pas
    /// le retirer entre la vérification et le commit (CART-R3). Null si le produit n'existe pas.
    /// </summary>
    Task<ProductSnapshot?> FindAsync(int productId, CancellationToken cancellationToken);
}

/// <summary>Commande appliquée à l'agrégat dans la transaction verrouillée.</summary>
public delegate Task<Result<CartChange>> CartCommand(Cart cart, ICartProductLookup products, CancellationToken cancellationToken);

/// <summary>Persistance transactionnelle du panier (specs/cart/design.md §3).</summary>
public interface ICartStore
{
    /// <summary>
    /// Exécute une commande dans UNE transaction qui commence par verrouiller le panier de l'utilisateur (UPDLOCK) :
    /// toutes les écritures d'un même panier sont sérialisées, toutes instances confondues. Crée le panier si
    /// <paramref name="createIfMissing"/> (création implicite optimiste, CART-R15.2). Une commande rejetée annule tout,
    /// y compris la création implicite. Jamais rejouée sur erreur transitoire (seulement après une collision de création,
    /// lorsque rien n'a été validé). Renvoie le panier après la commande.
    /// Avec une clé d'idempotence (CART-R18) : la clé et la réponse sont enregistrées dans la même transaction ; une
    /// clé déjà utilisée rejoue la réponse d'origine (même requête) ou est refusée (requête différente).
    /// </summary>
    Task<CartCommandResponse> ExecuteAsync(
        string userId,
        bool createIfMissing,
        CartCommand command,
        IdempotencyRequest? idempotency,
        CancellationToken cancellationToken);

    /// <summary>Crée le panier s'il n'existe pas, sinon le renvoie tel quel (CART-R1, CART-R13). Idempotent.</summary>
    Task<(CartView Cart, bool Created)> GetOrCreateAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Lecture du panier (sans verrou, instantané cohérent).</summary>
public interface ICartReader
{
    Task<CartView?> FindAsync(string userId, CancellationToken cancellationToken);
}
