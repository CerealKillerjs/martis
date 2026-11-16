namespace Martis.Simulation.Random;

/// <summary>
/// Export of the five domain streams. Order is Environment, Mutation, Reproduction,
/// GeneticDrift, Events (ADR-003).
/// </summary>
public sealed record RngStreamState(
    RandomState Environment,
    RandomState Mutation,
    RandomState Reproduction,
    RandomState GeneticDrift,
    RandomState Events);
