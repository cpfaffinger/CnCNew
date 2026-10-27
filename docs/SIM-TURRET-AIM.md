# Turrets in the simulation: aiming before the shot, attack state and idle scanning (`TurretAI`)

**Note (9 October 2026, Phase 23): historical, 30 Hz.** Tick counts, victory ticks and hashes in this document were measured at 30 Hz and at their state at the time and are superseded as reference values (authoritative: `baselines/reference-manifest.json`, at 60 Hz since `ea52f87`). Tick figures remain valid as a number of original frames. Above 30 Hz a turret decides its step once per original frame **of the turret** with the turn rate of the 30 Hz catalog and takes it in parts per tick; the angle is bit-identical at every frame end, the fire release falls into the same frame, `TurretWaits` counts frames, and bearing and pitch are read at the frame end of the victim: `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md` (`32eb25c`, `6e0c691`, `276a8de`, `dc888d3`), `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md` (`c87748a`), `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` (`43acce8`, `36a2404`). Open: the turret of an `AmericaFireBase` begins, in two matches on shipped maps at 60 Hz, to turn one tick instead of one frame after a look without result (not traced down to the symbol).

As of 4 October 2026 (measured values re-measured). Sources of the reference: `GeneralsMD/Code/GameEngine/Source/GameLogic/AI/TurretAI.cpp` and
`Include/GameLogic/TurretAI.h` (state machine, `friend_turnTowardsAngle`/`friend_turnTowardsPitch`, `TurretAIData::buildFieldParse`, `TurretAIIdleState`,
`TurretAIIdleScanState`, `TurretAIHoldTurretState`, `TurretAIRecenterTurretState`, `TurretAI::setTurretTargetObject`),
`AI/AIStates.cpp` (`AIAttackAimAtTargetState::onEnter/update`, `AIAttackFireWeaponState::update`, `AIAttackApproachTargetState::onEnter`, `AIAttackState::onEnter/update/onExit`),
`Object/Update/AIUpdate.cpp` (`getWhichTurretForCurWeapon`, `getTurretRotAndPitch`, `setTurretTargetObject`). None of it is copied; the sequence is understood and rewritten for the
command-driven combat simulation of this project.

## 1. Finding

The simulation knew no turret state. A `FireWeaponCommand` triggered the shot immediately as soon as weapon, range and vision were right; the presentation
(`TurretTracker`) turned the drawn turret only *after* the shot and guessed the direction in doing so. The scenario `tank-duel` (`PresentationTool scenario-audit`)
showed it: the Crusader fired in tick 5 with a turret that stood 45° beside the target.

In the reference the AI turns the turret first (`TurretAIAimTurretState`), and only when it is aligned does the state `TURRETAI_FIRE` fire
(`AIAttackFireWeaponState`) – for weapons on a turret the turret automaton fires, not the body (`AIAttackAimAtTargetState::update` always returns `STATE_CONTINUE` for turrets with a turn rate:
"The Body can never return Success if the weapon is on the turret").

The first version of this model had two gaps, which are now closed (sections 3.3 and 3.5): an attack was a stream of individual fire commands, the turret held its target only for
"readiness of the weapon + recentre time" after the last command and turned back after that, even if the unit kept attacking (reloading, approach). And a turret without a target stayed at rest,
although the data of about 40 turrets require a random looking around.

## 2. Data: `Turret`/`AltTurret` strictly typed (`CnCNew.Data`)

`Schema/TurretSchema.cs` contains **every** key of `TurretAIData::buildFieldParse`; the compiler (`DefinitionCompiler.CompileModule`) translates the
nested blocks `Turret`/`AltTurret` of implemented modules with it instead of with the heuristic generic path:

| Key | Type | Unit/meaning |
|---|---|---|
| `TurretTurnRate`, `TurretPitchRate` | Real | degrees per second (`parseAngularVelocityReal`) |
| `NaturalTurretAngle`, `NaturalTurretPitch`, `FirePitch`, `MinPhysicalPitch`, `GroundUnitPitch`, `MinIdleScanAngle`, `MaxIdleScanAngle` | Angle | degrees |
| `TurretFireAngleSweep`, `TurretSweepSpeedModifier` | weapon slot + Angle/Real, repeatable per slot | sweep angle / turn factor |
| `ControlledWeaponSlots` | Flags (`PRIMARY`/`SECONDARY`/`TERTIARY`), **mandatory** | `TurretAI::TurretAI`: "MUST specify controlled weapon slots" |
| `RecenterTime`, `MinIdleScanInterval`, `MaxIdleScanInterval` | DurationMs | milliseconds, rounded up to ticks |
| `AllowsPitch`, `InitiallyDisabled`, `FiresWhileTurning` | Bool | |

An unknown key or a missing `ControlledWeaponSlots` is a compiler error, not a silent default value. `InterTurretDelay` exists in the reference
only under `INTER_TURRET_DELAY`, which the shipped build does not define; no object sets it.

`baselines/compatibility.json` changes in exactly one entry: `compatibility-ignored-tokens` 132 → **208**. The 76 new cases are the annotations
`// turn rate, in degrees per sec` behind values of `TurretTurnRate`/`TurretPitchRate`; the reference parser reads the first token and ignores the rest, that is now
counted instead of being invisible. The number of baseline entries stays 751. Weapon fields were not newly typed: `AcceptableAimDelta` (Weapon/AcceptableAimDelta,
44 cases, still `deferred-field`) acts in the reference only on the **body** aiming of weapons without a turret (`AIAttackAimAtTargetState`), not on turrets; the turret
uses the fixed threshold `REL_THRESH` (see 3.4).

The scan fields (`MinIdleScanAngle`, `MaxIdleScanAngle`, `MinIdleScanInterval`, `MaxIdleScanInterval`) were already typed and merely not read in the catalog; the data do not change for this,
`IniTool compat-check` reports 751 entries unchanged.

## 3. Simulation

### 3.1 Template and state

* `Templates/TurretInfo.cs`: `TurretInfo` (a turret template in radians per tick and ticks) and `TurretSet` (main and second turret, `TurretsLinked` of the AI module);
  `ObjectTemplate.Turrets` is `null` for objects without a block. Missing keys have the constructor values of `TurretAIData` (0.01 rad per frame, two seconds of recentre time,
  scan interval 9,999,999 frames, no scan angle).
* `TurretInfo.Gates`: only a turret with a turn rate > 0 takes part in aiming (see 5). Whether it is switched on is state of the object (`TurretState.Enabled`, `TurretAI::m_enabled`;
  it starts as `InitiallyDisabled` says and is switched by `BattlePlanUpdate` and `DeployStyleAIUpdate` - F-57): a switched-off turret neither aims, scans nor turns, its weapons do
  not fire, and an object without `CAN_ATTACK` whose every weapon sits on one cannot attack at all (`Object::isAbleToAttack`: it acquires nobody, an order to fire is `NoWeapon`). `TurretInfo.CanScan`: `Gates`, a scan angle greater than 0 and a real
  interval (smaller than the default value); otherwise `TurretAIIdleScanState::onEnter` (`minA == 0 && maxA == 0` → immediately `STATE_SUCCESS`) never sees anything to do.
* `World/TurretState.cs`: per turret angle (relative to the vehicle direction), pitch, target, `HoldUntil`, weapon slot of the last command, `Rotating`, sweep state and, new, the attack state
  (`Persistent`, `Pursuing`, `LoseInterestAt`) and the scanning (`Scanning`, `ScanAngle`, `NextScan`, `ScanDraws`). `Entity.Turrets`, publicly readable as `Entity.TurretOf(index)` →
  `TurretReading(Angle, Pitch, Rotating)`. `SimulationWorld.AttackTargetOf(entity)` names the victim of the attack of a unit (target of its first turret with an attack state).

### 3.2 Phases (`World/SimulationWorld.Turrets.cs`)

The reference has a turret state machine (`IDLE`, `IDLESCAN`, `AIM`, `FIRE`, `HOLD`, `RECENTER`) that is controlled by the attack state of the AI. Here the phases follow from a few fields;
`StepTurrets` turns once per tick (after all movements of the tick, in entity id order) every turret that is not at rest or can scan:

| Phase | Condition | Behaviour | Reference |
|---|---|---|---|
| Aiming | target valid and (attack or time ≤ `HoldUntil`) | rotation by at most `TurretTurnRate` per tick towards the victim; if the remainder is below one step, the turret snaps in; `TURRET_ROTATE` as long as it has not arrived; pitch analogously | `TurretAIAimTurretState`, `friend_turnTowardsAngle/Pitch` |
| Holding | no target, time ≤ `HoldUntil` | the angle stays | `TurretAIHoldTurretState` (`m_timestamp = frame + RecenterTime`) |
| Recentring | no target, `HoldUntil` elapsed, angle ≠ rest angle | rotation to `NaturalTurretAngle`/`NaturalTurretPitch` at half rate; after that rest | `TurretAIRecenterTurretState` |
| Rest | rest angle, `HoldUntil` = 0 | nothing (scanning turrets: wait for `NextScan`) | `TurretAIIdleState` |
| Scanning | `Scanning` | rotation to rest angle + `ScanAngle` at half rate; arrived → holding | `TurretAIIdleScanState` |

A turret of an object under construction (`UNDER_CONSTRUCTION`) neither recentres nor scans (`TurretAIRecenterTurretState::update`, `TurretAIIdleScanState::update` wait there).

### 3.3 Attack state: the target stays until the attack ends

**Finding of the reference.** An attack command puts the unit into `AIAttackState`. Its sub-automaton (`AttackStateMachine`: approach `AIAttackApproachTargetState`, aiming `AIAttackAimAtTargetState`,
firing `AIAttackFireWeaponState`) calls `AIUpdateInterface::setTurretTargetObject(turret, victim)` in `onEnter` of the approach, in every `update` of the aim state and when re-choosing the victim, as long as it
runs, so also during approach and reloading. The turret stays in the state `AIM`/`FIRE` until `AIAttackState::onExit` sets the targets of all turrets to `NULL` (→ `TURRETAI_HOLD`, `RecenterTime`,
`RECENTER`, `IDLE`). `onExit` runs when the victim is dead (`AIAttackState::update`: `isEffectivelyDead` → `STATE_SUCCESS`), when the unit gets a new AI command (movement,
idle/stop, docking, entering, building …: the new state replaces the old one) or when the attack fails (no weapon window, victim no longer hostile, "no pursuit allowed" in the approach).
The turret itself aborts the aiming (`TurretAIAimTurretState::update` → `STATE_FAILURE` → `HOLD`) if `getAbleToAttackSpecificObject(ATTACK_CONTINUED_TARGET, …)` fails (victim in the fog,
stealthed, vanished); range plays no role for the *main target* (`isPrimaryEnemy`).

**Implementation.** The attack is the intention to fight the victim; it lives in the turrets (`TurretState.Persistent`, `Target`), because that is its only consumer. A turret without an attack state
behaves as before (one-off aiming, 3.4). Entry:

* an **accepted** `FireWeaponCommand` with origin player or AI (`FireOrigin.Player`/`Ai`; also `MissionSession` and `SkirmishAi`): the turrets of the chosen slot (all with `TurretsLinked`) get the victim.
  A rejected command (out of range, weapon reloading, victim invisible) starts nothing.
* a new **`PursueCommand`** (command table 12 → 13, replay format 9 → 10): movement to a point (X, Y) *in order to attack* a victim – `AIAttackApproachTargetState::onEnter` ("If we have a turret,
  start aiming"). The turrets of the weapon that the unit would use against the victim (`AttackSlotIgnoringRange`: like `ChooseWeapon` without the range and readiness filter) already follow the victim
  when it is still far out of range. The command also counts as "stand still and attack" (target = own position). It shoots nothing; shooting is still done by `FireWeaponCommand`.
  The command is new because a pursuit could otherwise not be distinguished from a real move command (both are `SetMoveTargetCommand`); the old command was not changed for it.
* not: `FireOrigin.Script` (a one-off button command is not repeated, behaves as before), module-driven shots (`TryFireWeaponSlot` among others) – both keep the old behaviour
  "readiness of the weapon + max(`RecenterTime`, 15 ticks) after the last command" (`HoldUntil`).

Duration: no expiry as long as the attack exists. It ends (`StopAiming` → target cleared, `HoldUntil` = now + `RecenterTime`, then recentring) on

| End | Implementation | Reference |
|---|---|---|
| Victim dead or vanished | in `StepTurret` in the same tick | `AIAttackState::update` (`isEffectivelyDead`), `friend_getTurretTarget` |
| new AI command to the unit | `IWorldServices.EndAttack(entity)`, called by `AIUpdateInterface.OrderMove`/`OrderIdle`/`OrderDock`/`StartTask` (movement, idle, docking, entering, building/repairing; so also script and module commands) | the new state replaces `AI_ATTACK` |
| Victim no longer attackable for the owner (`CanPlayerSee`: fog, stealthed and not detected, in a container) | checked per tick | `TurretAIAimTurretState::update` (`ATTACK_CONTINUED_TARGET`) |
| Victim outside the range of *every* weapon for longer than 45 ticks, **provided the unit does not pursue** (`Pursuing`) | `KeepsAttackIntent`, `LoseInterestAt` | `AIAttackApproachTargetState::onEnter` returns `STATE_FAILURE` for units without permission to chase (`isAllowedToChase`) |
| a new fire command at another victim | replaces the target | `AIAttackFireWeaponState`/`notifyNewVictimChosen` |
| the attacker dies | the turret is no longer stepped | – |

`Pursuing` remembers that the attack began with a `PursueCommand`; a later `FireWeaponCommand` at the same victim keeps it. A unit that pursues never gives up because of the distance; the
presentation (`StandingOrders`) ends its order when the victim vanishes or becomes invisible. Units whose attack consists only of fire commands (AI units, attack move) give up a victim
that is out of every range – moving or standing. The number 45 ticks is an approximation (short gaps between two pursue commands must not turn the turret back).

**Presentation.** `StandingOrders` (`Presentation/Input`) uses `PursueCommand` instead of `SetMoveTargetCommand` for the approach and for stopping within range and sends it once when the unit stands within
range but is reloading, without a turret already having a victim. Aircraft (have no turrets) still get `SetMoveTargetCommand`. `SimTool reference-match` likewise uses `PursueCommand` for approaching.

### 3.4 Fire release

`FireWeaponCommand` (execution in `SimulationWorld.Step` → `ExecuteCommands`): after weapon choice, vision and `IFireGate`, `AimTurretsAt(source, slot, victim, attack)` calls on the turrets that control the chosen
slot (all with `TurretsLinked`): each gets the target (`setTurretTargetObject`, called by `AIAttackAimAtTargetState`), and the weapon fires only if **all** are aligned:

* Angle: deviation from the *current* target direction ≤ `REL_THRESH` = 0.035 rad (≈ 2°, `TurretAIAimTurretState::update`); for a weapon with `TurretFireAngleSweep` < sweep angle
  (`turnAlignedToNemesis = fabs(angleDiff) < sweep`).
* Pitch (only `AllowsPitch` and rate > 0): difference from the desired pitch ≤ 2°. Desired pitch: `FirePitch`, otherwise the elevation to the target (from the top edge of the shooter, like `v.z -= maxHeightAbovePosition/2`),
  never below `MinPhysicalPitch`; with `GroundUnitPitch` an arc that grows with the distance to immobile or driving ground targets.
* Sweep: after a shot the sweep is active for 3 ticks (`ENABLE_SWEEP_FRAME_COUNT`); the turret then aims alternately ±sweep angle beside the target with `TurretSweepSpeedModifier` as turn factor.

If a turret is not aligned, the command stays **accepted** (no `CommandRejected` event, so no "NoCanDoSound" and no HUD message on every tick of the turning), no shot falls, and
`SimulationWorld.TurretWaits` counts it. After the shot `NoteTurretShot` extends the hold time for a one-off aiming (an attack does not need that) and switches the sweep on.

The same applies to the module-internal `TryFireActiveWeapon` (unused). `TryFireWeaponSlot` (modules that drive their attack sequence themselves and give up on a failure, e.g.
`AssistedTargetingUpdate`) aligns the turrets (one-off aiming), but does not wait.

### 3.5 Idle scanning (`TurretAIIdleState`, `TurretAIIdleScanState`)

The sequence of the reference for a turret without a target: `IDLE` waits a random interval `MinIdleScanInterval`..`MaxIdleScanInterval` (`resetIdleScan`: `GameLogicRandomValue`), then `IDLESCAN`:
random angle `MinIdleScanAngle + random(0, Max − Min)` with a random sign, rotation to `NaturalTurretAngle` + angle at half rate; arrived → `HOLD` (`RecenterTime`) → `RECENTER` →
`IDLE` with a new interval. An attack interrupts every step; after that it goes back to `IDLE` through `HOLD`/`RECENTER`.

Implementation as above (`ScheduleScan`, `IdleScanStep`, `ScanStep`):

* Scanning turrets (`TurretInfo.CanScan`) are always in the step directory. The time of the first scan is drawn at the first step of the turret (`NextScan = Tick + interval`) and anew after every
  recentring.
* **Randomness:** the reference draws from the common logic stream (`GameLogicRandomValue`), so that every resting turret shifts all other consumers. Here every draw is a hash of
  (world seed, entity id, turret index, draw counter `ScanDraws`) (SplitMix64 mixing, integer): the same inputs yield the same values, replays and snapshots agree, and
  `world.LogicRandom` is **not** touched (test: a world with and one without a scanning turret have the same state of the logic stream after 1500 ticks). The values differ from those of the
  reference, the distribution (uniform in the interval, uniform in the angle range, sign 50 : 50) does not. Order of the draws as in the reference: interval, angle, sign.
* The data: 38 `Turret` blocks set a scan interval (50 objects including the reskins of the generals have a scanning turret): the sentry drone (every 250 ms, up to 360°),
  Patriot battery and Gattling cannon (250 ms, 360°), the fire base (550–950 ms, 360°), strategy center (500–1000 ms, 60°; only while its turret is switched on, under bombardment), the technicals (5–10 s, 30–60°), the ECM vehicle (interval 0, exactly 180°,
  `RecenterTime` 0: it turns back and forth without interruption, as in the reference).
* Self-acquisition: `TurretAI::friend_checkForIdleMoodTarget` (a resting turret chooses a victim of its own accord, `AutoAcquireEnemiesWhenIdle`) has been part of the target acquisition of the simulation since Phase 17
  (`docs/SIMULATION-CORE.md` §33): a resting turret gets its victim from the unit, even if its body is driving; a victim interrupts the scanning like every attack.

### 3.6 Snapshot, hash, determinism

* `Entity.Describe` writes `turrets` only as long as a turret differs from the rest state (or can scan); likewise `turretWaits` in the world part only after the first wait. New and only when the state
  is set: `persistent`, `pursuing`, `loseInterestAt`, `scanning` + `scanAngle`, `nextScan`, `scanDraws`. Worlds without turrets (the 216,000-tick stress test) keep serialisation and hash.
* `WorldSnapshot.Version` stays **9**: the new fields are optional and are read only if present; a snapshot of the first version (without them) loads unchanged (its turret behaves
  as a one-off aiming). Runs with turrets nevertheless have different hashes (section 8).
* `CommandTypes.Version` 12 → **13** (`PursueCommand` = 31), `ReplayFormat.CurrentVersion` 9 → **10** (the new command has a payload: unit, victim, X, Y). Old replays fail at the
  command table (`ReplayRunner` checks it) and would have failed at the checksums anyway because of the changed combat times.
* All quantities are `Fixed`/integer; angles through `FixedTrig` (CORDIC `Atan2`, error < 1e-3 rad, far below the 2° threshold). No `float`, no wall time, no `Random`.

## 4. Presentation

`TurretTracker` (now `Presentation/Objects/TurretTracker.cs`) reads `Entity.TurretOf` and remembers only whether a turret was turning (`MODELCONDITION_TURRET_ROTATE` through `IsRotating`). `TurretMotion` (the
raw-text evaluation of the turret blocks), `TurretTracker.Observe` and `ObjectVisual.Turrets` are removed; the Stride layer (`ObjectLayerCore.ApplyTurret`) is unchanged, `DrawableInstance.TurretYaw/TurretPitch/
AltTurret*` come from the simulation state. With that the scanning follows as well without code of its own: drawn turret and `TURRET_ROTATE` come from the same state (the audit `tank-duel` checks it on
the sentry drone; the drawn picture can lag behind the simulation state by one tick).

## 5. Exceptions: what fires as before

| Case | Behaviour | Reason |
|---|---|---|
| Object without a `Turret` block (infantry, buildings without a turret, aircraft) | fires immediately | no `m_turretAI` |
| Turret with `TurretTurnRate = 0` (18 blocks of the retail data; the "fake turret" of the reference, e.g. of the Comanche) | fires immediately, turret stays centred | `AIAttackAimAtTargetState::update`: "incapable of turning, turn ourself" → body aims |
| A switched-off turret (`InitiallyDisabled = Yes`: the 4 Nuke Cannon blocks; the strategy center's turret, switched off when the center is created; a deploy-style unit with `TurretsFunctionOnlyWhenDeployed` while it is not set up) | does not aim, scan or turn; its weapons do not fire; without `CAN_ATTACK` the object cannot attack | `TurretAI::updateTurretAI` runs the turret's state machine only when enabled; `BattlePlanUpdate` switches it on under bombardment and recentres it before packing, `DeployStyleAIUpdate` when set up, centring it before packing with `TurretsMustCenterBeforePacking` (F-57; until Phase 40 such a turret fired without aiming and was never switched) |
| Slot that no turret controls (`ControlledWeaponSlots`) | fires immediately | `getWhichTurretForCurWeapon` = `TURRET_INVALID` |
| `FireOrigin.Script` (single command of a script button) | fires immediately, the turret turns nevertheless, **no** attack state | the command is not repeated |
| Module-driven shots (`TryFireWeaponSlot`, `FireCurrentWeaponAtPosition`, aircraft, projectiles) | as before (one-off aiming) | sequence of their own |
| Target as a position (ground attack) | no aiming | `FireWeaponCommand` knows only object targets |

`FiresWhileTurning` has no effect on the fire release in `TurretAI.cpp` (it controls only the end of the turning sound, `m_continuousFireExpirationFrame`); it is typed and read, but without consequence.

## 6. Known limits (honest)

* **The attack lives only in the turrets.** A unit without a turret has no attack state; whoever needs it would have to lift it onto the unit. The pursuit itself (driving to the victim, stopping within
  range, recomputing) is still done by the presentation (`StandingOrders`) or the command issuers; the simulation knows only "pursuit is running" (`PursueCommand`).
* **Approximations at the end of the attack:** giving up after 45 ticks outside every range instead of the state rules of the reference (`isAllowedToChase`, stealth, garrison, time in `AIAttackApproachTargetState`);
  the movement commands that the reference knows as a "new AI command" are those of the `AIUpdateInterface` methods (`OrderMove`, `OrderIdle`, `OrderDock`, building). `OrderEvacuate` without movement does not end
  the attack – as in the reference: `AIUpdateInterface::privateEvacuate` only calls `orderAllPassengersToExit` and does not change the AI state (with a target, `aiMoveToAndEvacuate`, it goes through
  `OrderMove` and ends it). A unit ability ends attack and movement: `SpecialAbilityUpdate::initiateIntentToDoSpecialPower` calls `aiIdle` ("Clear any old AI before starting this special
  ability"), implemented at the `UseSpecialPowerCommand` for every object with a `SpecialAbilityUpdate` of the power (the staged capture already did it itself; test `TurretAttackEndsWhenTheUnitStartsAnAbility`).
  Special powers without a `SpecialAbilityUpdate` (building powers, OCL powers) do not touch the AI state in the reference either.
* **Self-acquisition (implemented since Phase 17, before deliberately deferred):** `friend_checkForIdleMoodTarget` and `AutoAcquireEnemiesWhenIdle` are reproduced – not in the turret, but as an attack state of the
  unit (`Entity.Acquisition`, `World/SimulationWorld.Acquisition.cs`), which gives the turrets their victim (`AimTurretsForAttack`) and takes it from them again at the end of the attack (`StopTurrets`). The three command issuers
  (`MissionSession.DriveCombat`, `SkirmishAi.Fire`, `StandingOrders.FireAtNearestEnemy`) no longer choose victims; a victim that a command issuer names explicitly (`FireWeaponCommand`, `PursueCommand`) locks the
  unit's own target choice as long as such commands come. Rules, sources, approximations and measured values: `docs/SIMULATION-CORE.md` §33. Still not reproduced: the pursuit from idle (a resting unit
  of a computer player does not run after its victim; only in the attack move) and the answer of a passive unit to its last attacker.
* **Own random stream for the scanning:** the same distribution as the reference, different values (3.5). The scanning does not wait for activation by power, EMP or deactivation
  (`m_enabled` of the reference corresponds only to `Gates`).
* **Body does not aim:** weapons without a turret still fire without rotation of the body (`AcceptableAimDelta` stays `deferred-field`); driving units turn the body only through their movement.
* `DeployStyleAIUpdate`: `ALIGNING_TURRETS` (turret centred before packing up) is not entered; `TurretsFunctionOnlyWhenDeployed` is still implemented only through the fire gate (weapons).
* With `TurretsLinked` (3 of the 4 battleships) every weapon fires only when both turrets point; the reference lets the respectively aligned turret fire.
* The firing happens in the tick after the alignment (the command checks the state at the end of the previous tick against the current target direction); the reference fires one update later in the turret automaton.
* The visibility rule (an invisible victim ends the aiming) is tested with `STEALTHED` and with fog (`TurretAttackEndsWhenTheVictimFallsIntoTheFog`: the victim stays within range and unstealthed, the only object that saw it vanishes).
* Vision and presentation changes still need a visual check by a human (smoke tests prove reproducibility, not correctness).

## 7. Tests and audits

* `Simulation.Tests` (`Tests.Turrets.cs`): template model, default values and strict errors; rotation at 6°/tick and waiting time until tick +15; body direction; exceptions from section 5; recentring, rest state
  (no `turrets` in the snapshot), one-off aiming (script command) and dead target; two turrets with firing pitch, sweep, `TurretsLinked`; snapshot in the middle of the rotation and determinism; real data: Crusader (180°/s),
  Battlemaster, no turrets on the Ranger, four battleships with a second turret (3 linked), sweep weapons, duel Crusader against Battlemaster.
* `Simulation.Tests` (`Tests.TurretAttack.cs`): attack survives command gaps (300 ticks without a command) and a pursuit (drive past the victim, turret error ≤ 2.5° from tick 10, no command waits),
  a walking victim is pursued, a movement ends the attack, holding exactly `RecenterTime`, then recentring; dead victim; giving up out of range (standing 45 ticks, moving 45 ticks, pursuit never); dead attacker;
  a script command is no attack, a new victim replaces the old one; an invisible victim ends the aiming; a snapshot in the middle of the pursuit and of the scanning continues identically, `PursueCommand` in the replay;
  scanning: angle range, hold time `RecenterTime`, interval, both signs, deterministic, logic stream untouched, a different world seed different values, interruption by an attack and resumption;
  real data: Crusader pursues a Battlemaster (aim error 0.00° from tick 60, 7 shots, no command waits), sentry drone, Patriot and technical scan within their range (two runs equal), 50 scanning objects.
* `Presentation.Tests`: turret follows command/shot/recentring from the simulation (`TurretFollowsOrders`, recentring now after the death of the victim), `TURRET_ROTATE` through the projector (`TurretRotate`),
  standing orders (`PursueCommand` for the approach), real data: all turret blocks typed.
* `PresentationTool scenario-audit … --name tank-duel` (default run, output below): every shot with 0.0° angle error; **in every tick** between command and death of the victim the turret stays within 5° (Crusader from tick 13,
  Battlemaster from tick 6, 0 ticks outside), after the death of the Battlemaster the Crusader turret holds 45° exactly until tick 445 and turns back from tick 446 (death 384 + `RecenterTime` 60 ticks + 2), at rest from tick 460;
  the sentry drone scans 9 times (angles −170°…62°, each held 31 ticks with `RecenterTime` 30, exactly 8 ticks of rest in between = `MinIdleScanInterval` 8), the drawn turret agrees with the simulated one,
  `TURRET_ROTATE` is set in 518 of 537 turning samples (the others are the snapping-in steps). `capture-building` and `jet-strafe` stay green (`scripts/audit-scenarios.ps1`).

## 8. Measured values (Release, retail data, 4 October 2026)

| Run | before the first turret version | state of the first turret version (before) | now (attack state + scanning) |
|---|---|---|---|
| `SimTool reference-match` | winner FactionGLA, tick 13,890, final hash `76F22CB0CA79BEEF` | winner FactionGLA, tick 13,875, final hash `482FC8C52D507662`, chain `34AB50A397B0125C` | winner FactionGLA, tick **15,000**, final hash `01C01E3B91EAE18C`, chain `045DA5259A104F01`; run 2 and snapshot continuation identical |
| `SimTool stress` (216,000 ticks, synthetic, without turrets) | final hash `56882F763EABEF67`, chain `B8E663493F2C73D5` | unchanged | **unchanged** |
| `ai-match` China/GLA (Alpine Assault) | China, tick 43,275, final hash `757327AE69B87C2A` | China, tick 43,965, final hash `AE34F9421AA76C9A`, chain `DF948386BA2C5C98` | **stalemate** (no winner in 45,000 ticks; China still has buildings, GLA only arms dealer and two tunnel networks), final hash `BE5A7A4A37403928`, chain `1AF7BD86DED30A1B` |
| `ai-match` AmericaAirForce/China | stalemate, final hash `AF78F914D08B09DD` | stalemate, final hash `D31EB9C8679D6632`, chain `2536E3A7B8CB6620` | stalemate, final hash `21BC6E1596A8702E`, chain `4B231593B1ACA841` |
| `ai-match` America/GLA | America, tick 16,110, final hash `ED637C7C3E4FFF9E` | unchanged (`2B93BACCDB0F6450`) | America, tick 16,110, final hash `03CED6D83F6BD0A1`, chain `0F03F254211B30BA` (the fire bases scan) |
| `ai-match` ChinaTank/AmericaLaser (new) | – | – | stalemate, final hash `8C6F376D82A0F548`, chain `77C4A5F4F42006D7` |
| `CampaignTool audit --ticks 900 --campaign CHALLENGE_0` | – | 7/7 deterministic, 7/7 identical after save/load; elimination endings victory 7/7, defeat 6/7 | **unchanged**: 7/7, 7/7, victory 7/7, defeat 6/7 |

All `ai-match` runs: two independent runs and the continuation from a snapshot in the middle are identical to the tick. The hashes change for two reasons: turrets now follow their victim without interruption
(also between the shots and during the approach, hence different hit times), and all scanning turrets (Patriot, Gattling cannon, fire base, sentry drone …) turn when idle, which changes the state hash of every tick
even when no combat takes place; the stress test has no turrets and stays the same. For the reference match there is in addition that the scenario now sends its approach as a `PursueCommand`. The outcome of the
AI match China/GLA tips from "victory in tick 43,965" to "stalemate"; the cause is broken down in section 8a (the rule "a new command ends the aiming"; the match was already close to the tick limit before). Nothing was
changed for any other reason.

The values of the column "now" are superseded since the continuous-fire levels, the pre-attack delay, the damaged locomotors and the placement of the AI supply center; current reference values are in `docs/SIMULATION-CORE.md` §32.

## 8a. Breakdown of the AI shift (4 October 2026, measured)

What was open was why `ai-match` China/GLA on Alpine Assault has ended in a stalemate since the attack state (before, China at tick 43,965). The effects were switched off and on individually: a throwaway worktree
at the state `2b854d3` got a one-line patch per variant, the tool was built into a directory of its own per variant, then all 16 pairings from `SIMULATION-CORE.md` §19 ran
(`--max-ticks 45000`, default seeds). Each of the 128 runs (eight states) is identical with run 2 and the snapshot continuation.

| Variant | State | Patch |
|---|---|---|
| G | `8b8c8a7` (first turret version) | – |
| I | `2b854d3` | idle scan off, attack state off, `EndAttack` without effect |
| D | `2b854d3` | idle scan off, attack state off |
| B | `2b854d3` | idle scan off (`TurretInfo.CanScan` = false) |
| C | `2b854d3` | attack state off (`FireWeaponCommand` aims like a single shot, `TurretAimOrder.Shot`) |
| J | `2b854d3` | `EndAttack` without effect (a new command does not end the aiming) |
| A | `2b854d3` | – (`d713531` measured as well: hash-identical in all 16 runs) |

Result per run (winner and tick or stalemate, followed by the first eight digits of the final hash):

| Map | Pairing | G | I | D | B | C | J | A |
|---|---|---|---|---|---|---|---|---|
| Alpine Assault | China, GLA | China 43,965 `AE34F942` | China 43,965 `AE34F942` | stalemate `553A2186` | stalemate `553A2186` | stalemate `BE5A7A4A` | China 44,100 `A8EC7BF4` | stalemate `BE5A7A4A` |
| Alpine Assault | America, China | stalemate `63EEA99F` | stalemate `63EEA99F` | stalemate `63EEA99F` | stalemate `2616F0B2` | stalemate `131253B5` | stalemate `B748B8DD` | stalemate `B748B8DD` |
| Alpine Assault | GLA, America | America 12,120 `5BADFE2A` | America 12,120 `5BADFE2A` | America 12,120 `5BADFE2A` | America 12,120 `5BADFE2A` | America 12,120 `83FA72B7` | America 12,120 `83FA72B7` | America 12,120 `83FA72B7` |
| Alpine Assault | ChinaTank, AirForce | stalemate `36BD9041` | stalemate `36BD9041` | stalemate `36BD9041` | stalemate `F6C1297B` | stalemate `61CB3CC2` | stalemate `710439C5` | stalemate `710439C5` |
| Armored Fury | China, GLA | stalemate `1DA176CF` | stalemate `1DA176CF` | stalemate `1DA176CF` | stalemate `F8B0885E` | stalemate `7B9BC049` | stalemate `6F0CA3F1` | stalemate `6F0CA3F1` |
| Armored Fury | America, China | America 41,130 `12495B5C` | America 41,130 `12495B5C` | America 44,325 `871BA9DA` | America 35,175 `C8675A5A` | America 40,875 `D8620C00` | stalemate `4035F8F0` | America 36,375 `D3A4AAD6` |
| Armored Fury | GLA, America | America 22,410 `B5CD779E` | America 22,410 `B5CD779E` | America 22,410 `B5CD779E` | America 22,410 `B5CD779E` | America 22,410 `29601DCA` | America 22,410 `29601DCA` | America 22,410 `29601DCA` |
| Armored Fury | ChinaTank, AirForce | AirForce 9,405 `2261402C` | AirForce 9,405 `2261402C` | AirForce 9,405 `2261402C` | AirForce 9,405 `2261402C` | AirForce 9,405 `DF5D46B1` | AirForce 9,405 `DF5D46B1` | AirForce 9,405 `DF5D46B1` |
| Bitter Winter | China, GLA | stalemate `C9F239FC` | stalemate `C9F239FC` | stalemate `C9F239FC` | stalemate `C9F239FC` | stalemate `94F023A5` | stalemate `2C65DC45` | stalemate `2C65DC45` |
| Bitter Winter | America, China | stalemate `D660891B` | stalemate `D660891B` | stalemate `D660891B` | stalemate `A64316B6` | stalemate `C9D7E892` | stalemate `781E31A8` | stalemate `781E31A8` |
| Bitter Winter | GLA, America | America 14,565 `752A8CA8` | America 14,565 `752A8CA8` | America 14,565 `752A8CA8` | America 14,565 `752A8CA8` | America 14,565 `3922E188` | America 14,565 `3922E188` | America 14,565 `3922E188` |
| Bitter Winter | ChinaTank, AirForce | stalemate `4F07AF52` | stalemate `4F07AF52` | stalemate `4F07AF52` | stalemate `E61AB63B` | stalemate `92F1F0A9` | stalemate `75A58BD4` | stalemate `75A58BD4` |
| Death Valley | China, GLA | China 8,805 `DFB8BB3F` | China 8,805 `DFB8BB3F` | China 8,805 `DFB8BB3F` | China 8,805 `DFB8BB3F` | China 8,805 `4F687B3B` | China 8,805 `4F687B3B` | China 8,805 `4F687B3B` |
| Death Valley | America, China | stalemate `B27C7202` | stalemate `B27C7202` | stalemate `B27C7202` | stalemate `B27C7202` | stalemate `82DEB8C7` | China 35,985 `4A4BF443` | China 35,985 `4A4BF443` |
| Death Valley | GLA, America | stalemate `2C7EE0E2` | stalemate `2C7EE0E2` | stalemate `2C7EE0E2` | stalemate `C0947735` | stalemate `8252C18F` | stalemate `41808E02` | stalemate `41808E02` |
| Death Valley | ChinaTank, AirForce | stalemate `2BF5DE1C` | stalemate `2BF5DE1C` | stalemate `2BF5DE1C` | stalemate `2BF5DE1C` | stalemate `A619B93E` | stalemate `5532EEC9` | stalemate `5532EEC9` |

Findings:

* **The decomposition is complete.** Variant I is hash-identical with the first turret version G in all 16 runs. Between the two states there are therefore exactly three effective changes for AI matches:
  the idle scan, the persistent attack state and – so far not named as an effect of its own – **"a new command ends the aiming"** (`EndAttack` in `OrderMove`/`OrderIdle`/`OrderDock`/build order;
  it also acts on the one-off aiming, which is why D differs from G).
* **`PursueCommand` plays no role in `ai-match`:** `SkirmishAi` never sends it (only `StandingOrders` of the presentation and the reference scenario). In the reference match, by contrast, it is the main effect:
  without it (`SetMoveTargetCommand` for the approach) it ends at tick 15,225 instead of 15,000, without all three effects at tick 13,875 like the first turret version; the idle scan changes nothing there
  (final hash and chain equal), the attack state of the fire commands only the chain.
* **China/GLA on Alpine Assault tips solely through "command ends the aiming":** with the rule stalemate (A, B, C, D), without it China at tick 43,965 (G, I) or 44,100 (J). In this match the attack state has
  no influence (A = C, B = D), the idle scan changes only the hash. The match was already 1,035 ticks away from the limit before; one lost opportunity to shoot (the turret of a unit that gets a new
  march command holds its angle instead of following the last target) is enough for the shift.
* **No systematic effect:** G, I, J and A decide 7 of 16 runs, B, C and D 6; individual close matches tip in both directions (Death Valley America/China: stalemate in G, China at 35,985 in A;
  Armored Fury America/China: America between 35,175 and 44,325, stalemate in J). 13 of the 16 pairings have the same outcome in every variant.
* **The stalemates have a different cause.** In every stalemate both sides end up with 0 to a few hundred credits and few objects (the map supplies are exhausted, 2–4 attack waves in 45,000 ticks):
  a finding of the skirmish AI (economy, attack threshold), not of the turrets. It has existed since Phase 15 (8 instead of 10 decided runs after the pathfinding around standing units) and belongs to Phase 17.

**Decision:** no behaviour correction to hold time, visibility rule or idle scan. "A new command ends the aiming" is the behaviour of the reference (`AIAttackState::onExit` sets the turret targets to `NULL`
as soon as another AI state replaces the attack); taking it back would repair an AI result by falsifying a verified behaviour. The idle scan during an attack is already excluded
(a target interrupts it). What is missing compared with the reference is the target acquisition by the unit itself (attack move, `friend_checkForIdleMoodTarget`): there a marching unit does not lose its target
permanently, because it looks for the next one itself. Until Phase 16 the command issuer took that over in the next thinking step.

**Addendum Phase 17 (5 October 2026):** the target acquisition by the unit and the attack move are implemented (`docs/SIMULATION-CORE.md` §33). "A new command ends the aiming" applies unchanged
(`EndAttack` now also clears the attack state of the unit); the unit then looks for the next victim itself: in the attack move two frames after the command, otherwise as soon as it rests again. The 16 AI runs are
re-collected there; the columns of this section describe the state before that.
## 9. Follow-up in other documents (not part of this change)

`docs/SIMULATION-CORE.md` (weapon choice approximations "turret and pitch conditions are missing", section on the weapon choice), `docs/PRESENTATION-OBJECTS.md` (line 47 `TURRET_ROTATE`, section on `TurretYaw`: "zero, because the simulation
keeps no turret angle"), `docs/MODEL-CONDITIONS-UNITS.md` (lines on `TurretTracker`), `docs/CLIENT-OBJECTS.md`, `docs/DEMO-FINDINGS.md`, `README.md`/`ROADMAP.md`/`IMPLEMENTATION-PLAN.md`
(snapshot version 9, turret state, command table 13, replay 10) and `CLAUDE.md` (reference values) still describe the state without a turret state or without an attack state respectively.
