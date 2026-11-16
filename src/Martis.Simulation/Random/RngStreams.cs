namespace Martis.Simulation.Random;

/// <summary>
/// Five independent <c>pcg32-v1</c> streams derived from one world seed (ADR-003, H-04).
/// Each stream is <see cref="Pcg32.Seed"/> with the world seed as <c>initstate</c> and a
/// fixed sequence constant as <c>initseq</c>. The constants are 1..5 in the order
/// Environment, Mutation, Reproduction, GeneticDrift, Events. They are not hashed.
/// </summary>
public sealed class RngStreams
{
    public const ulong EnvironmentSequence = 1;
    public const ulong MutationSequence = 2;
    public const ulong ReproductionSequence = 3;
    public const ulong GeneticDriftSequence = 4;
    public const ulong EventsSequence = 5;

    private RngStreams(
        ulong worldSeed,
        Pcg32 environment,
        Pcg32 mutation,
        Pcg32 reproduction,
        Pcg32 geneticDrift,
        Pcg32 events)
    {
        WorldSeed = worldSeed;
        Environment = environment;
        Mutation = mutation;
        Reproduction = reproduction;
        GeneticDrift = geneticDrift;
        Events = events;
    }

    public ulong WorldSeed { get; }

    public Pcg32 Environment { get; }

    public Pcg32 Mutation { get; }

    public Pcg32 Reproduction { get; }

    public Pcg32 GeneticDrift { get; }

    public Pcg32 Events { get; }

    public static RngStreams Create(ulong worldSeed)
    {
        return new RngStreams(
            worldSeed,
            Pcg32.Seed(worldSeed, EnvironmentSequence),
            Pcg32.Seed(worldSeed, MutationSequence),
            Pcg32.Seed(worldSeed, ReproductionSequence),
            Pcg32.Seed(worldSeed, GeneticDriftSequence),
            Pcg32.Seed(worldSeed, EventsSequence));
    }

    public RngStreamState ExportState()
    {
        return new RngStreamState(
            Environment.ExportState(),
            Mutation.ExportState(),
            Reproduction.ExportState(),
            GeneticDrift.ExportState(),
            Events.ExportState());
    }

    public void Restore(RngStreamState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Environment.Restore(state.Environment);
        Mutation.Restore(state.Mutation);
        Reproduction.Restore(state.Reproduction);
        GeneticDrift.Restore(state.GeneticDrift);
        Events.Restore(state.Events);
    }
}
