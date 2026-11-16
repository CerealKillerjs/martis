namespace Martis.Simulation.Tests.Scenarios;

/// <summary>
/// Synthetic scenario. The numbers are not measurements of Gale.
/// Every non-scientific entry has a note so the catalog can load it.
/// </summary>
internal static class ScenarioFixtures
{
    internal const string Science = """
        {
          "metadata": {
            "name": "fixture-baseline",
            "version": "0.3",
            "scope": "texto libre sin sitio",
            "scope_id": "regional:gale",
            "tick": { "unit": "sol", "seconds": 88775 },
            "v0_field_map": {
              "TemperatureKelvin": { "id": "fix.temperature", "unit": "K" },
              "PressurePascal": { "id": "fix.pressure", "unit": "Pa" },
              "IonizingAbsorbedDoseGyPerSol": { "id": "fix.dose_gy", "unit": "Gy/sol" },
              "IonizingEquivalentDoseSvPerSol": { "id": "fix.dose_sv", "unit": "Sv/sol" },
              "SolarIrradiance": { "id": "fix.irradiance", "unit": "W/m2" },
              "PerchlorateWtPercent": { "id": "fix.perchlorate", "unit": "wt% ClO4" },
              "WaterIceWtPercent": { "id": "fix.ice_wt", "unit": "wt%" },
              "SepEnabled": { "id": "fix.sep", "unit": "bool" },
              "PerchlorateMassKg": { "id": null, "status": "UNASSIGNED" },
              "UvRadiation": { "id": null, "status": "UNASSIGNED" },
              "SoilPorosity": { "id": null, "status": "UNASSIGNED" },
              "WaterVapor": { "id": null, "status": "UNASSIGNED" }
            }
          },
          "location": { "site": "gale" },
          "parameters": [
            {
              "id": "fix.temperature",
              "field": "temperature",
              "value": 250,
              "unit": "K",
              "classification": "SCIENTIFIC_ESTIMATED",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": true,
              "source": [{ "citation": "Fixture", "accessed": "2026-11-16", "doi": "10.0000/fixture" }],
              "note": "Synthetic fixture. Not a measurement of Gale."
            },
            {
              "id": "fix.pressure",
              "field": "pressure",
              "value": 100,
              "unit": "Pa",
              "classification": "GAME_BALANCE",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": true,
              "source": null,
              "note": "Synthetic fixture.",
              "rationale": "Placeholder pressure for the fixture."
            },
            {
              "id": "fix.dose_gy",
              "field": "dose_gy",
              "value": 0.25,
              "unit": "Gy/sol",
              "classification": "GAME_BALANCE",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": true,
              "source": null,
              "note": "Synthetic fixture.",
              "rationale": "Placeholder absorbed dose for the fixture."
            },
            {
              "id": "fix.dose_sv",
              "field": "dose_sv",
              "value": 0.5,
              "unit": "Sv/sol",
              "classification": "GAME_BALANCE",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": true,
              "source": null,
              "note": "Synthetic fixture.",
              "rationale": "Placeholder equivalent dose for the fixture."
            },
            {
              "id": "fix.irradiance",
              "field": "irradiance",
              "value": 10,
              "unit": "W/m2",
              "classification": "GAME_BALANCE",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": true,
              "source": null,
              "note": "Synthetic fixture.",
              "rationale": "Placeholder irradiance for the fixture."
            },
            {
              "id": "fix.perchlorate",
              "field": "perchlorate",
              "value": 20,
              "unit": "wt% ClO4",
              "classification": "MODEL_ASSUMPTION",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": true,
              "source": null,
              "note": "Synthetic weight percent. Not converted to kilograms."
            },
            {
              "id": "fix.ice_wt",
              "field": "ice_wt",
              "value": 5,
              "unit": "wt%",
              "classification": "MODEL_ASSUMPTION",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": true,
              "source": null,
              "note": "Synthetic weight percent. Not converted to kilograms."
            },
            {
              "id": "fix.sep",
              "field": "sep",
              "value": false,
              "unit": "bool",
              "classification": "GAME_BALANCE",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": true,
              "source": null,
              "note": "Synthetic fixture.",
              "rationale": "SEP stays off in the fixture."
            },
            {
              "id": "fix.pending",
              "field": "pending",
              "value": null,
              "unit": "synthetic_unit",
              "classification": "UNCLASSIFIED_PENDING",
              "scope": "regional:gale",
              "version": "1",
              "status": "PENDING",
              "source": null,
              "note": "Pending fixture value."
            },
            {
              "id": "fix.historical",
              "field": "historical",
              "value": 9,
              "unit": "synthetic_unit",
              "classification": "MODEL_ASSUMPTION",
              "scope": "regional:gale",
              "version": "1",
              "status": "HISTORICAL",
              "used_in_v0": false,
              "source": null,
              "note": "Historical fixture value."
            },
            {
              "id": "fix.superseded",
              "field": "superseded",
              "value": 3,
              "unit": "synthetic_unit",
              "classification": "MODEL_ASSUMPTION",
              "scope": "regional:gale",
              "version": "1",
              "status": "SUPERADO",
              "used_in_v0": false,
              "source": null,
              "note": "Superseded fixture value."
            },
            {
              "id": "fix.unused",
              "field": "unused",
              "value": 8,
              "unit": "synthetic_unit",
              "classification": "GAME_BALANCE",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": false,
              "source": null,
              "note": "Not used in v0.",
              "rationale": "Present so a false used_in_v0 flag can be refused."
            },
            {
              "id": "fix.extra",
              "field": "extra",
              "value": 4,
              "unit": "synthetic_unit",
              "classification": "GAME_BALANCE",
              "scope": "regional:gale",
              "version": "1",
              "status": "OK",
              "used_in_v0": true,
              "source": null,
              "note": "Deliverable by the catalog but not in the field map.",
              "rationale": "Must not be handed to a v0 system."
            }
          ]
        }
        """;

    internal const string Deposits = """
        {
          "metadata": {
            "name": "fixture-deposits",
            "version": "0.1",
            "scope_id": "regional:gale",
            "deposit_map": {
              "CarbonDioxide": "game.fixture.carbon_dioxide",
              "Water": "game.fixture.water",
              "Oxygen": "game.fixture.oxygen",
              "Nitrogen": "game.fixture.nitrogen",
              "Phosphorus": "game.fixture.phosphorus",
              "Sulfur": "game.fixture.sulfur",
              "Iron": "game.fixture.iron",
              "OrganicCarbon": "game.fixture.organic_carbon",
              "MineralCarbon": "game.fixture.mineral_carbon",
              "WaterIce": "game.fixture.water_ice"
            }
          },
          "location": { "site": "gale" },
          "parameters": [
            { "id": "game.fixture.carbon_dioxide", "field": "CarbonDioxide", "value": 1, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Fixture deposit.", "rationale": "Placeholder." },
            { "id": "game.fixture.water", "field": "Water", "value": 2, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Fixture deposit.", "rationale": "Placeholder." },
            { "id": "game.fixture.oxygen", "field": "Oxygen", "value": 3, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Fixture deposit.", "rationale": "Placeholder." },
            { "id": "game.fixture.nitrogen", "field": "Nitrogen", "value": 4, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Fixture deposit.", "rationale": "Placeholder." },
            { "id": "game.fixture.phosphorus", "field": "Phosphorus", "value": 5, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Fixture deposit.", "rationale": "Placeholder." },
            { "id": "game.fixture.sulfur", "field": "Sulfur", "value": 6, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Fixture deposit.", "rationale": "Placeholder." },
            { "id": "game.fixture.iron", "field": "Iron", "value": 7, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Fixture deposit.", "rationale": "Placeholder." },
            { "id": "game.fixture.organic_carbon", "field": "OrganicCarbon", "value": 8, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Fixture deposit.", "rationale": "Placeholder." },
            { "id": "game.fixture.mineral_carbon", "field": "MineralCarbon", "value": 9, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Fixture deposit.", "rationale": "Placeholder." },
            { "id": "game.fixture.water_ice", "field": "WaterIce", "value": 0.5, "unit": "kg", "classification": "GAME_BALANCE", "scope": "regional:gale", "version": "0.1", "status": "PROPOSED", "used_in_v0": true, "source": null, "note": "Not derived from the weight percent.", "rationale": "Placeholder ice mass." }
          ]
        }
        """;
}
