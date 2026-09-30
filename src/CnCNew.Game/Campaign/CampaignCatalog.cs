using System.Collections.Immutable;
using System.Globalization;

namespace CnCNew.Game.Campaign;

/// <summary>One <c>Mission</c> block of a campaign (<c>CampaignManager::parseMissionPart</c>, <c>CampaignManager.cpp</c>).</summary>
/// <param name="MapPath">Virtual content path of the map (<c>Maps/USA01/USA01.map</c>): the INI writes it with backslashes.</param>
/// <param name="NextMission">Name of the following mission in the same campaign; empty for the last one.</param>
public sealed record CampaignMission(
    string Name, string MapPath, string NextMission, string IntroMovie, ImmutableArray<string> ObjectiveLabels, string BriefingVoice,
    ImmutableArray<string> UnitNames, string LocationLabel, int VoiceLengthSeconds, string GeneralName);

/// <summary>One <c>Campaign</c> block: a named chain of missions. <see cref="PlayerFaction"/> is only set by Generals' Challenge campaigns, where the campaign fixes the human's faction.</summary>
public sealed record CampaignDefinition(
    string Name, string NameLabel, string FirstMission, string FinalVictoryMovie, bool IsChallenge, string PlayerFaction, ImmutableArray<CampaignMission> Missions)
{
    public CampaignMission? FindMission(string name) => Missions.FirstOrDefault(mission => string.Equals(mission.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The missions in play order: from <see cref="FirstMission"/> along <see cref="CampaignMission.NextMission"/> (a cycle or a dangling reference ends the walk).</summary>
    public ImmutableArray<CampaignMission> Sequence
    {
        get
        {
            var order = ImmutableArray.CreateBuilder<CampaignMission>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var mission = FindMission(FirstMission); mission is not null && seen.Add(mission.Name); mission = FindMission(mission.NextMission)) order.Add(mission);
            return order.ToImmutable();
        }
    }

    /// <summary><c>Campaign::getNextMission</c>: the mission after <paramref name="mission"/>, or <see langword="null"/> at the end of the campaign.</summary>
    public CampaignMission? NextOf(CampaignMission mission) => mission.NextMission.Length == 0 ? null : FindMission(mission.NextMission);
}

/// <summary>
/// The campaigns of <c>Data/INI/Campaign.ini</c>. The file is not part of the compiled definition graph (the load plan lists it as
/// not loaded): its format is a fixed, flat subset of the INI syntax (<c>Key Value</c> lines, <c>Campaign</c>/<c>Mission</c> blocks closed by
/// <c>END</c>), so it is read here, by the layer that owns campaign flow. Unknown keys are not skipped silently: they are collected in
/// <see cref="UnknownKeys"/> and listed by the tools.
/// </summary>
public sealed class CampaignCatalog
{
    private CampaignCatalog(ImmutableArray<CampaignDefinition> campaigns, ImmutableArray<string> unknownKeys)
    {
        Campaigns = campaigns;
        UnknownKeys = unknownKeys;
    }

    public ImmutableArray<CampaignDefinition> Campaigns { get; }

    /// <summary><c>Block.Key</c> entries the parser did not recognize, each once, in file order.</summary>
    public ImmutableArray<string> UnknownKeys { get; }

    public CampaignDefinition? Find(string name) => Campaigns.FirstOrDefault(campaign => string.Equals(campaign.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Virtual path of a map as the INI spells it (<c>Maps\USA01\USA01.map</c>).</summary>
    public static string NormalizeMapPath(string path) => path.Replace('\\', '/').Trim();

    public static CampaignCatalog Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        return Parse(reader.ReadToEnd());
    }

    public static CampaignCatalog Parse(string text)
    {
        var campaigns = ImmutableArray.CreateBuilder<CampaignDefinition>();
        var unknown = ImmutableArray.CreateBuilder<string>();
        CampaignBuilder? campaign = null;
        MissionBuilder? mission = null;
        var lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            var line = raw;
            var comment = line.IndexOf(';');
            if (comment >= 0) line = line[..comment];
            line = line.Trim();
            if (line.Length == 0) continue;
            var (key, value) = Split(line);
            if (key.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                if (mission is not null) { campaign!.Missions.Add(mission.Build()); mission = null; }
                else if (campaign is not null) { campaigns.Add(campaign.Build()); campaign = null; }
                else throw new InvalidDataException($"Campaign.ini line {lineNumber}: END without an open block.");
                continue;
            }
            if (campaign is null)
            {
                if (!key.Equals("Campaign", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Campaign.ini line {lineNumber}: '{key}' outside a Campaign block.");
                campaign = new CampaignBuilder(value);
                continue;
            }
            if (mission is null && key.Equals("Mission", StringComparison.OrdinalIgnoreCase))
            {
                mission = new MissionBuilder(value);
                continue;
            }
            if (mission is not null) mission.Set(key, value, unknown, campaign.Name);
            else campaign.Set(key, value, unknown);
        }
        if (campaign is not null || mission is not null) throw new InvalidDataException("Campaign.ini ends inside an unclosed block.");
        return new CampaignCatalog(campaigns.ToImmutable(), unknown.ToImmutable());
    }

    /// <summary>Everything wrong with the catalog: a missing first mission, dangling or cyclic <c>NextMission</c>, unreachable missions, duplicate names, and maps <paramref name="mapExists"/> does not know.</summary>
    public ImmutableArray<string> Validate(Func<string, bool> mapExists)
    {
        var problems = ImmutableArray.CreateBuilder<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var campaign in Campaigns)
        {
            if (!names.Add(campaign.Name)) problems.Add($"campaign {campaign.Name}: defined twice");
            if (campaign.FindMission(campaign.FirstMission) is null) problems.Add($"campaign {campaign.Name}: first mission '{campaign.FirstMission}' does not exist");
            var missionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mission in campaign.Missions)
            {
                if (!missionNames.Add(mission.Name)) problems.Add($"campaign {campaign.Name}: mission {mission.Name} defined twice");
                if (mission.NextMission.Length > 0 && campaign.FindMission(mission.NextMission) is null) problems.Add($"campaign {campaign.Name}: mission {mission.Name} continues with unknown '{mission.NextMission}'");
                if (mission.MapPath.Length == 0) problems.Add($"campaign {campaign.Name}: mission {mission.Name} has no map");
                else if (!mapExists(mission.MapPath)) problems.Add($"campaign {campaign.Name}: mission {mission.Name} map '{mission.MapPath}' is not in the content");
            }
            var reachable = campaign.Sequence.Select(mission => mission.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var mission in campaign.Missions)
                if (!reachable.Contains(mission.Name)) problems.Add($"campaign {campaign.Name}: mission {mission.Name} cannot be reached from {campaign.FirstMission}");
        }
        return problems.ToImmutable();
    }

    private static (string Key, string Value) Split(string line)
    {
        var space = line.IndexOfAny([' ', '\t', '=']);
        if (space < 0) return (line, string.Empty);
        var value = line[(space + 1)..].Trim();
        if (value.StartsWith('=')) value = value[1..].Trim();
        return (line[..space], value);
    }

    private static bool Bool(string value) => value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private sealed class CampaignBuilder(string name)
    {
        public string Name { get; } = name;
        public List<CampaignMission> Missions { get; } = [];
        private string _label = string.Empty, _first = string.Empty, _movie = string.Empty, _faction = string.Empty;
        private bool _challenge;

        public void Set(string key, string value, ImmutableArray<string>.Builder unknown)
        {
            switch (key.ToLowerInvariant())
            {
                case "campaignnamelabel": _label = value; break;
                case "firstmission": _first = value; break;
                case "finalvictorymovie": _movie = value; break;
                case "ischallengecampaign": _challenge = Bool(value); break;
                case "playerfaction": _faction = value; break;
                default: Unknown(unknown, $"{Name}.{key}"); break;
            }
        }

        public CampaignDefinition Build() => new(Name, _label, _first, _movie, _challenge, _faction, [..Missions]);
    }

    private sealed class MissionBuilder(string name)
    {
        private readonly string[] _objectives = new string[5];
        private readonly string[] _units = new string[3];
        private string _map = string.Empty, _next = string.Empty, _movie = string.Empty, _voice = string.Empty, _location = string.Empty, _general = string.Empty;
        private int _length;

        public void Set(string key, string value, ImmutableArray<string>.Builder unknown, string campaign)
        {
            var lower = key.ToLowerInvariant();
            if (lower.StartsWith("objectiveline", StringComparison.Ordinal) && int.TryParse(lower["objectiveline".Length..], out var objective) && objective is >= 0 and < 5) { _objectives[objective] = value; return; }
            if (lower.StartsWith("unitnames", StringComparison.Ordinal) && int.TryParse(lower["unitnames".Length..], out var unit) && unit is >= 0 and < 3) { _units[unit] = value; return; }
            switch (lower)
            {
                case "map": _map = NormalizeMapPath(value); break;
                case "nextmission": _next = value; break;
                case "intromovie": _movie = value; break;
                case "briefingvoice": _voice = value; break;
                case "locationnamelabel": _location = value; break;
                case "generalname": _general = value; break;
                case "voicelength": _length = int.Parse(value.TrimStart('=', ' '), CultureInfo.InvariantCulture); break;
                default: Unknown(unknown, $"{campaign}.{name}.{key}"); break;
            }
        }

        public CampaignMission Build() => new(name, _map, _next, _movie, [.._objectives.Where(value => !string.IsNullOrEmpty(value))!], _voice, [.._units.Where(value => !string.IsNullOrEmpty(value))!], _location, _length, _general);
    }

    private static void Unknown(ImmutableArray<string>.Builder unknown, string key)
    {
        if (!unknown.Contains(key)) unknown.Add(key);
    }
}
