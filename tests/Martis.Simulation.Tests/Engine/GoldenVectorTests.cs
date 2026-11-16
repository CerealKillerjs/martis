using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;

namespace Martis.Simulation.Tests.Engine;

/// <summary>
/// Locked fingerprint of the empty 64 by 64 world after a fixed run.
/// The second test shows that a different system order changes that fingerprint.
/// </summary>
public sealed class GoldenVectorTests
{
    public const int TickCount = 100;

    public const ulong Seed = 12345UL;

    /// <summary>
    /// SHA-256 of the canonical text for seed 12345 after 100 ticks of pipeline-v0.
    /// Recomputed when the canonical text or the engine result changes on purpose.
    /// </summary>
    public const string Expected =
        "7e10ce7b59f01bc2931443e6b3a0334a461fd66beef2549756983d2192808253";

    [Fact]
    public void Seed12345After100TicksMatchesTheLockedFingerprint()
    {
        var first = CanonicalFingerprint.Hash(RunProduction());
        var second = CanonicalFingerprint.Hash(RunProduction());
        Assert.Equal(first, second);
        Assert.Equal(Expected, first);
    }

    [Fact]
    public void SwappingSystemOrderChangesTheFingerprint()
    {
        var forward = HashInOrder(reversed: false);
        var again = HashInOrder(reversed: false);
        var reversed = HashInOrder(reversed: true);

        Assert.Equal(forward, again);
        Assert.NotEqual(forward, reversed);
    }

    private static SimulationEngine RunProduction()
    {
        var engine = PrototypeWorld.Create(Seed);
        engine.Advance(TickCount);
        return engine;
    }

    private static string HashInOrder(bool reversed)
    {
        ISimulationSystem firstCell = new DrawingSystem("draw-cell-0", 0);
        ISimulationSystem secondCell = new DrawingSystem("draw-cell-1", 1);
        var configuration = new SimulationConfiguration(2, 2, 1.0, Seed, 8, true);
        var engine = new SimulationEngine(
            new SimulationState(2, 2),
            configuration,
            RngStreams.Create(Seed),
            reversed ? [secondCell, firstCell] : [firstCell, secondCell]);
        engine.Advance(3);
        return CanonicalFingerprint.Hash(engine);
    }

    /// <summary>
    /// Writes one Environment draw into a fixed cell. Order decides which cell
    /// receives the earlier draw.
    /// </summary>
    private sealed class DrawingSystem : ISimulationSystem
    {
        private readonly int _cell;

        public DrawingSystem(string name, int cell)
        {
            Name = name;
            _cell = cell;
        }

        public string Name { get; }

        public void Execute(SimulationContext context, double deltaTimeSols)
        {
            context.State.Environment[_cell].SolarIrradiance = context.Random.Environment.NextDouble();
        }
    }
}
