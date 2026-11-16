namespace Martis.Domain.Common;

/// <summary>
/// One simulation tick is one sol (ADR-011, H-05).
/// </summary>
public static class MartianTime
{
    /// <summary>
    /// Seconds in one sol. This is the only copy of the figure.
    /// TODO: cita exacta NSSDC pendiente de MARTIS Ciencia.
    /// </summary>
    public const int SecondsPerSol = 88775;
}
