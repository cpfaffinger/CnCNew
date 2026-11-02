# Installation: from your own Steam installation to a playable directory

As of: 9 October 2026 (all numbers measured on that day on one machine; re-measure)

The repository contains only self-written code. Game files and everything that is exported from
them never lie in the repository. A playable directory is created on the user's machine: the
**patcher** reads the user's own Steam installation of *Command & Conquer Generals Zero Hour* and creates a
new game directory in another place.

Legal framework: the user provides their own game, purchased on Steam; this project
distributes no game files and no files generated from them. Details are in `LICENSE` and `NOTICE`.

## The launcher: from a fresh clone to the game

The way for a machine that never saw this project. It needs nothing but Windows, a Steam library with the game, and the network once:

```bat
git clone https://github.com/cpfaffinger/CnCNew.git C:\Games\CnCNew
C:\Games\CnCNew\CnCNew-Launcher.cmd
```

`CnCNew-Launcher.cmd` opens a window (`scripts/CnCNew-Launcher.ps1`). It runs under the Windows PowerShell 5.1 that comes with Windows, because PowerShell 7, which every other script needs, is one of
the things a fresh machine lacks. **It lists what it finds and asks before it installs anything:**

| Row | What it checks | When it is not there |
|---|---|---|
| Windows | 64-bit Windows 10 or 11 | refuses |
| Folder | the path of the checkout | refuses above 100 characters (the deepest build path is 136 more), warns in a synchronised folder |
| Disk | about 10 GB free | refuses |
| Internet | `api.nuget.org`, `raw.githubusercontent.com` | warns: the first build and the patch need them |
| Git | `git` | installs it; only new versions need it |
| .NET SDK | the version of `global.json` or newer in its feature band | installs it with winget, else `dotnet-install.ps1` of Microsoft into the user's profile, no administrator needed |
| PowerShell 7 | `pwsh` | installs it with winget, else the ZIP of its release into `%LOCALAPPDATA%\CnCNew\tools` |
| Visual C++ runtime | `vcruntime140.dll` (Stride's native libraries need it) | installs it with winget, else Microsoft's redistributable |
| Graphics, sound | what Windows reports | warns: without sound the goals about sound cannot be judged |
| The game | the Steam libraries through the registry and `libraryfolders.vdf`, the app's manifest | offers to open Steam's install page or to choose the folder |
| The install folder | where the game is made: **default `%USERPROFILE%\CnCNew`**, or what the user chose with "Choose the install folder ..." (remembered in `%LOCALAPPDATA%\CnCNew\install-folder.txt`) | refuses a folder of Steam or overlapping a Steam library, the checkout, a drive itself, a folder that holds other files, or one that cannot be written. In a folder that holds other things the button makes a `CnCNew` of its own |

Then, each step only if it is not done for this version: the game data (`scripts/install.ps1 -ContentOnly` into the install folder: the master check first, so a wrong language is refused within minutes, in plain words, before anything
is built), the game program, the check of the data file by file, a first start of the game (it also prepares the shaders), and the **comparison with the reference values**: the input manifest and the quick set of the
reference manifest on this machine's CPU. A difference there is no reason to stop, it is a finding: two machines that compute differently cannot play together. The window ends with a report on the desktop
(`CnCNew-setup-report-<date>.md`: machine, versions, every step) and three buttons: **Start the acceptance**, **Play the game**, and **Get the new version** when GitHub has one.

`Start-Acceptance.cmd` on a machine that is not set up goes to the launcher first. Without a window: `powershell -ExecutionPolicy Bypass -File scripts\CnCNew-Launcher.ps1 -CheckOnly` (changes nothing), `-Unattended`, `-GameDir`, `-InstallDir`, `-NoWinget`, `-NoSimulationCheck`.

**Steam is only read.** Every game file is opened read-only and copied one by one into the install folder; nothing is linked, and the client, the acceptance and every tool read that copy only. `GameInstaller` refuses any target in a Steam library, whoever calls it. A clone in the default folder keeps the game in its git-ignored `game\`. Choosing another folder later makes the game there; the old folder stays until you delete it.
`scripts/check-setup.ps1` checks the launcher itself; `-Full` walks the whole way in an empty profile. **This is the way of the development phase: it builds from source. A launcher that downloads finished binaries comes later.**

**How the launcher starts things.** Every program runs under a hidden `cmd.exe` with its output in a log of `%LOCALAPPDATA%\CnCNew\logs`, the acceptance too (`acceptance.log`). On a tester's Windows 11 Enterprise PC `pwsh.exe` started directly failed with `0xC0000142` (a dialog, then nothing), while the same program under `cmd.exe` had just made the game; the cause on that PC is not known. The launcher stays open until the acceptance has run for ten seconds, tries twice, and says the code in its own window when it does not start. `Start-Acceptance.cmd` leads to the launcher when `pwsh` ends with a code below zero.

## What the patcher does – and what it never does

It does:

1. find the Steam installation (or take `-GameDir`) and check it **file by file** against the checked-in
   master description `baselines/steam-master.json` (path, size, SHA-256);
2. only on a complete match, fetch the community patch GeneralsGamePatch from its public repository at the commit of
   `baselines/patch-master.json` (600 files, 110 MB, each taken only with its recorded size and hash; **the network is needed
   once**, `docs/PATCH-LAYER.md`) and make the runtime files of it;
3. export the runtime content, the patch above the game, to `<target>\content`
   (`RuntimeContentImporter`, schema 2): **no `.big` archive is taken over, every file lies individually in the
   directory**;
4. check the export (every file against size and SHA-256 of the manifest) and compare the generated
   `content-manifest.json` with the one recorded in the patch description (byte count,
   SHA-256, file count) – "the export is exactly the known one" is a single comparison;
5. write `<target>\install.json`, publish the client to `<target>\bin` and
   create `<target>\Start-CnCNew.cmd`.

It never does:

- write into the Steam directory, rename or delete anything there (all source files are opened
  read-only with `FileShare.Read`; a test compares file list, sizes and timestamps of the source
  before and after installation, repair and uninstallation);
- silently accept another version, language or a modded installation – there is
  **no** switch that turns the check off;
- write into a target that lies inside the game directory or any Steam library, contains the game directory, or is not
  empty and not our own earlier installation.

## Prerequisites

- Windows (client: Direct3D 11) and PowerShell 7 (`pwsh`).
- Owning the game on Steam and having it installed: Steam app **2732960**, **English** language version,
  fresh installation without mods. That is the one known master version: 350 files, 2,939,721,100 bytes.
- The .NET SDK from `global.json`: **10.0.401** (`rollForward: latestFeature`). The SDK is needed because
  tool and client are built from the source code. Install commands and the pitfalls of a fresh machine (missing
  `nuget.org` source, the Windows `python` alias) are in [`../BUILDING.md`](../BUILDING.md).
- Around 2.9 GB of free space at the target (measured: `content` 2,763,982,961 bytes in 25,609 files, `bin`
  67,793,031 bytes in 172 files); during a repair briefly twice the size of `content`, because the
  new content is created next to the old one and only takes its place after a successful check.

## Step by step

```powershell
git clone <repository>
cd CnCNew
pwsh scripts\install.ps1
```

Without parameters the script looks for the game under
`C:\Program Files (x86)\Steam\steamapps\common\Command & Conquer Generals - Zero Hour`, otherwise through the
Steam path of the registry (`HKCU:\Software\Valve\Steam`, `HKLM:\SOFTWARE\WOW6432Node\Valve\Steam`), the
libraries from `steamapps\libraryfolders.vdf` and `appmanifest_2732960.acf` (`installdir`), and says what
it has found. Without a specification the target is `%LOCALAPPDATA%\CnCNew\Game` – outside the repository and
outside Steam.

| Parameter | Meaning |
| --- | --- |
| `-GameDir <path>` | the Steam installation, in case it is not found |
| `-Target <path>` | the new game directory |
| `-SkipClient` | do not publish the client in this run (an existing one stays) |
| `-ContentOnly` | only `content` and `install.json`, no client, no start script |

The game is started with `<target>\Start-CnCNew.cmd`. The script names its paths relative to itself; the
directory can be moved as a whole. Further options are passed through to the client
(e.g. `Start-CnCNew.cmd --size 1920x1080`); without options the client opens the main menu.

A second call on an existing directory of our own is safe: it checks every runtime file,
restores missing and changed ones from the game directory, removes foreign files **in
`content`** and reports what it has done (`AlreadyComplete`, `Repaired`). An aborted
installation carries `"verified": false` in `install.json` and is completed on the next run.

### Where things are written

| Path | Content |
| --- | --- |
| `<target>\content` | exported runtime content with `content-manifest.json` |
| `<target>\install.json` | product, version and commit of the build, master ID, Steam app, importer version, hash/size of the content manifest, file count, `verified` – without timestamps and without absolute paths |
| `<target>\bin` | published client (`dotnet publish -c Release`) |
| `<target>\Start-CnCNew.cmd` | start with `--content-dir` pointing to `content` |

Nowhere else – except for the usual `bin`/`obj` directories of the build in the repository.

## What the master check reports and what to do

`check-install` (and every `install`) names every deviation individually:

| Message | Meaning |
| --- | --- |
| `missing` | a file of the master is missing |
| `renamed` | a file of the master is missing under its name, but lies byte for byte under another one (for instance a disabled archive `PatchWindow.big.bak`) |
| `changed` | different size or, at the same size, different SHA-256 |
| `extra` | a file that the master does not know (mods, additional DLLs, backup copies) |
| `harmless` | an additional file that is **explicitly** listed in `harmlessExtraFiles` of the master description; it is counted, but is not a deviation |

`harmlessExtraFiles` is currently **empty**: no file is known that Steam adds of its own accord.
An entry there is a verified case-by-case decision and subject to review; there is no rule by
pattern or extension.

In case of deviations:

1. Remove mods and all added files from the game directory (the `extra` entries, the new
   names of the `renamed` entries).
2. In Steam: Library → right-click on the game → Properties → Installed Files →
   *Verify integrity of game files* (in the German Steam client: „Dateien auf Fehler überprüfen“). Steam restores missing and
   changed files, but does not delete files it does not know.
3. Check the language: Properties → General → English.
4. Check again.

Measured example on a modded copy of the same installation: 23 deviations – 1 `missing`
(`Data/INI/INIZH.big`), 1 `renamed` (`PatchWindow.big` → `PatchWindow.big.bak`), 1 `changed` (`Game.dat`),
20 `extra` (among them `340_ControlBarProZH.big`, `400_ControlBarHDEnglishZH.big`, `990_DecalsZH.big`,
`CustomContentMaps.big`, `310_ExpandedLANLobbyMenu.big`, `d3d8.dll`). The comparison ignores the
case of paths; empty directories do not count.

## The developer path: `game\`

Scripts, tools and tests expect the content under **`game/content`** in the repository. `game/` is an
installation with exactly the layout of a user installation (`game\content`, `game\install.json`, optionally
`game\bin`) and is listed in `.gitignore`:

```powershell
pwsh scripts\dev-content.ps1                 # = install.ps1 -Target <repo>\game -ContentOnly
pwsh scripts\dev-content.ps1 -WithClient     # additionally game\bin and game\Start-CnCNew.cmd
dotnet run --project tools/CnCNew.AssetTool -- verify game/content
dotnet run --project tools/CnCNew.AssetTool -- check-export game/content
```

`.local/` stays ignored, but is no longer the place for content. Whoever has content in another place
specifies it as before with `--content-dir` or `-ContentDir`.

## Tool commands

The logic lies in `CnCNew.Import` (`InstallationMaster`, `InstallationInspector`, `GameInstaller`,
`InstallRecord`), the scripts are thin drivers. A project of its own was not necessary: under the layer rules `CnCNew.Import` is
the only connection between the original installation (import VFS, BIG)
and the runtime contract anyway, and the installation needs exactly this connection; only BCL code has been added.
The master description is additionally embedded in the assembly (`InstallationMaster.LoadBuiltIn`), so that
a later GUI can do without a repository path.

```powershell
dotnet run -c Release --project tools/CnCNew.AssetTool -- check-install <game directory> [--master <json>]
dotnet run -c Release --project tools/CnCNew.AssetTool -- describe-install <game directory> <out.json> [--name <name>] [--id <ID>]
dotnet run -c Release --project tools/CnCNew.AssetTool -- install <game directory> <target> [--patch <GameFilesEdited directory>] [--original] [--master <json>]
dotnet run -c Release --project tools/CnCNew.AssetTool -- check-export <content directory> [--original] [--master <json>]
dotnet run -c Release --project tools/CnCNew.AssetTool -- describe-patch <game directory> <Patch104pZH directory> baselines/patch-master.json --commit <commit>
dotnet run -c Release --project tools/CnCNew.AssetTool -- uninstall <target> --yes
```

Exit codes: 0 identical or done, 1 the game directory (or the export) is not the master,
2 invocation, target or I/O error. Without `--master` the embedded description applies.

`describe-install` generates `baselines/steam-master.json` (canonical JSON: UTF-8 without BOM, LF, paths with
`/` in ordinal order, no absolute paths, no timestamps; two runs are byte-identical). The
file contains name, Steam app ID (read from `steam_appid.txt` of the installation), file count, byte total,
per file path, size and SHA-256 as well as the expected export – **no file content**. The command computes the expected
export without writing (`RuntimeContentImporter.DescribeAsync`). The earlier
`inventory` format (list of `Path`/`Bytes`/`Sha256`) supplied the file entries as a model, but is not
sufficient: it knows neither identification nor expected export and writes platform-dependent line endings.
If `RuntimeContentImporter.ImporterVersion` changes, the description must be rewritten; `install`
rejects a description for another importer version.

Expected export of the master: 25,608 runtime files, `content-manifest.json` 27,087,727 bytes, SHA-256
`b1f8b14cd5cb005afd16237759c9020a8c3f2402874d24d2ff204e61c620fbab` (importer 2.0.0).

Tests: `dotnet run --project tests/CnCNew.Import.Tests` (synthetic installation with a hand-built
BIG archive) and with `-- --game-dir <Steam path>` additionally the real master against the embedded
description including the export hash (reads only).

## Uninstallation

```powershell
pwsh scripts\uninstall.ps1 -Target "$env:LOCALAPPDATA\CnCNew\Game" -Yes
```

Only a directory that carries our `install.json` is removed, and of it only `content`, `bin`,
`Start-CnCNew.cmd` and `install.json`; everything else stays and is listed, the directory itself
disappears only if it is empty afterwards. A directory with `steam_appid.txt` in its root is never
touched. Nothing is asked; without `-Yes` nothing happens.

## Measured numbers (9 October 2026)

| Operation | Duration |
| --- | ---: |
| `check-install` on the master (350 files, 2.9 GB) | 2.3 s (files in the cache of the operating system) |
| `check-install` on the modded copy | 2.5 s |
| `describe-install` (hash of all files and export dry run) | 18.5 s |
| `install` into a new directory (check, export, check) | 99.9 s on the first, 53.0 s on a further run into another target |
| `install.ps1` in total with tool build and client publishing | 130 s |
| repeated `install` on a complete installation (`AlreadyComplete`) | 11.5 s |
| `AssetTool verify` on `game/content` | 8 s |
| Real-data test `Import.Tests -- --game-dir …` | 37 s |
| Client from the installed directory, smoke test of a skirmish with 300 frames | 23 s, exit code 0 |

## The package (Phase 30)

For players there is a package that needs neither the source code nor .NET: `pwsh scripts/package-client.ps1 -Zip` makes `cncnew-win-x64` (about 990 files,
320 MB) with the client and the patcher, both self-contained, and

| File | What it does |
| --- | --- |
| `Install-CnCNew.cmd [<game directory>]` | finds the Steam installation by itself (as `install.ps1` does; or takes the directory named), checks it file by file against the master and exports the content into `game\` beside it. The Steam installation is only read. |
| `Start-CnCNew.cmd` | starts the client with that content |
| `README-FIRST.txt`, `LICENSE.md`, `NOTICE.md`, `THIRD-PARTY.md`, `licences\` | what it is, the licence, the third-party packages and their licence files |

The package holds no file of the game and none of the patch: `scripts/check-client-package.ps1` compares every file with the master installation, the patch's sources, the original export and the patched one, and
runs the packaged patcher (`check-install auto`) and the packaged client (two smoke tests). Measured on 14 October 2026 on the development machine: 992 files,
321 MB, no file of the game, the patcher found the installation at the default path and found it to be the master, both smoke tests ended with exit code 0.
Not shown: a machine without .NET (this one has the SDK), a machine where Steam keeps the game in another library, and a person running it.

## Limits

- A script, no GUI. The logic is testable as a library and prepared for a GUI.
- Exactly one version and language (Steam, English). Other versions need their own, measured
  master description; there is no developer switch for unchecked installations. Whoever deliberately wants to import
  different content uses `AssetTool runtime-import` as before – that writes no
  `install.json`, so the result cannot pass itself off as a checked installation.
- `install.ps1` publishes the client from the source code: that needs the SDK and, because that publishing
  is not self-contained, .NET 10 on the machine at run time. The package of the section above is self-contained.
- The export leaves out what the client does not read: the original's program files, manuals and installation metadata stay in the Steam installation (F-33, Phase 56).
- `content-manifest.json` is written with the line endings of the platform; the recorded hash applies
  to Windows.
- The search through registry and `libraryfolders.vdf` has not been tried on an installation outside the
  default path.
- That the installed client starts is verified by a smoke test, not by a human at the
  screen.
