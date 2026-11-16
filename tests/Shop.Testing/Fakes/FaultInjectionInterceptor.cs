using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Shop.Testing.Fakes;

/// <summary>
/// Simule une panne de base de données au moment de l'enregistrement (CART-R12, CAT-R8) :
/// tant qu'il est armé, chaque SaveChanges lève une <see cref="TimeoutException"/> (classée transitoire → 503).
/// Partagé par les deux instances de test.
/// </summary>
public sealed class FaultInjectionInterceptor : SaveChangesInterceptor
{
    private volatile bool _armed;

    public void Arm() => _armed = true;

    public void Disarm() => _armed = false;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed();
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfArmed();
        return base.SavingChanges(eventData, result);
    }

    private void ThrowIfArmed()
    {
        if (_armed)
        {
            throw new TimeoutException("Panne de base de données simulée par les tests.");
        }
    }
}
