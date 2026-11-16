using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shop.Application.Catalog;
using Shop.Application.Common;

namespace Shop.Infrastructure.Catalog;

/// <summary>
/// Planificateur de la synchronisation, actif sur CHAQUE instance (CAT-R2) : à chaque tick, une vérification peu
/// coûteuse ; le verrou distribué garantit qu'une seule instance synchronise (CAT-R10).
/// </summary>
internal sealed partial class CatalogSyncHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<CatalogSyncOptions> options,
    TimeProvider clock,
    ILogger<CatalogSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SchedulerEnabled)
        {
            LogDisabled(logger);
            return;
        }

        using var timer = new PeriodicTimer(options.Value.TickInterval, clock);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CatalogSyncScheduler>().CheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Un tick en échec ne doit jamais arrêter le planificateur : le suivant réessaiera.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogTickFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 1008, EventName = nameof(LogEvents.CatalogSchedulerDisabled), Level = LogLevel.Information,
        Message = "Planificateur de synchronisation désactivé sur cette instance (CatalogSync:SchedulerEnabled = false).")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 1007, EventName = nameof(LogEvents.CatalogSchedulerTickFailed), Level = LogLevel.Error,
        Message = "Erreur inattendue lors de la vérification du planificateur de synchronisation.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
