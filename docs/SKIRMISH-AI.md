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
supply stash, spent its 10,000 starting credits within three minutes and was defeated. Reported, not investigated: whether the supply stash's placement beside a
warehouse fails on that map for GLA (`FindSupplyPlacement`, `SupplyPlacementPatience`) or its plan never asks for one. The survey of Phase 34 had already seen GLA win
rarely (9 and 2 of 80 matches).
