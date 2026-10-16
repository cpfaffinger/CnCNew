using CnCNew.Game.Data;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;

namespace CnCNew.Game.Replays;

/// <summary>Whether a replay file can be played by this game, and if not, what stands in the way - in the order the checks are made.</summary>
public enum ReplayCompatibilityKind
{
    Playable,
    /// <summary>The file cannot be read: a changed byte, a truncated end, a header that does not parse.</summary>
    Damaged,
    /// <summary>The file or the simulation's replay inside it has another format version.</summary>
    OtherFormat,
    /// <summary>The match was played with another simulation version (command table, world state, match description encoding).</summary>
    OtherSimulation,
    /// <summary>The match was played by another build of the game (the build identity the match description states).</summary>
    OtherBuild,
    /// <summary>The match ran at another tick rate than this game.</summary>
    OtherRate,
    /// <summary>The match was played with another rule profile.</summary>
    OtherRules,
    /// <summary>The match was played with other game data (another definition fingerprint).</summary>
    OtherDefinitions,
    /// <summary>This game does not have the map of the match.</summary>
    MapMissing,
    /// <summary>This game has a map of that name, but not the one the match was played on (another terrain fingerprint).</summary>
    OtherMap,
}

/// <summary>What this game is, for judging a replay: the definitions, the rate, the rule profile, the build and which maps it has.</summary>
public sealed record ReplayMachine(string DefinitionFingerprint, int TickRate, string Rules, string SimulationId, Func<string, bool> HasMap)
{
    /// <summary>This game as <paramref name="data"/> and the running build make it.</summary>
    public static ReplayMachine Of(GameData data) => new(data.Fingerprint, data.Rate.TicksPerSecond, data.Rules.Name, CnCNew.Net.BuildIdentity.Current.Id, data.Content.Exists);
}

/// <param name="Label">The short word of the list's state column.</param>
/// <param name="Reason">What stands in the way, in a sentence fit to show; empty when the file plays.</param>
public sealed record ReplayCompatibility(ReplayCompatibilityKind Kind, string Label, string Reason)
{
    public bool IsPlayable => Kind == ReplayCompatibilityKind.Playable;

    public static ReplayCompatibility Playable { get; } = new(ReplayCompatibilityKind.Playable, "Playable", string.Empty);

    /// <summary>The state of a file that could not be read at all.</summary>
    public static ReplayCompatibility Unreadable(ReplayRefusedException refusal)
        => new(refusal.Kind, refusal.Kind == ReplayCompatibilityKind.OtherFormat ? "Other format" : "Damaged", refusal.Message);

    /// <summary>
    /// Judges a replay by its header against this game: format, simulation version, build, rate, rule profile, definitions and map, in this order - the first that differs is
    /// the reason. A replay is bound to its simulation version and its profile fingerprint; there is no migration (decision D-31 is open for after the first release).
    /// </summary>
    public static ReplayCompatibility Judge(MatchRecordingInfo info, ReplayMachine machine)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(machine);
        if (info.ReplayFormatVersion != ReplayFormat.CurrentVersion)
            return new(ReplayCompatibilityKind.OtherFormat, "Other format", $"The match was recorded in replay format {info.ReplayFormatVersion}; this game plays format {ReplayFormat.CurrentVersion}.");
        if (info.CommandTableVersion != CommandTypes.Version || info.WorldStateVersion != WorldSnapshot.Version || info.DescriptionEncoding != MatchDescription.EncodingVersion)
            return new(ReplayCompatibilityKind.OtherSimulation, "Other version",
                $"The match was played with simulation version {info.CommandTableVersion}/{info.WorldStateVersion}/{info.DescriptionEncoding}; this game is {CommandTypes.Version}/{WorldSnapshot.Version}/{MatchDescription.EncodingVersion}.");
        if (info.SimulationId.Length > 0 && !string.Equals(info.SimulationId, machine.SimulationId, StringComparison.Ordinal))
            return new(ReplayCompatibilityKind.OtherBuild, "Other build", $"The match was played by build {Short(info.SimulationId)}; this game is build {Short(machine.SimulationId)}. Every build plays only its own replays.");
        if (info.TickRate != machine.TickRate)
            return new(ReplayCompatibilityKind.OtherRate, $"{info.TickRate} Hz", $"The match ran at {info.TickRate} Hz; this game runs at {machine.TickRate} Hz. Start the game with --tick-rate {info.TickRate} to play it.");
        if (!string.Equals(info.Rules, machine.Rules, StringComparison.Ordinal))
            return new(ReplayCompatibilityKind.OtherRules, "Other rules", $"The match was played with the rule profile {RulesText(info.Rules)}; this game runs {RulesText(machine.Rules)}. Start the game with --rules {info.Rules} to play it.");
        if (!string.Equals(info.DefinitionFingerprint, machine.DefinitionFingerprint, StringComparison.OrdinalIgnoreCase))
            return new(ReplayCompatibilityKind.OtherDefinitions, "Other data", "The match was played with other game data (the definition fingerprint differs).");
        if (!machine.HasMap(info.MapPath))
            return new(ReplayCompatibilityKind.MapMissing, "Map missing", $"The match was played on '{info.MapPath}', which this game does not have.");
        return Playable;
    }

    /// <summary>A rule profile for a sentence: its name, or - for a list of fixes too long to read - how many fixes it adds to the original.</summary>
    private static string RulesText(string name)
    {
        if (name.Length <= 40) return $"'{name}'";
        var fixes = name.Split('+', StringSplitOptions.RemoveEmptyEntries).Length;
        return $"'{name[..24]}...' (the original with {fixes} fixes)";
    }

    /// <summary>A build identity shortened for a sentence: the first twelve digits of the commit and the "modified" mark.</summary>
    private static string Short(string id)
    {
        var modified = id.EndsWith("+modified", StringComparison.Ordinal);
        var commit = modified ? id[..^"+modified".Length] : id;
        return (commit.Length > 12 ? commit[..12] : commit) + (modified ? " (modified)" : string.Empty);
    }
}
