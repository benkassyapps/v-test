using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Catalog;
using Shop.Domain.Catalog;
using Shop.Domain.Common;
using Shop.Testing.Fakes;

namespace Shop.IntegrationTests.Catalog;

public sealed class CatalogInfrastructureTests(ClusterFixture fixture) : IntegrationTest(fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // --- Fournisseur DummyJSON (CAT-R1, CAT-R7) ---

    [Fact(DisplayName = "Le fournisseur parcourt toutes les pages jusqu'au total annoncé")]
    [Trait("Spec", "CAT-R1")]
    public async Task Provider_reads_every_page()
    {
        Cluster.Doubles.Provider.SetProducts(Products(5));
        Cluster.Doubles.Provider.MaxPageSize = 2;

        var snapshot = await WithService<ICatalogProvider, Result<CatalogSnapshot>>(1, provider => provider.FetchSnapshotAsync(Ct));

        Assert.True(snapshot.IsSuccess);
        Assert.Equal([1, 2, 3, 4, 5], snapshot.Value.Products.Select(product => product.ExternalId));
        Assert.Equal(3, Cluster.Doubles.Provider.RequestCount);
    }

    [Fact(DisplayName = "Un total annoncé supérieur aux produits renvoyés donne un instantané invalide")]
    [Trait("Spec", "CAT-R1")]
    public async Task Provider_detects_incomplete_snapshot()
    {
        Cluster.Doubles.Provider.SetProducts(Products(3));
        Cluster.Doubles.Provider.AnnouncedTotalOverride = 4;

        var snapshot = await WithService<ICatalogProvider, Result<CatalogSnapshot>>(1, provider => provider.FetchSnapshotAsync(Ct));

        Assert.False(snapshot.IsSuccess);
        Assert.Equal(CatalogErrors.InvalidSnapshotReason, snapshot.Error.Details!["reason"]);
    }

    [Fact(DisplayName = "Un fournisseur en panne donne un échec explicite après les nouvelles tentatives, sans exception")]
    [Trait("Spec", "CAT-R7")]
    public async Task Provider_failure_is_reported()
    {
        Cluster.Doubles.Provider.Unavailable = true;

        var snapshot = await WithService<ICatalogProvider, Result<CatalogSnapshot>>(1, provider => provider.FetchSnapshotAsync(Ct));

        Assert.False(snapshot.IsSuccess);
        Assert.Equal(CatalogErrors.ProviderErrorReason, snapshot.Error.Details!["reason"]);
        Assert.Equal(2, Cluster.Doubles.Provider.RequestCount); // 1 essai + 1 nouvelle tentative (configuration de test)
    }

    [Fact(DisplayName = "Un corps de réponse qui cesse d'arriver après les en-têtes n'immobilise pas la synchronisation")]
    [Trait("Spec", "CAT-R8")]
    [Trait("Spec", "CAT-R7")]
    public async Task Stalled_response_body_fails_within_page_timeout()
    {
        // Cas observé en vérification manuelle : en-têtes reçus, puis plus rien. Les délais du pipeline de résilience
        // s'arrêtent aux en-têtes ; seul le délai par page (5 s en test) borne la lecture du corps.
        Cluster.Doubles.Provider.SetProducts(Products(3));
        Cluster.Doubles.Provider.MaxPageSize = 2;
        Cluster.Doubles.Provider.StallBodyFromPage = 2;
        var started = System.Diagnostics.Stopwatch.StartNew();

        var result = await RunManualAsync(instance: 1);

        Assert.InRange(started.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(20));
        Assert.Equal(ErrorCodes.ProviderUnavailable, result.Error!.Code);
        await using var db = Cluster.CreateDbContext();
        var run = await db.Set<SyncRun>().SingleAsync(Ct);
        Assert.Equal((SyncRunStatus.Failed, SyncFailureReason.ProviderError), (run.Status, run.FailureReason!.Value));
        Assert.Equal(0, await db.Set<Product>().CountAsync(Ct));
    }

    // --- Contraintes (CAT-R4) ---

    [Fact(DisplayName = "La base refuse deux produits avec le même identifiant fournisseur")]
    [Trait("Spec", "CAT-R4")]
    public async Task Database_rejects_duplicate_external_id()
    {
        const string insert = """
            INSERT INTO dbo.Products (ExternalId, Name, Category, Price, Stock, AvailabilityStatus, Status, LastUpdatedAt)
            VALUES (42, N'A', N'c', 1, 1, N'In Stock', 1, SYSDATETIMEOFFSET());
            """;
        await ExecuteSqlAsync(insert);

        var exception = await Assert.ThrowsAsync<SqlException>(() => ExecuteSqlAsync(insert));
        Assert.Equal(2601, exception.Number);
    }

    [Fact(DisplayName = "La page de produits est servie par une recherche de plage sur la clé primaire, sans tri ni parcours complet")]
    [Trait("Spec", "CAT-R6")]
    public async Task Keyset_page_uses_clustered_index_seek()
    {
        Cluster.Doubles.Provider.SetProducts(Products(300));
        Assert.True((await RunManualAsync(instance: 1)).IsSuccess);

        // Même forme que la requête du ProductReader (keyset : Status actif, Id > dernier, ORDER BY Id, TOP n).
        const string query = """
            DECLARE @afterId int = 150, @take int = 21;
            SELECT TOP (@take) Id, ExternalId, Name, Category, Price, Stock, AvailabilityStatus, LastUpdatedAt
            FROM dbo.Products WHERE Status = 1 AND Id > @afterId ORDER BY Id;
            """;

        var plan = await EstimatedPlanAsync(query);

        Assert.Contains("PhysicalOp=\"Clustered Index Seek\"", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicalOp=\"Clustered Index Scan\"", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("PhysicalOp=\"Sort\"", plan, StringComparison.Ordinal);
    }

    // --- Verrou distribué (CAT-R10) ---

    [Fact(DisplayName = "Le verrou de synchronisation est exclusif entre deux instances")]
    [Trait("Spec", "CAT-R10")]
    public async Task Sync_lock_is_exclusive_across_instances()
    {
        await using var first = await AcquireLockAsync(instance: 1);
        Assert.NotNull(first);

        await using var second = await AcquireLockAsync(instance: 2);
        Assert.Null(second);
    }

    [Fact(DisplayName = "Si la connexion de l'instance qui détient le verrou est coupée, le verrou est libéré")]
    [Trait("Spec", "CAT-R10")]
    public async Task Sync_lock_is_released_when_holder_connection_dies()
    {
        var first = await AcquireLockAsync(instance: 1);
        Assert.NotNull(first);
        try
        {
            await KillLockHolderAndReacquireAsync();
        }
        finally
        {
            try
            {
                await first.DisposeAsync();
            }
            catch (SqlException)
            {
                // Connexion déjà tuée : la libération explicite échoue, ce qui est attendu.
            }
        }
    }

    private async Task KillLockHolderAndReacquireAsync()
    {
        // Simule l'arrêt brutal de l'instance : SQL Server tue la session qui porte le verrou applicatif.
        var sessions = await QueryLockSessionsAsync();
        Assert.NotEmpty(sessions);
        foreach (var session in sessions)
        {
            await ExecuteSqlAsync($"KILL {session};");
        }

        await using var second = await AcquireLockAsync(instance: 2);
        Assert.NotNull(second);
    }

    // --- Cohérence en cas d'échec (CAT-R8) et exclusivité (CAT-R10) ---

    [Fact(DisplayName = "Une panne de base pendant l'application laisse le catalogue inchangé et le run en échec")]
    [Trait("Spec", "CAT-R8")]
    public async Task Database_failure_during_apply_changes_nothing()
    {
        Cluster.Doubles.Provider.SetProducts(Products(3));
        Assert.True((await RunManualAsync(instance: 1)).IsSuccess);
        var before = await CatalogStateAsync();

        Cluster.Doubles.Provider.Update(1, product => product with { Title = "Modifié", Price = 99m });
        Cluster.Doubles.Provider.Upsert(new FakeProviderProduct(4, "Nouveau", "cat", 4m));
        Cluster.Doubles.Faults.ArmFor<Product>();

        var result = await RunManualAsync(instance: 1);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ServiceUnavailable, result.Error.Code);
        Assert.Equal(before, await CatalogStateAsync());
        await using var db = Cluster.CreateDbContext();
        var lastRun = await db.Set<SyncRun>().OrderByDescending(run => run.Id).FirstAsync(Ct);
        Assert.Equal((SyncRunStatus.Failed, SyncFailureReason.DatabaseError), (lastRun.Status, lastRun.FailureReason!.Value));
    }

    [Fact(DisplayName = "Deux synchronisations manuelles simultanées sur deux instances : une seule s'exécute")]
    [Trait("Spec", "CAT-R10")]
    public async Task Concurrent_manual_syncs_run_once()
    {
        Cluster.Doubles.Provider.SetProducts(Products(3));
        Cluster.Doubles.Provider.HoldRequests();

        var first = RunManualAsync(instance: 1);
        await Cluster.Doubles.Provider.WaitForHeldRequestAsync(TimeSpan.FromSeconds(30));
        var second = await RunManualAsync(instance: 2);
        Cluster.Doubles.Provider.ReleaseRequests();

        Assert.True((await first).IsSuccess);
        Assert.Equal(ErrorCodes.SyncAlreadyRunning, second.Error!.Code);
        await using var db = Cluster.CreateDbContext();
        Assert.Equal(1, await db.Set<SyncRun>().CountAsync(Ct));
        Assert.Equal(3, await db.Set<Product>().CountAsync(Ct));
    }

    [Fact(DisplayName = "Barrière : une instance dont le run a été repris par une autre n'applique rien")]
    [Trait("Spec", "CAT-R10")]
    public async Task Fenced_apply_is_abandoned_when_run_was_taken_over()
    {
        // Instance A démarre un run puis perd son verrou ; l'instance B déclare ce run interrompu.
        var run = SyncRun.Start(SyncTrigger.Manual, "instance-1", forced: false, Cluster.Doubles.Clock.GetUtcNow());
        await WithService<ISyncRunStore, bool>(1, async runs => { await runs.AddAsync(run, Ct); return true; });
        var interrupted = await WithService<ISyncRunStore, int>(2, runs => runs.InterruptOrphanedRunsAsync(Cluster.Doubles.Clock.GetUtcNow(), Ct));
        Assert.Equal(1, interrupted);

        // A tente malgré tout d'appliquer : la barrière l'en empêche, rien n'est écrit.
        var newProduct = Product.Create(new ProviderProduct(1, "Intrus", "cat", 1m, 1, "In Stock", null), Cluster.Doubles.Clock.GetUtcNow());
        var applied = await WithService<ICatalogStore, Result<AppliedSync>>(1, store => store.ApplyAsync(
            run.Id, _ => new CatalogChanges(new SyncCounts(1, 1, 0, 0, 0), [newProduct]), Ct));

        Assert.False(applied.IsSuccess);
        Assert.Equal(CatalogErrors.InterruptedReason, applied.Error.Details!["reason"]);
        await using var db = Cluster.CreateDbContext();
        Assert.Equal(0, await db.Set<Product>().CountAsync(Ct));
        var stored = await db.Set<SyncRun>().SingleAsync(Ct);
        Assert.Equal((SyncRunStatus.Failed, SyncFailureReason.Interrupted), (stored.Status, stored.FailureReason!.Value));
    }

    [Fact(DisplayName = "Un run terminé n'est jamais réécrit (ni par une fin de run, ni par l'interruption des orphelins)")]
    [Trait("Spec", "CAT-R8")]
    public async Task Finished_run_is_never_overwritten()
    {
        Cluster.Doubles.Provider.SetProducts(Products(2));
        var succeeded = (await RunManualAsync(instance: 1)).Value;

        // Une issue « échec » tardive pour le même run, et une interruption des orphelins, ne doivent rien changer.
        var stale = SyncRun.Start(SyncTrigger.Manual, "instance-1", false, Cluster.Doubles.Clock.GetUtcNow());
        typeof(SyncRun).GetProperty(nameof(SyncRun.Id))!.SetValue(stale, succeeded.Id);
        stale.Fail(SyncFailureReason.DatabaseError, "issue tardive", Cluster.Doubles.Clock.GetUtcNow());
        await WithService<ISyncRunStore, bool>(2, async runs => { await runs.CompleteAsync(stale, Ct); return true; });
        await WithService<ISyncRunStore, int>(2, runs => runs.InterruptOrphanedRunsAsync(Cluster.Doubles.Clock.GetUtcNow(), Ct));

        await using var db = Cluster.CreateDbContext();
        var stored = await db.Set<SyncRun>().SingleAsync(run => run.Id == succeeded.Id, Ct);
        Assert.Equal((SyncRunStatus.Succeeded, 2), (stored.Status, stored.Inserted));
    }

    // --- Outils ---

    private static FakeProviderProduct[] Products(int count) =>
        [.. Enumerable.Range(1, count).Select(id => new FakeProviderProduct(id, $"Produit {id}", "cat", id))];

    private async Task<TResult> WithService<TService, TResult>(int instance, Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = Cluster.Instance(instance).Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private Task<Result<SyncRun>> RunManualAsync(int instance) =>
        WithService<CatalogSyncService, Result<SyncRun>>(instance, sync => sync.RunManualAsync(force: false, Ct));

    private Task<ICatalogSyncLockHandle?> AcquireLockAsync(int instance) =>
        Cluster.Instance(instance).Services.GetRequiredService<ICatalogSyncLock>().TryAcquireAsync(Ct);

    private async Task<List<string>> CatalogStateAsync()
    {
        await using var db = Cluster.CreateDbContext();
        return await db.Set<Product>().AsNoTracking().OrderBy(product => product.ExternalId)
            .Select(product => $"{product.ExternalId}|{product.Name}|{product.Price}|{product.Status}|{product.LastUpdatedAt:O}")
            .ToListAsync(Ct);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new SqlConnection(Cluster.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>Plan d'exécution estimé (SHOWPLAN_XML) : la requête n'est pas exécutée.</summary>
    private async Task<string> EstimatedPlanAsync(string query)
    {
        await using var connection = new SqlConnection(Cluster.ConnectionString);
        await connection.OpenAsync(Ct);
        await using (var on = new SqlCommand("SET SHOWPLAN_XML ON;", connection))
        {
            await on.ExecuteNonQueryAsync(Ct);
        }

        string plan;
        await using (var command = new SqlCommand(query, connection))
        {
            plan = string.Concat(await ReadAllStringsAsync(command));
        }

        await using (var off = new SqlCommand("SET SHOWPLAN_XML OFF;", connection))
        {
            await off.ExecuteNonQueryAsync(Ct);
        }

        return plan;
    }

    private static async Task<List<string>> ReadAllStringsAsync(SqlCommand command)
    {
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        do
        {
            while (await reader.ReadAsync(Ct))
            {
                values.Add(reader.GetString(0));
            }
        }
        while (await reader.NextResultAsync(Ct));

        return values;
    }

    private async Task<List<int>> QueryLockSessionsAsync()
    {
        await using var connection = new SqlConnection(Cluster.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(
            "SELECT DISTINCT request_session_id FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND resource_description LIKE '%catalog-sync%';",
            connection);
        var sessions = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            sessions.Add(reader.GetInt32(0));
        }

        return sessions;
    }
}
