> **Stage:** 3 – Changes · **Milestone:** S3-M7 Bug-fix and balance layers (a second source: Zero Hour Reforged) · **Phase:** 40
> **Status:** next (starting state: the end of Phase 39, see below)
> **Predecessor:** HANDOFF-PHASE39.md · **Results in:** docs/REFORGED-FIXES.md, docs/GGC-FIXES.md (the rule profile), docs/SIMULATION-CORE.md, docs/plan/STAGE-1-PORT.md §3
> **Side items:** the retail reference match's driver seed (it ends in a stalemate since F-44), F-48 to F-58 (found in the first reading), the Phase 39 leftovers named below
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 40): the fixes of Zero Hour Reforged reviewed against the original and this port - each candidate of `docs/REFORGED-FIXES.md` §3 decided (kind A to
E there), the defects of this port fixed in the original profile, the defects of the original adopted behind the rule profile.

You work autonomously on this repository. First read `CLAUDE.md` (paragraphs "Phase 32", "Phase 33" and "Phase 39"), `docs/plan/PROGRAMME.md` (§7, D-32),
`docs/GGC-FIXES.md` (the method of the first review: catalogue, rule profile, one fix per `GameFix`, a test under both profiles at 30, 60 and 120 Hz), the work list
`docs/REFORGED-FIXES.md`, and `docs/SIMULATION-CORE.md` §53. Prerequisite: Phase 39.

## Goal in one sentence

Every candidate R-01 to R-38 of `docs/REFORGED-FIXES.md` is decided with its reason from the original's source and this port's code; a defect of this port is fixed in
the original profile (its own manifest commit), a defect of the original is a `GameFix` behind the rule profile (no manifest commit), the rest is named.

## Starting point (measured, after Phase 39 – re-measure when this phase starts)

Filled in at the end of Phase 39 (the state, the reference values, the tier that ran).

## Decisions that are fixed

- The original profile keeps the original's behaviour, including what Reforged calls EA's bugs (Stage 1). A fix of the original's logic is offered behind the rule
  profile only, written anew, with the original's source places and Reforged's changelog entry as its reference - never Reforged's code.
- Reforged's balance, AI, interface, rendering and sight changes are kind D: not adopted; an idea Stage 3 wants becomes an option of its own design.
- D-32 (which fixes a release offers) stays the owner's.

## Brief (in this order)

1. Reproduce the starting state (selective checks, `docs/TESTING.md`; tier 4 only if Phase 39 did not end with it on the same code).
2. Fetch Reforged at a fixed commit into `references/` (extend `scripts/get-references.ps1`, as for the other four sources; never commit it), so every decision names
   the commit it read.
3. For each candidate: read the original's place, read Reforged's change, read this port's place; decide A to E and write the reason into `docs/REFORGED-FIXES.md`.
4. Kind C (the port differs from the original): a fidelity entry in `docs/plan/STAGE-1-PORT.md` §3, a test that fails before the fix at 30, 60 and 120 Hz, an
   implementation commit and a manifest commit per cause.
5. Kind B worth offering: a `GameFix` with its name, a test under both profiles at 30, 60 and 120 Hz; the original profile's manifests must not move.
6. The kind-C findings already entered: F-48 (R-17: weapon damage reaches `HighlanderBody`/`UndeadBody` already armour-adjusted - the original caps the raw amount and
   applies the armour in `ActiveBody::attemptDamage`) and F-49 (R-06: `Weapon::estimateWeaponDamage` refuses `DAMAGE_SNIPER` at a structure with a contain module and
   nobody inside) - each with its test, implementation and manifest commit as in step 4.
   The first reading at the end of Phase 39 found nine more, each registered in `docs/plan/STAGE-1-PORT.md` §3 with its source places: F-50 (no radar per player),
   F-51 (riders of a transport that cannot let them out survive its death), F-52 (an object's last damage is never recorded - stealth `TAKING_DAMAGE`, the original
   AI's `SKIRMISH_SUPPLY_SOURCE_ATTACKED`), F-53 (the landed Battle Bus hulk is not held), F-54 (no stealth garrison), F-55 (no attack line of sight - 245 objects need
   one; a large cause), F-56 (`Object::onDisabledEdge`: an EMP'd power plant keeps powering its base) F-57 (the strategy center's gun fires under every plan) and F-58
   (guarding is no state of the simulation, only a client's standing order).
   Order them by what a player meets first (F-52, F-56, F-57, F-51, F-53 are small; F-50, F-54, F-55, F-58 are causes of their own size) and name what does not fit into the phase.
7. The retail reference match: since F-44 (a dying producer refunds its queue) it runs to its limit of 432,000 ticks without a winner - deterministic, but the longest run of
   the manifest and no longer a match that ends. Survey driver seeds with `SimTool reference-match --driver-seed HEX --record-only` as Phase 22 did, choose one that ends,
   and change it in a commit of its own (cause: the seed, after F-44); if no seed ends within the limit, name that and keep the stalemate.
8. Phase 39 leftovers: F-06 as a `GameFix` candidate (R-36); the reskin equivalence of `QuantityModifier` (not needed by shipped data); the Game.Tests pictures of real
   matches after every cause (`CNCNEW_UPDATE_BASELINES=1`; an original-skin file may change only in the pins of real matches).
9. Documents, the next brief.

## Out of scope

Reforged's own designs (kind D), every judgement of a human, D-32.

## Acceptance

By machine: every candidate decided with its reason; fixes with tests as above; both manifests of the original profile unchanged except by a kind-C cause with its own
manifest commit; the checks the changes can reach green.
