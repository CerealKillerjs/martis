using Martis.Domain.Chemistry;

namespace Martis.Domain.Environment;

/// <summary>
/// Matrix A of TDD §10.1 stores dimensionless atom counts per species (ADR-011, ADR-012).
/// A molar mass is not stored: it is derived from A and the atomic masses,
/// <c>M_j = Σ_i A_ij · m_i</c>, with <c>m_i</c> in kg/mol.
/// </summary>
public static class ElementalCompositionMatrix
{
    /// <summary>Molar mass in kg/mol from atomic weights given in g/mol (IUPAC tables publish g/mol).</summary>
    public static double FormulaWeightKgPerMol(ChemicalFormula formula, IReadOnlyDictionary<string, double> atomicWeightsGPerMol)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(atomicWeightsGPerMol);
        var grams = 0.0;
        foreach (var pair in formula.Counts)
        {
            if (!atomicWeightsGPerMol.TryGetValue(pair.Key, out var weight))
            {
                throw new KeyNotFoundException($"Element '{pair.Key}' has no atomic weight.");
            }

            grams += pair.Value * weight;
        }

        return grams * 1e-3;
    }

    /// <summary>
    /// Mass fraction of each element in the species, ordered as <paramref name="elements"/>.
    /// The fractions sum to 1 within rounding.
    /// </summary>
    public static double[] MassFractions(
        ChemicalFormula formula,
        IReadOnlyList<string> elements,
        IReadOnlyDictionary<string, double> atomicWeightsGPerMol)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var molar = FormulaWeightKgPerMol(formula, atomicWeightsGPerMol) * 1e3;
        var fractions = new double[elements.Count];
        for (var i = 0; i < elements.Count; i++)
        {
            var count = formula.CountOf(elements[i]);
            fractions[i] = count == 0 ? 0.0 : count * atomicWeightsGPerMol[elements[i]] / molar;
        }

        return fractions;
    }
}
