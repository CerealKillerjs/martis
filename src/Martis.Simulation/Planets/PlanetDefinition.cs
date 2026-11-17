using System.IO.Compression;
using System.Text.Json;
using Martis.Domain.Parameters;

namespace Martis.Simulation.Planets;

/// <summary>
/// One planet (ARC-003, M1, ADR-034): physical constants and the regions that have real relief.
/// Loaded from <c>data/planets/&lt;id&gt;.v1.json</c>; every region lives in its own folder with the
/// files written by <c>tools/planet/build_region.py</c>.
/// </summary>
public sealed record PlanetDefinition(string Id, string Name, double RadiusM, double GravityMs2, double SolSeconds, IReadOnlyList<RegionDefinition> Regions)
{
    public const string MarsFile = "mars.v1.json";

    /// <summary>The planet split in playable sections (M3, ADR-036); the story mode opens them one by one.</summary>
    public IReadOnlyList<SectionDefinition> Sections { get; init; } = [];

    /// <summary>The section a new story game starts in.</summary>
    public SectionDefinition StartSection => Sections.FirstOrDefault(section => section.Start)
        ?? throw new InvalidOperationException($"Planet '{Id}' has no start section.");

    public SectionDefinition Section(string id)
        => Sections.FirstOrDefault(section => string.Equals(section.Id, id, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Planet '{Id}' has no section '{id}'.");

    /// <summary>The relief of a section with data: its region plus its detail inserts.</summary>
    public RegionRelief LoadRelief(SectionDefinition section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.RegionId is null)
        {
            throw new InvalidOperationException($"Section '{section.Id}' has no relief data yet.");
        }

        var relief = RegionRelief.Load(Region(section.RegionId));
        foreach (var insert in section.Inserts)
        {
            relief.WithInsert(RegionRelief.Load(Region(insert)), RadiusM);
        }

        return relief;
    }

    public RegionDefinition Region(string id)
        => Regions.FirstOrDefault(region => string.Equals(region.Id, id, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Planet '{Id}' has no region '{id}'.");

    public static PlanetDefinition LoadPublished(string repositoryRoot, string fileName = MarsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var folder = Path.Combine(repositoryRoot, "data", "planets");
        return Load(File.ReadAllText(Path.Combine(folder, fileName)), folder);
    }

    public static PlanetDefinition Load(string json, string folder)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.planet")
        {
            throw new InvalidDataException("A planet file needs metadata.schema = martis.planet.");
        }

        var planet = meta.GetProperty("planet");
        double Number(JsonElement fields, string name) => parameters.RequireNumber(fields.GetProperty(name).GetString()!).Value;
        var regions = new List<RegionDefinition>();
        foreach (var region in meta.GetProperty("regions").EnumerateArray())
        {
            var regionFolder = Path.Combine(folder, region.GetProperty("folder").GetString()!);
            regions.Add(RegionDefinition.Load(File.ReadAllText(Path.Combine(regionFolder, "region.json")), regionFolder, Number(region, "cell_m")) with
            {
                RoverSpeedMs = Number(region, "rover_speed_m_s"),
                TravelFactor = Number(region, "travel_factor"),
            });
        }

        var sections = new List<SectionDefinition>();
        if (meta.TryGetProperty("sections", out var sectionArray))
        {
            foreach (var section in sectionArray.EnumerateArray())
            {
                sections.Add(new SectionDefinition(
                    section.GetProperty("id").GetString()!,
                    section.GetProperty("name").GetString()!,
                    section.GetProperty("lat").GetDouble(),
                    section.GetProperty("lon").GetDouble(),
                    section.GetProperty("size_km").GetDouble() * 1000.0,
                    section.TryGetProperty("region", out var region) ? region.GetString() : null,
                    section.TryGetProperty("inserts", out var inserts) ? inserts.EnumerateArray().Select(item => item.GetString()!).ToArray() : [],
                    section.TryGetProperty("start", out var start) && start.GetBoolean(),
                    section.GetProperty("unlock_era").GetInt32(),
                    section.GetProperty("unlock").GetString()!,
                    section.GetProperty("summary").GetString()!)
                {
                    Arrival = section.TryGetProperty("arrival", out var arrival)
                        ? (arrival.GetProperty("lat").GetDouble(), arrival.GetProperty("lon").GetDouble(), arrival.GetProperty("name").GetString()!)
                        : null,
                    PlacesFile = section.TryGetProperty("places", out var places) ? places.GetString() : null,
                });
            }
        }

        var definition = new PlanetDefinition(
            meta.GetProperty("id").GetString()!,
            meta.GetProperty("name").GetString()!,
            Number(planet, "radius_m"),
            Number(planet, "gravity_m_s2"),
            Number(planet, "sol_s"),
            regions) { Sections = sections };
        foreach (var section in sections)
        {
            if (section.RegionId is not null)
            {
                definition.Region(section.RegionId);
            }

            foreach (var insert in section.Inserts)
            {
                definition.Region(insert);
            }
        }

        if (sections.Count > 0 && sections.Count(section => section.Start) != 1)
        {
            throw new InvalidDataException("A planet needs exactly one start section.");
        }

        if (sections.Select(section => section.Id).Distinct(StringComparer.Ordinal).Count() != sections.Count)
        {
            throw new InvalidDataException("Section ids must be unique.");
        }

        return definition;
    }
}

/// <summary>
/// A playable section of a planet (M3, ADR-036): a square centred on a point, opened by the story
/// mode at an era. <see cref="RegionId"/> is null while its relief has not been prepared yet.
/// </summary>
public sealed record SectionDefinition(
    string Id,
    string Name,
    double CenterLat,
    double CenterLon,
    double SizeM,
    string? RegionId,
    IReadOnlyList<string> Inserts,
    bool Start,
    int UnlockEra,
    string Unlock,
    string Summary)
{
    public bool HasRelief => RegionId is not null;

    /// <summary>Where the AI's lander arrives in a new story game (latitude, longitude), if set.</summary>
    public (double Lat, double Lon, string Name)? Arrival { get; init; }

    /// <summary>The section's points of interest (a <c>martis.valley</c> file in data/expedition), if any.</summary>
    public string? PlacesFile { get; init; }
}

/// <summary>A region with real relief: a square of <see cref="SizeM"/> metres centred on a point of the planet.</summary>
public sealed record RegionDefinition(
    string Id,
    string Name,
    double CenterLat,
    double CenterLon,
    double SizeM,
    int HeightPixels,
    double MinHeightM,
    double MaxHeightM,
    string HeightFile,
    string AlbedoFile,
    int AlbedoPixels,
    double CellM,
    IReadOnlyList<string> Sources)
{
    /// <summary>Rover speed when driven at real scale (M2), metres per second.</summary>
    public double RoverSpeedMs { get; init; } = 20.0;

    /// <summary>How much faster the rover goes on a move order (fast travel, M2).</summary>
    public double TravelFactor { get; init; } = 30.0;

    /// <summary>Scientific cells per side of the region.</summary>
    public int Cells => (int)Math.Round(SizeM / CellM);

    public static RegionDefinition Load(string json, string folder, double cellM)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellM);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "martis.region")
        {
            throw new InvalidDataException("A region file needs schema = martis.region.");
        }

        var height = root.GetProperty("height");
        var albedo = root.GetProperty("albedo");
        var definition = new RegionDefinition(
            root.GetProperty("id").GetString()!,
            root.GetProperty("name").GetString()!,
            root.GetProperty("center").GetProperty("lat").GetDouble(),
            root.GetProperty("center").GetProperty("lon").GetDouble(),
            root.GetProperty("size_m").GetDouble(),
            height.GetProperty("pixels").GetInt32(),
            height.GetProperty("min_m").GetDouble(),
            height.GetProperty("max_m").GetDouble(),
            Path.Combine(folder, height.GetProperty("file").GetString()!),
            Path.Combine(folder, albedo.GetProperty("file").GetString()!),
            albedo.GetProperty("pixels").GetInt32(),
            cellM,
            root.GetProperty("sources").EnumerateArray().Select(source => $"{source.GetProperty("name").GetString()} — {source.GetProperty("by").GetString()} ({source.GetProperty("license").GetString()})").ToArray());
        if (definition.SizeM <= 0 || definition.HeightPixels < 2 || definition.MaxHeightM <= definition.MinHeightM)
        {
            throw new InvalidDataException($"Region '{definition.Id}' has an invalid size or height range.");
        }

        return definition;
    }
}

/// <summary>
/// The real relief of a region in local metres: x grows east, y grows south (the map's row
/// order), both from 0 at the north-west corner to <see cref="RegionDefinition.SizeM"/>.
/// </summary>
public sealed class RegionRelief
{
    private readonly ushort[] _samples;
    private readonly List<Insert> _inserts = [];

    private RegionRelief(RegionDefinition definition, ushort[] samples)
    {
        Definition = definition;
        _samples = samples;
    }

    public RegionDefinition Definition { get; }

    public int Pixels => Definition.HeightPixels;

    public double SpacingM => Definition.SizeM / (Pixels - 1);

    public static RegionRelief Load(RegionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        using var stream = File.OpenRead(definition.HeightFile);
        var (width, height, samples) = Png16.ReadGrey(stream);
        if (width != definition.HeightPixels || height != definition.HeightPixels)
        {
            throw new InvalidDataException($"Region '{definition.Id}': the height map is {width}×{height}, expected {definition.HeightPixels}².");
        }

        return new RegionRelief(definition, samples);
    }

    /// <summary>Height in metres above the areoid of a grid sample.</summary>
    public double Sample(int px, int py)
    {
        px = Math.Clamp(px, 0, Pixels - 1);
        py = Math.Clamp(py, 0, Pixels - 1);
        return Definition.MinHeightM + (_samples[(py * Pixels) + px] / 65535.0 * (Definition.MaxHeightM - Definition.MinHeightM));
    }

    /// <summary>Detail regions blended over this one (M3, ADR-036), with their placement.</summary>
    public IReadOnlyList<Insert> Inserts => _inserts;

    /// <summary>
    /// Blends a smaller, more detailed region over this one (M3, ADR-036): inside its square the
    /// detail wins, fading into this relief over <paramref name="featherM"/> metres at its edges.
    /// The two are placed by their centres on the planet's sphere.
    /// </summary>
    public RegionRelief WithInsert(RegionRelief detail, double radiusM, double featherM = 3000)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusM);
        var (x, y) = LocalOf(detail.Definition.CenterLat, detail.Definition.CenterLon, radiusM);
        var half = detail.Definition.SizeM / 2;
        _inserts.Add(new Insert(detail, x - half, y - half, Math.Clamp(featherM, 1, half)));
        return this;
    }

    /// <summary>Local position (metres from the north-west corner) of a latitude and longitude.</summary>
    public (double X, double Y) LocalOf(double lat, double lon, double radiusM)
    {
        var half = Definition.SizeM / 2;
        var x = half + ((lon - Definition.CenterLon) * Math.PI / 180.0 * radiusM * Math.Cos(Definition.CenterLat * Math.PI / 180.0));
        var y = half - ((lat - Definition.CenterLat) * Math.PI / 180.0 * radiusM);
        return (x, y);
    }

    /// <summary>Height in metres at a local position: this relief, with any detail insert blended in.</summary>
    public double HeightAt(double xM, double yM)
    {
        var height = BaseHeightAt(xM, yM);
        foreach (var insert in _inserts)
        {
            var w = insert.Weight(xM, yM);
            if (w > 0)
            {
                height = (height * (1 - w)) + (insert.Detail.HeightAt(xM - insert.X, yM - insert.Y) * w);
            }
        }

        return height;
    }

    /// <summary>Bilinear height in metres of this relief alone (clamped to the region).</summary>
    public double BaseHeightAt(double xM, double yM)
    {
        var fx = Math.Clamp(xM / SpacingM, 0.0, Pixels - 1.000001);
        var fy = Math.Clamp(yM / SpacingM, 0.0, Pixels - 1.000001);
        var ix = (int)fx;
        var iy = (int)fy;
        var tx = fx - ix;
        var ty = fy - iy;
        var top = (Sample(ix, iy) * (1 - tx)) + (Sample(ix + 1, iy) * tx);
        var bottom = (Sample(ix, iy + 1) * (1 - tx)) + (Sample(ix + 1, iy + 1) * tx);
        return (top * (1 - ty)) + (bottom * ty);
    }

    /// <summary>Mean height of one scientific cell.</summary>
    public double CellHeight(int cx, int cy)
    {
        var cell = Definition.SizeM / Definition.Cells;
        var sum = 0.0;
        for (var j = 0; j < 4; j++)
        {
            for (var i = 0; i < 4; i++)
            {
                sum += HeightAt((cx + ((i + 0.5) / 4)) * cell, (cy + ((j + 0.5) / 4)) * cell);
            }
        }

        return sum / 16;
    }

    /// <summary>Latitude and longitude (degrees) of a local position, on the planet's sphere.</summary>
    public (double Lat, double Lon) LatLon(double xM, double yM, double radiusM)
    {
        var half = Definition.SizeM / 2;
        var lat = Definition.CenterLat - ((yM - half) / radiusM * 180.0 / Math.PI);
        var lon = Definition.CenterLon + ((xM - half) / (radiusM * Math.Cos(Definition.CenterLat * Math.PI / 180.0)) * 180.0 / Math.PI);
        return (lat, lon);
    }
}

/// <summary>A detail region placed inside a larger one: north-west corner in the larger region's metres.</summary>
public sealed record Insert(RegionRelief Detail, double X, double Y, double FeatherM)
{
    /// <summary>1 well inside the detail square, falling to 0 at its edge.</summary>
    public double Weight(double xM, double yM)
    {
        var size = Detail.Definition.SizeM;
        var edge = Math.Min(Math.Min(xM - X, X + size - xM), Math.Min(yM - Y, Y + size - yM));
        if (edge <= 0)
        {
            return 0;
        }

        var t = Math.Min(1, edge / FeatherM);
        return t * t * (3 - (2 * t));
    }
}

/// <summary>A small reader for the 16-bit greyscale PNGs the region tool writes (no interlace).</summary>
public static class Png16
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static (int Width, int Height, ushort[] Samples) ReadGrey(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new BinaryReader(stream);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG file.");
        }

        int width = 0, height = 0;
        using var data = new MemoryStream();
        while (true)
        {
            var length = ReadInt(reader);
            var type = new string(reader.ReadChars(4));
            var chunk = reader.ReadBytes(length);
            reader.ReadBytes(4); // CRC
            if (type == "IHDR")
            {
                width = (chunk[0] << 24) | (chunk[1] << 16) | (chunk[2] << 8) | chunk[3];
                height = (chunk[4] << 24) | (chunk[5] << 16) | (chunk[6] << 8) | chunk[7];
                if (chunk[8] != 16 || chunk[9] != 0 || chunk[12] != 0)
                {
                    throw new InvalidDataException("Only 16-bit greyscale, non-interlaced PNGs are supported.");
                }
            }
            else if (type == "IDAT")
            {
                data.Write(chunk);
            }
            else if (type == "IEND")
            {
                break;
            }
        }

        data.Position = 0;
        using var inflate = new ZLibStream(data, CompressionMode.Decompress);
        var stride = width * 2;
        var previous = new byte[stride];
        var current = new byte[stride];
        var samples = new ushort[width * height];
        for (var y = 0; y < height; y++)
        {
            var filter = inflate.ReadByte();
            inflate.ReadExactly(current);
            Unfilter(filter, current, previous);
            for (var x = 0; x < width; x++)
            {
                samples[(y * width) + x] = (ushort)((current[x * 2] << 8) | current[(x * 2) + 1]);
            }

            (previous, current) = (current, previous);
        }

        return (width, height, samples);
    }

    private static int ReadInt(BinaryReader reader)
    {
        var b = reader.ReadBytes(4);
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    private static void Unfilter(int filter, byte[] row, byte[] above)
    {
        const int bpp = 2;
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= bpp ? row[i - bpp] : 0;
            int up = above[i];
            int corner = i >= bpp ? above[i - bpp] : 0;
            row[i] = filter switch
            {
                0 => row[i],
                1 => (byte)(row[i] + left),
                2 => (byte)(row[i] + up),
                3 => (byte)(row[i] + ((left + up) >> 1)),
                4 => (byte)(row[i] + Paeth(left, up, corner)),
                _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
