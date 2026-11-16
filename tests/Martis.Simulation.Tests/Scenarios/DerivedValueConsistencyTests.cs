using System.Text.Json;
using Martis.Domain.Common;
using Martis.Simulation.Scenarios;

namespace Martis.Simulation.Tests.Scenarios;

/// <summary>
/// R6-05. The loader does not multiply the conversion factor. This test checks
/// the published file: each derived number is the native number times the unit
/// prefix times <c>conversion.factor</c>, within a relative tolerance of 1e-3.
/// That tolerance covers the rounding already written into the file. A larger
/// gap is a data error and is not corrected here.
/// A day-to-sol conversion must declare exactly
/// <see cref="MartianTime.SecondsPerSol"/> / 86400. A different day length,
/// for example 24.6 h, fails here even if the stored value was recomputed to match it.
/// </summary>
public sealed class DerivedValueConsistencyTests
{
    /// <summary>
    /// One tolerance for the derived values already stored in the file.
    /// If new derivatives are added, the tolerance has to be per entry,
    /// according to the figures stored for that entry.
    /// </summary>
    private const double RelativeTolerance = 1e-3;

    /// <summary>Earth day used by the RAD day-to-sol factor. 24 h, not 24.6 h.</summary>
    private const double SecondsPerEarthDay = 86400d;

    [Fact]
    public void PublishedDerivedValuesMatchNativeTimesFactor()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Repo.Root(),
            "data",
            "scenarios",
            "MarsBaseline",
            MarsBaselineScenario.ScienceFileName)));
        var parameters = document.RootElement.GetProperty("parameters");
        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var parameter in parameters.EnumerateArray())
        {
            byId.Add(parameter.GetProperty("id").GetString()!, parameter);
        }

        var checkedAny = false;
        var checkedDayToSol = false;
        foreach (var parameter in parameters.EnumerateArray())
        {
            if (!parameter.TryGetProperty("derived_from", out var derivedFromElement)
                || derivedFromElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var id = parameter.GetProperty("id").GetString()!;
            var native = byId[derivedFromElement.GetString()!];
            var factor = parameter.GetProperty("conversion").GetProperty("factor").GetDouble();
            var nativeUnit = native.GetProperty("unit").GetString()!;
            var derivedUnit = parameter.GetProperty("unit").GetString()!;
            if (IsDayToSolConversion(nativeUnit, derivedUnit))
            {
                // The declared factor, not the stored product. A consistent but
                // wrong day length (24.6 h / 24 h) still has to fail.
                var solFactor = MartianTime.SecondsPerSol / SecondsPerEarthDay;
                var dayOf24Point6Hours = 24.6d * 3600d / SecondsPerEarthDay;
                Assert.NotEqual(BitConverter.DoubleToInt64Bits(dayOf24Point6Hours), BitConverter.DoubleToInt64Bits(solFactor));
                Assert.True(
                    BitConverter.DoubleToInt64Bits(factor) == BitConverter.DoubleToInt64Bits(solFactor),
                    $"{id}: declared factor {factor} is not {MartianTime.SecondsPerSol}/86400. A 24.6 h day ({dayOf24Point6Hours}) is not that factor.");
                checkedDayToSol = true;
            }

            var derivedValue = parameter.GetProperty("value").GetDouble();
            var nativeValue = native.GetProperty("value").GetDouble();
            var scale = UnitScale(nativeUnit) / UnitScale(derivedUnit);
            var expected = nativeValue * scale * factor;
            // double.MinValue is negative, so Math.Max used to ignore it and a
            // zero expected value divided by zero. Epsilon is the positive floor.
            var relative = Math.Abs(derivedValue - expected) / Math.Max(Math.Abs(expected), double.Epsilon);
            Assert.True(
                relative <= RelativeTolerance,
                $"{id}: derived {derivedValue} vs native {nativeValue} * scale {scale} * factor {factor} = {expected} (relative {relative}).");
            checkedAny = true;
        }

        Assert.True(checkedAny);
        Assert.True(checkedDayToSol);
    }

    /// <summary>
    /// Native unit per Earth day, derived unit per sol. The SI prefix is a
    /// separate scale. The factor is only the day-to-sol ratio.
    /// </summary>
    private static bool IsDayToSolConversion(string nativeUnit, string derivedUnit)
    {
        return IsDay(TimeToken(nativeUnit))
            && string.Equals(TimeToken(derivedUnit), "sol", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDay(string token)
    {
        return token.Equals("day", StringComparison.OrdinalIgnoreCase)
            || token.Equals("día", StringComparison.OrdinalIgnoreCase)
            || token.Equals("days", StringComparison.OrdinalIgnoreCase);
    }

    private static string TimeToken(string unit)
    {
        var parts = unit.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }

    /// <summary>
    /// SI prefix on the unit symbol. <c>mGy</c> is milli; <c>Gy</c> is one.
    /// A leading <c>m</c>, <c>k</c> or <c>u</c> counts only when the next character
    /// is an uppercase unit symbol, so a word is not treated as a prefix.
    /// </summary>
    private static double UnitScale(string unit)
    {
        var symbol = unit.Split('/', ' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (symbol.StartsWith("µ", StringComparison.Ordinal) || symbol.StartsWith("μ", StringComparison.Ordinal))
        {
            return 1e-6;
        }

        if (symbol.Length > 1 && char.IsUpper(symbol[1]))
        {
            return symbol[0] switch
            {
                'm' => 1e-3,
                'u' => 1e-6,
                'k' => 1e3,
                _ => 1.0,
            };
        }

        return 1.0;
    }
}
