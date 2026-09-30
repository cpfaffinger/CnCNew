using System.Collections.Immutable;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.State;

namespace CnCNew.Game.Campaign;

/// <summary>
/// Where the player stands in one campaign: the difficulty chosen at the start, the mission to play next, the missions won and how often each mission
/// was started (<c>MISSION_ATTEMPTS</c> scripts read that). Immutable; every transition returns the new state. A lost mission changes nothing but the
/// attempt count, as in the reference (<c>CampaignManager</c> keeps the current mission until <c>SetVictorious(TRUE)</c> advances it).
/// </summary>
public sealed record CampaignProgress(
    string Campaign, ScriptDifficulty Difficulty, string CurrentMission, ImmutableSortedSet<string> Completed, ImmutableSortedDictionary<string, int> Attempts, bool Finished)
{
    public static CampaignProgress Begin(CampaignDefinition campaign, ScriptDifficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        if (campaign.FindMission(campaign.FirstMission) is null) throw new InvalidOperationException($"Campaign {campaign.Name} has no first mission '{campaign.FirstMission}'.");
        return new CampaignProgress(campaign.Name, difficulty, campaign.FirstMission, ImmutableSortedSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase),
            ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase), false);
    }

    /// <summary>How many times the current mission has been started, this start included once <see cref="Started"/> was applied.</summary>
    public int AttemptsOf(string mission) => Attempts.GetValueOrDefault(mission);

    /// <summary>The player starts (or retries) the current mission.</summary>
    public CampaignProgress Started()
    {
        if (Finished) throw new InvalidOperationException("The campaign is finished.");
        return this with { Attempts = Attempts.SetItem(CurrentMission, AttemptsOf(CurrentMission) + 1) };
    }

    /// <summary>The current mission was won: it is completed and the next one becomes current; after the last one the campaign is finished.</summary>
    public CampaignProgress Won(CampaignDefinition campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        if (Finished) throw new InvalidOperationException("The campaign is finished.");
        var mission = campaign.FindMission(CurrentMission) ?? throw new InvalidOperationException($"Mission {CurrentMission} is not part of campaign {campaign.Name}.");
        var next = campaign.NextOf(mission);
        return this with { Completed = Completed.Add(CurrentMission), CurrentMission = next?.Name ?? CurrentMission, Finished = next is null };
    }

    /// <summary>A lost mission keeps the state: the player retries the same mission.</summary>
    public CampaignProgress Lost() => this;

    public void Describe(IStateWriter writer)
    {
        writer.Text("campaign", Campaign);
        writer.Int("difficulty", (int)Difficulty);
        writer.Text("current", CurrentMission);
        writer.Bool("finished", Finished);
        writer.BeginArray("completed");
        foreach (var mission in Completed)
        {
            writer.BeginObject(null);
            writer.Text("mission", mission);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("attempts");
        foreach (var (mission, count) in Attempts)
        {
            writer.BeginObject(null);
            writer.Text("mission", mission);
            writer.Int("count", count);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public static CampaignProgress Load(IStateReader reader)
    {
        var difficulty = checked((int)reader.Int("difficulty"));
        if (!Enum.IsDefined((ScriptDifficulty)difficulty)) throw new InvalidDataException("Unknown difficulty in campaign progress.");
        var completed = ImmutableSortedSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in reader.Array("completed")) completed.Add(item.Text("mission"));
        var attempts = ImmutableSortedDictionary.CreateBuilder<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in reader.Array("attempts")) attempts[item.Text("mission")] = checked((int)item.Int("count"));
        return new CampaignProgress(reader.Text("campaign"), (ScriptDifficulty)difficulty, reader.Text("current"), completed.ToImmutable(), attempts.ToImmutable(), reader.Bool("finished"));
    }
}
