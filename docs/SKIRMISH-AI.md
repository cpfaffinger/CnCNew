# The built-in skirmish AI: tactics profiles and strengths

Stage 3, milestone S3-M1 (`docs/plan/STAGE-3-CHANGES.md`), Phase 33. The built-in computer player `SkirmishAi` (`src/CnCNew.Simulation/Skirmish/`; how it plays:
`docs/SIMULATION-CORE.md` §35, §36, §42) gets named **tactics profiles** and three **strengths**. Both are options of a computer slot: they are written into the match
description, so every machine of a match decides alike, and into the AI's session state. The original AI (`docs/SKIRMISH-AI-ORIGINAL.md`) is not touched; its own three
armies stay what they were.

**The default.** A slot that names no profile at medium strength is the default AI; the profile "balanced" is its name - a match of two balanced AIs is the match of two
AIs without a profile (test "balanced is the AI as it was"). Adding the profiles and strengths changed no result quantity of the reference manifest; the one change of
the default itself in this phase is the rule "no standing still" (section 4a), with its own manifest commit (`80fb676`).

## 1. Tactics profiles

`AiTactics` (`SkirmishAi.Profiles.cs`, `TacticsPlan`). Every number the profile changes was a constant of the AI before; "balanced" holds those constants, and the
patience of the rule "no standing still" (section 4a), which is new.

| | balanced | rush | turtle | eco |
| --- | --- | --- | --- | --- |
| first wave from | 8 units | 4 units | 20 units | 12 units |
| after a wave falls apart | +2, at most 30 | +1, at most 12 | +2, at most 40 | +2, at most 30 |
| supply carriers | 2 per center, at most 6 | 2 per center, at most 4 | 2 per center, at most 6 | 3 per center, at most 12 |
| second supply center | from 10 army units | only by the running-out rule | from 10 army units | at once |
| third supply center | only by the running-out rule | only by the running-out rule | only by the running-out rule | from 8 army units |
| second barracks | above 1,200 credits | above 600 credits | above 1,200 credits | above 1,200 credits |
| base defences | one per 6 army units, at most 4 | none | 2 at once, then one per 3 army units, at most 8 | one per 6 army units, at most 4 |
| defends against enemies within | 450 of a structure | 450 | 650 | 450 |
| army drawn among | all candidates | candidates at most twice the cheapest one's price | all candidates | all candidates |

"The running-out rule" is the rule of §36: when the stock beside the own centers falls below 3,000 credits, the next center goes to another safe source - every profile keeps
it. The composition rule (exchange value, §36.3 and `SkirmishAi.Composition.cs`) applies to every profile; rush first narrows the candidates to the cheap ones.

## 2. Strengths

`AiStrength` (`StrengthPlan`): what the AI may know and how fast it decides. **No strength changes a rule of the simulation**: no money, no damage, no build time, no vision a
human does not get - the AI plays through the same commands with the same pre-check.

| | easy (hasty, uninformed) | medium | hard (deliberate) |
| --- | --- | --- | --- |
| decides every | 0.33 s (10 frames of the original) | 0.5 s (15 frames) | 1 s (30 frames) |
| a mobile enemy out of sight is remembered for | 10 s | 30 s | 60 s |
| a sighting counts toward the enemy's force for | 1 min | 3 min | 6 min |
| weighs its army against the force it has seen | no (uniform draw) | yes | yes |
| attacks the map's start positions before it has seen anything | no - it has to find the enemy | yes | yes |

**Which way each factor works was measured, not assumed.** The first design (easy every second, hard every third of a second) failed its thresholds the wrong way
round: easy won 60.0 %, hard 42.7 % against the default (section 5). One factor at a time (each 128 matches against the default, the survey of section 4):

| Variant (only this changed against medium) | Win rate |
| --- | ---: |
| decides every 0.33 s | 45.9 % |
| decides every 1 s | 58.6 % |
| remembers 60 s / 6 min | 50.0 % |
| does not use the start positions | 51.7 % |
| does not weigh its army against the force seen | 39.8 % |

This AI plays weaker the more often it decides (why is not analysed - the deadlines of its build and production rules are in game time, the money it spends per
decision is not), and stronger when it weighs its army; memory and start positions hardly matter. The strengths were set by these measurements.

The AI observes every five frames at every strength (what it sees is the player's vision); the strength decides how often it acts on it. An easy AI draws its army
uniformly even when its slot names the exchange-value composition.

## 3. Where they are chosen

- **Match description** (encoding **3**, `MatchDescription.EncodingVersion`): the slot field `AiTactics` (-1: none) beside `AiStrength` (0 easy, 1 medium, 2 hard), which before
  this phase was counted and not obeyed (`ResolvedMatch.AiStrengthsIgnored` is gone). An unknown value is refused when the description is resolved.
- **Protocol** version **6** (`docs/NETWORK-PROTOCOL.md`): the room's settings carry the field. The room screen of a server still offers one computer army (medium, no profile).
- **Session state**: the AI writes `tactics` and `strength` into its state only when they are set (so the state of an AI without them is what it was), and refuses to load a
  state written for other ones. The skirmish session state is version **3** (it embeds the description).
- **Shell** (skirmish setup): every opponent slot offers the original's three armies (`GUI:EasyAI`, `GUI:MediumAI`, `GUI:HardAI`) - the built-in AI plays them by its strength -
  and, with the built-in AI, "Rush Army", "Turtle Army" and "Eco Army" (the profile at medium strength; English names, the original has no such entries). Profile and
  strength together are not selectable in the shell.
- **Client**: `--tactics balanced|rush|turtle|eco|default` (one value or one per slot) selects the built-in AI; with it `--difficulty easy|medium|hard` is its strength.
- **Tools**: `SimTool ai-match`, `ai-survey`, `ai-census` take `--tactics a,b` and `--strength a,b` per slot (the built-in AI); `ai-profiles` and `ai-profiles-report` are the survey.

## 4. The survey and its thresholds

`SimTool ai-profiles <content> --subject S --side a|b --out <tsv> [--seeds N] [--minutes M] [--jobs N]`: the subject S - a profile at medium strength (`rush`, `turtle`, `eco`),
a strength without a profile (`easy`, `hard`), or `default` (the default AI, the yardstick) - plays one side of each of the 16 two-player pairings of the reference manifest
(four maps, four pairings of factions) against the AI without a profile at medium strength, once per seed, 25 game minutes at 60 Hz. Played on both sides, every subject meets
every faction of the pairings and plays each of them. Per match it writes the outcome, the first attack of both sides (the first wave), the army value of both sides at minute
5, 10, 15 and 20 (the price of every infantry, vehicle and aircraft alive, harvesters and dozers not counted) and the subject's base defences and supply centers at minute 10
(at the end, when the match ended before).

`SimTool ai-profiles-report <tsv> ...` sums the files per subject over both sides and checks these **thresholds, fixed and committed before the first survey ran** (a first
attack that never came counts as the end of the budget; "default" is the subject `default`):

| Threshold | Subject | Holds when |
| --- | --- | --- |
| R1 | rush | its median first attack is at most 70 % of the default's |
| R2 | rush | its median count of base defences at minute 10 is 0 |
| T1 | turtle | its median first attack is at least 150 % of the default's |
| T2 | turtle | its median count of base defences at minute 10 is at least the default's + 2 |
| E1 | eco | its median count of supply centers at minute 10 is at least the default's + 1 |
| E2 | eco | its median army value at minute 20 is at least 110 % of the default's |
| S1 | easy | it wins at most 40 % of its decided matches against the default AI |
| S2 | hard | it wins at least 55 % of its decided matches against the default AI |
| D-x | each of rush, turtle, eco, easy, hard | it differs from the default by at least 10 points of win rate, 20 % of the median first attack or 20 % of the median army value at minute 10 |

A threshold that is missed is reported as missed, whatever the explanation. **Balance is reported, not judged**: a profile that loses more often than the default is not
a defect of this phase.

## 4a. A stronger default AI: the rule "no standing still" and its thresholds

The two manifest pairings that end in a stalemate (Alpine Assault America against China, Armored Fury China against GLA; the eight-player runs have a budget of
five minutes only) were traced (`SimTool ai-match --trace 7200 --trace-army`): both sides attack early, a wave falls apart at the enemy base, the threshold grows
after every such wave (8 to 18), and once the supply runs low the army no longer reaches its threshold - both sides stand at home until the budget ends.

**The rule "no standing still"** (`SkirmishAi.CommandArmy`, Phase 33): an army at rest that has not grown for the profile's patience (`TacticsPlan.StagnationFrames`:
2 min for balanced and eco, 1 min for rush, 4 min for turtle) marches with what it has, as long as that is a third of its threshold (at least three units). It
changes the default AI, so it moves the manifest's AI runs (its own manifest commit). Its state (`stagnantPeak`, `stagnantSince`) is part of the AI's state.

**Thresholds, fixed and committed before the survey of the new rule was read** (`SimTool ai-survey`, the 16 pairings x 20 seeds at 60 Hz, 25 game minutes, the
AI before the rule - binaries of `1259dee` - against the AI with it):

| Threshold | Holds when |
| --- | --- |
| G1 | the new AI decides at least 16 more of the 320 matches than the old one (5 % of all) |
| G2 | the two stalemated pairings of the reference manifest end with a winner |

## 5. Results (15 October 2026, 60 Hz, Release, one machine)

**The rule "no standing still"** (`131f590`; `ai-survey`, 16 pairings x 20 seeds, binaries of `1259dee` against those of the rule): 307 of 320 matches decided instead
of 290 - **G1 met** (at least 306); median time to victory 8.5 instead of 8.3 game minutes. In the reference manifest (`80fb676`) both stalemated pairings end with a
winner (Alpine Assault America-China: FactionAmerica at tick 53,640; Armored Fury China-GLA: FactionGLA at tick 76,470) - **G2 met** (two single trial matches had
shown it before the thresholds were committed; the survey had not been read). 16 of 16 pairings decided; no winner of a decided pairing changed.

**The profiles and strengths** (`ai-profiles`, every subject on both sides of the 16 pairings, 4 seeds, 128 matches per subject, 25 game minutes; the strengths in their
second design, `dbdb252` - the first one is in section 2):

| Subject | Decided | Won | Win rate against the default | First attack (s, median) | Army value at 5 / 10 / 15 / 20 min (median) | Defences at 10 min | Supply centers at 10 min |
| --- | ---: | ---: | ---: | ---: | --- | ---: | ---: |
| default | 120 | 60 | 50.0 % | 80 | 1,888 / 2,175 / 2,338 / 1,900 | 1 | 1 |
| rush | 127 | 40 | 31.5 % | 60 | 700 / 550 / 300 / 300 | 0 | 0 |
| turtle | 108 | 62 | 57.4 % | 315.5 | 3,550 / 4,800 / 3,825 / 4,613 | 5 | 2 |
| eco | 121 | 74 | 61.2 % | 147.5 | 2,800 / 4,000 / 3,713 / 3,713 | 0 | 2 |
| easy | 115 | 44 | 38.3 % | 80 | 3,450 / 2,588 / 2,138 / 1,825 | 0 | 1 |
| hard | 116 | 69 | 59.5 % | 81 | 2,950 / 4,375 / 3,788 / 4,075 | 1 | 2 |

| Threshold | Verdict |
| --- | --- |
| R1 rush: first attack at most 70 % of the default's | **NOT MET** - 60 s against 80 s (75 %) |
| R2 rush: no base defences at minute 10 | met |
| T1 turtle: first attack at least 150 % | met (315.5 s) |
| T2 turtle: defences at least the default's + 2 | met (5 against 1) |
| E1 eco: supply centers at least the default's + 1 | met (2 against 1) |
| E2 eco: army value at minute 20 at least 110 % | met (3,713 against 1,900) |
| S1 easy: at most 40 % of its decided matches | met (38.3 %) - in the first design **NOT MET** (60.0 %) |
| S2 hard: at least 55 % | met (59.5 %) - in the first design **NOT MET** (42.7 %) |
| D-rush, D-turtle, D-eco, D-easy, D-hard: differs from the default | all met - D-hard in the first design **NOT MET** |

Won of decided matches per subject and the faction it played (both sides together):

| Subject | America | China | GLA | ChinaTankGeneral | AmericaAirForceGeneral |
| --- | ---: | ---: | ---: | ---: | ---: |
| default | 19/28 | 19/28 | 6/32 | 12/16 | 4/16 |
| rush | 15/32 | 11/32 | 3/31 | 8/16 | 3/16 |
| turtle | 20/25 | 22/29 | 5/29 | 12/15 | 3/10 |
| eco | 22/30 | 21/29 | 15/32 | 12/16 | 4/14 |
| easy | 17/27 | 10/27 | 4/30 | 10/15 | 3/16 |
| hard | 23/30 | 21/27 | 7/28 | 15/16 | 3/15 |

**Balance is reported, not judged.** What the numbers say without judging: the rusher loses most of its matches (its army is spent early and its economy stays
small); eco and turtle beat the default more often than not; GLA and the AirForce general lose most matches under every subject, as in the surveys of Phases 17
and 23. Not surveyed: profiles at other strengths than medium (the shell does not offer them together), 30 and 120 Hz, the eight-player maps, a profile against
another profile.

## 6. What only a human can judge

Whether a profile "plays like a rusher" (or a turtle, or an economist) when watched in the spectator, and whether the strengths feel easy, medium and hard to a human
player. The survey's numbers replace the watching; they are not a verdict on how it feels.

## 7. Seen by the match analysis (Phase 36)

The analysis of a match (`docs/MATCH-ANALYSIS.md`) shows what a computer player did over the whole match. On its first eight-player match (Twilight Flame, 10 game minutes,
60 Hz, the factions of the replay measurement of `docs/REPLAY-BROWSER.md` section 7) **both GLA computer players collected nothing**: each built two barracks and never a
supply stash, spent its 10,000 starting credits within three minutes and was defeated. The survey of Phase 34 had already seen GLA win rarely (9 and 2 of 80 matches).

**The cause, found in Phase 37** (a trace in a throwaway build of the same match, `SimTool ai-match ... --players 8 --seed 11`): the supply stash's spot beside the warehouse
lies in ground the player has not explored yet, so `FindSupplyPlacement` sends the builder to look (tick 24) - and the next think (tick 54) gives the same builder the
barracks: a GLA worker is a builder and a supply carrier, and `PickBuilder` takes a carrier even while it walks. The look is abandoned, the spot stays unexplored, and the
same happens at every retry (ticks 864, 1,464 ...); the arms dealer needs the stash, so nothing else is built either. America's and China's dozers are not carriers and keep
walking. **A fix was measured, not adopted:** the builder sent to look is not picked for 60 s of game time - both GLA players then build their stash (the Toxin general at
60 s, GLA at 120 s) and survive the ten minutes. It changes the default AI and so the reference values (every pairing with GLA) - a decision of the project owner (D-34
in `docs/plan/PROGRAMME.md` §7), not taken here.

## 7a. D-34 as a measured proposal (Phase 38), adopted as variant B on 31 October 2026 (Phase 53)

The fix of section 7 was built in two variants in a worktree outside the repository (base `5ce44a2`, the simulation unchanged since Phase 37) and measured; the default AI
is unchanged, and so are the reference values. The variant proposed is in `docs/proposals/D-34-scout-reserved.patch` (applies to `SkirmishAi.cs` at `5ce44a2`).

- **Variant A** (Phase 37's fix as written): a builder sent to look at the ground of a supply center is not picked for another building for 60 s of game time
  (`_retryAt` under a key of its own, which is already session state - no new field, no new version).
- **Variant B** (proposed): the same, but only for a builder that is also a supply carrier - the GLA worker, the one `PickBuilder` takes while it walks. A dozer on its
  way is not idle and is never taken, so America and China keep their behaviour.

**Measured** (`SimTool reference-manifest results` over the 50 runs of the reference manifest, against the same of the base; `ai-match` of the case of section 7:
Twilight Flame, the eight factions of the replay measurement, seed 11, 10 game minutes, 60 Hz):

| | Variant A | Variant B (proposed) |
| --- | --- | --- |
| manifest runs whose result quantities change | 18 of 50: 13 of the 16 pairings (also America against China), the four eight-player runs, the skirmish sweep | **7 of 50: only pairings with GLA** (7 of the 8) |
| pairings decided | 15 of 16 (BitterWinter China-GLA a stalemate) | 14 of 16 (BitterWinter and DeathValley China-GLA stalemates) |
| winners changed | 5 (China-GLA twice to GLA, GLA-America on Armored Fury to GLA, ChinaTank-AirForce twice) | 1 (BitterWinter GLA-America to GLA) |
| the case of section 7 | both GLA players build their stash and are alive after 10 minutes (final hash `50713576CDA1FBA1`; base `7DF4A6C5584FEAC2`: both defeated, no stash) | the Toxin general builds its stash and is alive; GLA builds 6 structures instead of 2 and is defeated before the 10 minutes (`A17BB6195757D55F`) |

Variant A changes America's and China's games because a dozer that has arrived at the unexplored spot stays reserved for the rest of the 60 s; variant B touches only
the faction whose builder is taken while it walks. Adopting either changes the default AI and so the reference manifest (one manifest commit, the 7 or 18 runs above) -
the owner's decision D-34. The distribution of GLA's wins over many seeds is measured in section 7b.

## 7b. D-34: the distribution over seeds (Phase 38)

`SimTool ai-survey` over the eight pairings with GLA of the reference manifest, ten seeds each (80 matches per build, 60 Hz, 25 game minutes, the binaries of the base and
of variant B):

| | base | variant B |
| --- | --- | --- |
| decided within 25 minutes | 78 of 80 | **61 of 80** |
| won by GLA | 15 | 12 |
| won by the opponent | 63 | 49 |
| median length of a decided match | 384 s | 644 s |
| the same match (final hash) in both | 17 of 80 | |

By pairing (GLA / opponent / undecided), base against variant B: Alpine Assault China-GLA 6/4/0 against 0/7/3, GLA-America 0/10/0 against 2/7/1; Armored Fury China-GLA
1/7/2 against 1/6/3, GLA-America 2/8/0 against 2/8/0; Bitter Winter China-GLA 5/5/0 against 2/3/5, GLA-America 0/10/0 against 0/6/4; Death Valley China-GLA 1/9/0
against 5/2/3, GLA-America 0/10/0 against 0/10/0.

**What it says:** with the fix GLA builds its economy and lasts - a quarter of its matches now reach the 25-minute limit undecided - but it wins no more often (12
against 15); where the base GLA won, it won early (on Alpine Assault against China six times) before its missing economy mattered. The fix corrects a defect of the AI's
builder choice; whether its balance is wanted is the owner's question with D-34 (and D-9). Not measured: 30 and 120 Hz, the eight-player maps beyond the one case.

## 7c. The GLA player that stood still (Phase 56)

The owner's report left the pairings at 8 of 16 decided, and the first reading was that the computer had stalled. It had not: `ai-survey --seeds 4 --minutes 45` at `fc464d62` decides 58 of 64, and Alpine Assault America against China is 26 units to 1 when the 25 minutes end. What the same table showed instead: GLA won 4 of 32, its first barracks came after 115 s against 43 to 53 s, and on Death Valley it had none at all and was gone after 4.5 minutes, with 10,000 credits unspent.

Four causes, seen in a throwaway log: its only worker stayed reserved as a scout for 60 s after it had arrived (the reservation of D-34); it was sent only to the spot in front of the source, never to a legal spot it had not seen, so a walk that found only unseen spots failed three times and put the stash at the base, 539 units from its supply; a second builder was asked for only after the first building stood, and the worker is both; and the army and the buildings behind the first goal spent what the stash costs.

Thresholds committed before the fix (`docs/handoffs/HANDOFF-PHASE56.md`), the same survey after it:

| | before | after the fix | at the end of Phase 56 | threshold |
| --- | --- | --- | --- | --- |
| first barracks of GLA, median | 115 s | 29 s | **29 s** | T1 at most 60 s: met |
| GLA matches without a barracks | 5 of 32 | 0 | **0** | T2 none: met |
| decided of 64 within 45 minutes | 58 | 57 | **62** | T3 at least 58: met |
| GLA wins of 32 | 4 | 6 | **9** | T4 at least 8: met |

The column "after the fix" missed T3 by one and T4 by two; the rest of the phase met them. No code of the AI changed between the two columns; what did is the weapons (rockets, poison clouds and beams work now, F-109, F-110), clips and payloads. The move is not broken down by cause. The matches GLA loses last longer: the shortest went from 261 s to 486 s. GLA wins against China (9 of 16) and never against America (0 of 16). The balance is not judged (D-9).

## 7d. The economy that stopped (the owner's playtest of 7 November 2026, measured by `SimTool econ-survey`)

The owner felt that the enemy stopped collecting; a measurement over the 16 reference pairings (12, 30 and 45 minutes at 60 Hz, seeds 1 to 8, product rules, at most two matches at a time; `econ-survey` plays pairings and reports the income of each player per minute) confirmed it and found three systematic causes. **(1) No supply centre was ever built** in about 9 per cent of the player-matches of the first 12-minute survey (America against China on Alpine Assault seeds 1 and 2, Bitter Winter seed 2 for both players, Death Valley seed 2): the army spent the centre's price while a builder walked to look at the site or every builder was busy, and without a centre there is no income, so the AI never recovered. It keeps the price of its first centre in reserve in every think now. **(2) Idle carriers while supplies remain:** once every depot the AI had seen was empty its carriers stood idle for the rest of the match (America against China seed 4: seven carriers idle from minute 23 with more than 16,000 credits on the map); they go to the nearest stocked depot that no enemy owns or guards, seen or not (the map-wide view `FindSupplySource` already uses). **(3) Lost carriers were not replaced:** after a raid the AI never saved the price of the next one (America against GLA on Alpine Assault, seeds 1, 3, 5: no carrier for 8 to 13 minutes); one carrier's price stays in the bank once a centre stands, and a missing worker is saved for. Tests in `Tests.AiFirstCenter.cs` (real data, each failing before its fix). Over the 30-minute survey (seeds 3 and 4) the gathered total rose from 3.43 to 3.59 million and the decided matches fell from 21 to 18 of 32; 15 of 32 outcomes changed (a chaotic change of paths; balance not judged). **Phase 64 (`docs/SIMULATION-CORE.md` §88.2):** a side with a finished supply centre, no carrier alive or queued and less than a carrier's price sells its cheapest building whose refund covers the shortfall (`SellForCarrier`, the command a human uses, ten seconds between sales; never a command centre, a supply centre or a producer of builders or carriers), and a finished centre counts toward the cap of four only while a non-enemy stocked warehouse lies in its reach. `RealBrokeComputerPlayerSellsABuildingForACarrier`; `econ-survey` counts the stretches of 90 s with a centre and no carrier: 9 of 256 players before, 1 after (Bitter Winter ChinaTank against AirForce seed 3: America's only building was its centre, its last money went to five carriers with no stocked warehouse in reach, nothing was left to sell). **Phase 65:** a player with a builder and no centre standing or under way sells a building to pay for one after two game minutes (`SellForCenter`, the shared `SellToCover`, ten seconds apart); a USA player without a centre and under 2,000 credits never got another carrier before (`RealUsaComputerPlayersReplaceTheirChinooks`). **#274 (12 November 2026):** the AI spent its last money on carriers for a centre whose source was empty (Bitter Winter ChinaTank against AirForce seed 3, at an older build: five Chinooks for America's last 4,000 to 6,500 credits, dead at the enemy's depots). `AIPlayer::queueSupplyTruck` makes gatherers only for a centre with a stocked source that is not an enemy's within `SUPPLY_CENTER_CLOSE_DIST` plus the two bounding circles; `ProduceWorkers` now counts only such fed centres (`CountFedCenters`) for the carriers it wants and for the carrier's price it keeps, and `SellForCarrier` sells nothing for a carrier no fed centre would get. The original does not ask whether that source is safe, and neither does the port. `RealComputerPlayerBuysNoCarrierForACenterWithoutSupplies` (fails before the fix). `econ-survey`, 16 pairings x seeds 1 to 8, 25 minutes, 60 Hz, before (`7a1718fa`) and after: 125 and 126 of 128 matches played (`OverflowException: Fixed-point result out of range` in Armored Fury GLA against America seed 1 and Death Valley China against GLA seed 8 on both sides, seed 5 of the first before only; the target value of rule fix 27 divided by a sliver of health, issue #301, fixed in `1ae7d7db`: all 16 of these matches play to their end), 85 decided on both sides, 13 of the 125 common outcomes changed; carriers added in a minute with a centre and no stock in its reach 30 before, 4 after; the stretches of 90 s with a centre and no carrier 3 before, 13 after - every new one is a centre with no stock in reach, which now keeps its money; the minutes without a carrier while stock lay in reach are 5 player-matches on both sides, each with under 230 credits and nothing left to sell. Balance not judged.
