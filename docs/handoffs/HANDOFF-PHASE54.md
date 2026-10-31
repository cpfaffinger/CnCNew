> **Stage:** 1 – Port · **Milestone:** S1-M11 · **Phase:** 54
> **Status:** next (starting state: the end of Phase 53)
> **Predecessor:** HANDOFF-PHASE53.md · **Results in:** `docs/SIMULATION-CORE.md`, `docs/CLIENT-ROADS.md`, new manifests
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 54): terrain bridges (F-20).

You work autonomously on this repository. First read `CLAUDE.md` (paragraph "Phase 53"), `docs/plan/PROGRAMME.md` §7, `docs/CLIENT-ROADS.md`.

## Goal in one sentence

19 of the 64 skirmish maps have bridges nothing can cross: a bridge of a map becomes a walkable deck with towers, damage, repair and its model, as `TerrainLogic`, `AIPathfind` and `W3DBridgeBuffer` have it.

## Starting point (measured, after Phase 53 - re-measure when this phase starts)

Retail reference match a stalemate at tick 108,000, `556023EBE5E461C2` / `2B71E89699445661`; stress test `9EE9F730D124C3BD` / `5611A72EDB7D52F2`; 14 of 16 pairings decided; compatibility baseline 670; protocol 7.

## Brief (in this order)

1. The `Bridge` blocks typed. 2. The bridge objects of a map, the deck's height, the walkable layer of the navigation. 3. Destruction and repair. 4. The mesh. 5. Tests at 30, 60 and 120 Hz, a real-data crossing. 6. The manifests, each with its cause. Then the rest of the register: the recorded script actions (F-22), F-19, F-21, F-23 to F-25, F-30, F-33, F-88; Bink (F-02); the server on Ubuntu (D-20); the clean room with a real download; `scripts/perf-profile.ps1`.

## Acceptance

A unit crosses a shipped map's bridge; a broken bridge closes the way and a repaired one opens it; every run of the manifests that changes names its cause; tier 4.
