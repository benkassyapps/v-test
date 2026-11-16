using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Shop";

    /// <summary>Enregistre la persistance SQL Server et les services d'infrastructure.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Chaîne de connexion lue à la résolution (et non à l'enregistrement) : les tests peuvent la remplacer.
        services.AddDbContext<ShopDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"La chaîne de connexion « ConnectionStrings:{ConnectionStringName} » est obligatoire (variable d'environnement ConnectionStrings__{ConnectionStringName}).");
            }

            // Pas de EnableRetryOnFailure global : il interdirait les transactions explicites hors stratégie.
            // Le rejeu est appliqué explicitement aux seules opérations idempotentes (specs/platform/design.md §6).
            options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ShopDbContext).Assembly.FullName));
        });

        services.AddHealthChecks().AddDbContextCheck<ShopDbContext>("database");
        services.AddHostedService<ReadCommittedSnapshotCheck>();

        return services;
    }
}
