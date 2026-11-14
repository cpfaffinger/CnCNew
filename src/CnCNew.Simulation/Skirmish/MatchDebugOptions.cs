namespace CnCNew.Simulation.Skirmish;

/// <summary>
/// The debug options of a match, for testing: rules the original does not have, part of the <see cref="MatchDescription"/> so that every machine of a lockstep match simulates the same
/// game. The default value is the original's behaviour and writes nothing into the world's state, so a match without them keeps its hashes.
/// </summary>
/// <param name="BuildTimePercent">What everything built takes of its normal time, in percent (100 normal): units, structures under construction and researched upgrades - everything
/// <c>calcTimeToBuild</c> and the upgrade's build time give, before the power-supply ratio is applied.</param>
/// <param name="NeutralRespawnSeconds">When above 0, a destroyed neutral building of the map comes back at its place after that many seconds of game time; 0 is off.</param>
/// <param name="StartingGeneralPoints">The skill points (experience) every player of the match starts with, added as earned points are (<c>Player::addSkillPoints</c>): the general's rank rises to
/// the level those points reach and each level grants its science purchase points and sciences. 0 is the normal start (rank 1).</param>
/// <param name="PlaceBuildingsInFog">When set, a builder may be ordered to put a structure on ground the player has not explored or does not look at: the build site check leaves out its
/// shroud rule (<c>SHROUD_REVEALED</c>). The original refuses such a site.</param>
/// <param name="QuickBuildHumansOnly">When set, <paramref name="BuildTimePercent"/> and <paramref name="RechargePercent"/> apply to the human players only; a computer player builds and recharges in its normal time. Meaningless (and false) while both are normal.</param>
/// <param name="MoneyButtons">When set, the match shows two buttons that give 10,000 credits to the human who presses it, or to every player (<see cref="CnCNew.Simulation.Commands.DebugGiveMoneyCommand"/>).</param>
/// <param name="RechargePercent">What the recharge of every special power of a player takes of its normal time, in percent (100 normal, 50, 25 and 10 are the levels the setup offers). Like quick build it can be for human players only (<paramref name="QuickBuildHumansOnly"/> holds for both).</param>
public sealed record MatchDebugOptions(int BuildTimePercent = MatchDebugOptions.NormalBuildTime, int NeutralRespawnSeconds = 0, int StartingGeneralPoints = 0, bool PlaceBuildingsInFog = false, bool QuickBuildHumansOnly = false, bool MoneyButtons = false, int RechargePercent = MatchDebugOptions.NormalBuildTime)
{
    public const int NormalBuildTime = 100;
    public const int MinimumBuildTimePercent = 1, MaximumBuildTimePercent = 1000;
    public const int MaximumRespawnSeconds = 86_400, MaximumStartingGeneralPoints = 1_000_000;

    /// <summary>The original's rules: nothing is changed.</summary>
    public static MatchDebugOptions None { get; } = new();

    /// <summary>The build time the setup offers for "quick build": a tenth of the normal time (90 % less).</summary>
    public const int QuickBuildPercent = 10;

    /// <summary>The other levels of quick build: half the time (50 % less) and a quarter of it (75 % less).</summary>
    public const int HalfBuildPercent = 50, QuarterBuildPercent = 25;

    public bool IsDefault => BuildTimePercent == NormalBuildTime && NeutralRespawnSeconds == 0 && StartingGeneralPoints == 0 && !PlaceBuildingsInFog && !QuickBuildHumansOnly && !MoneyButtons && RechargePercent == NormalBuildTime;

    /// <summary>Whether every value is inside its range; <see cref="Problem"/> names the first that is not.</summary>
    public bool IsValid => Problem is null;

    public string? Problem
        => BuildTimePercent is < MinimumBuildTimePercent or > MaximumBuildTimePercent ? $"The build time of {BuildTimePercent} % is outside {MinimumBuildTimePercent} to {MaximumBuildTimePercent} %."
            : NeutralRespawnSeconds is < 0 or > MaximumRespawnSeconds ? $"The respawn time of {NeutralRespawnSeconds} s is outside 0 to {MaximumRespawnSeconds} s."
            : StartingGeneralPoints is < 0 or > MaximumStartingGeneralPoints ? $"{StartingGeneralPoints} starting general points are outside 0 to {MaximumStartingGeneralPoints}."
            : RechargePercent is < MinimumBuildTimePercent or > MaximumBuildTimePercent ? $"The recharge time of {RechargePercent} % is outside {MinimumBuildTimePercent} to {MaximumBuildTimePercent} %."
            : QuickBuildHumansOnly && BuildTimePercent == NormalBuildTime && RechargePercent == NormalBuildTime ? "Quick build for human players only needs a quick build or a recharge level."
            : null;
}
