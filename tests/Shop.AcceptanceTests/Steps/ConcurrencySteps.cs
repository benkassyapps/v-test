using System.Globalization;
using System.Net;
using Reqnroll;
using Shop.AcceptanceTests.Support;

namespace Shop.AcceptanceTests.Steps;

/// <summary>Étapes des modifications simultanées sur plusieurs instances (concurrence.feature, creation-consultation.feature).</summary>
[Binding]
public sealed class ConcurrencySteps(CartWorld carts)
{
    [When("{string} envoie simultanément, à partir de cette lecture :")]
    public Task SendConcurrentlyFromRead(string user, DataTable table)
    {
        var read = carts.LastRead(user);
        return carts.RunConcurrentlyAsync([.. table.Rows.Select(row =>
        {
            var line = CartWorld.FindLine(read, row["ligne"])!.Value;
            var quantity = int.Parse(row["nouvelle quantité"], CultureInfo.InvariantCulture);
            return new PendingCartRequest(
                int.Parse(row["instance"], CultureInfo.InvariantCulture),
                user,
                HttpMethod.Put,
                $"/api/cart/lines/{line.GetProperty("lineId").GetGuid()}",
                new { quantity, expectedVersion = line.GetProperty("version").GetInt64() },
                quantity);
        })]);
    }

    [When("{string} envoie simultanément, à partir de cette lecture, la suppression de la ligne {string} via l'instance {int} et la quantité {int} pour la ligne {string} via l'instance {int}")]
    public Task SendDeleteAndChange(string user, string deleted, int deleteInstance, int quantity, string changed, int changeInstance)
    {
        var read = carts.LastRead(user);
        var toDelete = CartWorld.FindLine(read, deleted)!.Value;
        var toChange = CartWorld.FindLine(read, changed)!.Value;
        return carts.RunConcurrentlyAsync(
        [
            new PendingCartRequest(deleteInstance, user, HttpMethod.Delete,
                $"/api/cart/lines/{toDelete.GetProperty("lineId").GetGuid()}?expectedVersion={toDelete.GetProperty("version").GetInt64()}", null, null),
            new PendingCartRequest(changeInstance, user, HttpMethod.Put,
                $"/api/cart/lines/{toChange.GetProperty("lineId").GetGuid()}",
                new { quantity, expectedVersion = toChange.GetProperty("version").GetInt64() }, quantity),
        ]);
    }

    [When("{string} ajoute simultanément {int} fois {int} {string} à son panier, en alternant les instances {int} et {int}")]
    public async Task AddConcurrently(string user, int times, int quantity, string product, int first, int second)
    {
        var productId = await carts.ProductIdAsync(product);
        await carts.RunConcurrentlyAsync([.. Enumerable.Range(0, times).Select(index => new PendingCartRequest(
            index % 2 == 0 ? first : second, user, HttpMethod.Post, "/api/cart/lines", new { productId, quantity }, quantity))]);
        _ = await carts.ReadCartAsyncOrNull(user);
    }

    [When("{string} envoie simultanément l'ajout de {int} {string} via l'instance {int} et l'ajout de {int} {string} via l'instance {int}")]
    public async Task AddTwoConcurrently(string user, int firstQuantity, string firstProduct, int firstInstance, int secondQuantity, string secondProduct, int secondInstance)
    {
        var firstId = await carts.ProductIdAsync(firstProduct);
        var secondId = await carts.ProductIdAsync(secondProduct);
        await carts.RunConcurrentlyAsync(
        [
            new PendingCartRequest(firstInstance, user, HttpMethod.Post, "/api/cart/lines", new { productId = firstId, quantity = firstQuantity }, firstQuantity),
            new PendingCartRequest(secondInstance, user, HttpMethod.Post, "/api/cart/lines", new { productId = secondId, quantity = secondQuantity }, secondQuantity),
        ]);
        _ = await carts.ReadCartAsyncOrNull(user);
    }

    // --- Vérifications ---

    [Then("toutes les réponses ont le statut {int} ou {int}")]
    public void AllResponsesHaveStatus(int first, int second) =>
        Assert.All(carts.ConcurrentResults, result => Assert.Contains((int)result.Response.StatusCode, new[] { first, second }));

    [Then("exactement une réponse a le statut {int}")]
    public void ExactlyOneResponseWithStatus(int status) =>
        Assert.Single(carts.ConcurrentResults, result => (int)result.Response.StatusCode == status);

    [Then("toutes les réponses renvoient le même identifiant de panier")]
    public void AllResponsesSameCart() =>
        Assert.Single(carts.ConcurrentResults.Select(result => result.Body!.Value.GetProperty("id").GetGuid()).Distinct());

    [Then("les {int} demandes réussissent")]
    public void AllSucceed(int count)
    {
        Assert.Equal(count, carts.ConcurrentResults.Count);
        Assert.All(carts.ConcurrentResults, result => Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode));
    }

    [Then("exactement {int} demande réussit et {int} demande est rejetée avec le code {string}")]
    [Then("exactement {int} demandes réussissent et {int} demandes sont rejetées avec le code {string}")]
    public void SomeSucceedOthersRejected(int succeeded, int rejected, string code)
    {
        Assert.Equal(succeeded, carts.ConcurrentResults.Count(result => result.Response.StatusCode == HttpStatusCode.OK));
        var failures = carts.ConcurrentResults.Where(result => result.Response.StatusCode != HttpStatusCode.OK).ToList();
        Assert.Equal(rejected, failures.Count);
        Assert.All(failures, failure => Assert.Equal(code, failure.Body!.Value.GetProperty("code").GetString()));
    }

    [Then("exactement {int} demande réussit")]
    public void ExactlySucceeded(int count) =>
        Assert.Equal(count, carts.ConcurrentResults.Count(result => result.Response.StatusCode == HttpStatusCode.OK));

    [Then("l'autre demande est rejetée avec le code {string} ou {string}")]
    public void OtherRejected(string first, string second)
    {
        var failure = Assert.Single(carts.ConcurrentResults, result => result.Response.StatusCode != HttpStatusCode.OK);
        Assert.Contains(failure.Body!.Value.GetProperty("code").GetString(), new[] { first, second });
    }

    [Then("la quantité de la ligne {string} est celle de la demande acceptée")]
    public async Task QuantityIsAccepted(string product)
    {
        var accepted = Assert.Single(carts.ConcurrentResults, result => result.Response.StatusCode == HttpStatusCode.OK);
        var line = await carts.LineAsync(accepted.Request.User, product);
        Assert.Equal(accepted.Request.RequestedQuantity, line.GetProperty("quantity").GetInt32());
    }

    [Then("l'état final du panier correspond à la seule demande acceptée")]
    public async Task FinalStateMatchesAccepted()
    {
        var accepted = Assert.Single(carts.ConcurrentResults, result => result.Response.StatusCode == HttpStatusCode.OK);
        var cart = await carts.ReadCartAsync(accepted.Request.User);
        if (accepted.Request.Method == HttpMethod.Delete)
        {
            Assert.Equal(0, cart.GetProperty("lines").GetArrayLength());
        }
        else
        {
            Assert.Equal(accepted.Request.RequestedQuantity, cart.GetProperty("lines")[0].GetProperty("quantity").GetInt32());
        }
    }
}
