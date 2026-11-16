using Martis.Domain.Common;

namespace Martis.Domain.Chemistry;

/// <summary>One species of a reaction side, with its coefficient in moles per unit extent.</summary>
public sealed record StoichiometricTerm
{
    public StoichiometricTerm(string speciesId, decimal moles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesId);
        if (moles <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(moles), moles, "A stoichiometric coefficient must be positive.");
        }

        SpeciesId = speciesId;
        Moles = moles;
    }

    public string SpeciesId { get; }

    /// <summary>Moles per unit of reaction extent. Exact decimal, so the balance check is exact.</summary>
    public decimal Moles { get; }
}

/// <summary>How a reaction meets the energy account of the population that runs it (ADR-012).</summary>
public enum ReactionEnergyMode
{
    /// <summary>No energy enters or leaves the population reserve.</summary>
    None,

    /// <summary>Light supplies the energy. The source is recorded; the reserve is not drawn.</summary>
    Light,

    /// <summary>The reaction releases energy into the population reserve.</summary>
    Release,

    /// <summary>The reaction draws energy from the population reserve.</summary>
    Cost,
}

/// <summary>
/// Reaction of TDD §3.4 with coefficients per unit extent. Inputs and outputs
/// are listed in species-id order. <see cref="CarbonMoles"/> is the moles of carbon
/// processed per unit extent; rates and energies are expressed per mole of carbon.
/// </summary>
public sealed record ReactionDefinition
{
    public ReactionDefinition(
        ReactionId id,
        string requiredCapability,
        IEnumerable<StoichiometricTerm> inputs,
        IEnumerable<StoichiometricTerm> outputs,
        decimal carbonMoles,
        ReactionEnergyMode energyMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredCapability);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        if (carbonMoles <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(carbonMoles), carbonMoles, "Carbon moles per extent must be positive.");
        }

        Id = id;
        RequiredCapability = requiredCapability;
        Inputs = Sorted(inputs, nameof(inputs));
        Outputs = Sorted(outputs, nameof(outputs));
        if (Inputs.Count == 0 || Outputs.Count == 0)
        {
            throw new ArgumentException($"Reaction '{id.Value}' needs at least one input and one output.");
        }

        CarbonMoles = carbonMoles;
        EnergyMode = energyMode;
    }

    public ReactionId Id { get; }

    public string RequiredCapability { get; }

    public IReadOnlyList<StoichiometricTerm> Inputs { get; }

    public IReadOnlyList<StoichiometricTerm> Outputs { get; }

    public decimal CarbonMoles { get; }

    public ReactionEnergyMode EnergyMode { get; }

    private static StoichiometricTerm[] Sorted(IEnumerable<StoichiometricTerm> terms, string name)
    {
        var array = terms.ToArray();
        Array.Sort(array, static (a, b) => string.CompareOrdinal(a.SpeciesId, b.SpeciesId));
        for (var i = 1; i < array.Length; i++)
        {
            if (string.Equals(array[i - 1].SpeciesId, array[i].SpeciesId, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Species '{array[i].SpeciesId}' appears twice on one side.", name);
            }
        }

        return array;
    }
}
