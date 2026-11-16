using System.Buffers.Binary;

namespace Martis.Audio;

/// <summary>16-bit PCM RIFF/WAVE encoder, used to export the procedural sounds for listening and review.</summary>
public static class Wav
{
    public static short[] ToPcm16(ReadOnlySpan<float> samples)
    {
        var pcm = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            pcm[i] = float.IsFinite(sample) ? (short)MathF.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue) : (short)0;
        }

        return pcm;
    }

    public static byte[] Encode(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (interleaved.Length % channels != 0)
        {
            throw new ArgumentException("The sample count is not a multiple of the channel count.", nameof(interleaved));
        }

        var pcm = ToPcm16(interleaved);
        var dataBytes = pcm.Length * 2;
        var bytes = new byte[44 + dataBytes];
        var span = bytes.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);
        for (var i = 0; i < pcm.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + (2 * i))..], pcm[i]);
        }

        return bytes;
    }
}
