namespace Martis.Domain.Environment;

/// <summary>
/// Tolerance of TDD §10.1 for a closed mass account:
/// the absolute error must be at most
/// <see cref="MassBalanceAbsoluteToleranceKg"/> plus
/// <see cref="MassBalanceRelativeTolerance"/> times the absolute expected mass.
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
    /// The figure is pending; zero leaves only the relative term in force.
    /// </summary>
    public const double MassBalanceAbsoluteToleranceKg = 0;
}
