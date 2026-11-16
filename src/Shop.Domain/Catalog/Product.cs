namespace Shop.Domain.Catalog;

public enum ProductStatus : byte
{
    Active = 1,
    Discontinued = 2,
}

/// <summary>Produit tel que reçu du fournisseur, avant application au catalogue local.</summary>
public sealed record ProviderProduct(
    int ExternalId,
    string Name,
    string Category,
    decimal Price,
    int Stock,
    string AvailabilityStatus,
    DateTimeOffset? ProviderUpdatedAt);

/// <summary>
/// Produit du catalogue local. L'identifiant interne (<see cref="Id"/>) est celui de l'API et des paniers ;
/// <see cref="ExternalId"/> est l'identifiant du fournisseur, clé de la synchronisation (CAT-R4, CAT-R5).
/// </summary>
public sealed class Product
{
    /// <summary>Longueurs maximales, partagées avec le schéma SQL (nvarchar).</summary>
    public const int MaxNameLength = 200;
    public const int MaxCategoryLength = 100;
    public const int MaxAvailabilityStatusLength = 50;

    /// <summary>Échelle des prix (decimal(18,2)).</summary>
    public const int PriceDecimals = 2;

    private Product()
    {
    }

    public int Id { get; private set; }

    public int ExternalId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Category { get; private set; } = string.Empty;

    public decimal Price { get; private set; }

    /// <summary>Disponibilité indicative : aucune réservation de stock.</summary>
    public int Stock { get; private set; }

    public string AvailabilityStatus { get; private set; } = string.Empty;

    public ProductStatus Status { get; private set; }

    public DateTimeOffset? ProviderUpdatedAt { get; private set; }

    /// <summary>Dernier changement effectif des données locales du produit (CAT-R5.2).</summary>
    public DateTimeOffset LastUpdatedAt { get; private set; }

    public DateTimeOffset? DiscontinuedAt { get; private set; }

    public bool IsActive => Status == ProductStatus.Active;

    public static Product Create(ProviderProduct source, DateTimeOffset now)
    {
        var product = new Product { ExternalId = source.ExternalId, Status = ProductStatus.Active };
        product.CopyFrom(source, now);
        return product;
    }

    /// <summary>Vrai si les informations métier sont identiques à celles du fournisseur (aucune écriture nécessaire).</summary>
    public bool HasSameData(ProviderProduct source) =>
        Name == source.Name
        && Category == source.Category
        && Price == source.Price
        && Stock == source.Stock
        && AvailabilityStatus == source.AvailabilityStatus;

    /// <summary>Met à jour les informations et réactive le produit s'il était retiré.</summary>
    internal void ApplyFrom(ProviderProduct source, DateTimeOffset now)
    {
        Status = ProductStatus.Active;
        DiscontinuedAt = null;
        CopyFrom(source, now);
    }

    /// <summary>Retrait logique : le produit reste en base pour les paniers qui le contiennent (CAT-R9).</summary>
    internal void Discontinue(DateTimeOffset now)
    {
        Status = ProductStatus.Discontinued;
        DiscontinuedAt = now;
        LastUpdatedAt = now;
    }

    private void CopyFrom(ProviderProduct source, DateTimeOffset now)
    {
        Name = source.Name;
        Category = source.Category;
        Price = source.Price;
        Stock = source.Stock;
        AvailabilityStatus = source.AvailabilityStatus;
        ProviderUpdatedAt = source.ProviderUpdatedAt;
        LastUpdatedAt = now;
    }
}
