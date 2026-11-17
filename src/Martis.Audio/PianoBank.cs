namespace Martis.Audio;

/// <summary>
/// Recorded grand piano (ADR-018): a Steinway model B from the Versilian Community Sample
/// Library (CC0 1.0, https://github.com/sgossner/VCSL), sampled every whole tone from
/// A♯1 to C7 with the sustain pedal down, in two velocity layers. The samples are summed
/// to mono and stored as IMA ADPCM in <c>Assets/piano.bank</c>, built by
/// <c>tools/piano-bank/build_bank.py</c>.
/// <para>Format (little-endian): "MPB1", sample rate, block size, entry count; then per
/// entry: MIDI note (byte), layer (byte), length in samples (int32) and its blocks. Each block
/// starts with the decoder state (int16 predictor, byte step index, byte reserved) followed
/// by block-size 4-bit codes, low nibble first.</para>
/// </summary>
public sealed class PianoBank
{
    private static readonly int[] Steps =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66,
        73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408,
        449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
        2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630,
        9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    ];

    private static readonly int[] IndexChange = [-1, -1, -1, -1, 2, 4, 6, 8];
    private static readonly Lazy<PianoBank> Shared = new(Load);
    private readonly Dictionary<(int Midi, int Layer), short[]> _samples;

    private PianoBank(int sampleRate, Dictionary<(int Midi, int Layer), short[]> samples)
    {
        SampleRate = sampleRate;
        _samples = samples;
        Notes = samples.Keys.Select(k => k.Midi).Distinct().Order().ToArray();
        Layers = samples.Keys.Select(k => k.Layer).Distinct().Order().ToArray();
    }

    /// <summary>The bank embedded in this assembly, decoded once on first use.</summary>
    public static PianoBank Instance => Shared.Value;

    public int SampleRate { get; }

    /// <summary>Sampled MIDI notes, ascending.</summary>
    public IReadOnlyList<int> Notes { get; }

    /// <summary>Velocity layers, softest first.</summary>
    public IReadOnlyList<int> Layers { get; }

    public short[] Sample(int midi, int layer) => _samples[(midi, layer)];

    /// <summary>The sampled note closest to <paramref name="midi"/>.</summary>
    public int Nearest(int midi)
    {
        var best = Notes[0];
        foreach (var note in Notes)
        {
            if (Math.Abs(note - midi) < Math.Abs(best - midi))
            {
                best = note;
            }
        }

        return best;
    }

    public static PianoBank Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "MPB1")
        {
            throw new InvalidDataException("Not a piano bank.");
        }

        var sampleRate = reader.ReadInt32();
        var block = reader.ReadInt32();
        var count = reader.ReadInt32();
        var samples = new Dictionary<(int, int), short[]>();
        var codes = new byte[block / 2];
        for (var e = 0; e < count; e++)
        {
            int midi = reader.ReadByte();
            int layer = reader.ReadByte();
            var pcm = new short[reader.ReadInt32()];
            for (var start = 0; start < pcm.Length; start += block)
            {
                int predictor = reader.ReadInt16();
                int index = reader.ReadByte();
                reader.ReadByte();
                reader.BaseStream.ReadExactly(codes);
                var end = Math.Min(block, pcm.Length - start);
                for (var i = 0; i < end; i++)
                {
                    var code = (i & 1) == 0 ? codes[i >> 1] & 0xF : codes[i >> 1] >> 4;
                    var step = Steps[index];
                    var delta = step >> 3;
                    if ((code & 4) != 0)
                    {
                        delta += step;
                    }

                    if ((code & 2) != 0)
                    {
                        delta += step >> 1;
                    }

                    if ((code & 1) != 0)
                    {
                        delta += step >> 2;
                    }

                    predictor = Math.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
                    index = Math.Clamp(index + IndexChange[code & 7], 0, 88);
                    pcm[start + i] = (short)predictor;
                }
            }

            samples[(midi, layer)] = pcm;
        }

        return new PianoBank(sampleRate, samples);
    }

    private static PianoBank Load()
    {
        using var stream = typeof(PianoBank).Assembly.GetManifestResourceStream("Martis.Audio.piano.bank")
            ?? throw new InvalidOperationException("The piano bank is not embedded in Martis.Audio.");
        return Read(stream);
    }
}
