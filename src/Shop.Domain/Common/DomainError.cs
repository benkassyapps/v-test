namespace Shop.Domain.Common;

/// <summary>
/// Erreur métier attendue (règle non respectée, ressource introuvable, conflit).
/// Le <see cref="Code"/> est stable et lisible par une machine ; le <see cref="Message"/> est destiné à l'utilisateur, en français.
/// </summary>
public sealed record DomainError(string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null)
{
    public static DomainError Validation(string message, IReadOnlyDictionary<string, object?>? details = null) =>
        new(ErrorCodes.ValidationFailed, message, details);
}
