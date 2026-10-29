> **Stage:** 1 – Port (fidelity package D) · **Phase:** 42
> **Status:** done by machine (29 October 2026); the rest is in HANDOFF-PHASE43.md
> **Predecessor:** HANDOFF-PHASE41.md · **Results in:** docs/SHOCK-WAVE.md, docs/GUARD-STATE.md, docs/SIMULATION-CORE.md §56, docs/plan/STAGE-1-PORT.md §3, docs/REFORGED-FIXES.md
> **Side items:** the parts of F-58 and F-60 Phase 41 left open, F-54's two open ends
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 42): the weapon's shock wave (F-63) - the original pushes, turns and stuns the victims of eleven weapons, the port does nothing - and what Phase 41
left of guarding (F-58) and firing from containers (F-60), each fixed in the original profile with its manifest commit.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 40" and "Phase 41"), `docs/plan/STAGE-1-PORT.md` §3 (F-54, F-58, F-60, F-63),
`docs/SHOCK-WAVE.md`, `docs/GUARD-STATE.md` (its notes on steps 5 and 7), `docs/FIRE-FROM-CONTAINERS.md` §3 and `docs/SIMULATION-CORE.md` §55. Prerequisite: Phase 41.

## Goal in one sentence

A unit hit by a shock wave flies, turns, lands and is stunned as in the original (F-63), a deploying unit guards as the original's does (F-58 step 7), and a garrison can be
cleared and ordered to fire at a place (F-60) - each with a test that fails on the code before at 30, 60 and 120 Hz and a manifest commit per cause.

## Starting point (measured, after Phase 41 – re-measure when this phase starts)

State `09a1e3e` (29 October 2026), Phase 41 done by machine (`CLAUDE.md`, paragraph "Phase 41"). Retail reference match (driver seed `5EED0001`) winner FactionGLA at
tick 37,890 (original frame 18,945; end tick 38,491), end hash `6C86A27E229B1849`, chain `8E7AC06F9771E2E6`, 25,551 commands; stress test `9EE9F730D124C3BD` /
`FD8E3547C802ACA2`; 16 of 16 AI pairings decided. `CommandTypes.Version` 18, replay 16, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description 3,
session state 3, protocol 6, transport 2; compatibility baseline 701 entries, the patch layer's 694. Tier 4 ran at `09a1e3e` (in three pieces), so this phase begins
without another.

## Decisions that are fixed

- As in Phase 41: the original profile keeps the original's behaviour, including its defects (the stun of a unit shocked on the ground ends in the next frame, its
  flailing pose outlasts the stun - `docs/SHOCK-WAVE.md` §1); a fix of the original's logic is offered behind the rule profile only, written anew; Reforged's own designs
  (its stun rule, `3af3c9c6`) are kind D; D-32 stays the owner's.
- The flight of a shocked object is a 30 Hz step at every rate (decided in the first tick of the object's frame, read at the frame end); its three random draws happen in
  the tick of the damage, in victim order.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 41 did not end with it on the same code).
2. F-63 by `docs/SHOCK-WAVE.md` §3: (1) the fields and the shock wave vector of an area weapon, (2) the flight of a shocked object, (3) the stun as the original has it,
   (4) the drawing; one cause and one manifest commit each.
3. F-58 step 7: first a way to hold an internal move while a `DeployStyleAIUpdate` unit packs (the guard's walk and the attack-move's chase must not end), then the
   deploy in guard IDLE and the jet's reload-resumable guard; F-58 step 5 (the tunnel network guard) only if the original AI's library needs it in a measured match.
4. F-60's rest: clearing a garrison (`KILL_GARRISONED`, `GarrisonHitKill*`, `AllowAttackGarrisonedBldgs`), an attack on a position through the container
   (`groupAttackPosition` - needs the port's first command for it), the Chinook's re-issue of its victim to its riders.
5. F-54's open ends: `CAN_ATTACK` of a hidden stealth garrison and `onCollide`'s ejection.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32, the patch layer's data comparison of R-27.

## Acceptance

By machine: F-63 built with tests as above (what does not fit is named with its reason); F-58 and F-60 closed or each remaining part named with its reason; both
manifests of the original profile unchanged except by a cause with its own manifest commit; the checks the changes can reach green.

## Progress

- F-63 in four steps (§56.1): the inputs, the flight, the stun, the drawing - and the flight's frame end (`cae779e`).
- F-58 step 7 (§56.2): a deploying unit on guard (`b9b0a8e`, manifest `f64949d`); a jet takes off for its guard and guards on after reloading (`c27533d`, no field moved).
- F-60's rest (§56.3): `DAMAGE_KILL_GARRISONED` (`59e6ff2`), the shell's `GarrisonHitKill` (`1950f68`), the Chinook's re-issue (`86925d9`); F-54's open ends covered.
- Not built here, carried into `HANDOFF-PHASE43.md`: an attack on a place (F-64, read in `docs/ATTACK-POSITION.md`), the missile's `GarrisonHitKill`; F-58 step 5 (only the original AI's library) and the flight deck (a shell) stay open. Closing measurements: `CLAUDE.md`, paragraph "Phase 42".
