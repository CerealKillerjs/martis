using System.Text.Json;
using Martis.Domain.Parameters;

namespace Martis.Domain.Tests.Parameters;

/// <summary>
/// Rejection and acceptance cases for the generic parameter loader (S1-17).
/// Numbers in the fixtures are synthetic. The only borrowed figure is the
/// illustrative <c>mutation_rate</c> of TDD §10.2, classified GAME_BALANCE.
/// </summary>
public sealed class ParameterCatalogTests
{
    private static readonly string ValidCatalogPath = Path.Combine(
        RepoRoot.Find(),
        "tests",
        "Martis.Domain.Tests",
        "Fixtures",
        "Parameters",
        "valid-catalog.json");

    [Fact]
    public void MutationRateFromSection10_2LoadsAsGameBalance()
    {
        var catalog = LoadValidCatalog();
        var rate = catalog.RequireNumber("mutation_rate");

        Assert.Equal("mutation_rate", rate.Id);
        Assert.Equal("mutation_rate", rate.Field);
        Assert.Equal("per_reproduction_event", rate.Unit);
        Assert.Equal(ParameterClassification.GameBalance, rate.Classification);
        Assert.Equal(1m, rate.Version);
        Assert.Empty(rate.Sources);
        Assert.Equal(ParseNumber("0.0001"), rate.Value);
        Assert.IsType<ParameterScope.Analog>(rate.Scope);
    }

    [Fact]
    public void AcceptsAnIntegerVersion()
    {
        var catalog = LoadValidCatalog();
        Assert.Equal(1m, catalog.RequireNumber("mutation_rate").Version);
        Assert.Equal(2m, catalog.RequireRange("band").Version);
    }

    [Fact]
    public void AcceptsADecimalStringVersion()
    {
        var catalog = LoadValidCatalog();
        Assert.Equal(0.1m, catalog.RequireBool("flag_enabled").Version);
        Assert.Equal(1.5m, catalog.RequireNumber("mean_level").Version);
    }

    [Fact]
    public void RejectsAMissingClassification()
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadOne(Without("classification", "\"GAME_BALANCE\"")));
        Assert.Contains("classification", error.Message, StringComparison.Ordinal);
        Assert.Equal("sample", error.ParameterId);
    }

    [Fact]
    public void RejectsAnUnknownClassification()
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(classification: "MADE_UP")));
        Assert.Contains("MADE_UP", error.Message, StringComparison.Ordinal);
        Assert.Equal("sample", error.ParameterId);
    }

    [Fact]
    public void RejectsScientificMeasuredWithoutASource()
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(
            classification: "SCIENTIFIC_MEASURED",
            source: "null")));
        Assert.Contains("sample", error.Message, StringComparison.Ordinal);
        Assert.Contains("DOI", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsScientificEstimatedWhenNoSourceHasADoiOrUrl()
    {
        const string source = """
            [{ "citation": "Synthetic citation", "accessed": "2026-01-01" }]
            """;
        var error = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(
            classification: "SCIENTIFIC_ESTIMATED",
            source: source)));
        Assert.Equal("sample", error.ParameterId);
        Assert.Contains("DOI", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnEmptyUnit()
    {
        var empty = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(unit: "")));
        Assert.Contains("unit", empty.Message, StringComparison.Ordinal);

        var missing = Assert.Throws<ParameterLoadException>(() => LoadOne(Without("unit", "\"synthetic_unit\"")));
        Assert.Contains("unit", missing.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"0\"")]
    [InlineData("\"0.0\"")]
    [InlineData("\"-0.1\"")]
    public void RejectsAVersionThatIsNotPositive(string versionJson)
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(version: versionJson)));
        Assert.Equal("sample", error.ParameterId);
        Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsADuplicateId()
    {
        var body = Sample() + "," + Sample(id: "sample");
        var error = Assert.Throws<ParameterLoadException>(() => LoadMany(body));
        Assert.Equal("sample", error.ParameterId);
        Assert.Contains("Duplicate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnclassifiedPendingUnlessTheValueIsPendingAndNull()
    {
        var wrongStatus = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(
            classification: "UNCLASSIFIED_PENDING",
            status: "OK",
            value: "null")));
        Assert.Contains("UNCLASSIFIED_PENDING", wrongStatus.Message, StringComparison.Ordinal);

        var withValue = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(
            classification: "UNCLASSIFIED_PENDING",
            status: "PENDING",
            value: "1")));
        Assert.Contains("UNCLASSIFIED_PENDING", withValue.Message, StringComparison.Ordinal);

        var catalog = LoadOne(Sample(
            classification: "UNCLASSIFIED_PENDING",
            status: "PENDING",
            value: "null"));
        var pending = Assert.Single(catalog.Definitions);
        Assert.Equal(ParameterClassification.UnclassifiedPending, pending.Classification);
        Assert.IsType<ParameterLiteral.None>(pending.Value);
    }

    [Fact]
    public void RejectsPendingWhenTheValueIsNotNull()
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(status: "PENDING", value: "3")));
        Assert.Equal("sample", error.ParameterId);
        Assert.Contains("PENDING", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingAPendingParameterNamesThatParameter()
    {
        var catalog = LoadValidCatalog();
        var error = Assert.Throws<ParameterUnavailableException>(() => catalog.RequireNumber("pending_gap"));
        Assert.Equal("pending_gap", error.ParameterId);
        Assert.Equal(ParameterUnavailableReason.Pending, error.Reason);
        Assert.Contains("pending_gap", error.Message, StringComparison.Ordinal);
        Assert.Contains(catalog.Definitions, item => item.Id == "pending_gap");
    }

    [Fact]
    public void HistoricalAndUnusedParametersStayLoadedButAreNotDelivered()
    {
        var catalog = LoadValidCatalog();

        var historical = Assert.Throws<ParameterUnavailableException>(() => catalog.RequireNumber("old_note"));
        Assert.Equal("old_note", historical.ParameterId);
        Assert.Equal(ParameterUnavailableReason.Historical, historical.Reason);
        Assert.Contains("old_note", historical.Message, StringComparison.Ordinal);

        var unused = Assert.Throws<ParameterUnavailableException>(() => catalog.RequireNumber("unused_knob"));
        Assert.Equal("unused_knob", unused.ParameterId);
        Assert.Equal(ParameterUnavailableReason.NotUsedInV0, unused.Reason);
        Assert.Contains("unused_knob", unused.Message, StringComparison.Ordinal);

        Assert.Equal("HISTORICAL", catalog.Definitions.Single(item => item.Id == "old_note").Status);
        Assert.False(catalog.Definitions.Single(item => item.Id == "unused_knob").UsedInV0);
    }

    [Fact]
    public void MissingParameterDoesNotBecomeZero()
    {
        var catalog = LoadValidCatalog();
        var error = Assert.Throws<ParameterUnavailableException>(() => catalog.RequireNumber("does_not_exist"));
        Assert.Equal("does_not_exist", error.ParameterId);
        Assert.Equal(ParameterUnavailableReason.Missing, error.Reason);
        Assert.Contains("does_not_exist", error.Message, StringComparison.Ordinal);
        Assert.Contains("No default", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireNumberRejectsANonNumberWithoutInventingZero()
    {
        var catalog = LoadValidCatalog();
        var error = Assert.Throws<ParameterUnavailableException>(() => catalog.RequireNumber("flag_enabled"));
        Assert.Equal("flag_enabled", error.ParameterId);
        Assert.Equal(ParameterUnavailableReason.WrongKind, error.Reason);
        Assert.Contains("flag_enabled", error.Message, StringComparison.Ordinal);

        var flag = catalog.RequireBool("flag_enabled");
        Assert.True(flag.Value);
        Assert.Equal(ParameterClassification.GameBalance, flag.Classification);

        var label = catalog.RequireString("label_code");
        Assert.Equal("synthetic-label", label.Value);
        Assert.Equal(ParameterClassification.Speculative, label.Classification);

        var band = catalog.RequireRange("band");
        Assert.Equal(0.25, band.Value.Minimum);
        Assert.Equal(0.75, band.Value.Maximum);
    }

    [Fact]
    public void ScopeAcceptsRegionalPlanetaryMeanAndAnalogOnly()
    {
        var catalog = LoadValidCatalog();
        Assert.IsType<ParameterScope.Analog>(catalog.RequireNumber("mutation_rate").Scope);
        Assert.IsType<ParameterScope.PlanetaryMean>(catalog.RequireNumber("mean_level").Scope);
        var regional = Assert.IsType<ParameterScope.Regional>(catalog.RequireString("label_code").Scope);
        Assert.Equal("site-alpha", regional.Site);

        var lab = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(scope: "lab")));
        Assert.Contains("lab", lab.Message, StringComparison.Ordinal);
        var game = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(scope: "game")));
        Assert.Contains("game", game.Message, StringComparison.Ordinal);
        Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(scope: "regional:")));
        Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(scope: "analog:")));
        Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(scope: "planetary")));
    }

    [Fact]
    public void ClassificationAndScopeStayWithTheValue()
    {
        var catalog = LoadValidCatalog();
        var native = catalog.RequireNumber("native_count");
        var derived = catalog.RequireNumber("derived_count");

        Assert.Equal("count", native.Field);
        Assert.Equal("count", derived.Field);
        Assert.Equal(ParameterClassification.ScientificMeasured, native.Classification);
        Assert.Equal(ParameterClassification.ScientificEstimated, derived.Classification);
        Assert.Equal("native_count", derived.DerivedFrom);
        Assert.Equal(2d, derived.Conversion!.Factor);
        Assert.Equal("MODEL_ASSUMPTION", derived.Conversion.FactorClassification);
        Assert.Equal("{\"note\":\"synthetic\",\"width\":0.1}", derived.Uncertainty);
        Assert.IsType<ParameterRange.Interval>(derived.Range);
        Assert.Equal("10.0000/synthetic.measured", Assert.Single(native.Sources).Doi);
        Assert.Equal("https://example.invalid/conversion", Assert.Single(derived.Sources).Url);
        Assert.NotEqual(native.Classification, derived.Classification);
    }

    [Fact]
    public void DerivedValueStaysApartFromTheNativeValue()
    {
        var catalog = LoadValidCatalog();
        var native = catalog.RequireNumber("native_count");
        var derived = catalog.RequireNumber("derived_count");
        var stored = catalog.Definitions.Single(item => item.Id == "derived_count");

        Assert.Equal(4d, native.Value);
        Assert.Equal(5d, derived.Value);
        Assert.Equal("native_count", stored.DerivedFrom);
        Assert.Equal(2d, stored.Conversion!.Factor);
        Assert.NotEqual(native.Value * stored.Conversion.Factor, derived.Value);
        Assert.Equal(ParameterClassification.ScientificEstimated, derived.Classification);

        var reversed = LoadMany(
            Sample(id: "derived_count", classification: "SCIENTIFIC_ESTIMATED", value: "5", source: SourceWithUrl(), extra: DerivedLink("native_count"))
            + ","
            + Sample(id: "native_count", classification: "SCIENTIFIC_MEASURED", value: "4", source: SourceWithDoi()));
        Assert.Equal(5d, reversed.RequireNumber("derived_count").Value);
        Assert.Equal(4d, reversed.RequireNumber("native_count").Value);
    }

    [Fact]
    public void EstimatedDerivedFromMeasuredPasses()
    {
        var catalog = LoadMany(
            Sample(id: "native_count", classification: "SCIENTIFIC_MEASURED", value: "4", source: SourceWithDoi())
            + ","
            + Sample(id: "derived_count", classification: "SCIENTIFIC_ESTIMATED", value: "5", source: SourceWithUrl(), extra: DerivedLink("native_count")));
        var derived = catalog.RequireNumber("derived_count");
        Assert.Equal(ParameterClassification.ScientificEstimated, derived.Classification);
        Assert.Equal(5d, derived.Value);
        Assert.Equal("native_count", derived.DerivedFrom);
    }

    [Fact]
    public void MeasuredDerivedValueIsRejected()
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadMany(
            Sample(id: "native_count", classification: "SCIENTIFIC_MEASURED", value: "4", source: SourceWithDoi())
            + ","
            + Sample(id: "derived_count", classification: "SCIENTIFIC_MEASURED", value: "5", source: SourceWithDoi(), extra: DerivedLink("native_count"))));
        Assert.Equal("derived_count", error.ParameterId);
        Assert.Contains("SCIENTIFIC_MEASURED", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EstimatedDerivedFromModelAssumptionIsRejected()
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadMany(
            Sample(id: "native_count", classification: "MODEL_ASSUMPTION", value: "4")
            + ","
            + Sample(id: "derived_count", classification: "SCIENTIFIC_ESTIMATED", value: "5", source: SourceWithUrl(), extra: DerivedLink("native_count"))));
        Assert.Equal("derived_count", error.ParameterId);
        Assert.Contains("not SCIENTIFIC_*", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelAssumptionDerivedValuePasses()
    {
        var catalog = LoadMany(
            Sample(id: "native_count", classification: "MODEL_ASSUMPTION", value: "4")
            + ","
            + Sample(id: "derived_count", classification: "MODEL_ASSUMPTION", value: "5", extra: DerivedLink("native_count")));
        var derived = catalog.RequireNumber("derived_count");
        Assert.Equal(ParameterClassification.ModelAssumption, derived.Classification);
        Assert.Equal(5d, derived.Value);
        Assert.Equal("MODEL_ASSUMPTION", derived.Conversion!.FactorClassification);
    }

    [Theory]
    [InlineData("expression")]
    [InlineData("factor")]
    [InlineData("factor_classification")]
    [InlineData("assumptions")]
    public void ConversionRejectsAMissingField(string field)
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadMany(
            Sample(id: "native_count", classification: "SCIENTIFIC_MEASURED", value: "4", source: SourceWithDoi())
            + ","
            + Sample(id: "derived_count", classification: "MODEL_ASSUMPTION", value: "5", extra: DerivedLink("native_count", Omit(field)))));
        Assert.Contains("derived_count", error.Message, StringComparison.Ordinal);
        Assert.Contains(field, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionRejectsANonPositiveFactorAndEmptyAssumptions()
    {
        var zero = Assert.Throws<ParameterLoadException>(() => LoadDerived(ConversionObject(factor: "0")));
        Assert.Contains("factor", zero.Message, StringComparison.Ordinal);

        var negative = Assert.Throws<ParameterLoadException>(() => LoadDerived(ConversionObject(factor: "-1")));
        Assert.Contains("factor", negative.Message, StringComparison.Ordinal);

        var text = Assert.Throws<ParameterLoadException>(() => LoadDerived(ConversionObject(factor: "\"2\"")));
        Assert.Contains("factor", text.Message, StringComparison.Ordinal);

        var emptyAssumptions = Assert.Throws<ParameterLoadException>(() => LoadDerived(ConversionObject(assumptions: "[]")));
        Assert.Contains("assumptions", emptyAssumptions.Message, StringComparison.Ordinal);

        var blankAssumption = Assert.Throws<ParameterLoadException>(() => LoadDerived(ConversionObject(assumptions: "[\" \"]")));
        Assert.Contains("assumptions", blankAssumption.Message, StringComparison.Ordinal);

        var wrongClass = Assert.Throws<ParameterLoadException>(() => LoadDerived(ConversionObject(factorClassification: "GAME_BALANCE")));
        Assert.Contains("factor_classification", wrongClass.Message, StringComparison.Ordinal);

        var blankExpression = Assert.Throws<ParameterLoadException>(() => LoadDerived(ConversionObject(expression: "")));
        Assert.Contains("expression", blankExpression.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConversionIsRequiredWithDerivedFromAndForbiddenWithoutIt()
    {
        var missing = Assert.Throws<ParameterLoadException>(() => LoadMany(
            Sample(id: "native_count", classification: "MODEL_ASSUMPTION", value: "4")
            + ","
            + Sample(id: "derived_count", classification: "MODEL_ASSUMPTION", value: "5", extra: ", \"derived_from\": \"native_count\"")));
        Assert.Contains("conversion", missing.Message, StringComparison.Ordinal);

        var stray = Assert.Throws<ParameterLoadException>(() => LoadOne(
            Sample(extra: ", \"conversion\": " + ConversionObject())));
        Assert.Contains("without derived_from", stray.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UncertaintyIsKeptRawForAStringAndCanonicalForAnObject()
    {
        var catalog = LoadValidCatalog();
        Assert.Equal("synthetic", catalog.RequireNumber("mean_level").Uncertainty);

        var swapped = LoadOne(Sample(
            classification: "SCIENTIFIC_ESTIMATED",
            source: SourceWithUrl(),
            extra: ", \"uncertainty\": {\"width\":0.1,\"note\":\"synthetic\"}"));
        Assert.Equal("{\"note\":\"synthetic\",\"width\":0.1}", swapped.RequireNumber("sample").Uncertainty);
    }

    [Fact]
    public void SupersededStatusIsNotDelivered()
    {
        var catalog = LoadOne(Sample(status: "SUPERADO", value: "6"));
        Assert.Equal("SUPERADO", Assert.Single(catalog.Definitions).Status);
        var error = Assert.Throws<ParameterUnavailableException>(() => catalog.RequireNumber("sample"));
        Assert.Equal("sample", error.ParameterId);
        Assert.Equal(ParameterUnavailableReason.Superseded, error.Reason);
        Assert.Contains("sample", error.Message, StringComparison.Ordinal);
        Assert.Contains("SUPERADO", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonScientificValueWithoutSourceNeedsARationaleOrANote()
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadOne(Sample(rationale: null)));
        Assert.Contains("rationale", error.Message, StringComparison.Ordinal);

        var withNote = LoadOne(Sample(rationale: null, extra: ", \"note\": \"why this balance exists\""));
        Assert.Equal("why this balance exists", withNote.Definitions[0].Note);
    }

    [Fact]
    public void NumericRangeRejectsAnOutsideValueAndAReversedInterval()
    {
        var outside = Assert.Throws<ParameterLoadException>(() => LoadOne(
            Sample(value: "1.1", extra: ", \"range\": [0, 1]")));
        Assert.Contains("outside", outside.Message, StringComparison.Ordinal);

        var reversed = Assert.Throws<ParameterLoadException>(() => LoadOne(
            Sample(value: "0.5", extra: ", \"range\": [2, 1]")));
        Assert.Contains("minimum", reversed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericRangeAcceptsBothEndpoints()
    {
        var lower = LoadOne(Sample(value: "0", extra: ", \"range\": [0, 1]"));
        Assert.Equal(0d, lower.RequireNumber("sample").Value);
        var upper = LoadOne(Sample(value: "1", extra: ", \"range\": [0, 1]"));
        Assert.Equal(1d, upper.RequireNumber("sample").Value);
    }

    [Fact]
    public void PlanetaryMeanWarnsWhenARegionalCellUsesIt()
    {
        var catalog = LoadValidCatalog();
        catalog.NoteRegionalCellUse("mean_level");
        catalog.NoteRegionalCellUse("mean_level");
        catalog.NoteRegionalCellUse("native_count");

        var warning = Assert.Single(catalog.Warnings);
        Assert.Equal("mean_level", warning.ParameterId);
        Assert.Contains("mean_level", warning.Message, StringComparison.Ordinal);
        Assert.Contains("planetary_mean", warning.Message, StringComparison.Ordinal);
        Assert.Equal(11d, catalog.RequireNumber("mean_level").Value);

        var missing = Assert.Throws<ParameterUnavailableException>(() => catalog.NoteRegionalCellUse("absent"));
        Assert.Equal("absent", missing.ParameterId);
    }

    [Fact]
    public void OtherStatusesAreKeptAsText()
    {
        var catalog = LoadValidCatalog();
        Assert.Equal(
            ["OK", "ACCEPTED", "CONTEXTO", "FALLBACK", "REFERENCE", "OK", "PENDING", "HISTORICAL", "OK", "OK"],
            catalog.Definitions.Select(item => item.Status).ToArray());
        Assert.Contains("loader_rule_T03", catalog.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("synthetic-site", catalog.LocationJson, StringComparison.Ordinal);
        Assert.Equal("synthetic", catalog.Definitions.Single(item => item.Id == "mean_level").Uncertainty);
    }

    [Fact]
    public void OmittingValueDoesNotBecomeNull()
    {
        var error = Assert.Throws<ParameterLoadException>(() => LoadOne(Without("value", "1")));
        Assert.Contains("value", error.Message, StringComparison.Ordinal);
        Assert.Contains("No default", error.Message, StringComparison.Ordinal);
    }

    private static ParameterCatalog LoadValidCatalog()
    {
        return ParameterCatalog.Load(File.ReadAllText(ValidCatalogPath));
    }

    private static ParameterCatalog LoadOne(string parameterJson)
    {
        return LoadMany(parameterJson);
    }

    private static ParameterCatalog LoadMany(string parametersJson)
    {
        var json = "{\"metadata\":{\"name\":\"synthetic\"},\"location\":{\"name\":\"synthetic\"},\"parameters\":["
            + parametersJson
            + "]}";
        return ParameterCatalog.Load(json);
    }

    private static string Sample(
        string id = "sample",
        string classification = "GAME_BALANCE",
        string unit = "synthetic_unit",
        string scope = "analog:fixture",
        string version = "1",
        string status = "OK",
        string value = "1",
        string source = "null",
        string? rationale = "synthetic fixture",
        string extra = "")
    {
        var rationaleJson = rationale is null ? string.Empty : $",\"rationale\":\"{rationale}\"";
        return "{"
            + $"\"id\":\"{id}\","
            + "\"field\":\"sample\","
            + $"\"value\":{value},"
            + $"\"unit\":\"{unit}\","
            + $"\"classification\":\"{classification}\","
            + $"\"scope\":\"{scope}\","
            + $"\"version\":{version},"
            + $"\"status\":\"{status}\","
            + $"\"source\":{source}"
            + rationaleJson
            + extra
            + "}";
    }

    private static string ConversionObject(
        string expression = "x 2",
        string factor = "2",
        string factorClassification = "MODEL_ASSUMPTION",
        string assumptions = "[\"synthetic scale\"]")
    {
        return "{"
            + $"\"expression\":\"{expression}\","
            + $"\"factor\":{factor},"
            + $"\"factor_classification\":\"{factorClassification}\","
            + $"\"assumptions\":{assumptions}"
            + "}";
    }

    private static string Omit(string field)
    {
        var complete = ConversionObject();
        return complete.Replace($"\"{field}\":", "\"dropped\":", StringComparison.Ordinal);
    }

    private static string DerivedLink(string nativeId, string? conversion = null)
    {
        return $", \"derived_from\": \"{nativeId}\", \"conversion\": {conversion ?? ConversionObject()}";
    }

    private static ParameterCatalog LoadDerived(string conversion)
    {
        return LoadMany(
            Sample(id: "native_count", classification: "SCIENTIFIC_MEASURED", value: "4", source: SourceWithDoi())
            + ","
            + Sample(id: "derived_count", classification: "MODEL_ASSUMPTION", value: "5", extra: DerivedLink("native_count", conversion)));
    }

    private static string Without(string propertyName, string propertyJson)
    {
        var complete = Sample();
        var token = $"\"{propertyName}\":{propertyJson},";
        if (propertyName == "source")
        {
            token = $"\"source\":{propertyJson}";
        }

        var removed = complete.Replace(token, string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(complete, removed);
        return removed;
    }

    private static string SourceWithDoi()
    {
        return """
            [{ "citation": "Synthetic measured source", "doi": "10.0000/synthetic.measured", "accessed": "2026-01-02" }]
            """;
    }

    private static string SourceWithUrl()
    {
        return """
            [{ "citation": "Synthetic estimate", "url": "https://example.invalid/estimate", "accessed": "2026-01-03" }]
            """;
    }

    private static double ParseNumber(string jsonNumber)
    {
        using var document = JsonDocument.Parse(jsonNumber, new JsonDocumentOptions());
        return document.RootElement.GetDouble();
    }
}
