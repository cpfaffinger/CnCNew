# 30 Hz defect: the lost wake request of the slow death

As of 8 October 2026, measured at the state `71e41d7` (fix) on top of `37b36b8` (`main`). Third measurement round of Phase 23, case `timed` of the
rate-equivalence suite (`docs/rate-invariance/RATE-INVARIANCE-TIMING.md` section 5, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`, `docs/TICKRATE-MIGRATION.md` §15.4).
The fix changes the behaviour at 30 Hz and is, by the decision of the project owner of 8 October 2026, one cause in a commit of its own;
the reference manifest is **not** rewritten in this branch.

## 1. Finding

`timed/lifetime CINE_BShipWeaponDetonationObject`, quantity "creation to end": – / 0.0667 / 0.0667 s at 30 / 60 / 120 Hz. At 30 Hz the object never leaves the
world. The rates differ because 30 Hz is wrong.

## 2. Mechanism

The object (`Data/INI/Object/AmericaMiscUnit.ini`) has `LifetimeUpdate` with 1 ms (one original frame) and `SlowDeathBehavior` with `DestructionDelay = 1` (one frame).
Sequence at 30 Hz, T = tick in which the object is created:

1. `SimulationWorld.AttachModules` (`src/CnCNew.Simulation/World/SimulationWorld.cs:1750`) wakes every update module that has not set a wish of its own
   for T + 1. The `SlowDeathBehavior` has none (`_nextWake = -1`), so it is due in T + 1. The `LifetimeUpdate` has scheduled itself in `OnSpawn` for T + 1
   (one frame).
2. Tick T + 1, `RunUpdates` (`SimulationWorld.cs:1348`): both modules of the same object are due; the order is wake time, object ID, module index
   (`CompareUpdates`), so first `LifetimeUpdate` (module 2), then `SlowDeathBehavior` (module 3).
3. `LifetimeUpdate` → `world.Kill` → `OnEntityDied` → `SlowDeathBehavior.OnDie` → `SlowDeathLottery.Run` → `BeginSlowDeath`
   (`src/CnCNew.Simulation/Modules/BuiltIn/Phase5BlockerModules.cs:288`): `_destructionTick = T + 2`, `world.WakeAt(Entity, this, T + 2)`.
4. `SimulationWorld.WakeAt` (`SimulationWorld.cs:1373`), line 1382: the module is still due in the running tick (`module.NextWake == _tick`) and the request
   says "next tick" (`requested == _tick + SleepNone`) – the request is discarded ("already awake").
5. Still in T + 1 the update of the `SlowDeathBehavior` runs. Before the fix: deadline not reached, return value `SleepForever`. `RunUpdates` sets `NextWake`
   to "never" and takes the module out of the heap. Nobody wakes it again: the dead object stays in the world.

At 60 and 120 Hz the deadline is T + 1 + 2 or T + 1 + 4, so not "the next tick"; `WakeAt` moves the module to the deadline, the object ends
two frames after its creation (0.0667 s).

The same gap without a lifetime: an object is killed in the tick after it appears, before its `SlowDeathBehavior` has run for the first (and only) time
– by a command (`ExecuteCommands` comes before `RunUpdates`), by a world intervention between two ticks (`_tick` then already points to the
tick in which the module is due) or by the update of an object with a smaller ID – and the drawn `DestructionDelay` is at most one frame
(`AtLeastOneFrame`: a value of 0 as well). After the first update a `SlowDeathBehavior` sleeps forever, then every wake request takes effect.

`BattleBusSlowDeathBehavior` carried a copy of the same update for the real death (`VehicleModules.cs`, branch `_realDeath`). There the module is due not only in the
first tick, but in every tick of the throw (first death) and every 15 frames while looking at the empty hulk.

## 3. The original

- `GameLogic::friend_awakenUpdateModule` (`GeneralsMD/Code/GameEngine/Source/GameLogic/System/GameLogic.cpp`): the same three special cases as `WakeAt`, in
  the same order – a call from the module's own `update()` is ignored ("it will be ignored, in favor of the return code from update"), for the same time there is
  nothing to do, and "if we already awake, and someone calls setWakeFrame(self, UPDATE_SLEEP_NONE), we don't want to reset our wake frame, since that would
  prevent us from getting called THIS frame". The contract: a module that still runs in this frame decides with the return value of its `update()`
  (`GameLogic::update`, loop over `m_sleepyUpdates`: `u->friend_setNextCallFrame(now + sleepLen)`).
- `SlowDeathBehavior::SlowDeathBehavior` (`…/Object/Behavior/SlowDeathBehavior.cpp`): `setWakeFrame(getObject(), UPDATE_SLEEP_FOREVER)` – the module sleeps
  from the constructor on and is **not** due in the first frame of the object.
- `SlowDeathBehavior::beginSlowDeath`: `m_destructionFrame = d->m_destructionDelay + GameLogicRandomValue(0, variance)`, then
  `setWakeFrame(obj, UPDATE_SLEEP(whenToWakeTime))` with the smallest of the three counters (sinking, midpoint, destruction), after that `m_destructionFrame += now`.
- `SlowDeathBehavior::update`: returns `UPDATE_SLEEP_NONE` in every branch until the destruction, so it runs every frame from the start of the slow death;
  `if (now >= m_destructionFrame) { doPhaseStuff(SDPHASE_FINAL); TheGameLogic->destroyObject(obj); }`.
- `LifetimeUpdate::LifetimeUpdate` / `update` (`…/Object/Update/LifetimeUpdate.cpp`): `setWakeFrame(UPDATE_SLEEP(delay))` with `delay >= 1`, then
  `getObject()->kill()`.
- `BattleBusSlowDeathBehavior::update` (`…/Object/Behavior/BattleBusSlowDeathBehavior.cpp`): "If a real death, we aren't allowed to do anything" –
  `return SlowDeathBehavior::update();`.

In the original the gap therefore does not arise: the module sleeps until `beginSlowDeath`, is woken there and stays awake until the destruction. For the object of the
finding: death one frame after creation, destruction one frame later – two frames, 0.0667 s, the value of the suite at 60 and 120 Hz.

## 4. Cause and fix

The scheduler is not the cause. `WakeAt` is, line by line, that of the original, and its contract ("the update of this frame decides") applies to
all modules. The cause lies in the module: of the slow death it models only the time of destruction, therefore sleeps until then (the original stays awake),
but held the deadline **only** as a wake request and answered "forever" from an update before the deadline. In addition, it is – unlike in the original – due at all in the
first tick of the object.

Fix (commit `71e41d7`, one cause): the update returns the remainder until the deadline.

```csharp
internal static class SlowDeathTimer
{
    public static long Update(IWorldServices world, Entity entity, bool activated, long destructionTick)
    {
        if (!activated) return SimulationTiming.SleepForever;
        if (world.Tick < destructionTick) return destructionTick - world.Tick;
        world.RequestDestroy(entity.Id);
        return SimulationTiming.SleepForever;
    }
}
```

`SlowDeathBehavior.Update` and the branch of the real death in `BattleBusSlowDeathBehavior.Update` call it (in the original this is one function). An update
before the deadline existed so far only in the lost case; all other sequences are unchanged (measured: section 7).

**Rejected: a general change to `WakeAt`** (remembering the discarded request and applying it as an upper bound after the update, or always
accepting the request). Reasons: (a) it would deviate from the scheduler of the original, and for all modules – "already awake" protects precisely the call in this tick; (b) it
would change `nextWake` (part of the state hash) everywhere a request is discarded today, i.e. the behaviour and all reference values at 30 Hz without
a named defect; (c) the review of all callers and the measurement (section 5) find no loss apart from the slow death. Also rejected:
letting the module sleep from creation as in the original – that fixes the first tick, not the bus, and changes the state of every object with
a slow death in its first tick (all hashes) without a gain in behaviour.

## 5. All callers of `WakeAt`

A request can only be lost if (1) the target module is due in the running tick and has not yet run, (2) the request says exactly "next tick"
and (3) the update of the module returns a longer pause in this tick without having done what was requested. 68 calls in the source text (64 in modules, 4 in
the world):

| Kind of request | Callers | Loss possible? |
|---|---|---|
| `world.Tick` ("now") | `AIUpdateInterface` (`OrderMove`, `OrderIdle`, `OrderDock`, `OrderEvacuate`, `StartTask`), `HackInternetAIUpdate` (`StartHacking`, `OrderMove`), `DockUpdateBase` (2), `GarrisonContain`, `HealContain`, `HelixContain`, `OverlordContain`, `TunnelContain`, `DeliverPayloadAIUpdate`, `SupplyCarrierAI`; world: production (`SimulationWorld.cs:1020`), construction completion (`SimulationWorld.Construction.cs:274`) | no – "now" never falls under "already awake"; a module that has already run runs once more in the same tick |
| `SleepForever` (put to sleep) | `BaseRegenerateUpdate`, `AutoHealBehavior.StopHealing`, `PoisonedBehavior.OnHealing`, `ToppleUpdate`, `StealthDetectorUpdate.SetEnabled(false)`, `OverchargeBehavior.Enable(false)`, `SpyVisionUpdate` (duration 0) | no |
| `Min(NextWake, tick + turret frame)` | world: target acquisition (`SimulationWorld.Acquisition.cs:221`, `:317`) | no – runs after `RunUpdates`; if the module is due, the request equals its wake time |
| "next tick" after a state change that the update reads immediately | `NeutronMissileUpdate`, `SpectreGunshipUpdate`, `BridgeBehavior`, `BridgeScaffoldBehavior`, `CleanupHazardUpdate`, `AssistedTargetingUpdate`, `RiderChangeContain`, `HijackerUpdate`, `JetSlowDeathBehavior`, `HelicopterSlowDeathBehavior`, `NeutronMissileSlowDeathBehavior`, `BattleBusSlowDeathBehavior` (first death), `CountermeasuresBehavior`, `ParticleUplinkCannonUpdate`, `HackInternetAIUpdate.DisableUntil`, `AIUpdateInterface.ParalyzeUntil`, `DemoTrapUpdate`, `FireWeaponPower`, `FireWeaponWhenDamagedBehavior`, `FlammableUpdate`, `StealthDetectorUpdate.SetEnabled(true)`, `StealthUpdate`, `PhysicsBehavior`, `DumbProjectileBehavior`, `MissileAIUpdate`, `OverchargeBehavior.Enable(true)`, `SlavedUpdate`, `SpecialAbilityUpdate`, `StickyBombUpdate`, `ProjectileStreamUpdate`, `StructureCollapseUpdate`, `StructureToppleUpdate`, `DeployStyleAIUpdate` (2), `ToppleUpdate`, `WaveGuideUpdate`, `SpyVisionUpdate.SetDisabledUntilFrame` (wake-up call) | no – if the request is discarded, the update runs in this tick, sees the new state and stays awake with its return value (one tick earlier than requested: "as soon as possible", as in the original) |
| Deadline that can be exactly one tick; the update computes its pause from the state | `PoisonedBehavior.OnDamage` (`CalcSleepTime`), `FlammableUpdate` (`CalcSleep`) | no |
| Deadline that can be exactly one tick; the update acts when called | `SupplyWarehouseCripplingBehavior.OnDamage`, `BaseRegenerateUpdate.OnDamage`, `AutoHealBehavior.TryActivate`, `FireSpreadUpdate.StartFireSpreading` | no loss; with a deadline of one frame and a module due in the same tick it takes effect one tick earlier (as in the original, the same construction there) |
| Deadline that can be exactly one tick; the update before the deadline answered "forever" | **`SlowDeathBehavior.BeginSlowDeath`**, **`BattleBusSlowDeathBehavior.BeginSlowDeath` (real death)** | **yes – the defect; fixed** |
| the same construction, exactly so in the original | `SpyVisionUpdate.ActivateSpyVision` (without `SelfPowered`: the update before `m_deactivateFrame` returns `UPDATE_SLEEP_FOREVER`), `SpyVisionUpdate.SetDisabledUntilFrame` with one frame | theoretically, with a duration of exactly one frame and a module due in the same tick; the data know only 20,000 and 240,000 ms. Faithful to the original, not changed |

Counter-measurement instead of review: a probe in the scheduler (built only for this measurement, not in the repo) counted over the full reference manifest (50 runs) every discarded request after which the update of the same tick wanted to sleep longer than one tick. After the fix: **not a single
occurrence**, for no module type. The second counter – updates of the slow death before the deadline, i.e. exactly the cases lost earlier – is in section 7.

## 6. Affected objects in the retail data

Census (`Tests.RealLostWakeCensus`, all instantiable objects with `SlowDeathBehavior` or `BattleBusSlowDeathBehavior`, a small world of its own
per object): 616 objects, of which 230 with a `DestructionDelay` of at most one frame in at least one module. "Dead and never removed":

| Staging | 30 Hz before | 30 Hz after | 60 / 120 Hz after |
|---|---|---|---|
| left to itself (10 s, then longest deadline + 5 s) | 6 | 0 | 0 |
| killed in the tick after appearing | 73 | 0 | 0 |
| killed there and once more in the next tick | 73 (the same ones) | 0 | 0 |

Before, the census is measured only at 30 Hz (the test aborts at the first rate with a finding).

- **By itself** (lifetime of one frame): `CINE_BShipWeaponDetonationObject`, `CINE_CruiseMissileDetObject`, `CINE_DemoTrapDetonationObject`,
  `CINE_MiniNukeExplosion`, `CINE_SandExplosionDetonationObject`, `DemoTrapDetonationObject`. None is named by a weapon or creation list of the INI data;
  the `CINE_` objects are created by the campaign scripts (section 7).
- **Killed in the first tick** (73, the six included): Humvee, medic, POW truck, troop transport, combat cycle, rocket buggy, radar van, each
  with their general variants, the five fake buildings of the GLA with variants, `ConvoyTruckArmedWithNuke`, `RemoteC4Charge`, `TimedC4Charge`,
  `TNTStickyBomb` – all with a `DestructionDelay` of 0 or one frame. In the game that requires a hit exactly in the tick after appearing; in the 50 runs
  of the manifest it does not occur.
- **Battle Bus:** in the retail data (no `DestructionDelay`, i.e. one frame) not hit by the census; the case is re-enacted on the synthetic fixture
  (`LostWakeABattleBusKilledWhileItLooksAtItsHulkEnds`), an occurrence in the game is not verified.

## 7. What changes at 30 Hz

`SimTool reference-manifest check … --set full --jobs 5` at the state of the fix: **3 of 50 runs deviate**, 47 are identical (reference match,
stress test, all 16 AI runs, the three 8-player runs, the skirmish overview and 24 of 27 mission runs):

| Run | Chain hash manifest | Chain hash after (the first reported deviation per run) |
|---|---|---|
| `mission_MD_GLA01` | `E764DA1050962851` | `0121F74708E56C61` |
| `mission_MD_USA02` | `2993A73F066BEDDE` | `CE9CE37430A7C4AD` |
| `mission_MD_USA05` | `628D7208A23B10AB` | `B944F0ECC6A49186` |

Result quantities (`reference-manifest results`, binaries of `main` against those of the fix, all 50 runs): 11 values differ, all in these three
runs, all of the same kind – more destroyed, fewer remaining objects:

| Run | `EntityDestroyed` | Objects at the end | per player |
|---|---|---|---|
| `mission_MD_GLA01` | 374 → 381 | 1,093 → 1,086 | player 1: 42 → 36, player 9: 11 → 10 |
| `mission_MD_USA02` | 138 → 148 | 1,780 → 1,770 | player 14: 10 → 0 |
| `mission_MD_USA05` | 68 → 79 | 1,869 → 1,858 | player 1: 1,092 → 1,084, player 5: 156 → 153 |

Outcome, final script, executed scripts and actions, shot counts, all other event counts and the command streams (22 files, byte-identical) are
the same. The probe names the objects (84 updates before the deadline counted; `MissionJob` computes the first half of every mission world three times): in the three
mission runs together 28 cutscene objects – 11
`CINE_CruiseMissileDetObject`, 9 `CINE_DemoTrapDetonationObject`, 4 `CINE_BShipWeaponDetonationObject`, 4 `CINE_MiniNukeExplosion` – which so far stayed dead in the
world and are now removed two frames after their creation (7 + 10 + 11 = 28).

The reference manifest thus has to be rewritten in exactly these three runs (a commit of its own by the project owner). The input manifest is identical.

Objective driver (`CampaignTool drive`, 24 maps, 30 Hz, binaries of `main` against those of the fix): victory 24/24 and defeat 24/24 before as after,
on every map the same final script at the same tick (deviation 0.00 s).

## 8. Measurements

| Measurement | before (`37b36b8`) | after (`71e41d7`) |
|---|---|---|
| Suite, case `CINE_BShipWeaponDetonationObject`, "creation to end" | – / 0.0667 / 0.0667 s | 0.0667 / 0.0667 / 0.0667 s |
| Suite in total outside the tolerance (60 / 120 Hz), 4,307 quantities | 35 / 33 | 34 / 32 |
| … `locomotion`, `flight`, `projectiles`, `turrets` | 2/2, 0/3, 4/4, 4/0 | 2/2, 0/3, 4/4, 4/0 |
| … `timed` | 1 / 1 | **0 / 0** |
| … `ai` | 24 / 23 | 24 / 23 |
| … `weapons`, `overtime`, `economy`, `scripts` | 0 / 0 | 0 / 0 |
| Report file of the suite (4,307 lines) | – | exactly one line different (that of the finding); all values at 30, 60 and 120 Hz otherwise the same, including those of the AI matches |
| Tests `Lost wake: …` (synthetic fixture, 30/60/120 Hz) | 3 of 3 fail at 30 Hz ("never left the world") | pass |
| Census of the retail data | 6 / 73 / 73 at 30 Hz | 0 / 0 / 0 at 30, 60 and 120 Hz |
| Simulation tests without content, 30 Hz, `--tick-rate 60`, `--tick-rate 120` | not re-measured (green at the state `3b44506`) | green |
| Simulation tests with content, 30 Hz | – | 520 pass, 5 fail: the five groups of the suite above |
| Simulation tests with content, `--tick-rate 60` / `120` | three and two real-data tests respectively fail (reference match without a winner at 60 Hz, Crusader turret, original skirmish AI) | the same ones with the same messages, otherwise only the five groups of the suite |
| `dotnet build CnCNew.slnx` | 0 warnings | 0 warnings |
| `input-manifest check` | – | identical |
| `reference-manifest check --set full` | 50 of 50 | 47 of 50 (section 7) |
| `reference-manifest check --set quick --verify-incremental` | – | 63,386 ticks checked, 0 deviations between incremental and full hash |

## 9. Not checked

- The `FINAL` effect of the slow death (`Weapon = FINAL …`, `OCL`, `FX`) is not modelled in the simulation; the fix removes the objects, the
  weapon of the object from the finding (`CINE_u02_BattleshipTargetDamagedWeapon`) still does not fire.
- Which scripts in `MD_GLA01`, `MD_USA02` and `MD_USA05` create the 28 objects has not been traced back; the distribution of the four types over the three
  maps has not been collected individually (only the sums per type and per map).
- Whether a map creates the objects `CINE_SandExplosionDetonationObject` and `DemoTrapDetonationObject` has not been checked.
- The Battle Bus is re-enacted only on the synthetic fixture; why the staging of the census does not hit the bus of the retail data has not been investigated.
- The determinism matrix (Linux, cultures, JIT switches) and the audit of the campaign (`CampaignTool audit`) have not been run for this commit.
- The simulation tests with content at `--tick-rate 60` and `120` fail in the named real-data tests already on `main`; that is only noted here.
