namespace Martis.Domain.Parameters;

/// <summary>
/// A planetary-mean value was associated with a regional cell.
/// The loader does not rewrite the value. The caller still has to decide.
/// </summary>
public sealed record ParameterWarning(string ParameterId, string Message);
