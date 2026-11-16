using System.Text.Json;
using Martis.Simulation.Scenarios;

namespace Martis.Simulation.Tests.Scenarios;

/// <summary>
/// R6-05. The loader does not multiply the conversion factor. This test checks
/// the published file: each derived number is the native number times the unit
/// prefix times <c>conversion.factor</c>, within a relative tolerance of 1e-3.
/// That tolerance covers the rounding already written into the file. A larger
/// gap is a data error and is not corrected here.
/// </summary>
public sealed class DerivedValueConsistencyTests
{
    private const double RelativeTolerance = 1e-3;

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
            var derivedValue = parameter.GetProperty("value").GetDouble();
            var nativeValue = native.GetProperty("value").GetDouble();
            var scale = UnitScale(native.GetProperty("unit").GetString()!)
                / UnitScale(parameter.GetProperty("unit").GetString()!);
            var expected = nativeValue * scale * factor;
            var relative = Math.Abs(derivedValue - expected) / Math.Max(Math.Abs(expected), double.MinValue);
            Assert.True(
                relative <= RelativeTolerance,
                $"{id}: derived {derivedValue} vs native {nativeValue} * scale {scale} * factor {factor} = {expected} (relative {relative}).");
            checkedAny = true;
        }

        Assert.True(checkedAny);
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
