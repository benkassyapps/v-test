using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shop.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Active READ_COMMITTED_SNAPSHOT (PLT-5) : les lectures ne sont plus bloquées par les écritures en cours.
    /// ALTER DATABASE exige d'être la seule connexion et ne peut pas s'exécuter dans une transaction :
    /// cette migration est appliquée par le migrateur dédié, seul, avant le démarrage des instances.
    /// La correction des règles métier ne dépend pas de ce réglage (verrous explicites, voir constitution §3).
    /// </summary>
    public partial class EnableReadCommittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;",
                suppressTransaction: true);
        }
    }
}
