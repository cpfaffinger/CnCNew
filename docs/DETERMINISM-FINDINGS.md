# Determinism findings D1–D9 (Phase 18, item 4)

As of 6 October 2026, starting commit `e5046b7`. For every finding of the reading search run (`docs/handoffs/HANDOFF-PHASE18.md`, table "Findings of the search run") this document records: confirmed or refuted, the test that shows it, the correction, whether reference values have changed, and what remains open. The check mode of the reference manifest (`SimTool reference-manifest`) and the determinism matrix are not part of this document.

Rule of the phase: no change of behaviour of the simulation except determinism corrections; every finding first confirmed or refuted by a test, then fixed; every correction that changes a reference value alone in one commit.

**Everything here was measured on one machine** (Windows 11, x64, .NET 10, Release). No run on a second machine or with a different .NET version has taken place; under Linux (WSL2 on the same machine) only the definition snapshot has been compared (D9). The corrections make the affected places independent of CPU, culture and runtime routines *by construction*; that two different machines actually compute bit-identically is only proven by the matrix.

## 1. Overview

| No. | Verdict | Correction | Reference values | Commit |
|---|---|---|---|---|
| D1 | dependency present in the code, cannot be triggered on this machine (table equal under four JIT switches) | bone table computed exactly (integer fractions, own arctangent) | unchanged; table byte-identical | `bef1f8c` |
| D2 | runtime routine confirmed; deviation not measurable (one machine) | `SingleBits.ToDecimal`: integer reproduction of `(decimal)float`, identical for all 3,923,635 reals of the 150 maps | unchanged | `979c787` |
| D3 | mechanism confirmed; influence on the checked runs not proven | `EntityTable`: enumeration in ID order by construction | unchanged | `cfe3951` |
| D4 | confirmed (hash, diagnostics and passed-on arguments depended on the culture) | ordinal comparer, `InvariantText` | unchanged | `f04c687` |
| D5 | confirmed | terrain fingerprint with waypoint IDs, waypoint links and map objects; map file hash as an accessor | **terrain fingerprints and replay files changed**; tick hashes unchanged | `2eb7772` |
| D6 | confirmed (rules incomplete, suppliers unchecked) | extended rules, second rule set, explicit exceptions | unchanged | `32ed854` |
| D7 | confirmed; 9 groups on one map (`GLA04`) | total order | unchanged | `21c0fa8` |
| D8 | **confirmed on real data** (19 of 45 campaign/challenge maps) | `MarkDirty` in four places; source rule | **`mission-run` of six measured missions changed** | `99a02fe`, `df523e4` |
| D9 | **line ending confirmed** (Linux: 139,503,384 instead of 144,626,133 bytes, different fingerprint); escaping not measurable | line ending/indentation fixed, golden value, source rule | unchanged; snapshots byte-identical, now equal under Linux | `3e75333` |

## 2. Reference values before and after

Measured at the starting commit and after every correction (Release; tools rebuilt after every source change).

| Run | before (`e5046b7`) | after | changed by |
|---|---|---|---|
| `SimTool reference-match` | FactionGLA, tick 14,490, `23B4B25DF62765D8`, chain `DE5849AF9F8493C4` | same | – |
| `SimTool stress` | `56882F763EABEF67`, chain `B8E663493F2C73D5` | same | – |
| `ai-match` Alpine Assault `FactionChina,FactionGLA` | China, tick 23,370, `0BD4FE5EEB2C37E7`, chain `BF9939E5A3B45736` | same | – |
| `ai-match` Alpine Assault `FactionAmerica,FactionChina` | America, tick 16,965, `BA10AC1EB0EB8AC9`, chain `F669459C2FA6E39C` | same | – |
| `ai-match` Bitter Winter `FactionAmerica,FactionChina` | America, tick 33,750, `38380B920A732955`, chain `5FED11D99834B1B6` | same | – |
| `mission-run MD_USA05 --ticks 3000` | `1CB28E6AA923BDEF`, chain `0816C3438CB7F689` | same | – |
| `mission-run MD_GLA05 --ticks 3000` | `317B464E8BA98377`, chain `6823506872EBFF9E` | **`826CCD82C8536DC2`, chain `10330D7D179054FD`** | D8 (`99a02fe`) |
| `mission-run MD_USA01 --ticks 3000` | `C970C1640A518A58`, chain `9074BA5AFC4B19AB` | **`8D26113C9DA64703`, chain `945589AB7750A0CB`** | D8 (`df523e4`) |
| `mission-run MD_USA02 --ticks 3000` | `B67A4CDAC03048DA`, chain `82C9F032E56272A4` | **`3AFE4707A243C3C7`, chain `A60D461ED547E16F`** | D8 (`df523e4`) |
| `mission-run MD_GLA03 --ticks 3000` | `144C93AE58338C72`, chain `5D469B2FDDFB5F3E` | **`E79236DDB44B3E30`, chain `369AFAA71BB959AE`** | D8 (`df523e4`) |
| `mission-run GC_LaserGeneral --ticks 3000` | `124403A596021418`, chain `4722B14F7E8A7FB8` | **`020982072960A29A`, chain `9FBBC6F2F689BC5C`** | D8 (`df523e4`) |
| `mission-run GC_NukeGeneral --ticks 3000` | `8F3ECBAC07C3343E`, chain `02CE97C4EBD00207` | **`652D33E7F7B4882A`, chain `E1C65818832ABB9F`** | D8 (`df523e4`) |
| `CampaignTool audit --ticks 900 --campaign CHALLENGE_0` | 7/7 deterministic, 7/7 identical after save/load, end probes victory 7/7, defeat 6/7 | same | – |
| Definition snapshot retail base data (`IniTool compile … --maps exclude`) | 144,626,133 bytes, SHA-256 `68879F19FA8DA1381D4F0513DB034D879323BFEFB990797F112E694B728E63BF` | same | – |
| Definition snapshot full inventory (`--maps include`) | 145,242,921 bytes, SHA-256 `6443CFA3D9E4B2B375178A62D489A4015DE02D28434588F18A0461F4C986B99B` | same | – |
| Bone table (`ModelGeometry.Fingerprint`) | `93D5819BF231304D…` | `93D5819BF231304D112CABABBC0A69986E82CF7E353BBC8742EEBE7A88F1DA47` (same) | – |
| World fingerprint (definitions + bone table, first 16 digits) | `63FE84D91A4EB0C6` | same | – |
| Terrain fingerprint `MD_USA05` / `MD_GLA05` (first 16 digits) | `B3FE20CB4BE45347` / `8265FD4E188F2D61` | **`FEE2594754FE793E` / `6583781069017CBB`** | D5 (`2eb7772`) |
| Terrain fingerprint Alpine Assault / Bitter Winter | `2F2793ED2FFE1E40` / `4C42D6D18A4977B4` | **`F561E2A2F78B3108` / `EBD38F9CF58F17A9`** | D5 (`2eb7772`) |
| Replay file `ai-match` Alpine Assault China/GLA (118,564 bytes, SHA-256) | `4D9FD31C…` | **`5675B0C1…`** (only the header) | D5 (`2eb7772`) |

The "before" values of the five missions `MD_USA01`, `MD_USA02`, `MD_GLA03`, `GC_LaserGeneral` and `GC_NukeGeneral` were measured at the state `2eb7772` (immediately before `df523e4`), not at the starting commit; none of the corrections before it touches them by construction, for these five this has not been measured.

## 3. D4 – culture-dependent content in the hashed script state

**Confirmed.** Test `Determinism D4` (`tests/CnCNew.Simulation.Tests/Tests.Determinism.cs`, `ScriptStateIsCultureIndependent`): the same synthetic mission under de-DE, tr-TR, ar-SA, sv-SE and an invented culture (minus sign `~`, decimal separator `#`), compared with the run under the invariant culture. Before the correction:

| Culture | State hash | passed-on arguments | Diagnostic |
|---|---|---|---|
| de-DE, tr-TR | same | `SPEECH_PLAY(1,5,-2,25,0\|…)` instead of `1.5,-2.25,0` | same |
| ar-SA | **different** | `1٫5,؜-2٫25,0` | `buildable status ؜-1 out of range` |
| sv-SE | **different** (minus sign `−` **and** sorting: `Ö` after `Z`) | `1,5,−2,25,0` | `buildable status −1 out of range` |
| invented | **different** | `1#5,~2#25,0` | `buildable status ~1 out of range` |

Causes and correction:

- `_priorityDelta` was a `SortedDictionary<string,int>` without a comparer (culture sorting); now `StringComparer.Ordinal`.
- Diagnostics are part of the hash and were interpolated with the current culture. New: `InvariantText` (`src/CnCNew.Simulation/Scripting/InvariantText.cs`), an interpolation handler with the invariant culture; `Diagnose($"…")` binds to it, counter keys and the coordinates of passed-on actions use `InvariantText.Format`.
- `EntityId`/`PlayerId.ToString`, the `X16` hashes in the snapshots and two `int.TryParse` (D6) name the invariant culture explicitly.

Real data: `CnCNew.Game.Tests` → `DeterminismTests`, test "D4": under each of the five cultures everything is recomputed from the files on (compile definitions, bone table, catalogue, decode `GC_ChemGeneral`, terrain, 900 mission ticks). Result after the corrections: definition fingerprint, world fingerprint, terrain fingerprint, program fingerprint, chain hash and final hash identical to the invariant run under all five cultures.

Search for further culture dependence (simulation, `CnCNew.Data`, map decoding): no culture comparers, no `ToUpper()`/`ToLower()`, no `StartsWith`/`EndsWith`/`IndexOf` with a string without `StringComparison`, no sorting over strings without an ordinal comparer. Interpolations with numbers outside diagnostics: only non-negative indices and IDs (field names such as `Rider{slot}`, keys such as `"tower" + index`); .NET formats non-negative integers with ASCII digits in every culture, the minus sign is the only culture property.

**Open:** a lexical rule sees no types. A negative number or a `decimal` in an interpolation or concatenation that gets into state is noticed only by the culture test – and only on the paths it runs through. The full culture row of the matrix (all runs of the manifest) is outstanding.

## 4. D7 – non-total sort comparison of the waypoints

**Confirmed.** Test `Determinism D7` (`WaypointOrderIsTotal`): 48 stacked waypoints of one name that differ only in height, in two file orders. Before the correction two different waypoint orders (`24 31 38 45 4 11 …` against `44 37 30 23 16 9 …`): the sort is not stable, and the comparison left out Z, Id and NextId.

Correction: `TerrainMap.CompareWaypoints` compares name, X, Y, Z, Id, NextId. Waypoints that are still equal after that are equal in everything.

Real maps (counted over all 150, test "D2 and D7" of the `DeterminismTests`): 25,460 named waypoints; **9 groups (18 waypoints)** agree in name, X and Y, all on `Maps/GLA04/GLA04.map`, and all 9 differ in height or Id – their order was undefined there. No map of the reference runs is affected; the reference values are unchanged. The terrain fingerprint of `GLA04` has not been compared before/after (it changes with D5 anyway).

## 5. D6 – source rules

**Confirmed:** `SourceRules` checked only `src/CnCNew.Simulation` and did not know the constructs named in the finding.

New (`tests/CnCNew.Simulation.Tests/Tests.SourceRules.cs`):

- A scanner (strings emptied, comments and multi-line raw strings removed), rules as named regular expressions, **explicit exceptions** (file, rule, line text, justification). An exception that no longer matches anything makes the test fail. The rules check themselves against a list of lines that they must catch and one that they must not catch.
- **Rule set simulation** (`Determinism rules hold in the simulation sources`): wall time; ambient randomness; floating point (type names, `MathF`, `Math.Sin/Cos/Tan/Asin/Acos/Atan/Atan2/Sqrt/Pow/Exp/Log/Round/Floor/Ceiling/Truncate/…`, real literals without `m` such as `var x = 1.5`, `1e5`, `2f`); `System.Numerics` (except `BitOperations`, `BigInteger`), `System.Runtime.Intrinsics`, `Vector64/128/256/512`, `Vector<T>`, `Matrix4x4`, `Quaternion`; threads, `Task`, `async`/`await`, `Parallel`, `Interlocked`, `volatile`, concurrent collections; `File`/`Directory`/`Process`/`Environment`/`AppContext`/`AppDomain`/`RuntimeInformation`; reflection; culture-dependent text operations; numbers as text without the invariant culture (`ToString()`, `ToString("…")`, `string.Format`, `Parse`/`TryParse`); `GetHashCode()`/`HashCode.`; line ending and path separator of the platform (`Environment.NewLine`, `AppendLine`, `WriteLine`, `Path.Combine` …, D9); dirty flag set by hand (D8).
- **Rule set suppliers** (`… in the sources that feed the simulation (D6)`): `src/CnCNew.Data`, `SageMapDecoder.cs`, `LegacyAssetReaders.cs`, `ModelGeometryLoader.cs`, `ExactBoneMath.cs`. The same rules; floating point only as a file format: no `float`/`double`, no floating-point function, no `(decimal)` cast, no reading of a single as a number (`Int32BitsToSingle`, `ReadSingle`, …). The one path is `SingleBits.ToDecimal` (maps) or `Dyadic.FromSingleBits` (bone table).

Violations found – fixed (all hash-neutral): `EntityId`/`PlayerId.ToString`, `ToString("X16")` in `WorldSnapshot`, `MissionSnapshot`, `ScriptProgram`, `int.TryParse` of the command set slots (`DefinitionCompiler`) and of the WND version (`LegacyAssetReaders`), the bone table (D1), the map decoding (D2), the dirty flag (D8).

Violations found – justified as an exception (18 in the simulation, 12 at the suppliers): `ToString()` of enum names, `DefinitionId` and `StringBuilder`; key hashes (`Fixed.GetHashCode`, `HashCode.Combine` of the science/upgrade bit sets and of a comparer – only for equality lookup, no value and no order is derived from it); a `decimal.TryParse` whose culture argument is on the following line; `MarkDirty` itself; `ValidateWak` (import check for finiteness); the parameter type of the one entry point of the bone table.

**Limits:** the rules are tripwires, not a proof. They see no types (interpolation holes, concatenations, `var x = pivot.X * 2`), no code in interpolation holes (the string is emptied as a whole) and no unordered enumeration. `src/CnCNew.Game/Live/LiveScenario.cs` (staged demo scenes, `(decimal)float` and `Math.PI`) does not belong to the rule set: the scenes are local tools, not a match path – whoever takes them over into a network match has to route them through `SingleBits`.

## 6. D8 – state change without `MarkDirty`

**Confirmed, on real data.** In the tools the incremental hash was only checked against the full one every 5,000 or 20,000 ticks.

Find: `MD_GLA05`, tick 855. The script action `WAREHOUSE_SET_VALUE` changed the stock of five `SupplyDock` objects (`boxes` 400 → 740). `SupplyWarehouseDockUpdate.SetCashValue` and `DockUpdateBase.SetOpen` (ferry) set `Entity.Dirty = true` by hand instead of calling `MarkDirty`. The flag without the entry in the dirty list of the world is worse than no flag: every later `MarkDirty` returns early, the cached object hash stays permanently stale, and the tick hash only becomes right when something recomputes it completely. Writing a snapshot does that (`WorldSnapshot.Write` → `ComputeFullHash`). Consequences:

- A mission hashed differently depending on whether and when a saved game had been written.
- A world restored from a snapshot (which hashes fully) ran away from the continuous one: `MD_GLA05`, restored at tick 150, deviated at tick 900.
- Two machines would have agreed on a hash that no longer described the supply stock.
- `mission-run` reported "continuation identical: True" because there both compared worlds had written the snapshot.

Tests:

- `Determinism D8: the incremental hash equals the full hash in every tick of the stress scenario` – the synthetic stress test (6,000 ticks, 600 objects), checked in every tick; the checked run hashes like the unchecked one. Passed before and after the correction (the stress test does not contain the error).
- `Determinism D8: a scripted warehouse value reaches the tick hash; writing a save does not change it` – failed before the correction.
- Synthetic skirmish and synthetic reference match now check in every tick (`VerifyIncrementalEvery = 1`).
- Real data (`DeterminismTests`, test "D8"): reference match 6,000 ticks, AI match Alpine Assault 9,000 ticks, `MD_USA05` 600, `GC_ChemGeneral` 1,500, `MD_GLA05` 1,200 ticks, each checked in every tick and compared with the unchecked run. `--determinism-full`: reference match and AI match in full, plus all campaign and challenge maps (result below).

Correction: both methods take the world and call `MarkDirty`; source rule "dirty flag set without MarkDirty"; `VerifyIncrementalHash` now names the stale objects and the tick.

**Reference value changed** (alone in commit `99a02fe`): `mission-run MD_GLA05 --ticks 3000` final hash `317B464E8BA98377` → `826CCD82C8536DC2`, chain `6823506872EBFF9E` → `10330D7D179054FD`. The behaviour of the simulation is unchanged; only the hash now describes the state. All other reference values unchanged (§2).

### Second find: containers, particle cannon, special powers (commit `df523e4`)

The full run (`DeterminismTests --determinism-full --determinism-only D8`: all 45 maps `MD_*`/`GC_*`, 1,500 ticks each, every tick checked) found, after the first correction, **18 further maps** with a stale hash:

| Place | Field | first seen | Cause |
|---|---|---|---|
| `ContainModuleBase.Add`/`Remove` (garrison, transport, tent) | occupant list and occupied slots of the container, `ContainedBy` of the occupant | `GC_NukeGeneral` tick 40 (`GuardTower`), `MD_USA01` tick 59 (`AmericaVehicleChinook`), `MD_USA04` tick 71 (`Tent02`), 18 maps in total | the marking was the business of the callers; a scripted garrison entry on arrival did not do it. The base now marks both ends itself |
| `ParticleUplinkCannonUpdate.PrepareScriptedPath`/`Initiate` | `pendingPath[]`, `pendingPathTick` | `GC_LaserGeneral` tick 361, `GC_SuperWeaponsGeneral` tick 541 (after the container correction) | script path noted without `MarkDirty` |
| `SpecialPowerModuleBase.StartRecharge`/`OnSpecialPowerCreation`, `NAMED_SET/ADD_SPECIAL_POWER_COUNTDOWN`, objective driver | `availableOnFrame` | reported by the manifest agent (`MD_USA02` from tick 1,338, `AmericaCommandCenter`) | readiness tick set without `MarkDirty`; no longer occurred in the own 1,500-tick run after the container correction, marked nevertheless after reading the source code |

Tests: `Determinism D8: entering and leaving a container reaches the tick hash` (garrison by script, leaving, team garrison, evacuating – checked in every tick; without the correction it fails at tick 73 on the `AlphaBunker`). The full run after all corrections: all 45 maps, the whole reference match (14,791 ticks) and the AI match Alpine Assault China/GLA (23,671 ticks) checked in every tick, no stale hash, tick hashes equal to the unchecked runs.

**Reference values changed** (alone in commit `df523e4`; `mission-run … --ticks 3000`, in each case two fresh runs identical and continuation after save/load identical, before as after): `MD_USA01` `C970C1640A518A58` → `8D26113C9DA64703`, `MD_USA02` `B67A4CDAC03048DA` → `3AFE4707A243C3C7`, `MD_GLA03` `144C93AE58338C72` → `E79236DDB44B3E30`, `GC_LaserGeneral` `124403A596021418` → `020982072960A29A`, `GC_NukeGeneral` `8F3ECBAC07C3343E` → `652D33E7F7B4882A` (chains in §2). Measured unchanged: reference match, stress test, the three AI runs, `MD_USA05`, `MD_GLA05` (value since `99a02fe`), audit `CHALLENGE_0`. Each of the 18 maps now hashes differently; only the five named ones have been measured.

**Open:** what has been checked is limited runs (1,500 ticks per map, normal difficulty, without player inputs). A change without `MarkDirty` on a path that no checked run runs through remains undiscovered until the check mode of the manifest runs more broadly. The incremental hash covers objects, timers and commands; the script state is described completely every time anyway.

## 7. D3 – unordered enumeration of the objects

**Mechanism confirmed, influence on the checked runs not proven.**

- Test `Determinism D3` (`EntityEnumerationIsOrderedById`): after three deaths and four births a running world enumerated `1,11,3,4,10,9,7,8,12`, the same world from a snapshot `1,3,4,7,8,9,10,11,12`.
- Real data (`DeterminismTests`, tests "D3", `--determinism-full`): the world restored from a snapshot at **every** checkpoint and computed to the end beside the continuous one, every tick hash compared. On the old dictionary: reference match (14 restorations, 102,074 ticks), AI match Alpine Assault China/GLA (23, 268,433), America/China (17, 140,522) and `MD_USA05` (20, 28,500) identical; `MD_GLA05` deviated – the cause was D8, not the order. After D8 and with an ordered table: all five identical (`MD_GLA05` 20 restorations, 28,500 ticks). The run "old dictionary with D8 correction" on `MD_GLA05` has not been measured.

Correction: `EntityTable` (`src/CnCNew.Simulation/World/EntityTable.cs`) – lookup by the ID as before, enumeration in ascending ID order by construction. IDs only grow, a new object is appended; a removed one leaves a gap that enumerations skip; after the destruction phase of a tick the gaps are closed. Removing during the enumeration remains allowed; an enumeration that survives a reordering throws. No caller was changed. Reference values unchanged; cost in the stress test (`SimTool stress --ticks 108000`, 13,076 objects alive at the same time, two builds alternating, three runs each, machine not quiet): dictionary 96.4 / 71.1 / 84.6 µs per tick, `EntityTable` 72.4 / 92.8 / 81.6 µs per tick – no difference outside the scatter, no performance statement beyond that.

Inventory of the enumerations over unordered collections in `src/CnCNew.Simulation` (without `Testing/`):

| Collection | Enumerations | Verdict |
|---|---|---|
| Objects: `_entities.Values` (13 places in `SimulationWorld*.cs`: acquisition, battle plan, area damage, construction, crates, vision, upgrades, hash) and `IWorldServices.Entities`/`SimulationWorld.Entities` (roughly 60 loops without their own sorting in modules, script VM, collision, `SkirmishAi`; roughly 30 more sort themselves with `OrderBy(Id)`) | with side effect: among others `ResolveFootprintCollisions`/`DeliverCollisions` (crushing, toppling, collision modules in sequence), `StepBlocked`/`StationaryBlockersAhead` (first hit), `ReevaluateUpgrades`, crate and field effects, script actions with `FirstOrDefault` | **now ordered** (ID order by construction; the own `OrderBy` are superfluous but harmless and unchanged) |
| `_timerById` (`Dictionary<ulong, SimTimer>`) | `ComputeFullHash` (sum), snapshot (sorted by ID) | order-independent because commutative sum or sorted |
| `_dirty` (list of the changed objects) | `ComputeIncrementalHash` | order-independent because sum per object |
| `SkirmishAi._boarding` | `Where(expired).ToList()` → removal | order-independent because every removal stands for itself |
| `SkirmishAi._roles`, `_producible`, `_structureCandidates`, `_retryAt`, `_powerRetry`, `_scienceRetry`, `_orders`, `_idleSince`, `_siteWatch`, `_rallySet`, `_clearedHints`, `_seenAt`, `_unreachable` | no enumeration, only lookup | irrelevant |
| `ContainModules._containedSinceTick`, `RetailMatchModules._containedSinceTick`, `ScriptEngine._busyThisTick`, `_entityTeams`, `NavigationGrid._escape`/`_temporary`, `TerrainMap._waypointsById`, `_playerSlots`, `_humanSequence`, the name tables of `ScriptProgram` | no enumeration, only lookup | irrelevant |
| local sets (`visited`, `used`, `seen`, `reserved`, `links`, `byId`, `labels`) | `byId` is enumerated sorted, the others are only queried | irrelevant |
| `ScriptCoverage` (`ImmutableHashSet<string>`) | `Validate()` | order of the messages depends on the string hash of the process; only test output, no state |
| `SortedDictionary`/`SortedSet` (players, commands per tick, all tables of the script VM, `SkirmishAi._enemies`) | many | ordered by type (strings ordinal, without exception since D4) |

**Open:** the inventory was created by searching over declarations and `foreach`/LINQ uses, not by a tool with type information. The restoration test covers five runs, not the full manifest.

## 8. D2 – map coordinates via `(decimal)float`

**Runtime routine confirmed; a deviation is not measurable on one machine.** Every coordinate, every angle and every script number of a map went through `(decimal)float` (`Decimal.DecCalc.VarDecFromR4`, seven significant digits). There was exactly one place: `SageMapDecoder.ChunkReader.ReadReal`; `TerrainMap`, `ScriptProgram`, `SkirmishMatchSession` and the script VM convert the `decimal` values created this way further with `Fixed.FromDecimal` (pure decimal arithmetic).

Correction: `CnCNew.Data.World.SingleBits.ToDecimal(int bits)` reproduces the routine with integers from the bit pattern – including its intermediate products in double precision (as correctly rounded 53-bit mantissas, as IEEE 754 prescribes for a single multiplication or division), the scaling steps, the rounding to even and the removal of trailing zeros. Value **and** scale of the result are those of the cast (the scale counts because coordinates are passed on to the presentation as text). `ReadReal` uses it; there is no second conversion, and the supplier rule set (D6) forbids it.

Proof of the equality (no hash-neutral rebuild on suspicion):

- `CnCNew.Data.Tests` (`SingleBitsMatchTheRuntimeCast`): 9,310,191 bit patterns – every exponent, both signs, a fixed pseudo-random sequence over the whole 32-bit space, one concentrated on map sizes, plus special cases (zeros, subnormals, decimal limits, half seventh digits) – identical in value and scale; 763,065 overflowing patterns throw in both paths.
- `DeterminismTests`, test "D2 and D7": **all 150 maps, 3,923,635 reals, 788,421 different bit patterns, 0 deviations** (via the observer `SageMapDecoder.Decode(stream, realBits)`).

Reference values unchanged. The test compares against the cast of *this* runtime: on a runtime whose cast changes it fails and indicates the change; the routine stays what it is.

**Open:** `Fixed.FromDecimal` computes with `System.Decimal` of the runtime (exact integer arithmetic according to the specification, but not our own code). `LiveScenario` (demo scenes) still converts via the cast (§5).

## 9. D1 – bone table in floating point

**Dependency present in the code; cannot be triggered on this machine.** The table (parking, hangar, apron, runway and helipad poses of the nine retail airfields) is part of the world fingerprint. It was read from the rest matrices of the skeleton (`Matrix4x4` products in single precision, whose instruction selection – vector width, fused multiply-add – belongs to processor and runtime) and its directions from `MathF.Atan2` (C runtime), then rounded to 1/16 unit and 1/1024 rad. π/2 lies 4.6·10⁻⁶ rad beside a rounding boundary.

Attempt to trigger the finding: the table hash of the old computation under `DOTNET_EnableFMA=0`, `DOTNET_EnableHWIntrinsic=0`, `DOTNET_EnableAVX=0` and `DOTNET_TieredCompilation=0` – each time `93D5819BF231304D…`, as without a switch. On this CPU the rounding swallows the differences. Whether a different CPU, an ARM processor or a different C runtime crosses a boundary cannot be measured here. The Linux measurement of the coordinator (WSL, the same machine) yielded the same value for the old table.

Correction (`src/CnCNew.Game/Data/ExactBoneMath.cs`, `ModelGeometryLoader.RestPose`, `W3dSkeleton.RestPivots`): a single is an integer times a power of two; a rest pose is nothing but sums and products of such numbers. `Dyadic` (`BigInteger` mantissa, binary exponent) and `ExactTransform` compute the chain **without any rounding** from the pivot numbers as the file names them (the same formulas as `CreateFromQuaternion * CreateTranslation` and `local * parent`). The direction is an integer arctangent to 2⁻¹⁹² (alternating series, π after Machin), rounded to the table step; if a direction lies so close to a rounding boundary that it is not decidable, that is an error, not an estimate. Positions round to even as before. No floating-point arithmetic remains in the loader; the supplier rule set checks both files.

Tests (`CnCNew.Game.Tests`, `DeterminismTests`):

- "D1: a synthetic skeleton …" (runs without content): known poses (quarter turn, 30°, exact halves of a sixteenth, half turn, a tilted chain), every bone against the path in single precision, the arctangent against `Math.Atan2` over 20,000 vectors of all quadrants.
- "D1: the bone table of the retail airfields has its golden hash": SHA-256 `93D5819BF231304D112CABABBC0A69986E82CF7E353BBC8742EEBE7A88F1DA47`.

**The table has not changed** (the same SHA-256 before and after), and therefore neither have the world fingerprint (`63FE84D91A4EB0C6…`) and all reference values (measured, §2).

**Open:** the run on a second CPU. The skeletons of the presentation (`W3dSkeleton.RestWorldTransforms`) remain floating point; they do not reach the simulation.

## 10. D5 – gaps of the fingerprints

**Confirmed.** Test `Determinism D5` (`TerrainFingerprintCoversWaypointLinksAndMapObjects`): seven variants of a map – a waypoint link leads somewhere else, waypoint IDs swapped, a map object moved, rotated, of a different type, with a different owner, one more – all had the fingerprint of the original map before the correction.

Correction:

- `TerrainMap.ComputeFingerprint`: identifier `terrain-fingerprint-2`, per waypoint additionally `Id` and `NextId`, then all map objects in file order (name, position, angle, flags, properties).
- `ContentFileSystem.FileHash(path)` and `GameData.MapFileHash(path)`: the SHA-256 of the map file from the content manifest (64 hex digits, upper case) – the identity of a whole map. The terrain fingerprint covers what the terrain of the simulation reads; sides, teams, scripts and trigger areas are covered only by the file hash. Test "D5" of the `DeterminismTests`: 150 maps, the manifest hashes of three maps recomputed against the files.

**Fingerprint values changed** (alone in commit `2eb7772`): every terrain fingerprint and with it the replay files that are recorded on a map (the header stores it); values in §2. **No tick hash, no winner and no end tick changes** (measured: all runs from §2; the replay file of the reference match, which has no terrain, is byte-identical).

**What a later phase (25, protocol and joining) has to add** – deliberately not invented here:

- An **identifier of the code state** of the simulation. The definition fingerprint describes data, not code: two machines with the same data and different simulation code consider themselves compatible today. There is no scheme for it (build number, hash of the assemblies, version maintained by hand); that is a decision of Phase 25, together with `CommandTypes.Version` and the replay format.
- The **map file hash** in the join comparison (the accessor is there, the protocol is not) and in the replay header, if replays are to name maps unambiguously.
- The **AI seed** and the remaining match parameters (slots, factions, start positions) in a match description that both sides compare (`docs/MULTIPLAYER-PLAN.md` E11, E15).
- The program fingerprint of the script VM (`ScriptProgram.Fingerprint`, 64 bits over names and type numbers, without parameters) is weak and is in the hashed mission state; for missions the file hash suffices until then. Strengthening it changes every mission hash and was not touched here.

## 11. D9 – encoding of the definition fingerprint

**Line ending confirmed, measured across operating systems.** The definition fingerprint is the SHA-256 of an indented JSON text from `System.Text.Json`. The line ending of an indented document there is `Environment.NewLine`. Measurement of the coordinator before the correction (Linux/WSL, the same content): snapshot 139,503,384 bytes instead of 144,626,133, world fingerprint `8E92E383…` instead of `63FE84D9…` – two machines with different operating systems would have rejected each other.

Correction (`DefinitionSnapshotWriter`): the options name line ending (`\r\n`, which the snapshot always had under Windows – existing fingerprints remain valid), indent character and indent width explicitly.

Proofs:

- Byte identity on real data before/after under Windows (`IniTool compile`, Release): retail base data 144,626,133 bytes `68879F19…63BF`, full inventory 145,242,921 bytes `6443CFA3…B99B` (§2).
- **Under Linux after the correction** (WSL2 Ubuntu, `dotnet publish tools/CnCNew.IniTool -r linux-x64 --self-contained`, `compile … --maps exclude`): 144,626,133 bytes, SHA-256 `68879F19FA8DA1381D4F0513DB034D879323BFEFB990797F112E694B728E63BF` – identical to Windows. The full inventory has not been measured under Linux.
- `CnCNew.Data.Tests` (`FingerprintEncodingIsPinned`): a snapshot with every class of specially treated characters (HTML-sensitive ASCII, quotation marks, backslash, non-ASCII) – what is checked is the expected escapes, CR LF at every line ending and the golden SHA-256 `024D8B21…BA43` (1,884 bytes), recorded before the fixing and unchanged afterwards.
- Source rule "platform line end or path separator" (D6, both rule sets): `Environment.NewLine`, `AppendLine`, `WriteLine`, `Path.DirectorySeparatorChar`/`Combine`/`Join`/… – no hit in the simulation and its suppliers. The bone table writes `\n` explicitly; the snapshot replaces `\` in source paths with `/`.

**Open:** the **escaping** is still the default encoder of the runtime; whether it differs between .NET versions cannot be measured on one version. The golden value turns a deviating runtime into a test failure instead of a silent mutual rejection. An encoding of the fingerprint input of our own, fixed (own writer instead of serialiser; then property order and number text also belong to us) needs either unsafe code in `CnCNew.Data` (an own `JavaScriptEncoder`) or a rebuild of the snapshot writer and has not been done.

## 12. Tests and how they run

| Test | Location | Invocation |
|---|---|---|
| `Determinism D3/D4/D5/D7/D8 …`, `Determinism rules …` | `tests/CnCNew.Simulation.Tests` (`Tests.Determinism.cs`, `Tests.SourceRules.cs`) | `dotnet run --project tests/CnCNew.Simulation.Tests [-- --only "Determinism"]` |
| `SingleBitsMatchTheRuntimeCast`, `FingerprintEncodingIsPinned` | `tests/CnCNew.Data.Tests` | `dotnet run --project tests/CnCNew.Data.Tests` |
| `DeterminismTests` (D1 synthetic without content; D1 golden, D2/D7, D3, D4, D5, D8 on real data) | `tests/CnCNew.Game.Tests` | `dotnet build -c Release tests/CnCNew.Game.Tests`, then `dotnet run -c Release --no-build --project tests/CnCNew.Game.Tests -- --only DeterminismTests --content-dir game/content` |
| the same, full runs (every restored world to the end; all 45 campaign/challenge maps checked in every tick) | | `… --determinism-full` (roughly one hour); `--determinism-only D8` selects the tests of one finding |

## 13. What is open after this item

1. **Second machine, different CPU, different .NET version:** none of it has run. Under Linux only the definition snapshot has been compared.
2. **D8 in breadth:** what has been checked is 1,500 ticks per campaign map without player inputs, reference match, one AI match and the stress test. The error type (state changed, hash not) has thereby been found in four places that nobody had suspected; further ones on unchecked paths (skirmishes with special powers, late mission phases, player commands) are probable until the check mode of the manifest runs broadly. Structurally, `MarkDirty` in the setter of every hashed field would be the solution; that is a rebuild, not a correction.
3. **D3:** inventory without type information; the restoration test covers five runs.
4. **D4/D6:** no rule sees numbers in interpolations and concatenations; the culture row of the matrix over the full manifest is outstanding.
5. **D9:** own escaping (see above).
6. **D5:** identifier of the code state, file hash in the protocol, match description (Phase 25); weak program fingerprint of the script VM.
7. **Not touched:** `LiveScenario` (demo scenes via `float`), `Fixed.FromDecimal` via `System.Decimal`, snapshot JSON (`JsonStateWriter`) with the default encoder of the runtime (format, no hash).
8. **Documentation consequences** (not part of this item, the files were locked): the reference values of the six missions and the terrain fingerprints in `CLAUDE.md`, `docs/handoffs/HANDOFF-PHASE18.md`, `docs/SIMULATION-CORE.md` and in the reference/input manifest (`baselines/`) are to be rewritten after the merge.
