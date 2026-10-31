using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CnCNew.Simulation.Scripting;

namespace CnCNew.Game.Campaign;

/// <summary>The bits of <c>BattleHonors.h</c> a local player can earn; the values are the original's, so the file reads the same.</summary>
[Flags]
public enum BattleHonor
{
    None = 0,
    Streak = 0x00000002,
    LoyaltyUsa = 0x00000020,
    LoyaltyChina = 0x00000040,
    BattleTank = 0x00000080,
    AirWing = 0x00000100,
    LoyaltyGla = 0x00000200,
    Endurance = 0x00000400,
    CampaignUsa = 0x00000800,
    CampaignChina = 0x00001000,
    CampaignGla = 0x00002000,
    Blitz5 = 0x00004000,
    Blitz10 = 0x00008000,
    Apocalypse = 0x00020000,
    Domination = 0x00080000,
    ChallengeMode = 0x00100000,
    Ultimate = 0x00200000,
}

/// <summary>One map of the skirmish list as the honors ask for it: <c>m_isOfficial</c>, <c>m_isMultiplayer</c> and the number of players.</summary>
public sealed record HonorMap(string Name, int Players);

/// <summary>One picture of the honors list (<c>InsertBattleHonor</c>): the mapped image, whether it is earned (greyed otherwise) and its tooltip's label.</summary>
public sealed record HonorEntry(string Image, bool Earned, string Tooltip);

/// <summary>What <c>updateSkirmishBattleHonors</c> and <c>updateChallengeMedals</c> read of a finished skirmish.</summary>
public sealed record SkirmishHonorResult(bool Won, string Side, string MapPath, long GameMinutes, int VehiclesBuilt, int AircraftBuilt, ImmutableHashSet<string> Built,
    int EasyOpponents, int MediumOpponents, int HardOpponents, bool AlliedComputer);

/// <summary>
/// The player's record of <c>SkirmishBattleHonors</c>: a <c>UserPreferences</c> file of <c>Key = Value</c> lines, <c>SkirmishStats.ini</c>, beside the savegames in the settings
/// directory - no account and nothing online. <b>The original has no unlocks:</b> <c>ChallengeMenu.cpp</c> opens every general whose <c>StartsEnabled</c> is set, and all nine
/// playable ones are; what a person has beaten is kept here (<c>ChallengeCampaign&lt;n&gt;_&lt;difficulty&gt;</c>, set by <c>finishSinglePlayerInit</c>) and shown as a medal in
/// the list of the skirmish setup (<c>populateSkirmishBattleHonors</c>). Keys this version does not know are kept, as <c>UserPreferences</c> keeps them.
/// </summary>
public sealed class BattleHonors
{
    /// <summary>The file's name: <c>SkirmishBattleHonors::loadFromIniFile</c>.</summary>
    public const string FileName = "SkirmishStats.ini";

    /// <summary><c>MAX_GLOBAL_GENERAL_TYPES</c>: the challenge campaigns the score screen looks for (<c>CHALLENGE_0</c> ...).</summary>
    public const int ChallengeCampaigns = 9;

    private readonly SortedDictionary<string, string> _values = new(StringComparer.Ordinal);

    public int Wins { get => Int("Wins"); set => Set("Wins", value); }
    public int Losses { get => Int("Losses"); set => Set("Losses", value); }
    public int WinStreak { get => Int("WinStreak"); set => Set("WinStreak", value); }
    public int BestWinStreak { get => Int("BestWinStreak"); set => Set("BestWinStreak", value); }
    public int LoyalGames { get => Int("LoyalGames"); set => Set("LoyalGames", value); }
    public int ChallengeMedals { get => Int("Challenge"); set => Set("Challenge", value); }
    public BattleHonor Honors => (BattleHonor)Int("Honors");
    public string LastHouse => _values.GetValueOrDefault("LastHouse", string.Empty);

    public void SetHonor(BattleHonor which) => Set("Honors", (int)Honors | (int)which);

    public bool HasHonor(BattleHonor which) => (Honors & which) != 0;

    public int EnduranceMedal(string map, int difficulty) => Int(EnduranceKey(map, difficulty));

    private static string EnduranceKey(string map, int difficulty) => string.Create(CultureInfo.InvariantCulture, $"{map}_{difficulty}");

    public bool IsChallengeComplete(int challenge, ScriptDifficulty difficulty) => Bool(string.Create(CultureInfo.InvariantCulture, $"ChallengeCampaign{challenge}_{(int)difficulty}"));

    public bool IsCampaignComplete(string prefix, ScriptDifficulty difficulty) => Bool(string.Create(CultureInfo.InvariantCulture, $"{prefix}Campaign_{(int)difficulty}"));

    /// <summary>
    /// <c>finishSinglePlayerInit</c> when the last mission of a campaign is won: the campaign and its difficulty, compared without regard to case, and <c>CHALLENGE_&lt;n&gt;</c> for each
    /// <c>n</c> below <see cref="ChallengeCampaigns"/>; a training or demo campaign leaves no mark.
    /// </summary>
    public void CampaignCompleted(string campaign, ScriptDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        var level = (int)difficulty;
        if (campaign.Equals("USA", StringComparison.OrdinalIgnoreCase)) { SetBool(string.Create(CultureInfo.InvariantCulture, $"USACampaign_{level}")); SetHonor(BattleHonor.CampaignUsa); }
        if (campaign.Equals("China", StringComparison.OrdinalIgnoreCase)) { SetBool(string.Create(CultureInfo.InvariantCulture, $"CHINACampaign_{level}")); SetHonor(BattleHonor.CampaignChina); }
        if (campaign.Equals("GLA", StringComparison.OrdinalIgnoreCase)) { SetBool(string.Create(CultureInfo.InvariantCulture, $"GLACampaign_{level}")); SetHonor(BattleHonor.CampaignGla); }
        for (var index = 0; index < ChallengeCampaigns; index++)
            if (campaign.Equals(string.Create(CultureInfo.InvariantCulture, $"CHALLENGE_{index}"), StringComparison.OrdinalIgnoreCase))
            {
                SetBool(string.Create(CultureInfo.InvariantCulture, $"ChallengeCampaign{index}_{level}"));
                SetHonor(BattleHonor.ChallengeMode);
            }
    }

    /// <summary>The superweapons <c>updateSkirmishBattleHonors</c> looks for, by the name of their template.</summary>
    private static readonly ImmutableArray<string> ScudStorms = ["GLAScudStorm", "Boss_GLAScudStorm", "Chem_GLAScudStorm", "Demo_GLAScudStorm", "Slth_GLAScudStorm"];
    private static readonly ImmutableArray<string> ParticleCannons = ["AmericaParticleCannonUplink", "AirF_AmericaParticleCannonUplink", "Lazr_AmericaParticleCannonUplink", "SupW_AmericaParticleCannonUplink", "Boss_ParticleCannonUplink"];
    private static readonly ImmutableArray<string> Nukes = ["ChinaNuclearMissileLauncher", "Boss_NuclearMissileLauncher", "Infa_ChinaNuclearMissileLauncher", "Nuke_ChinaNuclearMissileLauncher", "Tank_ChinaNuclearMissileLauncher"];

    /// <summary>
    /// The block of <c>populatePlayerInfo</c> for the local player of a skirmish, in its order: a win counts, its streak and best streak grow, <c>updateSkirmishBattleHonors</c> and
    /// <c>updateChallengeMedals</c> run; a loss counts and ends the streak; and the side either goes on being played or starts a new count of loyal games.
    /// </summary>
    public void SkirmishFinished(SkirmishHonorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Won)
        {
            Wins++;
            WinStreak++;
            BestWinStreak = Math.Max(BestWinStreak, WinStreak);
            UpdateSkirmishHonors(result);
            UpdateChallengeMedals(result);
        }
        else
        {
            Losses++;
            WinStreak = 0;
        }
        var last = LastHouse;
        _values["LastHouse"] = result.Side;
        LoyalGames = last != result.Side ? 0 : LoyalGames + 1;
    }

    private void UpdateSkirmishHonors(SkirmishHonorResult result)
    {
        if (WinStreak >= 5) SetHonor(BattleHonor.Streak);
        if (ScudStorms.Any(result.Built.Contains)) SetBool("SCUD");
        if (ParticleCannons.Any(result.Built.Contains)) SetBool("PPC");
        if (Nukes.Any(result.Built.Contains)) SetBool("Nuke");
        if (Bool("SCUD") && Bool("PPC") && Bool("Nuke")) SetHonor(BattleHonor.Apocalypse);
        if (result.VehiclesBuilt >= 50) SetHonor(BattleHonor.BattleTank);
        if (result.AircraftBuilt >= 20) SetHonor(BattleHonor.AirWing);
        if (result.GameMinutes < 5) SetHonor(BattleHonor.Blitz5);
        if (result.GameMinutes < 10) SetHonor(BattleHonor.Blitz10);
        if (LoyalGames >= 20 && result.Side == "America") SetHonor(BattleHonor.LoyaltyUsa);
        if (LoyalGames >= 20 && result.Side == "China") SetHonor(BattleHonor.LoyaltyChina);
        if (LoyalGames >= 20 && result.Side == "GLA") SetHonor(BattleHonor.LoyaltyGla);

        var (easy, medium, hard) = (result.EasyOpponents, result.MediumOpponents, result.HardOpponents);
        if (easy == 0 && medium == 0 && hard == 0) return;
        var oldEasy = EnduranceMedal(result.MapPath, EnduranceEasy);
        var oldMedium = EnduranceMedal(result.MapPath, EnduranceMedium);
        var oldHard = EnduranceMedal(result.MapPath, EnduranceHard);
        if (easy > 0) Set(EnduranceKey(result.MapPath, EnduranceEasy), Math.Max(oldEasy, easy + medium + hard));
        if (medium > 0) Set(EnduranceKey(result.MapPath, EnduranceMedium), Math.Max(oldMedium, medium + hard));
        if (hard > 0) Set(EnduranceKey(result.MapPath, EnduranceHard), Math.Max(oldHard, hard));
    }

    // SlotState of the original: SLOT_EASY_AI, SLOT_MED_AI, SLOT_BRUTAL_AI.
    private const int EnduranceEasy = 2;
    private const int EnduranceMedium = 3;
    private const int EnduranceHard = 4;

    /// <summary><c>updateChallengeMedals</c>: beating one to seven hard computer players on a side of its own, with no computer player beside you.</summary>
    private void UpdateChallengeMedals(SkirmishHonorResult result)
    {
        if (result.AlliedComputer || result.EasyOpponents + result.MediumOpponents + result.HardOpponents == 0) return;
        if (result.HardOpponents is >= 1 and <= 7) ChallengeMedals |= 1 << (result.HardOpponents - 1);
    }

    /// <summary><c>populateSkirmishBattleHonors</c>: the two rows of six pictures of <c>ListboxInfo</c>, in its order, with the commented-out loyalty and challenge pictures left out.</summary>
    public ImmutableArray<HonorEntry> List(IReadOnlyCollection<HonorMap> maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var list = ImmutableArray.CreateBuilder<HonorEntry>(12);
        list.Add(Campaign("CHINA", "ChinaCampaign", BattleHonor.CampaignChina, "China"));
        list.Add(Campaign("GLA", "GLACampaign", BattleHonor.CampaignGla, "GLA"));
        list.Add(Campaign("USA", "USACampaign", BattleHonor.CampaignUsa, "USA"));
        list.Add(ChallengeEntry());
        list.Add(Plain("HonorAirWing", HasHonor(BattleHonor.AirWing), "AirWing"));
        list.Add(Plain("HonorBattleTank", HasHonor(BattleHonor.BattleTank), "BattleTank"));
        list.Add(EnduranceEntry(maps));
        list.Add(Plain("Apocalypse", HasHonor(BattleHonor.Apocalypse), "Apocalypse"));
        list.Add(HasHonor(BattleHonor.Blitz5) ? new HonorEntry("HonorBlitz5", true, "TOOLTIP:BattleHonorBlitz5")
            : HasHonor(BattleHonor.Blitz10) ? new HonorEntry("HonorBlitz10", true, "TOOLTIP:BattleHonorBlitz10") : new HonorEntry("HonorBlitz10", false, "TOOLTIP:BattleHonorBlitzDisabled"));
        var streak = BestWinStreak;
        list.Add(streak switch
        {
            >= 1000 => new HonorEntry("HonorStreak_1000", true, "TOOLTIP:BattleHonorStreak1000"),
            >= 500 => new HonorEntry("HonorStreak_500", true, "TOOLTIP:BattleHonorStreak500"),
            >= 100 => new HonorEntry("HonorStreak_100", true, "TOOLTIP:BattleHonorStreak100"),
            >= 25 => new HonorEntry("HonorStreak_G", true, "TOOLTIP:BattleHonorStreak25"),
            >= 10 => new HonorEntry("HonorStreak_S", true, "TOOLTIP:BattleHonorStreak10"),
            >= 3 => new HonorEntry("HonorStreak_B", true, "TOOLTIP:BattleHonorStreak3"),
            _ => new HonorEntry("HonorStreak_B", false, "TOOLTIP:BattleHonorStreakDisabled"),
        });
        list.Add(Wins switch
        {
            >= 10000 => new HonorEntry("Domination_10000", true, "TOOLTIP:BattleHonorDomination10000"),
            >= 1000 => new HonorEntry("Domination_1000", true, "TOOLTIP:BattleHonorDomination1000"),
            >= 500 => new HonorEntry("Domination_500", true, "TOOLTIP:BattleHonorDomination500"),
            >= 100 => new HonorEntry("Domination_100", true, "TOOLTIP:BattleHonorDomination100"),
            _ => new HonorEntry("Domination_100", false, "TOOLTIP:BattleHonorDominationDisabled"),
        });
        var perfect = maps.All(map => EnduranceMedal(map.Name, EnduranceHard) >= map.Players - 1);
        list.Add(Plain("Ultimate", perfect, "Ultimate"));
        return list.MoveToImmutable();
    }

    private static HonorEntry Plain(string image, bool earned, string name) => new(image, earned, $"TOOLTIP:BattleHonor{name}{(earned ? string.Empty : "Disabled")}");

    private HonorEntry Campaign(string key, string image, BattleHonor honor, string name)
    {
        if (IsCampaignComplete(key, ScriptDifficulty.Hard)) return new HonorEntry(image + "_G", true, $"TOOLTIP:BattleHonorCampaign{name}");
        if (IsCampaignComplete(key, ScriptDifficulty.Normal)) return new HonorEntry(image + "_S", true, $"TOOLTIP:BattleHonorCampaign{name}");
        if (IsCampaignComplete(key, ScriptDifficulty.Easy)) return new HonorEntry(image + "_B", true, $"TOOLTIP:BattleHonorCampaign{name}");
        var earned = HasHonor(honor);
        return new HonorEntry(image + "_B", earned, $"TOOLTIP:BattleHonorCampaign{name}{(earned ? string.Empty : "Disabled")}");
    }

    private HonorEntry ChallengeEntry()
    {
        bool AnyOn(ScriptDifficulty difficulty) => Enumerable.Range(0, ChallengeCampaigns).Any(index => IsChallengeComplete(index, difficulty));
        if (AnyOn(ScriptDifficulty.Hard)) return new HonorEntry("Challenge_Gold", true, "TOOLTIP:BattleHonorCampaignChallenge");
        if (AnyOn(ScriptDifficulty.Normal)) return new HonorEntry("Challenge_Silver", true, "TOOLTIP:BattleHonorCampaignChallenge");
        if (AnyOn(ScriptDifficulty.Easy)) return new HonorEntry("Challenge_Bronz", true, "TOOLTIP:BattleHonorCampaignChallenge");
        return new HonorEntry("Challenge_Bronz", false, "TOOLTIP:BattleHonorCampaignChallengeDisabled");
    }

    private HonorEntry EnduranceEntry(IReadOnlyCollection<HonorMap> maps)
    {
        bool missingEasy = false, missingMedium = false, missingHard = false;
        foreach (var map in maps)
        {
            var easy = EnduranceMedal(map.Name, EnduranceEasy) != 0;
            var medium = EnduranceMedal(map.Name, EnduranceMedium) != 0;
            var hard = EnduranceMedal(map.Name, EnduranceHard) != 0;
            if (!easy && !medium && !hard) missingEasy = true;
            if (!medium && !hard) missingMedium = true;
            if (!hard) missingHard = true;
        }
        if (!missingHard) return new HonorEntry("Endurance_G", true, "TOOLTIP:BattleHonorEndurance");
        if (!missingMedium) return new HonorEntry("Endurance_S", true, "TOOLTIP:BattleHonorEndurance");
        if (!missingEasy) return new HonorEntry("Endurance_B", true, "TOOLTIP:BattleHonorEndurance");
        return new HonorEntry("Endurance_B", false, "TOOLTIP:BattleHonorEnduranceDisabled");
    }

    private int Int(string key) => _values.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private void Set(string key, int value) => _values[key] = value.ToString(CultureInfo.InvariantCulture);

    private bool Bool(string key) => _values.TryGetValue(key, out var text) && text.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private void SetBool(string key) => _values[key] = "yes";

    /// <summary>Reads the lines of the file; what has no <c>=</c> is no line of it. A file that does not exist is an empty record.</summary>
    public static BattleHonors Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var honors = new BattleHonors();
        foreach (var raw in text.Split('\n'))
        {
            var separator = raw.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0) continue;
            honors._values[raw[..separator].Trim()] = raw[(separator + 1)..].Trim();
        }
        return honors;
    }

    /// <summary>The lines in the order of their keys: the same record, the same bytes.</summary>
    public string Serialize()
    {
        var text = new StringBuilder();
        foreach (var (key, value) in _values) text.Append(key).Append(" = ").Append(value).Append('\n');
        return text.ToString();
    }

    public static BattleHonors Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : new BattleHonors();
    }

    /// <summary>Written beside a temporary file and moved over the old one, as the savegames are.</summary>
    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, Serialize(), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
