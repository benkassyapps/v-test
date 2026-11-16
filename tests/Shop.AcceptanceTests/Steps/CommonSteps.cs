using Reqnroll;
using Shop.AcceptanceTests.Support;

namespace Shop.AcceptanceTests.Steps;

/// <summary>Étapes communes à toutes les fonctionnalités : instances et vérification des réponses.</summary>
[Binding]
public sealed class CommonSteps(ScenarioHttp http)
{
    [Given("le service fonctionne avec {int} instances")]
    public static void ServiceRunsWithInstances(int count) =>
        Assert.True(count == 2, "Le cluster de test fournit exactement 2 instances.");

    [Then("la réponse a le statut {int}")]
    public void ResponseHasStatus(int status) =>
        Assert.True(
            (int)http.Last.StatusCode == status,
            $"Statut attendu {status}, reçu {(int)http.Last.StatusCode} : {http.LastBody}");

    [Then("la réponse est une erreur {int} de code {string}")]
    public void ResponseIsError(int status, string code)
    {
        ResponseHasStatus(status);
        Assert.Equal("application/problem+json", http.Last.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, http.LastJson().GetProperty("code").GetString());
    }
}
