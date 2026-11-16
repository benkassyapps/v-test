namespace Shop.UnitTests.Traceability;

/// <summary>Tests de l'analyseur sur des exemples, dont les cas négatifs.</summary>
public sealed class TraceabilityAnalyzerTests
{
    private const string Requirements = """
        # Exigences
        ### CART-R1 — Un seul panier
        ### CART-R2 — Panier personnel
        ### CART-R10 — Pas de réapparition
        ### PLT-4 — Démarrage
        Texte qui cite CART-R99 sans être un titre.
        """;

    [Fact(DisplayName = "Les identifiants sont lus uniquement dans les titres de niveau 3")]
    public void Declared_requirements_come_from_level_3_headings()
    {
        var declared = TraceabilityAnalyzer.DeclaredRequirements(Requirements).ToList();

        Assert.Equal(["CART-R1", "CART-R2", "CART-R10", "PLT-4"], declared);
    }

    [Fact(DisplayName = "Les tags Gherkin sont lus, les commentaires et les adresses e-mail ignorés")]
    public void Feature_tags_are_read_and_comments_ignored()
    {
        const string feature = """
            # language: fr
            # @CART-R2 dans un commentaire ne compte pas
            @CART-R1 @PLT-4
            Scénario: exemple
              Quand une requête est envoyée avec "alice@PLT-9"
            """;

        var references = TraceabilityAnalyzer.FeatureReferences(feature).ToList();

        Assert.Equal(["CART-R1", "PLT-4"], references);
    }

    [Fact(DisplayName = "Les traits Spec sont lus dans le source C#")]
    public void Spec_traits_are_read()
    {
        const string source = "[Fact, Trait(\"Spec\", \"CART-R10\")] [Trait(\"Category\", \"Lent\")]";

        var references = TraceabilityAnalyzer.TraitReferences(source).ToList();

        Assert.Equal(["CART-R10"], references);
    }

    [Fact(DisplayName = "Une exigence sans référence est signalée comme non couverte")]
    public void Uncovered_requirement_is_reported()
    {
        var report = TraceabilityAnalyzer.Analyze(
            declared: ["CART-R1", "CART-R2", "CART-R10"],
            referenced: ["CART-R1"],
            exemptions: []);

        Assert.Equal(["CART-R2", "CART-R10"], report.Uncovered);
    }

    [Fact(DisplayName = "Une référence à une exigence inexistante est signalée")]
    public void Unknown_reference_is_reported()
    {
        var report = TraceabilityAnalyzer.Analyze(
            declared: ["CART-R1"],
            referenced: ["CART-R1", "CART-R42"],
            exemptions: []);

        Assert.Equal(["CART-R42"], report.UnknownReferences);
    }

    [Fact(DisplayName = "Une exigence exemptée n'est pas signalée, une exemption inconnue l'est")]
    public void Exemptions_are_honoured_and_checked()
    {
        var report = TraceabilityAnalyzer.Analyze(
            declared: ["PLT-4"],
            referenced: [],
            exemptions: ["PLT-4", "PLT-77"]);

        Assert.Empty(report.Uncovered);
        Assert.Equal(["PLT-77"], report.UnknownExemptions);
    }
}
