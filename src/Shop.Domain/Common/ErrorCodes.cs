namespace Shop.Domain.Common;

/// <summary>
/// Codes d'erreur stables du contrat HTTP (PLT-3). La correspondance code → statut HTTP est centralisée dans l'Api.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string UserNotIdentified = "user_not_identified";
    public const string AdminKeyInvalid = "admin_key_invalid";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string CartNotFound = "cart_not_found";
    public const string CartLineNotFound = "cart_line_not_found";
    public const string CartLineVersionConflict = "cart_line_version_conflict";
    public const string SyncAlreadyRunning = "sync_already_running";
    public const string ProductUnavailable = "product_unavailable";
    public const string QuantityLimitExceeded = "quantity_limit_exceeded";
    public const string IdempotencyKeyReused = "idempotency_key_reused";
    public const string SyncGuardTriggered = "sync_guard_triggered";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ServiceUnavailable = "service_unavailable";
    public const string InternalError = "internal_error";
}
