using Medallion.Threading.SqlServer;
using Microsoft.Extensions.Configuration;
using Shop.Application.Catalog;

namespace Shop.Infrastructure.Catalog;

/// <summary>
/// Verrou distribué de la synchronisation (CAT-R10), basé sur sp_getapplock (bibliothèque DistributedLock.SqlServer).
/// Portée session, sur une connexion dédiée : si le processus meurt, SQL Server libère le verrou à la fermeture
/// de la connexion (CAT-R10.4) ; si la connexion est rompue, <see cref="ICatalogSyncLockHandle.LostToken"/> est signalé.
/// </summary>
internal sealed class SqlCatalogSyncLock(IConfiguration configuration) : ICatalogSyncLock
{
    public const string LockName = "catalog-sync";

    public async Task<ICatalogSyncLockHandle?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString(DependencyInjection.ConnectionStringName)!;
        var distributedLock = new SqlDistributedLock(LockName, connectionString, options => options
            // Surveillance de la connexion : alimente HandleLostToken.
            .KeepaliveCadence(TimeSpan.FromSeconds(30)));

        var handle = await distributedLock.TryAcquireAsync(TimeSpan.Zero, cancellationToken);
        return handle is null ? null : new Handle(handle);
    }

    private sealed class Handle(SqlDistributedLockHandle handle) : ICatalogSyncLockHandle
    {
        public CancellationToken LostToken => handle.HandleLostToken;

        public ValueTask DisposeAsync() => handle.DisposeAsync();
    }
}
