namespace Shop.Domain.Catalog;

/// <summary>Compteurs d'une synchronisation.</summary>
public sealed record SyncCounts(int Fetched, int Inserted, int Updated, int Discontinued, int Reactivated);

/// <summary>
/// Changements à appliquer au catalogue local pour refléter un instantané (calcul pur, sans effet de bord).
/// L'application (<see cref="ApplyTo"/>) n'a lieu qu'après la vérification du garde-fou.
/// </summary>
public sealed class SyncPlan
{
    internal SyncPlan(
        int fetched,
        int activeBefore,
        IReadOnlyList<ProviderProduct> toInsert,
        IReadOnlyList<(Product Product, ProviderProduct Source)> toUpdate,
        IReadOnlyList<(Product Product, ProviderProduct Source)> toReactivate,
        IReadOnlyList<Product> toDiscontinue)
    {
        Fetched = fetched;
        ActiveBefore = activeBefore;
        ToInsert = toInsert;
        ToUpdate = toUpdate;
        ToReactivate = toReactivate;
        ToDiscontinue = toDiscontinue;
    }

    public int Fetched { get; }

    /// <summary>Nombre de produits actifs avant la synchronisation (base du garde-fou).</summary>
    public int ActiveBefore { get; }

    public IReadOnlyList<ProviderProduct> ToInsert { get; }

    public IReadOnlyList<(Product Product, ProviderProduct Source)> ToUpdate { get; }

    public IReadOnlyList<(Product Product, ProviderProduct Source)> ToReactivate { get; }

    public IReadOnlyList<Product> ToDiscontinue { get; }

    public SyncCounts Counts => new(Fetched, ToInsert.Count, ToUpdate.Count, ToDiscontinue.Count, ToReactivate.Count);

    /// <summary>
    /// Applique le plan aux produits existants (suivis par l'appelant) et renvoie les nouveaux produits à ajouter.
    /// </summary>
    public IReadOnlyList<Product> ApplyTo(DateTimeOffset now)
    {
        foreach (var (product, source) in ToUpdate)
        {
            product.ApplyFrom(source, now);
        }

        foreach (var (product, source) in ToReactivate)
        {
            product.ApplyFrom(source, now);
        }

        foreach (var product in ToDiscontinue)
        {
            product.Discontinue(now);
        }

        return [.. ToInsert.Select(source => Product.Create(source, now))];
    }
}

/// <summary>Calcule le plan de synchronisation (CAT-R4, CAT-R9).</summary>
public static class SyncPlanner
{
    public static SyncPlan Plan(IReadOnlyCollection<Product> existing, CatalogSnapshot snapshot)
    {
        var byExternalId = existing.ToDictionary(product => product.ExternalId);
        var inSnapshot = new HashSet<int>();
        var toInsert = new List<ProviderProduct>();
        var toUpdate = new List<(Product, ProviderProduct)>();
        var toReactivate = new List<(Product, ProviderProduct)>();

        foreach (var source in snapshot.Products)
        {
            inSnapshot.Add(source.ExternalId);

            if (!byExternalId.TryGetValue(source.ExternalId, out var product))
            {
                toInsert.Add(source);
            }
            else if (!product.IsActive)
            {
                toReactivate.Add((product, source));
            }
            else if (!product.HasSameData(source))
            {
                toUpdate.Add((product, source));
            }

            // Sinon : données identiques, aucune écriture (idempotence, CAT-R4.3).
        }

        var toDiscontinue = existing
            .Where(product => product.IsActive && !inSnapshot.Contains(product.ExternalId))
            .ToList();

        return new SyncPlan(
            snapshot.Products.Count,
            existing.Count(product => product.IsActive),
            toInsert,
            toUpdate,
            toReactivate,
            toDiscontinue);
    }
}

/// <summary>
/// Garde-fou contre le retrait massif (CAT-R11) : une synchronisation qui retirerait plus d'une proportion
/// des produits actifs est bloquée, sauf forçage explicite par un exploitant.
/// </summary>
public sealed record DiscontinuationGuard(decimal MaxDiscontinuedRatio)
{
    /// <summary>Seuil strict : retirer exactement la proportion maximale est permis.</summary>
    public bool IsTriggered(SyncPlan plan) =>
        plan.ActiveBefore > 0
        && (decimal)plan.ToDiscontinue.Count / plan.ActiveBefore > MaxDiscontinuedRatio;
}
