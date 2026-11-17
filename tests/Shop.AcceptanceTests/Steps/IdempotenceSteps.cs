using System.Net;
using Reqnroll;
using Shop.AcceptanceTests.Support;
using Shop.Application.Carts;
using Shop.Testing.Hosting;

namespace Shop.AcceptanceTests.Steps;

/// <summary>Étapes de la clé d'idempotence des commandes du panier (idempotence.feature, CART-R18).</summary>
[Binding]
public sealed class IdempotenceSteps(CartWorld carts, ShopTestCluster cluster, ScenarioHttp http)
{
    private const string ReplayedHeader = IdempotencyKeyFormat.ReplayedHeaderName;

    [Given("la confirmation du prochain enregistrement du panier sera perdue")]
    public void NextCommitAcknowledgementIsLost() => cluster.Doubles.LostCommits.ArmOnce();

    [Given("il s'est écoulé {int} heures")]
    public void HoursElapsed(int hours) => cluster.Doubles.Clock.Advance(TimeSpan.FromHours(hours));

    [When("{string} ajoute {int} {string} à son panier avec la clé {string}")]
    public async Task AddWithKey(string user, int quantity, string product, string key) =>
        await carts.CommandAsync(user, HttpMethod.Post, "/api/cart/lines",
            new { productId = await carts.ProductIdAsync(product), quantity }, idempotencyKey: key);

    [Given("{string} a ajouté {int} {string} à son panier avec la clé {string}")]
    public async Task GivenAddedWithKey(string user, int quantity, string product, string key) =>
        await carts.GivenAsync(user, HttpMethod.Post, "/api/cart/lines",
            new { productId = await carts.ProductIdAsync(product), quantity }, idempotencyKey: key);

    [When("{string} remplace la quantité de la ligne {string} par {int} avec la version {int} et la clé {string}")]
    public async Task ChangeWithKey(string user, string product, int quantity, long version, string key) =>
        await carts.CommandAsync(user, HttpMethod.Put, $"/api/cart/lines/{await carts.LineIdAsync(user, product)}",
            new { quantity, expectedVersion = version }, idempotencyKey: key);

    [When("{string} supprime la ligne {string} avec la version {int} et la clé {string}")]
    public async Task RemoveWithKey(string user, string product, long version, string key) =>
        await carts.CommandAsync(user, HttpMethod.Delete,
            $"/api/cart/lines/{await carts.LineIdAsync(user, product)}?expectedVersion={version}", idempotencyKey: key);

    [When("{string} envoie simultanément {int} fois l'ajout de {int} {string} avec la clé {string}, en alternant les instances {int} et {int}")]
    public async Task AddConcurrentlyWithKey(string user, int times, int quantity, string product, string key, int first, int second)
    {
        var productId = await carts.ProductIdAsync(product);
        await carts.RunConcurrentlyAsync([.. Enumerable.Range(0, times).Select(index => new PendingCartRequest(
            index % 2 == 0 ? first : second, user, HttpMethod.Post, "/api/cart/lines", new { productId, quantity }, quantity, key))]);
        await carts.FocusOnAsync(user);
    }

    [Then("la réponse est un rejeu de la réponse d'origine")]
    public void ResponseIsReplay() =>
        Assert.True(IsReplay(http.Last), $"L'en-tête {ReplayedHeader} est absent : {http.LastBody}");

    [Then("la réponse n'est pas un rejeu")]
    public void ResponseIsNotReplay() => Assert.False(IsReplay(http.Last), "La réponse ne devrait pas être un rejeu.");

    [Then("les {int} demandes réussissent avec le même panier en réponse")]
    public void AllSucceedWithSameCart(int count)
    {
        Assert.Equal(count, carts.ConcurrentResults.Count);
        Assert.All(carts.ConcurrentResults, result => Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode));
        Assert.Single(carts.ConcurrentResults.Select(result => result.Body!.Value.GetRawText()).Distinct());
    }

    [Then("exactement {int} demande n'est pas un rejeu")]
    public void ExactlyNotReplayed(int count) =>
        Assert.Equal(count, carts.ConcurrentResults.Count(result => !IsReplay(result.Response)));

    private static bool IsReplay(HttpResponseMessage response) =>
        response.Headers.TryGetValues(ReplayedHeader, out var values) && values.Single() == "true";
}
