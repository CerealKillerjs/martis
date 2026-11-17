namespace Martis.Contracts.Snapshots;

/// <summary>
/// Immutable view of one cell (TDD §8.1). Unset quantities are null.
/// A null is not the number 0. Doses are separate and are not added together.
/// </summary>
public sealed record CellSnapshot(
    int CellIndex,
    double? TemperatureKelvin,
    double? PressurePascal,
    double? UvRadiation,
    double IonizingAbsorbedDoseGyPerSol,
    double IonizingEquivalentDoseSvPerSol,
    double SolarIrradiance,
    double WaterIceKg,
    double? WaterVaporKg,
    double? SoilPorosity,
    double? PerchlorateMassKg,
    double? PerchlorateWtPercent,
    double? WaterIceWtPercent,
    string? Scope,
    int PopulationCount,
    double WaterAvailableKg,
    double OrganicCarbonKg);

/// <summary>
/// Immutable view of one aggregated population (TDD §8.1).
/// <see cref="MeanFitness"/> is null in v0: fitness is not computed.
/// <see cref="GeneticVariantCount"/> is 1 because the population holds one genome.
/// Variants are not split in this version.
/// </summary>
public sealed record PopulationSnapshot(
    long Id,
    string OrganismId,
    double Abundance,
    double BiomassKg,
    double? MeanFitness,
    int GeneticVariantCount);

/// <summary>
/// Counts copied by value from the world at the moment of the snapshot.
/// Later edits of the live world do not change this record.
/// </summary>
public sealed record StatisticsSnapshot(long Tick, int CellCount, int PopulationCount);

/// <summary>
/// Immutable view of a run (TDD §8.1, S1-28). It does not carry the PRNG.
/// The environment-and-PRNG round trip lives in the simulation assembly.
/// </summary>
public sealed record SimulationSnapshot(
    long Tick,
    string ScenarioName,
    string DataVersion,
    bool SepEnabled,
    IReadOnlyList<CellSnapshot> Cells,
    IReadOnlyList<PopulationSnapshot> Populations,
    StatisticsSnapshot Statistics);
