using Shop.IntegrationTests;
using Shop.Testing.Hosting;

[assembly: AssemblyFixture(typeof(ClusterFixture))]

// Une seule base partagée, remise à zéro avant chaque test : les tests s'exécutent séquentiellement.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Shop.IntegrationTests;

/// <summary>Cluster de deux instances sur SQL Server (Testcontainers), partagé par tous les tests de l'assembly.</summary>
public sealed class ClusterFixture : IAsyncLifetime
{
    public ShopTestCluster Cluster { get; } = new();

    public async ValueTask InitializeAsync() => await Cluster.InitializeAsync();

    public ValueTask DisposeAsync() => Cluster.DisposeAsync();
}

/// <summary>Base des tests d'intégration : remise à zéro de la base et des doublures avant chaque test.</summary>
public abstract class IntegrationTest(ClusterFixture fixture) : IAsyncLifetime
{
    protected ShopTestCluster Cluster => fixture.Cluster;

    public virtual async ValueTask InitializeAsync() => await Cluster.ResetAsync();

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
