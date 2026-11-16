namespace Martis.Domain.Environment;

/// <summary>
/// One named scalar, in the declaration order of the type that owns it.
/// The validator walks these entries so a new field cannot be skipped by accident.
/// </summary>
public readonly record struct NamedQuantity(string Name, double Value);
