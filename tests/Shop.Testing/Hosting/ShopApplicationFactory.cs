using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
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
            ["CatalogSync:SchedulerEnabled"] = "false",
            ["CatalogSync:BaseUrl"] = ProviderBaseUrl,
        }));

        builder.ConfigureLogging(logging => logging.AddProvider(new FakeLoggerProvider(doubles.Logs)));

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(doubles.Clock);
            services.ConfigureDbContext<ShopDbContext>(options => options.AddInterceptors(doubles.Faults));
        });
    }
}
