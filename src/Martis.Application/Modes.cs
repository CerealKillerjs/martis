namespace Martis.Application;

/// <summary>
/// Ways of being present in the same world (Documento Maestro v2.0 §3.1). The interstellar
/// mode belongs to era IV and is not offered yet.
/// </summary>
public enum GameMode
{
    /// <summary>Third-person control of a robot: move, inspect, sample, scan.</summary>
    Exploration = 0,

    /// <summary>Free camera over map layers: populations, zones, orders.</summary>
    Strategic = 1,

    /// <summary>Scientific station: designs, experiments and analyses.</summary>
    Laboratory = 2,

    /// <summary>Globe and global balances.</summary>
    Planetary = 3,
}

/// <summary>A change of mode, for the presentation to animate.</summary>
public readonly record struct ModeChange(GameMode From, GameMode To);

/// <summary>
/// Which mode is showing (V1, ADR-027). Modes are presentations of one world (§3.2): a switch
/// never touches the simulation, and the context (selection, focus, layer) is kept as it is,
/// so going back to exploration finds the robot where it was. <see cref="Back"/> returns to
/// the previous mode, as Esc does from the laboratory or the planetary view.
/// </summary>
public sealed class ModeController
{
    private GameMode _previous;

    public ModeController(GameMode start = GameMode.Strategic)
    {
        Current = start;
        _previous = start;
    }

    public GameMode Current { get; private set; }

    /// <summary>The fastest time-speed index allowed in a mode; exploration keeps time readable (§3.2).</summary>
    public static int MaxSpeed(GameMode mode) => mode == GameMode.Exploration ? 1 : TimeController.FastestSpeed;

    /// <summary>Switches to <paramref name="mode"/>; returns null when it was already showing.</summary>
    public ModeChange? Switch(GameMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode.");
        }

        if (mode == Current)
        {
            return null;
        }

        var change = new ModeChange(Current, mode);
        _previous = Current;
        Current = mode;
        return change;
    }

    /// <summary>Tab: between the robot and the strategic view; from any other mode, back to the robot.</summary>
    public ModeChange? Toggle() => Switch(Current == GameMode.Exploration ? GameMode.Strategic : GameMode.Exploration);

    /// <summary>Returns to the mode shown before the current one.</summary>
    public ModeChange? Back() => Switch(_previous);
}
