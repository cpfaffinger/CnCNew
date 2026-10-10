# Contributing

Short version for humans. The complete, binding rules are in [CLAUDE.md](CLAUDE.md) (they apply to
coding agents and humans alike); the licence situation is described in [NOTICE.md](NOTICE.md).

## Prerequisites and data

- Windows, Git, .NET SDK 10.0.401 or a newer feature band (`global.json`), PowerShell 7 (`pwsh`), Python 3 (only for the
  check before a push). The full list with install commands and pitfalls is in [BUILDING.md](BUILDING.md).
- Your own fresh English Steam installation of *Command & Conquer Generals – Zero Hour*. The repository
  contains no game data.
- A new machine: `pwsh scripts/setup-dev.ps1` after the clone restores everything that is not in the repository (reference
  sources at the recorded commits via `scripts/get-references.ps1`, developer content, build, check against the baselines).
- Creating the developer data: `pwsh scripts/dev-content.ps1` reads the installation (without modifying it) and writes
  the developer installation to `game/` (`game/content`); details in `docs/INSTALLATION.md`.

## Building and testing

```powershell
dotnet build CnCNew.slnx
dotnet run --project tests/CnCNew.Simulation.Tests
dotnet run --project tests/CnCNew.Simulation.Tests -- --content-dir game/content
dotnet run --project tests/CnCNew.Data.Tests -- --content-dir game/content
dotnet run --project tools/CnCNew.IniTool -- compat-check game/content baselines/compatibility.json
```

There is no CI yet: whoever submits something runs the test programs of the touched projects themselves and states in
the pull request what was run. The complete list of test programs, audits and manifest checks is in
`CLAUDE.md`, section "Build, tests, audits". Tests are standalone console programs (no xUnit/NUnit).

## The non-negotiable rules in ten lines

1. **Never commit game data** – nothing from `game/` or `.local/`, no captures, snapshots, manifests of the content,
   no extracted texts, graphics or sounds. Checksums, sizes, names and counts are allowed, contents are not.
2. Test fixtures are small and synthetic, no excerpts from original files.
3. Nothing falls back silently to default values: the unknown is counted, reported or rejected
   (`baselines/compatibility.json`).
4. Layers stay separate: import → runtime content → data models → simulation → presentation.
5. The simulation is deterministic: no `float`/`double`, no wall-clock time, no threads, no ambient randomness, no
   reflection (the test `SourceRules` enforces this).
6. `baselines/reference-manifest.json` is the yardstick; whoever changes it does so in a commit of its own with a
   single cause.
7. `TreatWarningsAsErrors`: the clean build has 0 warnings.
8. Foreign source code (EA, community projects) is read, understood and rewritten, never copied; the source
   (file, function) is named in documentation or a comment.
9. New NuGet dependencies only with a justification; numbers in the documentation are measured, not taken over.
10. Keep the line endings of each file (`git ls-files --eol <file>`; `.gitattributes` sets `* -text`).

## Style and commits

- Everything is English: identifiers, comments, commit messages, documentation (`docs/`, README), script help and
  script output.
- Conventional Commits (`feat(simulation): …`, `fix: …`, `docs: …`, `test: …`), commits that are self-contained in subject matter.
- Work on `master` only, no branches: no feature branches are created or pushed; temporary local worktree branches of
  parallel agents are merged into `master`, deleted and never pushed. When a task is finished, commit and push to
  `origin master` without asking. Before every push run `pwsh scripts/audit-publish.ps1` (no game assets or anything
  exported from them ever reaches the repository).
- No force-push, no rewriting of published history.
- Contributions are under the licence of the repository (`GPL-3.0-or-later`, `LICENSE.md`).
