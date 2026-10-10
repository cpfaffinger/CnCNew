# Building

What a machine needs to build, test and publish CnCNew, and how to get there from a fresh Windows installation.
Installing the game for playing is described in [docs/INSTALLATION.md](docs/INSTALLATION.md); the working rules and the
complete command list are in [CLAUDE.md](CLAUDE.md), the short version for contributors in [CONTRIBUTING.md](CONTRIBUTING.md).

## Requirements

| Requirement | Version | Needed for |
|---|---|---|
| Windows | 10/11, x64 | everything (the only target platform; the client uses Direct3D 11) |
| Git | any current version | the clone, `scripts/get-references.ps1` |
| .NET SDK | **10.0.401** or a newer feature band of .NET 10 (`global.json`, `rollForward: latestFeature`) | build, tests, tools, client |
| PowerShell 7 (`pwsh`) | 7.x (measured with 7.6.6) | every script under `scripts/` – Windows PowerShell 5.1, which ships with Windows, cannot run them |
| Python 3 | 3.x (measured with 3.13.15) | only `scripts/audit-publish.ps1`, the check before every push |
| NuGet source `nuget.org` | – | package restore (Stride 4.3 and its tooling) |
| *Command & Conquer Generals – Zero Hour* | Steam app 2732960, fresh **English** installation without mods (`baselines/steam-master.json`) | `game/content`: real-data tests, audits, manifests, the client |
| Disk space | around 3 GB for `game/`, more for `references/` and build output | – |

Without the game the solution still builds and the synthetic tests run; everything that takes `--content-dir game/content` does not.

## Installing the requirements

With `winget` (part of Windows 10/11):

```powershell
winget install --id Git.Git -e --source winget
winget install --id Microsoft.DotNet.SDK.10 -e --source winget
winget install --id Microsoft.PowerShell -e --source winget
winget install --id Python.Python.3.13 -e --source winget
```

Open a new terminal afterwards so that `pwsh` and `python` are on `PATH`. Two pitfalls measured on a fresh machine
(10 October 2026):

- **`python` opens the Microsoft Store or prints "Python was not found".** Windows ships an app execution alias named
  `python.exe` that is not Python. After the installation the real `python.exe` must come first on `PATH`
  (`Get-Command python -All`), or the alias is switched off under Settings › Apps › Advanced app settings › App execution aliases.
- **The restore fails with `NU1101` and names only "Microsoft Visual Studio Offline Packages".** The user configuration
  (`%APPDATA%\NuGet\NuGet.Config`) has no `nuget.org` source. Check with `dotnet nuget list source` and add it once:

  ```powershell
  dotnet nuget add source https://api.nuget.org/v3/index.json -n nuget.org
  ```

## Setting up the developer state

The repository holds only what was written for it. One script recreates the rest – it checks the requirements, clones the
four reference sources into `references/` at the recorded commits, exports `game/content` from your Steam installation
(which is only read), builds, and checks against the baselines:

```powershell
git clone https://github.com/cpfaffinger/CnCNew.git
pwsh CnCNew/scripts/setup-dev.ps1        # options: -ShallowReferences -GameDir <path> -SkipReferences -SkipContent -SkipBuild -SkipCheck
```

Every step can be skipped, a failed step is reported and the script goes on, and running it again repairs. The single steps:

```powershell
pwsh scripts/get-references.ps1          # references/ (git-ignored); -Shallow fetches only the recorded commits
pwsh scripts/dev-content.ps1             # game/content and game/install.json (git-ignored); -GameDir <Steam installation>
dotnet build CnCNew.slnx                 # 0 warnings expected (TreatWarningsAsErrors)
```

`game/`, `references/` and `.local/` are never committed.

## Building and testing

```powershell
dotnet build CnCNew.slnx
dotnet run --project tests/CnCNew.Simulation.Tests
dotnet run --project tests/CnCNew.Simulation.Tests -- --content-dir game/content
dotnet run --project tools/CnCNew.AssetTool -- verify game/content
dotnet run --project tools/CnCNew.IniTool -- compat-check game/content baselines/compatibility.json
```

- Which checks a change needs is fixed in [docs/TESTING.md](docs/TESTING.md); `pwsh scripts/test.ps1` selects and runs
  them from the changed paths (`-Tier affected|gate|full`, `-Plan` prints the commands).
- Tests are standalone console programs (non-zero exit code on failure), no xUnit/NUnit. All test programs, audits,
  manifest checks and tool commands: [CLAUDE.md](CLAUDE.md), section "Build, tests, audits".
- Long runs use the Release build; build a Release tool on its own before `--no-build`
  (`dotnet build -c Release tools/CnCNew.SimTool`), and `tests/CnCNew.Game.Tests` only in Release.
- `tools/CnCNew.UiRenderTool` is deliberately not in `CnCNew.slnx` and is built on its own
  (`dotnet build tools/CnCNew.UiRenderTool`).
- Before every push: `pwsh scripts/audit-publish.ps1` (needs Python 3 and `game/`).
