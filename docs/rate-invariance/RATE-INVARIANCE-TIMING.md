# Raten-Invarianz, Paket TIMING: „ein Tick, wo das Original ein Bild meint“

Phase 23, Schritt 3 (`docs/handoffs/HANDOFF-PHASE23.md`), Ausgangsstand `7b54374` (7. Oktober 2026). Dieses Dokument gehört zu einem von sechs parallelen Paketen und führt nur dessen
Ursachen: `docs/TICKRATE-MIGRATION.md` §5.3 ohne die Zeilen der Türme und des Feuerzählers, die dem Paket zugewiesenen Zeilen aus §5.4 und die dazugehörigen neuen Befunde der
Raten-Äquivalenz-Suite (`docs/SIMULATION-CORE.md` §39). Die gemeinsamen Dokumente sind nicht angefasst; was hier steht, ist dort beim Zusammenführen nachzutragen.

Alles ist gemessen mit `SimTool rate-compare --group <gruppe>` (Release, Realdaten `game/content`, Kataloge ohne Modellgeometrie) und mit den Tests in
`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Timing.cs`. Zeiten in Sekunden, immer in der Reihenfolge 30 / 60 / 120 Hz. **Kein Mensch hat das Spiel bei 60 Hz gesehen.**

## 1. Ergebnis

| Gruppe | Größen | außerhalb bei 60 Hz, vorher → nachher | bei 120 Hz, vorher → nachher | Fälle mit Fehlschlag, vorher → nachher |
|---|---|---|---|---|
| `overtime` | 418 | 80 → **7** | 84 → **7** | 32 → 5 |
| `economy` | 81 | 22 → **9** | 25 → **11** | 18 → 6 |
| `timed` | 383 | 53 → **27** | 56 → **31** | 33 → 16 |
| `weapons` | 1.474 | 43 → **32** | 57 → **46** | 27 → 26 |

„Vorher“ ist der Lauf am Stand `7b54374` (identisch mit dem ersten vollständigen Lauf der Suite, §39.6), „nachher“ der Lauf am Stand `eca983f`. Bei 30 Hz hat sich nichts geändert:
`input-manifest check` und `reference-manifest check --set quick` nach jedem Commit identisch, `--set full` am Ende (Abschnitt 8).

Was in den vier Gruppen noch fehlschlägt, gehört bis auf drei benannte Einzelfälle (Abschnitt 5) anderen Paketen: Bodenbewegung (Sammler, der Weg des Dozers zur Baustelle, der Panzer
auf dem Weg zur Mine), Türme (erster Schuss, Dauerfeuer-Schusszahl, Feuerwand), Geschosse („erster Schuss bis erster Schaden“ bei 120 Hz), Hubschrauber-Absturz, Umstürzen, gestufte
Übernahme, Zug und die Heilung in Scheiben je Tick.

## 2. Das Prinzip, wie es hier angewendet wurde

Der Zeitplaner bleibt tickgenau: `SimulationWorld.RunUpdates` hebt einen Schlaf unter einem Tick weiter auf einen Tick, `Register` weckt ein Modul ohne eigenen Wunsch im nächsten
Tick, `ProcessSpawns` und die Phasenfolge von `Step` sind unverändert. Ein Modul, das sich bewegt oder integriert, muss jeden Tick laufen und mit dem Tick nach dem Entstehen
beginnen (dann ist sein Zustand an jeder Bildgrenze der 30-Hz-Zustand). Eine Regel im Zeitplaner, die „SleepNone“ oder „erstes Update“ für alle Module auf ein Bild legt, hätte genau
diese Module gebremst. Gesucht war eine Formulierung ohne Modulwissen im Zeitplaner; es gibt keine, weil derselbe Rückgabewert (`SleepNone`) und derselbe erste Weckzeitpunkt bei
einem Integrator „nächster Teilschritt“ und bei einem Ereignismodul „nächstes Bild“ heißen. **Die Lösung ist deshalb je Modul**, in drei Formen:

1. **Erstes Update ein Bild nach dem Entstehen** (`world.Tick + world.Time.TicksPerOriginalFrame` statt `+ SimulationTiming.SleepNone` im `OnSpawn`) für Module, deren erstes Update
   ein Ereignis entscheidet. Bei 30 Hz derselbe Tick.
2. **„N Bilder weniger ein Tick“ wird „N Bilder weniger ein Bild“:** der Schritt, in dem ein Befehl ausgeführt wird, steht bei 30 Hz für ein ganzes Bild. Über 30 Hz bekommt genau
   dieser erste Schritt den Rest seines Bildes gutgeschrieben (Verkauf, Produktion, Bau, Phasen des Hackers). Jeder weitere Schritt ist ein Tick.
3. **Eine Frist von null hinter einer Abfrage je Tick ist das nächste Bild:** neuer Helfer `SimulationTime.ZeroIsNextFrame(ticks)` (`src/CnCNew.Simulation/Time/FrameGrid.Timing.cs`):
   eine Frist ungleich null bleibt, null bleibt bei 30 Hz null (der gespeicherte Zeitpunkt behält seinen Wert, der nächste Versuch *ist* das nächste Bild) und wird darüber ein Bild.
   Untergrenzen „ein Tick“ (`Math.Max(…, SleepNone)`) sind `AtLeastOneFrame` geworden, wo sie ein Bild meinen.

Ein Ereignis fällt damit auf den **ersten** Tick des Bildes, in dem es bei 30 Hz fällt – das ist die Zeitkonvention der Suite („was im Schritt des Befehls geschieht, hat die Zeit 0“).
Eine Integration, die je Tick unterteilt ist, erreicht ihren Bildwert dagegen erst mit dem **letzten** Tick des Bildes. Beides zugleich lässt sich nicht exakt gleich messen; das
betrifft die Heilung in Scheiben je Tick (Abschnitt 5, offen).

## 3. Ursachen, je Zeile ein Commit

| §5-Eintrag | Befund (30 / 60 / 120 Hz vorher) | Korrektur (Datei, Symbol, Prinzip) | nachher | Commit |
|---|---|---|---|---|
| §5.3 `Register` (erstes Update einen Tick nach dem Erscheinen) – Feld und Tarnung | Giftfeld, erster Schaden 0,0333 / 0,0167 / 0,0083 s (42 Größen, dazu 17 „Tod eines Opfers“); Tarnung nach dem Entstehen 2,0333 / 2,0167 / 2,0083 s (9 von 14) | `CombatModules.cs` `FireWeaponUpdate.OnSpawn`, `Phase5Modules.cs` `StealthUpdate.OnSpawn`: erstes Update ein Bild nach dem Entstehen (Form 1) | 0,0333 und 2,0333 s bei jeder Rate; `overtime` 80/84 → 21/25, `timed` 53/56 → 44/47 | `9f61b3d` |
| – (Messung der Suite) | Selbstheilung der Mine 14,9333 / 14,9500 / 14,9583 s, Basisregeneration 2,9667 / 2,9833 / 2,9917 s, Sanitäter/Hospital/Reparaturbucht 0,9333 / 0,9500 / 0,9583 s, Lautsprecherturm 1,9000 / 1,9167 / 1,9250 s; in `MeasureDetector` war derselbe Fehler der Simulation wie in den Tarnfällen verdeckt | `RateCases.OverTime.cs` `MeasureRecovery`: die Zeit eines „Bildes mit Heilung“ ist der erste Schritt des Bildes, nicht der letzte; `RateCases.Timed.cs` `MeasureDetector`: `Align` vor dem Entfernen des Detektors. **Nur Messung**, keine Toleranz und kein Fall berührt (Abschnitt 4) | alle genannten Größen bei jeder Rate gleich; `overtime` 21/25 → 7/7, `timed` 44/47 → 52/55 (8 Detektorfälle zeigen jetzt den echten Unterschied) | `a6fdfea` |
| §5.3 `StealthDetectorUpdate` bei `DetectionRate = 0`; §5.3 `StealthUpdate.MarkDetected` | „Detektor weg bis wieder verborgen“ 0,5000 / 0,4833 / 0,4750 s (16 Fälle nach der berichtigten Messung): die Marke hielt `DetectionRate` + **ein Tick** | `Phase5BlockerModules.cs` `StealthDetectorUpdate.Update`: Marke `DetectionRate` + ein Bild (`markAsDetected(m_updateRate + 1)`, `StealthDetectorUpdate.cpp`, `update`), Schlaf `AtLeastOneFrame`; `Phase5Modules.cs` `StealthUpdate.MarkDetected`: Untergrenze ein Bild. Zwei benannte Ausnahmen der Quelltextregel „one tick minimum“ entfallen | 0,5000 s bei jeder Rate (auch 0,6 und 1,5 s der anderen Detektoren); `timed` 52/55 → 36/39 | `f922979` |
| §5.3 `_saleDurationTicks` | Verkauf 5,9667 / 5,9833 / 5,9917 s (3 von 3) | `SimulationWorld.cs` (Konstruktor): 180 Bilder weniger ein **Bild** (Form 2). Der Drei-Raten-Test der Phase 20 hielt die alte Form fest (179 / 359 / 719 Ticks) und erwartet jetzt 179 / 358 / 716 | 5,9667 s bei jeder Rate; `economy` 22/25 → 19/22 | `404fab6` |
| neu (Suite): Produktion | Ranger 4,9667 / 4,9833 / 4,9917 s (7 von 7; die 4 Flugfeld-Produktionen der Gruppe `flight` haben dieselbe Ursache) | `Phase5Modules.cs` `ProductionUpdate.Enqueue`: der Eintrag, der eine leere Warteschlange beginnt, bekommt den Rest des ersten Bildes gutgeschrieben (Form 2); ein wartender Eintrag ist unverändert (er dauerte schon bei jeder Rate die volle Bauzeit) | 4,9667 s, zweite Einheit 9,9667 s bei jeder Rate; `economy` 19/22 → 12/15 | `43d4a30` |
| neu (Suite): Bau | Kraftwerk 15,2333 / 15,2500 / 15,2500 s; die Arbeit selbst dauerte Bauzeit weniger ein Tick | `SimulationWorld.Construction.cs` `AdvanceConstruction`: der erste Arbeitsschritt zählt ein ganzes Bild (Form 2), die Gesundheit folgt der Arbeit | Arbeit bei jeder Rate Bauzeit weniger ein Bild (Test: 59 Bilder bei 2 s). In der Suite: USA-Kaserne 14,1000 bei jeder Rate; Kraftwerk 15,2333 / 15,2333 / 15,2250, China-Kraftwerk 15,0333 / 15,0167 / 15,0167, GLA-Kaserne 14,6333 / 14,6167 / 14,6167 – die Größe enthält den Weg des Dozers, der über 30 Hz bis zu einen Tick früher ankommt (Bodenbewegung, §5.2); `economy` 12/15 → 12/14 | `89283a0` |
| §5.3 `HackInternetAIUpdate` – erster Zähler nach einem Befehl von außen | Hacker zahlt 8,1000 / 8,1167 / 8,1250 s, alle weiteren Zahlungen um denselben Tick versetzt | `HackerAndAssaultModules.cs` `Varied`, `OrderMove`: eine Phase, die ein Befehl startet (Auspacken, Einpacken), dauert ihre Bilder – das Update desselben Ticks zählt schon; nur der Zähler, den das Update selbst nachlädt (Geldtakt), dauert Bilder + 1 (`CountdownReload`) | 8,1000 / 10,1333 / 26,4000 s bei jeder Rate; `economy` 12/14 → 9/11 | `938a32c` |
| neu (Suite): Aufladen ab der Entstehung bei sechs Superwaffen | 120,0000 / 120,0167 / 120,0083 s. **Was sie von den 62 anderen Kräften unterscheidet:** ihr Träger hat `SpecialPowerCreate`, das das Aufladen im ersten Update (einen Tick nach dem Entstehen) neu startet. Die Suite liest ein Bild nach dem Entstehen: bei 30 Hz vor, bei 60/120 Hz nach diesem Neustart | `SpecialPowerModules.cs` `BuildCompleteModule.OnSpawn` (`SpecialPowerCreate`, `GrantUpgradeCreate` und die übrigen Create-Module): `onBuildComplete` ein Bild nach dem Entstehen (Form 1) | bei jeder Rate gleich (der Neustart liegt bei jeder Rate ein Bild nach dem Entstehen: bereit `ReloadTime` + ein Bild danach); `timed` 36/39 → 30/33 | `4e83582` |
| §5.3 `BaseRegenerateUpdate.OnDamage`, `SupplyWarehouseCripplingBehavior.OnDamage` (`Math.Max(…, SleepNone)`) | bei einer Frist von 0 Heilung einen Tick statt ein Bild nach dem Treffer (Retail hat keine Frist von 0: Suite unverändert) | `MatchModules.cs`, `LastBlockerModules.cs`: `AtLeastOneFrame`; die beiden benannten Ausnahmen der Quelltextregel entfallen | Test (`BaseRegenDelay` 0): Treffer bis erste Regeneration ein Bild bei jeder Rate; `overtime` 7/7 → 7/7 | `c6ce657` |
| neu (Suite): Nachladen 0 | Schlachtschiff-Geschütze: Nachladen 0,0333 / 0,0167 / 0,0083 s, Schüsse in 60 s 110 / 112 / 112, Schaden +1,9 % | `SimulationWorld.WeaponSlots.cs` `ConsumeShot`, `CombatModules.cs` `FireWeaponUpdate.Update`: `ZeroIsNextFrame` auf die Nachladezeit (Form 3); `WeaponInfo.ClipReloadWith` unverändert | Nachladen 0,0333 s, 110 Schüsse, 20. Schuss 10,1667 s bei jeder Rate; `weapons` 43/57 → 32/46 | `798d263` |
| §5.3 `BattlePlanUpdate.Update` (ein Zustandsübergang je Tick) | ein Zustand mit der Dauer 0 (`TransitionIdleTime` 0 im Retail-Strategiezentrum) wird vom nächsten Update verlassen: ein Tick statt ein Bild. Planwechsel bis zum neuen Plan auf der Fixture 2,0333 / 2,0167 / 2,0083 s (aus dem Quelltext, nicht gemessen) | `BattlePlanModule.cs` `SetStatus`: die drei Fristen über `ZeroIsNextFrame` (Form 3) | Test: 30 Bilder bis zum ersten Plan, 61 Bilder für einen Wechsel, bei jeder Rate (gemessen); Suite stellt Kampfpläne nicht (`timed` 30/33 → 30/33) | `529d514` |
| §5.3 `LeafletDropBehavior.Update`; §5.4 `FireWeaponWhenDamagedBehavior`; dazu `HeightDieUpdate`, `TensileFormationUpdate` (Suite) | Flugblattbombe mit dem Standard-`Delay` von einem Bild: wirkt nach 0,0333 s / nie / nie (über 30 Hz schlief das Modul für immer). Lawine bemerkt den Schaden 0,9667 / 0,9500 / 0,9417 s. Bombengas der Aurora am Boden: „Tötung bis Entfernung“ 0,0000 / – / – (über 30 Hz schon vor der Tötung entfernt) | `AirWeaponModules.cs`, `Phase12Modules.cs`, `ToppleAndFallModules.cs`, `FieldEffectModules.cs`: je eine Zeile, erstes Update ein Bild nach dem Entstehen (Form 1; bei `FireWeaponWhenDamagedBehavior` auch nach dem Upgrade) | Flugblatt 0,0333 s bei jeder Rate (Test), Lawine 0,9667 s, Bombengas 0,0000 s bei jeder Rate; `timed` 30/33 → 28/31 | `68ae9c3` |
| §5.3 `FirestormDynamicGeometryInfoUpdate` | der Hinweg dauert `TransitionTime` (er beginnt in dem Update, das die Verzögerung beendet) und war bei jeder Rate gleich; **nur der Rückweg** (`ReverseAtTransitionTime`) dauert `TransitionTime` + ein Update. Fixture: Entstehen bis fertig 2,1333 / 2,1167 / (2,1083 aus dem Quelltext) s | `WorldEffectModules.cs` `Update`: der Zähler des Rückwegs beginnt ein Bild weniger ein Tick unter null (0 bei 30 Hz), die Geometrie ruht so lange auf dem Endwert | 2,1333 s bei jeder Rate (Test); der Feuersturm-Fall der Suite misst Schaden und bestand schon (`timed` 28/31 → 28/31) | `47c760c` |
| §5.4 `MinefieldBehavior` (Abtastung je Tick), Einzelfall Streumine | Streumine entfernt nach 4,8000 / 4,7000 / 4,6667 s: der Schwellweg `RepeatDetonateMoveThresh` wurde je Tick verglichen und nach einem kürzeren Weg überschritten gefunden | `MineModules.cs` `OnCollide`: eine Einheit auf der Mine wird nur in ganzen Bildern nach ihrer ersten Berührung wieder betrachtet; die Phase im Bild steht je Einheit im Zustand (0 bei 30 Hz, nur geschrieben, wenn ungleich 0). **Ausweg „Originalraster“** (Abschnitt 6) | 4,8000 / 4,8000 / 4,8417 s; die 5 Ticks bei 120 Hz sind die Ankunft des Panzers (erster Schaden 3,3667 / 3,4000 / 3,4083 s, Bodenbewegung); `timed` 28/31 → 27/31 | `eca983f` |

Tests: `test:`-Commit am Ende (Abschnitt 7).

## 4. Die berichtigte Messung (`a6fdfea`)

Zwei Messungen der Suite fügten selbst einen Versatz unter einem Bild hinzu, entgegen §39.1 („die Suite fügt selbst keinen Versatz unter einem Bild hinzu“):

- **`MeasureRecovery`** vergleicht die Gesundheit nach dem letzten Schritt eines Bildes und trug diesen Schritt als Zeit ein. Eine Heilung im ersten Tick des Bildes – also genau auf
  dem Raster – erschien bei 60 Hz einen Tick und bei 120 Hz drei Ticks zu spät. Die Zeit ist jetzt der erste Schritt des Bildes (bei 30 Hz derselbe Schritt). Damit sind
  Selbstheilung, Basisregeneration und die Heilpulse von Sanitäter, Hospital, Reparaturbucht und Lautsprecherturm bei jeder Rate gleich: **die Simulation war dort nie verschieden**
  (die Zeilen „Selbstheilung“ und „Basisregeneration“ der Ursache 7 in §39.6 waren ein Messfehler).
- **`MeasureDetector`** entfernte den Detektor nach einer Schleife, die einen Schritt nach dem Scan beginnt: bei 30 Hz ein Bild, darüber einen Tick nach dem letzten Scan. Der
  Versatz hob den echten Fehler (Marke hält Rate + ein Tick) gerade auf; mit `Align` vor dem Entfernen zeigten acht Detektorfälle denselben Fehlschlag wie die acht Tarnfälle, und
  `f922979` behebt alle sechzehn.

Keine Toleranz, keine Größenart und kein Fall ist verändert.

## 5. Offen, bewusst gelassen, nur gemeldet

**Bewusst unverändert (ändert kein Ergebnis oder ist Unterteilung):**

- §5.3 `SimulationWorld.RunUpdates`, `Register`, `ProcessSpawns`, `Step` (Phasenfolge): der Zeitplaner bleibt tickgenau (Abschnitt 2). Kein verbliebener Fehlschlag der vier Gruppen
  hängt an einer Spawn-Kette oder an der Phasenfolge.
- §5.4 `MinefieldBehavior.Update`: das Auszehren nach dem Tod des Erbauers ist ein Gesundheitsbetrag je Tick (unterteilte Integration).
- §5.4 `ProductionUpdate.Update` (Tür je Tick abfragen): die Türfrist wird in skalierten Ticks ab einem Ausgang auf dem Raster gezählt, die Abfrage je Tick findet die Tür im selben
  Tick frei wie bei 30 Hz. Nur Last. („Beide Einheiten stehen“ ist bei jeder Rate gleich.)
- §5.4 `FireWeaponWhenDamagedBehavior.Update`: Abfrage je Tick, die Bereitschaftsticks liegen im Raster. Nur Last (das erste Update ist berichtigt, `68ae9c3`).
- §5.4 `FloatUpdate`: setzt je Tick die Höhe auf den Wasserspiegel. Nur Last.
- §5.4 `CheckpointUpdate`: zwei Suchen je Tick, der Torschritt ist je Tick skaliert (Unterteilung). Nur Last.
- §5.4 `FireOCLAfterWeaponCooldownUpdate`: zählt einen Schuss im Tick danach und feuert die Liste, sobald die Waffe bereit war und nicht schoss – beides Reihenfolge, bei jeder Rate
  dasselbe; die Lebensdauer wird in Bildern gerechnet. Nur Last.

**Offen, in den Gruppen dieses Pakets, aber nicht seine Ursache:**

- **Heilung in Scheiben je Tick** (§5.6 `RepairDockUpdate`, `HealContain`, §5.2 `TunnelContain`, dazu `PropagandaTowerBehavior`): „Bilder mit Heilung“ 150 / 151 / 151 (Reparaturdock),
  75 / 76 / 76 (Tunnel), 750 / 751 / 751 (Lautsprecherturm); „volle Gesundheit“ 26,8667 / 26,9000 / 26,9000 s. Zwei Gründe: die Scheibe je Tick ist für sich gerundet (die Summe
  bleibt um Bruchteile unter dem Bildwert, die letzte Scheibe fehlt), und eine unterteilte Integration erreicht den Bildwert erst mit dem letzten Tick des Bildes, während die Suite
  die Größe als `ExactTime` führt. Exakt gleich wird das nur, wenn der Bildbetrag einmal je Bild gutgeschrieben wird (Ausweg „Originalraster“ für Heilung) – eine Entscheidung, die
  nicht in diesem Paket liegt. Die geheilte Menge in 60 s ist bei jeder Rate gleich.
- **Dauerfeuer, ein Schuss mehr in 60 s** (Toxin-Laster 899 / 900 / 900, Gattling-Panzer 440 / 441 / 441, Gattling-Kanone 886 / 887 / 887): der Schussabstand ist exakt gleich; der
  **erste** Schuss fällt über 30 Hz früher (Toxin-Laster: Befehl bis erster Schuss 0,0667 / 0,0500 / 0,0417 s – ein Bild plus ein Tick statt zwei Bilder), und damit passt ein Schuss
  mehr in das Fenster von 60 s. Ursache in `World/SimulationWorld.Turrets.cs` (§5.3 `StepTurrets`: der Zusatzschritt des Haltens/Ausrichtens ist ein Tick, kein Bild), Paket TURRETS.
  Nicht in `FiringTracker.cs`/`Acquisition.cs` und nicht im Schussabstand.
- **Feuerwand des Dragon Tanks** 838 / 899 / 899 Schüsse (30 Hz verliert 61 Schüsse in 60 s, rund einen je Schwenk): `World/SimulationWorld.Turrets.cs`. Die Waffe schwenkt
  (`TurretFireAngleSweep = SECONDARY 60`, `TurretSweepSpeedModifier 2.0`); `IsAligned` gibt eine schwenkende Waffe nur frei, solange die Abweichung **kleiner** als der Schwenkwinkel
  ist (`difference >= sweep` hält zurück). Bei 30 Hz ist der Drehschritt größer als die Umkehrschwelle von 0,035 rad: der Turm rastet am Ende des Schwenks genau auf dem Endwinkel
  ein (Abweichung = Schwenkwinkel), und der Schuss dieses Ticks fällt aus. Bei 60 und 120 Hz kehrt der Turm innerhalb der Schwelle um, bevor er einrastet, und hält nie an. Das ist
  §5.2 `AimStep`/`TurnAngle` (Einrasten unter einem Tickschritt, Umkehr bei 0,035 rad) zusammen mit `IsAligned`, Paket TURRETS. Aus Quelltext und Zahlen geschlossen (61 Ausfälle bei
  rund einem Schwenk je Sekunde), nicht durch eine Spur belegt. Der Schaden (14.605,5 / 14.250,5 / 14.253,0) folgt daraus und aus den Feuerwand-Segmenten.
- Bodenbewegung, Geschosse, Hubschrauber-Absturz, Umstürzen, Übernahme, Zug: unverändert, andere Pakete.

**Drei Einzelfälle, nur gemeldet:**

- **`CINE_BShipWeaponDetonationObject` endet bei 30 Hz nie – ein Fehler bei 30 Hz, nicht bei 60 Hz.** Das Objekt hat `LifetimeUpdate` 1 ms (ein Bild) und `SlowDeathBehavior` mit
  `DestructionDelay` 1 ms. Im Tick nach dem Entstehen sind beide Module fällig (das `SlowDeathBehavior` nur, weil `Register` jedes Modul ohne eigenen Wunsch im nächsten Tick weckt).
  `LifetimeUpdate` tötet, `BeginSlowDeath` verlangt `WakeAt(…, Tick + 1)`; `SimulationWorld.WakeAt` verwirft genau diese Bitte („ein waches Modul für den nächsten Tick zu wecken
  darf den Aufruf dieses Ticks nicht verschieben“), das Update des `SlowDeathBehavior` läuft im selben Tick, sieht die Frist nicht erreicht und gibt `SleepForever` zurück. Das Objekt
  bleibt für immer. Bei 60/120 Hz ist die verlangte Zeit nicht „der nächste Tick“, die Bitte greift, das Objekt endet nach 0,0667 s. Die Korrektur (`SlowDeathBehavior.Update` gibt
  den Rest bis zur Frist zurück, statt für immer zu schlafen) **ändert das Verhalten bei 30 Hz** und ist deshalb nicht gemacht; ob sie das Manifest ändert, ist nicht geprüft.
- **Leerer Fallschirm (`AirF_AmericaCrateParachute`), „Tötung bis Entfernung“ 3,0000 / 2,9833 / 2,9750 s:** `ParachuteContain.Update` tötet einen Fallschirm ohne Insassen im ersten
  Update, einen Tick nach dem Entstehen; die 4 s des langsamen Todes laufen ab da (die Tötung der Suite trifft ein schon sterbendes Objekt). `FlightModules.cs` gehört dem Paket
  Flug, und das Modul integriert den Fall je Tick – das erste Update zu verschieben würde den Fall verschieben. Vorschlag: nur die Selbsttötung ein Bild nach dem Entstehen
  (`world.Tick < Entity.SpawnTick + TicksPerOriginalFrame` abwarten); bei 30 Hz derselbe Tick.
- **Mob-Mitglied ohne Nexus (`Demo_GLAInfantryAngryMobMolotov02`), 7,0000 / 6,9833 / 6,9750 s:** `MobMemberSlavedUpdate.Update` tötet ein Mitglied ohne Herrn im ersten Update, einen
  Tick nach dem Entstehen. Das Modul zählt seine Prüfungen in Updates ab dem ersten (das mit dem Tick nach dem Entstehen richtig liegt); nur die Selbsttötung braucht dieselbe
  Wartebedingung wie oben. `SlaveModules.cs`, nicht in diesem Paket.

**Module, deren erstes Update weiter einen Tick nach dem Entstehen kommt** (im `OnSpawn` selbst gesetzt; nicht einzeln geprüft, die Suite zeigt an ihnen keinen Fehlschlag in den
Gruppen dieses Pakets): `ParkingPlaceBehavior`, `BunkerBusterBehavior`, `EmpUpdate`, `MissileLauncherBuildingUpdate`, `SpectreGunshipUpdate`, `BattlePlanUpdate`,
`CleanupHazardUpdate`, `OverlordContain`, `RiderChangeContain`, `GarrisonContain`/`HealContain`, `TransportContain`, `PilotFindVehicleUpdate`, `AutoDepositUpdate`,
`RailedTransportAIUpdate`, `RailedTransportDockUpdate`, `PropagandaTowerBehavior`, `ParachuteContain`, `AssaultTransportAIUpdate`, `JetAIUpdate`, `SmartBombTargetHomingUpdate`,
`BaseRegenerateUpdate`, `MinefieldBehavior`, `GenerateMinefieldBehavior`, `QueueProductionExitUpdate`, `PointDefenseLaserUpdate`, `RailroadBehavior`, `AutoFindHealingUpdate`,
`RebuildHoleBehavior`, `MobMemberSlavedUpdate`, `SpawnBehavior`, `GrantStealthBehavior`, `ProjectileStreamUpdate`, `WanderAIUpdate`, `FloatUpdate`, `ChinookAIUpdate`,
`TunnelContain`, `BattleBusSlowDeathBehavior`, `DynamicShroudClearingRangeUpdate`, `FirestormDynamicGeometryInfoUpdate`, `WaveGuideUpdate`, `WeaponBonusUpdate` – und jedes Modul
ohne eigenen Wunsch (`Register`), darunter `AIUpdateInterface`, die Geschossmodule und `PhysicsBehavior`. Darunter sind Integratoren (für die der nächste Tick richtig ist), Zähler,
die ab dem ersten Update in Updates zählen (für die er ebenfalls richtig ist: N Updates enden N Ticks nach dem Entstehen), und Ereignismodule, deren Fall die Suite nicht stellt.
Faustregel für neue Module: entscheidet das erste Update ein Ereignis, kommt es ein Bild nach dem Entstehen.

## 6. Verwendungen des Auswegs „Originalraster“

| Stelle | Was auf dem Raster läuft | Begründung |
|---|---|---|
| `Modules/BuiltIn/MineModules.cs`, `MinefieldBehavior.OnCollide` | der Vergleich „hat sich die Einheit seit der letzten Detonation um `RepeatDetonateMoveThresh` bewegt“, einmal je Bild ab der ersten Berührung dieser Einheit (die erste Berührung selbst zählt in ihrem Tick) | das Original vergleicht einmal je Bild; der Vergleich lässt sich nicht unterteilen, weil die Schwelle gegen den Weg *seit dem letzten Vergleich* steht – ein feinerer Takt quantisiert den Weg feiner und zündet schneller. Reaktion auf eine Kollision, weder Bewegung noch Befehlsannahme noch Zielerfassung. Die Phase hängt an der Einheit, nicht am Welttick, damit ein anderes Raster der Kollisionsmeldung die Mine nicht stumm macht |

Keine Verwendung des Auswegs, aber damit verwandt: `StealthDetectorUpdate` scannt bei `DetectionRate = 0` jetzt einmal je Bild statt einmal je Tick (Untergrenze `AtLeastOneFrame`,
die übliche Regel aus Phase 20; Retail hat keinen Detektor mit Rate 0).

## 7. Tests

`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Timing.cs`, neun Tests auf der synthetischen Fixture, jeder bei 30, 60 und 120 Hz, Erwartung in Bildern des Originals:
erstes Update (Feld, Tarnung mit und ohne Frist, Flugblattbombe), Detektormarke, Produktion und Bau, Hacker, `onBuildComplete`, Nachladen 0 (Waffenplätze und `FireWeaponUpdate`,
dazu `ZeroIsNextFrame` selbst), Regenerationsfrist 0, Kampfplan, Feuersturm. Die Mine ist nur über die Suite geprüft (ein Test bräuchte eine raten-invariante Fahrt).

Angepasste Erwartungen bestehender Tests (Klasse T, `docs/TICKRATE-MIGRATION.md` §15.2, und Tests, die das alte Verhalten bei 60/120 Hz festhielten):

| Test | Änderung |
|---|---|
| Nr. 1 „Phase 5: StealthDetectorUpdate marks and expires DETECTED“ | drei Bilder statt drei Ticks bis zur Tarnung |
| Nr. 3 „Construction: snapshot mid-construction and mid-sale, replay round trip“ | Befehlsticks, Ende und Prüfpunktabstand des Replays in Bildern |
| Nr. 2 „Phase 5: reference match scenario, replay v2 factions, determinism“ | auf 30 Hz festgelegt, mit Begründung im Test |
| „Tick rate, world: a sale takes …“ | 179 / 358 / 716 Ticks statt 179 / 359 / 719 |
| „Tick rate, modules A–M: the avalanche's dormant check …“ | das erste Update liegt ein Bild nach dem Entstehen |
| „Tick rate, modules A–M: HackInternetAIUpdate …“ | die erste Zahlung liegt auf dem 30-Hz-Bild (Versatz 0 statt „unter einem Bild“) |
| „Construction: a dozer builds a structure over BuildTime ticks“ | 59 Bilder plus der erste Schritt |
| „Construction: sale takes 179 ticks …“ | `Frames(179)` |
| „Special powers: shared timers, create hooks …“ | `GrantUpgradeCreate` ein Bild nach dem Entstehen |
| `Tests.SourceRules.cs` | vier benannte Ausnahmen der Regel „one tick minimum“ entfernt (die Ausdrücke gibt es nicht mehr) |

## 8. Prüfläufe

| Lauf | Ergebnis |
|---|---|
| `input-manifest check` | identisch, nach jedem der 13 Simulations-Commits und am Ende |
| `reference-manifest check --set quick --jobs 3` | 9 Läufe identisch, nach jedem der 13 Simulations-Commits und nach dem Commit der Messung |
| `reference-manifest check --set full --jobs 4` | **50 Läufe identisch**, am Stand `eca983f` (der letzte Commit, der die Simulation ändert; der Test- und der Doku-Commit danach ändern sie nicht) |
| `dotnet build CnCNew.slnx` | 0 Warnungen, 0 Fehler |
| Simulationstests, Release, ohne `--content-dir`, 30 Hz | alle 427 grün (418 bestehende, 9 neue) |
| dieselben mit `--tick-rate 60` | 6 Fehlschläge (vorher 6, dieselben: Blitzzahl der gestuften Übernahme, vier Geschossbahnen, Ratenliteral der Turmdrehrate) |
| dieselben mit `--tick-rate 120` | 12 Fehlschläge (vorher 14): Nr. 1, 2 und 3 aus `docs/TICKRATE-MIGRATION.md` §15.2 bestehen; die übrigen elf (Nr. 4–14) sind unverändert; **neu:** „Skirmish: two AIs play a match“ endet ohne Sieger (auch mit einem auf dieselbe Spielzeit skalierten Budget) – ein ganzes Match, Klasse C, nicht umgestellt und keiner einzelnen Ursache zugeordnet |
| Gruppe `flight` der Suite (nicht in diesem Paket) | 21 / 21 → 17 / 17: die vier Produktionszeiten der Flugfelder sind mit `43d4a30` gleich |

Nicht gemessen: die Kosten je Tick (kein Leistungslauf; die Änderungen liegen in `OnSpawn`, im Einreihen, im ersten Arbeitsschritt, beim Leerschießen eines Magazins und in einem
Vergleich je Kollisionsmeldung einer Mine, nicht im Pfad je Tick eines Moduls – außer `Math.Max(_timeActive, 0)` im Feuersturm und der Wahl des Arbeitsschritts in
`AdvanceConstruction`), die Gruppen `locomotion`, `projectiles`, `turrets`, `scripts` und `ai` der Suite, Kampagne und KI-Läufe bei 60 Hz. Das Ergebnis des Debug-Laufs der
Simulationstests bei 30 Hz steht im Abschlussbericht des Pakets.

## 9. Neue Befunde

- Zwei Messungen der Suite hatten einen eigenen Versatz (Abschnitt 4); Selbstheilung und Basisregeneration waren nie verschieden.
- `CINE_BShipWeaponDetonationObject`: ein Objekt, das bei 30 Hz nie endet (Abschnitt 5) – `SimulationWorld.WakeAt` verwirft eine Weckbitte für den nächsten Tick, wenn das Modul im
  laufenden Tick noch fällig ist, und `SlowDeathBehavior.Update` schläft dann für immer. Betrifft jedes Objekt mit einem langsamen Tod von einem Bild, der im Tick nach dem Entstehen
  beginnt.
- Die Feuerwand verliert bei **30 Hz** Schüsse am Schwenkende (Abschnitt 5); welches Verhalten das des Originals ist, ist nicht geprüft.
- `FirestormDynamicGeometryInfoUpdate`: nur der Rückweg war kürzer, nicht der Übergang als solcher (§5.3 nennt beide).
- Die Dauerfeuer-Schusszahl ist ein Fensterrand: wer den ersten Schuss berichtigt, behebt alle 14 Fälle zugleich.
- Ein Ereignis auf dem ersten Tick eines Bildes und eine je Tick unterteilte Integration lassen sich nicht beide `ExactTime`-gleich messen (Abschnitt 2 und 5, Heilung).
