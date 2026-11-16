using System.Net;
using Microsoft.Data.SqlClient;

namespace Shop.IntegrationTests.Carts;

/// <summary>
/// Stress de concurrence RÉPÉTÉ (T4.4) : un test de concurrence qui passe une fois prouve peu de chose. Chaque cas est
/// rejoué de nombreuses fois, requêtes réparties sur les 2 instances et lâchées ensemble par une barrière, puis un
/// invariant global est vérifié directement en SQL. Un utilisateur distinct par itération : pas de remise à zéro.
/// </summary>
public sealed class CartConcurrencyStressTests(ClusterFixture fixture) : IntegrationTest(fixture)
{
    private const int Iterations = 30;

    private CartApi Api => new(Cluster);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await Api.SeedCatalogAsync(("A", 10.00m), ("B", 2.50m), ("C", 1.00m));
    }

    [Fact(DisplayName = "Stress : des créations simultanées ne produisent jamais qu'un panier")]
    [Trait("Spec", "CART-R1")]
    [Trait("Spec", "PLT-1")]
    public async Task Concurrent_creations_produce_one_cart()
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var user = $"creation-{iteration}";
            var responses = await CartApi.ConcurrentlyAsync([.. Enumerable.Range(0, 8)
                .Select(index => (Func<Task<HttpResponseMessage>>)(() => Api.PutCartAsync(user, instance: (index % 2) + 1)))]);

            Assert.All(responses, response => Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created));
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            await AssertInvariantsAsync(user, expectedVersion: 0);
        }
    }

    [Fact(DisplayName = "Stress : des premiers ajouts simultanés sans panier créent un panier et conservent toutes les quantités")]
    [Trait("Spec", "CART-R1")]
    [Trait("Spec", "CART-R5")]
    public async Task Concurrent_first_adds_keep_every_quantity()
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var user = $"premiers-ajouts-{iteration}";
            var responses = await CartApi.ConcurrentlyAsync([.. Enumerable.Range(0, 6)
                .Select(index => (Func<Task<HttpResponseMessage>>)(() => Api.AddAsync(user, productId: 1, quantity: 2, instance: (index % 2) + 1)))]);

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var line = await Api.LineAsync(user, "A");
            Assert.Equal((12, 6L), (line.GetProperty("quantity").GetInt32(), line.GetProperty("version").GetInt64()));
            await AssertInvariantsAsync(user, expectedVersion: 6);
        }
    }

    [Fact(DisplayName = "Stress : deux modifications de lignes différentes réussissent toujours toutes les deux")]
    [Trait("Spec", "CART-R9")]
    public async Task Changes_on_different_lines_always_both_succeed()
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var user = $"alice-{iteration}";
            (await Api.AddAsync(user, 1, 1)).Dispose();
            (await Api.AddAsync(user, 2, 1)).Dispose();
            var (lineA, lineB) = (await Api.LineAsync(user, "A"), await Api.LineAsync(user, "B"));

            var responses = await CartApi.ConcurrentlyAsync(
                () => Api.ChangeAsync(user, lineA.GetProperty("lineId").GetGuid(), 3, expectedVersion: 1, instance: 1),
                () => Api.ChangeAsync(user, lineB.GetProperty("lineId").GetGuid(), 7, expectedVersion: 1, instance: 2));

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            await AssertInvariantsAsync(user, expectedVersion: 4);
        }
    }

    [Fact(DisplayName = "Stress : deux modifications de la même ligne à partir de la même version donnent exactement un succès")]
    [Trait("Spec", "CART-R9")]
    [Trait("Spec", "CART-R8")]
    public async Task Changes_on_same_line_accept_exactly_one()
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var user = $"meme-ligne-{iteration}";
            (await Api.AddAsync(user, 1, 1)).Dispose();
            var lineId = (await Api.LineAsync(user, "A")).GetProperty("lineId").GetGuid();

            var responses = await CartApi.ConcurrentlyAsync(
                () => Api.ChangeAsync(user, lineId, 3, expectedVersion: 1, instance: 1),
                () => Api.ChangeAsync(user, lineId, 8, expectedVersion: 1, instance: 2));

            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            await AssertInvariantsAsync(user, expectedVersion: 2);
        }
    }

    [Fact(DisplayName = "Stress : une suppression et une modification simultanées de la même ligne ne la font jamais réapparaître")]
    [Trait("Spec", "CART-R10")]
    public async Task Delete_and_change_never_resurrect_the_line()
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var user = $"suppression-{iteration}";
            (await Api.AddAsync(user, 1, 2)).Dispose();
            var lineId = (await Api.LineAsync(user, "A")).GetProperty("lineId").GetGuid();

            var responses = await CartApi.ConcurrentlyAsync(
                () => Api.RemoveAsync(user, lineId, expectedVersion: 1, instance: 1),
                () => Api.ChangeAsync(user, lineId, 5, expectedVersion: 1, instance: 2));

            var (removal, change) = (responses[0], responses[1]);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var lines = (await Api.CartAsync(user)).GetProperty("lines");
            if (removal.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
                Assert.Equal(0, lines.GetArrayLength());
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, removal.StatusCode);
                Assert.Equal(5, lines[0].GetProperty("quantity").GetInt32());
            }

            await AssertInvariantsAsync(user, expectedVersion: 2);
        }
    }

    [Fact(DisplayName = "Stress : un ajout et une synchronisation qui retire le produit, en parallèle, sans interblocage ni incohérence")]
    [Trait("Spec", "CART-R3")]
    public async Task Add_racing_a_discontinuing_sync_stays_consistent()
    {
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var user = $"course-sync-{iteration}";

            // Le produit C est présent chez le fournisseur ; la synchronisation concurrente le retire.
            await Api.SeedCatalogAsync(("A", 10.00m), ("B", 2.50m), ("C", 1.00m));
            Cluster.Doubles.Provider.Remove(3);

            var add = Api.AddAsync(user, productId: 3, quantity: 1, instance: 2);
            var sync = Api.SyncAsync(force: true);
            await Task.WhenAll(add, sync);
            using var response = await add;

            // Soit l'ajout est passé avant le retrait (le produit retiré reste alors dans le panier, CART-R11),
            // soit il a vu le produit retiré et a été refusé : jamais d'erreur, jamais d'interblocage.
            Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync(CartApi.Ct));
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.False((await Api.LineAsync(user, "C")).GetProperty("productAvailable").GetBoolean());
            }
        }
    }

    [Fact(DisplayName = "Une commande qui a attendu le verrou du panier décide sur l'état validé APRÈS son attente")]
    [Trait("Spec", "CART-R8")]
    [Trait("Spec", "CART-R9")]
    public async Task Command_waiting_for_lock_reads_state_committed_meanwhile()
    {
        // Cas déterministe (specs/cart/design.md §3) : une transaction concurrente tient le verrou du panier et modifie
        // la ligne A ; la commande de l'API doit attendre, puis décider sur l'état validé pendant son attente
        // (ligne en version 2 → 409), jamais sur la version 1 lue avant.
        const string user = "attente-verrou";
        (await Api.AddAsync(user, 1, 1)).Dispose();
        var lineId = (await Api.LineAsync(user, "A")).GetProperty("lineId").GetGuid();

        await using var connection = new SqlConnection(Cluster.ConnectionString);
        await connection.OpenAsync(CartApi.Ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(CartApi.Ct);
        await using (var concurrent = new SqlCommand("""
            SELECT Id FROM dbo.Carts WITH (UPDLOCK, ROWLOCK) WHERE UserId = @user;
            UPDATE dbo.CartLines SET Quantity = 3, Version = 2 WHERE Id = @line;
            UPDATE dbo.Carts SET Version = Version + 1 WHERE UserId = @user;
            """, connection, transaction))
        {
            concurrent.Parameters.AddWithValue("@user", user);
            concurrent.Parameters.AddWithValue("@line", lineId);
            await concurrent.ExecuteNonQueryAsync(CartApi.Ct);
        }

        var waiting = Api.ChangeAsync(user, lineId, 8, expectedVersion: 1, instance: 2);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CartApi.Ct);
        Assert.False(waiting.IsCompleted, "La commande aurait dû attendre le verrou du panier.");

        await transaction.CommitAsync(CartApi.Ct);
        using var response = await waiting;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, (await CartApi.JsonAsync(response)).GetProperty("currentVersion").GetInt64());
        Assert.Equal(3, (await Api.LineAsync(user, "A")).GetProperty("quantity").GetInt32());
    }

    [Fact(DisplayName = "La première création d'un panier ne bloque pas le panier d'un autre utilisateur")]
    [Trait("Spec", "CART-R1")]
    [Trait("Spec", "PLT-1")]
    public async Task First_cart_creation_does_not_block_another_users_cart()
    {
        // Bob a un panier ; sa clé suit immédiatement celle d'Alice dans l'index UX_Carts_UserId.
        (await Api.AddAsync("bob-voisin", 2, 1)).Dispose();
        var bobLine = (await Api.LineAsync("bob-voisin", "B")).GetProperty("lineId").GetGuid();

        // Une synchronisation (simulée) tient un verrou exclusif sur le produit A.
        await using var connection = new SqlConnection(Cluster.ConnectionString);
        await connection.OpenAsync(CartApi.Ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(CartApi.Ct);
        await using (var hold = new SqlCommand("UPDATE dbo.Products SET Stock = Stock WHERE Id = 1;", connection, transaction))
        {
            await hold.ExecuteNonQueryAsync(CartApi.Ct);
        }

        // Le premier ajout d'Alice (sans panier) attend le produit A…
        var aliceAdd = Api.AddAsync("alice-voisine", 1, 1, instance: 1);
        await Task.Delay(TimeSpan.FromMilliseconds(300), CartApi.Ct);
        Assert.False(aliceAdd.IsCompleted, "L'ajout d'Alice aurait dû attendre le produit verrouillé.");

        // …sans bloquer Bob : aucune serrure de plage sur la clé voisine (création optimiste, sans HOLDLOCK).
        var bobChange = Api.ChangeAsync("bob-voisin", bobLine, 2, expectedVersion: 1, instance: 2);
        var finished = await Task.WhenAny(bobChange, Task.Delay(TimeSpan.FromSeconds(5), CartApi.Ct));
        Assert.Same(bobChange, finished);
        using (var bobResponse = await bobChange)
        {
            Assert.Equal(HttpStatusCode.OK, bobResponse.StatusCode);
        }

        await transaction.CommitAsync(CartApi.Ct);
        using var aliceResponse = await aliceAdd;
        Assert.Equal(HttpStatusCode.OK, aliceResponse.StatusCode);
    }

    /// <summary>Invariant global vérifié en SQL : un seul panier pour l'utilisateur, et la version attendue.</summary>
    private async Task AssertInvariantsAsync(string user, long expectedVersion)
    {
        await using var connection = new SqlConnection(Cluster.ConnectionString);
        await connection.OpenAsync(CartApi.Ct);
        await using var command = new SqlCommand("SELECT COUNT(*), MAX(Version) FROM dbo.Carts WHERE UserId = @user;", connection);
        command.Parameters.AddWithValue("@user", user);
        await using var reader = await command.ExecuteReaderAsync(CartApi.Ct);
        await reader.ReadAsync(CartApi.Ct);

        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(expectedVersion, reader.GetInt64(1));
    }
}
