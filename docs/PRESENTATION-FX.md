# Presentation: effects (FXList, particles, tracers, light, camera, decals, sound cues)

As of: 30 September 2026 (package C2b)

`CnCNew.Presentation.Fx` executes the effect requests of the simulation in a renderer-neutral way. Inputs are the
`CompiledDefinitionGraph` (through `FxCatalog`) and the `SimEvent`s of a tick; outputs are pure data that a
renderer or the audio layer fetches per frame. There are no callbacks into renderer or audio, no
Stride, W3D, Content or Formats dependency. Floating point is allowed here; the reproducibility comes from a
random generator of its own, fixed steps and canonical order.

## Entry points for the integration

| Type | Task |
|---|---|
| `FxCatalog.Build(graph)` | FXLists, particle system templates, weapon/object/DamageFX effect references, sound tables, GameData values, `Diagnostics` |
| `FxExecutor(catalog, seed, budget?)` | `ExecuteEvents(tick.Events, view)` after every `SimulationWorld.Step()`, `Update(view)` once per logic frame, `Capture(cull)` → `FxFrame`, `DrainSoundCues()` |
| `IFxWorldView` / `SimulationFxWorldView` | Pose, bones, muzzle, ground height, shroud; the adapter over `SimulationWorld` needs `Refresh()` after every step |
| `ParticleWorld` | Particle simulation (`Spawn`, `Destroy`, `Step`, `Capture`), usable without the executor as well |
| `CameraShake` | Combining `ViewShakeRequest`s with the camera position (camera agent) |
| `SoundCueRateLimiter` | Deterministic throttling of the sound cues before the audio layer |
| `FxAudit.Run` / `FxAudit.ExecuteAll` | Inventory and execution pass (tool `fx-audit`, real-data test) |

`FxFrame` contains `ParticleFrame` (instances plus batch keys), active tracers, light pulses, all decals and the
ray and view-shake requests, which are delivered once.

## Data model and parser

The FX domains have no typed schema in `CnCNew.Data`; the compiler keeps the fields heuristically typed, but preserves
the original text (`DefinitionValue.OriginalText`). `FxValueReader` applies the parse functions of the reference to it
(`INI::scanReal` with number prefix, `scanBool`, `parseCoord3D`, `parseRGBColor` with division by 255, `parseIndexList`
case-insensitive, `parseDurationUnsignedInt` with rounding up in single precision, `parseVelocityReal` per
logic frame, `parseAngleReal`, `parsePercentToReal`). Unknown nuggets, keys and enum values as well as invalid values are
never replaced, but reported as `FxDiagnostic`.

Repeated `FXList` blocks: the compiler appends the nuggets, whereas `FXListStore::parseFXListDefinition` (FXList.cpp) empties the
list before parsing. `FxCatalog` therefore keeps only the sections of the last block (same file, after its
declaration line). Affected in the retail content: `FX_GattlingExplosionOneFinal` and `Nuke_WeaponFX_NukeCannon`.

Names are resolved case-insensitively as everywhere in `CnCNew.Data`. The reference
(`NameKeyGenerator::nameToKey`, template map of the `ParticleSystemManager`) compares exactly; every reference that can be resolved only this way
is reported as `case-mismatch` (retail: 0).

### Nuggets (`FXList.cpp TheFXListFieldParse`), measured with `fx-audit`

| Nugget | Retail count | Record | Effect |
|---|---:|---|---|
| `ParticleSystem` | 860 | `ParticleSystemFxNugget` | Particle systems, `Count`, `Offset` (rotated with the object matrix), `Radius`/`Height`/`InitialDelay` as a random variable, `RotateX/Y/Z`, `OrientToObject`, `Ricochet`, `AttachToObject`, `CreateAtGroundHeight`, `UseCallersRadius` |
| `Sound` | 320 | `SoundFxNugget` | `SoundCue` (Effect) at the primary position |
| `ViewShake` | 139 | `ViewShakeFxNugget` | `ViewShakeRequest` (SUBTLE … CINE_INSANE) |
| `LightPulse` | 65 | `LightPulseFxNugget` | `LightPulseInstance` |
| `TerrainScorch` | 46 | `TerrainScorchFxNugget` | Scorch decal (SCORCH_1..4, SHADOW_SCORCH, RANDOM) |
| `Tracer` | 18 | `TracerFxNugget` | `TracerInstance` |
| `FXListAtBonePos` | 5 | `FxListAtBonePosFxNugget` | FXList at bones `Name`, `Name01`, `Name02`, … |
| `RayEffect` | 0 | `RayEffectFxNugget` | `RayEffectRequest` (unused in the retail content, implemented) |

Retail (`--maps exclude`): 429 FXLists with 1,453 nuggets, 1,087 particle systems (1,052 PARTICLE, 30 STREAK,
4 VOLUME_PARTICLE, 1 DRAWABLE; shaders 589 ADDITIVE, 470 ALPHA, 26 ALPHA_TEST, 1 MULTIPLY, 1 NONE), 60 with `SlaveSystem`,
119 with `PerParticleAttachedSystem`, 363 weapons (284 with FireFX/ProjectileDetonationFX, 221 with FireSound),
11 DamageFX tables, 1,079 objects with default DamageFX.

### Particle system templates (`ParticleSys.cpp ParticleSystemTemplate::m_fieldParseTable`)

`ParticleSystemTemplate` types all 62 keys: priority (13 levels), shader, type, texture/drawable name, angles,
angular velocity, dampings, gravity, slave system and offset, per-particle attached system, lifetime
(particle and system, in frames), size, start size rate, size rate and damping, eight alpha and eight color keyframes,
ColorScale, burst delay and count, initial delay, drift velocity, velocity type (ORTHO, SPHERICAL,
HEMISPHERICAL, CYLINDRICAL, OUTWARD), volume type (POINT, LINE, BOX, SPHERE, CYLINDER), `IsHollow`, `IsGroundAligned`,
`IsEmitAboveGroundOnly`, `IsParticleUpTowardsEmitter` and the wind parameters. Default values follow
`ParticleSystemInfo::ParticleSystemInfo`; random variables without a key are zero-initialised as in the reference.

Velocity and volume parameters lie in unions as in the reference (`m_emissionVelocity`, `m_emissionVolume`);
`VelocitySlots`/`VolumeSlots` represent that exactly (the last write wins), named accessors read them.

`GameClientRandomVariable` (`RandomValue.cpp getValue`, `INI::parseGameClientRandomVariable`): `low high [distribution]`,
without a distribution UNIFORM. CONSTANT with `low == high` yields `low` without a random draw, CONSTANT with `low != high` behaves
like UNIFORM, UNIFORM draws even with `low == high`. GAUSSIAN, TRIANGULAR, LOW_BIAS and HIGH_BIAS are not
implemented in the reference and yield 0; the parser reports them as an unexplained diagnostic (retail: none).

## Particle simulation (`ParticleWorld`)

Rewritten after `ParticleSystem::update`, `Particle::update`, `generateParticleInfo`, `computeParticlePosition`,
`computeParticleVelocity`, `computePointOnUnitSphere`, `mergeRelatedParticleSystems`, `updateWindMotion`,
`Particle::doWindMotion`, `Particle::isInvisible`, `ParticleSystemManager::update/createParticle/removeOldestParticles`
(all `ParticleSys.cpp` of the EA sources, GeneralsMD):

- One `Step` is one logic frame (1/30 s); the reference updates particles once per logic frame
  (`ParticleSystemManager::update` checks `m_lastLogicFrameUpdate`).
- Systems run in creation order; a slave is queued before its master as in the constructor of the reference,
  systems created during a step (per-particle attached) are updated in the same step.
- The initial delay halts the whole system (including the particle update); after it wind, transformation (attached to
  an object: object matrix times local matrix; controlled by a particle: its position), emission of
  `(int)BurstCount` particles with inter-frame correction of the position, slave particles per master particle, then
  the particle update: acceleration (gravity), damping, position plus drift, wind, angle, size, alpha (not for
  ADDITIVE; on reaching a keyframe the value is set to the key value), color (without snapping, then ColorScale),
  lifetime, invisibility test per shader.
- The color clamping deliberately adopts the typo of the reference (`if (m_color.red < 0) m_color.green = 0`), so that
  tints stay identical.
- Positions and velocities are scaled with `0.5 + ParticleScale/2`, sizes with `ParticleScale`.
- The hollow BOX case of `computeParticlePosition` falls through into the LINE case in the reference without a `break`; the
  result therefore lies on the union line (half size to line end). Reproduced, including the random draws.
- `mergeRelatedParticleSystems` adds the `SlavePosOffset` of the slave template (not of the master); all 60 retail values
  are zero anyway.

Deviations (deliberate, documented):

- If a master dies, `ParticleWorld` destroys the slave. `~ParticleSystem` only decouples it, whereupon in the reference it
  would emit by itself at the world origin.
- OUTWARD with volume NONE leaves a static result unchanged in the reference (stale value); here zero.
- Negative samples at `(UnsignedInt)` casts (undefined in C++) become 0.

### Budgets and culling

`ParticleBudget`: `MaxParticles` (`GameData.ini MaxParticleCount`, retail 2,500; `GlobalData` default 0 = no
particles), `MaxFieldParticles` (`MaxFieldParticleCount`, 30; counted are the visible AREA_EFFECT particles with
`IsGroundAligned` of the last `Capture`), `MinPriority` (LOD minimum priority from `GameLOD.ini MinParticlePriority`;
the caller chooses the detail level), `MaxParticlesPerSystem` 512 (`W3DParticleSystemManager::MAX_POINTS_PER_GROUP`,
here as a simulation limit) and `MaxSystems` 4,096 (a safety limit of our own). ALWAYS_RENDER is exempt from all count
limits. If the total budget is reached, the oldest particle of the lowest, strictly smaller priority is displaced;
if there is none, the new one is rejected. That smooths the off-by-one of `removeOldestParticles` (whose return value
never equals the requested number after a complete pass). All rejections and displacements are in
`ParticleWorldStats`.

`Capture(isVisible)` is the culling hook (position, size); discarded particles count in `ParticleFrame.Culled`.

### Output and order

`ParticleInstance(Position, Size, Color RGBA 0..1, Rotation, Batch, SystemId, Personality)`, sorted by system id
(creation order) and personality (creation order within the system). `ParticleBatchKey(Texture, Shader, Kind,
GroundAligned)` in the order of first occurrence. Render hints from `W3DParticleSys.cpp`: ADDITIVE, ALPHA,
ALPHA_TEST, MULTIPLY correspond to the sprite presets of the same name; `GroundAligned` means no billboard
(`shouldBillboard`); STREAK systems with at least two particles are drawn as a polyline in instance order;
the reference quantises the angle to 8 bits.

### Determinism

All random values come from `FxRandom` (SplitMix64, seed from the caller, `NextUnit` with 24 bits, bounds inclusive like
`GameClientRandomValueReal`/`GameClientRandomValue`). No wall time, no `Random.Shared`, no hash order in
outputs (lists in creation order, sorted dictionaries). Two runs with the same seed are byte-identical
(test and `fx-audit` checksum); the guarantee holds for the same platform/runtime, since `MathF.Sin/Cos/Acos` use the CRT.

## FX execution (`FxExecutor`)

- `FXList::doFXPos`/`doFXObj` separated: object form for `FxRequested` with `Value == 1` (OrientToObject of the die modules),
  DamageFX and bones; position form for FireFX, detonation effects and `FxRequested` with `Value == 0` at the object pose.
  Shroud is checked through `IFxWorldView.IsShrouded` (adapter: no shroud model yet, always visible).
- `WeaponFired`: FireFX of the veterancy level at the weapon muzzle (`GetWeaponMuzzle`), otherwise at the source, for
  contact weapons (`AttackRange - 2.5 < 10`, `WeaponTemplate::isContactWeapon` with `RATIONALIZE_ATTACK_RANGE`) at the target;
  velocity = weapon speed, secondary position = target, radius = PrimaryDamageRadius.
  `ProjectileDetonationFX` does **not** play at the shot: the simulation flies the projectile object and requests the effect at the place and tick of the
  detonation (`SimulationWorld.DetonateProjectile` → `FxRequested`). Until 9 October 2026 the effect additionally played immediately at the target
  (a leftover from the time without projectile objects): every impact was seen and heard twice, once before the projectile arrived (`docs/ACCEPTANCE.md`).
- `EntityDamaged`: `ActiveBody::doDamageFX` with the DamageFX of the unconditional ArmorSet (`Conditions = None`); the
  damage type comes from the `WeaponFired` of the same tick (pair source/target, otherwise source), the veterancy level from the
  source, major for `damage >= AmountForMajorFX`, throttling per victim and damage type with `ThrottleTime` in ticks.
  Without a shot in the same tick the type is unknown (`damagefx-without-weapon`).
- `ParticleSystem`: `reallyDoFX` with offset × matrix, random radius/angle, height or ground height, local rotation,
  attaching to the object, initial delay in frames (`ceil(ms · 30/1000)`), `UseCallersRadius` for SPHERE/CYLINDER;
  `Ricochet` orients away from the causer.
- `Tracer` (`TracerFXNugget::doFXPos`, `W3DTracerDraw`): probability draw, lifetime
  `ceil((distance − length)/speed · DecayAt)` frames, opacity falls linearly to 0, the head moves by the
  speed per frame. Speed 0 (division by zero in the reference) gives one frame.
- `LightPulse` (`W3DDisplay::createLightPulse`, `W3DDynamicLight::On_Frame_Update`): inner radius 1, outer radius
  `1 + Radius`; below `2 · 10 + 1` no light is created, as in the reference (retail pass: 52 of 130
  executions in both forms); rise/decay in frames; at most 64 simultaneous lights (a limit of our own, oldest first).
- `ViewShake` (`W3DView::shake`, `updateView`): requests go to the camera; `CameraShake` adds
  `intensity · (1 − distance/MaxShakeRange)` (2D, nothing beyond the range) in a random direction, damps per frame
  by 0.75, reverses the direction and stops below 0.01. If the sum exceeds `MaxShakeIntensity`, the reference resets it
  to 3.0 – adopted.
- `TerrainScorch` (`BaseHeightMapRenderObjClass::addScorch/updateScorches`): decal with atlas `EXScorch01.tga`
  (3×3 cells at a spacing of 1.5 on 4 units), RANDOM = SCORCH_1..4, ring buffer 500, duplicate for the same type and
  a deviation below a quarter of the radius. The terrain tinting and the fitting to the height field are done by the
  renderer.
- `FXListAtBonePos`: bones from `IFxWorldView.GetBonePosition` (object layer C2a) like
  `W3DModelDraw::getCurrentBonePositions` (first the name, then `Name01` up to the first missing one, at most 40); the
  bone matrix is missing, instead the object rotation is used (`OrientToBone` is not evaluated by the reference either).
  The position form has no effect, as in the reference (`bonepos-without-object`).
- Decals of this package are exclusively scorches and explicit FX decals (`DecalBuffer.Add` with lifetime); building
  shadows, radius and selection decals belong to the object or input layer respectively.

All skipped or approximated cases are counted by `FxExecutor.Counters` (sorted).

## Attached effects (Phase 35, `AttachedEffects`, `AttachedEffectCatalog`)

**The finding (16 October 2026, the project owner, by watching):** the Chinese Dragon tanks showed no flames - only "some fake projectile" came out of them -, nothing
smoked, and units did not burn. The cause was general: every effect an object **carries continuously** - as opposed to the FX list an event plays once - had no
implementation. The simulation keeps `BoneFXUpdate` and `BoneFXDamage` as stateless shells ("effects on model bones are presentation", `docs/SIMULATION-CORE.md`),
`TransitionDamageFX` there plays only its creation lists, and the presentation parsed `ParticleSysBone` of the model states without ever creating a system from it. The
Dragon tank's flame is entirely such an effect: its projectile `DragonTankFlameProjectile` has `Model = NULL` and six particle systems of `BoneFXUpdate`
(`FlameThrowerTrail01`..`06`, `FlameThrowerSmoke01`), its weapon a projectile stream (`FlamethrowerProjectileStream`, `W3DProjectileStreamDraw`), and `Model = NULL` was
taken for a model name and drawn as the placeholder box - the "fake projectile". No check found it: `fx-audit` checks the references of FX lists, and the machine acceptance
line FX-04 named it as checked (`docs/ACCEPTANCE-MACHINE.md`, corrected).

**What is built** (`src/CnCNew.Presentation/Fx/AttachedEffects.cs`; the original's mechanisms read in the sources of Zero Hour and rewritten):

| Mechanism | In the original | Here |
| --- | --- | --- |
| `ParticleSysBone = <bone> <system>` of a condition state | `W3DModelDraw::setModelState`, `recalcBonesForClientParticleSystems`, `stopClientParticleSystems` | every system of the selected state of every draw module, at the bone of the state's model; all destroyed when another state is selected or the object is no longer shown; a bone the model lacks is the origin (as `Get_Bone_Index` 0) |
| the body's fire and smoke (`GameData.ini` `AutoFireParticle*`, `AutoSmokeParticle*`, `AutoAflameParticle*`) | `ActiveBody::updateBodyParticleSystems` on a change of the damage state or of `setAflame` | rebuilt on such a change for every object with an `ActiveBody` (or one of its kinds): at the bones PREFIX01 .. up to 16 of the current model, picked at random up to the category's maximum; aflame: fire one size up, smoke becomes small fire, every maximum doubled, the aflame category added; no bones, no systems |
| `BoneFXUpdate` / `BoneFXDamage` | `changeBodyDamageState`, `update`, `doParticleSystemAtBone` | the particle systems and FX lists of the slots of the current damage state, each after a random delay (min .. max, milliseconds in the file), once (`OnlyOnce:Yes`) or again and again; the systems end when the state changes |
| `TransitionDamageFX` particle systems and FX lists | `onBodyDamageStateChange` | on a worse damage state, at the bone, a random numbered bone (`RandomBone:Yes`) or the location; the systems of a state end when the object leaves it |
| a missile's exhaust (`ProjectileExhaust`, `VeterancyProjectileExhaust`) | `MissileAIUpdate::doIgnitionState` | attached to the projectile of a `MissileAIUpdate` after its `IgnitionDelay`, of the launcher's veterancy |
| projectile streams (`ProjectileStreamName`, `W3DProjectileStreamDraw`) | `ProjectileStreamUpdate`, `W3DProjectileStreamDraw::doDrawModule` | per launcher and weapon the projectiles in flight (at most 20), a hole where the aim changed, the newest `MaxSegments` points; handed to the renderer as `FxFrame.ProjectileStreams` |
| dust of a moving truck, tread debris of a moving tank (`Dust`, `TreadDebrisLeft/Right`) | `W3DTruckDraw`, `W3DTankDraw::doDrawModule` | a system attached to the vehicle while it moves, destroyed when it stands (the original pauses the same one); `DirtSpray`, `PowerslideSpray` and the speed scaling are not reproduced; the shipped tanks name no tread debris |
| `Model = NULL` | W3D's null render object | no model (like `NONE`) |

Every system is attached to its object with the bone as a local offset (`ParticleSpawn.LocalOffset`, the original's `setPosition` before `attachToObject`) and ends with
it. **Bones and muzzles:** `LiveGame` now answers `IFxWorldView.GetBonePosition` and `GetWeaponMuzzle` (before: always null, every shot's effect stood at the object's
origin): the rest-pose bone of the object's current model (`ModelBoneTable`, read from the W3D file when first asked for), scaled by the object's scale and turned with it;
the muzzle is the `WeaponFireFXBone` of the slot the object fires from now (the event names the weapon, not the slot). **Not reproduced (named):** bone rotations and
animated bones (`ParticlesAttachedToAnimatedBones`, `AnimatedParticleSysBoneClientUpdate`); the damage-type filters of `BoneFXUpdate` and `TransitionDamageFX`
(`DamageParticleTypes` - the presentation does not know the damage type of a change); a missile's exhaust ends with the missile, not when its fuel runs out; the texture
scroll of a stream; pausing an attached system while its object is shrouded (an object that is not shown loses its model-state and body systems and gets them again).
The other attached emitters of the original - `HelicopterSlowDeathBehavior`'s `AttachParticle`, the stealth detector's IR systems, the EMP's sparks, `OCL`
`CreateObject ... ParticleSystem` on debris (which is not created, F-36) - are not built.

**Test:** `Game.Tests` suite `AttachedEffectsTests` (staged scenes on Alpine Assault): the catalog of the shipped data (every slot read, every system it names exists, the
flame projectile's six pristine systems); a Dragon tank against infantry - the flame's trail in more than ten frames, its smoke, its stream through the projectiles; a
power plant damaged twice - fire and smoke at its bones; a barracks set on fire by the flame - fire at its bones in most of the frames it burns; a Tank Hunter's missiles -
their exhaust; `Model = NULL` loads no model; a rocket buggy - dust only once it drives. Counters: `AttachedEffects.Stats`, `MissingBoneNames` (the power plant's damaged
models lack bones their states name - the original then puts those systems at the origin too). **Not seen by a human.**

## Sound cues

`SoundCue(Tick, EventName, Kind, Position?, Entity, Player, Volume, Priority, Origin)` with `Kind` Effect, Voice, Eva,
Music or UiSound. Volume and priority come from the `AudioEvent` (`Volume` in percent, `Priority`
LOWEST..CRITICAL), otherwise 1 and NORMAL. The audio layer resolves the names and decides on the audibility for the
local player; `Player` is the player whom the reference addresses. `NoSound` and empty names produce no cue.

| Event | Cue | Reference |
|---|---|---|
| FXList `Sound` | Effect at the primary position | `SoundFXNugget` |
| `WeaponFired` | Weapon `FireSound` (Effect, source) | `FiringTracker::shotFired` |
| `VoiceFearRequested` | Object `VoiceFear` (Voice) | `ActiveBody::attemptDamage` |
| `ProductionCompleted` + `EntitySpawned` in the same tick | `VoiceCreated` and `UnitSpecificSounds.VoiceCreate` (Voice) | `ProductionUpdate::update` |
| `ConstructionCompleted` | `VoiceTaskComplete` of the builder (Voice) | `DozerAIUpdate` |
| `ConstructionStarted` | MiscAudio `MoneyWithdrawSound` (UiSound) | `Money::withdraw` |
| `SupplyDeposited`, `StructureSold`, `ConstructionCancelled` | MiscAudio `MoneyDepositSound` (UiSound) | `Money::deposit` |
| `UpgradeCompleted` | `ResearchSound`, otherwise EVA `UpgradeComplete`; additionally `UnitSpecificSound` | `ProductionUpdate::update` |
| `SpecialPowerUsed` | `InitiateSound` at the source, `InitiateAtLocationSound` at the target object | `SpecialPowerModule::aboutToDoSpecialPower` |
| `EntityContained` / `EntityUncontained` | `SoundEnter` / `SoundExit` of the container | `OpenContain::onContaining/onRemoving` |
| `EntityDied` (not self-inflicted) | EVA `BuildingLost` (STRUCTURE + MP_COUNT_FOR_VICTORY) or `UnitLost` (INFANTRY/VEHICLE) | `Object::onDie` |
| `EntityDamaged` by others | EVA `BaseUnderAttack` + radar structure sound, units radar structure/harvester sound, otherwise the general radar sound; per player at most once per 250 units and 10 s | `Radar::tryUnderAttackEvent`, `Radar::tryEvent` |
| `CommandRejected` | MiscAudio `NoCanDoSound` (a rule of our own) | – |
| `CaptureFlash` | MiscAudio `DefectorTimerTickSound` as a world sound at the position of the building that is being captured (no player index); if the key is missing, the cue carries the key name and is counted by the audio layer as an unknown event | `SpecialAbilityUpdate::continuePreparation` (`SpecialAbilityUpdate.cpp`) |

The radar distance test of the reference has a bracket error; the intended distance is implemented. The reference
plays the structure sound for attacked non-harvester units; adopted.

EVA events from `Eva.ini` that the simulation does not trigger yet: `LowPower`, `InsufficientFunds` (arises in the
command layer), all `Superweapon*` events, `BeaconDetected`, `Enemy/Own*Detected` (heroes), `AllyUnderAttack`
(needs the local player and alliances), `GeneralLevelUp`, `VehicleStolen`, `BuildingStolen`, `BuildingBeingStolen`,
`BuildingSabotaged`, `CashStolen`. Also not derivable: `InitiateAtLocationSound` for location targets (the event
carries no location), production-start withdrawals (no event), unit announcements on commands (`VoiceMove`,
`VoiceAttack`, `VoiceSelect` – a task of the input layer C3 based on `ObjectFxInfo.Sounds`).

`SoundCueRateLimiter(SoundCueLimits)`: per event name at most `MaxPerEventName` cues per `WindowTicks` (default 3
per 10 ticks), at most `MaxPerTick` (48) per tick, positioned Effect/Voice cues beyond `CutoffDistance` (2D,
0 = off) from the listener are dropped. Dependent purely on order and ticks; a counter for every kind of rejection.

### What the attached effects cost (Phase 36)

Measured with `PresentationTool sync-perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --hud` (60 Hz, eight computer players,
the binaries of `1d45cfd` - before the attached effects - and of `31b03c2`, alternating, two runs each, quiet machine, 17 October 2026); the same final world hash
`A4824DCBC874258B` in all four runs:

| per tick, mean | before | with the attached effects |
| --- | --- | --- |
| `SyncAfterTick` (the game layer's work after a tick) | 253.4 / 251.5 µs | 319.6 / 309.0 µs |
| of it `FxViewRefresh` (where `AttachedEffects.Observe` and `Update` run) | 17.2 / 17.4 µs | 45.0 µs (run 2) |
| of it `FxUpdate` (the particle systems) | 13.4 / 13.4 µs | 36.0 / 35.1 µs |
| the game layer alone (the tick without the simulation step) | 481.7 / 477.2 µs | 570.2 / 531.0 µs |
| at 60 Hz with one projection per tick | 2.9 % of one core | 3.4 / 3.2 % of one core |
| particles at most / at the end | 1,090 / 225 | 1,936 / 421 |

The attached effects cost about 55 to 65 µs per tick of eight players' match, a quarter of the game layer's work after a tick; the drawing of the particles in the
window is not part of this measurement.

## Audit (`PresentationTool fx-audit <content-dir>`)

Compiles the retail content (`--maps exclude`), builds the catalog and prints deterministically: counts of the FXLists,
nuggets per kind, particle systems per type, shader and priority, textures (name, then `.dds`, then `.tga` under
`Art/Textures/`; smudge systems excepted, because the heat-haze renderer never loads the texture), missing FXLists from
weapons, DamageFX and FXListAtBonePos, missing particle systems, unresolved graph references to FXLists, all
diagnostics by code (explained with a reason, or UNEXPLAINED) and an execution pass. Exit code 1 on
unexplained diagnostics.

Retail state (measured): 0 unexplained, 13 explained diagnostics:

- `fxlist-redefined` 2 (see above);
- `missing-particle-system` 6: `BattleMasterTransitionDebrisSmall` (2×), `BuggyTurretExplosion`, `CraterDebris`,
  `FireFieldSmall`, `BlackFireFieldSmall` – `reallyDoFX` does nothing in the release reference;
- `missing-attached-system` 1: `ScudMissleLauncherSmoke` (spelling error in the content);
- `particle-shader-none`/`particle-drawable-type` 1 each: `CarCrushGlassDebris` (DRAWABLE, shader NONE – invisible in the
  reference and skipped by the W3D particle renderer);
- `particle-velocity-none`/`particle-volume-none` 1 each: `TankStruckFlameSlave` (as a slave without emission of its own anyway).

Missing textures (5, explained: WW3D2 `missingtexture.cpp` replaces them, the particles are drawn nevertheless):
`EXDots.tga`, `EXRedSmokePuff.tga`, `EXSateliteHack1.tga`, `EXthunderRadar.tga`, `EXthunderWave.tga`. 74 of 79
texture names resolve only through the `.dds` substitution. No weapon and no DamageFX table refers to a missing
FXList. The three unresolved graph references to FXLists (`DieByFireUSA`, `FINAL` 2×) are typing leftovers of
module fields in the data layer and belong to its reference inventory.

Execution pass (`FxAudit.ExecuteAll`, seed 1): all 429 FXLists in object and position form, all 1,087
particle systems individually, 1,816 frames, peak 2,509 particles (of which ALWAYS_RENDER above the budget), 141,780 created
particles, 612 sound cues, checksum `4269BF8DD70C4247`, two runs identical. Run time in the real-data test about 3 s
(1.6–1.9 ms per frame with the budget fully used, Debug build).

## Tests

`tests/CnCNew.Presentation.Tests` (`FxTests`): random semantics, synthetic parsing of all nugget kinds and
template fields, unexplained diagnostics, hand-computed keyframes (alpha 0→1→0 over 10/20 frames, color, size with
damping, movement with gravity/damping/drift), emission/volumes/lifetime/initial delay, slave and
attached systems, all budgets, byte determinism, executor for all nuggets and events including DamageFX throttling,
camera shake, decal buffer, sound cue rules and rate limiter. With `--content-dir` additionally the real-data test
(0 unexplained diagnostics, every definition in the catalog, audit clean, pass identical twice).

## Limits

- DRAWABLE particles (1 system) are output as instances with `ParticleKind.Drawable`; the reference does not draw
  them. SMUDGE textures (`SMUD…`, 2 systems) need a heat-haze renderer (opacity from alpha, size);
  VOLUME_PARTICLE (4) is rendered by the reference with depth 1, i.e. like normal particles.
- No bone matrices: muzzles and bones are the rest-pose positions of the current model's bones (since Phase 35 supplied by `LiveGame`, "Attached effects" above); orientation = object.
- Shroud/fog: only as a hook; `SimulationFxWorldView.IsShrouded` is always false. The shroud test for attached
  systems when emitting is missing.
- DamageFX uses only the unconditional ArmorSet; `DamageFXOverride` and upgrade-dependent ArmorSets are missing, because the
  event carries neither damage type nor ArmorSet.
- Tracers have no real flight time; detonation effects of projectile weapons follow the projectile of the simulation (see `WeaponFired` above).
- `FireSoundLoopTime` (36 weapons, `FiringTracker::shotFired`: loop sound instead of single sound) is not reproduced;
  every shot produces a cue, which `SoundCueRateLimiter` throttles.
- `GameLOD.ini` levels (MaxParticleCount 500/1,500/3,000, MinParticlePriority, ParticleSkipMask) are not read;
  the caller sets `ParticleBudget` to match the chosen detail level.
- No new NuGet dependencies (only BCL, `System.Numerics`).

## Time and tick rate (Phase 21)

FX data are authored in **original frames** (1/30 s): lifetimes, delays, velocities, dampings and keyframes per frame (`FxValueReader.FramesPerSecond = OriginalFrame.PerSecond`, the one constant outside the simulation). The particle clock (`ParticleWorld.Step`, `FxExecutor.Frame`) therefore counts original frames, never simulation ticks.

- **Who drives the clock:** `LiveGame.SyncAfterTick` calls `Fx.ExecuteEvents` after every tick, but `Fx.Update` only on the ticks that begin an original frame (`World.Time.IsOriginalFrameBoundary`): at 30 Hz after every tick (unchanged), at 60 Hz after every second one. The clock thus hangs on tick and rate as an integer (no accumulator summed in `double`), stands still during pause and runs with the game speed. An effect that an event triggers between two frame boundaries begins with the next original frame – at most one tick later.
- **One clock for the throttling of the damage effects:** `DamageFX.ThrottleTime` is a duration in original frames; `FxExecutor.Damaged` compares it with event ticks. The duration is now converted into ticks through the time service of the world (`_time.OriginalFrames(throttle)`), the comparison runs uniformly on the simulation tick. For this, `FxExecutor` and `SoundCueDeriver` get the time service of the world in the constructor (no default value).
- **Sound cues:** `SoundCue.Tick` remains the simulation tick of the event. The lock of the under-attack message (`Radar::tryEvent`, 10 s) is `OriginalFrame.PerSecond * 10` original frames, compared in ticks of the world. The flood protection (`SoundCueRateLimiter`) counts per **time** instead of per tick: `SoundCueLimits.WindowFrames` (10 original frames) and `MaxPerFrame` (48 per original frame); `Filter(cues, ticksPerOriginalFrame, listener)` converts the ticks of the cues. A 60 Hz world thus lets through as many sounds per second as a 30 Hz world (`FxTests`, `RateLimiter`).
- **`FxAudit.ExecuteAll`** steps original frames and has no world; the time service is only passed through (`PresentationTool fx-audit` passes that of the original rate).

Unchanged at 30 Hz: checksum of the execution pass of `fx-audit`, digest of `sync-perf --digest` (contains every effect frame), the captures and sound recordings of the client (`docs/TICKRATE-MIGRATION.md` §14.2).

Time scale at 60 Hz (`tests/CnCNew.Game.Tests/TimeScaleTests.cs`, real data): after every tick the effect clock stands exactly on the original frame of the game time, and a particle system created by hand lives equally long at 30 and 60 Hz with the same maximum number of particles (numbers in `docs/TICKRATE-MIGRATION.md` §14.4). **Not verified:** what effects look like at 60 Hz; the particle picture changes 30 times per second, the objects 60 times.
