namespace Martis.Domain.Parameters;

/// <summary>
/// Declared conversion from a native value to a derived one.
/// <see cref="Factor"/> is not applied by the loader. The stored derived number
/// stays whatever the file says.
/// <see cref="FactorClassification"/> is the classification of the factor, not of
/// the derived value, and it is <c>MODEL_ASSUMPTION</c>.
/// </summary>
public sealed class ParameterConversion
{
    public ParameterConversion(
        string expression,
        double factor,
        string factorClassification,
        IReadOnlyList<string> assumptions)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            throw new ArgumentException("A conversion needs an expression.", nameof(expression));
        }

        if (!double.IsFinite(factor) || factor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "The conversion factor must be finite and greater than 0.");
        }

        if (!string.Equals(factorClassification, "MODEL_ASSUMPTION", StringComparison.Ordinal))
        {
            throw new ArgumentException("The conversion factor is classified MODEL_ASSUMPTION.", nameof(factorClassification));
        }

        ArgumentNullException.ThrowIfNull(assumptions);
        if (assumptions.Count == 0 || assumptions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A conversion needs at least one non-empty assumption.", nameof(assumptions));
        }

        Expression = expression;
        Factor = factor;
        FactorClassification = factorClassification;
        Assumptions = assumptions;
    }

    public string Expression { get; }

    public double Factor { get; }

    public string FactorClassification { get; }

    public IReadOnlyList<string> Assumptions { get; }
}
