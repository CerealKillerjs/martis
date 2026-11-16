using System.Text.Json;
using Martis.Simulation.Economy;

namespace Martis.Simulation.Missions;

/// <summary>One mission of the campaign, in play order. <see cref="File"/> is under <c>data/missions</c>.</summary>
public sealed record CampaignMission(string Id, string File, string ChapterId, int Number);

/// <summary>
/// One chapter. <see cref="GridSize"/> is the side of the map its missions use (64, 128 or
/// 256, Daniel 2026-11-16). A chapter with no missions is announced but not playable yet.
/// </summary>
public sealed record CampaignChapter(string Id, int Number, string Title, string Summary, int GridSize, IReadOnlyList<CampaignMission> Missions)
{
    public bool Playable => Missions.Count > 0;
}

/// <summary>
/// The story mode (campaign «Génesis», ADR-019): chapters of missions played in order,
/// from the first bacteria to complex and intelligent life. Loaded from
/// <c>data/campaign/*.json</c>.
/// </summary>
public sealed record CampaignDefinition(string Id, string Name, IReadOnlyList<CampaignChapter> Chapters)
{
    public const string GenesisFile = "genesis.v1.json";

    public static readonly IReadOnlySet<int> GridSizes = new HashSet<int> { 64, 128, 256 };

    /// <summary>Every playable mission, in play order.</summary>
    public IReadOnlyList<CampaignMission> Missions => [.. Chapters.SelectMany(chapter => chapter.Missions)];

    public CampaignChapter ChapterOf(string missionId)
        => Chapters.First(chapter => chapter.Missions.Any(mission => mission.Id == missionId));

    public CampaignMission Mission(string missionId)
        => Missions.FirstOrDefault(mission => mission.Id == missionId)
            ?? throw new KeyNotFoundException($"The campaign has no mission '{missionId}'.");

    public static CampaignDefinition LoadPublished(string repositoryRoot, string fileName = GenesisFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Load(System.IO.File.ReadAllText(Path.Combine(repositoryRoot, "data", "campaign", fileName)));
    }

    public static CampaignDefinition Load(string json)
    {
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.campaign")
        {
            throw new InvalidDataException("A campaign file needs metadata.schema = martis.campaign.");
        }

        var chapters = new List<CampaignChapter>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var item in document.RootElement.GetProperty("chapters").EnumerateArray())
        {
            number++;
            var chapterId = item.GetProperty("id").GetString()!;
            var grid = item.GetProperty("grid").GetInt32();
            if (!GridSizes.Contains(grid))
            {
                throw new InvalidDataException($"Chapter '{chapterId}' has grid {grid}; use 64, 128 or 256.");
            }

            var missions = new List<CampaignMission>();
            foreach (var mission in item.GetProperty("missions").EnumerateArray())
            {
                var id = mission.GetProperty("id").GetString()!;
                if (!seen.Add(id))
                {
                    throw new InvalidDataException($"Mission '{id}' appears twice in the campaign.");
                }

                missions.Add(new CampaignMission(id, mission.GetProperty("file").GetString()!, chapterId, missions.Count + 1));
            }

            chapters.Add(new CampaignChapter(
                chapterId,
                number,
                item.GetProperty("title").GetString()!,
                item.GetProperty("summary").GetString()!,
                grid,
                missions));
        }

        return new CampaignDefinition(meta.GetProperty("id").GetString()!, meta.GetProperty("name").GetString()!, chapters);
    }
}

/// <summary>
/// Which campaign missions the player has finished. A mission is unlocked when it is the
/// first one or the one before it is complete, so the story is played in order.
/// </summary>
public sealed class CampaignProgress
{
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);

    public CampaignProgress(CampaignDefinition campaign)
    {
        Campaign = campaign ?? throw new ArgumentNullException(nameof(campaign));
    }

    public CampaignDefinition Campaign { get; }

    /// <summary>Research points and technologies of the campaign (H2); null when the host plays without an economy.</summary>
    public ResearchState? Research { get; set; }

    public IReadOnlyCollection<string> Completed => _completed;

    /// <summary>The first unfinished mission, or null when every playable mission is done.</summary>
    public CampaignMission? Next => Campaign.Missions.FirstOrDefault(mission => !_completed.Contains(mission.Id));

    public bool IsComplete(string missionId) => _completed.Contains(missionId);

    public bool IsUnlocked(string missionId)
    {
        var missions = Campaign.Missions;
        var index = missions.ToList().FindIndex(mission => mission.Id == missionId);
        return index == 0 || (index > 0 && _completed.Contains(missions[index - 1].Id));
    }

    /// <summary>Marks a mission complete. Returns false for a locked or unknown mission, which is not recorded.</summary>
    public bool Complete(string missionId)
    {
        if (!IsUnlocked(missionId))
        {
            return false;
        }

        _completed.Add(missionId);
        return true;
    }

    public string ToJson()
        => JsonSerializer.Serialize(new Dto(Campaign.Id, [.. _completed.Order(StringComparer.Ordinal)], Research?.ToJson()));

    /// <summary>
    /// Reads saved progress; ids the campaign no longer has are dropped. With an economy,
    /// research comes from the save or starts fresh when the save has none.
    /// </summary>
    public static CampaignProgress FromJson(CampaignDefinition campaign, string json, EconomyDefinition? economy = null)
    {
        var progress = new CampaignProgress(campaign);
        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The campaign progress is not valid JSON.", exception);
        }

        if (dto is null || !string.Equals(dto.CampaignId, campaign.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The saved progress belongs to another campaign.");
        }

        var known = campaign.Missions.Select(mission => mission.Id).ToHashSet(StringComparer.Ordinal);
        progress._completed.UnionWith((dto.Completed ?? []).Where(known.Contains));
        if (economy is not null)
        {
            progress.Research = dto.Research is null ? new ResearchState(economy) : ResearchState.FromJson(economy, dto.Research);
        }

        return progress;
    }

    private sealed record Dto(string CampaignId, string[]? Completed, string? Research = null);
}

/// <summary>
/// What a save stores next to a snapshot so the mission resumes (H-31): the mission file
/// and the tracker progress at that tick. Hosts write it as a <c>mission-progress</c> event
/// of the save game (ADR-016 left «evento o snapshot» open; an event needs no new migration).
/// </summary>
public sealed record MissionSaveRecord(string MissionFile, MissionProgress Progress, SessionEconomyState? Economy = null)
{
    public const string EventType = "mission-progress";

    public string ToJson()
        => JsonSerializer.Serialize(new Dto(MissionFile, Progress.ToJson(), Economy?.InoculumKg, Economy?.MutationPoints));

    public static MissionSaveRecord FromJson(string json)
    {
        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The saved mission is not valid JSON.", exception);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.MissionFile) || dto.MissionFile.IndexOfAny(['/', '\\']) >= 0 || string.IsNullOrWhiteSpace(dto.Progress))
        {
            throw new InvalidDataException("The saved mission is incomplete.");
        }

        SessionEconomyState? economy = null;
        if (dto.InoculumKg is { } inoculum)
        {
            var mutation = dto.MutationPoints ?? 0.0;
            if (!double.IsFinite(inoculum) || inoculum < 0 || !double.IsFinite(mutation) || mutation < 0)
            {
                throw new InvalidDataException("The saved economy is not valid.");
            }

            economy = new SessionEconomyState(inoculum, mutation);
        }

        return new MissionSaveRecord(dto.MissionFile, MissionProgress.FromJson(dto.Progress), economy);
    }

    private sealed record Dto(string MissionFile, string Progress, double? InoculumKg = null, double? MutationPoints = null);
}
