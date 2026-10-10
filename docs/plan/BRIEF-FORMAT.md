# The format of a phase brief

Binding from 10 October 2026 for every new brief in `docs/handoffs/` and, as a header, for every existing one. A brief is
the self-contained working instruction of one phase: whoever reads it (a coding agent, a human) must be able to begin
without the conversation that produced it. The plan it belongs to is [PROGRAMME.md](PROGRAMME.md); the index of all
briefs is [../handoffs/README.md](../handoffs/README.md).

## 1. File and header

File: `docs/handoffs/HANDOFF-PHASE<N>.md`, `N` the phase number (continuing; the next free number is 31). One brief per
phase; a phase belongs to exactly one milestone of one stage as its main subject, and may carry side items of other
stages (named as such). Line endings: LF (every existing brief except `HANDOFF-PHASE4.md`, which is CRLF and stays so).

The first lines are the **header block**, a blockquote with these fields in this order:

```markdown
> **Stage:** 2 – Engine · **Milestone:** S2-M7 Session core · **Phase:** 24
> **Status:** current (starting state measured 10 October 2026 at `8588905`; not begun)
> **Predecessor:** HANDOFF-PHASE23.md · **Successor:** HANDOFF-PHASE25.md · **Results in:** docs/SIMULATION-CORE.md, …
```

- **Status** uses the words of PROGRAMME.md §1: `done (<date>)`, `built – not accepted`, `in progress`, `current`,
  `planned`, `superseded by <file>`, `ended without its acceptance goal`. A date or commit is given where one exists.
- **Results in** names the subject documents that carry the measured result; for a planned brief the documents it will
  write.
- Side items of other stages are named in the header: `**Side items:** S3-M2 spectator mode, S1-M13 original AI`.

## 2. Sections of a new brief

In this order; a section that does not apply says so in one line rather than being left out.

1. **Title line:** `N+1 handoff (Phase N): <topic in one line>`, then the standard sentence "You work autonomously on this
   repository" with the documents to read first and the prerequisite phase.
2. **Goal in one sentence.**
3. **Starting point (measured):** the state at the start, every number with date, commit and the command that produced
   it; what the previous phase left open; the tier that ran. This section is **continued, not rewritten**, at the end of
   the previous phase (rule 9 of `docs/MULTIPLAYER-PLAN.md` §6).
4. **What is already done – do not build twice:** everything of this milestone that exists, with the place in the code
   and the document (PROGRAMME.md §4 lists the pieces built in other stages' phases).
5. **Decisions that are fixed:** the decisions this brief rests on, with their source (PROGRAMME.md §7, the stage
   document, `docs/MULTIPLAYER-PLAN.md` §2); open decisions with the default the brief uses.
6. **Brief (in this order):** the numbered work items; each with its test, measurement and document.
7. **Bottlenecks and showstoppers of this phase.**
8. **Out of scope:** what is deliberately left to another phase or milestone.
9. **Non-negotiable rules:** `CLAUDE.md`, `docs/TESTING.md`, the rules of the stage; anything specific to the phase.
10. **Acceptance:** what must be true at the end, in terms a tool or a human can check; what only a human can accept is
    named as such.
11. **Final report and N+1:** what the report contains (numbers with commit hashes, the tier that ran, what was not run,
    what no human has seen), and the instruction to update the next brief's "Starting point", the stage document's
    milestone status, the fidelity register and the decision register.

## 3. Rules

- **Measured, not adopted.** No number in a brief counts as measured; the first step of every phase reproduces the
  starting state (`CLAUDE.md`, "Procedure of a phase").
- **Continue, do not rewrite.** A brief that exists is updated in its "Starting point" and gets addenda with a date; its
  work items are not rewritten (deviations from the plan are named in the addendum and in PROGRAMME.md §3). When addenda
  pile up, they are consolidated into one dated "Starting point" that keeps every fact – as was done for
  `HANDOFF-PHASE24.md` on 10 October 2026.
- **History stays.** Worked-off briefs keep their text as documents of their time (their numbers are outdated by
  definition); only the header block is added.
- **Every item has a stage.** The final report books every item to a stage and milestone; an item that changed the
  behaviour of the simulation names its cause and its manifest commit.
- **A brief is self-contained.** It names every document to read and every command to run; it does not assume the
  conversation that wrote it.

## 4. Status vocabulary (the same everywhere)

| Word | Meaning |
|---|---|
| done | built, measured, and accepted where only a human can accept |
| built – not accepted | built and measured headless; what only a human can judge is outstanding |
| in progress | a phase is working on it |
| current | the phase to be worked on next (exactly one at a time) |
| planned | a brief exists, no work has begun |
| open | defined in a stage document, no brief yet |
| decision | waits for the project owner (PROGRAMME.md §7) |
| limit | named, not planned to change |
| superseded | replaced by a later brief; kept as history |
