using System.Reflection;
using Shop.Api.Errors;
using Shop.Domain.Common;

namespace Shop.UnitTests.Api;

public sealed class ProblemMappingTests
{
    public static TheoryData<string> AllErrorCodes()
    {
        var data = new TheoryData<string>();
        foreach (var field in typeof(ErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            data.Add((string)field.GetRawConstantValue()!);
        }

        return data;
    }

    [Theory(DisplayName = "Chaque code d'erreur du domaine a un statut HTTP et un titre en français")]
    [MemberData(nameof(AllErrorCodes))]
    [Trait("Spec", "PLT-3")]
    public void Every_error_code_is_mapped(string code)
    {
        var (status, title) = ProblemMapping.Describe(code);

        Assert.InRange(status, 400, 599);
        Assert.False(string.IsNullOrWhiteSpace(title));
    }

    [Theory(DisplayName = "Les codes suivent la table du contrat d'erreurs")]
    [InlineData(ErrorCodes.ValidationFailed, 400)]
    [InlineData(ErrorCodes.UserNotIdentified, 401)]
    [InlineData(ErrorCodes.AdminKeyInvalid, 403)]
    [InlineData(ErrorCodes.CartNotFound, 404)]
    [InlineData(ErrorCodes.CartLineNotFound, 404)]
    [InlineData(ErrorCodes.CartLineVersionConflict, 409)]
    [InlineData(ErrorCodes.SyncAlreadyRunning, 409)]
    [InlineData(ErrorCodes.ProductUnavailable, 422)]
    [InlineData(ErrorCodes.QuantityLimitExceeded, 422)]
    [InlineData(ErrorCodes.SyncGuardTriggered, 422)]
    [InlineData(ErrorCodes.InternalError, 500)]
    [InlineData(ErrorCodes.ProviderUnavailable, 502)]
    [InlineData(ErrorCodes.ServiceUnavailable, 503)]
    [Trait("Spec", "PLT-3")]
    public void Codes_follow_the_error_contract(string code, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ProblemMapping.Describe(code).Status);
    }

    [Theory(DisplayName = "Une erreur produite hors du code métier reçoit un code cohérent avec son statut")]
    [InlineData(400, ErrorCodes.ValidationFailed)]
    [InlineData(401, ErrorCodes.UserNotIdentified)]
    [InlineData(404, ErrorCodes.NotFound)]
    [InlineData(405, ErrorCodes.MethodNotAllowed)]
    [InlineData(415, ErrorCodes.ValidationFailed)]
    [InlineData(500, ErrorCodes.InternalError)]
    [InlineData(503, ErrorCodes.ServiceUnavailable)]
    [Trait("Spec", "PLT-3")]
    public void Default_code_matches_status(int status, string expectedCode)
    {
        Assert.Equal(expectedCode, ProblemMapping.DefaultCodeFor(status));
    }

    [Fact(DisplayName = "Une erreur métier devient un ProblemDetails avec son code et ses détails en extensions")]
    [Trait("Spec", "PLT-3")]
    public void Domain_error_becomes_problem_with_extensions()
    {
        var error = new DomainError(
            ErrorCodes.CartLineVersionConflict,
            "La ligne a été modifiée.",
            new Dictionary<string, object?> { ["currentVersion"] = 2L });

        var problem = error.ToProblem().ProblemDetails;

        Assert.Equal(409, problem.Status);
        Assert.Equal("https://errors.shop.local/cart_line_version_conflict", problem.Type);
        Assert.Equal("La ligne a été modifiée.", problem.Detail);
        Assert.Equal(ErrorCodes.CartLineVersionConflict, problem.Extensions["code"]);
        Assert.Equal(2L, problem.Extensions["currentVersion"]);
    }
}
