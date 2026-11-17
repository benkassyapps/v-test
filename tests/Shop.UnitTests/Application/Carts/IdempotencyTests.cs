using Shop.Application.Carts;

namespace Shop.UnitTests.Application.Carts;

public sealed class IdempotencyTests
{
    private static readonly Guid Line = Guid.Parse("0193a2b4-0000-7000-8000-000000000001");

    [Fact(DisplayName = "L'empreinte d'une même requête est stable")]
    [Trait("Spec", "CART-R18")]
    public void Fingerprint_is_deterministic()
    {
        Assert.Equal(CartRequestFingerprint.Add(1, 2), CartRequestFingerprint.Add(1, 2));
        Assert.Equal(64, CartRequestFingerprint.Add(1, 2).Length);
    }

    [Fact(DisplayName = "Toute différence de commande, de produit, de ligne, de quantité ou de version change l'empreinte")]
    [Trait("Spec", "CART-R18")]
    public void Fingerprint_distinguishes_requests()
    {
        string[] fingerprints =
        [
            CartRequestFingerprint.Add(1, 2),
            CartRequestFingerprint.Add(2, 2),
            CartRequestFingerprint.Add(1, 3),
            CartRequestFingerprint.Change(Line, 2, 1),
            CartRequestFingerprint.Change(Line, 2, 2),
            CartRequestFingerprint.Change(Line, 3, 1),
            CartRequestFingerprint.Remove(Line, 1),
            CartRequestFingerprint.Remove(Line, 2),
        ];

        Assert.Equal(fingerprints.Length, fingerprints.Distinct().Count());
    }

    [Theory(DisplayName = "Une clé d'idempotence au bon format est acceptée")]
    [InlineData("ajout-1")]
    [InlineData("0193a2b4-0000-7000-8000-000000000001")]
    [InlineData("commande:42.v1_a")]
    [Trait("Spec", "CART-R18")]
    public void Valid_keys_are_accepted(string key) => Assert.True(IdempotencyKeyFormat.IsValid(key));

    [Theory(DisplayName = "Une clé d'idempotence vide, trop longue ou avec des caractères interdits est refusée")]
    [InlineData("")]
    [InlineData("clé invalide !")]
    [InlineData("a/b")]
    [InlineData("ajout\n")]
    [Trait("Spec", "CART-R18")]
    public void Invalid_keys_are_rejected(string key) => Assert.False(IdempotencyKeyFormat.IsValid(key));

    [Fact(DisplayName = "Une clé de 100 caractères est acceptée, 101 sont refusés")]
    [Trait("Spec", "CART-R18")]
    public void Key_length_limit() =>
        Assert.Equal((true, false), (IdempotencyKeyFormat.IsValid(new string('k', 100)), IdempotencyKeyFormat.IsValid(new string('k', 101))));
}
