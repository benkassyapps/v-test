using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shop.Application.Common;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Carts;

/// <summary>
/// Purge périodique des clés d'idempotence expirées (CART-R18.7). Exécutée sur chaque instance : la suppression
/// ensembliste est sans danger en parallèle (au pire, une instance ne trouve plus rien à supprimer). Une clé expirée
/// non encore purgée est de toute façon ignorée et remplacée lors de sa réutilisation.
/// </summary>
internal sealed partial class IdempotencyKeyCleanupService(
    IDbContextFactory<ShopDbContext> contexts,
    TimeProvider clock,
    ILogger<IdempotencyKeyCleanupService> logger) : BackgroundService
{
    private const int BatchSize = 1000;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(stoppingToken);
                var total = await PurgeExpiredAsync(db, clock.GetUtcNow(), BatchSize, stoppingToken);
                if (total > 0)
                {
                    LogPurged(logger, total);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Une purge en échec ne doit jamais arrêter l'instance : la suivante réessaiera.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogFailed(logger, exception);
            }
        }
    }

    /// <summary>
    /// Supprime les clés expirées par LOTS de taille bornée (sous le seuil d'escalade de verrous de SQL Server, ~5 000) :
    /// jamais de verrou de table bloquant les commandes du panier, transactions courtes. Un interblocage éventuel avec
    /// la suppression d'une clé expirée réutilisée au même moment n'est pas grave : la purge reprendra au tick suivant.
    /// </summary>
    internal static async Task<int> PurgeExpiredAsync(ShopDbContext db, DateTimeOffset now, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlAsync(
                $"DELETE TOP ({batchSize}) FROM dbo.IdempotencyKeys WHERE ExpiresAt <= {now}", cancellationToken);
            total += deleted;
        }
        while (deleted == batchSize);

        return total;
    }

    [LoggerMessage(EventId = 3000, EventName = nameof(LogEvents.IdempotencyKeysPurged), Level = LogLevel.Information,
        Message = "{Count} clé(s) d'idempotence expirée(s) supprimée(s).")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(EventId = 3001, EventName = nameof(LogEvents.IdempotencyKeysPurgeFailed), Level = LogLevel.Warning,
        Message = "Échec de la purge des clés d'idempotence expirées.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
