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

    /// <summary>A rider that leaves a container through its door walks on to the rally point of the object's production exit when the container has none of its own (<c>OpenContain::getRallyPoint</c>).</summary>
    DoorExitToRally = 17,

    /// <summary>A combat bike takes its rider's trainability, so an untrainable rider (a worker) cannot gain levels through the bike (<c>RiderChangeContain::onContaining</c>).</summary>
    BikeTrainableFromRider = 18,

    /// <summary>A boarder goes into a container only when the two overlap in height, not merely when the container stands on the ground (<c>AIEnterState::update</c>).</summary>
    EnterNeedsVerticalOverlap = 19,

    /// <summary>An attacker whose current weapon is a contact weapon is never view-blocked, so a suicide unit touching a building sets its charge off (<c>Pathfinder::isAttackViewBlockedByObstacle</c>).</summary>
    ContactWeaponNotViewBlocked = 20,

    /// <summary>A guard answers an attacker only in its IDLE state, not while it attacks (INNER) or walks back to its post (RETURN); the retaliation likewise not in RETURN (<c>AIGuardMachine</c>, <c>AIGuardRetaliateMachine</c>).</summary>
    GuardAnswersOnlyWhenIdle = 21,

    /// <summary>A power plant under construction changes no energy when it is disabled or enabled again - it produces nothing yet (<c>Object::onDisabledEdge</c>).</summary>
    NoPowerEdgeForUnfinishedPlant = 22,

}

/// <summary>
/// How ground units plan their paths (Stage 3, S3-M6; docs/PERF-MOVEMENT.md section 9). <see cref="Original"/> is the original's: every unit searches its own path in the
/// tick it needs one. Not a fix of the community source - an option of this port, carried by the rule profile so that it reaches the definition fingerprint, the match
/// description and the room like the fixes do.
/// </summary>
public enum MovementProfile : byte
{
    Original = 0,

    /// <summary>
    /// <c>movement-budget</c>: the searches of ground units wait in the world's queue, which expands a fixed number of cells per tick and carries a search that does not fit
    /// over to the next tick; units of a group order that head for the same place share one search.
    /// </summary>
    Budgeted = 1,
}

/// <summary>One adopted fix: where the community source made it, and what it changes.</summary>
public sealed record GameFixInfo(GameFix Fix, string Name, string Source, string Summary);

/// <summary>
/// The rules a world is simulated by: the original's (no fix on - the profile every reference value is measured with) or the original with a set of adopted fixes
/// (<see cref="GameFix"/>). The profile is part of a catalog's identity, like the tick rate: <see cref="Combine"/> folds it into the definition fingerprint, so a world,
/// a replay, a save and a match description made with one profile are refused by a machine with another - two machines with different rules are certain to diverge.
/// The original profile leaves the fingerprint unchanged, so that nothing recorded before profiles existed changes its identity.
/// </summary>
public class RuleProfile : IEquatable<RuleProfile>
{
    private RuleProfile(ImmutableSortedSet<GameFix> fixes) => Fixes = fixes;

    /// <summary>
    /// A profile with <see cref="MovementProfile.Budgeted"/>. A type of its own rather than a field: the profile is part of the catalog the input manifest describes field by
    /// field, and a profile of the original's movement keeps the description (and the manifest) it had before the movement profile existed.
    /// </summary>
    private sealed class WithBudgetedMovement(ImmutableSortedSet<GameFix> fixes) : RuleProfile(fixes);

    /// <summary>The name of <see cref="MovementProfile.Budgeted"/> in a profile's name and on the command line.</summary>
    public const string BudgetedMovementName = "movement-budget";

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
        new(GameFix.DoorExitToRally, "door-exit-to-rally",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/OpenContain.cpp, OpenContain::getRallyPoint",
            "a rider leaving a container through its door - an infantryman healed in a barracks - walks on to the rally point of the building's production exit"),
        new(GameFix.BikeTrainableFromRider, "bike-trainable-from-rider",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/RiderChangeContain.cpp, RiderChangeContain::onContaining",
            "a combat bike is trainable only while its rider is, so a worker on a bike cannot rank up through the bike's kills"),
        new(GameFix.EnterNeedsVerticalOverlap, "enter-needs-vertical-overlap",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIStates.cpp, AIEnterState::update (hasVerticalOverlap)",
            "a boarder at the end of its approach goes in only when it and the container overlap in height: a landing transport is boarded once it is low enough, a hovering boarder does not enter a container below it"),
        new(GameFix.ContactWeaponNotViewBlocked, "contact-weapon-not-view-blocked",
            "Core/GameEngine/Source/GameLogic/AI/AIPathfind.cpp, Pathfinder::isAttackViewBlockedByObstacle",
            "an attacker whose current weapon is a contact weapon - a suicide bomber - is never view-blocked, so it can set its charge off at a building it touches though another structure's cells lie in the line"),
        new(GameFix.GuardAnswersOnlyWhenIdle, "guard-answers-only-when-idle",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/AI/AIGuard.cpp, AIGuardMachine::AIGuardMachine; AIGuardRetaliate.cpp, AIGuardRetaliateMachine::AIGuardRetaliateMachine",
            "a guarding unit that is shot while it fights or walks back to its post goes on with that and answers the attacker once it stands idle at its post, instead of turning to the attacker at once - no more alternating between returning and shooting back"),
        new(GameFix.NoPowerEdgeForUnfinishedPlant, "no-power-edge-for-unfinished-plant",
            "GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Object.cpp, Object::onDisabledEdge",
            "an EMP or another disable on a power plant that is still being built takes no power from its player - it produces none yet - instead of putting the base into a brown-out"),
    ];

    // Declared after the catalogue: static initialisers run in textual order.
    /// <summary>The original game's rules: no fix.</summary>
    public static RuleProfile Original { get; } = new(ImmutableSortedSet<GameFix>.Empty);

    /// <summary>Every adopted fix.</summary>
    public static RuleProfile AllFixes { get; } = new(ImmutableSortedSet.CreateRange(Catalogue.Select(info => info.Fix)));

    public ImmutableSortedSet<GameFix> Fixes { get; }

    /// <summary>How ground units plan their paths; <see cref="MovementProfile.Original"/> in the original profile.</summary>
    public MovementProfile Movement => this is WithBudgetedMovement ? MovementProfile.Budgeted : MovementProfile.Original;

    public bool IsOriginal => Fixes.IsEmpty && Movement == MovementProfile.Original;

    public bool Has(GameFix fix) => Fixes.Contains(fix);

    /// <summary><c>original</c>, or the names of the fixes in their numeric order joined by <c>+</c>, then <c>movement-budget</c> when the movement is budgeted.</summary>
    public string Name => IsOriginal ? "original"
        : string.Join('+', Fixes.Select(fix => Info(fix).Name).Concat(Movement == MovementProfile.Budgeted ? [BudgetedMovementName] : []));

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
    /// <c>original</c>, <c>fixes</c> (every adopted fix) or a list of fix names separated by <c>,</c> or <c>+</c>; <c>movement-budget</c> in the list selects
    /// <see cref="MovementProfile.Budgeted"/>, and <c>fixes</c> may stand in the list for every fix. An unknown name is an error, never ignored.
    /// </summary>
    public static RuleProfile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var value = text.Trim();
        if (value.Equals("original", StringComparison.OrdinalIgnoreCase)) return Original;
        if (value.Equals("fixes", StringComparison.OrdinalIgnoreCase)) return AllFixes;
        var fixes = ImmutableSortedSet.CreateBuilder<GameFix>();
        var movement = MovementProfile.Original;
        foreach (var part in value.Split([',', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals(BudgetedMovementName, StringComparison.OrdinalIgnoreCase)) { movement = MovementProfile.Budgeted; continue; }
            if (part.Equals("fixes", StringComparison.OrdinalIgnoreCase)) { fixes.UnionWith(AllFixes.Fixes); continue; }
            var info = Catalogue.FirstOrDefault(entry => entry.Name.Equals(part, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown rule profile or fix '{part}'. Known: original, fixes, {BudgetedMovementName}, {string.Join(", ", Catalogue.Select(entry => entry.Name))}.");
            fixes.Add(info.Fix);
        }
        if (fixes.Count == 0 && movement == MovementProfile.Original) throw new ArgumentException("An empty rule profile; say 'original'.");
        return movement == MovementProfile.Budgeted ? new WithBudgetedMovement(fixes.ToImmutable()) : new RuleProfile(fixes.ToImmutable());
    }

    public bool Equals(RuleProfile? other) => other is not null && Fixes.SequenceEqual(other.Fixes) && Movement == other.Movement;

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
