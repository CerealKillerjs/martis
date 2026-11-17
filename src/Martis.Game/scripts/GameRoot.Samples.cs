using System;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Application;
using Martis.Simulation.Expedition;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// V3 (Documento Maestro v2.0 §4, §6.1, §32, ADR-030): the rover samples measured ground (M),
/// carries the samples back to the lander and the laboratory analyses them one at a time. An
/// analysis earns research and, for a biological sample, offers its dominant genome to the design
/// laboratory. Samples copy the cell; they never change the simulation.
/// </summary>
public partial class GameRoot
{
    private SampleDefinition? _sampleDefinition;
    private SampleStore _samples = null!;

    private void ResetSamples()
    {
        _sampleDefinition ??= SampleDefinition.LoadPublished(_dataRoot);
        _samples = new SampleStore(_sampleDefinition);
        _hud.ShowSamples(_samples, _session.Tick);
    }

    /// <summary>M in exploration: a sample of the ground under the rover.</summary>
    private void CollectHere()
    {
        var (x, y) = _rover.Cell;
        var order = _orders.Issue("sample", FormattableString.Invariant($"({x}, {y})"), "rover", _session.Tick, _samples.Definition.BatteryCost);
        var level = _knowledge.Level(x, y);
        if (level == KnowledgeLevel.Measured && !_samples.Full && !_rover.Spend(_samples.Definition.BatteryCost))
        {
            Resolve(_orders.Fail(order.Id, _session.Tick, "batería insuficiente"));
            _hud.Toast("Batería insuficiente para tomar la muestra. Vuelve al módulo a cargar.", bad: true);
            return;
        }

        var result = _samples.Collect(_session.Inspect(x, y), level, _session.Tick, out var sample);
        switch (result)
        {
            case CollectResult.NotMeasured:
                Resolve(_orders.Fail(order.Id, _session.Tick, "terreno sin medir"));
                _hud.Toast("Primero mide el terreno con el escáner (C): no se muestrea a ciegas.", bad: true);
                return;
            case CollectResult.Full:
                Resolve(_orders.Fail(order.Id, _session.Tick, "contenedor lleno"));
                _hud.Toast("El contenedor del rover está lleno. Vuelve al módulo para entregar las muestras.", bad: true);
                return;
        }

        var kind = sample!.Kind == SampleKind.Biological ? "biológica" : "de regolito";
        Resolve(_orders.Complete(order.Id, _session.Tick, $"muestra #{sample.Id} {kind}"));
        Act("collect-sample");
        _hud.ShowSamples(_samples, _session.Tick);
        _hud.Toast(_landed
            ? string.Create(CultureInfo.InvariantCulture, $"Muestra #{sample.Id} {kind} tomada ({_samples.Carried}/{_samples.Definition.Capacity}). Llévala al módulo para analizarla.")
            : string.Create(CultureInfo.InvariantCulture, $"Muestra #{sample.Id} {kind} tomada: el laboratorio de a bordo la analizará."));
        DeliverSamples();
    }

    /// <summary>Carried samples go to the laboratory near the lander (or at once before landing: on-board lab).</summary>
    private void DeliverSamples()
    {
        if (_samples.Carried == 0)
        {
            return;
        }

        if (_landed && (_rover.Lander is not { } lander || _rover.DistanceTo(lander.X, lander.Y) > _samples.Definition.DeliveryRadiusCells))
        {
            return;
        }

        var delivered = _samples.Deliver(_session.Tick);
        _hud.AddEvents([new SessionEvent(_session.Tick, "lab", string.Create(CultureInfo.InvariantCulture, $"Laboratorio: {delivered} muestra(s) en cola de análisis"))]);
        _hud.ShowSamples(_samples, _session.Tick);
        Act("deliver-sample");
    }

    /// <summary>Analyses finished by the sol that just ended.</summary>
    private void AnalyzeSamples()
    {
        var done = _samples.Advance(_session.Tick);
        foreach (var sample in done)
        {
            var points = _samples.ResearchFor(sample);
            _session.Research?.Earn(points);
            var dominant = sample.Dominant is { } population ? $", domina {population.OrganismName}" : string.Empty;
            var reward = _session.Research is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" · +{points:0} PI");
            _hud.AddEvents([new SessionEvent(_session.Tick, "lab", $"Muestra #{sample.Id} analizada: {(sample.Kind == SampleKind.Biological ? "biológica" : "regolito")}{dominant}{reward}")]);
            Act("analyze-sample");
        }

        if (done.Count > 0 || _samples.All.Any(sample => sample.State == SampleState.Analyzing))
        {
            _hud.ShowSamples(_samples, _session.Tick);
        }
    }

    /// <summary>«Diseñar desde esta muestra»: the sample's dominant genome goes to the design lab.</summary>
    private void DesignFromSample(Sample sample)
    {
        if (sample.Dominant is not { } dominant)
        {
            return;
        }

        if (!_hud.UseGenome(dominant.OrganismId, dominant.Genome))
        {
            _hud.Toast($"{dominant.OrganismName} aún no está disponible en el laboratorio: investígalo primero.", bad: true);
            return;
        }

        SwitchMode(GameMode.Laboratory);
        Act("design-from-sample");
        _hud.Toast($"Genoma de la muestra #{sample.Id} cargado: {dominant.OrganismName}. Experimenta antes de desplegar.");
    }

    /// <summary>Screenshot shelf (<c>--samples</c>): two analysed samples and one in the queue.</summary>
    private void ShowcaseSamples(int o)
    {
        _samples = new SampleStore(_sampleDefinition!);
        foreach (var (x, y) in new[] { (32 + o, 32 + o), (30 + o, 33 + o), (34 + o, 31 + o) })
        {
            _knowledge.Scan(x + 0.5, y + 0.5);
            _samples.Collect(_session.Inspect(x, y), KnowledgeLevel.Measured, _session.Tick - 6, out _);
        }

        _samples.Deliver(_session.Tick - 5);
        _samples.Advance(_session.Tick);
        _hud.ShowSamples(_samples, _session.Tick);
        if (_samples.All.FirstOrDefault(sample => sample.Dominant is not null) is { } living)
        {
            _hud.UseGenome(living.Dominant!.OrganismId, living.Dominant.Genome);
        }
    }
}
