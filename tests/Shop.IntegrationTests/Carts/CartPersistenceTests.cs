using System.Net;
using System.Net.Http.Headers;
using Microsoft.Data.SqlClient;
using Shop.Domain.Carts;

namespace Shop.IntegrationTests.Carts;

/// <summary>Contraintes de la base (filet de sécurité des règles du domaine) et non-régressions de la plateforme.</summary>
public sealed class CartPersistenceTests(ClusterFixture fixture) : IntegrationTest(fixture)
{
    private CartApi Api => new(Cluster);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Api.SeedCatalogAsync(("A", 10.00m), ("B", 2.50m));
    }

    [Fact(DisplayName = "La base refuse une quantité hors de [1, 999]")]
    [Trait("Spec", "CART-R4")]
    public async Task Database_rejects_out_of_range_quantity()
    {
        var cartId = await CreateCartWithLineAsync("alice");

        var exception = await Assert.ThrowsAsync<SqlException>(() =>
            ExecuteSqlAsync($"UPDATE dbo.CartLines SET Quantity = 1000 WHERE CartId = '{cartId}';"));
        Assert.Equal(547, exception.Number); // violation de contrainte CHECK
    }

    [Fact(DisplayName = "La base refuse deux lignes pour le même produit dans un panier")]
    [Trait("Spec", "CART-R5")]
    public async Task Database_rejects_duplicate_product_line()
    {
        var cartId = await CreateCartWithLineAsync("alice");

        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteSqlAsync($"""
            INSERT INTO dbo.CartLines (Id, CartId, ProductId, ProductName, UnitPrice, Quantity, Version)
            VALUES (NEWID(), '{cartId}', 1, N'A', 10, 1, 1);
            """));
        Assert.Equal(2601, exception.Number);
    }

    [Fact(DisplayName = "La base refuse deux paniers pour le même utilisateur")]
    [Trait("Spec", "CART-R1")]
    public async Task Database_rejects_second_cart_for_user()
    {
        await CreateCartWithLineAsync("alice");

        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteSqlAsync("""
            INSERT INTO dbo.Carts (Id, UserId, Version, CreatedAt, UpdatedAt)
            VALUES (NEWID(), N'alice', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """));
        Assert.Equal(2601, exception.Number);
    }

    [Fact(DisplayName = "L'identifiant utilisateur est sensible à la casse : « alice » et « Alice » ont deux paniers")]
    [Trait("Spec", "CART-R2")]
    public async Task User_id_is_case_sensitive()
    {
        using var lower = await Api.PutCartAsync("alice");
        using var upper = await Api.PutCartAsync("Alice");

        Assert.Equal(HttpStatusCode.Created, lower.StatusCode);
        Assert.Equal(HttpStatusCode.Created, upper.StatusCode);
        Assert.NotEqual(
            (await CartApi.JsonAsync(lower)).GetProperty("id").GetGuid(),
            (await CartApi.JsonAsync(upper)).GetProperty("id").GetGuid());
    }

    // --- Non-régressions issues de la revue du bloc 1 (docs/ai-reviews.md) ---

    [Fact(DisplayName = "Une réponse 503 (panne de base) indique toujours l'instance qui l'a traitée")]
    [Trait("Spec", "PLT-1")]
    [Trait("Spec", "CART-R12")]
    public async Task Service_unavailable_response_carries_instance_header()
    {
        Cluster.Doubles.Faults.ArmFor<Cart>();

        using var response = await Api.AddAsync("alice", productId: 1, quantity: 1, instance: 2);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("instance-2", response.Headers.GetValues("X-Instance").Single());
    }

    [Fact(DisplayName = "Sans X-User-Id et avec Accept: text/plain, la réponse reste 401 (et non 500)")]
    [Trait("Spec", "PLT-2")]
    public async Task Unauthorized_with_non_json_accept_is_still_401()
    {
        using var client = Cluster.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/cart", UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        using var response = await client.SendAsync(request, CartApi.Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> CreateCartWithLineAsync(string user)
    {
        using var response = await Api.AddAsync(user, productId: 1, quantity: 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await CartApi.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new SqlConnection(Cluster.ConnectionString);
        await connection.OpenAsync(CartApi.Ct);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CartApi.Ct);
    }
}
