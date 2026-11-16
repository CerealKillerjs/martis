namespace Martis.Domain.Common;

/// <summary>
/// Shared finite-and-range checks (H-13). A comparison with zero alone does not
/// reject NaN, so every quantity goes through <see cref="double.IsFinite"/> as well.
/// </summary>
internal static class QuantityRules
{
    public static bool IsStrictlyPositiveFinite(double value)
        => double.IsFinite(value) && value > 0.0;

    public static bool IsNonNegativeFinite(double value)
        => double.IsFinite(value) && value >= 0.0;

    public static bool IsUnitInterval(double value)
        => IsNonNegativeFinite(value) && value <= 1.0;

    public static void RequireStrictlyPositiveFinite(double value, string name)
    {
        if (!IsStrictlyPositiveFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                "The quantity must be finite and greater than 0.");
        }
    }

    public static void RequireNonNegativeFinite(double value, string name)
    {
        if (!IsNonNegativeFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                "The quantity must be finite and greater than or equal to 0.");
        }
    }

    public static double ReturnNonNegativeFinite(double value, string name)
    {
        RequireNonNegativeFinite(value, name);
        return value;
    }

    public static double ReturnUnitInterval(double value, string name, string invariantId)
    {
        if (!IsUnitInterval(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                invariantId + ": the quantity must be finite and inside the closed interval [0, 1].");
        }

        return value;
    }

    public static void RequireUnitInterval(double value, string name)
    {
        if (!IsUnitInterval(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                "The quantity must be finite and inside the closed interval [0, 1].");
        }
    }
}
