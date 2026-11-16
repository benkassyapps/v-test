using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Shop.Testing.Fakes;

/// <summary>Produit tel que le faux fournisseur l'expose.</summary>
public sealed record FakeProviderProduct(
    int Id,
    string Title,
    string Category,
    decimal Price,
    int Stock = 10,
    string AvailabilityStatus = "In Stock",
    DateTimeOffset? UpdatedAt = null);

/// <summary>
/// Faux DummyJSON (GET /products?limit=&amp;skip=&amp;select=) programmable, partagé par les deux instances de test :
/// catalogue modifiable, pagination réelle, plafond de taille de page, panne à partir d'une page,
/// total annoncé mensonger, indisponibilité totale et « porte » pour maintenir une synchronisation en cours.
/// </summary>
public sealed class FakeDummyJsonHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lock _sync = new();
    private readonly SortedDictionary<int, FakeProviderProduct> _products = [];
    private TaskCompletionSource? _gate;
    private TaskCompletionSource _requestReceivedWhileHeld = NewSignal();
    private int _requestCount;

    /// <summary>Plafond de produits par page (« le fournisseur renvoie au plus N produits par page »).</summary>
    public int? MaxPageSize { get; set; }

    /// <summary>Numéro de page (à partir de 1) à partir duquel le fournisseur répond 503.</summary>
    public int? FailFromPage { get; set; }

    /// <summary>Le fournisseur répond 503 à toutes les requêtes.</summary>
    public bool Unavailable { get; set; }

    /// <summary>Total annoncé différent du nombre réel de produits (instantané incomplet).</summary>
    public int? AnnouncedTotalOverride { get; set; }

    public int RequestCount => Volatile.Read(ref _requestCount);

    public void SetProducts(IEnumerable<FakeProviderProduct> products)
    {
        lock (_sync)
        {
            _products.Clear();
            foreach (var product in products)
            {
                _products[product.Id] = product;
            }
        }
    }

    public void Upsert(FakeProviderProduct product)
    {
        lock (_sync)
        {
            _products[product.Id] = product;
        }
    }

    public void Update(int id, Func<FakeProviderProduct, FakeProviderProduct> change)
    {
        lock (_sync)
        {
            _products[id] = change(_products[id]);
        }
    }

    public void Remove(int id)
    {
        lock (_sync)
        {
            _products.Remove(id);
        }
    }

    public IReadOnlyList<FakeProviderProduct> Products
    {
        get
        {
            lock (_sync)
            {
                return [.. _products.Values];
            }
        }
    }

    /// <summary>Bloque les réponses jusqu'à <see cref="ReleaseRequests"/> : une synchronisation reste « en cours ».</summary>
    public void HoldRequests()
    {
        lock (_sync)
        {
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _requestReceivedWhileHeld = NewSignal();
        }
    }

    public void ReleaseRequests()
    {
        lock (_sync)
        {
            _gate?.TrySetResult();
            _gate = null;
        }
    }

    /// <summary>Attend qu'une requête soit arrivée et bloquée par la porte (la synchronisation a bien démarré).</summary>
    public Task WaitForHeldRequestAsync(TimeSpan timeout) => _requestReceivedWhileHeld.Task.WaitAsync(timeout);

    /// <summary>Remet le faux fournisseur dans son état initial (avant chaque scénario).</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _products.Clear();
            _gate?.TrySetResult();
            _gate = null;
            _requestReceivedWhileHeld = NewSignal();
        }

        MaxPageSize = null;
        FailFromPage = null;
        Unavailable = false;
        AnnouncedTotalOverride = null;
        Interlocked.Exchange(ref _requestCount, 0);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);

        Task? gate;
        lock (_sync)
        {
            gate = _gate?.Task;
            if (gate is not null)
            {
                _requestReceivedWhileHeld.TrySetResult();
            }
        }

        if (gate is not null)
        {
            await gate.WaitAsync(cancellationToken);
        }

        if (request.Method != HttpMethod.Get || request.RequestUri?.AbsolutePath.TrimEnd('/') != "/products")
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
        var limit = query.TryGetValue("limit", out var l) && int.TryParse(l, out var parsedLimit) ? parsedLimit : 30;
        var skip = query.TryGetValue("skip", out var s) && int.TryParse(s, out var parsedSkip) ? parsedSkip : 0;

        List<FakeProviderProduct> all;
        lock (_sync)
        {
            all = [.. _products.Values];
        }

        var effectiveLimit = limit <= 0 ? all.Count : limit;
        if (MaxPageSize is { } max)
        {
            effectiveLimit = Math.Min(effectiveLimit, max);
        }

        var pageNumber = effectiveLimit == 0 ? 1 : (skip / effectiveLimit) + 1;
        if (Unavailable || (FailFromPage is { } failFrom && pageNumber >= failFrom))
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var page = all.Skip(skip).Take(effectiveLimit).Select(product => new
        {
            id = product.Id,
            title = product.Title,
            category = product.Category,
            price = product.Price,
            stock = product.Stock,
            availabilityStatus = product.AvailabilityStatus,
            meta = new { updatedAt = product.UpdatedAt ?? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero) },
        });

        var body = JsonSerializer.Serialize(
            new { products = page, total = AnnouncedTotalOverride ?? all.Count, skip, limit = effectiveLimit },
            Json);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
