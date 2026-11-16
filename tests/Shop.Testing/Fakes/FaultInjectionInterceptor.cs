using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Shop.Testing.Fakes;

/// <summary>
/// Simule une panne de base de données au moment de l'enregistrement (CART-R12, CAT-R8) :
/// tant qu'il est armé, chaque SaveChanges concerné lève une <see cref="TimeoutException"/> (classée transitoire → 503).
/// Peut cibler un type d'entité (ex. seulement l'application du catalogue, pas l'historique des runs).
/// Partagé par les deux instances de test.
/// </summary>
public sealed class FaultInjectionInterceptor : SaveChangesInterceptor
{
    private volatile Func<DbContext, bool>? _when;

    /// <summary>Fait échouer tous les enregistrements.</summary>
    public void Arm() => _when = _ => true;

    /// <summary>Fait échouer les enregistrements qui ajoutent, modifient ou suppriment une entité de type <typeparamref name="TEntity"/>.</summary>
    public void ArmFor<TEntity>()
        where TEntity : class =>
        _when = context => context.ChangeTracker.Entries<TEntity>().Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

    public void Disarm() => _when = null;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfArmed(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ThrowIfArmed(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void ThrowIfArmed(DbContext? context)
    {
        if (context is not null && _when?.Invoke(context) == true)
        {
            throw new TimeoutException("Panne de base de données simulée par les tests.");
        }
    }
}
