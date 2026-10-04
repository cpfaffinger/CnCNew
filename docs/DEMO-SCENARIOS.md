# Gestellte Demos zum Auditieren

Stand: 4. Oktober 2026. Drei kleine Szenen auf einer echten Karte (Alpine Assault, Ursprung = erste Startposition), die je **ein** Verhalten isolieren. Dieselbe Definition
(`CnCNew.Game/Live/DemoScenarios.cs`) läuft im Client und headless; `LiveScenario` (`CnCNew.Game/Live/LiveScenario.cs`) setzt genau die genannten Objekte (keine Basen, keine Wirtschaft),
deckt die Karte für beide Seiten auf und gibt Befehle über dasselbe `StandingOrders`-Buch wie ein Spieler. Die Szenen sind **Prüfstände**: ein fehlgeschlagener Check ist ein Befund
über den Stand der Simulation/Darstellung, kein Fehler des Werkzeugs. Alle drei bestehen seit den Fixes vom 4. Oktober 2026; die Sichtprüfung durch einen Menschen steht aus.

| Szene | Zeigt | Stand des Audits (4. Oktober 2026, nach den Fixes) |
| --- | --- | --- |
| `capture-building` | Ranger geht zur gegnerischen Kaserne, packt aus, hisst die Flagge (`RAISING_FLAG`), packt ein, Gebäude wechselt die Seite | **grün.** Gestufte Fähigkeit (`docs/SIM-STAGED-CAPTURE.md`): Anlaufen ab Tick 12, `UNPACKING` Tick 139 (90 Ticks), `RAISING_FLAG` Tick 229 (600), Übernahme Tick 828, `PACKING` 829 (60) |
| `tank-duel` | Crusader gegen Battlemaster, schräg zueinander; der Turm muss sich vor dem ersten Schuss drehen, bleibt bis zum Tod des Gegners auf ihn gerichtet und dreht nach der Rückstellzeit zurück; eine Sentry-Drohne tastet im Leerlauf ab | **grün.** Turm- und Angriffszustand in der Simulation (`docs/SIM-TURRET-AIM.md`): der Crusader schießt erst in Tick 13 (45° bei 6° je Tick), jeder Schuss mit 0° Fehler, in **jedem** Tick bis zum Tod innerhalb 5°; nach dem Tod (Tick 384) hält der Turm 60 Ticks und dreht ab Tick 446 zurück; die Drohne tastet 9-mal ab (Haltezeit 31 Ticks, Ruhe 8 Ticks dazwischen) |
| `jet-strafe` | Der Flugplatz baut zwei Raptors; Start auf der Startbahn des Modells, Anflug, Raketen auf zwei Missile Defender, Beschuss, Kreisen, Rückflug, Landung, Nachladen | **grün.** Flügelmodell (`docs/SIM-JET-FLIGHT.md`): Mindestgeschwindigkeit, begrenzte Kurvenrate, Kreisen; Bahnpunkte aus dem Flugplatzmodell; beide Raptors starten, feuern 4 Raketen, landen und laden nach |

(Hinweis zur Wahl der Ziele: Stinger-Soldaten sind laut `Armor.ini` (`StingerSoldierArmor`) **unverwundbar gegen Jet-Raketen**, deshalb ist der „Raketentrooper“ hier der Missile Defender.)

## Ausführen

```powershell
.\scripts\demo-scenario.ps1 -Scenario tank-duel          # Fenster, Skirmish-HUD (Befehle des Spielers sind möglich)
.\scripts\demo-capture-building.ps1                      # optischer Test im Fenster, je Szene ein Skript
.\scripts\demo-tank-duel.ps1
.\scripts\demo-jet-strafe.ps1
.\scripts\capture-scenarios.ps1                          # alle drei als Bildfolge (PNG alle N Ticks) + index.html unter %TEMP%\cncnew-scenario-frames
.\scripts\capture-scenarios.ps1 -Scenario tank-duel -Every 5 -Size 1920x1080
.\scripts\demo.ps1 scenario -Scenario jet-strafe -SmokeTest
.\scripts\audit-scenarios.ps1                            # alle drei headless prüfen, Ausgaben unter %TEMP%\cncnew-scenarios
.\scripts\audit-scenarios.ps1 -Scenario tank-duel -Every 10
dotnet run -c Release --project tools/CnCNew.PresentationTool -- scenario-audit .local/phase2-import-a --name capture-building --debug-fire ranger,barracks
```

`scripts/capture-world.ps1` enthält je Szene eine Aufnahme (`scenario-tank-duel`, `scenario-capture`, `scenario-jet`). `Game.Tests` (`ScenarioTests`) prüft mit echten Daten, dass jede Szene
ihre Objekte setzt, ihre Befehle ausführt und zweimal denselben Zustandshash liefert; sie prüft **nicht** die oben genannten Befunde (sonst wäre die Suite rot).

## Was ein grüner Audit bedeutet

- `capture-building`: gestufte Fähigkeit nach `SpecialAbilityUpdate.cpp` (Anlaufen, `UNPACKING` UnpackTime, `RAISING_FLAG` PreparationTime, Übernahme, `PACKING` PackTime).
- `tank-duel`: kein Schuss mit mehr als 5° Turmfehler; der Turm hat sich vor dem ersten Schuss gedreht (Turmzustand und Feuerfreigabe in der Simulation, `TurretAI`); in jedem Tick zwischen Befehl und Tod des Opfers (auch in den Nachladelücken) bleibt der Turm innerhalb 5°; nach dem Tod hält der Turm genau `RecenterTime` und dreht dann zurück; die abseits stehende Sentry-Drohne dreht im Leerlauf zufällig umher (Winkelbereich, Haltezeit, Ruheintervall) und gezeichneter Turm sowie `TURRET_ROTATE` folgen dem simulierten Winkel.
- `jet-strafe`: kein Stillstand in der Luft über 10 Ticks (Mindestgeschwindigkeit und Kreisen des Flügel-Locomotors).
