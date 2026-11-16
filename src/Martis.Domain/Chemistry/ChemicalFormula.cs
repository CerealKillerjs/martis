using System.Globalization;
using System.Text;

namespace Martis.Domain.Chemistry;

/// <summary>
/// Element counts of one species, parsed from a Hill-like text such as
/// <c>C106H263O110N16P</c> (TDD §10.1, matrix A). Counts are positive integers.
/// Elements are kept in ordinal symbol order so two formulas compare by value.
/// The parser accepts one capital letter plus optional lower-case letters per symbol,
/// and no brackets, charges or hydrates.
/// </summary>
public sealed class ChemicalFormula
{
    private readonly KeyValuePair<string, int>[] _counts;

    private ChemicalFormula(KeyValuePair<string, int>[] counts, string text)
    {
        _counts = counts;
        Text = text;
    }

    /// <summary>The text the formula was parsed from.</summary>
    public string Text { get; }

    /// <summary>Element symbols and atom counts, ordinal by symbol.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Counts => _counts;

    public int CountOf(string element)
    {
        foreach (var pair in _counts)
        {
            if (string.Equals(pair.Key, element, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return 0;
    }

    public static ChemicalFormula Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsAsciiLetterUpper(text[i]))
            {
                throw new FormatException($"Formula '{text}' has an unexpected '{text[i]}' at position {i}.");
            }

            var symbol = new StringBuilder().Append(text[i]);
            i++;
            while (i < text.Length && char.IsAsciiLetterLower(text[i]))
            {
                symbol.Append(text[i]);
                i++;
            }

            var start = i;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }

            var count = 1;
            if (i > start && !int.TryParse(text.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out count))
            {
                throw new FormatException($"Formula '{text}' has a count that is too large.");
            }

            if (count <= 0)
            {
                throw new FormatException($"Formula '{text}' has a zero count for '{symbol}'.");
            }

            var key = symbol.ToString();
            counts[key] = checked((counts.TryGetValue(key, out var existing) ? existing : 0) + count);
        }

        return new ChemicalFormula(counts.ToArray(), text);
    }

    /// <summary>True when both formulas hold the same elements with the same counts.</summary>
    public bool SameCompositionAs(ChemicalFormula other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _counts.SequenceEqual(other._counts);
    }

    public override string ToString() => Text;
}
