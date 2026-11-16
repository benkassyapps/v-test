using Shop.Domain.Catalog;
using Shop.Domain.Common;

namespace Shop.UnitTests.Domain.Catalog;

public sealed class SyncPlannerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddDays(1);

    [Fact(DisplayName = "Une première synchronisation insère tous les produits, actifs et datés")]
    [Trait("Spec", "CAT-R4")]
    [Trait("Spec", "CAT-R5")]
    public void First_sync_inserts_everything()
    {
        var plan = SyncPlanner.Plan([], Snapshot(Source(1, "Mascara", 9.99m), Source(2, "Parfum", 49.99m)));

        Assert.Equal(new SyncCounts(2, 2, 0, 0, 0), plan.Counts);
        var inserted = plan.ApplyTo(T1);
        Assert.All(inserted, product =>
        {
            Assert.Equal(ProductStatus.Active, product.Status);
            Assert.Equal(T1, product.LastUpdatedAt);
        });
        Assert.Equal(["Mascara", "Parfum"], inserted.Select(product => product.Name));
    }

    [Fact(DisplayName = "Des données identiques ne produisent aucune écriture (idempotence)")]
    [Trait("Spec", "CAT-R4")]
    public void Identical_data_produces_no_change()
    {
        var existing = Existing(Source(1, "Mascara", 9.99m), Source(2, "Parfum", 49.99m));

        var plan = SyncPlanner.Plan(existing, Snapshot(Source(1, "Mascara", 9.99m), Source(2, "Parfum", 49.99m)));

        Assert.Equal(new SyncCounts(2, 0, 0, 0, 0), plan.Counts);
        Assert.Empty(plan.ApplyTo(T1));
        Assert.All(existing, product => Assert.Equal(T0, product.LastUpdatedAt));
    }

    [Fact(DisplayName = "Un produit modifié est mis à jour sans doublon et seule sa date change")]
    [Trait("Spec", "CAT-R4")]
    [Trait("Spec", "CAT-R5")]
    public void Changed_product_is_updated()
    {
        var existing = Existing(Source(1, "Mascara", 9.99m), Source(2, "Parfum", 49.99m));

        var plan = SyncPlanner.Plan(existing, Snapshot(Source(1, "Mascara Pro", 12.50m), Source(2, "Parfum", 49.99m)));
        var inserted = plan.ApplyTo(T1);

        Assert.Equal(new SyncCounts(2, 0, 1, 0, 0), plan.Counts);
        Assert.Empty(inserted);
        var mascara = existing.Single(product => product.ExternalId == 1);
        Assert.Equal(("Mascara Pro", 12.50m, T1), (mascara.Name, mascara.Price, mascara.LastUpdatedAt));
        Assert.Equal(T0, existing.Single(product => product.ExternalId == 2).LastUpdatedAt);
    }

    [Theory(DisplayName = "Chaque champ métier modifié déclenche une mise à jour")]
    [InlineData("category")]
    [InlineData("stock")]
    [InlineData("availability")]
    [Trait("Spec", "CAT-R5")]
    public void Every_business_field_is_compared(string field)
    {
        var original = Source(1, "Mascara", 9.99m);
        var existing = Existing(original);
        var changed = field switch
        {
            "category" => original with { Category = "makeup" },
            "stock" => original with { Stock = 3 },
            _ => original with { AvailabilityStatus = "Low Stock" },
        };

        Assert.Equal(1, SyncPlanner.Plan(existing, Snapshot(changed)).Counts.Updated);
    }

    [Fact(DisplayName = "Un produit absent est retiré sans être supprimé")]
    [Trait("Spec", "CAT-R9")]
    public void Missing_product_is_discontinued()
    {
        var existing = Existing(Source(1, "Mascara", 9.99m), Source(2, "Parfum", 49.99m), Source(3, "Lampe", 19.50m));

        var plan = SyncPlanner.Plan(existing, Snapshot(Source(1, "Mascara", 9.99m), Source(3, "Lampe", 19.50m)));
        plan.ApplyTo(T1);

        Assert.Equal(new SyncCounts(2, 0, 0, 1, 0), plan.Counts);
        var parfum = existing.Single(product => product.ExternalId == 2);
        Assert.Equal((ProductStatus.Discontinued, (DateTimeOffset?)T1), (parfum.Status, parfum.DiscontinuedAt));
    }

    [Fact(DisplayName = "Un produit retiré qui réapparaît est réactivé avec ses nouvelles données")]
    [Trait("Spec", "CAT-R9")]
    public void Returning_product_is_reactivated()
    {
        var existing = Existing(Source(1, "Mascara", 9.99m), Source(2, "Parfum", 49.99m));
        SyncPlanner.Plan(existing, Snapshot(Source(1, "Mascara", 9.99m))).ApplyTo(T0);

        var plan = SyncPlanner.Plan(existing, Snapshot(Source(1, "Mascara", 9.99m), Source(2, "Parfum", 45.00m)));
        plan.ApplyTo(T1);

        Assert.Equal(new SyncCounts(2, 0, 0, 0, 1), plan.Counts);
        var parfum = existing.Single(product => product.ExternalId == 2);
        Assert.Equal((ProductStatus.Active, 45.00m, (DateTimeOffset?)null), (parfum.Status, parfum.Price, parfum.DiscontinuedAt));
    }

    // --- Garde-fou ---

    [Theory(DisplayName = "Le garde-fou bloque au-delà du seuil strict de 50 %")]
    [InlineData(10, 5, false)]
    [InlineData(10, 4, true)]
    [InlineData(10, 10, false)]
    [InlineData(2, 1, false)]
    [Trait("Spec", "CAT-R11")]
    public void Guard_threshold_is_strict(int active, int remaining, bool expectedTriggered)
    {
        var sources = Enumerable.Range(1, active).Select(id => Source(id, $"P{id}", 1m)).ToArray();
        var existing = Existing(sources);

        var plan = SyncPlanner.Plan(existing, Snapshot(sources.Take(remaining).ToArray()));

        Assert.Equal(expectedTriggered, new DiscontinuationGuard(0.5m).IsTriggered(plan));
    }

    [Fact(DisplayName = "Un catalogue vide chez le fournisseur déclenche le garde-fou")]
    [Trait("Spec", "CAT-R11")]
    public void Empty_provider_catalog_triggers_guard()
    {
        var existing = Existing(Source(1, "Mascara", 9.99m), Source(2, "Parfum", 49.99m));

        var plan = SyncPlanner.Plan(existing, Snapshot());

        Assert.True(new DiscontinuationGuard(0.5m).IsTriggered(plan));
        Assert.Equal((2, 2), (plan.ActiveBefore, plan.ToDiscontinue.Count));
    }

    [Fact(DisplayName = "La première synchronisation n'est jamais bloquée par le garde-fou")]
    [Trait("Spec", "CAT-R11")]
    public void First_sync_is_never_blocked()
    {
        Assert.False(new DiscontinuationGuard(0.5m).IsTriggered(SyncPlanner.Plan([], Snapshot())));
        Assert.False(new DiscontinuationGuard(0.5m).IsTriggered(SyncPlanner.Plan([], Snapshot(Source(1, "Mascara", 9.99m)))));
    }

    // --- Validation de l'instantané ---

    [Fact(DisplayName = "Un instantané dont le nombre de produits diffère du total annoncé est rejeté")]
    [Trait("Spec", "CAT-R1")]
    public void Incomplete_snapshot_is_rejected()
    {
        var result = CatalogSnapshot.Create([Source(1, "A", 1m), Source(2, "B", 1m), Source(3, "C", 1m)], announcedTotal: 4);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.ProviderUnavailable, result.Error.Code);
    }

    [Fact(DisplayName = "Un instantané avec un produit en double est rejeté")]
    [Trait("Spec", "CAT-R1")]
    public void Duplicate_in_snapshot_is_rejected()
    {
        Assert.False(CatalogSnapshot.Create([Source(1, "A", 1m), Source(1, "A bis", 1m)], announcedTotal: 2).IsSuccess);
    }

    [Theory(DisplayName = "Un instantané avec des données inexploitables est rejeté")]
    [InlineData(0, "A", 1, 1)]
    [InlineData(1, "", 1, 1)]
    [InlineData(1, "A", -1, 1)]
    [InlineData(1, "A", 1, -1)]
    [Trait("Spec", "CAT-R1")]
    public void Malformed_snapshot_is_rejected(int id, string name, int price, int stock)
    {
        var product = new ProviderProduct(id, name, "cat", price, stock, "In Stock", null);

        Assert.False(CatalogSnapshot.Create([product], announcedTotal: 1).IsSuccess);
    }

    [Theory(DisplayName = "Un instantané incompatible avec le schéma (texte trop long ou absent) est rejeté")]
    [InlineData("name")]
    [InlineData("category")]
    [InlineData("availability")]
    [InlineData("null-category")]
    [Trait("Spec", "CAT-R1")]
    public void Snapshot_must_fit_the_schema(string field)
    {
        var valid = Source(1, "Mascara", 9.99m);
        var invalid = field switch
        {
            "name" => valid with { Name = new string('n', Product.MaxNameLength + 1) },
            "category" => valid with { Category = new string('c', Product.MaxCategoryLength + 1) },
            "availability" => valid with { AvailabilityStatus = new string('a', Product.MaxAvailabilityStatusLength + 1) },
            _ => valid with { Category = null! },
        };

        Assert.False(CatalogSnapshot.Create([invalid], announcedTotal: 1).IsSuccess);
    }

    [Fact(DisplayName = "Un prix à plus de 2 décimales est arrondi : les synchronisations suivantes restent idempotentes")]
    [Trait("Spec", "CAT-R4")]
    public void Price_is_rounded_to_stored_scale()
    {
        var snapshot = Snapshot(Source(1, "Mascara", 9.999m));
        Assert.Equal(10.00m, snapshot.Products[0].Price);

        var existing = Existing(snapshot.Products[0]);
        Assert.Equal(0, SyncPlanner.Plan(existing, Snapshot(Source(1, "Mascara", 9.999m))).Counts.Updated);
    }

    // --- Runs ---

    [Fact(DisplayName = "Un run bloqué par le garde-fou garde les compteurs utiles au diagnostic")]
    [Trait("Spec", "CAT-R12")]
    public void Guard_blocked_run_keeps_diagnostic()
    {
        var run = SyncRun.Start(SyncTrigger.Scheduled, "instance-1", forced: false, T0);

        run.BlockByGuard(activeBefore: 194, wouldDiscontinue: 194, fetched: 0, T1);

        Assert.Equal((SyncRunStatus.Failed, SyncFailureReason.GuardTriggered), (run.Status, run.FailureReason!.Value));
        Assert.Equal((194, 194), (run.ActiveBefore!.Value, run.WouldDiscontinue!.Value));
        Assert.Contains("194 produits sur 194", run.Error, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Un run terminé ne peut plus changer d'état")]
    [Trait("Spec", "CAT-R8")]
    public void Finished_run_is_immutable()
    {
        var run = SyncRun.Start(SyncTrigger.Manual, "instance-1", forced: false, T0);
        run.Succeed(new SyncCounts(1, 1, 0, 0, 0), T1);

        Assert.Throws<InvalidOperationException>(() => run.Fail(SyncFailureReason.ProviderError, "x", T1));
    }

    [Fact(DisplayName = "La cause d'un échec est tronquée à 1000 caractères")]
    [Trait("Spec", "CAT-R8")]
    public void Error_is_truncated()
    {
        var run = SyncRun.Start(SyncTrigger.Manual, "instance-1", forced: false, T0);

        run.Fail(SyncFailureReason.ProviderError, new string('x', 5000), T1);

        Assert.Equal(SyncRun.MaxErrorLength, run.Error!.Length);
    }

    // --- Outils ---

    private static ProviderProduct Source(int id, string name, decimal price) =>
        new(id, name, "cat", price, Stock: 10, AvailabilityStatus: "In Stock", ProviderUpdatedAt: null);

    private static CatalogSnapshot Snapshot(params ProviderProduct[] products) =>
        CatalogSnapshot.Create(products, products.Length).Value;

    private static List<Product> Existing(params ProviderProduct[] sources) =>
        [.. sources.Select(source => Product.Create(source, T0))];
}
