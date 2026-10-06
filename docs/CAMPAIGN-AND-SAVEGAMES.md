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

### `SaveGame` (Game, Version 3)

Eine Datei, ein UTF-8-JSON-Dokument: `format` (`cncnew-savegame`), `version`, `title`, `mapPath` (virtuell), `campaign`, `mission`,
`difficulty`, `tick`, `tickRate` (seit Version 2), `definitionFingerprint`, `stateHash`, `stateSha256` (SHA-256 über den eingebetteten Zustand – deckt jedes Byte, auch
den Präsentationsstrom, den der Simulationshash bewusst nicht enthält), `progress` (`CampaignProgress` oder leer), `state` (das
`MissionSnapshot`, roh). Dieselbe Sitzung im selben Tick ergibt dieselben Bytes; Laden + erneutes Speichern reproduziert die Datei.

`SaveGame.ReadHeader` liest nur den Kopf (Ladeliste). `Load` meldet verständlich, **warum** eine Datei nicht lädt:
„Not a CnCNew savegame“, „Unsupported savegame version N (this game reads version 3)“, „Unsupported tick rate N Hz in the savegame“,
„written at 60 Hz; this session runs at 30 Hz“ (Rate des Spielstands gegen die Rate der Sitzung, in beiden Richtungen),
„written with different game data“ (Definitions-Fingerprint), „belongs to campaign X, which this game does not have“,
„damaged or truncated“, „checksum does not match“, „state does not match its header“ (Tick, Rate oder Zustandshash des Kopfs passen nicht zum
eingebetteten Zustand).

**Version 3 – Festkomma Q39.24 (7. Oktober 2026, `docs/FIXED-POINT-Q39-24.md`).** Der eingebettete Missionszustand trägt Festkommawerte mit 24 statt 16 Nachkommabits; Stände der Versionen 1 und 2 werden über ihre Version abgelehnt, nie mit dem neuen Maßstab gelesen.

**Version 2 – die Hülle trägt ihre Rate (6. Oktober 2026, `docs/TICKRATE-MIGRATION.md` §14.9).** `tick` zählt Ticks der Welt, die gespeichert wurde, und
sagt ohne Rate nichts; Version 1 schrieb keine Rate (ein Spielstand anderer Rate fiel nur mittelbar auf, über den Fingerabdruck, der die Rate
enthält). Seit Version 2 steht `tickRate` (Ticks je Sekunde: 30, 60 oder 120) direkt hinter `tick`. Regeln:

- **Version 1 wird abgelehnt, nie umgedeutet.** Eine Datei ohne Rate als „30 Hz“ zu lesen wäre eine stille Annahme; die vorhandene Versionsprüfung
  weist sie mit „Unsupported savegame version 1 (this game reads version 3)“ ab, in der Ladeliste erscheint der Slot mit diesem Grund. Es gibt keine
  Umwandlung alter Spielstände (binäre Kompatibilität zu alten Savegames ist nicht im Scope).
- **Die Rate wird vor dem Fingerabdruck geprüft**, damit die Meldung die Ursache nennt. Ein Spielstand von 60 Hz lädt nur in einer Sitzung mit 60 Hz.
- **Kopf und Zustand müssen übereinstimmen:** nach dem Wiederherstellen werden Tick und Rate des Kopfs mit der Welt verglichen (der eingebettete
  `MissionSnapshot` trägt seine Rate selbst); ein Kopf mit gefälschter Rate oder falschem Tick wird abgewiesen.
- **Listen zeigen Spielzeit statt des Ticks:** `SaveGameHeader.GameTime` (`m:ss`, ganze Sekunden aus Tick und Rate, Minuten laufen über 59 hinaus) –
  derselbe Text für denselben Zeitpunkt bei jeder Rate. Ladebildschirm (`LoadGameScreen`) und Pausenmenü (`MissionPauseMenu`) zeigen
  „Titel (m:ss)“ statt „Titel (tick N)“. Kein eingecheckter Bildschirm-Hash enthält eine Ladeliste mit Einträgen; die 54 Screen-Hashes sind unverändert.

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
Ablehnung fremder/beschädigter/abgeschnittener Dateien, Store; seit Version 2 die Rate der Hülle (Laden bei 30 und bei 60 Hz, Ablehnung über Kreuz in
beiden Richtungen, Version 1, fehlende und nicht unterstützte Rate, gefälschter Kopf, gleiche Spielzeit in der Liste bei beiden Raten). Mit `--content-dir`: die echte `Campaign.ini` (17 Kampagnen, 85 Missionen,
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

**Spielzeit in der Ausgabe (6. Oktober 2026):** `drive` und `audit` schreiben unter jede Kartenzeile, in der ein Ende erreicht wurde, eine eigene Zeile
`game time (m:ss at 30 Hz): victory after 0:26, defeat after 0:07` und am Schluss `Budget: N ticks are m:ss of game time (m:ss) at 30 Hz.`; `playthrough`
nennt die Spielzeit in Klammern hinter dem Tick. Die Kartenzeilen selbst (`victory REACHED … @Tick`, `win@Tick`, `lose@Tick`) und die Summenzeilen
(„… identical on a second run …“) sind Byte für Byte unverändert, weil sie ausgewertet werden; Ticks bleiben Ticks der gewählten Rate (`--tick-rate N`).

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

- **Kampagnenauswahl** (seit Phase 16 wie im Original, `docs/GAME-SHELL.md` §13): `MainMenu.wnd`s `ButtonUSA`/`ButtonGLA`/`ButtonChina` öffnen die Schwierigkeitsfrage **im Hauptmenü** (`MapBorder4`:
  Easy/Medium/Hard/Back), die Wahl ruft `GameShell.BeginCampaign` → `CampaignProgress.Begin` + `CampaignController.StartCurrentMission`; `ButtonChallenge` fragt dasselbe und öffnet dann
  `ChallengeSelectScreen` (`ChallengeMenu.wnd`: die neun spielbaren `GeneralPersona`s von `ChallengeMode.ini` auf den `GeneralPosition0..11`-Knöpfen, Biografie und Play nach der Wahl, die gewählte Schwierigkeit gilt).
  Laden ist der Knopf Load des Hauptmenüs (`LoadGameScreen`). `SinglePlayerMenu.wnd` und `DifficultySelect.wnd` benutzt Zero Hour nie und die Shell auch nicht mehr.
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
  (`CnCNew.Game.Tests` → `MissionPresentationTests`). `ApplyCameraTo` spielt die Skriptkamera auf einer echten
  `RtsCamera` ab (seit Phase 16 über die Dauer des Skripts, dazu Eingabesperre, Letterbox, Blenden, Zähler, Skript-Ton und Zähler je Ausgabetyp: `docs/SCRIPT-ENGINE.md` §7a); `BuildOverlayCommands` liefert den Bildschirmtext als `UiDrawCommand`s, die der vorhandene
  `CnCNew.Client.Stride`-`UiRenderer` bereits zeichnen kann (kein neuer Renderer nötig). `MissionUiBridge` leitet
  Selektionsänderungen an `ScriptEngine.SelectNamed` weiter (`NAMED_SELECTED`) und reicht `SignalUiInteraction` direkt durch
  (`FLAG`); eine feste Zuordnung „dieser Knopf → dieses Flag“ gibt es nicht (die Mission-Metadaten tragen das nirgends),
  das bleibt eine Entscheidung des jeweiligen Host-Codes.
- **Verzögerung ohne Director (Phase 11):** `CampaignController.StartCurrentMission(maxTicks, humanSideName, presentationLatencyTicks)` reicht
  `MissionOptions.PresentationLatencyTicks` durch; ein Host ohne `MissionPresentationDirector` setzt damit einen Verzögerungs-Fallback für
  `HAS_FINISHED_*`/`CAMERA_MOVEMENT_FINISHED` (Standard 0 = sofort fertig, wie headless). Test: `CampaignTests` → „a host without a presentation director …“.
- **Missions-HUD (Phase 11):** eine Kampagnenmission lässt sich jetzt mit der vollen Skirmish-Befehlsleiste spielen – `LiveGame`/`LiveMission`/`MissionController`, siehe
  `docs/GAME-HUD.md` §14. `MissionPresentationDirector`/`MissionUiBridge` hängen darin; `MissionPauseMenu` bleibt der Speicher-/Lade-Dialog der Mission.
- **Generals'-Challenge-General-Wahl (Phase 11, Phase 12 ergänzt):** `ChallengeSides.HumanSideFor(map)` (`CnCNew.Game/Campaign`) bestimmt die menschliche Seite einer Challenge-Karte, und
  `CampaignController.StartCurrentMission` ruft es für `IsChallenge`-Kampagnen auf, wenn der Host keine Seite nennt (`ChallengeSelectScreen` ruft dadurch automatisch die richtige
  Seite ab, ohne eigenen Code). Messung (`CampaignTool sides`): sieben der acht Challenge-Karten markieren ihre Seite selbst (`ThePlayer`, `playerIsHuman`) – dort entscheidet die Karte;
  `GC_LaserGeneral` markiert keine und definiert nur ihre Gegner (`PlyrAmericaLaserGeneral` ist der Feind, dessen Bauanlagen das Siegskript zerstört sehen will). **Phase 12 – Persona-Fraktion:**
  `MissionOptions.HumanFaction` (Missionszustand 2, `humanFaction` wird nur bei gesetztem Wert gespeichert) trägt die Fraktion des gewählten Generals; `ScriptProgram.WithHumanSide(seite, fraktion)` ersetzt
  damit die Fraktion der menschlichen Seite und hängt auf einer Karte ohne menschliche Seite (`GC_LaserGeneral`) eine **synthetische Seite** `ThePlayer` an (Feind jeder nicht-zivilen Kartenseite und
  umgekehrt; `ChallengeSides.SyntheticHumanSide`); die Programmprüfsumme bleibt die der Karte, damit ein Speicherstand das Programm erkennt, die Optionen sagen, wie es angepasst wurde.
  `CampaignCatalog.WithPersonas(ChallengeMode.ini)` füllt die `PlayerFaction` jeder Challenge-Kampagne ohne eigene aus `GeneralPersonaN` (`PlayerTemplate` + `Campaign`; Personas mit
  `Campaign = unimplemented` und Kampagnen mit eigener `PlayerFaction` bleiben unverändert), `GameShell` liest `Data/INI/ChallengeMode.ini` beim Start. Messung: `GC_LaserGeneral` hat jetzt eine eigene
  menschliche Seite und ist auf dem Skriptweg gewinn- und verlierbar (Zielfahrer, `docs/SCRIPT-ENGINE.md` §9); `CampaignTool drive|audit` reichen die Persona durch.
  Gemessen: `CampaignTool playthrough --campaign CHALLENGE_0` gewinnt Mission01–05 per Auslöschung (Sieg nach 330–360 Ticks).
