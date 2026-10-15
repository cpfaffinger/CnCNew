> **Stage:** 3 – Changes · **Milestone:** S3-M1 AI kinds, difficulty and tactics · **Phase:** 33
> **Status:** current (starting state measured 15 October 2026 at `8d57fb7`; not begun)
> **Predecessor:** HANDOFF-PHASE32.md · **Results in:** docs/SKIRMISH-AI.md (new), docs/SIMULATION-CORE.md, docs/SKIRMISH-AI-ORIGINAL.md, docs/GGC-FIXES.md
> **Side items:** S3-M7 rest – the eight candidate fixes and the three undecided markers of `docs/GGC-FIXES-CATALOGUE.md`
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 33): tactics profiles and difficulty levels of the built-in AI, a stronger default AI, and the rest of the fix review

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md` (paragraphs "Phase 30" and "Phase 32"),
`README.md`, `docs/plan/PROGRAMME.md` (§5 order, §7 decisions, above all D-9 and D-10 of the old list, D-31 to D-33), `docs/plan/STAGE-3-CHANGES.md` (S3-M1,
S3-M7), `docs/SKIRMISH-AI-ORIGINAL.md` (§8 proposals beyond the original), `docs/AI-OBSERVED-2026-10-08.md`, `docs/SIMULATION-CORE.md` §35, §36 and §50,
`docs/GGC-FIXES.md` and the final report of Phase 32. Prerequisite: Phase 32 (the rule profile, the patch layer that plays).

## Goal in one sentence

The built-in skirmish AI gets named tactics profiles (at least rush, turtle, balanced, eco) and difficulty levels that differ by what the AI may know and how
fast it decides - never by a rule bonus a human does not get -, each a selectable option of a slot that is part of the match description and the session
state, measured against every faction; and the eight candidate fixes of the review are adopted or rejected.

## Starting point (measured, after Phase 32 – re-measure when this phase starts)

- **State:** `master` at `8d57fb7` plus the documents of the end of Phase 32; tier 4 with 54 of 54 steps ok at `8d57fb7` (6,340 s). No format, no version and no
  reference value changed in Phase 32: `CommandTypes.Version` 17, replay 15, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description encoding 2,
  session state 2, protocol 5, transport 2; both manifests unchanged since Phase 30 (retail reference match FactionGLA at tick 32,520, `6052051714309B17` /
  `84077720F511C1ED`; 16 AI runs 14 decided).
- **The rule profile** has eleven fixes (`GameFix` 1 to 11 in `src/CnCNew.Simulation/Rules/RuleProfile.cs`; `--rules fixes` sets all); each has a test under both
  profiles at 30, 60 and 120 Hz (`tests/CnCNew.Simulation.Tests/Tests.GameFixes*.cs`). Of two measured AI pairings only `poison-kill-credit` changes a match
  (`docs/GGC-FIXES.md` 3.1b).
- **The review** (`scripts/ggc-fix-catalogue.py`, `docs/GGC-FIXES-CATALOGUE.md`): all 96 CRC-gated markers have an entry - 18 adopted, 17 equivalent, 49 not
  applicable, **8 candidates deferred** with the reason, 1 rejected (the Battle Bus's armour check: the port applies armour before the body), **3 undecided**.
  The deferred and undecided rows are item 6 of this brief.
- **The patch layer** (`docs/PATCH-LAYER.md`): `AssetTool import-layer auto game/content-patch --patch references/generals-game-patch/Patch104pZH` makes it; its
  definitions compile (`baselines/compatibility-patch.json`, 706 entries), it plays deterministically, a server refuses a client of the other content
  (`scripts/check-patch-layer.ps1`, step `patch-layer`). Catalogue 207 module types, 185 bound (`StatusBitsUpgrade`, `SabotageSupplyDropzoneCrateCollide` are
  used by the patch only).
- **The built-in AI**: `SkirmishAi` with `SkirmishAiOptions.Composition`; `MatchSlotDescription.AiStrength` is carried and counted, not obeyed; the four
  eight-player runs of the manifest end in stalemates within their budget; the survey tools (`ai-survey`, `ai-census`, `composition-test`, `rate-distribution`)
  exist. Performance at `8d57fb7`: 8 players 851 µs per tick on average, maximum 134 ms (D-3/D-6 territory, not this phase).
- **Not verified, carried on:** no human has played on the patch layer or seen an adopted fix; a second physical machine and a real network (E1, D-10); the
  owner's open decisions D-2 to D-8, D-10, D-17 to D-20, D-23 to D-33.

## What is already done – do not build twice

- The built-in `SkirmishAi` (roles from modules, build planning, supply centres at safe sources, group attacks, composition by exchange value,
  `SkirmishAiOptions.Composition`), its state in the session (`SkirmishAi.State.cs`, Phase 24), the original AI with three levels as an option
  (`ScriptedSkirmishSession`), `--ai builtin|original`, `--difficulty easy|medium|hard` per slot, the surveys (`ai-survey`, `ai-census`, `composition-test`,
  `ai-match --trace`), the AI table of the manifest (16 pairings, 14 decided at 60 Hz).
- `MatchSlotDescription.AiStrength` and `AiComposition` exist in the match description (encoding 2); the strength is counted, not obeyed.
- The rule profile (`--rules`) is the model for an option that changes the simulation and keeps the original profile's manifest.

## Decisions that are fixed

- The built-in AI stays the default (decision of 10 October 2026); the original profile - no tactics option set, medium strength - keeps the reference manifest.
- An option that changes an AI's behaviour is part of the match description (both machines of a match must decide alike) and of the session state.
- Open, with the default this brief uses: D-32 (which fixes a release offers) - the fixes stay opt-in; D-33 (the patch layer for players) - not offered.

## Brief (in this order)

1. **Reproduce the starting state** (tier 4 unless Phase 32 ended with it on the same commit).
2. **Tactics profiles** of the built-in AI: rush, turtle, balanced, eco - each with its build order, composition rule, attack timing and defence posture; an
   option per slot in the match description, the shell's setup and `ai-match`; written and read in the AI's session state.
3. **Difficulty levels**: by information (what it may see) and decision speed; `AiStrength` obeyed instead of counted. No resource or damage bonus.
4. **A stronger default AI** where the manifest pairings stall (the four stalemates), measured against the current one by `ai-survey`.
5. **Survey**: every profile and level against every faction (`ai-survey`), distinguishable by win rate, time of the first attack and army value over time;
   thresholds fixed and committed before the survey. Balance is reported, not judged.
6. **S3-M7 rest**: the eight candidate fixes of `docs/GGC-FIXES-CATALOGUE.md` (tunnel healing per player, battle plan aggregates and capture, vertical overlap
   when entering, propaganda towers in nested containers, rally points of riders and of a healed helicopter, a bike's trainability) - each adopted behind the
   rule profile with its test, or rejected with the reason; the three undecided markers decided by an audit of the disabled checks.
7. **Documents** and the next brief.

## Bottlenecks and showstoppers of this phase

- The survey takes hours (320 matches per configuration); plan it in pieces under two hours and fix its thresholds first.
- A change of the default AI moves the manifest's AI runs: one cause per commit, its own manifest commit.

## Out of scope

S3-M6 (pathfinding beyond the original, D-3), the original AI's missing C++ half, the patch layer's converted parts, a second machine, every judgement of a human.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`; every new AI option an option with a test at 30, 60 and 120 Hz and a line in the session state; nothing of the patch in the
repository.

## Acceptance

Each profile and level is distinguishable in the survey by thresholds committed beforehand; the original profile keeps the manifest; the candidate fixes are
decided; tier 4 green. What only a human could judge - that a profile "plays like a rusher" when watched - is named as such and replaced by the survey's numbers.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run; continue the "Starting point" of the next brief, the stage document, the registers and the index.
