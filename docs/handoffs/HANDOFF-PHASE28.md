> **Stage:** 4 – Multiplayer · **Milestone:** S4-M4 Server operation and security · **Phase:** 28
> **Status:** planned
> **Predecessor:** HANDOFF-PHASE27.md · **Successor:** HANDOFF-PHASE29.md · **Results in:** docs/DEDICATED-SERVER.md, docs/NETWORK-PROTOCOL.md
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 28): Server operation and security – open port, abuse limits, encryption, package, sustained load

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md`, `docs/NETWORK-DIAGNOSTICS.md` and the final report of Phase 27.
Prerequisite: Phase 27 is completed.

## Honest status (starting point)

The dedicated server works under benevolent conditions. But a program with an open UDP port on the internet is addressed by strangers before the first player arrives. So far there is: a cookie handshake and a strict parser (Phase 26). What is missing: any limit against overload, any authentication of the packets after the handshake, any protection of a room password, a package that someone can start without a development environment, and any statement about how many rooms a server carries.

## Decisions that are fixed

- **No anti-cheat against map reveal.** In the lockstep model every client knows the full state; that is a documented limit of the model (`docs/MULTIPLAYER-PLAN.md` §2), not a task of this phase. What the server can provide: no client can send commands for another player (the server assigns the player ID per connection), the server determines the game speed, every simulation rejects invalid commands in the same way, modified state shows up as a desync.
- **BCL cryptography only** (`ECDiffieHellman`, `AesGcm` or `ChaCha20Poly1305`, `HMACSHA256`). No cryptography of our own, no new dependency.
- **The server distributes no original data** and no maps. A room names map and fingerprints; whoever does not have them cannot join.

## Brief (in this order)

1. **Reproduce the starting state**, re-measure the reference values of Phase 27.
2. **Write the threat model** (`docs/DEDICATED-SERVER.md`, its own section): attackers without a connection (flood, forged senders, amplification), attackers with a connection (message flood, oversized or malformed messages, lobby abuse, deliberate stalling), eavesdroppers on the network. Per line: measure, test, residual risk.
3. **Limits and throttling.** Packets per second and sender before the handshake; messages and bytes per second per connection; command intents per tick and player; maximum message and fragment sizes; connections per address; rooms and players per server; memory cap per room (bundle buffer for spectators and rejoin). Every limit is configuration with a documented default; every violation is counted and leads to a defined reaction (discard, disconnect), never to unbounded memory or computing effort.
4. **Fuzzing of the parser.** A test program (`tests/CnCNew.Net.Tests`, suite `Fuzz`) sends random and deliberately corrupted packets generated from a seed (truncated, length fields above the limit, swapped types, old sequences, repetitions) against the server and client parser: no exception leaves the parser, no state changes because of an invalid packet, memory does not grow. A corpus of the cases found remains as a regression test.
5. **Authenticated and encrypted connection.** Session key per connection from an ECDH exchange in the handshake; all packets after that with AEAD (sequence number as nonce, protection against replay). Room password only as challenge-response (`HMACSHA256` over a server nonce), never in clear text. The server owns a permanent key pair; the client shows its fingerprint on first contact and remembers it (trust on first use). Document honestly what this protects against (eavesdropping, forging from outside the path) and what it does not (active attacker at the very first contact).
6. **Administration.** Configuration file, ban list (address/key), room password, kicking by the room creator; log as structured lines with configurable retention. IP addresses are personal data: the default is truncated storage, the docs say so.
7. **Package.** `dotnet publish` of the server self-contained for `win-x64` and `linux-x64`; the server has no Windows or Stride dependency (test via the project references). Start and configuration guide in `docs/DEDICATED-SERVER.md`. Prove the Linux run (WSL or a container is enough; do not claim it if it did not take place).
8. **Capacity and sustained load.** Headless clients (command generator from a seed) fill N rooms of eight players each: measure CPU, memory and bandwidth per room, from that a capacity table; 24-hour run with constantly restarting matches, disconnects and rejoins: no memory growth, no crash, no hanging room. The run needs a bounded timeout and intermediate results (see `CLAUDE.md`, background runs).
9. **Version rule in operation.** What must match when joining (protocol version, `CommandTypes.Version`, replay format, simulation version from Phase 25, definition and map fingerprint) and how the client explains a deviation to the human is in the docs; the server never offers a "join anyway" path.
10. **Docs:** `docs/DEDICATED-SERVER.md`, `docs/NETWORK-PROTOCOL.md`, `docs/MULTIPLAYER-PLAN.md`, README, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md`; extend `scripts/audit-vulnerable.ps1` by the server project.

## Bottlenecks and showstoppers of this phase

- **Cost of the encryption** per packet with eight clients and 60 bundles per second: measure, do not assume.
- **Nonce reuse** on retransmission: every transmission needs a fresh packet sequence, even if the content is the same (test).
- **Time limits as attack surface:** a client that stays exactly below every limit must not hold up a room permanently (test "maliciously slow client" against the rule from Phase 26).

## Out of scope

Ranking, accounts, matchmaking, central server list, NAT traversal (clients connect outbound to a server with an open port), kernel anti-cheat. Whoever wants that decides it as a scope change of its own.

## Acceptance

The fuzz run finds no crash and no memory growth; the limits demonstrably take effect; the connection is authenticated and encrypted; the server runs as a standalone package under Windows and Linux; the capacity table and the 24-hour run are measured. The final report names numbers with commit hashes, the residual risk per line of the threat model and what is not verified. At the end check `docs/handoffs/HANDOFF-PHASE29.md` and update the "starting point".
