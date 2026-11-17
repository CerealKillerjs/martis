using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Application;
using Martis.Simulation.Expedition;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// V4 (Documento Maestro v2.0 §6.2, §7, ADR-031): the AI acts through robots with limits. The
/// rover wears with distance (slower, faulty instruments) and is repaired parked at the lander; it
/// builds fixed stations (B) that measure the ground every sol on the lander's power budget, wear
/// out and wait for the rover to repair them (X). From the strategic view the rover takes orders
/// to drive somewhere (G or «Enviar el rover»): they stay pending until it arrives, or fail.
/// </summary>
public partial class GameRoot
{
    private readonly Dictionary<int, StationModel> _stationModels = [];
    private FacilityDefinition? _facilityDefinition;
    private FacilityNetwork _network = null!;
    private Order? _moveOrder;
    private (int X, int Y) _moveTarget;

    private void ResetFacilities()
    {
        _facilityDefinition ??= FacilityDefinition.LoadPublished(_dataRoot);
        _network = new FacilityNetwork(_facilityDefinition);
        foreach (var model in _stationModels.Values)
        {
            model.QueueFree();
        }

        _stationModels.Clear();
        _moveOrder = null;
    }

    /// <summary>One sol of the station network: measurements, wear and breakdowns.</summary>
    private void StationsTick()
    {
        foreach (var station in _network.Tick(_knowledge))
        {
            _hud.AddEvents([new SessionEvent(_session.Tick, "failure", string.Create(CultureInfo.InvariantCulture, $"Estación #{station.Id} ({station.X}, {station.Y}) averiada por polvo y frío: envía el rover a repararla (X)"), station.Y * _session.Width + station.X)]);
            _world.Pulse((station.Y * _session.Width) + station.X, _session.Width, new Color(1f, 0.3f, 0.2f));
        }

        ShowStations();
    }

    private void ShowStations()
    {
        foreach (var station in _network.Stations)
        {
            if (!_stationModels.TryGetValue(station.Id, out var model))
            {
                model = new StationModel { Name = $"Station{station.Id}" };
                _world.AddChild(model);
                model.Position = _world.CellPoint(station.X + 0.5, station.Y + 0.5);
                _stationModels[station.Id] = model;
            }

            model.ShowWear(station.Wear);
        }
    }

    /// <summary>Wear while driving, repairs while parked at the lander, and the autopilot.</summary>
    private (double Throttle, double Steer) Autopilot(double throttle, double steer, double delta)
    {
        if (_rover.Charging)
        {
            _rover.Repair(_network.Definition.RoverRepairPerSecond * delta);
        }

        if (_moveOrder is null || throttle != 0 || steer != 0)
        {
            return (throttle, steer);
        }

        var (t, s) = _rover.SteerTowards(_moveTarget.X, _moveTarget.Y);
        if (t == 0)
        {
            Resolve(_orders.Complete(_moveOrder.Id, _session.Tick, "rover en destino"));
            _moveOrder = null;
            Act("rover-arrived");
            _hud.Toast("El rover ha llegado a su destino.");
        }
        else if (_landed && _rover.Battery <= 0)
        {
            Resolve(_orders.Fail(_moveOrder.Id, _session.Tick, "batería agotada en ruta"));
            _moveOrder = null;
            _hud.Toast("El rover se ha quedado sin batería en ruta: tráelo al módulo a paso lento.", bad: true);
            return (0, 0);
        }

        return (t, s);
    }

    private void AfterDrive(double driven) => _rover.AddWear(driven * _network.Definition.RoverWearPerCell);

    /// <summary>G or «Enviar el rover»: an order for the rover to drive to the selected cell.</summary>
    private void SendRover()
    {
        if (_selected.X < 0)
        {
            return;
        }

        if (_moveOrder is not null)
        {
            Resolve(_orders.Fail(_moveOrder.Id, _session.Tick, "sustituida por una orden nueva"));
        }

        _moveTarget = _selected;
        _moveOrder = _orders.Issue("move", FormattableString.Invariant($"({_selected.X}, {_selected.Y})"), "rover", _session.Tick);
        _world.Pulse((_selected.Y * _session.Width) + _selected.X, _session.Width, new Color(0.4f, 0.85f, 1f));
        Act("send-rover");
        _hud.Toast(FormattableString.Invariant($"Orden #{_moveOrder.Id}: el rover va a ({_selected.X}, {_selected.Y}), a {_rover.DistanceTo(_selected.X, _selected.Y):0} casillas."));
    }

    /// <summary>A worn instrument may fail; the fault is decided by the run seed, so replays agree.</summary>
    private bool InstrumentFails(Order order)
    {
        var probability = _network.Definition.FaultProbability(_rover.Wear);
        if (!Faults.Happens(_session.Seed, _session.Tick, order.Id, probability))
        {
            return false;
        }

        Resolve(_orders.Fail(order.Id, _session.Tick, "fallo del instrumento por desgaste"));
        _hud.Toast(string.Create(CultureInfo.InvariantCulture, $"Fallo del instrumento: el rover está desgastado ({_rover.Wear * 100:0} %). Repáralo aparcado junto al módulo."), bad: true);
        return true;
    }

    /// <summary>B in exploration: a fixed station on the measured ground under the rover.</summary>
    private void BuildStation()
    {
        var (x, y) = _rover.Cell;
        var cost = _network.Definition.StationBatteryCost;
        var order = _orders.Issue("build", FormattableString.Invariant($"({x}, {y})"), "rover", _session.Tick, cost);
        var check = _network.CanBuild(x, y, _knowledge.Level(x, y));
        string? problem = check switch
        {
            BuildResult.NotMeasured => "Mide antes el terreno con el escáner (C): las estaciones van sobre suelo medido.",
            BuildResult.Occupied => "Ya hay una estación en esta celda.",
            BuildResult.NoPower => string.Create(CultureInfo.InvariantCulture, $"El módulo no da más energía: {_network.PowerUsedW:0}/{_network.Definition.PowerBudgetW:0} W en uso."),
            _ => !_rover.Spend(cost) ? "Batería insuficiente para montar la estación." : null,
        };
        if (problem is not null)
        {
            Resolve(_orders.Fail(order.Id, _session.Tick, check == BuildResult.Built ? "batería insuficiente" : check.ToString()));
            _hud.Toast(problem, bad: true);
            return;
        }

        _network.Build(x, y, KnowledgeLevel.Measured, _session.Tick, out var station);
        Resolve(_orders.Complete(order.Id, _session.Tick, $"estación #{station!.Id}"));
        _knowledge.Observe(x + 0.5, y + 0.5, _network.Definition.StationRadiusCells, _network.Definition.StationConfidence, 0.0);
        ShowStations();
        Act("build-station");
        _hud.Toast(string.Create(CultureInfo.InvariantCulture, $"Estación #{station.Id} montada: mide {_network.Definition.StationRadiusCells:0} casillas a su alrededor cada sol ({_network.PowerUsedW:0}/{_network.Definition.PowerBudgetW:0} W)."));
    }

    /// <summary>X in exploration: repairs the nearest station within reach.</summary>
    private void RepairStation()
    {
        if (_network.Near(_rover.X, _rover.Y) is not { } station)
        {
            _hud.Toast(_landed && _rover.Lander is { } lander && _rover.DistanceTo(lander.X, lander.Y) <= _rover.Definition.ChargeRadiusCells
                ? "El propio rover se repara aparcado aquí, junto al módulo."
                : "No hay ninguna estación al alcance: acércate a menos de 2 casillas.", bad: true);
            return;
        }

        var order = _orders.Issue("repair", FormattableString.Invariant($"estación #{station.Id}"), "rover", _session.Tick);
        if (!_network.Repair(station))
        {
            Resolve(_orders.Fail(order.Id, _session.Tick, "no necesitaba reparación"));
            _hud.Toast($"La estación #{station.Id} está en buen estado.");
            return;
        }

        Resolve(_orders.Complete(order.Id, _session.Tick, "reparada"));
        ShowStations();
        Act("repair-station");
        _hud.Toast($"Estación #{station.Id} reparada.");
    }

    /// <summary>Screenshot network (<c>--stations</c>): three stations of different wear and a worn rover.</summary>
    private void ShowcaseStations(int o)
    {
        // Built one after another: the first broke long ago, the last is almost new.
        var plan = new[] { (X: 30 + o, Y: 27 + o, Sols: 50), (X: 27 + o, Y: 37 + o, Sols: 30), (X: 36 + o, Y: 36 + o, Sols: 5) };
        foreach (var (x, y, sols) in plan)
        {
            _knowledge.Scan(x + 0.5, y + 0.5);
            _network.Build(x, y, KnowledgeLevel.Measured, _session.Tick, out _);
            for (var sol = 0; sol < sols; sol++)
            {
                _network.Tick(_knowledge);
            }
        }

        _rover.AddWear(0.62);
        ShowStations();
        _shownKnowledge = -1;
        ShowKnowledge();
    }
}
