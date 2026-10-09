# N+1-Handoff: Phase 9 – Skirmish, Missionen, Kampagnen und Savegames

Du arbeitest autonom an diesem Repository (`<repo>` = Wurzel des Arbeitsverzeichnisses). Lies zuerst `CLAUDE.md`, `README.md`,
`docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§7 „Status“, §8 „Status“ und §9), `docs/SIMULATION-CORE.md`
(§12, §12a, §19–§21), `docs/GAME-RULES.md` §10/§11 sowie die Fachdokumente der Präsentations- und Eingabeschicht:
`docs/PRESENTATION-INPUT.md`, `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/GAME-AUDIO.md`, `docs/CLIENT-TERRAIN.md`,
`docs/CLIENT-OBJECTS.md`, `docs/CLIENT-FX.md`, `docs/CLIENT-UI-RENDERING.md`. `docs/handoffs/HANDOFF-PHASE5.md` bis `docs/handoffs/HANDOFF-PHASE8.md`
bleiben als Verlauf erhalten; ihre Zahlen sind veraltet.

Die Nummerierung: Dieser Auftrag deckt Schritt 9 des Gesamtplans ab (`IMPLEMENTATION-PLAN.md` §9, „Skirmish, Missionen, Kampagnen und
Savegames“). Er trägt zusätzlich die aus Phase 8 verschobenen Simulationsschulden-Punkte 9–11 aus `docs/handoffs/HANDOFF-PHASE8.md` (Kollision,
Bau-/Regelgrenzen, Kartenskripte) sowie einen dokumentierten, nicht behobenen Determinismus-Fehler.

## Ehrlicher Status von Phase 7/8 (Ausgangslage)

Phase 7 und Phase 8 sind abgeschlossen (Merge-Commits u. a. `4da086f`, `b90f861`, `05b5c0c`, `cf117b0`, `0344582`, `86ce4fd`, sowie
`523525c`/`ae642e7`/`a948efb`; vollständige Liste mit `git log --oneline 034c5e6..472c65d`), aber mit bewussten Grenzen (vollständig:
`IMPLEMENTATION-PLAN.md` §7/§8, jeweils Abschnitt „Bekannte Grenzen“):

- **Abnahme erfüllt mit Einschränkungen:** Ein komplettes Skirmish-Match läuft headless-visualisierbar vom Hauptmenü bis zum
  Ergebnisbildschirm und zurück (`CnCNew.Game/Shell` + `CnCNew.Game/Hud` + `CnCNew.Client.Stride`); alle vier Weltlayer (Terrain, Objekte,
  FX, UI) und der Audiopfad liefern über wiederholte Läufe byteidentische SHA-256/Statistiken. Mehrspieler-, Lade-/Wiedergabe- und
  Kampagnen-Einstieg sind im Menü sichtbar, aber deaktiviert (außerhalb des bisherigen Produktscopes) – **das ändert sich jetzt mit
  Kampagne als Teil von Phase 9.**
- **Nicht umgesetzt/bekannt unvollständig:** Türme rendern in Ruhepose (keine live gelesenen Turmwinkel), keine Kontaktschatten, manche
  dünnen zweiseitigen W3D-Meshes schattieren auf der Rückseite schwarz (Stride-Pipeline-Grenze), kein Bink-Video-Decoder (keine SDK-Lizenz),
  keine Formations-/Gruppenversatzlogik, kein Befehl „Feuer auf Position“, Entführen/Autobombe/Sabotage/Bergen/Gebäude
  einnehmen/Hacken bleiben unübersetzt, Anzeigemodus/Sprache/Seitenverhältnis/Barrierefreiheits-Textskalierung sind in `GameSettings`
  vorhanden, aber von der ausgelieferten `OptionsMenu.wnd` aus nicht erreichbar. Volle Liste: `IMPLEMENTATION-PLAN.md` §7/§8.
- **Bekannter, nicht behobener Determinismus-Fehler (wichtig für Savegames!):** `SpecialPowerBase.SharedReadyFrame()`
  (`src/CnCNew.Simulation/Modules/BuiltIn/SpecialPowerModules.cs:133`) legt den Bereitschafts-Tick einer geteilten Spezialkraft lesend an
  (Seiteneffekt von `IsReady()`), statt ihn einmalig bei `OnSpawn`/`OnSpecialPowerCreation` zu setzen. Die Skirmish-KI fragt `IsReady()`
  live in jedem Tick ab, ein reiner `ReplayRunner`-Lauf nie – dadurch kann `SkirmishMatchSession.Step()` (live) einen anderen
  `World.StateHash` liefern als `ReplayRunner.Run` über den eigenen `Session.ToReplay()`-Mitschnitt, für `SharedSynced`-Spezialkräfte ohne
  expliziten Gewährungsbefehl. Vollständige Diagnose in `docs/SIMULATION-CORE.md` §12/§12a. **Das betrifft nicht die bisherigen
  Referenzzahlen** (die sind Live-gegen-Live-Vergleiche, siehe dort), **aber jedes künftige Savegame-/Replay-Format, das eine live
  gelaufene Partie gegen eine wiederhergestellte vergleicht, muss diesen Fehler entweder vorher beheben oder explizit umschiffen** (z. B.
  indem Savegames nur `WorldSnapshot`, nie `ReplayFile`, als Wahrheit behandeln – das tut `WorldSnapshot.Restore` bereits korrekt, siehe
  §7 unten). Ein Fix ändert KI-Timing und damit jeden Referenzmatch-/Stress-/KI-Lauf-Hash; er gehört in eine eigene, gemessene
  Simulationsphase, nicht beiläufig in diese.
- Die Skirmish-KI ist eine eigene Neuentwicklung (`SkirmishAi`), kein Port der Originalskripte; Kartenskripte (`SidesList`/
  `PlayerScriptsList`) werden von `SageMapDecoder` zwar dekodiert, aber vom Simulationskern nicht ausgeführt (siehe Teil B, Punkt 11).

## Reproduktionsbasis (Stand Ende Phase 8, 30. September 2026; vor dem Weiterarbeiten neu messen)

- Modulkatalog: 205 Typen, 85 implementiert (`SimTool audit-modules .local/phase2-import-a`); Instanziierbarkeit 1.470 von 2.115 Objekten
  (69,5 %, `SimTool audit-coverage`); Modulverwendungen abgedeckt 15.543 von 19.553 (79,5 %); Kompatibilitätsbasislinie 749 Einträge
  (`IniTool compat-check .local/phase2-import-a baselines/compatibility.json`).
- Größte verbleibende Blocker (Objekte insgesamt / nur durch dieses Modul blockiert, `SimTool audit-coverage`):
  `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0 (Phase-7-Kategorie,
  reine Präsentation), `HeightDieUpdate` 31/24, `StructureToppleUpdate` 6/4, `CountermeasuresBehavior` 40/0.
- Befehlstabelle: `CommandTypes.Version = 9`; Snapshot Version 7, Replay-Format v7.
- Retail-Referenzmatch (`SimTool reference-match`): 13 Fraktionen, Sieger FactionAmerica bei Tick 13.800, Endhash `4DCB46FE3D2E8214`.
- Stresstest (216.000 Ticks, `SimTool stress`): unverändert seit Phase 6 (Module der Phase 5–6); mit den neuen Transport-/
  Projektilmodulen neu messen, falls die Fixture sie berührt.
- `MapTool audit`: unverändert 150/150 Karten seit Phase 6; **World-Builder-Skripte werden jetzt mit dekodiert** (siehe unten und
  `docs/MAP-SCRIPTS.md`) – vorher schlugen 75 der 150 Karten an dieser Stelle fehl (25 Kampagnenkarten mit einem älteren
  Condition-Chunk-Format, 50 an einer falschen Annahme über `NameKeyType`), jetzt 0.
- **Neu: World-Builder-Skripte dekodiert** (`docs/MAP-SCRIPTS.md`, Commits `73b8ae8`/`ef6191e`/`bef2cd8`): 1.631 Skript-Listen, 23.906 Skripte,
  2.294 Gruppen, 33.890 Bedingungen (82 von 109 Typen verwendet), 82.563 Aktionen (291 von 344 Typen verwendet) über alle 150
  Retail-Karten – vollständige, nach Häufigkeit sortierte Verwendungstabelle in `MapTool audit`. **Nur die Dekodierung ist fertig;
  keine Zeile davon wird ausgeführt** – das ist Teil A, Punkt 2 unten.
- Weltpräsentation: Terrain 0,266 ms/Frame CPU-Mittel, 33–77 MiB GPU je Karte (`docs/CLIENT-TERRAIN.md`); Objekte 0,19–0,60 ms/Frame,
  0 fehlende Modelle/Texturen/Animationen über die geprüften Gefechte (`docs/CLIENT-OBJECTS.md`); FX-Bench mit 10.000 aktiven Partikeln
  1,735 ms je Build (`docs/CLIENT-FX.md`).
- Eingabe/UI: `CommandMap.ini` 96 Einträge/0 Probleme; 820 Befehlsschaltflächen/472 Befehlssätze/0 Probleme; 8 Shell-Screens mit
  54 Golden-Hashes über 6 Auflösungen; UI-Renderer-Verifikation über 32 PNGs byteidentisch (`docs/PRESENTATION-INPUT.md`,
  `docs/GAME-SHELL.md`, `docs/CLIENT-UI-RENDERING.md`).
- Build: `dotnet build CnCNew.slnx` bei **sauberem** `bin`/`obj` von `CnCNew.Client.Stride` (sonst irreführend niedrig, da der
  Stride-Asset-Compiler-Schritt inkrementell übersprungen wird): 21 Warnungen (10 bekannte `NU1901`/`NU1903` + 11 erklärte
  Stride-Konsolennotizen, siehe `CLAUDE.md`), 0 Fehler.

## Teil A – Skirmish, Missionen, Kampagnen, Savegames (Plan §9)

`CnCNew.Game/Shell` kennt Skirmish bereits vollständig (`MatchSetup`, `LiveMatch`, Ergebnisbildschirm); die Lücke ist Kampagne/Missionen
und Speicherstände. **Die Dekodierung der World-Builder-Skripte ist seit diesem Durchgang fertig**
(`MapData.PlayerScripts`, `docs/MAP-SCRIPTS.md`, `MapScriptCatalog` für die Typidentität) – lies dieses Dokument, bevor du Punkt 2
beginnst, es erspart die erneute Reverse-Engineering-Arbeit am Binärformat und erklärt insbesondere, warum `NameKeyType` verworfen
und stattdessen der rohe Enum-Index verwendet wird.

1. **Skirmish abschließen:** Startpositionen/Teams/Regeln/KI-Profile aus `docs/handoffs/HANDOFF-PHASE8.md` Teil B Punkt 10 (Bau-/Regelgrenzen) und
   Punkt 9 (Kollision) sind Voraussetzung für ein faires, vollständiges Skirmish – siehe Teil B unten, bevor du sie als „erledigt“ meldest.
   Statistik (`MatchResult` existiert bereits in `CnCNew.Game/Hud`, siehe `docs/GAME-HUD.md`) auf dem Ergebnisbildschirm vervollständigen.
2. **Skriptengine (Ausführung), Trigger, Counter, Kameraabläufe, Briefings, Zwischensequenzen:** Die Rohdaten stehen bereits in
   `MapData.PlayerScripts` (`MapScript`/`MapScriptGroup`/`MapOrCondition`/`MapCondition`/`MapScriptAction`/`MapScriptParameter`,
   `docs/MAP-SCRIPTS.md`). Zu tun bleibt die **Ausführung**: eine deterministische Skript-VM ohne Wandzeit/ambiente Zufallszahlen in
   `CnCNew.Simulation` (Test `SourceRules` gilt auch hier), die pro Tick die aktiven Skripte einer Skriptliste auswertet
   (ODER-von-UND-Bedingungsbaum, `IsOneShot`/`DelayEvaluationSeconds`/Schwierigkeitsgrad-Flags beachten) und bei Erfolg/Misserfolg die
   Wahr-/Falsch-Aktionsliste ausführt. `docs/MAP-SCRIPTS.md` §5 nennt die Häufigkeitsverteilung aller 82 verwendeten Bedingungs- und
   291 Aktionstypen als Priorisierungsgrundlage (die häufigsten sind `CONDITION_TRUE`, `TIMER_EXPIRED`, `FLAG`, `COUNTER` und
   `ENABLE_SCRIPT`/`SET_FLAG`/`SET_COUNTER`/`SET_(MILLISECOND_)TIMER`/`CALL_SUBROUTINE`); nicht implementierte Typen brauchen eine
   `MissingConditions`/`MissingActions`-Zählung nach demselben Muster wie `MissingModules` bei Objekten – nie einen stillen
   Fehlschlag. Zwischensequenzen/Kameraabläufe/Briefings sind Präsentationssache (`CnCNew.Client.Stride`/`CnCNew.Game`), nicht
   Simulation.
3. **Missionsziele, Niederlagen-/Siegespfade, Kampagnenreihenfolge, Freischaltungen:** eigenes typisiertes Kampagnen-/Missionsmodell in
   `CnCNew.Game` (nicht `CnCNew.Data`, das bleibt renderer-/engine-/archiv-/filesystemunabhängig – prüfen, ob ein neues Datenmodell
   überhaupt in `CnCNew.Data` gehört oder besser in `CnCNew.Game`, weil es Fortschritt/Freischaltung mit dem lokalen Nutzerprofil verknüpft).
4. **Savegameformat:** auf `WorldSnapshot` aufbauen (Version 7, siehe oben), **nicht** auf `ReplayFile` – der Determinismus-Fehler oben
   betrifft nur Replays, `WorldSnapshot.Restore` ist die bereits korrekte, geprüfte Quelle der Wahrheit (siehe
   `Tests.State.cs`/`Tests.ReferenceMatch.cs`: „Ein Snapshot, der mitten im Match genommen wird, setzt sich hash-identisch fort“). Ein
   Savegame ist mindestens `WorldSnapshot` plus Kampagnenfortschritt plus Kartenreferenz; versionieren wie Snapshot/Replay (eigene
   Versionsnummer, klare Ablehnungsmeldung bei Inkompatibilität).
5. **Automatisierte Lade-/Headless-Prüfung jeder Karte/Mission:** `MapTool audit`-Muster auf Missionen erweitern; jede Kampagnenmission
   muss ihre wesentlichen Erfolgs- und Fehlerpfade in einem Headless-Testlauf durchlaufen (kein manuelles Durchspielen als einziger Beleg).

**Abnahme von §9 (aus dem Plan, wörtlich):** Alle Skirmish-Karten sind start- und beendbar, jede Kampagnenmission besteht ihre
wesentlichen Erfolgs- und Fehlerpfade, Speichern/Laden setzt einen identischen Simulationszustand fort. Bleiben Grenzen, benenne sie
ehrlich in `IMPLEMENTATION-PLAN.md` §9.

## Teil B – Aus Phase 8 verschobene Simulationsschulden

Miss vor jedem Schritt mit `SimTool audit-coverage .local/phase2-import-a`, was ein Modul allein freischaltet (Zahlen oben, Stand Ende
Phase 8 – neu messen).

9. **Kollision:** `ToppleUpdate` (76 Objekte/66 nur dadurch blockiert), `StructureToppleUpdate` (6/4), echtes `CrushDie`/`SquishCollide`
   (heute implementiert, aber ohne echte Kollisionsgeometrie – prüfen, was fehlt), `HeightDieUpdate` (31/24); ein Kollisions- oder
   Vermeidungsmodell an `NavigationGrid` anbinden. Das ist Voraussetzung für ein faires Skirmish und für Missionsskripte, die auf
   Einheiten-Zusammenstöße reagieren.
10. **Bau- und Regelgrenzen:** Voraussetzungs- und Fähigkeitsprüfung in `QueueProduction`, `MaxSimultaneousOfType`, Reparaturkosten,
    Kraftwerkseroberung, Overcharge-Befehl, `CommandSet`-/Radarwirkung, veteranenbasierte `ArmorSet`/`WeaponSet`,
    `CommandButtonHuntUpdate` (79/18), `SpecialAbilityUpdate` (64/3), Detektoren, Höhen-Sichtlinie. `docs/PRESENTATION-INPUT.md` §10 und
    `docs/GAME-HUD.md` listen konkrete fehlende Lese-/Schreib-APIs, die die Eingabeschicht bereits vermisst (Baukosten-Trockenlauf,
    Produktionswarteschlangen-Lesezugriff, `SpecialPowerModuleBase.AvailableOnFrame`, `UpgradeMuxModule.Executed`, ein öffentlicher
    Dock-Marker) – beim Schließen dieser Lücken direkt mit erledigen, sie sind kleine, klar spezifizierte Anbauten.
11. **Kartenskripte** (`SidesList`/`PlayerScriptsList`): Die Dekodierung ist erledigt (`MapData.PlayerScripts`,
    `docs/MAP-SCRIPTS.md`); offen bleibt die Ausführung – siehe Teil A, Punkt 2, das ist Teil des Hauptauftrags dieser Phase, nicht
    optional.
12. **(Neu, aus Phase 7/8) `SharedReadyFrame`-Determinismus-Fehler beheben oder bewusst umschiffen** (siehe oben, „Ehrlicher Status“). —
    **Erledigt** (30. September 2026, außerhalb des eigentlichen Phase-9-Hauptauftrags, als eigenständiger Fix nachgezogen): Der
    Bereitschafts-Tick wird jetzt deterministisch bei `OnSpawn` (Objekte, die die Kraft bereits mitbringen) bzw. `OnSpecialPowerCreation`
    (gewährte Kräfte) gesetzt, `IsReady()` liest ihn nur noch. Live-gegen-Replay-Hashvergleichstest ergänzt
    (`Tests.Skirmish.cs` → `SkirmishLiveSessionMatchesItsOwnReplay`, schlägt auf dem unbehobenen Code nachweislich fehl). Retail-
    Referenzmatch- und Stresstest-Hashes unverändert (berühren die Stelle nicht); ein Skirmish-KI-Lauf, der sie berührt, ändert sich wie
    erwartet. Volle Diagnose, Messung und Commit-Referenzen in `docs/SIMULATION-CORE.md` §12/§12a und `CLAUDE.md`. **Nicht** in diesem
    Durchgang gemacht: die 16-Karten-KI-Übersicht in `docs/SIMULATION-CORE.md` §19 neu vermessen (eigene Balance-Erhebung, nicht
    Bestandteil der Determinismuskorrektur) – wer ihre Zahlen für eine andere Entscheidung braucht, muss sie neu laufen lassen. Der
    übrige Hauptauftrag dieser Phase (Teil A: Skirmish-Abschluss, Skriptengine, Kampagne, Savegames; Teil B, Punkte 9–11) ist von diesem
    Fix unberührt und steht weiterhin aus.

## Nicht verhandelbare Regeln (unverändert, siehe `CLAUDE.md`)

- Unbekannte Schlüssel/Module/Enums/Referenzen fallen nie still auf Standardwerte zurück; jeder Fall ist explizit, gezählt,
  dokumentiert und gegen `baselines/compatibility.json` abgesichert.
- Determinismus: kanonische Sortierung, keine Wandzeit, keine `Dictionary`-/`HashSet`-Reihenfolgeabhängigkeit; zwei unabhängige
  Läufe byteidentisch. `CnCNew.Simulation` hängt nur von `CnCNew.Data` ab (`SourceRules`-Test); jedes `IUpdateModule` speichert `nextWake`.
  Eine neue Skript-VM (Teil A, Punkt 2) unterliegt denselben Regeln – keine Ausnahme für „nur Kampagnenlogik“.
- `TreatWarningsAsErrors=true`; die 21 erklärten Warnungen (Stand Phase 8, siehe `CLAUDE.md`) sind der Referenzwert bei einem
  **sauberen** Build; jede andere neue Warnung ist ein Fehler, bis sie erklärt ist.
- Bestandszahlen immer neu messen, nie aus Docs oder diesem Handoff übernehmen.
- Fremder Code darf gelesen, nicht unbesehen kopiert werden; jeder portierte Algorithmus braucht einen Quellverweis (Datei, Funktion).
  Das gilt besonders für Punkt 2 (Skript-VM): `references/.../GameLogic/ScriptEngine/ScriptActions.cpp` (7.568 Zeilen) und
  `ScriptConditions.cpp` (2.992 Zeilen) sind die einzige verlässliche Referenz für die Semantik jeder Aktion/Bedingung, die die VM
  implementiert – jede einzeln zitieren, nicht pauschal.
- Neue Befehle erhöhen `CommandTypes.Version` (aktuell 9) und bekommen eigene Codes; bestehende ändern sich nie. Änderungen am
  Snapshot- oder Replay-Format erhöhen deren Version (aktuell Snapshot 7, Replay 7); ältere werden mit klarer Meldung abgewiesen. Ein
  neues Savegameformat bekommt eine eigene Versionsnummer nach demselben Muster.
- Nur lokal arbeiten, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber;
  am Ende dieses Durchlaufs `docs/handoffs/HANDOFF-PHASE10.md` (oder einen neuen Fortsetzungsauftrag) schreiben und committen.
- Praktisch: Große Editskripte mit dem Write-Werkzeug in eine Datei im Scratchpad legen statt komplexer Heredocs; ein laufender
  SimTool-Prozess sperrt die Release-DLL. `tools/CnCNew.UiRenderTool` ist absichtlich nicht in `CnCNew.slnx` (zwei Stride-`Game`-Exe-
  Projekte in einer Solution lösen zusätzliche, aber harmlose Stride-Asset-Compiler-Konsolennotizen aus, siehe `CLAUDE.md`) – bei neuen
  eigenständigen Stride-Tools genauso verfahren, oder die Konsequenz (höhere erklärte Warnungszahl) bewusst in Kauf nehmen und dokumentieren.

## Abschlussbericht

Zahlen (mit Commit-Hashes), implementierte Funktionen/Module, Determinismus-, Test-, Stress- und Replayergebnisse (inklusive: wurde der
`SharedReadyFrame`-Fehler behoben oder bewusst umschifft?), Performance- und Golden-Image-Messungen, Modulabdeckung und
Instanziierungsquote vorher/nachher mit Blockerliste, bekannte Grenzen und technische Schulden, `git status` und Bestätigung
„kein Remote, nichts gepusht“.
