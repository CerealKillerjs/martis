using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;

namespace Martis.Simulation.Tests.Engine;

/// <summary>
/// SHA-256 of a canonical text of the world, the tick and the five PRNG streams.
/// Line endings are LF. Doubles are the hex of their IEEE bits, so the text does
/// not depend on culture or on decimal rounding.
/// </summary>
internal static class CanonicalFingerprint
{
    public static string Hash(SimulationEngine engine)
    {
        var text = Serialize(engine);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static string Serialize(SimulationEngine engine)
    {
        var state = engine.State;
        var builder = new StringBuilder();
        Append(builder, "tick", state.Tick);
        Append(builder, "width", state.Width);
        Append(builder, "height", state.Height);
        AppendStream(builder, "Environment", engine.Random.Environment);
        AppendStream(builder, "Mutation", engine.Random.Mutation);
        AppendStream(builder, "Reproduction", engine.Random.Reproduction);
        AppendStream(builder, "GeneticDrift", engine.Random.GeneticDrift);
        AppendStream(builder, "Events", engine.Random.Events);

        for (var index = 0; index < state.CellCount; index++)
        {
            builder.Append("cell:").Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
            var environment = state.Environment[index];
            AppendAssigned(builder, "temperature", environment.TemperatureKelvin);
            AppendAssigned(builder, "pressure", environment.PressurePascal);
            AppendQuantities(builder, environment.Quantities);
            AppendScope(builder, environment.Scope);
            AppendQuantities(builder, state.Resources[index].Quantities);
            AppendBits(builder, "water-total", state.WaterBudgets[index].RecordedTotalKg);
            AppendQuantities(builder, state.AllocationBuffers[index].Quantities);
        }

        builder.Append("populations:").Append(state.Populations.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var population in state.Populations)
        {
            Append(builder, "population", population.Id.Value);
            builder.Append("organism:").Append(population.OrganismId.Value).Append('\n');
            Append(builder, "cell-index", population.CellIndex);
            AppendBits(builder, "abundance", population.Abundance);
            AppendBits(builder, "biomass", population.BiomassKg);
            AppendBits(builder, "energy", population.EnergyReserveJ);
            AppendBits(builder, "age", population.AgeDistributionMeanSols);
            AppendBits(builder, "viability", population.Viability);
            foreach (var gene in population.Genome.Expressions)
            {
                builder.Append("gene:").Append(gene.GeneId.Value).Append(':');
                builder.Append(Bits(gene.Expression)).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void AppendStream(StringBuilder builder, string name, Pcg32 stream)
    {
        var state = stream.ExportState();
        builder.Append("rng:").Append(name).Append(':');
        builder.Append(state.State.ToString("x16", CultureInfo.InvariantCulture)).Append(':');
        builder.Append(state.Increment.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
    }

    private static void AppendQuantities(StringBuilder builder, IReadOnlyList<NamedQuantity> quantities)
    {
        foreach (var quantity in quantities)
        {
            AppendBits(builder, quantity.Name, quantity.Value);
        }
    }

    private static void AppendAssigned(StringBuilder builder, string name, AssignedDouble value)
    {
        builder.Append(name).Append(':');
        builder.Append(value.IsSet ? "set:" + Bits(value.Value) : "unset").Append('\n');
    }

    private static void AppendScope(StringBuilder builder, EnvironmentScope? scope)
    {
        builder.Append("scope:");
        builder.Append(scope switch
        {
            null => "unset",
            EnvironmentScope.PlanetaryMean => "planetary_mean",
            EnvironmentScope.Regional regional => "regional:" + regional.SiteId.Value,
            EnvironmentScope.Lab => "lab",
            EnvironmentScope.Game => "game",
            _ => "unknown",
        });
        builder.Append('\n');
    }

    private static void AppendBits(StringBuilder builder, string name, double value)
    {
        builder.Append(name).Append(':').Append(Bits(value)).Append('\n');
    }

    private static void Append(StringBuilder builder, string name, long value)
    {
        builder.Append(name).Append(':').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    private static string Bits(double value)
    {
        return BitConverter.DoubleToInt64Bits(value).ToString("x16", CultureInfo.InvariantCulture);
    }
}
