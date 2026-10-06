# Tickrate als Wert der Welt – Umstellung (Phase 20)

Stand: 6. Oktober 2026, Stufe 1 der Phase 20 (`docs/HANDOFF-PHASE20.md` Punkte 2, 3 und 6). Arbeitsdokument für Stufe 2 (nackte Tickzahlen dateiweise abtragen) und für Phase 23 (richtiges Verhalten bei 60 Hz). Grundlage ist das geprüfte Inventar `docs/TICKRATE-AUDIT.md`; Klassen (A bis H) und Widersprüche (W1 bis W10) sind dort definiert.

**Die eiserne Regel:** Bei 30 Hz ändert sich nichts – kein Hash, kein Snapshot-Byte, kein Replay-Byte. Beweis ist `SimTool reference-manifest check <content> baselines/reference-manifest.json` (Stufe `quick` nach jedem Commit, `full` am Ende) und `SimTool input-manifest check <content> baselines/input-manifest.json`. Ein Zwischenstand, der das Manifest nicht besteht, ist ein Fehler im Umbau, nie ein neuer Referenzwert.

## 1. Konvention für Stufe 2

Jede Stelle, die Zeit meint, geht über den Zeitdienst – zur Laufzeit `world.Time` (auf `IWorldServices`, in der Skript-VM `_world.Time`), beim Katalogbau `configuration.Time` (Modul-`Parse`) bzw. der Parameter `timing` der Katalogbauer. Kein Modul rechnet selbst mit einer Rate. Konstanten bleiben `const`, aber in **Bildern des Originals** (Endung `Frames`) oder in Sekunden (Endung `Seconds`), nie in Ticks; die Umrechnung steht an der Verwendung. Der Quellverweis im Kommentar bleibt.

| Fall | So schreiben | Beispiel aus Stufe 1 |
|---|---|---|
| **(a) Originalkonstante in Bildern** | `private const long XFrames = N;` und an der Verwendung `world.Time.OriginalFrames(XFrames)` | `SpawnBehavior` (`Modules/BuiltIn/SpawnModules.cs`): `UpdateRateFrames = SimulationRate.OriginalFramesPerSecond / 2`; `_workTick = world.Tick + world.Time.OriginalFrames(UpdateRateFrames)`. `ParkingPlaceBehavior` (`AirModules.cs`): `HealRateFrames` (`HEAL_RATE_FRAMES`). |
| **(b) Zeitwert, der als Tickzahl hingeschrieben war** | wie (a): der bisherige Wert ist die Zahl der Bilder des Originals (30 Hz bleibt exakt). Steht der Wert für ganze Sekunden, `XSeconds` und `world.Time.WholeSecondsToTicks(XSeconds)` | `SimulationWorld.FiringTracker.cs`: `PreAttackStaleFrames = SimulationRate.OriginalFramesPerSecond`, `state.PreFinish + Time.OriginalFrames(PreAttackStaleFrames)`. `ScriptEngine.Phase13.cs`: `SupplyScanSeconds = 10`, `_world.Time.WholeSecondsToTicks(SupplyScanSeconds)`. `DockMachine.cs`: `ClearanceTimeoutFrames = 30L * SimulationRate.OriginalFramesPerSecond`. |
| **(c) Ablaufrhythmus mit Modulo** | Periode **und** Phase in Bildern, beide skaliert: `tick % world.Time.OriginalFrames(PeriodFrames) == world.Time.OriginalFrames(PhaseFrames)`. Bei 30 Hz ist das der alte Ausdruck; bei 60 Hz fällt der Rhythmus auf dieselben Spielzeitpunkte. Eine aus einem Hash gezogene Phase wird in Bildern gezogen und dann skaliert | `ScriptEngine.Phase13.cs` `UpdatePendingStructures`: `tick % _world.Time.TicksPerSecond != 0` (einmal je Sekunde, Phase 0). `ScriptEngine.cs`: `_nextEvaluation[index] = _world.Time.OriginalFrames((long)(NameHash(script.Name) % (2 * SimulationRate.OriginalFramesPerSecond + 1)))`. |
| **(d) Ganzzahlige Teilung der Rate** | Teilung der **Originalrate** als Konstante in Bildern, dann skalieren: `SimulationRate.OriginalFramesPerSecond / 4` ist 7 Bilder bei jeder Rate (14 Ticks bei 60 Hz, nicht `60 / 4 = 15`) | `SlavedUpdate` (`SlaveModules.cs`): `UpdateRateFrames = SimulationRate.OriginalFramesPerSecond / 4`; `_framesToWait = checked((int)world.Time.OriginalFrames(UpdateRateFrames))`. |
| **(e) Literal, das Reihenfolge meint (W2)** | **nicht skalieren.** Mit `TickOrder.Next` schreiben (`Time/TickOrder.cs`): `now + TickOrder.Next`, `frame - TickOrder.Next`, `now + 2 * TickOrder.Next`. Vergleiche „im selben Tick“ (`LastFireTick == world.Tick`) brauchen keine Markierung. `SimulationTiming.SleepNone` (kleinster Schlaf eines Updates, ein Tick) ist dieselbe Art und bleibt | `JetAIUpdate.cs`: `_transferTick = now + TickOrder.Next` bzw. `now + 2 * TickOrder.Next`. `ScriptEngine.Phase13.cs` (`UNIT_EMPTIED`): `status.Frame == frame - TickOrder.Next`. |
| **(f) Bildzähler der Kartenskripte** | Der Wert der Karte ist Bilder des Originals; er wird skaliert, sobald er ein Tick-Countdown wird. Zufall wird in Bildern gezogen, **dann** skaliert (der gezogene Wert bleibt auf dem 30er-Raster). Sekundenwerte: aufrunden auf Bilder des Originals, dann skalieren | `ScriptEngine.Actions.cs` `SET_TIMER`/`SET_RANDOM_TIMER`: `timer.Value = _world.Time.OriginalFrames(frames)`; `SecondsToFrames`: `_world.Time.OriginalFrames((seconds * SimulationRate.OriginalFramesPerSecond).Ceiling())`. `ScriptEngine.Sequential.cs` `SetSequenceTimer` (`*_FOR_FRAMECOUNT`). `ScriptEngine.TeamQueues.cs`: `_world.Time.OriginalFrames(prototype.InitialIdleFrames)`. |

Weitere Regeln:

- **Werte je Sekunde zur Laufzeit** (Anteil der aktuellen Lebenspunkte u. ä.): `wert / world.Time.TicksPerSecondFixed` bzw. `world.Time.PerSecondToPerTick(wert)` – derselbe Ausdruck wie das frühere `/ Fixed.FromInt(30)`, Reihenfolge der Operanden unverändert lassen (die Rundung von `Fixed` hängt daran). Beispiel: `RetailMatchModules.cs`, `body.MaxHealth * data.HealthDrainRatioPerSecond / world.Time.TicksPerSecondFixed`.
- **Ersatzwerte von `ModuleConfigReader.Ticks(configuration, name, fallbackOriginalFrames)`** sind Bilder des Originals und werden vom Leser skaliert. Kein Aufrufer rechnet den Ersatzwert vor.
- **Additive Rohwerte je Bild** (Geschwindigkeit je Bild, Zuwachs je Bild) beim Katalogbau über `configuration.Time.PerOriginalFrameToPerTick(…)` bzw. `PerOriginalFrameSquaredToPerTickSquared(…)`; zur Laufzeit (Literale im Rumpf) über `world.Time.…`. **Multiplikative Faktoren je Bild bleiben unverändert** und kommen auf die Liste in §5.
- **W1 – Rundung zur Laufzeit in Ticks** (`Ticks / Multiplikator`, „mindestens ein Tick“, Zähler mit Periode N + 1, Zufall über Tickbereiche): in **Bildern des Originals** rechnen und erst das Ergebnis skalieren – `world.Time.OriginalFrames(world.Time.ToOriginalFrames(ticks) / faktor)` bzw. den Zufall über `ToOriginalFrames(min)…ToOriginalFrames(max)` ziehen. `ToOriginalFrames` ist bei 30 Hz die Identität. Diese Stellen sind in Stufe 1 **nicht** angefasst.
- **Zustandsfelder** behalten ihre Namen (Snapshot-Schlüssel und der Inhalts-Hash des Eingabemanifests hängen an Feldnamen). Neue Instanzfelder an Katalogtypen (`ObjectTemplate`, `WeaponInfo`, jede `…Data` eines Moduls) ändern das Eingabemanifest – Konstanten (`const`, `static`) nicht.
- **Kein stiller Standard:** keine Simulationsfunktion bekommt einen Standardparameter für die Rate. Wer eine Welt oder einen Katalog erzeugt, nennt die Rate.

## 2. Der Zeitvertrag

`src/CnCNew.Simulation/Time/`:

- **`SimulationRate`** – unveränderlicher Wert, genau drei Instanzen (`Hz30`, `Hz60`, `Hz120`; `Original` = `Hz30`). `FromTicksPerSecond(n)` wirft `UnsupportedSimulationRateException` für alles andere. `OriginalFramesPerSecond = 30` ist die einzige benannte 30 der Simulation und wird nur in Umrechnungen und in Konstanten „in Bildern“ benutzt. `Combine(fingerprint)` faltet die Rate in den Definitions-Fingerabdruck (bei 30 Hz unverändert, §4).
- **`SimulationTime`** – der Zeitdienst einer Rate (`rate.Time`, je Rate eine Instanz; keine Allokation, keine Umrechnung im heißen Pfad außer einer Multiplikation bzw. einer `Fixed`-Division).
- **`SimulationTiming`** – nur noch ratenunabhängig: `SleepNone`, `SleepForever` (Wächterwert, nie skaliert), `MillisecondsPerSecond`, `DegreesToRadians`, `RatioToFixed`. Die Konstante `LogicFramesPerSecond` gibt es in `src/CnCNew.Simulation` nicht mehr.
- **`TickOrder`** – `Next = 1` für Literale, die Reihenfolge meinen.

| Funktion von `SimulationTime` | Rechnung | Rundung | Warum 30 Hz unverändert ist |
|---|---|---|---|
| `OriginalFrames(n)` | `n · (Rate / 30)` | exakt (Faktor 1, 2, 4); Überlauf wirft | Faktor 1 |
| `ToOriginalFrames(ticks)` | `floor(ticks · 30 / Rate)` | abgerundet | Identität |
| `IsOriginalFrameBoundary(tick)` | `tick % (Rate / 30) == 0` | – | immer wahr |
| `DurationMillisecondsToTicks(long)` | `((ms · 30 + 999) / 1000) · (Rate / 30)` | auf ganze Bilder des Originals **auf**gerundet (ganzzahlig), dann exakt skaliert | derselbe Ganzzahlausdruck, mal 1 |
| `DurationMillisecondsToTicks(decimal)` | `Ceiling(ms · 30 / 1000) · (Rate / 30)` | wie oben, in `decimal` | derselbe Dezimalausdruck, mal 1 |
| `DurationMillisecondsToFixedTicks(decimal)` | `ms · Rate / 1000` | einmal auf `Fixed` (nächster Wert, Halbwerte weg von null); **nicht** auf dem Raster, weil das Original (`parseDurationReal`) hier nicht rundet | `ms · 30 / 1000` wie zuvor |
| `SecondsToTicks(decimal)` | `Ceiling(s · 30) · (Rate / 30)` | auf Bilder des Originals aufgerundet | derselbe Ausdruck, mal 1 |
| `WholeSecondsToTicks(long)` | `s · Rate` | exakt | `s · 30` |
| `PerSecondToPerTick(decimal)` | `v / Rate` | einmal auf `Fixed` | `v / 30` |
| `PerSecondToPerTick(Fixed)`, `TicksPerSecondFixed` | `v / Fixed(Rate)` | eine `Fixed`-Division | früher `v / Fixed.FromInt(30)` |
| `PerSecondSquaredToPerTickSquared(decimal)` | `a / (Rate · Rate)` | einmal auf `Fixed` | `a / 900` |
| `DegreesPerSecondToRadiansPerTick(decimal)` | `deg · π / 180 / Rate` (in dieser Reihenfolge) | einmal auf `Fixed` | letzter Divisor 30 wie zuvor |
| `PerOriginalFrameToPerTick(decimal)` | `v / (Rate / 30)` | eine Dezimaldivision durch 1, 2 oder 4, dann einmal auf `Fixed` | Division durch 1 ist die Identität: `Fixed.FromDecimal(v)` wie beim rohen Lesen |
| `PerOriginalFrameToPerTick(Fixed)` | `v / Fixed(Rate / 30)` | eine `Fixed`-Division | bei 30 Hz wird der Wert **ohne Rechnung** zurückgegeben |
| `PerOriginalFrameSquaredToPerTickSquared(decimal / Fixed)` | `v / (Rate / 30)²` | Division durch 1, 4 oder 16 | wie oben |
| `ToWholeSeconds`, `ToWholeMinutes`, `ToMilliseconds` | `ticks / Rate`, `ticks / (60 · Rate)`, `ticks · 1000 / Rate` | ganzzahlig, zur Null | – |
| `ToSecondsRational(ticks)`, `ToSeconds(ticks)` | `(ticks, Rate)` bzw. `Fixed(ticks) / Fixed(Rate)` | exakt bzw. eine `Fixed`-Division | – |

Der Test `Tests.TickRate.cs` hält die früheren Formeln als Orakel (`FormerTiming`) und vergleicht bei 30 Hz über 120.001 ganze Millisekundenwerte, rund 8.000 Dezimalwerte je Funktion und beide Vorzeichen; bei 60 und 120 Hz prüft er das Raster (Dauer = 30-Hz-Wert mal 2 bzw. 4) und die skalierten Werte.

## 3. Was Stufe 1 umgestellt hat

Gezählt gegen den Stand `a1ab03b` (Vorkommen im alten Quelltext; `OriginalFrames(…)` = Aufrufe im heutigen Quelltext).

| Art | Stellen | Dateien |
|---|---|---|
| Verwendungen der Konstante `LogicFramesPerSecond` | 63 | 32 (davon 9 in `SimulationTiming.cs`) |
| – darunter `const`-Ausdrücke und ein `static readonly` | 9 + 1 | `AirModules`, `DockMachine`, `Phase5Modules`, `ProjectileModules`, `SlaveModules` (2), `SpawnModules`, `FiringTracker`, `ScriptEngine.Phase13`; `TransportModules` (`static readonly`). Die übrigen vier der 13 `const`-Ausdrücke des Inventars (W3) liegen in `CnCNew.Game` und gehören Phase 21 |
| – darunter Divisionen `/ Fixed.FromInt(LogicFramesPerSecond)` | 19 | 13 Moduldateien |
| direkte Umrechnungsaufrufe `SimulationTiming.…(…)` | 41 | 12 |
| Aufrufe `ModuleConfigReader.Ticks` (laufen alle über den Zeitdienst) | 133 | 33 |
| – darunter Ersatzwert ≠ 0, jetzt Bilder des Originals | 15 | 10 |
| additive Rohwerte je Bild, beim Katalogbau skaliert | 9 | 6 |
| Bildzähler der Kartenskripte | 4 Stellen | `ScriptEngine.Actions.cs` (`SET_TIMER`, `SET_RANDOM_TIMER`), `ScriptEngine.Sequential.cs` (`SetSequenceTimer`: fünf `*_FOR_FRAMECOUNT`-Aktionen), `ScriptEngine.TeamQueues.cs` (`teamInitialIdleFrames`) |
| Reihenfolge-Literale als Muster markiert | 3 | `JetAIUpdate.cs` (2), `ScriptEngine.Phase13.cs` (1) |
| Formatstellen (Rate schreiben, prüfen, Welt mit der Rate erzeugen) | 9 | `WorldSnapshot`, `MissionSnapshot`, `ReplayRunner`, `SkirmishMatchSession`, `ReferenceMatchScenario`, `StressScenario` |

Je Datei:

| Datei (`src/CnCNew.Simulation/…`) | Konstante | Umrechnungsaufrufe | `Ticks`-Ersatzwert ≠ 0 | rohe Bildwerte | `OriginalFrames(…)` heute |
|---|---|---|---|---|---|
| `Mission/MissionSnapshot.cs` | 2 |  |  |  |  |
| `Modules/BuiltIn/AirModules.cs` | 2 |  |  |  | 3 |
| `Modules/BuiltIn/AirWeaponModules.cs` |  | 2 | 4 | 1 |  |
| `Modules/BuiltIn/BridgeModules.cs` | 2 | 1 |  |  |  |
| `Modules/BuiltIn/ContainModules.cs` | 1 |  |  |  |  |
| `Modules/BuiltIn/DockMachine.cs` | 1 |  |  |  | 1 |
| `Modules/BuiltIn/FieldEffectModules.cs` | 1 |  | 1 |  |  |
| `Modules/BuiltIn/FlightModules.cs` | 5 |  |  |  |  |
| `Modules/BuiltIn/JetAIUpdate.cs` | 3 |  |  |  |  |
| `Modules/BuiltIn/MatchModules.cs` | 1 |  |  |  |  |
| `Modules/BuiltIn/MineModules.cs` | 2 |  | 1 |  |  |
| `Modules/BuiltIn/ModuleConfigReader.cs` |  | 1 |  |  | 1 |
| `Modules/BuiltIn/Phase12Modules.cs` | 1 |  | 1 |  |  |
| `Modules/BuiltIn/Phase5BlockerModules.cs` |  |  | 1 |  |  |
| `Modules/BuiltIn/Phase5Modules.cs` | 2 | 4 | 1 | 1 |  |
| `Modules/BuiltIn/ProjectileModules.cs` | 3 |  | 2 |  |  |
| `Modules/BuiltIn/RailroadModules.cs` |  |  | 1 | 3 |  |
| `Modules/BuiltIn/RepairModules.cs` |  | 1 |  |  |  |
| `Modules/BuiltIn/RetailMatchModules.cs` | 2 |  |  |  |  |
| `Modules/BuiltIn/SlaveModules.cs` | 3 |  |  |  | 3 |
| `Modules/BuiltIn/SpawnModules.cs` | 1 |  |  |  | 2 |
| `Modules/BuiltIn/SpecialsModules.cs` |  |  |  | 1 |  |
| `Modules/BuiltIn/StructureModules.cs` |  |  | 1 |  |  |
| `Modules/BuiltIn/TransportModules.cs` | 2 | 2 |  |  |  |
| `Modules/BuiltIn/VehicleModules.cs` |  |  |  | 1 |  |
| `Modules/BuiltIn/WorldEffectModules.cs` | 2 |  | 2 |  | 1 |
| `Replay/ReplayRunner.cs` | 2 |  |  |  |  |
| `Scripting/ScriptEngine.Actions.cs` | 1 |  |  |  | 2 |
| `Scripting/ScriptEngine.Phase13.cs` | 2 |  |  |  |  |
| `Scripting/ScriptEngine.Powers.cs` | 1 |  |  |  |  |
| `Scripting/ScriptEngine.Sequential.cs` |  |  |  |  | 1 |
| `Scripting/ScriptEngine.TeamAbilities.cs` | 1 |  |  |  |  |
| `Scripting/ScriptEngine.TeamQueues.cs` | 1 |  |  |  | 1 |
| `Scripting/ScriptEngine.cs` | 2 |  |  |  | 1 |
| `Skirmish/SkirmishMatchSession.cs` | 1 |  |  |  |  |
| `Snapshots/WorldSnapshot.cs` | 2 |  |  |  |  |
| `Templates/ObjectCreationListCatalog.cs` |  | 2 |  |  |  |
| `Templates/ObjectTemplate.cs` |  | 16 |  |  |  |
| `Templates/SpecialPowerCatalog.cs` |  | 1 |  |  |  |
| `Templates/TurretInfo.cs` | 1 | 5 |  | 2 | 1 |
| `Templates/UpgradeCatalog.cs` |  | 1 |  |  |  |
| `Templates/WeaponCatalog.cs` |  | 5 |  |  |  |
| `Testing/ReferenceMatchScenario.cs` | 1 |  |  |  |  |
| `Testing/StressScenario.cs` | 1 |  |  |  |  |
| `Time/SimulationTiming.cs` | 9 |  |  |  |  |
| `World/SimulationWorld.FiringTracker.cs` | 2 |  |  |  | 1 |
| **Summe** | 63 | 41 | 15 | 9 | 18 |

Die neun skalierten Rohwerte: `TurretInfo.DefaultRate` (0,01 rad je Bild, für fehlende `TurretTurnRate`/`TurretPitchRate`), `SpectreData.StrafingIncrement`, `StealthData.MoveThresholdSpeed` (roh gelesen und mit der Geschwindigkeit je Tick verglichen; das Original liest hier eine Geschwindigkeit je Sekunde – Nebenbefund des Inventars, nicht korrigiert), `RailroadData.RunningGarrisonSpeedMax`/`KillSpeedMin`/`SpeedMax`, `GrantStealthData.RadiusGrowRate`, `BattleBusData.ThrowForce` (wird als Geschwindigkeit je Tick eingesetzt).

Bewusst **nicht** skaliert: `WeaponInfo.InstantSpeed` (999999 je Bild heißt „sofort“; ein Wächterwert, kein Datenwert), der Ersatzwert `TwoPi · 100` für `NeutronMissileData.MaxTurnRate` (jeder Wert ab π rastet sofort ein).

## 4. Katalog je Rate, Fingerabdruck, Eingabemanifest

- `TemplateCatalog.Build(graph, bindings, rate, geometry)` baut den Katalog für eine Rate; `TemplateCatalog.Rate`/`.Time` sagen, für welche. Die Rate läuft als Parameter durch alle Bauer, die Ticks oder Werte je Tick erzeugen (`UpgradeCatalog`, `WeaponCatalog`, `SpecialPowerCatalog`, `ObjectCreationListCatalog`, Locomotoren, `TurretSet.Read`, `ObjectTemplate`, Basisregeneration) und über `RatedModuleConfiguration` (Konfiguration plus Zeitdienst) in jede `Parse`-Funktion eines Moduls.
- `WorldConfiguration.Rate` ist die Rate der Welt. `SimulationWorld` wirft beim Erzeugen, wenn der Katalog für eine andere Rate gebaut wurde. Sitzungen (`SkirmishMatchSession.Start`, `MissionSession.Start`) und Szenarien nehmen die Rate vom Katalog, den sie bekommen: wer den Katalog baut, nennt die Rate, und eine Sitzung kann nicht mit der falschen laufen.
- **Kosten** (gemessen mit `SimTool catalog-cost`, Release, nicht ruhig, 6. Oktober 2026, zwei Läufe): Definitionen übersetzen 4,3 bis 4,5 s und Knochentabelle 0,6 s fallen einmal an und hängen nicht an der Rate; `TemplateCatalog.Build` braucht je Rate 0,08 bis 0,22 s (erster Aufruf mit JIT 0,35 bis 0,38 s). **Ein Zwischenspeicher je Rate ist nicht nötig**; wer zwei Raten braucht, baut zweimal aus demselben Graphen.
- **Fingerabdruck:** `SimulationRate.Combine` lässt den Definitions-Fingerabdruck bei 30 Hz unverändert und faltet bei 60/120 Hz die Rate ein (`SHA-256(fingerabdruck + "+tick-rate:" + n)`). `GameData`, `SyntheticFixture` und die Werkzeuge benutzen das; ein 60-Hz-Replay scheitert an 30-Hz-Daten damit zweifach (Rate und Fingerabdruck).
- **Eingabemanifest:** `baselines/input-manifest.json` hasht die gebauten Kataloge Feld für Feld (`CanonicalObjectHasher`). Der Katalog hat genau **ein** neues Instanzfeld (`TemplateCatalog.Rate`); ohne weitere Maßnahme ändert sich genau ein Wert (`catalogs.whole`), die neun Teilkataloge bleiben gleich (gemessen). Damit der aufgezeichnete 30-Hz-Wert vergleichbar bleibt, lässt der Hasher des Werkzeugs dieses eine Feld aus, **solange es die Originalrate ist** (`InputManifest.RateAtOriginal`); bei jeder anderen Rate wird es beschrieben. Vorbild ist die Regel des Projekts „neuer Zustand wird nur geschrieben, wenn er vom Standard abweicht“. Das ist eine Änderung am Prüfwerkzeug, nicht an der Basislinie, und gehört ins Review. Alle anderen Katalogtypen haben kein neues Feld bekommen.

## 5. Liste für Phase 23 (bei 30 Hz richtig, bei 60 Hz falsch; in Phase 20 unverändert)

Multiplikative Faktoren je Bild und Rekursionen, die Faktor und Zuwachs mischen (W4: die Wurzel des Faktors allein genügt dort nicht):

| Datei (`Modules/BuiltIn/…`) | Symbol | Was |
|---|---|---|
| `RailroadModules.cs` | `RailroadData.Acceleration` (1,01), `Braking` (0,99), `Friction` (0,97) | Faktoren je Bild auf die Zuggeschwindigkeit |
| `RailroadModules.cs` | `RailroadBehavior.PushStart` (0,02), Anfahrwert 0,05, Stillstandsschwelle 0,1, Schaden `_pull.Speed * 10` | Zuwachs je Bild vor dem Faktor (gemischte Rekursion), Geschwindigkeiten je Bild als Literal, Schaden je Tick der Überlappung. `SpeedMax`, `KillSpeedMin`, `RunningGarrisonSpeedMax` sind seit Stufe 1 je Tick – der Zug erreicht bei 60 Hz dieselbe Höchstgeschwindigkeit, aber in der halben Zeit |
| `AirWeaponModules.cs` | `NeutronMissileData.ForwardDamping`, `RelativeSpeed` | Dämpfungsanteil je Bild und Schub je Bild² im selben Schritt (`_velocity + speed·dir − damping·_velocity`) |
| `FieldEffectModules.cs` | `TensileFormationUpdate.Friction` (0,95), `NewShare`/`LinkShare` (0,93/0,07), `BaseSlope` (0,3) | Reibung, Positionsmischung und Hangbeschleunigung je Bild |
| `LastBlockerModules.cs` | `SmartBombHomingData.CourseCorrectionScalar` (0,99) | Mischfaktor der Position je Bild |
| `ToppleAndFallModules.cs` | `StructureToppleData.StructuralDecay` | Zerfall der Strukturintegrität je Bild |
| `ToppleAndFallModules.cs` | `ToppleUpdateData.InitialAccelPercent` | Winkelbeschleunigung je Tick² als Anteil eines Stoßtempos je Tick: braucht einen weiteren Faktor 30/Rate (`InitialVelocityPercent` skaliert mit dem Stoßtempo von selbst, `BounceVelocityPercent` ist ein Verhältnis ohne Zeit) |
| `TransportModules.cs` | `HelicopterSlowDeathData.SpiralOrbitForwardSpeedDamping` | Dämpfung je Bild (`_forwardSpeed *= …`) |

Geprüft und **kein** Faktor je Bild: `StructureCollapseData` `CollapseDamping` (Anteil der Schwerkraft, `gravity · (1 − damping)`; die Schwerkraft je Tick² folgt der Rate).

Dazu alles aus Klasse E des Inventars (138 Stellen: Bewegung, Flug, Geschosse, Umstürzen, Türme) und W1 (Rundungen zur Laufzeit, Untergrenze „ein Tick“, Zähler mit Periode N + 1, Zufall über Tickbereiche) – das ist Verhalten, keine Parametrisierung.

## 6. Was für Stufe 2 bleibt

Die Zahlen sind die des Inventars (`docs/TICKRATE-AUDIT.md` §3.2, Stand `e5046b7`), ohne die mechanischen Umrechnungsaufrufe („B mech.“, 150 – mit Stufe 1 erledigt) und ohne die Klassen F und G. Zeilen des Inventars, deren Symbol in §3 steht, sind erledigt (die 63 Verwendungen der Konstante, die 15 Ersatzwerte, die neun Rohwerte, die Skript-Bildzähler); alles andere ist offen. Zeilennummern des Inventars sind verschoben, die Symbole stimmen. Klasse E bleibt bis auf die Reihenfolge-Literale (Vermerk „Reihenfolge“, Fall (e)) für Phase 23 stehen; Klasse H in `Testing/` sind Fahrer- und Budgetwerte.

**World/** (ein Auftrag)

| Datei | A | B | C | D | E | Summe |
|---|---|---|---|---|---|---|
| `World/Entity.cs` |  |  |  |  | 1 | 1 |
| `World/SimulationWorld.Acquisition.cs` |  | 2 | 4 |  | 1 | 7 |
| `World/SimulationWorld.Collision.cs` |  |  |  |  | 4 | 4 |
| `World/SimulationWorld.Construction.cs` | 1 |  |  |  | 1 | 2 |
| `World/SimulationWorld.CreationLists.cs` | 1 |  |  |  | 1 | 2 |
| `World/SimulationWorld.FiringTracker.cs` | 1 | 1 | 1 |  |  | 3 |
| `World/SimulationWorld.Projectiles.cs` |  |  |  |  | 1 | 1 |
| `World/SimulationWorld.Turrets.cs` | 1 | 3 | 1 |  | 9 | 14 |
| `World/SimulationWorld.Vision.cs` |  |  | 1 |  |  | 1 |
| `World/SimulationWorld.WeaponSlots.cs` |  | 1 |  |  |  | 1 |
| `World/SimulationWorld.cs` |  |  | 1 |  | 3 | 4 |
| **Summe** | 4 | 7 | 8 | 0 | 21 | 40 |

In `FiringTracker.cs` sind die beiden Stellen mit der Konstante erledigt, der Rest der Datei (Stufenfristen) nicht. `SimulationWorld.CreationLists.cs` (`IsSignificantlyAbove`, Handoff „Engstellen“) rechnete mit `/ 900m`: das ist eine Höhe (drei Bilder freier Fall), keine Rate – jetzt mit `OriginalFramesPerSecond²` geschrieben und bei jeder Rate gleich; die Dezimalrechnung je Aufruf bleibt (ein Feld an der Welt könnte sie vorhalten). `TemplateCatalog.GravityPerTickSquared` rechnet weiter bei jedem Zugriff (eine Dezimaldivision); ein Feld am Katalog würde das Eingabemanifest ändern – wenn es stört, den Wert an der Welt vorhalten.

**Modules/BuiltIn/ A–M** (ein Auftrag)

| Datei | A | B | C | D | E | Summe |
|---|---|---|---|---|---|---|
| `AirModules.cs` | 1 | 2 | 1 |  |  | 4 |
| `AirWeaponModules.cs` | 1 | 8 | 2 | 4 | 7 | 22 |
| `BattlePlanModule.cs` |  |  |  |  | 1 | 1 |
| `BridgeModules.cs` |  |  |  | 4 | 1 | 5 |
| `CleanupAndSpyModules.cs` | 1 | 1 | 2 |  |  | 4 |
| `CombatModules.cs` |  |  | 1 |  |  | 1 |
| `ContainChainModules.cs` |  |  | 1 |  | 1 | 2 |
| `ContainModules.cs` |  |  |  |  | 2 | 2 |
| `CrateModules.cs` |  | 2 | 1 |  |  | 3 |
| `DieAndUpdateModules.cs` |  | 1 |  |  |  | 1 |
| `DockMachine.cs` |  | 1 |  |  |  | 1 |
| `DockModules.cs` |  |  | 2 |  |  | 2 |
| `FerryModules.cs` |  |  |  |  | 4 | 4 |
| `FieldEffectModules.cs` | 3 | 1 |  | 3 | 2 | 9 |
| `FlightModules.cs` |  | 12 | 2 |  | 5 | 19 |
| `HackerAndAssaultModules.cs` |  | 1 | 1 |  | 1 | 3 |
| `JetAIUpdate.Flight.cs` |  |  |  |  | 1 | 1 |
| `JetAIUpdate.cs` | 3 | 3 | 1 | 2 | 9 | 18 |
| `LastBlockerModules.cs` |  |  |  | 1 | 2 | 3 |
| `MatchModules.cs` | 1 | 1 |  |  |  | 2 |
| `MineModules.cs` |  | 2 | 1 |  |  | 3 |
| `ModuleConfigReader.cs` |  | 1 |  |  |  | 1 |
| **Summe** | 10 | 36 | 15 | 14 | 36 | 111 |

Hinweise: `MatchModules.cs` – `BaseRegenerateUpdate.HealRate = 3` ist zugleich Schlafdauer und Faktor der Heilmenge (`… * Fixed.FromInt(HealRate) / world.Time.TicksPerSecondFixed`); beide zusammen über `world.Time.OriginalFrames(HealRateFrames)` führen, wie `ParkingPlaceBehavior` es jetzt tut. `BridgeModules.cs` – `BridgeScaffoldBehavior.AbsoluteMinSpeed` (0,001 je Tick) und die Standardgeschwindigkeit 1 je Tick sind additive Rohwerte im Rumpf (Laufzeit, `world.Time.PerOriginalFrameToPerTick`). `JetAIUpdate.cs` – Ersatzgeschwindigkeit `Fixed.One` und Landeuntergrenze 0,5 je Tick ebenso. `DockMachine.cs`, `AirModules.cs` und `ModuleConfigReader.cs` sind erledigt bis auf das, was das Inventar dort sonst nennt.

**Modules/BuiltIn/ N–Z** (ein Auftrag)

| Datei | A | B | C | D | E | Summe |
|---|---|---|---|---|---|---|
| `Phase12Modules.cs` |  | 1 | 2 |  | 2 | 5 |
| `Phase5BlockerModules.cs` |  | 3 | 1 |  | 2 | 6 |
| `Phase5Modules.cs` | 1 | 2 | 2 | 1 | 8 | 14 |
| `PhysicsAndMinefieldModules.cs` |  |  | 1 |  | 1 | 2 |
| `ProjectileModules.cs` | 3 | 3 | 1 |  | 6 | 13 |
| `RailroadModules.cs` | 1 | 1 | 1 | 10 | 4 | 17 |
| `RepairModules.cs` |  | 2 | 1 |  | 1 | 4 |
| `RetailMatchModules.cs` |  | 3 |  |  | 1 | 4 |
| `SlaveModules.cs` | 7 | 4 | 1 | 1 | 6 | 19 |
| `SpawnModules.cs` | 1 |  | 2 |  |  | 3 |
| `SpecialAbilityStaging.cs` |  | 1 |  |  | 2 | 3 |
| `SpecialPowerModules.cs` |  |  |  |  | 2 | 2 |
| `SpecialsModules.cs` |  |  | 2 | 1 | 2 | 5 |
| `StructureModules.cs` |  | 4 | 1 |  | 1 | 6 |
| `SupplyModules.cs` |  |  | 1 |  |  | 1 |
| `ToppleAndFallModules.cs` | 2 | 1 |  | 3 | 5 | 11 |
| `TransportModules.cs` | 1 | 5 | 1 | 2 | 7 | 16 |
| `VehicleModules.cs` | 2 |  |  | 1 | 3 | 6 |
| `WorldEffectModules.cs` | 1 | 3 | 5 | 1 | 9 | 19 |
| **Summe** | 19 | 33 | 22 | 20 | 62 | 156 |

Hinweise: `SlaveModules.cs` – `UpdateRateFrames` und `RecentFireFrames` sind erledigt, die übrigen Fristen (`_framesToWait = 15` in zwei Zweigen, die Schweißzeiten, `MobMemberData.CatchUpCrisisBailTime` als Zahl von Prüfungen) nicht. `WorldEffectModules.cs` – `CheckpointUpdate.GateStep` (0,333 je Tick) ist ein additiver Rohwert im Rumpf. `RailroadModules.cs` – siehe §5; `FramesUnpulledLongEnoughToUnhitch = 2` ist Reihenfolge (W2).

**Scripting/ + Mission/ + Skirmish/ + Testing/** (ein Auftrag)

| Datei | A | B | C | D | E | H | Summe |
|---|---|---|---|---|---|---|---|
| `Mission/MissionSession.cs` |  | 1 |  |  | 1 |  | 2 |
| `Scripting/ScriptEngine.Actions.cs` |  | 3 | 2 | 2 | 1 |  | 8 |
| `Scripting/ScriptEngine.Conditions.cs` |  |  |  |  | 3 |  | 3 |
| `Scripting/ScriptEngine.ExtendedActions.cs` |  |  | 1 |  |  |  | 1 |
| `Scripting/ScriptEngine.Phase13.cs` |  | 1 | 3 |  | 3 |  | 7 |
| `Scripting/ScriptEngine.Powers.cs` |  | 1 |  |  |  |  | 1 |
| `Scripting/ScriptEngine.Sequential.cs` |  |  | 1 | 3 | 1 |  | 5 |
| `Scripting/ScriptEngine.TeamAbilities.cs` |  | 2 |  |  |  |  | 2 |
| `Scripting/ScriptEngine.TeamEvents.cs` |  |  | 1 |  | 2 |  | 3 |
| `Scripting/ScriptEngine.TeamQueues.cs` | 1 |  | 1 |  |  |  | 2 |
| `Scripting/ScriptEngine.cs` | 1 | 2 | 1 | 1 |  |  | 5 |
| `Scripting/ScriptProgram.cs` |  |  |  | 1 |  |  | 1 |
| `Skirmish/SkirmishAi.cs` |  | 19 | 5 |  | 3 |  | 27 |
| `Skirmish/SkirmishMatchSession.cs` |  | 1 | 2 |  |  |  | 3 |
| `Testing/MissionObjectiveDriver.cs` |  |  | 1 |  |  | 2 | 3 |
| `Testing/MissionScenario.cs` |  |  |  |  |  | 6 | 6 |
| `Testing/ObjectiveDriver.Commands.cs` |  |  | 1 |  |  | 3 | 4 |
| `Testing/ObjectiveDriver.Leaves.cs` |  |  |  |  | 1 | 31 | 32 |
| `Testing/ObjectiveDriver.cs` |  |  |  |  |  | 7 | 7 |
| `Testing/ReferenceMatchScenario.cs` |  |  | 4 |  |  | 6 | 10 |
| `Testing/SkirmishMatchScenario.cs` |  |  | 2 |  |  | 2 | 4 |
| `Testing/StressScenario.cs` |  |  | 9 |  |  | 8 | 17 |
| `Testing/SyntheticFixture.cs` |  | 3 |  |  |  |  | 3 |
| **Summe** | 2 | 33 | 34 | 7 | 15 | 65 | 156 |

Erledigt sind dort die Stellen mit der Konstante und die Skript-Bildzähler (§3). Offen und ausdrücklich für diesen Auftrag:

- **W6:** die Auswertung jedes Skripts in jedem Tick (`ScriptEngine.Update`; naheliegend: nur an Bildgrenzen des Originals, `Time.IsOriginalFrameBoundary`), das Dekrement der Timer je Tick (die Werte sind jetzt Ticks, Vergleiche einer Karte gegen Bildliterale meinen aber Bilder), `ScriptEngine.RetargetInterval = 30`, `ProcessTeamBuilds`.
- **Regelwerte der Sitzungen, die heute Ticks sind** (in Stufe 1 unverändert gelassen): `SkirmishMatchOptions.GraceTicks` (300), `SkirmishMatchOptions.SciencePointInterval` (2.700), `SkirmishMatchSession.VictoryCheckInterval` (15, mit `tick % …`), `ReferenceMatchOptions.GraceTicks` (300), `MinTicks`, `IncomeInterval` (300, mit `tick % …`), `StressOptions.DrainTicks`, `MissionOptions.PresentationLatencyTicks` (wird in den Missionszustand geschrieben: der Schlüssel bleibt, der Wert ist Ticks der Rate) und `ScriptEngineOptions.PresentationLatencyTicks`. `MissionOptions.CombatIntervalTicks` ist nur noch ein Schalter (W8) und wird nicht skaliert. Laufbudgets (`MaxTicks`, `StressOptions.Ticks`, `CheckpointInterval` des Replay-Kopfs) bleiben Ticks.
- Die Fahrer der Referenzläufe (`MoveCadence`, `spacing`, die 34 Abklingzeiten in `ObjectiveDriver.Leaves.cs`) und `SkirmishAi` (`ThinkInterval`, `FireInterval`, Fristen): als Bilder des Originals, Rhythmen nach Fall (c).

**Übrige** (klein; von dem Auftrag mitnehmen, der die Datei ohnehin anfasst)

| Datei | A | B | C | D | E | Summe | Stand |
|---|---|---|---|---|---|---|---|
| `Modules/ModuleFramework.cs` | 1 |  |  |  | 1 | 2 | offen |
| `Replay/ReplayRunner.cs` |  |  | 1 |  |  | 1 | Laufbudget, bleibt Ticks |
| `Templates/ObjectTemplate.cs` |  | 2 |  |  |  | 2 | erledigt |
| `Templates/SpecialPowerCatalog.cs` |  | 1 |  |  |  | 1 | erledigt (`DefaultReloadMilliseconds` ist Millisekunden) |
| `Templates/TurretInfo.cs` | 2 |  |  | 1 |  | 3 | erledigt |
| `Templates/WeaponCatalog.cs` |  | 1 |  | 1 | 3 | 5 | B und D erledigt bzw. bewusst unverändert; E ist W1 (`ScaledByRateOfFire`, `PreAttackDelayWith`, `Math.Max(SleepNone, delay)`) |
| `Time/SimulationTiming.cs` | 1 | 4 | 1 |  |  | 6 | erledigt |

## 7. Formate

- `WorldSnapshot`, `MissionSnapshot` und der Replay-Kopf tragen die Rate wie bisher (`tickRate`); bei 30 Hz sind die Bytes unverändert, deshalb **keine Versionserhöhung** (`WorldSnapshot` 9, `MissionSnapshot` 2, Replay 11).
- Wiederherstellen erzeugt die Welt **mit der gespeicherten Rate**. Abgelehnt wird (a) eine nicht unterstützte Rate (`SnapshotFormatException` bzw. `ReplayRejectedException`: „Unsupported tick rate 50 Hz“), (b) ein Katalog, der für eine andere Rate gebaut wurde („The snapshot was written at 60 Hz; the catalog was built for 30 Hz.“). Der Zustand enthält absolute Ticks und Werte je Tick; er ist nur mit seiner Rate lesbar.
- `ReplayRunner.Run(replay, world, …)` prüft jetzt, dass die Welt mit der Rate des Replays läuft (W8). `ReplayRunner.RateOf(header)` liefert die Rate eines Kopfs.
- **Nicht angefasst, für Phase 21 vorgemerkt:** `SaveGame` (`CnCNew.Game/Save`, Version 1) schreibt und prüft keine Rate und zeigt den rohen Tick in der Ladeliste (W8). Der eingebettete Missionszustand trägt die Rate und wird beim Laden gegen den Katalog geprüft; die Hülle selbst nicht.

## 8. Außerhalb der Simulation

Nur, was zum Übersetzen und für gleiches Verhalten nötig war: `GameData.SessionRate` (die eine benannte Rate der Spielschicht, 30 Hz) baut den Katalog und faltet die Rate in den Fingerabdruck; `LiveScenario` nimmt `data.Templates.Rate`. Die 14 Verwendungen der alten Konstante in `CnCNew.Game`, die eine in `CnCNew.Presentation` und drei in den Werkzeugen heißen jetzt `SimulationRate.OriginalFramesPerSecond` (derselbe Wert 30) – **sie sind damit nicht umgestellt**, sondern nur übersetzbar; welche davon Simulationsticks und welche Originalbilder auf Wandzeit sind, entscheidet Phase 21 (W7). Tests bauen ihre Kataloge und Welten mit `SimulationRate.Original`.

Werkzeuge: `SimTool` und `CampaignTool` nehmen `--tick-rate N` (30, 60, 120; Standard 30) bei jedem Befehl, der eine Welt erzeugt; Tickbudgets (`--ticks`, `--max-ticks`) bleiben Ticks der gewählten Rate; Spielminuten in der Ausgabe kommen aus der Rate. Die Manifestbefehle laufen immer mit der aufgezeichneten Rate (30 Hz) und lehnen eine andere ab. Neu: `SimTool catalog-cost <content>`.

## 9. Probelauf bei 60 und 120 Hz

Nur „läuft durch“ – das Verhalten bei diesen Raten ist ausdrücklich **nicht** bewertet (Phase 23), die Zahlen sind keine Referenzwerte. Stand der Messung: nach Stufe 1, also mit unskalierten nackten Tickzahlen und Rhythmen im Rumpf der Module, der Welt, der KI und der Fahrer.

Release, Retaildaten, 6. Oktober 2026, nicht ruhig. Jeder Lauf außer dem Referenzlauf bei 120 Hz: Aufzeichnung, zweiter frischer Lauf mit gleichen Tick-Hashes, Fortsetzung aus einem Snapshot bzw. Speichern/Laden identisch – also auch bei 60 und 120 Hz deterministisch und speicherfest.

| Lauf | Rate | Ergebnis | Endhash |
|---|---|---|---|
| `reference-match` | 60 | Sieger FactionGLA bei Tick 42.885 (11,9 Spielminuten) | `544DEB308E8B356C` |
| `reference-match` | 120 | kein Sieger in 216.000 Ticks (30 Spielminuten), Spitze 1.488 lebende Objekte (bei 60 Hz 311); gemessen am Stand `ac8d272`, vor dem Skalieren der Rohwerte; Aufzeichnung und erster Prüflauf (216 von 216 Prüfpunkten) fertig, der zweite frische Lauf und die Snapshot-Fortsetzung wurden vom Zeitlimit des Hintergrundlaufs abgebrochen und sind **nicht belegt** | `D4ACD5E06C64E83D` |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 60 | Sieger FactionGLA bei Tick 16.275 (4,5 Minuten) | `EB52FEE9C95787A5` |
| `ai-match` Alpine Assault, FactionAmerica/FactionGLA | 60 | Sieger FactionAmerica bei Tick 20.055 (5,6 Minuten) | `A3FC9630A2C559BC` |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 120 | Sieger FactionGLA bei Tick 31.200 (4,3 Minuten) | `44F39B9597E599D6` |
| `mission-run` `USA01`, 6.000 Ticks | 60 | läuft durch, 0 fehlende Aktionen/Bedingungen, Ausgang offen | `A6E879206005B081` |
| `mission-run` `MD_GLA05`, 6.000 Ticks | 60 | läuft durch, 0 fehlende Aktionen/Bedingungen, Ausgang offen | `44D37CE0882C3223` |
| `mission-run` `USA01`, 12.000 Ticks | 120 | läuft durch, 0 fehlende Aktionen/Bedingungen, Ausgang offen | `D437B25CAFCEE483` |
| `stress` 40.000 Ticks | 60 | läuft durch, 40 Prüfpunkte | `E9DF0A4C0B3D03FC` |
| `stress` 40.000 Ticks | 120 | läuft durch, 40 Prüfpunkte | `DAE348C840DC3765` |

Keine Ausnahme, kein Überlauf, keine Ablehnung wegen der Rate in einem der Läufe. Beobachtungen (Eingabe für Phase 22 und 23):

- **Die Ergebnisse sind nicht mit 30 Hz vergleichbar** und wurden nicht verglichen (der Referenzlauf endet bei 120 Hz nicht in seinem Tickbudget, das bei 120 Hz nur ein Viertel der Spielzeit ist). Zu erwarten sind Abweichungen durch die noch unskalierten Tickzahlen und Rhythmen (Fahrer der Referenzläufe, `SkirmishAi`, Sicht, Türme, Zielerfassung) und die nicht invariante Integration – Stufe 2 und Phase 23.
- **Rundung auf null beim Katalogbau:** keine. `SimTool catalog-cost` vergleicht den 30-Hz-Katalog Wert für Wert mit dem der anderen Rate (62.648 Festkommawerte, 83.166 ganze Zahlen): 2.410 Festkommawerte und 12.648 ganze Zahlen unterscheiden sich, **kein** Wert ist bei 30 Hz ungleich null und bei 60 oder 120 Hz null.
- **Grobe Werte (unter acht signifikante Bits, Rohwert unter 256 bei Q47.16):** bei 60 Hz 46 Werte in fünf Feldern (`LocomotorInfo.Braking` 23, `Acceleration` 12, `TurnRatePerTick` 6, `Lift` 4, `WeaponInfo.WeaponSpeed` 1), bei 120 Hz 321 Werte in sieben Feldern (`TurretInfo.PitchRate` 105, `LocomotorInfo.Braking` 102, `Acceleration` 88, `TurnRatePerTick` 17, `Lift` 4, `TurretInfo.TurnRate` 4, `WeaponInfo.WeaponSpeed` 1). Das ist der Befund E8 des Plans in Zahlen: Größen je Tick² verlieren bei jeder Verdoppelung der Rate zwei Bits.
- Nicht gezählt: Rundungen auf null **zur Laufzeit** (Werte je Tick, die Module aus Zustand bilden, etwa Heilmengen `MaxHealth · Anteil / Rate`); dafür gibt es noch keinen Zähler.

## 10. Fallstricke

- `SimulationTiming.cs`, `SyntheticFixture.cs`, `StressScenario.cs`, `SageRandom.cs` und `Tests.World.cs` haben CRLF; `git ls-files --eol <datei>` vor jedem Skriptlauf.
- Der Name `time` kollidiert in den Katalogbauern mit Mustervariablen (`is GameTime time`); der Parameter heißt dort `timing`.
- `SimulationWorld` hat jetzt eine Eigenschaft `Time`; in Dateien der Welt meint `Time.X` den Zeitdienst, nicht den Namensraum.
- Eine `record`-Standardbelegung mit einer Tick-Konstante (`long MoodCheckTicks = …`) ist ein stiller Tickwert; `AIUpdateData` hat sie verloren, weil nur `Parse` den Datensatz erzeugt.
- Nach jeder Quelländerung `dotnet build -c Release tools/CnCNew.SimTool` und aus einer Kopie des `bin`-Verzeichnisses laufen lassen; `--no-build` zeigt sonst den alten Stand.
