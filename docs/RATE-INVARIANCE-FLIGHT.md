# Raten-Invarianz, Paket FLIGHT (Phase 23)

Stand: 8. Oktober 2026, Branch `worktree-agent-ab31e8d3a443e52b0`, Ausgangsstand `7b54374`. Auftrag: `docs/HANDOFF-PHASE23.md` Schritt 3 für Flug, Flugfeld, Hubschrauber,
Fallschirm, Leuchtkörper, Partikelstrahl, Spectre und Tunnel (`docs/TICKRATE-MIGRATION.md` §5.1, §5.2, §5.3, §5.6). Messlatte ist die Raten-Äquivalenz-Suite
(`docs/SIMULATION-CORE.md` §39) und, wo sie keinen Fall hat, je ein Drei-Raten-Test auf der synthetischen Fixture (`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Flight.cs`).
„Bild“ heißt Logikbild des Originals (1/30 s), „Tick“ ein Schritt der Simulation. Alle Zahlen sind an diesem Tag gemessen; Werte stehen als 30 / 60 / 120 Hz.

**Bei 30 Hz ist nichts anders:** `input-manifest check` identisch und `reference-manifest check --set quick` (9 Läufe) identisch nach jedem Korrektur-Commit (drei Ausnahmen in §7),
`--set full` am Ende (§7). Kein Katalogtyp hat ein neues Feld; kein Referenzwert wurde geschrieben.

## 1. Das Prinzip, wie es hier angewendet ist

Der 30-Hz-Schritt ist die Definition einer Integration. Bei höherer Rate wird er **einmal je Bild entschieden** – mit denselben Ausdrücken wie bei 30 Hz, in Einheiten eines Bilds –
und **in gleichen Anteilen je Tick geflogen** (Lage, Höhe und Blickwinkel). Am Ende jedes Bilds steht das Objekt dort, wo der 30-Hz-Schritt es hinstellt; dazwischen auf der Geraden
dorthin. Größen zweiter Ordnung (Tempo, Fall- und Steiggeschwindigkeit, gedämpftes Bahntempo, Eigendrehung) ändern sich nur bei der Entscheidung.

- **Wann entschieden wird:** im ersten Tick eines Bilds; die restlichen `Rate/30 − 1` Ticks fliegen nur den Rest. Die Phase gehört dem Objekt (es entscheidet wieder, sobald sein Bild
  geflogen ist), nicht dem Weltraster – ein Objekt, das mitten in einem Bild entsteht, stirbt oder einen Befehl bekommt, hat sein eigenes Raster. Ausnahme ist der Spectre (Zeile 15/16):
  sein Haubitzentakt liegt auf dem Weltraster (`tick % HowitzerFiringRate`), also entscheidet er dort.
- **Ereignisse und Lagen:** was die Entscheidung auslöst (Zustandswechsel, Ankunft, Aufschlag, Öffnen des Schirms), fällt auf den ersten Tick des Bilds – dieselbe Spielzeit wie bei
  30 Hz. Die Lage erreicht ihren 30-Hz-Wert mit dem letzten Tick des Bilds, also bis zu `Rate/30 − 1` Ticks (ein halbes bzw. drei Viertel Bild) nach dem 30-Hz-Tick. Das ist die Folge
  der Unterteilung, kein Fehler; ein Beobachter, der eine Schwelle auf der Lage misst, sieht sie um höchstens so viel später.
- **Das Bild, das ein Ziel erreicht** (Boden, Wegpunkt, Parkplatz), setzt das Objekt sofort dorthin, wie der 30-Hz-Schritt – es wird nicht mehr verteilt.
- **Befehle:** ein geparkter Jet ohne Auftrag verbraucht kein Bild, er sieht jeden Tick nach; ein Startbefehl wirkt im Tick, in dem er gegeben wird. Ein Befehl an ein Flugzeug im
  Flügelflug wird sofort angenommen und ab dem nächsten Bild angesteuert (höchstens 1 Tick später bei 60 Hz, 3 bei 120 Hz); das laufende Bild wird zu Ende geflogen, damit die Bahn
  dieselbe 30-Hz-Bahn bleibt. Ein Versuch, das Bild beim Befehl abzubrechen, machte die Schleife zurück zum Ziel um bis zu 0,37 s anders (Raptor) und wurde verworfen.
- **„Einen Tick später“:** das erste Update nach dem Erscheinen oder dem Tod bleibt einen Tick später (`SleepNone`). Damit hält z. B. die Erwartung `death + 1 + Frames(74)` des
  Hubschrauber-Tests bei jeder Rate. Gegenüber einem `KillCommand`, der am Anfang eines Schritts wirkt, liegen die Ereignisse dadurch bis zu `Rate/30 − 1` Ticks früher als bei 30 Hz
  (innerhalb eines Bilds); gegenüber Schaden am Ende eines Schritts liegen sie exakt.
- **Rechenwerte:** `FlightFrames.PerFrame`/`PerFrameSquared` machen aus den Katalogwerten je Tick bzw. je Tick² Werte je Bild (Faktor 1 bei 30 Hz, 2 und 4 bzw. 4 und 16 auf dem
  Rohwert, exakt). Die Schwerkraft je Bild² kommt aus dem Definitionswert selbst (`FlightFrames.GravityPerFrameSquared`) und ist bei jeder Rate dieselbe Zahl. Wo ein Modul eigene
  Daten hat (Hubschrauber-Absturz, Tunnel), hält sein Datensatz bei jeder Rate die Werte je Bild des 30-Hz-Katalogs; die Rechnung des Schritts ist dann bei jeder Rate bitgleich
  (bei 30 Hz dieselben Werte wie bisher, das Eingangsmanifest ist unverändert).
- **Zustand:** der noch nicht geflogene Rest eines Bilds ist Zustand (`FlightFrameStep`: Ticks übrig und Anteil je Tick). Er wird nur geschrieben, solange ein Bild geflogen wird; bei
  30 Hz ist ein Bild ein Tick, es gibt ihn dort nie. Der Test `RateFlightFrameStepsSurviveSnapshots` prüft je Modul: bei 60 und 120 Hz steht der Schlüssel mitten im Bild im Snapshot und
  die wiederhergestellte Welt läuft 12 Bilder hashgleich weiter; bei 30 Hz steht er nie darin.

Neue Datei: `src/CnCNew.Simulation/Time/FrameGrid.Flight.cs` (`FlightFrames`, `FlightFrameStep`).

## 2. Ursachen

| # | §5-Eintrag | Befund vorher (30 / 60 / 120 Hz) | Korrektur (Datei, Symbol, Prinzip) | nachher | Commit |
|---|---|---|---|---|---|
| 1 | 5.2 `AIUpdateInterface.MoveWinged`/`SteerWinged`, `JetAIUpdate.CircleStep` (Kreisbahn ≈ 0,93 · Radius hängt am Schritt) | Suite `flight`: Flugzeit ab Abheben Raptor 19,5667 / 19,6333 / 19,6583 s, Aurora 18,2667 / 18,8000 / 19,0833 s, MiG 19,8000 / 20,1167 / 20,2583 s | `Phase5Modules.cs` nur `MoveWinged`/`SteerWinged` und die Hilfen daneben; `JetAIUpdate.Flight.cs` `CircleStep`: Wende, Tempoänderung und Weg eines Bilds, Anteile je Tick (`_wingStep`, von beiden geteilt), Ankunftsradius ein Bild Weg | Raptor 19,5667 / 19,5667 / 19,5667, Aurora 18,2667 / 18,2667 / 18,2667, MiG 19,8000 / 19,8000 / 19,8000 s (Endstand); Test: 900 Bilder Flug und Kreisen, Ankunft in Bild 216 / 216 / 216, größter Abstand zur 30-Hz-Bahn 0,0002 / 0,0002 Einheiten | `bbee8ab` (+ `6601ef1`) |
| 2 | 5.2 `JetAIUpdate`: `TaxiAlongPath` (Radius `max(Speed, 1)`), `TurnTo`, `TakeOff` (beide Zweige), `Land` (beide Zweige); 5.6 `PerOriginalFrameToPerTick` an der Verwendung | Start Raptor 4,8333 / 4,7667 / 4,7250 s, MiG 4,9667 / 4,9167 / 4,8833 s; produziert bis geparkt 1,3667 / 1,3333 / 1,3333 s; erster Schuss des Angriffs 18,6333 / 18,5833 / 18,5417 s | `JetAIUpdate.cs` `Update`/`FlyOn`: die Zustände entscheiden einmal je Bild (ein Zustandswechsel kostet ein Bild), ihre Bewegung in Bild-Einheiten, Anteile je Tick (`_frame`); Übergabe der Startbahn „nach dem nächsten Schritt“ ein Bild später; die drei eigenen Tempi sind Konstanten je Bild, keine Division mehr | Start 4,8333 × 3 bzw. 4,9667 × 3; geparkt 1,3667 × 3; erster Schuss 18,6333 / 18,6500 / 18,6333 s; jeder Zustandswechsel des Raptor-Einsatzes zur selben Spielzeit (Spur) | `60fcb57` |
| 3 | 5.2 `JetAIUpdate.ControlHeight` (`Speed/8` als Beschleunigung, `Speed/2` als Deckel) | Comanche „steigt auf Flughöhe“ 1,6667 / 1,6500 / 1,6333 s | `JetAIUpdate.cs` `ControlHeight`: ein Schritt je Bild, Auftrieb, Schwerkraft, Tempo und Steiggeschwindigkeit (`_verticalSpeed`) je Bild, Höhenänderung in Anteilen | 1,6667 × 3 | `bb00e5f` |
| 4 | 5.2 `JetSlowDeathBehavior.Update` (Fall) | Test: Aufschlag in Bild 49 / 50 / 50, weg in 52 / 53 / 53, Abstand zur 30-Hz-Bahn 2 / 1 Einheiten | `FlightModules.cs`: Fallgeschwindigkeit je Bild, einmal je Bild erhöht, Schritt in Anteilen (`_fall`) | 49 × 3, 52 × 3, 0 / 0 | `307bf14` |
| 5 | 5.2 `ParachuteContain.Update` (freier Fall, Sinkflug, Ankunftsradius eine Tickstrecke) | Test: öffnet nach 16 / 16,5 / 16,5 Bildern, Reiter am Boden nach 106 / 106 / 106,5, Abstand zur 30-Hz-Bahn 0,3022 / 0,5056 (gelenkt 0,3022 / 0,7111) | `FlightModules.cs`: Abstieg als Bildschritt, Sink- und Lenktempo je Bild, Landepunkt innerhalb eines Bilds Weg, Anteile (`_descent`); der Reiter wird jeden Tick mitgeführt | 16 × 3, 106 × 3, 0 / 0 | `8435ba8` |
| 6 | 5.2 `CountermeasuresBehavior.MoveFlares` | Test: Weg des ersten Leuchtkörpers in 25 Bildern 127,7778 / 129,1667 / 129,8611 | `FlightModules.cs`: die Bremsung eines Bilds einmal je Bild, gezählt ab dem ersten Schritt des Leuchtkörpers; dazwischen gleiches Tempo, Bewegung je Tick; kein neuer Zustand | 127,7778 / 127,7777 / 127,7777 | `c5a8a35` |
| 7 | 5.2 `ParticleUplinkCannonUpdate.MoveBeam` (manuell, Wegpunktwechsel) | Test: zweiter Wegpunkt nach 10 / 9,5 / 9,5 Bildern, letzter nach 19 / 18 / 17,75, Abstand 10 / 15 Einheiten | `FlightModules.cs`: Fahrt als Bildschritt (Weg eines Bilds, höchstens der Rest; nächster Wegpunkt ab dem Folgebild), Anteile (`driveLeft`) | 10 × 3, 19 × 3, 0 / 0 | `5954338` |
| 8 | 5.1 `HelicopterSlowDeathData.SpiralOrbitForwardSpeedDamping`; 5.2 `HelicopterSlowDeathBehavior.Update` (Spin, Bahn, Fall, Vergleich `Tick − _lastSelfSpinUpdate > SelfSpinUpdateDelay`) | Suite `timed`: Comanche letzte Lage 26,524 / 20,950 / 24,518, Drehsumme 18,11652 / 17,17403 / 17,06059 rad; Helix tiefste Höhe 0,000 / 0,798 / 0,935, letzte Lage 146,079 / 144,093 / 141,509 | `TransportModules.cs` `Spiral`: Bildschritt mit Dämpfung einmal je Bild, Anteile (`_spiral`); Eigendrehung und Wartezeit am Boden wechseln an Bildgrenzen und zählen Bilder; `HelicopterSlowDeathData` hält bei jeder Rate die Werte je Bild des 30-Hz-Katalogs | alle 18 Größen der drei Fälle bestehen: 26,524 × 3, 18,11652 × 3, 0,000 × 3, 146,079 × 3; Test: Aufschlag 95 × 3, weg 111 × 3, Abstand 60,5227 × 3 | `cee88c4` |
| 9 | 5.3 `HelicopterSlowDeathBehavior.BeginSlowDeath` (`_bladeFlyOff` über ungerundete Ticks) | Test: Rotor fliegt ab nach 30 / 30,5 / 30,5 Bildern | `TransportModules.cs`: Grenzen in ungerundeten Bildern des 30-Hz-Katalogs (ein Zug, derselbe Bereich bei jeder Rate), Zähler −1 je Bild | 30 × 3 | `82dbdba` |
| 10 | 5.2 `TunnelContain.Update` (`MaxHealth / TicksForFullHeal` je Tick) | Suite `overtime`: Bilder mit Heilung 75 / 76 / 76, Zeit bis voll 10,3667 / 10,4167 / 10,4333 s | `TransportModules.cs`: **Ausweg Originalraster** – einmal je Bild, gezählt ab dem Eintritt, die Scheibe der 30-Hz-Division; `TicksForFullHeal` in Bildern | 75 × 3; 10,3667 / 10,3833 / 10,4000 s (Rest: der Weg in den Tunnel) | `81931ab` |
| 11 | 5.6 `ChinookAIUpdate.Update` (`climb = locomotor.Speed`) | Test (Weg kein Vielfaches des Schritts): Passagier draußen nach 22 / 21,5 / 21,5 Bildern, wieder oben in Bild 43 / 42 / 42, Abstand 2,3333 / 2,3333 | `TransportModules.cs`: Höhenschritt je Bild, Anteile (`_climb`) | 22 × 3, 43 × 3, 0 / 0 | `0bf4e6c` |
| 12 | 5.2 `SpectreGunshipUpdate.Fly`, `Orbit` | Test: beginnt zu kreisen nach 42 / 42,5 / 42,75 Bildern, fliegt ab nach 132 / 132,5 / 132,75, Abstand 9,1849 / 13,8413 | `AirWeaponModules.cs` nur `SpectreGunshipUpdate`: Flug als Bildschritt am Weltraster, Zielpunkt der Kreisbahn, Wechsel in die Kreisbahn und Abflugprüfung bei der Entscheidung, Anteile (`_flight`) | 42 × 3, 132 × 3, 0 / 0 | `e7a3153` |
| 13 | 5.2 `SpectreGunshipUpdate.Strafe`, `_okToFireHowitzerCounter` | Test (Computer-Spieler, Zielpunkt muss 2,8 Schritte wandern): erste Haubitze nach 48 / 51 / 51 Bildern, 26 / 23 / 23 Granaten | `AirWeaponModules.cs` `Strafe`: Bildschritt am Weltraster, „am Ziel“ bei Rest < Schritt eines Bilds, ein Zähler je Bild, Nachlauf in Bildern verglichen, Anteile (`strafeLeft`) | 48 × 3, 26 × 3 | `fc0480f` |

Zwei Commits berichtigen eine Messung, keine Simulation:

- `234f3c3` – der Einsatz-Fall der Suite (`RateCases.Flight.cs`, `MeasureSortie`) gab seinen zweiten und dritten Befehl einen Tick nach der gesehenen Ankunft: ein ganzes Bild bei
  30 Hz, ein Viertel bei 120 Hz. Die Suite verlangt selbst, dass eine Messung an einer Bildgrenze beginnt (`docs/SIMULATION-CORE.md` §39.1); beide Befehle bekommen jetzt
  `RateScene.Align` davor (bei 30 Hz wirkungslos). Keine Toleranz, keine Größe ist berührt. „Kehre: Befehl bis umgekehrter Kurs“ Raptor und MiG 1,4333 / 1,4667 / 1,4750 s →
  1,4333 / 1,4500 / 1,4500 s; Raptor „Landebefehl bis Aufsetzen“ 16,2333 / 16,2500 / 16,2583 → 16,2333 × 3.
- `1927883` – „Chinook: helicopter slow death …“ erwartete `hit + 1 + Frames(9)` (neun Bilder und ein Tick); richtig ist `hit + Frames(10)` (bei 30 Hz dieselbe Zahl).

## 3. Suite, vorher → nachher

Fehlschläge außerhalb der Toleranz bei 60 / 120 Hz, gemessen mit `--only "RateEquivalence: <gruppe>"`:

| Gruppe | Größen | Ausgangsstand | Endstand | davon dieses Paket | Rest gehört zu |
|---|---|---|---|---|---|
| `flight` | 74 | 21 / 21 | **9 / 11** | 12 / 12 → **0 / 2** | 4 × Produktionszeit „N Bilder weniger ein Tick“ (Paket TIMING); 5 × Luftbewegung der Hubschrauber über `AIUpdateInterface.Move` (Paket GROUND: Comanche „Befehl bis Ankunft“, „umgekehrter Kurs bis Ankunft“, „Landebefehl bis Aufsetzen“; Ankunftszeit von Comanche und Chinook) |
| `timed`, nur „helicopter slow death“ (3 Fälle, 18 Größen) | 18 | 7 / 7 | **0 / 0** | 7 / 7 → **0 / 0** | – (Gruppe gesamt 53 / 56 → 46 / 49) |
| `overtime`, nur „heal inside GLATunnelNetwork“ (5 Größen) | 5 | 2 / 4 | **0 / 2** | 2 / 2 → **0 / 0** | „erstes“ und „zweites Bild mit Heilung“ bei 120 Hz (7,9000 / 7,9167 / 7,9583 s): der Fußweg in den Tunnel, Paket GROUND (Gruppe gesamt 80 / 84 → 78 / 82) |

Die elf verbleibenden Fehlschläge der Gruppe `flight` am Endstand: Produktionszeit von Raptor, Aurora, MiG und Comanche (19,9667 / 19,9833 / 19,9917 s und entsprechend;
60 und 120 Hz); Comanche „Befehl bis Ankunft“ 21,8667 / 21,9500 / 21,9917 s, „umgekehrter Kurs bis Ankunft“ 13,3333 / 13,4500 / 13,5167 s, „Landebefehl bis Aufsetzen“
12,7667 / 12,8333 / 12,8667 s; Ankunftszeit des freien Comanche 13,5333 / 13,6167 / 13,6583 s und des Chinook 11,7000 / 11,7833 / 11,8417 s (alle 60 und 120 Hz); und die beiden
dieses Pakets, nur bei 120 Hz (§5). Fünf Größen des Fallschirmabwurfs sind bei jeder Rate ungemessen (§6). Alle anderen Größen der Jets – Rollen, Start, Flugzeit, Kehre, Landung,
Parken, erster und letzter Schuss – liegen bei 60 und 120 Hz innerhalb der Toleranz, die meisten exakt (Start, Ankunft, „produziert bis geparkt“: Abweichung 0).

## 4. Ausweg „Originalraster“

Genau eine Stelle läuft als Ganzes nur einmal je Bild:

| Stelle | Was | Warum |
|---|---|---|
| `TunnelContain.Update` (`TransportModules.cs`) | Heilung eines Insassen einmal je Bild, gezählt ab dem Tick seines Eintritts | Das Original heilt einmal je Bild; Heilung ist weder Bewegung noch Befehlsannahme noch Zielerfassung. Scheiben je Tick sind für sich gerundete Zahlen, die sich nicht zur Scheibe eines Bilds addieren (100/30 rundet auf, 100/60 und 100/120 ab) – die letzte Heilung fiel auf ein anderes Bild –, und ein Insasse, der zwischen zwei Bildern eintritt, würde in einem Bild mehr geheilt. |

Kein Ausweg, sondern das Unterteilungsprinzip (Entscheidung je Bild, Bewegung je Tick) sind: die Zustände des Jets (`JetAIUpdate.Update`: ein Zustandswechsel kostet ein Bild, Zeitgeber,
Angriffs-Locomotor, Schaden beim Kreisen und die Befehlsannahme bleiben je Tick), der Schritt des Hubschrauber-Absturzes samt Rotor-Zähler und Wartezeit am Boden, das Wandern und der
Zähler des Spectre-Zielpunkts (der Zielpunkt wandert in Anteilen je Tick, das Gattling feuert je Tick), die Bremsung der Leuchtkörper.

## 5. Offen und warum

- **`flight`, MiG „Landebefehl bis Aufsetzen“ bei 120 Hz: 14,6333 / 14,6333 / 14,5667 s (−8 Ticks).** Die Ankunft am Anflugpunkt wird drei Bilder früher entschieden (Spur: Wechsel
  zu „wartet auf Landung“ bei 6.840 / 6.840 / 6.828 Hundertzwanzigsteln). Die Lagen der drei Raten unterscheiden sich dort um rund 0,01 Einheiten, weil die Katalogwerte je Tick² für
  jede Rate eigens gerundet sind (16 · a₁₂₀ ist nicht a₃₀: bis 8 Rohwert-Einheiten, relativ rund 10⁻⁵); die Ankunftsregel des Flügelflugs („Ziel liegt hinter mir innerhalb des
  Wendekreises“) kippt an einer Schwelle. Nicht behebbar, ohne `LocomotorInfo` die Werte je Bild des 30-Hz-Katalogs mitzugeben (ein Katalogfeld, nicht in diesem Paket).
- **`flight`, Raptor-Angriff „Befehl bis geparkt und nachgeladen“ bei 120 Hz: 60,8000 / 60,8000 / 60,5333 s.** Die Verfolgung endet ein Bild früher (5.372 / 5.372 / 5.368), die
  Schleife zurück zum Flugfeld ist danach 7 Bilder kürzer. Dieselbe Ursache: eine Schwelle der Ankunft kippt an einem Hundertstel einer Einheit, und die Bahn danach verstärkt es.
- **Befehl mitten im Bild** (Flügelflug): wirkt ab dem nächsten Bild, höchstens 3 Ticks später bei 120 Hz (§1). Zusammen mit dem Nachlauf der Lage kann ein Beobachter eine Schwelle
  auf dem Blickwinkel bei 120 Hz bis zu 6 Ticks nach dem 30-Hz-Wert sehen, wenn er den Befehl mitten im Bild gibt; an der Bildgrenze gegeben sind es höchstens 3.
- **Fallschirmabwurf der Spezialkraft** (Suite `flight/paradrop`): bleibt bei jeder Rate ungemessen (5 Größen). Ursache ist kein Ratenunterschied und ist nicht behoben, weil die
  Korrektur 30 Hz ändert (§6).
- **`DeliverPayloadAIUpdate`** steht in keiner Zeile von §5 und ist nicht angefasst: sein Flug ist die Luftbewegung von `AIUpdateInterface.Move` (Paket GROUND), `CloseEnoughToTarget`
  und der Tiefflug werden je Tick geprüft (feinere Abtastung, kein eigener Fall in der Suite).
- **Nicht gemessen:** der Jet-Angriff über ein echtes Match bei 60 Hz, Flugzeuge der Generäle außer Raptor, Aurora, MiG und Comanche, Flugfelder mit Modellknochen (die Suite baut ohne
  Modellgeometrie), mehrere Jets an einer Startbahn bei 60/120 Hz (`FindWaiter`), ein Jet über einem toten Flugfeld. Kein Mensch hat einen Flug bei 60 Hz gesehen.

## 6. Neue Befunde

- **Der Fallschirmabwurf der Spezialkraft setzt bei jeder Rate niemanden ab** (auch bei 30 Hz). Zwei Ursachen, beide in der Erzeugung, nicht in der Suite:
  (a) `SimulationWorld.ExecuteDeliverPayload` reiht die Fracht **vor** ihrem Fallschirm ein; beim Entstehen des Rangers gibt es seinen Behälter noch nicht, er bleibt am Kartenrand
  stehen und der Fallschirm bleibt leer (`ParachuteContain.Update` tötet einen Schirm ohne Reiter). Probeweise vertauscht (nicht committet) sitzen die fünf Ranger in ihren Schirmen.
  (b) Danach nimmt das Frachtflugzeug den Fallschirm nicht auf: `AmericaJetCargoPlane` erlaubt `INFANTRY VEHICLE PROJECTILE DOZER PARACHUTABLE`, `AmericaParachute` ist `PARACHUTE`
  mit `TransportSlotCount 0`; `SpawnEntity` fragt `IsValidContainerFor`, das Original (`DeliverPayloadNugget::create`, `ObjectCreationList.cpp`) ruft `addToContain` ohne diese Frage.
  Die Schirme landen dann im Bild ihrer Entstehung 3.000 Einheiten vom Ziel. Beides zu beheben ändert das Verhalten bei 30 Hz (Abwürfe in Gefechten und Missionen) und ist deshalb
  hier nicht gemacht. Der skriptgesteuerte Abwurf (`ScriptEngine.Reinforcements.cs`) ist nicht betroffen.
- **Die Umkehr der Eigendrehung des Hubschrauber-Absturzes hängt am letzten Bit einer Summe**, wenn `(MaxSelfSpin − MinSelfSpin)` ein Vielfaches von `SelfSpinUpdateAmount` ist
  (Comanche): mit Werten je Tick der jeweiligen Rate kippte sie bei 120 Hz ein Intervall später (Drehsumme 18,117 → 18,530 rad). Gelöst durch die Werte je Bild des 30-Hz-Katalogs im
  Datensatz (Zeile 8). Dasselbe Muster gilt überall, wo Katalogwerte je Tick addiert und gegen eine Grenze verglichen werden.
- **Katalogwerte je Tick² sind je Rate eigens gerundet**; ein Bildschritt aus `Wert · (Rate/30)²` ist deshalb nicht bitgleich zum 30-Hz-Schritt (relativ rund 10⁻⁵ bei
  Beschleunigungen). Für Bahnen ohne Schwelle ist das unsichtbar (0,0002 Einheiten nach 900 Bildern Flügelflug), an Schwellen kann es ein Bild kosten (§5). Abhilfe wäre, die Werte
  je Bild des 30-Hz-Katalogs im Locomotor mitzuführen.
- **`TemplateCatalog.GravityPerTickSquared` rechnet bei jedem Aufruf eine Dezimaldivision** (Eigenschaft ohne Zwischenspeicher); die Flugmodule rufen jetzt
  `FlightFrames.GravityPerFrameSquared` einmal je Entscheidung, gleich teuer wie bisher je Tick.
- **Der Haubitzentakt des Spectre zählt ab Tick 0 der Welt** (`tick % HowitzerFiringRate`), nicht ab dem Einsatz: zwei Einsätze, die ein Bild auseinander beginnen, feuern die erste
  Granate bis zu einem Takt (10 Bilder in den Daten) verschieden nach dem Befehl – bei jeder Rate.
- **`HelicopterSlowDeathBehavior`: `_lastSelfSpinUpdate` beginnt bei 0** („seit Weltbeginn“); ein Absturz in den ersten `SelfSpinUpdateDelay` der Welt ändert die Eigendrehung erst
  später. Bei jeder Rate gleich, nur in Tests mit sehr junger Welt sichtbar.

## 7. Tests und Manifeste

- **Drei-Raten-Tests** (`Tests.RateInvariance.Flight.cs`, 12 Tests, ohne Realdaten, nennen ihre Raten selbst): Flügelflug, Jet am Flugfeld (Zustände und Bahn), abgeschossener Jet,
  Fallschirm (gerade und gelenkt), Leuchtkörper, Partikelstrahl auf einem Wegpunktpfad, Hubschrauber-Absturz, Tunnelheilung (an der Bildgrenze und einen Tick später betreten),
  Chinook, Spectre (Mensch und Computer-Spieler), Snapshot mitten im Bild je Modul.
- **Simulationstests bei 30 Hz:** alle 429 grün ohne Realdaten (Release und Debug); mit `--content-dir` 444 grün, es fallen nur die zehn Gruppentests der Suite (wie vorgesehen).
- **Simulationstests mit `--tick-rate 60` / `120`** (Release, mit Realdaten, ohne die zehn Gruppentests der Suite): vorher 6 / 14 Fehlschläge (`docs/TICKRATE-MIGRATION.md` §15.2),
  nachher **6 / 13**. Bei 60 Hz dieselben sechs wie vorher (Blitze der gestuften Übernahme, vier handgerechnete Geschossbahnen, der Rohwert der Turmdrehrate – keiner aus
  diesem Paket). Bei 120 Hz ist Nr. 9 der Liste aus §15.2 („Chinook: helicopter slow death …“) grün: der Fall landet jetzt bei jeder Rate auf `death + 1 + Frames(74)` (vorher
  75 / 149 / 298 Ticks). Zwei Tests dieses Bereichs hatten eine Erwartung in Ticks und sind in eigenen Test-Commits umgestellt (`1927883`: Wartezeit am Boden `hit + Frames(10)`;
  `eac35ab`: Tunnelscheibe je Bild `100 / 30`); beide sind bei 30, 60 und 120 Hz grün
- **Manifeste:** `input-manifest check` identisch und `reference-manifest check --set quick` 9 Läufe identisch an den Ständen `bbee8ab`, `60fcb57`, `bb00e5f`, `307bf14` (mit
  `234f3c3`), `8435ba8`, `c5a8a35`, `5954338`, `cee88c4`, `82dbdba` (mit `1927883`), `81931ab`, `0bf4e6c`, `fc0480f`. Nicht einzeln geprüft, vom jeweils nächsten Stand gedeckt:
  `6601ef1` (ein Name statt einer 1) und `e7a3153`; `9a43920` (keine Messung des Schritts bei 30 Hz) ist vom vollständigen Lauf gedeckt. `--set full`: **50 Läufe identisch**, Eingangsmanifest identisch, am Stand `c092a45` (nach dem letzten Commit an der Simulation, `9a43920`, und den beiden Test-Commits).
- **Build:** `dotnet build CnCNew.slnx` 0 Warnungen, 0 Fehler; `SourceRules` grün (die neue Datei `Time/FrameGrid.Flight.cs` und alle Änderungen eingeschlossen).
- **Probelauf bei 60 Hz mit Realdaten:** `SimTool ai-match … --map "Maps/Alpine Assault/Alpine Assault.map" --factions FactionAmericaAirForceGeneral,FactionChina --max-ticks 36000
  --tick-rate 60`: keine Ausnahme, zweiter Lauf tickgleich, Fortsetzung aus dem Snapshot bei Tick 18.000 identisch (Endhash `280602C8DA0A290D`, kein Sieger in 10 Spielminuten;
  im Lauf fliegen zwei Chinooks, die KI baut in dieser Zeit kein Flugfeld – Jets sind bei 60 Hz mit Realdaten nur über die Suite gefahren).
- **Suite gesamt am Endstand dieses Zweigs** (nur dieses Paket ist enthalten): außerhalb der Toleranz 458 bei 60 Hz und 606 bei 120 Hz (Ausgangsstand 479 / 625).
