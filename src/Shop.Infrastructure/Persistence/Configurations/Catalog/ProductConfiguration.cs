using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Catalog;

namespace Shop.Infrastructure.Persistence.Configurations.Catalog;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table => table.HasCheckConstraint("CK_Products_Price", "[Price] >= 0"));
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).UseIdentityColumn();

        // Un produit fournisseur = un produit local (CAT-R4).
        builder.HasIndex(product => product.ExternalId).IsUnique().HasDatabaseName("UX_Products_ExternalId");

        builder.Property(product => product.Name).HasMaxLength(Product.MaxNameLength).IsRequired();
        builder.Property(product => product.Category).HasMaxLength(Product.MaxCategoryLength).IsRequired();
        builder.Property(product => product.AvailabilityStatus).HasMaxLength(Product.MaxAvailabilityStatusLength).IsRequired();
        builder.Property(product => product.Price).HasPrecision(18, Product.PriceDecimals);
        builder.Property(product => product.Status).HasConversion<byte>();

        // Pagination keyset (CAT-R6) : « Id > @dernier ORDER BY Id » est une recherche de plage sur la clé primaire
        // clusterisée (lignes lues dans l'ordre, arrêt après limit + 1), le filtre Status écartant les rares produits
        // retirés. Pas d'index secondaire (Status, Id) : non couvrant, il imposerait une recherche par ligne (key lookup).
        builder.Ignore(product => product.IsActive);
    }
}
