# Raten-Invarianz, dritte Runde: Paket FLIGHT (Phase 23)

Stand: 8. Oktober 2026, Branch `worktree-agent-acaeab4437e5153cd`, Ausgangsstand `37b36b8` (`main`). Auftrag: die drei Flugfälle, die nur bei 120 Hz außerhalb der Toleranz lagen,
die Veröffentlichung des Bildrests der Flug- und Sonderbeweger, die Sentry-Drohne (`DeployStyleAIUpdate`) und der Fallschirmabwurf der Spezialkraft. Messlatte ist die
Raten-Äquivalenz-Suite (`docs/SIMULATION-CORE.md` §39) und je Ursache ein Drei-Raten-Test. „Bild“ heißt Logikbild des Originals (1/30 s), „Tick“ ein Schritt der Simulation,
r = Ticks je Bild (1, 2, 4). Werte stehen als 30 / 60 / 120 Hz. Die Konvention der Bilder steht in `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §1.3; dieses Dokument wendet sie an.
**Nichts davon hat ein Mensch gesehen.**

**Bei 30 Hz ist nichts anders:** `input-manifest check` identisch und `reference-manifest check --set quick` (9 Läufe) identisch nach jedem Commit, der die Simulation ändert;
`--set full` am Ende: 50 Läufe identisch (§7). Alle 4.307 Größen der Suite haben bei 30 Hz denselben Rohwert wie am Ausgangsstand. Kein Katalogtyp hat ein neues Feld, kein
Referenzwert wurde geschrieben, keine Toleranz und kein Fall der Suite wurde angefasst.

## 1. Ergebnis der Suite

Größen außerhalb der Toleranz bei 60 Hz / bei 120 Hz, alle zehn Gruppen, Release, Realdaten (`--only RateEquivalence --rate-report`).

| Gruppe | Größen | Ausgangsstand `37b36b8` | **Endstand `25a784e`** | Bemerkung |
|---|---|---|---|---|
| `locomotion` | 837 | 2 / 2 | 2 / 2 | Mob-Nexus (Paket OBSERVERS) |
| `flight` | 74 | 0 / 3 | **0 / 0** | 5 Größen des Fallschirmabwurfs bei jeder Rate ungemessen; mit dem zurückgehaltenen Commit (§6) 0 ungemessen |
| `weapons` | 1.474 | 0 / 0 | 0 / 0 | |
| `projectiles` | 743 | 4 / 4 | 4 / 4 | Streusalven (Paket OBSERVERS) |
| `turrets` | 216 | 4 / 0 | **0 / 0** | Sentry-Drohne |
| `overtime` | 418 | 0 / 0 | 0 / 0 | |
| `economy` | 81 | 0 / 0 | 0 / 0 | |
| `timed` | 383 | 1 / 1 | 1 / 1 | `CINE_BShipWeaponDetonationObject` (Paket DEFECT) |
| `scripts` | 15 | 0 / 0 | 0 / 0 | |
| `ai` | 66 | 24 / 23 | 24 / 23 | Paket OBSERVERS |
| **Summe** | **4.307** | **35 / 33** | **31 / 30** | keine Gruppe ist schlechter, keine Größe schlägt neu fehl |

## 2. Ursachen, je Zeile ein Commit

| # | Herkunft | Befund vorher (30 / 60 / 120 Hz) | Korrektur (Datei, Symbol, Prinzip) | nachher | Commit |
|---|---|---|---|---|---|
| 1 | §5.2 Flügelflug und `JetAIUpdate`; FLIGHT §5, CONSOLIDATION §6 „`flight`, nur 120 Hz“ | MiG „Landebefehl bis Aufsetzen“ 14,6333 / 14,6333 / 14,5667 s; Raptor-Angriff „geparkt und nachgeladen“ 60,8000 / 60,8000 / 60,5333 s. Die Schritte rechneten `Katalogwert je Tick × Ticks je Bild`; die Katalogwerte sind je Rate eigens gerundet (16 · a₁₂₀ ≠ a₃₀, relativ rund 10⁻⁵), `Entity.Speed` je Tick verliert Bits. Nach einigen hundert Bildern lagen die Lagen rund 0,01 Einheiten auseinander und eine Ankunftsschwelle kippte | `Phase5Modules.cs` nur `MoveWinged`/`SteerWinged` und neue Helfer daneben (`FlightFrameConstants`, `FlightVerticalConstants`, `FlightFrameSpeed`, `SetFlightFrameSpeed`, `TurnRadius(turnRate, speed)`); `JetAIUpdate.cs` (`TaxiAlongPath`, `TurnTo`, `TakeOff`, `Land`, `ControlHeight`), `JetAIUpdate.Flight.cs` (`CircleStep`); `Templates/LocomotorFrames.cs` um `SpeedLimitZ` und `Lift` erweitert (bestehende Werte unverändert), `ObjectTemplate.cs` übergibt die beiden Quellzahlen. Der Flug entscheidet mit den Konstanten je Bild des 30-Hz-Katalogs und führt sein Tempo je Bild im Modul (`_frameSpeed`, das die Bodenbewegung schon führt). Schwerkraft der Höhenregelung aus dem Definitionswert | MiG 14,6333 × 3; Raptor 60,8000 × 3; dazu exakt geworden: Raptor „erster Schuss“ 18,6333 / 18,6500 / 18,6333 → × 3, Aurora „Aufsetzen“ 15,8000 / 15,7667 / 15,8000 → × 3. `flight` 0 / 3 → 0 / 1 | `346cadc` |
| 2 | – (Messung der Suite) | Comanche „Kehre: umgekehrter Kurs bis Ankunft“ 13,3333 / 13,3667 / 13,3833 s bei **bitgleicher** Bahn (Spur: derselbe Rohwert von X am Ende jedes Bilds). `MeasureSortie` las den Kurs nach jedem Tick: der Kurs wird in Anteilen gedreht, die Schwelle wird mit dem ersten Anteil überschritten (bis zu ein Bild früh), die Zeit, die dort beginnt, endet mit einer Ankunft im letzten Tick eines Bilds: 2 · (r − 1) Ticks mehr als bei 30 Hz. „Produziert bis geparkt“ begann einen Tick nach dem Erscheinen, mitten im Bild | `RateCases.Flight.cs`, `MeasureSortie`: der Kurs wird am Bildende gelesen (`RateScene.StepUntilFrameEnd`, die Form von `f55c16d`); nach `Produce` ein `Align` (bei 30 Hz kein Schritt), wie seit `234f3c3` vor den anderen Befehlen dieses Falls. Keine Toleranz, keine Größe, kein 30-Hz-Wert geändert | 13,3333 / 13,3500 / 13,3583 s (der Rest ist die Ankunft im letzten Tick). `flight` 0 / 1 → 0 / 0 | `3a8d256` |
| 3 | neu | `FlightFrameStep` nimmt die Anteile der Drehung zurück und addiert sie einzeln, jedes Mal normalisiert. Die Summe stimmt, aber eine halbe Drehung hat zwei Darstellungen: Kurs der Landung bei 30 Hz Rohwert 52707180 (+π aus `Atan2`), über 30 Hz −52707177 | `Time/FrameGrid.Flight.cs`: der letzte Anteil setzt den Kurs der Entscheidung (wie `_frameAngle` der Bodenbewegung); im Snapshot nur während eines Bilds mit Drehung | Einsatz-Test (§4): erstes Bild, das von 30 Hz abweicht, 1154 / 1154 → hinter der Landung | `8be3a73` |
| 4 | §5.3 `Register` (erstes Update) für Flugzeuge; CONSOLIDATION §1.3 Regel 7, Ausnahme „Integratoren“ | `JetAIUpdate` lief zum ersten Mal einen **Tick** nach dem Erscheinen: ein im ersten Tick eines Bilds produziertes Flugzeug war für den Rest seines Lebens r − 1 Ticks vor seinem 30-Hz-Zustand. Ein geparktes Flugzeug sah jeden Tick nach: was es von selbst beginnt (gespeicherter Befehl nach dem Nachladen, Start des geheilten Hubschraubers), begann einen Tick statt ein Bild nach dem Zustand davor | `JetAIUpdate.cs`, `Update`/`BeginTakeoff` (nur über 30 Hz): erstes Update ein Bild nach dem Erscheinen; jede Entscheidung der Flugfeld-Zustände kostet ein Bild, auch der Leerlaufblick; ein Befehl an ein geparktes Flugzeug beendet dieses Leerlaufbild, der Start beginnt weiter im Tick des Befehls | Einsatz-Test: erstes abweichendes Bild Jet 30 / 30 → keines; Hubschrauber 30 / 30, danach 140 / 140 → keines. Suite: „produziert bis geparkt“ Raptor/Aurora 1,3667 / 1,3500 / 1,3417 → × 3, Comanche „Aufsetzen“ 12,7667 / 12,7833 / 12,7750 → × 3; die beiden Hubschrauber-Steigzeiten zeigen jetzt den Nachlauf einer Lage (1,6667 × 3 → 1,6667 / 1,6833 / 1,6917, innerhalb). `flight` 0 / 0 | `cc7fd75` |
| 5 | neu (Sentry-Drohne; TURRETS Befund 1, CONSOLIDATION §6 „`turrets`“) | Turmwinkel beim ersten Schuss 1,57080 / 1,55771 / 1,57080; „Opfer weg bis Turm verlässt sein Ziel“ 1,0000 / 0,0000 / 1,0250 s; 180° −3,14159 / 3,12850 / −3,14159. Ursache in §3 | `StructureModules.cs`, `DeployStyleAIUpdate.AllowsFire`/`Update`/`SetState` (nur über 30 Hz): Feuerversuche, die im Bild des Fertigwerdens **vor** dem Update der Einheit kommen, werden verworfen wie bei 30 Hz der Befehl desselben Ticks; wer nach dem Update fragt (eigener Zielerwerb), kommt durch | Suite `turrets` 4 / 0 → 0 / 0 (alle vier × 3). Erster Schuss nach dem ersten Befehl, Bilder: Nuke Cannon (4 Varianten) 101 / 100,5 / 100,25 → 101 × 3; Sentry-Drohne 34 / 33,5 / 34,25 → 34 × 3 | `c1a57ee` |
| 6 | neu | `JetAIUpdate.FlyOn` vergisst den Kreismittelpunkt, sobald das Flugzeug nicht kreist. Bei 30 Hz nennt ein Befehl, der im selben Schritt endet, den Punkt danach (`OnWingedArrival`); über 30 Hz liefen die Ticks, die den Rest des Ankunftsbilds fliegen, dieselbe Zeile noch einmal: ein Jet, der zur Landung zurückkehrt und am Anflugpunkt ankommt, kreiste um die Stelle, an der er gerade war | `JetAIUpdate.cs`, `FlyOn`: der Mittelpunkt wird nur bei einer Entscheidung vergessen (bei 30 Hz jeder Tick) | zweiter Einsatz des Tests (Heimkehr nach der Leerlaufzeit): erstes abweichendes Bild 782 / 782 (0,34 Einheiten, 0,14 rad, danach ein anderer Gleitpfad) → keines. Suite unverändert (ihre Einsätze bekommen den Landebefehl, während sie kreisen) | `fd96aff` |
| 7 | CONSOLIDATION §6 „nicht veröffentlichende Beweger“ | Reichweitenfragen, Schüsse und gelenkte Geschosse lasen ein Flugzeug einen Teil seines Bildschritts von der Stelle entfernt, an der es bei 30 Hz steht: Jet bis 3,75 / 5,62 Einheiten, Hubschrauber 2,13 / 3,20, Chinook 3,30 / 4,95, Fallschirm 1,12 / 1,68, abgeschossener Jet 2,18 / 3,27, abgeschossener Hubschrauber 2,50 / 3,75, Spectre 4,00 / 6,00, Leuchtkörper 3,89 / 5,84 (60 / 120 Hz) | §5: `FlightFrameStep.Publish` und die Module, die einen Schritt besitzen (Regel 3) | Abstand zwischen Lesung und Bildende 0 / 0 bei allen neun Bewegern, Lesung gleich der 30-Hz-Lage (0 / 0). Suite: alle Gruppen unverändert | `e06259f` |

Tests je Ursache: `813125d` (#1, #3, #4), `00db28e` (#5), `e6bf1d1` (#6), `25a784e` (#7); §4.

## 3. Die Sentry-Drohne: was es war, und warum nur bei 60 Hz

Der frühere Befund „Status bei 60 Hz in Tick 65 statt 64“ gilt am Ausgangsstand nicht mehr: das Statusbit `DEPLOYED` erscheint in Tick 32 / 64 / 128 (Protokoll von `SetState`).
Die Ursache liegt einen Schritt dahinter.

- Eine Einheit mit `DeployStyleAIUpdate` wird **in ihrem Update** feuerbereit. Bei 30 Hz kam der Feuerbefehl desselben Ticks vor diesem Update und wird verworfen
  (`WeaponNotReady`); der nächste Befehl, ein Bild später (Tick 33), passiert das Tor und gibt dem Turm seinen Auftrag.
- Über 30 Hz kommen Befehle in jedem Tick. Der Befehl des nächsten **Ticks** passierte (65 statt 66, 129 statt 132): der Turm begann r − 1 Ticks zu früh und neben dem Bildraster
  der Einheit, der Schuss fiel ein halbes bzw. drei Viertel Bild früh. Das ist die Art „einen Schritt später“ aus dem Prinzip: bei 30 Hz ein Bild, also bei jeder Rate ein Bild.
- **Warum die Suite nur bei 60 Hz fehlschlug:** der frühe Beginn bestand bei beiden Raten. Die Suite liest den Turmwinkel an der nächsten Bildgrenze der **Welt** nach dem Schuss.
  Ein Turm, der in den Ticks 4k + 1 entscheidet, hat dort (drei Ticks später) sein ganzes Bild gedreht: bei 120 Hz stand der 30-Hz-Winkel da, und die Zeiten blieben mit ¾ Bild
  innerhalb der Toleranz von einem Bild. Ein Turm, der in den Ticks 2k + 1 entscheidet, wird unmittelbar nach seiner Entscheidung gelesen, nach dem ersten von zwei Anteilen:
  1,55771 statt 1,57080 rad (ein halber Bildschritt von 1,5°). Die „Rückkehr“-Größen des Falls beginnen bei diesem Winkel und brachen mit ihm (0,0000 s: der Turm „verlässt“
  einen Winkel, den er noch gar nicht erreicht hatte).
- **Korrektur:** über 30 Hz merkt sich das Modul den ersten Tick des Bilds nach dem Fertigwerden und, solange dieses Bild läuft, den Tick seines letzten Updates. Ein Feuerversuch
  in diesem Bild, der vor dem Update des Ticks kommt (ein Befehl), wird verworfen; einer danach (der eigene Zielerwerb der Einheit, Phase nach den Updates) kommt durch, wie bei
  30 Hz im Tick des Updates. Zwei Zustandsfelder, nur während dieses einen Bilds geschrieben, nie bei 30 Hz.
- **Die anderen Benutzer** (Realdaten-Test `RealDeployStyleTimesAreTheSame`): in den Daten tragen genau zehn Objekte `DeployStyleAIUpdate` – vier Nuke Cannons und sechs
  Sentry-Drohnen; Artillerie-Plattformen und Tomahawk tragen es nicht. Fünf haben eine Waffe im Standard-Waffensatz und sind gemessen (die vier Nuke Cannons und die
  Sentry-Drohne der Mission); die fünf übrigen Drohnen bekommen ihr Gewehr erst mit dem Upgrade und sind **nicht** gemessen (dasselbe Modul mit denselben Zeiten). „Feuerbereit“
  und „gepackt“ waren schon vorher bei jeder Rate gleich (Nuke Cannon 100 × 3 und 100 × 3 Bilder, Drohne 30 × 3 und 30 × 3), der erste Schuss nicht (Tabelle #5).

## 4. Die Drei-Raten-Tests

Alle nennen ihre Raten selbst und laufen ohne Realdaten, außer dem genannten.

| Test (Datei) | Was verglichen wird | Ergebnis |
|---|---|---|
| `RateFlightJetSortieIsBitIdentical` (`Tests.RateInvariance.Round3Flight.cs`) | Rohwerte von Lage, Höhe, Kurs, Tempo je Bild, Flugfeld-Zustand, Befehlsflag und Magazin nach **jedem** von 1.500 Bildern. Einsatz 1: Produktion, Rollen aus dem Hangar, Parken, Befehl (Rollen, Pause, Startlauf, Steigen, Flug, Kreisen), zweiter Befehl im Flug (Kehre), Verfolgung und Feuer bis das Magazin leer ist, Heimkehr, Anflug, Landung, Ausrollen, Rollen, Drehen, Nachladen. Einsatz 2: ohne Angriff, Heimkehr nach der Leerlaufzeit | erstes Bild, das von 30 Hz abweicht: keines / keines, in beiden Einsätzen |
| `RateFlightHelicopterIsBitIdentical` (dieselbe Datei) | dasselbe für einen Hubschrauber eines Helipads über 900 Bilder: Produktion, Abheben, Steigen, Flug, Kehre im Flug, Heimkehr, Landung, Start des geheilten Hubschraubers (acht Umläufe) | keines / keines |
| `RateDeployStyleIsBitIdentical` (`Tests.RateInvariance.Round3Deploy.cs`) | Zustand, `DEPLOYED`, Turmwinkel, Gesundheit des Opfers, Lage nach jedem von 260 Bildern unter einem Feuerbefehl **in jedem Tick**: Aufstellen, Turm eine Vierteldrehung, Feuer, Packen auf einen Fahrbefehl, Fahrt, erneutes Aufstellen | Zustände `U@0 A@30 P@100 M@115 U@160 A@190`, 41 Schüsse bei jeder Rate (vorher 42 bei 60 Hz) |
| `RealDeployStyleTimesAreTheSame` (dieselbe Datei, Realdaten) | „feuerbereit“, erster Schuss, „gepackt“ in Ticks Spielzeit für jedes Retail-Objekt mit `DeployStyleAIUpdate` | gleich bei jeder Rate (§3) |
| `RateFlightObserversReadTheFrameEnd` (`Tests.RateInvariance.Round3FlightObservers.cs`) | nach jedem Tick: was ein Beobachter liest (`Entity.FrameEnd`) gegen die Lage am Ende des geflogenen Bilds und gegen die 30-Hz-Lage; neun Beweger (§5) | bei 30 Hz nichts veröffentlicht; über 30 Hz Abstand 0 / 0 |

Drei bestehende Szenen stellten sich auf „einen Tick nach dem Erscheinen“ ein und nehmen jetzt dieses Bild (bei 30 Hz dieselben Ticks; im Commit `cc7fd75`): `WingWorld`
(`Tests.JetFlight.cs`), die Flügelbahn und die Snapshot-Szene `wingStep` (`Tests.RateInvariance.Flight.cs`). Die Ausnahme der Quelltextregel „kein Wert mit sich selbst
multipliziert“ für den Bremsweg des Flügelflugs nennt den neuen Ausdruck (`Tests.SquareSourceRules.cs`, im Commit `346cadc`).

## 5. Wer seinen Bildrest veröffentlicht

Über 30 Hz, einmal je Tick, absolut (ein fremder Rest am Objekt überlebt die Veröffentlichung nicht); bei 30 Hz wird nichts geschrieben.

| Beweger | Was veröffentlicht wird | Stelle |
|---|---|---|
| Jet (Rollen, Startlauf, Landung, Höhe, Flügelflug, Kreisen) | Rest des Zustandsschritts (`_frame`) + Rest des Flügelschritts (`WingedStepRest`) | `JetAIUpdate.FlyOn` |
| Hubschrauber (`JetAIUpdate` ohne Flügel) | Rest des Zustandsschritts (Höhe) + Rest des Bewegungsschritts (`GroundStepRest`, nur lesend) | `JetAIUpdate.FlyOn` |
| Chinook | Rest des Höhenschritts (`_climb`) + Rest des Bewegungsschritts | `ChinookAIUpdate.Update` |
| Fallschirm und sein Reiter | Rest des Abstiegs (`_descent`); der Reiter bekommt denselben Rest, beim Landen keinen | `ParachuteContain.Update`/`Carry`/`Land` |
| abgeschossener Jet, abgeschossener Hubschrauber | Rest des Falls (`_fall`, `_spiral`); mit dem Beginn des Falls verfällt, was die KI des Flugzeugs von einem Bild übrig hatte | `JetSlowDeathBehavior`, `HelicopterSlowDeathBehavior` |
| Spectre | Rest des Flugschritts (`_flight`) | `SpectreGunshipUpdate.Update` |
| Leuchtkörper | verbleibende Ticks seines Bilds mal das Tempo, das er für dieses Bild behält; Sinken bis höchstens zum Boden | `CountermeasuresBehavior.MoveFlares` |
| abgeworfene Fracht, Frachtflugzeug | der Flug von `DeliverPayloadAIUpdate` ist die Luftbewegung von `AIUpdateInterface.Move` und veröffentlichte schon; die Fracht fällt am Fallschirm (oben) | – |

Ein Schritt, dessen veröffentlichter Rest weg ist, wurde von außen vollendet (`Entity.CompleteFrame`: eine verfolgende Einheit kam in Reichweite, Tod durch Höhe): er fliegt
von diesem Bild nichts mehr (`FlightFrameStep.Continue`), wie der Bodenschritt. **Folge:** ein verfolgendes Flugzeug, das in Reichweite kommt, steht wie eine Bodeneinheit sofort
am Bildende und fliegt die übrigen r − 1 Ticks dieses einen Bilds nicht (vorher hatte es keinen veröffentlichten Rest und wurde nicht vollendet).

## 6. Fallschirmabwurf der Spezialkraft: zurückgehaltener Commit

„Befehl bis Flugzeug weg“ liegt innerhalb der Toleranz: 49,2667 / 49,2500 / 49,2417 s (Ausgangsstand und Endstand). Fünf Größen sind bei jeder Rate ungemessen, weil der Abwurf
bei jeder Rate niemanden absetzt – ein **30-Hz-Fehler** (FLIGHT §6): (a) `SimulationWorld.ExecuteDeliverPayload` reiht die Fracht vor ihrem Fallschirm ein; (b) `SpawnEntity`
fragt das Frachtflugzeug mit `IsValidContainerFor`, ob es den Fallschirm nimmt (`AllowInsideKindOf` nennt `PARACHUTE` nicht), das Original (`DeliverPayloadNugget::create`,
`ObjectCreationList.cpp`) ruft `addToContain` ohne diese Frage.

Die Korrektur ändert das Verhalten bei 30 Hz und ist deshalb **der letzte Commit des Zweigs, markiert „HELD BACK — changes 30 Hz“**, getrennt zu mergen: der Fallschirm wird vor
seiner Fracht eingereiht (`SimulationWorld.CreationLists.cs`), und ein Transporter mit `DeliverPayloadAIUpdate` nimmt, was ihm die Lieferung zuweist, ohne Frage
(`SimulationWorld.cs`, `SpawnEntity`). Gemessen mit diesem Commit:

| Größe (Fall `flight/paradrop SuperweaponParadropAmerica`) | vorher | nachher |
|---|---|---|
| Befehl bis erster Abwurf | – / – / – | 23,2667 / 23,2500 / 23,2417 s |
| erster bis letzter Abwurf | – / – / – | 0,8000 × 3 |
| Befehl bis erste Landung | – / – / – | 28,3667 / 28,3500 / 28,3417 s |
| Befehl bis letzte Landung | – / – / – | 29,1667 / 29,1500 / 29,1417 s |
| gelandete Soldaten | 0 / 0 / 0 | 5 / 5 / 5 |
| mittlerer Abstand der Landung vom Ziel | – / – / – | 0,000 × 3 |
| Befehl bis Flugzeug weg | 49,2667 / 49,2500 / 49,2417 s | 49,6333 / 49,6333 / 49,6250 s |

- Suite mit dem Commit: `flight` 0 / 0 mit **0** ungemessenen Größen (vorher 5); alle anderen Gruppen wie am Endstand (Summe 31 / 30).
- `reference-manifest check --set full` mit dem Commit: **50 Läufe identisch** – kein Lauf des Manifests benutzt eine Lieferung mit Behälter. Das Manifest muss für diesen Commit
  nicht neu geschrieben werden (es wurde nicht geschrieben). `input-manifest check` identisch.
- Simulationstests mit dem Commit, Release: bei 30, 60 und 120 Hz alle grün (ohne Realdaten); mit Realdaten bei 30 Hz fallen nur die vier Gruppentests der Suite, die auch
  ohne ihn fallen.
- Nicht geprüft: Abwürfe in Missionen und Gefechten außerhalb des Manifests (Zielfahrer, `CampaignTool`); der skriptgesteuerte Abwurf (`ScriptEngine.Reinforcements.cs`) geht
  einen anderen Weg und ist nicht berührt. Auffällig, nicht untersucht: alle fünf Ranger landen exakt auf dem Zielpunkt (Abstand 0,000).

## 7. Prüfläufe

| Lauf | Ergebnis |
|---|---|
| `input-manifest check`, `reference-manifest check --set quick --jobs 3` | identisch bzw. 9 Läufe identisch an den Ständen `346cadc`, `8be3a73`, `cc7fd75`, `c1a57ee`, `fd96aff`, `e06259f` (jeder Commit, der die Simulation ändert, mit frisch gebautem `SimTool`) |
| `reference-manifest check --set full --jobs 4`, `input-manifest check` | **50 Läufe identisch**, Eingangsmanifest identisch, am Endstand `25a784e` |
| Suite gesamt (4.307 Größen) | §1; TSV im Scratch-Ordner `round3-flight\final-suite.tsv`; alle 30-Hz-Rohwerte gleich denen des Ausgangsstands |
| Simulationstests, Release, ohne Realdaten | 30 Hz, `--tick-rate 60`, `--tick-rate 120`: jeweils alle 499 grün (am Ausgangsstand 495, ebenfalls alle grün) |
| Simulationstests, Release, mit Realdaten, 30 Hz | 522 grün; rot nur die vier Gruppentests der Suite (`locomotion`, `projectiles`, `timed`, `ai`) |
| Simulationstests, Debug, 30 Hz (`dotnet run --project tests/CnCNew.Simulation.Tests`) | alle grün |
| `dotnet build CnCNew.slnx` | 0 Warnungen, 0 Fehler (inkrementell, `bin`/`obj` nicht gelöscht) |

## 8. Ausweg „Originalraster“ und was ihm nahekommt

Kein Vorgang läuft neu „als Ganzes nur jeden r-ten Tick der Welt“. Drei Stellen legen etwas auf das Bild des Objekts; sie stehen hier, damit sie beurteilt werden können:

| Stelle | Was einmal je Bild geschieht | Raster | Grund |
|---|---|---|---|
| `JetAIUpdate.Update`, geparktes Flugzeug ohne Auftrag | der Leerlaufblick (Heilung melden, gespeicherten Befehl ausführen, Rückkehrzeit, Start des geheilten Hubschraubers) | das des Flugzeugs | Zustandsautomat des Originals, ein Schritt je Bild; „einen Schritt später“ ist ein Bild. **Befehlsannahme bleibt je Tick:** ein Befehl beendet das Leerlaufbild, der Start beginnt im Tick des Befehls (Test: Zustand `TaxiToTakeoff` im Bild des Befehls bei jeder Rate) |
| `JetAIUpdate.Update`, neues Flugzeug | das erste Update kommt ein Bild nach dem Erscheinen | Tick des Erscheinens | Regel 7 der Konvention; bei 30 Hz der nächste Tick |
| `DeployStyleAIUpdate.AllowsFire` | im Bild des Fertigwerdens (r − 1 Ticks) werden Feuerversuche, die vor dem Update der Einheit kommen, verworfen | Tick des Fertigwerdens | §3. Der Befehl wird angenommen und ausgeführt wie in jedem Tick; das Tor antwortet in diesen Ticks, was es bei 30 Hz dem Befehl des Fertig-Ticks antwortet. Bewegung und Zielerwerb sind nicht berührt (der eigene Zielerwerb passiert das Tor ab dem Tick des Updates) |

## 9. Offen, bewusst gelassen, neue Befunde

- **Nicht auf die 30-Hz-Konstanten umgestellt:** Spectre (`Fly`), Chinook-Höhenschritt, Fallschirm, Leuchtkörper, Partikelstrahl und der abgeschossene Jet rechnen weiter
  `Katalogwert je Tick × r` (`FlightFrames.PerFrame`). Ihre Drei-Raten-Tests zeigen Abstand 0 / 0 mit den Zahlen der Fixture, die Suite hat dort keinen Fehlschlag; an einer
  Schwelle kann es wie in #1 ein Bild kosten. Gleiches Muster wie #1 (`LocomotorFrames`), nicht gemacht.
- **Regel 7 nur für `JetAIUpdate`:** Chinook, Frachtflugzeug und die übrigen von `AIUpdateInterface` abgeleiteten Flieger beginnen weiter einen Tick nach dem Erscheinen
  (die Ausnahme der Konvention). Ein Chinook ohne Sammelpunkt vollendet sein Bild deshalb weiter im ersten statt im letzten Tick eines Bilds der Welt.
- **Nicht veröffentlichend geblieben** (nicht in den Dateien dieses Pakets): Zug, Lawine, Fähre, Welle, freier Fall von `PhysicsBehavior`. In den Dateien dieses Pakets: das
  Gattling des Spectre (es wird auf die Lage des Schiffs vor dessen Schritt gesetzt, bei jeder Rate einen Schritt hinter dem Schiff) und der Partikelstrahl (kein Objekt).
- **Der Kurs am Bildende** wird weiterhin nicht veröffentlicht (`Entity.FrameEnd` kennt nur die Lage).
- **Zwei Schritte eines Objekts mit verschiedener Phase** (ein Hubschrauber, der mitten in einem Bild seines Zustandsautomaten einen Fahrbefehl bekommt): veröffentlicht wird
  die Summe beider Reste. Kein Test dafür; alle Szenen der Tests geben Befehle an Bildgrenzen.
- **Messung „Steigen auf Flughöhe“** der Hubschrauber liegt jetzt (r − 1) Ticks hinter 30 Hz (innerhalb): eine Schwelle auf einer Lage, die mit dem letzten Tick erreicht wird.
- **Leistung:** nicht ruhig gemessen. Ein direkter Wechsel der Binärverzeichnisse von Ausgangs- und Endstand (`SimTool perf`, Twilight Flame, 8 Spieler, 9.000 Ticks, 30 Hz,
  je zwei Läufe, Mittel ohne die ersten 300 Ticks): 2.190,6 und 2.186,0 µs gegen 2.141,2 und 1.606,5 µs – kein Anstieg erkennbar, die Streuung ist größer als jeder Unterschied.
  Bei 30 Hz kommen im Flügelflug und in den Flugfeld-Zuständen je Entscheidung ein Aufruf mit Vergleich (`_stride == 1`) dazu, keine
  Division; über 30 Hz je Flugzeug und Tick eine Veröffentlichung (drei Additionen) und ein Tabellenzugriff, wenn der Locomotor wechselt (Rollen gegen Flug).
- **Befund (TURRETS 1 überholt):** `DeployStyleAIUpdate` setzt seinen Status rechtzeitig; die Abweichung war der Befehl danach (§3).
- **Befund:** dieselbe Art „das Update macht etwas möglich, der nächste Befehl nutzt es“ gibt es vermutlich an weiteren Toren, die ein Befehl je Tick abfragt und ein Update
  öffnet (nicht durchgesehen; `IFireGate` hat nur diesen einen Implementierer).
- **Befund:** Raptor und MiG flogen am Ausgangsstand mit einem Reisetempo, dessen Rohwert je Sekunde sich zwischen den Raten unterschied (Raptor 2936012790 / 2936012820 /
  2936012760); die Suite zeigt es mit drei Nachkommastellen als 175,000 × 3. Am Endstand unterscheiden sich nur noch die abgeschnittenen Bits von `Entity.Speed` je Tick
  (… 790 / … 760 / … 760), gerechnet wird mit dem Tempo je Bild.
- **Nicht gemacht oder nicht geprüft:** Jets über ein echtes Gefecht bei 60 Hz; Flugfelder mit Modellknochen; mehrere Jets an einer Startbahn bei 60/120 Hz; ein Snapshot mitten
  im Bild des Fertigwerdens einer aufgestellten Einheit (die beiden Felder werden geschrieben und gelesen, ein eigener Test fehlt); Zielfahrer und `CampaignTool`; Debug-Lauf
  mit Realdaten; Präsentation (`Presentation.Tests`, `Game.Tests`).
