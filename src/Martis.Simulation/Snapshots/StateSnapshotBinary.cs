using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Martis.Domain.Common;
using Martis.Domain.Genetics;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;

namespace Martis.Simulation.Snapshots;

/// <summary>A save payload that fails its header, length or checksum check (ADR-004).</summary>
public sealed class SnapshotCorruptException(string message) : IOException(message);

/// <summary>
/// Versioned binary save format of <see cref="StateSnapshot"/> (ADR-004, ADR-015).
/// Little-endian. Header: magic <c>MRTS</c>, format version (u16), flags (u16, bit 0
/// reserved for compression and 0 for now), body length (i64) and SHA-256 of the body
/// (32 bytes). The body writes every field in the canonical order of
/// <see cref="StateSnapshotJson"/>: strings as length-prefixed UTF-8, doubles as their
/// IEEE bits, an optional value as a presence byte and the value.
/// </summary>
public static class StateSnapshotBinary
{
    public const ushort FormatVersion = 1;
    public const int HeaderLength = 4 + 2 + 2 + 8 + 32;

    private static readonly byte[] Magic = "MRTS"u8.ToArray();
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Write(StateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var body = Body(snapshot);
        using var stream = new MemoryStream(HeaderLength + body.Length);
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write((ushort)0);
            writer.Write((long)body.Length);
            writer.Write(SHA256.HashData(body));
            writer.Write(body);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Checks the header, the length and the checksum, then reads the body.
    /// Any mismatch throws <see cref="SnapshotCorruptException"/>.
    /// </summary>
    public static StateSnapshot Read(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < HeaderLength || !payload[..4].SequenceEqual(Magic))
        {
            throw new SnapshotCorruptException("Not a MARTIS save payload (missing MRTS header).");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..6]);
        if (version != FormatVersion)
        {
            throw new SnapshotCorruptException($"Save format {version} is not supported (expected {FormatVersion}).");
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..8]);
        if (flags != 0)
        {
            throw new SnapshotCorruptException($"Save flags {flags} are not supported.");
        }

        var length = BinaryPrimitives.ReadInt64LittleEndian(payload[8..16]);
        if (length != payload.Length - HeaderLength)
        {
            throw new SnapshotCorruptException("The save body length does not match the header.");
        }

        var body = payload[HeaderLength..];
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(payload[16..48]))
        {
            throw new SnapshotCorruptException("The save body checksum does not match.");
        }

        try
        {
            using var stream = new MemoryStream(body.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Utf8);
            var snapshot = ReadBody(reader);
            if (stream.Position != stream.Length)
            {
                throw new SnapshotCorruptException("The save body has trailing bytes.");
            }

            return snapshot;
        }
        catch (Exception exception) when (exception is EndOfStreamException or DecoderFallbackException or ArgumentException or FormatException)
        {
            throw new SnapshotCorruptException($"The save body cannot be read: {exception.Message}");
        }
    }

    private static byte[] Body(StateSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Utf8, leaveOpen: true))
        {
            writer.Write(snapshot.SchemaVersion);
            writer.Write(snapshot.EngineVersion);
            writer.Write(snapshot.PipelineVersion);
            writer.Write(snapshot.PrngAlgorithm);
            writer.Write(snapshot.Scenario is not null);
            if (snapshot.Scenario is { } scenario)
            {
                writer.Write(scenario.Id);
                writer.Write(scenario.Name);
                writer.Write(scenario.DataVersion);
                writer.Write(scenario.SepEnabled);
            }

            var configuration = snapshot.Configuration;
            writer.Write(configuration.MapWidth);
            writer.Write(configuration.MapHeight);
            writer.Write(configuration.DeltaTimeSols);
            writer.Write(configuration.WorldSeed);
            writer.Write(configuration.MaxVariantsPerSpeciesPerCell);
            writer.Write(configuration.EnableValidation);
            writer.Write(snapshot.Tick);
            writer.Write(snapshot.NextPopulationId);
            foreach (var random in Streams(snapshot.Random))
            {
                writer.Write(random.State);
                writer.Write(random.Increment);
            }

            writer.Write(ResourceColumns.Names.Count);
            foreach (var name in ResourceColumns.Names)
            {
                writer.Write(name);
            }

            writer.Write(snapshot.Cells.Count);
            foreach (var cell in snapshot.Cells)
            {
                WriteCell(writer, cell);
            }

            writer.Write(snapshot.Populations.Count);
            foreach (var population in snapshot.Populations)
            {
                writer.Write(population.Id);
                writer.Write(population.OrganismId);
                writer.Write(population.CellIndex);
                writer.Write(population.Genome.Count);
                foreach (var gene in population.Genome)
                {
                    writer.Write(gene.GeneId.Value);
                    writer.Write(gene.Expression);
                }

                writer.Write(population.Abundance);
                writer.Write(population.BiomassKg);
                writer.Write(population.EnergyReserveJ);
                writer.Write(population.AgeDistributionMeanSols);
                writer.Write(population.Viability);
            }
        }

        return stream.ToArray();
    }

    private static void WriteCell(BinaryWriter writer, CellStateImage cell)
    {
        Optional(writer, cell.Scope);
        Optional(writer, cell.TemperatureKelvin);
        Optional(writer, cell.PressurePascal);
        Optional(writer, cell.UvRadiation);
        writer.Write(cell.IonizingAbsorbedDoseGyPerSol);
        writer.Write(cell.IonizingEquivalentDoseSvPerSol);
        writer.Write(cell.SolarIrradiance);
        writer.Write(cell.WaterIceKg);
        Optional(writer, cell.WaterVaporKg);
        Optional(writer, cell.SoilPorosity);
        Optional(writer, cell.PerchlorateMassKg);
        writer.Write(cell.PerchlorateWtPercent);
        writer.Write(cell.WaterIceWtPercent);
        Doubles(writer, cell.Deposits);
        Doubles(writer, cell.Demands);
        writer.Write(cell.WaterBudgetTotalKg);
        writer.Write(cell.InitialMass is not null);
        if (cell.InitialMass is not null)
        {
            Doubles(writer, cell.InitialMass);
        }
    }

    private static StateSnapshot ReadBody(BinaryReader reader)
    {
        var schema = reader.ReadString();
        if (!string.Equals(schema, StateSnapshot.CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new FormatException($"Snapshot schema '{schema}' is not supported in a binary save.");
        }

        var engine = reader.ReadString();
        var pipeline = reader.ReadString();
        var prng = reader.ReadString();
        ScenarioIdentity? scenario = reader.ReadBoolean()
            ? new ScenarioIdentity(reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadBoolean())
            : null;
        var configuration = new SimulationConfiguration(
            reader.ReadInt32(),
            reader.ReadInt32(),
            reader.ReadDouble(),
            reader.ReadUInt64(),
            reader.ReadInt32(),
            reader.ReadBoolean());
        var tick = reader.ReadInt64();
        var next = reader.ReadInt64();
        var random = new RngStreamState(Stream(reader), Stream(reader), Stream(reader), Stream(reader), Stream(reader));
        var columns = Count(reader, 64);
        for (var i = 0; i < columns; i++)
        {
            var name = reader.ReadString();
            if (i >= ResourceColumns.Names.Count || !string.Equals(name, ResourceColumns.Names[i], StringComparison.Ordinal))
            {
                throw new FormatException("The save resource columns do not match this engine.");
            }
        }

        if (columns != ResourceColumns.Names.Count)
        {
            throw new FormatException("The save resource columns do not match this engine.");
        }

        var cells = new CellStateImage[Count(reader, 1 << 24)];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = ReadCell(reader);
        }

        var populations = new PopulationImage[Count(reader, 1 << 26)];
        for (var i = 0; i < populations.Length; i++)
        {
            var id = reader.ReadInt64();
            var organism = reader.ReadString();
            var cell = reader.ReadInt32();
            var genes = new GeneExpression[Count(reader, 4096)];
            for (var g = 0; g < genes.Length; g++)
            {
                genes[g] = new GeneExpression(new GeneId(reader.ReadString()), reader.ReadDouble());
            }

            populations[i] = new PopulationImage(
                id,
                organism,
                cell,
                genes,
                reader.ReadDouble(),
                reader.ReadDouble(),
                reader.ReadDouble(),
                reader.ReadDouble(),
                reader.ReadDouble());
        }

        return new StateSnapshot(schema, engine, pipeline, prng, scenario, configuration, tick, random, cells, populations)
        {
            NextPopulationId = next,
        };
    }

    private static CellStateImage ReadCell(BinaryReader reader)
    {
        var scope = reader.ReadBoolean() ? reader.ReadString() : null;
        var temperature = OptionalDouble(reader);
        var pressure = OptionalDouble(reader);
        var uv = OptionalDouble(reader);
        var absorbed = reader.ReadDouble();
        var equivalent = reader.ReadDouble();
        var irradiance = reader.ReadDouble();
        var ice = reader.ReadDouble();
        var vapor = OptionalDouble(reader);
        var porosity = OptionalDouble(reader);
        var perchlorate = OptionalDouble(reader);
        var perchloratePercent = reader.ReadDouble();
        var icePercent = reader.ReadDouble();
        var deposits = ReadDoubles(reader);
        var demands = ReadDoubles(reader);
        var budget = reader.ReadDouble();
        var initial = reader.ReadBoolean() ? ReadDoubles(reader) : null;
        return new CellStateImage(
            scope, temperature, pressure, uv, absorbed, equivalent, irradiance, ice, vapor, porosity,
            perchlorate, perchloratePercent, icePercent, deposits, demands, budget, initial);
    }

    private static IEnumerable<RandomState> Streams(RngStreamState state)
        => [state.Environment, state.Mutation, state.Reproduction, state.GeneticDrift, state.Events];

    private static RandomState Stream(BinaryReader reader) => new(reader.ReadUInt64(), reader.ReadUInt64());

    private static int Count(BinaryReader reader, int limit)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > limit)
        {
            throw new FormatException($"Count {count} is outside [0, {limit}].");
        }

        return count;
    }

    private static void Optional(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            writer.Write(value);
        }
    }

    private static void Optional(BinaryWriter writer, double? value)
    {
        writer.Write(value.HasValue);
        if (value is { } number)
        {
            writer.Write(number);
        }
    }

    private static double? OptionalDouble(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadDouble() : null;

    private static void Doubles(BinaryWriter writer, IReadOnlyList<double> values)
    {
        writer.Write(values.Count);
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static double[] ReadDoubles(BinaryReader reader)
    {
        var values = new double[Count(reader, 1024)];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadDouble();
        }

        return values;
    }
}
