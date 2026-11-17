using System.Text.Json;
using Martis.Domain.Parameters;
using Martis.Simulation.Economy;
using Martis.Simulation.Sessions;

namespace Martis.Simulation.Missions;

/// <summary>
/// One mission of the campaign, in play order. <see cref="File"/> is under <c>data/missions</c>.
/// <see cref="Teaches"/> names the concepts (H8) the player learns by finishing it.
/// </summary>
public sealed record CampaignMission(string Id, string File, string ChapterId, int Number, IReadOnlyList<string>? Teaches = null, MissionKind Kind = MissionKind.Colonization)
{
    public IReadOnlyList<string> Concepts => Teaches ?? [];
}

/// <summary>
/// A concept of the codex (H8): what a mission teaches, shown in its briefing and debrief.
/// <see cref="Epistemic"/> labels how solid it is (master document v2.0, §2 and §36.1).
/// </summary>
public sealed record CampaignConcept(string Id, string Title, string Text, Epistemic Epistemic = Epistemic.Science);

/// <summary>Mission types of the master document v2.0, §11.</summary>
public enum MissionKind
{
    Reconnaissance,
    Sampling,
    Experiment,
    Colonization,
    Restoration,
    Terraforming,
    Expedition,
}

/// <summary>How a concept should be read (master document v2.0, §2: «rigor transparente»).</summary>
public enum Epistemic
{
    /// <summary>Established science.</summary>
    Science,

    /// <summary>A simplified model of real science, tuned for play.</summary>
    Model,

    /// <summary>Speculative: a game hypothesis, labelled as such.</summary>
    Speculative,
}

/// <summary>The eras of progression of the master document v2.0, §5.</summary>
public static class Eras
{
    public static readonly IReadOnlyList<string> Names = ["Génesis", "Ecosistemas", "Inteligencia", "Expansión", "Biosferas múltiples"];

    public static string Roman(int era) => era switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => throw new ArgumentOutOfRangeException(nameof(era)) };

    public static string Name(int era) => Names[era - 1];
}

/// <summary>
/// One chapter. <see cref="GridSize"/> is the side of the map its missions use (64, 128 or
/// 256, Daniel 2026-11-16). A chapter with no missions is announced but not playable yet.
/// </summary>
public sealed record CampaignChapter(string Id, int Number, string Title, string Summary, int GridSize, IReadOnlyList<CampaignMission> Missions, int? PopulationBudget = null, string? Gradient = null, string? Transitions = null, string? Trophic = null, GuidanceLevel Guidance = GuidanceLevel.Hints, int Era = 1)
{
    public bool Playable => Missions.Count > 0;

    /// <summary>Map size, population budget (H3) and environment gradient (H4) of this chapter's missions.</summary>
    public WorldOptions World => new(GridSize, PopulationBudget, Gradient, Transitions, Trophic);
}

/// <summary>
/// The story mode (campaign «Génesis», ADR-019): chapters of missions played in order,
/// from the first bacteria to complex and intelligent life. Loaded from
/// <c>data/campaign/*.json</c>.
/// </summary>
public sealed record CampaignDefinition(string Id, string Name, IReadOnlyList<CampaignChapter> Chapters, IReadOnlyList<CampaignConcept>? ConceptList = null, GuidancePolicy? GuidancePolicy = null)
{
    public const string GenesisFile = "genesis.v1.json";

    public static readonly IReadOnlySet<int> GridSizes = new HashSet<int> { 64, 128, 256 };

    /// <summary>Every playable mission, in play order.</summary>
    public IReadOnlyList<CampaignMission> Missions => [.. Chapters.SelectMany(chapter => chapter.Missions)];

    /// <summary>World of the first chapter with that map size, for free play on a big map; null for an unknown size.</summary>
    public WorldOptions? WorldFor(int gridSize)
        => Chapters.FirstOrDefault(chapter => chapter.GridSize == gridSize)?.World;

    /// <summary>Concepts of the codex in the order the campaign teaches them (H8).</summary>
    public IReadOnlyList<CampaignConcept> Concepts => ConceptList ?? [];

    /// <summary>When hints appear in each guidance level (H8).</summary>
    public GuidancePolicy Guidance => GuidancePolicy ?? GuidancePolicy.Default;

    public CampaignConcept Concept(string conceptId)
        => Concepts.FirstOrDefault(concept => concept.Id == conceptId)
            ?? throw new KeyNotFoundException($"The campaign has no concept '{conceptId}'.");

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
        var parameters = ParameterCatalog.Load(json);
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

                IReadOnlyList<string>? teaches = mission.TryGetProperty("teaches", out var taught)
                    ? [.. taught.EnumerateArray().Select(concept => concept.GetString()!)]
                    : null;
                var kind = mission.TryGetProperty("kind", out var kindItem) ? ParseKind(id, kindItem.GetString()) : MissionKind.Colonization;
                missions.Add(new CampaignMission(id, mission.GetProperty("file").GetString()!, chapterId, missions.Count + 1, teaches, kind));
            }

            chapters.Add(new CampaignChapter(
                chapterId,
                number,
                item.GetProperty("title").GetString()!,
                item.GetProperty("summary").GetString()!,
                grid,
                missions,
                item.TryGetProperty("population_budget", out var budget) ? (int)parameters.RequireNumber(budget.GetString()!).Value : null,
                item.TryGetProperty("gradient", out var gradient) ? gradient.GetString() : null,
                item.TryGetProperty("transitions", out var transitions) ? transitions.GetString() : null,
                item.TryGetProperty("trophic", out var trophic) ? trophic.GetString() : null,
                item.TryGetProperty("guidance", out var guidance) ? ParseGuidance(chapterId, guidance.GetString()) : GuidanceLevel.Hints,
                item.TryGetProperty("era", out var era) ? era.GetInt32() : 1));
            if (chapters[^1].Era is < 1 or > 5 || (chapters.Count > 1 && chapters[^1].Era < chapters[^2].Era))
            {
                throw new InvalidDataException($"Chapter '{chapterId}' needs an era from 1 to 5 that does not go back.");
            }
        }

        var concepts = new List<CampaignConcept>();
        if (document.RootElement.TryGetProperty("concepts", out var conceptItems))
        {
            foreach (var item in conceptItems.EnumerateArray())
            {
                var epistemic = item.TryGetProperty("epistemic", out var label) ? label.GetString() switch
                {
                    "science" => Epistemic.Science,
                    "model" => Epistemic.Model,
                    "speculative" => Epistemic.Speculative,
                    var other => throw new InvalidDataException($"Unknown epistemic label '{other}'; use science, model or speculative."),
                } : Epistemic.Science;
                var concept = new CampaignConcept(item.GetProperty("id").GetString()!, item.GetProperty("title").GetString()!, item.GetProperty("text").GetString()!, epistemic);
                if (concepts.Any(other => other.Id == concept.Id) || string.IsNullOrWhiteSpace(concept.Title) || string.IsNullOrWhiteSpace(concept.Text))
                {
                    throw new InvalidDataException($"Concept '{concept.Id}' is repeated or has no title or text.");
                }

                concepts.Add(concept);
            }
        }

        var taughtBy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mission in chapters.SelectMany(chapter => chapter.Missions))
        {
            foreach (var concept in mission.Concepts)
            {
                if (concepts.All(item => item.Id != concept))
                {
                    throw new InvalidDataException($"Mission '{mission.Id}' teaches unknown concept '{concept}'.");
                }

                if (!taughtBy.TryAdd(concept, mission.Id))
                {
                    throw new InvalidDataException($"Concept '{concept}' is taught by both '{taughtBy[concept]}' and '{mission.Id}'.");
                }
            }
        }

        GuidancePolicy? policy = null;
        if (meta.TryGetProperty("guidance", out var policyItem))
        {
            double Read(string name) => parameters.RequireNumber(policyItem.GetProperty(name).GetString()!).Value;
            policy = new GuidancePolicy(Read("on_request_stall_sols"), Read("minimal_stall_sols"));
            if (!(policy.OnRequestStallSols > 0) || policy.MinimalStallSols < policy.OnRequestStallSols)
            {
                throw new InvalidDataException("Guidance needs 0 < on_request_stall_sols <= minimal_stall_sols.");
            }
        }

        return new CampaignDefinition(meta.GetProperty("id").GetString()!, meta.GetProperty("name").GetString()!, chapters, concepts, policy);
    }

    private static MissionKind ParseKind(string missionId, string? value) => value switch
    {
        "reconnaissance" => MissionKind.Reconnaissance,
        "sampling" => MissionKind.Sampling,
        "experiment" => MissionKind.Experiment,
        "colonization" => MissionKind.Colonization,
        "restoration" => MissionKind.Restoration,
        "terraforming" => MissionKind.Terraforming,
        "expedition" => MissionKind.Expedition,
        _ => throw new InvalidDataException($"Mission '{missionId}' has unknown kind '{value}'."),
    };

    private static GuidanceLevel ParseGuidance(string chapterId, string? value) => value switch
    {
        "tutorial" => GuidanceLevel.Tutorial,
        "hints" => GuidanceLevel.Hints,
        "on_request" => GuidanceLevel.OnRequest,
        "minimal" => GuidanceLevel.Minimal,
        "none" => GuidanceLevel.None,
        _ => throw new InvalidDataException($"Chapter '{chapterId}' has unknown guidance '{value}'; use tutorial, hints, on_request, minimal or none."),
    };
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

    /// <summary>Concepts of the finished missions, in the order the campaign teaches them (the codex, H8).</summary>
    public IReadOnlyList<CampaignConcept> Learned
        => [.. Campaign.Missions.Where(mission => _completed.Contains(mission.Id)).SelectMany(mission => mission.Concepts).Select(Campaign.Concept)];

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
