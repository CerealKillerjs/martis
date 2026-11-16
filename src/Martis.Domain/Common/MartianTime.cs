namespace Martis.Domain.Common;

/// <summary>
/// One simulation tick is one sol (ADR-011, H-05).
/// </summary>
public static class MartianTime
{
    /// <summary>
    /// One tick is one sol. This integer is a model rounding (MODEL_ASSUMPTION)
    /// of the mean solar day. The measurement and the contrast are in ADR-011.
    /// This is the only copy of the figure in source.
    /// </summary>
    public const int SecondsPerSol = 88775;
}
