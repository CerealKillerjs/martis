using Martis.Contracts.Commands;
using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.Genetics;
using Martis.Domain.World;
using Martis.Simulation.Random;

namespace Martis.Simulation.Engine;

/// <summary>
/// Deep copy of one world, its PRNG and the command queue, taken before a tick.
/// Restoring it undoes every write the tick made, including a tick counter a
/// system changed and random numbers it drew (R6-04).
/// </summary>
internal sealed class StateCheckpoint
{
    private readonly long _tick;
    private readonly long _nextPopulationId;
    private readonly CellCopy[] _cells;
    private readonly PopulationCopy[] _populations;
    private readonly RngStreamState _random;
    private readonly ISimulationCommand[] _commands;

    private StateCheckpoint(
        long tick,
        CellCopy[] cells,
        PopulationCopy[] populations,
        RngStreamState random,
        ISimulationCommand[] commands,
        long nextPopulationId)
    {
        _tick = tick;
        _nextPopulationId = nextPopulationId;
        _cells = cells;
        _populations = populations;
        _random = random;
        _commands = commands;
    }

    public static StateCheckpoint Capture(SimulationState state, RngStreams random, Queue<ISimulationCommand> commands)
    {
        var cells = new CellCopy[state.CellCount];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = CellCopy.Capture(state.Environment[i], state.Resources[i], state.AllocationBuffers[i], state.WaterBudgets[i], state.MassLedgers[i]);
        }

        var populations = new PopulationCopy[state.Populations.Count];
        for (var i = 0; i < populations.Length; i++)
        {
            populations[i] = PopulationCopy.Capture(state.Populations[i]);
        }

        return new StateCheckpoint(state.Tick, cells, populations, random.ExportState(), commands.ToArray(), state.NextPopulationId);
    }

    public void Restore(SimulationState state, RngStreams random, Queue<ISimulationCommand> commands)
    {
        if (state.CellCount != _cells.Length)
        {
            throw new InvalidOperationException("The checkpoint cell count does not match the world.");
        }

        state.Tick = _tick;
        state.NextPopulationId = _nextPopulationId;
        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i].Restore(state.Environment[i], state.Resources[i], state.AllocationBuffers[i], state.WaterBudgets[i], state.MassLedgers[i]);
        }

        state.Populations.Clear();
        foreach (var population in _populations)
        {
            state.Populations.Add(population.Restore());
        }

        random.Restore(_random);
        commands.Clear();
        foreach (var command in _commands)
        {
            commands.Enqueue(command);
        }
    }

    private readonly struct CellCopy
    {
        private readonly AssignedDouble _temperature;
        private readonly AssignedDouble _pressure;
        private readonly AssignedDouble _ultraviolet;
        private readonly double _absorbedDose;
        private readonly double _equivalentDose;
        private readonly double _irradiance;
        private readonly double _waterIce;
        private readonly AssignedDouble _vapor;
        private readonly AssignedDouble _porosity;
        private readonly AssignedDouble _perchlorateMass;
        private readonly AssignedDouble _perchloratePercent;
        private readonly AssignedDouble _icePercent;
        private readonly EnvironmentScope? _scope;
        private readonly double _carbonDioxide;
        private readonly double _water;
        private readonly double _oxygen;
        private readonly double _nitrogen;
        private readonly double _phosphorus;
        private readonly double _sulfur;
        private readonly double _iron;
        private readonly double _organicCarbon;
        private readonly double _mineralCarbon;
        private readonly double _demandCarbonDioxide;
        private readonly double _demandWater;
        private readonly double _demandOxygen;
        private readonly double _demandNitrogen;
        private readonly double _demandPhosphorus;
        private readonly double _demandSulfur;
        private readonly double _demandIron;
        private readonly double _demandOrganicCarbon;
        private readonly double _demandMineralCarbon;
        private readonly double _waterTotal;
        private readonly double[]? _ledger;

        private CellCopy(
            AssignedDouble temperature,
            AssignedDouble pressure,
            AssignedDouble ultraviolet,
            double absorbedDose,
            double equivalentDose,
            double irradiance,
            double waterIce,
            AssignedDouble vapor,
            AssignedDouble porosity,
            AssignedDouble perchlorateMass,
            AssignedDouble perchloratePercent,
            AssignedDouble icePercent,
            EnvironmentScope? scope,
            double carbonDioxide,
            double water,
            double oxygen,
            double nitrogen,
            double phosphorus,
            double sulfur,
            double iron,
            double organicCarbon,
            double mineralCarbon,
            double demandCarbonDioxide,
            double demandWater,
            double demandOxygen,
            double demandNitrogen,
            double demandPhosphorus,
            double demandSulfur,
            double demandIron,
            double demandOrganicCarbon,
            double demandMineralCarbon,
            double waterTotal,
            double[]? ledger)
        {
            _temperature = temperature;
            _pressure = pressure;
            _ultraviolet = ultraviolet;
            _absorbedDose = absorbedDose;
            _equivalentDose = equivalentDose;
            _irradiance = irradiance;
            _waterIce = waterIce;
            _vapor = vapor;
            _porosity = porosity;
            _perchlorateMass = perchlorateMass;
            _perchloratePercent = perchloratePercent;
            _icePercent = icePercent;
            _scope = scope;
            _carbonDioxide = carbonDioxide;
            _water = water;
            _oxygen = oxygen;
            _nitrogen = nitrogen;
            _phosphorus = phosphorus;
            _sulfur = sulfur;
            _iron = iron;
            _organicCarbon = organicCarbon;
            _mineralCarbon = mineralCarbon;
            _demandCarbonDioxide = demandCarbonDioxide;
            _demandWater = demandWater;
            _demandOxygen = demandOxygen;
            _demandNitrogen = demandNitrogen;
            _demandPhosphorus = demandPhosphorus;
            _demandSulfur = demandSulfur;
            _demandIron = demandIron;
            _demandOrganicCarbon = demandOrganicCarbon;
            _demandMineralCarbon = demandMineralCarbon;
            _waterTotal = waterTotal;
            _ledger = ledger;
        }

        public static CellCopy Capture(
            EnvironmentState environment,
            ResourcePool resources,
            AllocationBuffer buffer,
            CellWaterBudget budget,
            CellMassLedger ledger)
        {
            return new CellCopy(
                environment.TemperatureKelvin,
                environment.PressurePascal,
                environment.UvRadiation,
                environment.IonizingAbsorbedDoseGyPerSol,
                environment.IonizingEquivalentDoseSvPerSol,
                environment.SolarIrradiance,
                environment.WaterIce,
                environment.WaterVapor,
                environment.SoilPorosity,
                environment.PerchlorateMassKg,
                environment.PerchlorateWtPercent,
                environment.WaterIceWtPercent,
                environment.Scope,
                resources.CarbonDioxide,
                resources.Water,
                resources.Oxygen,
                resources.Nitrogen,
                resources.Phosphorus,
                resources.Sulfur,
                resources.Iron,
                resources.OrganicCarbon,
                resources.MineralCarbon,
                buffer.CarbonDioxide,
                buffer.Water,
                buffer.Oxygen,
                buffer.Nitrogen,
                buffer.Phosphorus,
                buffer.Sulfur,
                buffer.Iron,
                buffer.OrganicCarbon,
                buffer.MineralCarbon,
                budget.RecordedTotalKg,
                ledger.ExportRecorded());
        }

        public void Restore(
            EnvironmentState environment,
            ResourcePool resources,
            AllocationBuffer buffer,
            CellWaterBudget budget,
            CellMassLedger ledger)
        {
            environment.TemperatureKelvin = _temperature;
            environment.PressurePascal = _pressure;
            environment.UvRadiation = _ultraviolet;
            environment.IonizingAbsorbedDoseGyPerSol = _absorbedDose;
            environment.IonizingEquivalentDoseSvPerSol = _equivalentDose;
            environment.SolarIrradiance = _irradiance;
            environment.WaterIce = _waterIce;
            environment.WaterVapor = _vapor;
            environment.SoilPorosity = _porosity;
            environment.PerchlorateMassKg = _perchlorateMass;
            environment.PerchlorateWtPercent = _perchloratePercent;
            environment.WaterIceWtPercent = _icePercent;
            environment.Scope = _scope;
            resources.CarbonDioxide = _carbonDioxide;
            resources.Water = _water;
            resources.Oxygen = _oxygen;
            resources.Nitrogen = _nitrogen;
            resources.Phosphorus = _phosphorus;
            resources.Sulfur = _sulfur;
            resources.Iron = _iron;
            resources.OrganicCarbon = _organicCarbon;
            resources.MineralCarbon = _mineralCarbon;
            buffer.CarbonDioxide = _demandCarbonDioxide;
            buffer.Water = _demandWater;
            buffer.Oxygen = _demandOxygen;
            buffer.Nitrogen = _demandNitrogen;
            buffer.Phosphorus = _demandPhosphorus;
            buffer.Sulfur = _demandSulfur;
            buffer.Iron = _demandIron;
            buffer.OrganicCarbon = _demandOrganicCarbon;
            buffer.MineralCarbon = _demandMineralCarbon;
            budget.RecordedTotalKg = _waterTotal;
            ledger.RestoreRecorded(_ledger);
        }
    }

    private readonly record struct PopulationCopy(
        PopulationId Id,
        OrganismId OrganismId,
        int CellIndex,
        Genome Genome,
        double Abundance,
        double BiomassKg,
        double EnergyReserveJ,
        double AgeDistributionMeanSols,
        double Viability)
    {
        public static PopulationCopy Capture(Population population)
        {
            return new PopulationCopy(
                population.Id,
                population.OrganismId,
                population.CellIndex,
                population.Genome,
                population.Abundance,
                population.BiomassKg,
                population.EnergyReserveJ,
                population.AgeDistributionMeanSols,
                population.Viability);
        }

        public Population Restore()
        {
            return new Population(Id, OrganismId, CellIndex, Genome)
            {
                Abundance = Abundance,
                BiomassKg = BiomassKg,
                EnergyReserveJ = EnergyReserveJ,
                AgeDistributionMeanSols = AgeDistributionMeanSols,
                Viability = Viability,
            };
        }
    }
}
