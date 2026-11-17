using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;
using Martis.Simulation.Planets;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// M4 (ADR-038): the world grows section by section. Each open section of the mosaic is its own
/// simulation and its own save game; only the section the rover is in runs. Driving over the
/// edge of a section saves it and opens its neighbour, which first catches up with the sols that
/// passed (the planet's clock is shared), so whatever was seeded there kept living. A locked
/// neighbour says which era opens it. The map of section saves travels with every save.
/// <c>--era=N</c> opens the sections of eras up to N (checks and screenshots).
/// </summary>
public partial class GameRoot
{
    private readonly Dictionary<string, string> _sectionSaves = new(StringComparer.Ordinal);
    private int _era = 1;
    private double _edgeHintClock = -10;

    /// <summary>The highest era reached: it opens the sections of that era and earlier.</summary>
    private int Era => Math.Max(_era, _options.TryGetValue("era", out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var era) ? era : 1);

    private bool IsOpen(SectionDefinition section) => section.UnlockEra <= Era;

    private void RefreshSectionMarks()
    {
        if (_planet is null)
        {
            return;
        }

        _sectionMarks = _planet.Sections
            .Select(section => new SectionMark(section == _section ? $"{section.Name} (aquí)" : section.Name, section.CenterLat, section.CenterLon, section.SizeM, IsOpen(section), section.Unlock))
            .ToArray();
        if (_planetary is not null && Relief() is not null)
        {
            _planetary.SetSections(_sectionMarks, _planetRadiusM);
        }
    }

    /// <summary>Saves the current section, with the map of every section's save.</summary>
    private string SaveSection()
    {
        if (_section is not null && _sectionSaves.TryGetValue(_section.Id, out var id))
        {
            _saves.CurrentId = id;
        }

        var saved = _saves.Save(_session, _mission?.File, CaptureExpedition(), _section is null ? null : WorldJson(_saves.CurrentId));
        if (_section is not null)
        {
            _sectionSaves[_section.Id] = saved;
            // The id is known only now for a first save: store the map again so it names itself.
            _saves.Save(_session, null, null, WorldJson(saved));
        }

        return saved;
    }

    private string WorldJson(string? currentSave)
    {
        var saves = new Dictionary<string, string>(_sectionSaves, StringComparer.Ordinal);
        if (_section is not null && currentSave is not null)
        {
            saves[_section.Id] = currentSave;
        }

        return JsonSerializer.Serialize(new WorldRecord(_section?.Id ?? string.Empty, Era, saves));
    }

    /// <summary>Takes the section map of a loaded save: which section this is and where the others are.</summary>
    private void RestoreWorld(string? json, string? saveId)
    {
        _sectionSaves.Clear();
        if (json is null || _planet is null)
        {
            if (_planet is not null && _section != _planet.StartSection)
            {
                UseSection(_planet.StartSection);
            }

            return;
        }

        var record = JsonSerializer.Deserialize<WorldRecord>(json);
        if (record is null)
        {
            return;
        }

        foreach (var (section, id) in record.Saves)
        {
            _sectionSaves[section] = id;
        }

        _era = Math.Max(1, record.Era);
        if (_planet.Sections.FirstOrDefault(section => section.Id == record.Active) is { } active && active != _section)
        {
            UseSection(active);
        }

        if (_section is not null && saveId is not null)
        {
            _sectionSaves[_section.Id] = saveId;
        }
    }

    /// <summary>Makes <paramref name="section"/> the one the views and places use (the relief changes with it).</summary>
    private void UseSection(SectionDefinition section)
    {
        _section = section;
        _relief = _planet!.LoadRelief(section);
        _valleyDefinition = null;
        DropReal();
        RefreshSectionMarks();
    }

    /// <summary>
    /// Called while driving: at the edge of the section, pushing outwards, the rover crosses into
    /// the neighbour when it is open; a locked one says what opens it.
    /// </summary>
    private void CrossSectionEdge(double throttle)
    {
        if (_planet is null || _section is null || throttle <= 0 || Relief() is null)
        {
            return;
        }

        var (dx, dy) = EdgeDirection();
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var next = _planet.Neighbour(_section, dx, dy);
        var now = Time.GetTicksMsec() / 1000.0;
        if (next is null)
        {
            if (now - _edgeHintClock > 6)
            {
                _edgeHintClock = now;
                _hud.Toast("Fin del mapa conocido de Marte por este lado.");
            }

            return;
        }

        if (!IsOpen(next))
        {
            if (now - _edgeHintClock > 6)
            {
                _edgeHintClock = now;
                _hud.Toast($"{next.Name}: sección bloqueada. Se abre con {next.Unlock}.", bad: true);
            }

            return;
        }

        EnterSection(next, dx, dy);
    }

    /// <summary>Which edge the rover is pushing against: (-1, 0) west, (1, 0) east, (0, -1) north, (0, 1) south.</summary>
    private (int Dx, int Dy) EdgeDirection()
    {
        const double edge = 0.62;
        var cos = Math.Cos(_rover.Heading);
        var sin = Math.Sin(_rover.Heading);
        if (_rover.X <= edge && cos < -0.3) return (-1, 0);
        if (_rover.X >= _session.Width - edge && cos > 0.3) return (1, 0);
        if (_rover.Y <= edge && sin < -0.3) return (0, -1);
        if (_rover.Y >= _session.Height - edge && sin > 0.3) return (0, 1);
        return (0, 0);
    }

    /// <summary>
    /// Saves this section and opens <paramref name="next"/> (loaded from its save, or new), caught
    /// up to the shared sol; the rover keeps its battery and wear and appears on the matching edge.
    /// </summary>
    private void EnterSection(SectionDefinition next, int dx, int dy)
    {
        var from = _section!;
        var (x, y, heading, battery, wear) = (_rover.X, _rover.Y, _rover.Heading, _rover.Battery, _rover.Wear);
        var sol = _session.Tick;
        var mission = _mission;
        SaveSection();

        UseSection(next);
        GameSession session;
        string? expedition = null;
        if (_sectionSaves.TryGetValue(next.Id, out var id) && _saves.LoadById(id, out _, out _, _campaign.Campaign.WorldFor, out expedition) is { } loaded)
        {
            session = loaded;
            _saves.CurrentId = id;
        }
        else
        {
            // A section never visited starts as fresh Mars; it has no mission of its own.
            session = GameSession.NewGame(_dataRoot, _session.Seed + StableSeed(next.Id), withMission: false, _session.World);
            _saves.CurrentId = null;
        }

        var behind = sol - session.Tick;
        if (behind > 0)
        {
            session.Advance((int)behind); // the planet's clock is shared: catch up before arriving
        }

        _mission = session.Mission is null ? null : mission;
        StartSession(session);
        RestoreExpedition(expedition);
        var nx = dx > 0 ? 0 : dx < 0 ? session.Width - 1 : (int)x;
        var ny = dy > 0 ? 0 : dy < 0 ? session.Height - 1 : (int)y;
        _rover.Restore(nx + 0.5, ny + 0.5, heading, battery, wear, _rover.Lander);
        PoseRover(0f);
        Act("enter-section");
        _hud.AddEvents([new SessionEvent(session.Tick, "section", $"El rover cruza de {from.Name} a {next.Name}")]);
        _hud.Toast(behind > 0
            ? string.Create(CultureInfo.InvariantCulture, $"Entras en {next.Name}. Su simulación se ha puesto al día: {behind} soles.")
            : $"Entras en {next.Name}.");
    }

    /// <summary>A seed offset from a section id that is the same on every run (FNV-1a).</summary>
    private static ulong StableSeed(string id)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in id)
        {
            hash = (hash ^ c) * 1099511628211UL;
        }

        return hash & 0xffffffffUL;
    }

    /// <summary>Forgets the real-scale view so it is built again for another section.</summary>
    private void DropReal()
    {
        if (_real is null)
        {
            return;
        }

        if (_realActive && _world is not null)
        {
            _world.Visible = true;
            _world.Camera.MakeCurrent();
        }

        _real.QueueFree();
        _real = null;
        _realTerrain = null;
        _realRover = null;
        _realLander = null;
        _realSun = null;
        _realCamera = null;
        _realActive = false;
    }

    /// <summary>The section map stored with each section's save (M4).</summary>
    private sealed record WorldRecord(string Active, int Era, Dictionary<string, string> Saves);
}
