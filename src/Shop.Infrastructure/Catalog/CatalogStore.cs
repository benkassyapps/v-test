using Microsoft.EntityFrameworkCore;
using Shop.Application.Catalog;
using Shop.Domain.Catalog;
using Shop.Domain.Common;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Catalog;

/// <summary>
/// Application d'une synchronisation en UNE transaction (CAT-R8) : tout est enregistré, ou rien.
/// Contexte EF neuf à chaque tentative : un échec ne laisse aucun état suivi qui pourrait fuiter ailleurs,
/// et le rejeu (opération idempotente) repart de l'état réellement validé en base.
/// </summary>
internal sealed class CatalogStore(IDbContextFactory<ShopDbContext> contexts, TransientRetry retry, TimeProvider clock) : ICatalogStore
{
    public Task<Result<AppliedSync>> ApplyAsync(
        long runId,
        Func<IReadOnlyCollection<Product>, Result<CatalogChanges>> change,
        CancellationToken cancellationToken) =>
        retry.ExecuteAsync(async token =>
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(token);

            // Barrière (fencing) : le verrou distribué peut être perdu sans que l'instance le sache encore
            // (détection au keepalive suivant). On verrouille la ligne de NOTRE run jusqu'au commit : si une autre
            // instance l'a déjà déclaré interrompu, on abandonne ; sinon, elle ne pourra le faire qu'après notre commit,
            // et verra alors un run réussi. Deux instances ne peuvent donc pas appliquer en même temps.
            var status = await db.Database
                .SqlQuery<byte>($"SELECT Status AS [Value] FROM dbo.SyncRuns WITH (UPDLOCK, ROWLOCK) WHERE Id = {runId}")
                .SingleAsync(token);
            if (status != (byte)SyncRunStatus.Running)
            {
                return Result<AppliedSync>.Fail(CatalogErrors.SyncInterrupted());
            }

            var products = await db.Set<Product>().ToListAsync(token);
            var result = change(products);
            if (!result.IsSuccess)
            {
                // Garde-fou ou autre refus : rollback, rien n'est enregistré.
                return Result<AppliedSync>.Fail(result.Error);
            }

            db.Set<Product>().AddRange(result.Value.NewProducts);
            await db.SaveChangesAsync(token);

            // Le run passe à « réussi » dans la MÊME transaction que le catalogue : impossible d'avoir un catalogue
            // modifié avec un run resté « en cours » (puis déclaré interrompu à tort).
            var counts = result.Value.Counts;
            var finishedAt = clock.GetUtcNow();
            await db.Set<SyncRun>()
                .Where(run => run.Id == runId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(run => run.Status, SyncRunStatus.Succeeded)
                    .SetProperty(run => run.FinishedAt, finishedAt)
                    .SetProperty(run => run.Fetched, counts.Fetched)
                    .SetProperty(run => run.Inserted, counts.Inserted)
                    .SetProperty(run => run.Updated, counts.Updated)
                    .SetProperty(run => run.Discontinued, counts.Discontinued)
                    .SetProperty(run => run.Reactivated, counts.Reactivated), token);

            await transaction.CommitAsync(token);
            return Result<AppliedSync>.Ok(new AppliedSync(counts, finishedAt));
        }, cancellationToken);
}
