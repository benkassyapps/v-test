using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Shop.Testing.Fakes;

/// <summary>
/// Simule la perte de l'accusé de commit (CART-R18) : le prochain commit est réellement VALIDÉ en base, puis une
/// exception est levée comme si la connexion avait été rompue avant la réponse. L'API répond alors 503 alors que la
/// modification est enregistrée : c'est exactement le cas où une nouvelle tentative du client doublerait la commande.
/// Seul le commit d'une commande du PANIER (contexte qui suit un panier ou une clé d'idempotence) consomme l'armement :
/// un commit concurrent d'une autre nature (synchronisation, planificateur) ne peut pas le « voler ».
/// </summary>
public sealed class CommitAcknowledgementLossInterceptor : DbTransactionInterceptor
{
    private int _armed;

    /// <summary>Le prochain commit d'une commande du panier (et lui seul) perd son accusé.</summary>
    public void ArmOnce() => Interlocked.Exchange(ref _armed, 1);

    public void Disarm() => Interlocked.Exchange(ref _armed, 0);

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ThrowIfArmed(eventData);
        return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        ThrowIfArmed(eventData);
        base.TransactionCommitted(transaction, eventData);
    }

    // Après SaveChanges, les entités enregistrées restent suivies : la table de l'une d'elles identifie la commande.
    private static bool IsCartCommand(DbContext? context) =>
        context?.ChangeTracker.Entries().Any(entry => entry.Metadata.GetTableName() is "Carts" or "IdempotencyKeys") == true;

    private void ThrowIfArmed(TransactionEndEventData eventData)
    {
        if (IsCartCommand(eventData.Context) && Interlocked.Exchange(ref _armed, 0) == 1)
        {
            throw new TimeoutException("Accusé de commit perdu (simulé par les tests) : la transaction est pourtant validée.");
        }
    }
}
