namespace Martis.Simulation.Missions;

/// <summary>
/// How much the campaign helps in a chapter (H8, ADR-028). The help fades chapter by
/// chapter so the player ends up terraforming on their own.
/// </summary>
public enum GuidanceLevel
{
    /// <summary>Guided steps: the hint of the active objective is always shown (chapter 1).</summary>
    Tutorial,

    /// <summary>Hints always shown, objectives in any order (chapter 2).</summary>
    Hints,

    /// <summary>The hint appears when the player asks for it or after a short stall (chapter 3).</summary>
    OnRequest,

    /// <summary>No hint button; the hint appears only after a long stall (chapter 4).</summary>
    Minimal,

    /// <summary>No hints at all: the player is on their own (chapter 5 and the epilogue).</summary>
    None,
}

/// <summary>Sols without progress after which a hint appears by itself (parameters of the campaign file).</summary>
public sealed record GuidancePolicy(double OnRequestStallSols, double MinimalStallSols)
{
    public static readonly GuidancePolicy Default = new(30.0, 90.0);
}

/// <summary>
/// Decides when the HUD shows the hint of the active objective (H8). The host calls
/// <see cref="Observe"/> after every tick. Any progress on the mission (an objective
/// completed or a progress bar moving) resets the stall counter; a new active objective
/// also clears a hint the player asked for.
/// </summary>
public sealed class MissionGuidance
{
    private string? _activeId;
    private double _progress;
    private long _lastProgressTick;
    private bool _requested;
    private bool _started;

    /// <param name="startTick">Tick the mission started at; the stall counter runs from it.</param>
    public MissionGuidance(GuidanceLevel level, GuidancePolicy? policy = null, long startTick = 0)
    {
        Level = level;
        Policy = policy ?? GuidancePolicy.Default;
        _lastProgressTick = startTick;
    }

    public GuidanceLevel Level { get; }

    public GuidancePolicy Policy { get; }

    /// <summary>Whether the HUD offers a «Pista» button.</summary>
    public bool CanRequest => Level == GuidanceLevel.OnRequest;

    /// <summary>Hints the player asked for in this mission.</summary>
    public int Requests { get; private set; }

    /// <summary>Sols since the mission last moved forward, as of the last <see cref="Observe"/>.</summary>
    public long StalledSols { get; private set; }

    public void Observe(MissionTracker mission, long tick)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var active = mission.Status().FirstOrDefault(status => status.Active);
        var progress = mission.Status().Sum(status => status.CompletedTick is null ? status.Progress / Math.Max(status.Target, 1e-12) : 1.0);
        if (!_started)
        {
            _started = true;
            _activeId = active?.Id;
        }
        else if (active?.Id != _activeId)
        {
            _activeId = active?.Id;
            _requested = false;
            _lastProgressTick = tick;
        }
        else if (progress > _progress + 1e-9)
        {
            _lastProgressTick = tick;
        }

        _progress = progress;
        StalledSols = Math.Max(0, tick - _lastProgressTick);
    }

    /// <summary>The player pressed «Pista». Returns false when this level offers no button.</summary>
    public bool Request()
    {
        if (!CanRequest)
        {
            return false;
        }

        _requested = true;
        Requests++;
        return true;
    }

    /// <summary>Whether the hint of the active objective is visible now.</summary>
    public bool ShowsHint => Level switch
    {
        GuidanceLevel.Tutorial or GuidanceLevel.Hints => true,
        GuidanceLevel.OnRequest => _requested || StalledSols >= Policy.OnRequestStallSols,
        GuidanceLevel.Minimal => StalledSols >= Policy.MinimalStallSols,
        _ => false,
    };
}
