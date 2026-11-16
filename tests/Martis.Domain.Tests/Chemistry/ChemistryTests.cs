using Martis.Domain.Chemistry;
using Martis.Domain.Common;
using Martis.Domain.Environment;

namespace Martis.Domain.Tests.Chemistry;

public sealed class ChemistryTests
{
    private static readonly Dictionary<string, double> Weights = new(StringComparer.Ordinal)
    {
        // Illustrative test weights in g/mol. The published table lives in data/reactions.
        ["H"] = 1.0, ["C"] = 12.0, ["O"] = 16.0, ["N"] = 14.0, ["P"] = 31.0,
    };

    [Theory]
    [InlineData("H2O", "H", 2)]
    [InlineData("H2O", "O", 1)]
    [InlineData("C106H263O110N16P", "C", 106)]
    [InlineData("C106H263O110N16P", "P", 1)]
    [InlineData("Fe", "Fe", 1)]
    [InlineData("CH2O", "N", 0)]
    public void FormulaCountsAtoms(string text, string element, int expected)
        => Assert.Equal(expected, ChemicalFormula.Parse(text).CountOf(element));

    [Theory]
    [InlineData("h2o")]
    [InlineData("H0")]
    [InlineData("H2O+")]
    [InlineData("(CH2O)6")]
    public void FormulaRejectsUnsupportedText(string text)
        => Assert.Throws<FormatException>(() => ChemicalFormula.Parse(text));

    [Fact]
    public void RepeatedSymbolsAreSummed()
        => Assert.True(ChemicalFormula.Parse("CH3COOH").SameCompositionAs(ChemicalFormula.Parse("C2H4O2")));

    [Fact]
    public void MolarMassUsesKilogramsPerMole()
        => Assert.Equal(0.018, ElementalCompositionMatrix.FormulaWeightKgPerMol(ChemicalFormula.Parse("H2O"), Weights), 12);

    [Fact]
    public void MassFractionsSumToOne()
    {
        var fractions = ElementalCompositionMatrix.MassFractions(ChemicalFormula.Parse("C106H263O110N16P"), ["C", "H", "N", "O", "P"], Weights);
        Assert.Equal(1.0, fractions.Sum(), 12);
    }

    [Fact]
    public void PhotosynthesisIsBalanced()
    {
        var reaction = new ReactionDefinition(
            new ReactionId("photo"),
            "photoautotrophy",
            [new StoichiometricTerm("CO2", 1m), new StoichiometricTerm("H2O", 1m)],
            [new StoichiometricTerm("CH2O", 1m), new StoichiometricTerm("O2", 1m)],
            1m,
            ReactionEnergyMode.Light);

        Assert.True(ElementalBalance.IsBalanced(reaction, Formulas("CO2", "H2O", "CH2O", "O2")));
    }

    [Fact]
    public void AnUnbalancedReactionReportsTheElement()
    {
        var reaction = new ReactionDefinition(
            new ReactionId("broken"),
            "none",
            [new StoichiometricTerm("CO2", 1m)],
            [new StoichiometricTerm("CH2O", 1m)],
            1m,
            ReactionEnergyMode.None);

        var residuals = ElementalBalance.Residuals(reaction, Formulas("CO2", "CH2O"));

        Assert.Contains(residuals, pair => pair.Key == "H" && pair.Value == 2m);
        Assert.Contains(residuals, pair => pair.Key == "O" && pair.Value == -1m);
    }

    [Fact]
    public void ReactionRejectsDuplicateSpeciesAndNonPositiveCoefficients()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StoichiometricTerm("CO2", 0m));
        Assert.Throws<ArgumentException>(() => new ReactionDefinition(
            new ReactionId("dup"),
            "none",
            [new StoichiometricTerm("CO2", 1m), new StoichiometricTerm("CO2", 2m)],
            [new StoichiometricTerm("O2", 1m)],
            1m,
            ReactionEnergyMode.None));
    }

    private static Dictionary<string, ChemicalFormula> Formulas(params string[] texts)
        => texts.ToDictionary(text => text, ChemicalFormula.Parse, StringComparer.Ordinal);
}
