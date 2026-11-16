using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.Genetics;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;

namespace Martis.Simulation.Snapshots;

/// <summary>
/// Canonical JSON for <see cref="StateSnapshot"/> (ADR-004: export and debugging only,
/// not the save format). The layout is fixed: properties in a fixed order, one cell
/// and one population per line, <c>\n</c> line ends, UTF-8 without BOM.
/// Doubles use the shortest text that parses back to the same IEEE bits.
/// 64-bit seeds and PRNG words are strings (decimal seed, 16-digit hex words), so a
/// reader that maps JSON numbers to doubles cannot round them.
/// <see cref="Read"/> accepts only this layout's properties, in this order.
/// </summary>
public static class StateSnapshotJson
{
    private static readonly JsonWriterOptions CompactOptions = new() { Indented = false };

    private static readonly string[] RootProperties =
    [
        "schema", "engine_version", "pipeline_version", "prng_algorithm", "scenario",
        "configuration", "tick", "rng", "resource_columns", "cells", "populations",
    ];

    private static readonly string[] ScenarioProperties = ["id", "name", "data_version", "sep_enabled"];

    private static readonly string[] ConfigurationProperties =
    [
        "map_width", "map_height", "delta_time_sols", "world_seed",
        "max_variants_per_species_per_cell", "enable_validation",
    ];

    private static readonly string[] StreamNames = ["environment", "mutation", "reproduction", "genetic_drift", "events"];

    private static readonly string[] StreamProperties = ["state", "increment"];

    private static readonly string[] CellProperties =
    [
        "index", "scope", "temperature_kelvin", "pressure_pascal", "uv_radiation",
        "ionizing_absorbed_dose_gy_per_sol", "ionizing_equivalent_dose_sv_per_sol",
        "solar_irradiance", "water_ice_kg", "water_vapor_kg", "soil_porosity",
        "perchlorate_mass_kg", "perchlorate_wt_percent", "water_ice_wt_percent",
        "deposits_kg", "demands_kg", "water_budget_total_kg", "initial_mass_kg",
    ];

    private static readonly string[] PopulationProperties =
    [
        "id", "organism_id", "cell_index", "genome", "abundance", "biomass_kg",
        "energy_reserve_j", "age_distribution_mean_sols", "viability",
    ];

    private static readonly string[] GeneProperties = ["gene", "expression"];

    public static byte[] Write(StateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var text = new StringBuilder();
        text.Append("{\n");
        Line(text, "schema", Compact(writer => writer.WriteStringValue(snapshot.SchemaVersion)), last: false);
        Line(text, "engine_version", Compact(writer => writer.WriteStringValue(snapshot.EngineVersion)), last: false);
        Line(text, "pipeline_version", Compact(writer => writer.WriteStringValue(snapshot.PipelineVersion)), last: false);
        Line(text, "prng_algorithm", Compact(writer => writer.WriteStringValue(snapshot.PrngAlgorithm)), last: false);
        Line(text, "scenario", Compact(writer => WriteScenario(writer, snapshot.Scenario)), last: false);
        Line(text, "configuration", Compact(writer => WriteConfiguration(writer, snapshot.Configuration)), last: false);
        Line(text, "tick", Compact(writer => writer.WriteNumberValue(snapshot.Tick)), last: false);
        Line(text, "rng", Compact(writer => WriteRandom(writer, snapshot.Random)), last: false);
        Line(text, "resource_columns", Compact(writer => WriteNames(writer, ResourceColumns.Names)), last: false);
        Block(text, "cells", snapshot.Cells.Count, i => Compact(writer => WriteCell(writer, i, snapshot.Cells[i])), last: false);
        Block(text, "populations", snapshot.Populations.Count, i => Compact(writer => WritePopulation(writer, snapshot.Populations[i])), last: true);
        text.Append("}\n");
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text.ToString());
    }

    /// <summary>Lower-case hex SHA-256 of <see cref="Write"/>.</summary>
    public static string Sha256Hex(StateSnapshot snapshot)
        => Convert.ToHexString(SHA256.HashData(Write(snapshot))).ToLowerInvariant();

    public static StateSnapshot Read(ReadOnlySpan<byte> utf8)
    {
        using var document = JsonDocument.Parse(
            utf8.ToArray(),
            new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        var root = document.RootElement;
        Expect(root, RootProperties, "snapshot");
        var schema = String(root, "schema");
        if (!string.Equals(schema, StateSnapshot.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new FormatException($"Snapshot schema '{schema}' is not supported.");
        }

        var columns = root.GetProperty("resource_columns").EnumerateArray().Select(item => item.GetString()).ToArray();
        if (!columns.SequenceEqual(ResourceColumns.Names, StringComparer.Ordinal))
        {
            throw new FormatException("resource_columns does not match this engine's resource pool.");
        }

        var cells = root.GetProperty("cells").EnumerateArray().Select((cell, index) => ReadCell(cell, index)).ToArray();
        var populations = root.GetProperty("populations").EnumerateArray().Select(ReadPopulation).ToArray();
        return new StateSnapshot(
            schema,
            String(root, "engine_version"),
            String(root, "pipeline_version"),
            String(root, "prng_algorithm"),
            ReadScenario(root.GetProperty("scenario")),
            ReadConfiguration(root.GetProperty("configuration")),
            root.GetProperty("tick").GetInt64(),
            ReadRandom(root.GetProperty("rng")),
            cells,
            populations);
    }

    private static void Line(StringBuilder text, string name, string value, bool last)
    {
        text.Append("  ").Append(Compact(writer => writer.WriteStringValue(name))).Append(": ").Append(value);
        text.Append(last ? "\n" : ",\n");
    }

    private static void Block(StringBuilder text, string name, int count, Func<int, string> item, bool last)
    {
        text.Append("  ").Append(Compact(writer => writer.WriteStringValue(name))).Append(": [");
        if (count == 0)
        {
            text.Append(']');
        }
        else
        {
            text.Append('\n');
            for (var i = 0; i < count; i++)
            {
                text.Append("    ").Append(item(i)).Append(i + 1 < count ? ",\n" : "\n");
            }

            text.Append("  ]");
        }

        text.Append(last ? "\n" : ",\n");
    }

    private static string Compact(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, CompactOptions))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteScenario(Utf8JsonWriter writer, ScenarioIdentity? scenario)
    {
        if (scenario is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("id", scenario.Id);
        writer.WriteString("name", scenario.Name);
        writer.WriteString("data_version", scenario.DataVersion);
        writer.WriteBoolean("sep_enabled", scenario.SepEnabled);
        writer.WriteEndObject();
    }

    private static void WriteConfiguration(Utf8JsonWriter writer, SimulationConfiguration configuration)
    {
        writer.WriteStartObject();
        writer.WriteNumber("map_width", configuration.MapWidth);
        writer.WriteNumber("map_height", configuration.MapHeight);
        writer.WriteNumber("delta_time_sols", configuration.DeltaTimeSols);
        writer.WriteString("world_seed", configuration.WorldSeed.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("max_variants_per_species_per_cell", configuration.MaxVariantsPerSpeciesPerCell);
        writer.WriteBoolean("enable_validation", configuration.EnableValidation);
        writer.WriteEndObject();
    }

    private static void WriteRandom(Utf8JsonWriter writer, RngStreamState random)
    {
        RandomState[] streams = [random.Environment, random.Mutation, random.Reproduction, random.GeneticDrift, random.Events];
        writer.WriteStartObject();
        for (var i = 0; i < streams.Length; i++)
        {
            writer.WriteStartObject(StreamNames[i]);
            writer.WriteString("state", Hex(streams[i].State));
            writer.WriteString("increment", Hex(streams[i].Increment));
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteNames(Utf8JsonWriter writer, IReadOnlyList<string> names)
    {
        writer.WriteStartArray();
        foreach (var name in names)
        {
            writer.WriteStringValue(name);
        }

        writer.WriteEndArray();
    }

    private static void WriteCell(Utf8JsonWriter writer, int index, CellStateImage cell)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", index);
        if (cell.Scope is null)
        {
            writer.WriteNull("scope");
        }
        else
        {
            writer.WriteString("scope", cell.Scope);
        }

        Optional(writer, "temperature_kelvin", cell.TemperatureKelvin);
        Optional(writer, "pressure_pascal", cell.PressurePascal);
        Optional(writer, "uv_radiation", cell.UvRadiation);
        writer.WriteNumber("ionizing_absorbed_dose_gy_per_sol", cell.IonizingAbsorbedDoseGyPerSol);
        writer.WriteNumber("ionizing_equivalent_dose_sv_per_sol", cell.IonizingEquivalentDoseSvPerSol);
        writer.WriteNumber("solar_irradiance", cell.SolarIrradiance);
        writer.WriteNumber("water_ice_kg", cell.WaterIceKg);
        Optional(writer, "water_vapor_kg", cell.WaterVaporKg);
        Optional(writer, "soil_porosity", cell.SoilPorosity);
        Optional(writer, "perchlorate_mass_kg", cell.PerchlorateMassKg);
        writer.WriteNumber("perchlorate_wt_percent", cell.PerchlorateWtPercent);
        writer.WriteNumber("water_ice_wt_percent", cell.WaterIceWtPercent);
        Numbers(writer, "deposits_kg", cell.Deposits);
        Numbers(writer, "demands_kg", cell.Demands);
        writer.WriteNumber("water_budget_total_kg", cell.WaterBudgetTotalKg);
        if (cell.InitialMass is null)
        {
            writer.WriteNull("initial_mass_kg");
        }
        else
        {
            Numbers(writer, "initial_mass_kg", cell.InitialMass);
        }

        writer.WriteEndObject();
    }

    private static void WritePopulation(Utf8JsonWriter writer, PopulationImage population)
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", population.Id);
        writer.WriteString("organism_id", population.OrganismId);
        writer.WriteNumber("cell_index", population.CellIndex);
        writer.WriteStartArray("genome");
        foreach (var gene in population.Genome)
        {
            writer.WriteStartObject();
            writer.WriteString("gene", gene.GeneId.Value);
            writer.WriteNumber("expression", gene.Expression);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("abundance", population.Abundance);
        writer.WriteNumber("biomass_kg", population.BiomassKg);
        writer.WriteNumber("energy_reserve_j", population.EnergyReserveJ);
        writer.WriteNumber("age_distribution_mean_sols", population.AgeDistributionMeanSols);
        writer.WriteNumber("viability", population.Viability);
        writer.WriteEndObject();
    }

    private static void Optional(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void Numbers(Utf8JsonWriter writer, string name, IReadOnlyList<double> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteNumberValue(value);
        }

        writer.WriteEndArray();
    }

    private static ScenarioIdentity? ReadScenario(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        Expect(element, ScenarioProperties, "scenario");
        return new ScenarioIdentity(
            String(element, "id"),
            String(element, "name"),
            String(element, "data_version"),
            element.GetProperty("sep_enabled").GetBoolean());
    }

    private static SimulationConfiguration ReadConfiguration(JsonElement element)
    {
        Expect(element, ConfigurationProperties, "configuration");
        var configuration = new SimulationConfiguration(
            element.GetProperty("map_width").GetInt32(),
            element.GetProperty("map_height").GetInt32(),
            element.GetProperty("delta_time_sols").GetDouble(),
            ulong.Parse(String(element, "world_seed"), NumberStyles.None, CultureInfo.InvariantCulture),
            element.GetProperty("max_variants_per_species_per_cell").GetInt32(),
            element.GetProperty("enable_validation").GetBoolean());
        configuration.Validate();
        return configuration;
    }

    private static RngStreamState ReadRandom(JsonElement element)
    {
        Expect(element, StreamNames, "rng");
        var streams = new RandomState[StreamNames.Length];
        for (var i = 0; i < streams.Length; i++)
        {
            var stream = element.GetProperty(StreamNames[i]);
            Expect(stream, StreamProperties, StreamNames[i]);
            streams[i] = new RandomState(ParseHex(String(stream, "state")), ParseHex(String(stream, "increment")));
        }

        return new RngStreamState(streams[0], streams[1], streams[2], streams[3], streams[4]);
    }

    private static CellStateImage ReadCell(JsonElement element, int position)
    {
        Expect(element, CellProperties, "cell");
        var index = element.GetProperty("index").GetInt32();
        if (index != position)
        {
            throw new FormatException($"Cell at position {position} carries index {index}.");
        }

        var scope = element.GetProperty("scope");
        var initial = element.GetProperty("initial_mass_kg");
        return new CellStateImage(
            scope.ValueKind == JsonValueKind.Null ? null : scope.GetString(),
            OptionalNumber(element, "temperature_kelvin"),
            OptionalNumber(element, "pressure_pascal"),
            OptionalNumber(element, "uv_radiation"),
            element.GetProperty("ionizing_absorbed_dose_gy_per_sol").GetDouble(),
            element.GetProperty("ionizing_equivalent_dose_sv_per_sol").GetDouble(),
            element.GetProperty("solar_irradiance").GetDouble(),
            element.GetProperty("water_ice_kg").GetDouble(),
            OptionalNumber(element, "water_vapor_kg"),
            OptionalNumber(element, "soil_porosity"),
            OptionalNumber(element, "perchlorate_mass_kg"),
            element.GetProperty("perchlorate_wt_percent").GetDouble(),
            element.GetProperty("water_ice_wt_percent").GetDouble(),
            NumberArray(element.GetProperty("deposits_kg"), ResourceColumns.Names.Count, "deposits_kg"),
            NumberArray(element.GetProperty("demands_kg"), ResourceColumns.Names.Count, "demands_kg"),
            element.GetProperty("water_budget_total_kg").GetDouble(),
            initial.ValueKind == JsonValueKind.Null
                ? null
                : NumberArray(initial, CellMassLedger.Names.Count, "initial_mass_kg"));
    }

    private static PopulationImage ReadPopulation(JsonElement element)
    {
        Expect(element, PopulationProperties, "population");
        var genome = element.GetProperty("genome").EnumerateArray().Select(gene =>
        {
            Expect(gene, GeneProperties, "gene");
            return new GeneExpression(new GeneId(String(gene, "gene")), gene.GetProperty("expression").GetDouble());
        }).ToArray();
        return new PopulationImage(
            element.GetProperty("id").GetInt64(),
            String(element, "organism_id"),
            element.GetProperty("cell_index").GetInt32(),
            genome,
            element.GetProperty("abundance").GetDouble(),
            element.GetProperty("biomass_kg").GetDouble(),
            element.GetProperty("energy_reserve_j").GetDouble(),
            element.GetProperty("age_distribution_mean_sols").GetDouble(),
            element.GetProperty("viability").GetDouble());
    }

    private static double? OptionalNumber(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDouble();
    }

    private static double[] NumberArray(JsonElement element, int length, string name)
    {
        var values = element.EnumerateArray().Select(item => item.GetDouble()).ToArray();
        if (values.Length != length)
        {
            throw new FormatException($"'{name}' has {values.Length} values, expected {length}.");
        }

        return values;
    }

    private static void Expect(JsonElement element, IReadOnlyList<string> names, string what)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"'{what}' must be a JSON object.");
        }

        var found = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (!found.SequenceEqual(names, StringComparer.Ordinal))
        {
            throw new FormatException(
                $"'{what}' has properties [{string.Join(", ", found)}], expected [{string.Join(", ", names)}].");
        }
    }

    private static string String(JsonElement element, string name)
        => element.GetProperty(name).GetString()
            ?? throw new FormatException($"'{name}' must be a string.");

    private static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);

    private static ulong ParseHex(string text)
    {
        if (text.Length != 16 || !ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException($"'{text}' is not a 16-digit hex word.");
        }

        return value;
    }
}
