# Gestellte Demos zum Auditieren

Stand: 4. Oktober 2026. Drei kleine Szenen auf einer echten Karte (Alpine Assault, Ursprung = erste Startposition), die je **ein** Verhalten isolieren. Dieselbe Definition
(`CnCNew.Game/Live/DemoScenarios.cs`) läuft im Client und headless; `LiveScenario` (`CnCNew.Game/Live/LiveScenario.cs`) setzt genau die genannten Objekte (keine Basen, keine Wirtschaft),
deckt die Karte für beide Seiten auf und gibt Befehle über dasselbe `StandingOrders`-Buch wie ein Spieler. Die Szenen sind **Prüfstände**: ein fehlgeschlagener Check ist ein Befund
über den Stand der Simulation/Darstellung, kein Fehler des Werkzeugs.

| Szene | Zeigt | Stand des Audits (4. Oktober 2026) |
| --- | --- | --- |
| `capture-building` | Ranger geht zur gegnerischen Kaserne, packt aus, hisst die Flagge (`RAISING_FLAG`), packt ein, Gebäude wechselt die Seite | **besteht** (gestufte Fähigkeit, `docs/SIM-STAGED-CAPTURE.md`): `UNPACKING` ab Tick 139 (90 Ticks), `RAISING_FLAG` ab 229 (600), Übernahme Tick 828, `PACKING` ab 829 (60) |
| `tank-duel` | Crusader gegen Battlemaster, schräg zueinander; der Turm muss sich vor dem ersten Schuss drehen | **1 Check scheitert:** der Crusader schießt in Tick 5 mit 45° Winkelfehler; die Simulation kennt keinen Turmzustand und schießt sofort, die Darstellung dreht den Turm erst nach dem Schuss. Danach stimmt der Winkel bei jedem Schuss (0°) |
| `jet-strafe` | Der Flugplatz baut zwei Raptors; Start, Anflug, Raketen auf zwei Missile Defender, Beschuss, Rückflug, Landung, Nachladen | Start, Angriff (4 Schuss je Jet), Treffer, Rückflug, Landung bestehen. Beobachtung: der Jet bleibt am Ende des Angriffsflugs **bewegungslos in der Luft** stehen (`MinSpeed` 60 und `CirclingRadius` 100 des Raptor-Locomotors werden nicht umgesetzt) – der Audit meldet das als Fehlschlag |

(Hinweis zur Wahl der Ziele: Stinger-Soldaten sind laut `Armor.ini` (`StingerSoldierArmor`) **unverwundbar gegen Jet-Raketen**, deshalb ist der „Raketentrooper“ hier der Missile Defender.)

## Ausführen

```powershell
.\scripts\demo-scenario.ps1 -Scenario tank-duel          # Fenster, Skirmish-HUD (Befehle des Spielers sind möglich)
.\scripts\demo.ps1 scenario -Scenario jet-strafe -SmokeTest
.\scripts\audit-scenarios.ps1                            # alle drei headless prüfen, Ausgaben unter %TEMP%\cncnew-scenarios
.\scripts\audit-scenarios.ps1 -Scenario tank-duel -Every 10
dotnet run -c Release --project tools/CnCNew.PresentationTool -- scenario-audit .local/phase2-import-a --name capture-building --debug-fire ranger,barracks
```

`scripts/capture-world.ps1` enthält je Szene eine Aufnahme (`scenario-tank-duel`, `scenario-capture`, `scenario-jet`). `Game.Tests` (`ScenarioTests`) prüft mit echten Daten, dass jede Szene
ihre Objekte setzt, ihre Befehle ausführt und zweimal denselben Zustandshash liefert; sie prüft **nicht** die oben genannten Befunde (sonst wäre die Suite rot).

## Was ein grüner Audit bedeuten würde

- `capture-building`: gestufte Fähigkeit nach `SpecialAbilityUpdate.cpp` (Anlaufen, `UNPACKING` UnpackTime, `RAISING_FLAG` PreparationTime, Übernahme, `PACKING` PackTime).
- `tank-duel`: kein Schuss mit mehr als 5° Turmfehler; der Turm hat sich vor dem ersten Schuss gedreht (Turmzustand und Feuerfreigabe in der Simulation, `TurretAI`).
- `jet-strafe`: kein Stillstand in der Luft über 10 Ticks (Mindestgeschwindigkeit und Kreisen des Flügel-Locomotors).
