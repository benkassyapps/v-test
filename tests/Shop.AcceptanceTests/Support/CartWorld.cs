using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shop.Application.Carts;
using Shop.Domain.Carts;
using Shop.Domain.Catalog;
using Shop.Testing.Hosting;

namespace Shop.AcceptanceTests.Support;

/// <summary>Requête de panier préparée, pour les envois simultanés.</summary>
public sealed record PendingCartRequest(int Instance, string User, HttpMethod Method, string Path, object? Body, int? RequestedQuantity, string? IdempotencyKey = null);

/// <summary>
/// État et actions partagés par les étapes du panier : envoi des commandes par utilisateur et par instance,
/// dernière lecture de chaque utilisateur, lignes mémorisées, photographies avant commande et envois simultanés.
/// </summary>
public sealed class CartWorld(ShopTestCluster cluster, ScenarioHttp http)
{
    private readonly Dictionary<string, JsonElement> _lastRead = [];
    private readonly Dictionary<string, Guid> _memorizedLines = [];
    private Dictionary<string, string> _statesBefore = [];

    public string? LastUser { get; private set; }

    public IReadOnlyList<(PendingCartRequest Request, HttpResponseMessage Response, JsonElement? Body)> ConcurrentResults { get; private set; } = [];

    // --- Envoi ---

    public async Task<HttpResponseMessage> SendAsync(string user, HttpMethod method, string path, object? body = null, int instance = 1, string? idempotencyKey = null)
    {
        LastUser = user;
        using var client = cluster.CreateClient(instance, user);
        using var request = BuildRequest(method, path, body, idempotencyKey);
        return await http.SendAsync(client, request);
    }

    /// <summary>Envoi d'une commande « Quand » : photographie l'état de tous les paniers avant l'envoi.</summary>
    public async Task<HttpResponseMessage> CommandAsync(string user, HttpMethod method, string path, object? body = null, int instance = 1, string? idempotencyKey = null)
    {
        await CaptureStatesAsync();
        return await SendAsync(user, method, path, body, instance, idempotencyKey);
    }

    /// <summary>Envoi d'une commande de préparation (« Étant donné ») qui doit réussir.</summary>
    public async Task GivenAsync(string user, HttpMethod method, string path, object? body = null, int instance = 1, string? idempotencyKey = null)
    {
        using var response = await SendRawAsync(user, method, path, body, instance, idempotencyKey);
        Assert.True(response.IsSuccessStatusCode, $"Préparation en échec ({(int)response.StatusCode}) : {await response.Content.ReadAsStringAsync()}");
    }

    public async Task RunConcurrentlyAsync(IReadOnlyList<PendingCartRequest> requests)
    {
        await CaptureStatesAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = requests.Select(async request =>
        {
            await start.Task;
            var response = await SendRawAsync(request.User, request.Method, request.Path, request.Body, request.Instance, request.IdempotencyKey);
            var text = await response.Content.ReadAsStringAsync();
            JsonElement? body = text.Length == 0 ? null : JsonDocument.Parse(text).RootElement.Clone();
            return (request, response, body);
        }).ToArray();
        start.SetResult();
        ConcurrentResults = await Task.WhenAll(tasks);
    }

    // --- Lectures ---

    /// <summary>Lit le panier de l'utilisateur (sans toucher à la dernière réponse du scénario) et la mémorise.</summary>
    public async Task<JsonElement> ReadCartAsync(string user)
    {
        using var response = await SendRawAsync(user, HttpMethod.Get, "/api/cart", body: null, instance: 1);
        Assert.True(response.IsSuccessStatusCode, $"Panier de {user} illisible ({(int)response.StatusCode}).");
        var cart = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        _lastRead[user] = cart;
        return cart;
    }

    /// <summary>
    /// Désigne l'utilisateur concerné par les vérifications suivantes (après des envois simultanés, il n'y a pas de
    /// « dernière réponse ») et mémorise son panier s'il existe.
    /// </summary>
    public async Task FocusOnAsync(string user)
    {
        LastUser = user;
        if (await CartCountAsync(user) > 0)
        {
            await ReadCartAsync(user);
        }
    }

    public void RememberRead(string user, JsonElement cart) => _lastRead[user] = cart;

    public JsonElement LastRead(string user) =>
        _lastRead.TryGetValue(user, out var cart) ? cart : throw new InvalidOperationException($"{user} n'a pas encore consulté son panier.");

    public static JsonElement? FindLine(JsonElement cart, string productName) =>
        cart.GetProperty("lines").EnumerateArray().Cast<JsonElement?>()
            .FirstOrDefault(line => line!.Value.GetProperty("productName").GetString() == productName);

    public async Task<JsonElement> LineAsync(string user, string productName) =>
        FindLine(await ReadCartAsync(user), productName) ?? throw new InvalidOperationException($"Pas de ligne « {productName} » dans le panier de {user}.");

    /// <summary>
    /// Identifiant de la ligne d'un produit ; s'il n'existe plus (ligne supprimée), le dernier identifiant connu :
    /// permet de renvoyer à l'identique une demande déjà traitée (clé d'idempotence).
    /// </summary>
    public async Task<Guid> LineIdAsync(string user, string productName)
    {
        if (FindLine(await ReadCartAsync(user), productName) is { } line)
        {
            var id = line.GetProperty("lineId").GetGuid();
            _knownLineIds[$"{user}|{productName}"] = id;
            return id;
        }

        return _knownLineIds[$"{user}|{productName}"];
    }

    private readonly Dictionary<string, Guid> _knownLineIds = [];

    public void Memorize(string user, string productName, Guid lineId) => _memorizedLines[$"{user}|{productName}"] = lineId;

    public Guid Memorized(string user) =>
        _memorizedLines.Single(entry => entry.Key.StartsWith(user + "|", StringComparison.Ordinal)).Value;

    /// <summary>Identifiant local d'un produit désigné par son nom actuel ou par un ancien nom (produit renommé).</summary>
    public async Task<int> ProductIdAsync(string name)
    {
        await using var db = cluster.CreateDbContext();
        var ids = await db.Set<Product>().Where(product => product.Name == name).Select(product => product.Id).ToListAsync();
        return ids is [var id] ? id : _formerNames[name];
    }

    /// <summary>Retient l'ancien nom d'un produit renommé : un scénario peut continuer à le désigner ainsi.</summary>
    public void RegisterFormerName(string formerName, int productId) => _formerNames[formerName] = productId;

    private readonly Dictionary<string, int> _formerNames = [];

    public async Task<int> CartCountAsync(string user)
    {
        await using var db = cluster.CreateDbContext();
        return await db.Set<Cart>().CountAsync(cart => cart.UserId == user);
    }

    // --- Photographies (« le panier est inchangé ») ---

    public string StateBefore(string user) => _statesBefore.GetValueOrDefault(user, "aucun panier");

    public async Task<string> StateAsync(string user) => (await StatesAsync()).GetValueOrDefault(user, "aucun panier");

    private async Task CaptureStatesAsync() => _statesBefore = await StatesAsync();

    private async Task<Dictionary<string, string>> StatesAsync()
    {
        await using var db = cluster.CreateDbContext();
        var carts = await db.Set<Cart>().AsNoTracking().Include(cart => cart.Lines).ToListAsync();
        return carts.ToDictionary(
            cart => cart.UserId,
            cart => string.Create(CultureInfo.InvariantCulture, $"v{cart.Version}|") + string.Join(";", cart.Lines
                .OrderBy(line => line.ProductName, StringComparer.Ordinal)
                .Select(line => string.Create(CultureInfo.InvariantCulture,
                    $"{line.Id}:{line.ProductName}:{line.UnitPrice}:{line.Quantity}:v{line.Version}"))));
    }

    private async Task<HttpResponseMessage> SendRawAsync(string user, HttpMethod method, string path, object? body, int instance, string? idempotencyKey = null)
    {
        using var client = cluster.CreateClient(instance, user);
        using var request = BuildRequest(method, path, body, idempotencyKey);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyKeyFormat.HeaderName, idempotencyKey);
        }

        return request;
    }
}
