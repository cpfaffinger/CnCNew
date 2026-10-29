# Determinism across machine boundaries (Phase 18)

As of: 6 October 2026, commit `b432fb7`; §2a (the matrix with the manifest of Phase 22, Q39.24) at the state `d7fca91`, §2b (repetition at the starting state of Phase 23) at the state `20ffce9`, §2c (the 60 Hz manifests after the switch of the default, 9 October 2026) at the state `ea52f87` – only two rows are measured there so far. This document describes **how** it is checked that two machines compute the same world, and **what** of that is measured. The individual findings D1–D9 with test,
fix and before/after values are in `docs/DETERMINISM-FINDINGS.md`; the yardstick itself (reference manifest, input manifest) in `docs/SIMULATION-CORE.md` §37.

## 1. What is compared

Lockstep (`docs/MULTIPLAYER-PLAN.md` §2.1) demands that every machine computes bit-identically the same state from the same commands. The check has two stages, so that a deviation can be attributed:

1. **Inputs** – `SimTool input-manifest check <content> baselines/input-manifest.json`: definition fingerprint, bone table, world fingerprint, content hashes of the built catalogs, per map file hash,
   terrain fingerprint, cells and start positions (150 maps). If that deviates, the machines already compute with different data.
2. **Computation** – `SimTool reference-manifest check <content> baselines/reference-manifest.json --set full`: 50 runs (reference match, stress test, 16 AI runs, three 8-player runs, skirmish overview over 64 maps,
   27 mission runs), per run end tick, winner, end hash, chain hash, "run 2 identical", "identical after snapshot continuation" and for missions "identical to the uninterrupted run after save/load".
   The quick set (`--set quick`, 9 runs) takes about 5 minutes, the full one about 25 to 40.

In addition `--verify-incremental` (checks in every tick the incremental hash against the fully computed one; uncovers missing `MarkDirty`, finding D8).

## 2. The matrix (measured on 6 October 2026)

Every row is a full run of both manifests against the checked-in files; "identical" means exit code 0 for both, i.e. not a single deviating field in 50 runs.

| Row | How | Inputs | Computation (set) | Result |
|---|---|---|---|---|
| Release, run 1 | `dotnet CnCNew.SimTool.dll …` | identical | 50/50 (full) | **identical** |
| Release, run 2 | the same call once more | identical | 50/50 (full) | **identical** |
| Debug build | `dotnet build -c Debug tools/CnCNew.SimTool` | identical | 9/9 (**quick**) | **identical**; the full set did not finish within two hours in the Debug build and was not waited for |
| Tiered compilation off | `DOTNET_TieredCompilation=0` | identical | 50/50 (full) | **identical** |
| Tiered PGO off | `DOTNET_TieredPGO=0` | identical | 50/50 (full) | **identical** |
| ReadyToRun off | `DOTNET_ReadyToRun=0` | identical | 50/50 (full) | **identical** |
| Hardware intrinsics off | `DOTNET_EnableHWIntrinsic=0` | identical | 50/50 (full) | **identical** |
| AVX2 off | `DOTNET_EnableAVX2=0` | identical | 50/50 (full) | **identical** |
| Server GC | `DOTNET_gcServer=1` | identical | 50/50 (full) | **identical** |
| Culture `de-DE` | `CNCNEW_TEST_CULTURE=de-DE` | identical | 50/50 (full) | **identical** |
| Culture `tr-TR` | `CNCNEW_TEST_CULTURE=tr-TR` | identical | 50/50 (full) | **identical** |
| Culture `ar-SA` | `CNCNEW_TEST_CULTURE=ar-SA` | identical | 50/50 (full) | **identical** |
| **Linux** on the same machine | WSL2 (Ubuntu, kernel 6.6), `dotnet publish -r linux-x64 --self-contained`, the same content via `/mnt/c` (ICU instead of NLS, different C runtime) | identical | 50/50 (full) | **identical** |
| different .NET patch version | – | – | – | **open**: only .NET 10.0.12 is installed |
| **second physical machine, different CPU** | instructions in §4 | – | – | **open**: has not taken place (needs the human and a second machine) |

The culture of the development machine is `de-AT`; so all runs without a switch have always run under a non-invariant culture. The switch `CNCNEW_TEST_CULTURE` sets the default culture of all
threads in `SimTool` before any command runs.

**Before the fixes** (state `981aec4`) the Linux run did not pass: the definition snapshot had 139,503,384 instead of 144,626,133 bytes (line endings of the platform), and with that a different fingerprint; the
end hashes and chains of all nine runs of the quick set, in contrast, were already identical – the computation itself was portable, only one input was not (D9).

## 2a. The matrix with the manifest of Phase 22 (Q39.24, measured on 6 October 2026, state `d7fca91`)

Phase 22 switched the fixed-point resolution from 16 to 24 fractional bits and with that rewrote both manifests (`docs/FIXED-POINT-Q39-24.md`). The rows of this machine were repeated with the new
files; every row is again `input-manifest check` plus `reference-manifest check` against the checked-in files, "identical" means exit code 0 for both and no deviating field.

| Row | How | Inputs | Computation (set) | Result |
|---|---|---|---|---|
| Release, run 1 | `CnCNew.SimTool.exe …` (copy of the `bin` directory) | identical | 50/50 (full) | **identical** |
| Release, run 2 | the same call once more | identical | 50/50 (full) | **identical** |
| Debug build | `dotnet build -c Debug tools/CnCNew.SimTool` | identical | 9/9 (**quick**) | **identical** (the full set was not run in the Debug build, as in Phase 18) |
| Tiered compilation off | `DOTNET_TieredCompilation=0` | identical | 50/50 (full) | **identical** |
| Tiered PGO off | `DOTNET_TieredPGO=0` | identical | 50/50 (full) | **identical** |
| ReadyToRun off | `DOTNET_ReadyToRun=0` | identical | 50/50 (full) | **identical** |
| Hardware intrinsics off | `DOTNET_EnableHWIntrinsic=0` | identical | 50/50 (full) | **identical** |
| AVX2 off | `DOTNET_EnableAVX2=0` | identical | 50/50 (full) | **identical** |
| Server GC | `DOTNET_gcServer=1` | identical | 50/50 (full) | **identical** |
| Culture `de-DE` | `CNCNEW_TEST_CULTURE=de-DE` | identical | 50/50 (full) | **identical** |
| Culture `tr-TR` | `CNCNEW_TEST_CULTURE=tr-TR` | identical | 50/50 (full) | **identical** |
| Culture `ar-SA` | `CNCNEW_TEST_CULTURE=ar-SA` | identical | 50/50 (full) | **identical** |
| **Linux** on the same machine | WSL2 (Ubuntu, kernel 6.6.87), `dotnet publish -r linux-x64 --self-contained`, the same content via `/mnt/c` | identical | 50/50 (full) | **identical** |
| different .NET patch version | – | – | – | **open** as in §2 |
| **second physical machine, different CPU** | instructions in §4 | – | – | **open** as in §2 |

For the arithmetic this means: the routines derived for a bit count (`FixedMath`: long division on machine words, bitwise extended root, CORDIC with 24 steps) compute the same with and without
hardware intrinsics (`Math.BigMul` and `BitOperations.LeadingZeroCount` then have software paths) and the same under Linux as under Windows. What §3 names as not verified stays not verified.
Additionally run deterministically, without being part of the manifest: twelve runs at 60 and 120 Hz (`docs/TICKRATE-MIGRATION.md` §11.5; second fresh run and continuation the same, where checked).

New rule for §5: **the scale of `Fixed` is in one place.** `Tests.FixedScaleSourceRules` forbids it as a number or shift in all projects and its use outside the simulation
(exception: `CnCNew.Presentation.FixedConvert`). Formats that carry raw values change their version with the resolution; an old state is rejected, never reinterpreted.

## 2b. The matrix at the starting state of Phase 23 (measured on 7 October 2026, state `20ffce9`)

§2a ran at the state `d7fca91`, i.e. **before** the merge with Phase 21 and **before** the range repair (`b274df5`, `SquaredReach`). Both were announced as result-identical; the rows of this machine were therefore repeated at the merged
state `20ffce9` before Phase 23 changes anything. Procedure as in §2a: per row `input-manifest check` and `reference-manifest check` against the checked-in files from a copy of the `bin` directory;
"identical" means exit code 0 for both and 50 (or 9) rows `OK`. The runs did not run quietly (other runs alongside); the run time is given only for orientation.

| Row | How | Inputs | Computation (set) | Run time | Result |
|---|---|---|---|---|---|
| Release, run 1 | `dotnet CnCNew.SimTool.dll …`, `--jobs 6` | identical | 50/50 (full) | 404 s | **identical** |
| Release, run 2 | the same call once more | identical | 50/50 (full) | 450 s | **identical** |
| Debug build | `tools/CnCNew.SimTool/bin/Debug`, `--jobs 3` | identical | 9/9 (**quick**) | 1,035 s | **identical** (the full set was not run in the Debug build, as before) |
| Tiered compilation off | `DOTNET_TieredCompilation=0` | identical | 50/50 (full) | 648 s | **identical** |
| Tiered PGO off | `DOTNET_TieredPGO=0` | identical | 50/50 (full) | 628 s | **identical** |
| ReadyToRun off | `DOTNET_ReadyToRun=0` | identical | 50/50 (full) | 604 s | **identical** |
| Hardware intrinsics off | `DOTNET_EnableHWIntrinsic=0` | identical | 50/50 (full) | 741 s | **identical** |
| AVX2 off | `DOTNET_EnableAVX2=0` | identical | 50/50 (full) | not noted | **identical** |
| Server GC | `DOTNET_gcServer=1`, `--jobs 5` | identical | 50/50 (full) | 760 s | **identical** |
| Culture `de-DE` | `CNCNEW_TEST_CULTURE=de-DE` | identical | 50/50 (full) | 726 s | **identical** |
| Culture `tr-TR` | `CNCNEW_TEST_CULTURE=tr-TR` | identical | 50/50 (full) | 587 s | **identical** |
| Culture `ar-SA` | `CNCNEW_TEST_CULTURE=ar-SA` | identical | 50/50 (full) | 489 s | **identical** |
| **Linux** on the same machine | WSL2 (Ubuntu, kernel 6.6.87.2), `dotnet publish -r linux-x64 --self-contained`, the same content via `/mnt/c`, `--jobs 4` | identical | 50/50 (full) | not noted | **identical** |
| different .NET patch version | – | – | – | – | **open** as in §2 |
| **second physical machine, different CPU** | instructions in §4 | – | – | – | **open** as in §2; to be made up before Phase 26 |

All 13 rows of this machine are identical at the merged state; no reference value has changed. What §3 names as not verified stays not verified.

## 2c. The matrix with the 60 Hz manifests (Phase 23, state `ea52f87`, 9 October 2026) – only two rows measured so far

**The matrix from §2b is measured at the 30 Hz starting state `20ffce9`.** Since then Phase 23 has changed the behaviour above 30 Hz in four rounds, merged three behaviour changes at 30 Hz
(manifests `6a99102`, `8a6e5b7`, `cca6f92`), switched the default to 60 Hz (`e8632bb`) and rewritten both manifests at 60 Hz (`ea52f87`; reference manifest schema 2, input manifest
schema 2 with `simulation.tickRate`). A matrix with the new files does not exist yet. Measured at the state `ea52f87` are two rows, on the same machine, Windows, Release
(logs of the session: `verify\switch\check-60-1.log`, `check-60-2.log`, `input-60.log`):

| Row | How | Inputs | Computation (set) | Result |
|---|---|---|---|---|
| Release, run 1 | `reference-manifest check … --set full` against `baselines/reference-manifest.json` (60 Hz, schema 2) | identical ("Input manifest (60 Hz, schema 2): identical.") | 50/50 (full) | **identical** |
| Release, run 2 | the same call once more | – | 50/50 (full) | **identical** |
| Debug build | – | – | – | **not measured** |
| Tiered compilation off, Tiered PGO off, ReadyToRun off | – | – | – | **not measured** |
| Hardware intrinsics off, AVX2 off | – | – | – | **not measured** |
| Server GC | – | – | – | **not measured** |
| Cultures `de-DE`, `tr-TR`, `ar-SA` | – | – | – | **not measured** |
| **Linux** on the same machine (WSL2) | – | – | – | **not measured** |
| different .NET patch version | – | – | – | **open** as in §2 |
| **second physical machine, different CPU** | instructions in §4 | – | – | **open** as in §2; to be made up before Phase 26 |

In addition, with the same binary (default 60 Hz) against the last 30 Hz files (`cca6f92`, kept in the session): `input-manifest check` "(30 Hz, schema 1): identical" and
`reference-manifest check --set full` 50 of 50 runs identical – a manifest is checked at the rate its file names, independently of the default.

**What follows from this:** the ten switch and culture rows and the Linux row are **not verified** for the simulation above 30 Hz. Above 30 Hz, computation paths run that do not exist at 30 Hz
(the fractions of a frame step, the end-of-frame vision, the tables of the constants per frame); that the matrix passed at 30 Hz says nothing about them. The three 30 Hz intermediate states of the
phase, too, are checked only under Windows in the Release build, not across the matrix and not under Linux. Until the rows have been added, the following holds for 60 Hz: deterministic on one machine, under
Windows, in the Release build, twice in a row.

## 2d. Linux at 60 Hz, and a match across the two systems (Phase 27, 12 October 2026)

The Linux row of §2c is measured, and a second kind of comparison exists: not one program run twice, but processes of two systems in **one** match.

**The environment** (the project owner allowed it for this purpose): the WSL distribution `Debian` of the development machine (kernel 5.15.167.4-microsoft-standard-WSL2,
x86_64) with the .NET runtime 10.0.12 installed by Microsoft's `dotnet-install.sh` into `~/.dotnet` - the runtime only, no SDK. The binaries are the ones built under
Windows (`dotnet build -c Release`); a framework-dependent build is the same files on both systems, so nothing is built twice. Windows runs the same runtime version
10.0.12. The machine is one AMD Ryzen 3 PRO 4350GE.

| Row | How | Result |
|---|---|---|
| **Linux (WSL2), inputs** | `SimTool input-manifest check game/content baselines/input-manifest.json` under Linux, binaries of `4f7d5ca` | **identical** |
| **Linux (WSL2), computation** | `SimTool reference-manifest check … --set full --jobs 6` under Linux, the same binaries | **50 of 50 runs identical** (60 Hz, schema 2) |
| **One match, two systems** | `scripts/check-net-cross-os.ps1` (`NetTool net-match --linux server,2,4`): the dedicated server, its proxy and clients 2 and 4 are Linux processes, clients 1 and 3 Windows processes; three players, five computer players, a watcher, 45 s of game time through the proxy with 40 ms delay, 10 ms jitter, 1 % loss | **one hash chain** on all four clients (2,700 ticks), the server agreed on every tick |
| **A state across the boundary** | the same, and the Linux client 2 loses its connection and joins again with the state a Windows client (1 or 3, the first whose reports are in time) wrote | the state loaded under Linux hashes to what the clients agreed on; all four end in the same state |

Every process states its operating system itself (`Os` in a client's result, `os=` in the server's `listening` line), and the check fails if one ran elsewhere than
asked. Without the WSL distribution or its runtime the check ends with exit code 3 and the tier shows the step as **NOT RUN** - never as passed
(`docs/TESTING.md`).

**What this proves.** The simulation, the command codec, the session state and its packing, the protocol and the transport give the same result under Windows and
under Linux: two operating systems, two builds of the runtime with their own JIT back ends and libraries, a different C library, different sockets and a different
timer. A state written under one system is read under the other. What §2 measured in 2026 for 30 Hz holds for the 60 Hz manifests.

**What it does not prove.** It is the same CPU, and under WSL2 the same physical cores behind a virtual machine. Whether another vendor's or another generation's CPU,
or ARM, computes the same is exactly as open as before: E1 stays open, and only §4 closes it. It is also the same runtime version on both sides; "a different .NET
patch version" stays open. The switch and culture rows of §2c (tiered compilation, PGO, ReadyToRun, intrinsics, AVX2, server GC, the three cultures, the debug build)
are **still not measured** at 60 Hz.

## 3. What follows from this and what does not

- **Verified:** on one machine (AMD Ryzen 7 PRO 6850U, x64) the result is independent of JIT tier, PGO, ReadyToRun, hardware intrinsics, GC kind, culture, Debug/Release (quick set) and operating system
  (Windows 11 against Linux under WSL2). The manifest passes twice in a row.
- **Not verified:** a different CPU (different vendor, different generation, ARM), a different .NET patch or major version, a kernel other than that of WSL2, a native Linux. The simulation computes in
  integers (`Fixed`, `FixedTrig`, `SageRandom`) and `SourceRules` forbids `float`, intrinsics and anything culture-dependent in the core and in everything that feeds it (D6); that makes a CPU dependency unlikely,
  but does not replace the run. **E1 in the register therefore stays open** until §4 has been carried out once.
- The matrix checks the 50 runs of the manifest. Paths that none of these runs enters (player inputs of a human, rarely used modules) are not covered by it; D8 showed that precisely there
  errors lay (four unsuspected state changes without `MarkDirty` in 19 of 45 mission maps).

## 4. Instructions: the run on a second machine

Needs a human. None of this is committed; original data does not leave one's own possession (copy locally, no upload).

1. On the development machine: `dotnet publish tools/CnCNew.SimTool -c Release -r win-x64 --self-contained true -o <ordner>\simtool` (for Linux `-r linux-x64`, for ARM `-r win-arm64`/`linux-arm64`).
2. Copy onto a storage medium: `<ordner>\simtool`, the directory `game\content` (about 25,682 files) and `baselines\input-manifest.json`, `baselines\reference-manifest.json`.
3. On the second machine (no .NET needed), in the directory with the three parts:
   ```
   simtool\CnCNew.SimTool input-manifest check content input-manifest.json
   simtool\CnCNew.SimTool reference-manifest check content reference-manifest.json --set quick
   simtool\CnCNew.SimTool reference-manifest check content reference-manifest.json --set full
   ```
4. Report the result: the three closing lines ("identical" or the first deviation per run), plus CPU model, operating system and the output of `simtool\CnCNew.SimTool --version`, if available.
5. In case of a deviation look at the input manifest first: if it deviates, the error lies in the data or their preparation; if it is the same and a run deviates, it lies in the computation –
   then repeat the same run with `--only <id> --verify-incremental` and enter the result in `docs/DETERMINISM-FINDINGS.md`.

The result belongs in the table in §2 (row "second physical machine") and in `docs/MULTIPLAYER-PLAN.md` §4 (E1).

## 5. Rules that remain from this

- The manifest is the yardstick (`docs/MULTIPLAYER-PLAN.md` §6 item 2). Whoever changes it does so in a commit of its own with a cause and before/after values.
- New simulation inputs go through the checked conversion routines (`SingleBits.ToDecimal` for floating-point values from files, `ExactBoneMath` for the bone table); `SourceRules` and `FeederSourceRules` enforce that.
- Every state change calls `MarkDirty`; a `Dirty` flag set by hand is forbidden (rule in `SourceRules`). New modules run once with `--verify-incremental`.
- Before every phase completion: `input-manifest check`, `reference-manifest check --set full` – under Windows; that is the acceptance.
- **The Linux row is informative, not an acceptance criterion** (decision of the project owner of 9 October 2026, `docs/MULTIPLAYER-PLAN.md` §7): the target platform is for now only
  Windows, Linux comes into consideration later solely as the host of the dedicated server. The Linux rows of the matrices in §2 to §2b remain as a measurement of their state; a new
  Linux run (WSL) is useful on changes to formats or fingerprints, but no phase fails because of it or because of its absence. If the server under Linux becomes a delivery target
  (Phase 28), the row becomes a criterion again there – for the server, not for the client.
- **Golden hashes are kept at the product rate (60 Hz, since Phase 23).** `check` runs at the rate and in the schema that **the file** names (`simulation.tickRate`); a deviating
  `--tick-rate` is rejected ("A manifest is checked at the rate it was written at."). `write` takes `--tick-rate` or the default. 30 and 120 Hz have no golden hashes of their own; they are
  checked through the rate-equivalence suite against 30 Hz as the definition (`docs/SIMULATION-CORE.md` §39.7) and through tests that name their rates themselves.
- Since Phase 23 the phase completion additionally includes: the suite with 0 / 0 in all ten groups and the test programs at 30, 60 and 120 Hz (`--tick-rate N`).
