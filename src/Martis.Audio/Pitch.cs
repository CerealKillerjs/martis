namespace Martis.Audio;

/// <summary>Church modes, as semitone steps from the root.</summary>
public enum Mode
{
    Ionian,
    Dorian,
    Phrygian,
    Lydian,
    Mixolydian,
    Aeolian,
}

/// <summary>Equal temperament on A4 = 440 Hz and modal scale degrees.</summary>
public static class Pitch
{
    private static readonly int[][] Steps =
    [
        [0, 2, 4, 5, 7, 9, 11],
        [0, 2, 3, 5, 7, 9, 10],
        [0, 1, 3, 5, 7, 8, 10],
        [0, 2, 4, 6, 7, 9, 11],
        [0, 2, 4, 5, 7, 9, 10],
        [0, 2, 3, 5, 7, 8, 10],
    ];

    public static double Hertz(double midi) => 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0);

    /// <summary>MIDI note of a scale degree; degree 7 is the octave, negative degrees go down.</summary>
    public static int Degree(int root, Mode mode, int degree)
    {
        var octave = (int)Math.Floor(degree / 7.0);
        var index = degree - (octave * 7);
        return root + (12 * octave) + Steps[(int)mode][index];
    }
}
