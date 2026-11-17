using System;
using Godot;
using Martis.Application;
using Martis.Simulation.Expedition;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// V2 (Documento Maestro v2.0 §2, §6, §13.2, ADR-029): the AI does not know the region at the
/// start. The orbital probe estimates a swath every few sols, the rover's cameras estimate what
/// it drives past and its scanner (C) measures the ground around it. Unknown ground is veiled,
/// the inspector shows estimates with their uncertainty, and colonies need at least an estimate.
/// The knowledge never changes the simulation; it only filters what the player sees and may do.
/// </summary>
public partial class GameRoot
{
    private SurveyDefinition? _surveyDefinition;
    private KnowledgeMap _knowledge = null!;
    private int _shownKnowledge = -1;
    private (int X, int Y) _glimpsedCell = (-1, -1);

    /// <summary>A new or loaded session starts from what the probe could have mapped by its sol.</summary>
    private void ResetKnowledge()
    {
        _surveyDefinition ??= SurveyDefinition.LoadPublished(_dataRoot);
        _knowledge = new KnowledgeMap(_surveyDefinition, _session.Width, _session.Height);
        var sols = Math.Min(_session.Tick, (long)_knowledge.Swaths * _surveyDefinition.OrbitalSolsPerPass);
        for (long sol = 0; sol <= sols; sol++)
        {
            _knowledge.Orbit(sol);
        }

        _shownKnowledge = -1;
        _glimpsedCell = (-1, -1);
    }

    /// <summary>The orbital pass of the sol that just ended.</summary>
    private void OrbitalPass()
    {
        if (_knowledge.SwathAt(_session.Tick) is not { } swath || _knowledge.Orbit(_session.Tick) == 0)
        {
            return;
        }

        var estimated = _knowledge.Coverage(KnowledgeLevel.Estimated);
        _hud.AddEvents([new SessionEvent(_session.Tick, "survey", FormattableString.Invariant($"Sonda orbital: franja {swath + 1}/{_knowledge.Swaths} cartografiada · {estimated * 100:0} % de la región estimada"))]);
    }

    /// <summary>Navigation cameras: every new cell the rover enters estimates its surroundings.</summary>
    private void Glimpse()
    {
        if (_rover.Cell == _glimpsedCell)
        {
            return;
        }

        _glimpsedCell = _rover.Cell;
        _knowledge.Glimpse(_rover.X, _rover.Y);
    }

    /// <summary>Pushes new knowledge to the terrain veil, the inspector and the gauge.</summary>
    private void ShowKnowledge()
    {
        if (_knowledge.Version == _shownKnowledge)
        {
            return;
        }

        _shownKnowledge = _knowledge.Version;
        _world.ShowKnowledge(_knowledge);
        if (_pois is not null)
        {
            UpdatePois();
        }
        if (_selected.X >= 0)
        {
            ShowSelected();
        }
    }

    private void ShowSelected()
        => _hud.ShowCell(_session.Inspect(_selected.X, _selected.Y), _knowledge.Level(_selected.X, _selected.Y));

    /// <summary>C in exploration: the scanner measures the ground around the rover.</summary>
    private void ScanHere()
    {
        var (x, y) = _rover.Cell;
        var cost = _knowledge.Definition.ScanBatteryCost;
        var order = _orders.Issue("scan", FormattableString.Invariant($"({x}, {y})"), "rover", _session.Tick, cost);
        if (InstrumentFails(order))
        {
            return;
        }

        if (!_rover.Spend(cost))
        {
            Resolve(_orders.Fail(order.Id, _session.Tick, "batería insuficiente"));
            _hud.Toast("Batería insuficiente para escanear. Vuelve al módulo a cargar.", bad: true);
            return;
        }

        var measured = _knowledge.Scan(_rover.X, _rover.Y);
        Resolve(_orders.Complete(order.Id, _session.Tick, FormattableString.Invariant($"{measured} celdas medidas")));
        _world.Scanned(_rover.X, _rover.Y, _knowledge.Definition.ScanRadiusCells);
        Select(x, y);
        Act("scan-site");
        _hud.Toast(measured > 0
            ? FormattableString.Invariant($"Escaneo: {measured} celdas medidas. {_knowledge.Coverage(KnowledgeLevel.Measured) * 100:0.#} % de la región medida.")
            : "Escaneo: nada nuevo aquí. Conduce hacia terreno sin medir.");
    }

    /// <summary>Colonies need at least an estimate of the ground: no seeding blind.</summary>
    private bool KnowledgeAllows(int x, int y, out string? reason)
    {
        reason = _knowledge.Level(x, y) == KnowledgeLevel.Unknown
            ? FormattableString.Invariant($"Celda ({x}, {y}) sin datos: escanéala con el rover (C) o espera a la sonda orbital.")
            : null;
        return reason is null;
    }

    /// <summary>
    /// Screenshot knowledge: by default the probe has mapped everything and the rover measured the
    /// colony; with <c>--survey</c> only the first passes are in and the rover is out exploring.
    /// </summary>
    private void ShowcaseKnowledge(int o)
    {
        ResetKnowledge();
        if (_options.ContainsKey("survey"))
        {
            _knowledge = new KnowledgeMap(_surveyDefinition!, _session.Width, _session.Height);
            for (long sol = 0; sol < 3 * _surveyDefinition!.OrbitalSolsPerPass; sol++)
            {
                _knowledge.Orbit(sol);
            }

            for (var step = 0; step <= 16; step++)
            {
                _knowledge.Glimpse(32 + o - (step * 0.9), 33 + o + (step * 0.35));
            }

            _knowledge.Scan(32.5 + o, 32.5 + o);
            _knowledge.Scan(19.5 + o, 38.5 + o);
            _world.Scanned(19.5 + o, 38.5 + o, _surveyDefinition.ScanRadiusCells);
        }
        else
        {
            _knowledge.Scan(32.5 + o, 32.5 + o);
            if (_options.ContainsKey("places") && _pois is not null)
            {
                // V5: two places of the valley already visited by the rover.
                _knowledge.Scan(_pois.Pois[0].X + 0.5, _pois.Pois[0].Y + 0.5);
                _knowledge.Scan(_pois.Pois[1].X + 0.5, _pois.Pois[1].Y + 0.5);
            }
        }

        _shownKnowledge = -1;
        ShowKnowledge();
    }
}
