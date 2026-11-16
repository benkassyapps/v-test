using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Infrastructure.Persistence;

/// <summary>
/// Fabrique utilisée uniquement par l'outil dotnet-ef pour générer les migrations.
/// La génération n'ouvre aucune connexion : la chaîne ci-dessous n'est jamais utilisée pour se connecter.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShopDbContext>()
            .UseSqlServer("Server=design-time-only;Database=Shop;Integrated Security=true")
            .Options;
        return new ShopDbContext(options);
    }
}
