using Microsoft.AspNetCore.Http.HttpResults;
using Shop.Api.Auth;
using Shop.Api.Errors;
using Shop.Application.Catalog;

namespace Shop.Api.Endpoints;

/// <summary>Endpoints du catalogue (liste des produits) et de l'administration de la synchronisation.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products", ListProductsAsync)
            .WithTags("Catalogue")
            .WithSummary("Liste paginée (curseur) des produits actifs du catalogue local")
            .WithDescription("Lecture locale uniquement : disponible même si le fournisseur est en panne. Aucune recherche ni filtre.");

        var admin = app.MapGroup("/api/admin/catalog")
            .WithTags("Administration du catalogue")
            .AddEndpointFilter<AdminKeyFilter>();

        admin.MapPost("/sync", SyncAsync)
            .WithSummary("Déclenche immédiatement une synchronisation du catalogue")
            .WithDescription("Même traitement que la synchronisation quotidienne. force=true lève le garde-fou de retrait massif.");
        admin.MapGet("/sync-runs", ListRunsAsync)
            .WithSummary("Historique des synchronisations, du plus récent au plus ancien");
        admin.MapGet("/sync-status", GetStatusAsync)
            .WithSummary("État de la synchronisation et action conseillée");

        return app;
    }

    // La vue de lecture est renvoyée telle quelle (comme CartView pour le panier) : aucune copie champ à champ.
    private static async Task<Results<Ok<ProductPage>, ProblemHttpResult>> ListProductsAsync(
        CatalogQueries queries, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        var page = await queries.ListProductsAsync(limit, cursor, cancellationToken);
        return page.IsSuccess ? TypedResults.Ok(page.Value) : page.Error.ToProblem();
    }

    private static async Task<Results<Ok<SyncRunResponse>, ProblemHttpResult>> SyncAsync(
        CatalogSyncService sync, IHostApplicationLifetime lifetime, bool? force)
    {
        // Volontairement indépendant de l'annulation de la requête : un client qui se déconnecte
        // n'interrompt pas la synchronisation. Seul l'arrêt de l'instance l'annule (rollback propre).
        var result = await sync.RunManualAsync(force ?? false, lifetime.ApplicationStopping);
        return result.IsSuccess ? TypedResults.Ok(SyncRunResponse.From(result.Value)) : result.Error.ToProblem();
    }

    private static async Task<Results<Ok<SyncRunPageResponse>, ProblemHttpResult>> ListRunsAsync(
        CatalogQueries queries, int? limit, long? beforeId, CancellationToken cancellationToken)
    {
        var page = await queries.ListRunsAsync(limit, beforeId, cancellationToken);
        return page.IsSuccess
            ? TypedResults.Ok(new SyncRunPageResponse([.. page.Value.Items.Select(SyncRunResponse.From)], page.Value.NextBeforeId, page.Value.HasMore))
            : page.Error.ToProblem();
    }

    private static async Task<Ok<SyncStatusResponse>> GetStatusAsync(CatalogQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(SyncStatusResponse.From(await queries.GetStatusAsync(cancellationToken)));
}
