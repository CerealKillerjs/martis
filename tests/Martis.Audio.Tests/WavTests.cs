using System.Buffers.Binary;
using System.Text;

namespace Martis.Audio.Tests;

public sealed class WavTests
{
    [Fact]
    public void EncodesA16BitPcmHeader()
    {
        var bytes = Wav.Encode([0f, 0.5f, -0.5f, 1f], channels: 2, sampleRate: 44100);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(20)));
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22)));
        Assert.Equal(44100, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)));
        Assert.Equal(44100 * 4, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(28)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34)));
        Assert.Equal("data", Encoding.ASCII.GetString(bytes, 36, 4));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)));
        Assert.Equal(44 + 8, bytes.Length);
        Assert.Equal(bytes.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
    }

    [Fact]
    public void ClampsSamplesOutsideTheRange()
    {
        var pcm = Wav.ToPcm16([2f, -2f, 0f, float.NaN]);
        Assert.Equal([short.MaxValue, (short)-short.MaxValue, (short)0, (short)0], pcm);
    }

    [Fact]
    public void RejectsBadChannelCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Wav.Encode([0f], channels: 0, sampleRate: 44100));
        Assert.Throws<ArgumentException>(() => Wav.Encode([0f, 0f, 0f], channels: 2, sampleRate: 44100));
    }
}
