# The built-in skirmish AI: tactics profiles and strengths

Stage 3, milestone S3-M1 (`docs/plan/STAGE-3-CHANGES.md`), Phase 33. The built-in computer player `SkirmishAi` (`src/CnCNew.Simulation/Skirmish/`; how it plays:
`docs/SIMULATION-CORE.md` §35, §36, §42) gets named **tactics profiles** and three **strengths**. Both are options of a computer slot: they are written into the match
description, so every machine of a match decides alike, and into the AI's session state. The original AI (`docs/SKIRMISH-AI-ORIGINAL.md`) is not touched; its own three
armies stay what they were.

**The original profile.** A slot that names no profile at medium strength is the AI as it was before this phase: the reference manifest keeps its result quantities. The
profile "balanced" is only the name of the old values - a match of two balanced AIs is the match of two AIs without a profile (test "balanced is the AI as it was").

## 1. Tactics profiles

`AiTactics` (`SkirmishAi.Profiles.cs`, `TacticsPlan`). Every number the profile changes was a constant of the AI before; "balanced" holds those constants.

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

| | easy | medium | hard |
| --- | --- | --- | --- |
| decides every | 1 s (30 frames of the original) | 0.5 s (15 frames) | 0.33 s (10 frames) |
| a mobile enemy out of sight is remembered for | 10 s | 30 s | 60 s |
| a sighting counts toward the enemy's force for | 1 min | 3 min | 6 min |
| weighs its army against the force it has seen | no (uniform draw) | yes | yes |
| attacks the map's start positions before it has seen anything | no - it has to find the enemy | yes | yes |

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
a strength without a profile (`easy`, `hard`), or `default` (the AI as it was, the yardstick) - plays one side of each of the 16 two-player pairings of the reference manifest
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

## 5. Results

Not yet measured.

## 6. What only a human can judge

Whether a profile "plays like a rusher" (or a turtle, or an economist) when watched in the spectator, and whether the strengths feel easy, medium and hard to a human
player. The survey's numbers replace the watching; they are not a verdict on how it feels.
