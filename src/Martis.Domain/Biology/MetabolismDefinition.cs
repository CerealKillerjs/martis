using Martis.Domain.Common;

namespace Martis.Domain.Biology;

/// <summary>
/// Reactions associated with an organism, in the order the caller listed them.
/// T02 stores identifiers only. Rates, yields and stoichiometry are absent.
/// TODO: pendiente MARTIS Ciencia.
/// </summary>
public sealed class MetabolismDefinition
{
    private readonly ReactionId[] _reactionIds;

    public MetabolismDefinition(IEnumerable<ReactionId> reactionIds)
    {
        ArgumentNullException.ThrowIfNull(reactionIds);
        var list = reactionIds.ToArray();
        for (var i = 0; i < list.Length; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (string.Equals(list[j].Value, list[i].Value, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Reaction '{list[i].Value}' is listed more than once.",
                        nameof(reactionIds));
                }
            }
        }

        _reactionIds = list;
    }

    /// <summary>Reaction identifiers in caller order. Empty when none are declared.</summary>
    public IReadOnlyList<ReactionId> ReactionIds => _reactionIds;
}
