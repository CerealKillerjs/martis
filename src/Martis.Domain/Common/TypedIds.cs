namespace Martis.Domain.Common;

/// <summary>
/// Identifies a region. Two identifiers are equal when <see cref="Value"/> is equal.
/// </summary>
public readonly record struct RegionId(int Value);

/// <summary>
/// Identifies a cell. Two identifiers are equal when <see cref="Value"/> is equal.
/// </summary>
public readonly record struct CellId(int Value);

/// <summary>
/// Identifies one aggregated population inside a single game.
/// The same value may be reused in another game (TDD §2.1).
/// </summary>
public readonly record struct PopulationId(long Value);

/// <summary>
/// Stable content identifier of an organism definition.
/// Content identifiers are intended to stay the same across versions (TDD §2.1).
/// </summary>
public readonly record struct OrganismId(string Value);

/// <summary>
/// Stable content identifier of a gene definition.
/// Content identifiers are intended to stay the same across versions (TDD §2.1).
/// </summary>
public readonly record struct GeneId(string Value);

/// <summary>
/// Stable content identifier of a reaction definition.
/// Content identifiers are intended to stay the same across versions (TDD §2.1).
/// </summary>
public readonly record struct ReactionId(string Value);
