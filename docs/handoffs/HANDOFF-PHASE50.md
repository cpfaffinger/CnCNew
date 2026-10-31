> **Stage:** 1 – Port (fidelity package L) · **Phase:** 50
> **Status:** next (starting state: the end of Phase 49, see below)
> **Predecessor:** HANDOFF-PHASE49.md · **Results in:** docs/SIMULATION-CORE.md §64, docs/plan/STAGE-1-PORT.md §3
> **Side items:** the special abilities that are shells (F-83, `SpecialAbilityUpdate::triggerAbilityEffect`), then the approximations of target acquisition (F-17, `docs/SIMULATION-CORE.md` §33.5)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 50): the building site of the original (F-18, `BuildAssistant::isLocationLegalToBuild`).

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 48" and "Phase 49"), `docs/plan/STAGE-1-PORT.md` §3 (F-17, F-18), `docs/SIMULATION-CORE.md` §43 (the frame
convention), §33.5, §63. Prerequisite: Phase 49.

## Goal in one sentence

A building site is legal by the rules of the original and refused for its reasons - flatness, the map's edge, the border around supplies, objects in the way, a clear path, the shroud - and
friendly units make room for a building that is begun; each rule a cause with a test that fails on the code before at 30, 60 and 120 Hz and a manifest commit per cause.

## Starting point (measured, after Phase 49 – re-measure when this phase starts)

State `10f56f6` (Phase 49 merged into `master`). Tier 4 with 56 of 56 steps ok at `10f56f6` (6,892 s), so this phase begins without another. Reference values: the retail
reference match (driver seed `5EED0004`) FactionGLA at tick 36,540 (original frame 18,270; end tick 37,141), end hash `3E41F7F2A4F4D9A5`, chain `938C7502C5E562FF`, 22,445 commands; the stress
test `9EE9F730D124C3BD` / `FD8E3547C802ACA2`; 15 of 16 AI pairings decided; every mission run keeps its outcome. Versions: `CommandTypes.Version` 20, replay 18, `WorldSnapshot` 10,
`MissionSnapshot` 3, `SaveGame` 3, match description 3, session state 3, protocol 6, transport 2; compatibility baseline 699 entries, the patch layer's 692. All sixteen pairings of the manifest
are the same game at 30 and 60 Hz to their endsand at 30 and 120 Hz. Not run at the end of Phase 49 (selective by the owner's rule): `scripts/perf-profile.ps1`, the clean room, the campaign checks.

## What the port does today

`CheckConstruct` asks `NavigationGrid.IsFootprintLegal` (no cliff, no water, no obstacle under the footprint) and `IsPositionExploredBy` at the centre, and answers `InvalidPlacement` for all of it;
the built-in AI and the original AI's library search their spots with `IsFootprintLegal` alone.

## What the original does (read, not yet built)

`DozerAIUpdate::construct` asks `isLocationLegalToBuild` with `TERRAIN_RESTRICTIONS | CLEAR_PATH | NO_OBJECT_OVERLAP | SHROUD_REVEALED`, the client adds `USE_QUICK_PATHFIND` and `IGNORE_STEALTHED`:

- off the pathfind extent: `LBC_RESTRICTED_TERRAIN`, whatever the options;
- `SHROUD_REVEALED`: the cell of the centre must be clear (`LBC_SHROUD`), first of all, "since returning other errors for shrouded areas could be used to game the system";
- `NO_OBJECT_OVERLAP` (`isLocationClearOfObjects`): allies that use an ability or are busy block; shrubbery, `CLEARED_BY_BUILD`, rubble and the dead are removable; mines and inert objects are ignored;
  an immobile object blocks, a disabled one blocks, an enemy blocks; a stealthed object not seen is passed with `IGNORE_STEALTHED`;
- `CANNOT_BUILD_NEAR_SUPPLIES` with `SupplyBuildBorder` (20): the footprint must not collide with the closest `SUPPLY_SOURCE`'s geometry grown by the border (`LBC_TOO_CLOSE_TO_SUPPLIES`);
- `CLEAR_PATH`: the builder needs an AI and a quick path (`LBC_NO_CLEAR_PATH`);
- `TERRAIN_RESTRICTIONS`: not on a bridge; `iterateFootprint` samples the footprint at three cells and then at one cell (`checkSampleBuildLocation`): water, cliff, impassable cells and
  `MinDistFromEdgeOfMapForBuild` (30) restrict, and the highest and lowest sample may differ by `AllowedHeightVariationForBuilding` (10) at most (`LBC_NOT_FLAT_ENOUGH`).

`BuildAssistant::buildObjectNow` then calls `moveObjectsForConstruction`: the footprint's neutral and allied units move to a place drawn around the site (two draws of the logic stream per unit:
a distance of 0.5 to 1.5 times the radius, a direction), what has no AI blocks the building (`anyUnmovables`), and removable objects are destroyed.

## Decisions that are fixed

- As before: the original profile keeps the original's behaviour; a fix of the original's logic is offered behind the rule profile only, written anew; D-32 stays the owner's.
- The keys `SupplyBuildBorder`, `AllowedHeightVariationForBuilding` and `MinDistFromEdgeOfMapForBuild` of `GameData` become typed; the compatibility baselines lose their entries in the commit that
  reads them.
- The computer players must search their spots with the same rule the command answers with, or they send orders that are refused.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 49 did not end with it on the same code).
2. The reasons: one answer of the world for a site (`LegalBuildCode`), the client's texts for each.
3. Terrain: flatness, the map's edge, the two sample resolutions; both AIs search with it.
4. The supply border.
5. Objects in the way, the removable ones, and units that make room.
6. The clear path, the shroud.
7. Side items as named above; every rate difference a change opens is compared with `rate-compare --map-diff ... --fields --frames 50000 --persist 10 --ignore '\.nextWake$|\.previousHealth$|constructionProgress|DynamicShroudClearingRange'` over the sixteen pairings.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32, D-34, the built-in AI's army orders (Stage 3).

## Acceptance

By machine: each cause built with a test as above (what does not fit is named with its reason); both manifests of the original profile unchanged except by a cause with its own manifest
commit; the checks the changes can reach green.

## Progress

- F-18 (§64.1): the terrain `b247a21`, the supplies `2f17c00`, the objects `d0cd821`, the sight and the path `75daa55`, the client's reasons `6407cd2`.
- F-83 (§64.2): the charges `82bb3c9`, the hacks and the laser `9b402ec`, the Helix's bomb `c7eb261`, the disguise `de827ac`, its drawing `e540cbd`.
- Found by the real data and the rate comparison: F-84 (§64.3, `08c7049`), F-85 (§64.4, `427c724`), F-86 (§64.5, `2293c97`). Carried into `HANDOFF-PHASE51.md`: F-17. Closing measurements: `CLAUDE.md`, paragraph "Phase 50".
