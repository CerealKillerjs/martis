using Martis.Domain.World;
using Martis.Simulation.Snapshots;

namespace Martis.Simulation.Biology;

/// <summary>Planned and applied flux of one population during one tick.</summary>
public sealed class PopulationFlux
{
    public PopulationFlux(long populationId, ReactionModel reaction, double plannedExtent)
    {
        PopulationId = populationId;
        Reaction = reaction;
        PlannedExtent = plannedExtent;
    }

    public long PopulationId { get; }

    public ReactionModel Reaction { get; }

    /// <summary>Extent the population would run with unlimited shared resources.</summary>
    public double PlannedExtent { get; }

    /// <summary>Extent after proportional allocation (TDD §3.6). Never above <see cref="PlannedExtent"/>.</summary>
    public double AllocatedExtent { get; set; }
}

/// <summary>Per-tick totals published for histories and the mass balance file.</summary>
public sealed class TickTotals
{
    /// <summary>Biomass added by commands before this tick's account started (kg).</summary>
    public double ExternalInflowKg { get; set; }

    public double CarbonFixedMol { get; set; }

    public double CarbonRespiredMol { get; set; }

    public double LightEnergyJ { get; set; }

    public double ReleasedEnergyJ { get; set; }

    public double ReserveSpentJ { get; set; }

    public double BiomassDiedKg { get; set; }

    public double BiomassRecycledKg { get; set; }

    /// <summary>Largest per-cell, per-element mass error of this tick (kg).</summary>
    public double MaxElementErrorKg { get; set; }

    public int Births { get; set; }

    public int Extinctions { get; set; }

    public int Mutations { get; set; }

    public void Reset()
    {
        ExternalInflowKg = 0;
        CarbonFixedMol = 0;
        CarbonRespiredMol = 0;
        LightEnergyJ = 0;
        ReleasedEnergyJ = 0;
        ReserveSpentJ = 0;
        BiomassDiedKg = 0;
        BiomassRecycledKg = 0;
        MaxElementErrorKg = 0;
        Births = 0;
        Extinctions = 0;
        Mutations = 0;
    }
}

/// <summary>
/// Scratch shared by the biology systems inside one tick (ADR-012). It is reset at
/// the start of the resource-availability step and holds nothing a snapshot needs
/// between ticks: every lasting value is on <see cref="SimulationState"/>.
/// Arrays are indexed <c>cell * stride + column</c>.
/// </summary>
public sealed class MetabolismWorkspace
{
    private int _cells = -1;

    public MetabolismWorkspace(BiologyCatalog catalog)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public BiologyCatalog Catalog { get; }

    public int Slots => ResourceColumns.Names.Count;

    public int ElementCount => Catalog.Elements.Count;

    /// <summary>Pool masses at the start of the account (kg), cell-major.</summary>
    public double[] StartPool { get; private set; } = [];

    /// <summary>Element masses at the start of the account (kg), cell-major.</summary>
    public double[] StartElements { get; private set; } = [];

    /// <summary>Requested mass per pool slot (kg), cell-major.</summary>
    public double[] Demand { get; private set; } = [];

    /// <summary>Consumed mass per pool slot (kg), cell-major.</summary>
    public double[] Consumed { get; private set; } = [];

    /// <summary>Fluxes in population order. Reset each tick.</summary>
    public List<PopulationFlux> Fluxes { get; } = [];

    /// <summary>Energy reserve at the start of the tick, by population id.</summary>
    public Dictionary<long, double> ReserveAtStart { get; } = [];

    /// <summary>Energy added to each reserve this tick, by population id.</summary>
    public Dictionary<long, double> ReserveGained { get; } = [];

    /// <summary>Energy drawn from each reserve this tick, by population id.</summary>
    public Dictionary<long, double> ReserveSpent { get; } = [];

    /// <summary>Inoculated biomass waiting for the next account (kg). Set by the command handler.</summary>
    public double PendingInflowKg { get; set; }

    public TickTotals Totals { get; } = new();

    /// <summary>True once the availability step has opened this tick's account.</summary>
    public bool AccountOpen { get; private set; }

    public void Open(SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_cells != state.CellCount)
        {
            _cells = state.CellCount;
            StartPool = new double[_cells * Slots];
            StartElements = new double[_cells * ElementCount];
            Demand = new double[_cells * Slots];
            Consumed = new double[_cells * Slots];
        }
        else
        {
            Array.Clear(Demand);
            Array.Clear(Consumed);
        }

        Fluxes.Clear();
        ReserveAtStart.Clear();
        ReserveGained.Clear();
        ReserveSpent.Clear();
        Totals.Reset();
        Totals.ExternalInflowKg = PendingInflowKg;
        PendingInflowKg = 0;

        for (var cell = 0; cell < _cells; cell++)
        {
            var quantities = state.Resources[cell].Quantities;
            for (var slot = 0; slot < Slots; slot++)
            {
                StartPool[(cell * Slots) + slot] = quantities[slot].Value;
            }
        }

        ElementTotals(state, StartElements);
        foreach (var population in state.Populations)
        {
            ReserveAtStart[population.Id.Value] = population.EnergyReserveJ;
            ReserveGained[population.Id.Value] = 0;
            ReserveSpent[population.Id.Value] = 0;
        }

        AccountOpen = true;
    }

    public void Close() => AccountOpen = false;

    public void Gain(long populationId, double joules)
        => ReserveGained[populationId] = (ReserveGained.TryGetValue(populationId, out var current) ? current : 0) + joules;

    public void Spend(long populationId, double joules)
    {
        ReserveSpent[populationId] = (ReserveSpent.TryGetValue(populationId, out var current) ? current : 0) + joules;
        Totals.ReserveSpentJ += joules;
    }

    /// <summary>
    /// Element masses (kg) per cell into <paramref name="target"/>: pool species plus the
    /// biomass of the populations in the cell. Cells ascending, populations in list order.
    /// </summary>
    public void ElementTotals(SimulationState state, double[] target)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(target);
        Array.Clear(target);
        var elements = ElementCount;
        for (var cell = 0; cell < state.CellCount; cell++)
        {
            var quantities = state.Resources[cell].Quantities;
            for (var slot = 0; slot < Slots; slot++)
            {
                var fractions = Catalog.PoolSpecies(slot).ElementMassFractions;
                var mass = quantities[slot].Value;
                for (var e = 0; e < elements; e++)
                {
                    target[(cell * elements) + e] += mass * fractions[e];
                }
            }
        }

        var biomass = Catalog.Biomass.ElementMassFractions;
        foreach (var population in state.Populations)
        {
            if (population.CellIndex < 0 || population.CellIndex >= state.CellCount)
            {
                continue;
            }

            for (var e = 0; e < elements; e++)
            {
                target[(population.CellIndex * elements) + e] += population.BiomassKg * biomass[e];
            }
        }
    }
}
