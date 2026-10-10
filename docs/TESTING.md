# Test and acceptance strategy

Binding since 10 October 2026 (decision of the project owner). It replaces the habit of running the whole acceptance
run for every change. The executable form is `scripts/test.ps1`; where this document and the script disagree, that is a
defect of one of them and is fixed, not worked around.

## 1. Principle

A check is chosen by **what the change can break**, not by habit and not by the size of the diff:

1. **Fast and on the point first.** The test that covers the changed code runs first and alone; a failure is known in
   seconds, not after forty minutes.
2. **Widen in steps.** Four tiers, each a superset of the one before. A tier is run when its trigger applies (§2, §4),
   and the run stops at the first failure.
3. **The whole run is owed at fixed points**, not at every commit: completion of a phase, a written reference manifest,
   a cross-cutting change (§4).
4. **A report names the tier that ran.** "Green" without a tier is not a statement. What was not run is named (§7).

Selective testing accepts a known risk: a defect outside the selected steps is found later, at the next gate or full
run, and then costs a search over the commits since. The triggers in §4 are chosen so that this window stays small
where a late finding is expensive (everything that changes results of the simulation).

## 2. The four tiers

| Tier | When | What | Budget |
|---|---|---|---|
| **1 focus** | while working, after every edit that should change a test result | the one test or suite of the changed code, called directly (§6) | seconds |
| **2 affected** | before **every commit** | build with 0 warnings; the test programs of the touched areas and of what reads them directly | 1–5 min |
| **3 gate** | before a **push** that contains a change of an area other than `docs` and `scripts`; before a merge of a parallel worktree | tier 2, plus the consumers further up, the other tick rates and – for everything that feeds the simulation – the determinism yardstick (input manifest, quick reference set) | 10–25 min |
| **4 full** | the triggers of §4 | every step: all test programs at 30, 60 and 120 Hz, real data, both manifests in full, the client checks, the scene audits | around TOTAL |

```powershell
pwsh scripts/test.ps1                    # tier 2 for the changes against origin/master (committed or not, plus untracked files)
pwsh scripts/test.ps1 -Tier gate         # tier 3 for the same changes
pwsh scripts/test.ps1 -Tier full         # tier 4
pwsh scripts/test.ps1 -Plan -Tier gate   # print the steps and their commands, run nothing
pwsh scripts/test.ps1 -Tier gate -Area simulation,ui     # the steps of named areas, whatever the changes (-Area ? lists them)
pwsh scripts/test.ps1 -Since HEAD~3  |  -Paths src/CnCNew.Ui/Layout.cs  |  -KeepGoing
```

Independent of the tiers and unchanged: **`pwsh scripts/audit-publish.ps1` before every push**, also for a
documentation-only push (it guards the repository, not the code).

## 3. What a change asks for

The script maps every changed path to an area (first matching prefix; a path nothing matches – `Directory.Build.*`,
`global.json`, `CnCNew.slnx`, `build/`, `tests/Shared/` – is the area `build` and asks for everything). The step names are
those of `scripts/test.ps1`; the commands are printed by `-Plan`.

| Area | Paths | Tier 2 affected | Tier 3 gate adds |
|---|---|---|---|
| `archives` | `CnCNew.Archives`, its tests | `archives`, `io`, `import` | – |
| `io` | `CnCNew.IO`, its tests | `io`, `import` | – |
| `content` | `CnCNew.Content`, its tests | `content`, `import` | `verify` |
| `import` | `CnCNew.Import`, its tests, `AssetTool`, `baselines/steam-master.json` | `import` | `verify` |
| `images` | `CnCNew.Images`, its tests, `ImageTool` | `images`, `import`, `presentation` | `presentation-content`, `game` |
| `formats` | `CnCNew.Formats`, its tests, `MapTool` | `formats`, `import` | `inputs`, `quick`, `presentation-content` |
| `w3d` | `CnCNew.W3D`, its tests, `W3dTool` | `w3d` | `presentation-content`, `game` |
| `data` | `CnCNew.Data`, its tests, `IniTool`, `baselines/compatibility.json` | `data`, `data-content`, `compat` | `ui`, `audio`, `sim`, `inputs`, `quick` |
| `simulation` | `CnCNew.Simulation`, its tests | `sim` (60 Hz) | `sim-30`, `sim-120`, `presentation`, `inputs`, `quick`, `rate-suite`, `game-sim` |
| `simtool` | `SimTool`, `baselines/input-manifest.json` | – (build) | `inputs`, `quick` |
| `manifest` | `baselines/reference-manifest.json` | `inputs`, `reference` (the full set) | – |
| `presentation` | `CnCNew.Presentation`, its tests, `PresentationTool` | `presentation` | `presentation-content`, `game` |
| `ui` | `CnCNew.Ui`, its tests, `UiRenderTool` | `ui` | `game` |
| `audio` | `CnCNew.Audio`, its tests | `audio` | `audio-content`, `game` |
| `game` | `CnCNew.Game`, its tests, `CampaignTool` | `game-focus`: the suites of the touched folder (below) | `game` (all suites) |
| `client` | `CnCNew.Client.Stride` | – (build) | `check-spectator`, `check-dozer`, `check-hotkeys` |
| `scripts` | `scripts/` | – : run the script you changed | – |
| `docs` | `docs/`, `*.md` in the root | – | – |
| `build` | everything else | every fast test program and `sim` | every gate step above |

`game-focus` picks suites of `Game.Tests` by the folder of `CnCNew.Game` (always with `SourceRuleTests`; a file outside
these folders asks for all suites): `Audio` → `AudioDirectorTests`; `Campaign`, `Save` → `CampaignTests`,
`CampaignShellTests`; `Hud` → `HudTests`, `MissionHudTests`, `HotkeyTests`; `Live` → `LiveMatchTests`, `DeterminismTests`,
`TimeScaleTests`, `ScenarioTests`, `MissionPresentationTests`, `MissionEffectsTests`, `MissionTextTests`; `Shell` →
`ShellTests`, `ShellTransitionTests`, `CampaignShellTests`; `Spectator` → `SpectatorTests`. A changed suite file runs
itself. `game-sim` is `DeterminismTests`, `LiveMatchTests`, `SourceRuleTests`.

The mapping is a judgment about where a defect of an area shows first, derived from the project references; it is
corrected when a gate or full run finds a defect that tier 2 of the causing area should have found (§5, rule 4).

**Not selected by the script, chosen by hand** when the change touches the subject:

| Subject | Check |
|---|---|
| campaign, script VM, mission session | `CampaignTool audit` / `drive` / `reachability` for the touched maps (`--map`, `--campaign`), all maps at the completion of the phase |
| skirmish AI | `SimTool ai-match` on the pairings concerned; `ai-survey` + `rate-distribution` only when balance or rate equality is the claim |
| performance-relevant code (movement, pathfinding, vision, hash) | `SimTool perf` as a direct switch between two binary directories (`scripts/perf-compare.ps1`), quiet machine |
| world rendering, HUD, shell pictures | the capture script of the subject (`capture-world`, `capture-scenarios`, `capture-text`, `UiRenderTool`) and a look at the pictures – a hash proves reproducibility, not correctness |
| a script under `scripts/` | that script |
| NuGet packages, SDK | `scripts/audit-vulnerable.ps1`, clean build (deleted `bin`/`obj` of the client) |

## 4. When the full run is owed

Tier 4 runs, and its result is part of the report, when one of these applies:

1. **Completion of a phase** (together with the checklist in §8).
2. **A commit writes `baselines/reference-manifest.json` or `baselines/input-manifest.json`.** Before the write: the full
   check on the state without the cause, so that the write has exactly one cause. After it: the full check twice identical.
3. **A cross-cutting change of the simulation**, whatever its size: `Numerics` (`Fixed`, `FixedMath`), `Random`, `Time`
   (rate, frame grid), the state hash, `Snapshots`, `Replay`, command validation and command versions, the order in which
   the world enumerates objects or modules, and every version jump of a format.
4. **A change of the build itself**: SDK, NuGet packages, `Directory.Build.*`, `build/`, compiler or analyser settings.
5. **The quick reference set deviates and the change was meant to be neutral** – the full set shows the extent before
   anything is "fixed" (§5, rule 2).
6. **Before a tag or a package** handed to someone else.
7. **At the latest after ten pushed commits that touched `simulation`, `data` or `formats`** without a full run in
   between – so that a late finding has at most ten suspects.

Everything else – a module, a script action, a HUD element, a tool option, a test – ends at tier 3.

## 5. Rules

1. **A failure is fixed at the tier that found it**, and that tier is run again from the start. A higher tier is not
   started on a red lower one.
2. **A manifest deviation is a finding, never a number to adopt.** A change that was meant to be neutral and changes a
   hash is wrong until its cause is named; a change that is meant to change results gets its own manifest commit with one
   cause (`CLAUDE.md`, phase paragraphs). The yardstick stays `baselines/reference-manifest.json`.
3. **Unknown ground counts as wide.** A path the script does not know is the area `build`; when in doubt between two
   tiers, take the higher one. The strategy saves time on changes whose reach is known, not on changes nobody has
   looked at.
4. **An escaped defect moves the selection.** When tier 3 or 4 finds a defect that the change's own tier 2 could have
   found, the commit that fixes it also gets the test that would have caught it into tier 1/2 (a new focused test, or a
   step added to the area in `scripts/test.ps1` and §3).
5. **Every fix and every new behaviour comes with its focused test** – the selection can only be as sharp as the tests
   are addressable. New tests get a name prefix that `--only` can address (`Simulation.Tests`) or belong to a suite
   (`Game.Tests`).
6. **Missing content is not a pass.** A step skipped for lack of `game/content` is reported as skipped; such a run does
   not count as its tier.
7. **Long runs use the Release build**, built on its own before `--no-build` (the script does this); a debug run of
   `Game.Tests` is not waited for.

## 6. Focused runs (tier 1)

```powershell
# One group of simulation tests: --only takes a part of the test name (the prefix before the colon, e.g. "Scripts", "Navigation",
# "Weapon slots", "Rate invariance", "Fixed point", "Determinism rules").
dotnet build -c Release tests/CnCNew.Simulation.Tests -v q
dotnet run -c Release --no-build --project tests/CnCNew.Simulation.Tests -- --only "Navigation"
dotnet run -c Release --no-build --project tests/CnCNew.Simulation.Tests -- --content-dir game/content --only "Real data"
dotnet run -c Release --no-build --project tests/CnCNew.Simulation.Tests -- --content-dir game/content --only RateEquivalence

# One or more suites of the game tests.
dotnet build -c Release tests/CnCNew.Game.Tests -v q
dotnet run -c Release --no-build --project tests/CnCNew.Game.Tests -- --content-dir game/content --only HudTests,HotkeyTests

# Single runs of the reference manifest (ids: reference-manifest list).
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest check game/content baselines/reference-manifest.json --only reference-match,stress

# One campaign map, one staged scene.
dotnet run -c Release --project tools/CnCNew.CampaignTool -- audit game/content --map "/MD_USA01.map" --seconds 30
pwsh scripts/audit-scenarios.ps1 -Scenario exits -Faction China
```

`Simulation.Tests --only` matches a part of the name, `Game.Tests --only` whole suite names. The other test programs
run in seconds and have no selection.

## 7. Reporting

- A commit message or report that claims a check names it: "tier 2 (simulation)", "gate", "full", or the single
  commands. "All tests green" is only said after tier 4.
- A report lists what was **not** run and why, when the change could have asked for it.
- Numbers quoted from a run carry the commit they were measured at; numbers from documents are not adopted
  (`CLAUDE.md`, quality rules).

## 8. Completion of a phase

Tier 4 (`pwsh scripts/test.ps1 -Tier full`) and, in addition, what the script does not run:

- the rate-equivalence suite with 0 / 0 in all ten groups (contained in `sim-content`; the report quotes the counts);
- `reference-manifest check --set full` a second time when a manifest was written in the phase;
- the checks chosen by hand in §3 for every subject the phase touched (campaign driver/audit/reachability on all maps
  when the simulation changed);
- `scripts/perf-profile.ps1` on a quiet machine with a comparison against the previous profile;
- `scripts/audit-publish.ps1`, a clean working tree, the documents and the next handoff (`CLAUDE.md`, "Procedure of a phase").

What needs a human (pictures, sound, playability) is driven by `scripts/acceptance.ps1` (`docs/ACCEPTANCE.md`) and is
never claimed by a test run.

## 9. Measured durations

DURATIONS

## 10. What this strategy does not cover

- It selects by project and folder, not by call graph: a change in `CnCNew.Simulation/World` runs all simulation
  tests, and a defect it causes in a campaign map is found only by the hand-chosen checks or the full manifest.
- The quick reference set is 9 of 50 runs. A neutral-meant change can pass it and still move one of the other 41; the
  ten-commit rule (§4, no. 7) bounds how long that stays unnoticed.
- The suite mapping of `Game.Tests` (§3) is a judgment and not derived from coverage data.
- Nothing here replaces a human: no tier sees a picture or hears a sound.
