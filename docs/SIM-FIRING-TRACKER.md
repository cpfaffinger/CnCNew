# Continuous-fire levels, pre-attack delay and damaged locomotors in the simulation

**Note (9 October 2026, Phase 23): historical, 30 Hz.** Tick counts, victory ticks and hashes in this document were measured at 30 Hz and at their state at the time and are superseded as reference values (authoritative: `baselines/reference-manifest.json`, at 60 Hz since `ea52f87`). Tick figures remain valid as a number of original frames. Above 30 Hz the continuous-fire levels drop one frame (not one tick) after the deadline, a clip without reload time waits one frame, and the flash phase of the capture runs once per frame (4 / 4 / 4 flashes); damaged locomotors have their own quantities per frame: `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md` (`165e6d8`, `20d4766`, `d923cea`), `docs/rate-invariance/RATE-INVARIANCE-GROUND.md`.

As of 4 October 2026 (measured values re-measured). Sources of the reference: `GeneralsMD/Code/GameEngine/Source/GameLogic/Object/FiringTracker.cpp` (`shotFired`, `update`, `speedUp`, `coolDown`),
`Object/Weapon.cpp` (`Weapon::preFireWeapon`, `getPreAttackDelay`, `getStatus`, `WeaponTemplate::getPreAttackDelay`), `Object/Object.cpp` (`preFireCurrentWeapon`, `adjustModelConditionForWeaponStatus`),
`AI/AIStates.cpp` (`AIAttackFireWeaponState::onEnter/update/onExit`), `Object/Locomotor.cpp` (`getMaxSpeedForCondition`, `getMaxTurnRate`, `getMaxAcceleration`, `LocomotorTemplate::validate`),
`Object/Update/SpecialAbilityUpdate.cpp` (`initiateIntentToDoSpecialPower`, `continuePreparation`). None of it is copied; the sequences are understood and rewritten.

## 1. Finding

Three behaviours of the weapons and of the movement existed only as a picture or not at all:

* `CONTINUOUS_FIRE_SLOW/MEAN/FAST` was derived by the presentation from the shot events (`UnitConditionTracker`); the simulation did not know the levels. The Gattling tank spun the barrel faster, but never
  fired faster (`WeaponBonus = CONTINUOUS_FIRE_MEAN RATE_OF_FIRE 200%` of the weapon had no effect).
* `PreAttackDelay` (29 retail weapons: 250 ms to 3,000 ms, 20 per shot, 5 per attack, 4 per clip) was not read: every shot fell in the tick of the command. `PREATTACK_A/B/C` could therefore not be a model condition.
* `SpeedDamaged`, `TurnRateDamaged`, `AccelerationDamaged`, `LiftDamaged` (507 lines in `Locomotor.ini`) were not read: heavily damaged units drove and flew like new ones.

## 2. Continuous-fire levels (`SimulationWorld.FiringTracker.cs`)

State per object (`FiringState`, present only while something is set): consecutive shots, victim, cool-down tick, "slow". The levels "mean" and "fast" are the weapon bonus conditions
`CONTINUOUS_FIRE_MEAN`/`CONTINUOUS_FIRE_FAST` of the object (`Entity.WeaponBonusConditions`, exclusive, not cumulative); the rate of fire thus follows from `WeaponInfo.DelayBetweenShotsWith` without further code.

| Sequence | Implementation | Reference |
|---|---|---|
| Shot at the same victim | counter + 1 | `shotFired` |
| Shot at another victim within the coast time | counter + 1, new victim ("we will not spin down") | `shotFired` (`now < m_frameToStartCooldown`) |
| otherwise | counter = 1 | `shotFired` |
| Cool-down tick | next possible shot + `ContinuousFireCoast`; without coast time 0 (no timeout) | `getPossibleNextShotFrame() + coast` |
| Counter > `ContinuousFireOne` | no level → mean | `speedUp` |
| Counter > `ContinuousFireTwo` | mean → fast | `speedUp` |
| Counter below the threshold of the current level | back to nothing, "slow" set | `coolDown` |
| Tick after the cool-down tick | level drops directly to nothing ("straight to zero"), "slow" for one second, then the end | `update`, `coolDown` |

The tracker is notified **after** the shot (`Object::fireCurrentWeapon`): the shot that reaches a level is still timed with the old delay. Tested (`FiringTrackerSpinsUpCoastsAndCoolsDown`):
intervals 18, 18, 18, 18, 9, 9, 9, 4 … ticks at 600 ms, `ContinuousFireOne = 3`, `ContinuousFireTwo = 6`; a target change within the coast time keeps the level; after the last shot 30–36 ticks fast, 31 ticks
slow, then nothing; a snapshot in the middle of the run continues identically.

The presentation reads the level from the simulation (`SimulationWorld.ContinuousFireOf`); the `UnitConditionTracker`'s own counting is removed, the levels now survive loading a saved game.

Not implemented (parts of the same tracker of the reference): the continuous-fire sound loop (`FireSoundLoopTime`, sound), `AutoReloadWhenIdle` (`m_frameToForceReload`) and the bonus `TARGET_FAERIE_FIRE`.

## 3. Pre-attack delay (`PreAttackDone`)

`WeaponInfo.PreAttackDelayTicks` and `PreAttackType` (`PER_SHOT` – the constructor value –, `PER_ATTACK`, `PER_CLIP`; an unknown value is an error when the catalog is built). Both keys remain `deferred-field` in `CnCNew.Data`
and are read from the source text of the field like `ContinuousFireOne` (baseline unchanged).

A `FireWeaponCommand` (player or AI) whose weapon would otherwise fire – selected, in range, ready, turret aligned – starts the wind-up instead: slot, victim and end tick are in the `FiringState`,
the command remains **accepted** (no `CommandRejected`), no shot falls. A later command of the same attack fires as soon as the end tick is reached. The delay is dropped
(`Weapon::getPreAttackDelay`: "Look for a reason to return zero")

* for `PER_CLIP`, if the clip has been started,
* for `PER_ATTACK`, if a consecutive shot has already fallen on this victim (the counter from section 2; it is therefore kept for such weapons as well),

and it scales with the weapon bonus `PRE_ATTACK`. Cancellation: a new command to the unit (`EndAttack`, like `AIAttackFireWeaponState::onExit`: `setPreAttackFinishedFrame(0)`), another victim, or – as
an approximation, because the simulation does not see the end of an attack without a command – one second without a follow-up command after the end tick. A one-off script button command (`FireOrigin.Script`) and module-driven shots
(`TryFireWeaponSlot`, `TryFireActiveWeapon`, aircraft) fire as before without a wind-up.

`SimulationWorld.PreAttackSlotOf(entity)` names the slot that is winding up; from it `ConditionDeriver.Weapon` sets `PREATTACK_A/B/C` with `USING_WEAPON_A/B/C`. Tested (`PreAttackDelayWindsUpBeforeTheShot`,
`UnitConditionTests.PreAttack`): per shot 15 ticks before every shot (shots at 15 and 60), per attack only before the first shot at a victim and again after a target change, per clip before the first of three
shots; a move command discards the wind-up; snapshot in the middle of the wind-up; picture sequence `PREATTACK_A > FIRING_A > BETWEEN_FIRING_SHOTS_A`.

## 4. Damaged locomotors

`LocomotorInfo.Damaged` carries the constants with `SpeedDamaged`, `TurnRateDamaged`, `AccelerationDamaged`, `LiftDamaged` (a missing key keeps the normal value, like `LocomotorTemplate::validate`; if all are missing, there
is no damaged form). `AIUpdateInterface.ActiveLocomotor` supplies them as soon as the body is at least as damaged as `GameData.ini MovementPenaltyDamageState` (retail and constructor value: `REALLYDAMAGED`);
jets first choose their set (taxiing, attack, return flight) and then its damaged form. Tested (`DamagedLocomotorSlowsAReallyDamagedObject`): unchanged at 50 % health, one third of the distance at 20 %.

Not covered: objects that do not read their locomotor through `AIUpdateInterface` (projectiles, the Spectre gunship, `WorldEffectModules`).

## 5. Ability ends movement and attack, flashing of the capture

* `SpecialAbilityUpdate::initiateIntentToDoSpecialPower` calls `aiIdle` ("Clear any old AI before starting this special ability"): every unit ability (a `SpecialAbilityUpdate` for the power used) stops the
  unit and ends its attack; before, only the staged building capture did that (`docs/SIM-TURRET-AIM.md` §6).
* `SimEventKind.CaptureFlash` (building, capturing unit): `continuePreparation` with `DoCaptureFX` increases a phase per tick by one third of the elapsed share of the preparation; every time its integer
  part changes from odd to even, the building flashes. With 60 ticks of preparation four flashes with intervals 11, 8, 8 ticks (`StagedCaptureFlashesTheBuilding`). Color and sound are a matter of the presentation
  (`docs/SIM-STAGED-CAPTURE.md`).

## 6. Snapshot, hash, versions

`FiringState` is written only while it differs from the idle state (`firing`), the pre-attack fields only during the wind-up, the flash phase only during a capture; `WorldSnapshot.Version` stays **9**,
older snapshots load unchanged. Command table (13) and replay format (10) are unchanged. Runs with continuous-fire or pre-attack weapons have different hashes than before.

| Run | before (`2b854d3`) | now |
|---|---|---|
| `SimTool reference-match` | winner FactionGLA, tick 15,000, final hash `01C01E3B91EAE18C`, chain `045DA5259A104F01` | after continuous-fire levels and pre-attack tick 14,490, `1104C4CD97A0D4CA`; with damaged locomotors tick **13,500**, final hash `0946FB676DEC615C`, chain `EA6D12F180930D71` |
| `SimTool stress` (216,000 ticks, synthetic) | final hash `56882F763EABEF67` | **unchanged** |

## 7. Known limits

* The wind-up does not know the attack state of the reference: its end without a command is approximated by one second without a follow-up command; `LeechRangeWeapon` (range stays valid during the wind-up) is missing.
* `PreAttackDelay`/`PreAttackType`/`ContinuousFire*` are still typed generically in `CnCNew.Data` (`deferred-field`); the catalog checks them strictly when it is built.
* The visual and audible effect (faster barrel, wind-up animation, flashing) needs the visual check by a human.
