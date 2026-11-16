using Shop.Api.Auth;

namespace Shop.UnitTests.Api;

public sealed class IdentificationTests
{
    [Theory(DisplayName = "Un identifiant utilisateur au bon format est accepté")]
    [InlineData("alice")]
    [InlineData("Alice")]
    [InlineData("alice.dupont-01@exemple")]
    [InlineData("a_b")]
    [InlineData("1")]
    [Trait("Spec", "PLT-2")]
    public void Valid_user_ids_are_accepted(string userId)
    {
        Assert.True(UserIdFormat.IsValid(userId));
    }

    [Theory(DisplayName = "Un identifiant utilisateur vide, trop long ou avec des caractères interdits est refusé")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("alice dupont")]
    [InlineData("alice/../bob")]
    [InlineData("élodie")]
    [InlineData("alice\n")]
    [Trait("Spec", "PLT-2")]
    public void Invalid_user_ids_are_rejected(string? userId)
    {
        Assert.False(UserIdFormat.IsValid(userId));
    }

    [Fact(DisplayName = "Un identifiant de 64 caractères est accepté, 65 caractères sont refusés")]
    [Trait("Spec", "PLT-2")]
    public void Length_limit_is_64()
    {
        Assert.True(UserIdFormat.IsValid(new string('a', 64)));
        Assert.False(UserIdFormat.IsValid(new string('a', 65)));
    }

    [Theory(DisplayName = "La clé d'administration n'est acceptée que si elle est configurée et identique")]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "Secret", false)]
    [InlineData("secret", "secret ", false)]
    [InlineData("secret", "", false)]
    [InlineData("secret", null, false)]
    [InlineData("", "", false)]
    [InlineData(null, "secret", false)]
    [Trait("Spec", "CAT-R3")]
    public void Admin_key_must_match_configuration(string? configured, string? provided, bool expected)
    {
        Assert.Equal(expected, AdminKey.Matches(configured, provided));
    }
}
