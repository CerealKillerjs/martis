namespace Martis.Domain.Common;

/// <summary>
/// A scalar that may be absent. The default is <see cref="Unset"/>, which is not
/// the number 0: an assigned 0 stays a different value.
/// A nullable double would hide that distinction inside null. This marker names it.
/// </summary>
public readonly record struct AssignedDouble
{
    private readonly byte _state;
    private readonly double _value;

    private AssignedDouble(byte state, double value)
    {
        _state = state;
        _value = value;
    }

    public bool IsUnset => _state == 0;

    public bool IsSet => _state == 1;

    /// <summary>
    /// The assigned number. Throws <see cref="InvalidOperationException"/> when unset.
    /// </summary>
    public double Value
    {
        get
        {
            if (IsUnset)
            {
                throw new InvalidOperationException("The quantity is unset.");
            }

            return _value;
        }
    }

    public static AssignedDouble Unset => default;

    public static AssignedDouble Set(double value) => new(1, value);
}
