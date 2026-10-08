# Zuschauermodus: KI gegen KI im Fenster

Stand: 8. Oktober 2026. Ein Skirmish zwischen zwei Computerspielern, das im Client läuft und dem ein Mensch nur zusieht – wie ein Replay, aber live.
**Gebaut und ohne Fenster gemessen; von einem Menschen weder gesehen noch bedient.** Die Bilder des Prüfskripts belegen, dass etwas gezeichnet wird, nicht, dass es
richtig aussieht oder lesbar ist (Checkliste in §11).

## 1. Was der Modus ist

- **Niemand spielt.** `MatchSetup.Spectator` macht jeden Slot zum Computerspieler (`SkirmishSlot.IsHuman = false`); die Sitzung ist dieselbe `SkirmishMatchSession`, die
  `SimTool ai-match` ohne Fenster fährt. Ein Test vergleicht den Zustandshash des beobachteten Gefechts mit dem der reinen Sitzung desselben Seeds (gleich nach 60 s
  Spielzeit, trotz Klicks, Tasten, Tempo- und Nebelwechseln des Zuschauers).
- **Der Zuschauer schreibt nichts.** Die Befehlssenke des Gefechts ist im Zuschauermodus `SpectatorCommandSink`: sie lehnt jeden Befehl ab, ohne ihn zu bauen. Linksklicks
  in die Welt und alle Tasten außer den Zuschauertasten und den Pfeiltasten erreichen die Eingabeschicht des Spielers gar nicht erst. `SkirmishMatchSession.RecordedCommands`
  enthält nach jedem Lauf nur Befehle des Systems und der Computerspieler (Test und Prüfskript).
- **Sicht.** Standard ist die ganze Karte ohne Nebel; umschaltbar auf die Sicht von Spieler 1 oder 2 (`LiveGame.ShowWorldAs`). Das ändert nur die Darstellung
  (Projektor und Nebelraster), die Sicht der Simulation bleibt unberührt.
- **Ende.** Die übliche Siegerkennung der Sitzung (`World.TryGetWinner`, alle 15 Originalbilder); danach steht das Spiel, und unter der Kopfleiste steht der Sieger
  (`WINNER: …`) oder nach Ablauf des Tickbudgets `NO WINNER (time limit)`.

Schichten: `CnCNew.Presentation/Spectator` (Kamera der KI, Zoom-Hilfen; kein Stride), `CnCNew.Game/Spectator` (Statistik, Steuerung, HUD als Zeichenliste; kein Stride),
`CnCNew.Client.Stride` (Verdrahtung, Bildkostenmessung). Die Simulation ist um genau einen lesenden Zugriff erweitert: `SkirmishMatchSession.RecordedCommands` gibt die
ohnehin geführte Befehlsliste heraus. Kein neuer Zustand, kein neuer Hash; Eingangs- und Referenzmanifest sind unverändert (§12).

## 2. Start, Optionen, Tasten

```powershell
pwsh scripts/demo-spectator.ps1                                   # frischer Seed, eine der offenen Paarungen (§8)
pwsh scripts/demo-spectator.ps1 -Seed 7 -Map "Alpine Assault" -FactionA FactionAmerica -FactionB FactionChina
pwsh scripts/demo-spectator.ps1 -Speed 2 -Size 1920x1080 -MaxZoom 2000 -TickRate 60
pwsh scripts/check-spectator.ps1                                  # dasselbe ohne Fenster, mit Prüfungen und Bildern (§12)
```

Parameter von `demo-spectator.ps1`: `-Map`, `-FactionA`, `-FactionB`, `-Seed`, `-TickRate` (30/60/120; ohne Angabe der Standard des Clients), `-MaxZoom`, `-Speed`,
`-Size`, `-ContentDir`, `-Mute`, `-NoBuild`. Das Skript gibt Seed, Karte, Armeen, das Ergebnis der Erhebung für die Paarung und die Tasten aus.

Optionen des Clients (zusammen mit `--match <Karte> --faction <A> --opponent <B>`):

| Option | Bedeutung |
| --- | --- |
| `--spectate` | Zuschauermodus: beide Fraktionen sind Computerspieler, das Beobachter-HUD ersetzt das des Spielers |
| `--seed N` | Seed des Gefechts: `AiSeed = N`, `WorldSeed = N xor C0FFEE06` – derselbe wie `SimTool ai-match --seed N` |
| `--spectator-max-zoom H` | höchste Kamerahöhe über Grund; ohne Angabe die Höhe, aus der die ganze Karte im Bild ist |
| `--spectator-speed X` | Spieltempo beim Start (1 = Echtzeit) |
| `--tick-rate N` | Simulationsrate wie überall |

Tasten (jede steht auch auf ihrem Knopf; `SpectatorController.KeyTable`):

| Taste | Wirkung |
| --- | --- |
| `P` `U` `L` `E` | Tafel Produktion / Einheiten / Verluste / Wirtschaft; `Tab` nächste Tafel; `H` Tafeln aus/an |
| `F1` `F2` | der simulierten Kamera von KI 1 / KI 2 folgen (noch einmal oder `F3`: freie Kamera; Scrollen löst das Folgen auch) |
| `J` `K` | einmal zur Kamera von KI 1 / KI 2 springen |
| `O` | Übersicht über die ganze Karte (noch einmal: zurück zur vorigen Ansicht) |
| `F` | Nebel: alles → Sicht Spieler 1 → Sicht Spieler 2 → alles |
| Leertaste, `1` `2` `4` `8` | Pause, Tempo 1× / 2× / 4× / 8× |
| Mausrad | Zoom, je Raste 12 % der Höhe, bis zur Zuschauergrenze |
| Pfeiltasten, Bildrand, Rechtsziehen | scrollen (die Kamerasteuerung des Spielers; der Schritt wächst mit der Höhe); Mitteltaste ziehen: drehen; Klick aufs Radar: dorthin |
| `Esc` | beenden |

**Tempo und Pause** sind die vorhandenen Schalter von `LiveGame` (`Speed`, `Paused`): `Advance` sammelt `Sekunden × Speed` und rechnet je Aufruf höchstens acht
Originalbilder nach. Bei 60 Bildern je Sekunde sind 8× vier Originalbilder je Bild, also innerhalb des Budgets; ein Rechner, der die Ticks nicht schafft, fällt
zurück, statt zu springen. Das Tempo ist keine Eingabe der Simulation.

## 3. Das Beobachter-HUD

Vorbild für das, **was** gezeigt wird, ist die Beobachteroberfläche von StarCraft II; das Aussehen ist es nicht.

- **Kopfleiste** (oben Mitte): beide Spieler nebeneinander – Nummer, Fraktion, Farbe (Balken am Rand), Geld, Einkommen je Minute, Armeewert, Einheiten (davon Armee),
  Strom erzeugt/verbraucht (rot bei Mangel); dazwischen Spielzeit, Tempo und Nebelsicht. Mehr als zwei Spieler erscheinen als Zeilen darunter.
- **Tafel** (oben links), vier Reiter mit Tastenhinweis: Produktion (je Gebäude der Kopf der Warteschlange mit Fortschritt und `+N` für weitere, Baustellen mit
  Fortschritt), Einheiten (Armee nach Typ mit Anzahl und Wert), Verluste (Abschüsse und ihr Wert, verlorene Einheiten und Gebäude nach Typ und Wert), Wirtschaft.
- **Radar** (unten links): die Minimap des Clients; darauf je KI ein Rahmen in ihrer Farbe – der Bodenausschnitt, den ein Spieler bei Standardzoom an der Stelle sähe,
  auf die die simulierte Kamera gerade blickt (dicker, wenn die Hauptansicht ihr folgt). Der weiße Rahmen ist die Hauptkamera.
- **Knopfleiste** (unten Mitte, wo ein Spieler die Befehlsleiste hat): Reiter, Folgen KI 1/2, freie Kamera, Übersicht, Nebel, Pause, Tempo. Jeder Knopf ist klickbar
  und trägt seine Taste.
- **In der Welt**: eine Raute in der Farbe des Spielers, wo seine simulierte Kamera hinsieht, mit der Art des Blickpunkts (`AI 1: Construction`).

**Platzhalter.** Alles außer den Bildchen der Objekte ist Platzhalter: flache Rechtecke, eine Schrift (Arial), englische Beschriftungen, keine Grafik der
Beobachter-Befehlsleiste des Originals (`ControlBar.wnd` hat Beobachterfenster; sie werden hier nicht benutzt). Die Bildchen sind die Knopfbilder der Original-
Befehlsknöpfe (`CommandButton … ButtonImage` des Knopfs, der das Objekt baut); ein Objekt ohne solchen Knopf erscheint nur mit seinem Vorlagennamen. Namen sind
Vorlagennamen (`AmericaInfantryRanger`), keine lokalisierten Anzeigenamen. Der Bereich außerhalb der Karte ist in der Übersicht die Löschfarbe des Renderers.

## 4. Woher jeder Wert kommt

`SpectatorStats` (Spielschicht) sammelt aus den Ereignissen jedes Ticks, was die Simulation nicht selbst führt, und liest den Rest aus der Welt. Keine Wandzeit; Zeit ist
der Tick mit der Rate der Welt (`world.Time.TicksPerSecond`). Feste Reihenfolge (sortierte Tabellen), also dieselben Tafeln bei jedem Lauf desselben Gefechts (Test).

| Wert | Quelle |
| --- | --- |
| Geld | `world.MoneyOf(player)`, ganze Credits |
| Einkommen je Minute | Summe der `SupplyDeposited`-Ereignisse der letzten 60 **Spiel**sekunden (`IncomeWindow`: ein Fach je Spielsekunde; in der ersten Minute die Summe bisher, keine Hochrechnung). Gutgeschrieben dem Besitzer des Versorgungszentrums |
| gesammelt gesamt | Summe aller `SupplyDeposited` |
| Versorgungsfahrzeuge | lebende eigene Objekte mit `KindOf HARVESTER` oder einem Versorgungsmodul (`SupplyTruckAIUpdate`, `WorkerAIUpdate`) |
| Versorgungszentren | fertige eigene Gebäude mit `SupplyCenterDockUpdate` |
| Strom | wie die Befehlsleiste (`CommandBarModel.Hud`): Verbrauch = negative `EnergyProduction` der fertigen eigenen Gebäude; Erzeugung = `world.EnergyProductionOf` + Verbrauch |
| Einheiten / Armee / Arbeiter | lebende eigene Objekte: `STRUCTURE` → Gebäude; `PROJECTILE` → zählt nicht; `DOZER` oder `HARVESTER` → Arbeiter; sonst `INFANTRY`/`VEHICLE`/`AIRCRAFT` → Armee (`SpectatorStats.Classify`) |
| Armeewert, Wert je Typ | Summe der `BuildCost` der Vorlagen |
| Produktion | je fertigem Gebäude `ProductionUpdate.Queue`: Kopf der Schlange, Fortschritt `(BuildTicks − RemainingTicks) / BuildTicks`, Länge |
| Bau | eigene Gebäude mit Status `UnderConstruction`: `ConstructionWork / BuildTicks` |
| Verluste | `EntityDied`, einmal je Objekt, nach Klasse und Vorlage, Wert = `BuildCost`. Besitzer und Vorlage aus der Welt, ersatzweise aus dem Vermerk beim `EntitySpawned` (ein Objekt, das im selben Tick stirbt und verschwindet) |
| Abschüsse | dieselben Ereignisse: der Besitzer des Verursachers (`SimEvent.Other`), wenn er ein anderer Spieler ist (`Object::scoreTheKill`) |
| Gebäude gebaut | `ConstructionCompleted` |
| Sieger | `SkirmishMatchSession.Winner` |

Die Tafeln werden alle fünf Originalbilder neu gebaut (`PanelIntervalFrames`), nicht je Bild.

**Raten.** `IncomeWindow` liefert für dieselben Einzahlungen zur selben Spielzeit bei 30, 60 und 120 Ticks je Sekunde dieselbe Zahl (Test ohne Spieldaten). Mit echten
Daten prüft ein Test bei 30 **und** 60 Hz, dass das angezeigte Einkommen gleich der unabhängig aus den Ereignissen gebildeten Summe der letzten Spielminute ist und die
Tafel Spielsekunden zählt. Die Gefechte selbst verlaufen bei 30 und 60 Hz verschieden (bis Phase 23 erwartet): im Testlauf sammelt Spieler 2 nach 170 s bei 30 Hz
nichts, bei 60 Hz 3.900 Credits.

## 5. Die „Kamera“ der KI

Ein Computerspieler hat keine Kamera. `AiCamera` (`CnCNew.Presentation/Spectator`) beantwortet stattdessen: *Wohin müsste ein Spieler sehen, um diese Befehle zu geben?*

**Quelle.** Nach jedem Tick liest `SpectatorController` die seit dem letzten Tick hinzugekommenen Einträge von `SkirmishMatchSession.RecordedCommands` (die Liste, aus der
`ToReplay` das Replay baut). Die Simulation meldet jede Ablehnung als `CommandRejected` mit Spieler und Befehlstyp; sind in einem Tick **alle** Befehle eines Typs eines
Spielers abgelehnt, zählen sie nicht (die KI wiederholt abgelehnte Befehle bei jedem Nachdenken – eine nicht bereite Spezialkraft hat die Kamera sonst dauernd an einen
Ort gezogen, an dem nichts geschieht). Sind nur einige abgelehnt, sagt das Ereignis nicht, welche; dann bleiben alle.

**Blickpunkt je Befehl** (schwerster zuletzt; je Tick zählt der schwerste):

| Art | Befehl | Ort | Haltezeit |
| --- | --- | --- | --- |
| `Supply` | `DockCommand` | das Dock | 2 s |
| `Move` | `SetMoveTargetCommand`, `MoveAndEnterCommand`, `EvacuateCommand`, weniger als drei Angriffsmärsche | Ziel bzw. Objekt bzw. Mitte der Einheiten | 2 s |
| `Research` | `PurchaseUpgradeCommand` an einem Gebäude | das Gebäude | 2 s |
| `Production` | `QueueProductionCommand` | das Produktionsgebäude | 3 s |
| `Upkeep` | `ResumeConstructCommand`, `RepairCommand`, `CaptureCommand`, `SellCommand` | das Gebäude | 3 s |
| `Construction` | `ConstructCommand` | der Bauplatz | 4 s |
| `Power` | `UseSpecialPowerCommand` | Zielort, Zielobjekt oder die Quelle | 5 s |
| `Attack` | drei oder mehr `AttackMoveCommand` in einem Tick (eine Welle) | die **Mitte der befohlenen Einheiten**, je Tick neu, solange sie leben | 6 s |

Ein Wissenschaftskauf hat keinen Ort und bewegt nichts. Das Versorgungszentrum erscheint als Bauplatz (wenn es gesetzt wird) und als Dock.

**Wechsel.** Ein neuer Blickpunkt übernimmt, wenn er schwerer ist als der aktuelle, wenn der aktuelle seine Haltezeit hatte, oder wenn er ihn fortsetzt (dieselbe Welle,
dasselbe Gebäude – dann ohne neue Haltezeit, damit ein langer Angriff nicht alles andere für immer verdrängt). Sonst wird er verworfen.

**Glättung.** Die Blickposition gleitet je Tick exponentiell auf das Ziel zu (Zeitkonstante 15 Originalbilder = 0,5 s); liegt das Ziel weiter als 600 Einheiten weg,
schneidet sie (ein Spieler drückte eine Taste oder klickte aufs Radar). Alle Zeiten sind Originalbilder, umgerechnet mit den Ticks je Originalbild der Welt: dieselben
Befehle zur selben Spielzeit ergeben bei 30, 60 und 120 Hz denselben Wechselzeitpunkt und dieselbe Position bis auf den Weg eines Originalbilds (Test). Die
Hauptansicht, die einer KI-Kamera folgt, gleitet ihr auf Bildzeit nach, damit sie nicht im Ticktakt ruckt.

**Beide Kameras zugleich: Rahmen auf dem Radar statt Bild-im-Bild.** Gebaut ist die zweite der beiden Möglichkeiten des Auftrags: beide KI-Kameras als farbige Rahmen
auf dem Radar und als Marken in der Welt, dazu Folgen (`F1`/`F2`) und Springen (`J`/`K`). Ein zweites und drittes 3D-Fenster ist in diesem Renderer nicht billig zu
haben: `LiveMatchHost` hat eine Kamera in einem Kompositor-Slot, und die Weltschichten bereiten je Bild **eine** Ansicht vor – `TerrainLayer` schaltet seine Chunks nach
`RtsCamera.VisibleChunks` der einen Kamera, `ObjectLayerCore` wählt und verwirft Instanzen gegen ihren Frustum und füllt seine Puffer dafür, `FxLayer` ebenso. Zwei
weitere Ansichten hießen, `IWorldLayer` auf mehrere Kameras umzubauen und die Objektschicht dreimal je Bild zu füllen; die Messung in §7 (das Zeichnen ist der Teil,
der mit der Zahl der Objekte im Bild wächst) spricht dagegen, das nebenbei zu tun. Es ist als eigene Aufgabe möglich, nicht als Teil dieser.

## 6. Kamera und weiter Zoom

- `RtsCamera.SetMaxHeightAboveGround` hebt die Obergrenze der Kamerahöhe für den Zuschauer an (nie unter `MaxCameraHeight` der Spieldaten, 310); die Kamera eines
  Spielers ist unverändert (Test). Standardgrenze ist `SpectatorCamera.OverviewHeight`: die kleinste Höhe, aus der bei 70° Neigung alle vier Ecken der Kartengrenze mit
  3 % Rand im Bild sind (Bisektion auf einer Probekamera). Alpine Assault bei 1280×720: **3.275** gegenüber 310.
- `O` stellt 70° Neigung, diese Höhe und die Kartenmitte ein und merkt sich die vorige Ansicht.
- Mit der Höhe wächst der Scrollschritt (sonst kröche die Ansicht), und das Mausrad ändert die Höhe um 12 % je Raste statt um feste 10 Einheiten.

**Was die Renderer in der Ferne tun** (gelesen und in den Bildern des Prüfskripts angesehen):

- *Fernebene:* `RtsCamera.BuildMatrices` legt sie hinter die fernste Ecke des Höhenfelds; das gilt auch von oben (Test „the far camera still sees the whole height field“).
- *Terrain:* Auswahl der Chunks nur über den Frustum, keine Entfernungsgrenze; in der Übersicht sind alle Chunks der bespielbaren Karte an (Test), im Mittel 132 statt 8.
- *Nebel/Schleier:* eine Textur mit einem Texel je Zelle für die ganze Karte, unabhängig von der Kamera; „alles“ setzt alle Zellen auf sichtbar.
- *Objekte:* nur Frustum-Auswahl, keine Entfernungsgrenze und keine Detailstufen – in der Übersicht wird jedes Objekt der Karte gezeichnet (455 statt 25).
- *Effekte:* Frustum-Auswahl; nichts Auffälliges.
- *Kamerabegrenzung:* `W3DView::calcCameraConstraints` hält den Blickpunkt in der Kartenmitte, sobald der Rand des Bildes breiter ist als die halbe Karte. Weit oben lässt
  sich deshalb nicht mehr scrollen – beabsichtigt gelassen (man sieht ohnehin alles).

**Behoben** wurde dabei eines: Beim Verlassen der Übersicht muss erst der Zoom gesetzt und dann der Blickpunkt wiederhergestellt werden, sonst hält die Begrenzung der
noch fernen Kamera ihn in der Kartenmitte. Gebrochen ist in den Bildern sonst nichts; ob Texturen in der Ferne flimmern, kann nur ein Mensch am laufenden Bild sehen.

## 7. Bildkosten, gemessen

`scripts/check-spectator.ps1`, Alpine Assault, America gegen China, Seed 7, 1280×720, Rauchtest (jedes Bild rechnet 1/30 s, das Gefecht läuft dabei mit Tempo 3 bis 8),
dieser Rechner, Release, zwei Läufe (der erste mit Starttempo 4, der zweite mit 3). Gemessen ist die **CPU-Zeit des Clients** je Bild mit einer Stoppuhr um `Update`
(Simulation, Präsentation, Schichten) und um `Draw` (Übergabe der Szene und HUD); was die Grafikkarte danach braucht, ist nicht enthalten. „Fern“ ist jede Höhe über dem
Doppelten der Spielergrenze; in beiden Läufen sind das die 699 Bilder der Übersicht.

| | Lauf | Bilder | Update Median | Draw Median | Update 95 % | Draw 95 % | Objekte gezeichnet | Dreiecke | Terrain-Chunks |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| nah (Spielerzoom) | 1 | 2.309 | 5,1 ms | 3,6 ms | 49,9 ms | 26,8 ms | 25 | 5.055 | 8 |
| nah (Spielerzoom) | 2 | 2.309 | 5,9 ms | 5,2 ms | 64,8 ms | 42,9 ms | 27 | 5.205 | 9 |
| fern (Übersicht) | 1 | 699 | 7,7 ms | 19,4 ms | 27,6 ms | 49,9 ms | 455 | 34.684 | 132 |
| fern (Übersicht) | 2 | 699 | 7,8 ms | 13,4 ms | 21,3 ms | 36,8 ms | 433 | 33.070 | 132 |

Lesart: Das Zeichnen kostet in der Übersicht das Zweieinhalb- bis Fünffache des Spielerzooms, Update und Draw zusammen 21 bis 27 ms je Bild im Median (also rund 35 bis
45 Bilder je Sekunde in dieser Messung; nah über 90). Die Dreieckszahl ist klein; teuer ist die Zahl der einzeln übergebenen Objekte (über 400 statt rund 25). Die
beiden Läufe streuen um ein Drittel, und die Mittelwerte liegen über den Medianen (fern 17 und 42 ms Draw), weil einzelne Bilder Modelle laden oder mehrere Ticks
rechnen – die Zahlen sind eine Größenordnung, keine Messreihe. Nicht gemessen: die Grafikkarte, andere Karten, Gefechte mit acht Spielern, ein ruhiger Rechner.

## 8. Der Ausgang darf nicht vorhersagbar sein

**Wie das Original Zufall in ein Gefecht bringt** (Zero-Hour-Quellen unter `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/`):

- *Der Seed.* `GameClient/GUI/GUICallbacks/Menus/SkirmishGameOptionsMenu.cpp` setzt beim Öffnen des Gefechtsmenüs `TheSkirmishGameInfo->setSeed(GetTickCount())` (Zeile
  1351) – die Millisekunden seit dem Start von Windows – und übergibt ihn beim Start an `InitGameLogicRandom` (`reallyDoStart`, Zeile 434; `Common/RandomValue.cpp`).
  Ein Replay speichert ihn und setzt ihn wieder (`Common/Recorder.cpp`). Das Original ist also wie wir deterministisch; **der Zufall ist der Seed.**
- *Wo die KI würfelt* (`GameLogicRandomValue`): `AIPlayer::selectTeamToBuild` (`GameLogic/AI/AIPlayer.cpp`, Zeile 1702) wählt gleichverteilt unter den baubaren Teams
  mit der höchsten Priorität; die Priorität steigt und fällt mit Erfolg und Misserfolg des Teams (`Common/RTS/Team.cpp`). `AIPlayer::doUpgradesAndSkills` (Zeile 2969)
  zieht einmal ein `SkillSet` der Seite (`AIData.ini`) und kauft dessen Wissenschaften der Reihe nach. `AIPlayer::computeSuperweaponTarget` (Zeile 1202) würfelt die
  Suchrichtung, `AISkirmishPlayer::computeSuperweaponTarget` (`AISkirmishPlayer.cpp`, Zeile 1145) den Anmarschweg (Mitte, Flanke, Hintertür) für Streuminen. Dazu
  zufällige Verzögerungen je Einheit in `AIGroup.cpp`, die Zielwahl auf „leicht“ und Verzweigungen an Wegpunkten in `AIStates.cpp`, zufällige Skript-Timer in
  `ScriptEngine::setTimer`. **Kein** Zufall steckt in der Gegnerwahl (`AISkirmishPlayer::acquireEnemy`: die nächste Basis) und in der Bauliste
  (`AISkirmishPlayer::adjustBuildList`: die `SkirmishBuildList` der Seite; `RotateSkirmishBases` ist in den Daten `No`).
- *Skripte.* Die Skirmish-KI des Originals ist vor allem skriptgetrieben: Hat die Karte keine eigenen Skripte, lädt `GameLogic/Map/SidesList.cpp`
  `Data\Scripts\SkirmishScripts.scb`, `Player::initFromDict` (`Common/RTS/Player.cpp`) kopiert Skripte und Teams je Computerspieler. Welche Zufallsaktionen die Datei
  enthält, ist hier nicht ausgewertet.

**Wie wir es tun.** `SkirmishMatchOptions.AiSeed` und `WorldSeed`; jede KI erhält `AiSeed + Slot × 7919` (`SkirmishMatchSession.Start`). `SkirmishAi` hat einen Strom
(`_random`) mit zwei Verbrauchern: den Startwinkel der Platzsuche je Ring (`FindPlacement`) und die gleichverteilte Wahl der nächsten Armeeeinheit unter den
bezahlbaren mit erfüllten Voraussetzungen (`ProduceArmy`). Der Weltseed speist den Zufallsstrom der Welt. `SkirmishScripts.scb` wird nicht ausgeführt.
Ohne Seed-Angabe sind die Seeds fest (`SkirmishMatchOptions.Standard`): **jedes Gefecht derselben Paarung verliefe gleich.**

**Was das Demo-Skript tut.** Es zieht je Start einen Seed mit `Get-Random` (Zufallsquelle des Systems, im Skript, nie in der Simulation), gibt ihn aus und nimmt
`-Seed N` zum genauen Wiederholen. Der Client-Seed ist derselbe wie bei `SimTool ai-match --seed N`. Ohne Paarungsangabe wählt das Skript eine der Paarungen, deren
Ausgang in der Erhebung (320 Gefechte, 20 Seeds je Paarung, 30 Hz) offen war – Siege der ersten / der zweiten Fraktion / unentschieden:

| Karte | Paarung | Ergebnis |
| --- | --- | --- |
| Armored Fury | China – GLA | 13 / 4 / 3 |
| Bitter Winter | China – GLA | 6 / 14 / 0 |
| Armored Fury | America – China | 8 / 3 / 9 |
| Alpine Assault | America – China | 12 / 1 / 7 |
| Death Valley | ChinaTank – AirForce | 4 / 3 / 13 |

Die Erhebung stammt aus dem Auftrag und ist hier nicht wiederholt worden; gemessen ist ein Gefecht (Alpine Assault, America – China, Seed 7): America siegt bei Tick
8.910, Endhash `D53EDB8AFD67B61B` nach 9.211 Ticks – gleich in `SimTool ai-match --seed 7` und im Client als Zuschauer (Rauchtest mit Tempowechseln, Nebelschalter und
Klicks). „Unentschieden“ heißt: kein Sieger im Tickbudget – im Fenster wäre das eine Stunde Spielzeit.

**Vorschlag, nicht gebaut** (ändert das Verhalten der Simulation und damit jeden Referenzwert der KI-Läufe; Entscheidung des Projektinhabers): Dem Original am nächsten
und im Bild am sichtbarsten wären zwei zusätzliche Würfe aus dem vorhandenen Strom der KI – (a) **der Anmarschweg je Welle** (Mitte, Flanke oder Hintertür, wie
`doTeamFollowSkirmishApproachPath` mit den Wegmarken der Karte; heute bekommt jede Einheit einer Welle einen `AttackMoveCommand` auf denselben Zielpunkt, eine Wahl
des Wegs gibt es nicht), und (b) **die Wahl eines `SkillSet`** der Seite zu Beginn statt der festen Regel „die billigste kaufbare Wissenschaft“
(`SkirmishAi.BuyScience`). Beides sind einzelne Würfe je Welle bzw. je Gefecht, kein neuer Zustand außer dem gewählten Wert. Nötig für
das Ziel dieser Demo ist es nicht: die Erhebung zeigt, dass der Ausgang schon mit dem Seed allein wechselt.

## 9. Schwierigkeitsgrade der KI

**Bei uns: nein.** `SkirmishAi` kennt keine Schwierigkeit (`SkirmishAiOptions` hat nur `AttackThreshold`, `MaxAttackThreshold`, `EconomyFirst`); es gibt einen
Computerspieler, der `DIFFICULTY_NORMAL` entspricht (`docs/SIMULATION-CORE.md` §35: „keine der in den Daten belegten Wirkungen gilt für einen Computerspieler im
Skirmish“). Die Shell bietet entsprechend eine Computerarmee an (`GUI:MediumAI`, `ShellSkirmishText.ComputerArmy`); `SimTool ai-match` hat keine Option dafür.

**Im Original: drei Stufen** – „Easy Army“, „Medium Army“, „Hard Army“ (`SLOT_EASY_AI`, `SLOT_MED_AI`, `SLOT_BRUTAL_AI`; letzteres ist `DIFFICULTY_HARD`, eine vierte
Stufe gibt es nicht). `GameLogic::startNewGame` (`GameLogic/System/GameLogic.cpp`, Zeilen 1429–1431) schreibt die Stufe als `skirmishDifficulty` in die Spielerdaten,
`Player::initFromDict` gibt sie an die KI. Was sich unterscheidet:

- **Skripte und Teams je Stufe – der eigentliche Hebel.** `ScriptEngine::executeScript` (`GameLogic/ScriptEngine/ScriptEngine.cpp`, Zeilen 6964–6966) führt ein Skript
  nur aus, wenn sein Schalter `isEasy`/`isNormal`/`isHard` zur Stufe des Spielers passt; `TeamPrototype::evaluateProductionCondition` (`Common/RTS/Team.cpp`, ab Zeile
  1156) filtert die Produktionsbedingungen der Teams genauso. Welche Teams und Angriffe eine Stufe hat, steht damit in `SkirmishScripts.scb`, nicht im Code.
- **Sammler:** `AIPlayer::checkForSupplyCenter` (`AIPlayer.cpp`, Zeilen 190–220) nimmt `ResourceGatherersEasy/Normal/Hard` der Seite plus eins. In den ausgelieferten
  Daten sind die drei Werte je Seite gleich (2 für USA und China, 5 für die GLA) – ohne Wirkung.
- **Zielwahl:** Auf „leicht“ bekommt ein Team kein gemeinsames Ziel (`Team::setTeamTargetObject`, `Team.cpp` Zeile 1510) und ein Angriffstrupp wählt sein Opfer
  zufällig (`AIAttackSquadState::chooseVictim`, `AIStates.cpp` ab Zeile 5970); „normal“ und „schwer“ haben eigene Regeln.
- **Nicht je Stufe:** die Zeitwerte von `AIData.ini` (`StructureSeconds 0`, `TeamSeconds 10`, `Wealthy 7000`, `Poor 2000`, `StructuresWealthyRate`/`TeamsWealthyRate
  2.0`, `…PoorRate 0.6`) gelten für alle.
- **Kein Geld- oder Bautempo-Bonus im Skirmish.** Die Boni je Stufe (`WeaponBonus SOLO_AI_EASY/NORMAL/HARD RATE_OF_FIRE` 80/100/120 %,
  `HumanSoloPlayerHealthBonus_Easy/Normal/Hard` 150/100/80 %) wendet `Player::friend_applyDifficultyBonusesForObject` nur im Einzelspielermodus der Kampagne an
  (`isInSinglePlayerGame`). Ein Geld- oder Produktionskostenbonus je Stufe wurde in `Player.cpp` nicht gefunden.

Stufen bei uns hießen also vor allem: die Skirmish-Skripte ausführen. Das ist nicht Teil dieser Aufgabe.

## 10. Grenzen

- Kein Mensch hat den Modus gesehen oder bedient. Lesbarkeit, Farben, Schriftgrößen, das Gefühl der Kamera und des Folgens sind unbeurteilt.
- Einkommen zählt nur Einzahlungen von Versorgungsgütern. Hacker, Schwarzmärkte, Ölfördertürme, Verkäufe und Kisten erzeugen kein `SupplyDeposited` und fehlen.
- Die Produktionstafel zeigt Einheiten und Baustellen, keine laufenden Upgrades und Wissenschaften.
- Der Zuschauer kann nichts auswählen oder untersuchen (keine Lebensbalken, keine Einheiteninfo).
- Ton: Eva und die Musik gehören weiter zu Spieler 1 (das HUD des Spielers läuft unsichtbar mit, weil Ton und Eingabeskript daran hängen).
- Beim Umschalten der Nebelsicht vergisst die Darstellung, was die vorige Sicht sich gemerkt hatte (Geisterbilder gesehener Gebäude); erkundete Szenerie wird aus der
  Sichtkarte neu gelesen.
- Die Kopfleiste zeigt zwei Spieler nebeneinander, weitere als Zeilen; Folgen und Springen gibt es für KI 1 und 2.
- Sind nur einige Befehle eines Typs in einem Tick abgelehnt, zählen alle für die KI-Kamera (§5).
- Kein Bild-im-Bild (§5). Weit oben lässt sich nicht scrollen (§6). Außerhalb der Karte ist das Bild einfarbig.
- Kein Eintrag in der Shell: der Modus startet nur über die Befehlszeile bzw. das Skript. Kein Speichern, kein Replay-Export aus dem Fenster.
- Bei 60 und 120 Hz läuft der Modus (Test bei 60 Hz; ein Rauchtest mit `-TickRate 60`), der Verlauf eines Gefechts ist dort aber ein anderer als bei 30 Hz, und die
  Erhebung der Paarungen gilt für 30 Hz.

## 11. Checkliste für einen Menschen

Start: `pwsh scripts/demo-spectator.ps1 -Seed 7 -Map "Alpine Assault" -FactionA FactionAmerica -FactionB FactionChina -Speed 2` (rund fünf Minuten Spielzeit bis zum Sieg).

1. **Start.** Fenster zeigt die Basis von Spieler 1 ohne Nebel; Kopfleiste mit beiden Fraktionen, Geld 10.000; die Spielzeit läuft. Ist alles lesbar (Größe, Kontrast)?
2. **Tafeln.** `P`, `U`, `L`, `E` drücken: wechselt der Reiter, stimmt die Hervorhebung, passen die Bildchen zu den Namen, laufen die Fortschrittsbalken?
3. **Zoom.** Mausrad nach hinten: geht es weit über den gewohnten Zoom hinaus, flimmert das Gelände, fehlen Objekte oder Kacheln, ruckelt es?
4. **Übersicht.** `O`: ganze Karte im Bild? Noch einmal `O`: zurück an denselben Ort?
5. **Nebel.** `F` dreimal: alles → nur Sicht von Spieler 1 → nur Sicht von Spieler 2 → alles. Stimmen Radar und Welt überein?
6. **KI-Kamera.** `F1`: folgt die Ansicht dem blauen Rahmen des Radars, und sieht man dort, was die KI gerade tut (Bauplatz, Kaserne, die Welle)? Wirkt das Folgen
   ruhig oder springt es zu oft? `F2` für die andere KI, `J`/`K` zum Springen, Pfeiltaste löst das Folgen.
7. **Tempo.** Leertaste hält an (PAUSED in der Mitte), `4` und `8` beschleunigen, `1` normal. Bleibt das Bild bei 8× flüssig?
8. **Knöpfe.** Jeden Knopf der unteren Leiste einmal anklicken; Klick aufs Radar versetzt die Ansicht.
9. **Nichts befehlen.** Links- und Rechtsklick auf Einheiten und Boden: es darf nichts ausgewählt werden und nichts geschehen.
10. **Ende.** Bis zum Sieg warten: erscheint `WINNER: …`, steht das Spiel, stimmen die Verluste der Tafel mit dem Gesehenen überein?
11. **Zufall.** Zweimal ohne `-Seed` starten: andere Seeds, anderer Verlauf? Einmal mit dem ausgegebenen Seed wiederholen: derselbe Verlauf?

## 12. Tests und Prüfungen

- `tests/CnCNew.Presentation.Tests/SpectatorTests.cs` (ohne Spieldaten): Zoomgrenze des Spielers und des Zuschauers, Fernebene, Übersichtshöhe, Blickpunkt je Befehl,
  Gewicht und Haltezeit, Welle mit mitwandernder Mitte, Gleiten und Schnitt, gleiche Kamera bei 30/60/120 Hz.
- `tests/CnCNew.Game.Tests/SpectatorTests.cs` (`--only SpectatorTests`): Einkommen je Spielminute bei drei Raten; Tasten und Knöpfe; mit Spieldaten: zwei Läufe
  desselben Gefechts liefern denselben Bericht (Tafeln, Kameras, Hash) und die Tafeln haben Daten; nichts vom Zuschauer erreicht die Simulation (Hash gleich der reinen
  Sitzung, Befehlsliste gleich lang, Senke lehnt ab); Nebelschalter; Übersicht, Grenzen, Folgen; Zeichenliste bei drei Auflösungen und Klick auf Knöpfe; Einkommen gegen
  die Ereignisse bei 30 und 60 Hz.
- `scripts/check-spectator.ps1`: der echte Client ohne Fenster mit `scripts/input/spectator.txt`; prüft an der Ausgabe (Zeilen `spectator …`), dass das Gefecht lief,
  beide KI befahlen, kein Zuschauerbefehl aufgezeichnet wurde, alle Tafeln, Nebelsichten und Tempi an waren, die KI-Kameras Blickpunkte fanden und die Kamera die
  Übersichtshöhe erreichte; legt zehn benannte PNG unter `%TEMP%\cncnew-spectator-check` ab.
- Unverändert: `SimTool input-manifest check` und `reference-manifest check --set quick` (die Simulation rechnet nichts anders), die Golden-Hashes der vorhandenen Screens.
