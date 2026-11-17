namespace Martis.Audio;

/// <summary>What the music director looks at: a few numbers taken from the session view.</summary>
public sealed record MusicContext
{
    public bool OnTitle { get; init; }

    /// <summary>Fraction of the sol, 0 = midnight, 0.5 = noon (World3D.TimeOfDay).</summary>
    public float TimeOfDay { get; init; }

    public double BiomassKg { get; init; }

    /// <summary>Relative biomass change per sol (0.05 = +5 % per sol).</summary>
    public double BiomassGrowthPerSol { get; init; }

    public bool Failure { get; init; }

    /// <summary>Real seconds per sol; infinity when paused. Fast sols make the sun useless as a clock.</summary>
    public double SecondsPerSol { get; init; } = double.PositiveInfinity;

    /// <summary>A mission was just completed and the banner is on screen.</summary>
    public bool Celebrating { get; init; }
}

/// <summary>Chooses the soundtrack mood from the game state (ADR-018).</summary>
public static class MusicDirector
{
    public const double ThrivingGrowth = 0.02;
    public const double ThrivingBiomassKg = 0.5;
    public const double CollapseGrowth = -0.1;
    public const double SlowSolSeconds = 30;

    public static MusicMood Choose(MusicContext context)
    {
        if (context.OnTitle)
        {
            return MusicMood.Title;
        }

        if (context.Celebrating)
        {
            return MusicMood.Victory;
        }

        if (context.Failure || (context.BiomassKg > 0 && context.BiomassGrowthPerSol <= CollapseGrowth))
        {
            return MusicMood.Crisis;
        }

        if (context.BiomassKg >= ThrivingBiomassKg && context.BiomassGrowthPerSol >= ThrivingGrowth)
        {
            return MusicMood.Thriving;
        }

        if (context.BiomassKg <= 0)
        {
            // Nothing alive yet: the sparse nocturnal theme of an empty planet.
            return MusicMood.Night;
        }

        if (context.SecondsPerSol < SlowSolSeconds)
        {
            return MusicMood.Day;
        }

        return context.TimeOfDay is >= 0.24f and <= 0.76f ? MusicMood.Day : MusicMood.Night;
    }

    public static double Growth(double previousKg, double currentKg, int sols)
    {
        if (sols <= 0)
        {
            return 0;
        }

        if (previousKg <= 0)
        {
            return currentKg > 0 ? 1 : 0;
        }

        return (currentKg - previousKg) / previousKg / sols;
    }
}
