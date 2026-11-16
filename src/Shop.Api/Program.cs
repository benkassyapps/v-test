using Shop.Api.Hosting;
using Shop.Application;
using Shop.Infrastructure;
using Shop.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShopApi(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Mode migrateur (PLT-5) : applique les migrations puis s'arrête, sans serveur HTTP ni planificateur.
// Exécuté seul avant les instances (service « migrator » de docker compose).
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    return await DatabaseMigrator.MigrateAsync(app.Services, CancellationToken.None) ? 0 : 1;
}

// Option réservée au développement en instance unique.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup")
    && !await DatabaseMigrator.MigrateAsync(app.Services, CancellationToken.None))
{
    return 1;
}

app.UseShopApi();
await app.RunAsync();
return 0;

/// <summary>Point d'entrée, exposé pour WebApplicationFactory dans les tests.</summary>
public partial class Program;
