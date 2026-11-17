using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Simulation.Expedition;

namespace Martis.Game;

/// <summary>
/// V3 (ADR-030): the laboratory's sample shelf. It lists what the rover carries, what waits in
/// the analysis queue and what is done; a biological sample's dominant genome can be loaded into
/// the design laboratory, closing the loop «sample → analyse → design → deploy».
/// </summary>
public partial class Hud
{
    private PanelContainer? _samplesPanel;
    private VBoxContainer _sampleRows = null!;
    private Label _samplesSummary = null!;

    /// <summary>The player wants to design from an analysed sample.</summary>
    public event Action<Sample>? GenomeRequested;

    public void SetSamplesVisible(bool visible)
    {
        EnsureSamples();
        _samplesPanel!.Visible = visible;
    }

    public void ShowSamples(SampleStore store, long sol)
    {
        EnsureSamples();
        foreach (var child in _sampleRows.GetChildren())
        {
            child.QueueFree();
        }

        var analyzing = store.All.Count(sample => sample.State == SampleState.Analyzing);
        var analyzed = store.All.Count(sample => sample.State == SampleState.Analyzed);
        _samplesSummary.Text = string.Create(CultureInfo.InvariantCulture, $"En el rover {store.Carried}/{store.Definition.Capacity} · en cola {analyzing} · analizadas {analyzed}");
        if (store.All.Count == 0)
        {
            _sampleRows.AddChild(Muted("Escanea con el rover (C) y toma muestras de terreno medido (M). Vuelve al módulo para entregarlas."));
            return;
        }

        // The newest six, plus any older analysed biological sample: those are the ones the
        // player can design from, so they must not scroll out of the shelf.
        var newest = store.All.Reverse().Take(6).ToList();
        var designable = store.All.Reverse().Skip(6).Where(sample => sample.State == SampleState.Analyzed && sample.Dominant is not null).Take(3);
        foreach (var sample in newest.Concat(designable))
        {
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 2);
            var kind = sample.Kind == SampleKind.Biological ? "biológica" : "regolito";
            var colour = sample.Kind == SampleKind.Biological ? Bio : new Color(1f, 0.78f, 0.55f);
            var title = new Label { Text = string.Create(CultureInfo.InvariantCulture, $"#{sample.Id} {kind} ({sample.X}, {sample.Y}) · sol {sample.TakenSol}") };
            title.AddThemeColorOverride("font_color", colour);
            title.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(title);
            row.AddChild(Muted(sample.State switch
            {
                SampleState.Carried => "En el contenedor del rover: llévala al módulo.",
                SampleState.Analyzing => string.Create(CultureInfo.InvariantCulture, $"Analizando: lista el sol {sample.ReadySol} (faltan {Math.Max(0, sample.ReadySol!.Value - sol)})."),
                _ => Finding(sample),
            }));
            if (sample.State == SampleState.Analyzed && sample.Dominant is not null)
            {
                row.AddChild(Action("Diseñar desde esta muestra", () => GenomeRequested?.Invoke(sample), "Carga el genoma dominante de la muestra en el laboratorio de diseño"));
            }
            else if (sample.Kind == SampleKind.Biological)
            {
                row.AddChild(Hint("«Diseñar desde esta muestra» aparecerá cuando termine el análisis."));
            }
            else if (sample.State == SampleState.Analyzed)
            {
                row.AddChild(Hint("Sin vida en la muestra: no hay genoma para diseñar. Toma muestras donde haya biomasa (capa Biomasa) para diseñar desde ellas."));
            }

            _sampleRows.AddChild(row);
        }
    }

    /// <summary>Selects <paramref name="organismId"/> and moves the sliders to <paramref name="genome"/> within the editing limits.</summary>
    public bool UseGenome(string organismId, IReadOnlyDictionary<string, double> genome)
    {
        var index = -1;
        for (var i = 0; i < _organisms.Count; i++)
        {
            if (_organisms[i].Id == organismId)
            {
                index = i;
            }
        }

        if (index < 0 || _organism.IsItemDisabled(index))
        {
            return false;
        }

        _organism.Select(index);
        RebuildGenes();
        foreach (var (gene, value) in genome)
        {
            if (_genes.TryGetValue(gene, out var slot))
            {
                slot.Slider.Value = Math.Clamp(value, slot.Slider.MinValue, slot.Slider.MaxValue);
            }
        }

        return true;
    }

    private static string Finding(Sample sample)
    {
        var source = sample.Source;
        var water = source.ResourcesKg.TryGetValue("Water", out var kg) ? kg : 0;
        var text = string.Create(CultureInfo.InvariantCulture, $"Analizada: {source.TemperatureKelvin ?? 0:0} K · agua {Mass(water)} · {source.RadiationSvPerSol * 1000:0.00} mSv/sol");
        if (sample.Dominant is { } dominant)
        {
            var genes = string.Join(" ", dominant.Genome.OrderByDescending(gene => gene.Value).Take(3).Select(gene => string.Create(CultureInfo.InvariantCulture, $"{Gene(gene.Key)} {gene.Value:0.00}")));
            text += string.Create(CultureInfo.InvariantCulture, $"\n{source.Populations.Count} poblaciones · domina #{dominant.Id} {dominant.OrganismName}: {genes}");
        }

        return text;
    }

    private static Label Hint(string text)
    {
        var label = Muted(text);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.55f, 0.8f));
        return label;
    }

    private static Label Muted(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(266, 0) };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.65f));
        return label;
    }

    private void EnsureSamples()
    {
        if (_samplesPanel is not null)
        {
            return;
        }

        var panel = _samplesPanel = Panel(new Vector2(290, 0));
        panel.SetAnchorsPreset(LayoutPreset.TopLeft);
        panel.OffsetLeft = 12;
        panel.OffsetTop = 70;
        panel.Visible = false;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        box.AddChild(Caption("MUESTRAS"));
        _samplesSummary = new Label();
        _samplesSummary.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_samplesSummary);
        _sampleRows = new VBoxContainer();
        _sampleRows.AddThemeConstantOverride("separation", 10);
        box.AddChild(_sampleRows);
        AddChild(panel);
    }
}
