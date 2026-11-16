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
/// Storage for a content identifier. Empty text and omitted text are one value,
/// so <c>default</c> compares equal to <c>""</c>. The field stays null in that
/// case; <see cref="Value"/> still returns <see cref="string.Empty"/>.
/// </summary>
internal readonly record struct ContentKey
{
    private readonly string? _text;

    public ContentKey(string text)
    {
        _text = string.IsNullOrEmpty(text) ? null : text;
    }

    public string Value => _text ?? string.Empty;
}

/// <summary>
/// Stable content identifier of an organism definition.
/// Content identifiers are intended to stay the same across versions (TDD §2.1).
/// <see cref="Value"/> is <see cref="string.Empty"/> when the identifier is omitted, and never null.
/// </summary>
public readonly record struct OrganismId
{
    private readonly ContentKey _key;

    /// <summary>Creates an identifier. The parameter name matches the previous positional record.</summary>
    public OrganismId(string Value)
    {
        _key = new ContentKey(Value);
    }

    public string Value => _key.Value;
}

/// <summary>
/// Stable content identifier of a gene definition.
/// Content identifiers are intended to stay the same across versions (TDD §2.1).
/// <see cref="Value"/> is <see cref="string.Empty"/> when the identifier is omitted, and never null.
/// </summary>
public readonly record struct GeneId
{
    private readonly ContentKey _key;

    /// <summary>Creates an identifier. The parameter name matches the previous positional record.</summary>
    public GeneId(string Value)
    {
        _key = new ContentKey(Value);
    }

    public string Value => _key.Value;
}

/// <summary>
/// Stable content identifier of a reaction definition.
/// Content identifiers are intended to stay the same across versions (TDD §2.1).
/// <see cref="Value"/> is <see cref="string.Empty"/> when the identifier is omitted, and never null.
/// </summary>
public readonly record struct ReactionId
{
    private readonly ContentKey _key;

    /// <summary>Creates an identifier. The parameter name matches the previous positional record.</summary>
    public ReactionId(string Value)
    {
        _key = new ContentKey(Value);
    }

    public string Value => _key.Value;
}
