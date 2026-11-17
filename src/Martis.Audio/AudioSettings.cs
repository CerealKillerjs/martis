using System.Globalization;
using System.Text;

namespace Martis.Audio;

/// <summary>The audio buses of the game (ADR-018): Master feeds the speakers; the others feed Master.</summary>
public enum AudioBus
{
    Master,
    Music,
    Effects,
    Ambience,
}

/// <summary>
/// Player volume settings, linear 0–1 per bus, stored as <c>key=value</c> lines
/// (user://audio.cfg). Invalid values keep the previous value.
/// </summary>
public sealed record AudioSettings
{
    public const float SilentDecibels = -80f;

    private float _master = 0.9f;
    private float _music = 0.7f;
    private float _effects = 0.8f;
    private float _ambience = 0.6f;

    public float Master { get => _master; set => _master = Clamp(value, _master); }

    public float Music { get => _music; set => _music = Clamp(value, _music); }

    public float Effects { get => _effects; set => _effects = Clamp(value, _effects); }

    public float Ambience { get => _ambience; set => _ambience = Clamp(value, _ambience); }

    public bool Muted { get; set; }

    public float this[AudioBus bus]
    {
        get => bus switch
        {
            AudioBus.Master => Master,
            AudioBus.Music => Music,
            AudioBus.Effects => Effects,
            AudioBus.Ambience => Ambience,
            _ => throw new ArgumentOutOfRangeException(nameof(bus)),
        };
        set
        {
            switch (bus)
            {
                case AudioBus.Master: Master = value; break;
                case AudioBus.Music: Music = value; break;
                case AudioBus.Effects: Effects = value; break;
                case AudioBus.Ambience: Ambience = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(bus));
            }
        }
    }

    /// <summary>Linear amplitude to decibels, with silence at <see cref="SilentDecibels"/>.</summary>
    public static float ToDecibels(float linear)
        => linear <= 1e-4f ? SilentDecibels : MathF.Max(SilentDecibels, 20f * MathF.Log10(linear));

    /// <summary>The volume to set on a bus; the mute switch silences Master only.</summary>
    public float BusDecibels(AudioBus bus)
        => bus == AudioBus.Master && Muted ? SilentDecibels : ToDecibels(this[bus]);

    public string Serialize()
    {
        var text = new StringBuilder();
        foreach (var bus in Enum.GetValues<AudioBus>())
        {
            text.Append(Key(bus)).Append('=').Append(this[bus].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        text.Append("muted=").Append(Muted ? "true" : "false").Append('\n');
        return text.ToString();
    }

    public static AudioSettings Parse(string text)
    {
        var settings = new AudioSettings();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (line.StartsWith('#') || equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key == "muted")
            {
                if (bool.TryParse(value, out var muted))
                {
                    settings.Muted = muted;
                }

                continue;
            }

            foreach (var bus in Enum.GetValues<AudioBus>())
            {
                if (key == Key(bus) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
                {
                    settings[bus] = volume;
                }
            }
        }

        return settings;
    }

    private static string Key(AudioBus bus) => bus switch
    {
        AudioBus.Master => "master",
        AudioBus.Music => "music",
        AudioBus.Effects => "effects",
        _ => "ambience",
    };

    private static float Clamp(float value, float fallback)
        => float.IsNaN(value) ? fallback : Math.Clamp(value, 0f, 1f);
}
