# N+1-Handoff: Phase 9 abschließen – Skirmish, Shell-Anbindung, Durchspielbarkeit der Missionen

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses). Lies zuerst `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`,
`docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§9 „Status“ und „Bekannte Grenzen“), **`docs/SCRIPT-ENGINE.md`**, **`docs/CAMPAIGN-AND-SAVEGAMES.md`**,
`docs/MAP-SCRIPTS.md`, `docs/SIMULATION-CORE.md` (§12, §19–§22), `docs/GAME-RULES.md` §10/§11, `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`,
`docs/PRESENTATION-INPUT.md`. `docs/handoffs/HANDOFF-PHASE9.md` bleibt als Verlauf; seine Teile A2–A5 und B11/B12 sind erledigt, seine Zahlen veraltet.

Dieser Auftrag setzt Phase 9 (`IMPLEMENTATION-PLAN.md` §9) fort. **Phase 9 ist noch nicht abgeschlossen:** die Abnahme verlangt „alle Skirmish-Karten start-
und beendbar, jede Kampagnenmission besteht ihre wesentlichen Erfolgs- und Fehlerpfade, Speichern/Laden setzt einen identischen Simulationszustand fort“.
Der dritte Teil ist für Missionen belegt, die beiden anderen sind es nicht.

## Ehrlicher Status (Ausgangslage, Stand 1. Oktober 2026)

**Fertig und gemessen** (Details, Quellverweise und Zahlen in den beiden neuen Dokumenten):

- Deterministische **Skript-VM** in `CnCNew.Simulation/Scripting` (Bedingungen 63/109, Aktionen 69 ausgeführt + 129 weitergereicht von 344; der Rest wird zur
  Laufzeit gezählt, nie still übergangen), **Missionssitzung** (`MissionSession`) mit Seiten/Beziehungen/platzierten Objekten/KI-Baulisten/Startbasis,
  speicherbarer Zustand (`MissionSnapshot` v1 auf `WorldSnapshot` v7).
- **Kampagnenlogik** (`CampaignCatalog`, `CampaignProgress`, `CampaignController`, `MissionRun`) und **Savegames** (`SaveGame` v1, `SaveGameStore`) in
  `CnCNew.Game`; **`CnCNew.CampaignTool`** (`list`, `audit`, `playthrough`) und `SimTool mission-run`.
- 24 eindeutige Kampagnenkarten: 24/24 deterministisch, 24/24 identisch nach Speichern/Wiederherstellen; Endproben durch Ausschalten Sieg 10/24,
  Niederlage 15/24; `playthrough --campaign CHALLENGE_0` spielt fünf Missionen mit Speichern/Laden je Mission, bei `GC_LaserGeneral` (keine menschliche Seite
  auf der Karte) bleibt die Probe stehen.
- `CommandButtonHuntUpdate` und `LockWeaponCreate` als ehrliche Hüllenmodule; 1.494 von 2.115 Objekten instanziierbar; Baseline 747.

**Nicht erledigt (Auftrag dieser Phase):**

1. **Teil A Punkt 1 – Skirmish abschließen** (unberührt): Setup/Startpositionen/Teams/Regeln/KI-Profile/Statistik; „alle Skirmish-Karten start- und beendbar“
   ist nicht gemessen (`MapTool audit`: 75 von 150 Karten haben keine Startwegpunkte, 462 Startpaare, 456 vollständig begehbar). Die 16-Karten-KI-Übersicht in
   `SIMULATION-CORE.md` §19 ist seit dem `SharedReadyFrame`-Fix nicht neu vermessen. **Ein Skirmish-Speicherstand ist nicht möglich** (die Skirmish-KI hält
   Zustand außerhalb der Welt); entscheide, ob `SkirmishAi` serialisierbar wird oder Skirmish ausdrücklich ohne Speichern bleibt.
2. **Shell-Anbindung** (`CnCNew.Game/Shell`, Stride-UI): „Kampagne“ und „Spiel laden“ im Hauptmenü sind deaktiviert. Zu bauen: Kampagnenauswahl und Schwierigkeit,
   Briefing (`CampaignMission.ObjectiveLabels/IntroMovie/BriefingVoice/UnitNames/LocationLabel`), Ladeliste (`SaveGameStore.List`), Speichern im Spiel, der
   Missionsabschluss (Sieg/Niederlage → `CampaignController.Conclude`), `ScriptOutput`-Wiedergabe (Sprache, Film, Text, Kamera, Radar; ohne Bink-Decoder bleiben
   Filme Platzhalter) und Rückmeldung an die VM (`CompletePresentation`, `SignalUiInteraction`, `SelectNamed`). Anschlussliste: `CAMPAIGN-AND-SAVEGAMES.md` §7.
3. **Durchspielbarkeit der Missionen** – der Kern der offenen Abnahme. Reihenfolge nach Laufzeit-Häufigkeit (`CampaignTool audit`, Tabelle `SCRIPT-ENGINE.md` §9):
   - **Lokale Seite wählen**: Karten ohne `playerIsHuman` (z. B. `GC_LaserGeneral`) brauchen vom Host die Seite des Menschen (`MissionOptions`/`CampaignProgress`);
     `LocalPlayerOf` fällt heute auf Spieler 1 zurück. Generals'-Challenge: `Campaign.PlayerFaction`/General-Wahl.
   - **Befehlsknopf-Fähigkeiten** (`NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY*`, 89+27+20 Treffer) und **`SpecialAbilityUpdate`** (64 Objekte, 36 nur dadurch blockiert:
     Ranger-Fähigkeiten, Colonel Burton, Jarmen Kell, Hacker …) – das sind die meistgenutzten Kampagneneinheiten; dazu `CommandButtonHuntUpdate` ausbauen
     (`TEAM_HUNT_WITH_COMMAND_BUTTON`).
   - **Garnison-Aktionen** (`TEAM_/NAMED_GARRISON_*`, `*_EXIT_*`, `NAMED_ENTER_NAMED`), **`MAP_REVEAL_*`/`MAP_SHROUD_*`** (braucht dauerhafte Aufdeckung in
     `VisionMap`), **`BUILD_TEAM`/`TEAM_MERGE_INTO_TEAM`/`RECRUIT_TEAM`**, **`PLAYER_ENABLE/DISABLE_*_CONSTRUCTION`**, `WAREHOUSE_SET_VALUE`,
     `TEAM_SET_OVERRIDE_RELATION_*`, `OBJECT_ALLOW_BONUSES`, `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, Kamera-gebundene Bedingungen.
   - **Sicht-Bedingungen** (`*_DISCOVERED`, `*_SIGHTED`, `ENEMY_SIGHTED`), `PLAYER_TRIGGERED_SPECIAL_POWER`/`*_COMPLETED_*` (Ereignisse `SpecialPowerUsed`).
   - **`SKIRMISH_*`-Skripte der Original-KI** (37.000+ Treffer je Bedingung auf den Challenge-Karten): entscheide, ob die Skirmish-KI der Challenge-Gegner diese
     Skripte ausführt (Port der Bedingungen/Aktionen der `Skirmish`-Familie) oder die eigene `SkirmishAi` sie ersetzt.
   - **Verstärkung mit Transport** (`teamTransport`, `teamStartsFull`, Flugzeuge/Fallschirm), echte Formationen und Angriffsbewegung, `*_FOLLOW_WAYPOINTS`
     mit `asTeam`.
   - Danach die **Endproben** erneut messen und jede UNMET-Mission mit `--explain` einzeln untersuchen (Ziel: jede Mission erreicht Sieg und Niederlage auf
     dem vom Skript vorgesehenen Weg, nicht nur durch Ausschalten). Fehlende Karten-Überlagerungsdefinitionen (Straßen, Zäune, Bäume):
     prüfen, ob `MapOverlayMode` für Missionen auf `Include` gehört (Kompilierung pro Karte).
4. **Teil B Punkt 9 – Kollision** (`ToppleUpdate` 76/66, `StructureToppleUpdate`, `HeightDieUpdate` 31/24, echtes `CrushDie`/`SquishCollide`) und
   **Punkt 10 – Bau-/Regelgrenzen** (Voraussetzungs-/Fähigkeitsprüfung in `QueueProduction`, `MaxSimultaneousOfType`, Reparaturkosten, Kraftwerkseroberung,
   Overcharge-Befehl, `CommandSet`-/Radarwirkung, veteranenbasierte `ArmorSet`/`WeaponSet`, Detektoren, Höhen-Sichtlinie; die kleinen Lese-/Schreib-APIs
   aus `PRESENTATION-INPUT.md` §10/`GAME-HUD.md`). Beides ist Voraussetzung für faires Skirmish und für Missionsskripte, die auf Zusammenstöße reagieren.

## Reproduktionsbasis (vor dem Weiterarbeiten neu messen)

- Build `dotnet build CnCNew.slnx`: 21 erklärte Warnungen bei **sauberem** `bin`/`obj` von `CnCNew.Client.Stride` (10 `NU1901`/`NU1903` + 11 Stride-Notizen), 0 Fehler;
  `CnCNew.CampaignTool` verweist auf `CnCNew.Game` (kein Stride) und ändert die Zahl nicht.
- Alle Testprogramme aus `CLAUDE.md` bestehen, zusätzlich `CnCNew.Game.Tests` mit `CampaignTests` (Campaign.ini, Fortschritt, Kontrollfluss, Savegame, Store).
- `IniTool compat-check`: 747 Einträge; `SimTool audit-coverage`: 1.494/2.115 (70,6 %), Blocker siehe `SIMULATION-CORE.md` §11; Modulverwendungen 15.639 von 19.553.
- Retail-Referenzmatch `SimTool reference-match`: Tick 13.665, `final hash` `D4779BE413835B6D`; Stresstest `ED6F9329F59662E3`; `MapTool audit` 150/150.
- `CampaignTool audit … --ticks 900` (Release, ca. 6 Minuten): 24 Karten; Ergebnisse siehe `SCRIPT-ENGINE.md` §9. Ändert sich eine dieser Zahlen ohne eigene Änderung, ist
  das ein Befund.

## Nicht verhandelbare Regeln (unverändert, siehe `CLAUDE.md`)

- Unbekannte Schlüssel/Module/Enums/Referenzen fallen nie still auf Standardwerte zurück; jeder Fall ist explizit, gezählt, dokumentiert und gegen
  `baselines/compatibility.json` abgesichert. Neue Skriptaktionen/-bedingungen: in `ScriptCoverage` eintragen (der Test `ScriptSupportTableIsConsistent` prüft die
  Namen), Quellfunktion zitieren, Test in `Tests.Scripting.cs`, Zeile in `SCRIPT-ENGINE.md` §4/§5, Abweichungen in §8. Neue Module: Katalogschema, Bindung, Test,
  Eintrag in `SIMULATION-CORE.md`, Baseline (`IniTool compat-baseline`) – Änderungen daran sind Review-Gegenstand.
- Determinismus: sortierte Sammlungen, keine Wandzeit, kein `float`/`double`, kein `Random.Shared`; `SourceRules` gilt für die VM. Alles, was Skriptzustand ist, gehört in
  `ScriptEngine.Describe/Load` (sonst weicht ein wiederhergestellter Lauf ab – der Fortsetzungstest fängt das).
- Snapshot-/Replay-/Missionsformate: Änderungen erhöhen die jeweilige Version (`WorldSnapshot` 7, Replay 7, `MissionSnapshot` 1, `SaveGame` 1, `CommandTypes.Version` 9);
  ältere werden mit klarer Meldung abgewiesen.
- Fremder Code nur lesen, nicht kopieren; jede portierte Aktion/Bedingung einzeln mit Datei und Funktion zitieren.
- Bestandszahlen immer neu messen. Vorher/nachher-Zahlen zu jeder Modul-/Skriptänderung (Instanziierbarkeit, Abdeckung, `CampaignTool audit`).
- Nur lokal arbeiten, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber; am Ende `docs/handoffs/HANDOFF-PHASE11.md` schreiben und
  committen.
- **Zeilenenden erhalten:** das Repo ist überwiegend LF, einzelne Dateien (`README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` u. a.) sind CRLF;
  beim Skript-Editieren `newline=''` verwenden und `git diff --stat` prüfen (eine versehentliche Umstellung erzeugt Diffs über ganze Dateien).
- Praktisch: große Editskripte als Datei im Scratchpad ausführen statt komplexer Heredocs (Bash-Quoting bricht bei Apostrophen/Zitaten); ein laufender Audit-Prozess sperrt die
  Release-DLLs; `tools/CnCNew.UiRenderTool` bleibt außerhalb der Solution.

## Abschlussbericht

Zahlen mit Commit-Hashes, implementierte Skriptaktionen/-bedingungen und Module mit Quellverweisen, Determinismus-, Test-, Stress- und Fortsetzungsergebnisse, die
`CampaignTool audit`-Tabelle vorher/nachher, Instanziierungsquote und Blockerliste vorher/nachher, bekannte Grenzen, `git status` und Bestätigung „kein Remote, nichts
gepusht“.
