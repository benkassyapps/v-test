using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Catalog;
using Shop.Domain.Catalog;
using Shop.Domain.Common;
using Shop.Testing.Fakes;
using Shop.Testing.Hosting;

namespace Shop.AcceptanceTests.Support;

/// <summary>Ligne du catalogue local, pour comparer l'état avant et après une synchronisation.</summary>
public sealed record CatalogRow(
    int Id,
    int ExternalId,
    string Name,
    string Category,
    decimal Price,
    int Stock,
    string AvailabilityStatus,
    ProductStatus Status,
    DateTimeOffset LastUpdatedAt);

/// <summary>
/// État et actions partagés par les étapes du catalogue au sein d'un scénario :
/// déclenchements de synchronisation, planificateur, lecture directe de la base pour les vérifications.
/// </summary>
public sealed class CatalogWorld(ShopTestCluster cluster, ScenarioHttp http) : IDisposable
{
    public ShopTestCluster Cluster => cluster;

    public FakeDummyJsonHandler Provider => cluster.Doubles.Provider;

    /// <summary>État du catalogue juste avant la dernière synchronisation déclenchée par une étape « Quand ».</summary>
    public IReadOnlyList<CatalogRow>? CatalogBefore { get; private set; }

    /// <summary>Nombre de runs avant la dernière action « Quand » (pour « aucune nouvelle synchronisation »).</summary>
    public int RunCountBefore { get; private set; }

    /// <summary>Synchronisation lancée en arrière-plan et maintenue « en cours » par le faux fournisseur.</summary>
    public Task<HttpResponseMessage>? BackgroundSync { get; set; }

    public HttpClient? BackgroundClient { get; set; }

    /// <summary>Photographie l'état avant une action susceptible de synchroniser.</summary>
    public async Task CaptureBeforeAsync()
    {
        CatalogBefore = await CatalogAsync();
        RunCountBefore = (await RunsAsync()).Count;
    }

    /// <summary>Synchronisation manuelle par HTTP (réponse enregistrée comme dernière réponse du scénario).</summary>
    public async Task<HttpResponseMessage> TriggerManualSyncAsync(int instance = 1, bool force = false, string? adminKey = ShopApplicationFactory.AdminApiKey)
    {
        using var client = cluster.CreateClient(instance);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/admin/catalog/sync?force={(force ? "true" : "false")}", UriKind.Relative));
        if (adminKey is not null)
        {
            request.Headers.Add("X-Admin-Key", adminKey);
        }

        return await http.SendAsync(client, request);
    }

    /// <summary>Synchronisation manuelle directe (sans toucher à la dernière réponse HTTP), pour préparer un contexte.</summary>
    public async Task<Result<SyncRun>> SyncDirectAsync(bool force = false, int instance = 1)
    {
        await using var scope = cluster.Instance(instance).Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogSyncService>()
            .RunManualAsync(force, TestContext.Current.CancellationToken);
    }

    public async Task SyncDirectOrFailAsync(bool force = false)
    {
        var result = await SyncDirectAsync(force);
        Assert.True(result.IsSuccess, $"La synchronisation de préparation a échoué : {result.Error?.Message}");
    }

    public async Task<SchedulerCheckOutcome> CheckSchedulerAsync(int instance)
    {
        await using var scope = cluster.Instance(instance).Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogSyncScheduler>().CheckAsync(TestContext.Current.CancellationToken);
    }

    public async Task<HttpResponseMessage> GetAdminAsync(string path, bool withKey = true, int instance = 1)
    {
        using var client = cluster.CreateClient(instance, admin: withKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        return await http.SendAsync(client, request);
    }

    public async Task<IReadOnlyList<CatalogRow>> CatalogAsync()
    {
        await using var db = cluster.CreateDbContext();
        return await db.Set<Product>().AsNoTracking()
            .OrderBy(product => product.ExternalId)
            .Select(product => new CatalogRow(product.Id, product.ExternalId, product.Name, product.Category, product.Price,
                product.Stock, product.AvailabilityStatus, product.Status, product.LastUpdatedAt))
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task<CatalogRow> ProductAsync(int externalId) =>
        (await CatalogAsync()).Single(product => product.ExternalId == externalId);

    public async Task<IReadOnlyList<SyncRun>> RunsAsync()
    {
        await using var db = cluster.CreateDbContext();
        return await db.Set<SyncRun>().AsNoTracking().OrderBy(run => run.Id).ToListAsync(TestContext.Current.CancellationToken);
    }

    public async Task<SyncRun> LastRunAsync() => (await RunsAsync())[^1];

    /// <summary>Insère directement un run dans l'historique (préparation d'un contexte du planificateur).</summary>
    public async Task InsertRunAsync(SyncRun run)
    {
        await using var db = cluster.CreateDbContext();
        db.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public void AdvanceClock(TimeSpan delta) => cluster.Doubles.Clock.Advance(delta);

    public void SetClock(DateTimeOffset now) => cluster.Doubles.Clock.SetUtcNow(now);

    public DateTimeOffset Now => cluster.Doubles.Clock.GetUtcNow();

    public int NextProviderId() => Provider.Products.Count == 0 ? 1 : Provider.Products.Max(product => product.Id) + 1;

    public void Dispose() => BackgroundClient?.Dispose();

    public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>())!;
}
