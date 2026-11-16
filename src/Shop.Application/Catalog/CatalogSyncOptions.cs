namespace Shop.Application.Catalog;

/// <summary>Configuration de la synchronisation du catalogue (section « CatalogSync », specs/catalog-sync/design.md §8).</summary>
public sealed class CatalogSyncOptions
{
    public const string SectionName = "CatalogSync";

    /// <summary>Adresse du fournisseur.</summary>
    public Uri BaseUrl { get; set; } = new("https://dummyjson.com");

    /// <summary>Nombre de produits demandés par page au fournisseur.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Créneau quotidien (expression cron à 5 champs, en UTC).</summary>
    public string Cron { get; set; } = "0 2 * * *";

    /// <summary>Planificateur actif sur cette instance.</summary>
    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>Fréquence de vérification du planificateur.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Délai avant la 1re nouvelle tentative après un échec (doublé à chaque échec consécutif).</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Délai maximal entre deux tentatives.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Proportion maximale de produits actifs retirés par une synchronisation non forcée (CAT-R11).</summary>
    public decimal MaxDiscontinuedRatio { get; set; } = 0.5m;

    /// <summary>Au-delà de cette durée sans synchronisation réussie, une action est requise (CAT-R12).</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromHours(26);
}
