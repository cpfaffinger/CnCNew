# Gestellte Demos zum Auditieren

Stand: 4. Oktober 2026. Drei kleine Szenen auf einer echten Karte (Alpine Assault, Ursprung = erste Startposition), die je **ein** Verhalten isolieren. Dieselbe Definition
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
