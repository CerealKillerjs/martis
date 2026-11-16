using BenchmarkDotNet.Attributes;
using Martis.Simulation.Biology;
using Martis.Simulation.Engine;
using Martis.Simulation.Snapshots;

namespace Martis.Benchmarks;

/// <summary>Whole runs of 1000 sols (TDD §7.4 «1000 ticks», S1-35 FullSimulation).</summary>
[MemoryDiagnoser]
public class FullSimulation
{
    private SimulationEngine _engine = null!;

    [Params(100, 1000)]
    public int Ticks { get; set; }

    [IterationSetup(Target = nameof(EmptyWorld))]
    public void SetUpEmpty() => _engine = World.Empty();

    [IterationSetup(Target = nameof(FirstColony))]
    public void SetUpColony() => _engine = World.Colony(out _);

    /// <summary>pipeline-v0: environment and validation only (Sprint 1 prototype).</summary>
    [Benchmark(Baseline = true)]
    public long EmptyWorld()
    {
        _engine.Advance(Ticks);
        return _engine.CurrentTick;
    }

    /// <summary>pipeline-v3 with the FIRST COLONY start (one cell alive).</summary>
    [Benchmark]
    public long FirstColony()
    {
        _engine.Advance(Ticks);
        return _engine.CurrentTick;
    }
}

/// <summary>The §7.4 load: 4096 cells, 10 000 populations, three organisms.</summary>
[MemoryDiagnoser]
public class StressWorld
{
    private SimulationEngine _engine = null!;

    [Params(10, 100)]
    public int Ticks { get; set; }

    [IterationSetup]
    public void SetUp() => _engine = World.Stress(out _);

    [Benchmark]
    public long TenThousandPopulations()
    {
        _engine.Advance(Ticks);
        return _engine.CurrentTick;
    }
}

/// <summary>Snapshot creation and restore of a living world (S1-35 SnapshotCreation, T10 Restore).</summary>
[MemoryDiagnoser]
public class Snapshots
{
    private SimulationEngine _engine = null!;
    private SimulationEngine _target = null!;
    private StateSnapshot _snapshot = null!;
    private byte[] _binary = [];
    private byte[] _json = [];

    [GlobalSetup]
    public void SetUp()
    {
        _engine = World.Stress(out _);
        _engine.Advance(20);
        _snapshot = StateSnapshot.Capture(_engine, null);
        _binary = StateSnapshotBinary.Write(_snapshot);
        _json = StateSnapshotJson.Write(_snapshot);
        _target = Martis.Simulation.Scenarios.MarsBaselineWorld.CreateWithBiology(World.Scenario, World.Catalog, World.Seed).Engine;
    }

    [Benchmark]
    public StateSnapshot Capture() => StateSnapshot.Capture(_engine, null);

    [Benchmark]
    public void Restore() => _snapshot.Restore(_target);

    [Benchmark]
    public byte[] WriteBinary() => StateSnapshotBinary.Write(_snapshot);

    [Benchmark]
    public StateSnapshot ReadBinary() => StateSnapshotBinary.Read(_binary);

    [Benchmark]
    public byte[] WriteJson() => StateSnapshotJson.Write(_snapshot);

    public int PopulationCount => _snapshot.Populations.Count;

    public int JsonLength => _json.Length;
}
