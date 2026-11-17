using Shop.Application.Catalog;
using Shop.Domain.Catalog;

namespace Shop.Api.Endpoints;

/// <summary>Résumé d'une exécution de synchronisation.</summary>
public sealed record SyncRunResponse(
    long Id,
    SyncTrigger Trigger,
    SyncRunStatus Status,
    SyncFailureReason? FailureReason,
    bool Forced,
    string InstanceId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int Fetched,
    int Inserted,
    int Updated,
    int Discontinued,
    int Reactivated,
    int? ActiveBefore,
    int? WouldDiscontinue,
    string? Error)
{
    public static SyncRunResponse From(SyncRun run) =>
        new(run.Id, run.Trigger, run.Status, run.FailureReason, run.Forced, run.InstanceId, run.StartedAt, run.FinishedAt,
            run.Fetched, run.Inserted, run.Updated, run.Discontinued, run.Reactivated, run.ActiveBefore, run.WouldDiscontinue, run.Error);
}

/// <summary>Historique des synchronisations, du plus récent au plus ancien (passer <c>nextBeforeId</c> en <c>beforeId</c>).</summary>
public sealed record SyncRunPageResponse(IReadOnlyList<SyncRunResponse> Items, long? NextBeforeId, bool HasMore);

/// <summary>Dernière synchronisation réussie et son ancienneté.</summary>
public sealed record LastSuccessResponse(long RunId, DateTimeOffset? FinishedAt, TimeSpan? Age);

/// <summary>État de la synchronisation et action conseillée à l'exploitant (CAT-R12).</summary>
public sealed record SyncStatusResponse(
    LastSuccessResponse? LastSuccess,
    SyncRunResponse? LastRun,
    int ConsecutiveFailures,
    DateTimeOffset NextScheduledAttemptAt,
    bool ActionRequired,
    string? Reason,
    string? RecommendedAction)
{
    public static SyncStatusResponse From(SyncStatus status) =>
        new(
            status.LastSuccess is { } success ? new LastSuccessResponse(success.Id, success.FinishedAt, status.LastSuccessAge) : null,
            status.LastRun is { } run ? SyncRunResponse.From(run) : null,
            status.ConsecutiveFailures,
            status.NextScheduledAttemptAt,
            status.Advice.ActionRequired,
            status.Advice.Reason,
            status.Advice.RecommendedAction);
}
