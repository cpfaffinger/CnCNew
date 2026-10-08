# Phase 23, Paket „Physik“: multiplikative Faktoren, gemischte Rekursionen, kleine Physik je Tick und Heilscheiben

Stand 8. Oktober 2026, Zweig `worktree-agent-a1997b956694a9a6c`, Ausgang `7b54374`. Dieses Dokument gehört zu einem von sechs parallelen
Korrekturpaketen der Phase 23 (`docs/HANDOFF-PHASE23.md`, Schritt 3). Es beschreibt nur, was dieses Paket geändert und gemessen hat; die
gemeinsamen Dokumente (`docs/TICKRATE-MIGRATION.md`, `docs/SIMULATION-CORE.md`) sind unberührt und werden beim Zusammenführen fortgeschrieben.

**Nichts davon hat ein Mensch gesehen.** Alle Zahlen stammen aus Läufen dieses Tages (Release, Realdaten `.local/phase2-import-a`).

## 1. Prinzip

Der 30-Hz-Schritt ist die Definition. Eine Rekursion, die das Original einmal je Bild ausführt (Geschwindigkeit unter einem Faktor, Winkelgeschwindigkeit
unter einer Beschleunigung, Fall unter Schwerkraft, Dämpfung), läuft **einmal je Bild des Originals in den Einheiten des Originals**; was sie bewegt
(Lage, Winkel, Höhe), rückt **jeden Tick** um seinen Anteil des Bildschritts vor. An jeder Bildgrenze ist der Zustand der 30-Hz-Zustand, dazwischen die
Gerade zwischen den beiden.

- **Das Bild ist das des Objekts**, nicht das Raster der Welt: es beginnt mit dem Update, in dem die Bewegung begann (`FramePhase`, Zustand des Moduls,
  nur geschrieben, solange es nicht null ist – bei 30 Hz nie; Snapshots und Hashes bei 30 Hz bleiben bytegleich).
- **Anteile summieren exakt:** `FramePhysics.FrameShare(schritt, k)` = `floor(schritt · k / n) − floor(schritt · (k − 1) / n)` auf dem Rohwert; die
  Anteile eines Bilds ergeben genau den Bildschritt. Wo das Ziel eines Bilds vorab feststeht (Lawine, Gerüst), rückt das Objekt je Tick um
  „Rest geteilt durch verbleibende Ticks“ vor und steht mit dem letzten Tick genau am Ziel.
- **Katalogwerte je Tick werden je Bild zurückgelesen** (`PerTickToPerOriginalFrame`, `PerTickSquaredToPerOriginalFrameSquared`): kein neues Feld an
  einem Katalogtyp, das Eingangsmanifest ist identisch. Bei 60/120 Hz weichen die letzten Bits solcher Werte von 30 Hz ab (Schwerkraft je Tick² mal
  4 bzw. 16); die Tests nennen dort eine Schranke von 0,001 Einheiten, sonst verlangen sie Bitgleichheit.
- **Bei 30 Hz** ist ein Bild ein Tick, jeder Anteil der ganze Schritt, die Phase immer null: jeder Ausdruck ist der bisherige.
- Neue Hilfsdatei nur dieses Pakets: `src/CnCNew.Simulation/Time/FrameGrid.Physics.cs` (`FramePhysics`, `FramePhase`).

**Gleicher Tick wie der Auslöser.** Fällt der erste Schritt einer Bewegung bei 30 Hz in denselben Tick wie ihr Auslöser, kann das erste Bild bei
60/120 Hz erst `n − 1` Ticks später enden (ein Bild kann nicht vor seinem Auslöser beginnen). Wo der Beginn vorher bekannt ist, beginnt die Bewegung
entsprechend früher (Flutwelle); wo der erste Schritt nichts bewegt, endet das Bild im auslösenden Update (Einsturz); sonst bleibt ein Versatz von
weniger als einem Bild (Fähre, wenn das Dock im Tick der Anfrage zieht; Battle Bus, wenn der Tod auf einen Hüllen-Prüftick fällt).

## 2. Ursachen

Werte: 30 / 60 / 120 Hz. „Suite“ ist die Raten-Äquivalenz-Suite (`SimTool rate-compare`), „Test“ der neue Drei-Raten-Test in
`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Physics.cs`.

| § 5 (`TICKRATE-MIGRATION.md`) | Befund vorher | Korrektur (Datei, Symbol, Prinzip) | nachher | Commit |
|---|---|---|---|---|
| 5.1 `ToppleUpdateData.InitialAccelPercent`; 5.2 `ToppleUpdate.Update` | Suite: Fall „gestoßen bis liegend“ Baum und Statue 1,2000 / 0,8000 / 0,5667 s, Mauer 0,2667 / 0,2167 / 0,1667 s, Zaun 0,6000 / 0,4167 / 0,3000 s (4 von 4 gemessenen) | `ToppleAndFallModules.cs`, `ToppleUpdate.Update`, `ApplyTopplingSpeed`: Winkelgeschwindigkeit und -beschleunigung je Bild(²), Schritt eines Bilds mit dem ersten Tick festgelegt, Winkel je Tick anteilig, Abprall/Beschleunigung mit dem letzten Tick; Abprallgrenze je Bild. Eingang in Strecke je **Originalbild** | 1,2000 / 1,2000 / 1,2000; 0,2667 ×3; 0,6000 ×3; Winkel 1,52171 bei jeder Rate. Test: Winkel an jeder Bildgrenze bitgleich | `021b318` |
| 5.1 `StructureToppleData.StructuralDecay`; 5.2 `StepToppling`, `ApplyCrushingDamage`, `_doneAtTick` | Suite (ein Retail-Objekt): liegt nach 4,3333 / 4,3000 / 4,3000 s (bestanden nur innerhalb der Toleranz) | `StructureToppleUpdate.StepToppling`: Beschleunigung und Zerfall mit dem ersten Tick des Bilds, Winkel je Tick anteilig (exakte Anteile des Feinwerts), Quetsch-Schuss und Ende mit dem letzten Tick, „Done“ ein Bild später (`>` statt `>=` gegen `Tick + n − 1`), ohne Verzögerung erstes Update ein Bild nach dem Tod; `Angle` am Viertelkreis gedeckelt | 4,3333 ×3. Test (zwei Gebäude): Winkel bitgleich, dieselben Schadensereignisse in denselben Bildern | `f8d0077` |
| 5.1 `RailroadData.Acceleration`/`Braking`/`Friction`, `RailroadBehavior.PushStart`, Anfahrwert, Stillstandsschwelle, Schaden `Speed · 10`, `Collide`, „Wagen erst im nächsten Tick“ | Suite: Höchsttempo nach 2,4333 / 1,1000 / 1,0000 s; Bremsen ab 15,5000 / 14,4167 / 14,0500 s; steht nach 19,8000 / 16,1833 / 14,7417 s bei 2.703,3 / 2.617,9 / 2.578,3 | `RailroadModules.cs`, `RailroadBehavior`: jede Geschwindigkeit des Zustands ist Strecke je Bild; Schaffner und Reibung mit dem ersten Tick des Bilds der Lok bzw. des führenden Wagens, Streckenlage je Tick anteilig, `onCollide` und das Verlassen der Strecke mit dem letzten Tick; ein gezogener Wagen erfährt vom Zieher, ob das Bild offen ist; Wartezeit in Bildern, erstes Update ein Bild nach dem Erscheinen, Abkoppeln nach derselben Spielzeit | 2,4333 / 2,4167 / 2,4083; 15,5000 / 15,4833 / 15,4750; 19,8000 / 19,7833 / 19,7750; 2.703,328 ×3 (4 Fehlschläge je Rate → 0). Test: Lok, Wagen, gerammtes Fahrzeug, Tode bitgleich | `9871bc2` |
| 5.1 `TensileFormationUpdate.Friction`, `NewShare`/`LinkShare`, `BaseSlope`; 5.2 Physikblock | Suite misst nichts (flache Karte). Test am Hang mit Ebene: Rutschweg 65,528 / 69,705 (60 Hz); letztes Bild mit Absinken 186 / 106; reiner Hang 90,31 / 184,73 | `FieldEffectModules.cs`, `TensileFormationUpdate.Update`, `PlaceOf`: Rekursion mit dem ersten Tick jedes Bilds der Rutschphase (Bildnummer aus `_life`), Ziel des Bilds im Zustand, je Tick gleicher Anteil des Rests; für Nachbarn steht ein Stein, der sein Bild begonnen hat, am Bildende | 65,528 ×3; 186 ×3; 90,31 ×3; alle Lagen bitgleich an 339 Bildgrenzen | `302e88a` |
| 5.3 Art `Register` (erstes Update einen Tick nach dem Erscheinen) – hier: eigene `OnSpawn` von Lawine und Battle Bus | Suite: „Lawine bemerkt den Schaden“ 0,9667 / 0,9500 / 0,9417 s | `TensileFormationUpdate.OnSpawn`, `BattleBusSlowDeathBehavior.OnSpawn`: erstes Aufwachen ein Bild nach dem Erscheinen | 0,9667 ×3 | `29df2ba` |
| – (Messung der Suite) | Fall „battle bus second life“: `KillCommand` tötet unaufhaltsam, kein zweites Leben, Wurf ungemessen | `RateCases.Timed.cs`, `MeasureBattleBus`: tödlicher Explosionsschaden über `DamageCommand` (der erste Tod, für den das Modul geschrieben ist); keine Toleranz, keine Größe geändert | misst jetzt: Scheitel 45,200 / 44,572 / 44,259 (Fehlschlag, vor der Korrektur) | `be4d800` |
| 5.2 `BattleBusSlowDeathBehavior.Update` | Suite (nach `be4d800`): Scheitel 45,200 / 44,572 / 44,259; in der Luft 2,3667 / 2,3500 / 2,3500 s. Test: Landung in Bild 72 / 71 | `VehicleModules.cs`: Steiggeschwindigkeit je Bild, Höhe je Tick anteilig, Schwerkraft und Landung mit dem letzten Tick | 45,200 ×3; 2,3667 / 2,3667 / 2,3750 s; Landung Bild 72 ×3; Höhen innerhalb 0,001 | `d311db0` |
| 5.2 `StructureCollapseUpdate.Update` | Suite bestanden; Ticks Tod/Beginn/unten 1/23/76 · 2/46/152 · 4/92/304 (2,5000 s bei jeder Rate, vorher wie nachher); Sichthöhe wich um die Euler-Ordnung ab (rund 0,9 Einheiten nach 53 Bildern bei 60 Hz, gerechnet) | `StructureModules.cs`: Sinkgeschwindigkeit je Bild, das auslösende Update beendet ein Bild (wie der erste Schritt des Originals), Höhe je Tick anteilig, Schwerkraft/Burst/Ende mit dem letzten Tick | Test: Ende und Effekte in denselben Bildern, Sichthöhe innerhalb 0,001 an 200 Bildgrenzen | `01535b0` |
| 5.1 `BridgeScaffoldBehavior.Update`; 5.6 `PerOriginalFrameToPerTick` in `BridgeModules` | kein Suite-Fall; geometrisches Abklingen je Tick, eine `Fixed`-Division je Update | `BridgeModules.cs`, `FrameEnd`: Schritt des Originals mit dem ersten Tick des Bilds, je Tick gleicher Anteil des Rests, Ankunft und nächste Bewegung mit dem letzten Tick; keine Division durch den Ratenfaktor mehr im Update | Test: Lage und Bewegung bitgleich an jeder Bildgrenze bis zur Zerstörung | `501350c` |
| 5.2 `RailedTransportDockUpdate.DoPullInDocking`, `DoPushOutDocking` | kein Suite-Fall. Test: drinnen bzw. frei nach 16 Bildern (30 Hz), bei 60 Hz nach 15 Bildern und einem Tick | `FerryModules.cs`: Ziehen/Schieben je Tick wie bisher, Ankunftsfrage (6 bzw. 3 Einheiten) mit dem letzten Tick jedes Bilds ab Beginn | 16 und 16 Bilder bei jeder Rate, Lage innerhalb 0,001 | `b53cbd3` |
| 5.4 `WaveGuideUpdate.Update`/`Advance`/`DoDamage` | kein Suite-Fall. Test: Welle zerstört in Bild 164 (30 Hz), bei 60 Hz einen Tick in Bild 165 | `WorldEffectModules.cs`: Wegschritt je Tick, Front (Nässe, Umstoßen, Schaden) und Bahnende mit dem letzten Tick jedes Bilds; über 30 Hz setzt die Welle um die Ticks früher an, die ihr erstes Bild braucht; `ToppleForce` geht je Bild an `ApplyTopplingSpeed` | Bild 164 ×3; alle Schäden, Tode, Zerstörungen in denselben Bildern; Lage innerhalb 0,001 | `c333364` |
| 5.6 `DynamicShroudClearingRangeUpdate.Update` | Suite bestanden (Raster 10 Einheiten). Test: erste übergebene Reichweite 3,333 / 1,667 | `WorldEffectModules.cs`: Zuwachs je Bild (30-Hz-Betrag) exakt auf die Ticks verteilt (Tick im Bild aus dem Zustandszähler), Übergabe mit dem letzten Tick eines Bilds | Reichweite bitgleich an 130 Bildgrenzen | `c5b43fa` |
| 5.6 `GarrisonContain.HealOne`, `RepairDockUpdate.Action`, `HealContain.Update` | Suite: Reparaturdock „Bilder mit Heilung“ 150 / 151 / 151 (zwei Fälle); Kaserne 30 / 31 / 30 | `ContainModules.cs`, `RepairModules.cs`, `RetailMatchModules.cs`: die Scheibe des Originals (Betrag über die Dauer in Bildern) einmal je Bild, gezählt ab Eintritt bzw. erster Aktion | 150 ×3 (zweimal); 30 ×3; geheilte Menge in 60 s gleich. Test: Gesundheit bitgleich an jeder Bildgrenze | `b05da71` |
| 5.2 `HeightDieUpdate.Update` (`_lastZ` des Vorticks) | Suite: „slow death AirF_AuroraBombGas“ 0,0000 / – / –. Test: Tod nach 1 / 0, 4 / 3, 21 / 20 ganzen Bildern (30 / 60 Hz) | `ToppleAndFallModules.cs`: Höhenprüfung mit dem letzten Tick jedes Bilds ab dem ersten Update | 0,0000 ×3; 1, 4, 21 Bilder bei jeder Rate | `5440888` |
| – (Messung der Suite) | „collapse: start to down“ 2,4667 / 2,4833 / 2,4917 s, obwohl die Simulation exakt 75 / 150 / 300 Ticks braucht: zwei verkettete `StepUntil` verlieren einen Tick statt eines Bilds | `RateCases.Timed.cs`, `MeasureStructureCollapse`: Ergebnis des zweiten `StepUntil` plus eins | 2,5000 ×3 | `1f87669` |

### Suite-Zahlen der beiden Gruppen (Größen außerhalb der Toleranz)

| Gruppe | Größen | vorher bei 60 / 120 Hz | nachher bei 60 / 120 Hz | Fälle mit Fehlschlag vorher → nachher |
|---|---|---|---|---|
| `timed` | 383 | 53 / 56 | 43 / 46 | 33 → 27 |
| `overtime` | 418 | 80 / 84 | 77 / 81 | 32 → 30 |

„Vorher“ ist der erste vollständige Lauf (`docs/SIMULATION-CORE.md` §39.6). Der Battle-Bus-Fall hatte dort keinen Fehlschlag (ungemessen), mit der
berichtigten Messung einen und nach der Korrektur keinen. Was in `timed` noch fehlschlägt, gehört nicht diesem Paket: Aufladen ab Entstehung (6),
Tarnung/Enttarnung (17), Blitze der Übernahme (9), Hubschrauber-Absturz (7), Minen (3), zwei langsame Tode, eine Lebensdauer und „Befehl bis
umgestoßen“ des Baums bei 120 Hz (6,4667 / 6,5000 / 6,5083 s: die Fahrt des Panzers). In `overtime`: Felder (59), Selbstheilung (4), Basisregeneration
(2), Lautsprecherturm (4), Heilradius/Hospital/Reparaturbucht und die Wege in Kaserne und Tunnel (12, darunter `TunnelContain` 75 / 76 / 76).

## 3. Verwendungen des Auswegs „Originalraster“

Jede Stelle läuft mit dem letzten (bzw. ersten) Tick eines Bilds **des Objekts**, nicht auf dem Weltraster; keine betrifft Bewegung, Befehlsannahme
oder Zielerfassung.

| Stelle | Was nur einmal je Originalbild läuft | Grund |
|---|---|---|
| `RailroadBehavior.Collide` | Kollisionsantwort des Zugs (Töten, Quetschschaden `Tempo · 10`, Wegschieben um ein Viertel der Überlappung), Zerstörung am Streckenende | Schaden und Schub sind Beträge je Bild; je Tick wären sie doppelt, und das Viertel der Überlappung klingt geometrisch ab |
| `RailedTransportDockUpdate` (Ziehen, Schieben) | nur die Ankunftsfrage (6 bzw. 3 Einheiten) | das Original fragt einmal je Bild; die Bewegung läuft je Tick |
| `WaveGuideUpdate.DoDamage`, Bahnende | Abtastung der Front, Ende der Bahn | eine dichtere Abtastung trifft Objekte, an denen eine gröbere vorbeigeht |
| `HeightDieUpdate.Update` | Höhenprüfung, „bewegt sich abwärts“ | vergleicht mit der Höhe des vorigen Bilds; je Tick stirbt das Objekt in einer Höhe, die das Original nie sieht |
| `HealContain.Update`, `GarrisonContain.HealOne`, `RepairDockUpdate.Action` | Heilscheibe | das Original heilt einmal je Bild; die Gesundheit an jeder Bildgrenze soll die von 30 Hz sein (Heilung ist keine Bewegung) |

Nicht der Ausweg, sondern das Unterteilungsprinzip (zweite Ordnung einmal je Bild, Lage je Tick): Umstürzen, Gebäudesturz, Zug (Schaffner, Reibung),
Lawine, Battle Bus, Einsturz, Brückengerüst, dynamische Aufdeckung.

## 4. Für die Zusammenführung

- **`ToppleUpdate.ApplyTopplingForce` rechnet einmal um** (eine markierte Zeile, `PHASE 23 MERGE`): es nimmt weiter ein Tempo je Tick, weil
  `SimulationWorld.Collision.cs`, `TryTopple` (Paket GROUND) `Entity.Speed` übergibt. Sobald `TryTopple` Strecke je Originalbild übergibt: den Faktor
  entfernen (sonst doppelte Umrechnung) und alle Aufrufer auf `ApplyTopplingSpeed` legen. `WaveGuideUpdate` ruft bereits `ApplyTopplingSpeed`.
- **`AirWeaponModules.cs` Zeile 787 (Neutronenexplosion, nicht in diesem Paket)** ruft `ApplyTopplingForce` mit `blast.ToppleSpeed`, einem Wert je
  Originalbild aus den Daten: bis zum Entfernen der markierten Zeile wird er bei 60/120 Hz mit 2 bzw. 4 multipliziert (vorher war dieselbe Stelle auf
  andere Weise falsch: Beschleunigung je Tick² aus einem Wert je Bild). Bei 30 Hz unverändert.
- **`tests/CnCNew.Simulation.Tests/Program.cs`**: zwölf neue Zeilen nach „Collision: HeightDieUpdate …“ (Konfliktstelle, falls andere Pakete dort
  einfügen).
- **`Tests.TickRate.ModulesAM.cs`** (`TickRateModulesAmTensileFormation`): die Phase der Ruheprüfung der Lawine steht jetzt in Bildern.
- **`RateCases.Timed.cs`** (Suite, zwei Messkorrekturen in eigenen Commits): Battle Bus, Einsturz.

## 5. Offen, mit Grund

- **Kein Suite-Fall für die Lawine am Hang.** `RateScene` kennt nur die flache Karte (`FlatMap`); ein Hang hätte die gemeinsame Szenendatei der
  Suite geändert. Der Drei-Raten-Test am Hang deckt die Rekursion ab; die Suite prüft weiter nur Beginn und Dauer.
- **Kein Suite-Fall** für Brückengerüst, Fähre, Flutwelle, `HeightDieUpdate` (außer mittelbar) und `GarrisonContain` – wie in §39.5 als „nicht
  abgedeckt“ geführt; je ein Drei-Raten-Test auf dem synthetischen Bestand.
- **Versatz unter einem Bild bei Auslöser im selben Tick** (§1): Fähre, wenn das Dock nach dem Andockenden im selben Tick läuft; Battle Bus, wenn der
  erste Tod auf einen Hüllen-Prüftick fällt (Suite: in der Luft 2,3667 / 2,3667 / 2,3750 s). Innerhalb der Toleranz, nicht exakt.
- **Letzte Bits bei Werten aus dem Katalog je Tick:** Schwerkraft (Battle Bus, Einsturz), Zieh-/Schubstrecke der Fähre, Wegschritt der Welle – die
  Zustände sind an den Bildgrenzen innerhalb 0,001 Einheiten gleich, nicht bitgleich. Bitgleichheit bräuchte Werte je Bild im Katalog (ein neues Feld
  ändert das Eingangsmanifest).
- **Zug:** eine Station, die der Zug innerhalb eines Bilds vollständig überfährt, wird bei 60/120 Hz gesehen und bei 30 Hz nicht (Streckenstücke
  kürzer als ein Bildweg; in den Retail-Daten nicht gesucht). Wagen in der Kulisse oder hinter dem Streckenende liegen bei 60/120 Hz für ein Bild an
  einer anderen Stelle als bei 30 Hz (sie kollidieren dort mit nichts; die Ausrichtung hängt an der Zahl der Züge am Haken, nicht am Spiel).
- **Flutwelle:** der Zufallszug des Platschens fällt über 30 Hz einen Tick früher (die Welle setzt früher an); Zahl und Folge der Züge sind gleich.
- **Zustandsbetrag `_wantsToBeLead`, `_waitAtStationTimer`** (Zug) und `healthPerTick` (Dock) haben bei 60/120 Hz eine andere Einheit als ihr Name
  (Bilder bzw. je Bild); die Namen sind Snapshot-Schlüssel und bleiben.

## 6. Neue Befunde

- **Verkettete `StepUntil` der Suite verlieren einen Tick statt eines Bilds** (`RateScene.StepUntil` zählt ab dem Schritt, der einen Befehl ausführt;
  ein zweiter Aufruf ohne Befehl beginnt schon einen Tick später). Berichtigt für den Einsturz; andere Fälle mit derselben Form (z. B. Tarnung,
  Aufdeckung in `RateCases.Timed.cs`) sind nicht geprüft.
- **„Schon wach“ im selben Tick:** `SimulationWorld.WakeAt` lässt ein Modul, das in diesem Tick ohnehin läuft, im selben Tick handeln statt im
  nächsten. Ob ein Ereignis (erster Tod des Busses) im selben oder im nächsten Tick aufgenommen wird, hängt damit an der Phase eines periodischen
  Updates – bei jeder Rate, aber mit anderem Gewicht (ein Tick ist ein halbes Bild).
- **`HeightDieUpdate` beendet `AirF_AuroraBombGas` im ersten Bild** (Zielhöhe 40, Objekt am Boden): der Suite-Fall „slow death“ misst deshalb nur,
  ob das Objekt eine Sekunde später noch da ist.
- **Der Battle Bus ist 30 s nach dem ersten Tod bei jeder Rate tot** („alive after the first death“ 0 / 0 / 0): die Strafe für die leere Hülle;
  kein Ratenunterschied.
- **Zug am Streckenende:** ein Wagen, der die Strecke verlassen hat, wird an den vorletzten Streckenpunkt gesetzt (`FindPosition` liefert bei
  „Abstand = Streckenlänge“ den Punkt davor) – bei jeder Rate, ohne Spielwirkung, im Test aufgefallen.

## 7. Prüfstand

- **30 Hz:** `input-manifest check` identisch und `reference-manifest check --set quick` 9 Läufe identisch je Commit, der die Simulation ändert – jeweils aus
  einer Kopie der Binärdateien genau dieses Quellstands. Ohne eigenen Lauf: die beiden reinen Suite-Commits (`be4d800`, `1f87669`) und der Test-Commit
  `a6da89c`. Am Ende `--set full`: **50 Läufe identisch**, einmal vor und einmal nach dem Aufräum-Commit `2dc46d9` (Endstand).
- **Simulationstests bei 30 Hz:** Debug-Lauf `dotnet run --project tests/CnCNew.Simulation.Tests` 430 von 430 grün (418 vorher + 12 neue).
  Zwischen `9871bc2` und `2dc46d9` schlug der Quelltexttest „Determinism rules hold in the simulation sources“ an fünf Zeilen dieses Pakets fehl
  (nackte Tickzahlen, ein Konstantenname); `2dc46d9` behebt das ohne Verhaltensänderung. Der volle Testlauf lief nach dem ersten Commit und am Ende,
  dazwischen nur die betroffenen Gruppen.
- **`--tick-rate 60` / `120` (Release, ohne Realdaten):** 6 / 13 Fehlschläge (vorher 6 / 14). Weg ist Nr. 10 aus `TICKRATE-MIGRATION.md` §15.2
  („Collision: ToppleUpdate push, kill, stump and mid-fall snapshot continuation“), ohne Änderung des Tests. Der Test „Repair: RepairDockUpdate
  heals over TimeForFullHeal“ zählte Heilschritte je Tick und ist in `a6da89c` auf Bilder umgestellt (zwischen `b05da71` und `a6da89c` schlug er bei
  60/120 Hz fehl). Die übrigen Fehlschläge gehören anderen Paketen (Geschosse, Türme, Blitze der Übernahme, Tarnung, Chinook, Bau/Verkauf, Referenzmatch).
  Mit Realdaten bei 60 Hz: dieselben 6 und die zehn Gruppentests der Suite; der Lauf bei 120 Hz mit Realdaten war beim Schreiben nicht beendet.
- **Sauberer Build:** `dotnet build CnCNew.slnx` 0 Warnungen, 0 Fehler (ohne vorheriges Löschen von `bin`/`obj`).
- **Nicht gemessen:** Kosten je Tick bei 30 Hz (hinzugekommen sind je Update ein Zähler und ein Vergleich, keine Division im 30-Hz-Pfad außer
  `ToOriginalFrames` in den Heil- und Aufdeckungsphasen); das Leistungstor bei 60 Hz.
- Zwölf neue Drei-Raten-Tests „Rate invariance (physics): …“ (laufen immer bei 30, 60 und 120 Hz, unabhängig von `--tick-rate`).
