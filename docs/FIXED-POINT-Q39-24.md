# Fixed-point resolution: from Q47.16 to Q39.24 (Phase 22)

As of: 6 October 2026, branch of Phase 22, switch in commit `d7fca91`. Brief: `docs/handoffs/HANDOFF-PHASE22.md`; frame: `docs/MULTIPLAYER-PLAN.md` §2.6 and §4 (E8). Everything here is measured at **30 Hz**
(Release, retail data `game/content`, one machine: AMD Ryzen 7 PRO 6850U, 16 logical processors, Windows 11, .NET 10.0.12). Where a number is not measured quietly, it says so.

`Fixed` is a `long` with now **24 fractional bits** (resolution 2⁻²⁴ ≈ 6 · 10⁻⁸, range ± 5.5 · 10¹¹) instead of 16 (2⁻¹⁶ ≈ 1.5 · 10⁻⁵, ± 1.4 · 10¹⁴). The switch changes every hash, every snapshot and every replay and
therefore happened on its own: first preparatory work that changes no result, then one commit with one cause.

**Two findings are in §5 and §6:** (1) the square of some ranges from the data (1,000,000) leaves the new range and threw – in none of the measured runs, but
on paths reachable by a player. The switch reported that and did not work around it; **it has since been fixed (§5a, addendum of 7 October 2026), without changing a reference value.** (2) The reference match is a war of attrition whose end depends on the production rolls; its seed was changed in
a commit of its own.

## 1. Commits

| Commit | What | Reference values |
|---|---|---|
| `659db1d` | `SimTool reference-manifest results`: the quantities of a run that are not hashes | unchanged |
| `6596310` | `FixedMath`: product, quotient, root and trigonometric functions derived for a number of bits | unchanged |
| `756e978` | raw-value computations of the simulation without the assumption of 16 bits | unchanged |
| `1c07699` | one conversion `Fixed` ↔ floating point outside the simulation (`FixedConvert`) | unchanged |
| `d770cd4` | source rule "the scale is written once" for all projects; tests without raw numbers | unchanged |
| `065067a` | tests in units; `reference-match --world-seed/--driver-seed/--record-only`; `rate-audit` lists the value range | unchanged |
| `5d1a09f` | construction health in 128 bits; `results --commands` (command streams as text) | unchanged (`--set full`) |
| `41c3bc0` | **reference change, one cause:** seed of the reference match `5EED0005` → `5EED0007` | only `reference-match` |
| `a0e7b23` | vision-circle test in machine words at 16 fractional bits | unchanged |
| **`d7fca91`** | **THE SWITCH:** `Fixed.FractionBits` 16 → 24, format versions, both manifests new | all 50 runs |

After every commit of the preparatory work `input-manifest check` and `reference-manifest check --set quick` passed, at the end (`5d1a09f`) `--set full` (50 runs identical); after `a0e7b23` the quick set again. The manifests of the switch are in the
switch commit itself.

## 2. The preparatory work (without a change of results)

### 2.1 Arithmetic for a number of bits (`src/CnCNew.Simulation/Numerics/FixedMath.cs`)

`FixedMath` computes on raw values with the number of fractional bits as an argument; `Fixed` and `FixedTrig` call it with `Fixed.FractionBits`, the tests also with other numbers of bits. Every routine is exact
integer arithmetic: products and quotients are those of the 128-bit formula (to the nearest number, halves away from zero), the root is rounded down, overflow throws.

- **Product** and **quotient with a small dividend**: the routines of Phase 19, the shift widths follow the number of bits.
- **Quotient with a large dividend** (`|a| · 2^F` does not fit into a machine word): long division on machine words – first `|a| / |b|`, then the F fractional bits are brought down in the largest pieces
  that keep `remainder · 2^piece` in the word (the remainder is smaller than the divisor, whose leading zeros are the room). Instead of one 128-bit software division two to three 64-bit divisions. At 16 bits "large" began
  at 2³¹ units and did not occur; at 24 bits it begins at 32,768 units (every squared distance above 181 units).
- **Root of a large value**: the 64-bit root of as many fractional bits as fit into a word (an even number fewer than all), after that the remaining root bits one by one from the comparison of the
  candidate square with the exact 128-bit radicand. At most twelve steps at 24 bits.
- **Angles:** π, 2π, π/2 and the arctangent table are hard-coded constants with 61 fractional bits, rounded to the resolution (never from `Math.*` at runtime). At 16 bits that gives exactly the numbers that
  were written out before (205,887, 411,775, 102,944 and the 16 table entries). The CORDIC (`Atan2`) makes one step per fractional bit and scales the vector by the fractional bits – by fewer if the
  vector would otherwise leave the word (above 2⁴⁵ raw the old routine silently returned nonsense). `Sin`/`Cos` keep the Taylor polynomials (up to x¹³ and x¹² respectively: truncation error 7 · 10⁻¹⁰ and 7 · 10⁻⁹, below every supported
  resolution); what remains is the rounding of the seven steps, and that shrinks with the resolution. The divisions by the series terms are integer divisions (bit-identical to the fixed-point quotient, because the
  terms are even numbers).

**Equivalence proofs** (`tests/CnCNew.Simulation.Tests/Tests.FixedResolution.cs`):

| Test | What | Scope |
|---|---|---|
| `FixedMathMatchesWideArithmeticAtEveryResolution` | product, quotient, root against `BigInteger` formulas (root by bisection, independent of the routine) at **16, 24, 12 and 32** bits | per number of bits over 1 million pairs (edges, random bit lengths, coordinates, squared distances up to 10⁸, projections "large square by large square") and over 150,000 roots; in addition `Fixed` itself |
| `FixedTrigReproducesTheSixteenBitResults` | the trigonometric functions at 16 bits against the **earlier implementation**, which stands word for word as an oracle in the test | `Sin`/`Cos`: every raw angle of three revolutions in both directions (2.47 million) plus 300,000 of arbitrary size; `Atan2`: every vector of a raw grid ±260, of a grid in whole units and 6 million vectors of every length up to 2⁴⁴ raw |
| `FixedTrigPrecisionFollowsTheResolution` | error against `Math.Sin/Cos/Atan2` per resolution, constants at 24 bits, very long vectors | numbers below |

Measured largest error of the trigonometric functions against the exact ones:

| Resolution | `Sin` | `Cos` | `Atan2` (rad) |
|---|---|---|---|
| 16 bits (one step 1.5 · 10⁻⁵) | 2.59 · 10⁻⁵ | 2.42 · 10⁻⁵ | 5.94 · 10⁻⁵ |
| 24 bits (one step 6.0 · 10⁻⁸) | 1.00 · 10⁻⁷ | 1.15 · 10⁻⁷ | 2.69 · 10⁻⁷ |

The precision therefore follows the resolution (factor 210 to 260 at factor 256), without a higher polynomial order and without guard bits; the document of Phase 6 named only the bound 10⁻³ for `Atan2`.

### 2.2 Raw-value computations in the simulation

Everything that computes directly on raw values was read (`.Raw`, `FromRaw`, shift operations). The simulation is **not** `checked` as a whole – only the operators of `Fixed` –, so a raw-value computation that leaves the
range wraps silently. Changed:

| Place | Before | Now |
|---|---|---|
| `VisionMap` (three circle tests: `Trace`, `RevealReference`, `ForCircle`) | squares of raw differences in `long`, unchecked; literal `65536` for half a cell | the circle test is **defined at 16 fractional bits**, whatever resolution `Fixed` has: centre and radius are truncated to it, the cell centres (whole units) are exact in it. At 24 bits the square of a radius of 300 units would need 67 bits; a 128-bit test was a third slower (§8.2). The computation is now `checked`: a radius above 46,000 units throws instead of wrapping silently as before. A cell is ten units wide. |
| `StructureToppleUpdate` | product of three raw values "exact at 48 fractional bits", start angle as a literal | product in 128 bits, truncated to the fine resolution (fractional bits + 32); the start angle 0.001 follows from the number of bits. At 16 bits nothing is truncated. |
| Idle scan of the turrets (`DrawScanFraction`) | `Bits % 65536` | the lowest 16 bits of the draw are the 16 leading binary places, further places come from the bits above them: the same draw is the same number at every resolution, as far as its places reach. |
| `JetAIUpdate`, `AIUpdateInterface` | `FromRaw(34315)` (π/6), `FromRaw(1024)` (twice) | `FixedTrig.PiFraction(1, 6)`, `Fixed.FromFraction(1, 64)` |
| `ConstructionHealthTarget` | raw health × work steps in `long`, unchecked | in 128 bits (the data know a health of 99,999,999; at 24 bits the product wrapped on a long construction) |

**Deliberately raw-value-dependent and unchanged:** the state hash (`HashStateWriter` mixes raw values), snapshot (`JsonState` writes raw values), replay and commands (raw coordinates), the event quantities (`SimEvent.Value`
carries damage and money as a raw value), the fingerprint of terrain, catalog and bone table (raw values as text or bytes), the time probe ("below eight significant bits" is a statement about raw values).
These places are the reason for the new format versions (§3). **Checked and independent of the number of bits:** `decimal` → `Fixed` on loading (`FromDecimal` rounds to the resolution; values from the data now keep
more places), terrain heights and map objects (`FromInt`/`FromDecimal`), the bone table (steps of 1/16 unit and 1/1024 rad are exact in both resolutions), the cell conversion of the navigation
(`Fixed` operations), `SageRandom.Range` (128-bit intermediate value), the mood offset (integers in frames), flight times (ratio of two raw values).

### 2.3 Outside the simulation: one conversion

`CnCNew.Presentation.FixedConvert` (`ToDouble`, `ToSingle`, `RawToSingle`, `FromDouble`, `FromSingle`, `FromSingleTruncated`) is the only place outside the simulation that uses the scale. 15 hand-written
conversions in 12 files now call it (`x.Raw / 65536f`, `value.Raw / (double)Fixed.ScaleRaw`, `(long)(x * 65536f)`, `Math.Round(value * ScaleRaw)` in `LiveMatch`, `LiveScenario`, `LiveMission`,
`FxExecutor`, `FxWorldView`, `MinimapImage`, `SimulationQueries`, `TurretTracker`, `Suspension`, `FrameProjector`, `SimTool`, `PresentationTool`); each yields the bits of the replaced expression (division and product
with a power of two are exact, every variant rounded the same number once). The tolerance of the `UnitConditionTracker` is 1/8192 instead of raw 8. Outside the simulation only these lines were touched.
Besides there are 21 places that go via `ToDecimal()` to `float`/`double`; they do not depend on the scale and stayed as they are.

### 2.4 Source rule (`Tests.FixedScaleSourceRules`)

Third lexical rule set beside `SourceRules` and `FeederSourceRules`, over `src`, `tools` and `tests`:

1. **The scale never as a number or shift**, neither 2¹⁶ nor 2²⁴: `65536`, `16777216`, `0x10000`, `0x1000000`, `<< 16`, `>> 16`, `<< 24`, `>> 24` (also with a suffix or underscores). 33 named exceptions
   for hits that are something else: colour channels and bytes of a word (12), file formats (9: WAV 24-bit sample, RefPack length, four CSF markers, three W3D fields), others (7: log₁₀ 2 in `SingleBits`, an
   option bit, a buffer size, test dice, singles around 2²⁴) and the 16-bit oracle of the trigonometric functions (5 lines).
2. **Outside the simulation no use of `Fixed.ScaleRaw`, `Fixed.FractionBits` or `FixedMath`**: 4 exceptions (the three lines of `FixedConvert`, one random value of the pathfinding benchmark).
3. **No raw value as a number** (`FromRaw(1024)`) in product code, not in the simulation either: no exception.

Like every rule there it is a tripwire (regular expressions over code lines); an exception that no longer hits anything makes the test fail. The self-test of the rule found its first hole while it was being written
(`65536f` with a suffix).

## 3. The switch (`d7fca91`)

One cause: `Fixed.FractionBits = 24`. No other change of behaviour in the commit.

| Format | old | new | Rejection of an old state |
|---|---|---|---|
| `WorldSnapshot.Version` | 9 | **10** | `Unsupported snapshot version 9 (expected 10).` |
| `ReplayFormat.CurrentVersion` | 11 | **12** | `Unsupported replay format version 11 (expected 12).` |
| `CommandTypes.Version` | 14 | **15** | `The replay uses command table version 14; this build implements 15.` |
| `MissionSnapshot.Version` | 2 | **3** | `Unsupported mission state version 2 (expected 3).` |
| `SaveGame.Version` | 2 (since Phase 21: rate in the envelope; in the branch of this phase still 1) | **3** (after the merge with Phase 21) | `Unsupported savegame version 2 (this game reads version 3).` |

An old state is rejected by its version and never read with the new scale; tests: `Tests.StatesOfSixteenFractionBitsAreRejected` (snapshot, replay container, command table), `Tests.Scripting`
(mission state), `CampaignTests` (saved game).

In the same commit: `baselines/reference-manifest.json` (all 50 runs new, afterwards cross-checked in the full set) and `baselines/input-manifest.json` (catalogs, terrain and bone table are hashed as raw values;
world fingerprint `49A55E310F780F60`). Test expectations that encoded raw values or old versions: version numbers in three tests; the angular velocity of 180° per tick is compared with `FixedTrig.Pi`;
the value that is lost entirely at 120 Hz is now 0.0004 per s² (the 0.1 of the `NapalmBombLocomotor`, which were lost at 16 bits, are 117 raw steps); the golden hash of the bone table (hashed as raw values,
the same poses); the bound of the suspension test (§9).

## 4. Reference values old and new

| Quantity | up to Phase 21 (16 bits, seed `5EED0005`) | `41c3bc0` (16 bits, seed `5EED0007`) | `d7fca91` (24 bits) |
|---|---|---|---|
| Reference match | FactionGLA at tick 14,490 | FactionGLA at tick 14,055 | **FactionGLA at tick 17,805** |
| – final hash / chain | `23B4B25DF62765D8` / `DE5849AF9F8493C4` | `AE9C688309DA37E4` / `FC6DFA759B1DCAF0` | **`D0CA5F4EE456D6CE` / `C5EE1090E1C24D7B`** |
| Stress test final hash / chain | `56882F763EABEF67` / `B8E663493F2C73D5` | unchanged | **`56882F763EABEF67`** (the empty final state, unchanged) / **`754FA4DCB9A41DF7`** |
| 16 AI runs decided | 12 | 12 | **12** (five with a different outcome, §7.2) |
| Formats | snapshot 9, replay 11, commands 14, mission 2, savegame 1 | unchanged | **snapshot 10, replay 12, commands 15, mission 3, savegame 2** |

The final hashes and chains of all 50 runs are in `baselines/reference-manifest.json`; the table in appendix B puts old and new side by side.

## 5. Finding: values whose square leaves the range (state of the switch; fixed in §5a)

The range shrinks by the factor 256 to ± 5.5 · 10¹¹. The root of the largest number is **741,455**: a larger value must not be squared, its square throws `OverflowException` (at 16 bits the
limit was at 11.9 million). Since the preparatory work `SimTool rate-audit <content>` lists every catalog value above this limit:

| Field | Values above 741,455 | largest | Is the value squared? |
|---|---:|---|---|
| `WeaponInfo.MinWeaponSpeed` | 360 | 999,999 (default "instant") | no (only comparisons and quotients) |
| `SpecialAbilityUpdateData.AbilityAbortRange` | 86 | 10,000,000 (default "never") | no: `SpecialAbilityStaging` asks `< HugeDistance` beforehand |
| `BodyData.MaxHealth` / `InitialHealth` | 62 each | 99,999,999 (`CargoTruckNuke`) | no; health × 100 = 10¹⁰ stays in the range (margin factor 55) |
| `WeaponInfo.WeaponSpeed` | 52 | 999,999 | no |
| **`WeaponInfo.AttackRange`** | **8** | 1,000,000 (`AircraftCarrierOrderLaunch`), 999,999 (`ScudStormWeapon` and its three Generals variants, `CINE_RangerAdvancedCombatRifle`, `CINE_GLAJarmenKellRifle`, `CINE_USAPathfinderSniperRifle`) | **yes**: every range check computes `reach * reach` (`SimulationWorld.WithinWeaponRange`, `Turrets.cs`, `Combat.cs`, `AirWeaponModules`, the scenarios) – about ten places |
| **`SpecialAbilityUpdateData.StartAbilityRange`** | **6** | 1,000,000 (the six bomb trucks: `GLAVehicleBombTruck` and Generals variants) | **yes**: `SpecialAbilityUpdate.WithinSpheres` computes `reach * reach` as soon as the ability has a target object |
| `WeaponInfo.PrimaryDamage` | 3 | 999,999 | no (damage × bonus) |

At 16 bits only the ten health values of 99,999,999 lay above the limit.

**What is measured:** no run of the manifest, no campaign run (audit and objective driver, 24 maps each), no test and none of the trial runs at 60 and 120 Hz threw an overflow (§7, §10, §11). **What
does not follow from that:** that these paths were not reachable. The disguise of the bomb truck with a vehicle as the target is an ordinary player command; no measured run gives it. At 16 bits
the same path computed (10¹² lay in the range), at 24 bits it throws. **This must be fixed before a release.** The brief demands that such an intermediate value be reported before it is worked around; therefore nothing
is worked around here.

**Proposal (not implemented):** at the places that square a range from the data in order to compare with a squared distance, form the square via a named function that returns the largest number for a value above
the limit (`Fixed.SquaredBound(reach)` or similar). A squared distance that is itself representable is never larger; the comparison then has the same result for every representable distance as
with the exact square, and at 16 bits no result changes. It is not a silent saturation: the function is named that way and stands only in comparisons. Affected are the range checks of the weapons (via
`RangeWith`/`MaxWeaponRange`) and `WithinSpheres`; the other about 30 places of the form `range * range` take values that are small in the data (`ScanRange`, `TriggerRange` …) – `rate-audit` records that. Every
result-neutral solution leaves the reference values measured here untouched (it concerns only paths that throw today). The alternative, a different resolution, would change them: at 20 fractional bits the limit would be
at 2.97 million (the ranges 10⁶ would fit, `AbilityAbortRange` 10⁷ still not), an acceleration of 1 unit/s² would have 291 instead of 4,660 raw steps at 60 Hz.

**Largest measured intermediate values** (throwaway build with a probe in every operator of `Fixed`, over all 50 runs of the manifest and the objective driver of all 24 campaign maps; the sentinels `MaxValue` as "no
best value yet" excluded):

| Operator | largest result | margin to the edge | where |
|---|---:|---:|---|
| Product | 1.0 · 10¹⁰ (100,000 × 100,000) | factor **55** | `SkirmishAi.UseTunnels`: the constant 100,000 as "everywhere", squared |
| Sum | 5.86 · 10⁷ | factor 9,400 | squared distances (`DealDamageCore`; in the skirmish `SkirmishAi.DistanceSquared` with 5.45 · 10⁷) – distances up to about 7,600 units |
| Difference | 5.0 · 10⁷ | factor 11,000 | money (`SimulationWorld.Execute`, 50,000,000 − 1,000) |
| Quotient | 1.2 · 10⁵ | factor 4.6 · 10⁶ | `ActiveBody.SetInitialHealth` |
| `Fixed * long` | 3.0 · 10⁵ | factor 1.8 · 10⁶ | script condition (3,000 × 100) |

Not captured by the probe: the raw-value computations outside the operators (vision, topple, construction – set to `checked` or 128 bits in §2.2) and paths that no run enters. `StructureToppleUpdate` holds angle
and angular velocity with 56 fractional bits; a quarter circle is 1.1 · 10¹⁷ there, margin to the edge of the `long` factor 81.

## 5a. Fix (addendum, 7 October 2026): comparisons via `SquaredReach`

The finding from §5 is fixed, result-neutrally: every comparison that could compute before gives the same answer; only paths that threw before compute now. No reference value has changed
(`reference-manifest check --set full` and `input-manifest check` identical, §5a.7). The catalog value stays what the data say – nothing is capped on loading, no `if (range > …)` stands at a call site.

### 5a.1 What the finding in §5 underestimated and what it overestimated

Searched systematically (all products of two `Fixed` values of the simulation, enumerated type-exactly with a throwaway tool on the syntax tree of the compiler: 857 products, of which 413 of a value with itself;
in addition `CnCNew.Game`, `CnCNew.Presentation` and the tools):

- **More places than the two named.** About 95 places squared a range, a radius or a limit for a comparison (now 91 lines with `SquaredReach` in 38 files). The range of a weapon runs via `RangeWith`/`MaxWeaponRange`
  not only into `WithinWeaponRange` but also into the target acquisition (`AcquireRadius` → `FindMoodTarget`), the attack intent of the turrets (`KeepsAttackIntent`), `AssaultTransportAIUpdate.DetectDeployShot` (weapons with `DamageType = DEPLOY`, like those of the
  carrier), `SlavedUpdate` (`MasterTarget`), the two drivers (`ReferenceMatchScenario`, `ObjectiveDriver`) and – **outside the simulation** – into `SimulationQueries.InRange`, the question
  of the mouse pointer "is the target in range of the selection" (`CommandModeController.Hover`, `StandingOrders`).
- **The path that a player really reaches was the mouse pointer:** a selected Scud Storm (range 999,999) and the pointer over an enemy – `InRange` threw. The simulation itself squares the
  range of the Scud Storm on no command path (`AutoChooseSources = PRIMARY NONE`: `ChooseWeapon` chooses the weapon for no command source before the range is checked; firing happens via
  `OCLSpecialPower`). The aircraft carrier (`AircraftCarrierOrderLaunch`, 1,000,000) and the three cinematic soldiers threw in the simulation already on the fire command, the soldiers also without a command as soon as an enemy stood beside them.
- **The disguise of the bomb truck is not reachable today.** For `SPECIAL_DISGUISE_AS_VEHICLE` there is no handler (`SpecialAbilityUpdate.Handles` knows only the capture abilities); the command is rejected with
  `SpecialPowerHandlerMissing` before `StartAbilityRange` is read. The statement in §5 that this is an ordinary player command that throws was true for the command, not for the throwing. The path itself
  (`IsWithinStartRange` → `WithinSpheres`) threw with an ability that has a handler (synthetic test), and is fixed. The six objects are five bomb trucks and `GC_Slth_GLAVehicleBattleBus`.
- **A product that §5 listed as harmless overflows:** `ActiveBody.AdjustMaxHealth` forms `Health * newMax / oldMax` for `PRESERVE_RATIO`. From a health of 741,455 (62 retail objects) the
  product leaves the range as soon as a bonus changes the maximum (veterancy level, battle plan, `MaxHealthUpgrade`). For the ten objects with 99,999,999 that already threw at 16 bits (10¹⁶), for values up to 11.9 million
  it is a regression of Phase 22.
- **Vision circles:** since the preparatory work of Phase 22 (§2.2) the circle test of the `VisionMap` throws for a radius above 46,340 units (before, it wrapped silently). `ShroudClearingRange` reaches up to 1,000 in the data; a script can however
  reveal with any radius.

### 5a.2 Design

`src/CnCNew.Simulation/Numerics/SquaredReach.cs`, a value type for "the square of a range, only for comparing":

| Call | Meaning |
|---|---|
| `SquaredReach.Of(reach)` | the square as `Fixed` forms it (magnitude², rounded to the nearest number, halves away from zero) – in 128 bits |
| `.Contains(d²)` | `d² <= reach²` |
| `.ContainsStrictly(d²)` | `d² < reach²` |
| `SquaredReach.OfSquare(q)` | a value that already is a square (best squared distance of a search, "no radius" = `MaxValue`) |
| `SquaredReach.ContainsStrictly(reach, d², margin)` | `d² + margin < reach²`, sum and square exact (the minimum range: `distSqr < minAttackRangeSqr - 0.5`) |
| `SquaredReach.Compare(a, b)` | sign of `a² - b²` for two lengths whose squares were compared |
| `Fixed.MultiplyDivide(a, b, c)` | `a * b / c` with both roundings of the two operators, the product in 128 bits |

**Why 128 bits and not "above the limit = always inside" at the call site:** the type stores the rounded square as a machine word, or – where it leaves the word – the largest number and a flag bit.
`Contains` is then a single comparison of two machine words (`d.Raw <= raw`): if the true square lies beyond the largest number, every representable squared distance is smaller, and the comparison with the
largest number says exactly that. Only `ContainsStrictly` needs the flag bit (for `d² = MaxValue`, which occurs as a marker "nothing found yet"). That is not a saturation of a result: the saturated word
never leaves the type, there is no way to read it as a number. The formation (`FixedMath.SquareSaturated`) is the same 64×64 multiplication with the same rounding as `FixedMath.Multiply`, without its exception:
the fast path costs what `reach * reach` cost.

**Proof of result neutrality.** (a) From the construction: `Multiply(r, r)` returns `(|r|² + 2²³) >> 24` if that lies below 2⁶³, otherwise it throws; `SquareSaturated` returns the same expression under the same
condition (`high >> 23 == 0`), otherwise `long.MaxValue`. For every `r` for which the old expression computed, the stored word is therefore the old value, and `Contains`/`ContainsStrictly` are the old comparisons
`<=`/`<` on the same two words. (b) Measured against an independent oracle (`Tests.SquaredReachGivesTheExactComparison`, `BigInteger`): 5,328,044 comparisons – every raw value in ±60,000 steps around the old
overflow limit (largest squarable raw value 12,439,554,047,901 = 741,455.2 units; one step above it the product throws and `IsBeyondRange` flips), the sentinels of the data and of the code with their
neighbours (999,999, 1,000,000, 10,000,000, 99,999,999, the edges of the range), 300,000 values of every order of magnitude; per range the distances around the exact square (±2 raw steps), 0, ±1, the edges,
random values. 3,885,182 of them additionally against `d <= reach * reach` and `d < reach * reach` where the product computes. In addition 400,000 comparisons with margin (against `BigInteger` and against the replaced expression
`d >= reach * reach - margin`) and 784,051 quotients of products (`MultiplyDivide` against the formula with both roundings, 485,249 of them also against `a * b / c`, 241,176 with a product that only 128 bits hold).
(c) On the whole program: the full manifest and the input manifest are identical.

**Searches with a running best value** (`var best = range * range; … if (distance > best) continue; best = distance;`) now separate limit and best value: `var limit = SquaredReach.Of(range)`, the best value applies only when
a candidate has been found. For every candidate the condition is the same as before (the old best value was never larger than the limit).

**Vision circles** continue to compute in machine words at 16 fractional bits (§2.2); `VisionMap.SquaredRadius` returns the largest number for a radius whose square leaves the word (above 46,340 units). Exact for
every cell that lies at most 32,768 units from the centre (then the sum of two squared differences fits); the differences stay `checked`.

### 5a.3 Audit: which field is squared where

Largest values from `SimTool rate-audit <content> --ranges` (new: the largest magnitude of each of the 251 fixed-point fields of the retail catalog; shortened with `--ranges-above N`). "Squared in" from the enumeration of the products.

| Field | largest retail value | squared in | Overflow possible at Q39.24? | now |
|---|---:|---|---|---|
| `WeaponInfo.AttackRange` (× range bonus: `RangeWith`, `MaxWeaponRange`) | 1,000,000 (8 weapons above the limit, on 11 objects) | `WithinWeaponRange`, `KeepsAttackIntent`, `AcquireRadius` → `FindMoodTarget`, `DetectDeployShot`, `MasterTarget`, both drivers, `SimulationQueries.InRange` (presentation) | **yes** | `SquaredReach` |
| `WeaponInfo.MinimumAttackRange` | 200 | `WithinWeaponRange` (`min² − 0.5`) | no (data) | `SquaredReach.ContainsStrictly` with margin |
| `WeaponInfo.PrimaryRadius` / `SecondaryRadius` (× radius bonus) | 300 / 200 | `DealDamageCore`, `AirWeaponModules.WithinSphere3D` | no (data) | `SquaredReach` |
| `WeaponInfo.RequestAssistRange` | 200 | `ProcessRequestAssistance` | no | `SquaredReach` |
| `SpecialAbilityUpdateData.StartAbilityRange` | 1,000,000 (6 objects) | `SpecialAbilityUpdate.WithinSpheres` | **yes** (today rejected before the check, §5a.1) | `SquaredReach` |
| `SpecialAbilityUpdateData.AbilityAbortRange` | 10,000,000 (= "never", 86 objects) | `WithinSpheres`, only if below 10,000,000 | yes for values between 741,455 and 10⁷ | `SquaredReach` |
| `BodyData.MaxHealth` / `InitialHealth` | 99,999,999 (62 objects above the limit) | never squared; `Health * newMax / oldMax` in `AdjustMaxHealth` | **yes** (product) | `Fixed.MultiplyDivide` |
| `WeaponInfo.WeaponSpeed` / `MinWeaponSpeed` | 999,999 ("instant") | not squared (comparisons, quotients, `ratio * (Speed − MinSpeed)`) | no in comparisons; see §5a.6 | – |
| `WeaponInfo.PrimaryDamage` | 999,999 | not squared (× bonus, × armor) | no | – |
| `ObjectTemplate.VisionRange` | 99,999 (`ChinaArtilleryCannon`) | `SightedObjects`, `EnemyInSight`, `AcquireRadius` (× mood factor), `StealthDetectorUpdate` (if `DetectionRange` is missing) | no (10¹⁰, margin factor 55) | `SquaredReach` |
| `ObjectTemplate.ShroudClearingRange` | 1,000 | `VisionMap` | no | `VisionMap.SquaredRadius` |
| `ObjectTemplate.GeometryMajorRadius` / `MinorRadius` / `GeometryHeight` | 1,117 / 846 / 460 | `BoundingCircleRadius`/`BoundingSphereRadius` (diagonal, under the root); as a sum of radii in collision, crushing, docking | no | diagonal: plain product (named exception); comparisons: `SquaredReach` |
| Search radii of the modules (`ScanRange`, `ScanDistance`, `DetectionRange`, `TriggerRange`, `Radius`, `Range`, `RubOffRadius`, `BlastRadius`, `OuterRadius`, `DamageRadius`, `LockDistance`, `DiveStartDistance`, `DeliveryDistance`, `CloseEnoughDist` …) | at most 3,000 (`DeliverPayloadData.DeliveryDistance`), otherwise at most 700 | one comparison each in the module | no | `SquaredReach` |
| Integer fields as a range (`SlavedUpdate`: `GuardMaxRange`, `AttackRange`, `DistToTargetToGrantRangeBonus`; `MustCatchUpRadius`) | not in the fixed-point audit | `SlaveModules` | – | `SquaredReach` |
| Radii from maps (script parameters: reveal, `RECRUIT_TEAM`, special power radius, vision range) | not in the catalog | `VisionMap`, `ScriptEngine.Powers`, `ScriptEngine.Extended` | depending on the map | `SquaredReach` / `SquaredRadius` |
| Constants of the AI and of the drivers (`DefenseRadius`, `EngageRadius`, `BoardingReach`, "everywhere" = 100,000) | 100,000 | `SkirmishAi` | no (10¹⁰) | `SquaredReach` |

Above the limit there are unchanged 639 values in 8 fields in the retail catalog (table in §5). No further field lies above 100,000.

### 5a.4 Tests

| Test | What | On the state before the fix (`e1687c8`) |
|---|---|---|
| `Tests.SquaredReachGivesTheExactComparison`, `Tests.MultiplyDivideEqualsTheTwoOperatorsWithoutTheirOverflow` | oracle (§5a.2) | – (the type does not exist there) |
| `Tests.WeaponsWithAnEverywhereRangeFire` | synthetic: weapons with `AttackRange` 999,999 and 1,000,000 fire on command and by themselves; a minimum range of 800,000; a damage radius of 1,000,000 | **throws** `OverflowException` in `WithinWeaponRange` (on submitting the fire command) |
| `Tests.AbilityWithAnEverywhereStartRangeBeginsWhereTheUnitStands` | synthetic: an ability with a handler, `StartAbilityRange` 1,000,000, `AbilityAbortRange` 2,000,000 and a target object 600 units away runs through its stages, the unit does not move | **throws** in `SpecialAbilityUpdate.WithinSpheres` |
| `Tests.HealthRatioOfTheLargestBodiesSurvivesANewMaximum` | a body with 99,999,999 gets a new maximum (`PRESERVE_RATIO`) | **throws** in `ActiveBody.AdjustMaxHealth` |
| `Tests.VisionCircleWithAHugeRadiusCoversTheMap` | radii 46,000, 46,341, 99,999, 1,000,000 reveal the map (fast path, reference path, `ExploreCircle`) | **throws** in `VisionMap.Trace` |
| `Tests.RealSentinelRanges` (`--content-dir`) | the eleven retail objects with one of the eight weapons: fire command at tank and soldier, 150 frames without a command; the six objects with `StartAbilityRange` 1,000,000: the ability on a vehicle | **7 steps throw:** carrier (fire command), `CINE_AmericaInfantryRanger`, `CINE_AmericanInfantryPathfinder`, `CINE_GLAInfantryJarmenKell` (each fire command and standing beside an enemy) |
| `SentinelRangeTests` (presentation; with `--content-dir` all eleven retail objects) | `SimulationQueries.InRange` for a range of 999,999 | **throws** (synthetically and at the first retail object) |
| `Tests.SquaredReachSourceRules` | source rule (§5a.5) | – |

On the old state the behaviour tests were put unchanged into a copy of the tree and executed; they use only the public surface of the world.

**Which of the 14 named retail cases really ran through their check:**

| Case | driven? |
|---|---|
| `AircraftCarrierOrderLaunch` (`AmericaAircraftCarrier`) | yes: fire command accepted, one shot each at tank and soldier; threw before |
| `CINE_RangerAdvancedCombatRifle` | yes: fires on command and by itself; threw before |
| `CINE_USAPathfinderSniperRifle` | yes: one shot (the command at the tank is rejected as an invalid target, the check runs before that); threw before |
| `CINE_GLAJarmenKellRifle` | yes: fire command and standing beside an enemy threw before and now run through; nothing is fired (the command is rejected with `NoWeapon`, `AutoChooseSources = NONE`) |
| `ScudStormWeapon`, `Chem_`, `Demo_`, `GC_Chem_ScudStormWeapon` (seven buildings) | in the simulation **not**: no command path squares their range (§5a.1), no exception before as after. **Yes** via `SimulationQueries.InRange` (presentation test with retail data, all seven buildings); that threw before |
| `StartAbilityRange` 1,000,000 (five bomb trucks, `GC_Slth_GLAVehicleBattleBus`) | **no:** the command is rejected with `SpecialPowerHandlerMissing`, the disguise is not simulated. The test records exactly this rejection and checks the stages as soon as the command is accepted |

`SimTool range-audit <content>` (new) as a search run beyond that: each of the 526 instantiable retail objects with a weapon stands 250 units in front of a tank and in front of a soldier, gets a
fire command and runs 150 frames: 1,052 runs, **0 exceptions**; 68 objects fired (447 shots). The run has no terrain and gives no special powers; that 458 objects do not fire in it is not a finding
and not a coverage of their firing paths.

### 5a.5 What protects against relapses

- **Source rule `Tests.SquaredReachSourceRules`** (fourth lexical rule set): in `src/CnCNew.Simulation` no value is multiplied with itself (`x * x`, `(a + b) * (a + b)`), unless its last
  identifier ends in x, y or z (a coordinate component: `dx * dx`, `v.X * v.X`). 21 named exceptions with a reason: lengths that are at most a distance (3), geometry of the templates (3),
  speeds per tick and fractions between 0 and 1 (10), integers (5). The rule reads names, not types – a square as `a * b` gets through; it is a tripwire like the others.
  Outside the simulation it would be too noisy (there `float` is used for computing, `radius * radius` is ordinary there); the type-exact enumeration found a single `Fixed` square there (`SimulationQueries.InRange`).
- **The tests from §5a.4**, among them two with retail data that record the list of the weapons and objects above the limit: a new value above the limit in these fields makes them fail.
- **`SimTool rate-audit --ranges`** and **`SimTool range-audit`** for every new data source.

### 5a.6 Only reported, not changed

- **Squared distances themselves** (`dx * dx + dy * dy`, 110 times `dx`/`dy`) have stayed plain products: two positions that lie more than 741,455 apart on one axis or more than about 524,000 units apart on two axes
  throw. Maps are a few thousand units in size. **But: a command with target coordinates is not checked against the map on submission** (`SetMoveTargetCommand` and others carry
  raw coordinates) – a move target at a distance of 600,000 units makes the movement of the unit throw (at 16 bits from 8.4 million). For multiplayer that is a question of command validation (Phases 27/28),
  not a comparison that a helper could answer neutrally.
- **Stored and hashed** is exactly one square: `DeliverPayloadAIUpdate._previousDistance2` (the squared distance to the target in the previous tick, in the snapshot as `previousDistance2`). It is a squared distance
  of two positions (previous item), not a square of a catalog value; unchanged.
- **`DeliverPayloadAIUpdate.MinTurnRadius`** returns 999,999 if the locomotor has no turn rate, and `ConsiderNewApproach` thereby sets a move target at a distance of 2.2 million units – after that the
  movement throws (previous item). Whether a retail delivery aircraft without a turn rate exists is not checked; no measured run enters the path. The target is stored state and was not touched.
- **Projectiles with `WeaponSpeed` 999,999** (52 weapons): not squared; a `MissileAIUpdate` with `UseWeaponSpeed` would get this speed per tick. `range-audit` fired what it got to fire, without
  an exception; the coverage of these weapons is thereby not verified.
- **`VisionMap`:** a circle whose centre lies further than 32,768 units from a cell of the map still throws (checked differences) instead of wrapping silently as before Phase 22.

### 5a.7 Verification

Commits: `b274df5` (fix), `7e93401` (tests and source rule), `2bd08d2` (tools). Measured at the source state of `b274df5` (the two following commits change no product code of the simulation):

| Check | Result |
|---|---|
| `SimTool input-manifest check` | identical |
| `SimTool reference-manifest check --set quick` | 9 runs identical (twice: before and after the last comment changes) |
| `SimTool reference-manifest check --set full` | **50 runs identical** |
| Simulation tests (Debug) without and with `--content-dir` | passed (414 and 429 tests respectively; the reference match in the test still FactionGLA at tick 17,805) |
| Simulation tests `-c Release -- --long` | passed |
| `CnCNew.Game.Tests` (Release, `--content-dir`) | passed |
| `CnCNew.Presentation.Tests` (without and with `--content-dir`) | passed |
| Clean build `dotnet build CnCNew.slnx` (`bin`/`obj` of `CnCNew.Client.Stride` deleted) | 0 warnings, 0 errors |
| `SimTool range-audit` | 1,052 runs, 0 exceptions |

Not repeated: the determinism matrix (§8.1), `CampaignTool audit`/`drive`, the scene audits, `tools/CnCNew.UiRenderTool` – the full manifest (27 mission runs included) is identical, there were no further runs for that.

**Performance, back to back** (`SimTool perf <content> --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 12000`, old state `e1687c8` and new state alternately, three times each, without own runs
beside them; another check run was working on the same machine). Mean per tick in microseconds without the first 300 ticks; in all six runs final hash `F78F071AF5EC8A6E`, chain `D9975439375F5DF7`:

| | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| before: whole tick | 2,088.3 | 2,115.6 | 2,070.3 |
| after: whole tick | 2,089.7 | 2,080.9 | 2,071.7 |
| before: phase "Updates" (target acquisition, weapon range, movement) | 807.5 | 832.2 | 805.0 |
| after: phase "Updates" | 821.7 | 814.4 | 811.2 |

The differences lie within the scatter of the runs of the same program (before 2,070 to 2,116); an increase is not measurable. The absolute numbers are higher than in §8.2 because the machine was not quiet.

## 6. Finding: the reference match and the three-way match of the tests are metastable

### 6.1 What happened

At 24 bits the reference match no longer ended with the previous seed (no winner in 45,000 ticks; the full budget of 216,000 ticks takes over an hour per run), and the test
"Phase 5: reference match scenario" (three sides of the synthetic fixture) found no winner in 200,000 ticks where it ended at tick 13,080 at 16 bits.

### 6.2 Cause, with evidence

Both scenarios are wars of attrition between equally strong sides with ongoing production; they end when a next-target decision between two units almost equally far away tips
the balance.

- **Three-way match (no random draw in the fixture; world and driver seed demonstrably have no influence):** up to tick 3,000 the courses are statistically the same (643 shots, 121 deaths in both
  resolutions), the positions drift apart steadily: at 16 bits an accelerating unit holds the speed 0.50011 where the exact value is 0.5 and at 24 bits 0.50000 stands (the quantisation of the
  acceleration per tick – the reason for this phase); at tick 2,145 the same unit lies 0.7 units apart in the two resolutions. **First deviating decision: tick 2,160**, unit 100 pursues at 16 bits the target
  88, at 24 bits it stays with the target 87. How sensitive the end is, is shown by the arena radius (16 bits / 24 bits, victory tick): 300: 2,895 / 2,895; 350: 10,980 / 13,230; 380: 49,035 / 45,720; 390: 5,640 / 11,325;
  398: **10,200 / 10,200**; 399: 30,870 / 4,050; 400: 13,080 / no winner in 200,000; 401: 12,060 / 12,435; 402: 40,275 / 31,470; 410: 19,860 / 76,605; 420: 16,425 / 29,865; 450: 10,380 / 15,825;
  500: 40,020 / 10,335. A step of the radius by one unit shifts the end by an order of magnitude, in both resolutions. Since `065067a` the test uses the radius 398, whose match takes the same course
  in both resolutions.
- **Reference match (retail data, 13 factions on a circle):** the **first deviating decision falls in tick 15**, at the first command: unit 41 pursues at 16 bits the target 73, at 24 bits the target 71.
  The bases stand on a circle; the two targets stand at the two neighbouring bases and are practically equally far away from the base of the unit (from the target positions of the commands: 700.2 and 700.3 units). Which is "nearer" is decided by the last places of the positions. From then on it is a
  different match. Whether it ends depends on the production rolls – survey over 36 driver seeds (`5EED0005` + 0…35, 45,000 ticks, `SimTool reference-match --driver-seed … --record-only`):

  | | 16 bits | 24 bits |
  |---|---|---|
  | decided | 32 of 36 | 31 of 36 |
  | victory tick smallest / median / largest | 11,955 / 15,158 / 24,495 | 11,910 / 16,995 / 29,745 |
  | winner | GLA 29, AmericaSuperWeaponGeneral 2, America 1 | GLA 24, America 4, GLAToxinGeneral 1, GLAStealthGeneral 1, AmericaSuperWeaponGeneral 1 |
  | not decided (seed + …) | 9, 17, 30, 34 | 0, 1, 6, 7, 31 |

  An undecided match has the same anatomy in both resolutions (one examined each, 16 bits +9 and 24 bits +6): a US faction is left and holds several hundred units that cannot attack what remains of the
  others (GLA holes, Chinooks). That is a weakness of the scenario, not of the switch. The distributions cannot be distinguished with 36 seeds (rank-sum test over
  the duration, two-sided p = 0.10); a shift of the order of ten percent is thereby **not ruled out** and would, as far as it exists, be a consequence of more exact rates (at 16 bits two
  catalog conversions were off by 3.9 % and 4.9 %, §11).
- With the previous driver seed and twelve other world seeds the matches last longer throughout at 24 bits (9 of 12 decided, median 25,635; at 16 bits 12 of 12, median 15,300): the
  production sequence of this one seed is a different one at 24 bits than at 16 (the driver draws per production building and attempt; as soon as a building becomes free one tick earlier, the sequence shifts)
  and a slow one. The twelve runs are therefore not independent samples.

### 6.3 What was made of it

`41c3bc0` (commit of its own, one cause, at 16 bits): `ReferenceMatchOptions.Standard` uses the driver seed **`5EED0007`**. Its match ends in both resolutions with the same winner (FactionGLA at
tick 14,055 and 17,805 respectively). In the manifest only the run `reference-match` changed with that; the 49 others were recomputed and are byte-identical. The reference match remains a run whose end can tip again at the next
intended change of results (Phase 23, 60 Hz); `--driver-seed` and `--record-only` are there for that.

## 7. Comparison of the result quantities (16 bits against 24 bits)

Comparison state old: `41c3bc0` (16 bits, new seed); new: `d7fca91`. Collected with `SimTool reference-manifest results <content> --out <json> --commands <dir> --set full` on both states; the complete
tables are in appendix A. Quantities are in units, not raw.

### 7.1 Summary

| Runs | Result |
|---|---|
| Stress test (216,000 ticks, synthetic) | command stream identical (914,394 commands), every event count identical (238,273 deaths), final state empty as before |
| 27 mission runs (3,000 ticks) | outcome, object count, executed scripts and actions **identical in all 27**; shots identical in 26, `mission_MD_GLA05` 568 → 564 (deaths 162 → 158) |
| Skirmish sweep (64 maps, 3,000 ticks) | 64 of 64 with the same outcome (all at the tick budget) |
| three 8-player runs (9,000 ticks) and the short one | all at the tick budget as before; object counts 173 → 165, 194 → 226, 179 → 170, 178 → 177 |
| 16 AI runs (up to 45,000 ticks) | 12 decided as before; **11 with the same outcome, 5 with a different one** (§7.2) |
| Reference match | the same winner, tick 14,055 → 17,805 (§6) |
| Campaign: objective driver (24 maps, 60,000 ticks) | victory 24/24 and defeat 24/24 as before; **on all 24 maps the same end script in the same tick**, the same number of interventions (appendix C) |
| Campaign: audit (24 maps, 900 ticks) | 24/24 deterministic, 24/24 identical after save/load, end probes victory 13/24, defeat 15/24, 0 hits on missing types – map by map as before |
| `CampaignTool reachability` | victory clean 24/24, defeat clean 24/24; the output is byte-identical to that at 16 bits (static analysis, does not depend on the resolution) |

### 7.2 The 16 AI runs: every deviation and its cause

In **every** one of the 16 runs (and of the four 8-player runs) the command stream of the AI is identical over the whole build-up – 149 to 467 commands (building sites with position, production, supply, upgrades), 2,890 to
5,670 ticks. The conversion therefore carries economy, construction and production unchanged. The first deviating command is in every run a command of the battle; after that it is a different battle. First deviating
command per run (`results --commands`, positions in whole units, one unit of difference does not count):

| Run | Outcome 16 bits | Outcome 24 bits | first deviating command (no., tick) | in what (16 bits → 24 bits) |
|---|---|---|---|---|
| AlpineAssault America–China | America @16,965 | America @16,665 | 250, tick 3,613 | at 24 bits an attack-move (tick 3,613) comes before the special power of the opposing side (tick 3,614) |
| AlpineAssault China–GLA | China @23,370 | China @10,710 | 149, tick 2,938 | attack-move of the same unit, target (1308, 1697) → (1311, 1698) |
| AlpineAssault ChinaTank–AirForce | **stalemate** | **AirForce @14,610** | 154, tick 3,553 | attack-move of the same unit, target (1314, 1775) → (1334, 1770) |
| AlpineAssault GLA–America | America @8,895 | America @7,815 | 317, tick 2,969 | attack-move of the same unit, target (1129, 884) → (1122, 886) |
| ArmoredFury America–China | **America @19,095** | **stalemate** | 216, tick 2,893 | attack-move, unit 65 → 84 |
| ArmoredFury China–GLA | **stalemate** | **China @42,840** | 226, tick 2,999 | attack-move of the same unit, target (1897, 2786) → (1888, 2784) |
| ArmoredFury ChinaTank–AirForce | stalemate | stalemate | 220, tick 3,299 | attack-move, unit 124 → 125 |
| ArmoredFury GLA–America | America @29,865 | America @28,800 | 386, tick 3,794 | the same enter command, tick 3,809 → 3,794 |
| BitterWinter America–China | **America @33,750** | **stalemate** | 311, tick 4,258 | attack-move, unit 129 → 130 |
| BitterWinter China–GLA | China @17,220 | China @7,950 | 293, tick 3,644 | the attack-move of tick 3,644 is absent at 24 bits |
| BitterWinter ChinaTank–AirForce | stalemate | stalemate | 357, tick 4,679 | attack-move, unit 128 → 127 |
| BitterWinter GLA–America | America @8,895 | America @9,075 | 426, tick 3,583 | attack-move of the same unit, target (998, 1533) → (1010, 1544) |
| DeathValley America–China | America @9,330 | America @9,390 | 326, tick 4,078 | at 24 bits an attack-move (tick 4,078) comes before a special ability (tick 4,079) |
| DeathValley China–GLA | China @8,565 | China @8,580 | 382, tick 3,659 | attack-move, unit 166 → 165 |
| DeathValley ChinaTank–AirForce | **ChinaTank @31,770** | **AirForce @10,635** | 466, tick 5,594 | attack-move, tick 5,669 → 5,594, unit 432 → 388 |
| DeathValley GLA–America | America @8,175 | America @8,475 | 467, tick 3,343 | attack-move, unit 44 → 41 |

The deviations that go beyond "end tick nearby" – five different outcomes (two stalemates become victories, two victories become stalemates, **one winner changes**: DeathValley ChinaTank–AirForce) and two victories at
half the tick (China–GLA on Alpine Assault and Bitter Winter) – all have the same named cause: **chaotic amplification of a rounding.** The evidence is the table: up to the named tick the commands are the same,
the first difference is an attack command whose target (the position of an enemy object at this tick) lies a few units differently or whose unit is a different one (the units leave the
factories in a different order or offset by one tick). Such differences arise because amounts per tick are more exact at 24 bits (measured in the synthetic case: 0.6 units of position difference after
2,100 ticks). The direction is not uniform: of the nine runs that end with the same winner in both resolutions, five end earlier and four later, the event rates per 1,000 ticks (shots, hits, deaths, productions) fluctuate in both directions. The number of
decided runs stays 12 of 16. An error of the switch would have shown in the build-up, in the missions or in the stress test; there everything is the same. **Not checked** is whether the new outcomes are "better"
(balance); the AI table in `docs/SIMULATION-CORE.md` §19/§36 is thereby superseded.

## 8. Determinism and performance

### 8.1 Matrix of this machine with the new manifest

Every row: `input-manifest check` and `reference-manifest check --set full` against the checked-in files of `d7fca91`.

| Row | Setting | Inputs | Computation | Result |
|---|---|---|---|---|
| Release, run 1 | – | identical | 50/50 (full) | **identical** |
| Release, run 2 | the same call once more | identical | 50/50 (full) | **identical** |
| Debug build | `dotnet build -c Debug tools/CnCNew.SimTool` | identical | 9/9 (**quick**) | **identical** |
| Tiered compilation off | `DOTNET_TieredCompilation=0` | identical | 50/50 (full) | **identical** |
| Tiered PGO off | `DOTNET_TieredPGO=0` | identical | 50/50 (full) | **identical** |
| ReadyToRun off | `DOTNET_ReadyToRun=0` | identical | 50/50 (full) | **identical** |
| Hardware intrinsics off | `DOTNET_EnableHWIntrinsic=0` | identical | 50/50 (full) | **identical** |
| AVX2 off | `DOTNET_EnableAVX2=0` | identical | 50/50 (full) | **identical** |
| Server GC | `DOTNET_gcServer=1` | identical | 50/50 (full) | **identical** |
| Culture `de-DE` | `CNCNEW_TEST_CULTURE=de-DE` | identical | 50/50 (full) | **identical** |
| Culture `tr-TR` | `CNCNEW_TEST_CULTURE=tr-TR` | identical | 50/50 (full) | **identical** |
| Culture `ar-SA` | `CNCNEW_TEST_CULTURE=ar-SA` | identical | 50/50 (full) | **identical** |
| Linux on the same machine | WSL2 (Ubuntu, kernel 6.6.87), `dotnet publish -r linux-x64 --self-contained`, content via `/mnt/c` | identical | 50/50 (full) | **identical** |

Thirteen rows, no deviation in any field. The new manifest carries no false repetition flag (every run: run 2 identical, continuation from the snapshot identical; every mission additionally identical after
save/load to the uninterrupted run). Run times of the full set: 439 s alone with six workers, 585 to 1,586 s with four workers beside other own runs, 2,251 s under WSL.

Open as in `docs/DETERMINISM.md`: a second physical machine with a different CPU, a different .NET version.

### 8.2 Performance, back to back

Absolute numbers of other days are not comparable on this machine; the comparison is therefore made in the same time window, alternately, twice each, without own runs beside them (a second agent was working on the same
machine – the measurement is quiet as far as I could control it, not beyond that). `SimTool perf … --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 12000` and
`perf-mission … --map "Maps/MD_GLA05/MD_GLA05.map" --ticks 3000`; mean per tick in microseconds, with observer.

**Preparatory work against the state of Phase 20** (both 16 bits, the same final hashes `ADC3E2661AF15B6D` and `826CCD82C8536DC2`, quiet window, two runs each):

| Run | Phase 20 (`4c45764`) | Preparatory work (`a0e7b23`) |
|---|---|---|
| 8 players, Twilight Flame, 12,000 ticks: whole tick | 1,670 / 1,649 | 1,666 / 1,647 |
| – phase "Vision" (vision circles) | 147 / 140 | 146 / 135 |
| `MD_GLA05`, 3,000 ticks | 4,583 / 4,569 | 4,547 / 4,601 |

With the 128-bit squares from `756e978` the vision phase had needed 187 / 171 against 132 / 135 (whole tick 1,742 / 1,642 against 1,563 / 1,587); hence the test in machine words (`a0e7b23`).

**Switch against the state of Phase 20** (16 bits against 24 bits):

| Run | same work? | Phase 20, 16 bits | switch, 24 bits |
|---|---|---|---|
| Stress test, 216,000 ticks, µs per tick in the first run (`SimTool stress`) | **yes** (command stream and every event count identical) | 57.6 / 57.7 / 58.3 | **57.1 / 56.7 / 56.7** |
| Stress test shortened, 40,000 ticks, four runs | yes | 44.0 – 51.4 (median 44.9) | 44.0 – 44.8 (median 44.1) |
| `MD_USA05`, 3,000 ticks, six runs | yes (all counters identical) | 2,582 – 3,899, mean 3,233 | 2,820 – 3,894, mean 3,330 |
| `MD_GLA05`, 3,000 ticks, five runs | almost (564 instead of 568 shots) | 4,537 – 5,865, mean 4,957 | 4,261 – 6,068, mean 5,106 |
| 8 players, Twilight Flame, 12,000 ticks, five runs | no (from tick 2,773 a different battle) | 1,597 – 1,944, mean 1,765 | 1,612 – 1,940, mean 1,772 |

The cleanest comparison is the stress test: the same work, three alternating runs in a quiet window, 24 bits one to two percent **faster**. In the missions and in the skirmish the runs
of the same program scatter by ± 15 % (a second agent was computing at times), the means are at + 0.4 % (skirmish) and + 3 % (missions) – smaller than the scatter. **Verified is: no measurable increase of the
computing costs; not verified is a statement accurate to a few percent for missions.** The vision phase costs at 24 bits what it cost at 16 bits (138 – 176 µs against 137 – 176 µs in the same window).
The prerequisite for that were the routines derived for the number of bits (§2.1): with the routines of Phase 19 every squared distance above 181 units would have taken the 128-bit software paths at 24 bits.

**Snapshot size:** over all snapshots of the manifest that lie in the same tick old and new (missions, 8-player runs, stress test), 118,737,497 → 119,710,407 bytes, **+0.82 %**; stress test at tick 108,000
11,862,560 → 12,095,586 bytes (+2.0 %), `mission_MD_GLA05` 9,038,371 → 9,071,782 bytes (+0.4 %). The numbers get longer, the share of the fixed-point values in a snapshot is small.

## 9. Golden hashes and tests outside the simulation

- **Changed, as a consequence of the one cause** (in the switch commit): the golden hash of the bone table of the retail airfields (`DeterminismTests.BoneTableGolden`, `93D5819B…F1DA47` → `EA1915EB…DAC858`; the
  table is hashed as raw values, the poses are the same numbers). The bound of the suspension test (`ObjectTests.ChassisFollowsMotion`): the roll overshoots its limit by one step, three steps of
  4 · (1/30) are exactly 0.4 = limit + 0.1; since the acceleration per tick is 1/30 to seven places, the peak hits this value to the last bit of a `float` (0.4000001), the bound now allows that.
- **Unchanged:** all other tests of the presentation and of the game layer (UI golden hashes, terrain, FX, input, HUD, shell, campaign), the three scene audits (`scripts/audit-scenarios.ps1`: `capture-building`,
  `tank-duel`, `jet-strafe` – all checks passed).
- No human has looked at a presentation of this state. Positions are finer; that nothing is visibly different is not verified.

## 10. Verification at the end

Everything at the state `d7fca91` (the documentation came afterwards and changes no code):

| Check | Result |
|---|---|
| `SimTool input-manifest check`, `reference-manifest check --set full` | identical, twice in a row, plus the matrix (§8.1) |
| Simulation tests (Debug) without and with `--content-dir` | passed (407 tests) |
| Simulation tests `-c Release -- --long` (216,000 ticks) | passed |
| `SimTool stress` | final hash `56882F763EABEF67`, chain `754FA4DCB9A41DF7`, run 2/3 and continuation identical |
| `CnCNew.Game.Tests` (Release, `--content-dir`) | passed (the first full run failed on the golden hash of the bone table, §9; after the correction in the switch commit full run passed) |
| `CnCNew.Presentation.Tests` | passed (after the bound from §9) |
| Archives, Content, Data (without and with content), IO, Images, Formats, Import, Ui, W3D (with content), Audio (with content) | all passed |
| `IniTool compat-check … baselines/compatibility.json` | 751 entries, matches (the definition layer does not know `Fixed`; the definition snapshot is not touched by the switch) |
| `SimTool audit-modules` / `audit-coverage` | 183 of 205 modules, 16,558 of 19,553 uses; 2,115 of 2,115 objects created |
| `scripts/audit-scenarios.ps1` | all three scenes passed |
| `CampaignTool audit --ticks 900`, `drive --ticks 60000 --explain` per map (24), `reachability` | §7.1; no exception, no time limit |
| Clean build `dotnet build CnCNew.slnx` (`bin`/`obj` of `CnCNew.Client.Stride` deleted), `dotnet build tools/CnCNew.UiRenderTool` | 0 warnings, 0 errors each |
| `scripts/audit-vulnerable.ps1` | no known vulnerabilities |

## 11. Trial run at 60 and 120 Hz

`SimTool rate-audit <content> --tick-rate N` at the state of the preparatory work (16 bits, confirms the numbers of Phase 20) and at the state of the switch (24 bits):

| Counter | 60 Hz, 16 bits | 60 Hz, 24 bits | 120 Hz, 16 bits | 120 Hz, 24 bits |
|---|---|---|---|---|
| conversions during the catalog build that round to zero | 0 | **0** | 1 (`NapalmBombLocomotor`, 0.1/s²) | **0** |
| conversions with over 1 % / over 10 % error | 5 / 0 | **0 / 0** | 39 / 1 | **0 / 0** |
| worst error of a conversion | 9.863 % | **0.007 %** | 100 % | **0.422 %** (the same 0.1/s²: 117 raw steps) |
| catalog values: ≠ 0 at 30 Hz, 0 at the rate | 0 | 0 | 0 | 0 |
| catalog values with over 1 % up to 10 % error against the scaled 30 Hz value | 1 | **0** | 41 in 5 fields | **0** |
| catalog values with over 10 % | 0 | 0 | 0 | 0 |
| catalog values below eight significant bits | 46 in 5 fields | **0** | 321 in 7 fields | **0** |

At 30 Hz itself: 16 bits had two conversions above 1 % (4.912 % with `DegreesPerSecondToRadiansPerTick`, 3.870 % with `PerSecondSquaredToPerTickSquared`), 24 bits has none (worst case 0.007 %). The goal
of the phase – zero cases at 60 Hz – is reached; at 120 Hz likewise.

Runtime counters (`--time-probe`, first run of every command, 24 bits):

Twelve runs (reference match, two AI runs on Alpine Assault, `MD_USA05`, `MD_GLA05`, `GC_ChemGeneral`, each at 60 and 120 Hz); the table is in `docs/TICKRATE-MIGRATION.md` §11.5.

| Counter | Sum of the calls over the twelve runs | Hits |
|---|---:|---:|
| `RunTimeRoundedToZero` (amount per tick becomes zero) | 579,635 | **0** |
| `MinimumOneFrame` (lower limit "one frame" takes effect) | 21,570 | 48 (the same places as in Phase 20: duration 0 becomes one frame) |
| `OffGrid` (duration off the grid) | 454,979 | **0** |

Every run is deterministic (second fresh run and continuation the same; with the reference match only recorded), none throws. Target and result: **at 60 Hz zero cases of rounding to zero** – during the
catalog build and at runtime; no case remains to be named. Not counted remains what modules form per tick without the time service.

## 12. What is not verified

- The overflow paths from §5 are fixed (§5a). What stands there under "only reported" remains open: distances between positions that lie further apart than about 524,000 units
  (a command with arbitrary target coordinates is not checked), and two placeholder values in the code.
- A second physical machine, a different CPU, a different .NET version (as in `docs/DETERMINISM.md`).
- Whether the new AI outcomes and the longer reference match shift the balance; the survey over 36 seeds does not rule out a shift of the duration by about ten percent.
- Every presentation: no human has seen or played this state.
- The performance is compared in the same time window, not on a demonstrably quiet machine.

## 13. Pitfalls

- **The simulation is not `checked`.** Only `Fixed` throws. Whoever computes on `.Raw` writes `checked(…)` or computes in 128 bits; a square of two raw values at 24 bits no longer fits into a `long` already for 180 units.
- **A range, a radius or a limit is never squared with `*`**, but compared via `SquaredReach` (§5a); the source rule `Tests.SquaredReachSourceRules` records that. The square of a
  value above 741,455 does not fit. `SimTool rate-audit <content> --ranges` lists the largest value of every field; sentinels in the code ("everywhere" = 100,000) have, squared, only the factor 55 of margin.
- **`Fixed.ToDecimal()` is no longer exact above 65,536 units** (24 binary places need 24 decimal places, a `decimal` has 28 to 29 significant ones); the rounding lies far below half a raw step,
  `FromDecimal(ToDecimal(x))` still gives `x`.
- **Scenarios with equally strong sides are not a yardstick for "only rounding".** Their end is a tipping decision; the comparison needs a survey over seeds or quantities that lie before the tipping
  (command stream of the build-up, event rates).
- Comparison of two resolutions or rates: `reference-manifest results --commands <dir>` on both states, then the first deviating line per run – not the hashes.
- A trial build with a different number of bits: change the constant, build into a directory of its own (`dotnet build -o`), set it back. Such a build does not read old replays and snapshots (versions).

## Appendix A: result quantities of all runs of the manifest, 16 bits (`41c3bc0`) against 24 bits (`d7fca91`)

`SimTool reference-manifest results`. "Outcome": winner@victory tick, `stalemate` = no winner in the tick budget, `none` = mission without an end script in 3,000 ticks, or stress test. Players in the order of their ids
(without the neutral player); money in whole units. "Shots (script fire)" is the counter of the mission session, the event count `WeaponFired` is in the first table.

| Run | Outcome old | Outcome new | End tick old | new | Objects old | new | Shots/productions/constructions/deaths old | new |
|---|---|---|---:|---:|---:|---:|---|---|
| AlpineAssault_America-China | America@16965 | America@16665 | 17266 | 16966 | 75 | 85 | 3197/135/16/92 | 5711/139/15/85 |
| AlpineAssault_China-GLA | China@23370 | China@10710 | 23671 | 11011 | 65 | 56 | 5869/204/15/180 | 3668/77/13/60 |
| AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral | stalemate | AmericaAirForceGeneral@14610 | 45000 | 14911 | 38 | 93 | 7392/271/21/267 | 5245/143/17/97 |
| AlpineAssault_GLA-America | America@8895 | America@7815 | 9196 | 8116 | 49 | 39 | 4715/68/10/49 | 2218/64/9/53 |
| ArmoredFury_America-China | America@19095 | stalemate | 19396 | 45000 | 101 | 87 | 6014/214/19/181 | 10742/535/21/512 |
| ArmoredFury_China-GLA | stalemate | China@42840 | 45000 | 43141 | 71 | 90 | 9754/369/20/390 | 10685/314/19/320 |
| ArmoredFury_ChinaTankGeneral-AmericaAirForceGeneral | stalemate | stalemate | 45000 | 45000 | 113 | 74 | 9971/409/27/411 | 7602/267/25/540 |
| ArmoredFury_GLA-America | America@29865 | America@28800 | 30166 | 29101 | 131 | 116 | 13083/388/22/366 | 15100/348/20/324 |
| BitterWinter_America-China | America@33750 | stalemate | 34051 | 45000 | 98 | 44 | 10154/354/26/311 | 9236/410/24/416 |
| BitterWinter_China-GLA | China@17220 | China@7950 | 17521 | 8251 | 60 | 56 | 2221/99/14/86 | 1454/66/10/49 |
| BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral | stalemate | stalemate | 45000 | 45000 | 48 | 84 | 4650/222/18/228 | 7392/280/28/261 |
| BitterWinter_GLA-America | America@8895 | America@9075 | 9196 | 9376 | 71 | 71 | 3086/81/11/49 | 3336/81/11/50 |
| DeathValley_America-China | America@9330 | America@9390 | 9631 | 9691 | 61 | 62 | 2616/52/12/38 | 2767/53/12/38 |
| DeathValley_China-GLA | China@8565 | China@8580 | 8866 | 8881 | 61 | 63 | 1607/69/9/54 | 1746/69/9/53 |
| DeathValley_ChinaTankGeneral-AmericaAirForceGeneral | ChinaTankGeneral@31770 | AmericaAirForceGeneral@10635 | 32071 | 10936 | 42 | 54 | 9446/157/17/209 | 2990/52/12/52 |
| DeathValley_GLA-America | America@8175 | America@8475 | 8476 | 8776 | 60 | 60 | 3005/69/9/53 | 3104/69/10/53 |
| ai8_FortressAvalanche | stalemate | stalemate | 9000 | 9000 | 173 | 165 | 10822/204/45/393 | 9945/201/44/334 |
| ai8_IronDragon | stalemate | stalemate | 9000 | 9000 | 194 | 226 | 9035/238/57/455 | 8945/250/52/564 |
| ai8_TwilightFlame | stalemate | stalemate | 9000 | 9000 | 179 | 170 | 7943/213/44/189 | 9202/208/43/259 |
| ai8_TwilightFlame_short | stalemate | stalemate | 3000 | 3000 | 178 | 177 | 15/88/32/0 | 11/88/32/0 |
| mission_GC_AirGeneral_AmericaLaserGeneral | none | none | 3000 | 3000 | 1049 | 1049 | 0/1/0/0 | 0/1/0/0 |
| mission_GC_AirGeneral_ChinaInfantryGeneral | none | none | 3000 | 3000 | 1049 | 1049 | 0/1/0/0 | 0/1/0/0 |
| mission_GC_AirGeneral_GLAStealthGeneral | none | none | 3000 | 3000 | 1049 | 1049 | 0/1/0/0 | 0/1/0/0 |
| mission_GC_ChemGeneral_AmericaAirForceGeneral | none | none | 3000 | 3000 | 787 | 787 | 0/0/0/3 | 0/0/0/3 |
| mission_GC_ChemGeneral_ChinaTankGeneral | none | none | 3000 | 3000 | 787 | 787 | 0/0/0/3 | 0/0/0/3 |
| mission_GC_ChinaBoss_AmericaAirForceGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_AmericaLaserGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_AmericaSuperWeaponGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_ChinaInfantryGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_ChinaNukeGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_ChinaTankGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_GLADemolitionGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_GLAStealthGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_ChinaBoss_GLAToxinGeneral | none | none | 3000 | 3000 | 1494 | 1494 | 0/0/0/8 | 0/0/0/8 |
| mission_GC_LaserGeneral_ChinaNukeGeneral | none | none | 3000 | 3000 | 2109 | 2109 | 0/23/0/0 | 0/23/0/0 |
| mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral | none | none | 3000 | 3000 | 689 | 689 | 0/0/0/0 | 0/0/0/0 |
| mission_GC_TankGeneral_AmericaSuperWeaponGeneral | none | none | 3000 | 3000 | 1624 | 1624 | 0/0/0/0 | 0/0/0/0 |
| mission_GC_TankGeneral_GLAToxinGeneral | none | none | 3000 | 3000 | 1624 | 1624 | 0/0/0/0 | 0/0/0/0 |
| mission_MD_CHI01 | none | none | 3000 | 3000 | 1133 | 1133 | 0/0/0/15 | 0/0/0/15 |
| mission_MD_CHI05 | none | none | 3000 | 3000 | 2109 | 2109 | 2926/0/0/12 | 2926/0/0/12 |
| mission_MD_GLA01 | none | none | 3000 | 3000 | 1093 | 1093 | 1231/0/0/40 | 1231/0/0/40 |
| mission_MD_GLA03 | none | none | 3000 | 3000 | 1312 | 1312 | 0/0/0/4 | 0/0/0/4 |
| mission_MD_GLA05 | none | none | 3000 | 3000 | 2249 | 2249 | 568/0/0/162 | 564/0/0/158 |
| mission_MD_USA01 | none | none | 3000 | 3000 | 1986 | 1986 | 127/0/0/70 | 127/0/0/70 |
| mission_MD_USA02 | none | none | 3000 | 3000 | 1780 | 1780 | 39/0/0/35 | 39/0/0/35 |
| mission_MD_USA05 | none | none | 3000 | 3000 | 1869 | 1869 | 0/4/0/24 | 0/4/0/24 |
| mission_Training01 | none | none | 3000 | 3000 | 1901 | 1901 | 181/0/0/14 | 181/0/0/14 |
| reference-match | GLA@14055 | GLA@17805 | 14356 | 18106 | 207 | 158 | 15854/872/0/2109 | 19314/1143/0/2099 |
| skirmish-sweep | 64 maps | same outcome 64, other 0 |  |  |  |  |  |  |
| stress | none | none | 216000 | 216000 | 0 | 0 | 0/0/0/238273 | 0/0/0/238273 |

| Run | Objects per player old | new | Money per player old | new |
|---|---|---|---|---|
| AlpineAssault_America-China | 69/0 | 79/0 | 625/200 | 1350/200 |
| AlpineAssault_China-GLA | 53/6 | 45/5 | 600/375 | 1500/300 |
| AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral | 10/23 | 0/87 | 150/25 | 1300/1925 |
| AlpineAssault_GLA-America | 3/40 | 3/30 | 0/1250 | 0/1975 |
| ArmoredFury_America-China | 62/0 | 46/12 | 775/100 | 75/100 |
| ArmoredFury_China-GLA | 10/25 | 47/8 | 700/37 | 950/19 |
| ArmoredFury_ChinaTankGeneral-AmericaAirForceGeneral | 21/62 | 28/14 | 225/100 | 0/150 |
| ArmoredFury_GLA-America | 9/88 | 8/73 | 825/750 | 75/600 |
| BitterWinter_America-China | 94/0 | 26/14 | 750/0 | 100/200 |
| BitterWinter_China-GLA | 41/3 | 37/3 | 300/50 | 1700/50 |
| BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral | 35/5 | 24/52 | 150/350 | 200/350 |
| BitterWinter_GLA-America | 3/52 | 3/52 | 0/1350 | 0/1950 |
| DeathValley_America-China | 39/0 | 40/0 | 650/500 | 650/500 |
| DeathValley_China-GLA | 36/3 | 38/3 | 1000/50 | 1000/50 |
| DeathValley_ChinaTankGeneral-AmericaAirForceGeneral | 18/2 | 0/32 | 400/11400 | 1300/1250 |
| DeathValley_GLA-America | 3/35 | 3/35 | 0/775 | 0/1975 |
| ai8_FortressAvalanche | 26/26/20/27/9/3/34/13 | 23/32/19/20/9/3/31/13 | 625/750/150/200/0/1400/50/50 | 100/1200/150/500/1050/1400/50/50 |
| ai8_IronDragon | 28/21/31/24/30/4/36/0 | 35/25/41/26/40/0/39/0 | 600/200/0/50/725/0/0/600 | 1275/0/175/75/0/200/0/900 |
| ai8_TwilightFlame | 19/17/47/30/8/3/20/23 | 20/23/41/23/8/3/21/19 | 575/675/75/200/150/3800/1200/1350 | 575/1075/75/2150/150/3500/1200/300 |
| ai8_TwilightFlame_short | 22/22/23/21/35/15/14/14 | 22/22/22/21/35/15/14/14 | 750/1200/50/175/150/200/50/0 | 750/1200/50/175/150/200/50/0 |
| mission_GC_AirGeneral_AmericaLaserGeneral | 36/2/306 | 36/2/306 | 22000/13000/0 | 22000/13000/0 |
| mission_GC_AirGeneral_ChinaInfantryGeneral | 36/2/306 | 36/2/306 | 22000/13000/0 | 22000/13000/0 |
| mission_GC_AirGeneral_GLAStealthGeneral | 36/2/306 | 36/2/306 | 22000/13000/0 | 22000/13000/0 |
| mission_GC_ChemGeneral_AmericaAirForceGeneral | 425/134/2 | 425/134/2 | 6400/499180/13000 | 6400/499180/13000 |
| mission_GC_ChemGeneral_ChinaTankGeneral | 425/134/2 | 425/134/2 | 6400/499180/13000 | 6400/499180/13000 |
| mission_GC_ChinaBoss_AmericaAirForceGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_AmericaLaserGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_AmericaSuperWeaponGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_ChinaInfantryGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_ChinaNukeGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_ChinaTankGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_GLADemolitionGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_GLAStealthGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_ChinaBoss_GLAToxinGeneral | 644/3/285 | 644/3/285 | 0/13000/203900 | 0/13000/203900 |
| mission_GC_LaserGeneral_ChinaNukeGeneral | 1455/96/11/11/11/11/11/11/11/11/23/11/2 | 1455/96/11/11/11/11/11/11/11/11/23/11/2 | 6400/12200/20000/20000/20000/20000/20000/20000/20000/20000/20000/20000/13000 | 6400/12200/20000/20000/20000/20000/20000/20000/20000/20000/20000/20000/13000 |
| mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral | 243/138/2/32 | 243/138/2/32 | 0/116100/13000/3200 | 0/116100/13000/3200 |
| mission_GC_TankGeneral_AmericaSuperWeaponGeneral | 447/2/94 | 447/2/94 | 0/13000/34200 | 0/13000/34200 |
| mission_GC_TankGeneral_GLAToxinGeneral | 447/2/94 | 447/2/94 | 0/13000/34200 | 0/13000/34200 |
| mission_MD_CHI01 | 736/65/13/12/0/0/0/0/18 | 736/65/13/12/0/0/0/0/18 | 3200/101900/18000/0/0/0/0/0/0 | 3200/101900/18000/0/0/0/0/0/0 |
| mission_MD_CHI05 | 1741/29/183/103/0/5/10 | 1741/29/183/103/0/5/10 | 4800/15400/49994465/49977120/0/0/0 | 4800/15400/49994465/49977120/0/0/0 |
| mission_MD_GLA01 | 42/37/464/6/15/0/0/3/11/1 | 42/37/464/6/15/0/0/3/11/1 | 0/0/0/600/0/0/0/0/0/0 | 0/0/0/600/0/0/0/0/0/0 |
| mission_MD_GLA03 | 191/23/863/6/3/0/2/1/7/19 | 191/23/863/6/3/0/2/1/7/19 | 3000/5000/0/30000/0/0/0/525/0/980 | 3000/5000/0/30000/0/0/0/525/0/980 |
| mission_MD_GLA05 | 1488/0/154/20/20/20/3/0/43/1/0/2 | 1488/0/154/20/20/20/3/0/43/1/0/2 | 0/4300/419800/3500/0/49400/0/0/0/0/74/0 | 0/4300/419800/3500/0/49400/0/0/0/0/74/0 |
| mission_MD_USA01 | 995/25/259/5/8/12/43/5/1/1/0/3/0/4 | 995/25/259/5/8/12/43/5/1/1/0/3/0/4 | 1600/0/980/0/0/2000/0/0/0/0/0/0/0/0 | 1600/0/980/0/0/2000/0/0/0/0/0/0/0/0 |
| mission_MD_USA02 | 3/1246/44/31/64/30/0/78/1/0/0/6/6/10 | 3/1246/44/31/64/30/0/78/1/0/0/6/6/10 | 0/3200/800/0/0/102630/0/0/0/0/0/1600/0/0 | 0/3200/800/0/0/102630/0/0/0/0/0/1600/0/0 |
| mission_MD_USA05 | 1092/6/88/30/156/42/0/0/0/4 | 1092/6/88/30/156/42/0/0/0/4 | 8000/18000/193670/0/225/199470/0/0/0/0 | 8000/18000/193670/0/225/199470/0/0/0/0 |
| mission_Training01 | 0/168/213/5/33/0/0 | 0/168/213/5/33/0/0 | 0/0/0/0/0/0/0 | 0/0/0/0/0/0/0 |
| reference-match | 0/2/0/0/0/0/0/0/0/186/2/2/2 | 0/0/0/4/0/0/0/0/0/135/2/2/2 | 1800/0/69300/1600/43500/45000/44400/45000/16500/509/1725/600/1800 | 2000/400/87300/0/34500/32400/34500/23100/13500/468/5475/3825/2925 |
| stress | 0/0 | 0/0 | 0/0 | 0/0 |

| Mission | Scripts old | new | Actions old | new | Shots (script fire) old | new | End script old | new |
|---|---:|---:|---:|---:|---:|---:|---|---|
| mission_GC_AirGeneral_AmericaLaserGeneral | 58 | 58 | 588 | 588 | 0 | 0 | - | - |
| mission_GC_AirGeneral_ChinaInfantryGeneral | 58 | 58 | 588 | 588 | 0 | 0 | - | - |
| mission_GC_AirGeneral_GLAStealthGeneral | 58 | 58 | 588 | 588 | 0 | 0 | - | - |
| mission_GC_ChemGeneral_AmericaAirForceGeneral | 83 | 83 | 783 | 783 | 0 | 0 | - | - |
| mission_GC_ChemGeneral_ChinaTankGeneral | 83 | 83 | 783 | 783 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_AmericaAirForceGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_AmericaLaserGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_AmericaSuperWeaponGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_ChinaInfantryGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_ChinaNukeGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_ChinaTankGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_GLADemolitionGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_GLAStealthGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_ChinaBoss_GLAToxinGeneral | 12092 | 12092 | 12720 | 12720 | 0 | 0 | - | - |
| mission_GC_LaserGeneral_ChinaNukeGeneral | 122 | 122 | 581 | 581 | 0 | 0 | - | - |
| mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral | 97 | 97 | 593 | 593 | 0 | 0 | - | - |
| mission_GC_TankGeneral_AmericaSuperWeaponGeneral | 3035 | 3035 | 550 | 550 | 0 | 0 | - | - |
| mission_GC_TankGeneral_GLAToxinGeneral | 3035 | 3035 | 550 | 550 | 0 | 0 | - | - |
| mission_MD_CHI01 | 65 | 65 | 3357 | 3357 | 0 | 0 | - | - |
| mission_MD_CHI05 | 151 | 151 | 487 | 487 | 2926 | 2926 | - | - |
| mission_MD_GLA01 | 133 | 133 | 275 | 275 | 1231 | 1231 | - | - |
| mission_MD_GLA03 | 101 | 101 | 485 | 485 | 0 | 0 | - | - |
| mission_MD_GLA05 | 102 | 102 | 384 | 384 | 568 | 564 | - | - |
| mission_MD_USA01 | 107 | 107 | 225 | 225 | 127 | 127 | - | - |
| mission_MD_USA02 | 79 | 79 | 196 | 196 | 39 | 39 | - | - |
| mission_MD_USA05 | 109 | 109 | 396 | 396 | 0 | 0 | - | - |
| mission_Training01 | 46 | 46 | 48 | 48 | 181 | 181 | - | - |

| Map (sweep) | End tick old | new | Outcome old | new | Shots old | new | Objects old | new |
|---|---:|---:|---|---|---:|---:|---:|---:|
| skirmish-sweep Maps/Alpine Assault/Alpine Assault.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 49 | 49 |
| skirmish-sweep Maps/BarrenBadlands/BarrenBadlands.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 50 | 50 |
| skirmish-sweep Maps/Bear Town Beatdown/Bear Town Beatdown.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 62 | 62 |
| skirmish-sweep Maps/Bitter Winter/Bitter Winter.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 59 | 59 |
| skirmish-sweep Maps/Bombardment Beach/Bombardment Beach.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 63 | 63 |
| skirmish-sweep Maps/Cairo Commandos/Cairo Commandos.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Dark Mountain/Dark Mountain.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 55 | 55 |
| skirmish-sweep Maps/Dark Night/Dark Night.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 62 | 62 |
| skirmish-sweep Maps/Death Valley/Death Valley.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 63 | 63 |
| skirmish-sweep Maps/Defcon6/Defcon6.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 85 | 85 |
| skirmish-sweep Maps/Desert Fury/Desert Fury.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Destruction Station/Destruction Station.map | 3000 | 3000 | stalemate | stalemate | 6 | 9 | 73 | 75 |
| skirmish-sweep Maps/DogsOfWar/DogsOfWar.map | 3000 | 3000 | stalemate | stalemate | 90 | 89 | 54 | 54 |
| skirmish-sweep Maps/Dust Devil/Dust Devil.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Eastern Everglades/Eastern Everglades.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 61 | 61 |
| skirmish-sweep Maps/El Scorcho/El Scorcho.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 77 | 77 |
| skirmish-sweep Maps/Fallen Empire/Fallen Empire.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 50 | 50 |
| skirmish-sweep Maps/Final Crusade/Final Crusade.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Flash Effect/Flash Effect.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Flash Fire/Flash Fire.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 54 | 54 |
| skirmish-sweep Maps/FloodedPlains/FloodedPlains.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/ForgottenForestZH/ForgottenForestZH.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 59 | 59 |
| skirmish-sweep Maps/Fortress Avalanche/Fortress Avalanche.map | 3000 | 3000 | stalemate | stalemate | 17 | 16 | 64 | 64 |
| skirmish-sweep Maps/Free Fire Zone/Free Fire Zone.map | 3000 | 3000 | stalemate | stalemate | 191 | 185 | 64 | 62 |
| skirmish-sweep Maps/Golden Oasis/Golden Oasis.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 54 | 54 |
| skirmish-sweep Maps/Green Pastures/Green Pastures.map | 3000 | 3000 | stalemate | stalemate | 156 | 155 | 61 | 61 |
| skirmish-sweep Maps/Heartland Shield/Heartland Shield.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 48 | 48 |
| skirmish-sweep Maps/Homeland Alliance/Homeland Alliance.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 52 | 52 |
| skirmish-sweep Maps/Hostile Dawn/Hostile Dawn.map | 3000 | 3000 | stalemate | stalemate | 159 | 159 | 55 | 55 |
| skirmish-sweep Maps/Iron Dragon/Iron Dragon.map | 3000 | 3000 | stalemate | stalemate | 50 | 49 | 66 | 66 |
| skirmish-sweep Maps/Killing Fields/Killing Fields.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 62 | 62 |
| skirmish-sweep Maps/Leipzig Lowlands/Leipzig Lowlands.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Lights Out/Lights Out.map | 3000 | 3000 | stalemate | stalemate | 46 | 46 | 56 | 56 |
| skirmish-sweep Maps/Lone Eagle/Lone Eagle.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 60 | 60 |
| skirmish-sweep Maps/Manic Aggression/Manic Aggression.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Mountain Guns/Mountain Guns.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 54 | 54 |
| skirmish-sweep Maps/MountainFox/MountainFox.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 50 | 50 |
| skirmish-sweep Maps/NorthAmerica/NorthAmerica.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 44 | 44 |
| skirmish-sweep Maps/Overland/Overland.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 63 | 63 |
| skirmish-sweep Maps/Red Rock/Red Rock.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 53 | 53 |
| skirmish-sweep Maps/Rocky Rampage/Rocky Rampage.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Rogue Agent/Rogue Agent.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/Sand Serpent/Sand Serpent.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 50 | 50 |
| skirmish-sweep Maps/Seaside Mutiny/Seaside Mutiny.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 53 | 53 |
| skirmish-sweep Maps/Silent River/Silent River.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/TheFrontline/TheFrontline.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 56 | 56 |
| skirmish-sweep Maps/Tournament City/Tournament City.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 74 | 74 |
| skirmish-sweep Maps/Tournament Continent/Tournament Continent.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 67 | 67 |
| skirmish-sweep Maps/Tournament Desert/Tournament Desert.map | 3000 | 3000 | stalemate | stalemate | 5 | 5 | 61 | 61 |
| skirmish-sweep Maps/Tournament Island/Tournament Island.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 60 | 60 |
| skirmish-sweep Maps/Tournament Lake/Tournament Lake.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 71 | 71 |
| skirmish-sweep Maps/Tournament Plains/Tournament Plains.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep Maps/Tournament Tundra/Tournament Tundra.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 56 | 56 |
| skirmish-sweep Maps/Tournament Urban/Tournament Urban.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 47 | 47 |
| skirmish-sweep Maps/TournamentA/TournamentA.map | 3000 | 3000 | stalemate | stalemate | 156 | 158 | 84 | 84 |
| skirmish-sweep Maps/TournamentB/TournamentB.map | 3000 | 3000 | stalemate | stalemate | 325 | 343 | 82 | 80 |
| skirmish-sweep Maps/Twilight Flame/Twilight Flame.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 55 | 55 |
| skirmish-sweep Maps/Victory Valley/Victory Valley.map | 3000 | 3000 | stalemate | stalemate | 99 | 96 | 56 | 57 |
| skirmish-sweep Maps/Wasteland Warlords/Wasteland Warlords.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 45 | 45 |
| skirmish-sweep Maps/Whiteout/Whiteout.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 56 | 56 |
| skirmish-sweep Maps/Winding River/Winding River.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 52 | 52 |
| skirmish-sweep Maps/Winter Wolf/Winter Wolf.map | 3000 | 3000 | stalemate | stalemate | 0 | 0 | 46 | 46 |
| skirmish-sweep maps/Armored Fury/Armored Fury.map | 3000 | 3000 | stalemate | stalemate | 170 | 159 | 86 | 86 |
| skirmish-sweep maps/Scorched Earth/Scorched Earth.map | 3000 | 3000 | stalemate | stalemate | 80 | 64 | 51 | 53 |

## Appendix B: final hash, chain and snapshot size per run, 16 bits (`41c3bc0`) against 24 bits (`d7fca91`)

From `baselines/reference-manifest.json` of the two states. The snapshot size is comparable only where the snapshot lies in the same tick (missions, 8-player runs, stress test); with the AI runs and the
reference match it lies in the middle of a run of a different length.

| Run | Set | Final hash 16 bits | Final hash 24 bits | Chain 16 bits | Chain 24 bits | Snapshot bytes 16 bits | 24 bits |
|---|---|---|---|---|---|---:|---:|
| `AlpineAssault_America-China` | full | `BA10AC1EB0EB8AC9` | `8FA1F3E86560055A` | `F669459C2FA6E39C` | `C07E2CACB84DFDA0` | 380668 | 386926 |
| `AlpineAssault_China-GLA` | full | `0BD4FE5EEB2C37E7` | `BAABE02976F3F436` | `BF9939E5A3B45736` | `37763867679EF7A9` | 380984 | 380496 |
| `AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral` | full | `D99B60905846A6B1` | `472B3E1147C53711` | `80EDA8449C86CEE6` | `E1E50480088C008B` | 410180 | 383971 |
| `AlpineAssault_GLA-America` | quick | `23A93C58C775A691` | `73CB68FC2A23A303` | `B29CF881F6B1CC59` | `E9D96D818C6F18DC` | 390287 | 391625 |
| `ArmoredFury_America-China` | full | `0E0E07558397DB62` | `11A1BC4E365AE06F` | `E41C4EF3D1DFBEF4` | `BFF01CBE3E0889B2` | 704547 | 716172 |
| `ArmoredFury_China-GLA` | full | `7196F712342A3BDA` | `360DBEB6A4A5A5F3` | `FF370FDA722BFA3B` | `98705009BFAB3C9A` | 692867 | 693950 |
| `ArmoredFury_ChinaTankGeneral-AmericaAirForceGeneral` | full | `7AC8B5574DE21417` | `104B58733F51FE60` | `68F33BE69B8EDF6D` | `2268AEEB66E1B7FF` | 714914 | 692984 |
| `ArmoredFury_GLA-America` | full | `58B066C7284C21B2` | `97CDBA96BD00F063` | `64E8453DEE5297E4` | `D6440448F9630ED4` | 727580 | 770737 |
| `BitterWinter_America-China` | full | `38380B920A732955` | `51BD14F53E358F43` | `5FED11D99834B1B6` | `D4992C5FE99A68D3` | 564691 | 497002 |
| `BitterWinter_China-GLA` | full | `26CD35AC8AA660F5` | `14DF904BEC8D311D` | `2B86E2038EDFAAB7` | `7B7F706B3E35D58C` | 436151 | 496018 |
| `BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral` | full | `9D040D6ACB8E269D` | `34E6C959E12715E6` | `24F626B9E8F8CCA2` | `389BDF8604E76EB4` | 429385 | 537814 |
| `BitterWinter_GLA-America` | quick | `AB725263AEF8E3B7` | `092F2CF08F0E03A5` | `CDFCDF66730FF07B` | `8FC07F7A5B002530` | 481693 | 477416 |
| `DeathValley_America-China` | quick | `E98A532C90F89642` | `7F0A365871E47399` | `AC321A0CFA9C00E1` | `87C0A807F1C1276E` | 1023942 | 1028691 |
| `DeathValley_China-GLA` | quick | `9F0F8D7D28E8654D` | `B22B302FDE59E05E` | `90CDEBE18E7C6B01` | `C973A67C747213DD` | 1053704 | 1056258 |
| `DeathValley_ChinaTankGeneral-AmericaAirForceGeneral` | full | `FDAD90E9E139AFE9` | `6C73FC4D1714033F` | `6E2EE5C9DA09EFD1` | `DB7A55C079DFE16B` | 997490 | 1033478 |
| `DeathValley_GLA-America` | full | `3F33A4FEDEBA85C1` | `652C6C59B131BD01` | `3B00774A84E69278` | `F2C075335AB011FD` | 1055608 | 1055215 |
| `ai8_FortressAvalanche` | full | `90A0A2020CD9D943` | `2ADF0836978AAB3E` | `4FE3E6F70CF223B5` | `B93FBEB518AA06F3` | 2632596 | 2644361 |
| `ai8_IronDragon` | full | `C894A034D7911797` | `35F3E682D2F22B01` | `E816C1A74BAF7FE7` | `97B543DFCA1B259F` | 2393895 | 2400731 |
| `ai8_TwilightFlame` | full | `538D104E2A8FE934` | `E26CC739356ED921` | `BDB8F56ADD63170C` | `493EDF98BFBEE062` | 3379330 | 3386275 |
| `ai8_TwilightFlame_short` | quick | `518B62C47106C2AB` | `6231F002D205EE8F` | `4FAF848E76ECFEBC` | `EF6322ABDB48B522` | 2954218 | 2956611 |
| `mission_GC_AirGeneral_AmericaLaserGeneral` | full | `A7A5EFC754196678` | `9073D81C99DAB53A` | `8D0EEB0393C52136` | `E77FEF52AEA951B8` | 1630191 | 1645167 |
| `mission_GC_AirGeneral_ChinaInfantryGeneral` | full | `983315D08BABBBD1` | `0E5984962FEB4224` | `27DCB81F7419F0C4` | `E936B06AAB2B4B72` | 1630187 | 1645165 |
| `mission_GC_AirGeneral_GLAStealthGeneral` | full | `F95DDC63B877815C` | `A61D03B983CE7F25` | `048B1906AAA3BEB2` | `0A5A54628495E5A6` | 1630972 | 1645951 |
| `mission_GC_ChemGeneral_AmericaAirForceGeneral` | full | `E66023A1A7713080` | `3BDA3E3B47773BC7` | `495DAE0E030EEE3B` | `7A38E61AC62F5C28` | 1491769 | 1503399 |
| `mission_GC_ChemGeneral_ChinaTankGeneral` | full | `FDB19B02BC40D94A` | `BC014352B0BEBFBC` | `84709824D111DAAC` | `1DE3D0FB97A73AFF` | 1491244 | 1502874 |
| `mission_GC_ChinaBoss_AmericaAirForceGeneral` | full | `1DE21B847AB3F8BC` | `062B1C2BAE34D4DA` | `5A1F9964030EEEFE` | `0CEBE28BD2E0C38E` | 2887235 | 2910523 |
| `mission_GC_ChinaBoss_AmericaLaserGeneral` | full | `E788E126D7F981B2` | `D670E49EEC0CDD21` | `0FB25AE6821A211C` | `F01D5B632AD87F99` | 2887061 | 2910349 |
| `mission_GC_ChinaBoss_AmericaSuperWeaponGeneral` | full | `F1D2D7CBE8DEA529` | `31B0CFDA74B43EF5` | `4D1000E1F11DD6CB` | `8ABF8E3348CA946C` | 2887244 | 2910532 |
| `mission_GC_ChinaBoss_ChinaInfantryGeneral` | full | `87C0C8262EC13734` | `72B050F101ECE333` | `1CB2C6566F167085` | `11602E34420E2C6A` | 2876796 | 2900085 |
| `mission_GC_ChinaBoss_ChinaNukeGeneral` | full | `28AF6CD9ABDDB40A` | `957AF3FD726C96FA` | `EDAB910A2ED5DE69` | `3D32038BACA67AE1` | 2876457 | 2899746 |
| `mission_GC_ChinaBoss_ChinaTankGeneral` | full | `52FE4778A34A594C` | `F10C5125CCCEC477` | `9A23CADEAC6A1676` | `E17DA5585A01C4C1` | 2876618 | 2899907 |
| `mission_GC_ChinaBoss_GLADemolitionGeneral` | full | `20CF8D24D6249EB7` | `C58DC738480A588D` | `C78B57065FD8A868` | `60C8230A34A0A1CC` | 2881820 | 2905112 |
| `mission_GC_ChinaBoss_GLAStealthGeneral` | full | `D4D2228048E89A5D` | `BE851DDCADE9A6D6` | `4EBCAF824A441758` | `2EE1CC7E02AA7D9B` | 2881299 | 2904591 |
| `mission_GC_ChinaBoss_GLAToxinGeneral` | full | `EFA6C01C342601DE` | `8A4BBA469F426E72` | `234CF97434610B57` | `B1A6E3A154885DEF` | 2881427 | 2904719 |
| `mission_GC_LaserGeneral_ChinaNukeGeneral` | full | `231523828930AD5B` | `DA6E37150843479A` | `05F16683E5E71578` | `80C0A73E7767C341` | 4495609 | 4530853 |
| `mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral` | full | `5E436B3BD943DFC2` | `CAF9F1C8FA87CB05` | `ADCCB4A1B93CBAD7` | `97EE826F82D4DC34` | 1519548 | 1530153 |
| `mission_GC_TankGeneral_AmericaSuperWeaponGeneral` | full | `33D616C9E3B835D8` | `064718BF1C6C0FD0` | `5A5B8107630E51F7` | `469B8AA67790DCD9` | 2656652 | 2679307 |
| `mission_GC_TankGeneral_GLAToxinGeneral` | full | `BB6D04A6D5FD4759` | `C6B0DBCC757AFFDB` | `68169DCF5C83C7BD` | `46759DD417DE7123` | 2657356 | 2680014 |
| `mission_MD_CHI01` | full | `8E5774AE10BE1225` | `3F64C8F8D64B81C6` | `6BA18B9F50AE1B8E` | `656362D9C4763058` | 3514104 | 3533078 |
| `mission_MD_CHI05` | full | `19954A666CCCF947` | `9709FBD1429C8206` | `CCC309C2F84C1A87` | `A1F70E2AC2AC4639` | 5266371 | 5300450 |
| `mission_MD_GLA01` | full | `D1E3DB52348F9898` | `960B2D1660A1279E` | `5E3469F37CC49737` | `E764DA1050962851` | 4429967 | 4445902 |
| `mission_MD_GLA03` | full | `E79236DDB44B3E30` | `2BB0C71748DA9B5F` | `369AFAA71BB959AE` | `3B189E12ECA1853E` | 4090830 | 4111757 |
| `mission_MD_GLA05` | quick | `826CCD82C8536DC2` | `5A7711E275ECB644` | `10330D7D179054FD` | `D79C856A73783150` | 9038371 | 9071782 |
| `mission_MD_USA01` | full | `8D26113C9DA64703` | `8CFD326B5B0D5D84` | `945589AB7750A0CB` | `283468E78538EDE8` | 7799358 | 7840194 |
| `mission_MD_USA02` | full | `3AFE4707A243C3C7` | `E3FB0B6FE97E8B5E` | `A60D461ED547E16F` | `2993A73F066BEDDE` | 4986767 | 5018032 |
| `mission_MD_USA05` | quick | `1CB28E6AA923BDEF` | `F21A211382F17CD7` | `0816C3438CB7F689` | `628D7208A23B10AB` | 6549194 | 6580158 |
| `mission_Training01` | full | `160EB89088698B16` | `F59CB7888AECB13C` | `315CAEABA92DD3C2` | `C370CEAEF084D019` | 3556152 | 3586245 |
| `reference-match` | quick | `AE9C688309DA37E4` | `D0CA5F4EE456D6CE` | `FC6DFA759B1DCAF0` | `C5EE1090E1C24D7B` | 438099 | 357825 |
| `skirmish-sweep` | full | `DB5B0EA0F386D5D7…` (SHA-256 over 64 maps) | `CBB37B33A4F1ABD8…` | – | – | – | – |
| `stress` | quick | `56882F763EABEF67` | `56882F763EABEF67` | `B8E663493F2C73D5` | `754FA4DCB9A41DF7` | 11862560 | 12095586 |

## Appendix C: objective driver per campaign map, 16 bits against 24 bits

`CampaignTool drive <content> --map "/NAME.map" --ticks 60000 --explain`, one process per map. In parentheses the number of interventions of the driver.

| Map | Victory old (script @tick, interventions) | Victory new | Defeat old | Defeat new |
|---|---|---|---|---|
| `Training01` | Player Wins @420 (1) | same | Player Loses @2655 (2) | same |
| `GC_ChemGeneral` | END_Final_USA @270 (1) | same | Lose_END_Final_USA @150 (1) | same |
| `GC_NukeGeneral` | END_Final_USA @240 (1) | same | Lose_END_Final_USA @150 (1) | same |
| `GC_SuperWeaponsGeneral` | END_Final_USA @240 (1) | same | Lose_END_Final_USA @150 (1) | same |
| `GC_TankGeneral` | END_Final_USA @240 (1) | same | Lose_END_Final_USA @150 (1) | same |
| `GC_Stealth` | END_Final_USA @240 (2) | same | Lose_END_Final_USA @150 (1) | same |
| `GC_LaserGeneral` | END_Final_USA @240 (1) | same | Lose_END_Final_USA @150 (1) | same |
| `GC_ChinaBoss` | End the mission @360 (5) | same | Lose_END_Final_USA @2431 (2) | same |
| `GC_AirGeneral` | END_Final_GLA @270 (1) | same | Lose_END_Final_GLA @150 (1) | same |
| `MD_USA01` | END_Final_USA @785 (1) | same | Lose_END_Final_USA @211 (1) | same |
| `MD_USA02` | END_Final_ @2718 (4) | same | Lose_END_Final_ @12361 (30) | same |
| `MD_USA03` | CINE_AVVictory @1509 (3) | same | Lose_END_Final_ @241 (1) | same |
| `MD_USA04` | END_Final_USA @303 (9) | same | Lose_END_Final_USA @617 (20) | same |
| `MD_USA05` | END_Fade 04 @1443 (4) | same | Lose_END_Final_ @1280 (6) | same |
| `MD_GLA01` | Cheer1Done @526 (3) | same | Lose_END_Final_ @241 (1) | same |
| `MD_GLA02` | END_Final_ @240 (1) | same | Lose_END_Final_ @572 (5) | same |
| `MD_GLA03` | CINE_ENDVictory @4033 (12) | same | Lose_END_Final_ @242 (1) | same |
| `MD_GLA04` | CINE_END @1022 (1) | same | Lose_END_Final_ @241 (2) | same |
| `MD_GLA05` | CINE_END04END @1394 (1) | same | Lose_END_Final_ @1036 (1) | same |
| `MD_CHI01` | END_Final_ @301 (1) | same | Lose_END_Final_ @241 (1) | same |
| `MD_CHI02` | END_Final_ @301 (4) | same | Lose_END_Final_ @301 (1) | same |
| `MD_CHI03` | Cheer1Done @1722 (6) | same | Lose_END_Final_ @4471 (1) | same |
| `MD_CHI04` | END_Final_ @48258 (10) | same | Lose_END_Final_ @273 (1) | same |
| `MD_CHI05` | CINE_ENDVictory @2662 (23) | same | Lose_END_Final_ @241 (1) | same |

Objective driver: old victory 24/24, defeat 24/24; new victory 24/24, defeat 24/24; differing rows: 0 (none).
Audit (900 ticks): old deterministic 24/24, identical after save/load 24/24, end probes victory 13/24, defeat 15/24, hits on missing actions 0; new 24/24, 24/24, victory 13/24, defeat 15/24, hits 0; maps with a different audit result: 0 .
