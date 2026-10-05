# Tickraten-Kopplung – geprüftes Inventar

Stand: 6. Oktober 2026, geprüft am Commit `e5046b7` (`main`). Dieses Dokument ersetzt das ungeprüfte Startinventar (Commit `2a66661`) und ist die Arbeitsgrundlage für die Phasen 20, 21 und 23 des Programms in `docs/MULTIPLAYER-PLAN.md`. Es ist ein Arbeitsdokument: jede Stelle nennt Datei, Zeile **und Symbol** (Zeilennummern verschieben sich mit jedem Commit, das Symbol bleibt auffindbar).

**Was „geprüft“ hier heißt (§1):** Jede Stelle der Tabellen in §5 bis §7 wurde im Quelltext mit ihrer Umgebung (die ganze Methode, nicht die Trefferzeile) gelesen und genau einer Klasse zugeordnet. Die Simulation, die Spielschicht und die Präsentation wurden dateiweise vollständig gelesen; UI, Ton, Client, Daten, Werkzeuge und Tests treffergetrieben. Was nicht gelesen wurde, steht mit Zahl in Anhang B und nirgends sonst. Es wurde **kein Quelltext geändert und nichts bei 60 Hz ausgeführt** – jede Aussage „bei 60 Hz …“ ist aus dem Code gefolgert, nicht gemessen. Gemessen sind die Zählungen dieses Dokuments und die Quantisierung auf den Realdaten (§4.4).

Abkürzungen: `Sim\` = `src/CnCNew.Simulation/`, `Mod\` = `Sim\Modules/BuiltIn/`, `Pres\` = `src/CnCNew.Presentation/`, `Game\` = `src/CnCNew.Game/`, `Ui\` = `src/CnCNew.Ui/`, `Client\` = `src/CnCNew.Client.Stride/`, `Data\` = `src/CnCNew.Data/`. „Bild“ = ein Logikbild des Originals (1/30 s), „Tick“ = ein Schritt der Simulation.

## 0. Zusammenfassung für Phase 20/21/23

### 0.1 Zahlen

1959 gekoppelte Stellen in `src`, `tools` und `tests` (Zählregel §1.2). Die Spalte B enthält 150 mechanisch gezählte Umrechnungsaufrufe (§2), alles andere sind einzeln gelesene Stellen.

| Projekt | A | B | C | D | E | F | G | H | Summe |
|---|---|---|---|---|---|---|---|---|---|
| `CnCNew.Simulation` | 39 | 267 | 81 | 43 | 138 | 12 | 15 | 65 | 660 |
| `CnCNew.Game` | 7 | 15 | 6 | 4 | 3 | 30 | 1 | 0 | 66 |
| `CnCNew.Presentation` | 2 | 8 | 7 | 20 | 21 | 44 | 0 | 3 | 105 |
| `CnCNew.Ui` | 0 | 3 | 0 | 5 | 0 | 21 | 0 | 0 | 29 |
| `CnCNew.Client.Stride` | 0 | 0 | 0 | 2 | 0 | 2 | 0 | 10 | 14 |
| `CnCNew.Audio` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `CnCNew.Data` | 0 | 0 | 0 | 14 | 0 | 0 | 0 | 0 | 14 |
| übrige src | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| tools | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 55 | 55 |
| tests | 0 | 0 | 0 | 14 | 0 | 0 | 0 | 1002 | 1016 |
| **Summe** | **48** | **293** | **94** | **102** | **162** | **109** | **16** | **1135** | **1959** |

Klassen: **A** Originalkonstante in Bildern; **B** Zeitwert; **C** Ablaufrhythmus; **D** rohe Daten je Bild; **E** nicht invariante Integration; **F** Präsentationsuhr; **G** Format; **H** Test oder Werkzeug. Definitionen in §1.1. `CnCNew.Audio` hat keine Kopplung an den Tick (kein Verweis auf die Simulation, keine 30er-Konstante); „übrige src“ sind `CnCNew.W3D`, `Archives`, `Content`, `Formats`, `Images`, `Import`, `IO` – dort nichts (die W3D-Animationsuhr rechnet in Sekunden mit der Bildrate aus der Datei). `CnCNew.Data` enthält keine 30-Hz-Umrechnung; die 14 Stellen dort sind Felder, die nur als `Real`/`Integer` typisiert sind, obwohl das Original sie je Bild anwendet – dieselben Felder von der Schemaseite gesehen, die bei ihrem Verbraucher in der Simulation noch einmal als D oder E stehen (keine zusätzliche Arbeit, aber der Ort, an dem ein Feldtyp „je Bild“ fehlt).

Dazu 222 gespeicherte Zustandsfelder mit absoluten Ticks in 56 Dateien (§4.3, Anhang C), 5198 geprüfte, nicht gekoppelte Trefferzeilen (Anhang A) und 1908 nicht geprüfte Trefferzeilen (Anhang B).

### 0.2 Die zehn riskantesten Stellen

Rang nach „ändert Spielergebnisse und ist mit Skalieren allein nicht zu lösen“. Jede Stelle wurde vom Koordinator selbst nachgelesen.

1. **Bodenbewegung und Flügelflug** – `AIUpdateInterface.Move`, `MoveWinged`, `SteerWinged`, `MoveStraight` (`Mod\Phase5Modules.cs`, Zeilen 1059–1253): Ankunftsradius `max(CloseEnoughDist, Speed)` (ein Tick Weg), Euler-Schritt für Drehung, Beschleunigung und Bremsweg, Neuplanung `world.Tick - _blockerReplanTick >= 30`, Vorausschau `Speed * 25`; dazu `SimulationWorld.AvoidLocalBlockers` (Vorausschau genau ein Tick Weg, `Sim\World/SimulationWorld.Collision.cs`) und die Jet-Kinematik (`Mod\JetAIUpdate.cs`: Literale 1 und 0,5 je Tick, `Speed/8` als Beschleunigung). Klasse E; trifft jede Einheit in jedem Tick.
2. **Waffentakt zur Laufzeit** – `WeaponInfo.ScaledByRateOfFire` und `PreAttackDelayWith` (`Sim\Templates/WeaponCatalog.cs`, Zeilen 80–118) schneiden `Ticks / Multiplikator` zur Laufzeit ab; `WeaponCatalog.Build` setzt fehlende oder 0-ms-Schussabstände auf einen Tick (`Math.Max(SleepNone, delay)`); `SimulationWorld.FlightTicks` (`Sim\World/SimulationWorld.Projectiles.cs`, Zeile 125) trifft „sofort“ unter einem Tick Weg. Auf den Realdaten betrifft die Ein-Tick-Untergrenze 118 von 363 Waffen (§4.4). **Das widerspricht der Planaussage, die Balance sei bei jeder Rate dieselbe (§0.3 W1).**
3. **Geschosse** – `DumbProjectileBehavior.CalcFlightPath`/`Update` (ein Bahnpunkt je Tick, Detonation einen Tick nach dem letzten Punkt), `MissileAIUpdate.Launch` (Waffengeschwindigkeit je Tick als Beschleunigung eingesetzt) und `Move` (Ankunft bei Restweg ≤ ein Tick Flug) in `Mod\ProjectileModules.cs`; `NeutronMissileUpdate` (`Mod\AirWeaponModules.cs`, `ForwardDamping`/`RelativeSpeed` roh je Tick); `SmartBombTargetHomingUpdate` (`Mod\LastBlockerModules.cs`, Positionsmischung mit `CourseCorrectionScalar` 0,99 je Tick).
4. **Skript-VM** – `ScriptEngine.Update` wertet jedes nicht verzögerte Skript jeden Tick aus (Zähler, die ein Skript je Bild erhöht, laufen bei 60 Hz doppelt so schnell); `SET_TIMER`/`SET_RANDOM_TIMER` (`Sim\Scripting/ScriptEngine.Actions.cs`, Zeilen 50–59) und `*_FOR_FRAMECOUNT`, `teamInitialIdleFrames`, `CAMERA_FADE_*`/`CAMERA_BW_MODE_*` verbrauchen Bildzahlen der Karte roh; `SecondsToFrames` rechnet zur Laufzeit mit der Konstante; die erste Auswertung verzögerter Skripte hängt an `NameHash % (2 * 30 + 1)` (`ScriptEngine.cs`, Zeile 178). Klassen C, D und E; betrifft jede Kampagnenkarte.
5. **Effekte am Simulationstick** – `LiveGame.SyncAfterTick` (`Game\Live/LiveGame.cs`, Zeilen 203–212) ruft `FxExecutor.ExecuteEvents`/`Update` und damit `ParticleWorld.Step` genau einmal je Tick; die Partikeldaten sind je Bild verfasst (multiplikative Dämpfung, Schwerkraft je Bild², `BurstDelay` 0 = ein Ausstoß je Schritt). Bei 60 Hz läuft jeder Effekt doppelt so schnell. Klasse F/E.
6. **Türme** – `SimulationWorld.Turrets.cs`: `TurnAngle`/`TurnPitch`/`AimStep`/`IsAligned` (Einrasten innerhalb eines Tick-Schritts, Feuerfreigabe gegen feste 0,035 rad), dazu die eigenen Literale `TurretAimGraceTicks = 15`, `TurretReloadHoldCapTicks = 450`, `TurretLoseInterestTicks = 45` und die Originalkonstante `TurretSweepEnableTicks = 3`; `TurretInfo.DefaultRate = 0.01` rad je Bild für Türme ohne eigene Rate.
7. **Rohdaten je Bild mit multiplikativer oder gemischter Wirkung** – `RailroadBehavior` (`Mod\RailroadModules.cs`: `SpeedMax`, `Acceleration 1.01`, `Braking 0.99`, `Friction 0.97`, Literale 0,02/0,05/0,1, Schaden `Speed * 10` je Tick), `TensileFormationUpdate` (`Mod\FieldEffectModules.cs`: Lebensdauer 300, Reibung 0,95, 0,93/0,07 je Tick), `HelicopterSlowDeathBehavior` (`Mod\TransportModules.cs`: `SpiralOrbitForwardSpeedDamping`), `StructureToppleUpdate`/`ToppleUpdate` (`Mod\ToppleAndFallModules.cs`: `StructuralDecay`, `InitialVelocityPercent`, ein Waffenschuss je Tick in `ApplyCrushingDamage`), `BattleBusSlowDeathBehavior` (`Mod\VehicleModules.cs`, Zeile 178: `ThrowForce / mass` als Geschwindigkeit je Tick – unverändert fliegt der Bus bei 60 Hz rund viermal so hoch). Wurzelziehen allein genügt für die gemischten Rekursionen nicht (§0.3 W4).
8. **Zähler mit Periode N + 1, Zufall über Tickbereiche, tick-gehashte Phasen** – „auf N setzen, je Tick −1, im Tick nach 0 handeln“ in `PointDefenseLaserUpdate` (Suche und Schuss), `AutoFindHealingUpdate`, dem Hacker-Geldzyklus, `DemoTrapUpdate`, `HordeUpdate`, `DynamicShroudClearingRangeUpdate`, `EnemyNearUpdate`: mit skaliertem N wird die Periode 2N + 1 statt 2N + 2. Dauern werden zur Laufzeit mit `LogicRandom.Range(minTicks, maxTicks)` über Tickgrenzen gezogen (`SlavedUpdate`, `StructureCollapseUpdate`, `FireSpreadUpdate`, `SlowDeathBehavior`, `LifetimeUpdate` …): gezogene Werte liegen dann nicht mehr auf dem 30er-Raster. `SimulationWorld.MoodCheckOffset` (`Sim\World/SimulationWorld.Acquisition.cs`, Zeile 236) und der Leerlauf-Schwenk der Türme hashen die Ticknummer.
9. **Die Präsentation erzeugt Befehle im Ticktakt** – `StandingOrders.Update`/`UpdateAttack` (`Pres\Input/StandingOrders.cs`, Zeilen 77–152) läuft über `GameInputController.Tick` ← `LiveGame.BeforeTick` (`Game\Hud/MatchController.cs`, Zeile 114) vor **jedem** Simulationstick und reicht `FireWeaponCommand`, `PursueCommand` und `SetMoveTargetCommand` ein (ein Feuerbefehl je Tick, in dem die Einheit feuern darf); `IssuedTick + 1` unterstellt „wirkt einen Tick später“. Dazu der Missions-Director: `DurationTicks`, `DefaultPlaceholderTicks`, `ClientPresentationLatencyTicks` (`Game\Shell/MissionPresentation.cs`, `Game\Live/LiveMission.cs`) speisen `CompletePresentation` und damit Skriptbedingungen. Der Rhythmus ändert Ergebnisse und den Replay-Inhalt, nicht nur die Last (§0.3 W5).
10. **Eigene Rhythmen, die Ergebnisse bestimmen** – `SkirmishAi` (`Sim\Skirmish/SkirmishAi.cs`: `ThinkInterval = 15`, `FireInterval = 5`, sieben benannte Fristen und 13 Literale im Rumpf, alle nur auf Denkticks geprüft; `< 450` und `< 60` mit drei Fehlversuchen ändern das Verhalten dauerhaft), `SimulationWorld.VisionIntervalTicks = 3` mit `tick % 3` in `Step` (auch in `LiveGame`, Zeile 211), `ScriptEngine.RetargetInterval = 30`, und die Fahrer der Referenzläufe in `Sim\Testing` (`MoveCadence = 15`, `IncomeInterval = 300`, `spacing = 15` des Zielfahrers mit 34 Abklingzeiten in Ticks): Referenzmatch, Stresstest und `drive` ändern sich bei 60 Hz auch dann, wenn die Regeln stimmen.

### 0.3 Widersprüche und Lücken gegenüber `MULTIPLAYER-PLAN.md` §2.5 und den Handoffs 20/21/23

- **W1 – „Zeiten aus den Daten bleiben auf dem 30-Hz-Raster, die Balance ist bei jeder Rate dieselbe“ (Plan §2.5, Handoff 20 Entscheidung 2, Handoff 23 Toleranz „Zeiten aus Daten exakt gleich“) gilt nur für die Katalogrundung.** Nicht abgedeckt sind (a) Rundungen zur Laufzeit in Ticks der laufenden Rate: `WeaponInfo.ScaledByRateOfFire` (`floor(Ticks / Multiplikator)`, im Original `REAL_TO_INT_FLOOR` in `WeaponTemplate::getDelayBetweenShots`), `PreAttackDelayWith`, `HealContain` (`MaxHealth / Ticks`), Blast-Verzögerungen mit ungerundeten Ticks; (b) Untergrenzen „mindestens ein Tick“ (`Math.Max(…, SleepNone)`: Schussabstand, `AutoHealBehavior`, `AutoDepositUpdate`, `FlammableUpdate`, `SlowDeathBehavior` …); (c) Zähler mit Periode N + 1 (Rang 8); (d) Dauern, die zur Laufzeit über Tickbereiche gewürfelt werden. Für alle vier muss Phase 20 festlegen, dass in **Originalbildern** gerechnet und erst das Ergebnis mit Rate/30 multipliziert wird – sonst verschieben sich Feuerraten mit Bonus, Heil- und Schadensraten. Ob und wie stark Feuerraten-Boni auf den Realdaten abweichen, ist nicht gemessen.
- **W2 – „Kein nacktes Tickliteral bleibt in der Simulation“ (Handoff 20 Entscheidung 4, Punkt 7 `SourceRules`) braucht eine vierte Kategorie: Tick-Nachbarschaft.** Mehrere Literale meinen keine Zeit, sondern Reihenfolge („im selben Tick“, „einen Tick später“): `UNIT_EMPTIED` (`frame - 1`), `_transferTick = now + 1 / + 2` in `JetAIUpdate`, `FramesUnpulledLongEnoughToUnhitch = 2`, `StandingOrders` `IssuedTick + 1`, `StealthUpdate` `LastFireTick == world.Tick`, `ConditionDeriver` `lastFire == world.Tick - 1`, die Quick-Victory-Verzögerung 1, das „Wagen existieren erst im nächsten Tick“ des Zugs. Sie dürfen **nicht** skaliert werden. Sie stehen in den Tabellen als Klasse E mit dem Vermerk „Reihenfolge“.
- **W3 – Kompilierzeit-Konstanten.** 13 `const`-Ausdrücke und ein `static readonly` rechnen mit `LogicFramesPerSecond` (`DefaultMoodCheckTicks`, `DefaultMaxLifespanTicks`, `DockMachine.ClearanceTimeoutTicks`, `SlavedUpdate.UpdateRate`/`RecentFireTicks`, `SpawnBehavior.UpdateRate`, `ParkingPlaceBehavior.HealRateTicks`, `PreAttackStaleTicks`, `SupplyScanFrames`, `HelicopterSlowDeathData.FramesPerSecond`; außerhalb der Simulation `LiveGame.TickSeconds`, `LiveMission.ClientPresentationLatencyTicks`, `MissionPresentation.DefaultPlaceholderTicks`/`RadarBlipLifetimeTicks`). Sie sind in die Assemblies einkompiliert, auch über Assemblygrenzen; Handoff 20 nennt nur den Katalog. Dazu rechnen 31 weitere Zeilen der Simulation von Hand mit der Konstante statt über die Umrechnungen von `SimulationTiming`: 19 Divisionen `/ Fixed.FromInt(LogicFramesPerSecond)` in Modulen (ein Teil davon in jedem Tick zur Laufzeit) und 12 Additionen, Multiplikationen und Modulo in Modulen, Skript-VM, Turmkatalog und Feuerstufen; `TemplateCatalog.GravityPerTickSquared` rechnet bei jedem Zugriff.
- **W4 – „Multiplikative Faktoren brauchen nur Quadratwurzeln“ (Plan §2.5, Handoff 23 Schritt 3) stimmt nur für reine Faktoren.** Gemischte Rekursionen – Zug (`Speed = (Speed + 0.02) * 1.01`), Zugformation (Mischung 0,93/0,07 plus Reibung plus Euler), MOAB-Zielsuche (Positionsmischung 0,99), Hubschrauber-Spirale, Partikel (Dämpfung und Schwerkraft im selben Schritt) – ergeben mit der Wurzel des Faktors nicht dieselbe Bahn. Hier bleibt die geschlossene Form oder der Ausweg „Originalraster“. Die Faktoren kommen teils aus den Daten (`ForwardDamping`, `SpiralOrbitForwardSpeedDamping`, `CollapseDamping`, `StructuralDecay`, `VelocityDamping` der Partikel) und sind in `CnCNew.Data` nur als `Real` typisiert.
- **W5 – „Die Präsentation ändert die Simulation nicht“ (Handoff 21) trifft heute nicht zu.** `StandingOrders` (Projekt `CnCNew.Presentation`) reicht im Ticktakt Befehle ein (Rang 9); der Missions-Director meldet Abschlüsse an die Skript-VM (im Plan als E21 bekannt). Phase 21 kann diese beiden nicht „bildgleich“ umstellen, ohne Simulationsergebnisse zu berühren; sie gehören in Phase 20 (Rhythmus in Originalbildern) bzw. Phase 24 (Befehlsweg). Für den Netzpfad heißt das außerdem: ein angreifender Trupp erzeugt heute bis zu einen Befehl je Einheit und Tick.
- **W6 – Skripte: mehr als die Bildzähler.** Handoff 20 nennt `SET_TIMER` und `*_FOR_FRAMECOUNT`. Zusätzlich gefunden: die Auswertung selbst (jedes Skript jeden Tick, „Originalraster“ wäre hier der naheliegende Weg), `teamInitialIdleFrames` (`ScriptProgram.cs`), die an die Präsentation durchgereichten Bildzahlen (`CAMERA_FADE_*`, `CAMERA_BW_MODE_*`), `SecondsToFrames` als eigene Laufzeit-Umrechnung neben `SimulationTiming`, der Phasenversatz `% (2 * Rate + 1)`, `ProcessTeamBuilds` (ein Warteschlangenbefehl je Auftrag und Tick) und `SupplyScanFrames` (10 s, obwohl das Original `SCAN_RATE = 10` in Bildern addiert).
- **W7 – „Genau eine benannte Konstante für das Originalbild“ (Handoff 21) – heute sind es mindestens acht unabhängige 30er:** `FxValueReader.FramesPerSecond`, `RtsCamera.FrameRate`, `CameraSettings.FramesPerSecond`, `OrderHintBook.ClientFramesPerSecond`, `WindowTransitionHandler.FramesPerSecond`, `InGameUiSettings.LogicFramesPerSecond`, `Suspension.LogicTicksPerSecond`, `TransitionScenes.Frame`; dazu Literale (`3 * 30`, `30 * 10`, `0.002f * 30f`, `deltaSeconds * 30.0`, `LetterboxFadeFrames = 30`, drei `End = 30`). Zwei davon sind in Wahrheit Simulationsticks (`InGameUiSettings.LogicFramesPerSecond` über `MissionText`/`SuperweaponTimers`, `Suspension.LogicTicksPerSecond`), die übrigen sind richtige Originalbild-Einheiten auf Wandzeit und dürfen **nicht** an die Tickrate gehängt werden (in §6 als F/originalbild geführt).
- **W8 – Formate.** Handoff 23 Schritt 8 nennt `maxTicks`, `presentationLatencyTicks`, `combatIntervalTicks`. `MissionOptions.CombatIntervalTicks` ist nur noch ein Schalter (nicht skalieren). Nicht genannt: `SaveGame` (`CnCNew.Game`, Version 1) schreibt und prüft **keine** Rate und zeigt den rohen Tick in der Ladeliste; der Replay-Kopf trägt `EndTick` und `CheckpointInterval` in Ticks; `ReplayRunner.Run(replay, world, options)` prüft die Rate gar nicht; Befehle serialisieren `tick`, `delay` und `repeat`; Katalogwerte in Ticks werden in den Zustand kopiert (`runDropDelay`, `healthPerTick`, Geschwindigkeiten je Tick – 39 Felder „je Tick“, Anhang C), ein gespeicherter Stand ist also nur mit seiner Rate lesbar. Der Code steht bei Replay-Format **11**; `CLAUDE.md` nennt noch 10.
- **W9 – Tests und Werkzeuge laufen bei 60 Hz weiter, prüfen aber etwas anderes.** Tests und Werkzeuge schreiten durchweg mit dem Literal `1.0 / 30` (103 Vorkommen in 18 Dateien; `ScenarioAudit` mit `1.0 / 30 + 1e-6`); weil `LiveGame.Advance` Sekunden sammelt, laufen dann zwei Ticks je Schritt, und Annahmen „ein Schritt = ein Tick“ (`ScenarioAudit`, HUD- und Missionstests) werden still falsch statt rot. Phase 21 sollte den Schritt aus der Rate ableiten, **bevor** die Rate umgestellt wird.
- **W10 – Zahlen im Register (Plan §4).** E5: 61 → 63 Verwendungen der Konstante in der Simulation, „rund 110 nackte Tickzahlen“ → 159 in der Simulation ohne `Testing` plus 17 Rhythmen und 65 Fahrerwerte in `Sim\Testing`. E10: „rund 845 Testzeilen“ → 1002 Stellen in 82 von 119 Testdateien. Zustandsfelder „rund 111 in 41 Dateien“ → 222 in 56 Dateien.

### 0.4 Nebenbefunde (unabhängig von der Rate, beim Lesen aufgefallen, nicht weiterverfolgt)

Abweichungen vom Original, die die Raten-Äquivalenz-Suite der Phase 23 als Fehlschläge zeigen wird, obwohl sie keine Ratenfehler sind: `StealthData.MoveThresholdSpeed` wird roh gelesen und mit der Geschwindigkeit je Tick verglichen (Original: `parseVelocityReal`); `BridgeData` fällt auf 1 je Sekunde zurück (Original 1,0 je Bild); `AutoHealBehavior` fällt bei fehlendem `HealingDelay` auf 0 zurück (Original `UINT_MAX`; auf den Realdaten hat jedes der 848 Module den Schlüssel); `GrantStealthData.RadiusGrowRate` und `StructureToppleData.StructuralDecay` haben andere Standardwerte als die Konstruktoren des Originals; `ForceIdleTicks = 2` für 67 ms, wo die Aufrundung des Originals 3 Bilder ergäbe; `SUB_FROM_MSEC_TIMER` rundet anders als das Original; `SUPPLY_SOURCE_SAFE` ohne den 2-s-Zwischenspeicher des Originals; `GroupDoubleTapMilliseconds = 500` gegen 20 Logikbilder im Original; der Klassenkommentar von `SimulationWorld` nennt 8 Phasen, `Step` führt mehr aus (Zielerfassung, Türme, Feuerstufen, Verkäufe, verzögerter Schaden und Sicht fehlen dort). Jede dieser Aussagen stammt aus einem Lesedurchgang und ist vor einer Korrektur am Referenzquelltext zu bestätigen.

## 1. Methode, Zählregeln, Grenzen

### 1.1 Klassen

| Kl. | Bedeutung | Unterart (Spalte „Kl.“ in den Tabellen) | Wer |
|---|---|---|---|
| A | Originalkonstante in Bildern: ein Wert, den das Original in Logikbildern festlegt; zu skalieren mit Rate/30. Herkunft mit Datei und Funktion/Konstante, wo im Referenzquelltext gefunden, sonst „Herkunft unbelegt“ | – | 20 |
| B | Zeitwert | `daten` = Millisekunden/Sekunden aus den Daten, umgerechnet (einzeln nur, wo auffällig; sonst mechanisch gezählt, §2); `fallback` = Ersatzwert in Ticks für ein Datenfeld; `literal` = eigene Dauer, direkt als Tickzahl geschrieben | 20 |
| C | Ablaufrhythmus: wie oft etwas läuft (Denk- und Suchintervalle, `% N`, Staffelung nach ID, Abfragen je Tick). In der Spalte „Bei 60 Hz“ steht, ob das Ergebnisse ändert oder nur Last | – | 20 |
| D | rohe Daten je Bild: Felder oder Standardwerte, die ohne Umrechnung „je Bild“ verbraucht werden | – | 20, 23 |
| E | nicht invariante Integration: Euler-Schritte je Tick, Schwellen gegen einen Tick-Schritt, multiplikative Dämpfung je Tick, Ankunftsradius „ein Tick Weg“, Reihenfolge, „ein Tick später“ | – | 23 |
| F | Präsentationsuhr außerhalb der Simulation | `tick` = am Simulationstick; `wand` = an Wandzeit, aber mit 30er-Konstante; `originalbild` = feste 1/30-s-Einheit, richtig mit Wandzeit verrechnet (nicht an die Tickrate hängen) | 21 |
| G | Format: Rate oder Tickwerte in Snapshot, Replay, Missionsstand, Speicherstand | – | 20, 23 |
| H | Test oder Werkzeug | `schritt` (fester Schritt `1.0/30`), `zeit` (Tickzahl, die eine Zeit meint), `ticks` (echte Ticksemantik, bleibt), `budget` (Lauflänge), `rate` (benutzt oder prüft die Rate selbst), `golden`, `ausgabe` (Tick → Sekunden mit 30) | 21, 23 |

### 1.2 Zählregeln

- **Eine Stelle** = eine Deklaration (Konstante/Feld mit Tickbedeutung) **oder** eine Anweisung mit einem Tickliteral **oder** ein zusammenhängender Integrationsblock (Klasse E, Zeilenbereich) **oder** ein rohes Datenfeld. Verwendungen einer benannten Konstante zählen nicht noch einmal.
- **Mechanisch gezählt** (nicht als Zeile in §5): schlichte Umrechnungen beim Parsen, also `ModuleConfigReader.Ticks(cfg, "Key", 0)` und Aufrufe der `SimulationTiming`-Umrechnungen mit Datenwert. Sie stehen je Datei in §2 und §3.2 und in Spalte B der Tabelle §0.1. Wo eine Umrechnung auffällig ist (Ersatzwert ≠ 0, Laufzeit statt Parsezeit, andere Rundung, Rechnen mit dem Ergebnis), hat sie eine eigene Zeile.
- **Tests:** eine Zeile der Tabellen in §7 fasst Trefferzeilen derselben Datei und derselben Unterart zusammen; gezählt wird die Zahl am Anfang der Beschreibung („12 Stellen: …“), sonst 1.
- **Zustandsfelder** (Schlüssel, die über `IStateWriter` geschrieben werden) stehen nicht in den Klassentabellen, sondern in §4.3 und Anhang C.

### 1.3 Vorgehen und Grenzen

1. Suchlauf mit 33 Mustern (§8) über `src`, `tests`, `tools` (nur `*.cs`, ohne `bin`, `obj`, `.claude/worktrees`): 7.679 Trefferzeilen in 34 Projektverzeichnissen.
2. Aufteilung in 29 Pakete. **Vollständig gelesen** (jede Datei ganz, Treffer nur als Hinweise): `CnCNew.Simulation`, `CnCNew.Game`, `CnCNew.Presentation` – in der Simulation mit diesen Ausnahmen: die Katalog- und Formatdateien (`Sim\Templates/*`, `Sim\Replay/*`, `Sim\Snapshots/WorldSnapshot.cs`) sind in den Abschnitten mit Zeit- und Ratenbezug und an jeder Trefferzeile gelesen, `Sim\Numerics`, `Sim\State/IStateIO.cs` und einzelne Katalogdateien nur an den Trefferzeilen, und Dateien ohne Treffer (`Sim\Ids`, `Sim\Random`, `Sim\State/JsonState.cs`, vier Kataloge ohne Zeitfelder) gar nicht. **Treffergetrieben** (jede Trefferzeile der starken Muster mit der umgebenden Methode; jede Trefferzeile ist einer Tabellenzeile oder einer Gruppe in Anhang A zugeordnet): `CnCNew.Ui`, `CnCNew.Audio`, `CnCNew.Client.Stride`, `CnCNew.Data`, `CnCNew.W3D` und die kleinen Projekte, `tools`, `tests`. Das Lesen der Pakete haben getrennte Lesedurchgänge mit festem Ausgabeformat erledigt; der Koordinator hat `Sim\Time`, die Kataloge in `Sim\Templates`, `Sim\Replay`, `Sim\Snapshots`, `ModuleConfigReader`, die Golden-Dateien und die Tests zur Ratenablehnung selbst gelesen, die zehn Stellen aus §0.2 und Stichproben aus jedem Paket am Quelltext und am Referenzquelltext nachgeprüft und alle Zählungen mit Skripten aus den Ergebnisdateien erzeugt (Zeilenangaben gegen die Dateien geprüft).
3. Lesestand je Projekt (Dateien):

| Projekt | Dateien | vollständig gelesen | teilweise (Methoden um die Treffer) | nicht gelesen (kein Treffer der starken Muster) |
|---|---|---|---|---|
| `CnCNew.Simulation` | 136 | 112 | 16 | 8 |
| `CnCNew.Game` | 58 | 58 | 0 | 0 |
| `CnCNew.Presentation` | 71 | 71 | 0 | 0 |
| `CnCNew.Ui` | 48 | 12 | 14 | 22 |
| `CnCNew.Client.Stride` | 50 | 18 | 15 | 17 |
| `CnCNew.Audio` | 21 | 10 | 2 | 9 |
| `CnCNew.Data` | 29 | 5 | 7 | 17 |
| übrige src | 45 | 6 | 16 | 23 |
| tools | 24 | 12 | 11 | 1 |
| tests | 119 | 14 | 98 | 7 |

**Grenzen:** (a) Herkunftsangaben sind nur so weit belegt, wie sie in der Spalte stehen – meist wurde die Konstante oder die Zeile im Referenzquelltext gesucht, nicht die ganze Funktion gelesen; „Herkunft unbelegt“ heißt: nicht gefunden oder nicht gesucht. (b) In treffergetriebenen Paketen kann Kopplung fehlen, die kein Muster trifft; in den Tests ist das sicher so (die Lesedurchgänge haben über die Treffer hinaus Stellen ergänzt, aber nicht jede Schleifengrenze aufgezählt). (c) Die „Bei 60 Hz“-Aussagen sind Folgerungen aus dem Code. (d) Dateien außerhalb von `*.cs` (Skripte in `scripts/`, Projektdateien, Doku) sind nicht Gegenstand; die Capture- und Demo-Skripte mit `--frames` sind nicht geprüft.

## 2. Die zentrale Konstante und die Umrechnungen

`SimulationTiming.LogicFramesPerSecond = 30` (`Sim\Time/SimulationTiming.cs`, Zeile 21; Original `LOGICFRAMES_PER_SECOND = 30`, `GameCommon.h` Zeile 72). Umrechnungen dort: `DurationMillisecondsToTicks` (aufgerundet, wie `INI::parseDurationUnsignedInt`), `DurationMillisecondsToFixedTicks` (ungerundet, wie `parseDurationReal`), `SecondsToTicks` (aufgerundet), `PerSecondToPerTick` (÷ 30), `PerSecondSquaredToPerTickSquared` (÷ 900), `DegreesPerSecondToRadiansPerTick`. Nicht ratenabhängig: `DegreesToRadians`, `RatioToFixed`, `MillisecondsPerSecond`, `SleepNone = 1`, `SleepForever`.

**Verwendungen der Konstante (Vorkommen des Bezeichners, gezählt):**

| Projekt | alt (Startinventar) | neu | Dateien | Anmerkung |
|---|---|---|---|---|
| `CnCNew.Simulation` | 61 (davon 8 in `SimulationTiming.cs`) in 32 | **63** (davon 9 in `SimulationTiming.cs`) | 32 | 54 qualifizierte Verwendungen außerhalb der Datei, 9 in der Datei selbst |
| `CnCNew.Game` | 14 (4 über die Kopie in `CnCNew.Ui`) in 6 | **14** | 6 | 10 × `SimulationTiming.…`, 4 × `InGameUiSettings.LogicFramesPerSecond` (`MissionText.cs`) |
| `CnCNew.Presentation` | 1 | **1** | 1 | `SelectionFlash.cs` |
| `CnCNew.Ui` | 0 | **5** | 1 | eigene Konstante `InGameUiSettings.LogicFramesPerSecond = 30` (Definition und 4 Verwendungen in der Datei); im Startinventar unter „Kopien“ geführt |
| `CnCNew.Client.Stride`, `CnCNew.Audio`, `CnCNew.Data`, übrige, Werkzeuge | 0 | **0** | – |  |
| Tests | 4 in 4 | **4** | 4 | `Tests.Basics.cs`, `Tests.Collision.cs`, `InputTests.cs`, `MissionPresentationTests.cs` |

**Aufrufe der Umrechnungen in `src` (ohne die Definitionen selbst):** 173 Aufrufe in 41 Dateien; davon haben 23 eine eigene Zeile in §5, 150 sind schlichte Umrechnungen beim Katalogbau (mechanisch gezählt). `ModuleConfigReader.Ticks`: **132** Aufrufe in 33 Dateien, 117 mit Ersatzwert 0, **15 mit Ersatzwert ≠ 0 in 10 Dateien** (alt: 10 genannt) – diese 15 stehen einzeln in §5 (`Lifetime 1`, `Delay 1`, `CrashThroughBunkerFXFrequency 4`, `HowitzerFiringRate 10`, `DelayBetweenUpdates 100`, `CreatorDeathCheckRate 30`, `UpdateRate 30`, `DetectionRate 1`, `MoodAttackCheckRate 60`, `MaxLifespan 300`, `KillSelfDelay 3`, `WaitAtStationTime 150`, `MinBurstDelay 9999`, `OCLLifetimeMaxCap 1000`, `ScanDelayTime 30`).

**Rundung und Zeitpunkt:** Alle Umrechnungen sind statische Funktionen ohne Ratenparameter. Dauern werden aufgerundet (ganzzahlig bzw. in `decimal`, exakt), Geschwindigkeiten, Beschleunigungen und Winkelgeschwindigkeiten auf `Fixed` zum nächsten Wert. Die Umrechnung geschieht **einmal beim Bau des Katalogs** (`TemplateCatalog.Build` → `binding.ParseData`, `WeaponCatalog.Build`, `BuildLocomotors`, `UpgradeCatalog.Build`, `SpecialPowerCatalog.Build`, `ObjectCreationListCatalog.Build`, `TurretInfo.Parse`); die geparsten Moduldaten sind damit ratenabhängig. Ausnahmen, die zur Laufzeit rechnen, stehen in §5 (Unterart `daten` mit Vermerk „zur Laufzeit“) – unter anderem die Skript-VM (`SecondsToFrames`), `TemplateCatalog.GravityPerTickSquared`, die Divisionen `… / LogicFramesPerSecond` in Modul-Updates. `CnCNew.Data` speichert Dauern als Millisekunden (`GameTime`) und kennt keinen Feldtyp für Bilder; Werte je Sekunde und `BuildTime` in Sekunden liegen als rohe Zahl vor.

**Ganzzahlige Teilung der Rate** (alt 6, neu 6 – unverändert, alle nachgelesen): `SlavedUpdate.UpdateRate = 30 / 4` (7 Ticks; 60 / 4 = 15, nicht 14), `ParkingPlaceBehavior.HealRateTicks = 30 / 5`, `SpawnBehavior.UpdateRate = 30 / 2`, `WaveGuideUpdate` `30 / 2`, `InGameUiSettings` `FloatingTextTimeOut` `30 / 3`, `ScriptEngine` `% (2 * 30 + 1)`. Nur die erste ist bei 30 Hz nicht ganzzahlig.

## 3. Zählung

### 3.1 Je Klasse und Projekt

Siehe Tabelle in §0.1. Unterarten:

| Kl. | Unterart | src | tools | tests |
|---|---|---|---|---|
| B | daten | 219 | 0 | 0 |
| B | fallback | 21 | 0 | 0 |
| B | literal | 53 | 0 | 0 |
| F | originalbild | 32 | 0 | 0 |
| F | tick | 74 | 0 | 0 |
| F | wand | 3 | 0 | 0 |
| H | ausgabe | 1 | 3 | 2 |
| H | budget | 9 | 18 | 311 |
| H | golden | 1 | 2 | 2 |
| H | rate | 2 | 0 | 94 |
| H | schritt | 2 | 5 | 95 |
| H | ticks | 11 | 9 | 120 |
| H | zeit | 52 | 18 | 378 |

B/`daten` in `src` enthält die 150 mechanisch gezählten Umrechnungen. Die 14 D-Stellen unter „tests“ in §0.1 sind Fixture-Werte je Bild in Testdateien (z. B. `StructuralDecay`, `ForwardDamping`, Zugwerte), die von der Entscheidung für die Simulation abhängen.

**Alt → neu (Startinventar gegen diese Zählung):**

| Größe | alt | neu |
|---|---|---|
| Kopien der Rate und feste Schritte `1.0 / 30` | rund 105 Stellen, davon 97 `1.0 / 30` in 15 Testdateien | 103 Vorkommen von `1.0 / 30` bzw. `1f / 30` in 18 Dateien: Tests 95 in 11 (`Game.Tests` 68 in 8, `Presentation.Tests` 27 in 3), `src` 3 in 3, Werkzeuge 5 in 4 |
| Nackte Tickzahlen in der Simulation (A, B/`literal`, B/`fallback`, C) | rund 80 und rund 30 in `Sim\Testing` | 159 ohne `Sim\Testing`; in `Sim\Testing` 17 Rhythmen plus 65 Fahrer-/Budgetwerte (Klasse H) |
| … in `CnCNew.Game` / `CnCNew.Presentation` | je rund 15 | 26 / 11 (dazu die Uhren der Klasse F: 30 / 44) |
| Ersatzwerte ≠ 0 von `ModuleConfigReader.Ticks` | 10 | 15 in 10 Dateien |
| Zustandsfelder mit absoluten Ticks | rund 111 in 41 Dateien | 222 in 56 Dateien (dazu 26 Restzähler, 23 Dauern, 39 Größen je Tick) |
| Tickerwartungen in Tests | rund 845 Zeilen in 84 Testdateien | 1002 Stellen in 82 von 119 Testdateien: zeit 378, budget 311, ticks 120, schritt 95, rate 94, ausgabe 2, golden 2 |
| Sechzehnstellige Hex-Werte in der Doku | 137 in 26 Dateien, 79 in `SIMULATION-CORE.md` | 458 in 29 Dateien (`docs/*.md`, `CLAUDE.md`, `README.md`), 189 in `SIMULATION-CORE.md` – die Doku ist seit dem Startinventar gewachsen; im Testquelltext weiterhin kein gepinnter Zustands-Hash |

### 3.2 Simulation je Datei

| Datei | A | B | B mech. | C | D | E | F | G | H | Summe |
|---|---|---|---|---|---|---|---|---|---|---|
| `Sim\Commands/Commands.cs` |  |  |  |  |  |  |  | 2 |  | 2 |
| `Sim\Events/SimEvent.cs` |  |  |  |  |  |  | 2 | 1 |  | 3 |
| `Sim\Mission/MissionSession.cs` |  | 1 |  |  |  | 1 | 2 |  |  | 4 |
| `Sim\Mission/MissionSnapshot.cs` |  |  |  |  |  |  |  | 3 |  | 3 |
| `Mod\AirModules.cs` | 1 | 2 |  | 1 |  |  |  |  |  | 4 |
| `Mod\AirWeaponModules.cs` | 1 | 8 | 9 | 2 | 4 | 7 |  |  |  | 31 |
| `Mod\BattlePlanModule.cs` |  |  | 5 |  |  | 1 |  |  |  | 6 |
| `Mod\Bodies.cs` |  |  | 1 |  |  |  |  |  |  | 1 |
| `Mod\BridgeModules.cs` |  |  | 1 |  | 4 | 1 |  |  |  | 6 |
| `Mod\CleanupAndSpyModules.cs` | 1 | 1 | 6 | 2 |  |  |  |  |  | 10 |
| `Mod\CombatModules.cs` |  |  | 2 | 1 |  |  |  |  |  | 3 |
| `Mod\ContainChainModules.cs` |  |  | 1 | 1 |  | 1 |  |  |  | 3 |
| `Mod\ContainModules.cs` |  |  | 1 |  |  | 2 |  |  |  | 3 |
| `Mod\CrateModules.cs` |  | 2 | 2 | 1 |  |  |  |  |  | 5 |
| `Mod\DieAndUpdateModules.cs` |  | 1 | 2 |  |  |  |  |  |  | 3 |
| `Mod\DockMachine.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Mod\DockModules.cs` |  |  | 1 | 2 |  |  |  |  |  | 3 |
| `Mod\FerryModules.cs` |  |  | 2 |  |  | 4 |  |  |  | 6 |
| `Mod\FieldEffectModules.cs` | 3 | 1 |  |  | 3 | 2 |  |  |  | 9 |
| `Mod\FlightModules.cs` |  | 12 | 15 | 2 |  | 5 |  |  |  | 34 |
| `Mod\HackerAndAssaultModules.cs` |  | 1 | 4 | 1 |  | 1 |  |  |  | 7 |
| `Mod\JetAIUpdate.Flight.cs` |  |  |  |  |  | 1 |  |  |  | 1 |
| `Mod\JetAIUpdate.cs` | 3 | 3 | 3 | 1 | 2 | 9 |  |  |  | 21 |
| `Mod\LastBlockerModules.cs` |  |  | 2 |  | 1 | 2 | 1 |  |  | 6 |
| `Mod\MatchModules.cs` | 1 | 1 |  |  |  |  |  |  |  | 2 |
| `Mod\MineModules.cs` |  | 2 | 1 | 1 |  |  |  |  |  | 4 |
| `Mod\ModuleConfigReader.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Mod\Phase12Modules.cs` |  | 1 |  | 2 |  | 2 | 1 |  |  | 6 |
| `Mod\Phase5BlockerModules.cs` |  | 3 | 10 | 1 |  | 2 |  |  |  | 16 |
| `Mod\Phase5Modules.cs` | 1 | 2 | 8 | 2 | 1 | 8 |  |  |  | 22 |
| `Mod\PhysicsAndMinefieldModules.cs` |  |  |  | 1 |  | 1 |  |  |  | 2 |
| `Mod\ProductionExitModules.cs` |  |  | 1 |  |  |  |  |  |  | 1 |
| `Mod\ProjectileModules.cs` | 3 | 3 | 4 | 1 |  | 6 |  |  |  | 17 |
| `Mod\RailroadModules.cs` | 1 | 1 |  | 1 | 10 | 4 |  |  |  | 17 |
| `Mod\RepairModules.cs` |  | 2 | 1 | 1 |  | 1 |  |  |  | 5 |
| `Mod\RetailMatchModules.cs` |  | 3 | 6 |  |  | 1 |  |  |  | 10 |
| `Mod\SlaveModules.cs` | 7 | 4 | 4 | 1 | 1 | 6 |  |  |  | 23 |
| `Mod\SpawnModules.cs` | 1 |  | 1 | 2 |  |  |  |  |  | 4 |
| `Mod\SpecialAbilityStaging.cs` |  | 1 | 3 |  |  | 2 | 1 |  |  | 7 |
| `Mod\SpecialPowerModules.cs` |  |  |  |  |  | 2 | 1 |  |  | 3 |
| `Mod\SpecialsModules.cs` |  |  |  | 2 | 1 | 2 |  |  |  | 5 |
| `Mod\StructureModules.cs` |  | 4 | 6 | 1 |  | 1 |  |  |  | 12 |
| `Mod\SupplyModules.cs` |  |  | 2 | 1 |  |  |  |  |  | 3 |
| `Mod\ToppleAndFallModules.cs` | 2 | 1 | 3 |  | 3 | 5 |  |  |  | 14 |
| `Mod\TransportModules.cs` | 1 | 5 |  | 1 | 2 | 7 |  |  |  | 16 |
| `Mod\VehicleModules.cs` | 2 |  | 2 |  | 1 | 3 | 1 |  |  | 9 |
| `Mod\WorldEffectModules.cs` | 1 | 3 | 14 | 5 | 1 | 9 |  |  |  | 33 |
| `Sim\Modules/ModuleFramework.cs` | 1 |  |  |  |  | 1 |  |  |  | 2 |
| `Sim\Replay/ReplayFormat.cs` |  |  |  |  |  |  |  | 1 |  | 1 |
| `Sim\Replay/ReplayRunner.cs` |  |  |  | 1 |  |  |  | 1 |  | 2 |
| `Sim\Scripting/ScriptEngine.Actions.cs` |  | 3 |  | 2 | 2 | 1 | 1 |  |  | 9 |
| `Sim\Scripting/ScriptEngine.Conditions.cs` |  |  |  |  |  | 3 |  |  |  | 3 |
| `Sim\Scripting/ScriptEngine.ExtendedActions.cs` |  |  |  | 1 |  |  |  |  |  | 1 |
| `Sim\Scripting/ScriptEngine.Phase13.cs` |  | 1 |  | 3 |  | 3 |  |  |  | 7 |
| `Sim\Scripting/ScriptEngine.Powers.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Sim\Scripting/ScriptEngine.Sequential.cs` |  |  |  | 1 | 3 | 1 |  |  |  | 5 |
| `Sim\Scripting/ScriptEngine.TeamAbilities.cs` |  | 2 |  |  |  |  |  |  |  | 2 |
| `Sim\Scripting/ScriptEngine.TeamEvents.cs` |  |  |  | 1 |  | 2 |  |  |  | 3 |
| `Sim\Scripting/ScriptEngine.TeamQueues.cs` | 1 |  |  | 1 |  |  |  |  |  | 2 |
| `Sim\Scripting/ScriptEngine.cs` | 1 | 2 |  | 1 | 1 |  | 2 |  |  | 7 |
| `Sim\Scripting/ScriptProgram.cs` |  |  |  |  | 1 |  |  |  |  | 1 |
| `Sim\Skirmish/SkirmishAi.cs` |  | 19 |  | 5 |  | 3 |  |  |  | 27 |
| `Sim\Skirmish/SkirmishMatchSession.cs` |  | 1 |  | 2 |  |  |  | 2 |  | 5 |
| `Sim\Snapshots/WorldSnapshot.cs` |  |  |  |  |  |  |  | 2 |  | 2 |
| `Sim\Templates/ObjectCreationListCatalog.cs` |  |  | 2 |  |  |  |  |  |  | 2 |
| `Sim\Templates/ObjectTemplate.cs` |  | 2 | 15 |  |  |  |  |  |  | 17 |
| `Sim\Templates/SpecialPowerCatalog.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Sim\Templates/TurretInfo.cs` | 2 |  | 5 |  | 1 |  |  |  |  | 8 |
| `Sim\Templates/UpgradeCatalog.cs` |  |  | 1 |  |  |  |  |  |  | 1 |
| `Sim\Templates/WeaponCatalog.cs` |  | 1 | 4 |  | 1 | 3 |  |  |  | 9 |
| `Sim\Testing/MissionObjectiveDriver.cs` |  |  |  | 1 |  |  |  |  | 2 | 3 |
| `Sim\Testing/MissionScenario.cs` |  |  |  |  |  |  |  |  | 6 | 6 |
| `Sim\Testing/ObjectiveDriver.Commands.cs` |  |  |  | 1 |  |  |  |  | 3 | 4 |
| `Sim\Testing/ObjectiveDriver.Leaves.cs` |  |  |  |  |  | 1 |  |  | 31 | 32 |
| `Sim\Testing/ObjectiveDriver.cs` |  |  |  |  |  |  |  |  | 7 | 7 |
| `Sim\Testing/ReferenceMatchScenario.cs` |  |  |  | 4 |  |  |  |  | 6 | 10 |
| `Sim\Testing/SkirmishMatchScenario.cs` |  |  |  | 2 |  |  |  |  | 2 | 4 |
| `Sim\Testing/StressScenario.cs` |  |  |  | 9 |  |  |  |  | 8 | 17 |
| `Sim\Testing/SyntheticFixture.cs` |  | 3 |  |  |  |  |  |  |  | 3 |
| `Sim\Time/SimulationTiming.cs` | 1 | 4 |  | 1 |  |  |  |  |  | 6 |
| `Sim\World/Entity.cs` |  |  |  |  |  | 1 |  |  |  | 1 |
| `Sim\World/SimulationWorld.Acquisition.cs` |  | 2 |  | 4 |  | 1 |  |  |  | 7 |
| `Sim\World/SimulationWorld.Collision.cs` |  |  |  |  |  | 4 |  |  |  | 4 |
| `Sim\World/SimulationWorld.Construction.cs` | 1 |  |  |  |  | 1 |  |  |  | 2 |
| `Sim\World/SimulationWorld.CreationLists.cs` | 1 |  |  |  |  | 1 |  |  |  | 2 |
| `Sim\World/SimulationWorld.FiringTracker.cs` | 1 | 1 |  | 1 |  |  |  |  |  | 3 |
| `Sim\World/SimulationWorld.Projectiles.cs` |  |  |  |  |  | 1 |  |  |  | 1 |
| `Sim\World/SimulationWorld.Snapshot.cs` |  |  |  |  |  |  |  | 1 |  | 1 |
| `Sim\World/SimulationWorld.Turrets.cs` | 1 | 3 |  | 1 |  | 9 |  |  |  | 14 |
| `Sim\World/SimulationWorld.Vision.cs` |  |  |  | 1 |  |  |  |  |  | 1 |
| `Sim\World/SimulationWorld.WeaponSlots.cs` |  | 1 |  |  |  |  |  |  |  | 1 |
| `Sim\World/SimulationWorld.cs` |  |  |  | 1 |  | 3 |  | 2 |  | 6 |
| **Summe** | 39 | 117 | 150 | 81 | 43 | 138 | 12 | 15 | 65 | 660 |

„B mech.“ = schlichte Umrechnungsaufrufe ohne eigene Zeile. Klasse F in der Simulation sind Werte, die die Simulation nur für die Darstellung führt (Ereigniswerte in Ticks, Anzeigezustände). Dateien der Simulation ohne Eintrag enthalten keine Stelle.

## 4. Antworten auf die offenen Fragen

### 4.1 Hängen die Golden-Dateien von der Tickrate ab?

- **`tests/CnCNew.Game.Tests/baselines/screens.txt`** (54 SHA-256 = 9 Bildschirme × 6 Auflösungen; erzeugt von `ShellTests.WalkScreens`/`Capture` über `UiDrawList.Dump`): Die Shell wird mit `animateTransitions: false` gebaut, die Bildschirme werden nach Klicks ohne Zeitfortschritt aufgenommen (ein einziges `shell.Update(1.0 / 30)` vor der Übergabe an das Match). **48 Einträge hängen nicht an der Rate.** Die **6 Einträge `ScoreScreen@…`** zeigen `UnitsBuilt`, `UnitsLost`, `UnitsDestroyed`, `BuildingsBuilt`, `BuildingsLost` und `Resources` eines echten Gefechts, das über `Session.Step()` bis `MaxTicks = 600` läuft (`ShellTests.cs`, Zeilen 107–113): sie hängen an der Rate, erstens über das Tickbudget (600 Ticks sind bei 60 Hz 10 s statt 20 s Spielzeit), zweitens über das Simulationsergebnis selbst. Bei 30 Hz bleiben sie in Phase 20/21 unverändert; mit Phase 23 sind sie neu zu erheben. Aus dem Code gefolgert, nicht ausgeführt.
- **`tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256`** (ein SHA-256; `HudTests.GoldenDrawListHash`): Der Dump entsteht nach genau einem `match.Advance(1.0 / 30)` plus `controller.Update(1.0 / 30)` in einem frischen Gefecht auf „Alpine Assault“ – bei 30 Hz ein Tick, bei 60 Hz wegen des Akkumulators zwei. Tickabhängige Inhalte der HUD-Zeichenliste sind Geldtext, Strombalken, Befehlsleiste (Abklingwischer, Bauschlange), Superwaffen-Countdown (`(readyFrame - Tick) / LogicFramesPerSecond`, Blinken über `Tick / SuperweaponFlashFrames`), Meldungszeilen (altern je Tick) und Heil-Symbole; eine Spielzeituhr gibt es nicht, die Minikarte ist ein einzelnes Rechteck. Ohne Auswahl, Mausereignis und Superwaffe sind davon nur Geld und Strom belegt. **Ob sich der Hash zwischen Tick 1 und Tick 2 unterscheidet, ist nicht ermittelt** (dazu müsste der Test laufen). Abhängig ist er also höchstens über die Schrittzahl; bei 30 Hz bleibt er unverändert.
- **Weitere gepinnte Werte:** `Audio.Tests/Tests.Catalog.cs` (`GoldenRng`, drei Zufallswerte), `Ui.Tests/WndTests.cs` (`DrawListGolden`, synthetische Oberfläche ohne Zeit; nur die Fundstelle gesehen), `baselines/compatibility.json` – nicht ratenabhängig. `Presentation.Tests`: kein gepinnter Hash (jede Prüfsumme wird Lauf gegen Lauf verglichen; einziger Festwert ist die Straßen-Dreieckszahl 206.956). `Simulation.Tests`: kein gepinnter Zustands-Hash (im Testquelltext stehen keine 16- oder 64-stelligen Hex-Zeichenketten; geprüft mit einer Mustersuche). Die Referenzwerte der Simulation stehen weiterhin nur in der Doku.
- **Werkzeuge:** `UiRenderTool` und die Capture-Skripte erzeugen Prüfsummen, die nirgends im Repo gepinnt sind (nicht geprüft, Anhang B).

### 4.2 Welche Tests prüfen ausdrücklich, dass Rate 60 abgelehnt wird?

Genau zwei, beide in `tests/CnCNew.Simulation.Tests/Tests.State.cs`:

1. `SnapshotRejection`, Zeile 153: ersetzt im Snapshot-Text `"tickRate":30` durch `"tickRate":60` und erwartet `SnapshotFormatException` aus `WorldSnapshot.Restore` („foreign tick rate“). Achtung: der Test arbeitet mit Textersetzung – steht im Snapshot einmal nicht mehr `30`, ersetzt er nichts und schlägt mit „the defective snapshot was accepted“ fehl.
2. `ReplayFormatTests`, Zeilen 221–222: setzt im Replay-Kopf `TickRate = 60` und erwartet `ReplayRejectedException` aus `ReplayRunner.Run(replay, templates, fingerprint)` („60 Hz“).

Kein Test prüft die Ablehnung in `MissionSnapshot.Restore` (Zeile 81; dieselbe Prüfung, ungetestet), und `SaveGame` hat keine Ratenprüfung. Zehn Testdateien bauen an elf Stellen einen `ReplayHeader` mit dem Literal `30` als Rate (`Tests.Acquisition`, `Construction`, `Repair`, `WeaponSlots` (2), `TurretAttack`, `Transports`, `Terrain`, `Teams`, `Supply`, `SpecialPower`); `InputTests`, `SkirmishMatchSession`, `ReferenceMatchScenario` und `StressScenario` nehmen die Konstante. `Tests.Basics.cs` (`Timing`, `FloatDeviation`) prüft `LogicFramesPerSecond == 30` und die ganze Tabelle Millisekunden → Ticks.

### 4.3 Welche Zustandsfelder in Snapshots sind absolute Ticks?

310 gespeicherte Felder mit Tickbezug in 59 Dateien: **222 absolute Ticks** (`nextWake`, Bereit-, Ablauf-, Start-Ticks), 26 Restzähler (herunterzählende Ticks), 23 Dauern/Intervalle in Ticks, 39 Größen je Tick (Geschwindigkeiten, Heilmengen, Winkelraten). Je Datei:

| Datei | absolut | Rest | Dauer | je Tick |
|---|---|---|---|---|
| `Game\Save/SaveGame.cs` | 1 |  |  |  |
| `Sim\Commands/Commands.cs` | 1 |  | 2 |  |
| `Sim\Events/SimEvent.cs` | 2 |  |  |  |
| `Sim\Mission/MissionSnapshot.cs` | 1 |  | 2 |  |
| `Mod\AirModules.cs` | 3 |  |  |  |
| `Mod\AirWeaponModules.cs` | 16 |  | 1 | 2 |
| `Mod\BattlePlanModule.cs` | 2 |  |  |  |
| `Mod\BridgeModules.cs` | 3 |  | 1 | 2 |
| `Mod\CleanupAndSpyModules.cs` | 5 | 1 |  |  |
| `Mod\CombatModules.cs` | 3 |  |  |  |
| `Mod\ContainChainModules.cs` | 1 |  |  |  |
| `Mod\ContainModules.cs` | 3 |  |  |  |
| `Mod\CrateModules.cs` | 2 |  |  |  |
| `Mod\DieAndUpdateModules.cs` | 2 |  |  |  |
| `Mod\DockMachine.cs` | 2 |  |  |  |
| `Mod\DockModules.cs` | 3 |  |  |  |
| `Mod\FerryModules.cs` |  |  |  | 2 |
| `Mod\FieldEffectModules.cs` | 4 |  | 1 | 2 |
| `Mod\FlightModules.cs` | 19 | 1 | 1 | 4 |
| `Mod\HackerAndAssaultModules.cs` | 2 | 1 |  |  |
| `Mod\JetAIUpdate.cs` | 6 |  |  | 1 |
| `Mod\LastBlockerModules.cs` | 4 |  |  |  |
| `Mod\MatchModules.cs` | 1 |  |  |  |
| `Mod\MineModules.cs` | 3 | 1 |  |  |
| `Mod\Phase12Modules.cs` | 6 |  |  |  |
| `Mod\Phase5BlockerModules.cs` | 11 |  |  |  |
| `Mod\Phase5Modules.cs` | 9 | 2 |  | 1 |
| `Mod\PhysicsAndMinefieldModules.cs` | 2 |  |  | 3 |
| `Mod\ProductionExitModules.cs` | 1 | 1 |  |  |
| `Mod\ProjectileModules.cs` | 7 | 3 | 1 | 5 |
| `Mod\RailroadModules.cs` | 1 | 1 | 1 | 2 |
| `Mod\RepairModules.cs` | 1 | 1 |  | 1 |
| `Mod\RetailMatchModules.cs` | 6 | 1 |  |  |
| `Mod\SlaveModules.cs` | 3 | 2 | 1 |  |
| `Mod\SpawnModules.cs` | 3 |  |  |  |
| `Mod\SpecialAbilityStaging.cs` | 1 | 2 |  | 1 |
| `Mod\SpecialPowerModules.cs` | 4 |  |  |  |
| `Mod\SpecialsModules.cs` | 3 |  |  | 1 |
| `Mod\StructureModules.cs` | 6 |  |  | 1 |
| `Mod\ToppleAndFallModules.cs` | 6 |  |  | 6 |
| `Mod\TransportModules.cs` | 5 | 1 |  | 3 |
| `Mod\VehicleModules.cs` | 4 |  |  | 1 |
| `Mod\WorldEffectModules.cs` | 17 | 5 | 1 |  |
| `Sim\Scripting/ScriptEngine.Phase13.cs` | 1 |  |  |  |
| `Sim\Scripting/ScriptEngine.Sequential.cs` |  | 1 |  |  |
| `Sim\Scripting/ScriptEngine.TeamAbilities.cs` | 1 |  | 1 |  |
| `Sim\Scripting/ScriptEngine.TeamQueues.cs` | 1 |  |  |  |
| `Sim\Scripting/ScriptEngine.cs` | 4 | 2 |  |  |
| `Sim\Templates/ObjectCreationListCatalog.cs` |  |  | 1 |  |
| `Sim\Timers/SimTimer.cs` | 1 |  | 1 |  |
| `Sim\World/AcquisitionState.cs` | 3 |  |  |  |
| `Sim\World/Entity.cs` | 6 |  | 1 | 1 |
| `Sim\World/SimulationWorld.FiringTracker.cs` | 2 |  |  |  |
| `Sim\World/SimulationWorld.Projectiles.cs` | 1 |  |  |  |
| `Sim\World/SimulationWorld.Snapshot.cs` | 3 |  | 4 |  |
| `Sim\World/SimulationWorld.cs` | 4 |  | 3 |  |
| `Sim\World/TunnelTracker.cs` | 1 |  |  |  |
| `Sim\World/TurretState.cs` | 4 |  |  |  |
| `Sim\World/WeaponSlotState.cs` | 5 |  |  |  |
| **Summe** | 222 | 26 | 23 | 39 |

Die Schlüssel je Datei stehen in Anhang C. Der Welt-Tick selbst (`"tick"`) und die Tick-Stempel der Befehle und Ereignisse sind mitgezählt. Die Liste stammt aus den vollständig gelesenen Dateien der Simulation; ein Gegenlauf über alle `writer.Int/UInt("…")`-Zeilen mit zeitartigen Namen findet 266 solche Zeilen, von denen 215 einem Listeneintrag direkt zuzuordnen sind – die übrigen 51 sind Pfadschreibweisen (`turrets[].holdUntil`), ID-Zähler und Kopffelder; sie wurden nicht einzeln nachgezogen (Anhang B). `SkirmishAi` hält absolute Ticks, wird aber nicht gespeichert (Plan E11). Für Phase 20 heißt das: ein Stand ist nur mit seiner Rate lesbar; ein Umrechnen gespeicherter Stände zwischen Raten wäre eine Änderung an rund 300 Feldern und ist im Plan zu Recht nicht vorgesehen.

### 4.4 Millisekunden-Felder und ihre Quantisierung bei 60 Hz (auf den Realdaten gemessen)

Gemessen am 5. Oktober 2026 auf `.local/phase2-import-a` (Retail-Basisdaten, `IniTool compile --maps exclude`, 12.550 Definitionen, 19.553 Module; Ausgabe 144.626.133 Byte im Scratchpad, nicht im Repo) mit einem Wegwerf-Skript über den Definitionssnapshot. Verglichen wird je Wert *m* in Millisekunden die Planregel `2 · ceil(m · 30 / 1000)` gegen die direkte Rundung `ceil(m · 60 / 1000)`.

| Bestand | Werte | verschiedene ms-Werte | davon bei 60 Hz anders quantisiert | Vorkommen | bei 120 Hz |
|---|---|---|---|---|---|
| Waffen (363 Definitionen): `ClipReloadTime` | 309 | 22 | 2 (40 ms, 1367 ms) | 21 | 2 / 21 |
| Waffen: `DelayBetweenShots` (Minimum) | 334 | 27 | 10 (1, 5, 40, 50, 75, 150, 250, 650, 750, 777 ms) | 81 | 11 / 83 |
| Waffen: `PreAttackDelay` | 29 | 8 | 2 (250 ms, 1538 ms) | 3 | 2 / 3 |
| Waffen: `ContinuousFireCoast` | 17 | 3 | 0 | 0 | 0 |
| **Waffen, alle vier Felder** | 689 | 42 | **12** | **105** (in **84 von 363 Waffen**) | 13 / 107 |
| **Modulfelder**, deren Schlüssel über `ModuleConfigReader.Ticks` gelesen wird (119 Schlüssel) | 9934 | 76 | **17** | **263** (42 verschiedene Modul/Schlüssel/Wert, 31 Modul/Schlüssel-Paare) | 18 / 267 |
| alle `Time`-Felder in Objektmodulen (Obergrenze) | 10545 | 94 | 26 | 331 | 30 / 347 |

Beispiele: 250 ms sind im Original 8 Bilder = 266,7 ms; direkt bei 60 Hz wären es 15 Ticks = 250 ms (115 Modulvorkommen, 22 bei Waffen). 40 ms sind 2 Bilder = 66,7 ms gegen 3 Ticks = 50 ms (43 Waffenfelder). Am häufigsten betroffen: `AIUpdateInterface.MoodAttackCheckRate` (41 Objekte), `SlowDeathBehavior.DestructionDelay` (34), `TransportContain.ExitDelay` (27), `LifetimeUpdate.Min/MaxLifetime` (je 12), `RiderChangeContain.ExitDelay` (11), `StructureCollapseUpdate.MinBurstDelay` (10). Die Planregel ist also nötig; mit direkter Rundung änderte sich bei 84 Waffen der Takt.

Weitere Messwerte: **118 von 363 Waffen** haben kein `DelayBetweenShots` (29) oder 0 ms (89) und laufen auf die Ein-Tick-Untergrenze (§0.2 Rang 2); bei 4 Waffen ist Minimum ≠ Maximum (nur das Minimum wird benutzt). In Modulen stehen 1.042 Zeitwerte mit 0 ms und 88 mit höchstens einem Bild (≤ 33 ms), unter anderem `PointDefenseLaserUpdate.ScanRate` (10 × 0 ms, 4 × ≤ 33 ms), `AutoHealBehavior.HealingDelay` (3 × 1 ms), `CountermeasuresBehavior.ReactionLaunchLatency` und `ReloadTime` (je 36 × 0 ms). `AutoDepositUpdate.DepositTiming` ist in den Realdaten nie 0 (10 × 2000 ms, 1 × 12000 ms). `BuildTime` in Sekunden (Objekte und Upgrades): kein Wert quantisiert bei 60 Hz anders.

Einschränkungen der Messung: Die Modulzählung ordnet nach **Schlüsselname**, nicht nach Modultyp (ein Schlüssel, den ein Modul liest, zählt auch in Modulen mit, die ihn nicht lesen – deshalb die Obergrenze in der letzten Zeile). Felder aus `GameData`, Spezialkräften, Upgrades, OCL und Türmen sind nicht aufgeschlüsselt. FX-Zeitfelder (4.348 Werte, 48 verschiedene mit anderem Ergebnis, 1.832 Vorkommen) betreffen die Präsentation, nicht die Simulation. Nicht gemessen ist die Wirkung der Laufzeitrundung mit Feuerraten-Bonus (W1).

## 5. Inventar Simulation (`CnCNew.Simulation`)

Je Datei, sortiert nach Zeile. „Phase“: 20 = Parametrisierung (ergebnisgleich bei 30 Hz), 23 = Zeitschritt-Invarianz, 21 = Präsentationsseite. Schlichte Umrechnungsaufrufe sind nicht aufgeführt (Zahl je Datei in Klammern).

### 5.1 Zeit, Kataloge, Formate

**`Sim\Commands/Commands.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 91 | SimCommand.Tick | G | Jeder Befehl ist an einen absoluten Tick adressiert; serialisiert als "tick" (Z. 106, gelesen Z. 116) im Replay | - | Replays sind nur mit ihrer Aufnahmerate gültig; ein 30-Hz-Replay lässt sich bei 60 Hz nicht abspielen (Ticks sind keine Zeiten). | 20 |
| 600 | SetTimerCommand.DelayTicks / RepeatTicks | G | Systembefehl mit Verzögerung und Wiederholintervall in Ticks; serialisiert als "delay"/"repeat" (Z. 609–610, gelesen Z. 125–126) | eigen | Aufrufer geben Ticks vor: unskaliert halbe Dauer; im Replay als Tickzahl festgeschrieben. | 20,21 |

**`Sim\Events/SimEvent.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 75-80 | SimEventKind.ProjectileLaunched (Value) | F/tick | Laut Doc-Kommentar trägt Value den absoluten Tick, an dem ein verzögerter Treffer landet (0 bei Projektil); Emittent und Verbraucher außerhalb des Pakets nicht geprüft | eigen | Am Simulationstick: eine Präsentation, die aus (Value − Tick) eine Flugzeit mit 1/30 s rechnet, zeigt die halbe Geschwindigkeit bzw. doppelte Dauer. | 21 |
| 116 | SimEventKind.Sabotaged (Value) | F/tick | Laut Doc-Kommentar kann Value die Deaktivierungsdauer in Ticks tragen („the disabling ticks“); Emittent und Verbraucher außerhalb des Pakets nicht geprüft | eigen | Am Simulationstick: Anzeige einer Dauer aus Value braucht die Rate. | 21 |
| 136 | SimEvent.Tick | G | Jedes Ereignis trägt den absoluten Tick; serialisiert als "tick" (Z. 143, gelesen Z. 164) | - | Verbraucher außerhalb der Simulation, die Tick-Differenzen als Zeit deuten, rechnen falsch; im Format nur mit der Rate gültig. | 20,21 |

**`Sim\Mission/MissionSession.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 34 | MissionOptions.MaxTicks | B/literal | Harte Laufgrenze in Ticks für Headless-Läufe (Standard 0 = keine); geprüft in IsOver (Z. 95), wird im Missionsstand gespeichert | eigen | Ein in Ticks angegebenes Budget deckt bei 60 Hz nur die halbe Spielzeit ab; Aufrufer (Werkzeuge, Tests) müssen skalieren. | 20,21 |
| 34 | MissionOptions.PresentationLatencyTicks | F/tick | Dauer in Ticks, die Sprache/Ton/Film/Kamerafahrt für HAS_FINISHED_*-Bedingungen als laufend gilt (Standard 0); Verbraucher ScriptEngine.Actions.cs Z. 398–405 (außerhalb des Pakets, nur per Grep gesehen) | eigen | Am Simulationstick: ein vom Host in Ticks übergebener Wert dauert bei 60 Hz nur halb so lang wie die echte Wiedergabe. | 20,21 |
| 249-260 | MissionSession.Step (Reihenfolge) | E | Je Tick: Skripte (Engine.Update), dann Weltschritt, dann Engine.Observe(Ereignisse): Skripte reagieren auf Ereignisse erst im nächsten Tick | eigen | Die Ein-Tick-Verzögerung zwischen Ereignis und Skriptreaktion halbiert sich von 33 ms auf 17 ms; Skriptketten über mehrere Ticks laufen in Echtzeit schneller. | 23 |
| 254 | MissionSession.Step Engine.TimeFrozen | F/tick | FREEZE_TIME: Skripte laufen je Step-Aufruf weiter, Weltschritt entfällt, World.Tick bleibt stehen; Kommentar verweist auf eine Präsentationsuhr für die Kameras (Verhalten der Engine im Freeze nicht geprüft, außerhalb des Pakets) | eigen | Im Freeze zählt nur die Aufrufrate von Step: bei 60 Hz doppelt so viele Skriptdurchläufe pro Sekunde bei stehendem Tick. | 20,21 |

**`Sim\Mission/MissionSnapshot.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 39 | MissionSnapshot.Write "tickRate" | G | Schreibt die Tickrate (SimulationTiming.LogicFramesPerSecond) in den Missionsstand | - | Muss die Rate der gespeicherten Sitzung tragen. | 20 |
| 46-48 | MissionSnapshot.Write options maxTicks / presentationLatencyTicks / combatIntervalTicks | G | Missionsoptionen werden in Ticks gespeichert und in Restore Z. 91–92 unverändert zurückgelesen | - | Werte gelten nur zusammen mit der gespeicherten Rate; durch die Ratenprüfung (Z. 81) derzeit konsistent, bei Ratenwechsel nicht umrechenbar ohne Regel. | 20 |
| 81 | MissionSnapshot.Restore "tickRate" | G | Lehnt den Stand ab, wenn tickRate ungleich der einkompilierten Rate ist („Tick rate mismatch.“); keine Umrechnung, kein Versionssprung (Version 2) | - | Alle 30-Hz-Stände werden bei anderer Rate abgewiesen; bei wählbarer Rate muss gegen die Rate der Zielsitzung geprüft oder die Sitzung mit der gespeicherten Rate aufgebaut werden. | 20 |

**`Sim\Modules/ModuleFramework.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 119-123 | IWorldServices.AdvanceConstruction | E | Schnittstelle „one tick of construction work“: Baufortschritt als Schritt je Tick, letzter Tick stellt fertig; Implementierung außerhalb des Pakets, nicht gelesen | DozerAIUpdate::update (nur im Doc-Kommentar genannt; Herkunft unbelegt) | Hängt von der Implementierung ab: fester Fortschritt je Aufruf würde bei 60 Hz doppelt so schnell bauen. | 20,23 |
| 125-126 | IWorldServices.IsSignificantlyAboveTerrain | A | Schnittstelle mit Originalkonstante im Doc: „higher than three frames of free fall“; die Rechnung selbst liegt in der Implementierung (SimulationWorld, außerhalb des Pakets, nicht gelesen) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/Common/Thing/Thing.cpp, Thing::isSignificantlyAboveTerrain: getHeightAboveTerrain() > -(3*3)*m_gravity | Mit Schwerkraft je Tick² ergibt 3×3×g bei 60 Hz ein Viertel der Höhe; 3 Frames müssen zu 6 werden (36×g60 = 9×g30). | 20 |

**`Sim\Replay/ReplayFormat.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 19-21, 52, 67-68, 102 | ReplayHeader.TickRate / EndTick / CheckpointInterval | G | Replay-Kopf schreibt u32 Tickrate, i64 Endtick und u32 Prüfpunktabstand in Ticks; Befehle tragen i64 Tick | eigen | Rate wird schon gespeichert; EndTick/CheckpointInterval sind Tickzahlen der Aufzeichnungsrate | 20 |

**`Sim\Replay/ReplayRunner.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 50-51 | ReplayRunner.CheckCompatibility | G | Lehnt jeden Replay-Kopf ab, dessen TickRate ≠ LogicFramesPerSecond ist; die Überladung `Run(replay, world, options)` (Zeile 71) prüft gar nicht | eigen | Künftig: Welt mit der gespeicherten Rate erzeugen, nur nicht unterstützte Raten ablehnen; die ungeprüfte Überladung muss Rate der Welt gegen den Kopf prüfen | 20 |
| 100-102 | ReplayRunOptions.RecordCheckpointInterval / VerifyIncrementalEvery | C | `(tick + 1) % N`: Prüfpunkt- und Verifikationsrhythmus in Ticks (Werkzeug-/Testparameter) | eigen | Nur Last und Dichte der Prüfpunkte, keine Ergebnisänderung | 20 |

**`Sim\Snapshots/WorldSnapshot.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 37 | WorldSnapshot.Write ("tickRate") | G | Schreibt `tickRate` = Konstante in den Kopf (nicht in den Zustands-Hash) | eigen | Muss die Rate der Welt schreiben | 20 |
| 67, 78-82 | WorldSnapshot.Restore | G | Lehnt `tickRate` ≠ Konstante ab („Tick rate mismatch.“); `WorldConfiguration` wird ohne Rate neu gebaut | eigen | Künftig Welt mit der gespeicherten Rate erzeugen; WorldConfiguration braucht das Feld | 20 |

**`Sim\Templates/ObjectTemplate.cs`** (dazu 15 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 361-460 | TemplateCatalog.Build (binding.ParseData je Modul, BuildLocomotors, WeaponCatalog.Build …) | B/daten | Der gesamte Katalog wird ohne Ratenparameter gebaut; alle Tick- und Je-Tick-Werte der Moduldaten, Waffen, Locomotoren, Upgrades, Spezialkräfte, OCL und Türme entstehen hier einmal je Datenbestand | eigen | Der Katalog ist ratenabhängig und wird heute zwischen Werkzeugen, Client und Sitzungen geteilt; die Rate muss Bauparameter und Teil des Fingerabdrucks werden | 20 |
| 480 | TemplateCatalog.GravityPerTickSquared | B/daten | Eigenschaft rechnet bei JEDEM Zugriff `Gravity / 900` (decimal → Fixed), nicht einmal beim Bau; `Gravity` bleibt als decimal je s² im Katalog | GameData.ini Gravity; GlobalData::m_gravity | Braucht die Rate der Welt zur Laufzeit; bei 60 Hz ein Viertel je Tick² (−64/3600 = −0,0178: 1165 Rohschritte in Q47.16) | 20 |

**`Sim\Templates/SpecialPowerCatalog.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 25, 54 | SpecialPowerCatalog.DefaultReloadMilliseconds | B/daten | Standard 1000 ms, wenn ReloadTime fehlt; in Millisekunden geführt und über DurationMillisecondsToTicks umgerechnet (kein Tickliteral) | Default/SpecialPower.ini DefaultSpecialPower (laut Kommentar) | Keine eigene Kopplung über die Umrechnung hinaus | 20 |

**`Sim\Templates/TurretInfo.cs`** (dazu 5 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 26 | TurretInfo.NeverScanTicks | A | `9999999` Bilder als Standard für Min/MaxIdleScanInterval („praktisch nie“) | Herkunft unbelegt (Kommentar nennt TurretAIData::m_minIdleScanInterval; Wert im Original nicht nachgeschlagen) | Sentinel; skalieren unnötig, darf aber nicht überlaufen, wenn pauschal × Rate/30 gerechnet wird | 20 |
| 29 | TurretInfo.DefaultRate | D | `0.01` rad je Bild als Standard für TurretTurnRate/TurretPitchRate, wenn das Feld fehlt (Zeilen 168–169); Datenwerte laufen über DegreesPerSecondToRadiansPerTick | TurretAI.h: DEFAULT_TURN_RATE = DEFAULT_PITCH_RATE = 0.01f; TurretAI.cpp Zeile 178/179 | Ohne Umrechnung dreht ein Turm ohne eigene Rate bei 60 Hz doppelt so schnell | 20 |
| 134 | TurretInfo.Parse: RecenterTime-Standard `2 * LogicFramesPerSecond` | A | Standard der Rückstellzeit in Bildern, wenn RecenterTime fehlt | TurretAI.cpp Zeile 200: m_recenterTime = 2*LOGICFRAMES_PER_SECOND | Skaliert mit der Konstante; beim Katalogbau berechnet | 20 |

**`Sim\Templates/WeaponCatalog.cs`** (dazu 4 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 15-17 | WeaponInfo (Flugzeit ohne Projektilobjekt: ceil(distance / WeaponSpeed) Ticks) | E | Dokumentierter Vertrag: Treffer nach aufgerundeten Ticks Flugzeit, „sofort“ unter einem Tick Weg (Umsetzung in SimulationWorld.Projectiles.cs, dort eigene Zeile) | Weapon.cpp WeaponTemplate::fireWeaponTemplate (laut Kommentar; Ablauf nicht nachgeschlagen) | Schwelle „unter einem Tick Weg“ und Aufrundung hängen an der Schrittweite | 23 |
| 33 | WeaponInfo.InstantSpeed | D | Standard `999999` Strecke je Bild für WeaponSpeed/MinWeaponSpeed, wenn das Feld fehlt (Zeile 243 `Speed(key)`) | Weapon.cpp WeaponTemplate-Konstruktor: m_weaponSpeed = m_minWeaponSpeed = 999999.0f | Bleibt „sofort“; als Wert je Tick nicht skalieren (sonst ändert sich nichts Sichtbares, aber der Wert ist kein Datenwert je Sekunde) | 20 |
| 80-85 | WeaponInfo.PreAttackDelayWith | E | Laufzeit-Rundung: `floor(PreAttackDelayTicks × PRE_ATTACK-Multiplikator)` | Weapon.cpp WeaponTemplate::getPreAttackDelay (Int-Rückgabe schneidet ab) | Wie ScaledByRateOfFire: abgeschnitten wird in Ticks der laufenden Rate, nicht in Originalbildern | 20,23 |
| 113-118 | WeaponInfo.ScaledByRateOfFire (DelayBetweenShotsWith, ClipReloadWith) | E | Laufzeit-Rundung: `floor(Ticks / RateOfFire-Multiplikator)`, mindestens 1 Tick; je Schuss berechnet | Weapon.cpp WeaponTemplate::getDelayBetweenShots / getClipReloadTime: REAL_TO_INT_FLOOR(delay / bonusROF) | floor(2t/m) ist nicht 2·floor(t/m): mit Feuerraten-Bonus (Veteranen, Upgrades, Feuerstufen) ergeben sich bei 60 Hz andere Schussabstände als auf dem 30er-Raster – der Plan (§2.5) deckt nur die Katalogrundung ab | 20,23 |
| 209-210, 263 | WeaponCatalog.Build: DelayBetweenShots (Standard SleepNone, Math.Max(SleepNone, delay)) | B/fallback | Fehlt DelayBetweenShots oder ist es 0 ms, wird der Schussabstand 1 Tick; nur `ShotDelayValue.Min` wird benutzt (Max verworfen) | Weapon.cpp WeaponTemplate::getDelayBetweenShots (das Original würfelt zwischen min und max); Untergrenze 1 Tick ist eigen | Auf den Realdaten 118 von 363 Waffen (29 ohne Schlüssel, 89 mit 0 ms): ihr Mindestabstand würde von 33 ms auf 17 ms fallen, wenn die Untergrenze ein Tick statt ein Originalbild bleibt | 20 |

**`Sim\Time/SimulationTiming.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 21 | SimulationTiming.LogicFramesPerSecond | A | Die zentrale Konstante `const int = 30`; als `const` in jede Verwendung einkompiliert (auch in andere Assemblies) | GameCommon.h: LOGICFRAMES_PER_SECOND = 30 | Muss ein Wert der Welt werden; jede der 63 Verwendungen in der Simulation hängt daran | 20 |
| 25-28 | SimulationTiming.SleepNone / SleepForever | C | `SleepNone = 1` (kleinster Schlaf ein Tick, 273 Verwendungen) und `SleepForever = 0x3fffffff` (190 Verwendungen); Kommentar „about 414 days at 30 Hz“ | UpdateModule.h: UPDATE_SLEEP_FOREVER = 0x3fffffff | „Jeden Tick“ heißt bei 60 Hz doppelt so oft je Sekunde: wo ein Modul je Aufwachen etwas Festes tut, ändert das Ergebnisse (Einzelstellen in den Modultabellen); SleepForever bleibt | 20,23 |
| 33-46 | SimulationTiming.DurationMillisecondsToTicks (long und decimal) | B/daten | ms → ganze Ticks, aufgerundet (`ceil(ms·30/1000)`), exakt ganzzahlig bzw. in `decimal`; statische Funktion ohne Ratenparameter | INI.cpp INI::parseDurationUnsignedInt (ceilf(ConvertDurationFromMsecsToFrames)) | Laut Plan: erst auf Originalbilder aufrunden, dann × Rate/30; direkte Rundung bei 60 Hz ergäbe andere Zeiten (§4.4) | 20 |
| 49-53 | SimulationTiming.DurationMillisecondsToFixedTicks | B/daten | ms → gebrochene Ticks ohne Rundung (`Fixed`), 4 Aufrufer | INI.cpp INI::parseDurationReal | Skaliert linear; nur Fixed-Auflösung | 20 |
| 56-62 | SimulationTiming.SecondsToTicks | B/daten | Sekunden → ganze Ticks, aufgerundet (`ceil(s·30)`); für BuildTime von Objekten und Upgrades | Herkunft unbelegt (Rundungsregel der Bauzeit im Original nicht nachgeschlagen) | Wie Dauern: auf dem 30er-Raster runden, dann skalieren; auf den Realdaten kein Unterschied gemessen (§4.4) | 20 |
| 64-71 | SimulationTiming.PerSecondToPerTick / PerSecondSquaredToPerTickSquared / DegreesPerSecondToRadiansPerTick | B/daten | Geschwindigkeit ÷ 30, Beschleunigung ÷ 900 (`LogicFramesPerSecond * LogicFramesPerSecond`), Winkelgeschwindigkeit ÷ 30; Rundung auf `Fixed` (nächster Wert) | INI.cpp INI::parseVelocityReal / parseAccelerationReal / parseAngularVelocityReal | Bei 60 Hz halbe bzw. geviertelte Werte je Tick: Q47.16 wird für Beschleunigungen grob (Phase 22); Reihenfolge Teilen/Runden muss bei 30 Hz gleich bleiben | 20 |

### 5.2 Welt (`Sim\World`)

**`Sim\World/Entity.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 61 | Entity.Speed | E | Vorwärtsgeschwindigkeit als Strecke je Tick (Feldkommentar „dist/tick“), wird im Snapshot geschrieben und in Collision.cs als Kippkraft gelesen | eigen | Zahlenwert halbiert sich bei gleicher Geschwindigkeit; jeder Leser, der Speed als absolute Größe deutet, verhält sich anders. | 20,23 |

**`Sim\World/SimulationWorld.Acquisition.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 35 | SimulationWorld.OrderedAttackGraceTicks | B/literal | Schonfrist 45 Ticks (1,5 s): befohlener Angriff gilt als beendet, wenn so lange nach Feuerbereitschaft kein Feuer-/Verfolgungsbefehl kam; 2 Verwendungen (Z. 153, 339, dort zusätzlich "- 1") | eigen | Frist schrumpft auf 0,75 s: befohlene Angriffe enden früher, die Selbstakquise setzt früher ein. | 20 |
| 38 | SimulationWorld.ForceIdleTicks | B/fallback | ForceIdleMSEC (AIData.ini 67 ms, nicht typisiert) fest als 2 Ticks geschrieben: Wartezeit bis zum ersten Blick nach Eintritt in einen suchenden Zustand; 2 Verwendungen (Z. 163, 179) | GameLogic/AI/AI.cpp Feldtabelle ForceIdleMSEC -> m_forceIdleFramesCount (INI::parseDurationUnsignedInt, ceil; Default 1); benutzt in GameLogic/Object/Update/AIUpdate.cpp AIUpdateInterface::resetNextMoodCheckTime; Wert 67 ms unter references nicht belegt | Wartezeit halbiert sich auf 33 ms; keine Rundung, weil nicht aus Millisekunden gerechnet (ceil(67 ms) wären bei 30 Hz übrigens 3, nicht 2). | 20 |
| 41 | SimulationWorld.ChaseRefreshTicks | C | Verfolgung wird alle 15 Ticks (0,5 s) neu auf die Position des Opfers gerichtet (StartInternalMove); 1 Verwendung (Z. 289) | eigen | Ergebnisse: Neuplanung alle 0,25 s, also doppelt so viele Wegsuchen und andere Pfade/Weckzeitpunkte. | 20 |
| 111-131 | SimulationWorld.StepAcquisition | C | Ein Durchlauf je Tick über alle Akquirierer in Id-Reihenfolge; je Tick ein AutoFire-Versuch, in AutoFire (Z. 466) _turretWaits++ je Tick, den ein Turm noch dreht | eigen | Ergebnisse: Schusszeitpunkte feiner gerastert, Zähler turretWaits (im Snapshot) zählt Ticks und verdoppelt sich. | 20,23 |
| 208 | SimulationWorld.StepAcquirer (WakeAt tick + 1, Wiederaufnahme des Angriffsmarschs) | C | AI-Modul wird nach StartInternalMove für den nächsten Tick geweckt (tick + 1) | eigen | Nur Latenz: Marsch beginnt 1/60 s statt 1/30 s später; Reihenfolge im Tick unverändert. | 23 |
| 236-248 | SimulationWorld.MoodCheckOffset | E | Einmaliger Zufallsversatz der Blickrate im Bereich ±(rate >> 1) Ticks, als Hash aus (Weltseed, Entity-Id, Tick) statt aus dem Logikstrom; addiert auf tick + rate (Z. 189) | GameLogic/Object/Update/AIUpdate.cpp AIUpdateInterface::getNextMoodTarget (GameLogicRandomValue(-halfRate, halfRate)); der Hash selbst ist eigen | Andere Ticknummern und doppelter Wertebereich ergeben andere Versätze; die Ziehung ist nicht ratenunabhängig, Akquisezeitpunkte verschieben sich. | 23 |
| 292 | SimulationWorld.Engage (WakeAt tick + 1, Verfolgung) | C | AI-Modul wird nach StartInternalMove auf das Opfer für den nächsten Tick geweckt (tick + 1) | eigen | Nur Latenz: Verfolgung beginnt 1/60 s statt 1/30 s später. | 23 |

**`Sim\World/SimulationWorld.Collision.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 37-52,95-109 | SimulationWorld.AvoidLocalBlockers / StepBlocked | E | Blockadeprüfung am Endpunkt eines Tick-Schritts (dx, dy = Weg eines Ticks, Aufrufer Phase5Modules.cs Z. 1117); die Vorausschau ist genau ein Tick Weg, der Ausweichschritt ebenso lang | eigen | Vorausschau und Ausweichschritt halbieren sich: Ausweichen setzt später und näher am Hindernis ein, Bahnen ändern sich. | 23 |
| 58-92 | SimulationWorld.StationaryBlockersAhead | E | Vorausschau = übergebener Vektor, an vier Stützpunkten geprüft; der Aufrufer (Phase5Modules.cs Z. 1109, nicht in diesem Paket) übergibt Speed je Tick × 25; Umkreis 150 ist eine Strecke | eigen | Bleibt der Aufrufer bei „Speed × 25“, halbiert sich die Vorausschau (25 Ticks Weg); stehende Blocker werden später erkannt. | 23 |
| 112-124,204-218 | SimulationWorld.ResolveFootprintCollisions / TrySquish | E | Überlappungs-, Kipp- und Überrolltest einmal je Bewegungsschritt des Bewegers; „nähert sich“ wird aus dem tatsächlichen Tick-Versatz (stepX/stepY) bestimmt | eigen | Doppelt so viele Abtastpunkte je Strecke: Überlappung wird bis zu einen halben alten Schritt früher erkannt, Zeitpunkt/Reihenfolge von Kollisionsereignissen ändert sich. | 23 |
| 193 | SimulationWorld.TryTopple (mover.Speed) | E | Kippkraft = mover.Speed (Strecke je Tick) wird unverändert an ToppleUpdate.ApplyTopplingForce gereicht | Herkunft unbelegt | Bei skalierter Speed (Strecke je Tick halbiert) kommt die halbe Kraft an, sofern der Verbraucher nicht umrechnet; Kippverhalten ändert sich. | 20,23 |

**`Sim\World/SimulationWorld.Construction.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 18 | SimulationWorld.SaleDurationTicks | A | 179 Ticks vom Verkaufsbefehl bis zur Erstattung (45 Frames Gerüst + 135 Frames bis -50 % - 1); 1 Verwendung (Z. 275, SellCompleteTick = _tick + 179) | Common/System/BuildAssistant.cpp: FRAMES_TO_ALLOW_SCAFFOLD (LOGICFRAMES_PER_SECOND * 1.5f), TOTAL_FRAMES_TO_SELL_OBJECT (LOGICFRAMES_PER_SECOND * 3.0f), BuildAssistant::update (Schwelle -50 %) | Verkauf dauert knapp 3 s statt 6 s; die Skalierung ist nicht linear (2 × 180 - 1 = 359, nicht 358). | 20 |
| 227-242 | SimulationWorld.AdvanceConstruction (Entity.ConstructionWork) | E | ConstructionWork++ je Arbeitstick des Bauenden bis Template.BuildTicks; Gesundheit steigt je Tick um die ganzzahlige Differenz der linearen Zielkurve | Herkunft unbelegt | Mit skaliertem BuildTicks gleich lang, nur feinere Gesundheitsstufen; unskaliert doppelt so schnell. constructionWork im Snapshot ist eine Tickzahl. | 20 |

**`Sim\World/SimulationWorld.CreationLists.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 136 | SimulationWorld.ExecuteDeliverPayload (DelayDeliveryMaxTicks) | E | Zufallsverzögerung der Lieferung: LogicRandom.Range(0, DelayDeliveryMaxTicks) in ganzen Ticks, je Formationsmitglied eine Ziehung | Herkunft unbelegt | Doppelter Wertebereich: gleiche Zahl Ziehungen, aber andere Werte; die Verzögerung in Sekunden ist nicht bitgleich zum 30-Hz-Lauf. | 23 |
| 343 | SimulationWorld.IsSignificantlyAbove | A | Schwelle -9 × Gravity / 900: Fallhöhe von 3 Frames (3 × 3 Frames² mal Gravitation je Frame²); Gravity liegt in Einheiten/s² vor, die 900 ist 30²; decimal-Rechnung bei jedem Aufruf (Laufzeit) | Common/Thing/Thing.cpp Thing::isSignificantlyAboveTerrain (-(3*3)*TheGlobalData->m_gravity, „more than 3 frames“) | Unverändert bleibt die Höhe gleich (0,1 s Fall). Wird nur 900 durch rate² ersetzt, viertelt sie sich: die 3 Frames müssen mitskaliert werden (viele Aufrufer: Luft/Boden-Entscheidungen). | 20 |

**`Sim\World/SimulationWorld.FiringTracker.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 166 | SimulationWorld.PreAttackStaleTicks | B/literal | = SimulationTiming.LogicFramesPerSecond (30 Ticks = 1 s): Verfallsfrist eines Aufziehens, dem kein Schuss folgte; 1 Verwendung (Z. 178) | eigen | Folgt der Ratenkonstante; als const an den Kompilierzeitwert 30 gebunden, bei Laufzeitparameter wären es sonst 0,5 s. | 20 |
| 234-257 | SimulationWorld.StepFiringTrackers | C | Alle Tracker mit laufendem Abkühltimer werden in jedem Tick abgefragt (die Referenz schläft stattdessen per UPDATE_SLEEP) | GameLogic/Object/FiringTracker.cpp FiringTracker::update | Nur Last: der Vergleich tick > CooldownFrame arbeitet mit absoluten Ticks, das Ergebnis hängt nicht vom Abfragerhythmus ab. | 20 |
| 247 | SimulationWorld.StepFiringTrackers (CooldownFrame = tick + LogicFramesPerSecond) | A | Dauer der Slow-Stufe nach Ablauf der Auslaufzeit: tick + SimulationTiming.LogicFramesPerSecond (1 s) | GameLogic/Object/FiringTracker.cpp FiringTracker::update (m_frameToStartCooldown = now + LOGICFRAMES_PER_SECOND) | Bleibt 1 s, sobald die Konstante der Ratenparameter ist; als feste 30 wären es 0,5 s. | 20 |

**`Sim\World/SimulationWorld.Projectiles.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 125-134 | SimulationWorld.FlightTicks | E | Flugzeit eines projektillosen Schusses = ceil(Distanz / WeaponSpeed je Tick) auf Rohwerten; 0 (Sofortschaden im selben Tick), wenn Distanz < ein Tick Weg | GameLogic/Object/Weapon.cpp WeaponTemplate::fireWeaponTemplate (delayInFrames, „< 1.0f“, REAL_TO_INT_CEIL) | Sofortschaden-Schwelle halbiert sich und die Aufrundung erfolgt auf 1/60 s: Schaden landet bis zu einen alten Tick früher, Nahschüsse sind nicht mehr sofort. | 20,23 |

**`Sim\World/SimulationWorld.Snapshot.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 36-45 | SimulationWorld.LoadFull | G | Lädt den absoluten Tick und alle Tick-Felder (Entities, Spieler, Spawn-Warteschlange, verzögerter Schaden) ohne Prüfung einer Tickrate; in dieser Datei gibt es kein Ratenfeld | - | Ein 30-Hz-Snapshot würde bei 60 Hz kommentarlos mit falsch gedeuteten absoluten Ticks und Dauern fortgesetzt. | 20 |

**`Sim\World/SimulationWorld.Turrets.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 21 | SimulationWorld.TurretAimGraceTicks | B/literal | Mindesthaltezeit eines Einmal-Zielens nach der letzten Anforderung: 15 Ticks (0,5 s); 2 Verwendungen (Z. 128, 283), jeweils Math.Max(info.RecenterTicks, 15) | eigen | Unskaliert nur 0,25 s: der Turm gibt ein Einmal-Ziel doppelt so früh frei; kein Rundungsproblem (15 → 30). | 20 |
| 24 | SimulationWorld.TurretReloadHoldCapTicks | B/literal | Obergrenze des Nachladens, das die Haltezeit verlängert: 450 Ticks (15 s); 2 Verwendungen (Z. 282 und SimulationWorld.Acquisition.cs:339 als Clamp-Grenze) | eigen | Unskaliert 7,5 s statt 15 s: Türme mit langem Nachladen (und die Angriffsfrist in Acquisition) lassen früher los. | 20 |
| 27 | SimulationWorld.TurretSweepEnableTicks | A | Schwenk (TurretFireAngleSweep) bleibt 3 Ticks nach einem Schuss an; 1 Verwendung (Z. 286: SweepUntil = _tick + 3), Vergleich tick < SweepUntil in AimStep Z. 428 | GameEngine/Source/GameLogic/AI/TurretAI.cpp, lokale Konstante ENABLE_SWEEP_FRAME_COUNT = 3 (Z. 724, m_enableSweepUntil = now + …) | Unskaliert nur 50 ms Schwenkfenster statt 100 ms: Schwenkwaffen überstreichen weniger Winkel, Trefferbild ändert sich. | 20 |
| 30 | SimulationWorld.TurretLoseInterestTicks | B/literal | Frist, nach der ein stehender Angreifer ein Opfer außer Reichweite aufgibt: 45 Ticks (1,5 s); 1 Verwendung (Z. 265) | eigen | Unskaliert 0,75 s: Angriffe ohne Verfolgung enden früher, Türme zentrieren früher, KI-Verläufe ändern sich. | 20 |
| 38 | SimulationWorld._turretWaits | E | Zähler zurückgehaltener Feuerbefehle (erhöht in SimulationWorld.cs Z. 1051 und WeaponSlots.cs Z. 411), Teil des Zustands-Hashes sobald ungleich 0 | eigen | Zählt je Feuerbefehl während des Eindrehens; kommen Befehle je Tick, verdoppelt sich der Wert, Hash und Goldens ändern sich. | 23 |
| 257-267 | SimulationWorld.KeepsAttackIntent | E | Je Tick geprüft: Sicht (CanPlayerSee, Nebel nur alle 3 Ticks aktuell) und Reichweite; Frist startet am ersten Tick außer Reichweite, Ende bei tick >= LoseInterestAt | eigen | Abbruchzeitpunkt hängt am Sichtraster und an der Tickgrenze; bei 60 Hz feinere Abtastung, Abbruch kann bis zu einem halben alten Tick früher fallen. | 23 |
| 295-302 | SimulationWorld.IsAligned (TurretAlignThreshold 0,035 rad) | E | Feuerfreigabe: Winkelabstand höchstens 0,035 rad (bzw. kleiner als Sweep-Winkel) und Pitch höchstens 0,035 rad; geprüft je Feuerbefehl gegen den im Vortick gesteppten Turmwinkel | TurretAI.cpp, REL_THRESH = 0.035f (Z. 1093) und fabs(angleDiff) < sweep (Z. 1104) | Schwelle ist ein fester Winkel, der Schritt aber halb so groß: erster Schuss fällt auf einen anderen (feineren) Zeitpunkt, Winkel beim Befehl ist nur 1/60 s alt. | 23 |
| 340-367 | SimulationWorld.StepTurrets | C | Jeder Turm, der nicht ruht oder scannen kann, wird jeden Tick gesteppt (Id-Reihenfolge), nach RunUpdates und StepAcquisition | eigen | Ergebnisse: ja – doppelt so viele Dreh-/Fristschritte; nur richtig, wenn Raten und Fristen skaliert sind; Last verdoppelt. | 20,23 |
| 396 | SimulationWorld.StepTurret (holding) | E | Halten gilt solange tick <= HoldUntil, also einschließlich des Frist-Ticks (RecenterTicks + 1 Schritte); die Referenz verlässt den Haltezustand bei now >= m_timestamp | TurretAI.cpp Z. 1439/1459 (m_timestamp = getFrame() + getRecenterTime(); if (getFrame() >= m_timestamp)) | Der eine Zusatzschritt ist 16,7 ms statt 33,3 ms: Haltezeit nicht exakt rateninvariant (Abweichung um einen Tick). | 23 |
| 421-438 | SimulationWorld.AimStep | E | Turm folgt dem Opfer je Tick um TurnRate (rad/Tick, mal SweepSpeed während des Schwenks); Schwenkseite kippt, sobald der Winkel höchstens 0,035 rad am Ziel liegt; danach Pitch-Schritt | TurretAI.cpp, friend_turnTowardsAngle(aimAngle, turnSpeedModifier, REL_THRESH) Z. 1093–1094 | Halbe Schrittweite: andere Einrast- und Umkehrzeitpunkte, Nachführfehler gegen bewegte Ziele/Wanne halbiert; mit unskaliertem 3-Tick-Fenster weniger Schwenkweg. | 20,23 |
| 454-471 | SimulationWorld.IdleScanStep | E | Leerlauf-Scan: erster Schritt plant nur NextScan; bei tick >= NextScan werden zwei Hash-Zufallswerte gezogen (Winkel, Seite) und im selben Tick der erste Drehschritt gemacht | TurretAI.cpp Z. 1315 (now >= m_nextIdleScan) und Z. 1369–1370 (GameLogicRandomValueReal / GameLogicRandomValue(0,1)) | Ziehungen hängen am Zähler ScanDraws, nicht am Tick; Zeitpunkte aber an NextScan in Ticks – ohne Skalierung doppelt so häufige Scans. | 20,23 |
| 495-496 | SimulationWorld.ScheduleScan | E | Nächster Scan = tick + ganzzahlige Zufallszahl aus [MinScanIntervalTicks, MaxScanIntervalTicks] (Hash aus Seed, Entity, Turm, Ziehungszähler; Modulo über die Spannweite in Ticks) | TurretAI.cpp, TurretAIIdleState::resetIdleScan (GameLogicRandomValue(getMinIdleScanInterval(), getMaxIdleScanInterval()), Z. 1285–1289) | Spannweite in Ticks verdoppelt sich, das Modulo liefert andere Werte: Scan-Zeitpunkte sind nicht aus einem 30-Hz-Lauf ableitbar. | 20,23 |
| 530-544 | SimulationWorld.TurnAngle | E | Einrasten, wenn der Restwinkel kleiner als ein Tickschritt (rate) ist, sonst Schritt um ±rate; setzt Rotating; auch für Recenter/Scan mit Faktor 0,5 (TurretRecenterRateFactor, dimensionslos) | TurretAI.cpp, TurretAI::friend_turnTowardsAngle (fabs(angleDiff) < turnRate; Faktor 0.5f in Z. 1236, 1388) | Einrastfenster halbiert sich mit der Rate; Ankunftstick, Rotating-Flag (Modellzustand) und Folgezustände verschieben sich um Bruchteile eines 30-Hz-Ticks. | 23 |
| 547-551 | SimulationWorld.TurnPitch | E | Pitch rastet ein, wenn der Rest kleiner als ein Tickschritt (PitchRate rad/Tick) ist, sonst Schritt um ±rate | TurretAI.cpp, TurretAI::friend_turnTowardsPitch (Z. 432–458) | Wie TurnAngle: halbes Einrastfenster, Pitch-Freigabe (IsAligned) zu anderem Zeitpunkt. | 23 |

**`Sim\World/SimulationWorld.Vision.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 12 | SimulationWorld.VisionIntervalTicks | C | Sicht/Nebel wird alle 3 Ticks neu berechnet (tick % 3 == 0 in SimulationWorld.Step Z. 1678, am Tickende nach Spawn); zweite Verwendung in src/CnCNew.Game/Live/LiveGame.cs:211 (Fog-Präsentation); Kommentar nennt unter 100 ms | eigen (Kommentar verweist auf den Partition-Zellentimer der Referenz; dort nicht nachgeschlagen) | Ergebnisse: ja – CanPlayerSee (Zielbarkeit, Befehlsprüfung, KI-Sicht, Turm-Abbruch) und visionHash wechseln alle 50 ms statt 100 ms; Last von RefreshVision verdoppelt. | 20,21 |

**`Sim\World/SimulationWorld.WeaponSlots.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 330-332 | SimulationWorld.ConsumeShot (ready) | B/daten | Nächster Schuss = _tick + DelayBetweenShotsWith(…) bzw. ClipReloadWith(…): Tickwerte des Katalogs werden zur Laufzeit je Schuss durch den Feuerratenbonus geteilt (WeaponCatalog.ScaledByRateOfFire: floor, mindestens 1 Tick) | - | Rundung: Laufzeit-floor auf ganze Ticks und Untergrenze 1 Tick; bei 60 Hz feinere Quantisierung und doppelte Höchstfeuerrate (1 Schuss je Tick) – effektive Feuerraten mit Bonus ändern sich. | 20,23 |

**`Sim\World/SimulationWorld.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 597-607 | SimulationWorld.Submit / Validate (command.Tick) | G | Befehle sind auf absolute Ticks adressiert (_commands nach command.Tick, Late-Prüfung command.Tick < _tick, Ausführung in ExecuteCommands(tick)); der Befehlsstrom ist das Replay | eigen | Mit 30 Hz aufgezeichnete Befehlsströme treffen bei 60 Hz den halben Zeitpunkt; Aufrufer mit festem Tickvorlauf (Eingabelatenz) halbieren ihn. | 20,21 |
| 908-918 | SimulationWorld.Execute (SetTimerCommand: DueTick = _tick + set.DelayTicks, RepeatTicks) | G | Timerbefehl trägt Verzögerung und Wiederholung in Ticks (Befehlsfeld, landet im SimTimer und damit im Snapshot); Wiederholung in FireTimers Z. 1276: DueTick = tick + RepeatTicks | eigen | Timer aus Befehlsströmen/Erzeugern mit festen Tickzahlen laufen doppelt so schnell; Erzeuger müssen in der neuen Rate rechnen. | 20 |
| 1298-1318 | SimulationWorld.RunUpdates | C | Sleepy-Scheduler: Rückgabe unter SleepNone wird auf 1 Tick angehoben, wake = tick + sleep; Reihenfolge (Wecktick, Entity-Id, Modulindex) | eigen | Ergebnisse: ja – jedes Modul mit Rückgabe 1 läuft 60-mal je Sekunde, relative Schlafwerte in Ticks halbieren ihre Dauer, sofern die Module nicht skalieren; Last steigt. | 20,23 |
| 1460-1468 | SimulationWorld.ProcessSpawns | E | Spawns und Destroys wirken erst am Tickende; Spawns, die während der Spawnphase angefordert werden (Tod bei Erzeugung → Folgespawn), bleiben bis zum nächsten Tick in der Warteschlange | eigen | Jedes Kettenglied kostet einen Tick: bei 60 Hz halbe Verzögerung (16,7 ms statt 33,3 ms) je Glied. | 23 |
| 1645 | SimulationWorld.Register (NextWake = _tick + SleepNone) | E | Update-Module ohne gesetztes NextWake laufen erstmals einen Tick nach der Registrierung (Spawn am Ende von Tick t, _tick ist noch t → erstes Update in t+1) | eigen | „Ein Tick später“ ist 16,7 ms statt 33,3 ms; Reihenfolge bleibt, Anlaufverzögerung neuer Objekte halbiert sich. | 23 |
| 1662-1681 | SimulationWorld.Step | E | Feste Phasenfolge je Tick: Befehle, Timer, Updates, Zielerfassung, Türme, Feuerstufen, Ereignisse, Verkäufe, Destroys, verzögerter Schaden, Spawns, Ereignisse, Sicht (jeder 3. Tick), Tick+1, Hash | eigen | Wirkungen zwischen Phasen (Turmwinkel nach Bewegung, Schaden nach Destroy, Sicht nach Spawn) sind um genau einen Tick versetzt; dieser Versatz halbiert sich, die Reihenfolge selbst ist ratenunabhängig. | 23 |

### 5.3 Module (`Mod\`)

**`Mod\AirModules.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 79 | ParkingPlaceBehavior.HealRateTicks | A | Heilintervall des Flugfelds als const LogicFramesPerSecond / 5 = 6 Ticks (3 Verwendungen: Z. 382, 402, 403) | ParkingPlaceBehavior.cpp, Konstante HEAL_RATE_FRAMES = LOGICFRAMES_PER_SECOND / 5 | Als const an die Rate gebunden; bei parametrischer Rate muss es zur Laufzeit rate/5 werden (60 Hz: 12 Ticks), sonst Heilschritt alle 0,1 s statt 0,2 s. | 20 |
| 396-408 | ParkingPlaceBehavior.Update (return SleepNone) | C | Läuft jeden Tick: Purge toter Belegungen, Ankunftsmerker, Heiltakt über _nextHeal | ParkingPlaceBehavior.cpp, update (UPDATE_SLEEP_NONE) | Nur Last (doppelt so viele Purge-Läufe); die Ergebnisse hängen am _nextHeal-Takt, nicht am Aufrufrhythmus. | 20 |
| 399 | ParkingPlaceBehavior.Update (_arrivals.RemoveAll, > 2) | B/literal | Ankunftsmerker eines produzierten Flugzeugs verfällt nach mehr als 2 Ticks (Fenster für TryAdoptArrival) | eigen | Fenster halbiert sich zeitlich; gemeint ist Tick-Reihenfolge (Spawn im Folgetick), daher vermutlich nicht skalieren – prüfen. | 20 |
| 403 | ParkingPlaceBehavior.Update (amount) | B/daten | Heilmenge je Heilschritt = HealRateTicks · HealAmountPerSecond / LogicFramesPerSecond, zur Laufzeit je Heilereignis berechnet (Rohwert aus ModuleConfigReader.Real, Z. 50) | ParkingPlaceBehavior.cpp, update: HEAL_RATE_FRAMES * m_healAmount * SECONDS_PER_LOGICFRAME_REAL | Invariant nur, wenn HealRateTicks und Divisor gemeinsam skaliert werden; wird nur eines angepasst, verdoppelt oder halbiert sich die Heilrate. | 20 |

**`Mod\AirWeaponModules.cs`** (dazu 9 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 95-102 | AirWeaponModules.FixedTicks | B/daten | Dauer in ms als ungerundete Fixed-Ticks (DurationMillisecondsToFixedTicks) zur Parse-Zeit; Nutzer: Blast1..9Delay/ScorchDelay (Z. 699) | NeutronMissileSlowDeathUpdate.cpp, Feldtabelle BlastNDelay/BlastNScorchDelay: INI::parseDurationReal | Andere Rundung als ceil (Bruchteil-Ticks); der Vergleich in Z. 758 entscheidet die tatsächliche Auslösung. | 20 |
| 114 | EmpData.Parse "Lifetime" | B/fallback | Fallback 1 Tick für Lifetime (ModuleConfigReader.Ticks) | EMPUpdate.h, EMPUpdateModuleData-Konstruktor: m_lifeFrames = 1 | Bleibt 1 Tick = 1/60 s statt 1/30 s; nur wirksam, wenn das Feld in den Daten fehlt. | 20 |
| 163 | EmpUpdate.Update (_scale) | E | Skalierung nähert sich je Tick um 5 % dem Zielwert (Faktor 0.05 je Tick); der Wert liegt im Zustand (scale) | EMPUpdate.cpp, EMPUpdate::update: m_currentScale += ( m_targetScale - m_currentScale ) * 0.05f | Annäherung läuft in halber Zeit; betrifft Zustandshash und Darstellung des Pulses, keine Spielwirkung. | 23 |
| 230 | LeafletData.Parse "Delay" | B/fallback | Fallback 1 Tick für Delay (ModuleConfigReader.Ticks) | EMPUpdate.h, LeafletDropBehaviorModuleData-Konstruktor: m_delayFrames = 1 | Bleibt 1 Tick; zusammen mit Z. 254-259 entscheidet die Tickzahl über das Verhalten (siehe dort). | 20 |
| 254-259 | LeafletDropBehavior.Update | E | Erster Update (1 Tick nach Spawn) vor Ablauf von Delay führt zu SleepForever (Wirkung erst beim Tod); Delay von höchstens 1 Tick führt zu DoDisableAttack jeden Tick | EMPUpdate.cpp, LeafletDropBehavior::update: getFrame() < m_startFrame ergibt UPDATE_SLEEP_FOREVER | Ein Delay, das bei 30 Hz 1 Tick ergibt (bis 33 ms), wird bei 60 Hz 2 Ticks: das Modul schläft für immer statt jeden Tick zu wirken – Ergebnisänderung. | 20,23 |
| 306 | BunkerBusterData.Parse "CrashThroughBunkerFXFrequency" | B/fallback | Fallback 4 Ticks (ModuleConfigReader.Ticks) | BunkerBusterBehavior.cpp, BunkerBusterBehaviorModuleData-Konstruktor: m_crashThroughBunkerFXFrequency = 4 | Ohne Datenwert doppelt so viele Effekte je Sekunde. | 20 |
| 334 | BunkerBusterBehavior.Update (world.Tick % CrashFxFrequencyTicks == 1) | C | Einschlag-FX-Ereignis auf dem globalen Tickraster (Rest 1) | BunkerBusterBehavior.cpp, BunkerBusterBehavior::update: getFrame() % m_crashThroughBunkerFXFrequency == 1 | Ergebnisse: Zahl und Zeitpunkt der FxRequested-Ereignisse, Phase am globalen Tick; bei Frequenz 1 Tick feuert es nie (Rest 1 unmöglich). | 20,21 |
| 459 | NeutronMissileData.Parse "MaxTurnRate" (Fallback) | D | Fehlendes MaxTurnRate ergibt TwoPi·100 Radiant je Tick ohne Umrechnung (vorhandener Wert über DegreesPerSecondToRadiansPerTick) | NeutronMissileUpdate.cpp, NeutronMissileUpdateModuleData-Konstruktor: m_maxTurnRate = 999.0f | Praktisch ohne Wirkung: jeder Wert ab Pi lässt die Richtung sofort einrasten (Z. 626). | 20 |
| 463 | NeutronMissileData.Parse "ForwardDamping" | D | Roher Dämpfungsanteil der Geschwindigkeit je Tick (ModuleConfigReader.Real) | NeutronMissileUpdate.cpp, Feldtabelle INI::parseReal; doAttack: damping * m_vel je Frame | Dämpfung wirkt doppelt so oft je Sekunde: Grenzgeschwindigkeit und Einschwingzeit der Rakete ändern sich. | 20,23 |
| 464 | NeutronMissileData.Parse "RelativeSpeed" | D | Roher Schub: Geschwindigkeitszuwachs je Tick (Strecke je Tick²), Standard 1 | NeutronMissileUpdate.cpp, Konstruktor m_relativeSpeed = 1.0f, Feldtabelle INI::parseReal; doAttack | Ohne Skalierung vierfache Beschleunigung je Sekunde²; Flugbahn und Flugzeit der Rakete ändern sich. | 20,23 |
| 521-562 | NeutronMissileUpdate.Update | E | Je Tick: Zwischenpunkt gilt als erreicht bei Abstand bis Hüllkugelradius (Schwelle gegen Tickschritt); Start kostet einen Tick; _noTurnLeft sinkt um die Tickstrecke; Bodenkontakt je Tick | NeutronMissileUpdate.cpp, NeutronMissileUpdate::update | Bei anderem Tickschritt wird der Zwischenpunkt an anderer Stelle getroffen oder verfehlt; Einschlagtick und -ort verschieben sich. | 23 |
| 591-615 | NeutronMissileUpdate.DoAttack | E | Euler-Schritt: _velocity += speed·direction − ForwardDamping·_velocity, Position += _velocity (Geschwindigkeit je Tick); Sonderphase: Höhe aus elapsed/SpecialSpeedTicks | NeutronMissileUpdate.cpp, NeutronMissileUpdate::doAttack (m_accel, m_vel, pos += m_vel) | Nicht schrittinvariant: Grenzgeschwindigkeit speed/damping gilt je Tick, bei 60 Hz also doppelt so schnell je Sekunde; die Sonderphase selbst ist zeitrichtig. | 20,23 |
| 739-741 | NeutronMissileSlowDeathBehavior.BeginSlowDeath | B/daten | Zufallszüge Range(0, VarianzTicks) auf Tickbasis für Sink- und Zerstörungsverzögerung zur Laufzeit, mindestens 1 Tick | Herkunft unbelegt | Wertebereich des Zufallszugs verdoppelt sich: derselbe Zug liefert eine andere Verzögerung. | 20,23 |
| 758 | NeutronMissileSlowDeathBehavior.Update (Tick − _activationTick > DelayTicks) | B/daten | Blaststufe löst aus, wenn die vergangenen ganzen Ticks strikt größer als die ungerundete Verzögerung sind (wirkt wie floor + 1) | NeutronMissileSlowDeathUpdate.cpp, update: currFrame - m_activationFrame > m_blastInfo[ i ].delay | Andere Rundung als ceil: Auslösung bis zu einem Tick nach der Sollzeit; bei 60 Hz kleinerer Versatz, Stufen mit engem Abstand können auf andere Ticks fallen. | 20 |
| 894 | MissileLauncherBuildingUpdate.SwitchTo (AvailableOnFrame − 1) | A | Tür ist 1 Tick vor der Bereitschaft der Spezialkraft offen (Literal 1) | MissileLauncherBuildingUpdate.cpp, switchToState: m_timeoutFrame = m_specialPowerModule->getReadyFrame() - 1 | Vorlauf halbiert sich zeitlich; gemeint ist „ein Frame davor“, also nicht skalieren. Keine Spielwirkung (nur Türzustand und FX). | 20 |
| 958 | SpectreData.Parse "HowitzerFiringRate" | B/fallback | Fallback 10 Ticks, zusätzlich Math.Max(…, 1) | SpectreGunshipUpdate.cpp, SpectreGunshipUpdateModuleData-Konstruktor: m_howitzerFiringRate = 10 | Ohne Datenwert doppelte Zielsuch- und Haubitzenrate je Sekunde. | 20 |
| 962 | SpectreData.Parse "StrafingIncrement" | D | Roher Weg des Gatling-Zielpunkts je Tick (Standard 20), verwendet in Strafe() | SpectreGunshipUpdate.cpp, Konstruktor m_strafingIncrement = 20.0f, Feldtabelle INI::parseReal; update (delta.scale) | Zielpunkt wandert doppelt so schnell je Sekunde; die Haubitze setzt früher ein. | 20,23 |
| 1129-1140 | SpectreGunshipUpdate.Fly | E | Kinematischer Euler-Schritt je Tick: Drehung um TurnRatePerTick, Speed ± Acceleration/Braking, Position += Speed; Ziel je Tick neu; Umstieg auf Orbit bei Abstand < Radius (Z. 1076) | eigen | Bahn ist ein Polygonzug: anderer Tickschritt ändert Orbitbahn, Eintrittstick und damit die Schussfolge. | 23 |
| 1151 | SpectreGunshipUpdate.Orbit (world.Tick % HowitzerFiringRateTicks == 0) | C | Zielsuche und Haubitzenschuss auf dem globalen Tickraster; je Schuss 2 Zufallszüge | SpectreGunshipUpdate.cpp, update: getFrame() % m_howitzerFiringRate < ONE | Ergebnisse: Zahl der Schüsse und Zufallszüge hängt an der Tickzahl des Intervalls und an der Phase zum globalen Tick. | 20,23 |
| 1162 | SpectreGunshipUpdate.Orbit (_okToFireHowitzerCounter > HowitzerFollowLagTicks) | E | Zähler steigt je Strafe-Tick um 1 und wird mit einer Dauer in Ticks verglichen | SpectreGunshipUpdate.cpp, update: m_okToFireHowitzerCounter > data->m_howitzerFollowLag | Zeitrichtig nur, solange Strafe() wirklich jeden Tick läuft (hängt an Z. 1176); sonst verzögert sich die Haubitze. | 23 |
| 1176 | SpectreGunshipUpdate.Orbit (Tick − LastFireTick <= 2) | B/literal | „Gatling feuert gerade“ = letzter Schuss höchstens 2 Ticks her (Ersatz für OBJECT_STATUS_IS_FIRING_WEAPON des Originals) | eigen | Fenster halbiert sich: bei Schussabständen über 2 Ticks (60 Hz) setzt das Strafen aus, Zähler und Haubitze laufen anders. | 20,23 |
| 1183-1198 | SpectreGunshipUpdate.Strafe | E | Zielpunkt rückt je Tick um StrafingIncrement vor; angekommen, wenn der Restweg kleiner als ein Tickschritt ist | SpectreGunshipUpdate.cpp, update: dist < data->m_strafingIncrement | Ankunftsradius ist ein Tickschritt: ohne Skalierung doppelte Geschwindigkeit, mit Skalierung halber Radius. | 23 |

**`Mod\BattlePlanModule.cs`** (dazu 5 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 110-135 | BattlePlanUpdate.Update | E | Zustandsmaschine: ein Übergang je Tick, gesperrt bis _nextReadyTick; Packing→Idle→Unpacking kosten zusätzlich je 1 Tick | BattlePlanUpdate.cpp, update gibt UPDATE_SLEEP_NONE zurück (Übergangslogik selbst nicht gegengeprüft) | Die Zusatzticks zwischen den Phasen halbieren sich zeitlich; ein Planwechsel endet wenige Ticks früher. | 23 |

**`Mod\BridgeModules.cs`** (dazu 1 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 29 | BridgeData.Parse "LateralScaffoldSpeed" | D | Rohwert / LogicFramesPerSecond von Hand (nicht PerSecondToPerTick); Fallback 1 je Sekunde = 1/30 je Tick | BridgeBehavior.cpp, Feldtabelle INI::parseVelocityReal; Konstruktor m_lateralScaffoldSpeed = 1.0f (je Frame, unkonvertiert) | Handdivision muss mitparametrisiert werden; der Fallback weicht schon heute vom Original (1.0 je Frame) ab. | 20 |
| 30 | BridgeData.Parse "VerticalScaffoldSpeed" | D | Rohwert / LogicFramesPerSecond von Hand (nicht PerSecondToPerTick); Fallback 1 je Sekunde = 1/30 je Tick | BridgeBehavior.cpp, Feldtabelle INI::parseVelocityReal; Konstruktor m_verticalScaffoldSpeed = 1.0f (je Frame, unkonvertiert) | Handdivision muss mitparametrisiert werden; der Fallback weicht schon heute vom Original (1.0 je Frame) ab. | 20 |
| 248 | BridgeScaffoldBehavior.AbsoluteMinSpeed | D | Mindestgeschwindigkeit 0.001 je Tick | BridgeScaffoldBehavior.cpp, update: if( speed < 0.001f ) speed = 0.001f | Doppelte Mindestgeschwindigkeit je Sekunde; praktisch bedeutungslos. | 20 |
| 256-257 | BridgeScaffoldBehavior._lateralSpeed/_verticalSpeed | D | Standardgeschwindigkeit 1 je Tick, bis SetSpeeds aufgerufen wird; als je-Tick-Wert im Zustand | BridgeScaffoldBehavior.cpp, Konstruktor: m_lateralSpeed = 1.0f, m_verticalSpeed = 1.0f | Ohne SetSpeeds doppelt so schnell je Sekunde; gespeicherte Werte sind je Tick. | 20 |
| 291-333 | BridgeScaffoldBehavior.Update | E | Je Tick: Position += Richtung·speed; speed im letzten Viertel proportional zum Restweg (mindestens 8 %); Ziel erreicht beim Überschreiten | BridgeScaffoldBehavior.cpp, BridgeScaffoldBehavior::update (minSpeed = topSpeed * 0.08f) | Abbremsrampe ist schrittabhängig (geometrisches Abklingen je Tick); der Ankunftstick ändert sich. Gerüste werden derzeit von nichts angetrieben. | 23 |

**`Mod\CleanupAndSpyModules.cs`** (dazu 6 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 98-104 | CleanupHazardUpdate.Update (_nextScan) | C | Abwärtszähler je Tick, Nachladen mit ScanTicks: ein Scan alle ScanTicks + 1 Ticks | CleanupHazardUpdate.cpp, update: m_nextScanFrames-- und m_nextScanFrames = data->m_scanFrames | Ergebnisse: Zielwahl; Periode 2N + 1 statt 2(N + 1) Ticks, also etwas kürzer in Zeit. | 20,23 |
| 129 | CleanupHazardUpdate.FireWhenReady (LogicRandom.Range(0, 3)) | A | Zufällige Wartezeit 0..3 Ticks bis zum Neuscan, wenn das Ziel aus der Reichweite gerät (ein Zufallszug je Ereignis) | CleanupHazardUpdate.cpp, fireWhenReady: m_nextScanFrames = GameLogicRandomValue( 0, 3 ) | Wartezeit halbiert sich zeitlich; bei Skalierung auf 0..6 ändert sich die Verteilung des Zufallszugs. | 20,23 |
| 426 | SpyVisionSpecialPower.Apply | B/daten | Dauer zur Laufzeit: BaseDurationTicks + ContainCount · BonusPerCapturedTicks, gedeckelt auf MaxDurationTicks | Herkunft unbelegt | Die Aufrundung je Bonus wird mit der Insassenzahl vervielfacht; bei 60 Hz kleinerer Rundungsfehler, also leicht andere Gesamtdauer. | 20 |
| 505 | AssistedTargetingUpdate.ReloadWait | C | Obergrenze 1000 Ticks für einen Schlaf, während der Slot nachlädt (2 Verwendungen: Z. 498, 501) | eigen | Halbiert sich zeitlich (16,7 s); überwiegend Last, bestimmt aber, wann eine hängende Anfrage (Opfer weg) endet und das Modul wieder frei ist. | 20 |

**`Mod\CombatModules.cs`** (dazu 2 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 65-81 | FireWeaponUpdate.Update | C | Pollt jeden Tick (SleepNone) gegen _initialDelayTick, _readyTick und ExclusiveWeaponDelay | FireWeaponUpdate.cpp, update (UPDATE_SLEEP_NONE) | Nur Last; die Schusszeitpunkte stehen als absolute Ticks aus Datendauern. | 20 |

**`Mod\ContainChainModules.cs`** (dazu 1 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 28-51 | ContainChainModules.RequestPayload/EnterPayload | E | Anfangsnutzlast entsteht im Spawn-Tick und steigt erst im Folgetick ein (Reihenfolge „ein Tick später“); betrifft RiderChangeContain und OverlordContain | eigen | Das Fenster ohne Insassen halbiert sich zeitlich; die Reihenfolge bleibt. | 23 |
| 380-391 | OverlordContain.Update | C | Aufbau folgt Position und Richtung des Overlord jeden Tick (SleepNone, solange umgeleitet) | eigen | Nur Last; der Nachlauf des Aufbaus beträgt höchstens einen Tick und halbiert sich zeitlich. | 20 |

**`Mod\ContainModules.cs`** (dazu 1 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 280-286 | GarrisonContain.HealOne (TimeForFullHealTicks) | E | Heilung je Tick = MaxHealth / TimeForFullHealTicks, jeden Tick (SleepNone); voller Sprung, sobald Tick − since >= TimeForFullHealTicks | GarrisonContain.cpp healSingleObject (m_framesForFullHeal, getMaxHealth() / framesForFullHeal) | Skaliert mit, wenn TimeForFullHealTicks zur Rate umgerechnet wird (nur Ceil-Rundung der Dauer); bleibt die Tickzahl 30er-basiert, heilt es doppelt so schnell. | 20,23 |
| 406-408 | TransportContain.Update (HealthRegen%PerSec, perTick) | E | Regeneration je Tick = MaxHealth · Prozent / 100 / LogicFramesPerSecond, zur Laufzeit jeden Tick berechnet (SleepNone) | TransportContain.cpp (regen = getMaxHealth() * m_healthRegen / 100.0f * SECONDS_PER_LOGICFRAME_REAL) | Folgt der Konstante LogicFramesPerSecond; solange die 30 bleibt, doppelte Heilrate. Mit Parameter invariant bis auf Fixed-Rundung der kleineren Scheibe. | 20 |

**`Mod\CrateModules.cs`** (dazu 2 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 544 | SabotageMilitaryFactoryCrateCollide.Sabotage (return data.DurationTicks) | B/daten | Die Sabotagedauer wird als Tickzahl im Ereigniswert (SimEventKind.Sabotaged / CrateCollected) nach außen gegeben | eigen | Verbraucher des Ereignisses erhalten eine doppelt so große Zahl für dieselbe Zeit; wer sie mit 30 in Sekunden umrechnet, zeigt das Doppelte. | 21 |
| 561 | SabotagePowerPlantCrateCollide.Sabotage (return data.DurationTicks) | B/daten | Die Sabotagedauer (SabotagePowerDuration) wird als Tickzahl im Ereigniswert nach außen gegeben | eigen | Wie beim Fabrik-Saboteur: Ereigniswert in Ticks, Verbraucher außerhalb der Simulation müssen die Rate kennen. | 21 |
| 608-611 | PilotFindVehicleUpdate.Update (scan = Max(ScanTicks, SleepNone)) | C | Scan-Rhythmus aus ScanRate (ms, Ceil); bei ScanRate 0 (Standard) Scan in jedem Tick | Herkunft unbelegt | Mit Datenwert zeitinvariant; bei ScanRate 0 doppelt so viele Scans je Sekunde – Ergebnis: Pilot reagiert bis zu 1/60 s früher, sonst nur Last. | 20 |

**`Mod\DieAndUpdateModules.cs`** (dazu 2 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 181-182 | TimedExpiryModule.OnSpawn (delay, _minTicks/_maxTicks) | B/daten | Lebensdauer wird als Zufallszug über den Tickbereich [MinTicks, MaxTicks] gezogen (Ganzzahl, inklusiv), Minimum 1 Tick | LifetimeUpdate.cpp LifetimeUpdate::calcSleepDelay (GameLogicRandomValue(minFrames, maxFrames); if (delay < 1) delay = 1) | Zug über den doppelten Wertebereich: andere gezogene Werte und feinere Auflösung, also andere Zeitpunkte; Minimum 1 Tick wird 1/60 s. | 20,23 |

**`Mod\DockMachine.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 25 | DockMachine.ClearanceTimeoutTicks | B/literal | Wartezeit auf Einfahrtfreigabe 30 s als const 30L * LogicFramesPerSecond (= 900 Ticks), 1 Verwendung (Zeile 167, _enterTick + Timeout < world.Tick) | AIDock.cpp AIDockWaitForClearanceState::update (m_enterFrame + 30*LOGICFRAMES_PER_SECOND < getFrame()) | Als const an die Rate gebunden: muss Laufzeitwert werden, sonst 15 s statt 30 s, wenn die Konstante 30 bleibt. | 20 |

**`Mod\DockModules.cs`** (dazu 1 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 253-277 | DockUpdateBase.Update (SleepNone solange belegt) | C | Dock prüft jeden Tick seine Plätze und ernennt den aktiven Docker; der Docker sieht die Freigabe erst in seinem nächsten Update | - | Ergebnis: Freigabe-Latenz von einem Tick halbiert sich in Echtzeit (1/60 s), sonst nur Last. | 23 |
| 494 | AutoDepositUpdate.Update (Math.Max(DepositTimingTicks, SleepNone)) | C | Einzahlungsintervall aus DepositTiming (ms, Ceil); bei DepositTiming 0 (Standard) wird jeden Tick eingezahlt | Herkunft unbelegt | Mit Datenwert zeitinvariant (Ceil-Rundung); bei DepositTiming 0 doppelt so viel Geld je Sekunde – Ergebnisänderung. | 20 |

**`Mod\FerryModules.cs`** (dazu 2 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 351 | RailedTransportDockUpdate.Action (_pullPerTick, Math.Max(PullInsideTicks, 1)) | E | Zugstrecke je Tick = Anfangsabstand / PullInsideTicks; Dauer 0 wird als 1 Tick behandelt | RailedTransportDockUpdate.cpp (m_pullInsideDistancePerFrame = mag / m_pullInsideDurationInFrames) | Invariant, wenn die Dauer zur Rate umgerechnet wird; die 1-Tick-Untergrenze entspricht dann 1/60 s. | 20,23 |
| 385-407 | RailedTransportDockUpdate.DoPullInDocking | E | Euler-Schritt je Tick: Docker wird um _pullPerTick zum Fährenmittelpunkt gezogen, bis Abstand² <= 36 | RailedTransportDockUpdate.cpp (v.x *= m_pullInsideDistancePerFrame) | Halbe Schrittweite, Ankunft (6 Einheiten) kann einen Tick früher/später fallen; gespeichertes pullPerTick ist ratengebunden. | 23 |
| 427-452 | RailedTransportDockUpdate.DoPushOutDocking | E | Euler-Schritt je Tick: Reiter wird um min(_pushPerTick, Rest) zum Ausstiegspunkt geschoben, bis Abstand² <= 9 | RailedTransportDockUpdate.cpp (v.x *= m_pushOutsideDistancePerFrame) | Halbe Schrittweite, Ankunft (3 Einheiten) verschiebt sich um Bruchteile; gespeichertes pushPerTick ist ratengebunden. | 23 |
| 481 | RailedTransportDockUpdate.UnloadNext (_pushPerTick, Math.Max(PushOutsideTicks, 1)) | E | Schubstrecke je Tick = Abstand zum Ausstiegspunkt / PushOutsideTicks; Dauer 0 wird als 1 Tick behandelt | RailedTransportDockUpdate.cpp (m_pushOutsideDistancePerFrame = mag / m_pushOutsideDurationInFrames) | Invariant, wenn die Dauer zur Rate umgerechnet wird; 1-Tick-Untergrenze wird 1/60 s. | 20,23 |

**`Mod\FieldEffectModules.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 32 | PropagandaTowerData.Parse (DelayBetweenUpdates, Fallback 100) | B/fallback | Fallback 100 Ticks für das Scanintervall, wenn DelayBetweenUpdates fehlt (ModuleConfigReader.Ticks(…, 100)); dient auch als Laufzeit des Heil-Anspruchs | PropagandaTowerBehavior.cpp PropagandaTowerBehaviorModuleData (m_scanDelayInFrames = 100, parseDurationUnsignedInt) | Fallback bleibt 100 Ticks = 1,67 s statt 3,33 s; Scan und Anspruchsdauer halbieren sich, wo der Schlüssel fehlt. | 20 |
| 168 | PropagandaTowerBehavior.Give (HealPerSecond / LogicFramesPerSecond) | E | Heilung je Tick = Rate je Sekunde / LogicFramesPerSecond · MaxHealth, zur Laufzeit jeden Tick je Objekt | PropagandaTowerBehavior.cpp PropagandaTowerBehavior::effectLogic (healthPercent / LOGICFRAMES_PER_SECOND * body->getMaxHealth()) | Folgt der Konstante; bleibt sie 30, doppelte Heilrate. Mit Parameter invariant bis auf Fixed-Rundung. | 20 |
| 273 | TensileFormationUpdate.Lifetime | A | Gleitdauer der Lawine 300 Ticks (++_life > Lifetime, 1 Verwendung); _life zählt Updates, nicht Zeit | TensileFormationUpdate.cpp TensileFormationUpdate::update (if (++m_life > 300)) | Die Lawine rutscht nur 5 s statt 10 s; Life wird zudem von der Präsentation gelesen (Schwelle 100 laut Kommentar). | 20,21 |
| 274 | TensileFormationUpdate.Friction | D | Reibungsfaktor 0,95 je Tick, multiplikativ auf die Trägheit (Zeilen 319–320) | TensileFormationUpdate.cpp TensileFormationUpdate::update (Real friction = 0.95f) | Dämpfung wirkt doppelt so oft je Sekunde: andere Endgeschwindigkeit und Rutschstrecke. | 20,23 |
| 275-276 | TensileFormationUpdate.NewShare / LinkShare | D | Zug zum Nachbarn 7 % je Verbindung und Tick (0,93 / 0,07), Mischfaktor je Update | TensileFormationUpdate.cpp TensileFormationUpdate::update (newPos.x*0.93f + desiredPos.x*0.07f) | Die Formation zieht sich doppelt so schnell zusammen; Bahnen der Felsen ändern sich. | 20,23 |
| 277 | TensileFormationUpdate.BaseSlope | D | Hangbeschleunigung je Tick: inertia += Normal · (0,3 + Steilheit), ohne Zeitumrechnung | TensileFormationUpdate.cpp TensileFormationUpdate::update (slope.scale( 0.3f + steepness)) | Beschleunigung wird doppelt so oft je Sekunde addiert; zusammen mit der Reibung andere Geschwindigkeit. | 20,23 |
| 310 | TensileFormationUpdate.Update (return 30) | A | Schlafintervall 30 Ticks des ruhenden Felsens zwischen zwei Prüfungen auf Schaden/Anstoß | TensileFormationUpdate.cpp TensileFormationUpdate::update (return UPDATE_SLEEP(30)) | Prüfung alle 0,5 s statt 1 s: die Lawine löst im Mittel früher aus (Ergebnis, nicht nur Last). | 20 |
| 314 | TensileFormationUpdate.Update (_life % 30 == 29) | A | Weitergabe des Anstoßes an Nachbarn alle 30 gleitenden Ticks (erstmals nach 29) | TensileFormationUpdate.cpp TensileFormationUpdate::update (if ( m_life%30 == 29 )) | Ausbreitung der Lawine doppelt so schnell in Echtzeit, bei halber Gleitdauer aber gleich viele Weitergaben. | 20 |
| 316-336 | TensileFormationUpdate.Update (Physikblock) | E | Euler-Schritt je Tick: Trägheit += Hang, · Reibung, Position += Trägheit (Einheiten je Tick), danach Mischung mit den Verbindungen und Bodenhöhe | TensileFormationUpdate.cpp TensileFormationUpdate::update | Nicht schrittinvariant: bei 60 Hz andere Bahn, Geschwindigkeit und Endlage jedes Felsens; inertiaX/Y sind je Tick gespeichert. | 23 |

**`Mod\FlightModules.cs`** (dazu 15 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 127 | JetSlowDeathBehavior.BeginSlowDeath (SinkDelayVarianceTicks) | B/daten | Zufallszug über 0..SinkDelayVarianceTicks (Ganzzahl in Ticks); Ergebnis wird verworfen, der Zug zählt für den Zufallsstrom | Herkunft unbelegt | Zug über doppelten Bereich: anderer Zustand des Zufallsstroms als bei 30 Hz. | 20,23 |
| 128 | JetSlowDeathBehavior.BeginSlowDeath (DestructionDelayVarianceTicks) | B/daten | Zerstörungszeitpunkt = DestructionDelayTicks + Zufallszug über 0..VarianceTicks (Ganzzahl in Ticks), Minimum 1 Tick | Herkunft unbelegt | Zug über doppelten Bereich: anderer Zerstörungszeitpunkt und Zufallsstrom; Minimum wird 1/60 s. | 20,23 |
| 142-155 | JetSlowDeathBehavior.Update (Fallblock) | E | Euler je Tick: X/Y += Richtung · Entity.Speed (je Tick), _fallSpeed += GravityPerTickSquared · FallHowFast, Z += _fallSpeed; Aufschlag bei Z <= Boden | eigen | Halbe Schritte: Aufschlagzeit und -ort weichen leicht ab (halbimplizites Euler); fallSpeed ist je Tick gespeichert. | 23 |
| 291 | CountermeasuresBehavior.ReportMissile (Math.Max(ReactionTicks, 1)) | B/literal | Reaktionslatenz mindestens 1 Tick (ReactionLaunchLatency 0 wird 1 Tick), weil 0 als „nicht gesetzt“ dient | eigen | Untergrenze wird 1/60 s statt 1/30 s; erste Salve bei Latenz 0 einen halben alten Tick früher. | 20 |
| 345-369 | CountermeasuresBehavior.MoveFlares | E | Je Tick: Flare-Geschwindigkeit −= Locomotor.Braking (linear je Tick), Position += Geschwindigkeit (je Tick), Z −= SpeedLimitZ (je Tick) | eigen | Hängt an den je-Tick-Werten des Locomotors; lineares Abbremsen je Schritt ergibt bei 60 Hz leicht andere Flare-Bahn; vx/vy je Tick gespeichert. | 23 |
| 387 | CountermeasuresBehavior.LaunchVolley (speed, 1 und −10) | B/literal | Schwelle „Tempo < 1“ und Ersatzwert −10, hier als Einheiten je Sekunde gedeutet und zur Laufzeit über LogicFramesPerSecond in je-Tick umgerechnet | CountermeasuresBehavior.cpp CountermeasuresBehavior::launchVolley (velocity < 1.0f → velocity = -10.0f; Wert aus physics->getVelocityMagnitude() ohne Umrechnung) | Folgt der Konstante; bleibt sie 30, verschieben sich Schwelle und Rückwärtswurf. Die Referenz rechnet nicht um – Einheit der Schwelle prüfen. | 20 |
| 524-533 | ParachuteContain.Update (freier Fall) | E | Euler je Tick: _fallSpeed += GravityPerTickSquared, Z += _fallSpeed; Öffnen, sobald \|startZ − Z\| >= ParachuteOpenDist | eigen | Halbe Schritte: Öffnungshöhe und -zeitpunkt weichen um bis zu einen Schritt ab; fallSpeed je Tick gespeichert. | 23 |
| 534-549 | ParachuteContain.Update (Sinkflug und Steuern) | E | Je Tick: Z += −SpeedLimitZ, X/Y += Locomotor.Speed zum Ziel; Ankunftsradius „eine Tickstrecke“ (distance <= locomotor.Speed) | eigen | Ankunftsradius halbiert sich mit der je-Tick-Geschwindigkeit; Landezeitpunkt verschiebt sich um Bruchteile eines Ticks. | 23 |
| 850-870 | DeliverPayloadAIUpdate.Strafe | C | Während des Sturzflugs wird in jedem Update ein Schuss auf den wandernden Zielpunkt angefordert (FireCurrentWeaponAtPosition); ob die Waffe selbst taktet, liegt außerhalb der Datei | Herkunft unbelegt | Doppelt so viele Feuerversuche je Sekunde; Ergebnis ändert sich, falls die Waffe keinen eigenen Zeittakt erzwingt (nicht geprüft). | 20,23 |
| 875-881 | DeliverPayloadAIUpdate.RunDelivering (_dropDelayLeft) | B/daten | Tür-/Abwurfverzögerung als Zähler, der je Update um 1 sinkt (DoorDelayTicks, danach DropDelayTicks); Abwurf im Update nach Ablauf, also Delay + 1 Updates | DeliverPayloadAIUpdate.cpp (m_dropDelayLeft = getDoorDelay(); --m_dropDelayLeft; m_dropDelayLeft = getDropDelay()) | Dauer skaliert mit umgerechneten Ticks, der zusätzliche Tick je Abwurf wird 1/60 s: Abwurfabstände minimal kürzer, Abwurforte verschieben sich. | 20,23 |
| 965-969 | DeliverPayloadAIUpdate.TurnTicks | B/daten | Wartezeit außerhalb der Karte zur Laufzeit berechnet: Ceil(MinTurnRadius / Locomotor.Speed) Ticks, mindestens 1; MinTurnRadius = Speed / TurnRatePerTick | DeliverPayloadAIUpdate.cpp DeliverPayloadAIUpdate::calcMinTurnRadius (timeToTravelThatDist) | Invariant, sofern Speed und TurnRatePerTick zur Rate umgerechnet sind; Ceil und Minimum 1 Tick runden feiner. | 20 |
| 1071 | ParticleUplinkData.Parse (ManualDrivingSpeed) | B/daten | Strahltempo je Sekunde wird beim Parsen von Hand durch LogicFramesPerSecond geteilt (nicht über SimulationTiming.PerSecondToPerTick) | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (speed /= LOGICFRAMES_PER_SECOND) | Folgt der Konstante; eigene Division wird von einer Umstellung der SimulationTiming-Funktionen nicht erfasst. | 20 |
| 1072 | ParticleUplinkData.Parse (ManualFastDrivingSpeed) | B/daten | Schnelles Strahltempo je Sekunde wird beim Parsen von Hand durch LogicFramesPerSecond geteilt | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (speed /= LOGICFRAMES_PER_SECOND) | Wie ManualDrivingSpeed: eigene Division, muss mit umgestellt werden. | 20 |
| 1168 | ParticleUplinkCannonUpdate.Initiate (world.Tick − _pendingPathTick <= 3) | B/literal | Angebot eines Skript-Wegpunktpfads verfällt nach 3 Ticks, wenn kein Einsatz folgt | eigen | Fenster wird 0,05 s statt 0,1 s; kommt der Skriptbefehl in Ticks gemessen später an, verfällt der Pfad und der Strahl schwenkt statt zu fahren. | 20 |
| 1235 | ParticleUplinkCannonUpdate.Update (_nextScorch) | B/daten | Nächste Brandspur = birth + (death − birth) · n / TotalScorchMarks, Ganzzahldivision auf Ticks (abgerundet) | Herkunft unbelegt | Zeitlich invariant bei umgerechneten Dauern; feinere Abrundung verschiebt einzelne FX-Zeitpunkte um Bruchteile. | 20 |
| 1241 | ParticleUplinkCannonUpdate.Update (_nextPulse) | B/daten | Nächster Schadenspuls = birth + (death − birth) · n / TotalDamagePulses, Ganzzahldivision auf Ticks (abgerundet) | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (m_nextDamagePulseFrame = orbitalBirthFrame + nextFactor * (orbitalDeathFrame - orbitalBirthFrame)) | Pulszeitpunkte verschieben sich durch feinere Abrundung; Pulszahl bleibt, der Strahl steht bei gefahrenem Strahl an leicht anderer Stelle. | 20,23 |
| 1247 | ParticleUplinkCannonUpdate.Update (Math.Max(LaunchFxTicks, 1)) | C | Start-FX alle DelayBetweenLaunchFX (ms, Ceil), mindestens jeden Tick; bei 0 ein FX-Ereignis je Tick | Herkunft unbelegt | Mit Datenwert invariant; bei 0 doppelt so viele FX-Ereignisse je Sekunde (Ergebnis nur in der Präsentation). | 20,21 |
| 1298-1303 | ParticleUplinkCannonUpdate.MoveBeam (manueller Zweig) | E | Je Tick: Strahl fährt um min(Tempo je Tick, Rest) zum Ziel; Wegpunktwechsel, wenn Rest <= eine Tickstrecke | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (speed /= LOGICFRAMES_PER_SECOND) | Wegpunktwechsel-Radius halbiert sich; je Wegpunkt geht ein Tick statt 1/30 s verloren, Bahn minimal anders. | 23 |
| 1311 | ParticleUplinkCannonUpdate.Pulse (seconds = TotalFiringTicks / LogicFramesPerSecond) | B/daten | Rückrechnung der bereits auf Ticks aufgerundeten Feuerdauer in Sekunden, zur Laufzeit je Puls; Schaden je Puls = Sekunden · DamagePerSecond / Pulse | ParticleUplinkCannonUpdate.cpp ParticleUplinkCannonUpdate::update (totalFiringSeconds = m_totalFiringFrames / LOGICFRAMES_PER_SECOND) | Folgt der Konstante; Ceil auf feinere Ticks ändert den Schaden je Puls geringfügig, wenn die Dauer kein Vielfaches von 1/30 s ist. | 20 |

**`Mod\HackerAndAssaultModules.cs`** (dazu 4 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 97-101 | HackInternetAIUpdate.Varied | B/daten | Laufzeit-Arithmetik auf bereits (ceil) umgerechneten Ticks: UnpackTicks/PackTicks × Zufallsfaktor [1−v, 1+v], danach Floor; ein Logikzufallszug je Aufruf (Aufrufe Z. 92, 117) | HackInternetAIUpdate.cpp, UnpackingState::onEnter / PackingState::onEnter (m_framesRemaining = getUnpackTime() * variation) | Doppelte Rundung (ceil beim Parsen, floor nach dem Zufallsfaktor) auf anderer Tick-Körnung: Dauer in Sekunden weicht leicht ab, Ergebnis ist nicht exakt 2× der 30-Hz-Ticks. | 20,23 |
| 139-161 | HackInternetAIUpdate.Update (_remaining) | E | Countdown je Update-Aufruf nach dem Muster „>0 dekrementieren, sonst Übergang/Auszahlung“: jede Phase (Unpack, Cash-Intervall, Pack) dauert N+1 Ticks; läuft nur, weil Z. 162 SleepNone liefert | HackInternetAIUpdate.cpp, UnpackingState::update / HackInternetState::update / PackingState::update (if m_framesRemaining > 0: m_framesRemaining--) | Bei verdoppeltem N dauert ein Zyklus 2N+1 statt 2N+2 Ticks: Auszahlungsintervall etwas kürzer, Hacker-Einkommen je Sekunde steigt leicht. | 20,23 |
| 263-298 | AssaultTransportAIUpdate.Update | C | Schläft nie (SleepNone): Mitgliederliste, Deploy-Erkennung (LastFireTick != _lastShot) und Mitgliederbefehle laufen jeden Tick; keine Tick-Literale | AssaultTransportAIUpdate.cpp, update (return UPDATE_SLEEP_NONE) | Nur Last und Reaktionslatenz (ein Tick = halbe Zeit); Ergebnisse höchstens über die um Bruchteile frühere Reaktion. | 20 |

**`Mod\JetAIUpdate.Flight.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 42-65 | JetAIUpdate.CircleStep | E | Kreisflug: jeden Tick neuer Zielpunkt (π−π/8 um den Kreis voraus) und SteerWinged mit MinSpeed je Tick; der Doku-Messwert „Bahn ≈ 0,93·Radius“ ist ein Ergebnis der 30-Hz-Schrittweite | Locomotor.cpp, Locomotor::maintainCurrentPositionWings (aimDir = PI − PI/8) | Kleinere Schritte ändern die eingeschwungene Kreisbahn (Radius, Position, Hash); die gemessenen 0,93 gelten nicht mehr. | 23 |

**`Mod\JetAIUpdate.cs`** (dazu 3 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 251-283 | JetAIUpdate.TaxiAlongPath | E | Rollen per Euler je Tick: Drehen um TurnRatePerTick, Schritt = Speed je Tick, Ankunftsradius „ein Tick Weg“ (mindestens 1 Einheit, Z. 261) mit Einrasten auf den Punkt, Drehen im Stand bis π/6 | eigen (Kinematik statt Physik, Klassenkommentar) | Ankunftsradius halbiert sich bis zur Untergrenze 1; Einrasten und Drehen im Stand geschehen an anderen Positionen/Ticks, Rollzeit weicht ab. | 23 |
| 257 | JetAIUpdate.TaxiAlongPath (taxi?.Speed ?? Fixed.One) | D | Ersatzgeschwindigkeit 1 Einheit je Tick, wenn kein Locomotor aktiv ist | eigen | Unskaliert doppelte Rollgeschwindigkeit in Sekunden (60 statt 30 Einheiten/s). | 20 |
| 286-293 | JetAIUpdate.TurnTo | E | Drehen im Stand um TurnRatePerTick je Tick, Einrasten wenn Rest ≤ Schritt, Toleranz 0,001 rad (Aufrufe Z. 541, 855) | eigen | Drehdauer bis auf den letzten Teilschritt gleich; der Abschluss-Tick (Start der Startpause/des Nachladens) verschiebt sich um Bruchteile. | 23 |
| 441 | JetAIUpdate.Update (return SleepNone) | C | Die ganze Jet-Zustandsmaschine läuft jeden Tick (auch geparkt und beim Nachladen) | JetAIUpdate.cpp, JetAIUpdate::update (return UPDATE_SLEEP_NONE) | Ergebnisrelevant: alle Integrationsschritte dieser Datei hängen am Aufruf je Tick; zusätzlich doppelte Last. | 20,23 |
| 447 | JetAIUpdate.UpdateAttackLocomotor (Math.Max(AttackLocomotorPersistTicks, 1)) | B/literal | Mindestdauer 1 Tick für das Angriffs-Locomotorset; Auslöser ist LastFireTick == now (Tick-Gleichheit) statt Status IS_ATTACKING | eigen (Referenz JetAIUpdate.cpp, JetAIUpdate::update: m_attackLocoExpireFrame = now + m_attackLocoPersistTime ohne Mindestwert) | Mindestdauer wird 1/60 s; nur bei AttackLocomotorPersistTime = 0 sichtbar. | 20 |
| 551 | JetAIUpdate.PauseBeforeTakeoff (_transferTick = now + 1) | E | Startbahn wird 1 Tick nach Beginn des Countdowns weitergegeben (Reihenfolge-Verzögerung in Ticks) | JetAIUpdate.cpp, JetPauseBeforeTakeoffState::update (m_whenTransfer = now + 1) | Bleibt 1 Tick = halbe Zeit; ist eine Reihenfolge-, keine Zeitangabe – Skalieren würde die Staffelung der Starts ändern. | 23 |
| 553 | JetAIUpdate.PauseBeforeTakeoff (_transferTick = now + 2) | E | Startbahnübergabe 2 Ticks später, wenn auf ein anderes Flugzeug gewartet wurde | JetAIUpdate.cpp, JetPauseBeforeTakeoffState::update (m_whenTransfer = now + 2; „2 seems odd, but is correct“) | Wie Z. 551: Tick-Reihenfolge, halbe Zeit bei 60 Hz. | 23 |
| 608-616 | JetAIUpdate.TakeOff (Hubschrauberzweig) | E | Senkrechtstart: Z += max(SpeedLimitZ, Speed/4) je Tick bis Vorfeldhöhe + ApproachHeight | eigen | Skaliert mit den Per-Tick-Werten; nur das Kappen am Ziel (letzter Teilschritt) fällt auf einen anderen Zeitpunkt. | 23 |
| 617-645 | JetAIUpdate.TakeOff (Startlauf) | E | Startlauf: Speed += Acceleration, danach Position += Speed (semi-implizites Euler), Wegpunktwechsel/Ende bei Abstand ≤ max(Speed, 1) (ein Tick Weg, mind. 1 Einheit); Höhe als Funktion der Strecke | eigen; Höhenformel nach JetAIUpdate.cpp, JetTakeoffOrLandingState (setMaxLift(m_maxLift * ratio)) | Strecke je Sekunde weicht um den Euler-Fehler ab, Ankunftsradius halbiert sich: Abhebe- und Endzeitpunkt verschieben sich. | 23 |
| 732 | JetAIUpdate.EnterCircling (_checkAirfieldTick = Tick + LogicFramesPerSecond) | A | Erste Flugplatzsuche 1 s (30 Ticks) nach Beginn des Kreisens über dem toten Flugplatz | JetAIUpdate.cpp, JetOrHeliCirclingDeadAirfieldState (HOW_OFTEN_TO_CHECK = LOGICFRAMES_PER_SECOND; onEnter) | Hängt an der Konstante: bleibt 1 s nur, wenn LogicFramesPerSecond der Rate folgt; sonst 0,5 s. | 20 |
| 739 | JetAIUpdate.CircleDeadAirfield (OutOfAmmoDamageRatePerSecond) | B/daten | Umrechnung zur Laufzeit je Tick: Anteil/s × MaxHealth / LogicFramesPerSecond, Schaden wird jeden Tick angewandt (Rohwert aus Z. 28) | JetAIUpdate.cpp, JetOrHeliCirclingDeadAirfieldState::update (damageRate *= SECONDS_PER_LOGICFRAME_REAL) | Invariant nur, wenn der Divisor der Rate folgt (sonst doppelter Schaden/s); Fixed-Rundung je Tick ändert die Summe leicht. | 20 |
| 743 | JetAIUpdate.CircleDeadAirfield (_checkAirfieldTick = Tick + LogicFramesPerSecond) | A | Wiederholte Flugplatzsuche jede Sekunde (30 Ticks); hier absoluter Tick, in der Referenz Abwärtszähler | JetAIUpdate.cpp, JetOrHeliCirclingDeadAirfieldState::update (--m_checkAirfield <= 0; m_checkAirfield = HOW_OFTEN_TO_CHECK) | Wie Z. 732; Suchrhythmus ist ergebnisrelevant (Zeitpunkt der Landung am neuen Flugplatz). | 20 |
| 776-800 | JetAIUpdate.Land (Hubschrauberzweig) | E | Landung senkrecht: horizontal min(Abstand, max(MinSpeed, 1)) je Tick, Sinken max(SpeedLimitZ, Speed/4) je Tick, fertig bei Abstand ≤ 1; Literal 1 Einheit/Tick als Mindesttempo | eigen | Bei MinSpeed < 1/Tick wirkt das Literal: Anflug doppelt so schnell in Sekunden; Aufsetz-Tick verschiebt sich. | 20,23 |
| 802-829 | JetAIUpdate.Land (Gleitflug und Ausrollen) | E | Speed je Tick um max(Braking, Acceleration) Richtung MinSpeed, Schritt = min(Speed, Abstand), Wegpunkt erreicht bei Abstand ≤ max(Schritt, ε); Höhe aus der Reststrecke | eigen (laut Kommentar loco->setMaxSpeed(getMinSpeed()), nicht geprüft) | Brems- und Gleitstrecke je Sekunde und der Aufsetz-/Ausroll-Tick verschieben sich mit der Schrittweite. | 23 |
| 803 | JetAIUpdate.Land (minSpeed = Max(MinSpeed, 1/2)) | D | Untergrenze der Landegeschwindigkeit 0,5 Einheiten je Tick (15 Einheiten/s bei 30 Hz) | eigen | Unskaliert entspricht die Untergrenze 30 Einheiten/s. | 20 |
| 881 | JetAIUpdate.BeginReload (time * fehlend / ClipSize) | B/daten | Arithmetik auf umgerechneten Ticks zur Laufzeit: ClipReload-Ticks × fehlende Schüsse / ClipSize als Ganzzahldivision (abschneidend), Maximum über die Slots | JetAIUpdate.cpp, JetOrHeliReloadAmmoState::onEnter (rt = (rt * needed) / clipSize) | Abschneiden auf anderer Tick-Körnung: Nachladedauer in Sekunden weicht um bis zu einen Tick ab. | 20 |
| 883 | JetAIUpdate.BeginReload (Math.Max(total, 1)) | A | Mindest-Nachladezeit 1 Tick | JetAIUpdate.cpp, JetOrHeliReloadAmmoState::onEnter (if (m_reloadTime < 1) m_reloadTime = 1) | 1 Tick = 1/60 s; nur bei Waffen ohne Nachladezeit oder vollem Magazin wirksam. | 20 |
| 903-924 | JetAIUpdate.ControlHeight (_verticalSpeed) | E | Höhenregelung per Euler: _verticalSpeed += (Lift − Schwerkraft) je Tick, Deckel Speed/2; Ersatzbeschleunigung Speed/8 (Tempo je Tick als Beschleunigung benutzt); Sinken max(SpeedLimitZ, Speed/4) je Tick | eigen (kinematischer Ersatz für die Physik) | Steigprofil ändert sich; die Ersatzwerte mischen ÷Rate- mit ÷Rate²-Größen und skalieren falsch (Speed/8 wäre bei 60 Hz die doppelte Beschleunigung). | 20,23 |

**`Mod\LastBlockerModules.cs`** (dazu 2 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 200 | SabotageInternetCenterCrateCollide.Sabotage (return data.DurationTicks) | F/tick | Sabotagedauer in Ticks als Ereigniswert (SimEventKind.Sabotaged und Collected über SabotageCollideBase, CrateModules.cs Z. 484-486) | eigen | Verbraucher des Ereignisses bekommen Sim-Ticks; wer sie als 1/30 s deutet, zeigt die doppelte Dauer. | 21 |
| 209 | SmartBombHomingData.Parse (CourseCorrectionScalar) | D | Mischfaktor je Tick als Rohwert (ModuleConfigReader.Real), Vorgabe 0,99; 1 = keine Kurskorrektur | SmartBombTargetHomingUpdate.h, SmartBombTargetHomingUpdateModuleData (m_courseCorrectionScalar = 0.99f) | Ein Faktor „je Frame“: unverändert wirkt er doppelt so oft je Sekunde (müsste zu s^(30/Rate) werden). | 20 |
| 246-255 | SmartBombTargetHomingUpdate.Update | E | Multiplikative Annäherung je Tick: pos = Ziel·(1−s) + pos·s für x und y, solange deutlich über dem Boden | SmartBombTargetHomingUpdate.cpp, SmartBombTargetHomingUpdate::update (statusCoeff/targetCoeff) | Restabstand je Sekunde 0,99^60 statt 0,99^30: die MOAB driftet deutlich schneller aufs Ziel, Einschlagpunkt ändert sich. | 20,23 |
| 323-329 | SupplyWarehouseCripplingBehavior.Update | E | Heilt SelfHealAmount je Aufwachen und schläft SelfHealDelay Ticks, mindestens SleepNone: bei SelfHealDelay = 0 (Fallback Z. 284) Heilung jeden Tick | SupplyWarehouseCripplingBehavior.cpp, update (m_nextHealingFrame = now + m_selfHealDelay; UPDATE_SLEEP(m_nextHealingFrame - now)) | Mit Delay > 0 nur ceil-Rundung des Intervalls; bei Delay 0 verdoppelt sich die Heilung je Sekunde. | 20 |

**`Mod\MatchModules.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 45 | BaseRegenerateUpdate.HealRate | A | Heilschub alle 3 Ticks mit dem Dreifachen der Tickmenge; 2 Verwendungen (Z. 69 Faktor, Z. 71 Schlafdauer) | BaseRenerateUpdate.cpp, BaseRegenerateUpdate::update (const Int HEAL_RATE = 3; return UPDATE_SLEEP(HEAL_RATE)) | Unskaliert Schübe alle 0,05 s statt 0,1 s; Menge je Sekunde stimmt nur, wenn der Divisor in Z. 69 der Rate folgt. | 20 |
| 69 | BaseRegenerateUpdate.Update (amount) | B/daten | Umrechnung zur Laufzeit je Heilschub: MaxHealth × BaseRegenHealthPercentPerSecond × HealRate / LogicFramesPerSecond | BaseRenerateUpdate.cpp, BaseRegenerateUpdate::update (amount = HEAL_RATE * maxHealth * m_baseRegenHealthPercentPerSecond / LOGICFRAMES_PER_SECOND) | Hängt an der Konstante und an HealRate; Fixed-Rundung je Schub und das Kappen bei voller Gesundheit ändern den Verlauf leicht. | 20 |

**`Mod\MineModules.cs`** (dazu 1 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 88-94 | DemoTrapUpdate.Update (_scanCountdown) | C | Scan-Rhythmus als Abwärtszähler je Update (jeden Tick wach): Scan, dann ScanTicks Ticks Pause, also Periode ScanTicks+1 | DemoTrapUpdate.cpp, DemoTrapUpdate::update (m_nextScanFrames-- / m_nextScanFrames = m_scanFrames) | Ergebnisrelevant: Periode 2N+1 statt 2N+2 Ticks, der Auslösezeitpunkt der Falle verschiebt sich. | 20,23 |
| 174 | MinefieldData.Parse (CreatorDeathCheckRate) | B/fallback | Fallback in Ticks: SimulationTiming.LogicFramesPerSecond (30 = 1 s) für ModuleConfigReader.Ticks | MinefieldBehavior.cpp, MinefieldBehaviorModuleData (m_creatorDeathCheckRate = LOGICFRAMES_PER_SECOND) | Bleibt 1 s nur, wenn die Konstante der Rate folgt; als Literal 30 wären es 0,5 s. Keine Rundung (ganzzahlig). | 20 |
| 243 | MinefieldBehavior.Update (DegenPerSecond) | B/daten | Umrechnung zur Laufzeit je Tick: MaxHealth × DegenPercentPerSecondAfterCreatorDies / LogicFramesPerSecond, Schaden jeden Tick während des Abbaus (Rohwert Z. 175) | MinefieldBehavior.cpp, update (maxHealth * m_healthPercentToDrainPerSecond / LOGICFRAMES_PER_SECOND) | Invariant nur mit der Rate als Divisor; je Tick löst der Schaden Reconcile aus – die virtuellen Minen fallen in feineren Schritten. | 20,23 |

**`Mod\ModuleConfigReader.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 40-45 | ModuleConfigReader.Ticks | B/daten | Zentrale Umrechnung Millisekunden → ganze Ticks (ceil, SimulationTiming.DurationMillisecondsToTicks) zur Parsezeit; der fallback wird unkonvertiert als Tickzahl zurückgegeben | eigen (bildet INI::parseDurationUnsignedInt der Modul-Feldtabellen nach; dessen Rumpf nicht geprüft) | Jeder Fallback ≠ 0 bleibt eine 30-Hz-Tickzahl, wenn nur die Umrechnung parametrisiert wird; ceil rundet bei 60 Hz feiner (Dauern werden bis zu 1/60 s kürzer). | 20 |

**`Mod\Phase12Modules.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 55 | TechBuildingData.Parse (PulseFXRate → PulseRateTicks) | F/tick | FX-Pulsrate einer Präsentationswirkung wird als Sim-Ticks gehalten; in src kein Verbraucher von PulseRateTicks gefunden | Herkunft unbelegt | Ein späterer Verbraucher bekäme Sim-Ticks statt Wandzeit. | 21 |
| 154 | FireWeaponWhenDamagedBehavior.Fire (Math.Max(DelayBetweenShotsTicks, SleepNone)) | E | Schussabstand der Reaktions-/Dauerwaffe mindestens 1 Tick; die Dauerwaffe wird jeden Tick geprüft (Update Z. 141-146, SleepNone) | eigen (ersetzt den Waffenstatus der Referenz; FireWeaponWhenDamagedBehavior.cpp, update: UPDATE_SLEEP_NONE) | Bei DelayBetweenShots = 0 feuert die Dauerwaffe doppelt so oft je Sekunde; sonst nur ceil-Rundung. | 20,23 |
| 229 | FireWeaponPower.Update (Math.Max(DelayBetweenShotsTicks, SleepNone)) | E | Schlaf zwischen den Schüssen der Spezialkraft mindestens 1 Tick | eigen | Bei DelayBetweenShots = 0 fallen die MaxShots in der halben Zeit. | 20 |
| 299 | HordeData.Parse (UpdateRate) | B/fallback | Fallback in Ticks: SimulationTiming.LogicFramesPerSecond (30 = 1 s) | HordeUpdate.cpp (DEFAULT_UPDATE_RATE = LOGICFRAMES_PER_SECOND) | Bleibt 1 s nur, wenn die Konstante der Rate folgt. | 20 |
| 336 | HordeUpdate.OnSpawn (LogicRandom.Range(1, UpdateRateTicks)) | C | Zufällige Staffelung des ersten Aufwachens über 1..UpdateRate Ticks; ein Logikzufallszug je Objekt | HordeUpdate.cpp, HordeUpdate-Konstruktor (UPDATE_SLEEP(GameLogicRandomValue(1, delay))) | Ergebnisrelevant: der Ziehbereich hängt an der Tickzahl, der gezogene Wert und damit Phase und Hash ändern sich. | 20,23 |
| 343-349 | HordeUpdate.Update (_lastRefresh) | C | Neubewertung: Infanterie schläft UpdateRateTicks; Fahrzeuge laufen jeden Tick und werten bei Tick > _lastRefresh + UpdateRate aus (strikt, also Periode N+1) | HordeUpdate.cpp, HordeUpdate::update (getFrame() > m_lastHordeRefreshFrame + m_updateRate; UPDATE_SLEEP(m_updateRate)) | Ergebnisrelevant (Zeitpunkt des HORDE-Bonus): Fahrzeugperiode 2N+1 statt 2N+2 Ticks, Infanterie exakt; doppelte Last bei Fahrzeugen. | 20,23 |

**`Mod\Phase5BlockerModules.cs`** (dazu 10 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 292 | SlowDeathBehavior.BeginSlowDeath (Range(0, SinkDelayVarianceTicks)) | B/daten | Zufallszug über einen Tickbereich zur Laufzeit; Wert ungenutzt, nur für den RNG-Gleichlauf gezogen | SlowDeathBehavior.cpp (m_sinkFrame = timeScale * (m_sinkDelay + GameLogicRandomValue(0, m_sinkDelayVariance))) | Ziehbereich verdoppelt sich mit skalierten Ticks; die Zahl der Züge bleibt, der RNG-Zustand danach kann je nach Generator abweichen. | 20,23 |
| 293 | SlowDeathBehavior.BeginSlowDeath (Range(0, DestructionDelayVarianceTicks)) | B/daten | Zerstörungsverzögerung = DestructionDelay-Ticks + ganzzahliger Zufall in 0..Varianz-Ticks (Laufzeit, Tick-Körnung) | SlowDeathBehavior.cpp (m_destructionFrame = timeScale * (m_destructionDelay + GameLogicRandomValue(0, m_destructionDelayVariance))) | Anderer Ziehbereich: der gezogene Wert ist nicht das Doppelte des 30-Hz-Werts, Zerstörungszeitpunkt und Hash ändern sich. | 20,23 |
| 426 | StealthDetectorData.Parse (DetectionRate) | B/fallback | Fallback 1 Tick für ModuleConfigReader.Ticks („jeden Frame scannen“, eine Frame-Zahl, keine Dauer) | StealthDetectorUpdate.h, StealthDetectorUpdateModuleData (m_updateRate = 1) | Unskaliert Scan jeden Tick = 60-mal je Sekunde (doppelte Last, O(n) je Detektor); skaliert auf 2 wäre es weiter 1/30 s. | 20 |
| 457 | StealthDetectorUpdate.OnSpawn (LogicRandom.Range(1, UpdateRateTicks)) | C | Zufällige Staffelung des ersten Scans über 1..DetectionRate Ticks; ein Logikzufallszug je Detektor | StealthDetectorUpdate.cpp, Konstruktor (UPDATE_SLEEP(GameLogicRandomValue(1, m_updateRate))) | Ergebnisrelevant: Ziehbereich hängt an der Tickzahl, anderer Wert → andere Scanphase und anderer Hash. | 20,23 |
| 486 | StealthDetectorUpdate.Update (MarkDetected(UpdateRateTicks + 1)) | E | Entdeckung hält DetectionRate + 1 Ticks: ein Tick Überlappung bis zum nächsten Scan (Tick-Reihenfolge) | StealthDetectorUpdate.cpp, StealthDetectorUpdate::update (stealth->markAsDetected(m_updateRate + 1)) | Die Überlappung bleibt 1 Tick (halbe Zeit); sie sichert nur die Reihenfolge Scan → Ablauf und darf nicht mitskaliert werden müssen. | 23 |
| 555 | PoisonedBehavior.Update (Math.Max(PoisonDamageIntervalTicks, SleepNone)) | E | Giftschaden je Intervall, Intervall mindestens 1 Tick (ebenso Z. 535): bei PoisonDamageInterval = 0 (Fallback Z. 501) Schaden jeden Tick | eigen (Mindestwert; PoisonedBehavior.cpp nicht auf einen Mindestwert geprüft) | Mit Intervall > 0 nur ceil-Rundung (Schaden je Sekunde ändert sich über die Rundung); bei 0 doppelter Schaden je Sekunde. | 20 |

**`Mod\Phase5Modules.cs`** (dazu 8 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 71 | StealthData.Parse "MoveThresholdSpeed" (StealthData.MoveThresholdSpeed) | D | Rohwert über ModuleConfigReader.Real, in StealthUpdate.ConditionActive ("MOVING", Z. 185) direkt mit Entity.Speed (je Tick) verglichen; keine Umrechnung | StealthUpdate.cpp, Feldtabelle: "MoveThresholdSpeed" ist INI::parseVelocityReal (Original rechnet je Sekunde in je Frame um; hier nicht) | Schwelle bleibt gleich, Entity.Speed je Tick halbiert sich: Einheiten gelten bei doppelter Geschwindigkeit noch als stehend; schon bei 30 Hz weicht die Einheit vom Original ab | 20 |
| 187-188 | StealthUpdate.ConditionActive (FIRING_*/TAKING_DAMAGE: LastFireTick == world.Tick) | E | Ereignisfenster von genau einem Tick: Bedingung gilt nur im Tick des Schusses/Schadens; verlangt, dass Update jeden Tick läuft (SleepNone) und nach dem Ereignis drankommt | eigen | Fenster dauert 1/60 s statt 1/30 s; Ergebnis nur gleich, solange das Modul weiter jeden Tick läuft und die Reihenfolge Schuss vor Update bleibt | 23 |
| 283 | AutoHealBehavior.Update (return Math.Max(data.HealingDelayTicks, SleepNone)) | E | Heilimpuls mit festem HealingAmount je Aufwachen; Untergrenze 1 Tick, Fallback von HealingDelay ist 0 (Z. 228) – dann Heilung jeden Tick | AutoHealBehavior.h: m_healingDelay = UINT_MAX (Original-Standard ist „nie“, nicht 0); "HealingDelay" parseDurationUnsignedInt | Bei fehlendem oder sehr kleinem HealingDelay verdoppelt sich die Heilung je Sekunde; sonst nur Aufrundung der Periode | 20,23 |
| 528 | AIUpdateData.DefaultMoodCheckTicks | A | Konstante 2 * LogicFramesPerSecond (= 60 Ticks), Fallback von ModuleConfigReader.Ticks("MoodAttackCheckRate") in Z. 533 (dort Math.Max(1, …)) und Record-Standardwert Z. 525 | AIUpdate.cpp, AIUpdateModuleData::AIUpdateModuleData: m_moodAttackCheckRate = LOGICFRAMES_PER_SECOND * 2 | Skaliert über die Konstante mit, sofern LogicFramesPerSecond zur Laufzeitgröße wird; als const long ist es zur Übersetzungszeit eingefroren | 20 |
| 880 | AIUpdateInterface.UpdateTask (RepairRatePerSecond / LogicFramesPerSecond) | B/daten | Reparaturanteil je Sekunde wird zur Laufzeit in jedem Tick durch 30 geteilt (Fixed-Division, kein Aufrunden); Heilung je Tick = MaxHealth * Rate / 30 | DozerAIUpdate.cpp: "RepairHealthPercentPerSecond" parsePercentToReal, Division durch LOGICFRAMES_PER_SECOND (Z. 720) | Rechnet über die Konstante richtig um; Fixed-Rundung je Tick ändert die Summe geringfügig | 20 |
| 1059-1078 | AIUpdateInterface.Move (arrivalRadius, reach = locomotor.Speed) | E | Ankunftsradius = Max(CloseEnoughDist, Speed je Tick); Zwischenwegpunkte gelten bei Abstand <= ein Tick Fahrt als erreicht; danach Einrasten auf den Wegpunkt | eigen | Radius halbiert sich mit der Geschwindigkeit je Tick: Wegpunkte werden enger abgefahren, Ankunftsort und -zeitpunkt verschieben sich | 23 |
| 1084-1103 | AIUpdateInterface.Move (Drehung, Bremsweg, Beschleunigung) | E | Euler je Tick: Winkel += TurnRatePerTick, Speed +/- Acceleration/Braking je Tick, Bremsweg v²/(2a) in Tick-Einheiten, Wendekreis Speed/turnRate*2, Schwelle π/6 | eigen (Kommentar verweist auf calcSlowDownDist, nicht nachgeschlagen) | Diskrete Schritte halb so groß: Bremsbeginn, Kurvenradius und Einrasten auf den Sollwinkel (difference <= turnRate) fallen anders aus | 23 |
| 1106 | AIUpdateInterface.Move (world.Tick - _blockerReplanTick >= 30) | C | Neuplanung um stehende Blocker höchstens einmal je 30 Ticks (Literal); Startwert/Sentinel _blockerReplanTick = -1000 (Z. 1182, auch im Snapshot) | eigen (docs/DEMO-FINDINGS.md #12) | Ohne Skalierung doppelt so häufige Neuplanung (alle 0,5 s): andere Pfade, also andere Ergebnisse, nicht nur Last | 20 |
| 1109 | AIUpdateInterface.Move (Vorausschau Entity.Speed * 25) | B/literal | Vorausschau für stehende Blocker = 25 Ticks Fahrt (Literal 25 mal Geschwindigkeit je Tick, zweimal im Ausdruck) | eigen | Vorausschaustrecke halbiert sich (25 Ticks = 0,42 s): Blocker werden später erkannt | 20 |
| 1115-1124 | AIUpdateInterface.Move (Positionsschritt, AvoidLocalBlockers, ResolveFootprintCollisions) | E | Position += Richtung * Speed je Tick; lokale Ausweichbewegung und Kollisions-/Überrollprüfung einmal je Tick mit dem Tick-Schritt | eigen | Halbe Schrittweite: Ausweichen und Kollisionstreffer werden doppelt so oft und mit kleineren Schritten geprüft, Bahnen weichen ab | 23 |
| 1133-1150 | AIUpdateInterface.MoveWinged | E | Ankunft bei Abstand <= Max(CloseEnoughDist, Speed je Tick) oder Ziel hinter dem Flugzeug innerhalb 2 * Wendekreis (Speed/TurnRatePerTick); Bremsweg (v² - vmin²)/(2a) | Kommentar nennt Locomotor::moveTowardsPositionWings; nicht nachgeschlagen – Herkunft unbelegt | Ankunftsradius „ein Tick Flug“ halbiert sich; Ankunftstick und damit Kreisflug-Beginn verschieben sich | 23 |
| 1163-1180 | AIUpdateInterface.SteerWinged | E | Ein Tick Flügelflug: Drehung um höchstens TurnRatePerTick, Speed +/- Acceleration/Braking, Position += Richtung * Speed | Herkunft unbelegt | Euler-Schritt halb so groß: Kurvenbahn und Zeitpunkt des Erreichens der Sollgeschwindigkeit ändern sich leicht | 23 |
| 1223-1253 | AIUpdateInterface.MoveStraight | E | Geradeausfahrt ohne Terrain: closeEnough = Max(CloseEnoughDist, Speed je Tick), Bremsweg v²/(2a), Speed +/- je Tick, Schritt = Min(Speed, dist), Kollisionsprüfung je Tick | eigen | Ankunftsradius und Bremsbeginn hängen an der Tick-Schrittweite; Ankunftstick verschiebt sich | 23 |
| 1464-1489 | ProductionUpdate.Update (RemainingTicks - 1; Tür zu: RemainingTicks = 1) | C | Bauzeit wird je Tick um 1 heruntergezählt (Gesamtzahl kommt vom Aufrufer über Enqueue, mind. 1); fertige Einheit pollt die Tür jeden Tick | ProductionUpdate::update laut Kommentar; nicht nachgeschlagen – Herkunft unbelegt | Richtig, wenn die übergebene Tickzahl skaliert wird; Türabfrage dann in 1/60-s-Raster (Ergebnis: Austrittstick) | 20 |

**`Mod\PhysicsAndMinefieldModules.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 58-61 | PhysicsBehavior.Update (freier Fall) | E | Euler je Tick: velocityZ += GravityPerTickSquared, Position += Geschwindigkeit je Tick (X/Y vom Aufrufer StartFall je Tick übergeben); Landung, sobald Z <= Boden | eigen (Ersatz für PhysicsUpdate.cpp) | Erst Geschwindigkeit, dann Position: Fallzeit und Aufschlagort weichen bei halber Schrittweite ab; Aufschlag wird auf den Tick gerundet | 23 |
| 168-180 | GenerateMinefieldBehavior.Update (return SleepNone) | C | Fragt jeden Tick ab, ob das Upgrade der Minen da ist (solange Upgradable und noch nicht aufgewertet) | GenerateMinefieldBehavior.cpp update() laut Kommentar; nicht nachgeschlagen – Herkunft unbelegt | Nur Last und Reaktionsraster (1/60 s statt 1/30 s); Zufallszüge erst beim Auslösen, nicht je Abfrage | 20 |

**`Mod\ProjectileModules.cs`** (dazu 4 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 44 | DumbProjectileData.DefaultMaxLifespanTicks | A | Konstante 10 * LogicFramesPerSecond (= 300 Ticks), Fallback von ModuleConfigReader.Ticks("MaxLifespan") in Z. 47 | DumbProjectileBehavior.cpp: const Int DEFAULT_MAX_LIFESPAN = 10 * LOGICFRAMES_PER_SECOND | Skaliert über die Konstante mit; als const long zur Übersetzungszeit eingefroren | 20 |
| 54 | DumbProjectileData.Parse "FlightPathAdjustDistPerSecond" (FlightPathAdjustPerTick) | B/daten | Strecke je Sekunde wird beim Modul-Parsen von Hand durch LogicFramesPerSecond geteilt (nicht über SimulationTiming.PerSecondToPerTick), Fixed-Division ohne Aufrunden | DumbProjectileBehavior.cpp: "FlightPathAdjustDistPerSecond" INI::parseVelocityReal (m_flightPathAdjustDistPerFrame) | Rechnet über die Konstante richtig um; eigene Divisionsstelle, die bei der Parametrisierung nicht über die Umrechnungsfunktion läuft | 20 |
| 148 | DumbProjectileBehavior.CalcFlightPath (_segments = ceil(length / _speed)) | E | Anzahl der Bahnpunkte = aufgerundete Bahnlänge durch Waffengeschwindigkeit je Tick; ein Punkt je Tick, also Flugdauer in ganzen Ticks; im Snapshot als "segments" | DumbProjectileBehavior.cpp calcFlightPath laut Kommentar; nicht nachgeschlagen – Herkunft unbelegt | Flugdauer wird auf 1/60 s statt 1/30 s aufgerundet: Einschlag bis zu einem halben alten Tick früher | 20,23 |
| 229-256 | DumbProjectileBehavior.Update | E | Je Update ein Bahnpunkt (_step++); Detonation erst im Update nach dem letzten Punkt („ein Tick später“); Zielnachführung um höchstens FlightPathAdjustPerTick je Tick mit Neuberechnung der Kurve | Herkunft unbelegt | Der Zusatz-Tick bis zur Detonation ist nur noch 1/60 s; Nachführung in doppelt so vielen, halb so großen Schritten ergibt eine leicht andere Kurve | 23 |
| 334 | MissileData.Parse "InitialVelocity" | B/daten | Geschwindigkeit je Sekunde wird beim Modul-Parsen von Hand durch LogicFramesPerSecond geteilt (nicht über SimulationTiming.PerSecondToPerTick) | MissileAIUpdate.cpp: "InitialVelocity" INI::parseVelocityReal (m_initialVel) | Rechnet über die Konstante richtig um; eigene Divisionsstelle | 20 |
| 341 | MissileData.Parse "KillSelfDelay" (Fallback 3) | B/fallback | Fallback von ModuleConfigReader.Ticks ist 3 Ticks (Wartezeit nach der Detonation bis zur Selbstentfernung) | MissileAIUpdate.cpp, MissileAIUpdateModuleData-Konstruktor: m_killSelfDelay = 3 (Frames; "KillSelfDelay" parseDurationUnsignedInt) | Ohne Skalierung 50 ms statt 100 ms Nachlauf der Rakete (Kondensstreifen, spätere Entfernung) | 20 |
| 428 | MissileAIUpdate.Launch (_acceleration = weapon.WeaponSpeed) | E | Bei UseWeaponSpeed wird die Waffengeschwindigkeit (Strecke je Tick) als Beschleunigung (je Tick²) eingesetzt: volle Geschwindigkeit nach einem Tick | Kommentar: m_maxAccel = weaponSpeed (MissileAIUpdate.cpp); nicht nachgeschlagen – Herkunft unbelegt | Bleibt „volle Geschwindigkeit nach einem Tick“, also nach 1/60 s; Einheitenmischung, die nicht mit dem Faktor für Beschleunigungen skaliert werden darf | 23 |
| 469-473 | MissileAIUpdate.Update (_noTurnDistanceLeft -= Distance(position, _previous)) | E | Strecke vor dem Einlenken wird aus der Bewegung des vorigen Ticks gemessen (vor dem eigenen Schritt), hinkt also einen Tick Flug nach | Herkunft unbelegt (Kommentar: „as the reference does before moving“) | Nachlauf nur noch ein halber alter Tick Flug: Wechsel in den Lenkzustand etwas früher auf der Bahn | 23 |
| 610-648 | MissileAIUpdate.Move | E | Euler je Tick: _speed += _acceleration (bis _maxSpeed), Ankunft bei Restweg <= _speed (ein Tick Flug) mit Einrasten auf das Ziel, ohne Treibstoff _verticalSpeed += GravityPerTickSquared, Bodenkontakt je Tick | eigen (kinematischer Ersatz für den THRUST-Locomotor) | Ankunftsfenster halbiert sich, Detonationstick und -ort verschieben sich; Gleitbahn ohne Treibstoff ändert sich mit der Schrittweite | 23 |
| 788-794 | PointDefenseLaserUpdate.Update (_nextScan-Zähler) | C | Suchzähler: wird auf ScanTicks gesetzt und je Tick heruntergezählt, gesucht wird im Tick nach 0 – Periode ScanTicks + 1; Fallback von "ScanRate" ist 0 = jeden Tick; Reichweitenprüfung/Schuss jeden Tick | PointDefenseLaserUpdate.cpp: m_scanFrames = 0, m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames ("ScanRate" parseDurationUnsignedInt) | Ändert Ergebnisse: Periode N+1 skaliert nicht linear (2N+1 statt 2N+2 Ticks), Zielwahl fällt auf andere Ticks | 20,23 |
| 808 | PointDefenseLaserUpdate.FireWhenReady (_nextScan = LogicRandom.Range(0, 3), Ziel verlässt Reichweite) | A | Zufällige Wartezeit 0..3 Ticks bis zur nächsten Suche (Literale 0 und 3, ein Zufallszug je Ereignis) | PointDefenseLaserUpdate.cpp: m_nextScanFrames = GameLogicRandomValue( 0, 3 ) | Ohne Skalierung 0..50 ms statt 0..100 ms; mit Skalierung (0..6) ändert sich der Wertebereich des Zufallszugs und damit der Zufallsstrom | 20 |
| 819-830 | PointDefenseLaserUpdate.FireWhenReady (_nextShot-Zähler) | E | Schusszähler: nach dem Schuss auf weapon.DelayBetweenShotsTicks gesetzt, je Tick -1, Schuss im Tick nach 0 – Schussabstand Delay + 1 Ticks | PointDefenseLaserUpdate.cpp: m_nextShotAvailableInFrames-- / = wt->getDelayBetweenShots( bonus ) | Schussabstand 2N+1 statt 2N+2 Ticks: Feuerrate des Lasers steigt leicht (bei kleinem Delay spürbar) | 20,23 |
| 833 | PointDefenseLaserUpdate.FireWhenReady (_nextScan = LogicRandom.Range(0, 3), Ziel tot) | A | Zufällige Wartezeit 0..3 Ticks bis zur nächsten Suche nach einem Abschuss | PointDefenseLaserUpdate.cpp: m_nextScanFrames = GameLogicRandomValue( 0, 3 ) | Wie Z. 808: Wartezeit halbiert sich bzw. Wertebereich des Zufallszugs ändert sich | 20 |

**`Mod\RailroadModules.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 33 | RailroadData.Parse "KillSpeedMin" (Standard 1) | D | Tötungsschwelle in Strecke je Frame, Rohwert, Standard Fixed.One; Vergleich mit _pull.Speed in Collide (Z. 561) | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_killSpeedMin = 1.0f | Ohne Umrechnung tötet der Zug erst ab doppelter Geschwindigkeit | 20 |
| 33 | RailroadData.Parse "RunningGarrisonSpeedMax" (Standard 1) | D | Geschwindigkeitsschwelle in Strecke je Frame, Rohwert ohne Umrechnung, Standard Fixed.One; Vergleich mit _pull.Speed in Collide (Z. 559) | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_runningGarrisonSpeedMax = 1.0f | Schwelle bezieht sich auf Strecke je Tick; ohne Umrechnung gilt sie für die doppelte Geschwindigkeit | 20 |
| 34 | RailroadData.Parse "Acceleration" (Standard 1.01) | D | Multiplikativer Beschleunigungsfaktor je Frame, Rohwert, Standard 1,01 | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_acceleration = 1.01f | Faktor wirkt doppelt so oft: Zug beschleunigt in der halben Zeit; lineares Skalieren ist falsch (Wurzel nötig) | 20,23 |
| 34 | RailroadData.Parse "SpeedMax" (Standard 4) | D | Höchstgeschwindigkeit des Zugs in Strecke je Frame, Rohwert, Standard 4 | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_speedMax = 4 | Zug fährt doppelt so schnell (4 Einheiten je 1/60 s) | 20 |
| 35 | RailroadData.Parse "Braking" (Standard 0.99) | D | Multiplikativer Bremsfaktor je Frame, Rohwert, Standard 0,99 | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_braking = 0.99f | Zug bremst in der halben Zeit und auf kürzerem Weg; Halt am Bahnhof an anderer Stelle | 20,23 |
| 35 | RailroadData.Parse "Friction" (Standard 0.97) | D | Multiplikative Reibung je Frame für ausrollende Wagen, Rohwert, Standard 0,97 | RailroadGuideAIUpdate.h: INI::parseReal; RailroadGuideAIUpdate.cpp: m_friction = 0.97f | Abgekoppelte Wagen rollen in der halben Zeit aus | 20,23 |
| 36 | RailroadData.Parse "WaitAtStationTime" (Fallback 150) | B/fallback | Fallback von ModuleConfigReader.Ticks ist 150 Ticks (5 s Halt am Bahnhof) | RailroadGuideAIUpdate.cpp, RailroadBehaviorModuleData-Konstruktor: m_waitAtStationTime = 150; Header: parseDurationUnsignedInt | Ohne Skalierung hält der Zug nur 2,5 s, wenn das Feld in den Daten fehlt | 20 |
| 59 | RailroadBehavior.FramesUnpulledLongEnoughToUnhitch | A | Konstante 2: Ticks ohne Zug durch den Vorderwagen, nach denen ein Wagen sich zum Führungswagen erklärt (2 Verwendungen, Z. 197 und 217; Zähler _wantsToBeLead im Snapshot) | RailroadGuideAIUpdate.cpp: #define FRAMES_UNPULLED_LONG_ENOUGH_TO_UNHITCH (2) | Abkoppeln nach 3 Ticks = 50 ms statt 100 ms; eigentlich eine Reihenfolge-Toleranz in Ticks, nicht zwingend zu skalieren | 20 |
| 60 | RailroadBehavior.PushStart | D | Konstante 0,02: Anschub, der je Tick zur Geschwindigkeit addiert wird, bevor der Beschleunigungsfaktor wirkt (Verwendung Z. 251) | RailroadGuideAIUpdate.cpp: conductorPullInfo.speed += 0.02f * conductorPullInfo.m_direction (push start multiplier) | Wird doppelt so oft addiert: schnellerer Anlauf; ist eine Beschleunigung je Tick² in Tick-Einheiten | 20,23 |
| 188-190 | RailroadBehavior.Update (Wagen existieren erst im nächsten Tick) | E | Nach CreateCarriages wird ein Tick gewartet, weil per RequestSpawn erzeugte Wagen erst ab dem nächsten Tick existieren | eigen | Ein-Tick-Verzögerung ist nur noch 1/60 s; Reihenfolgesemantik in Ticks, keine Zeit | 23 |
| 197-217 | RailroadBehavior.Update (Reibung, TrackDistance += Speed, _wantsToBeLead++) | E | Je Tick: beim Ausrollen Speed *= Friction, Streckenposition += Speed (Strecke je Tick), Zähler der Ticks ohne Zug | Herkunft unbelegt (RailroadBehavior::update laut Kommentar; nur Konstanten nachgeschlagen) | Multiplikative Dämpfung und Wegschritt doppelt so oft: Ausrollweg und Zuglage ändern sich | 23 |
| 219 | RailroadBehavior.Update (Collide(world) jeden Tick) | C | Kollisionsprüfung des Wagens gegen alle Objekte einmal je Tick nach der Bewegung; Wegschieben um ein Viertel der Überlappung je Tick (Z. 553) | eigen (Ersatz für onCollide der Physik) | Ändert Ergebnisse: doppelt so viele Prüfungen und Schubse je Sekunde, Schaden speed*10 fällt doppelt so oft an (siehe Z. 562) | 20,23 |
| 229 | RailroadBehavior.RunConductor (ApplyBrakes: Speed *= data.Braking) | E | Multiplikatives Bremsen je Tick bis unter die Schwelle, dann Halt und Start des Bahnhofszählers (_waitAtStationTimer = WaitAtStationTicks, je Tick -1) | Herkunft unbelegt (Ablauf nicht nachgeschlagen, nur Konstanten) | Bremsweg und Haltepunkt ändern sich (Faktor wirkt doppelt so oft) | 23 |
| 230 | RailroadBehavior.RunConductor (Speed.Abs() < 0.1) | D | Literal 0,1: Geschwindigkeitsschwelle in Strecke je Tick, unter der der Zug als stehend gilt | RailroadGuideAIUpdate.cpp: if (fabs(conductorPullInfo.speed) < 0.1f) | Schwelle entspricht der doppelten realen Geschwindigkeit: Zug „steht“ früher | 20 |
| 247 | RailroadBehavior.RunConductor (Speed = 0.05 * Direction) | D | Literal 0,05: Anfahrgeschwindigkeit in Strecke je Tick nach dem Bahnhofshalt | RailroadGuideAIUpdate.cpp: conductorPullInfo.speed = 0.05f * conductorPullInfo.m_direction | Anfahrgeschwindigkeit real doppelt so hoch | 20 |
| 251-254 | RailroadBehavior.RunConductor (Accelerate) | E | Je Tick: Speed += PushStart * Richtung, Speed *= Acceleration, Begrenzung auf +/- SpeedMax | RailroadGuideAIUpdate.cpp: speed += 0.02f * m_direction (Rest des Blocks nicht nachgeschlagen) | Gemischt additiv/multiplikativ je Tick: Anfahrkurve nicht durch einfaches Skalieren der Faktoren reproduzierbar | 23 |
| 562 | RailroadBehavior.Collide (Schaden _pull.Speed * 10) | D | Überrollschaden = Geschwindigkeit je Tick mal 10 (Literal), je Kollisionsprüfung, also je Tick der Überlappung | RailroadGuideAIUpdate.cpp: damageInfo.in.m_amount = m_pullInfo.speed * 10 | Bei skalierter Geschwindigkeit halber Schaden je Treffer, aber doppelt so viele Treffer; ohne Skalierung doppelter Schaden je Sekunde | 20,23 |

**`Mod\RepairModules.cs`** (dazu 1 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 34 | RepairDockData.Parse (frames = Fixed.One ohne "TimeForFullHeal") | B/fallback | Fallback der Heildauer ist 1 Frame (Fixed.One), in Ticks geschrieben | RepairDockUpdate.cpp, Konstruktor: m_framesForFullHeal = 1.0f (1 frame, instant heal by default) | Bleibt „sofort“ (1 Tick); als Zeit halbiert, praktisch ohne Wirkung | 20 |
| 35 | RepairDockData.Parse "TimeForFullHeal" (DurationMillisecondsToFixedTicks) | B/daten | Umrechnung ms in gebrochene Ticks ohne Aufrunden (andere Rundung als ModuleConfigReader.Ticks), beim Modul-Parsen | RepairDockUpdate.cpp: "TimeForFullHeal" INI::parseDurationReal (m_framesForFullHeal) | Rechnet über die Umrechnungsfunktion um; keine Rundungsstufe, nur Fixed-Genauigkeit | 20 |
| 57-69 | RepairDockUpdate.Action (_healthToAddPerTick) | E | Heilmenge je Tick wird beim ersten Action-Aufruf als (Max - Health) / FramesForFullHeal festgelegt und je Aufruf angewendet; setzt genau einen Action-Aufruf je Tick voraus; Wert steht im Snapshot ("healthPerTick") | RepairDockUpdate.cpp: m_framesForFullHeal (RepairDockUpdate::action laut Kommentar, Ablauf nicht nachgeschlagen) | Richtig nur, wenn FramesForFullHeal skaliert und Action weiter genau einmal je Tick läuft; alte Snapshots tragen den 30-Hz-Wert | 20,23 |
| 114-122 | AutoFindHealingUpdate.Update (_nextScanTicks-Zähler) | C | Suchzähler: auf ScanTicks gesetzt, je Tick -1, Suche im Tick nach 0 – Periode ScanTicks + 1; Fallback von "ScanRate" ist 0 = Suche jeden Tick | AutoFindHealingUpdate.cpp: m_scanFrames = 0; m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames | Ändert Ergebnisse: Periode N+1 skaliert nicht linear, Heilbefehl fällt auf andere Ticks; bei Fallback 0 nur Last | 20,23 |

**`Mod\RetailMatchModules.cs`** (dazu 6 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 88-89 | HealContain.Update (MaxHealth / TimeForFullHealTicks) | B/daten | Heilung je Tick = MaxHealth geteilt durch die aufgerundete Tickzahl von "TimeForFullHeal", zur Laufzeit je Tick berechnet; Ende über world.Tick - since >= Ticks | HealContain.cpp: m_framesForFullHeal = 0; "TimeForFullHeal" INI::parseDurationUnsignedInt | Skaliert mit der Tickzahl; Aufrundung der Dauer und Fixed-Division je Tick ändern die Summe geringfügig | 20 |
| 267 | RadarUpdate.IsExtendComplete (tick > _extendDoneTick) | E | Fertig erst einen Tick nach Ablauf (strikt größer), während PowerPlantUpdate.IsExtendComplete (Z. 307) >= benutzt | Herkunft unbelegt | Zusatz-Tick ist 1/60 s statt 1/30 s; nur Anzeigezustand, kein Simulationsverbraucher in dieser Datei | 20 |
| 372 | OverchargeBehavior.Update (HealthDrainRatioPerSecond / LogicFramesPerSecond) | B/daten | Schaden je Tick = MaxHealth * Anteil je Sekunde / 30, zur Laufzeit in jedem Tick geteilt (Fixed, kein Aufrunden) | OverchargeBehavior.cpp: (body->getMaxHealth() * m_healthPercentToDrainPerSecond) / LOGICFRAMES_PER_SECOND | Rechnet über die Konstante richtig um; Abschaltschwelle wird in feinerem Raster erreicht | 20 |
| 475 | RebuildHoleBehavior.Update (HealthRegenPercentPerSecond / LogicFramesPerSecond) | B/daten | Heilung je Tick = MaxHealth * Anteil je Sekunde / 30, zur Laufzeit in jedem Tick geteilt; daneben Zähler _workerWait je Tick -1 (Z. 479) | RebuildHoleBehavior.cpp: (m_holeHealthRegenPercentPerSecond / LOGICFRAMES_PER_SECOND) * … | Rechnet über die Konstante richtig um; nur Fixed-Rundung je Tick | 20 |

**`Mod\SlaveModules.cs`** (dazu 4 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 166 | SlavedUpdate.UpdateRate | A | Prüfrhythmus der Prioritätenliste = LogicFramesPerSecond / 4 in Ganzzahlarithmetik (7 Ticks); 2 Verwendungen (Z.229, Z.430) | GameEngine/Include/GameLogic/Module/SlavedUpdate.h: SLAVED_UPDATE_RATE = LOGICFRAMES_PER_SECOND/4 | Schon von der Rate abgeleitet, aber Ganzzahldivision: 30 Hz ergibt 7 Ticks (0,233 s), 60 Hz ergibt 15 Ticks (0,25 s); Rhythmus und Zufallsfolge verschieben sich. | 20,23 |
| 170 | SlavedUpdate.RecentFireTicks | B/literal | Eigene Frist „Master hat in den letzten 2 s gefeuert“ = 2 * LogicFramesPerSecond (60 Ticks), Vergleich world.Tick - LastFireTick in Z.313 | eigen | Als const von der Rate abgeleitet; bleibt 2 s, sobald LogicFramesPerSecond parametriert ist (const muss dann Laufzeitwert werden). | 20 |
| 223-230 | SlavedUpdate.Update (_framesToWait) | C | Modul wacht jeden Tick (SleepNone), zählt _framesToWait je Tick herunter; Prioritätenliste nur alle UpdateRate Ticks, im Reparaturzustand jeden Tick | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::update (m_framesToWait = SLAVED_UPDATE_RATE) | Ergebnisse: Bewegungsbefehle, DRONE_SPOTTING-Bonus und Zufallsziehungen hängen am Rhythmus; unskaliert doppelt so häufig je Sekunde. | 20,23 |
| 335 | SlavedUpdate.DoAttackLogic (RandomDirection) | E | Zufallsziehung Range(0,6) je Prüfung, solange Angriffslogik läuft und AttackWanderRange != 0; dazu neuer Bewegungsbefehl je Prüfung | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doAttackLogic (GameLogicRandomValue(0, 2*PI)) | Zahl der Ziehungen aus dem Logikstrom je Sekunde folgt dem Prüfrhythmus; andere Rhythmuslänge verschiebt die Zufallsfolge aller Module. | 23 |
| 355 | SlavedUpdate.DoScoutLogic (RandomDirection) | E | Zufallsziehung Range(0,6) je Prüfung, solange Späherlogik läuft und ScoutWanderRange != 0 | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doScoutLogic (GameLogicRandomValue(0, 2*PI)) | Wie Angriffslogik: Ziehungen je Sekunde hängen am Prüfrhythmus. | 23 |
| 379 | SlavedUpdate.DoGuardLogic (RandomDirection) | E | Zufallsziehung Range(0,6) je Prüfung, in der die Drohne untätig und zu weit vom Ankerpunkt ist (GuardWanderRange != 0) | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doGuardLogic (GameLogicRandomValue(0, 2*PI)) | Ziehung je Prüfung; bei feinerem Raster kann dieselbe Lage öfter oder zu anderem Zeitpunkt ziehen. | 23 |
| 414 | SlavedUpdate.DoRepairLogic (Höhenziehung) | E | Zufallsziehung Range(RepairMinAltitude, RepairMaxAltitude) bei jedem Aufruf, solange die Drohne nicht nah genug ist (im Reparaturzustand jeden Tick), nur zur Stromausrichtung | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doRepairLogic (GameLogicRandomValueReal Z.492) | Doppelt so viele Ziehungen und Bewegungsbefehle je Sekunde während des Anflugs; Zufallsfolge verschiebt sich. | 23 |
| 420 | SlavedUpdate.DoRepairLogic (amount) | B/daten | RepairRatePerSecond / LogicFramesPerSecond zur Laufzeit je Schweißtick, ungerundet (Fixed-Division); Heilung je Tick | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::doRepairLogic (repairAmount = m_repairRatePerSecond / LOGICFRAMES_PER_SECOND) | Je Sekunde gleich, wenn die Konstante parametriert ist; Heilschritte halb so groß, Fixed-Rundung je Schritt anders. | 20,23 |
| 442 | SlavedUpdate.SetRepairState (Unpacking/Packing) | A | _framesToWait = 15 Ticks beim Ein-/Auspacken (direkt angefordert) | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (m_framesToWait = 15, Z.581/585) | Auspacken dauert 0,25 s statt 0,5 s. | 20 |
| 449 | SlavedUpdate.SetRepairState (None nach Unpacking) | A | _framesToWait = 15 Ticks für das Auspacken vor dem ersten Schweißen | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (m_framesToWait = 15, Z.595) | Auspacken dauert 0,25 s statt 0,5 s; Heilbeginn entsprechend früher. | 20 |
| 453 | SlavedUpdate.SetRepairState (Welding nach Retracting) | A | _framesToWait = 5 Ticks für das Einziehen des Arms nach dem Schweißen | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (m_framesToWait = 5, Z.600) | Einziehen dauert 0,083 s statt 0,167 s. | 20 |
| 458 | SlavedUpdate.SetRepairState (Ready-Dauer) | B/daten | Zufallsdauer Range(MinReadyTicks, MaxReadyTicks) zur Laufzeit über Grenzen, die beim Modulparsen aus ms in Ticks (ceil) umgerechnet wurden | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (GameLogicRandomValue(m_minReadyFrames, m_maxReadyFrames)) | Ganzzahl-Wertebereich der Ziehung verdoppelt sich; gleiche Ziehung ergibt andere Dauer in Sekunden, nicht bitgleich skalierbar. | 20,23 |
| 466 | SlavedUpdate.SetRepairState (Ready nach Extending) | A | _framesToWait = 5 Ticks für das Ausfahren des Arms vor dem Schweißen | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (m_framesToWait = 5, Z.616) | Ausfahren dauert 0,083 s statt 0,167 s. | 20 |
| 471 | SlavedUpdate.SetRepairState (Weld-Dauer) | B/daten | Zufallsdauer Range(MinWeldTicks, MaxWeldTicks) zur Laufzeit über in Ticks (ceil) umgerechnete Grenzen; bestimmt die Heilmenge je Schweißgang | GameEngine/Source/GameLogic/Object/Update/SlavedUpdate.cpp: SlavedUpdate::setRepairState (GameLogicRandomValue(m_minWeldFrames, m_maxWeldFrames)) | Ganzzahl-Wertebereich der Ziehung verdoppelt sich; Schweißdauer und damit Heilmenge je Gang nicht bitgleich skalierbar. | 20,23 |
| 528 | MobMemberData.CatchUpCrisisBailTime | D | Rohwert über ModuleConfigReader.Int ohne Umrechnung (Default 999999), gezählt in Prüfungen (alle 16 Ticks); Verbrauch Z.598-604, auch als Wert / 3 | GameEngine/Include/GameLogic/Module/MobMemberSlavedUpdate.h: m_catchUpCrisisBailTime (parseUnsignedInt, Default 999999); MobMemberSlavedUpdate.cpp: MobMemberSlavedUpdate::update | Frist = Wert mal Prüfintervall; bei unskaliertem Intervall halbiert sich die Zeit bis zum Direktmarsch und bis zum Tod des Mob-Mitglieds. | 20 |
| 542 | MobMemberSlavedUpdate.CheckInterval | A | Prüfung der Entfernung zum Nexus jeden 16. Tick (Modul wacht jeden Tick und zählt hoch, Z.577) | GameEngine/Source/GameLogic/Object/Update/MobMemberSlavedUpdate.cpp: MobMemberSlavedUpdate::update (if ( ++m_framesToWait < 16), Z.187) | Prüfung alle 0,27 s statt 0,53 s; Ergebnisse: Bewegungsbefehle, Krisenzähler und Zufallsziehungen doppelt so oft. | 20,23 |
| 556 | MobMemberSlavedUpdate.OnSpawn (_framesToWait) | A | Startwert des Prüfzählers = Range(0, 20) Frames (Versatz der ersten Prüfung, Staffelung der Mitglieder) | GameEngine/Source/GameLogic/Object/Update/MobMemberSlavedUpdate.cpp: Konstruktor (m_framesToWait = GameLogicRandomValue(0,20), Z.75) | Versatz halbiert sich zeitlich; wird die Obergrenze skaliert, ändert sich der Wertebereich der Ziehung. | 20,23 |
| 611 | MobMemberSlavedUpdate.Update (Locomotor-Ziehung) | E | Zufallsziehung Range(0, 10) je Prüfung, solange das Mitglied innerhalb des Radius ist und ein Bewegungsziel hat (Ergebnis verworfen) | GameEngine/Source/GameLogic/Object/Update/MobMemberSlavedUpdate.cpp: MobMemberSlavedUpdate::update (Int seed = GameLogicRandomValue( 0, 10 ), Z.288) | Ziehungen je Sekunde folgen dem Prüfintervall; unskaliert doppelt so viele, Zufallsfolge aller Module verschiebt sich. | 23 |
| 729-735,754 | SpawnPointProductionExitUpdate.Revalidate (world.Tick > tick) | E | Tick-Folge: ein im selben Tick beanspruchter Besetzer existiert noch nicht und zählt bis zum Folgetick als vorhanden | eigen | Reine Reihenfolge-Semantik (Spawn am Tickende); bleibt richtig, darf nicht mit der Rate skaliert werden. | 23 |

**`Mod\SpawnModules.cs`** (dazu 1 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 73 | SpawnBehavior.UpdateRate | A | Arbeitsrhythmus des Spawners = LogicFramesPerSecond / 2 (15 Ticks); Schlafrückgabe Z.130 und _workTick Z.127 | GameEngine/Include/GameLogic/Module/SpawnBehavior.h: SPAWN_UPDATE_RATE = LOGICFRAMES_PER_SECOND/2 | Schon von der Rate abgeleitet (60 Hz ergibt exakt 30 Ticks); const muss Laufzeitwert werden. | 20 |
| 100-131,144 | SpawnBehavior.Update / CollectDead | C | Tote Spawns werden nur im UpdateRate-Raster gefunden (Polling statt onSpawnDeath); die Ersatzfrist ReplaceDelayTicks beginnt beim Fund, Ersatz im nächsten Rasterpunkt danach | eigen | Ergebnisse: Ersatzzeitpunkt hängt an der Rasterphase (bis 0,5 s Versatz); bei skaliertem Raster gleich, bei unskaliertem früher und andere Spawn-Reihenfolge. | 20,23 |
| 102-108,125-129 | SpawnBehavior.Update (AggregateHealth) | C | Mit AggregateHealth wacht das Modul jeden Tick: CollectDead und ComputeAggregateStates je Tick, Erzeugung weiter im UpdateRate-Raster über _workTick | Herkunft unbelegt | Nur Last: Gesundheit/Veteranenstufe sind zustandsabgeleitet; Tod des Nexus folgt dem letzten Sklaven einen Tick statt 1/30 s später. | 20 |

**`Mod\SpecialAbilityStaging.cs`** (dazu 3 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 134-139 | SpecialAbilityUpdate.StageTicksLeft | F/tick | Rest-Ticks der laufenden Stufe (Auspacken, Vorbereitung, Einpacken) als öffentliche Eigenschaft für die Präsentation | eigen | Tick-Wert verlässt die Simulation; ein Verbraucher, der mit 30 rechnet, zeigt Fortschritt/Animation mit halber Geschwindigkeit. | 21 |
| 311-316 | SpecialAbilityUpdate.Varied | B/daten | Pack-/Unpack-Ticks (beim Parsen ceil) mal Zufallsfaktor zur Laufzeit, danach Floor und Untergrenze 1 Tick | GameEngine/Source/GameLogic/Object/Update/SpecialAbilityUpdate.cpp: startPacking / startUnpacking (GameLogicRandomValueReal(1 - m_packUnpackVariationFactor, 1 + ...)) | Doppelte Rundung (ceil, dann floor) wirkt bei 60 Hz feiner; Untergrenze 1 Tick ist dann 1/60 s; Dauer nicht exakt das Doppelte in Ticks. | 20,23 |
| 368-374 | SpecialAbilityUpdate.StepCaptureFlash (_flashPhase) | E | Blitzphase wächst je Tick der Vorbereitung um (1 - prepFrames/PreparationTicks) / 3; Ereignis CaptureFlash bei Wechsel ungerade nach gerade | GameEngine/Source/GameLogic/Object/Update/SpecialAbilityUpdate.cpp: SpecialAbilityUpdate::continuePreparation (m_captureFlashPhase += increment / 3.0f, Z.1228) | Summe über doppelt so viele Ticks verdoppelt sich: etwa doppelt so viele CaptureFlash-Ereignisse (Blinken und Tickton) je Übernahme. | 23 |
| 472-484 | SpecialAbilityUpdate.TurnToward | E | Drehung auf der Stelle je Tick um Locomotor.TurnRatePerTick; Einrasten, sobald der Restwinkel höchstens ein Tickschritt ist | eigen | Richtig nur mit skaliertem TurnRatePerTick; Einrastschwelle halbiert sich, Ausrichten und damit Beginn des Auspackens bis zu einen Tick anders. | 20,23 |

**`Mod\SpecialPowerModules.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 107 | SpecialPowerModuleBase.AvailableOnFrame | F/tick | Absoluter Bereitschafts-Tick der Spezialkraft als öffentlicher Lesezugriff für das HUD (ReadyFraction) | eigen | Tick-Wert verlässt die Simulation; HUD muss Restzeit über die Rate statt über 30 umrechnen. | 21 |
| 369-382 | BuildCompleteModule.OnSpawn / Update | E | OnBuildComplete läuft genau einen Tick nach dem Spawn (NextWake = Tick + SleepNone), danach SleepForever | eigen | Reine Tick-Folge: Spezialkraft-Erzeugung und Upgrade-Vergabe 1/60 s statt 1/30 s nach dem Spawn; nicht skalieren. | 23 |
| 589 | SpectreGunshipDeploymentUpdate.TryTakePending (world.Tick - value.Tick <= 2) | E | Toleranz von 2 Ticks zwischen Anforderung des Gunships und seinem Erscheinen (Entität entsteht am Tickende) | eigen | Tick-Folge, keine Zeit: bleibt richtig; bei Skalierung mit der Rate würden fremde ältere Anforderungen fälschlich passen. | 23 |

**`Mod\SpecialsModules.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 112-131 | StickyBombUpdate.Update | C | Bombe wird jeden Tick auf die Position des Opfers gesetzt (SleepNone), totes Opfer führt zur Entfernung | Herkunft unbelegt | Nur Last; Position folgt dem Opfer mit einem Tick Verzug (1/60 s statt 1/30 s). | 20 |
| 200 | GrantStealthData.RadiusGrowRate | D | Rohwert über ModuleConfigReader.Real ohne Umrechnung (hier Default 0, Referenz-Default 10.0f), Verbrauch als Zuwachs je Tick in Z.221 | GameEngine/Source/GameLogic/Object/Behavior/GrantStealthBehavior.cpp: update (m_currentScanRadius += d->m_radiusGrowRate); GameEngine/Include/GameLogic/Module/GrantStealthBehavior.h | Radius wächst doppelt so schnell; der GPS-Scrambler-Marker erreicht FinalRadius in halber Zeit und wird doppelt so früh zerstört. | 20 |
| 218-242 | GrantStealthBehavior.Update | E | Je Tick: _radius += RadiusGrowRate, Scan aller Objekte im Radius, beim Erreichen von FinalRadius letzter Scan und Zerstörung | GameEngine/Source/GameLogic/Object/Behavior/GrantStealthBehavior.cpp: update | Zeitpunkt, zu dem ein Objekt Tarnung erhält, folgt der Schrittzahl; mit skaliertem Zuwachs feinere Radiusstufen und doppelt so viele Scans (Last). | 20,23 |
| 269-279 | WanderAIUpdate.Update | E | Schläft nie; in jedem Tick im Leerlauf zwei Zufallsziehungen Range(5, 50) und ein neuer Bewegungsbefehl | GameEngine/Source/GameLogic/Object/Update/AIUpdate/WanderAIUpdate.cpp: WanderAIUpdate::update (GameLogicRandomValue( 5, 50 ) für x und y) | Ziehungen je Leerlauf-Tick: bleibt ein Objekt trotz Befehl untätig, zieht es doppelt so oft je Sekunde; Zufallsfolge verschiebt sich. | 23 |
| 368-375 | ProjectileStreamUpdate.Update | C | Jeden Tick: verschwundene Projektile vorn aus dem Ringpuffer (20 Plätze) entfernen, Selbstzerstörung wenn leer und Besitzer weg | Herkunft unbelegt | Nur Last; allerdings fasst der Puffer 20 Projektile unabhängig von der Rate (bei schnellerer Schussfolge in Ticks kein Unterschied in Sekunden). | 20 |

**`Mod\StructureModules.cs`** (dazu 6 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 91 | StructureCollapseData.MinBurstDelay (Fallback) | B/fallback | ModuleConfigReader.Ticks mit Fallback 9999 Ticks für MinBurstDelay (Daten setzen den Wert laut Kommentar immer) | GameEngine/Include/GameLogic/Module/StructureCollapseUpdate.h: Konstruktor (m_minBurstDelay = 9999) | Fallback in Ticks: 333 s bei 30 Hz, 167 s bei 60 Hz; praktisch „nie“, nur ohne Datenwert wirksam. | 20 |
| 140 | StructureCollapseUpdate.OnDie (_collapseTick) | B/daten | Zufallsverzögerung Range(MinCollapseDelay, MaxCollapseDelay) zur Laufzeit über beim Parsen in Ticks (ceil) umgerechnete Grenzen | GameEngine/Source/GameLogic/Object/Update/StructureCollapseUpdate.cpp: beginStructureCollapse (m_collapseFrame = now + GameLogicRandomValue(...)) | Ganzzahl-Wertebereich der Ziehung verdoppelt sich; gleiche Ziehung ergibt andere Verzögerung, nicht bitgleich skalierbar. | 20,23 |
| 155,161-166 | StructureCollapseUpdate.Update (_burstTick) | B/daten | Zufallsabstand Range(MinBurstDelay, MaxBurstDelay) in Ticks zwischen Bursts; höchstens ein Burst je Tick (now >= _burstTick, dann += Ziehung) | GameEngine/Source/GameLogic/Object/Update/StructureCollapseUpdate.cpp: update (m_burstFrame += GameLogicRandomValue(d->m_minBurstDelay, d->m_maxBurstDelay)) | Wertebereich der Ziehung verdoppelt sich; bei Abstand 0 entstünde ein Burst je Tick, also doppelt so viele FX/OCL je Sekunde. | 20,23 |
| 157-173 | StructureCollapseUpdate.Update (Einsturz) | E | Expliziter Euler-Schritt je Tick: _height -= _velocity, danach _velocity -= GravityPerTickSquared * (1 - Damping); Ende, wenn Höhe + Vorlagenhöhe <= 0 | GameEngine/Source/GameLogic/Object/Update/StructureCollapseUpdate.cpp: update (m_currentHeight -= m_collapseVelocity; m_collapseVelocity -= m_gravity * (1.0 - m_collapseDamping)) | Höhe vor Geschwindigkeit: Einsturzdauer weicht bei halbem Schritt ab, FINAL-Phase (FX/OCL) und IsCollapsed zu anderem Zeitpunkt; braucht skalierte Schwerkraft. | 20,23 |
| 280 | FireSpreadUpdate.NextDelay | B/daten | Schlafdauer = Max(Range(MinDelay, MaxDelay), SleepNone): Zufall über Tick-Grenzen (ceil beim Parsen), Untergrenze 1 Tick | GameEngine/Source/GameLogic/Object/Update/FireSpreadUpdate.cpp: FireSpreadUpdate::calcNextSpreadDelay (GameLogicRandomValue(m_minSpreadTryDelayData, m_maxSpreadTryDelayData)) | Wertebereich der Ziehung verdoppelt sich; bei Verzögerung 0 Glut-OCL und Übergreifen in jedem Tick, also doppelt so oft je Sekunde. | 20,23 |
| 511-516 | FloatUpdate.Update | C | Aktiviertes Objekt wird jeden Tick auf die Wasserhöhe gesetzt (SleepNone) | Herkunft unbelegt | Nur Last (zustandsabgeleitet). | 20 |

**`Mod\SupplyModules.cs`** (dazu 2 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 279-284 | SupplyCarrierAI.Update | C | Versorgungsautomat läuft vor der Basis-KI jeden Tick, solange er nicht Idle ist (Min(baseSleep, SleepNone)) | Herkunft unbelegt | Nur Last: Übergänge sind zustandsgetrieben; Reaktionslatenz ein Tick (1/60 s statt 1/30 s). | 20 |

**`Mod\ToppleAndFallModules.cs`** (dazu 3 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 52 | ToppleUpdateData.InitialVelocityPercent | D | Rohfaktor (Default 0,2): Winkelgeschwindigkeit je Tick = Stoßtempo mal Faktor (Z.113); Stoßtempo kommt vom Aufrufer (SimulationWorld.Collision.cs, nicht geprüft) | GameEngine/Source/GameLogic/Object/Update/ToppleUpdate.cpp: START_VELOCITY_PERCENT = 0.2f; applyTopplingForce (m_angularVelocity = toppleSpeed * m_initialVelocityPercent) | Radiant je Tick: ist das Stoßtempo je Tick skaliert, halbiert sich die Geschwindigkeit mit, sonst kippt das Objekt doppelt so schnell. | 20 |
| 53 | ToppleUpdateData.InitialAccelPercent | D | Rohfaktor (Default 0,01): Winkelbeschleunigung je Tick² = Stoßtempo mal Faktor (Z.114), ohne Umrechnung | GameEngine/Source/GameLogic/Object/Update/ToppleUpdate.cpp: START_ACCEL_PERCENT = 0.01f; applyTopplingForce (m_angularAcceleration = toppleSpeed * m_initialAccelPercent) | Je Tick²: auch mit skaliertem Stoßtempo (Faktor 1/2) fehlt ein weiterer Faktor 1/2; Beschleunigung je Sekunde² wäre doppelt. | 20 |
| 73 | ToppleUpdate.VelocityBounceLimit | A | Schwelle 0,01 Radiant je Frame: nach dem Abprall darunter bleibt das Objekt liegen (Tod durch TOPPLED) | GameEngine/Source/GameLogic/Object/Update/ToppleUpdate.cpp: ToppleUpdate::update (const Real VELOCITY_BOUNCE_LIMIT = 0.01f) | Geschwindigkeit je Tick ist bei 60 Hz halb so groß; unskalierte Schwelle beendet das Abprallen früher (andere Zahl Abpraller, anderer Todeszeitpunkt). Skalierung mit 30/Rate. | 20,23 |
| 144-168 | ToppleUpdate.Update | E | Euler je Tick: Winkel += Geschwindigkeit (auf ANGULAR_LIMIT gekappt), Abprall mit BounceVelocityPercent, sonst Geschwindigkeit += Beschleunigung; im Abprall-Tick keine Beschleunigung | GameEngine/Source/GameLogic/Object/Update/ToppleUpdate.cpp: ToppleUpdate::update | Fallzeit, Zahl der Abpraller und Todes-Tick ändern sich; der ausgelassene Beschleunigungsschritt beim Abprall ist an den Tick gebunden. | 23 |
| 209 | StructureToppleData.StructuralDecay | D | Rohfaktor (hier Default 1, Referenz-Konstruktor 0.0f), multiplikativer Zerfall der Strukturintegrität je Tick in Z.316 | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: update (m_structuralIntegrity *= d->m_structuralDecay); GameEngine/Include/GameLogic/Module/StructureToppleUpdate.h | Zerfall je Tick: nach einer Sekunde decay^60 statt decay^30; Integrität fällt schneller, Gebäude kippt früher. Umrechnung wäre decay^(30/Rate). | 20,23 |
| 228 | StructureToppleUpdate.AccelFactor | A | Winkelbeschleunigungsfaktor 0,02 je Frame² (mal sin(Winkel) mal (1 - Integrität)), Verwendung Z.313 | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: update (static const Real TOPPLE_ACCELERATION_FACTOR = 0.02f) | Unskaliert fällt das Gebäude in gleicher Tickzahl, also in halber Zeit; Skalierung mit (30/Rate)². | 20,23 |
| 272-273 | StructureToppleUpdate.OnDie (_toppleFrame) | B/daten | Zufallsverzögerung Max(0, Range(MinToppleDelayTicks, MaxToppleDelayTicks)) zur Laufzeit über beim Parsen in Ticks (ceil) umgerechnete Grenzen | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: beginStructureTopple (m_toppleFrame = now + GameLogicRandomValue(d->m_minToppleDelay, d->m_maxToppleDelay)) | Ganzzahl-Wertebereich der Ziehung verdoppelt sich; gleiche Ziehung ergibt andere Verzögerung, nicht bitgleich skalierbar. | 20,23 |
| 302-304,328 | StructureToppleUpdate.Update (_doneAtTick) | E | _doneAtTick = Tick des Aufschlags; Zustand Done (IsToppled) wird erst im Folgetick gesetzt | eigen | Reine Tick-Folge: IsToppled 1/60 s statt 1/30 s nach dem Aufschlag; nicht skalieren. | 23 |
| 310-330 | StructureToppleUpdate.StepToppling | E | Euler je Tick: Geschwindigkeit += Beschleunigung, Integrität *= StructuralDecay, Winkel += Geschwindigkeit; Überschuss am Boden wird von der Geschwindigkeit abgezogen | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: update (TOPPLE_ACCELERATION_FACTOR * Sin(m_accumulatedAngle) * (1.0 - m_structuralIntegrity)) | Nichtlineare Rückkopplung über sin(Winkel): Kippdauer ist nicht schrittinvariant; Aufschlag-Tick und Reihenfolge der Schadensschüsse ändern sich. | 23 |
| 333-349 | StructureToppleUpdate.ApplyCrushingDamage | E | Ein Waffenschuss je Tick, in dem die Vorderkante weiter reicht als _lastCrushedDistance (die Referenz schießt in festem räumlichem Abstand entlang der Strecke) | GameEngine/Source/GameLogic/Object/Update/StructureToppleUpdate.cpp: applyCrushingDamage (Schleife über m_lastCrushedLocation bis maxDistance in WEAPON_SPACING-Schritten) | Schusszahl folgt der Tickzahl statt der Strecke: bei 60 Hz rund doppelt so viele Schüsse, also etwa doppelter Schaden unter dem fallenden Gebäude. | 23 |
| 418-452 | HeightDieUpdate.Update (_lastZ) | E | Höhenprüfung jeden Tick; OnlyWhenMovingDown vergleicht Z mit dem Z des Vorticks; InitialDelay wird erst beim ersten Update auf world.Tick addiert | GameEngine/Source/GameLogic/Object/Update/HeightDieUpdate.cpp: HeightDieUpdate::update (Details nicht verglichen) | Feinere Abtastung: Tod bis zu einem halben alten Tick früher und näher an der Zielhöhe; Vorzeichenvergleich bleibt gültig. | 23 |

**`Mod\TransportModules.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 84-93 | ChinookAIUpdate.Update (climb = locomotor.Speed) | E | Höhenänderung des Chinook: je Tick ein Schritt von locomotor.Speed (Horizontalgeschwindigkeit je Tick als Steig-/Sinkrate), auf Zielhöhe geklemmt; solange unterwegs SleepNone | eigen (Abweichung laut Kommentar; Referenz ChinookTakeoffOrLandingState nicht nachgeschlagen) | Steigzeit halbiert sich, falls Speed nicht mit der Rate skaliert; mit skaliertem Speed nur andere Diskretisierung des Landezeitpunkts (Evakuierung wartet auf Landed). | 20,23 |
| 117 | HelicopterSlowDeathData.FramesPerSecond | B/daten | static readonly Fixed aus LogicFramesPerSecond; teilt in Parse 4 Felder (SpiralOrbitTurnRate, SpiralOrbitForwardSpeed, MinSelfSpin, MaxSelfSpin; Z. 125-129) von je Sekunde auf je Tick, an PerSecondToPerTick vorbei | GameLogic/Object/Update/HelicopterSlowDeathUpdate.cpp, Feldtabelle: INI::parseAngularVelocityReal / parseVelocityReal | Folgt der Konstante, aber als statisches Feld beim Typ-Init eingefroren; bei Rate als Laufzeitparameter bliebe der Teiler auf dem ersten Wert stehen. Keine Rundung (Fixed). | 20 |
| 127 | HelicopterSlowDeathData.Parse "SpiralOrbitForwardSpeedDamping" | D | Dämpfungsfaktor je Frame roh gelesen (ModuleConfigReader.Real, Fallback 1), in Update Z. 234 je Tick multipliziert | HelicopterSlowDeathUpdate.cpp: INI::parseReal, m_spiralOrbitForwardSpeedDamping = 1.0f, update: m_forwardSpeed *= m_spiralOrbitForwardSpeedDamping | Bei 60 Hz wird doppelt so oft gedämpft: die Vorwärtsgeschwindigkeit fällt pro Sekunde quadratisch stärker, die Absturzspirale wird kürzer. | 20,23 |
| 131 | HelicopterSlowDeathData.Parse "SelfSpinUpdateAmount" | D | Winkel (Grad→Radiant, keine Zeitumrechnung) als Änderung der Eigendrehrate je Aktualisierung gelesen; die Teilung durch die Rate geschieht erst zur Laufzeit (Z. 218) | HelicopterSlowDeathUpdate.cpp: INI::parseAngleReal, m_selfSpinUpdateAmount | Siehe Z. 218: Bedeutung hängt an der Laufzeit-Teilung. | 20 |
| 147-149 | HelicopterSlowDeathData.FixedTicks | B/daten | Millisekunden → ungerundete Fixed-Ticks (DurationMillisecondsToFixedTicks) zur Parse-Zeit; 4 Felder: SelfSpinUpdateDelay, MinBladeFlyOffDelay, MaxBladeFlyOffDelay, DelayFromGroundToFinalDeath | HelicopterSlowDeathUpdate.cpp, Feldtabelle: INI::parseDurationReal | Andere Rundung als ceil (keine); die Rundung entsteht erst durch die Vergleiche/Countdowns zur Laufzeit (Z. 216, 237, 257). | 20 |
| 191 | HelicopterSlowDeathBehavior.BeginSlowDeath (SinkDelayVarianceTicks-Wurf) | E | Zufallswurf Range(0, SinkDelayVarianceTicks) in Tick-Einheiten, Ergebnis verworfen (nur Verbrauch des Logikstroms) | Herkunft unbelegt (Kommentar nennt SlowDeathBehavior::beginSlowDeath, hier nicht nachgeschlagen) | Obergrenze des Wurfs verdoppelt sich; ob der Stromzustand danach gleich bleibt, hängt von LogicRandom.Range ab (nicht geprüft). | 20,23 |
| 192-193 | HelicopterSlowDeathBehavior.BeginSlowDeath (destruction) | E | DestructionDelayTicks + Range(0, DestructionDelayVarianceTicks): Zufallswurf mit Tick-Körnung, mindestens SleepNone (1 Tick), ergibt _destructionTick | Herkunft unbelegt (SlowDeathBehavior::beginSlowDeath nicht nachgeschlagen) | Wurf über doppelt so viele Ganzzahlwerte: gleiche Zeitspanne, aber andere konkrete Verzögerungen als bei 30 Hz. | 20,23 |
| 194 | HelicopterSlowDeathBehavior.BeginSlowDeath (_bladeFlyOff) | E | Zufallswurf Range(MinBladeFlyOffDelay, MaxBladeFlyOffDelay) über ungerundete Fixed-Ticks; in Update Z. 235-238 je Tick um 1 heruntergezählt | HelicopterSlowDeathUpdate.cpp beginSlowDeath: m_bladeFlyOffFrame = GameLogicRandomValueReal(m_minBladeFlyOffDelay, m_maxBladeFlyOffDelay); update: m_bladeFlyOffFrame-- | Zeit bleibt gleich, wenn die Fixed-Ticks skalieren; der gezogene Wert und der Auslösetick (Aufrunden durch <= 0) verschieben sich. | 20,23 |
| 215-234 | HelicopterSlowDeathBehavior.Update (Spin/Orbit) | E | Euler je Tick: Angle += _selfSpin; X/Y += cos/sin(_forwardAngle)·_forwardSpeed; _forwardAngle += SpiralOrbitTurnRate; _forwardSpeed *= Damping (Position vor Winkel, Dämpfung zuletzt) | HelicopterSlowDeathUpdate.cpp update Z. 313-379 (dort Kraft über PhysicsBehavior; hier kinematisch = eigen) | Mit halbierten Je-Tick-Werten gleiche Raten, aber anderer Spiralpfad (Reihenfolge Position/Winkel) und doppelt angewandte Dämpfung. | 20,23 |
| 216 | HelicopterSlowDeathBehavior.Update (SelfSpinUpdateDelay-Vergleich) | B/daten | Laufzeitvergleich (Tick − _lastSelfSpinUpdate) > ungerundete Fixed-Ticks; _lastSelfSpinUpdate startet bei 0 (absoluter Tick), erste Anpassung daher sofort | HelicopterSlowDeathUpdate.cpp update: TheGameLogic->getFrame() - m_lastSelfSpinUpdateFrame > m_selfSpinUpdateDelay | Rundung floor+1 Ticks durch strenges >; bei 60 Hz feinere Körnung, Intervall in Sekunden leicht kürzer. | 20 |
| 218 | HelicopterSlowDeathBehavior.Update (change = SelfSpinUpdateAmount / LogicFramesPerSecond) | A | Laufzeit-Teilung eines Winkels durch 30 (LogicFramesPerSecond) je Spin-Anpassung; _selfSpin ist Radiant je Tick, die Anpassung erfolgt alle SelfSpinUpdateDelay (ms), nicht je Tick | HelicopterSlowDeathUpdate.cpp update Z. 329/343: m_selfSpin += m_selfSpinUpdateAmount / LOGICFRAMES_PER_SECOND | Folgt der Konstante: bei 60 halbiert sich der Je-Tick-Zuwachs, was für eine Rate je Tick richtig ist; läuft aber zur Laufzeit je Ereignis statt zur Parse-Zeit. | 20 |
| 245-254 | HelicopterSlowDeathBehavior.Update (Fall) | E | Euler je Tick: _fallSpeed += GravityPerTickSquared·FallHowFast, dann Z += _fallSpeed (Geschwindigkeit vor Position); Aufschlag bei Z <= Boden + 1 | HelicopterSlowDeathUpdate.cpp update: pos->z <= ground + 1.0f (Schwelle belegt); Fall dort über Locomotor setMaxLift, hier kinematisch = eigen | Halbschritt-Fehler der Integration ändert sich: Aufschlagzeit und -ort (X/Y wandern weiter) weichen ab, damit FXHitGround/OCLHitGround-Zeitpunkt. | 20,23 |
| 257 | HelicopterSlowDeathBehavior.Update (DelayFromGroundToFinalDeath-Vergleich) | B/daten | Laufzeitvergleich (Tick − _hitGroundTick) > ungerundete Fixed-Ticks; _hitGroundTick == 0 dient zugleich als „noch nicht aufgeschlagen“ | HelicopterSlowDeathUpdate.cpp update Z. 477-478 | Rundung floor+1 Ticks durch strenges >; bei 60 Hz bis zu einem halben alten Tick früher. | 20 |
| 385 | HelixContain.Update (SleepNone solange Aufbau getragen) | C | Solange ein PORTABLE_STRUCTURE getragen wird, läuft das Modul jeden Tick und kopiert Position/Winkel | eigen (Referenz HelixContain.cpp nicht nachgeschlagen) | Nur Last: der Aufbau folgt weiterhin jeden Tick, Ergebnis unverändert. | 20 |
| 419 | TunnelContainData.Parse "TimeForFullHeal" | B/fallback | Millisekunden → ungerundete Fixed-Ticks zur Parse-Zeit; fehlt das Feld: Fixed.Zero (= sofort volle Heilung), Referenz-Standard ist 1 Frame | GameEngine/Include/GameLogic/Module/TunnelContain.h: m_framesForFullHeal = 1.0f, "TimeForFullHeal" INI::parseDurationReal | Keine Rundung; der abweichende Standard (0 statt 1 Frame) wirkt in beiden Fällen als Vollheilung im ersten Tick. | 20 |
| 517-534 | TunnelContain.Update (Heilung) | E | Jeden Tick (SleepNone) je Tunnel: Heilung MaxHealth / TicksForFullHeal je Insasse, volle Heilung sobald (Tick − EnteredAt) >= TicksForFullHeal; mit n Tunneln n-mal je Tick | GameLogic/Object/Contain/TunnelContain.cpp update: tunnelSystem->healObjects(modData->m_framesForFullHeal) | Mit skalierten Ticks gleiche Heilrate; ohne Skalierung doppelte Heilung je Sekunde. Sliver ist Fixed, daher nur Rundungsunterschiede. | 20,23 |

**`Mod\VehicleModules.cs`** (dazu 2 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 110 | BattleBusData.Parse "ThrowForce" | D | Roh gelesener Kraftstoß (Fallback 1); in BeginSlowDeath Z. 178 als _verticalSpeed = ThrowForce / Masse direkt zur Geschwindigkeit je Tick | GameLogic/Object/Behavior/BattleBusSlowDeathBehavior.cpp: m_throwForce = 1.0f, "ThrowForce" INI::parseReal, beginSlowDeath: throwForce.z = data->m_throwForce | Gleicher Je-Tick-Impuls bei halber Tickdauer: der Bus startet doppelt so schnell (Einheiten/s); mit skalierter Schwerkraft fliegt er etwa viermal so hoch. | 20,23 |
| 124 | BattleBusSlowDeathBehavior.GroundCheckDelay | A | Konstante 10 Ticks: Bodenkontakt wird erst danach geprüft (1 Verwendung, Z. 173; Vergleich Tick > _groundCheckTick Z. 195) | BattleBusSlowDeathBehavior.cpp: enum GROUND_CHECK_DELAY = 10 | Sperrzeit halbiert sich auf 1/6 s; bei flachem Wurf könnte die Landung früher erkannt werden. | 20 |
| 125 | BattleBusSlowDeathBehavior.EmptyHulkCheckDelay | A | Konstante 15 Ticks als Schlafrückgabe (1 Verwendung, Z. 223): Abfrage, ob die Hülle leer ist | BattleBusSlowDeathBehavior.cpp: enum EMPTY_HULK_CHECK_DELAY = 15, UPDATE_SLEEP(EMPTY_HULK_CHECK_DELAY) | Abfrage doppelt so oft; Ergebnis betroffen: Start und Ablauf der Leerfrist werden nur an Abfragepunkten erkannt (Verzug bis 15 Ticks = 0,25 statt 0,5 s). | 20 |
| 165 | BattleBusSlowDeathBehavior.BeginSlowDeath (SinkDelayVarianceTicks-Wurf) | E | Zufallswurf Range(0, SinkDelayVarianceTicks) in Tick-Einheiten, Ergebnis verworfen | Herkunft unbelegt (SlowDeathBehavior::beginSlowDeath nicht nachgeschlagen) | Obergrenze verdoppelt sich; Wirkung auf den Logikstrom hängt von LogicRandom.Range ab (nicht geprüft). | 20,23 |
| 166-168 | BattleBusSlowDeathBehavior.BeginSlowDeath (destruction) | E | DestructionDelayTicks + Range(0, DestructionDelayVarianceTicks), mindestens 1 Tick, ergibt _destructionTick und Weckzeit | Herkunft unbelegt (SlowDeathBehavior::beginSlowDeath nicht nachgeschlagen) | Gleiche Zeitspanne, aber andere gezogene Verzögerung wegen doppelter Tick-Körnung. | 20,23 |
| 188-203 | BattleBusSlowDeathBehavior.Update (Wurf) | E | Euler je Tick: Z += _verticalSpeed, dann _verticalSpeed += GravityPerTickSquared (Position vor Geschwindigkeit), Klemmen auf Boden, Landung wenn Tick > _groundCheckTick und Z <= Boden | eigen (kinematisch; Referenz schiebt über PhysicsBehavior, BattleBusSlowDeathBehavior.cpp update) | Flugbahn und Landetick ändern sich mit der Schrittweite; zusammen mit ThrowForce (roh) und GroundCheckDelay nicht schrittinvariant. | 20,23 |
| 281 | AnimationSteeringUpdate.MinTransitionTicks | F/tick | Dauer in Ticks (ModuleConfigReader.Ticks "MinTransitionTime", Z. 269) wird der Präsentation gereicht (laut Kommentar UnitConditionTracker; dort nicht gelesen) | Herkunft unbelegt (Kommentar nennt m_transitionFrames; AnimationSteeringUpdate.cpp nicht nachgeschlagen) | Wert verdoppelt sich mit der Rate; der Verbraucher in der Präsentation muss ihn weiter als Simulationsticks oder als Zeit deuten. | 21 |

**`Mod\WorldEffectModules.cs`** (dazu 14 schlichte Umrechnungsaufrufe, §2)

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 108-154 | OCLUpdate.Update (SleepNone) | C | Modul läuft jeden Tick und vergleicht Tick >= _nextCreation; _nextCreation == 0 ist Marke „Timer noch nie gestartet“ | GameLogic/Object/Update/OCLUpdate.cpp update: UPDATE_SLEEP_NONE, m_nextCreationFrame == 0 | Nur Last; Besitzerwechsel wird einen (kürzeren) Tick später erkannt. | 20 |
| 158 | OCLUpdate.SetNextCreation | E | Zufallswurf Range(MinDelay, MaxDelay) in ganzen Ticks, _nextCreation = Tick + delay | OCLUpdate.cpp Z. 245-248: GameLogicRandomValue(m_minDelay, m_maxDelay), m_nextCreationFrame = m_timerStartedFrame + delay | Gleiche Zeitspanne, aber Wurf über doppelt so viele Werte: konkrete Abwurf-/Verstärkungszeiten weichen von 30 Hz ab. | 20,23 |
| 247-262 | DynamicShroudClearingRangeUpdate.Update (Wachsen/Schrumpfen) | E | Je Tick: _current += native / GrowTime bzw. −= (native − FinalVision) / ShrinkTime (Tickzahlen aus ceil-gerundeten Dauern, mind. 1) | GameLogic/Object/Update/DynamicShroudClearingRangeUpdate.cpp update (Zustandsfolge Z. 223-229 belegt; Schrittformel nicht verglichen) | Schritt skaliert mit den Tickzahlen, also invariant bis auf die ceil-Rundung von GrowTime/ShrinkTime. | 20,23 |
| 264-272 | DynamicShroudClearingRangeUpdate.Update (_stateCountdown/_changeIntervalCountdown) | C | Tick-Countdowns: _stateCountdown-- je Tick; Sichtweite wird übernommen, wenn _changeIntervalCountdown 0 ist, dann auf ChangeInterval/GrowInterval gesetzt (Periode = Intervall + 1 Ticks) | DynamicShroudClearingRangeUpdate.cpp update Z. 271-279 („it is important that this gets called every frame without sleeping“) | Ergebnis betroffen: die Periode Intervall+1 Ticks entspricht bei 60 Hz einer anderen Zeit (der +1-Tick halbiert sich); Sichtweitenstufen fallen auf andere Zeitpunkte. | 20,23 |
| 314 | FireOclAfterCooldownData.Parse "OCLLifetimeMaxCap" | B/fallback | ModuleConfigReader.Ticks mit Fallback 1000 Ticks (= 33,3 s bei 30 Hz); im selben Aufruf OCLLifetimePerSecond roh (ms Lebenszeit je Sekunde Feuer, Fallback 1000; ratenneutrales Verhältnis) | GameLogic/Object/Update/FireOCLAfterWeaponCooldownUpdate.cpp Konstruktor: m_oclMaxFrames = 1000, m_oclLifetimePerSecond = 1000; "OCLLifetimeMaxCap" INI::parseDurationUnsignedInt | Fallback bliebe 1000 Ticks = 16,7 s; ceil-Rundung des Datenwerts zur Parse-Zeit. | 20 |
| 359 | FireOCLAfterWeaponCooldownUpdate.Update (LastFireTickOf == now − 1) | E | Schussserie wird erkannt, wenn der letzte Schuss genau im Vortick fiel; verlangt, dass das Modul jeden Tick läuft (SleepNone Z. 373) und nach dem Waffenschritt gelesen wird | FireOCLAfterWeaponCooldownUpdate.cpp update Z. 143: weapon->getLastShotFrame() == now - 1 | Semantik „ein Tick später“ bleibt, solange jeder Tick läuft; Serienabbruch (ReadyTick < now) wird in feinerer Körnung erkannt. | 23 |
| 385 | FireOCLAfterWeaponCooldownUpdate.FireOcl (lifetime) | B/daten | Laufzeitrechnung (Tick − _startTick) · LifetimePerSecond / 1000 in Ticks, Ganzzahldivision (abschneiden), gedeckelt auf MaxLifetimeTicks | FireOCLAfterWeaponCooldownUpdate.cpp Z. 200-202: seconds = (now - m_startFrame) * SECONDS_PER_LOGICFRAME_REAL; oclFrames = seconds * LOGICFRAMES_PER_SECOND | Ticks-zu-Ticks-Verhältnis, ratenneutral; nur das Abschneiden wirkt auf halb so große Tickdauern. | 20 |
| 475-479 | WaveGuideUpdate.Update (Splash-Wurf) | A | Alle LogicFramesPerSecond / 2 Ticks (Ganzzahldivision = 15, strenges > ergibt Periode 16) ein Logik-Zufallswurf Range(1,100), Ergebnis verworfen; _splashTick startet bei 0 | GameLogic/Object/Update/WaveGuideUpdate.cpp update Z. 808-815: getFrame() - m_splashSoundFrame > LOGICFRAMES_PER_SECOND / 2.0f, GameLogicRandomValue(1, 100) | Folgt der Konstante (30 Ticks); Anzahl der Würfe je Welle ändert sich leicht (Periode 31 statt 2·16 Ticks) und damit der Logikstrom. | 20,23 |
| 481-490 | WaveGuideUpdate.Update (Advance + DoDamage je Tick) | E | Jeden Tick: ein Schritt entlang des Pfads, Endprüfung (Abstand <= 10 Zellen) und Schadensabtastung an den Frontpunkten der aktuellen Position; jedes Objekt wird nur einmal nass | WaveGuideUpdate.cpp update/doDamage (Ablauf laut Kommentar; nur PATH_EXTRA_DISTANCE und UPDATE_SLEEP_NONE belegt) | Doppelt so viele Abtastpositionen: Objekte am Rand des DamageRadius und der „hinter der Welle“-Test fallen anders aus, Treffer- und Zerstörungstick verschieben sich. | 23 |
| 511-537 | WaveGuideUpdate.Advance | E | Pfadfolger: step = locomotor.Speed je Tick, Rest wird beim Erreichen eines Wegpunkts auf den nächsten übertragen | eigen (Referenz lässt die KI fliegen, laut Kommentar) | Mit skaliertem Speed gleiche Strecke je Sekunde (Übertrag macht den Weg schrittinvariant); ohne Skalierung doppelt so schnelle Welle. | 20,23 |
| 628 | ScanData.Parse "ScanDelayTime" | B/fallback | ModuleConfigReader.Ticks mit Fallback SimulationTiming.LogicFramesPerSecond (= 1 s in Ticks); für CheckpointUpdate und EnemyNearUpdate | GameEngine/Include/GameLogic/Module/CheckpointUpdate.h und EnemyNearUpdate.h: m_enemyScanDelayTime = LOGICFRAMES_PER_SECOND | Folgt der Konstante (bleibt 1 s), wird aber zur Parse-Zeit des Moduls gebunden; ceil-Rundung des Datenwerts. | 20 |
| 640 | CheckpointUpdate.GateStep | D | Konstante 0,333 Längeneinheiten je Tick: Tor (Minor-Radius) schrumpft/wächst je Tick um diesen Wert (2 Verwendungen, Z. 673/675; kein Klemmen auf 0 bzw. max) | GameLogic/Object/Update/CheckpointUpdate.cpp update Z. 157/161: radius - 0.333f / radius + 0.333f | Tor öffnet und schließt doppelt so schnell; GateRadius ist Zustand (Hash), überschießt weiterhin um bis zu einen Schritt. | 20,23 |
| 662 | CheckpointUpdate.OnSpawn (_scanDelay-Wurf) | E | Zufallswurf Range(0, ScanDelayTicks) in Ticks; der Wert wird gespeichert, aber in Update nie gelesen (Scan läuft jeden Tick) | CheckpointUpdate.cpp Konstruktor Z. 55: m_enemyScanDelay += GameLogicRandomValue(0, m_enemyScanDelayTime) | Nur der gespeicherte Wert (Zustandshash) und ggf. der Logikstrom ändern sich; kein Verhalten hängt daran. | 20,23 |
| 665-676 | CheckpointUpdate.Update (Scan je Tick) | C | Jeden Tick zwei Vollsuchen über alle Objekte (nächster Feind/Verbündeter), danach Torschritt; kein Scan-Intervall | CheckpointUpdate.cpp update Z. 71: if (m_enemyScanDelay == 0 \|\| TRUE) | Last verdoppelt sich (2 O(n)-Suchen je Tick und Checkpoint); Ergebnis: Auf/Zu wird einen kürzeren Tick früher erkannt, Torbewegung siehe GateStep. | 20 |
| 713 | EnemyNearUpdate.OnSpawn (_scanDelay-Wurf) | E | Zufallswurf Range(0, ScanDelayTicks) als Startversatz des Scans in Ticks | GameLogic/Object/Update/EnemyNearUpdate.cpp Konstruktor Z. 51: GameLogicRandomValue(0, m_enemyScanDelayTime) | Versatz über doppelt so viele Werte gezogen: erster Scan fällt auf einen anderen Zeitpunkt als bei 30 Hz. | 20,23 |
| 715-724 | EnemyNearUpdate.Update (_scanDelay-Countdown) | C | Tick-Countdown je Tick (SleepNone): bei 0 Scan und Rücksetzen auf ScanDelayTicks, sonst −1; Periode = ScanDelayTicks + 1 Ticks | EnemyNearUpdate.cpp update Z. 68-78: if (m_enemyScanDelay == 0) … else --m_enemyScanDelay | Ergebnis betroffen (IsEnemyNear ist Zustand, treibt ENEMYNEAR-Animation): Periode 61 statt 2·31 Ticks, Scanzeitpunkte driften gegenüber 30 Hz. | 20,23 |
| 796-819 | FirestormDynamicGeometryInfoUpdate.Update (Geometrie-Übergang) | E | Je Tick: _startDelay--, ratio = _timeActive / TransitionTime, _timeActive++; Ende erst bei _timeActive > TransitionTime (TransitionTime + 1 Schritte, bei Umkehr zweimal) | DynamicGeometryInfoUpdate.cpp update Z. 126-163: m_startingDelayCountdown--, ratio = m_timeActive / m_transitionTime, m_timeActive++ | Linear in Ticks, also invariant bis auf den Zusatztick je Phase und die ceil-Rundung; Radius zum Zeitpunkt eines Schadensscans weicht minimal ab. | 20,23 |
| 827-831 | FirestormDynamicGeometryInfoUpdate.Update (Schadensscan) | E | Fester DamageAmount je Scan, Scan wenn Tick − _lastDamage >= DamageDelay (ceil-Ticks, Fallback 0); bei DamageDelay 0 Schaden jeden Tick; _lastDamage startet bei 0 (absoluter Tick) | GameLogic/Object/Update/FirestormDynamicGeometryInfoUpdate.cpp update Z. 215-219; m_delayBetweenDamageFrames = 0.0f, INI::parseDurationReal (dort ungerundet) | Bei Daten ohne Verzögerung verdoppelt sich der Schaden je Sekunde; mit Verzögerung gleiche Rate, aber ceil statt ungerundet rundet bei 60 Hz anders (Daten nicht geprüft). | 20,23 |
| 957 | WeaponBonusUpdate.Update (_nextPulse = now + Max(Delay, 1)) | C | Pulsintervall BonusDelay in Ticks, mindestens 1 Tick (bei Delay 0 Puls jeden Tick); Schlafrückgabe Z. 961 = Abstand zum nächsten Puls/Ablauf | GameLogic/Object/Update/WeaponBonusUpdate.cpp update Z. 165: return UPDATE_SLEEP(data->m_bonusDelay) | Mit Delay > 0 gleiche Zeit (ceil-Rundung); bei Delay 0 nur Last (doppelt so viele Vollsuchen), Bonusdauer bleibt Zeit. | 20 |

### 5.4 Skript-VM (`Sim\Scripting`)

**`Sim\Scripting/ScriptEngine.Actions.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 20 | ScriptEngine.RetargetInterval | C | Konstante 30 Ticks; 1 Verwendung (Z. 611: `_tick % RetargetInterval != follower.Entity % RetargetInterval`), nach Entity-Id gestaffeltes Neuzielen von Jagd-/Angriffs-/Gebiets-/Positionsaufträgen | eigen | Neuzielen alle 0,5 s statt 1 s; ändert Ergebnisse (Zeitpunkt und Anzahl der Bewegungs-/Angriffsbefehle, Staffelung nach Id), nicht nur Last | 20 |
| 50-59 | ScriptEngine.ExecuteAction SET_TIMER/SET_RANDOM_TIMER (frames) | D | Frame-Zahl aus dem Kartenskript roh in den Zähler (`timer.Value = frames`), bei SET_RANDOM_TIMER Zufallsbereich ebenfalls in Frames; Zähler wird außerhalb des Pakets je Tick dekrementiert | ScriptEngine.cpp ScriptEngine::setTimer (millisecondTimer=false: value = getInt, unkonvertiert) | Skript-Timer laufen in halber Echtzeit ab; Wert muss mit Rate/30 skaliert werden (Zufallsziehung vor oder nach Skalierung festlegen) | 20 |
| 77-82 | ScriptEngine.ExecuteAction ADD_TO_MSEC_TIMER/SUB_FROM_MSEC_TIMER | B/daten | Laufzeit-Arithmetik auf dem Tick-Zähler: `Value += SecondsToFrames(x)` bzw. `Value -= SecondsToFrames(x)`; jede Aktion rundet einzeln auf (Rundung summiert sich); SUB zieht ceil(+x) ab, das Original addiert ceil(-x) | ScriptEngine.cpp ScriptEngine::adjustTimer (value = -value; += REAL_TO_INT_CEIL(...)) | Aufgelaufene Rundungsreste halbieren sich in Echtzeit; die Abweichung SUB (ceil(+x) statt ceil(-x)) tritt bei 60 Hz für andere Bruchwerte auf als bei 30 Hz | 20 |
| 109 | ScriptEngine.ExecuteAction QUICKVICTORY End(..., 1) | E | Inline-Literal 1 Tick als Endspiel-Countdown («sofort, einen Tick später»); VICTORY/DEFEAT/LOCALDEFEAT nutzen dagegen EndGameDelayTicks (=120, deklariert in ScriptEngine.cs:72, außerhalb des Pakets; 3 Verwendungen Z. 107, 108, Phase13 Z. 64) | ScriptEngine.cpp ScriptEngine::startQuickEndGameTimer (m_endGameTimer = 1) | Nicht skalieren: bedeutet «nächster Tick»; Ende kommt 1/60 s statt 1/30 s später | 20 |
| 354-359 | ScriptEngine.SecondsToFrames | B/daten | Eigene Umrechnung Sekunden→Ticks zur Laufzeit je Aktion: `seconds * SimulationTiming.LogicFramesPerSecond`, Ceiling; nicht über SimulationTiming.SecondsToTicks; 3 Verwendungen (Z. 67, 78, 81: SET_MILLISECOND_TIMER, SET_RANDOM_MSEC_TIMER, ADD_TO/SUB_FROM_MSEC_TIMER) | ScriptEngine.cpp ScriptEngine::setTimer / adjustTimer (REAL_TO_INT_CEIL(ConvertDurationFromMsecsToFrames(value*1000))) | Skaliert mit der Konstante automatisch; Aufrundung auf ganze Ticks ergibt bei 60 Hz andere Echtzeit (max. 1/60 s statt 1/30 s Überhang) | 20 |
| 385-393 | ScriptEngine.Forward (arguments) | D | Parameter weitergereichter Aktionen gehen unkonvertiert als Text an die Präsentation, darunter Frame-Zahlen (CAMERA_FADE_*: Frames Anstieg/Halten/Abfall) und Sekundenwerte; keine Einheit im ScriptOutput | ScriptEngine.cpp ScriptEngine::setFade (m_fadeFramesIncrease/Hold/Decrease = getInt) | In der Simulation kein Unterschied; der Verbraucher muss Frame-Parameter als 1/30 s deuten, nicht als Ticks, sonst halbe Blend-/Kameradauern | 21 |
| 394 | ScriptEngine.Forward new ScriptOutput(_tick, ...) | F/tick | Jede Skriptausgabe trägt den Simulations-Tick als Zeitstempel für den Client | eigen | Tick-Zeitstempel: Verbraucher, die daraus Zeit ableiten (Tick/30), laufen doppelt so schnell | 21 |
| 398-405 | ScriptEngine.Forward _pendingPresentation / ScriptEngineOptions.PresentationLatencyTicks | B/literal | Host-Option in Ticks (Wert kommt vom Aufrufer, Standard 0, deklariert in ScriptEngine.cs:20): `_pendingPresentation[key] = _tick + PresentationLatencyTicks` als absoluter Tick; steuert HAS_FINISHED_*/CAMERA_MOVEMENT_FINISHED (Conditions Z. 220-226) | eigen | Gleiche Tickzahl ist halbe Echtzeit: Skripte, die auf Sprach-/Kameraende warten, laufen früher weiter; Aufrufer müssen den Wert mit der Rate bilden | 20,21 |
| 574-634 | ScriptEngine.UpdateFollowers (inkl. UpdateTeamFollower, Phase13 Z. 925-962) | C | Je Tick ohne Staffelung: Ankunftsprüfung am Wegpunkt (fester Radius 40), Neuausgabe des Marschbefehls wenn nicht marschierend, FireAtOrdered je Tick sobald Waffe bereit; nur der Rest läuft im 30-Tick-Raster | eigen | Ergebnisse ändern sich leicht: feinere Abtastung (Ankunft, Schuss, Wegpunktwechsel bis 1/60 s früher), doppelt so viele Abfragen und ggf. Befehle je Sekunde | 23 |

**`Sim\Scripting/ScriptEngine.Conditions.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 81-90 | ScriptEngine.EvaluateCondition PLAYER_LOST_OBJECT_TYPE | E | Flankenerkennung gegen den Stand der vorigen Auswertung (Zähler `count:<player>:<type>`), also je Auswertungstakt des Skripts | - | Ergebnis hängt am Auswertungstakt: bei 60 Hz doppelt so viele Vergleichspunkte; Verlust und Ersatz innerhalb eines 30-Hz-Ticks werden sichtbar | 23 |
| 129-138 | ScriptEngine.EvaluateCondition NAMED_ENTERED_AREA/NAMED_EXITED_AREA | E | Betreten/Verlassen als Wechsel gegenüber der vorigen Auswertung (`_areaInside`), wahr genau für eine Auswertung | - | Wahr nur für einen Tick (1/60 s); kürzere Durchquerungen werden erkannt; Skripte mit Auswertungsverzögerung können den Wechsel anders treffen | 23 |
| 152-163 | ScriptEngine.EvaluateCondition TEAM_ENTERED_AREA_*/TEAM_EXITED_AREA_* | E | Wie NAMED_ENTERED_AREA: Wechsel gegenüber der vorigen Auswertung (`_areaInside`), wahr für genau eine Auswertung | - | Wahr nur für einen Tick (1/60 s); Abtastung doppelt so fein, Zeitpunkt des Auslösens verschiebt sich um bis zu einen halben alten Tick | 23 |

**`Sim\Scripting/ScriptEngine.ExtendedActions.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 363-392 | ScriptEngine.ProcessTeamBuilds | C | Laut Kommentar je Tick (Aufrufer außerhalb des Pakets): je offener BUILD_TEAM-Bestellung höchstens ein QueueProductionCommand pro Tick; Geldmangel = Wiederholung im nächsten Tick | eigen | Ergebnisse ändern sich: Bestellungen werden in Echtzeit doppelt so schnell eingereiht (begrenzt durch HasRoom/Geld), Befehlsreihenfolge gegenüber anderen Spielerbefehlen verschiebt sich | 23 |

**`Sim\Scripting/ScriptEngine.Phase13.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 245-258 | ScriptEngine.AttachPendingTraps | E | Sprengfalle wird in dem Tick angeheftet, in dem das angeforderte Objekt existiert (Spawn-Warteschlange zwischen den Ticks) – «einen Tick später»-Reihenfolge | eigen | Verzögerung 1/60 s statt 1/30 s; Reihenfolge bleibt, das Opfer kann in der kürzeren Lücke seltener sterben oder sich bewegen | 23 |
| 266-276 | ScriptEngine.UpdateBridgeStates (Changed-Flag) | E | BRIDGE_BROKEN/BRIDGE_REPAIRED: das Wechsel-Flag steht genau ein Update (einen Tick) lang und fällt im nächsten Update wieder | - | Fenster ist 1/60 s statt 1/30 s; Skripte, die nicht in jedem Tick ausgewertet werden, verpassen den Wechsel wie bisher, nur mit halb so langem Fenster | 23 |
| 341 | ScriptEngine.UpdateCaptureOrders `tick % 30 == 0` | C | Inline-Literal 30: stehende Mitglieder eines Übernahmeauftrags bekommen alle 30 Ticks (ungestaffelt, globales Raster) erneut den Marschbefehl; Reichweitenprüfung (25) dagegen je Tick | eigen | Befehl alle 0,5 s statt 1 s; ändert Ergebnisse (Zeitpunkt der Wiederaufnahme, Befehlszahl) | 20 |
| 525 | ScriptEngine.UpdatePendingStructures `tick % SimulationTiming.LogicFramesPerSecond != 0` | C | Wartende Bauaufträge (kein Geld) werden nur geprüft, wenn der Tick ein Vielfaches der Rate ist («höchstens einmal je Sekunde») | eigen | Bleibt 1 s, wenn die Konstante zur Rate wird (ganzzahlige Rate vorausgesetzt); Raster liegt dann auf anderen Ticks – Ergebnis (Zeitpunkt des ConstructCommand) nur über die Rasterlage betroffen | 20 |
| 619 | ScriptEngine.SupplyScanFrames | B/literal | `10 * LogicFramesPerSecond` = 300 Ticks; 1 Verwendung (Z. 840: Schaden an Geldquelle/Dozer/Sammler in den letzten 300 Ticks). Das Original addiert SCAN_RATE = 10 direkt auf Frames (Kommentar dort «10 seconds») und drosselt zusätzlich die Prüfung; hier 10 s, ohne Drosselung | AIPlayer.cpp AIPlayer::isSupplySourceAttacked (const Int SCAN_RATE = 10) | Bleibt 10 s, wenn die Konstante zur Rate wird; Vergleich gegen Entity.LastDamageTick (absoluter Tick) | 20 |
| 689-701 | ScriptEngine.TryEvaluatePhase13Condition SUPPLY_SOURCE_SAFE | C | Kein Rhythmus umgesetzt: das Original wertet nur alle 2*LOGICFRAMES_PER_SECOND neu aus und liefert dazwischen den gemerkten Wert, hier bei jeder Auswertung neu (Kommentar Z. 689) | ScriptConditions.cpp ScriptConditions::evaluateSkirmishSupplySourceSafe (setCustomFrame(getFrame()+2*LOGICFRAMES_PER_SECOND)) | Keine Tickkonstante im Code; Antwort folgt dem Auswertungstakt des Skripts (doppelt so oft, mehr Last); falls der 2-s-Cache nachgezogen wird, in Sekunden ausdrücken | 23 |
| 783-795 | ScriptEngine.UnitHasEmptied `status.Frame == frame - 1` | E | UNIT_EMPTIED ist nur wahr, wenn die vorige Beobachtung genau einen Tick zurückliegt (absoluter Tick in `_transportStatus`, wird gespeichert) und der Container von besetzt auf leer ging | ScriptConditions.cpp ScriptConditions::evaluateUnitHasEmptied (m_frameNumber == frameNum - 1) | Nicht skalieren: Tick-Nachbarschaft. Setzt Auswertung in jedem Tick voraus; würde der Skripttakt auf jeden 2. Tick gestreckt, wird die Bedingung nie wahr | 23 |

**`Sim\Scripting/ScriptEngine.Powers.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 58 | ScriptEngine.TryExecutePowerAction/NAMED_SET_SPECIAL_POWER_COUNTDOWN, NAMED_ADD_SPECIAL_POWER_COUNTDOWN | B/daten | Sekunden (Int) * LogicFramesPerSecond zur Laufzeit je Aktion, ohne Rundung; Ergebnis als absoluter Bereit-Tick (world.Tick + frames bzw. AvailableOnFrame + frames, Z. 59) | ScriptActions.cpp: ScriptActions::doNamedSetSpecialPowerCountdown / doNamedAddSpecialPowerCountdown (LOGICFRAMES_PER_SECOND * seconds) | Stimmt, sobald die Konstante die Rate ist; Laufzeitumrechnung über statische Konstante, keine Rundung. | 20 |

**`Sim\Scripting/ScriptEngine.Sequential.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 43 | ScriptEngine._busyThisTick (IsIdle Z. 126) | E | Objekte mit Befehl aus diesem Tick gelten als beschäftigt, weil der Befehl erst im nächsten Weltschritt ankommt (Ein-Tick-Versatz zwischen Skript und Welt) | eigen | Versatz bleibt ein Tick, also 1/60 s statt 1/30 s; Reihenfolge gleich, Folgeanweisungen der Sequenz kommen entsprechend früher. | 23 |
| 81 | ScriptEngine.TryExecuteSequentialAction/TEAM_GUARD_FOR_FRAMECOUNT, TEAM_IDLE_FOR_FRAMECOUNT | D | Wartezeit der Sequenz roh in Frames aus dem Skriptparameter Int(p,1) an SetSequenceTimer | ScriptActions.cpp: ScriptActions::doTeamIdleForFramecount / doTeamGuardForFramecount -> setSequentialTimer(team, framecount) | Wartezeit halbiert sich in Sekunden; Missionsabläufe (Wellen, Pausen) laufen schneller. | 20 |
| 85 | ScriptEngine.TryExecuteSequentialAction/TEAM_SPIN_FOR_FRAMECOUNT | D | Wartezeit der Sequenz roh in Frames aus dem Skriptparameter Int(p,1) an SetSequenceTimer (ohne Stopp der Einheiten) | ScriptActions.cpp: ScriptActions::doTeamSpinForFramecount -> setSequentialTimer(team, waitForFrames) | Wartezeit halbiert sich in Sekunden. | 20 |
| 92 | ScriptEngine.TryExecuteSequentialAction/UNIT_GUARD_FOR_FRAMECOUNT, UNIT_IDLE_FOR_FRAMECOUNT | D | Wartezeit der Objekt-Sequenz roh in Frames aus dem Skriptparameter Int(p,1) an SetSequenceTimer | ScriptActions.cpp: ScriptActions::doUnitIdleForFramecount / doUnitGuardForFramecount -> setSequentialTimer(obj, framecount) | Wartezeit halbiert sich in Sekunden. | 20 |
| 132-206 | ScriptEngine.ProcessSequences/SequenceItem.FramesToWait | C | Sequenzen werden je Tick einmal geprüft: Anweisung läuft bei Leerlauf oder wenn der Zähler genau 0 ist, sonst FramesToWait-- (Z. 152-155); Warteaktionen (DontAdvance) werden je Tick neu geprüft | ScriptEngine.cpp: ScriptEngine::evaluateAndProgressAllSequentialScripts (m_framesToWait) | Ergebnisse: Wartezähler laufen in halber Zeit ab, Leerlauf wird doppelt so fein abgetastet, der Zeitpunkt jeder Folgeanweisung verschiebt sich. | 20,23 |

**`Sim\Scripting/ScriptEngine.TeamAbilities.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 214 | ScriptEngine.TryExecuteTeamAbilityAction/SCRIPTING_OVERRIDE_HULK_LIFETIME (_hulkLifetimeOverrideTicks) | B/daten | Sekunden (Real) -> Ticks über SecondsToFrames (Fixed * LogicFramesPerSecond, Ceiling) zur Laufzeit je Aktion; nur aufgezeichnet, kein Verbraucher in der Simulation; das Original schneidet ab | ScriptActions.cpp: ScriptActions::doOverrideHulkLifetime, (Int)(seconds * LOGICFRAMES_PER_SECOND) | Stimmt mit parametrierter Konstante; Rundung Ceiling statt Abschneiden weicht vom Original ab (höchstens 1 Tick). | 20 |
| 341 | ScriptEngine.UpdateButtonHunts/_buttonHuntNext | B/fallback | Scan-Intervall der Knopfjagd = max(ScanRate in Ticks, LogicFramesPerSecond): 1 s als Standard (Parse-Fallback ist 0) und zugleich Untergrenze; zur Laufzeit je Scan | CommandButtonHuntUpdate.cpp: CommandButtonHuntUpdateModuleData-Konstruktor, m_scanFrames = LOGICFRAMES_PER_SECOND (die Untergrenze ist eigen) | Stimmt mit parametrierter Konstante; der Rhythmus bestimmt, wann die Kraft eingesetzt wird (Ergebnis), ScanRate selbst ist ceil-gerundet. | 20 |

**`Sim\Scripting/ScriptEngine.TeamEvents.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 15,29-35 | ScriptEngine._pendingTeamScripts | E | Einheit-zerstört-Skripte werden beim Beobachten der Ereignisse gesammelt und erst im nächsten Update ausgeführt (ein Tick später) | eigen | Verzögerung bleibt ein Tick, also 1/60 s statt 1/30 s; Reihenfolge unverändert. | 23 |
| 55-77 | ScriptEngine.ProcessTeamEvents/ScriptTeam.SeeEnemy, CurUnits | C | Feindsicht und Verlustschwelle jedes Teams mit Ereignisskripten werden jeden Tick geprüft, Skript bei Zustandswechsel | Herkunft unbelegt | Überwiegend Last (Sichtprüfung über alle Objekte doppelt so oft); der Flankenzeitpunkt wird feiner, Ergebnisse können sich um einen Tick verschieben. | 23 |
| 79-85 | ScriptEngine.ProcessTeamEvents/ScriptTeam.WasIdle | E | OnIdle-Skript läuft, wenn das Team in zwei aufeinanderfolgenden Ticks untätig ist (idle && WasIdle), und danach jeden weiteren untätigen Tick | Team.cpp: isIdle && m_wasIdle -> runScript(m_scriptOnIdle) (Z. 1914, Funktionsname nicht geprüft) | Das Zwei-Tick-Fenster ist halb so lang; das Skript läuft je Sekunde doppelt so oft, solange das Team untätig bleibt. | 23 |

**`Sim\Scripting/ScriptEngine.TeamQueues.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 40-78 | ScriptEngine.UpdateTeamQueues/UpdateBuildingTeam | C | Teamwarteschlangen werden jeden Tick geprüft; bei teamExecutesActionsOnCreate laufen die Startaktionen jeden Tick, solange ein Mitglied untätig ist (Z. 77) | Herkunft unbelegt (AIPlayer::checkQueuedTeams/checkReadyTeams gefunden, deren Aufrufrhythmus nicht geprüft) | Ergebnisse: Startaktionen laufen je Sekunde doppelt so oft; Aktivierung eines Teams wird doppelt so fein abgetastet. | 20,23 |
| 83 | ScriptEngine.UpdateReadyTeam/timeExpired | A | Ein gesammeltes Team wird spätestens nach 60 * LogicFramesPerSecond Ticks (60 s) aktiv; als Ausdruck mit der Ratenkonstante, je Tick berechnet | AIPlayer.cpp: AIPlayer::checkReadyTeams, m_frameStarted+60*LOGICFRAMES_PER_SECOND | Skaliert über die Konstante mit; ohne Parametrierung wären es 30 s. | 20 |

**`Sim\Scripting/ScriptEngine.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 20 | ScriptEngineOptions.PresentationLatencyTicks | F/tick | Option in Ticks (Standard 0): wie lange Sprache/Ton/Film/Kamerafahrt als laufend gilt; gesetzt in Actions.cs Forward (_tick + Latenz), Ablauf in Update Z. 323; wird auch im MissionSnapshot gespeichert | eigen | Am Simulationstick: gleiche Tickzahl ist halbe Wandzeit, HAS_FINISHED_*/CAMERA_MOVEMENT_FINISHED werden früher wahr, wenn der Aufrufer nicht umrechnet. | 20,21 |
| 26 | ScriptOutput.Tick | F/tick | Jede Präsentationsausgabe der Skripte trägt den Simulationstick als Zeitstempel (Output Z. 554, Forward in Actions.cs) | eigen | Stempel selbst ratenneutral; Verbraucher, die Tickdifferenzen als 1/30 s deuten, laufen doppelt so schnell (Simulationstick, keine Wandzeit). | 21 |
| 72 | ScriptEngine.EndGameDelayTicks | A | Nachlaufzeit nach VICTORY/DEFEAT = 120 Ticks (4 s); _endTimer wird in Update (Z. 316-321) je Tick um 1 verringert; 3 Verwendungen im Code (Actions.cs 107/108, Phase13.cs 64) + 1 Test | ScriptEngine.cpp: FRAMES_TO_SHOW_WIN_LOSE_MESSAGE = 120, ScriptEngine::startEndGameTimer | Mission endet 2 s statt 4 s nach der Entscheidung; Sieg-/Niederlagenanzeige halb so lang. | 20 |
| 178 | ScriptEngine..ctor/_nextEvaluation (DelayEvaluationSeconds) | B/literal | Erster Auswertungstick verzögerter Skripte: NameHash % (2*LogicFramesPerSecond+1) = 0..60 Ticks (0..2 s), im Konstruktor; ersetzt den Zufallswert des Originals durch einen Namens-Hash | ScriptEngine.cpp: ScriptEngine::checkConditionsForTeamNames, GameLogicRandomValue(0,2*LOGICFRAMES_PER_SECOND) | Skaliert über die Konstante mit, aber Modulo 121 statt 61 verteilt die Skripte anders: Auswertungsphasen und damit Abläufe ändern sich; keine Rundung. | 20,23 |
| 312-353 | ScriptEngine.Update | C | Alle aktiven Skripte ohne Verzögerung werden jeden Tick ausgewertet (Bedingungen, Aktionen oder FalseActions), danach Follower, Knopfjagd, Teamereignisse, Sequenzen, Teambau, Teamwarteschlangen | ScriptEngine.cpp: ScriptEngine::update | Ergebnisse, nicht nur Last: nicht einmalige Skripte laufen je Sekunde doppelt so oft (Zähler-Inkremente, wiederholte Aktionen/FalseActions, Zufallszüge je Auswertung). | 20,23 |
| 326-327 | ScriptEngine.Update/Counter.Value (IsTimer) | D | Skript-Timer sind Zähler in Frames, je Tick um 1 verringert (bis -1); Werte kommen roh aus SET_TIMER bzw. über SecondsToFrames (Actions.cs), Bedingungen vergleichen sie in Frames | ScriptEngine.cpp: ScriptEngine::update (m_counters[i].value-- für isCountdownTimer) | Frame-Timer aus Kartenskripten laufen in halber Zeit ab; Vergleiche gegen Frame-Literale der Karten bedeuten die halbe Zeit. | 20 |
| 375 | ScriptEngine.ExecuteScriptBody/_nextEvaluation (DelayEvaluationSeconds) | B/daten | Auswertungsintervall eines Skripts: ganze Sekunden * LogicFramesPerSecond, zur Laufzeit bei jeder Auswertung berechnet, ganzzahlig ohne Rundung; kein SimulationTiming-Umrechner | ScriptEngine.cpp: ScriptEngine::executeScript, setFrameToEvaluate(getFrame()+delaySeconds*LOGICFRAMES_PER_SECOND) | Stimmt, sobald die Konstante die Rate ist; Laufzeitumrechnung über statische Konstante muss parametriert werden, keine Rundung. | 20 |

**`Sim\Scripting/ScriptProgram.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 267 | ScriptTeamPrototype.InitialIdleFrames (teamInitialIdleFrames) | D | Kartenfeld in Frames roh gelesen; Verbrauch in TeamQueues.cs Z. 58: _tick > Started + InitialIdleFrames (Bauzeit abgelaufen -> Team geht los oder wird aufgelöst) | AIPlayer.cpp: TeamInQueue::isBuildTimeExpired (m_initialIdleFrames) | Bauzeitfenster eines KI-Teams halbiert sich: Teams werden früher aufgelöst oder unvollständig losgeschickt. | 20 |

### 5.5 Skirmish-KI und Sitzung (`Sim\Skirmish`)

**`Sim\Skirmish/SkirmishAi.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 44 | SkirmishAiStats.AttackTicks | B/literal | Statistikzähler der Angriffsdauer in Ticks; wird je Denkschritt um ThinkInterval (15) erhöht (Z. 226); nur Diagnose/Bericht | eigen | Kein Einfluss auf den Zustand; Verbraucher, die den Wert als 30-Hz-Ticks deuten, zeigen bei 60 Hz die doppelte Dauer. | 20,21 |
| 62 | SkirmishAi.FireInterval | C | Beobachtungsrhythmus 5 Ticks (Observe: Sicht, Gegner-/Lager-Erinnerung, Vergessen); Verwendungen Z. 158 (Phase), Z. 200 (Modulo) | eigen | Ergebnisse ändern sich: Observe läuft pro Sekunde doppelt so oft, Seen-Stempel und Vergessen verschieben sich, damit Befehle und Hashes. | 20 |
| 63 | SkirmishAi.ThinkInterval | C | Denkrhythmus 15 Ticks (0,5 s): alle Befehle der KI entstehen nur an diesen Ticks; Verwendungen Z. 203, 226; muss Vielfaches von FireInterval bleiben (Think wird nur nach bestandenem FireInterval-Test erreicht) | eigen | Ergebnisse ändern sich: KI entscheidet pro Sekunde doppelt so oft (Bauen, Produzieren, Angriffswellen), andere Zufallszüge und Befehls-Ticks. | 20 |
| 64 | SkirmishAi.RetryTicks | B/literal | Wiederholsperre 300 Ticks (10 s); Standardwert von Backoff (Z. 244; Aufrufe ohne Wert Z. 832, 1304) und direkt in Z. 588, 872, 1081, 1129 | eigen | Sperre dauert unskaliert nur 5 s; ×2 ist exakt, keine Rundung. | 20 |
| 65 | SkirmishAi.MemoryTicks | B/literal | Erinnerungsdauer 900 Ticks (30 s) für nicht mehr gesehene bewegliche Gegner (Forget, Z. 300) | eigen | KI vergisst Gegner nach 15 s statt 30 s; ×2 exakt. | 20 |
| 66 | SkirmishAi.OrderRefreshTicks | B/literal | Befehlsauffrischung 150 Ticks (5 s) für stehende Einheiten mit gleichem Ziel (CommandArmy, Z. 1044) | eigen | Marschbefehl wird nach 2,5 s statt 5 s wiederholt, also mehr Befehle im Replay; ×2 exakt. | 20 |
| 69 | SkirmishAi.PlacementChecksPerThink | C | Budget 400 Platzierungsprüfungen je Denkschritt (FindPlacement Z. 643, FindSupplyPlacement Z. 737/762); bei Überschreitung kein Bauplatz in diesem Denkschritt | eigen | Kein Tick-Wert, aber an den Denkrhythmus gebunden: bei unskaliertem ThinkInterval doppelte Last pro Sekunde und doppelt so viele Versuche pro Sekunde (Ergebnis). | 20 |
| 76 | SkirmishAi.BoardingTicks | B/literal | Einsteigefrist 300 Ticks (10 s): so lange gilt eine Einheit als unterwegs zum Tunnel/Transport und erhält keine Marschbefehle (Z. 1083, 1131; geprüft Z. 211, 1034, 1074, 1079, 1123) | eigen | Frist halbiert sich auf 5 s, Einheiten werden früher wieder umkommandiert; ×2 exakt. | 20 |
| 146 | SkirmishAi.RevisitTicks | B/literal | Wiederbesuchsfrist 3600 Ticks (120 s) für geräumte Startpositionen und alte Erkundungszellen (Z. 1192, 1248) | eigen | Wiederbesuch nach 60 s statt 120 s; ×2 exakt. | 20 |
| 158 | SkirmishAi..ctor _phase | C | Versatz der KI-Spieler: _phase = Player.Value % FireInterval, verteilt Beobachten/Denken über die Ticks | eigen | Ergebnisse ändern sich, sobald FireInterval skaliert wird: die Phasen verteilen sich auf mehr Ticks, Denk-Ticks je Spieler verschieben sich. | 20 |
| 207-227 | SkirmishAi.Think (Fristenprüfung) | E | Alle KI-Fristen (_retryAt, _inFlight, _boarding, _powerRetry, _scienceRetry, Order-/Scout-Alter) werden nur an Denk-Ticks geprüft; wirksame Dauer = Frist aufgerundet auf das nächste Denkraster (15 Ticks) | eigen | Wird nur die Frist oder nur das Raster skaliert, ändert sich die wirksame Dauer (z. B. BuildTicks + 30 trifft das Raster anders); beides gemeinsam skalieren. | 20,23 |
| 420 | SkirmishAi.QueueWorker Backoff(…, 60) | B/literal | Wiederholsperre 60 Ticks (2 s) je Produzent/Vorlage beim Einreihen von Baufahrzeugen und Sammlern | eigen | Sperre 1 s statt 2 s, Arbeiter werden schneller nachbestellt; ×2 exakt. | 20 |
| 424 | SkirmishAi.QueueWorker _inFlight Until | B/literal | In-Produktion-Merker läuft bis _tick + template.BuildTicks + 30: Zuschlag 30 Ticks (1 s) auf die Bauzeit (BuildTicks liegt schon in Ticks vor) | eigen | Zuschlag nur 0,5 s; BuildTicks skaliert über die Datenumrechnung mit, der Literalwert 30 nicht. | 20 |
| 588 | SkirmishAi.BuildStructures Backoff(essential ? 60 : RetryTicks) | B/literal | Wiederholsperre je Gebäudevorlage: 60 Ticks (2 s) für das erste Versorgungszentrum im Modus EconomyFirst, sonst RetryTicks | eigen | Sperre 1 s statt 2 s; ×2 exakt. | 20 |
| 637 | SkirmishAi.FindPlacement _random.Range | E | Zufallszug aus dem privaten KI-Strom je Ring und Platzierungsversuch (Startwinkel) | eigen | Anzahl der Züge hängt an Denkrhythmus und Wiederholsperren; ohne Skalierung doppelt so viele Züge pro Sekunde, andere Bauplätze. | 20,23 |
| 722 | SkirmishAi.FindSupplyPlacement Backoff(…, 150) | B/literal | Wiederholsperre 150 Ticks (5 s) für den Erkundungsgang des Baufahrzeugs zur Versorgungsquelle | eigen | Marschbefehl alle 2,5 s statt 5 s; ×2 exakt. | 20 |
| 743 | SkirmishAi.FindSupplyPlacement paths > 12 | C | Budget 12 Wegsuchen je Denkschritt bei der Platzsuche am Lager | eigen | Kein Tick-Wert, aber je Denkschritt: bei unskaliertem Denkrhythmus doppelte Wegsuchlast pro Sekunde. | 20 |
| 805 | SkirmishAi.ManageSites _tick - watch.LastResume < 60 | B/literal | Wartezeit 60 Ticks (2 s) zwischen zwei Wiederaufnahmen derselben Baustelle; nach 3 erfolglosen Wiederaufnahmen (Z. 807) wird abgebrochen | eigen | Baustelle wird nach 3 s statt 6 s ohne Fortschritt abgebrochen; Fehlabbrüche wahrscheinlicher, weil der Bauarbeiter in 1 s seltener ankommt. | 20 |
| 851 | SkirmishAi.BuyScience _tick + 1800 | B/literal | Wiederholsperre 1800 Ticks (60 s) je Wissenschaft nach einem Kaufversuch | eigen | Sperre 30 s statt 60 s; ×2 exakt. | 20 |
| 872 | SkirmishAi.UsePowers _powerRetry | B/literal | Sperre je Spezialkraft: RetryTicks nach Erfolg, 3000 Ticks (100 s) bei fehlender Wissenschaft, 900 Ticks (30 s) bei anderer Ablehnung | eigen | Sperren halbieren sich (50 s / 15 s), mehr abgelehnte Befehle im Mitschnitt; ×2 exakt. | 20 |
| 911 | SkirmishAi.ProduceArmy _random.Range | E | Zufallszug aus dem privaten KI-Strom je freiem Produzenten und Denkschritt (Wahl der Einheit) | eigen | Bei unskaliertem Denkrhythmus fallen die Züge an anderen Ticks und häufiger: andere Armeezusammensetzung, anderer Spielverlauf. | 20,23 |
| 1045 | SkirmishAi.CommandArmy _tick - order.Tick < 60 | B/literal | Mindestabstand 60 Ticks (2 s), bevor eine marschierende Einheit auf ein anderes Ziel umkommandiert wird | eigen | Umkommandieren schon nach 1 s, mehr Befehle und unruhigere Armee; ×2 exakt. | 20 |
| 1088 | SkirmishAi.UseTunnels Backoff(0x81000000u, 90) | B/literal | Wiederholsperre 90 Ticks (3 s) für die Räumung des Tunnelnetzes | eigen | Sperre 1,5 s statt 3 s; ×2 exakt. | 20 |
| 1109 | SkirmishAi.UseTransports Backoff(…, 60) | B/literal | Wiederholsperre 60 Ticks (2 s) je Transporter für den Aussteigebefehl | eigen | Sperre 1 s statt 2 s; ×2 exakt. | 20 |
| 1214 | SkirmishAi.Scout _tick - _scoutAt < 450 | B/literal | Frist 450 Ticks (15 s), in der ein Erkundungsziel erreicht sein muss, sonst gilt die Zelle dauerhaft als unerreichbar (_unreachable) | eigen | Frist 7,5 s: bei gleicher Einheitengeschwindigkeit werden deutlich mehr Zellen fälschlich als unerreichbar abgeschrieben (bleibende Verhaltensänderung). | 20 |
| 1280 | SkirmishAi.ScoutForWarehouses Backoff(…, 300) | B/literal | Wiederholsperre 300 Ticks (10 s) je untätigem Sammler für die Lagersuche | eigen | Sperre 5 s statt 10 s; ×2 exakt. | 20 |
| 1304 | SkirmishAi.OrderCarriers _tick - since < 90 | B/literal | Untätigkeitsdauer 90 Ticks (3 s), nach der ein Sammler zum nächsten Lager geschickt wird (danach Backoff mit RetryTicks) | eigen | Sammler gilt nach 1,5 s als untätig; kann normale kurze Pausen (Andocken, Wenden) als Leerlauf werten. | 20 |

**`Sim\Skirmish/SkirmishMatchSession.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 33 | SkirmishMatchSession.VictoryCheckInterval | C | Siegprüfung alle 15 Ticks nach dem Weltschritt (Z. 115); bestimmt VictoryTick und damit _endTick | eigen | Ergebnisse ändern sich: Sieg wird bis zu 14 Ticks nach dem Ereignis erkannt (0,47 s → 0,23 s); VictoryTick/EndTick und Replay-Ende verschieben sich. | 20 |
| 76 | SkirmishMatchSession.Start ReplayHeader(… LogicFramesPerSecond …) | G | Schreibt die Tickrate (SimulationTiming.LogicFramesPerSecond) in den Replay-Kopf der Sitzung | - | Muss die tatsächliche Rate der Sitzung tragen; bei fester 30 passen Kopf und Lauf nicht mehr zusammen. | 20 |
| 77 | SkirmishMatchSession.Start options.MaxTicks / options.CheckpointInterval | G | Optionen in Ticks gehen in den Replay-Kopf (EndTick, Checkpoint-Intervall) und in _endTick (Z. 47, 119); Standardwerte außerhalb des Pakets: Testing/SkirmishMatchScenario.cs Z. 19 (108_000 = „eine Stunde“, 1_000) | eigen | 108_000 Ticks sind bei 60 Hz nur 30 Minuten; Checkpoints doppelt so dicht in Echtzeit. | 20,21 |
| 119 | SkirmishMatchSession.Step _options.GraceTicks | B/literal | Nachlaufzeit nach dem Sieg in Ticks: _endTick = min(MaxTicks, tick + 1 + GraceTicks); Standardwert 300 (10 s) steht in Testing/SkirmishMatchScenario.cs Z. 19 (außerhalb des Pakets) | eigen | Nachlauf 5 s statt 10 s; ändert EndTick und Endhash des Laufs. | 20 |
| 179 | SkirmishMatchSession.GrantSciencePoints tick % SciencePointInterval | C | Wissenschaftspunkt für jeden Spieler alle SciencePointInterval Ticks (Systembefehl); Standardwert 2_700 (90 s) in Testing/SkirmishMatchScenario.cs Z. 19 | eigen | Ergebnisse ändern sich: Punkte kommen alle 45 s statt 90 s, Generalsfähigkeiten doppelt so schnell. | 20 |

### 5.6 Szenarien und Fahrer (`Sim\Testing`)

**`Sim\Testing/MissionObjectiveDriver.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 35 | MissionObjectiveDriver.Run(maxTicks) | H/budget | Standard-Laufbudget maxTicks = 4500 Ticks (150 s bei 30 Hz); Schleifengrenze world.Tick < maxTicks in Zeile 44 | eigen | Bei 60 Hz nur noch 75 s Spielzeit; Aufrufer mit dem Standardwert erreichen Missionsenden seltener. | 21 |
| 35 | MissionObjectiveDriver.Run(settleTicks) | H/zeit | Einschwingzeit settleTicks = 60 Ticks (2 s) vor der ersten Planungsrunde (Zeile 42) | eigen | Nur 1 s Einschwingen; erste Runde trifft einen früheren Missionszustand, Ergebnis des Fahrers kann abweichen. | 21 |
| 35 | MissionObjectiveDriver.Run(spacing) | C | Planungsrhythmus spacing = 15 Ticks: driver.Round() läuft alle 15 Ticks (Zeilen 46-49, nextAt = Tick + spacing) | eigen | Ergebnisse: Runden doppelt so oft je Spielsekunde, Eingriffe/Befehle fallen auf andere Ticks; Ausgangstick, Eingriffs- und Befehlszähler des Fahrers ändern sich. | 21 |

**`Sim\Testing/MissionScenario.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 13,48 | MissionRunReport.Ticks / MissionRunReport.OutcomeTick | H/ticks | Berichtsfelder in Ticks (erreichter Welt-Tick, Tick des Missionsendes), feldweise von Tests/Werkzeugen verglichen | eigen | Werte verdoppeln sich bei gleicher Spielzeit; feste Erwartungen in Tests/Dokumenten passen nicht mehr. | 21 |
| 25-35 | MissionScenario.Run(ticks) / MissionScenario.Continue(ticks) | H/budget | Laufbudget als absolute Tickzahl vom Aufrufer (kein Literal hier); Schleife session.World.Tick < ticks | eigen | Aufrufer mit festen Tickzahlen (z. B. Audit mit 900 Ticks) laufen bei 60 Hz nur die halbe Spielzeit. | 21 |
| 64 | MissionScenario.VerifyContinuation half | H/ticks | half = ticks / 2: Speichern/Wiederherstellen auf halber Strecke, reines Verhältnis zum übergebenen Budget | eigen | Als Verhältnis ratenneutral; gekoppelt ist nur das vom Aufrufer übergebene Tickbudget. | 21 |
| 94 | MissionProbe.Run(settleTicks) | H/zeit | Einschwingzeit settleTicks = 150 Ticks (5 s) vor dem Ausschalten einer Seite (Zeile 97) | eigen | Nur 2,5 s Einschwingen; Skripte mit Sekundenverzögerung sind beim Ausschalten noch nicht so weit, Endproben können anders ausfallen. | 21 |
| 94 | MissionProbe.Run(waitTicks) | H/zeit | Wartezeit waitTicks = 600 Ticks (20 s) auf das Skript-Ende nach dem Ausschalten (Zeile 106: world.Tick < killTick + waitTicks) | eigen | Nur 10 s Wartezeit; Enden hinter Sekunden-Timern der Skripte werden als nicht erreicht gezählt (Endproben Sieg/Niederlage sinken). | 21 |
| 113-115 | MissionProbeResult.TicksToOutcome | H/ticks | Berichtswert outcomeTick - killTick in Ticks (negativ = vor der Probe entschieden) | eigen | Zahl verdoppelt sich bei gleicher Spielzeit; Auswerter, die sie als Zeit lesen, müssen umrechnen. | 21 |

**`Sim\Testing/ObjectiveDriver.Commands.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 97 | ObjectiveDriver.PublishObjectives (_objectivesAt, 150) | H/zeit | Ziele des Agenten verfallen nach 150 Ticks (5 s) ohne erneute Nennung: _world.Tick - _objectivesAt > 150 | eigen | Verfall nach 2,5 s; der Nur-Befehle-Agent verliert Ziele früher, Angriffsverhalten und Ergebnis (1/24) können sich ändern. | 21 |
| 116 | ObjectiveDriver.Destroy (commands-only Leaf, cooldown 90) | H/zeit | Nur-Befehle-Angriffsblatt: Cooldown 90 Ticks (3 s), max. 100000 Anwendungen; überschreibt den übergebenen Cooldown | eigen | Angriffsbefehle werden alle 1,5 s statt 3 s erneuert; Befehlszahl und Verlauf des Nur-Befehle-Laufs ändern sich. | 21 |
| 219-252 | ObjectiveDriver.Act / ObjectiveDriver.DriveAssaults | C | Je Tick: _agent.Update() (SkirmishAi) und DriveAssaults, das für jedes Angriffspaar FireWeaponCommand ausgibt, sobald ChooseWeapon(...).CanFire | eigen | Überwiegend Last (Feuerfreigabe hängt an der Waffenbereitschaft), aber Befehle fallen auf feinere Ticks; SkirmishAi.Update hat eigene Tickrhythmen (anderes Paket). | 21 |
| 263 | ObjectiveDriver.AgentSummary (stats.AttackTicks) | H/ticks | Textausgabe „attacking N ticks“ ohne Umrechnung in Sekunden | eigen | Zahl verdoppelt sich bei gleicher Spielzeit; reine Diagnoseausgabe. | 21 |

**`Sim\Testing/ObjectiveDriver.Leaves.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 19 | ObjectiveDriver.GuardMargin | E | Schutzrand 150 Welteinheiten um ein Gebiet, weil der Fahrer nur alle paar Ticks schaut und eine Einheit vor der Ankunft entfernen muss (Verwendung Zeile 583); Rand gegen Weg je Runde (15 Ticks) | eigen | Bei unverändertem 15-Tick-Rhythmus halber Weg je Runde, Rand reicht reichlich; wird der Rhythmus auf Zeit umgestellt, bleibt es wie heute. | 21 |
| 125-126 | ObjectiveDriver.GoTo (attempt < 2) | H/zeit | Zwei Bewegungsbefehle, danach Platzierung; die Frist bis zum Platzieren ist 2 × Cooldown des Blatts (90 Ticks = 3 s je Versuch) | eigen | Einheiten haben nur halb so viel Spielzeit zum Laufen, bevor sie versetzt werden; mehr Weltschreibungen. | 21 |
| 128 | ObjectiveDriver.KillTeamLeaf (20, 30) | H/zeit | Blatt „Team zerstören“: Cooldown 30 Ticks (1 s), max. 20 Anwendungen | eigen | Cooldown zählt Ticks: halbe Spielzeit zwischen Anwendungen, Zeitfenster (Anwendungen × Cooldown) halbiert; Eingriffe und Ausgangstick ändern sich. | 21 |
| 130 | ObjectiveDriver.KillPlayerLeaf (20, 60) | H/zeit | Blatt „alles eines Spielers zerstören“: Cooldown 60 Ticks (2 s), max. 20 Anwendungen | eigen | Cooldown zählt Ticks: halbe Spielzeit zwischen Anwendungen, Zeitfenster halbiert; Eingriffe und Ausgangstick ändern sich. | 21 |
| 174 | ObjectiveDriver.LeafFor NAMED_DESTROYED (Destroy kill:, 6, 30) | H/zeit | Blatt „benanntes Objekt zerstören“: Cooldown 30 Ticks (1 s), max. 6 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 3 s statt 6 s; Eingriffe fallen früher, Blatt ist schneller erschöpft. | 21 |
| 184 | ObjectiveDriver.LeafFor NAMED_OWNED_BY_PLAYER (Leaf own: capture, 100000, 90) | H/zeit | Nur-Befehle-Blatt „einnehmen“: Cooldown 90 Ticks (3 s), praktisch unbegrenzt | eigen | Einnahmebefehl wird alle 1,5 s erneut gegeben; Befehlszahl steigt, eine laufende Übernahme kann häufiger neu angestoßen werden. | 21 |
| 185 | ObjectiveDriver.LeafFor NAMED_OWNED_BY_PLAYER (Leaf own: hand, 3, 30) | H/zeit | Blatt „Besitz übertragen“: Cooldown 30 Ticks (1 s), max. 3 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 1,5 s statt 3 s; Blatt schneller erschöpft. | 21 |
| 198 | ObjectiveDriver.LeafFor UNIT_HEALTH (Destroy health:, 4, 30) | H/zeit | Nur-Befehle-Variante „auf Prozent herunterschießen“: übergibt 4/30, Destroy ersetzt im Nur-Befehle-Modus durch 100000/90 (Zeile 116 von ObjectiveDriver.Commands.cs) | eigen | Wie das Angriffsblatt: Befehle alle 1,5 s statt 3 s. | 21 |
| 199 | ObjectiveDriver.LeafFor UNIT_HEALTH (Leaf health: set, 4, 30) | H/zeit | Blatt „Gesundheit setzen“: Cooldown 30 Ticks (1 s), max. 4 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 2 s statt 4 s; Heilung/Regeneration dazwischen wirkt nur halb so lang. | 21 |
| 216 | ObjectiveDriver.LeafFor NAMED_INSIDE_AREA/NAMED_ENTERED_AREA (Leaf go:, 4\|100000, 90\|150) | H/zeit | Blatt „Benanntes in Gebiet schicken“: Cooldown 90 Ticks (3 s), max. 4; Nur-Befehle 150 Ticks (5 s), unbegrenzt | eigen | Halbe Laufzeit je Versuch: Platzierung (ab Versuch 3) kommt nach 3 s statt 6 s; mehr Weltschreibungen, andere Ausgangsticks. | 21 |
| 226 | ObjectiveDriver.LeafFor NAMED_SELECTED (Leaf select:, 3, 15) | H/zeit | Blatt „Benanntes auswählen“: Cooldown 15 Ticks (0,5 s, gleich spacing), max. 3 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 0,75 s statt 1,5 s. | 21 |
| 241 | ObjectiveDriver.LeafFor TEAM_INSIDE/ENTERED_AREA (Leaf goteam:, 4\|100000, 90\|150) | H/zeit | Blatt „Team in Gebiet schicken“: Cooldown 90 Ticks (3 s), max. 4; Nur-Befehle 150 Ticks (5 s), unbegrenzt | eigen | Halbe Laufzeit je Versuch, Platzierung nach 3 s statt 6 s; mehr Weltschreibungen, andere Ausgangsticks. | 21 |
| 266 | ObjectiveDriver.LeafFor MULTIPLAYER_ALLIED_VICTORY (Destroy killenemies, 20, 60) | H/zeit | Blatt „alle Feinde zerstören“: Cooldown 60 Ticks (2 s), max. 20 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 20 s statt 40 s; nachrückende Verstärkungen nach dem Fenster bleiben stehen. | 21 |
| 270 | ObjectiveDriver.LeafFor MULTIPLAYER_ALLIED_DEFEAT/PLAYER_DEFEAT (Leaf killown, 20, 60) | H/zeit | Blatt „eigene Seite zerstören“: Cooldown 60 Ticks (2 s), max. 20 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 20 s statt 40 s. | 21 |
| 282 | ObjectiveDriver.LeafFor PLAYER_HAS_CREDITS (Leaf money:, 3, 30) | H/zeit | Blatt „Kontostand setzen“: Cooldown 30 Ticks (1 s), max. 3 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 1,5 s statt 3 s; Einkommen dazwischen verändert den Stand nur halb so lang. | 21 |
| 297 | ObjectiveDriver.LeafFor PLAYER_HAS_N_OR_FEWER_BUILDINGS (Destroy buildings:, 20, 30) | H/zeit | Blatt „Gebäude bis zur Grenze zerstören“: Cooldown 30 Ticks (1 s), max. 20 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 10 s statt 20 s. | 21 |
| 320 | ObjectiveDriver.LeafFor PLAYER_LOST_OBJECT_TYPE (Destroy lose:, 6, 30) | H/zeit | Kantenblatt „ein Objekt des Typs zerstören“: Cooldown 30 Ticks (1 s), max. 6 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 3 s statt 6 s. | 21 |
| 328 | ObjectiveDriver.LeafFor PLAYER_ACQUIRED_SCIENCE (Leaf science: purchase, 100000, 300) | H/zeit | Nur-Befehle-Blatt „Wissenschaft kaufen“: Cooldown 300 Ticks (10 s), unbegrenzt | eigen | Kaufversuch alle 5 s statt 10 s; mehr (abgelehnte) Befehle. | 21 |
| 334 | ObjectiveDriver.LeafFor PLAYER_ACQUIRED_SCIENCE (Leaf science: grant, 2, 30) | H/zeit | Blatt „Wissenschaft gewähren“: Cooldown 30 Ticks (1 s), max. 2 Anwendungen | eigen | Cooldown zählt Ticks: zweite Anwendung nach 0,5 s statt 1 s. | 21 |
| 341 | ObjectiveDriver.LeafFor SKIRMISH_PLAYER_HAS_UNITS_IN_AREA (Leaf present:, 4\|100000, 90\|150) | H/zeit | Blatt „Einheit in Gebiet bringen“: Cooldown 90 Ticks (3 s), max. 4; Nur-Befehle 150 Ticks (5 s), unbegrenzt | eigen | Halbe Laufzeit je Versuch, Platzierung nach 3 s statt 6 s; Einheit steht früher im feindlichen Gebiet. | 21 |
| 352 | ObjectiveDriver.LeafFor SKIRMISH_PLAYER_IS_OUTSIDE_AREA (Destroy vacate:, 10, 60) | H/zeit | Blatt „Gebiet räumen“: Cooldown 60 Ticks (2 s), max. 10 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 10 s statt 20 s; Nachrücker nach dem Fenster bleiben im Gebiet. | 21 |
| 393 | ObjectiveDriver.LeafFor NAMED/TEAM_ATTACKED_BY_OBJECTTYPE (Leaf hurttype:, 3, 30) | H/zeit | Blatt „Schaden durch Objekttyp“: Cooldown 30 Ticks (1 s), max. 3 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 1,5 s statt 3 s. | 21 |
| 413 | ObjectiveDriver.LeafFor SKIRMISH_SPECIAL_POWER_READY Recharge (SetReadyFrame) | H/ticks | module.SetReadyFrame(_world.Tick): Bereitschafts-Tick der Spezialkraft wird auf den aktuellen absoluten Tick gesetzt („sofort bereit“) | eigen | Ratenneutral (echte Ticksemantik: jetzt). | 21 |
| 420 | ObjectiveDriver.LeafFor SKIRMISH_SPECIAL_POWER_READY (Leaf carrier: capture, 100000, 90) | H/zeit | Nur-Befehle-Blatt „Träger einnehmen“: Cooldown 90 Ticks (3 s), unbegrenzt | eigen | Einnahmebefehl alle 1,5 s statt 3 s; Befehlszahl steigt. | 21 |
| 425 | ObjectiveDriver.LeafFor SKIRMISH_SPECIAL_POWER_READY (Leaf recharge:, 2, 60) | H/zeit | Blatt „Aufladung beenden“: Cooldown 60 Ticks (2 s), max. 2 Anwendungen | eigen | Cooldown zählt Ticks: zweite Anwendung nach 1 s statt 2 s. | 21 |
| 426 | ObjectiveDriver.LeafFor SKIRMISH_SPECIAL_POWER_READY (Leaf carrier: transfer, 2, 60) | H/zeit | Blatt „Träger übertragen und aufladen“: Cooldown 60 Ticks (2 s), max. 2 Anwendungen | eigen | Cooldown zählt Ticks: zweite Anwendung nach 1 s statt 2 s. | 21 |
| 444 | ObjectiveDriver.HurtLeaf (commands-only, 100000, 90) | H/zeit | Nur-Befehle-Blatt „angreifen lassen“: Cooldown 90 Ticks (3 s), unbegrenzt | eigen | Angriffsbefehle alle 1,5 s statt 3 s. | 21 |
| 445 | ObjectiveDriver.HurtLeaf (3, 30) | H/zeit | Blatt „ein Schadenspunkt“: Cooldown 30 Ticks (1 s), max. 3 Anwendungen | eigen | Cooldown zählt Ticks: Zeitfenster 1,5 s statt 3 s. | 21 |
| 452 | ObjectiveDriver.RevealLeaf (commands-only, 100000, 150) | H/zeit | Nur-Befehle-Blatt „Späher schicken“: Cooldown 150 Ticks (5 s), unbegrenzt | eigen | Späherbefehl alle 2,5 s statt 5 s. | 21 |
| 453 | ObjectiveDriver.RevealLeaf (2, 60) | H/zeit | Blatt „Karte aufdecken“: Cooldown 60 Ticks (2 s), max. 2 Anwendungen | eigen | Cooldown zählt Ticks: zweite Anwendung nach 1 s statt 2 s. | 21 |
| 479 | ObjectiveDriver.CountLeaf (12\|100000, 30\|90) | H/zeit | Blatt „Anzahl herstellen“: Cooldown 30 Ticks (1 s), max. 12; Nur-Befehle 90 Ticks (3 s), unbegrenzt; enthält GoTo mit Versuchszähler | eigen | Zeitfenster 6 s statt 12 s; Nur-Befehle reiht alle 1,5 s Produktion ein (mehr Einheiten/Befehle). | 21 |
| 579 | ObjectiveDriver.PreventLeaf (Leaf defend:, 100000, 15) | H/zeit | Verteidigungsblatt „Seite aus Gebiet fernhalten“: Cooldown 15 Ticks (gleich spacing, also jede Runde), unbegrenzt; hängt mit GuardMargin zusammen | eigen | Muss dem Planungsrhythmus folgen: unverändert bleibt es bei jeder Runde; wird nur dieser Wert skaliert (30) und spacing nicht (15), greift die Verteidigung nur noch jede zweite Runde. | 21 |

**`Sim\Testing/ObjectiveDriver.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 20 | ObjectiveDriver.IdleRoundsBeforeHarm | H/zeit | 40 Planungsrunden ohne Fortschritt, bevor schädliche Blätter erlaubt sind; über spacing = 15 Ticks sind das 600 Ticks (20 s); 1 Verwendung (Zeile 305) | eigen | Zählt Runden, nicht Zeit: bei unverändertem spacing nur 10 s Geduld; schädliche Eingriffe kommen früher, Fahrerergebnis kann kippen. | 21 |
| 21 | ObjectiveDriver.ExhaustiveRoundsBeforeUnion | H/zeit | 20 Planungsrunden ohne Fortschritt bis zur Vereinigung aller Alternativen; über spacing = 15 Ticks 300 Ticks (10 s); 2 Verwendungen über Exhaustive (Zeilen 455, 476) | eigen | Zählt Runden: bei unverändertem spacing schon nach 5 s; Planwahl und damit Eingriffe ändern sich. | 21 |
| 30-35 | ObjectiveDriver.Leaf.Cooldown / Leaf.MaxApplications | H/zeit | Cooldown eines Blatts in Ticks; geprüft als tick - last < leaf.Cooldown (Zeilen 318, 331); MaxApplications × Cooldown ist das Zeitfenster eines Eingriffs | eigen | Alle Blatt-Cooldowns halbieren sich in Spielzeit; Blätter mit wenigen Anwendungen sind doppelt so schnell erschöpft (Sieg/Niederlage 24/24 gefährdet). | 21 |
| 324,336 | ObjectiveDriver.Round (_log) | H/ticks | Logzeilen des Fahrers mit Tickpräfix „{tick}: {label}“ (Teil von ObjectiveDriveResult.Log) | eigen | Tickzahlen im Log verdoppeln sich; Vergleiche mit alten Logs passen nicht mehr. | 21 |
| 514 | ObjectiveDriver.NeedCondition FLAG (Leaf ui:, 3, 60) | H/zeit | Blatt „UI-Schalter drücken“: Cooldown 60 Ticks (2 s), max. 3 Anwendungen | eigen | Cooldown zählt Ticks: 1 s zwischen den Anwendungen, Zeitfenster 3 s statt 6 s; Eingriffe fallen früher. | 21 |
| 535 | ObjectiveDriver.TeamEventPlan (Destroy teamdie:, 400, 24) | H/zeit | Blatt „Teammitglied zerstören“: Cooldown 24 Ticks (0,8 s), max. 400; wirkt wegen spacing 15 erst in der übernächsten Runde (30 Ticks) | eigen | Wellenmissionen (MD_CHI04) töten Teammitglieder in halber Spielzeit; Wechselwirkung mit spacing bestimmt den tatsächlichen Abstand. | 21 |
| 539 | ObjectiveDriver.TeamEventPlan (Destroy killteams:, 40, 30) | H/zeit | Blatt „Team zerstören“: Cooldown 30 Ticks (1 s), max. 40 Anwendungen | eigen | Cooldown zählt Ticks: 0,5 s Abstand, Zeitfenster 20 s statt 40 s; Eingriffe und Ausgangstick ändern sich. | 21 |

**`Sim\Testing/ReferenceMatchScenario.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 28 | ReferenceMatchOptions.Standard CheckpointInterval | C | CheckpointInterval = 1_000 Ticks, in den Replay-Kopf geschrieben (Zeile 135) | eigen | Nur Prüfdichte: doppelt so viele Checkpoints je Spielzeit; Checkpointliste/Kettenwerte liegen auf anderen Zeitpunkten, die Simulation bleibt unberührt. | 21 |
| 28 | ReferenceMatchOptions.Standard GraceTicks | H/zeit | GraceTicks = 300 Ticks (10 s) Nachlauf nach dem Sieg (Zeile 151: endTick = tick + 1 + GraceTicks) | eigen | Nur 5 s Nachlauf; EndTick und Endhash des Referenzlaufs ändern sich. | 21 |
| 28,334 | ReferenceMatchOptions.Standard IncomeInterval (mit Income) | C | Einkommen 1.500 Geld alle IncomeInterval = 300 Ticks (10 s) je lebendem Spieler; Auslöser tick % IncomeInterval == 0 (Zeile 334) | eigen | Ergebnisse: doppeltes Einkommen je Spielsekunde, Produktion/Heere wachsen schneller; Sieger, Siegtick und Endhash ändern sich. | 21 |
| 28 | ReferenceMatchOptions.Standard MaxTicks | H/budget | MaxTicks = 216_000 Ticks (2 h bei 30 Hz) als harter Stopp; geht als EndTick in den Replay-Kopf (Zeile 135) und in endTick (Zeile 139) | eigen | Nur 1 h Spielzeit; der Referenzlauf (Sieg heute bei Tick 13.500) passt weiter hinein, der EndTick im Replay-Kopf ändert sich aber nicht mit. | 21 |
| 47 | ReferenceMatchScenario.MoveCadence | C | MoveCadence = 15 Ticks: Befehlsrunde (Verfolgen und FireWeaponCommand, Zeile 340) und Siegprüfung (Zeile 147) nur bei tick % 15 == 0; 2 Verwendungen | eigen | Ergebnisse: Feuer-/Bewegungsbefehle doppelt so oft je Spielsekunde (Feuerbefehl höchstens alle 0,25 s statt 0,5 s), Siegtick auf anderes Raster; Sieger und Endhash ändern sich. | 21 |
| 48 | ReferenceMatchScenario.ProductionCadence | C | ProductionCadence = 30 Ticks: Produktionsaufträge mit Zufallswahl (SageRandom, bis 6 Ziehungen je Produzent) nur bei tick % 30 == 0 (Zeile 339); 1 Verwendung | eigen | Ergebnisse: doppelt so viele Produktionsrunden und Zufallsziehungen je Spielsekunde; Befehlsstrom, Sieger und Endhash ändern sich. | 21 |
| 49 | ReferenceMatchScenario.OrderRefreshTicks | H/zeit | OrderRefreshTicks = 150 Ticks (5 s): Verfolgungsbefehl auf dasselbe Ziel frühestens nach 150 Ticks erneuert (Zeile 420: tick - order.Tick >= 150); 1 Verwendung | eigen | Erneuerung nach 2,5 s; mehr PursueCommand je Spielzeit, Befehlsstrom und Endhash ändern sich. | 21 |
| 134 | ReferenceMatchScenario.Record (ReplayHeader, SimulationTiming.LogicFramesPerSecond) | H/rate | Kopiert die Tickrate als Konstante in den Replay-Kopf des Referenzlaufs | eigen | Kopf trägt die neue Rate, sobald die Konstante parametrisiert ist; das Szenario muss die Rate dann als Eingabe bekommen statt die Konstante zu lesen. | 20,21 |
| 333 | ReferenceMatchScenario.Generate (tick == 0) | H/ticks | Tick 0 ist den Startobjekten vorbehalten (SpawnCommand mit Tick 0, Zeilen 191-239); der Generator gibt dort nichts aus | eigen | Ratenneutral (echte Ticksemantik). | 21 |
| 338,356 | ReferenceMatchScenario.Generate / IssueDockOrders (tick == 1) | H/ticks | Dockbefehle der Versorgungsträger genau bei Tick 1 (DockCommand mit Tick 1), also einen Tick nach dem Erscheinen | eigen | Ratenneutral als Abfolge; Träger starten 1/60 s statt 1/30 s nach Beginn. | 21 |

**`Sim\Testing/SkirmishMatchScenario.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 15,19 | SkirmishMatchOptions.Standard SciencePointInterval | C | SciencePointInterval = 2_700 Ticks (90 s): je Intervall ein Wissenschaftspunkt für jeden lebenden Spieler (Ersatz für das Rangsystem; Auswertung in SkirmishMatchSession) | eigen | Ergebnisse: Punkte doppelt so schnell je Spielzeit, Generalsfähigkeiten früher; KI-Verläufe und Hashes ändern sich. | 21 |
| 18-19 | SkirmishMatchOptions.Standard MaxTicks | H/budget | MaxTicks = 108_000 Ticks; der Kommentar nennt das ausdrücklich „One hour of game time“ (30 Hz) | eigen | Nur 30 min Spielzeit; KI-Läufe, die heute spät entscheiden, enden als Patt; Kommentar wird falsch. | 21 |
| 19 | SkirmishMatchOptions.Standard CheckpointInterval | C | CheckpointInterval = 1_000 Ticks (Verwendung in SkirmishMatchSession/Replay, anderes Paket) | eigen | Nur Prüfdichte: Checkpoints doppelt so dicht in Spielzeit, Simulation unberührt. | 21 |
| 19 | SkirmishMatchOptions.Standard GraceTicks | H/zeit | GraceTicks = 300 Ticks (10 s) Nachlauf nach dem Sieg (Auswertung in SkirmishMatchSession, anderes Paket) | eigen | Nur 5 s Nachlauf; EndTick und Endhash der KI-Läufe ändern sich. | 21 |

**`Sim\Testing/StressScenario.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 16 | StressOptions.Standard CheckpointInterval | C | CheckpointInterval = 1_000 Ticks, in den Replay-Kopf geschrieben (Zeile 62) | eigen | Nur Prüfdichte: Checkpoints doppelt so dicht in Spielzeit. | 21 |
| 16,117 | StressOptions.Standard DrainTicks | H/zeit | DrainTicks = 6_000 Ticks (200 s) Abbauphase am Ende: draining = tick >= Ticks - DrainTicks | eigen | Abbauphase nur 100 s; Abbau ist tickgetaktet (Kills je Tick), funktioniert weiter, liegt aber anders zu den ms-Lebensdauern. | 21 |
| 16 | StressOptions.Standard Ticks | H/budget | Ticks = 216_000 (2 h bei 30 Hz) Lauflänge des Stresstests; Schleifengrenze Zeile 64, EndTick im Replay-Kopf (Zeile 62) | eigen | Gleiche Tickzahl, halbe Spielzeit; Last je Tick bleibt, aber Lebensdauern aus Millisekunden verdoppeln sich in Ticks (siehe SyntheticFixture), Hash ändert sich. | 21 |
| 61 | StressScenario.Record (ReplayHeader, SimulationTiming.LogicFramesPerSecond) | H/rate | Kopiert die Tickrate als Konstante in den Replay-Kopf des Stresstests | eigen | Kopf trägt die neue Rate; das Szenario muss die Rate als Eingabe bekommen statt die Konstante zu lesen. | 20,21 |
| 122-131 | StressScenario.Generate (_newlySpawned) | H/ticks | Timer werden den Entitäten gegeben, die im vorigen Tick erschienen sind (Ereignisse aus Observe): „ein Tick später“ als Abfolge | eigen | Ratenneutral als Abfolge (1/60 s statt 1/30 s Versatz). | 21 |
| 127 | StressScenario.Generate SetTimerCommand(30, 90, Notify) | H/zeit | Timer mit DelayTicks = 30 (1 s) und RepeatTicks = 90 (3 s), direkt als Ticks im Befehl | eigen | Timer feuern doppelt so oft je Spielzeit; bei gleicher Tickzahl bleibt die Zahl der Auslösungen je Tick gleich, relativ zu ms-Lebensdauern aber halb so lang. | 21 |
| 129 | StressScenario.Generate SetTimerCommand(120, 300, Damage) | H/zeit | Schadens-Timer mit DelayTicks = 120 (4 s) und RepeatTicks = 300 (10 s), 1 Schaden je Auslösung | eigen | Doppelter Schaden je Spielsekunde; Verhältnis zu den ms-Lebensdauern der Fixture verschiebt sich, Hash ändert sich. | 21 |
| 132 | StressScenario.Generate (tick % 500 == 250) | C | Alle 500 Ticks (Phase 250) ein Welt-Timer | eigen | Ergebnisse: gleiche Befehle je Tick, doppelt so viele je Spielzeit; Hash ändert sich nur über die Wechselwirkung mit ms-Daten. | 21 |
| 133 | StressScenario.Generate SetTimerCommand(EntityId.Invalid, 45, 0) | H/zeit | Einmaliger Welt-Timer mit DelayTicks = 45 (1,5 s), keine Wiederholung | eigen | Feuert nach 0,75 s Spielzeit; in Ticks unverändert. | 21 |
| 137 | StressScenario.Generate (remaining, - 60) | H/zeit | Abbau verteilt die Kills so, dass 60 Ticks (2 s) vor dem Ende alles tot ist: remaining = Ticks - tick - 60 | eigen | Reserve nur 1 s; Sterbemodule mit ms-Dauer hätten doppelt so viele Ticks nötig – in der Fixture ohne Folgen, bei anderen Daten Restbestand am Ende. | 21 |
| 148-150 | StressScenario.Generate (tick < 100, spawnLongLived) | C | Rampe: in den ersten 100 Ticks TargetPopulation/100 langlebige Spawns je Tick, danach 1–2 je Tick | eigen | Ergebnisse: Spawns je Tick fest; je Spielsekunde doppelt so viele, Rampe dauert 1,7 s statt 3,3 s. | 21 |
| 151 | StressScenario.Generate (tick % 2 == 0, Spark) | C | Jeden 2. Tick ein „Spark“ (LifetimeUpdate 500–1500 ms) | eigen | Ergebnisse: Lebensdauer verdoppelt sich in Ticks bei gleichem Spawnrhythmus – etwa doppelt so viele Sparks gleichzeitig, PeakAlive und Hash ändern sich. | 21 |
| 152 | StressScenario.Generate (tick % 3 == 0, Puff) | C | Jeden 3. Tick ein „Puff“ (DeletionUpdate 300–900 ms) | eigen | Ergebnisse: etwa doppelt so viele Puffs gleichzeitig; PeakAlive und Hash ändern sich. | 21 |
| 153 | StressScenario.Generate (tick % 40 == 0, Ghost) | C | Alle 40 Ticks ein „Ghost“ (ImmortalBody, keine Lebensdauer) | eigen | Ergebnisse: je Tick unverändert, je Spielzeit doppelt so viele. | 21 |
| 155-169 | StressScenario.Generate (tick >= 100, ein Kill je Tick) | C | Ab Tick 100 und voller Population je Tick ein Kill/Destroy eines langlebigen Objekts (mit Zufallsziehung) | eigen | Ergebnisse: Umsatz je Tick fest, je Spielzeit doppelt; Zufallsfolge je Tick unverändert. | 21 |
| 170-174 | StressScenario.Generate (tick % 2 == 1, DamageCommand) | C | Jeden 2. Tick (ungerade) zufälliger Schaden 1–20 an einem lebenden Objekt | eigen | Ergebnisse: Schaden je Spielsekunde doppelt; mehr Objekte sterben vor Ablauf ihrer ms-Lebensdauer. | 21 |
| 175-179 | StressScenario.Generate (tick % 97 == 0) | C | Alle 97 Ticks ein absichtlich ungültiger DestroyCommand (Ablehnungstest) | eigen | Nur Zahl der Ablehnungen je Spielzeit; kein Zustandseffekt. | 21 |

**`Sim\Testing/SyntheticFixture.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 35 | SyntheticFixture.Files DefaultThingTemplate BuildTime | B/daten | BuildTime = 1 (Sekunden); ObjectTemplate rechnet beim Katalogaufbau mit SecondsToTicks (aufrunden) in BuildTicks = 30 um | eigen | 60 Ticks statt 30, ohne Rundungsfehler; der Fingerabdruck der Fixture bleibt, abgeleitete Tickwerte ändern sich. | 21 |
| 103-104 | SyntheticFixture.Files Spark LifetimeUpdate MinLifetime/MaxLifetime | B/daten | 500 / 1500 ms; LifetimeData.Parse über ModuleConfigReader.Ticks (aufrunden, Modul-Parsezeit) = 15 / 45 Ticks | eigen | 30 / 90 Ticks, glatt ohne Rundung (auch 120 Hz); Zufallsbereich der Lebensdauer doppelt so breit, andere Ziehungsergebnisse – Stresshash 56882F763EABEF67 ändert sich. | 21 |
| 115-116 | SyntheticFixture.Files Puff DeletionUpdate MinLifetime/MaxLifetime | B/daten | 300 / 900 ms; LifetimeData.Parse über ModuleConfigReader.Ticks (aufrunden, Modul-Parsezeit) = 9 / 27 Ticks | eigen | 18 / 54 Ticks, glatt ohne Rundung; Zufallsbereich doppelt so breit, Stresshash ändert sich. | 21 |

## 6. Inventar Spielschicht, Präsentation, UI, Client, Daten

Hier entscheidet die Unterart der Klasse F: `tick` muss Phase 21 auf Spielzeit umstellen; `originalbild` ist bereits richtig (feste 1/30-s-Einheit auf Wandzeit) und darf nicht an die Tickrate gehängt werden; `wand` läuft an Wandzeit, enthält aber eine 30er-Konstante.

### 6.1 `CnCNew.Game`

**`Game\Audio/AudioDirector.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 31 | AudioDirectorOptions.CueLimits (SoundCueLimits MaxPerEventName/WindowTicks/MaxPerTick) | F/tick | Flutschutz der Soundcues als Literal new(3, 10, 0f, 48): höchstens 3 je Ereignisname in 10 Ticks, höchstens 48 je Tick; SoundCueRateLimiter zählt cue.Tick (Simulationstick) | eigen | Tick-getrieben: Fenster 1/6 s statt 1/3 s und Tick-Obergrenze doppelt so oft, also etwa doppelt so viele Töne je Sekunde zugelassen | 21 |
| 648-654 | AudioDirector.UpdateEva (_lastEvaTick, IAudioGameView.Tick, LiveAudioView.Tick) | F/tick | Eva-Ansager läuft höchstens einmal je gerendertem Frame und nur wenn LiveGame.Tick (= World.Tick) sich geändert hat; reicht den absoluten Simulationstick als Logikframe an EvaAnnouncer.Update; nicht bei Pause; Aufrufer GameAudio.Update -> AudioDirector.Update -> Tick | Eva.cpp Eva::update (frame = TheGameLogic->getFrame()) | Tick-Uhr: alle Eva-Fristen in Frames (Sperrzeit, Verfall) laufen doppelt so schnell, solange die Frame-Werte nicht skaliert werden; bei 60 Hz und 30 fps sieht der Ansager nur jeden zweiten Tick | 21 |

**`Game\Audio/EvaCatalog.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 126 | EvaCatalog.Frames | B/daten | eigene Kopie der Umrechnung ms -> Frames: ceil(ms × SimulationTiming.LogicFramesPerSecond / 1000) beim Parsen von Eva.ini (TimeBetweenChecksMS Z.108, ExpirationTimeMS Z.113; -1 -> int.MaxValue); nicht über SimulationTiming.DurationMillisecondsToTicks | Eva.cpp Feldtabelle: INI::parseDurationUnsignedInt für TimeBetweenChecksMS/ExpirationTimeMS | folgt der Konstante nur zur Übersetzungszeit (const); wird die Rate Laufzeitparameter, muss sie hier hineingereicht werden; Rundung ceil zur Parse-Zeit, bei 60 Hz feiner | 21 |
| 140 | EvaCatalog.EvaBuilder.FramesBetweenChecks | A | Standardwert 900 Frames (30 s Sperrzeit) für EvaEvent-Blöcke ohne TimeBetweenChecksMS; in Frames geschrieben, nicht umgerechnet | Eva.cpp EvaCheckInfo::EvaCheckInfo m_framesBetweenChecks(900) "30 seconds at 30 fps" | 15 s statt 30 s Sperrzeit für Ereignisse ohne eigenen Wert | 21 |
| 141 | EvaCatalog.EvaBuilder.FramesToExpire | A | Standardwert 150 Frames (5 s Verfall einer wartenden Ansage) für EvaEvent-Blöcke ohne ExpirationTimeMS; in Frames geschrieben | Eva.cpp EvaCheckInfo::EvaCheckInfo m_framesToExpire(150) "5 seconds at 30 fps" | 2,5 s statt 5 s: wartende Ansagen verfallen doppelt so früh | 21 |
| 219 | EvaAnnouncer.Update (frame < 2) | A | vor Logikframe 2 tut der Ansager nichts und verwirft die Flags (Startenergie 0 ignorieren); Literal 2 in Frames | Eva.cpp Eva::update (if (frame < 2) return) | Schonfrist 1/30 s statt 1/15 s; echte Frame-Semantik, praktisch folgenlos | 21 |
| 234 | EvaAnnouncer.Update (check.NextCheck <= frame + 1) | A | gespielte Prüfung wird einen Frame vor Ablauf der Sperrzeit entfernt ("next frame"); Literal +1 Frame; daneben Z.163/252 NextCheck = frame + FramesBetweenChecks und Z.235 TriggeredOn + FramesToExpire (Verwendungen) | Eva.cpp Eva::processPlayingMessages (m_timeForNextCheck <= currentFrame + 1) | +1 Frame ist 1/60 s; da Update nicht für jeden Tick läuft (einmal je Renderframe), kann die Entfernung um einen Tick später fallen | 21 |

**`Game\Campaign/CampaignController.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 31,39 | CampaignController.StartCurrentMission (maxTicks, presentationLatencyTicks) | B/literal | Schnittstellenparameter in Ticks, unverändert an MissionOptions gereicht: maxTicks (Laufgrenze, 0 = keine) und presentationLatencyTicks (so viele Ticks gelten Sprach-/Film-/Kameraausgaben als laufend, Standard 0) | eigen | Aufrufer, die Tickzahlen übergeben, bekommen halbe Dauer; Standard 0 bleibt gleich | 20,21 |

**`Game\Hud/HudMessageFeed.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 35 | HudMessageFeed.UnderAttackFrames | A | "30 * 10" = 300 Ticks: höchstens eine Unter-Beschuss-Zeile je 250 Welteinheiten und 10 s; die Rate steht als Literal 30; verglichen mit SimEvent.Tick (1 Verwendung, Z.186) | Radar.cpp Radar::tryEvent (framesBetweenEvents = LOGICFRAMES_PER_SECOND * 10) | Sperre 5 s statt 10 s: doppelt so viele Unter-Beschuss-Meldungen (Tick-Uhr) | 21 |

**`Game\Hud/MatchController.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 79 | MatchController..ctor (Messages.Clock = () => Match.Tick) | F/tick | Meldungszeilen des HUD werden mit dem Simulationstick gestempelt (HudMessageFeed.Clock, Standard () => 0); Alter = Tick-Differenz in UiMessageLog | eigen | Tick-Uhr: Zeilenalter wächst doppelt so schnell, siehe Z.449 | 21 |
| 114 | MatchController..ctor (match.BeforeTick = Input.Tick) | C | je Simulationstick vor dem Schritt: GameInputController.Tick -> Selection.Prune und CommandModeController.Update -> StandingOrders.Update (kann Befehle absetzen, world.Tick > IssuedTick + 1) | eigen | Rhythmus folgt dem Tick; Ergebnisse möglich, weil Daueraufträge je Tick geprüft und Befehle abgesetzt werden (Details in CnCNew.Presentation, nicht in diesem Paket) | 21 |
| 429 | MatchController.MapCameraSettings (KeyboardRotateRadiansPerFrame) | F/originalbild | Drehgeschwindigkeit je "Frame" (Standard 0,1 rad) mit dem Optionsregler skaliert; CameraInput rechnet frames = deltaSeconds × CameraSettings.FramesPerSecond (Standard 30f, CameraInput.cs Z.23/99) | Herkunft unbelegt | unverändert, solange CameraSettings.FramesPerSecond eine feste 30 bleibt und nicht an die Tickrate gehängt wird (Wandzeit) | 21 |
| 449 | MatchController.OnTickStepped (Messages.Log.Update(Match.Tick)) | F/tick | Ausblenden der Meldungszeilen einmal je Logikschritt (LiveGame.TickStepped): UiMessageLog.Update zieht je Aufruf floor(Alter × 0,01) Alpha ab, Schwelle MessageDelayMS / 30 / 1000 (Ganzzahl, = 0) | eigen (Original aktualisiert je Renderframe, siehe Kommentar UiMessageLog) | Zeile verschwindet nach etwa 277 Ticks: rund 4,6 s statt 9,2 s; Inhalt der Zeichenliste (Alpha, Zeilenzahl) hängt an der Tickrate | 21 |
| 719 | MatchController.BuildSuperweaponCommands (_superweaponTick != Match.Tick) | C | Superwaffenzeilen werden nur neu gesammelt, wenn sich der Simulationstick (oder der Skriptschalter) geändert hat; Zwischenframes nutzen die alte Liste | eigen | nur Last (doppelt so viele Sammelläufe je Sekunde); keine Ergebnisänderung | 21 |
| 726 | MatchController.BuildSuperweaponCommands (SuperweaponTimers.Draw(..., Match.Tick, ...)) | F/tick | Simulationstick als Blinkuhr der bereiten Superwaffe (tick / SuperweaponFlashFrames % 2); der Text m:ss stammt aus (readyFrame − Tick) / SimulationTiming.LogicFramesPerSecond in SuperweaponTimers.Collect Z.77 (anderes Paket) | eigen | Zeichenliste hängt an der Tickrate: Blinken doppelt so schnell, wenn SuperweaponFlashFrames (ms × 30 / 1000) nicht mitskaliert; Restzeit nur richtig, wenn die Konstante der Rate folgt | 21 |

**`Game\Hud/MatchResult.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 16 | MatchResult.EndTick | F/tick | Endzeitpunkt des Spiels als roher Simulationstick (gesetzt in MatchController Z.453 und MatchStatsTracker.Snapshot Z.84/97 aus World.Tick); 2 Erzeugungsstellen | eigen | Zahl verdoppelt sich für dieselbe Spielzeit; Verbraucher (Abschlussbild der Shell) nicht in diesem Paket geprüft | 21 |

**`Game\Hud/MissionController.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 38 | MissionController..ctor (Hud.Messages.Clock = () => mission.Director.Clock) | F/tick | Meldungszeilen einer Mission werden mit der Uhr des Präsentationsdirectors gestempelt: MissionPresentationDirector.Clock = World.Tick + _frozenSteps (MissionPresentation.cs Z.131), also Logikschritte | eigen | Tick-Uhr: Zeilen altern und verblassen doppelt so schnell (Ausblenden macht der Director, anderes Paket) | 21 |

**`Game\Hud/MissionPauseMenu.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 147 | MissionPauseMenu.RefreshSlots ("(tick {header.Tick})") | F/tick | Spielstandsliste zeigt den rohen Simulationstick aus dem Kopf des Spielstands als Text, keine Umrechnung in Zeit | eigen | angezeigte Zahl doppelt so groß für dieselbe Spielzeit; alte Spielstände (30 Hz) und neue sind nicht vergleichbar | 21 |

**`Game\Hud/SuperweaponTimers.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 77 | SuperweaponTimers.Collect seconds | F/tick | Restzeit der Superwaffe: (readyFrame - world.Tick) / SimulationTiming.LogicFramesPerSecond, ganzzahlig, für die Anzeige m:ss | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (Superwaffen-Timer: readySecs = (getReadyFrame - getFrame) / LOGICFRAMES_PER_SECOND) | Folgt der Konstante; bleibt sie eine Compile-Zeit-30, läuft der Countdown bei 60 Hz doppelt so schnell herunter (Anzeige doppelte Sekunden). | 21 |
| 111-112 | SuperweaponTimers.Draw flashFrames/flash | F/tick | Blinken bereiter Superwaffen: tick / SuperweaponFlashFrames % 2; tick ist Match.Tick (MatchController.cs:726), FlashFrames kommt aus InGameUiSettings (ms * 30 / 1000 in CnCNew.Ui) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (m_superweaponFlashDuration, Vergleich mit TheGameLogic->getFrame()) | Simulationstick als Uhr: blinkt bei 60 Hz doppelt so schnell, solange InGameUiSettings mit eigener 30 umrechnet. | 21 |

**`Game\Live/DemoScenarios.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 19 | DemoScenarios.CaptureBuilding Ticks | B/literal | Laufzeit der Demo 'capture-building': 1500 Ticks (= 50 s) | eigen | Demo endet nach 25 s; Anmarsch, Auspacken und Flaggehissen passen dann evtl. nicht mehr hinein (LiveScenario.IsOver). | 21 |
| 24 | DemoScenarios.CaptureBuilding Orders[0].Tick | B/literal | Befehlszeitpunkt Capture ab Tick 10 (= 0,33 s) | eigen | Befehl kommt nach 0,17 s statt 0,33 s; praktisch folgenlos, verschiebt aber den Ablauf der Auditdemo. | 21 |
| 39 | DemoScenarios.TankDuel Ticks | B/literal | Laufzeit der Demo 'tank-duel': 900 Ticks (= 30 s) | eigen | Demo endet nach 15 s; Rückstellzeit der Türme nach dem Abschuss liegt dann evtl. außerhalb des Laufs. | 21 |
| 46-47 | DemoScenarios.TankDuel Orders[0..1].Tick | B/literal | Angriffsbefehle beider Panzer ab Tick 5 (zwei Einträge) | eigen | Befehle nach 0,08 s statt 0,17 s; folgenlos für das Ergebnis, verschiebt Audit-Zeitachsen. | 21 |
| 55 | DemoScenarios.JetStrafe Ticks | B/literal | Laufzeit der Demo 'jet-strafe': 3600 Ticks (= 120 s) | eigen | Demo endet nach 60 s; Bau, Start, Anflug, Rückflug und Landung passen dann nicht mehr vollständig hinein. | 21 |
| 62-67 | DemoScenarios.JetStrafe Orders[0..5].Tick | B/literal | Produktions- und Angriffsbefehle ab Tick 1 (sechs Einträge) | eigen | Kein Unterschied (frühestmöglicher Tick); nur als Tick-Literal geführt. | 21 |

**`Game\Live/LiveGame.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 28 | LiveGame.TickSeconds | F/wand | Kopie der Rate: const double TickSeconds = 1.0 / SimulationTiming.LogicFramesPerSecond; fester Schritt des Akkumulators in Advance (4 Verwendungen: Schleife, Abzug, Deckel, Interpolationsanteil) | eigen | Folgt der Konstante nur zur Compile-Zeit; bei laufzeitparametrisierter Rate muss der Schritt aus der Rate der Welt kommen, sonst läuft die Simulation mit halber/doppelter Geschwindigkeit. | 21 |
| 29 | LiveGame.MaxTicksPerFrame | C | Höchstens 8 Simulationsticks je gerendertem Frame (Aufholgrenze = 267 ms Spielzeit bei 30 Hz); danach wird der Akkumulator auf einen Tick gekappt | eigen | Nur Last/Tempo: Grenze entspricht bei 60 Hz 133 ms; unter 7,5 fps (bzw. bei Speed > 1 früher) fällt das Spiel hinter die Wandzeit zurück. Simulationsergebnisse unberührt. | 21 |
| 178-198 | LiveGame.Advance | F/wand | Treiber aller Uhren: Wandzeit * Speed -> Akkumulator -> je TickSeconds ein BeforeTick/StepSimulation/SyncAfterTick/TickStepped; Fog.Advance und OnAdvanced laufen auf Wandzeit, Projektion mit Anteil accumulator/TickSeconds | eigen | Korrekt auf Wandzeit, sofern TickSeconds der echten Rate folgt; alle 'je Tick'-Abnehmer (BeforeTick, TickStepped: HUD, Statistik, Ton) werden doppelt so oft aufgerufen. | 21 |
| 203-212 | LiveGame.SyncAfterTick | F/tick | Je Simulationstick genau einmal: FrameProjector.Observe/Advance, FxExecutor.ExecuteEvents/Update, Sound-Cues abholen - die Präsentationsschichten werden vom Tick getaktet, nicht vom Renderframe | eigen | FrameProjector und FxExecutor (andere Pakete) bekommen 60 Schritte je Sekunde; alles, was dort '1 Schritt = 1/30 s' annimmt (Partikel, Animation, Blitzen), läuft doppelt so schnell. | 21 |
| 211 | LiveGame.SyncAfterTick fog refresh | C | Nebel-Zielwerte nur aktualisieren, wenn World.Tick % SimulationWorld.VisionIntervalTicks == 0 (Konstante der Simulation) | eigen | Nur Darstellung/Last: folgt VisionIntervalTicks; bleibt die Zahl gleich, wird der Nebel doppelt so oft aktualisiert. | 20,21 |

**`Game\Live/LiveMission.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 51 | LiveMission.ClientPresentationLatencyTicks | B/literal | 60 Sekunden als Ticks: const int = 60 * SimulationTiming.LogicFramesPerSecond (1800); geht als MissionOptions.PresentationLatencyTicks in die Skript-VM und wird im MissionSnapshot gespeichert (presentationLatencyTicks) | eigen | Folgt der Konstante zur Compile-Zeit; mit Laufzeitrate muss es aus der Rate berechnet werden, sonst warten Skripte nur 30 s auf nie beendete Präsentationen. Ganzzahlig, keine Rundung. | 20,21 |
| 75-81 | LiveMission.StepSimulation | F/tick | Präsentationsuhr der Mission: Director.BeforeStep / Session.Step / Director.AfterStep genau einmal je Simulationstick (aus LiveGame.Advance) | eigen | Der Director (Texte, Blenden, Filter, Radar, Abschlüsse) wird 60-mal je Sekunde geschaltet; alle seine Frame-Zählungen laufen doppelt so schnell, soweit sie nicht über die Rate umgerechnet werden. | 21 |

**`Game\Live/LiveScenario.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 34 | ScenarioOrder.Tick | B/literal | Feld: Befehl gilt ab diesem absoluten Simulationstick (Vergleich order.Tick > World.Tick in Zeile 108); Werte stehen in DemoScenarios | eigen | Feld in Ticks statt Sekunden: gleiche Zahl bedeutet bei 60 Hz die halbe Zeit. | 21 |
| 41 | DemoScenario.Ticks | B/literal | Feld: Laufzeit der Demo in Ticks; IsOver => World.Tick >= Scenario.Ticks (Zeile 90) | eigen | Feld in Ticks statt Sekunden: jede Demo ist bei 60 Hz halb so lang. | 21 |
| 103-117 | LiveScenario.StepSimulation | C | Je Tick: fällige Befehle prüfen, StandingOrders.Update je Spieler aufrufen (Paket Presentation), dann World.Step; unerledigte Befehle (Capture, Attack auf noch nicht Gebautes) werden jeden Tick erneut versucht | eigen | Ergebniswirksam gering: doppelt so viele Wiederholversuche/abgelehnte Befehle je Sekunde (Sequenznummern laufen schneller), Annahme-Zeitpunkt feiner; Rhythmus von StandingOrders.Update verdoppelt sich. | 21 |
| 134-139 | LiveScenario.Apply Capture retry | C | CaptureCommand wird jeden Tick neu eingereicht, bis die Simulation ihn annimmt; solange nicht in Reichweite, Bewegungsbefehl wenn kein Ziel | eigen | Nur Last und Befehlszähler (doppelt so viele abgelehnte Befehle je Sekunde); Annahme höchstens einen halben alten Tick früher. | 21 |

**`Game\Save/SaveGame.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 32 | SaveGame.Version | G | Formatversion 1 des Spielstands; der Kopf speichert 'tick' und bettet den MissionSnapshot ein, die Tickrate selbst wird weder geschrieben noch beim Laden geprüft (nur Version, Fingerprint, Prüfsummen) | eigen | Ein 30-Hz-Spielstand würde in einem 60-Hz-Build ohne Fehlermeldung geladen und alle Tick-Werte falsch gedeutet, sofern nicht Version oder Fingerprint die Rate tragen. | 20,21 |

**`Game\Shell/MissionPresentation.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 46-47 | MissionPostEffects.MotionBlurMaxCount / MotionBlurStep | A | Zoom-Unschärfe: Zähler +5 je Logikschritt bis 60, dann -5 je Schritt (12 + 11 Schritte, 'CAMERA_MOTION_BLUR_JUMP'); verwendet in AdvanceFilters 673-688 nur in Schritten mit Welt-Tick | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngineDevice/Include/W3DDevice/GameClient/W3DShaderManager.h (ScreenMotionBlurFilter enum MAX_COUNT = 60, COUNT_STEP = 5; postRender in W3DShaderManager.cpp) | Der Blur-Sprung dauert 0,4 s statt 0,8 s (Schrittweite je Tick statt je Zeit); der Kameraschnitt am Scheitel kommt entsprechend früher. | 21 |
| 74 | MissionPresentationDirector.DefaultPlaceholderTicks | B/literal | Platzhalterdauer für Sprache/Ton/Film: const long = 3 * SimulationTiming.LogicFramesPerSecond (90); 4 Verwendungen (Untertitel-Ablauf, Film-Ablauf, zwei CompletePresentation-Termine) | eigen | Folgt der Konstante zur Compile-Zeit. Ergebniswirksam: der Termin geht über CompletePresentation in HAS_FINISHED_*-Bedingungen der Skripte; bei fester 90 enden Sprache/Film nach 1,5 s. | 20,21 |
| 75 | MissionPresentationDirector.RadarBlipLifetimeTicks | B/literal | Lebensdauer eines Radar-Blips: const long = 8 * SimulationTiming.LogicFramesPerSecond (240), verglichen mit Clock - CreatedAtTick (Zeile 630) | eigen | Folgt der Konstante zur Compile-Zeit; nur Darstellung (Blip 4 s statt 8 s, falls die Zahl 240 bleibt). | 21 |
| 131 | MissionPresentationDirector.Clock | F/tick | Präsentationsuhr: World.Tick + _frozenSteps (Schritte mit FREEZE_TIME); Basis für Kameratermine, Blenden, Letterbox, Texte, Filter, Radar. Getrieben von BeforeStep/AfterStep = ein Schritt je Simulationstick | eigen | Uhr zählt 60 Einheiten je Sekunde; jede Dauer, die in dieser Einheit als Zahl steht (Skript-Frames, Literale), halbiert sich. | 21 |
| 216 | MissionPresentationDirector.LetterboxFadeFrames | B/literal | Kopie der Rate als nackte 30: Letterbox-Blende '1000 ms in Präsentationsschritten' = 30 Frames, nicht aus der Konstante abgeleitet; LetterboxFadeLevel = (Clock - since) / 30 (Zeile 226) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngineDevice/Source/W3DDevice/GameClient/W3DDisplay.cpp (#define LETTER_BOX_FADE_TIME 1000.0f, W3DDisplay::renderLetterBox; dort Wanduhr-Millisekunden) | Balken blenden in 0,5 s statt 1 s ein/aus (Tick-Uhr mit fester 30); das Original misst Wandzeit. | 21 |
| 270 | MissionPresentationDirector.InputLockedTicks | F/tick | Zähler: Präsentationsschritte mit gesperrter Eingabe (BeforeStep Zeile 609, ++ je Schritt); nur Bericht ('input locked for N ticks', Zeile 1300) | eigen | Berichtszahl verdoppelt sich für dieselbe Sperrzeit; kein Einfluss auf Spiel oder Bild. | 21 |
| 590-610 | MissionPresentationDirector.BeforeStep | F/tick | Je Schritt: Ablauf von Film-/Untertitel-Platzhalter, fällige CompletePresentation-Termine und Kameraabschluss gegen die Tick-Uhr; Abschluss wirkt für die Skripte ab dem Update desselben Schritts | eigen | Granularität der Abschlüsse 1/60 s statt 1/30 s; Termine stimmen nur, wenn die Tick-Dauern (DurationTicks, DefaultPlaceholderTicks) mit der echten Rate gebildet sind. Ergebniswirksam für Skriptbedingungen. | 21 |
| 637-653 | MissionPresentationDirector.AdvanceText | F/tick | Ein Text-Update je Präsentationsschritt: UiMessageLog.Update, MilitaryCaptionState.Update (je neuem Zeichen ein Tipp-Klick an den Ton), Ablauf des Cinematic-Texts gegen Clock | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update; dort einmal je gerendertem Frame mit currLogicFrame) | Doppelt so viele Updates je Sekunde: Schreibmaschine, Klickfolge und Ausblenden laufen doppelt so schnell (siehe MissionText-Zeilen). | 21 |
| 661-672 | MissionPresentationDirector.AdvanceFilters grey-scale ramp | F/tick | Graustufen-Rampe: _bwFrame++ je Präsentationsschritt, Pegel = _bwFrame / _bwFrames; ein Logikschritt steht für einen gezeichneten Frame des Originals | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngineDevice/Source/W3DDevice/GameClient/W3DShaderManager.cpp (ScreenBWFilter, m_curFadeFrame++ je Renderdurchlauf) | Rampe ist bei 60 Hz in der halben Zeit durch (Zählung je Tick mit rohen Skript-Frames). | 21 |
| 673-688 | MissionPresentationDirector.AdvanceFilters motion blur | F/tick | Zoom-Unschärfe: +/- MotionBlurStep je Schritt, nur wenn die Welt in diesem Schritt getickt hat; am Scheitel _blurJumpDue (Kameraschnitt im nächsten ApplyCameraTo) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngineDevice/Source/W3DDevice/GameClient/W3DShaderManager.cpp (ScreenMotionBlurFilter::postRender, m_maxCount +/- COUNT_STEP) | 23 Schritte dauern 0,38 s statt 0,77 s. | 21 |
| 773 | MissionPresentationDirector.Apply CAMERA_MOD_SET_FINAL_ZOOM/PITCH seconds | F/tick | Kopie der Rate: Restzeit der laufenden Kamerabewegung = (_cameraCompletesAtTick - tick) / (double)SimulationTiming.LogicFramesPerSecond, als Sekunden an den Wandzeit-Tween | eigen | Folgt der Konstante; bleibt sie 30, bekommt der Tween die doppelte Restzeit und Zoom/Neigung enden erst nach der Bewegung. | 21 |
| 791 | MissionPresentationDirector.Apply CAMERA_MOD_LOOK_TOWARD seconds | F/tick | Kopie der Rate: Restzeit der laufenden Kamerabewegung = (_cameraCompletesAtTick - tick) / (double)SimulationTiming.LogicFramesPerSecond für die Blickdrehung | eigen | Folgt der Konstante; bleibt sie 30, dreht die Kamera doppelt so lange wie die Bewegung dauert. | 21 |
| 812 | MissionPresentationDirector.Apply CAMERA_MOVE_HOME/MOVE_CAMERA_TO_SELECTION | E | Kamerabewegung ohne Dauer: _cameraCompletesAtTick = tick, Abschluss im BeforeStep des nächsten Ticks ('ein Tick später') | eigen | CAMERA_MOVEMENT_FINISHED wird 1/60 s statt 1/30 s später wahr; Reihenfolge gleich, Abstand zu anderen Skriptereignissen halbiert. | 21,23 |
| 832 | MissionPresentationDirector.Apply CAMERA_FADE_* frames | D | Skriptargumente framesIncrease/framesHold/framesDecrease werden roh als Schritte der Präsentationsuhr übernommen ((long)increase ...) und in FadeAlpha (567-577) gegen Clock - Start ausgewertet | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/ScriptEngine.cpp (ScriptEngine::updateFades, m_curFadeFrame++ gegen m_fadeFramesIncrease) | Jede Skript-Blende (auf/halten/ab) dauert halb so lange; Missionsintros sind auf 1/30-s-Frames geschrieben. | 20,21 |
| 910 | MissionPresentationDirector.Apply DISPLAY_CINEMATIC_TEXT expiry | F/tick | Kopie der Rate: Ablauf = tick + (long)seconds * SimulationTiming.LogicFramesPerSecond (Sekunden werden zuerst abgeschnitten, dann mal Rate); Laufzeitumrechnung je Ereignis | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/ScriptActions.cpp (ScriptActions::doDisplayCinematicText: frames = LOGICFRAMES_PER_SECOND * timeInSeconds; W3DDisplay.cpp zählt m_cinematicTextFrames-- je Zeichnen) | Folgt der Konstante; bleibt sie 30, steht der Text halb so lange. Abschneiden auf ganze Sekunden unabhängig von der Rate. | 21 |
| 1127 | MissionPresentationDirector.ApplyScene CAMERA_BW_MODE_BEGIN frames | D | Skriptargument 'frames' roh als Schrittzahl der Graustufen-Rampe (_bwFrames = (int)beginFrames), je Präsentationsschritt ein Frame | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/ScriptActions.cpp (ScriptActions::doBlackWhiteMode(Bool, Int frames)) | Einblenden der Graustufen dauert halb so lange. | 20,21 |
| 1137 | MissionPresentationDirector.ApplyScene CAMERA_BW_MODE_END frames | D | Skriptargument 'frames' roh als Schrittzahl der Rück-Rampe (_bwFrames = (int)endFrames) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/ScriptActions.cpp (ScriptActions::doBlackWhiteMode(Bool, Int frames)) | Ausblenden der Graustufen dauert halb so lange. | 20,21 |
| 1305 | MissionPresentationDirector.DurationTicks | B/daten | Kopie der Rate: Skript-Sekunden -> Ticks zur Laufzeit je Ereignis mit Math.Round (nicht ceil) * SimulationTiming.LogicFramesPerSecond; 5 Aufrufer (Kamerabewegung, Rotation, Blick auf Wegpunkt/Objekt) setzen _cameraCompletesAtTick | eigen | Folgt der Konstante; Rundung auf nächsten Tick (feiner bei 60 Hz). Ergebniswirksam: der Tick-Termin meldet CAMERA_MOVEMENT_FINISHED an die Skripte, der sichtbare Tween läuft daneben auf Wandzeit. | 20,21 |

**`Game\Shell/MissionText.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 56 | UiMessageLog.Update timeout | F/tick | Kopie der Rate (InGameUiSettings.LogicFramesPerSecond = 30, eigene Konstante in CnCNew.Ui): timeout = MessageDelayMilliseconds / 30 / 1000 in Ganzzahlarithmetik wie im Original, Vergleich mit Alter in Frames | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update: messageTimeout = m_messageDelayMS / LOGICFRAMES_PER_SECOND / 1000) | Schwelle in Frames der Tick-Uhr: gleiche Zahl = halbe Zeit; wird die Ui-Konstante auf 60 gesetzt, ändert die Ganzzahldivision den Wert zusätzlich. | 21 |
| 60-66 | UiMessageLog.Update fade | E | Ausblenden je Update: alpha -= floor(age * 0.01), age in Frames der Präsentationsuhr, ein Update je Präsentationsschritt (akkumulierend, nicht zeitinvariant) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update, Nachrichtenzeilen; dort ein Update je gerendertem Frame) | Alter wächst doppelt so schnell und es wird doppelt so oft abgezogen: Zeilen verschwinden nach rund 4,5 s statt 9 s. | 21,23 |
| 102 | MilitaryCaptionState.BlinkFrames | A | Blockcursor wechselt alle 10 Frames (_blockBegin + BlinkFrames - 1 < frame, Zeile 176) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update: blockBeginFrame + 9 < currLogicFrame) | Cursor blinkt doppelt so schnell (alle 0,17 s statt 0,33 s). | 21 |
| 136 | MilitaryCaptionState.Begin lifetime | F/tick | Kopie der Rate: lifetime = frame + (long)(InGameUiSettings.LogicFramesPerSecond * durationMs / 1000f), abgeschnitten, Laufzeitumrechnung je Caption (auch für 8000-ms-Sprachuntertitel) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::militarySubtitle: currLogicFrame + (Int)((LOGICFRAMES_PER_SECOND * duration) / 1000.0f)) | Mit fester Ui-30 steht die Caption halb so lange; folgt nicht SimulationTiming, sondern der zweiten Konstante. | 21 |
| 137 | MilitaryCaptionState.Begin delay | F/tick | Kopie der Rate: Startverzögerung = (int)(InGameUiSettings.LogicFramesPerSecond * MilitaryCaptionDelayMS / 1000f) (750 ms = 22 Frames), abgeschnitten; gilt auch nach jedem Zeilenumbruch (Zeile 189) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::militarySubtitle / update: incrementOnFrame = currLogicFrame + (Int)((LOGICFRAMES_PER_SECOND * m_militaryCaptionDelayMS) / 1000.0f)) | Mit fester Ui-30 beginnt das Tippen nach 0,37 s statt 0,73 s. | 21 |
| 169-174 | MilitaryCaptionState.Update fade | E | Ausblenden je Update: alpha -= floor((frame - lifetime) * 0.1), akkumulierend je Präsentationsschritt | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update, m_militarySubtitle) | Caption blendet in etwa der halben Zeit aus (doppelt so viele Updates, Alter in Ticks doppelt so schnell). | 21,23 |
| 181-197 | MilitaryCaptionState.Update typing (_speed) | D | MilitaryCaptionSpeed (Language.ini, 1) wird roh als Frames zwischen zwei Zeichen benutzt: _increment = frame + _speed, Test _increment >= frame -> ein Zeichen alle speed + 1 Frames | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::update: incrementOnFrame = currLogicFrame + TheGlobalLanguageData->m_militaryCaptionSpeed) | Text wird doppelt so schnell getippt (30 statt 15 Zeichen/s) und der Tipp-Klick doppelt so oft ausgelöst. | 21 |
| 322-332 | MissionTextOverlay.NamedTimers flashFrames | F/tick | Blinken eines abgelaufenen Timers: frame / NamedTimerFlashFrames % 2, frame = Präsentationsuhr; FlashFrames aus InGameUiSettings (ms * 30 / 1000) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (m_namedTimerFlashDuration, Vergleich mit TheGameLogic->getFrame()) | Blinkt doppelt so schnell, solange InGameUiSettings mit eigener 30 umrechnet. | 21 |
| 326 | MissionTextOverlay.NamedTimers seconds | F/tick | Kopie der Rate: Skript-Timerwert (ScriptEngine.CounterValue, in Frames) / InGameUiSettings.LogicFramesPerSecond -> Sekunden für die Anzeige m:ss und für 'bereit' (seconds == 0) | references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameClient/InGameUI.cpp (InGameUI::postDraw, benannte Timer) | Mit fester Ui-30 zeigt der Countdown die doppelte Zeit bzw. läuft doppelt so schnell; hängt zusätzlich davon ab, ob die Skript-VM Timer in Ticks der neuen Rate führt. | 20,21 |

**`Game\Shell/Screens/CampaignScreens.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 150 | LoadGameScreen.Refresh row text | F/tick | Ladeliste zeigt den rohen Simulationstick des Spielstands: "{Title} (tick {Tick})" | eigen | Gleiche Spielzeit zeigt die doppelte Zahl; keine Umrechnung in Zeit vorhanden. | 21 |

### 6.2 `CnCNew.Presentation`

**`Pres\Camera/RtsCamera.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 22 | RtsCamera.FrameRate | F/originalbild | Kopie der Rate `FrameRate = 30f`: rechnet Wandzeit dt in 30-Hz-Originalbilder um (Verwendungen Z. 147, 195, 200, 217, 250, 252, 277); Kamera wird von MatchController.Update(deltaSeconds) getrieben | eigen (Kamera des Originals läuft pro Clientbild: W3DView.cpp W3DView::update) | Nichts, läuft auf Wandzeit; die 30 ist die Autoreneinheit der Kamerawerte und darf NICHT an die Tickrate gekoppelt werden. | 21 |
| 249-266 | RtsCamera.Update (_shakeTime, _shakeIntensity *= 0.75f) | F/originalbild | Shake-Feder: Wandzeit-Akkumulator mit festen Teilschritten 1/30 s, je Schritt Dämpfung 0,75 und Richtungsumkehr, Stopp unter 0,01; Auslöser MissionPresentation camera.Shake | W3DView.cpp W3DView::update (dampingCoeff = 0.75f) | Unverändert (Wandzeit mit festem 1/30-s-Schritt); nur falsch, wenn FrameRate an die Tickrate gehängt würde. | 21 |
| 277 | RtsCamera.Update (factor, AdjustSpeed) | F/originalbild | Zoom-Nachführung: factor = 1-(1-CameraAdjustSpeed)^(dt*30), bildratenunabhängige Form des Pro-Bild-Faktors | W3DView.cpp W3DView::update ((desiredZoom - m_zoom)*m_cameraAdjustSpeed) | Unverändert, Wandzeit. | 21 |
| 278-283 | RtsCamera.Update (MathF.Abs(step) >= 0.0001f) | E | Schwelle 0,0001 wird gegen den Zoomschritt eines Renderbilds verglichen (Schritt hängt von dt ab) | eigen | Tickrate ohne Einfluss; bei höherer Bildrate wird der Schritt kleiner und die Nachführung endet früher. | 21 |

**`Pres\Camera/RtsCameraSettings.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 46 | RtsCameraSettings.ScrollAmount | F/originalbild | `ScrollAmount = 100f`: Scrollbetrag pro 30-Hz-Bild; in RtsCamera mit dt*FrameRate bzw. in ScrollByIntent mit ScrollAmount*FrameRate (pro Sekunde) verrechnet | LookAtXlat.cpp SCROLL_AMT = 100 | Unverändert, Wandzeit. | 21 |
| 71 | RtsCameraSettings.AdjustSpeed (GameData CameraAdjustSpeed) | F/originalbild | Rohwert pro 30-Hz-Bild aus GameData.ini, unkonvertiert gelesen; Verbrauch in RtsCamera.Update über den Exponenten dt*30 | W3DView.cpp W3DView::update (TheGlobalData->m_cameraAdjustSpeed) | Unverändert, Wandzeit. | 21 |
| 77 | RtsCameraSettings.KeyboardRotateSpeed (GameData KeyboardCameraRotateSpeed) | F/originalbild | Rohwert rad pro Bild; in RtsCamera.RotateByKeyboard mit dt*30 verrechnet; RotateByKeyboard hat in src keinen Aufrufer (der Client dreht über CameraSettings.KeyboardRotateRadiansPerFrame) | GlobalData.cpp ("KeyboardCameraRotateSpeed", parseReal); InGameUI.cpp setAngle(getAngle() -/+ m_keyboardCameraRotateSpeed) | Unverändert, Wandzeit. | 21 |

**`Pres\Fx/FxAudit.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 94,133 | FxAudit.ExecuteAll (settleFrames = 300) | H/budget | Auslaufbudget 300 FX-Frames (10 s bei 30 Hz) nach je einem Frame pro FXList und Vorlage; Aufrufer PresentationTool FxAuditCommand | eigen | Mit skalierten FX-Daten reichen 300 Frames nur für 5 s; Kennzahlen (Peak, Stats, Frames) ändern sich. | 21 |
| 100-115 | FxAudit.ExecuteAll (checksum) | H/golden | FNV-Prüfsumme über Position, Größe und Alpha aller Partikel jedes Frames (FxExecutionSweep.Checksum) | eigen | Die Prüfsumme hängt an der Schrittfolge; jede Skalierung der FX-Daten oder Schrittweite ändert sie. | 21 |
| 122 | FxAudit.ExecuteAll (PrimarySpeed = 10f) | H/ticks | Testeingabe: Geschwindigkeit 10 Einheiten je Frame bei Ziel in 100 Einheiten Entfernung | eigen | Feste Pro-Frame-Eingabe; nur betroffen, wenn PrimarySpeed künftig pro Sekunde gemeint ist. | 21 |

**`Pres\Fx/FxCatalog.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 227 | WeaponFxInfo.WeaponSpeed (Weapon "WeaponSpeed") | F/tick | Waffengeschwindigkeit wird unabhängig von der Simulation aus dem INI-Text über VelocityPerFrame in Strecke pro Frame umgerechnet; dient als FxInvocation.PrimarySpeed für Tracer | Herkunft unbelegt | Zweite, eigene Umrechnung neben der Simulation: Tracer ohne eigenes Speed doppelt so schnell. | 21 |
| 317 | DamageFxLine.ThrottleFrames (DamageFX "ThrottleTime") | B/daten | ms→Frames (ceil, float) beim Katalogbau, Fallback 0; wird in FxExecutor.Damaged auf den Simulationstick addiert | DamageFX.cpp Feldtabelle ("ThrottleTime", parseTime) | Drosselzeit halbiert sich in Echtzeit; Rundung auf 30-Hz-Frames. | 21 |

**`Pres\Fx/FxEffects.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 10 | TracerInstance.Speed / TracerInstance.ExpireFrame | F/tick | Rendervertrag trägt Strecke pro Frame und Ablauf-Frame in FX-Frames | eigen | Renderer, die damit extrapolieren, rechnen in Tick-Frames; Verbraucher in Client.Stride nicht geprüft. | 21 |
| 30-31 | DecalInstance.CreatedFrame / ExpireFrame / FadeFrames | F/tick | Decal-Zeiten in FX-Frames im Rendervertrag (FadeFrames als Dauer); DecalBuffer.Expire Z. 72 vergleicht frame >= ExpireFrame | eigen | Ausblend- und Ablaufdauer in Frames halbieren sich in Echtzeit; Verbraucher in Client.Stride nicht geprüft. | 21 |
| 119-134 | CameraShake.Update | E | Shake-Feder ohne dt: je Aufruf Dämpfung *0,75, Richtungsumkehr, Stopp unter 0,01; in src weder Instanz noch Aufrufer gefunden (FxFrame.ViewShakes wird in src ebenfalls nicht verbraucht) | W3DView.cpp W3DView::update (dampingCoeff = 0.75f) | Hängt allein von der Aufrufhäufigkeit ab; bei Aufruf je Tick klingt der Shake doppelt so schnell ab. | 21,23 |

**`Pres\Fx/FxExecutor.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 141-149 | FxExecutor.Damaged (_damageThrottle NextTick = simEvent.Tick + throttle) | F/tick | DamageFX-Drossel: 30-basierte Frames werden auf den Simulationstick des Ereignisses addiert und gegen spätere Ereignisticks verglichen | eigen (Umsetzung); Datenfeld DamageFX.cpp "ThrottleTime" | Simulationstick: halbe Drosselzeit, bis zu doppelt so viele Schadenseffekte. | 21 |
| 235,345 | FxExecutor (Decals.AddScorch(.., Frame), Decals.Expire(frame)) | F/tick | Decals werden mit der FX-Framenummer gestempelt und gegen sie abgelaufen (Scorches haben keinen Ablauf) | eigen | FX-Frame = Simulationstick; Ablaufzeiten von Decals mit ExpireFrame halbieren sich. | 21 |
| 262-273 | FxExecutor.Tracer (lifetime, Frame + lifetime) | F/tick | Tracerlebensdauer = ceil((Distanz - Länge) / Geschwindigkeit pro Frame * DecayAt) in FX-Frames; Ablauf = Frame + lifetime | FXList.cpp TracerFXNugget::doFXPos (REAL_TO_INT_CEIL(frames * m_decayAt)) | Bei unveränderter Pro-Frame-Geschwindigkeit gleiche Framezahl und halbe Echtzeit; bei skalierter Geschwindigkeit andere Aufrundung. | 21 |
| 265-271 | FxExecutor.Tracer (frames = 1f) | B/literal | Ersatzdauer 1 Frame bei Geschwindigkeit 0 und bei negativer Reststrecke (zwei Literale 1f) | Herkunft unbelegt | 1 Frame ist 1/60 s statt 1/30 s. | 21 |
| 307 | FxExecutor.SpawnParticles (InitialDelayFrames) | B/daten | Laufzeitumrechnung je Spawn: gezogene Millisekunden → Frames über FxValueReader.MillisecondsToFrames (ceil, 30-basiert) | Herkunft unbelegt | Umrechnung zur Laufzeit statt beim Katalogbau; Verzögerung halbiert sich, Rundung auf 30-Hz-Frames. | 21 |
| 337-346 | FxExecutor.Update | F/tick | FX-Uhr: ein Aufruf = ein Logikframe (Partikel, Tracer, Lichter, Decals); einziger Laufzeitaufrufer in src ist LiveGame.SyncAfterTick, also genau einmal je Simulationstick (daneben FxAudit.ExecuteAll) | eigen (Kopplung an den Tick); Original: ParticleSys.cpp ParticleSystemManager::update | Simulationstick: alle Effekte laufen doppelt so schnell ab, solange die FX-Daten 30-basiert bleiben. | 21,23 |
| 377-381 | FxExecutor.TracerState.Advance | E | je Update: Opazität -= Opazität/(Restframes), Kopf += Richtung*speed (Strecke pro Frame) | Herkunft unbelegt (Kommentar nennt W3DTracerDraw::doDrawModule, nicht nachgelesen) | Pro-Tick-Schritt: Bewegung und Ausblenden doppelt so schnell; die Ausblendkurve hängt von der Schrittzahl ab. | 21,23 |
| 394-408 | FxExecutor.LightState.Advance | E | je Update ein Frame Auf- oder Abblenden: Zähler _increaseLeft/_decayLeft werden dekrementiert, Faktor linear in Frames | Herkunft unbelegt (Kommentar nennt W3DDynamicLight::On_Frame_Update, nicht nachgelesen) | Lichtpuls dauert halb so lang; mit skalierten Framezahlen gleiche Kurve in feineren Stufen. | 21,23 |

**`Pres\Fx/FxList.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 81 | TracerFxNugget.Speed (FXList Tracer "Speed") | F/tick | Tracergeschwindigkeit über VelocityPerFrame als Strecke pro FX-Frame abgelegt (0 = Geschwindigkeit des Aufrufers) | FXList.cpp TracerFXNugget-Feldtabelle ("Speed", INI::parseVelocityReal) | Tracer fliegt doppelt so schnell; Lebensdauer in Frames gleich, in Echtzeit halb. | 21 |
| 116 | LightPulseFxNugget.IncreaseFrames (FXList LightPulse "IncreaseTime") | B/daten | ms→Frames (ceil, float) beim Katalogbau, Fallback 0 | FXList.cpp LightPulseFXNugget-Feldtabelle ("IncreaseTime", INI::parseDurationUnsignedInt) | Aufblenden dauert halb so lang; Rundung auf 30-Hz-Frames. | 21 |
| 117 | LightPulseFxNugget.DecreaseFrames (FXList LightPulse "DecreaseTime") | B/daten | ms→Frames (ceil, float) beim Katalogbau, Fallback 0 | FXList.cpp LightPulseFXNugget-Feldtabelle ("DecreaseTime", INI::parseDurationUnsignedInt) | Abblenden dauert halb so lang; Rundung auf 30-Hz-Frames. | 21 |
| 168 | ParticleSystemFxNugget.InitialDelay (FXList ParticleSystem "InitialDelay") | B/daten | Millisekunden als Zufallsvariable roh gespeichert (Standard -1 = Vorlage behalten); Umrechnung in Frames erst zur Laufzeit je Spawn (FxExecutor Z. 307) | Herkunft unbelegt | Siehe FxExecutor.SpawnParticles: Laufzeitumrechnung mit der 30er-Konstante. | 21 |

**`Pres\Fx/FxValueReader.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 17 | FxValueReader.FramesPerSecond | F/tick | Kopie der Rate `public const float FramesPerSecond = 30f`; Grundlage aller FX-Umrechnungen (ms→Frames, pro Sekunde→pro Frame) beim Katalogbau; die FX-Frames werden einmal je Simulationstick gestuft | eigen | Bleibt 30, während FxExecutor.Update 60-mal/s läuft: alle FX-Dauern halbieren sich, alle FX-Geschwindigkeiten verdoppeln sich. | 21 |
| 18 | FxValueReader.FramesPerMillisecond | F/tick | abgeleitete Ratenkopie `FramesPerSecond / 1000f` (0,03 Frames je ms) | eigen | Wie FramesPerSecond: ms-Dauern ergeben halbe Echtzeit. | 21 |
| 19 | FxValueReader.SecondsPerFrame | F/tick | abgeleitete Ratenkopie `1f / FramesPerSecond` (1/30 s) | eigen | Wie FramesPerSecond: Geschwindigkeiten pro Frame wirken doppelt so schnell. | 21 |
| 85-91 | FxValueReader.DurationFrames / MillisecondsToFrames | B/daten | ms→Frames mit MathF.Ceiling in float (ms*0,03); Katalogbauzeit für DurationFrames, zusätzlich Laufzeitaufruf aus FxExecutor.SpawnParticles | Herkunft unbelegt (Kommentar nennt INI::parseDurationUnsignedInt, dessen Rumpf nicht nachgelesen) | Aufrundung auf ganze 30-Hz-Frames; ohne Anpassung halbe Dauer, mit Anpassung andere Rundungsgrenzen. | 21 |
| 94 | FxValueReader.VelocityPerFrame | F/tick | Strecke/s → Strecke/Frame durch Multiplikation mit SecondsPerFrame (1/30) beim Katalogbau; der Fallback wird vorher mit *30 zurückgerechnet | Herkunft unbelegt (INI::parseVelocityReal nur als Feldparser in FXList.cpp gesehen) | Pro-Frame-Geschwindigkeiten bleiben 30-basiert, Bewegung doppelt so schnell. | 21 |

**`Pres\Fx/ParticleSystemTemplate.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 138 | ParticleSystemTemplate.AngularRateZ ("AngularRateZ") | D | Drehrate in rad je Frame, roh gelesen | ParticleSys.cpp m_fieldParseTable ("AngularRateZ", parseGameClientRandomVariable) | Partikel drehen doppelt so schnell. | 21 |
| 139 | ParticleSystemTemplate.AngularDamping ("AngularDamping") | D | multiplikativer Dämpfungsfaktor je Frame, roh gelesen | Herkunft unbelegt (Feldzeile nicht nachgelesen) | Dämpfung wirkt doppelt so oft pro Sekunde. | 21,23 |
| 140 | ParticleSystemTemplate.VelocityDamping ("VelocityDamping") | D | multiplikativer Dämpfungsfaktor je Frame, roh gelesen | ParticleSys.cpp m_fieldParseTable ("VelocityDamping", parseGameClientRandomVariable) | Dämpfung wirkt doppelt so oft pro Sekunde. | 21,23 |
| 141 | ParticleSystemTemplate.Gravity ("Gravity") | D | Beschleunigung in Strecke je Frame², roh gelesen | ParticleSys.cpp m_fieldParseTable ("Gravity", parseReal) | Vierfache Beschleunigung pro Sekunde². | 21,23 |
| 145 | ParticleSystemTemplate.Lifetime ("Lifetime") | D | Partikellebensdauer in Frames, roh gelesen | ParticleSys.cpp m_fieldParseTable ("Lifetime", parseGameClientRandomVariable) | Halbe Lebensdauer. | 21 |
| 146 | ParticleSystemTemplate.SystemLifetime ("SystemLifetime") | D | Systemlebensdauer in Frames, roh gelesen | ParticleSys.cpp m_fieldParseTable ("SystemLifetime", parseUnsignedInt) | Halbe Lebensdauer. | 21 |
| 149 | ParticleSystemTemplate.SizeRate ("SizeRate") | D | Größenänderung je Frame, roh gelesen | ParticleSys.cpp m_fieldParseTable ("SizeRate", parseGameClientRandomVariable) | Partikel wachsen doppelt so schnell. | 21 |
| 150 | ParticleSystemTemplate.SizeRateDamping ("SizeRateDamping") | D | multiplikativer Dämpfungsfaktor je Frame, roh gelesen | Herkunft unbelegt (Feldzeile nicht nachgelesen) | Dämpfung wirkt doppelt so oft pro Sekunde. | 21,23 |
| 152 | ParticleSystemTemplate.BurstDelay ("BurstDelay") | D | Frames zwischen zwei Bursts, roh gelesen | ParticleSys.cpp m_fieldParseTable ("BurstDelay", parseGameClientRandomVariable) | Doppelte Emissionsrate. | 21,23 |
| 154 | ParticleSystemTemplate.InitialDelay ("InitialDelay") | D | Startverzögerung in Frames, roh gelesen (anders als das gleichnamige FXList-Feld in ms) | ParticleSys.cpp m_fieldParseTable ("InitialDelay", parseGameClientRandomVariable) | Halbe Verzögerung. | 21 |
| 155 | ParticleSystemTemplate.DriftVelocity ("DriftVelocity") | D | Drift in Strecke je Frame, roh gelesen | ParticleSys.cpp m_fieldParseTable ("DriftVelocity", parseCoord3D) | Doppelte Drift pro Sekunde. | 21 |
| 157-165 | ParticleSystemTemplate.VelocitySlots (VelOrthoX/Y/Z, VelSpherical, VelHemispherical, VelCylindricalRadial/Normal, VelOutward, VelOutwardOther) | D | Emissionsgeschwindigkeiten in Strecke je Frame, roh gelesen (neun gleichartige Schlüssel, eine Zeile) | ParticleSys.cpp m_fieldParseTable ("VelOrthoX", parseGameClientRandomVariable; übrige Schlüssel nicht einzeln nachgelesen) | Partikel fliegen doppelt so schnell. | 21 |
| 178-179,268 | ParticleSystemTemplate.WindAngleChangeMin / WindAngleChangeMax (Standard 0.15f / 0.45f) | D | Windwinkeländerung in rad je Frame, roh gelesen; Standardwerte 0,15 und 0,45 | ParticleSys.cpp m_fieldParseTable ("WindAngleChangeMin", parseReal); Standardwerte nicht nachgelesen | Wind pendelt doppelt so schnell. | 21 |
| 220-242 | AlphaKeyframe.Frame / ColorKeyframe.Frame (Alpha1..8, Color1..8) | D | Keyframe-Zeitpunkte in Frames seit Partikelerzeugung, roh gelesen | ParticleSys.cpp ParticleSystemTemplate::parseRandomKeyframe ("Alpha1".."Alpha8"); parseRGBColorKeyframe nicht nachgelesen | Verläufe laufen doppelt so schnell. | 21 |

**`Pres\Fx/ParticleWorld.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 111-121 | ParticleWorld.Step (_frame++) | F/tick | Partikeluhr: ein Step = ein Logikframe (laut Kommentar 1/30 s); von FxExecutor.Update einmal je Simulationstick aufgerufen | ParticleSys.cpp ParticleSystemManager::update | Simulationstick; ohne Umbau doppelte Partikelgeschwindigkeit und halbe Lebensdauern. | 21,23 |
| 165,216-220 | SystemState.DelayLeft | D | InitialDelay der Vorlage roh als Frames (uint-Abschneiden) bzw. vom Nugget umgerechnet; je Step um 1 dekrementiert, System pausiert solange | ParticleSys.cpp m_fieldParseTable ("InitialDelay", parseGameClientRandomVariable, keine Zeitumrechnung) | Startverzögerung halbiert sich. | 21 |
| 170,311-342 | ParticleWorld.UpdateWind (WindAngleChange = 0.15f, Minimum 0.005f) | E | Windwinkel ändert sich je Step um WindAngleChange (rad je Frame; Startwert 0,15, Minimum 0,005, Daten WindAngleChangeMin/Max roh); Zufallsziehungen bei der Umkehr | ParticleSys.cpp (m_windAngleChange = 0.15f; "WindAngleChangeMin", parseReal) | Wind pendelt doppelt so schnell, Zufallsziehungen doppelt so oft. | 21,23 |
| 260-290 | ParticleWorld.UpdateSystem (BurstDelayLeft, BurstCount) | E | Emission: alle BurstDelay Frames (roh, uint-Abschneiden) BurstCount Partikel; BurstDelay 0 heißt ein Burst je Step; Zufallsziehungen je Burst und Partikel | ParticleSys.cpp ParticleSystem::update (m_burstDelayLeft) | Doppelte Emissionsrate und doppelt so viele Zufallsziehungen pro Sekunde; BurstDelay 0 lässt sich nicht durch Skalieren der Frames ausgleichen. | 21,23 |
| 292-298 | ParticleWorld.UpdateSystem (gravity) | E | Gravity der Vorlage (roh, Strecke je Frame²) wird je Step auf die Beschleunigung jedes Partikels addiert | ParticleSys.cpp m_fieldParseTable ("Gravity", parseReal); Particle::update (m_vel.z += m_accel.z) | Vierfache Beschleunigung pro Sekunde². | 21,23 |
| 302-307 | SystemState.LifetimeLeft | D | SystemLifetime roh in Frames, je Step dekrementiert (0 = dauerhaft) | ParticleSys.cpp m_fieldParseTable ("SystemLifetime", parseUnsignedInt) | Systemlebensdauer halbiert sich. | 21 |
| 350 | ParticleWorld.CreateParticle (_fieldParticlesLastFrame) | C | Feldpartikel-Obergrenze vergleicht mit der Zählung des letzten Capture; Capture läuft laut FxLayer-Kommentar je Renderbild, nicht je Step | eigen | Nur Menge/Last: doppelt so viele Emissionsversuche zwischen zwei Zählungen. | 21 |
| 404-415 | ParticleWorld.UpdateParticle (velocity, angle, size) | E | Euler je Step: v += a; v *= VelocityDamping; pos += v + DriftVelocity; Winkel += AngularRate; AngularRate *= AngularDamping; Size += SizeRate; SizeRate *= SizeRateDamping | ParticleSys.cpp Particle::update (m_vel.x *= m_velDamping) | Multiplikative Dämpfung je Step wirkt bei 60 Hz quadriert pro 1/30 s, Bewegung doppelt so schnell; Skalieren verlangt die Wurzel der Dämpfungen und ändert die Bahn. | 21,23 |
| 417-448 | ParticleWorld.UpdateParticle (alpha/color keyframes, ColorScale) | E | elapsed = frame - CreatedFrame wird gegen Keyframe-Frames (roh) verglichen; Alpha += AlphaRate, Color += ColorRate und Color += ColorScale je Step | ParticleSys.cpp ParticleSystemTemplate::parseRandomKeyframe; Particle::update | Farb- und Alphaverläufe laufen doppelt so schnell; der ColorScale-Zuschlag verdoppelt sich pro Sekunde. | 21,23 |
| 451 | Particle.LifetimeLeft | D | Lifetime der Vorlage roh in Frames (Z. 514, uint-Abschneiden), je Step dekrementiert | ParticleSys.cpp m_fieldParseTable ("Lifetime", parseGameClientRandomVariable) | Partikellebensdauer halbiert sich. | 21 |
| 468-478 | ParticleWorld.ApplyWind | E | Position += 2*WindRandomness Einheiten je Step (feste Stärke pro Frame, Abfall zwischen 75 und 200 Einheiten) | Herkunft unbelegt (Kommentar nennt updateWindMotion, nicht nachgelesen) | Doppelte Windverschiebung pro Sekunde. | 21,23 |
| 501-507 | ParticleWorld.GenerateInfo (system.Pos - system.LastPos) | E | Burst wird entlang der Emitterbewegung des letzten Steps verteilt (LastPos = Position vor einem Step) | Herkunft unbelegt | Halb so lange Verteilstrecke je Step bei doppelt so vielen Bursts; ähnlich, aber nicht identisch. | 23 |
| 772-786 | Particle.ComputeAlphaRate / Particle.ComputeColorRate | D | Rate je Frame = Wertdifferenz / Differenz der Keyframe-Frames (roh aus den Daten) | ParticleSys.cpp ParticleSystemTemplate::parseRandomKeyframe | Rate je Frame bleibt gleich, der Verlauf ist in Echtzeit doppelt so schnell. | 21 |

**`Pres\Fx/SoundCues.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 25 | SoundCueDeriver.UnderAttackFrames | A | `30 * 10` = 300 Ticks (10 s) Sperrzeit je Spieler im Umkreis 250 für die Unter-Beschuss-Meldung; enthält die Rate als Literal 30; Z. 140 gegen simEvent.Tick verglichen (1 Verwendung) | Radar.cpp Radar::tryEvent (framesBetweenEvents = LOGICFRAMES_PER_SECOND * 10) | Simulationstick: Sperrzeit nur 5 s, doppelt so viele Warnmeldungen. | 21 |
| 179 | SoundCueLimits.MaxPerTick | C | `MaxPerTick = 48` Töne je Simulationstick (Z. 204 und 214) | eigen | Ergebnis ändert sich: die Obergrenze pro Sekunde verdoppelt sich (hörbar, nicht nur Last). | 21 |
| 179 | SoundCueLimits.WindowTicks | F/tick | `WindowTicks = 10` Ticks (1/3 s) Fenster je Ereignisname; Filter Z. 212 rechnet mit cue.Tick; Kopie in src/CnCNew.Game/Audio/AudioDirector.cs Z. 31 `new(3, 10, 0f, 48)` (anderes Paket) | eigen | Simulationstick: Fenster nur 1/6 s, doppelt so viele gleiche Töne pro Sekunde. | 21 |

**`Pres\Input/CameraInput.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 18 | CameraSettings.KeyboardRotateRadiansPerFrame | F/originalbild | `= 0.1f` rad pro 30-Hz-Bild (fest kopiert, nicht aus GameData gelesen); Z. 132-133 mal frames; MatchController.MapCameraSettings skaliert den Wert mit dem Optionsregler | GlobalData.cpp (m_keyboardCameraRotateSpeed = 0.1f) | Unverändert, Wandzeit. | 21 |
| 21 | CameraSettings.KeyboardZoomPerFrame | F/originalbild | `= 10f` Höheneinheiten pro 30-Hz-Bild bei gehaltener Zoomtaste; Z. 135-136 mal frames | Herkunft unbelegt | Unverändert, Wandzeit. | 21 |
| 23 | CameraSettings.FramesPerSecond | F/originalbild | Kopie der Rate `FramesPerSecond = 30f` (Record-Standardwert); Z. 99 frames = deltaSeconds*FramesPerSecond für Tastaturdrehung und Tastaturzoom | eigen | Unverändert (Wandzeit aus MatchController.Update); darf nicht auf die Tickrate umgestellt werden. | 21 |

**`Pres\Input/CommandBarModel.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 54,283,307 | CommandBarButton.BuildTicks | F/tick | Bauzeit in Simulationsticks (template.BuildTicks, upgrade.BuildTicks) wird unkonvertiert an die HUD-Schicht gereicht | eigen | Der Wert ist tickratenabhängig; Verbraucher (Tooltip/HUD, nicht in diesem Paket) müssen mit der Rate in Sekunden umrechnen. | 21 |

**`Pres\Input/CommandModeController.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 19 | CommandSettings.MarkerFrames | F/originalbild | Lebensdauer aller übrigen Marken/Rückmeldungen (Angriff, Betreten, Sammelpunkt, ungültig, abgelehnt) = 40 Client-Frames (Default); 12 Verwendungen | eigen (analog zur Bewegungsmarke; das Original markiert Angriffsziele durch Blinken) | Kein Unterschied, solange OrderHintBook mit festen 30 fps umrechnet; Wanduhr 1333 ms. | 21 |
| 19 | CommandSettings.MoveHintFrames | F/originalbild | Lebensdauer der Bewegungs-/Wegpunktmarke = 40 Client-Frames (Default); 3 Verwendungen (Zeilen 258, 330, 573); wird in OrderHintBook.Observe mit festen 30 fps in Millisekunden umgerechnet | GameEngineDevice/Source/W3DDevice/GameClient/W3DInGameUI.cpp W3DInGameUI::drawMoveHints (elapsed <= 40 gegen TheGameClient->getFrame()) | Kein Unterschied, solange die Umrechnung bei festen 30 Client-Frames/s bleibt (1333 ms Wanduhr); würde sie an die Tickrate gehängt, halbierte sich die Dauer. | 21 |
| 698-703 | CommandModeController.Update | C | Je Simulationstick: StandingOrders.Update und Abbruch des Platziermodus, wenn der Bauarbeiter fehlt/tot ist | eigen | Platzierabbruch: nur Last/halbe Latenz; der Orders-Teil siehe StandingOrders.Update. | 21 |

**`Pres\Input/CommandTypes.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 93 | CommandFeedback.Tick | F/tick | Zeitstempel jeder Rückmeldung in Simulationsticks (world.Tick bzw. Tick des abgelehnten Befehls, sonst 0); in src kein Leser gefunden (Grep feedback.Tick), die Lebensdauer läuft über die Wanduhr | eigen | Kein Effekt, solange niemand daraus eine Dauer bildet; ein künftiger Leser würde mit Ticks statt Zeit rechnen. | 21 |

**`Pres\Input/GameInputController.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 91-95 | GameInputController.Tick | C | Einmal je Simulationstick vor dem Schritt (LiveGame.Advance -> BeforeTick, gesetzt in MatchController Zeile 114): Selection.Prune + Commands.Update | eigen | Läuft doppelt so oft; Prune ändert nur Last/Latenz, über Commands.Update (StandingOrders) ändern sich aber Befehlszeitpunkte, also Ergebnisse. | 21 |

**`Pres\Input/InputState.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 17 | InputSettings.MiddleClickMaxMilliseconds | F/wand | Mittelklick-Grenze 167 ms = 5 Client-Frames bei 30 fps (30er-Konstante in Wanduhr-Millisekunden); Uhr = InputEvent.TimeMs des Clients | GameEngine/Source/GameClient/MessageStream/LookAtXlat.cpp MSG_RAW_MOUSE_MIDDLE_BUTTON_UP: const UnsignedInt CLICK_DURATION = 5 gegen TheGameClient->getFrame() | Kein Unterschied (Wanduhr); die 167 ms sind eine eingefrorene 5/30-Umrechnung und dürfen nicht mit der Tickrate skaliert werden. | 21 |

**`Pres\Input/OrderMarkers.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 41 | OrderHintBook.ClientFramesPerSecond | F/originalbild | Kopie der Rate: const int = 30 (Client-Frames/s, nicht SimulationTiming); 1 Verwendung Zeile 59: Lebensdauer = DurationFrames * 1000 / 30 ms; Uhr = MatchController._clockMs (Summe der Frame-Deltas, Wanduhr bzw. fixedSeconds im Smoke-Test) | Einheit aus W3DInGameUI.cpp drawMoveHints (Client-Frames); die Zahl 30 selbst: eigen | Kein Unterschied (Wanduhr); die 30 darf bei der Parametrisierung NICHT durch die Tickrate ersetzt werden, sonst leben Marken nur 667 ms. | 21 |

**`Pres\Input/StandingOrders.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 77-152 | StandingOrders.Update / UpdateAttack | C | Mehrtick-Befehle (Angriff, Angriffsmarsch, Wegpunkte, Anflug) werden je Simulationstick geprüft und als Einzelbefehle neu erteilt (FireWeaponCommand in jedem Tick mit CanFireAt, Pursue/Move bei Zielbewegung > 10 m); alle gehen über die Senke ins Replay | eigen | Ergebnisse ändern sich: doppelt so viele Prüfungen und Feuerbefehle je Sekunde im Replay-Strom, Reaktionslatenz (Nachsetzen, nächster Wegpunkt, Abdrehen) halbiert sich von 33 auf 17 ms. | 21 |
| 88 | StandingOrders.Update settled (IssuedTick + 1) | E | Reihenfolge-Annahme "einen Tick später": ein Befehl gilt erst als wirksam, wenn world.Tick > IssuedTick + 1 (Literal 1 Tick); davor wird HasMoveTarget/IsAttackMoving nicht ausgewertet | eigen | Bleibt als Abfolge korrekt, solange Befehle im Erteilungstick ausgeführt werden; die Wartezeit halbiert sich in Wanduhr (67 -> 33 ms). Bei einer künftigen Befehlsverzögerung in Ticks bricht die Annahme. | 21,23 |

**`Pres\Objects/ConditionDeriver.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 10,165-174 | ConditionContext.PreviousConstructionWork / ConditionDeriver.Construction | F/tick | ACTIVELY_BEING_CONSTRUCTED = Bauarbeit (int) ist gegenüber der Aufnahme des vorigen Simulationsticks gestiegen (Vergleich über genau einen Tick; FrameProjector Zeile 252-253 liefert previous) | eigen | Flackert, falls ConstructionWork bei 60 Hz nicht mehr in jedem Tick steigt (hängt von der Skalierung von BuildTicks/Arbeit je Tick in Phase 20 ab); sonst unverändert. | 21 |
| 156 | ConditionDeriver.Weapon FIRING (lastFire == world.Tick - 1) | F/tick | FIRING_A/B/C gilt genau einen Simulationstick lang (Schuss im zuletzt simulierten Tick); aufgerufen aus FrameProjector.CaptureEntity <- Advance <- LiveGame.SyncAfterTick, einmal je Simulationstick | eigen (Zuordnung nach WeaponSet::getModelConditionForWeaponSlot laut Kommentar, dort nicht nachgeprüft) | FIRING dauert 16,7 statt 33 ms; bei zwei Ticks je gezeichnetem Bild kann die Aufnahme mit FIRING überschrieben werden, bevor ein Bild sie sieht (Schussanimation/Mündungsfeuer fällt aus). | 21 |

**`Pres\Objects/DrawSpecCompiler.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 73,80 | DrawSpecCompiler.ModuleKey (DrawSetting-Rohtext) | D | Zeitbehaftete Draw-Schlüssel werden unkonvertiert als Text in DrawSpec.Settings abgelegt (Zeile 80 prüft nur die Zahl): InitialRecoilSpeed, RecoilSettleSpeed, TreadAnimationRate, RecoilDamping, RotationDamping, MoveOutwardTime, MoveInwardTime, SinkTime, MaxIntensityLifetime, FadeLifetime | W3DModelDraw.cpp buildFieldParse (InitialRecoilSpeed/RecoilSettleSpeed parseVelocityReal, RecoilDamping parseReal); W3DTankDraw.cpp (TreadAnimationRate parseVelocityReal); W3DTruckDraw.cpp (RotationDamping parseReal); W3DTreeDraw.cpp und W3DLaserDraw.cpp (parseDurationUnsignedInt) | Hier keine Wirkung; jeder Verbraucher muss selbst von je-Sekunde/Millisekunden bzw. je-Frame-Faktor umrechnen. Derzeit liest nur ObjectLayerCore.cs 388-393 (TireRotationMultiplier, Cab/Trailer, RotationDamping als Faktor je Update). | 21 |

**`Pres\Objects/FrameProjector.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 31 | FrameProjectorOptions.SnapDistance | E | Teleport-Schwelle 100 Welteinheiten Weg innerhalb EINES Ticks (keine Interpolation, kein Radweg, Fahrwerk-Geschwindigkeit 0); 3 Verwendungen (Project Z.222, CaptureEntity Z.266, SuspensionTracker.Update Z.86) | eigen | Bei 60 Hz halbiert sich der Weg je Tick, die Schwelle entspricht dann 6000 statt 3000 Einheiten/s; schnelle Sprünge werden eher interpoliert. | 21,23 |
| 51 | FrameProjector.MuzzleFlashTicks | F/tick | Mündungsfeuer-Dauer = 4 Ticks nach dem Schuss (Vergleich world.Tick - LastFireTickOf(slot) <= 4, Z.276; nutzt world.Tick, nicht Projektor-Tick) | eigen (Kommentar: Original zeigt es im ersten gezeichneten Rückstoß-Frame; nicht nachgeschlagen) | Tick-Uhr: bei 60 Hz nur noch 0,067 s statt 0,13 s sichtbar. | 21 |
| 54 | FrameProjector.HealingIconTicks | F/tick | Heilsymbol-Dauer = 3 * 30 Ticks (Kopie der Rate als Literal 30); 2 Verwendungen in Z.319 (Alter <= 90 und Startsperre Tick > 90); HealedAtTick wird mit world.Tick gesetzt (Z.273), aber gegen Tick = world.Tick-1 verglichen | GameEngine/Source/GameClient/Drawable.cpp: HEALING_ICON_DISPLAY_TIME = LOGICFRAMES_PER_SECOND * 3 (Z.247) | Tick-Uhr: bei 60 Hz verschwindet das Symbol nach 1,5 s statt 3 s; auch die Startsperre halbiert sich. | 21 |
| 140-176 | FrameProjector.Advance / FrameProjector.Tick | F/tick | Uhr des Projektors: Tick = world.Tick - 1; Advance läuft genau einmal je Simulations-Tick (LiveGame.SyncAfterTick Z.203-212, Schleife in LiveGame.Advance mit TickSeconds = 1.0/LogicFramesPerSecond); treibt alle Tracker (Blitz, Fahrwerk, Türen, Lenkung) | eigen | Tick-Uhr: alle abhängigen Zustände laufen bei 60 Hz doppelt so oft; Project(alpha) interpoliert nur Position, Gier und Turmwinkel. | 21 |
| 162 | FrameProjector.Advance (world.Tick % 150) | C | Aufräumen der Tracker-Tabellen alle 150 Ticks (5 s) für nicht mehr vorhandene Entitäten | eigen | Nur Last/Speicher: bei 60 Hz alle 2,5 s; kein sichtbares Ergebnis (abgelaufene Blitze sind schon unsichtbar). | 21 |
| 179 | FrameProjector.SceneryFogInterval | C | Sichtbarkeit erkundeter Szenerie wird alle 5 Ticks neu gelesen (world.Tick % 5, Z.187); 1 Verwendung | eigen | Ergebnis nur in der Darstellung: Abdunkeln folgt mit höchstens 83 ms statt 167 ms Verzug; sonst Last (doppelt so viele Sichtabfragen je Sekunde). | 21 |

**`Pres\Objects/RenderFrame.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 77 | RenderFrame.Tick / RenderFrame.Alpha | F/tick | Bild trägt den letzten simulierten Tick und den Interpolationsfaktor (Alpha = Akkumulator/TickSeconds aus LiveGame.Advance Z.197); Verbraucher von RenderFrame.Tick außerhalb des Pakets nicht geprüft | eigen | Tick-Uhr als Träger: Alpha bleibt 0..1; wer RenderFrame.Tick als Zeit liest, läuft bei 60 Hz doppelt so schnell. | 21 |

**`Pres\Objects/SelectionFlash.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 31 | SelectionFlashTracker.DecayTicks | F/tick | Abklingdauer des Auswahl-/Übernahmeblitzes = 4 Ticks; LastVisibleAge (Z.34) ist davon abgeleitet; Verwendungen: Envelope Z.57, Prune Z.115 | GameEngine/Source/GameClient/Drawable.cpp: Drawable::flashAsSelected, m_selectionFlashEnvelope->play( color, 0, 4 ) (Z.1366) | Tick-Uhr: Blitz dauert bei 60 Hz 5 Ticks = 83 ms statt 167 ms. | 21 |
| 53-59 | SelectionFlashTracker.Envelope | F/tick | Hüllkurve als Stufenfunktion des Alters in Ticks: Literal '<= 1' (Spitze zwei Ticks), danach 3/4, 1/2, 1/4; Alter = Projektor-Tick - SimEvent.Tick (TintAt Z.110), nicht interpoliert | Herkunft unbelegt (Kommentar nennt TintEnvelope::update; nur play(color,0,4) nachgeschlagen) | Tick-Uhr: Stufen halb so lang; bei 60 Hz kaum noch wahrnehmbar. | 21 |
| 74 | SelectionFlashTracker.ScriptFlashInterval | F/tick | Skriptblitz alle 15 Ticks (eine halbe Sekunde bei 30 Hz); Verwendungen: StartScripted Z.85, Advance Z.95 (tick % 15 auf dem globalen Projektor-Tick) | GameEngine/Include/GameClient/Drawable.h: DRAWABLE_FRAMES_PER_FLASH = LOGICFRAMES_PER_SECOND / 2 (Z.279); Drawable.cpp Z.1243 getFrame() % DRAWABLE_FRAMES_PER_FLASH | Tick-Uhr: bei 60 Hz blitzt es alle 0,25 s. | 21 |
| 85 | SelectionFlashTracker.StartScripted (count) | F/tick | Anzahl Blitze = seconds * SimulationTiming.LogicFramesPerSecond / ScriptFlashInterval (Ganzzahldivision, 30/15 = 2 je Sekunde); mischt die Raten-Konstante mit dem festen Intervall 15 | GameEngine/Source/GameLogic/ScriptEngine/ScriptActions.cpp Z.2634: count = frames / DRAWABLE_FRAMES_PER_FLASH | Wird nur die Rate 60, das Intervall bleibt 15: 4 n Blitze in n Sekunden (doppelt so viele); skalieren beide, bleibt es bei 2 n. | 21 |

**`Pres\Objects/StructureConditions.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 269-346 | StructureConditionTracker.Production / UpdateDoor | F/tick | Türuhr der Produktionsgebäude auf world.Tick: Stempel Opened/WaitOpen/Closed/ConstructionComplete, verglichen mit ProductionData.DoorOpeningTicks/DoorWaitOpenTicks/DoorCloseTicks/ConstructionCompleteTicks und head.RemainingTicks; Literale '+ 1' (Z.325, 334) und LastHold + 1 | eigen (Port von ProductionUpdate::updateDoors laut Kommentar; nicht nachgeschlagen) | Tick-Uhr, folgt den Tick-Dauern der Simulation: stimmt, sobald ProductionData skaliert wird; die '+1'-Versätze sind dann 1/60 s. | 21 |
| 285 | StructureConditionTracker.Production (Math.Max(lead, 1)) | B/literal | Tür/Bauabschluss startet, wenn head.RemainingTicks <= max(DoorOpeningTicks, 1): Mindestvorlauf 1 Tick als Literal | eigen | Mindestvorlauf 1/60 s statt 1/30 s; ohne Türanimation praktisch ohne Wirkung. | 21 |

**`Pres\Objects/Suspension.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 21 | SuspensionMotion.LogicTicksPerSecond | F/tick | Private Kopie der Rate '= 30'; 1 Verwendung Z.36: BounceAmount (Grad/s) * Degrees / 30 -> BounceKick in Radiant je Tick, beim Katalogbau | GameEngine/Source/GameLogic/Object/Locomotor.cpp: Feldtabelle 'BounceAmount', INI::parseAngularVelocityReal (Z.469) | Bleibt die 30 stehen, ist der Stoß je Tick bei 60 Hz doppelt so groß wie die Datenangabe je Sekunde. | 21 |
| 33-38 | SuspensionMotion.FromLocomotor (PitchStiffness, RollStiffness, PitchDamping, RollDamping, ForwardAccelerationPitchFactor, LateralAccelerationRollFactor, UniformAxialDamping) | D | Feder-/Dämpfungs- und Beschleunigungsfaktoren roh je Logik-Frame gelesen (Standard 0.1 / 0.1 / 0.9 / 0.9 / 0 / 0 / 1), ohne Umrechnung; werden in Update je Tick angewandt | Herkunft unbelegt (Kommentar nennt LocomotorTemplate::LocomotorTemplate; Standardwerte nicht nachgeschlagen) | Je Tick angewandt: bei 60 Hz schwingt und dämpft das Fahrwerk in der halben Zeit, Beschleunigungsneigung wird kleiner. | 21,23 |
| 82-99 | SuspensionTracker.Update (velX/velY, accelX/accelY, yawStep, ForwardEpsilon, TurnEpsilon) | E | Geschwindigkeit = Positionsdifferenz je Tick, Beschleunigung = Differenz der Tick-Geschwindigkeiten, Gierschritt je Tick; Schwellen ForwardEpsilon 1e-4 (Einheiten/Tick) und TurnEpsilon 0.002 rad/Tick | eigen (Original liest PhysicsBehavior-Werte; hier aus Positionsdifferenzen) | Bei 60 Hz halbiert sich die Geschwindigkeit und viertelt sich die Beschleunigung je Tick; langsame Drehungen unter 0,002 rad/Tick lenken die Räder nicht mehr ein. | 21,23 |
| 116-118 | SuspensionTracker.Update (airborne wheels) | E | Hinterräder nähern sich in der Luft je Tick um die Hälfte dem Ziel: += (target - x) / 2 | Herkunft unbelegt (Kommentar: calcPhysicsXformWheels) | Halbierung je Tick: bei 60 Hz doppelt so schnelles Ausfedern. | 21,23 |
| 126-136 | SuspensionTracker.Update (bounce kick, Hash(id, world.Tick)) | E | Bodenstoß: je Tick geprüft (speed > Höchsttempo/10, Raten unter Schwelle), Richtung aus Hash(Entität, world.Tick) & 3; addiert BounceKick*factor auf die Tick-Raten | GameEngine/Source/GameClient/Drawable.cpp Z.2012-2031 und Z.2307-2314 (BOUNCE_ANGLE_KICK = locomotor->getBounceKick()) | Bei 60 Hz doppelt so viele Prüfungen und Zufallsziehungen je Sekunde; andere Stoßfolge und -häufigkeit (nur Darstellung). | 21,23 |
| 139-160 | SuspensionTracker.Update (spring/damper integration) | E | Euler-Schritt je Tick: Rate += -Steifigkeit*(Winkel - Boden) - Dämpfung*Rate; PitchRate *= 0.5 bei steigender Nase; Winkel += Rate * UniformAxialDamping; gleich für Beschleunigungsneigung; ChassisPose wird nicht interpoliert | GameEngine/Source/GameClient/Drawable.cpp (calcPhysicsXformTreads/Wheels laut Kommentar; nur die BOUNCE/SPRING-Zeilen nachgeschlagen) | Gleiche Koeffizienten doppelt so oft: Einschwingen in halber Zeit, anderes Überschwingen; multiplikative Dämpfung 0,5 je Tick wirkt doppelt. | 21,23 |
| 179 | SuspensionTracker.UpdateWheels (WheelAngle smoothing / 10f) | E | Lenkwinkel nähert sich je Tick um ein Zehntel: WheelAngle += (wanted - WheelAngle) / 10 | GameEngine/Source/GameClient/Drawable.cpp Z.2124: m_wheelAngle += (new - old)/WHEEL_SMOOTHNESS | Bei 60 Hz lenken die Räder doppelt so schnell ein. | 21,23 |
| 210-219 | SuspensionTracker.UpdateWheels.Follow | E | Ein absinkendes Rad folgt je Tick nur zur Hälfte (current + (target - current)/2), ein steigendes sofort | Herkunft unbelegt (SPRING_FACTOR 0.9 in Drawable.cpp Z.2126 belegt, die Halbierung nicht nachgeschlagen) | Halbierung je Tick: bei 60 Hz doppelt so schnelles Nachfedern. | 21,23 |

**`Pres\Objects/UnitConditionTracker.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 27 | UnitConditionTracker.CheerTicks | F/tick | Jubeldauer = 3 * 30 Ticks (Kopie der Rate als Literal 30); Verwendung Z.86 (CheerUntil = tick + CheerTicks), Vergleich Z.134; für FrameProjector.Cheer in src kein Aufrufer gefunden | GameEngine/Source/GameLogic/AI/AIGroup.cpp Z.2784: setSpecialModelConditionState( MODELCONDITION_SPECIAL_CHEERING, LOGICFRAMES_PER_SECOND * 3 ) | Tick-Uhr: bei 60 Hz nur 1,5 s Jubel. | 21 |
| 30 | UnitConditionTracker.TensileFreefallLife | A | FREEFALL nur solange TensileFormationUpdate.Life < 100 (Life ist ein Tick-Zähler der Simulation); 1 Verwendung Z.126 | GameEngine/Source/GameLogic/Object/Update/TensileFormationUpdate.cpp Z.357: m_life < 100 (Z.260: ++m_life > 300) | Zählt die Simulation Life weiter je Tick, endet das Fenster bei 60 Hz nach 1,67 s statt 3,3 s. | 20,21 |
| 33,268-280 | UnitConditionTracker.TurnOf / TurnTolerance | E | Drehrichtung wird erkannt, wenn die Kursänderung EINES Ticks genau Locomotor.TurnRatePerTick ist (Toleranz 8 Roh-Einheiten Fixed); steuert CENTER_TO_LEFT/RIGHT | eigen (ersetzt PhysicsBehavior::getTurning laut Kommentar) | Stimmt, solange die Simulation je Tick genau die skalierte TurnRatePerTick dreht; mit Teilschritten oder anderer Rundung fällt die Lenkanimation aus. | 21,23 |
| 34,126-128 | UnitConditionTracker.TensileDrop | E | FREEFALL verlangt \|Z - Z des Vortakts\| > 0.2 Welteinheiten: Schwelle gegen den Höhenschritt EINES Ticks | GameEngine/Source/GameLogic/Object/Update/TensileFormationUpdate.cpp Z.357: fabs( pos->z - newPos.z ) > 0.2f | Bei 60 Hz ist der Fallschritt je Tick halb so groß; langsam fallende Felsen zeigen FREEFALL nicht mehr. | 21,23 |
| 230-262 | UnitConditionTracker.StepSteering (NextSteerFrame) | F/tick | Mindesthaltezeit je Lenkzustand: NextSteerFrame = now + AnimationSteeringUpdate.MinTransitionTicks (Tick-Dauer der Simulation), now = world.Tick - 1 | eigen (Port von AnimationSteeringUpdate::update laut Kommentar; nicht nachgeschlagen) | Tick-Uhr, folgt der Tick-Dauer des Moduls: stimmt, sobald MinTransitionTicks skaliert wird. | 21 |

**`Pres\Terrain/WaterSurfaces.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 26 | WaterAppearance.RiverVScrollPerSecond | F/originalbild | Fluss-Scrollen 0.002f * 30f je Sekunde (Originalwert je 30-Hz-Frame mal feste 30); 1 Verwendung Z.187 als UvScrollPerSecond; Verbraucher WaterBatch.ApplyScroll(frame.TotalSeconds) = Wandzeit (TerrainLayer Z.180) | GameEngineDevice/Source/W3DDevice/GameClient/Water/W3DWater.cpp Z.1225: m_riverVOrigin += 0.002f | Wandzeit: unverändert; die 30 ist die Autorenrate und darf nicht durch die neue Tickrate ersetzt werden. | 21 |

### 6.3 `CnCNew.Ui`

**`Ui\Text/GlobalLanguageSettings.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 80,105 | GlobalLanguageSettings.MilitaryCaptionSpeed (captionSpeed = 0) | D | Bildzahl zwischen zwei getippten Zeichen der Militärbeschriftung, roh als Ganzzahl aus Language.ini (Rückfall 0). Verbraucher: Game/Shell/MissionText.MilitaryCaptionState (`_increment = frame + _speed`), getrieben von MissionPresentation.Clock = World.Tick + frozenSteps. | GlobalLanguage.cpp GlobalLanguage::GlobalLanguage: m_militaryCaptionSpeed = 0 | Tippgeschwindigkeit verdoppelt sich mit dem 60-Hz-Tick (Wert bleibt Bildzahl). | 21 |

**`Ui\Text/InGameUiSettings.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 67 | InGameUiSettings.LogicFramesPerSecond | F/tick | Eigene Kopie der Rate `= 30` (nicht SimulationTiming). 4 Verwendungen in der Datei (217, 221, 225, 253) und 4 in src/CnCNew.Game/Shell/MissionText.cs (56, 136, 137, 326). Verbraucher laufen auf Sim-Tick (Match.Tick, MissionPresentation.Clock = World.Tick + frozenSteps). | InGameUI.h DEFAULT_FLOATING_TEXT_TIMEOUT = LOGICFRAMES_PER_SECOND/3; InGameUI.cpp InGameUI::update (m_messageDelayMS / LOGICFRAMES_PER_SECOND / 1000) | Bleibt 30, während der treibende Sim-Tick 60 Hz hat: alle daraus berechneten Bildzahlen (Blinkperiode, Beschriftungsdauer, Sekundenanzeige der Zähler) laufen doppelt so schnell bzw. zeigen doppelte Sekunden. | 21 |
| 216-217 | InGameUiSettings.FromDefinition.Frames (SuperweaponCountdownFlashDuration, NamedTimerCountdownFlashDuration) | F/tick | Eigene Umrechnung ms → Bilder `* LogicFramesPerSecond / 1000m`, ungerundet (decimal), beim Laden des Definitionsgraphen. Verbraucher: Game/Hud/SuperweaponTimers.Draw (tick = Match.Tick, `(int)` abgeschnitten, `tick / flashFrames % 2`) und Game/Shell/MissionText.NamedTimers (frame = MissionPresentation.Clock). | InGameUI.cpp s_fieldParseTable: INI::parseDurationReal für beide Schlüssel | Blinkperiode bleibt als 30er-Bildzahl stehen und wird gegen den 60-Hz-Tick gerechnet: Blinken doppelt so schnell; Rundung: keine hier, Abschneiden beim Verbraucher. | 21 |
| 220-221 | InGameUiSettings.FromDefinition.WholeFrames (FloatingTextTimeOut) | F/tick | Eigene Umrechnung ms → ganze Bilder `Math.Ceiling(ms * LogicFramesPerSecond / 1000m)`, beim Laden. Ergebnis FloatingTextTimeOutFrames hat in src/ keinen Verbraucher (Grep über src/: nur Deklaration). | InGameUI.cpp s_fieldParseTable: INI::parseDurationUnsignedInt für FloatingTextTimeOut; Verbrauch im Original InGameUI.cpp (m_frameTimeOut = TheGameLogic->getFrame() + …) | Derzeit ohne Wirkung (kein Verbraucher); sobald Schwebetext gebaut wird, wäre die Dauer bei 60-Hz-Tick halbiert. Rundung: auf ganze Bilder aufgerundet. | 21 |
| 224-225 | InGameUiSettings.FromDefinition.PerFrame (FloatingTextMoveUpSpeed, FloatingTextVanishRate) | F/tick | Eigene Umrechnung Einheiten/Sekunde → Einheiten/Bild `/ LogicFramesPerSecond`, beim Laden. FloatingTextMoveUpPerFrame und FloatingTextVanishPerFrame haben in src/ keinen Verbraucher. | InGameUI.cpp s_fieldParseTable: INI::parseVelocityReal für beide Schlüssel; Verbrauch im Original InGameUI.cpp (pos.y -= m_frameCount * m_floatingTextMoveUpSpeed; (currLogicFrame - m_frameTimeOut) * m_floatingTextMoveVanishRate) | Derzeit ohne Wirkung; bei späterem Verbrauch pro 60-Hz-Tick doppelte Steig- und Ausblendgeschwindigkeit. | 21 |
| 240 | InGameUiSettings.MilitaryCaptionSpeed: Integer("MilitaryCaptionSpeed", 1) | D | Bildzahl zwischen zwei getippten Zeichen, roh als Ganzzahl gelesen (Rückfall 1). In src/ nicht verbraucht: die Hosts übergeben GlobalLanguageSettings.MilitaryCaptionSpeed (MissionPresentation.cs 921, 988). | InGameUI.cpp s_fieldParseTable (MilitaryCaptionSpeed) und InGameUI::InGameUI: m_militaryCaptionSpeed = 1 | Derzeit ohne Wirkung (nicht verbraucht). | 21 |
| 242 | InGameUiSettings.FromDefinition: Frames("SuperweaponCountdownFlashDuration", 1m) | B/fallback | Rückfallwert 1 Bild (bereits in Bildern, nicht ms) für die Blinkdauer der Superwaffenanzeige; laut Kommentar Zeile 227 setzt die Retail-Datei den Schlüssel, der Rückfall greift dort nicht. | InGameUI.cpp InGameUI::InGameUI: m_superweaponFlashDuration = 1.0f | Rückfall bliebe 1 Tick = 1/60 s statt 1/30 s; keine Rundung. | 21 |
| 247 | InGameUiSettings.FromDefinition: Frames("NamedTimerCountdownFlashDuration", 1m) | B/fallback | Rückfallwert 1 Bild (in Bildern) für die Blinkdauer der benannten Zähler; Retail-Datei setzt den Schlüssel laut Kommentar Zeile 227. | InGameUI.cpp InGameUI::InGameUI: m_namedTimerFlashDuration = 1.0f | Rückfall bliebe 1 Tick = 1/60 s statt 1/30 s; keine Rundung. | 21 |
| 253 | InGameUiSettings.FromDefinition: WholeFrames("FloatingTextTimeOut", LogicFramesPerSecond / 3) | B/fallback | Kopie `/ 3`: Rückfallwert 30/3 = 10 Bilder (ganzzahlig) für die Schwebetext-Dauer; kein Verbraucher in src/. | InGameUI.h DEFAULT_FLOATING_TEXT_TIMEOUT = LOGICFRAMES_PER_SECOND/3; InGameUI.cpp InGameUI::InGameUI: m_floatingTextTimeOut = DEFAULT_FLOATING_TEXT_TIMEOUT | Ohne Wirkung solange kein Verbraucher; sonst 10 Ticks = 1/6 s statt 1/3 s. Ganzzahldivision: bei 60 → 20, bei 120 → 40 (ohne Rest). | 21 |
| 254 | InGameUiSettings.FromDefinition: PerFrame("FloatingTextMoveUpSpeed", 1m) | D | Rückfallwert 1,0 Einheiten je Bild (roh, ohne Umrechnung, anders als der Datenwert, der durch 30 geteilt wird); kein Verbraucher in src/. | InGameUI.cpp InGameUI::InGameUI: m_floatingTextMoveUpSpeed = 1.0f | Ohne Wirkung solange kein Verbraucher; sonst doppelte Steiggeschwindigkeit je Sekunde. | 21 |
| 255 | InGameUiSettings.FromDefinition: PerFrame("FloatingTextVanishRate", 0.1m) | D | Rückfallwert 0,1 je Bild (roh) für die Ausblendrate des Schwebetexts; kein Verbraucher in src/. | InGameUI.cpp InGameUI::InGameUI: m_floatingTextMoveVanishRate = 0.1f | Ohne Wirkung solange kein Verbraucher; sonst doppelt so schnelles Ausblenden. | 21 |

**`Ui\Transitions/TransitionEffects.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 87 | FlashEffect.End | F/originalbild | FLASH dauert 8 Übergangsbilder; dazu Alphatabellen je Bild (BorderAlpha, FillAlpha, Zeilen 88–89) und feste Bildnummern 1–7 im switch. | GameWindowTransitions.h FLASHTRANSITION_* (…FADE_TO_BACKGROUND_4 = 7 gefunden; END-Zeile ohne Zahlenwert nicht gesehen) | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 146 | ButtonFlashEffect.End | F/originalbild | BUTTONFLASH dauert 17 Übergangsbilder; feste Bildnummern 1–7 und 8–10 (Warten) im switch. | GameWindowTransitions.h BUTTONFLASHTRANSITION_* (Werte 0–7 und 11–16 gefunden; END-Zeile nicht gesehen) | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 147 | ButtonFlashEffect.GradeIn1..GradeOut4 | F/originalbild | Bildnummern 11–16 des Verlaufsteils von BUTTONFLASH (zugleich Zeichenzustände). | GameWindowTransitions.h BUTTONFLASHTRANSITION_FADE_TO_GRADE_IN_1 = 11 … FADE_TO_GRADE_OUT_4 | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 293 | FadeEffect.End | F/originalbild | WINFADE dauert 10 Übergangsbilder; Alpha `25 * DrawState` je Bild (Zeile 328). | GameWindowTransitions.h FADETRANSITION_FADE_IN_1..9 (9 gefunden; END-Zeile nicht gesehen) | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 341 | ScaleUpEffect.End | F/originalbild | WINSCALEUP/SCORESCALEUP dauern 6 Übergangsbilder; Größenschritt = Fenstergröße / End je Bild (Zeilen 351–352). | Herkunft unbelegt | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 397 | MainMenuScaleUpEffect.End | F/originalbild | MAINMENUSCALEUP dauert 5 Übergangsbilder; Positions- und Größenschritt = Differenz / End je Bild (Zeilen 415–418). | GameWindowTransitions.h MAINMENUSCALEUPTRANSITION_END = 5 | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 468 | FactionScaleEffect.End (medium ? 3 : 6) | F/originalbild | MAINMENUMEDIUMSCALEUP dauert 3, MAINMENUSMALLSCALEDOWN 6 Übergangsbilder; Schritt = Größendifferenz / End. Kein Mustertreffer (Ausdruck statt const). | GameWindowTransitions.h MAINMENUMEDIUMSCALEUPTRANSITION_END = 3; der Wert 6 der Small-Variante: Herkunft unbelegt | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 543 | ScreenFadeEffect.End | F/originalbild | Kopie `End = 30`: SCREENFADE dauert 30 Übergangsbilder (= 1 s bei 30 Bildern/s); Alpha je Bild `Percent = 1 / (End - 1)` (Zeile 544). Die 30 ist eine Bildzahl des Originals, nicht die Tickrate. | GameWindowTransitions.h SCREENFADETRANSITION_END = 30 | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 572 | FullFadeEffect.End | F/originalbild | FULLFADE dauert 10 Übergangsbilder (5 auf, 5 ab); `Percent = 1 / (End / 2)` (Zeile 573). | GameWindowTransitions.h FULLFADETRANSITION_END = 10 | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 616 | TextOnFrameEffect.End | F/originalbild | TEXTONFRAME dauert 1 Übergangsbild. | GameWindowTransitions.h TEXTONFRAMETRANSITION_END = 1 | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 669 | ReverseSoundEffect.End | F/originalbild | REVERSESOUND dauert 2 Übergangsbilder; Ton GUITransitionFade auf Bild 1. | GameWindowTransitions.h REVERSESOUNDTRANSITION_END = 2 | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 711 | CountUpEffect.End | F/originalbild | Kopie `End = 30`: COUNTUP höchstens 30 Übergangsbilder, ein Zählschritt und ein Ton GUIScoreScreenTick je Bild; End dient zugleich als Schwelle für die Schrittweite 1/100/1000 (Zeilen 731–733). | GameWindowTransitions.h COUNTUPTRANSITION_END = 30 | Keine Änderung durch die Sim-Rate; würde End mitskaliert, änderten sich Schrittweite und Tonanzahl der Punkteanzeige – nicht mit der Tickrate skalieren. | 21 |
| 816 | TypeTextEffect.End | F/originalbild | Kopie `End = 30`: TYPETEXT tippt ein Zeichen und einen Ton GUITypeText je Übergangsbild, höchstens 30 Bilder (FrameLength = min(Textlänge, End)). | GameWindowTransitions.h TEXTTYPETRANSITION_END = 30 | Keine Änderung durch die Sim-Rate; würde End mitskaliert, änderte sich die Zahl der getippten Zeichen je Durchlauf – nicht mit der Tickrate skalieren. | 21 |
| 883 | ControlBarArrowEffect.BeginFade | F/originalbild | CONTROLBARARROW: 16 Übergangsbilder Abwärtsbewegung (`Percent = 1 / BeginFade`, Schritt je Bild), danach Ausblenden. | GameWindowTransitions.h CONTROLBARARROWTRANSITION_BEGIN_FADE = 16 | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |
| 884 | ControlBarArrowEffect.End | F/originalbild | CONTROLBARARROW dauert 22 Übergangsbilder (6 Bilder Ausblenden, `FadePercent = 1 / (End - BeginFade)`). | GameWindowTransitions.h CONTROLBARARROWTRANSITION_END = 22 | Keine Änderung durch die Sim-Rate: läuft über WindowTransitionHandler.FramesPerSecond auf Wandzeit; darf nicht mit der Tickrate skaliert werden. | 21 |

**`Ui\Transitions/WindowTransitionHandler.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 130 | WindowTransitionHandler.FramesPerSecond | F/originalbild | Eigene Kopie `= 30`: Übergangsbilder je Sekunde der Shell-Animationen. 1 Verwendung (Zeile 285 `_carry += seconds * FramesPerSecond`). Uhr: Advance(seconds) ← ShellScreen.Update(min(dt, 0,1 s)) ← GameShell.Update(dtSeconds) ← ClientRuntime.cs 234 `_shell.Update(seconds)` (Client-Bildzeit); UiRenderTool ruft mit festen Schritten (1.0 / 30). | Herkunft unbelegt (Kommentar nennt GameData.ini UseFPSLimit/FramesPerSecondLimit = 30; Datendatei nicht geprüft) | Keine Änderung: hängt nicht am Sim-Tick, sondern an Wandzeit mit fester 1/30-s-Einheit; darf nicht mit der Tickrate skaliert werden (sonst laufen die Menüübergänge doppelt so schnell). | 21 |
| 303 | WindowTransitionHandler.Settle(limit = 600) | F/originalbild | Schleifengrenze 600 Übergangsbilder (= 20 s bei 30 Bildern/s) für das Ausschwingen; in src/ und tools/ kein Aufrufer gefunden (Grep `.Settle(`), vermutlich nur Tests. | eigen | Keine Änderung durch die Sim-Rate (Übergangsbilder, nicht Ticks). | 21 |

**`Ui\Transitions/WindowTransitionTable.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 31,173-176 | TransitionWindowSpec.FrameDelay (Schlüssel FrameDelay in WindowTransitions.ini) | D | Startverzögerung eines Fenstereffekts in Bildern, roh als Ganzzahl aus den Daten, verbraucht in TransitionWindow.Update/TotalFrames gegen den Gruppen-Bildzähler (Übergangsbilder zu 1/30 s Wandzeit). | Herkunft unbelegt (Kommentar nennt GameWindowTransitionsHandler parseWindow; .cpp nicht geöffnet) | Keine Änderung durch die Sim-Rate; Einheit ist das Übergangsbild (1/30 s Wandzeit), nicht der Tick. | 21 |

### 6.4 `CnCNew.Client.Stride`

**`Client\ClientRuntime.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 120,141,142,375,378,379 | ClientRuntime.FinishMission / RunningGame.MissionSteps / _frame | H/ticks | Missionsbericht: Frames, world tick, session steps (zählt TickStepped), presentation latency in Ticks; der Kommentar erwartet steps == world tick; Frames == Ticks gilt nur bei 1 Tick je Frame | eigen | Bei 60 Hz und Schritt 1/30 s ist world tick = 2 × frames und die Latenz wird als doppelte Tickzahl gedruckt; Prüfskripte, die Frames mit Ticks vergleichen, schlagen fehl. | 21 |
| 192 | ClientRuntime.Update: _clockMs += (long)Math.Round(seconds * 1000) | H/schritt | Millisekundenuhr für Eingabeereignisse, Cursoranimation und Minimap-Takt; im Rauchtest 33 ms je Frame (1/30 s gerundet), also 1 % hinter der Spielzeit; sonst Wandzeit | eigen | Unabhängig von der Tickrate; ändert sich nur mit dem Rauchtest-Schritt (1/60 s ergäbe 17 ms, 2 % zu schnell). | 21 |

**`Client\Layers/Fx/FxDemo.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 14,26,30,61,66 | FxDemo.PeriodFrames = 45 | F/tick | Diagnose-Ring (CNCNEW_FX_DEMO=ring): neue Effekte alle 45 Fx-Frames; match.Fx.Frame ist der ParticleWorld-Zähler, der je Simulationstick einmal läuft (LiveGame.SyncAfterTick ruft Fx.Update) | eigen | Tick-getrieben: der Ring erscheint alle 0,75 s statt 1,5 s, solange Fx.Update je Tick läuft. | 21 |
| 103 | FxDemo.Spawn: FxInvocation.PrimarySpeed = 6f | D | Tracer-Geschwindigkeit des Diagnose-Rings als Strecke je Fx-Frame (FxInvocation.PrimarySpeed: "Distance per frame"), Literal 6 | eigen | Tracer des Demo-Rings fliegen doppelt so schnell, falls der Fx-Frame mit der Tickrate läuft. | 21 |

**`Client\Layers/Objects/ObjectLayerCore.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 45,46,57-63,121,122 | ObjectLayerCore.Attach/Update: _frameLimit aus --frames, _frameCounter | H/budget | Die Objektschicht liest --frames ein zweites Mal aus der Kommandozeile und druckt ihre Rauchtest-Zusammenfassung nach so vielen gerenderten Frames | eigen | Unverändert (Frames, nicht Ticks); muss mitziehen, falls --frames künftig Ticks oder Sekunden bedeutet. | 21 |
| 369,372,393 | ObjectLayerCore.ApplyWheels: Number("RotationDamping", 1f) | D | Draw-Modulfeld RotationDamping roh gelesen (Anteil je Originalbild von 1/30 s, Standard 1 = sofort); Verbrauch in Zeile 401 | GameEngineDevice/Source/W3DDevice/GameClient/Drawable/Draw/W3DTruckDraw.cpp, W3DTruckDraw-Feldtabelle "RotationDamping" (INI::parseReal, m_rotationDampingFactor) | Unverändert, weil die Umrechnung die feste 1/30-s-Einheit benutzt; nur falsch, wenn jemand die 30 durch die Tickrate ersetzt. | 21 |
| 400-403 | ObjectLayerCore.ApplyWheels: blend = 1 - Pow(1 - RotationDamping, deltaSeconds * 30.0) | F/originalbild | Kabinen-/Anhängerdämpfung von W3DTruckDraw: Anteil je 1/30 s (Originalbild), auf die Frame-Zeit umgerechnet; Literal 30.0 | GameEngineDevice/Source/W3DDevice/GameClient/Drawable/Draw/W3DTruckDraw.cpp, W3DTruckDraw::doDrawModule (deltaAngle *= m_rotationDampingFactor je gezeichnetem Frame) | Läuft bereits auf Frame-/Wandzeit; die 30 ist die Autoreneinheit und darf NICHT mit der Tickrate skaliert werden. | 21 |

**`Client\Program.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 40,92,365 | Program top-level/CnCNewGame.Update: --frames Standard "90", smokeFrames, LiveOptions.Frames | H/budget | Laufbudget des Rauchtests in gerenderten Frames (Standard 90 = 3 s = 90 Ticks bei 30 Hz); zweimal geparst; Ende bei ++_renderedFrames >= smokeFrames | eigen | Die Spielzeit bleibt (Frames × 1/30 s), die erreichte Tickzahl verdoppelt sich; Aufrufe wie --frames 1500, die eine Tickzahl meinen, treffen andere Ticks. | 21 |
| 93,151,388,389 | Program top-level/CnCNewGame: --every Standard 30, filmstripEvery = 30 | H/zeit | Filmstreifen: ein PNG alle N gerenderten Frames, Standard 30 = 1 s beim Festschritt 1/30 s (zwei Stellen: CLI-Standard "30" und Konstruktor-Standard 30) | eigen | Bleibt 1 s, solange der Schritt 1/30 s bleibt; wird der Schritt an die Tickrate gekoppelt, halbiert sich der Bildabstand. | 21 |
| 121,357 | CnCNewGame.Update: fixedSeconds = smokeTest ? 1.0 / 30 : null | H/schritt | Rauchtest-Festschritt 1.0/30 s je Frame an ClientRuntime.Update (ersetzt die Wandzeit gameTime.Elapsed); bei 30 Hz genau 1 Tick je Frame, weil LiveGame.TickSeconds = 1/30 | eigen | Bleibt der Schritt 1/30 s, laufen 2 Ticks je Frame (exakt, 1.0/60 ist binär die Hälfte von 1.0/30); Frame ist dann nicht mehr Tick, Ticknummern in Berichten und Captures verschieben sich. | 21 |
| 397 | CnCNewGame.Draw: Capture bei _renderedFrames >= liveOptions.Frames ?? (shellMode ? smokeFrames : 12) | H/budget | Zeitpunkt des --capture in gerenderten Frames (Match/Mission: --frames; Shell: smokeFrames; Modellbetrachter: 12) | eigen | Gleicher Spielzeitpunkt, aber anderer Welttick und andere Tickfolge davor; Capture-Prüfsummen (golden) können sich ändern. | 21 |

**`Client\Testing/InputScript.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 18,58,76,165-167 | InputScript.Step.Frame / EventsFor(frame, timeMs) | H/zeit | Skriptformat "FRAME VERB ARGUMENTE": Frames zählen die Updates von ClientRuntime ab 1 (nur im Rauchtest, Festschritt 1/30 s); bei 30 Hz zugleich die Ticknummer | eigen | Zeitpunkte bleiben (Frame × 1/30 s), aber Frame ist nicht mehr Tick; Skripte, deren Frames auf Tickzahlen (Bauzeiten) abgestimmt sind, bleiben nur richtig, solange der Schritt 1/30 s bleibt. | 21 |
| 48,254,261 | InputScript._frame, Log "frame N tick M", Report "at exit tick" | H/ausgabe | Protokoll druckt Frame und Welttick roh (ohne Umrechnung in Sekunden) | eigen | Ticknummern im Protokoll verdoppeln sich; Regressionsprüfungen, die Tickwerte aus dem Protokoll lesen, brauchen neue Erwartungen. | 21 |
| 88 | InputScript.Load: click → up bei frame + 2 | H/zeit | Klick: Loslassen zwei Frames (etwa 66 ms beim Festschritt) nach dem Drücken auf demselben Pixel | eigen | Bleibt 2 Frames; dazwischen liegen dann 4 statt 2 Ticks (Eingabe ist Frame-getaktet, Wirkung nur über die Tickfolge der Befehle). | 21 |

### 6.5 `CnCNew.Data` (Felder, die nur als Zahl typisiert sind)

**`Data\Definitions/DefinitionPrimitives.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 38 | ColorKeyframeValue.Frame (ValueGrammar.ParseColorForm, Zeilen 195-196) | D | Form `R:n G:n B:n <zahl>` wird als Farb-Keyframe mit roher Framezahl (long) typisiert und so in den Definitionssnapshot geschrieben (DefinitionSnapshotWriter Zeile 108); keine Zeiteinheit | Herkunft unbelegt (Presentation nennt ParticleSystemTemplate::parseRGBColorKeyframe; in references nicht nachgeschlagen) | Die Zahl bleibt eine Framezahl der 30-Hz-Autorenschaft; ein Verbraucher muss sie als 1/30-s-Einheit deuten, nicht als Tick. | 21 |

**`Data\Registry/ModuleCatalog.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 865 | HelicopterSlowDeathBehavior: SpiralOrbitForwardSpeedDamping | D | Schema FieldType.Real, rohe Zahl ohne Einheit; im Original parseReal und je Update multiplikativ auf die Vorwärtsgeschwindigkeit (`m_forwardSpeed *= damping`) | HelicopterSlowDeathUpdate.cpp buildFieldParse (INI::parseReal, Zeile 114) und update (Zeile 379) | Faktor je Logikframe: bei 60 Hz würde doppelt so oft gedämpft, wenn die Simulation ihn je Tick anwendet (Verbrauch in CnCNew.Simulation prüfen). | 20,23 |
| 968 | StructureToppleUpdate: StructuralDecay | D | Schema FieldType.Real, rohe Zahl; im Original parseReal und multiplikativ auf die Restfestigkeit (`m_structuralIntegrity *= m_structuralDecay`) | StructureToppleUpdate.cpp buildFieldParse (INI::parseReal, Zeile 125) und Verwendung Zeile 250 | Zerfallsfaktor je Anwendung: häufigere Anwendung bei 60 Hz beschleunigt den Zerfall, falls die Simulation ihn je Tick anwendet (dort prüfen). | 20,23 |
| 1024 | JetSlowDeathBehavior: RollRate (und PitchRate, Zeile 1026) | D | Schema FieldType.Real, rohe Zahl; im Original parseReal (nicht parseAngularVelocityReal), also keine Umrechnung je Sekunde; RollRate wird je Update mit RollRateDelta multipliziert | JetSlowDeathBehavior.cpp buildFieldParse (INI::parseReal, Zeilen 114/116) und `m_rollRate *= m_rollRateDelta` (Zeile 248) | Vermutlich Winkel je Logikframe: bei 60 Hz doppelte Drehrate, wenn je Tick angewandt (Verbrauch in der Simulation prüfen; kein Trefferort, zusätzlich gefunden). | 20,23 |
| 1068 | ParachuteContain: LowAltitudeDamping | D | Schema FieldType.Real, rohe Zahl; im Original parseReal (Standard 0.2) als Dämpfungsfaktor in der Fallschirm-Aktualisierung (Formel dort nicht gelesen) | ParachuteContain.cpp buildFieldParse (INI::parseReal, Zeile 82), Verwendung Zeile 391 | Dämpfung je Logikframe: bei 60 Hz andere Pendeldämpfung, wenn je Tick angewandt (Verbrauch in der Simulation prüfen). | 20,23 |
| 1201 | MobMemberSlavedUpdate: CatchUpCrisisBailTime | D | Schema FieldType.Integer (Min 0), rohe Zahl; im Original parseUnsignedInt (keine Dauer-Umrechnung) und Vergleich mit einem Zähler m_catchUpCrisisTimer (Inkrement nicht gelesen) | MobMemberSlavedUpdate.h buildFieldParse (INI::parseUnsignedInt, Zeile 72); MobMemberSlavedUpdate.cpp Zeilen 259/275 | Vermutlich Zahl von Updates/Frames: bei 60 Hz halbe Echtzeit, wenn als Ticks verglichen (in der Simulation prüfen; zusätzlich gefunden). | 20 |
| 1354 | NeutronMissileUpdate: ForwardDamping | D | Schema FieldType.Real, rohe Zahl; im Original parseReal (Standard 0), als `damping` in der Flugphase gelesen (Formel dort nicht gelesen) | NeutronMissileUpdate.cpp buildFieldParse (INI::parseReal, Zeile 83), Verwendung Zeile 371 | Dämpfung je Logikframe: bei 60 Hz anderes Flugprofil, wenn je Tick angewandt (Verbrauch in der Simulation prüfen). | 20,23 |
| 1587 | StructureCollapseUpdate: CollapseDamping | D | Schema FieldType.Real, rohe Zahl; im Original parseReal und je Update `m_collapseVelocity -= gravity * (1 - damping)` | StructureCollapseUpdate.cpp buildFieldParse (INI::parseReal, Zeile 119) und update (Zeile 214) | Geht in einen Euler-Schritt je Logikframe ein: bei 60 Hz anderer Einsturzverlauf, wenn unverändert je Tick (Verbrauch in der Simulation prüfen). | 20,23 |
| 1589 | StructureCollapseUpdate: BigBurstFrequency | D | Schema FieldType.Integer, rohe Zahl; im Original parseInt und Zufallszug `GameLogicRandomValue(1, N) == 1` in der Aktualisierung (1-aus-N je Prüfung) | StructureCollapseUpdate.cpp buildFieldParse (INI::parseInt, Zeile 121) und Zeile 228 | Wahrscheinlichkeit je Prüfung: häufigere Prüfung bei 60 Hz ändert Zahl und Zeitpunkt der Zufallszüge (in der Simulation prüfen; zusätzlich gefunden). | 20,23 |
| 1628-1630 | RailroadBehavior: RunningGarrisonSpeedMax/KillSpeedMin/SpeedMax | D | Schema FieldType.Real, rohe Zahlen; im Original parseReal (nicht parseVelocityReal), also Geschwindigkeit je Logikframe; Vergleich/Begrenzung von m_pullInfo.speed | RailroadGuideAIUpdate.h RailroadBehaviorModuleData::buildFieldParse (INI::parseReal, Zeilen 66-67); RailroadGuideAIUpdate.cpp Zeilen 293, 347, 375, 753-759 | Strecke je Frame: bei 60 Hz doppelt so schneller Zug und verschobene Schwellen, wenn unverändert je Tick (in der Simulation prüfen; zusätzlich gefunden). | 20 |
| 1631 | RailroadBehavior: Acceleration | D | Schema FieldType.Real, rohe Zahl; im Original parseReal und multiplikativ `speed *= m_acceleration` je Update | RailroadGuideAIUpdate.h buildFieldParse (INI::parseReal, Zeile 68); RailroadGuideAIUpdate.cpp Zeile 752 | Faktor je Logikframe: bei 60 Hz beschleunigt der Zug doppelt so oft, wenn je Tick angewandt (in der Simulation prüfen; zusätzlich gefunden). | 20,23 |
| 1632 | RailroadBehavior: Braking | D | Schema FieldType.Real, rohe Zahl; im Original parseReal und multiplikativ `speed *= m_braking` je Update | RailroadGuideAIUpdate.h buildFieldParse (INI::parseReal, Zeile 69); RailroadGuideAIUpdate.cpp Zeile 709 | Faktor je Logikframe: bei 60 Hz bremst der Zug doppelt so oft, wenn je Tick angewandt (in der Simulation prüfen). | 20,23 |
| 1637 | RailroadBehavior: Friction | D | Schema FieldType.Real, rohe Zahl; im Original parseReal und multiplikativ `speed *= m_friction` je Update | RailroadGuideAIUpdate.h buildFieldParse (INI::parseReal, Zeile 74); RailroadGuideAIUpdate.cpp Zeile 789 | Faktor je Logikframe: bei 60 Hz doppelt so starke Reibung, wenn je Tick angewandt (in der Simulation prüfen). | 20,23 |

**`Data\World/MapScriptCatalog.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 60 | MapScriptCatalog: Aktionstypen UNIT_/TEAM_GUARD_FOR_FRAMECOUNT, UNIT_/TEAM_IDLE_FOR_FRAMECOUNT, TEAM_SPIN_FOR_FRAMECOUNT (Zeile 68) | D | Der Katalog führt Skriptaktionen, deren Parameter rohe Framezahlen aus der Kartendatei sind; Data/Formats reichen die Ganzzahl unverändert durch (kein Trefferort, zusätzlich gefunden; nur die Namenszeilen gelesen) | Herkunft unbelegt (Namen laut Dateikommentar aus Scripts.h extrahiert; dort nicht nachgeschlagen) | Kartendaten in 30-Hz-Frames: bei 60 Hz halbe Dauer, wenn die Skript-VM sie als Ticks zählt (in ScriptEngine prüfen). | 20 |

`CnCNew.Audio`, `CnCNew.W3D`, `Archives`, `Content`, `Formats`, `Images`, `Import`, `IO`: keine gekoppelte Stelle (Anhang A).

## 7. Inventar Werkzeuge und Tests

### 7.1 Werkzeuge (`tools/`)

**`tools/CnCNew.CampaignTool/Program.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 187 | Drive --ticks (Standard 60000) | H/budget | Lauflänge des Zielfahrers: --ticks, Standard 60.000 Ticks (= 33,3 min); MD_CHI04 siegt laut Doku bei Tick ~48.000 | eigen | Budget = 16,7 min Spielzeit: lange Missionen (MD_CHI04) erreichen den Sieg nicht mehr, 24/24 fällt. | 21 |
| 196,488-491 | Drive --timeline N / Timeline: world.Tick % every | H/zeit | Zeitleisten-Intervall in Ticks vom Aufrufer (Standard 0 = aus) | eigen | Gleiches N liefert doppelt so dichte Zeilen pro Spielzeit; keine Ergebnisänderung. | 21 |
| 278 | Audit --ticks (Standard 1800) | H/budget | Lauflänge audit: --ticks, Standard 1800 Ticks (= 60 s); dokumentierter Aufruf nutzt --ticks 900 | eigen | Halbe Spielzeit je Mission: weniger ausgeführte Skripte/Aktionen, Abdeckungszahlen nicht vergleichbar. | 21 |
| 314 | Audit: VerifyContinuation(..., Math.Min(ticks, 900)) | H/budget | Speichern/Laden-Fortsetzung über höchstens 900 Ticks (= 30 s), Speicherpunkt bei der Hälfte (MissionScenario.cs:64) | eigen | Speicherpunkt nach 7,5 s statt 15 s Spielzeit; deckt weniger laufende Skriptzustände ab. | 21 |
| 320-321 | Audit: MissionProbe.Run (settleTicks 150, waitTicks 600) | H/budget | Ausschalt-Endproben mit den Standardwerten settleTicks = 150 (5 s) und waitTicks = 600 (20 s), deklariert in src/CnCNew.Simulation/Testing/MissionScenario.cs:94 | eigen | Skripte mit Zeitverzögerung (Timer in Sekunden) feuern nicht mehr im Wartefenster; Endproben 13/24 und 15/24 können sinken. | 21 |
| 375 | Playthrough --ticks (Standard 900) | H/budget | Lauflänge je Mission im playthrough: --ticks, Standard 900 Ticks (= 30 s) bis zum erzwungenen Ende | eigen | Nur 15 s Spielzeit für das Siegskript nach dem Ausschalten: „no outcome“ wird wahrscheinlicher. | 21 |
| 386 | Playthrough: session.World.Tick < 150 | H/zeit | 150 Ticks (= 5 s) Vorlauf, bevor gespeichert und der Gegner ausgeschaltet wird | eigen | Vorlauf nur 2,5 s: Speicherstand und Ausschalten treffen einen früheren Missionszustand (Intro-Skripte laufen noch). | 21 |
| 517 | WatchEntities: world.Tick % 150 < 2 | H/zeit | Drossel der Diagnosezeile „nearest foes“: nur in 2 von 150 Ticks (alle 5 s) | eigen | Diagnosezeile alle 2,5 s statt 5 s; nur Ausgabe. | 21 |

**`tools/CnCNew.PresentationTool/ScenarioAudit.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 32 | ScenarioAudit.Run --every (Standard 30) | H/zeit | Abtastintervall der gedruckten Tabelle: --every, Standard 30 Ticks (= 1 s); benutzt in live.Tick % every (Z. 60, 61) | eigen | Tabellenzeile alle 0,5 s statt jede Sekunde; nur Ausgabe. | 21 |
| 33 | ScenarioAudit.Run --ticks (Standard scenario.Ticks) | H/budget | Lauflänge: --ticks, Standard DemoScenario.Ticks der gewählten Szene (deklariert in src/CnCNew.Game/Live, anderes Paket) | eigen | Hängt an der Szenendefinition: bleibt deren Tickzahl, läuft die Szene nur die halbe Spielzeit und Prüfungen (Landung, Übernahme) schlagen fehl. | 21 |
| 55 | ScenarioAudit.Run live.Advance(1.0 / 30 + 1e-6) | H/schritt | Fester Schritt 1/30 s (+1e-6) je Schleifendurchlauf, gedacht als genau ein Tick; danach genau eine Probe (history) je Durchlauf | eigen | LiveGame.Advance macht dann zwei Ticks je Aufruf: nur jeder zweite Tick wird abgetastet, alle „in jedem Tick“-Prüfungen (Halteticks, Lücken, Blitz im Folgeframe) zählen falsch. | 21 |
| 111 | Capture: ToFloat(entity.Speed) * 30f | H/ausgabe | Geschwindigkeit pro Tick mit Literal 30 in Einheiten pro Sekunde umgerechnet; speist Tabelle und die Schwellen 40/2/5/55/51/52 pro Sekunde in CheckJet/CheckAirfieldGeometry | eigen | Geschwindigkeiten erscheinen halb so groß: „flying“ wird „hovering“, Mindestgeschwindigkeit 55/s gilt als unterschritten, jet-strafe schlägt fehl. | 21 |
| 207 | CheckDuelTracking: sample.Tick > 5 | H/zeit | Fenster beginnt nach Tick 5, dem Befehlstick der Szene tank-duel (auch im Text Z. 177 und Kommentar Z. 206); der Wert spiegelt die Szenendefinition | eigen | Stimmt nur, solange die Szene den Befehl weiter bei Tick 5 gibt; bei skalierten Szenenticks (10) beginnt das Fenster zu früh. | 21 |
| 216 | CheckDuelTracking: aligned > 5 + 20 | H/zeit | Der Turm muss spätestens 20 Ticks (= 0,67 s) nach dem Befehl bei Tick 5 ausgerichtet sein | eigen | Zeitlimit halbiert sich auf 0,33 s; der Turm braucht in Ticks doppelt so lang, Prüfung schlägt fehl. | 21 |
| 234-241 | CheckDuelTracking: rate = TurnRate * 180/π * 0.5f; expectedRest | H/ticks | Erwarteter Ruhetick = erste Bewegung + ceil(Winkel / (halbe Drehrate pro Tick)) + 2; rechnet mit der Drehrate pro Tick aus dem Template und 2 Ticks Toleranz | eigen | Bleibt stimmig, wenn TurnRate pro Tick skaliert wird; die Toleranz von 2 Ticks ist dann nur 1/30 s. | 21 |
| 238-239 | CheckDuelTracking: death + recenter + 2 / + 3 | H/ticks | Rückdrehen frühestens bei Todestick + RecenterTicks + 2, spätestens + 3: Versatz von 2–3 Ticks aus der Schrittreihenfolge (Probe nach dem Schritt), RecenterTicks kommt aus den Daten | eigen | Der Versatz +2/+3 bleibt in Ticks richtig, wenn jeder Tick abgetastet wird; mit dem 1/30-Schritt aus Z. 55 trifft die Probe das Fenster nicht mehr zuverlässig. | 21 |
| 265 | CheckSentinelScan: series.Length < 600 | H/zeit | Mindestens 600 Proben (= 600 Ticks = 20 s) des Wachpostens verlangt | eigen | 600 Proben sind nur 10 s Spielzeit; zusammen mit Z. 287 zu wenige Scans im Fenster. | 21 |
| 287 | CheckSentinelScan: complete.Length < 4 | H/zeit | Mindestens 4 vollständige Leerlauf-Scans im Lauf; hängt an Lauflänge in Ticks gegen Scanintervall in Spielzeit | eigen | Bei unveränderter Szenenlänge in Ticks nur die halbe Spielzeit: weniger als 4 Scans möglich, Prüfung schlägt fehl. | 21 |
| 291 | CheckSentinelScan: holds < RecenterTicks \|\| > RecenterTicks + 2 | H/ticks | Haltedauer am Scanwinkel in Proben gegen RecenterTicks (aus Daten) mit 2 Ticks Toleranz; setzt eine Probe je Tick voraus | eigen | Mit einer Probe je zwei Ticks (Z. 55) ist die gezählte Haltedauer halb so groß wie RecenterTicks: Prüfung schlägt fehl. | 21 |
| 292-294 | CheckSentinelScan: gaps gegen MinScanIntervalTicks/MaxScanIntervalTicks | H/ticks | Pause zwischen zwei Scans in Proben (Indexdifferenz) gegen das Scanintervall in Ticks aus den Daten | eigen | Gleicher Fehler wie Z. 291, wenn nicht jeder Tick abgetastet wird; sonst stimmig. | 21 |
| 301-303 | CheckSentinelScan: Off(i) / Off(i - 1) | H/ticks | Der gezeichnete Turm darf dem simulierten Winkel dieser oder der vorigen Probe entsprechen („one tick later“ der Präsentation), Toleranz 0,6 Grad | eigen | Bei 60 Hz ist der Winkelschritt je Tick halb so groß (unkritisch); bei zwei Ticks je Probe liegt der gezeichnete Winkel zwischen den Proben und weicht ab. | 21 |
| 392 | CheckCaptureFlash: sample.Tick <= tick + 1 + SelectionFlashTracker.LastVisibleAge | H/ticks | Tönung erlaubt bis Blitztick + 1 + LastVisibleAge (= DecayTicks, Konstante in src/CnCNew.Presentation/Objects/SelectionFlash.cs:34, in Ticks) | eigen | Folgt der Konstante; bleibt DecayTicks eine Tickzahl, ist der Blitz bei 60 Hz halb so lang sichtbar (Sache des Präsentationspakets). | 21 |
| 396 | CheckCaptureFlash: gaps b > a + 1 | H/ticks | Blitzabstände müssen schrumpfen, erlaubt ist 1 Tick Jitter zwischen Nachbarn (Phase wird in ganzen Ticks geschaltet) | eigen | Jitter bleibt 1 Tick, Abstände verdoppeln sich in Ticks; Prüfung bleibt gültig, wenn die Blitzphase mit der Rate skaliert. | 21 |
| 433 | CheckJet: hover > 10 | H/zeit | Jet darf höchstens 10 Ticks (= 0,33 s) reglos in der Luft stehen (Proben mit Speed < 5/s) | eigen | Toleranz nur noch 0,17 s; zusammen mit der falschen Umrechnung in Z. 111 zählt fast jeder Flugtick als Stillstand. | 21 |

**`tools/CnCNew.SimTool/Program.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 88 | UnitAudit --max-ticks (Standard 9000) | H/budget | Lauflänge unit-audit: Option --max-ticks, Standard 9000 Ticks (= 5 min) | eigen | Lauf deckt nur 2,5 min Spielzeit ab; KI kommt weniger weit, Befunde nicht vergleichbar. | 21 |
| 112 | UnitAudit world.Tick % 15 | H/zeit | Abtastung alle 15 Ticks (= 0,5 s); steht auch im Usage-Text (Z. 45), im Doc-Kommentar (Z. 82) und in der Ausgabe (Z. 171) | eigen | Abtastung alle 0,25 s statt 0,5 s; Stichprobenzahlen (samples, inside, stall) verdoppeln sich, Vergleich mit alten Berichten bricht. | 21 |
| 165 | UnitAudit world.Tick - run.Start >= 60 | H/zeit | Meldeschwelle: Einheit mindestens 60 Ticks (= 2 s) in einem Gebäude-Footprint | eigen | Schwelle halbiert sich auf 1 s; mehr Meldungen (z. B. Durchfahrten durch Fabriken). | 21 |
| 199-200 | Options: StressOptions.Standard / --ticks | H/budget | Lauflänge stress und replay-record: Option --ticks, Standard StressOptions.Standard.Ticks = 216.000 (= 2 h bei 30 Hz; deklariert in src/CnCNew.Simulation/Testing/StressScenario.cs:16) | eigen | 216.000 Ticks sind nur noch 1 h Spielzeit; als Schrittzahl-Belastungstest unverändert gültig, die Aussage „2 Stunden“ nicht. | 21 |
| 202 | Options: DrainTicks / CheckpointInterval | H/budget | DrainTicks = min(Standard 6000, Ticks/4); CheckpointInterval = min(1000, Ticks/20) – Ticks-Literale 6000/1000 aus StressOptions.Standard | eigen | Abbauphase und Checkpoint-Abstand halbieren sich in Spielzeit; Ergebnis des synthetischen Tests bleibt gültig, Replaydatei ändert sich nicht. | 21 |
| 293-295 | ReferenceMatch: ReferenceMatchOptions.Standard / --max-ticks / --min-ticks | H/budget | Lauflänge reference-match: --max-ticks Standard 216.000, --min-ticks Standard 0; dazu aus Standard GraceTicks 300, CheckpointInterval 1000, IncomeInterval 300 (ReferenceMatchScenario.cs:28) | eigen | Budget entspricht 1 h statt 2 h; Einkommenstakt und Nachlauf halbieren sich in Spielzeit, Sieger/Tick/Hash ändern sich. | 21 |
| 325 | ReferenceMatch: scenario.VictoryTick / 30 / 60.0 | H/ausgabe | Ausgabe „game minutes“: Siegtick durch Literal 30 (Ganzzahldivision) und 60.0 | eigen | Angezeigte Spielminuten sind doppelt so hoch wie die echte Spielzeit. | 21 |
| 336-339,452-455 | ReferenceMatch/AiMatch: FinalHash, Chain(TickHashes) | H/golden | Ausgabe von Endhash und Kettenhash; diese Werte stehen als Referenzwerte in CLAUDE.md und docs (z. B. Tick 13.500, 0946FB676DEC615C) | eigen | Alle dokumentierten Sieger/Ticks/Hashes werden ungültig und müssen je Rate neu erhoben werden. | 21 |
| 361-362 | AiMatch: SkirmishMatchOptions.Standard / --max-ticks | H/budget | Lauflänge ai-match: --max-ticks, Standard 108.000 (= 1 h; SkirmishMatchScenario.cs:19, dazu GraceTicks 300, CheckpointInterval 1000, SciencePointInterval 2700); dokumentierte Aufrufe nutzen 45000 | eigen | Budget entspricht halber Spielzeit (45000 = 12,5 statt 25 min): mehr Patt-Ergebnisse, Referenztabellen nicht vergleichbar. | 21 |
| 381-385 | AiMatch: --trace N / world.Tick % trace | H/zeit | Trace-Intervall in Ticks vom Aufrufer (kein Standard, 0 = aus); eine Zeile je N Ticks | eigen | Gleiches N liefert doppelt so dichte Zeilen pro Spielzeit; dokumentierte Aufrufe (--trace N) müssen N skalieren. | 21 |
| 441 | AiMatch: scenario.VictoryTick / 30 / 60.0 | H/ausgabe | Ausgabe „game minutes“: Siegtick durch Literal 30 (Ganzzahldivision) und 60.0 | eigen | Angezeigte Spielminuten sind doppelt so hoch wie die echte Spielzeit. | 21 |
| 482 | SkirmishSweep --max-ticks (Standard 3000) | H/budget | Lauflänge skirmish-sweep: --max-ticks, Standard 3000 Ticks (= 100 s) je Karte | eigen | Nur 50 s Spielzeit je Karte; „startet und endet“ bleibt prüfbar, KI handelt weniger. | 21 |
| 518 | SkirmishSweep: GraceTicks = Math.Min(Standard.GraceTicks, maxTicks / 4) | H/budget | Nachlauf nach dem Sieg: min(300, maxTicks/4) Ticks; 300 aus SkirmishMatchOptions.Standard | eigen | Nachlauf 5 s statt 10 s Spielzeit; nur Länge des Mitschnitts. | 21 |
| 563,645-655 | HashReplay/Stress: FinalHash, Chain | H/golden | Stresstest-Endhash und Kettenhash (dokumentiert 56882F763EABEF67 / B8E663493F2C73D5) über die synthetische Fixture | eigen | Hash ändert sich, sobald Fixture-Zeiten oder Lauflänge mit der Rate skalieren; Referenzwert neu erheben. | 21 |
| 577 | MissionRun --ticks (Standard 9000) | H/budget | Lauflänge mission-run: --ticks, Standard 9000 (= 5 min); dokumentierter Aufruf nutzt --ticks 3000; Speicherpunkt bei ticks/2 (Z. 593) | eigen | Halbe Spielzeit: weniger Skripte/Aktionen im Bericht, Ketten-/Endhash ohnehin neu. | 21 |
| 685 | Stress: options.Ticks >= 216_000 && SpawnedTotal < 100_000 | H/budget | Abnahmeschwelle: bei mindestens 216.000 Ticks müssen 100.000 Spawns erreicht sein (koppelt Standardlauflänge an Spawnzahl) | eigen | Bei skalierter Lauflänge (432.000) greift die Prüfung weiter, bei skalierter Spawnrate bleibt sie erfüllt; Literal 216.000 muss zur Standardlänge passen. | 21 |

**`tools/CnCNew.UiRenderTool/HudFeedbackScenes.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 37 | HudFeedbackScenes.Capture.Advance: match.Advance(1.0 / 30) | H/schritt | Fester Schritt 1/30 s für das Live-Match je „frame“; Advance(1) (Z. 49, 53) meint genau einen Tick | eigen | Jeder Aufruf macht zwei Ticks; das Bild zeigt einen späteren Simulationsstand, PNG-Prüfsummen ändern sich. | 21 |
| 38 | HudFeedbackScenes.Capture.Advance: controller.Update(1.0 / 30) | H/schritt | Fester Schritt 1/30 s für den HUD-Controller (Wandzeit in Sekunden, MatchController.Update(deltaSeconds)) | eigen | HUD-Uhr läuft in Sekunden und bleibt richtig; nur das Verhältnis ein Tick je HUD-Schritt gilt nicht mehr. | 21 |
| 64 | HudFeedbackScenes.Capture: Advance(45) | H/zeit | 45 Schritte zu 1/30 s (= 1,5 s) Wartezeit, damit Tooltip und Meldungszeilen stehen | eigen | Bleibt 1,5 s Wandzeit, simuliert aber 90 statt 45 Ticks; Meldungsalter in Logikframes und damit die Bildprüfsumme ändern sich. | 21 |

**`tools/CnCNew.UiRenderTool/MissionTextScenes.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 41-44 | MissionTextScenes.Capture: log.Add(..., 0/0/120); log.Update(1..150) | H/zeit | Meldungszeilen bei Frame 0, 0 und 120 hinzugefügt, 150 Updates in Logikframes (UiMessageLog zählt Frames); Kommentar sagt „ten seconds younger“, 120 Frames sind 4 s | eigen | Zustand hängt an InGameUiSettings.LogicFramesPerSecond und der Zahl der Updates; bei 60 Updates/s wäre dasselbe Bild nach 2,5 s erreicht, PNG nur stabil, wenn die Frames 30-Hz-Originalbilder bleiben. | 21 |
| 48-50 | MissionTextScenes.Capture: MilitaryCaptionState.Begin(..., 5000, 0, ...); caption.Update(1..130) | H/zeit | Militär-Untertitel mit 5000 ms Dauer ab Frame 0, 130 Updates in Logikframes (Kommentar nennt Frame 95) | eigen | Tippfortschritt nach 130 Frames hängt an Frames pro Sekunde in MilitaryCaptionState; ändert sich die Konstante dort, ändert sich das Bild. | 21 |
| 55 | MissionTextScenes.Capture: NamedTimers 30 * (11 * 60 + 55) | H/zeit | Zählerwert in Logikframes: 30 × 715 s = 11:55 Anzeige; NamedTimers teilt durch InGameUiSettings.LogicFramesPerSecond | eigen | Wird der Teiler 60, zeigt das Bild 5:57 statt 11:55 und die Prüfsumme ändert sich. | 21 |
| 56 | MissionTextScenes.Capture: NamedTimers frame 15 * 5 | H/ticks | Frame 75 als Blinkphase des abgelaufenen Zählers (frame / NamedTimerFlashFrames % 2) | eigen | Phase bleibt, solange NamedTimerFlashFrames nicht skaliert wird; sonst anderes Bild (Blinkfarbe an/aus). | 21 |
| 66 | MissionTextScenes.Capture: SuperweaponTimers.Draw(..., 15, ...) | H/ticks | Tick 15 als Blinkphase der Superwaffenliste (tick / flashFrames % 2); die Sekundenwerte 252/47 sind bereits Sekunden | eigen | Phase bleibt, solange die Blinkframes nicht skaliert werden; sonst anderes Bild. | 21 |
| 69 | MissionTextScenes.Capture: CinematicTextState(..., 120) | H/zeit | Ablauf des Kinotexts bei Frame 120 (ExpiresAtFrame = 4 s bei 30 Hz); hier nur gesetzt, nicht abgespielt | eigen | Kein Bildunterschied in dieser Szene; der Wert bedeutet bei 60 Hz 2 s. | 21 |

**`tools/CnCNew.UiRenderTool/ShellFlowScenes.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 63 | ShellFlowScenes.Capture: shell.Update(1.0 / 30) | H/schritt | Ein Shell-Schritt von 1/30 s, damit der Ladebildschirm die Startausgabe liefert; GameShell.Update nimmt Sekunden (weitere Aufrufe mit 0.9/0.5 s in Z. 44, 46, 106) | eigen | Shell läuft in Wandzeit: kein Unterschied durch die Simulationsrate. | 21 |
| 65 | ShellFlowScenes.Capture: MaxTicks = 600 | H/budget | Kurzes Gefecht von 600 Ticks (= 20 s), dessen Statistik den Score-Screen füllt | eigen | Nur 10 s Spielzeit: andere Statistikzahlen im Score-Bild, PNG-Prüfsumme ändert sich. | 21 |

**`tools/CnCNew.UiRenderTool/TransitionScenes.cs`**

| Zeile | Symbol | Kl. | Was | Herkunft | Bei 60 Hz, wenn unverändert | Phase |
|---|---|---|---|---|---|---|
| 21 | TransitionScenes.Frame = 1.0 / 30 | H/schritt | Schrittweite der Shell-Animation: 1/30 s je Bild, laut Kommentar die Bildrate des Originals; 4 Verwendungen (Z. 45, 54, 81 und Film) | eigen | Shell-Übergänge laufen in Wandzeit (GameShell.Update(dtSeconds)); bleibt richtig als Originalbildrate, darf nicht mit der Simulationsrate mitskaliert werden. | 21 |
| 45,81 | TransitionScenes.Settle frame < 600 / guard++ < 400 | H/budget | Schleifengrenzen in Bildern zu 1/30 s: höchstens 600 (20 s) bis die Animation ruht, 400 (13,3 s) bis der Screen wechselt | eigen | Kein Unterschied, solange Frame 1/30 s bleibt. | 21 |
| 60,66,72,74,79,82,92 | TransitionScenes.Film(..., frames) | H/zeit | Aufgenommene Bildnummern der Übergänge in 1/30-s-Bildern (z. B. entry 0,12,24,30,…,58; score 0,1,8,12,20,30,40,55,70); gehen in die Dateinamen fNN ein | eigen | Kein Unterschied, solange Frame 1/30 s bleibt; würde Frame skaliert, zeigen dieselben Nummern frühere Animationsphasen. | 21 |
| 85 | TransitionScenes.Capture: MaxTicks = 900 | H/budget | Kurzes Gefecht von 900 Ticks (= 30 s), dessen Statistik der Score-Screen hochzählt | eigen | Nur 15 s Spielzeit: andere Zahlen im Score-Bild, PNG-Prüfsummen der score-Bilder ändern sich. | 21 |

### 7.2 Tests je Datei (Übersicht für die Liste „umgestellt / bewusst in Ticks / nicht betroffen“ der Phase 21)

| Datei | schritt | zeit | ticks | budget | rate | golden | ausgabe | D | Summe |
|---|---|---|---|---|---|---|---|---|---|
| `CnCNew.Game.Tests/AudioDirectorTests.cs` | 13 | 6 | 5 | 2 | 3 |  | 1 |  | 30 |
| `CnCNew.Game.Tests/CampaignShellTests.cs` |  |  |  | 8 |  |  |  |  | 8 |
| `CnCNew.Game.Tests/CampaignTests.cs` |  | 3 | 1 | 10 |  |  |  |  | 14 |
| `CnCNew.Game.Tests/HudTests.cs` | 1 | 22 |  | 5 |  | 1 |  |  | 29 |
| `CnCNew.Game.Tests/LiveMatchTests.cs` | 1 | 1 |  |  | 1 |  |  |  | 3 |
| `CnCNew.Game.Tests/MissionEffectsTests.cs` | 16 | 11 | 2 |  |  |  |  |  | 29 |
| `CnCNew.Game.Tests/MissionHudTests.cs` | 21 | 1 |  |  | 1 |  |  |  | 23 |
| `CnCNew.Game.Tests/MissionPresentationTests.cs` |  |  | 1 |  | 1 |  |  |  | 2 |
| `CnCNew.Game.Tests/MissionTextTests.cs` | 12 | 10 |  | 1 |  |  |  |  | 23 |
| `CnCNew.Game.Tests/ScenarioTests.cs` | 1 |  |  | 2 |  |  |  |  | 3 |
| `CnCNew.Game.Tests/ShellTests.cs` | 2 |  |  | 1 |  | 1 |  |  | 4 |
| `CnCNew.Game.Tests/ShellTransitionTests.cs` |  |  | 4 | 1 |  |  | 1 |  | 6 |
| `CnCNew.Presentation.Tests/CameraTests.cs` | 16 | 2 |  |  | 1 |  |  |  | 19 |
| `CnCNew.Presentation.Tests/CaptureConditionTests.cs` |  | 2 |  | 1 |  |  |  |  | 3 |
| `CnCNew.Presentation.Tests/CaptureFlashTests.cs` |  | 14 | 1 | 1 |  |  |  |  | 16 |
| `CnCNew.Presentation.Tests/CursorTests.cs` | 8 | 1 |  | 2 |  |  |  |  | 11 |
| `CnCNew.Presentation.Tests/FxTests.cs` |  | 32 | 1 | 9 |  |  |  |  | 42 |
| `CnCNew.Presentation.Tests/InputTests.cs` | 4 | 2 | 2 | 3 | 2 |  |  |  | 13 |
| `CnCNew.Presentation.Tests/JetOrderTests.cs` |  | 2 | 1 | 1 | 1 |  |  |  | 5 |
| `CnCNew.Presentation.Tests/ObjectTests.cs` |  | 3 | 5 | 3 | 1 |  |  |  | 12 |
| `CnCNew.Presentation.Tests/StructureConditionTests.cs` |  | 21 | 7 | 31 |  |  |  |  | 59 |
| `CnCNew.Presentation.Tests/TerrainTests.cs` |  |  |  |  | 1 |  |  |  | 1 |
| `CnCNew.Presentation.Tests/UnitConditionTests.cs` |  | 6 | 3 | 19 |  |  |  |  | 28 |
| `CnCNew.Simulation.Tests/Program.cs` |  |  | 5 |  |  |  |  |  | 5 |
| `CnCNew.Simulation.Tests/Tests.Acquisition.cs` |  | 6 | 1 |  | 2 |  |  |  | 9 |
| `CnCNew.Simulation.Tests/Tests.Air.cs` |  | 3 | 1 | 2 |  |  |  |  | 6 |
| `CnCNew.Simulation.Tests/Tests.AirModelGeometry.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.AirReal.cs` |  | 2 |  | 1 |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.AirWeapons.cs` |  | 7 | 1 |  |  |  |  | 1 | 9 |
| `CnCNew.Simulation.Tests/Tests.Basics.cs` |  |  |  |  | 14 |  |  |  | 14 |
| `CnCNew.Simulation.Tests/Tests.BattlePlan.cs` |  | 1 |  |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.CleanupAndSpy.cs` |  | 3 |  |  |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.Collision.cs` |  | 2 |  |  | 1 |  |  | 2 | 5 |
| `CnCNew.Simulation.Tests/Tests.Combat.cs` |  | 6 |  |  |  |  |  |  | 6 |
| `CnCNew.Simulation.Tests/Tests.Construction.cs` |  | 6 | 2 |  | 1 |  |  |  | 9 |
| `CnCNew.Simulation.Tests/Tests.CratesAndSpecials.cs` |  | 3 |  |  | 1 |  |  | 1 | 5 |
| `CnCNew.Simulation.Tests/Tests.CreationLists.cs` |  | 1 |  |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.DamagedLocomotor.cs` |  | 1 |  |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.FiringTracker.cs` |  | 10 |  |  | 1 |  |  |  | 11 |
| `CnCNew.Simulation.Tests/Tests.Flight.cs` |  | 2 | 1 | 1 |  |  |  |  | 4 |
| `CnCNew.Simulation.Tests/Tests.GoldenMaster.cs` |  | 1 |  |  | 3 |  |  |  | 4 |
| `CnCNew.Simulation.Tests/Tests.JetFlight.cs` |  | 2 |  | 3 | 5 |  |  |  | 10 |
| `CnCNew.Simulation.Tests/Tests.JetFlightReal.cs` |  | 1 |  | 1 | 4 |  |  |  | 6 |
| `CnCNew.Simulation.Tests/Tests.LastBlockers.cs` |  | 5 | 4 | 5 |  |  |  | 1 | 15 |
| `CnCNew.Simulation.Tests/Tests.Navigation.cs` |  | 2 | 1 | 5 | 1 |  |  |  | 9 |
| `CnCNew.Simulation.Tests/Tests.NavigationEscape.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Phase12Modules.cs` |  |  | 1 |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Phase13Actions.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Phase5.cs` |  | 28 | 8 |  | 3 |  |  |  | 39 |
| `CnCNew.Simulation.Tests/Tests.PlayerAgent.cs` |  |  |  | 3 |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.PowerPlant.cs` |  | 1 | 1 |  |  |  |  |  | 2 |
| `CnCNew.Simulation.Tests/Tests.Projectiles.cs` |  | 4 |  | 1 | 6 |  |  |  | 11 |
| `CnCNew.Simulation.Tests/Tests.RealFlight.cs` |  |  | 3 | 1 |  |  |  |  | 4 |
| `CnCNew.Simulation.Tests/Tests.RealMovement.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.ReferenceMatch.cs` |  |  | 1 | 1 |  |  |  |  | 2 |
| `CnCNew.Simulation.Tests/Tests.Repair.cs` |  |  |  |  | 1 |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.RetailModules.cs` |  | 2 | 1 |  |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.Scripting.cs` |  |  |  | 5 |  |  |  |  | 5 |
| `CnCNew.Simulation.Tests/Tests.ScriptingActions12.cs` |  |  | 1 |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.ScriptingPhase11.cs` |  | 1 | 1 | 1 |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.ScriptingPhase12.cs` |  |  | 1 |  |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Skirmish.cs` |  | 1 | 2 |  |  |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.Slaves.cs` |  | 2 |  | 1 | 2 |  |  |  | 5 |
| `CnCNew.Simulation.Tests/Tests.SpecialPower.cs` |  | 11 | 5 | 3 | 1 |  |  |  | 20 |
| `CnCNew.Simulation.Tests/Tests.StagedCapture.cs` |  | 17 | 1 | 11 |  |  |  |  | 29 |
| `CnCNew.Simulation.Tests/Tests.State.cs` |  |  |  | 16 | 2 |  |  |  | 18 |
| `CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs` |  | 35 |  | 29 | 5 |  |  | 9 | 78 |
| `CnCNew.Simulation.Tests/Tests.Supply.cs` |  | 7 |  | 13 | 1 |  |  |  | 21 |
| `CnCNew.Simulation.Tests/Tests.Teams.cs` |  |  |  | 2 | 1 |  |  |  | 3 |
| `CnCNew.Simulation.Tests/Tests.Terrain.cs` |  |  |  |  | 1 |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.TimeFreeze.cs` |  | 1 | 3 | 3 |  |  |  |  | 7 |
| `CnCNew.Simulation.Tests/Tests.Transports.cs` |  | 12 | 1 | 14 | 14 |  |  |  | 41 |
| `CnCNew.Simulation.Tests/Tests.TurretAttack.cs` |  | 15 | 1 | 47 | 2 |  |  |  | 65 |
| `CnCNew.Simulation.Tests/Tests.Turrets.cs` |  | 13 | 2 | 11 | 4 |  |  |  | 30 |
| `CnCNew.Simulation.Tests/Tests.Veterancy.cs` |  |  |  | 1 |  |  |  |  | 1 |
| `CnCNew.Simulation.Tests/Tests.Vision.cs` |  |  | 1 | 1 |  |  |  |  | 2 |
| `CnCNew.Simulation.Tests/Tests.WeaponSlots.cs` |  | 17 | 5 | 9 | 2 |  |  |  | 33 |
| `CnCNew.Simulation.Tests/Tests.WeaponSlotsIntegration.cs` |  | 2 | 1 | 12 |  |  |  |  | 15 |
| `CnCNew.Simulation.Tests/Tests.WeaponSlotsReal.cs` |  |  | 1 | 3 |  |  |  |  | 4 |
| `CnCNew.Simulation.Tests/Tests.World.cs` |  | 4 | 11 | 1 | 1 |  |  |  | 17 |
| `CnCNew.Ui.Tests/TextAndSettingsTests.cs` |  | 2 |  |  |  |  |  |  | 2 |
| `CnCNew.Ui.Tests/TransitionTests.cs` |  |  | 19 |  | 3 |  |  |  | 22 |
| **Summe** | 95 | 378 | 120 | 311 | 94 | 2 | 2 | 14 | 1016 |

Testdateien ohne Zeile hier (37 von 119) haben keine gekoppelte Stelle unter den geprüften Treffern. `zeit` ist in Spielzeit auszudrücken, `schritt` aus der Rate abzuleiten, `ticks` bleibt, `budget` ist als Spielzeit zu rechnen, wo es Spielzeit meint; `rate` hängt an der Entscheidung der Phase 20. Die Übergangstests (`Ui.Tests/TransitionTests`, `Game.Tests/ShellTransitionTests`) rechnen in Originalbildern auf Wandzeit und sind als `ticks` mit diesem Vermerk geführt.

### 7.3 Tests, Einzelstellen

**`tests/CnCNew.Game.Tests/AudioDirectorTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 123,124 | AudioDirectorTests.EvaCatalogParsing (FramesBetweenChecks, FramesToExpire) | H/zeit | 2 | 2 Stellen: erwartet FramesBetweenChecks == 60 für 2000 ms und FramesToExpire == 30 für 1000 ms (Meldung: 'at 30 Hz') | 21 |
| 128 | AudioDirectorTests.EvaCatalogParsing (Standardwerte) | H/zeit | 1 | erwartet die Frame-Standardwerte 900 (30 s) und 150 (5 s) eines EvaEvent ohne eigene Zeiten | 21 |
| 172-178 | AudioDirectorTests.EvaAnnouncerBehaviour (announcer.Update(frame)) | H/ticks | 5 | 5 Stellen: EvaAnnouncer.Update mit den Logikframes 0, 2, 3 – reine Abfolge ('frame 0 never announces', gleiche Frame-Nummer zweimal) | 21 |
| 184 | AudioDirectorTests.EvaAnnouncerBehaviour (solo.Update(2 + 3334 * 30 / 1000 + 2)) | H/rate | 1 | Verfallsframe einer Ansage mit der Literal-Rate 30 gerechnet: 2 + 3334 ms × 30 / 1000 + 2 = 104 | 21 |
| 505 | AudioDirectorTests.SelectionOrdersAndClicks (CommandFeedback Tick 1, DurationFrames 30) | H/zeit | 1 | Testfeedback mit Tick 1 und DurationFrames 30 (1 s Lebensdauer eines Hinweises); der Wert wird in diesem Test nicht ausgewertet | 21 |
| 561,562,605,606,731,732 | AudioDirectorTests.MuteAndSlidersSilence/ScriptControlsTheSound/OfflineRenderIsReproducible (director.Tick, render.Pump) | H/schritt | 6 | 6 Stellen: fester Schritt 1.0/30 an AudioDirector.Tick(null, dt) und OfflineAudioRenderer.Pump(dt); ohne Spielsicht, die Uhr des Directors ist die Summe der dt | 21 |
| 687,689,693,697 | AudioDirectorTests.ScriptControlsTheSound (director.Tick(view, 1.0 / 30), view.Tick = 10..13) | H/schritt | 4 | 4 Stellen: Schritt 1.0/30 mit Testsicht, deren Tick von Hand 10, 11, 12, 13 gesetzt wird (ein Eva-Durchlauf je neuem Tick) | 21 |
| 727,742 | AudioDirectorTests.OfflineRenderIsReproducible (30 Schritte) | H/zeit | 2 | 2 Stellen: 30 Schritte à 1/30 s gelten als eine Sekunde; erwartet 22049 oder 22050 gerenderte Audioframes; UI-Klick in Schritt 3, Auswahl in Schritt 9 | 21 |
| 767 | AudioDirectorTests.RealCaptureScenario (const frames = 900) | H/budget | 1 | Lauflänge 900 Schritte (30 s) des Szenarios capture-building über LiveScenario.Advance/hud.Update/audio.Update; erwartet nur flashes > 0 und gleichen Welt-Hash mit/ohne Ton | 21 |
| 768 | AudioDirectorTests.RealCaptureScenario (const step = 1.0 / 30) | H/schritt | 1 | fester Schritt 1.0/30 für live.Advance, hud.Update, audio.Update, output.Pump (6 Verwendungen) | 21 |
| 821 | AudioDirectorTests.RealDataRun (const frames = 30 * 20) | H/budget | 1 | Lauflänge 600 Schritte, Kommentar '20 s of game time at 30 Hz'; NotifyInsufficientFunds bei frames / 4 | 21 |
| 822,881 | AudioDirectorTests.RealDataRun (const blockFrames = 1470; blockFrames * 90) | H/rate | 2 | 2 Stellen: Audioblock je Schritt = 44100 / 30 = 1470 Abtastframes ('one tick's worth of audio'); WAV-Auszug 90 Blöcke = 3 s | 21 |
| 835,836 | AudioDirectorTests.RealDataRun (live.Advance(1.0 / 30), director.Update(live, 1.0 / 30)) | H/schritt | 2 | 2 Stellen: fester Schritt 1.0/30 für LiveMatch.Advance und AudioDirector.Update; zwei Läufe müssen byteidentischen Ton liefern (Hash wird nur verglichen, nicht gepinnt) | 21 |
| 871 | AudioDirectorTests.RealDataRun (Konsolenzeile frames / 30.0) | H/ausgabe | 1 | Ausgabe rechnet Schritte mit / 30.0 in Sekunden Spielzeit und den Echtzeitfaktor | 21 |

**`tests/CnCNew.Game.Tests/CampaignShellTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 163,165,180,182,193,195,225,247 | CampaignShellTests.StepUntilOver; SaveLoadThroughShellContinuesIdentically | H/budget | 8 | 8 Stellen: Lauflängen in Ticks über Session.Step(): 5 vor dem Zerstören, 200 bis das Sieg-/Niederlageskript die Mission beendet, 40 bis zum Speichern, 120 Ticks Kettenhash-Vergleich | 21 |

**`tests/CnCNew.Game.Tests/CampaignTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 205,208,213,218,225,227,240,250,269,306 | CampaignTests.ControllerFlow; SaveLoadContinues; SaveRejection; StoreBehaves (Step(run, n)) | H/budget | 10 | 10 Stellen: Lauflängen 3, 5, 10, 40 Ticks und 200 Ticks Kettenhash-Vergleich über Session.Step() | 21 |
| 221,222,228 | CampaignTests.ControllerFlow (presentationLatencyTicks: 90; Step(last, 200)) | H/zeit | 3 | 3 Stellen: Präsentationslatenz 90 Ticks (3 s) wird durchgereicht und geprüft; 200 Ticks 'end-game delay' bis IsOver | 21 |
| 241-243 | CampaignTests.SaveLoadContinues (header.Tick == 40) | H/ticks | 1 | der Spielstand-Kopf trägt den Welt-Tick 40 nach 40 Schritten (Titel 'Mission A, tick 40') | 21 |

**`tests/CnCNew.Game.Tests/HudTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 128,384 | HudTests.CursorAndMarkers; ScriptedSession (Advance 30) | H/budget | 2 | 2 Stellen: 30 Frames Vorlauf bzw. Wartezeit, bis die Einheit sich bewegt hat (Abstand > 1) | 21 |
| 164,175,863 | HudTests.CursorAndMarkers; MoneyAndPowerTooltips (Advance 45, 20, 45) | H/zeit | 3 | 3 Stellen: 45 Frames, weil Befehlshinweise 40 Frames leben; 20 Frames für Alter 0,4–0,7; 45 Frames Tooltip-Wartezeit – alles auf der UI-Uhr _clockMs (Summe der dt) | 21 |
| 208,234 | HudTests.OverlayIsDrawn (Advance 120; Advance 4 * 30) | H/zeit | 2 | 2 Stellen: 120 Frames, weil Drawable::drawHealing die ersten drei Sekunden ignoriert; 4 × 30 Frames, bis das Heil-Icon drei Sekunden nach der letzten Heilung weg ist (FrameProjector.HealingIconTicks = 3 × 30) | 21 |
| 285 | HudTests.Advance (dt = 1.0 / 30) | H/schritt | 1 | Helfer Advance(match, controller, frames, dt = 1.0/30): je Frame LiveMatch.Advance(dt) und MatchController.Update(dt); über 60 Aufrufe, nie mit anderem dt | 21 |
| 350,369,454 | HudTests.ScriptedSession; ReplayMatches (Advance 60, Schleife 4000, Advance 90) | H/budget | 3 | 3 Stellen: Wartefenster 60 Frames bis die Baustelle existiert, höchstens 4000 Frames bis die Einheit fertig ist, 90 Frames Fahrt | 21 |
| 594,781 | HudTests.CommandButtonTooltips; RequirementTooltip (Advance 30) | H/zeit | 2 | 2 Stellen: 30 Frames = 990 ms auf der UI-Uhr, bis der Tooltip nach der Mouse.ini-Wartezeit erscheint | 21 |
| 620,641,642,646,647,648 | HudTests.MessageList (CommandFeedback(..., match.Tick, 30, ...)) | H/zeit | 6 | 6 Stellen: Testfeedback mit Tick = match.Tick und DurationFrames 30; die Meldungsliste wertet die Dauer nicht aus | 21 |
| 673,674,675,681,685,690 | HudTests.MessageList (Event(..., tick: match.Tick + 200/400/700)) | H/zeit | 6 | 6 Stellen: Ereignis-Ticks +200, +400, +700 bilden die Zehn-Sekunden-Sperre der Angriffsmeldung ab (+200 und +400 noch gesperrt, +700 'ten seconds later') | 21 |
| 709,711,712 | HudTests.MessageList (Advance 120, Advance 200, '277 logic frames') | H/zeit | 3 | 3 Stellen: nach 120 Frames beginnen die Zeilen zu verblassen, nach weiteren 200 sind sie weg; Meldung nennt 277 Logikframes (ca. 9,2 s); UiMessageLog.Update(Match.Tick) je Tick | 21 |
| 876-893 | HudTests.GoldenDrawListHash (baselines/hud-drawlist-1280x720.sha256) | H/golden | 1 | gepinnter SHA-256 des HUD-Draw-List-Dumps nach Advance(match, controller, 1) mit dt 1/30 in einem frischen Alpine-Assault-Gefecht; Inhalt siehe done-Datei | 21 |

**`tests/CnCNew.Game.Tests/LiveMatchTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 39 | LiveMatchTests.Run (live.Advance(1.0)) | H/zeit | 1 | ein Advance über eine ganze Sekunde, danach müssen Drawables da sein ('the base shows up within a second'); LiveGame begrenzt die Ticks je Aufruf (MaxTicksPerFrame) | 21 |
| 43 | LiveMatchTests.Run (30 * 20 Frames, live.Advance(1.0 / 30)) | H/schritt | 1 | 600 Schritte mit festem 1.0/30 (20 s) | 21 |
| 45 | LiveMatchTests.Run (live.Tick >= 590) | H/rate | 1 | erwartet nach 600 Schritten mindestens 590 Ticks: 'simulation advanced at 30 Hz' | 21 |

**`tests/CnCNew.Game.Tests/MissionEffectsTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 225 | MissionEffectsTests.PresentationClockRunsWhileTimeIsFrozen (PresentationLatencyTicks = 1_000_000) | H/ticks | 1 | Wächterwert: Präsentationslatenz in Welt-Ticks so groß, dass sie nie abläuft | 21 |
| 234,236 | MissionEffectsTests.PresentationClockRunsWhileTimeIsFrozen (steps 30..36; director.Clock >= frozenAt + 30) | H/zeit | 2 | 2 Stellen: ein Kameraschwenk von 1 s (MOVE_CAMERA_TO Real(1)) soll 30 bis 36 Director-Schritte dauern und die Präsentationsuhr um mindestens 30 weiterlaufen | 21 |
| 246 | MissionEffectsTests.PresentationClockRunsWhileTimeIsFrozen (15 Schritte, CAMERA_FADE_ADD 10/10/10) | H/zeit | 1 | nach 15 Director-Schritten soll die Blende (je 10 Frames an/halten/ab) im Halten stehen | 21 |
| 263,266,293,297,350 | MissionEffectsTests.ScreenShakeShakesTheCamera; BorderSwitchConfinesTheCamera; ScreenFiltersFollowTheOriginalFrameCounts (ApplyCameraTo(camera, 1.0 / 30), camera.Update(1f / 30)) | H/schritt | 5 | 5 Stellen: fester Schritt 1/30 an MissionPresentationDirector.ApplyCameraTo und RtsCamera.Update | 21 |
| 274 | MissionEffectsTests.ScreenShakeShakesTheCamera (Abklingen 0,75 je Logikframe) | H/zeit | 1 | erwartet fallende Erschütterung über 8 Kamera-Updates à 1/30 s und Offsets[7] < 0,6 bei Stärke 3 ('decays by 0.75 per logic frame') | 21 |
| 322-337 | MissionEffectsTests.ScreenFiltersFollowTheOriginalFrameCounts (CAMERA_BW_MODE_BEGIN/END Int(10)) | H/zeit | 1 | Graustufen-Rampe über 10 Frames: je Director-Schritt +0,1 (levels[0] 0,1; [4] 0,5; [9] 1), Ende nach 12 Schritten | 21 |
| 339,354,355 | MissionEffectsTests.ScreenFiltersFollowTheOriginalFrameCounts (Bewegungsunschärfe +5/-5 je Frame) | H/zeit | 3 | 3 Stellen: Unschärfezähler steigt je Director-Schritt um 5 bis 60 (12 Frames), fällt 11 Frames, Schnitt in Schritt 11 | 21 |
| 394,424,435,439,442,453,480,485,489,496,533 | MissionEffectsTests.RealIntro/RealCommandBar/RealScienceAndSelection/RealColorsAndFlashes/RealSuperweaponList (controller.Update(1.0 / 30)) | H/schritt | 11 | 11 Stellen: MissionController.Update mit festem 1.0/30; Schleifenlängen 3900 (Intro MD_USA01), 120, 60, 60, 45, 60 Frames als Vorlauf | 21 |
| 400,405 | MissionEffectsTests.RealIntro (clockLead = director.Clock - live.Tick) | H/ticks | 1 | vergleicht die Präsentationsuhr des Directors direkt mit dem Welt-Tick (Vorsprung > 0 während FREEZE_TIME) | 21 |
| 494,497-500 | MissionEffectsTests.RealColorsAndFlashes (45 Frames, Flash 1 s) | H/zeit | 1 | eine Sekunde Blinken (Flash(target, color, 1)) soll in 45 Updates genau zwei Blinker zeigen und danach erloschen sein | 21 |
| 516,517 | MissionEffectsTests.SuperweaponDrawRules (Draw(..., tick 14/15/30)) | H/zeit | 2 | 2 Stellen: Farbwechsel des fertigen Countdowns bei Tick 15 und zurück bei 30 ('every 15 logic frames', SuperweaponCountdownFlashDuration 500 ms) | 21 |

**`tests/CnCNew.Game.Tests/MissionHudTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 37,40,48,69,72,83,89,91,99,114,116,120,135,140,158,162,170,173,192,194,195 | MissionHudTests.Run (controller.Update(1.0 / 30)) | H/schritt | 21 | 21 Stellen: MissionController.Update mit festem 1.0/30; Schleifen 60, 10, 20, 30, 90 Frames und Abbruchgrenzen 1500, 2400, 30 * 20 Frames | 21 |
| 38 | MissionHudTests.Run (live.Tick >= 55) | H/rate | 1 | erwartet nach 60 Updates mindestens 55 Ticks: 'the mission advanced at 30 Hz' | 21 |
| 164 | MissionHudTests.Run (live.Tick is > 600 and < 1200) | H/zeit | 1 | das Intro von GC_ChemGeneral soll 'about 25 seconds' dauern: Ende zwischen Tick 600 und 1200 | 21 |

**`tests/CnCNew.Game.Tests/MissionPresentationTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 99 | MissionPresentationTests.CameraMoveCompletesOnItsRealDuration (PresentationLatencyTicks = 1_000_000) | H/ticks | 1 | Wächterwert in Welt-Ticks, der nie abläuft | 21 |
| 119 | MissionPresentationTests.CameraMoveCompletesOnItsRealDuration (completionTick) | H/rate | 1 | erwarteter Abschlusstick = fireTick + round(5 s × SimulationTiming.LogicFramesPerSecond): benutzt die Konstante statt eines Literals | 21 |

**`tests/CnCNew.Game.Tests/MissionTextTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 105-117,122 | MissionTextTests.MessageLines (UiMessageLog.Update(frame)) | H/zeit | 1 | Verblassen der Meldungszeilen in Frames: Alpha 255 bis Alter 99, 254 bei 100, 153 bei 200, weg bei 277 ('about 9.2 seconds'); Kommentar rechnet MessageDelayMS 75000 / 30 / 1000 = 0 | 21 |
| 154-175 | MissionTextTests.LetterboxFollowsTheOriginal (15/15/40/15/15 Schritte) | H/zeit | 1 | Letterbox-Blende 1000 ms: nach 15 Director-Schritten Stufe 0,5, nach 30 Stufe 1, beim Schließen genauso | 21 |
| 186-198 | MissionTextTests.TypingClick (Schritte 22, 23, 24..100, 101..260) | H/zeit | 1 | erster Tippklick in Schritt 23 (750 ms Startverzögerung), dann jeder zweite Schritt (23, 25, 27, 29, 31) | 21 |
| 212,216-234 | MissionTextTests.MilitaryCaptionTimeline (caption.Update(frame)) | H/zeit | 1 | Zeitlinie in Frames: Beginn 100, Lebensdauer 150 (5000 ms), erstes Zeichen bei 123, Zeilenumbruch wartet bis 150, Cursor wechselt alle 10 Frames (110/120/130) | 21 |
| 240,249,250 | MissionTextTests.MilitaryCaptionFadeAndRefusals (LastFrame == 40; 75/76 Frames) | H/zeit | 3 | 3 Stellen: '1000 ms are 30 frames' (Beginn 10, LastFrame 40); danach Alpha 3 nach 75 Frames, entfernt im 76. | 21 |
| 281-283,295-324 | MissionTextTests (Step-Schleifen 150; 22/23/62/150/200/240) | H/zeit | 1 | Director-Schritte als Zeit: Zeilen beginnen nach 150 Schritten zu verblassen; Titel tippt ab Schritt 23, Dauer 150 Schritte, danach Ausblenden bis 240 | 21 |
| 355-358 | MissionTextTests (director.Clock == 16 / == 31, Blinken alle 15 Frames) | H/zeit | 1 | der fertige Zähler zeigt bei Uhr 16 die Blinkfarbe und bei 31 wieder die eigene (NamedTimerCountdownFlashDuration 500 ms = 15 Frames) | 21 |
| 386-388,438,459 | MissionTextTests.CinematicText; Popup; SpeechSubtitles | H/zeit | 1 | '4 seconds are 120 frames' (noch sichtbar bei Schritt 119, weg bei 120); Popup über 300 Schritte; Untertitel 8000 ms = 240 Frames | 21 |
| 467 | MissionTextTests.ScalingFollowsTheScreen (30 Schritte) | H/budget | 1 | 30 Director-Schritte Vorlauf, bis mindestens ein Zeichen der Einblendung getippt ist (Startverzögerung 22 Frames) | 21 |
| 523,536,545,561,578,591,593,653,658,661,674,681 | MissionTextTests.RealCaption/RealCountdown/RealTypingClick/RealSharedMessageList/RealPopupInTheMission (controller.Update(1.0 / 30)) | H/schritt | 12 | 12 Stellen: MissionController.Update mit festem 1.0/30; Schleifen 190 (Einblendung fertig getippt), 120, 2100 (Countdown MD_CHI02 sichtbar), 30, 150 + 200 (Zeile verblasst und verschwindet) | 21 |

**`tests/CnCNew.Game.Tests/ScenarioTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 40 | ScenarioTests.Run/Play (live.Advance(1.0 / 30 + 1e-6)) | H/schritt | 1 | fester Schritt 1.0/30 plus 1e-6, bis live.Tick >= ticks; der Zuschlag soll genau einen Tick je Aufruf sichern | 21 |
| 44,45 | ScenarioTests.Run (Play(Math.Min(scenario.Ticks, 1300))) | H/budget | 2 | 2 Stellen: Lauflänge = Tickzahl des Szenarios, höchstens 1300 Ticks; danach werden gezählte Ereignisse des Szenarios erwartet | 21 |

**`tests/CnCNew.Game.Tests/ShellTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 99,428 | ShellTests.WalkScreens; LocalizedSetupText (shell.Update(1.0 / 30)) | H/schritt | 2 | 2 Stellen: ein GameShell.Update mit 1.0/30 nach dem Ladebildschirm ('exactly one StartMatchOutput after one drawn frame'); Shell-Uhr ist Wandzeit | 21 |
| 107 | ShellTests.WalkScreens (MaxTicks = 600) | H/budget | 1 | Gefecht wird über live.Session.Step() bis MaxTicks = 600 gerechnet; das Ergebnis füllt den Punktebildschirm, dessen Draw-List-Hash gepinnt ist (ScoreScreen@...) | 21 |
| 137-152 | ShellTests.Capture (baselines/screens.txt) | H/golden | 1 | 54 gepinnte SHA-256 (9 Bildschirme × 6 Auflösungen) von UiDrawList.Dump ohne Übergänge; nur ScoreScreen hängt an einem Simulationslauf (600 Ticks), alle anderen entstehen ohne Zeitfortschritt | 21 |

**`tests/CnCNew.Game.Tests/ShellTransitionTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 22,23 | ShellTransitionTests.Frame, WindowFrames = 30 | H/ticks | 1 | Schritt 1.0 / WindowFrames mit WindowFrames = 30: Bildtakt der Fensterübergänge (GameWindowTransitions) auf der Shell-Uhr – Originalbild, nicht Simulationstick; über 20 Verwendungen (Step, Settle, shell.Update(Frame)) | 21 |
| 161,463,464 | ShellTransitionTests.MainMenuEntry; ScoreScreenCountsUp (frames 45..120; 40..200; shown[40]) | H/ticks | 3 | 3 Stellen: Dauer des Menüeinstiegs (29 + 27 Bilder) und der Punktezeilen (letzte Spalte ab Bild 48) in Übergangsbildern – Originalbild, nicht Simulationstick | 21 |
| 169 | ShellTransitionTests.MainMenuEntry (Konsolenzeile frames / 30.0) | H/ausgabe | 1 | Ausgabe rechnet Übergangsbilder mit / 30.0 in Sekunden – Originalbild, nicht Simulationstick | 21 |
| 436 | ShellTransitionTests.ScoreScreenCountsUp (MaxTicks = 600) | H/budget | 1 | Gefecht bis MaxTicks = 600 über live.Session.Step(); verglichen wird nur animierter gegen übergangsfreien Bildschirm desselben Laufs | 21 |

**`tests/CnCNew.Presentation.Tests/CameraTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 133,142,146,148,152,153,155,156 | CameraTests.Scrolling (dtSeconds 1f/30f) | H/schritt | 8 | 8 Stellen: dtSeconds = 1f/30f (bzw. Vektor / 30f) als „ein Bild“ in KeyboardScrollDelta, EdgeScrollDelta, RightMouseScrollDelta, ScrollByIntent; Z. 157 (kein Treffer) 0.1f = 3 Bilder ergibt 960 | 21 |
| 164 | CameraTests.RotationPitchZoom (RotateByKeyboard) | H/rate | 1 | 1 Stelle: RotateByKeyboard(1, 1f) erwartet 3 rad = 0,1 rad je Bild mal 30 Bilder je Sekunde | 21 |
| 195,198,204,213,215,217,220,223 | CameraTests.TerrainFollowing, CameraTests.Shake (camera.Update 1f/30f) | H/schritt | 8 | 8 Stellen: camera.Update(1f / 30f, …) als ein Bild; Schleifen mit 120 bzw. 40 solchen Schritten bis zum Einschwingen | 21 |
| 197,216 | CameraTests.TerrainFollowing, CameraTests.Shake (Erwartung je Bild) | H/zeit | 2 | 2 Stellen: Erwartungen je Schritt von 1/30 s: Zoom nähert sich um 30 % dem Zielwert, Shake wird mit 0,75 gedämpft und kehrt die Richtung um | 21 |

**`tests/CnCNew.Presentation.Tests/CaptureConditionTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 117 | CaptureConditionTests.Stages (Schleife 200) | H/budget | 1 | 1 Stelle (kein Treffer): Laufbudget 200 Ticks für die ganze Übernahme (54 Ticks plus Anlauf) | 21 |
| 128,129 | CaptureConditionTests.Stages (Längen 15/30/9) | H/zeit | 2 | 2 Stellen: Phasenlängen 15, 30 und 9 Ticks für UnpackTime 500 ms, PreparationTime 1000 ms, PackTime 300 ms | 21 |

**`tests/CnCNew.Presentation.Tests/CaptureFlashTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 17,80,82,84,154,155,160,182,200,202 | CaptureFlashTests.Envelope, Capture, Restart, ScriptedFlash (Hüllkurve) | H/zeit | 10 | 10 Stellen: Hüllkurve des Blinkens in Tick-Altern (zwei Ticks Spitze, dann 3/4, 1/2, 1/4, ab Alter 5 aus; LastVisibleAge), geprüft über TintAt bei festen Ticks (9, 10, 13, 15, 60 bis 65) | 21 |
| 85,164,181 | CaptureFlashTests.ScriptedFlash (Intervall 15) | H/zeit | 3 | 3 Stellen: Skriptblinken beginnt auf jedem durch 15 teilbaren Logikbild, Anzahl = Sekunden * 30 / 15; erwartet Starts bei 15, 30, 45, 60 für 2 s (Z. 178/179: Ticks 46 und 61) | 21 |
| 141 | CaptureFlashTests.Capture (vier Blinker) | H/zeit | 1 | 1 Stelle (kein Treffer): „a preparation of 60 ticks flashes four times“ – PreparationTime 2000 ms, erwartet genau vier CaptureFlash-Ereignisse; Z. 127 Laufbudget 200 Ticks; Z. 138 prüft, dass die Farbe mit dem Tick und nicht mit dem Bild wechselt | 21 |
| 172 | CaptureFlashTests.ScriptedFlash (Schleife 100) | H/budget | 1 | 1 Stelle: Schleife über die Ticks 1 bis 100, die den Tracker vorrückt | 21 |
| 174 | CaptureFlashTests.ScriptedFlash (tick - 1) | H/ticks | 1 | 1 Stelle: Vergleich mit dem Farbwert des vorigen Ticks, um den Starttick eines Blinkers zu finden | 21 |

**`tests/CnCNew.Presentation.Tests/CursorTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 281,284,289,292,296,300,304,307 | CursorTests.CursorByContext (UpdateCamera 1f/30) | H/schritt | 8 | 8 Stellen: input.UpdateCamera(view, 1f / 30) als ein Bild; geprüft wird nur Cursorart und Scrollrichtung, nicht der Betrag | 21 |
| 348,372 | CursorTests.WaypointMarkers (Schleifen 400) | H/budget | 2 | 2 Stellen (keine Treffer): je höchstens 400 Ticks, bis die Einheit die Station erreicht | 21 |
| 404 | CursorTests.OrderHints (LifetimeMilliseconds 1333) | H/zeit | 1 | 1 Stelle: Bewegungsmarke lebt 40 Bilder bei 30 je Sekunde = 1333 ms (auch Z. 403, 405, 409; DurationFrames 40 in Z. 417 bis 421) | 21 |

**`tests/CnCNew.Presentation.Tests/FxTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 63,64,67,68,76,77,91,92,96,98,99,113,116,130,131,134,151,154,164,167,178,181 | FxTests.ParticleIni (Lifetime, SystemLifetime, BurstDelay, InitialDelay, Keyframes, Damping) | H/zeit | 22 | 22 Stellen: Partikeldaten der Fixture in Originalbildern (Lifetime, SystemLifetime, BurstDelay, InitialDelay, Keyframe-Bild 30, Dämpfung je Bild) | 21 |
| 393,416 | FxTests.SyntheticParse (tracer.Speed, ThrottleFrames) | H/zeit | 2 | 2 Stellen: Tracer-Speed 300 je Sekunde wird 10 je Bild, ThrottleTime 1000 ms wird ThrottleFrames 30; ebenso Z. 389 DecreaseFrames 3 (100 ms) und Z. 412 WeaponSpeed 10 (keine Treffer) | 21 |
| 461,482,487 | FxTests.KeyframesHandComputed | H/zeit | 3 | 3 Stellen: von Hand gerechnete Bildfolge: Alpha 0,1 je Bild bis Keyframe 10, Geschwindigkeit und SizeRate mal 0,5 je Update, Tod bei Bild 20 (21 Updates) | 21 |
| 513,527,550,571,674,695,737 | FxTests (Auslaufschleifen) | H/budget | 7 | 7 Stellen (keine Treffer): Schleifen mit 200, 60, 40, 5, 20, 80 und 40 Schritten, bis Systeme, Tracer, Lichter oder Shake erloschen sind | 21 |
| 611,613 | FxTests.ParticleDeterminism (frame % 10) | H/budget | 2 | 2 Stellen: Testrhythmus, alle 10 von 150 Bildern werden FX ausgelöst; verglichen werden zwei Läufe byteweise, kein festgeschriebener Hash | 21 |
| 673 | FxTests.ExecutorNuggets (Tracer, LightPulse) | H/zeit | 1 | 1 Stelle: Tracer wandert 10 je Bild (Kopf bei 40 nach vier Updates); dazu Z. 658 ExpireFrame 9 und Z. 670 Lichtfaktor 2/3 nach einem von drei Bildern (keine Treffer) | 21 |
| 712 | FxTests.ExecutorEvents (tick += 31) | H/zeit | 1 | 1 Stelle (kein Treffer): 31 Ticks später ist das Drosselfenster von ThrottleFrames 30 (1000 ms) vorbei | 21 |
| 735,736 | FxTests.CameraShakeSemantics (shake.Update) | H/zeit | 2 | 2 Stellen: Intensität fällt je Update auf 0,75 (1,25 dann 0,9375), Richtung kippt je Bild; Z. 737 vierzig Updates bis zum Stillstand | 21 |
| 804-808 | FxTests.RateLimiter (WindowTicks 10) | H/zeit | 1 | 1 Stelle (kein Treffer): Fenster von 10 Ticks je Ereignisname, Hinweise bei Tick 1, 10 und 11 | 21 |
| 812 | FxTests.RateLimiter (MaxPerTick) | H/ticks | 1 | 1 Stelle: höchstens 5 Hinweise im selben Tick (DroppedByTickCap) | 21 |

**`tests/CnCNew.Presentation.Tests/InputTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 585 | InputTests.Arena.Step(int ticks) | H/ticks | 1 | 1 Stelle: Hilfsmethode, die die Welt um N Ticks vorrückt (Aufrufe mit 1, 2, 3, 5) | 21 |
| 597 | InputTests.Arena.Replay (ReplayHeader) | H/rate | 1 | 1 Stelle: ReplayHeader wird mit SimulationTiming.LogicFramesPerSecond gebaut; der Lauf wird in Z. 1347 gegen den Live-Hash verglichen (kein festgeschriebener Hash) | 21 |
| 947 | InputTests.ContextOrders (DurationFrames 40) | H/zeit | 1 | 1 Stelle (kein Treffer): Bewegungsmarke mit DurationFrames 40 (Originalbilder, 1333 ms) | 21 |
| 1011,1035,1055 | InputTests.StandingOrderBehaviour (Schleifen 150/120/300) | H/budget | 3 | 3 Stellen (keine Treffer): Laufbudgets in Ticks, bis das Ziel stirbt, die Einheit feuert oder den Wegpunkt erreicht | 21 |
| 1170 | InputTests.CommandBarStates (BuildTicks 60) | H/zeit | 1 | 1 Stelle (kein Treffer): plantButton.BuildTicks == 60, „2 s = 60 ticks“ (BuildTime 2) | 21 |
| 1230,1254,1259,1261 | InputTests.CameraIntents (camera.Update 1f/30) | H/schritt | 4 | 4 Stellen: CameraInputController.Update(…, 1f / 30) als ein Bild | 21 |
| 1255 | InputTests.CameraIntents (RotateDelta 0.1) | H/rate | 1 | 1 Stelle: „rotate-left turns 0.1 rad per frame“ – RotateDelta 0,1 bei dt = 1/30 s setzt 30 Bilder je Sekunde voraus | 21 |
| 1335-1339 | InputTests.AcceptanceSequence (Befehlsticks 2, 3, 4) | H/ticks | 1 | 1 Stelle (kein Treffer): erwarteter Befehlsstrom mit den Ticks 2, 3 und 4 (ein Tick je Eingabeschritt) | 21 |

**`tests/CnCNew.Presentation.Tests/JetOrderTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 168,181 | JetOrderTests.GunFighterPasses (2400 Ticks) | H/zeit | 2 | 2 Stellen (keine Treffer): Schleife 2400 Ticks, Aussage „at least three passes … in 80 seconds“ | 21 |
| 190 | JetOrderTests.SimulationTimingMin | H/rate | 1 | 1 Stelle: Mindesttempo je Tick über SimulationTiming.PerSecondToPerTick(60m) minus 16 Rohbits Toleranz, verglichen mit jet.Speed | 21 |
| 203 | JetOrderTests.MissileFighterEndsItsAttack (1500 Ticks) | H/budget | 1 | 1 Stelle (kein Treffer): Laufbudget 1500 Ticks | 21 |
| 215 | JetOrderTests.MissileFighterEndsItsAttack (clearedAt - emptiedAt) | H/ticks | 1 | 1 Stelle (kein Treffer): der Auftrag endet höchstens 3 Ticks nach der letzten Rakete | 21 |

**`tests/CnCNew.Presentation.Tests/ObjectTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 498-507 | ObjectTests.Derivation (Feuerzustände) | H/zeit | 1 | 1 Stelle (kein Treffer): FIRING_A im Schusstick, danach 12 Schritte Schusspause (300 ms), nach leerem Magazin 40 Schritte bis bereit (ClipReloadTime 1000 ms) | 21 |
| 600,681,693 | ObjectTests.Ghosts, SceneryFollowsTheShroud (VisionIntervalTicks, SceneryFogInterval) | H/ticks | 3 | 3 Stellen (keine Treffer): Warten über SimulationWorld.VisionIntervalTicks + 1 und bis world.Tick % FrameProjector.SceneryFogInterval == 0 | 21 |
| 716,861,871 | ObjectTests.ChassisFollowsMotion, RealData (Schleifen 25/40/50) | H/budget | 3 | 3 Stellen (keine Treffer): Fahrstrecken von 25, 40 und 50 Ticks für Neigung und Lenkung | 21 |
| 742 | ObjectTests.TurretFollowsOrders (const step) | H/rate | 1 | 1 Stelle: step = 6 Grad je Tick, Kommentar „TurretTurnRate = 180 degrees per second, 30 ticks per second“ | 21 |
| 747,749 | ObjectTests.TurretFollowsOrders (Feuerbefehl je Tick) | H/ticks | 2 | 2 Stellen: ein Feuerbefehl je Tick, shotTick merkt den Tick des Schusses | 21 |
| 759,760 | ObjectTests.TurretFollowsOrders (15 Ticks) | H/zeit | 2 | 2 Stellen: erster Schritt = step, Schuss genau 15 Ticks nach dem Befehl (90 Grad bei 6 Grad je Tick); Z. 761 TurretWaits == 15, Z. 765 bis 774 Halten 60 und Rückdrehen 160 Ticks (keine Treffer) | 21 |

**`tests/CnCNew.Presentation.Tests/StructureConditionTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 484,493,556,572,692,719,838 | StructureConditionTests (Rig.Step, Rig.SampleOfFirst, ProductionDoorReuse, SupplyDock, BomberDoors, TrackerHousekeeping) | H/ticks | 7 | 7 Stellen: Probe nach dem Schritt = Ereignistick + 1, Zählung von Ticks mit DOCKING_ACTIVE, DOOR_1_CLOSING direkt im Folgetick, Stichprobe nur jeden 7. Tick | 21 |
| 516,520,555,571,589,605,608,620,633,659,662,669,682,711,737,759,775,786,814,833,882,912,923,934,953,980,990,1010,1012,1021,1052 | StructureConditionTests (Laufschleifen aller Tests) | H/budget | 31 | 31 Stellen (keine Treffer): Laufbudgets von 3 bis 12000 Ticks (z. B. 260 und 360 für einen Türzyklus, 1500 Dock, 2400 Reparaturdrohne, 200 Kanone bereit, 80 Feuern) | 21 |
| 529,530,533,535,541,543,612,616,624,637,791 | StructureConditionTests (ProductionDoorCycle, AirfieldDoors, RepairDrone) | H/zeit | 11 | 11 Stellen: Near(30, …) für DoorOpeningTime, DoorCloseTime und ConstructionCompleteDuration von 1000 ms (Toleranz 2 Ticks), UNPACKING mindestens 15 Ticks | 21 |
| 534,548,562,574,576,753 | StructureConditionTests (ProductionDoorCycle, ProductionDoorReuse, UplinkCannon) | H/zeit | 6 | 6 Stellen (keine Treffer): Near(60, …) für DoorWaitOpenTime 2000 ms, Fenster completions - 32 und - 25 bis - 2, Near(9, …) für RaiseAntennaTime 300 ms | 21 |
| 876,893,894,895 | StructureConditionTests.RealData (War Factory) | H/zeit | 4 | 4 Stellen (keine Treffer): Retail-Türzeiten als Ticks festgeschrieben: DoorOpeningTicks 98, DoorWaitOpenTicks 90, DoorCloseTicks 120, ConstructionCompleteTicks 45 (3250, 3000, 4000, 1500 ms; 98 durch Aufrunden von 97,5) | 21 |

**`tests/CnCNew.Presentation.Tests/TerrainTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 258 | TerrainTests.Water (UvScrollPerSecond) | H/rate | 1 | 1 Stelle: Fluss-UV-Scroll -0,06 je Sekunde = 0,002 je Bild mal 30 | 21 |

**`tests/CnCNew.Presentation.Tests/UnitConditionTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 444,445 | UnitConditionTests.ContinuousFire (ContinuousFireCoastTicks) | H/zeit | 2 | 2 Stellen: ContinuousFireCoastTicks == 30 für ContinuousFireCoast 1000 ms | 21 |
| 451-464 | UnitConditionTests.ContinuousFire (Schussfenster) | H/zeit | 1 | 1 Stelle (kein Treffer): geschossen wird nur in den ersten 24 Ticks, erwartet genau 8 Schüsse (DelayBetweenShots 100 ms = 3 Ticks) und die Stufenfolge innerhalb von 150 Ticks | 21 |
| 477,499,510,520,535,563,575,622,637,651,675,774,847,849,867,891,938,948 | UnitConditionTests (Laufschleifen aller Tests) | H/budget | 18 | 18 Stellen (keine Treffer): Laufbudgets von 5 bis 1200 Ticks (40 PreAttack mit 500 ms Vorlauf, 80 je Kurve, 200 Turm, 800/1200 Jet, 320 Fels, 400 Gatling mit Feuer in den ersten 90 Ticks) | 21 |
| 528 | UnitConditionTests.TurretRotate (6 Grad je Tick) | H/zeit | 1 | 1 Stelle: Kommentar „+90 degrees at 6 degrees per tick“; dazu Z. 540 Kill bei Schritt 40 und Z. 547 bis 550 Take(14), Skip(16).Take(6), Skip(22).Take(18), Skip(40) (keine Treffer) | 21 |
| 532,534,604 | UnitConditionTests.TurretRotate, UnitConditionTests.Parachute | H/ticks | 3 | 3 Stellen: Feuerbefehl je Tick mit shotTick, Fallschirm und Reiter entstehen im selben Tick (Reihenfolge vor dem ersten Update) | 21 |
| 730-736,744 | UnitConditionTests.Cheering (CheerTicks) | H/zeit | 1 | 1 Stelle (kein Treffer): Jubel von Tick 10 bis 99, bei Tick 100 vorbei („three seconds (90 ticks)“); Z. 744 benutzt UnitConditionTracker.CheerTicks + 2 | 21 |
| 786 | UnitConditionTests.TensileFreefall | H/zeit | 1 | 1 Stelle: FREEFALL, wenn der Fels mehr als 0,2 je Tick fällt; Z. 782 bis 787 nur während der ersten 100 Updates (Life < 100) | 21 |
| 925 | UnitConditionTests.RealJetsAndBus (30 Ticks) | H/budget | 1 | 1 Stelle: 30 Ticks Flug, danach muss JETEXHAUST gesetzt sein | 21 |

**`tests/CnCNew.Simulation.Tests/Program.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 434,438,442,460,466 | ProbeModule._nextWake / NextWake / OnSpawn / Describe / Load | H/ticks | 5 | 5 Stellen: Testmodul führt nextWake als absoluten Tick (world.Tick + ProbeData.FirstWake), Update gibt ProbeData.Sleep in Ticks zurück; Describe/Load schreiben nextWake | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Acquisition.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 196 | AcquisitionFlagsAreTypedAndStrict: MoodCheckTicks == 8 / == 60 | H/rate | 1 | Zusatz (kein Treffer): MoodAttackCheckRate 250 ms -> 8 Ticks, Standard 2 s -> 60 Ticks als feste Erwartung | 21 |
| 221 | AcquisitionIdleUnitPicksTheClosestEnemyInRange: firstShot is >= 2 and <= 4 | H/ticks | 1 | Zusatz (kein Treffer): erster Schuss 2–4 Ticks nach Leerlauf ("looks two frames after it became idle") | 20,21 |
| 222 | AcquisitionIdleUnitPicksTheClosestEnemyInRange | H/zeit | 1 | 1 Stelle (Schleife 215: 40 Ticks): erwartet >= 30 Schaden = 3 Schüsse bei 300 ms Nachladen (9 Ticks) innerhalb von 40 Ticks | 21 |
| 255 | AcquisitionKillsOneVictimAfterAnother: shots[3] - shots[2] <= 10 | H/zeit | 1 | Zusatz (kein Treffer): nächster Schuss höchstens 10 Ticks nach dem Kill (Nachladen 9 Ticks = 300 ms); 6 Schüsse in 120 Ticks | 21 |
| 283 | AcquisitionStructuresStealthAndMoods | H/zeit | 1 | 1 Stelle: 30 Ticks warten, bis der entdeckte Gegner erfasst und beschossen ist (MoodAttackCheckRate 250 ms = 8 Ticks); daneben 60/40/6-Tick-Läufe (263,268,278,290,294,300) | 21 |
| 333,334 | AcquisitionLeavesAnOrderedAttackAlone | H/zeit | 2 | 2 Stellen: 30 Ticks Befehlspause beenden den befohlenen Angriff nicht, 60 weitere schon; zuvor 90 Ticks = 9–10 Schüsse (325-331) | 21 |
| 374 | AttackMoveStopsFightsAndMarchesOn.haltTicks | H/zeit | 1 | 1 Stelle: Zähler der Halte-Ticks, geprüft mit haltTicks >= 18 (Zeile 390: zwei Nachladezeiten zu 9 Ticks); Schleifen 400/220/500/120 Ticks sind Budgets | 21 |
| 458 | AttackMoveSnapshotDeterminismAndReplay: new ReplayHeader(..., 30, ..., 300, 20) | H/rate | 1 | 1 Stelle: ReplayHeader.TickRate als Literal 30; EndTick 300 und CheckpointInterval 20 in Ticks | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Air.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 257,267 | JetLifecycle | H/budget | 2 | 2 Stellen: Schleife bis 900 Ticks für den Start, StepUntil(…, 900) bis zum Ziel; weitere Budgets 2500/600/200/1500/4000 Ticks in der Datei | 21 |
| 278 | JetLifecycle: jet.WeaponReadyTick >= SimulationTiming.SleepForever | H/ticks | 1 | 1 Stelle: RETURN_TO_BASE-Waffe hat Bereit-Tick >= SleepForever (Sentinel, kein Zeitwert) | 21 |
| 347-348 | AirRunwayQueueIdleReturnAndFullAirfield: StepUntil(…, 120, …) | H/zeit | 1 | Zusatz (kein Treffer): ReturnToBaseIdleTime 1500 ms = 45 Ticks (Kommentar), Grenze 120 Ticks; 150 Ticks Warten auf zurückgehaltene Produktion (318); 40 Ticks Heilung (250) | 21 |
| 377,378 | AirDestroyedAirfield | H/zeit | 2 | 2 Stellen: 30 Ticks = 1 s, erwartet mehr als 30 Lebenspunkte Verlust (OutOfAmmoDamagePerSecond 30 % von 200 = 60/s) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.AirModelGeometry.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 97 | AirAirfieldUsesTheModelBones: StepUntil(…, 900, …) | H/budget | 1 | 1 Stelle: bis 900 Ticks bis zum Ziel; weitere Budgets 200/600/1500/4000 Ticks und 60 Folgeschritte nach Snapshot | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.AirReal.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 149,150 | RealAir.Pulse | H/zeit | 2 | 2 Stellen: 30 Ticks nach dem Erzeugen des EMP-Pulses muss EmpDisabledUntil > Tick + 800 sein ("30 seconds" = 900 Ticks aus Realdaten) | 21 |
| 214 | RealAir.NukeFromSilo | H/budget | 1 | 1 Stelle: bis 900 Ticks Flug der Rakete; weitere Budgets der Datei: 4000/3000/2000/6000/1500/600/300/700/12000/150/40 Ticks | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.AirWeapons.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 350 | AirWeaponFixtureFiles: NeutronMissileUpdate ForwardDamping = 0.1 | D | 1 | Fixture-Wert, den NeutronMissileUpdate roh je Tick anwendet (velocity -= ForwardDamping * velocity, AirWeaponModules.cs 597-599; ModuleConfigReader.Real ohne Umrechnung) | 20,23 |
| 516 | AirEmpPulse: pulseTick = world.Tick - 1 | H/ticks | 1 | 1 Stelle: der Puls fiel im Tick vor dem beobachteten (StepUntil prüft nach dem Schritt) | 21 |
| 518,519 | AirEmpPulse | H/zeit | 2 | 2 Stellen: Puls StartFadeTime 300 ms = 9 Ticks nach Erzeugung (±1); DisabledDuration 2000 ms = pulseTick + 59…62; davor 6 Ticks 'noch nichts', StepUntil 20, danach 20 und 100 Ticks | 21 |
| 555 | AirLeafletBomb | H/zeit | 1 | 1 Stelle: DisabledDuration 1500 ms = tick + 44 oder + 45 | 21 |
| 723,725,727 | AirMissileSiloDoors | H/zeit | 3 | 3 Stellen: ready > Tick + 100 (ReloadTime 5000 ms = 150 Ticks); Run(ready - 40) noch zu, Run(ready - 20) öffnet (DoorOpenTime 30 Ticks laut Meldung); außerdem use + 3 / + 25 / + 70 (DoorWaitOpenTime 15 Ticks, 732-738) | 21 |
| 782 | AirSpectreGunship: world.Tick - orbitStart > 20 | H/zeit | 1 | 1 Stelle: Abstandsmessung erst 20 Ticks nach Orbitbeginn (Einschwingen); davor 125 Ticks 'the power recharges for 4 seconds' (759), Schleife 300 Ticks bei OrbitTime 3000 ms | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Basics.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 50 | Timing: SimulationTiming.LogicFramesPerSecond == 30 | H/rate | 1 | 1 Stelle: prüft den Ratenwert selbst ("LOGICFRAMES_PER_SECOND is 30") | 21 |
| 52,55,56,58,62,63,64,65,67 | Timing: cases ms->ticks; DurationMillisecondsToTicks/ToFixedTicks, SecondsToTicks, PerSecondToPerTick, PerSecondSquaredToPerTickSquared, DegreesPerSecondToRadiansPerTick | H/rate | 9 | 9 Stellen: Erwartungstabelle ms -> Ticks ([33,1],[34,2],[999,30],[1000,30],[1001,31] …), 1000 ms = 30, 1 s = 30, 30/s = 1/Tick, 900/s² = 1/Tick², 180°·30/s = pi/Tick | 21 |
| 73,75,80,85 | FloatDeviation: perMsec = 30.0f / 1000.0f | H/rate | 4 | 4 Stellen: Vergleich der exakten Umrechnung mit der binary32-Formel der Referenz (ceilf(msec * 30/1000)) über 0…5.000.000 ms; Grenzen 4.000.000 ms und < 50.000 Abweichungen (86-87) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.BattlePlan.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 185-209 | SpecialsBattlePlanCycle: CsRun(world, 5/40/12/2/12/40/60) | H/zeit | 1 | Zusatz (kein Treffer): nach 5 Ticks noch Unpacking, binnen 40 aktiv (AnimationTime 1000 ms); 12 Ticks gelähmt, nach 40 weiter (BattlePlanChangeParalyzeTime 1 s) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.CleanupAndSpy.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 335 | SpecialsSpyVision: ended - start >= 44 && <= 47 | H/zeit | 1 | Zusatz (kein Treffer): gekappte Dauer MaxDuration 1,5 s = 45 Ticks; Beobachtungsschleife 70 Ticks (328), Aufladen bis 200 Ticks (313) | 21 |
| 372,373 | SpecialsSpyVisionUpgrades: onTicks / offTicks | H/zeit | 2 | 2 Stellen: zählt An-/Aus-Ticks über 400 Ticks (1 s an, 2 s aus = 90 Ticks je Zyklus); verlangt cycles >= 3 und Anteil 28–40 % (376-384) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Collision.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 13 | Tests.CollisionLoco (Kommentar) | H/rate | 1 | 1 Stelle: Kommentar "LogicFramesPerSecond = 30 ticks/second: 300/sec = 10 world units/tick" – die Fixture-Geschwindigkeit ist auf 10 Einheiten je Tick ausgelegt | 21 |
| 170,188 | CollisionFiles: StructureToppleUpdate StructuralDecay = 0.9 / 1.0 (mit StructuralIntegrity 0.5 / 0.75, Zeilen 169/187) | D | 2 | 2 Stellen: Fixture-Werte, die StructureToppleUpdate roh je Tick verwendet (_structuralIntegrity *= StructuralDecay, ToppleAndFallModules.cs 316; ModuleConfigReader.Real) | 20,23 |
| 413,433 | CollisionStructureTopple | H/zeit | 2 | 2 Stellen: erwartet den Aufschlag nach etwa 104 Ticks (90 < fallen < 130), hergeleitet aus 0,02·0,25 rad je Tick²; Stichprobe 'mitten im Fall' bei tick == 60 (426); Schleifen 400/300 Ticks | 21,23 |

**`tests/CnCNew.Simulation.Tests/Tests.Combat.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 298,310 | FireWeaponUpdateCadence | H/zeit | 2 | 2 Stellen: Pulsabstände als Tickdifferenzen, geprüft mit drops[0] 18…20 (InitialDelay 600 ms) und == 9 (DelayBetweenShots 300 ms) in 60 Ticks (302-303); 30 Ticks Warten auf den Selbstschaden | 21 |
| 328,333 | FireWeaponUpdateClip | H/zeit | 2 | 2 Stellen: 3 Ticks zwischen Schüssen (100 ms), 30 Ticks ClipReloadTime 1000 ms, wieder 3; Schleife 100 Ticks (332-334) | 21 |
| 354,357 | FireWeaponUpdateExclusiveDelay: world.Tick < shotTick + 28 | H/zeit | 2 | 2 Stellen: 28 Ticks nach dem echten Schuss bleibt der Puls stumm (ExclusiveWeaponDelay 1000 ms = 30 Ticks), 20 weitere Ticks später pulst er wieder | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Construction.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 237 | Tests.StepUntil(world, limit, done): return tick + 1 | H/ticks | 1 | 1 Stelle: Helfer zählt Schritte bis zur Bedingung und gibt die Schrittzahl zurück; limit ist das Budget in Ticks | 21 |
| 268,401 | ConstructionBuildsAStructure; ConstructionResumeWithAnotherBuilder: ConstructionWork == 60 | H/zeit | 2 | 2 Stellen: BuildTime 2 s = genau 60 Ticks Bauarbeit (work == 60, site.ConstructionWork == 60) | 21 |
| 315 | ConstructionSellTimingAndPayout: sold = world.Tick - start | H/zeit | 1 | 1 Stelle: Verkaufsdauer als Tickdifferenz, geprüft mit sold == 179 (Zeile 317); Schleife 300 Ticks | 21 |
| 381 | ConstructionRepair: ticks > 100 && ticks < 200 | H/zeit | 1 | 1 Stelle: 10 % von 500 je Sekunde heilen 200 Punkte in etwa 120 Ticks plus Anfahrt; StepUntil-Budget 400 | 21 |
| 393,502 | ConstructionResumeWithAnotherBuilder; ConstructionSnapshotAndReplay: StepUntil(world, 30, () => false) | H/zeit | 2 | 2 Stellen: 30 Ticks laufen lassen = 'mitten im Bau' (BuildTime 60 Ticks) bzw. mitten im Verkauf; danach 260 Folgeschritte | 21 |
| 517-529 | ConstructionSnapshotAndReplay: Befehls-Ticks 0, 2, 3, 5, 20, 25, 40, 60, 70 | H/ticks | 1 | Zusatz (kein Treffer): Replay-Befehle mit festen Tick-Stempeln; die Abfolge unterstellt den 30-Hz-Fortschritt (Abbruch bei 20, Wiederaufnahme 25, Reparatur 60) | 21 |
| 531 | ConstructionSnapshotAndReplay: new ReplayHeader(..., 30, ..., 400, 50) | H/rate | 1 | 1 Stelle: ReplayHeader.TickRate als Literal 30; EndTick 400, CheckpointInterval 50 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.CratesAndSpecials.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 520 | CratesAndSpecialsFiles: GrantStealthBehavior RadiusGrowRate = 10.0 | D | 1 | Zusatz (kein Treffer): Fixture-Wert, den GrantStealthBehavior roh je Tick addiert (_radius += RadiusGrowRate, SpecialsModules.cs 221) | 20,23 |
| 838 | SpecialsHijacker: (truck.X - hijacker.X).Abs() <= 2 | H/rate | 1 | 1 Stelle: Kommentar/Erwartung 'höchstens der Weg eines Ticks (2 Einheiten)' = Speed 60/s ÷ 30 (Prüfung Zeile 839) | 21 |
| 923,925 | SpecialsSabotage | H/zeit | 2 | 2 Stellen: SabotageDuration 3 s = 90 Ticks: nach 80 Ticks nichts produziert, Fertigstellung frühestens 110 Ticks nach der Sabotage; außerdem PowerSabotagedUntil = hit + 59…61 (2 s, Zeile 912) | 21 |
| 1026 | SpecialsGrantStealth: CsRun(world, 1) / CsRun(world, 1) / CsRun(world, 2) | H/zeit | 1 | 1 Stelle: je Tick wächst der Scanradius um RadiusGrowRate 10 (erster Scan 20, zweiter 30, letzter 50; 1023-1030) | 21,23 |

**`tests/CnCNew.Simulation.Tests/Tests.CreationLists.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 245 | Phase5CreationLists: world.Tick - spawnedAt is >= 25 and <= 35 | H/zeit | 1 | 1 Stelle: Lebensdauer 1000 ms = 25…35 Ticks nach dem Erzeugen; Schleifengrenze spawnedAt + 200 (244) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.DamagedLocomotor.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 73,81 | DamagedLocomotorSlowsAReallyDamagedObject.Travel: 60 Ticks; pristine > 170 | H/zeit | 1 | Zusatz (kein Treffer): 60 Ticks = 2 s Fahrt bei Speed 90/s ergeben > 170 Einheiten; beschädigt ein Drittel davon (±5) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.FiringTracker.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 11,139,172 | FiringTrackerFiles (Kommentar); FiringTrackerSpinsUpCoastsAndCoolsDown | H/zeit | 3 | 3 Stellen: Kommentar '600 ms = 18 Ticks, Coast 1000 ms = 30 Ticks'; Schussabstände 18/9/4–5 Ticks (144-147); schnelle Stufe hält 30–36 Ticks, langsame genau 31 (172-173); Läufe 120/20/10/90/60/100 Ticks | 21 |
| 194 | PreAttackDelayWindsUpBeforeTheShot: clipGun.PreAttackDelayTicks == 15 | H/rate | 1 | Zusatz (kein Treffer): PreAttackDelay 500 ms -> 15 Ticks als feste Erwartung am Waffentemplate | 21 |
| 215,220,224,230,231,232,236 | PreAttackDelayWindsUpBeforeTheShot | H/zeit | 7 | 7 Stellen: Schuss-Ticks relativ zum Start: PER_SHOT [15, 60], PER_ATTACK [15, 45, 75] und + 30/+ 15 nach dem Zielwechsel bei Tick 100, PER_CLIP [15, 21, 27, 102, 108, 114], Slot-Indizes 15/16/30/46; Läufe 100/200/150/60 Ticks | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Flight.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 17 | FlightFixtureIni (Doc-Kommentar) | H/zeit | 1 | 1 Stelle: Kommentar 'drops after 1 door tick, 3 ticks between drops, sinks at 1 per tick' – die Fixture ist auf 30 Hz ausgelegt (DoorDelay 34 ms, DropDelay 100 ms, SpeedLimitZ 30/s) | 21 |
| 530 | FlightCountermeasuresDivertMissiles: MissionOptions.Standard with { CombatIntervalTicks = 3 } | H/ticks | 1 | 1 Stelle: Missionsoption in Ticks (Kampfintervall 3) vom Test gesetzt; danach 60 Ticks Lauf, in denen Raketen treffen müssen | 20,21 |
| 580 | FlightParticleUplinkCannonBurnsItsTarget | H/zeit | 1 | 1 Stelle: Kommentar '18 Ticks Strahl bei 100/s in 6 Pulsen zu 10'; dazu Run 4 = Strahl noch unterwegs (BeamTravelTime 200 ms = 6 Ticks), Run 8 = brennt, 60 Ticks bis zum Ende (567-579) | 21 |
| 680 | FlightStrikePowerDeliversBombs | H/budget | 1 | 1 Stelle: bis 900 Ticks, bis die Bomber weg sind; Snapshot bei tick == 60, danach 700 Ticks im wiederhergestellten Lauf (684, 691) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.GoldenMaster.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 17,71,94 | Phase5GoldenDamageChain (Doc); Phase5GoldenProduction: completed[1] - completed[0] == 2 * 30 | H/rate | 3 | 3 Stellen: Doc 'ms -> Ticks bei 30 pro Sekunde: 500 ms = 15, 2000 ms = 60', Doc 'BuildTime * LOGICFRAMES_PER_SECOND', Erwartung BuildTime 2 · 30 = 60 Ticks zwischen zwei Fertigstellungen | 21 |
| 54 | Phase5GoldenDamageChain: unabhängiges Modell ready += 60 / ready += 15 | H/zeit | 1 | 1 Stelle (mit 60-61): Modell der Schuss-Ticks mit ClipReloadTime 2000 ms = 60 und DelayBetweenShots 500 ms = 15 Ticks, verglichen mit den gemessenen Ticks; Schleife 400 Ticks | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.JetFlight.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 129,130 | Tests.WingMinSpeed / Tests.WingTurnPerTick | H/rate | 2 | 2 Stellen: Erwartungswerte je Tick über SimulationTiming.PerSecondToPerTick(60) und DegreesPerSecondToRadiansPerTick(120); 5 Verwendungen (175,176,213,230,333) | 21 |
| 165,182,229 | WingMinimumSpeedAndNoStopping / WingCirclesItsDestination | H/budget | 3 | 3 Stellen: Laufschleifen 900 Ticks (Flug 1000 Einheiten + Messung), 30 Ticks (Heli bleibt stehen), 900 Ticks (ankommen und kreisen) | 21 |
| 174 | WingMinimumSpeedAndNoStopping (arrived < 400) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): Ankunft muss vor Tick 400 liegen (1000 Einheiten bei 150/s ≈ 200 Ticks) | 21 |
| 176,215 | WingMinimumSpeedAndNoStopping / WingTurnRateIsLimited | H/rate | 2 | 2 Stellen: Schritt je Tick wird gegen Geschwindigkeit je Tick geprüft (WingMinSpeed − Raw 64; PerSecondToPerTick(150) + Raw 64) | 21,23 |
| 187 | WingTurnRateIsLimited (Doku-Kommentar) | H/rate | 1 | 1 Stelle: Kommentar «120 degrees/s, 4 degrees per tick» rechnet mit 30 Hz | 21 |
| 214 | WingTurnRateIsLimited (turned >= 40) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): eine Kehrtwende braucht mindestens 40 Ticks (180° / 4° je Tick = 45) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.JetFlightReal.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 36,37 | RealWingedFlight (RaptorJetLocomotor) | H/rate | 2 | 2 Stellen: Katalogwerte MinSpeed/TurnRatePerTick werden mit PerSecondToPerTick(60) und DegreesPerSecondToRadiansPerTick(120) verglichen | 21 |
| 69 | RealWingedFlight.Run (tick < 4200) | H/budget | 1 | 1 Stelle (kein Mustertreffer): 4200 Ticks für vier Schenkel des Quadrats und das Kreisen | 21 |
| 75-77 | RealWingedFlight.Run (tick > 20) | H/zeit | 1 | 1 Stelle: die ersten 20 Ticks (Steigen auf Flughöhe) zählen nicht für die Geschwindigkeits-/Schrittmessung | 21 |
| 102,104 | RealWingedFlight (smallestStep, worstTurn) | H/rate | 2 | 2 Stellen: Schritt und Drehung je Tick gegen loco.MinSpeed/TurnRatePerTick (Katalogwerte je Tick, Toleranz Raw 64 bzw. Raw 4) | 21,23 |

**`tests/CnCNew.Simulation.Tests/Tests.LastBlockers.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 335 | Tests.LastBlockerIni: SmartBombTargetHomingUpdate.CourseCorrectionScalar = 0.8 | D | 1 | Fixture-Wert (kein Mustertreffer): Faktor je Tick, vom Modul roh über ModuleConfigReader.Real gelesen (LastBlockerModules.cs SmartBombHomingData) | 20,23 |
| 467,718 | Tests.LbSnapshotContinues (ticks = 120) / LastBlockersSmartBombHoming | H/budget | 2 | 2 Stellen: Vergleichslänge live gegen wiederhergestellt, Standard 120 Ticks und Aufruf mit 30 (weitere Aufrufe mit 20/40/60/100 ohne Treffer) | 21 |
| 682 | LastBlockersSabotageInternetCenter (sabotaged.Value == 30) | H/zeit | 1 | 1 Stelle: Ereigniswert = SabotageDuration 1000 ms als 30 Ticks; danach CsRun 24/70 Ticks als «noch gesperrt»/«wieder frei» (685,688) | 21 |
| 717 | LastBlockersSmartBombHoming (ein Schritt = ein Fünftel) | H/ticks | 1 | 1 Stelle: nach genau einem Tick steht die Bombe bei 20 % des Wegs (CourseCorrectionScalar 0.8 je Tick) | 21,23 |
| 777,833 | LastBlockersCrippling… (NextWake == SimulationTiming.SleepForever) | H/ticks | 2 | 2 Stellen: Modul schläft für immer (SleepForever ist nicht ratenabhängig) | 21 |
| 784,785,824 | LastBlockersCrippling… (HealingSuppressedUntil/NextWake == hit + 30) | H/zeit | 3 | 3 Stellen: SelfHealSupression 1000 ms als «+ 30» Ticks erwartet | 21 |
| 792-794 | LastBlockersCripplingBlocksAndHealsTheWarehouse (CsRun 100, Health 300..350) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): nach 100 Ticks liegt die Heilung zwischen 300 und 350 («5 every 15 ticks», SelfHealDelay 500 ms); danach CsRun 600 als Schranke | 21 |
| 908,953,965 | LastBlockersFerry… (CsRun 900 / 900 / 30) | H/budget | 3 | 3 Stellen: Obergrenzen mit Abbruchbedingung für die Fährfahrt (900) und 30 Ticks «fährt nie los» | 21 |
| 1020 | RealLastBlockers (Hashfolge zweier Läufe) | H/ticks | 1 | 1 Stelle: zwei Läufe sind in jedem Tick hashgleich (150 + 300 Ticks, kein gepinnter Hash) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Navigation.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 166,300,335,395,410 | Tests.NavRun (limit) / NavigationMovement / NavigationRepathsAfterConstruction | H/budget | 5 | 5 Stellen: NavRun zählt Ticks bis zum Ende des Befehls; Obergrenzen 900, 30, 900, 900 (weitere 400er ohne Treffer) | 21 |
| 294,310 | NavigationMovement (turnLimit = 0.0523599 rad) | H/rate | 1 | 1 Stelle: Drehgrenze je Tick als Literal 0,0523599 rad = 3° je Tick = TurnRate 90°/s ÷ 30 (+ Raw 64) | 21 |
| 312 | NavigationMovement (ticks > 150 / 2) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): Umweg dauert länger als die Gerade – 150 Einheiten bei 2 Einheiten je Tick (60/s ÷ 30) | 21 |
| 390-391 | NavigationRepathsAfterConstruction (36 Ticks, y 26..50) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): nach 36 Ticks steht der Läufer noch nördlich der künftigen Mauer | 21 |
| 444 | NavigationSnapshotsAndDeterminism | H/ticks | 1 | 1 Stelle: zwei Läufe sind in jedem Tick hashgleich (40 Ticks, danach 400) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.NavigationEscape.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 36 | NavigationFootprintEscape (NavRun 900) | H/budget | 1 | 1 Stelle: Obergrenze 900 Ticks bis zur Ankunft | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Phase12Modules.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 18 | Phase12ModulesCreateAndBehave (MissionOptions.CombatIntervalTicks = 0) | H/ticks | 1 | 1 Stelle: Missionsoption in Ticks auf 0 gesetzt (schaltet die Selbsterfassung ab, MissionSession.cs:78); die übrigen Zahlen der Zeile sind Koordinaten | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Phase13Actions.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 159 | ScriptAiExpandsToASupplyWarehouse (Run 900) | H/budget | 1 | 1 Stelle: 900 Ticks, in denen der Dozer das Depot fertig gebaut und die Wache losgelaufen sein muss | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Phase5.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 627-628 | Phase5StealthAndHeal (35 Ticks, StealthDelay) | H/zeit | 1 | 1 Stelle: nach 35 Ticks ist getarnt – «1000ms = 30 ticks»; danach 90 Ticks «tarnt nie» und 10 Ticks Heilung | 21 |
| 751,832,851,1003,1026,1042,1082,1098 | Phase5Flammable / Phase5SlowDeath / Phase5Poisoned / Phase5BaseRegenerate (world.Tick - 1) | H/ticks | 8 | 8 Stellen: Ankertick des Ereignisses = world.Tick − 1 (der gerade ausgeführte Tick) | 21 |
| 753,754,757,758,762,763 | Phase5Flammable (igniteTick + 3 / + 6 / + 9) | H/zeit | 6 | 6 Stellen: AflameDamageDelay 100 ms = 3, BurnedDelay 200 ms = 6, AflameDuration 300 ms = 9 Ticks, mit exakten Gesundheitswerten je Stufe | 21 |
| 836,838,839,841,853,855 | Phase5SlowDeath (deathTick + 14 / + 15) | H/zeit | 6 | 6 Stellen: DestructionDelay 500 ms = 15 Ticks, «einen Tick vorher noch da» (+ 14) und «genau dann weg» (+ 15), zweimal | 21 |
| 937,945 | Phase5StealthDetector (20 Ticks, expiry = Tick + 16) | H/zeit | 1 | 1 Stelle: DetectionRate 500 ms = 15 Ticks plus Zufallsphase (20 Ticks warten); DETECTED läuft nach UpdateRate + 1 = 16 Ticks ab | 21 |
| 1005,1006,1009,1012,1014,1018,1034,1045 | Phase5Poisoned (doseTick + 3 / 12 / 15 / 18, + 20) | H/zeit | 8 | 8 Stellen: PoisonDamageInterval 100 ms = 3 Ticks, PoisonDuration 500 ms = 15 Ticks; exakte Gesundheit an +3, +12, +15, +18 und nach Heilung +20 | 21 |
| 1085,1086,1090,1092,1105 | Phase5BaseRegenerate (hitTick + 29 / 30 / 33, + 30 + 3 * 40) | H/zeit | 5 | 5 Stellen: BaseRegenDelay 1000 ms = 30 Ticks; Heilstöße bei +30 und +33 (alle drei Ticks) mit exakten Werten 712,5 und 725 | 21,23 |
| 1089 | Phase5BaseRegenerate (Kommentar 1000 * 12.5% * 3 / 30) | H/rate | 1 | 1 Stelle: Erwartungsrechnung mit der 30 im Nenner: 12,5 Punkte je Stoß von drei Ticks | 21,23 |
| 1153 | Phase5Garrison (TimeForFullHeal 300 ms = 9 Ticks) | H/zeit | 1 | 1 Stelle: nach genau 9 Ticks ist der Insasse voll geheilt (50/9 je Tick), Schleife in Zeile 1154 | 21 |
| 1230,1233 | Phase5Transport (HealthRegen%PerSec 30 = 0,6 je Tick) | H/rate | 2 | 2 Stellen: 18/s als 0,6 je Tick; nach 10 Ticks liegt die Gesundheit zwischen 30 und 60 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.PlayerAgent.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 30,67,74 | ScriptCommandsOnlyDriver… (maxTicks: 900) | H/budget | 3 | 3 Stellen: Laufbudget des Zielfahrers 900 Ticks (weitere Aufrufe mit 6000 ohne Treffer, Zeilen 20 und 56) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.PowerPlant.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 152 | OverchargeTogglesDrainsAndSwitchesItselfOff (zwei Schritte) | H/ticks | 1 | 1 Stelle: nach zwei Ticks ist Gesundheit abgezogen («drains health every tick») | 21 |
| 156-158 | OverchargeTogglesDrainsAndSwitchesItselfOff (40 Ticks) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): nach 40 Ticks hat 30 %/s die Anlage unter 90 % gedrückt (10 % = 10 Ticks) und sie schaltet ab | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Projectiles.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 266,325,353,373 | ProjectileStraightShellFlightTime / ProjectileMissileFlightTime / ProjectileMissileAcceleration / ProjectileDelayedHit (Doku) | H/rate | 4 | 4 Stellen: handgerechnete Umrechnungen in den Kommentaren: 300/s = 10 je Tick, 9000/s² = 10 je Tick², 900/s² = 1 je Tick², 60/s = 2 je Tick, 600/s = 20 je Tick, 100 ms = 3 Ticks | 21 |
| 279-288 | ProjectileStraightShellFlightTime (Segments == 20, Einschlag F + 21) | H/zeit | 1 | 1 Stelle (Treffer 281): 200 Einheiten bei 10 je Tick = 20 Bahnpunkte, kein Einschlag vor F + 21, Detonation und FX genau in F + 21 | 21,23 |
| 305,311,319 | ProjectileBallisticArc (Segments == 14, F + 6, F + 15) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): 14 Bahnpunkte, Punkt 5 bei x = 44,515/z = 21,302 in Tick F + 6, Landung in F + 15 | 21,23 |
| 308 | ProjectileBallisticArc (world.Tick < fire + 30) | H/budget | 1 | 1 Stelle: Suchschranke 30 Ticks nach dem Schuss (Rakete: fire + 20 in Zeile 339) | 21 |
| 336,344,349 | ProjectileMissileFlightTime (x == 10 nach F + 1, Detonation F + 10, entfernt F + 13) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): Zündung sofort, 10 je Tick, Einschlag F + 10; KillSelfDelay-Rückfallwert 3 Ticks (ProjectileModules.cs:341) entfernt die Rakete in F + 13 | 21,23 |
| 363-370 | ProjectileMissileAcceleration (expected = [2, 4, 7, 11, …, 96], F + 15) | H/rate | 1 | 1 Stelle (Treffer 367): Tabelle der x-Positionen je Tick aus Euler-Schritten mit 2 je Tick Anfangstempo und 1 je Tick² Beschleunigung, Landung F + 15 | 21,23 |
| 379-387 | ProjectileDelayedHit (Treffer in F + 8) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): 150 Einheiten bei 20 je Tick landen ceil(7,5) = 8 Ticks später | 21,23 |
| 390-393 | ProjectileDelayedHit (unter einem Tick Flugweg sofort) | H/rate | 1 | 1 Stelle: 15 Einheiten sind weniger als ein Tick Flugweg (20 je Tick), der Schaden fällt im Schusstick | 21,23 |

**`tests/CnCNew.Simulation.Tests/Tests.RealFlight.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 87 | RealFlightAndTransports.Run (tick < 1800) | H/budget | 1 | 1 Stelle: 1800 Ticks Laufzeit für Geschosse, Chinook-Lieferung und Tunnel; Evakuierung bei tick == 10 | 21 |
| 117,118,124 | RealFlightAndTransports (impact > fire + 1; Hashfolge) | H/ticks | 3 | 3 Stellen: Geschoss und Rakete fliegen länger als einen Tick; zwei Läufe sind in jedem Tick hashgleich | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.RealMovement.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 45 | RealSquadPassesAStandingDozer.Run (tick < 1800) | H/budget | 1 | 1 Stelle: Obergrenze 1800 Ticks, bis alle acht Ranger angekommen sind | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.ReferenceMatch.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 168 | ReferenceMatch (ReferenceMatchOptions: MaxTicks 60_000, MinTicks 62_000, CheckpointInterval 500, GraceTicks 100) | H/budget | 1 | 1 Stelle: Laufbudget in Ticks; Zeile 172 erwartet EndTick == 62_000; CheckpointInterval 500 ist Tick-Semantik, GraceTicks 100 eine Frist in Ticks | 21 |
| 200 | ReferenceMatch (TickHashes zweier Läufe) | H/ticks | 1 | 1 Stelle: zwei Wiedergaben sind in jedem Tick hashgleich (kein gepinnter Hash in dieser Datei) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Repair.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 294 | RepairPlayerTypeReplayHeader (new ReplayHeader(…, 30, …)) | H/rate | 1 | 1 Stelle: Literal 30 als ReplayHeader.TickRate (dritter Parameter, ReplayFormat.cs:20) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.RetailModules.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 265 | RetailProductionExits (defaultTicks[n+1] - defaultTicks[n] == 30) | H/zeit | 1 | 1 Stelle: Einheiten verlassen die Fabrik im Abstand der BuildTime 1 s = 30 Ticks (SpawnTicks-Budget 400) | 21 |
| 326 | RetailHealContain (Heilung im Eintrittstick) | H/ticks | 1 | 1 Stelle: schon im Tick des Eintritts ist die Gesundheit über 20 | 21 |
| 327-328 | RetailHealContain (12 Ticks bis voll) | H/zeit | 1 | 1 Stelle (kein Mustertreffer): nach 12 Ticks ist der Insasse innerhalb TimeForFullHeal voll geheilt und ausgestiegen | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Scripting.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 555,582,598,603,605 | ScriptEngineDamageHistoryAreasAndPaths / …AttackPrioritiesAndReinforcements / …SnapshotContinuesIdentically | H/budget | 5 | 5 Stellen: Missionslauf 900 Ticks (Pfad ablaufen), 30 Ticks (Schütze trifft), dreimal 900 Ticks für Fortsetzungs- und Wiederholungsvergleich (Hashes nur untereinander) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.ScriptingActions12.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 43 | ScriptTeamCommandButtons (MissionOptions.CombatIntervalTicks = 0) | H/ticks | 1 | 1 Stelle: Missionsoption in Ticks auf 0 gesetzt (Selbsterfassung aus); die übrigen Zahlen der Zeile sind Koordinaten | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.ScriptingPhase11.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 110 | ScriptMapRevealAndShroud (Run 30) | H/zeit | 1 | 1 Stelle: 30 Ticks müssen mindestens eine periodische Sichtauffrischung enthalten («survives the periodic vision refresh») | 21 |
| 261 | ScriptSequentialScripts (Sequenz in einem Tick) | H/ticks | 1 | 1 Stelle: eine Einheitensequenz läuft ihre Anweisungen in einem Tick durch (nach Run 3) | 21 |
| 373 | ScriptTeamEventScripts (Run 30) | H/budget | 1 | 1 Stelle: 30 Ticks, in denen kein Skript ein zweites Mal läuft (weitere Läufe 400/3000/200 ohne Treffer) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.ScriptingPhase12.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 61 | ScriptExplainLeavesTheEngineUntouched | H/ticks | 1 | 1 Stelle: Erklärung in jedem der drei Ticks ändert den Hash nicht | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Skirmish.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 236,246 | SkirmishAiPlaysAMatch (RecordCheckpointInterval = 500; TickHashes) | H/ticks | 2 | 2 Stellen: Replay-Prüfpunkte alle 500 Ticks (auch VerifyIncrementalEvery 500), zwei Läufe in jedem Tick hashgleich; SkirmishRecord(12_000) ist das Budget | 21 |
| 263-264 | SkirmishAiOnlyUsesWhatItSees (SkirmishRecord(60)) | H/zeit | 1 | 1 Stelle: 60 Ticks stehen für «2 s of game time», in denen ein Späher mit 60 Einheiten/s die 400 Einheiten nicht überblickt | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Slaves.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 660 | SlavedScoutDroneGrantsSpottingBonus (tick % 12 == 0) | H/zeit | 1 | 1 Stelle: der Test feuert alle 12 Ticks (DelayBetweenShots 300 ms = 9 Ticks), Obergrenze 200 Ticks, danach StepN 150 | 21 |
| 782,795 | PropagandaTowerBuffsAlliesInRange (ein Punkt je Tick) | H/rate | 2 | 2 Stellen: HealPercentEachSecond 30 % von 100 = «one a tick»; Gewinn in 20 Ticks zwischen 15 und 25 erwartet | 21 |
| 970 | AssaultTransportDeploysItsTroops (StepN 30) | H/budget | 1 | 1 Stelle: 30 Ticks, in denen der abgesetzte Soldat näher ans Ziel kommt | 21 |
| 993,1000,1002 | TensileFormationSlidesDownTheSlope (200 + 250 Ticks, dann 30) | H/zeit | 1 | 1 Stelle (Treffer 1002): nach 450 Ticks muss die Formation ruhen («stops after 300 ticks»), 30 weitere Ticks ohne Bewegung | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.SpecialPower.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 312,453 | SpecialPowerUse (airdrop.ReloadTicks == 300) / SpecialPowerWiring (fallback.ReloadTicks == 30) | H/zeit | 2 | 2 Stellen (312 kein Mustertreffer): ReloadTime 10.000 ms = 300 Ticks und DefaultSpecialPower 1.000 ms = 30 Ticks als feste Tickzahl erwartet | 21 |
| 340,380,409,422,437 | SpecialPowerUse / SpecialPowerWiring (usedAt = Tick - 1; + 3; ein Tick nach dem Spawn) | H/ticks | 5 | 5 Stellen: Ankertick world.Tick − 1, drei Ticks für die Befehlsverarbeitung eines Upgradekaufs, «SpecialPowerCreate runs one tick after the spawn» | 21 |
| 359,443,454 | SpecialPowerUse / SpecialPowerWiring (StepTo(Tick + ReloadTicks)) | H/zeit | 3 | 3 Stellen: Wartezeit aus dem Katalogwert ReloadTicks (auch 330 und 342: reload − 1 mit Text «tick 299») | 21 |
| 365,382,418,469,475,484 | SpecialPowerUse / SpecialPowerWiring / SpecialPowerCash (StepTo + 100 / + 61 / + 130 / + 40) | H/zeit | 6 | 6 Stellen: Wartezeiten knapp über der Nachladezeit: Edge 3000 ms (+ 100), Paused 2000 ms (+ 61), Gunship 4000 ms (+ 130), Hack 1000 ms (dreimal + 40) | 21 |
| 375,500,508 | SpecialPowerUse / SpecialPowerCash (StepTo + 400 / + 30) | H/budget | 3 | 3 Stellen: 400 Ticks «pausiert wird nie bereit», zweimal 30 Ticks, bis der Schuss das Ziel getötet hat | 21 |
| 541 | SpecialPowerSnapshot (new ReplayHeader(…, 30, …)) | H/rate | 1 | 1 Stelle: Literal 30 als ReplayHeader.TickRate (dritter Parameter, ReplayFormat.cs:20) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.StagedCapture.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 22-25 | Tests.StagedUnpack/StagedPrepare/StagedPack | H/zeit | 4 | 4 Stellen: Konstanten 30/60/15 Ticks = UnpackTime 1000 ms, PreparationTime 2000 ms, PackTime 500 ms; als exakte Stufenlängen geprüft (Z. 176-178, 238, 270, 338) | 21 |
| 153,165,267 | Tests.StagedWorld / StagedCaptureRunsThroughItsStages / StagedCaptureAbortRules | H/zeit | 3 | 3 Stellen (nicht in der Trefferliste): 40 bzw. 35 Schritte warten das Aufladen von 1000 ms (30 Ticks) ab; StagedPack + 10 als Beobachtungsfenster | 21 |
| 170,210,235,255,278,287,297,352,365,376,413 | Tests.ObserveCapture-Aufrufe / StagedWorldAt / RealStagedCapture | H/budget | 11 | 11 Stellen (nicht in der Trefferliste): Laufbudgets 600/400/200/300/50/2400 Ticks | 21 |
| 184 | Tests.StagedCaptureRunsThroughItsStages | H/ticks | 1 | 1 Stelle: Besitzerwechsel im ersten Pack-Tick (captured.Tick == pack.Start), reine Reihenfolge | 21 |
| 197,198 | Tests.StagedCaptureRunsThroughItsStages | H/zeit | 2 | 2 Stellen: StagedPack + 30 = Nachladezeit ReloadTime 1000 ms als Literal 30 Ticks | 21 |
| 222,223 | Tests.StagedCaptureFlashesTheBuilding | H/zeit | 2 | 2 Stellen: genau 4 CaptureFlash-Ereignisse über 60 Vorbereitungs-Ticks (Phase wächst je Tick um Anteil/3: 59*60/2/60/3 = 9,83) | 21,23 |
| 242 | Tests.StagedCaptureFacesItsTargetAndLotusStartsFromAfar | H/zeit | 1 | 1 Stelle: unpack.Start >= 8 (90 Grad bei 360 Grad/s = 7,5 Ticks) | 21 |
| 425-427,438,439 | Tests.RealStagedCapture | H/zeit | 5 | 5 Stellen (nicht in der Trefferliste): Erwartungstabelle Retaildaten in Ticks: 90/600/60 (Ranger, Redguard, Rebel) und 202/180/84 (Black Lotus: 6730/6000/2800 ms, aufgerundet) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.State.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 22,61,95,98,135,155,262,271,338,341,425,430 | Tests.ShortOptions / HashDeterminism / SnapshotRoundTrip / SnapshotRejection / ReplayRuns / ShortStress / IdleAllocation / RealData | H/budget | 12 | 12 Stellen (nicht in der Trefferliste): Lauflängen und Stopp-Ticks 3000/60/1500/600/1234/12000/100/300/400, dazu "tick":600 im Snapshottext | 21 |
| 126 | Tests.SnapshotRoundTrip | H/budget | 1 | 1 Stelle: 30 Schritte Hashvergleich nach Restore | 21 |
| 153 | Tests.SnapshotRejection | H/rate | 1 | 1 Stelle: Snapshottext wird von "tickRate":30 auf "tickRate":60 umgeschrieben und muss abgelehnt werden (Literal 30 und 60) | 20,21 |
| 221 | Tests.ReplayFormatTests | H/rate | 1 | 1 Stelle: Replay-Header mit TickRate = 60 gilt als fremde Rate und muss ReplayRejectedException auslösen | 20,21 |
| 315,323,325 | Tests.LongRun | H/budget | 3 | 3 Stellen (nicht in der Trefferliste): 216.000 Ticks als 'two hours of game time', Erwartung TickHashes.Length == 216_000, Stopp bei 108.000 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 253-260,279-281 | Tests.StructuresFixtureIni: RailroadBehavior SpeedMax/Acceleration/Braking/Friction/RunningGarrisonSpeedMax/KillSpeedMin | D | 9 | 9 Stellen: SpeedMax 3, Acceleration 1.1, Braking 0.9, Friction 0.995, RunningGarrisonSpeedMax 1, KillSpeedMin 1 werden roh je Frame gelesen (RailroadModules.cs Z. 33-35, ModuleConfigReader.Real) | 20,23 |
| 735,746,769,785,786,788,826,835,869,870,880,891,913,927,939,948,952,1045,1097,1100,1104,1223,1225,1256,1257,1260,1314 | Tests.Structures*-Methoden (Laufbudgets) | H/budget | 27 | 27 Stellen (nicht in der Trefferliste): Schleifengrenzen und Nachläufe 2..700 Ticks ohne exakte Zeitaussage (z. B. 200 Einsturz, 700/400/260/120 Zug, 250 Restore-Vergleich) | 21 |
| 813,838,839,843,849,852,854,995,1000,1050-1052,1072,1074,1078,1087,1089,1091,1112,1117,1138,1141,1145,1154,1156,1160,1162,1177,1181,1214,1219,1221 | Tests.Structures*-Methoden (Run(n)-Wartezeiten) | H/zeit | 32 | 32 Stellen (nicht in der Trefferliste): sw.Run(n) als Zeit: 40 (Spread 500 ms), 20/2/12 (UnpackTime 30 Ticks), 8/25 (PackTime 15), 40 (Delay 1000 ms), 10/40/40 (OCL 1000 ms), 45/60 (Shroud), 60/90/60/100 (Firestorm), 40/25 (Bonus), 60/5/20/80 (WaveDelay 3000 ms) | 21 |
| 909,910 | Tests.StructuresTrainsRunTheirTrackAndHaulCarriages | H/rate | 2 | 2 Stellen (nicht in der Trefferliste): maxSpeed <= 3 und > 2 vergleicht die Geschwindigkeit je Tick mit dem Rohwert SpeedMax | 21,23 |
| 998,1115,1179 | Tests.StructuresMinesDetonateOnceAndRegenerate / StructuresWorldEffects / StructuresWeaponBonusAndCostModifier | H/zeit | 3 | 3 Stellen: Wartezeiten 100 (Heilung 25 je s), 30 (Schrumpfphase nach GrowTime 1000 ms + ShrinkDelay 2000 ms), 30 (BonusDuration 1000 ms) | 21 |
| 1015,1017 | Tests.StructuresMinesDetonateOnceAndRegenerate | H/rate | 2 | 2 Stellen: Run(30 * 3) und Run(30 * 8) = 3 s und 8 s mit Literal 30 als Ticks je Sekunde (CreatorDeathCheckRate 1000 ms, Degeneration 20 % je s) | 21 |
| 1070 | Tests.StructuresBridgesTowersAndScaffolds: BridgeScaffoldBehavior.SetSpeeds | H/rate | 1 | 1 Stelle (nicht in der Trefferliste): SetSpeeds(4, 3) übergibt Geschwindigkeiten je Tick direkt (das Modul teilt Datenwerte sonst durch LogicFramesPerSecond) | 21 |
| 1317,1337 | Tests.RealStructuresAndWorld | H/budget | 2 | 2 Stellen: 900 Ticks nach dem Töten (Einsturz muss fertig sein), 450 Ticks Zugfahrt | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Supply.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 282,302,318,372,387,407,417,438,441,450,453,459,466 | Tests.Supply*/SpawnBehavior*-Methoden | H/budget | 13 | 13 Stellen (nicht in der Trefferliste): Laufbudgets 4000/600/3000/130/400/300/40/200/4 Ticks | 21 |
| 287 | Tests.SupplyEconomyGoldenMaster | H/zeit | 1 | 1 Stelle: Abstand zweier Einzahlungen > 6 Ticks (zwei Aktionsverzögerungen von je 100 ms = 3 Ticks) | 21 |
| 356,358,360,367 | Tests.SupplyAutoDeposit | H/zeit | 4 | 4 Stellen (356 Treffer): absolute Ticks 30/31/61/91 als Periodengrenzen von DepositTiming 1000 ms | 21 |
| 396 | Tests.SupplySnapshotAndReplay | H/rate | 1 | 1 Stelle: ReplayHeader mit Tickrate-Literal 30 | 20,21 |
| 431,433 | Tests.SpawnBehaviorReplaceAndRequireSpawner | H/zeit | 2 | 2 Stellen: deathTick + 70 (Ersatz nicht vor SpawnReplaceDelay 2000 ms = 60 Ticks plus 15-Tick-Abfragerhythmus) und + 140 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Teams.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 101,118 | Tests.TeamsRelationshipMatrix / KillPrize | H/budget | 2 | 2 Stellen (101 Treffer): 30 Schritte Hashvergleich, 200 Schritte bis der Preis stirbt | 21 |
| 105 | Tests.TeamsRelationshipMatrix | H/rate | 1 | 1 Stelle: ReplayHeader mit Tickrate-Literal 30 | 20,21 |

**`tests/CnCNew.Simulation.Tests/Tests.Terrain.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 130 | Tests.TerrainInWorldSnapshotAndReplay | H/rate | 1 | 1 Stelle: ReplayHeader mit Tickrate-Literal 30; der Replay wird danach ausgeführt (TicksExecuted == 10) | 20,21 |

**`tests/CnCNew.Simulation.Tests/Tests.TimeFreeze.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 20,41,46 | Tests.ScriptFreezeTime* | H/budget | 3 | 3 Stellen (nicht in der Trefferliste): Schleifengrenzen 200/5/20 Schritte | 21 |
| 27 | Tests.ScriptFreezeTimeStopsTheWorldNotTheScripts | H/zeit | 1 | 1 Stelle: steps zwischen 28 und 40 für SET_MILLISECOND_TIMER mit 1 s ('its 30 script updates') | 21 |
| 28,31,58 | Tests.ScriptFreezeTimeStopsTheWorldNotTheScripts / ScriptFrozenTimeSurvivesSaveAndEnds | H/ticks | 3 | 3 Stellen (58 Treffer): Tick == frozenAt + 1 / + 2 / tick + 1: ein Schritt weiter nach dem Auftauen | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Transports.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 30-33,37-40 | Tests.TransportFiles: Locomotor WalkLoco/RotorLoco | H/rate | 8 | 8 Stellen (37 nicht in der Trefferliste): Fixturewerte je Sekunde als Vielfache von 30/900 gewählt: Speed 30 und 150 (1 bzw. 5 je Tick), Acceleration/Braking 900 (1 je Tick²), TurnRate 3600 | 21 |
| 243,244,262,264 | Tests.TunnelHealingAndCaveIn | H/zeit | 4 | 4 Stellen (243, 244 Treffer): TimeForFullHeal 1000 ms = 30 Ticks: nach 6 Ticks nicht heil, nach 5 + 25 weiteren heil | 21 |
| 260 | Tests.TunnelHealingAndCaveIn | H/rate | 1 | 1 Stelle: sliver = 100 / 30 als Heilmenge je Tick und Tunnel (Literal 30 = Ticks in 1000 ms) | 21,23 |
| 290,306,311,338,363,393,425,449,451,465-468 | Tests.Tunnel*/Transport*/Chinook*/Helix*-Methoden | H/budget | 13 | 13 Stellen (nicht in der Trefferliste): Schleifengrenzen 200/300/2000/100/240 Ticks, Befehls- und Snapshot-Zeitpunkte bei Tick 120/100/130 | 21 |
| 307,371,386 | Tests.TransportLoadAndUnloadAtPosition / ChinookSupplyFlightAndLanding / HelicopterSlowDeathFall | H/zeit | 3 | 3 Stellen (nicht in der Trefferliste): world.Tick > 90 (100 Einheiten bei 1 je Tick), 25 Ticks Steigen zurück auf 100, 20 Ticks bis Flughöhe | 21 |
| 317,328,329,331 | Tests.ChinookSupplyFlightAndLanding | H/rate | 4 | 4 Stellen (317, 329 Treffer): Steigen mit 5 je Tick: nach 19 Schritten Z == 95, nach 20 Schritten Z == 100 | 21,23 |
| 376-378,398,405 | Tests.HelicopterSlowDeathFall | H/zeit | 5 | 5 Stellen (376 Treffer): handgerechnet: Gravitation -64/s² = -4660 roh je Tick², Fallweg 2330 * n(n+1)/2, Aufschlag bei death + 75; DelayFromGroundToFinalDeath 300 ms = 9 Ticks, Ende bei hit + 10 | 21,23 |
| 400 | Tests.HelicopterSlowDeathFall | H/budget | 1 | 1 Stelle: Schleifengrenze 30 Ticks bis zum Verschwinden (erwartet nach 10) | 21 |
| 403 | Tests.HelicopterSlowDeathFall | H/ticks | 1 | 1 Stelle: gone = world.Tick - 1 (der gerade ausgeführte Tick) | 21 |
| 470 | Tests.TransportSnapshotAndReplay | H/rate | 1 | 1 Stelle: ReplayHeader mit Tickrate-Literal 30 | 20,21 |

**`tests/CnCNew.Simulation.Tests/Tests.TurretAttack.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 174,178,189,199,210,243,270,290,304,313,326,364,390,399,426,430-433,440,447-449,461,462,482,510,523,526,530,537,545,554,556,564,567,570,573,650,662 | Tests.TurretAttack*/TurretIdleScan*-Methoden | H/budget | 42 | 42 Stellen (nicht in der Trefferliste): Schleifengrenzen 10..1500 Ticks, Befehlszeitpunkte in Ticks (Tick 3, 60..90, 150, 260; Replay 70..110, 130), 'ab Tick 10/60' für die Zielfehlermessung, 'i > 140' | 21 |
| 218,222,225,231,242,249,258,259 | Tests.TurretAttackSurvivesGapsChaseAndAnOrderEndsIt / TurretAttackEndsWhenTheVictimDiesOrIsLost | H/zeit | 8 | 8 Stellen (259 Treffer): RecenterTime 500 ms als 16 Halte-Ticks, TicksUntilShot == 15 (90 Grad bei 6 Grad je Tick), 40 bzw. 90 Ticks Rückdrehen mit halber Rate (3 Grad je Tick) | 21 |
| 261,278,297 | Tests.TurretAttackEndsWhenTheVictimDiesOrIsLost | H/zeit | 3 | 3 Stellen (nicht in der Trefferliste): Aufgabe des Ziels 44..47 Schritte nach Verlassen der Reichweite ('loses interest after 45 ticks', Konstante der Simulation) | 20,21 |
| 359,385,626,627,630 | Tests.TurretAttackEndsWhenTheVictimIsNoLongerVisible / TurretAttackEndsWhenTheVictimFallsIntoTheFog / RealTurretAttackAndScan | H/budget | 5 | 5 Stellen: 30 Schritte 'solange sichtbar', Idle(name, 1800) = 60 s Leerlauf mit Erwartung Scans >= 3 | 21 |
| 464 | Tests.TurretAttackSnapshotAndReplay | H/rate | 1 | 1 Stelle: ReplayHeader mit Tickrate-Literal 30; der Replay wird ausgeführt | 20,21 |
| 476,514,516,518 | Tests.TurretIdleScanGlancesAroundDeterministically | H/zeit | 4 | 4 Stellen (476, 516 Treffer): Scanabstand 30..60 Ticks (1000..2000 ms), Haltezeit 15..17 Ticks (RecenterTime 500 ms), Ausflug höchstens 31 + 16 + 31 Ticks (90 Grad bei 3 Grad je Tick) | 21 |
| 599 | Tests.RealTurretAttackAndScan | H/rate | 1 | 1 Stelle (nicht in der Trefferliste): MinScanIntervalTicks == 8 und MaxScanIntervalTicks == 8 (250 ms aufgerundet) | 21 |
| 660 | Tests.RealTurretAttackAndScan.Chase | H/ticks | 1 | 1 Stelle: Feuerbefehl nur in jedem zweiten Tick (tick % 2 == 0) als Eingaberhythmus des Tests | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Turrets.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 18,295,306,308,345,356,388,395,401 | Tests (Klassenkommentar) / TurretsTurnAtTheirRateAndTheShotWaits / TurretsHoldRecentreAndRest / TurretsTwoTurretsPitchAndSweep | H/zeit | 9 | 9 Stellen (18, 295, 306, 388, 401 Treffer): 180 Grad/s = 6 Grad je Tick, 500 ms = 15, 300 ms = 9 Ticks; shotAfter == 15, TurretWaits == 15, 22 Halteschritte, 8..10, 3..4, >= 30 Ticks | 21 |
| 236 | Tests.TicksUntilShot | H/ticks | 1 | 1 Stelle: Helfer befiehlt je Tick einen Schuss und liefert die Zahl der Ticks bis zum Schuss (limit 80) | 21 |
| 261 | Tests.TurretsTemplateModelAndStrictParsing | H/rate | 1 | 1 Stelle: TurnRate.Raw zwischen 6860 und 6866 ('180 are 6 degrees per tick') neben dem Vergleich über die Umrechnungsfunktion | 21 |
| 262,265,266 | Tests.TurretsTemplateModelAndStrictParsing | H/rate | 3 | 3 Stellen (266 Treffer): RecenterTicks == 15 (500 ms), Vorgaben TurnRate/PitchRate == 0.01 rad je Frame und RecenterTicks == 60 (zwei Sekunden) aus TurretInfo.DefaultRate | 20,21 |
| 304 | Tests.TurretsTurnAtTheirRateAndTheShotWaits | H/ticks | 1 | 1 Stelle: nach einem Tick ist der Winkel genau eine Rate je Tick (über die Umrechnungsfunktion) | 21 |
| 349,358,366,374,376,415,422,428-430,486 | Tests.Turrets*-Methoden | H/budget | 11 | 11 Stellen (nicht in der Trefferliste): Schleifengrenzen 100/200/4/10/120/7/150/90 Ticks, Bedingung world.Tick < 60 | 21 |
| 500,504,512,514 | Tests.RealTurrets.Duel | H/zeit | 4 | 4 Stellen: Ticks seit Start bis zum ersten Schuss; Crusader 7..9 Ticks (45 Grad bei 6 Grad je Tick), Battlemaster 0 | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Veterancy.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 179 | Tests.KillDummy | H/budget | 1 | 1 Stelle (nicht in der Trefferliste): höchstens 200 Schritte bis das Ziel stirbt | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.Vision.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 112 | Tests.VisionSettle | H/ticks | 1 | 1 Stelle (nicht in der Trefferliste): läuft SimulationWorld.VisionIntervalTicks (= 3) Schritte, bis die Sicht neu berechnet ist | 21 |
| 225 | Tests.VisionSnapshotAndDeterminism | H/budget | 1 | 1 Stelle: 30 Schritte Hashvergleich nach Restore (dazu 20 und 5 Schritte in Z. 213, 219) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.WeaponSlots.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 16,331,337,338,409,427,430,431,438,439,440,453-457,496 | Tests (Klassenkommentar) / WeaponSlotsBothSlotsFireAtDifferentTargets / WeaponSlotsPreferredWeaponWaitsWhileReloading / WeaponSlotsIndependentClipsAndReload / WeaponSlotsSharedReloadTime / WeaponSlotsExplicitSlotAndWeaponLock | H/zeit | 17 | 17 Stellen (16, 438 Treffer): '30 ticks per second: 500 ms = 15, 100 ms = 3, 200 ms = 6, 2000 ms = 60, 3000 ms = 90'; ReadyTick == start + 15, + 1 + 3, + 15 + 60, Tick - 1 + 6, + 3 + 15; Warteschleifen bis start + 15 / + 75; 15 Schritte | 21 |
| 334 | Tests.WeaponSlotsBothSlotsFireAtDifferentTargets | H/ticks | 1 | 1 Stelle: 'One tick later' – zweiter Befehl im nächsten Schritt | 21 |
| 551,602,603 | Tests.WeaponSlotsSnapshotMidReloadAndDeterminism / WeaponSlotsCommandsRoundTripInReplays | H/ticks | 3 | 3 Stellen: Eingabemuster des Tests über Tick-Modulo (Tick % 40 == 10, tick % 20 == 7, tick % 3, tick % 5) | 21 |
| 556,564-566,600,604,605,687,690 | Tests.WeaponSlotsSnapshotMidReloadAndDeterminism / WeaponSlotsCommandsRoundTripInReplays / WeaponSlotsScriptCommandButtonsHonourTheSlot | H/budget | 9 | 9 Stellen (nicht in der Trefferliste): Lauflängen 40/200/150 Ticks, Befehle bei Tick 60 und 100, Run(session, 3) | 21 |
| 608,620 | Tests.WeaponSlotsCommandsRoundTripInReplays | H/rate | 2 | 2 Stellen: ReplayHeader mit Tickrate-Literal 30; der erste Replay wird ausgeführt | 20,21 |
| 683 | Tests.WeaponSlotsScriptCommandButtonsHonourTheSlot | H/ticks | 1 | 1 Stelle: MissionOptions.CombatIntervalTicks = 0 (Option in Ticks; der Wert 0 schaltet das Intervall ab) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.WeaponSlotsIntegration.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 175,186,205,207,223,236,257,263,275,285,304,376 | Tests.WeaponSlots*-Methoden (Integration) | H/budget | 12 | 12 Stellen (nicht in der Trefferliste): Schleifengrenzen 200/40/100/20/60/3/10 Ticks | 21 |
| 255,310 | Tests.WeaponSlotsFireOclAfterCooldownWatchesTheSecondarySlot / WeaponSlotsAssistedTargetingUsesTheRealSlot | H/zeit | 2 | 2 Stellen (nicht in der Trefferliste): 12 Schritte bis die Waffe wieder hätte feuern können (DelayBetweenShots 300 ms = 9 Ticks); Schussabstände == 3 und == 15 Ticks | 21 |
| 272 | Tests.WeaponSlotsFireOclAfterCooldownWatchesTheSecondarySlot | H/ticks | 1 | 1 Stelle: ein Schritt, weil der Beobachter den Schuss einen Tick später zählt | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.WeaponSlotsReal.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 74,80,98 | Tests.RealWeaponSlots-Lauf | H/budget | 3 | 3 Stellen (nicht in der Trefferliste): höchstens 40 bzw. 120 Feuerversuche, 20 Schritte Pause zwischen den Zielen | 21 |
| 105 | Tests.RealWeaponSlots-Lauf | H/ticks | 1 | 1 Stelle: ReadyTick > world.Tick - 1 (bezogen auf den gerade ausgeführten Tick) | 21 |

**`tests/CnCNew.Simulation.Tests/Tests.World.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 151,212,291,303,387,417,443,492,507,529 | Tests.Lifecycle / BodyAndDieModules / LifetimeAndDeletion / UpdateOrderAndMutation / TimerBehaviour / CommandValidationAndOrder | H/ticks | 10 | 10 Stellen (212, 291, 303, 417, 492, 507, 529 Treffer): Schedulersemantik: Step führt Tick 0 aus, Befehl für Tick + 1, Lebensdauer 0 wird ein Tick (removed.Tick == 1), Updatefolge (Wecktick, Id, Modulindex), Timer nach (Fälligkeit, Id), verspäteter Tick | 21 |
| 275,277-279 | Tests.LifetimeAndDeletion | H/zeit | 4 | 4 Stellen (275 Treffer): Referenzziehung reference.Range(15, 45) und Range(9, 27) = 500..1500 ms und 300..900 ms in Ticks; Todeszeitpunkt muss exakt stimmen | 21 |
| 281 | Tests.LifetimeAndDeletion | H/budget | 1 | 1 Stelle (nicht in der Trefferliste): Schleife bis Tick 60 deckt die längste Lebensdauer (45 Ticks) ab | 21 |
| 305 | Tests.LifetimeAndDeletion | H/rate | 1 | 1 Stelle (nicht in der Trefferliste): DieTick == 128849019 (-1 als 4294967295 ms, aufgerundet auf Ticks bei 30 Hz) | 21 |
| 328-366,384,395,407,438-442,444-476 | Tests.ProbeWorld / UpdateOrderAndMutation / TimerBehaviour | H/ticks | 1 | Etwa 30 Stellen (nicht in der Trefferliste): Probe-Fixture FirstWake/Sleep/DestroyAtTick/EmitAtTick und Timerverzögerungen (5, 3, 4, 2, 50) sind absichtlich Ticks eines Testmoduls bzw. des SetTimerCommand | 21 |

**`tests/CnCNew.Ui.Tests/TextAndSettingsTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 276,277 | TextAndSettingsTests.InGameUiSettingsAreTypedFromTheGraph (SuperweaponFlashFrames, FloatingTextTimeOutFrames, FloatingTextMoveUpPerFrame, FloatingTextVanishPerFrame) | H/zeit | 2 | 2 Stellen: erwartet 500 ms = 15 Logikframes, 333 ms = 10 Frames (aufgerundet), 30 je Sekunde = 1 je Frame, 3 je Sekunde = 0,1 je Frame | 21 |

**`tests/CnCNew.Ui.Tests/TransitionTests.cs`**

| Zeilen | Test/Symbol | Kl. | n | Was | Phase |
|---|---|---|---|---|---|
| 120,150,154,199,208,345,465,494,636,759 | TransitionTests.Group/One/Table-Fixtures (FrameDelay = n) | H/ticks | 10 | 10 Stellen: FrameDelay-Werte der synthetischen WindowTransitions-Blöcke (0, 1, 2, 3, 5, 6, -1) in Übergangsbildern – Originalbild, nicht Simulationstick | 21 |
| 229,353,354,366,370,400,425,488,530 | TransitionTests (handler.Step()-Erwartungen je Bild) | H/ticks | 9 | 9 Stellen: bildgenaue Erwartungen je WindowTransitionHandler.Step(): FLASH 8 Bilder, SCREENFADE 30 Bilder (Alpha 1/(30-1) je Bild), Skalierung je Bild, ein Zählton je Bild – Originalbild, nicht Simulationstick | 21 |
| 695,698,699 | TransitionTests (handler.Advance(seconds)) | H/rate | 3 | 3 Stellen: Advance(0.1) ergibt 3 Bilder ('3 frames at 30 per second'), 30 Aufrufe mit 1.0/60 ergeben 15 Bilder – Takt der Übergänge auf Wandzeit, Originalbild, nicht Simulationstick | 21 |

## 8. Suchmuster (zum Wiederholen)

Über `src`, `tests`, `tools`, nur `*.cs`, ohne `bin`, `obj`, `.claude/worktrees`; zeilenweise, .NET-/Python-Regex. In Klammern Trefferzeilen / Dateien am geprüften Commit. P01–P16 sind die Muster des Startinventars, N01–N17 kamen in dieser Prüfung dazu. „Stark“ (für die treffergetriebenen Pakete verbindlich) sind alle außer N11, N12, N15, N16.

```
P01 (85/44)  LogicFramesPerSecond
P02 (35/29)  (PerSecond|FramesPerSec|TicksPerSec|FrameRate|TickRate|Fps)\w*\s*=\s*\d
P03 (111/23)  (1(\.0)?[dfm]?\s*/\s*30(\.0)?[dfm]?\b|/\s*30(\.0)?[dfm]\b|\*\s*30(\.0)?[dfm]\b|30m?\s*/\s*1000|/\s*33\b|33\.33|1000\s*/\s*30)
P04 (748/157)  \b30(\.0)?[fdmL]?\b|\b900m?\b|\b3600\b|\b1800\b|\b450\b
P05 (416/144)  const\s+(int|long|uint|ulong|short)\s+\w+\s*=\s*[^;]*;
P06 (73/47)  (long|int)\s+\w*(Tick|Ticks|Frames|Interval|Cadence|Every|Spacing)\w*\s*(\{[^}]*\})?\s*=\s*[\d_]+
P07 (18/11)  (Tick|tick|_life|frame\w*|_age)\s*%\s*\d+
P08 (79/29)  Tick\s*[-+]\s*[^;]*?(>=|<=|>|<|==)\s*\d+|Tick\s*[-+]\s*\d+
P09 (144/63)  return\s+(world\.Tick\s*\+\s*)?\d+L?\s*;
P10 (107/33)  ModuleConfigReader\.Ticks\([^)]*,\s*[^0)\s][^)]*\)
P11 (25/15)  ModuleConfigReader\.(Int|Real)\(configuration,\s*"[^"]*(Time|Delay|Rate|Frame|Duration|Interval|Life|Wait|Speed|PerSecond)[^"]*"
P12 (49/23)  \*=|(Speed|Velocity|Rate)\s*=\s*[^;]*\*\s*\w*(Damp|Friction|Factor|Decay|Braking|Acceleration)
P13 (8/5)  LogicRandom\.Range\((0|1),\s*\d+\)
P14 (76/56)  (?i)(per frame|every frame|\(frames\)|per logic frame)
P15 (95/35)  writer\.Int\("\w*(Tick|Frame|Until|At|Timer|Delay|Expire)\w*"
P16 (83/31)  GameTime|ElapsedInState|DeltaSeconds|TotalSeconds|W3dAnimationClock
N01 (573/81)  SimulationTiming\.\w+
N02 (107/33)  ModuleConfigReader\.(Ticks|Duration\w*|Milliseconds\w*|Seconds\w*|PerSecond\w*|Velocity\w*|Angular\w*|DegreesPerSecond\w*)\(
N03 (0/0)  return\s+[^;]*\bTick\s*\+
N04 (495/132)  \b(long|int|uint)\??\s+_?\w*(?i:nextwake|ready|until|expire|deadline|frame|tick|timer|countdown|cooldown|delay|timeout|lastshot|lasttime|starttime|endtime|wake|when)\w*\s*(;|=|\{)
N05 (89/53)  %\s*(\d+|\w*(Interval|Ticks|Frames|Period|Rate|Cadence)\w*)\b
N06 (439/166)  (?i)\b(per tick|per-tick|each tick|every tick|one tick|a tick|ticks? (of|per)|logic frames?|LOGICFRAMES|frames? per|per second|/s\b|30 ?hz|30 fps|1/30)
N07 (379/105)  \b(Tick|tick|Frame|frame|now)\s*(\+|-)\s*\w
N08 (389/112)  \b\w*(Frames|Ticks|Interval|Period|Cadence|Latency|Lifetime|Duration|Delay|Cooldown|Timeout)\w*\s*=\s*-?\d
N09 (444/56)  (reader|state|r)\.(Int|Long|Int64)\w*\("\w*(?i:tick|frame|until|timer|delay|expire|wake|ready|when|time|next|last|start|end|deadline|cooldown)\w*"
N10 (246/56)  writer\.(Int|Long|Int64|Fixed)\w*\("\w*(?i:tick|frame|until|timer|delay|expire|wake|ready|when|time|next|last|start|end|deadline|cooldown)\w*"
N11 (1001/146)  \.Tick\b|\bTick\b\s*[<>=!]|CurrentTick|world\.Tick
N12 (1005/140)  Fixed\.From(Ratio|Int|Decimal)\([^)]*\)\s*(;|,|\))|FromRatio\(
N13 (385/92)  (?i)(damp|friction|decay|drag|braking|stiffness|falloff)
N14 (89/29)  LogicRandom\.\w+\(
N15 (907/70)  (?i)(sleep|wake)
N16 (1900/181)  \b(15|45|60|90|120|150|300|600)L?\b
N17 (153/57)  (float|double)\s+\w*(dt|Dt|delta|Delta|elapsed|Elapsed|Seconds)\w*|Stopwatch|DateTime|UpdateTime\.|Elapsed\.|\.TotalMilliseconds
```

Erfahrung aus dieser Prüfung: (1) N03 (`return world.Tick + N`) trifft nichts – `IUpdateModule.Update` gibt die Schlafdauer relativ zurück, P09 ist das richtige Muster. (2) Die meiste Kopplung der Klassen C und E treffen die Muster **nicht**: Schleifen `for (i < N) world.Step()`, Zähler `--`/`++` je Update, Vergleiche einer Geschwindigkeit je Tick mit einem Abstand, `Math.Max(x, SleepNone)`. Dafür hilft nur das Lesen der `Update`-Methoden. (3) In den Tests treffen die Muster fast nur Fixture-INI in Millisekunden (nicht gekoppelt); die Tickerwartungen stehen in Zeilen wie `== 9`, `Run(45)`, `StepUntil(…, 300)`. (4) Für Phase 20 Punkt 7 (`SourceRules`) taugen P01, P03, P06, P07, P10 und ein neues Muster für `/ LogicFramesPerSecond` außerhalb von `SimulationTiming`; P04/N16 sind als Regel zu laut.

## Anhang A: geprüft, nicht gekoppelt

**Vollständig gelesene Dateien** – Trefferzeilen ohne eigene Tabellenzeile (ohne die mechanisch gezählten Umrechnungen):

| Projekt | Trefferzeilen ohne Kopplung |
|---|---|
| `CnCNew.Simulation` | 1952 |
| `CnCNew.Game` | 97 |
| `CnCNew.Presentation` | 199 |
| `CnCNew.Ui` | 9 |
| `CnCNew.Client.Stride` | 5 |
| `CnCNew.Data` | 1 |
| übrige src | 6 |
| tools | 67 |
| tests | 146 |
| **Summe** | 2482 |

Gründe, gruppiert (die Dateien wurden ganz gelesen, die Zuordnung der einzelnen Zeile zu einem Grund ist nicht einzeln protokolliert): Rückgaben `SleepNone`/`SleepForever` und `return 0;` ohne Zeitbedeutung; `NextWake`-Verdrahtung; Zeilen des Zustandslesens und -schreibens (Felder in Anhang C); Abstände, Radien, Prozentwerte und Winkel über `Fixed.FromInt/FromDecimal/FromRatio`; Zahlen 15/30/45/60/90/120/150/300/600/900 als Gesundheit, Kosten, Abstand, Winkel in Grad, Kartenmaß oder Tabellenindex; Enum-, Masken- und Versionskonstanten; `%` auf IDs, Indizes und Zufallswerten; Kommentare, die „per second“/„frames“ nur erwähnen; Vergleiche des Ticks als Ordnungsschlüssel (Sortierung von Befehlen, Ereignissen, Prüfpunkten); Wandzeit-Variablen der Präsentation (`deltaSeconds`, Millisekunden-Uhren). Vom Koordinator Zeile für Zeile gesehen (Trefferzeilen mit Umgebung): Festkomma- und Hash-Konstanten, Slot-/Masken- und Enum-Konstanten, `return 0/1/2/3` von Index- und Maskenfunktionen, Abstände/Vektoren/Prozentwerte über `Fixed.FromDecimal`, Winkel über `DegreesToRadians`, Tick als Ordnungsschlüssel im Replay, Doku-Kommentare.

**Treffergetriebene Pakete und Gruppen aus den vollständig gelesenen Paketen** – je Zeile Datei, erfasste Trefferzeilen und Grund:

*CnCNew.Simulation* (737 Trefferzeilen in 32 Gruppen)

- `Sim\Commands/Commands.cs` (16,33,74,647,671): Treffer sind Typ- und Ablehnungscodes (15, 30) und die Tabellenversion 14, keine Zeitwerte
- `Sim\Events/SimEvent.cs` (34,103,175-193): Treffer sind Enum-Werte und Grundcodes (15, 60, 1–10), keine Zeitwerte
- `Sim\Mission/MissionSession.cs` (34): Standard 3, heißt „Ticks“, ist aber laut Doc und Z. 78 nur noch ein Schalter (0 = Selbstzielwahl aus); Name und Snapshot-Feld bleiben bestehen
- `Sim\Mission/MissionSession.cs` (232): Ringradius + 30 Welteinheiten für Starteinheiten, kein Zeitwert
- `Mod\ModuleConfigReader.cs` (23-37,47-57): Generische Rohzugriffe ohne Einheitenumrechnung (Zahl, Distance, Percentage, Money); die Kopplung entsteht erst beim Aufrufer
- `Mod\WorldEffectModules.cs` (785): Mindestens 1 Tick Startverzögerung: echte Tick-Semantik (ein Update vor dem Start), keine Zeitangabe
- `Sim\Modules/CollideModule.cs` (1-19): Vollständig gelesen: nur Schnittstelle, keine Tick- oder Zeitwerte
- `Sim\Modules/ModuleBindings.cs` (1-88): Vollständig gelesen: Bindungstabelle, keine Tick- oder Zeitwerte
- `Sim\Modules/ModuleFramework.cs` (24,62,182,214-215,282-285,324,335): Schnittstellen tragen Ticks (Tick, WakeAt, BaseRegenDelayTicks, Shared-Ready-Frame, PowerSabotagedUntil, ParalyzeTroops untilTick, CreateFromListWithLifetime lifetimeTicks), enthalten aber keine Werte oder Umrechnungen
- `Sim\Modules/ModuleFramework.cs` (553-560): Vertrag: Update liefert relative Schlafticks (mindestens 1) oder SleepForever, NextWake ist ein absoluter Tick; in dieser Datei gibt es keinen Scheduler und keine Staffelung (nur Schnittstellen)
- `Sim\Modules/UpgradeMux.cs` (1-92): Vollständig gelesen: Zustandsautomat ohne Zeitbezug (Upgrade-Kauf wirkt sofort)
- `Sim\Scripting/ScriptEngine.Sequential.cs` (20): Schleifenschutz 256 Anweisungen je Runner und Tick: kein Zeitwert
- `Sim\Scripting/ScriptEngine.Settings.cs` (61): Int-Parameter roh aufgezeichnet; im Original Sekunden (kein Frame-Wert), hier ohne Verbraucher
- `Sim\Scripting/ScriptEngine.TeamAbilities.cs` (249,254): Zwischenspeicher der Team-Zuordnung, je Tick neu aufgebaut (Vergleich mit world.Tick nur als Gültigkeitsmarke)
- `Sim\Scripting/ScriptEngine.cs` (75,77): Angriffspriorität 1 und Rekursionsgrenze 32: keine Zeitwerte
- `Sim\Skirmish/SkirmishAi.cs` (70,740,781,783,1040,1043,1106,1193): Treffer auf 30/450/900/3600/90_000 sind Abstände bzw. Abstandsquadrate in Welteinheiten (DefenseRadius, _badSpots, Lagerabstand, Ankunftsradius), keine Ticks; ebenso MaxAttackThreshold = 30 (Einheitenzahl)
- `Sim\Skirmish/SkirmishMatchSession.cs` (191,193,196,208,225): Startbefehle mit Tick 0 (absolute Adresse des ersten Ticks) und Ringradius + 30 Welteinheiten; keine Zeitwerte
- `Sim\Terrain/NavigationGrid.cs` (37,53,198-220): Temporäre Blocker gelten nur für eine Suche (finally löscht sie), keine Lebensdauer in Ticks; ExpansionBudget 200_000 gilt je Suche, nicht je Tick; Kosten 10/14 sind Wegkosten
- `Sim\Terrain/TerrainMap.cs` (1-313): Vollständig gelesen: nur Geometrie (CellSize 10, MaxLineOfSightSamples 64), keine Tick- oder Zeitwerte
- `Sim\Terrain/VisionMap.cs` (43-48): Sicht wird je Aufruf vollständig neu aufgebaut; die Karte hat keinen eigenen Takt, keinen Tickzähler und keine Abklingzeit (der Takt liegt beim Aufrufer in der Welt)
- `Sim\Testing/ObjectiveDriver.cs` (19): MaxDepth = 28 ist die Rekursionstiefe des Planers, keine Tickzahl
- `Sim\Testing/ObjectiveDriver.cs` (405): Diagnosetext gibt die Skriptverzögerung in Sekunden aus den Kartendaten aus, keine Tickrechnung
- `Sim\Testing/ReferenceMatchScenario.cs` (269): Prüft nur, ob eine Bauzeit vorhanden ist; keine Rechnung mit der Tickzahl
- `Sim\Testing/StressScenario.cs` (126,161,173): Die 30er/20er sind Prozentwahrscheinlichkeiten bzw. Wertebereiche für DamageType/DeathType-Indizes, keine Tickwerte
- `Sim\World/SimulationWorld.Turrets.cs` (18,33,201,335,338,370,491,507): Winkelschwelle und Faktor 0,5 sind dimensionslos/Winkel; HoldUntil = tick + RecenterTicks ist Datenwert in Ticks als absolute Frist (Math.Max(1, …) nur Sentinel für 0 = keine); Kommentare; Bitshift >> 30 der Hashmischung
- `Sim\World/SimulationWorld.Vision.cs` (61,62): Sichtweite mal ShroudScalar / Kampfplan-Faktor: Längenskalierung, keine Zeit
- `Sim\World/SimulationWorld.WeaponSlots.cs` (98,143): Kommentar zum Ready-Tick; 0,5 ist die Abstandsquadrat-Toleranz der Mindestreichweite; alle übrigen Stellen vergleichen _tick mit absoluten Ready-Ticks (Z. 94, 189, 211, 231, 391) oder setzen sie auf _tick (Z. 290, 308, 375)
- `Sim\World/SimulationWorld.cs` (23,39,69,72,119,199,297,392,781,805,827,970,1292-1293,1304,1308,1311,1313-1316,1320-1337, …): Kommentare, return 0 (keine Ablehnung), Zähler, Vergleich/Clamp absoluter Ticks mit SleepForever, WakeAt-Semantik in echten Ticks, Durchreichen von BaseRegenDelayTicks, Geldbetrag, Snapshot-Zeilen (siehe state-Datei)
- `Sim\World/SimulationWorld.cs` (50-51): Sturm-Schutzgrenzen je Tick (50.000.000 Updates, 20.000.000 Ereignisse), keine Zeitbedeutung
- `Sim\World/TunnelTracker.cs` (9): Eintrittstick je Insasse wird nur gespeichert und abgefragt (EnteredAt); die Heilrechnung liegt im Verbraucher (anderes Paket)
- `Sim\World/TurretState.cs` (13,40,73,114,117,122,124,134,137,144): Zustandsfelder und Snapshot-Zeilen mit absoluten Ticks (HoldUntil, SweepUntil, LoseInterestAt, NextScan); keine Konstanten oder Rechnungen
- `Sim\World/WeaponSlotState.cs` (33,52,53,70,71): Absolute Ready- und Letzter-Schuss-Ticks je Slot, nur gespeichert

*CnCNew.Game* (190 Trefferzeilen in 16 Gruppen)

- `Game\Audio/AudioDirector.Script.cs` (95,99,106): Treffer ist nur "return 0"; Ausblendzeit 0.05f in Sekunden an den Mixer, keine Tick-Kopplung; ganze Datei ohne Zeitbezug zum Tick
- `Game\Audio/AudioDirector.cs` (33,34,38,39,316,337): Uhr des Directors ist die Summe der übergebenen dt in Sekunden (Wandzeit des Renderframes): Musikpause 3 s, Ambient-Abtastung 0,25 s, Blenden 0,15/0,08/0,25 s; keine 30er-Konstante
- `Game\Audio/AudioDirector.cs` (174,554-559): "Frames" sind hier Audio-Abtastframes des Mixers (RenderedFrames), Schonfrist ein Zehntel der Abtastrate = 0,1 s; kein Logiktick
- `Game\Audio/EvaCatalog.cs` (9,10,150,189,211,212): Kommentarzeilen und ein Ereigniszähler; die Kopplung steht in den Zeilen 126, 140, 141, 219, 234
- `Game\Audio/GameAudio.cs` (105-110,232): Update reicht die Sekunden des Renderframes durch (Director.Update/Tick); Report rechnet Mixer-Abtastframes / SampleRate in Sekunden; kein Logiktick
- `Game\Audio/OfflineAudioRenderer.cs` (15,22,40,46-69): Audio-Abtastframes aus Sekunden × SampleRate; WAV-Kopf 44 Byte; kein Logiktick (Blockgrößen hängen aber vom Zeitschritt des Aufrufers ab, siehe Klassenkommentar)
- `Game\Campaign/CampaignCatalog.cs` (11,225,240): Länge der Briefingstimme in ganzen Sekunden aus Campaign.ini, ohne Umrechnung in Ticks in dieser Datei
- `Game\Data/ModelGeometryLoader.cs` (46): Treffer ist "return 0" für fehlendes NumRows/NumCols; Datei enthält nur Geometrie (Rundung 1/16 Einheit, 1/1024 rad)
- `Game\Hud/HudMessageFeed.cs` (169): unterdrückt die Niederlagenmeldung für Ereignisse der ersten Ticks (Spieler ohne Startobjekte); echte Tick-Semantik des Spielbeginns, keine Dauer
- `Game\Hud/HudOverlayRenderer.cs` (11-37,101,102,209,210): "/ 30" und "/ 60" sind Millisekunden je Bild der 2D-Animation (SCPCross 30 ms, SCPWrench 60 ms laut Kommentar Animation2D.ini), Uhr ist MatchController._clockMs (Wandzeit); 10/16 sind Bildanzahlen; übrige Treffer Farben und Kommentare
- `Game\Hud/HudOverlayRenderer.cs` (144-157): Alter 0..1 kommt fertig aus OrderHintBook.Age(_clockMs) (Millisekunden, Wandzeit); der Kommentar nennt die 40 Frames des Originals nur als Beschreibung
- `Game\Hud/MatchController.cs` (58,465,741-757,764): Abklingwischer und Baufortschritt sind Verhältnisse 0..1 (Ticks/Ticks) aus dem Befehlsleistenmodell; übrige Treffer Feldtyp, IsDragging und Farbwerte
- `Game\Hud/MatchController.cs` (372-391,515,520,628): HUD-eigene Uhr _clockMs += round(deltaSeconds × 1000): Fensterbaum (AdvanceTo), Befehlsmarken (OrderHintBook), Heilsymbole; Kamera über deltaSeconds; alles Wandzeit des Renderframes
- `Game\Hud/MatchResult.cs` (50-77): wird einmal je Tick aufgerufen, zählt aber nur Ereignisse (gebaut, verloren, getötet, Geld); kein Zeitbezug
- `Game\Hud/MissionController.cs` (152-161): reicht die Sekunden des Renderframes an LiveGame.Advance und Hud.Update; Popup-Uhr in Millisekunden Wandzeit; der Kommentar Z.152 nennt "30 Hz" als Text (Kopie der Rate nur im Kommentar)
- `Game\Hud/MissionPauseMenu.cs` (200,208,216): Ersatzname eines Spielstands aus dem absoluten Tick, nur als eindeutiger Dateiname benutzt

*CnCNew.Presentation* (338 Trefferzeilen in 40 Gruppen)

- `Pres\Fx/FxRandom.cs` (18): `z >> 30` ist eine SplitMix64-Schiebekonstante
- `Pres\Fx/FxSettings.cs` (15): `MaxFieldParticles = 30` ist eine Anzahl (GlobalData-Standard laut Kommentar), keine Rate
- `Pres\Fx/FxWorldView.cs` (54-60): Pose zerstörter Entitäten bleibt genau einen Refresh (einen Tick) verfügbar: Ticksemantik für Todeseffekte, keine Dauer
- `Pres\Fx/SoundCues.cs` (49): Paarung ProductionCompleted/EntitySpawned im selben Tick (ältere Einträge verworfen): Reihenfolgesemantik, keine Dauer
- `Pres\Input/CameraInput.cs` (138): Scroll-Intent ist Faktor mal Sekunden (1 = Grundgeschwindigkeit für eine Sekunde); enthält keine Rate
- `Pres\Input/CommandBarModel.cs` (345,390-395,406-407): Vergleich world.Tick < ReadyTick und Verhältnisse Restticks/Gesamtticks (ReloadTicks, BuildTicks): dimensionslos
- `Pres\Input/CommandModeController.cs` (14-17,252,256,266,471,570,603,626,633,675,710,717,722): world.Tick wird nur als IssuedTick (StandingOrder) bzw. CommandFeedback.Tick durchgereicht (eigene Zeilen dazu vorhanden); PlacementDragPixels/PlacementViewAngle sind Pixel/Winkel
- `Pres\Input/GameInputController.cs` (97-104): Reicht deltaSeconds (Wanduhr je gezeichnetem Bild, MatchController.Update) an CameraInputController.Update weiter (anderes Paket); Trefferzeilen 25-26, 102, 106, 241-258 ohne Tickbezug
- `Pres\Input/InputState.cs` (13-16): Schwellen in Pixeln und Millisekunden (25 px, 250 ms, 500 ms, 4 px) auf der vom Aufrufer gelieferten Ereignisuhr; Trefferzeilen 7-8, 13, 23-26, 44-45, 50, 58, 69, 83, 117-168 sind Drag-Zustandsmaschine ohne Tickbezug
- `Pres\Input/KeyCode.cs` (70): Tastenname (Backtick), kein Tickbezug
- `Pres\Input/MouseCursors.cs` (81,239-245,288-302): Cursor-FPS-Default 20f (CursorInfo) wird nur gespeichert, nicht zur Zeitsteuerung benutzt; die Animation läuft über nowMilliseconds (SoftwareCursor.Draw) und AniCursor.FrameIndexAt (CnCNew.Images, nicht dieses Paket); Zeilen 190, 257 ohne Zeitbezug
- `Pres\Input/SelectionModel.cs` (16,285-288): Doppeldruck einer Gruppentaste: 500 ms auf der Eingabeuhr (InputState.LastTime), keine 30er-Konstante. Hinweis: Kommentar nennt m_doubleClickTimeMS; gefunden wurde im Original aber 20 Logikframes
- `Pres\Input/SimulationQueries.cs` (155-160): Gibt den gespeicherten Modulzustand (inkl. Tick-Feldern wie Timer/Warteschlangen) als Baum an HUD-Code weiter; hier keine Rechnung mit Ticks
- `Pres\Input/StandingOrders.cs` (36,41,104): 10 m Nachsetz-Schwelle (Strecke); _standing verhindert Wiederholung je Tick; Zeile 104 setzt IssuedTick neu (gehört zur settled-Zeile)
- `Pres\Minimap/MinimapImage.cs` (180,201): Pixelgrenze 1 << 14 und Farbfaktoren 95 %/60 %; keine Zeitgröße in der ganzen Datei
- `Pres\Objects/ConditionDeriver.Units.cs` (37): Höhe 3 Einheiten (Tunnel-Erkennung der Bahn), keine Zeitgröße; übrige Bedingungen (JETEXHAUST bei Speed != 0 usw.) sind Zustandsabfragen
- `Pres\Objects/ConditionDeriver.cs` (117,120,157): Vergleiche von world.Tick mit absoluten Ticks, die die Simulation selbst führt (Ausfahren von Radar/Kraftwerk, Bereitschaft der Waffe)
- `Pres\Objects/DrawKeyCatalog.cs` (15,29,35,43,48-50,55-56): Nur Schlüsselnamen (u. a. RecoilDamping, TreadAnimationRate, MoveOutwardTime, FadeLifetime); die Rohwerte behandelt die Zeile DrawSpecCompiler.ModuleKey
- `Pres\Objects/DrawSpec.cs` (53,88-89): Strecke je Animationsdurchlauf und Geschwindigkeitsfaktor der Animation (dimensionslos); keine Tick- oder Frameeinheit
- `Pres\Objects/FrameProjector.cs` (257,259-269): Baufortschritt ist das Verhältnis ConstructionWork/BuildTicks, Radweg die aufsummierte Wegstrecke (nicht Geschwindigkeit je Frame): beide ratenunabhängig; Travel wird aber nicht zwischen Ticks interpoliert
- `Pres\Objects/ObjectVisualCatalog.cs` (103): Winkel in Grad zu Radiant, keine Zeit
- `Pres\Objects/PlacementPreview.cs` (10,16): Vorschau wird je Bild aus dem PlacementGhost neu gebaut, keine eigene Uhr; Treffer sind Kommentare
- `Pres\Objects/RenderFrame.cs` (53): Kennzahl -2 für die Platzier-Vorschau, keine Zeit
- `Pres\Objects/StructureConditions.cs` (39-126,375-428): Reine Abbildung des Modulzustands der Simulation (Enum/Flags) und Latches auf Zustandswechsel; einmal je Tick abgetastet, keine Tick-Arithmetik
- `Pres\Objects/StructureConditions.cs` (129,190): Tiefe 3 Welteinheiten und Türanzahl 4, keine Zeit
- `Pres\Objects/Suspension.cs` (230,242,243): Abtastabstand 2 Welteinheiten und Hash-Konstanten, keine Zeit (der Tick im Hash steht in der Zeile 126-136)
- `Pres\Objects/TeamColors.cs` (18,19): Farbwerte
- `Pres\Objects/TurretTracker.cs` (24-34): Liest Winkel, Neigung und Rotating-Flag des Simulationsturms je Tick; keine eigene Zeit- oder Ratenlogik (Interpolation in FrameProjector.Blend)
- `Pres\Objects/UnitConditionTracker.cs` (34,35): Faktor 0.5 für den Quetschpunkt (Geometrie), keine Zeit; TensileDrop siehe eigene Zeile
- `Pres\Terrain/RoadCatalog.cs` (11,24): Straßenbreiten, statische Daten
- `Pres\Terrain/RoadGeometry.cs` (18,139,140,156,157,160,174,175,178,198,329-332): Statische Straßengeometrie (Vektorskalierung, Eckindizes), keine Zeit
- `Pres\Terrain/RoadLayout.cs` (43,88,710,719,728,734,750,767,855,876,964,1007,1106,1182): Statische Straßentopologie; die '30' sind 30-Grad-Schritte der Kurven (PI/6), keine Rate
- `Pres\Terrain/TerrainAtlas.cs` (86,89,92): Pixelmaße des Atlas
- `Pres\Terrain/TerrainLighting.cs` (19): Anzahl globaler Lichter
- `Pres\Terrain/TerrainRenderModel.cs` (22,647): Bytegröße eines Vertex und Pixelanzahl einer Kachel
- `Pres\Terrain/TerrainTextureLookup.cs` (32): Kachelzählung
- `Pres\Terrain/WaterSurfaces.cs` (22-23,59-60): WaterSet UScrollPerMS/VScrollPerMS werden roh (je Millisekunde) gelesen, aber in src nirgends verbraucht; stehendes Wasser bekommt UvScrollPerSecond = 0 (Z.116)
- `Pres\Terrain/WaterSurfaces.cs` (28,44,83): Welteinheiten je Texturwiederholung und Farbrechnung, keine Zeit
- `Pres\Vision/FogOfWarGrid.cs` (21): Kommentar zur Einheit Stufen je Sekunde
- `Pres\Vision/FogOfWarGrid.cs` (95-105): Nebelüberblendung in Stufen je Sekunde mal dtSeconds; Aufrufer LiveGame.Advance Z.195 mit unskalierten Wandsekunden (auch bei Pause), Wert 510 aus LiveGame Z.63; Update(vision) kommt aus LiveGame alle VisionIntervalTicks (außerhalb des Pakets)

*CnCNew.Ui* (73 Trefferzeilen in 26 Gruppen)

- `Ui\Layout/UiScaler.cs` (68,69): Bezugsauflösung 800 × 600.
- `Ui\Rendering/UiFontSpec.cs` (23,28): 96 DPI und 91 % Zeichenbreite.
- `Ui\Rendering/UiGlyphAtlas.cs` (23,24): Atlasmaße in Texeln.
- `Ui\Rendering/UiRenderStats.cs` (12): Kommentar „one per frame“ meint ein gezeichnetes Bild des Renderers (Zähler), keine Zeit.
- `Ui\Rendering/UiTooltipLayout.cs` (17): Breite 15 Pixel des Hervorhebungsbands; die Enthüllung läuft über `width * AgeMs / FillTimeMilliseconds` (Zeile 38, Millisekunden).
- `Ui\Runtime/UiDrawBuilder.cs` (28,312,314,331): Kachelgrenze 256, Schieberegler-Zustand „Dragging“ und Lücke 2 Pixel.
- `Ui\Runtime/UiEvents.cs` (19): Kommentar „slider drag finished“.
- `Ui\Runtime/UiGadgetGeometry.cs` (13,14,16,18,19): Pixelmaße der Gadgets (`* 2 / 3` ist die Daumenposition, keine Rate).
- `Ui\Runtime/UiGadgets.cs` (65): Zustandsflag des Schiebereglers.
- `Ui\Runtime/UiWindowTree.Input.cs` (71,144,198,199,209,214): Schieberegler-Zustand „Dragging“ und ein Kommentar; die einzige Zeit der Datei ist der Doppelklick `Now - _lastClickTime <= DoubleClickMs` (Zeile 273, ms-Uhr).
- `Ui\Runtime/UiWindowTree.cs` (16): Millisekunden gegen die vom Host gesetzte ms-Uhr UiWindowTree.Now (AdvanceTo ← GameShell._clockMs += dt * 1000; MatchController._clockMs; MissionController._popupClockMs: alle aus Bildzeit in Sekunden). Kein Sim-Tick, keine 30.
- `Ui\Runtime/UiWindowTree.cs` (209,307): 209: Obergrenze der Ereigniszustellung je Aufruf (Schutz vor Endlosschleife); 307: Wort „Dragging“ (Schieberegler-Zustand).
- `Ui\Settings/GameSettings.cs` (81): Schemaversion 1 der Einstellungsdatei (keine Tick- oder Zeitwerte).
- `Ui\Settings/Resolution.cs` (40,41): Auflösungsliste (z. B. 1680 × 1050, 3840 × 2160).
- `Ui\Settings/SettingsValidator.cs` (17,18,19): Wertgrenzen der Einstellungen.
- `Ui\Text/CsfFile.cs` (36,37,38,39,40,41): Dateiformatkonstanten der CSF-Datei.
- `Ui\Text/GlobalLanguageSettings.cs` (83): Millisekunden (Rückfall 750), hier ohne Umrechnung. Die Umrechnung mit 30 geschieht in Game/Shell/MissionText.cs 137 (`(int)(LogicFramesPerSecond * ms / 1000f)`, abgeschnitten) – anderes Paket.
- `Ui\Text/InGameUiSettings.cs` (34,234): Hier reine Millisekunden ohne Umrechnung. Die Kopplung sitzt beim Verbraucher Game/Shell/MissionText.cs 56 (`ms / LogicFramesPerSecond / 1000`, Ganzzahl, ergibt 0 Bilder wie im Original) – anderes Paket.
- `Ui\Text/InGameUiSettings.cs` (66,215,219,223,235): Zeilen 66, 215, 219, 223 sind die Kommentare zu den oben einzeln gemeldeten Stellen (67, 217, 221, 225); 235 ist ein Farbwert (200, 200, 30, 255).
- `Ui\Text/MouseTooltipSettings.cs` (9): Kommentar („drag tolerance“). Die Felder FillTimeMilliseconds/DelayMilliseconds der Datei sind Millisekunden; FillTime wird in UiTooltipLayout.Build mit UiTooltip.AgeMs (ms-Uhr) verrechnet.
- `Ui\Transitions/TransitionEffects.cs` (27,538,705,811,812,853,855): 27: Eigenschaft, die die einzeln gemeldeten End-Werte trägt; 538, 705, 811, 812, 855: Kommentare zu den Zeilen 543, 711, 816; 853: Zeichenindex `_fullText[frame - 1]` (Folge der Bildeinheit, kein eigener Wert).
- `Ui\Transitions/TransitionEffects.cs` (148): Kennzahl 18 eines Zeichenzustands (nur das Knopfbild zeichnen), keine Bildnummer und keine Dauer.
- `Ui\Transitions/WindowTransitionHandler.cs` (13,46,66,119,129,149): Zähler in Übergangsbildern (kein Sim-Tick) und die Kommentare zur Konstante in Zeile 130; die Bildeinheit selbst ist unter Zeile 130 gemeldet.
- `Ui\Wnd/WndModel.cs` (32,132): Statusbit und MAX_DRAW_DATA = 9; nichts Zeitliches.
- `Ui\Wnd/WndParser.cs` (17,40,170,177,184,595,777): Verschachtelungstiefe 9, Statusname „DRAGABLE“, `return 0` in Fehler- und NULL-Zweigen des Parsers; nichts Zeitliches.
- `Ui\Wnd/WndParser.cs` (800): Tooltip-Verzögerung eines Fensters in Millisekunden, roh gelesen; verglichen mit der ms-Uhr des Baums (UiWindowTree.Tooltip, Zeile 373–374). Kein Tick.

*CnCNew.Client.Stride* (172 Trefferzeilen in 40 Gruppen)

- `Client\Audio/AudioOutput.cs` (24,25,26,28,29,31): Audio-Frames (882 = 20 ms bei 44100 Hz), Pufferzahl, Pumpintervall 4 ms Wandzeit (Thread.Sleep), WinMM-Konstanten
- `Client\Audio/ClientAudio.cs` (61-66): Ton läuft auf der Frame-Uhr (Sekunden; ohne Gerät rendert Pump seconds × SampleRate Samples); die Tonereignisse selbst entstehen je Tick in CnCNew.Game
- `Client\ClientRuntime.cs` (22,69,386): Kommentare "every frame" (gerenderter Frame) und "fed by every simulated tick" (MatchStatistics.Record je TickResult, reine Zählung ohne Zeitgröße im Client)
- `Client\ClientRuntime.cs` (189,191,213,227,234,237,391,398-406): Frame-Uhr des Clients: Rauchtest-Festschritt (1/30 s) oder Wandzeit; geht als Sekunden an LiveGame.Advance, MatchController/MissionController.Update, GameShell.Update und ClientAudio.Update
- `Client\ClientRuntime.cs` (216-221): Minimap-Punkte alle 250 ms und Blinken der Radarereignisse auf der Client-Millisekundenuhr (Wandzeit bzw. Festschritt), kein Tick
- `Client\Layers/Fx/FxBuilder.cs` (42,645): Obergrenze der Zeichenläufe; Geometriehilfsfunktion
- `Client\Layers/Fx/FxDemo.cs` (154,155,169,170,171,179,185): Synthetischer CPU-Benchmark: Partikelzahl, Iterationen, Modulo-Verteilung, Stopwatch-Wandzeit
- `Client\Layers/Fx/FxGeometry.cs` (11,70): Puffergröße in Quads; return 0 bei leerem Puffer
- `Client\Layers/Fx/FxStats.cs` (39): Zähler gerenderter Frames für die Statistik
- `Client\Layers/Fx/FxVertex.cs` (20): Vertexgröße in Byte
- `Client\Layers/FxLayer.cs` (71,73,87,94): CPU-Zeitmessung (Wandzeit) von Capture und Aufbau je gerendertem Frame; die Schicht selbst rückt keine Effekte vor (das macht LiveGame je Tick)
- `Client\Layers/Objects/FlashTint.cs` (15,18): Quantisierung des Blitzfaktors auf 1/32 für den Materialcache
- `Client\Layers/Objects/HouseColor.cs` (87): Farbton in Grad (HSV-Sextant × 60°)
- `Client\Layers/Objects/ModelAsset.cs` (80,96,100,273): Opazitätsstufen in Prozent; animierte Texturmapper einmal je unterschiedlicher Frame-Zeit (Sekunden) neu berechnet
- `Client\Layers/Objects/ObjectInstance.cs` (16,27,43,63,68): Zustandsfelder der Objektschicht: Animationszeit in Sekunden, Dämpfungsanteil (Kopplung siehe ObjectLayerCore 393/401), Kommentare "per frame" = gerenderter Frame
- `Client\Layers/Objects/ObjectLayerCore.cs` (43,119,650,655,686): CPU-Zeitmessung der Schicht (Stopwatch) und deterministische Hash-/Modulo-Auswahl von Animation und Startversatz
- `Client\Layers/Objects/ObjectLayerCore.cs` (236,243,250,251,264,571,614,656,747): Animationsuhr in Sekunden der Frame-Uhr (Wandzeit/Festschritt), Bildrate aus der W3D-Animation; animierte Texturmapper mit TotalSeconds; Rücksetzen bei Zustandswechsel
- `Client\Layers/Objects/ObjectLayerStats.cs` (66,68,71): Statistik je gerendertem Frame (CPU-Millisekunden, Bytes)
- `Client\Layers/Objects/ObjectRenderVertexBuilder.cs` (27,77): Sekunden der Frame-Uhr für W3dRenderPlan.GetTextureCoordinate (Texturmapper-Animation)
- `Client\Layers/Objects/PooledEntity.cs` (10): Kommentar "every frame" = gerenderter Frame
- `Client\Layers/Terrain/RoadMeshBuilder.cs` (17): Konsistenzprüfung Indexzahl % 3
- `Client\Layers/Terrain/TerrainMeshMerger.cs` (55): Konsistenzprüfung Vertexzahl % 4
- `Client\Layers/Terrain/TerrainVertex.cs` (15,30): Vertexgrößen in Byte
- `Client\Layers/Terrain/WaterMeshBuilder.cs` (11,50): Scroll je Sekunde × Sekunden ("wrapped per frame" = je gerendertem Frame); Indexzahl % 3
- `Client\Layers/TerrainLayer.cs` (40,43,176,184,187,221,235): Abtastintervalle der Darstellung in Sekunden der Frame-Uhr (Nebeltextur höchstens 20-mal je s, Minimap 4-mal je s); der Kommentar nimmt an, dass die Simulation den Nebel nur alle paar Ticks ändert
- `Client\Layers/TerrainLayer.cs` (69,70,95,137,141,144,209): Wandzeitmessung von Attach und Update (Profilausgabe ms je Frame)
- `Client\Layers/TerrainLayer.cs` (180): Wasser-UV-Scroll = UvScrollPerSecond × Sekunden der Frame-Uhr
- `Client\Program.cs` (94,340): Anzahl der Journalzeilen, die FinishMission druckt; keine Zeit- oder Tickgröße
- `Client\Program.cs` (183,185,351,384): Zähler gerenderter Frames und Methodensignaturen (GameTime von Stride = Wandzeit); die gekoppelten Verwendungen stehen in eigenen Zeilen
- `Client\Program.cs` (375-378): Modellbetrachter ohne Simulation: Zeit der Texturanimation im Rauchtest = Frames/60 s (sonst Wandzeit gameTime.Total), Budget 10/16 Frames
- `Client\Rendering/IWorldLayer.cs` (8): Sekunden der Frame-Uhr (Wandzeit oder Rauchtest-Festschritt), keine Ticks
- `Client\Rendering/LiveMatchHost.cs` (55,57,58,62,63,68): Frame-Uhr: Festschritt oder Wandzeit gameTime.Elapsed; _total summiert sie; Match.Advance(seconds) bzw. step(seconds); WorldFrame(DeltaSeconds, TotalSeconds) für die Layer
- `Client\Rendering/ScreenFilters.cs` (13,32,56): Farbgewichte im Kommentar; Zähler gefilterter gerenderter Frames
- `Client\Rendering/ScreenFilters.cs` (14,15,79,88): Höchstens 30 Zoom-Ebenen des Bewegungsunschärfe-Filters (Ebenenzahl, keine Zeit); der Zähler count wird in CnCNew.Game (MissionPresentation, _blurCount) fortgeschaltet, nicht hier
- `Client\Testing/InputScript.cs` (15,26): Kommentare "per frame" = Update/Frame des Clients
- `Client\Ui/GdiFontProvider.cs` (106,107): Win32-GDI-Konstanten (FW_NORMAL 400 usw.)
- `Client\Ui/SoftwareCursor.cs` (41-44): Cursoranimation auf der Client-Millisekundenuhr (_clockMs: Wandzeit bzw. 33 ms je Rauchtest-Frame)
- `Client\Ui/StrideInputTranslator.cs` (19-47): Zeitstempel der Eingabeereignisse = Client-Millisekundenuhr (Wandzeit); keine Ticks
- `Client\Ui/UiRenderer.cs` (25): Atlasgröße in Pixeln
- `Client\W3dStrideScene.cs` (94,144,167): Sekunden für die Texturmapper-Animation des Modellbetrachters (Wandzeit bzw. Frames/60 im Rauchtest)

*CnCNew.Audio* (46 Trefferzeilen in 12 Gruppen)

- `Audio\Catalog/AudioCatalog.cs` (201,263,329,566): „Decay“ ist die Ausklang-Klangliste eines Ereignisses (Dateinamen), `return 0` löscht die Bits bei NONE.
- `Audio\Catalog/AudioCatalog.cs` (554,555): Verzögerung in Millisekunden, roh als Ganzzahlen; Verbrauch: SoundSelector.NextIteration (Zufallswert in ms) → Segment.Silence(ms, outputRate) = `ms * Abtastrate / 1000` Abtastbilder. Kein Tick.
- `Audio\Catalog/AudioEventInfo.cs` (51,99,108,114): Flag-Bit POSTDELAY und die Ausklang-Klangliste; DelayMin/DelayMax des Records sind Millisekunden.
- `Audio\Catalog/AudioSettingsInfo.cs` (30-31): Bleiben Millisekunden (Kommentar: das Original rechnet sie mit parseDurationUnsignedInt in Logikbilder um); Verbraucher Game/Audio/AudioDirector.cs 741, 745, 809, 863 teilen durch 1000 und rechnen in Sekunden. Kein Mustertreffer, zur Vollständigkeit.
- `Audio\Mixing/PlayRequest.cs` (43,44,73): Millisekunden und Sekunden, im Mixer mit der Abtastrate in Abtastbilder umgerechnet; Zeile 73 ist die Decay-Klangliste.
- `Audio\Mixing/SoftwareMixer.cs` (75,83,89,97,104,186,301,391,396,434,440,441,442): Blenden in Sekunden × Options.SampleRate (Zeilen 190, 303); „frame“ ist ein Stereo-Abtastbild (`% 2`, `2 * frame`). Die einzige Uhr des Mixers ist die Zahl gerenderter Abtastbilder (Render), keine Tick- und keine 30er-Größe.
- `Audio\Mixing/VoiceSources.cs` (26,88,110,160): Schutzgrenze 64 leere Segmente, Decay-Klang, Kommentare; Stille = `ms * outputRate / 1000` Abtastbilder (Zeile 9).
- `Audio\Movies/BinkHeader.cs` (26,29,80): Videobilder und Bildrate aus dem Bink-Kopf (FpsNumerator / FpsDenominator der Datei), Grenze 256 Tonspuren; keine Sim-Rate.
- `Audio\PcmBuffer.cs` (8,26,27,47,58): Abtastbilder und Dauer = FrameCount / SampleRate; `2 * frame` ist die Stereo-Verschachtelung.
- `Audio\Selection/SeededRng.cs` (16): Bitverschiebung `>> 30` des SplitMix64-Schritts.
- `Audio\Selection/SoundSelector.cs` (29,44,61,63): Auswahl des Ausklang-Klangs; Ziehreihenfolge des audioeigenen Zufallsgenerators je Ereignisstart bzw. Schleifendurchlauf, nicht je Tick.
- `Audio\Wav/WavReader.cs` (95): Anzahl Abtastbilder aus dem fact-Chunk der WAV-Datei.

*CnCNew.Data* (31 Trefferzeilen in 13 Gruppen)

- `Data\Compilation/CompatibilityBaseline.cs` (13): Schemaversion 1 der Kompatibilitätsbasislinie, kein Zeitbezug
- `Data\Definitions/DefinitionPrimitives.cs` (29,88,304): GameTime hält Millisekunden als decimal; ShotDelay ist ein ms-Bereich; die Heuristik für Opaque-Schlüssel (Time/Delay/Duration …) legt die INI-Zahl unverändert als ms ab – keine Umrechnung in Frames
- `Data\Definitions/NameTables.cs` (31): Namenstabelle der Objektstatus-Bits (Treffer durch IS_BRAKING), kein Zeitbezug
- `Data\Ini/SageIniParser.cs` (7): Maximale INI-Zeilenlänge 1028 Zeichen, kein Zeitbezug
- `Data\Registry/ModuleCatalog.cs` (421,426,427): Schema hält die Reibungen als rohe INI-Zahl je Sekunde (Original: parseFrictionPerSec teilt beim Parsen durch die Framerate); CenterOfMassOffset ist eine Länge. In Data keine Umrechnung
- `Data\Registry/ModuleCatalog.cs` (888): Schema hält die rohe INI-Zahl (Original parseAccelerationReal = Strecke/s²); in Data keine Umrechnung
- `Data\Registry/ModuleCatalog.cs` (959): Kommentarzeile zum Schema (delay/integrity/decay), kein Code
- `Data\Schema/FieldSchema.cs` (119,156,170,216): DurationMs und ShotDelayMs werden als Millisekunden in GameTime abgelegt (Vorzeichen-Umlauf modulo 2^32 wie sscanf %u); `index % 2` wählt jeden zweiten Namen. Keine Frame-Umrechnung, kein Frame-Feldtyp in FieldType
- `Data\Schema/ObjectCreationListSchema.cs` (12,38): Schema hält ExtraFriction als rohe Zahl je Sekunde und die Drehraten als Grad je Sekunde (FieldType.Angle); in Data keine Umrechnung
- `Data\Schema/TurretSchema.cs` (11,12,27,28): Schema hält Grad je Sekunde als rohe Zahl, Zeiten als DurationMs; der Kommentar sagt, dass erst die Simulation in Radiant je Tick und Ticks umrechnet
- `Data\Schema/UpgradeSchema.cs` (63): Schema hält die rohe INI-Zahl (Strecke/s²; ebenso Speed, TurnRate, Acceleration als je-Sekunde-Werte); laut Kommentar rechnet erst die Simulation über SimulationTiming um
- `Data\Snapshots/DefinitionSnapshotWriter.cs` (11,99): Schemaversion 2; GameTime wird als `milliseconds` in den Definitionssnapshot geschrieben – der Fingerabdruck enthält keine Tickrate und keine Tickwerte
- `Data\World/MapModel.cs` (55,56,58,59,60,62,64): Bitmasken der Kartenobjekt-Flags (Straßen-/Brückenpunkte), kein Zeitbezug

*übrige src* (160 Trefferzeilen in 23 Gruppen)

- `src\CnCNew.Archives/ArchiveEntryStream.cs` (37,50,63): `return 0` am Ende des Archiveintrags (Bytezahl), kein Zeitbezug
- `src\CnCNew.Archives/BigArchive.cs` (9,10,207): Formatgrößen (16 Byte Kopf, 32 KiB Pfad) und Prozent-Escaping von Dateinamen, kein Zeitbezug
- `src\CnCNew.Content/RuntimeContentManifest.cs` (11): Schemaversion 2 des Inhaltsmanifests, kein Zeitbezug
- `src\CnCNew.Formats/LegacyAssetReaders.cs` (18,128,129,245,454): Formatgrenzen und -prüfungen; die Bink-Bildrate (Zähler/Nenner) wird nur aus dem Dateikopf als Metadaten gelesen, nichts rechnet damit; `% 20` ist die WAK-Satzlänge
- `src\CnCNew.Formats/SageMapDecoder.cs` (18,363): Formatkonstanten (Kennwert 0x7ADA0000, höchstens 12 Skriptparameter), kein Zeitbezug
- `src\CnCNew.Images/CursorReader.cs` (19): 60 Jiffies je Sekunde ist die Zeiteinheit des ANI-Dateiformats; FrameIndexAt rechnet aus Millisekunden (Wandzeit des Aufrufers), nicht aus Simulationsticks – die 60 hat nichts mit der Tickrate zu tun
- `src\CnCNew.Images/CursorReader.cs` (47,48,84,158,179): Formatgrenzen (256 Pixel, 4096 Bilder), Kopf-Felder und Längenprüfungen des Cursor-Dekoders, kein Zeitbezug zur Simulation
- `src\CnCNew.Images/TgaReader.cs` (7,8): Formatgrößen des TGA-Lesers, kein Zeitbezug
- `src\CnCNew.Import/RuntimeContentImporter.cs` (340): Prozent-Escaping abschließender Punkte/Leerzeichen in Dateinamen (%2E/%20), kein Zeitbezug
- `src\CnCNew.W3D/W3dAnimation.cs` (62,63,74,222,223,225): FrameRate stammt aus dem Animationskopf der W3D-Datei (1..1000 geprüft im Dekoder), DurationSeconds = FrameCount/FrameRate; die Abtastung arbeitet auf gebrochenen Animationsbildern. Keine 30 im Code
- `src\CnCNew.W3D/W3dAnimationChannels.cs` (65,66,70,79,91,172,177,183,194,229,230,247,254,276,319,320,330,342): Bildindizes und Formatkonstanten der Animationskanäle (Bereich First..Last, Zeitcodes als Bildnummern, 16er-Gruppen, 9-Byte-Pakete); alles in Animationsbildern der Datei
- `src\CnCNew.W3D/W3dAnimationClock.cs` (20,26,55): Zustandslose Abbildung elapsedSeconds (double, Sekunden) × frameRate (aus der Datei) × speedMultiplier → Animationsbild; `return 0` für Ein-Bild-Animationen. Kein Tick, keine 30
- `src\CnCNew.W3D/W3dAnimationDecoder.cs` (7-12): Kopfgrößen der Animations-Chunks in Byte; ReadHeader liest die Bildrate aus der Datei (uint32 bzw. uint16)
- `src\CnCNew.W3D/W3dBinary.cs` (9): Namenslänge 16 Byte im W3D-Format
- `src\CnCNew.W3D/W3dChunkReader.cs` (7-10): Chunk-Formatkonstanten, kein Zeitbezug
- `src\CnCNew.W3D/W3dChunkType.cs` (5-81): Chunk-Typkennungen des W3D-Formats (alle Trefferzeilen 5-40, 42-45, 47-50, 52-55, 57-64, 66-72, 74-81), kein Zeitbezug
- `src\CnCNew.W3D/W3dHierarchy.cs` (23-25): Strukturgrößen in Byte (36/60/48); die 60 ist eine Pivot-Größe, keine Rate
- `src\CnCNew.W3D/W3dHlod.cs` (32-36): Strukturgrößen des HLOD-Formats in Byte, kein Zeitbezug
- `src\CnCNew.W3D/W3dMeshDecoder.cs` (10-15,599): Strukturgrößen des Mesh-Formats in Byte, kein Zeitbezug
- `src\CnCNew.W3D/W3dModel.cs` (62): Obergrenze 99 für nummerierte Knochennamen (nur Deklaration und Umgebung gelesen), kein Zeitbezug
- `src\CnCNew.W3D/W3dRenderPlan.cs` (108): Parameter totalSeconds (double, Sekunden) wird an W3dTextureMapperEvaluator.Evaluate durchgereicht; kein Tick
- `src\CnCNew.W3D/W3dSkeletonAudit.cs` (60): Optionale Stopwatch für die Dekodierzeit eines Audits (Wandzeit, bewusst nicht im Bericht), kein Simulationsbezug
- `src\CnCNew.W3D/W3dTextureMapper.cs` (232,262,277,288): UV-Animation aus totalSeconds: UPerSec/VPerSec und FPS kommen aus den Mapper-Argumenten der Datei (Standard FPS 1); Bild = floor(abs(FPS) × Sekunden). Keine 30

*tools* (132 Trefferzeilen in 29 Gruppen)

- `tools/CnCNew.AssetTool/Program.cs` (34,47,55,84,109,159,179,187,206,216,294): Exitcodes der Asset-Kommandos
- `tools/CnCNew.AssetTool/Program.cs` (168,262): Fortschrittsanzeige in 5-%-Schritten und URL-Escape-Zeichenkette; kein Zeitbezug
- `tools/CnCNew.CampaignTool/Program.cs` (51,87,104,156,182,231,237,362,415,431,464,543,576,624): Exitcodes der Kommandos bzw. Listenbegrenzung Take(30); keine Zeitbedeutung
- `tools/CnCNew.CampaignTool/Program.cs` (295,347,357): Wandzeit des Auditlaufs (TotalSeconds); keine Umrechnung Tick↔Sekunde
- `tools/CnCNew.ImageTool/Program.cs` (7,25,30): Exitcodes; Werkzeug dekodiert TGA-Dateien, kein Simulations- oder Zeitbezug
- `tools/CnCNew.IniTool/Program.cs` (23,50,60,78,84,223): Exitcodes der INI-Kommandos; kein Zeitbezug an diesen Stellen
- `tools/CnCNew.MapTool/Program.cs` (23,28,71,79,277): Exitcodes der Kartenkommandos; kein Zeitbezug an diesen Stellen
- `tools/CnCNew.PresentationTool/AngleAuditCommand.cs` (24,86): Exitcodes
- `tools/CnCNew.PresentationTool/FxAuditCommand.cs` (14): Exitcode; die FX-Uhr wird nicht hier, sondern in FxAudit.ExecuteAll (src/CnCNew.Presentation, anderes Paket) geschaltet, das Werkzeug druckt nur Frames/Checksumme
- `tools/CnCNew.PresentationTool/ModelAuditCommand.cs` (22): Exitcode
- `tools/CnCNew.PresentationTool/Program.cs` (20,36,42,52,54): Exitcodes der Kommandoverteilung und des object-audit
- `tools/CnCNew.PresentationTool/RoadAudit.cs` (30,32,139): Exitcode und Wandzeit des Aufbaus („build time total“)
- `tools/CnCNew.PresentationTool/ScenarioAudit.cs` (28,191,195,213,377,395): Exitcode, Kommentare und Ausgabetexte mit dem Wort „tick“; die zugehörigen Anweisungen stehen in eigenen Zeilen (207, 216, 392, 396)
- `tools/CnCNew.PresentationTool/ScenarioAudit.cs` (434,437): Schwellen in Einheiten pro Sekunde (55/s, Text „minimum speed 60/s“), ebenso 40f, 2f, 5f (Z. 423, 431) und 51f/52f (Z. 491, 501); raten-unabhängig, solange die Umrechnung in Z. 111 stimmt
- `tools/CnCNew.PresentationTool/TerrainAudit.cs` (37,39,124): Exitcode und Wandzeit des Aufbaus („build time total“)
- `tools/CnCNew.SimTool/Program.cs` (53,77,173,180,188,215,238,313,487,546,555,564,656,671): Exitcodes der Kommandos bzw. Listenbegrenzung Take(30); keine Zeitbedeutung
- `tools/CnCNew.SimTool/Program.cs` (158,170): Tickdifferenzen (Stillstand, Aufenthalt im Gebäude) werden roh in Ticks ausgegeben, ohne Umrechnung
- `tools/CnCNew.SimTool/Program.cs` (296,319,336,364,430,452,483,531,586,588,617,622,625,631,644,646,652,666,679,709,711): Wandzeitmessung der Werkzeugläufe (Stopwatch, TotalSeconds, langsamster Tick in ms); keine Umrechnung Tick↔Sekunde
- `tools/CnCNew.SimTool/Program.cs` (335,451,553): Kadenz der Inkrementalhash-Prüfung in Ticks (5000) bei reference-match, ai-match, replay-run; kein Zeitbezug
- `tools/CnCNew.SimTool/Program.cs` (578,638): MaxTicks = 0 bedeutet kein Limit (Lauflänge kommt aus --ticks, eigene Zeile); sampledTicks ist ein Zähler
- `tools/CnCNew.SimTool/Program.cs` (730): Hashprüfung alle 20.000 Ticks (VerifyIncrementalHash); reine Prüfkadenz, kein Zeitbezug
- `tools/CnCNew.UiRenderTool/FontAudit.cs` (47): Glyphen-Deckung als Vielfaches von 17 (4-Bit-Alpha); kein Zeitbezug
- `tools/CnCNew.UiRenderTool/MissionTextScenes.cs` (37): Farbwert und Layoutbruch 4/5 der Höhe
- `tools/CnCNew.UiRenderTool/Program.cs` (76): Stride-Draw-Rückruf; gameTime wird nicht benutzt, die Aufnahmen laufen einmal im ersten Frame
- `tools/CnCNew.UiRenderTool/ShellScenes.cs` (64,66): Bildnamen; die Datei schaltet keine Uhr (nur Eingabeereignisse mit fortlaufendem Zeitstempel ++time)
- `tools/CnCNew.UiRenderTool/SyntheticScenes.cs` (71): Farbwert
- `tools/CnCNew.UiRenderTool/TransitionScenes.cs` (13,14,20): Doc-Kommentar zur 30-Bilder-Schrittweite (Anweisung in Z. 21) und Bildgröße 1280x720
- `tools/CnCNew.W3dTool/Program.cs` (27,32,65,100,105,142,147,159,384): Exitcodes der W3D-Kommandos
- `tools/CnCNew.W3dTool/Program.cs` (47,48,58,59): Wandzeitmessung des Audits (nach stderr); kein Tickbezug

*tests* (837 Trefferzeilen in 104 Gruppen)

- `tests/CnCNew.Archives.Tests/Program.cs` (27): return 0
- `tests/CnCNew.Audio.Tests/Program.cs` (15,51,54): 3 Stellen: Testname ('delay ... decay') und Exitcodes
- `tests/CnCNew.Audio.Tests/Tests.Catalog.cs` (36,79,84,129,146,199): 6 Stellen: AudioSettings/SoundEffects-Fixture in Millisekunden (TimeBetweenDrawableSounds 900, Delay 100 250), Decay-Dateinamen; keine Umrechnung in Ticks
- `tests/CnCNew.Audio.Tests/Tests.Decoding.cs` (102,105,127,139,161): 5 Stellen: ADPCM-Abtastwerte 900/1000, MPEG-Frames, gerade Byte-/Framezahlen
- `tests/CnCNew.Audio.Tests/Tests.Mixer.cs` (54,109,110,111,176,177,179,200,212,216,228,235,236,275): 14 Stellen: Vektor (0,-30,40), 30 Abtastframes einer Schleife, DelayMin/Max in ms bei 1000 Hz, step % n im Mixerskript (Audioblöcke), Bink-Kommentar; Mixer rechnet in Abtastframes
- `tests/CnCNew.Audio.Tests/Tests.RealData.cs` (31,72,76,124,127,135,141,158): 8 Stellen: Stopwatch und Durchsatzausgabe, Auswahl jedes 25. Ereignisses, step % 6 in Audioblöcken; Hash nur zwischen zwei Läufen verglichen
- `tests/CnCNew.Content.Tests/Program.cs` (55): return 0
- `tests/CnCNew.Data.Tests/Program.cs` (46,341,441,442,451,453,456,457,574,772,773,778): 12 Stellen: return 0, Vektor X:-30, Lifetime-/StealthDelay-Felder in Millisekunden als GameTime (keine Tick-Umrechnung in CnCNew.Data), Locomotor Speed = 30 als Rohwert im Snapshot-Vergleich
- `tests/CnCNew.Formats.Tests/MapDecoderTests.cs` (26): Objektkoordinate 30, 40
- `tests/CnCNew.Game.Tests/AudioDirectorTests.cs` (38,84,85,471,832,841): 6 Stellen: Deklaration der Tick-Eigenschaft des Testdoubles, Stopwatch für Ladezeit/Messung (Wandzeit), 30f ist der Radius einer FxEntityPose
- `tests/CnCNew.Game.Tests/CampaignShellTests.cs` (26,109): 2 Stellen: Bildschirmgröße und Kartengröße
- `tests/CnCNew.Game.Tests/CampaignTests.cs` (153): Kartengröße 30 Zellen
- `tests/CnCNew.Game.Tests/HudTests.cs` (27,88,261,270,426,512,513,608,663): 9 Stellen: return 0, Kommentare ('every frame'), Maus-Drag-Helfer (Eingabezeit in ms), Signatur des Ereignis-Helfers
- `tests/CnCNew.Game.Tests/LiveMatchTests.cs` (19,22,23,35,37,42,44): 7 Stellen: return 0, Stopwatch-Messungen und Konsolenausgabe ms/Frame (600 = Schleifenlänge)
- `tests/CnCNew.Game.Tests/MissionEffectsTests.cs` (32,33,356,388,547,551): 6 Stellen: return 0, Zähler-Initialisierung, 30f/60f ist Unschärfezähler 30 von 60, m:ss-Formatierung (% 60), Farbwert 30
- `tests/CnCNew.Game.Tests/MissionHudTests.cs` (25,176): 2 Stellen: return 0 und Kommentar 'every frame'
- `tests/CnCNew.Game.Tests/MissionPresentationTests.cs` (34,64,66,268): 4 Stellen: Testname/Meldung ('every frame'), Kartengröße 30 Zellen, Rand 1
- `tests/CnCNew.Game.Tests/MissionTextTests.cs` (134,146): 2 Stellen: Balkenhöhe 75 Pixel bei 800x600; Int(30) ist die Sekundenangabe von DISPLAY_CINEMATIC_TEXT (Skriptdaten in Sekunden)
- `tests/CnCNew.Game.Tests/ModelGeometryTests.cs` (18,50,54): 3 Stellen: return 0; 30.825 ist eine Knochenkoordinate
- `tests/CnCNew.Game.Tests/Program.cs` (28): return 2 bei unbekannter Suite
- `tests/CnCNew.Game.Tests/ScenarioTests.cs` (19): return 0
- `tests/CnCNew.Game.Tests/ShellTests.cs` (31,41,53,502,514,515,517,587,588): 9 Stellen: Bildschirmgröße, return 0, Schieberegler-Drag, Frist des Boot-Tests in Wandzeit (DateTime.UtcNow)
- `tests/CnCNew.Game.Tests/ShellTransitionTests.cs` (21,45): 2 Stellen: Bildschirmgröße und return 0
- `tests/CnCNew.Game.Tests/TextFixtures.cs` (15,19,21,22): 4 Stellen: synthetische INI-Werte in Millisekunden (MilitaryCaptionDelayMS 750, MessageDelayMS 75000, FlashDuration 500); die Umrechnung liegt im geprüften Code
- `tests/CnCNew.IO.Tests/Program.cs` (23): return 0
- `tests/CnCNew.Images.Tests/Program.cs` (15,18,22): 3 Stellen: TGA-Pixelbytes 30/20/10 und return 0
- `tests/CnCNew.Presentation.Tests/CameraTests.cs` (104): 1 Stelle: Modulo 40 erzeugt nur das Höhenmuster der Testkarte
- `tests/CnCNew.Presentation.Tests/CameraTests.cs` (182): 1 Stelle: 30 sind Höheneinheiten (drei Rasten), keine Zeit
- `tests/CnCNew.Presentation.Tests/CursorTests.cs` (55,91,305,329,335,355,356,477): 8 Stellen: Frames = 2 und Ani-Parameter der Cursordatei (eigene Cursoruhr), Graustufe 30, Bildschirm-/Weltkoordinaten 30, Alphakanal-Modulo 4, Assert-Zeile ohne Zeitbezug
- `tests/CnCNew.Presentation.Tests/FxTests.cs` (30,44,194,201,222,442,460,465,511,517,729,730,820,863): 14 Stellen: Testname, Partikelobergrenze 30, Radius 30, DecayAt-Anteil, FXList-InitialDelay nur als Rohwert verglichen, Diagnosefixture, Koordinaten 30f, Volumenabstand, InitialDelayFrames = 0, räumlicher Shake-Abfall, Stopwatch nur für die Ausgabe
- `tests/CnCNew.Presentation.Tests/InputTests.cs` (29,40,42,286,295,368,384,385,498,514,608,612,614,640,756,822,824,825,828,833,834,836,922, …): 42 Stellen: Testnamen, Fixturedaten in ms oder je Sekunde, Kartenmaße, Pixel- und Weltkoordinaten 30, Ereigniszeitstempel in ms, Winkel, Geld 450, Kameraabsicht mit dt = 1 s oder 0 (je Sekunde definiert)
- `tests/CnCNew.Presentation.Tests/JetOrderTests.cs` (30,45,53): 3 Stellen: Braking 60 je Sekunde im Quadrat und DelayBetweenShots in ms; Daten werden umgerechnet
- `tests/CnCNew.Presentation.Tests/MinimapTests.cs` (67,77,81): 3 Stellen: Pixelkoordinate 30 und Modulo 80 für die Zellenspalte
- `tests/CnCNew.Presentation.Tests/ObjectTests.cs` (67,74,87,89,90,277,596,597,619,640,675,680,855,859): 14 Stellen: Fixturedaten in ms und je Sekunde, Kartenmaße, Weltkoordinaten 30, Befehlsstempel world.Tick
- `tests/CnCNew.Presentation.Tests/PlacementPreviewTests.cs` (20): 1 Stelle: Farbwert (240, 200, 30)
- `tests/CnCNew.Presentation.Tests/RoadRealDataTests.cs` (25,82): 2 Stellen: return 0 beim Überspringen, Indexzahl modulo 3
- `tests/CnCNew.Presentation.Tests/RoadTests.cs` (87,103,214): 3 Stellen: Modulo 2 der Vertexzahl, 30-Grad-Kurvenstücke, Modulo 3 im Höhenmuster
- `tests/CnCNew.Presentation.Tests/StructureConditionTests.cs` (36,37,38,62,110,124,172,184,232,241,250,252,253,297,298,336,341,390,412,413,430,755,764, …): 24 Stellen: Fixturedaten in ms und je Sekunde (werden umgerechnet), Geometrie 30, Testname und Meldungstexte, Raster index % 60 der Spawnpositionen
- `tests/CnCNew.Presentation.Tests/SyntheticTerrain.cs` (45): 1 Stelle: Bildgröße 128 Pixel
- `tests/CnCNew.Presentation.Tests/TerrainTests.cs` (50,207,228,268,274): 5 Stellen: Weltkoordinaten 30, Modulo im Höhenmuster, Farbwerte 30
- `tests/CnCNew.Presentation.Tests/UnitConditionTests.cs` (65,67,75,77,85,86,97,105,107,114,124,131,132,272,284,360,371,573,924): 19 Stellen: Fixturedaten in ms und je Sekunde (werden umgerechnet), Geometrie und Kartenmaße, Zähler nur gegen 0 verglichen, Befehlsstempel World.Tick
- `tests/CnCNew.Presentation.Tests/VisionTests.cs` (48,88): 2 Stellen: Nebelüberblendung in Stufen je Sekunde mit dt in Sekunden (Wandzeit), Argumentprüfung
- `tests/CnCNew.Presentation.Tests/WorldRealDataTests.cs` (36): 1 Stelle: return 0 beim Überspringen ohne --content-dir
- `tests/CnCNew.Simulation.Tests/Program.cs` (408,411): 2 Stellen: Exitcodes 1/0 des Testläufers
- `tests/CnCNew.Simulation.Tests/Tests.Acquisition.cs` (32,138,139,339,366,433): 6 Stellen: Fixture in ms (DelayBetweenShots 300), Lebenspunkte 30.0, Koordinaten (-30, 300, 450, 900)
- `tests/CnCNew.Simulation.Tests/Tests.Air.cs` (28,29,39,40,51,58,67,68,75,76,86,94,104,116,145,366): 16 Stellen: Locomotor-Werte pro Sekunde (Acceleration/Braking), DelayBetweenShots in ms, HealAmountPerSecond 60, OutOfAmmoDamagePerSecond 30 %, Radius 30, Koordinate 900
- `tests/CnCNew.Simulation.Tests/Tests.AirModelGeometry.cs` (20,81,82,93,114,122): 6 Stellen: Knochenkoordinaten (30) und Startbahnlinie y = 450 als Weltkoordinaten
- `tests/CnCNew.Simulation.Tests/Tests.AirReal.cs` (57,130): 2 Stellen: Abstand 30 zwischen Spawnpunkten, Zieltoleranz 30 Einheiten
- `tests/CnCNew.Simulation.Tests/Tests.AirWeapons.cs` (26,47,54,71,76,80,83,94,99,180,181,235,236,241,341,366,369,373,374,382,383,417,655,662,788): 25 Stellen: Fixture in ms (DelayBetweenShots, Lifetime 900, DisabledDuration, Delay, KillSelfDelay, InitialDelay, Blast*Delay), Braking pro s², Radien 30, Koordinate 900, Sentinel deathTick = -1, Tickdifferenz nur auf >= 0 geprüft
- `tests/CnCNew.Simulation.Tests/Tests.Basics.cs` (51,59,60,61,66,68): 6 Stellen: SleepForever/SleepNone (ratenunabhängig), Ausnahmen bei negativen/überlaufenden ms, DegreesToRadians, RatioToFixed
- `tests/CnCNew.Simulation.Tests/Tests.BattlePlan.cs` (31,130,168,180,240): 5 Stellen: DelayBetweenShots 100 ms, StealthDelay 0, Koordinate 30, Sichtabstand 30 Einheiten, Schaden 30
- `tests/CnCNew.Simulation.Tests/Tests.CleanupAndSpy.cs` (24,103,104,105,140,141,150,174,183,249,250,257,262,265,398): 15 Stellen: Fixture in ms (DelayBetweenShots, BaseDuration, BonusDurationPerCaptured, MaxDuration, SelfPoweredDuration/Interval), VisionRange 30, Koordinaten 900/450/30, Toleranz 30
- `tests/CnCNew.Simulation.Tests/Tests.Collision.cs` (19,40,167,168,185,186,231,268): 8 Stellen: Braking 30000 pro s², DelayBetweenShots 0, Min/MaxToppleDelay 100 ms, InitialDelay 100 ms, Kartengröße cells = 30
- `tests/CnCNew.Simulation.Tests/Tests.Combat.cs` (20,27,31,37,44,52,61,70,121,157,215,216,287,292,342,346,403): 17 Stellen: Fixture in ms (DelayBetweenShots 0/100/300, ExclusiveWeaponDelay 1000, InitialDelay 600), Radien 30, MaxHealth 900, Koordinate 30
- `tests/CnCNew.Simulation.Tests/Tests.Construction.cs` (25,42,179,196,283): 5 Stellen: Braking 60 pro s², RepairHealthPercentPerSecond 10 %, BuildCost 30, Geldstand 900
- `tests/CnCNew.Simulation.Tests/Tests.Crates.cs` (94,118,124): 3 Stellen: LogicRandom.CopyState (Zufallszustand, kein Takt), Koordinate 30 beim Spawnen und Nachschlagen
- `tests/CnCNew.Simulation.Tests/Tests.CratesAndSpecials.cs` (25,42,45,51,122,140,269,293,367,371,478,479,533,546,635,647,655,656,739,969,972,998,1019, …): 24 Stellen: Braking 30000 pro s², Reichweiten/Radien 30, ExperienceValue 10 30 90 300, Dauern in ms (SabotageDuration, Min/MaxLifetime, StealthDelay), Koordinaten 30/900, MinHealth 30 %, Lebenspunkte 900
- `tests/CnCNew.Simulation.Tests/Tests.CreationLists.cs` (66,67,139,140,276): 5 Stellen: MinLifetime/MaxLifetime 1000 bzw. 100000 ms, Schaden 30
- `tests/CnCNew.Simulation.Tests/Tests.DamagedLocomotor.cs` (14,15,16,23,24): 5 Stellen: SpeedDamaged 30, Acceleration/Braking 900 – Locomotor-Daten pro Sekunde
- `tests/CnCNew.Simulation.Tests/Tests.FiringTracker.cs` (20,45,46,54,55,64,65): 7 Stellen: DelayBetweenShots 600/1000/200 ms, PreAttackDelay 500 ms
- `tests/CnCNew.Simulation.Tests/Tests.Flight.cs` (26,36,38,45,70,73,84,85,153,154,170,173,182,184,185,193,197,198,232,268,269,293,295,353, …): 27 Stellen: Locomotor-Werte pro Sekunde (Braking, SpeedLimitZ 30), Dauern in ms (DelayBetweenShots, Lifetime, DoorDelay 34, DropDelay 100, Destruction-/Volley-Delays), DamagePerSecond 100, RepairHealthPercentPerSecond, Radius/Toleranz 30, Meldungstext 'decayed'
- `tests/CnCNew.Simulation.Tests/Tests.GoldenMaster.cs` (40): 1 Stelle: Sentinel für 'noch nicht gestorben'
- `tests/CnCNew.Simulation.Tests/Tests.JetFlight.cs` (24,38,51,64,153,157,336,340,341): 9 Stellen: Fixture-INI «Braking/TurnRate» in Einheiten je Sekunde (24,38,51,64,340,341), Doku-Kommentare (153,336) und Spawn-Koordinaten 500/500 (157)
- `tests/CnCNew.Simulation.Tests/Tests.JetFlightReal.cs` (20): 1 Stelle: Kommentar «turn rate per tick» ohne Zahl
- `tests/CnCNew.Simulation.Tests/Tests.LastBlockers.cs` (41,50,79,125,167,249,312,313,362,363,387,421,422,518,557,583,600,617,791,811,1003,1006): 22 Stellen: Fixture-INI in ms bzw. je Sekunde oder Schadens-/XP-Werte (41–422), Koordinaten x = 30 (518–617), Befehls-Sequenznummer 900 (791,811), Spaltenraster % 3 (1003,1006)
- `tests/CnCNew.Simulation.Tests/Tests.MatchModules.cs` (40,89,178): 3 Stellen: Fixture-INI BaseRegenHealthPercentPerSecond/BaseRegenDelay (ms, je Sekunde), ForwardFriction = 2.0 (Lesestelle im Simulationscode nicht gefunden), Zählwert orientations > 30
- `tests/CnCNew.Simulation.Tests/Tests.Navigation.cs` (15,20,206,218,424,431): 6 Stellen: Kartengröße 20 Zellen, Fixture «Braking = 60.0» je Sekunde, Koordinaten, id % 2 für den Besitzer
- `tests/CnCNew.Simulation.Tests/Tests.Phase13Actions.cs` (86): 1 Stelle: Ereignis-Id 900, world.Tick nur durchgereicht
- `tests/CnCNew.Simulation.Tests/Tests.Phase5.cs` (31,38,60,71,72,142,158,174,190,191,193,203,240,243,247,256,263,314,315,333,334,384,636, …): 31 Stellen: Fixture-INI in ms bzw. je Sekunde oder Kosten/Gesundheit 30 (31–384, 1058), Schadenswerte und Gesundheitsprüfungen (636,638,800,1132,1185,1212), Texte ohne Zeitbezug (893,957)
- `tests/CnCNew.Simulation.Tests/Tests.PlayerAgent.cs` (86,95,98): 3 Stellen: Skriptparameter 30 wird nur aufgezeichnet und zurückgelesen (RecordedSetting == 30), kein Verbraucher
- `tests/CnCNew.Simulation.Tests/Tests.PowerPlant.cs` (52,61): 2 Stellen: Fixture-INI HealthPercentToDrainPerSecond = 30% und Braking = 60.0 (je Sekunde)
- `tests/CnCNew.Simulation.Tests/Tests.Projectiles.cs` (31,42,50,59,67,74,80,86,98,111,112,128,129,147,294,296,307,338,442): 19 Stellen: Fixture-INI in ms, je Sekunde, Prozent oder Höhen (31–147), Geometrie im Kommentar (294,296), Variable impactTick = -1 (307,338), Koordinaten (442)
- `tests/CnCNew.Simulation.Tests/Tests.ReferenceMatch.cs` (21,26,27): 3 Stellen: Fixture-INI DelayBetweenShots = 500 (ms), Acceleration/Braking = 30.0 (je Sekunde)
- `tests/CnCNew.Simulation.Tests/Tests.Repair.cs` (139,187,216,241): 4 Stellen: Schadenswert 30 und Gesundheitsprüfung == 30
- `tests/CnCNew.Simulation.Tests/Tests.RetailModules.cs` (79,217,218,313): 4 Stellen: Fixture-INI ExitDelay = 3000, WorkerRespawnDelay = 1000 (ms), HoleHealthRegen%PerSecond = 30%; Schadenswert 30
- `tests/CnCNew.Simulation.Tests/Tests.Scripting.cs` (33,52,132,182): 4 Stellen: Vergleichsoperator-Nummern, Skriptparameter delaySeconds = 0 (Sekunden), Fixture-INI DelayBetweenShots = 100 und ReplacementDelay = 4000 (ms)
- `tests/CnCNew.Simulation.Tests/Tests.Skirmish.cs` (54,168,175): 3 Stellen: Fixture-INI RepairHealthPercentPerSecond = 10%, DelayBetweenShots = 400 (ms), Braking = 60.0 (je Sekunde)
- `tests/CnCNew.Simulation.Tests/Tests.Slaves.cs` (24,32,33,42,48,89,140,160,195,200,247,258,259,277,278,318,320,410,430,431,631,734,783,790, …): 25 Stellen: Fixture-INI in ms bzw. je Sekunde, Reichweiten und Gesundheit 30 (24–431), Gesundheits-/Schadenswerte 30 (631,734,783,790), Abstand < 30 (996)
- `tests/CnCNew.Simulation.Tests/Tests.StagedCapture.cs` (113): 1 Stelle: Doc-Kommentar 'One observed tick', kein Zahlenwert
- `tests/CnCNew.Simulation.Tests/Tests.State.cs` (43,47,54,55,76,337,343,355,379,464): 10 Stellen: Kommentare und Meldungen mit dem Wort Tick, Modulo auf Schleifenindex (Spielerwahl), Schleifenindex == 2, Regex für Wandzeit
- `tests/CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs` (53,60,64,68,71,76,85,86,129,130,131,133,134,155,157,163,164,165,353,358,359,403,427,428, …): 38 Stellen: Fixture-INI in ms (über ModuleConfigReader.Ticks), Schaden/Radien/Höhen, Locomotor-Braking je s, CollapseDamping als dimensionsloser Faktor (1 - Damping) auf die Gravitation; DegenPercentPerSecond: Umrechnung im Modul nicht geprüft
- `tests/CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs` (897,900,901,1069,1073,1075,1157,1308,1311,1326,1350): 11 Stellen: Positionen, Abstände und Höhen (30, 450), Modulo auf Spaltenindex, Meldungstexte
- `tests/CnCNew.Simulation.Tests/Tests.Supply.cs` (15,90,91,158,281): 5 Stellen: Geldwert 50, Fixture-INI in ms (100, 2000), Befehlssequenznummer 900
- `tests/CnCNew.Simulation.Tests/Tests.Teams.cs` (27,154): 2 Stellen: Erfahrungswerte (ExperienceValue 10 30 90 300, Experience == 30)
- `tests/CnCNew.Simulation.Tests/Tests.Terrain.cs` (79,89,92,93,97,143,160): 7 Stellen: Koordinaten und Polygonpunkte mit 30, Modulo auf Höhenfeldindex
- `tests/CnCNew.Simulation.Tests/Tests.Transports.cs` (108,109,117,122,202,237,238,337): 8 Stellen: Fixture-INI in ms (0, 99999999, 300), Streuabstand 20..30, Zähler insideTicks nur auf > 0 geprüft
- `tests/CnCNew.Simulation.Tests/Tests.TurretAttack.cs` (30,92,93,94,377,527,624): 7 Stellen: Fixture-INI je Sekunde bzw. in ms (Braking 300, Scan 1000/2000 ms), Winkel in Grad, Position x 30, Zufallsstrom-Vergleich
- `tests/CnCNew.Simulation.Tests/Tests.Turrets.cs` (30,37,154,249,269,459,460): 7 Stellen: Fixture-INI in ms bzw. Grad je Sekunde, DegreesToRadians (ratenunabhängig), Vergleiche gegen SimulationTiming.DegreesPerSecondToRadiansPerTick (Erwartung skaliert mit)
- `tests/CnCNew.Simulation.Tests/Tests.Veterancy.cs` (15,40,66,90,106,129,215): 7 Stellen: Erfahrungswerte 10 30 90 300 und deren Summe, DelayBetweenShots 500 ms in der Fixture
- `tests/CnCNew.Simulation.Tests/Tests.Vision.cs` (23,88,125,214): 4 Stellen: DelayBetweenShots 0, VisionRange 30, Position (30, 30), Meldungstext
- `tests/CnCNew.Simulation.Tests/Tests.WeaponSlots.cs` (29,36,44,52,61,75,76,365,541,542,543,596,646): 13 Stellen: Fixture-INI in ms (DelayBetweenShots), Locomotor Acceleration/Braking 30 je s², Positionen y = 30, Kommentar
- `tests/CnCNew.Simulation.Tests/Tests.WeaponSlotsIntegration.cs` (28,57,58,210,218,328,335,386): 8 Stellen: Fixture-INI in ms; OCLLifetimePerSecond 1000 wird als (Ticks * Wert / 1000) verrechnet und ist ratenneutral; Pickup(30) ist eine x-Position; SimulationTiming.SleepForever
- `tests/CnCNew.Simulation.Tests/Tests.WeaponSlotsReal.cs` (72,96): 2 Stellen: Kommentare 'fire order every tick', kein Zahlenwert
- `tests/CnCNew.Simulation.Tests/Tests.World.cs` (30,33,36,39,42,45,50,53,56,59,62,65,68,71,74,77,80,83,86,89,92,95,98,101,104,107,110,113, …): 33 Stellen: Parameter 'long? tick = null' = Ausführungstick eines Befehls (Vorgabe world.Tick), keine Dauer, kein Ratenbezug; Timer(delay, repeat) reicht Ticks durch
- `tests/CnCNew.Simulation.Tests/Tests.World.cs` (158,161,268,294,319,521): 6 Stellen: Schaden 30, Seed 4242, Fixture-INI in ms (0, -1), SimulationTiming.SleepForever als Vorgabe, Sequenznummer 30
- `tests/CnCNew.Ui.Tests/SentenceLayoutTests.cs` (84,103,105): 3 Stellen: Umbruchbreite 30 und Blockbreite 30 in Pixeln
- `tests/CnCNew.Ui.Tests/TextAndSettingsTests.cs` (65,70,205,251,253,265,267,268,540,541,732,734): 12 Stellen: return 0/1, Formatangaben, INI-Fixturewerte in Millisekunden bzw. je Sekunde, Auflösung 1600x900, Regex.Match
- `tests/CnCNew.Ui.Tests/TransitionTests.cs` (445,543,844): 3 Stellen: 50,30 und 480 / 16 = 30 sind Pixelwerte; maxFrames = 0 ist ein Zähler
- `tests/CnCNew.Ui.Tests/UiRenderingTests.cs` (112,113,182,198,232,233,374): 7 Stellen: Zufallsgrößen 1..30 Pixel, Modulo für Testalpha, Seitengröße 5x4
- `tests/CnCNew.Ui.Tests/WndFixtures.cs` (32,62): 2 Stellen: TOOLTIPDELAY in Millisekunden (Standard -1), Farbwerte 10 20 30
- `tests/CnCNew.Ui.Tests/WndTests.cs` (17,105,108,154,160,238,265,279,309,607,608,681,686,935,984,1041,1042,1092,1143,1194,1269, …): 25 Stellen: Auflösungen, Rechtecke, Farben 10,20,30, Spaltenbreiten 30/70, Bildgrößen (10, 30), Schieberegler-Drag, Stopwatch-Messung
- `tests/CnCNew.Ui.Tests/WndTests.cs` (101,865,867,868,877): 5 Stellen: Tooltip-Verzögerungen und UiWindowTree.AdvanceTo in Millisekunden der UI-Uhr (1000, 1799, 1800, 2650 ...): Wandzeit, kein Tick
- `tests/CnCNew.W3D.Tests/Program.cs` (122,213,291,385,387): 5 Stellen: return 0, Farbbytes 10/20/30, Texturmapper-Argument FPS=30 (W3D-Materialdaten, in Sekunden ausgewertet)
- `tests/CnCNew.W3D.Tests/SkeletonTests.cs` (112,145,150,177-187,189,329,330,345,346,511,515,543,633): 23 Stellen: W3dAnimationClock.GetFrame(mode, frames, frameRate, seconds) rechnet Animationsbilder aus Sekunden und der Bildrate der Datei (10 bzw. 30 fps im Header der Fixture), Stopwatch, Delta-Kodierung; kein Simulationstick

## Anhang B: Treffer, nicht geprüft

Diese Zeilen hat ein Muster getroffen, sie wurden aber **nicht** mit ihrer Umgebung gelesen. Sie stehen in keiner Tabelle dieses Dokuments.

| Projekt | nicht geprüfte Trefferzeilen (nur schwache Muster N11, N12, N15, N16) | nicht geprüfte Trefferzeilen starker Muster |
|---|---|---|
| `CnCNew.Ui` | 7 | 0 |
| `CnCNew.Audio` | 1 | 0 |
| `CnCNew.Data` | 3 | 0 |
| übrige src | 2 | 0 |
| tools | 5 | 0 |
| tests | 1890 | 0 |
| **Summe** | 1908 | 0 |

Die schwachen Muster sind `.Tick`/`world.Tick`, `Fixed.From…`, `sleep|wake` und die Zahlen 15/45/60/90/120/150/300/600. In den Tests sind das überwiegend Zeilen, die den Welt-Tick lesen, Fixture-Zahlen und Schleifen; ein Teil davon sind Tickerwartungen, die in §7 fehlen. Außerdem nicht geprüft:

- 0 als „U“ gemeldete Zeilen der Lesedurchgänge.
- 51 `writer`-Zeilen der Simulation mit zeitartigem Namen, die keinem Eintrag des Anhangs C direkt zugeordnet sind (§4.3).
- Dateien ohne Treffer starker Muster in den treffergetriebenen Projekten wurden nicht geöffnet (Zahlen in §1.3, Spalte „nicht gelesen“); `Data\Registry/ModuleCatalog.cs` (154 KB) nur um die Treffer und mit einer Namenssuche – weitere nur als `Real`/`Integer` typisierte Bildwerte sind dort möglich (unbestätigte Kandidaten: `RelativeSpeed`, `SpecialAccelFactor`, `ManualDrivingSpeed`, `RandomSplashSoundFrequency`, `OCLLifetimePerSecond`, `ExecuteAnimationTime`).
- Skripte (`scripts/*.ps1`), die den Client mit `--frames`, `--every`, `--input-script` aufrufen oder Tickzahlen aus Werkzeugausgaben lesen; die nirgends gepinnten Prüfsummen von `UiRenderTool`, Capture- und Audio-Capture-Läufen; Strides Standard für die Spielschleife.
- Referenzquelltext: Funktionsabläufe hinter den Konstanten (z. B. `Locomotor::moveTowardsPosition…`, `SlowDeathBehavior::beginSlowDeath`, `GameWindowTransitions*.cpp`, `PartitionManager`) – überall dort steht „Herkunft unbelegt“ oder „nicht nachgeschlagen“.
- Nichts wurde bei 60 Hz ausgeführt; ob der HUD-Hash an der Schrittzahl hängt, ist offen (§4.1); die Wirkung der Laufzeitrundung auf Feuerraten mit Bonus ist nicht gemessen (§4.4).

## Anhang C: Zustandsfelder mit Tickbezug (Schlüssel je Datei)

Art: **absolut** = absoluter Tick; **Rest** = verbleibende Ticks; **Dauer** = Dauer/Intervall in Ticks; **je Tick** = Größe je Tick. Schlüssel so, wie sie der Lesedurchgang notiert hat (teils als Pfad, z. B. `turrets[].holdUntil`).

- `Game\Save/SaveGame.cs` – absolut: `tick`
- `Sim\Commands/Commands.cs` – absolut: `tick`; Dauer: `delay`, `repeat`
- `Sim\Events/SimEvent.cs` – absolut: `tick`, `value`
- `Sim\Mission/MissionSnapshot.cs` – absolut: `maxTicks`; Dauer: `presentationLatencyTicks`, `combatIntervalTicks`
- `Mod\AirModules.cs` – absolut: `nextWake`, `nextHeal`, `arrivals[].tick`
- `Mod\AirWeaponModules.cs` – absolut: `nextWake`, `dieTick`, `playTick`, `nextWake`, `startTick`, `nextWake`, `nextWake`, `nextWake`, `launchTick`, `nextWake`, `destructionTick`, `activationTick`, `nextWake`, `timeoutTick`, `nextWake`, `orbitEscape`; Dauer: `howitzerCounter`; je Tick: `scale`, `velocity`
- `Mod\BattlePlanModule.cs` – absolut: `nextWake`, `nextReadyTick`
- `Mod\BridgeModules.cs` – absolut: `nextWake`, `deathTick`, `nextWake`; Dauer: `firedUpTo`; je Tick: `lateralSpeed`, `verticalSpeed`
- `Mod\CleanupAndSpyModules.cs` – absolut: `nextWake`, `nextWake`, `deactivateTick`, `disabledUntil`, `nextWake`; Rest: `nextScan`
- `Mod\CombatModules.cs` – absolut: `nextWake`, `initialDelay`, `ready`
- `Mod\ContainChainModules.cs` – absolut: `scuttledTick`
- `Mod\ContainModules.cs` – absolut: `nextWake`, `nextWake`, `contained[].since`
- `Mod\CrateModules.cs` – absolut: `nextWake`, `nextWake`
- `Mod\DieAndUpdateModules.cs` – absolut: `nextWake`, `dieTick`
- `Mod\DockMachine.cs` – absolut: `dockMachine.enterTick`, `dockMachine.nextActionTick`
- `Mod\DockModules.cs` – absolut: `nextWake`, `nextWake`, `depositOnTick`
- `Mod\FerryModules.cs` – je Tick: `pullPerTick`, `pushPerTick`
- `Mod\FieldEffectModules.cs` – absolut: `nextWake`, `lastScan`, `claims[].expiry`, `nextWake`; Dauer: `life`; je Tick: `inertiaX`, `inertiaY`
- `Mod\FlightModules.cs` – absolut: `nextWake`, `destructionTick`, `deathTick`, `groundTick`, `nextWake`, `reactionTick`, `nextVolleyTick`, `reloadTick`, `flares[].added`, `reentryTick`, `deliverStart`, `nextWake`, `pendingPathTick`, `startAttack`, `startDecay`, `decayStart`, `nextPulse`, `nextScorch`, `nextLaunchFx`; Rest: `dropDelayLeft`; Dauer: `(run.Describe)`; je Tick: `fallSpeed`, `flares[].vx`, `flares[].vy`, `fallSpeed`
- `Mod\HackerAndAssaultModules.cs` – absolut: `hackedUntil`, `lastShot`; Rest: `hackRemaining`
- `Mod\JetAIUpdate.cs` – absolut: `whenTick`, `transferTick`, `reloadDone`, `returnTick`, `attackExpire`, `checkAirfield`; je Tick: `verticalSpeed`
- `Mod\LastBlockerModules.cs` – absolut: `nextWake`, `nextWake`, `suppressedUntil`, `nextHealing`
- `Mod\MatchModules.cs` – absolut: `nextWake`
- `Mod\MineModules.cs` – absolut: `nextWake`, `nextWake`, `nextDeathCheck`; Rest: `scanCountdown`
- `Mod\Phase12Modules.cs` – absolut: `nextWake`, `reactionReady0..3`, `continuousReady0..3`, `nextWake`, `nextWake`, `lastRefresh`
- `Mod\Phase5BlockerModules.cs` – absolut: `aflameEndTick`, `burnedEndTick`, `damageEndTick`, `lastFlameDamageTick`, `nextWake`, `destructionTick`, `nextWake`, `nextWake`, `nextWake`, `poisonDamageTick`, `poisonStopTick`
- `Mod\Phase5Modules.cs` – absolut: `nextWake`, `readyTick`, `detectedUntilTick`, `nextWake`, `nextWake`, `paralyzedUntil`, `blockerReplanTick`, `nextWake`, `disabledUntil`; Rest: `framesGranted`, `queue[].remaining`; je Tick: `speedCap`
- `Mod\PhysicsAndMinefieldModules.cs` – absolut: `nextWake`, `nextWake`; je Tick: `velocityX`, `velocityY`, `velocityZ`
- `Mod\ProductionExitModules.cs` – absolut: `nextWake`; Rest: `delay`
- `Mod\ProjectileModules.cs` – absolut: `nextWake`, `lifespanTick`, `nextWake`, `stateTick`, `fuelExpiration`, `decoyTick`, `nextWake`; Rest: `step`, `nextScan`, `nextShot`; Dauer: `segments`; je Tick: `speed`, `speed`, `maxSpeed`, `acceleration`, `verticalSpeed`
- `Mod\RailroadModules.cs` – absolut: `nextWake`; Rest: `waitTimer`; Dauer: `wantsLead`; je Tick: `pull.speed`, `conductorPull.speed`
- `Mod\RepairModules.cs` – absolut: `nextWake`; Rest: `nextScan`; je Tick: `healthPerTick`
- `Mod\RetailMatchModules.cs` – absolut: `nextWake`, `contained[].since`, `extendDoneTick`, `extendDoneTick`, `nextWake`, `nextWake`; Rest: `workerWait`
- `Mod\SlaveModules.cs` – absolut: `nextWake`, `nextWake`, `occupiers[].tick`; Rest: `framesToWait`, `framesToWait`; Dauer: `crisisTimer`
- `Mod\SpawnModules.cs` – absolut: `nextWake`, `workTick`, `replacementTimes[].tick`
- `Mod\SpecialAbilityStaging.cs` – absolut: `nextWake`; Rest: `abilityAnim`, `abilityPrep`; je Tick: `abilityFlash`
- `Mod\SpecialPowerModules.cs` – absolut: `availableOnFrame`, `pausedOnFrame`, `nextWake`, `pendingGunships[].tick`
- `Mod\SpecialsModules.cs` – absolut: `nextWake`, `nextWake`, `nextWake`; je Tick: `radius`
- `Mod\StructureModules.cs` – absolut: `nextWake`, `collapseTick`, `burstTick`, `nextWake`, `deployFrame`, `nextWake`; je Tick: `velocity`
- `Mod\ToppleAndFallModules.cs` – absolut: `nextWake`, `nextWake`, `toppleFrame`, `doneAtTick`, `nextWake`, `earliestDeathTick`; je Tick: `angularVelocity`, `angularAcceleration`, `velocity`, `velocityFine`, `structuralIntegrity`, `lastZ`
- `Mod\TransportModules.cs` – absolut: `nextWake`, `destructionTick`, `lastSelfSpinUpdate`, `hitGroundTick`, `nextWake`; Rest: `bladeFlyOff`; je Tick: `forwardSpeed`, `selfSpin`, `fallSpeed`
- `Mod\VehicleModules.cs` – absolut: `nextWake`, `groundCheckTick`, `penaltyDeathTick`, `destructionTick`; je Tick: `verticalSpeed`
- `Mod\WorldEffectModules.cs` – absolut: `nextWake`, `nextCreation`, `timerStarted`, `nextWake`, `doneForever`, `nextWake`, `startTick`, `nextWake`, `activeTick`, `splashTick`, `nextWake`, `nextWake`, `nextWake`, `lastDamage`, `nextWake`, `nextPulse`, `active[].expiry`; Rest: `stateCountdown`, `changeCountdown`, `scanDelay`, `scanDelay`, `startDelay`; Dauer: `timeActive`
- `Sim\Scripting/ScriptEngine.Phase13.cs` – absolut: `transportStatus[].frame`
- `Sim\Scripting/ScriptEngine.Sequential.cs` – Rest: `sequences[].queue[].wait`
- `Sim\Scripting/ScriptEngine.TeamAbilities.cs` – absolut: `buttonHunts[].next`; Dauer: `hulkLifetimeOverride`
- `Sim\Scripting/ScriptEngine.TeamQueues.cs` – absolut: `teamQueue[].started`
- `Sim\Scripting/ScriptEngine.cs` – absolut: `tick`, `outcomeTick`, `scripts[].next`, `presentation[].until`; Rest: `endTimer`, `counters[].value`
- `Sim\Templates/ObjectCreationListCatalog.cs` – Dauer: `runDropDelay`
- `Sim\Timers/SimTimer.cs` – absolut: `due`; Dauer: `repeat`
- `Sim\World/AcquisitionState.cs` – absolut: `orderedTick`, `nextCheck`, `chaseTick`
- `Sim\World/Entity.cs` – absolut: `spawnTick`, `weaponReadyTick`, `lastDamageTick`, `lastFireTick`, `empDisabledUntil`, `sellCompleteTick`; Dauer: `constructionWork`; je Tick: `speed`
- `Sim\World/SimulationWorld.FiringTracker.cs` – absolut: `cooldownFrame`, `preFinish`
- `Sim\World/SimulationWorld.Projectiles.cs` – absolut: `due`
- `Sim\World/SimulationWorld.Snapshot.cs` – absolut: `tick`, `frame`, `powerSabotagedUntil`; Dauer: `turretWaits`, `minLifetime`, `maxLifetime`, `deliveryDelay`
- `Sim\World/SimulationWorld.cs` – absolut: `tick`, `players[].sharedReadyFrames[].frame`, `players[].powerSabotagedUntil`, `events[]`; Dauer: `spawnQueue[].minLifetime`, `spawnQueue[].maxLifetime`, `spawnQueue[].deliveryDelay`
- `Sim\World/TunnelTracker.cs` – absolut: `tunnelRiders[].since`
- `Sim\World/TurretState.cs` – absolut: `turrets[].holdUntil`, `turrets[].sweepUntil`, `turrets[].loseInterestAt`, `turrets[].nextScan`
- `Sim\World/WeaponSlotState.cs` – absolut: `weaponSlots.ready1`, `weaponSlots.ready2`, `weaponSlots.fire0`, `weaponSlots.fire1`, `weaponSlots.fire2`
