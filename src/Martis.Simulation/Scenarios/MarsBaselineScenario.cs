using System.Globalization;
using System.Text.Json;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.Parameters;
using Martis.Domain.World;

namespace Martis.Simulation.Scenarios;

/// <summary>
/// Loads MarsBaseline and writes every cell from the scenario documents.
/// Science values come only from <c>metadata.v0_field_map</c>.
/// The cell scope comes from <c>metadata.scope_id</c>, not from the free-text scope.
/// Kilogram deposits come from the separate GAME_BALANCE document.
/// Water-ice kilograms are not derived from <see cref="EnvironmentState.WaterIceWtPercent"/>.
/// Perchlorate weight percent is not converted to kilograms.
/// Ids that are not in the field map or the deposit map are not delivered.
/// This type does not relax the generic catalog. A document the catalog refuses
/// still fails here, with the catalog's error.
/// </summary>
public sealed class MarsBaselineScenario
{
    public const string ScienceFileName = "mars_baseline_gale.v0.json";

    public const string DepositsFileName = "game_balance_deposits.v0.json";

    private const string SepEnabledField = "SepEnabled";

    private readonly ParameterCatalog _science;
    private readonly ParameterCatalog _deposits;
    private readonly Dictionary<string, SourcedValue<double>> _scienceById;
    private readonly Dictionary<string, SourcedValue<double>> _depositsById;
    private readonly string[] _scienceIds;
    private readonly string[] _depositIds;
    private readonly double[] _depositKilograms;
    private readonly EnvironmentNumbers _environment;

    private MarsBaselineScenario(
        ParameterCatalog science,
        ParameterCatalog deposits,
        string name,
        string dataVersion,
        string cellSite,
        EnvironmentScope scope,
        SourcedValue<bool> sepEnabled,
        EnvironmentNumbers environment,
        IReadOnlyList<SourcedValue<double>> deliveredNumbers,
        IReadOnlyList<SourcedValue<double>> depositValues,
        string[] scienceIds,
        string[] depositIds,
        double[] depositKilograms)
    {
        _science = science;
        _deposits = deposits;
        Name = name;
        DataVersion = dataVersion;
        CellSite = cellSite;
        Scope = scope;
        SepEnabledValue = sepEnabled;
        SepEnabled = sepEnabled.Value;
        _environment = environment;
        DeliveredNumbers = deliveredNumbers;
        DepositValues = depositValues;
        _scienceIds = scienceIds;
        _depositIds = depositIds;
        _depositKilograms = depositKilograms;
        _scienceById = Index(deliveredNumbers);
        _depositsById = Index(depositValues);
    }

    public string Name { get; }

    public string DataVersion { get; }

    public string CellSite { get; }

    public EnvironmentScope Scope { get; }

    public bool SepEnabled { get; }

    public SourcedValue<bool> SepEnabledValue { get; }

    /// <summary>Science numbers the field map delivered, in map order.</summary>
    public IReadOnlyList<SourcedValue<double>> DeliveredNumbers { get; }

    /// <summary>Deposit kilograms, in <see cref="CellMassLedger.Names"/> order.</summary>
    public IReadOnlyList<SourcedValue<double>> DepositValues { get; }

    /// <summary>
    /// Warnings from science parameters, then deposit parameters.
    /// <see cref="Apply"/> records them. Reading this before <see cref="Apply"/> is empty.
    /// </summary>
    public IReadOnlyList<ParameterWarning> Warnings
    {
        get
        {
            var combined = new List<ParameterWarning>(_science.Warnings.Count + _deposits.Warnings.Count);
            combined.AddRange(_science.Warnings);
            combined.AddRange(_deposits.Warnings);
            return combined;
        }
    }

    public static MarsBaselineScenario LoadPublished(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var directory = Path.Combine(repositoryRoot, "data", "scenarios", "MarsBaseline");
        var sciencePath = Path.Combine(directory, ScienceFileName);
        var depositsPath = Path.Combine(directory, DepositsFileName);
        if (!File.Exists(sciencePath))
        {
            throw new FileNotFoundException("The MarsBaseline science file is missing.", sciencePath);
        }

        if (!File.Exists(depositsPath))
        {
            throw new FileNotFoundException("The MarsBaseline deposit file is missing.", depositsPath);
        }

        return Load(File.ReadAllText(sciencePath), File.ReadAllText(depositsPath));
    }

    public static MarsBaselineScenario Load(string scienceJson, string depositsJson)
    {
        ArgumentNullException.ThrowIfNull(scienceJson);
        ArgumentNullException.ThrowIfNull(depositsJson);
        var science = ParameterCatalog.Load(scienceJson);
        var deposits = ParameterCatalog.Load(depositsJson);
        using var metadata = JsonDocument.Parse(science.MetadataJson);
        var root = metadata.RootElement;
        var name = RequireString(root, "name", "metadata");
        var dataVersion = ReadVersion(root);
        var cellSite = ReadCellSite(RequireString(root, "scope_id", "metadata"));
        RequireSol(root);

        var fields = ReadFieldMap(root);
        var scienceIds = new List<string>();
        var numbers = new List<SourcedValue<double>>();
        var numberByField = new Dictionary<string, double>(StringComparer.Ordinal);
        SourcedValue<bool>? sepEnabled = null;
        foreach (var field in fields)
        {
            if (field.Unassigned)
            {
                continue;
            }

            var definition = FindDefinition(science, field.Id!, field.Field);
            if (definition.UsedInV0 != true)
            {
                throw new ScenarioLoadException(
                    $"Field '{field.Field}' maps to '{field.Id}', which does not have used_in_v0 true.",
                    field.Id,
                    field.Field);
            }

            if (field.Unit is not null && !string.Equals(field.Unit, definition.Unit, StringComparison.Ordinal))
            {
                throw new ScenarioLoadException(
                    $"Field '{field.Field}' maps to '{field.Id}' with unit '{definition.Unit}', not '{field.Unit}'.",
                    field.Id,
                    field.Field);
            }

            if (string.Equals(field.Field, SepEnabledField, StringComparison.Ordinal))
            {
                sepEnabled = science.RequireBool(field.Id!);
            }
            else
            {
                var sourced = science.RequireNumber(field.Id!);
                numbers.Add(sourced);
                numberByField.Add(field.Field, sourced.Value);
            }

            scienceIds.Add(field.Id!);
        }

        if (sepEnabled is null)
        {
            throw new ScenarioLoadException("SepEnabled is not mapped to a boolean.", fieldName: SepEnabledField);
        }

        var environment = new EnvironmentNumbers(
            RequireMapped(numberByField, nameof(EnvironmentState.TemperatureKelvin)),
            RequireMapped(numberByField, nameof(EnvironmentState.PressurePascal)),
            RequireMapped(numberByField, nameof(EnvironmentState.IonizingAbsorbedDoseGyPerSol)),
            RequireMapped(numberByField, nameof(EnvironmentState.IonizingEquivalentDoseSvPerSol)),
            RequireMapped(numberByField, nameof(EnvironmentState.SolarIrradiance)),
            RequireMapped(numberByField, nameof(EnvironmentState.PerchlorateWtPercent)),
            RequireMapped(numberByField, nameof(EnvironmentState.WaterIceWtPercent)));

        var (depositValues, depositIds, depositKilograms) = ReadDeposits(deposits, scienceIds);
        return new MarsBaselineScenario(
            science,
            deposits,
            name,
            dataVersion,
            cellSite,
            new EnvironmentScope.Regional(new SiteId(cellSite)),
            sepEnabled,
            environment,
            numbers,
            depositValues,
            scienceIds.ToArray(),
            depositIds,
            depositKilograms);
    }

    /// <summary>
    /// Writes the mapped science values and the kilogram deposits into every cell,
    /// in ascending index order. Unassigned fields stay unset.
    /// The initial mass of each cell is recorded from those deposits.
    /// </summary>
    public void Apply(SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        for (var index = 0; index < state.CellCount; index++)
        {
            var environment = state.Environment[index]
                ?? throw new InvalidOperationException($"Environment[{index}] is null.");
            var resources = state.Resources[index]
                ?? throw new InvalidOperationException($"Resources[{index}] is null.");
            var budget = state.WaterBudgets[index]
                ?? throw new InvalidOperationException($"WaterBudgets[{index}] is null.");
            var ledger = state.MassLedgers[index]
                ?? throw new InvalidOperationException($"MassLedgers[{index}] is null.");

            environment.Scope = Scope;
            environment.TemperatureKelvin = AssignedDouble.Set(_environment.TemperatureKelvin);
            environment.PressurePascal = AssignedDouble.Set(_environment.PressurePascal);
            environment.IonizingAbsorbedDoseGyPerSol = _environment.IonizingAbsorbedDoseGyPerSol;
            environment.IonizingEquivalentDoseSvPerSol = _environment.IonizingEquivalentDoseSvPerSol;
            environment.SolarIrradiance = _environment.SolarIrradiance;
            environment.PerchlorateWtPercent = AssignedDouble.Set(_environment.PerchlorateWtPercent);
            environment.WaterIceWtPercent = AssignedDouble.Set(_environment.WaterIceWtPercent);
            AssignDeposits(resources, environment);
            environment.CheckInvariants();
            resources.CheckInvariants();

            // Unset vapor is not read. The zero below is only the sum's identity.
            var vaporKg = 0.0;
            if (environment.WaterVapor.IsSet)
            {
                vaporKg = environment.WaterVapor.Value;
            }

            budget.RecordedTotalKg = CellWaterBudget.ComponentSum(resources.Water, environment.WaterIce, vaporKg);
            ledger.Record(resources, environment.WaterIce);
            NoteCell(CellSite);
        }
    }

    /// <summary>
    /// Returns a science or deposit number that v0 is allowed to read.
    /// PENDING, HISTORICAL and <c>used_in_v0: false</c> fail with the catalog's
    /// error, which names the id. An id that exists but is not mapped is not delivered.
    /// </summary>
    public SourcedValue<double> ReadNumber(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A parameter id is required.", nameof(id));
        }

        if (_scienceById.TryGetValue(id, out var scienceValue))
        {
            return scienceValue;
        }

        if (_depositsById.TryGetValue(id, out var depositValue))
        {
            return depositValue;
        }

        var catalog = CatalogContaining(id);
        try
        {
            _ = catalog.RequireNumber(id);
        }
        catch (ParameterUnavailableException)
        {
            throw;
        }

        throw new ScenarioLoadException(
            $"Parameter '{id}' is not in the v0 field map and is not delivered to a system.",
            id);
    }

    private void NoteCell(string cellSite)
    {
        foreach (var id in _scienceIds)
        {
            _science.NoteRegionalCellUse(id, cellSite);
        }

        foreach (var id in _depositIds)
        {
            _deposits.NoteRegionalCellUse(id, cellSite);
        }
    }

    private void AssignDeposits(ResourcePool resources, EnvironmentState environment)
    {
        var names = CellMassLedger.Names;
        for (var i = 0; i < names.Count; i++)
        {
            var value = _depositKilograms[i];
            switch (names[i])
            {
                case nameof(ResourcePool.CarbonDioxide):
                    resources.CarbonDioxide = value;
                    break;
                case nameof(ResourcePool.Water):
                    resources.Water = value;
                    break;
                case nameof(ResourcePool.Oxygen):
                    resources.Oxygen = value;
                    break;
                case nameof(ResourcePool.Nitrogen):
                    resources.Nitrogen = value;
                    break;
                case nameof(ResourcePool.Phosphorus):
                    resources.Phosphorus = value;
                    break;
                case nameof(ResourcePool.Sulfur):
                    resources.Sulfur = value;
                    break;
                case nameof(ResourcePool.Iron):
                    resources.Iron = value;
                    break;
                case nameof(ResourcePool.OrganicCarbon):
                    resources.OrganicCarbon = value;
                    break;
                case nameof(ResourcePool.MineralCarbon):
                    resources.MineralCarbon = value;
                    break;
                case nameof(EnvironmentState.WaterIce):
                    environment.WaterIce = value;
                    break;
                default:
                    throw new ScenarioLoadException($"Deposit '{names[i]}' has no cell field.", fieldName: names[i]);
            }
        }
    }

    private ParameterCatalog CatalogContaining(string id)
    {
        var inScience = Contains(_science, id);
        var inDeposits = Contains(_deposits, id);
        if (inScience && inDeposits)
        {
            throw new ScenarioLoadException($"Parameter '{id}' is in both the science catalog and the deposit catalog.", id);
        }

        if (inScience)
        {
            return _science;
        }

        if (inDeposits)
        {
            return _deposits;
        }

        throw new ParameterUnavailableException(id, ParameterUnavailableReason.Missing);
    }

    private static bool Contains(ParameterCatalog catalog, string id)
    {
        foreach (var definition in catalog.Definitions)
        {
            if (string.Equals(definition.Id, id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static (IReadOnlyList<SourcedValue<double>> Values, string[] Ids, double[] Kilograms) ReadDeposits(
        ParameterCatalog deposits,
        List<string> scienceIds)
    {
        using var metadata = JsonDocument.Parse(deposits.MetadataJson);
        if (!metadata.RootElement.TryGetProperty("deposit_map", out var map) || map.ValueKind != JsonValueKind.Object)
        {
            throw new ScenarioLoadException("The deposit document is missing metadata.deposit_map.");
        }

        var names = CellMassLedger.Names;
        var allowed = new HashSet<string>(names, StringComparer.Ordinal);
        var idsByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in map.EnumerateObject())
        {
            if (!idsByName.TryAdd(property.Name, ReadDepositId(property)))
            {
                throw new ScenarioLoadException($"metadata.deposit_map repeats '{property.Name}'.", fieldName: property.Name);
            }

            if (!allowed.Contains(property.Name))
            {
                throw new ScenarioLoadException(
                    $"metadata.deposit_map key '{property.Name}' is not a resource deposit or WaterIce.",
                    fieldName: property.Name);
            }
        }

        var values = new List<SourcedValue<double>>(names.Count);
        var ids = new string[names.Count];
        var kilograms = new double[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            if (!idsByName.TryGetValue(name, out var id))
            {
                throw new ScenarioLoadException($"metadata.deposit_map is missing '{name}'.", fieldName: name);
            }

            if (scienceIds.Contains(id, StringComparer.Ordinal))
            {
                throw new ScenarioLoadException($"Parameter '{id}' is in both the science catalog and the deposit catalog.", id);
            }

            var definition = FindDefinition(deposits, id, name);
            if (definition.UsedInV0 != true)
            {
                throw new ScenarioLoadException(
                    $"Deposit '{name}' maps to '{id}', which does not have used_in_v0 true.",
                    id,
                    name);
            }

            if (definition.Classification != ParameterClassification.GameBalance)
            {
                throw new ScenarioLoadException(
                    $"Deposit '{name}' maps to '{id}', whose classification is not GAME_BALANCE.",
                    id,
                    name);
            }

            if (!string.Equals(definition.Unit, "kg", StringComparison.Ordinal))
            {
                throw new ScenarioLoadException(
                    $"Deposit '{name}' maps to '{id}' with unit '{definition.Unit}'. The unit must be kg.",
                    id,
                    name);
            }

            if (string.IsNullOrWhiteSpace(definition.Rationale))
            {
                throw new ScenarioLoadException($"Deposit '{name}' maps to '{id}', which has no rationale.", id, name);
            }

            var sourced = deposits.RequireNumber(id);
            if (!double.IsFinite(sourced.Value) || sourced.Value < 0.0)
            {
                throw new ScenarioLoadException(
                    $"Deposit '{name}' maps to '{id}', whose value is not finite and at least 0.",
                    id,
                    name);
            }

            values.Add(sourced);
            ids[i] = id;
            kilograms[i] = sourced.Value;
        }

        return (values, ids, kilograms);
    }

    private static string ReadDepositId(JsonProperty property)
    {
        if (property.Value.ValueKind != JsonValueKind.String)
        {
            throw new ScenarioLoadException(
                $"metadata.deposit_map.{property.Name} must be a parameter id.",
                fieldName: property.Name);
        }

        var id = property.Value.GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ScenarioLoadException($"metadata.deposit_map.{property.Name} is empty.", fieldName: property.Name);
        }

        return id;
    }

    private static List<FieldMapEntry> ReadFieldMap(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("v0_field_map", out var map) || map.ValueKind != JsonValueKind.Object)
        {
            throw new ScenarioLoadException("metadata.v0_field_map is missing.");
        }

        var properties = typeof(EnvironmentState)
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(property => property.CanWrite && property.SetMethod is not null)
            .Select(property => property.Name)
            .ToArray();
        var allowed = new HashSet<string>(properties, StringComparer.Ordinal);
        allowed.Remove(nameof(EnvironmentState.WaterIce));
        allowed.Remove(nameof(EnvironmentState.Scope));
        allowed.Add(SepEnabledField);

        var entries = new List<FieldMapEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in map.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new ScenarioLoadException($"metadata.v0_field_map repeats '{property.Name}'.", fieldName: property.Name);
            }

            if (!allowed.Contains(property.Name))
            {
                throw new ScenarioLoadException(
                    $"The v0 field map key '{property.Name}' is not a property of EnvironmentState and is not SepEnabled.",
                    fieldName: property.Name);
            }

            entries.Add(ReadField(property.Name, property.Value));
        }

        var orderedNames = properties.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        foreach (var name in orderedNames)
        {
            if (name is nameof(EnvironmentState.WaterIce) or nameof(EnvironmentState.Scope))
            {
                continue;
            }

            if (!seen.Contains(name))
            {
                throw new ScenarioLoadException(
                    $"EnvironmentState.{name} is missing from metadata.v0_field_map.",
                    fieldName: name);
            }
        }

        return entries;
    }

    private static FieldMapEntry ReadField(string field, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ScenarioLoadException($"metadata.v0_field_map.{field} must be an object.", fieldName: field);
        }

        if (!element.TryGetProperty("id", out var idElement))
        {
            throw new ScenarioLoadException($"metadata.v0_field_map.{field} is missing id.", fieldName: field);
        }

        string? unit = null;
        if (element.TryGetProperty("unit", out var unitElement) && unitElement.ValueKind == JsonValueKind.String)
        {
            unit = unitElement.GetString();
        }

        if (idElement.ValueKind == JsonValueKind.Null)
        {
            var status = element.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
                ? statusElement.GetString()
                : null;
            if (!string.Equals(status, "UNASSIGNED", StringComparison.Ordinal))
            {
                throw new ScenarioLoadException(
                    $"Field '{field}' has no id and its status is not UNASSIGNED.",
                    fieldName: field);
            }

            if (string.Equals(field, SepEnabledField, StringComparison.Ordinal) || !CanStayUnset(field))
            {
                throw new ScenarioLoadException(
                    $"Field '{field}' cannot be UNASSIGNED. v0 needs a value for it.",
                    fieldName: field);
            }

            return new FieldMapEntry(field, null, unit, true);
        }

        if (idElement.ValueKind != JsonValueKind.String)
        {
            throw new ScenarioLoadException($"metadata.v0_field_map.{field}.id must be a string or null.", fieldName: field);
        }

        var id = idElement.GetString();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ScenarioLoadException($"metadata.v0_field_map.{field}.id is empty.", fieldName: field);
        }

        if (IsBlockedInV0(field))
        {
            throw new ScenarioLoadException(
                $"Field '{field}' must stay UNASSIGNED in v0. The id '{id}' is not delivered.",
                id,
                field);
        }

        return new FieldMapEntry(field, id, unit, false);
    }

    private static bool IsBlockedInV0(string field)
    {
        return field is nameof(EnvironmentState.UvRadiation)
            or nameof(EnvironmentState.SoilPorosity)
            or nameof(EnvironmentState.PerchlorateMassKg)
            or nameof(EnvironmentState.WaterVapor);
    }

    private static bool CanStayUnset(string field)
    {
        return field is nameof(EnvironmentState.TemperatureKelvin)
            or nameof(EnvironmentState.PressurePascal)
            or nameof(EnvironmentState.UvRadiation)
            or nameof(EnvironmentState.WaterVapor)
            or nameof(EnvironmentState.SoilPorosity)
            or nameof(EnvironmentState.PerchlorateMassKg);
    }

    private static ParameterDefinition FindDefinition(ParameterCatalog catalog, string id, string field)
    {
        foreach (var definition in catalog.Definitions)
        {
            if (string.Equals(definition.Id, id, StringComparison.Ordinal))
            {
                return definition;
            }
        }

        throw new ScenarioLoadException(
            $"Field '{field}' maps to '{id}', which is not in the catalog.",
            id,
            field);
    }

    private static double RequireMapped(Dictionary<string, double> values, string field)
    {
        if (!values.TryGetValue(field, out var value))
        {
            throw new ScenarioLoadException($"Field '{field}' has no delivered number.", fieldName: field);
        }

        return value;
    }

    private static void RequireSol(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("tick", out var tick) || tick.ValueKind != JsonValueKind.Object)
        {
            throw new ScenarioLoadException("metadata.tick is missing.");
        }

        if (!tick.TryGetProperty("seconds", out var seconds) || seconds.ValueKind != JsonValueKind.Number
            || !seconds.TryGetInt32(out var value))
        {
            throw new ScenarioLoadException("metadata.tick.seconds must be an integer.");
        }

        if (value != MartianTime.SecondsPerSol)
        {
            throw new ScenarioLoadException(
                $"metadata.tick.seconds is {value.ToString(CultureInfo.InvariantCulture)}. The model sol is {MartianTime.SecondsPerSol.ToString(CultureInfo.InvariantCulture)} s.");
        }
    }

    private static string ReadCellSite(string scopeId)
    {
        const string prefix = "regional:";
        if (!scopeId.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ScenarioLoadException(
                $"metadata.scope_id is '{scopeId}'. It must be regional:<site>.");
        }

        var site = scopeId[prefix.Length..];
        if (string.IsNullOrWhiteSpace(site) || site.Contains(' ', StringComparison.Ordinal))
        {
            throw new ScenarioLoadException($"metadata.scope_id '{scopeId}' has no single site.");
        }

        return site;
    }

    private static string ReadVersion(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("version", out var version))
        {
            throw new ScenarioLoadException("metadata.version is missing.");
        }

        var text = version.ValueKind switch
        {
            JsonValueKind.String => version.GetString(),
            JsonValueKind.Number => version.GetRawText(),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ScenarioLoadException("metadata.version is empty.");
        }

        return text;
    }

    private static string RequireString(JsonElement element, string name, string path)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ScenarioLoadException($"{path}.{name} must be a string.");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ScenarioLoadException($"{path}.{name} is empty.");
        }

        return text;
    }

    private static Dictionary<string, SourcedValue<double>> Index(IReadOnlyList<SourcedValue<double>> values)
    {
        var index = new Dictionary<string, SourcedValue<double>>(values.Count, StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!index.TryAdd(value.Id, value))
            {
                throw new ScenarioLoadException($"Parameter '{value.Id}' is delivered twice.", value.Id);
            }
        }

        return index;
    }

    private readonly record struct FieldMapEntry(string Field, string? Id, string? Unit, bool Unassigned);

    private readonly record struct EnvironmentNumbers(
        double TemperatureKelvin,
        double PressurePascal,
        double IonizingAbsorbedDoseGyPerSol,
        double IonizingEquivalentDoseSvPerSol,
        double SolarIrradiance,
        double PerchlorateWtPercent,
        double WaterIceWtPercent);
}
