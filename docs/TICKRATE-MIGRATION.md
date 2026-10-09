# Tickrate als Wert der Welt – Umstellung (Phase 20)

Stand: 6. Oktober 2026, **Endstand der Phase 20** (`docs/HANDOFF-PHASE20.md`): Zeitvertrag, Kataloge je Rate und Formate (Stufe 1), die nackten Tickzahlen der Simulation in Bildern des Originals (Stufe 2, vier Aufträge nach Verzeichnis), die Zusammenführung mit `SourceRules` und der Probelauf bei 60 und 120 Hz. Dieses Dokument ersetzt die vier Ergebnisdateien der Stufe 2 (`TICKRATE-STAGE2-*.md`); ihr Inhalt steht hier. Grundlage ist das geprüfte Inventar `docs/TICKRATE-AUDIT.md`; Klassen (A bis H) und Widersprüche (W1 bis W10) sind dort definiert. „Bild“ heißt immer Logikbild des Originals (1/30 s), „Tick“ ein Schritt der Simulation.

**Die eiserne Regel:** Bei 30 Hz ändert sich nichts – kein Hash, kein Snapshot-Byte, kein Replay-Byte. Beweis ist `SimTool reference-manifest check <content> baselines/reference-manifest.json` (Stufe `quick` nach jedem Commit, `full` am Ende) und `SimTool input-manifest check <content> baselines/input-manifest.json`. Ein Zwischenstand, der das Manifest nicht besteht, ist ein Fehler im Umbau, nie ein neuer Referenzwert. Die Werte in `CLAUDE.md` sind keine Messlatte mehr (das Manifest führt für das Referenzmatch Sieger FactionGLA bei Tick 14.490, Endhash `23B4B25DF62765D8`).

**Was Phase 20 nicht ist:** richtiges Verhalten bei 60 Hz. Die Simulation läuft bei 60 und 120 Hz durch und ist dort deterministisch (§11); was sich dort anders verhält, steht in der Liste für Phase 23 (§5). Was Verbraucher außerhalb der Simulation ändern müssen, steht in der Liste für Phase 21 (§6).

## 1. Konvention (wie sie angewendet wurde)

Jede Stelle, die Zeit meint, geht über den Zeitdienst – zur Laufzeit `world.Time` (auf `IWorldServices`, in der Skript-VM `_world.Time`, in der Welt selbst `Time`), beim Katalogbau `configuration.Time` (Modul-`Parse`) bzw. der Parameter `timing` der Katalogbauer. Kein Modul rechnet selbst mit einer Rate. Konstanten bleiben `const`, aber in **Bildern des Originals** (Endung `Frames`) oder in Sekunden (Endung `Seconds`), nie in Ticks; die Umrechnung steht an der Verwendung. Der Quellverweis im Kommentar bleibt.

| Fall | So schreiben | Beispiel |
|---|---|---|
| **(a) Originalkonstante in Bildern** | `private const long XFrames = N;` und an der Verwendung `world.Time.OriginalFrames(XFrames)` | `SpawnBehavior` (`SpawnModules.cs`): `UpdateRateFrames = SimulationRate.OriginalFramesPerSecond / 2`; `_workTick = world.Tick + world.Time.OriginalFrames(UpdateRateFrames)`. `SlavedUpdate`: `PackFrames = 15`, `ArmFrames = 5`. |
| **(b) Zeitwert, der als Tickzahl hingeschrieben war** | wie (a): der bisherige Wert ist die Zahl der Bilder des Originals (30 Hz bleibt exakt). Steht der Wert für ganze Sekunden, `XSeconds` und `world.Time.WholeSecondsToTicks(XSeconds)` | `SimulationWorld.Acquisition.cs`: `OrderedAttackGraceFrames = 45`. `ScriptEngine.Phase13.cs`: `SupplyScanSeconds = 10`. `SkirmishAi`: 16 Fristen (`RetryFrames`, `MemoryFrames` …). |
| **(c) Ablaufrhythmus mit Modulo** | Periode **und** Phase in Bildern, beide skaliert: `tick % world.Time.OriginalFrames(PeriodFrames) == world.Time.OriginalFrames(PhaseFrames)`. Bei 30 Hz ist das der alte Ausdruck; bei 60 Hz fällt der Rhythmus auf dieselben Spielzeitpunkte. Eine aus einem Hash oder einer Kennung gezogene Phase wird in Bildern gezogen und dann skaliert | `SimulationWorld.Step`: `tick % VisionInterval == 0`. `SkirmishAi`: `(_tick + _phase) % _thinkInterval`, `_phase = Time.OriginalFrames(Player % FireIntervalFrames)`. `TensileFormationUpdate`: `_life % OriginalFrames(30) == OriginalFrames(29)`. |
| **(d) Ganzzahlige Teilung der Rate** | Teilung der **Originalrate** als Konstante in Bildern, dann skalieren: `SimulationRate.OriginalFramesPerSecond / 4` ist 7 Bilder bei jeder Rate (14 Ticks bei 60 Hz, nicht `60 / 4 = 15`) | `SlavedUpdate` (`SlaveModules.cs`): `UpdateRateFrames = SimulationRate.OriginalFramesPerSecond / 4`. |
| **(e) Literal, das Reihenfolge meint (W2)** | **nicht skalieren.** Mit `TickOrder.Next` schreiben (`Time/TickOrder.cs`): `now + TickOrder.Next`, `frame - TickOrder.Next`, `2 * TickOrder.Next`. Vergleiche „im selben Tick“ (`LastFireTick == world.Tick`) brauchen keine Markierung. `SimulationTiming.SleepNone` (kleinster Schlaf eines Updates, ein Tick) ist dieselbe Art und bleibt | `JetAIUpdate.cs`: `_transferTick = now + 2 * TickOrder.Next`. `RailroadBehavior.FramesUnpulledLongEnoughToUnhitch = 2 * TickOrder.Next`. `SimulationWorld.Step`: `_tick = tick + TickOrder.Next`. |
| **(f) Bildzähler der Kartenskripte** | Der Wert der Karte ist Bilder des Originals; er wird skaliert, sobald er ein Tick-Countdown wird. Zufall wird in Bildern gezogen, **dann** skaliert. Sekundenwerte: aufrunden auf Bilder des Originals, dann skalieren | `ScriptEngine.Actions.cs` `SET_TIMER`/`SET_RANDOM_TIMER`: `timer.Value = _world.Time.OriginalFrames(frames)`; `SecondsToFrames`. `ScriptEngine.Sequential.cs` `SetSequenceTimer` (`*_FOR_FRAMECOUNT`). |
| **(g) Rundung, Untergrenze, Zähler, Zufall zur Laufzeit (W1)** | in Bildern rechnen und erst das Ergebnis skalieren – über den einen Rasterhelfer `FrameGrid` (§2): `AtLeastOneFrame` (Untergrenze), `CountdownReload` (Zähler mit Periode N + 1), `DrawFrames` (Zufall über einen Bereich von Dauern), `FloorToFrame`/`CeilingToFrame`; oder von Hand `Time.OriginalFrames(… Time.ToOriginalFrames(ticks) …)`, wo zwischen Hin- und Rückrechnung gerechnet wird | `WeaponInfo.ScaledByRateOfFire`: `time.OriginalFrames(max(MinimumShotDelayFrames, floor(time.ToOriginalFrames(ticks) / Multiplikator)))`. `AutoHealBehavior.Update`: `return world.Time.AtLeastOneFrame(data.HealingDelayTicks)`. `PointDefenseLaserUpdate`: `_nextScan = world.Time.CountdownReload(data.ScanTicks)`. `SlowDeathBehavior`: `world.Time.DrawFrames(world.LogicRandom, 0, data.SinkDelayVarianceTicks)`. |

Weitere Regeln:

- **Werte je Sekunde zur Laufzeit** (Anteil der aktuellen Lebenspunkte u. ä.): `world.Time.PerSecondToPerTick(wert)` – eine `Fixed`-Division durch die Rate, derselbe Ausdruck wie das frühere `/ Fixed.FromInt(30)`; die Reihenfolge der Operanden im Argument bleibt (die Rundung von `Fixed` hängt daran). Ein Betrag, der über eine Dauer verteilt wird (`MaxHealth / TimeForFullHealTicks`), geht über `world.Time.SharePerTick(gesamt, ticks)` – dieselbe Division, aber zählbar (§11.3). `world.Time.TicksPerSecondFixed` bleibt für Rechnungen, die keinen Betrag je Tick bilden (zwei Stellen in `FlightModules.cs`: ein Vergleich und eine Dauer in Sekunden).
- **Ersatzwerte von `ModuleConfigReader.Ticks(configuration, name, fallbackOriginalFrames)`** sind Bilder des Originals und werden vom Leser skaliert. Kein Aufrufer rechnet den Ersatzwert vor.
- **Additive Rohwerte je Bild** (Geschwindigkeit je Bild, Zuwachs je Bild) beim Katalogbau über `configuration.Time.PerOriginalFrameToPerTick(…)` bzw. `PerOriginalFrameSquaredToPerTickSquared(…)`; Literale im Rumpf als Konstante mit Endung `PerFrame`/`PerFrameSquared` und zur Laufzeit über `world.Time.…`. **Multiplikative Faktoren je Bild bleiben unverändert** und stehen in §5.1.
- **Konstanten der Welt** werden einmal im Konstruktor von `SimulationWorld` in Ticks aufgelöst (`private readonly long _…Ticks`, `VisionInterval`); kein Schritt der Welt rechnet eine Konstante um. Diese Felder sind kein Zustand (nicht im Snapshot, nicht im Hash) und keine Katalogfelder.
- **Regelwerte der Optionsdatensätze sind Bilder des Originals, Laufbudgets bleiben Ticks** (§8.1). Die Datensätze behalten Form und Feldnamen; der Name `…Ticks` eines Regelwerts ist historisch, die Einheit steht im Kommentar des Datensatzes.
- **Zustandsfelder** behalten ihre Namen (Snapshot-Schlüssel und der Inhalts-Hash des Eingabemanifests hängen an Feldnamen). Neue Instanzfelder an Katalogtypen (`ObjectTemplate`, `WeaponInfo`, jede `…Data` eines Moduls) ändern das Eingabemanifest – Konstanten (`const`, `static`) nicht.
- **Kein stiller Standard:** keine Simulationsfunktion bekommt einen Standardparameter für die Rate. Wer eine Welt oder einen Katalog erzeugt, nennt die Rate. (Die Parameter `file`/`line` einiger Funktionen des Zeitdiensts sind Aufruferinformation für den Messfühler, §2, und werden nie von Hand übergeben.)

Die Konvention ist als Regelwerk in `SourceRules` gefasst (§10); ein nacktes Tickliteral in `src/CnCNew.Simulation` lässt den Test fehlschlagen.

## 2. Der Zeitvertrag

`src/CnCNew.Simulation/Time/`:

- **`SimulationRate`** – unveränderlicher Wert, genau drei Instanzen (`Hz30`, `Hz60`, `Hz120`; `Original` = `Hz30`). `FromTicksPerSecond(n)` wirft `UnsupportedSimulationRateException` für alles andere. `OriginalFramesPerSecond = 30` ist die einzige benannte 30 der Simulation und wird nur in Umrechnungen und in Konstanten „in Bildern“ benutzt. `Combine(fingerprint)` faltet die Rate in den Definitions-Fingerabdruck (bei 30 Hz unverändert, §4).
- **`SimulationTime`** – der Zeitdienst einer Rate (`rate.Time`, je Rate eine Instanz; keine Allokation, keine Umrechnung im heißen Pfad außer einer Multiplikation bzw. einer `Fixed`-Division).
- **`FrameGrid`** – der eine Rasterhelfer (Erweiterungsmethoden auf `SimulationTime`) für Rechnungen zur Laufzeit, die auf dem 30-Hz-Raster bleiben müssen (W1).
- **`SimulationTimeProbe`** – Messfühler für den Probelauf (unten).
- **`SimulationTiming`** – nur noch ratenunabhängig: `SleepNone`, `SleepForever` (Wächterwert, nie skaliert), `MillisecondsPerSecond`, `DegreesToRadians`, `RatioToFixed`. Die Konstante `LogicFramesPerSecond` gibt es in `src/CnCNew.Simulation` nicht mehr.
- **`TickOrder`** – `Next = 1` für Literale, die Reihenfolge meinen.

| Funktion von `SimulationTime` | Rechnung | Rundung | Warum 30 Hz unverändert ist |
|---|---|---|---|
| `OriginalFrames(n)` | `n · (Rate / 30)` | exakt (Faktor 1, 2, 4); Überlauf wirft | Faktor 1 |
| `ToOriginalFrames(ticks)` | `floor(ticks · 30 / Rate)` | abgerundet | Identität |
| `TicksPerOriginalFrame` | `Rate / 30` | – | 1 |
| `IsOriginalFrameBoundary(tick)` | `tick % (Rate / 30) == 0` | – | immer wahr |
| `DurationMillisecondsToTicks(long)` | `((ms · 30 + 999) / 1000) · (Rate / 30)` | auf ganze Bilder des Originals **auf**gerundet (ganzzahlig), dann exakt skaliert | derselbe Ganzzahlausdruck, mal 1 |
| `DurationMillisecondsToTicks(decimal)` | `Ceiling(ms · 30 / 1000) · (Rate / 30)` | wie oben, in `decimal` | derselbe Dezimalausdruck, mal 1 |
| `DurationMillisecondsToFixedTicks(decimal)` | `ms · Rate / 1000` | einmal auf `Fixed` (nächster Wert, Halbwerte weg von null); **nicht** auf dem Raster, weil das Original (`parseDurationReal`) hier nicht rundet | `ms · 30 / 1000` wie zuvor |
| `SecondsToTicks(decimal)` | `Ceiling(s · 30) · (Rate / 30)` | auf Bilder des Originals aufgerundet | derselbe Ausdruck, mal 1 |
| `WholeSecondsToTicks(long)` | `s · Rate` | exakt | `s · 30` |
| `PerSecondToPerTick(decimal)` | `v / Rate` | einmal auf `Fixed` | `v / 30` |
| `PerSecondToPerTick(Fixed)`, `TicksPerSecondFixed` | `v / Fixed(Rate)` | eine `Fixed`-Division | früher `v / Fixed.FromInt(30)` |
| `SharePerTick(Fixed gesamt, Fixed ticks)` | `gesamt / ticks` | eine `Fixed`-Division | die frühere Division, unverändert bei jeder Rate |
| `PerSecondSquaredToPerTickSquared(decimal)` | `a / (Rate · Rate)` | einmal auf `Fixed` | `a / 900` |
| `DegreesPerSecondToRadiansPerTick(decimal)` | `deg · π / 180 / Rate` (in dieser Reihenfolge) | einmal auf `Fixed` | letzter Divisor 30 wie zuvor |
| `PerOriginalFrameToPerTick(decimal)` | `v / (Rate / 30)` | eine Dezimaldivision durch 1, 2 oder 4, dann einmal auf `Fixed` | Division durch 1 ist die Identität: `Fixed.FromDecimal(v)` wie beim rohen Lesen |
| `PerOriginalFrameToPerTick(Fixed)` | `v / Fixed(Rate / 30)` | eine `Fixed`-Division | bei 30 Hz wird der Wert **ohne Rechnung** zurückgegeben |
| `PerOriginalFrameSquaredToPerTickSquared(decimal / Fixed)` | `v / (Rate / 30)²` | Division durch 1, 4 oder 16 | wie oben |
| `ToWholeSeconds`, `ToWholeMinutes`, `ToMilliseconds` | `ticks / Rate`, `ticks / (60 · Rate)`, `ticks · 1000 / Rate` | ganzzahlig, zur Null | – |
| `ToSecondsRational(ticks)`, `ToSeconds(ticks)` | `(ticks, Rate)` bzw. `Fixed(ticks) / Fixed(Rate)` | exakt bzw. eine `Fixed`-Division | – |

**`FrameGrid` – ein Name je Begriff.** Zwei Aufträge der Stufe 2 hatten parallel denselben Helfer geschrieben (`Modules/BuiltIn/FrameGrid.cs`, Module A–M; `Modules/BuiltIn/TickGrid.cs`, Module N–Z). Beide Dateien sind gelöscht; es gibt genau `Time/FrameGrid.cs`.

| Funktion | Rechnung | 30 Hz | ersetzt |
|---|---|---|---|
| `time.FloorToFrame(ticks)` | `OriginalFrames(ToOriginalFrames(ticks))` – auf ganze Bilder abgerundet, in Ticks | `ticks` | `FrameGrid.Floor` |
| `time.CeilingToFrame(ticks)` | auf ganze Bilder aufgerundet, in Ticks | `ticks` | `FrameGrid.Ceiling` |
| `time.AtLeastOneFrame(ticks)` | `Math.Max(ticks, TicksPerOriginalFrame)` – die Untergrenze „ein Bild“ (`if (delay < 1) delay = 1` im Original und jede eigene Untergrenze) | `Math.Max(ticks, 1)` | `FrameGrid.AtLeastOneFrame` und 20-mal `Math.Max(x, world.Time.TicksPerOriginalFrame)` der Module N–Z |
| `time.DrawFrames(random, minTicks, maxTicks)` | ein Zufallszug über `ToOriginalFrames(min)…ToOriginalFrames(max)`, Ergebnis skaliert (ein Zug, derselbe Zufallsstrom bei jeder Rate, das Ergebnis bleibt auf dem Raster) | `random.Range(min, max)` | `FrameGrid.DrawFrames` und `TickGrid.RandomTicks` (17 Aufrufe) |
| `time.CountdownReload(ticks)` | `OriginalFrames(ToOriginalFrames(ticks) + 1) − 1` – Nachladewert eines Zählers der Form „> 0: −1, sonst handeln und auf N setzen“ (Periode N + 1 Bilder) | `ticks` | `FrameGrid.CountdownReload` und `TickGrid.CountdownActingAfterZero` (10 Aufrufe) |

**Beweis der Gleichheit** (`Tests.TickRate.Grid.cs`, `TickRateGridMergedHelpersAreTheSameFunctions`, je bei 30, 60 und 120 Hz, die ersetzten Ausdrücke als Orakel): `CountdownReload(t)` gegen `t + (TicksPerOriginalFrame − 1)` (die Form von `TickGrid.CountdownActingAfterZero`) für jede Dauer auf dem Raster von 0 bis 20.000 Bildern, mit der Periode `(N + 1) · Ticks je Bild`; `AtLeastOneFrame(t)` gegen beide früheren Schreibweisen für jeden Tickwert von −50 bis 50.000; `DrawFrames` gegen den Ausdruck von `TickGrid.RandomTicks` über 1.681 Bereiche mit zwei gleich geimpften Strömen – gleiches Ergebnis **und** gleicher Stromzustand danach; `FloorToFrame`/`CeilingToFrame` als Raster-Eigenschaft; bei 30 Hz ist jede Funktion die Identität bzw. `max(t, 1)`. Die beiden Zählerformen sind **nur auf dem Raster** gleich (abseits davon schneidet `CountdownReload` den Rest ab, `CountdownActingAfterZero` behielt ihn – der Test hält auch das fest). Jede Aufrufstelle übergibt eine Dauer auf dem Raster (Katalogdauer über `ModuleConfigReader.Ticks`, `OriginalFrames(…)`, das Ergebnis von `DrawFrames`, `WeaponInfo.DelayBetweenShotsTicks`); der Messfühler zählt jeden Aufruf abseits des Rasters (Zähler `OffGrid`), in den Probeläufen 0 von über 20 Millionen Aufrufen (§11.3).

**`SimulationTimeProbe` – Messfühler, kein Zustand.** `SimulationTime.Probe` ist normalerweise `null`; jede Umrechnung prüft dann nur dieses eine Feld. Ein Werkzeug hängt einen Messfühler an den Zeitdienst einer Rate (`--time-probe`, `rate-audit`), lässt laufen und liest die Zähler. Nichts davon wird von der Simulation gelesen, in einen Snapshot geschrieben oder gehasht (der Test `TickRateProbeOnlyObserves` prüft, dass mit Messfühler jede Funktion dasselbe liefert; die 30-Hz-Probeläufe mit Messfühler tragen die Endhashes des Referenzmanifests). Der Zeitdienst einer Rate gehört allen Welten dieser Rate im Prozess; ein Messfühler ist für einen Lauf zur Zeit gedacht. Gezählt wird je Stelle im Quelltext (Datei und Zeile über `CallerFilePath`/`CallerLineNumber`):

| Zähler | Zählt | Wo |
|---|---|---|
| `RunTimeRoundedToZero` | ein Betrag je Tick, der zur Laufzeit aus Zustand entsteht, ist null, obwohl die Eingabe es nicht war | `PerSecondToPerTick(Fixed)`, `SharePerTick`, `PerOriginalFrameToPerTick(Fixed)`, `PerOriginalFrameSquaredToPerTickSquared(Fixed)` |
| `MinimumOneFrame` | die Untergrenze „ein Bild“ hat gegriffen | `AtLeastOneFrame` |
| `OffGrid` | eine Dauer, die auf dem Raster liegen sollte, liegt nicht darauf | `ToOriginalFrames`, `CountdownReload`, `DrawFrames`, `AtLeastOneFrame` |
| `RoundedToFrame` | `FloorToFrame`/`CeilingToFrame` hat den Wert geändert (zwischen zwei Bildgrenzen erwartet) | `FloorToFrame`, `CeilingToFrame` |
| Quantisierung | je Dezimalumrechnung (Katalogbau): Aufrufe, Werte ≠ 0, auf null gerundet, relativer Fehler über 1 % und über 10 % gegen den exakten Dezimalwert, schlechtester Fall | `PerSecondToPerTick(decimal)`, `PerSecondSquaredToPerTickSquared`, `DegreesPerSecondToRadiansPerTick`, `DurationMillisecondsToFixedTicks`, `PerOriginalFrame…(decimal)` |

Der Test `Tests.TickRate.cs` hält die früheren Formeln als Orakel (`FormerTiming`) und vergleicht bei 30 Hz über 120.001 ganze Millisekundenwerte, rund 8.000 Dezimalwerte je Funktion und beide Vorzeichen; bei 60 und 120 Hz prüft er das Raster (Dauer = 30-Hz-Wert mal 2 bzw. 4) und die skalierten Werte.

## 3. Umgestellte Stellen

Drei Abschnitte der Phase, jeder mit seiner Zählweise: Stufe 1 zählt **Vorkommen** im alten Quelltext (Stand `a1ab03b`), Stufe 2 und der Abschluss zählen **Stellen** (ein Symbol bzw. ein Literal; Zählregel in `docs/TICKRATE-AUDIT.md` §1.2). Die Zahlen der Stufen stammen aus deren Ergebnisberichten; die Einzelstellen der Stufe 2 mit Herkunft im Original stehen in Anhang A.

| Abschnitt | Was | Zahl |
|---|---|---|
| Stufe 1 | Verwendungen der Konstante `LogicFramesPerSecond` (32 Dateien) | 63 |
| Stufe 1 | direkte Umrechnungsaufrufe `SimulationTiming.…(…)` (12 Dateien) | 41 |
| Stufe 1 | Aufrufe `ModuleConfigReader.Ticks` (33 Dateien; darunter 15 mit Ersatzwert ≠ 0, jetzt Bilder des Originals) | 133 |
| Stufe 1 | additive Rohwerte je Bild, beim Katalogbau skaliert | 9 |
| Stufe 1 | Bildzähler der Kartenskripte | 4 |
| Stufe 1 | Reihenfolge-Literale als Muster markiert | 3 |
| Stufe 1 | Formatstellen (Rate schreiben, prüfen, Welt mit der Rate erzeugen) | 9 |
| **Stufe 1** | **Summe der Vorkommen** | **262** |
| Stufe 2 | Welt, Kataloge, Replay | 27 |
| Stufe 2 | Module A–M | 46 |
| Stufe 2 | Module N–Z | 69 |
| Stufe 2 | Skripte, Mission, Skirmish, Szenarien | 46 |
| **Stufe 2** | **Summe der Stellen** | **188** |
| Abschluss | Simulation: von `SourceRules` gefunden bzw. Reste der Zusammenführung (§3.3) | 4 |
| Abschluss | Werkzeuge (§9) | 7 |
| **Phase 20** | **Stellen der Stufe 2 und des Abschlusses in der Simulation** | **192** |

### 3.1 Stufe 1 je Art und Datei

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

| Datei (`src/CnCNew.Simulation/…`) | Konstante | Umrechnungsaufrufe | `Ticks`-Ersatzwert ≠ 0 | rohe Bildwerte |
|---|---|---|---|---|
| `Mission/MissionSnapshot.cs` | 2 |  |  |  |
| `Modules/BuiltIn/AirModules.cs` | 2 |  |  |  |
| `Modules/BuiltIn/AirWeaponModules.cs` |  | 2 | 4 | 1 |
| `Modules/BuiltIn/BridgeModules.cs` | 2 | 1 |  |  |
| `Modules/BuiltIn/ContainModules.cs` | 1 |  |  |  |
| `Modules/BuiltIn/DockMachine.cs` | 1 |  |  |  |
| `Modules/BuiltIn/FieldEffectModules.cs` | 1 |  | 1 |  |
| `Modules/BuiltIn/FlightModules.cs` | 5 |  |  |  |
| `Modules/BuiltIn/JetAIUpdate.cs` | 3 |  |  |  |
| `Modules/BuiltIn/MatchModules.cs` | 1 |  |  |  |
| `Modules/BuiltIn/MineModules.cs` | 2 |  | 1 |  |
| `Modules/BuiltIn/ModuleConfigReader.cs` |  | 1 |  |  |
| `Modules/BuiltIn/Phase12Modules.cs` | 1 |  | 1 |  |
| `Modules/BuiltIn/Phase5BlockerModules.cs` |  |  | 1 |  |
| `Modules/BuiltIn/Phase5Modules.cs` | 2 | 4 | 1 | 1 |
| `Modules/BuiltIn/ProjectileModules.cs` | 3 |  | 2 |  |
| `Modules/BuiltIn/RailroadModules.cs` |  |  | 1 | 3 |
| `Modules/BuiltIn/RepairModules.cs` |  | 1 |  |  |
| `Modules/BuiltIn/RetailMatchModules.cs` | 2 |  |  |  |
| `Modules/BuiltIn/SlaveModules.cs` | 3 |  |  |  |
| `Modules/BuiltIn/SpawnModules.cs` | 1 |  |  |  |
| `Modules/BuiltIn/SpecialsModules.cs` |  |  |  | 1 |
| `Modules/BuiltIn/StructureModules.cs` |  |  | 1 |  |
| `Modules/BuiltIn/TransportModules.cs` | 2 | 2 |  |  |
| `Modules/BuiltIn/VehicleModules.cs` |  |  |  | 1 |
| `Modules/BuiltIn/WorldEffectModules.cs` | 2 |  | 2 |  |
| `Replay/ReplayRunner.cs` | 2 |  |  |  |
| `Scripting/ScriptEngine.Actions.cs` | 1 |  |  |  |
| `Scripting/ScriptEngine.Phase13.cs` | 2 |  |  |  |
| `Scripting/ScriptEngine.Powers.cs` | 1 |  |  |  |
| `Scripting/ScriptEngine.TeamAbilities.cs` | 1 |  |  |  |
| `Scripting/ScriptEngine.TeamQueues.cs` | 1 |  |  |  |
| `Scripting/ScriptEngine.cs` | 2 |  |  |  |
| `Skirmish/SkirmishMatchSession.cs` | 1 |  |  |  |
| `Snapshots/WorldSnapshot.cs` | 2 |  |  |  |
| `Templates/ObjectCreationListCatalog.cs` |  | 2 |  |  |
| `Templates/ObjectTemplate.cs` |  | 16 |  |  |
| `Templates/SpecialPowerCatalog.cs` |  | 1 |  |  |
| `Templates/TurretInfo.cs` | 1 | 5 |  | 2 |
| `Templates/UpgradeCatalog.cs` |  | 1 |  |  |
| `Templates/WeaponCatalog.cs` |  | 5 |  |  |
| `Testing/ReferenceMatchScenario.cs` | 1 |  |  |  |
| `Testing/StressScenario.cs` | 1 |  |  |  |
| `Time/SimulationTiming.cs` | 9 |  |  |  |
| `World/SimulationWorld.FiringTracker.cs` | 2 |  |  |  |
| **Summe** | 63 | 41 | 15 | 9 |

Die neun skalierten Rohwerte: `TurretInfo.DefaultRate` (0,01 rad je Bild, für fehlende `TurretTurnRate`/`TurretPitchRate`), `SpectreData.StrafingIncrement`, `StealthData.MoveThresholdSpeed` (roh gelesen und mit der Geschwindigkeit je Tick verglichen; das Original liest hier eine Geschwindigkeit je Sekunde – Nebenbefund des Inventars, nicht korrigiert), `RailroadData.RunningGarrisonSpeedMax`/`KillSpeedMin`/`SpeedMax`, `GrantStealthData.RadiusGrowRate`, `BattleBusData.ThrowForce` (wird als Geschwindigkeit je Tick eingesetzt). Bewusst **nicht** skaliert: `WeaponInfo.InstantSpeed` (999999 je Bild heißt „sofort“; ein Wächterwert, kein Datenwert), der Ersatzwert `TwoPi · 100` für `NeutronMissileData.MaxTurnRate` (jeder Wert ab π rastet sofort ein).

### 3.2 Stufe 2 je Verzeichnis, Datei und Klasse

A, B, C, D sind die Klassen des Inventars; **W1** (Rundung, Untergrenze, Zähler N + 1 oder Zufall zur Laufzeit) und **W2** (Reihenfolge, `TickOrder.Next`) haben Vorrang vor der Klasse, unter der das Inventar die Stelle führt; **H** sind Zeitwerte der Fahrer und Sonden in `Testing/`.

| Datei (`src/CnCNew.Simulation/…`) | A | B | C | D | W1 | W2 | H | Summe |
|---|---|---|---|---|---|---|---|---|
| `World/SimulationWorld.Acquisition.cs` |  | 2 | 1 |  | 1 | 3 |  | 7 |
| `World/SimulationWorld.Turrets.cs` | 1 | 3 |  |  | 1 |  |  | 5 |
| `World/SimulationWorld.Vision.cs` (mit `Step`/`StepObserved`) |  |  | 1 |  |  |  |  | 1 |
| `World/SimulationWorld.cs` |  |  |  |  |  | 2 |  | 2 |
| `World/SimulationWorld.Construction.cs` | 1 |  |  |  | 1 | 1 |  | 3 |
| `World/SimulationWorld.CreationLists.cs` | 1 |  |  |  | 1 |  |  | 2 |
| `World/SimulationWorld.Projectiles.cs` |  |  |  |  | 1 |  |  | 1 |
| `Templates/WeaponCatalog.cs` (mit den Aufrufern in `WeaponSlots.cs`, `FiringTracker.cs`) |  |  |  |  | 3 |  |  | 3 |
| `Replay/ReplayRunner.cs` |  |  |  |  |  | 2 |  | 2 |
| **Welt, Kataloge, Replay** | **3** | **5** | **2** | **0** | **9** | **8** | **0** | **27** |
| `Modules/BuiltIn/AirModules.cs` |  |  |  |  |  | 1 |  | 1 |
| `Modules/BuiltIn/AirWeaponModules.cs` |  | 1 | 1 |  | 5 | 1 |  | 8 |
| `Modules/BuiltIn/BridgeModules.cs` |  |  |  | 2 |  |  |  | 2 |
| `Modules/BuiltIn/CleanupAndSpyModules.cs` |  |  | 1 |  | 3 |  |  | 4 |
| `Modules/BuiltIn/CrateModules.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/DieAndUpdateModules.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/DockModules.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/FerryModules.cs` |  |  |  |  | 2 |  |  | 2 |
| `Modules/BuiltIn/FieldEffectModules.cs` | 3 |  |  |  |  |  |  | 3 |
| `Modules/BuiltIn/FlightModules.cs` |  | 1 |  |  | 9 | 1 |  | 11 |
| `Modules/BuiltIn/HackerAndAssaultModules.cs` |  |  |  |  | 2 |  |  | 2 |
| `Modules/BuiltIn/JetAIUpdate.cs` | 1 |  |  | 3 | 2 |  |  | 6 |
| `Modules/BuiltIn/LastBlockerModules.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/MatchModules.cs` | 1 | 1 |  |  |  |  |  | 2 |
| `Modules/BuiltIn/MineModules.cs` |  |  |  |  | 1 |  |  | 1 |
| **Module A–M** | **5** | **3** | **2** | **5** | **28** | **3** | **0** | **46** |
| `Modules/BuiltIn/Phase12Modules.cs` |  |  |  |  | 4 |  |  | 4 |
| `Modules/BuiltIn/Phase5BlockerModules.cs` |  |  |  |  | 9 | 1 |  | 10 |
| `Modules/BuiltIn/Phase5Modules.cs` |  | 1 | 1 |  | 5 | 1 |  | 8 |
| `Modules/BuiltIn/ProjectileModules.cs` |  |  |  |  | 7 |  |  | 7 |
| `Modules/BuiltIn/RailroadModules.cs` |  |  |  |  |  | 1 |  | 1 |
| `Modules/BuiltIn/RepairModules.cs` |  | 1 |  |  | 1 |  |  | 2 |
| `Modules/BuiltIn/SlaveModules.cs` | 5 |  |  |  | 3 |  |  | 8 |
| `Modules/BuiltIn/SpecialAbilityStaging.cs` |  |  |  |  | 1 |  |  | 1 |
| `Modules/BuiltIn/SpecialPowerModules.cs` |  |  |  |  |  | 1 |  | 1 |
| `Modules/BuiltIn/StructureModules.cs` |  |  |  |  | 4 |  |  | 4 |
| `Modules/BuiltIn/ToppleAndFallModules.cs` | 2 |  |  |  | 1 |  |  | 3 |
| `Modules/BuiltIn/TransportModules.cs` |  |  |  |  | 3 |  |  | 3 |
| `Modules/BuiltIn/VehicleModules.cs` | 2 |  |  |  | 3 |  |  | 5 |
| `Modules/BuiltIn/WorldEffectModules.cs` |  |  |  | 1 | 10 | 1 |  | 12 |
| **Module N–Z** | **9** | **2** | **1** | **1** | **51** | **5** | **0** | **69** |
| `Skirmish/SkirmishAi.cs` |  | 19 | 3 |  |  |  |  | 22 |
| `Skirmish/SkirmishMatchSession.cs` |  | 1 | 2 |  |  | 1 |  | 4 |
| `Testing/ReferenceMatchScenario.cs` |  |  | 3 |  |  | 3 | 2 | 8 |
| `Testing/StressScenario.cs` |  |  |  |  |  |  | 1 | 1 |
| `Scripting/ScriptEngine.cs` | 1 |  |  |  |  |  |  | 1 |
| `Scripting/ScriptEngine.Actions.cs` |  | 1 | 1 |  |  | 1 |  | 3 |
| `Scripting/ScriptEngine.Phase13.cs` |  |  | 1 |  |  |  |  | 1 |
| `Testing/MissionScenario.cs` |  |  |  |  |  |  | 2 | 2 |
| `Testing/MissionObjectiveDriver.cs` |  |  | 1 |  |  |  | 1 | 2 |
| `Testing/ObjectiveDriver.cs` |  |  |  |  |  |  | 1 | 1 |
| `Testing/ObjectiveDriver.Commands.cs` |  |  |  |  |  |  | 1 | 1 |
| **Skripte, Mission, Skirmish, Szenarien** | **1** | **21** | **11** | **0** | **0** | **5** | **8** | **46** |
| **Stufe 2 gesamt** | **18** | **31** | **16** | **6** | **88** | **21** | **8** | **188** |

Der Bericht der Module N–Z führt seine Stellen nach der Klasse des Inventars mit W1/W2 als „davon“ (A 15, B 13, C 12, D 1, E 28; davon W1 51, W2 5); die Tabelle oben ordnet sie wie die drei anderen Aufträge mit Vorrang für W1/W2 (nachgezählt aus Anhang A.3: 13 Stellen sind weder W1 noch W2). Mehrfachklassen (A + W1, C / W1) stehen unter W1. Die 34 Abklingzeiten der Blätter des Zielfahrers (`ObjectiveDriver.Leaves.cs` 31, `ObjectiveDriver.cs` 2, `ObjectiveDriver.Commands.cs` 1) zählen als eine Stelle: skaliert wird an der einen Verwendung in `Round`.

### 3.3 Abschluss der Phase (dieser Stand)

| Datei | Symbol | Kl. | War | Ist |
|---|---|---|---|---|
| `Modules/BuiltIn/CombatModules.cs` | `FireWeaponUpdate.Update` (`_readyTick`) | W1 | `weapon.ClipReloadWith(bonuses, conditions)` / `DelayBetweenShotsWith(bonuses, conditions)` – in Ticks abgeschnitten, mindestens ein Tick | die Überladungen mit `world.Time` (in Bildern abgeschnitten, mindestens ein Bild) |
| `Modules/BuiltIn/JetAIUpdate.cs` | `BeginReload` (`time`) | W1 | `weapon.ClipReloadWith(global, conditions)` | `weapon.ClipReloadWith(world.Time, global, conditions)` |
| `Templates/WeaponCatalog.cs` | `WeaponInfo.DelayBetweenShotsWith`/`ClipReloadWith` ohne Zeitdienst, `ScaledByRateOfFire(global, conditions, ticks)` | – | zwei öffentliche Überladungen und ein privater Helfer, nur bei 30 Hz gleich der neuen Form | **gelöscht**; der Test hält die frühere Formel als Orakel |
| `Modules/BuiltIn/Phase5Modules.cs` | `ProductionUpdate.Enqueue` | W1 | `Math.Max(totalTicks, 1)` (eine Bauzeit von 0 dauert einen Tick) | `time.AtLeastOneFrame(totalTicks)` (ein Bild); die Funktion nimmt den Zeitdienst, einziger Aufrufer ist `SimulationWorld` |
| `Modules/BuiltIn/Phase5Modules.cs` | `ProductionUpdate.Update` (`remaining`) | W2 | `head.RemainingTicks - 1` | `head.RemainingTicks - TickOrder.Next` (ein Schritt des Zählers) |
| `Modules/BuiltIn/Phase12Modules.cs` | `HordeUpdate.OnSpawn` | (W1, schon gezählt) | Zug von Hand `OriginalFrames(Range(1, Math.Max(1, ToOriginalFrames(UpdateRateTicks))))` | `world.Time.DrawFrames(LogicRandom, TicksPerOriginalFrame, AtLeastOneFrame(UpdateRateTicks))` – derselbe Zug |
| 14 Moduldateien | 12 Stellen `wert / world.Time.TicksPerSecondFixed`, 8 Stellen `gesamt / ticks` | – | nackte `Fixed`-Divisionen | `world.Time.PerSecondToPerTick(wert)` bzw. `world.Time.SharePerTick(gesamt, ticks)` – dieselbe Rechnung, jetzt zählbar |

Vier Stellen zählen neu (die beiden Aufrufer, `Enqueue`, der Zählerschritt); die übrigen Zeilen sind dieselben Stellen in der einen Schreibweise. `SimulationWorld.VisionIntervalTicks` bleibt als `public const`-Alias für vier Aufrufer außerhalb der Simulation (§6); innerhalb von `src/CnCNew.Simulation` benutzt ihn nichts.

## 4. Katalog je Rate, Fingerabdruck, Eingabemanifest

- `TemplateCatalog.Build(graph, bindings, rate, geometry)` baut den Katalog für eine Rate; `TemplateCatalog.Rate`/`.Time` sagen, für welche. Die Rate läuft als Parameter durch alle Bauer, die Ticks oder Werte je Tick erzeugen (`UpgradeCatalog`, `WeaponCatalog`, `SpecialPowerCatalog`, `ObjectCreationListCatalog`, Locomotoren, `TurretSet.Read`, `ObjectTemplate`, Basisregeneration) und über `RatedModuleConfiguration` (Konfiguration plus Zeitdienst) in jede `Parse`-Funktion eines Moduls.
- `WorldConfiguration.Rate` ist die Rate der Welt. `SimulationWorld` wirft beim Erzeugen, wenn der Katalog für eine andere Rate gebaut wurde. Sitzungen (`SkirmishMatchSession.Start`, `MissionSession.Start`) und Szenarien nehmen die Rate vom Katalog, den sie bekommen: wer den Katalog baut, nennt die Rate, und eine Sitzung kann nicht mit der falschen laufen.
- **Kosten** (gemessen mit `SimTool catalog-cost`, Release, nicht ruhig, 6. Oktober 2026, zwei Läufe): Definitionen übersetzen 4,3 bis 4,5 s und Knochentabelle 0,6 s fallen einmal an und hängen nicht an der Rate; `TemplateCatalog.Build` braucht je Rate 0,08 bis 0,22 s (erster Aufruf mit JIT 0,35 bis 0,38 s). **Ein Zwischenspeicher je Rate ist nicht nötig**; wer zwei Raten braucht, baut zweimal aus demselben Graphen.
- **Fingerabdruck:** `SimulationRate.Combine` lässt den Definitions-Fingerabdruck bei 30 Hz unverändert und faltet bei 60/120 Hz die Rate ein (`SHA-256(fingerabdruck + "+tick-rate:" + n)`). `GameData`, `SyntheticFixture` und die Werkzeuge benutzen das; ein 60-Hz-Replay scheitert an 30-Hz-Daten damit zweifach (Rate und Fingerabdruck).
- **Eingabemanifest:** `baselines/input-manifest.json` hasht die gebauten Kataloge Feld für Feld (`CanonicalObjectHasher`). Der Katalog hat genau **ein** neues Instanzfeld (`TemplateCatalog.Rate`). Damit der aufgezeichnete 30-Hz-Wert vergleichbar bleibt, lässt der Hasher des Werkzeugs dieses eine Feld aus, **solange es die Originalrate ist** (`InputManifest.RateAtOriginal`); bei jeder anderen Rate wird es beschrieben. Das ist eine Änderung am Prüfwerkzeug, nicht an der Basislinie, und gehört ins Review. Alle anderen Katalogtypen haben kein neues Feld bekommen – auch im Abschluss nicht (`SimulationTime.Probe` hängt am Zeitdienst der Rate, den der Hasher bei 30 Hz mit dem Feld `Rate` auslässt).
- `TemplateCatalog.GravityPerTickSquared` rechnet weiter bei jedem Zugriff (eine Dezimaldivision); ein Feld am Katalog würde das Eingabemanifest ändern. Die Welt liest den Wert nicht im heißen Pfad (`_significantHeight`, einmal je Welt); der Messfühler sieht die Zugriffe der Module (§11.4).

## 5. Liste für Phase 23 (bei 30 Hz richtig, bei 60 Hz anders; in Phase 20 unverändert)

Zusammengeführt aus Stufe 1 und den vier Aufträgen der Stufe 2, ohne Doppelte; zwei Zeilen der Stufe 2 sind mit dem Abschluss erledigt und stehen nicht mehr hier (`WeaponInfo.DelayBetweenShotsWith`/`ClipReloadWith` ohne Zeitdienst, `ProductionUpdate.Enqueue`). 95 Zeilen in sechs Gruppen; eine Zeile kann mehrere Symbole desselben Blocks nennen. Das ist Verhalten, keine Parametrisierung: alles aus Klasse E des Inventars (138 Stellen dort) ist hier vertreten oder in Anhang B als „nur Last“ bzw. „Reihenfolge ohne Literal“ bewertet.

**Stand am Ende der Phase 23 (9. Oktober 2026, Stand `f3acc2e`).** Jede der 95 Zeilen trägt am Ende ihrer letzten Spalte ihren Verbleib („— **Phase 23:** …“; die zwölf Zeilen der Skript-VM tragen ihn seit dem Skript-Paket im selben Stil ohne Statuswort und zählen als erledigt). Gezählt: **78 erledigt**, **13 bewusst belassen** (nur Last, reine Ordnung, Test- und Werkzeugcode, oder die Lösung liegt an anderer Stelle), **4 offen** (§5.3 `Register`, §5.4 `ObjectiveDriver.Commands`, §5.6 Quantisierung der Katalogwerte und Beträge je Tick aus Zustand). „Erledigt“ heißt: die Ursache ist benannt, in einem Commit korrigiert und von der Raten-Äquivalenz-Suite oder einem Drei-Raten-Test gemessen – nicht, dass ein Mensch es gesehen hat. Sechs erledigte Zeilen nennen in ihrem Vermerk einen offenen Rest oder die ausstehende Bestätigung des Projektinhabers; die zusammengeführte Liste dessen, was nicht gleich oder nicht belegt ist, steht in §15.7.

### 5.1 Multiplikative Faktoren je Bild und gemischte Rekursionen (11)

Bei 30 Hz richtig; bei 60 Hz wirkt der Faktor doppelt so oft. Für reine Faktoren genügt die Wurzel, für Rekursionen, die Faktor und Zuwachs mischen (W4), nicht.

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `Modules/BuiltIn/RailroadModules.cs` | `RailroadData.Acceleration` (1,01), `Braking` (0,99), `Friction` (0,97) | Faktoren je Bild auf die Zuggeschwindigkeit — **Phase 23:** erledigt: Faktoren einmal je Bild der Lok, Streckenlage je Tick anteilig, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `9871bc2` |
| `Modules/BuiltIn/RailroadModules.cs` | `RailroadBehavior.PushStart` (0,02), Anfahrwert 0,05, Stillstandsschwelle 0,1, Schaden `_pull.Speed * 10` | Zuwachs je Bild vor dem Faktor (gemischte Rekursion), Geschwindigkeiten je Bild als Literal, Schaden je Tick der Überlappung. `SpeedMax`, `KillSpeedMin`, `RunningGarrisonSpeedMax` sind seit Stufe 1 je Tick – der Zug erreicht bei 60 Hz dieselbe Höchstgeschwindigkeit, aber in der halben Zeit — **Phase 23:** erledigt: jede Geschwindigkeit des Zustands ist Strecke je Bild, Schaffner und Reibung im ersten Tick des Bildes; Höchsttempo nach 2,4333 / 2,4167 / 2,4083 s (vorher 2,4333 / 1,1000 / 1,0000), `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `9871bc2` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `NeutronMissileData.ForwardDamping`, `RelativeSpeed` | Dämpfungsanteil je Bild und Schub je Bild² im selben Schritt (`_velocity + speed·dir − damping·_velocity`) — **Phase 23:** erledigt: Rekursion aus Schub und Dämpfung einmal je Bild, Geschwindigkeit in Anteilen; Flugzeit 8,0667 s bei jeder Rate; das Ende des Sturzflugs liest `HeightDieUpdate` am Bildende, `docs/RATE-INVARIANCE-PROJECTILES.md`, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, Commit `41d1cbc`, `ad2b9e9` |
| `Modules/BuiltIn/FieldEffectModules.cs` | `TensileFormationUpdate.Friction` (0,95), `NewShare`/`LinkShare` (0,93/0,07), `BaseSlope` (0,3) | Reibung, Positionsmischung und Hangbeschleunigung je Bild — **Phase 23:** erledigt: Rekursion im ersten Tick jedes Bildes der Rutschphase, Rutschweg 65,528 bei jeder Rate; belegt nur durch den Drei-Raten-Test am Hang (die Suite misst auf der flachen Karte nichts), `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `302e88a` |
| `Modules/BuiltIn/LastBlockerModules.cs` | `SmartBombHomingData.CourseCorrectionScalar` (0,99) | Mischfaktor der Position je Bild — **Phase 23:** erledigt: Mischung einmal je Bild, Weg in Anteilen; kein Fall der Suite, `docs/RATE-INVARIANCE-PROJECTILES.md`, Commit `0f5fd14` |
| `Modules/BuiltIn/ToppleAndFallModules.cs` | `StructureToppleData.StructuralDecay` | Zerfall der Strukturintegrität je Bild — **Phase 23:** erledigt: Zerfall und Beschleunigung im ersten Tick des Bildes, Winkel je Tick anteilig; liegt nach 4,3333 s bei jeder Rate, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `f8d0077` |
| `Modules/BuiltIn/ToppleAndFallModules.cs` | `ToppleUpdateData.InitialAccelPercent` | Winkelbeschleunigung je Tick² als Anteil eines Stoßtempos je Tick: braucht einen weiteren Faktor 30/Rate (`InitialVelocityPercent` skaliert mit dem Stoßtempo von selbst, `BounceVelocityPercent` ist ein Verhältnis ohne Zeit) — **Phase 23:** erledigt: Winkelgeschwindigkeit und -beschleunigung je Bild; Fall 1,2000 s bei jeder Rate (vorher 1,2000 / 0,8000 / 0,5667); die Kippkraft wird einmal umgerechnet, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `021b318`, `bcc54ae` |
| `Modules/BuiltIn/TransportModules.cs` | `HelicopterSlowDeathData.SpiralOrbitForwardSpeedDamping` | Dämpfung je Bild (`_forwardSpeed *= …`) — **Phase 23:** erledigt: Dämpfung einmal je Bild im Bildschritt der Spirale, Datensatz hält die Werte je Bild des 30-Hz-Katalogs, `docs/RATE-INVARIANCE-FLIGHT.md`, Commit `cee88c4` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `EmpUpdate.Update` (`_scale`, Faktor 0,05 je Tick) | die Skalierung nähert sich dem Ziel in der halben Zeit (Zustand und Darstellung, keine Spielwirkung) — **Phase 23:** erledigt: Anteil nur auf den Bildticks des Pulses (Ausweg Originalraster, `docs/SIMULATION-CORE.md` §41; keine Spielwirkung), `docs/RATE-INVARIANCE-PROJECTILES.md`, Commit `9132127` |
| `Modules/BuiltIn/BridgeModules.cs` | `BridgeScaffoldBehavior.Update` | Abbremsrampe proportional zum Restweg je Tick (geometrisches Abklingen) — **Phase 23:** erledigt: Schritt des Originals im ersten Tick des Bildes, je Tick gleicher Anteil des Rests; kein Fall der Suite, ein Drei-Raten-Test, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `501350c` |
| `Modules/BuiltIn/RailroadModules.cs` | `RailroadBehavior.Update` („Wagen existieren erst im nächsten Tick“), `Collide` (Wegschieben um ein Viertel der Überlappung je Tick) | Reihenfolge über `SleepNone` (bleibt); das Wegschieben ist doppelt so schnell — **Phase 23:** erledigt: Kollisionsantwort und Wegschieben einmal je Bild des Zugs (Ausweg Originalraster), erstes Update ein Bild nach dem Erscheinen, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `9871bc2` |

### 5.2 Integration je Tick und Schwellen gegen einen Tickschritt (27)

Euler-Schritte, Ankunftsradien „ein Tick Weg“, Einrasten unter einem Schritt, Größen je Tick als absolute Größe gelesen.

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `World/Entity.cs` | `Entity.Speed` | Strecke je Tick; jeder Leser, der den Wert als absolute Größe nimmt, sieht die Hälfte (siehe `TryTopple`) — **Phase 23:** bewusst belassen: `Entity.Speed` bleibt die Strecke je Tick; die Beweger führen ihr Tempo je Bild selbst (`_frameSpeed`, über 30 Hz `Entity.SpeedPerFrame`), die Leser sind umgestellt, `docs/RATE-INVARIANCE-GROUND.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `bf028df`, `3b5c781` |
| `World/SimulationWorld.Collision.cs` | `AvoidLocalBlockers`, `StepBlocked` | Vorausschau und Ausweichschritt sind ein Tick Weg und halbieren sich — **Phase 23:** erledigt: Ausweichschritt und Vorausschau aus dem Schritt des Bildes, entschieden im Tick der Entscheidung; ein Beweger, der im Tick schon dran war, wird am Bildende gelesen, `docs/RATE-INVARIANCE-GROUND.md`, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, Commit `bf028df`, `2db1fc6` |
| `World/SimulationWorld.Collision.cs` | `StationaryBlockersAhead` | Vorausschau ist der vom Aufrufer übergebene Vektor (`Speed × 25` Ticks Weg in `Phase5Modules.cs`) — **Phase 23:** erledigt: Vorausschau 25-mal der Weg des Bildes; Bildend-Sicht wie oben, `docs/RATE-INVARIANCE-GROUND.md`, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, Commit `bf028df`, `2db1fc6` |
| `World/SimulationWorld.Collision.cs` | `ResolveFootprintCollisions`, `TrySquish` | Abtastung je Bewegungsschritt: Überlappung wird früher erkannt, Reihenfolge der Kollisionsereignisse ändert sich — **Phase 23:** erledigt: Prüfung einmal je Bild des Fahrenden (Ausweg Originalraster) – zuerst mit dem letzten Anteil, seit Runde 4 im ersten Tick, der Fahrende steht dabei am Bildende, `docs/RATE-INVARIANCE-GROUND.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `1c65446`, `e910174` |
| `World/SimulationWorld.Collision.cs` | `TryTopple` (`mover.Speed`) | Kippkraft = Strecke je Tick: halbe Kraft — **Phase 23:** erledigt: Kippkraft aus dem Tempo je Bild, `docs/RATE-INVARIANCE-GROUND.md`, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `1c65446`, `bcc54ae` |
| `World/SimulationWorld.Turrets.cs` | `IsAligned` (0,035 rad) | Fester Winkel gegen halbe Schrittweite: erster Schuss fällt auf einen anderen Zeitpunkt — **Phase 23:** erledigt: Freigabe, wenn Anfang und Ziel des Bildschritts ausgerichtet sind; Winkel beim ersten Schuss 1,57080 rad bei jeder Rate, `docs/RATE-INVARIANCE-TURRETS.md`, Commit `32eb25c` |
| `World/SimulationWorld.Turrets.cs` | `AimStep`, `RecenterStep`, `ScanStep`, `TurnAngle`, `TurnPitch` | Euler-Schritt mit Einrasten unter einem Tickschritt; Umkehr des Schwenks bei 0,035 rad — **Phase 23:** erledigt: Schritt einmal je Bild des Turms mit der Drehrate des 30-Hz-Katalogs, Weg je Tick; Winkel am Bildende bitgleich, `docs/RATE-INVARIANCE-TURRETS.md`, Commit `32eb25c` |
| `World/SimulationWorld.Turrets.cs` | `IdleScanStep`, `ScheduleScan` | Der Abstand liegt jetzt auf dem 30er-Raster; die Drehung danach ist Euler je Tick, und der Zeitpunkt, an dem der Turm zur Ruhe kommt (Start der nächsten Frist), hängt daran — **Phase 23:** erledigt: erster Schritt und Scan-Frist belegen je ein Bild; Leerlauf-Scan über 1.500 Bilder bitgleich, `docs/RATE-INVARIANCE-TURRETS.md`, Commit `276a8de` |
| `Templates/ObjectTemplate.cs` | `LocomotorInfo.CloseEnoughDist` (0 = ein Tick Weg; Verbraucher in `Phase5Modules.cs`) | Ankunftsradius von einem Tick Weg halbiert sich — **Phase 23:** erledigt: Ankunftsradius `max(CloseEnoughDist, Weg eines Bildes)`; der Marsch endet seit Runde 3 im ersten Tick des letzten Bildes, der Rest wird ausgerollt, `docs/RATE-INVARIANCE-GROUND.md`, `docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, Commit `bf028df`, `2117316` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `NeutronMissileUpdate.Update`, `DoAttack` | Zwischenpunkt „erreicht“ gegen den Tickschritt, Start kostet einen Tick, Euler-Schritt mit Dämpfung — **Phase 23:** erledigt: Start ein Bild nach dem Schuss, Zwischenpunkt und Boden einmal je Bild, `docs/RATE-INVARIANCE-PROJECTILES.md`, Commit `41d1cbc` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `SpectreGunshipUpdate.Fly`, `Strafe`, `Orbit` (`_okToFireHowitzerCounter`) | Polygonzug der Bahn, Ankunftsradius ein Tickschritt, Zähler je Strafe-Tick gegen eine Dauer in Ticks (zeitrichtig nur, solange jeden Tick gestrafft wird) — **Phase 23:** erledigt: Flug, Kreisbahn und Zielpunkt als Bildschritt am Raster der Welt, ein Zähler je Bild; **offen:** die Konstanten rechnen weiter `Wert je Tick × r` (an einer Schwelle kann das ein Bild kosten), `docs/RATE-INVARIANCE-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md`, Commit `e7a3153`, `fc0480f` |
| `Modules/BuiltIn/FerryModules.cs` | `RailedTransportDockUpdate.DoPullInDocking`, `DoPushOutDocking` | halbe Schrittweite, Ankunftsschwellen 6 bzw. 3 Einheiten fallen auf andere Ticks — **Phase 23:** erledigt: Ankunftsfrage mit dem letzten Tick jedes Bildes (Ausweg Originalraster); Lage innerhalb 0,001 Einheiten, nicht bitgleich; kein Fall der Suite, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `b53cbd3` |
| `Modules/BuiltIn/FieldEffectModules.cs` | `TensileFormationUpdate.Update` (Physikblock) | Euler-Schritt mit Reibung und Mischung je Tick; seit Stufe 2 dauert die Rutschphase bei jeder Rate 300 Bilder, die Rekursion läuft bei 60 Hz also doppelt so oft — **Phase 23:** erledigt: siehe §5.1, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `302e88a` |
| `Modules/BuiltIn/FlightModules.cs` | `JetSlowDeathBehavior.Update` (Fall), `ParachuteContain.Update` (freier Fall, Sinkflug, Ankunftsradius eine Tickstrecke), `CountermeasuresBehavior.MoveFlares` | halbimplizite Euler-Schritte, lineares Bremsen je Tick — **Phase 23:** erledigt: Fall, Abstieg und Bremsung einmal je Bild, Bewegung in Anteilen; die Beweger veröffentlichen ihren Bildrest, `docs/RATE-INVARIANCE-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md`, Commit `307bf14`, `8435ba8`, `c5a8a35`, `e06259f` |
| `Modules/BuiltIn/FlightModules.cs` | `ParticleUplinkCannonUpdate.MoveBeam` (manuell) | Wegpunktwechsel bei Restweg ≤ eine Tickstrecke — **Phase 23:** erledigt: Fahrt als Bildschritt, nächster Wegpunkt ab dem Folgebild, `docs/RATE-INVARIANCE-FLIGHT.md`, Commit `5954338` |
| `Modules/BuiltIn/JetAIUpdate.cs` | `TaxiAlongPath` (Ankunftsradius `max(Speed, 1)`), `TurnTo`, `TakeOff` (beide Zweige), `Land` (beide Zweige; Aufsetzschwelle 1 Einheit, Sinken `max(SpeedLimitZ, Speed/4)`), `ControlHeight` (`Speed/8` als Beschleunigung, `Speed/2` als Deckel) | Euler-Schritte und Schwellen gegen den Tickschritt; `Speed/8` mischt eine Größe je Tick mit einer je Tick² — **Phase 23:** erledigt: Zustände entscheiden einmal je Bild, Bewegung in Anteilen; seit Runde 3 mit den Konstanten je Bild des 30-Hz-Katalogs (`LocomotorFrames`) und dem ersten Update ein Bild nach dem Erscheinen, `docs/RATE-INVARIANCE-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md`, Commit `60fcb57`, `bb00e5f`, `346cadc`, `cc7fd75` |
| `Modules/BuiltIn/JetAIUpdate.Flight.cs` | `CircleStep` | die eingeschwungene Kreisbahn (gemessen ≈ 0,93 · Radius) hängt an der Schrittweite — **Phase 23:** erledigt: Kreisbahn aus dem Bildschritt; Jet-Einsatz über 1.500 Bilder bitgleich bei 30, 60 und 120 Hz, `docs/RATE-INVARIANCE-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md`, Commit `bbee8ab`, `346cadc` |
| `Modules/BuiltIn/Phase5Modules.cs` | `AIUpdateInterface.Move` (Ankunftsradius `max(CloseEnoughDist, Speed)`, Drehung, Beschleunigung/Bremsweg, Positionsschritt, `AvoidLocalBlockers`), `MoveWinged`, `SteerWinged`, `MoveStraight` | Euler je Tick; Ankunftsradius und Wendekreis hängen am Tick-Schritt (halber Weg je Tick), Bremsweg `v²/(2a)` in Tick-Einheiten — **Phase 23:** erledigt: Boden: Tempo und Kurs einmal je Bild, Lage je Tick; Flügelflug ebenso; Bremsweg 110,000 bei jeder Rate (vorher 110,000 / 111,250 / 111,875), `docs/RATE-INVARIANCE-GROUND.md`, `docs/RATE-INVARIANCE-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md`, Commit `bf028df`, `bbee8ab`, `346cadc` |
| `Modules/BuiltIn/PhysicsAndMinefieldModules.cs` | `PhysicsBehavior.Update` (freier Fall) | Euler je Tick mit `GravityPerTickSquared`; Landezeit weicht um Bruchteile eines Bilds ab — **Phase 23:** erledigt: Schwerkraft je Bild², Landung am ganzen Schritt beurteilt; kein eigener Fall der Suite; **offen:** der freie Fall veröffentlicht seinen Bildrest nicht, `docs/RATE-INVARIANCE-PROJECTILES.md`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md`, Commit `2777f86` |
| `Modules/BuiltIn/ProjectileModules.cs` | `DumbProjectileBehavior.CalcFlightPath` (`_segments = ceil(length / _speed)`), `Update` (ein Bahnpunkt je Tick, Detonation im Update nach dem letzten Punkt, Nachführung je Tick) | Flugdauer in ganzen Ticks statt ganzen Bildern; „ein Tick später“ ist ein halbes Bild — **Phase 23:** erledigt: ein Bahnpunkt je Bild, Detonation ein Bild nach dem letzten Punkt; Battlemaster-Granate 0,3000 s bei jeder Rate, `docs/RATE-INVARIANCE-PROJECTILES.md`, Commit `2ed62cd` |
| `Modules/BuiltIn/ProjectileModules.cs` | `MissileAIUpdate.Launch` (`_acceleration = weapon.WeaponSpeed`), `Update` (`_noTurnDistanceLeft` aus dem Vortick), `Move` (Euler, Ankunft bei Restweg ≤ ein Tick Flug) | Waffengeschwindigkeit je Tick als Beschleunigung je Tick²: volle Geschwindigkeit nach einem Tick, halber Wert; Ankunftsschwelle halbiert — **Phase 23:** erledigt: Tempo und Beschleunigung je Bild (Bild²), Zustandsmaschine einmal je Bild; seit Runde 4 aus `LocomotorFrames`, `docs/RATE-INVARIANCE-PROJECTILES.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `c4a0ac1`, `65b9783`, `ec9f90a` |
| `Modules/BuiltIn/SpecialAbilityStaging.cs` | `SpecialAbilityUpdate.StepCaptureFlash` (`_flashPhase += (1 − prep/Preparation) / 3` je Tick) | die Blitzphase wächst doppelt so schnell: doppelt so viele `CaptureFlash`-Ereignisse — **Phase 23:** erledigt: Blitzphase nur in den Ticks, die ein Bild der Vorbereitung vollenden: 4 / 4 / 4 Blitze (vorher 4 / 9 / 19), `docs/RATE-INVARIANCE-TURRETS.md`, Commit `d923cea` |
| `Modules/BuiltIn/SpecialAbilityStaging.cs` | `SpecialAbilityUpdate.TurnToward` | Einrasten innerhalb eines Tick-Schritts der Drehung — **Phase 23:** erledigt: Entscheidung im ersten Tick jedes Bildes gegen ein Bild Drehung; **offen:** die Drehung je Bild ist `Rate je Tick × r` (eine Einheit genau eine ganze Zahl von Schritten vor dem Ziel kann um ein Bild abweichen), `docs/RATE-INVARIANCE-TURRETS.md`, Commit `95bcf3d` |
| `Modules/BuiltIn/StructureModules.cs` | `StructureCollapseUpdate.Update` (Euler: `_height -= _velocity`, `_velocity -= g · (1 − Damping)`; höchstens ein Burst je Tick) | Einsturzzeit weicht um die Euler-Ordnung ab — **Phase 23:** erledigt: Sinkgeschwindigkeit je Bild, Höhe je Tick anteilig; Sichthöhe innerhalb 0,001 Einheiten, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `01535b0` |
| `Modules/BuiltIn/ToppleAndFallModules.cs` | `ToppleUpdate.Update` (Euler, im Abprall-Tick keine Beschleunigung), `StructureToppleUpdate.StepToppling`, `ApplyCrushingDamage` (ein Waffenschuss je Tick, in dem die Vorderkante weiterreicht), `_doneAtTick` (Done im Folgetick), `HeightDieUpdate.Update` (`_lastZ` des Vorticks) | Fallzeit weicht ab; doppelt so viele Quetsch-Schüsse; `VelocityBounceLimit` und `AccelFactor` sind seit Stufe 2 je Tick — **Phase 23:** erledigt: Umstürzen und Gebäudesturz je Bild; `HeightDieUpdate` sieht einmal je Bild nach (Ausweg Originalraster) und liest das Bildende, `docs/RATE-INVARIANCE-PHYSICS.md`, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, Commit `021b318`, `f8d0077`, `5440888`, `ad2b9e9`, `0cd38b7` |
| `Modules/BuiltIn/TransportModules.cs` | `HelicopterSlowDeathBehavior.Update` (Spin/Orbit/Fall, Vergleich `Tick − _lastSelfSpinUpdate > SelfSpinUpdateDelay` gegen ungerundete Ticks), `TunnelContain.Update` (`MaxHealth / TicksForFullHeal` je Tick, je Tunnel) | Euler je Tick; Vergleich kippt auf Tick- statt Bildgrenzen — **Phase 23:** erledigt: Absturz als Bildschritt, erster Schritt ein Bild nach dem Tod; Tunnelheilung einmal je Bild ab dem Eintritt (Ausweg Originalraster), `docs/RATE-INVARIANCE-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `cee88c4`, `81931ab`, `96be02e` |
| `Modules/BuiltIn/VehicleModules.cs` | `BattleBusSlowDeathBehavior.Update` (Wurf: `Z += v`, dann `v += g`) | Euler je Tick; Wurfhöhe weicht um die Euler-Ordnung ab — **Phase 23:** erledigt: Steiggeschwindigkeit je Bild, Schwerkraft und Landung mit dem letzten Tick; Scheitel 45,200 bei jeder Rate; „in der Luft“ 2,3667 / 2,3667 / 2,3750 s (innerhalb der Toleranz, nicht exakt), `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `d311db0` |

### 5.3 Zähler mit Periode N + 1, Ein-Tick-Versätze und Untergrenzen, die nicht auf das Raster gelegt werden konnten (18)

Reihenfolge der Phasen und „ein Tick später“ sind bei 60 Hz ein halbes Bild; wo das Ergebnisse verschiebt, steht es hier.

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `World/SimulationWorld.Turrets.cs` | `StepTurrets`, `StepTurret` | Ein Dreh- und Fristschritt je Tick; „Halten“ gilt bis einschließlich `HoldUntil` (N + 1 Schritte: der Zusatzschritt ist ein Tick, kein Bild) — **Phase 23:** erledigt: der Zusatzschritt des Haltens ist ein Bild (`tick < HoldUntil + Ticks je Bild`): 16 Bilder bei jeder Rate, `docs/RATE-INVARIANCE-TURRETS.md`, Commit `6e0c691` |
| `World/SimulationWorld.FiringTracker.cs` | `StepFiringTrackers` (`tick <= CooldownFrame`), `PreAttackDone` (`_tick <= PreFinish + …`), `PreAttackSlotOf` | Zähler mit Periode N + 1: gehandelt wird im Tick nach der Frist, also einen Tick statt ein Bild später — **Phase 23:** erledigt: beide Vergleiche `< Frist + Ticks je Bild`; `PreAttackSlotOf` bewusst unverändert (Abfrage zwischen zwei Schritten), `docs/RATE-INVARIANCE-TURRETS.md`, Commit `165e6d8` |
| `World/SimulationWorld.cs` | `RunUpdates` | Rückgabe unter `SleepNone` wird ein Tick; jedes Modul mit Schlaf 1 läuft doppelt so oft je Sekunde — **Phase 23:** bewusst belassen: der Zeitplaner bleibt tickgenau – eine Regel für alle Module hätte die Integratoren gebremst; gelöst je Modul, `docs/RATE-INVARIANCE-TIMING.md` |
| `World/SimulationWorld.cs` | `ProcessSpawns` | Jedes Glied einer Spawn-Kette kostet einen Tick, also die halbe Zeit — **Phase 23:** erledigt: ein in der Erzeugungsphase angefordertes Objekt erscheint ein Bild später (`PendingSpawn.NotBefore`, nur über 30 Hz), `docs/RATE-INVARIANCE-CONSOLIDATION.md`, Commit `f8ce7c4` |
| `World/SimulationWorld.cs` | `Register` (`NextWake = _tick + SleepNone`) | Erstes Update einen Tick nach dem Erscheinen — **Phase 23:** offen: je Modul umgestellt, wo ein Fehlschlag oder eine erste Abweichung gemessen ist (Feld, Tarnung, Create-Module, Flugblatt, `SpawnBehavior`, Sammelpunkt-Befehl, `AutoFindHealingUpdate`, `JetAIUpdate`, `ChinookAIUpdate`, `PilotFindVehicleUpdate`); die allgemeine Regel „erstes Bild für alle Module“ ist gemessen und nicht übernommen (13 / 15 rote Tests bei 60 / 120 Hz); **nicht umgestellt:** `DeliverPayloadAIUpdate`, Fallschirm, Fähre, dynamische Aufdeckung, Feuersturm, `FireOCLAfterWeaponCooldownUpdate`, Feindnähe-Scan, Checkpoint, `docs/RATE-INVARIANCE-TIMING.md`, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, `docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `9f61b3d`, `4e83582`, `68ae9c3`, `21a6f7d`, `cc7d92e`, `35952fb`, `cc7fd75`, `2837f1e`, `713c662` |
| `World/SimulationWorld.cs` | `Step` (Phasenfolge) | Wirkungen zwischen Phasen sind um einen Tick versetzt (Turmwinkel nach Bewegung, Schaden nach Zerstörung, Sicht nach Spawn) — **Phase 23:** bewusst belassen: die Phasenfolge bleibt; statt sie zu ändern lesen Beobachter die Bildend-Sicht, und die Sicht wird nach dem letzten Tick ihres Bildes aufgefrischt, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, Commit `a8eed4a` |
| `World/SimulationWorld.Construction.cs` | `_saleDurationTicks` | 180 Bilder weniger **ein Tick**: 5,983 s bei 60 Hz statt 5,967 s — **Phase 23:** erledigt: 180 Bilder weniger ein Bild: 5,9667 s bei jeder Rate (179 / 358 / 716 Ticks), `docs/RATE-INVARIANCE-TIMING.md`, Commit `404fab6` |
| `Modules/BuiltIn/AirWeaponModules.cs` | `LeafletDropBehavior.Update` | das erste Update kommt einen Tick nach dem Entstehen: ein `Delay` von einem Bild (zwei Ticks) ist dann noch nicht abgelaufen, das Modul schläft für immer statt jeden Tick zu wirken. Naheliegende Lösung: das erste Aufwachen auf `OriginalFrames(1)` legen — **Phase 23:** erledigt: erstes Update ein Bild nach dem Entstehen: wirkt nach 0,0333 s bei jeder Rate (vorher über 30 Hz nie), `docs/RATE-INVARIANCE-TIMING.md`, Commit `68ae9c3` |
| `Modules/BuiltIn/BattlePlanModule.cs` | `BattlePlanUpdate.Update` | ein Zustandsübergang je Tick: die Zusatzticks zwischen den Phasen halbieren sich zeitlich — **Phase 23:** erledigt: eine Frist von null ist das nächste Bild (`ZeroIsNextFrame`), `docs/RATE-INVARIANCE-TIMING.md`, Commit `529d514` |
| `Modules/BuiltIn/HackerAndAssaultModules.cs` | `HackInternetAIUpdate` – erster Zähler nach einem Befehl von außen | Versatz unter einem Bild (Anhang A.2, „Phasenlage der Zähler“) — **Phase 23:** erledigt: eine von einem Befehl gestartete Phase dauert ihre Bilder: erste Zahlung 8,1000 s bei jeder Rate, `docs/RATE-INVARIANCE-TIMING.md`, Commit `938a32c` |
| `Modules/BuiltIn/Phase5BlockerModules.cs` | `StealthDetectorUpdate` bei `DetectionRate = 0` | scannt je Tick statt je Bild (bewusst, Anhang B.3) — **Phase 23:** erledigt: Marke `DetectionRate` + ein Bild, Schlaf `AtLeastOneFrame`, `docs/RATE-INVARIANCE-TIMING.md`, Commit `f922979` |
| `Modules/BuiltIn/TransportModules.cs` | `HelicopterSlowDeathBehavior.BeginSlowDeath` (`_bladeFlyOff = Range(MinBladeFlyOffDelay, MaxBladeFlyOffDelay)` über **ungerundete** `Fixed`-Ticks, je Tick −1) | W1 d, nicht umgestellt: die Grenzen liegen nicht auf dem Raster (`parseDurationReal`), ein Zug in Bildern müsste die Grenzen zurückteilen und würde bei 60 Hz anders runden als das Original; der gezogene Wert ist bei 60 Hz nicht das Doppelte — **Phase 23:** erledigt: Grenzen in ungerundeten Bildern des 30-Hz-Katalogs, ein Zug, Zähler je Bild: Rotor fliegt nach 30 Bildern bei jeder Rate ab, `docs/RATE-INVARIANCE-FLIGHT.md`, Commit `82dbdba` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `FirestormDynamicGeometryInfoUpdate.Update` (`ratio = _timeActive / TransitionTime`, Ende bei `_timeActive > TransitionTime`) | Übergang dauert `TransitionTime + 1` Ticks: bei 60 Hz ein halbes Bild kürzer als bei 30 Hz (W1 c in einer Interpolation; `_timeActive` ist zugleich der Zähler der Interpolation) — **Phase 23:** erledigt: der Zähler des Rückwegs beginnt ein Bild weniger ein Tick unter null: 2,1333 s bei jeder Rate, `docs/RATE-INVARIANCE-TIMING.md`, Commit `47c760c` |
| `Scripting/ScriptEngine.Phase13.cs` | `UNIT_EMPTIED` (`frame - TickOrder.Next`) | Ordnung, nicht skaliert; setzt Auswertung in **jedem** Tick voraus – läuft die VM nur an Bildgrenzen, muss die Nachbarschaft „voriges Bild“ heißen. — **Phase 23:** vergleicht mit dem vorigen Bild (`frame - Time.TicksPerOriginalFrame`), weil die VM je Originalbild auswertet; Test `TickRateScriptClockUnitEmptiedLooksOneFrameBack` (vorher bei 60 Hz nie wahr), Commit 2fe8b32 |
| `Scripting/ScriptEngine.Phase13.cs` | `UpdateBridgeStates` (Wechsel-Flag ein Update lang), `AttachPendingTraps`, Reichweitenprüfung in `UpdateCaptureOrders` | Fenster bzw. Versatz von einem Tick; Prüfung je Tick. — **Phase 23:** läuft mit der VM einmal je Originalbild (`docs/SIMULATION-CORE.md` §41): Fenster und Versatz sind wieder ein Bild, die Prüfung fällt auf dieselben Spielzeitpunkte wie bei 30 Hz, Commit 727c4d6 |
| `Mission/MissionSession.cs` | `Step` (Reihenfolge), `Engine.TimeFrozen` | Skripte, dann Welt, dann Ereignisse: Skripte reagieren einen Tick später (1/60 s). Im `FREEZE_TIME` zählt nur die Aufrufrate von `Step`: Skripttimer laufen weiter in Ticks der Rate, die Welt steht. — **Phase 23:** die Reihenfolge bleibt, die VM arbeitet aber nur auf Ticks, die ein Originalbild beginnen – Skripte reagieren bei jeder Rate auf dem Bildraster von 30 Hz; unter `FREEZE_TIME` zählt die VM die Aufrufe selbst (`frozenPhase`), Skripttimer behalten eingefroren ihre Dauer, Commit 727c4d6 |
| `Modules/BuiltIn/LastBlockerModules.cs`, `Modules/BuiltIn/MatchModules.cs` | `SupplyWarehouseCripplingBehavior.OnDamage` (`Math.Max(SuppressTicks, SleepNone)`), `BaseRegenerateUpdate.OnDamage` (`Math.Max(BaseRegenDelayTicks, SleepNone)`) | Bei einer Frist von 0 wacht das Modul nach einem Tick statt nach einem Bild auf (benannte Ausnahmen der Regel „Untergrenze ein Tick“, §10) — **Phase 23:** erledigt: Untergrenze `AtLeastOneFrame`; die benannten Ausnahmen der Quelltextregel entfallen, `docs/RATE-INVARIANCE-TIMING.md`, Commit `c6ce657` |
| `Modules/BuiltIn/Phase5Modules.cs` | `StealthUpdate.MarkDetected` (`Math.Max(ticks, SleepNone)`) | Untergrenze eines Werts, den der Aufrufer aus Rate + `TickOrder.Next` bildet; ein Aufrufer mit 0 enttarnt für einen Tick — **Phase 23:** erledigt: Untergrenze ein Bild, `docs/RATE-INVARIANCE-TIMING.md`, Commit `f922979` |

### 5.4 Auswertung und Abtastung je Tick, die Verhalten ist (25)

Rhythmen ohne eigenes Literal: was je Tick läuft, läuft bei 60 Hz doppelt so oft je Spielzeit („nur Last“, wo es keine Ergebnisse ändert).

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `World/SimulationWorld.Acquisition.cs` | `StepAcquisition`, `AutoFire` | Ein Durchlauf und ein Feuerversuch je Tick: Schusszeitpunkte fallen auf das feinere Raster, sobald eine Bedingung (Turm ausgerichtet, Sicht) zwischen zwei Bildern wahr wird — **Phase 23:** erledigt: der Durchlauf bleibt je Tick, **eine Einheit entscheidet aber nur im ersten Tick ihres eigenen Bildes** und liest die Bildend-Sicht – nach dem Wortlaut des Auftrags ein verbotener Ausweg für den Zielerwerb, vorläufig gemacht, Bestätigung des Projektinhabers steht aus (§15.7, Entscheidung 1), `docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, `docs/RATE-INVARIANCE-TURRETS.md`, Commit `f6dacdb`, `04e2fc8`, `2d637e1` |
| `World/SimulationWorld.Turrets.cs` | `_turretWaits` (auch `AutoFire`, `TryFireActiveWeapon`) | Zählt zurückgehaltene Feuerversuche je Tick; der Wert (Teil des Hashes) verdoppelt sich — **Phase 23:** erledigt: zählt Bilder des Wartens: 15 / 15 / 15 (vorher 15 / 30 / 59), `docs/RATE-INVARIANCE-TURRETS.md`, Commit `dc888d3` |
| `World/SimulationWorld.Turrets.cs` | `KeepsAttackIntent` | Sicht ist nur alle drei Bilder aktuell, geprüft wird je Tick; die Frist beginnt am ersten Tick außer Reichweite — **Phase 23:** erledigt: in den Entscheidungsticks des Turms ausgewertet und am Bildende gelesen; die Sicht wird nach dem letzten Tick ihres Bildes aufgefrischt, `docs/RATE-INVARIANCE-TURRETS.md`, `docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, Commit `c87748a`, `a8eed4a` |
| `Modules/BuiltIn/MineModules.cs` | `MinefieldBehavior.Update` | Schaden je Tick löst den Abgleich der virtuellen Minen in feineren Schritten aus — **Phase 23:** erledigt: der Schwellweg wird einmal je Bild des Fahrenden verglichen (Ausweg Originalraster, ein Raster für die Meldung); das Auszehren nach dem Tod des Erbauers bleibt bewusst je Tick, `docs/RATE-INVARIANCE-TIMING.md`, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, Commit `eca983f`, `0136733` |
| `Modules/BuiltIn/Phase12Modules.cs` | `FireWeaponWhenDamagedBehavior.Update` | Dauerwaffe wird je Tick geprüft (nur Last; der Schussabstand liegt im Raster) — **Phase 23:** bewusst belassen: nur Last; das erste Update liegt ein Bild nach dem Entstehen, `docs/RATE-INVARIANCE-TIMING.md`, Commit `68ae9c3` |
| `Modules/BuiltIn/Phase5Modules.cs` | `ProductionUpdate.Update` (Tür je Tick abfragen) | fertige Einheit verlässt die Fabrik bis zu einen halben Frame früher — **Phase 23:** bewusst belassen: nur Last (die Türfrist liegt im Raster); das erste Bild eines Produktionsauftrags zählt ganz, `docs/RATE-INVARIANCE-TIMING.md`, Commit `43d4a30` |
| `Modules/BuiltIn/ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` | Reichweitenprüfung und Schussversuch je Tick (die Zähler liegen jetzt im Raster) — **Phase 23:** erledigt: das Paket PROJECTILES hatte die Stelle bewusst gelassen; seit eine KI Avenger baut, schlug sie fehl: der Laser entscheidet jetzt im ersten Tick des Bildes seines Trägers und liest am Bildende (Ausweg nahe am Verbot für Zielerwerb, Bestätigung des Projektinhabers steht aus, §15.7 Entscheidung 1), `docs/AI-OBSERVED-2026-10-08.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `3c24352`, `887b51e` |
| `Modules/BuiltIn/StructureModules.cs` | `FloatUpdate.Update` | Lauf je Tick, nur Last — **Phase 23:** bewusst belassen: nur Last, `docs/RATE-INVARIANCE-TIMING.md` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `FireOCLAfterWeaponCooldownUpdate.Update` (läuft je Tick, liest den Schuss des Vorticks) | Reihenfolge, unverändert richtig; nur Last — **Phase 23:** bewusst belassen: nur Last; das erste Bild des Moduls ist nicht umgestellt, `docs/RATE-INVARIANCE-TIMING.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `WaveGuideUpdate.Update`/`Advance`/`DoDamage` | ein Pfadschritt und eine Schadensabtastung je Tick: doppelt so dichte Abtastung der Front — **Phase 23:** erledigt: Front und Bahnende mit dem letzten Tick jedes Bildes (Ausweg Originalraster); kein Fall der Suite, ein Drei-Raten-Test, `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `c333364` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `CheckpointUpdate.Update` (zwei Vollsuchen je Tick) | nur Last; der Torschritt ist jetzt je Tick skaliert — **Phase 23:** bewusst belassen: nur Last; das erste Bild des Moduls ist nicht umgestellt, `docs/RATE-INVARIANCE-TIMING.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` |
| `Scripting/ScriptEngine.cs` | `Update` (W6) | Jedes aktive Skript ohne Verzögerung wird je Tick ausgewertet: Zähler, die ein Skript je Auswertung erhöht, und wiederholte Aktionen laufen doppelt so oft. Naheliegend: nur an Bildgrenzen (`Time.IsOriginalFrameBoundary`). — **Phase 23:** Ausweg „Originalraster“ (`docs/SIMULATION-CORE.md` §41): `Update` arbeitet nur auf Ticks, die ein Originalbild beginnen; Ursache der Verschiebung von `MD_USA02`, Messung §15.3, Commit 727c4d6 |
| `Scripting/ScriptEngine.cs` | Timerwerte (`Counter.Value`, `IsTimer`) | Seit Stufe 1 in Ticks, je Tick −1. Vergleiche einer Karte gegen Bildliterale (`COUNTER`, `TIMER`-Bedingungen mit Zahl) und Rechnen auf einem Timer (`INCREMENT_COUNTER`, `DECREMENT_COUNTER`, `SET_COUNTER` roh) meinen Bilder – bei 60 Hz passt die Einheit nicht. Löst sich, wenn die VM an Bildgrenzen läuft und Timer wieder Bilder sind (dann die Skalierung der Stufe 1 in `SET_TIMER`, `SecondsToFrames`, `SetSequenceTimer` zurücknehmen); getrennt davon nicht ohne Zustandsänderung lösbar. — **Phase 23:** Timer zählen wieder Bilder des Originals (die Skalierung der Stufe 1 in `SET_TIMER`, `SecondsToFrames`, `SetSequenceTimer` und im Endspiel-Zeitgeber ist zurückgenommen); `CounterValue` eines Timers ist bei jeder Rate in Bildern, der eine Verbraucher (`MissionPresentationDirector.CounterForDisplay`) rechnet nicht mehr um, Commit 727c4d6 |
| `Scripting/ScriptEngine.Actions.cs` | `UpdateFollowers` (Teil je Tick), `UpdateTeamFollower` | Ankunft am Wegpunkt, Neuausgabe des Marschbefehls und `FireAtOrdered` laufen je Tick: feinere Abtastung, mehr Befehle je Sekunde. Der gestaffelte Rest ist umgestellt. — **Phase 23:** läuft mit der VM einmal je Originalbild: Ankunft, Neuausgabe des Marschbefehls und `FireAtOrdered` fallen auf das Bildraster, gleich viele Befehle je Spielzeit wie bei 30 Hz (Befehlstakt der Skripte, nicht Bewegung oder Zielerwerb der Welt), Commit 727c4d6 |
| `Scripting/ScriptEngine.Sequential.cs` | `ProcessSequences`, `SequenceItem.FramesToWait`, `_busyThisTick` | Wartezähler sind skaliert (Stufe 1), aber Leerlauf wird je Tick abgetastet und der Ein-Tick-Versatz zwischen Befehl und Welt ist 1/60 s. — **Phase 23:** Leerlauf wird je Originalbild abgetastet, `FramesToWait` zählt Bilder (N Bilder halten N + 1 Updates, bei jeder Rate dieselbe Spielzeit); Test `TickRateScriptClockSequentialWait`, Commit 727c4d6 |
| `Scripting/ScriptEngine.ExtendedActions.cs` | `ProcessTeamBuilds` | Je offener Bestellung höchstens ein Produktionsbefehl je Tick: doppelt so schnell eingereiht. — **Phase 23:** höchstens ein Produktionsbefehl je offener Bestellung und Originalbild, Commit 727c4d6 |
| `Scripting/ScriptEngine.TeamQueues.cs` | `UpdateTeamQueues`, `UpdateBuildingTeam` | Prüfung je Tick; Startaktionen laufen je Tick, solange ein Mitglied untätig ist. — **Phase 23:** Prüfung und Startaktionen je Originalbild; die Fristen (`teamInitialIdleFrames`, 60 s) vergleichen weiter mit dem Tick der Welt und liegen auf dem Raster, Commit 727c4d6 |
| `Scripting/ScriptEngine.TeamEvents.cs` | `ProcessTeamEvents`, `ScriptTeam.WasIdle`, `_pendingTeamScripts` | Feindsicht und Verlustschwelle je Tick; „zwei Ticks untätig“ ist halb so lang; Zerstört-Skripte einen Tick später. — **Phase 23:** je Originalbild: „zwei Updates untätig“ sind zwei Bilder, Zerstört-Skripte laufen im nächsten Update (die Todesereignisse sammelt `Observe` weiter in jedem Tick), Commit 727c4d6 |
| `Scripting/ScriptEngine.Conditions.cs` | `PLAYER_LOST_OBJECT_TYPE`, `NAMED_/TEAM_ENTERED/EXITED_AREA*` | Flanken gegen die vorige Auswertung: Fenster und Abtastung hängen am Auswertungstakt. — **Phase 23:** die vorige Auswertung liegt bei jeder Rate ein Originalbild zurück, die Flanken sehen dieselben Spielzeitpunkte wie bei 30 Hz (Commit 727c4d6); Test `TickRateScriptClockSeesWhatHappenedBetweenFrames` und das Merken kurzlebiger benannter Objekte, Commit d710835 |
| `Scripting/ScriptEngine.Phase13.cs` | `SUPPLY_SOURCE_SAFE` | Kein Zwischenspeicher (Original: 2 s, `ScriptConditions.cpp`, `evaluateSkirmishSupplySourceSafe`); folgt dem Auswertungstakt. — **Phase 23:** folgt dem Auswertungstakt, der jetzt bei jeder Rate das Originalbild ist (Commit 727c4d6); der fehlende 2-s-Zwischenspeicher des Originals bleibt offen (unabhängig von der Rate) |
| `Skirmish/SkirmishMatchSession.cs` | `Step` | `SkirmishAi.Update` wird je Tick aufgerufen (kehrt außerhalb der Bilder sofort zurück); kein Verhalten, nur Aufrufzahl. — **Phase 23:** bewusst belassen: nur Aufrufzahl, `docs/RATE-INVARIANCE-TIMING.md` |
| `Skirmish/SkirmishAi.cs` | `Think` (Fristen am Denkraster), `FindPlacement`/`ProduceArmy` (Zufallszüge je Denkschritt), `PlacementChecksPerThink`, `paths > 12` | Mit skaliertem Raster **und** skalierten Fristen ist die wirksame Dauer und die Zahl der Züge je Spielzeit dieselbe wie bei 30 Hz; die KI sieht aber eine Welt, die sich zwischen zwei Denkschritten feiner bewegt hat (andere Positionen, andere `BuildTicks`-Reste) – Abläufe sind nicht tickgleich zu 30 Hz. — **Phase 23:** erledigt: kein Eingriff in die KI nötig: seit die Welt zwischen zwei Denkschritten bei jeder Rate dieselbe ist, sind die drei Gefechte der Gruppe `ai` über 14.400 Bilder in jedem Feld gleich; **offen:** auf ausgelieferten Karten stimmen 14 von 16 Paarungen in Sieger und Bild des Siegs überein, 120 Hz ist dort nicht gemessen, `docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `502e75b`, `1b82e45` |
| `Testing/ObjectiveDriver.Commands.cs` | `Act`, `DriveAssaults` | Je Tick ein `FireWeaponCommand` je Angriffspaar, sobald die Waffe feuern darf (wie `StandingOrders`, W5). — **Phase 23:** offen: nicht untersucht; gemessen ist nur, dass der Zielfahrer bei 30, 60 und 120 Hz dasselbe Endskript zur selben Spielzeit erreicht (eine Ausnahme: Niederlage auf `MD_USA02` −1,00 s); der Nur-Befehle-Agent ist bei 60 Hz nicht gelaufen,  |
| `Testing/ObjectiveDriver.cs` | `IdleRoundsBeforeHarm`, `ExhaustiveRoundsBeforeUnion` | Zählen Runden; mit skaliertem `spacing` wieder 20 s bzw. 10 s. Nicht angefasst. — **Phase 23:** bewusst belassen: nicht angefasst (zählt Runden, mit skaliertem Abstand dieselbe Spielzeit),  |
| `Testing/StressScenario.cs` | ganzer Zeitplan | Zählt Schritte (§8.3). — **Phase 23:** bewusst belassen: zählt Schritte: 216.000 Schritte sind bei 60 Hz eine Stunde Spielzeit, mit neuem Endhash (§15.5.2), , Commit `ea52f87` |

### 5.5 Zufallszüge je Tick (4)

Ein Zug je Tick verbraucht den Logikstrom doppelt so schnell; die Folge der Werte ist bei jeder Rate eine andere.

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `World/SimulationWorld.Acquisition.cs` | `MoodCheckOffset` | Der Versatz liegt jetzt auf dem 30er-Raster, die Ziehung hasht aber die Ticknummer der Welt: die Folge der Werte ist bei jeder Rate eine andere — **Phase 23:** erledigt: der Versatz hasht die Bildnummer, gezählt auf dem Raster der Einheit, `docs/RATE-INVARIANCE-TURRETS.md`, `docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, Commit `146f92d`, `f6dacdb` |
| `Modules/BuiltIn/SlaveModules.cs` | `SlavedUpdate.DoRepairLogic` (läuft im Reparaturzustand je Tick: Höhen-Zufallszug und Bewegungsbefehl je Tick, solange die Drohne nicht nah genug ist) | doppelt so viele Züge aus dem Logikstrom und Befehle — **Phase 23:** erledigt: Zug und Befehl im Reparaturzustand nur im Tick, der ein Bild der Welt beginnt (Ausweg Originalraster): 100 / 100 / 100 Züge in 100 Bildern, `docs/RATE-INVARIANCE-TURRETS.md`, Commit `01121cc`, `e1252df` |
| `Modules/BuiltIn/SlaveModules.cs` | `DoAttackLogic`/`DoScoutLogic`/`DoGuardLogic` (`RandomDirection` je Prüfung), `MobMemberSlavedUpdate.Update` (`Range(0, 10)` je Prüfung) | liegen im Prüfraster (jetzt skaliert) – unverändert viele Züge je Spielzeit; hier nur zur Vollständigkeit — **Phase 23:** bewusst belassen: lag schon im Prüfraster; das Mob-Mitglied liest seinen Nexus seit Runde 3 am Bildende, `docs/RATE-INVARIANCE-TURRETS.md`, `docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, Commit `c987fb9` |
| `Modules/BuiltIn/SpecialsModules.cs` | `GrantStealthBehavior.Update` (Scan je Tick), `WanderAIUpdate.Update` (zwei Züge und ein Befehl je Leerlauf-Tick), `StickyBombUpdate.Update`, `ProjectileStreamUpdate.Update` | Läufe je Tick; `WanderAIUpdate` zieht doppelt so oft aus dem Logikstrom — **Phase 23:** erledigt: `GrantStealthBehavior` und `WanderAIUpdate` auf dem Bildraster (Ausweg Originalraster); `StickyBombUpdate` und `ProjectileStreamUpdate` bewusst belassen (kein Zufallszug, kein Befehl), `docs/RATE-INVARIANCE-TURRETS.md`, Commit `f1c5ceb`, `022bec8` |

### 5.6 Reste umgestellter Stellen (10)

Stellen, die Phase 20 auf das Raster gelegt hat und die trotzdem nicht bitgleich skalieren (Rundung in `Fixed`, ungerundete Dauern).

| Datei | Symbol | Was bei 60 Hz anders ist |
|---|---|---|
| `World/SimulationWorld.Projectiles.cs` | `FlightTicks` | Gezählt wird jetzt in Bildern; der Weg eines Bildes ist aber `Tempo je Tick × Ticks je Bild`, und das Tempo je Tick ist bei 60/120 Hz für sich gerundet – an einer Bildgrenze kann die Flugzeit um ein Bild von 30 Hz abweichen — **Phase 23:** erledigt: gezählt gegen das Tempo je Bild des 30-Hz-Katalogs: 61 / 61 / 61 Bilder (vorher 61 / 60 / 61), `docs/RATE-INVARIANCE-PROJECTILES.md`, Commit `b5b9fd4` |
| `Modules/BuiltIn/ContainModules.cs` | `GarrisonContain.HealOne` | Fixed-Rundung der kleineren Scheibe `MaxHealth / Ticks` (Summe weicht in den letzten Bits ab) — **Phase 23:** erledigt: die Scheibe des Originals einmal je Bild ab dem Eintritt (Ausweg Originalraster), `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `b05da71` |
| `Modules/BuiltIn/BridgeModules.cs`, `JetAIUpdate.cs` | `PerOriginalFrameToPerTick(…)` an der Verwendung | bei 60/120 Hz eine `Fixed`-Division je Update (bei 30 Hz keine Rechnung). Für Phase 22/23: einmal je Welt vorhalten, falls es im Profil auftaucht — **Phase 23:** erledigt: keine Division durch den Ratenfaktor mehr im Update; die Tempi sind Konstanten je Bild, `docs/RATE-INVARIANCE-PHYSICS.md`, `docs/RATE-INVARIANCE-FLIGHT.md`, Commit `501350c`, `60fcb57` |
| `Modules/BuiltIn/RepairModules.cs` | `RepairDockUpdate.Action` (`_healthToAddPerTick = (Max − Health) / FramesForFullHeal`) | Wert je Tick aus ungerundeten Ticks; setzt einen Aufruf je Tick voraus – gleicht sich aus, bis auf die `Fixed`-Rundung — **Phase 23:** erledigt: Scheibe einmal je Bild ab der ersten Aktion: 150 Bilder mit Heilung bei jeder Rate (vorher 150 / 151 / 151), `docs/RATE-INVARIANCE-PHYSICS.md`, Commit `b05da71` |
| `Modules/BuiltIn/RetailMatchModules.cs` | `HealContain.Update` (`MaxHealth / TimeForFullHealTicks` je Tick) | W1 a: Heilung je Tick aus der Tickzahl – gleiche Dauer, andere `Fixed`-Rundung je Schritt — **Phase 23:** erledigt: Scheibe einmal je Bild; dieselbe Form später für `TransportContain`, `docs/RATE-INVARIANCE-PHYSICS.md`, `docs/AI-OBSERVED-2026-10-08.md`, Commit `b05da71`, `df639ff` |
| `Modules/BuiltIn/TransportModules.cs` | `ChinookAIUpdate.Update` (`climb = locomotor.Speed`) | Steigrate je Tick folgt der Geschwindigkeit je Tick – gleiche Rate je Spielzeit — **Phase 23:** erledigt: Höhenschritt je Bild in Anteilen; seit Runde 4 erstes Update ein Bild nach dem Erscheinen und Boden am Ende der Bewegung des Bildes, `docs/RATE-INVARIANCE-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `0bf4e6c`, `2837f1e`, `b62ccc4`, `10e0031` |
| `Modules/BuiltIn/WorldEffectModules.cs` | `DynamicShroudClearingRangeUpdate.Update` (`_current += native / GrowTime` je Tick) | gleicht sich über die Tickzahl aus, andere `Fixed`-Rundung — **Phase 23:** erledigt: Zuwachs je Bild exakt auf die Ticks verteilt, Übergabe mit dem letzten Tick; der Zwischenwert `current` läuft innerhalb des Bildes voraus und ist vom Feldvergleich ausgenommen, `docs/RATE-INVARIANCE-PHYSICS.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `c5b43fa` |
| `Testing/ObjectiveDriver.Leaves.cs` | `GuardMargin` (150 Welteinheiten) | Rand gegen den Weg einer Einheit je Planungsrunde; mit skalierter Runde unverändert richtig, hängt aber an der Bewegung (Klasse E). — **Phase 23:** bewusst belassen: nicht angefasst (Test- und Werkzeugcode),  |
| `Templates/ObjectTemplate.cs`, `Templates/TurretInfo.cs`, `Templates/WeaponCatalog.cs` | `LocomotorInfo.Braking`/`Acceleration`/`Lift`/`TurnRatePerTick`, `TurretInfo.PitchRate`/`TurnRate`, `WeaponInfo.WeaponSpeed` | Quantisierung der Werte je Tick und je Tick² in Q47.16 (Zahlen in §11.2); Eingabe für Phase 22 — **Phase 23:** offen: die Quantisierung selbst ist seit Phase 22 kein Befund mehr; wo es auf die letzten Bits ankommt, stehen die Konstanten je Bild des 30-Hz-Katalogs neben dem Katalogtyp (`LocomotorFrames`, `TurretInfo.RatesPerOriginalFrame`) – für Bodenbewegung, Flügelflug, Jets, Raketen und Türme; **offen:** Spectre, Chinook-Höhenschritt, Fallschirm, Leuchtkörper, Partikelstrahl, abgeschossener Jet, Schwerkraft der Physikmodule und das Gruppentempo rechnen weiter `Wert je Tick × r`, `docs/RATE-INVARIANCE-GROUND.md`, `docs/RATE-INVARIANCE-TURRETS.md`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`, Commit `bf028df`, `32eb25c`, `346cadc`, `ec9f90a` |
| `Time/SimulationTime.cs` | `PerSecondToPerTick(Fixed)`, `SharePerTick`, `PerOriginalFrameToPerTick(Fixed)` | Beträge je Tick, die zur Laufzeit aus Zustand entstehen, verlieren bei jeder Verdoppelung ein Bit; der Zähler `RunTimeRoundedToZero` zählt die Fälle, die ganz verschwinden (§11.3) — **Phase 23:** offen: für Heilung gelöst, indem die Scheibe des Originals einmal je Bild gutgeschrieben wird (Reparaturdock, Kaserne, Tunnel, Lautsprecherturm, Reparatur durch einen Bauenden, GLA-Loch, Truppentransporter); **offen:** `SlavedUpdate` (Reparaturdrohne) und die Regeneration der Insassen in `ContainModules.cs` heilen weiter je Tick, `docs/RATE-INVARIANCE-PHYSICS.md`, `docs/RATE-INVARIANCE-FLIGHT.md`, `docs/RATE-INVARIANCE-CONSOLIDATION.md`, `docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md`, `docs/AI-OBSERVED-2026-10-08.md`, Commit `b05da71`, `81931ab`, `f05af4f`, `64f438f`, `2a433ba`, `df639ff` |

Geprüft und **kein** Faktor je Bild: `StructureCollapseData` `CollapseDamping` (Anteil der Schwerkraft, `gravity · (1 − damping)`; die Schwerkraft je Tick² folgt der Rate).

## 6. Liste für Phase 21 (Verbraucher außerhalb der Simulation)

> **Status nach Phase 21:** diese Liste ist abgearbeitet; der Stand je Nummer steht in §14.3 (Nr. 3 und Nr. 12 im Nachtrag §14.9 erledigt; unverändert nach Plan: Nr. 15). Die Tabelle beschreibt den Stand am Ende der Phase 20 und wird nicht fortgeschrieben.

Nach Phase 20 liest alles außerhalb die Rate aus der Welt bzw. vom Katalog. Was dort heute noch die 30 unterstellt oder eine Einheit falsch deutet:

| Nr. | Wo | Was | Was zu tun ist |
|---|---|---|---|
| 1 | `CnCNew.Game/Live/LiveGame.cs`, `tools/CnCNew.SimTool/PerfCommand.cs`, `tests/CnCNew.Presentation.Tests/ObjectTests.cs`, `tests/CnCNew.Simulation.Tests/Tests.Vision.cs` | `SimulationWorld.VisionIntervalTicks` (`public const int`, Alias von `VisionIntervalFrames`): bei 30 Hz richtig, bei 60 Hz die halbe Periode | `world.VisionInterval` benutzen, dann die Konstante entfernen |
| 2 | `CnCNew.Game/Data/GameData.cs` | `GameData.SessionRate` – die eine benannte Rate der Spielschicht (30 Hz); sie baut den Katalog und faltet die Rate in den Fingerabdruck | zur Einstellung der Sitzung machen; alles andere liest `data.Templates.Rate` |
| 3 | `CnCNew.Game/Save/SaveGame.cs` (Version 1) | schreibt und prüft **keine** Rate und zeigt den rohen Tick in der Ladeliste (W8). Der eingebettete Missionszustand trägt die Rate und wird beim Laden gegen den Katalog geprüft; die Hülle selbst nicht | Rate in die Hülle, Spielzeit statt Tick in der Liste |
| 4 | `CnCNew.Game`, `CnCNew.Presentation`, Werkzeuge: 14 + 1 + 3 Verwendungen der früheren Konstante heißen jetzt `SimulationRate.OriginalFramesPerSecond` | **nicht umgestellt, nur übersetzbar**: welche davon Simulationsticks und welche Originalbilder auf Wandzeit sind, ist nicht entschieden (W7) | je Stelle entscheiden: `world.Time` oder Originalbild auf Wandzeit |
| 5 | `CrateModules.cs` (`SabotageMilitaryFactoryCrateCollide.Sabotage`, `SabotagePowerPlantCrateCollide.Sabotage`), `LastBlockerModules.cs` (`SabotageInternetCenterCrateCollide.Sabotage`) | geben die Dauer der Sabotage als **Tickzahl im Ereigniswert** nach außen | der Leser rechnet mit `world.Time` |
| 6 | `CnCNew.Presentation` `UnitConditionTracker.TensileFreefallLife` | liest `TensileFormationUpdate.Life`, das Ticks zählt | mit `Time.OriginalFrames(100)` vergleichen |
| 7 | `MissionText`, Countdown-Anzeigen (`CnCNew.Game`, `CnCNew.Ui`) | `ScriptEngine.CounterValue(name)` eines Timers ist in **Ticks** (Zustand der VM; Stufe 1 skaliert beim Setzen); eine Anzeige, die den Wert als Bilder liest, zeigt bei 60 Hz das Doppelte | `world.Time.ToOriginalFrames` bzw. `ToWholeSeconds` |
| 8 | `CnCNew.Client.Stride/ClientRuntime.cs` | schreibt „presentation latency N ticks“ – `MissionOptions.PresentationLatencyTicks` sind Bilder des Originals | Text und Einheit |
| 9 | Verbraucher von `ScriptOutput` (`MissionPresentationDirector`) | `Arguments` sind die Parameter der Karte, nie skaliert: Bildzahlen (`CAMERA_FADE_*`, `CAMERA_BW_MODE_*`) sind Bilder des Originals; `ScriptOutput.Tick` ist ein Tick der Welt (§8.2) | nicht als Ticks deuten; Zeit aus `Tick` über `world.Time` |
| 10 | `CnCNew.Game/Live/LiveMission.cs` | `ClientPresentationLatencyTicks = 60 * OriginalFramesPerSecond` ist ohne Änderung bei jeder Rate 60 s (die Option ist in Bildern) | nur der Name ist historisch |
| 11 | Präsentation: `SpecialAbilityUpdate.StageTicksLeft`, `SpecialPowerModuleBase.AvailableOnFrame`, `AnimationSteeringUpdate.MinTransitionTicks`, `ProductionData.Door…Ticks`, `TechBuildingData.PulseRateTicks` | Werte der Simulation in Ticks der Rate (absolute Ticks bzw. Katalogdauern), die HUD und Modellzustände lesen | Anzeige und Animationsdauer über `world.Time` |
| 12 | Aufrufer von `MissionObjectiveDriver.Run`/`MissionProbe.Run` (`CnCNew.CampaignTool`, Tests) | `settleTicks:`/`waitTicks:`/`spacing:` sind Bilder des Originals; `maxTicks`/`--ticks` Ticks der gewählten Rate; `SkirmishAiStats.AttackTicks`, `MissionProbeResult.TicksToOutcome` sind Ticks | Ausgaben in Sekunden über `world.Time` |
| 13 | `tools/CnCNew.PresentationTool` (`ScenarioAudit`: `live.Advance(1.0 / 30 + 1e-6)`; `SyncPerfCommand`: `1.0 / OriginalFramesPerSecond`), `tools/CnCNew.UiRenderTool` (`1.0 / 30` in drei Dateien, `TransitionScenes.Frame`, Timerwert `30 * (11 * 60 + 55)` in `MissionTextScenes`) | Präsentationsschritte mit dem Literal 30; „ein Schritt = ein Tick“ wird bei 60 Hz still falsch (W9) | den Schritt aus der Rate ableiten, **bevor** die Rate umgestellt wird |
| 14 | Tests außerhalb der Simulation | 103 Vorkommen von `1.0 / 30` in 18 Dateien (Zahl des Inventars, W9) | wie Nr. 13 |
| 15 | `tools/CnCNew.SimTool/PerfCommand.cs` | Hochrechnungen `catchup30`/`catchup60`/`core30_percent` nennen ihre Rate im Namen und rechnen mit Literalen – richtig beschriftet, aber unabhängig von `--tick-rate` | bei der Umstellung des Standards (Phase 23) an die Rate des Laufs hängen |
| 16 | `CnCNew.Presentation/Input/StandingOrders`, `LiveGame.BeforeTick` (W5) | erzeugen Befehle im Ticktakt (ein Feuerbefehl je Tick); der Rhythmus ändert Ergebnisse und den Replay-Inhalt | Rhythmus in Bildern des Originals (wie `ObjectiveDriver.Commands.Act`, §5.4) |

## 7. Formate

- `WorldSnapshot`, `MissionSnapshot` und der Replay-Kopf tragen die Rate wie bisher (`tickRate`); bei 30 Hz sind die Bytes unverändert, deshalb **keine Versionserhöhung** in Phase 20 (`WorldSnapshot` 9, `MissionSnapshot` 2, Replay **11**, `CommandTypes.Version` **14** – so stehen die beiden letzten im Code; `CLAUDE.md` nennt noch 10 und 13).
- Wiederherstellen erzeugt die Welt **mit der gespeicherten Rate**. Abgelehnt wird (a) eine nicht unterstützte Rate (`SnapshotFormatException` bzw. `ReplayRejectedException`: „Unsupported tick rate 50 Hz“), (b) ein Katalog, der für eine andere Rate gebaut wurde („The snapshot was written at 60 Hz; the catalog was built for 30 Hz.“). Der Zustand enthält absolute Ticks und Werte je Tick; er ist nur mit seiner Rate lesbar.
- `ReplayRunner.Run(replay, world, …)` prüft, dass die Welt mit der Rate des Replays läuft (W8). `ReplayRunner.RateOf(header)` liefert die Rate eines Kopfs.
- Befehle (`SimCommand.Tick`, `SetTimerCommand.DelayTicks`/`RepeatTicks`) tragen Ticks der Rate der Welt; wer den Befehl erzeugt, rechnet in dieser Rate.
- `SaveGame` blieb in Phase 20 unberührt (§6 Nr. 3); seit dem Nachtrag der Phase 21 trägt die Hülle ihre Rate (**Version 2**, §14.9.3).

## 8. Einheiten

### 8.1 Öffentliche Werte der Sitzungen, Szenarien und der Skript-VM

| Wert | Einheit | Wo umgerechnet |
|---|---|---|
| `SkirmishMatchOptions.MaxTicks`, `.CheckpointInterval` | Ticks (Laufbudget, geht so in den Replay-Kopf) | – |
| `SkirmishMatchOptions.GraceTicks`, `.SciencePointInterval` | Bilder des Originals | `SkirmishMatchSession`-Konstruktor (`_graceTicks`, `_sciencePointInterval`) |
| `ReferenceMatchOptions.MaxTicks`, `.MinTicks`, `.CheckpointInterval` | Ticks (Laufbudget; `MinTicks` wird mit `MaxTicks` verglichen und füllt den Lauf auf) | – |
| `ReferenceMatchOptions.GraceTicks`, `.IncomeInterval` | Bilder des Originals | `ReferenceMatchScenario.Record` |
| `StressOptions.Ticks`, `.DrainTicks`, `.CheckpointInterval` | Schritte (Ticks); `DrainTicks` ist ein Anteil des Laufbudgets | – |
| `MissionOptions.MaxTicks` | Ticks (Laufbudget; im Missionszustand unter `maxTicks`) | – |
| `MissionOptions.PresentationLatencyTicks`, `ScriptEngineOptions.PresentationLatencyTicks` | Bilder des Originals; im Missionszustand steht unter `presentationLatencyTicks` der Wert der Option, wie er übergeben wurde | `ScriptEngine.Forward` (`_tick + Time.OriginalFrames(…)`) |
| `MissionOptions.CombatIntervalTicks` | nur ein Schalter (W8), keine Zeit | – |
| `ScriptEngine.EndGameDelayFrames` (vorher `EndGameDelayTicks`) | Bilder des Originals; `ScriptEngine.EndGameDelay` ist derselbe Wert in Ticks der Welt | Eigenschaft `EndGameDelay` |
| `MissionObjectiveDriver.Run(maxTicks, …)` | Ticks (Laufbudget) | – |
| `MissionObjectiveDriver.Run(spacing, settleTicks)`, `MissionProbe.Run(settleTicks, waitTicks)` | Bilder des Originals | in `Run` |
| `MissionProbeResult.TicksToOutcome`, `MissionRunReport.Ticks`/`.OutcomeTick`, `ObjectiveDriveResult.OutcomeTick` | Ticks | – |
| `SkirmishAiStats.AttackTicks` | Ticks (zählt je Denkschritt das skalierte Denkintervall) | – |
| `ScriptEngine.CounterValue(name)` eines Timers | **seit Phase 23:** Bilder des Originals bei jeder Rate (Zustand der VM, §15.3); von Stufe 1 bis dahin Ticks | – |
| `ScriptEngine.HulkLifetimeOverrideTicks` | Ticks (Zustand der VM; seit Stufe 1) | `SCRIPTING_OVERRIDE_HULK_LIFETIME` |
| `ScriptOutput.Tick` | Tick | – |
| `ScriptOutput.Arguments` | Parameter der Karte, unverändert (§8.2) | – |

Wer heute 300 als `GraceTicks` übergibt, bekommt bei 30 Hz dasselbe Verhalten und bei 60 Hz dieselbe Spielzeit. Werkzeuge, die einen solchen Wert mit einem Tickbudget verrechnen, rechnen das Budget zuerst in Bilder um (`ToolRate.GraceWithinBudget`, §9).

### 8.2 Was als Skriptausgabe die Simulation verlässt

`ScriptEngine.Forward` reicht 129 Aktionstypen als `ScriptOutput(Tick, Action, Arguments)` an den Host:

- `Arguments` sind die Parameter der Karte als Text, **unverändert und nie mit der Rate skaliert**. Eine Bildzahl darunter (`CAMERA_FADE_*`, `CAMERA_BW_MODE_*`: Bilder für Anstieg, Halten, Abfall; `ScriptEngine.cpp`, `ScriptEngine::setFade`, `m_fadeFramesIncrease/Hold/Decrease`) ist bei jeder Rate eine Zahl von **Bildern des Originals (1/30 s)**; Sekundenwerte sind Sekunden.
- `ScriptOutput.Tick` ist der Simulationstick der Rate der Welt. Wer daraus Zeit ableitet, rechnet mit `world.Time`.
- `PendingPresentation` läuft nach `PresentationLatencyTicks` **Bildern des Originals** ab.
- `VICTORY`/`DEFEAT`-Ausgaben: der Nachlauf bis `IsEnded` ist 120 Bilder (4 s) bei jeder Rate, bei `QUICKVICTORY` ein Update (seit Phase 23 ein Update der VM, also ein Bild des Originals).
- `CounterValue` eines Timers ist seit Phase 23 in **Bildern des Originals** (§15.3; davor in Ticks: §6 Nr. 7, §5.4 „Timerwerte“).

### 8.3 Stresstest: der Zeitplan zählt Schritte

`StressScenario` ist ein Belastungstest je Simulationsschritt: je Tick ein Kill und ein Ersatz, Funken jeden zweiten, Rauch jeden dritten Tick, Timer je neuem Objekt. Ein Zeitplan in Spielzeit würde die Last je Tick bei 60 Hz halbieren und etwas anderes messen; das Laufbudget (`Ticks`) ist ohnehin eine Schrittzahl. Deshalb bleiben Rampe (100), alle Modulo-Rhythmen, die Timerverzögerungen der Befehle (`SetTimerCommand(…, 30, 90, …)`, `(…, 120, 300, …)`, `(…, 45, 0, …)` – Ticks nach dem Vertrag des Befehls) und `DrainTicks` (Anteil des Budgets; `SimTool` kürzt ihn auf `Ticks / 4`) bei jeder Rate dieselben Zahlen. Die Rhythmen heißen `…Steps`. Folge bei 60 Hz: gegenüber den Lebensdauern der Fixture (Millisekunden, die skalieren) ist der Umsatz in Spielzeit doppelt so dicht; der Lauf ist bei 60 Hz ein anderer Lauf, kein Vergleichslauf zu 30 Hz. Einziger Spielzeitwert ist die Reserve vor dem Ende des Abbaus (`DrainReserveFrames = 60`). Bei 30 Hz ist der Lauf bitgleich (Manifestlauf `stress`). Der Test `TickRateStressScheduleCountsSteps` hält die Entscheidung fest.

## 9. Außerhalb der Simulation, Werkzeuge

Nur, was zum Übersetzen und für gleiches Verhalten nötig war: `GameData.SessionRate` baut den Katalog und faltet die Rate in den Fingerabdruck; `LiveScenario` nimmt `data.Templates.Rate`; Tests bauen ihre Kataloge und Welten mit `SimulationRate.Original`. Alles Weitere steht in §6.

`SimTool` und `CampaignTool` nehmen `--tick-rate N` (30, 60, 120; Standard 30) bei jedem Befehl, der eine Welt erzeugt; Tickbudgets (`--ticks`, `--max-ticks`) bleiben Ticks der gewählten Rate; Spielminuten in der Ausgabe kommen aus der Rate (`ToolRate.Minutes`). Die Manifestbefehle laufen immer mit der aufgezeichneten Rate (30 Hz) und lehnen eine andere ab.

Im Abschluss berichtigt (bei 30 Hz jeweils derselbe Wert bzw. dieselbe Ausgabe):

| Werkzeug | Stelle | War | Ist |
|---|---|---|---|
| `SimTool` | `skirmish-sweep` (`Program.cs`), Sweep des Referenzmanifests (`ReferenceManifest.cs`) | `GraceTicks = Math.Min(Standard.GraceTicks, maxTicks / 4)` – ein Minimum aus Bildern und Ticks | `ToolRate.GraceWithinBudget(graceFrames, maxTicks, rate)`: der Budgetanteil wird erst in Bilder umgerechnet (2 Stellen) |
| `SimTool` | `perf`, Allokationen | „MiB per game minute at 30 Hz“ mit `* 60 * 30` | `* ToolRate.Current.Time.TicksPerMinute`, beschriftet mit der Rate des Laufs |
| `SimTool` | `unit-audit` | Stichprobe `world.Tick % 15`, Meldung ab 60 Ticks | alle 15 Bilder, Meldung ab 60 Bildern (2 Stellen) |
| `CampaignTool` | `playthrough` | 150 Ticks Vorlauf vor dem Speichern | 150 Bilder (5 s) |
| `CampaignTool` | `drive --watch` | Hinweiszeile bei `world.Tick % 150 < 2` | `ToOriginalFrames(world.Tick) % 150 < 2` |

Geprüft und richtig: `StressOptions.DrainTicks = Math.Min(DrainTicks, Ticks / 4)` (beides Schritte); die Standardbudgets `--ticks 900/1800/9000`, `--max-ticks 3000/9000` und `Math.Min(ticks, 900)` in `CampaignTool audit` (Laufbudgets in Ticks der gewählten Rate – bei 60 Hz die halbe Spielzeit, wie dokumentiert); `ToolRate.Minutes` in `SimTool` und `CampaignTool`. Nicht geändert, weil Präsentation (Phase 21): §6 Nr. 13 bis 15.

Neu: `SimTool catalog-cost <content>` (Stufe 1), `SimTool rate-audit <content> --tick-rate N` und die Option `--time-probe` für `reference-match`, `ai-match` und `mission-run` (§11).

## 10. `SourceRules` und die Suchmuster des Inventars

`tests/CnCNew.Simulation.Tests/Tests.SourceRules.cs` (Test „Determinism rules hold in the simulation sources“) prüft `src/CnCNew.Simulation` zeilenweise (Zeichenketten geleert, Kommentare entfernt). Die Regeln der Phase 20:

| Regel | Trifft | Erlaubt nach Regel |
|---|---|---|
| fest einkodierte Tickrate (Stufe 1) | `LogicFramesPerSecond`; Umrechnungen der statischen Klasse `SimulationTiming` | – |
| Zahl als Tickrate | ein Feld mit `PerSecond`, `FramesPerSec`, `TicksPerSec`, `FrameRate`, `TickRate`, `Fps` oder `Hz` im Namen, das eine Zahl bekommt; 30 oder 900 als Faktor, Divisor oder Modulus (auch `Fixed.FromInt(30)`); `1/30`, `30/1000`, `1000/30`, `/ 33`; `FromTicksPerSecond(` mit Literal | – |
| tickbenannter Wert als Zahl | Konstante, Feld, lokale Variable, Eigenschaft oder Parameter mit `Tick`, `Frame`, `Interval`, `Delay`, `Period` oder `Cooldown` im Namen (auch klein geschrieben), der eine Zahl zugewiesen wird (`=`, Standardwert, `? … : zahl`) | 0 und −1 (ungesetzt, nie); ein Ausdruck mit `TickOrder.Next`; eine `const`, deren Name auf `Frames` endet; `MaxTicks`/`maxTicks` (Laufbudget) |
| nackte Zahl in Tickrechnung | `…Tick… + / − / % zahl`, `now ± zahl`, eine Tickdifferenz verglichen mit einer Zahl ≠ 0, `_life`/`_age`/`frame… % zahl` | ein Vielfaches von `TickOrder.Next` |
| nackte Zahl als Schlaf | `return zahl;` bzw. `return world.Tick + zahl;` mit Zahl ≠ 0 in `Modules/` und `Scripting/` | – |
| Dauer mit Untergrenze ein Tick | `Math.Max(…SleepNone…)`, `Math.Max(…Ticks, 1)`, `Math.Max(1, …Ticks…)` | – (eine Dauer benutzt `AtLeastOneFrame`) |
| Logikzufall über einen Zahlenbereich | `LogicRandom.Range(0|1, zahl)` | – (ein Zug über Dauern benutzt `DrawFrames` oder zieht in Bildern) |
| Konstante in Bildern erreicht den Zeitdienst | jede `const int/long …Frames`: in ihrem Typ (Datei bzw. die Dateien einer `partial`-Klasse; für `public` überall) muss eine Zeile sie in `OriginalFrames(…)`, als Ersatzwert von `ModuleConfigReader.Ticks(…)` oder in einer weiteren Konstante in Bildern verwenden | – |

Jede Ausnahme steht namentlich im Test, mit Datei, Zeilenteil und Grund; eine Ausnahme, die nichts mehr trifft, lässt den Test fehlschlagen. **34 Ausnahmen** (29 Zeilen, 5 Konstanten):

| Regel | Ausnahmen |
|---|---|
| Zahl als Tickrate (4) | `SimulationRate.OriginalFramesPerSecond = 30` (die eine benannte 30); `SimulationTiming.MillisecondsPerSecond = 1000` (keine Rate); `DockMachine.ClearanceTimeoutFrames = 30L * OriginalFramesPerSecond` (30 Sekunden mal Bilder je Sekunde); `FixedTrig` (`/ Fixed.FromInt(30)`, Glied der Sinusreihe) |
| tickbenannter Wert als Zahl (8) | `OriginalFramesPerSecond = 30`; `MissionOptions.CombatIntervalTicks = 3` (Schalter, Schlüssel des Missionszustands); `_blockerReplanTick = -1000` („nie geplant“, Standard im Snapshot); `TurretInfo.NeverScanTicks = 9999999` (Wächter des Originals, `TurretAIData::TurretAIData`); `MissionObjectiveDriver.Run(… spacing = 15, settleTicks = 60 …)` und `MissionProbe.Run(settleTicks = 150, waitTicks = 600)` (Bilder unter historischem Namen, in `Run` skaliert); `MaxUpdatesPerTick`, `MaxEventsPerTick` (Sturmgrenzen je Schritt) |
| nackte Zahl in Tickrechnung (1) | `MoodCheckOffset`: `(ulong)(tick + 1)` (Versatz der Hash-Eingabe, weder Zeit noch Reihenfolge) |
| Untergrenze ein Tick (12) | Differenzen absoluter Ticks bzw. „ein Aufwachen liegt in der Zukunft“: `AssistedTargetingUpdate.Update`, `SpyVisionUpdate.ActivateSpyVision`, `AutoDepositUpdate.OnSpawn`, `MinefieldBehavior.Update`, `FlammableUpdate.Update`, `WeaponBonusUpdate.Update`; bewusst ein Tick: `StealthDetectorUpdate.Update` (eine Untergrenze von einem Bild öffnete bei `DetectionRate 0` eine Lücke von einem Tick), `StealthUpdate.MarkDetected`; **auf der Liste für Phase 23**: `SupplyWarehouseCripplingBehavior.OnDamage`, `BaseRegenerateUpdate.OnDamage` (bei einer Frist 0 ein Tick statt ein Bild); keine Dauer: `SpecialAbilityUpdate` (`Math.Max(1, PreparationTicks)` schützt eine Division), `StressScenario` (Schrittzahl als Modulus) |
| Logikzufall über einen Zahlenbereich (4) | `Bodies.cs` (`Range(0, 99) < 25`, Prozent); `SlaveModules.cs` `RandomDirection` (`Range(0, 6)`, Richtung in Radiant) und `Range(0, 10)` (Zug des Originals für die Locomotor-Wahl, für den Strom behalten); `WorldEffectModules.cs` `Range(1, 100)` (Zug des Originals für den Splash) |
| Konstante in Bildern erreicht den Zeitdienst (5) | `RescanWaitMaxFrames` (`CleanupAndSpyModules.cs`) und `RescanMaxFrames` (`ProjectileModules.cs`): obere Grenze eines Zugs in Bildern, der gezogene Wert wird skaliert; `SkirmishAi` `SupplyLookRetryFrames`, `TunnelEvacuationRetryFrames`, `WarehouseScoutRetryFrames`: an `Backoff(key, frames)` übergeben, das skaliert |

**Von den Regeln gefunden** (gemessen: der neue Test über den Stand der zusammengeführten Stufe 2, `0efc7d8`): **fünf Stellen**, alle umgestellt bzw. entfernt – `FrameGrid.OneFrame = 1` (Hilfskonstante, entfällt), `HordeUpdate.OnSpawn` (`Math.Max(1, …)` im Zug von Hand, jetzt `DrawFrames`), `ProductionUpdate.Enqueue` (`Math.Max(totalTicks, 1)`), `ProductionUpdate.Update` (`RemainingTicks - 1`), `WeaponInfo.ScaledByRateOfFire` ohne Zeitdienst (`Math.Max(SleepNone, …)`). Zwei davon ändern Verhalten bei 60 Hz (Bauzeit 0, Schussabstand mit Bonus in `FireWeaponUpdate`/`BeginReload`), keine bei 30 Hz.

**Grenzen der Regeln:** es sind lexikalische Stolperdrähte. Eine Zahl als Positionsargument (`new Leaf(…, 3, 30, …)`, `new(…, 216_000, 300, 0, 1_000, …)` in den `Standard`-Optionen, `SetTimerCommand(…, 30, 90, …)`) sehen sie nicht – deren Einheiten stehen in §8. Zähler `++`/`--` je Update und Abfragen je Tick ohne Literal sind Verhalten (§5.4), keine Literale.

**Die Suchmuster aus `docs/TICKRATE-AUDIT.md` §8 über `src/CnCNew.Simulation`**, am Endstand gezählt (Trefferzeilen / Dateien, ohne Kommentarzeilen). „Regel“ heißt: jeder Treffer ist durch eine Regel oben erlaubt oder eine benannte Ausnahme; die übrigen Muster sind als Regel zu laut (das Inventar sagt das selbst für P04/N16) – ihre Treffer sind in Stufe 2 dateiweise gelesen und bewertet (Anhang B) und hier nach Art ausgezählt (mechanisch, mit denselben Unterscheidungen wie die Regeln).

| Muster | Treffer | Stand |
|---|---|---|
| P01 `LogicFramesPerSecond` | 0 | Regel |
| P02 Ratenfeld mit Zahl | 2 / 2 | Regel: beide benannte Ausnahmen (`OriginalFramesPerSecond`, `MillisecondsPerSecond`) |
| P03 `1/30`, `/ 30m`, `30/1000` … | 0 | Regel |
| P06 `long/int …Tick/Frames/Interval… = zahl` | 75 / 30 | Regel: 58 Konstanten in Bildern, 6 `…Steps` des Stresstests, 4 Wächter 0, 1 `TickOrder.Next`, 6 benannte Ausnahmen (`OriginalFramesPerSecond`, `NeverScanTicks`, die Parameter von `MissionObjectiveDriver.Run`/`MissionProbe.Run`, zwei Sturmgrenzen) |
| P07 `tick % zahl` | 0 | Regel |
| P08 `Tick ± … Vergleich mit Zahl`, `Tick ± zahl` | 5 / 5 | Regel: 2 `TickOrder.Next`, 2 über `OriginalFrames(…)`, 1 Katalogwert (`FireWeaponUpdate`, Vergleich `_clip == 0`) |
| P09 `return zahl;` | 28 / 15 | Regel für `Modules/` und `Scripting/` (kein Treffer mit Zahl ≠ 0); 23-mal `return 0;` (Geldbeträge, Flags, Zähler – kein Schlaf), 5 außerhalb der Module: Rangnummern in `ObjectTemplate.cs` (4), ein Ergebniswert in `NavigationGrid.cs` (1) |
| P13 `LogicRandom.Range(0|1, zahl)` | 4 / 3 | Regel: 4 benannte Ausnahmen |
| N05 `% zahl` bzw. `% …Interval/Frames…` | 14 / 6 | Regel (Literal): 2 `formation % 2` (Geometrie); 12 Rhythmen über ein im Konstruktor aufgelöstes Feld (`_thinkInterval`, `VisionInterval`, `_moveCadence` …) bzw. `% FireIntervalFrames` in Bildern |
| N08 `…Frames/Ticks/Interval/Delay… = zahl` | 79 / 35 | Regel: 58 Konstanten in Bildern, 12 Wächter 0/−1, 1 `TickOrder.Next`, 4 benannte Ausnahmen (`OriginalFramesPerSecond`, `NeverScanTicks`, die Parameter von `MissionObjectiveDriver.Run`/`MissionProbe.Run`), 4 Millisekundenwerte im INI-Text der `SyntheticFixture` (Daten, werden beim Katalogbau umgerechnet) |
| P04 `30`, `900`, `3600`, `1800`, `450` | 49 / 20 | 12 Konstanten in Bildern, 13 Abklingzeiten der Fahrerblätter (Bilder, in `Round` skaliert), 4 Stresstest, 11 Abstände/Radien/Mengen in Welteinheiten, 9 Kennnummern, Hash-Schiebungen (`z >> 30`), Reihenglied, `SimulationRate` selbst |
| N16 `15`, `45`, `60`, `90`, `120`, `150`, `300`, `600` | 81 / 29 | 24 Konstanten in Bildern, 20 Abklingzeiten der Fahrerblätter, 5 Stresstest, 26 Abstände/Radien/Mengen/Prozente, 4 Kennnummern, 1 Ersatzwert in Bildern, 1 über den Zeitdienst |
| P05 `const int/long … = …` | 198 / 60 | 64 Konstanten in Bildern, 8 `…Steps`, 1 `TickOrder.Next`, 2 benannte Schlafwerte/Wächter, 123 ohne Zeit (Größen, Anzahlen, Bitbreiten, Kennnummern, Versionen) |
| P10 `ModuleConfigReader.Ticks(…, ersatz ≠ 0)` (das Muster trifft auch mehrzeilige Aufrufe mit Ersatzwert 0) | 107 / 33 | Ersatzwerte sind Bilder des Originals (Vertrag des Lesers, Stufe 1); 15 mit Ersatzwert ≠ 0 |
| P11 `ModuleConfigReader.Int/Real(…, "…Time/Delay/Rate/Speed…")` | 25 / 15 | 10 über den Zeitdienst skaliert (Rohwerte je Bild, Stufe 1), 15 Rohwerte ohne Zeit bzw. Werte je Sekunde, die zur Laufzeit umgerechnet werden, und `CatchUpCrisisBailTime` (Zahl von Prüfungen) |
| P12 `*=`, `Speed = … * …Damp/Friction…` | 13 / 9 | 5 multiplikative Faktoren je Bild (§5.1: Zug ×3, `StructuralDecay`, `SpiralOrbitForwardSpeedDamping`), 1 Schubhalbierung der Neutronenrakete (§5.2), 7 Skalare ohne Zeit (Schaden, Sicht, Reichweite, Kosten) |
| N07 `Tick/now ± name` | 237 / 52 | 97 mit `SleepNone`/`SleepForever`, 44 über den Zeitdienst, 15 `TickOrder.Next`, 80 Tick ± Katalogdauer, Zustandsfeld oder aufgelöstes Feld der Welt, 1 Stresstest – kein nacktes Literal (Regel „Tickrechnung“) |
| N14 `LogicRandom.…(` | 63 / 25 | Züge über Dauern laufen über `DrawFrames` oder werden in Bildern gezogen und skaliert; übrig sind Anteile, Winkel, Abstände, Auswahlen – und `_bladeFlyOff` (§5.3) |

Kein Treffer der Regel-Muster ist unbewertet. Für die lauten Muster ist die Auszählung eine Sortierung nach Schreibweise, kein zweites Lesen; die Bewertung dahinter ist die der Stufe 2.

## 11. Probelauf bei 60 und 120 Hz

Nur „läuft durch, ist deterministisch, und was geht dabei verloren“ – das Verhalten bei diesen Raten ist ausdrücklich **nicht** bewertet (Phase 23), die Ergebnisse sind keine Referenzwerte. Stand: Endstand der Phase 20 (nach Stufe 2 und Zusammenführung). Release, Retaildaten, 6. Oktober 2026, bis zu sechs Läufe nebeneinander (Laufzeiten nicht ruhig).

### 11.1 Läufe

Jeder Befehl nimmt auf, lässt zwei frische Läufe gegen die Aufnahme laufen (Prüfpunkte, Tick-Hashes) und setzt aus einem Snapshot bzw. Speicherstand zur Hälfte fort. Tickbudgets sind auf dieselbe Spielzeit wie bei 30 Hz skaliert (`reference-match --max-ticks` 432.000 / 864.000 – bei 120 Hz zusätzlich 216.000 –, `ai-match --max-ticks` 90.000 / 180.000 für 45.000 bei 30 Hz, `mission-run --ticks` 6.000 / 12.000 für 3.000 bei 30 Hz). Die 30-Hz-Zeilen sind dieselben Läufe mit Messfühler; ihre Endhashes stehen im Referenzmanifest.

| Lauf | Rate | Ergebnis (Spielzeit) | Endhash | Lauf 2 gleich | Fortsetzung gleich |
|---|---|---|---|---|---|
| `reference-match` | 30 | Sieger FactionGLA bei Tick 14.490 (8,1 min) | `23B4B25DF62765D8` | ja | ja |
| `reference-match` | 60 | Sieger FactionGLA bei Tick 29.280 (8,1 min) | `EEBBD93BB03EBE8A` | ja | ja |
| `reference-match` (`--max-ticks 216000`) | 120 | kein Sieger in 216.000 Ticks (30 min), Spitze 252 lebende Objekte | `DE475CBB34080F25` | ja | ja |
| `reference-match` (`--max-ticks 864000`) | 120 | kein Sieger in 864.000 Ticks (120 min), Spitze 2.039 lebende Objekte | `F597B73A4DEB35C7` | nicht belegt | nicht belegt |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 30 | Sieger FactionChina bei Tick 23.370 (13,0 min) | `0BD4FE5EEB2C37E7` | ja | ja |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 60 | Sieger FactionChina bei Tick 23.430 (6,5 min) | `B05BD85E6644CF26` | ja | ja |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 120 | Sieger FactionChina bei Tick 45.900 (6,4 min) | `DF4127D263A690FB` | ja | ja |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 30 | Sieger FactionAmerica bei Tick 16.965 (9,4 min) | `BA10AC1EB0EB8AC9` | ja | ja |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 60 | Sieger FactionAmerica bei Tick 44.340 (12,3 min) | `EF1A3A9C2ED1CD96` | ja | ja |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 120 | Sieger FactionAmerica bei Tick 65.820 (9,1 min) | `49FBBF394F84B3F5` | ja | ja |
| `mission-run` `MD_USA05` | 30 | 3.000 Ticks, Ausgang offen, 0 fehlende Aktionen/Bedingungen | `1CB28E6AA923BDEF` | ja | ja |
| `mission-run` `MD_USA05` | 60 | 6.000 Ticks, Ausgang offen, 0 fehlende | `24230BF86F255B78` | ja | ja |
| `mission-run` `MD_USA05` | 120 | 12.000 Ticks, Ausgang offen, 0 fehlende | `40C0DB506E9C8E57` | ja | ja |
| `mission-run` `MD_GLA05` | 30 | 3.000 Ticks, Ausgang offen, 0 fehlende | `826CCD82C8536DC2` | ja | ja |
| `mission-run` `MD_GLA05` | 60 | 6.000 Ticks, Ausgang offen, 0 fehlende | `353037A43B41B0E2` | ja | ja |
| `mission-run` `MD_GLA05` | 120 | 12.000 Ticks, Ausgang offen, 0 fehlende | `A4385AFE59E38C1C` | ja | ja |
| `mission-run` `GC_ChemGeneral` | 30 | 3.000 Ticks, Ausgang offen, 0 fehlende | `E66023A1A7713080` | ja | ja |
| `mission-run` `GC_ChemGeneral` | 60 | 6.000 Ticks, Ausgang offen, 0 fehlende | `1DFDF910057F2353` | ja | ja |
| `mission-run` `GC_ChemGeneral` | 120 | 12.000 Ticks, Ausgang offen, 0 fehlende | `4F05AAFF1ADB080A` | ja | ja |

Keine Ausnahme, kein Überlauf (`checked`-Arithmetik des Zeitdiensts und von `Fixed`), keine Ablehnung wegen der Rate in einem der Läufe. Das Referenzmatch findet bei 120 Hz in zwei Stunden Spielzeit keinen Sieger (bei 30 und 60 Hz nach 8,1 Minuten); der Lauf mit dem vollen Budget (864.000 Ticks, Aufnahme 30 Minuten Rechenzeit, bis 2.039 lebende Objekte) wurde aufgenommen und von einem frischen Lauf an allen 864 Prüfpunkten bestätigt (Endhash in der Tabelle); der zweite frische Lauf mit Tick-Hashes und die Snapshot-Fortsetzung passten nicht in das Zeitlimit eines Hintergrundlaufs (je Durchgang rund 29 Minuten) und sind **nicht belegt**; vollständig geprüft ist der Lauf mit 216.000 Ticks (30 Spielminuten, das 3,7-Fache der Spielzeit des 30-Hz-Laufs).

### 11.2 Rundung beim Katalogbau (`SimTool rate-audit <content> --tick-rate N`)

Zwei Sichten auf denselben Verlust. **Je Umrechnung:** der Messfühler sieht beim Bau den exakten Dezimalwert jeder Umrechnung auf `Fixed` (Q47.16) und zählt, was null wird oder mehr als 1 % bzw. 10 % danebenliegt.

| Funktion | Rate | Aufrufe | ≠ 0 | → 0 | > 1 % | > 10 % | schlechtester Fall (exakter Wert) |
|---|---|---|---|---|---|---|---|
| `DegreesPerSecondToRadiansPerTick` | 30 | 645 | 616 | 0 | 1 | 0 | 4,912 % (5,82 · 10⁻⁵) |
| `DegreesPerSecondToRadiansPerTick` | 60 | 645 | 616 | 0 | 1 | 0 | 4,912 % (2,91 · 10⁻⁵) |
| `DegreesPerSecondToRadiansPerTick` | 120 | 645 | 616 | 0 | 2 | 0 | 4,912 % (1,45 · 10⁻⁵) |
| `DurationMillisecondsToFixedTicks` | 30 / 60 / 120 | 173 | 173 | 0 | 0 | 0 | 0,001 % / 0,000 % / 0,000 % |
| `PerSecondSquaredToPerTickSquared` | 30 | 805 | 529 | 0 | 1 | 0 | 3,870 % (1,11 · 10⁻⁴) |
| `PerSecondSquaredToPerTickSquared` | 60 | 805 | 529 | 0 | 4 | 0 | 9,863 % (2,78 · 10⁻⁵) |
| `PerSecondSquaredToPerTickSquared` | 120 | 805 | 529 | **1** | 36 | 1 | 100 % (6,94 · 10⁻⁶) |
| `PerSecondToPerTick` | 30 | 989 | 673 | 0 | 0 | 0 | 0,708 % (3,33 · 10⁻⁴) |
| `PerSecondToPerTick` | 60 | 989 | 673 | 0 | 0 | 0 | 0,708 % (1,67 · 10⁻⁴) |
| `PerSecondToPerTick` | 120 | 989 | 673 | 0 | 1 | 0 | 8,447 % (8,33 · 10⁻⁵) |

Die eine Umrechnung, die bei 120 Hz **ganz auf null** rundet, ist `Acceleration = 0.1` (je s²) des `NapalmBombLocomotor` (`Locomotor.ini`): 0,1 / 14.400 liegt unter einem halben `Fixed`-Schritt. Kein Objekt der Retaildaten benutzt diesen Locomotor (nur die Definition nennt ihn), deshalb erscheint der Wert nicht im Katalogvergleich unten. Derselbe Locomotor liefert die 8,447 % (`Speed = 0.1` je s). Bei 60 Hz rundet nichts auf null.

**Je Feld:** der 30-Hz-Katalog und der Katalog der Rate werden Wert für Wert verglichen (62.648 Festkommawerte, 83.166 ganze Zahlen; 2.410 Festkommawerte und 12.766 ganze Zahlen unterscheiden sich – bei beiden Raten dieselben). Ein Festkommawert, der sich unterscheidet, folgt einem Faktor (je Tick, je Tick², Dauer); er wird mit dem exakt skalierten 30-Hz-Wert verglichen. Dieser Bezug ist selbst ein Festkommawert: der Fehler misst, wo die höhere Rate gröber ist als 30 Hz es schon ist, nicht den Fehler gegen die Daten.

| | 60 Hz | 120 Hz |
|---|---|---|
| geänderte Festkommawerte nach Faktor | je Tick (÷ 2) 1.634, je Tick² (÷ 4) 603, Dauer (× 2) 173 | je Tick (÷ 4) 1.634, je Tick² (÷ 16) 603, Dauer (× 4) 173 |
| bei 30 Hz ≠ 0, bei der Rate 0 | **0** | **0** |
| relativer Fehler über 10 % | 0 | 0 |
| relativer Fehler über 1 % bis 10 % | 1 Wert in 1 Feld: `LocomotorInfo.Braking` 1 (`FireWallSegment`: roh 73 → 18, 1,37 %) | 41 Werte in 5 Feldern: `LocomotorInfo.Braking` 23 (schlechtester `FireWallSegment`: roh 73 → 5, 9,59 %), `Acceleration` 12 (`AFG_AmericaVehicleChinook`: roh 583 → 36, 1,20 %), `Lift` 4 (`AirF_AmericaCrateParachute`: roh 728 → 46, 1,10 %), `TurnRatePerTick` 1 (`FireWallSegment`: roh 38 → 10, 5,26 %), `WeaponInfo.WeaponSpeed` 1 (`NeutronMineWeapon`: roh 22 → 5, 9,09 %) |
| unter acht signifikanten Bits (Rohwert unter 256, ≠ 0) | 46 Werte in 5 Feldern: `LocomotorInfo.Braking` 23, `Acceleration` 12, `TurnRatePerTick` 6, `Lift` 4, `WeaponInfo.WeaponSpeed` 1 | 321 Werte in 7 Feldern: `TurretInfo.PitchRate` 105, `LocomotorInfo.Braking` 102, `Acceleration` 88, `TurnRatePerTick` 17, `Lift` 4, `TurretInfo.TurnRate` 4, `WeaponInfo.WeaponSpeed` 1 |
| ganze Zahlen, die sich unterscheiden, aber nicht das 2- bzw. 4-Fache des 30-Hz-Werts sind (Dauer abseits des Rasters) | **0** | **0** |

Das ist der Befund E8 des Plans in Zahlen: Größen je Tick² verlieren bei jeder Verdoppelung der Rate zwei Bits. Die letzte Zeile ist der Beleg der Rasterregel am ganzen Katalog: jede der 12.766 geänderten ganzen Zahlen ist exakt das Vielfache.

### 11.3 Zähler zur Laufzeit (`--time-probe`, erster Lauf jedes Befehls)

Aufrufe / Treffer je Zähler. `PerSecondToPerTick` usw. sind die Umrechnungen aus Zustand (Heil- und Schadensmengen je Tick); „Treffer“ heißt: Ergebnis null bei Eingabe ≠ 0.

| Lauf | Rate | `RunTimeRoundedToZero` (Aufrufe / Treffer) | `MinimumOneFrame` (`AtLeastOneFrame`: Aufrufe / gegriffen) | `OffGrid` (`ToOriginalFrames` + `CountdownReload` + `DrawFrames`: Aufrufe / Treffer) |
|---|---|---|---|---|
| `reference-match` | 30 | 60.496 / 0 | 4.167 / 0 | 46.058 / 0 |
| `reference-match` | 60 | 108.081 / 0 | 4.574 / 0 | 70.937 / 0 |
| `reference-match` (216.000 Ticks) | 120 | 785.308 / 0 | 15.477 / 0 | 321.379 / 0 |
| `reference-match` (864.000 Ticks) | 120 | 5.888.716 / 0 | 18.630 / 0 | 20.132.858 / 0 |
| `ai-match` China/GLA | 30 | 35.601 / 0 | 1.627 / 0 | 8.226 / 0 |
| `ai-match` China/GLA | 60 | 18.696 / 0 | 419 / 0 | 2.496 / 0 |
| `ai-match` China/GLA | 120 | 48.278 / 0 | 622 / 0 | 3.499 / 0 |
| `ai-match` America/China | 30 | 3.336 / 0 | 1.009 / 6 | 14.278 / 0 |
| `ai-match` America/China | 60 | 16.023 / 0 | 1.597 / 6 | 24.678 / 0 |
| `ai-match` America/China | 120 | 23.199 / 0 | 789 / 3 | 16.934 / 0 |
| `MD_USA05` | 30 | 2.400 / 0 | 214 / 0 | 45.453 / 0 |
| `MD_USA05` | 60 | 4.812 / 0 | 214 / 0 | 45.468 / 0 |
| `MD_USA05` | 120 | 9.610 / 0 | 214 / 0 | 45.469 / 0 |
| `MD_GLA05` | 30 | 24.410 / 0 | 258 / 11 | 47.323 / 0 |
| `MD_GLA05` | 60 | 30.759 / 0 | 258 / 11 | 47.318 / 0 |
| `MD_GLA05` | 120 | 37.033 / 0 | 257 / 11 | 47.337 / 0 |
| `GC_ChemGeneral` | 30 | 0 / 0 | 2.428 / 13 | 66.261 / 0 |
| `GC_ChemGeneral` | 60 | 0 / 0 | 2.428 / 13 | 66.283 / 0 |
| `GC_ChemGeneral` | 120 | 0 / 0 | 2.428 / 13 | 66.283 / 0 |

- **Rundung auf null zur Laufzeit: keine** – in keinem Lauf, bei keiner Rate, bei bis zu 5.888.716 Umrechnungen je Lauf. Gezählt sind die Umrechnungen über den Zeitdienst (`PerSecondToPerTick(Fixed)`, `SharePerTick`, `PerOriginalFrameToPerTick(Fixed)`); **nicht gezählt** ist, was Module ohne den Zeitdienst je Tick bilden (Produkte aus einer Katalograte je Tick und einem Zustandswert, etwa Drehung · Faktor) – dafür gibt es keinen Zähler.
- **Die Untergrenze „ein Bild“ greift selten und bei jeder Rate gleich oft** (dieselben Stellen, dieselbe Zahl in den Missionen): `AutoHealBehavior.OnSpawn`/`TryActivate` (`StartHealingDelay` 0; `Phase5Modules.cs`), `StealthUpdate.Update` (`StealthDelay` 0), `SlowDeathBehavior.BeginSlowDeath` (Zerstörungsverzögerung 0; `Phase5BlockerModules.cs`), `JetAIUpdate.BeginReload` (Nachladezeit 0). In jedem Fall wird eine Dauer von **0** auf ein Bild gehoben; ein positiver Wert unter einem Bild (ein Wert abseits des Rasters) kam nicht vor.
- **Abseits des Rasters: 0 Treffer** bei allen Dauern, die durch `ToOriginalFrames`, `CountdownReload` und `DrawFrames` gingen. Die Annahme hinter dem zusammengeführten Zählerhelfer (§2) hält in diesen Läufen.
- `FloorToFrame`/`CeilingToFrame` (Blaststufen der Neutronenrakete, Wendekreis der Lieferflugzeuge) wurden in keinem der Läufe aufgerufen.

### 11.4 Beobachtungen (Eingabe für Phase 22 und 23)

- **Die Ergebnisse sind nicht mit 30 Hz vergleichbar und wurden nicht bewertet.** Zur Einordnung der Liste in §5: die Spielzeit bis zum Sieg weicht je Lauf verschieden ab (Referenzmatch 8,1 / 8,1 min bei 30 / 60 Hz, bei 120 Hz kein Sieger in 120 min und achtmal so viele lebende Objekte; China gegen GLA 13,0 / 6,5 / 6,4 min; America gegen China 9,4 / 12,3 / 9,1 min) – nach Stufe 2 sind Fristen und Rhythmen gleich, die Integration (§5.2) und die Abtastung (§5.4) nicht.
- **Zähler je Bild halten ihren Takt:** `CountdownReload` wird in `MD_USA05` bei 30, 60 und 120 Hz gleich oft aufgerufen (44.985 / 45.000 / 45.000 in derselben Spielzeit), in `GC_ChemGeneral` 65.978 / 66.000 / 66.000 – Zähler mit Periode ein Bild laufen bei jeder Rate einmal je Bild, nicht einmal je Tick.
- **Dezimalrechnung zur Laufzeit:** in `MD_GLA05` ruft die Simulation `PerSecondSquaredToPerTickSquared(decimal)` 72-, 142- bzw. 278-mal im Lauf – das ist `TemplateCatalog.GravityPerTickSquared`, das bei jedem Zugriff eines Moduls rechnet (§4); die Zahl wächst mit der Tickzahl. Kein Fehler, aber eine Dezimaldivision in einem Laufzeitpfad; ein vorgehaltener Wert an der Welt (wie `_significantHeight`) nimmt sie heraus.
- **`PerOriginalFrameToPerTick(Fixed)` an der Verwendung** kostet bei 60/120 Hz eine `Fixed`-Division je Aufruf (bei 30 Hz keine): 6.348 bzw. 12.623 Aufrufe in `MD_GLA05` (Brückengerüst, Jets), 12 bzw. 10 in `MD_USA05`.
- **Grobe Werte** (§11.2): bei 120 Hz haben 321 Katalogwerte weniger als acht signifikante Bits, bei 60 Hz 46; null wird im benutzten Katalog keiner. Für Phase 22 (Q39.24) sind die Felder `LocomotorInfo.Braking`/`Acceleration`/`Lift`/`TurnRatePerTick`, `TurretInfo.PitchRate`/`TurnRate` und `WeaponInfo.WeaponSpeed` die Messlatte.
- **Verletzte Annahmen, die beim Lesen auffielen** (alle in §5.3): ein Schlaf von einem Tick, der ein Bild war (`RunUpdates`, jedes Modul mit `return SleepNone`); „halten bis einschließlich Frist“ (N + 1 Schritte: der Zusatzschritt ist ein Tick); der Verkauf (180 Bilder weniger ein Tick); der erste Zähler nach einem Befehl von außen (Versatz unter einem Bild); `LeafletDropBehavior` (erstes Update einen Tick nach dem Entstehen, ein `Delay` von einem Bild ist dann noch nicht abgelaufen).

### 11.5 Nachtrag Phase 22: dieselben Zähler bei 24 Nachkommabits (6. Oktober 2026, Stand `d7fca91`)

`Fixed` hat seit Phase 22 24 statt 16 Nachkommabits (`docs/FIXED-POINT-Q39-24.md`). Die Zähler aus §11.2 und §11.3 wurden am Stand der Umstellung neu erhoben; die 16-Bit-Zahlen wurden am Stand unmittelbar davor
(`a0e7b23`) noch einmal gemessen und stimmen mit §11.2 überein. Wie in §11 gilt: das Verhalten bei 60 und 120 Hz ist nicht bewertet, die Ergebnisse sind keine Referenzwerte.

**Katalogbau (`SimTool rate-audit <content> --tick-rate N`):**

| Zähler | 60 Hz, 16 Bit | 60 Hz, 24 Bit | 120 Hz, 16 Bit | 120 Hz, 24 Bit |
|---|---|---|---|---|
| Umrechnungen, die auf null runden | 0 | **0** | 1 | **0** |
| Umrechnungen über 1 % / über 10 % Fehler (Summe der vier Funktionen) | 5 / 0 | **0 / 0** | 39 / 1 | **0 / 0** |
| schlechtester Fehler einer Umrechnung | 9,863 % | 0,007 % | 100 % | 0,422 % (`Acceleration = 0.1` des `NapalmBombLocomotor`: 117 Rohschritte statt 0) |
| Katalogwerte: bei 30 Hz ≠ 0, bei der Rate 0 | 0 | 0 | 0 | 0 |
| Katalogwerte über 1 % bis 10 % gegen den exakt skalierten 30-Hz-Wert | 1 | **0** | 41 in 5 Feldern | **0** |
| Katalogwerte über 10 % | 0 | 0 | 0 | 0 |
| Katalogwerte unter acht signifikanten Bits (Rohwert unter 256, ≠ 0) | 46 in 5 Feldern | **0** | 321 in 7 Feldern | **0** |
| ganze Zahlen abseits des Rasters | 0 | 0 | 0 | 0 |

Bei 30 Hz selbst lagen bei 16 Bit zwei Umrechnungen über 1 % (4,912 % und 3,870 %); bei 24 Bit ist der schlechteste Fall 0,007 %. Verglichen wurden dieselben 62.648 Festkommawerte und 83.166 ganzen Zahlen;
2.410 Festkommawerte und 12.766 ganze Zahlen unterscheiden sich zwischen den Raten wie zuvor (je Tick 1.634, je Tick² 603, Dauer 173).

**Laufzeit (`--time-probe`, erster Lauf jedes Befehls, 24 Bit).** Aufrufe / Treffer wie in §11.3. Das Referenzmatch läuft seit Phase 22 mit dem Treiber-Startwert `5EED0007` und wurde hier nur aufgenommen
(`--record-only`, Budgets 90.000 bzw. 180.000 Ticks); die übrigen Befehle mit ihren Prüfläufen (zweiter frischer Lauf, Fortsetzung aus Snapshot bzw. Speicherstand).

| Lauf | Rate | Ergebnis (Spielzeit) | `RunTimeRoundedToZero` | `MinimumOneFrame` | `OffGrid` | Endhash | Lauf 2 / Fortsetzung gleich |
|---|---|---|---|---|---|---|---|
| `reference-match` | 60 | Sieger FactionGLA bei Tick 28.590 (7,9 min) | 75.552 / 0 | 6.087 / 0 | 46.594 / 0 | nicht geprüft | nicht geprüft |
| `reference-match` | 120 | Sieger FactionGLA bei Tick 112.920 (15,7 min) | 379.904 / 0 | 7.956 / 0 | 66.884 / 0 | nicht geprüft | nicht geprüft |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 60 | Sieger FactionChina bei Tick 23.220 (6,5 min) | 13.090 / 0 | 477 / 0 | 2.540 / 0 | `17F89955311BDDAE` | ja / ja |
| `ai-match` Alpine Assault, FactionChina/FactionGLA | 120 | Sieger FactionChina bei Tick 49.440 (6,9 min) | 21.259 / 0 | 505 / 0 | 2.667 / 0 | `BFF1C33F041ACDF8` | ja / ja |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 60 | Sieger FactionAmerica bei Tick 24.240 (6,7 min) | 2.771 / 0 | 389 / 0 | 9.206 / 0 | `52E95115272B9463` | ja / ja |
| `ai-match` Alpine Assault, FactionAmerica/FactionChina | 120 | Sieger FactionAmerica bei Tick 48.240 (6,7 min) | 4.845 / 0 | 362 / 0 | 8.868 / 0 | `20870C5115C41ABC` | ja / ja |
| `mission-run` `MD_USA05` | 60 | 6.000 Ticks, Ausgang offen | 4.812 / 0 | 214 / 0 | 45.468 / 0 | `199D42C8B0B88DF6` | ja / ja |
| `mission-run` `MD_USA05` | 120 | 12.000 Ticks, Ausgang offen | 9.610 / 0 | 214 / 0 | 45.469 / 0 | `F036DAAF9DEEC606` | ja / ja |
| `mission-run` `MD_GLA05` | 60 | 6.000 Ticks, Ausgang offen | 30.759 / 0 | 257 / 11 | 47.331 / 0 | `0992106EBA5D865A` | ja / ja |
| `mission-run` `MD_GLA05` | 120 | 12.000 Ticks, Ausgang offen | 37.033 / 0 | 253 / 11 | 47.386 / 0 | `66BC9427E265652E` | ja / ja |
| `mission-run` `GC_ChemGeneral` | 60 | 6.000 Ticks, Ausgang offen | 0 / 0 | 2.428 / 13 | 66.283 / 0 | `3ECA1E1988852F4C` | ja / ja |
| `mission-run` `GC_ChemGeneral` | 120 | 12.000 Ticks, Ausgang offen | 0 / 0 | 2.428 / 13 | 66.283 / 0 | `A4C732DFBC83F4AF` | ja / ja |

- **Rundung auf null zur Laufzeit: weiterhin keine**, bei 60 und bei 120 Hz. Das Ziel der Phase 22 – null Fälle bei 60 Hz – ist beim Katalogbau und zur Laufzeit erreicht; es bleibt kein Fall zu benennen. Wie in
  §11.3 nicht gezählt: Beträge je Tick, die Module ohne den Zeitdienst bilden.
- Die Zähler der Missionen sind die von §11.3 (dieselben Stellen, dieselben Zahlen bis auf einzelne Aufrufe): die Auflösung ändert nicht, wie oft die Untergrenze „ein Bild“ greift.
- Kein Überlauf, keine Ausnahme, keine Ablehnung in einem der zwölf Läufe.
- **Beobachtung für Phase 23, nicht bewertet:** bei 24 Bit endet das Referenzmatch auch bei 120 Hz (bei 16 Bit fand es dort in 864.000 Ticks keinen Sieger, mit anderem Startwert), und die Spielzeit bis zum Sieg
  der beiden KI-Läufe liegt bei 60 und 120 Hz nah beieinander (China/GLA 6,5 und 6,9 min; America/China 6,7 und 6,7 min; bei 16 Bit 6,5 / 6,4 und 12,3 / 9,1 min). Bei 30 Hz und 24 Bit sind es 6,0 und 9,3 min
  (Ticks 10.710 und 16.665, Referenzmanifest).

## 12. Tests

`tests/CnCNew.Simulation.Tests`, alle bei 30, 60 und 120 Hz auf der synthetischen Fixture der jeweiligen Rate, wo nicht anders gesagt. Die bestehenden Tests mit Tickerwartungen bei 30 Hz (179 Ticks Verkauf, 45 Ticks Interesse, 18/9/4 Ticks Schussabstand …) laufen unverändert und sind der 30-Hz-Beleg auf Testebene.

| Datei | Tests | Belegt |
|---|---|---|
| `Tests.TickRate.cs` (Stufe 1) | 8 | Zeitdienst gegen die früheren Formeln (30 Hz), Raster und Skalierung (60/120 Hz); unterstützte Raten, Fingerabdruck; Katalog je Rate; Welt lehnt fremden Katalog ab; Snapshot, Replay und Missionszustand tragen ihre Rate (50 Hz und fremder Katalog abgelehnt) |
| `Tests.TickRate.World.cs` | 7 | Verkauf 179/359/719 Ticks; Sichtrhythmus 3/6/12 mit Phase 0; Schussabstand, Nachladen und Aufziehzeit jeder Waffe unter drei Bonuszuständen genau 2× bzw. 4× (18 Bilder bei 400 % sind 4 Bilder); Feuerstufen durch die Welt; Flugzeit in Bildern; Turmfristen und Leerlauf-Scan auf dem Raster; erster Blick zwei Bilder nach dem Leerlauf |
| `Tests.TickRate.ModulesAM.cs` | 4 | `BaseRegenerateUpdate` (Schübe alle drei Bilder, 12,5 Punkte je Schub); Lebensdauern und `AutoDepositUpdate` (0 ms = ein Bild); Hacker-Zähler (Periode 16 Bilder, Versatz des ersten Zählers unter einem Bild); `TensileFormationUpdate` (Prüfung bei Bild 120, Rutschphase 300 Bilder) |
| `Tests.TickRate.ModulesNZ.cs` | 6 | Detektor-Staffelung (`DrawFrames`); Gift ohne Intervall und Selbstheilung ohne Verzögerung (Untergrenze ein Bild); Feind-in-der-Nähe-Scan (`CountdownReload`); Varianz des langsamen Tods; `AIUpdateData.MoodCheckTicks` = 60 Bilder je Rate |
| `Tests.TickRate.Scripting.cs` | 7 | `VICTORY` nach 120 Bildern, `QUICKVICTORY` nach einem Update; Präsentationslatenz in Bildern, Argumente unverändert; Sonde; Regeln der Skirmish-Sitzung; Denkrhythmus der KI; Rhythmen des Referenzmatch-Generators; Stresstest zählt Schritte |
| `Tests.TickRate.Grid.cs` (Abschluss) | 2 | der zusammengeführte Rasterhelfer gegen beide früheren (§2); der Messfühler beobachtet nur und zählt richtig |
| `Tests.SourceRules.cs` / `Tests.State.cs` (`SourceRules`) | 1 | die Regeln aus §10 über den Quelltext, die Konstanten in Bildern, und die Regeln selbst (23 Zeilen, die sie fangen müssen, 22, die sie durchlassen müssen) |

Nicht durch einen eigenen Ratentest belegt (nur durch die unveränderten 30-Hz-Tests, die Manifeste und die Probeläufe): die Neuplanung in `AIUpdateInterface.Move`, die Schweißzeiten von `SlavedUpdate`, `MobMemberSlavedUpdate`, `PointDefenseLaserUpdate`, `BattleBusSlowDeathBehavior`, die Kippwerte, `CheckpointUpdate.GateStep`, `FireOCLAfterWeaponCooldownUpdate`, die einzelnen Fristen von `SkirmishAi` (nur über den Rhythmus), der Neuziel- und der Übernahmerhythmus der Skripte, die Abklingzeiten des Zielfahrers, `ProductionUpdate.Enqueue` mit Bauzeit 0.

## 13. Fallstricke

- `SimulationTiming.cs`, `SyntheticFixture.cs`, `StressScenario.cs`, `SageRandom.cs` und `Tests.World.cs` haben CRLF; `git ls-files --eol <datei>` vor jedem Skriptlauf.
- Der Name `time` kollidiert in den Katalogbauern mit Mustervariablen (`is GameTime time`); der Parameter heißt dort `timing`.
- `SimulationWorld` hat eine Eigenschaft `Time`; in Dateien der Welt meint `Time.X` den Zeitdienst, nicht den Namensraum.
- Eine `record`-Standardbelegung mit einer Tick-Konstante (`long MoodCheckTicks = …`) ist ein stiller Tickwert; `SourceRules` fängt sie jetzt.
- Statische Felder einer `partial`-Klasse werden in Textreihenfolge der Datei initialisiert: die Regeltabellen in `Tests.SourceRules.cs` stehen vor den Tabellen, die sie einbetten.
- Der Messfühler hängt am Zeitdienst der **Rate**, nicht an einer Welt: zwei Läufe derselben Rate im selben Prozess zählen in denselben Messfühler. Die Werkzeuge hängen ihn nur für den ersten Lauf an.
- Nach jeder Quelländerung `dotnet build -c Release tools/CnCNew.SimTool` und aus einer Kopie des `bin`-Verzeichnisses laufen lassen; `--no-build` zeigt sonst den alten Stand.

## Anhang A: umgestellte Stellen der Stufe 2 je Symbol

Spalte „Ist“ in der heutigen Schreibweise (nach der Zusammenführung der Helfer). Referenzpfade der Module relativ zu `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/Object/`, sonst wie angegeben; „eigen“ heißt: keine Herkunft im Original.

### A.1 Welt, Kataloge, Replay (27)

| Datei | Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|---|
| `World/SimulationWorld.Acquisition.cs` | `OrderedAttackGraceTicks` | B | `const long … = 45` (Ticks) | `OrderedAttackGraceFrames = 45`, aufgelöst in `_orderedAttackGraceTicks` | eigen |
| `World/SimulationWorld.Acquisition.cs` | `ForceIdleTicks` | B | `const long … = 2` | `ForceIdleFrames = 2`, `_forceIdleTicks` (zwei Verwendungen) | `GameLogic/AI/AI.cpp`, Feldtabelle `ForceIdleMSEC` → `m_forceIdleFramesCount` (`INI::parseDurationUnsignedInt`); benutzt in `AIUpdate.cpp`, `AIUpdateInterface::resetNextMoodCheckTime`. Der Wert 67 ms der ausgelieferten `AIData.ini` ist unter `references` nicht belegt; nach der Rundung des Originals wären 67 ms drei Bilder, nicht zwei (Nebenbefund des Inventars, nicht korrigiert) |
| `World/SimulationWorld.Acquisition.cs` | `ChaseRefreshTicks` | C | `const long … = 15`, `tick - ChaseTick >= 15` | `ChaseRefreshFrames = 15`, `_chaseRefreshTicks` | eigen |
| `World/SimulationWorld.Acquisition.cs` | `StepAcquirer` (Marsch fortsetzen) | W2 | `WakeAt(…, tick + 1)` | `tick + TickOrder.Next` | eigen (Reihenfolge) |
| `World/SimulationWorld.Acquisition.cs` | `Engage` (Verfolgung) | W2 | `WakeAt(…, tick + 1)` | `tick + TickOrder.Next` | eigen (Reihenfolge) |
| `World/SimulationWorld.Acquisition.cs` | `EarliestReadyTick` | W2 | `tick - OrderedAttackGraceTicks - 1` | `tick - _orderedAttackGraceTicks - TickOrder.Next` | eigen (ein Schritt unter der Frist, damit der Vergleich `<=` sicher endet) |
| `World/SimulationWorld.Acquisition.cs` | `MoodCheckOffset` | W1 | Versatz in `±(rate >> 1)` Ticks | in Bildern gezogen (`ToOriginalFrames(rate) >> 1`), dann `OriginalFrames(…)` | `AIUpdate.cpp`, `AIUpdateInterface::getNextMoodTarget` (`halfRate = checkRate >> 1`, `GameLogicRandomValue(-halfRate, halfRate)`) |
| `World/SimulationWorld.Turrets.cs` | `TurretAimGraceTicks` | B | `const long … = 15` | `TurretAimGraceFrames = 15`, `_turretAimGraceTicks` (zwei Verwendungen) | eigen |
| `World/SimulationWorld.Turrets.cs` | `TurretReloadHoldCapTicks` | B | `const long … = 450` | `TurretReloadHoldCapFrames = 15L * SimulationRate.OriginalFramesPerSecond`, `_turretReloadHoldCapTicks` (auch als Klammergrenze in `EarliestReadyTick`) | eigen |
| `World/SimulationWorld.Turrets.cs` | `TurretSweepEnableTicks` | A | `const long … = 3` | `TurretSweepEnableFrames = 3`, `_turretSweepEnableTicks` | `GameLogic/AI/TurretAI.cpp`, lokale Konstante `ENABLE_SWEEP_FRAME_COUNT = 3` (`m_enableSweepUntil = now + …`) |
| `World/SimulationWorld.Turrets.cs` | `TurretLoseInterestTicks` | B | `const long … = 45` | `TurretLoseInterestFrames = 45`, `_turretLoseInterestTicks` | eigen |
| `World/SimulationWorld.Turrets.cs` | `ScheduleScan` | W1 | `DrawScan(…, MinScanIntervalTicks, MaxScanIntervalTicks)` (Ziehung über Ticks) | Ziehung über `ToOriginalFrames(min)…ToOriginalFrames(max)`, dann `OriginalFrames(…)` | `TurretAI.cpp`, `TurretAIIdleState::resetIdleScan` (`GameLogicRandomValue(getMinIdleScanInterval(), getMaxIdleScanInterval())`) |
| `World/SimulationWorld.Vision.cs`, `World/SimulationWorld.cs` | `VisionIntervalTicks`, `Step`, `StepObserved` | C | `const int … = 3`, `tick % VisionIntervalTicks == 0` | `VisionIntervalFrames = 3`, Eigenschaft `VisionInterval` (Ticks der Welt), `tick % VisionInterval == 0` (Phase 0 bleibt) | eigen |
| `World/SimulationWorld.cs` | `Step`, `StepObserved` | W2 | `_tick = tick + 1` (zweimal) | `tick + TickOrder.Next` | eigen (der Tickzähler selbst) |
| `World/SimulationWorld.Construction.cs` | `SaleDurationTicks` | A | `const long … = 179` | `SaleDurationFrames = 45 + 135`, `_saleDurationTicks = OriginalFrames(180) - TickOrder.Next` (179, 359, 719) | `Common/System/BuildAssistant.cpp`: `FRAMES_TO_ALLOW_SCAFFOLD` (`LOGICFRAMES_PER_SECOND * 1.5f`), `TOTAL_FRAMES_TO_SELL_OBJECT` (`LOGICFRAMES_PER_SECOND * 3.0f`), `BuildAssistant::update` (je Bild `100 / TOTAL_FRAMES_TO_SELL_OBJECT` Prozent bis −50 %: 135 Bilder) |
| `World/SimulationWorld.Construction.cs` | dieselbe Stelle, das „− 1“ | W2 | in der 179 enthalten | `- TickOrder.Next` (nicht skaliert) | eigen (Reihenfolge: die Erstattung fällt in den Schritt vor Ablauf) |
| `World/SimulationWorld.Construction.cs` | `AdvanceConstruction` | W1 | `Math.Max(BuildTicks, 1)` | `Math.Max(BuildTicks, _minimumBuildTicks)` (ein Bild des Originals) | eigen (Untergrenze); `DozerAIUpdate::update` nur als Aufrufer genannt, Herkunft unbelegt |
| `World/SimulationWorld.CreationLists.cs` | `ExecuteDeliverPayload` | W1 | `LogicRandom.Range(0, DelayDeliveryMaxTicks)` | `OriginalFrames(LogicRandom.Range(0, ToOriginalFrames(DelayDeliveryMaxTicks)))` | `GameLogic/Object/ObjectCreationList.cpp`, `DeliverPayloadNugget` (`GameLogicRandomValue(0, m_delayDeliveryFramesMax)`; `DelayDeliveryMax` über `INI::parseDurationUnsignedInt`) – im Inventar „Herkunft unbelegt“, jetzt belegt |
| `World/SimulationWorld.CreationLists.cs`, `World/SimulationWorld.cs` | `IsSignificantlyAbove` | A | Dezimalrechnung je Aufruf (seit Stufe 1 mit `OriginalFramesPerSecond²`) | `_significantHeight`, einmal je Welt berechnet (derselbe Ausdruck) | `Common/Thing/Thing.cpp`, `Thing::isSignificantlyAboveTerrain` (`-(3*3) * m_gravity`) |
| `World/SimulationWorld.Projectiles.cs` | `FlightTicks` | W1 (im Inventar E) | `ceil(Distanz / Tempo je Tick)` Ticks, sofort unter einem Tick Weg; `static` | in Bildern gezählt gegen den Weg eines Bildes (`speed.Raw * TicksPerOriginalFrame`), dann `OriginalFrames(…)`; Instanzmethode | `GameLogic/Object/Weapon.cpp`, `WeaponTemplate::fireWeaponTemplate` (`delayInFrames = v.length() / getWeaponSpeed()`, „< 1.0f“, `REAL_TO_INT_CEIL`) |
| `Templates/WeaponCatalog.cs`, `World/SimulationWorld.WeaponSlots.cs` | `WeaponInfo.DelayBetweenShotsWith`, `ClipReloadWith` (`ScaledByRateOfFire`), Aufrufer `ConsumeShot` | W1 | `max(1 Tick, floor(Ticks / Multiplikator))` | neue Überladungen mit `SimulationTime`: `OriginalFrames(max(MinimumShotDelayFrames, floor(ToOriginalFrames(ticks) / Multiplikator)))` | `Weapon.cpp`, `WeaponTemplate::getDelayBetweenShots` und `getClipReloadTime` (`REAL_TO_INT_FLOOR(… / bonusROF)`); die Untergrenze ist eigen |
| `Templates/WeaponCatalog.cs`, `World/SimulationWorld.FiringTracker.cs` | `WeaponInfo.PreAttackDelayWith`, Aufrufer `PreAttackDone` | W1 | `floor(Ticks × Multiplikator)` | `OriginalFrames(floor(ToOriginalFrames(ticks) × Multiplikator))`; nimmt `SimulationTime` | `Weapon.cpp`, `WeaponTemplate::getPreAttackDelay` (Rückgabe `Int` schneidet ab) |
| `Templates/WeaponCatalog.cs` | `WeaponCatalog.Build`: fehlender bzw. 0-ms-Schussabstand | W1 (im Inventar B/fallback) | `SimulationTiming.SleepNone` bzw. `Math.Max(SleepNone, delay)` (ein Tick) | `timing.OriginalFrames(WeaponInfo.MinimumShotDelayFrames)` (ein Bild des Originals) | eigen (das Original gäbe 0 zurück und feuerte im nächsten Bild) |
| `Replay/ReplayRunner.cs` | `Run` (Prüfpunkt- und Verifikationsrhythmus) | W2 | `(tick + 1) % options.…` (zweimal) | `(tick + TickOrder.Next) % options.…`; die Intervalle selbst sind Laufbudgets und bleiben Ticks | eigen |

Dazu ohne Umrechnung, aber benannt: `Math.Max(1, tick + …)` an drei Stellen von `SimulationWorld.Turrets.cs` (`StopAiming`, `ScanStep`, `ScheduleScan`) heißt `Math.Max(TurretDeadlineSet, …)` – die 1 ist der kleinste Wert, der „gesetzt“ bedeutet (0 heißt „keine Frist“), kein Zeitwert und keine Reihenfolge.

### A.2 Module A–M (46)

| Datei | Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|---|
| `AirModules.cs` | `ParkingPlaceBehavior.Update` (`_arrivals.RemoveAll`) | W2 (im Inventar B/literal) | `world.Tick - arrival.Tick > 2` | `> 2 * TickOrder.Next` – Reihenfolge: das Flugzeug entsteht im Tick des Ausgangs oder im nächsten | eigen |
| `MatchModules.cs` | `BaseRegenerateUpdate.HealRateFrames` | A | `const int HealRate = 3`, `return HealRate` | `HealRateFrames = 3`, `return world.Time.OriginalFrames(HealRateFrames)` | `Update/BaseRenerateUpdate.cpp`, `BaseRegenerateUpdate::update`: `const Int HEAL_RATE = 3`, `UPDATE_SLEEP(HEAL_RATE)` |
| `MatchModules.cs` | `BaseRegenerateUpdate.Update` (`amount`) | B | `… * Fixed.FromInt(HealRate) / TicksPerSecondFixed` | derselbe Ausdruck mit `Fixed.FromInt(OriginalFrames(HealRateFrames))` – Schlafdauer und Faktor über einen Wert | dieselbe Funktion: `amount = HEAL_RATE * maxHealth * m_baseRegenHealthPercentPerSecond / LOGICFRAMES_PER_SECOND` |
| `AirWeaponModules.cs` | `BunkerBusterBehavior.Update` | C | `world.Tick % CrashFxFrequencyTicks == 1` | `== world.Time.OriginalFrames(CrashFxPhaseFrames)` (Periode aus den Daten, Phase 1 Bild, beide skaliert) | `Behavior/BunkerBusterBehavior.cpp`, `update`: `getFrame() % m_crashThroughBunkerFXFrequency == 1` |
| `AirWeaponModules.cs` | `NeutronMissileSlowDeathBehavior.BeginSlowDeath` (Züge) | W1 | `LogicRandom.Range(0, (int)…VarianceTicks)` (zweimal) | `world.Time.DrawFrames(world.LogicRandom, 0, …VarianceTicks)` | `Behavior/SlowDeathBehavior.cpp`, `SlowDeathBehavior::beginSlowDeath`: `GameLogicRandomValue(0, d->m_sinkDelayVariance)` / `(0, d->m_destructionDelayVariance)` (im Inventar „Herkunft unbelegt“) |
| `AirWeaponModules.cs` | dieselbe Methode (`_destructionTick`) | W1 | `Math.Max(destruction, SleepNone)` | `world.Time.AtLeastOneFrame(destruction)` | eigen (Untergrenze) |
| `AirWeaponModules.cs` | `NeutronMissileSlowDeathBehavior.Update` (Blaststufen) | W1 | `Fixed.FromInt(world.Tick - _activationTick) > blast.DelayTicks` | vergangene Zeit auf ganze Bilder abgerundet (`world.Time.FloorToFrame(…)`, einmal je Update vor der Schleife), dann derselbe Vergleich: die Stufe fällt bei jeder Rate auf dieselbe Bildgrenze | `Update/NeutronMissileSlowDeathUpdate.cpp`, `update`: `currFrame - m_activationFrame > m_blastInfo[ i ].delay` |
| `AirWeaponModules.cs` | `MissileLauncherBuildingUpdate.SwitchTo` (`Opening`) | W2 (im Inventar A) | `AvailableOnFrame(world) - 1` | `- TickOrder.Next` („one frame ahead“ ist Reihenfolge) | `Update/MissileLauncherBuildingUpdate.cpp`, `switchToState`: `getReadyFrame() - 1` |
| `AirWeaponModules.cs` | dieselbe Methode (`Closing`, `half`) | W1 | `(AvailableOnFrame - now) / 2` | `OriginalFrames(ToOriginalFrames(AvailableOnFrame - now) / 2)` | dieselbe Funktion (Halbierung einer Bildzahl) |
| `AirWeaponModules.cs` | `SpectreData.Parse` (`HowitzerFiringRate`) | W1 | `Math.Max(Ticks(…, 10), 1)` | `configuration.Time.AtLeastOneFrame(Ticks(…, 10))` (Schutz des Modulo) | `Update/SpectreGunshipUpdate.cpp`, Konstruktor `m_howitzerFiringRate = 10` |
| `AirWeaponModules.cs` | `SpectreGunshipUpdate.Orbit` (`GattlingFiringWindowFrames`) | B | `world.Tick - gattling.LastFireTick <= 2` | `<= world.Time.OriginalFrames(GattlingFiringWindowFrames)` mit `= 2` | eigen (Ersatz für `OBJECT_STATUS_IS_FIRING_WEAPON`) |
| `BridgeModules.cs` | `BridgeScaffoldBehavior.AbsoluteMinSpeedPerFrame` | D (additiv) | `speed < 0.001` je Tick | `world.Time.PerOriginalFrameToPerTick(AbsoluteMinSpeedPerFrame)` an der Verwendung | `Behavior/BridgeScaffoldBehavior.cpp`, `update`: `if( speed < 0.001f ) speed = 0.001f` |
| `BridgeModules.cs` | `BridgeScaffoldBehavior._lateralSpeed/_verticalSpeed` | D (additiv) | Feldvorgabe `Fixed.One` je Tick | `DefaultSpeedPerFrame`; `OnSpawn` setzt beide auf `PerOriginalFrameToPerTick(DefaultSpeedPerFrame)` (Zustand bleibt „je Tick“, Schlüssel unverändert) | dieselbe Datei, Konstruktor: `m_lateralSpeed = 1.0f`, `m_verticalSpeed = 1.0f` |
| `CleanupAndSpyModules.cs` | `CleanupHazardUpdate.Update` / `FireWhenReady` (`_nextScan = data.ScanTicks`, zwei Stellen) | W1 (Zähler N + 1) | `_nextScan = data.ScanTicks` | `world.Time.CountdownReload(data.ScanTicks)` | `Update/CleanupHazardUpdate.cpp`, `update`: `m_nextScanFrames--` / `m_nextScanFrames = data->m_scanFrames` |
| `CleanupAndSpyModules.cs` | `CleanupHazardUpdate.FireWhenReady` (`RescanWaitMaxFrames`) | A + W1 | `_nextScan = LogicRandom.Range(0, 3)` | Zug in Bildern (`Range(0, RescanWaitMaxFrames)`), 0 bleibt 0, sonst `CountdownReload(OriginalFrames(n))` | dieselbe Datei, `fireWhenReady`: `m_nextScanFrames = GameLogicRandomValue( 0, 3 )` |
| `CleanupAndSpyModules.cs` | `SpyVisionUpdate.Update` (zwei Rückgaben) | W1 | `Math.Max(data.SelfPoweredIntervalTicks, SleepNone)` | `world.Time.AtLeastOneFrame(data.SelfPoweredIntervalTicks)` | eigen (Untergrenze) |
| `CleanupAndSpyModules.cs` | `AssistedTargetingUpdate.ReloadWaitFrames` | C | `const long ReloadWait = 1000` (Ticks) | `ReloadWaitFrames = 1000`, einmal je Update skaliert | eigen |
| `CrateModules.cs` | `PilotFindVehicleUpdate.Update` (`scan`) | C / W1 | `Math.Max(data.ScanTicks, SleepNone)` | `world.Time.AtLeastOneFrame(data.ScanTicks)` | `Update/PilotFindVehicleUpdate.cpp`: `ScanRate` über `INI::parseDurationUnsignedInt`, Konstruktor `m_scanFrames = 0` (im Inventar „Herkunft unbelegt“) |
| `DieAndUpdateModules.cs` | `TimedExpiryModule.OnSpawn` | W1 | `Range((int)_minTicks, (int)_maxTicks)`, `if (delay < 1) delay = 1` | `AtLeastOneFrame(DrawFrames(LogicRandom, _minTicks, _maxTicks))` | `Update/LifetimeUpdate.cpp`, `LifetimeUpdate::calcSleepDelay` |
| `DockModules.cs` | `AutoDepositUpdate.Update` (`_depositOnTick`) | C / W1 | `world.Tick + Math.Max(data.DepositTimingTicks, SleepNone)` | `world.Tick + world.Time.AtLeastOneFrame(data.DepositTimingTicks)` | `Update/AutoDepositUpdate.cpp`, `update`: `m_depositOnFrame = getFrame() + modData->m_depositFrame` (im Inventar „Herkunft unbelegt“; die Untergrenze ist eigen) |
| `FerryModules.cs` | `RailedTransportDockUpdate.Action` (`_pullPerTick`) | W1 (im Inventar E, Phase 20/23) | `Math.Max(data.PullInsideTicks, 1)` | `world.Time.AtLeastOneFrame(data.PullInsideTicks)` | `Update/DockUpdate/RailedTransportDockUpdate.cpp`: `m_pullInsideDistancePerFrame = mag / m_pullInsideDurationInFrames` |
| `FerryModules.cs` | `RailedTransportDockUpdate.UnloadNext` (`_pushPerTick`) | W1 | `Math.Max(data.PushOutsideTicks, 1)` | `world.Time.AtLeastOneFrame(data.PushOutsideTicks)` | dieselbe Datei: `m_pushOutsideDistancePerFrame = mag / m_pushOutsideDurationInFrames` |
| `FieldEffectModules.cs` | `TensileFormationUpdate.LifetimeFrames` | A | `const int Lifetime = 300`, `++_life > Lifetime` | `LifetimeFrames = 300`, `++_life > world.Time.OriginalFrames(LifetimeFrames)` (`_life` zählt weiter Updates = Ticks) | `Update/TensileFormationUpdate.cpp`, `TensileFormationUpdate::update`: `if (++m_life > 300)` |
| `FieldEffectModules.cs` | `TensileFormationUpdate.DormantCheckFrames` | A | `return 30` | `return world.Time.OriginalFrames(DormantCheckFrames)` | dieselbe Funktion: `return UPDATE_SLEEP(30)` |
| `FieldEffectModules.cs` | `TensileFormationUpdate.PropagatePeriodFrames/PropagatePhaseFrames` | A / C | `_life % 30 == 29` | `_life % OriginalFrames(30) == OriginalFrames(29)` | dieselbe Funktion: `if ( m_life%30 == 29 )` |
| `FlightModules.cs` | `JetSlowDeathBehavior.BeginSlowDeath` (`SinkDelayVarianceTicks`) | W1 | `LogicRandom.Range(0, (int)…)` | `world.Time.DrawFrames(…)` | `Behavior/SlowDeathBehavior.cpp`, `beginSlowDeath` (im Inventar „Herkunft unbelegt“) |
| `FlightModules.cs` | dieselbe Methode (`DestructionDelayVarianceTicks`) | W1 | `LogicRandom.Range(0, (int)…)` | `world.Time.DrawFrames(…)` | wie oben |
| `FlightModules.cs` | dieselbe Methode (`_destructionTick`) | W1 | `Math.Max(destruction, SleepNone)` | `world.Time.AtLeastOneFrame(destruction)` | eigen |
| `FlightModules.cs` | `CountermeasuresBehavior.ReportMissile` | B / W1 | `Math.Max(data.ReactionTicks, 1)` | `world.Time.AtLeastOneFrame(data.ReactionTicks)` | eigen (0 heißt „nicht gesetzt“) |
| `FlightModules.cs` | `DeliverPayloadAIUpdate.StartPhase` / `RunDelivering` (`_dropDelayLeft`, zwei Stellen) | W1 (Zähler N + 1) | `= data.DoorDelayTicks` / `= run.DropDelayTicks` | `world.Time.CountdownReload(…)` | `Update/AIUpdate/DeliverPayloadAIUpdate.cpp`: `m_dropDelayLeft = getDoorDelay()`, `--m_dropDelayLeft`, `m_dropDelayLeft = getDropDelay()` |
| `FlightModules.cs` | `DeliverPayloadAIUpdate.TurnTicks` | W1 | `Math.Max(1, (MinTurnRadius() / Speed).Ceiling())`, sonst `1` | `AtLeastOneFrame(CeilingToFrame(…))` in Bildern aufgerundet, sonst `OriginalFrames(1)` | dieselbe Datei, `DeliverPayloadAIUpdate::calcMinTurnRadius` (`timeToTravelThatDist`) |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.PendingPathFrames` | B | `world.Tick - _pendingPathTick <= 3` | `<= world.Time.OriginalFrames(PendingPathFrames)` mit `= 3` | eigen |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Update` (`_nextScorch`) | W1 | `birth + (death - birth) * n / Total` | `birth + OriginalFrames(ToOriginalFrames(death - birth) * n / Total)` | `Update/ParticleUplinkCannonUpdate.cpp`, `update`: `nextFactor = (Real)m_scorchMarksMade / (Real)data->m_totalScorchMarks` (im Inventar „Herkunft unbelegt“) |
| `FlightModules.cs` | dieselbe Zeile (`death + 1`) | W2 | `death + 1` („nie wieder“) | `death + TickOrder.Next` | eigen |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Update` (`_nextPulse`) | W1 | `birth + (death - birth) * n / TotalDamagePulses` | wie `_nextScorch` | dieselbe Funktion: `m_nextDamagePulseFrame = orbitalBirthFrame + nextFactor * (orbitalDeathFrame - orbitalBirthFrame)` |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Update` (`_nextLaunchFx`) | C / W1 | `now + Math.Max(data.LaunchFxTicks, 1)` | `now + world.Time.AtLeastOneFrame(data.LaunchFxTicks)` | dieselbe Datei (`m_nextLaunchFXFrame`; Untergrenze eigen) |
| `HackerAndAssaultModules.cs` | `HackInternetAIUpdate.Varied` | W1 | `(Fixed.FromInt(ticks) * variation).Floor()` | in Bildern variiert und abgerundet, skaliert, dann `CountdownReload` | `Update/AIUpdate/HackInternetAIUpdate.cpp`, `UnpackingState::onEnter` / `PackingState::onEnter`: `m_framesRemaining = getUnpackTime() * variation` |
| `HackerAndAssaultModules.cs` | `HackInternetAIUpdate.CashDelay` und `_remaining = 0` beim Packen im Gebäude | W1 (Zähler N + 1) | `_remaining = CashDelay()` bzw. `0` | `world.Time.CountdownReload(…)` bzw. `CountdownReload(0)` | dieselbe Datei, `HackInternetState::update`: `if (m_framesRemaining > 0) m_framesRemaining--` |
| `JetAIUpdate.cs` | `TaxiAlongPath` (`FallbackTaxiSpeedPerFrame`) | D (additiv) | `taxi?.Speed ?? Fixed.One` | `?? world.Time.PerOriginalFrameToPerTick(FallbackTaxiSpeedPerFrame)` | eigen |
| `JetAIUpdate.cs` | `UpdateAttackLocomotor` | B / W1 | `Math.Max(data.AttackLocomotorPersistTicks, 1)` | `world.Time.AtLeastOneFrame(…)` (die Methode nimmt jetzt die Welt) | eigen (Referenz ohne Untergrenze) |
| `JetAIUpdate.cs` | `Land`, Hubschrauber (`MinHelicopterApproachSpeedPerFrame`) | D (additiv; im Inventar Teil der E-Zeile 776–800) | `Fixed.Max(flight.MinSpeed, Fixed.One)` | `Fixed.Max(flight.MinSpeed, PerOriginalFrameToPerTick(…))` | eigen |
| `JetAIUpdate.cs` | `Land`, Gleitflug (`MinLandingSpeedPerFrame`) | D (additiv) | `Fixed.Max(flight.MinSpeed, Fixed.One / Fixed.FromInt(2))` | `Fixed.Max(flight.MinSpeed, PerOriginalFrameToPerTick(MinLandingSpeedPerFrame))` | eigen |
| `JetAIUpdate.cs` | `BeginReload` (`total`) | W1 | `time * fehlend / ClipSize` | `OriginalFrames(ToOriginalFrames(time) * fehlend / ClipSize)` | `Update/AIUpdate/JetAIUpdate.cpp`, `JetOrHeliReloadAmmoState::onEnter`: `rt = (rt * needed) / clipSize` |
| `JetAIUpdate.cs` | `BeginReload` (`_reloadDoneTick`) | A | `Math.Max(total, 1)` | `world.Time.AtLeastOneFrame(total)` | dieselbe Funktion: `if (m_reloadTime < 1) m_reloadTime = 1` |
| `LastBlockerModules.cs` | `SupplyWarehouseCripplingBehavior.Update` (Rückgabe) | W1 (im Inventar E) | `Math.Max(_nextHealing - world.Tick, SleepNone)` | `world.Time.AtLeastOneFrame(_nextHealing - world.Tick)`; `_nextHealing` selbst bleibt unverändert (Zustand) | `Behavior/SupplyWarehouseCripplingBehavior.cpp`, `update`: `m_nextHealingFrame = now + md->m_selfHealDelay`, `UPDATE_SLEEP(m_nextHealingFrame - now)` |
| `MineModules.cs` | `DemoTrapUpdate.Update` (`_scanCountdown`) | W1 (Zähler N + 1) | `_scanCountdown = data.ScanTicks` | `world.Time.CountdownReload(data.ScanTicks)` | `Update/DemoTrapUpdate.cpp`, `DemoTrapUpdate::update`: `m_nextScanFrames--` / `m_nextScanFrames = m_scanFrames` |

**Phasenlage der Zähler (bekannte Grenze).** `CountdownReload` ist für einen Zähler bemessen, der **in** einem Update nachgeladen wird (dann vergehen bis zur nächsten Handlung N + 1 Bilder). Wird der erste Zähler von außen gesetzt und läuft das Update noch im selben Tick (Hacker, der zwischen zwei Schritten ins Gebäude geht), endet er bei 60 Hz einen Tick, bei 120 Hz bis zu drei Ticks später als die Bildgrenze – ein einmaliger Versatz unter einem Bild; die Periode danach ist exakt. Der Test `TickRateModulesAmHackerCountdown` hält genau das fest.

### A.3 Module N–Z (69)

Spalte „Kl.“: Klasse des Inventars, dahinter der Fall (W1 a = Rundung zur Laufzeit, W1 b = Untergrenze ein Tick, W1 c = Zähler N + 1, W1 d = Zufall über Tickbereich, W2 = Reihenfolge).

| Datei | Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|---|
| `Phase12Modules.cs` | `FireWeaponWhenDamagedBehavior.Fire` | E / W1 b | `Math.Max(DelayBetweenShotsTicks, SleepNone)` | `world.Time.AtLeastOneFrame(…)` | eigen (ersetzt den Waffenstatus; `FireWeaponWhenDamagedBehavior.cpp`, `update`) |
| `Phase12Modules.cs` | `FireWeaponPower.Update` | E / W1 b | Schlaf `Math.Max(DelayBetweenShotsTicks, SleepNone)` | `AtLeastOneFrame(…)` | eigen |
| `Phase12Modules.cs` | `HordeUpdate.OnSpawn` | C / W1 d | `LogicRandom.Range(1, UpdateRateTicks)` | `world.Time.DrawFrames(LogicRandom, TicksPerOriginalFrame, AtLeastOneFrame(UpdateRateTicks))` | `HordeUpdate.cpp`, Konstruktor: `UPDATE_SLEEP(GameLogicRandomValue(1, delay))` |
| `Phase12Modules.cs` | `HordeUpdate.Update` (Fahrzeuge) | C / W1 c | `world.Tick > _lastRefresh + UpdateRateTicks` | `world.Tick >= _lastRefresh + UpdateRateTicks + TicksPerOriginalFrame` (Periode Rate + 1 Bilder) | `HordeUpdate.cpp`, `HordeUpdate::update`: `getFrame() > m_lastHordeRefreshFrame + m_updateRate` |
| `Phase5BlockerModules.cs` | `FlammableUpdate.Ignite` (`_aflameEndTick`) | E / W1 b | `Math.Max(AflameDurationTicks, SleepNone)` | `AtLeastOneFrame(…)` | eigen (Mindestwert) |
| `Phase5BlockerModules.cs` | `FlammableUpdate.Update` (`_damageEndTick`) | E / W1 b | `Math.Max(AflameDamageDelayTicks, SleepNone)` | `AtLeastOneFrame(…)` | eigen (Mindestwert) |
| `Phase5BlockerModules.cs` | `SlowDeathBehavior.BeginSlowDeath` (Sink-Zug) | B / W1 d | `Range(0, SinkDelayVarianceTicks)` | `world.Time.DrawFrames(world.LogicRandom, 0, SinkDelayVarianceTicks)` | `SlowDeathBehavior.cpp`, `beginSlowDeath`: `m_sinkDelay + GameLogicRandomValue(0, m_sinkDelayVariance)` |
| `Phase5BlockerModules.cs` | `SlowDeathBehavior.BeginSlowDeath` (Zerstörungs-Zug) | B / W1 d | `Range(0, DestructionDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | ebenda: `m_destructionDelay + GameLogicRandomValue(0, m_destructionDelayVariance)` |
| `Phase5BlockerModules.cs` | `SlowDeathBehavior.BeginSlowDeath` (`_destructionTick`) | E / W1 b | `Math.Max(destruction, SleepNone)` | `AtLeastOneFrame(…)` | eigen (Mindestwert) |
| `Phase5BlockerModules.cs` | `StealthDetectorUpdate.OnSpawn` | C / W1 d | `Range(1, UpdateRateTicks)` | `world.Time.DrawFrames(world.LogicRandom, TicksPerOriginalFrame, UpdateRateTicks)` | `StealthDetectorUpdate.cpp`, Konstruktor: `UPDATE_SLEEP(GameLogicRandomValue(1, m_updateRate))` |
| `Phase5BlockerModules.cs` | `StealthDetectorUpdate.Update` (`MarkDetected`) | E / W2 | `UpdateRateTicks + 1` | `UpdateRateTicks + TickOrder.Next` (nicht skaliert) | `StealthDetectorUpdate.cpp`, `update`: `markAsDetected(m_updateRate + 1)` |
| `Phase5BlockerModules.cs` | `PoisonedBehavior.OnDamage` (`_poisonStopTick`) | E / W1 b | `Math.Max(PoisonDurationTicks, SleepNone)` | `AtLeastOneFrame(…)` | eigen (Mindestwert) |
| `Phase5BlockerModules.cs` | `PoisonedBehavior.OnDamage` (`freshDamageTick`) | E / W1 b | `Math.Max(PoisonDamageIntervalTicks, SleepNone)` | `AtLeastOneFrame(…)` | eigen (Mindestwert) |
| `Phase5BlockerModules.cs` | `PoisonedBehavior.Update` (`_poisonDamageTick`) | E / W1 b | wie oben | wie oben | eigen (Mindestwert) |
| `Phase5Modules.cs` | `StealthUpdate.Update` (`_readyTick`) | E / W1 b | `Math.Max(StealthDelayTicks, SleepNone)` | `AtLeastOneFrame(…)` | eigen (Mindestwert) |
| `Phase5Modules.cs` | `AutoHealBehavior.OnSpawn` | E / W1 b | `Math.Max(StartHealingDelayTicks, SleepNone)` | `AtLeastOneFrame(…)` | eigen (Mindestwert) |
| `Phase5Modules.cs` | `AutoHealBehavior.TryActivate` | E / W1 b | wie oben | wie oben | eigen (Mindestwert) |
| `Phase5Modules.cs` | `AutoHealBehavior.Update` | E / W1 b | `return Math.Max(HealingDelayTicks, SleepNone)` | `AtLeastOneFrame(…)` (ein Heilimpuls je Bild des Originals) | `AutoHealBehavior.h`: `"HealingDelay"` `parseDurationUnsignedInt` (Standard dort `UINT_MAX`, Nebenbefund des Inventars) |
| `Phase5Modules.cs` | `AIUpdateData.Parse` (`MoodAttackCheckRate`) | A / W1 b | `Math.Max(1, Ticks(…))` | `configuration.Time.AtLeastOneFrame(Ticks(…))` (Katalogbau) | `AIUpdate.cpp`, `AIUpdateModuleData::AIUpdateModuleData`: `m_moodAttackCheckRate = LOGICFRAMES_PER_SECOND * 2` |
| `Phase5Modules.cs` | `AIUpdateInterface.Move` (Neuplanung) | C | `world.Tick - _blockerReplanTick >= 30` | `>= _blockerReplanInterval` = `OriginalFrames(BlockerReplanFrames)` mit `BlockerReplanFrames = OriginalFramesPerSecond` | eigen (`docs/DEMO-FINDINGS.md` Nr. 12) |
| `Phase5Modules.cs` | `AIUpdateInterface.Move` (Vorausschau) | B | `Entity.Speed * Fixed.FromInt(25)` (zweimal) | `Entity.Speed * _blockerLookAhead` = `Fixed.FromInt(OriginalFrames(BlockerLookAheadFrames))`, `BlockerLookAheadFrames = 25` | eigen |
| `Phase5Modules.cs` | `ProductionUpdate.Update` (Tür zu) | C / W2 | `RemainingTicks = 1` | `RemainingTicks = TickOrder.Next` (nicht skaliert) | eigen („im nächsten Tick wieder fragen“) |
| `ProjectileModules.cs` | `MissileAIUpdate.SetFramesTillCountermeasureDiversionOccurs` | E / W1 b | `Math.Max(frames, 1)` | `AtLeastOneFrame(frames)` | `MissileAIUpdate.cpp`, `setFramesTillCountermeasureDiversionOccurs` |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` (Ziel verlässt Reichweite) | A / W1 c+d | `_nextScan = Range(0, 3)`; `if (_nextScan == 0)` | `wait = Range(0, RescanMaxFrames)`; `_nextScan = CountdownReload(OriginalFrames(wait))`; `if (wait == 0)` | `PointDefenseLaserUpdate.cpp`: `m_nextScanFrames = GameLogicRandomValue( 0, 3 )` |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` (Ziel tot) | A / W1 c+d | wie oben | wie oben | ebenda |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.Update` (`_nextScan = ScanTicks`) | C / W1 c | `_nextScan = data.ScanTicks` | `world.Time.CountdownReload(data.ScanTicks)` | `PointDefenseLaserUpdate.cpp`: `m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames` |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` (zwei weitere `_nextScan = ScanTicks`) | C / W1 c | wie oben | wie oben (2 Stellen) | ebenda |
| `ProjectileModules.cs` | `PointDefenseLaserUpdate.FireWhenReady` (`_nextShot`) | E / W1 c | `_nextShot = weapon.DelayBetweenShotsTicks` | `world.Time.CountdownReload(…)` (Schussabstand Delay + 1 Bilder) | `PointDefenseLaserUpdate.cpp`: `m_nextShotAvailableInFrames-- / = wt->getDelayBetweenShots( bonus )` |
| `RailroadModules.cs` | `RailroadBehavior.FramesUnpulledLongEnoughToUnhitch` | A / W2 | `const int … = 2` | `const long … = 2 * TickOrder.Next` (nicht skaliert) | `RailroadGuideAIUpdate.cpp`: `#define FRAMES_UNPULLED_LONG_ENOUGH_TO_UNHITCH (2)` |
| `RepairModules.cs` | `RepairDockData.Parse` (ohne `TimeForFullHeal`) | B / fallback | `Fixed.One` (ein Tick) | `Fixed.FromInt(configuration.Time.TicksPerOriginalFrame)` (ein Bild) | `RepairDockUpdate.cpp`, Konstruktor: `m_framesForFullHeal = 1.0f` |
| `RepairModules.cs` | `AutoFindHealingUpdate.Update` | C / W1 c | `_nextScanTicks = data.ScanTicks` | `world.Time.CountdownReload(…)` | `AutoFindHealingUpdate.cpp`: `m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames` |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (Unpacking/Packing) | A | `_framesToWait = 15` | `OriginalFrames(PackFrames)`, `PackFrames = 15` | `SlavedUpdate.cpp`, `setRepairState`: `m_framesToWait = 15` |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (None → Unpacking) | A | `_framesToWait = 15` | `OriginalFrames(PackFrames)` | ebenda |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (Welding → Retracting) | A | `_framesToWait = 5` | `OriginalFrames(ArmFrames)`, `ArmFrames = 5` | ebenda: `m_framesToWait = 5` |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (Ready → Extending) | A | `_framesToWait = 5` | `OriginalFrames(ArmFrames)` | ebenda |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (Ready-Dauer) | B / W1 d | `Range(MinReadyTicks, MaxReadyTicks)` | `world.Time.DrawFrames(…)` | ebenda: `GameLogicRandomValue(m_minReadyFrames, m_maxReadyFrames)` |
| `SlaveModules.cs` | `SlavedUpdate.SetRepairState` (Schweißdauer) | B / W1 d | `Range(MinWeldTicks, MaxWeldTicks)` | `world.Time.DrawFrames(…)` | ebenda: `GameLogicRandomValue(m_minWeldFrames, m_maxWeldFrames)` |
| `SlaveModules.cs` | `MobMemberSlavedUpdate.CheckInterval` | A | `const int CheckInterval = 16`; `++_framesToWait < CheckInterval` | `CheckIntervalFrames = 16`; `< world.Time.OriginalFrames(CheckIntervalFrames)` | `MobMemberSlavedUpdate.cpp`, `update`: `if ( ++m_framesToWait < 16 )` |
| `SlaveModules.cs` | `MobMemberSlavedUpdate.OnSpawn` | A / W1 d | `_framesToWait = Range(0, 20)` | `OriginalFrames(Range(0, FirstCheckStaggerFrames))` | `MobMemberSlavedUpdate.cpp`, Konstruktor: `m_framesToWait = GameLogicRandomValue(0,20)` |
| `SpecialAbilityStaging.cs` | `SpecialAbilityUpdate.Varied` | B / W1 a | `Math.Max(1, (Fixed.FromInt(ticks) * factor).Floor())` | dieselbe Rechnung auf `ToOriginalFrames(ticks)`, Ergebnis über `OriginalFrames` | `SpecialAbilityUpdate.cpp`, `startPacking`/`startUnpacking`: `GameLogicRandomValueReal(1 - m_packUnpackVariationFactor, …)` |
| `SpecialPowerModules.cs` | `SpectreGunshipDeploymentUpdate.TryTakePending` | E / W2 | `world.Tick - value.Tick <= 2` | `<= 2 * TickOrder.Next` (nicht skaliert) | eigen (die Entität entsteht am Tickende) |
| `StructureModules.cs` | `StructureCollapseUpdate.OnDie` (`_collapseTick`) | B / W1 d | `Range(MinCollapseDelay, MaxCollapseDelay)` | `world.Time.DrawFrames(…)` | `StructureCollapseUpdate.cpp`, `beginStructureCollapse` |
| `StructureModules.cs` | `StructureCollapseUpdate.Update` (erster `_burstTick`) | B / W1 d | `Range(MinBurstDelay, MaxBurstDelay)` | `world.Time.DrawFrames(…)` | `StructureCollapseUpdate.cpp`, `update`: `m_burstFrame += GameLogicRandomValue(d->m_minBurstDelay, d->m_maxBurstDelay)` |
| `StructureModules.cs` | `StructureCollapseUpdate.Update` (`_burstTick +=`) | B / W1 d | wie oben | wie oben | ebenda |
| `StructureModules.cs` | `FireSpreadUpdate.NextDelay` | B / W1 b+d | `Math.Max(Range(MinDelay, MaxDelay), SleepNone)` | `world.Time.AtLeastOneFrame(world.Time.DrawFrames(…))` | `FireSpreadUpdate.cpp`, `calcNextSpreadDelay` |
| `ToppleAndFallModules.cs` | `StructureToppleUpdate.OnDie` (`_toppleFrame`) | B / W1 d | `Math.Max(0, Range(MinToppleDelayTicks, MaxToppleDelayTicks))` | `Math.Max(0, world.Time.DrawFrames(…))` | `StructureToppleUpdate.cpp`, `beginStructureTopple` |
| `ToppleAndFallModules.cs` | `ToppleUpdate.VelocityBounceLimit` | A (additiv je Bild) | `0,01` als Schwelle gegen die Winkelgeschwindigkeit je Tick | `VelocityBounceLimitPerFrame`; an der Verwendung `world.Time.PerOriginalFrameToPerTick(…)` (nur im Abprall-Tick) | `ToppleUpdate.cpp`, `ToppleUpdate::update`: `const Real VELOCITY_BOUNCE_LIMIT = 0.01f` |
| `ToppleAndFallModules.cs` | `StructureToppleUpdate.AccelFactor` | A (additiv je Bild²) | `0,02` als Winkelbeschleunigung je Tick² | `AccelFactorPerFrameSquared`; in `StepToppling` `world.Time.PerOriginalFrameSquaredToPerTickSquared(…)` | `StructureToppleUpdate.cpp`, `update`: `TOPPLE_ACCELERATION_FACTOR = 0.02f` |
| `TransportModules.cs` | `HelicopterSlowDeathBehavior.BeginSlowDeath` (Sink-Zug) | E / W1 d | `Range(0, SinkDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | `SlowDeathBehavior.cpp`, `beginSlowDeath` (siehe oben) |
| `TransportModules.cs` | `HelicopterSlowDeathBehavior.BeginSlowDeath` (Zerstörungs-Zug) | E / W1 d | `Range(0, DestructionDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | ebenda |
| `TransportModules.cs` | `HelicopterSlowDeathBehavior.BeginSlowDeath` (`_destructionTick`) | E / W1 b | `Math.Max(destruction, SleepNone)` | `AtLeastOneFrame(…)` | eigen (Mindestwert) |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.GroundCheckDelay` | A | `const long … = 10`; `world.Tick + GroundCheckDelay` | `GroundCheckDelayFrames`; `world.Tick + OriginalFrames(…)` | `BattleBusSlowDeathBehavior.cpp`: `GROUND_CHECK_DELAY = 10` |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.EmptyHulkCheckDelay` | A | `const long … = 15`; `return EmptyHulkCheckDelay` | `EmptyHulkCheckDelayFrames`; `return OriginalFrames(…)` | `BattleBusSlowDeathBehavior.cpp`: `EMPTY_HULK_CHECK_DELAY = 15`, `UPDATE_SLEEP(EMPTY_HULK_CHECK_DELAY)` |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.BeginSlowDeath` (Sink-Zug) | E / W1 d | `Range(0, SinkDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | `SlowDeathBehavior.cpp`, `beginSlowDeath` |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.BeginSlowDeath` (Zerstörungs-Zug) | E / W1 d | `Range(0, DestructionDelayVarianceTicks)` | `world.Time.DrawFrames(…)` | ebenda |
| `VehicleModules.cs` | `BattleBusSlowDeathBehavior.BeginSlowDeath` (`_destructionTick`) | E / W1 b | `Math.Max(destruction, SleepNone)` | `AtLeastOneFrame(…)` | eigen (Mindestwert) |
| `WorldEffectModules.cs` | `OCLUpdate.SetNextCreation` | E / W1 d | `Range(MinDelay, MaxDelay)` | `world.Time.DrawFrames(…)` | `OCLUpdate.cpp`: `GameLogicRandomValue(m_minDelay, m_maxDelay)` |
| `WorldEffectModules.cs` | `DynamicShroudClearingRangeUpdate.Update` (`_changeIntervalCountdown`) | C / W1 c | `= GrowInterval` bzw. `ChangeInterval` | `world.Time.CountdownReload(…)` | `DynamicShroudClearingRangeUpdate.cpp`, `update` (Zeilen 271–279) |
| `WorldEffectModules.cs` | `FireOCLAfterWeaponCooldownUpdate.Update` | E / W2 | `LastFireTickOf(slot) == now - 1` | `== now - TickOrder.Next` (nicht skaliert) | `FireOCLAfterWeaponCooldownUpdate.cpp`, `update`: `weapon->getLastShotFrame() == now - 1` |
| `WorldEffectModules.cs` | `FireOCLAfterWeaponCooldownUpdate.FireOcl` | B / W1 a | `(Tick − _startTick) * LifetimePerSecond / 1000` (abgeschnitten, in Ticks) | dieselbe Rechnung auf `ToOriginalFrames(Tick − _startTick)`, Ergebnis über `OriginalFrames` | ebenda, Zeilen 200–202 |
| `WorldEffectModules.cs` | `WaveGuideUpdate.Update` (Splash-Wurf) | A / W1 c | `now - _splashTick > OriginalFrames(30 / 2)` | `>= OriginalFrames(SplashFrames) + TicksPerOriginalFrame` (Periode 16 Bilder) | `WaveGuideUpdate.cpp`, `update`: `getFrame() - m_splashSoundFrame > LOGICFRAMES_PER_SECOND / 2.0f` |
| `WorldEffectModules.cs` | `CheckpointUpdate.GateStep` | D (additiv je Bild) | `0,333` je Tick | `GateStepPerFrame`; in `Update` `world.Time.PerOriginalFrameToPerTick(…)` | `CheckpointUpdate.cpp`, `update`: `radius - 0.333f` / `radius + 0.333f` |
| `WorldEffectModules.cs` | `CheckpointUpdate.OnSpawn` | E / W1 d | `Range(0, ScanDelayTicks)` | `world.Time.DrawFrames(world.LogicRandom, 0, ScanDelayTicks)` (Wert weiter ungenutzt, der Zug bleibt) | `CheckpointUpdate.cpp`, Konstruktor: `GameLogicRandomValue(0, m_enemyScanDelayTime)` |
| `WorldEffectModules.cs` | `EnemyNearUpdate.OnSpawn` | E / W1 c+d | `Range(0, ScanDelayTicks)` | `CountdownReload(DrawFrames(LogicRandom, 0, ScanDelayTicks))` | `EnemyNearUpdate.cpp`, Konstruktor: `GameLogicRandomValue(0, m_enemyScanDelayTime)` |
| `WorldEffectModules.cs` | `EnemyNearUpdate.Update` | C / W1 c | `_scanDelay = data.ScanDelayTicks` | `world.Time.CountdownReload(…)` | `EnemyNearUpdate.cpp`, `update`: `if (m_enemyScanDelay == 0) … else --m_enemyScanDelay` |
| `WorldEffectModules.cs` | `FirestormDynamicGeometryInfoUpdate.OnSpawn` | E / W1 b | `Math.Max(InitialDelay, 1)` | `AtLeastOneFrame(InitialDelay)` | `DynamicGeometryInfoUpdate.cpp`, `update`: `m_startingDelayCountdown--` |
| `WorldEffectModules.cs` | `FirestormDynamicGeometryInfoUpdate.Update` (Schadensscan) | E / W1 b | `Tick − _lastDamage >= DamageDelay` (bei 0 jeder Tick) | `>= AtLeastOneFrame(DamageDelay)` | `FirestormDynamicGeometryInfoUpdate.cpp`, `update` (Zeilen 215–219) |
| `WorldEffectModules.cs` | `WeaponBonusUpdate.Update` | C / W1 b | `Math.Max(Delay, SleepNone)` | `AtLeastOneFrame(Delay)` | `WeaponBonusUpdate.cpp`, `update`: `return UPDATE_SLEEP(data->m_bonusDelay)` |

Die Zeile `PointDefenseLaserUpdate.FireWhenReady` „zwei weitere `_nextScan = ScanTicks`“ zählt zweifach. Zu `AIUpdateInterface.Move`: Neuplanungsfrist und Vorausschau werden **einmal je Modulinstanz** aufgelöst (`ResolveBlockerTiming`, zwei nicht gespeicherte Felder, beim ersten `Move`); der Wächterwert `_blockerReplanTick = -1000` („nie geplant“, auch Standard im Snapshot) bleibt. `StructureToppleUpdate.StepToppling` rechnet bei 60/120 Hz eine `Fixed`-Division je Tick des Kippens (bei 30 Hz keine).

### A.4 Skripte, Mission, Skirmish, Szenarien (46)

„Ordnung“ heißt: nicht skaliert, mit `TickOrder.Next` geschrieben (W2). `SkirmishAi` und die Fahrer sind eigener Code ohne Herkunft im Original.

| Datei | Symbol | Kl. | War | Ist | Herkunft |
|---|---|---|---|---|---|
| `Skirmish/SkirmishAi.cs` | `FireInterval` → `FireIntervalFrames`, Feld `_fireInterval` | C | `(_tick + _phase) % 5` | `(_tick + _phase) % _fireInterval`, im Konstruktor `Time.OriginalFrames(5)` | eigen |
| `Skirmish/SkirmishAi.cs` | `ThinkInterval` → `ThinkIntervalFrames`, Feld `_thinkInterval` | C | `% 15` | `% _thinkInterval` | eigen |
| `Skirmish/SkirmishAi.cs` | `_phase` | C | `Player % 5` Ticks | `Time.OriginalFrames(Player % FireIntervalFrames)` | eigen |
| `Skirmish/SkirmishAi.cs` | `SkirmishAiStats.AttackTicks` | B | `+= 15` je Denkschritt | `+= _thinkInterval` (Ticks) | eigen |
| `Skirmish/SkirmishAi.cs` | `RetryTicks` → `RetryFrames` (Standard von `Backoff`, vier direkte Verwendungen) | B | 300 Ticks | `Backoff` nimmt Bilder und skaliert selbst | eigen |
| `Skirmish/SkirmishAi.cs` | `MemoryTicks` → `MemoryFrames` | B | 900 | `Time.OriginalFrames(MemoryFrames)` | eigen |
| `Skirmish/SkirmishAi.cs` | `OrderRefreshTicks` → `OrderRefreshFrames` | B | 150 | ebenso | eigen |
| `Skirmish/SkirmishAi.cs` | `BoardingTicks` → `BoardingFrames` (2 Stellen) | B | 300 | ebenso | eigen |
| `Skirmish/SkirmishAi.cs` | `RevisitTicks` → `RevisitFrames` (2 Stellen) | B | 3600 | ebenso | eigen |
| `Skirmish/SkirmishAi.cs` | `QueueWorker`: `Backoff(…, 60)` | B | 60 | `ShortRetryFrames` | eigen |
| `Skirmish/SkirmishAi.cs` | `QueueWorker`: `BuildTicks + 30` | B | 30 | `BuildTicks + Time.OriginalFrames(InFlightMarginFrames)` (`BuildTicks` ist schon Ticks) | eigen |
| `Skirmish/SkirmishAi.cs` | `BuildStructures`: `essential ? 60 : RetryTicks` | B | 60 | `ShortRetryFrames`/`RetryFrames` | eigen |
| `Skirmish/SkirmishAi.cs` | `FindSupplyPlacement`: `Backoff(…, 150)` | B | 150 | `SupplyLookRetryFrames` | eigen |
| `Skirmish/SkirmishAi.cs` | `ManageSites`: `_tick - LastResume < 60` | B | 60 | `Time.OriginalFrames(ShortRetryFrames)` | eigen |
| `Skirmish/SkirmishAi.cs` | `BuyScience`: `_tick + 1800` | B | 1800 | `ScienceRetryFrames` | eigen |
| `Skirmish/SkirmishAi.cs` | `UsePowers`: `RetryTicks`/3000/900 | B | Ticks | `RetryFrames`/`PowerMissingScienceRetryFrames`/`PowerRefusedRetryFrames`, einmal skaliert | eigen |
| `Skirmish/SkirmishAi.cs` | `CommandArmy`: `< 60` | B | 60 | `ShortRetryFrames` | eigen |
| `Skirmish/SkirmishAi.cs` | `UseTunnels`: `Backoff(0x81000000u, 90)` | B | 90 | `TunnelEvacuationRetryFrames` | eigen |
| `Skirmish/SkirmishAi.cs` | `UseTransports`: `Backoff(…, 60)` | B | 60 | `ShortRetryFrames` | eigen |
| `Skirmish/SkirmishAi.cs` | `Scout`: `_tick - _scoutAt < 450` | B | 450 | `ScoutReachFrames` | eigen |
| `Skirmish/SkirmishAi.cs` | `ScoutForWarehouses`: `Backoff(…, 300)` | B | 300 | `WarehouseScoutRetryFrames` | eigen |
| `Skirmish/SkirmishAi.cs` | `OrderCarriers`: `_tick - since < 90` | B | 90 | `CarrierIdleFrames` | eigen |
| `Skirmish/SkirmishMatchSession.cs` | `VictoryCheckInterval` → `VictoryCheckIntervalFrames`, Feld `_victoryCheckInterval` | C | `tick % 15` | `tick % _victoryCheckInterval` | eigen |
| `Skirmish/SkirmishMatchSession.cs` | `SkirmishMatchOptions.SciencePointInterval` | C | `tick % Option` | `tick % _sciencePointInterval` (Option in Bildern) | eigen |
| `Skirmish/SkirmishMatchSession.cs` | `SkirmishMatchOptions.GraceTicks` | B | `tick + 1 + Option` | `tick + TickOrder.Next + _graceTicks` (Option in Bildern) | eigen |
| `Skirmish/SkirmishMatchSession.cs` | `+ 1` vor der Gnadenfrist | E, Ordnung | Literal | `TickOrder.Next` | eigen |
| `Testing/ReferenceMatchScenario.cs` | `MoveCadence` → `MoveCadenceFrames`, Feld `_moveCadence` (Befehlsrunde und Siegprüfung) | C | `tick % 15` | `tick % _moveCadence` | eigen |
| `Testing/ReferenceMatchScenario.cs` | `ProductionCadence` → `ProductionCadenceFrames`, `_productionCadence` | C | `tick % 30` | `tick % _productionCadence` | eigen |
| `Testing/ReferenceMatchScenario.cs` | `ReferenceMatchOptions.IncomeInterval`, `_incomeInterval` | C | `tick % Option` | Option in Bildern, einmal in `Record` skaliert | eigen |
| `Testing/ReferenceMatchScenario.cs` | `OrderRefreshTicks` → `OrderRefreshFrames`, `_orderRefresh` | H/zeit | 150 Ticks | skaliert | eigen |
| `Testing/ReferenceMatchScenario.cs` | `ReferenceMatchOptions.GraceTicks` | H/zeit | `tick + 1 + Option` | `tick + TickOrder.Next + grace` | eigen |
| `Testing/ReferenceMatchScenario.cs` | `tick == 1`, `DockCommand(…, 1, …)`, `+ 1` | H/ticks, Ordnung | Literal 1 | `TickOrder.Next` (3 Stellen) | eigen |
| `Testing/StressScenario.cs` | Reserve vor dem Ende des Abbaus | H/zeit | `Ticks - tick - 60` | `- world.Time.OriginalFrames(DrainReserveFrames)` | eigen |
| `Scripting/ScriptEngine.cs` | `EndGameDelayTicks` → `EndGameDelayFrames`, Eigenschaft `EndGameDelay` (3 Verwendungen: `VICTORY`, `DEFEAT`, `LOCALDEFEAT` in `Phase13.cs`) | A | 120 Ticks | `_world.Time.OriginalFrames(120)` | `ScriptEngine.cpp`, `FRAMES_TO_SHOW_WIN_LOSE_MESSAGE = 120`, `ScriptEngine::startEndGameTimer` |
| `Scripting/ScriptEngine.Actions.cs` | `QUICKVICTORY` `End(…, 1)` | E, Ordnung | Literal 1 | `TickOrder.Next` | `ScriptEngine.cpp`, `ScriptEngine::startQuickEndGameTimer` (`m_endGameTimer = 1`) |
| `Scripting/ScriptEngine.Actions.cs` | `RetargetInterval` → `RetargetIntervalFrames` | C | `_tick % 30 != Entity % 30` | `_tick % Time.OriginalFrames(30) != Entity % 30 * TicksPerOriginalFrame`; Periode und Faktor einmal vor der Schleife | eigen |
| `Scripting/ScriptEngine.Actions.cs` | `Forward`, `PresentationLatencyTicks` | B | `_tick + Option` | `_tick + _world.Time.OriginalFrames(Option)` | eigen |
| `Scripting/ScriptEngine.Phase13.cs` | `UpdateCaptureOrders` | C | `tick % 30 == 0` | `tick % Time.OriginalFrames(CaptureResendFrames) == 0`, einmal vor der Schleife | eigen |
| `Testing/MissionScenario.cs` | `MissionProbe.Run(settleTicks)` | H/zeit | `Tick < settleTicks` | `Tick < Time.OriginalFrames(settleTicks)` | eigen |
| `Testing/MissionScenario.cs` | `MissionProbe.Run(waitTicks)` | H/zeit | `killTick + waitTicks` | `killTick + Time.OriginalFrames(waitTicks)` | eigen |
| `Testing/MissionObjectiveDriver.cs` | `MissionObjectiveDriver.Run(settleTicks)` | H/zeit | Ticks | skaliert | eigen |
| `Testing/MissionObjectiveDriver.cs` | `MissionObjectiveDriver.Run(spacing)` | C | `nextAt = Tick + spacing` | `+ Time.OriginalFrames(spacing)` | eigen |
| `Testing/ObjectiveDriver.cs` | `ObjectiveDriver.Leaf.Cooldown` (zwei Vergleiche in `Round`) | H/zeit | `tick - last < Cooldown` | `tick - last < Time.OriginalFrames(Cooldown)`; damit sind alle 34 Abklingzeiten in `ObjectiveDriver.Leaves.cs` (31), `ObjectiveDriver.cs` (2 weitere Blätter) und `ObjectiveDriver.Commands.cs` (1) Bilder | eigen |
| `Testing/ObjectiveDriver.Commands.cs` | `ObjectiveDriver.PublishObjectives` | H/zeit | `> 150` | `> Time.OriginalFrames(ObjectiveExpiryFrames)` | eigen |

`SkirmishAi`: alle Fristen werden nur an Denkticks gesetzt und geprüft – keine Umrechnung je Tick; `Update` vergleicht zwei vorab berechnete Felder. In `StressScenario` heißen Rampe und Modulo-Rhythmen `…Steps` und sind **nicht** skaliert (§8.3).

## Anhang B: geprüft und bewusst unverändert

Was die Aufträge der Stufe 2 gelesen und nicht umgestellt haben, mit Grund – die Bewertung hinter den Resttreffern der Suchmuster (§10). Was bei 60 Hz anders ist, steht nicht hier, sondern in §5.

### B.1 Welt, Kataloge, Replay

| Datei | Symbol | Warum |
|---|---|---|
| `World/SimulationWorld.Vision.cs` | `VisionIntervalTicks` (`public const int = VisionIntervalFrames`) | Bleibt als Name für die Aufrufer außerhalb der Simulation (`CnCNew.Game/Live/LiveGame.cs`, `SimTool/PerfCommand.cs`, `Presentation.Tests/ObjectTests.cs`, `Simulation.Tests/Tests.Vision.cs`), die bei 30 Hz laufen. Phase 21: dort `world.VisionInterval` benutzen, dann die Konstante entfernen |
| `Templates/TurretInfo.cs` | `NeverScanTicks = 9999999` | Wächterwert („nie“), wird mit `CanScan` verglichen und nie skaliert (wie `SleepForever`) |
| `Templates/WeaponCatalog.cs` | `WeaponInfo.InstantSpeed` | Wächterwert, in Stufe 1 entschieden |
| `Templates/ObjectTemplate.cs` | `TemplateCatalog.GravityPerTickSquared` | Rechnet weiter je Zugriff (eine Dezimaldivision); ein Feld am Katalog änderte das Eingabemanifest. Die Welt selbst liest den Wert nicht mehr im heißen Pfad (`_significantHeight`) |
| `World/SimulationWorld.Acquisition.cs` | `MoodCheckOffset`: `(ulong)(tick + 1)` | Versatz der Hash-Eingabe (kein Nullwert im ersten Tick), weder Zeit noch Reihenfolge; im Kommentar vermerkt |
| `World/SimulationWorld.cs` | `RunUpdates` (`sleep < SleepNone`), `WakeAt` (`_tick + SleepNone`), `Register` (`NextWake = _tick + SleepNone`) | `SimulationTiming.SleepNone` ist der benannte kleinste Schlaf (Reihenfolge, Konvention §1 Fall (e)) |
| `World/SimulationWorld.cs` | `MaxUpdatesPerTick`, `MaxEventsPerTick` | Sturmgrenzen je Schritt, keine Zeit |

Geprüft, nicht gekoppelt (oder Format):

- `Commands/Commands.cs` (`SimCommand.Tick`, `SetTimerCommand.DelayTicks`/`RepeatTicks`), `Timers/SimTimer.cs`, `SimulationWorld.Execute`/`FireTimers` (`DueTick = _tick + set.DelayTicks`, `tick + timer.RepeatTicks`): Format (G). Die Werte sind Ticks der Rate der Welt; wer den Befehl erzeugt, rechnet in dieser Rate.
- `Events/SimEvent.cs`, `Replay/ReplayFormat.cs`, `Snapshots/WorldSnapshot.cs`, `SimulationWorld.Snapshot.cs`: Format (G), Stufe 1. `Replay/ReplayRunner.cs` (`RecordCheckpointInterval`, `VerifyIncrementalEvery`, `StopTick`): Laufbudgets, bleiben Ticks.
- `SimulationWorld.FiringTracker.cs`: `ContinuousFireCoastTicks` kommt skaliert aus dem Katalog; `StepFiringTrackers` fragt je Tick ab, der Vergleich arbeitet mit absoluten Ticks (nur Last).
- `SimulationWorld.Construction.cs` `AdvanceConstruction`: `ConstructionWork++` je Arbeitstick gegen `BuildTicks`, das der Katalog skaliert – gleiche Bauzeit, feinere Gesundheitsstufen.
- `SimulationWorld.cs`: `SpawnSetup.MinLifetimeTicks`/`MaxLifetimeTicks`, `BaseRegenDelayTicks`, `production.Enqueue(…, BuildTicks)`; `SimulationWorld.BattlePlan.cs` `ParalyzeTroops(…, untilTick)`; `SimulationWorld.StructuresAndWorld.cs` `CreateFromListWithLifetime(…, lifetimeTicks)`; `SimulationWorld.CrateServices.cs` `SetPowerSabotagedUntil`; `TunnelTracker.AddRider(…, tick)`: Katalogwerte bzw. absolute Ticks, die der Aufrufer (Module) liefert und die die Welt nur durchreicht.
- `SimulationWorld.Collision.cs` (`Fixed.FromInt(150)`), `SimulationWorld.CreationLists.cs` (`formation % 2`, 90 Grad): Strecken und Geometrie. `Terrain/`, `Numerics/`, `Views/`, `State/`, `Ids/`, `Random/`: keine Zeit (die Treffer der Suchmuster sind Hash-Konstanten `z >> 30`, Reihenglieder in `FixedTrig`, Enum-Nummern).
- `Templates/ObjectTemplate.cs`, `SpecialPowerCatalog.cs`, `UpgradeCatalog.cs`, `ObjectCreationListCatalog.cs`, `TurretInfo.cs`: alle Umrechnungen laufen seit Stufe 1 über den Parameter `timing`; kein Literal offen.

### B.2 Module A–M

Mit Stufe 1 erledigt (nachgeprüft): `ParkingPlaceBehavior.HealRateFrames` und die Heilmenge; `EmpData` `Lifetime` (Ersatzwert 1 Bild), `LeafletData` `Delay` (1), `BunkerBusterData` `CrashThroughBunkerFXFrequency` (4), `SpectreData` `HowitzerFiringRate` (10) und `StrafingIncrement`, `PropagandaTowerData` `DelayBetweenUpdates` (100), `MinefieldData` `CreatorDeathCheckRate`; `BridgeData` `Lateral/VerticalScaffoldSpeed`; `TransportContain` (Regeneration), `PropagandaTowerBehavior.Give`, `MinefieldBehavior.Update` (`DegenPerSecond`), `JetAIUpdate.CircleDeadAirfield`, `CountermeasuresBehavior.LaunchVolley`, `ParticleUplinkData` `ManualDrivingSpeed`/`ManualFastDrivingSpeed`, `ParticleUplinkCannonUpdate.Pulse` (`seconds`), `DockMachine.ClearanceTimeoutFrames`, `ModuleConfigReader.Ticks`, `JetAIUpdate.PauseBeforeTakeoff` (`TickOrder.Next`).

| Datei | Symbol | Grund |
|---|---|---|
| `AirWeaponModules.cs` | `AirWeaponModules.FixedTicks` (`Blast…Delay`) | ungerundete Dauer wie im Original (`parseDurationReal`); die Rasterung geschieht jetzt im Vergleich (Anhang A.2) |
| `AirWeaponModules.cs` | `NeutronMissileData.MaxTurnRate`-Ersatzwert | in Stufe 1 bewusst unverändert (jeder Wert ab π rastet sofort ein) |
| `AirWeaponModules.cs` | `SpectreGunshipUpdate.Orbit` (`world.Tick % HowitzerFiringRateTicks == 0`) | Periode aus den Daten (skaliert), Phase 0: bei jeder Rate derselbe Spielzeitpunkt |
| `CleanupAndSpyModules.cs` | `SpyVisionSpecialPower.Apply` | Summe und Deckel von Dauern, die alle auf dem 30er-Raster liegen: bei 60 Hz exakt das Doppelte (die Vermutung des Inventars „kleinerer Rundungsfehler“ gilt mit der Rasterrundung der Stufe 1 nicht mehr) |
| `CleanupAndSpyModules.cs` | `SpyVisionUpdate.ActivateSpyVision`, `AssistedTargetingUpdate.Update` (`Math.Max(…, SleepNone)`) | `SleepNone` ist hier der kleinste Schlaf des Planers (ein Aufwachen muss in der Zukunft liegen), keine Dauer – Reihenfolge |
| `CombatModules.cs` | `FireWeaponUpdate.Update` | fragt jeden Tick ab; die Schusszeitpunkte sind absolute Ticks aus Daten – nur Last |
| `ContainChainModules.cs` | `RequestPayload`/`EnterPayload`, `OverlordContain.Update` | „ein Tick später“ ohne Literal (Reihenfolge) bzw. Nachführen je Tick – nur Last |
| `ContainModules.cs` | `GarrisonContain.HealOne` | `MaxHealth / TimeForFullHealTicks` je Tick: skaliert mit der umgerechneten Dauer von selbst |
| `DockModules.cs` | `DockUpdateBase.Update`, `AutoDepositUpdate.OnSpawn` (`Math.Max(_depositOnTick, Tick + SleepNone)`) | Abfrage je Tick ohne Literal; kleinster Schlaf des Planers |
| `DockMachine.cs` | `ProcessDock` mit `ActionDelay` 0 | ruft die Aktion des Docks jeden Tick; was die Aktion je Aufruf bewirkt, liegt in den Dock-Modulen (N–Z: Reparatur, Versorgung) – dort bewerten |
| `HackerAndAssaultModules.cs` | `AssaultTransportAIUpdate.Update` | schläft nie, keine Tickliterale – nur Last und Reaktionslatenz |
| `JetAIUpdate.cs` | `Update` (`return SleepNone`) | die Zustandsmaschine läuft jeden Tick; ergebnisrelevant nur über die Integration (Klasse E, §5.2) |
| `LastBlockerModules.cs` | `SupplyWarehouseCripplingBehavior.OnDamage` (`Math.Max(SuppressTicks, SleepNone)`) | kleinster Schlaf des Planers |
| `MatchModules.cs` | `BaseRegenerateUpdate.OnDamage` (`Math.Max(BaseRegenDelayTicks, SleepNone)`) | kleinster Schlaf des Planers |
| `FlightModules.cs` | `ParticleUplinkCannonUpdate.Initiate` (`Math.Max(world.Tick, 1)`) | 0 heißt „kein Einsatz“; Wächter, keine Dauer |
| `FlightModules.cs` | `DeliverPayloadAIUpdate.Strafe` | fordert je Update einen Schuss an; den Takt erzwingt die Waffe (`FireCurrentWeaponAtPosition`, Welt) – nicht in dieser Datei zu lösen |
| `FlightModules.cs` | `ParticleUplinkData` (`long BeginChargeTicks = 0` usw. als `record`-Vorgabe) | Vorgabe 0, kein stiller Tickwert; nur `Parse` und Tests erzeugen den Datensatz |
| `Bodies.cs`, `BuiltInModules.cs`, `CreateCrateDie.cs`, `JetAIUpdate.Flight.cs` (bis auf §5.2), `ModuleBindings.cs`, `CollideModule.cs`, `ModuleFramework.cs` | – | keine Tickwerte: Zufall über Anteile und Indizes, Bindungstabellen, Schnittstellen (`IsSignificantlyAboveTerrain` ist seit Stufe 1 ratenfest, `AdvanceConstruction` liegt in der Welt) |

### B.3 Module N–Z

| Datei | Symbol | Befund |
|---|---|---|
| `Phase12Modules.cs` | `TechBuildingData.PulseRateTicks` (F/tick) | kein Verbraucher in der Simulation; über `ModuleConfigReader.Ticks` schon je Rate |
| `Phase12Modules.cs` | `HordeData.Parse` (`UpdateRate`), `HordeUpdate.Update` (Infanterie schläft `UpdateRateTicks`) | Ersatzwert seit Stufe 1 in Bildern; Schlafdauer ist ein Katalogwert |
| `Phase5BlockerModules.cs` | `StealthDetectorData.Parse` (`DetectionRate`, Ersatz 1 Bild) | Stufe 1 |
| `Phase5BlockerModules.cs` | `StealthDetectorUpdate.Update`: `return Math.Max(UpdateRateTicks, SleepNone)` | **bewusst ein Tick:** die Entdeckung hält `UpdateRateTicks + TickOrder.Next`; eine Untergrenze von einem Bild würde bei `DetectionRate = 0` zwischen zwei Scans eine Lücke von einem Tick öffnen. Nur bei `DetectionRate 0` wirksam |
| `Phase5BlockerModules.cs` | `FlammableUpdate.Update`: `return Math.Max(soonest - now, SleepNone)`; `WorldEffectModules.cs` `WeaponBonusUpdate.Update` ebenso | Wächter des Planers (nie 0 schlafen) auf einer Differenz absoluter Ticks, keine Zeit |
| `Phase5Modules.cs` | `StealthData.MoveThresholdSpeed`, `AIUpdateData.DefaultMoodCheckFrames`, `UpdateTask` (`RepairRatePerSecond / TicksPerSecondFixed`) | Stufe 1 |
| `Phase5Modules.cs` | `StealthUpdate.MarkDetected`: `Math.Max(ticks, SleepNone)` | Untergrenze eines Werts, den der Aufrufer aus Rate + Reihenfolge bildet; bleibt ein Tick |
| `Phase5Modules.cs` | `StealthUpdate._framesGranted` (Zähler je Tick) | kein Aufrufer übergibt eine Dauer > 0 (`ReceiveGrant(…, 0)`); ein Aufrufer mit Dauer muss Ticks übergeben |
| `Phase5Modules.cs` | `StealthUpdate.ConditionActive` (`LastFireTick == world.Tick`) | Vergleich „im selben Tick“, braucht keine Markierung (Konvention Fall e) |
| `Phase5Modules.cs` | `ProductionUpdate.Update` (`RemainingTicks - 1` je Tick) | Tickzähler über einen Wert des Aufrufers in Ticks |
| `PhysicsAndMinefieldModules.cs` | `GenerateMinefieldBehavior.Update` (Abfrage je Tick) | nur Last |
| `ProductionExitModules.cs` | `QueueProductionExitUpdate` (`_delay = ExitDelayTicks`, `_delay--`) | Zähler mit Periode genau N (frei, sobald 0), Katalogwert in Ticks |
| `ProjectileModules.cs` | `DefaultMaxLifespanFrames`, `FlightPathAdjustDistPerSecond`, `InitialVelocity`, `KillSelfDelay` (Ersatz 3 Bilder) | Stufe 1 |
| `RailroadModules.cs` | `WaitAtStationTime` (Ersatz 150 Bilder), `SpeedMax`/`KillSpeedMin`/`RunningGarrisonSpeedMax` | Stufe 1 |
| `RepairModules.cs` | `RepairDockData.Parse` (`TimeForFullHeal`, ungerundet) | Stufe 1 (`DurationMillisecondsToFixedTicks`) |
| `RetailMatchModules.cs` | `OverchargeBehavior.Update`, `RebuildHoleBehavior.Update` (`… / TicksPerSecondFixed`) | Stufe 1 |
| `RetailMatchModules.cs` | `RebuildHoleBehavior._workerWait` (`--` je Tick bis 0) | Katalogwert in Ticks, Periode genau N |
| `RetailMatchModules.cs` | `RadarUpdate.IsExtendComplete` (`tick > _extendDoneTick`) | Vergleich ohne Literal (Reihenfolge) |
| `SlaveModules.cs` | `UpdateRateFrames`, `RecentFireFrames`, `DoRepairLogic` (`RepairRatePerSecond / TicksPerSecondFixed`) | Stufe 1 |
| `SlaveModules.cs` | `SlavedUpdate.Update` (`_framesToWait--`, Handeln bei 0 im selben Update) | Periode genau N, kein N + 1 |
| `SlaveModules.cs` | `MobMemberData.CatchUpCrisisBailTime` (D) | Zahl von **Prüfungen**, nicht von Bildern; mit skaliertem `CheckIntervalFrames` fällt dieselbe Zahl Prüfungen in dieselbe Spielzeit – bleibt unskaliert |
| `SlaveModules.cs` | `SpawnPointProductionExitUpdate.Revalidate` (`world.Tick > tick`) | Vergleich ohne Literal (Reihenfolge) |
| `SpawnModules.cs` | `SpawnBehavior.UpdateRateFrames`, `_workTick`, Schlafrückgabe | Stufe 1; das Polling toter Spawns liegt damit im Bildraster |
| `SpecialAbilityStaging.cs` | `StageTicksLeft` (F/tick), `_prepFrames`/`_animFrames` (`--` je Tick) | Zähler über Katalogwerte in Ticks; die Präsentation liest Ticks (Phase 21) |
| `SpecialPowerModules.cs` | `AvailableOnFrame` (F/tick), `BuildCompleteModule` (`Tick + SleepNone`) | absoluter Tick für das HUD (Phase 21); Reihenfolge über `SleepNone` |
| `SpecialsModules.cs` | `GrantStealthData.RadiusGrowRate` | Stufe 1 |
| `StructureModules.cs` | `MinBurstDelay` (Ersatz 9999 Bilder) | Stufe 1 |
| `StructureModules.cs` | `DeployStyleAIUpdate` (`_frameToWait = now + UnpackTicks …`, `= 0`) | absolute Ticks aus Katalogwerten; 0 ist der Wächter „kein Zeitgeber“ |
| `SupplyModules.cs` | `SupplyCarrierAI.Update` (`Math.Min(baseSleep, SleepNone)`) | läuft je Tick, solange der Automat arbeitet – nur Last; die Aktionsfristen sind Katalogwerte |
| `TransportModules.cs` | `HelicopterSlowDeathData` (vier Werte je Sekunde), `FixedTicks`, `SelfSpinUpdateAmount / TicksPerSecondFixed` | Stufe 1; der letzte Ausdruck ist auch bei 60 Hz richtig (`_selfSpin` ist Radiant je Tick: Betrag / 30 je Bild = Betrag / Rate je Tick) |
| `TransportModules.cs` | `TunnelContainData` (`TimeForFullHeal` fehlt → 0) | Ersatzwert 0, nichts zu skalieren (Nebenbefund des Inventars: Original 1 Bild) |
| `VehicleModules.cs` | `BattleBusData.ThrowForce`, `AnimationSteeringUpdate.MinTransitionTicks` (F/tick) | Stufe 1 bzw. Katalogwert für die Präsentation |
| `WorldEffectModules.cs` | `OCLLifetimeMaxCap` (Ersatz 1000 Bilder), `ScanDelayTime` (Ersatz 30 Bilder) | Stufe 1 |
| `WorldEffectModules.cs` | `DynamicShroudClearingRangeUpdate`: `Math.Max(1, GrowTime)`/`Math.Max(1, ShrinkTime)`, `_stateCountdown--` | Schutz vor Division durch 0 (kein Zeitwert); Tickzähler über Katalogwerte |
| `WorldEffectModules.cs` | `OCLUpdate` (`_nextCreation == 0`), `HelicopterSlowDeathBehavior._hitGroundTick == 0` | Wächterwerte, keine Zeit |
| `UpgradeMux.cs`, `StructuresAndWorldModules.cs` | – | keine Zeitkopplung |

### B.4 Skripte, Mission, Skirmish, Szenarien

Bereits in Stufe 1 erledigt und nachgeprüft: `SET_TIMER`/`SET_RANDOM_TIMER` (Zufall in Bildern, dann skaliert), `SecondsToFrames` (aufrunden auf Bilder, dann skalieren – `SET_MILLISECOND_TIMER`, `SET_RANDOM_MSEC_TIMER`, `ADD_TO_MSEC_TIMER`, `SUB_FROM_MSEC_TIMER`, `SCRIPTING_OVERRIDE_HULK_LIFETIME`), `*_FOR_FRAMECOUNT`, `teamInitialIdleFrames`, erster Auswertungstick `NameHash % 61` Bilder, `DelayEvaluationSeconds`, `NAMED_SET/ADD_SPECIAL_POWER_COUNTDOWN`, Knopfjagd `max(ScanTicks, eine Sekunde)`, `UpdatePendingStructures` (`tick % TicksPerSecond`), `SupplyScanSeconds`, `UpdateReadyTeam` (60 s, `AIPlayer.cpp`, `AIPlayer::checkReadyTeams`), `UNIT_EMPTIED` (`frame - TickOrder.Next`).

Resttreffer der Suchmuster, jeder einer dieser Fälle: Konstanten in Bildern (`…Frames = N`) und `SupplyScanSeconds`; skalierte Rhythmen und Fristen (`% _fireInterval`, `tick - x < Time.OriginalFrames(…)`); Ordnung (`TickOrder.Next`); Laufbudgets und Standardwerte der Optionen (`108_000`, `216_000`, `1_000`, `300`, `2_700`, `4500`, `150`, `600`, `15`, `60` – Einheiten in §8.1); Blatt-Abklingzeiten in `ObjectiveDriver*.cs` (Literale 15 bis 300 als viertes Argument von `Leaf`/`Destroy`: Bilder, in `Round` skaliert); Stresstest (`…Steps`, Timerverzögerungen, Prozentwerte 30/45, Wertebereiche 20/30 – §8.3 bzw. keine Zeit); Abstände, Radien, Mengen (`Fixed.FromInt(30/45/90/450/600/900/3600/…)`, `MaxAttackThreshold = 30`, Ringabstände 60 bis 360, `GuardMargin = 150`, `MaxHealth = 300`: Welteinheiten oder Stückzahlen); Daten der Fixture (`MinLifetime`/`MaxLifetime` in Millisekunden, `BuildTime = 1` s: werden beim Katalogbau umgerechnet); Wächter und Zustand (`FramesToWait = -1`, `_hulkLifetimeOverrideTicks = -1`, `PresentationLatencyTicks = 0`, `CombatIntervalTicks = 3`); Zufall (`LogicRandom.Range` in `SET_RANDOM_TIMER`/`SET_RANDOM_MSEC_TIMER`: in Bildern bzw. Sekunden gezogen, dann skaliert; in `Phase13.cs`: Positionen und Winkel).

Nebenbefunde (keine Ratenfehler, nicht korrigiert): `SUB_FROM_MSEC_TIMER` zieht `ceil(+x)` ab, das Original addiert `ceil(−x)`; `SCRIPTING_OVERRIDE_HULK_LIFETIME` rundet auf, das Original schneidet ab; `SUPPLY_SOURCE_ATTACKED` prüft 10 s, das Original addiert `SCAN_RATE = 10` auf Bilder (`AIPlayer.cpp`, `AIPlayer::isSupplySourceAttacked`).

## 14. Phase 21: Präsentation, Spielschicht, UI, Ton, Client, Werkzeuge und Tests

Stand: 6. Oktober 2026, Endstand der Phase 21 (`docs/HANDOFF-PHASE21.md`) mit dem Nachtrag vom selben Tag (§14.9: Tests in Spielzeit, `SaveGame` mit Rate, restliche Zeitmaß-Tests, Werkzeugausgabe). Dieser Abschnitt ist der Bericht für alles außerhalb der Simulation; er schließt die Liste aus §6 ab. Ziel in einem Satz: nichts außerhalb der Simulation nimmt an, dass ein Tick 1/30 s dauert; bei 30 Hz bleibt jedes Bild, jeder Ton und jede Prüfsumme gleich; bei 60 Hz dauert alles Sichtbare und Hörbare in Sekunden gleich lang.

**Was Phase 21 nicht ist:** eine Abnahme. Prüfsummen belegen Reproduzierbarkeit, der Zeitmaß-Test belegt Dauern. Ob irgendetwas bei 60 Hz richtig aussieht oder klingt, hat kein Mensch gesehen oder gehört (§14.7), und die Simulation selbst ist bei 60 Hz unbewertet (§5, Phase 23). Der Standard bleibt 30 Hz.

### 14.1 Zwei Begriffe, zwei Namen, eine Uhr

- **Originalbild** (1/30 s): die Einheit, in der das Original Client-Inhalte verfasst hat. Außerhalb der Simulation gibt es dafür genau eine Konstante, `CnCNew.Data.OriginalFrame.PerSecond` (`src/CnCNew.Data/OriginalFrame.cs`; `CnCNew.Data`, weil `Presentation`, `Ui`, `Audio`, `Game` und der Client es alle referenzieren). Jede frühere 30 außerhalb der Simulation ist entweder durch sie ausgedrückt (`FxValueReader.FramesPerSecond`, `RtsCamera.FrameRate`, `CameraSettings.FramesPerSecond`, `OrderHintBook.ClientFramesPerSecond`, `WindowTransitionHandler.FramesPerSecond`, `WaterAppearance.RiverVScrollPerSecond`, `InGameUiSettings`, `EvaCatalog.Frames`, die Kabinendämpfung in `ObjectLayerCore`) oder entfallen. `SimulationRate.OriginalFramesPerSecond` bleibt die Konstante der Simulation und wird außerhalb nicht mehr benutzt.
- **Simulationsrate:** kommt aus der Welt (`world.Time`) bzw. aus dem Katalog (`GameData.Rate`). `GameData.Load(…, rate:)` nimmt sie als Einstellung der Sitzung; `GameData.DefaultSessionRate` (`SimulationRate.Original`) ist die eine Stelle außerhalb der Simulation, die eine Rate nennt (der Standard, den Phase 23 umstellt). Der Client reicht `--tick-rate N` durch.
- **Uhr der Präsentation:** `CnCNew.Presentation.GameClock` (Tick, Rate, Interpolationsanteil), bereitgestellt von `LiveGame.Clock`. Sie wird ganzzahlig geführt (Tick und Rate), nie aus Bildzeiten summiert; `OriginalFrames` und `IsOriginalFrameBoundary` rechnen ganzzahlig, `Seconds` ist `(Tick + Anteil) / Rate`. Sie steht bei Pause und läuft mit der Spielgeschwindigkeit, weil sie am Tick hängt. Wandzeit bleibt die Uhr für Shell, Mauszeiger, Kamera, Wasser, Befehlsmarken.

Zwei Muster tragen den ganzen Umbau, beide bei 30 Hz die Identität:

1. **Dauern in Originalbildern, verglichen in Ticks der Welt:** `world.Time.OriginalFrames(NFrames)` an der Verwendung (Mündungsfeuer, Heilsymbol, Jubel, Unter-Beschuss-Sperre, Platzhalterdauern, Skriptblenden, Kameratermine).
2. **Was das Original einmal je Bild fortschreibt, läuft auf dem Originalbild-Raster:** auf den Ticks, die ein Originalbild beginnen (`world.Time.IsOriginalFrameBoundary(tick)` bzw. `GameClock.BeginsOriginalFrame` für Uhren, die der Welt einen Tick nachlaufen oder eingefrorene Schritte mitzählen): Partikel und Lichter, die Hüllkurve des Blitzes, das Fahrwerk, Texte und Bildfilter der Mission, die Meldungsliste, der Eva-Ansager, die Daueraufträge.

### 14.2 Der Beweis bei 30 Hz

Vor jeder Änderung wurden alle Darstellungsprüfsummen am Stand `4c45764` zweimal erzeugt (welche überhaupt reproduzierbar sind) und nach dem Umbau erneut. Erzeuger: `PresentationTool` (Release) direkt, `UiRenderTool`, der Stride-Client im Rauchtest (`--smoke-test --capture`, `--audio-capture`, `--filmstrip`), Pixelprüfsummen wie `scripts/capture-world.ps1` (SHA-256 über die dekodierten Pixel).

| Prüfsumme | reproduzierbar (zwei Läufe vorher) | vorher | nachher | Ergebnis |
|---|---|---|---|---|
| `PresentationTool fx-audit` (ganze Ausgabe, darin die Prüfsumme des Ausführungslaufs) | ja | `5504C152200DB149…` | `5504C152200DB149…` | gleich |
| `PresentationTool object-audit` (ganze Ausgabe) | ja | `032CF63DD4EC7BE7…` | `032CF63DD4EC7BE7…` | gleich |
| `PresentationTool terrain-audit` (ganze Ausgabe ohne die Zeitzeile) | ja | `DC60C6DD9447198E…` | `DC60C6DD9447198E…` | gleich |
| `scenario-audit --name capture-building --every 30` (ganze Ausgabe) | ja | `BC7DD2A2F453D2EC…` | `BC7DD2A2F453D2EC…` | gleich |
| `scenario-audit --name tank-duel --every 10` (ganze Ausgabe) | ja | `8FD85E76B599D514…` | `8FD85E76B599D514…` | gleich |
| `scenario-audit --name jet-strafe --every 30` (ganze Ausgabe) | ja | `67DE0DFB83346317…` | `67DE0DFB83346317…` | gleich |
| `sync-perf --map "Maps/Alpine Assault/Alpine Assault.map" --players 2 --ticks 4000 --hud --digest` (Digest über jedes Bild, Nebel, Effekte, Tonhinweise) | ja | `68CF838B1D93F711…` | `68CF838B1D93F711…` | gleich |
| `UiRenderTool`: 162 PNG (Datei- und Pixelprüfsumme), `manifest.txt`, `settings.json` (164 Dateien) | 164 von 164 | – | – | 164 von 164 gleich |
| Client `--capture`: `hud-tooltip` (Pixel) | ja | `3D3AE9D8D938479A…` | `3D3AE9D8D938479A…` | gleich |
| Client `--capture`: `match-base-late` (Pixel) | ja | `A8998097CF690823…` | `A8998097CF690823…` | gleich |
| Client `--capture`: `match-combat` (Pixel) | NEIN | `DDEFB9DC63C5DADE…` | `4A44682DF6956FC5…` | anders (nicht reproduzierbar) |
| Client `--capture`: `match-cursor-move` (Pixel) | ja | `2D22C665A8DA246A…` | `2D22C665A8DA246A…` | gleich |
| Client `--capture`: `match-fog-unexplored` (Pixel) | ja | `C1BB7E7FE3DEAF87…` | `C1BB7E7FE3DEAF87…` | gleich |
| Client `--capture`: `match-selected` (Pixel) | ja | `7D298221AA816C7C…` | `7D298221AA816C7C…` | gleich |
| Client `--capture`: `match-start` (Pixel) | ja | `6567166959F06212…` | `6567166959F06212…` | gleich |
| Client `--capture`: `match-waypoints` (Pixel) | ja | `E45FAC79518BE708…` | `E45FAC79518BE708…` | gleich |
| Client `--capture`: `mission-intro-45` (Pixel) | ja | `99FCC5CE4E88C394…` | `99FCC5CE4E88C394…` | gleich |
| Client `--capture`: `mission-start` (Pixel) | ja | `E2D718FE99350D30…` | `E2D718FE99350D30…` | gleich |
| Client `--capture`: `mission-usa01-300` (Pixel) | ja | `11B8829E8754096C…` | `11B8829E8754096C…` | gleich |
| Client `--capture`: `scenario-capture` (Pixel) | ja | `20AC3FA25DACB82C…` | `20AC3FA25DACB82C…` | gleich |
| Client `--capture`: `scenario-capture-1000` (Pixel) | ja | `620006FAC1AED38D…` | `620006FAC1AED38D…` | gleich |
| Client `--capture`: `scenario-jet` (Pixel) | ja | `BDF6C7DE55446DCE…` | `BDF6C7DE55446DCE…` | gleich |
| Client `--capture`: `scenario-tank-duel` (Pixel) | ja | `441E02FE3C062544…` | `441E02FE3C062544…` | gleich |
| Client `--capture`: `scenario-tank-duel-90` (Pixel) | ja | `37C25571CF734340…` | `37C25571CF734340…` | gleich |
| Client `--capture`: `viewer-crusader` (Pixel) | ja | `BBFFF06CC89FD548…` | `BBFFF06CC89FD548…` | gleich |
| Client `--audio-capture`: `audio-capture-building/audio.wav` | ja | `7DB0DF9B6C46B48F…` | `7DB0DF9B6C46B48F…` | gleich |
| Client `--audio-capture`: `audio-match-autoplay/audio.wav` | ja | `775A7EE122429A32…` | `775A7EE122429A32…` | gleich |
| Client `--audio-capture`: `audio-mission/audio.wav` | ja | `9B5098CDC8A0BDF7…` | `9B5098CDC8A0BDF7…` | gleich |
| Client `--audio-capture`: `audio-tank-duel/audio.wav` | ja | `B9C6F66CF73D4BF5…` | `B9C6F66CF73D4BF5…` | gleich |
| Client `--filmstrip`: capture-building, tank-duel, jet-strafe, Pixelprüfsummen der Bilder (77 Dateien) | 77 von 77 | – | – | 77 von 77 gleich |

Dazu die eingecheckten Messlatten, die `tests/CnCNew.Game.Tests` (Release, mit `--content-dir`) prüft und die unverändert bestehen: die 54 Screen-Hashes (`baselines/screens.txt`, `ShellTests`) und der HUD-Drawlist-Hash (`baselines/hud-drawlist-1280x720.sha256`, `HudTests`). Die Aufnahmeszenen sind die von `scripts/capture-world.ps1` (Auswahl ohne die reinen Straßenszenen und drei Modellbetrachter-Szenen, dazu längere Laufzeiten der gestellten Demos, zwei weitere Missionsbilder und die Tooltip-Szene von `scripts/capture-text.ps1`); die Bildfolgen sind die von `scripts/capture-scenarios.ps1`; die Tonmitschnitte: Gefecht mit `--autoplay` (3.000 Bilder), `capture-building` (1.500), `tank-duel` (300), Mission `GC_ChemGeneral` (900).

Nicht als Beweis tauglich: `match-combat` (Autoplay, 7.500 Bilder) – drei Läufe, drei Pixelprüfsummen, schon am Ausgangsstand (wie `scripts/capture-world.ps1` für die Autoplay-Szenen dokumentiert). Die Zeile „build time total (not deterministic)“ von `terrain-audit` und die Setup-Zeit von `sync-perf` sind aus dem Vergleich genommen.

Simulation: `SimTool input-manifest check` identisch; `SimTool reference-manifest check --set quick` (9 Läufe) nach jedem Arbeitspaket identisch, `--set full` am Ende (50 Läufe identisch). In `src/CnCNew.Simulation` ist genau eine Stelle geändert: der Alias `SimulationWorld.VisionIntervalTicks` ist entfernt (§6 Nr. 1).

### 14.3 Die Liste aus §6, Punkt für Punkt

| Nr. | Stand | Was getan wurde |
|---|---|---|
| 1 | erledigt | `LiveGame`, `SimTool perf`, `ObjectTests`, `Tests.Vision` benutzen `world.VisionInterval`; `SimulationWorld.VisionIntervalTicks` ist entfernt |
| 2 | erledigt | `GameData.Load(…, rate:)`; `GameData.Rate`; `DefaultSessionRate` als benannter Standard; Client `--tick-rate N`, `PresentationTool scenario-audit`/`sync-perf --tick-rate N`, `GameShell.CreateLoading(…, rate:)` |
| 3 | erledigt (Nachtrag) | `SaveGame` **Version 2** schreibt `tickRate` in die Hülle und prüft sie vor dem Fingerabdruck; Version 1 wird über die Versionsprüfung abgelehnt und nie umgedeutet; Ladebildschirm und Pausenmenü zeigen Spielzeit (`SaveGameHeader.GameTime`, m:ss) statt des Ticks (§14.9.3) |
| 4 | erledigt | jede der 14 + 1 + 3 Verwendungen entschieden: Simulationsticks über `world.Time` (`LiveGame`, `SuperweaponTimers`, `MissionPresentation`, `SyncPerfCommand`) oder Originalbild (`EvaCatalog`, `LiveMission`, `SelectionFlash`); keine Verwendung von `SimulationRate.OriginalFramesPerSecond` mehr in `CnCNew.Game`, `CnCNew.Presentation`, `CnCNew.Ui`, `CnCNew.Client.Stride` (Quelltexttest, §14.6) |
| 5 | geprüft, kein Leser | die Sabotagedauer im Ereigniswert liest außerhalb der Simulation niemand (Suche nach den drei Ereignisarten in `CnCNew.Game` und `CnCNew.Presentation`: kein Treffer auf den Wert); wer ihn künftig liest, rechnet mit `world.Time` |
| 6 | erledigt | `UnitConditionTracker`: `Life < world.Time.OriginalFrames(100)`, Fallschwelle je Originalbild |
| 7 | erledigt | `MissionPresentationDirector.CounterForDisplay` rechnet Timerwerte in Originalbilder; `MissionTextOverlay.NamedTimers` und `SuperweaponTimers` teilen durch `OriginalFrame.PerSecond` bzw. benutzen `ToWholeSeconds` |
| 8 | erledigt | „presentation latency N original frames (S s)“; „world tick T at R Hz“ |
| 9 | erledigt | Bildzahlen der Skripte sind Originalbilder und werden beim Annehmen umgerechnet (`docs/SCRIPT-ENGINE.md` §7a, Tabelle) |
| 10 | erledigt | `LiveMission.ClientPresentationLatencyFrames` (umbenannt; der Wert ist und war in Bildern) |
| 11 | geprüft | Türuhr, Lenkhaltezeit, Baufortschritt und Bereitschaft der Spezialkräfte werden als Ticks gegen Ticks verglichen (Anteile, keine Sekunden); die einzige Sekundenanzeige (`SuperweaponTimers`) rechnet über `world.Time` |
| 12 | erledigt (Nachtrag) | `CampaignTool drive`/`audit` schreiben die Spielzeit der Ticks einer Kartenzeile in eine eigene Zeile darunter und das Budget als Spielzeit; `playthrough` in Klammern hinter dem Tick; die ausgewerteten Zeilen sind bytegleich (§14.9.4). `SkirmishAiStats.AttackTicks` gibt kein Werkzeug aus |
| 13 | erledigt | `ScenarioAudit` (ein Abtastwert je Originalbild, Ereignisticks und Turmzeiten beim Einlesen umgerechnet, `--tick-rate`), `SyncPerfCommand` (Schritt aus der Rate der Welt), `UiRenderTool` (Schritte `1.0 / OriginalFrame.PerSecond`, Budgets `WholeSecondsToTicks`, Timerwert in Originalbildern) |
| 14 | erledigt (Schritte) | kein `1.0 / 30` mehr in den Tests außerhalb der Simulation; `tests/Shared/TestTime.cs` (§14.5) |
| 15 | unverändert | `SimTool perf`: `catchup30`/`catchup60`/`core30_percent` nennen ihre Rate im Namen (Umstellung des Standards, Phase 23) |
| 16 | erledigt | `StandingOrders.Update` und die Befehle von `LiveScenario` laufen je Originalbild; bei 30 Hz dieselben Befehle (Szenen-Audits, `ScenarioTests`, `sync-perf`-Digest unverändert) |

Über die Liste hinaus (aus `docs/TICKRATE-AUDIT.md` §6 und §7.1): die Aufholgrenze von `LiveGame` als Zeitbudget (8 Originalbilder), die Effektuhr auf dem Originalbild-Raster, die Drosselung der Schadenseffekte auf einer Uhr, Mündungsfeuer/Feuerzustand/Heilsymbol/Jubel/Blitz/Fahrwerk (`docs/PRESENTATION-OBJECTS.md` §11), Eva und Flutschutz (`docs/GAME-AUDIO.md`), Meldungsliste und Superwaffenliste (`docs/GAME-HUD.md` §19), die Missionspräsentation (`docs/SCRIPT-ENGINE.md` §7a), `DemoScenario.Frames`/`ScenarioOrder.Frame` (Demos in Originalbildern), der Rauchtest-Schritt des Clients (ein Bild = ein Originalbild bei jeder Rate) und die Skripte `capture-world.ps1`, `capture-scenarios.ps1`, `audit-scenarios.ps1` (`-TickRate`).

**Drei bewusste Abweichungen vom Wortlaut des Auftrags:**

1. *Fahrwerk:* nicht „mit der Schrittweite gerechnet“, sondern auf dem Originalbild-Raster gestuft; Begründung und Messung in `docs/PRESENTATION-OBJECTS.md` §11.
2. *Letterbox-Blende:* nicht an Wandzeit, sondern auf der Präsentationsuhr in Originalbildern; Begründung in `docs/SCRIPT-ENGINE.md` §7a.
3. *Rauchtest-Schritt:* nicht „ein Tick“, sondern ein Originalbild (1/30 s) je gezeichnetem Bild – bei 30 Hz dasselbe, bei 60 Hz zwei Ticks. So bedeuten `--frames`, `--every`, Eingabeskripte und alle Aufnahmeskripte bei jeder Rate dieselbe Spielzeit, und Aufnahmen bei 30 und 60 Hz zeigen denselben Zeitpunkt.

### 14.4 Zeitmaß-Test bei 60 Hz (und 120 Hz)

Je Element dieselbe Szene je Rate, Dauer in Ticks der jeweiligen Welt gemessen und mit ihrer Rate in Sekunden umgerechnet; Toleranz ein Originalbild (0,0333 s). `ObjectTests.TimeScale.cs` (synthetische Fixture je Rate), `TimeScaleTests.cs` und `AudioDirectorTests` (synthetische Mission bzw. Fixture je Rate), `TimeScaleTests` mit `--content-dir` (Realdaten, `LiveScenario` bei 30, 60 und 120 Hz); im Nachtrag dazu `FxTests.TimeScale.cs`, `UnitConditionTests.TimeScale.cs`, je ein Test in `JetOrderTests` und `CaptureFlashTests` (alle `Presentation.Tests`) und vier weitere in `TimeScaleTests` (`Game.Tests`).

| Element | 30 Hz | 60 Hz | 120 Hz |
|---|---|---|---|
| Mündungsfeuer nach einem Schuss | 0,1333 s | 0,1333 s | 0,1333 s |
| `FIRING_A` nach einem Schuss | 0,0333 s | 0,0333 s | 0,0333 s |
| Heilsymbol nach der letzten Heilung | 3,0333 s | 3,0167 s | 3,0083 s |
| Skriptblitz von 2 s: Zahl der Blitze | 4 | 4 | 4 |
| Skriptblitz: Ende des letzten Blitzes nach der Anforderung | 1,6667 s | 1,6667 s | 1,6667 s |
| ein Blitz von der Spitze bis null (Hüllkurve, auch die der Übernahme) | 0,1667 s | 0,1667 s | 0,1667 s |
| Fahrwerk: Ausschwingen auf einen Hang (1 % der Ruhelage) | 1,2667 s | 1,2667 s | 1,2833 s |
| Kamerafahrt von 5 s (Director und `CAMERA_MOVEMENT_FINISHED`) | 5,0333 s | 5,0167 s | 5,0083 s |
| Militäruntertitel: erstes Zeichen | 0,7667 s | 0,7667 s | 0,7667 s |
| Militäruntertitel: alle neun Zeichen | 2,0667 s | 2,0667 s | 2,0667 s |
| Militäruntertitel von 5000 ms: verschwunden | 7,5333 s | 7,5333 s | 7,5333 s |
| Meldungszeile bis ausgeblendet | 9,2333 s | 9,2333 s | 9,2333 s |
| Kinotext von 4 s | 4,0000 s | 4,0000 s | 4,0000 s |
| Sprachplatzhalter | 3,0333 s | 3,0167 s | 3,0083 s |
| Skriptblende 30/30/30 Bilder: schwarz / vorbei | 1,0000 / 3,0000 s | 1,0000 / 3,0000 s | 1,0000 / 3,0000 s |
| Letterbox-Blende bis voll | 1,0333 s | 1,0167 s | 1,0083 s |
| Graustufen-Rampe von 30 Bildern | 1,0000 s | 1,0000 s | 1,0000 s |
| Radarereignis | 8,0333 s | 8,0167 s | 8,0083 s |
| Eva: Abstand zweier Ansagen desselben Ereignisses | 30,0000 s | 30,0000 s | 30,0000 s |
| Partikelsystem von der Erzeugung bis zum letzten Partikel (Realdaten, `AirF_AuroraBombExplosion`, je Rate 30 Partikel im Maximum) | 11,4000 s | 11,3833 s | 11,3917 s |
| *Nachtrag:* Unter-Beschuss-Hinweis (`SoundCueDeriver`, `Radar::tryUnderAttackEvent`): Zeit bis zum nächsten für denselben Ort | 10,0000 s | 10,0000 s | 10,0000 s |
| Unter-Beschuss-Zeile der Meldungsliste (`HudMessageFeed`): Zeit bis zur nächsten | 10,0000 s | 10,0000 s | 10,0000 s |
| Schadenseffekt: Zeit bis zum nächsten derselben Schadensart (`ThrottleTime` 1000 ms, `FxExecutor`) | 1,0000 s | 1,0000 s | 1,0000 s |
| Jubel (`SPECIAL_CHEERING` nach `ALL_CHEER`) | 2,9667 s | 2,9833 s | 2,9917 s |
| Superwaffenzeile (`ReloadTime` 10000 ms): höchstens fünf Sekunden übrig / bereit | 4,0333 / 10,0000 s | 4,0167 / 10,0000 s | 4,0083 / 10,0000 s |
| Superwaffenzeile: erster weißer Blitz der bereiten Zeile (`SuperweaponCountdownFlashDuration` 500 ms) | 10,5000 s | 10,5000 s | 10,5000 s |
| Benannter Zähler von 5 s (`DISPLAY_COUNTDOWN_TIMER`): zeigt 0:03 / 0:00, gemessen ab dem Schritt, der ihn setzt | 1,0333 / 4,0333 s | 1,0167 / 4,0167 s | 1,0083 / 4,0083 s |
| Benannter Zähler: erste Blinkfarbe nach dem Ablauf (`NamedTimerCountdownFlashDuration` 500 ms) | 4,4667 s | 4,4833 s | 4,4917 s |
| Zoom-Unschärfe (`CAMERA_MOTION_BLUR_JUMP`): Scheitel (60) / vorbei | 0,4000 / 0,8000 s | 0,4000 / 0,8000 s | 0,4000 / 0,8000 s |
| Dauerauftrag (Jet im Angriff): Abstand der Feuerbefehle eines Anflugs (`DelayBetweenShots` 300 ms), fünf Befehle je Anflug | 0,3000 s | 0,3000 s | 0,3000 s |
| Übernahme: Dauer, die der letzte Blitz der Folge gezeigt wird | 0,1667 s | 0,1667 s | 0,1667 s |

Die Unterschiede von 1/60 bzw. 1/120 s in einigen Zeilen sind der Schritt, in dem der Ablauf bemerkt wird (ein Tick der jeweiligen Rate), kein Skalierungsfehler. Dazu: `GameClock` gegen den Zeitdienst der Simulation für die Ticks −3 bis 499 bei allen drei Raten; der Flutschutz der Tonhinweise bei zwei Ticks je Originalbild (`FxTests.RateLimiter`).

Läufe bei 60 Hz über die ganze Kette (keine Zeitmaß-Tests, nur „läuft durch“ und die Prüfungen der Szenen): `scripts/audit-scenarios.ps1 -TickRate 60` besteht alle drei Szenen (dafür nimmt `scenario-audit` seine Proben auf Bildgrenzen und rechnet die zwei bis drei Ticks Schrittversatz der Rückstellprüfung in Bilder um; bei 30 Hz ist seine Ausgabe byteidentisch); der Client läuft im Rauchtest mit `--tick-rate 60` durch (`--match "Maps/Alpine Assault/Alpine Assault.map" --autoplay --frames 600` und `--mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map" --frames 600`: 600 Bilder, Welttick 1.200, 1.200 Schritte der Skript-VM, 0 Treffer auf fehlende Typen; bei 30 Hz Welttick 600).

Die Superwaffenzeile und der benannte Zähler lesen außerdem auf jeder ganzen Sekunde bei allen drei Raten denselben Text (im Test verglichen). Die Daueraufträge geben bei jeder Rate nur auf Ticks, die ein Originalbild beginnen, Befehle aus und höchstens einen Feuerbefehl je Originalbild.

**Gemessen und nicht gleich – die Ursache liegt in der Simulation, nicht in der Darstellung** (die Tests messen und drucken diese Werte, verlangen aber keine Gleichheit; alle drei Stellen stehen in §5):

| Element | 30 Hz | 60 Hz | 120 Hz | Ursache |
|---|---|---|---|---|
| Blinkfolge einer Übernahme (Vorbereitung 2000 ms): Zahl der Blitze | 4 | 9 | 19 | `SpecialAbilityUpdate.StepCaptureFlash` erhöht die Blitzphase je Tick (§5.1); belegt ist, dass die Kaserne nur innerhalb der Hüllkurve eines Ereignisses getönt ist und jeder Blitz gleich lange dauert — **Phase 23:** behoben, **4 / 4 / 4** (`d923cea`, `docs/RATE-INVARIANCE-TURRETS.md`) |
| Blinkfolge: erster Blitz bis zum Ende der letzten Tönung | 1,0333 s | 1,4167 s | 1,6583 s | dieselbe — **Phase 23:** 1,0333 / 1,0500 / 1,0417 s (Ausgabe des Zeitmaß-Tests am Stand `e1252df`; der Rest unter einem Bild ist der Tick, in dem das Ende bemerkt wird) |
| `FREEFALL` eines Lawinenfelsens: gezeigt für | 2,3667 s | 1,3333 s | 0,6917 s | die Rutschphysik von `TensileFormationUpdate` rechnet je Tick (§5.1, §5.2): der letzte freie Fall liegt bei jeder Rate im 85. Tick des Rutschens (2,8333 / 1,4167 / 0,7083 s). Belegt ist das Fenster der Darstellung: nie ab dem hundertsten Originalbild — **Phase 23:** die Ursache in der Simulation ist behoben (Rutschphysik einmal je Bild, `302e88a`, `docs/RATE-INVARIANCE-PHYSICS.md`: letztes Bild mit Absinken 186 bei jeder Rate); der Wert dieser Zeile ist danach in keinem Protokoll notiert – **nicht neu gemessen** |
| Dauerauftrag: erster Feuerbefehl nach dem Auftrag | 2,0333 s | 2,1000 s | 2,1333 s | der Anflug des Jets (Flugmodell der Simulation, §5.2); der Rhythmus der Befehle selbst ist gleich (Tabelle oben) — **Phase 23:** die Ursache ist behoben (Flugmodell je Bild; in der Suite fällt der erste Schuss des Raptor-Angriffs bei jeder Rate nach 18,6333 s, `346cadc`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md`); der Wert dieser Zeile ist **nicht neu gemessen** |

**Nicht headless prüfbar:** ob irgendetwas davon richtig aussieht oder klingt (§14.7). Die Zoom-Unschärfe ist als Zustand des Directors belegt (`MissionPostEffects.MotionBlurCount`), nicht als gezeichnetes Bild.

### 14.5 Tests: Schritte, Zeiten, Liste

`tests/Shared/TestTime.cs` (in `Game.Tests` und `Presentation.Tests` eingebunden): `FrameSeconds`/`FrameSecondsF` (ein Originalbild Wandzeit – der Schritt einer Anzeige mit 30 Bildern je Sekunde und von allem ohne Welt), `TickSeconds(world)` (genau ein Tick dieser Welt), `Ticks(world, sekunden)` und `Frames(world, originalbilder)` (Spielzeit in Ticks der Testwelt), `FramesPerSecond`. Kein Test außerhalb der Simulation schreibt mehr `1.0 / 30` oder `1f / 30` (vorher 95 Stellen).

**Simulationstests (Nachtrag).** `tests/CnCNew.Simulation.Tests/TestTime.cs`: `world.Frames(n)` (Originalbilder in Ticks der Testwelt; auch auf `MissionSession` und `SkirmishMatchSession`), `world.InFrames(ticks)` (eine gemessene Tickzahl auf dem Raster des Originals, abgerundet – für Erwartungen der Form `is >= 44 and <= 47`, die Konstanten brauchen), `world.PerFrame(wert)` (eine Geschwindigkeit je Originalbild als Wert je Tick), `TestRate.Frames(n)`/`TestRate.InFrames`/`TestRate.Time` für Katalogerwartungen ohne Welt. Bei 30 Hz ist jeder Helfer die Identität: jede Erwartung behält ihren Zahlenwert. Stand: 629 Stellen `….Frames(…)` an einer Welt oder Sitzung, 36 `TestRate.Frames`, 20 `InFrames`, 4 `PerFrame`; acht Hilfsfunktionen zählen ihre Budgets selbst in Originalbildern, statt dass jeder Aufruf umgerechnet wird (`StepUntil` in `Tests.Air.cs` und `Tests.Construction.cs`, `CsRun`, `StepN`, `StructuresWorld.Run`, `NavRun`, `RunUntilContained`, `ObserveCapture`, `TicksUntilShot`). Die Standard-Testwelten und -kataloge nehmen ihre Rate von `TestRate.Default` (235 Stellen, 30 Hz; `--tick-rate N` oder `CNCNEW_TEST_TICK_RATE=N` baut sie mit 60 oder 120 Hz, §14.9.2); Tests über eine bestimmte Rate (`Tests.TickRate*.cs`, die Umrechnungstabellen in `Tests.Basics.cs`, die Ablehnung einer fremden Rate in `Tests.State.cs`) nennen diese Rate weiter selbst.

**Regel.** Eine Zahl, die Zeit meint – eine Dauer aus den Daten, eine Frist, ein Budget „so lange laufen, bis X geschehen ist“ –, steht in Originalbildern und geht über einen Helfer. Eine Zahl, die wirklich Ticks meint, bleibt nackt und ist unten je Datei mit Grund genannt: Reihenfolge im Scheduler und „ein Schritt später“ (`Tick − 1`, `+ 1`, `Run(1)`), Schritte der Skript-VM, Befehlsticks und Verzögerungen von `SetTimerCommand` (§7), Laufbudgets und Prüfpunkte aufgezeichneter Replays und des Stresslaufs (§8.1, §8.3), von Hand je Schritt gerechnete Flugbahnen, Stichprobentakte, Wächterwerte.

Stand je Testdatei (alle `*.cs` unter `tests/`, ohne `bin`/`obj`): **142 Dateien – umgestellt 46, gemischt 33, bewusst in Ticks 7, nicht betroffen 49, neu 7, offen 0.** „gemischt“ heißt: die Zeit- und Budgetzeilen der Datei sind umgestellt, und die Anmerkung nennt, was darin bewusst in Ticks (oder in Originalbildern einer Uhr ohne Welt) bleibt. Von den 57 Dateien, die am Ende der Phase 21 „offen“ waren, sind jetzt 27 umgestellt, 27 gemischt, 2 bewusst in Ticks und 1 nicht betroffen. Die Zahlen der „Zeit- und Budgetzeilen des Inventars“ in den Anmerkungen der schon in Phase 21 umgestellten Dateien stammen aus `docs/TICKRATE-AUDIT.md` §7.2 und sind dort nachzulesen.

| Datei | Stand | Anmerkung |
|---|---|---|
| `CnCNew.Archives.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Audio.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Audio.Tests/Tests.Catalog.cs` | nicht betroffen |  |
| `CnCNew.Audio.Tests/Tests.Decoding.cs` | nicht betroffen |  |
| `CnCNew.Audio.Tests/Tests.Mixer.cs` | nicht betroffen |  |
| `CnCNew.Audio.Tests/Tests.RealData.cs` | nicht betroffen |  |
| `CnCNew.Content.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Data.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Formats.Tests/MapDecoderTests.cs` | nicht betroffen |  |
| `CnCNew.Formats.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Game.Tests/AudioDirectorTests.cs` | umgestellt | 13 Schritte über `TestTime`; 6 Zeit- und 2 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Game.Tests/CampaignShellTests.cs` | umgestellt | Lauflängen über `StepUntilOver` und die Kettenhash-Schleife in Originalbildern (`TestTime.Frames`) |
| `CnCNew.Game.Tests/CampaignTests.cs` | umgestellt | Lauflängen (`Step`), Kopf-Tick und Stichprobe über `TestTime.Frames`; `presentationLatencyTicks: 90` ist nach §8.1 ein Wert in Originalbildern und bleibt; neu: Ratenprüfungen der Spielstand-Hülle (§14.9) |
| `CnCNew.Game.Tests/DeterminismTests.cs` | nicht betroffen |  |
| `CnCNew.Game.Tests/HudTests.cs` | umgestellt | 1 Schritte über `TestTime`; 22 Zeit- und 5 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Game.Tests/LiveMatchTests.cs` | umgestellt | 1 Schritte über `TestTime`; 1 Zeit- und 0 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Game.Tests/MissionEffectsTests.cs` | umgestellt | 16 Schritte über `TestTime`; 11 Zeit- und 0 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Game.Tests/MissionHudTests.cs` | umgestellt | 21 Schritte über `TestTime`; 1 Zeit- und 0 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Game.Tests/MissionPresentationTests.cs` | umgestellt | Aufrufstellen und Dauern über die benannten Konstanten |
| `CnCNew.Game.Tests/MissionTextTests.cs` | umgestellt | 12 Schritte über `TestTime`; 10 Zeit- und 1 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Game.Tests/ModelGeometryTests.cs` | nicht betroffen |  |
| `CnCNew.Game.Tests/Program.cs` | umgestellt | nur Aufrufstellen geänderter Schnittstellen |
| `CnCNew.Game.Tests/ScenarioTests.cs` | umgestellt | 1 Schritte über `TestTime`; 0 Zeit- und 2 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Game.Tests/ShellTests.cs` | umgestellt | 2 Schritte über `TestTime`; 0 Zeit- und 1 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Game.Tests/ShellTransitionTests.cs` | umgestellt | Aufrufstellen und Dauern über die benannten Konstanten; 0 Zeit- und 1 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Game.Tests/SourceRuleTests.cs` | neu | Phase 21 (Zeithelfer, Zeitmaß-Test bzw. Quelltexttest) |
| `CnCNew.Game.Tests/TestHarness.cs` | nicht betroffen |  |
| `CnCNew.Game.Tests/TextFixtures.cs` | nicht betroffen |  |
| `CnCNew.Game.Tests/TimeScaleTests.cs` | neu | Phase 21 (Zeitmaß-Test); im Nachtrag um Unter-Beschuss-Zeile, Superwaffenliste, benannten Zähler, Zoom-Unschärfe und 120 Hz für das Partikelsystem erweitert |
| `CnCNew.IO.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Images.Tests/CursorFixtures.cs` | nicht betroffen |  |
| `CnCNew.Images.Tests/CursorReaderTests.cs` | nicht betroffen |  |
| `CnCNew.Images.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Import.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Presentation.Tests/CameraTests.cs` | umgestellt | 16 Schritte über `TestTime`; 2 Zeit- und 0 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Presentation.Tests/CaptureConditionTests.cs` | umgestellt | Laufbudget und die Stufenlängen 15/30/9 Bilder über `TestTime.Frames` |
| `CnCNew.Presentation.Tests/CaptureFlashTests.cs` | gemischt | umgestellt: Laufbudget, neuer Zeitmaß-Test der Blinkfolge. Bewusst nicht in Ticks der Welt, sondern in Originalbildern der Präsentationsuhr: Hüllkurve (`TintAt`) und Skriptblitz (Bilder 15/30/45/60). „Vier Blitze je Vorbereitung“ ist der Rhythmus der Simulation bei 30 Hz (die Welt nennt 30 Hz; §5.1 `StepCaptureFlash`) |
| `CnCNew.Presentation.Tests/CursorTests.cs` | umgestellt | 8 Schritte über `TestTime`; 1 Zeit- und 2 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Presentation.Tests/FxTests.TimeScale.cs` | neu | Nachtrag Phase 21: Zeitmaß der Unter-Beschuss-Sperre und der Schadenseffekt-Drosselung |
| `CnCNew.Presentation.Tests/FxTests.cs` | umgestellt | Aufrufstellen und Dauern über die benannten Konstanten; 32 Zeit- und 9 Budgetzeilen des Inventars zählen Schritte der Effektuhr (Originalbilder, ohne Welt); im Nachtrag nur `partial` und eine Registrierzeile |
| `CnCNew.Presentation.Tests/InputTests.cs` | umgestellt | 4 Schritte über `TestTime`; 2 Zeit- und 3 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Presentation.Tests/JetOrderTests.cs` | gemischt | umgestellt: beide Laufbudgets; neuer Zeitmaß-Test der Daueraufträge. Bewusst in Ticks: „höchstens 3 Ticks nach der letzten Rakete“ (Reihenfolge); `SimulationTimingMin` nennt 30 Hz (Rohwert-Toleranz) |
| `CnCNew.Presentation.Tests/MinimapTests.cs` | nicht betroffen |  |
| `CnCNew.Presentation.Tests/ObjectTests.TimeScale.cs` | neu | Phase 21 (Zeithelfer, Zeitmaß-Test bzw. Quelltexttest) |
| `CnCNew.Presentation.Tests/ObjectTests.cs` | umgestellt | Aufrufstellen und Dauern über die benannten Konstanten; 3 Zeit- und 3 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Presentation.Tests/PlacementPreviewTests.cs` | nicht betroffen |  |
| `CnCNew.Presentation.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Presentation.Tests/RoadRealDataTests.cs` | nicht betroffen |  |
| `CnCNew.Presentation.Tests/RoadTests.cs` | nicht betroffen |  |
| `CnCNew.Presentation.Tests/StructureConditionTests.cs` | gemischt | umgestellt: 23 Laufschleifen, 13 Dauern (`Near(TestTime.Frames(…), …)`), zwei Fenster. Bewusst in Ticks: Probentakt `i % 7`, Versätze der Abtastung (`Tick + 1`, `+ 2`), Toleranzen von `Near`, „Schließen direkt nach der Freigabe“, sechs Schritte Vorlauf vor der Projektion |
| `CnCNew.Presentation.Tests/SyntheticTerrain.cs` | nicht betroffen |  |
| `CnCNew.Presentation.Tests/TerrainTests.cs` | bewusst in Ticks | 0 Tickerwartungen (Reihenfolge, Bildnummern der Shell-Übergänge), 1 Ratenangaben |
| `CnCNew.Presentation.Tests/TestHarness.cs` | nicht betroffen |  |
| `CnCNew.Presentation.Tests/UnitConditionTests.TimeScale.cs` | neu | Nachtrag Phase 21: Zeitmaß von Jubel und `FREEFALL` |
| `CnCNew.Presentation.Tests/UnitConditionTests.cs` | umgestellt | Aufrufstellen und Dauern über die benannten Konstanten; 6 Zeit- und 19 Budgetzeilen des Inventars zählen weiter Schritte der 30-Hz-Fixture (Originalbilder), soweit sie nicht eine der umbenannten Dauern benutzen |
| `CnCNew.Presentation.Tests/VisionTests.cs` | umgestellt | nur Aufrufstellen geänderter Schnittstellen |
| `CnCNew.Presentation.Tests/WorldRealDataTests.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Program.cs` | bewusst in Ticks | Testmodul `ProbeModule` führt `nextWake` als absoluten Tick (Scheduler); im Nachtrag nur die Hinweiszeile für `--tick-rate` |
| `CnCNew.Simulation.Tests/TestTime.cs` | neu | Nachtrag Phase 21: `TestRate` (Rate der Standard-Testwelten, `--tick-rate N`) und `TestTime` (`world.Frames`, `world.InFrames`, `world.PerFrame`) |
| `CnCNew.Simulation.Tests/Tests.Acquisition.cs` | gemischt | umgestellt: Laufbudgets, `MoodCheckTicks`, Nachlade- und Haltefristen, erster Blick „zwei Bilder nach Leerlauf“. Bewusst in Ticks: `step > 1`/`> 2` (Reihenfolge nach einem Befehl), Replay-Kopf `EndTick 300`/Prüfpunkte 20 und die Befehlsticks des Replays |
| `CnCNew.Simulation.Tests/Tests.Air.cs` | umgestellt | alle Budgets von `StepUntil` (Helfer zählt Originalbilder) und die Laufschleifen; `WeaponReadyTick >= SleepForever` ist ein Wächterwert |
| `CnCNew.Simulation.Tests/Tests.AirModelGeometry.cs` | umgestellt | Budgets über `StepUntil` und Schleifen; „zwei Schüsse“ ist eine Anzahl |
| `CnCNew.Simulation.Tests/Tests.AirReal.cs` | umgestellt | Budgets; EMP-Dauer „mehr als 800 Bilder“ |
| `CnCNew.Simulation.Tests/Tests.AirWeapons.cs` | gemischt | umgestellt: `StartFadeTime`, EMP- und Flugblattdauer, Silotür (`ready − 40/20`, `use + 25/70`), Orbit-Einschwingzeit, Budgets. Bewusst in Ticks: `pulseTick = Tick − 1` und die Toleranz ±1 (der gerade ausgeführte Schritt), `Run(ready + 2)`, `Run(use + 3)` (Schritte nach einem Befehl), vier Schritte des zweiten Laufs |
| `CnCNew.Simulation.Tests/Tests.Basics.cs` | bewusst in Ticks | die Umrechnungstabellen prüfen den Zeitdienst der Rate 30 Hz und nennen sie (`SimulationRate.Original`); die Katalogtests darunter laufen auf der Testrate |
| `CnCNew.Simulation.Tests/Tests.BattlePlan.cs` | umgestellt | alle Wartezeiten über `CsRun` (Helfer zählt Originalbilder) |
| `CnCNew.Simulation.Tests/Tests.CleanupAndSpy.cs` | umgestellt | gekappte Dauer 44–47 Bilder, An-/Aus-Anteil über 400 Bilder, Budgets, `CsRun` |
| `CnCNew.Simulation.Tests/Tests.Collision.cs` | gemischt | umgestellt: Fallzeit des Turms (90–130 Bilder), Stichprobe bei Bild 60, Budgets, `tick > 5`. Bewusst in Ticks bzw. Phase 23: die Fixture-Faktoren `StructuralDecay` u. a., die die Simulation je Tick anwendet (§5.1) |
| `CnCNew.Simulation.Tests/Tests.Combat.cs` | gemischt | umgestellt: Pulsabstände 18–20/9/3/30 Bilder, Sperrfenster 28 Bilder, Budgets. Bewusst in Ticks: drei Schritte nach dem Befehl (`ReplaceObjectUpgrade`) |
| `CnCNew.Simulation.Tests/Tests.Construction.cs` | umgestellt | `StepUntil` nimmt und liefert Originalbilder; Bauarbeit 60 Bilder; Verkauf `Frames(180) − 1` (180 Bilder weniger ein Schritt, wie `TickRateWorldSaleTakesTheSameGameTime`); `tick == 0` ist der erste Schritt |
| `CnCNew.Simulation.Tests/Tests.Crates.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.CratesAndSpecials.cs` | umgestellt | `CsRun` zählt Originalbilder (118 Aufrufe in fünf Dateien); Sabotagedauer 59–61 Bilder, Wiederaufnahme nach 110 Bildern, Dauer eines Stealth-Grants |
| `CnCNew.Simulation.Tests/Tests.CreationLists.cs` | umgestellt | Lebensdauer 25–35 Bilder, Budget |
| `CnCNew.Simulation.Tests/Tests.DamagedLocomotor.cs` | umgestellt | Fahrzeit von 60 Bildern |
| `CnCNew.Simulation.Tests/Tests.Determinism.cs` | gemischt | umgestellt: vier Laufschleifen. Bewusst in Ticks: je drei Schritte zwischen Befehlen (Reihenfolge), Stresslauf (§8.3) |
| `CnCNew.Simulation.Tests/Tests.FiringTracker.cs` | umgestellt | Schussabstände 18/9/4–5 Bilder, Auslaufzeiten 30–36 und `Frames(30) + 1`, Vorlaufzeiten; `Shots` misst auf dem Bildraster (`InFrames`), der Rückruf bekommt den Schrittindex |
| `CnCNew.Simulation.Tests/Tests.Flight.cs` | gemischt | umgestellt: alle `Run(session, n)` ab zwei Schritten, Budgets, Landung, Stichprobe bei Bild 60. Bewusst in Ticks: `Run(session, 1)` (ein Schritt der Skript-VM); `CombatIntervalTicks` ist ein Schalter (§8.1) |
| `CnCNew.Simulation.Tests/Tests.GoldenMaster.cs` | umgestellt | das Modell rechnet Schusspause 15 und Nachladen 60 in Bildern; Produktionsabstand `Frames(2 · 30)` |
| `CnCNew.Simulation.Tests/Tests.HashWork.cs` | gemischt | umgestellt: Laufschleifen. Bewusst in Ticks: Stichprobentakt (`% 97`, `% 10`) und der Stresslauf (§8.3); geprüft werden Hashgleichheiten, keine Zeiten |
| `CnCNew.Simulation.Tests/Tests.Indexes.cs` | nicht betroffen | Listen gegen Weltdurchläufe; ein Laufbudget über `Frames`, sonst Rundenzähler |
| `CnCNew.Simulation.Tests/Tests.JetFlight.cs` | gemischt | umgestellt: Budgets, Ankunft „vor Bild 400“, Kehrtwende „mindestens 40 Bilder“; Werte je Tick kommen vom Zeitdienst der Testrate. Bewusst unverändert: Rohwert-Toleranzen (64 bzw. 2 Rohbits, Phase 22) |
| `CnCNew.Simulation.Tests/Tests.JetFlightReal.cs` | umgestellt | Budget 4200 Bilder, Messung ab Bild 20; Katalogwerte je Tick über den Zeitdienst der Testrate |
| `CnCNew.Simulation.Tests/Tests.LastBlockers.cs` | umgestellt | `CsRun`; Sabotagedauer im Ereigniswert, Heilsperre `hit + Frames(30)`, Hashlauf 150 Bilder |
| `CnCNew.Simulation.Tests/Tests.MatchModules.cs` | gemischt | umgestellt: Hashvergleichsschleifen. Bewusst in Ticks: `Step(3)`, `Step(4)` und die Schleifen mit drei/vier Schritten (Reihenfolge nach einem Befehl) |
| `CnCNew.Simulation.Tests/Tests.Navigation.cs` | umgestellt | `NavRun` nimmt und liefert Originalbilder (15 Aufrufe); „Umweg länger als 75 Bilder“ |
| `CnCNew.Simulation.Tests/Tests.NavigationEscape.cs` | umgestellt | über `NavRun` (Budget 900 Bilder) |
| `CnCNew.Simulation.Tests/Tests.PerfMovement.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.PerfSummary.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.Phase12Modules.cs` | gemischt | umgestellt: `Run(session, n)`. Bewusst in Ticks: `CombatIntervalTicks = 0` ist ein Schalter (§8.1) |
| `CnCNew.Simulation.Tests/Tests.Phase13Actions.cs` | umgestellt | alle `Run(session, n)` in Bildern |
| `CnCNew.Simulation.Tests/Tests.Phase5.cs` | gemischt | umgestellt: jede Frist der Form `while (Tick <= start + N)` (Brand, Gift, Regeneration, verzögerter Tod, Entdeckung), Budgets, „ein Bild“ zwischen zwei Schüssen ohne Schusspause. Bewusst in Ticks: `xTick = Tick − 1` (der ausgeführte Schritt), drei Schritte nach dem Erzeugen |
| `CnCNew.Simulation.Tests/Tests.PlayerAgent.cs` | umgestellt | Laufbudgets des Zielfahrers (`maxTicks: TestRate.Frames(…)`) |
| `CnCNew.Simulation.Tests/Tests.PowerPlant.cs` | gemischt | umgestellt: Budgets (`StepUntil` in Bildern), 40 Bilder Überlast. Bewusst in Ticks: „nach zwei Schritten ist Gesundheit abgezogen“ |
| `CnCNew.Simulation.Tests/Tests.Projectiles.cs` | gemischt | umgestellt: verzögerter Treffer ohne Geschoss (Landung `F + Frames(8)`), Abfanglaser, Budgets. **Bewusst in Ticks:** vier von Hand gerechnete Flugbahnen je Schritt (Pfadpunkte der Granate, Bogen, Rakete mit und ohne Zündverzug: x je Tick) – sie beschreiben die 30-Hz-Welt Schritt für Schritt und schlagen bei 60 Hz fehl (4 der 6 Fehlschläge, §14.9) |
| `CnCNew.Simulation.Tests/Tests.RealFlight.cs` | umgestellt | Budget 1800 Bilder, Evakuierung bei Bild 10 |
| `CnCNew.Simulation.Tests/Tests.RealMovement.cs` | umgestellt | Budget 1800 Bilder, Stillstand ab Bild 20 |
| `CnCNew.Simulation.Tests/Tests.ReferenceManifest.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.ReferenceMatch.cs` | bewusst in Ticks | Laufbudget, `MinTicks`, `EndTick == 62_000` und Prüfpunkte sind die Länge eines aufgezeichneten Replays (§8.1: Laufbudgets bleiben Ticks); `GraceTicks` ist dort schon ein Regelwert in Bildern |
| `CnCNew.Simulation.Tests/Tests.Repair.cs` | umgestellt | Reparaturdauer 8–10 Bilder, Betrag je Tick aus `Frames(9)`, `RunUntilContained` in Bildern, Budgets |
| `CnCNew.Simulation.Tests/Tests.RetailModules.cs` | gemischt | umgestellt: Produktionsabstand 30 Bilder, Ausgangssperre 88–92 Bilder, Budgets (`SpawnTicks`), `Step(40)`/`Step(60)`. Bewusst in Ticks: `Step(3)`, `Step(1)` und die Schleifen mit drei/vier Schritten (Reihenfolge nach einem Befehl) |
| `CnCNew.Simulation.Tests/Tests.Scripting.cs` | gemischt | umgestellt: `Run(session, n)` ab zwei Schritten, Timerwert `Frames(60)`, Nachlauf `EndGameDelayFrames + 5`, Laufbudgets der Szenarien. Bewusst in Ticks: `Run(session, 1)` (ein Schritt der Skript-VM) und `Run(session, 3)` vor der Zählung „je Auswertung“ |
| `CnCNew.Simulation.Tests/Tests.ScriptingActions12.cs` | gemischt | umgestellt: `Run(session, n)`, `HulkLifetimeOverrideTicks == Frames(60)`. Bewusst in Ticks: `CombatIntervalTicks = 0` (Schalter) |
| `CnCNew.Simulation.Tests/Tests.ScriptingPhase11.cs` | gemischt | umgestellt: 40 `Run(session, n)`, Budgets des Zielfahrers. Bewusst in Ticks: `Run(session, 1)` (ein VM-Schritt) |
| `CnCNew.Simulation.Tests/Tests.ScriptingPhase12.cs` | gemischt | umgestellt: Budgets des Zielfahrers. Bewusst in Ticks: „in jedem der drei Ticks ändert die Erklärung den Hash nicht“ |
| `CnCNew.Simulation.Tests/Tests.Skirmish.cs` | gemischt | umgestellt: „60 Bilder sind 2 s“. Bewusst in Ticks: Laufbudget und Prüfpunkte des aufgezeichneten Gefechts (`SkirmishRecord(12_000)`, alle 500 Ticks; §8.1) |
| `CnCNew.Simulation.Tests/Tests.Slaves.cs` | umgestellt | `StepN` zählt Originalbilder (48 Aufrufe); Feuertakt alle 12 Bilder, Budget |
| `CnCNew.Simulation.Tests/Tests.SourceRules.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.SpecialPower.cs` | gemischt | umgestellt: Nachladezeiten (`ReloadTicks == Frames(300)`/`Frames(30)`), Wartezeiten über `StepTo(Tick + Frames(…))`, Stichprobe bei Bild 320. Bewusst in Ticks: `StepTo(Tick + 3)` und drei Schritte nach einem Kauf (Reihenfolge), `reload − 1`, Replay-Kopf einer nur geschriebenen und gelesenen Datei |
| `CnCNew.Simulation.Tests/Tests.StagedCapture.cs` | gemischt | umgestellt: Stufenlängen (`Frames(StagedUnpack)` …, Realdaten 90/600/60 und 202/180/84 Bilder), `ObserveCapture` und `StagedWorldAt` in Bildern. **Bewusst in Ticks:** „vier Blitze je Vorbereitung“ – `StepCaptureFlash` wächst je Tick (§5.1) und liefert bei 60 Hz neun (1 der 6 Fehlschläge); Stichprobe nach 20 Vorbereitungsschritten |
| `CnCNew.Simulation.Tests/Tests.State.cs` | bewusst in Ticks | der Stresslauf zählt Schritte (§8.3): Lauflängen und Stopp-Ticks 3000/1500/600/1234/216.000, `"tick":600`, Befehlsticks 9 und 12; die Ablehnungstests nennen 30 Hz als Rate ihres Katalogs. Umgestellt sind nur vier Hashvergleichsschleifen |
| `CnCNew.Simulation.Tests/Tests.StructuresAndWorld.cs` | umgestellt | `StructuresWorld.Run` zählt Originalbilder (76 Aufrufe); Geschwindigkeiten je Bild über `PerFrame` (Zug, Gerüst), Budgets |
| `CnCNew.Simulation.Tests/Tests.Supply.cs` | gemischt | umgestellt: `RunCollecting`-Grenzen (`Frames(30) + 1` …), Fahrtabstand, Ersatzfrist 70/140 Bilder, Budgets. Bewusst in Ticks: Stichproben `i == 8 || i == 25`, vier Schritte, Replay-Kopf |
| `CnCNew.Simulation.Tests/Tests.Teams.cs` | umgestellt | Hashvergleich über 30 Bilder, Budget 200 Bilder; der Replay-Kopf nimmt die Testrate |
| `CnCNew.Simulation.Tests/Tests.Terrain.cs` | bewusst in Ticks | ein Replay von zehn Ticks (`TicksExecuted == 10`); der Kopf nimmt die Testrate |
| `CnCNew.Simulation.Tests/Tests.TickPhases.cs` | nicht betroffen | Phasenbeobachter über dem Stresslauf (§8.3); ein Laufbudget über `Frames` |
| `CnCNew.Simulation.Tests/Tests.TickRate.Grid.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.ModulesAM.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.ModulesNZ.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.Scripting.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.World.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.TickRate.cs` | nicht betroffen | nennt seine Raten; im Nachtrag drei Aufrufe mit ausdrücklicher Rate (`ShortReplay(rate: Hz30)`, `MissionMap(…, rate: Hz30)`) |
| `CnCNew.Simulation.Tests/Tests.TimeFreeze.cs` | gemischt | umgestellt: Timerdauer 28–40 Bilder, Budgets. Bewusst in Ticks: `frozenAt + 1`/`+ 2`/`tick + 1` (ein Schritt weiter nach dem Auftauen) |
| `CnCNew.Simulation.Tests/Tests.Transports.cs` | gemischt | umgestellt: Budgets, Heilbetrag je Tick aus `Frames(30)`, Steigzeit, Absturz und Explosion als „ein Schritt, dann N − 1 Bilder“ (`death + 1 + Frames(74)`), Stichproben. Bewusst in Ticks: `gone = Tick − 1`, Replay-Kopf einer nur geschriebenen Datei |
| `CnCNew.Simulation.Tests/Tests.TurretAttack.cs` | gemischt | umgestellt: Budgets, Haltezeit `1 + Frames(15)`, Aufgabe nach 44–47 Bildern, Abtastpause, Befehlsplan des Determinismuslaufs (Bilder 3, 60–90, 150, 260), Realdaten. Bewusst in Ticks: Befehle des Replays (Ticks 70–110, Kopf 200/20), `tick % 2` (Befehlstakt), `step > …`-Reihenfolgen |
| `CnCNew.Simulation.Tests/Tests.Turrets.cs` | gemischt | umgestellt: Wartezeit bis zum Schuss (`Frames(15)`, 8–10, 3–4, ≥ 30 Bilder), `RecenterTicks`, Ersatzwerte je Bild, Budgets. **Bewusst unverändert:** der Rohwert `TurnRate.Raw is > 6860 and < 6866` (30 Hz in Q47.16; 1 der 6 Fehlschläge, die Zeile gehört Phase 22); vier Befehle in vier Schritten |
| `CnCNew.Simulation.Tests/Tests.Veterancy.cs` | umgestellt | das Modell rechnet Schusspause und Nachladen je Stufe in Bildern; Budgets |
| `CnCNew.Simulation.Tests/Tests.Vision.cs` | gemischt | umgestellt: Hashvergleichsschleifen, „ein Bild“ bis die Waffe wieder bereit ist. Bewusst in Ticks: `world.VisionInterval` Schritte bis zur Auffrischung (schon ein Wert der Welt) |
| `CnCNew.Simulation.Tests/Tests.VisionEquivalence.cs` | nicht betroffen |  |
| `CnCNew.Simulation.Tests/Tests.WeaponSlots.cs` | gemischt | umgestellt: Bereitschaftsticks je Slot (`start + Frames(15)`, `start + 1 + Frames(3)`, `Frames(15 + 60)` …), Budgets, Befehlstakt `% Frames(40)`. Bewusst in Ticks: `LastFireTick == start + 1` (ein Schritt später), Befehle des Replays (Ticks 2–150, 60, 100; Kopf 160/20), Formatversionen |
| `CnCNew.Simulation.Tests/Tests.WeaponSlotsIntegration.cs` | gemischt | umgestellt: Schussabstände 3 und 15 Bilder, Budgets. Bewusst in Ticks: drei Schritte nach dem Umschalten, `ReadyTick >= SleepForever` (Wächterwert) |
| `CnCNew.Simulation.Tests/Tests.WeaponSlotsReal.cs` | umgestellt | Feuerversuche und Pausen in Bildern; `ReadyTick > Tick − 1` bezieht sich auf den ausgeführten Schritt |
| `CnCNew.Simulation.Tests/Tests.World.cs` | gemischt | umgestellt: Lebensdauern (gezogene Bilder, „eine Null wird ein Bild“, `DieTick`), Budget. **Bewusst in Ticks:** Scheduler-Reihenfolge und Ereignisticks (`update:2:5`), Befehlsticks, `SetTimerCommand`-Verzögerungen (nach §7 Ticks der Welt), Schritte `Run(1)`…`Run(15)` der Timerprüfung |
| `CnCNew.Ui.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.Ui.Tests/SentenceLayoutTests.cs` | nicht betroffen |  |
| `CnCNew.Ui.Tests/TextAndSettingsTests.cs` | nicht betroffen | die zwei Zeitzeilen prüfen `InGameUiSettings` in Originalbildern (500 ms = 15 Bilder, 333 ms = 10) – ohne Welt, ohne Tick |
| `CnCNew.Ui.Tests/TransitionTests.cs` | bewusst in Ticks | 19 Tickerwartungen (Reihenfolge, Bildnummern der Shell-Übergänge), 3 Ratenangaben |
| `CnCNew.Ui.Tests/UiRenderingTests.cs` | nicht betroffen |  |
| `CnCNew.Ui.Tests/WndFixtures.cs` | nicht betroffen |  |
| `CnCNew.Ui.Tests/WndTests.cs` | nicht betroffen |  |
| `CnCNew.W3D.Tests/Program.cs` | nicht betroffen |  |
| `CnCNew.W3D.Tests/SkeletonTests.cs` | nicht betroffen |  |
| `Shared/TestTime.cs` | neu | Phase 21 (Zeithelfer, Zeitmaß-Test bzw. Quelltexttest) |

### 14.6 Suchlauf und Quelltexttest

`tests/CnCNew.Game.Tests/SourceRuleTests.cs` verbietet in `src/CnCNew.Game`, `src/CnCNew.Presentation`, `src/CnCNew.Ui` und `src/CnCNew.Client.Stride` (230 Dateien, Kommentare und Zeichenketten ausgenommen): `SimulationRate.OriginalFramesPerSecond`/`Hz30`/`Hz60`/`Hz120`/`Original`; `FromTicksPerSecond(<Zahl>)`; eine benannte Bild- oder Tickrate mit Zahl (`…FramesPerSec…`, `…TicksPerSec…`, `…TicksPerFrame…`, `…FrameRate…`, `…TickRate…`, `…Fps…`, `…LogicFrames…`, `…LogicTicks… = <Zahl>`); die 30 als Rate (`1.0 / 30`, `/ 30`, `* 30`, `30 *`, `1000 / 30`, `/ 33`, `33.33`, `0.0333`); die 60 und 120 als Rate (`1.0 / 60`, `/ 60d`). Drei benannte Ausnahmen, jede mit Grund im Test, und eine Ausnahme, die keine Zeile mehr trifft, lässt den Test fehlschlagen:

| Datei | Zeile | Grund |
|---|---|---|
| `CnCNew.Game/Data/GameData.cs` | `DefaultSessionRate => SimulationRate.Original` | die eine Stelle, die eine Simulationsrate nennt (Standard der Sitzung) |
| `CnCNew.Game/Hud/HudOverlayRenderer.cs` | `clockMilliseconds / 30 + …` | 30 Millisekunden Wandzeit je Bild des Reparaturkreuzes: eine Dauer, keine Rate |
| `CnCNew.Client.Stride/Program.cs` | `_renderedFrames / 60d` | Rauchtest-Uhr des Modellbetrachters (1/60 s Animationszeit je Bild), ohne Simulation |

Der Test prüft auch sich selbst: 14 Zeilen, die er fangen muss (darunter die früheren Stellen des Inventars), 10, die er durchlassen muss.

Suchlauf mit den Mustern aus `docs/TICKRATE-AUDIT.md` §8 nach dem Umbau, Trefferzeilen / Dateien (`src` = die vier Projekte, `tools` = PresentationTool, UiRenderTool, CampaignTool, SimTool, `tests` = Game-, Presentation-, Ui-Tests und `tests/Shared`):

| Muster | `src` | `tools` | `tests` | Bewertung |
|---|---|---|---|---|
| P01 `LogicFramesPerSecond` | 0/0 | 0/0 | 1/1 | der Treffer ist eine Musterzeile des Quelltexttests |
| P02 benannte Rate mit Zahl | 3/3 | 0/0 | 11/5 | `src`: `PixelsPerSecond = 40f` (Abspann, Wandzeit), `RiverVScrollPerSecond = 0.002f * OriginalFrame.PerSecond`, ein Kommentar; `tests`: Fixture-INI und Musterzeilen |
| P03 Literale der 30 | 23/17 | 3/2 | 14/6 | `src`: nur Kommentare; `tools`: die Hinweiszeile von `scenario-audit` und die beschrifteten Hochrechnungen von `SimTool perf` (§6 Nr. 15); `tests`: Musterzeilen des Quelltexttests und Kommentare |
| P06 Tick-/Bildzahl mit Zahl | 25/16 | 13/2 | 10/6 | `src`: jede ist eine Konstante in Originalbildern (`…Frames`) oder eine Bildzahl von Bildern (Animationsbilder, Audiopuffer); keine Tickzahl |
| P07 `tick % <Zahl>` | 0/0 | 0/0 | 1/1 | – |
| P08 `Tick ± … <Vergleich> <Zahl>` | 5/3 | 3/1 | 12/5 | `src`: Reihenfolge (`IssuedTick + 1`, `world.Tick - 1`) und die umgestellten Vergleiche |
| N01 `SimulationTiming.` | 0/0 | 0/0 | 0/0 | – |
| N05 Modulo | 16/10 | 3/3 | 22/13 | `src`: Blinken auf Originalbild- bzw. Wanduhr, sonst Geometrie und Minuten |

Die übrigen Muster (P04, P05, P09, P14, P16, N06, N07, N08, N13, N17) sind laut Inventar als Regel zu laut; ihre Treffer in `src` sind die Stellen, die `docs/TICKRATE-AUDIT.md` §6 Zeile für Zeile bewertet hat, und jede dort als „Phase 21“ markierte Zeile ist in §14.3 bzw. den Fachdokumenten behandelt oder oben als offen genannt. Neu gelesen wurden für diesen Lauf die Treffer von P01, P02, P03, P06, P07, P08, N01 und N05 in `src`.

### 14.7 Sichtprüfliste für Phase 23 (von einem Menschen, bei `--tick-rate 60` gegen `--tick-rate 30`)

Nichts davon ist abgenommen. Vorgehen: dieselbe Szene im Fenster einmal mit 30 und einmal mit 60 Hz (`scripts/demo-*.ps1` bzw. der Client mit `--tick-rate 60`), dazu die Bildfolgen von `scripts/capture-scenarios.ps1 -TickRate 60` neben denen ohne die Option.

1. **Bewegung:** Einheiten fahren bei 60 Hz weicher (halb so große Interpolationsschritte) und gleich schnell; kein Ruckeln im Zweiertakt, kein Springen beim Anhalten.
2. **Effekte:** Explosionen, Rauch, Staub, Leuchtspuren und Lichtblitze dauern gleich lang und sind gleich groß und dicht wie bei 30 Hz; Rauchfahnen bewegter Objekte reißen nicht ab (die Effektuhr läuft mit 30 Schritten je Sekunde, die Objekte mit 60).
3. **Mündungsfeuer:** bei jedem Schuss sichtbar, weder Dauerfeuer noch nur ein Aufblitzen; der Rückstoß des Rohrs passt dazu.
4. **Feuerzustand:** Schussanimationen (`FIRING_A`) starten bei jedem Schuss und werden nicht doppelt ausgelöst.
5. **Federung und Lenkung:** Radfahrzeuge nicken beim Anfahren und Bremsen wie bei 30 Hz, schwingen aus und zittern nicht; Räder lenken ein und zurück; die Kabine eines Lasters folgt.
6. **Blitz der Übernahme und Auswahl:** die Kaserne blinkt beim Flaggehissen in gleicher Folge und Helligkeit; kein Flackern zwischen zwei Stufen.
7. **Heilsymbol und Jubel:** das Symbol bleibt etwa drei Sekunden nach der letzten Heilung.
8. **Nebel:** Aufdecken und Abdunkeln laufen gleich schnell; erkundete Szenerie wird ohne sichtbares Nachhinken abgedunkelt.
9. **HUD:** Meldungszeilen stehen gleich lange (etwa neun Sekunden), die Superwaffenuhr zählt Sekunden und blinkt im gleichen Takt, Befehlsmarken und Tooltips unverändert.
10. **Ton:** keine doppelten oder fehlenden Schuss- und Einschlagtöne in einem Gefecht; Eva sagt dasselbe Ereignis nicht häufiger an; die Unter-Beschuss-Meldung kommt nicht öfter.
11. **Missionsintro** (`--mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map"` und `MD_USA01`): Kamerafahrten enden, wenn das Skript weitergeht; Blenden, Letterbox und Graustufen dauern gleich; Untertitel tippen im gleichen Tempo mit einem Klick je Zeichen und stehen gleich lange; Zähler zeigen dieselbe Zeit.
12. **Daueraufträge:** eine angreifende Einheit schießt im gleichen Rhythmus, ein Jäger fliegt dieselben Anflüge; Wegpunktfolgen werden abgefahren.
13. **Shell und Kamera:** Übergänge, Scrollen, Drehen und Zoomen sind von der Rate unberührt (Wandzeit).

### 14.8 Offen nach Phase 21 (Stand nach dem Nachtrag §14.9)

- `SimTool perf`: die Hochrechnungen `catchup30`/`catchup60`/`core30_percent` nennen ihre Rate im Namen (§6 Nr. 15; Umstellung des Standards, Phase 23). — **Phase 23:** erledigt; `catchup` und `core_percent` werden an der Rate des Laufs gemessen (`0740ebf`).
- Drei Zeitmaße sind bei 60 Hz **nicht** gleich, weil die Simulation dort anders rechnet (§14.4, zweite Tabelle): Blinkfolge der Übernahme, Rutschen des Lawinenfelsens hinter `FREEFALL`, Anflug eines Jets. Sie gehören zur Liste der Phase 23 (§5). — **Phase 23:** alle drei Ursachen sind in der Simulation behoben (§14.4, zweite Tabelle; zwei der drei Zeitmaß-Werte sind nicht neu gemessen).
- Der Probelauf der Simulationstests bei 60 Hz besteht 396 von 402 Tests; die 6 Fehlschläge sind benannt (§14.9.2) und keine Fehler dieser Phase. — **Phase 23:** die Simulationstests bestehen bei 30, 60 und 120 Hz vollständig (513 von 513, mit Realdaten 543 von 543; gemessen am Stand `1b82e45`, §15.7).
- Die Tests von `Game.Tests`, `Presentation.Tests` und `Ui.Tests` haben keinen Schalter für die Standardrate: ihre Welten nennen 30 Hz (`SimulationRate.Original`) oder nehmen `GameData.DefaultSessionRate`; was dort bei 60 Hz gilt, belegen die Zeitmaß-Tests je Rate, nicht ein Probelauf der ganzen Suite. — **Phase 23:** erledigt; alle drei Programme nehmen `--tick-rate` und bestehen bei 30, 60 und 120 Hz (`docs/RATE-SWITCH-OUTER-TESTS.md`, `095ef8f`, `203806e`).
- Die Blende der Letterbox läuft auf der Präsentationsuhr statt auf Wandzeit; das Fahrwerk wird je Originalbild gestuft und nicht interpoliert.
- Keine Sichtabnahme, kein Hörtest, kein Spielen – weder bei 30 noch bei 60 Hz. — **Phase 23:** unverändert, mit einer Ausnahme: der Projektinhaber hat am 8. Oktober 2026 **ein** KI-Gefecht im Zuschauermodus bei 30 Hz angesehen (`docs/AI-OBSERVED-2026-10-08.md`). Bei 60 Hz hat kein Mensch das Spiel gesehen oder gehört.

### 14.9 Nachtrag: Rest der Phase 21 (6. Oktober 2026)

Vier Dinge, die der Bericht der Phase 21 offen ließ: Punkt 8 des Auftrags (Tests in Spielzeit), `SaveGame` mit Rate (§6 Nr. 3), die nicht belegten Zeitmaße (§14.4) und die Ausgabe von `CampaignTool` (§6 Nr. 12). `src/CnCNew.Simulation` ist dabei nicht angefasst.

#### 14.9.1 Tests in Spielzeit

Regel, Helfer und die vollständige Liste stehen in §14.5. Verfahren: zuerst die Rate der Standard-Testwelten auf einen Schalter gelegt (unten), dann jede Datei so lange umgestellt, bis bei 60 Hz nur noch übrig blieb, was wirklich Ticks meint oder was die Simulation dort anders rechnet; Budgets, die auch bei 60 Hz ausreichten, sind trotzdem umgestellt. Nicht mechanisch: wo eine Erwartung „ein Schritt, dann N − 1 Bilder“ meint, steht sie so da (`Frames(180) − 1` für den Verkauf, `death + 1 + Frames(74)` für den Absturz, `1 + Frames(15)` für die Haltezeit eines Turms, `Frames(30) + 1` für die erste Einzahlung) – diese Formen sind bei 30 Hz die alte Zahl und bei 60 Hz gemessen richtig.

#### 14.9.2 Probelauf der Simulationstests bei 60 Hz

```powershell
dotnet run --project tests/CnCNew.Simulation.Tests -- --tick-rate 60 [--content-dir .local/phase2-import-a] [--only TEXT]
$env:CNCNEW_TEST_TICK_RATE = '60'   # dasselbe über die Umgebung
```

Ohne die Option ändert sich nichts (30 Hz, kein Hinweis). Mit ihr meldet das Programm in der ersten Zeile die Rate. Der Lauf ist ein **Messfühler für Phase 23**, kein Abnahmetest: er sagt, welche Erwartungen brechen, wenn der Standard umgestellt wird.

| Stand | bestanden | fehlgeschlagen |
|---|---|---|
| 30 Hz (Referenz), vor und nach der Umstellung | 402 von 402 | 0 |
| 60 Hz, nur die Rate umgeschaltet, noch keine Erwartung umgestellt | 268 von 402 | 134 |
| 60 Hz nach der Umstellung, ohne `--content-dir` | 396 von 402 | 6 |
| 60 Hz nach der Umstellung, mit `--content-dir` (dazu die Realdatentests) | 410 von 416 | 6 |
| 120 Hz nach der Umstellung, ohne `--content-dir` (nur zur Information) | 389 von 402 | 13 |

Die 6 Fehlschläge bei 60 Hz, nach Klasse:

| Klasse | Tests | Was bricht |
|---|---|---|
| von Hand je Schritt gerechnete Flugbahn (bewusst in Ticks) | „Projectiles: straight shell flight time…“, „…ballistic arc…“, „…missile flight time (instant acceleration)“, „…missile ignition delay and acceleration“ | Pfadpunkte und Position je Tick sind für 30 Schritte je Sekunde gerechnet (20 Pfadpunkte werden 40, x = 10 nach einem Schritt wird 2,5). Phase 23 entscheidet: neu rechnen oder diese vier Tests auf 30 Hz festlegen |
| Rhythmus der Simulation je Tick (§5.1) | „Staged capture: the building flashes while the flag goes up…“ | vier Blitze je Vorbereitung werden neun |
| Rohwert des Festkommaformats bei 30 Hz | „Turrets: template model, defaults and strict parsing“ | `TurnRate.Raw is > 6860 and < 6866` (6° je Tick in Q47.16); die Zeile gehört der Festkomma-Umstellung (Phase 22) — **Berichtigung (Phase 23, §15.2 Nr. 11):** diese Beschreibung ist die Fassung vor Phase 22 und seither falsch. Seit Phase 22 prüft die Zeile `(TurnRate − PiFraction(1, 30)).Abs() <= Epsilon` – ein **Ratenliteral** (eine Konstante je Tick bei 30 Hz), kein Rohwert des Formats; behoben als `PiFraction(1, TicksPerSecond)` (`b405704`, `docs/RATE-INVARIANCE-TURRETS.md`) |

Ein siebter Test brach vor der Umstellung an einer Annahme, nicht an einer Zeit: „Scripts: hunting with a command button“ erwartete, dass das Opfer nach 240 Bildern noch existiert; bei 60 Hz ist es dann schon getötet und entfernt. Der Test nimmt jetzt auch das als Angriff (bei 30 Hz unverändert) – dass der Jäger bei 60 Hz schneller tötet, ist eine Beobachtung für Phase 23.

Bei 120 Hz kommen weitere dazu (13 Fehlschläge; nicht einzeln bewertet – 120 Hz ist keine Produktrate).

Was der Probelauf **nicht** sagt: dass sich die Simulation bei 60 Hz richtig verhält. Viele Tests prüfen Schranken (`>=`, Bereiche), die auch ein anderes Verhalten erfüllt; die Liste der Phase 23 bleibt §5.

#### 14.9.3 `SaveGame` Version 2

`src/CnCNew.Game/Save/SaveGame.cs`: die Hülle schreibt `"tickRate"` direkt hinter `"tick"`; `SaveGameHeader` trägt `TickRate`, `GameSeconds` und `GameTime` (m:ss). Geprüft wird in dieser Reihenfolge: Format, Version, Schwierigkeit, unterstützte Rate, **Rate gegen die Sitzung** (`context.Templates.Rate`), Fingerabdruck, Kampagne, Prüfsumme des Zustands, nach dem Wiederherstellen Tick und Rate des Kopfs gegen die Welt, Zustandshash. Meldungen: „Unsupported savegame version 1 (this game reads version 2).“, „Unsupported tick rate 50 Hz in the savegame.“, „This savegame was written at 60 Hz; this session runs at 30 Hz. It cannot be loaded.“, „The savegame's state does not match its header (tick or tick rate differ; the file is damaged).“. Version 1 wird nie als 30 Hz gelesen. Listen zeigen „Titel (m:ss)“; kein eingecheckter Bildschirm-Hash enthält eine Ladeliste mit Einträgen (`baselines/screens.txt` unverändert). Test `CampaignTests` „Savegame: the header carries the tick rate…“: derselbe Spielstand nach 95 Originalbildern bei 30 Hz (Tick 95) und bei 60 Hz (Tick 190) zeigt beide Male „0:03“, lädt jeweils in seiner Rate und wird über Kreuz in beiden Richtungen abgelehnt. Einzelheiten: `docs/CAMPAIGN-AND-SAVEGAMES.md` §4.

#### 14.9.4 Ausgabe von `CampaignTool`

`drive` und `audit` schreiben unter jede Kartenzeile, in der ein Ende erreicht wurde, eine eigene Zeile (`game time (m:ss at 30 Hz): victory after 0:26, defeat after 0:07` bzw. `…: lose after 0:05`) und am Ende `Budget: 9000 ticks are 5:00 of game time (m:ss) at 30 Hz.`; `playthrough` setzt die Spielzeit in Klammern hinter „at tick N“ und „no outcome after N ticks“. Die Kartenzeilen (`victory REACHED by '…' @Tick`, `win@Tick`, `lose@Tick`) und die Summenzeilen (`… maps driven (… ticks): victory reached …`, `… identical on a second run …`) sind Byte für Byte unverändert – sie werden mit `-cmatch 'victory REACHED'` und ähnlichen Mustern gezählt; kein Skript unter `scripts/` liest die Ausgabe von `CampaignTool`. Gemessen an `MD_USA01`: `audit --ticks 600` bei 30 Hz „lose@151“ und `--ticks 1200 --tick-rate 60` „lose@301“, beide „lose after 0:05“.

#### 14.9.5 Beweis bei 30 Hz

`SimTool input-manifest check` identisch und `reference-manifest check --set quick` (9 Läufe identisch) nach jedem Commit, der `src` oder die Werkzeuge berührt; `--set full` am Ende: 50 Läufe identisch (21 min 35 s bei drei Läufen nebeneinander auf einem belasteten Rechner). Simulationstests: 402 von 402 (Debug), 416 von 416 mit `--content-dir`, `-c Release -- --long` 403 von 403. `Game.Tests` (Release, `--content-dir`): 137 Tests bestanden, 0 fehlgeschlagen; `Presentation.Tests` mit `--content-dir`: alle bestanden; die übrigen Testprogramme: Archives, Content, Data (auch mit `--content-dir`), IO, Images, Formats, Import, W3D, Ui und Audio bestanden. Die Golden-Dateien unter `tests/**/baselines` sind unverändert (`git status` leer für diese Pfade). `scripts/audit-scenarios.ps1`: drei von drei Szenen bestanden, mit `-TickRate 60` ebenfalls drei von drei. `IniTool compat-check`: 751 Einträge, unverändert. Sauberer Solution-Build: 0 Warnungen, 0 Fehler (mit gelöschtem `bin`/`obj` von `CnCNew.Client.Stride`); `tools/CnCNew.UiRenderTool`: 0 Warnungen.

## 15. Phase 23: Invarianz gegenüber der Tickrate

Protokoll der Phase 23 (`docs/HANDOFF-PHASE23.md`), Ausgangsstand `20ffce9` (7. Oktober 2026). Dieser Abschnitt wird je Schritt fortgeschrieben; was er als gemessen nennt, stammt aus Einzelprotokollen dieses Tages.

### 15.1 Ausgangsstand: Kampagne und KI-Läufe nach der Reichweiten-Reparatur

Zielfahrer, Audit und Erreichbarkeit liefen zuletzt vor der Reichweiten-Reparatur der Phase 22 (`b274df5`, `SquaredReach`); die Reparatur war als ergebnisgleich angekündigt und nur über das Manifest belegt. Wiederholt am Stand `20ffce9`, 30 Hz, Release, je Karte ein eigener Prozess (`--map "/NAME.map"`), gezählt aus den 24 Einzelprotokollen:

| Lauf | Ergebnis | wie zuletzt gemessen? |
|---|---|---|
| `CampaignTool drive` (60.000 Ticks) | Sieg über das eigene Skript **24 von 24**, Niederlage **24 von 24** | ja |
| `CampaignTool audit --ticks 900` | 24 von 24 deterministisch im zweiten Lauf, 24 von 24 identisch nach Speichern/Laden, 0 Treffer auf fehlende Typen; Ausschalt-Endproben Sieg **13 von 24**, Niederlage **15 von 24** | ja |
| `CampaignTool reachability` | Sieg sauber 24, eingeschränkt 0, blockiert 0; Niederlage sauber 24 | ja |
| 16 KI-Läufe (Teil von `reference-manifest check --set full`, 50 von 50 Läufen identisch) | **12 entschieden**, 4 Patts (Armored Fury America/China und ChinaTank/AirForce, Bitter Winter America/China und ChinaTank/AirForce) | ja (Manifest unverändert) |

Laufzeit je Karte: Zielfahrer 13 bis 134 s (`MD_CHI04`), Audit 9 bis 57 s (`MD_USA01`), vier Prozesse nebeneinander. Kein Referenzwert hat sich geändert; die Reparatur ist damit auch über die Kampagnenwerkzeuge als ergebnisgleich belegt.

### 15.2 Die Ausfälle der Simulationstests bei 120 Hz, einzeln eingeordnet

Probelauf am Stand `20ffce9` (Release, `--tick-rate N --content-dir .local/phase2-import-a`, 429 Tests): bei 60 Hz **6** Fehlschläge (die sechs aus §14.9.2), bei 120 Hz **14** (§14.9.2 nannte 13 ohne Realdaten). Jeder der 14 ist gelesen und, wo es ging, durch Einzelläufe bei allen drei Raten vermessen. Klassen: **T** – nur die Erwartung des Tests zählt Ticks, die Simulation verhält sich in Spielzeit gleich; **S** – die Simulation hängt an der Schrittweite (Stelle in §5); **C** – chaotischer Ausgang eines ganzen Matchs. Ein Fall der Festkomma-Auflösung ist nicht darunter.

| Nr. | Test | Klasse | Ursache | §5 | 30 / 60 / 120 Hz (gemessen) | Verbleib |
|---|---|---|---|---|---|---|
| 1 | Phase 5: StealthDetectorUpdate marks and expires DETECTED | T | `Tests.Phase5.cs` läuft wörtlich 3 Ticks; getarnt wird ein Tick (erstes Update nach dem Erscheinen) plus ein Bild (`AtLeastOneFrame(StealthDelay 0)`) später | – (der eine Tick: §5.3 `Register`) | getarnt nach 2 / 3 / 5 Schritten = 0,067 / 0,050 / 0,042 s | Erwartung in Bildern (`1 + Frames(2)`) |
| 2 | Phase 5: reference match scenario, replay v2 factions, determinism | C | `MaxTicks = 60_000` ist ein Tickbudget (2.000 / 1.000 / 500 s); die Matchdauer ist chaotisch (Tabelle unten) | – | Radius 398: Alpha nach 340 s / Alpha nach 106,5 s / Beta nach 530 s | auf 30 Hz festlegen (der Radius 398 wurde für 30 Hz gewählt) |
| 3 | Construction: snapshot mid-construction and mid-sale, replay round trip | T | `EndTick` 400 und Befehlsticks 0–70 des Replays sind rohe Ticks; der Verkauf (180 Bilder) passt bei 120 Hz nicht hinein | – (§5.3 `_saleDurationTicks` für den einen Tick) | Kaserne weg 180 / 360 / 720 Ticks nach dem Befehl = 6,0 s; bei Welttick 220 / 400 / 760 | Befehlsticks und Ende über `Frames(…)` |
| 4 | Staged capture: the building flashes while the flag goes up | S | `SpecialAbilityStaging.cs`, `StepCaptureFlash`: `_flashPhase += (1 − prep/Preparation) / 3` je Tick | §5.2 | 4 / 9 / 19 Blitze in 2 s | Simulation: Phase je Originalbild |
| 5 | Projectiles: straight shell flight time, impact event and detonation effect | T | erwartet 20 Bahnpunkte; `_segments = ceil(length / Tempo je Tick)` | Rest: §5.2 `DumbProjectileBehavior` | 20 / 40 / 80 Abschnitte = 0,667 s; Aufschlag 0,700 / 0,683 / 0,675 s | auf 30 Hz festlegen (von Hand je Bild gerechnet) |
| 6 | Projectiles: ballistic arc path points and flight time | T | erwartet 14 Punkte, Literale `F+6`, `F+15` | Rest: §5.2 `CalcFlightPath` (Flugdauer in ganzen Ticks) | 14 / 27 / 53 Abschnitte = 0,467 / 0,450 / 0,442 s; Aufschlag 0,500 / 0,467 / 0,450 s; Scheitel 22,37 / 22,5 / 22,5 | auf 30 Hz festlegen; der Rest ist Sache der Suite |
| 7 | Projectiles: missile flight time (instant acceleration) | T | erwartet x = 10 nach einem Tick; `_speed = min(max, _speed + _acceleration)` je Tick | Rest: §5.2 `MissileAIUpdate.Move` | x nach einem Bild 10 / 7,5 / 6,25; Aufschlag 0,333 / 0,350 / 0,350 s | auf 30 Hz festlegen |
| 8 | Projectiles: missile ignition delay and acceleration | T | Tabelle von x je Tick | Rest: §5.2 `MissileAIUpdate.Move` | x nach Bild 10: 56 / 50 / 47,19; Aufschlag 0,500 / 0,500 / 0,517 s | auf 30 Hz festlegen; der Rückstand im Flug (bis 8,8 Einheiten) ist echt und Sache der Suite |
| 9 | Chinook: helicopter slow death falls, crashes and leaves a hull | T | erwartet `death + 1 + Frames(74)`; der Euler-Fall skaliert nicht so | Rest: §5.2 `HelicopterSlowDeathBehavior.Update` | 75 / 149 / 298 Ticks = 2,500 / 2,483 / 2,483 s (erwartet 297) | Erwartung als Bereich in Bildern |
| 10 | Collision: ToppleUpdate push, kill, stump and mid-fall snapshot continuation | S | `ToppleAndFallModules.cs`: `_angularAcceleration = speed * InitialAccelPercent` nimmt ein Tempo je Tick als Beschleunigung je Tick²; der Fall dauert 3–5 Updates gleich welcher Rate | §5.1 `InitialAccelPercent`, §5.2 `ToppleUpdate.Update`, `ResolveFootprintCollisions` | Fall 3 / 4 / 5 Ticks = 0,100 / 0,067 / 0,042 s; Stoß bei Schritt 4 / 7 / 13 | Simulation: Beschleunigung mit 30/Rate skalieren |
| 11 | Turrets: template model, defaults and strict parsing | T | `(TurnRate − PiFraction(1, 30)).Abs() <= Epsilon` ist eine Konstante je Tick bei 30 Hz – ein Ratenliteral, **kein** Rohwert des Formats (§14.9.2 beschreibt die Zeile noch in der Fassung vor Phase 22) | – | roh 1.756.906 / 878.453 / 439.226; mal Rate gibt π rad/s bei jeder Rate | `PiFraction(1, TicksPerSecond)` |
| 12 | Turrets: they turn at their rate toward the victim and the shot waits | S (klein) | `SimulationWorld.Turrets.cs`, `IsAligned`: feste 0,035 rad gegen 1,5° Schritt – nach 59 Schritten (88,5°) gilt der Turm schon als ausgerichtet | §5.2 `IsAligned`, §5.3 `StepTurrets`, §5.4 `_turretWaits` | Schuss im Schritt 16 / 31 / 60 = 0,533 / 0,517 / 0,500 s; `TurretWaits` 15 / 30 / 59 | Simulation (Freigabe am Raster) oder Toleranz ein Bild |
| 13 | Turrets: hold, recentre, rest and a victim that dies | S | wie 12 (`TicksUntilShot == Frames(15)`, gemessen 59) | wie 12 | wie 12 | wie 12 |
| 14 | Turret attack: a dead or departing victim ends it, a dead attacker leaves nothing | S | wie 12 (dieselbe Erwartung, derselbe Panzer; nicht einzeln vermessen, aus 12/13 geschlossen) | wie 12 | – | wie 12 |

Zu Nr. 2, Budget 2.000 s bei jeder Rate – ein Radiusschritt verschiebt den Ausgang so stark wie ein Ratenwechsel, auch bei 30 Hz:

| Radius der Arena | 30 Hz | 60 Hz | 120 Hz |
|---|---|---|---|
| 396 | Beta, 169,5 s | Beta, 751,5 s | kein Sieger |
| 397 | Beta, 431,5 s | Beta, 125,5 s | Alpha, 241,5 s |
| 398 | Alpha, 340 s | Alpha, 106,5 s | Beta, 530 s |
| 399 | Beta, 135 s | kein Sieger | Alpha, 342 s |
| 400 | kein Sieger | Beta, 1.180 s | Beta, 947,5 s |

Daraus: 9 der 14 sind Testerwartungen, 4 sind echte Kopplungen, die alle schon in §5 stehen (kein neuer Fund), 1 ist chaotisch. **Drei Tests bestehen bei 60 Hz nur zufällig** (Nr. 1 und Nr. 3 ohne jede Reserve, Nr. 10 mit einem Tick) und werden unabhängig von 120 Hz umgestellt. Nicht bestimmt: die erste abweichende Befehlszeile in Nr. 2 („keine einzelne Ursache“ stützt sich auf die Streuung der Tabelle); die Anteile der drei Kopplungen in Nr. 10; der Turmwinkel im Schussmoment in Nr. 12–14 (88,5° sind aus 59 × 1,5° gerechnet).

### 15.3 Die Skript-VM auf dem Originalraster: `MD_USA02` und die Kampagne bei 30, 60 und 120 Hz

**Befund.** Zielfahrer, dieselben Binärdateien, 30 gegen 60 Hz (`CampaignTool drive <content> --map "/NAME.map" [--tick-rate 60 --ticks 120000]`): auf 23 von 24 Karten lief das Endskript der Mission
zur selben Spielzeit (innerhalb 0,05 s), auf `MD_USA02` nicht – Sieg `END_Final_` bei 90,60 s gegen 340,57 s, Niederlage `Lose_END_Final_` bei 412,03 s gegen 529,52 s; der erste Schritt des
Fahrers („send named Truck01 to area Warehouse Lot“) wurde bei 30 Hz bei Tick 2.295 (76,5 s) möglich, bei 60 Hz bei Tick 19.590 (326,5 s).

**Ursache** (`CampaignTool script <content> --map MD_USA02 --name "Truck01 Deploy"`): das Skript `Global Timers` setzt den Bildtimer `Convoy Trucks` mit `SET_TIMER` auf 16.000; `Truck01 Deploy`
(Gruppe `Convoy Trucks Land`) erzeugt den ersten Lastwagen, sobald `COUNTER(Convoy Trucks < 15000)` gilt. Die VM führte Timer seit Stufe 1 in Ticks (32.000 bei 60 Hz, je Tick −1), das Literal der
Karte blieb 15.000: 17.001 Ticks = 283,4 s statt 1.001 Bilder = 33,4 s. Dieselbe Karte vergleicht weitere Timer mit Bildliteralen (Niederlage). Das sind die Zeilen „`Update` (W6)“ und „Timerwerte“ aus
§5.4 – kein neuer Fund, aber der erste gemessene.

**Korrektur** (ein Commit je Ursache; Beschreibung in `docs/SCRIPT-ENGINE.md` §2 „Die Uhr der VM ist das Originalbild“, Stelle des Auswegs in `docs/SIMULATION-CORE.md` §41):

| Commit | Ursache | Korrektur | 30 Hz |
|---|---|---|---|
| `727c4d6` | die VM wertete je Tick aus und zählte Timer in Ticks; das Original je Bild (`GameLogic::update`, `ScriptEngine::update`) | `ScriptEngine.Update` arbeitet nur auf Ticks, die ein Originalbild beginnen; Timer, Sequenzwartezeiten und Endspiel-Zeitgeber zählen Bilder; unter `FREEZE_TIME` zählt die VM die Aufrufe selbst; `CounterForDisplay` der Präsentation rechnet nicht mehr um | Eingabemanifest identisch, Manifest `quick` 9 von 9 |
| `2fe8b32` | `UNIT_EMPTIED` verglich mit dem vorigen **Tick**, die VM wertet aber je Bild aus – bei 60/120 Hz nie wahr | Vergleich mit dem vorigen Bild (`ScriptConditions::evaluateUnitHasEmptied`) | identisch, 9 von 9 |
| `d710835` | ein benanntes Objekt, das zwischen zwei Updates erscheint und verschwindet, galt als nie erzeugt (`didUnitExist`) – bei 120 Hz liegen drei Ticks dazwischen | das Merken „hat existiert“ läuft in jedem Tick; alles andere wartet auf das Bild | identisch, 9 von 9 |

**Was aus den Listen in §5.3/§5.4 von selbst folgt** (kein eigener Commit, weil es Teil desselben Updates ist): Brückenzustand ein Update lang, `AttachPendingTraps`, Reichweitenprüfung der Übernahme,
`ProcessTeamEvents` mit `WasIdle` und `_pendingTeamScripts`, `ProcessSequences` mit `_busyThisTick`, `ProcessTeamBuilds`, `UpdateTeamQueues`, `UpdateFollowers`, `SUPPLY_SOURCE_SAFE`, die Flanken
`PLAYER_LOST_OBJECT_TYPE` und `NAMED_/TEAM_ENTERED/EXITED_AREA*`, die Reihenfolge in `MissionSession.Step`. **Bewusst je Tick** bleiben `ScriptEngine.Observe`, die Auskünfte an den Zielerwerb der
Welt und die Annahme der Befehle der VM im Weltschritt. **Nicht angefasst:** der Zielfahrer (`Testing/ObjectiveDriver*.cs`) – sein Rhythmus war nicht die Ursache.

**Messung am Endstand `d710835`** (7./8. Oktober 2026, Release, Binärdateien aus einer Kopie, je Karte und Rate ein eigener Prozess, höchstens vier nebeneinander; 60.000 / 120.000 / 240.000 Ticks;
gezählt aus 72 Einzelprotokollen mit `victory REACHED by '…' @T`, Spielzeit = T / Rate). Vorab festgelegte Abnahme: dasselbe Endskript auf jeder Karte bei allen drei Raten; die Spielzeit innerhalb
± 1 Originalbild (0,034 s) der 30-Hz-Zeit, wo die Kette rein skriptgetrieben ist, sonst ± 2 % oder ± 1 s.

| Karte | 30 Hz: Sieg / Niederlage (s) | 60 Hz | 120 Hz | Endskripte (bei allen drei Raten dieselben) |
|---|---|---|---|---|
| `GC_AirGeneral` | 9,00 / 5,00 | 9,00 / 5,00 | 9,00 / 5,00 | `END_Final_GLA` / `Lose_END_Final_GLA` |
| `GC_ChemGeneral` | 9,00 / 5,00 | 9,00 / 5,00 | 9,00 / 5,00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_ChinaBoss` | 12,00 / 81,03 | 12,00 / 81,03 | 12,00 / 81,03 | `End the mission` / `Lose_END_Final_USA` |
| `GC_LaserGeneral` | 8,00 / 5,00 | 8,00 / 5,00 | 8,00 / 5,00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_NukeGeneral` | 8,00 / 5,00 | 8,00 / 5,00 | 8,00 / 5,00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_Stealth` | 8,00 / 5,00 | 8,00 / 5,00 | 8,00 / 5,00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_SuperWeaponsGeneral` | 8,00 / 5,00 | 8,00 / 5,00 | 8,00 / 5,00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `GC_TankGeneral` | 8,00 / 5,00 | 8,00 / 5,00 | 8,00 / 5,00 | `END_Final_USA` / `Lose_END_Final_USA` |
| `MD_CHI01` | 10,03 / 8,03 | 10,03 / 8,03 | 10,03 / 8,03 | `END_Final_` / `Lose_END_Final_` |
| `MD_CHI02` | 10,03 / 10,03 | 10,03 / 10,03 | 10,03 / 10,03 | `END_Final_` / `Lose_END_Final_` |
| `MD_CHI03` | 57,40 / 149,03 | 57,40 / 149,03 | 57,40 / 149,03 | `Cheer1Done` / `Lose_END_Final_` |
| `MD_CHI04` | 1608,60 / 9,10 | 1608,60 / 9,10 | 1608,60 / 9,10 | `END_Final_` / `Lose_END_Final_` |
| `MD_CHI05` | 88,73 / 8,03 | 88,73 / 8,03 | 88,73 / 8,03 | `CINE_ENDVictory` / `Lose_END_Final_` |
| `MD_GLA01` | 17,53 / 8,03 | 17,53 / 8,03 | 17,53 / 8,03 | `Cheer1Done` / `Lose_END_Final_` |
| `MD_GLA02` | 8,00 / 19,07 | 8,00 / 19,07 | 8,00 / 19,07 | `END_Final_` / `Lose_END_Final_` |
| `MD_GLA03` | 134,43 / 8,07 | 134,43 / 8,07 | 134,43 / 8,07 | `CINE_ENDVictory` / `Lose_END_Final_` |
| `MD_GLA04` | 34,07 / 8,03 | 34,07 / 8,03 | 34,07 / 8,03 | `CINE_END` / `Lose_END_Final_` |
| `MD_GLA05` | 46,47 / 34,53 | 46,47 / 34,53 | 46,47 / 34,53 | `CINE_END04END` / `Lose_END_Final_` |
| `MD_USA01` | 26,17 / 7,03 | 26,17 / 7,03 | 26,17 / 7,03 | `END_Final_USA` / `Lose_END_Final_USA` |
| `MD_USA02` | 90,60 / 412,03 | 90,60 / 412,03 | 90,60 / 412,03 | `END_Final_` / `Lose_END_Final_` |
| `MD_USA03` | 50,30 / 8,03 | 50,30 / 8,03 | 50,30 / 8,03 | `CINE_AVVictory` / `Lose_END_Final_` |
| `MD_USA04` | 10,10 / 20,57 | 10,10 / 20,57 | 10,10 / 20,57 | `END_Final_USA` / `Lose_END_Final_USA` |
| `MD_USA05` | 48,10 / 42,67 | 48,10 / 42,67 | 48,10 / 42,67 | `END_Fade 04` / `Lose_END_Final_` |
| `Training01` | 14,00 / 88,50 | 14,00 / 88,50 | 14,00 / 88,50 | `Player Wins` / `Player Loses` |

Ergebnis: Sieg **24 von 24** und Niederlage **24 von 24** bei 30, 60 und 120 Hz, auf jeder Karte dieselben Endskripte, größte Abweichung von der 30-Hz-Zeit über alle 144 Zeitpunkte **0,00 s** – jede
Karte liegt innerhalb von ± 1 Originalbild, auch die, deren Kette an Bewegung und Gefecht hängt. (Die Welteingriffe des Fahrers ersetzen dort das Gefecht; das belegt die Skriptkette, nicht das
Fahrverhalten.) Dieselbe Tabelle ergab der Lauf am Stand `727c4d6`. Vor der Korrektur bei 60 Hz: `MD_USA02` 340,57 / 529,52 s, alle übrigen Karten innerhalb 0,05 s; für 120 Hz gibt es keine Messung
des Standes davor.

Weitere Läufe am Endstand:

| Lauf | Ergebnis |
|---|---|
| `SimTool input-manifest check` | identisch |
| `SimTool reference-manifest check --set full --jobs 6` | **50 von 50 Läufen identisch** |
| `CampaignTool audit --ticks 900` (30 Hz, 24 Karten einzeln) | 24 von 24 deterministisch, 24 von 24 identisch nach Speichern/Laden, 0 Treffer auf fehlende Typen, Endproben Sieg 13 von 24, Niederlage 15 von 24 – unverändert |
| `CampaignTool audit --tick-rate 60 --ticks 1800` | 24 von 24 deterministisch, 24 von 24 identisch nach Speichern/Laden, Endproben Sieg 13 von 24, Niederlage 15 von 24 |
| Simulationstests mit `--content-dir` | 30 Hz 442 von 442; `--tick-rate 60` die sechs bekannten Ausfälle (§14.9.2), sonst keiner; `--tick-rate 120` die 14 aus §15.2 |
| neue Tests (`Tests.TickRate.ScriptClock.cs`, `Tests.TickRate.ScriptEvents.cs`), je bei 30/60/120 Hz in einem Lauf | 9 von 9 |
| `Game.Tests -- --only TimeScaleTests,SourceRuleTests,CampaignTests` (Release) | bestanden (der benannte Countdown liest bei allen drei Raten dieselbe Zeit) |
| `dotnet build CnCNew.slnx` | 0 Warnungen |

**Nicht belegt bzw. offen:** Der Audit speichert an einem festen Tick; dass ein Stand **zwischen** zwei Bildern identisch fortsetzt, belegt nur der synthetische Test (auch eingefroren), kein Lauf
mit Realdaten. Ein UI-Signal (`SignalUiInteraction`) zwischen zwei Updates steht wie bisher nicht im gespeicherten Zustand (bei 30 Hz dieselbe Lücke zwischen Signal und nächstem Schritt, bei
60/120 Hz bis zu einem Bild lang). Die drei Felder des Skriptzustands mit neuer Bedeutung bei 60/120 Hz (Timerwert, Sequenzwartezeit, `endTimer`: Bilder statt Ticks) haben keine neue
Formatversion. Die Standbefehle der VM (`UpdateFollowers`, `FireAtOrdered`) laufen je Bild – ob das im Spiel bei 60 Hz anders aussieht als ein Befehl je Tick, hat niemand gesehen. Der
2-s-Zwischenspeicher von `SUPPLY_SOURCE_SAFE` fehlt weiter. Kein Mensch hat eine Mission bei 60 oder 120 Hz gespielt.

### 15.4 Messrunden, Stand nach zwei Runden und die Entscheidung des Projektinhabers vom 8. Oktober 2026

Die Raten-Äquivalenz-Suite (`docs/SIMULATION-CORE.md` §39) und der Verteilungsvergleich (§40) wurden mit vorab festgelegten Toleranzen bzw. Testgrenzen gebaut; keine davon ist seither geändert worden. Größen außerhalb der Toleranz, von 4.307 (60 Hz / 120 Hz):

| Stand | Commit | Suite | Verteilungsvergleich 30 gegen 60 Hz (320 Matches je Rate) |
|---|---|---|---|
| Ausgang (vor jeder Korrektur) | `91cb830` | 479 / 625 | 38 von 39 Tests bestanden; durchgefallen: Anteil entschiedener Matches 79,1 % → 70,0 %, z = −2,63 (Grenze 1,96) |
| Runde 1: sieben Korrekturpakete (Geschosse, Skript-VM, Physikmodule, Zeitversatz, Bodenbewegung, Flug, Türme/Feuer) | `62fbb6a` | 63 / 64 | 39 von 39; Anteil entschiedener 79,1 % → 76,3 %, z = −0,85; Dauer D = −0,10 min (SE 0,37) |
| Runde 2: Konsolidierung (eine Bildkonvention über alle Pakete, `docs/RATE-INVARIANCE-CONSOLIDATION.md`) | `3b44506` | 35 / 33 | 39 von 39; Anteil entschiedener 79,1 % → 76,6 %, z = −0,76; Dauer D = −0,11 min (SE 0,39) |

Bei 30 Hz hat sich in beiden Runden nichts geändert: das volle Referenzmanifest ist am Stand `3b44506` identisch (50 von 50 Läufen), das Eingangsmanifest ebenso, und die 30-Hz-Erhebung über 320 Matches ist nach beiden Runden Byte für Byte dieselbe Datei wie vor den Korrekturen. Am Stand `3b44506` bestehen alle Testprogramme, die Simulationstests auch mit `--tick-rate 60` und `--tick-rate 120`; der Zielfahrer erreicht auf allen 24 Kampagnenkarten bei 30, 60 und 120 Hz Sieg und Niederlage über dasselbe Endskript zur selben Spielzeit (Abweichung 0,00 s), das Audit ist bei 30 und 60 Hz auf 24 von 24 Karten deterministisch und nach Speichern/Laden identisch (Endproben 13/24 und 15/24).

Nach Runde 2 außerhalb der Toleranz, je Gruppe (60 / 120 Hz): `ai` 24 / 23 (bis zum Beginn des ersten Gefechts bei 117,9 s sind alle Positionen zwischen 30 und 60 Hz bitgleich; danach liest der nächste Leser noch nicht die Bildende-Sicht – die Blickrichtung eines Schützen, der mitten im Bild anhält), `projectiles` 4 / 4 (Streusalven, nicht zurückverfolgt), `turrets` 4 / 0 (Wachdrohne, `DeployStyleAIUpdate`), `flight` 0 / 3 (Ankunftsschwellen des Flügelflugs bei 120 Hz), `locomotion` 2 / 2 (Mob-Mitglieder lesen ihren Nexus an der Momentanposition), `timed` 1 / 1 (`CINE_BShipWeaponDetonationObject` endet bei 30 Hz nie – ein Fehler bei 30 Hz); `weapons`, `overtime`, `economy`, `scripts` 0 / 0.

**Entscheidung.** Der Auftrag der Phase enthielt ein Abbruchkriterium: bestehen Suite oder Verteilungsvergleich nach zwei Messrunden je Gruppe nicht, bleibt der Standard bei 30 Hz. Nach Runde 2 bestand der Vergleich, die Suite nicht vollständig. Der Projektinhaber hat das Kriterium am 8. Oktober 2026 **aufgehoben**: das Ziel der Phase – die Suite besteht bei 30, 60 und 120 Hz, danach ist 60 Hz der Standard – ist zu erreichen, und alles, was auf dem Weg dorthin liegt, wird behoben. Das schließt Korrekturen ein, die das Verhalten bei 30 Hz ändern, wo die Ursache ein Fehler bei 30 Hz ist; sie bleiben je eine Ursache in einem eigenen Commit mit neu geschriebenem Manifest (`docs/MULTIPLAYER-PLAN.md` §6 Regeln 2 und 3). Unverändert gilt: keine Toleranz und keine Testgrenze wird an ein Ergebnis angepasst.

### 15.5 Die Umstellung des Standards: Inventar

Stand 8. Oktober 2026, Ausgang `203806e`. Gesucht ist in `src/`, `tools/`, `tests/` und `scripts/` nach `Hz30`, `SimulationRate.Original`, `DefaultSessionRate`, `ToolRate`, `TestRate`, `TickRate = 30`, `--tick-rate`, `tickRate` und nach Tickbudgets, die Spielzeit bei 30 Hz meinen. Die Tabelle nennt jede Stelle, die die **Standardrate** benennt oder unterstellt – nicht die Stellen, die das Originalbild meinen (`SimulationRate.Original` als Raster in der Simulation, `OriginalFrame.PerSecond` außerhalb) und nicht die Tests, die ihre Raten selbst nennen (Drei-Raten-Tests). „Vorbereitung“ heißt: in einem Commit vor der Umstellung so geändert, dass sich bei 30 Hz nichts ändert; „folgt“ heißt: die Stelle leitet ihren Wert ab und wird im Umstellungs-Commit nicht angefasst.

**Regel nach der Vorbereitung.** Die Standardrate ist an genau einer Stelle definiert: `src/CnCNew.Game/Data/ProductRate.cs` (`ProductRate.TicksPerSecond`). Die Simulation kennt keinen Standard – wer eine Welt erzeugt, nennt die Rate. Die Spielschicht macht daraus `GameData.DefaultSessionRate`; Client und Werkzeuge nehmen diesen Wert; die Testprojekte, die die Spielschicht nicht referenzieren dürfen (`Simulation.Tests`, `Presentation.Tests`, `Ui.Tests`), binden die Datei `ProductRate.cs` als Quelltext ein (`<Compile Include="../../src/CnCNew.Game/Data/ProductRate.cs" Link="…" />`), `Game.Tests` ebenso (für `tests/Shared/TestRateOption.cs`, das die Spielschicht nicht kennt). Was bei einer Rate aufgezeichnet ist (Referenz- und Eingangsmanifest, Speicherstände, Replays), trägt seine Rate in der Datei und folgt dem Standard nicht.

| Stelle | Was es ist | Bei der Umstellung |
|---|---|---|
| `src/CnCNew.Game/Data/ProductRate.cs`, `ProductRate.TicksPerSecond` | **die eine Definition** der Standardrate (neu; `internal const int`) | **die eine geänderte Zeile:** 30 → 60 |
| `src/CnCNew.Game/Data/GameData.cs`, `DefaultSessionRate` | Standard einer Sitzung, deren Host keine Rate nennt (`GameData.Load(…, rate: null)`); vorher `=> SimulationRate.Original` | Vorbereitung: aus `ProductRate`; folgt |
| `src/CnCNew.Client.Stride/Program.cs` (`--tick-rate`), `Game/Shell/GameShell.cs` (`CreateLoading(…, rate)`) | Rate des Clients ohne Option: `GameData.DefaultSessionRate` | folgt |
| `src/CnCNew.Client.Stride/Program.cs` (`--match`), `Game/Shell/Screens/SkirmishScreens.cs`, `tools/CnCNew.UiRenderTool/HudFeedbackScenes.cs` | starteten ein Gefecht mit `SkirmishMatchOptions.Standard`, dessen `MaxTicks` 108.000 **Ticks** sind: eine Stunde bei 30 Hz, bei 60 Hz wäre das Gefecht nach 30 Minuten beendet worden | Vorbereitung: `SkirmishMatchOptions.StandardAt(rate)` (eine Stunde Spielzeit bei jeder Rate); folgt |
| `src/CnCNew.Simulation/Testing/SkirmishMatchScenario.cs`, `ReferenceMatchScenario.cs` | `Standard` mit Tickbudgets (108.000 bzw. 216.000), die Spielzeit bei 30 Hz meinen | Vorbereitung: zusätzlich `StandardAt(SimulationRate)`; `Standard` bleibt für Aufrufer mit eigenem Budget |
| `src/CnCNew.Simulation/Testing/StressScenario.cs`, `StressOptions.Standard` | 216.000 **Schritte**, Abbau 6.000 Schritte, Kontrollpunkte alle 1.000 | bleibt (Entscheidung unten) |
| `src/CnCNew.Simulation/Mission/MissionSession.cs`, `MissionOptions.Standard` | `MaxTicks` 0 (kein Budget), `PresentationLatencyTicks` 0 (Bilder), `CombatIntervalTicks` 3 (Schalter) | nichts zu tun |
| `src/CnCNew.Simulation/Skirmish/Original/ScriptedSkirmishSession.cs`, `ScriptedSkirmishOptions.Standard` | `MaxTicks` 108.000 Ticks; einziger Aufrufer außerhalb der Tests ist `SimTool original-ai`, das das Budget über `ToolRate.Budget` in Spielzeit umrechnet | nichts zu tun |
| `src/CnCNew.Simulation/Time/SimulationRate.cs`, `Combine`; `tools/CnCNew.SimTool/InputManifest.cs`, `RateAtOriginal` | bei 30 Hz bleibt der Fingerabdruck ohne Ratenanteil, damit alles bei 30 Hz Aufgezeichnete seine Identität behält; bei 60 und 120 Hz geht die Rate ein | bleibt: das ist die Identität der 30-Hz-Stände, kein Standard. Folge: der Welt-Fingerabdruck des Standards ist nach der Umstellung ein anderer (Eingangsmanifest §15.6) |
| `tools/CnCNew.SimTool/ToolRate.cs`, `Current` (vorher `= SimulationRate.Hz30`) | Rate eines Werkzeuglaufs ohne `--tick-rate` | Vorbereitung: `GameData.DefaultSessionRate`; folgt |
| `tools/CnCNew.SimTool/ToolRate.cs`, `Manifest` (vorher `=> SimulationRate.Hz30`) und der Satz „The manifest commands always run at the recorded 30 Hz“ (`Program.cs`, §9 dieses Dokuments) | die Manifestbefehle liefen mit einer Konstante des Werkzeugs und lehnten jede andere `--tick-rate` ab | Vorbereitung: entfällt. `check` läuft mit der Rate und in dem Schema, die **die Datei** nennt (`ToolRate.UseRecorded`), und lehnt eine abweichende `--tick-rate` ab; `write`, `show`, `results`, `list` nehmen `--tick-rate` oder den Standard |
| `tools/CnCNew.SimTool/ReferenceManifest.cs`, `AiMatchTicks` 45.000, `EightPlayerTicks` 9.000, `EightPlayerQuickTicks` 3.000, `SweepTicks` 3.000, `MissionTicks` 3.000, `ReferenceMatchOptions.Standard.MaxTicks` 216.000 | Tickbudgets der Manifestläufe, die Spielzeit bei 30 Hz meinen | Vorbereitung: als Originalbilder (`…Frames`), je Lauf mit `rate.Time.OriginalFrames(n)` in Ticks; folgt |
| `tools/CnCNew.SimTool/ReferenceManifest.cs`, `InputManifest.cs`, `Schema` | Dateiform der Manifeste | Vorbereitung: Schema 2 (Entscheidung unten); `check` liest Schema 1 weiter |
| `tools/CnCNew.SimTool/Program.cs`, Standardbudgets (`ToolRate.Budget(args, …, Originalbilder)`) für `reference-match`, `ai-match`, `skirmish-sweep`, `mission-run`, `unit-audit`, `perf`, `original-ai`; `ai-survey`/`ai-census` (`--minutes`) | Spielzeit, schon vor diesem Paket | nichts zu tun |
| `tools/CnCNew.SimTool/Program.cs`, `stress --ticks` | Schritte | bleibt |
| `tools/CnCNew.CampaignTool/ToolRate.cs`, `Current` (vorher `= SimulationRate.Hz30`); Standardbudgets `drive` 2.000 s, `audit` 60 s, `playthrough` 30 s | Rate ohne Option; Budgets schon Spielzeit | Vorbereitung: `GameData.DefaultSessionRate`; folgt |
| `tools/CnCNew.PresentationTool/ScenarioAudit.cs`, `SyncPerfCommand.cs` (`--tick-rate`) | ohne Option `GameData.DefaultSessionRate` | folgt |
| `tools/CnCNew.PresentationTool/SyncPerfCommand.cs`, `--ticks` (Standard „18000“) | Tickbudget, das zehn Minuten bei 30 Hz meint | Vorbereitung: ohne `--ticks` 18.000 Originalbilder in Ticks der Rate; folgt |
| `tools/CnCNew.PresentationTool/FxAuditCommand.cs` | nennt `SimulationRate.Original` selbst (Effekte laufen auf dem Originalbild) | bleibt bei 30 Hz, mit diesem Grund (`docs/RATE-SWITCH-OUTER-TESTS.md` §7.6) |
| `tools/CnCNew.UiRenderTool/Program.cs` (`--tick-rate`) | ohne Option `GameData.DefaultSessionRate`; die Ausgabe ist ratenunabhängig | folgt |
| `tools/CnCNew.MapTool`, `IniTool`, `AssetTool`, `ImageTool`, `W3dTool` | bauen keine Welt und keinen Katalog je Rate | nichts zu tun |
| `tools/CnCNew.SimTool/RateAudit.cs`, `CatalogCost.cs`, `RateCompare.cs` | vergleichen Raten und nennen sie selbst (30 als Bezug) | bleibt |
| `tests/CnCNew.Simulation.Tests/TestTime.cs`, `TestRate.Default` (vorher ohne Option `SimulationRate.Original`) | Rate der Standard-Testwelten | Vorbereitung: ohne Option `TestRate.Product` aus `ProductRate`; folgt |
| `tests/Shared/TestRateOption.cs`, `TicksPerSecond` (vorher ohne Option 30), `tests/Shared/TestTime.cs`, `TestRate.Default` | dasselbe für `Presentation.Tests`, `Game.Tests`, `Ui.Tests` | Vorbereitung: `DefaultTicksPerSecond` aus `ProductRate`; folgt |
| erste Ausgabezeile der Testprogramme („… a probe, the reference is 30 Hz“) | erschien bei jeder Rate außer 30 Hz | Vorbereitung: erscheint, wenn die Rate des Laufs nicht der Standard ist, und nennt den Standard |
| `tests/CnCNew.Game.Tests/HotkeyTests.cs`, `SpectatorTests.cs` | luden `GameData` ohne Rate und liefen deshalb bei jeder `--tick-rate` mit dem Standard | Vorbereitung: `rate: TestRate.Default` |
| `tests/CnCNew.Simulation.Tests/Tests.State.cs` (Referenzmatch mit Realdaten), `tests/CnCNew.Game.Tests/DeterminismTests.cs` (D3, D4) | `ReferenceMatchOptions.Standard`: 216.000 Ticks sind 2 h / 1 h / 30 min | Vorbereitung: `StandardAt(rate)`, zwei Stunden Spielzeit bei jeder Rate |
| `tests/CnCNew.Game.Tests/SourceRuleTests.cs`, benannte Ausnahme für `GameData.cs` | „die eine Stelle außerhalb der Simulation, die eine Rate nennt“ | Vorbereitung: die Ausnahme gilt jetzt `ProductRate.cs`; der Text der Ausnahme nennt keine Zahl und passt vor und nach der Umstellung |
| Tests mit `SimulationRate.Hz30`/`Original` als genannter Rate (`Tests.TickRate*.cs`, `Tests.RateInvariance*.cs`, `Tests.Basics.cs`, `TimeScaleTests`, `CampaignTests` „Savegame: the header carries the tick rate“, Ablehnungstexte in `Tests.State.cs`) | nennen ihre Rate selbst | bleibt |
| `scripts/audit-scenarios.ps1`, `capture-scenarios.ps1`, `capture-text.ps1`, `capture-world.ps1`, `check-build-hotkeys.ps1`, `check-dozer-build.ps1`, `check-spectator.ps1`, `demo.ps1`, `demo-factory-exits.ps1`, `demo-spectator.ps1` (`-TickRate`, 0 bzw. nicht gesetzt = Standard des Clients/Werkzeugs) | reichen `--tick-rate` nur durch, wenn sie gesetzt ist | folgt |
| `scripts/perf-profile.ps1`, `-TickRate 30`, `$Ticks = 30 * 60 * $TickRate`, `100 * $TickRate`, `300 * $TickRate` | einzige Zahl einer Rate in einem Skript | Vorbereitung: `-TickRate 0` = Standard des Werkzeugs; Budgets als `--minutes 30` und `--seconds 100`/`300`; die Zusammenfassung liest die Rate aus der `summary:`-Zeile des Werkzeugs (`tick_rate`); folgt |
| `scripts/perf-compare.ps1`, `audit-vulnerable.ps1`, `demo-*.ps1` (reichen `@args` weiter) | keine Rate | nichts zu tun |

**Nach der Vorbereitung nennt außerhalb der Tests, die ihre Raten prüfen, nichts mehr „30 Hz“ als Standard.** Kontrolle: `grep -rnE "Hz30|SimulationRate\.Original" tools src/CnCNew.Game src/CnCNew.Client.Stride scripts` findet nur noch `fx-audit`, die Ratenvergleiche (`RateAudit`, `CatalogCost`) und in `InputManifest.cs` die Schema-1-Regel und `RateAtOriginal`.

#### 15.5.1 Manifeste: Rate und Schema stehen in der Datei

- **Rate.** `reference-manifest check` und `input-manifest check` lesen die Datei zuerst und laufen mit `simulation.tickRate` der Datei. Dieselbe Binärdatei prüft damit heute das 30-Hz-Manifest und nach der Umstellung das 60-Hz-Manifest. `--tick-rate N` darf die Rate der Datei wiederholen; nennt sie eine andere, bricht der Befehl ab („… records 30 Hz; --tick-rate 60 is refused. A manifest is checked at the rate it was written at.“). `write`, `show`, `results` und `list` laufen mit `--tick-rate N`, ohne Option mit dem Standard; die Kopfzeile der Ausgabe und die Schlusszeile nennen die Rate.
- **Schema 2 des Referenzmanifests** (`write`): die Parameter jedes Laufs nennen neben jedem Tickbudget die Spielzeit (`maxTicks` und `maxGameSeconds`, bei Missionen und beim Stresstest `ticks` und `gameSeconds`), und Werte, die Originalbilder sind, heißen so (`graceOriginalFrames`, `incomeIntervalOriginalFrames`, `sciencePointIntervalOriginalFrames` statt `graceTicks`, `incomeInterval`, `sciencePointInterval` – bei 30 Hz war der alte Name richtig, bei 60 Hz hätte `graceTicks: 300` fünf statt zehn Sekunden behauptet). Der Stresstest trägt zusätzlich `budgetCounts: "steps"`. `tickRate` steht wie bisher im Kopf und in jedem Lauf. **Die Ergebnisfelder sind in beiden Schemata dieselben.**
- **Schema 2 des Eingangsmanifests** (`write`): zusätzlich `simulation.tickRate`. Schema 1 hatte keinen solchen Eintrag und ist nach seiner Definition bei 30 Hz geschrieben.
- **Schema 1 wird weiter geprüft, in seiner eigenen Form.** `check` baut die Parameter so, wie das Schema der Datei sie schreibt; die eingecheckten 30-Hz-Dateien bleiben Byte für Byte, was sie sind, und brauchen vor der Umstellung **kein** Neuschreiben. Ein unbekanntes Schema und ein Referenzmanifest ohne `simulation.tickRate` werden mit Meldung abgelehnt. Der Schema-1-Pfad ist nach dem Schreiben der 60-Hz-Manifeste (Schema 2) nicht mehr nötig und kann dann entfallen.

#### 15.5.2 Budgets der Manifestläufe

| Lauf | Budget (Spielzeit) | Ticks bei 30 Hz | Ticks bei 60 Hz | Sicherungspunkt |
|---|---|---:|---:|---|
| `reference-match` | bis 2 h (`ReferenceMatchOptions.StandardAt`); endet 300 Bilder nach dem Sieg | 216.000 | 432.000 | halbe Länge des Replays |
| 16 KI-Läufe (4 Karten × 4 Paarungen) | 25 min | 45.000 | 90.000 | halbe Länge |
| `ai8_*` (3 Karten) | 5 min | 9.000 | 18.000 | halbe Länge |
| `ai8_TwilightFlame_short` | 100 s | 3.000 | 6.000 | halbe Länge |
| `skirmish-sweep` (je Karte) | 100 s, Nachlauf höchstens ein Viertel davon | 3.000 | 6.000 | – |
| 27 Missionsläufe | 100 s | 3.000 | 6.000 | Speichern bei halber Länge |
| `stress` | **216.000 Schritte** | 216.000 (2 h) | 216.000 (1 h) | halbe Länge |

Bei 30 Hz ist jede Zahl die frühere; `reference-manifest check` der eingecheckten Datei ist identisch (§15.6). Die Sicherungspunkte leiten sich aus der Lauflänge ab und brauchten keine Änderung. Nach `docs/TICKRATE-MIGRATION.md` §8.1 geprüft: `MaxTicks`, `MinTicks` (0) und `CheckpointInterval` der Optionen sind Ticks – skaliert wird nur `MaxTicks`; `CheckpointInterval` (1.000 Ticks) bleibt eine Tickzahl (bei 60 Hz doppelt so viele Kontrollpunkte je Spielzeit im Replay, kein Einfluss auf den Lauf); `GraceTicks`, `IncomeInterval`, `SciencePointInterval` sind Originalbilder und werden von den Sitzungen selbst umgerechnet.

**Entscheidung Stresstest.** Zur Wahl standen: (a) 216.000 Schritte behalten – bei 60 Hz ist das eine Stunde Spielzeit; (b) 432.000 Schritte – zwei Stunden Spielzeit bei 60 Hz, doppelte Laufzeit; (c) den Zeitplan des Tests in Spielzeit ausdrücken – halbe Last je Schritt bei 60 Hz. Gewählt ist **(a)**. Der Zweck des Tests ist die Last je Simulationsschritt (je Tick ein Abgang und ein Ersatz, Funken jeden zweiten, Rauch jeden dritten Tick, §8.3) und die Stabilität über eine feste Zahl von Schritten; beides bleibt bei (a) genau erhalten, (c) würde etwas anderes messen, und (b) verdoppelt nur die Dauer, ohne die Last je Schritt zu ändern. Die Aussage des Handoffs „Stresstest 216.000 Ticks ≙ 2 h“ gilt damit bei 60 Hz nicht mehr: das Manifest nennt 216.000 Schritte und `gameSeconds` 3.600. Bei 60 Hz ist der Lauf ein anderer als bei 30 Hz (die Lebensdauern der Fixture sind Millisekunden und dauern doppelt so viele Schritte), also mit neuem Endhash.

#### 15.5.3 Formate

| Format (Version) | Gespeicherte Tick- oder Dauerfelder | Lesart | Befund |
|---|---|---|---|
| Replay-Kopf (12) | `tickRate`, `endTick`, `checkpointInterval`; `Tick` jedes Befehls, `SetTimerCommand.DelayTicks`/`RepeatTicks` | Ticks der Rate im Kopf | Zustand zur gespeicherten Rate. Die Welt entsteht mit der Rate des Kopfs; ein Katalog einer anderen Rate wird abgelehnt („The replay was recorded at 30 Hz; the catalog was built for 60 Hz.“) |
| `WorldSnapshot` (10) | `tickRate`, `tick`, alle Weckzeiten, Zeitgeber und Werte je Tick im Zustand | Ticks der gespeicherten Rate | Zustand zur gespeicherten Rate; Ablehnung „The snapshot was written at 30 Hz; the catalog was built for 60 Hz.“ |
| `MissionSnapshot` (3) | `tickRate`; `maxTicks` | Tickbudget der gespeicherten Rate (0 = keines) | Zustand zur gespeicherten Rate – der Stand läuft mit seiner Rate weiter, das Budget meint dieselbe Stelle |
| | `presentationLatencyTicks` | Originalbilder (der Wert der Option, wie er übergeben wurde; `ScriptEngine.Forward` rechnet um) | ratenunabhängig; der Name ist irreführend, die Bytes sind richtig |
| | `combatIntervalTicks` | Schalter (0 oder nicht 0) | keine Zeit |
| | Zähler und Timer der VM | Originalbilder seit Phase 23 (§15.3) | ratenunabhängig |
| `ScriptedSkirmishSession`-Stand | `tickRate`, `maxTicks`, `graceFrames`, `sciencePointIntervalFrames` | Ticks der gespeicherten Rate bzw. Originalbilder | wie oben |
| `SaveGame` (3) | `tickRate`, `tick`; eingebetteter `MissionSnapshot` | Ticks der gespeicherten Rate; Listen zeigen Spielzeit | ein Stand einer anderen Rate als der der Sitzung wird abgelehnt („This savegame was written at 30 Hz; this session runs at 60 Hz. It cannot be loaded.“) |

**Kein gespeichertes Feld ist ein Standard, der nach der Umstellung falsch gelesen würde; kein Byte ändert sich, keine Version wird erhöht.** Folge der Umstellung für Bestände: ein bei 30 Hz geschriebener Spielstand lädt in einer Sitzung mit 30 Hz (Client `--tick-rate 30`) und läuft dort mit 30 Hz weiter; der Client ohne Option (60 Hz) lehnt ihn mit der vorhandenen Meldung ab. Die Shell bietet keine Wahl der Rate und lädt einen Stand nicht mit dessen Rate nach – wer alte Stände weiterspielen will, startet den Client mit der Rate des Stands. Das ist nach `docs/MULTIPLAYER-PLAN.md` §6 Regel 7 zulässig (keine Migrationspflicht vor der ersten Veröffentlichung) und als Grenze benannt.

Tests mit einer bei 30 Hz geschriebenen Fixture, unabhängig von der Standardrate: `Tests.TickRateStatesWrittenAtThirtyOutliveTheDefault` (neu, `Simulation.Tests`: Replay, `WorldSnapshot` und `MissionSnapshot` bei 30 Hz geschrieben, mit 30-Hz-Katalog fortgesetzt bis zum gespeicherten Budget, von Katalogen mit 60 und 120 Hz und der Standardrate mit Meldung abgelehnt) und `CampaignTests` „Savegame: the header carries the tick rate …“ (vorhanden: ein 30-Hz-Stand lädt in eine 30-Hz-Sitzung und wird von einer 60-Hz-Sitzung abgelehnt, und umgekehrt).

### 15.6 Die Umstellung: was sich ändert, Probewerte bei 60 Hz, Golden-Werte

Stand 8. Oktober 2026. Die Vorbereitung sind fünf Commits (`0cb2ddf`, `d1402fe`, `632d189`, `80db1df`, `c112b17`), die Umstellung ist `809ef04`. Alles in diesem Abschnitt ist auf einem Rechner gemessen, mit mehreren Läufen nebeneinander (Laufzeiten sind keine Leistungsangaben); gemessen ist der Quellstand des Umstellungs-Commits (der Arbeitsbaum, aus dem die Binärdateien stammen, ist Byte für Byte der Commit). **Nichts davon hat ein Mensch gesehen oder gehört.** Die eingecheckten Manifeste unter `baselines/` sind in diesem Paket nicht verändert; die 60-Hz-Manifeste schreibt der Projektinhaber in einem eigenen Commit.

#### 15.6.1 Was der Umstellungs-Commit ändert

Eine Zeile: `ProductRate.TicksPerSecond` 30 → 60 (`src/CnCNew.Game/Data/ProductRate.cs`). Daraus folgt ohne weitere Änderung: `GameData.DefaultSessionRate`, der Client ohne `--tick-rate` (Rauchtest: „session: simulation rate 60 Hz“), `SimTool`, `CampaignTool`, `PresentationTool`, `UiRenderTool` ohne `--tick-rate`, die Standardwelten aller Testprogramme und über die Werkzeuge alle Skripte. Der Welt-Fingerabdruck einer Standardsitzung ist ein anderer (die Rate geht bei 60 Hz in den Fingerabdruck ein, §15.5).

**Vorbereitung, je Commit geprüft** (`SimTool` Release gebaut, `input-manifest check` und `reference-manifest check --set quick --jobs 3` gegen die eingecheckten Dateien):

| Commit | Inhalt | Eingangsmanifest | Referenzmanifest (Schnellsatz, 9 Läufe) |
|---|---|---|---|
| `0cb2ddf` | eine Definition der Standardrate (`ProductRate`), Tests und `CampaignTool` leiten ab | identisch | identisch |
| `d1402fe` | Manifestbefehle mit der Rate und dem Schema der Datei, Budgets als Spielzeit, Schema 2 | identisch (30 Hz, Schema 1) | identisch (30 Hz, Schema 1) |
| `632d189` | Standardgefecht eine Stunde Spielzeit bei jeder Rate (`SkirmishMatchOptions.StandardAt`) | identisch | identisch |
| `80db1df` | Tests: Referenzmatch-Budget als Spielzeit, bei 30 Hz geschriebene Stände | nur Testcode, nicht eigens geprüft | nur Testcode, nicht eigens geprüft |
| `c112b17` | `sync-perf` und `perf-profile.ps1`: Budgets als Spielzeit, keine Rate im Skript | – | **voller Satz am Ende der Vorbereitung: Eingangsmanifest identisch, Referenzmanifest 50 von 50 Läufen identisch (30 Hz, Schema 1)** |

Am Ende der Vorbereitung (Standard noch 30 Hz) dazu: `Simulation.Tests` ohne Optionen 513 von 513, `Game.Tests --only SourceRuleTests,TimeScaleTests,CampaignTests` bestanden.

**Nur der Standard hat sich bewegt** (Binärdatei *nach* der Umstellung, Standard 60 Hz, gegen die eingecheckten 30-Hz-Dateien):

| Befehl | Ergebnis |
|---|---|
| `input-manifest check <content> baselines/input-manifest.json` | „Input manifest (30 Hz, schema 1): identical.“ |
| `reference-manifest check <content> baselines/reference-manifest.json --set full --jobs 5` (ohne `--tick-rate`; die Rate kommt aus der Datei) | „Reference manifest (full, 30 Hz, schema 1): 50 runs identical.“ |
| dasselbe mit `--set quick --tick-rate 30` | „Reference manifest (quick, 30 Hz, schema 1): 9 runs identical.“ |
| dasselbe mit `--tick-rate 60` | abgelehnt: „… records 30 Hz; --tick-rate 60 is refused. A manifest is checked at the rate it was written at.“ |

**Braucht die 30-Hz-Datei vor der Umstellung ein Neuschreiben? Nein.** `check` prüft sie in ihrem Schema 1 unverändert. Zusätzlicher Beleg, dass Schema 2 kein Ergebnis berührt: mit derselben Binärdatei ist ein 30-Hz-Manifest in Schema 2 in das Scratch-Verzeichnis geschrieben (`reference-manifest write … --set full --tick-rate 30`); gegen die eingecheckte Datei verglichen sind Kopf, Liste der Läufe, `kind` und `set` gleich, **die `results` aller 50 Läufe gleich**, jeder Parameter aus Schema 1 steht mit demselben Wert unter seinem Schema-2-Namen, und hinzugekommen sind genau `maxGameSeconds`, `gameSeconds` und `budgetCounts`. Das Eingangsmanifest in Schema 2 bei 30 Hz unterscheidet sich von der eingecheckten Datei in genau zwei Stellen: `schema` 2 und `simulation.tickRate` 30 (`diff`). Wer die 30-Hz-Dateien trotzdem vor der Umstellung in Schema 2 haben will, schreibt sie mit `reference-manifest write … --tick-rate 30` und `input-manifest write … --tick-rate 30`; nötig ist es nicht.

#### 15.6.2 Testprogramme mit angewandter Umstellung

Release-Build, Realdaten `.local/phase2-import-a`; gezählt sind Testzeilen aus den Einzelprotokollen (bestanden / fehlgeschlagen). „ohne Option“ ist jetzt 60 Hz.

| Programm | ohne Option (60 Hz) | `--tick-rate 30` | `--tick-rate 120` |
|---|---|---|---|
| `Archives.Tests`, `Content.Tests`, `IO.Tests`, `Images.Tests`, `Formats.Tests`, `Import.Tests` | bestanden | bestanden | bestanden |
| `Data.Tests` ohne und mit `--content-dir` | bestanden | bestanden | bestanden |
| `W3D.Tests --content-dir`, `Audio.Tests --content-dir` | bestanden | bestanden | bestanden |
| `Simulation.Tests` | 513 / 0 | 513 / 0 | 513 / 0 |
| `Simulation.Tests --content-dir` | 542 / 0 | 542 / 0 | 542 / 0 |
| `Simulation.Tests -c Release -- --long` | 514 / 0 | 514 / 0 | 514 / 0 |
| `Ui.Tests` ohne und mit `--content-dir` (fünf Suiten) | bestanden | bestanden | bestanden |
| `Presentation.Tests` | 155 / 0 | 155 / 0 | 155 / 0 |
| `Presentation.Tests --content-dir` | 172 / 0 | 172 / 0 | 172 / 0 |
| `Game.Tests` (Release) `--content-dir` | 162 / 0 | 162 / 0 | 162 / 0 |

Die neun Programme der ersten drei Zeilen bauen keine Welt; sie kennen `--tick-rate` nicht und übergehen die Option (die Läufe „30“ und „120“ sind dort derselbe Lauf). Die Raten-Äquivalenz-Suite in `Simulation.Tests --content-dir` meldet in allen zehn Gruppen 0 Größen außerhalb der Toleranz bei 60 und bei 120 Hz. Das Referenzmatch der Simulationstests (Katalog ohne Modellgeometrie, Budget jetzt zwei Stunden Spielzeit) endet bei jeder Rate mit Sieger: FactionGLA bei Tick 14.130 / 29.130 / 49.560 (7:51 / 8:06 / 6:53 min) – bei 120 Hz war dieser Test mit dem Tickbudget rot (`docs/RATE-SWITCH-OUTER-TESTS.md` §7.4).

**Kein Test schlägt fehl, weil ein Golden-Wert bei 30 Hz aufgezeichnet wäre.** Die eingecheckten Golden-Dateien (`tests/CnCNew.Game.Tests/baselines/screens.txt` mit 54 Hashes, `hud-drawlist-1280x720.sha256`) und die Golden-Konstanten im Testcode (`DrawListGolden`, Knochentabelle D1, Dreieckszahl der Straßen) bestehen unverändert bei allen drei Raten. Es gibt deshalb **keinen** Commit „Golden-Werte bei 60 Hz“.

Sauberer Build mit der Umstellung: `dotnet build CnCNew.slnx` mit gelöschtem `bin`/`obj` von `CnCNew.Client.Stride` 0 Warnungen, 0 Fehler; `dotnet build tools/CnCNew.UiRenderTool` 0 Warnungen, 0 Fehler.

#### 15.6.3 Probe-Manifest bei 60 Hz (nur im Scratch-Verzeichnis)

**Überholt (9. Oktober 2026).** Die Probewerte dieses Abschnitts und die Läufe in §15.6.4 stammen vom Stand der Umstellungs-Vorbereitung, **vor** Runde 4 (`1b82e45`). Die eingecheckten 60-Hz-Manifeste (`ea52f87`) sind danach geschrieben und tragen andere Werte: Referenzmatch Sieger FactionGLA bei Tick 28.260 (Bild 14.130) statt 29.130, Endhash `CA05181C36C62C7A` statt `50A37479CA9367CF`, 16 von 16 KI-Läufen mit demselben Ausgang wie bei 30 Hz statt 13. Maßgeblich ist §15.7; die Tabellen bleiben als Protokoll des Zwischenstands stehen. Unverändert gültig sind die Werte des Stresstests (`9EE9F730D124C3BD` / `F338463BE66D99C0`).

`reference-manifest write <content> <scratch>\reference-manifest-60.json --set full --jobs 5` (818 s, kein Lauf mit falschem Wiederholungsmerker) und `input-manifest write <content> <scratch>\input-manifest-60.json`; danach `check` gegen die Probedateien: Eingangsmanifest „(60 Hz, schema 2): identical“, Referenzmanifest **zweimal 50 von 50 Läufen identisch** (Schlusszeile beider Läufe: „Reference manifest (full, 60 Hz, schema 2): 50 runs identical.“; dazu der Schnellsatz und das Eingangsmanifest mit einer aus dem Umstellungs-Commit gebauten Binärdatei: 9 von 9 identisch, identisch). Kopf der Probedatei: `tickRate` 60, `schema` 2, Versionen unverändert (`commandTableVersion` 15, `replayFormatVersion` 12, `worldSnapshotVersion` 10, `missionSnapshotVersion` 3).

| Lauf | 30 Hz (eingecheckt) | 60 Hz (Probe) |
|---|---|---|
| `reference-match` | Sieger FactionGLA, Tick 14.130 (7:51,0 min), Endhash `5BF9BB7CED6D7A35`, Kette `56149C0105E992D7`, Replay 14.431 Ticks | Sieger FactionGLA, Tick 29.130 (8:05,5 min), Endhash `50A37479CA9367CF`, Kette `A02EF82C24AFCE0C`, Replay 29.731 Ticks |
| `stress` (216.000 Schritte) | Endhash `56882F763EABEF67`, Kette `754FA4DCB9A41DF7` | Endhash `9EE9F730D124C3BD`, Kette `F338463BE66D99C0` |
| 16 KI-Läufe | 14 entschieden | 14 entschieden |
| 4 Acht-Spieler-Läufe | alle Patt am Budget | alle Patt am Budget |
| `skirmish-sweep` | 64 gestartet und beendet, 0 fehlgeschlagen, 0 mit Sieger, 64 am Budget; 75 Karten ohne, 11 mit einer Startposition; `493C72CF9D2AE2E9…` | dieselben Zähler; `E21219D7804BB22B…` |
| 27 Missionsläufe (100 s) | alle ohne Ausgang im Budget | alle ohne Ausgang im Budget; alle drei Wiederholungsmerker wahr auf 27 von 27 |

Die 16 KI-Läufe und die Acht-Spieler-Läufe (Sieger, Spielzeit des Siegs min:s, Siegtick):

| Lauf | 30 Hz: Ausgang, Spielzeit | 60 Hz: Ausgang, Spielzeit | Endhash 60 Hz |
|---|---|---|---|
| `AlpineAssault_America-China` | Patt | Patt | `58A6A55F05EADD4F` |
| `AlpineAssault_China-GLA` | China 4:46.5 (Tick 8595) | China 4:46.5 (Tick 17190) | `2ED97C970A583EFC` |
| `AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral` | ChinaTankGeneral 10:10.0 (Tick 18300) | ChinaTankGeneral 9:32.0 (Tick 34320) | `047D1B00C7441E77` |
| `AlpineAssault_GLA-America` | America 5:22.0 (Tick 9660) | America 5:22.0 (Tick 19320) | `C897120A7DAC6A30` |
| `ArmoredFury_America-China` | America 20:35.0 (Tick 37050) | Patt | `C2D04A71C519EC23` |
| `ArmoredFury_China-GLA` | Patt | China 21:54.5 (Tick 78870) | `BE1D51852D99DD51` |
| `ArmoredFury_ChinaTankGeneral-AmericaAirForceGeneral` | ChinaTankGeneral 6:02.0 (Tick 10860) | ChinaTankGeneral 6:02.0 (Tick 21720) | `64ACBBC886C95BE7` |
| `ArmoredFury_GLA-America` | America 20:06.0 (Tick 36180) | America 14:10.0 (Tick 51000) | `25CC536DD88592E3` |
| `BitterWinter_America-China` | China 12:13.5 (Tick 22005) | America 11:33.5 (Tick 41610) | `F79B173AAE20CA02` |
| `BitterWinter_China-GLA` | China 4:39.5 (Tick 8385) | China 4:39.5 (Tick 16770) | `D6BF8631997D9F8F` |
| `BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral` | ChinaTankGeneral 16:45.0 (Tick 30150) | ChinaTankGeneral 12:34.5 (Tick 45270) | `64E866CABEC459EE` |
| `BitterWinter_GLA-America` | America 4:51.0 (Tick 8730) | America 4:51.0 (Tick 17460) | `090185887CFBA7B8` |
| `DeathValley_America-China` | America 4:50.0 (Tick 8700) | America 4:50.0 (Tick 17400) | `4137ABC50FED8435` |
| `DeathValley_China-GLA` | China 7:11.0 (Tick 12930) | China 7:11.0 (Tick 25860) | `41EE8526DBCCC99C` |
| `DeathValley_ChinaTankGeneral-AmericaAirForceGeneral` | ChinaTankGeneral 12:42.0 (Tick 22860) | ChinaTankGeneral 8:41.0 (Tick 31260) | `024ECBF625F3AFA1` |
| `DeathValley_GLA-America` | America 5:51.0 (Tick 10530) | America 5:51.5 (Tick 21090) | `D2E97F00A86D8109` |
| `ai8_FortressAvalanche` | Patt | Patt | `480A8CD2BFBC7EC6` |
| `ai8_IronDragon` | Patt | Patt | `BADAAA6219710424` |
| `ai8_TwilightFlame` | Patt | Patt | `3CD82FBEF25B2E25` |
| `ai8_TwilightFlame_short` | Patt | Patt | `B5BBA63A4540130D` |

Die 27 Missionsläufe enden bei beiden Raten nach 100 s Spielzeit ohne Ausgang (kein Endskript im Budget; `outcomeScript` leer bei beiden Raten). Ausgeführte Skripte und Aktionen sind auf 27 von 27 Läufen gleich, die Schusszahl auf 26 von 27 (`MD_GLA05`: 643 gegen 640).

| Mission | 30 Hz: Ausgang, Ende, ausgeführte Skripte/Aktionen, Schüsse | 60 Hz: Ausgang, Ende, ausgeführte Skripte/Aktionen, Schüsse | Endhash 60 Hz | gleich nach Speichern/Laden |
|---|---|---|---|---|
| `GC_AirGeneral_AmericaLaserGeneral` | none, Tick 3000 (1:40.0), 58/588, 0 | none, Tick 6000 (1:40.0), 58/588, 0 | `18E38A8D23C8F813` | ja |
| `GC_AirGeneral_ChinaInfantryGeneral` | none, Tick 3000 (1:40.0), 58/588, 0 | none, Tick 6000 (1:40.0), 58/588, 0 | `1392094860086813` | ja |
| `GC_AirGeneral_GLAStealthGeneral` | none, Tick 3000 (1:40.0), 58/588, 0 | none, Tick 6000 (1:40.0), 58/588, 0 | `C9B00717D81E7E8F` | ja |
| `GC_ChemGeneral_AmericaAirForceGeneral` | none, Tick 3000 (1:40.0), 83/783, 0 | none, Tick 6000 (1:40.0), 83/783, 0 | `4CAD90734E13F02A` | ja |
| `GC_ChemGeneral_ChinaTankGeneral` | none, Tick 3000 (1:40.0), 83/783, 0 | none, Tick 6000 (1:40.0), 83/783, 0 | `68388B1EA399E017` | ja |
| `GC_ChinaBoss_AmericaAirForceGeneral` | none, Tick 3000 (1:40.0), 12092/12720, 0 | none, Tick 6000 (1:40.0), 12092/12720, 0 | `555C096C0E6D724A` | ja |
| `GC_ChinaBoss_AmericaLaserGeneral` | none, Tick 3000 (1:40.0), 12092/12720, 0 | none, Tick 6000 (1:40.0), 12092/12720, 0 | `EC07A3B90506FE40` | ja |
| `GC_ChinaBoss_AmericaSuperWeaponGeneral` | none, Tick 3000 (1:40.0), 12092/12720, 0 | none, Tick 6000 (1:40.0), 12092/12720, 0 | `154593BB1A1B269F` | ja |
| `GC_ChinaBoss_ChinaInfantryGeneral` | none, Tick 3000 (1:40.0), 12092/12720, 0 | none, Tick 6000 (1:40.0), 12092/12720, 0 | `EE8AFCB3B10A37A2` | ja |
| `GC_ChinaBoss_ChinaNukeGeneral` | none, Tick 3000 (1:40.0), 12092/12720, 0 | none, Tick 6000 (1:40.0), 12092/12720, 0 | `C1CDBE60D4670542` | ja |
| `GC_ChinaBoss_ChinaTankGeneral` | none, Tick 3000 (1:40.0), 12092/12720, 0 | none, Tick 6000 (1:40.0), 12092/12720, 0 | `507C1049CCB05E1A` | ja |
| `GC_ChinaBoss_GLADemolitionGeneral` | none, Tick 3000 (1:40.0), 12092/12720, 0 | none, Tick 6000 (1:40.0), 12092/12720, 0 | `1B3F8D6817B186A3` | ja |
| `GC_ChinaBoss_GLAStealthGeneral` | none, Tick 3000 (1:40.0), 12092/12720, 0 | none, Tick 6000 (1:40.0), 12092/12720, 0 | `0004F5E35205A146` | ja |
| `GC_ChinaBoss_GLAToxinGeneral` | none, Tick 3000 (1:40.0), 12092/12720, 0 | none, Tick 6000 (1:40.0), 12092/12720, 0 | `9154939ADA5177C9` | ja |
| `GC_LaserGeneral_ChinaNukeGeneral` | none, Tick 3000 (1:40.0), 123/582, 0 | none, Tick 6000 (1:40.0), 123/582, 0 | `134C5C65640E2FAB` | ja |
| `GC_SuperWeaponsGeneral_GLADemolitionGeneral` | none, Tick 3000 (1:40.0), 97/593, 0 | none, Tick 6000 (1:40.0), 97/593, 0 | `620A01483ABDE0AF` | ja |
| `GC_TankGeneral_AmericaSuperWeaponGeneral` | none, Tick 3000 (1:40.0), 3035/550, 0 | none, Tick 6000 (1:40.0), 3035/550, 0 | `680039C27D4F14DC` | ja |
| `GC_TankGeneral_GLAToxinGeneral` | none, Tick 3000 (1:40.0), 3035/550, 0 | none, Tick 6000 (1:40.0), 3035/550, 0 | `04D2BC1EA433815A` | ja |
| `MD_CHI01` | none, Tick 3000 (1:40.0), 65/3357, 0 | none, Tick 6000 (1:40.0), 65/3357, 0 | `4CF02E68E0400F5A` | ja |
| `MD_CHI05` | none, Tick 3000 (1:40.0), 151/487, 2926 | none, Tick 6000 (1:40.0), 151/487, 2926 | `2A2C8136A280E89B` | ja |
| `MD_GLA01` | none, Tick 3000 (1:40.0), 133/275, 1231 | none, Tick 6000 (1:40.0), 133/275, 1231 | `CCE1AB28809422B7` | ja |
| `MD_GLA03` | none, Tick 3000 (1:40.0), 101/485, 0 | none, Tick 6000 (1:40.0), 101/485, 0 | `CFA0CD3B1304A2FD` | ja |
| `MD_GLA05` | none, Tick 3000 (1:40.0), 100/381, 643 | none, Tick 6000 (1:40.0), 100/381, 640 | `9138F6E3D49214A1` | ja |
| `MD_USA01` | none, Tick 3000 (1:40.0), 107/225, 127 | none, Tick 6000 (1:40.0), 107/225, 127 | `CADD74730A3FA0A7` | ja |
| `MD_USA02` | none, Tick 3000 (1:40.0), 79/196, 63 | none, Tick 6000 (1:40.0), 79/196, 63 | `F0B92F6C1127B52E` | ja |
| `MD_USA05` | none, Tick 3000 (1:40.0), 109/394, 0 | none, Tick 6000 (1:40.0), 109/394, 0 | `4DC86DD2F74A5706` | ja |
| `Training01` | none, Tick 3000 (1:40.0), 46/48, 181 | none, Tick 6000 (1:40.0), 46/48, 181 | `08ECC3A089265ADA` | ja |

**Vergleich der Ergebnisgrößen** (`reference-manifest results <content> --out <datei> --set full`, einmal ohne Option und einmal mit `--tick-rate 30`; Dateien `results-30.json` und `results-60.json` im Scratch-Verzeichnis, je 50 Läufe, keiner fehlgeschlagen; derselbe Vergleich über die beiden Manifeste gibt dieselbe Tabelle). Von 49 Läufen mit einem Ausgangsfeld haben **46 denselben Ausgang** und **40 denselben Ausgang zur selben Spielzeit innerhalb eines Originalbilds**:

- gleich bis auf ein Originalbild: `stress`, alle 27 Missionsläufe, alle 4 Acht-Spieler-Läufe und 8 der 16 KI-Läufe (dieselbe Spielzeit des Siegs bzw. beide Patt);
- derselbe Sieger zu anderer Spielzeit (6): `reference-match` (+14,5 s), `DeathValley_GLA-America` (+0,5 s), `AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral` (−38 s), `DeathValley_ChinaTankGeneral-AmericaAirForceGeneral` (−241 s), `BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral` (−250,5 s), `ArmoredFury_GLA-America` (−356 s);
- anderer Ausgang (3): `ArmoredFury_America-China` (America nach 20:35 gegen Patt), `ArmoredFury_China-GLA` (Patt gegen China nach 21:54,5), `BitterWinter_America-China` (China nach 12:13,5 gegen America nach 11:33,5 – ein Siegerwechsel).

Weitere Ergebnisgrößen aus `results`: im `skirmish-sweep` haben nach 100 s **alle 64 Karten** bei beiden Raten dieselbe Objektzahl und je Spieler denselben Besitz und Kontostand; in den 27 Missionsläufen sind weitergereichte Aktionen, Objektzahl und Besitz/Geld je Spieler auf 27 von 27 gleich, die Ereigniszählung je Art auf 17 von 27; von den 20 KI-Läufen haben 8 dieselbe Befehlszahl und denselben Endstand je Spieler (die 8, die auch zur selben Spielzeit enden). Der Stresstest ist bei 60 Hz nach Bauart ein anderer Lauf (914.394 gegen 914.403 Befehle, Höchststand 15.775 gegen 15.797 Objekte); das Referenzmatch hat 26.626 gegen 28.930 Befehle und 13.589 gegen 14.040 Schüsse des Szenarios.

Die neun abweichenden Läufe sind **nicht bis zur ersten abweichenden Größe verfolgt**. Sie sind die langen Gefechte (das kürzeste abweichende endet nach 5:51 min); nach `docs/RATE-SWITCH-OUTER-TESTS.md` §7.3 weicht ein Gefecht der Original-KI ab Bild 10.532 (351 s) in der Wahl zweier Zielplätze ab, und der Verteilungsvergleich über 320 Gefechte je Rate (§15.4) liegt innerhalb seiner vorab festgelegten Grenzen. Ob die Häufung kürzerer Siegzeiten von ChinaTankGeneral gegen AirForce bei 60 Hz (drei von vier Karten) Zufall ist, ist hier nicht entschieden; **Balance ist nicht bewertet**.

#### 15.6.4 Kampagne, Szenen und Client bei 60 Hz (Standardrate, keine `--tick-rate`)

- **`CampaignTool drive`**, 24 Karten, je ein Prozess, vier nebeneinander, Standardbudget 2.000 s (120.000 Ticks): **Sieg 24 von 24, Niederlage 24 von 24** auf dem eigenen Endskript (gezählt aus den 24 Einzelprotokollen).

| Karte | Sieg: Endskript @ Tick | Spielzeit | Niederlage: Endskript @ Tick | Spielzeit |
|---|---|---:|---|---:|
| GC_AirGeneral | `END_Final_GLA` @ 540 | 0:09 | `Lose_END_Final_GLA` @ 300 | 0:05 |
| GC_ChemGeneral | `END_Final_USA` @ 540 | 0:09 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_ChinaBoss | `End the mission` @ 720 | 0:12 | `Lose_END_Final_USA` @ 4862 | 1:21 |
| GC_LaserGeneral | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_NukeGeneral | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_Stealth | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_SuperWeaponsGeneral | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| GC_TankGeneral | `END_Final_USA` @ 480 | 0:08 | `Lose_END_Final_USA` @ 300 | 0:05 |
| MD_CHI01 | `END_Final_` @ 602 | 0:10 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_CHI02 | `END_Final_` @ 602 | 0:10 | `Lose_END_Final_` @ 602 | 0:10 |
| MD_CHI03 | `Cheer1Done` @ 3444 | 0:57 | `Lose_END_Final_` @ 8942 | 2:29 |
| MD_CHI04 | `END_Final_` @ 96516 | 26:48 | `Lose_END_Final_` @ 546 | 0:09 |
| MD_CHI05 | `CINE_ENDVictory` @ 5324 | 1:28 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_GLA01 | `Cheer1Done` @ 1052 | 0:17 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_GLA02 | `END_Final_` @ 480 | 0:08 | `Lose_END_Final_` @ 1144 | 0:19 |
| MD_GLA03 | `CINE_ENDVictory` @ 8066 | 2:14 | `Lose_END_Final_` @ 484 | 0:08 |
| MD_GLA04 | `CINE_END` @ 2044 | 0:34 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_GLA05 | `CINE_END04END` @ 2788 | 0:46 | `Lose_END_Final_` @ 2072 | 0:34 |
| MD_USA01 | `END_Final_USA` @ 1570 | 0:26 | `Lose_END_Final_USA` @ 422 | 0:07 |
| MD_USA02 | `END_Final_` @ 5436 | 1:30 | `Lose_END_Final_` @ 25202 | 7:00 |
| MD_USA03 | `CINE_AVVictory` @ 3018 | 0:50 | `Lose_END_Final_` @ 482 | 0:08 |
| MD_USA04 | `END_Final_USA` @ 606 | 0:10 | `Lose_END_Final_USA` @ 1234 | 0:20 |
| MD_USA05 | `END_Fade 04` @ 2886 | 0:48 | `Lose_END_Final_` @ 2560 | 0:42 |
| Training01 | `Player Wins` @ 840 | 0:14 | `Player Loses` @ 5310 | 1:28 |

- **`CampaignTool audit`** (Standardbudget 60 s = 3.600 Ticks), je Karte ein Prozess: 24 von 24 identisch im zweiten Lauf, 24 von 24 identisch nach Speichern/Laden, 0 Treffer auf fehlende Aktions- und Bedingungstypen; Ausschalt-Endproben **Sieg 13 von 24, Niederlage 15 von 24** (wie bei 30 Hz).
- **`CampaignTool reachability`**: Sieg sauber 24, Niederlage sauber 24.
- **`scripts/audit-scenarios.ps1`** (3 Szenen) und **`-Scenario exits`** (9 Szenen): alle zwölf „RESULT: all checks passed“, zweimal gelaufen, die zwölf Ausgabedateien beider Läufe bytegleich.
- **`scripts/check-spectator.ps1`**: PASS (Gefecht endet bei Tick 17.101 = 285 s mit Sieger Spieler 1). **`scripts/check-dozer-build.ps1`**: 5 von 5 PASS. **`scripts/check-build-hotkeys.ps1`**: 3 von 3 PASS.
- **Client-Rauchtest** `--smoke-test --match "Maps/Alpine Assault/Alpine Assault.map" --autoplay --frames 600`: Exitcode 0, „session: simulation rate 60 Hz“, 600 gezeichnete Originalbilder, der Mischer rendert 20,00 s. Der Welttick wird im Gefechtsmodus nicht ausgegeben; im Missionsmodus (`--mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map" --frames 600`) meldet der Client „600 frames, world tick 1200 at 60 Hz, session steps 1200“.

#### 15.6.5 Golden-Werte und Prüfsummen: was an der Rate hängt, wo es steht, womit es neu entsteht

Eingecheckte Dateien mit Golden-Werten, die an der Standardrate hängen, sind genau zwei – und beide schreibt der Projektinhaber:

| Datei | Hängt an der Rate | Neu erzeugen mit |
|---|---|---|
| `baselines/reference-manifest.json` | ja (jeder Lauf) | `dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest write .local/phase2-import-a baselines/reference-manifest.json --set full --jobs 5` (ohne `--tick-rate`: Standardrate) |
| `baselines/input-manifest.json` | ja (Welt-Fingerabdruck und Katalog-Hashes; Karten und Definitionen nicht) | `… -- input-manifest write .local/phase2-import-a baselines/input-manifest.json` |
| `baselines/compatibility.json` | nein (INI-Kompatibilität) | – |
| `tests/CnCNew.Game.Tests/baselines/screens.txt`, `hud-drawlist-1280x720.sha256` | nein, bei 30, 60 und 120 Hz gemessen | – |

Prüfsummen, die nur in Dokumenttabellen stehen (kein Test liest sie) und bei 60 Hz andere sind; hier die Werte der Standardrate, je zweimal erzeugt und bytegleich:

| Wert | Steht in | Befehl | Wert bei 60 Hz (SHA-256 der ganzen Ausgabe, erste 16 Stellen) |
|---|---|---|---|
| `scenario-audit capture-building` | `docs/TICKRATE-MIGRATION.md` §14.2 (Vergleichstabelle der Phase 21, 30 Hz: `BC7DD2A2F453D2EC…`) | `pwsh scripts/audit-scenarios.ps1` (schreibt `<OutDir>\<szene>.txt`) | `B2557B4A970F7512` |
| `scenario-audit tank-duel` | §14.2 (30 Hz damals: `8FD85E76B599D514…`) | wie oben | `25BC3C786F8C3837` |
| `scenario-audit jet-strafe` | §14.2 (30 Hz damals: `67DE0DFB83346317…`) | wie oben | `163DB77C14A0A257` |
| neun `exit-<fraktion>-<gebäude>` | – (bisher nicht tabelliert) | `pwsh scripts/audit-scenarios.ps1 -Scenario exits` | america: `C5CC32D01893815B` (barracks), `DA3F60F789A09001` (commandcenter), `B338020853A0437F` (warfactory); china: `A45494EBAEEC4085`, `5D9A45FF9EA02CF5`, `0A935EA49EA0379B`; gla: `4BB751EE1C437CBB`, `E92AFB4EACE5C9B5`, `17604EE390E972A4` |
| `sync-perf --digest` | `docs/PERF-GAME-LAYER.md` (Vorher/Nachher-Vergleich, kein fester Wert) | `dotnet run -c Release --project tools/CnCNew.PresentationTool -- sync-perf .local/phase2-import-a --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --hud --digest` (Standardbudget jetzt zehn Minuten Spielzeit: 36.000 Ticks) | `EE731E8D792C759A…`, kein Sieger im Budget |
| Manifest von `scripts/capture-world.ps1` (19 Szenen), Filmstreifen von `scripts/capture-scenarios.ps1` (77 Bilder) | temporäre Manifeste der Skripte (beschrieben in `docs/CLIENT-OBJECTS.md`, `docs/DEMO-SCENARIOS.md`), nicht eingecheckt | die beiden Skripte ohne `-TickRate` | **nicht neu erzeugt** (Straßenbefund `docs/RATE-SWITCH-OUTER-TESTS.md` §7.7 vorher zu entscheiden) |
| `fx-audit` | `docs/PRESENTATION-FX.md` (Prüfsumme des Ausführungsdurchlaufs) | nennt `SimulationRate.Original` selbst | bleibt bei 30 Hz |

Die Dateinamen der Audit-Ausgaben enthalten den Pfad des Inhaltsverzeichnisses nicht, die Ausgaben selbst nennen in der Kopfzeile die Rate; die Prüfsummen gelten für die Aufrufe oben mit `-ContentDir` auf den geprüften Bestand. Die Dokumenttabellen selbst sind in diesem Paket nicht ersetzt.

#### 15.6.6 Nicht gemacht, nicht belegt

- Die neun Manifestläufe, deren Ausgang oder Siegzeit zwischen 30 und 60 Hz abweicht, sind nicht bis zur ersten abweichenden Befehlszeile verfolgt (`reference-manifest results --commands` ist nicht gelaufen).
- `capture-world.ps1`, `capture-scenarios.ps1`, `capture-text.ps1` und das `UiRenderTool` sind mit der Umstellung nicht gelaufen (das `UiRenderTool` ist nur gebaut); die Demo-Skripte sind nicht gestartet.
- `perf-profile.ps1` ist nach dem Umbau nur auf Syntax geprüft (Parser ohne Fehler), kein Messlauf; es gibt keine Leistungsmessung in diesem Paket.
- `Game.Tests` ohne `--content-dir`, der Debug-Build von `Game.Tests` und `--determinism-full` sind nicht gelaufen. Die Determinismus-Matrix (andere Laufzeitschalter, Linux) ist bei 60 Hz nicht wiederholt.
- Der Nur-Befehle-Agent (`drive --commands-only`) ist bei 60 Hz nicht gelaufen.
- Ein 30-Hz-Spielstand in einem Client der Standardrate wird abgelehnt (§15.5.3); eine Shell-Funktion, die einen Stand mit dessen Rate lädt, gibt es nicht.
- Die Obergrenze eines Standardgefechts von einer Stunde Spielzeit (`SkirmishMatchOptions.Standard.MaxTicks`) gilt auch für ein Gefecht eines Menschen im Client; ob das gewollt ist, ist keine Frage der Rate und hier nicht entschieden.
- Kein Bild, kein Ton und kein Spielablauf bei 60 Hz ist von einem Menschen geprüft.

### 15.7 Runde 3 und 4, Umstellung, Stand am Ende der Phase (9. Oktober 2026, Stand `f3acc2e`)

**Vorweg, damit es niemand überliest.** Kein Mensch hat das Spiel bei 60 Hz gesehen oder gehört. Der Projektinhaber hat am 8. Oktober 2026 **ein** KI-Gefecht im Zuschauermodus bei 30 Hz angesehen – vor den KI- und Ausfahrtskorrekturen, die daraus folgten. Nichts am Zuschauermodus, an den Fabrikausfahrten, am Scrolltempo, an der Mausbelegung oder an den Bautasten ist von einem Menschen abgenommen. „Die Suite besteht“ heißt: die gemessenen Größen der Fälle der Suite liegen bei 60 und 120 Hz innerhalb der vorab festgelegten Toleranzen – nicht, dass jedes Gefecht identisch ist (der gemessene Rest steht unten). Die Gleichheit ganzer KI-Gefechte ist mit einem Selbsterwerb je Originalbild der Einheit erkauft, den der Auftrag dem Wortlaut nach verbot (Entscheidung 1).

#### 15.7.1 Die Runden

Größen außerhalb der Toleranz bei 60 Hz / bei 120 Hz. Die Suite hatte bis zur Fabrikausfahrt 4.307 Größen, danach 4.388 (neun Ausfahrtsfälle mit 81 Größen in `economy`). Keine Toleranz und keine Testgrenze ist in der Phase geändert worden. Verteilungsvergleich: `ai-survey` mit 320 Gefechten je Rate (16 Paarungen × 20 Seeds, 25 Spielminuten), `rate-distribution` 30 gegen 60 Hz; Protokolle im Scratchpad der Sitzung (`survey\r0` bis `r3`).

| Stand | Commit | Suite | Verteilungsvergleich 30 gegen 60 Hz |
|---|---|---|---|
| Ausgang | `91cb830` | 479 / 625 | 38 von 39 bestanden; entschieden 253 gegen 224 von 320, z = −2,63 (Grenze 1,96) |
| Runde 1: sieben Pakete | `62fbb6a` | 63 / 64 | 39 von 39; 253 gegen 244, z = −0,85; D = −0,10 min (SE 0,37) |
| Runde 2: Konsolidierung | `3b44506` | 35 / 33 | 39 von 39; 253 gegen 245, z = −0,76; D = −0,11 min (SE 0,39) |
| Entscheidung des Projektinhabers: Abbruchkriterium aufgehoben | `37b36b8` | – | – |
| 30-Hz-Fehler „verlorene Weckbitte“ (ändert 3 Missionsläufe; Manifest `6a99102`) | `5abbeea` | 34 / 32 (`timed` 0 / 0) | nicht erhoben |
| Fabrikausfahrt mit zwei Folge-Commits (ändert 23 Läufe bei 30 Hz; Manifest `8a6e5b7`) | `c821fbd` | außerhalb von `ai` dieselben 14 Zeilen wie vorher; `ai` 27 / 26 | nicht erhoben |
| Runde 3, Flug (mit dem Fallschirmabwurf, einem 30-Hz-Fehler, der keinen Manifestlauf ändert) | `929f629` | `flight` 0 / 0, `turrets` 0 / 0 | nicht erhoben |
| Runde 3, Beobachter | `502e75b`, `4d9d9e5` | **0 / 0 in allen zehn Gruppen** | Information: 80 Gefechte je Rate, 54 von 80 enden beim selben Seed mit demselben Ausgang innerhalb einer Sekunde |
| KI- und Bewegungskorrekturen nach der ersten Sichtung (ändern 40 Läufe bei 30 Hz; Manifest `cca6f92`) | `0fe9923` | 0 / 0 | neue 30-Hz-Referenz nötig: 305 gegen 301 von 320 entschieden, 39 von 39, z = −0,71; D = −0,009 min (SE 0,286); beim selben Seed derselbe Ausgang innerhalb einer Sekunde: 188 von 320 |
| Testprogramme bei 30/60/120 Hz | `203806e` | 0 / 0 | – |
| Runde 4: erstes Bild, ganze Gefechte auf ausgelieferten Karten | `1b82e45` | 0 / 0 | beim selben Seed derselbe Ausgang und Sieger **317 von 320**, dazu Ende innerhalb einer Sekunde **287 von 320** (am Ausgang der Runde nach derselben Auswertung 295 und 192); das Urteil von `rate-distribution` ist für diesen Stand in keinem Protokoll notiert |
| Umstellung des Standards, eine Zeile | `e8632bb` | – | – |
| Manifeste bei 60 Hz | `ea52f87` | – | – |

Bei 30 Hz hat keine der vier Runden etwas geändert: das volle Referenzmanifest war nach jeder Runde identisch (50 von 50), und die 30-Hz-Erhebung über 320 Gefechte war nach Runde 1, 2 und 4 Byte für Byte dieselbe Datei wie zuvor. Geändert haben 30 Hz genau drei Dinge, je mit eigenem Manifest-Commit (§15.7.3).

Die 188 der Zeile „KI-Korrekturen“ stammen aus den Arbeitsnotizen der Sitzung; das Paket der Runde 4 zählt am selben Stand mit einer eigenen Auswertung 192 (`docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` §1.3). Beide Zahlen stehen hier, weil keine von beiden nachgerechnet ist.

#### 15.7.2 Die Bildkonvention in fünf Sätzen

1. Der 30-Hz-Schritt ist die Definition; höhere Raten unterteilen ihn, sie integrieren nicht mit kleinerer Schrittweite neu.
2. Jedes Objekt entscheidet im **ersten** Tick seines eigenen Originalbilds (Größen zweiter Ordnung, Zustandsautomaten, Ereignisse) und erreicht seine 30-Hz-Lage mit dem **letzten** Tick; dazwischen liegt es auf der Geraden dorthin.
3. Wer ein anderes Objekt ansieht, um etwas zu entscheiden, liest dessen Lage, Kurs und Höhe am Ende des Bildes, das es begonnen hat (`Entity.FrameEnd`, `Entity.Ahead…`); die Sicht wird nach dem letzten Tick ihres Bildes aufgefrischt.
4. „Einen Tick später“ ist ein Bild später: neue Objekte, Entfernen nach einem verzögerten Treffer, das erste Update eines Moduls, das ein Ereignis entscheidet; ein Marsch endet im ersten Tick seines letzten Bildes und rollt den Rest aus.
5. Der Zustand dafür wird nur oberhalb von 30 Hz geschrieben – bei 30 Hz ist ein Bild ein Tick, kein Feld wird gesetzt, Snapshots und Hashes sind bytegleich.

Ausgeschrieben: `docs/SIMULATION-CORE.md` §43 (zusammengeführt), `docs/RATE-INVARIANCE-CONSOLIDATION.md` §1.3 (Regeln 1–8), `docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md` §2 (Zielerwerb), `docs/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` §3 (erstes Bild); jede Verwendung des Auswegs „Originalraster“: `docs/SIMULATION-CORE.md` §41.

#### 15.7.3 Drei Verhaltensänderungen bei 30 Hz

| Ursache | Merge | Manifest | Was sich bei 30 Hz ändert | Dokument |
|---|---|---|---|---|
| Verlorene Weckbitte: ein langsamer Tod, dessen Weckbitte verworfen wird, schlief für immer | `5abbeea` (Korrektur `71e41d7`) | `6a99102` | 3 von 50 Läufen (`MD_GLA01`, `MD_USA02`, `MD_USA05`); 28 Zwischensequenz-Objekte werden jetzt entfernt; Referenzmatch unverändert | `docs/DEFECT-LOST-WAKE.md` |
| Fabrikausfahrt: produzierte Einheiten fahren hinaus; jeder Befehl ersetzt den Ausfahrtsweg; die KI gibt einer ausfahrenden Raupe Arbeit | `c821fbd` (`df7443d`, `78dc175`, `7b85160`) | `8a6e5b7` (auch Eingangsmanifest: `ProductionExitData.NaturalRallyPoint`) | 23 von 50 Läufen; Referenzmatch Tick 14.130, `5BF9BB7CED6D7A35` / `56149C0105E992D7` (vorher 17.805); weiter 12 von 16 KI-Läufen entschieden | `docs/SIMULATION-CORE.md` §42 |
| Erste Sichtung eines KI-Gefechts: die KI spart für Fahrzeuge, Einheiten bekommen einen eigenen Zielplatz, Einsteiger erreichen ihr Fahrzeug, Heereszusammensetzung nach Tauschwert | `0fe9923` | `cca6f92` | 40 von 50 Läufen; **14 statt 12** von 16 KI-Läufen entschieden, der Ausgang ist in 8 anders; Referenzmatch und Stresstest unverändert | `docs/AI-OBSERVED-2026-10-08.md` §11 |

Dazu ohne Manifeständerung: der Fallschirmabwurf der Spezialkraft setzt seine Soldaten ab (`09b4d21`, in `929f629`; kein Manifestlauf benutzt eine Lieferung mit Behälter).

#### 15.7.4 Schlussprüfung

Vor der Umstellung, am Stand `1b82e45` (Standard noch 30 Hz), gezählt aus den Einzelprotokollen der Sitzung (`verify\r4`):

| Prüfung | Ergebnis |
|---|---|
| sauberer Build (`CnCNew.slnx` mit gelöschtem `bin`/`obj` des Clients, `UiRenderTool`) | 0 Warnungen |
| `Simulation.Tests` (Release) ohne Realdaten bei 30 / 60 / 120 Hz | 513 / 513 / 513 bestanden |
| `Simulation.Tests --content-dir` bei 30 / 60 / 120 Hz | 543 / 543 / 543 bestanden, darin die Suite mit 0 / 0 in allen zehn Gruppen (4.388 Größen) |
| `Simulation.Tests -c Release -- --long` | 514 bestanden |
| `Game.Tests` (Release, Realdaten), `Presentation.Tests` (ohne und mit Realdaten) bei 30 / 60 / 120 Hz | bestanden |
| Archives, Content, Data (auch mit Realdaten), IO, Images, Formats, Import, W3D, Ui, Audio | bestanden |
| `IniTool compat-check` | 751 Einträge |
| `input-manifest check`, `reference-manifest check --set full` gegen die 30-Hz-Dateien | identisch; 50 von 50 Läufen identisch |
| `scripts/audit-scenarios.ps1` bei 30 Hz und mit `-TickRate 60` | je 3 von 3 Szenen bestanden |
| `check-spectator.ps1`, `check-dozer-build.ps1`, `check-build-hotkeys.ps1` | PASS; 5 von 5; 3 von 3 |

Nach der Umstellung (`e8632bb`) und mit den 60-Hz-Manifesten (`ea52f87`; Protokolle `verify\switch`):

| Prüfung | Ergebnis |
|---|---|
| `input-manifest check` gegen die letzte 30-Hz-Datei und gegen die neue | „Input manifest (30 Hz, schema 1): identical.“ und „Input manifest (60 Hz, schema 2): identical.“; Welt-Fingerabdruck bei 60 Hz `C81DC18A282037CD` |
| `reference-manifest check --set full` gegen die letzte 30-Hz-Datei (dieselbe Binärdatei, Standard jetzt 60 Hz) | 50 von 50 Läufen identisch |
| `reference-manifest check --set full` gegen die neue 60-Hz-Datei, **zweimal** (Windows, Release) | beide Male 50 von 50 Läufen identisch |
| Linux (WSL2) mit dem 60-Hz-Manifest, Determinismus-Matrix bei 60 Hz, Kampagne (`drive`, `audit`, `reachability`) mit den Binärdateien nach Runde 4 bei der Standardrate, Erhebung am Endstand, Nur-Befehle-Agent bei 60 Hz | **nicht gemessen** (lag beim Schreiben dieses Abschnitts nicht vor) |

**Neue Referenzwerte bei 60 Hz** (`baselines/reference-manifest.json`, Schema 2): Referenzmatch Sieger FactionGLA bei **Tick 28.260** (Originalbild 14.130, 7:51 min), Endhash `CA05181C36C62C7A`, Kette `21ACEDA63325A658`, 26.626 Befehle; Stresstest 216.000 Schritte (eine Stunde Spielzeit bei 60 Hz) Endhash `9EE9F730D124C3BD`, Kette `F338463BE66D99C0`; 16 KI-Läufe **14 entschieden**; die vier 8-Spieler-Läufe Patt am Budget; 27 Missionsläufe ohne Ausgang in 100 s. Versionen unverändert: `WorldSnapshot` 10, Replay 12, `CommandTypes.Version` 15, `MissionSnapshot` 3, `SaveGame` 3. **Überholt:** Tick 17.805 / `D0CA5F4EE456D6CE` / `C5EE1090E1C24D7B` (30 Hz, Phase 22) und die 30-Hz-Zwischenstände Tick 14.130 / `5BF9BB7CED6D7A35` / `56149C0105E992D7`; der Stresstest-Endhash `56882F763EABEF67` gilt nur noch für 30 Hz.

**Letztes 30-Hz-Manifest (`cca6f92`) gegen das 60-Hz-Manifest (`ea52f87`)**, für dieses Dokument aus den beiden Dateien ausgewertet (Ausgang, Sieger, Zeit des Siegs in Spielzeit): **50 von 50 Läufen mit demselben Ausgang, 48 davon zur selben Spielzeit.** Die beiden anderen: `AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral` 610,0 gegen 617,0 s und `BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral` 1.005,0 gegen 1.089,5 s (derselbe Sieger). Das Referenzmatch hat bei beiden Raten 26.626 Befehle und denselben Höchststand von 258 lebenden Objekten. In den 27 Missionsläufen sind ausgeführte Skripte und Aktionen auf 27 von 27 gleich, die Schusszahl auf 26 von 27 (`MD_GLA05`: 643 gegen 642). Der Stresstest ist bei 60 Hz nach Bauart ein anderer Lauf (914.394 gegen 914.403 Befehle, Höchststand 15.775 gegen 15.797 Objekte; §15.5.2).

#### 15.7.5 Leistungstor (ruhig gemessen, 9. Oktober 2026)

8 Spieler auf Twilight Flame, 30 Spielminuten (108.000 Ticks) bei 60 Hz, zwei Läufe: Mittel 683 / 694 µs je Tick (Soll ≤ 1.000: erreicht), 99 % 1.615 / 1.793 µs (≤ 3.000: erreicht), **Maximum 141 / 143 ms (≤ 8 ms: verfehlt)**, Aufholtempo 26,7- / 26,5-fach (≥ 8-fach: erreicht), keine Gen-2-Läufe, Allokation rund 8 KB je Tick (nahe 0: nein). Tabelle, Vergleich alt gegen neu bei 30 Hz und die Missionen bei 60 Hz: `docs/PERFORMANCE.md` §6c.

#### 15.7.6 Was nicht gleich ist, und was nicht belegt ist

Zusammengeführt aus den Listen „nicht gemacht oder nicht geprüft“ der Pakete (`docs/RATE-INVARIANCE-*.md`, `docs/RATE-SWITCH-OUTER-TESTS.md` §7.7/§7.9, §15.6.6). Jede Zeile ist gemessen oder ausdrücklich als nicht gemessen benannt.

**Gemessen und nicht gleich:**

- **2 von 16 Paarungen des Manifests** enden bei 60 Hz in einem anderen Bild als bei 30 Hz (Alpine Assault ChinaTank/AirForce: Bild 18.300 gegen 18.510, 7 s; Bitter Winter ChinaTank/AirForce: 30.150 gegen 32.685, 84,5 s; derselbe Sieger). In 8 von 16 ist jedes verglichene Feld jedes Objekts über das ganze Gefecht gleich, in drei weiteren bis nach dem Sieg. Die Trennstellen der übrigen (Turm einer `AmericaFireBase` im Bild eines ergebnislosen Blicks, Kurs eines Rangers, Tempo eines Gattling-Panzers) sind **nicht bis zum Symbol verfolgt**.
- **Erhebung über 320 Gefechte:** 3 mit anderem Ausgang, 33 enden nicht innerhalb einer Sekunde des 30-Hz-Endes.
- **120 Hz** ist für die 16 Paarungen und für die Erhebung **nicht gemessen**; gemessen sind bei 120 Hz die Suite, die Testprogramme, das Referenzmatch (Tick 56.520, dasselbe Bild 14.130) und der Zielfahrer.
- **Kampagne:** der Zielfahrer erreichte am Stand `cca6f92` bei 30, 60 und 120 Hz auf allen 24 Karten Sieg und Niederlage über dasselbe Endskript zur selben Spielzeit, mit einer Ausnahme: Niederlage auf `MD_USA02` 421,03 gegen 420,03 s (−1,00 s bei 60 und 120 Hz), **nicht untersucht**. Nach Runde 4 und nach der Umstellung ist der Zielfahrer nicht wiederholt.
- **Ein Missionslauf des Manifests** hat eine andere Schusszahl (`MD_GLA05`, 643 gegen 642).
- **Vom Feldvergleich ausgenommen**, weil sie innerhalb eines Bildes vorauslaufen, ohne eine Entscheidung zu berühren: `nextWake`, `previousHealth`, Gesundheit und Baufortschritt einer Baustelle, `weaponReadyTick`, `dockMachine.nextActionTick`, `DynamicShroudClearingRangeUpdate.current`.
- **Innerhalb der Toleranz, nicht exakt:** Zeitpunkte, die mit dem letzten Tick eines Bildes erreicht werden (z. B. Ranger aus der Kaserne „beide stehen“ 13,8333 / 13,8500 / 13,8583 s), Battle Bus „in der Luft“ 2,3667 / 2,3667 / 2,3750 s, Fähre und Welle (Lage innerhalb 0,001 Einheiten).
- **Präsentation** (Stand vor Runde 4, danach nicht wiederholt): die Zeichenreihenfolge der Straßen hängt an der Rate (Entscheidung 7); gestellte Szenen zeigen ein halbes Bild Versatz (`[MOVING]` des Rangers in Bild 12 / 11 / 11); `tank-duel` weicht in allen 29 Filmstreifenbildern ab, `match-start` in 1 Pixel, `mission-start` in 161 – ohne Einordnung.

**Nicht umgestellt** (jede Stelle kann an einer Schwelle ein Bild kosten; an keiner ist in den Läufen der Phase eine erste Abweichung gemessen):

- Beweger, die weiter `Wert je Tick × r` rechnen: Spectre, Fallschirm, Leuchtkörper, Partikelstrahl, abgeschossener Jet, die Schwerkraft der Physikmodule, das Gruppentempo, `SpecialAbilityUpdate.TurnToward`.
- Heilscheiben je Tick: `SlavedUpdate` (Reparaturdrohne) und die Regeneration der Insassen in `ContainModules.cs`.
- „Erstes Bild“ (Regel 7) für `DeliverPayloadAIUpdate`, Fallschirm, Fähre, dynamische Aufdeckung, Feuersturm, `FireOCLAfterWeaponCooldownUpdate`, Feindnähe-Scan und Checkpoint.
- Leser an der augenblicklichen Lage: die Skript-VM (läuft im ersten Tick und liest Objekte in Phase einen Anteil weit), Spezialfähigkeiten, `SlavedUpdate`, `IsSignificantlyAboveTerrain` an Minen, Kisten, Brücken und `IsAirborne`. Nicht veröffentlichende Beweger: Zug, Lawine, Fähre, Welle, freier Fall.
- **Nicht untersucht:** Brücken, Wasser, Klippen (die Suite kennt nur die flache Karte; Hänge sind auf den vier Karten des Manifests gemessen); die Original-KI nach Runde 4 (verglichen ist eine Paarung mit einem Seed am Stand davor); ein Befehl eines Menschen mitten im Bild (hat kein 30-Hz-Gegenstück).

**Leistung:** das Tickmaximum (141–143 ms; ein Tick mit vielen gleichzeitigen langen Wegsuchen) und die Allokation (rund 8 KB je Tick) verfehlen ihre Sollwerte (Entscheidung 3).

**Nicht gelaufen:** was in §15.7.4 als nicht gemessen steht; die Demo-Skripte, `capture-world.ps1` und `capture-scenarios.ps1` bei 60 Hz nach Runde 4; `Game.Tests` ohne Realdaten, im Debug-Build und mit `--determinism-full` bei 60 und 120 Hz.

#### 15.7.7 Entscheidungen, die beim Projektinhaber liegen

Dieselbe Liste steht in `docs/MULTIPLAYER-PLAN.md` §7, in `CLAUDE.md` (Absatz „Phase 23“) und im Nachtrag von `docs/HANDOFF-PHASE24.md`.

1. **Selbsterwerb einmal je Originalbild der Einheit.** Eine Einheit entscheidet ihren eigenen Angriff im ersten Tick ihres eigenen Bildes. Der Auftrag der Phase 23 verbot den Ausweg „Originalraster“ für den Zielerwerb; es ist vorläufig so gemacht, weil die Gleichheit ganzer Gefechte anders nicht erreichbar war. Kosten: eine Einheit, die mitten im Bild durch einen Befehl frei wird, beginnt ihre eigene Zielsuche bis zu 1 Tick (60 Hz) bzw. 3 Ticks (120 Hz) später. Alternative: je Tick entscheiden – dann unterscheiden sich Gefechte zwischen den Raten, und es gilt nur der Verteilungsvergleich. Dieselbe Frage für `PointDefenseLaserUpdate`, `CleanupHazardUpdate` und `DeployStyleAIUpdate.AllowsFire`.
2. **Fabrikausfahrt „immer durch das Tor, auch bei sofortigem Befehl“.** Gebaut und gemessen, nicht gemergt: verschiebt die Balance (America 46 statt 61 Siege von 80, China 32 statt 23; damals 10 statt 12 der 16 Läufe entschieden); der Flicken liegt im Scratchpad der Sitzung.
3. **Das Tickmaximum senken** (141–143 ms bei 60 Hz im Leistungstor): braucht eine Verhaltensänderung (gemeinsamer Gruppenweg, Zellbudget je Tick oder eine andere Suche) – vom Projektinhaber vorbehalten.
4. **Die zwei schnelleren Hash-Konstruktionen** (Sicht zeilenweise; Objekt-Hash mit Modul-Hashes) – vorbehalten, unverändert.
5. **Befehlsprüfung gegen die Kartengrenzen** (Ziele jenseits von rund 524.000 Einheiten lassen die Bewegung überlaufen) – für Phase 27/28 vorbehalten, unverändert.
6. **Solo-Boni der Kampagne** – vorbehalten, unverändert.
7. **Die Zeichenreihenfolge der Straßen hängt an der Rate** (`TerrainLayer.CreateRoads` gibt jedem Straßen-Mesh dieselbe Hülle; keine Korrektur gibt jedes 30-Hz-Bild wieder).
8. **Ein Standardgefecht endet nach einer Stunde Spielzeit**, auch für einen Menschen – ist das gewollt?
9. **Die Original-Skirmish-KI (drei Schwierigkeitsgrade) als Standard-KI** und ihre Auswahl im Client und im Zuschauermodus: sie kostet je Tick rund das 27-fache der eingebauten KI (1,1 ms für 8 Spieler), und ihr fehlen Teile des C++-Kerns; Standard ist die eingebaute KI.
10. **Die Balance nach den KI-Korrekturen ist nicht bewertet** (ChinaTankGeneral gewinnt in der 30-Hz-Erhebung der Runde 3 70 von 80 Gefechten gegen AirForce).
11. **Der zweite physische Rechner** (vor Phase 26) – unverändert offen.
