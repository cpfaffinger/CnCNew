namespace CnCNew.Simulation.Skirmish;

/// <summary>
/// The debug options of a match, for testing: rules the original does not have, part of the <see cref="MatchDescription"/> so that every machine of a lockstep match simulates the same
/// game. The default value is the original's behaviour and writes nothing into the world's state, so a match without them keeps its hashes.
/// </summary>
/// <param name="BuildTimePercent">What everything built takes of its normal time, in percent (100 normal): units, structures under construction and researched upgrades - everything
/// <c>calcTimeToBuild</c> and the upgrade's build time give, before the power-supply ratio is applied.</param>
/// <param name="NeutralRespawnSeconds">When above 0, a destroyed neutral building of the map comes back at its place after that many seconds of game time; 0 is off.</param>
/// <param name="StartingGeneralPoints">The science purchase points of the general's window every player of the match starts with, on top of its rank's.</param>
public sealed record MatchDebugOptions(int BuildTimePercent = MatchDebugOptions.NormalBuildTime, int NeutralRespawnSeconds = 0, int StartingGeneralPoints = 0)
{
    public const int NormalBuildTime = 100;
    public const int MinimumBuildTimePercent = 1, MaximumBuildTimePercent = 1000;
    public const int MaximumRespawnSeconds = 86_400, MaximumStartingGeneralPoints = 1000;

    /// <summary>The original's rules: nothing is changed.</summary>
    public static MatchDebugOptions None { get; } = new();

    /// <summary>The build time the setup offers for "quick build": a tenth of the normal time (90 % less).</summary>
    public const int QuickBuildPercent = 10;

    public bool IsDefault => BuildTimePercent == NormalBuildTime && NeutralRespawnSeconds == 0 && StartingGeneralPoints == 0;

    /// <summary>Whether every value is inside its range; <see cref="Problem"/> names the first that is not.</summary>
    public bool IsValid => Problem is null;

    public string? Problem
        => BuildTimePercent is < MinimumBuildTimePercent or > MaximumBuildTimePercent ? $"The build time of {BuildTimePercent} % is outside {MinimumBuildTimePercent} to {MaximumBuildTimePercent} %."
            : NeutralRespawnSeconds is < 0 or > MaximumRespawnSeconds ? $"The respawn time of {NeutralRespawnSeconds} s is outside 0 to {MaximumRespawnSeconds} s."
            : StartingGeneralPoints is < 0 or > MaximumStartingGeneralPoints ? $"{StartingGeneralPoints} starting general points are outside 0 to {MaximumStartingGeneralPoints}."
            : null;
}
