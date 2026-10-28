> **Stage:** 1 – Port (fidelity package C, by decision D-12's order) and 3 – Changes (S3-M7, the rest of the Reforged review) · **Phase:** 41
> **Status:** in progress (starting state: the end of Phase 40, see below)
> **Predecessor:** HANDOFF-PHASE40.md · **Results in:** docs/ATTACK-LINE-OF-SIGHT.md, docs/STEALTH-GARRISON.md, docs/GUARD-STATE.md, docs/SIMULATION-CORE.md §55, docs/plan/STAGE-1-PORT.md §3, docs/REFORGED-FIXES.md, docs/GGC-FIXES.md
> **Side items:** the Reforged candidates still open after Phase 40, the two community fixes Phase 40 re-opened, the stealth detector's terrain test
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 41): the findings of the Reforged review that are causes of their own size - the attack's line of sight (F-55), riders firing from inside a
container (F-60) and a garrisoned building passing to its riders' player (F-61), which the stealth garrison (F-54) rests on, and guarding as a state of the simulation
(F-58) - each fixed in the original profile with its manifest commit, and the Reforged candidates Phase 40 left open.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 39" and "Phase 40"), `docs/plan/STAGE-1-PORT.md` §3 (F-54, F-55, F-58, F-60, F-61), the work
list `docs/REFORGED-FIXES.md`, the three readings `docs/ATTACK-LINE-OF-SIGHT.md`, `docs/STEALTH-GARRISON.md` and `docs/GUARD-STATE.md`, and
`docs/SIMULATION-CORE.md` §54. Prerequisite: Phase 40.

## Goal in one sentence

A unit that needs a line of sight does not fire through the terrain or a structure and goes where it can (F-55), infantry in a building or a transport fires from it
(F-60), the building is its riders' player's while they hold it (F-61), a building held only by undetected stealth units shows its enemies nothing (F-54), and a guard
is an AI state every player's and every script's guard order enters (F-58) - each with a test that fails on the code
before at 30, 60 and 120 Hz and a manifest commit per cause; the open Reforged candidates are decided.

## Starting point (measured, after Phase 40 – re-measure when this phase starts)

Filled in at the end of Phase 40 (the state, the reference values, the tier that ran).

## Decisions that are fixed

- As in Phase 40: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; Reforged's
  own designs are kind D; D-32 stays the owner's.
- F-55 follows `docs/ATTACK-LINE-OF-SIGHT.md`: the terrain line and the obstacle walk first (no behaviour), then the attack gate, the acquisition filter and the search
  for a firing position, one cause each.
- Where structure footprints overlap, the cell's owner for the view is the set of them (the original keeps the first claimer, which a restored world could not know).

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 40 did not end with it on the same code).
2. F-55 (`docs/ATTACK-LINE-OF-SIGHT.md` §4): (a) the terrain line, (b) the obstacle walk, (c) the attack gate (`outOfWeaponRangeObject`), (d) the acquisition
   filter (`PartitionFilterLineOfSight`), (e) the search for a firing position (`findAttackPath`); then the community fix of `isAttackViewBlockedByObstacle` (a contact
   weapon is never view-blocked, R-09) as a `GameFix`; the scenes of (c) to (e) as cases of the rate-equivalence suite.
3. F-60, firing from inside: `OpenContain`'s fire points and `PassengersAllowedToFire` (garrisons, the Battle Bus, the Humvee, the Overlord's and the Helix's bunker,
   the internet center's hack flag), `GarrisonContain`'s positions per side; the port's `MayAct` excludes every contained object (`docs/STEALTH-GARRISON.md` §2).
   Then F-61: `GarrisonContain::recalcApparentControllingPlayer`/`onRemoving` - a garrisoned building is its first rider's player's and goes back to its team when it
   empties; one player per building (`ActionManager::canEnterObject`, `OpenContain::onCollide` throws the other player's riders out). Both move most matches; each its
   own manifest commit. Then F-54, the stealth garrison: `OpenContain::addToContainList` counts stealth-garrison riders; a building held only by undetected ones shows its enemies its apparent
   owner and an empty state (`GarrisonContain`), an enemy may try to enter it as if empty (`ActionManager::canEnterObject`), the acquisition passes it by, a detector
   reveals the riders; then R-18 (a neutron shell's kill leaves the count behind - kind B in the original).
4. F-58, guarding (`docs/GUARD-STATE.md`, eight steps there): `AIGuardMachine`, `AIGuardRetaliateMachine`, `AITNGuardMachine` (position, object, area; inner and outer range scaled by
   `GuardOuterModifierHuman`/`AI`; the return to the post; retaliation; the tunnel network's guard) as an AI state of the simulation that the player's guard command
   and the scripts enter - a command of its own (`CommandTypes.Version`, replay format, protocol); the client's standing order `StandingOrderKind.Guard` becomes that
   command; then R-35.
5. The Reforged candidates still open: R-03 (launchers in a container fire on one frame), R-24 (a stun's pose; thrown units regain control in the air), R-27 (the
   generals' data errors - compare with GeneralsGamePatch's layer first), R-34 (two jets deadlocked on one airfield; a Chinook unloading at once); each read and decided.
6. The two community fixes Phase 40 re-opened (`docs/GGC-FIXES.md` 3.1e): `Object::onDisabledEdge` for a power plant under construction (F-56 gave the port the path)
   and `UndeadBody::attemptDamage` (F-48 moved the armour into the body) - decide, and adopt behind the rule profile where they apply.
7. Side items, each registered and fixed as a cause of its own: the port's `StealthDetectorUpdate` refuses to detect behind terrain, the original's has no
   line-of-sight test (`StealthDetectorUpdate.cpp`); a rider in a transport or a tunnel cannot be stealthed in the original (`StealthUpdate`), in the port it can.
8. The Game.Tests pictures of real matches after every cause (`CNCNEW_UPDATE_BASELINES=1`; an original-skin file may change only in the pins of real matches); the
   reference match's driver seed again if a cause makes it a stalemate (the survey with `SimTool reference-match --driver-seed HEX --record-only`).
9. Documents, the next brief.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32.

## Acceptance

By machine: F-54, F-55, F-58, F-60 and F-61 fixed with tests as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its own manifest commit; every open
Reforged candidate decided with its reason; the checks the changes can reach green.

## Progress

- Step 2, F-55, built before Phase 40 was merged (on top of its last commit): (a) and (b) without behaviour; (c), (d), (e) each with its test that fails before at 30,
  60 and 120 Hz; the rate-equivalence suite 0 / 0 after each; `contact-weapon-not-view-blocked` (`GameFix` 20) under both profiles. The manifest commits of (c) to (e)
  follow the measurements.
- F-61 and the destroyed rider (§55.3, §55.4); F-60 steps 0, 2, 3, 5 (§55.5, a stand-in for the fire points); the pictures of real matches pinned again
  (`43c3be6`).
- F-60 steps 1 and 4 (§55.6): the fire points and stations read from the models (part of the world fingerprint), the garrison's 40 points and the
  fire base's stations; then (§55.7) the match on entry, the turret bone and the transports' fire points - 16 of 16 pairings decided; the Battle Bus's passed bonus
  (§55.8).
- F-54, the stealth garrison (§55.9): the count, capture, the hidden state and the acquisition, entering, the detector, no stealth in a transport, exposure, the drawing.
- F-58, guarding (§55.10): the guard command (`CommandTypes.Version` 18, replay 16), the guard of a position, an object, an area, the script guard actions, the
  player's retaliation, the rule fix `guard-answers-only-when-idle` (R-35). The detector without a line of sight; the re-opened fixes 22 and 23 (§55.11). R-03 decided (D).
- R-34 decided (§55.11, §55.12): the rule fix `takeoff-waits-for-one-jet` (24); F-62, a Chinook unloads at a place all at once and riders wait at its door; the rule
  fix `chinook-unloads-one-by-one` (25).
  R-24 rests on the shock wave, which the port lacks (F-63, open); R-27 is data, decided for the data layers.
- F-60 completed (§55.13): orders through the container, armed riders' weapon set, the garrison gun. F-58 steps 5 and 7 read again and left open (docs/GUARD-STATE.md).
