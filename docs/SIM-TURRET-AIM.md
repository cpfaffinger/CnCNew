# Türme in der Simulation: Zielen vor dem Schuss (`TurretAI`)

Stand 4. Oktober 2026 (Messwerte neu gemessen). Quellen der Referenz: `GeneralsMD/Code/GameEngine/Source/GameLogic/AI/TurretAI.cpp` und
`Include/GameLogic/TurretAI.h` (Zustandsmaschine, `friend_turnTowardsAngle`/`friend_turnTowardsPitch`, `TurretAIData::buildFieldParse`),
`AI/AIStates.cpp` (`AIAttackAimAtTargetState::onEnter/update`, `AIAttackFireWeaponState::update`), `Object/Update/AIUpdate.cpp`
(`getWhichTurretForCurWeapon`, `getTurretRotAndPitch`, `setTurretTargetObject`). Nichts davon ist kopiert; der Ablauf ist verstanden und für die
befehlsgetriebene Kampfsimulation dieses Projekts neu geschrieben.

## 1. Befund

Die Simulation kannte keinen Turmzustand. Ein `FireWeaponCommand` löste den Schuss sofort aus, sobald Waffe, Reichweite und Sicht stimmten; die Darstellung
(`TurretTracker`) drehte den gezeichneten Turm erst *nach* dem Schuss und riet dabei die Richtung. Das Szenario `tank-duel` (`PresentationTool scenario-audit`)
zeigte es: der Crusader schoss in Tick 5 mit einem Turm, der 45° neben dem Ziel stand.

In der Referenz dreht die KI den Turm zuerst (`TurretAIAimTurretState`), und nur wenn er ausgerichtet ist, feuert der Zustand `TURRETAI_FIRE`
(`AIAttackFireWeaponState`) – für Waffen auf einem Turm feuert der Turm-Automat, nicht der Körper (`AIAttackAimAtTargetState::update` gibt für Türme mit Drehrate
immer `STATE_CONTINUE` zurück: „The Body can never return Success if the weapon is on the turret“).

## 2. Daten: `Turret`/`AltTurret` streng typisiert (`CnCNew.Data`)

`Schema/TurretSchema.cs` enthält **jeden** Schlüssel von `TurretAIData::buildFieldParse`; der Compiler (`DefinitionCompiler.CompileModule`) übersetzt die
verschachtelten Blöcke `Turret`/`AltTurret` implementierter Module damit statt mit der heuristischen Generik:

| Schlüssel | Typ | Einheit/Bedeutung |
|---|---|---|
| `TurretTurnRate`, `TurretPitchRate` | Real | Grad je Sekunde (`parseAngularVelocityReal`) |
| `NaturalTurretAngle`, `NaturalTurretPitch`, `FirePitch`, `MinPhysicalPitch`, `GroundUnitPitch`, `MinIdleScanAngle`, `MaxIdleScanAngle` | Angle | Grad |
| `TurretFireAngleSweep`, `TurretSweepSpeedModifier` | Waffenslot + Angle/Real, je Slot wiederholbar | Spreizwinkel / Drehfaktor |
| `ControlledWeaponSlots` | Flags (`PRIMARY`/`SECONDARY`/`TERTIARY`), **Pflicht** | `TurretAI::TurretAI`: „MUST specify controlled weapon slots“ |
| `RecenterTime`, `MinIdleScanInterval`, `MaxIdleScanInterval` | DurationMs | Millisekunden, auf Ticks aufgerundet |
| `AllowsPitch`, `InitiallyDisabled`, `FiresWhileTurning` | Bool | |

Ein unbekannter Schlüssel oder ein fehlendes `ControlledWeaponSlots` ist ein Compilerfehler, kein stiller Standardwert. `InterTurretDelay` gibt es in der Referenz
nur unter `INTER_TURRET_DELAY`, das der ausgelieferte Build nicht definiert; kein Objekt setzt es.

`baselines/compatibility.json` ändert sich in genau einem Eintrag: `compatibility-ignored-tokens` 132 → **208**. Die 76 neuen Fälle sind die Anmerkungen
`// turn rate, in degrees per sec` hinter Werten von `TurretTurnRate`/`TurretPitchRate`; der Referenzparser liest das erste Token und ignoriert den Rest, das wird jetzt
gezählt statt unsichtbar zu sein. Die Zahl der Basislinieneinträge bleibt 751. Waffenfelder wurden nicht neu typisiert: `AcceptableAimDelta` (Weapon/AcceptableAimDelta,
44 Fälle, weiter `deferred-field`) wirkt in der Referenz nur auf das **Körper**-Zielen von Waffen ohne Turm (`AIAttackAimAtTargetState`), nicht auf Türme; der Turm
benutzt die feste Schwelle `REL_THRESH` (siehe 4).

## 3. Simulation

### 3.1 Vorlage und Zustand

* `Templates/TurretInfo.cs`: `TurretInfo` (eine Turmvorlage in Radiant je Tick und Ticks) und `TurretSet` (Haupt- und Zweitturm, `TurretsLinked` des AI-Moduls);
  `ObjectTemplate.Turrets` ist `null` für Objekte ohne Block. Fehlende Schlüssel haben die Konstruktorwerte von `TurretAIData` (0,01 rad je Frame, zwei Sekunden Rückstellzeit).
* `TurretInfo.Gates`: nur ein Turm mit Drehrate > 0 und nicht `InitiallyDisabled` nimmt am Zielen teil (siehe 5).
* `World/TurretState.cs`: je Turm Winkel (relativ zur Fahrzeugrichtung), Neigung, Ziel, `HoldUntil`, Waffenslot des letzten Befehls, `Rotating`, Sweep-Zustand. `Entity.Turrets`,
  öffentlich lesbar als `Entity.TurretOf(index)` → `TurretReading(Angle, Pitch, Rotating)`.

### 3.2 Ablauf (`World/SimulationWorld.Turrets.cs`)

Die Referenz hat einen Turm-Zustandsautomaten (`IDLE`, `IDLESCAN`, `AIM`, `FIRE`, `HOLD`, `RECENTER`), der vom Angriffszustand der KI gesteuert wird. Diese Simulation hat keinen
dauerhaften Angriffszustand – ein Angriff ist ein Strom von `FireWeaponCommand`s (`StandingOrders`, `SkirmishAi`, `MissionSession`). Der Automat ist deshalb auf drei Phasen
zurückgeführt, die aus zwei Feldern folgen (Ziel gültig, `HoldUntil`):

1. **Zielen** (`AIM`+`FIRE`): Ziel gültig und Tick ≤ `HoldUntil`. `StepTurrets` dreht den Turm einmal je Tick (nach allen Bewegungen des Ticks, in Entity-Id-Reihenfolge) um
   höchstens `TurretTurnRate`; liegt der Rest unter einem Schritt, rastet er ein (`TurretAI::friend_turnTowardsAngle`), und `TURRET_ROTATE` ist gesetzt, solange er nicht
   angekommen ist. Die Neigung folgt analog (`friend_turnTowardsPitch`).
2. **Halten** (`HOLD`): ist das Ziel tot oder verschwunden (`friend_getTurretTarget` löscht tote Ziele), bleibt der Winkel bis `HoldUntil`.
3. **Rückstellen** (`RECENTER`): danach dreht der Turm mit halber Rate zum `NaturalTurretAngle`/`NaturalTurretPitch` und kehrt in den Ruhezustand zurück. Ruhende Türme werden nicht
   gesteppt.

`HoldUntil` ist der Zeitpunkt „Bereitschaft der Waffe + `max(RecenterTime, 15 Ticks)` nach dem letzten Befehl“ (Bereitschaft auf 450 Ticks begrenzt): in der Referenz beginnt der Halte-Timer,
wenn der Angriff endet, nicht schon während des Nachladens; ein Befehlsstrom hat zwischen zwei Schüssen Lücken (Nachladen, KI-Takt von 5 Ticks, `MissionSession` alle 3 Ticks), und der Turm
darf in diesen Lücken nicht zur Mitte zurückdrehen. Das ist die eine bewusste Annäherung (siehe 6).

### 3.3 Feuerfreigabe

`FireWeaponCommand` (Ausführung in `SimulationWorld.Step` → `ExecuteCommands`): nach Waffenwahl, Sicht und `IFireGate` ruft `AimTurretsAt(source, slot, victim)` die Türme auf, die den gewählten
Slot steuern (bei `TurretsLinked` alle): jeder bekommt das Ziel (`setTurretTargetObject`, aufgerufen von `AIAttackAimAtTargetState`), und die Waffe feuert nur, wenn **alle** ausgerichtet sind:

* Winkel: Abweichung zur *aktuellen* Zielrichtung ≤ `REL_THRESH` = 0,035 rad (≈ 2°, `TurretAIAimTurretState::update`); bei einer Waffe mit `TurretFireAngleSweep` < Sweepwinkel
  (`turnAlignedToNemesis = fabs(angleDiff) < sweep`).
* Neigung (nur `AllowsPitch` und Rate > 0): Differenz zur Sollneigung ≤ 2°. Sollneigung: `FirePitch`, sonst die Erhebung zum Ziel (von der Oberkante des Schützen, wie `v.z -= maxHeightAbovePosition/2`),
  nie unter `MinPhysicalPitch`; mit `GroundUnitPitch` ein Bogen, der mit der Entfernung zu unbeweglichen oder fahrenden Bodenzielen wächst.
* Sweep: nach einem Schuss ist der Sweep 3 Ticks aktiv (`ENABLE_SWEEP_FRAME_COUNT`); der Turm zielt dann abwechselnd ±Sweepwinkel neben das Ziel mit `TurretSweepSpeedModifier` als Drehfaktor.

Ist ein Turm nicht ausgerichtet, bleibt der Befehl **angenommen** (kein `CommandRejected`-Ereignis, also kein „NoCanDoSound“ und keine HUD-Meldung bei jedem Tick des Drehens), es fällt kein Schuss, und
`SimulationWorld.TurretWaits` zählt es. Nach dem Schuss verlängert `NoteTurretShot` den Halte-Zeitpunkt und schaltet den Sweep ein.

Gleiches gilt für das modulinterne `TryFireActiveWeapon` (ungenutzt). `TryFireWeaponSlot` (Module, die ihre Angriffsfolge selbst fahren und bei einem Fehlschlag aufgeben, z. B.
`AssistedTargetingUpdate`) richtet die Türme aus, wartet aber nicht.

### 3.4 Snapshot, Hash, Determinismus

* `Entity.Describe` schreibt `turrets` nur, solange ein Turm vom Ruhezustand abweicht; ebenso `turretWaits` im Weltteil erst nach dem ersten Warten. Welten ohne bewegte Türme behalten
  Serialisierung und Hash: der 216.000-Tick-Stresstest (Fixture ohne Türme) ist unverändert.
* `WorldSnapshot.Version` 8 → **9**. Das Replay-Format (9) und `CommandTypes.Version` (12) bleiben: die Befehle haben sich nicht geändert (alte Replays würden wegen der geänderten Kampfzeiten ohnehin
  an den Prüfsummen scheitern).
* Alle Größen sind `Fixed`/ganzzahlig; Winkel über `FixedTrig` (CORDIC-`Atan2`, Fehler < 1e-3 rad, weit unter der 2°-Schwelle). Kein `float`, keine Wandzeit.

## 4. Darstellung

`TurretTracker` (jetzt `Presentation/Objects/TurretTracker.cs`) liest `Entity.TurretOf` und merkt sich nur, ob ein Turm sich drehte (`MODELCONDITION_TURRET_ROTATE` über `IsRotating`). `TurretMotion` (die
Rohtext-Auswertung der Turmblöcke), `TurretTracker.Observe` und `ObjectVisual.Turrets` sind entfernt; die Stride-Schicht (`ObjectLayerCore.ApplyTurret`) ist unverändert, `DrawableInstance.TurretYaw/TurretPitch/
AltTurret*` kommen aus dem Simulationszustand.

## 5. Ausnahmen: was feuert wie vorher

| Fall | Verhalten | Begründung |
|---|---|---|
| Objekt ohne `Turret`-Block (Infanterie, Gebäude ohne Turm, Flugzeuge) | feuert sofort | kein `m_turretAI` |
| Turm mit `TurretTurnRate = 0` (18 Blöcke der Retaildaten; der „Scheinturm“ der Referenz, z. B. des Comanche) | feuert sofort, Turm bleibt mittig | `AIAttackAimAtTargetState::update`: „incapable of turning, turn ourself“ → Körper zielt |
| `InitiallyDisabled = Yes` (4 Blöcke, die Strategiezentren; zusammen mit den Scheintürmen 22 Objekte mit nicht zielendem Turm) | wie vorher | die Referenz schaltet sie über `BattlePlanUpdate`/`DeployStyleAIUpdate` ein (`setTurretEnabled`); `BattlePlanModule` schaltet hier keinen Turm |
| Slot, den kein Turm steuert (`ControlledWeaponSlots`) | feuert sofort | `getWhichTurretForCurWeapon` = `TURRET_INVALID` |
| `FireOrigin.Script` (Einzelbefehl einer Skript-Schaltfläche) | feuert sofort, der Turm dreht trotzdem | der Befehl wird nicht wiederholt |
| Modulgesteuerte Schüsse (`TryFireWeaponSlot`, `FireCurrentWeaponAtPosition`, Flugzeuge, Projektile) | wie vorher | eigener Ablauf |
| Ziel als Position (Bodenangriff) | kein Zielen | `FireWeaponCommand` kennt nur Objektziele |

`FiresWhileTurning` hat in `TurretAI.cpp` keine Wirkung auf die Feuerfreigabe (es steuert nur das Ende des Drehgeräuschs, `m_continuousFireExpirationFrame`); es ist typisiert und gelesen, aber ohne Folge.

## 6. Bekannte Grenzen (ehrlich)

* **Kein Angriffszustand:** Halten/Rückstellen beruhen auf der „letzte Bereitschaft + Rückstellzeit“-Näherung. Ein Turm, dessen Befehlsstrom länger als diese Zeit abreißt, während das Ziel noch lebt, dreht
  zurück; die Referenz würde im Angriffszustand nachführen.
* **Kein Leerlaufabtasten, keine Selbstzielsuche des Turms:** `MinIdleScanAngle…`, `TurretAI::friend_checkForIdleMoodTarget` (ein Turm, der von sich aus ein Ziel wählt) und das zufällige Umsehen sind nicht
  nachgebildet; ruhende Türme bleiben in Ruhe.
* **Körper zielt nicht:** Waffen ohne Turm feuern weiter ohne Drehung des Körpers (`AcceptableAimDelta` bleibt `deferred-field`); fahrende Einheiten drehen den Körper nur durch ihre Bewegung.
* `DeployStyleAIUpdate`: `ALIGNING_TURRETS` (Turm vor dem Einpacken mittig) wird nicht betreten; `TurretsFunctionOnlyWhenDeployed` ist weiter nur durch das Feuer-Gate (Waffen) umgesetzt.
* Bei `TurretsLinked` (3 der 4 Schlachtschiffe) feuert jede Waffe erst, wenn beide Türme zeigen; die Referenz lässt den jeweils ausgerichteten Turm feuern.
* Das Feuern geschieht im Tick nach dem Ausrichten (der Befehl prüft den Zustand am Ende des Vortags gegen die aktuelle Zielrichtung); die Referenz feuert einen Update später im Turm-Automaten.
* Sicht- und Darstellungsänderungen brauchen weiter eine Sichtprüfung durch einen Menschen (Rauchtests beweisen Reproduzierbarkeit, nicht Richtigkeit).

## 7. Tests und Audits

* `Simulation.Tests` (`Tests.Turrets.cs`): Vorlagenmodell, Standardwerte und strenge Fehler; Drehung mit 6°/Tick und Wartezeit bis Tick +15; Körperrichtung; Ausnahmen aus Abschnitt 5; Halten, Rückstellen,
  Ruhezustand (kein `turrets` im Snapshot) und totes Ziel; zwei Türme mit Firing-Pitch, Sweep, `TurretsLinked`; Snapshot mitten in der Drehung und Determinismus; echte Daten: Crusader (180°/s), Battlemaster,
  keine Türme beim Ranger, vier Schlachtschiffe mit Zweitturm (3 verbunden), Sweepwaffen, Duell Crusader gegen Battlemaster (Crusader schießt nach 8 Ticks mit 0,00° Fehler, Battlemaster sofort).
* `Presentation.Tests`: Turm folgt Befehl/Schuss/Rückstellung aus der Simulation (`TurretFollowsOrders`), `TURRET_ROTATE` durch den Projektor (`TurretRotate`), echte Daten: alle Turmblöcke typisiert.
* `Simulation.Tests` (angepasst): Schlachtfeld-Szenarien der Gattling-Panzer-Waffenwahl und des Scud-Werfers wiederholen den Befehl wie ein Angriff; Versionsprüfungen auf Snapshot 9.
* `PresentationTool scenario-audit … --name tank-duel`: alle Schüsse mit 0,0° Winkelfehler (Grenze 5°); erster Schuss des Crusader in **Tick 13** (Befehl in Tick 5, 45° bei 6° je Tick: nach 7 Schritten
  noch 3° > 2°, im 8. Schritt eingerastet, Schuss im 9. Tick), der Battlemaster steht bereits richtig und schießt in Tick 5. `RESULT: all checks passed`.

## 8. Messwerte (Release, Retaildaten, 4. Oktober 2026)

| Lauf | vorher | nachher |
|---|---|---|
| `SimTool reference-match` | Sieger FactionGLA, Tick 13.890, Endhash `76F22CB0CA79BEEF`, Kette `35F54CAD40A43E6E` | Sieger FactionGLA, Tick **13.875**, Endhash `482FC8C52D507662`, Kette `34AB50A397B0125C`; Run 2 und Snapshot-Fortsetzung identisch |
| `SimTool stress` (216.000 Ticks, synthetisch, ohne Türme) | Endhash `56882F763EABEF67`, Kette `B8E663493F2C73D5` | **unverändert** |
| `ai-match` China/GLA (Alpine Assault) | China, Tick 43.275, Endhash `757327AE69B87C2A`, Kette `EBE789B0176D56CB` | China, Tick 43.965, Endhash `AE34F9421AA76C9A`, Kette `DF948386BA2C5C98` |
| `ai-match` AmericaAirForce/China | Patt (45.000), Endhash `AF78F914D08B09DD`, Kette `68A8ABB3A7BCFF0C` | Patt, Endhash `D31EB9C8679D6632`, Kette `2536E3A7B8CB6620` |
| `ai-match` America/GLA | America, Tick 16.110, Endhash `ED637C7C3E4FFF9E`, Kette `2B93BACCDB0F6450` | **unverändert** (kein Turmobjekt kämpft) |
| `CampaignTool audit --ticks 900 --campaign CHALLENGE_0` | – (nicht erhoben) | 7/7 deterministisch, 7/7 identisch nach Speichern/Laden; Ausschalt-Enden Sieg 7/7, Niederlage 6/7 |

Alle `ai-match`-Läufe: zwei unabhängige Läufe und die Fortsetzung ab einem Snapshot in der Mitte sind tickgenau identisch. Die Hashes ändern sich mit der Kampfzeit (Türme drehen, bevor sie schießen); sie ändern
sich dort nicht, wo kein Objekt mit Turm kämpft. Es wurde nichts aus anderem Anlass verändert.

## 9. Nachziehen in anderen Dokumenten (nicht Teil dieser Änderung)

`docs/SIMULATION-CORE.md` (Waffenwahl-Näherungen „Turm- und Neigungsbedingungen fehlen“, Abschnitt zur Waffenwahl), `docs/PRESENTATION-OBJECTS.md` (Zeile 47 `TURRET_ROTATE`, Abschnitt zu `TurretYaw`: „null, weil die Simulation
keinen Turmwinkel führt“), `docs/MODEL-CONDITIONS-UNITS.md` (Zeilen zu `TurretTracker`), `docs/DEMO-SCENARIOS.md` (Zeile `tank-duel`), `docs/CLIENT-OBJECTS.md`, `docs/DEMO-FINDINGS.md`, `README.md`/`ROADMAP.md`/`IMPLEMENTATION-PLAN.md`
(Snapshotversion 9, Turmzustand) und `CLAUDE.md` (Referenzwerte) beschreiben noch den Stand ohne Turmzustand.
