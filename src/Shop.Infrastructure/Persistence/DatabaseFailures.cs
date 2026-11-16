using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Shop.Infrastructure.Persistence;

/// <summary>
/// Classe les exceptions de base de données : une panne transitoire (coupure, délai dépassé, interblocage,
/// base indisponible) se traduit par 503 ; toute autre erreur reste une erreur interne (500).
/// </summary>
public static class DatabaseFailures
{
    /// <summary>
    /// Numéros d'erreur SQL Server considérés comme transitoires : délai dépassé (-2), interblocage (1205),
    /// connexion rompue ou refusée (2, 53, 64, 233, 10053, 10054, 10060, 10928, 10929, 4060, 4221),
    /// basculement ou indisponibilité Azure SQL (40143, 40197, 40501, 40613, 49918, 49919, 49920).
    /// </summary>
    private static readonly HashSet<int> TransientErrorNumbers =
    [
        -2, 2, 53, 64, 233, 1205, 4060, 4221, 10053, 10054, 10060, 10928, 10929,
        40143, 40197, 40501, 40613, 49918, 49919, 49920,
    ];

    public static bool IsTransient(Exception exception) =>
        exception switch
        {
            TimeoutException => true,
            RetryLimitExceededException => true,
            SqlException sql => sql.Errors.Cast<SqlError>().Any(error => TransientErrorNumbers.Contains(error.Number)),
            DbUpdateException { InnerException: { } inner } => IsTransient(inner),
            InvalidOperationException { InnerException: { } inner } => IsTransient(inner),
            InvalidOperationException pool when IsPoolExhaustion(pool) => true,
            _ => false,
        };

    /// <summary>
    /// Pool de connexions épuisé : SqlClient lève une InvalidOperationException sans exception interne ni code.
    /// Seul son message permet de la reconnaître (message non localisé de Microsoft.Data.SqlClient).
    /// </summary>
    private static bool IsPoolExhaustion(InvalidOperationException exception) =>
        exception.Message.Contains("obtaining a connection from the pool", StringComparison.OrdinalIgnoreCase);
}
