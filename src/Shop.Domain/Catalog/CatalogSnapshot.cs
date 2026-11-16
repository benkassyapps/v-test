using Shop.Domain.Common;

namespace Shop.Domain.Catalog;

/// <summary>
/// Instantané COMPLET et valide du catalogue fournisseur (CAT-R1). Seul un instantané validé peut être appliqué :
/// jamais d'application partielle, jamais de données qui échoueraient à l'écriture en base.
/// </summary>
public sealed class CatalogSnapshot
{
    private CatalogSnapshot(IReadOnlyList<ProviderProduct> products) => Products = products;

    public IReadOnlyList<ProviderProduct> Products { get; }

    /// <summary>
    /// Valide les produits reçus face au total annoncé par le fournisseur (identifiants uniques, nombre égal
    /// au total, données compatibles avec le schéma) et normalise les prix à 2 décimales.
    /// </summary>
    public static Result<CatalogSnapshot> Create(IReadOnlyCollection<ProviderProduct> products, int announcedTotal)
    {
        var duplicate = products.GroupBy(product => product.ExternalId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return Invalid($"le produit {duplicate.Key} apparaît plusieurs fois");
        }

        if (products.Count != announcedTotal)
        {
            return Invalid($"{products.Count} produits reçus pour {announcedTotal} annoncés");
        }

        var malformed = products.FirstOrDefault(product => !IsWellFormed(product));
        if (malformed is not null)
        {
            return Invalid($"données invalides pour le produit {malformed.ExternalId}");
        }

        // Prix arrondi à l'échelle stockée : sinon 9.999 serait stocké 10.00 puis jugé différent
        // à chaque synchronisation (fausse mise à jour quotidienne, CAT-R4.3).
        return new CatalogSnapshot([.. products.Select(product => product with
        {
            Price = Math.Round(product.Price, Product.PriceDecimals, MidpointRounding.AwayFromZero),
        })]);
    }

    private static bool IsWellFormed(ProviderProduct product) =>
        product.ExternalId > 0
        && !string.IsNullOrWhiteSpace(product.Name) && product.Name.Length <= Product.MaxNameLength
        && product.Category is not null && product.Category.Length <= Product.MaxCategoryLength
        && product.AvailabilityStatus is not null && product.AvailabilityStatus.Length <= Product.MaxAvailabilityStatusLength
        && product.Price >= 0
        && product.Stock >= 0;

    private static DomainError Invalid(string reason) =>
        new(ErrorCodes.ProviderUnavailable, $"Instantané du fournisseur incomplet ou invalide : {reason}.",
            new Dictionary<string, object?> { ["reason"] = "invalid_snapshot" });
}
