# CnCNew

## What this is – and what it is not

CnCNew is an independent reimplementation, in C#/.NET 10, of the SAGE engine runtime needed for
*Command & Conquer™ Generals – Zero Hour*: data model, deterministic simulation, presentation and a Windows client on
Stride 4.3 / Direct3D 11. It includes a patcher that builds a new local game directory from an existing installation
of the game – a copy of the game **you already own** – in which every file lies open (plain files, no `.big` archives)
and which runs with this engine. The user's installation is only read in the process, never modified. The repository
contains **no game data**.

It is **not a finished game**, not a mod of the original program, not a substitute for buying the game and not a
download of game data. Scope of the first product: single player (skirmish and campaign) and – planned, not built –
multiplayer skirmishes with up to eight players through a dedicated server (`docs/MULTIPLAYER-PLAN.md`). Not in scope:
networked campaign, matchmaking/ladder, NAT traversal, GameSpy compatibility, anti-cheat, binary
compatibility with old mods and savegames.

In short: the simulation is deterministic at 60 Hz and checked against a reference manifest; skirmish and campaign run
headless; a Windows client renders and plays sound – but **no human has accepted its look, sound or playability**.
Multiplayer is planned, not built. Licence: GPL-3.0-or-later; not affiliated with or endorsed by Electronic Arts (see
`NOTICE.md`).

## Status (9 October 2026)

Measured is what tools and tests can verify without a human; details and numbers are in the linked
documents, the yardstick for results is `baselines/reference-manifest.json`.

- **Data:** the game data is loaded strictly typed; unknown keys, modules or references never fall back
  silently to default values, every compatibility case is counted and secured against `baselines/compatibility.json`
  (`docs/TYPED-DEFINITIONS.md`, `docs/PHASE3-COMPATIBILITY.md`).
- **Simulation:** fixed point (Q39.24), deterministic, 60 Hz by default (30 and 120 Hz are checked by a
  rate-equivalence suite against 30 Hz as the definition). All 183 simulation modules of the catalogue are
  implemented (open: the 22 presentation modules), all 2,115 objects of the retail data can be instantiated. A
  reference manifest with 50 runs (reference match, stress test, AI matches, missions) is checked at the end of every
  phase under Windows (`docs/SIMULATION-CORE.md`, `docs/DETERMINISM.md`, `docs/TICKRATE-MIGRATION.md` §15).
- **Skirmish and campaign, headless:** AI-versus-AI matches run deterministically (14 of 16 reference runs end with
  a winner). The script VM executes the map scripts; a checking tool reaches victory and defeat on all 24 unique
  campaign maps through the map's own scripts – in doing so it replaces player actions by
  interventions in the world and verifies the script chain, **not the playability**. An agent that only issues commands wins three
  of these maps (`docs/SCRIPT-ENGINE.md`, `docs/CAMPAIGN-AND-SAVEGAMES.md`).
- **Client:** draws terrain, objects, effects, HUD and the menus from the original layouts, outputs sound and accepts
  input; skirmish, mission, shell and a spectator mode start. **No human has accepted look, sound or
  playability**, and at 60 Hz nobody has yet seen or heard the game; the viewings so far
  found defects (`docs/DEMO-FINDINGS.md`). Smoke tests and checksums verify reproducibility, not correctness.
- **Multiplayer:** planned only. Built is the preparatory work (measurement basis, performance, tick rate as a value of the world, fixed point,
  60 Hz) and, since Phase 24, the session core: the computer players are part of the hashed and saved session state, a running skirmish can be saved and
  continued, a match is started from one description, orders pass a two-stage command path and a tick driver releases the ticks
  (`docs/SIMULATION-CORE.md` §45, `docs/PRESENTATION-INPUT.md` §13). Protocol and server follow from Phase 25 on.
- **Open and named:** determinism is verified on one machine, a second physical machine is outstanding; the
  tick maximum misses its target; no video decoder; the balance of the AI is not assessed; eleven decisions lie
  with the project owner (`docs/plan/PROGRAMME.md` §7). The plan: `docs/plan/PROGRAMME.md` – four stages (port,
  engine, changes, multiplayer) with milestones, what is done, and what only a human can still accept.

## Legal matters in brief

- **Your own copy is required.** The repository contains no game data and nothing exported from it – only checksums,
  sizes, names and counts for checking (`baselines/`). The rights to the game data lie with Electronic Arts Inc.
- **Licence:** `GPL-3.0-or-later` ([LICENSE.md](LICENSE.md)). The code was written with reference to the source code
  published by Electronic Arts under GPL v3 (with additional terms); it is a newly written implementation, marked as
  such, and not the original program. Details, the additional terms and the
  licences of the third-party components are in [NOTICE.md](NOTICE.md).
- **Trademarks:** "Command & Conquer", "Generals" and "Zero Hour" are trademarks of Electronic Arts Inc. This project is
  not affiliated with Electronic Arts and is neither supported nor endorsed by Electronic Arts.

## Prerequisites

- Windows (the only target platform; Linux comes into consideration later only for the dedicated server)
- .NET SDK 10.0.401 or a newer feature band of the same major version (`global.json`), PowerShell 7 (`pwsh`);
  for development also Git and Python 3 – the full list with install commands is in [BUILDING.md](BUILDING.md)
- a fresh **English Steam installation** of *Command & Conquer Generals – Zero Hour* – the one supported
  source version; it is described in `baselines/steam-master.json` by paths, sizes and SHA-256

## Quick start for users

```powershell
pwsh scripts/install.ps1                 # optional: -GameDir <Steam installation> -Target <target directory>
pwsh scripts/uninstall.ps1               # removes the created game directory again
```

`install.ps1` reads the installation without modifying it and creates the new game directory (default
`%LOCALAPPDATA%\CnCNew\Game`). What is checked in the process, how the game is started, options and failure patterns
are described in [docs/INSTALLATION.md](docs/INSTALLATION.md).

Acceptance by a human is driven by `pwsh scripts/acceptance.ps1` (documented in [docs/ACCEPTANCE.md](docs/ACCEPTANCE.md)).

## For developers

Restoring the whole developer state on another machine takes a clone and one script. It checks the prerequisites, clones the
reference sources into `references/` at the recorded commits, exports `game/content` from your Steam installation, builds and
checks against the baselines (each step can be skipped; running it again repairs):

```powershell
git clone https://github.com/cpfaffinger/CnCNew.git
pwsh CnCNew/scripts/setup-dev.ps1        # options: -ShallowReferences -GameDir <path> -SkipReferences -SkipContent -SkipBuild -SkipCheck
```

The single steps:

```powershell
pwsh scripts/get-references.ps1          # the four upstream repositories to <repo>\references (git-ignored); -Shallow, -Only <name>
pwsh scripts/dev-content.ps1             # developer installation to <repo>\game (git-ignored): game\content
dotnet build CnCNew.slnx                 # clean build: 0 warnings (TreatWarningsAsErrors)
dotnet run --project tests/CnCNew.Simulation.Tests
dotnet run --project tests/CnCNew.Simulation.Tests -- --content-dir game/content
dotnet run --project tests/CnCNew.Data.Tests -- --content-dir game/content
dotnet run --project tools/CnCNew.IniTool -- compat-check game/content baselines/compatibility.json
```

Which checks a change needs is fixed in [docs/TESTING.md](docs/TESTING.md): `pwsh scripts/test.ps1` runs the test programs
of the touched areas before a commit, `-Tier gate` before a push of code, `-Tier full` at the completion of a phase.

Tests are standalone console programs (non-zero exit code on failure), no xUnit/NUnit. The complete list
of test programs, audits, manifest checks and tool commands is in [CLAUDE.md](CLAUDE.md), section "Build,
tests, audits". Tech demos in a window (they build the client and need `game/content`):

```powershell
.\scripts\demo-skirmish.ps1              # skirmish against the AI
.\scripts\demo-mission.ps1               # campaign mission
.\scripts\demo-shell.ps1                 # main menu
.\scripts\demo-spectator.ps1             # AI against AI to watch
```

## Layout of the repository

| Directory | Content |
|---|---|
| `src/` | the libraries and the client: `CnCNew.Archives`, `.IO`, `.Import` (import side), `.Content` (runtime file system), `.Formats`, `.Images`, `.W3D`, `.Audio` (formats), `.Data` (typed definitions), `.Simulation` (deterministic core), `.Presentation`, `.Ui`, `.Game` (game layer, shell, HUD), `.Client.Stride` (Windows client) |
| `tools/` | console tools: `AssetTool` (import, installation), `IniTool`, `MapTool`, `ImageTool`, `W3dTool`, `SimTool` (reference runs, manifests, performance), `CampaignTool`, `PresentationTool`, `UiRenderTool` |
| `tests/` | one test program per project, synthetic fixtures |
| `scripts/` | PowerShell scripts: installation, developer content, demos, captures, audits, performance profile |
| `baselines/` | checked-in yardsticks: compatibility baseline, input and reference manifest, description of the source version (only paths, sizes, checksums) |
| `build/` | MSBuild building blocks (Stride tooling packages, asset compiler target) |
| `docs/` | documentation; entry point is [docs/README.md](docs/README.md) |

Layers and dependency rules (import → runtime content → data models → simulation → presentation) are in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Not in the repository and ignored by Git: `game/` (developer installation),
`.local/` (local working data), `references/` (foreign source code for reading), `bin/`, `obj/`.

## Documentation

The overview of all documents, ordered by topic, is in [docs/README.md](docs/README.md). First points of call:
[docs/plan/PROGRAMME.md](docs/plan/PROGRAMME.md) (the plan in four stages), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md),
[docs/SIMULATION-CORE.md](docs/SIMULATION-CORE.md),
[docs/DEMO-FINDINGS.md](docs/DEMO-FINDINGS.md).

## How the project is made

The work proceeds in four stages (port, engine, changes, multiplayer; `docs/plan/PROGRAMME.md`) and in phases: every
phase has a self-contained brief (`docs/handoffs/`, indexed by stage in `docs/handoffs/README.md`), begins with the
reproduced starting state and ends with measurements, updated documentation and the brief of the next
phase. The code is written predominantly by coding agents under the guidance of the project owner;
[CLAUDE.md](CLAUDE.md) (with the pointer [AGENTS.md](AGENTS.md)) is their binding working instruction and at the same time
the most precise description of the rules and of the measured state. What only a human can judge counts as open
until it has taken place.

## Contributing

There is no CI yet: whoever contributes something runs the test programs of the touched projects themselves
([CONTRIBUTING.md](CONTRIBUTING.md)). Game data or anything exported from it is never committed.
