using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Shop.Application.Catalog;
using Shop.Infrastructure.Persistence;

namespace Shop.Testing.Hosting;

/// <summary>
/// Une instance applicative de test (hôte en mémoire) branchée sur la base partagée.
/// Deux instances sur la même base reproduisent le fonctionnement multi-instance (PLT-1).
/// </summary>
public sealed class ShopApplicationFactory(string instanceId, string connectionString, TestDoubles doubles)
    : WebApplicationFactory<Program>
{
    public const string AdminApiKey = "cle-admin-de-test";
    public const string ProviderBaseUrl = "http://dummyjson.test";

    public string InstanceId { get; } = instanceId;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Shop"] = connectionString,
            ["Instance:Id"] = InstanceId,
            ["Api:EnableDocs"] = "true",
            ["Admin:ApiKey"] = AdminApiKey,

            // Planificateur piloté explicitement par les tests (CatalogSyncScheduler.CheckAsync).
            ["CatalogSync:SchedulerEnabled"] = "false",
            ["CatalogSync:BaseUrl"] = ProviderBaseUrl,
            ["CatalogSync:PageTimeout"] = "00:00:05",

            // Pipeline de résilience HTTP : une seule nouvelle tentative, sans délai (l'horloge des tests est simulée).
            ["CatalogSync:Http:Retry:MaxRetryAttempts"] = "1",
            ["CatalogSync:Http:Retry:Delay"] = "00:00:00",
            ["CatalogSync:Http:Retry:UseJitter"] = "false",
            ["CatalogSync:Http:CircuitBreaker:MinimumThroughput"] = "100000",

            // Rejeu des opérations idempotentes : délais courts.
            ["Database:RetryDelay"] = "00:00:00.010",
            ["Database:MaxRetryDelay"] = "00:00:00.020",
        }));

        builder.ConfigureLogging(logging => logging.AddProvider(new FakeLoggerProvider(doubles.Logs)));

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(doubles.Clock);
            services.ConfigureDbContext<ShopDbContext>(options => options.AddInterceptors(doubles.Faults, doubles.LostCommits));

            // Le client typé du fournisseur porte le nom de son interface.
            services.AddHttpClient(nameof(ICatalogProvider))
                .ConfigurePrimaryHttpMessageHandler(() => doubles.Provider)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        });
    }
}
