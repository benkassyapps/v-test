using Microsoft.EntityFrameworkCore;
using Shop.Application.Catalog;
using Shop.Domain.Catalog;
using Shop.Infrastructure.Persistence;

namespace Shop.Infrastructure.Catalog;

/// <summary>
/// Lecture keyset des produits actifs (CAT-R6) : <c>WHERE Status = Active AND Id &gt; @afterId ORDER BY Id</c>,
/// servie par l'index (Status, Id). Lecture locale uniquement (CAT-R7), non bloquée par la synchro grâce à RCSI.
/// </summary>
internal sealed class ProductReader(IDbContextFactory<ShopDbContext> contexts, TransientRetry retry) : IProductReader
{
    public Task<IReadOnlyList<ProductView>> ListActiveAsync(int afterId, int take, CancellationToken cancellationToken) =>
        retry.ExecuteAsync<IReadOnlyList<ProductView>>(async token =>
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            return await db.Set<Product>().AsNoTracking()
                .Where(product => product.Status == ProductStatus.Active && product.Id > afterId)
                .OrderBy(product => product.Id)
                .Take(take)
                .Select(product => new ProductView(
                    product.Id,
                    product.ExternalId,
                    product.Name,
                    product.Category,
                    product.Price,
                    product.Stock,
                    product.AvailabilityStatus,
                    product.LastUpdatedAt))
                .ToListAsync(token);
        }, cancellationToken);
}
