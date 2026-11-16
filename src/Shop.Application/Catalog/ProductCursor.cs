using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shop.Application.Catalog;

/// <summary>
/// Curseur opaque de la pagination keyset des produits (CAT-R6) : base64url de {"v":1,"id":dernierId}.
/// Le champ « v » permet de faire évoluer le format ou le tri sans casser les clients.
/// </summary>
public static class ProductCursor
{
    private const int CurrentVersion = 1;

    public static string Encode(int lastId) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(CurrentVersion, lastId)));

    /// <summary>Décode strictement un curseur : tout curseur altéré, d'une version inconnue ou incohérent est refusé.</summary>
    public static bool TryDecode(string? cursor, [NotNullWhen(true)] out int? lastId)
    {
        lastId = null;
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 256)
        {
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CursorPayload>(Base64Url.DecodeFromChars(cursor));
            if (payload is not { V: CurrentVersion, Id: > 0 })
            {
                return false;
            }

            lastId = payload.Id;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    // Noms explicites : le format documenté est {"v":1,"id":n} (le sérialiseur par défaut écrirait "V" et "Id",
    // et la désérialisation, sensible à la casse, refuserait un curseur au format documenté).
#pragma warning disable CA1812 // Instancié par System.Text.Json.
    private sealed record CursorPayload(
        [property: JsonPropertyName("v")] int V,
        [property: JsonPropertyName("id")] int Id);
#pragma warning restore CA1812
}
