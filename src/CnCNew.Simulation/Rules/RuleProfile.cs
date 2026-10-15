using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Simulation.Numerics;

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

    /// <summary>A bunker buster busts only when its missile kills itself at the target, not when it is shot down (<c>BunkerBusterBehavior::onDie</c>).</summary>
    BunkerBusterOnlyWhenKillingSelf = 4,

    /// <summary>A building site that dies fires no death weapon of its <c>InstantDeathBehavior</c> (<c>InstantDeathBehavior::onDie</c>).</summary>
    NoDeathWeaponFromScaffold = 5,

    /// <summary>Nobody leaves a transport that is itself inside a container (<c>TransportContain::isSpecificRiderFreeToExit</c>).</summary>
    NoExitFromContainedTransport = 6,

    /// <summary>An object that is already dead gets no health, weapon bonus or upgrade from a new veterancy level (<c>Object::onVeterancyLevelChanged</c>).</summary>
    NoVeterancyEffectsWhenDead = 7,

    /// <summary>The upgraded supply boost is paid in proportion to the boxes delivered (<c>SupplyCenterDockUpdate</c>, <c>getUpgradedSupplyBoostValue</c>).</summary>
    ProportionalSupplyBoost = 8,

    /// <summary>The neutron missile's blast reaches every object whose bounding sphere is in its outer radius, not only those whose centre is (<c>NeutronMissileSlowDeathBehavior::doBlast</c>).</summary>
    NeutronBlastBoundingSphere = 9,

    /// <summary>A booby trap that is destroyed before it goes off takes its mark from its victim (<c>StickyBombUpdate::onDelete</c>).</summary>
    BoobyTrapClearedOnRemoval = 10,

    /// <summary>A cash bounty that is a whole number is paid as that number, not one more (<c>Player::doBountyForKill</c>).</summary>
    BountyRounding = 11,

    /// <summary>A player's tunnel network heals its riders once per frame, not once per tunnel (<c>Player::update</c>).</summary>
    TunnelHealOncePerPlayer = 12,

    /// <summary>A change of battle plan hands every object all the plans the player runs, not only the newest one (<c>Player::applyBattlePlanBonusesForPlayerObjects</c>).</summary>
    BattlePlanAggregate = 13,

    /// <summary>A captured strategy center takes its plan's bonuses from the old owner to the new one (<c>BattlePlanUpdate::onCapture</c>).</summary>
    BattlePlanFollowsCapture = 14,

    /// <summary>A propaganda tower falls silent inside any enclosing container of its chain, not only when its container is itself contained (<c>PropagandaTowerBehavior::update</c>).</summary>
    PropagandaTowerEnclosingContainer = 15,

    /// <summary>A helicopter that has healed on its pad takes off and flies to its airfield's rally point (<c>JetAIUpdate::update</c>).</summary>
    HealedHelicopterToRally = 16,
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
        new(GameFix.BunkerBusterOnlyWhenKillingSelf, "bunker-buster-only-when-killing-self",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Behavior/BunkerBusterBehavior.cpp, BunkerBusterBehavior::onDie",
            "a bunker buster that is shot down on its way (by a point-defence laser, for one) no longer busts the bunker it was aimed at"),
        new(GameFix.NoDeathWeaponFromScaffold, "no-death-weapon-from-scaffold",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Behavior/InstantDeathBehavior.cpp, InstantDeathBehavior::onDie",
            "a building site that is cancelled or destroyed fires no death weapon, as FireWeaponWhenDeadBehavior already does not"),
        new(GameFix.NoExitFromContainedTransport, "no-exit-from-contained-transport",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/TransportContain.cpp, TransportContain::isSpecificRiderFreeToExit",
            "riders of a transport that is itself inside another container cannot get out"),
        new(GameFix.NoVeterancyEffectsWhenDead, "no-veterancy-effects-when-dead",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Object.cpp, Object::onVeterancyLevelChanged",
            "an object that dies and gains a level in the same moment keeps the level but gets no health, weapon bonus or veterancy upgrade from it"),
        new(GameFix.ProportionalSupplyBoost, "proportional-supply-boost",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/DockUpdate/SupplyCenterDockUpdate.cpp, SupplyCenterDockUpdate::action (getUpgradedSupplyBoostValue)",
            "the upgraded supply boost (Supply Lines, Worker Shoes) is paid in proportion to the boxes delivered instead of in full for every delivery"),
        new(GameFix.NeutronBlastBoundingSphere, "neutron-blast-bounding-sphere",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/NeutronMissileSlowDeathUpdate.cpp, NeutronMissileSlowDeathBehavior::doBlast",
            "the nuclear blast hits every object whose bounding sphere reaches into its outer radius, not only those whose centre lies in it (2-D)"),
        new(GameFix.BoobyTrapClearedOnRemoval, "sticky-bomb-clears-trap-on-removal",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/StickyBombUpdate.cpp, StickyBombUpdate::onDelete",
            "a booby trap that is destroyed before it goes off no longer leaves its victim marked as booby-trapped"),
        new(GameFix.BountyRounding, "bounty-rounding",
            "GeneralsMD/Code/GameEngine/Source/Common/RTS/Player.cpp, Player::doBountyForKill",
            "a cash bounty that is a whole number of credits pays that number; the rounding up no longer adds one for the last bit of the percentage"),
        new(GameFix.TunnelHealOncePerPlayer, "tunnel-heal-once-per-player",
            "GeneralsMD/Code/GameEngine/Source/Common/RTS/Player.cpp, Player::update (TunnelTracker::healObjects)",
            "the riders of a tunnel network are healed once per frame by the player, with the shortest full-heal time of its tunnels, instead of once per frame by every tunnel"),
        new(GameFix.BattlePlanAggregate, "battle-plan-aggregate",
            "GeneralsMD/Code/GameEngine/Source/Common/RTS/Player.cpp, Player::applyBattlePlanBonusesForPlayerObjects",
            "a plan that starts or ends hands the player's objects the switches of every plan the player still runs, so a second strategy center no longer wipes the first one's bonus"),
        new(GameFix.BattlePlanFollowsCapture, "battle-plan-follows-capture",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/BattlePlanUpdate.cpp, BattlePlanUpdate::onCapture",
            "a captured strategy center takes its running plan from the old owner's army to the new owner's"),
        new(GameFix.PropagandaTowerEnclosingContainer, "propaganda-tower-enclosing-container",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Behavior/PropagandaTowerBehavior.cpp, PropagandaTowerBehavior::update (Object::getEnclosingContainedBy)",
            "a propaganda tower (an Emperor's, an Overlord's) falls silent as soon as any container of its chain encloses it, e.g. an Emperor carried by a Helix"),
        new(GameFix.HealedHelicopterToRally, "healed-helicopter-to-rally",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/AIUpdate/JetAIUpdate.cpp, JetAIUpdate::update",
            "a helicopter that took off again after healing on its pad flies on to its airfield's rally point instead of hovering over the pad"),
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

/// <summary>The arithmetic of adopted fixes that more than one place, or a test, needs.</summary>
public static class GameFixRules
{
    /// <summary><c>WWMATH_EPSILON</c> (WWMath <c>wwmath.h</c>): 0.0001.</summary>
    public static Fixed BountyEpsilon { get; } = Fixed.FromDecimal(0.0001m);

    /// <summary>
    /// <c>Player::doBountyForKill</c>: the bounty is the cost times the percentage, rounded up; with the fix <c>bounty-rounding</c> an epsilon is taken off first, so a
    /// product that is a whole number in exact arithmetic but lies a hair above it in the fixed point (5 % is not exact in binary) pays that whole number.
    /// </summary>
    public static long CashBounty(Fixed product, bool fixRounding) => (fixRounding ? product - BountyEpsilon : product).Ceiling();

    /// <summary><c>getUpgradedSupplyBoostValue</c> with the fix: <c>boost * delivered / maximum</c> in whole numbers, 0 for a carrier without boxes.</summary>
    public static long ProportionalSupplyBoost(long boost, int deliveredBoxes, int maximumBoxes) => maximumBoxes > 0 ? boost * deliveredBoxes / maximumBoxes : 0;
}
