using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Shop.Api.Auth;
using Shop.Api.Errors;
using Shop.Application.Carts;
using Shop.Domain.Carts;
using Shop.Domain.Common;

namespace Shop.Api.Endpoints;

/// <summary>Corps de POST /api/cart/lines.</summary>
public sealed class AddCartLineRequest
{
    public int? ProductId { get; init; }

    public int? Quantity { get; init; }
}

/// <summary>Corps de PUT /api/cart/lines/{lineId}.</summary>
public sealed class ChangeQuantityRequest
{
    public int? Quantity { get; init; }

    /// <summary>Version de la ligne lue par le client (CART-R8).</summary>
    public long? ExpectedVersion { get; init; }
}

/// <summary>
/// Endpoints du panier personnel (CART-R13 à CART-R17). Aucun identifiant de panier dans les routes : chaque opération
/// vise le panier de l'utilisateur identifié par X-User-Id (CART-R2).
/// </summary>
public static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/api/cart").WithTags("Panier").RequireAuthorization();

        cart.MapPut(string.Empty, GetOrCreateAsync).WithSummary("Crée le panier de l'utilisateur, ou renvoie le panier existant (idempotent)");
        cart.MapGet(string.Empty, GetAsync).WithSummary("Consulte le panier : lignes, quantités, prix conservés, montant total et versions");
        cart.MapPost("/lines", AddLineAsync)
            .WithSummary("Ajoute un produit (ou cumule sa quantité) ; crée le panier si besoin");
        cart.MapPut("/lines/{lineId:guid}", ChangeQuantityAsync)
            .WithSummary("Remplace la quantité d'une ligne à partir de la version consultée (409 si elle a changé)");
        cart.MapDelete("/lines/{lineId:guid}", RemoveLineAsync)
            .WithSummary("Supprime une ligne à partir de la version consultée (409 si elle a changé)");

        return app;
    }

    private static async Task<Results<Created<CartView>, Ok<CartView>>> GetOrCreateAsync(
        ClaimsPrincipal user, CartService carts, CancellationToken cancellationToken)
    {
        var (cart, created) = await carts.GetOrCreateAsync(user.GetUserId(), cancellationToken);
        return created ? TypedResults.Created("/api/cart", cart) : TypedResults.Ok(cart);
    }

    private static async Task<Results<Ok<CartView>, ProblemHttpResult>> GetAsync(
        ClaimsPrincipal user, CartService carts, CancellationToken cancellationToken) =>
        ToResponse(await carts.GetAsync(user.GetUserId(), cancellationToken));

    private static async Task<Results<Ok<CartView>, ProblemHttpResult>> AddLineAsync(
        AddCartLineRequest request, ClaimsPrincipal user, CartService carts, CancellationToken cancellationToken)
    {
        var errors = new Validation()
            .Require("productId", request.ProductId, "L'identifiant du produit est obligatoire.")
            .Quantity(request.Quantity);
        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        return ToResponse(await carts.AddItemAsync(user.GetUserId(), request.ProductId!.Value, request.Quantity!.Value, cancellationToken));
    }

    private static async Task<Results<Ok<CartView>, ProblemHttpResult>> ChangeQuantityAsync(
        Guid lineId, ChangeQuantityRequest request, ClaimsPrincipal user, CartService carts, CancellationToken cancellationToken)
    {
        var errors = new Validation()
            .Quantity(request.Quantity)
            .Require("expectedVersion", request.ExpectedVersion, "La version de la ligne consultée (expectedVersion) est obligatoire.");
        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        return ToResponse(await carts.ChangeQuantityAsync(
            user.GetUserId(), lineId, request.Quantity!.Value, request.ExpectedVersion!.Value, cancellationToken));
    }

    private static async Task<Results<Ok<CartView>, ProblemHttpResult>> RemoveLineAsync(
        Guid lineId, long? expectedVersion, ClaimsPrincipal user, CartService carts, CancellationToken cancellationToken)
    {
        var errors = new Validation()
            .Require("expectedVersion", expectedVersion, "La version de la ligne consultée (paramètre expectedVersion) est obligatoire.");
        if (errors.HasErrors)
        {
            return errors.ToProblem();
        }

        return ToResponse(await carts.RemoveLineAsync(user.GetUserId(), lineId, expectedVersion!.Value, cancellationToken));
    }

    private static Results<Ok<CartView>, ProblemHttpResult> ToResponse(Result<CartView> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();

    /// <summary>Validation explicite des entrées, avec messages en français et détail par champ (PLT-3).</summary>
    private sealed class Validation
    {
        private readonly Dictionary<string, string[]> _errors = [];

        public bool HasErrors => _errors.Count > 0;

        public Validation Require<T>(string field, T? value, string message)
            where T : struct
        {
            if (value is null)
            {
                _errors[field] = [message];
            }

            return this;
        }

        public Validation Quantity(int? quantity)
        {
            if (quantity is null)
            {
                _errors["quantity"] = ["La quantité est obligatoire."];
            }
            else if (!CartRules.IsValidQuantity(quantity.Value))
            {
                _errors["quantity"] = [CartRules.QuantityRangeMessage];
            }

            return this;
        }

        public ProblemHttpResult ToProblem() =>
            DomainError.Validation(
                    "Un ou plusieurs champs de la requête sont invalides.",
                    new Dictionary<string, object?> { ["errors"] = _errors })
                .ToProblem();
    }
}
