using Shop.Domain.Common;

namespace Shop.UnitTests.Domain.Common;

public sealed class ResultTests
{
    [Fact(DisplayName = "Un succès porte sa valeur et aucune erreur")]
    [Trait("Spec", "PLT-3")]
    public void Success_carries_value()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(42, result.Value);
    }

    [Fact(DisplayName = "Un échec porte son erreur et interdit l'accès à la valeur")]
    [Trait("Spec", "PLT-3")]
    public void Failure_carries_error()
    {
        Result<int> result = new DomainError(ErrorCodes.CartNotFound, "Pas de panier.");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.CartNotFound, result.Error.Code);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
