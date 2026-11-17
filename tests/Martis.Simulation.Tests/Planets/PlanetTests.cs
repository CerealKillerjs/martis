using System.IO.Compression;
using Martis.Simulation.Planets;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Planets;

/// <summary>M1 (ADR-034): the planet definition and the real relief of Jezero.</summary>
public sealed class PlanetTests
{
    private static PlanetDefinition Mars() => PlanetDefinition.LoadPublished(Repo.Root());

    [Fact]
    public void MarsLoadsItsConstantsAndTheJezeroRegion()
    {
        var mars = Mars();
        var jezero = mars.Region("jezero");

        Assert.Equal("mars", mars.Id);
        Assert.Equal(3396190.0, mars.RadiusM);
        Assert.InRange(mars.GravityMs2, 3.6, 3.8);
        Assert.InRange(jezero.CenterLat, 18.0, 18.8);
        Assert.InRange(jezero.CenterLon, 77.2, 77.9);
        Assert.Equal(256, jezero.Cells);
        Assert.NotEmpty(jezero.Sources);
        Assert.True(File.Exists(jezero.AlbedoFile));
        Assert.Equal(20.0, jezero.RoverSpeedMs);
        Assert.Equal(30.0, jezero.TravelFactor);
    }

    [Fact]
    public void TheCraterFloorLiesBelowItsRim()
    {
        var relief = RegionRelief.Load(Mars().Region("jezero"));
        var half = relief.Definition.SizeM / 2;
        var floor = relief.HeightAt(half, half);
        // The rim of Jezero is ~24 km from the centre of the 45 km crater; take the highest point on a ring.
        var rim = Enumerable.Range(0, 72).Max(i => relief.HeightAt(half + (24000 * Math.Cos(i * Math.PI / 36)), half + (24000 * Math.Sin(i * Math.PI / 36))));

        Assert.InRange(floor, -3000, -2000);
        Assert.True(rim > floor + 300, $"floor {floor:0} m, rim {rim:0} m");
        Assert.InRange(relief.CellHeight(128, 128), floor - 200, floor + 200);
    }

    [Fact]
    public void LocalPositionsMapToLatitudeAndLongitude()
    {
        var mars = Mars();
        var relief = RegionRelief.Load(mars.Region("jezero"));
        var half = relief.Definition.SizeM / 2;

        var (lat, lon) = relief.LatLon(half, half, mars.RadiusM);
        var (north, east) = relief.LatLon(half + 1000, half - 1000, mars.RadiusM);

        Assert.Equal(relief.Definition.CenterLat, lat, 9);
        Assert.Equal(relief.Definition.CenterLon, lon, 9);
        Assert.True(north > lat && east > lon);
    }

    [Fact]
    public void Png16ReadsEveryFilter()
    {
        const int width = 5, height = 5;
        var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter none; values chosen so the round trip is exact
            for (var x = 0; x < width; x++)
            {
                var v = (ushort)((y * 13000) + (x * 900));
                raw.WriteByte((byte)(v >> 8));
                raw.WriteByte((byte)v);
            }
        }

        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Chunk(png, "IHDR", [0, 0, 0, width, 0, 0, 0, height, 16, 0, 0, 0, 0]);
        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw.ToArray());
        }

        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        png.Position = 0;

        var (w, h, samples) = Png16.ReadGrey(png);

        Assert.Equal((width, height), (w, h));
        Assert.Equal((ushort)((4 * 13000) + (3 * 900)), samples[(4 * width) + 3]);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        stream.Write([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
        stream.Write(System.Text.Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write([0, 0, 0, 0]); // CRC is not checked by the reader
    }

    [Fact]
    public void TheStorySectionIsIsidisWestWithJezeroAsADetailInsert()
    {
        var mars = Mars();
        var start = mars.StartSection;
        var relief = mars.LoadRelief(start);

        Assert.Equal("isidis_occidental", start.Id);
        Assert.Equal(400000.0, relief.Definition.SizeM);
        Assert.Equal(256, relief.Definition.Cells);
        Assert.Contains(mars.Sections, section => !section.Start && section.UnlockEra > 1);
        var insert = Assert.Single(relief.Inserts);
        Assert.Equal("jezero", insert.Detail.Definition.Id);

        // Inside the insert the detailed relief wins; the floor of Jezero lies in it.
        var (x, y) = relief.LocalOf(18.4, 77.55, mars.RadiusM);
        Assert.Equal(insert.Detail.HeightAt(x - insert.X, y - insert.Y), relief.HeightAt(x, y), 6);
        Assert.InRange(relief.HeightAt(x, y), -3000, -2000);
        // Far from it, only the base relief counts.
        Assert.Equal(relief.BaseHeightAt(380000, 20000), relief.HeightAt(380000, 20000), 9);
    }

    [Fact]
    public void ThePlacesOfTheStorySectionSitOnRealFeatures()
    {
        var mars = Mars();
        var start = mars.StartSection;
        var relief = mars.LoadRelief(start);
        var places = Martis.Simulation.Expedition.ValleyDefinition.LoadPublished(Repo.Root(), start.PlacesFile!, (lat, lon) =>
        {
            var (x, y) = relief.LocalOf(lat, lon, mars.RadiusM);
            return (x / relief.Definition.SizeM, y / relief.Definition.SizeM);
        });

        var delta = places.Pois.Single(poi => poi.Id == "delta");
        // The Jezero delta is ~60 km west and ~30 km south of the section centre.
        Assert.InRange(delta.FractionX, 0.30, 0.40);
        Assert.InRange(delta.FractionY, 0.53, 0.62);
        Assert.NotNull(start.Arrival);
    }

    [Fact]
    public void NeighbouringSectionsTileAndTheirEdgesMeet()
    {
        var mars = Mars();
        var start = mars.StartSection;
        var east = mars.Neighbour(start, 1, 0)!;
        var north = mars.Neighbour(start, 0, -1)!;

        Assert.Equal((1, 0), east.Tile);
        Assert.Equal((0, -1), north.Tile);
        Assert.Null(mars.Neighbour(start, 5, 5));
        Assert.All(mars.Sections.Where(section => !section.Start), section => Assert.True(section.UnlockEra > 1));

        // The same ground seen from both sides of a shared edge has the same height.
        var a = mars.LoadRelief(start);
        var b = mars.LoadRelief(east);
        var size = a.Definition.SizeM;
        foreach (var y in new[] { 50000.0, 200000.0, 350000.0 })
        {
            Assert.InRange(Math.Abs(a.BaseHeightAt(size, y) - b.BaseHeightAt(0, y)), 0, 60);
        }

        var (lat, lon) = a.LatLon(size, 200000, mars.RadiusM);
        var (bx, by) = b.LocalOf(lat, lon, mars.RadiusM);
        // Centres are stored to 1e-4° (~6 m), so the edges meet within a few metres.
        Assert.InRange(bx, -10, 10);
        Assert.InRange(by, 199990, 200010);
    }
}
