namespace Shop.Domain.Carts;

/// <summary>
/// Vue d'un produit du catalogue au moment d'un ajout au panier (lue dans la transaction).
/// Le nom et le prix sont copiés dans la ligne s'il s'agit d'une nouvelle ligne.
/// </summary>
public sealed record ProductSnapshot(int Id, string Name, decimal Price, bool IsActive);

/// <summary>Effet d'une commande acceptée : le contenu a changé (versions augmentées) ou pas.</summary>
public enum CartChange
{
    Changed,
    Unchanged,
}
