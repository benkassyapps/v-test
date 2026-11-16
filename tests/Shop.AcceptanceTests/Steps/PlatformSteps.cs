using System.Text.Json;
using Reqnroll;
using Shop.AcceptanceTests.Support;
using Shop.Testing.Hosting;

namespace Shop.AcceptanceTests.Steps;

/// <summary>Étapes des exigences transverses (platform/acceptance/plateforme.feature).</summary>
[Binding]
public sealed class PlatformSteps(ShopTestCluster cluster, ScenarioHttp http)
{
    private const string AbsentHeader = "<absent>";
    private readonly List<HttpResponseMessage> _responses = [];

    [When("une requête {string} est envoyée avec l'en-tête X-User-Id {string}")]
    public async Task SendWithUserHeader(string request, string userId)
    {
        var parts = request.Split(' ', 2);
        using var client = cluster.CreateClient();
        using var message = new HttpRequestMessage(new HttpMethod(parts[0]), new Uri(parts[1], UriKind.Relative));
        if (userId != AbsentHeader)
        {
            message.Headers.TryAddWithoutValidation("X-User-Id", userId);
        }

        await http.SendAsync(client, message);
    }

    [Then("la réponse est au format {string} avec un type, un titre, un statut et un détail en français")]
    public void ProblemFormat(string mediaType)
    {
        Assert.Equal(mediaType, http.Last.Content.Headers.ContentType?.MediaType);
        var problem = http.LastJson();
        Assert.StartsWith("https://errors.shop.local/", problem.GetProperty("type").GetString(), StringComparison.Ordinal);
        Assert.Equal((int)http.Last.StatusCode, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        var detail = problem.GetProperty("detail").GetString();
        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.Matches(@"\b(le|la|les|de|du|des|pas|votre|vous|n')\b", detail);
    }

    [Then("la réponse ne contient ni pile d'appels ni requête SQL")]
    public void NoInternalDetails()
    {
        foreach (var marker in new[] { "   at ", "Exception", "SELECT ", "INSERT ", "Microsoft.Data.SqlClient", "StackTrace" })
        {
            Assert.DoesNotContain(marker, http.LastBody, StringComparison.Ordinal);
        }
    }

    [Then("la réponse détaille l'erreur du champ {string}")]
    public void FieldErrorDetailed(string field) =>
        Assert.True(http.LastJson().GetProperty("errors").TryGetProperty(field, out var messages) && messages.GetArrayLength() > 0,
            $"Pas d'erreur détaillée pour le champ {field} : {http.LastBody}");

    [When("le client liste les produits via l'instance {int} puis via l'instance {int}")]
    public async Task ListThroughInstances(int first, int second)
    {
        foreach (var instance in new[] { first, second })
        {
            using var client = cluster.CreateClient(instance);
            _responses.Add(await client.GetAsync(new Uri("/api/products", UriKind.Relative)));
        }
    }

    [Then("les deux réponses portent un en-tête X-Instance avec des valeurs différentes")]
    public void DifferentInstances()
    {
        var instances = _responses.Select(response => response.Headers.GetValues("X-Instance").Single()).ToList();
        Assert.Equal(2, instances.Count);
        Assert.NotEqual(instances[0], instances[1]);
    }

    [When("le client consulte l'état de santé du service")]
    public Task Health() => GetAsync("/health");

    [Then("l'état indiqué est {string}")]
    public void HealthIs(string state) => Assert.Equal(state, http.LastBody);

    [When("le client récupère la description OpenAPI du service")]
    public Task OpenApi() => GetAsync("/openapi/v1.json");

    [Then("la description contient les chemins {string}, {string}, {string} et {string}")]
    public void OpenApiContainsPaths(string first, string second, string third, string fourth)
    {
        var paths = http.LastJson().GetProperty("paths").EnumerateObject().Select(path => path.Name).ToList();
        foreach (var expected in new[] { first, second, third, fourth })
        {
            Assert.Contains(paths, path => path == expected || path.StartsWith(expected + "/", StringComparison.Ordinal));
        }
    }

    private async Task GetAsync(string path)
    {
        using var client = cluster.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        await http.SendAsync(client, request);
    }
}
