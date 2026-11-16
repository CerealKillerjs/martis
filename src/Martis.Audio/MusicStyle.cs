namespace Martis.Audio;

/// <summary>Moods of the soundtrack (ADR-018), chosen by <see cref="MusicDirector"/>.</summary>
public enum MusicMood
{
    Title,
    Day,
    Night,
    Thriving,
    Crisis,
    Victory,
}

/// <summary>Accompaniment layer under the pads and bells.</summary>
public enum PulseKind
{
    None,
    Arpeggio,
    Heartbeat,
}

/// <summary>
/// Everything the generative composer needs to play a mood: key, tempo, chord progression
/// (scale degrees) and the level of each layer. Values are art direction, not science.
/// </summary>
public sealed record MusicStyle(
    int Root,
    Mode Mode,
    float Bpm,
    int[] Progression,
    float PadCutoffHz,
    float PadLevel,
    float BellDensity,
    int BellOctave,
    float DroneLevel,
    PulseKind Pulse,
    float PulseLevel)
{
    private static readonly int[] TitleProgression = [0, 4, 5, 3];
    private static readonly int[] DayProgression = [0, 5, 3, 4];
    private static readonly int[] NightProgression = [0, 5, 2, 6];
    private static readonly int[] ThrivingProgression = [0, 4, 1, 5];
    private static readonly int[] CrisisProgression = [0, 1, 0, 6];
    private static readonly int[] VictoryProgression = [0, 4, 5, 3];

    private static readonly MusicStyle[] Base =
    [
        new(50, Mode.Lydian, 64f, TitleProgression, 2600f, 1f, 0.32f, 2, 0.45f, PulseKind.None, 0f),
        new(52, Mode.Dorian, 72f, DayProgression, 2000f, 0.9f, 0.24f, 2, 0.35f, PulseKind.None, 0f),
        new(45, Mode.Aeolian, 56f, NightProgression, 900f, 0.85f, 0.14f, 3, 0.5f, PulseKind.None, 0f),
        new(55, Mode.Mixolydian, 84f, ThrivingProgression, 2800f, 0.85f, 0.4f, 2, 0.3f, PulseKind.Arpeggio, 0.35f),
        new(48, Mode.Phrygian, 60f, CrisisProgression, 520f, 0.9f, 0.04f, 1, 0.85f, PulseKind.Heartbeat, 0.7f),
        new(53, Mode.Lydian, 76f, VictoryProgression, 3200f, 1f, 0.55f, 2, 0.35f, PulseKind.Arpeggio, 0.3f),
    ];

    /// <summary>
    /// Style for a mood, coloured by the campaign chapter: each chapter moves the key and,
    /// in exploration moods, the mode, so the five chapters of «Génesis» sound different.
    /// </summary>
    public static MusicStyle For(MusicMood mood, string? chapter)
    {
        var style = Base[(int)mood];
        var (shift, mode) = chapter switch
        {
            "ch2" => (2, (Mode?)Mode.Dorian),
            "ch3" => (5, Mode.Lydian),
            "ch4" => (-2, Mode.Aeolian),
            "ch5" => (7, Mode.Lydian),
            _ => (0, null),
        };

        if (shift == 0)
        {
            return style;
        }

        var exploring = mood is MusicMood.Day or MusicMood.Thriving;
        return style with { Root = style.Root + shift, Mode = exploring && mode is { } m ? m : style.Mode };
    }
}
