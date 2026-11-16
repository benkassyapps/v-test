using System.Globalization;
using System.Text.Json;
using Reqnroll;
using Shop.AcceptanceTests.Support;
using Shop.Testing.Fakes;

namespace Shop.AcceptanceTests.Steps;

/// <summary>
/// Préparation du catalogue local et liste paginée des produits (liste-produits.feature).
/// Le catalogue est toujours alimenté par une VRAIE synchronisation depuis le faux fournisseur.
/// </summary>
[Binding]
public sealed class CatalogueSteps(CatalogWorld world, ScenarioHttp http)
{
    private readonly List<int> _seenIds = [];
    private string? _nextCursor;
    private int? _limit;

    [Given("les produits suivants dans le catalogue local :")]
    public async Task CatalogContains(DataTable table)
    {
        var rows = table.Rows.Select((row, index) => (
            Product: new FakeProviderProduct(index + 1, row["nom"], "cat", decimal.Parse(row["prix"], CultureInfo.InvariantCulture)),
            Status: row["statut"])).ToList();

        // 1. Tous les produits sont synchronisés (identifiants locaux 1..n dans l'ordre du tableau).
        world.Provider.SetProducts(rows.Select(row => row.Product));
        await world.SyncDirectOrFailAsync();

        // 2. Les produits « Discontinued » disparaissent du fournisseur, puis synchronisation forcée.
        if (rows.Any(row => row.Status == "Discontinued"))
        {
            world.Provider.SetProducts(rows.Where(row => row.Status == "Active").Select(row => row.Product));
            await world.SyncDirectOrFailAsync(force: true);
        }

        world.AdvanceClock(TimeSpan.FromMinutes(1));
    }

    [Given("le catalogue local contient {int} produits actifs supplémentaires")]
    public async Task CatalogHasMoreProducts(int count)
    {
        var firstId = world.NextProviderId();
        foreach (var id in Enumerable.Range(firstId, count))
        {
            world.Provider.Upsert(new FakeProviderProduct(id, $"Supplémentaire {id}", "cat", 1m));
        }

        await world.SyncDirectOrFailAsync();
    }

    [Given("le produit {string} est retiré du catalogue")]
    public async Task ProductIsDiscontinued(string name)
    {
        world.Provider.Remove(world.Provider.Products.Single(product => product.Title == name).Id);
        await world.SyncDirectOrFailAsync(force: true);
    }

    [Given("le produit {string} au prix {float} est ajouté au catalogue")]
    public async Task ProductIsAdded(string name, decimal price)
    {
        world.Provider.Upsert(new FakeProviderProduct(world.NextProviderId(), name, "cat", price));
        await world.SyncDirectOrFailAsync();
    }

    [When("le client liste les produits avec une limite de {int}")]
    public Task ListWithLimit(int limit)
    {
        _limit = limit;
        return ListAsync($"limit={limit}");
    }

    [When("le client liste les produits sans préciser de limite")]
    public Task ListWithoutLimit() => ListAsync(string.Empty);

    [When("le client liste les produits avec les paramètres {string}")]
    public Task ListWithParameters(string query) => ListAsync(query);

    [When("le client demande la page suivante")]
    public Task NextPage()
    {
        Assert.NotNull(_nextCursor);
        var limit = _limit is { } value ? $"limit={value}&" : string.Empty;
        return ListAsync($"{limit}cursor={Uri.EscapeDataString(_nextCursor)}");
    }

    [Then("la page contient les produits {string} et indique qu'il en reste d'autres")]
    public void PageContainsWithMore(string names) => AssertPage(names, hasMore: true);

    [Then("la page contient les produits {string} et indique qu'il n'en reste pas d'autres")]
    public void PageContainsWithoutMore(string names) => AssertPage(names, hasMore: false);

    [Then("la page contient {int} produits et indique qu'il en reste d'autres")]
    public void PageCountWithMore(int count)
    {
        var page = http.LastJson();
        Assert.Equal(count, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("hasMore").GetBoolean());
    }

    [Then("aucun produit n'a été renvoyé deux fois au cours du parcours")]
    public void NoDuplicates() => Assert.Equal(_seenIds.Count, _seenIds.Distinct().Count());

    private async Task ListAsync(string query)
    {
        using var client = world.Cluster.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/products?{query}", UriKind.Relative));
        await http.SendAsync(client, request);

        if (http.Last.IsSuccessStatusCode)
        {
            var page = http.LastJson();
            _seenIds.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32()));
            _nextCursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.String ? page.GetProperty("nextCursor").GetString() : null;
        }
    }

    private void AssertPage(string names, bool hasMore)
    {
        Assert.True(http.Last.IsSuccessStatusCode, http.LastBody);
        var page = http.LastJson();
        var expected = names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var actual = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToList();

        Assert.Equal(expected, actual);
        Assert.Equal(hasMore, page.GetProperty("hasMore").GetBoolean());
        Assert.Equal(hasMore, page.GetProperty("nextCursor").ValueKind == JsonValueKind.String);
    }
}
