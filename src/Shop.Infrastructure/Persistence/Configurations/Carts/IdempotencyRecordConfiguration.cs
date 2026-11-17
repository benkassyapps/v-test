using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Application.Carts;
using Shop.Infrastructure.Carts;

namespace Shop.Infrastructure.Persistence.Configurations.Carts;

/// <summary>Table IdempotencyKeys (CART-R18).</summary>
internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyKeys");

        // Une clé par utilisateur ; la clé primaire arbitre aussi deux enregistrements simultanés (reprise de la commande).
        builder.HasKey(record => new { record.UserId, record.Key });
        builder.Property(record => record.UserId).HasMaxLength(64).UseCollation(CartConfiguration.UserIdCollation);
        builder.Property(record => record.Key).HasMaxLength(IdempotencyKeyFormat.MaxLength).UseCollation(CartConfiguration.UserIdCollation);
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(record => record.Outcome).IsRequired();

        // Purge des clés expirées.
        builder.HasIndex(record => record.ExpiresAt).HasDatabaseName("IX_IdempotencyKeys_ExpiresAt");
    }
}
