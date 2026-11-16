using CsCheck;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;

namespace Martis.Simulation.Tests.Properties;

/// <summary>
/// Property checks for the PRNG and the engine. CsCheck chooses the inputs.
/// The simulation itself still draws only from <c>pcg32-v1</c>.
/// </summary>
public sealed class SimulationPropertyTests
{
    [Fact]
    public void RestoredGeneratorRepeatsTheContinuation()
    {
        Gen.UInt.Sample(seed =>
        {
            var rng = Pcg32.Seed(seed, 7UL);
            _ = rng.NextUInt32();
            var saved = rng.ExportState();
            var first = rng.NextUInt64();
            var second = rng.NextUInt64();
            var restored = Pcg32.FromState(saved);
            return restored.NextUInt64() == first && restored.NextUInt64() == second;
        });
    }

    [Fact]
    public void NextIntStaysInsideTheHalfOpenRange()
    {
        Gen.Int[1, 10_000].Sample(bound =>
        {
            var rng = Pcg32.Seed(1UL, 1UL);
            for (var i = 0; i < 8; i++)
            {
                var value = rng.NextInt(bound);
                if (value < 0 || value >= bound)
                {
                    return false;
                }
            }

            return true;
        });
    }

    [Fact]
    public void NextDoubleStaysInsideTheUnitInterval()
    {
        Gen.UInt.Sample(seed =>
        {
            var rng = Pcg32.Seed(seed, 3UL);
            for (var i = 0; i < 16; i++)
            {
                var value = rng.NextDouble();
                if (!double.IsFinite(value) || value < 0.0 || value >= 1.0)
                {
                    return false;
                }
            }

            return true;
        });
    }

    [Fact]
    public void SplitAdvanceMatchesOneAdvance()
    {
        Gen.Select(Gen.Int[0, 6], Gen.Int[0, 6]).Sample(pair =>
        {
            var split = Fingerprint(pair.Item1, pair.Item2);
            var whole = Fingerprint(pair.Item1 + pair.Item2, 0);
            return split.SequenceEqual(whole);
        });
    }

    private static long[] Fingerprint(int firstSteps, int secondSteps)
    {
        var engine = new SimulationEngine(
            new SimulationState(2, 2),
            new SimulationConfiguration(2, 2, 1.0, 19UL, 8, true),
            RngStreams.Create(19UL),
            [new EnvironmentStreamProbe()]);
        engine.Advance(firstSteps);
        engine.Advance(secondSteps);
        var bits = new long[engine.State.CellCount + 1];
        bits[0] = engine.CurrentTick;
        for (var i = 0; i < engine.State.CellCount; i++)
        {
            bits[i + 1] = BitConverter.DoubleToInt64Bits(engine.State.Environment[i].SolarIrradiance);
        }

        return bits;
    }

    private sealed class EnvironmentStreamProbe : ISimulationSystem
    {
        public string Name => "environment-stream-probe";

        public void Execute(SimulationContext context, double deltaTimeSols)
        {
            for (var i = 0; i < context.State.CellCount; i++)
            {
                context.State.Environment[i].SolarIrradiance = context.Random.Environment.NextDouble();
            }
        }
    }
}
