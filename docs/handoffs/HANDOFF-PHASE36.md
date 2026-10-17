> **Stage:** 3 – Changes · **Milestone:** S3-M4 Match analysis with graphics · **Phase:** 36
> **Status:** done by machine (17 October 2026, at `c2dcc4f`); the placement side item measured and replaced by a faster footprint check, the angry-mob case narrowed (`docs/PERF-MOVEMENT.md` §11) · **Successor:** HANDOFF-PHASE37.md
> **Predecessor:** HANDOFF-PHASE35.md · **Results in:** docs/MATCH-ANALYSIS.md (new), docs/REPLAY-BROWSER.md, docs/GAME-SHELL.md, docs/plan/STAGE-3-CHANGES.md
> **Side items:** the residue of S3-M6 that Phase 35 did not take (the rate-equivalence case of the angry mob under `movement-budget`; the computer players' placement checks under a budget)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 36): a statistics model of a match computed from a replay or a running session, its export, and a graphical report - first from a tool, then as a screen
after a match and in the replay browser

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md` (paragraphs "Phase 24", "Phase 34" and "Phase 35"),
`README.md`, `docs/plan/PROGRAMME.md` (§5 order, §7 decisions), `docs/plan/STAGE-3-CHANGES.md` (S3-M4, S3-M3, S3-M5), `docs/REPLAY-BROWSER.md` (the file, the playback,
the checkpoints), `docs/SPECTATOR-MODE.md` §4 (where every value of the observer panels comes from), `docs/GAME-SHELL.md` §7 and §17 (the score screen, the replay list)
and `docs/NETWORK-DIAGNOSTICS.md` §5 (the field comparison of `desync-diff`). Prerequisite: Phase 35. This milestone touches no simulation and no manifest.

## Goal in one sentence

A player sees the course of a match they played - money, income, army value, losses, buildings, sciences, commands per minute, where the fights were - as timelines and
pictures after the match and in the replay browser, computed from the replay, and the same numbers come out byte-identical every time.

## Starting point (measured, after Phase 35 – re-measure when this phase starts)

- **State:** `master` at `55274ba`; Tier 4 with 55 of 55 steps ok at `8f6dc49` (6,513 s; 0 warnings; all test programs; simulation, game, presentation and network tests
  at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; every network step; the new `replay-browser`; `patch-layer`;
  the staged scenes). Versions unchanged by Phase 35: `CommandTypes.Version` 17, replay 15, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description
  encoding 3, skirmish session state 3, protocol 6, transport 2; new: the replay file of the game layer (`MatchRecordingFormat`) version 1.
- **The reference manifest** (`3b6fa80`, unchanged): retail reference match FactionGLA at tick 32,520, `6052051714309B17` / `84077720F511C1ED`; stress test
  `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 AI pairings 16 decided.
- **The replay browser** (S3-M3, Phase 35, `docs/REPLAY-BROWSER.md`): every skirmish of the built-in AI is recorded beside the savegames (`Replays`, `.cncreplay`: a header
  with date, map, players, ending, the session hash every five seconds, the score counters; the simulation's replay inside); the list on `ReplayMenu.wnd` with the state of
  every file; a playback (`ReplayPlayback`) that checks every recorded hash, keeps a checkpoint every 30 s of game time and seeks to them. Measured: an eight-player hour is
  a file of 1.67 MB and 38 MB of checkpoints in memory; a seek 0.24 to 0.32 s plus the ticks from the checkpoint. **This is the input of S3-M4:** a replay plays headless
  to any tick with the same session hash as the recorded match.
- **The attached effects** (Phase 35, the owner's finding, `docs/PRESENTATION-FX.md` "Attached effects", F-41): model-state particle systems, the body's fire and smoke,
  `BoneFXUpdate`, `TransitionDamageFX` particles, missile exhaust, projectile streams, vehicle dust; not seen by a human.
- **Campaign at 60 Hz (at `8f6dc49`):** reachability clean 24/24 and 24/24; audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and
  15/24 (exit code 1 as before: `MD_CAMPEA_DEMO`); objective driver victory 24/24 and defeat 24/24.
- **Performance** (`scripts/perf-profile.ps1` with the binaries of `1ea771c` and of `8f6dc49` one after the other): every one of the ten runs with the same final hash
  on both sides (Phase 35 changes no simulation); means within -5.0 % and +5.0 % (8 players: 847.9 against 864.5 µs per tick, 99 % 2.12 against 2.20 ms, maximum 134 against
  123 ms, 10,297 against 10,296 bytes per tick).
- **Clean room:** the commit `55274ba` cloned into a fresh directory with empty caches (1,114 files, nothing of `game/`, `.local/`, `references/`), built in
  140 s (259 packages, 0 warnings), the content exported afresh in 117 s (25,608 files, the known export), input manifest identical, the quick reference set 9 of 9,
  `Simulation.Tests` and `Data.Tests` with that content green, the client package (933 files, 317 MB) and both server packages (204 files, 81 and 83 MB) free of
  original data - every step ok.
- **Side items carried on:** the rate-equivalence case of the angry mob under `movement-budget` (narrowed in `docs/PERF-MOVEMENT.md` §10: only 30 Hz differs, already on the
  straight run; the nexus's mob members and the profile's detour questions are the likely place, not traced); the computer players' placement checks under a budget (not
  built).
- **Not verified, carried on:** no human has used the replay list or a playback, or seen the new effects; the presentation's cost of the attached effects is not measured (`PresentationTool sync-perf`); a second physical machine and a real network (E1, D-10); the
  owner's open decisions D-2 to D-8, D-10, D-17 to D-20, D-23 to D-33.

## What is already done – do not build twice

The replay file with its header, its recorded hashes and counters (`MatchRecordingFormat`, `MatchRecorder`), the playback that rebuilds a match from it with checkpoints and
seeking (`ReplayPlayback`), the replay list and its details window (`ReplayMenuScreen`), the observer statistics (`SpectatorStats`: money, income per minute, army value by type,
production, losses and kills, power - from the events of every tick), the score counters (`MatchStatistics`), the headless surveys (`ai-match --trace`, `ai-survey`, `ai-census`,
`reference-manifest results`, `rate-compare --fields`) and `NetTool desync-diff`.

## Decisions that are fixed

- Presentation, game layer and tools only: no simulation change; the reference and input manifests unchanged.
- The statistics are computed from what the simulation already tells (events, the world, the recording); no new state in the simulation for them.
- A report is a function of the replay: the same file gives the same bytes on every run and at every machine of the same build (canonical order, no wall-clock time
  in it, no culture).

## Brief (in this order)

1. **Reproduce the starting state** (tier 4 unless Phase 35 ended with it on the same commit).
2. **The statistics model** (S3-M4 (a)): per player over game time - money, income, spent, army value, units built and lost by type, buildings, upgrades, sciences,
   commands per minute, map control, superweapon uses; events with tick and place. Computed by playing a replay headless (`ReplayPlayback` without a presentation, or
   `SkirmishReplay.Play`) or by observing a running session; the same core for both.
3. **The export** (S3-M4 (b)): JSON and TSV with a fixed schema version; a tool command (`SimTool` or a new one) that writes them from a replay file; two runs byte-identical.
4. **The graphical report** (S3-M4 (b)): a generated HTML page or image set from the tool (timelines, army composition, loss exchange, heat maps of movement and fights);
   then a screen in the client after a match (beside the score screen) and from the replay list (a button or the details), on the original's layouts where they reach and
   with own drawing where they do not (S3-M5 designs the modern skin later).
5. **The shared field comparison** (S3-M4 (c)): `rate-compare --fields` and `NetTool desync-diff` use one comparison core.
6. **Frameworks instead of a human:** the export byte-identical over two runs and at 30, 60 and 120 Hz where the rate is involved; the report's draw lists pinned by hashes
   in six resolutions; a `ShellScript` person opens the analysis after a match and from the list.
7. **Side items as time allows** (named in the header).
8. **Documents** and the next brief.

## Bottlenecks and showstoppers of this phase

A statistic that cannot be had from the events and the world without a simulation change is named and left out, not computed by a change of the simulation. Map control
and heat maps need positions over time: sample them on the original-frame grid so that they are the same at every rate.

## Out of scope

The modern skin (S3-M5), balance judgements, every judgement of a human.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`; nothing of the game in the repository (a replay of a real match is a file of the user, never a fixture); the manifests unchanged.

## Acceptance

By machine: a replay gives a byte-identical export on two runs; the report's pictures are pinned; a scripted person opens the analysis after a match and from the replay
list; tier 4 green. What only a human could judge - that the report is readable and shows the course of the match a person remembers - is named as such.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run; continue the "Starting point" of the next brief, the stage document, the registers and the index.
