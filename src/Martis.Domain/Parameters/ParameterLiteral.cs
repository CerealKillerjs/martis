namespace Martis.Domain.Parameters;

/// <summary>
/// JSON value carried by one parameter. The loader does not coerce one kind into another
/// and does not replace null with a zero.
/// </summary>
public abstract record ParameterLiteral
{
    private ParameterLiteral()
    {
    }

    /// <summary>JSON number. <see cref="Raw"/> is the exact token from the file.</summary>
    public sealed record Number(double Value, string Raw) : ParameterLiteral;

    /// <summary>JSON boolean.</summary>
    public sealed record Boolean(bool Value) : ParameterLiteral;

    /// <summary>JSON string. An empty string is a value, not a missing unit.</summary>
    public sealed record Text(string Value) : ParameterLiteral;

    /// <summary>JSON array of two finite numbers, kept in file order.</summary>
    public sealed record Interval(double Minimum, double Maximum) : ParameterLiteral;

    /// <summary>JSON null. Pending entries use this. It is not the number zero.</summary>
    public sealed record None : ParameterLiteral;
}
