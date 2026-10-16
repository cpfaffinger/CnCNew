# Stage 3 – Changes

As of: 10 October 2026, state `2777a90`. Frame: [PROGRAMME.md](PROGRAMME.md). This stage is the least worked out of the
four: its milestones are defined here with goal, what already exists, deliverables, acceptance and dependencies; a brief
in the format of [BRIEF-FORMAT.md](BRIEF-FORMAT.md) is written when a milestone is scheduled. **Scheduling (D-1, decided
10 October 2026):** the milestones that touch the simulation (S3-M1, S3-M6, S3-M7) come after Phase 27, in this order;
the milestones outside the simulation (S3-M2, S3-M3, S3-M4, S3-M5) may run as parallel packages from Phase 24 on, in
their own worktrees, merged before a network phase closes (PROGRAMME.md §5).

## 1. Goal, rule, yardstick

What the original did not have: kinds of AI and their tactics, a spectator mode, a replay browser, analysis of matches with
graphics, a modern user interface, movement and pathfinding beyond the original, and the community's bug-fix and balance
patches as layers. **Rule:** every change is **selectable** from its first commit – a profile, an option, a layer – and the
"original" profile keeps the reference manifest unchanged. A change that touches the simulation gets its own **profile
fingerprint**, which is part of the match description (Stage 4: only machines with the same profile play together) and of
the replay header. **Yardstick:** the original profile passes the unchanged manifest; the changed profile is measured by the
distribution comparison (`SimTool ai-survey`, `rate-distribution`, `ai-census`) and accepted by a human in the window.

Three things are built already and are booked here (PROGRAMME.md §4): the built-in `SkirmishAi`, the spectator mode and
the surveys. Nothing is rebuilt.

## 2. Milestones

### S3-M1 AI kinds, difficulty and tactics

- **Already there (do not build twice):** the built-in `SkirmishAi` (Phases 6, 16, 17, 23: roles from modules, build
  planning, supply centres at safe sources, group attacks, standing army order, composition by exchange value against what it
  has seen – `SkirmishAiOptions.Composition`/`SkirmishSlot.Composition`, selectable per player; works only with visible
  information; default AI by decision of 10 October 2026); the original AI with three levels as an option (S1-M13); the
  selection `--ai builtin|original`, `--difficulty easy|medium|hard` per slot in client, shell and spectator; the surveys
  (`ai-survey` 320 matches per rate, `ai-census`, `composition-test`, `ai-match --trace --trace-army`); the AI table of
  the manifest (16 pairings, 14 decided at 60 Hz); the decision basis in `docs/SKIRMISH-AI-ORIGINAL.md` and the
  proposals beyond the original in its §8; the observation of 8 October 2026 (`docs/AI-OBSERVED-2026-10-08.md`).
- **Deliverables:** (a) **tactics profiles** of the built-in AI – at least rush, bunker (turtle), balanced, eco – each a
  named option of the slot, with its build order, army composition rule, attack timing and defence posture, selectable in
  the shell and in `ai-match`; (b) **difficulty levels** of the built-in AI (today one) by what the AI may know and how
  fast it decides, never by a rule bonus in the simulation that a human does not get; (c) **a stronger default AI**: the
  four stalemates of the manifest pairings, the composition by combat value against the opponent's army (measured and
  withdrawn once in Phase 17 – the finding is in `docs/SIMULATION-CORE.md` §36.3), teams instead of single purchases
  (`AIPlayer::selectTeamToBuild` as the model); (d) **balance survey** of every profile against every faction (D-9); (e) the
  state of every profile in the session state (S2-M7 provides the frame).
- **Acceptance:** each profile is distinguishable in the survey (win rate, time of the first attack, army value over
  time), a human has watched one match per profile in the spectator and names it by its behaviour; the original profile
  (no tactics option set) keeps the manifest.
- **Depends on:** S2-M7 (AI state in the session; profile fingerprint). Scope note: making the AI win the challenge maps is
  the same work from the mission side (`CampaignTool drive --commands-only` as the measure).
- **State after Phase 33 (15 October 2026, by machine):** (a) the four profiles and (b) the three strengths are options of a slot in the match description and the
  session state (`docs/SKIRMISH-AI.md`); (c) the rule "no standing still" decides the manifest's stalemates (16 of 16 pairings; 307 instead of 290 of 320 survey
  matches) - composition by combat value and teams instead of single purchases were not built; (d) the survey: 12 of 13 thresholds met (rush's first attack
  missed), balance reported, not judged; (e) the state of the profiles is in the session state. "A human has watched one match per profile and names it" is not met.

### S3-M2 Spectator and observer

- **Already there:** the spectator mode (`--spectate`, `scripts/demo-spectator.ps1`, `scripts/check-spectator.ps1`,
  `docs/SPECTATOR-MODE.md`): two to eight AIs in the window, observer HUD with money, army, income and the AI's own
  "camera", speed and pause, far zoom, three difficulty levels and both AI kinds per slot, the check that the outcome is not
  predictable from the start. Built in Phase 23, **not accepted by a human**.
- **Deliverables:** (a) the acceptance of what exists (checklist `docs/SPECTATOR-MODE.md` §11); (b) free camera and
  player view switching (fog of one player, all revealed), production and research panels per player, event ticker (first
  blood, buildings lost, superweapons); (c) spectator of a human match – locally from a replay (S3-M3), on the network with
  the delayed bundle stream (S4-M3, Phase 27) and the display without an own player (S4-M5, Phase 29, `ObserverQuit.wnd`).
- **Acceptance:** a human follows an AI match and a replayed human match in the spectator and names the players' intentions
  from the display alone.
- **Depends on:** S3-M3 for replays; S4-M3 for network spectators. Presentation only – does not touch the manifest.

### S3-M3 Replay browser

- **Already there:** the replay format (version 12: header with fingerprints, rate, seed, players; tick-addressed
  commands; periodic checkpoint hashes; damaged, truncated and foreign-version files rejected), `ReplayRunner`,
  `SkirmishMatchSession.ToReplay()`, the live-against-replay test, `SimTool` playback; the main menu's load/replay submenu
  exists with the replay button **disabled** (`docs/GAME-SHELL.md`). Phase 24 adds the match description as the replay
  header and the AI commands as an audit trail; Phase 27 adds the server replay.
- **Deliverables:** (a) the client **records** every skirmish (and later every network match) as a replay in a user
  directory, with a name, date, map, players, outcome and duration in the header; (b) a **replay list** in the shell
  (the disabled button becomes the entry), sorted, with the compatibility state of every file (simulation version,
  fingerprints, rate – a replay that cannot be played says why); (c) **playback controls**: pause, speed, seek to a
  checkpoint (restore from the nearest checkpoint snapshot, then run forward), player view switching (S3-M2); (d) the
  rule for compatibility across versions (a replay is bound to its simulation version and profile fingerprint; there is no
  migration before the first release).
- **Acceptance:** a human records a match, finds it in the list, plays it back to the end with the same result, seeks,
  and is told clearly why an incompatible file does not play.
- **Depends on:** S2-M7 (match description, replay header). Presentation and game layer only.
- **Status (Phase 35, 16 October 2026): built by machine** (`docs/REPLAY-BROWSER.md`). (a) every skirmish of the built-in AI - local, of a server (both games),
  a spectator's - is recorded beside the savegames with a header the list reads; (b) `ReplayMenu.wnd` behind the main menu's Replay button with the state of every
  file and the reason, delete and rename; (c) pause, speed, seek to checkpoints the playback keeps every 30 s of game time, the view of one player or everything,
  the score screen against the recorded result; (d) a replay is bound to its build, rate, rule profile and data - every build plays only its own replays, no migration
  (D-31). No simulation code, format or reference value changed. A match of the original AI is not recorded. "A human records, finds, plays, seeks and is told" is
  replaced by machine checks (a scripted person in `Game.Tests` and in the window client, hashes at every seek, screen hashes); it is not met by a human.

### S3-M4 Match analysis with graphics

- **Already there (headless):** `ai-match --trace N` (money, army, collectors, distance over time), `--trace-army`,
  `ai-survey` (TSV per match), `rate-distribution` (Markdown report), `ai-census` (medians per player), `reference-manifest
  results` (result quantities per run, command stream), `rate-compare --fields` (first deviating frame of two runs field by
  field), `SimTool perf --json` and `perf-compare`, the `ScoreScreen` with `MatchResult` after a match.
- **Deliverables:** (a) a **statistics model** of a match computed from a replay or a running session (per player over game
  time: money, income, spent, army value, units built and lost by type, buildings, upgrades, sciences, commands per minute,
  map control, superweapon uses; events with tick and place); (b) an **export** (JSON, TSV) and a **graphical report**
  (timelines, army composition, loss exchange, heat maps of movement and fights – first as a generated HTML page or image set
  from a tool, then as a screen in the client after a match and in the replay browser); (c) the same core for the desync
  diagnosis of Stage 4 (`rate-compare --fields` and `NetTool desync-diff` share the field comparison).
- **Acceptance:** a human reads the report of a match they played and recognises its course; two runs of the tool over the
  same replay produce byte-identical exports.
- **Depends on:** S3-M3 for replays as the input. Tools and presentation only.

### S3-M5 Modern user interface

- **Already there:** the shell, HUD and texts fit the unmodified original layouts (`docs/UI-VANILLA-FIT.md`): 800×600
  scheme scaled to the window, the command bar stretched at 16:9 as in the original; `--caption`; the 2D draw-list renderer
  with the GDI font path; the 54 screen golden hashes in six resolutions; the WND layout runtime (`docs/UI-WND.md`). The
  alternative `Display.UiScale = IntegerMultiple` is named (D-21).
- **Deliverables:** (a) a design decision – native-resolution layouts versus integer scaling, widescreen command bar,
  typography – as a document before any pixel; (b) the modern profile as a **selectable skin** next to the original one
  (both from the same draw-list contract; the original stays the default until the owner decides); (c) an **options screen**
  that reaches what the original's `OptionsMenu.wnd` does not (display mode, language, aspect ratio, text scaling,
  audio devices, hotkey view); (d) the lobby and in-game indicators of Stage 4 are built on the original layouts first
  (Phase 29) and get their modern skin here; (e) the replay browser (S3-M3) and the analysis screens (S3-M4) are designed in
  the modern skin from the start, with an original-style fallback where a WND layout exists.
- **Acceptance:** golden hashes for both skins in six resolutions; a human uses the modern skin through shell → skirmish →
  result without a developer argument and judges it against the original one.
- **Depends on:** nothing of the simulation. Note F-24 (Stage 1) closes the original options first where the original has
  them.

### S3-M6 Movement and pathfinding beyond the original

- **Already there:** the deterministic A* with one state byte per cell, remembered answers, abort on proven
  unreachability (Phase 19), stationary blockers and avoidance (Phases 10, 15), formations `asTeam` (Phase 14), the
  measurements of the tick maximum (141–143 ms through many simultaneous long searches, `docs/PERFORMANCE.md` §6c) and
  `SimTool path-bench`; the fidelity finding F-04 (own destination per unit) is Stage 1.
- **Deliverables:** (a) a **shared group path** (one search per group, members follow with offsets) and a **cell budget
  per tick** with deterministic carry-over (by counted work, never by wall-clock time); (b) optionally a different search
  (hierarchical or flow field) measured against `path-bench`; (c) the behaviour as a **profile** with a fingerprint – the
  original profile keeps the manifest; (d) the tick maximum under 8 ms in the 60 Hz gate with the new profile.
- **Acceptance:** `path-bench` and the 60 Hz performance gate with the new profile; the distribution comparison shows no
  systematic shift of win rates the owner did not want; a human watches a group march in the spectator.
- **Depends on:** S2-M7 (profile fingerprint). This is the home of D-3.
- **Status (Phase 34, 16 October 2026): built by machine.** (a) and (c) as the option `movement-budget` of the rule profile (`docs/SIMULATION-CORE.md` §52); (b) not
  built; (d) **missed**: 13.7 / 13.8 ms in the 60 Hz gate after the first 300 ticks (the original 123 / 128 ms), the mean 15 % lower (`docs/PERF-MOVEMENT.md` §10). The
  distribution comparison under both profiles passes 39 of 39 checks, the GLA wins 2 instead of 9 of 80 matches (reported, not judged). The march of a group is measured
  (`SimTool group-march`: the same arrival and spread as the original, the same path lengths), not watched by a human.

### S3-M7 Bug-fix and balance layers

- **State after Phase 30 (14 October 2026):** (a) the catalogue exists (`docs/GGC-FIXES-CATALOGUE.md`, 354 markers, 96 CRC-gated); three fixes adopted behind
  the rule profile with tests, four rejected with reasons, 83 CRC-gated not reviewed; (b) the patch layer imports (`AssetTool import-layer`, 366 files, its own
  manifest options) but its definitions do not compile (`docs/PATCH-LAYER.md` §2); (c) the profile is part of the definition fingerprint and so of every match
  description and room. Balance changes are not separately switchable - the patch does not separate them. Next: `HANDOFF-PHASE32.md`.
- **State after Phase 32 (15 October 2026):** (a) every CRC-gated marker has a review entry: 11 fixes adopted, 8 candidates deferred with reasons, 1 rejected,
  3 undecided; (b) the layer compiles (`baselines/compatibility-patch.json`) and plays deterministically - against the original all 27 missions end the same way,
  11 of 16 pairings, the reference match finds no winner; (c) on a server a client without the layer is refused (`Definitions`). The acceptance's "a human plays
  one skirmish per layer" is not met. Rest: `HANDOFF-PHASE33.md`, side item.
- **State after Phase 33 (15 October 2026):** every CRC-gated marker decided - 23 adopted (16 fixes), 17 equivalent, 51 not applicable, 5 rejected; three of the
  rejected ones wait for fidelity fixes of the original first (F-34, F-38, F-39).

- **Already there:** `references/` contains GeneralsGameCode and GeneralsGamePatch for reading
  (`scripts/get-references.ps1`); `CnCNew.Import` builds overlays and fingerprints; `baselines/compatibility.json`
  counts every compatibility case. Nothing of the patches is integrated (the former Phase 30, items 3 and 4).
- **Deliverables:** (a) a **catalogue of the GeneralsGameCode fixes** by subsystem (simulation, AI, pathfinding, scripts,
  rendering, UI), each understood, rewritten with a source reference and a regression test; fixes that change the
  simulation are a measured hash change and a new simulation version; (b) **GeneralsGamePatch as an overlay layer** with its
  own definition fingerprint through `CnCNew.Import`, balance changes separately switchable; (c) the layer as a **profile**
  in the match description (a room names original or patch; only clients with the same fingerprint join).
- **Acceptance:** the original profile passes the unchanged manifest; every adopted fix has its test; a human plays one
  skirmish per layer.
- **Depends on:** S2-M7 (match description); S4-M1 for the room (or local only before that).

### R3 Changes release — open

Passed when (PROGRAMME.md §6): every change is selectable and off in the original profile; the original profile passes the
unchanged manifest; every profile that touches the simulation has a fingerprint; balance effects are measured by the
distribution comparison; a human has accepted each feature in the window.

## 3. Candidate backlog (not milestones yet)

Collected from the documents; each becomes a milestone only by a decision of the project owner: the proposals beyond the
original in `docs/SKIRMISH-AI-ORIGINAL.md` §8 (each only as an option or a kind of AI of its own); a co-simulating server
that can deliver state (`docs/MULTIPLAYER-PLAN.md` §2.1, later option); 120 Hz as a player option (D-22); custom maps
through an import path (`RadioButtonUserMaps` is empty in Stage 1, F-24); a substitute display for the Bink movies
(F-02; R1 may do a still image with text); accounts, ladder, matchmaking, NAT traversal and GameSpy compatibility stay
**out of scope** (`docs/MULTIPLAYER-PLAN.md` §1).

## 4. Rules of the stage

1. Option first: a change beyond the original is never in the default profile without the owner's decision.
2. Fingerprint: every profile that touches the simulation changes the match description's fingerprint and the replay
   header; the manifest of the original profile does not move.
3. Measured, not asserted: balance by the distribution comparison with the sampling variance calculated beforehand
   (Phase 23's method, `docs/SIMULATION-CORE.md` §40); performance by the 60 Hz gate.
4. Each milestone gets a brief in the format of `BRIEF-FORMAT.md` when scheduled; the brief names what is already there.
