using Microsoft.Extensions.Options;

namespace Shop.Infrastructure.Persistence;

/// <summary>Configuration du rejeu des opérations idempotentes (section « Database »).</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Nombre total de tentatives pour une opération idempotente.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Délai avant la 2e tentative (doublé ensuite).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Délai maximal entre deux tentatives.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Rejeu explicite des opérations IDEMPOTENTES en cas d'erreur transitoire (specs/platform/design.md §6) :
/// l'unité de travail entière est rejouée sur un contexte neuf. Jamais utilisé pour les commandes de panier,
/// qu'un rejeu après un commit dont l'accusé est perdu appliquerait deux fois.
/// </summary>
public sealed class TransientRetry(IOptions<DatabaseOptions> options, TimeProvider clock)
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var delay = settings.RetryDelay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception exception) when (attempt < settings.MaxAttempts && DatabaseFailures.IsTransient(exception))
            {
                await Task.Delay(delay, clock, cancellationToken);
                delay = delay * 2 < settings.MaxRetryDelay ? delay * 2 : settings.MaxRetryDelay;
            }
        }
    }
}
