# Rate invariance: merging the seven packages (Phase 23, package CONSOLIDATION)

As of 8 October 2026, starting state `b1a47b4` (all seven correction packages PROJECTILES, SCRIPTS, PHYSICS, TIMING, GROUND, FLIGHT, TURRETS are on `main`). This document
lays down how the original frames of the individual objects and modules lie relative to each other, what was changed about that, and lists the causes of the failures that were left after the
merging of the packages. The documents of the packages (`docs/rate-invariance/RATE-INVARIANCE-*.md`) stay as they are; where this document contradicts them, this one applies.
"Frame" means a logic frame of the original (1/30 s), "tick" a step of the simulation, r = ticks per frame (1, 2, 4). Values are given as 30 / 60 / 120 Hz.
**No human has seen any of this.**

## 1. The convention

### 1.1 The problem

Every package counts "the object's own frame" for itself: per module (`FramePhase`, `subStep`, `frameTicksLeft`, `FlightFrameStep`, turret `SubSteps`), on the grid of the world
(`IsOriginalFrameBoundary`: script VM, Spectre, random draws, dock and supply machine) or from the creation (`SpawnTick`). Each on its own is correct: a single object
stands, after each of its frames, bit-identically where the 30 Hz step puts it. But where two modules of an object or two objects **look at** each other, it was not laid down
*in which tick* the look happens and *which position* the observer then reads. Measured consequences at the starting state:

- `HeightDieUpdate` (looks once per frame) and the projectile of the same object (decides once per frame) lie in the same tick, but the projectile has covered only
  1/r of its frame step in this tick: neutron missile in the test 94 / 94 / 95 frames of dive; Inferno shell miss distance 1.258 / 0.000 / 0.000, path length 247.837 / 257.625 / 257.625.
- Mine (TIMING: compares the distance once per frame *from the first contact of the unit*) and ground movement (GROUND: reports overlap once per frame *of the driving object*): two
  grids for the same report; if they lie on different ticks, the mine stays mute (test "Structures: mines detonate …" at 60 and 120 Hz).
- The shroud is refreshed after the **first** tick of its frame: readers of the vision see a change up to (r − 1)/r frame earlier than at 30 Hz (TURRETS, finding 3).
- Objects that another object creates (rider, mob members, the supply centre's own gatherer) appear one **tick** after their carrier instead of one frame.

### 1.2 What applies at 30 Hz (the definition)

In a tick T of 30 Hz the phases run commands → updates (in the order wake time, object id, module index) → target acquisition/turrets → events → destroying → delayed
hits → creating → vision. From this follows what every observer sees:

| Observer in tick T | sees another object (or module) |
|---|---|
| Command (phase 1) | in the state after step T − 1 |
| Update that runs **after** the update of the observed one (larger id or larger module index respectively) | after step T |
| Update that runs **before** it | after step T − 1 |
| Phases after the updates (target acquisition, turrets, delayed hits, vision) | all after step T |
| a new object requested in tick T | exists at the end of tick T; its first update runs in T + 1; one requested during the creation phase appears at the end of T + 1 |

### 1.3 The determination for r > 1

The frame f of the world is the ticks r·f … r·f + r − 1. An object that is created or ordered at a frame boundary is **in phase** with the world; one that receives a command in the middle of the frame
counts its frames from this tick (command acceptance stays per tick). For every object the following applies, in its phase:

1. **The decision is made in the first tick of the frame.** Second-order quantities, state machines and events "once per frame" fall on the first tick – in seconds the
   point in time of the 30 Hz tick (all seven packages do it this way; the time convention of the suite, `docs/SIMULATION-CORE.md` §39.1, demands it).
2. **The position reaches its 30 Hz value with the last tick of the frame**; in between the object lies on the straight line towards it (subdivision, one share per tick).
3. **Whoever looks at another object or another module of the same object in order to decide something reads its position at the end of the frame that it has already begun**
   (`Entity.FrameEnd(tick)` = position + not yet covered remainder of the frame step, `Entity.AheadX/Y/Z`). That is exactly the table from 1.2: a mover that has already had
   its turn in this tick has begun its frame and counts with its position after step T; one that is still to have its turn has finished its previous frame (remainder zero) and counts with its
   position after step T − 1. For objects in phase the answer is the same in every tick of a frame as that of the 30 Hz step; it does not tip over in the middle of the frame. At 30 Hz
   the remainder is always zero, `FrameEnd` is the position, and nothing is ever written. A remainder that its mover has touched neither in this nor in the previous tick (stopped,
   paralysed, put to sleep) no longer counts (`Entity.AheadTick`). The mover itself continues to compute with its own position.
4. **An event that the observer triggers in the decision tick at the frame-end position (death by `HeightDieUpdate`) puts the object on this position** (`Entity.CompleteFrame`):
   it dies where it dies at 30 Hz. The mover covers no further share of this frame afterwards (the remainder on the object is its only truth about what is missing).
5. **Phases of the world that look at all objects after all updates and themselves run only every few frames (vision) run after the last tick of their frame** and read the
   momentary position; for every object in phase that is then the position after step T. Whoever reads vision reads, until then, the state of the previous frame – never one that does not yet exist at
   30 Hz at this game time.
6. **What has not been converted reads the momentary position** (§6 names the places: the look of target acquisition, damage in a radius, dock, boarding, the collision check
   of a driving object at the end of its own frame, all flight and special movers that do not publish their remainder): at most one frame step of the observed object away from the
   30 Hz value. Commands are accepted in every tick; an object that is ordered in the middle of the frame of the world has its own phase.
7. **A new object exists at the end of the tick that requested it; its first frame begins one frame (r ticks) after that** – its phase is the tick of its creation.
   An object that is requested during the creation phase (at 30 Hz "at the end of the next tick") is created one frame later, not one tick. A command that an
   object receives in the creation phase (rally point) is executed with its first frame. A module whose first update decides an event or creates something
   runs for the first time in this tick (`SpawnTick + r`). **Exception, deliberately left:** a module that integrates or counts its updates from the creation still begins
   with the tick after the creation (the packages and their tests are built on that; §6 "first frame for all modules" names what the general rule costs as measured).
8. **A report has one grid, that of the reporter.** Whoever receives a report per frame (`ICollideModule.OnCollide` from a driving object) keeps no grid of its own for it.

The phase thus belongs to the **object** (tick of the creation or of the command that sets it in motion respectively), not to the module: two modules of an object that both count "from the creation"
or both "from the shot" decide in the same tick and in the order of their module index, as at 30 Hz. A phase counter of a module's own is now only the
bookkeeping of this one phase (it is not rebuilt into a shared counter on the object: that would be a rebuild of all movers without a measured benefit and would change the content
of every 60 Hz snapshot).

### 1.4 Discarded alternatives

| Alternative | What it means | Cost | Why discarded |
|---|---|---|---|
| A. All objects on the grid of the world (frame phase enforced) | every decision falls on `tick % r == 0`; a command in the middle of the frame waits until the next boundary or begins with a partial frame | none in the hot path; but every mover of the seven packages would have to be converted | a waiting command is the forbidden escape "original grid" (Ausweg „Originalraster“) for movement and command acceptance (up to r − 1 ticks of latency); a partial frame has no 30 Hz counterpart and is not bit-identical. It does not solve the observation within a tick (who has already had its turn?) anyway – for that rule 3 would be needed in addition |
| B. Observing in the **last** tick of the frame, position "at the last completed frame" readable | decisions and events fall on the last tick; a mover that is still to have its turn in the tick is read with its position from the frame beginning | the same bookkeeping as rule 3 (a second position triple per mover), plus every decision (r − 1) ticks later | shifts every time of the suite by (r − 1)/r frame against its time convention ("what happens in the step of the command has the time 0"): all `ExactTime` quantities of the seven packages would drop out |
| C. Order: first all movers, then all observers | a second pass per tick | the scheduler wakes modules individually; a second pass doubles the work also at 30 Hz and changes the order there | changes 30 Hz |
| D. chosen: rules 1–8 | three `Fixed` and one tick per object, which only movers above 30 Hz write; an observer adds them when reading | at 30 Hz: four more fields per object, no write access. The scans of the ground movement in the hot path (`StepBlocked`, `StationaryBlockersAhead`) and the range question branch once per call into a copy with the frame-end view that never runs at 30 Hz; per candidate nothing is added at 30 Hz. Only observers on which a failure or the first deviation of a match is measured are changed (§2, §5) | |

## 2. Causes, one commit per row

Values 30 / 60 / 120 Hz. "Suite" is the rate-equivalence suite (`docs/SIMULATION-CORE.md` §39), numbers are "quantities outside the tolerance at 60 Hz / at 120 Hz" of the named
group immediately before and after the commit. After every commit that changes the simulation: `input-manifest check` identical and `reference-manifest check --set quick` 9 runs
identical (§8).

| # | §5 entry (`TICKRATE-MIGRATION.md`) or origin | Finding before | Correction (file, symbol, rule from §1.3) | after | Commit |
|---|---|---|---|---|---|
| 1 | 5.2 `HeightDieUpdate` against the projectiles (packages PHYSICS and PROJECTILES) | Test "the neutron missile flies the 30 Hz flight" red: dive 94 / 94 / 95 frames; Inferno shell miss distance 1.258 / 0.000 / 0.000, path length 247.837 / 257.625 / 257.625 | `World/Entity.cs` (`AheadX/Y/Z`, `AheadTick`, `FrameEnd`, `SetAhead`, `CoverShare`, `CompleteFrame`); `DumbProjectileBehavior`, `MissileAIUpdate`, `NeutronMissileUpdate` publish the remainder; `HeightDieUpdate` reads the frame end and lets the object die there (rules 3, 4) | 94 × 3; 1.258 × 3 and 247.837 × 3; `projectiles` 10 / 9 → 6 / 6 | `ad2b9e9` |
| 2 | 5.2 tracking of the projectiles onto a driving victim | the projectile read the victim one share into the step; 89 of 91 "moving victim: impact distance" not bit-identical to 30 Hz (within the tolerance) | `AIUpdateInterface.AdvanceFrame` publishes the remainder of the ground step; `DumbProjectileBehavior` (launch, path adjustment) and `MissileAIUpdate` (launch, `FollowedVictim`) read `FrameEnd` (rule 3) | together with #3: 89 → 2 not bit-identical; `projectiles` 6 / 6 → 6 / 10 (only with the old measurement) | `910644d` |
| 3 | – (measurement of the suite) | "moving victim: impact distance" read the victim in the tick of the impact at its momentary position: an offset below one frame that the suite is not supposed to add (§39.1) | `RateCases.Projectiles.cs`: the victim is read at the frame end (`Entity.FrameEnd`; at 30 Hz the position). No tolerance, no case, no 30 Hz value changed | Inferno 31.400 / 31.067 / 30.900 → 31.400 × 3; `projectiles` 6 / 10 → 6 / 6 | `43443f5` |
| 4 | 5.3 (kind "first update one tick after …"): neutron explosion | `NeutronMissileSlowDeathBehavior.BeginSlowDeath` woke "in the next tick": all blast waves half or three quarters of a frame too early; driving victim 119.250 / 118.583 / 118.250 | `AirWeaponModules.cs`: one frame after the death | 119.250 / 118.917 / 118.750 (within; the remainder is the position of a victim that dies in the middle of the frame); `projectiles` 6 / 6 → 4 / 4 | `ee4ef91` |
| 5 | 5.4 `MinefieldBehavior` (package TIMING) against `ResolveFootprintCollisions` (package GROUND) | Test "Structures: mines detonate …" red at `--tick-rate 60` and `120`: two grids for one report, the mine stayed mute | `MineModules.cs`, `OnCollide`: no grid of its own any more, the report comes per frame of the driving object (rule 8); the state `phase` is dropped | test green at 30 / 60 / 120 Hz; `timed` unchanged, mine cases equal in damage and events | `0136733` |
| 6 | – (measurement of the suite) | three vision cases asked after every tick whether a place is seen and took this tick as the time; the vision is a state of the frame end | `RateScene.StepUntilFrameEnd`, `RateCases.Timed.cs`: read at the frame end, counted from the first step of the frame (the form of `a6fdfea`) | 0.0000 s at every rate before and after #7 (without the correction after #7: 0.0000 / 0.0167 / 0.0250) | `f55c16d` |
| 7 | new (package TURRETS, finding 3): refresh of the vision | after the first tick of its frame; readers saw a change up to (r − 1)/r frame earlier | `SimulationWorld.Step`/`StepObserved`, `_visionPhase`: after the last tick of the frame (rule 5) | refresh for an object from frame 1: ticks 3 / 6 / 12 → 3 / 7 / 15; all groups unchanged | `a8eed4a` |
| 8 | – (without a behaviour change) | `HeightDieUpdate` counted its frame from its own first update (`FramePhase`) | `EntityFrames.BeginsFrameOf` (new file `Time/FrameGrid.Consolidation.cs`): the phase is the tick of the creation | the same ticks; the state `framePhase` is dropped | `f0770fe` |
| 9 | 5.3 `ProcessSpawns` | an object requested in the creation phase (rider of the combat bike, superstructure of the Overlord) appeared one tick after the carrier and boarded one further tick later; combat bike "time to top speed" 2.0333 / 2.5167 / 1.3250 s | `SimulationWorld.ProcessSpawns` (`PendingSpawn.NotBefore`, set and written only above 30 Hz); `ContainChainModules.EnterPayload` checks in the frame ticks of the carrier (rule 7) | 2.0333 / 2.0500 / 2.0583 s; `locomotion` 12 / 12 → 4 / 3 | `f8ce7c4` |
| 10 | 5.3 `Register` (first update): `SpawnBehavior` | the supply centre's own gatherer and the mob members were created half or three quarters of a frame too early; truck income in 5 min 6000 / 6300 / 6300 | `SpawnModules.cs`, `OnSpawn`: first update one frame after the creation | 6000 × 3; Chinook first delivery 31.6333 × 3; `economy` 7 / 10 → 4 / 5, `locomotion` 4 / 3 → 2 / 2 | `21a6f7d` |
| 11 | 5.3 (TIMING, §5 "three individual cases") | empty parachute "kill until removal" 3.0000 / 2.9833 / 2.9750 s, mob member without nexus 7.0000 / 6.9833 / 6.9750 s | `FlightModules.cs` `ParachuteContain.Update`, `SlaveModules.cs` `MobMemberSlavedUpdate.Update`: only the giving up waits for the first frame of the object | 3.0000 × 3 and 7.0000 × 3; `timed` 3 / 3 → 1 / 1 | `9ba608c` |
| 12 | new (ground movement, found with `rate-compare --ai-diff`) | a speed per frame below one raw value per tick (here 1 raw value when braking) was lost above 30 Hz: `FrameSpeed()` took the published speed 0 for "stopped"; following frame 1 raw value too slow | `Phase5Modules.cs`, `FrameSpeed`; the module clears `_frameSpeed` where it stops itself | speed per frame in raw values, frames 2253–2255: 1864136 / 1 / 1864136 at 30 Hz; at 60 Hz before 1864136 / 0 / 1864135, after as 30 Hz | `3f046f8` |
| 13 | 5.2 (GROUND, open: "two driving objects that meet") | `StepBlocked` and `StationaryBlockersAhead` read a mover that had already had its turn in the tick one share along; gatherer round trip 27.4000 / 27.3333 / 27.4000 s | `SimulationWorld.Collision.cs`: `StepBlockedAtFrameEnd`, `StationaryBlockersAheadAtFrameEnd`, `StandsStillAtFrameEnd` (above 30 Hz; rule 3; whoever covers the last step of its drive stands) | all gatherer quantities equal at every rate; `economy` 4 / 5 → 0 / 0; new three-rate test (eight runners, bit-identical over 400 frames) | `2db1fc6`, test `305da79` |
| 14 | 5.3 `Register`: command in the creation phase | a unit produced with a rally point set off one tick after appearing and from then on stood one share ahead of all objects in phase | `SimulationWorld.DeferSpawnPhaseOrders` (above 30 Hz; rule 7) | first frame with a deviating position in the match USA against China: 1140 (38.00 s) → 3467 (115.57 s); `flight` 0 / 4 → 0 / 3 | `cc7d92e` |
| 15 | 5.4 `StepAcquisition`/`AutoFire` (position when attacking) | pursuit target, "in range" and shot were decided at a position in the middle of the frame | `SimulationWorld.Acquisition.cs` `Engage`, `WeaponSlots.cs` `WithinWeaponRange`, `Projectiles.cs` `FireWeaponAt`: frame-end view above 30 Hz; whoever comes into range stops at the frame end (rules 3, 4) | first frame with a deviating position 3467 → 3537 → 3540 → 3543 (118.10 s); suite unchanged (`weapons` 0 / 0, `turrets` 4 / 0, `projectiles` 4 / 4) | `04e2fc8` |
| 16 | 5.6 healing in slices: `PropagandaTowerBehavior` | "frames with healing" 750 / 751 / 751, full health 26.8667 / 26.9000 / 26.9000 s | `FieldEffectModules.cs`, `Give`: the slice of the original once per frame of the tower (escape, §3) | 750 × 3, 26.8667 × 3; `overtime` 2 / 2 → 0 / 0 | `f05af4f` |

Tool (no behaviour): `SimTool rate-compare <content> --ai-diff A,B [--rates 30,60] [--context N] [--dump DIR] [--state [--positions] [--frames N] [--watch ID --watch-from BILD]]`
(`d5ce353`, `551f11a`) – the first deviating command or the first frame after which an object deviates between two rates.

## 3. Uses of the escape "original grid" in this package

| Place | What runs once per frame | Grid | Reason |
|---|---|---|---|
| `PropagandaTowerBehavior.Give` | the heal slice (bonus and scan stay per tick) | that of the tower (`BeginsFrameOf`) | the original heals once per frame; slices per tick are rounded individually and do not sum to the frame slice. Neither movement nor command acceptance nor target acquisition |
| `ContainChainModules.EnterPayload` | the check of a carrier whether its initial crew has appeared | that of the carrier | a state change "one update after appearing"; not subdividable |
| `SimulationWorld.Step` (vision) | unchanged every three frames; only the phase within the frame is new (last tick) | world | – |

Not an escape but the view of the 30 Hz step (rule 3): `HeightDieUpdate`, the scans of the ground movement, `Engage`, `WithinWeaponRange`, `FireWeaponAt` run in every tick or
at every decision respectively as before and only read a different position.

## 4. The group `ai`: first deviation, with numbers

Case "skirmish FactionAmerica against FactionChina", 30 against 60 Hz, with `rate-compare --ai-diff` (command stream in game time) and `--state --positions` (position of all objects at the end
of every frame). At the starting state the first command and the first build order were equal, 1,120 against 1,168 commands in eight minutes.

| State | first deviating command | first frame with a deviating position | Cause of the deviation at this place |
|---|---|---|---|
| after #11 (`9ba608c`) | command 121, 113.4667 s (attack target 1927.66 against 1927.71) | not measured | the eight infantrymen of China's first wave avoid each other; `StepBlocked` read the one in front one share along (#13) |
| + #12 alone | 113.4667 s | 1140 (38.00 s) | the supply Chinook climbs one tick after appearing (rally-point command in the creation phase, #14) |
| + #13, #14 without #12 | command 135, 114.4333 s | 2255 (75.17 s) | speed per frame of 1 raw value is lost when braking (#12) |
| + #12, #13, #14 (`cc7d92e`) | command 158, 116.4333 s | 3467 (115.57 s) | target of a pursuit from the momentary position of the victim (#15) |
| + pursuit at the frame end | command 194, 119.9333 s | 3537 (117.90 s) | "in range" tips over in the middle of the frame, the unit stops half a step earlier (#15) |
| + range and stop at the frame end | command 182, 119.4667 s | 3540 (118.00 s) | the target of a missile from the momentary position (#15) |
| + shot at the frame end (`04e2fc8`, final state) | command 182, **119.4667 s** | **3543 (118.10 s)** | the heading of a shooter that has stopped in the middle of its frame: the missile launches with an intermediate angle (the heading does not yet belong to the frame-end view) |

The battle begins at 117.9 s. Until then – build-up, economy, two waves on the approach, 3,537 frames, all objects – the worlds at 30 and 60 Hz are **bit-identical** in position
(before, no longer from frame 1140). The remaining deviation is **not** a legitimate response below one frame but the next not converted reading place (§6); each
further one has so far cost two to seventy frames. The numbers of the group (commands in three minutes etc.) therefore stay outside: 25 / 24 → 24 / 23. Not measured: 120 Hz, the
two other pairings.

## 5. Suite at the final state

All ten groups, 4,307 quantities, Release, real data, `--only RateEquivalence --rate-report` at the final state of the simulation (`f05af4f`). "Before" is the run at the state `62fbb6a`
(all seven packages), "Start" the first run of the suite (§39.6).

| Group | Quantities | Start 60 / 120 Hz | before 60 / 120 Hz | **after 60 / 120 Hz** | Quantities with a failure | Cases with a failure |
|---|---|---|---|---|---|---|
| `locomotion` | 837 | 152 / 211 | 12 / 12 | **2 / 2** | 2 | 1 |
| `flight` | 74 | 21 / 21 | 0 / 4 | **0 / 3** | 3 | 3 |
| `weapons` | 1,474 | 43 / 57 | 0 / 0 | **0 / 0** | 0 | 0 |
| `projectiles` | 743 | 54 / 103 | 10 / 9 | **4 / 4** | 4 | 2 |
| `turrets` | 216 | 28 / 36 | 4 / 0 | **4 / 0** | 4 | 1 |
| `overtime` | 418 | 80 / 84 | 2 / 2 | **0 / 0** | 0 | 0 |
| `economy` | 81 | 22 / 25 | 7 / 10 | **0 / 0** | 0 | 0 |
| `timed` | 383 | 53 / 56 | 3 / 3 | **1 / 1** | 1 | 1 |
| `scripts` | 15 | 4 / 5 | 0 / 0 | **0 / 0** | 0 | 0 |
| `ai` | 66 | 22 / 27 | 25 / 24 | **24 / 23** | 26 | 3 |
| **Sum** | **4,307** | **479 / 625** | **63 / 64** | **35 / 33** | **40** | **12** |

Without the group `ai` it is 11 quantities at 60 Hz and 10 at 120 Hz in 9 cases; each is in §6. Unmeasured at every rate remain 207 quantities (unchanged the cases from §39.5;
`timed` 36 → 34).

## 6. What still fails or is open, with classification

| What | Values 30 / 60 / 120 Hz | Classification | Reason, proposal |
|---|---|---|---|
| `locomotion`, mob nexus: course time, course distance (2 / 2) | 23.0667 / 25.2167 / 25.0250 s; 400.062 / 439.291 / 435.284 | fixable later | the nexus drives around its members; `MobMemberSlavedUpdate` reads position and target of the nexus momentarily and counts from the tick after the creation. Frame-end view there (rule 3) |
| `projectiles`, scatter salvos (4 / 4) | impacts of 40: artillery platform 8 / 7 / 7, battleship 21 / 20 / 20 | fixable later, not traced | forty projectiles in the same tick; the first detonation hits the neighbours in flight (`DetonateProjectile` reads the radius at the momentary position) |
| `turrets`, Sentry drone (4 / 0) | angle at the first shot 1.57080 / 1.55771 / 1.57080 | fixable later, not investigated | `DeployStyleAIUpdate`: status at 60 Hz in tick 65 instead of 64 (finding TURRETS); the trigger of the gate has not been found |
| `flight`, only 120 Hz (0 / 3) | MiG landing command until touchdown 14.6333 / 14.6333 / 14.5667 s; Raptor "parked and reloaded" 60.8000 / 60.8000 / 60.5333 s; Comanche "turnaround until arrival" 13.3333 / 13.3667 / 13.3833 s | fixable later (larger rebuild) | winged flight and `JetAIUpdate` compute at about 35 places with `value per tick × ticks per frame` and keep `Entity.Speed` per tick; `LocomotorFrames` fits (the table carries speed, acceleration, turn rate, braking, minimum speed per frame), but demands a speed per frame of its own in the module as for the ground movement. The paradrop ("aircraft gone" 49.2667 / 49.2500 / 49.2417 s) has been within since #14 |
| `timed`, `CINE_BShipWeaponDetonationObject` (1 / 1) | – / 0.0667 / 0.0667 s | **30 Hz error**, needs a change at 30 Hz | `LifetimeUpdate` (1 frame) and `SlowDeathBehavior` (1 frame) are due in the same tick at 30 Hz; `WakeAt` discards the wake request "next tick" of a module that is still awake, whose update sees the deadline not reached and sleeps forever: the object never ends. Above 30 Hz the module is not awake in this tick (it ran one tick after the creation), the request takes hold. Making the rates equal would mean rebuilding the error (rule 7 for all modules **and** "already awake" extended to "within the next frame"; with rule 7 alone the case stayed, as measured, at – / 0.0667 / 0.0667). Proposal for a commit of its own that may change the manifest: `SlowDeathBehavior.Update` returns the remainder until the deadline instead of `SleepForever` |
| `ai` (24 / 23) | commands in three minutes etc. | chaotic consequence of a fixable cause | §4: the first deviation now lies in the first battle (118.1 s); before that bit-identical |
| Angle at the frame end | – | fixable later | `Entity.FrameEnd` only knows the position; a shooter that stops in the middle of the frame keeps an intermediate heading (first deviation of the match, §4) |
| an object that dies in the middle of the frame | neutron missile, driving victim 119.250 / 118.917 / 118.750 | fixable later | it stays lying at the momentary position; at 30 Hz it dies after its whole step. `CompleteFrame` at death would be rule 4 for all |
| readers not converted (rule 6) | – | fixable later, each place individually | look of target acquisition (`FindMoodTarget`), damage in a radius, turrets (`AimTurretsAt`), dock and boarding, `MobMemberSlavedUpdate`, `SlavedUpdate`, special abilities, `SkirmishAi.Observe` (reads before the step at a frame boundary: already correct), script VM (runs in the first tick: reads objects in phase one share along) |
| movers that do not publish | – | fixable later | all flight and special movers (`FlightFrameStep`, `JetAIUpdate`, parachute, crash, train, avalanche, ferry, wave, free fall) keep their remainder themselves; an observer sees them momentarily |
| Collision check of a driving object | – | deliberately left | `ResolveFootprintCollisions` runs with the last share (GROUND) at the momentary position: exact against standing objects and against driving ones that had their turn earlier in the tick; a later driving object is (r − 1)/r of its step along. Events of the check fall on the last tick of the frame (mine gone 4.8000 / 4.8167 / 4.8250 s, within) |
| **first frame for all modules** | – | decision open | measured on a trial basis (not adopted): every update of a new object at the earliest `SpawnTick + r`. Suite: `locomotion` 12 / 12 → 10 / 10, `economy` 7 / 10 → 4 / 5, but `projectiles` 4 / 4 → 24 / 24 and `timed` 3 / 3 → 5 / 5, because modules that count their updates from the first (`HeightDieUpdate` – converted in the meantime –, `FirestormDynamicGeometryInfoUpdate`) and integrators (winged flight, parachute, Chinook, dynamic shroud clearing, ferry) ran r − 1 ticks later: 6 three-rate tests and 6 to 8 further tests at `--tick-rate 60`/`120` failed. The narrow solution (#9, #10, #11, #14) fixes the same suite cases without these tests. Consequence of the exception: an integrator that begins with the creation (a climbing Chinook without a rally point, a falling parachute) completes its frame in the first instead of in the last tick of a frame of the world |
| Health of a building site at the frame end | 5.997 / 8.495 (60 Hz) in the first frame of the work | deliberately left | "the first work step counts a whole frame" (TIMING): the build time is exact, the intermediate state lies (r − 1)/r frame ahead |
| Performance | – | not measured | at 30 Hz four fields are added per object, in `HeightDieUpdate`, at the projectile launch and in `WithinWeaponRange`/`FireWeaponAt`/`Engage` one comparison each; the scans of the ground movement branch once per call. No comparison run of two binary directories |

Checked from the brief, without a change: the fire wall of the Dragon Tank is equal at the starting state of this package (damage in 60 s 14,605.5 × 3, with upgrade 18,167.625 × 3,
shots 838 × 3); the ECM tank passes in all quantities (angle after 180° −2.09440 × 3); the four build cases are exact (power plant 15.2333 × 3, China 15.0333 × 3, GLA barracks
14.6333 × 3); chained `StepUntil` were not looked through systematically (no case of the groups `timed`/`turrets` fails because of it).

## 7. The helpers side by side (inventory only, nothing changed)

| Helper (file) | What | Rounding | Way of counting | Users |
|---|---|---|---|---|
| `FrameGrid` (`Time/FrameGrid.cs`) | durations on the grid: `FloorToFrame`, `CeilingToFrame`, `AtLeastOneFrame`, `DrawFrames`, `CountdownReload` | whole frames | – | everywhere, since Phase 20 |
| `FrameGridTiming.ZeroIsNextFrame` (`FrameGrid.Timing.cs`) | delay 0 behind a per-tick query = one frame | – | – | weapon reload, battle plan, `FireWeaponUpdate` |
| `ProjectileFrameGrid.FrameShare` (`FrameGrid.Projectiles.cs`) | share of a frame step | `Fixed` division step / n; the **last** share takes the remainder | `subStep` 0 … n − 1 (0 = decision), state in the module | `MissileAIUpdate`, `NeutronMissileUpdate`, `PhysicsBehavior`, guided bomb |
| `DumbProjectileBehavior.FlyToward` | share towards the path point | (point − position) / remaining shares, anew per tick; the last sets the point | as above | shells |
| `ProjectileFrameGrid.PerTickToPerOriginalFrame` | speed per frame of the 30 Hz catalogue from the value per tick | reconstruction via a decimal number with four places, otherwise × n | – | projectiles, `FlightTicks` |
| `FramePhysics.FrameSlice`/`FramePart` (`FrameGrid.Physics.cs`) | share of a frame step | `floor(step · k / n) − floor(step · (k − 1) / n)` (shift operation, rounds towards −∞), sum exact | `subTick` 1 … n via `FramePhase` (state 0 … n − 1) | toppling, structure topple, train, Battle Bus, collapse, shroud clearing |
| `FramePhysics.PerTickAsPerOriginalFrame` / `…Squared…` | catalogue value per tick → per frame | × n or × n² respectively (last bits different per rate) | – | package PHYSICS |
| `FlightFrames.PerFrame`/`PerFrameSquared`/`PerTick` (`FrameGrid.Flight.cs`) | the same; `PerTick` divides the raw value | × n, × n²; division truncates towards zero | – | flight, jet, parachute, Spectre, Chinook |
| `FlightFrameStep` | remainder of a frame step | share = raw value / n truncated towards zero; the **first** tick takes the remainder (the object is set back by n − 1 shares) | `left` n − 1 … 0, state as an object in the snapshot | winged flight, jet, crash, parachute, Chinook, Spectre |
| `AIUpdateInterface.AdvanceFrame` (`Phase5Modules.cs`) | share of the ground step | `raw · (i + 1) / n − raw · i / n` with integer division (towards zero), sum exact | `frameTicksLeft` n … 1, `frameDue` | ground movement |
| `LocomotorFrames` (`Templates/LocomotorFrames.cs`) | locomotor constants per frame | the 30 Hz conversion of the same source numbers (static table, key catalogue instance); otherwise × n, × n² | – | ground movement |
| `TurretInfo.RatesPerOriginalFrame`, `TurretState.SubSteps` | turn rates per frame; share of the turret step | table as above; share = remainder / remaining sub-steps rounded down, the last sets the goal | `SubSteps` | turrets |
| new: `Entity.Ahead…`/`FrameEnd`/`CoverShare`/`CompleteFrame`, `EntityFrames.BeginsFrameOf` | the published remainder; the frame ticks of an object | no rounding of its own (the mover supplies its shares) | `AheadTick` | observers (§2) |

**Proposal for later (changes results at 60/120 Hz, never at 30 Hz; a step of its own with the suite as the measure):** *one* share helper in the form of `FramePhysics.FrameSlice`
(sum exact, sign-correct, no state except the number of the tick within the frame) and the remainder on the object (`Entity.Ahead…`) as the only bookkeeping of what is missing – then
`subStep`, `FlightFrameStep`, `frameTicksLeft`/`frameStep` and `FramePhase` are dropped as a state of their own each, and every mover publishes by itself. For constants per frame
the pattern of `LocomotorFrames` (table next to the catalogue type with the 30 Hz conversion of the source numbers) instead of `× n` and instead of the decimal reconstruction.

## 8. Check runs

| Run | Result |
|---|---|
| `input-manifest check` | identical, after each of the 14 commits that change the simulation (`f0770fe` checked together with `f8ce7c4`), and at the end |
| `reference-manifest check --set quick --jobs 3` | 9 runs identical, after each of these commits (each with a freshly built `SimTool`; the three pure measurement and tool commits `43443f5`, `f55c16d`, `d5ce353`/`551f11a` without a run of their own) |
| `reference-manifest check --set full --jobs 4` | **50 runs identical**, at the state `305da79` (last commit on the simulation: `f05af4f`) |
| `dotnet build CnCNew.slnx` | 0 warnings, 0 errors (incremental, `bin`/`obj` of `CnCNew.Client.Stride` not deleted) |
| Simulation tests, Release, without real data, 30 Hz | all 495 green (494 existing, 1 new) |
| the same with `--tick-rate 60` | **all green** (red at the starting state: "Structures: mines detonate …") |
| the same with `--tick-rate 120` | **all green** (red at the starting state: the same test; plus at every rate "the neutron missile flies the 30 Hz flight") |
| Simulation tests, Debug, 30 Hz (`dotnet run --project tests/CnCNew.Simulation.Tests`) | all green |
| Objective driver, 24 campaign maps (`CampaignTool drive <content> --map "/NAME.map" [--tick-rate N]`, one process per map, copy of the binary directory) | 30 Hz: victory 24 / 24, defeat 24 / 24. 60 Hz: 24 / 24 and 24 / 24, on every map the same final script and the same game time as at 30 Hz (at most one frame apart). 120 Hz: 24 / 24 and 24 / 24, likewise (e.g. `MD_CHI04`: victory in tick 48,258 / 96,516 / 193,032, the same frame) |

Between `d5ce353` and `551f11a` the source test "Determinism rules hold in the simulation sources" was red (a number without invariant culture in the diagnostics); in this span
lie `3f046f8`, `2db1fc6` and `cc7d92e`, whose other tests were green.

Changed test expectations: "Tick rate, world: the shroud is refreshed every three logic frames" (refresh after the last tick of the frame: ticks 3 / 7 / 15) and "Turret
attack: a victim that falls into the fog ends the aim" (waits two frames instead of two ticks for the first refresh), both in `a8eed4a`. New test: "Rate invariance,
consolidation: movers that meet are where the 30 Hz movers are" (`Tests.RateInvariance.Consolidation.cs`; fails without #13 at 60 Hz after frame 15).

Not done or not checked: dedicated three-rate tests for #1 to #11 and #14 to #16 beyond the named ones (the suite and the two previously red tests carry them); the
first deviation of the match at 120 Hz and for the two other pairings; performance; the Debug run with real data; tests and suite of the presentation
(`Presentation.Tests`, `Game.Tests`) – the frame-end view changes only the simulation above 30 Hz. **No human has seen any of it.**
