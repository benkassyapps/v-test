using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shop.Application.Common;

namespace Shop.Infrastructure.Persistence;

/// <summary>
/// Contrôle que READ_COMMITTED_SNAPSHOT est actif (PLT-5).
/// Simple avertissement : la correction des règles métier n'en dépend pas, seules les lectures pourraient être bloquées.
/// Exécuté en arrière-plan pour ne jamais retarder le démarrage de l'instance si la base est lente ou injoignable.
/// </summary>
internal sealed partial class ReadCommittedSnapshotCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<ReadCommittedSnapshotCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Cède immédiatement la main : le démarrage de l'hôte n'attend pas la base.
        await Task.Yield();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var enabled = await db.Database
                .SqlQuery<bool>($"SELECT is_read_committed_snapshot_on AS [Value] FROM sys.databases WHERE name = DB_NAME()")
                .SingleAsync(stoppingToken);

            if (!enabled)
            {
                LogDisabled(logger);
            }
        }
#pragma warning disable CA1031 // Le contrôle est indicatif : il ne doit jamais arrêter l'instance.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogCheckFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 2000, EventName = nameof(LogEvents.ReadCommittedSnapshotDisabled), Level = LogLevel.Warning,
        Message = "READ_COMMITTED_SNAPSHOT est désactivé : les lectures peuvent être bloquées par les écritures en cours. Appliquer les migrations avec le migrateur.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
        Message = "Impossible de vérifier READ_COMMITTED_SNAPSHOT au démarrage.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
