using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace CnCNew.Simulation.Rules;

/// <summary>
/// A correction of the original's behaviour that the community source (GeneralsGameCode) made and this port adopted as an option. Every value names one fix of
/// <c>docs/GGC-FIXES.md</c> section 3; its number never changes and is never reused. The original profile has none of them on.
/// </summary>
public enum GameFix
{
    /// <summary>Poison damage credits the poisoner: a unit that dies of poison gives its experience to whoever poisoned it (<c>PoisonedBehavior::startPoisonedEffects</c>).</summary>
    PoisonKillCredit = 1,

    /// <summary>Fire damage credits the one who set the fire: a unit that burns to death gives its experience to the source of the flame (<c>FlammableUpdate::onDamage</c>).</summary>
    FlameKillCredit = 2,

    /// <summary>A building that is under construction or being sold cannot be sabotaged (<c>Sabotage*CrateCollide::isValidToExecute</c>).</summary>
    NoSabotageOfUnfinishedOrSold = 3,
}

/// <summary>One adopted fix: where the community source made it, and what it changes.</summary>
public sealed record GameFixInfo(GameFix Fix, string Name, string Source, string Summary);

/// <summary>
/// The rules a world is simulated by: the original's (no fix on - the profile every reference value is measured with) or the original with a set of adopted fixes
/// (<see cref="GameFix"/>). The profile is part of a catalog's identity, like the tick rate: <see cref="Combine"/> folds it into the definition fingerprint, so a world,
/// a replay, a save and a match description made with one profile are refused by a machine with another - two machines with different rules are certain to diverge.
/// The original profile leaves the fingerprint unchanged, so that nothing recorded before profiles existed changes its identity.
/// </summary>
public sealed class RuleProfile : IEquatable<RuleProfile>
{
    private RuleProfile(ImmutableSortedSet<GameFix> fixes) => Fixes = fixes;

    /// <summary>The adopted fixes, with their source in the community code (<c>references/generals-game-code</c>, the file of Zero Hour).</summary>
    public static ImmutableArray<GameFixInfo> Catalogue { get; } =
    [
        new(GameFix.PoisonKillCredit, "poison-kill-credit", "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Behavior/PoisonedBehavior.cpp, PoisonedBehavior::startPoisonedEffects",
            "the periodic poison damage is dealt in the name of the poisoner instead of nobody, so a kill by poison counts for the poisoner"),
        new(GameFix.FlameKillCredit, "flame-kill-credit", "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/FlammableUpdate.cpp, FlammableUpdate::onDamage",
            "the burning damage is dealt in the name of the one whose flame set the object alight instead of the object itself, so a kill by fire counts for the source"),
        new(GameFix.NoSabotageOfUnfinishedOrSold, "no-sabotage-of-unfinished-or-sold",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Collide/CrateCollide/Sabotage*CrateCollide.cpp, Sabotage*CrateCollide::isValidToExecute",
            "a saboteur cannot enter a building that is still under construction or is being sold"),
    ];

    // Declared after the catalogue: static initialisers run in textual order.
    /// <summary>The original game's rules: no fix.</summary>
    public static RuleProfile Original { get; } = new(ImmutableSortedSet<GameFix>.Empty);

    /// <summary>Every adopted fix.</summary>
    public static RuleProfile AllFixes { get; } = new(ImmutableSortedSet.CreateRange(Catalogue.Select(info => info.Fix)));

    public ImmutableSortedSet<GameFix> Fixes { get; }

    public bool IsOriginal => Fixes.IsEmpty;

    public bool Has(GameFix fix) => Fixes.Contains(fix);

    /// <summary><c>original</c>, or the names of the fixes in their numeric order joined by <c>+</c>.</summary>
    public string Name => IsOriginal ? "original" : string.Join('+', Fixes.Select(fix => Info(fix).Name));

    /// <summary>
    /// The definition fingerprint a world with these rules carries. The original profile returns it unchanged; any other folds its name in - the same scheme as the
    /// tick rate (<see cref="Time.SimulationRate.Combine"/>), applied after it.
    /// </summary>
    public string Combine(string definitionFingerprint)
    {
        ArgumentNullException.ThrowIfNull(definitionFingerprint);
        return IsOriginal ? definitionFingerprint : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definitionFingerprint + "+rules:" + Name)));
    }

    public static GameFixInfo Info(GameFix fix)
    {
        foreach (var info in Catalogue)
            if (info.Fix == fix) return info;
        throw new ArgumentOutOfRangeException(nameof(fix), fix, "Not an adopted fix.");
    }

    /// <summary>
    /// <c>original</c>, <c>fixes</c> (every adopted fix) or a list of fix names separated by <c>,</c> or <c>+</c>. An unknown name is an error, never ignored.
    /// </summary>
    public static RuleProfile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Trim();
        if (value.Equals("original", StringComparison.OrdinalIgnoreCase)) return Original;
        if (value.Equals("fixes", StringComparison.OrdinalIgnoreCase)) return AllFixes;
        var fixes = ImmutableSortedSet.CreateBuilder<GameFix>();
        foreach (var part in value.Split([',', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var info = Catalogue.FirstOrDefault(entry => entry.Name.Equals(part, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown rule profile or fix '{part}'. Known: original, fixes, {string.Join(", ", Catalogue.Select(entry => entry.Name))}.");
            fixes.Add(info.Fix);
        }
        if (fixes.Count == 0) throw new ArgumentException("An empty rule profile; say 'original'.");
        return new RuleProfile(fixes.ToImmutable());
    }

    public bool Equals(RuleProfile? other) => other is not null && Fixes.SequenceEqual(other.Fixes);

    public override bool Equals(object? obj) => Equals(obj as RuleProfile);

    public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Name;
}
