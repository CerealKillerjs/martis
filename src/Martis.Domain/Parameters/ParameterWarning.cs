namespace Martis.Domain.Parameters;

/// <summary>
/// A parameter does not belong to the regional cell that would use it (R5-07).
/// The loader does not rewrite the value. The caller still has to decide.
/// <see cref="CellSite"/> is the site of that cell, not the parameter's own scope.
/// </summary>
public sealed record ParameterWarning(string ParameterId, string Message, string CellSite);
