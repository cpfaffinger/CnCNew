# GeneralsGameCode fixes - catalogue, adoption and the rule profile

Phase 30, item 3 (S3-M7 (a), (c)). The community source **GeneralsGameCode** (TheSuperHackers, GPL-3.0, `references/generals-game-code`, fetched by
`scripts/get-references.ps1` at commit `d6837ff7adeb9b40787db49085d96e1fc1180424`) corrects many defects of the original engine. This document says how they
were catalogued, which of them this port adopted and how, and why the rest was not adopted. The generated list of every fix is
[GGC-FIXES-CATALOGUE.md](GGC-FIXES-CATALOGUE.md).

## 1. The catalogue

`python scripts/ggc-fix-catalogue.py` reads the reference checkout and writes [GGC-FIXES-CATALOGUE.md](GGC-FIXES-CATALOGUE.md): every comment marked `@bugfix`
or `@fix` in the code of Zero Hour (`GeneralsMD/`) and in the code both games share (`Core/`), with file, line, enclosing function, subsystem, author and date,
the first line of the community's comment, and **whether the community compiles the fix only when it may change the game's logic CRC** (inside
`#if !RETAIL_COMPATIBLE_CRC`, `RETAIL_COMPATIBLE_XFER_SAVE` or a `PRESERVE_*` switch). That last column is the community's own judgement of which fixes change
the outcome of a match - the same line this port draws between "simulation" and "everything else". `--check` compares the committed list with what the script
makes; without the reference checkout or at another commit the script ends with exit code 3 (not run, never a pass). Only places, names and one-line
descriptions are written - no code.

| Subsystem | Markers | CRC-gated (changes a match, by the community's judgement) |
| --- | ---: | ---: |
| simulation (game logic: objects, behaviours, weapons, containers, upgrades) | 109 | 55 |
| AI (unit AI states, groups, the AI player) | 37 | 24 |
| pathfinding | 11 | 7 |
| scripts | 2 | 0 |
| engine (game loop, files, memory, platform) | 56 | 8 |
| network | 9 | 1 |
| rendering | 97 | 1 |
| UI | 30 | 0 |
| tools | 3 | 0 |
| **all** | **354** | **96** |

The `Generals/` tree (the base game without Zero Hour) carries the same kind of markers again; this port is Zero Hour only and does not catalogue it.

## 2. What a fix means for this port

This port is not the C++ engine: it was written anew from the original's behaviour. A fix of the community source matters here only when this port reproduces
the defect the fix corrects. That splits the 354 markers:

- **Defects of the C++ code itself** - a use after free, a read past an array, an uninitialised variable, a leak, a crash on a null pointer, a save/load (`xfer`)
  that writes a field twice. They are not CRC-gated (they do not change a correct run). This port has none of that code: managed memory, its own state writers,
  its own renderer and user interface. **Not adopted, by kind** - nothing to rewrite. Where such a fix revealed that the original *behaves* oddly as a result,
  the behaviour is examined like a logic fix.
- **Logic fixes** - the 96 CRC-gated ones. They change what happens in a match: who gets experience, what may be entered, how a weapon picks its target. This
  port reproduces the original's behaviour (Stage 1: "the original, as it is"), so it has most of these defects too - **on purpose**: the reference manifest
  measures the original. A logic fix is adopted only as an **option of the rule profile** (section 3), never as a silent change of the original.

## 3. Adopting a logic fix: the rule profile

`CnCNew.Simulation.Rules.RuleProfile` names the rules a world follows: the **original** profile (no fix - the one every reference value is measured with) or the
original with a set of adopted fixes (`GameFix`). It is chosen where the catalogs are built - `GameData.Load(..., rules:)`, the client's and `SimTool`'s
`--rules original|fixes|NAME[,NAME...]` - and is part of the catalog (`TemplateCatalog.Rules`). Like the tick rate it is folded into the **definition
fingerprint** (`RuleProfile.Combine`; the original profile leaves the fingerprint unchanged), so every world, snapshot, replay, save and match description made
with one profile is refused by a machine with another: a room on a server names its definitions by that fingerprint, and a client with other rules cannot join.
No format and no protocol changed for it. `SimTool reference-manifest` and `input-manifest` refuse every profile but the original.

The procedure for each adopted fix: read the community's change and the original around it; write the behaviour anew in the module of this port, behind
`world.Templates.Rules.Has(GameFix.X)`; write new state only when it is set (so the original profile's snapshots and hashes stay byte for byte); a regression test
that runs the same scene under both profiles and shows the difference and only that (`tests/CnCNew.Simulation.Tests/Tests.GameFixes.cs`, at 30, 60 and 120 Hz);
the source reference in `RuleProfile.Catalogue` and in the review column of the generated catalogue.

### 3.1 Adopted (Phase 30)

| Fix | Community source (file, function) | Original | With the fix | Test |
| --- | --- | --- | --- | --- |
| `poison-kill-credit` (1) | `PoisonedBehavior.cpp`, `PoisonedBehavior::startPoisonedEffects` | the periodic poison damage is dealt in nobody's name - a kill by poison gives nobody experience | dealt in the poisoner's name: the poisoner gets the victim's experience value | `GameFixPoisonKillCredit` (also across a save) |
| `flame-kill-credit` (2) | `FlammableUpdate.cpp`, `FlammableUpdate::onDamage` | the burning damage is dealt in the burning object's own name (this port: nobody's) - a kill by fire gives nobody experience | dealt in the name of the source of the flame | `GameFixFlameKillCredit` (also across a save) |
| `no-sabotage-of-unfinished-or-sold` (3) | `Sabotage*CrateCollide.cpp` (eight modules), `isValidToExecute` | a saboteur may enter a building under construction or being sold | it may not | `GameFixNoSabotageOfUnfinishedOrSold` |

**Measured** (60 Hz, Release, `SimTool --rules fixes` against the original profile, same binary): the retail reference match ends identically - FactionGLA at
tick 32,520, chain `84077720F511C1ED` in both profiles; the definition fingerprint is `4E6F6F5623FE86DC` (original) against `E4E8D95BC0A8E1C3` (fixes). None of
the three cases happens in that match (no kill by poison or fire whose source can gain experience, no sabotage of an unfinished building). The AI match of
section 3.2 shows where the profile does change a match.

### 3.1b Adopted (Phase 32)

The review of all 83 CRC-gated markers that Phase 30 left open (three research passes that read both branches of each fix and the port's code, then each
candidate checked again by hand; one candidate failed that check, see 3.2) gave eight more fixes:

| Fix | Community source (file, function) | Original | With the fix | Test |
| --- | --- | --- | --- | --- |
| `bunker-buster-only-when-killing-self` (4) | `BunkerBusterBehavior.cpp`, `BunkerBusterBehavior::onDie` | a bunker buster busts its bunker however it dies, also when shot down on its way | only when its missile kills itself at the target | `GameFixBunkerBusterOnlyWhenKillingSelf` |
| `no-death-weapon-from-scaffold` (5) | `InstantDeathBehavior.cpp`, `InstantDeathBehavior::onDie` | a building site that dies draws and fires its death weapon | it fires none (as `FireWeaponWhenDeadBehavior` already does not) | `GameFixNoDeathWeaponFromScaffold` |
| `no-exit-from-contained-transport` (6) | `TransportContain.cpp`, `TransportContain::isSpecificRiderFreeToExit` | a rider may leave a transport that is itself inside another container | it may not | `GameFixNoExitFromContainedTransport` |
| `no-veterancy-effects-when-dead` (7) | `Object.cpp`, `Object::onVeterancyLevelChanged` | an object that dies and reaches a level in the same moment gets the level's health, weapon bonus and upgrade | it keeps the level and gets none of them | `GameFixNoVeterancyEffectsWhenDead` |
| `proportional-supply-boost` (8) | `SupplyCenterDockUpdate.cpp`, `SupplyCenterDockUpdate::action` (`getUpgradedSupplyBoostValue`) | Supply Lines and Worker Shoes pay their whole boost for every delivery, however few boxes it brings | the boost in proportion to the boxes delivered (`boost * boxes / maximum`) | `GameFixBountyAndSupplyArithmetic` |
| `neutron-blast-bounding-sphere` (9) | `NeutronMissileSlowDeathUpdate.cpp`, `NeutronMissileSlowDeathBehavior::doBlast` | the nuclear blast hits objects whose centre lies in its outer radius (2-D) | every object whose bounding sphere reaches into it | `GameFixNeutronBlastBoundingSphere` |
| `sticky-bomb-clears-trap-on-removal` (10) | `StickyBombUpdate.cpp`, `StickyBombUpdate::onDelete` | a booby trap destroyed before it goes off leaves its victim marked as booby-trapped | the mark goes with the trap | `GameFixStickyBombClearsTrapOnRemoval` |
| `bounty-rounding` (11) | `Player.cpp`, `Player::doBountyForKill` | a bounty that is a whole number of credits pays one more (5 % is a hair above 0.05) | that whole number (an epsilon of 0.0001, `WWMATH_EPSILON`, before rounding up) | `GameFixBountyAndSupplyArithmetic` |

Every test runs the scene under the original profile and under a profile that names the fix, at 30, 60 and 120 Hz. **Measured** (60 Hz, Release, two AI
pairings on Alpine Assault, 25 game minutes, each fix alone and all eleven together against the original profile; `SimTool ai-match --rules NAME`):

| Pairing | Original | Changed by | Not changed by |
| --- | --- | --- | --- |
| FactionGLAToxinGeneral against FactionChinaInfantryGeneral | stalemate, final hash `3C7D25C4459BD941`, 1,264 commands | `poison-kill-credit` and `fixes`: `86D86CDFDEA40971`, the same 1,264 commands, still a stalemate | the other ten fixes alone: the original's hash |
| FactionAmerica against FactionGLA | FactionAmerica at tick 20,370, `A60F2D9740C78670`, 1,439 commands | none | all eleven alone and together: the original's hash |

So in these two matches only the poison credit happens; the eight fixes of Phase 32 change nothing there - their cases (a shot-down bunker buster, a dying
building site with a death weapon, a carried transport, a dead unit reaching a level, a delivery with the supply boost, a neutron blast, a removed booby trap, a
bounty) do not occur in them. Their effect is shown by their tests, not by these matches; a survey that provokes each case in real matches is not made.

### 3.2 Reviewed and not adopted

| Fix | Why not |
| --- | --- |
| `ActiveBody::internalAddSubdualDamage` - subdual damage can no longer go negative | this port does not simulate subdual damage at all (a body reports it as "no effect"); the defect cannot occur. The missing subdual simulation is a fidelity debt of its own. |
| `Object::onDisabledEdge` - a power plant under construction does not change the power when it is disabled | this port has no disabled-power path for power plants (energy follows ownership and construction only); nothing to correct. |
| `GameLogic::tryStartNewGame` - random colour, position and faction from the logical seed | this port already draws them from the match description's own seed (`MatchDescription.Resolve`). |
| `AIUpdateInterface::xfer`, `GameLogic::xfer` - save order and an out-of-range field of the save | this port's saves are its own format; the original's `xfer` is not ported. |
| `UndeadBody::attemptDamage` - the Battle Bus decides by the armour-adjusted damage (Phase 32) | **rejected after the check by hand**: this port applies the armour before the body (`SimulationWorld.Combat.cs`, `DealWeaponDamage`), so the Battle Bus already compares the armour-adjusted amount for weapon damage; the fix would apply the armour twice. Damage that is not a weapon's reaches a body without armour in this port - a fidelity gap of its own. |

The other 74 CRC-gated markers that are not adopted are, by the review column of the catalogue: **17 equivalent** (this port already behaves as the fix makes the
original behave, e.g. the owner checks of rally points and special powers, an exit that is immediate), **49 not applicable** (C++ memory and crash defects,
CRC and save bookkeeping, effects decoupled from the logic CRC, code paths this port does not have - guard state machines, collision damage, batches), and
**8 candidates not adopted in Phase 32**, each with the reason in the catalogue: the tunnel network healed per tunnel, the newest battle plan's switches instead
of the aggregate, entering without a vertical overlap, a propaganda tower inside nested containers, the rally point of riders leaving a container, a bike's
trainability from its rider, a healed helicopter's rally point, battle plan bonuses on capture. Each needs state or a mechanism this port does not have yet.
**3 are not decided** (a disarming unit and stealthed mines; the exclusive disabled mask, twice): their decision needs an audit of the disabled checks of every
module. Several "not applicable" entries name fidelity gaps of this port found on the way (no power loss of disabled power plants, no ejection of passengers on
capture, no wait for a dead builder before a resume, no sniper check against empty structures) - they are Stage 1 debts, not fixes.

### 3.3 Not yet reviewed

Since Phase 32 every one of the 96 CRC-gated markers has a review entry (`python scripts/ggc-fix-catalogue.py`; entries by file and line where several markers
share a function). The 258 ungated markers are not reviewed one by one - by the community's judgement they change no match; the 70 of them in simulation, AI,
pathfinding and scripts are the next list if that judgement is to be checked. **The choice of which fixes a release
offers, and whether a profile other than the original may ever become the default, is the owner's** (it changes every reference value and is a break for every
recorded match; `docs/plan/PROGRAMME.md` §7).

## 4. Limits

- The catalogue is made from comment markers. A fix the community made without a marker is not in it; a marker inside a block of several fixes counts once per
  marker.
- "CRC-gated" is the community's judgement. A fix it did not gate can still change a match in this port if this port happens to reproduce the defect's
  visible effect; the review is what decides, not the column.
- The profile changes the definition fingerprint, not the simulation version: two builds of this port with the same profile and different code still refuse
  each other by the build identity of the match description (`SimulationId`, Phase 25).
