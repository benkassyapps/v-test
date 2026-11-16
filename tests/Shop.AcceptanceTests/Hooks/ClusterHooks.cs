using Reqnroll;
using Reqnroll.BoDi;
using Shop.Testing.Hosting;

// Une seule base partagée, remise à zéro avant chaque scénario : exécution séquentielle.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Shop.AcceptanceTests.Hooks;

/// <summary>
/// Cycle de vie du cluster de test : démarré une fois pour toute l'exécution (2 instances + SQL Server),
/// remis à zéro avant chaque scénario, puis mis à disposition des étapes par injection.
/// </summary>
[Binding]
public sealed class ClusterHooks(IObjectContainer scenarioContainer)
{
    private static ShopTestCluster? _cluster;

    [BeforeTestRun]
    public static async Task StartClusterAsync()
    {
        _cluster = new ShopTestCluster();
        await _cluster.InitializeAsync();
    }

    [AfterTestRun]
    public static async Task StopClusterAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.DisposeAsync();
        }
    }

    [BeforeScenario(Order = 0)]
    public async Task ResetAndRegisterClusterAsync()
    {
        var cluster = _cluster ?? throw new InvalidOperationException("Cluster de test non démarré.");
        await cluster.ResetAsync();
        scenarioContainer.RegisterInstanceAs(cluster, dispose: false);
    }
}
