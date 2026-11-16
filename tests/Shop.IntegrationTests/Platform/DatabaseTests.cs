using Microsoft.EntityFrameworkCore;

namespace Shop.IntegrationTests.Platform;

public sealed class DatabaseTests(ClusterFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = "Toutes les migrations sont appliquées")]
    [Trait("Spec", "PLT-5")]
    public async Task All_migrations_are_applied()
    {
        await using var db = Cluster.CreateDbContext();

        var pending = await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(pending);
    }

    [Fact(DisplayName = "READ_COMMITTED_SNAPSHOT est actif après migration")]
    [Trait("Spec", "PLT-5")]
    public async Task Read_committed_snapshot_is_enabled()
    {
        await using var db = Cluster.CreateDbContext();

        var enabled = await db.Database
            .SqlQuery<bool>($"SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE name = DB_NAME()")
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.True(enabled);
    }
}
