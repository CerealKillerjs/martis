using Martis.Contracts.Commands;
using Martis.Simulation.Biology;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Snapshots;

/// <summary>T07: the binary save format (ADR-004, ADR-015).</summary>
public sealed class StateSnapshotBinaryTests
{
    [Fact]
    public void TheBinaryFormatRoundTripsTheWholeState()
    {
        var snapshot = Snapshot(30);

        var decoded = StateSnapshotBinary.Read(StateSnapshotBinary.Write(snapshot));

        Assert.Equal(StateSnapshotJson.Write(snapshot), StateSnapshotJson.Write(decoded));
        Assert.Equal(snapshot.NextPopulationId, decoded.NextPopulationId);
        Assert.NotEmpty(decoded.Populations);
    }

    [Fact]
    public void TheHeaderIsMagicVersionFlagsLengthAndChecksum()
    {
        var bytes = StateSnapshotBinary.Write(Snapshot(1));

        Assert.Equal("MRTS"u8.ToArray(), bytes[..4]);
        Assert.Equal(new byte[] { 1, 0, 0, 0 }, bytes[4..8]);
        Assert.Equal(bytes.Length - StateSnapshotBinary.HeaderLength, BitConverter.ToInt64(bytes, 8));
    }

    [Fact]
    public void AnyFlippedBodyByteIsDetected()
    {
        var bytes = StateSnapshotBinary.Write(Snapshot(5));
        foreach (var position in new[] { StateSnapshotBinary.HeaderLength, bytes.Length / 2, bytes.Length - 1 })
        {
            var copy = (byte[])bytes.Clone();
            copy[position] ^= 0x01;
            Assert.Throws<SnapshotCorruptException>(() => StateSnapshotBinary.Read(copy));
        }
    }

    [Fact]
    public void BadHeadersAreRejectedWithAClearError()
    {
        var bytes = StateSnapshotBinary.Write(Snapshot(1));
        var magic = (byte[])bytes.Clone();
        magic[0] = (byte)'X';
        var version = (byte[])bytes.Clone();
        version[4] = 9;

        Assert.Contains("MRTS", Assert.Throws<SnapshotCorruptException>(() => StateSnapshotBinary.Read(magic)).Message, StringComparison.Ordinal);
        Assert.Contains("format 9", Assert.Throws<SnapshotCorruptException>(() => StateSnapshotBinary.Read(version)).Message, StringComparison.Ordinal);
        Assert.Throws<SnapshotCorruptException>(() => StateSnapshotBinary.Read(bytes.AsSpan(0, bytes.Length - 3)));
        Assert.Throws<SnapshotCorruptException>(() => StateSnapshotBinary.Read([]));
    }

    [Fact]
    public void AVersionOneJsonSnapshotIsStillReadable()
    {
        var snapshot = Snapshot(3) with { SchemaVersion = StateSnapshot.SchemaVersionV1 };
        var text = StateSnapshotJson.Write(snapshot);

        var read = StateSnapshotJson.Read(text);

        Assert.Equal(StateSnapshot.SchemaVersionV1, read.SchemaVersion);
        Assert.Equal(0, read.NextPopulationId);
    }

    private static StateSnapshot Snapshot(int ticks)
    {
        var catalog = BiologyCatalog.LoadPublished(Repo.Root());
        var scenario = MarsBaselineScenario.LoadPublished(Repo.Root());
        var (engine, _) = MarsBaselineWorld.CreateWithBiology(scenario, catalog, 99UL, 8, 8);
        engine.ApplyCommand(new IntroducePopulationCommand("phototroph_a01", 3, 0.2));
        engine.Advance(ticks);
        return StateSnapshot.Capture(engine, null);
    }
}
