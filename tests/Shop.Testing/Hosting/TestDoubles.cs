using Microsoft.Extensions.Logging.Testing;
using Shop.Testing.Fakes;

namespace Shop.Testing.Hosting;

/// <summary>
/// Doublures partagées par les deux instances de test : une seule horloge, un seul fournisseur,
/// un seul collecteur de logs et un seul injecteur de pannes, comme le seraient la base et le fournisseur réels.
/// </summary>
public sealed class TestDoubles
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    public TestClock Clock { get; } = new(DefaultNow);

    public FakeDummyJsonHandler Provider { get; } = new();

    public FakeLogCollector Logs { get; } = FakeLogCollector.Create(new FakeLogCollectorOptions());

    public FaultInjectionInterceptor Faults { get; } = new();

    public void Reset()
    {
        Clock.SetUtcNow(DefaultNow);
        Provider.Reset();
        Logs.Clear();
        Faults.Disarm();
    }
}
