using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shop.Application.Carts;
using Shop.Application.Catalog;
using Shop.Infrastructure.Carts;
using Shop.Infrastructure.Catalog;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Shop";

    /// <summary>Section de configuration du pipeline de résilience HTTP vers le fournisseur.</summary>
    public const string ProviderHttpSection = "CatalogSync:Http";

    /// <summary>Enregistre la persistance SQL Server et les services d'infrastructure.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Fabrique de contextes : les stores créent des contextes courts et indépendants (transaction de synchro
        // isolée de l'historique). Le contexte reste aussi injectable par portée (health check, outils).
        // Chaîne de connexion lue à la résolution (et non à l'enregistrement) : les tests peuvent la remplacer.
        services.AddDbContextFactory<ShopDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"La chaîne de connexion « ConnectionStrings:{ConnectionStringName} » est obligatoire (variable d'environnement ConnectionStrings__{ConnectionStringName}).");
            }

            // Pas de EnableRetryOnFailure global : le rejeu est explicite et réservé aux opérations idempotentes
            // (TransientRetry, specs/platform/design.md §6).
            options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ShopDbContext).Assembly.FullName));
        });
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<ShopDbContext>>().CreateDbContext());

        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);
        services.AddSingleton<TransientRetry>();

        services.AddHealthChecks().AddDbContextCheck<ShopDbContext>("database");
        services.AddHostedService<ReadCommittedSnapshotCheck>();

        AddCatalog(services, configuration);
        services.AddScoped<ICartStore, CartStore>();
        services.AddScoped<ICartReader, CartReader>();

        return services;
    }

    private static void AddCatalog(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICatalogSyncLock, SqlCatalogSyncLock>();
        services.AddScoped<ICatalogStore, CatalogStore>();
        services.AddScoped<ISyncRunStore, SyncRunStore>();
        services.AddScoped<IProductReader, ProductReader>();

        services.AddHttpClient<ICatalogProvider, DummyJsonCatalogProvider>((provider, client) =>
            {
                var baseUrl = provider.GetRequiredService<IOptions<CatalogSyncOptions>>().Value.BaseUrl.ToString();
                client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            })
            // Nouvelles tentatives (5xx, 408, 429), délai par tentative et global, coupe-circuit (Polly v8).
            // Valeurs du design (10 s par tentative, 60 s au total), surchargeables par la section CatalogSync:Http (lue à l'utilisation).
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
                configuration.GetSection(ProviderHttpSection).Bind(options);
            });

        services.AddHostedService<CatalogSyncHostedService>();
    }
}
