using System.Text.Json;
using System.Text.RegularExpressions;
using Shop.UnitTests.Traceability;

namespace Shop.UnitTests.Security;

/// <summary>
/// PLT-8 : aucun secret dans les fichiers de configuration versionnés.
/// Les valeurs sensibles viennent des variables d'environnement (.env, ignoré par git) ou des user-secrets.
/// </summary>
public sealed partial class NoSecretsInRepositoryTests
{
    [Fact(DisplayName = "Les fichiers appsettings ne contiennent aucune valeur sensible renseignée")]
    [Trait("Spec", "PLT-8")]
    public void Appsettings_contain_no_secret()
    {
        var findings = Directory
            .EnumerateFiles(Path.Combine(RepositoryFiles.Root.FullName, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => FindSecrets(path))
            .ToList();

        Assert.True(findings.Count == 0, "Valeurs sensibles trouvées : " + string.Join(" ; ", findings));
    }

    [Fact(DisplayName = "docker-compose ne contient aucun mot de passe ou clé en clair")]
    [Trait("Spec", "PLT-8")]
    public void Compose_file_uses_environment_variables_only()
    {
        var composePath = Path.Combine(RepositoryFiles.Root.FullName, "docker-compose.yml");
        if (!File.Exists(composePath))
        {
            return; // Créé au bloc 5 (T5.1) ; le contrôle s'applique dès qu'il existe.
        }

        var findings = FindPlainSecrets(File.ReadAllLines(composePath));

        Assert.True(findings.Count == 0, "Valeurs en clair aux lignes de docker-compose.yml : " + string.Join(", ", findings));
    }

    [Theory(DisplayName = "La détection des secrets en clair repère les mots de passe et ignore les variables")]
    [InlineData("      MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD}", false)]
    [InlineData("      MSSQL_SA_PASSWORD: \"${MSSQL_SA_PASSWORD}\"", false)]
    [InlineData("      MSSQL_SA_PASSWORD: P@ssw0rd!", true)]
    [InlineData("      password: secret", true)]
    [InlineData("      Admin__ApiKey: ${ADMIN_API_KEY}", false)]
    [InlineData("      Admin__ApiKey: ma-cle", true)]
    [InlineData("      ConnectionStrings__Shop: \"Server=sql;User Id=sa;Password=P@ssw0rd\"", true)]
    [InlineData("      ConnectionStrings__Shop: \"Server=sql;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=true\"", false)]
    [InlineData("      - \"8080:8080\"", false)]
    [Trait("Spec", "PLT-8")]
    public void Plain_secret_detection(string line, bool expectedFinding)
    {
        Assert.Equal(expectedFinding, FindPlainSecrets([line]).Count == 1);
    }

    /// <summary>Numéros des lignes qui contiennent un mot de passe, une clé ou un secret en clair (pas une variable ${…}).</summary>
    private static List<int> FindPlainSecrets(IEnumerable<string> lines) =>
        lines
            .Select((line, index) => (line, number: index + 1))
            .Where(entry =>
                InlinePassword().Matches(entry.line).Any(match => !IsVariable(match.Groups["value"].Value))
                || (SensitiveKeyAssignment().Match(entry.line) is { Success: true } assignment
                    && !IsVariable(assignment.Groups["value"].Value)))
            .Select(entry => entry.number)
            .ToList();

    private static bool IsVariable(string value)
    {
        var trimmed = value.Trim().Trim('"', '\'');
        return trimmed.Length == 0 || trimmed.StartsWith("${", StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Le fichier .env local est ignoré par git")]
    [Trait("Spec", "PLT-8")]
    public void Env_file_is_git_ignored()
    {
        var gitignore = File.ReadAllLines(Path.Combine(RepositoryFiles.Root.FullName, ".gitignore"));

        Assert.Contains(".env", gitignore.Select(line => line.Trim()));
    }

    private static List<string> FindSecrets(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var relative = Path.GetRelativePath(RepositoryFiles.Root.FullName, path);
        return Walk(document.RootElement, string.Empty)
            .Where(property => IsSensitive(property.Path, property.Value))
            .Select(property => $"{relative} → {property.Path}")
            .ToList(); // Matérialisé avant la libération du JsonDocument.
    }

    private static IEnumerable<(string Path, string Value)> Walk(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var child in Walk(property.Value, path.Length == 0 ? property.Name : $"{path}:{property.Name}"))
                    {
                        yield return child;
                    }
                }

                break;
            case JsonValueKind.String:
                yield return (path, element.GetString() ?? string.Empty);
                break;
        }
    }

    private static bool IsSensitive(string path, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return SensitiveKey().IsMatch(path.Split(':')[^1]) || ConnectionStringPassword().IsMatch(value);
    }

    [GeneratedRegex(@"(password|pwd|secret|apikey|token)", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKey();

    [GeneratedRegex(@"(password|pwd)\s*=\s*[^;\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPassword();

    /// <summary>« Password=valeur » dans une chaîne de connexion.</summary>
    [GeneratedRegex(@"\b(password|pwd)\s*=\s*(?<value>[^;""'\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex InlinePassword();

    /// <summary>Clé YAML ou variable d'environnement sensible : « MSSQL_SA_PASSWORD: valeur », « Admin__ApiKey: valeur ».</summary>
    [GeneratedRegex(@"^\s*-?\s*[\w.]*(password|api_?key|secret|token)[\w.]*\s*[:=]\s*(?<value>.*?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKeyAssignment();
}
