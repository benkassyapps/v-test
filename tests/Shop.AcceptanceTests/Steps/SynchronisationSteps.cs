using System.Globalization;
using System.Text.Json;
using Reqnroll;
using Shop.AcceptanceTests.Support;
using Shop.Domain.Catalog;
using Shop.Testing.Fakes;

namespace Shop.AcceptanceTests.Steps;

/// <summary>Étapes de la synchronisation du catalogue (synchronisation.feature).</summary>
[Binding]
public sealed class SynchronisationSteps(CatalogWorld world, ScenarioHttp http)
{
    // --- Fournisseur ---

    [Given("le fournisseur renvoie au plus {int} produits par page")]
    public void ProviderPageSize(int max) => world.Provider.MaxPageSize = max;

    [Given("le fournisseur propose les produits suivants :")]
    public void ProviderOffers(DataTable table) =>
        world.Provider.SetProducts(table.Rows.Select(row => new FakeProviderProduct(
            int.Parse(row["externalId"], CultureInfo.InvariantCulture),
            row["nom"],
            row["catégorie"],
            decimal.Parse(row["prix"], CultureInfo.InvariantCulture),
            row.TryGetValue("stock", out var stock) ? int.Parse(stock, CultureInfo.InvariantCulture) : 10,
            row.TryGetValue("disponibilité", out var availability) ? availability : "In Stock")));

    [Given("le fournisseur modifie le produit {int} avec le nom {string} et le prix {float}")]
    public void ProviderChanges(int externalId, string name, decimal price) =>
        world.Provider.Update(externalId, product => product with { Title = name, Price = price });

    [Given("le fournisseur ne propose plus le produit {int}")]
    public void ProviderRemoves(int externalId) => world.Provider.Remove(externalId);

    [Given("le fournisseur ajoute le produit {int} {string} au prix {float}")]
    [Given("le fournisseur propose de nouveau le produit {int} {string} au prix {float}")]
    public void ProviderAdds(int externalId, string name, decimal price) =>
        world.Provider.Upsert(new FakeProviderProduct(externalId, name, "cat", price));

    [Given("le fournisseur échoue à partir de la page {int}")]
    public void ProviderFailsFromPage(int page) => world.Provider.FailFromPage = page;

    [Given("le fournisseur fonctionne de nouveau")]
    public void ProviderWorksAgain()
    {
        world.Provider.FailFromPage = null;
        world.Provider.Unavailable = false;
    }

    [Given("le fournisseur est indisponible")]
    public void ProviderUnavailable() => world.Provider.Unavailable = true;

    [Given("le fournisseur annonce {int} produits mais n'en renvoie que {int}")]
    public void ProviderLiesAboutTotal(int announced, int returned)
    {
        world.Provider.SetProducts(world.Provider.Products.Take(returned));
        world.Provider.AnnouncedTotalOverride = announced;
    }

    [Given("le fournisseur ne propose aucun produit")]
    public void ProviderEmpty() => world.Provider.SetProducts([]);

    [Given("le fournisseur ne propose plus que {int} de ces produits")]
    public void ProviderKeepsOnly(int count) => world.Provider.SetProducts(world.Provider.Products.Take(count));

    // --- Catalogue local (préparation) ---

    [Given("une synchronisation a déjà réussi")]
    public async Task SyncAlreadySucceeded()
    {
        await world.SyncDirectOrFailAsync();
        world.AdvanceClock(TimeSpan.FromHours(1));
    }

    [Given("le catalogue local contient des produits synchronisés")]
    public async Task CatalogHasSyncedProducts()
    {
        world.Provider.SetProducts([
            new FakeProviderProduct(1, "Mascara", "beauty", 9.99m),
            new FakeProviderProduct(2, "Parfum", "fragrance", 49.99m),
            new FakeProviderProduct(3, "Lampe", "furniture", 19.50m),
        ]);
        await SyncAlreadySucceeded();
    }

    [Given("le catalogue local contient {int} produits actifs synchronisés")]
    public async Task CatalogHasActiveProducts(int count)
    {
        world.Provider.SetProducts(Enumerable.Range(1, count).Select(id => new FakeProviderProduct(id, $"Produit {id}", "cat", id)));
        await SyncAlreadySucceeded();
    }

    [Given("le catalogue local est vide")]
    public async Task CatalogIsEmpty() => Assert.Empty(await world.CatalogAsync());

    [Given("le catalogue local contient le produit {int} {string} au statut {string}")]
    public async Task CatalogHasProductWithStatus(int externalId, string name, string status)
    {
        var others = world.Provider.Products.ToList();
        world.Provider.Upsert(new FakeProviderProduct(externalId, name, "cat", 1m));
        await world.SyncDirectOrFailAsync();
        if (Enum.Parse<ProductStatus>(status) == ProductStatus.Discontinued)
        {
            world.Provider.SetProducts(others);
            await world.SyncDirectOrFailAsync(force: true);
        }

        world.AdvanceClock(TimeSpan.FromHours(1));
        Assert.Equal(status, (await world.ProductAsync(externalId)).Status.ToString());
    }

    [Given("le seuil de retrait maximal est de {int} %")]
    public static void GuardThreshold(int percent) =>
        Assert.True(percent == 50, "Le seuil configuré pour les tests est le seuil par défaut (50 %).");

    [Given("la base de données échoue pendant l'application de la synchronisation")]
    public void DatabaseFailsDuringApply() => world.Cluster.Doubles.Faults.ArmFor<Product>();

    [Given("une synchronisation manuelle a échoué")]
    public async Task ManualSyncFailed() => Assert.False((await world.SyncDirectAsync()).IsSuccess);

    [Given("une synchronisation manuelle a réussi")]
    public async Task ManualSyncSucceeded()
    {
        world.AdvanceClock(TimeSpan.FromMinutes(1));
        await world.SyncDirectOrFailAsync();
    }

    // --- Actions ---

    [When("une synchronisation manuelle est déclenchée")]
    public async Task TriggerManualSync()
    {
        await world.CaptureBeforeAsync();
        world.AdvanceClock(TimeSpan.FromMinutes(1));
        await world.TriggerManualSyncAsync();
    }

    [When("une synchronisation manuelle forcée est déclenchée")]
    public async Task TriggerForcedSync()
    {
        await world.CaptureBeforeAsync();
        world.AdvanceClock(TimeSpan.FromMinutes(1));
        await world.TriggerManualSyncAsync(force: true);
    }

    // --- Vérifications ---

    [Then("la synchronisation réussit avec {int} produits récupérés, {int} insérés, {int} mis à jour, {int} retirés et {int} réactivés")]
    public void SyncSucceededWith(int fetched, int inserted, int updated, int discontinued, int reactivated)
    {
        Assert.True(http.Last.IsSuccessStatusCode, $"Synchronisation en échec : {http.LastBody}");
        var run = http.LastJson();
        Assert.Equal("Succeeded", run.GetProperty("status").GetString());
        Assert.Equal(
            (fetched, inserted, updated, discontinued, reactivated),
            (Int(run, "fetched"), Int(run, "inserted"), Int(run, "updated"), Int(run, "discontinued"), Int(run, "reactivated")));
    }

    [Then("la synchronisation échoue")]
    public async Task SyncFailed()
    {
        Assert.False(http.Last.IsSuccessStatusCode, "La synchronisation aurait dû échouer.");
        Assert.Equal(SyncRunStatus.Failed, (await world.LastRunAsync()).Status);
    }

    [Then("la synchronisation est bloquée par le garde-fou de retrait massif")]
    public async Task SyncBlockedByGuard()
    {
        var last = await world.LastRunAsync();
        Assert.Equal((SyncRunStatus.Failed, SyncFailureReason.GuardTriggered), (last.Status, last.FailureReason!.Value));
    }

    [Then("le catalogue local contient exactement les produits suivants :")]
    public async Task CatalogContainsExactly(DataTable table)
    {
        var expected = table.Rows.Select(row => string.Join("|",
            row["externalId"], row["nom"], row["catégorie"], row["prix"], row["stock"], row["disponibilité"], row["statut"]));
        var actual = (await world.CatalogAsync()).Select(product => string.Join("|",
            product.ExternalId, product.Name, product.Category, product.Price.ToString("0.00", CultureInfo.InvariantCulture),
            product.Stock, product.AvailabilityStatus, product.Status));

        Assert.Equal(expected, actual);
    }

    [Then("chaque produit a pour date de dernière mise à jour la date de la synchronisation")]
    public async Task EveryProductUpdatedAtSyncTime()
    {
        var finishedAt = (await world.LastRunAsync()).FinishedAt;
        Assert.All(await world.CatalogAsync(), product => Assert.Equal(finishedAt, product.LastUpdatedAt));
    }

    [Then("le catalogue local contient {int} produits")]
    public async Task CatalogCount(int count) => Assert.Equal(count, (await world.CatalogAsync()).Count);

    [Then("le produit {int} du catalogue local a le nom {string} et le prix {float}")]
    public async Task ProductHasNameAndPrice(int externalId, string name, decimal price)
    {
        var product = await world.ProductAsync(externalId);
        Assert.Equal((name, price), (product.Name, product.Price));
    }

    [Then("la date de dernière mise à jour du produit {int} est la date de cette synchronisation")]
    public async Task ProductUpdatedNow(int externalId) =>
        Assert.Equal((await world.LastRunAsync()).FinishedAt, (await world.ProductAsync(externalId)).LastUpdatedAt);

    [Then("la date de dernière mise à jour du produit {int} est inchangée")]
    public async Task ProductUpdateDateUnchanged(int externalId) =>
        Assert.Equal(
            world.CatalogBefore!.Single(product => product.ExternalId == externalId).LastUpdatedAt,
            (await world.ProductAsync(externalId)).LastUpdatedAt);

    [Then("le produit {int} du catalogue local a le statut {string}")]
    public async Task ProductHasStatus(int externalId, string status) =>
        Assert.Equal(status, (await world.ProductAsync(externalId)).Status.ToString());

    [Then("le produit {int} du catalogue local a le statut {string} et le prix {float}")]
    public async Task ProductHasStatusAndPrice(int externalId, string status, decimal price)
    {
        var product = await world.ProductAsync(externalId);
        Assert.Equal((status, price), (product.Status.ToString(), product.Price));
    }

    [Then("le produit {int} n'apparaît plus dans la liste des produits")]
    public async Task ProductNotListed(int externalId)
    {
        using var client = world.Cluster.CreateClient();
        using var response = await client.GetAsync(new Uri("/api/products?limit=100", UriKind.Relative));
        var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.DoesNotContain(externalId, page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("externalId").GetInt32()));
    }

    [Then("le catalogue local est identique à son état avant la synchronisation")]
    public async Task CatalogUnchanged() => Assert.Equal(world.CatalogBefore, await world.CatalogAsync());

    [Then("le run indique que le forçage a été utilisé")]
    public void RunWasForced() => Assert.True(http.LastJson().GetProperty("forced").GetBoolean());

    [Then("l'historique contient un run {string} suivi d'un run {string}")]
    public async Task HistoryContainsInOrder(string first, string second)
    {
        var runs = await world.RunsAsync();
        Assert.True(runs.Count >= 2, "L'historique devrait contenir au moins deux runs.");
        Assert.Equal((first, second), (runs[^2].Status.ToString(), runs[^1].Status.ToString()));
    }

    private static int Int(JsonElement element, string property) => element.GetProperty(property).GetInt32();
}
