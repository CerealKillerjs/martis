using Martis.Domain.Common;

namespace Martis.Domain.Environment;

/// <summary>
/// Initial mass of one cell, recorded when a scenario fills the deposits.
/// The nine <see cref="ResourcePool"/> stocks come first, in
/// <see cref="ResourcePool.Quantities"/> order, and <see cref="EnvironmentState.WaterIce"/>
/// is last. Unset water vapor is not a term: it is not stored as zero kilograms.
/// The total is that sequence added from left to right. Nothing in this type
/// creates or moves mass. A later process may change a deposit; the recorded
/// initial stays the t0 account.
/// </summary>
public sealed class CellMassLedger
{
    /// <summary>Deposit names in the order of <see cref="RecordedInitialTotalKg"/>.</summary>
    public static IReadOnlyList<string> Names { get; } = CreateNames();

    private double[]? _values;

    /// <summary>True after <see cref="Record"/> has copied the initial stocks.</summary>
    public bool IsRecorded => _values is not null;

    /// <summary>
    /// Copies the recorded stocks, or null when nothing has been recorded.
    /// The caller owns the array. Used to roll a tick back.
    /// </summary>
    public double[]? ExportRecorded()
    {
        if (_values is null)
        {
            return null;
        }

        var copy = new double[_values.Length];
        Array.Copy(_values, copy, _values.Length);
        return copy;
    }

    /// <summary>Replaces the recorded stocks. A null clears the account.</summary>
    public void RestoreRecorded(double[]? values)
    {
        if (values is null)
        {
            _values = null;
            return;
        }

        var copy = new double[values.Length];
        Array.Copy(values, copy, values.Length);
        _values = copy;
    }

    /// <summary>
    /// Sum of the recorded stocks, in <see cref="Names"/> order.
    /// Throws if <see cref="Record"/> has not been called, so a missing account is not a zero.
    /// </summary>
    public double RecordedInitialTotalKg
    {
        get
        {
            if (_values is null)
            {
                throw new InvalidOperationException("The initial mass has not been recorded.");
            }

            return Sum(_values);
        }
    }

    /// <summary>Copies the current stocks. A second call is refused so t0 cannot be rewritten.</summary>
    public void Record(ResourcePool pool, double waterIceKg)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (_values is not null)
        {
            throw new InvalidOperationException("The initial mass is already recorded.");
        }

        var values = Read(pool, waterIceKg);
        for (var i = 0; i < values.Length; i++)
        {
            QuantityRules.RequireNonNegativeFinite(values[i], Names[i]);
        }

        _values = values;
    }

    /// <summary>
    /// True when every current stock matches the recorded initial bit for bit.
    /// False when the initial has not been recorded.
    /// </summary>
    public bool Matches(ResourcePool pool, double waterIceKg)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (_values is null)
        {
            return false;
        }

        var current = Read(pool, waterIceKg);
        for (var i = 0; i < _values.Length; i++)
        {
            if (BitConverter.DoubleToInt64Bits(current[i]) != BitConverter.DoubleToInt64Bits(_values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Current stocks in <see cref="Names"/> order. The caller owns the array.</summary>
    public static double[] Read(ResourcePool pool, double waterIceKg)
    {
        ArgumentNullException.ThrowIfNull(pool);
        var quantities = pool.Quantities;
        var values = new double[quantities.Count + 1];
        for (var i = 0; i < quantities.Count; i++)
        {
            values[i] = quantities[i].Value;
        }

        values[^1] = waterIceKg;
        return values;
    }

    /// <summary>Left-to-right sum. The order is part of the account.</summary>
    public static double Sum(ReadOnlySpan<double> values)
    {
        var total = 0.0;
        for (var i = 0; i < values.Length; i++)
        {
            total += values[i];
        }

        return total;
    }

    private static string[] CreateNames()
    {
        var quantities = new ResourcePool().Quantities;
        var names = new string[quantities.Count + 1];
        for (var i = 0; i < quantities.Count; i++)
        {
            names[i] = quantities[i].Name;
        }

        names[^1] = nameof(EnvironmentState.WaterIce);
        return names;
    }
}
