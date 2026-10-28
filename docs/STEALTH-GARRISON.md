# The stealth garrison (F-54) and what it rests on (F-60, F-61)

This document is the reading behind F-54 (`docs/plan/STAGE-1-PORT.md` §3, Reforged R-18) and the two findings it led to: riders do not fire from inside a container
(F-60) and a garrisoned building does not pass to its riders' player (F-61). It was written at the start of Phase 41, before any of it was built.

Sources read: EA original `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine` (paths below relative to it),
GeneralsGameCode (`references/generals-game-code`), Zero Hour Reforged (`references/zero-hour-reforged`), the port
(`src/`, HEAD `a1ac2af`).

**Headline finding.** F-54 sits on top of two foundations the port does not have, and neither is in the fidelity register:

* **F-new-a, the garrison's team swap.** In the original a garrisoned building *belongs to its occupants' player*
  (`GarrisonContain::recalcApparentControllingPlayer` calls `getObject()->setTeam(first rider's player's default team)`,
  GarrisonContain.cpp 1206-1242; on the last removal `onRemoving` puts back `m_originalTeam`, 1675-1687), and gets
  `OBJECT_STATUS_CAN_ATTACK` (`onContaining` 1626; cleared 1690). The "apparent controlling player" is defined *against*
  that swap: it hides the new owner and shows the original team. The port never swaps: `GarrisonContain`'s summary says so
  (`src/CnCNew.Simulation/Modules/BuiltIn/ContainModules.cs` 267, "the apparent-controlling-player/team-swap system (no team
  system)"); `PutIntoContainer` (`World/SimulationWorld.cs` 417-425) only adds the rider. A garrisoned civilian building
  stays `PlayerId.System`: `FindMoodTarget` only walks enemy players' entities and `ConsiderMoodTarget` returns on a system
  owner (`World/SimulationWorld.Acquisition.cs` 421-427, 453), so **nobody ever auto-acquires a garrisoned civilian
  building in the port** - the port already "hides" every garrison from everybody, allies included.
* **F-new-b, firing from a garrison.** `docs/SIMULATION-CORE.md` (line ~1518) and `MayAct` (`SimulationWorld.Acquisition.cs`
  246-249) say it: contained objects acquire nothing; `PassengersAllowedToFire` is recorded and unread.
* (Part of F-new-a) **one player per garrison.** Original `ActionManager::canEnterObject` (ActionManager.cpp 678-700) lets a
  unit into a building that is not its player's only when it holds no non-stealth rider and is no faction structure, and
  `OpenContain::onCollide` (OpenContain.cpp 782-835) ejects every rider of another player when an enterer touches the
  building. The port has neither: `GarrisonContain.IsValidContainerFor` (ContainModules.cs 279-285) checks kind,
  relationship to the (unswapped) owner, damage and capacity only, so two players can share one building.

So implementing only the F-54 consumers would change almost nothing observable (the port's default is already "hidden").
The plan in section 3 therefore starts with the swap as its own cause (to be registered as its own finding).

---

## 1. What the original does

### 1.1 The count `m_stealthUnitsContained` (OpenContain.h 179, 264; one per `OpenContain`, xfer'd at OpenContain.cpp 1678)

| Event | Code | Count |
|---|---|---|
| a rider is added (every path: `addToContain` -> `addToContainList`) | OpenContain.cpp 367-375 | `++` if the rider `isKindOf(KINDOF_STEALTH_GARRISON)` - the KindOf only; the rider's stealth state is not looked at |
| a rider is removed (`removeFromContain`, `removeAllContained`, `harmAndForceExitAllContained`, exits via door / in a hurry) | `removeFromContainViaIterator` 630-660 | `--` if STEALTH_GARRISON; with `exposeStealthUnits` the rider's `StealthUpdate::markAsDetected()` is called |
| a rider dies inside | nothing at death; the dead rider stays in the list until `Object::onDestroy` calls `removeFromContain(this)` (Object.cpp 746-753) | `--` then |
| the container dies | `OpenContain::onDie` 857-875 -> `GarrisonContain::removeAllContained(FALSE)` 1467-1492 | `--` per rider, no exposure |
| `killAllContained` (neutron shell `NeutonBlastBehavior.cpp` 131, bunker buster without occupant weapon `BunkerBusterBehavior.cpp` 221) | OpenContain.cpp 435-462: `m_containList.erase`, `m_containListSize--`, `onRemoving`, `onRemovedFrom`, `kill` | **not lowered** (R-18) |
| `harmAndForceExitAllContained` (bunker buster with occupant weapon) | 467-493: `removeFromContain(rider, true)` | `--`, riders marked detected |
| load | `loadPostProcess` 1773-1823 pushes riders straight onto the list | the saved count is kept (stale part survives a save) |
| a rider becomes detected / undetected | - | unchanged (the count is a KindOf count) |

Exposure (`markAsDetected` on STEALTH_GARRISON riders) happens on: `removeFromContain(..., TRUE)` callers -
`harmAndForceExitAllContained`, `onCollide`'s eject of riders without AI (829), `Object::defect` when `isKickOutOnCapture`
(Object.cpp 6211-6216), `SpecialAbilityUpdate` capture of a garrisonable building (SpecialAbilityUpdate.cpp 1454-1456),
`TunnelContain` (139, 162); `markAllPassengersDetected` (OpenContain.cpp 1346-1367) via `aiEvacuate(TRUE)` /
`aiEvacuateInstantly(TRUE)` (AIUpdate.cpp 3894-3932; callers with TRUE: ActiveBody.cpp 415 combat bike,
RiderChangeContain.cpp 197; every player/script evacuate passes FALSE); `onCollide`'s eject of riders with AI
(817-825: `markAsDetected` then `aiExit`).

### 1.2 The hidden flag `m_hideGarrisonedStateFromNonallies` (GarrisonContain only; xfer'd 1837)

* Set in `recalcApparentControllingPlayer` (1206-1242): with riders,
  `hide = !firstRider.DETECTED && stealthCount == containCount`; without riders `hide = false`. Only the **first** rider
  (list order = entry order) is asked, and only its `OBJECT_STATUS_DETECTED` bit - **not** `STEALTHED`. A Jarmen Kell who
  fires from the building and loses `STEALTHED` keeps the building hidden as long as he is not DETECTED.
* `onRemoving` (1653-1709): empty -> false; `stealthCount != containCount` -> false; then recalc anyway.
* Recalc is called on contain (`onContaining` 1642), remove (`onRemoving` 1709), `removeAllContained` 1490,
  `exitObjectViaDoor` 1608, and **by the rider's own `StealthUpdate::update` whenever its DETECTED bit flips** while it is
  in a garrisonable container (StealthUpdate.cpp 812-825). DETECTED itself is set/cleared only in `StealthUpdate::update`
  from `m_detectionExpiresFrame` (778-810); `markAsDetected` (870-925) only moves the expiry. So in effect the flag is
  the derived value `count > 0 && stealthCount == count && !Riders[0].DETECTED`, evaluated at the rider's stealth update.
  (Exception without consequence here: a disguise ending clears DETECTED without a recalc - only bomb trucks disguise.)
* `getApparentControllingPlayer(observer)` (1191-1201): if hide and `m_originalTeam`, owner and observer exist, and
  `owner->getRelationship(observer's default team) != ALLIES` (the *owner's* view of the observer), it returns
  `m_originalTeam`'s player; otherwise the real owner. `OpenContain` returns NULL (OpenContain.h 143), so only garrisons
  have an apparent owner. For a player's own faction garrison the original team is that player, so hiding changes nothing
  of its apparent owner.

### 1.3 What "hidden" means to each consumer

| Consumer | Original code | Effect for a non-allied observer of a hidden garrison |
|---|---|---|
| Drawable `MODELCONDITION_GARRISONED` | GarrisonContain.cpp 1244-1268 | set only if the first rider is DETECTED or apparent(local) == owner -> looks empty (client only, local player) |
| House/indicator colour | 1270-1279; InGameUI.cpp 2452-2459 (tooltip colour) | original team's colour (client) |
| Mouse-over owner | InGameUI.cpp 2318-2324 | original team (client) |
| Radar colour | Radar.cpp 450-458 | apparent owner's colour (client) |
| Command bar of a selected building | ControlBar.cpp 1773-1797 | a neutral garrisonable building can be "peeked into"; with apparent = neutral the non-ally may look into it (client) |
| Player attack order | WeaponSet.cpp 575-595 (`getAbleToAttackSpecificObject`) | with no force-attack and `CMD_FROM_PLAYER`, rejected if `source team -> apparent != ENEMIES` (unless `SCRIPT_TARGETABLE` and not allied). AI/script sources are not rejected |
| Capture | ActionManager.cpp 1078-1093 (`canCaptureBuilding`) | refused if non-stealth riders > 0; refused if `appearsToContainFriendlies` (80-95: `obj team -> apparent != ENEMIES`) |
| Hacks | ActionManager.cpp 1170, 1300, 1373 | `appearsToContainFriendlies` refuses vehicle hack, cash hack, building hack |
| Enter / garrison | ActionManager.cpp 678-700 (`canEnterObject`) | building not of the enterer's player: non-stealth > 0 -> refuse; faction structure -> refuse; stealth > 0 and non-stealth == 0 -> **capacity not checked** (a full hidden garrison can be "entered"). Checked at order (`aiEnter` AIUpdate.cpp 3779), at `AIEnterState::onEnter` 6229 and every `update` 6312 (failing against an ENEMIES goal turns into an attack, 6318-6340), Player.cpp 2219 (script garrison). On touching, `OpenContain::onCollide` 782-835 marks the hidden riders detected and orders them out (`aiExit`), then adds the enterer only if `isValidContainerFor(other, TRUE)` |
| `canGarrison` / `canPlayerGarrison` (UI) | ActionManager.cpp 2012-2080 | real owner and real count; no stealth exception |
| Target acquisition, `AI::findClosestEnemy` (AI.cpp 587-660) used by `getNextMoodTarget` (AIUpdate.cpp 4609-4643), hunt (AIStates.cpp 7192, 7363), `CommandButtonHuntUpdate` 274, `SpectreGunshipUpdate` 523, `EnemyNearUpdate`, `CheckpointUpdate` | filters in order: `LiveMapEnemies` (real relationship ENEMIES, AI.cpp 506-528); `RejectBuildings` unless the `ATTACK_BUILDINGS` qualifier (PartitionManager.cpp 4922-4977); range/LOS/attack/fog/insignificant; `StealthedAndUndetected(me,false)` always (5501-5570) | `RejectBuildings`: relationship `myPlayer -> apparent` must be ENEMIES, *before* the computer player's "acquire enemy buildings" exception - so humans and computers alike pass a hidden garrison by. `StealthedAndUndetected`, building branch (5543-5566): rejects a container when `stealthCount == count`, the first rider is not DETECTED and `me team -> apparent == ENEMIES`. For a hidden garrison the apparent owner is the original team (normally neutral civilians), so this filter does **not** reject it; a unit with `AutoAcquireEnemiesWhenIdle ... ATTACK_BUILDINGS` (no `RejectBuildings`) therefore *does* acquire a hidden garrison whose original team is not its enemy. The branch bites when the apparent owner is an enemy (original team hostile, or a player's own faction garrison) |
| `PartitionFilterInsignificantBuildings` | 4982-5000 | real count (a garrison with riders is significant) |
| Script condition "player has N garrisoned buildings" | ScriptConditions.cpp 2405-2425 | real owner (team members) and real count - not hidden |
| AI's own evaluation | no reference to the count or the flag in `AIPlayer`/`AISkirmishPlayer`; only via `findClosestEnemy` | - |
| Attack state bail-out on a de-garrisoned neutral building | AIStates.cpp 5612-5630 | real count |
| Detector | below | - |

### 1.4 The detector (StealthDetectorUpdate.cpp)

* Candidate filter `PartitionFilterStealthedOrStealthGarrisoned` (117-142): STEALTHED objects, or a garrisonable container
  with `stealthCount > 0` (any, not "all"). Plus `PartitionFilterRelationship(self, ENEMIES|NEUTRAL)` on the **building's
  real owner** (after the swap: the garrisoner), the `ExtraRequiredKindOf`/`ExtraForbiddenKindOf` kind filter **on the
  building**, same map status; range `FROM_CENTER_2D` to the building's centre (195-201). No line-of-sight test in this code.
* Riders of an enclosing container are out of the partition (`addOrRemoveObjFromWorld`, OpenContain.cpp 344-346), so they
  are never found directly.
* For a found object without its own `StealthUpdate` (a civilian building) the else branch (332-352) walks the riders and,
  for **every rider that has a StealthUpdate** (STEALTH_GARRISON or not, stealthed or not) whose player is not the
  detector's and not allied to it, calls `markAsDetected(updateRate + 2)` (the direct path uses `+ 1`, 302).
* So detecting the building reveals the riders; the riders' DETECTED bits then flip in their `StealthUpdate::update`,
  which recalcs the building -> not hidden (first rider detected). A rider's DETECTED status is exactly what matters.
* `StealthUpdate::allowedToStealth` (389-396): a rider inside a **non-garrisonable** container may not stealth; inside a
  garrison it may. `getDisabledTypesToProcess` = HELD for both modules, so they run while held.

### 1.5 Not determined

* The building branch of `PartitionFilterStealthedAndUndetected` runs for **every** non-stealthed container, and for an
  empty one `0 == 0` holds and it dereferences `begin()` of an empty `std::list` (5552-5553). What the shipped binary read
  there is undefined; the game evidently did not crash. The port should treat an empty container as "not hidden" and say so.
* Whether an enterer that ejected a hidden full garrison gets in in the same frame (`aiExit` is a state of each rider;
  `isValidContainerFor(other, TRUE)` is asked right after) or on a later touch - not traced.
* The order between a detector's mark and the rider's `StealthUpdate::update` within one frame (DETECTED the same or next
  frame) depends on module update order; the port sets DETECTED at the mark (`StealthUpdate.MarkDetected`, Phase5Modules.cs 136-143).
* Which shipped units have the `ATTACK_BUILDINGS` idle bit (the data has 20 such lines) - not mapped to names.

### 1.6 Shipped objects with KINDOF_STEALTH_GARRISON (game data, names only)

21 object definitions: `AmericaInfantryPathfinder`, `AirF_`, `Lazr_`, `SupW_`, `Boss_`, `CINE_American` Pathfinder variants;
`GLAInfantryJarmenKell` and its `Chem_`, `Demo_`, `Slth_`, `Boss_`, `GC_Chem_`, `GC_Slth_`, `CINE_` variants;
`GLAInfantryHijacker`, `Demo_`, `Slth_`, `GC_Chem_`, `GC_Slth_` Hijacker; `GC_Slth_GLAInfantrySniper`; one more match
attributed by my scan to `CINE_CheeringHijacker` (to verify). `ObjectReskin` blocks were not scanned.

---

## 2. The port today

| Item | Where | State |
|---|---|---|
| STEALTH_GARRISON | only in `CnCNew.Data/Definitions/NameTables.cs` 28 (the KindOf name table) | read by nothing |
| Rider list | `ContainModuleBase.Riders` (`List<EntityId>`, entry order), ContainModules.cs 65; `Add` 141-155, `Remove` 157-164, `OnDie` 166-178 (`EjectAll`), `PruneDead` 195-202 (drops dead riders at the container's update, not at destroy) | fine as the basis of a derived count |
| Team swap, original team, CAN_ATTACK on garrison | - | **missing** (F-new-a) |
| Firing from a garrison | `MayAct` Acquisition.cs 246-249 | **missing** (F-new-b) |
| Enter checks | `TryEnterContainer` SimulationWorld.cs 409-415; command checks 888-895 (`EnterContainerCommand`), 945-958 (`CheckMoveAndEnter`), execution 1256-1266; arrival `AIUpdateInterface` Phase5Modules.cs ~1370-1408 | no "other player's garrison" rule, no eject on touch |
| Capture | `StagedCaptureRules.CanCapture` SpecialAbilityStaging.cs 61-68; `CheckCapture` SimulationWorld.PowerAndCapture.cs 47-68; `TriggerEffect` 391-401 (garrison -> `EvacuateContainer`, no exposure) | no garrison/non-stealth rule, no `appearsToContainFriendlies` |
| Player attack order | `FireWeaponCommand` check SimulationWorld.cs 856-866: no relationship check at all (the cursor decides: `CnCNew.Presentation/Input/CommandModeController.cs` 184 `AreEnemies`, `SimulationQueries.CanFireAt` 64-68) | apparent-owner rejection belongs to the presentation's cursor/command choice |
| Acquisition | `FindMoodTarget` 395-430 (enemy players' entities only), `ConsiderMoodTarget` 451-481, `IsAttackable` 289-291, `AcquiresStructure` 492-497 (`buildings = computer || ATTACK_BUILDINGS`, line 405) | merges the original's two cases (computer: filter applied but enemy buildings accepted after the apparent-relationship test; ATTACK_BUILDINGS: filter not applied) - the split is needed for F-54 |
| Vision | `CanPlayerSee` SimulationWorld.Vision.cs 123-130: contained -> not visible to non-allies; STEALTHED && !DETECTED -> not visible | ok; the building itself is never hidden by this, as in the original |
| StealthUpdate | Phase5Modules.cs 85-215: STEALTHED by delay/conditions; `MarkDetected` sets DETECTED **at once** and an expiry; `Update` clears it at expiry | lacks `allowedToStealth`'s "not inside a non-garrisonable container" (StealthUpdate.cpp 389-396) - side finding; no recalc of a container needed if the hidden state is derived |
| StealthDetectorUpdate | Phase5BlockerModules.cs 602-660 (`Update` 626-650) | scans `world.Entities` for STEALTHED, **including contained riders** (at the building's position), relationship to the rider's owner, kind filter on the rider, terrain line of sight; no building branch; its summary (595-598) is out of date ("an object is never contained") |
| Kill-all | `AirWeaponModules.KillAllContained` AirWeaponModules.cs 79-88 (calls `contain.Remove`, then `world.Kill`); used by `NeutronBlastBehavior.BlastObject` 447-460 and `BunkerBusterBehavior` 377; `harmAndForceExitAllContained` inline at 368-375 (no detection mark) | R-18 needs this path to *not* lower the count in the original profile |
| Script condition | `ScriptEngine.Extended.cs` 64-65 counts buildings **owned** by the player with riders - without the swap a civilian garrison never counts | changes with F-new-a, not with F-54 |
| Presentation | `ConditionDeriver.cs` 103-104 (GARRISONED for any rider), `FrameProjector.cs` 155, 341-375 (house colour = `capture.Owner`, also the minimap dot colour via `drawable.TeamColor`, `ClientRuntime.cs` 792; fog memory of structures 349-357 by owner), `SelectionModel.cs` 83 (relationship colour), `CommandModeController.cs` 184, 542 (attack cursor / relationship), `SimulationQueries.CanFireAt` 64-68, `CommandBarModel.cs` 374 | each would read an "apparent owner for viewer" query instead of `Owner`/`ContainCount` |

---

## 3. Proposed plan (one cause per step; each with a synthetic scene that fails before it)

Common rules: everything in `CnCNew.Simulation`; Fixed point only; the rider order is `Riders` (entry order) - never a
dictionary; new state written only when it differs from the default, so worlds without a stealth rider or a swap keep
their snapshots. Decisions follow the frame convention (observers read at the frame end; self-acquisition once per
original frame). The rate-equivalence suite has no garrison case (`docs/SIMULATION-CORE.md` ~2186) - add one in step 3 or 5.

**Step 1 (prerequisite, register as its own finding): the garrison takes its occupants' player.**
`GarrisonContain.Add`: on the first rider remember `_originalOwner` (written only while set) and
`world.TransferOwnership(building, Riders[0].Owner)` (the first rider decides, as `recalc` reads `begin()`); set
`CAN_ATTACK`. `Remove`/`EjectAll`/`PruneDead`/kill-all: when the list becomes empty, transfer back to `_originalOwner`,
clear it and `CAN_ATTACK`. In the same cause or right after (it is the same original mechanism): the
`canEnterObject` rule for a building not of the enterer's player (refuse with non-stealth riders - at first "any riders",
since there is no count yet - refuse a faction structure) in `TryEnterContainer`, the two command checks and the arrival,
and `onCollide`'s ejection of other players' riders. Tests: a civilian garrison entered by player 1 belongs to player 1
and returns to System when empty; player 2 is refused while player 1's rider is inside; enemy units with the attack mood
acquire the garrisoned building (before: never). 30/60/120 Hz: ownership changes in the tick of entry/exit, which the
port already places at the frame boundary (`PutIntoContainer` settles the frame). Expect manifest changes (mission runs
that garrison by script, e.g. those named in `docs/SIMULATION-CORE.md` ~1823-1825) - its own manifest commit.
(`TransferOwnership` moves energy and battle-plan effects; a civilian building has none, a faction bunker keeps its owner.)

*Firing from a garrison (F-new-b) is a separate, larger cause; F-54's visible payoff (a Jarmen Kell sniping unseen) needs
it, but every F-54 step below is testable without it.*

**Step 2: the count.** `ContainModuleBase.StealthUnitsContained` = number of `Riders` whose template `IsKindOf("STEALTH_GARRISON")`
plus `_strandedStealth` (0 in every normal path; written only when non-zero - the R-18 hook). Derived, so `Add`/`Remove`/
`PruneDead`/death need no bookkeeping and no snapshot changes. Test: add two Jarmen-Kell-like synthetic riders and a normal
one, remove, kill one inside -> counts 2/1/... (fails before: the property does not exist; better, make the step-3 test the
failing one and keep this step inside it if a stand-alone failing test is wanted).

**Step 3: the hidden state and the apparent owner.** `GarrisonContain.HidesGarrison` = `count > 0 && stealthCount == count
&& !Riders[0].DETECTED` (derived; equals the original's flag because the original recalcs on every contain change and on
every DETECTED flip of a rider - section 1.2); `ApparentOwner(PlayerId observer)` = `_originalOwner` when hidden and
`RelationshipOf(owner, observer) != Allies` (the owner's view, as 1195-1199), else the owner; `IContainerModule` default
returns null (non-garrison). Read the first rider's DETECTED at the frame end of the decision like every other status read.
Consumers in the simulation, each a small test:
  a. acquisition: split `AcquiresStructure`'s flag into (computer) and (ATTACK_BUILDINGS); for the reject-buildings case
     test the relationship to `ApparentOwner(entity.Owner)` before the computer exception (PartitionManager.cpp 4934-4977).
     Scene: player 1's hidden garrison, player 2's idle tank in range -> not acquired; with a detected first rider -> acquired.
  b. `PartitionFilterStealthedAndUndetected`'s building branch for every container (all candidates, including the
     ATTACK_BUILDINGS path): reject when `stealthCount == count > 0`, `!Riders[0].DETECTED` and the relationship of the
     unit's team to `ApparentOwner` is Enemies (empty container: not hidden - 1.5). Scene: a hidden garrison whose original
     owner is an enemy of the looking unit, ATTACK_BUILDINGS unit -> not acquired.
  c. capture: `CanCapture` refuses a garrisonable target with non-stealth riders, and one whose apparent owner the actor's
     team does not see as an enemy (ActionManager.cpp 1078-1093).
  d. enter: replace step 1's "any riders" by "non-stealth riders" (`count - stealthCount`, signed - the R-18 case makes it
     negative), and skip the capacity check when `stealth > 0 && nonStealth == 0` (ActionManager.cpp 689-700); on touch,
     mark the ejected STEALTH_GARRISON riders detected (OpenContain.cpp 817-825). Scene: player 2's infantry ordered into
     player 1's full hidden garrison -> order accepted, riders marked detected and ejected.
  Rate note: all four are decisions taken where the port already takes them (acquisition per original frame, commands at
  submission, entry at the frame boundary); the only time-varying input is DETECTED.

**Step 4: the detector's building branch.** In `StealthDetectorUpdate.Update`: skip contained candidates in the direct
scan when their container is enclosing (all shipped `GarrisonContain`s but four `IsEnclosingContainer = No` and all
transports; the key is `Opaque` in `ModuleCatalog.cs` 640 and would need typing), and add the original's second
candidate kind - a garrison container with `stealthCount > 0`, its owner Enemies/Neutral to the detector, the kind filter
on the building, centre distance to the building - whose riders with a `StealthUpdate` not of the detector's player and
not allied get `MarkDetected(rate + 2 frames)`. Scene: a detector (synthetic, `DetectionRange`) moves toward a hidden
garrison: hidden until the frame it comes in range, then `HidesGarrison` false and acquisition (3a) picks the building;
assert the same original frame at 30/60/120 Hz (the `+2` is `UpdateRateTicks + 2 original frames`, like the existing
`+1` conversion at 646). Whether to keep the port's terrain line-of-sight test for this branch: the original has none
in this code (decide and document).

**Step 5 (side, own cause): `allowedToStealth` inside a non-garrisonable container.** A rider in a transport/tunnel loses
STEALTHED (StealthUpdate.cpp 389-396). Small, testable, may move manifests if any run boards stealthed units.

**Step 6: exposure on the remaining paths** (one cause): `markAllPassengersDetected` for the evacuations that pass TRUE
(the combat bike in `ActiveBody`, `RiderChangeContain`), capture of a garrison (`TriggerEffect`, SpecialAbilityUpdate.cpp
1456), `harmAndForceExitAllContained` (AirWeaponModules.cs 368-375), the defect path. Test per path: the rider is DETECTED
after it.

**Presentation (after steps 1 and 3; only reads simulation queries):** `ConditionDeriver` GARRISONED when the viewer is
allied/owner or the first rider is detected (`detected || apparent == owner`); house/minimap colour, tooltip owner and the
relationship colour of `SelectionModel` from `ApparentOwner(viewer)`; the attack cursor (`CommandModeController` 184/542,
`SimulationQueries.CanFireAt`) refuses a non-forced attack on a building whose apparent owner is not an enemy
(WeaponSet.cpp 575-595 - the port has no simulation-side relationship check for `FireWeaponCommand`, so this is a
client rule); the fog memory of structures (`FrameProjector` 349-357) keyed by apparent owner. Golden picture hashes of
the client would move only for scenes with a stealth garrison (none expected).

## 4. R-18

**Confirmed.** EA `OpenContain::killAllContained` (OpenContain.cpp 435-462) erases each rider and lowers
`m_containListSize` but never `m_stealthUnitsContained`; `GarrisonContain` does not override it; `onRemoving` then resets
the team and `hide` because `getContainCount()` is 0, but the stale count remains and is saved (xfer 1678; EA's
`loadPostProcess` keeps it). Consequences in the original after a neutron shell or a bunker buster without occupant
weapon clears k stealth riders: with n later riders and s of them stealth-garrison,
`nonStealth = n - (k + s)` is negative or zero, so (a) an enemy may enter a building held by up to k ordinary riders
(`nonStealth > 0` fails) and on touching ejects them - Reforged's words "let the next enemy in on top of new riders";
(b) when `n == k + s` the building counts as "all stealth": a garrison of exactly k ordinary infantry is hidden from
enemies (`hide`, apparent owner, `RejectBuildings`) and passed by acquisition; (c) the detector still treats the empty
building as stealth-garrisoned (scans it, marks nothing). GeneralsGameCode does **not** fix the count (its
`killAllContained` restarts iteration but still skips the decrement, GGC OpenContain.cpp 486-520) but recounts on load
(`loadPostProcess` 1958-1975 adds through `addToContainList`) - a load heals it there. Zero Hour Reforged lowers the count
in `killAllContained` (its OpenContain.cpp 497-501) and in its reworked `processDamageToContained` (1565-1567; the EA
version leaves dead riders in the list for `removeAllContained`, so that second change is part of Reforged's restructure,
not a second EA defect).

**Port:** keep the defect in the original profile: `AirWeaponModules.KillAllContained` adds the number of removed
STEALTH_GARRISON riders to the container's `_strandedStealth` (step 2's field, saved only when non-zero, kept across
save/load as EA does). A rule fix behind the profile (next free `GameFix`; kind B; name e.g.
`kill-all-contained-uncounts-stealth`) skips that addition. Test under both profiles at 30/60/120 Hz and across a save:
neutron-clear a building with one stealth rider, garrison it with one ordinary rider of player 1, then (original) player
2's ordinary infantry may enter and the building hides from player 2 / (fix) refused and not hidden. It belongs after
steps 1-3 (F-54 comes first, as the register says).
