namespace Martis.Domain.Parameters;

/// <summary>
/// Optional range beside a value. It is not the value itself.
/// An interval keeps file order and is not sorted.
/// </summary>
public abstract record ParameterRange
{
    private ParameterRange()
    {
    }

    public sealed record Interval(double Minimum, double Maximum) : ParameterRange;

    public sealed record Text(string Value) : ParameterRange;
}
