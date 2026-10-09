# First observation of two built-in AIs by a human (8 October 2026): four findings, causes, fixes, measurement

As of: 8 October 2026, branch `worktree-agent-a4e29cbcc29f6bab6` (base `main` `5abbeea`). For the first time a human – the project owner – has watched a match between two built-in
AIs (`SkirmishAi`) in the client (`scripts/demo-spectator.ps1`, 30 Hz, one of the five standard pairings, seed not noted). This document takes his four observations
one by one: reproduced? (numbers), cause (file, symbol), fix (commit), measured effect, what remains. **Nobody has seen the result in the window:** everything here is
measured headless; what only a human can confirm is in §6.

**Addendum:** the branch has been brought onto `main` `4d9d9e5` (exit path, round 3 of the rate packages). §1 to §10 describe the state before the merge and remain as a measurement
log; **what holds today – a single goal-spot mechanism, all measurements redone, the acceptance of rule D repeated – is in §11.**

**Method.** A whole match is chaotic; no finding rests on a single one. Measurement is done with `SimTool ai-census` (new, §5): the 16 pairings of the reference manifest
(four maps × four pairings) with 5 seeds each = 80 matches = 160 player-matches, 25 game minutes of budget, 30 Hz; in addition `ai-survey` (16 × 20 seeds = 320 matches). "before" is
`main` (`5abbeea`) unless something else is stated. The states are named like the commits in §7: **B** (production), **C1** (goal spots), **C2** (entering), **D** (composition).

| State | `ai-census`: decided of 80 | `ai-survey`: decided of 320 |
|---|---|---|
| `main` | 66 | 253 (given by the brief; not collected anew) |
| after B | 73 | 281 |
| after C1 | 74 | 288 |
| after C2 | 71 | 284 |
| after D (rule for both players) | 75 | 298 |

The differences between B, C1 and C2 lie within the scatter (half the width of the 95 % interval for the difference of two such proportions is about 5 points); the step
from `main` to B (8.8 points) does not, the one from C2 to D (4.4 points) lies at the boundary. What is reliable for D is the paired test in §4.3, not this column.

## 1. Observation A: produced units stand in the barracks

Known, not subject of this brief (no produced vehicle and no soldier of any faction drives out; the fix "units leave the factory on the
exit path" lies on another branch). For everything that follows this means: the built-in AI today works with units that come into being at the `UnitCreatePoint` inside the building and go from
there to the rally point. As soon as that fix is merged, the numbers of this document are to be collected anew.

## 2. Observation B: "The China AI builds no tanks although the building for them is standing"

### 2.1 Reproduced – and not only for China

| Faction (player-matches) | with vehicle factory | factories that ever built a vehicle | infantry | vehicles | vehicle share | looks of the army production at a factory: order / "not affordable" |
|---|---|---|---|---|---|---|
| China (40) | 23 | **4** | 2,137 | **6** | 0.3 % | 8 / 28,388 |
| ChinaTankGeneral (20) | 18 | 14 | 1,342 | 57 | 4.1 % | 61 / 35,909 |
| America (40) | 36 | 31 | 3,991 | 161 | 3.9 % | 165 / 55,205 |
| AmericaAirForceGeneral (20) | 20 | 17 | 2,166 | 65 | 2.9 % | 69 / 43,589 |
| GLA (40) | 10 | 10 | 2,202 | 25 | 1.1 % | 25 / 25,514 |

Over all factions 188,605 looks at a factory ended with "nothing affordable" and 328 with an order. Not a single production command was rejected (no rejection
because of prerequisites, queue, power, `MaxSimultaneousOfType` or science); China's tanks need no prerequisite that the AI does not build (Battlemaster, Gattling
and Dragon only the war factory); the factory is not blocked. The American "vehicles" before are 73 % Ambulances (the cheapest, 118 of 161).

### 2.2 Cause

`Skirmish/SkirmishAi.cs`, `ProduceArmy` (state `main`): every producer bought, in the order in which the buildings came into being, a random unit **among those that the
money covers at this moment**. The barracks stands first (`PlanGoals`: barracks before factory), and its infantry is the cheapest army unit (infantry 300, tanks from 800). With
an income of two gatherers the account balance exceeds the price of a soldier every few seconds and that of a tank never: the barracks spends every credit before the
factory is asked. Nothing was ever saved.

Yardstick original (`MapTool script-library <content> --show "China N "`, normal difficulty): after four Red Guards of the first wave every wave consists of vehicles –
`China N 1ST Wave Veh` 1 Gattling + 2 Battlemaster, `2ND Wave Tanks` 2 Battlemaster + ECM + Gattling, `3RD Wave Tanks` 4 Battlemaster, `3RD Wave Artillery` 2 Inferno,
`4TH Wave Tanks` 2–4 Battlemaster + 2 Overlord. "A China army has Battlemasters and Gattlings" is a datum of the scripts there (with money gifts, `docs/SKIRMISH-AI-ORIGINAL.md` §1.4).

### 2.3 Fix: rule "standing army order" (commit `50718d7`, test `7a29943`)

The next army unit is drawn among **everything** that barracks and factories with room in the queue could build (prerequisites fulfilled, every unit type with
the same chance, **the price is not looked at**), and the AI then saves for exactly this unit; nothing else is bought for the army until it is paid for. An order that
could not be paid for 60 s (`ArmyOrderPatienceFrames`, own choice) gives way to a draw among what the money covers now – an economy that has come to a standstill
spends its last money nevertheless (in the census 121 of 7,308 orders). The order is a field in the state of the AI (`_armyOrder`), next to its others; randomness only from its own stream.

### 2.4 Effect

| Faction | factories that build | infantry | vehicles | vehicle share |
|---|---|---|---|---|
| China | 21 of 23 (before 4) | 1,033 | **300** (before 6) | 22.5 % |
| ChinaTankGeneral | 17 of 17 | 514 | 477 | 48.1 % |
| America | 34 of 35 | 1,282 | 491 | 27.7 % |
| AmericaAirForceGeneral | 20 of 20 | 870 | 266 | 23.4 % |
| GLA | 10 of 10 | 1,654 | 261 | 13.6 % |

China now builds Battlemasters (84), Dragons (83), Gattlings (74) and Troop Crawlers (59). The 16 runs of the
reference manifest (one seed): decided 12 → 12, the outcome changes in 7 (four stalemates come and go, three changes of winner).

**What remains.** (a) The share of infantry stays high because the barracks stands earlier than the factory (median of the first factory: tick 4,725) and until then is drawn from alone. (b) The rule chooses blindly
(until D): Ambulances too, anti-air against an opponent without aircraft, empty transports. (c) **Side finding, not fixed:** on three of the four maps (Alpine
Assault, Bitter Winter, Death Valley) the GLA AI finishes exactly one building, the barracks, and only on Armored Fury Supply Stash, Tunnel and Arms Dealer – in all 30 player-matches there, before as
after. Hence "GLA with factory: 10 of 40". Cause not investigated. (d) The attack threshold counts heads (eight units); a more expensive army reaches it later.

## 3. Observation C: "Three infantrymen outside, one stands in the middle, two run around him"

### 3.1 The detector

`OrbitDetector` (`tools/CnCNew.SimTool/AiCensus.cs`): per logic frame the position of every mobile ground unit of a player; once per second the last 10 s are judged. A
unit that covers more than 150 units of path and gets less than 20 away from the spot is an episode: **circling** (`orbit`) with at least two full turns of the
walking direction, otherwise **back and forth** (`shuffle`). Counting is done with the place – at the rally point of the AI (`rally`), near its own goal elsewhere, on the way to a distant goal, without
a move order – and separately for army and economy (gatherers, dozers). `--orbits <datei>` writes state and neighbours per episode, `--watch <id>` follows one unit.

### 3.2 Reproduced

On `main`: 359 circling episodes of army units in 80 matches, of which **269 at the rally point, in 74 of 160 player-matches**, for every faction (among the 359: Red Guard 235,
Ranger 47, Missile Defender 32 …). The picture is always the same, here the first:

```
AlpineAssault_America-China, seed 1, tick 2580: ChinaInfantryRedguard#27 at (1087, 596), path 250, from the spot 19.9, 2.7 turns;
  goal (1098, 605) – the rally point, 14.1 away; neighbours: ChinaInfantryRedguard#23 stands at (+11, +9), ChinaInfantryRedguard#32 is walking
```

One stands exactly on the rally point, the others run around him at the distance of the two radii (about 14) – what the owner saw. Often several units stand on
the same point on top of each other (on `main` up to three, after B up to eight in one episode): whoever finds no avoidance step any more walks through.

### 3.3 First cause: all have the same goal point

Everything produced gets the same point (`SetRallyPointCommand`, after that the AI's `AttackMoveCommand`). `AIUpdateInterface.Move` (`Modules/BuiltIn/Phase5Modules.cs`): the last
waypoint is the goal itself, arrived is whoever lies within `max(CloseEnoughDist, ein Bildschritt)`. If somebody already stands there, the waypoint lies in his circle; the
step there is blocked, `SimulationWorld.AvoidLocalBlockers` turns it by 45° or 90° – every frame again, hence in a circle. The replanning around standing units
(`StationaryBlockersAhead` → `ReplanAround`) does not help: a detour that arrives does not exist, and without one the old path stays.

**What the original does** (read, rewritten, nothing copied): every unit gets a goal cell of its own. `AIInternalMoveToState::onEnter` (`GameLogic/AI/AIStates.cpp`)
sends the goal through `Pathfinder::adjustDestination` (`GameLogic/AI/AIPathfind.cpp`): a square spiral around the desired cell (right, up, twice left, twice
down, three times right …, at most `MAX_CELLS_TO_TRY` = 400), the first cell that `checkDestination` accepts – walkable, not the goal cell of an ally ("Don't usurp your
allies goals"), not occupied by a standing unit that one cannot run over ("Don't move to an occupied cell") – is reserved with `Pathfinder::updateGoal`; if none
is found, `snapClosestGoalPosition`. Groups additionally get offset goals (`AIGroup::computeIndividualDestination`, `AIGroup.cpp`: the distance to the group centre, limited to
six radii, then likewise `adjustDestination`).

**Fix (commit `3816445`, test `ccdc12b`):** `SimulationWorld.TryFreeGoalSpot` (`World/SimulationWorld.GoalSpots.cs`), without a reservation table: a standing unit
claims the spot on which it stands, a moving one the spot to which it is on its way (`Entity.MoveTargetX/Y` – state that every snapshot already carries, **no new state**).
If the own goal is occupied by a standing unit or one with a smaller id is walking there, the unit takes the first spot on the same spiral that nobody
claims and that lies in a straight line of walkable cells from the goal. Units of an enemy never claim (a tank that is ordered onto enemy infantry drives
on). The AI does not send a unit that has come to rest within eleven cells of the rally point there again (`SkirmishAi.GatherRadius`).

*For the merge (the package R3-OBSERVERS edits the same file):* the intervention in `AIUpdateInterface` is **one block of six lines** at the beginning of the frame decision of `Move`,
immediately before `if (!_pathValid || …) PlanPath(…)`, and **one helper** `GoalSpotCheckDue` with two constants before `BlockerReplanFrames`. The question is asked only for a plain
move order (no dock, no build task, no enter target, no goal object, no way back, no victim in the sights), only within six cells of the goal and only in every
15th logic frame of the world, offset by the id. Deviations from the original: the question is asked near the goal instead of when the order is given (so a spot that was occupied on the way
is noticed too); the spiral lies around the exact goal point, not around the cell centre; "reachable" is the straight line, not the zone table.

**Frame convention (`docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §1):** the question falls into the one decision per logic frame; the rhythm is the frame number of the world
(`Time.FramesIn(Tick)`), hence the same frame at every rate; a unit that at above 30 Hz is just covering the last step of its path counts, as with the 30 Hz step, as standing on
its goal (rule 3). The test sends six units to one point: all arrive after 193 frames, exactly one stands on the point, the others on the neighbouring cells
(10,10), (−10,0), (−10,10), (10,0), (0,10), nobody stands inside another – and position, facing and order state after **every** logic frame are bit-identical at 30, 60 and 120 Hz.
Without the fix two of the six are still walking after 20 s.

### 3.4 Second cause: whoever is to enter runs around his vehicle

After the first fix, cases remained at the rally point with the same picture – in the middle, however, a **vehicle** (Humvee, Troop Crawler; since B the AI builds them), and the
walking ones had the command to enter (`SkirmishAi.UseTransports` → `MoveAndEnterCommand`). Numbers from `main`: in the 20 AI runs of the reference manifest **1,523
enter commands and 130 entries**; the case "heal inside ChinaVehicleTroopCrawler" of the rate suite was "unmeasured" there because the Red Guard never got in.

Cause: for the enterer the vehicle was an obstacle like every other unit (`SimulationWorld.Collision.cs`: `AvoidLocalBlockersCore` and the two
`StationaryBlockersAhead…` excluded only the goal object of an approach), and he was taken in only when his movement was **finished** – on the centre of the vehicle,
which he cannot reach (`AIUpdateInterface.UpdateEnter`).

Original: `AIEnterState::onEnter` (`AIStates.cpp`) tells the pathfinder to ignore the object to be entered (`ai->ignoreObstacle(goal)`), and the container takes in whoever collides with
it and has it as enter target (`OpenContain::onCollide`, `GameLogic/Object/Contain/OpenContain.cpp`).

**Fix (commit `b93ba6c`, test `2259f70`):** `AIUpdateInterface.IgnoredObstacle` (goal object or enter target) is what the three collision queries exclude; `UpdateEnter`
takes a unit in as soon as its circle touches that of the container, checked after a whole frame step. A building keeps the old rule (the path ends at its wall; its
circumcircle reaches far beyond a rectangular footprint). *For the merge:* one property after `GoalObject`, eleven lines in `UpdateEnter`, three times one word in
`SimulationWorld.Collision.cs`. The test: three enterers from three directions are in the vehicle after 51, 52 and 68 frames, in the same frames at 30, 60 and 120 Hz; before,
each of the three is still running around the vehicle after 15 s, 18 units from the centre.

### 3.5 Effect (circling episodes of army units, 80 matches)

| State | at the rally point | player-matches with one | near the goal elsewhere | on the way to a distant goal | without an order | enter commands / intakes into containers (20 manifest runs) |
|---|---|---|---|---|---|---|
| `main` | **269** | **74 of 160** | 8 | 61 | 21 | 1,523 / 130 |
| after B | 238 | 78 | 8 | 50 | 14 | 2,396 / 1,100 |
| after C1 | 15 | 12 | 9 | 48 | 2 | 2,343 / 639 |
| after C2 | **2** | **2 of 160** | 2 | 33 | 2 | 1,469 / 844 |
| after D | 2 | 1 | 2 | 19 | 2 | 1,726 / 1,001 |

Back-and-forth episodes at the rally point: 534 → 71 (after C2).

**What remains.** (a) Two episodes at the rally point: enterers of a vehicle on which a rejected unit already stands (the AI orders more enterers than there are free
seats; it does not count those on their way). (b) **On the way, not fixed:** two units that avoid each other on the march and in doing so turn around each other
(33 episodes; `AvoidLocalBlockers` knows its limit: "can oscillate against a mover coming the other way") – visible as two soldiers circling each other without one
standing. (c) **Gatherers at the dock, not fixed:** workers and supply trucks circle at the supply depot or supply center when another one stands there (88 episodes on `main`, 235 after C2,
68 after D; the number fluctuates with the number of gatherers). The same kind of cause as C1, but in the dock state machine, which the fix deliberately excludes. (d) The rate suite now measures the
Troop Crawler case and in doing so shows a quantity outside the tolerance that was not to be seen before (§8).

## 4. Observation D: "The China AI does not adapt to its opponent"

### 4.1 Facts

**What `ProduceArmy` chooses from.** On `main`: per producer equally likely among the units affordable right now – in fact only infantry, China 52 % Tank Hunters and 47 %
Red Guards, no matter against whom. Since B: every unit type of all producers with the same chance. The opponent enters into neither of the two. The observation is correct.

**What the armies build** (shares of the produced army units, 80 matches, state C2; in parentheses: what they are good against according to the weapon data):

| Faction | Composition |
|---|---|
| China | Tank Hunter 38 % (vehicles, buildings), Red Guard 34 % (infantry), Battlemaster 9 %, Dragon 8 % (infantry, buildings), Gattling 7 % (infantry, aircraft), Troop Crawler 5 % |
| America | Ranger 38 % (infantry), Missile Defender 35 % (vehicles, buildings, aircraft), Ambulance 8 % (nothing), Crusader 7 %, Humvee 6 %, Avenger 5 % (only aircraft and projectiles) |
| GLA | Terrorist 34 %, Rebel 29 % (infantry), Tunnel Defender 25 % (vehicles, buildings), Scorpion 5 %, Toxin Tractor 4 %, Quad Cannon 4 % |

**What hits what** (damage from `EntityDamaged`, by attacker type and kind of the victim, state C2): Red Guard 56 % on infantry, 37 % on buildings; Tank Hunter 18 % infantry,
18 % vehicles, 59 % buildings; Dragon 26 % / 16 % / 55 %; Battlemaster 7 % / 28 % / 63 %; Gattling Tank 34 % / 24 % / 33 % and 6 % aircraft; Ranger 43 % infantry, 52 %
buildings; Missile Defender 8 % / 12 % / 76 %; Crusader 5 % / 45 % / 46 %; Avenger 100 % on projectiles – on not a single ground target. The largest item of every unit except the pure
infantry weapons is the enemy base: the armies that meet here are predominantly infantry and buildings.

**What the original does.** It adapts, but not unit by unit, rather with **counter teams** of the scripts (`SkirmishScripts.scb`, read with
`MapTool script-library <content> --show "China"`). Scripts set flags from the number of enemy objects of an object list:

```
IF FLAG['_CT_INFANTRY'; 0] AND PLAYER_HAS_OBJECT_COMPARISON['<This Player's Enemy>'; 3; 25; 'Infantry']   DO SET_FLAG['_CT_INFANTRY'; 1]
IF FLAG['_CT_INFANTRY'; 1] AND PLAYER_HAS_OBJECT_COMPARISON['<This Player's Enemy>'; 0; 10; 'Infantry']   DO SET_FLAG['_CT_INFANTRY'; 0]
```

likewise `_CT_TANK` (from 8 or 15 `'Tanks'`), `_CT_AIR` (from 4 `'Aircraft'`), `_CT_BASEDEF` (from 5 `'Base Defenses'`), `_CT_STEALTH` (from 5 `'Spy'`). On the flags hang the
production conditions of teams with higher priority (170–215) than the waves (100–130): `China CT - Infantry 1` (up to 2 Gattling Tanks, up to 4 Red Guards),
`China CT - Infantry 2` (Dragon, Gattling, Red Guard), `China CT - Tank 1–3` (Tank Hunter, Battlemaster), `China CT - Air 1/2` (Tank Hunter, Gattling Tank),
`China CT - Base Def 1/2` (Inferno Cannon, Nuke Cannon), `China CT - Stealth 1/2` (Troop Crawler). The condition counts the objects of the opponent, seen or not. Against
a lot of infantry China's original AI therefore builds Gattling and Dragon tanks **and** Red Guards – what the owner expected. The built-in AI executes no scripts.

**The attempt from Phase 17** (`docs/SIMULATION-CORE.md` §36.3): per producer the most expensive unit, doubled for greater range, factories first, saving instead of small purchases.
Withdrawn after two measurement rounds over 16 individual runs (12 → 10 → 11 decided): the price is not a combat value (18 Avengers in front of a base at which they cannot
shoot), and the verdict stood on one seed per pairing. The lesson there – "a combat value per target, damage against armour" – is what is built here; and measurement is done with 640
matches instead of 16.

### 4.2 The rule "exchange value" (`ArmyComposition.ExchangeValue`, `Skirmish/SkirmishAi.Composition.cs`)

What the player has **seen** of the opponent in the last three minutes (every enemy object with hit points, by type; the AI remembers it in `Observe` next to its other
memories – no query of the world, nothing that the player cannot know) is the force E. For a candidate unit u, only from the catalog:

- **Damage per frame** d(a, b): the best weapon of the default weapon set of a that may be fired at b (ground or air): base damage × armour coefficient of b for the
  damage type × shots per logic frame (magazine and reloading included); a weapon that kills its carrier counts as one shot in a lifetime; at most one object per frame;
- **Effect** O(u) = Σ over E of n(e) · price(e) · d(u, e) / health(e) – the credits of the opponent that u destroys per frame as long as it fires;
- **Exposure** X(u) = 1/1800 + Σ over E of n(e) · d(e, u) / health(u) – the share of itself that u loses per frame; never less than "lives one minute";
- **Exchange value** V(u) = O(u) / (X(u) · price(u)) – destroyed enemy credits per credit spent.

The draw gives every candidate the same share of one quarter (`VarietyShare`: the floor of variety, and the only chance for units whose value the catalog does not
show – transports, healers, units that only capture) and distributes the rest proportionally to V. Without a sighting, or if no candidate can hurt anything seen, the
draw is the uniform one of B. Always exactly one draw from the AI's own, seeded stream. Cost: per draw (every few seconds) candidates × seen types; the
damage values per pair of types are remembered. The new state (`_sightings`) lies in the same object as the rest of the AI state (register E11).

What the rule does **not** know: range and speed (a unit that never gets to shoot), area damage, garrisons in buildings, upgrades and veterancy levels, stealth,
special powers, the next unit of the opponent. It judges the unit of a producer, not teams. Three numbers are our own choice: three minutes of memory, one minute of
lifetime as the upper limit, one quarter of variety.

Test (`8d8dcdf`): two equally expensive soldiers from one camp, a rifle (against soft armour) and a bazooka (against hard). Against hard targets 20 rifles to 82
bazookas, against soft ones 80 to 22, without a sighting or without the rule 48 to 54; same seed, same army.

### 4.3 Acceptance – fixed in advance, not adjusted afterwards

Criteria (from the brief, before any run): **(1)** paired, the same 16 pairings × 20 seeds (1–20) at 30 Hz, one player with the rule against one without in the same match, both
seating orders, 640 matches: the AI with the rule must win more decided matches, two-sided exact binomial test, α = 0.05. **(2)** symmetric (both with the rule, 320
matches): the share of decided matches must not lie more than half the width of the 95 % interval of two proportions below `main`'s 253 of 320.

Before the acceptance **one** trial ran with other seeds (101–105, 160 matches: 86 victories, 63 defeats, 11 open) to exclude gross errors; nothing was changed in the rule
afterwards. Commands: `ai-survey --composition exchange,uniform`, `uniform,exchange`, `exchange,exchange`, then `composition-test`.

| Criterion | Result |
|---|---|
| (1) paired, 640 matches | AI with the rule: **339 victories, 252 defeats**, 49 undecided; win rate among the 591 decided **57.4 %**, exact 95 % interval [53.3 %, 61.4 %]; p = **0.0004** → **passed** |
| (2) symmetric, 320 matches | **298 of 320** decided (93.1 %); `main` 253 of 320 (79.1 %); half width 5.2 points, limit 73.8 % → **passed** |

In addition, not part of the criterion: the same state without the rule decides 284 of 320. Per pairing (victories with the rule / defeats / open): the rule wins in 12 of 16 pairings
more than it loses, in 4 equally many, in none fewer; most clearly Bitter Winter America–China (22/5/13), Bitter Winter ChinaTank–AirForce (26/10/4) and Armored Fury
America–China (25/10/5). In the four pairings with a tie (20/20) the faction decides, not the rule: twice America wins all 40
matches against GLA, twice one faction 34 or 38 of 40.

**Consequence:** the rule is the **default** (`SkirmishAi.DefaultComposition = ExchangeValue`, own commit); `SkirmishAiOptions.Composition` and `SkirmishSlot.Composition` choose per
player.

### 4.4 What the rule changes in the armies – and what it does not

Shares with the rule (the same census, in parentheses without): China Dragon **21 %** (8), Battlemaster 3 % (9), Gattling 6 % (7), Troop Crawler 1–2 % (5), Red Guard 34–37 % (34),
Tank Hunter 33–34 % (38); America Humvee 15–16 % (6), Crusader 8–13 % (7), **Ambulance 2 % (8), Avenger 1 % (5)**; GLA Terrorist **47 %** (34), vehicles 6 % (13). Of 7,511
draws 4,228 were informed and 3,283 (44 %) fell before the first sighting.

**Honest about the observation itself:** "Infantry comes, and the China AI builds Tank Hunters instead of infantry of its own" is thereby fixed **only in part**. The rule strikes out what
hits nothing (Ambulances, anti-air without aircraft, empty transports), and shifts towards what destroys infantry and buildings cheaply (Dragon, Humvee, Terrorist). The
share of Tank Hunters, however, sinks only from 38 % to one third: almost half of the draws fall before the AI has seen an opponent (then 50 : 50 from the barracks), and the
rule credits the Tank Hunter with his damage to buildings – half of what he actually hits in these matches. Against whom China plays hardly changes the mix
(against America as against GLA 21 % Dragon), because both opponents here field predominantly infantry and buildings. Whether that looks like "adapts" in the window only a human can
say.

## 5. Tools (new)

| Command | Purpose |
|---|---|
| `SimTool ai-census <content> --out <tsv> [--orbits <txt>] [--seeds N] [--first-seed N] [--pairings …] [--jobs N] [--composition a,b] [--watch <id>]` | the matches of `ai-survey`, observed: per player first completion of every building, production per producer, outcome of every look of the army production (`SkirmishAiStats.ArmyProduction`), money every 50 s, damage by attacker type and victim kind, losses, circling units by place |
| `SimTool ai-survey … --composition uniform\|exchange\|default,uniform\|exchange\|default` | composition per player |
| `SimTool composition-test --rule-first <tsv> --rule-second <tsv> --symmetric <tsv> --baseline 253/320 [--out <md>]` | the acceptance from §4.3: exact binomial test, Clopper-Pearson interval, two-proportion limit |

## 6. What only a human can confirm, and what he should note next time

- Whether nothing circles in the window any more. Headless: of 269 episodes at the rally point 2 remain; **on the way** two units still occasionally turn around each other (§3.5 b), and
  gatherers circle at the dock (c). Whoever sees that has not seen a relapse of C, but one of the two open cases.
- Whether the China army "looks like tanks" (about a quarter to a third vehicles, above all Dragon) and whether the mix comes across as an answer to the opponent (§4.4).
- What units look like that stand next to the rally point on neighbouring cells (a grid of 10 units; formations do not exist), and what a soldier looks like who disappears into the
  vehicle on touching it.
- Please note: **the seed** (the script prints it: `Seed N`) and the pairing, the **game time**, the **units** (type, which player) and whether one of them is standing. With the
  seed the match is the same headless: `SimTool ai-census <content> --out x.tsv --orbits x.txt --pairings <Paarung> --first-seed N --seeds 1`.
- Observation A is open; as soon as its fix is merged, paths, gathering times and all numbers here change.

## 7. Commits, reference manifest, missions

`baselines/reference-manifest.json` is **not** rewritten (brief). `reference-manifest check --set full` against the file of the branch start; "results" from
`reference-manifest results`, each against the state before. On `main` itself three mission runs deviate (`MD_GLA01`, `MD_USA02`, `MD_USA05`: the fix of the lost
wake-up, manifest still outstanding there); that is inferred from their commit message and from the fact that the two tool commits show the same three deviations.

| Commit | Content | Runs with a different hash than the manifest | Result comparison with the state before |
|---|---|---|---|
| `8c42f70`, `6d83063` | tools: `ai-census`, counter `ArmyProduction`, place of the episodes, `--watch` | 3 (those of `main`) | – (no behaviour changed) |
| `50718d7` (B), test `7a29943` | standing army order | 24 = 3 + the 21 runs with `SkirmishAi` (16 two-player, 4 `ai8_…`, `skirmish-sweep`) | 29 runs the same; 16 two-player runs: decided 12 → 12, outcome different in 7; `skirmish-sweep` different checksum; all 27 mission runs the same |
| `3816445` (C1), test `ccdc12b` | free goal spot | 40: the 21 AI runs and 19 mission runs (`GC_ChinaBoss_*` ×9, `GC_LaserGeneral_ChinaNukeGeneral`, `MD_CHI01`, `MD_CHI05`, `MD_GLA01`, `MD_GLA03`, `MD_GLA05`, `MD_USA01`, `MD_USA02`, `MD_USA05`, `Training01`) | 22 runs the same (among them 12 of the 19 missions: only the hash is different); 7 missions with different event counts, **no outcome and no end script different** – e.g. `MD_GLA01` `MoveArrived` 53 → 185, `MD_USA01` 699 → 796; two-player runs decided 12 → 13, outcome different in 3 |
| `b93ba6c` (C2), test `2259f70` | entering | the same 40; compared with C1 18 hashes change (13 two-player runs, `ai8_FortressAvalanche`, `ai8_IronDragon`, `ai8_TwilightFlame`, `mission_MD_USA01`, `mission_MD_USA02`) | 32 runs the same; missions: `MD_USA01` `EntityContained` 216 → 217, `MD_USA02` `MoveArrived` 93 → 92, outcome the same; two-player runs decided 13 → 15, outcome different in 5 |
| `ca02d34`, test `8d8dcdf`, tools `a933fc2` | rule "exchange value", **off** | the same 40, the same hashes as C2 (the measurement for C2 ran with this state) | – |
| `2451852` | rule **on** (default) | the same 40; compared with the state before 19 hashes change: the 16 two-player runs, `ai8_FortressAvalanche`, `ai8_IronDragon`, `ai8_TwilightFlame` – not `ai8_TwilightFlame_short`, not `skirmish-sweep` (in 3,000 ticks no AI sees an opponent), no mission | 31 runs the same; two-player runs decided 15 → 14, outcome different in 4 |

Unchanged compared with `main` after all commits remain: `reference-match`, `stress` and eight mission runs (`GC_AirGeneral_*` ×3, `GC_ChemGeneral_*` ×2,
`GC_SuperWeaponsGeneral_GLADemolitionGeneral`, `GC_TankGeneral_*` ×2).

**Commands-only agent of the missions** (`CampaignTool drive <content> --commands-only --map "/NAME.map" --minutes 34`, 0 world writes; it uses `SkirmishAi`):

| State | `MD_GLA04` | `MD_CHI01` | `MD_CHI02` |
|---|---|---|---|
| `main` | victory @1,022 | victory @18,252 | victory @29,250 |
| after B | victory @1,022 | victory @15,504 | **not reached** (61,200 ticks) |
| after C2 (rule off) | victory @1,022 | victory @18,206 | **not reached** |
| after D (rule on) | victory @1,022 | victory @18,585 | **not reached** |

**`MD_CHI02` has not been won within 34 game minutes since B** (before at 16:15); C and D change nothing about that. It is a single, chaotic run, and already the attempt from
Phase 17, which likewise made the AI save, lost exactly this mission (`docs/SIMULATION-CORE.md` §36.3) – that suggests that the agent of this mission does better with many cheap
units than with few expensive ones. It is not investigated. With 60 game minutes too (108,000 ticks, state D) it is not won: that is not a "later", but
a regression of this one run that B causes.

## 8. Rate suite (`Simulation.Tests -- --content-dir … --only RateEquivalence`)

Quantities outside the tolerance at 60 / 120 Hz, `main` according to the brief against this branch: locomotion 2/2 → 2/2, flight 0/3 → 0/3, projectiles 4/4 → 4/4, turrets 4/0 → 4/0,
weapons, economy, timed, scripts 0/0 → 0/0, **ai 24/23 → 22/22**, **overtime 0/0 → 0/1**.

On `overtime`: that is not a quantity that has become worse, but one that could not be measured before. On `main` the Red Guard of the case "heal inside
ChinaVehicleTroopCrawler" never gets into the transport (three quantities "unmeasured", two with the value 0 at all rates "ok"; re-measured at the starting state). Since C2 he enters – the
first frame with healing lies at 5.2000 s at all three rates –, and the suite sees the healing in the transport for the first time: "frames with healing in 120 s" 151 / 151 / 150.
The deviation lies in the healing (`TransportContain`), not in the entering; it is **not fixed** and belongs to the rate packages. In the group 52 instead of 55 quantities are now
unmeasured.

## 9. Verification at the final state (`2451852`)

| Check | Result |
|---|---|
| `dotnet build CnCNew.slnx` | 0 warnings, 0 errors |
| `Simulation.Tests` (Release) at 30 Hz, `-- --tick-rate 60`, `-- --tick-rate 120` | each "All simulation core tests passed" (502 tests, among them the four new ones) |
| `reference-manifest check --set quick --verify-incremental` | incremental hash in 67,031 checked ticks equal to the fully computed one, **0 deviations**; 7 of the 9 runs deviate from the manifest as expected, `reference-match` and `stress` OK |
| `input-manifest check` | identical |
| Rate suite | §8 |
| `Game.Tests` (Release, `--content-dir`) | "All game tests passed" (spectator and skirmish tests use the AI) |
| `scripts/check-spectator.ps1` | PASS (Alpine Assault America–China, seed 7: America builds a Crusader in the war factory in the process) |

Not run: `CampaignTool drive` over all 24 maps, `audit`, `reachability`, the scene audits, `reference-manifest check --set full` under Linux, the determinism matrix. The
mission runs of the manifest show no changed outcome (§7); more is not verified for the campaign.

## 10. Not done, not verified

- No human has seen any of this (§6).
- Observation D is fixed only in part (§4.4); a counterpart to the counter teams of the original (thresholds per object list) does not exist.
- GLA builds only a barracks on three of four maps (§2.4 c) – found, not investigated.
- Two units that circle each other on the way, and gatherers at the dock (§3.5 b, c) – found, not fixed.
- The AI overbooks transports (§3.5 a).
- The healing in the transport at 120 Hz (§8).
- **`MD_CHI02` in commands-only mode has been lost since B** (§7), with 60 game minutes too; the commands-only agent thereby wins two instead of three of the three maps checked.
- The 16 table runs in `docs/SIMULATION-CORE.md` §19/§36 and all hashes there are superseded; that document, `CLAUDE.md`, README and the handoffs are, in accordance with the brief, not
  touched.
- Balance is not assessed: "more decided matches" and "wins against the AI without the rule" say nothing about whether the AI plays well against a human.

## 11. Addendum: brought onto `main` `4d9d9e5` (8 October 2026)

Since the starting state `main` has received the exit path (produced units drive out of the factory – observation A is thereby fixed), the new manifests and the rounds 3 of the
packages flight and observers. The merge is `c683067`; after that four commits. Everything in this section is measured on the merged state.

### 11.1 Conflicts and their resolution

| Place | both sides | Resolution |
|---|---|---|
| `IWorldServices` (`ModuleFramework.cs`) | `main`: `FindFreeGoal` (exit: rally point per unit); branch: `TryFreeGoalSpot` (unit on the way to an occupied goal) | both methods stay, **but there is now only one search** (11.2) |
| `AIUpdateInterface.Move` (`Phase5Modules.cs`) | `main`: block "through the door" (`_viaPending`); branch: block "goal occupied" | both before `PlanPath`, the door first; a unit on the way out of the factory (`_exitRallyPending`, `_viaPending`) is not asked about a free spot |
| `UpdateEnter`, the three collision queries, `SkirmishAi.PickBuilder`, test program | – | merged without conflict; `UpdateEnter` adapted to the convention of round 3 (11.3) |

### 11.2 One mechanism for the free goal spot

The exit path had already solved the same task from the same original function (`Pathfinder::adjustDestination`): `SimulationWorld.FindFreeGoal`/`GoalIsFree`
(`World/SimulationWorld.ExitGoals.cs`), spots in rows across the walking direction, next to the goal first. The branch's own spiral (`SimulationWorld.GoalSpots.cs`, §3.3) is
**deleted**. Kept is the search of `main`: on it stand the exit path, its tests, nine scenes and nine cases of the rate suite, and it already reads every unit where
its logic frame ends. `TryFreeGoalSpot` now lies in the same file and is only the **question** that the exit does not ask – is the goal of a unit that is already
on its way occupied in the meantime (a non-enemy ground unit stands there, or one with a smaller id is walking there)? – and then calls the same search from the location of the
unit, with twelve instead of four rows (83 spots: an army gathers on one point, in front of a factory door few stand). The calls of the exit path are unchanged.
The test with six units to one point passes frame-identically at 30, 60 and 120 Hz (arrival after 110 frames, the spots now lie in rows instead of on cells).

### 11.3 What was necessary for the rate suite (goal: every group 0 / 0 at 60 and 120 Hz – reached)

| Group | after the merge, before the fixes | Cause | Fix | now |
|---|---|---|---|---|
| overtime | 0 / 1 ("heal inside ChinaVehicleTroopCrawler": frames with healing 151 / 151 / 150) | `TransportContain` gave `HealthRegen%PerSec` / tick rate per tick; the slices per tick do not add up to the slice of a frame | `df639ff`: the slice of the original (one second over 30 frames), once per logic frame, counted from the tick of the entry – like `GarrisonContain` and the tunnel network; at 30 Hz nothing is stored and no hash changes | 0 / 0 (151 / 151 / 151) |
| locomotion | 1 / 1 (`Boss_InfantryAngryMobNexus`, run time 23.20 / 23.05 / 23.06 s) | mob members are sent again and again by their module to the spot of their nexus and got a "free spot" there | in the merge: a slave or mob member (`ISlavedModule`) keeps the goal that its module gives it | 0 / 0 |
| ai | 5 / 5 (America against China) | (a) `UpdateEnter` stopped an enterer whose container had just died in the middle of the frame step; (b) **not from the branch:** `PointDefenseLaserUpdate` looked at projectiles in every tick at their momentary position and shot one frame earlier at 60 Hz (first deviation: frame 5494, an Avenger shoots down a Tank Hunter rocket) | (a) in the merge: the look at the container falls into the tick that decided the frame step, reads both at the end of the frame and puts the enterer there (`SettleFrame`) before he stops or enters; (b) `3c24352`: the laser decides in the first tick of the logic frame of its carrier and reads carrier and targets at the end of the frame; unchanged at 30 Hz | 0 / 0 |

On (b): the case of the group `ai` was the same on `main` only because its AI built almost only infantry. The mechanics of `main` with **only** the army purchase of this branch (rule off)
gives 10 / 8 quantities outside the tolerance in the group (measured: export of `4d9d9e5` with `SkirmishAi*.cs` on top) – there the units still circle, and circling amplifies
every difference. `docs/rate-invariance/RATE-INVARIANCE-PROJECTILES.md` §5.4 had deliberately left the laser alone ("less than one frame earlier, no case fails"); that is no longer true
as soon as an AI builds Avengers. All ten groups: locomotion, flight, weapons, projectiles, turrets, overtime, economy (34 cases), timed, scripts, ai **0 / 0** each.

### 11.4 Measurements (30 Hz, merged state)

**Census** (16 pairings × 5 seeds, rule on; in parentheses the same state with the rule off):

| Faction | with factory | factories that build | infantry | vehicles | vehicle share |
|---|---|---|---|---|---|
| China (40) | 25 | 24 | 1,042 | 456 | 30.4 % (22.9 %) |
| ChinaTankGeneral (20) | 18 | 18 | 507 | 380 | 42.8 % (39.9 %) |
| America (40) | 36 | 36 | 1,383 | 606 | 30.5 % (29.0 %) |
| AmericaAirForceGeneral (20) | 20 | 19 | 738 | 240 | 24.5 % (31.4 %) |
| GLA (40) | 10 | 10 | 1,918 | 175 | 8.4 % (13.3 %) |

Decided 78 of 80 (rule off: 77). Circling episodes of army units: **at the rally point 0** (rule off: 1; `main` before everything: 269), near the goal elsewhere 10, on the way 40
(the two open cases from §3.5 b); gatherers at the dock 209 (open, §3.5 c). China's army: Red Guard 36 %, Tank Hunter 34 %, Dragon 20 %, Gattling 5 %, Battlemaster 4 %,
Troop Crawler 1 %. Enter commands / intakes in the 20 AI runs of the manifest: `main` 1,126 / 99, now 1,693 / 945. GLA still builds a base only on Armored Fury (10 of 40).

**Acceptance of rule D, repeated** (the same criteria, the same seeds 1–20, nothing adjusted afterwards; `composition-test`):

| Criterion | Result on the merged state |
|---|---|
| (1) paired, 640 matches | AI with the rule: **337 victories, 250 defeats**, 53 undecided; win rate among the 587 decided 57.4 %, exact 95 % interval [53.3 %, 61.4 %]; p = **0.0004** → **passed** |
| (2) symmetric, 320 matches | **305 of 320** decided (95.3 %); limit 74.0 % (253 of 320, half width 5.0 points) → **passed** |

The rule stays the default. The same state without the rule decides 299 of 320; the gap 305 against 299 lies within the scatter – what is reliable is the paired test. Per pairing
the rule wins in 9 more, in 4 equally many and in 3 fewer than it loses (Armored Fury China–GLA 15 : 21, Death Valley America–China 19 : 20, Death Valley China–GLA 19 : 21).

**60 Hz, for information** (symmetric, 320 matches, rule on): 301 decided (30 Hz: 305); mean duration of the decided ones 9.23 against 9.33 game minutes, median both 7.88;
in **297 of 320** matches the same outcome as at 30 Hz with the same seed.

### 11.5 What changes at 30 Hz compared with `main` `4d9d9e5`

`reference-manifest check --set full --jobs 5` against the manifest of `main` (not rewritten): **40 of 50 runs deviate** – the 16 two-player runs, the four
`ai8_…` runs, `skirmish-sweep` and 19 mission runs (`GC_ChinaBoss_*` ×9, `GC_LaserGeneral_ChinaNukeGeneral`, `MD_CHI01`, `MD_CHI05`, `MD_GLA01`, `MD_GLA03`, `MD_GLA05`,
`MD_USA01`, `MD_USA02`, `MD_USA05`, `Training01`). **Unchanged:** `reference-match` (winner FactionGLA at tick 14,130, 26,626 commands, 13,589 shots, end hash and chain as in the
manifest), `stress` and eight mission runs (`GC_AirGeneral_*` ×3, `GC_ChemGeneral_*` ×2, `GC_SuperWeaponsGeneral_GLADemolitionGeneral`, `GC_TankGeneral_*` ×2). Input manifest
identical. Results (`reference-manifest results`, `main` = tool of the state after the exit path, whose 30 Hz results are those of `main`):

| Run | `main` | Branch |
|---|---|---|
| Alpine Assault America–China | America @16,665 | no winner |
| Alpine Assault China–GLA | China @10,665 | China @8,595 |
| Alpine Assault ChinaTank–AirForce | AirForce @15,195 | ChinaTank @18,300 |
| Alpine Assault GLA–America | America @7,815 | America @9,660 |
| Armored Fury America–China | no winner | America @37,050 |
| Armored Fury China–GLA | China @11,595 | no winner |
| Armored Fury ChinaTank–AirForce | no winner | ChinaTank @10,860 |
| Armored Fury GLA–America | America @13,215 | America @36,180 |
| Bitter Winter America–China | no winner | China @22,005 |
| Bitter Winter China–GLA | China @7,950 | China @8,385 |
| Bitter Winter ChinaTank–AirForce | no winner | ChinaTank @30,150 |
| Bitter Winter GLA–America | America @9,090 | America @8,730 |
| Death Valley America–China | America @9,390 | America @8,700 |
| Death Valley China–GLA | China @10,515 | China @12,930 |
| Death Valley ChinaTank–AirForce | AirForce @10,635 | ChinaTank @22,860 |
| Death Valley GLA–America | America @8,415 | America @10,530 |

Decided 12 → **14** of 16, the outcome is different in 8. The four 8-player runs end as before without a winner within their budget. Missions: in all 27 runs **the same
outcome and the same end script**; 18 with identical result quantities, 9 with different event counts (most clearly `MD_USA02`: 72 → 143 damage events, 39 → 63 shots;
`MD_GLA01`: `MoveArrived` 53 → 81; in three of them also the number of executed scripts or actions: `GC_LaserGeneral_ChinaNukeGeneral` 122 → 123 scripts, `MD_GLA05` 102 → 100,
`MD_USA05` 396 → 394 actions).

**Campaign** (30 Hz): `CampaignTool drive` on all 24 maps **victory 24 / 24, defeat 24 / 24**; `audit --ticks 900`: 24 of 24 identical on the second run and after
save/load, 0 hits on missing types, end probes victory 13 / 24, defeat 15 / 24 – like `main`.

### 11.6 `MD_CHI02` in commands-only mode

Cause found, and it does **not** lie in the saving rule. The agent marches on the goal that lies nearest to its army; its path ends 120 units before one that
it cannot get at, and because it stands there, that one stays the nearest – for the rest of the mission ("1 attack wave, 60,645 ticks in the attack", 241 units in one place). The
run on `main` did the same for 22,000 ticks until it lost, within 50 seconds, 107 of its 130 infantrymen gathered there (to what is not investigated); the wave was
reset, the next one set out from the base to the west and won at tick 29,250. The army that saves for tanks does not lose its mass there (its number only grows)
and is never reset. The victory on `main` followed from a defeat. Times until barracks and first attack do not distinguish the runs: both attack from the start
(wave 1 in the first 500 ticks).

Fix (`05b2c1f`, only for an agent with mission goals; a skirmish sets none): a goal on which the army has marched for two minutes and which is still standing goes
behind all others until each has had its turn. With that the army moves through all four enemy bases.

| `--commands-only`, `--minutes 34` | `MD_GLA04` | `MD_CHI01` | `MD_CHI02` |
|---|---|---|---|
| `main` (before the branch) | victory @1,022 | victory @18,252 | victory @29,250 |
| merged state | victory @1,022 | victory @16,452 | **not reached**; after 34 minutes two of the four bases are only GLA holes any more |
| merged state, `--minutes 60` | – | – | **victory @78,429** (43:34) |

So `MD_CHI02` is won again, but no longer within 34 game minutes. Why it takes 14 minutes longer than the lucky run on `main` is not investigated further (the
holes of the GLA rebuild themselves while the army is at the next goal).

### 11.7 Verification (merged state)

| Check | Result |
|---|---|
| `dotnet build CnCNew.slnx` | 0 warnings, 0 errors |
| `Simulation.Tests` (Release) without `--content-dir` at 30, 60, 120 Hz | each "All simulation core tests passed" |
| `Simulation.Tests` with `--content-dir` | 30 Hz: all passed. 60 and 120 Hz: **2 of 540 fail** – "a Crusader keeps its turret on a Battlemaster it chases" (3.00° worst value) and "the original skirmish AI … Player 2 has built a base" (4 buildings after 150 s). **Both fail in exactly the same way on `main` `4d9d9e5` alone** (the same text, the same numbers; re-measured on an export of `main`); not from this branch, not fixed |
| Rate suite | all ten groups 0 / 0 at 60 and 120 Hz (11.3) |
| own tests | six units to one point, three enterers, army order, composition: green at all three rates |
| `reference-manifest check --set quick --verify-incremental` | 64,871 ticks checked, **0 deviations** of the incremental hash |
| `input-manifest check` | identical |
| `Game.Tests` (Release, `--content-dir`) | "All game tests passed" |
| `scripts/check-spectator.ps1` | PASS |
| `scripts/audit-scenarios.ps1 -Scenario exits` | 9 of 9 scenes "all checks passed", at 30 Hz and with `-TickRate 60` (the script runs nine scenes: three factions × command center, barracks, factory) |

Not run: the full manifest run under Linux, the determinism matrix, `reachability`, the three scene audits other than `exits`, the acceptance of the rule at 60 Hz (only the
symmetric survey), `drive` and `audit` at 60 Hz. No human has seen any of it.
