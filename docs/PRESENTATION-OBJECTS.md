# Object presentation: model states, draw specifications, frame projector (package C2a)

This document describes `src/CnCNew.Presentation/Objects/**`: the renderer-neutral layer that answers, from the
`CompiledDefinitionGraph` and the running `SimulationWorld`, the question *which model is drawn with which
animation where*. It knows neither Stride nor W3D files; file existence is checked only by the tool
(`PresentationTool object-audit`) through `ContentFileSystem.Exists`.

Dependencies: `CnCNew.Presentation` → `CnCNew.Data`, `CnCNew.Simulation` (public API plus the additive
read view `src/CnCNew.Simulation/Views/EntityPresentationView.cs`). Floating point is allowed here; the simulation
stays fixed point and unchanged.

## 1. Public entry points

| Type | Task |
|---|---|
| `ModelConditions`, `ModelConditionFlag`, `ModelConditionSet` | flag table (117 entries, stable ids), parser, 128-bit set |
| `DrawSpecCompiler.Compile(ModuleConfiguration, diagnostics)` | one `Draw` module → `DrawSpec` |
| `ObjectVisualCatalog.Build(graph)` | `ObjectVisual` per object template (draw modules, scale, shadow, selection radius, `KindOf`, `ModelConditionUpgrade`s) plus all diagnostics |
| `BestStateSelector.FindBest(spec, conditions)` / `FindTransition` | state selection after `findBestInfo`, transition states |
| `ModelStateTracker` | optional client logic for transitions and "wait until finished" |
| `ConditionDeriver.Derive(entity, world, visual, context)` | flags from the simulation state |
| `FrameProjector` (`Advance(world)` per tick, `Project(alpha)` per frame) → `RenderFrame` | interpolated, fog-filtered draw list |
| `StaticScenery.Build(mapObjects, catalog, groundHeight)` | map objects as static scenery |
| `TeamColors.Resolve(players, graph, overrides)` | house colours |
| `ObjectAudit.Run(graph, fileExists)` | inventory audit (used by the tool) |

## 2. Model condition flags

Source of the table: `ModelState.h` (`ModelConditionFlagType`), names from `BitFlags.cpp`
(`ModelConditionFlags::s_bitNameList`), Zero Hour, without `ALLOW_SURRENDER` (there `SURRENDER` is hidden; the
retail content does not use it). The id is the table position and never changes. Names are looked up as by
`INI::scanIndexList` without regard to case. Lists follow `BitFlags::parse`
(`BitFlagsIO.h`): `NONE` empties and ends the list, `+X`/`-X` change the start value, a pure name list replaces
it; mixing and unknown names are explicit errors (the reference throws).

Column "Derivation": how `ConditionDeriver` sets the flag, or – for "not modelled" – what the simulation would have to
deliver. The reference sets the bits imperatively in many places; here every bit is a pure function of the state.

| Id | Flag | Derivation |
|---|---|---|
| 0 | TOPPLED | not modelled: `ToppleUpdate` (topple direction/state) |
| 1–2 | FRONTCRUSHED, BACKCRUSHED | not modelled: crush damage with hit side (`CrushDie`) |
| 3–5 | DAMAGED, REALLYDAMAGED, RUBBLE | exactly one per `BodyDamageType` of the `ActiveBody` (`Drawable::onDamageStateChanged`, thresholds `UnitDamagedThreshold`/`UnitReallyDamagedThreshold` from `GameData.ini`) |
| 6 | SPECIAL_DAMAGED | not modelled: special damage of the `GarrisonContain` |
| 7–8 | NIGHT, SNOW | caller (`ConditionContext`, `FrameProjectorOptions.Night/Snow`) |
| 9 | PARACHUTING | status bit `PARACHUTING` (the simulation does not set it yet: `ParachuteContain` is missing) |
| 10 | GARRISONED | own `GarrisonContain` with at least one occupant |
| 11 | ENEMYNEAR | not modelled: `EnemyNearUpdate` |
| 12–14 | WEAPONSET_VETERAN/ELITE/HERO | veterancy level 1/2/3 (`Object::onVeterancyLevelChanged` → `setWeaponSetFlag`) |
| 15–16 | WEAPONSET_CRATEUPGRADE_ONE/TWO | not modelled: weapon crates (`SalvageCrateCollide`) |
| 17 | WEAPONSET_PLAYER_UPGRADE | `Entity.HasPlayerUpgradeWeapon` (`WeaponSetUpgrade`) |
| 18–33 | DOOR_1..4_OPENING/CLOSING/WAITING_OPEN/WAITING_TO_CLOSE | not modelled: door states of the `ProductionUpdate` when a product drives out |
| 34 | ATTACKING | status bit `IS_ATTACKING` (currently never set: no AI attack state) |
| 35 | PREATTACK_A | not modelled: `PreAttackDelay` |
| 36 | FIRING_A | `LastFireTick` is the most recently simulated tick (`world.Tick - 1`) |
| 37 | BETWEEN_FIRING_SHOTS_A | `world.Tick < WeaponReadyTick` with a non-empty magazine |
| 38 | RELOADING_A | `world.Tick < WeaponReadyTick` with an empty magazine |
| 39–46 | PREATTACK/FIRING/BETWEEN_FIRING_SHOTS/RELOADING _B and _C | not modelled: the simulation knows only the primary weapon |
| 47 | TURRET_ROTATE | `Entity.TurretOf(i).Rotating`: the turret of the simulation is still turning (`TurretAI::friend_turnTowardsAngle`, `docs/SIM-TURRET-AIM.md`) |
| 48 | POST_COLLAPSE | not modelled: `StructureCollapseUpdate` |
| 49 | MOVING | `Speed ≠ 0` or active move command |
| 50 | DYING | `EffectivelyDead`, body is not an `InactiveBody`, object still in the world |
| 51 | AWAITING_CONSTRUCTION | `UNDER_CONSTRUCTION` and `ConstructionWork == 0` |
| 52 | PARTIALLY_CONSTRUCTED | `UNDER_CONSTRUCTION` and `ConstructionWork > 0` |
| 53 | ACTIVELY_BEING_CONSTRUCTED | additionally: work risen since the previous tick (the projector knows the previous tick); without a previous tick: the builder is building right now |
| 54 | PRONE | not modelled |
| 55 | FREEFALL | not modelled: `ParachuteContain` |
| 56 | ACTIVELY_CONSTRUCTING | approximation: `AIUpdateInterface.IsBuilding` and the object stands still; the simulation does not expose target/approach |
| 57 | CONSTRUCTION_COMPLETE | not modelled: `ProductionUpdate` sets it when a finished product exits |
| 58–59 | RADAR_EXTENDING, RADAR_UPGRADED | `RadarUpdate.RadarActive` and `IsExtendComplete` |
| 60 | PANICKING | not modelled |
| 61, 63 | AFLAME, BURNED | status bits `AFLAME`, `BURNED` |
| 62 | SMOLDERING | not modelled: intermediate state of `FlammableUpdate` is not public |
| 64 | DOCKING | `AIUpdateInterface.IsDocking` |
| 65–67 | DOCKING_BEGINNING/ACTIVE/ENDING | not modelled: phase of the `DockMachine` is not public |
| 68 | CARRYING | carried supply boxes > 0 (`EntityPresentationView.CarriedSupplyBoxes`) |
| 69 | FLOODED | not modelled |
| 70 | LOADED | own container that is not a `GarrisonContain` (`TransportContain`), with occupants |
| 71–72 | JETAFTERBURNER, JETEXHAUST | not modelled: `JetAIUpdate` |
| 73–74 | PACKING, UNPACKING | not modelled: `DeployStyleAIUpdate`, `SpecialAbilityUpdate` |
| 75 | DEPLOYED | status bit `DEPLOYED` |
| 76 | OVER_WATER | not modelled: locomotor state over water |
| 77, 82 | POWER_PLANT_UPGRADED, POWER_PLANT_UPGRADING | `PowerPlantUpdate.Extended` and `IsExtendComplete` |
| 78 | CLIMBING | not modelled |
| 79 | SOLD | status bit `SOLD` |
| 80–81 | RAPPELLING, ARMED | not modelled |
| 83 | SPECIAL_CHEERING | not modelled |
| 84–86 | CONTINUOUS_FIRE_SLOW/MEAN/FAST | not modelled |
| 87–88 | RAISING_FLAG, CAPTURED | not modelled |
| 89–91 | EXPLODED_FLAILING, EXPLODED_BOUNCING, SPLATTED | not modelled: death physics |
| 92 | USING_WEAPON_A | together with FIRING_A, BETWEEN_FIRING_SHOTS_A, RELOADING_A (`WeaponSet::getModelConditionForWeaponSlot`) |
| 93–94 | USING_WEAPON_B, USING_WEAPON_C | not modelled (primary weapon only) |
| 95 | PREORDER | not modelled |
| 96–99 | CENTER_TO_LEFT, LEFT_TO_CENTER, CENTER_TO_RIGHT, RIGHT_TO_CENTER | not modelled |
| 100–107 | RIDER1..RIDER8 | status bits `STATUS_RIDER1..8` |
| 108–109 | STUNNED_FLAILING, STUNNED | not modelled |
| 110 | SECOND_LIFE | not modelled: `UndeadBody` |
| 111 | JAMMED | not modelled |
| 112–113 | ARMORSET_CRATEUPGRADE_ONE/TWO | not modelled |
| 114–115 | USER_1, USER_2 | via `ModelConditionUpgrade` (see below) |
| 116 | DISGUISED | status bit `DISGUISED` |

`ModelConditionUpgrade` (`ModelConditionUpgrade.cpp`) can set any flag; the catalog reads `ConditionFlag`,
`TriggeredBy`, `ConflictsWith`, `RequiresAllTriggers` from the graph. It is active if the upgrade mask of the
object (`world.UpgradeKeyMaskOf`) fulfils the triggers and carries no conflict bits. Limit: the `Executed` memory
of the reference (`UpgradeMux`) lies protected in the simulation module; removed upgrades take effect here immediately.

Repeated: for the missing flags the simulation would have to deliver: door phases of production, second/third weapon with
its own state, preparation time and continuous fire, turret angle, dock phase, parachute/free-fall, topple, crush,
collapse and death physics states, jet/deploy/pack states, water/climbing state, crate upgrades,
`SMOLDERING`, stun/jam/second life as well as `CONSTRUCTION_COMPLETE` at the producer.

## 3. Draw modules and `DrawSpec`

The data compiler catalogues the 18 retail draw modules but does not type them: keys and blocks come as
generic fields/`DefinitionSection`s. `DrawSpecCompiler` re-reads the original text (`DefinitionValue.OriginalText`) with
the reference grammar and replays module keys and blocks in source order (line, column), because
`AliasConditionState` refers to the previously declared state.

Key tables (`DrawKeyCatalog`, copied from the `buildFieldParse` tables):

| Module | Kind | Keys beyond `W3DModelDraw` |
|---|---|---|
| `W3DModelDraw`, `W3DOverlordAircraftDraw` | Model | – |
| `W3DTankDraw`, `W3DOverlordTankDraw` | Model | `TreadDebrisLeft/Right`, `TreadAnimationRate`, `TreadPivotSpeedFraction`, `TreadDriveSpeedFraction` |
| `W3DTruckDraw`, `W3DOverlordTruckDraw`, `W3DPoliceCarDraw` | Model | dust/splash particles, twelve tyre bones, tyre/cab/trailer factors (`W3DTruckDraw.cpp`) |
| `W3DTankTruckDraw` | Model | truck keys without middle tyres/cab/trailer plus tread keys |
| `W3DDependencyModelDraw` | Model | `AttachToBoneInContainer` |
| `W3DSupplyDraw` | Model | `SupplyBonePrefix` |
| `W3DScienceModelDraw` | Model | `RequiredScience` |
| `W3DTreeDraw` | Tree | `ModelName`, `TextureName`, swaying/toppling (18 keys) |
| `W3DPropDraw` | Prop | `ModelName` (not in the retail content and not in the module catalog) |
| `W3DLaserDraw` | Laser | 14 beam keys |
| `W3DProjectileStreamDraw` | ProjectileStream | `Texture`, `Width`, `TileFactor`, `ScrollRate`, `MaxSegments` |
| `W3DRopeDraw`, `W3DDebrisDraw`, `W3DTracerDraw`, `W3DDefaultDraw` | – | none (base `ModuleData`) |

`W3DModelDraw` module keys: `InitialRecoilSpeed`, `MaxRecoilDistance`, `RecoilDamping`, `RecoilSettleSpeed`,
`OkToChangeModelColor`, `AnimationsRequirePower`, `ParticlesAttachedToAnimatedBones`, `MinLODRequired`,
`ProjectileBoneFeedbackEnabledSlots`, `TrackMarks`, `ExtraPublicBone`, `AttachToBoneInAnotherModule`,
`IgnoreConditionStates`, `ReceivesDynamicLights` as well as the blocks `DefaultConditionState`, `ConditionState`,
`TransitionState` and the key `AliasConditionState`. State keys (`parseConditionState`): `Model`,
`Turret`, `TurretArtAngle`, `TurretPitch`, `TurretArtPitch`, `AltTurret…` (4), `ShowSubObject`, `HideSubObject`,
`WeaponFireFXBone`, `WeaponRecoilBone`, `WeaponMuzzleFlash`, `WeaponLaunchBone`, `WeaponHideShowBone`, `Animation`,
`IdleAnimation`, `AnimationMode`, `TransitionKey`, `WaitForStateToFinishIfPossible`, `Flags`, `ParticleSysBone`,
`AnimationSpeedFactorRange`. What the presentation needs is typed; remaining module keys are kept as text in
`DrawSpec.Settings` (source order).

Semantics (`W3DModelDraw.cpp W3DModelDrawModuleData::parseConditionState`):

- `DefaultConditionState` must be the first state, at most once, without arguments; it carries the empty bit set.
- A `ConditionState` begins as a copy of the default state (model, turrets, bones, visibilities,
  particle bones, flags, mode, transition key). The first `Animation`/`IdleAnimation` replaces the copied
  animations (`ANIMS_COPIED_FROM_DEFAULT_STATE`), `ParticleSysBone` appends, `Show/HideSubObject` merges by
  name, `Flags` replaces (pure list) or changes (`+/-`) like `INI::parseBitString32`.
- **A header line is a conjunctive bit set** (`ConditionState = REALLYDAMAGED RUBBLE` demands both bits in
  the sense of the best selection). **Every `AliasConditionState` line adds a further alternative to the previous state**
  (`m_conditionsYesVec`); alternatives are "any-of". That is checked against `parseConditionState` (`PARSE_ALIAS` appends to
  `prevState.m_conditionsYesVec`, `PARSE_NORMAL` creates exactly one set).
- `TransitionState = a b` copies the default without keys and is stored under the pair (a, b) (later declaration
  wins like `m_transitionMap[sig] = info`).
- Animations `hierarchie.animation [Weg] [Wiederholungen]` (`parseAnimation`): name lower case, the repetition count
  duplicates the entry; the file is `<animation>.w3d` (`WW3DAssetManager::Get_HAnim`), the hierarchy the skeleton.
- Values that begin with `"` are joined up to the next quotation mark like `INI::getNextAsciiString`
  (`WeaponFireFXBone = TERTIARY "UIMOB03 R HAND"` gives the bone `uimob03 r hand`).
- Trees and props receive a synthetic default state with their `ModelName`, so that the selection is uniform.

Diagnostic codes (`DrawDiagnostic`): `unknown-draw-module`, `unknown-draw-key` (module keys, foreign blocks,
`ConditionState/<Schlüssel>`), `unknown-condition`, `unknown-enum`, `invalid-value`, `state-rule` (the `throw
INI_INVALID_DATA` rules of the reference: duplicate/later default, first state without default not NONE, duplicate
condition set, default next to NONE, bit from `IgnoreConditionStates`, missing model, idle and normal animation
mixed, wrong mode for idle/transition animation, transition with keys or identical states, alias without
predecessor) as well as the warnings `ignored-tokens` (tokens that the reference skips) and `animation-without-dot`.

Allow list: `DrawKeyCatalog.AllowList` – every entry names module type, key and reason. The retail content
needs no entry; the list is empty.

## 4. State selection

`BestStateSelector.FindBest` is a rewrite of `W3DModelDrawModuleData::findBestInfo` and
`SparseMatchFinder::findBestInfoSlow` (`SparseMatchFinder.h`):

1. Bits from `IgnoreConditionStates` are deleted from the current set.
2. All states in declaration order, per state the alternatives from back to front.
3. `yesMatch` = set bits that the alternative demands (`countIntersection`); `extraneous` = demanded bits that
   are not set (`countInverseIntersection`).
4. An alternative wins on `yesMatch > best` or on `yesMatch ≥ best` and `extraneous < bestExtraneous`
   (start `0`/`999`). Full ties keep the earlier candidate.

This is a *sparse* search: a state can win although it demands a bit that is not set
(`MOVING DAMAGED` fits `MOVING` if there is no pure `MOVING` state). Without states the search delivers
`DefaultStateIndex` or -1. The cache of the reference (`m_bestMatches`) is meaningless for the result and is dropped.

Transitions (`W3DModelDraw::setModelState`): if a real state with `TransitionKey` changes into another with
`TransitionKey`, the `TransitionState` of the key pair plays first (`FindTransition`); if the new state carries
`WaitForStateToFinishIfPossible` equal to the transition key of the running state and its animation is still running,
it is noted for later. `ModelStateTracker` reproduces that; the animation time belongs to the renderer, which reports completion
(`AnimationCompleted`). The frame projector delivers only the target state (no "sticky" transitions in the frame);
transitions are the client's business.

## 5. Frame projector

Contract:

- `Advance(world)` is called exactly once after every `world.Step()`. Per drawn object it captures position,
  yaw angle (radians like the simulation), owner, health, construction progress, flags and the chosen state per
  draw module. `Tick` is the most recently simulated tick.
- `Project(alpha)` does not access the world: it interpolates linearly between the second-to-last and the last capture
  (angles over the shorter arc), `alpha` is limited to [0, 1]. New objects appear without interpolation,
  jumps beyond `SnapDistance` (default 100) too. Objects that the world no longer holds are missing from the next
  capture on; dying objects stay with `DYING` as long as the simulation holds them. Occupants are not drawn.
- Order in the `RenderFrame`: objects by ascending id (ghosts sorted in), after that the scenery by index.
- `DrawableInstance` carries `Visual` (template, scale, shadow, selection radius, `KindOf`, `IsStructure`, `IsUnit`),
  `Selections` parallel to `Visual.Draws`, house colour, `IsStatic`, `IsGhost`. `TurretYaw/TurretPitch` (and `AltTurret…`) come from the
  turret state of the simulation (`Entity.TurretOf`, `docs/SIM-TURRET-AIM.md`) and are `null` only for objects without a turret.
- Fog (`FrameProjectorOptions.Viewer`): what is drawn is what `world.EntitiesVisibleTo(viewer)` delivers (own,
  allied and visible foreign objects). Foreign buildings (`STRUCTURE`, not own, not allied) that drop out of
  vision stay with their last capture as a ghost (`IsGhost`) until the viewer sees their position again –
  even if they have been destroyed in the meantime (like the ghost objects of the reference). Without a viewer everything is drawn.

## 6. Scenery from map objects

Rule of `StaticScenery.Build`: every map object that is not a waypoint and not a road/bridge end point, whose
template exists and whose draw modules choose a model under the empty condition set, becomes a `SceneryItem`
(position from the map, height via the optional `groundHeight` delegate, otherwise map Z; angle of the map in
radians; scale of the template). Unknown templates and templates without a model are counted (`SceneryReport`).

The simulation creates some map objects itself (currently the supply depots in `SkirmishMatchSession`). The
projector hides a scenery object **permanently** as soon as a newly captured object of the same template stands within
`FrameProjector.ClaimRadius` (1 unit); if the object dies later, the scenery does not return. The scenery
ignores the fog like the terrain and sees night/snow from the projector options.

## 7. House colours

`TeamColors.Resolve`: override, otherwise `PreferredColor` of the `PlayerTemplate` of the faction (from the graph), provided
no earlier player carries this colour yet, otherwise the next free entry of our own palette (eight colours, not from
the game data). System and unknown owners: neutral.

## 8. `PresentationTool object-audit`

```powershell
dotnet run --project tools/CnCNew.PresentationTool -- object-audit game/content
```

Compiles the retail base data (`--maps exclude` equivalent, strict), builds the catalog and writes deterministic
lines (counters, module types, states, animation modes, diagnostics per code/key with locations, file resolution
under `Art/W3D` with missing names, turret/weapon/particle bones). Exit code 1 if an unknown flag name or an
unknown draw key is not in the allow list.

Measured on 30 September 2026 (two runs byte-identical, 247 lines):

| Quantity | Value |
|---|---|
| Objects / with draw module / with model / with several draw modules | 2,115 / 2,115 / 1,847 / 320 |
| Draw modules | 2,928 (W3DModelDraw 2,328, W3DDefaultDraw 180, W3DTreeDraw 128, W3DTruckDraw 118, W3DTankDraw 75, W3DDependencyModelDraw 26, W3DLaserDraw 25, W3DOverlordAircraftDraw 12, W3DProjectileStreamDraw 7, W3DTankTruckDraw 7, W3DOverlordTankDraw 5, W3DOverlordTruckDraw 5, W3DSupplyDraw 5, W3DPoliceCarDraw 3, W3DDebrisDraw/W3DRopeDraw/W3DScienceModelDraw/W3DTracerDraw 1 each) |
| States / of which default / alias alternatives / transitions | 23,170 / 1,777 / 15,400 / 4,774 |
| Animation modes (states and transitions) | ONCE 15,034, LOOP 7,508, MANUAL 2,748, ONCE_BACKWARDS 2,648, LOOP_BACKWARDS 6, LOOP_PINGPONG 0 |
| flags used | 106 of 117 |
| unknown flag names / unknown keys / not classified | 0 / 0 / 0 |
| Rule violations | 45 × `missing-model` (see 9.) |
| Warnings | 4 × `ignored-tokens` at `TransitionState = UP_SNOW NIGHT DOWN_DEFAULT` (the reference reads only `up_snow`/`night`) |
| Model files | 6,041 different, 5,970 present, 71 missing |
| Animation files | 3,146 different, 3,108 present, 38 missing |
| Skeletons (animation hierarchies) | 2,431 different, 2,406 present, 25 missing |
| States with turret / objects with turret / with second turret | 1,680 / 180 / 4 |
| Weapon bone uses (FX / recoil / muzzle flash / launch / hide-show) | 5,825 / 485 / 5,177 / 2,973 / 26 |
| Particle bones | 22,062 entries, 311 bones, 240 particle systems |
| `ModelConditionUpgrade`s | 55 |

Scenery "Alpine Assault" (real-data test): 1,702 map objects → 588 scenery objects, 252 waypoints, 786
road/bridge points, 76 without a model, 0 unknown.

The missing files (for example `aiblocktile`, `avbattship_d`, snow/night variants of some civilian buildings) are missing
in the extracted original archives too (`.local/assets/by-archive`, spot-checked); the reference
only registers such assets as missing (`WW3DAssetManager::Get_HAnim`, `Register_Missing`). They are listed, not
counted as errors; the renderer must skip a state without a loadable model.

## 9. Known limits

- **Data gap in the compiler:** `DefinitionCompiler.CompileSection` discards lines without `=` in nested blocks
  (`IniStatement`). The reference separates tokens at spaces too (`INI::m_seps`), so it reads `Model ABPWRPLANT_A1` as
  key/value. Affected are 45 states of the power plant control rods (nine each in `AirforceGeneral.ini`,
  `BossGeneral.ini`, `FactionBuilding.ini`, `LaserGeneral.ini`, `SuperWeaponGeneral.ini`); they appear as
  `missing-model` and draw no model until the data compiler carries such lines as properties.
- Particle system names from `ParticleSysBone` are not checked against particle definitions (business of the FX package).
- Module keys outside the presentation's needs (tyres, treads, laser, tree physics) stay text in `Settings`.
- Second/third weapon, doors, dock phases and the remaining "not modelled" flags from section 2 are missing
  in the simulation; the associated behaviour of the models is absent until then.
- `ACTIVELY_CONSTRUCTING` is an approximation (build task and standstill).

## 10. Tests

`tests/CnCNew.Presentation.Tests/ObjectTests.cs` (synthetic fixtures, no original excerpts): flag table and
parser, selection including the FRONTCRUSHED example of the reference, ties, aliases and ignore bits,
transitions/waiting in the `ModelStateTracker`, `DrawSpec` copy semantics, diagnostics, quotation-mark tokens,
flag derivation on a real `SimulationWorld` (firing/reloading, damage, movement, veterans, upgrades, construction,
sale), interpolation, fog with ghosts, scenery with the hiding rule, house colours. With
`--content-dir game/content` additionally: Crusader and barracks against the retail data, audit without
anything unclassified and deterministic, projection of real templates, scenery of "Alpine Assault".

## 11. Time and tick rate (Phase 21)

The object presentation no longer assumes anywhere that a tick lasts 1/30 s. Two concepts, two names: the **simulation rate** comes from the world (`world.Time`), the **original frame** (1/30 s) is the unit in which the original authored its client values – outside the simulation exactly one constant, `CnCNew.Data.OriginalFrame.PerSecond`. At 30 Hz an original frame is one tick; therefore every value and every checksum is unchanged there (evidence: `docs/TICKRATE-MIGRATION.md` §14).

| What | Before | Now | At 60 Hz |
|---|---|---|---|
| Capture and interpolation (`FrameProjector.Advance`/`Project`) | per tick | per tick (unchanged) | twice as many captures, interpolation steps half as large – intended |
| Muzzle flash (`FrameProjector.MuzzleFlashFrames = 4`) | `world.Tick - lastFire <= 4` | `<= world.Time.OriginalFrames(4)` | 9 instead of 5 ticks, same game time |
| `FIRING_A/B/C` (`ConditionDeriver.Weapon`) | "fired in the last tick" | "fired within the last original frame" | two ticks long, 1/30 s as at 30 Hz |
| Healing icon (`HealingIconFrames = 3 * OriginalFrame.PerSecond`) | `3 * 30` ticks | converted into ticks of the world (`_healingIconTicks`) | 3 s |
| Cheering (`UnitConditionTracker.CheerFrames`) | `3 * 30` ticks | `FrameProjector.Cheer(entities, world)` converts | 3 s |
| Selection/capture flash (`SelectionFlashTracker`) | age in ticks | age in original frames (`GameClock.OriginalFrameOf` of the event tick); the envelope (two frames peak, then 3/4, 1/2, 1/4) is a table per original frame | same duration; the steps change every two ticks |
| Script flash (`ScriptFlashInterval = 15`, `StartScripted`) | `tick % 15`, `seconds * 30 / 15` | `Advance(frame)` once per original frame, number of flashes from `OriginalFrame.PerSecond` | every 0.5 s, four flashes in two seconds |
| Suspension (`SuspensionTracker`) | one step per tick | one step per **original frame** (on the ticks that begin one); in between the pose is held | the same oscillation; the pose changes 30 times per second |
| `FREEFALL` of the avalanche rocks | `Life < 100`, fall per tick > 0.2 | `Life < OriginalFrames(100)`, fall per tick times ticks per original frame > 0.2 | same threshold per game time |
| Cleaning up the tables, fog of the scenery | every 150 and 5 ticks respectively | every 150 and 5 original frames respectively (`PruneIntervalFrames`, `SceneryFogIntervalFrames`) | same game time |

**Suspension: why the frame grid and not the step size.** The brief of Phase 21 says "compute spring and damper with the step size". That is deliberately not built that way: the recursion from `Drawable::calcPhysicsXformTreads/Wheels` is a mapping per frame with factors per frame (damping 0.9, rising nose halved, wheels follow by half, steering by one tenth, plus clamps and the ground bump), not a discretised differential equation; computed with half the step size it settles differently. It therefore runs once per original frame at every rate, speed and acceleration are differences over one original frame (the maximum speed of the locomotor – per tick of the world – is converted to the original frame for that), and the randomness of the ground bump depends on the frame number instead of the tick. Measured (`ObjectTests.TimeScale.cs`): settling onto a slope 1.2667 s at 30 and 60 Hz, 1.2833 s at 120 Hz (one tick of offset on appearing), rest position the same to five decimal places. The pose is still not interpolated (Phase 29).

**Deliberately left in ticks:** the door clock of the production buildings and `ConstructionComplete` (`StructureConditionTracker`: stamp and catalog durations are both ticks of the world), the minimum hold time of the steering states (`AnimationSteeringUpdate.MinTransitionTicks`), the turn direction from the course change of one tick against `Locomotor.TurnRatePerTick`, `ACTIVELY_BEING_CONSTRUCTED` (construction work compared with the previous capture) and the teleport threshold of the interpolation (`SnapDistance`, distance between two captures).

**Time-scale test** (`tests/CnCNew.Presentation.Tests/ObjectTests.TimeScale.cs`, each at 30, 60 and 120 Hz on the synthetic fixture of the rate, tolerance one original frame): muzzle flash after a shot, `FIRING_A` (exactly 1/30 s at every rate), healing icon after the last healing (3.0333 / 3.0167 / 3.0083 s), script flash (four flashes, end of the last one 1.6667 s after the request, one flash 0.1667 s), settling of the suspension.

**Not verified:** whether muzzle flash, flash and suspension *look* right at 60 Hz. Durations are measured, no human has seen it (visual check list: `docs/TICKRATE-MIGRATION.md` §14.7).
