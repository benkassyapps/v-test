using Cronos;

namespace Shop.Application.Catalog;

/// <summary>Historique utile au planificateur, lu dans SyncRuns.</summary>
/// <param name="LastSuccessAt">Fin de la dernière synchronisation réussie (manuelle ou planifiée).</param>
/// <param name="ConsecutiveScheduledFailures">Runs planifiés en échec depuis la dernière réussite.</param>
/// <param name="LastFailureAt">Fin du dernier de ces échecs.</param>
public sealed record SyncScheduleState(
    DateTimeOffset? LastSuccessAt,
    int ConsecutiveScheduledFailures,
    DateTimeOffset? LastFailureAt);

/// <param name="IsDue">Une synchronisation planifiée doit être lancée maintenant.</param>
/// <param name="NextAttemptAt">Prochaine tentative planifiée (maintenant si elle est due).</param>
public sealed record SyncScheduleDecision(bool IsDue, DateTimeOffset NextAttemptAt);

/// <summary>
/// Décision PURE du planificateur (CAT-R2) : la synchronisation est due si le dernier créneau échu n'a pas
/// encore été couvert par une réussite, en respectant l'espacement progressif après des échecs consécutifs.
/// </summary>
public sealed class SyncSchedulePolicy
{
    /// <summary>Fenêtres de recherche du dernier créneau échu, de la plus courte à la plus longue (cron fréquent ou rare).</summary>
    private static readonly TimeSpan[] SlotLookBackWindows =
        [TimeSpan.FromHours(1), TimeSpan.FromDays(2), TimeSpan.FromDays(400), TimeSpan.FromDays(366 * 5)];

    private readonly CronExpression _cron;
    private readonly TimeSpan _baseRetryDelay;
    private readonly TimeSpan _maxRetryDelay;

    public SyncSchedulePolicy(CatalogSyncOptions options)
    {
        _cron = CronExpression.Parse(options.Cron);
        _baseRetryDelay = options.BaseRetryDelay;
        _maxRetryDelay = options.MaxRetryDelay;
    }

    public SyncScheduleDecision Evaluate(DateTimeOffset now, SyncScheduleState state)
    {
        var lastSlot = LastSlotAtOrBefore(now);
        var slotCovered = state.LastSuccessAt is { } success && (lastSlot is null || success >= lastSlot);

        if (slotCovered)
        {
            return new SyncScheduleDecision(false, NextSlotAfter(now));
        }

        // Créneau non couvert (ou jamais de réussite : première mise en service).
        if (state.ConsecutiveScheduledFailures == 0 || state.LastFailureAt is null)
        {
            return new SyncScheduleDecision(true, now);
        }

        var retryAt = state.LastFailureAt.Value + RetryDelay(state.ConsecutiveScheduledFailures);
        return retryAt <= now
            ? new SyncScheduleDecision(true, now)
            : new SyncScheduleDecision(false, retryAt);
    }

    /// <summary>Espacement progressif : base × 2^(n−1), plafonné (15 min, 30, 60, 120, 120…).</summary>
    public TimeSpan RetryDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        // Doublement itératif plafonné : on compare au plafond AVANT de doubler, sans jamais dépasser TimeSpan.MaxValue
        // (une multiplication par 2^n déborde dès ~30 échecs consécutifs).
        var delay = _baseRetryDelay;
        for (var attempt = 1; attempt < consecutiveFailures && delay < _maxRetryDelay; attempt++)
        {
            delay = delay < _maxRetryDelay / 2 ? delay * 2 : _maxRetryDelay;
        }

        return delay < _maxRetryDelay ? delay : _maxRetryDelay;
    }

    /// <summary>
    /// Dernière occurrence du cron au plus tard à <paramref name="now"/>. Fenêtres croissantes : la plus petite
    /// fenêtre contenant une occurrence suffit, ce qui reste peu coûteux pour un cron fréquent comme pour un cron rare.
    /// </summary>
    private DateTimeOffset? LastSlotAtOrBefore(DateTimeOffset now)
    {
        foreach (var window in SlotLookBackWindows)
        {
            var last = _cron.GetOccurrences(now - window, now, TimeZoneInfo.Utc, fromInclusive: true, toInclusive: true)
                .Select(occurrence => (DateTimeOffset?)occurrence)
                .LastOrDefault();
            if (last is not null)
            {
                return last;
            }
        }

        return null;
    }

    private DateTimeOffset NextSlotAfter(DateTimeOffset now) =>
        _cron.GetNextOccurrence(now, TimeZoneInfo.Utc) ?? now + SlotLookBackWindows[^1];
}
