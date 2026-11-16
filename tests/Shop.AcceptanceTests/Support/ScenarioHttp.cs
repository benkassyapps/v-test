using System.Text.Json;

namespace Shop.AcceptanceTests.Support;

/// <summary>Dernière réponse HTTP reçue au cours du scénario (partagée entre les classes d'étapes).</summary>
public sealed class ScenarioHttp : IDisposable
{
    private HttpResponseMessage? _last;

    public HttpResponseMessage Last => _last ?? throw new InvalidOperationException("Aucune requête n'a encore été envoyée dans ce scénario.");

    public string LastBody { get; private set; } = string.Empty;

    public async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        var response = await client.SendAsync(request);
        await RecordAsync(response);
        return response;
    }

    public async Task RecordAsync(HttpResponseMessage response)
    {
        _last?.Dispose();
        _last = response;
        LastBody = await response.Content.ReadAsStringAsync();
    }

    public JsonElement LastJson()
    {
        using var document = JsonDocument.Parse(LastBody);
        return document.RootElement.Clone();
    }

    public void Dispose() => _last?.Dispose();
}
