using System.Text.RegularExpressions;

namespace Shop.UnitTests.Traceability;

/// <summary>
/// Analyse la couverture des exigences : identifiants déclarés dans les requirements.md,
/// références dans les tags Gherkin (@ID) et dans les traits xUnit (Trait("Spec", "ID")).
/// Logique pure, alimentée par du texte, pour pouvoir être testée sur des exemples.
/// </summary>
internal static partial class TraceabilityAnalyzer
{
    private const string IdPattern = @"(?:CAT-R|CART-R|PLT-)\d+";

    [GeneratedRegex(@"^###\s+(" + IdPattern + @")\b", RegexOptions.Multiline)]
    private static partial Regex RequirementHeading();

    [GeneratedRegex(@"(?<![\w.-])@(" + IdPattern + @")\b")]
    private static partial Regex FeatureTag();

    [GeneratedRegex(@"Trait\(\s*""Spec""\s*,\s*""([^""]+)""\s*\)")]
    private static partial Regex SpecTrait();

    /// <summary>Identifiants déclarés par les titres « ### ID — … » d'un requirements.md.</summary>
    public static IEnumerable<string> DeclaredRequirements(string requirementsMarkdown) =>
        RequirementHeading().Matches(requirementsMarkdown).Select(m => m.Groups[1].Value);

    /// <summary>Identifiants cités par les tags d'un fichier .feature (lignes de commentaire ignorées).</summary>
    public static IEnumerable<string> FeatureReferences(string featureText) =>
        featureText
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#'))
            .SelectMany(line => FeatureTag().Matches(line).Select(m => m.Groups[1].Value));

    /// <summary>Identifiants cités par les attributs Trait("Spec", "…") d'un fichier source C#.</summary>
    public static IEnumerable<string> TraitReferences(string csharpSource) =>
        SpecTrait().Matches(csharpSource).Select(m => m.Groups[1].Value);

    public static TraceabilityReport Analyze(
        IEnumerable<string> declared,
        IEnumerable<string> referenced,
        IEnumerable<string> exemptions)
    {
        var declaredSet = declared.ToHashSet(StringComparer.Ordinal);
        var referencedSet = referenced.ToHashSet(StringComparer.Ordinal);
        var exemptionSet = exemptions.ToHashSet(StringComparer.Ordinal);

        var uncovered = declaredSet
            .Where(id => !referencedSet.Contains(id) && !exemptionSet.Contains(id))
            .Order(RequirementIdComparer.Instance)
            .ToList();

        var unknown = referencedSet
            .Where(id => !declaredSet.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToList();

        var unknownExemptions = exemptionSet
            .Where(id => !declaredSet.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToList();

        return new TraceabilityReport(uncovered, unknown, unknownExemptions);
    }
}

internal sealed record TraceabilityReport(
    IReadOnlyList<string> Uncovered,
    IReadOnlyList<string> UnknownReferences,
    IReadOnlyList<string> UnknownExemptions);

/// <summary>Tri naturel des identifiants : CART-R2 avant CART-R10.</summary>
internal sealed class RequirementIdComparer : IComparer<string>
{
    public static readonly RequirementIdComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.CompareOrdinal(x, y);
        }

        var (prefixX, numberX) = Split(x);
        var (prefixY, numberY) = Split(y);
        var byPrefix = string.CompareOrdinal(prefixX, prefixY);
        return byPrefix != 0 ? byPrefix : numberX.CompareTo(numberY);
    }

    private static (string Prefix, int Number) Split(string id)
    {
        var index = id.Length;
        while (index > 0 && char.IsAsciiDigit(id[index - 1]))
        {
            index--;
        }

        return index < id.Length && int.TryParse(id.AsSpan(index), out var number)
            ? (id[..index], number)
            : (id, 0);
    }
}
