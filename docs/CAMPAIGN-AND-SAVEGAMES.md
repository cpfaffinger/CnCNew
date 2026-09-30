# Kampagnen, Missionsablauf und Savegames (Phase 9, Teil A Punkte 3–5)

Die Logikschicht für Kampagnen und Speicherstände liegt in `CnCNew.Game` (`Campaign/`, `Save/`); sie hängt nicht von Stride oder
Shell ab und ist headless testbar (`tests/CnCNew.Game.Tests/CampaignTests.cs`). Die Missionssimulation selbst steht in
`docs/SCRIPT-ENGINE.md`. **Nicht verdrahtet:** die Hauptmenüeinträge „Kampagne“ und „Spiel laden“ bleiben deaktiviert
(`Shell/Screens/MainMenuScreen.cs`); die Screens (Kampagnenauswahl, Briefing, Ladeliste) sind Präsentationsarbeit der nächsten Phase,
die Schnittstellen dafür sind fertig (§7).

## 1. Kampagnenkatalog (`CampaignCatalog`)

`Data/INI/Campaign.ini` ist im Ladeplan als „nicht geladen“ geführt (`LoadPlan.cs`), also nicht Teil des kompilierten
Definitionsgraphen. Das Format ist eine feste, flache INI-Teilmenge (`Key Value` oder `Key = Value`, `;`-Kommentare, `Campaign`- und
`Mission`-Blöcke mit `END`); `CampaignCatalog.Parse` liest sie in der Schicht, die den Kampagnenfluss besitzt. Schlüssel nach
`CampaignManager::parseMissionPart` (`CampaignManager.cpp`) plus die Zero-Hour-Schlüssel der Datei: Kampagne `CampaignNameLabel`,
`FirstMission`, `FinalVictoryMovie`, `IsChallengeCampaign`, `PlayerFaction`; Mission `Map`, `NextMission`, `IntroMovie`,
`ObjectiveLine0..4`, `BriefingVoice`, `UnitNames0..2`, `LocationNameLabel`, `VoiceLength`, `GeneralName`. **Unbekannte Schlüssel** werden in
`UnknownKeys` gesammelt (nie still übergangen), ungeschlossene/überzählige Blöcke sind ein Fehler.

`Validate(mapExists)` meldet: fehlende erste Mission, hängende `NextMission`, doppelte Namen, fehlende Karten, nicht erreichbare Missionen
(Zyklen beenden den Lauf statt ihn zu endlosen).

## 2. Fortschritt (`CampaignProgress`)

Unveränderlicher Record: Kampagne, Schwierigkeit (`ScriptDifficulty`), aktuelle Mission, abgeschlossene Missionen (sortiert), Versuche je
Mission, beendet-Flag. Übergänge: `Begin` → `Started()` (zählt einen Versuch) → `Won(campaign)` (Mission abgeschlossen, nächste wird
aktuell, nach der letzten `Finished`) oder `Lost()` (Zustand bleibt, Wiederholung derselben Mission). `Describe`/`Load` in der
kanonischen Zustandsform (`IStateWriter`/`IStateReader`).

**Freischaltungen:** nicht modelliert – jede Kampagne und jede Mission der Kette ist wählbar. Die Reihenfolge der Challenge-Kampagnen und
die Profilfreischaltung der Originalshell sind Sache des Profils/der Shell und hier nicht belegt.

## 3. Ablauf (`CampaignController`, `MissionRun`)

`MissionContext` = kompilierte Vorlagen + Definitions-Fingerprint + „Karte laden“ (entkoppelt von Dateisystem und `GameData`;
`MissionContext.From(GameData)` für das Spiel, Lambdas für Tests). `MissionRun.Load` baut Terrain und `MissionSession`; `Restore`
stellt sie aus einem `MissionSnapshot` wieder her.

`CampaignController.StartCurrentMission()` zählt den Versuch und startet die Mission mit Schwierigkeit und Versuchszahl
(`MISSION_ATTEMPTS`); der Logik-Seed hängt von Mission und Versuch ab, eine Wiederholung ist also nicht der Lauf des letzten Versuchs.
`Conclude(run)` liest `MissionSession.Outcome` (vom Skript gesetzt): Sieg → `Won`, Niederlage → `Lost`; ein Lauf ohne Ergebnis oder einer
fremden Mission wird abgewiesen. `ResumeCurrentMission(state)` setzt einen gespeicherten Lauf fort, ohne einen Versuch zu zählen.

## 4. Zustandsformate

### `MissionSnapshot` (Simulation, Version 1)

UTF-8-JSON, feste Schlüsselreihenfolge, nur Ganzzahlen, keine Pfade/Zeitstempel. Felder: `format` (`cncnew-mission-state`), `version`,
`tickRate`, `programFingerprint` (strukturelle Prüfsumme des Kartenskripts), `definitionFingerprint`, `terrainFingerprint`, `options`
(Seed, Schwierigkeit, Tick-Limit, Präsentationslatenz, Gefechtsintervall, Missionsversuche), `stateHash` (Welt + Skript), `session`
(Gefechtszähler, Befehlssequenzen je Spieler), `script` (`ScriptEngine.Describe`: Skript-/Gruppenzustand, Zähler, Flags, benannte
Objekte, Teams, Objektlisten, Folgeaufträge, Angriffsprioritäten, Schadenshistorie, Ergebnis, Zählerstände der fehlenden Typen), `world`
(der **unveränderte** `WorldSnapshot`, Version 7, als Roh-JSON eingebettet). `Restore` baut die Welt aus dem Snapshot, leitet das Skriptprogramm
aus der Karte neu ab, lädt den Skriptzustand und prüft beide Hashes. Abgewiesen (`SnapshotFormatException`): andere Version, andere
Definitionen, anderes Gelände, anderes Kartenskript, gekippte/fehlende Bytes.

Gemessen: gespeichert und wiederhergestellt läuft eine Mission Tick für Tick hash-identisch weiter; Speichern → Laden → Speichern
reproduziert die Bytes.

### `SaveGame` (Game, Version 1)

Eine Datei, ein UTF-8-JSON-Dokument: `format` (`cncnew-savegame`), `version`, `title`, `mapPath` (virtuell), `campaign`, `mission`,
`difficulty`, `tick`, `definitionFingerprint`, `stateHash`, `stateSha256` (SHA-256 über den eingebetteten Zustand – deckt jedes Byte, auch
den Präsentationsstrom, den der Simulationshash bewusst nicht enthält), `progress` (`CampaignProgress` oder leer), `state` (das
`MissionSnapshot`, roh). Dieselbe Sitzung im selben Tick ergibt dieselben Bytes; Laden + erneutes Speichern reproduziert die Datei.

`SaveGame.ReadHeader` liest nur den Kopf (Ladeliste). `Load` meldet verständlich, **warum** eine Datei nicht lädt:
„Not a CnCNew savegame“, „Unsupported savegame version N“, „written with different game data“ (Definitions-Fingerprint),
„belongs to campaign X, which this game does not have“, „damaged or truncated“, „checksum does not match“.

**Bewusst außerhalb des Formats:** Skirmish-Partien. Die Skirmish-KI hält Zustand außerhalb der Welt, den dieses Format nicht trägt;
ein Skirmish-Speicherstand würde also nicht identisch fortsetzen. Ein Speicherstand entspricht nie einem `ReplayFile` (der Fehler
`SharedReadyFrame` ist behoben, aber Missionen sind ohnehin nicht als Replay reproduzierbar, `docs/SCRIPT-ENGINE.md` §3).

### `SaveGameStore`

`<Slot>.cncsave` in einem Verzeichnis: atomares Schreiben (temporäre Datei, dann Verschieben), `List()` meldet beschädigte Dateien mit
Grund statt zu werfen, Slotnamen können das Verzeichnis nicht verlassen, `Delete`. Den Speicherort wählt die Shell (Einstellungen), nicht der
Store.

## 5. Automatisierte Prüfung

`tests/CnCNew.Game.Tests/CampaignTests.cs` (synthetische Daten): Parser, Validierung, Fortschritt inkl. JSON-Runde, Kontrollfluss mit
Niederlage/Wiederholung/Versuchszähler/Seed, Speichern → Laden → identische Fortsetzung durch einen Sieg, Byte-Kanonizität,
Ablehnung fremder/beschädigter/abgeschnittener Dateien, Store. Mit `--content-dir`: die echte `Campaign.ini` (17 Kampagnen, 85 Missionen,
0 unbekannte Schlüssel, einzige Auffälligkeit die im Retail-Bestand fehlende Karte `Maps/CampEADemo/CampEADemo.map`).

## 6. Werkzeug `CnCNew.CampaignTool`

```powershell
dotnet run --project tools/CnCNew.CampaignTool -- list .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.CampaignTool -- audit .local/phase2-import-a --ticks 900
dotnet run -c Release --project tools/CnCNew.CampaignTool -- audit .local/phase2-import-a --map MD_USA01 --explain
dotnet run -c Release --project tools/CnCNew.CampaignTool -- playthrough .local/phase2-import-a --campaign USA
```

`audit`: jede eindeutige Kampagnenkarte – zwei frische Läufe, Speichern/Wiederherstellen, Endproben Sieg/Niederlage, danach die
Aggregation der fehlenden Aktions-/Bedingungstypen, nicht auflösbarer Verweise und nicht erzeugbarer Objekttypen; Exitcode ≠ 0 bei
Nichtdeterminismus, Fortsetzungsabweichung oder Ladefehler. `--explain` nennt je nicht erreichtem Ende die Endskripte und ihre
falschen Bedingungen. `playthrough`: spielt eine Kampagne durch (Mission starten, Speicherstand schreiben und neu laden, Feinde
ausschalten, Fortschritt übernehmen).

Stand 1. Oktober 2026: `CampaignTool list` – 17 Kampagnen, 85 Missionen, 0 unbekannte Schlüssel, ein Problem (die EA-Demokarte
`CampEADemo` fehlt im Retail-Bestand). `CampaignTool audit` – 24 Karten, 24/24 deterministisch, 24/24 identisch nach Speichern/Wiederherstellen (Tabelle
und Endproben: `docs/SCRIPT-ENGINE.md` §9). Die Größe eines Missionszustands liegt bei rund 3 MB (2.991.420 Byte für die Originalkarte `USA01` nach
1.500 Ticks, Messung vom Werkzeug `SimTool mission-run`; der Weltzustand dominiert).

## 7. Anschluss für die Shell (nächster Schritt)

- Kampagnenauswahl: `CampaignCatalog.Campaigns` (Namensschlüssel `NameLabel` → Text), Schwierigkeit → `CampaignProgress.Begin`.
- Briefing: `CampaignMission.ObjectiveLabels`/`IntroMovie`/`BriefingVoice`/`UnitNames`/`LocationLabel`; Zwischensequenzen spielt der
  Client anhand der `ScriptOutput`-Liste (`ScriptEngine.DrainOutputs`).
- Laden: `SaveGameStore.List()` → `SaveGame.Load` → `CampaignController(context, campaign, loaded.Progress)` + `run`.
- Mission beenden: `MissionSession.Outcome` ≠ `None` und `IsOver` → `Conclude`.
