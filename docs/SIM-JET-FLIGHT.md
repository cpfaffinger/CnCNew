# Fixed-wing aircraft: minimum speed, turn radius, circling and the airfield geometry

**Note (9 October 2026, Phase 23): historical, 30 Hz.** Tick counts, victory ticks and hashes in this document were measured at 30 Hz and at their state at the time and are superseded as reference values (authoritative: `baselines/reference-manifest.json`, at 60 Hz since `ea52f87`). Tick figures remain valid as a number of original frames. Above 30 Hz, wing flight and the airfield states decide once per original frame with the constants of the 30 Hz catalogue (`LocomotorFrames`) and fly the step in fractions; a jet sortie is bit-identical over 1,500 frames at 30, 60 and 120 Hz: `docs/rate-invariance/RATE-INVARIANCE-FLIGHT.md`, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md` (`bbee8ab`, `60fcb57`, `346cadc`, `cc7fd75`). Not measured: airfields with model bones and several jets on one runway at 60/120 Hz.

As of 4 October 2026 (re-measure). Concerns `CnCNew.Simulation` (`JetAIUpdate`, `AIUpdateInterface`, `ParkingPlaceBehavior`,
`LocomotorInfo`, `ModelGeometry`), `CnCNew.Game` (`ModelGeometryLoader`, `GameData`), `CnCNew.Presentation` (`StandingOrders`)
and the audits (`PresentationTool scenario-audit`, `W3dTool bones`). The occasion was the visual check of the scenario `jet-strafe`
(`docs/DEMO-SCENARIOS.md`).

## 1. Starting point (measured before the change)

`PresentationTool scenario-audit … --name jet-strafe` failed: after the attack each Raptor stayed motionless for 32 and 37 ticks respectively
at a height of 118 m (speed 0) before it flew back. Causes in the code:

* `AIUpdateInterface.Move` ended a flight movement with `Entity.Speed = 0` at the target; an aircraft with `MinSpeed > 0` could therefore
  stand still and stop in the turn (`goalSpeed = 0` within the turning circle).
* An idle, flying `JetAIUpdate` object held its position in the air (comment in the source: "holds its position
  in the air instead of circling").
* The airfield knew no model points: `ParkingPlaceBehavior` computed a synthetic layout (runway `b + 80` beside the
  building, `3a` long). The Raptor taxied over grass roughly 154 units south of the airfield and took off there (measurement of the
  human, reproduced with the audit: take-off at (−43, −154), 128 units from the real runway).

## 2. Reference

`references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Locomotor.cpp`:

| Function | Content |
| --- | --- |
| `LocomotorTemplate::friend_parse` post-processing | "WINGS should always have positive minSpeeds (otherwise, they hover)" |
| `moveTowardsPositionWings` | passes on to `moveTowardsPositionOther` (the optional altitude circling `CIRCLE_FOR_LANDING` is switched off) |
| `moveTowardsPositionOther` | turns with `rotateTowardsPosition` by at most the turn rate, pushes along the facing direction, target speed never below `MinSpeed`; closer to the target than the braking distance: `MinSpeed` |
| `maintainCurrentPositionWings` | holds a position by circling: target point on a circle of radius `CirclingRadius` around `m_maintainPos`, rotated by `π − π/8` (negative radius: mirrored) from the bearing angle to the centre, speed `MinSpeed`; radius 0 → `calcMinTurnRadius` |
| `calcMinTurnRadius` | `MinSpeed / TurnRate` (huge for locomotors that cannot turn) |
| `locoUpdate_maintainCurrentPosition` | `m_maintainPos` is the position at which the holding begins |

`MinTurnSpeed` is not evaluated for `LOCO_WINGS` (only `moveTowardsPositionWheels`); the turn rate applies at every speed.
`ParkingPlaceBehavior.cpp` (`buildInfo`, `calcPPInfo`): all parking, taxiway and hangar points are bones of the building model
(`getSingleLogicalBonePosition`).

## 3. Implementation of the flight physics

**Affected objects:** only objects with `JetAIUpdate` **and** a locomotor with `Appearance = WINGS` (Raptor, Stealth Fighter,
Aurora, MiG including the `AirF_`/`Boss_`/`CINE_`/`Lazr_`/`Nuke_`/`SupW_`/`Tank_`/`Infa_` variants, the cruise missile
`CINE_U06_CruiseMissileFLY` of the cutscenes: 27 objects in the retail content). Not affected: helicopters (`Comanche`, `JetAIUpdate`
without WINGS), `DeliverPayloadAIUpdate` (B-52, A-10, B-3: despite the WINGS locomotor), Spectre gunships, drones, birds and missiles
(`AIUpdateInterface`/`MissileAIUpdate`). The switch is `AIUpdateInterface.FliesWithWings` (virtual, only `JetAIUpdate` answers
yes, and only as long as the aircraft is allowed to fly, `AllowAirLoco`; taxiing with the taxi locomotor stays as it was).

**Movement** (`AIUpdateInterface.MoveWinged`/`SteerWinged`, only without a path planner, straight ahead as before for air):

* The heading is turned towards the target by at most `TurnRatePerTick` per tick.
* The speed approaches the target speed with `Acceleration`/`Braking` and always stays `≥ MinSpeed`.
  Target speed: `Speed` (or the group speed), within the braking distance `(v² − MinSpeed²) / (2·Braking)` to the target `MinSpeed`.
* The aircraft advances in the facing direction by its speed (no stopping in turns, no instant turn).
* **Arrival** without stopping: the command ends (`HasMoveTarget = false`, `MoveArrived`) when the target is closer than `max(CloseEnoughDist, Speed)`
  **or** lies behind the aircraft within twice the turning circle (`2 · Speed / TurnRate`; a point there cannot be
  approached – the reference lets the aircraft circle around it). The speed is not touched in the process.

**Circling** (`JetAIUpdate.CircleStep`, `maintainCurrentPositionWings`): a flying aircraft without a command (phase `None`, `AwaitLanding`
– it is waiting for the runway –, `CirclingDeadAirfield`) steers for the point on the circle. Centre: the target of the last
finished command, otherwise the position at which the circling began. Radius `CirclingRadius` (positive clockwise, negative
counter-clockwise, 0 = `MinSpeed / TurnRate`). **Measured:** the track settles at roughly `0,93 · Radius` (Raptor: 92.8 at radius 100), in
agreement with an independent Python simulation of the same rule; radius 0 yields the smallest circle (27–30 for the
test jet). The INI statement "pos = clockwise" is correct (the direction of rotation was measured from the cross product). A new command (`OrderMove`, return flight,
landing) ends the circling; the return to the airfield with empty ammunition, the return flight after `ReturnToBaseIdleTime` and the landing
remain unchanged.

**State/snapshot:** `circling`, `circleX`, `circleY` are only written as long as the aircraft is circling, and are read optionally
(`reader.Has`); there is no version bump (`WorldSnapshot` 8, Replay 8, `CommandTypes.Version` 11 unchanged). The new
`LocomotorInfo` fields (`IsWings`, `CirclingRadius`) are in the catalogue, not in the state. `CirclingRadius` remains a
deferred field (`baselines/compatibility.json` unchanged): the general grammar types it by its name as `Distance`; the
binding accepts every number form (`decimal`, `long`, `Distance`) and rejects anything else with `TemplateBuildException`.

**Attack overflight (presentation, `StandingOrders`):** commands are single steps; the attack command is issued anew every tick. A
fighter flies over its target (movement to the target, firing in the weapon band), and without intervention gets stuck in the circle around the target
(circle radius ≈ 93 lies below `MinimumAttackRange` 100: it could never fire again). Therefore `AttackRunEgress` sends an aircraft with
a WINGS locomotor that has flown over the target (target behind it, still within weapon range) straight on by one weapon range,
and only afterwards lets it approach the target again. When all weapons that only reload at the airfield (`JetAIUpdate.IsOutOfSpecialReloadAmmo`)
are empty, the attack command ends (a move command to its own position makes the aircraft idle, the return is handled by
`JetAIUpdate` as before). Test (`JetOrderTests`, synthetic, 2400 ticks): 12 overflights, 133 shots all in the band 90…330, egress of
≈ 372 each, never below the minimum speed; the missile aircraft ends the attack with the last missile.

## 4. Airfield geometry from the model bones

The simulation must not read model files (layering). Chosen (and why): an **injected, fingerprint-secured table**
that the game layer reads from the skeletons when loading.

* `CnCNew.Simulation.Templates.ModelGeometry` (`AirfieldLayout`, `BonePose`): per airfield object, for each space `Runway{c}Parking{r}`,
  `Runway{c}Park{r}Han`, `Runway{c}Prep{r}`, per runway `RunwayStart{c}`/`RunwayEnd{c}`, optionally `HeliPark01`; position in the object frame
  (x forward, y left, z up) and heading of the bone. `TemplateCatalog.Build(graph, bindings, geometry = null)` accepts it
  (unknown names: `TemplateBuildException`); without a table (synthetic fixtures, `SimTool`, `CampaignTool`) the computed
  layout remains (`ParkingPlaceBehavior` header comment).
* `CnCNew.Game.Data.ModelGeometryLoader` (`GameData.Load`): for every object with `ParkingPlaceBehavior` load the model of the
  default state, rest pose with identity root (`W3dSkeleton.RestWorldTransforms`, the "pristine bones" of `W3DModelDraw`), every
  missing bone is an error. Rounded to 1/16 unit and 1/1024 rad so that a last bit of the float matrix products changes neither the table nor
  the fingerprint. In the retail content: 9 airfields, 2 models (`ABArFrcCmd`, `NBAirfield`), each with 2 × 2 spaces and 2 runways.
* **Identity:** `ModelGeometry.Fingerprint` (SHA-256 of the canonical text, raw values) is folded with `Combine` into the definition fingerprint
  against which world, replay and saved game are checked (`GameData.Fingerprint`). The table is reproducible (loading twice is
  byte-identical, test `ModelGeometryTests`) and contains no paths or timestamps. Without a table the fingerprint remains unchanged.
* `ParkingPlaceBehavior.InfoOfSpace` (reference `calcPPInfo`): parking space = parking bone (or hangar bone with `ParkInHangars`), `RunwayPrep` =
  prep bone, start/end = runway bones of the column, approach point `0,75` runway lengths behind the end at `ApproachHeight`, orientation = heading
  of the bone. If rows/columns or the name do not match the table, the module throws `InvalidDataException` (no silent fallback).
* Take-off and landing: the aircraft taxis to the start of the runway, accelerates along the runway and climbs with the square of the fraction of the
  runway rolled through to `ApproachHeight` (reference: `m_maxLift · ratio²`); the landing glides onto the end of the runway and rolls out to the start (mirror image).
  The runway of the real models is short (`ABArFrcCmd`: 79 units), the aircraft takes off after ≈ 25 units of rolling distance (audit) and reaches the approach height at the end of the runway.

**Height on the ground.** The reference puts parked and taxiing aircraft at the height of their airfield (`LandingDeckHeightOffset` applies only
to carriers; `AmericaAirfield` has 0). The simulation now takes the **airfield height** (`ParkingPlaceBehavior.ApronHeight`: object height + deck offset)
instead of the terrain height under the aircraft; the model of the apron lies at the height of its origin. On the test map (Alpine Assault)
the terrain there is flat (18.8 at all 13 × 8 samples), so the audit value is the same before and after: **a sinking-in could not be measured in the
simulation.** Measured on the model (`W3dTool bones AVRaptor`, "mesh extent"): in the rest pose the Raptor reaches 1.95 units
**below** its origin (z −1.95 … 5.31); on the ground it is therefore stuck that deep in the apron. This is a presentation problem. Raising the simulation height
would be wrong (`IsSignificantlyAboveTerrain` threshold ≈ 0.64: parked jets would become air targets, and `onDie` of the airfield
would no longer distinguish them from flying ones); the lift belongs in the drawing (Stride layer, e.g. by the model draught for aircraft on the ground).

## 5. Tests and audits

* `tests/CnCNew.Simulation.Tests/Tests.JetFlight.cs`: minimum speed/no standing still/helicopter unchanged; turn rate and loop; circling
  (clockwise, counter-clockwise, smallest circle, weak brake); snapshot in the middle of the circle; airfield life cycle with WINGS locomotor.
  `Tests.AirModelGeometry.cs`: hand-made bone table (hangar, parking, taxiing, take-off on the runway line, landing, snapshots with folded
  fingerprint, wrong dimensions and unknown name are rejected). `Tests.JetFlightReal.cs` (`--content-dir`): real Raptor, MiG,
  Stealth, Aurora fly a square (track without gaps, speed in `[MinSpeed, Speed]`, turn rate observed, afterwards circling around the last
  point), Comanche and B-52 unchanged, two runs identical.
* `tests/CnCNew.Presentation.Tests/JetOrderTests.cs` (section 3), `tests/CnCNew.Game.Tests/ModelGeometryTests.cs` (`--content-dir`: nine
  airfields, bones of `AmericaAirfield` against `W3dTool bones`, fingerprint folded, loading twice identical).
* `PresentationTool scenario-audit … --name jet-strafe`: in addition to the hover check, checks the minimum speed in the air, the taxi height,
  that the take-off, the whole take-off roll and the landing lie on a runway line of the model (tolerance 12) – with `--synthetic-airfield`
  (airfield without model bones) exactly these checks fail (6 errors, take-off 128/183 units beside the runway), with the bones they pass.
* `W3dTool bones <content> <modell> [präfix…]` prints bone positions/headings and the model extent of the rest pose.

## 6. Measured values (4 October 2026, Release, before = state `c54aa9a`)

| Measurement | before | after |
| --- | --- | --- |
| `SimTool reference-match` | winner FactionGLA, tick 13,890, final hash `76F22CB0CA79BEEF`, chain `35F54CAD40A43E6E` | identical |
| `SimTool stress` (216,000 ticks) | `56882F763EABEF67`, chain `B8E663493F2C73D5` | identical |
| `SimTool ai-match` Alpine Assault, `FactionAmericaAirForceGeneral,FactionChina`, 45,000 ticks | stalemate, final hash `AF78F914D08B09DD`, chain `68A8ABB3A7BCFF0C` | identical, deterministic, snapshot continuation identical |
| the same with swapped sides | stalemate, `2B3E66FDB102E504`, chain `084775445341A724` | identical |
| `CampaignTool audit --campaign CHALLENGE_5 --map GC_AirGeneral --ticks 900` / `--map GC_Stealth` | 1 mission deterministic, identical after save/load, 0 missing hits | identical (same numbers) |
| `scenario-audit … --name jet-strafe` | 2 errors (hovering 32/37 ticks); Raptor 1 died | everything passed: no hovering, lowest flight speed 60.0 to 65.3/s, both Raptors take off on the runway, fire 4 missiles, land, reload |
| Baseline of the compatibility list | 751 entries | 751 (no field newly typed) |

The hashes of the runs stay the same because no fixed-wing aircraft flies in these scenarios (the skirmish AI builds no jets; the checked
campaign maps do not deploy any in 900 ticks). The flight change takes effect in `jet-strafe`, in the flight tests and in the client. The fingerprint
of the client world (`GameData.Fingerprint`) changes because of the bone table (new saved games/replays; old ones no longer fit).
For the scenario `jet-strafe` the target of `trooper2` was moved from `raptor1` to `raptor2`: both Missile Defenders shot at the same
jet and killed it; this way the sequence "take off, fire, fly back, land, reload" stays checkable for both jets.

## 7. Known limits

* The flight path is kinematic (no physics model with lift/roll/pitch); the circling has the property of the reference (chasing a point on a circle),
  not its numbers to the float digit. Damaged locomotors (`SpeedDamaged`, `TurnRateDamaged`, `AccelerationDamaged`, `LiftDamaged`) have taken effect since 4 October 2026 from `MovementPenaltyDamageState` (`docs/SIM-FIRING-TRACKER.md` §4), also on the selected jet set.
* The cruise missile `CINE_U06_CruiseMissileFLY` is a `JetAIUpdate` object with a WINGS locomotor and now circles at the target instead of standing still.
* The landing still aligns the heading with the start of the runway (no approach arc of its own); the approach point is only roughly kept because of the turning circle. Deliberately deferred: an approach arc only changes the flight path of the last seconds and cannot be judged without a visual acceptance of the jet demo (`scripts/demo-jet-strafe.ps1`); it comes after this acceptance.
* Since 4 October 2026 `SimTool` and `CampaignTool` build the catalogue with the model bone table (`ModelGeometryLoader`, like `GameData.Load`); their world fingerprint matches that of the client.
  The reference game stayed tick- and hash-identical in the process (no airfield in the scenario).
* The sinking-in on the ground (model draught 1.95) can only be fixed in the drawing (section 4).
* Playability by a human is not verified by this; smoke tests and audits prove reproducibility, not correctness.
