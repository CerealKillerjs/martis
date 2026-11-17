using System;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// M5 (ADR-039): on real Mars the campaign is the story mode, one game from the first bacteria to
/// a civilisation. Every chapter plays on the same section map and the same simulation: the next
/// mission continues the living world (its organisms, knowledge, stations and places) with the
/// next chapter's rules, instead of starting a new map. Each chapter's era opens its sections
/// of the mosaic (M4). On invented terrain (<c>--procedural</c>) missions keep their own maps.
/// </summary>
public partial class GameRoot
{
    /// <summary>True when missions continue one another in the same game (real Mars).</summary>
    private bool Story => Relief() is not null;

    /// <summary>The chapter's rules on the story map: its gradient, transitions and food web, at the section's size.</summary>
    private WorldOptions StoryWorld(CampaignChapter chapter)
        => Relief() is { } relief ? chapter.World with { GridSize = relief.Definition.Cells } : chapter.World;

    /// <summary>World of a saved mission at a map size (loads keep the chapter's rules on the story map).</summary>
    private WorldOptions? MissionWorld(string missionFile, int width)
    {
        var mission = _campaign.Campaign.Missions.FirstOrDefault(item => item.File == missionFile);
        if (mission is null)
        {
            return null;
        }

        var chapter = _campaign.Campaign.ChapterOf(mission.Id);
        return chapter.GridSize == width ? chapter.World : Story ? chapter.World with { GridSize = width } : null;
    }

    /// <summary>«Siguiente misión»: in the story the world goes on; otherwise a new map, as before.</summary>
    private void NextMission(CampaignMission next)
    {
        if (!Story)
        {
            StartMission(next);
            return;
        }

        ContinueStory(next);
    }

    /// <summary>
    /// The next mission on the living world: the simulation is captured and resumed with the next
    /// mission and its chapter's rules; the expedition (rover, knowledge, samples, stations, places
    /// and heaters) carries over. The story always continues in the section where it began.
    /// </summary>
    private void ContinueStory(CampaignMission next)
    {
        if (_planet is not null && _section != _planet.StartSection && _sectionSaves.TryGetValue(_planet.StartSection.Id, out _))
        {
            _hud.Toast("Vuelve a Isidis occidental para seguir la historia: allí está el módulo.", bad: true);
            return;
        }

        var expedition = CaptureExpedition();
        var chapter = _campaign.Campaign.ChapterOf(next.Id);
        var session = GameSession.Resume(_dataRoot, _session.Capture(), next.File, null, StoryWorld(chapter));
        session.AttachEconomy(_campaign.Research!);
        _mission = next;
        StartSession(session);
        RestoreExpedition(expedition);
        _playing = true;
        SetSpeed(0);
        ChapterChanged?.Invoke(ChapterId);
        ReachEra(chapter.Era, announce: true);
        Act("continue-story");
        Brief(next);
    }

    /// <summary>Raises the era reached; a new era opens its sections of Mars.</summary>
    private void ReachEra(int era, bool announce)
    {
        if (era <= _era)
        {
            return;
        }

        var before = _era;
        _era = era;
        RefreshSectionMarks();
        var opened = _planet?.Sections.Where(section => section.UnlockEra > before && section.UnlockEra <= era).Select(section => section.Name).ToArray() ?? [];
        if (announce && opened.Length > 0)
        {
            _hud.AddEvents([new SessionEvent(_session.Tick, "era", $"Era {Eras.Roman(era)} · {Eras.Name(era)}: se abren {string.Join(", ", opened)}")]);
            _hud.Toast(string.Create(CultureInfo.InvariantCulture, $"Era {Eras.Roman(era)}: se abren nuevas secciones de Marte ({string.Join(", ", opened)}). Conduce hasta el borde para cruzar."));
        }
    }
}
