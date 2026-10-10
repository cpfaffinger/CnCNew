# Game rules (Phase 5, completed; additions of Phase 6 in §11)

As of: 30 September 2026 (Phase 6 adds terrain, construction and AI, see §11 and `docs/SIMULATION-CORE.md` §13–§20; the remaining text describes the state of Phase 5, as far as §11 does not replace it). This document describes the game rules that build on the deterministic simulation core
(`docs/SIMULATION-CORE.md`) and that Phase 5 implemented: economy, upgrades, direct combat
with area damage, simple movement, production, the blocker modules from §3/§6/§6a (damage states, fire, poison, death,
stealth detection, garrison containment), the special power subsystem (§6d), the supply economy with docking machine
and spawners (§6e), repair (§6f), experience and veterancy levels (§6h) as well as teams and diplomacy (§7). Phase 5 counts as
completed because supply, experience and teams are in place and the reference match runs on retail data with 0 skipped
start objects (§7a); the deliberate limits are in §10, the follow-up brief was in `docs/handoffs/HANDOFF-PHASE7.md` (Phase 6 is done, the next brief is `docs/handoffs/HANDOFF-PHASE8.md`).

**Unit "tick" in this document (Phase 20):** The tick rate is a value of the world (`docs/SIMULATION-CORE.md` §1, `docs/TICKRATE-MIGRATION.md`). Every tick figure here – "every 15 ticks", "after 300 ticks", "3 ticks" – is a value **at 30 Hz** and means original frames (1/30 s): at 60 and 120 Hz these are twice or four times as many ticks and the same game time. Conversions "milliseconds with 30 ticks per second" mean exactly: rounded up to whole original frames, times rate/30. Names in the code that still appear here as `…Ticks` have ended in `Frames` since Phase 20 where they are a constant in frames (`SaleDurationFrames`, `UpdateRateFrames` …). What behaves differently at 60 Hz is not here, but in `docs/TICKRATE-MIGRATION.md` §5.

**Addendum Phase 23 (9 October 2026):** Since `e8632bb` a session runs at **60 Hz** by default; the rate stays a value of the world, 30 and 120 Hz stay supported. Every tick figure, every victory tick and every hash in this document is therefore **historical, 30 Hz**: as a number of original frames it still holds (at 60 Hz twice as many ticks, the same game time), as a reference value no longer – authoritative is `baselines/reference-manifest.json` (60 Hz). The rules themselves are the same at every rate: above 30 Hz the 30-Hz step is subdivided (decided in the first tick of an object's original frame, the position reached with the last one; `docs/SIMULATION-CORE.md` §43), and the rate-equivalence suite measures this (§39.7 there: 0 quantities outside the tolerance at 60 and 120 Hz). The whereabouts of every line from `docs/TICKRATE-MIGRATION.md` §5 are stated there at the end of the line. Three rule changes of the phase also apply at 30 Hz and are not in the sections below: produced units drive out of their factory on the exit path and every command replaces it (`docs/SIMULATION-CORE.md` §42 – the sentence "the unit is created at the `UnitCreatePoint`" below is incomplete in this respect); a slow death of one frame's duration ends (`docs/DEFECT-LOST-WAKE.md`); units whose destination is occupied take a free place next to it, units boarding reach their vehicle, and the paradrop of the special power drops its soldiers (`docs/AI-OBSERVED-2026-10-08.md` §11, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md` §6).

All rules run inside the existing tick phases (`SIMULATION-CORE.md` §4): new commands are executed in phase 1,
new modules tick in phase 3 like any other update module. There is no additional tick phase.

## 1. Players and economy

- `SimulationWorld` keeps per player a balance (`Fixed`, `Money::withdraw` semantics: `WithdrawMoney` clamps to the
  available balance and returns the amount actually withdrawn) and an incrementally maintained
  energy balance (`PlayerState.EnergyProduction`, sum of `ObjectTemplate.EnergyProduction` of all own objects,
  analogous to `Energy::objectEnteringInfluence`/`objectLeavingInfluence` – updated on spawn/destruction, never recomputed per
  frame). Negative values are net consumers.
- `GiveMoneyCommand` (system only) credits money directly, for scenarios with starting capital and for tests; the income of the
  reference match has come from the supply economy since §6e.
- **Known gap:** energy has no consumption effect yet (e.g. production slowdown on energy shortage,
  `ProductionUpdate::calcTimeToBuild`); the balance is kept, but read nowhere.

### Players, factions and sciences

- `PlayerConfiguration.Faction` names a `PlayerTemplate` (`FactionCatalog`, block 0.1 continued); if it is set,
  `SimulationWorld` writes `StartMoney` and `IntrinsicSciences` into the player state once at construction
  (`Player::Player()`). `StartingBuilding`/`StartingUnit0`–`9` stay names (no resolved indices): a
  reference match spawns them through ordinary `SpawnCommand`s, instead of coupling the faction catalog to the object template
  list.
- `ScienceCatalog` gives every `Science` definition a stable bit index (128-bit `ScienceMask`, structurally like
  `UpgradeMask`, but a separate index space – sciences and upgrades are separate reference domains in the original).
  `PurchaseScienceCommand` ports `Player::attemptToPurchaseScience`/`isCapableOfPurchasingScience` in this
  order: already owned (`AlreadyOwned`) → missing direct prerequisite (`MissingPrerequisite`,
  `Player::hasPrereqsForScience`) → purchase price 0 means **not directly purchasable** (`ScienceNotPurchasable` – the
  original uses the same sentinel value for "grantable only via `grantScience`", e.g. intrinsic faction sciences)
  → too few points (`InsufficientSciencePurchasePoints`). On success the points are deducted and the mask is set.
- **Deliberate simplification:** "Science Purchase Points" are coupled to rank/experience in the original
  (`Player::resetRank`, `RankInfo::m_sciencePurchasePointsGranted`) – a progression system that Phase 5 does not
  model. `GiveSciencePointsCommand` (system only) credits points directly, analogous to `GiveMoneyCommand`.
  `IsGrantable` is typed, but unused (no `grantScience` caller exists apart from the intrinsic
  faction grant, which sets the mask directly instead of checking through `grantScience` — as in the original).
- Teams and diplomacy have existed as a data model since §7; **Known gap:** `CommandSet`/`CommandButton` stay pure presentation
  without simulation effect (`CommandSetUpgrade` is a no-op too).

## 2. Upgrades

- `UpgradeCatalog` (`src/CnCNew.Simulation/Templates/UpgradeCatalog.cs`) assigns every `Upgrade` definition entry
  a stable bit index (128-bit `UpgradeMask`, analogous to the `UpgradeMaskType` of the original). An upgrade is either
  player-bound (`Type = PLAYER`, `Player::addUpgrade`) or object-bound (`Type = OBJECT`,
  `Object::getObjectCompletedUpgradeMask`); the effective key of an object is the union of both masks
  (`SimulationWorld.UpgradeKeyMaskOf`).
- `PurchaseUpgradeCommand` checks ownership, already completed upgrade (`AlreadyOwned`) and balance
  (`InsufficientFunds`), deducts the cost immediately in full (no build progress – unlike production,
  an upgrade is completed instantly in Phase 5 as soon as it is affordable; a build time through the
  production queue is a Phase 6 extension) and calls `CompleteUpgrade`, which sets the mask and re-evaluates every
  `IUpgradeModule` of the affected object (or of all objects of the player for a player-bound upgrade) through
  `TryActivate`.
- **`UpgradeMuxState`** (`src/CnCNew.Simulation/Modules/UpgradeMux.cs`) is the literal port of
  `UpgradeModule.cpp` (`wouldUpgrade`, `attemptUpgrade`, `testUpgradeConditions`, `resetUpgrade`): activation
  requires a non-empty `TriggeredBy` mask in the key (all or any, depending on `RequiresAllTriggers`), no
  set `ConflictsWith` mask and not yet executed; `AttemptUpgrade` first removes the upgrades named in `RemovesUpgrades`,
  then applies the module effect and marks itself as executed.
- **Eleven `*Upgrade` modules**, all checked against the real content (`game/content`):

  | Module | Effect | Source |
  |---|---|---|
  | `ArmorUpgrade` | switches damage resolution to `ArmorSet Conditions=PLAYER_UPGRADE` | `ArmorUpgrade.cpp` |
  | `WeaponSetUpgrade` | switches to `WeaponSet Conditions=PLAYER_UPGRADE`, refills the clip | `WeaponSetUpgrade.cpp` |
  | `WeaponBonusUpgrade` | sets the weapon bonus condition `PLAYER_UPGRADE` (§6h) | `WeaponBonusUpgrade.cpp` |
  | `LocomotorSetUpgrade` | state machine runs, effect deliberately a no-op (only `SET_NORMAL` modelled) | – |
  | `StealthUpgrade` | sets the `CAN_STEALTH` status bit | `StealthUpgrade.cpp` |
  | `MaxHealthUpgrade` | raises `MaxHealth` by `AddMaxHealth`, four `ChangeType` variants (§3) | `MaxHealthUpgrade.cpp`, `ActiveBody.cpp::setMaxHealth` |
  | `RadarUpgrade` | state machine runs, effect deliberately a no-op (no radar/shroud) | `RadarUpgrade.cpp` |
  | `CommandSetUpgrade` | state machine runs, effect deliberately a no-op (UI presentation) | `CommandSetUpgrade.cpp` |
  | `ExperienceScalarUpgrade` | increases the experience scalar of the object (§6h) | `ExperienceScalarUpgrade.cpp` |
  | `ModelConditionUpgrade` | state machine runs, effect deliberately a no-op (pure presentation) | `ModelConditionUpgrade.cpp` |
  | `ObjectCreationUpgrade` | requests the creation list by event (like `CreateObjectDie` in Phase 4) | `ObjectCreationUpgrade.cpp` |

  Every "deliberate no-op" is a module whose trigger/conflict/removal logic runs correctly (other modules that
  depend on the same upgrade name see the right state), but whose effect would concern a subsystem that does not
  exist yet – never an unknown, silently discarded key.

## 3. Weapons, damage, armor

- `WeaponCatalog`/`ArmorCatalog` (`src/CnCNew.Simulation/Templates/WeaponCatalog.cs`) resolve `Weapon` and
  `Armor` definitions into simulation values. Since Phase 8 a shot flies: delayed hit according to `WeaponSpeed` or a projectile object
  (`DumbProjectileBehavior`/`MissileAIUpdate`, §6i); area damage (`PrimaryDamageRadius`, `SecondaryDamage`, `RadiusDamageAffects`) has existed since §6g, the
  `WeaponBonus` multipliers since §6h.
- `ArmorInfo.Adjust` ports `ArmorTemplate::adjustDamage`: `damage *= coefficient[damage type]`, except for
  `DAMAGE_UNRESISTABLE`/`DAMAGE_SUBDUAL_UNRESISTABLE` (bypass the armor completely), result never negative.
- **`ArmorSet`/`WeaponSet` selection:** The original selects through a `SparseMatchFinder` the set that best matches the current
  conditions (veterancy level, upgrades, second life, crate upgrades, …). Phase 5 modelled only two
  conditions: `Conditions = None` (default) and `Conditions = PLAYER_UPGRADE`. Since Phase 10 `VETERAN`/`ELITE`/`HERO` are added as further
  conditions, each standing on its own (`ObjectTemplate.VeteranArmor`/`VeteranWeapon`, selected by `Entity.VeterancyLevel` in
  `SimulationWorld.ActiveArmor`/`ActiveWeaponIndex`); the combination of several simultaneous conditions stays unmodelled (a set that
  e.g. requires `PLAYER_UPGRADE` AND `VETERAN` at once) – checked against `game/content`: **not a single `ArmorSet`/`WeaponSet` of the
  retail Zero Hour data combines a condition with the veterancy level**, so the restriction has no effect on the real content.
  The veterancy level previously acted only through weapon bonus and health (§6h), now additionally through the set selection itself.
- **`FireWeaponCommand`:** direct, explicit shot (no automatic target search, no line of sight). Rejections:
  `NoWeapon` (no `DefaultWeapon`/`PlayerUpgradeWeapon`), `WeaponNotReady` (tick < `WeaponReadyTick` – reload time
  or pause between shots), `OutOfRange` (squared distance > squared `AttackRange`, so that no root is needed).
  Clip: `Entity.WeaponClip` starts at spawn (or at `WeaponSetUpgrade`) with `ClipSize` (`ClipSize = 0` means
  unlimited, encoded as `int.MaxValue`); after every shot it drops by one, at 0 `ClipReloadTime` begins, otherwise
  `DelayBetweenShots` applies. A hit calls `SimulationWorld.Damage` with the armor-adjusted amount,
  `DamageType`/`DeathType` of the weapon.
- **`MaxHealthUpgrade` calculation rules** (`ActiveBody.cpp::setMaxHealth`, all four cases ported):

  | `ChangeType` | Effect on the current health |
  |---|---|
  | `SAME_CURRENTHEALTH` | unchanged, unless it exceeds the new upper limit |
  | `PRESERVE_RATIO` | `new = old · (newLimit / oldLimit)` (ratio stays the same) |
  | `ADD_CURRENT_HEALTH_TOO` | rises by the same amount as the upper limit |
  | `FULLY_HEAL` | is set to the new upper limit |

- **Fear signal (block 0.3):** `ActiveBody::attemptDamage`'s random consumption is ported – on the transition from above
  25 % to below 25 % health (and still alive) the simulation draws `GameLogicRandomValue(0,99)`; at < 25 a
  `VoiceFearRequested` event is raised (pure presentation request, playing the sound is up to Phase 7). The
  draw itself always happens when the threshold is crossed, independent of the result, so that the random stream stays
  in sync with the original (test: `Tests.Phase5FearSoundRng`, compares the raw state of the `logic` stream before/after).
- Experience and veterancy levels from kills have existed since §6h. **Known gaps:** no `IS_FIRING_WEAPON`/retaliate logic; projectiles do not collide with objects they fly past (§6i).

### Damage states, fire, poison and death (the next blocker modules)

- **Damage states** (`ActiveBody.CalcDamageState`): `PRISTINE`/`DAMAGED`/`REALLYDAMAGED`/`RUBBLE` according to the
  health ratio against `GameData.ini`'s `UnitDamagedThreshold`/`UnitReallyDamagedThreshold` (retail default
  0.7/0.35 without a `GameData` block). `TransitionDamageFX` requests, on the change into a **worse** state, the
  matching `DamagedOCL`/`ReallyDamagedOCL`/`RubbleOCL` (`CreationListRequested` event), but only with a resolvable
  damage source (`DamageInfo.Source` valid) – the associated FX lists and particle systems are pure presentation.
- **`FlammableUpdate`:** flame damage (`DAMAGE_FLAME`/`DAMAGE_PARTICLE_BEAM`) accumulates against `FlameDamageLimit`
  (expires after `FlameDamageExpiration` without a new hit); once the limit is reached, it sets `AFLAME`, damages
  periodically (`AflameDamageDelay`/`AflameDamageAmount`), becomes permanently `BURNED` after `BurnedDelay` and extinguishes itself
  after `AflameDuration` (unless it is already `BURNED` – then it stays out, but cannot be ignited again).
- **`PoisonedBehavior`:** a `DAMAGE_POISON` hit remembers the damage actually dealt
  (`IDamageModule.OnDamage`'s `actualDamageDealt`) and applies it again every `PoisonDamageInterval` (as
  `DAMAGE_UNRESISTABLE`, not `DAMAGE_POISON` again – otherwise every tick would restart the poisoning), until
  `PoisonDuration` has elapsed. A renewed poisoning during the cycle extends the duration, but moves the
  next tick only forward, never backward. Healing (`IDamageModule.OnHealing`, new: fires only when the
  health has actually risen, literal to `ActiveBody::attemptHealing`) heals the poisoning completely.
- **`SlowDeathBehavior`:** on the matching death all applicable sibling modules (up to seven per object in
  real data) enter a weighted lottery (`ProbabilityModifier`, at least 1); only the winning module schedules the
  delayed destruction (`DestructionDelay`/`-Variance`). `Entity.SlowDeathStarted` generalises the
  original lock (which presupposes an `AIUpdateInterface` there) to every entity. Intentionally not modelled:
  overkill bonus (needs the actual damage of the killing blow, not yet passed through `OnDie`), physics
  when being flung away, LOD time scaling, the 3-phase random effect lists.
- **`FireWeaponWhenDeadBehavior`:** fires `DeathWeapon` on the matching death, once active (`StartsActive` or a
  matching `TriggeredBy` upgrade). Unique among the upgrade modules: `ConflictsWith` is checked once more on death,
  because the `StartsActive` path never ran through the activation conflict check (this is how
  ChemicalGeneral's mutually exclusive explosive upgrades work: the default starts active, a later
  conflict upgrade silences it). The firing itself only raises a `WeaponFireRequested` event – the radius damage of §6g
  is not yet executed for this death shot (known gap).

## 4. Simple movement

- `SetMoveTargetCommand` sets `Entity.HasMoveTarget`/`MoveTargetX`/`MoveTargetY`; only objects with `AIUpdateInterface`
  accept the command (otherwise `CannotMove`).
- `AIUpdateInterface` (`src/CnCNew.Simulation/Modules/BuiltIn/Phase5Modules.cs`) moves in a straight line to the target:
  remaining distance and "close enough" (`max(CloseEnoughDist, current speed)`, so that no object overshoots the target
  forever) are checked without taking a root through squared distances; for the actual normalisation when
  moving `Fixed.Sqrt` is available – a deterministic integer Newton iteration (no `Math.Sqrt`, no
  floating-point number, see `Numerics/Fixed.cs`), the speed approaches the target speed through
  `Acceleration`/`Braking` (braking setting in as soon as the remaining distance falls below the braking distance `v²/(2·Braking)`),
  and a tick never moves further than to the target.
- **Deliberate simplification:** no turn-rate-limited orientation (`Entity.Angle` stays unchanged when moving;
  `LocomotorInfo.TurnRatePerTick` is compiled, but not applied), no terrain, no collision, no
  pathfinding – that was Phase 5; **since Phase 6** navigation applies to units with `AIUpdateInterface` and a map (`SIMULATION-CORE.md` §14: A*, turn rate, terrain height), the straight-line movement stays for worlds without terrain. Without turn rate/trigonometry the movement stays completely integer/fixed-point based
  and thus platform-independently deterministic (compare the rationale for `Fixed` in `SIMULATION-CORE.md` §1).

## 5. Production and construction

- `QueueProductionCommand` enqueues a template in the queue of an object with `ProductionUpdate`: costs
  are deducted **immediately in full** (`ProductionUpdate.h` semantics, like `Money::withdraw`), rejections are
  `CannotProduce` (no `ProductionUpdate` module), `QueueFull` (`MaxQueueEntries` reached) and `InsufficientFunds`.
  `CancelProductionCommand` removes a slot and refunds the costs in full.
- `ProductionUpdate::update()`: only the head of the queue counts one tick less `RemainingTicks` per tick
  (`BuildTime` in seconds, rounded up to whole ticks); on reaching 0 the unit is created at the `UnitCreatePoint` of the
  exit module in the ownership of the producer's player, has received its exit path since 8 October 2026
  (`docs/SIMULATION-CORE.md` §42), and the next queue slot starts anew.
- **Deliberate simplification:** no energy slowdown factor (`ProductionUpdate::calcTimeToBuild`), no
  collision check (build site check and rally point have existed for buildings since Phase 6, §11), `QuantityModifier` and the door animation fields are typed, but
  unused (see module catalog comment).

## 6. Stealth and self-healing (block 0.2)

- `StealthUpdate` cloaks an object (`STEALTHED` status bit) as soon as none of its
  `StealthForbiddenConditions` applied for `StealthDelay` ticks; every violation breaks the stealth immediately and resets the countdown
  (literal to the original). Phase 5 mechanically checks `ATTACKING`, `MOVING` (`Speed` against `MoveThresholdSpeed`),
  `USING_ABILITY`, `FIRING_PRIMARY`/`_SECONDARY`/`_TERTIARY` (`LastFireTick == current tick`) and `TAKING_DAMAGE`
  (`LastDamageTick == current tick`); `NO_BLACK_MARKET` and `RIDERS_ATTACKING` are compiled, but never true
  (no black market/rider system). The set status bit has no consumer yet: Phase 5 has neither
  automatic target acquisition nor line of sight that could evaluate it.
- `AutoHealBehavior` periodically (`HealingDelay`) heals all matching objects of the same player: `KindOf`/
  `ForbiddenKindOf` filter, `Radius` or (`AffectsWholePlayer`) without distance check, `SkipSelfForHealing`. The
  ability starts active (`StartsActive`) or is unlocked by an upgrade (`IUpgradeModule`, the same
  `UpgradeMuxState` as the `*Upgrade` modules).
- `StealthDetectorUpdate` periodically (`DetectionRate`) searches all other objects for a currently `STEALTHED`,
  hostile object matching the `KindOf` filter within `DetectionRange` (default: `VisionRange` of its own
  template) and marks it `DETECTED` (`StealthUpdate.MarkDetected`: sets the bit, extends, never shortens, its
  expiry time). `DETECTED` exists next to `STEALTHED` (reveals it without cancelling the stealth itself) and expires by
  itself as soon as nothing marks it again. **Relationship check:** since §7 through the relationship model (only enemies
  are marked); without
  a garrison/transport system (`CanDetectWhileGarrisoned`/`CanDetectWhileContained` are typed, but without effect,
  an object is never "contained").

### 6a. Containment (garrison)

The largest single blocker by measured unlock gain (167 of 200 objects with `GarrisonContain` depended only
on this module any more, see `docs/handoffs/HANDOFF-PHASE5.md`). New base infrastructure, not just another module:
`Entity.ContainedBy`/`Entity.Container` (one container per entity, intentionally kept tight – `IContainerModule` is
deliberately narrow until a second implementer, `TransportContain`, actually extends it) as well as
`EnterContainerCommand`/`ExitContainerCommand` (`CommandTypes.Version` 3 → 4).

- **Entry/exit:** immediate, without pathfinding/distance check (the same "immediate, no real spatial check"
  simplification as economy/production). Entry requires `IsValidContainerFor`; exit can be ordered by the owner of the
  passenger unit or by the owner of the container.
- **`GarrisonContain`:** `ContainMax` (capacity), `AllowInsideKindOf`/`ForbidInsideKindOf` (default: only `INFANTRY`,
  `GarrisonContainModuleData`'s own, narrower default compared to `OpenContain`'s generic "everything allowed"),
  `AllowAlliesInside`/`AllowEnemiesInside` (both default `Yes`; since §7 checked by the relationship of the applicant's owner to the
  owner of the container, a neutral applicant counts neither as an ally nor as an enemy). A building in `REALLYDAMAGED` can no longer be entered and, on the transition **into**
  this state, immediately ejects all occupants (both do not apply with `KindOf = GARRISONABLE_UNTIL_DESTROYED`).
- **Healing** (`HealObjects`): a sliver `MaxHealth / TimeForFullHeal` per tick, from the full dwell time on a
  hard jump to full health (`GarrisonContain::healSingleObject`).
- **Death of the container:** `DamagePercentToUnits` is applied to the current health of every occupant, then
  survivors are ejected (not killed – `OpenContain::onDie` calls `removeAllContained`, never
  `killAllContained`, as long as `killRidersWhoAreNotFreeToExit` stays with the base behaviour, as with `GarrisonContain`).
- **Deliberately not modelled:** `InitialRoster` (starting crew – would need synchronous nested creation at
  construction, a larger change to the spawn pipeline than this first cut justifies), firing out of the
  container, garrison points/positions (art-derived, no INI data), the
  apparent-controlling-player swap system, capture handling (no capture system).
- **`TransportContain`** (the second implementer, which justified `ContainModuleBase` – shared
  gating/entry/exit/death logic, see the class comments): capacity is **weighted**
  (`ObjectTemplate.TransportSlotCount`, not simply counted) – an object with 0 slots (the default) is never
  transportable, and the sum of the slot costs of all occupants plus the applicant must not exceed `Slots`.
  `HealthRegen%PerSec` heals continuously (`MaxHealth · percent / 100` per second), not ramp-like as
  `GarrisonContain`'s `HealObjects`. No `REALLYDAMAGED` special case (the reference has none for it either). Deliberately
  not modelled: `InitialPayload`, firing out of the container, exit paths/delay/scatter (presentation
  or need real movement/positioning), capture handling.

## 6b. Creation lists and crates

**`ObjectCreationList`** (295 definitions, strictly typed in `CnCNew.Data`: `ObjectCreationListDefinition.Sections`; the
nugget counts in the content: `CreateObject` 184, `CreateDebris` 473, `DeliverPayload` 46, `ApplyRandomForce` 10, `FireWeapon` 4,
`Attack` 1) is now executed, no longer just reported as a `CreationListRequested` event.
`SimulationWorld.CreateFromList(source, list, requester, transferPreviousHealth)` corresponds to
`ObjectCreationList::create` and is called by `CreateObjectDie` and `InstantDeathBehavior`. The event remains as a
log. The catalog (`ObjectCreationListCatalog`) binds every list when the templates are built; only an unknown
nugget type is a `TemplateBuildException`, everything else is reported at runtime as a `CreationSkipped` event with a reason.

- **`CreateObject`** (`GenericObjectCreationNugget::create/reallyCreate/doStuffToObj`): Per `Count` one object is chosen from
  `ObjectNames` (one `Range(0, n−1)` draw per instance), then – in this order – minimum distance and
  start angle with `SpreadFormation`, then health fraction (`Range(MinHealth, MaxHealth)`) and with `OnGroundAligned`/
  `SendItOut` an orientation draw. `Offset` is rotated by the source orientation; `Disposition` `LIKE_EXISTING` takes over
  the orientation. `PutInContainer` creates the container first (`newObject` in the original), but positions it only after
  the content. `ContainInsideSourceObject` puts the new object into the source, `InheritsVeterancy` transfers the rank
  (only to trainable templates), `MinLifetime`/`MaxLifetime` override `LifetimeUpdate` without an additional draw.
  `SkipIfSignificantlyAirborne` checks `Thing::isSignificantlyAboveTerrain` (`height > −9·Gravity`, Gravity now typed from
  `GameData`; terrain is flat at Z = 0).
- **Deferred creation:** spawns continue to run through the queue (`ProcessSpawns` after `ProcessDestroys`). Everything
  that the original sets on the object after `newObject` travels along as `SpawnSetup` and is applied in `SpawnEntity`:
  starting health (`ActiveBody::setInitialHealth`), rank, lifetime, container, occupant of the source and
  `TransferPreviousHealth` (`CreateObjectDie`: unresistable damage `MaxHealth − PreviousHealth`). A `RequestDestroy`
  triggered in `SpawnEntity` therefore takes effect only in the following tick. The spawn containment reports `EntityContained`.
- **Random order:** All draws take place at list time (the health and orientation draws of the original lie
  after `newObject`, here after the selection, but before the actual creation). This is the only deliberate deviation in the
  draw order; the golden master tests follow it. `InstantDeathBehavior` draws exactly one
  list per module, like the original.
- **Not simulated, but never silently discarded:** `CreateDebris`, `ApplyRandomForce`, `FireWeapon`, `Attack` and
  `DeliverPayload` produce `CreationSkipped` (reason `NuggetNotSimulated`), their random draws (force, rotation, physics) are
  not consumed; `InvulnerableTime` and `DiesOnBadLand` appear as `SettingNotSimulated`. Further reasons:
  `UnknownList`, `UnknownTemplate`, `TemplateNotInstantiable`, `ContainerNotInstantiable`, `NoObjectNamed`. This makes visible
  which lists of the content become complete only with physics (Phase 6).
- **Position finding:** `PartitionManager::findPositionAround` without terrain and obstacles chooses the innermost ring: one
  start angle draw, distance `MinDistance`; if `MaxDistance < MinDistance`, the formation position is skipped. Sine and
  cosine are supplied by `FixedTrig` (Taylor series, error below 2·10⁻⁵, `Cos(0)` exactly 1) instead of the binary32 functions of the original.
- **`RequiresLivePlayer`** checks `IsPlayerDefeated` of the owner; because the source itself counts, the branch is practically
  never active (likewise in the original) and therefore untested. Objects that are skipped as not instantiable consume
  no `doStuff` draws.
- **`LifetimeUpdate`** draws once (the original draws again after `setLifetimeRange`).
- **Fixtures:** The fixture compilation is strictly reference-checked, an unknown object name in a creation list cannot be
  represented there; `UnknownTemplate` can only be checked with real data.

**`CreateCrateDie`** (`CreateCrateDie.cpp onDie`, `CrateSystem.cpp`; catalog `CrateCatalog`, 9 `CrateData` definitions):
After the death filter (`DieMux`) a kill by the same owner is not rewarded, as "ALLIES". Per `CrateData` there follows
a `Range(0,1)` draw against `CreationChance`, then `VeterancyLevel`, `KilledByType` (all kinds must be present) and
`KillerScience`; only when all conditions are met does a further draw choose a `CrateObject` by weight
(sum < draw → no crate, as in the original), a last one draws the orientation. For lack of terrain the crate appears at the location
of the victim (`findPositionAround` is Phase 6); the owner is the causer (`OwnedByMaker`) or neutral. All crate objects
of the content use `*CrateCollide` modules, which are not implemented: the request is always reported as `CrateRequested`,
but the object is only created if it is instantiable. "Ally" has followed the relationship model since §7; the
AI hook `notifyCrate` is missing.

## 6c. Modules for the retail match

Fourteen modules unlock retail objects that the reference match needs. All are strictly typed, listed in the catalog with a
schema and checked in `Tests.RetailModules.cs`; update modules save their wake time
(`nextWake`, enforced by the `SourceRules` test), because otherwise `SimulationWorld.Register` starts a new
interval after a restore and the snapshot continuation diverges.

| Module | Reference | Behaviour |
| --- | --- | --- |
| `BaseRegenerateUpdate` | `BaseRegenerateUpdate.cpp` | Structures heal `BaseRegenHealthPercentPerSecond` (`GameData`) of their maximum health per second, once they have been undamaged for `BaseRegenDelay` (burst of three ticks every three ticks as in the original; sleeps at full health, ends on `SOLD`). |
| `PhysicsBehavior` | `PhysicsUpdate.cpp` | Rest state of the original: reacts only to forces and terrain contact, sleeps forever. Neither exists yet (Phase 6); the module never moves an object. |
| `SquishCollide` | `SquishCollide.cpp` | Without collision detection `onCollide` is never called; stateless. |
| `GenerateMinefieldBehavior` | `GenerateMinefieldBehavior.cpp` | Lays minefields on death/upgrade (`placeMines`, `SmartBorder`, circular) with the draw order of the logic stream; terrain veto, `setScootParms` and rectangle borders are reported as `CreationSkipped`. |
| `DefaultProductionExitUpdate`, `SupplyCenterProductionExitUpdate` | `.cpp` of the same name | Exit always free; no gate, no rally point, no stealth grant (`GrantTemporaryStealth`). |
| `QueueProductionExitUpdate` | `QueueProductionExitUpdate.cpp` | Burst exit: `InitialBurst` units in a row, then `ExitDelay` lock time; the producer holds back finished units until the exit is free. |
| `HealContain` | `HealContain.cpp` | Occupants heal `MaxHealth / TimeForFullHeal` per tick, then released fully healed (placed at the container without AI). |
| `EjectPilotDie` | `EjectPilotDie.cpp` | Creates the ground or air list on death (`IsSignificantlyAboveTerrain`); `InvulnerableTime` is not modelled. |
| `VeterancyGainCreate` | `VeterancyGainCreate.cpp` | Sets `StartingLevel` on creation if the required science is present. The original stores it as a minimum level; without an experience system it is set once. |
| `PreorderCreate` | `PreorderCreate.cpp` | Shows only a preorder decoration; inert placeholder without keys. |
| `RadarUpdate` | `RadarUpdate.cpp` | Passive; `RadarUpgrade` starts the extend animation (`RadarExtendTime`). There is no radar/no minimap in the simulation. |
| `RebuildHoleExposeDie` | `RebuildHoleExposeDie.cpp` | Dying GLA structures leave a hole (owner, position, orientation, `HoleMaxHealth`); not for neutral ones or ones under construction. Omitted: `isPlayerActive`, geometry takeover, `TransferAttackers`. |
| `RebuildHoleBehavior` | `RebuildHoleBehavior.cpp` | The hole regenerates `HoleHealthRegen%PerSecond`; after `WorkerRespawnDelay` a worker would rebuild – worker and build order are AI (Phase 6), therefore `CreationSkipped` and end of the countdown. |

`HealContain` belongs to the containers (§6a); `ProductionUpdate` queries the exit through the interface
`IProductionExit`. `ModuleFramework.cs` additionally knows `ISpawnOriginReceiver` (hole origin) and the
world services `IsSignificantlyAboveTerrain` and `BaseRegen*`.

**Not modelled (deliberately, not silently):** rally points and `UseSpawnRallyPoint`, `GrantTemporaryStealth`, hole geometry,
`TransferAttackers`, worker/rebuild AI, permanent minimum veterancy level, radar system, preorders,
`InvulnerableTime` of `EjectPilotDie`.

## 6d. Special powers

Eleven modules and one command form the special power subsystem. It unlocks the **real command centers of all 13
factions** (previously barracks as a substitute, §7a). Reference: `SpecialPower.cpp` (`SpecialPowerTemplate`, field table),
`SpecialPowerModule.cpp`, `OCLSpecialPower.cpp`, `CashBountyPower.cpp`, `CashHackSpecialPower.cpp`,
`SpectreGunshipDeploymentUpdate.cpp`, `SpecialAbilityUpdate.cpp`, `Player.cpp` (`addScience`, `grantScience`,
`getOrStartSpecialPowerReadyFrame`, `expressSpecialPowerReadyFrame`), `ActionManager.cpp` (`canDoSpecialPower`) and
`Object.cpp` (`scoreTheKill`).

**Definition and catalog.** `SpecialPower` is strictly typed (`SpecialPowerSchema`): `ReloadTime`, `RequiredScience`
(reference to `Science`), `PublicTimer`, `Enum` (name table `SpecialPowerType`, 67 entries), `SharedSyncedTimer`,
`DetectionTime`, `ViewObject*`, `RadiusCursorRadius`, `ShortcutPower`; `InitiateSound`, `InitiateAtLocationSound` and
`AcademyClassify` are presentation or statistics and are listed as `deferred-field` in the compatibility baseline (79
uses). The `SpecialPowerCatalog` of the simulation assigns dense indices (data order) and converts `ReloadTime` into
ticks; without `ReloadTime` the default of the original applies (1 s = 30 ticks, 3 of 80 definitions, explicitly counted).
Content: 80 definitions, 36 of them with `SharedSyncedTimer`.

**Readiness machine** (`SpecialPowerModuleBase`, ported as written): `availableOnFrame`, `pausedCount`,
`pausedOnFrame`. A power is ready when it is not paused and `Tick >= availableOnFrame` holds; a command in tick
N therefore sees the readiness of tick N (a carrier created at tick 0 with 300 ticks reload time is ready at tick 300,
not earlier). On creation (`OnSpawn`, not under construction) the reload starts, `StartsPaused` additionally pauses. A
`SharedSyncedTimer` power ignores its own pause and reads the shared ready frame of the player
(`PlayerState.SharedReadyFrames`, sorted, part of snapshot and hash); a timer that does not exist yet starts on the first
access. `SpecialPowerCreate` (at the end of construction, in the tick after the spawn) reports the power as "created"
(`onSpecialPowerCreation`: restart the reload, shared power ready immediately, pause `StartsPaused` again – the
double pause with `StartsPaused` + `SpecialPowerCreate` is ported as in the original and is lifted individually by
`UnpauseSpecialPowerUpgrade`).

**Command.** `UseSpecialPowerCommand` (code 17, `CommandTypes.Version = 5`, encoded in the replay): source object, power name,
target kind (`None`, `Object`, `Location`), target object, coordinates. The check (`CheckSpecialPower`, same rules at submission
and execution, because the readiness changes in between) rejects explicitly: `UnknownEntity`, `NotPermitted`,
`UnknownSpecialPower` (25; power unknown or the object carries no module for it), `MissingScience` (26),
`SpecialPowerNotReady` (27; paused, reloading or under construction), `InvalidSpecialPowerTarget` (28; target kind does not match or the
target object does not exist) and `SpecialPowerHandlerMissing` (29; `UpdateModuleStartsAttack` requires a handler in the
update module, which is missing – the power is refused instead of silently doing nothing). When executed, the command emits
`SpecialPowerUsed`, calls the first matching handler (`ISpecialPowerHandler`, module order), starts the reload
(unless `StartsAttack`) and applies the power-specific effect.

| Module | Behaviour |
| --- | --- |
| `SpecialAbility` | Pure power whose effect is carried by a handler (unit abilities, gunships). Without a handler and with `UpdateModuleStartsAttack` it is rejected (29). |
| `OCLSpecialPower` | Creates an `ObjectCreationList` (§6b): the first `UpgradeOCL` whose science the owner has, otherwise `OCL`. `CREATE_AT_LOCATION`/`USE_OWNER_OBJECT` create at the target (without a target at the source object), `CREATE_ABOVE_LOCATION` 300 units above it (`ON_GROUND_ALIGNED` sets z back to 0, `LIKE_EXISTING` keeps it). The three map-edge locations need terrain: the power triggers and reloads, the missing delivery reports `CreationSkipped(NoTerrainEdge)`. |
| `CashBountyPower` | Raises the bounty rate of the owner to `Bounty` (only if he has the science of the power, on appearance and on science acquisition). Payout: `ScoreTheKill` (`Object::scoreTheKill`/`Player::doBountyForKill`) pays the owner of the killer `ceil(BuildCost · rate)` for the kill of a hostile object (§7; not `IGNORED_IN_GUI`, not under construction, no system player). |
| `CashHackSpecialPower` | Steals `min(amount, balance of the victim)` from the owner of the target object; amount = first `UpgradeMoneyAmount` whose science the thief has, otherwise `MoneyAmount`. A location target does nothing, as in the original. |
| `SpecialPowerCreate` | Reports the powers of the object as created (see above). |
| `GrantUpgradeCreate` | Completes the named upgrade (player- or object-bound) once, when the object is finished. |
| `GrantScienceUpgrade` | Grants a science (`Player::grantScience`; a science that cannot be granted, `IsGrantable = No`, is refused). |
| `UnpauseSpecialPowerUpgrade` | Lifts a pause of the named power on the object. |
| `SpecialPowerCompletionDie` | Reports the end of the carrier to the script system; scripts do not exist, the module is explicitly inert. |
| `SpectreGunshipDeploymentUpdate` | Handler of the gunship power (first hit by module order, `RequiredScience`). The gunship is a flying unit with its own AI that flies in from the map edge; both are missing, therefore `CreationSkipped(NoTerrainEdge)` and only the reload start of the carrier. |
| `SpecialAbilityUpdate` | Like `SpecialAbility`: a pure power without an effect of its own here (Phase 10 shell); **the two capture powers (`SPECIAL_INFANTRY_CAPTURE_BUILDING`, `SPECIAL_BLACKLOTUS_CAPTURE_BUILDING`) have been staged since then (`docs/SIM-STAGED-CAPTURE.md`)**, the other abilities stay a shell. In the original this module carries the command-button-triggered unit abilities (Ranger grenades, Colonel Burton's explosive charge, Jarmen Kell's sniper shot, the GLA Rebel's building capture/trap laying, the Hacker's money theft) with its own approach/packing/preparation/ability-object state machine (`SpecialAbilityUpdate.cpp`); none of these phases is simulated. The module keeps only the shared reload mechanism, so that the object is instantiable (`docs/SIMULATION-CORE.md` §11) and `NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` can trigger the power and let it reload (`docs/SCRIPT-ENGINE.md` §5/§8). |

**Science granting** (`SimulationWorld.AddScience`, common to purchase and `GrantScienceUpgrade`): after the entry is made,
all powers of the player's objects that require this science are woken (`onSpecialPowerCreation` and ready immediately,
`Player::addScience`).

**Not modelled (deliberately, not silently):** no construction process (since Phase 6 it exists for dozer buildings, §11; every object without `UnderConstruction` is finished in the tick after the spawn,
`onBuildComplete`), no disabled state (`isDisabled` check), `createOwner = false` (the owner change of
`CreateFromList`), `OCLAdjustPositionToPassable`, gunship AI and edge fly-in, view objects (`ViewObject*`), EVA announcements,
sounds, public timers and script notifications. The
reference match generation uses no special powers (the command generator produces no `UseSpecialPowerCommand`); the modules
run along on the real command centers (reload clocks, science wake-ups), their effect is verified by `Tests.SpecialPower.cs`
(fixture with nine powers: rejections, reload, OCL origins, science switching, pause/unpause, shared timers,
bounty/cash hack amounts, snapshot continuation, replay round trip of the command; real-data check of the 80 definitions).

## 6e. Supply economy, docking machine and spawners

The economy replaces the stopgap `GiveMoneyCommand`: supply centers, warehouses, trucks and workers run on the
real definitions. Reference: `DockUpdate.cpp`, `AIDock.cpp` (`AIDockMachine`), `StateMachine.cpp`
(`internalSetState`, `friend_checkForTransitions`), `SupplyCenterDockUpdate.cpp`, `SupplyWarehouseDockUpdate.cpp`,
`SupplyCenterCreate.cpp`, `SupplyWarehouseCreate.cpp`, `AutoDepositUpdate.cpp`, `SupplyTruckAIUpdate.cpp`, `WorkerAIUpdate.cpp`,
`ResourceGatheringManager.cpp`, `SpawnBehavior.cpp`. The schemas of the nine modules are strictly typed (catalog, `ModuleCatalog.cs`);
`ValuePerSupplyBox` (`GameData`) is typed as an integer (default of the original 100 without an entry, explicitly counted).

**Docking machine** (`DockMachine.cs`, `AIDockMachine`). States as in the original (Approach, Enter, Dock, Exit, Rally); the
transition semantics of `StateMachine` are ported: a state that reports success or failure immediately on entering continues in the same
call (up to 20 levels). The docking positions carry no geometry (no bones, no terrain): entry, dock, exit and
rally point lie at the position of the docker, so these four states report success on entering; only the approach position is
a real movement. The machine lock of the original during the dock movement is dropped (nothing sets a state from outside).

**`DockUpdate`** (base of the docking modules): approach slots (`NumberApproachPositions`, dynamic at −1 or `HasApproachPositions =
No`), clearance (`isClearToApproach`), slot reservation and advancing (`reserveApproachPosition`, `advanceApproachPosition`) and the
one active docker. Without bone geometry every approach point lies in the centre of the dock, shifted by `majorRadius / 2` towards the docker
(the boneless branch of `computeApproachPosition`, without `findPositionAround`). `update()` makes the owner of the first
slot reached the active docker, as long as none is active. Slots and dockers whose entity has died are
released (the original relies on the docker's machine doing that itself). The dock sleeps as long as no slot
and no docker is occupied, and is woken by the first reservation.

| Module | Behaviour |
| --- | --- |
| `SupplyCenterDockUpdate` | The docker unloads all boxes at `ValuePerSupplyBox` each (`Player::getSupplyBoxValue`) plus its upgrade bonus at once; the sum goes to the controlling player of the center (`SupplyDeposited` event with amount). Not modelled: `GrantTemporaryStealth`, score counter, floating text. |
| `SupplyWarehouseDockUpdate` | Hands out one box per action as long as there is stock; a `DeleteWhenEmpty` warehouse disappears with the last box. The proximity check (the docker randomly twitches closer) is dropped; `setDockCrippled` (power "cripple warehouse") is not modelled. |
| `SupplyCenterCreate`, `SupplyWarehouseCreate` | Register center or warehouse with the `ResourceGatheringManager`; here the network is derived from the entities, so nothing remains to do (deliberately stateless). |
| `AutoDepositUpdate` | Every `DepositTiming` ticks the owner receives `DepositAmount` plus the first bonus whose upgrade he owns. Neutral objects and objects under construction earn nothing (the counter keeps running). `InitialCaptureBonus` is typed, but without effect (no capture). |
| `SupplyTruckAIUpdate` | `SupplyTruckStateMachine` (waiting, gathering, delivering, retreating), transition semantics like the docking machine. The supply machine runs before the base AI, the sleep is the shorter of both values; an idle truck sleeps until a command or a creation hook wakes it. Without upgrade bonus. The `forcedBusy` of the original (set by the stop command) is dropped. |
| `WorkerAIUpdate` | Like the truck, with a bonus after `Upgrade_GLAWorkerShoes`; the supply brain keeps running, building happens through the build commands of the world (§11). |
| `DozerAIUpdate` | Movement; since Phase 6 dozers build, repair and sell through `ConstructCommand`, `RepairCommand`, `ResumeConstructCommand` (§11). Mines (the dozer's own machine) are still missing. |

**Supply network** (`ResourceGatheringManager`): every warehouse and every center is visible to every player. The truck chooses by
squared 3D distance; candidates are scanned in entity ID order, so that ties break deterministically (the original
uses the registration order). The remembered dock of a player command wins unconditionally (`findBestSupplyWarehouse`,
`findBestSupplyCenter`); otherwise the nearest warehouse within the search distance counts, for the center without a distance limit. After the delivery
the truck runs back if needed (`SupplyTruckRegroupingState`: nearest own money generator, otherwise command center, otherwise
any building).

**`DockCommand`** (code 18, `CommandTypes.Version = 6`): the player sends a docker to a dock (`aiDock`); the target is remembered as
the preferred dock (one memory for warehouse and center). Rejection `CannotDock` (30): the target has no dock module or the
actor no AI.

**`SpawnBehavior`** (`SpawnBehavior.cpp`; schema with all fields typed): a creator that keeps `SpawnNumber` objects alive and
runs through its template list in turn. It works every `SPAWN_UPDATE_RATE` (15) ticks, starting with the first update; every
spawn leaves the object through its `IProductionExit` (without an exit module nothing is created, reported once as `NoExitInterface`).
`SpawnReplaceDelay` delays the replacement; `OneShot` and `SpawnedRequireSpawner` are ported. Deviations (all deliberate):
the death of a spawn is polled in the 15-tick rhythm instead of through `onSpawnDeath` (replacement `SpawnReplaceDelay` after the finding); the
initial burst does not stagger (the original computes a birth frame and never uses it); spawns are not bound to a creator ID or
a `SlavedUpdate` (the family is missing); a template that is not instantiable is reported as `CreationSkipped`, consumes
its slot and is never retried. `AggregateHealth`, `ExitByBudding`, `CanReclaimOrphans` (without `OneShot`) and
`PropagateDamageTypesToSlavesWhenExisting` need the slaved family (mobs, breeding sites): an object that requires them keeps the
module in `MissingModules`, instead of being simulated wrongly. `SlavesHaveFreeWill` is read, but without effect.

**Reference match** (`ReferenceMatchScenario`): as a substitute for construction every base places a supply center and a truck (or
worker) of its own side as well as a neutral warehouse (`SpawnSupplyInfrastructure`); in tick 1 the generator sends every carrier by
`DockCommand` to the nearest warehouse, after that the carrier machine runs by itself. A side without one of the three elements is noted in
`SupplyNotes`. The generator no longer writes a `GiveMoneyCommand`. `SupplyDeliveries` and
`SupplyDeliveryMoney` are counted.

**Evidence** (`Tests.Supply.cs`): golden master with independent recalculation (approach, load, delivery, amount), queue
of several carriers at one warehouse, command check, snapshot and replay continuation, the `SpawnBehavior` variants and real-data check; the
real match requires zero skipped start objects and deliveries.

**Not modelled (deliberately, not silently):** geometry (bones, terrain, paths), `GrantTemporaryStealth`, construction/dozer/repair by
workers, warehouse crippling, capture bonus, slaved/mob spawners.

## 6f. Repair and healing

Reference: `RepairDockUpdate.cpp` (`action`), `AutoFindHealingUpdate.cpp` (`update`, `scanClosestTarget`), `ActionManager.cpp`
(`canGetHealedAt`), `AIUpdate.cpp` (`privateGetHealed`).

- **`RepairDockUpdate`** (repair dock): the first action for a new docker fixes the amount per tick as
  `(Max − health) / framesForFullHeal`; every action heals this amount (source: the dock, `DAMAGE_HEALING`), and the docking ends
  as soon as the docker has full health. `TimeForFullHeal` is `parseDurationReal` (fractions of frames, default of the original one
  frame). Not modelled: the drone healing (the battle drone of the docker is fully healed on every action; drones do not exist).
- **`AutoFindHealingUpdate`**: only for computer players (new: `PlayerConfiguration.IsComputer`, default human; part of state,
  snapshot v3 and replay v4). Between two search runs lie `ScanRate` ticks; it is skipped when the object is healthier than
  `NeverHeal` or busy. The branch "stay on it above `AlwaysHeal`" lies behind an early return in the original: `AlwaysHeal` is
  read, but without effect. The run takes the nearest object with `KINDOF_HEAL_PAD` within `ScanRange` (centre distance, tie
  by ID, no matter whom it belongs to) and calls `AIUpdateInterface.OrderGetHealed`; that works only towards an allied, finished,
  living pad and for injured infantry.
- **New AI state `Enter`** (`OrderEnter`, `TryEnterContainer`): first movement, on arrival the entry into the container or the dock.

**Not modelled:** sale (`Sell`), repair orders of the dozer, crane vehicles, drone healing, `AlwaysHeal`.

## 6g. Area damage, `FireWeaponUpdate`, `ReplaceObjectUpgrade`, `CrushDie`

**Area damage** (`SimulationWorld.DealWeaponDamage`, `SimulationWorld.Combat.cs`; reference: `Weapon.cpp`
`WeaponTemplate::dealDamageInternal`). Every weapon without a projectile (`FireWeaponCommand`, `FireWeaponUpdate`) now distributes its damage over a radius,
instead of hitting only the target (`FireWeaponWhenDeadBehavior` still reports the shot only as `WeaponFireRequested`, §3):

- Distance according to `DAMAGE_RANGE_CALC_TYPE = FROM_BOUNDINGSPHERE_3D`: centre distance minus bounding sphere of the victim, not below 0. Whoever
  lies within `PrimaryDamageRadius` receives `PrimaryDamage`, whoever lies further out (up to the larger of the two radii),
  `SecondaryDamage`. Without any radius only the primary target is hit.
- **`RadiusDamageAffects`** (typed bit mask: `SELF`, `ALLIES`, `ENEMIES`, `NEUTRALS`, `SUICIDE`, `NOT_SIMILAR`, `NOT_AIRBORNE`;
  default `ALLIES|ENEMIES|NEUTRALS`, i.e. without the shooter). The primary target bypasses all filters; every other victim is filtered by self,
  `NOT_SIMILAR` (same template and allied), `NOT_AIRBORNE` and the relationship (§7). `SUICIDE` kills the shooter with
  `HUGE_DAMAGE_AMOUNT`.
- Victims are visited in entity ID order (the original iterates the partition cells); occupants of containers do not count (except
  as the primary target). The armor factor applies per victim (`ActiveArmor`), the return value is the armor-adjusted damage to the primary target.
- **Not modelled:** `RadiusDamageAngle` cone, shock waves, `DamageStatusType`, historic damage, the creator exception of the
  self filter (modelled for projectiles since Phase 8, §6i); projectile flight time is in §6i.

| Module | Reference | Behaviour |
| --- | --- | --- |
| `FireWeaponUpdate` | `FireWeaponUpdate.cpp` `update`, `isOkayToFire` | After `InitialDelay` the private weapon of the module (own clip and reload, never the weapon set) detonates at the position of the object as soon as it is ready; `forceFireWeapon` ignores range and target, the radius decides. Not under construction and not within `ExclusiveWeaponDelay` ticks after a shot of the object (`getLastShotFiredFrame` considers only the weapon set, so the own weapon never suppresses itself). |
| `ReplaceObjectUpgrade` | `ReplaceObjectUpgrade.cpp` `upgradeImplementation` | The object is destroyed, a fresh one of the replacement template takes over owner, position and orientation (full health, no rank, no queue). An unknown or not instantiable template yields only a `CreationSkipped` event (the reference aborts before destroying). Not modelled: path map maintenance, `Player::onStructureConstructionComplete`. |
| `CrushDie` | `CrushDie.cpp` | Reacts only to `DAMAGE_CRUSH` of a crusher (front/rear end, sound, model condition flags); crush damage arises through `PhysicsCollide`, and there is no collision detection. Stateless and inert (precedent `SquishCollide`); collision is Phase 6. |

Evidence: `Tests.Combat.cs` (rings and enemies filter, default mask and bounding sphere, `FireWeaponUpdate` cadence, clip,
`ExclusiveWeaponDelay`, snapshot, `ReplaceObjectUpgrade`, `CrushDie`). **Explicitly not unlocked** are the remaining
blockers from §9: they need terrain, collision or AI (Phase 6) or do not unlock any object on their own.

## 6h. Experience and veterancy levels

Reference: `ExperienceTracker.cpp`, `Object.cpp` (`onVeterancyLevelChanged`, `scoreTheKill`), `Weapon.cpp` (`WeaponBonusSet`,
`Weapon::getDamage/Range/DelayBetweenShots/ClipReloadTime`), `WeaponBonusUpgrade.cpp`, `ExperienceScalarUpgrade.cpp`,
`VeterancyGainCreate.cpp`, `GameData.ini` (`WeaponBonus`, `HealthBonus_*`).

- **Experience value.** A death against an enemy (§7; not neutral, not allied, not under construction, not `IGNORED_IN_GUI`, no
  system object) gives the killer `ExperienceValue[level of the victim]`, multiplied by its `ExperienceScalar` (`(int)(amount · scalar)`,
  rounded down). Only trainable objects (`IsTrainable`) collect experience. The level follows `ExperienceRequired[…]` (4 levels: 0 normal,
  1 veteran, 2 elite, 3 hero); several levels at once are possible.
- **`SetVeterancyLevel`** sets the experience to the minimum amount of the level (`ExperienceTracker::setVeterancyLevel`).
  `SetMinVeterancyLevel` only raises (`VeterancyGainCreate`, `InheritsVeterancy` of a creation list).
- **Level change** (`OnVeterancyLevelChanged`): the maximum health is scaled with the ratio of the `GameData` values `HealthBonus_*`
  (`PRESERVE_RATIO`, the health keeps its ratio), the weapon bonus state VETERAN/ELITE/HERO follows the level, and the
  `Upgrade_Veterancy_*` of the new level generated by the engine is granted (not purchasable). Armor and weapon set selection by level
  is not modelled: no shipped set requires it.
- **`WeaponBonus`** (`GameData` global and per weapon; strictly typed as `<condition> <field> <percent>`, based on the
  name tables of the original, `WeaponBonusTable`): every line yields a delta (`ratio − 1`), lines of an active condition are
  added; a repeated line (condition, field) overwrites (`parseWeaponBonusSet`). Fields: damage, radius, range,
  rate of fire, pre-attack time (`PreAttack` is typed, but without a consumer); conditions among others `PLAYER_UPGRADE` (5), `VETERAN` (9), `ELITE` (10), `HERO` (11). Damage, radius, range,
  delay between shots and clip reload (`DamageWith`, `RadiusWith`, `DelayBetweenShotsWith`, `ClipReloadWith`) apply the bonus of the
  weapon and the global bonus.
- **`WeaponBonusUpgrade`** sets the condition `PLAYER_UPGRADE` on the object (as in the original it is never cleared again), the `WeaponBonus
  PLAYER_UPGRADE …` lines of the weapons thereby take effect (previously a no-op). **`ExperienceScalarUpgrade`** increases the `ExperienceScalar` of the
  object by `AddXPScalar` (previously a no-op). **`VeterancyGainCreate`** sets `StartingLevel` on creation if the science is present.
- **Evidence** (`Tests.Veterancy.cs`): golden master with independent recalculation of experience, levels, scaled health,
  shot ticks, damage and range per level; granting rules, `ExperienceScalarUpgrade`, `WeaponBonusUpgrade`,
  `VeterancyGainCreate`, snapshot continuation.
- **Not modelled:** rank and "Science Purchase Points" from experience (points still come via `GiveSciencePointsCommand`), permanent
  minimum level (`VeterancyGainCreate` sets it once), experience sinks (healing, repair, training by buildings), statistics/skill points.

## 6i. Projectile flight time (Phase 8, package E1)

Reference: `Weapon.cpp` (`WeaponTemplate::fireWeaponTemplate`, `dealDamageInternal`, `WeaponStore::setDelayedDamage`/`update`/`handleProjectileDetonation`,
`Weapon::positionProjectileForLaunch`), `DumbProjectileBehavior.cpp`, `MissileAIUpdate.cpp`, `BezierSegment.cpp`/`BezFwdIterator.cpp`,
`PointDefenseLaserUpdate.cpp`. Implementation: `SimulationWorld.Projectiles.cs`, `SimulationWorld.Combat.cs`, `Modules/BuiltIn/ProjectileModules.cs`.

**Launch** (`SimulationWorld.FireWeaponAt`, used by `FireWeaponCommand`, `FireWeaponUpdate` and `PointDefenseLaserUpdate`):

1. **Scatter:** With `ScatterRadius > 0` or (`ScatterRadiusVsInfantry > 0` and an infantry target) the radius (plus infantry surcharge) is drawn with
   `LogicRandom.Range(0, r)` and the angle with `Range(0, 2π)` (two draws of the logic stream, like `GameLogicRandomValueReal`); the target building
   is aimed at its geometry centre, the point lands on the ground. The scatter only diverts a projectile; a delayed hit keeps
   its victim (in the original `victimObj = NULL` is commented out).
2. **Without a projectile object:** `delayInFrames = |victim position − shooter position| / WeaponSpeed` (3D). Below one tick the shot hits immediately
   (unchanged from Phase 5), otherwise after `ceil(delay)` ticks: the entry lands in the list of delayed hits (order of insertion,
   part of hash and snapshot) and is evaluated **at the end of the due tick after the destruction phase** (`WeaponStore::update` runs in the original
   after `processDestroyList`). The victim is hit at its then current position (`victimID`), if it is gone, the stored point;
   `DamageDealtAtSelfPosition` puts the damage on the shooter position. If the shooter no longer exists, as in the original **no**
   `RadiusDamageAffects` filters apply. `WeaponSpeed ≤ 0` (three damageless retail weapons; the original divides by zero) hits immediately.
3. **With a projectile object:** The object of the template `ProjectileObject` is created at the end of the shot tick (spawn queue, §4 of the core description) at the
   geometry centre of the shooter (the launch bones are graphics data) with its owner and facing; immediately after its
   `OnSpawn` calls its projectile module (`IProjectileModule`) receives weapon, launcher, victim (none with scatter), target point and the
   weapon bonus conditions of the launcher (`m_extraBonusFlags`). Until then the start data are in the spawn queue entry (snapshot and hash).
   An object without a projectile module is set onto the target point ("ok, for things like Firestorm").
4. **Projectile object not instantiable:** never silently: `CreationSkipped` with reason 10 (`ProjectileNotInstantiable`, text = template), after that
   the shot behaves like one without a projectile (step 2). On retail data 157 weapons fire a projectile object, 117 of them an
   instantiable one (real-data test), the remaining 40 run through this substitute.

**Detonation** (`DetonateProjectile`): the weapon detonates at the projectile position **without** a primary target (area damage only), with the bonus conditions
of the launch; the source of the damage is the launcher (`projectileGetLauncherID`), the self filter spares projectile and launcher
(`getProducerID`). Events at the right position and at the right tick: `FxRequested` with `ProjectileDetonationFX` (with position),
`ProjectileDetonationOCL` is executed, `WeaponImpact` (Entity = projectile, Other = launcher, position). On launch
`ProjectileLaunched` reports the target point (for delayed hits with the landing tick in `Value`); `WeaponFired` now reports only the damage dealt **immediately**
(0 with flight time). Events with a location carry `HasPosition` and X/Y/Z; only they write the position, all other events are serialised unchanged.
The veterancy-dependent variants (`VeterancyProjectileDetonationFX/OCL`), `FireOCL`/`FireFX` and `ProjectileExhaust` are not modelled.

**`DumbProjectileBehavior`** (shells on a Bézier arc): speed `WeaponSpeed`, with `ScaleWeaponSpeed` linear between `MinWeaponSpeed` and
`WeaponSpeed` according to the position of the 2D distance between minimum and unchanged range. Control points: start, target (geometry centre of the victim
or target point) and two points at `FirstPercentIndent`/`SecondPercentIndent` of the distance at `FirstHeight`/`SecondHeight` above the highest terrain
between start and target (never below one of the ends). Point count `n = ceil(length / speed)` with the reference length
(`getApproximateLength`: halving until the control polygon is at most 1 longer than the chord, then the mean of both). Update k (k = 1 … n) sets
the projectile to point k − 1, the update after that (or after `MaxLifespan`, default 300 ticks) detonates: **impact in tick F + n + 1** after the
shot tick F. `FlightPathAdjustDistPerSecond` drags the end point after the victim (at most that far per tick) and recomputes the curve with the same
point count. `DetonateCallsKill` kills with `DEATH_DETONATED`, otherwise destruction. `TumbleRandomly` draws three logic numbers when a
`PhysicsBehavior` is present (turn rates are presentation). **Fixed point:** The points are computed from the Bernstein form at `t = i/(n−1)` (the original
sums forward differences in binary32, not reproducible); start and end point are exact. `n = 1` (undefined behaviour in the original:
`BezFwdIterator` leaves the point at the origin, the orientation reads behind the array) flies directly to the end point. The highest terrain is sampled every half cell
(`estimateTerrainExtremesAlongLine` runs over partition cells); an end point outside the map destroys the projectile.

**`MissileAIUpdate`** (guided missiles) as a kinematic state machine instead of THRUST locomotor and physics:

| State | Behaviour |
| --- | --- |
| Launch | glides in the start direction (facing of the launcher, raised by `2·max(0, dz)/max(1, xy)` and normalised) with `InitialVelocity` (with `UseWeaponSpeed` the weapon speed, which is then also maximum speed and acceleration), until `IgnitionDelay` ticks have passed |
| Ignition | arms the warhead, requests `IgnitionFX`, fuel for `FuelLifetime` ticks (0: unlimited), continues without turning |
| Attack (without/with turning) | accelerates with the locomotor acceleration up to the locomotor speed; without turning until `DistanceToTravelBeforeTurning` has been flown (measured before the movement, like the original), then direction straight at the target (`setMaxTurnRate(BIGNUM)`): the position of the tracked victim (`TryToFollowTarget`), otherwise the target point 10 above the ground (`APPROACH_HEIGHT`, only with lock distance) |
| Kill | within the lock distance (`DistanceToTargetForLock`, default 75, 2D; halved for a point target and aimed at the ground point again) straight at the target; whoever reaches it detonates there |
| KillSelf | after the detonation wait `KillSelfDelay` ticks (default 3), then `DetonateCallsKill` → death, otherwise destruction |

Without fuel the missile detonates (`DetonateOnNoFuel`) or glides on without thrust and turning under gravity (`GameData.ini Gravity`, −64/s² =
−4660 raw value per tick²). If the tracked victim is gone, the fuel runs out immediately and the missile disappears without damage
(`airborneTargetGone`). `MissileCallsOnDie` of the weapon (`m_dieOnDetonate`) kills the missile on detonation. **Stand-in for collisions**
(there are none, item 9 of the handoff): an armed missile that reaches its target point in an attack state or would get below the ground
detonates there; an unarmed one glides along the ground; below height 0 it is destroyed. Not modelled: decoys, jammers
(`DistanceScatterWhenJammed`), cruise altitude (`DistanceToTargetBeforeDiving`), bridge layers, `GarrisonHitKill*` (needs collision with buildings).
All vectors are `Fixed`, directions are normalised with the fixed-point root.

**`PointDefenseLaserUpdate`** (Paladin, Avenger, Air Force Chinook): every `ScanRate` ticks it chooses the nearest enemy object of the primary kinds
(`BALLISTIC_MISSILE SMALL_MISSILE`) in laser range, otherwise of the secondary ones, otherwise the nearest outside the range, but within `ScanRange`
(2D, `FROM_CENTER_2D`; equal distances by entity ID, because the partition order is not reproducible); undetected stealth is skipped,
ground targets only with `AntiGround`. Between the scans it fires as soon as the laser is ready (`DelayBetweenShots` without bonus), through `FireWeaponAt`; a
killed or escaped target is discarded and the next scan is scheduled after `LogicRandom.Range(0, 3)` ticks. `PredictTargetVelocityFactor` has no
effect in the original (the predicted position is computed, but not used).

**Evidence** (`Tests.Projectiles.cs`, hand calculations): straight shell 200 units at 300/s = 20 points, impact F + 21 with impact event and
detonation FX at the target point; arc 120 units (heights 30/30 at 25 %/75 %, length 131.43 recalculated independently in binary64) = 14 points,
impact F + 15, point 5 at (44.515; 21.302), apex below the analytical 22.5; missile without ignition delay (10 per tick immediately) impact F + 10,
removal F + 13; missile with 3 ticks ignition delay, 2 per tick initial speed and 1 per tick² acceleration with all 14 positions and
impact F + 15; delayed hit 150 units at 600/s = F + 8, below one tick immediately; scatter deterministic within the radius; the
point defense laser shoots down the missile; snapshot in mid-flight (shell, missile, delayed hit) and two runs with equal hashes.
Real data (`Tests.RealFlight.cs`): the first instantiable unit with a shell (`AmericaTankCrusader`/`GenericTankShell`, impact after 11 ticks) and the
first with a missile (`AirF_AmericaVehicleTomahawk`/`TomahawkMissile`, 45 ticks) hit a Crusader, twice with equal hashes.

## 6j. Transports, tunnel network and Chinook supply (Phase 8, package E1)

Reference: `TransportAIUpdate.cpp`, `ChinookAIUpdate.cpp`, `SupplyTruckAIUpdate.cpp`, `TunnelContain.cpp`, `TunnelTracker.cpp`, `HelixContain.cpp`,
`HelicopterSlowDeathUpdate.cpp`, `OpenContain.cpp`. Implementation: `Modules/BuiltIn/TransportModules.cs`, `World/TunnelTracker.cs`,
`SimulationWorld.Containers.cs`, `AIUpdateInterface` (evacuation).

**Commands** (`CommandTypes.Version = 9`): `MoveAndEnterCommand` (26, `aiEnter`): an own movable, not contained object walks to the container
and boards when it has really reached it (circumference of both objects plus two cells; a partial path that ends too early no longer boards by
teleporting) and the container then still accepts it (capacity on arrival). `EvacuateCommand` (27, `MSG_EVACUATE`/`aiMoveToAndEvacuate`): the
owner has all occupants get out; a movable container with a position drives there first, a Chinook lands beforehand, buildings and tunnels
let out immediately; a tunnel releases the **whole network** through itself. The evacuation order is a state of the AI (`AI_EVACUATE`: not idle),
which every new order replaces. The immediate `EnterContainerCommand` (15) and `ExitContainerCommand` (16) stay unchanged.

**`TransportAIUpdate`:** an `AIUpdateInterface`; the forwarding of attack commands to occupants entitled to fire (`privateAttackObject`) is dropped, because
there is no firing out of containers. Loading/unloading through the commands above, getting out always allowed (`FREE_TO_EXIT`).

**`ChinookAIUpdate`:** a `SupplyTruckAIUpdate` (the same supply machine, §6e) with its own dock delays and `UpgradedSupplyBoost`, as soon as the
player owns `Upgrade_AmericaSupplyLines` (`getUpgradedSupplyBoost`). It flies "even if it starts on the ground": a new Chinook climbs immediately to
`PreferredHeight` above ground (Chinook 100), height change by one locomotor speed per tick (the reference lets the locomotor climb with `Lift`,
`ChinookTakeoffOrLandingState`); supply runs in flight above the warehouse. Occupants leave only a landed Chinook (`getAiFreeToExit`): an
evacuation lowers it to the ground, unloads and lets it climb again. Boarding works without landing (simplification). Not simulated:
rappelling (combat drop), healing at the airfield, flying away from the map edge, `isAvailableForSupplying` (a loaded Chinook supplies anyway).

**`HelicopterSlowDeathBehavior`:** takes part in the same weighted sibling lottery as `SlowDeathBehavior` (common interface
`ISlowDeathCandidate`); after winning (two variance draws of the base module plus one `Real` draw for the rotor ejection) the helicopter rotates around
its own spin (oscillating between `MinSelfSpin` and `MaxSelfSpin` every `SelfSpinUpdateDelay`), flies along its orbit angle with damped
`SpiralOrbitForwardSpeed` (the angle turns by `SpiralOrbitTurnRate`) and falls with `FallHowFast` of gravity (lift `−g·(1 − FallHowFast)`).
After the drawn time the rotor flies off (`FXBlade`, `OCLBlade`, from veteran on `OCLEjectPilot`); one unit above the ground it impacts
(`FXHitGround`, `OCLHitGround`), and in the first tick more than `DelayFromGroundToFinalDeath` later it explodes (`FXFinalBlowUp`, `OCLFinalBlowUp`),
leaves `FinalRubbleObject` (unknown or not instantiable: `CreationSkipped`) and is destroyed. Kinematic instead of force through `PhysicsBehavior`.

**`TunnelContain` with `TunnelTracker`:** one tunnel network per player (in `PlayerState`, part of hash and snapshot v7): the registered tunnels in
registration order and the common occupant list in entry order with entry tick. Every tunnel registers on creation
(`onObjectCreated`, also under construction) and deregisters when it dies (death filter), is removed (`onDelete`) or sold (`onSelling` at the **start** of the sale;
the last tunnel lets everyone out beforehand). If the last tunnel dies, all occupants are destroyed (collapse); otherwise the oldest remaining
tunnel takes over the occupants that had entered through the dead one. Capacity is `GameData.ini MaxTunnelCapacity` (10) for the whole network; aircraft are
excluded (`TunnelTracker::isValidContainerFor`); as a stand-in for `ActionManager::canEnterObject` only own objects, only into living, finished,
unsold tunnels. Healing (`healObject`): per tunnel and tick a share `MaxHealth / TimeForFullHeal`, after `TimeForFullHeal` in the network full –
with several tunnels therefore several times per tick, exactly as in the original, where every tunnel calls `healObjects`. Getting out sets the occupant onto the tunnel
and sends it to a point at the distance `BoundingCircleRadius · [1; 1.5]` in a random direction (`scatterToNearbyPosition`, two logic draws).
Not modelled: nemesis target choice of the tunnel defenders, `harmAndForceExitAllContained` (bunker buster), capture.

**`HelixContain`:** a `TransportContain` with an additional slot for a `PORTABLE_STRUCTURE` (Gatling cannon, propaganda tower): the first one is
always accepted, occupies no slot, follows position and direction of the Helix in every update, dies with it (`onDie` → `kill`) and disappears with it
(`onDelete`). `PayloadTemplateName` (unused in the retail content) is explicitly rejected (template not instantiable with
`HelixContain[PayloadTemplateName]`). Not modelled: garrison weapon bonus, mirroring of the damage state (presentation).

**Skirmish AI** (`SkirmishAi`): Chinooks are suppliers (`Role.Carrier`); GLA units below 50 % health walk into an own tunnel within a
radius of 600 units (`MoveAndEnterCommand`), and as soon as all occupants are healed, the network empties through the tunnel that lies closest
to the army target (`EvacuateCommand`); transports of the army (Humvee, Listening Outpost) pick up infantry within a radius of 250 units when attacking and
let them get out when enemies are in `EngageRadius` or the target is reached. Projectiles are neither targets nor own units of the AI.

**Evidence** (`Tests.Transports.cs`): common network, capacity, entry rules, exit through the other tunnel in the scatter ring 20 … 30; healing
two shares per tick with two tunnels and full after 30 ticks, handover on death, collapse; sale lets out; walking in and unloading at a position;
Chinook climbs 5 per tick to 100, supplies in flight (2 boxes = 200, with Supply Lines 260), lands to unload and climbs again; crash hand-calculated
(−2330 raw value per tick², impact after 75 updates, explosion in the tenth tick after that, wreck); Helix add-on; snapshot v7 with occupants and during the
evacuation, replay v7 of the new commands, v6 rejected. Real data: Chinook delivers (4 deliveries in 1,800 ticks), GLA tunnel lets out through the
second tunnel.

## 7. Teams, diplomacy, victory and defeat

**Relationship model** (`SimulationWorld.RelationshipOf`, `Relationship.cs`; reference: `Player::getRelationship`, `Player::setPlayerRelationship`).
`Relationship` carries the indices of the original (`Enemies = 0`, `Neutral = 1`, `Allies = 2`; part of replay and snapshot). The relationship is
**directed** (`from` sees `to`). Resolution in this order: a player is its own ally; the system object (neutral
objects) is neutral for everyone; an explicit directed override wins; otherwise the team decides – same team other than 0
means allies, everything else enemies. The original starts with NEUTRAL and writes every relationship in the skirmish setup; this world has
no such setup step, "no team, no relationship" therefore means enemy (deliberate deviation).
`PlayerConfiguration(Id, Name, Faction, Team, IsComputer)` supplies the team; `SetRelationshipCommand` (code 19, system only,
`CommandTypes.Version = 7`, encoded in the replay; both players real and different) sets a directed override, the opposite direction
is a command of its own. State: team, overrides (sorted) and computer flag are part of snapshot (v3) and hash; the
replay format is version 4.

**Consumers** (previously everywhere "same owner = ally"): kill accounting (`ScoreTheKill`: experience and bounty only for
enemies), containers (`GarrisonContain`/`TransportContain`: `AllowAlliesInside`/`AllowEnemiesInside`, neutral counts neither as an ally nor as an
enemy), crate drop (`CreateCrateDie`: a kill by an ally does not reward), stealth detection (`StealthDetectorUpdate`
marks enemies), `RadiusDamageAffects` (§6g), target and pad choice (`AutoFindHealingUpdate`, reference match target choice).

**Victory** (`VictoryConditions::update` for a skirmish without "last alliance" deselection):
- `SimulationWorld.IsPlayerDefeated(player)` corresponds to `VictoryConditions::hasSinglePlayerBeenDefeated` with the
  default flags `NOBUILDINGS|NOUNITS` = `!Player::hasAnyObjects`. `Team::hasAnyObjects` (`Team.cpp`) skips
  effectively dead objects or objects registered for destruction as well as projectiles, `INERT` and `MINE` objects; exactly these
  exceptions apply here (`CountsForSurvival`). Without them every faction survives forever through mines, debris and projectiles –
  the reference match then never ended.
- `SimulationWorld.TryGetWinningAlliance()` returns the surviving players in ascending ID order as soon as every survivor is an
  ally of the first (the original compares every survivor only with the first; a non-transitive matrix is judged
  the same way); empty as long as enemies are left or nobody lives any more. `TryGetWinner()` is the member with the smallest ID (without teams the
  last survivor).
- **Not modelled:** script/mission victories, surrender, observers, the "last alliance" deselection (`LastAlliance`).

## 7a. Reference match and golden master scenarios

**Scenario** (`src/CnCNew.Simulation/Testing/ReferenceMatchScenario.cs`, call `SimTool reference-match <Inhalt>`): a
scriptless, seed-based command generator plays all factions able to participate against each other on an obstacle-free
map that exists only as coordinates, and records the commands as a replay (format v4, with players, teams and checkpoints).
The match ends with the victory (§7) plus `GraceTicks` of run-on, at the latest at `MaxTicks` (default 216,000); `MinTicks`
pads the replay with empty ticks if needed. The generator writes exclusively typed commands: start objects by
`SpawnCommand`, production by `QueueProductionCommand`, movement by `SetMoveTargetCommand`, fire by `FireWeaponCommand`
and – new – supply orders by `DockCommand`. All players are computer players (`IsComputer: true`, so that
`AutoFindHealingUpdate` runs). The former money substitute `GiveMoneyCommand` is no longer written: Every base receives,
in addition to the command center, a supply center, a supply carrier of its own side and a neutral warehouse
(`SpawnSupplyInfrastructure`, a logged substitute for the still missing construction process), the carrier drives to the warehouse in the first tick by
`DockCommand` and works after that with its own state machine (§6e). If a side lacks center, carrier or
warehouse, that is stated as a `supply note` in the report; the generator counts `SupplyDeliveries` and `SupplyDeliveryMoney`.

- A participant is every playable faction (`PlayableSide`) whose side provides at least one instantiable, armed,
  buildable unit and which has an instantiable starting building or an **explicitly logged substitute**
  (an instantiable building of the same side with `ProductionUpdate`; the reason is in `MatchParticipant.Substitution`).
- Excluded factions appear with a justification in the report; nothing is silently skipped. With fewer than two
  participants `Record` refuses the recording, `SimTool reference-match` returns exit code 3 and the coverage report.
- Two recordings of the same scenario yield byte-identical replays; two fresh runs and one run with a snapshot at a
  quarter of the length have equal hashes tick by tick; all checkpoints verify; no recorded command is rejected.

**Evidence** (`Tests.ReferenceMatch`): synthetic fixture with two sides, three participating factions (one with a
substitute building, because its starting base contains an unbound module), one excluded for lack of a unit and one
unplayable faction. The match ends with a winner, the replay runs through file, two runs and snapshot identically.

**Retail data.** Newly measured (`SimTool reference-match game/content`, base data without map overlays,
fingerprint `9F5307957A55CD46`): **all 13 playable factions take part**, each with its **real command center**
(`AmericaCommandCenter`, `Infa_ChinaCommandCenter`, `GLACommandCenter`, …), and **no start object is skipped**
(`StartObjectsSkipped` = 0, enforced by the real-data test; the former 13 construction vehicles and GLA workers have been instantiable since `DozerAIUpdate`/`WorkerAIUpdate`).
Result with `GraceTicks` 300: winner player 12 (FactionGLAStealthGeneral) at tick 26,880 (14.9 game minutes), replay 27,181 ticks
(949,191 bytes, 27 checkpoints, SHA-256 `208BD746B5F4059422B72705F9723B7187AAAC9D6CA76DF8DDA3EBB52C52A5A8`), 27,607 commands (1,240
production orders, 7,194 movement orders, 13 dock commands, 18,591 shots), all checkpoints verified, no rejection except 16
`UnknownEntity` (commands to objects destroyed in the meantime), final hash `B6E0CDECA3168EFC`, chain hash `BE21F2345E87EF24`; a second
fresh run and the run from the snapshot at tick 13,590 are identical tick by tick. Peak 135 living objects; final state: 62
objects, 984 created, 922 destroyed; survivors: 47 FactionGLAStealthGeneral, 2 each of FactionGLA, FactionGLADemolitionGeneral and
FactionGLAToxinGeneral. The economy runs for real: **707 deliveries** to supply centers paid out **93,075** credits. The four
American factions have no supply economy in this measurement (`supply note`: their supply carrier is the Chinook, whose
`ChinookAIUpdate` is still missing), their income is the starting capital. **Re-measurement Phase 8, package E1:** the reference match now also uses Chinooks as supply carriers (no `supply note` any more), shots fly (§6i);
winner player 1 (FactionAmerica) at tick 13,800, 36,880 commands, 428 deliveries with 114,450 credits, values in `docs/SIMULATION-CORE.md` §21. The real-data test `Tests.RealReferenceMatch` checks participant count,
winner, deliveries, determinism and snapshot continuation. The coverage situation per side is supplied by `--diagnose`; the task "reference match
of all factions on retail data" is **fulfilled**. Limits (as of Phase 5; the reference match still uses no construction, the skirmish AI of Phase 6 builds, §11): no construction process in the match (dozers build nothing, bases are start objects plus substitute infrastructure),
no special powers in the command stream (there is no `UseSpecialPowerCommand` generator, §6d), no collision/no terrain/no pathfinding (§4);
that GLA Stealth wins is an effect of these simplifications (stealth has no effect on target choice), not a statement about game balance.

**Golden master** (`tests/CnCNew.Simulation.Tests/Tests.GoldenMaster.cs`): the expected values are computed by an independent
model in the test, they are not read back from the simulation.

- Damage chain: `Armor.cpp ArmorTemplate::adjustDamage` (damage · coefficient, below 0 to 0, `UNRESISTABLE` bypasses the
  armor), `Weapon.cpp Weapon::privateFireWeapon` (next shot after `DelayBetweenShots`, after an empty clip after
  `ClipReloadTime`), `ActiveBody.cpp attemptDamage` (death at 0 hit points). Checked are the shot ticks and the death
  with and without armor; milliseconds are converted with 30 ticks per second.
- Production clock: `ThingTemplate::calcTimeToBuild` = `BuildTime · LOGICFRAMES_PER_SECOND`; successive units of the
  queue are finished exactly one build time after another; the costs leave the account on enqueueing.
- The coefficients of the tests (100 %, 50 %) are exact in floating point and fixed point; the documented deviation
  (fixed point instead of `Real`) is thereby not masked, but also not measured against the original – there are no
  original runs as a reference, only the calculation derived from the source code.
- Supply and experience have their own golden master tests with an independent model in the test (`SupplyEconomyGoldenMaster` in
  `Tests.Supply.cs`, `VeterancyExperienceAndHealthGoldenMaster` and `VeterancyWeaponBonusGoldenMaster` in `Tests.Veterancy.cs`).
  They are derived from the source code, not measured against original runs.
- **Block 0.4** of the handoff **stays open** (comparison of the RNG sequence with a compiled original): there is no compiler and there are no
  original runs in this environment; the value is not guessed.

## 8. Command table

See `docs/SIMULATION-CORE.md` §6 for the complete, versioned table (27 command codes, `CommandTypes.Version = 9` up to Phase 9; Phase 8 adds 26 `MoveAndEnterCommand`
and 27 `EvacuateCommand`, §6j; Phase 6 adds codes 20–25: `ConstructCommand`, `SellCommand`, `SetRallyPointCommand`, `ResumeConstructCommand`, `CancelConstructCommand`, `RepairCommand`; `DockCommand` = 18, `SetRelationshipCommand` = 19) and the rejection reasons (codes 25–29 for special powers, §6d;
code 30 `CannotDock`, §6e); the replay format v7 (`docs/SIMULATION-CORE.md` §8) encodes all 27, the snapshot is version 7; rejection codes 31–34 (`TargetNotVisible`, `InvalidPlacement`, `CannotConstruct`, `CannotSell`) come from Phase 6. Phase 10 adds codes 28/29
(`OverchargeCommand`, `CaptureCommand`, `CommandTypes.Version = 10`) and rejection reasons 35–38 (`CannotProduceTemplate`, `ProductionLimitReached`, `CannotCapture`,
`CannotToggleOvercharge`); see `docs/SIMULATION-CORE.md` §23.

## 9. Module coverage

As of Phase 14 (3 October 2026, freshly measured with `SimTool audit-coverage`/`audit-modules`): **2,115 of 2,115 objects instantiable (100.0 %)**, **183 of 205 module types implemented – all 183 simulation modules**, only
the 22 presentation modules are open; module uses covered 16,558 of 19,553 (84.7 %, the rest are presentation modules). New since Phase 13: 76 modules in four groups (air 10, crates/special/collision 21, structure/world 24, slaves/spawn/container
12 from the slave package, the last nine blockers) plus the weapon slot model (`docs/SIMULATION-CORE.md` §30). The rule chapters §6 no longer describe these modules individually; the tables with source and approximation are in §29/§30 of `SIMULATION-CORE.md`.

As of Phase 13 (3 October 2026, freshly measured with `SimTool audit-coverage`/`audit-modules`): **1,735 of 2,115 objects instantiable (82.0 %)**, 107 of 205 module types implemented, module uses covered 16,085 of
19,553 (82.3 %); new are nine aircraft/vehicle/uplink modules (`docs/SIMULATION-CORE.md` §28). The largest remaining blockers by objects (of which blocked only by this module): `JetAIUpdate` 34/34,
`MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `SlavedUpdate` 20/7, `PropagandaTowerBehavior` 14/13, `UpgradeDie` 13/0, `HiveStructureBody` 11/4, `RiderChangeContain` 11/11, `DeployStyleAIUpdate` 10/10,
`StructureCollapseUpdate` 10/10, `ParkingPlaceBehavior` 9/9.

As of Phase 12 (2 October 2026): 1,680 of 2,115 objects (79.4 %), 98 of 205 module types, 15,903 of 19,553 uses (81.3 %); blockers at that time: `JetSlowDeathBehavior` 59/0, `CountermeasuresBehavior` 40/0,
`JetAIUpdate` 34/5, `DeliverPayloadAIUpdate` 28/1, `ParticleUplinkCannonUpdate` 8/8 (all five implemented since Phase 13 except `JetAIUpdate`).

As of Phase 8, package E1 (freshly measured): 1,470 of 2,115 objects instantiable (69.5 %), 85 of 205 module types implemented, 15,543 of 19,553
module uses (79.5 %); details `docs/SIMULATION-CORE.md` §11 and §21. History (state after Phase 6): 1,393 of 2,115 objects are instantiable (65.9 %, compared to 26/2,113 in
Phase 4 and 1,384 at the end of Phase 5); 77 of 205 module types are implemented (183
simulation modules, 22 presentation modules) and cover 15,382 of 19,553 module uses (78.7 %). Not implemented are 77
modules that the catalog assigns to the category "Phase 5" and that are still open,
29 of Phase 6 and 22 of Phase 7 (presentation). According to a fresh `audit-coverage` measurement the largest remaining blockers
by objects (of which blocked only by this module) are: `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66 (needs collision physics),
`SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0, `MissileAIUpdate` 44/29, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/2,
`HeightDieUpdate` 31/15, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `TransportAIUpdate` 24/13,
`BaikonurLaunchPower` 23/23, `SlavedUpdate` 20/7, `HelicopterSlowDeathBehavior` 18/0, `PointDefenseLaserUpdate` 17/4,
`DumbProjectileBehavior` 16/12, `PropagandaTowerBehavior` 14/11, `HelixContain` 13/1, `HordeUpdate` 13/3, `UpgradeDie` 13/0,
`TunnelContain` 12/12, `AnimationSteeringUpdate` 11/0, `ChinookAIUpdate` 11/0, `HiveStructureBody` 11/3. Almost all need terrain,
collision, flight physics, target choice or AI and are still pending (Phase 6 did not introduce collision); a single module no longer unlocks a large object group.

## 10. Known limits (summary)

Every line is a deliberate, documented simplification, not a silent gap:

- **Block 0.4 open:** the RNG sequence was not compared against a compiled original (no compiler, no original runs). The
  golden master tests (§7a) are derived from the source code, not measured against original runs; fixed point instead of `Real` is a
  documented deviation.
- Projectile flight time has been modelled since Phase 8 (§6i), but **without collision**: projectiles detonate at the end of their trajectory, at the target, on the ground or after
  lifetime/fuel, never at an object they fly past; decoys, jammers, cruise altitude and `GarrisonHitKill*` are missing.
  Area damage (§6g) runs for `FireWeaponCommand`, `FireWeaponUpdate`, `PointDefenseLaserUpdate` and detonations, `FireWeaponWhenDeadBehavior` still reports
  only `WeaponFireRequested`.
- Experience, levels, health and weapon bonuses are implemented (§6h); the armor/weapon set selection by level is not
  modelled, because no shipped set requires it. Rank and "Science Purchase Points" from experience are missing. The fear signal
  (block 0.3) is implemented.
- Since Phase 6 there are terrain, pathfinding and turn-rate-limited orientation (§11), still **no collision** (§4). `PhysicsBehavior`, `SquishCollide` and
  `CrushDie` are therefore in the rest state (§6c, §6g); crushing and toppling (`ToppleUpdate`) follow with the collision.
- No energy slowdown factor (§5). Build site check, rally point, construction process, sale and repair by dozers have existed since Phase 6 (§11).
- (Phase 5) The `STEALTHED` status bit had no consumer apart from `StealthDetectorUpdate`'s `DETECTED` marking (no targeting system that
  evaluates `DETECTED`, §6); since Phase 6 visibility queries exclude undetected `STEALTHED` objects (§11), a detector consumer for the AI does not exist.
- Teams and diplomacy are a data model (§7). The **effect** of `CommandSet`/`CommandButton` (command bars) and the
  **radar effect** (`RadarUpdate`/`RadarUpgrade` only keep the state) are not implemented. Garrison (§6a), sciences (§1a) and
  `TransportContain` are implemented, since Phase 8 also `TunnelContain`, `HelixContain`, `TransportAIUpdate` and `ChinookAIUpdate` (§6j; without rappelling,
  firing out of containers and airfield healing). "Science Purchase Points" are
  credited only by system command (§1a).
- Supply (§6e): the machine lock during the dock movement, the proximity check of the warehouses, `setDockCrippled` and the binding of
  spawns to a `SlavedUpdate` are missing; the dock movement is a simple straight-line movement without geometry.
- Special powers (§6d): no construction process, no disabled state, no map-edge delivery (gunships, edge creation only as
  `CreationSkipped`), no gunship AI, no view objects/EVA/script messages, bounty only for enemies (§7). There is no
  generator for `UseSpecialPowerCommand` in the reference match.
- Reference match on retail data: 13 of 13 factions with real command centers, 0 skipped start objects, real deliveries
  (§7a), but without construction process, special powers and collision; winner and duration are an effect of these simplifications. Golden masters
  cover damage chain, production clock, supply and experience. The mechanics are additionally secured by `Tests.Phase5.cs`, `Tests.Supply.cs`,
  `Tests.Veterancy.cs`, `Tests.Teams.cs`, `Tests.Repair.cs`, `Tests.Combat.cs` and the 216,000-tick stress test with the new modules on
  real objects.

## 11. Additions of Phase 6

The rules of Phase 6 are in `docs/SIMULATION-CORE.md` §13–§20 with numbers; here only what changes the game rules:

- **Vision:** shots (`FireWeaponCommand`) require a target visible to the player (`TargetNotVisible`); undetected `STEALTHED` objects and contained objects are invisible. `STEALTHED` is set only with
  `CAN_STEALTH` (bug fix: previously all objects were cloaked).
- **Construction:** dozers and workers place buildings with `ConstructCommand`; build site according to `BuildAssistant::isLocationLegalToBuild` (terrain, footprint, obstacles, explored), costs at the start, build progress linear over
  `BuildTime`. Sale through `SellCommand` (`SaleDurationTicks` 179, `SellPercentage`), repair **free of charge** (not a simplification, but the correct port: neither
  `DozerAIUpdate::privateRepair`/`DozerPrimaryStateMachine`'s healing step nor `RepairDockUpdate::action` deduct money in the reference, both scale only over time/percentage;
  see `docs/SIMULATION-CORE.md` §23 for the supporting text), rally points through `SetRallyPointCommand`.
- **Energy:** `PowerPlantUpgrade`, `PowerPlantUpdate`, `OverchargeBehavior` (§6 of the core description, `SIMULATION-CORE.md` §17); since Phase 10 a real `OverchargeCommand`
  (`TOGGLE_OVERCHARGE`) and a reduced power plant capture (`CaptureCommand`, `SIMULATION-CORE.md` §23).
- **Limits (as of Phase 9, see §23 for the Phase 10 state):** `QueueProduction` checked no prerequisite; `MaxSimultaneousOfType` unchecked; no capture behaviour for
  power plants; no collision; `ToppleUpdate`/`CrushDie` stay open.

## 12. Additions of Phase 31 (fidelity package A, 10 October 2026)

Rules of the original that the port did not follow and now does; the full description with tables, tests and what was left out is `docs/SIMULATION-CORE.md` §44.

- **Leaving a transport or a garrisoned building (F-03, §44.1).** A rider of a living transport leaves through the door: one rider at a time, `ExitDelay` apart, lowest id first
  (`AIExitState::update`, `TransportContain::isExitBusy`); it appears on the exit path's `ExitStart` bone facing the way the transport faces and walks to the `ExitEnd` bone, moved to the
  nearest place it can stand on (`OpenContain::exitObjectViaDoor`, `Pathfinder::adjustToPossibleDestination`). `NumberOfExitPaths = 0` leaves the rider on the transport. An occupant
  told to leave a building walks from its centre to the nearest place it can stand on (`GarrisonContain::exitObjectViaDoor`). Only the riders of a **destroyed** transport are
  scattered, one to one and a half bounding radii away (`TransportContain::onRemoving`, `OpenContain::scatterToNearbyPosition`); `ScatterNearbyOnExit` is read by nothing, as in the
  original. Keys now typed: `NumberOfExitPaths`, `ExitDelay`, `ScatterNearbyOnExit`.
- **A group order gives every unit its own destination (F-04, §44.2).** A player's move or attack-move order for several selected units is one `GroupMoveCommand`
  (`MSG_DO_MOVETO` to an `AIGroup`): the units are taken nearest to the goal first, each keeps its offset from the nearest one up to six of its bounding-circle radii
  (`AIGroup::computeIndividualDestination`) and takes the nearest free place there (`Pathfinder::adjustDestination`); nobody is routed around a group that has already parked.
  Every single move of a ground unit is adjusted to the nearest free place when it is ordered (`AIInternalMoveToState::onEnter`). `CommandTypes.Version` 16, replay format 13.
