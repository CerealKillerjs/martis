using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;

namespace Martis.Simulation.Tests.Engine;

/// <summary>
/// Repeatability of two runs (VAL-010). The same seed matches bit for bit and
/// another seed does not. This class does not lock system order: that lock is
/// <see cref="GoldenVectorTests"/>. The probe writes the Environment stream into
/// solar irradiance. Those numbers are draws in [0, 1), not a solar model.
/// </summary>
[Trait("Category", "Determinism")]
public sealed class EngineRepeatabilityTests
{
    [Fact]
    public void SameSeedMatchesBitForBitAndAnotherSeedDoesNot()
    {
        for (var run = 0; run < 2; run++)
        {
            var left = Fingerprint(Run(12345UL));
            var right = Fingerprint(Run(12345UL));
            Assert.Equal(left, right);

            var other = Fingerprint(Run(12346UL));
            Assert.NotEqual(left, other);
        }
    }

    [Fact]
    public void TheProbeConsumesOnlyTheEnvironmentStream()
    {
        var engine = Run(9UL);
        var untouched = RngStreams.Create(9UL);
        Assert.Equal(untouched.Mutation.ExportState(), engine.Random.Mutation.ExportState());
        Assert.Equal(untouched.Reproduction.ExportState(), engine.Random.Reproduction.ExportState());
        Assert.Equal(untouched.GeneticDrift.ExportState(), engine.Random.GeneticDrift.ExportState());
        Assert.Equal(untouched.Events.ExportState(), engine.Random.Events.ExportState());
        Assert.NotEqual(untouched.Environment.ExportState(), engine.Random.Environment.ExportState());
    }

    private static SimulationEngine Run(ulong seed)
    {
        var configuration = new SimulationConfiguration(
            PrototypeWorld.Width,
            PrototypeWorld.Height,
            1.0,
            seed,
            SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell,
            true);
        var systems = SimulationPipeline.CreateStubs().ToArray();
        for (var i = 0; i < systems.Length; i++)
        {
            if (systems[i].Name == SimulationSteps.UpdateEnvironment)
            {
                systems[i] = new EnvironmentStreamProbe();
            }
        }

        var engine = new SimulationEngine(
            new SimulationState(PrototypeWorld.Width, PrototypeWorld.Height),
            configuration,
            RngStreams.Create(seed),
            systems);
        engine.Advance(1000);
        return engine;
    }

    private static long[] Fingerprint(SimulationEngine engine)
    {
        var state = engine.State;
        var bits = new long[state.CellCount + 1];
        bits[0] = state.Tick;
        for (var i = 0; i < state.CellCount; i++)
        {
            bits[i + 1] = BitConverter.DoubleToInt64Bits(state.Environment[i].SolarIrradiance);
        }

        return bits;
    }

    private sealed class EnvironmentStreamProbe : ISimulationSystem
    {
        public string Name => SimulationSteps.UpdateEnvironment;

        public void Execute(SimulationContext context, double deltaTimeSols)
        {
            for (var i = 0; i < context.State.CellCount; i++)
            {
                context.State.Environment[i].SolarIrradiance = context.Random.Environment.NextDouble();
            }
        }
    }
}
