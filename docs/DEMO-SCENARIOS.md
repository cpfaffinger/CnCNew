# Gestellte Demos zum Auditieren

Stand: 7. Oktober 2026. Drei kleine Verhaltensszenen und zwölf Fabrikausfahrten (eigener Abschnitt unten) auf einer echten Karte (Alpine Assault, Ursprung = erste Startposition), die je **ein** Verhalten isolieren. Dieselbe Definition
(`CnCNew.Game/Live/DemoScenarios.cs`) läuft im Client und headless; `LiveScenario` (`CnCNew.Game/Live/LiveScenario.cs`) setzt genau die genannten Objekte (keine Basen, keine Wirtschaft),
deckt die Karte für beide Seiten auf und gibt Befehle über dasselbe `StandingOrders`-Buch wie ein Spieler. Die Szenen sind **Prüfstände**: ein fehlgeschlagener Check ist ein Befund
über den Stand der Simulation/Darstellung, kein Fehler des Werkzeugs. Alle drei bestehen seit den Fixes vom 4. Oktober 2026; die Sichtprüfung durch einen Menschen steht aus.

**Zeiteinheit.** Alle Zeitangaben dieser Szenen, der Audit-Ausgabe (`t=`, „tick“, „ticks“) und der Optionen `-Every`/`--every`/`--ticks` sind **Originalbilder** (1/30 s Spielzeit),
nicht Simulationsticks. Die Szenen sind in Originalbildern verfasst (`DemoScenario.Frames`, `ScenarioOrder.Frame`), der Audit nimmt einen Abtastwert je Originalbild und rechnet jeden Tick, den
die Simulation herausgibt, beim Einlesen um; im Rauchtest des Clients ist ein gezeichnetes Bild ein Originalbild. Bei 30 Hz ist ein Originalbild ein Tick, bei 60 Hz (`-TickRate 60` an
`audit-scenarios.ps1` und `capture-scenarios.ps1`, `--tick-rate 60` an Client und `scenario-audit`) sind es zwei – die Zahlen unten bedeuten bei jeder Rate dieselbe Spielzeit
(`docs/TICKRATE-MIGRATION.md` §14).

| Szene | Zeigt | Stand des Audits (Zahlen: Originalbilder, gemessen am 6. Oktober 2026 bei 30 Hz) |
| --- | --- | --- |
| `capture-building` | Ranger geht zur gegnerischen Kaserne, packt aus, hisst die Flagge (`RAISING_FLAG`), packt ein, Gebäude wechselt die Seite | **grün.** Gestufte Fähigkeit (`docs/SIM-STAGED-CAPTURE.md`): Anlaufen ab Bild 12, `UNPACKING` ab Bild 139 (90 Bilder = 3 s), `RAISING_FLAG` ab Bild 229 (600 Bilder = 20 s), Übernahme in Bild 828, `PACKING` ab Bild 829 (60 Bilder = 2 s). Bei 60 Hz dieselben Bilder (Anlaufen ab Bild 11); die Zahl der Blitze während des Flaggehissens ist dort eine andere (49 bei 30 Hz, 99 bei 60 Hz – `docs/TICKRATE-MIGRATION.md` §5.1, Phase 23) |
| `tank-duel` | Crusader gegen Battlemaster, schräg zueinander; der Turm muss sich vor dem ersten Schuss drehen, bleibt bis zum Tod des Gegners auf ihn gerichtet und dreht nach der Rückstellzeit zurück; eine Sentry-Drohne tastet im Leerlauf ab | **grün.** Turm- und Angriffszustand in der Simulation (`docs/SIM-TURRET-AIM.md`): der Crusader schießt erst in Bild 11 (45° bei 180° je Sekunde, also 6° je Originalbild), jeder Schuss mit 0° Fehler, in **jedem** Abtastwert bis zum Tod innerhalb 5°; nach dem Tod (Bild 382) hält der Turm seine `RecenterTime` von 60 Bildern (2 s) und dreht ab Bild 444 zurück; die Drohne tastet 9-mal ab (Haltezeit 31 Bilder, Ruhe 8 Bilder dazwischen). Bei 60 Hz: erster Schuss in Bild 10, Tod in Bild 380, Rückdrehen ab Bild 441 |
| `jet-strafe` | Der Flugplatz baut zwei Raptors; Start auf der Startbahn des Modells, Anflug, Raketen auf zwei Missile Defender, Beschuss, Kreisen, Rückflug, Landung, Nachladen | **grün.** Flügelmodell (`docs/SIM-JET-FLIGHT.md`): Mindestgeschwindigkeit, begrenzte Kurvenrate, Kreisen; Bahnpunkte aus dem Flugplatzmodell; beide Raptors starten, feuern 4 Raketen, landen und laden nach |

(Hinweis zur Wahl der Ziele: Stinger-Soldaten sind laut `Armor.ini` (`StingerSoldierArmor`) **unverwundbar gegen Jet-Raketen**, deshalb ist der „Raketentrooper“ hier der Missile Defender.)

## Ausführen

```powershell
.\scripts\demo-scenario.ps1                              # OHNE Namen: listet die Szenen und startet nichts (bis 7. Oktober 2026 startete das stillschweigend tank-duel)
.\scripts\demo-scenario.ps1 -Scenario tank-duel          # Fenster, Skirmish-HUD (Befehle des Spielers sind möglich)
.\scripts\demo-capture-building.ps1                      # optischer Test im Fenster, je Szene ein Skript
.\scripts\demo-tank-duel.ps1
.\scripts\demo-jet-strafe.ps1
.\scripts\capture-scenarios.ps1                          # alle drei als Bildfolge (PNG alle N Originalbilder) + index.html unter %TEMP%\cncnew-scenario-frames
.\scripts\capture-scenarios.ps1 -Scenario tank-duel -Every 5 -Size 1920x1080
.\scripts\demo.ps1 scenario -Scenario jet-strafe -SmokeTest
.\scripts\audit-scenarios.ps1                            # alle drei headless prüfen, Ausgaben unter %TEMP%\cncnew-scenarios
.\scripts\audit-scenarios.ps1 -Scenario tank-duel -Every 10
.\scripts\audit-scenarios.ps1 -TickRate 60                # dieselben Prüfungen mit 60 Simulationsticks je Sekunde; -Every und alle Ausgaben bleiben Originalbilder
dotnet run -c Release --project tools/CnCNew.PresentationTool -- scenario-audit .local/phase2-import-a --name capture-building --debug-fire ranger,barracks
```

`scripts/capture-world.ps1` enthält je Szene eine Aufnahme (`scenario-tank-duel`, `scenario-capture`, `scenario-jet`). `Game.Tests` (`ScenarioTests`) prüft mit echten Daten, dass jede Szene
ihre Objekte setzt, ihre Befehle ausführt und zweimal denselben Zustandshash liefert; sie prüft **nicht** die oben genannten Befunde (sonst wäre die Suite rot).

## Was ein grüner Audit bedeutet

- `capture-building`: gestufte Fähigkeit nach `SpecialAbilityUpdate.cpp` (Anlaufen, `UNPACKING` UnpackTime, `RAISING_FLAG` PreparationTime, Übernahme, `PACKING` PackTime).
- `tank-duel`: kein Schuss mit mehr als 5° Turmfehler; der Turm hat sich vor dem ersten Schuss gedreht (Turmzustand und Feuerfreigabe in der Simulation, `TurretAI`); in jedem Abtastwert (einer je Originalbild) zwischen Befehl und Tod des Opfers (auch in den Nachladelücken) bleibt der Turm innerhalb 5°; nach dem Tod hält der Turm genau `RecenterTime` (Spielzeit aus den Daten) und dreht dann zurück; die abseits stehende Sentry-Drohne dreht im Leerlauf zufällig umher (Winkelbereich, Haltezeit, Ruheintervall) und gezeichneter Turm sowie `TURRET_ROTATE` folgen dem simulierten Winkel.
- `jet-strafe`: kein Stillstand in der Luft über 10 Originalbilder (eine Drittelsekunde; Mindestgeschwindigkeit und Kreisen des Flügel-Locomotors).

## Zuschauer-Demo (KI gegen KI)

Keine gestellte Szene, sondern ein ganzes Skirmish, dem man zusieht: `pwsh scripts/demo-spectator.ps1` startet im Fenster ein Gefecht zweier Computerspieler ohne Spieler
(ganze Karte ohne Nebel, weiter Zoom und Kartenübersicht, Folgen der simulierten Kamera einer KI, Beobachter-Tafeln für Produktion, Einheiten, Verluste und Wirtschaft). Je Start
zieht das Skript einen neuen Seed und gibt ihn aus; `-Seed N` wiederholt genau dasselbe Gefecht. `pwsh scripts/check-spectator.ps1` fährt denselben Modus ohne Fenster mit gedrückten
Zuschauertasten, prüft die Ausgabe des Clients und legt zehn Bilder unter `%TEMP%\cncnew-spectator-check` ab. Alles Weitere (Tasten, Herkunft der Werte, Grenzen, Checkliste für die
Sichtprüfung) steht in `docs/SPECTATOR-MODE.md`; von einem Menschen gesehen ist davon nichts.
## Fabrikausfahrten (`exit-<fraktion>-<gebäude>`, 7. Oktober 2026)

Frage des Projektinhabers: fahren frisch gebaute Einheiten aus ihrer Fabrik heraus, oder „clippen“ sie hinaus? Zwölf Szenen (`CnCNew.Game/Live/FactoryExits.cs`) stellen je Fraktion
(`america`, `china`, `gla`) die Kommandozentrale (baut Planierraupen bzw. Arbeiter), die Kaserne (Ranger, Rotgardist, Rebell) und die Waffenfabrik bzw. den Waffenhändler (Crusader,
Battlemaster, Scorpion) einzeln nahe an die Kamera (`exit-america-warfactory` …) oder alle drei in ein Bild (`exit-america-all`, dazu ein Bauarbeiter, Kraftwerk und Versorgungslager –
dieselbe Szene benutzt der Bautasten-Test). Jedes Gebäude bekommt ab Bild 30 drei Bauaufträge nacheinander. Alle Gebäude stehen im Standardwinkel: die Ausfahrt zeigt nach Osten; die Kamera blickt aus Südosten
auf die Torseite, im Bild fährt die Einheit also nach rechts unten hinaus.

```powershell
.\scripts\demo-factory-exits.ps1                                    # Fenster: America, alle drei Gebäude; druckt, worauf zu achten ist
.\scripts\demo-factory-exits.ps1 -Faction China -Building warfactory
.\scripts\demo-factory-exits.ps1 -Faction GLA -Building barracks -TickRate 60
.\scripts\audit-scenarios.ps1 -Scenario exits [-Faction America]    # dieselben neun Einzelszenen ohne Fenster, mit Zahlen
.\scripts\capture-scenarios.ps1 -Scenario exit-america-warfactory   # Bildfolge (900 Originalbilder, alle 15 eines)
```

### Maßstab: das Original

Quellen: `ProductionUpdate.cpp` (`ProductionUpdate::update`, `updateDoors`), `DefaultProductionExitUpdate.cpp` und `QueueProductionExitUpdate.cpp` (`exitObjectViaDoor`,
`getNaturalRallyPoint`), `SupplyCenterProductionExitUpdate.cpp`, `AIStates.cpp` (`AIFollowPathState` für `aiFollowExitProductionPath`).

1. Ist der Bauauftrag fertig, reserviert `ProductionUpdate::update` ein Tor. Hat das Gebäude Toranimationen (`NumDoorAnimations > 0`), beginnt das Tor zu öffnen
   (`DOOR_1_OPENING` für `DoorOpeningTime`); die Einheit wird **erst erzeugt, wenn das Tor ganz offen ist** (`m_doorWaitOpenFrame != 0`, `DOOR_1_WAITING_OPEN`). Das Tor bleibt
   `DoorWaitOpenTime` nach der letzten Einheit offen und schließt dann über `DoorCloseTime` (`DOOR_1_CLOSING`).
2. `exitObjectViaDoor` setzt die Einheit auf den ins Gebäude gedrehten `UnitCreatePoint` – das ist **im Gebäude** (Kaserne: die Gebäudemitte) –, mit der Ausrichtung des Gebäudes.
3. Die Einheit bekommt einen Ausfahrtsweg (`aiFollowExitProductionPath`): zuerst `NaturalRallyPoint`, um zwei Wegfindungszellen (20 Einheiten) in seiner eigenen Richtung nach außen
   geschoben (`getNaturalRallyPoint(…, offset = TRUE)`; der Punkt liegt laut Kommentar der Daten immer auf der Gebäudekante), danach – falls gesetzt – der Sammelpunkt des Spielers.
   Die eigene Fabrik ist auf diesem Weg kein Hindernis (`ignoreObstacle`).
4. `QueueProductionExitUpdate` (chinesische Kaserne) lässt nach jeder Einheit `ExitDelay` (300 ms) verstreichen, damit die paarweise gebauten Rotgardisten nacheinander herauskommen.

| Gebäude | Ausfahrtsmodul | `UnitCreatePoint` → `NaturalRallyPoint` (Gebäudekoordinaten) | Halbe Kantenlängen | Tor: auf / offen / zu (Bilder) |
| --- | --- | --- | --- | --- |
| `AmericaCommandCenter` | Default | (−18, 35) → (60, 35) | 60 × 70 | 45 / 90 / 45 |
| `AmericaBarracks` | Default | (0, 0) → (55, 0) | 55 × 45 | kein Tor |
| `AmericaWarFactory` | Default | (−10, −30) → (53, −30) | 53 × 60 | 98 / 90 / 120 |
| `ChinaCommandCenter` | Default | (−18, 40) → (60, 40) | 60 × 63 | 90 / 90 / 90 |
| `ChinaBarracks` | Queue (`ExitDelay` 300 ms) | (0, −25) → (36, −25) | 36 × 44 | kein Tor |
| `ChinaWarFactory` | Default | (0, −27) → (57, −27) | 57 × 61 | 120 / 60 / 150 |
| `GLACommandCenter` | Default | (25, 0) → (65, 0) | 65 × 65 | kein Tor |
| `GLABarracks` | Default | (0, 0) → (42, 0) | 42 × 52 | kein Tor |
| `GLAArmsDealer` | Default | (−20, −15) → (40, −15) | 40 × 62 | 60 / 90 / 60 |

### Was der Audit misst und prüft (`tools/CnCNew.PresentationTool/FactoryExitAudit.cs`)

Je Originalbild, in Gebäudekoordinaten (x zur Ausfahrtsseite): Erscheinungsbild und -ort jeder gebauten Einheit, ihr Weg in den ersten drei Sekunden (alle 15 Bilder), das erste Bild
außerhalb des Gebäudekastens und die Stelle dort, der Ruheort, ob sie in jedem Bild gezeichnet wird, und die Zeitleiste der Torzustände des Gebäudes. Prüfungen: (a) Erscheinen am
`UnitCreatePoint` (±1,5); (b) bei Gebäuden mit Tor: das Tor zeigt `DOOR_1_WAITING_OPEN`, wenn die Einheit erscheint, und nach der letzten Einheit folgen `DOOR_1_CLOSING` und „zu“;
(c) die Einheit verlässt den Kasten über die Ausfahrtsseite (x ≥ halbe Länge, höchstens 20 neben der Linie des `NaturalRallyPoint`); (d) sie ruht höchstens 30 vom Ende des
Ausfahrtswegs; (e) sie wird in jedem Bild gezeichnet; (f) je Auftrag erscheint eine Einheit.

### Befund (gemessen 7. Oktober 2026, 30 Hz; **nicht von einem Menschen gesehen**)

| Prüfung | Ergebnis über alle neun Gebäude (27 Einheiten) |
| --- | --- |
| (a) Erscheinungsort | 27 von 27 genau am `UnitCreatePoint`, im Gebäude – wie im Original |
| (b) Tor | bei allen fünf Gebäuden mit Tor: offen (`WAITING_OPEN`), wenn die Einheit erscheint; schließt nach der letzten (z. B. `AmericaWarFactory`: öffnet ab Bild 231, offen ab 330, Einheit in Bild 330, schließt ab 1020, zu ab 1141). Vor dieser Sitzung war das Tor beim Erscheinen der **ersten** Einheit ein Bild lang noch `OPENING` (behoben in `StructureConditionTracker`: Vorlauf `DoorOpeningTime` + 1 Tick) |
| (c) Ausfahrt | **0 von 27**: keine Einheit bewegt sich. Sie bleibt am `UnitCreatePoint` im Gebäude stehen; drei nacheinander gebaute Einheiten stehen auf demselben Punkt ineinander |
| (d) Haltepunkt | entfällt (keine Ausfahrt): Abstand zum Ende des Ausfahrtswegs 54 bis 96 Einheiten |
| (e) gezeichnet | 27 von 27 in jedem Bild (die Einheit ist also im Gebäudemodell sichtbar bzw. von ihm verdeckt) |
| (f) Stückzahl | 27 von 27 Aufträgen liefern **eine** Einheit – auch die chinesische Kaserne, die im Original je Auftrag **zwei** Rotgardisten baut (`QuantityModifier = ChinaInfantryRedguard 2`, in der Simulation typisiert, aber nicht angewendet; nur gemeldet) |

Das ist, was der Projektinhaber als „clippt gerade hinaus“ sieht: die Raupe steht nach dem Bau **in** der Kommandozentrale und fährt erst auf einen Befehl hin – dann quer durch das
Gebäude. Die Abweichung liegt in der **Simulation** (`ProductionUpdate.Update` erzeugt die Einheit am `UnitCreatePoint` und gibt ihr keinen Weg; nur ein vom Spieler gesetzter
Sammelpunkt bewegt sie), nicht in der Darstellung. Zwei weitere Abweichungen der Simulation, nur gemeldet: die Einheit entsteht, sobald der Auftrag fertig ist, nicht erst nach
`DoorOpeningTime` (die Darstellung öffnet das Tor dafür entsprechend früher, der ganze Torzyklus liegt um `DoorOpeningTime` vor dem Original); und die Stückzahl der Rotgardisten.

Weil der Hauptzweig mitten in einer Messphase liegt, in der die Simulation bei 30 Hz bytegleich bleiben muss (`baselines/reference-manifest.json`), steht die Korrektur der
Ausfahrt in einem **eigenen, letzten Commit** des Zweigs (`docs/SIMULATION-CORE.md`, Abschnitt „Fabrikausfahrt“); solange er nicht gemergt ist, ist `audit-scenarios.ps1 -Scenario exits`
in Prüfung (c)/(d) **rot** – das ist der Befund, kein Werkzeugfehler.

### Checkliste für den Menschen (`scripts/demo-factory-exits.ps1`)

1. Die Einheit erscheint im Gebäude, nicht davor und nicht auf dem Dach.
2. Gebäude mit Tor (beide Kommandozentralen außer GLA, alle drei Fabriken): das Tor ist ganz offen, wenn die Einheit erscheint, und schließt danach wieder.
3. Die Einheit fährt/geht von selbst durch die Torseite (im Bild rechts unten) hinaus und hält kurz vor dem Gebäude.
4. Die zweite und dritte Einheit stellen sich neben die erste, nicht in sie hinein.
5. Sitzt das Modell der Einheit während der Ausfahrt sichtbar richtig im Tor (Höhe, Richtung), oder ragt es durch Wand oder Dach?
6. Mit gesetztem Sammelpunkt (Gebäude wählen, Rechtsklick auf den Boden): erst hinaus, dann zum Sammelpunkt.
