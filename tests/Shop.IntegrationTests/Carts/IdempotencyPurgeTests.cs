using Microsoft.Data.SqlClient;
using Shop.Infrastructure.Carts;

namespace Shop.IntegrationTests.Carts;

/// <summary>Purge des clés d'idempotence expirées (CART-R18.7), par lots.</summary>
public sealed class IdempotencyPurgeTests(ClusterFixture fixture) : IntegrationTest(fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "La purge supprime toutes les clés expirées, sur plusieurs lots, et conserve les clés valides")]
    [Trait("Spec", "CART-R18")]
    public async Task Purge_removes_all_expired_keys_in_batches_and_keeps_live_ones()
    {
        // 5 clés expirées (dont une qui expire exactement maintenant) et une clé encore valide.
        for (var index = 0; index < 5; index++)
        {
            await InsertKeyAsync($"expiree-{index}", Now.AddMinutes(-index));
        }

        await InsertKeyAsync("valide", Now.AddMinutes(1));

        await using var db = Cluster.CreateDbContext();
        var purged = await IdempotencyKeyCleanupService.PurgeExpiredAsync(db, Now, batchSize: 2, CartApi.Ct);

        Assert.Equal(5, purged);
        Assert.Equal(["valide"], await RemainingKeysAsync());
    }

    private async Task InsertKeyAsync(string key, DateTimeOffset expiresAt)
    {
        await using var connection = new SqlConnection(Cluster.ConnectionString);
        await connection.OpenAsync(CartApi.Ct);
        await using var command = new SqlCommand("""
            INSERT INTO dbo.IdempotencyKeys (UserId, [Key], RequestHash, Outcome, CreatedAt, ExpiresAt)
            VALUES (N'alice', @key, REPLICATE('0', 64), N'{}', @createdAt, @expiresAt);
            """, connection);
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@createdAt", expiresAt.AddHours(-24));
        command.Parameters.AddWithValue("@expiresAt", expiresAt);
        await command.ExecuteNonQueryAsync(CartApi.Ct);
    }

    private async Task<List<string>> RemainingKeysAsync()
    {
        await using var connection = new SqlConnection(Cluster.ConnectionString);
        await connection.OpenAsync(CartApi.Ct);
        await using var command = new SqlCommand("SELECT [Key] FROM dbo.IdempotencyKeys ORDER BY [Key];", connection);
        await using var reader = await command.ExecuteReaderAsync(CartApi.Ct);
        var keys = new List<string>();
        while (await reader.ReadAsync(CartApi.Ct))
        {
            keys.Add(reader.GetString(0));
        }

        return keys;
    }
}
