using Shop.Application.Catalog;

namespace Shop.UnitTests.Application.Catalog;

/// <summary>Décision du planificateur : mêmes cas que les scénarios de declenchement.feature, en test rapide.</summary>
public sealed class SyncSchedulePolicyTests
{
    private readonly SyncSchedulePolicy _policy = new(new CatalogSyncOptions());

    [Theory(DisplayName = "La synchronisation est due une fois le créneau du jour échu et non couvert")]
    [InlineData("2026-11-15T02:05:00Z", "2026-11-16T02:10:00Z", true)] // créneau du 16 échu, dernière réussite la veille
    [InlineData("2026-11-16T02:05:00Z", "2026-11-16T14:00:00Z", false)] // déjà réussie pour ce créneau
    [InlineData("2026-11-15T02:05:00Z", "2026-11-16T01:30:00Z", false)] // créneau du 16 pas encore atteint
    [InlineData("2026-11-13T02:05:00Z", "2026-11-16T09:00:00Z", true)] // rattrapage après plusieurs jours d'arrêt
    [InlineData("2026-11-15T02:05:00Z", "2026-11-16T02:00:00Z", true)] // à l'instant exact du créneau
    [Trait("Spec", "CAT-R2")]
    public void Due_when_slot_not_covered(string lastSuccess, string now, bool expectedDue)
    {
        var decision = _policy.Evaluate(At(now), new SyncScheduleState(At(lastSuccess), 0, null));

        Assert.Equal(expectedDue, decision.IsDue);
    }

    [Fact(DisplayName = "Sans aucune réussite (première mise en service), la synchronisation est due immédiatement")]
    [Trait("Spec", "CAT-R2")]
    public void Due_on_first_start()
    {
        Assert.True(_policy.Evaluate(At("2026-11-16T01:00:00Z"), new SyncScheduleState(null, 0, null)).IsDue);
    }

    [Theory(DisplayName = "Après des échecs consécutifs, les tentatives sont espacées : 15 min, 30, 60, puis 2 h au maximum")]
    [InlineData(1, "2026-11-16T03:14:00Z", false)]
    [InlineData(1, "2026-11-16T03:15:00Z", true)]
    [InlineData(2, "2026-11-16T03:29:00Z", false)]
    [InlineData(2, "2026-11-16T03:30:00Z", true)]
    [InlineData(3, "2026-11-16T03:59:00Z", false)]
    [InlineData(3, "2026-11-16T04:00:00Z", true)]
    [InlineData(6, "2026-11-16T04:59:00Z", false)]
    [InlineData(6, "2026-11-16T05:00:00Z", true)]
    [Trait("Spec", "CAT-R2")]
    public void Retries_are_spaced_after_failures(int failures, string now, bool expectedDue)
    {
        var state = new SyncScheduleState(At("2026-11-15T02:05:00Z"), failures, At("2026-11-16T03:00:00Z"));

        Assert.Equal(expectedDue, _policy.Evaluate(At(now), state).IsDue);
    }

    [Fact(DisplayName = "La prochaine tentative est indiquée pendant l'attente")]
    [Trait("Spec", "CAT-R12")]
    public void Next_attempt_is_reported()
    {
        var state = new SyncScheduleState(At("2026-11-15T02:05:00Z"), 2, At("2026-11-16T03:00:00Z"));

        var decision = _policy.Evaluate(At("2026-11-16T03:10:00Z"), state);

        Assert.False(decision.IsDue);
        Assert.Equal(At("2026-11-16T03:30:00Z"), decision.NextAttemptAt);
    }

    [Fact(DisplayName = "Une fois le créneau couvert, la prochaine tentative est le créneau suivant")]
    [Trait("Spec", "CAT-R2")]
    public void Next_attempt_is_next_slot_when_covered()
    {
        var decision = _policy.Evaluate(At("2026-11-16T14:00:00Z"), new SyncScheduleState(At("2026-11-16T02:05:00Z"), 0, null));

        Assert.Equal(At("2026-11-17T02:00:00Z"), decision.NextAttemptAt);
    }

    [Theory(DisplayName = "Le délai reste plafonné à 2 h quel que soit le nombre d'échecs (pas de débordement)")]
    [InlineData(5)]
    [InlineData(31)]
    [InlineData(64)]
    [InlineData(int.MaxValue)]
    [Trait("Spec", "CAT-R2")]
    public void Retry_delay_never_overflows(int failures)
    {
        Assert.Equal(TimeSpan.FromHours(2), _policy.RetryDelay(failures));

        var state = new SyncScheduleState(At("2026-11-13T02:05:00Z"), failures, At("2026-11-16T03:00:00Z"));
        Assert.True(_policy.Evaluate(At("2026-11-16T05:00:00Z"), state).IsDue);
    }

    [Theory(DisplayName = "Un cron rare (créneau à plus d'un mois) est correctement pris en compte")]
    [InlineData("2024-02-01T00:00:00Z", true)] // dernier créneau 2024-02-29 non couvert
    [InlineData("2024-03-01T00:00:00Z", false)] // dernier créneau 2024-02-29 couvert
    [Trait("Spec", "CAT-R2")]
    public void Sparse_cron_is_supported(string lastSuccess, bool expectedDue)
    {
        var policy = new SyncSchedulePolicy(new CatalogSyncOptions { Cron = "0 2 29 2 *" });

        Assert.Equal(expectedDue, policy.Evaluate(At("2026-11-16T10:00:00Z"), new SyncScheduleState(At(lastSuccess), 0, null)).IsDue);
    }

    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
