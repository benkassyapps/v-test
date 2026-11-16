using System.Globalization;
using System.Text;
using Shop.Application.Catalog;
using Shop.Domain.Catalog;

namespace Shop.UnitTests.Application.Catalog;

public sealed class SyncStatusAndCursorTests
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(26);
    private static readonly DateTimeOffset Now = At("2026-11-16T14:00:00Z");

    // --- actionRequired (CAT-R12) ---

    [Fact(DisplayName = "Un blocage par le garde-fou demande une action : vérifier le fournisseur puis forcer")]
    [Trait("Spec", "CAT-R12")]
    public void Guard_block_requires_action()
    {
        var run = SyncRun.Start(SyncTrigger.Scheduled, "instance-1", false, Now.AddHours(-1));
        run.BlockByGuard(activeBefore: 2, wouldDiscontinue: 2, fetched: 0, Now.AddHours(-1));

        var advice = SyncStatusEvaluator.Evaluate(Now, At("2026-11-16T02:05:00Z"), run, StaleAfter);

        Assert.True(advice.ActionRequired);
        Assert.Contains("garde-fou de retrait massif déclenché", advice.Reason, StringComparison.Ordinal);
        Assert.Contains("Vérifier le fournisseur avant de forcer", advice.RecommendedAction, StringComparison.Ordinal);
        Assert.Contains("force=true", advice.RecommendedAction, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Un catalogue non synchronisé depuis plus de 26 h demande une action")]
    [Trait("Spec", "CAT-R12")]
    public void Stale_catalog_requires_action()
    {
        var failed = SyncRun.Start(SyncTrigger.Scheduled, "instance-1", false, Now);
        failed.Fail(SyncFailureReason.ProviderError, "panne", Now);

        var advice = SyncStatusEvaluator.Evaluate(At("2026-11-16T05:00:00Z"), At("2026-11-15T02:05:00Z"), failed, StaleAfter);

        Assert.True(advice.ActionRequired);
        Assert.Equal("aucune synchronisation réussie depuis plus de 26 h", advice.Reason);
    }

    [Fact(DisplayName = "Une synchronisation à jour ne demande aucune action")]
    [Trait("Spec", "CAT-R12")]
    public void Fresh_catalog_requires_no_action()
    {
        var advice = SyncStatusEvaluator.Evaluate(Now, At("2026-11-16T02:05:00Z"), lastRun: null, StaleAfter);

        Assert.False(advice.ActionRequired);
        Assert.Null(advice.Reason);
    }

    [Fact(DisplayName = "Jamais de réussite : action requise seulement après un échec")]
    [Trait("Spec", "CAT-R12")]
    public void Never_succeeded()
    {
        Assert.False(SyncStatusEvaluator.Evaluate(Now, null, null, StaleAfter).ActionRequired);

        var failed = SyncRun.Start(SyncTrigger.Scheduled, "instance-1", false, Now);
        failed.Fail(SyncFailureReason.ProviderError, "panne", Now);
        Assert.True(SyncStatusEvaluator.Evaluate(Now, null, failed, StaleAfter).ActionRequired);
    }

    // --- Curseur (CAT-R6) ---

    [Theory(DisplayName = "Un curseur encodé se décode en son dernier identifiant")]
    [InlineData(1)]
    [InlineData(194)]
    [InlineData(int.MaxValue)]
    [Trait("Spec", "CAT-R6")]
    public void Cursor_round_trip(int lastId)
    {
        Assert.True(ProductCursor.TryDecode(ProductCursor.Encode(lastId), out var decoded));
        Assert.Equal(lastId, decoded);
    }

    [Theory(DisplayName = "Un curseur altéré, d'une version inconnue ou incohérent est refusé")]
    [InlineData("")]
    [InlineData("pas-un-curseur")]
    [InlineData("eyJ2IjoxLCJpZCI6Mn")]
    [InlineData("{\"v\":2,\"id\":5}")]
    [InlineData("{\"v\":1,\"id\":0}")]
    [InlineData("{\"v\":1,\"id\":-3}")]
    [InlineData("{\"v\":1}")]
    [InlineData("[1,2]")]
    [Trait("Spec", "CAT-R6")]
    public void Invalid_cursor_is_rejected(string raw)
    {
        // Les cas JSON sont encodés en base64url pour tester la validation du contenu, pas seulement de l'encodage.
        var cursor = raw.StartsWith('{') || raw.StartsWith('[')
            ? System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw))
            : raw;

        Assert.False(ProductCursor.TryDecode(cursor, out _));
    }

    [Fact(DisplayName = "Un curseur au format documenté {\"v\":1,\"id\":n} est accepté")]
    [Trait("Spec", "CAT-R6")]
    public void Documented_cursor_format_is_accepted()
    {
        var cursor = System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"v\":1,\"id\":5}"));

        Assert.True(ProductCursor.TryDecode(cursor, out var lastId));
        Assert.Equal(5, lastId);
        Assert.Equal(cursor, ProductCursor.Encode(5));
    }

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
