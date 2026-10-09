# Rate invariance: projectiles (Phase 23, package PROJECTILES)

As of 8 October 2026, branch `worktree-agent-a4dece435d2badf01`, building on `7b54374`. This document belongs to the backlog from
`docs/TICKRATE-MIGRATION.md` §5 and to the rate-equivalence suite (`docs/SIMULATION-CORE.md` §39). It describes what was changed on projectiles, missiles, the
neutron missile, the guided bomb, the EMP pulse and free fall so that they do the same thing at 30, 60 and 120 Hz – and what remains open.
30 Hz is the definition of the correct behaviour and stayed byte-identical (input manifest and reference manifest after every commit, full set at the end).

## 1. Principle

A projectile makes its decisions **once per original frame**, on its own phase (counted from the launch), from exactly the state that
it has at this point at 30 Hz, and with per-frame quantities (speed per frame, acceleration per frame², gravity per frame²). The step that a frame
decides is flown in equal shares over the ticks of that frame; the last share takes the remainder, so that the sum is exactly the step.

- **First step one frame after the launch.** At 30 Hz the projectile is created at the end of the firing tick F and moved for the first time in tick F + 1. At
  the rate r·30 Hz the first step falls into tick F + r, every further one r ticks later. Decisions and events (detonation, impact,
  ignition, end of fuel) thus fall on the **first tick** of their frame – in seconds the same point in time as at 30 Hz.
- **State at the frame boundary.** After the last tick of a frame the projectile is where it is at 30 Hz after the tick of that frame – in the
  tests bit for bit. In between it lies on the chord between the two frame states.
- **Immediately instead of in shares:** arrival at the target, ground contact and the jump to the intermediate point of the neutron missile put the projectile at the
  location in the deciding tick, as at 30 Hz (there it is likewise a jump by at most one step).
- **New state** (`subStep`, for the missile and the guided bomb additionally the step of the frame) is only written when it is not 0. At 30 Hz
  it is always 0: snapshots and hashes stay byte-identical. At 60/120 Hz the snapshot content changes within a frame.
- **Speed per frame as in the 30 Hz catalogue.** `ProjectileFrameGrid.PerTickToPerOriginalFrame` (new file `src/CnCNew.Simulation/Time/FrameGrid.Projectiles.cs`)
  finds, from the catalogue value per tick, the value per second with at most four decimal places that reproduces it, and rounds it the way the
  30 Hz catalogue rounds. A value that cannot be reproduced (more decimal places, not a catalogue speed) is scaled with the whole factor
  (deviation at most the last bits). Catalogue types have not received a new field.
- **Cost:** at 30 Hz each update gains one comparison `subStep != 0` and one read of `TicksPerOriginalFrame`, no division. The conversion of the
  speed (decimal arithmetic) runs once per shot and only above 30 Hz.

## 2. Causes and corrections

Suite numbers: group `projectiles`, "outside the tolerance" of 743 quantities, in parentheses at 60 Hz / at 120 Hz. Starting state 106 (54 / 103).

| §5 entry | Finding before (30 / 60 / 120 Hz) | Correction (file, symbol, principle) | after | Commit |
|---|---|---|---|---|
| §5.6 `SimulationWorld.Projectiles.FlightTicks` | Frames from the shot to the hit, 200 units at 100 per second: 61 / 60 / 61 (the speed per tick is rounded per rate on its own; "speed per tick × ticks per frame" shifts `ceil` at a frame boundary by one frame) | `World/SimulationWorld.Projectiles.cs` `FlightTicks`, `ProjectileModules.FrameSpeed`, `Time/FrameGrid.Projectiles.cs` `PerTickToPerOriginalFrame`: counting is done against the speed per frame of the 30 Hz catalogue; the sentinel value "immediately" stays itself | 61 / 61 / 61; suite 106 → 106 (no case lies on such a boundary) | `b5b9fd4` |
| §5.2 `DumbProjectileBehavior.CalcFlightPath`, `Update` | Path points per tick instead of per frame: Battlemaster shell 0.3000 / 0.2667 / 0.2500 s; arc 14 / 27 / 53 points; detonation "one update later" = half or a quarter of a frame; tracking per tick | `Modules/BuiltIn/ProjectileModules.cs` `DumbProjectileBehavior.Launch`/`Update`/`FlyToward`: speed per frame, one path point per frame, first point one frame after the launch, detonation one frame after the last point, `MaxLifespan` and tracking (distance of one frame) only on the frame ticks; within the frame in equal shares towards the point of the frame | Battlemaster 0.3000 at all rates, apex 14.257 and path length 94.928 equal; `NukeCannonGun` 1.6000 at all; suite 106 → 73 (49 / 70), quantities of the `DumbProjectileBehavior` cases 41 → 8 | `2ed62cd` |
| §5.2 `MissileAIUpdate.Launch`, `Update`, `Move` | `_acceleration = WeaponSpeed` per tick as acceleration per tick² (x after one frame 10 / 7.5 / 6.25); Euler lag (x after frame 10: 56 / 50 / 47.19); arrival "≤ one tick of flight"; `_noTurnDistanceLeft` from the previous tick; gravity per tick²; Tomahawk flight time 1.2000 / 1.2500 / 1.2417 s, distance 225.024 / 231.861 / 230.662; Scud Storm miss distance 31.773 / 34.209 / 35.442 | `ProjectileModules.cs` `MissileAIUpdate.Launch`/`Update`/`Fly`/`FlyShare`: speed, maximum speed and acceleration per frame (frame²), state machine once per frame (ignition delay, fuel, lock, distance before turning, arrival = one frame of flight, ground), step in shares; `ProjectileModules.GravityPerFrameSquared`; follow-up commit `65b9783` writes the scaling of the acceleration without a square (source rule), without a behaviour change | Tomahawk 1.2000 and 225.024 at all rates; Scud Storm 31.773 and 568.250 at all; suite 73 → 38 (20 / 37), quantities of the `MissileAIUpdate` cases 53 → 18 | `c4a0ac1`, `65b9783` |
| §5.1 `NeutronMissileData.ForwardDamping`/`RelativeSpeed`, §5.2 `NeutronMissileUpdate.Update`/`DoAttack` | mixed recursion (thrust per frame², damping share per frame) per tick: flight time 8.0667 / 6.5667 / 5.8083 s and 8.5000 / 6.8000 / 5.9167 s; apex 626.581 / 636.207 / 630.016 | `Modules/BuiltIn/AirWeaponModules.cs` `NeutronMissileUpdate.Launch`/`Update`/`DoAttack`/`TurnToward`: launch one frame after the shot, recursion, turn limit (of one frame), special climb, distance before turning, intermediate point and ground once per frame; the velocity of the frame in shares | 8.0667 at all rates; 8.5000 / 8.5000 / 8.5083 s (`HeightDieUpdate` ends the dive and looks every tick); apex 626.581 at all; suite 38 → 29 (10 / 29), quantities of the two cases 11 → 2 | `41d1cbc` |
| §5.1 `SmartBombHomingData.CourseCorrectionScalar` | blend factor per frame applied per tick; with s = 0.8 after the first frame 20 / 36 / 59.04 of 100 units (calculated, old state not measured) | `Modules/BuiltIn/LastBlockerModules.cs` `SmartBombTargetHomingUpdate.Update`: blend once per frame, the distance in shares | 20 / 20 / 20, bit for bit equal over 30 frames; the suite has no case (§39.5) | `0f5fd14` |
| §5.1 `EmpUpdate.Update` (`_scale`) | share 0.05 per tick: remaining distance to the target scale after 20 frames 35.8 % / 12.9 % / 1.7 % (calculated); state and presentation only | `AirWeaponModules.cs` `EmpUpdate.Update`: the share only on the frame ticks of the pulse (counted back from the death tick); no new state | scale after every frame bit for bit equal; the EMP times of the group `timed` were equal and stay so | `9132127` |
| §5.2 `PhysicsBehavior.Update` (free fall) | Euler per tick with gravity per tick²; landing different by fractions of a frame (old state not measured on the test scene) | `Modules/BuiltIn/PhysicsAndMinefieldModules.cs` `PhysicsBehavior.StartFall`/`Update`: first step one frame after the release, gravity per frame² on the velocity per frame, landing judged on the whole step, step in shares | from 300 units of height with 3 units per frame sideways: bit for bit equal after every frame, landing in the same frame; the suite has no case of its own | `2777f86` |

Tests: `tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Projectiles.cs` (eleven tests "Rate invariance, projectiles: …", at least one per cause
at three rates, plus a snapshot within a frame). The four hand-calculated trajectories from `Tests.Projectiles.cs` (class T no. 5–8 in
`docs/TICKRATE-MIGRATION.md` §15.2) are written in frames (`537ce09`) and pass at 30, 60 and 120 Hz **through the behaviour of the simulation**;
none had to be pinned to 30 Hz. Likewise the guided-bomb test in `Tests.LastBlockers.cs` ("one frame" instead of "one step").

## 3. Result

| Measurement | before | after |
|---|---|---|
| Suite `projectiles` (743 quantities, 91 cases) | 106 outside (54 at 60 Hz, 103 at 120 Hz), 53 cases | **29** outside (10 at 60 Hz, 29 at 120 Hz), 24 cases |
| Suite `weapons` (1,474 quantities), side effect | 57 (43 / 57) | 47 (43 / 47): nine times "first shot to first damage", once "damage in 60 s" |
| Suite `flight` (74), `timed` (383) | 23 and 56 | unchanged 23 and 56 |
| Simulation tests `--tick-rate 60` | 6 failures | 2 (flash count of the capture, one raw turret value – neither in this package) |
| Simulation tests `--tick-rate 120` | 14 failures | 10 (none in this package) |
| Simulation tests 30 Hz | green | green |

80 quantities of the group `projectiles` are fixed, three are newly outside the tolerance (previously inside by chance): `BattleshipUSSBenderGun`
"scatter: mean impact distance" (26.318 / 26.899 / 26.899, previously 26.769), and at 120 Hz "moving victim: impact distance" of `ArtilleryPlatformGun`
(10.846 / 10.561 / 10.344) and `StrategyCenterGun` (14.089 / 13.800 / 13.578).

## 4. Uses of the escape "original grid" (Ausweg „Originalraster“)

No movement, no command acceptance and no target acquisition runs only on every (rate/30)-th tick: every projectile moves in every tick. Once per
original frame (on the phase of the object) the following run:

1. **The decisions of the projectiles** – `DumbProjectileBehavior` (path point, `MaxLifespan`, tracking of the end point, detonation),
   `MissileAIUpdate` (state machine: ignition, fuel, lock, direction to the target, arrival, ground, diversion to a flare),
   `NeutronMissileUpdate` (recursion, turn, intermediate point, ground), `PhysicsBehavior` (gravity, landing), `SmartBombTargetHomingUpdate`
   (blend). Reason: this is the agreed subdivision – second-order quantities and mixed recursions change once per frame, the position
   every tick. The direction of a steering missile counts among them: it is part of the integration step.
2. **The growth share of the EMP scale** (`EmpUpdate`): a multiplicative factor per frame, applied once per frame (preferred, exact form); the scale
   is not interpolated between the frames because it has no gameplay effect.

The diversion of a missile to a flare (`_decoyTick`) thereby only takes effect in the next frame tick of the missile – at most one frame later
than set, unchanged at 30 Hz.

## 5. Open, and why

The 29 remaining quantities of the group `projectiles`:

1. **21 × "moving victim: impact distance from the victim"** (19 only at 120 Hz, 2 at 60 and 120 Hz), deviation 0.2 to 1.0 units. The projectile
   lands at every rate at the same location in the same frame; what differs is where the driving victim is in that tick. The impact falls on the first
   tick of a frame; in that tick the victim has its whole frame step behind it at 30 Hz, half of it at 60 Hz, a quarter at 120 Hz
   (0.33 and 0.5 units for a tank with 20 units per second, with the tolerance of 0.5 units). This follows from the subdivision itself and
   cannot be fixed in the projectiles. A measurement attempt (reading the victim only at the frame boundary) made it worse (54 instead of 29), because a
   steering missile then landed on the half step of the victim; it was discarded. In the two neutron-missile cases (119.250 / 118.621 /
   118.228) the travel of the victim over eight seconds is added (package LOCOMOTION).
2. **4 × Inferno shell** (`InfernoCannonGun`, `…Upgraded`: miss distance 1.258 / 0.629 / 0.314, path length 247.837 / 252.731 / 255.178). The shell carries
   `HeightDieUpdate` (`TargetHeight = 1`, only while descending); the module looks every tick and now sees the shell below the height within a frame,
   that is, earlier and at an intermediate point. `HeightDieUpdate` does not belong to this package. It becomes exactly equal only if the module judges the state
   at the frame boundary (last tick of the frame) – a determination that concerns all packages (§6).
3. **4 × scatter salvo** (`ArtilleryPlatformGun` 8 / 7 / 7 of 40 impacts, `BattleshipUSSBenderGun` 21 / 20 / 20, plus the mean miss distance). Forty
   projectiles launch in the same tick; most of them never impact even at 30 Hz. **Not traced**; what is plausible: the first detonation destroys
   neighbours in flight, and in the detonation tick the neighbours with a smaller id are a whole frame step further at 30 Hz, at a higher rate
   a share of it. The number of random draws per shot is the same at every rate (two in `FireWeaponAt`, three for `TumbleRandomly`), the mean
   miss distance differs only through the missing impacts.

Deliberately not changed:

- **§5.4 `PointDefenseLaserUpdate.FireWhenReady`** (range check and firing attempt per tick). This is target acquisition and is meant to run every tick; the
  counters have been on the grid since Phase 20. The first shot can fall less than one frame earlier at a higher rate. No case of the suite fails
  because of it; not measured individually.
- **"Patriot anti-air damage +1.2 % at 120 Hz"**: not re-measured as a finding of its own; in the group `weapons` one quantity
  "damage in 60 s" fewer is outside the tolerance after the changes (4 → 3); which weapon that is was not attributed.
- **`MissileData.KillSelfDelay`** and the other durations have been on the grid since Phase 20 and needed no change.

## 6. New findings

1. **Observers in the first tick of a frame see a partial step.** The subdivision puts decisions on the first tick of the frame and distributes
   the movement over all ticks. A module that judges the position of another object every tick (`HeightDieUpdate`, radius damage of a detonation,
   the distance to a driving victim) sees 1/r of the step in the decision tick, the whole one at 30 Hz. The error is limited to one frame step of the
   observed object, but cannot be avoided entirely with any choice of phase (objects with a smaller id have already been moved in the same tick,
   the others not yet). The tolerance "0.5 %, at least 0.5 units" is not always enough for that at 120 Hz.
2. **The charge time "recharge from creation (ready tick)"** is one tick longer for all superweapons with a silo or doors (240.0000 / 240.0167 /
   240.0083 s; neutron missile, particle cannon, Scud Storm, cruise missile, laser cannon – six quantities of the group `timed`). This is not the
   neutron missile itself but the special power or, respectively, `MissileLauncherBuildingUpdate` (package TIMING); not touched here.
3. **`ProjectileFrameGrid.PerTickToPerOriginalFrame` is a reconstruction**: exact for speeds with at most four decimal places per second (all
   checked values of the test), otherwise accurate to the last bits. A speed per frame in the catalogue would be cleaner; the input manifest forbids that in
   this phase.
4. **`StartFall` takes the sideways velocity per frame** (previously per tick); the only caller passes zero.
5. **The source rule "no value multiplied by itself" failed between `c4a0ac1` and `65b9783`** (a `perFrame * perFrame`); in
   this span the manifest and the targeted tests were green, but the full test run was not.
