using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;

namespace Martis.Simulation.Snapshots;

/// <summary>
/// Copy of the environment, the deposits and the PRNG.
/// Restoring it puts those values back. The initial-mass ledger stays the t0
/// account and is not rewritten. The hash is SHA-256 of a canonical text, not
/// <c>GetHashCode</c>. Doubles are written as IEEE bits so the text does not
/// depend on culture or on a decimal rounding.
/// </summary>
public sealed class ScenarioRunSnapshot
{
    private readonly long _tick;
    private readonly string _scenarioName;
    private readonly string _dataVersion;
    private readonly bool _sepEnabled;
    private readonly RngStreamState _random;
    private readonly CellImage[] _cells;

    private ScenarioRunSnapshot(
        long tick,
        string scenarioName,
        string dataVersion,
        bool sepEnabled,
        RngStreamState random,
        CellImage[] cells)
    {
        _tick = tick;
        _scenarioName = scenarioName;
        _dataVersion = dataVersion;
        _sepEnabled = sepEnabled;
        _random = random;
        _cells = cells;
    }

    public static ScenarioRunSnapshot Capture(SimulationEngine engine, MarsBaselineScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(scenario);
        var state = engine.State;
        var cells = new CellImage[state.CellCount];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = CellImage.Capture(
                state.Environment[i],
                state.Resources[i],
                state.WaterBudgets[i],
                state.MassLedgers[i]);
        }

        return new ScenarioRunSnapshot(
            state.Tick,
            scenario.Name,
            scenario.DataVersion,
            scenario.SepEnabled,
            engine.Random.ExportState(),
            cells);
    }

    public void Restore(SimulationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.State.CellCount != _cells.Length)
        {
            throw new InvalidOperationException("The snapshot cell count does not match the world.");
        }

        engine.State.Tick = _tick;
        engine.Random.Restore(_random);
        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i].Restore(engine.State.Environment[i], engine.State.Resources[i], engine.State.WaterBudgets[i]);
        }
    }

    public string Sha256Hex()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalText()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string CanonicalText()
    {
        var builder = new StringBuilder();
        builder.Append("tick:").Append(_tick.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("scenario:").Append(_scenarioName).Append('\n');
        builder.Append("version:").Append(_dataVersion).Append('\n');
        builder.Append("sep:").Append(_sepEnabled ? "true" : "false").Append('\n');
        AppendRandom(builder, _random);
        for (var i = 0; i < _cells.Length; i++)
        {
            builder.Append("cell:").Append(i.ToString(CultureInfo.InvariantCulture)).Append(';');
            _cells[i].Append(builder);
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private static void AppendRandom(StringBuilder builder, RngStreamState random)
    {
        builder.Append("rng:");
        AppendState(builder, random.Environment);
        builder.Append(';');
        AppendState(builder, random.Mutation);
        builder.Append(';');
        AppendState(builder, random.Reproduction);
        builder.Append(';');
        AppendState(builder, random.GeneticDrift);
        builder.Append(';');
        AppendState(builder, random.Events);
        builder.Append('\n');
    }

    private static void AppendState(StringBuilder builder, RandomState state)
    {
        builder.Append(state.State.ToString("X16", CultureInfo.InvariantCulture));
        builder.Append(',');
        builder.Append(state.Increment.ToString("X16", CultureInfo.InvariantCulture));
    }

    private sealed class CellImage
    {
        private readonly string? _scope;
        private readonly bool _temperatureSet;
        private readonly double _temperature;
        private readonly bool _pressureSet;
        private readonly double _pressure;
        private readonly bool _uvSet;
        private readonly double _uv;
        private readonly double _absorbedDose;
        private readonly double _equivalentDose;
        private readonly double _irradiance;
        private readonly double _waterIce;
        private readonly bool _vaporSet;
        private readonly double _vapor;
        private readonly bool _porositySet;
        private readonly double _porosity;
        private readonly bool _perchlorateMassSet;
        private readonly double _perchlorateMass;
        private readonly double _perchloratePercent;
        private readonly double _icePercent;
        private readonly double[] _deposits;
        private readonly double _waterTotal;
        private readonly string _ledger;

        private CellImage(
            string? scope,
            bool temperatureSet,
            double temperature,
            bool pressureSet,
            double pressure,
            bool uvSet,
            double uv,
            double absorbedDose,
            double equivalentDose,
            double irradiance,
            double waterIce,
            bool vaporSet,
            double vapor,
            bool porositySet,
            double porosity,
            bool perchlorateMassSet,
            double perchlorateMass,
            double perchloratePercent,
            double icePercent,
            double[] deposits,
            double waterTotal,
            string ledger)
        {
            _scope = scope;
            _temperatureSet = temperatureSet;
            _temperature = temperature;
            _pressureSet = pressureSet;
            _pressure = pressure;
            _uvSet = uvSet;
            _uv = uv;
            _absorbedDose = absorbedDose;
            _equivalentDose = equivalentDose;
            _irradiance = irradiance;
            _waterIce = waterIce;
            _vaporSet = vaporSet;
            _vapor = vapor;
            _porositySet = porositySet;
            _porosity = porosity;
            _perchlorateMassSet = perchlorateMassSet;
            _perchlorateMass = perchlorateMass;
            _perchloratePercent = perchloratePercent;
            _icePercent = icePercent;
            _deposits = deposits;
            _waterTotal = waterTotal;
            _ledger = ledger;
        }

        public static CellImage Capture(
            EnvironmentState environment,
            ResourcePool resources,
            CellWaterBudget budget,
            CellMassLedger ledger)
        {
            var recorded = ledger.IsRecorded
                ? Bits(ledger.RecordedInitialTotalKg)
                : "unrecorded";
            return new CellImage(
                ScopeText.Format(environment.Scope),
                environment.TemperatureKelvin.IsSet,
                environment.TemperatureKelvin.IsSet ? environment.TemperatureKelvin.Value : 0.0,
                environment.PressurePascal.IsSet,
                environment.PressurePascal.IsSet ? environment.PressurePascal.Value : 0.0,
                environment.UvRadiation.IsSet,
                environment.UvRadiation.IsSet ? environment.UvRadiation.Value : 0.0,
                environment.IonizingAbsorbedDoseGyPerSol,
                environment.IonizingEquivalentDoseSvPerSol,
                environment.SolarIrradiance,
                environment.WaterIce,
                environment.WaterVapor.IsSet,
                environment.WaterVapor.IsSet ? environment.WaterVapor.Value : 0.0,
                environment.SoilPorosity.IsSet,
                environment.SoilPorosity.IsSet ? environment.SoilPorosity.Value : 0.0,
                environment.PerchlorateMassKg.IsSet,
                environment.PerchlorateMassKg.IsSet ? environment.PerchlorateMassKg.Value : 0.0,
                environment.PerchlorateWtPercent,
                environment.WaterIceWtPercent,
                CellMassLedger.Read(resources, environment.WaterIce),
                budget.RecordedTotalKg,
                recorded);
        }

        public void Restore(EnvironmentState environment, ResourcePool resources, CellWaterBudget budget)
        {
            environment.Scope = ScopeText.Parse(_scope);
            environment.TemperatureKelvin = _temperatureSet ? AssignedDouble.Set(_temperature) : AssignedDouble.Unset;
            environment.PressurePascal = _pressureSet ? AssignedDouble.Set(_pressure) : AssignedDouble.Unset;
            environment.UvRadiation = _uvSet ? AssignedDouble.Set(_uv) : AssignedDouble.Unset;
            environment.IonizingAbsorbedDoseGyPerSol = _absorbedDose;
            environment.IonizingEquivalentDoseSvPerSol = _equivalentDose;
            environment.SolarIrradiance = _irradiance;
            environment.WaterIce = _waterIce;
            environment.WaterVapor = _vaporSet ? AssignedDouble.Set(_vapor) : AssignedDouble.Unset;
            environment.SoilPorosity = _porositySet ? AssignedDouble.Set(_porosity) : AssignedDouble.Unset;
            environment.PerchlorateMassKg = _perchlorateMassSet ? AssignedDouble.Set(_perchlorateMass) : AssignedDouble.Unset;
            environment.PerchlorateWtPercent = _perchloratePercent;
            environment.WaterIceWtPercent = _icePercent;
            WriteDeposits(resources);
            budget.RecordedTotalKg = _waterTotal;
        }

        public void Append(StringBuilder builder)
        {
            builder.Append(_scope ?? "unset").Append(';');
            builder.Append(Flag(_temperatureSet, _temperature)).Append(';');
            builder.Append(Flag(_pressureSet, _pressure)).Append(';');
            builder.Append(Flag(_uvSet, _uv)).Append(';');
            builder.Append(Bits(_absorbedDose)).Append(';');
            builder.Append(Bits(_equivalentDose)).Append(';');
            builder.Append(Bits(_irradiance)).Append(';');
            builder.Append(Bits(_waterIce)).Append(';');
            builder.Append(Flag(_vaporSet, _vapor)).Append(';');
            builder.Append(Flag(_porositySet, _porosity)).Append(';');
            builder.Append(Flag(_perchlorateMassSet, _perchlorateMass)).Append(';');
            builder.Append(Bits(_perchloratePercent)).Append(';');
            builder.Append(Bits(_icePercent)).Append(';');
            for (var i = 0; i < _deposits.Length; i++)
            {
                builder.Append(Bits(_deposits[i])).Append(';');
            }

            builder.Append(Bits(_waterTotal)).Append(';');
            builder.Append(_ledger);
        }

        private void WriteDeposits(ResourcePool resources)
        {
            var names = CellMassLedger.Names;
            for (var i = 0; i < names.Count - 1; i++)
            {
                switch (names[i])
                {
                    case nameof(ResourcePool.CarbonDioxide):
                        resources.CarbonDioxide = _deposits[i];
                        break;
                    case nameof(ResourcePool.Water):
                        resources.Water = _deposits[i];
                        break;
                    case nameof(ResourcePool.Oxygen):
                        resources.Oxygen = _deposits[i];
                        break;
                    case nameof(ResourcePool.Nitrogen):
                        resources.Nitrogen = _deposits[i];
                        break;
                    case nameof(ResourcePool.Phosphorus):
                        resources.Phosphorus = _deposits[i];
                        break;
                    case nameof(ResourcePool.Sulfur):
                        resources.Sulfur = _deposits[i];
                        break;
                    case nameof(ResourcePool.Iron):
                        resources.Iron = _deposits[i];
                        break;
                    case nameof(ResourcePool.OrganicCarbon):
                        resources.OrganicCarbon = _deposits[i];
                        break;
                    case nameof(ResourcePool.MineralCarbon):
                        resources.MineralCarbon = _deposits[i];
                        break;
                    default:
                        throw new InvalidOperationException($"Deposit '{names[i]}' cannot be restored.");
                }
            }
        }

        private static string Flag(bool set, double value) => set ? Bits(value) : "unset";

        private static string Bits(double value)
            => BitConverter.DoubleToUInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
    }
}
