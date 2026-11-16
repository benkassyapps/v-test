using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Shop.Application.Catalog;
using Shop.Domain.Catalog;
using Shop.Domain.Common;

namespace Shop.Infrastructure.Catalog;

/// <summary>
/// Fournisseur DummyJSON (https://dummyjson.com/docs/products) : parcourt toutes les pages jusqu'au total annoncé,
/// puis valide l'instantané complet (CAT-R1). Les pannes sont renvoyées comme des échecs, jamais propagées.
/// La résilience (nouvelles tentatives, délais, coupe-circuit) est portée par le pipeline HTTP standard.
/// </summary>
internal sealed class DummyJsonCatalogProvider(HttpClient http, IOptions<CatalogSyncOptions> options) : ICatalogProvider
{
    private const string SelectedFields = "title,category,price,stock,availabilityStatus,meta";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<CatalogSnapshot>> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var pageSize = options.Value.PageSize;
        var products = new List<ProviderProduct>();
        int? announcedTotal = null;
        var skip = 0;

        try
        {
            while (true)
            {
                var page = await http.GetFromJsonAsync<DummyJsonPage>(
                    $"products?limit={pageSize}&skip={skip}&select={SelectedFields}", Json, cancellationToken);

                if (page?.Products is null)
                {
                    return Invalid("réponse sans liste de produits");
                }

                if (announcedTotal is null)
                {
                    announcedTotal = page.Total;
                }
                else if (announcedTotal != page.Total)
                {
                    return Invalid($"total incohérent entre deux pages ({announcedTotal} puis {page.Total})");
                }

                products.AddRange(page.Products.Select(ToProviderProduct));
                skip += page.Products.Count;

                // Fin : tout est récupéré, ou page vide prématurée (l'instantané sera alors jugé incomplet).
                if (page.Products.Count == 0 || skip >= announcedTotal)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
                                              || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return CatalogErrors.ProviderUnavailable(Describe(exception));
        }
        catch (JsonException)
        {
            return Invalid("réponse JSON illisible");
        }

        // La validation de complétude renvoie elle-même une erreur « invalid_snapshot ».
        return CatalogSnapshot.Create(products, announcedTotal ?? 0);
    }

    private static ProviderProduct ToProviderProduct(DummyJsonProduct product) =>
        new(product.Id, product.Title!, product.Category!, product.Price, product.Stock, product.AvailabilityStatus!, product.Meta?.UpdatedAt);

    private static string Describe(Exception exception) =>
        exception switch
        {
            HttpRequestException { StatusCode: { } status } => $"réponse HTTP {(int)status}",
            BrokenCircuitException => "coupe-circuit ouvert après des échecs répétés",
            TimeoutRejectedException or TaskCanceledException => "délai de réponse dépassé",
            _ => "connexion impossible",
        };

    private static DomainError Invalid(string reason) =>
        new(ErrorCodes.ProviderUnavailable,
            $"Instantané du fournisseur incomplet ou invalide : {reason}.",
            new Dictionary<string, object?> { ["reason"] = CatalogErrors.InvalidSnapshotReason });

#pragma warning disable CA1812 // Instanciés par System.Text.Json.
    private sealed record DummyJsonPage(List<DummyJsonProduct>? Products, int Total, int Skip, int Limit);

    private sealed record DummyJsonProduct(
        int Id,
        string? Title,
        string? Category,
        decimal Price,
        int Stock,
        string? AvailabilityStatus,
        DummyJsonMeta? Meta);

    private sealed record DummyJsonMeta(DateTimeOffset? UpdatedAt);
#pragma warning restore CA1812
}
