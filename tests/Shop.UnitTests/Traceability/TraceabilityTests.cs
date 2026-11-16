using System.Text.RegularExpressions;

namespace Shop.UnitTests.Traceability;

/// <summary>
/// Vérifie de façon déterministe que chaque exigence des specs est couverte
/// par au moins un scénario Gherkin (tag @ID) ou un test xUnit (Trait("Spec", "ID")).
/// </summary>
public sealed partial class TraceabilityTests
{
    /// <summary>Exigences vérifiées autrement que par un test automatisé (justification dans specs/traceability.md).</summary>
    private static readonly string[] Exemptions =
    [
        "PLT-4", // Démarrage reproductible avec docker compose : vérification manuelle (tâche T5.2).
    ];

    [Fact(DisplayName = "Chaque exigence est couverte par au moins un scénario ou un test")]
    public void Every_requirement_is_covered()
    {
        var report = AnalyzeRepository();

        Assert.True(
            report.Uncovered.Count == 0,
            $"Exigences sans scénario ni test : {string.Join(", ", report.Uncovered)}");
    }

    [Fact(DisplayName = "Aucun tag ni trait ne référence une exigence inconnue")]
    public void No_reference_to_unknown_requirement()
    {
        var report = AnalyzeRepository();

        Assert.True(
            report.UnknownReferences.Count == 0,
            $"Références à des exigences inexistantes : {string.Join(", ", report.UnknownReferences)}");
        Assert.True(
            report.UnknownExemptions.Count == 0,
            $"Exemptions qui ne correspondent à aucune exigence : {string.Join(", ", report.UnknownExemptions)}");
    }

    [Fact(
        DisplayName = "Tous les fichiers .feature des specs sont exécutés par le projet d'acceptation",
        Skip = "Activé au bloc 6 (T6.4) : les .feature sont inclus progressivement, bloc par bloc (specs/platform/tasks.md).")]
    public void Every_feature_file_is_executed()
    {
        var csprojPath = Path.Combine(RepositoryFiles.Root.FullName, "tests", "Shop.AcceptanceTests", "Shop.AcceptanceTests.csproj");
        var csprojDirectory = Path.GetDirectoryName(csprojPath)!;
        var included = IncludedFeature()
            .Matches(File.ReadAllText(csprojPath))
            .Select(m => Path.GetFullPath(Path.Combine(csprojDirectory, m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = RepositoryFiles.FeatureFiles()
            .Where(file => !included.Contains(file.FullName))
            .Select(RepositoryFiles.RelativePath)
            .ToList();

        Assert.True(missing.Count == 0, $"Fichiers .feature non exécutés : {string.Join(", ", missing)}");
    }

    private static TraceabilityReport AnalyzeRepository()
    {
        var declared = RepositoryFiles.RequirementFiles()
            .SelectMany(file => TraceabilityAnalyzer.DeclaredRequirements(File.ReadAllText(file.FullName)))
            .ToList();

        Assert.NotEmpty(declared);

        var fromFeatures = RepositoryFiles.FeatureFiles()
            .SelectMany(file => TraceabilityAnalyzer.FeatureReferences(File.ReadAllText(file.FullName)));
        var fromTests = RepositoryFiles.TestSourceFiles()
            .SelectMany(file => TraceabilityAnalyzer.TraitReferences(File.ReadAllText(file.FullName)));

        return TraceabilityAnalyzer.Analyze(declared, fromFeatures.Concat(fromTests), Exemptions);
    }

    [GeneratedRegex(@"Include=""([^""]+\.feature)""")]
    private static partial Regex IncludedFeature();
}
