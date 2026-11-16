using Microsoft.EntityFrameworkCore;

namespace Shop.Infrastructure.Persistence;

/// <summary>
/// Contexte EF Core unique (catalogue, synchronisation, paniers).
/// Les configurations sont découvertes par fonctionnalité dans Persistence/Configurations.
/// </summary>
public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShopDbContext).Assembly);
    }
}
