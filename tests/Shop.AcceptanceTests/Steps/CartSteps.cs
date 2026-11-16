using System.Globalization;
using System.Net;
using System.Text.Json;
using Reqnroll;
using Shop.AcceptanceTests.Support;
using Shop.Testing.Fakes;

namespace Shop.AcceptanceTests.Steps;

/// <summary>Étapes du panier : création, consultation, ajout, modification, suppression (cart/acceptance/*.feature).</summary>
[Binding]
public sealed class CartSteps(CartWorld carts, CatalogWorld catalog, ScenarioHttp http)
{
    private const int UnknownProductId = 999_999;

    // --- Préparation ---

    [Given("{string} possède un panier vide")]
    public Task HasEmptyCart(string user) => carts.GivenAsync(user, HttpMethod.Put, "/api/cart");

    [Given("{string} possède un panier contenant :")]
    public async Task HasCartWith(string user, DataTable table)
    {
        foreach (var row in table.Rows)
        {
            await GivenAdded(user, int.Parse(row["quantité"], CultureInfo.InvariantCulture), row["produit"]);
        }
    }

    [Given("{string} n'a pas de panier")]
    public async Task HasNoCart(string user) => Assert.Equal(0, await carts.CartCountAsync(user));

    [Given("{string} a ajouté {int} {string} à son panier")]
    public async Task GivenAdded(string user, int quantity, string product) =>
        await carts.GivenAsync(user, HttpMethod.Post, "/api/cart/lines", new { productId = await carts.ProductIdAsync(product), quantity });

    [Given("{string} a ajouté {int} {string} à son panier via l'instance {int}")]
    public async Task GivenAddedVia(string user, int quantity, string product, int instance) =>
        await carts.GivenAsync(user, HttpMethod.Post, "/api/cart/lines", new { productId = await carts.ProductIdAsync(product), quantity }, instance);

    [Given("{string} a remplacé la quantité de la ligne {string} par {int} avec la version {int}")]
    public async Task GivenChanged(string user, string product, int quantity, long version)
    {
        var line = await carts.LineAsync(user, product);
        await carts.GivenAsync(user, HttpMethod.Put, LinePath(line), new { quantity, expectedVersion = version });
    }

    [Given("{string} a supprimé la ligne {string} avec la version {int}")]
    public async Task GivenRemoved(string user, string product, long version)
    {
        var line = await carts.LineAsync(user, product);
        await carts.GivenAsync(user, HttpMethod.Delete, $"{LinePath(line)}?expectedVersion={version}");
    }

    [Given("{string} a mémorisé la ligne {string} en version {int}")]
    public async Task MemorizeLine(string user, string product, long version)
    {
        var line = await carts.LineAsync(user, product);
        Assert.Equal(version, line.GetProperty("version").GetInt64());
        carts.Memorize(user, product, line.GetProperty("lineId").GetGuid());
    }

    [Given("{string} possède un panier en version {int} contenant les articles {string} et {string}")]
    public async Task HasCartAtVersion(string user, int version, string first, string second)
    {
        await GivenAdded(user, 1, first);
        await GivenAdded(user, 1, second);
        var quantity = 1;
        while ((await carts.ReadCartAsync(user)).GetProperty("version").GetInt64() < version)
        {
            var line = await carts.LineAsync(user, first);
            quantity = quantity == 1 ? 2 : 1;
            await carts.GivenAsync(user, HttpMethod.Put, LinePath(line), new { quantity, expectedVersion = line.GetProperty("version").GetInt64() });
        }

        Assert.Equal(version, (await carts.ReadCartAsync(user)).GetProperty("version").GetInt64());
    }

    [Given("le produit {string} est renommé {string} et passe au prix {float} dans le catalogue")]
    public async Task ProductRenamed(string name, string newName, decimal price)
    {
        var localId = await carts.ProductIdAsync(name);
        var product = catalog.Provider.Products.Single(candidate => candidate.Title == name);
        catalog.Provider.Upsert(product with { Title = newName, Price = price });
        await catalog.SyncDirectOrFailAsync();
        carts.RegisterFormerName(name, localId);
    }

    [Given("le produit {string} passe au prix {float} dans le catalogue")]
    public Task ProductRepriced(string name, decimal price) => ProductRenamed(name, name, price);

    [Given("la base de données échoue lors du prochain enregistrement du panier")]
    public void DatabaseFailsOnCartSave() => catalog.Cluster.Doubles.Faults.ArmFor<Shop.Domain.Carts.Cart>();

    // --- Commandes ---

    [When("{string} demande la création de son panier")]
    public Task CreateCart(string user) => carts.CommandAsync(user, HttpMethod.Put, "/api/cart");

    [When("{string} demande {int} fois simultanément la création de son panier, en alternant les instances {int} et {int}")]
    public Task CreateCartConcurrently(string user, int count, int first, int second) =>
        carts.RunConcurrentlyAsync([.. Enumerable.Range(0, count)
            .Select(index => new PendingCartRequest(index % 2 == 0 ? first : second, user, HttpMethod.Put, "/api/cart", null, null))]);

    [Given("{string} consulte son panier")]
    [When("{string} consulte son panier")]
    public async Task ReadCart(string user)
    {
        await carts.SendAsync(user, HttpMethod.Get, "/api/cart");
        if (http.Last.IsSuccessStatusCode)
        {
            carts.RememberRead(user, http.LastJson());
        }
    }

    [When("{string} ajoute {int} {string} à son panier")]
    public Task Add(string user, int quantity, string product) => AddVia(user, quantity, product, 1);

    [When("{string} ajoute {int} {string} à son panier via l'instance {int}")]
    public async Task AddVia(string user, int quantity, string product, int instance) =>
        await carts.CommandAsync(user, HttpMethod.Post, "/api/cart/lines", new { productId = await carts.ProductIdAsync(product), quantity }, instance);

    [When("{string} ajoute {int} produit d'identifiant inexistant à son panier")]
    public Task AddUnknown(string user, int quantity) =>
        carts.CommandAsync(user, HttpMethod.Post, "/api/cart/lines", new { productId = UnknownProductId, quantity });

    [When("{string} remplace la quantité de la ligne {string} par {int} avec la version {int}")]
    public async Task Change(string user, string product, int quantity, long version) =>
        await carts.CommandAsync(user, HttpMethod.Put, LinePath(await carts.LineAsync(user, product)), new { quantity, expectedVersion = version });

    [When("{string} remplace la quantité de la ligne {string} par {int} sans indiquer de version")]
    public async Task ChangeWithoutVersion(string user, string product, int quantity) =>
        await carts.CommandAsync(user, HttpMethod.Put, LinePath(await carts.LineAsync(user, product)), new { quantity });

    [When("{string} remplace la quantité de la ligne {string} d'{string} par {int} avec la version {int}")]
    public async Task ChangeOthersLine(string actor, string product, string owner, int quantity, long version) =>
        await carts.CommandAsync(actor, HttpMethod.Put, LinePath(await carts.LineAsync(owner, product)), new { quantity, expectedVersion = version });

    [When("{string} remplace la quantité de la ligne mémorisée par {int} avec la version {int}")]
    public Task ChangeMemorized(string user, int quantity, long version) =>
        carts.CommandAsync(user, HttpMethod.Put, $"/api/cart/lines/{carts.Memorized(user)}", new { quantity, expectedVersion = version });

    [When("{string} supprime la ligne {string} avec la version {int}")]
    public async Task Remove(string user, string product, long version) =>
        await carts.CommandAsync(user, HttpMethod.Delete, $"{LinePath(await carts.LineAsync(user, product))}?expectedVersion={version}");

    [When("{string} supprime la ligne {string} sans indiquer de version")]
    public async Task RemoveWithoutVersion(string user, string product) =>
        await carts.CommandAsync(user, HttpMethod.Delete, LinePath(await carts.LineAsync(user, product)));

    [When("{string} supprime la ligne {string} d'{string} avec la version {int}")]
    public async Task RemoveOthersLine(string actor, string product, string owner, long version) =>
        await carts.CommandAsync(actor, HttpMethod.Delete, $"{LinePath(await carts.LineAsync(owner, product))}?expectedVersion={version}");

    // --- Vérifications ---

    [Then("le panier renvoyé est vide, en version {int}, avec un montant total de {float}")]
    public void ReturnedCartIsEmpty(long version, decimal total)
    {
        var cart = http.LastJson();
        Assert.Equal(0, cart.GetProperty("lines").GetArrayLength());
        Assert.Equal((version, total), (cart.GetProperty("version").GetInt64(), cart.GetProperty("total").GetDecimal()));
    }

    [Then("le panier renvoyé est le panier existant d'{string}, inchangé")]
    public async Task ReturnedCartIsExisting(string user)
    {
        Assert.Equal(carts.StateBefore(user), await carts.StateAsync(user));
        Assert.Equal((await carts.ReadCartAsync(user)).GetProperty("id").GetGuid(), http.LastJson().GetProperty("id").GetGuid());
    }

    [Then("le panier renvoyé est vide et différent de celui d'{string}")]
    public async Task ReturnedCartIsOtherEmpty(string other)
    {
        var cart = http.LastJson();
        Assert.Equal(0, cart.GetProperty("lines").GetArrayLength());
        Assert.NotEqual((await carts.ReadCartAsync(other)).GetProperty("id").GetGuid(), cart.GetProperty("id").GetGuid());
    }

    [Then("le panier est en version {int} avec les lignes suivantes :")]
    public async Task CartHasLines(long version, DataTable table)
    {
        var cart = await carts.ReadCartAsync(carts.LastUser!);
        Assert.Equal(version, cart.GetProperty("version").GetInt64());

        var expected = table.Rows
            .Select(row => string.Join("|", row["produit"], row["prix unitaire"], row["quantité"], row["montant"], row["version"], row["disponible"]))
            .Order(StringComparer.Ordinal);
        var actual = cart.GetProperty("lines").EnumerateArray()
            .Select(line => string.Join("|",
                line.GetProperty("productName").GetString(),
                Money(line.GetProperty("unitPrice")),
                line.GetProperty("quantity").GetInt32(),
                Money(line.GetProperty("lineTotal")),
                line.GetProperty("version").GetInt64(),
                line.GetProperty("productAvailable").GetBoolean() ? "oui" : "non"))
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [Then("le panier est en version {int} avec les quantités suivantes :")]
    public async Task CartHasQuantities(long version, DataTable table)
    {
        var cart = await carts.ReadCartAsync(carts.LastUser!);
        Assert.Equal(version, cart.GetProperty("version").GetInt64());
        var expected = table.Rows.Select(row => $"{row["produit"]}|{row["quantité"]}").Order(StringComparer.Ordinal);
        var actual = cart.GetProperty("lines").EnumerateArray()
            .Select(line => $"{line.GetProperty("productName").GetString()}|{line.GetProperty("quantity").GetInt32()}")
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Then("le panier est en version {int}")]
    public async Task CartIsAtVersion(long version) =>
        Assert.Equal(version, (await carts.ReadCartAsync(carts.LastUser!)).GetProperty("version").GetInt64());

    [Then("le montant total du panier est {float}")]
    public async Task CartTotal(decimal total) =>
        Assert.Equal(total, (await carts.ReadCartAsync(carts.LastUser!)).GetProperty("total").GetDecimal());

    [Then("le panier d'{string} est inchangé")]
    public async Task CartUnchanged(string user) => Assert.Equal(carts.StateBefore(user), await carts.StateAsync(user));

    [Then("le panier d'{string} ne contient pas de ligne {string}")]
    public async Task CartHasNoLine(string user, string product) =>
        Assert.Null(CartWorld.FindLine(await carts.ReadCartAsync(user), product));

    [Then("la ligne {string} du panier d'{string} a la quantité {int}")]
    public async Task LineQuantity(string product, string user, int quantity) =>
        Assert.Equal(quantity, (await carts.LineAsync(user, product)).GetProperty("quantity").GetInt32());

    [Then("la ligne {string} a un nouvel identifiant, la version {int}, la quantité {int} et le prix unitaire {float}")]
    public async Task LineIsNew(string product, long version, int quantity, decimal price)
    {
        var user = carts.LastUser!;
        var line = await carts.LineAsync(user, product);
        Assert.NotEqual(carts.Memorized(user), line.GetProperty("lineId").GetGuid());
        Assert.Equal(
            (version, quantity, price),
            (line.GetProperty("version").GetInt64(), line.GetProperty("quantity").GetInt32(), line.GetProperty("unitPrice").GetDecimal()));
    }

    [Then("la réponse est une erreur {int} de code {string} indiquant la version actuelle {int}")]
    public void ConflictWithCurrentVersion(int status, string code, long currentVersion)
    {
        new CommonSteps(http).ResponseIsError(status, code);
        Assert.Equal(currentVersion, http.LastJson().GetProperty("currentVersion").GetInt64());
    }

    [Then("{string} possède exactement un panier")]
    public async Task HasExactlyOneCart(string user) => Assert.Equal(1, await carts.CartCountAsync(user));

    [Then("{string} n'a pas de panier")]
    public async Task ThenHasNoCart(string user) => Assert.Equal(0, await carts.CartCountAsync(user));

    private static string LinePath(JsonElement line) => $"/api/cart/lines/{line.GetProperty("lineId").GetGuid()}";

    private static string Money(JsonElement value) => value.GetDecimal().ToString("0.00", CultureInfo.InvariantCulture);
}
