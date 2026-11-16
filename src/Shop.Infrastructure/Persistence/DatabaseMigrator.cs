using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shop.Application.Common;

namespace Shop.Infrastructure.Persistence;

/// <summary>
/// Applique les migrations EF (PLT-5). Utilisé par le migrateur dédié (argument --migrate),
/// qui s'exécute seul avant les instances : la migration initiale active READ_COMMITTED_SNAPSHOT,
/// ce qui exige d'être la seule connexion à la base.
/// </summary>
public static partial class DatabaseMigrator
{
    public static async Task<bool> MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator).FullName!);
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();

        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await db.Database.MigrateAsync(cancellationToken);
            var names = pending.Count == 0 ? "aucune" : string.Join(", ", pending);
            LogMigrated(logger, pending.Count, names);
            return true;
        }
#pragma warning disable CA1031 // Le migrateur rend un code de sortie au lieu de planter sans trace.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogFailed(logger, exception);
            return false;
        }
    }

    [LoggerMessage(EventId = 2001, EventName = nameof(LogEvents.DatabaseMigrated), Level = LogLevel.Information,
        Message = "Base de données à jour : {Count} migration(s) appliquée(s) ({Migrations}).")]
    private static partial void LogMigrated(ILogger logger, int count, string migrations);

    [LoggerMessage(EventId = 2002, EventName = nameof(LogEvents.DatabaseMigrationFailed), Level = LogLevel.Error,
        Message = "Échec de l'application des migrations.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
