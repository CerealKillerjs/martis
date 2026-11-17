using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Simulation.Expedition;
using Martis.Simulation.Terraforming;

namespace Martis.Game;

/// <summary>
/// E1 (Documento Maestro v2.0 §10, ADR-033): the first regional intervention. The rover sets up
/// heaters (H) on measured ground; they draw from the same lander power as the stations, warm the
/// ground around them slowly, wear out and stop, and the heat fades when they do. The engine sees
/// the warming as validated temperature commands at the tick boundary.
/// </summary>
public partial class GameRoot
{
    private readonly Dictionary<int, HeaterModel> _heaterModels = [];
    private HeatingDefinition? _heatingDefinition;
    private RegionalClimate _climate = null!;

    private void ResetClimate(ClimateRecord? record = null)
    {
        _heatingDefinition ??= HeatingDefinition.LoadPublished(_dataRoot);
        _climate = _session.AttachClimate(_heatingDefinition, record);
        foreach (var model in _heaterModels.Values)
        {
            model.QueueFree();
        }

        _heaterModels.Clear();
        _network.ExtraLoadW = _climate.PowerUsedW;
        ShowHeaters();
    }

    /// <summary>After each sol: power drawn, heater lights and the heat on the terrain.</summary>
    private void ClimateTick()
    {
        _network.ExtraLoadW = _climate.PowerUsedW;
        foreach (var heater in _climate.Heaters.Where(heater => heater.Broken && heater.Wear >= 1.0 && _heaterModels.TryGetValue(heater.Id, out var model) && !model.ShownBroken))
        {
            _world.Pulse((heater.Y * _session.Width) + heater.X, _session.Width, new Color(1f, 0.3f, 0.2f));
        }

        ShowHeaters();
        if (_climate.Heaters.Count > 0 || _climate.WarmedCells > 0)
        {
            _shownKnowledge = -1; // the heat lives in the knowledge texture
        }
    }

    private void ShowHeaters()
    {
        foreach (var heater in _climate.Heaters)
        {
            if (!_heaterModels.TryGetValue(heater.Id, out var model))
            {
                model = new HeaterModel { Name = $"Heater{heater.Id}" };
                _world.AddChild(model);
                model.Position = _world.CellPoint(heater.X + 0.5, heater.Y + 0.5);
                _heaterModels[heater.Id] = model;
            }

            model.ShowState(heater.Wear, _climate.Anomaly(heater.X, heater.Y) / _climate.Definition.PeakDeltaK);
        }
    }

    /// <summary>H in exploration: a heater on the measured ground under the rover.</summary>
    private void BuildHeater()
    {
        var (x, y) = _rover.Cell;
        var definition = _climate.Definition;
        var order = _orders.Issue("heat", CellText(x, y), "rover", _session.Tick, definition.BatteryCost);
        var check = _climate.CanPlace(x, y, _knowledge.Level(x, y), _network.FreePowerW);
        string? problem = check switch
        {
            BuildResult.NotMeasured => "Mide antes el terreno con el escáner (C): los calefactores van sobre suelo medido.",
            BuildResult.Occupied => "Ya hay un calefactor en esta celda.",
            BuildResult.NoPower => string.Create(CultureInfo.InvariantCulture, $"El módulo no da {definition.PowerW:0} W más: quedan {_network.FreePowerW:0} de {_network.Definition.PowerBudgetW:0} W. Retira carga o espera a que algo se apague."),
            _ => !_rover.Spend(definition.BatteryCost) ? "Batería insuficiente para montar el calefactor." : null,
        };
        if (problem is not null)
        {
            Resolve(_orders.Fail(order.Id, _session.Tick, check == BuildResult.Built ? "batería insuficiente" : check.ToString()));
            _hud.Toast(problem, bad: true);
            return;
        }

        _climate.Place(x, y, KnowledgeLevel.Measured, _network.FreePowerW, _session.Tick, out var heater);
        _network.ExtraLoadW = _climate.PowerUsedW;
        Resolve(_orders.Complete(order.Id, _session.Tick, $"calefactor #{heater!.Id}"));
        ShowHeaters();
        _world.Pulse((y * _session.Width) + x, _session.Width, new Color(1f, 0.55f, 0.2f));
        Act("build-heater");
        _hud.Toast(string.Create(CultureInfo.InvariantCulture, $"Calefactor #{heater.Id} encendido: hasta +{definition.PeakDeltaK:0} K en {definition.RadiusCells:0} casillas, en unos {definition.RelaxationSols * 3:0} soles ({_network.PowerUsedW + _network.ExtraLoadW:0}/{_network.Definition.PowerBudgetW:0} W)."));
    }

    /// <summary>X near a worn heater repairs it; otherwise X repairs a station.</summary>
    private void RepairHere()
    {
        var station = _network.Near(_rover.X, _rover.Y);
        if (_climate.Near(_rover.X, _rover.Y, _network.Definition.RepairRadiusCells) is { Wear: > 0 } heater && (station is null || station.Wear <= heater.Wear))
        {
            var order = _orders.Issue("repair", $"calefactor #{heater.Id}", "rover", _session.Tick);
            _climate.Repair(heater);
            _network.ExtraLoadW = _climate.PowerUsedW;
            Resolve(_orders.Complete(order.Id, _session.Tick, "reparado"));
            ShowHeaters();
            Act("repair-heater");
            _hud.Toast($"Calefactor #{heater.Id} reparado: vuelve a calentar.");
            return;
        }

        RepairStation();
    }

    private static string CellText(int x, int y) => string.Create(CultureInfo.InvariantCulture, $"({x}, {y})");

    /// <summary>Screenshot heat (<c>--heaters</c>): two heaters warmed for a while and a broken one cooling.</summary>
    private void ShowcaseHeaters(int o)
    {
        var plan = new[] { (X: 33 + o, Y: 30 + o), (X: 29 + o, Y: 34 + o), (X: 36 + o, Y: 35 + o) };
        foreach (var (x, y) in plan)
        {
            _knowledge.Scan(x + 0.5, y + 0.5);
            _climate.Place(x, y, KnowledgeLevel.Measured, double.MaxValue, _session.Tick, out _);
        }

        _session.Advance(14);
        _climate.AddWear(_climate.Heaters[2], 1.0);
        _session.Advance(6);
        Refresh();
        ClimateTick();
        ShowKnowledge();
    }
}
