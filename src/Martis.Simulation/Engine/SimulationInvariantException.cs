using Martis.Domain.Validation;

namespace Martis.Simulation.Engine;

/// <summary>
/// A tick finished its systems and <see cref="SimulationValidator"/> found violations.
/// The tick counter is left unchanged. <see cref="Violations"/> is the full list.
/// </summary>
public sealed class SimulationInvariantException : InvalidOperationException
{
    public SimulationInvariantException(long tick, IReadOnlyList<InvariantViolation> violations)
        : base(Describe(tick, violations))
    {
        ArgumentNullException.ThrowIfNull(violations);
        Tick = tick;
        Violations = violations;
    }

    public long Tick { get; }

    public IReadOnlyList<InvariantViolation> Violations { get; }

    private static string Describe(long tick, IReadOnlyList<InvariantViolation> violations)
    {
        var listed = string.Join(
            "; ",
            violations.Select(violation =>
                $"{violation.InvariantId} cell {violation.CellIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"} {violation.Quantity}"));
        return $"Tick {tick.ToString(System.Globalization.CultureInfo.InvariantCulture)} failed validation: {listed}";
    }
}
