using Shop.Api.Auth;
using Shop.Infrastructure.Persistence;
using Shop.Testing.Database;

namespace Shop.Testing.Hosting;

/// <summary>
/// Deux instances applicatives (« instance-1 », « instance-2 ») partageant une même base SQL Server
/// et les mêmes doublures. Indépendant du framework de test : xUnit et Reqnroll l'enveloppent chacun.
/// </summary>
public sealed class ShopTestCluster : IAsyncDisposable
{
    private ShopApplicationFactory? _instance1;
    private ShopApplicationFactory? _instance2;
    private string? _connectionString;

    public TestDoubles Doubles { get; } = new();

    public string ConnectionString => _connectionString ?? throw NotInitialized();

    public async Task InitializeAsync()
    {
        _connectionString = await SqlServerContainer.GetConnectionStringAsync();
        _instance1 = new ShopApplicationFactory("instance-1", _connectionString, Doubles);
        _instance2 = new ShopApplicationFactory("instance-2", _connectionString, Doubles);

        // Démarrage effectif des deux hôtes.
        _ = _instance1.Server;
        _ = _instance2.Server;
    }

    public ShopApplicationFactory Instance(int number) =>
        number switch
        {
            1 => _instance1 ?? throw NotInitialized(),
            2 => _instance2 ?? throw NotInitialized(),
            _ => throw new ArgumentOutOfRangeException(nameof(number), number, "Seules les instances 1 et 2 existent."),
        };

    /// <summary>Client HTTP vers une instance, avec éventuellement un utilisateur (X-User-Id) et la clé d'administration.</summary>
    public HttpClient CreateClient(int instance = 1, string? userId = null, bool admin = false)
    {
        var client = Instance(instance).CreateClient();
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add(UserIdAuthenticationHandler.HeaderName, userId);
        }

        if (admin)
        {
            client.DefaultRequestHeaders.Add(AdminKey.HeaderName, ShopApplicationFactory.AdminApiKey);
        }

        return client;
    }

    public ShopDbContext CreateDbContext() => SqlServerContainer.CreateDbContext(ConnectionString);

    /// <summary>Remet la base et les doublures à zéro (avant chaque scénario).</summary>
    public async Task ResetAsync()
    {
        await SqlServerContainer.ResetAsync(ConnectionString);
        Doubles.Reset();
    }

    public async ValueTask DisposeAsync()
    {
        if (_instance1 is not null)
        {
            await _instance1.DisposeAsync();
        }

        if (_instance2 is not null)
        {
            await _instance2.DisposeAsync();
        }
    }

    private static InvalidOperationException NotInitialized() =>
        new("Le cluster de test n'est pas initialisé : appeler InitializeAsync.");
}
