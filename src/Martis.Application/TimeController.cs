namespace Martis.Application;

/// <summary>
/// Game time against real time (V1, Documento Maestro v2.0 §3.2 and §23). The player asks for a
/// speed; the mode may cap it (exploration keeps it readable) and the effective speed is the
/// smaller one. <see cref="Advance"/> turns real seconds into sols, at most one per call, so
/// the host steps the simulation on its main thread one tick per frame (ADR-000 R4).
/// Pausing stops time coherently: no sol runs while paused.
/// </summary>
public sealed class TimeController
{
    /// <summary>Speed index 3: eight sols per real second.</summary>
    public const int FastestSpeed = 3;

    private static readonly double[] SecondsPerSol = [double.PositiveInfinity, 2.0, 0.5, 0.125];

    private int _cap = FastestSpeed;

    /// <summary>The speed the player chose: 0 pause, 1 one sol every 2 s, 2 two sols/s, 3 eight sols/s.</summary>
    public int RequestedSpeed { get; private set; } = 1;

    public int EffectiveSpeed => Math.Min(RequestedSpeed, _cap);

    public bool Paused => EffectiveSpeed == 0;

    /// <summary>Fraction of the current sol already elapsed, in [0, 1).</summary>
    public double SolProgress { get; private set; }

    public static double RealSecondsPerSol(int speed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(speed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(speed, FastestSpeed);
        return SecondsPerSol[speed];
    }

    public void Request(int speed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(speed);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(speed, FastestSpeed);
        RequestedSpeed = speed;
    }

    /// <summary>Space: pause, or resume at normal speed.</summary>
    public void TogglePause() => Request(EffectiveSpeed == 0 ? 1 : 0);

    /// <summary>Applies a mode's limit; the request is remembered and comes back when the cap lifts.</summary>
    public void Cap(int maxSpeed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxSpeed);
        _cap = Math.Min(maxSpeed, FastestSpeed);
    }

    /// <summary>Places the clock inside the sol (screenshots, loading).</summary>
    public void SetProgress(double fraction)
    {
        if (!double.IsFinite(fraction))
        {
            throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "The sol fraction must be finite.");
        }

        SolProgress = fraction - Math.Floor(fraction);
    }

    /// <summary>Real seconds elapsed; returns 1 when a sol is due (the host advances the simulation), else 0.</summary>
    public int Advance(double realSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(realSeconds);
        if (Paused)
        {
            return 0;
        }

        SolProgress += realSeconds / SecondsPerSol[EffectiveSpeed];
        if (SolProgress < 1.0)
        {
            return 0;
        }

        // One sol per call at most; any extra is dropped so a slow frame never runs a burst.
        SolProgress = Math.Min(SolProgress - 1.0, 0.999);
        return 1;
    }
}
