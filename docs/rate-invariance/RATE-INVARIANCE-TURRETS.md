# Raten-Invarianz: Türme, Zielerfassung und Feuerrhythmus (Phase 23, Paket TURRETS)

Stand: 8. Oktober 2026, Branch `worktree-agent-a18c18f52abecd6cb`, Ausgang `7b54374`. Dieses Dokument ist das Protokoll eines der sechs parallelen Korrekturpakete der Phase 23
(`docs/handoffs/HANDOFF-PHASE23.md`, Schritt 3). Es beschreibt, was dieses Paket an der Simulation geändert hat, damit Türme, Feuerfreigabe, Feuerstufen, die gestufte Übernahme und die
Zufallszüge seiner Module bei 30, 60 und 120 Hz **dasselbe Spiel** sind – gemessen von der Raten-Äquivalenz-Suite (`docs/SIMULATION-CORE.md` §39) und von Drei-Raten-Tests auf der
synthetischen Fixture (`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Turrets.cs`). Maßstab ist 30 Hz; „Bild“ heißt Logikbild des Originals (1/30 s), „Tick“ ein Schritt der
Simulation. Die gemeinsamen Dokumente (`TICKRATE-MIGRATION.md`, `SIMULATION-CORE.md`, `SIM-TURRET-AIM.md`, `SIM-FIRING-TRACKER.md`) sind von diesem Paket **nicht** angefasst; sie
beschreiben an den hier genannten Stellen noch den Stand davor.

**Nichts davon hat ein Mensch gesehen.** Die Aussagen sind Messungen der Suite und der Tests, keine Sichtabnahme.

## 1. Ergebnis in Zahlen

Größen außerhalb der Toleranz (ohne die bei jeder Rate ungemessenen), Release, Realdaten `.local/content`, `SimTool rate-compare --group …`:

| Gruppe | Größen | vorher 60 Hz / 120 Hz (gesamt) | nachher 60 Hz / 120 Hz (gesamt) | was übrig ist |
|---|---|---|---|---|
| `turrets` | 216 | 28 / 36 (40) | **5 / 1 (5)** | Sentry-Drohne (4 Größen bei 60 Hz) und ECM-Panzer (1 Größe) – beide nicht vom Turm verursacht, §5 |
| `weapons` | 1.474 | 43 / 57 (57) | **2 / 11 (11)** | 9-mal „erster Schuss bis erster Schaden“ bei 120 Hz (Geschossflug, anderes Paket), 2-mal Schaden der Feuerwand des Drachenpanzers bei gleicher Schusszahl, §5 |
| `timed` | 383 | 53 / 56 (56) | **44 / 47 (47)** | die drei Übernahme-Fälle bestehen vollständig; alles Übrige gehört anderen Paketen (Aufladen, Lebensdauern, langsamer Tod, Umstürzen, Minen, Tarnung, Zug) |

Simulationstests mit `--tick-rate` (ohne Realdaten): bei 60 Hz **6 → 4** Fehlschläge (übrig die vier handgerechneten Geschossbahnen), bei 120 Hz **14 → 9** (übrig: dieselben vier,
dazu Nr. 1, 2, 3, 9 und 10 der Tabelle in `docs/TICKRATE-MIGRATION.md` §15.2 – keiner aus diesem Paket). Die Nummern 4 (Blitze der Übernahme), 11 (Turmdrehrate, reine Testerwartung)
und 12–14 (Turmschwenk) bestehen bei allen drei Raten. Bei 30 Hz sind alle Simulationstests grün.

30 Hz ist unverändert: nach **jedem** Commit `input-manifest check` identisch und `reference-manifest check --set quick` 9 Läufe identisch; am Ende `--set full` (§7).

## 2. Das Prinzip am Turm: Entscheidung je Bild, Weg je Tick

Der Turm des Originals macht je Bild einen Schritt (`TurretAI::friend_turnTowardsAngle`): näher am Ziel als ein Schritt – einrasten; sonst ein Schritt auf dem kurzen Weg. Die
Feuerfreigabe prüft „innerhalb von 0,035 rad“ (`REL_THRESH`). Ein Euler-Schritt je Tick mit halber Schrittweite erreicht die Freigabeschwelle in einem anderen Bild und mit einem
anderen Winkel (Fire Base nach 90°: 1,57080 / 1,55334 / 1,54462 rad), und ein schwenkender Turm (`TurretFireAngleSweep`) steht im Moment eines fälligen Schusses an einer anderen
Stelle seines Schwenks (Feuerwand des Drachenpanzers: 838 / 899 / 899 Schüsse in 60 s).

Jetzt gilt (`SimulationWorld.Turrets.cs`, `TurretState.cs`):

- **Der Schritt wird einmal je Bild entschieden, genau wie bei 30 Hz** – wie weit, in welche Richtung, ob eingerastet wird, ob der Schwenk umkehrt –, mit der Drehrate **je Bild, wie
  der 30-Hz-Katalog sie rundet**. Der Turm geht ihn in gleichen Teilen, einen je Tick (`TurretState.SubSteps`, `StartAngle`/`GoalAngle`, `StartPitch`/`GoalPitch`). Am Ende jedes
  Bildes ist der Winkel **bitgleich** der 30-Hz-Winkel; dazwischen liegt er auf dem Weg dorthin.
- **Das Bild ist das des Turms**, nicht das der Welt: es beginnt mit dem Tick, in dem der Turm zum ersten Mal (oder nach einem neuen Befehl) entscheidet. Ein Befehl, der mitten in
  einem Bild der Welt kommt, wird nicht bis zur nächsten Bildgrenze aufgehalten; ein **neues** Opfer bricht den laufenden Schritt ab (`AbandonFrameStep`), der Turm dreht ab dem
  nächsten Tick von dort, wo er steht.
- **Freigabe innerhalb eines Bildes** (`IsAligned`): der Turm gilt als ausgerichtet, wenn **Anfang und Ziel** des laufenden Schritts es sind. Ein Turm, der seinem Opfer folgt,
  feuert damit in jedem Tick; ein Turm, der noch hinschwenkt, wird freigegeben, wenn der Schritt fertig ist – im selben Bild und mit demselben Winkel wie bei 30 Hz.
- **Drehrate je Bild:** `Rate je Tick × Ticks je Bild` weicht um die Rundung des Tickwerts ab (ein, zwei Rohwerte). Das genügt, um einen Turm, der genau einen Schritt vor seinem
  Ziel steht (90° bei 6° je Bild – jede achsparallele Szene der Suite), in ein anderes Bild kippen zu lassen. Die Raten je Bild eines Katalogs über 30 Hz stehen deshalb in einer
  statischen Tabelle **neben** `TurretInfo` (`TurretInfo.RatesPerOriginalFrame`, gefüllt in `TurretSet.Read` mit dem Zeitdienst der Originalrate) – kein Feld des Katalogtyps, das
  Eingangsmanifest ist unberührt; ein von Hand gebauter `TurretInfo` fällt auf `Rate je Tick × Ticks je Bild` zurück.
- **Fristen „bis einschließlich“** (Halten bis `HoldUntil`, §5.3): der eine Schritt mehr ist ein Bild (`tick < HoldUntil + Ticks je Bild`). Der gespeicherte Wert bleibt.
- **Erster Schritt und Leerlauf-Scan:** ein neuer Turm wird einen Tick nach dem Erscheinen seines Objekts zum ersten Mal gestuft, setzt in diesem Schritt die Scan-Frist und sieht im
  nächsten Schritt nach. Über 30 Hz wartet er jetzt, bis das erste Bild seines Lebens beginnt (Alter in Ticks kleiner als ein Bild: nichts geschieht), und der Schritt, der die Frist
  setzt, belegt sein Bild (`OccupyFrame`: ein Schritt ohne Bewegung). Damit ist der Leerlauf-Scan über 1.500 Bilder an jedem Bildende bitgleich zu 30 Hz (Drei-Raten-Test).
- **`TurretWaits`** (Teil des Hashes) zählt jetzt Bilder des Wartens: ein zurückgehaltener Befehl zählt nur in einem Tick, mit dem ein Bild der Welt beginnt (15 / 15 / 15 statt
  15 / 30 / 59 für eine halbe Sekunde Warten).

Bei 30 Hz ist ein Bild ein Tick: `SubSteps` wird nie gesetzt und nie geschrieben, jede Vergleichsform ist die frühere, die Raten je Bild sind die Katalogwerte.

**Neuer Zustand, nur über 30 Hz geschrieben:** je Turm `subSteps`, `goalAngle`, `goalPitch`, `startAngle`, `startPitch` (nur während eines laufenden Schritts);
`abilityFacePhase` der `SpecialAbilityUpdate` (nur während des Zudrehens). Bei 30 Hz bleiben Snapshot und Hash bytegleich; bei 60/120 Hz ändert sich der Snapshot-Inhalt (dort gibt
es noch keine Referenzwerte).

## 3. Ursachen, eine je Commit

Suite-Zahlen sind „Größen außerhalb der Toleranz bei 60 Hz / bei 120 Hz“ der genannten Gruppe unmittelbar vor und nach dem Commit; Einzelwerte sind 30 / 60 / 120 Hz.

| § 5 (`TICKRATE-MIGRATION.md`) | Befund (30 / 60 / 120 Hz vorher) | Korrektur (Datei, Symbol, Prinzip) | nachher | Commit |
|---|---|---|---|---|
| – (Messung der Suite) | Turmwinkel beim ersten Schuss wurde nach dem Tick des Schusses gelesen: ein Tickanteil des Turmschritts später (bei 30 Hz der ganze Schritt, darüber ein Teil) – gemessen wurde die Ablesung, nicht der Turm. Fire Base 90°: 1,57080 / 1,55334 / 1,54462 rad | `Testing/RateEquivalence/RateCases.Turrets.cs`, `MeasureTurret`: Ablesen am Ende des Bildes, in das der Schuss fiel (`RateScene.Align`); bei 30 Hz kein Schritt mehr, jeder Wert wie zuvor. Keine Toleranz berührt | 1,57080 bei jeder Rate; `turrets` 28 / 36 → 17 / 28 | `f791682` |
| – (Messung der Suite) | „return: leaving the aim to rest“ verkettete zwei `StepUntil`; der zweite beginnt bei einer Beobachtung, nicht bei einem Befehl, ließ aber den Befehlstick aus: ein Tick (4 / 2 / 1 Einheiten) fehlte | dieselbe Datei: `rested + 1`. Die 30-Hz-Werte wachsen um ein Bild (2,9333 → 2,9667 s) | Tomahawk 2,9667 / 2,9833 / 2,9917 s; `turrets` 17 / 28 → 15 / 15 | `2f62a6b` |
| §5.2 `IsAligned`, `AimStep`, `RecenterStep`, `ScanStep`, `TurnAngle`, `TurnPitch` | Euler-Schritt je Tick, Einrasten und Freigabe gegen einen Tickschritt. Drachenpanzer 90°: 1,43117 / 1,44862 / 1,43117 rad; Dauerfeuer in 60 s: Gatling-Panzer 440 / 441 / 441, Toxin-Laster 899 / 900 / 900, Feuerwand 838 / 899 / 899 | `World/SimulationWorld.Turrets.cs` (`StepTurret`, `BeginFrameStep`, `ContinueFrameStep`, `AbandonFrameStep`, `IsAligned`/`IsAlignedAt`), `World/TurretState.cs` (`SubSteps`, `Start…`, `Goal…`, `Frame…Rate`), `Templates/TurretInfo.cs` (`RatesPerOriginalFrame`): Entscheidung je Bild, Weg je Tick (§2) | Winkel gleich; 440 / 899 / 838 bei jeder Rate; `weapons` 43 / 57 → 13 / 22; `turrets` 15 / 15 → 16 / 19 (der Zusatztick des Haltens wird sichtbar) | `32eb25c` |
| §5.3 `StepTurrets`/`StepTurret` (Halten bis einschließlich `HoldUntil`) | Zähler N + 1, der Zusatzschritt war ein Tick: 16 / 15,5 / 15,25 Bilder Halten bei `RecenterTime` 15 Bilder; Fire Base, dritter Blick 14,7333 / 14,6833 / 14,6583 s | `SimulationWorld.Turrets.cs`, `StepTurret`: `tick < HoldUntil + Ticks je Bild` | 16 Bilder bei jeder Rate; dritter Blick 14,7333 / 14,7167 / 14,7083 s; `turrets` 16 / 19 → 9 / 11 | `6e0c691` |
| §5.2 `IdleScanStep`, `ScheduleScan`; §5.3 „ein Schritt später“ | Erster Schritt des Turms und der Schritt, der die Scan-Frist setzt, kosteten je einen Tick. Erster Blick Fire Base 0,6333 / 0,6167 / 0,6083 s, ECM-Panzer (Intervall 0) 0,0333 / 0 / 0 s | `SimulationWorld.Turrets.cs`, `IdleScanStep` (Alter gegen ein Bild), `OccupyFrame` | 0,6333 und 0,0333 s bei jeder Rate, dritter Blick 14,7333 s; alle Leerlauf-Größen bestehen; `turrets` 9 / 11 → 5 / 1 | `276a8de` |
| §5.4 `_turretWaits` (auch `AutoFire`, `TryFireActiveWeapon`) | Zählt zurückgehaltene Befehle je Tick: 15 / 30 / 59 | `SimulationWorld.Turrets.cs`, `NoteTurretWait`: zählt in Ticks, mit denen ein Bild der Welt beginnt (drei Aufrufstellen: `SimulationWorld.cs` `FireWeaponCommand`, `SimulationWorld.Acquisition.cs` `AutoFire`, `SimulationWorld.WeaponSlots.cs` `TryFireActiveWeapon`) | 15 / 15 / 15 (Test); keine Suite-Größe liest den Zähler | `dc888d3` |
| §5.3 `StepFiringTrackers` (`tick <= CooldownFrame`), `PreAttackDone` (`_tick <= PreFinish + …`) | Handeln im Schritt nach der Frist, der Schritt war ein Tick: Stufe fällt 35 / 34,5 / 34,25 Bilder nach dem letzten Schuss, langsame Stufe endet nach 66 / 65 / 64,5 (gerechnet) | `World/SimulationWorld.FiringTracker.cs`: beide Vergleiche `< Frist + Ticks je Bild`. **`PreAttackSlotOf` bewusst unverändert** (§4) | 35 und 66 Bilder bei jeder Rate (Test); `weapons` unverändert 13 / 22 (keine Suite-Größe misst das Abklingen) | `165e6d8` |
| neu (Feuerpfad, `ConsumeShot`) | Magazin ohne Nachladezeit (`ClipReloadTime = 0`, die Schlachtschiff-Geschütze) war im Tick des letzten Schusses wieder bereit: der nächste Schuss einen **Tick** später. Nachladen 0,0333 / 0,0167 / 0,0083 s, Schüsse in 60 s 110 / 112 / 112 | `World/SimulationWorld.WeaponSlots.cs`, `ClipReloadOf`: über 30 Hz ein Bild Wartezeit; bei 30 Hz bleibt die Wartezeit 0 (der gespeicherte Bereit-Tick ist Zustand) | 0,0333 s und 110 Schüsse bei jeder Rate; `weapons` 13 / 22 → 2 / 11 | `20d4766` |
| §5.2 `SpecialAbilityUpdate.StepCaptureFlash` | Blitzphase je Tick statt je Bild: 4 / 9 / 19 Blitze in 2 s (Test), 49 / 99 / 199 (Suite), erster bis letzter Blitz 16,9667 / 17,9000 / 18,5333 s | `Modules/BuiltIn/SpecialAbilityStaging.cs`, `StepCaptureFlash`: Schritt nur in den Ticks, die ein Bild der Vorbereitung vollenden (`_prepFrames % Ticks je Bild == 0`) | 4 / 4 / 4 und 49 / 49 / 49, 16,9667 s; alle drei Übernahme-Fälle bestehen; `timed` 53 / 56 → 44 / 47 | `d923cea` |
| – (Testerwartung, Folge von `165e6d8`) | „Firing tracker: … The slow stage lasts a second“ erwartete `Frames(30) + 1` Ticks | `Tests.FiringTracker.cs`: `Frames(30 + 1)` | besteht bei 30 / 60 / 120 Hz | `f5653f9` |
| – (Testerwartung, §15.2 Nr. 11) | `PiFraction(1, 30)`: Konstante je Tick bei 30 Hz | `Tests.Turrets.cs`: `PiFraction(1, TicksPerSecond)` | besteht bei 30 / 60 / 120 Hz | `b405704` |
| §5.2 `SpecialAbilityUpdate.TurnToward` | Einrasten gegen einen Tick Drehung: Zudrehen der Black Lotus 8 Bilder / 15 Ticks (7,5 Bilder) / – | `SpecialAbilityStaging.cs`, `TurnToward`, `_facePhase`: Entscheidung im ersten Tick jedes Bildes gegen ein Bild Drehung, dazwischen Drehung je Tick | 8 Bilder bei jeder Rate (Test, schlägt auf dem alten Stand fehl); `timed` unverändert | `95bcf3d` |
| §5.5 `MoodCheckOffset` | Der Zufallsversatz hashte die Ticknummer: bei jeder Rate eine andere Folge | `World/SimulationWorld.Acquisition.cs`: Hash über die Bildnummer (`SimulationTime.FramesIn`); die benannte Ausnahme der Quelltextregel für `tick + 1` entfällt | erster Schuss nach dem zweiten Blick im selben Bild bei jeder Rate, über acht Startwerte (Test) | `146f92d` |
| §5.5 `SlavedUpdate.DoRepairLogic` | Höhen-Zufallszug und Bewegungsbefehl je Tick im Reparaturzustand: 100 / 200 / – Züge in 100 Bildern | `Modules/BuiltIn/SlaveModules.cs`: Zug und Befehl im Reparaturzustand nur im Tick, mit dem ein Bild der Welt beginnt (Ausweg, §4) | 100 / 100 / 100 (Test) | `01121cc`, berichtigt in `e1252df` |
| §5.5 `WanderAIUpdate.Update` | Zwei Züge und ein Befehl je Leerlauf-Tick: 200 / 400 / – Züge in 100 Bildern | `Modules/BuiltIn/SpecialsModules.cs`: der Blick nur im Tick, mit dem ein Bild der Welt beginnt (Ausweg, §4) | 200 / 200 / 200 (Test) | `022bec8` |
| §5.5 `GrantStealthBehavior.Update` | Wachsen und Scan je Tick, erster Scan einen Tick nach dem Erscheinen: Tarnung bei 25 / 45 Einheiten nach 2 / 4 Bildern bei 30 Hz, nach 1,5 / 3,5 Bildern bei 60 Hz | `SpecialsModules.cs`: läuft über 30 Hz auf dem Bildraster des Markers (Ticks, die ein ganzes Vielfaches eines Bildes nach `SpawnTick` liegen), mit dem Zuwachs eines Bildes (Ausweg, §4) | dieselben Bilder bei jeder Rate (Test) | `f1c5ceb` |
| §5.4 `StepAcquisition` (Gnadenfrist des befohlenen Angriffs) | Zähler N + 1: der Angriff „auf Befehl“ endet 46 / 45,5 / 45,25 Bilder nach der letzten Feuerbereitschaft (gerechnet) | `SimulationWorld.Acquisition.cs`, `StepAcquirer` und `EarliestReadyTick`: `< Frist + Ticks je Bild` | 55 Bilder nach einem einzelnen Befehl bei jeder Rate (Test) | `2d637e1` |
| §5.5 (Berichtigung) | `01121cc` hielt auch den Zug **außerhalb** eines Reparaturzustands am Weltraster fest; dort läuft die Logik nur alle 7 Bilder im eigenen Takt – fiel der nie auf eine Bildgrenze, flog die Drohne nie los („Slaves: a battle drone repairs its hurt master“ bei 120 Hz) | `SlaveModules.cs`: das Raster gilt nur im Reparaturzustand (dort läuft die Logik je Tick) | Test bei 120 Hz grün; 14 oder 15 Züge in 100 Bildern außerhalb des Zustands bei jeder Rate | `e1252df` |

## 4. Der Ausweg „Originalraster“ – jede Verwendung

Der Ausweg (Logik nur auf jedem (Rate/30)-ten Tick) ist für Bewegung, Befehlsannahme und Zielerfassung nicht zulässig und dort nicht benutzt: Türme drehen in jedem Tick, Feuerbefehle
werden in jedem Tick angenommen und geprüft, `StepAcquisition` und `AutoFire` laufen in jedem Tick. Benutzt ist er hier:

| Stelle | Was auf dem Raster läuft | Raster | Begründung |
|---|---|---|---|
| `SlavedUpdate.DoRepairLogic` | der Höhen-Zufallszug und der Bewegungsbefehl einer Drohne im Reparaturzustand, die noch nicht nah genug ist | Welt (`IsOriginalFrameBoundary(world.Tick)`) | ein Zufallszug ist nicht teilbar; die Zahl der Züge je Spielzeit muss gleich sein (Regel 5). Die Bewegung, die Reparaturzustände und die Heilung laufen je Tick |
| `WanderAIUpdate.Update` | der Blick eines untätigen Wanderers (zwei Zufallszüge, ein Bewegungsbefehl) | Welt | wie oben. Die Bewegung läuft je Tick; ein Wanderer, der mitten im Bild ankommt, bricht bis zu ½ (¾) Bild später auf als bei seiner Ankunft – im nächsten Bild, wie bei 30 Hz |
| `GrantStealthBehavior.Update` | Wachsen des Radius und Scan (Vergabe der Tarnung) | das des Markers (`SpawnTick`) | Logik des Originals einmal je Bild ohne Zwischenzustand: eine Vergabe ist ein Ereignis, der Radius nur die Reichweite des nächsten Scans |
| `SpecialAbilityUpdate.StepCaptureFlash` | die Blitzphase der Übernahme | das der Vorbereitung (ihr Tickzähler) | Rekursion je Bild (der Zuwachs wächst von Bild zu Bild), ein Blitz ist ein Ereignis. Die Vorbereitung selbst zählt Ticks |
| `SimulationWorld.NoteTurretWait` | das Hochzählen von `TurretWaits` | Welt | reine Statistik (Teil des Hashes); kein Verhalten hängt daran |

Keine Verwendung des Auswegs, sondern Unterteilung nach dem Prinzip (Entscheidung je Bild, Fortschritt je Tick): Turmschritt (§2), `SpecialAbilityUpdate.TurnToward`.

## 5. Was offen bleibt, und warum

**In den Gruppen dieses Pakets, aber nicht aus diesem Paket:**

- **`turrets/MISSION_AmericaVehicleSentryDrone`** (4 Größen bei 60 Hz: Winkel beim ersten Schuss 1,57080 / 1,55771 / 1,57080 rad, Rückkehr): die Drohne muss sich aufstellen
  (`DeployStyleAIUpdate`), bevor sie feuern darf. Ihr Statusbit erscheint bei 60 Hz einen Tick später als das Doppelte des 30-Hz-Ticks (Tick 65 statt 64, mit einem Ablauf-Protokoll
  des Falls gemessen), der Turm bekommt seinen Auftrag deshalb auf einem ungeraden Tick, kommt aus einer anderen Stellung und erreicht die 2°-Schwelle ein halbes Bild früher. Der
  Turm selbst rechnet richtig; die Ursache ist die Aufstellzeit (nicht in den Dateien dieses Pakets). Zusätzlich liest die Suite den Winkel an einer Bildgrenze der **Welt**, der Turm
  ist dann mitten in **seinem** Bild.
- **`turrets/ChinaTankECM`, Winkel nach 180°** (−2,09440 / −1,98968 / −1,98968 rad): der „erste Schuss“, auf den der Fall wartet, ist kein Schuss des Turms. Der Feuerbefehl wartet
  bei jeder Rate noch (der Zähler der zurückgehaltenen Befehle läuft bis zum Schuss weiter); es feuert ein Modul des Panzers, 18 Bilder **und einen Tick** nach dem Befehl
  (Schritt 19 / 37 / 73). Der Fall misst den Turmwinkel zu diesem Zeitpunkt. Ursache ist der Ein-Tick-Versatz dieses Moduls (§5.3 der Liste), nicht der Turm.
- **`weapons`, 9-mal „first shot to first damage“ bei 120 Hz**: Flugzeit der Geschosse (Paket Geschosse).
- **`weapons`, Schaden der Feuerwand des Drachenpanzers** (14.605,5 / 13.009,5 / 13.005,5 in 60 s; mit Upgrade 18.167,6 / 16.313,9 / 16.318,9): die **Schusszahl ist jetzt gleich**
  (838), der Schaden nicht. Die gesuchte Ursache der Schusszahl (838 / 899 / 899) war der Turm: die Waffe schwenkt ±60° und wartet an den Umkehrpunkten. Der Schaden entsteht aus
  den Flammengeschossen und den Feuerwand-Segmenten, die sie hinterlassen; die Segmente wandern (`WanderAIUpdate`), aber deren Zufallszüge sind es nicht (der Wert ändert sich mit
  der Korrektur kaum). Nicht weiter untersucht; gehört zu Geschossen bzw. Schaden über Zeit.
- **`timed`**: 47 Größen, keine aus diesem Paket.

**Bewusst gelassen (§5 der Liste):**

- **§5.3 `PreAttackSlotOf`** (`_tick <= PreFinish`): eine Abfrage zwischen zwei Schritten, keine Handlung in einem Schritt. Ihre Antwort wechselt nach dem Schritt des Fristticks,
  das ist bei jeder Rate dieselbe Spielzeit; mit einem Bild Zugabe zeigte sie das Aufziehen bei 60 Hz ein halbes Bild länger.
- **§5.4 `KeepsAttackIntent`**: wird nur in den Entscheidungsticks des Turms ausgewertet, und die Frist zählt 45 Bilder ab dem Tick, in dem das Opfer außer Reichweite gesehen
  wurde – der Angriff wird im selben Bild aufgegeben wie bei 30 Hz (der Tick innerhalb des Bildes kann abweichen). Der bestehende Drei-Raten-Test (45 Bilder) bleibt unverändert.
- **§5.4 `StepAcquisition`/`AutoFire` je Tick**: bleibt je Tick (Vorgabe). Verbleibende Unterschiede unter einem Bild, ehrlich benannt: (a) eine Einheit, die mitten in einem Bild
  untätig wird oder deren Opfer mitten in einem Bild in Reichweite kommt, sieht und feuert bis zu ½ (¾) Bild früher als bei 30 Hz; (b) der erste Blick einer neuen Einheit fällt
  zwei Bilder nach ihrem ersten Schritt, und der ist einen **Tick** nach dem Erscheinen (§5.3 `Register`, Paket TIMING): eine auf einer Bildgrenze erzeugte Einheit blickt bei 30 Hz
  in Bild 3, bei 60 und 120 Hz in Bild 2 – und zieht damit auch einen anderen Zufallsversatz; (c) ein Turm, der einem Opfer folgt, gibt mitten im Bild nur frei, wenn Anfang und
  Ziel seines Schritts ausgerichtet sind.
- **§5.5 `StickyBombUpdate`, `ProjectileStreamUpdate`**: kein Zufallszug, kein Befehl; die Bombe folgt ihrem Opfer je Tick (das ist Bewegung und richtig), der Strom führt nur Buch.
  Unterschied unter einem Bild: eine Bombe an einem toten Opfer und ein leerer Strom verschwinden bis zu ¾ Bild früher.
- **§5.5 `DoAttackLogic`/`DoScoutLogic`/`DoGuardLogic`, `MobMemberSlavedUpdate`**: liegen schon im Prüfraster (Phase 20), unverändert.

**Rundung:** `TurnToward` der Fähigkeiten rechnet die Drehung je Bild als `Rate je Tick × Ticks je Bild` (`LocomotorInfo` trägt keinen Wert je Bild, und ein Feld dort änderte das
Eingangsmanifest): eine Einheit, die genau eine ganze Zahl von Schritten vor ihrem Ziel steht, kann um ein Bild von 30 Hz abweichen. Beim Turm ist das durch die Tabelle der
30-Hz-Raten ausgeschlossen. Die Teilschritte des Turms (ein n-tel des Rests) runden abwärts; der letzte Tick setzt den Zielwinkel exakt.

**Nur bei 30 Hz:** ein Magazin ohne Nachladezeit kann bei 30 Hz durch einen zweiten Befehl **im selben Tick** noch einmal feuern (Wartezeit 0, wie immer); über 30 Hz nicht.

## 6. Neue Befunde für andere Pakete

1. **`DeployStyleAIUpdate`**: Statuswechsel bei 60 Hz einen Tick später als das Doppelte des 30-Hz-Ticks (Sentry-Drohne, §5).
2. **Ein Modul des ECM-Panzers feuert 18 Bilder + 1 Tick nach dem Befehl** (§5); der Turm-Fall der Suite misst bei diesem Objekt den Modulschuss, nicht den Turm.
3. **Der Schleier wird nach dem ersten Tick seines Bildes aufgefrischt, nicht nach dem letzten** (`SimulationWorld.Step`, `tick % VisionInterval == 0`): alles, was Sicht liest
   (`KeepsAttackIntent`, `IsAttackable`, die Prüfung eines Feuerbefehls), sieht eine Änderung über 30 Hz bis zu ½ (¾) Bild früher im selben Bild, bei 30 Hz erst im nächsten.
4. **Feuerwand des Drachenpanzers**: rund 11 % weniger Schaden bei 60/120 Hz bei gleicher Schusszahl (§5).
5. **Die Suite vergleicht Winkel ohne Umlauf** (−π gegen +π wäre ein Fehlschlag um 2π). Seit der Turm an Bildenden bitgleich ist, tritt es in `turrets` nicht mehr auf.
6. **Laufzeit des Schnellsatzes** des Referenzmanifests schwankte an diesem Tag zwischen rund 1 und 7,5 Minuten (sechs Pakete arbeiten parallel auf dem Rechner); kein Leistungsbefund.

## 7. Prüfungen

- Nach jedem der Commits: `SimTool input-manifest check` identisch, `SimTool reference-manifest check --set quick --jobs 3` 9 Läufe identisch, Simulationstests bei 30 Hz grün
  (Release). Ausnahmen: die zwei reinen Test-Commits `f5653f9` und `b405704` (keine Simulationsänderung) haben keinen eigenen Manifestlauf, sie sind mit dem Lauf von `95bcf3d` geprüft; zwei umgeschriebene Testzeilen von `6e0c691` sind bei 60 Hz erst mit dem Lauf von `276a8de` bestätigt; `01121cc` bestand bei 30 und 60 Hz und fiel erst im 120-Hz-Lauf drei Commits später auf (Berichtigung `e1252df`).
- Am Ende: am Stand `e1252df` `SimTool input-manifest check` identisch und `SimTool reference-manifest check --set full --jobs 4` **50 Läufe identisch**; `dotnet build CnCNew.slnx` 0 Warnungen, 0 Fehler (inkrementeller Build, `bin`/`obj` von `CnCNew.Client.Stride` nicht gelöscht); `dotnet run --project tests/CnCNew.Simulation.Tests` (Debug, 30 Hz) alle Tests grün; Release mit `--tick-rate 60` 4 Fehlschläge (die vier Geschossbahn-Tests), mit `--tick-rate 120` 9 (dieselben vier und Nr. 1, 2, 3, 9, 10 aus §15.2). `dotnet run --project tests/CnCNew.Presentation.Tests`: alle Tests grün, **keine Zusicherung dort ändert sich**; der Zeitmaß-Test der Übernahme (`CaptureFlashTests`, `docs/TICKRATE-MIGRATION.md` §14.4, zweite Tabelle) druckt jetzt „flashes of one preparation 30 Hz: 4, 60 Hz: 4, 120 Hz: 4“ (vorher 4 / 9 / 19), „the last flash is shown for 0,1667 / 0,1667 / 0,1500 s“ und „first flash to the end of the last tint 1,0333 / 1,0500 / 1,0417 s“ – die Tabelle in §14.4 und die Liste „drei Größen folgen der Simulation“ sind in diesem Punkt überholt (nicht von diesem Paket nachgezogen).
- Drei-Raten-Tests (`Tests.RateInvariance.Turrets.cs`, 13 Tests, benennen ihre Raten selbst): Turmschritt bitgleich an jedem Bildende und Freigabe im selben Bild, `TurretWaits`;
  Halten ein Bild mehr als die Rückstellzeit; Leerlauf-Scan über 1.500 Bilder; Abklingen der Feuerstufen; Gnadenfrist des Aufziehens; Magazin ohne Nachladezeit; Blitze der
  Übernahme; Zudrehen einer Fähigkeit; Zufallsversatz des Stimmungs-Checks über acht Startwerte; Zufallszüge der Reparaturdrohne und des Wanderers (gezählt durch Nachspielen des
  Logikstroms); Scan der Tarnvergabe; Ende des befohlenen Angriffs.
- Angefasste Dateien außerhalb der Liste des Pakets (klein gehalten): `World/SimulationWorld.cs` (eine Zeile im Konstruktor, ein Aufruf `NoteTurretWait`), `World/TurretState.cs`
  und `Templates/TurretInfo.cs` (Turmzustand und Raten je Bild), `Testing/RateEquivalence/RateCases.Turrets.cs` (zwei Messkorrekturen), in den Tests `Program.cs` (ein Block),
  `Tests.SourceRules.cs` (eine entfallene Ausnahme), `Tests.TickRate.World.cs`, `Tests.Turrets.cs`, `Tests.TurretAttack.cs`, `Tests.FiringTracker.cs` (Erwartungen, die Ticks des
  alten Modells zählten).
- Leistung bei 30 Hz: je Turmschritt zwei zusätzliche Vergleiche gegen ein Feld, keine neue Division; die Divisionen der Teilschritte und die Tabellensuche der Raten je Bild
  laufen nur über 30 Hz (die Raten je Bild werden einmal je Objekt beim Erzeugen gelesen). Nicht gemessen.
