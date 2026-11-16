using System;
using System.Collections.Generic;
using Godot;
using Martis.Audio;

namespace Martis.Game;

/// <summary>
/// Audio autoload (ADR-018). Creates the Music, Effects and Ambience buses, streams the
/// generative soundtrack and the wind through <see cref="AudioStreamGenerator"/>s, plays the
/// pre-rendered effects from a voice pool and gives every button a hover and click sound.
/// Volumes live in <c>user://audio.cfg</c>. Everything is synthesized by Martis.Audio.
/// </summary>
public partial class GameAudio : Node
{
    private const int Chunk = 512;
    private const int Voices = 12;
    private const string SettingsPath = "user://audio.cfg";

    /// <summary>Seconds a new mood must hold before the music follows it.</summary>
    private const double MoodHold = 3.0;

    private readonly Dictionary<Sfx, AudioStreamWav> _effects = [];
    private readonly Dictionary<Sfx, ulong> _lastPlayed = [];
    private readonly List<AudioStreamPlayer> _voices = [];
    private readonly Vector2[] _frames = new Vector2[Chunk];
    private readonly float[] _samples = new float[Chunk * 2];
    private MusicEngine _music = null!;
    private WindEngine _wind = null!;
    private AudioStreamGeneratorPlayback? _musicPlayback;
    private AudioStreamGeneratorPlayback? _windPlayback;
    private MusicMood _pending;
    private double _pendingFor;
    private double _celebrate;
    private int _nextVoice;

    public static GameAudio? Instance { get; private set; }

    public AudioSettings Settings { get; private set; } = new();

    public MusicMood Mood => _music.Mood;

    public float WindStrength
    {
        get => _wind.Strength;
        set => _wind.Strength = value;
    }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        foreach (var bus in new[] { AudioBus.Music, AudioBus.Effects, AudioBus.Ambience })
        {
            EnsureBus(bus);
        }

        LoadSettings();
        foreach (var sfx in Enum.GetValues<Sfx>())
        {
            _effects[sfx] = ToStream(SfxSynth.Render(sfx));
        }

        for (var i = 0; i < Voices; i++)
        {
            var voice = new AudioStreamPlayer { Bus = BusName(AudioBus.Effects) };
            AddChild(voice);
            _voices.Add(voice);
        }

        var seed = (ulong)Time.GetTicksUsec();
        _music = new MusicEngine(seed, MusicMood.Title);
        _pending = MusicMood.Title;
        _wind = new WindEngine(seed + 1) { Strength = 0.35f };
        _musicPlayback = StartStream(AudioBus.Music);
        _windPlayback = StartStream(AudioBus.Ambience);
        GetTree().NodeAdded += Decorate;
    }

    public override void _Process(double delta)
    {
        if (_celebrate > 0)
        {
            _celebrate -= delta;
        }

        if (_pending != _music.Mood)
        {
            _pendingFor += delta;
            if (_pendingFor >= MoodHold || _pending is MusicMood.Victory or MusicMood.Title)
            {
                _music.SetMood(_pending);
            }
        }

        Fill(_musicPlayback, _music.Render);
        Fill(_windPlayback, _wind.Render);
    }

    /// <summary>Plays an effect. Repeats of the same effect within 40 ms are dropped.</summary>
    public void Play(Sfx sfx, float volumeDb = 0f, float pitch = 1f)
    {
        var now = Time.GetTicksMsec();
        if (_lastPlayed.TryGetValue(sfx, out var last) && now - last < 40)
        {
            return;
        }

        _lastPlayed[sfx] = now;
        var voice = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Count;
        voice.Stream = _effects[sfx];
        voice.VolumeDb = volumeDb;
        voice.PitchScale = pitch;
        voice.Play();
    }

    /// <summary>The music director's view of the game; called by the game every frame.</summary>
    public void Observe(MusicContext context)
    {
        var mood = MusicDirector.Choose(context with { Celebrating = context.Celebrating || _celebrate > 0 });
        if (mood != _pending)
        {
            _pending = mood;
            _pendingFor = 0;
        }
    }

    /// <summary>Plays the victory theme for a while (mission banners).</summary>
    public void Celebrate(double seconds = 24) => _celebrate = seconds;

    public void SetChapter(string? chapter) => _music.SetChapter(chapter);

    public void Apply(AudioSettings settings)
    {
        Settings = settings;
        foreach (var bus in Enum.GetValues<AudioBus>())
        {
            AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(BusName(bus)), settings.BusDecibels(bus));
        }
    }

    public void SaveSettings()
    {
        using var file = Godot.FileAccess.Open(SettingsPath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(Settings.Serialize());
    }

    public static string BusName(AudioBus bus) => bus.ToString();

    private void LoadSettings()
    {
        var settings = new AudioSettings();
        if (Godot.FileAccess.FileExists(SettingsPath))
        {
            using var file = Godot.FileAccess.Open(SettingsPath, Godot.FileAccess.ModeFlags.Read);
            if (file is not null)
            {
                settings = AudioSettings.Parse(file.GetAsText());
            }
        }

        Apply(settings);
    }

    private static void EnsureBus(AudioBus bus)
    {
        var name = BusName(bus);
        if (AudioServer.GetBusIndex(name) >= 0)
        {
            return;
        }

        AudioServer.AddBus();
        var index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, BusName(AudioBus.Master));
    }

    private AudioStreamGeneratorPlayback? StartStream(AudioBus bus)
    {
        var player = new AudioStreamPlayer
        {
            Bus = BusName(bus),
            Stream = new AudioStreamGenerator { MixRate = MusicEngine.SampleRate, BufferLength = 1.0f },
        };
        AddChild(player);
        player.Play();
        return player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    private void Fill(AudioStreamGeneratorPlayback? playback, SpanRenderer render)
    {
        if (playback is null)
        {
            return;
        }

        // Never more than a quarter of a second per frame, so a long hitch cannot snowball.
        var budget = MusicEngine.SampleRate / 4;
        while (budget >= Chunk && playback.GetFramesAvailable() >= Chunk)
        {
            render(_samples);
            for (var i = 0; i < Chunk; i++)
            {
                _frames[i] = new Vector2(_samples[2 * i], _samples[(2 * i) + 1]);
            }

            playback.PushBuffer(_frames);
            budget -= Chunk;
        }
    }

    private delegate void SpanRenderer(Span<float> interleaved);

    private void Decorate(Node node)
    {
        if (node is BaseButton button && !button.HasMeta("silent"))
        {
            button.MouseEntered += () =>
            {
                if (!button.Disabled)
                {
                    Play(Sfx.UiHover, -4f);
                }
            };
            button.Pressed += () => Play(button is CheckButton or CheckBox || (button.ToggleMode && !button.ButtonPressed) ? Sfx.UiBack : Sfx.UiClick);
        }
        else if (node is Slider slider)
        {
            slider.DragEnded += _ => Play(Sfx.UiClick, -6f, 1.3f);
        }
    }

    private static AudioStreamWav ToStream(float[] samples)
    {
        var pcm = Wav.ToPcm16(samples);
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SfxSynth.SampleRate,
            Stereo = false,
            Data = bytes,
        };
    }
}
