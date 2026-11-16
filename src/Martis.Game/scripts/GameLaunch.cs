using System;
using Godot;
using Martis.Audio;

namespace Martis.Game;

/// <summary>
/// Autoload that carries the title-screen choice into the game scene and fades between
/// scenes. Other scripts may read it without the type: <c>GetNode("/root/GameLaunch").Get("Mode")</c>
/// returns <c>"Campaign"</c>, <c>"Sandbox"</c> or <c>"Load"</c>; <c>ChapterId</c> is <c>""</c> or <c>"ch1"</c>…
/// </summary>
public partial class GameLaunch : CanvasLayer
{
    public const string Campaign = "Campaign";
    public const string Sandbox = "Sandbox";
    public const string LoadLatest = "Load";
    public const string TitleScene = "res://scenes/Title.tscn";
    public const string GameScene = "res://scenes/Main.tscn";

    private ColorRect _veil = null!;
    private bool _busy;

    public static GameLaunch? Instance { get; private set; }

    /// <summary>How the game scene should start. Sandbox when the game scene is run directly.</summary>
    [Export]
    public string Mode { get; set; } = Sandbox;

    /// <summary>Campaign chapter to open, or empty for the chapter menu.</summary>
    [Export]
    public string ChapterId { get; set; } = string.Empty;

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
        Layer = 100;
        ProcessMode = ProcessModeEnum.Always;
        _veil = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_veil);
    }

    /// <summary>Fades to black, switches to the game scene with the chosen mode, fades back in.</summary>
    public void StartGame(string mode, string chapterId = "")
    {
        Mode = mode;
        ChapterId = chapterId;
        Go(GameScene);
    }

    public void ToTitle() => Go(TitleScene);

    /// <summary>Fades the screen in from black (for scenes that open themselves).</summary>
    public void Reveal(double seconds = 1.2)
    {
        _veil.Modulate = Colors.White;
        CreateTween().TweenProperty(_veil, "modulate:a", 0f, seconds).SetTrans(Tween.TransitionType.Sine);
    }

    private async void Go(string scene)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _veil.MouseFilter = Control.MouseFilterEnum.Stop;
        GameAudio.Instance?.Play(Sfx.Transition);
        var tween = CreateTween();
        tween.TweenProperty(_veil, "modulate:a", 1f, 0.7).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(tween, Tween.SignalName.Finished);
        GetTree().Paused = false;
        var error = GetTree().ChangeSceneToFile(scene);
        if (error != Error.Ok)
        {
            GD.PushError($"No se pudo abrir {scene}: {error}");
        }

        await ToSignal(GetTree(), SceneTree.SignalName.SceneChanged);
        _veil.MouseFilter = Control.MouseFilterEnum.Ignore;
        var back = CreateTween();
        back.TweenProperty(_veil, "modulate:a", 0f, 1.1).SetTrans(Tween.TransitionType.Sine);
        _busy = false;
    }

    /// <summary>True when the command line asks for a headless or scripted game run.</summary>
    public static bool ScriptedRun()
        => Array.Exists(OS.GetCmdlineUserArgs(), arg => arg.StartsWith("--smoke", StringComparison.Ordinal) || arg.StartsWith("--shot", StringComparison.Ordinal));
}
