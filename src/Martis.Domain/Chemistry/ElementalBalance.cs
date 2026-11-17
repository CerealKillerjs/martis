namespace Martis.Domain.Chemistry;

/// <summary>
/// Exact element balance of a reaction (TDD §10.1, VAL-007): for each element e,
/// Σ outputs n·A_e − Σ inputs n·A_e = 0, computed in <see cref="decimal"/> so the check
/// has no rounding. This is A·S = 0 for one column of S.
/// </summary>
public static class ElementalBalance
{
    /// <summary>Element symbols with a non-zero residual, with that residual (moles of atoms per extent).</summary>
    public static IReadOnlyList<KeyValuePair<string, decimal>> Residuals(
        ReactionDefinition reaction,
        IReadOnlyDictionary<string, ChemicalFormula> formulas)
    {
        ArgumentNullException.ThrowIfNull(reaction);
        ArgumentNullException.ThrowIfNull(formulas);
        var totals = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
        Add(reaction.Outputs, formulas, totals, 1m);
        Add(reaction.Inputs, formulas, totals, -1m);
        return totals.Where(pair => pair.Value != 0m).ToArray();
    }

    public static bool IsBalanced(ReactionDefinition reaction, IReadOnlyDictionary<string, ChemicalFormula> formulas)
        => Residuals(reaction, formulas).Count == 0;

    private static void Add(
        IReadOnlyList<StoichiometricTerm> terms,
        IReadOnlyDictionary<string, ChemicalFormula> formulas,
        SortedDictionary<string, decimal> totals,
        decimal sign)
    {
        foreach (var term in terms)
        {
            if (!formulas.TryGetValue(term.SpeciesId, out var formula))
            {
                throw new KeyNotFoundException($"Species '{term.SpeciesId}' has no formula.");
            }

            foreach (var pair in formula.Counts)
            {
                totals[pair.Key] = (totals.TryGetValue(pair.Key, out var current) ? current : 0m) + (sign * term.Moles * pair.Value);
            }
        }
    }
}
