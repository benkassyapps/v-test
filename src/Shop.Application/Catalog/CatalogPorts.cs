using Shop.Domain.Catalog;
using Shop.Domain.Common;

namespace Shop.Application.Catalog;

/// <summary>Fournisseur externe du catalogue (DummyJSON).</summary>
public interface ICatalogProvider
{
    /// <summary>
    /// Récupère un instantané COMPLET et validé. Échec (jamais d'exception pour une panne attendue) si le fournisseur
    /// est indisponible (<c>reason = provider_error</c>) ou si l'instantané est incomplet ou invalide (<c>reason = invalid_snapshot</c>).
    /// </summary>
    Task<Result<CatalogSnapshot>> FetchSnapshotAsync(CancellationToken cancellationToken);
}

/// <summary>Verrou distribué garantissant qu'une seule synchronisation s'exécute, toutes instances confondues (CAT-R10).</summary>
public interface ICatalogSyncLock
{
    /// <summary>Tente d'obtenir le verrou sans attendre ; null s'il est déjà détenu.</summary>
    Task<ICatalogSyncLockHandle?> TryAcquireAsync(CancellationToken cancellationToken);
}

public interface ICatalogSyncLockHandle : IAsyncDisposable
{
    /// <summary>Signalé si le verrou est perdu (connexion du verrou rompue) : la synchronisation doit s'interrompre.</summary>
    CancellationToken LostToken { get; }
}

/// <summary>Changements produits par le calcul de synchronisation : compteurs et nouveaux produits à insérer.</summary>
public sealed record CatalogChanges(SyncCounts Counts, IReadOnlyList<Product> NewProducts);

/// <summary>Résultat d'une synchronisation appliquée et validée.</summary>
public sealed record AppliedSync(SyncCounts Counts, DateTimeOffset FinishedAt);

/// <summary>Persistance du catalogue pour la synchronisation.</summary>
public interface ICatalogStore
{
    /// <summary>
    /// Applique une synchronisation dans UNE transaction (CAT-R8, CAT-R10) :
    /// 1. barrière : verrouille la ligne du run <paramref name="runId"/> et abandonne s'il n'est plus « en cours »
    ///    (verrou distribué perdu et run repris par une autre instance) ;
    /// 2. charge tous les produits, applique <paramref name="change"/> (un échec annule tout) ;
    /// 3. enregistre les produits ET passe le run à « réussi », puis valide le tout.
    /// Rejoué en cas d'erreur transitoire (opération idempotente).
    /// </summary>
    Task<Result<AppliedSync>> ApplyAsync(
        long runId,
        Func<IReadOnlyCollection<Product>, Result<CatalogChanges>> change,
        CancellationToken cancellationToken);
}

/// <summary>Historique des synchronisations.</summary>
public interface ISyncRunStore
{
    Task AddAsync(SyncRun run, CancellationToken cancellationToken);

    /// <summary>
    /// Enregistre l'issue d'un run terminé (échec, blocage) UNIQUEMENT s'il est encore « en cours » en base :
    /// un run déjà terminé (réussi, ou interrompu par une autre instance) n'est jamais réécrit.
    /// </summary>
    Task CompleteAsync(SyncRun run, CancellationToken cancellationToken);

    /// <summary>
    /// Passe en échec « interrompu » les runs encore en cours (instance arrêtée brutalement). À appeler sous le verrou.
    /// Mise à jour ensembliste conditionnelle : un run validé entre-temps n'est pas touché.
    /// </summary>
    Task<int> InterruptOrphanedRunsAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<SyncScheduleState> GetScheduleStateAsync(CancellationToken cancellationToken);

    Task<SyncRun?> GetLastSuccessAsync(CancellationToken cancellationToken);

    /// <summary>Dernier run terminé, hors « Skipped ».</summary>
    Task<SyncRun?> GetLastFinishedAsync(CancellationToken cancellationToken);

    /// <summary>Historique du plus récent au plus ancien (keyset sur l'identifiant).</summary>
    Task<IReadOnlyList<SyncRun>> ListAsync(long? beforeId, int take, CancellationToken cancellationToken);
}

/// <summary>Produit tel que l'expose la liste des produits.</summary>
public sealed record ProductView(
    int Id,
    int ExternalId,
    string Name,
    string Category,
    decimal Price,
    int Stock,
    string AvailabilityStatus,
    DateTimeOffset LastUpdatedAt);

/// <summary>Lecture du catalogue local (jamais du fournisseur, CAT-R7).</summary>
public interface IProductReader
{
    /// <summary>Produits actifs d'identifiant strictement supérieur à <paramref name="afterId"/>, triés par identifiant.</summary>
    Task<IReadOnlyList<ProductView>> ListActiveAsync(int afterId, int take, CancellationToken cancellationToken);
}
