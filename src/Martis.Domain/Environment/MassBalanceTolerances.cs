namespace Martis.Domain.Environment;

/// <summary>
/// Tolerance of TDD §10.1 for a closed mass account:
/// the absolute error must be at most
/// <see cref="MassBalanceAbsoluteToleranceKg"/> plus
/// <see cref="MassBalanceRelativeTolerance"/> times the absolute recorded total.
/// Both figures are MODEL_ASSUMPTION. MARTIS Lead fixed the relative term.
/// </summary>
public static class MassBalanceTolerances
{
    /// <summary>
    /// Relative term. MODEL_ASSUMPTION, fixed by MARTIS Lead.
    /// </summary>
    public const double MassBalanceRelativeTolerance = 1e-9;

    /// <summary>
    /// Absolute term, in kg. MODEL_ASSUMPTION.
    /// It sits above the rounding residue (about machine epsilon times the
    /// maximum cell mass; with stocks below about 1e3 kg, about 1e-13 kg)
    /// and below any water mass that would matter. Review it with H-07.
    /// </summary>
    public const double MassBalanceAbsoluteToleranceKg = 1e-12;
}
