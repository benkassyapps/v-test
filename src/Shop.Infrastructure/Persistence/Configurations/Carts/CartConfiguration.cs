using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Carts;
using Shop.Domain.Catalog;

namespace Shop.Infrastructure.Persistence.Configurations.Carts;

internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    /// <summary>Collation binaire : identifiant utilisateur opaque et sensible à la casse (« alice » ≠ « Alice »).</summary>
    public const string UserIdCollation = "Latin1_General_100_BIN2";

    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");
        builder.HasKey(cart => cart.Id);
        builder.Property(cart => cart.Id).ValueGeneratedNever();

        // Un panier par utilisateur (CART-R1), garanti par la base.
        builder.Property(cart => cart.UserId).HasMaxLength(64).UseCollation(UserIdCollation).IsRequired();
        builder.HasIndex(cart => cart.UserId).IsUnique().HasDatabaseName("UX_Carts_UserId");

        // Filet de sécurité : toute mise à jour vérifie la version lue (le verrou UPDLOCK reste le mécanisme principal).
        builder.Property(cart => cart.Version).IsConcurrencyToken();

        builder.HasMany(cart => cart.Lines).WithOne().HasForeignKey(line => line.CartId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(cart => cart.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(cart => cart.Total);
    }
}

internal sealed class CartLineConfiguration : IEntityTypeConfiguration<CartLine>
{
    public void Configure(EntityTypeBuilder<CartLine> builder)
    {
        // Quantité toujours entre 1 et 999 (CART-R4), garantie par la base.
        builder.ToTable("CartLines", table => table.HasCheckConstraint("CK_CartLines_Quantity", "[Quantity] BETWEEN 1 AND 999"));
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.ProductName).HasMaxLength(Product.MaxNameLength).IsRequired();
        builder.Property(line => line.UnitPrice).HasPrecision(18, Product.PriceDecimals);
        builder.Property(line => line.Version).IsConcurrencyToken();

        // Un produit au plus une fois par panier (CART-R5).
        builder.HasIndex(line => new { line.CartId, line.ProductId }).IsUnique().HasDatabaseName("UX_CartLines_CartId_ProductId");

        // Un produit n'est jamais supprimé physiquement (retrait logique) : suppression interdite s'il est référencé.
        builder.HasOne<Product>().WithMany().HasForeignKey(line => line.ProductId).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(line => line.LineTotal);
    }
}
