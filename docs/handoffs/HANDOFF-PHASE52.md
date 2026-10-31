> **Stage:** 3 – Changes (the owner's change of course of 31 October 2026) · **Phase:** 52
> **Status:** done
> **Predecessor:** HANDOFF-PHASE51.md · **Results in:** docs/PATCH-LAYER.md, docs/plan/PROGRAMME.md, a new reference manifest
> **Side items:** the two faster hashes (D-4)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 52): one product, our patched variant (D-32, D-33, D-3, D-4).

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 32" and "Phase 51"), `docs/plan/PROGRAMME.md` §7 (the decisions of 31 October 2026), `docs/PATCH-LAYER.md`, `docs/GGC-FIXES.md`, `docs/PERF-MOVEMENT.md` §10. Prerequisite: Phase 51.

## Goal in one sentence

The game a player gets, and the yardstick of every test, is the patch layer with every adopted fix and the movement profile; the original profile stays in the code only as a switch that tells whether a defect lies in the patch or in the port.

## Starting point (measured, after Phase 51 – re-measure when this phase starts)

State `25cd630` (Phase 51 merged into `master`). Tier 4 with 56 of 56 steps ok at `25cd630` (7027 s), so this phase begins without another. Reference values of the original profile: the retail reference match (driver seed `5EED0003`) FactionAmericaAirForceGeneral at tick 41,070 (original frame 20,535; end tick 41,671), end hash `0C93FC376E2D669F`, chain `C7CFAA4B096306F1`, 26,086 commands; the stress test `9EE9F730D124C3BD` / `FD8E3547C802ACA2`; 14 of 16 AI pairings decided; every mission run keeps its outcome. Versions: `CommandTypes.Version` 20, replay 18, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3, protocol 6, transport 2; compatibility baseline 696 entries, the patch layer's 689. Ten of the sixteen pairings of the manifest are the same game at 30, 60 and 120 Hz to their ends; six part: Armored Fury America against China (frame 29,490), China against GLA (11,939), GLA against America (16,119), Bitter Winter ChinaTank against AirForce (7,095), Death Valley America against China (38,879), ChinaTank against AirForce (13,343) (F-88). The rate-equivalence suite stands at 0 / 0. Not run at the end of Phase 51 (selective by the owner's rule): `scripts/perf-profile.ps1`, the clean room, audit and reachability.

## Decisions that are fixed

- D-33: the patch layer is always on, there is one variant; the installer imports it. D-32: the adopted fixes are the default. D-3: `movement-budget` is the default. D-4: the two faster hash constructions are built.
- The manifests measure this variant. `--rules original` stays, as the switch that tells patch from port, and its tests stay as far as they are cheap.
- All reference values move once, in as few manifest commits as the causes allow.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`).
2. The content: `scripts/dev-content.ps1`, `scripts/install.ps1` and the clean room import the patch layer; `game/content` is the patched content; the two compatibility baselines become one.
3. The rule profile: fixes and movement profile are the default; `baselines/` measure them; the input manifest and the reference manifest are written anew, one commit per cause.
4. The two hash constructions (D-4), each with its measurement.
5. Everything that names "the original" as the yardstick: `docs/plan/PROGRAMME.md` §2, `docs/TESTING.md`, the checks, the rate-equivalence suite and its tolerances.
6. Tier 4, then the next phases: the rate differences of F-88, F-40, the rank system (D-15), the solo bonuses (D-6), no time limit for a human (D-8), the factory exit (D-2), the original AI's C++ half (D-16), the road order (D-7), the modern skin as the default (D-21), 120 Hz in the options (D-22), votes (D-23), the majority rule (D-24), the player key on request (D-25), D-34 variant B.

## Out of scope

Every judgement of a human; the report of the acceptance suite is read as findings when it arrives.

## Acceptance

By machine: the clean room builds the patched variant from a fresh checkout, tier 4 is green against the new manifests, and a client started without options plays the patched variant.

## Progress

Done: `docs/SIMULATION-CORE.md` §66, `docs/PATCH-LAYER.md`. The installation fetches the patch at a commit and makes the content of it, the fixes and the budgeted movement are the default, the two hash constructions are built, the manifests measure the product. Carried on: `HANDOFF-PHASE53.md`. Closing measurements: `CLAUDE.md`, paragraph "Phase 52".
