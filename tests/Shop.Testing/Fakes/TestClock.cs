namespace Shop.Testing.Fakes;

/// <summary>
/// Horloge métier des tests, réglable librement (y compris en arrière, contrairement à FakeTimeProvider) :
/// les scénarios fixent des instants arbitraires (« il est 2026-11-16T02:10:00Z ») et chaque scénario repart
/// de la même date. Les minuteries restent celles du temps réel (implémentation de base de TimeProvider) :
/// seuls les horodatages métier sont simulés.
/// </summary>
public sealed class TestClock(DateTimeOffset initial) : TimeProvider
{
    private long _utcTicks = initial.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void SetUtcNow(DateTimeOffset value) => Interlocked.Exchange(ref _utcTicks, value.UtcTicks);

    public void Advance(TimeSpan delta) => Interlocked.Add(ref _utcTicks, delta.Ticks);
}
