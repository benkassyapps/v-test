using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Catalog;
using Shop.Testing.Fakes;
using Shop.Testing.Hosting;

namespace Shop.IntegrationTests.Carts;

/// <summary>Appels HTTP du panier, par utilisateur et par instance, pour les tests d'intégration.</summary>
internal sealed class CartApi(ShopTestCluster cluster)
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Alimente le catalogue par une vraie synchronisation : identifiants locaux 1..n dans l'ordre.</summary>
    public async Task SeedCatalogAsync(params (string Name, decimal Price)[] products)
    {
        cluster.Doubles.Provider.SetProducts(products.Select((product, index) => new FakeProviderProduct(index + 1, product.Name, "cat", product.Price)));
        await SyncAsync(force: false);
    }

    public async Task SyncAsync(bool force)
    {
        await using var scope = cluster.Instance(1).Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<CatalogSyncService>().RunManualAsync(force, Ct);
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    public async Task<HttpResponseMessage> PutCartAsync(string user, int instance = 1)
    {
        using var client = cluster.CreateClient(instance, user);
        return await client.PutAsync(new Uri("/api/cart", UriKind.Relative), content: null, Ct);
    }

    public async Task<HttpResponseMessage> GetCartAsync(string user, int instance = 1)
    {
        using var client = cluster.CreateClient(instance, user);
        return await client.GetAsync(new Uri("/api/cart", UriKind.Relative), Ct);
    }

    public async Task<HttpResponseMessage> AddAsync(string user, int productId, int quantity, int instance = 1)
    {
        using var client = cluster.CreateClient(instance, user);
        return await client.PostAsJsonAsync(new Uri("/api/cart/lines", UriKind.Relative), new { productId, quantity }, Ct);
    }

    public async Task<HttpResponseMessage> ChangeAsync(string user, Guid lineId, int quantity, long expectedVersion, int instance = 1)
    {
        using var client = cluster.CreateClient(instance, user);
        return await client.PutAsJsonAsync(new Uri($"/api/cart/lines/{lineId}", UriKind.Relative), new { quantity, expectedVersion }, Ct);
    }

    public async Task<HttpResponseMessage> RemoveAsync(string user, Guid lineId, long expectedVersion, int instance = 1)
    {
        using var client = cluster.CreateClient(instance, user);
        return await client.DeleteAsync(new Uri($"/api/cart/lines/{lineId}?expectedVersion={expectedVersion}", UriKind.Relative), Ct);
    }

    /// <summary>Panier courant de l'utilisateur (échoue si absent).</summary>
    public async Task<JsonElement> CartAsync(string user)
    {
        using var response = await GetCartAsync(user);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return await JsonAsync(response);
    }

    public async Task<JsonElement> LineAsync(string user, string productName) =>
        (await CartAsync(user)).GetProperty("lines").EnumerateArray()
            .Single(line => line.GetProperty("productName").GetString() == productName);

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    /// <summary>Lance toutes les requêtes en même temps (barrière) et attend leurs réponses.</summary>
    public static async Task<HttpResponseMessage[]> ConcurrentlyAsync(params Func<Task<HttpResponseMessage>>[] requests)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = requests.Select(async request =>
        {
            await start.Task;
            return await request();
        }).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }
}
