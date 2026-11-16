using System.Collections;
using Martis.Domain.Common;

namespace Martis.Domain.Genetics;

/// <summary>
/// Functional genome: a set of gene expressions, not a DNA string (TDD §2.4).
/// Enumeration follows <see cref="GeneId"/> in ordinal order so the result does
/// not depend on insertion order or on hash order (VAL-005, ADR-000 R4).
/// </summary>
public sealed class Genome : IEnumerable<GeneExpression>
{
    private readonly GeneExpression[] _ordered;

    public Genome(IDictionary<GeneId, double> expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        var buffer = new GeneExpression[expressions.Count];
        var index = 0;
        foreach (var pair in expressions)
        {
            buffer[index] = new GeneExpression(pair.Key, pair.Value);
            index++;
        }

        _ordered = Order(buffer, skipRangeChecks: false);
    }

    public Genome(IEnumerable<GeneExpression> expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        _ordered = Order(expressions.ToArray(), skipRangeChecks: false);
    }

    private Genome(GeneExpression[] expressions, bool skipRangeChecks)
    {
        _ordered = Order(expressions, skipRangeChecks);
    }

    /// <summary>
    /// Expressions in ordinal <see cref="GeneId"/> order.
    /// </summary>
    public IReadOnlyList<GeneExpression> Expressions => _ordered;

    public GeneExpression this[int index] => _ordered[index];

    public int Count => _ordered.Length;

    public double this[GeneId geneId]
    {
        get
        {
            if (!TryGetExpression(geneId, out var expression))
            {
                throw new KeyNotFoundException($"Gene '{geneId.Value}' is not in the genome.");
            }

            return expression;
        }
    }

    /// <summary>
    /// Looks up an expression. When the gene is absent, <paramref name="expression"/>
    /// is 0 and the method returns false. Callers must use the boolean: 0 is also
    /// a legal expression.
    /// </summary>
    public bool TryGetExpression(GeneId geneId, out double expression)
    {
        var lower = 0;
        var upper = _ordered.Length - 1;
        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var comparison = CompareGene(_ordered[middle].GeneId, geneId);
            if (comparison == 0)
            {
                expression = _ordered[middle].Expression;
                return true;
            }

            if (comparison < 0)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        expression = 0.0;
        return false;
    }

    public IEnumerator<GeneExpression> GetEnumerator()
        => ((IEnumerable<GeneExpression>)_ordered).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Stores the given expressions without the [0, 1] check, still sorted by
    /// <see cref="GeneId"/> and still rejecting duplicates. Used only so the
    /// validator can be shown a VAL-005 failure.
    /// </summary>
    internal static Genome CreateUnchecked(IEnumerable<GeneExpression> expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        return new Genome(expressions.ToArray(), skipRangeChecks: true);
    }

    private static GeneExpression[] Order(GeneExpression[] source, bool skipRangeChecks)
    {
        if (!skipRangeChecks)
        {
            foreach (var gene in source)
            {
                QuantityRules.RequireUnitInterval(gene.Expression, "expressions");
            }
        }

        Array.Sort(source, static (left, right) => CompareGene(left.GeneId, right.GeneId));
        for (var i = 1; i < source.Length; i++)
        {
            if (CompareGene(source[i - 1].GeneId, source[i].GeneId) == 0)
            {
                throw new ArgumentException(
                    $"Gene '{source[i].GeneId.Value}' is listed more than once.",
                    "expressions");
            }
        }

        return source;
    }

    private static int CompareGene(GeneId left, GeneId right)
        => string.CompareOrdinal(left.Value, right.Value);
}
