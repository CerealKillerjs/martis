namespace Martis.Simulation.Scenarios;

/// <summary>
/// The scenario document cannot be applied to a world.
/// <see cref="ParameterId"/> is set when the failure belongs to one parameter.
/// <see cref="FieldName"/> is set when the failure belongs to a field-map key.
/// The message names that id or key. No default value is invented.
/// </summary>
public sealed class ScenarioLoadException : InvalidOperationException
{
    public ScenarioLoadException(string message, string? parameterId = null, string? fieldName = null)
        : base(message)
    {
        ParameterId = parameterId;
        FieldName = fieldName;
    }

    public string? ParameterId { get; }

    public string? FieldName { get; }
}
