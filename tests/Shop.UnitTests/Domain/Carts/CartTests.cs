using Shop.Domain.Carts;
using Shop.Domain.Common;

namespace Shop.UnitTests.Domain.Carts;

public sealed class CartTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);
    private static readonly ProductSnapshot A = new(1, "A", 10.00m, IsActive: true);
    private static readonly ProductSnapshot B = new(2, "B", 2.50m, IsActive: true);
    private static readonly ProductSnapshot Retired = new(3, "Retiré", 7.00m, IsActive: false);

    // --- Création ---

    [Fact(DisplayName = "Un panier est créé vide, en version 0")]
    [Trait("Spec", "CART-R7")]
    [Trait("Spec", "CART-R13")]
    public void New_cart_is_empty_at_version_0()
    {
        var cart = Cart.Create(Guid.NewGuid(), "alice", T0);

        Assert.Equal(0, cart.Version);
        Assert.Empty(cart.Lines);
        Assert.Equal(0m, cart.Total);
    }

    // --- Ajout ---

    [Fact(DisplayName = "Ajouter un produit crée une ligne en version 1 avec le nom et le prix copiés, et le panier passe en version 1")]
    [Trait("Spec", "CART-R6")]
    [Trait("Spec", "CART-R7")]
    [Trait("Spec", "CART-R8")]
    [Trait("Spec", "CART-R15")]
    public void Adding_creates_line()
    {
        var cart = NewCart();
        var lineId = Guid.NewGuid();

        var result = cart.AddItem(A.Id, A, 2, lineId, T1);

        Assert.True(result.IsSuccess);
        Assert.Equal(CartChange.Changed, result.Value);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(lineId, line.Id);
        Assert.Equal(("A", 10.00m, 2, 1L), (line.ProductName, line.UnitPrice, line.Quantity, line.Version));
        Assert.Equal(1, cart.Version);
        Assert.Equal(T1, cart.UpdatedAt);
    }

    [Fact(DisplayName = "Réajouter un produit cumule la quantité sur la même ligne, sans changer le nom ni le prix conservés")]
    [Trait("Spec", "CART-R5")]
    [Trait("Spec", "CART-R6")]
    public void Adding_again_merges_into_existing_line()
    {
        var cart = NewCart();
        var lineId = Guid.NewGuid();
        cart.AddItem(A.Id, A, 2, lineId, T0);

        var renamedAndRepriced = A with { Name = "A nouvelle formule", Price = 15.00m };
        var result = cart.AddItem(A.Id, renamedAndRepriced, 3, Guid.NewGuid(), T1);

        Assert.True(result.IsSuccess);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(lineId, line.Id);
        Assert.Equal(("A", 10.00m, 5, 2L), (line.ProductName, line.UnitPrice, line.Quantity, line.Version));
        Assert.Equal(2, cart.Version);
        Assert.Equal(50.00m, cart.Total);
    }

    [Theory(DisplayName = "Une quantité hors de [1, 999] est refusée sans modifier le panier")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    [Trait("Spec", "CART-R4")]
    public void Out_of_range_quantity_is_rejected(int quantity)
    {
        var cart = CartWith((A, 2));
        var before = State(cart);

        var result = cart.AddItem(A.Id, A, quantity, Guid.NewGuid(), T1);

        AssertError(result, ErrorCodes.ValidationFailed);
        Assert.Equal(before, State(cart));
    }

    [Theory(DisplayName = "Les bornes 1 et 999 sont acceptées")]
    [InlineData(1)]
    [InlineData(999)]
    [Trait("Spec", "CART-R4")]
    public void Boundary_quantities_are_accepted(int quantity)
    {
        var cart = NewCart();

        Assert.True(cart.AddItem(A.Id, A, quantity, Guid.NewGuid(), T1).IsSuccess);
        Assert.Equal(quantity, cart.Lines[0].Quantity);
    }

    [Fact(DisplayName = "Le cumul peut atteindre exactement 999")]
    [Trait("Spec", "CART-R4")]
    public void Cumulated_quantity_can_reach_999()
    {
        var cart = CartWith((A, 998));

        Assert.True(cart.AddItem(A.Id, A, 1, Guid.NewGuid(), T1).IsSuccess);
        Assert.Equal(999, cart.Lines[0].Quantity);
    }

    [Fact(DisplayName = "Un ajout qui dépasserait 999 est refusé en entier, sans altérer le panier")]
    [Trait("Spec", "CART-R4")]
    [Trait("Spec", "CART-R7")]
    public void Cumulated_quantity_above_999_is_rejected_without_change()
    {
        var cart = CartWith((A, 998));
        var before = State(cart);

        var result = cart.AddItem(A.Id, A, 2, Guid.NewGuid(), T1);

        AssertError(result, ErrorCodes.QuantityLimitExceeded);
        Assert.Equal(before, State(cart));
    }

    [Fact(DisplayName = "Un produit inexistant ne peut pas être ajouté")]
    [Trait("Spec", "CART-R3")]
    public void Missing_product_is_rejected()
    {
        var cart = CartWith((A, 2));
        var before = State(cart);

        var result = cart.AddItem(42, product: null, 1, Guid.NewGuid(), T1);

        AssertError(result, ErrorCodes.ProductUnavailable);
        Assert.Equal(42, result.Error!.Details!["productId"]);
        Assert.Equal(before, State(cart));
    }

    [Fact(DisplayName = "Un produit retiré ne peut être ajouté ni comme nouvelle ligne ni en cumul")]
    [Trait("Spec", "CART-R3")]
    [Trait("Spec", "CART-R11")]
    public void Retired_product_is_rejected()
    {
        var cart = CartWith((Retired with { IsActive = true }, 2));
        var before = State(cart);

        AssertError(cart.AddItem(Retired.Id, Retired, 1, Guid.NewGuid(), T1), ErrorCodes.ProductUnavailable);
        Assert.Equal(before, State(cart));
    }

    // --- Modification de quantité ---

    [Fact(DisplayName = "La nouvelle quantité remplace l'ancienne et augmente les deux versions de 1")]
    [Trait("Spec", "CART-R16")]
    [Trait("Spec", "CART-R7")]
    [Trait("Spec", "CART-R8")]
    public void Changing_quantity_replaces_it()
    {
        var cart = CartWith((A, 2), (B, 4));
        var lineA = LineOf(cart, A);

        var result = cart.ChangeQuantity(lineA.Id, 5, expectedVersion: 1, productActive: true, T1);

        Assert.True(result.IsSuccess);
        Assert.Equal((5, 2L), (lineA.Quantity, lineA.Version));
        Assert.Equal(1L, LineOf(cart, B).Version);
        Assert.Equal(3, cart.Version);
    }

    [Fact(DisplayName = "Remplacer une quantité par la même valeur ne change aucune version")]
    [Trait("Spec", "CART-R7")]
    public void Same_quantity_is_not_an_effective_change()
    {
        var cart = CartWith((A, 2));
        var before = State(cart);

        var result = cart.ChangeQuantity(LineOf(cart, A).Id, 2, expectedVersion: 1, productActive: true, T1);

        Assert.True(result.IsSuccess);
        Assert.Equal(CartChange.Unchanged, result.Value);
        Assert.Equal(before, State(cart));
    }

    [Fact(DisplayName = "Même avec la même quantité, une version périmée est un conflit")]
    [Trait("Spec", "CART-R8")]
    public void Stale_version_is_checked_before_no_op()
    {
        var cart = CartWith((A, 2));
        var line = LineOf(cart, A);
        cart.ChangeQuantity(line.Id, 3, 1, true, T1);

        AssertError(cart.ChangeQuantity(line.Id, 3, expectedVersion: 1, productActive: true, T1), ErrorCodes.CartLineVersionConflict);
    }

    [Fact(DisplayName = "Une modification fondée sur une version périmée est rejetée avec la version actuelle, sans rien modifier")]
    [Trait("Spec", "CART-R8")]
    public void Stale_version_is_rejected()
    {
        var cart = CartWith((A, 2));
        var line = LineOf(cart, A);
        cart.ChangeQuantity(line.Id, 3, 1, true, T1);
        var before = State(cart);

        var result = cart.ChangeQuantity(line.Id, 7, expectedVersion: 1, productActive: true, T1);

        AssertError(result, ErrorCodes.CartLineVersionConflict);
        Assert.Equal(2L, result.Error!.Details!["currentVersion"]);
        Assert.Equal(before, State(cart));
    }

    [Fact(DisplayName = "Modifier deux lignes différentes à partir de la même lecture ne crée aucun conflit")]
    [Trait("Spec", "CART-R9")]
    public void Changes_on_different_lines_do_not_conflict()
    {
        var cart = CartWith((A, 2), (B, 4));
        var (lineA, lineB) = (LineOf(cart, A), LineOf(cart, B));

        // Même lecture initiale : versions de ligne 1 et 1, panier en version 2.
        Assert.True(cart.ChangeQuantity(lineA.Id, 3, expectedVersion: 1, productActive: true, T1).IsSuccess);
        Assert.True(cart.ChangeQuantity(lineB.Id, 7, expectedVersion: 1, productActive: true, T1).IsSuccess);

        Assert.Equal(4, cart.Version);
    }

    [Theory(DisplayName = "Une nouvelle quantité hors de [1, 999] est refusée")]
    [InlineData(0)]
    [InlineData(1000)]
    [Trait("Spec", "CART-R4")]
    public void Out_of_range_new_quantity_is_rejected(int quantity)
    {
        var cart = CartWith((A, 2));
        var before = State(cart);

        AssertError(cart.ChangeQuantity(LineOf(cart, A).Id, quantity, 1, true, T1), ErrorCodes.ValidationFailed);
        Assert.Equal(before, State(cart));
    }

    [Fact(DisplayName = "Modifier une ligne absente (supprimée ou d'un autre panier) est introuvable")]
    [Trait("Spec", "CART-R10")]
    [Trait("Spec", "CART-R2")]
    public void Unknown_line_is_not_found()
    {
        var cart = CartWith((A, 2));
        var before = State(cart);

        AssertError(cart.ChangeQuantity(Guid.NewGuid(), 5, 1, true, T1), ErrorCodes.CartLineNotFound);
        AssertError(cart.RemoveLine(Guid.NewGuid(), 1, T1), ErrorCodes.CartLineNotFound);
        Assert.Equal(before, State(cart));
    }

    [Fact(DisplayName = "La quantité d'un produit retiré peut être réduite mais pas augmentée")]
    [Trait("Spec", "CART-R11")]
    public void Retired_product_quantity_can_only_decrease()
    {
        var cart = CartWith((A, 5));
        var line = LineOf(cart, A);
        var before = State(cart);

        AssertError(cart.ChangeQuantity(line.Id, 6, 1, productActive: false, T1), ErrorCodes.ProductUnavailable);
        Assert.Equal(before, State(cart));

        Assert.True(cart.ChangeQuantity(line.Id, 1, 1, productActive: false, T1).IsSuccess);
        Assert.Equal(1, line.Quantity);
    }

    // --- Suppression ---

    [Fact(DisplayName = "Supprimer une ligne la retire et augmente la version du panier de 1")]
    [Trait("Spec", "CART-R17")]
    [Trait("Spec", "CART-R7")]
    public void Removing_line()
    {
        var cart = CartWith((A, 2), (B, 4));

        Assert.True(cart.RemoveLine(LineOf(cart, A).Id, expectedVersion: 1, T1).IsSuccess);

        Assert.Equal("B", Assert.Single(cart.Lines).ProductName);
        Assert.Equal(3, cart.Version);
        Assert.Equal(10.00m, cart.Total);
    }

    [Fact(DisplayName = "Une suppression fondée sur une version périmée est rejetée")]
    [Trait("Spec", "CART-R8")]
    [Trait("Spec", "CART-R17")]
    public void Removing_with_stale_version_is_rejected()
    {
        var cart = CartWith((A, 2));
        var line = LineOf(cart, A);
        cart.ChangeQuantity(line.Id, 3, 1, true, T1);
        var before = State(cart);

        AssertError(cart.RemoveLine(line.Id, expectedVersion: 1, T1), ErrorCodes.CartLineVersionConflict);
        Assert.Equal(before, State(cart));
    }

    [Fact(DisplayName = "La ligne d'un produit retiré peut être supprimée")]
    [Trait("Spec", "CART-R11")]
    public void Retired_product_line_can_be_removed()
    {
        var cart = CartWith((A, 2));

        Assert.True(cart.RemoveLine(LineOf(cart, A).Id, 1, T1).IsSuccess);
        Assert.Empty(cart.Lines);
    }

    [Fact(DisplayName = "Après suppression, un réajout crée une nouvelle ligne avec les valeurs actuelles et l'ancienne ligne reste introuvable")]
    [Trait("Spec", "CART-R10")]
    [Trait("Spec", "CART-R6")]
    public void Re_adding_after_removal_creates_a_new_line()
    {
        var cart = CartWith((A, 2));
        var oldLine = LineOf(cart, A);
        cart.RemoveLine(oldLine.Id, 1, T1);
        var newLineId = Guid.NewGuid();

        cart.AddItem(A.Id, A with { Price = 12.00m }, 1, newLineId, T1);

        var line = Assert.Single(cart.Lines);
        Assert.Equal((newLineId, 1L, 1, 12.00m), (line.Id, line.Version, line.Quantity, line.UnitPrice));
        AssertError(cart.ChangeQuantity(oldLine.Id, 5, 1, true, T1), ErrorCodes.CartLineNotFound);
        Assert.Equal(1, line.Quantity);
    }

    // --- Montants ---

    [Fact(DisplayName = "Le montant total est la somme des lignes conservées, produits retirés compris")]
    [Trait("Spec", "CART-R14")]
    [Trait("Spec", "CART-R6")]
    public void Total_uses_kept_prices()
    {
        var cart = CartWith((A, 2), (B, 4));

        Assert.Equal(20.00m, LineOf(cart, A).LineTotal);
        Assert.Equal(10.00m, LineOf(cart, B).LineTotal);
        Assert.Equal(30.00m, cart.Total);
    }

    // --- Outils ---

    private static Cart NewCart() => Cart.Create(Guid.NewGuid(), "alice", T0);

    private static Cart CartWith(params (ProductSnapshot Product, int Quantity)[] items)
    {
        var cart = NewCart();
        foreach (var (product, quantity) in items)
        {
            Assert.True(cart.AddItem(product.Id, product with { IsActive = true }, quantity, Guid.NewGuid(), T0).IsSuccess);
        }

        return cart;
    }

    private static CartLine LineOf(Cart cart, ProductSnapshot product) => cart.Lines.Single(line => line.ProductId == product.Id);

    /// <summary>Photographie complète de l'état du panier, pour vérifier qu'une commande rejetée ne modifie rien.</summary>
    private static string State(Cart cart) =>
        $"v{cart.Version}@{cart.UpdatedAt:O}|" + string.Join(";", cart.Lines.Select(line =>
            $"{line.Id}:{line.ProductId}:{line.ProductName}:{line.UnitPrice}:{line.Quantity}:v{line.Version}"));

    private static void AssertError(Result result, string expectedCode)
    {
        Assert.False(result.IsSuccess, "La commande aurait dû être rejetée.");
        Assert.Equal(expectedCode, result.Error.Code);
    }
}
