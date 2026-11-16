namespace Martis.Simulation.Random;

/// <summary>
/// Internal PCG state saved with a snapshot (TDD §6, ADR-003).
/// <see cref="State"/> is the LCG state. <see cref="Increment"/> is the odd stream
/// increment already installed by the seeder, not the sequence number passed to it.
/// </summary>
public sealed record RandomState(ulong State, ulong Increment);
