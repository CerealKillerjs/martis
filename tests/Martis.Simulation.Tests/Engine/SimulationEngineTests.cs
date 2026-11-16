using Martis.Contracts.Commands;
using Martis.Domain.Validation;
using Martis.Domain.World;
using Martis.Simulation.Commands;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;

namespace Martis.Simulation.Tests.Engine;

public sealed class SimulationEngineTests
{
    [Fact]
    public void PipelineOrderIsFixed()
    {
        Assert.Equal("pipeline-v0", SimulationPipeline.Version);
        Assert.Equal(
            [
                "apply-commands",
                "update-environment",
                "calculate-resource-availability",
                "calculate-metabolism",
                "allocate-shared-resources",
                "update-biomass-and-energy",
                "calculate-mortality",
                "generate-reproduction",
                "apply-mutations",
                "apply-selection-and-drift",
                "update-ecology",
                "recycle-dead-biomass",
                "validate-conservation",
                "update-statistics",
                "publish-snapshot",
            ],
            SimulationPipeline.StepNames);
        var engine = Engine();
        Assert.Equal(SimulationPipeline.StepNames, engine.SystemNames);
    }

    [Fact]
    public void AdvanceFollowsRegistrationOrderAndTickRules()
    {
        var log = new List<string>();
        var systems = new ISimulationSystem[]
        {
            new RecordingSystem("first", log),
            new RecordingSystem("second", log),
        };
        var engine = Engine(systems);

        engine.Advance(0);
        Assert.Empty(log);
        Assert.Equal(0, engine.CurrentTick);

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Advance(-1));
        Assert.Empty(log);
        Assert.Equal(0, engine.CurrentTick);

        engine.Advance(2);
        Assert.Equal(["first", "second", "first", "second"], log);
        Assert.Equal(2, engine.CurrentTick);
    }

    [Fact]
    public void Advance1000ReachesTick1000()
    {
        var engine = Engine();
        engine.Advance(1000);
        Assert.Equal(1000, engine.CurrentTick);
        Assert.Equal(1000, engine.State.Tick);
    }

    [Fact]
    public void CommandsAreDataAndRunOnTheNextTickInOrder()
    {
        var seen = new List<string>();
        var engine = Engine(handlers: [new NoteHandler(seen)]);
        engine.ApplyCommand(new NoteCommand("alpha"));
        engine.ApplyCommand(new NoteCommand("beta"));
        Assert.Empty(seen);
        Assert.Equal(0, engine.CurrentTick);

        engine.Advance(1);
        Assert.Equal(["alpha", "beta"], seen);
        Assert.Equal(1, engine.CurrentTick);
    }

    [Fact]
    public void UnknownCommandKindFailsOnTheTickAndDoesNotAdvance()
    {
        var engine = Engine();
        engine.ApplyCommand(new OrphanCommand());
        var error = Assert.Throws<InvalidOperationException>(() => engine.Advance(1));
        Assert.Contains("orphan", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, engine.CurrentTick);
    }

    [Fact]
    public void ACorruptStateFailsTheTickAndListsTheViolations()
    {
        var engine = Engine();
        engine.State.Resources[0].Water = -1;
        var error = Assert.Throws<SimulationInvariantException>(() => engine.Advance(1));
        Assert.Contains(error.Violations, violation => violation.InvariantId == InvariantIds.NoNegativeResource);
        Assert.Contains("VAL-001", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, engine.CurrentTick);

        var balance = Engine();
        balance.State.Resources[0].Water = 5;
        balance.State.Environment[0].WaterIce = 5;
        balance.State.WaterBudgets[0].RecordedTotalKg = 5;
        var broken = Assert.Throws<SimulationInvariantException>(() => balance.Advance(1));
        Assert.Contains(broken.Violations, violation => violation.InvariantId == InvariantIds.WaterPartition);
        Assert.Equal(0, balance.CurrentTick);
        Assert.Equal(-1, engine.State.Resources[0].Water);
    }

    [Fact]
    public void EnableValidationFalseLetsANegativeDepositThrough()
    {
        var engine = Engine();
        engine.Initialize(new SimulationConfiguration(4, 4, 1.0, 11UL, 32, false));
        engine.State.Resources[0].Water = -1;
        engine.Advance(1);
        Assert.Equal(1, engine.CurrentTick);
        Assert.Equal(-1, engine.State.Resources[0].Water);
    }

    [Fact]
    public void AFailedTickRestoresTheWorldTheGeneratorAndTheTick()
    {
        var engine = Engine([new CorruptingSystem(armAtTick: 2, throwAfter: false)]);
        engine.Advance(2);
        var before = CanonicalFingerprint.Hash(engine);
        var error = Assert.Throws<SimulationInvariantException>(() => engine.Advance(1));
        Assert.Contains("VAL-001", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, engine.CurrentTick);
        Assert.Equal(before, CanonicalFingerprint.Hash(engine));

        var throwing = Engine([new CorruptingSystem(armAtTick: 1, throwAfter: true)]);
        throwing.Advance(1);
        var prior = CanonicalFingerprint.Hash(throwing);
        var boom = Assert.Throws<InvalidOperationException>(() => throwing.Advance(1));
        Assert.Contains("boom", boom.Message, StringComparison.Ordinal);
        Assert.Equal(1, throwing.CurrentTick);
        Assert.Equal(prior, CanonicalFingerprint.Hash(throwing));
    }

    [Fact]
    public void InitializeValidatesAndDoesNotResetTheTick()
    {
        var engine = Engine();
        engine.Advance(3);
        var replacement = new SimulationConfiguration(4, 4, 2.0, 9UL, 4, false);
        engine.Initialize(replacement);
        Assert.Equal(2.0, engine.Configuration.DeltaTimeSols);
        Assert.Equal(3, engine.CurrentTick);

        var invalid = new SimulationConfiguration(4, 4, double.NaN, 9UL, 4, false);
        Assert.Throws<InvalidOperationException>(() => engine.Initialize(invalid));
        Assert.Equal(2.0, engine.Configuration.DeltaTimeSols);
    }

    private static SimulationEngine Engine(
        IReadOnlyList<ISimulationSystem>? systems = null,
        IReadOnlyList<ICommandHandler>? handlers = null)
    {
        var configuration = new SimulationConfiguration(4, 4, 1.0, 11UL, 32, true);
        return new SimulationEngine(
            new SimulationState(4, 4),
            configuration,
            RngStreams.Create(11UL),
            systems ?? SimulationPipeline.CreateStubs(),
            handlers);
    }

    private sealed class CorruptingSystem : ISimulationSystem
    {
        private readonly long _armAtTick;
        private readonly bool _throwAfter;

        public CorruptingSystem(int armAtTick, bool throwAfter)
        {
            _armAtTick = armAtTick;
            _throwAfter = throwAfter;
        }

        public string Name => "corrupt";

        public void Execute(SimulationContext context, double deltaTimeSols)
        {
            if (context.State.Tick < _armAtTick)
            {
                return;
            }

            context.State.Resources[0].Water = -4;
            context.State.Tick = 50;
            _ = context.Random.Environment.NextUInt64();
            if (_throwAfter)
            {
                throw new InvalidOperationException("boom");
            }
        }
    }

    private sealed class RecordingSystem : ISimulationSystem
    {
        private readonly List<string> _log;

        public RecordingSystem(string name, List<string> log)
        {
            Name = name;
            _log = log;
        }

        public string Name { get; }

        public void Execute(SimulationContext context, double deltaTimeSols)
        {
            _log.Add(Name);
            Assert.Equal(context.Configuration.DeltaTimeSols, deltaTimeSols);
        }
    }

    private sealed record OrphanCommand : ISimulationCommand
    {
        public string Kind => "orphan";
    }

    private sealed record NoteCommand(string Text) : ISimulationCommand
    {
        public string Kind => "test-note";
    }

    private sealed class NoteHandler : ICommandHandler
    {
        private readonly List<string> _seen;

        public NoteHandler(List<string> seen)
        {
            _seen = seen;
        }

        public string Kind => "test-note";

        public void Handle(ISimulationCommand command, SimulationState state)
        {
            var note = Assert.IsType<NoteCommand>(command);
            _seen.Add(note.Text);
            _ = state;
        }
    }
}
