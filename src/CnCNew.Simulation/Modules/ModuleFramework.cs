using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules;

/// <summary>Typed, immutable per-template configuration of a module, produced by a binding from a <c>ModuleConfiguration</c>.</summary>
public abstract record ModuleData;

/// <summary>
/// What modules may use of the world. Deliberately narrow: modules read the tick, draw from the <c>logic</c> stream, find
/// entities, and mutate the world only through requests (events, destroy, damage, wake), never by touching each other.
/// </summary>
public interface IWorldServices
{
    long Tick { get; }

    /// <summary>The time service of the world's tick rate: logic frames of the original, seconds and per-second values to ticks and per-tick values.</summary>
    SimulationTime Time { get; }

    /// <summary>The loaded map's terrain; <see langword="null"/> in fixture worlds (flat ground at height 0, no boundaries).</summary>
    TerrainMap? Terrain { get; }

    /// <summary>Passability grid and path search over <see cref="Terrain"/>; <see langword="null"/> exactly when there is no terrain (objects then move in straight lines).</summary>
    NavigationGrid? Navigation { get; }

    /// <summary>Terrain height at (x, y); 0 without terrain.</summary>
    Fixed GroundHeightAt(Fixed x, Fixed y);

    /// <summary><c>ScriptEngine::adjustToppleDirection</c>: the direction a script gave a named object to fall in (<c>NAMED_SET_TOPPLE_DIRECTION</c>), normalised; false when it has none.</summary>
    bool TryGetToppleDirectionOverride(Entity entity, out Fixed x, out Fixed y) { x = Fixed.Zero; y = Fixed.Zero; return false; }
    SageRandom LogicRandom { get; }
    Entity? FindEntity(EntityId id);

    /// <summary>Every live entity (registered, not yet destroyed). Used by area-effect modules (<c>AutoHealBehavior</c>); O(n), fine at fixture scale.</summary>
    IReadOnlyCollection<Entity> Entities { get; }

    /// <summary>The entities whose template has the kind, in ascending id order: what a scan over <see cref="Entities"/> with <c>Template.IsKindOf(kind)</c> visits.</summary>
    EntityList EntitiesOfKind(string kind);

    /// <summary>The entities of one owner, in ascending id order: what a scan over <see cref="Entities"/> with <c>Owner == owner</c> visits.</summary>
    EntityList EntitiesOwnedBy(PlayerId owner);

    /// <summary>The entities with a module that is a <typeparamref name="TModule"/>, in ascending id order: what a scan over <see cref="Entities"/> with that test visits.</summary>
    EntityList EntitiesWithModule<TModule>() where TModule : class;

    /// <summary>What the world was created with; <see cref="WorldConfiguration.Players"/> are all players there are besides the system player.</summary>
    WorldConfiguration Configuration { get; }
    void Emit(SimEventKind kind, EntityId entity, EntityId other, string text, long value);

    /// <summary>Requests removal at the end of the tick. The entity receives no further updates, but its events are still delivered.</summary>
    void RequestDestroy(EntityId id);

    DamageOutcome Damage(EntityId target, in DamageInfo info);

    /// <summary>One resolved shot of <paramref name="weapon"/> from <paramref name="source"/> at a victim or a position, including radius damage (see <c>SimulationWorld.DealWeaponDamage</c>); returns the damage the primary victim took.</summary>
    Fixed DealWeaponDamage(Entity source, WeaponInfo weapon, Entity? primaryVictim, Fixed x, Fixed y, Fixed z);

    /// <summary><c>Object::kill</c>: unresistable damage with the kill flag, so that die modules run.</summary>
    void Kill(EntityId target);

    /// <summary>Called by a body when its entity died; emits the event and runs the die modules in module order.</summary>
    void OnEntityDied(Entity entity, in DamageInfo info);

    /// <summary>Marks an entity whose state a module changed from outside that entity's own update (the world only tracks the updated entity on its own).</summary>
    void MarkDirty(Entity entity);

    /// <summary><c>friend_awakenUpdateModule</c>: moves the module's next wake tick.</summary>
    void WakeAt(Entity entity, IUpdateModule module, long tick);

    /// <summary>
    /// A new AI command replaces the entity's current AI state, which ends an attack (<c>AIAttackState::onExit</c> clears the turrets' targets): the turrets stop tracking their victim and
    /// start their recentre hold. A no-op for objects without a turret that tracks.
    /// </summary>
    void EndAttack(Entity entity);

    /// <summary>
    /// <c>AI_ATTACK_MOVE_TO</c> begins for the entity, which is being ordered to move to (<paramref name="x"/>, <paramref name="y"/>): it engages what it meets on the way and resumes the
    /// march afterwards. Called by the AI module from inside its move order, after <see cref="EndAttack"/>.
    /// </summary>
    void BeginAttackMove(Entity entity, Fixed x, Fixed y);

    // ------------------------------------------------------------------------------------ phase 5: economy, upgrades

    UpgradeCatalog Upgrades { get; }
    WeaponCatalog Weapons { get; }
    CrateCatalog Crates { get; }
    TemplateCatalog Templates { get; }

    /// <summary>The controlling player's current balance (0 for the system player or an unknown player).</summary>
    Fixed MoneyOf(PlayerId player);
    void AddMoney(PlayerId player, Fixed amount);

    /// <summary><c>Energy::addProduction</c> (through <c>addPowerBonus</c>/<c>removePowerBonus</c>): changes the player's production by a signed amount; unknown players are ignored.</summary>
    void AddEnergyProduction(PlayerId player, int amount);

    /// <summary><c>Money::withdraw</c>: clamps to the available balance and returns the amount actually withdrawn.</summary>
    Fixed WithdrawMoney(PlayerId player, Fixed amount);

    /// <summary>Player-scoped completed-upgrade mask (<c>Player::getCompletedUpgradeMask</c>).</summary>
    UpgradeMask PlayerUpgrades(PlayerId player);

    /// <summary>Union of the entity's own and its controlling player's completed-upgrade masks — the reference's dispatch key.</summary>
    UpgradeMask UpgradeKeyMaskOf(Entity entity);

    /// <summary>
    /// Completes an upgrade for a player (<see cref="UpgradeScope.Player"/>) or one entity (<see cref="UpgradeScope.Object"/>),
    /// then re-evaluates every <see cref="IUpgradeModule"/> the change can affect (<c>Player::onUpgradeCompleted</c>).
    /// </summary>
    void CompleteUpgrade(PlayerId player, EntityId? objectScope, string upgradeName);

    void RemoveUpgradeByName(Entity entity, string upgradeName);

    /// <summary><c>ExperienceTracker::setMinVeterancyLevel</c>: raises the entity's veterancy level to at least <paramref name="level"/> and runs the level-change effects (also valid during <c>OnSpawn</c>).</summary>
    void SetMinVeterancyLevel(Entity entity, int level);

    /// <summary><c>ExperienceTracker::addExperiencePoints</c>: a trainable object gains <c>amount</c> scaled by its experience scalar and takes the level its experience earns.</summary>
    void AddExperiencePoints(Entity entity, int amount);

    /// <summary>Reserves and queues the creation of a fresh entity outside the command/validation path (engine-internal spawns: production, OCLs).</summary>
    EntityId RequestSpawn(int templateIndex, PlayerId owner, Fixed x, Fixed y, Fixed z, Fixed angle);

    /// <summary><see cref="RequestSpawn(int, PlayerId, Fixed, Fixed, Fixed, Fixed)"/> with the adjustments an engine-internal spawner applies to the fresh object (<see cref="SpawnExtras"/>).</summary>
    EntityId RequestSpawn(int templateIndex, PlayerId owner, Fixed x, Fixed y, Fixed z, Fixed angle, in SpawnExtras extras);

    /// <summary>
    /// One tick of construction work on a site (<c>DozerAIUpdate::update</c> in <c>DOZER_TASK_BUILD</c>): health rises, and the last tick completes the structure
    /// (clears <c>UNDER_CONSTRUCTION</c>, starts its power production and create modules). True when the structure is complete.
    /// </summary>
    bool AdvanceConstruction(Entity site);

    /// <summary><c>Thing::isSignificantlyAboveTerrain</c>: higher than three frames of free fall (the terrain is flat at height 0 until a terrain model exists).</summary>
    bool IsSignificantlyAboveTerrain(Entity entity);

    /// <summary>
    /// <c>ObjectCreationList::create</c> with <paramref name="source"/> as primary object: queues the spawns the list describes and returns
    /// the first created entity (<see cref="EntityId.Invalid"/> when none). Emits <see cref="SimEventKind.CreationListRequested"/> for the
    /// request itself and <see cref="SimEventKind.CreationSkipped"/> for everything that is not simulated. With
    /// <paramref name="transferPreviousHealth"/> the first created entity takes the damage the source had suffered before it died.
    /// </summary>
    EntityId CreateFromList(Entity source, string listName, EntityId requester, bool transferPreviousHealth);

    /// <summary><see cref="CreateFromList(Entity, string, EntityId, bool)"/> with the creation origin an <c>OCLSpecialPower</c> chooses (<c>ObjectCreationList::create(ocl, primary, &creationCoord, …)</c>) instead of the source's position.</summary>
    EntityId CreateFromList(Entity source, string listName, EntityId requester, bool transferPreviousHealth, Fixed originX, Fixed originY, Fixed originZ);

    /// <summary>
    /// <c>ObjectCreationList::create(ocl, primaryObj, &primary, &secondary, angle, createOwner)</c>: the creation origin (<c>primary</c>) and the target (<c>secondary</c>) a
    /// <c>DeliverPayload</c> nugget needs; with <paramref name="createOwner"/> false the source object itself is the transport (<c>USE_OWNER_OBJECT</c>).
    /// </summary>
    EntityId CreateFromList(Entity source, string listName, EntityId requester, bool transferPreviousHealth, Fixed originX, Fixed originY, Fixed originZ, Fixed targetX, Fixed targetY, Fixed targetZ, bool createOwner);

    /// <summary>
    /// <c>DeliverPayloadDataState</c> of the visible payload: a projectile object of <paramref name="templateIndex"/> is created at the start point and launched at the target with
    /// <paramref name="weapon"/> (<c>projectileFireAtObjectOrPosition</c>); an object without a projectile module is simply put at the target.
    /// </summary>
    /// <summary>
    /// <c>WeaponStore::createAndFireTempWeapon(template, source, pos)</c>: a weapon of this name is fired once from <paramref name="source"/> at a position, without any clip or delay of the source
    /// (death weapons of <c>FireWeaponWhenDeadBehavior</c> and <c>InstantDeathBehavior</c>). An unknown name is reported as a skipped creation, never ignored.
    /// </summary>
    void FireTempWeapon(Entity source, string weaponName, Fixed x, Fixed y, Fixed z);

    /// <summary><c>Object::fireCurrentWeapon(pos)</c>/<c>(victim)</c>: fires the current weapon (the locked slot, else the primary one) at a position (aimed at <paramref name="victim"/> when given) when it is ready (false otherwise).</summary>
    bool FireCurrentWeaponAtPosition(Entity source, Fixed x, Fixed y, Fixed z, Entity? victim = null);

    /// <summary><c>WeaponSet::setWeaponLock</c>: pins <paramref name="slot"/> as the weapon the object fires (a temporary lock never replaces a permanent one); false when the active weapon set has no weapon there.</summary>
    bool SetWeaponLock(Entity entity, int slot, WeaponLockKind kind);

    /// <summary><c>WeaponSet::releaseWeaponLock</c>: a permanent release clears every lock, a temporary one only a temporary lock.</summary>
    void ReleaseWeaponLock(Entity entity, WeaponLockKind kind);

    /// <summary>The object's weapon set flags changed (<c>WeaponSetUpgrade</c>, a salvage crate, a car bomb conversion): the weapons of the now active set are loaded and ready.</summary>
    void WeaponSetFlagsChanged(Entity entity);

    EntityId LaunchProjectileObject(Entity launcher, int templateIndex, WeaponInfo weapon, Fixed startX, Fixed startY, Fixed startZ, Fixed targetX, Fixed targetY, Fixed targetZ);

    ObjectCreationListCatalog CreationLists { get; }

    // ------------------------------------------------------------------------------------------- phase 5: sciences

    ScienceCatalog Sciences { get; }
    ScienceMask PlayerSciences(PlayerId player);

    /// <summary><c>GlobalData::m_unitDamagedThresh</c>/<c>m_unitReallyDamagedThresh</c> (<c>GameData.ini</c>): health-ratio thresholds <see cref="BuiltIn.BodyDamageStates"/> is computed from.</summary>
    Fixed UnitDamagedThreshold { get; }
    Fixed UnitReallyDamagedThreshold { get; }

    /// <summary><c>GlobalData::m_baseRegenHealthPercentPerSecond</c> (ratio) and <c>m_baseRegenDelay</c> (ticks), read by <c>BaseRegenerateUpdate</c>.</summary>
    Fixed BaseRegenHealthPercentPerSecond { get; }
    long BaseRegenDelayTicks { get; }

    /// <summary><c>GlobalData::m_baseValuePerSupplyBox</c>: the payout of one supply box at a supply center.</summary>
    int ValuePerSupplyBox { get; }

    /// <summary><c>Player::getRelationship</c>: <paramref name="from"/>'s directed view of <paramref name="to"/> (teams plus explicit overrides; self = allies, neutral objects = neutral).</summary>
    Relationship RelationshipOf(PlayerId from, PlayerId to);

    /// <summary>The arrival half of <c>aiEnter</c>: puts <paramref name="actor"/> into <paramref name="container"/> when the container accepts it; false leaves both untouched.</summary>
    bool TryEnterContainer(Entity actor, Entity container);

    /// <summary><c>Object::setTeam</c> for a controlling-player change: the object (and the power it supplies or consumes) now belongs to <paramref name="newOwner"/>; false when nothing changed.</summary>
    bool TransferOwnership(EntityId id, PlayerId newOwner);

    /// <summary><c>Player::getPlayerType() == PLAYER_COMPUTER</c>.</summary>
    bool IsComputerPlayer(PlayerId player);

    /// <summary><see cref="RelationshipOf"/> == enemies.</summary>
    bool AreEnemies(PlayerId a, PlayerId b);

    /// <summary>"Science Purchase Points" earned via rank/experience in the reference (not modeled); a scenario/system command credits them directly instead — see <c>docs/GAME-RULES.md</c>.</summary>
    int SciencePointsOf(PlayerId player);
    void AddSciencePoints(PlayerId player, int amount);

    /// <summary><c>Player::grantScience</c>: adds a science without paying (no prerequisite or purchasability check beyond <c>isScienceGrantable</c>), then re-arms the special powers it gates. False when it was already owned.</summary>
    bool GrantScience(PlayerId player, string scienceName);

    // ------------------------------------------------------------------------------------------- special powers

    SpecialPowerCatalog SpecialPowers { get; }

    /// <summary><c>SpecialPowerStore</c> shared timer (<c>SharedSyncedTimer</c>): the ready frame all of a player's modules for one power share; false when none was recorded yet.</summary>
    bool TryGetSharedReadyFrame(PlayerId player, int powerIndex, out long readyFrame);
    void SetSharedReadyFrame(PlayerId player, int powerIndex, long readyFrame);

    /// <summary><c>SpecialAbilityUpdate::triggerAbilityEffect</c> for the capture powers (<c>Object::defect</c> into the capturer's team): <paramref name="target"/> changes to <paramref name="actor"/>'s owner, its power-pool bonus moves with it and <see cref="SimEventKind.StructureCaptured"/> is emitted.</summary>
    void CaptureStructure(Entity actor, Entity target);

    /// <summary><c>Player::m_cashBountyPercent</c> as a ratio (0: no bounty); <c>CashBountyPower</c> only ever raises it.</summary>
    Fixed CashBountyOf(PlayerId player);
    void RaiseCashBounty(PlayerId player, Fixed ratio);

    // ------------------------------------------------------------------------------------ phase 8: flight time, tunnels

    /// <summary>An event that happens at a place (projectile launch/impact, detonation effect); see <see cref="SimEvent.HasPosition"/>.</summary>
    void EmitAt(SimEventKind kind, EntityId entity, EntityId other, string text, long value, Fixed x, Fixed y, Fixed z);

    /// <summary><c>WeaponTemplate::fireWeaponTemplate</c> from <paramref name="source"/> at a victim or a position: scatter, delayed or immediate hit, or a launched projectile. Returns the damage the victim took right now.</summary>
    Fixed FireWeaponAt(Entity source, WeaponInfo weapon, Entity? victim, Fixed x, Fixed y, Fixed z);

    /// <summary><c>WeaponStore::handleProjectileDetonation</c> at the projectile's position, credited to <paramref name="launcher"/>.</summary>
    void DetonateProjectile(Entity projectile, WeaponInfo weapon, EntityId launcher, uint conditions, bool damage = true);

    /// <summary><c>Player::getTunnelSystem</c>: the player's tunnel network (every player has one; an unknown player gets an empty, unused one).</summary>
    TunnelTracker TunnelSystemOf(PlayerId player);

    /// <summary><c>ContainModuleInterface::removeAllContained</c> as an ordered evacuation: every passenger leaves through <paramref name="container"/> (for a tunnel: everybody in the network).</summary>
    void EvacuateContainer(Entity container);

    // ------------------------------------------------------------------------------------ phase 10: footprint collision

    /// <summary>
    /// Local collision avoidance for ground path-following (<c>docs/SIMULATION-CORE.md</c>, phase 10): when the straight
    /// step from <paramref name="mover"/>'s position by (<paramref name="dx"/>, <paramref name="dy"/>) would end inside
    /// another live, mobile entity's footprint, rotates the step by one of a few fixed angles and retries, bounded to a
    /// handful of attempts; returns the unmodified step when every attempt is still blocked. A bounded nudge-and-retry,
    /// not steering or flocking: it does not coordinate with the blocker, does not look beyond this one step and can
    /// oscillate against a mover coming the other way.
    /// </summary>
    (Fixed Dx, Fixed Dy) AvoidLocalBlockers(Entity mover, Fixed dx, Fixed dy);

    /// <summary>
    /// When the step (<paramref name="dx"/>, <paramref name="dy"/>) of <paramref name="mover"/> would land in a unit that stands still, the bounding circles of the standing units around the mover
    /// (the goal object excepted), else an empty list: what a path search has to route around, because the static obstacle set does not know units. The list is the world's own
    /// buffer and only valid until the next call.
    /// </summary>
    IReadOnlyList<(Fixed X, Fixed Y, Fixed Radius)> StationaryBlockersAhead(Entity mover, Fixed dx, Fixed dy);

    /// <summary>
    /// <c>Pathfinder::adjustDestination</c>: when the place <paramref name="mover"/> is heading for (<see cref="Entity.MoveTargetX"/>) is taken — a unit of a side that is not
    /// its enemy stands there, or one with a lower id is heading there — the nearest free place on a square spiral of path cells around it; false when the goal is free, the
    /// mover flies or closes in on a victim, or no free place lies within 400 cells.
    /// </summary>
    bool TryFreeGoalSpot(Entity mover, out Fixed x, out Fixed y);

    /// <summary>
    /// <c>SquishCollide.cpp onCollide</c> / <c>ToppleUpdate.cpp onCollide</c>: after <paramref name="mover"/>'s position
    /// changed this tick by (<paramref name="stepX"/>, <paramref name="stepY"/>) — the exact displacement just applied, used
    /// to tell a closing mover from a receding one without rebuilding velocity from <c>Angle</c>/<c>Speed</c> — checks every
    /// other live entity whose footprint now overlaps the mover's and applies the crush (a <c>SquishCollide</c> victim, not
    /// allied, the mover closing on it) or topple (a <c>ToppleUpdate</c> object, the mover's <c>CrusherLevel</c> above 1)
    /// effect that the reference's physics collision would have delivered.
    /// </summary>
    void ResolveFootprintCollisions(Entity mover, Fixed stepX, Fixed stepY);

    // ------------------------------------------------------------------------------------ crates, collisions, sabotage (see SimulationWorld.CrateServices.cs)

    /// <summary>
    /// <c>ExperienceTracker::gainExpForLevel</c>: gives <paramref name="entity"/> exactly the experience it needs to rise <paramref name="levels"/> veterancy levels
    /// (capped at heroic), scaled by its experience scalar when <paramref name="scaleForBonus"/>; false when no level can be gained. A non-trainable object takes
    /// nothing (<c>addExperiencePoints</c> returns early) but still reports true when a level was available, as the reference does.
    /// </summary>
    bool GainExperienceForLevels(Entity entity, int levels, bool scaleForBonus);

    /// <summary><c>ExperienceTracker::setVeterancyLevel</c>: sets the level exactly (up or down), the experience becomes the level's requirement and the level-change effects run.</summary>
    void SetVeterancy(Entity entity, int level);

    /// <summary><c>Energy::setPowerSabotagedTillFrame</c>: the player's power counts as sabotaged until the given tick (overwrites any earlier value; unknown players are ignored).</summary>
    void SetPowerSabotagedUntil(PlayerId player, long tick);

    /// <summary><c>Energy::m_powerSabotagedTillFrame</c> as a tick; 0 when the player's power was never sabotaged.</summary>
    long PowerSabotagedUntil(PlayerId player);

    /// <summary>
    /// The attack half of a <c>FireWeaponCommand</c> for an object's own weapon choice, issued by a module instead of the player (<c>CMD_FROM_AI</c>): the best weapon of the active
    /// weapon set against the victim fires when it is ready and the victim is alive and within its range (the slot's clip, reload and last-shot state are kept like the command keeps
    /// them; <see cref="FireWeaponAt"/>); returns whether a shot was fired. There is no visibility check (the module acts for the object itself, not for a player).
    /// </summary>
    bool TryFireActiveWeapon(Entity source, Entity victim);

    /// <summary>
    /// One shot of weapon <paramref name="slot"/> (<c>WeaponSlot</c>) at the victim, for a module that locks that slot (<c>AssistedTargetingUpdate</c>): the slot's weapon must be loaded,
    /// ready, able to hit the victim and have it in range; the slot's own clip and reload keep the bookkeeping. False when no shot goes out.
    /// </summary>
    bool TryFireWeaponSlot(Entity source, int slot, Entity victim);

    /// <summary><c>Object::reloadAllAmmo(true)</c>: every clipped weapon of the active weapon set is full again and ready now (an aircraft that landed at its airfield).</summary>
    void ReloadAllAmmo(Entity entity);

    /// <summary>Every player of the world in ascending id order, the neutral system player included (<c>ThePlayerList</c>).</summary>
    IReadOnlyList<PlayerId> PlayerIds { get; }

    /// <summary>
    /// <c>Player::setUnitsVisionSpied(setting, whichUnits, byWhom)</c>: raises or lowers, on every object of <paramref name="victim"/> that is of one of <paramref name="kinds"/> (empty: any), the
    /// reference count of "<paramref name="byWhom"/> looks through this object's eyes". Per object and unbalanced exactly like the reference (an object created between an
    /// activation and its deactivation ends at -1 and is never spied again until it climbs back above 0).
    /// </summary>
    void SetUnitsVisionSpied(PlayerId victim, bool setting, ImmutableArray<string> kinds, PlayerId byWhom);

    /// <summary>
    /// <c>Player::changeBattlePlan</c>: a strategy center brings a plan of its player into (<paramref name="delta"/> +1) or out of (-1) force. The first center of a plan hands the bonus to every qualifying
    /// object of the player (the <c>BATTLEPLAN_*</c> weapon bonus, the hold-the-line damage scalar, the search-and-destroy sight scalar) and the last one to leave takes it back; objects created or
    /// transferred later receive the standing bonuses (see <c>SimulationWorld.BattlePlan.cs</c>).
    /// </summary>
    void ChangeBattlePlan(PlayerId player, Modules.BuiltIn.BattlePlanKind plan, int delta, Modules.BuiltIn.BattlePlanBonus bonus);

    /// <summary>The number of centers whose plan <paramref name="plan"/> is in force for the player (<c>Player::getBattlePlansActiveSpecific</c>).</summary>
    int BattlePlansActive(PlayerId player, Modules.BuiltIn.BattlePlanKind plan);

    /// <summary><c>paralyzeTroop</c> for every qualifying object of the player: until <paramref name="untilTick"/> it neither moves nor fires (<c>DISABLED_PARALYZED</c>).</summary>
    void ParalyzeTroops(PlayerId player, ImmutableArray<string> validKinds, ImmutableArray<string> invalidKinds, long untilTick);

    /// <summary><c>BodyModuleInterface::getDamageScalar</c>: the factor every non-unresistable damage is multiplied by (1 unless a hold-the-line battle plan covers the object).</summary>
    Fixed DamageScalarOf(Entity entity);

    /// <summary>Scales the object's shroud clearing range by <paramref name="scalar"/> (<c>Object::setShroudClearingRange</c>); 1 restores the template's range.</summary>
    void SetShroudScalar(Entity entity, Fixed scalar);

    // ------------------------------------------------------------------------------------ structures and world group

    /// <summary><c>ObjectCreationList::create(list, source, secondary, lifetimeFrames)</c>: the list is executed with every created object's <c>LifetimeUpdate</c> range replaced by <paramref name="lifetimeTicks"/>; returns the first created entity.</summary>
    EntityId CreateFromListWithLifetime(Entity source, string listName, EntityId requester, long lifetimeTicks);

    /// <summary><c>Player::addKindOfProductionCostChange</c>: objects with all of <paramref name="kinds"/> cost <c>1 + percent</c> times as much for the player from now on (reference counted per kinds/percent pair).</summary>
    void AddProductionCostChange(PlayerId player, ImmutableArray<string> kinds, Fixed percent);

    /// <summary><c>Player::removeKindOfProductionCostChange</c>: undoes one <see cref="AddProductionCostChange"/>.</summary>
    void RemoveProductionCostChange(PlayerId player, ImmutableArray<string> kinds, Fixed percent);

    /// <summary>The damage amount after the victim's armor (<c>ActiveBody::attemptDamage</c> adjusts it inside the body in the reference; here the caller of <see cref="Damage"/> does).</summary>
    Fixed AdjustDamageByArmor(Entity victim, int damageType, Fixed amount);

    /// <summary><c>PlayerTemplate::getSide()</c> of the player's faction (empty without a faction, for the system player and an unknown player).</summary>
    string FactionSideOf(PlayerId player);

    /// <summary><c>Player::isPlayableSide</c>: the player's faction is a playable side (false without a faction and for the system player).</summary>
    bool IsPlayableSide(PlayerId player);
}

/// <summary>
/// What a projectile object is told when it is launched (<c>ProjectileUpdateInterface::projectileLaunchAtObjectOrPosition</c>): the firing
/// weapon (its detonation weapon), the launcher, the victim it may follow (invalid for a position shot or a scattered one), the aim point and
/// the launcher's weapon bonus conditions (<c>m_extraBonusFlags</c>).
/// </summary>
public readonly record struct ProjectileLaunch(int Weapon, EntityId Launcher, EntityId Victim, Fixed X, Fixed Y, Fixed Z, uint Conditions);

/// <summary><c>ProjectileUpdateInterface</c>: <c>DumbProjectileBehavior</c> and <c>MissileAIUpdate</c>.</summary>
public interface IProjectileModule
{
    /// <summary>Runs right after the projectile object was created (after <see cref="SimModule.OnSpawn"/>).</summary>
    void Launch(IWorldServices world, in ProjectileLaunch launch);

    /// <summary><c>projectileGetLauncherID</c>.</summary>
    EntityId Launcher { get; }
}

/// <summary><c>ContainModuleInterface::onSelling</c>: called when a sale of the entity starts (<c>BuildAssistant::sellObject</c>).</summary>
public interface ISellModule
{
    void OnSelling(IWorldServices world);
}

/// <summary>A module that needs a world-owned structure (a player's tunnel network) whenever its entity is registered — fresh or restored from a snapshot.</summary>
public interface IWorldAttached
{
    void Attach(IWorldServices world);
}

/// <summary>
/// A module that carries one special power (<c>SpecialPowerModuleInterface</c>): the recharge state machine of <c>SpecialPowerModule</c>
/// (<c>availableOnFrame</c>, pause counter) plus the power-specific effect of the derived module.
/// </summary>
public interface ISpecialPowerModule
{
    int PowerIndex { get; }

    /// <summary><c>UpdateModuleStartsAttack</c>: the power is triggered (recharge started) by an update-module handler, not by the use itself.</summary>
    bool StartsAttack { get; }
    bool IsPaused { get; }

    /// <summary>Whether a use of this power with the given target kind is meaningful (an object power without an object is not).</summary>
    bool AcceptsTarget(SpecialPowerTargetKind kind);

    /// <summary><c>SpecialPowerModule::isReady</c>: recharged (<c>availableOnFrame &lt;= now</c>) and not paused.</summary>
    bool IsReady(IWorldServices world);

    /// <summary><c>SpecialPowerModule::onSpecialPowerCreation</c>: restarts the recharge (shared timers: syncs to the shared frame) and re-applies <c>StartsPaused</c>.</summary>
    void OnSpecialPowerCreation(IWorldServices world);

    /// <summary><c>SpecialPowerModule::setReadyFrame</c>.</summary>
    void SetReadyFrame(long frame);

    /// <summary><c>SpecialPowerModule::pauseCountdown</c>.</summary>
    void PauseCountdown(IWorldServices world, bool pause);

    /// <summary><c>SpecialPowerModule::triggerSpecialPower</c>: starts the recharge.</summary>
    void Trigger(IWorldServices world);

    /// <summary><c>doSpecialPower*</c>: initiates via the entity's handlers, triggers unless <see cref="StartsAttack"/>, then applies the module's own effect.</summary>
    void Use(IWorldServices world, in SpecialPowerTarget target);
}

/// <summary>An update module that carries out a power's effect (<c>SpecialPowerUpdateInterface::initiateIntentToDoSpecialPower</c>); implemented by <c>SpectreGunshipDeploymentUpdate</c>.</summary>
public interface ISpecialPowerHandler
{
    /// <summary>True when this module serves the power for its entity's owner right now (matching template and, where configured, owned science).</summary>
    bool Handles(IWorldServices world, int powerIndex);
    void Initiate(IWorldServices world, int powerIndex, in SpecialPowerTarget target);

    /// <summary>Whether the handler can carry out a use on this target (a capture needs an enemy structure); a refused use is rejected with <c>InvalidSpecialPowerTarget</c> before anything starts.</summary>
    bool ValidatesTarget(IWorldServices world, int powerIndex, in SpecialPowerTarget target) => true;
}

public static class SpecialPowerRouting
{
    /// <summary>The first handler on <paramref name="entity"/> serving the power (module order: the reference lists the highest technology level first).</summary>
    public static ISpecialPowerHandler? FindHandler(Entity entity, IWorldServices world, int powerIndex)
    {
        foreach (var module in entity.Modules)
            if (module is ISpecialPowerHandler handler && handler.Handles(world, powerIndex)) return handler;
        return null;
    }

    public static ISpecialPowerModule? FindModule(Entity entity, int powerIndex)
    {
        foreach (var module in entity.Modules)
            if (module is ISpecialPowerModule power && power.PowerIndex == powerIndex) return power;
        return null;
    }
}

/// <summary>
/// Adjustments an engine-internal spawner applies to a fresh object: <see cref="MaxHealth"/> (positive = replace the template's maximum
/// health keeping the current health, as <c>RebuildHoleExposeDie</c> does) and <see cref="OriginTemplate"/> (index of the template the
/// object stands in for, or -1; delivered to <see cref="ISpawnOriginReceiver"/> modules) and <see cref="ForceSupplyWanting"/> (the
/// supply-center exit tells a fresh truck to start supplying at once, <c>SupplyTruckAIUpdate::privateWanting</c>) and the rally point
/// (<c>ProductionUpdate::setRallyPoint</c>): a mobile object walks there right after it appears.
/// </summary>
public readonly record struct SpawnExtras(Fixed MaxHealth, int OriginTemplate = -1, bool ForceSupplyWanting = false, bool HasRally = false, Fixed RallyX = default, Fixed RallyY = default,
    EntityId Enslaver = default, EntityId Producer = default);

// ------------------------------------------------------------------------------------------------ structures and world group

/// <summary><c>UpgradeModule::onCapture</c>: told that the object's controlling player is about to change (<c>Object::setTeam</c> by a capture or a script transfer), before <see cref="Entity.Owner"/> changes.</summary>
public interface IOwnerChangeReceiver
{
    void OnOwnerChanged(IWorldServices world, PlayerId oldOwner, PlayerId newOwner);
}

/// <summary>A module that wants to know which entity created its object (<c>Object::getProducerID</c>), delivered once right after <see cref="SimModule.OnSpawn"/>.</summary>
public interface IProducerReceiver
{
    void OnProducedBy(IWorldServices world, EntityId producer);
}

/// <summary>Names of the modules that implement <see cref="IProducerReceiver"/>: a creation list only records the producer for objects that carry one (so the saved state of every other object stays as it was).</summary>
public static class ProducerAwareModules
{
    public static bool Contains(string moduleName) => moduleName is "UpgradeDie" or "MinefieldBehavior" or "RailroadBehavior";
}

/// <summary>A module that vetoes or delays a fire command (<c>DeployStyleAIUpdate</c>: a deployed-only weapon); asked when an accepted command executes (never while it is validated, so that replays, which hold only accepted commands, repeat its state changes).</summary>
public interface IFireGate
{
    /// <summary>True when the object may fire now; a gate that returns false may start a state change (deploying) that lets a later command succeed, and the shot is counted as a <c>WeaponNotReady</c> rejection.</summary>
    bool AllowsFire(IWorldServices world, Entity victim);
}

/// <summary>A module that overrides its object's shroud clearing range at run time (<c>DynamicShroudClearingRangeUpdate</c>, <c>Object::setShroudClearingRange</c>).</summary>
public interface IShroudRangeProvider
{
    /// <summary>The current range, or false while the template's own range applies.</summary>
    bool TryGetShroudRange(out Fixed range);
}

/// <summary>A module told which template its object replaces when it is created by <see cref="IWorldServices.RequestSpawn(int, PlayerId, Fixed, Fixed, Fixed, Fixed, in SpawnExtras)"/>.</summary>
public interface ISpawnOriginReceiver
{
    void OnSpawnedFrom(IWorldServices world, int originTemplate);
}

/// <summary>A module that owns the door/exit geometry a producer's new units leave through (<c>ExitInterface</c>).</summary>
public interface IProductionExit
{
    /// <summary><c>ExitInterface::reserveDoorForExit</c>: true when a unit may leave right now.</summary>
    bool IsFreeToExit { get; }

    /// <summary><c>ExitInterface::exitObjectViaDoor</c>, position part: where the unit appears (world space) and which way it faces; the exit's delay/burst state advances.</summary>
    (Fixed X, Fixed Y, Fixed Z, Fixed Angle) ClaimExit(IWorldServices world);

    /// <summary><c>SupplyCenterProductionExitUpdate::exitObjectViaDoor</c>: units leaving this exit start gathering supplies immediately.</summary>
    bool ForcesSupplyWanting => false;

    /// <summary><see cref="IsFreeToExit"/> for a specific product: <c>ParkingPlaceBehavior::hasAvailableSpaceFor</c> needs no parking space for a <c>PRODUCED_AT_HELIPAD</c> object.</summary>
    bool IsFreeToExitFor(ObjectTemplate produced) => IsFreeToExit;

    /// <summary><see cref="ClaimExit(IWorldServices)"/> for a specific product (<c>ParkingPlaceBehavior::reserveDoorForExit</c> reserves a door only for what needs one).</summary>
    (Fixed X, Fixed Y, Fixed Z, Fixed Angle) ClaimExit(IWorldServices world, ObjectTemplate produced) => ClaimExit(world);
}

/// <summary>A module driven by the <see cref="UpgradeMux"/> state machine (<c>UpgradeModule</c> / any <c>UpgradeMux</c>-mixin module).</summary>
public interface IUpgradeModule
{
    void TryActivate(IWorldServices world, UpgradeMask keyMask);
    void TryReset(IWorldServices world, UpgradeMask keyMask);

    /// <summary><c>UpgradeModuleInterface::wouldUpgrade</c>: whether the module would execute in response to this combination of completed upgrades (a module that never does reports false).</summary>
    bool WouldUpgrade(UpgradeMask keyMask) => false;
}

/// <summary>A body that can change its maximum health at run time (<c>MaxHealthUpgrade.cpp</c>). Not every body supports this (<c>InactiveBody</c> has no health).</summary>
/// <summary>A body whose current health can be set as a percentage of its initial health on a fresh object (<c>BodyModuleInterface::setInitialHealth</c>, used by object creation lists).</summary>
public interface IInitialHealthSettable
{
    void SetInitialHealth(IWorldServices world, int percent);
}

public interface IMaxHealthAdjustable
{
    void AdjustMaxHealth(IWorldServices world, Fixed delta, string changeType);
}

public abstract class SimModule
{
    public Entity Entity { get; internal set; } = null!;
    public int Index { get; internal set; }
    public string Tag { get; internal set; } = string.Empty;
    public abstract string ModuleName { get; }

    /// <summary>
    /// The words <see cref="World.Entity.DescribeModuleFrame"/> writes for this module, prepared when it was attached and shared by all modules of the same template slot.
    /// A shortcut for the hash only, never state: <see langword="null"/> means "write them out".
    /// </summary>
    internal ulong[]? FrameWords { get; set; }

    /// <summary>Runs once when the entity is created (not when restored from a snapshot). May schedule updates and draw random numbers.</summary>
    public virtual void OnSpawn(IWorldServices world) { }

    /// <summary>Runs in the destroy phase, before the entity is removed.</summary>
    public virtual void OnRemove(IWorldServices world) { }

    public abstract void Describe(IStateWriter writer);
    public abstract void Load(IStateReader reader);
}

public interface IUpdateModule
{
    /// <summary>Absolute tick of the next call; <see cref="Time.SimulationTiming.SleepForever"/> or more means "not scheduled".</summary>
    long NextWake { get; set; }

    /// <summary>Returns the number of ticks to sleep (at least 1) or <see cref="Time.SimulationTiming.SleepForever"/>.</summary>
    long Update(IWorldServices world);
}

public interface IDieModule
{
    void OnDie(IWorldServices world, in DamageInfo info);
}

/// <summary>A body whose health rises with construction progress independently of damage and healing (<c>Object::setConstructionPercent</c> healing of the body).</summary>
public interface IConstructionBody
{
    void ChangeHealthForConstruction(IWorldServices world, Fixed delta);
}

public interface IBodyModule
{
    Fixed Health { get; }
    Fixed MaxHealth { get; }
    DamageOutcome AttemptDamage(IWorldServices world, in DamageInfo info);
}

public interface IEventReceiver
{
    void OnEvent(IWorldServices world, in SimEvent simEvent);
}

/// <summary>
/// A module that can hold other entities (<c>ContainModuleInterface</c>/<c>OpenContain</c>). Phase 5 models one
/// container type (<c>GarrisonContain</c>) with a bounded feature set — capacity, <c>KindOf</c>/relationship gating,
/// death and damage-state ejection, periodic healing — not the reference's full <c>OpenContain</c> (firepoints, exit
/// paths, weapon-bonus passthrough, capture handling: all deferred, documented at the module).
/// </summary>
public interface IContainerModule
{
    /// <summary>-1 means unlimited (<c>CONTAIN_MAX_UNKNOWN</c>).</summary>
    int ContainMax { get; }
    int ContainCount { get; }
    IReadOnlyList<EntityId> Contained { get; }
    bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity);
    void Add(IWorldServices world, Entity candidate);

    /// <summary><paramref name="positionAtContainer"/> is false only when the container itself is gone (its position is stale/irrelevant).</summary>
    void Remove(IWorldServices world, Entity candidate, bool positionAtContainer);

    /// <summary><c>ContainModuleInterface::isSpecificRiderFreeToExit</c>, as an exit command asks it: false refuses the command (the ferry's riders cannot leave while it is in transit); true for every container but the ferry.</summary>
    bool IsRiderFreeToExit(Entity rider) => true;

    /// <summary>
    /// <c>ContainModuleInterface::exitObjectViaDoor</c> for a container that runs its own exit sequence instead of letting the rider simply step out (<c>RailedTransportContain</c>
    /// hands the exit to its dock): true when the container took the exit over, false to let the world remove the rider with <see cref="Remove"/>.
    /// </summary>
    bool ExitViaDoor(IWorldServices world, Entity rider) => false;
}

/// <summary>
/// <c>DamageModuleInterface</c>: called for every damage application that actually lowers health, before the
/// fear-sound check and before the death check (so both run even on a killing blow). Either callback may be left at
/// its no-op default (the reference's own <c>DamageModuleInterface</c> subclasses routinely implement only one).
/// </summary>
public interface IDamageModule
{
    void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt) { }

    /// <summary><c>oldState</c>/<c>newState</c> are <see cref="BuiltIn.BodyDamageStates"/> values; <paramref name="info"/> is the damage that caused the transition (matches <c>getLastDamageInfo()</c> in a synchronous, single-threaded model).</summary>
    void OnBodyDamageStateChange(IWorldServices world, in DamageInfo info, int oldState, int newState) { }

    /// <summary><c>ActiveBody::attemptHealing</c>: fires only when health actually rose (mirrors <see cref="OnDamage"/>'s "health actually dropped" gate).</summary>
    void OnHealing(IWorldServices world, in DamageInfo info) { }
}

/// <summary>Bit positions of the object status table (<c>ObjectStatusMaskType::s_bitNameList</c>).</summary>
public static class ObjectStatusBits
{
    public static int Index(string name) => NameTables.ObjectStatus.TryIndexOf(name, out var index)
        ? index
        : throw new ArgumentException($"'{name}' is not an object status.", nameof(name));

    public static int Destroyed { get; } = Index("DESTROYED");
    public static int UnderConstruction { get; } = Index("UNDER_CONSTRUCTION");
    public static int Sold { get; } = Index("SOLD");
    public static int Reconstructing { get; } = Index("RECONSTRUCTING");

    public static ulong Mask(FlagsValue? flags, NameTable table)
    {
        if (flags is null) return 0;
        ulong mask = 0;
        foreach (var name in flags.Names) mask |= 1UL << (table.TryIndexOf(name, out var index) ? index : throw new InvalidDataException($"Unknown flag '{name}'."));
        return mask;
    }
}
