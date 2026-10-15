using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Skirmish;

/// <summary>
/// The tactics profile of a <see cref="SkirmishAi"/> (Stage 3, S3-M1): its build order, the rule its army is drawn by, when it attacks and how it defends. <see cref="Balanced"/> is the AI
/// as it was before the profiles existed; a slot that names no profile plays it.
/// </summary>
public enum AiTactics : byte
{
    /// <summary>The built-in AI as it was: one supply center until the army counts ten, an attack from eight units on, defences by the size of the army.</summary>
    Balanced = 0,

    /// <summary>Early and often: attacks from four units on, a second barracks early, no base defences, the army drawn among the cheap units, no second supply center while the first one has stock.</summary>
    Rush = 1,

    /// <summary>The base first: defences from the start and more of them, the army defends further out and attacks only from twenty units on.</summary>
    Turtle = 2,

    /// <summary>Income first: more gatherers per center, a second supply center at once and a third one from eight units on, an attack from twelve units on.</summary>
    Eco = 3,
}

/// <summary>The strength of a <see cref="SkirmishAi"/>: how much it may know and how fast it decides. Never a rule bonus in the simulation that a human does not get.</summary>
public enum AiStrength : byte
{
    /// <summary>Decides every second (instead of every half second), forgets a unit it has lost sight of after 10 s (instead of 30 s), does not weigh its army against the enemy's force it has seen,
    /// and does not use the map's start positions as attack targets - it has to find the enemy.</summary>
    Easy = 0,

    /// <summary>The built-in AI as it was.</summary>
    Medium = 1,

    /// <summary>Decides every third of a second, remembers a unit it has lost sight of for 60 s and the enemy's force it has seen for 6 min (instead of 3 min).</summary>
    Hard = 2,
}

public sealed partial class SkirmishAi
{
    /// <summary>
    /// The numbers a tactics profile changes. <see cref="Balanced"/> holds exactly the values the AI had as constants before the profiles, so a slot without a profile plays as before.
    /// </summary>
    /// <param name="AttackThreshold">Army size at which the first wave marches; <see cref="ThresholdStep"/> more after every wave that falls apart, up to <see cref="MaxAttackThreshold"/>.</param>
    /// <param name="CarriersPerCenter">Supply carriers per own supply center, at most <see cref="MaxCarriers"/>.</param>
    /// <param name="SecondCenterArmy">Army size from which a second supply center is planned (besides the rule of the running-out stock).</param>
    /// <param name="ThirdCenterArmy">Army size from which a third supply center is planned; <see cref="int.MaxValue"/> for never.</param>
    /// <param name="SecondBarracksBudget">Money above which a second barracks is planned.</param>
    /// <param name="DefenseBase">Base defences wanted before the army counts; then one more per <see cref="DefensesPerArmy"/> army units, at most <see cref="MaxDefenses"/>.</param>
    /// <param name="DefenseRadius">How near to an own structure a visible enemy unit has to come for the army to drop its plans and defend.</param>
    /// <param name="CheapArmy">The army is drawn among the units that cost at most twice the cheapest candidate.</param>
    /// <param name="StagnationFrames">How long an army at rest may stop growing before it marches with what it has (the rule "no standing still"), in frames of the original.</param>
    internal readonly record struct TacticsPlan(int AttackThreshold, int MaxAttackThreshold, int ThresholdStep, int CarriersPerCenter, int MaxCarriers, int SecondCenterArmy,
        int ThirdCenterArmy, int SecondBarracksBudget, int DefenseBase, int DefensesPerArmy, int MaxDefenses, Fixed DefenseRadius, bool CheapArmy, long StagnationFrames)
    {
        public static TacticsPlan Of(AiTactics tactics) => tactics switch
        {
            AiTactics.Balanced => new(SkirmishAi.MinAttackThreshold, SkirmishAi.MaxAttackThreshold, 2, 2, 6, 10, int.MaxValue, 1200, 0, 6, 4, BalancedDefenseRadius, false, 3600),
            AiTactics.Rush => new(4, 12, 1, 2, 4, int.MaxValue, int.MaxValue, 600, 0, 6, 0, BalancedDefenseRadius, true, 1800),
            AiTactics.Turtle => new(20, 40, 2, 2, 6, 10, int.MaxValue, 1200, 2, 3, 8, Fixed.FromInt(650), false, 7200),
            AiTactics.Eco => new(12, 30, 2, 3, 12, 0, 8, 1200, 0, 6, 4, BalancedDefenseRadius, false, 3600),
            _ => throw new ArgumentOutOfRangeException(nameof(tactics), tactics, "Unknown tactics profile."),
        };
    }

    /// <summary>The numbers a strength changes, in logic frames of the original. <see cref="AiStrength.Medium"/> holds exactly the values the AI had as constants before the levels.</summary>
    /// <param name="ThinkFrames">Time between two decisions; a multiple of <see cref="FireIntervalFrames"/>.</param>
    /// <param name="MemoryFrames">How long a mobile enemy out of sight stays remembered.</param>
    /// <param name="SightingFrames">How long a sighting counts toward the enemy's force the army is weighed against.</param>
    /// <param name="KnowsForce">Whether the army is drawn by its worth against the force seen (<see cref="ArmyComposition.ExchangeValue"/>); otherwise uniformly.</param>
    /// <param name="UsesStartPositions">Whether the map's start positions are attack targets before anything has been seen.</param>
    internal readonly record struct StrengthPlan(long ThinkFrames, long MemoryFrames, long SightingFrames, bool KnowsForce, bool UsesStartPositions)
    {
        public static StrengthPlan Of(AiStrength strength) => strength switch
        {
            AiStrength.Easy => new(30, 300, 1800, false, false),
            // The values the AI had as constants before the levels: a think every 15 frames, a mobile enemy remembered for 30 s, a sighting counted for 3 min.
            AiStrength.Medium => new(15, 900, 5400, true, true),
            AiStrength.Hard => new(10, 1800, 10800, true, true),
            _ => throw new ArgumentOutOfRangeException(nameof(strength), strength, "Unknown AI strength."),
        };
    }

    private readonly TacticsPlan _plan;
    private readonly StrengthPlan _strength;

    /// <summary>The profile this AI plays (its options' profile, or <see cref="AiTactics.Balanced"/>).</summary>
    public AiTactics Tactics => _options.Tactics ?? AiTactics.Balanced;

    /// <summary>The profile the options name; null when they name none (then the AI plays <see cref="AiTactics.Balanced"/>).</summary>
    private AiTactics? NamedTactics => _options.Tactics;

    /// <summary>The strength this AI plays at.</summary>
    public AiStrength Strength => _options.Strength;
}
