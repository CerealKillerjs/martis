namespace Martis.Domain.Common;

/// <summary>
/// Stable identifier of a regional site. It is not an organism, gene or reaction id.
/// <see cref="Value"/> is <see cref="string.Empty"/> when the identifier is omitted, and never null.
/// </summary>
public readonly record struct SiteId
{
    private readonly ContentKey _key;

    public SiteId(string Value)
    {
        _key = new ContentKey(Value);
    }

    public string Value => _key.Value;
}
