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

/// <summary>How the piano's left hand accompanies the melody.</summary>
public enum LeftHand
{
    /// <summary>One low note or open fifth per bar.</summary>
    Sparse,

    /// <summary>Slow broken chord: root, fifth, tenth.</summary>
    Broken,

    /// <summary>Flowing eighth-note arpeggio.</summary>
    Flowing,
}

/// <summary>
/// Everything the generative composer needs to play a mood: key, tempo, chord progression
/// (scale degrees), how busy the piano is, the rests between phrases and the level of the
/// soft pad and drone underneath. Values are art direction, not science.
/// </summary>
public sealed record MusicStyle(
    int Root,
    Mode Mode,
    float Bpm,
    int[] Progression,
    float MelodyDensity,
    int MelodyOctave,
    LeftHand LeftHand,
    float RestSeconds,
    float PadCutoffHz,
    float PadLevel,
    float DroneLevel,
    float HeartbeatLevel)
{
    /// <summary>True for the title screen: it plays the written <see cref="TitleTheme"/>
    /// instead of generated phrases.</summary>
    public bool Scored { get; init; }

    private static readonly int[] TitleProgression = [0, 3, 5, 4];
    private static readonly int[] DayProgression = [0, 5, 3, 4];
    private static readonly int[] NightProgression = [0, 5, 3, 6];
    private static readonly int[] ThrivingProgression = [0, 4, 5, 3];
    private static readonly int[] CrisisProgression = [0, 1, 0, 6];
    private static readonly int[] VictoryProgression = [0, 4, 5, 3];

    private static readonly MusicStyle[] Base =
    [
        new(50, Mode.Aeolian, 54f, TitleProgression, 0.5f, 1, LeftHand.Broken, 4.5f, 1300f, 0f, 0f, 0f) { Scored = true },
        new(55, Mode.Ionian, 72f, DayProgression, 0.55f, 1, LeftHand.Broken, 5f, 1200f, 0.4f, 0.1f, 0f),
        new(50, Mode.Aeolian, 58f, NightProgression, 0.3f, 1, LeftHand.Sparse, 7f, 800f, 0.45f, 0.3f, 0f),
        new(57, Mode.Lydian, 80f, ThrivingProgression, 0.75f, 1, LeftHand.Flowing, 3f, 1600f, 0.35f, 0.1f, 0f),
        new(48, Mode.Phrygian, 56f, CrisisProgression, 0.25f, 0, LeftHand.Sparse, 4f, 520f, 0.6f, 0.8f, 0.6f),
        new(53, Mode.Ionian, 76f, VictoryProgression, 0.8f, 1, LeftHand.Flowing, 2.5f, 1800f, 0.45f, 0.1f, 0f),
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
            "ch3" => (-3, Mode.Lydian),
            "ch4" => (-2, Mode.Aeolian),
            "ch5" => (5, Mode.Mixolydian),
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
