using Martis.Domain.Common;

namespace Martis.Domain.Genetics;

/// <summary>
/// Expression of one gene in the closed interval [0, 1] (TDD §2.4, VAL-005).
/// The value is a normalized model weight, not a measured molecular rate.
/// </summary>
public readonly record struct GeneExpression
{
    public GeneExpression(GeneId geneId, double expression)
        : this(geneId, expression, trusted: false)
    {
    }

    private GeneExpression(GeneId geneId, double expression, bool trusted)
    {
        if (!trusted)
        {
            QuantityRules.RequireUnitInterval(expression, nameof(expression));
        }

        GeneId = geneId;
        Expression = expression;
    }

    public GeneId GeneId { get; }

    public double Expression { get; }

    /// <summary>
    /// Builds an expression without the range check. Validator tests use this to
    /// plant a VAL-005 violation. Public constructors do not.
    /// </summary>
    internal static GeneExpression Unchecked(GeneId geneId, double expression)
        => new(geneId, expression, trusted: true);
}
