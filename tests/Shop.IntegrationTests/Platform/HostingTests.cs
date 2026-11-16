using System.Net;
using System.Text.Json;

namespace Shop.IntegrationTests.Platform;

public sealed class HostingTests(ClusterFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = "Deux instances partagent la base et s'identifient par l'en-tête X-Instance")]
    [Trait("Spec", "PLT-1")]
    [Trait("Spec", "PLT-6")]
    public async Task Two_instances_identify_themselves()
    {
        var ct = TestContext.Current.CancellationToken;

        using var first = await Cluster.CreateClient(instance: 1).GetAsync(new Uri("/health", UriKind.Relative), ct);
        using var second = await Cluster.CreateClient(instance: 2).GetAsync(new Uri("/health", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("Healthy", await first.Content.ReadAsStringAsync(ct));
        Assert.Equal("instance-1", first.Headers.GetValues("X-Instance").Single());
        Assert.Equal("instance-2", second.Headers.GetValues("X-Instance").Single());
    }

    [Fact(DisplayName = "Une route inconnue renvoie un ProblemDetails avec un code stable et sans détail interne")]
    [Trait("Spec", "PLT-3")]
    public async Task Unknown_route_returns_problem_details()
    {
        var ct = TestContext.Current.CancellationToken;

        using var response = await Cluster.CreateClient().GetAsync(new Uri("/api/inexistant", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("not_found", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("https://errors.shop.local/not_found", problem.RootElement.GetProperty("type").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact(DisplayName = "La description OpenAPI est publiée")]
    [Trait("Spec", "PLT-7")]
    public async Task OpenApi_document_is_published()
    {
        var ct = TestContext.Current.CancellationToken;

        using var response = await Cluster.CreateClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
