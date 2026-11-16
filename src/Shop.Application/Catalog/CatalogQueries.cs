using Microsoft.Extensions.Options;
using Shop.Domain.Catalog;
using Shop.Domain.Common;

namespace Shop.Application.Catalog;

public sealed record ProductPage(IReadOnlyList<ProductView> Items, string? NextCursor, bool HasMore);

public sealed record SyncRunPage(IReadOnlyList<SyncRun> Items, long? NextBeforeId, bool HasMore);

/// <summary>État de la synchronisation pour l'exploitant (CAT-R12).</summary>
public sealed record SyncStatus(
    SyncRun? LastSuccess,
    TimeSpan? LastSuccessAge,
    SyncRun? LastRun,
    int ConsecutiveFailures,
    DateTimeOffset NextScheduledAttemptAt,
    SyncActionAdvice Advice);

/// <summary>Lectures du catalogue et de l'historique de synchronisation.</summary>
public sealed class CatalogQueries(
    IProductReader products,
    ISyncRunStore runs,
    SyncSchedulePolicy schedule,
    IOptions<CatalogSyncOptions> options,
    TimeProvider clock)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    /// <summary>Liste paginée par curseur des produits actifs (CAT-R6) : exacte même si le catalogue change entre deux pages.</summary>
    public async Task<Result<ProductPage>> ListProductsAsync(int? limit, string? cursor, CancellationToken cancellationToken)
    {
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
        {
            return CatalogErrors.InvalidLimit(1, MaxLimit);
        }

        var afterId = 0;
        if (cursor is not null)
        {
            if (!ProductCursor.TryDecode(cursor, out var decoded))
            {
                return CatalogErrors.InvalidCursor();
            }

            afterId = decoded.Value;
        }

        // Un élément de plus que demandé, uniquement pour savoir s'il reste une page (pas de COUNT).
        var rows = await products.ListActiveAsync(afterId, take + 1, cancellationToken);
        var hasMore = rows.Count > take;
        var items = hasMore ? rows.Take(take).ToList() : rows;
        return new ProductPage(items, hasMore ? ProductCursor.Encode(items[^1].Id) : null, hasMore);
    }

    /// <summary>Historique des runs, du plus récent au plus ancien (CAT-R3.5).</summary>
    public async Task<Result<SyncRunPage>> ListRunsAsync(int? limit, long? beforeId, CancellationToken cancellationToken)
    {
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
        {
            return CatalogErrors.InvalidLimit(1, MaxLimit);
        }

        var rows = await runs.ListAsync(beforeId, take + 1, cancellationToken);
        var hasMore = rows.Count > take;
        var items = hasMore ? rows.Take(take).ToList() : rows;
        return new SyncRunPage(items, hasMore ? items[^1].Id : null, hasMore);
    }

    /// <summary>État de la synchronisation et action conseillée (CAT-R12).</summary>
    public async Task<SyncStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var lastSuccess = await runs.GetLastSuccessAsync(cancellationToken);
        var lastRun = await runs.GetLastFinishedAsync(cancellationToken);
        var state = await runs.GetScheduleStateAsync(cancellationToken);
        var decision = schedule.Evaluate(now, state);
        var advice = SyncStatusEvaluator.Evaluate(now, lastSuccess?.FinishedAt, lastRun, options.Value.StaleAfter);

        return new SyncStatus(
            lastSuccess,
            lastSuccess?.FinishedAt is { } finished ? now - finished : null,
            lastRun,
            state.ConsecutiveScheduledFailures,
            decision.NextAttemptAt,
            advice);
    }
}
