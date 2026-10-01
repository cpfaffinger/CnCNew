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

`CampaignController.StartCurrentMission(maxTicks, humanSideName)` zählt den Versuch und startet die Mission mit Schwierigkeit und
Versuchszahl (`MISSION_ATTEMPTS`); der Logik-Seed hängt von Mission und Versuch ab, eine Wiederholung ist also nicht der Lauf des
letzten Versuchs. `humanSideName` (optional, Standard leer) reicht `MissionOptions.HumanSideName` durch — für Karten, die keine Seite
selbst als `playerIsHuman` markieren (die Generals'-Challenge-Karten), muss der Host die Seite des Menschen benennen, siehe
`docs/SCRIPT-ENGINE.md` §7; die General-Auswahl selbst ist Sache der Shell (§7 unten).
`Conclude(run)` liest `MissionSession.Outcome` (vom Skript gesetzt): Sieg → `Won`, Niederlage → `Lost`; ein Lauf ohne Ergebnis oder einer
fremden Mission wird abgewiesen. `ResumeCurrentMission(state)` setzt einen gespeicherten Lauf fort, ohne einen Versuch zu zählen.

## 4. Zustandsformate

### `MissionSnapshot` (Simulation, Version 2)

UTF-8-JSON, feste Schlüsselreihenfolge, nur Ganzzahlen, keine Pfade/Zeitstempel. Felder: `format` (`cncnew-mission-state`), `version`,
`tickRate`, `programFingerprint` (strukturelle Prüfsumme des Kartenskripts), `definitionFingerprint`, `terrainFingerprint`, `options`
(Seed, Schwierigkeit, Tick-Limit, Präsentationslatenz, Gefechtsintervall, Missionsversuche, seit Version 2 `humanSideName`), `stateHash` (Welt + Skript), `session`
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
dotnet run -c Release --project tools/CnCNew.CampaignTool -- audit .local/phase2-import-a --map GC_LaserGeneral --human-side PlyrLaserGeneral
dotnet run -c Release --project tools/CnCNew.CampaignTool -- playthrough .local/phase2-import-a --campaign USA
```

`--human-side <Name>` (`audit` und `playthrough`): reicht `MissionOptions.HumanSideName` durch, für Karten ohne eigene `playerIsHuman`-Seite
(`docs/SCRIPT-ENGINE.md` §7); ein Name, der keine Seite der Karte trifft, fällt mit einer Notiz auf den Standard zurück (nie still).

`audit`: jede eindeutige Kampagnenkarte – zwei frische Läufe, Speichern/Wiederherstellen, Endproben Sieg/Niederlage, danach die
Aggregation der fehlenden Aktions-/Bedingungstypen, nicht auflösbarer Verweise und nicht erzeugbarer Objekttypen; Exitcode ≠ 0 bei
Nichtdeterminismus, Fortsetzungsabweichung oder Ladefehler. `--explain` nennt je nicht erreichtem Ende die Endskripte und ihre
falschen Bedingungen. `playthrough`: spielt eine Kampagne durch (Mission starten, Speicherstand schreiben und neu laden, Feinde
ausschalten, Fortschritt übernehmen).

Stand 1. Oktober 2026: `CampaignTool list` – 17 Kampagnen, 85 Missionen, 0 unbekannte Schlüssel, ein Problem (die EA-Demokarte
`CampEADemo` fehlt im Retail-Bestand). `CampaignTool audit` – 24 Karten, 24/24 deterministisch, 24/24 identisch nach Speichern/Wiederherstellen (Tabelle
und Endproben: `docs/SCRIPT-ENGINE.md` §9). Die Größe eines Missionszustands liegt bei rund 3 MB (2.991.420 Byte für die Originalkarte `USA01` nach
1.500 Ticks, Messung vom Werkzeug `SimTool mission-run`; der Weltzustand dominiert).

## 7. Anschluss an die Shell (Phase 10, Punkt 2 – umgesetzt)

`src/CnCNew.Game/Shell/Screens/CampaignScreens.cs`, `src/CnCNew.Game/Shell/MissionPresentation.cs` und die Erweiterungen in
`GameShell.cs`/`MainMenuScreen.cs` verdrahten diesen Abschnitt vollständig:

- **Kampagnenauswahl**: `MainMenu.wnd`s `ButtonUSA`/`ButtonGLA`/`ButtonChina` (jetzt aktiv) öffnen `CampaignFactionScreen`
  (`SinglePlayerMenu.wnd`, New/Load Game); `ButtonChallenge` öffnet `ChallengeSelectScreen` (`ChallengeMenu.wnd`, listet
  `IsChallengeCampaign`-Kampagnen über die `GeneralPosition0..11`-Knöpfe, feste Schwierigkeit Hard). New Game fragt die
  Schwierigkeit über `DifficultySelectScreen` (`DifficultySelect.wnd`) ab, dann `GameShell.BeginCampaign` →
  `CampaignProgress.Begin` + `CampaignController.StartCurrentMission`.
- **Briefing**: `MissionBriefingScreen` nutzt das reale `SinglePlayerLoadScreen.wnd` wieder (dessen `ObjectivesWin`-Gruppe mit
  `StaticTextLine0..4` schon für genau `ObjectiveLabels` gedacht ist; `StaticTextCameoText0..3` trägt `LocationLabel` plus
  `UnitNames0..2`). `ShellPhase.MissionBriefing` hält den Bildschirm für `max(VoiceLengthSeconds, 2)` simulierte Sekunden
  (kein Klick-Skip, exakt wie die ausgelieferte Datei – sie deklariert keinen Eingabe-Callback); `IntroMovie`/`BriefingVoice`
  erscheinen als benannter Platzhalter im Fortschrittstext (kein Bink-Decoder, dauerhafte Grenze). Die meisten echten
  USA/GLA/China-Missionen tragen gar keine `ObjectiveLine`-Einträge in `Campaign.ini` (nur `TRAINING` tut das) – eine leere
  Zeile ist dann korrektes Verhalten, kein Fehler.
- **Ladeliste**: `LoadGameScreen` (`SaveLoad.wnd`, aus dem Hauptmenü über „Load/Replay“ → „Load Game“) listet
  `SaveGameStore.List()`, lädt über die vorhandenen Bestätigungsdialoge der Datei; ein `SaveGameException` zeigt
  `GameShell.ShowError` als `MessageBox.wnd` (neue Klartext-Variante, da die Fehlermeldung nie ein CSF-Schlüssel ist).
- **Speichern im Spiel**: `MissionPauseMenu` (`src/CnCNew.Game/Hud/MissionPauseMenu.cs`) kombiniert `QuitMenu.wnd` (wie
  `MatchController`) mit `PopupSaveLoad.wnd` hinter dessen `ButtonSaveLoad`; der Slotname kommt aus der eingegebenen
  Beschreibung (dateinamensicher gemacht), Überschreiben fragt über das vorhandene `OverwriteConfirmParent` nach.
  Eigenständig von `MatchController`/`LiveMatch` (die bleiben Skirmish-spezifisch, siehe `docs/HANDOFF-PHASE10.md`).
- **Missionsabschluss**: `GameShell.MissionFinished` ruft `CampaignController.Conclude` auf und legt `MissionBannerScreen`
  (`Victorious.wnd`/`Defeat.wnd`, reine Grafik ohne Knöpfe im Original) unter `MissionResultScreen` (`ScoreScreen.wnd`
  wiederverwendet, da die echten Sieg/Niederlage-Dateien gar keine Schaltflächen haben); `ButtonContinue` wird zu „Next
  Mission“/„Retry Mission“/„Campaign Complete“ je nach Ausgang, `ButtonOk` bleibt „Main Menu“.
- **`ScriptOutput`-Wiedergabe und Rückmeldung an die VM**: `MissionPresentationDirector` (neu) zieht
  `ScriptEngine.DrainOutputs()` pro Tick, verfolgt Sprache/Film (Platzhalter-Dauer, kein Audio-/Videolänge-Datensatz
  vorhanden), Bildschirmtext (`DISPLAY_TEXT`/`SHOW_MILITARY_CAPTION`/`INGAME_POPUP_MESSAGE`, echte CSF-Auflösung über einen
  Callback) und Radar-Ereignisse (`RADAR_CREATE_EVENT`/`OBJECT_CREATE_RADAR_EVENT`/`TEAM_CREATE_RADAR_EVENT`, echte
  Weltposition). Kamerabewegungen (`MOVE_CAMERA_TO` u. a.) werden mit ihrer echten Sekundenparameter-Dauer verfolgt und
  `CompletePresentation` wird exakt zu diesem Zeitpunkt aufgerufen (`BeforeStep`/`AfterStep`, zeitlich genauso wie die
  eigene `PresentationLatencyTicks`-Selbstauflösung der Engine) – mit Test gegen eine echte Kamerabewegungssequenz
  (`CnCNew.Game.Tests` → `MissionPresentationTests`). `ApplyCameraTo` überträgt das Ziel einmalig auf eine echte
  `RtsCamera`; `BuildOverlayCommands` liefert den Bildschirmtext als `UiDrawCommand`s, die der vorhandene
  `CnCNew.Client.Stride`-`UiRenderer` bereits zeichnen kann (kein neuer Renderer nötig). `MissionUiBridge` leitet
  Selektionsänderungen an `ScriptEngine.SelectNamed` weiter (`NAMED_SELECTED`) und reicht `SignalUiInteraction` direkt durch
  (`FLAG`); eine feste Zuordnung „dieser Knopf → dieses Flag“ gibt es nicht (die Mission-Metadaten tragen das nirgends),
  das bleibt eine Entscheidung des jeweiligen Host-Codes.
- **Bekannte Grenze**: `CampaignController.StartCurrentMission` hat keinen Parameter für
  `MissionOptions.PresentationLatencyTicks` (immer 0) – ein Klient kann also keinen Verzögerungs-Fallback setzen und muss
  sich für `HAS_FINISHED_*`/`CAMERA_MOVEMENT_FINISHED` vollständig auf eigene `CompletePresentation`-Aufrufe verlassen
  (was `MissionPresentationDirector` tut); ein kleiner zusätzlicher Parameter dort wäre ein sinnvoller Folgeschritt.
- Noch nicht angebunden: eine volle Missions-HUD (Kamera/Auswahl/Befehlsleiste) parallel zu `LiveMatch`/`MatchController`
  (die bleiben Skirmish-typisiert); `MissionPresentationDirector`/`MissionUiBridge`/`MissionPauseMenu` sind bewusst headless
  testbare, renderer-neutrale Bausteine dafür, aber kein vollständiger Ersatz für `MatchController` bei Kampagnenmissionen.
- **Noch offen:** die Generals'-Challenge-General-Wahl bildet den vom Spieler gewählten General noch nicht auf den
  Seitennamen der jeweiligen Karte ab (pro Karte verschieden, z. B. `PlyrLaserGeneral` auf `GC_LaserGeneral`); die
  Engine-Seite dafür steht bereits (`StartCurrentMission(humanSideName:)`/`MissionOptions.HumanSideName`,
  `docs/SCRIPT-ENGINE.md` §7) – `ChallengeSelectScreen` muss die Zuordnung nur noch herstellen und durchreichen.
