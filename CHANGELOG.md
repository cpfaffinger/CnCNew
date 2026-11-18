# Changelog

All notable changes of CnCNew releases are listed here, newest first. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
the versions follow [Semantic Versioning](https://semver.org/). Entries are made by `scripts/release.ps1` from the Conventional Commit subjects.

## [0.0.1] - 2026-11-18

The first public release: a preview for people who want to try CnCNew and report what they find. It is young software in test - read
"Status - read this first" in the README before you install it.

### How to install

1. Download `CnCNew-Launcher-0.0.1.exe` below (the `.sha256` file beside it holds its checksum).
2. Run it. It finds your Steam installation of *Command & Conquer Generals - Zero Hour* (English, Steam app 2732960), checks it file by file, and makes a new game
   folder in a folder you choose. Your Steam folder is only read, never written. The launcher downloads the client and the community patch GeneralsGamePatch
   (about 100 MB, every file checked against its recorded hash).
3. Play. The launcher starts the game and offers updates when a newer release exists.

You need your own copy of the game: CnCNew contains no game data. Windows 10 or 11, 64-bit, Direct3D 11; no .NET installation needed.

### What is in it

- Skirmish against computer players (the built-in AI with tactics profiles and three strengths, or the original skirmish AI) and the campaign missions,
  including the Generals' Challenge, on the original's data and map scripts.
- The community patch GeneralsGamePatch and a set of reviewed GeneralsGameCode fixes as the one product.
- A deterministic simulation at 60 Hz (30 and 120 Hz selectable).
- A modern user interface beside the original skin: rebindable hotkeys, selection markers, health bars, control bar opacity, windowed fullscreen.
- Multiplayer skirmishes for up to eight players through dedicated servers (encrypted, with watchers, rejoin, votes); the server packages for Windows and
  Linux are part of this release.
- A replay browser, an observer with an event ticker and player views, and a match analysis with charts and an HTML report.
- Your own maps from the game's user map folder, with their `map.ini` rules and scripts.

### Known limits

- Played and judged by the project owner in several playtests and checked by machine; not yet played by a wider public, not on a second machine over a
  real network.
- Only the English Steam version of Zero Hour is supported; other versions and languages are refused with a message.
- Open findings are the GitHub issues of this repository; please report what you find there (what you did, what you saw, the build shown in the log).

### Assets

| File | What it is |
|---|---|
| `CnCNew-Launcher-0.0.1.exe` | The installer and launcher for players - start here. |
| `CnCNew-Client-0.0.1-win-x64.zip` | The game client the launcher installs (for a manual installation). |
| `CnCNew-Server-0.0.1-win-x64.zip` | The dedicated server for Windows. |
| `CnCNew-Server-0.0.1-linux-x64.zip` | The dedicated server for Linux. |
