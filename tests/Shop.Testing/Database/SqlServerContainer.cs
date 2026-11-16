using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Shop.Infrastructure.Persistence;
using Testcontainers.MsSql;

namespace Shop.Testing.Database;

/// <summary>
/// Un seul conteneur SQL Server par processus de test, base migrée une fois (specs/platform/design.md §10).
/// Les scénarios partagent cette base et la remettent à zéro avant chacun d'eux.
/// </summary>
public static class SqlServerContainer
{
    private const string DatabaseName = "ShopTests";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static MsSqlContainer? _container;
    private static string? _connectionString;

    public static async Task<string> GetConnectionStringAsync()
    {
        if (_connectionString is not null)
        {
            return _connectionString;
        }

        await Gate.WaitAsync();
        try
        {
            if (_connectionString is null)
            {
                var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
                await container.StartAsync();

                var connectionString = new SqlConnectionStringBuilder(container.GetConnectionString())
                {
                    InitialCatalog = DatabaseName,
                    TrustServerCertificate = true,
                }.ConnectionString;

                await using (var db = CreateDbContext(connectionString))
                {
                    await db.Database.MigrateAsync();
                }

                _container = container;
                _connectionString = connectionString;
            }

            return _connectionString;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static ShopDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ShopDbContext>().UseSqlServer(connectionString).Options);

    /// <summary>
    /// Vide les tables dans l'ordre des clés étrangères et réinitialise les identités.
    /// Les tables absentes (pas encore créées par les migrations) sont ignorées.
    /// </summary>
    public static async Task ResetAsync(string connectionString)
    {
        string[] tablesInDeletionOrder = ["CartLines", "Carts", "Products", "SyncRuns"];
        string[] identityTables = ["Products", "SyncRuns"];

        var statements = tablesInDeletionOrder
            .Select(table => $"IF OBJECT_ID(N'dbo.{table}') IS NOT NULL DELETE FROM dbo.[{table}];")
            // RESEED 0 seulement si la table a déjà reçu une ligne : sur une table jamais alimentée,
            // RESEED 0 ferait commencer les identités à 0 au lieu de 1 (identifiants dépendants de l'ordre des tests).
            .Concat(identityTables.Select(table =>
                $"IF EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id = OBJECT_ID(N'dbo.{table}') AND last_value IS NOT NULL) " +
                $"DBCC CHECKIDENT (N'dbo.{table}', RESEED, 0) WITH NO_INFOMSGS;"));

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = string.Join(Environment.NewLine, statements);
        await command.ExecuteNonQueryAsync();
    }
}
