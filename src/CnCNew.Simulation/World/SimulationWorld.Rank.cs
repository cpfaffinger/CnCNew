using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

/// <summary>
/// The rank of a player (<c>Player::addSkillPoints</c>, <c>setRankLevel</c>, <c>resetRank</c>; <c>docs/SIMULATION-CORE.md</c>): skill points from kills and abilities raise the
/// level, each level grants its sciences and science purchase points. A catalog without ranks (synthetic data) leaves a player at level 0 with nothing to gain.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary><c>GameLogic::m_rankLevelLimit</c>, reset for every game; a script lowers it.</summary>
    private int _rankLevelLimit = DefaultRankLevelLimit;
    private const int DefaultRankLevelLimit = 1000;

    public int RankLevelOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.RankLevel : 0;
    public int SkillPointsOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.SkillPoints : 0;

    /// <summary>The map-wide limit, or -1 while nothing lowered it.</summary>
    public int RankLevelLimit => _rankLevelLimit == DefaultRankLevelLimit ? -1 : _rankLevelLimit;

    /// <summary><c>Player::resetRank</c>: level 1 with what it grants, the intrinsic sciences and nothing else. Without ranks the intrinsic values stand alone.</summary>
    private void ResetRank(PlayerState player)
    {
        player.CompletedSciences = player.IntrinsicSciences;
        player.SciencePurchasePoints = player.IntrinsicSciencePoints;
        if (_templates.Ranks.LevelCount == 0) return;
        player.RankLevel = 1;
        player.SkillPoints = 0;
        var first = _templates.Ranks.At(1)!;
        player.SciencePurchasePoints += first.SciencePurchasePointsGranted;
        foreach (var science in first.SciencesGranted) player.CompletedSciences = player.CompletedSciences.Set(science.Index);
    }

    /// <summary><c>m_levelUp</c>: the points of the next level, or none to reach when there is no next level.</summary>
    private int LevelUpOf(PlayerState player) => _templates.Ranks.At(player.RankLevel + 1)?.SkillPointsNeeded ?? int.MaxValue;

    /// <summary>The points the next level needs, or -1 at the last one (the experience bar of the general's window).</summary>
    public int NextLevelPointsOf(PlayerId player)
        => _players.TryGetValue(player.Value, out var state) && _templates.Ranks.At(state.RankLevel + 1) is { } next ? next.SkillPointsNeeded : -1;

    /// <summary>The way from this level to the next in percent, as the general's window fills its bar; 0 at the last level.</summary>
    public int RankProgressPercentOf(PlayerId player)
    {
        if (!_players.TryGetValue(player.Value, out var state) || _templates.Ranks.At(state.RankLevel) is not { } rank || _templates.Ranks.At(state.RankLevel + 1) is not { } next) return 0;
        return (state.SkillPoints - rank.SkillPointsNeeded) * 100 / (next.SkillPointsNeeded - rank.SkillPointsNeeded);
    }

    /// <summary><c>Player::addSkillPoints</c>: true when a level was gained.</summary>
    public bool AddSkillPoints(PlayerId player, int delta)
    {
        if (!_players.TryGetValue(player.Value, out var state) || state.RankLevel == 0) return false;
        delta = checked((int)(state.SkillPointsModifier * Fixed.FromInt(delta)).Ceiling());
        if (delta == 0) return false;
        var cap = Math.Min(_rankLevelLimit, _templates.Ranks.LevelCount);
        state.SkillPoints = Math.Min(_templates.Ranks.At(cap)!.SkillPointsNeeded, state.SkillPoints + delta);
        var gained = false;
        while (state.SkillPoints >= LevelUpOf(state))
        {
            SetRankLevel(player, state.RankLevel + 1);
            gained = true;
        }
        return gained;
    }

    /// <summary><c>Player::setRankLevel</c>: true when the level really changed. A step down loses everything, as there.</summary>
    public bool SetRankLevel(PlayerId player, int level)
    {
        if (!_players.TryGetValue(player.Value, out var state) || state.RankLevel == 0) return false;
        level = Math.Min(Math.Min(Math.Max(level, 1), _templates.Ranks.LevelCount), _rankLevelLimit);
        if (level == state.RankLevel) return false;
        if (level < state.RankLevel) ResetRank(state);
        for (var next = state.RankLevel + 1; next <= level; next++)
        {
            var rank = _templates.Ranks.At(next)!;
            state.SciencePurchasePoints = Math.Max(0, state.SciencePurchasePoints + rank.SciencePurchasePointsGranted);
            state.SkillPoints = Math.Max(state.SkillPoints, rank.SkillPointsNeeded);
            foreach (var science in rank.SciencesGranted) AddScience(state, science);
        }
        state.RankLevel = level;
        return true;
    }

    /// <summary><c>GameLogic::setRankLevelLimit</c> (<c>PLAYER_SET_RANKLEVELLIMIT</c>).</summary>
    public void SetRankLevelLimit(int limit) => _rankLevelLimit = Math.Max(1, limit);

    /// <summary><c>Player::setSkillPointsModifier</c> (<c>PLAYER_AFFECT_RECEIVING_EXPERIENCE</c>).</summary>
    public void SetSkillPointsModifier(PlayerId player, Fixed modifier)
    {
        if (_players.TryGetValue(player.Value, out var state)) state.SkillPointsModifier = modifier;
    }

    public Fixed SkillPointsModifierOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.SkillPointsModifier : Fixed.One;

    private static void DescribeRank(IStateWriter writer, PlayerState player)
    {
        if (player.RankLevel == 0) return;
        writer.Int("rankLevel", player.RankLevel);
        writer.Int("skillPoints", player.SkillPoints);
        if (player.SkillPointsModifier != Fixed.One) writer.Fixed("skillPointsModifier", player.SkillPointsModifier);
    }

    private static void LoadRank(IStateReader reader, PlayerState player)
    {
        if (!reader.Has("rankLevel")) return;
        player.RankLevel = checked((int)reader.Int("rankLevel"));
        player.SkillPoints = checked((int)reader.Int("skillPoints"));
        player.SkillPointsModifier = reader.Has("skillPointsModifier") ? reader.Fixed("skillPointsModifier") : Fixed.One;
    }
}
