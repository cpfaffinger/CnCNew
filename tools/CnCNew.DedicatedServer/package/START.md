# CnCNew dedicated server - start guide

This directory is a self-contained package: it brings its own .NET runtime and needs none on the machine. The server is a relay for multiplayer
matches of CnCNew. It holds no game data, simulates nothing and opens one UDP port. The full description is `docs/DEDICATED-SERVER.md` in the
repository (https://github.com/cpfaffinger/CnCNew); the licence is in `LICENSE.md` beside this file.

## What the machine needs

- **Windows x64** (package `win-x64`): Windows 10 or later. Nothing else.
- **Linux x64** (package `linux-x64`): a distribution with glibc 2.31 or later and OpenSSL 3 or 1.1 (`libssl`) - the cryptography of the
  connection uses it. No ICU library is needed. Make the program executable once: `chmod +x CnCNew.DedicatedServer`.
- One UDP port reachable from the players (default 27960). No TCP port, no outgoing connection.
- Per room of eight: see the capacity table in `docs/DEDICATED-SERVER.md` section 11 - it was measured on one machine and is a starting point,
  not a promise.

## First start

1. Copy `server.sample.json` to `server.json` and read it through. The defaults are the limits the project documents.
2. Start it:
   - Windows: `CnCNew.DedicatedServer.exe --config server.json`
   - Linux: `./CnCNew.DedicatedServer --config server.json`
3. The first line it writes is `event=listening ... key=<64 hex digits>`. That is the fingerprint of the server's permanent key (the file named
   by `keyFile`, made on this first start). **Publish the fingerprint together with the address.** A client that is told the fingerprint
   (`--server-key`) takes no other server; a client that is not remembers the key of its first meeting and refuses the server if the key ever
   changes.
4. Open the UDP port in the firewall of the machine and of the network.

Every line is `ts=<ms> event=<name> key=value ...` on standard output, and in one file per day under `logDir`. Stop the server with Ctrl+C
(SIGINT) or SIGTERM: everybody who is connected is told at once that the connection ends. The recording of a match that was still running is
not written - stop the server when its `status` line shows `rooms=0` if the replays matter.

## What to keep, and what to keep private

- `server-key.pem` (the `keyFile`): whoever has it can pretend to be this server. Readable for the server's account only. If it is lost, the
  server makes a new one, and every client that remembered the old key will refuse the server until its user removes the entry - say so to
  your players before you replace a key on purpose.
- Logs hold addresses as their network (`a.b.c.0/24`) unless `logFullAddresses` is on. An address is personal data: keep full addresses only
  if you need them and for as long as you need them (`logRetentionDays`).
- Replay files hold the orders of the players and the names of the factions, no addresses.

## Running it as a service

Linux, systemd (a unit file to adapt):

```
[Unit]
Description=CnCNew dedicated server
After=network-online.target

[Service]
User=cncnew
WorkingDirectory=/opt/cncnew-server
ExecStart=/opt/cncnew-server/CnCNew.DedicatedServer --config /opt/cncnew-server/server.json
Restart=on-failure
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=/opt/cncnew-server
LimitNOFILE=4096

[Install]
WantedBy=multi-user.target
```

Windows: run it under an account without administrator rights, for example as a scheduled task "at startup" or with a service wrapper of your
choice; the program itself needs no rights beyond its own directory and its port.

## Versions

Players and server must run the same protocol and transport version; players of one room must run the same build. The server says both on its
first line (`protocol=`, `transport=`, `build=`). Update the server and tell your players which client build goes with it; a client of another
protocol version is turned away with a message that names both versions (docs/DEDICATED-SERVER.md section 12).

## Removing somebody

- The host of a room can remove a member of that room.
- Add an address or a network (`203.0.113.7`, `203.0.113.0/24`, `2001:db8::/48`) to the `banFile`, one per line; the server reads the file
  again within seconds, closes the connections from there and answers nothing from there any more.
