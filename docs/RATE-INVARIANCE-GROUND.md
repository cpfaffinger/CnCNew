# Raten-Invarianz der Bodenbewegung (Phase 23, Paket GROUND)

Stand 8. Oktober 2026, Ausgangsstand `7b54374`. Dieses Dokument gehört zu Schritt 3 der Phase 23 (`docs/HANDOFF-PHASE23.md`): es beschreibt, wie die Bodenbewegung
(`AIUpdateInterface.Move`/`MoveStraight`, `SimulationWorld.Collision.cs`) bei 60 und 120 Hz dasselbe Spiel wie bei 30 Hz wird, was gemessen wurde und was offen bleibt.
Bei 30 Hz ändert sich nichts: Eingangsmanifest und Referenzmanifest sind nach jedem Commit identisch (§7). Gemessen ist mit der Raten-Äquivalenz-Suite
(`docs/SIMULATION-CORE.md` §39) und mit eigenen Drei-Raten-Tests auf der synthetischen Fixture (`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Ground.cs`).
Kein Mensch hat eine Fahrt bei 60 Hz gesehen.

## 1. Prinzip

Der 30-Hz-Schritt ist die Definition. Oberhalb von 30 Hz wird er **unterteilt**, nicht mit kleinerer Schrittweite neu integriert:

- **Eine Entscheidung je Bild des Originals.** Wegplanung, Wegpunktwechsel, Drehung, Geschwindigkeit unter Beschleunigung und Bremsen, Neuplanung um stehende Einheiten, lokales
  Ausweichen und der Weg des Bildes werden einmal je Bild berechnet – mit den Größen *je Bild* (Weg eines Bildes bei Höchsttempo, Tempoänderung je Bild, Drehung je Bild). Der
  Ankunftsradius, die Wegpunkt-Reichweite, der Bremsweg `v²/(2a)`, die Schwelle „im Wendekreis“ und der Ausweichschritt sind damit bei jeder Rate die des 30-Hz-Schritts.
- **Ein Anteil je Tick.** Den Weg des Bildes legt das Objekt in gleichen Anteilen zurück, einen je Tick; Anteil *i* von *n* ist die Differenz der Rohwert-Teile *i/n* und *(i − 1)/n*
  des Schritts, die Anteile summieren sich also **exakt** zum 30-Hz-Schritt. Der Kurs dreht in denselben Ticks auf den Kurs der Entscheidung und steht mit dem letzten Anteil exakt
  darauf. Zwischen zwei Bildgrenzen ändern sich nur Lage, Höhe über Grund und Kurs.
- **Dieselben Bits.** Ein Katalog für 60 oder 120 Hz trägt seine Größen je Tick, jede einmal für diese Rate gerundet; zurückmultipliziert verfehlen sie die 30-Hz-Werte um ein, zwei
  Stufen der Auflösung, und ein Vergleich, der in den Daten ein exakter Gleichstand ist (Restweg gegen Bremsweg 110,000), fällt dann bei jeder Rate anders. Deshalb gibt der
  Katalogbau die 30-Hz-Umrechnung derselben Quellzahlen an `LocomotorFrames` (`src/CnCNew.Simulation/Templates/LocomotorFrames.cs`): eine statische Tabelle, Schlüssel ist die
  `LocomotorInfo`-Instanz des Katalogs – kein Feld eines Katalogtyps (das Eingangsmanifest bleibt gleich), kein Zustand, nicht im Hash. Das Modul führt das Tempo je Bild selbst
  (`_frameSpeed`); `Entity.Speed` bleibt die Strecke je Tick (Tempo je Bild geteilt durch die Ticks je Bild) und gilt ab dem ersten Anteil des Bildes.
- **Folge:** ein einzelnes Objekt steht nach jedem Bild des Originals **bitgleich** dort, wo der 30-Hz-Schritt es hinstellt – bei 60 und bei 120 Hz, mit und ohne Gelände, auch mit
  dem Locomotor des beschädigten Zustands (Test `GroundRateInvarianceCourse`; in der Suite: die Weglänge des Quadratkurses 2000,114 bei jeder Rate).
- **Die Ankunft ist ein Bild wie jedes andere.** Bei 30 Hz setzt der Schritt, der das Objekt im Ankunftsradius findet, es auf das Ziel und beendet den Befehl. Oberhalb von 30 Hz
  legt das Objekt den Restweg in den Anteilen dieses Bildes zurück; der Befehl endet (mit `MoveArrived`, Tempo 0) im Tick des **letzten** Anteils. Der Zustand nach dem Bild ist der
  Zustand nach dem 30-Hz-Schritt, und ein Befehl, der auf die Ankunft folgt, beginnt im selben Bild wie bei 30 Hz. Gemessen wird die Ankunft dadurch (Zeitkonvention der Suite:
  „Tick des Ereignisses weniger Tick des Befehls“) ein halbes Bild (60 Hz) bzw. drei Viertel Bild (120 Hz) später als bei 30 Hz – einmal je Fahrt, nicht je Abschnitt, innerhalb
  der Toleranz von einem Bild.
- **Die Phase gehört dem Objekt.** Das erste Bild einer Fahrt beginnt in dem Tick, in dem das stehende Objekt den Befehl ausführt – ein Befehl wirkt im nächsten Tick, bei jeder Rate.
  Ein Befehl, der ein **fahrendes** Objekt mitten in einem Bild erreicht, wird sofort angenommen (Ziel gesetzt, Weg verworfen, eine anstehende Ankunft aufgehoben); das Objekt
  legt die restlichen Anteile des laufenden Bildes zurück (höchstens ein Tick bei 60 Hz, drei bei 120 Hz) und entscheidet mit seinem nächsten Bild über das neue Ziel. Tempo und
  Kurs ändern sich also weiter nur einmal je Bild; Lage und Kurs bleiben stetig (Test `GroundRateInvarianceOrderInsideAFrame`).
- **Ein Anteil, der nicht fällig wird, verfällt.** Wird das Objekt im Tick eines Anteils nicht bewegt (EMP, festgehalten, gelähmt, ein anderer Zustand einer abgeleiteten KI) oder
  hält es an (`StopInternalMove`), ist der Rest des Schritts nichtig (`_frameDue`).
- **Zustand.** Neu im Snapshot und im Hash, nur oberhalb von 30 Hz geschrieben: `frameSpeed` (wenn ungleich 0) sowie `frameTicksLeft`, `frameDue`, `frameStepX`, `frameStepY`,
  `frameAngle`, `frameArriving` (nur zwischen zwei Entscheidungen). Bei 30 Hz wird keines dieser Felder je gesetzt; Snapshots und Hashes sind dort bytegleich. Ein Snapshot mitten
  in einem Bild setzt bitgleich fort (Test `GroundRateInvarianceSnapshotInsideAFrame`).
- **Kosten.** Bei 30 Hz derselbe Pfad mit denselben Ausdrücken (§8). Oberhalb von 30 Hz kostet nur der Tick der Entscheidung, was der 30-Hz-Schritt kostet; die übrigen Ticks eines
  Bildes sind drei Additionen und eine Geländehöhe.

## 2. Ursachen

Werte: 30 / 60 / 120 Hz. „Suite“ meint die Gruppe `locomotion` (54 Fälle, 837 Größen), sofern nichts anderes dasteht.

| # | §5-Eintrag (`docs/TICKRATE-MIGRATION.md`) | Befund (vorher) | Korrektur (Datei, Symbol, Prinzip) | nachher | Commit |
|---|---|---|---|---|---|
| 1 | §5.2 `AIUpdateInterface.Move`, `MoveStraight` (Beschleunigung, Bremsweg, Positionsschritt) | Chinook, 1.500 Einheiten: Bremsweg 110,000 / 111,250 / 111,875; Ankunft 11,7000 / 11,7833 / 11,8417 s; Quadratkurs 21,1333 / 21,4667 / 21,7083 s | `Phase5Modules.cs`, `Move`/`MoveStraight`/`FrameConstants`/`FrameSpeed`/`BeginFrame`/`AdvanceFrame`; `Templates/LocomotorFrames.cs`; eine Zeile in `ObjectTemplate.cs`. Tempo ändert sich einmal je Bild wie bei 30 Hz, Lage je Tick um einen Anteil | Bremsweg 110,000 bei jeder Rate; Ankunft 11,7000 / 11,7167 / 11,7250 s; Kurs 21,1333 / 21,1500 / 21,1583 s; Lage nach jedem Bild bitgleich | `bf028df` |
| 2 | §5.2 `Move` (Drehung, Wendekreis) | Zeit für 90°/180° lag schon innerhalb eines Bilds; Dreh-Schwelle (`difference > turnRate`) und „im Wendekreis anhalten“ (`distance < Speed / turnRate · 2`) hingen am Tickschritt | wie 1: Drehung je Bild entschieden, Kurs je Tick anteilig, am Bildende exakt | Kurs nach jedem Bild bitgleich; 90°: 0,4667 / 0,4833 / 0,4917 s (Chinook, die Messung trifft die Schwelle mitten im Bild) | `bf028df` |
| 3 | §5.2 `LocomotorInfo.CloseEnoughDist` (0 = ein Tick Weg), Wegpunkt-Reichweite, Einrasten, `MoveArrived` | Ankunftsradius und Wegpunkt-Reichweite ein Tick Weg (halbiert, geviertelt) | wie 1: `max(CloseEnoughDist, Weg eines Bildes)`; Ankunft als Bild (`BeginArrival`), Ende im Tick des letzten Anteils | Ankunftsfehler 0 bei jeder Rate; jede Ankunft im selben Bild wie bei 30 Hz | `bf028df` |
| 4 | §5.2 `AvoidLocalBlockers`, `StepBlocked`, `StationaryBlockersAhead` | Ausweichschritt ein Tick Weg; Vorausschau schon 25 Bilder Weg (Phase 20), aber aus dem Tempo je Tick | wie 1: `AvoidLocalBlockers` bekommt den Schritt des Bildes, die Vorausschau ist 25-mal der Weg des Bildes (`BlockerLookAhead`), geprüft und neu geplant wird im Tick der Entscheidung | Umweg um stehende Einheiten bitgleich zum 30-Hz-Umweg (Test `GroundRateInvarianceStandingUnits`); in der Suite nicht gestellt | `bf028df` |
| 5 | – (Messung) | „Strecke bis Höchsttempo“, „Bremsweg“, „Bremszeit“: die Suite las das Tempo je Tick und nahm den ersten/letzten Tick bei Höchsttempo – mitten im Bild, wo kein 30-Hz-Messpunkt liegt; und `Entity.Speed` je Tick kann oberhalb von 30 Hz das Höchsttempo nicht von einer Stufe darunter unterscheiden. Nach #1: 41 / 63 Größen außerhalb | `Testing/RateEquivalence/RateCases.Locomotion.cs`, `Track.FirstAtTopSpeed`/`LastAtTopSpeed`: am Bildende abgetastet (wie die Weglängen), aus dem Tempo je Bild (`AIUpdateInterface.SpeedPerOriginalFrame`, Lesezugriff). Keine Toleranz berührt, kein 30-Hz-Wert der Gruppe ändert sich | 12 / 12; Chinook 195,000 bei jeder Rate (vorher 195,000 / 192,500 / 191,250) | `fbe8d9a`, `466d85f` |
| 6 | §5.3 (Klasse „ein Tick statt ein Bild“; keine eigene Zeile): Dock-Automat und Versorgungs-Automat | je Zustandswechsel ein Update: ein Sammler gewann je Umlauf rund zehn halbe bzw. Dreiviertel-Bilder; erste Lieferung des Lasters 22,3000 / 22,2333 / 22,2000 s (nach #1) | `Phase5Modules.cs`, `AIUpdateInterface.Update` (Dock-Automat; während `ProcessDock` weiter je Tick); `SupplyModules.cs`, `SupplyCarrierAI.Update` (eine Zeile, außerhalb der Dateiliste des Pakets). Ausweg „Originalraster“, §3 | erste Lieferung 22,3000 bei jeder Rate; ein Sammler **allein** zwischen Lager und Zentrum: Lage, Befehlszustand und Geld nach jedem Bild über 9.000 Bilder gleich (Laster, Arbeiter, Chinook); die übrigen Größen der Suite scheitern am zweiten Sammler (§5) | `096160d` |
| 7 | §5.2 `ResolveFootprintCollisions`, `TrySquish` | Überlappung je Positionsänderung geprüft: bis zu drei Viertel eines Bildschritts früher erkannt, andere Reihenfolge der Kollisionsereignisse | `Phase5Modules.cs`, `AdvanceFrame`: Prüfung einmal je Bild, mit dem letzten Anteil und dem ganzen Weg des Bildes. Ausweg „Originalraster“, §3 | Streumine „Befehl bis Mine weg“ 4,8000 / 4,7000 / 4,6417 → 4,8000 / 4,8167 / 4,8250 s; Fixture: Quetschen und Stoß im selben Bild | `1c65446` |
| 8 | §5.2 `TryTopple` (`mover.Speed`), `Entity.Speed` | Kippkraft = Strecke je Tick: halbe und Viertel-Kraft | `SimulationWorld.Collision.cs`, `TryTopple`: Tempo je Bild (`SpeedPerOriginalFrame`) | `ApplyTopplingForce` bekommt bei jeder Rate den 30-Hz-Wert. Die Falldauer ist damit **noch nicht** gleich (1,2000 / 0,6500 / 0,3250 s, vorher 1,2000 / 0,8000 / 0,5667 s): `ToppleUpdate` addiert sein Winkeltempo weiter je Tick (§5) | `1c65446` |
| 9 | – (Folge von #1): Bauauftrag und Zielobjekt | Reichweitenprüfung des Bauarbeiters und Nachführen auf ein wanderndes Zielobjekt liefen auch mitten im Bild, auf einer Lage, die es bei 30 Hz nicht gibt | `Phase5Modules.cs`, `UpdateTask`, `UpdateGoal`: geprüft wird, wenn der Schritt des Bildes zurückgelegt ist; `StopInternalMove` lässt den Rest des Schritts verfallen | Baubeginn im selben Bild wie bei 30 Hz. Suite `economy/build`, „Baubeginn bis fertig“: 15,2333 / 15,2500 / 15,2583 s (vorher 15,2333 / 15,2333 / 15,2417 s) – weiter weg, und die ehrliche Zahl: die Bauzeit selbst dauert „N Bilder weniger ein Tick“ (Befund des ersten Suite-Laufs, nicht Bodenbewegung), und der zu frühe Halt des Dozers hat davon bisher ein bis drei Ticks verdeckt | `6e018fe` |

Die beschädigten Locomotoren (`SpeedDamaged`, `TurnRateDamaged`, `AccelerationDamaged`) gehen denselben Weg (eigene Bildgrößen in `LocomotorFrames`); die Suite misst sie in 54 Fällen
ohne Fehlschlag außerhalb der beiden Fälle aus §5.

**Die drei Locomotoren „mit derselben Strecke je Tick bei jeder Rate“** (`AirF_AmericaCrateParachute`, `CINE_U04_AmericaParachute`, `HorizontalArrow`: 2,000 / 1,000 / 0,500 bzw.
1,000 / 0,500 / 0,250) waren kein übersehener Rohwert: die Fahrt dieser Objekte endet nach einem Schritt, das „Höchsttempo“ war ein einzelner Beschleunigungsschritt, und der
war je Tick² gerechnet. Mit der Beschleunigung je Bild (#1) sind die Werte bei jeder Rate gleich.

## 3. Verwendungen des Auswegs „Originalraster“

| Stelle | Was auf dem Raster läuft | Warum | Was nicht auf dem Raster läuft |
|---|---|---|---|
| `AIUpdateInterface.Update` → `DockMachine.Update` | die Zustandswechsel von `AIDockMachine` (Anflug beendet, Freigabe, Einfahrt, Ausfahrt), auf `Time.IsOriginalFrameBoundary` | ein Zustandsautomat des Originalbilds: jeder Wechsel kostet ein Update; das lässt sich nicht unterteilen, nur zählen | die Bewegung zum Dock; die Arbeit des Docks (`ProcessDock`) läuft je Tick, weil die Dock-Aktionen je Tick geschrieben sind (`RepairDockUpdate` heilt einen Anteil je Aufruf) und die Wartezeiten der Sammler von selbst im Raster liegen |
| `SupplyCarrierAI.Update` → `SupplyMachine.Update` | `SupplyTruckStateMachine` (untätig → will liefern → Dockbefehl) | wie oben | Befehle an den Sammler, seine Bewegung |
| `AIUpdateInterface.AdvanceFrame` → `ResolveFootprintCollisions` | die Abtastung der Überlappung (Quetschen, Umstoßen, `ICollideModule`) einmal je Bild des **Objekts**, mit dem letzten Anteil | bei 30 Hz wird nach jedem ganzen Schritt geprüft; eine dichtere Abtastung findet Überlappungen früher und in anderer Reihenfolge | die Lage ändert sich je Tick |
| `AIUpdateInterface.UpdateTask`, `UpdateGoal` | Reichweitenprüfung des Bauauftrags und Nachführen auf das Zielobjekt, wenn der Schritt des Bildes zurückgelegt ist | geprüft wird die Lage, die auch der 30-Hz-Lauf prüft | die Bauarbeit selbst (`AdvanceConstruction`) und die Reparatur laufen je Tick |

Die Entscheidung der Bewegung selbst („einmal je Bild“) ist die im Auftrag festgelegte Unterteilung, nicht der Ausweg: Lage und Kurs ändern sich in jedem Tick, ein Befehl wird im
nächsten Tick angenommen, ein stehendes Objekt fährt im Tick des Befehls los.

## 4. Messung

Suite, außerhalb der Toleranz bei 60 Hz / bei 120 Hz (Größen). „Ausgang“ ist der eigene Lauf am Stand `7b54374`.

| Gruppe | Größen | Ausgang | nach `bf028df` (#1–#4) | nach `fbe8d9a` (#5) | nach `096160d` (#6) | nach `1c65446` (#7, #8) | nach `6e018fe` (#9) |
|---|---|---|---|---|---|---|---|
| `locomotion` | 837 | 152 / 211 | 41 / 63 | 12 / 12 | 12 / 12 | 12 / 12 | 12 / 12 |
| `economy` | 81 | 22 / 25 | 22 / 24 | – | 20 / 25 | – | 24 / 27 |
| `scripts` | 15 | 4 / 5 | 4 / 4 | – | 4 / 4 | – | 4 / 4 |
| `overtime` | 418 | 80 / 84 | 78 / 85 | – | 80 / 81 | 80 / 81 | 80 / 81 |
| `flight` | 74 | 21 / 21 | 16 / 18 | 16 / 18 | 16 / 18 | – | 16 / 18 |
| `timed` | 383 | 53 / 56 | 53 / 53 | 53 / 53 | – | 52 / 52 | 52 / 52 |

„–“: für diesen Commit nicht gemessen. `locomotion`: von 217 Größen in 52 Fällen auf 14 Größen in 2 Fällen (§5). `scripts`: der Sequenzweg (300 Einheiten) dauert
15,1000 s bei jeder Rate (vorher 15,1000 / 15,1333 / 15,1500 s); die vier übrigen Größen sind Zähler und Wartezeiten der Skript-VM. `economy`: die vier Baufälle zeigen nach #9 die reine Bauzeit „N Bilder weniger ein Tick“, die Sammler
scheitern am zweiten Sammler (§5); Verkauf, Produktion und Hacker sind nicht Sache dieses Pakets. `overtime`: was bleibt, sind erste Pulse und Heilscheiben (Zähler N + 1,
Heilung je Tick), kein Weg. `flight`: weggefallen sind die Ankunftszeiten der Hubschrauber (Comanche, Chinook), die über `AIUpdateInterface.Move` fliegen
(Chinook 11,7000 / 11,7167 / 11,7250 s). Zwei Größen liegen nur bei 120 Hz außerhalb und sind Differenzen zweier Zeitpunkte, die je für sich innerhalb eines Bilds liegen:
Comanche „Kehre bis Ankunft“ 13,3333 / 13,3667 / 13,3833 s (der Kurs überschreitet die Schwelle mitten im Bild, die Ankunft fällt auf das Bildende) und – neu – der
Fallschirmabwurf „Befehl bis Flugzeug weg“ 49,2667 / 49,2333 / 49,2167 s (vorher 49,2667 / 49,2333 / 49,2333 s; `DeliverPayloadAIUpdate`, Paket FLIGHT).

**Ganze Suite am Endstand** (`9548bab`, alle zehn Gruppen, 4.307 Größen): außerhalb der Toleranz bei 60 Hz **334**, bei 120 Hz **417** (erster Lauf der Suite: 479 / 625); Größen mit
Fehlschlag 428 (643). `weapons` 43 / 57, `projectiles` 54 / 103 und `turrets` 28 / 36 sind unverändert, `ai` 21 / 27 (vorher 22 / 27).

Bestehende Simulationstests (Release, ohne Realdaten), Fehlschläge: 30 Hz 0 vorher und nachher; 60 Hz 6 → 6 (dieselben sechs aus `docs/TICKRATE-MIGRATION.md` §15.2);
120 Hz 14 → 13 (Nr. 2, das Referenzmatch mit Tickbudget, besteht in diesem Lauf – ein chaotischer Fall, kein Verdienst). Ein Test bestand bei 60 Hz nach #6 nicht mehr
(`Chinook: flight height, supply trips …`): seine Befehle folgten der dritten Lieferung in einzelnen Ticks und trafen den Versorgungs-Automaten bei jeder Rate in einem anderen
Zustand; er zählt jetzt in Bildern (Klasse T, eigener Commit).

## 5. Offen, und warum

- **Kampfrad und Mob-Nexus** (`locomotion`, 14 Größen, die einzigen Fehlschläge der Gruppe): nicht die Bewegung. Der Fahrer des Kampfrads und die Mitglieder des Mobs sind eigene
  Objekte, die **einen Tick** – nicht ein Bild – nach ihrem Träger erscheinen und einen weiteren Tick später einsteigen bzw. sich aufstellen (§5.3 `Register`, `ProcessSpawns`).
  Die Suite befiehlt die Fahrt ein Bild nach dem Erscheinen: bei 30 Hz steht der Fahrer dann noch frei am Startpunkt (das Rad plant um ihn herum), bei 60 Hz ist er im Tick des
  Befehls gerade nicht eingestiegen, bei 120 Hz sitzt er. Daher „nicht monoton in der Rate“. Gegenprobe: drei Bilder nach dem Erscheinen befohlen, ist die Lage beider Objekte nach
  jedem von 500 Bildern bei allen drei Raten bitgleich. Zu beheben in der Spawn-Kette der Welt, nicht hier.
- **Sammler-Umlauf** (`economy`, Laster, Arbeiter, Chinook): das Versorgungszentrum erzeugt einen eigenen Sammler; der erscheint aus demselben Grund bei jeder Rate zu einer anderen
  Spielzeit, fährt von selbst los und begegnet dem gemessenen Sammler an einer anderen Stelle (Ausweichen, belegte Anflugposition). Allein ist der gemessene Sammler über 9.000
  Bilder bitgleich (#6).
- **Umsturz** (`timed`, 4 Größen „Fall“): `ToppleUpdate` (anderes Paket) bekommt jetzt die 30-Hz-Kraft, addiert sein Winkeltempo aber je Tick. Mit einem Schritt je Bild (§5.1
  `InitialAccelPercent`, §5.2 `ToppleUpdate.Update`) wird die Falldauer gleich; bis dahin ist sie 2- und 4-mal zu kurz statt wie vorher eine Mischung zweier Fehler.
- **Helix, tiefste Höhe vor dem Entfernen 0,000 / 0,798 / 0,935** (`timed`): Flugcode, `HelicopterSlowDeathBehavior.Update` (`TransportModules.cs`, §5.2): der Euler-Fall endet
  bei 60/120 Hz einen Tick früher über dem Boden. Nur festgestellt, nicht angefasst (Paket FLIGHT).
- **Zwei fahrende Objekte, die sich begegnen:** bei 30 Hz zieht A ganz, dann sieht B (später im selben Tick) A an dessen neuer Lage. Oberhalb von 30 Hz sieht B im Tick der
  Entscheidung A erst einen Anteil weit gezogen. `StepBlocked`, `StandsStill` und die Überlappungsprüfung lesen die augenblickliche Lage des anderen; die Begegnung zweier Fahrender
  ist deshalb nicht bitgleich zu 30 Hz (Unterschied höchstens ein Bildschritt des anderen). Der Weg dorthin wäre eine „sequentielle Sicht“ (wer in diesem Tick schon gezogen hat,
  zählt mit seiner Lage am Bildende; wer noch zieht, mit seiner Lage am Bildanfang) – das verlangt je Nachbar einen Blick in dessen Modul im heißen Pfad und ist nicht gemacht.
  Gruppenfahrten, Formationen und ganze Gefechte sind Sache des Verteilungsvergleichs (Schritt 4).
- **Objekte außerhalb der Phase der Welt:** ein mitten im Bild der Welt befohlenes Objekt fährt mit eigener Phase. Dock- und Versorgungs-Automat laufen auf dem Raster der Welt und
  sehen seine Ankunft bis zu ein Bild später als bei einem Objekt in Phase; der nächste Bewegungsbefehl des Automaten bringt es auf das Raster der Welt. Ein Fall der Suite ist das
  nicht (sie befiehlt an Bildgrenzen).
- **Gruppentempo** (`SpeedCap`, `AIGroup::getSpeed`): der Deckel kommt als Strecke je Tick und wird mit den Ticks je Bild multipliziert – nicht die Bits des 30-Hz-Werts.
  Nicht gemessen.
- **`MoveWinged`/`SteerWinged`** (Flügelflug) sind unverändert (Paket FLIGHT). `Entity.Speed` bleibt für alle Leser die Strecke je Tick.

## 6. Neue Befunde

- Die Spawn-Kette (Träger → Fahrer/Mitglieder/eigener Sammler) ist die gemeinsame Ursache der verbliebenen Fehlschläge in `locomotion` und im Sammler-Umlauf (§5).
- **Ein Befehl im falschen Tick geht verloren – bei jeder Rate, auch bei 30 Hz:** `SupplyMachine` betritt „will liefern“ in einem Update und gibt im nächsten den Dockbefehl, ohne
  noch einmal zu prüfen, ob der Sammler inzwischen etwas anderes tut. Ein Befehl des Spielers (hier: Entladen des Chinook), der in genau diesem Bild eintrifft, wird vom Dockbefehl
  überschrieben (`OrderDock` löscht die anstehende Evakuierung). Das Fenster ist ein Bild lang; vor #6 war es bei 120 Hz einen Tick lang. Aufgefallen am Test
  `Chinook: flight height, supply trips …`; nicht behoben (Verhalten bei 30 Hz).
- `ToppleUpdate.ApplyTopplingForce`: der Parameter `speed` ist jetzt bei jeder Rate das Tempo je Bild des Originals (Vertrag für das Paket, dem `ToppleAndFallModules.cs` gehört).

## 7. Beweis bei 30 Hz

Nach jedem Commit: `SimTool input-manifest check` identisch, `SimTool reference-manifest check --set quick` 9 Läufe identisch. Am Ende: `--set full` 50 Läufe identisch (Stand `9548bab`), `dotnet build CnCNew.slnx` 0 Warnungen (inkrementell, nicht mit gelöschtem `bin`/`obj`), alle 424 Simulationstests ohne Realdaten
bei 30 Hz grün (Debug).
`SimTool ai-match … Alpine Assault … FactionChina,FactionGLA --max-ticks 45000` bei 30 Hz: Sieger FactionChina bei Tick 10.710, 11.011 Ticks, Endhash `BAABE02976F3F436`
(Manifestlauf `AlpineAssault_China-GLA`, unverändert).

## 8. Leistung und KI-Lauf (zur Information)

Leistung bei 30 Hz: `SimTool perf … --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 12000` aus zwei kopierten Binärverzeichnissen (Ausgang `7b54374`, nachher
`6e018fe` bzw. `9548bab`), abwechselnd, zwei Serien zu je vier Paaren. Die Arbeit ist identisch: 823.710 Aufrufe von `AIUpdateInterface`, Endhash `F78F071AF5EC8A6E`, Kette
`D9975439375F5DF7` in allen 16 Läufen. **Der Rechner war nicht ruhig** – fünf weitere Pakete arbeiteten gleichzeitig; die Tickzeiten liegen beim Doppelten bis Vierfachen von
`docs/PERFORMANCE.md` §6a und streuen zwischen zwei Läufen derselben Binärdatei um bis zu 100 %.

| Serie, Paar | Tick, Mittel µs vorher | nachher | `AIUpdateInterface` je Aufruf µs vorher | nachher |
|---|---|---|---|---|
| 1, 1 | 2.817,9 | 3.203,1 | 7,00 | 7,94 |
| 1, 2 | 2.740,4 | 3.803,4 | 6,76 | 9,83 |
| 1, 3 | 3.064,7 | 3.101,2 | 7,88 | 7,86 |
| 1, 4 | 3.127,3 | 2.939,0 | 7,81 | 7,47 |
| 2, 1 | 5.083,4 | 2.373,2 | 14,32 | 5,98 |
| 2, 2 | 4.775,7 | 2.614,5 | 14,13 | 6,45 |
| 2, 3 | 2.522,8 | 2.494,5 | 6,18 | 6,33 |
| 2, 4 | 2.230,3 | 2.331,9 | 5,67 | 5,98 |

In Serie 1 sind die ersten beiden Paare nachher langsamer, in Serie 2 vorher (Fremdlast: das 95-%-Quantil der betroffenen Läufe ist zwei- bis viermal so hoch wie in den
übrigen). Die vier Paare ohne solchen Ausreißer (1,3; 1,4; 2,3; 2,4): Tick im Mittel 2.736 µs vorher gegen 2.717 µs nachher (−0,7 %), `AIUpdateInterface` 6,89 gegen 6,91 µs je
Aufruf (+0,4 %). **Eine Verschlechterung bei 30 Hz ist nicht messbar; die Messung ist aber nur auf einige Prozent genau** und gehört auf einem ruhigen Rechner wiederholt. Der
zusätzliche Code im 30-Hz-Pfad sind einige Vergleiche und Kopien je Entscheidung, keine neue Division. Oberhalb von 30 Hz kostet die Bewegung je Spielsekunde ungefähr so viel wie
bei 30 Hz (eine Entscheidung je Bild), nicht das Doppelte oder Vierfache – nicht eigens gemessen.

KI-Lauf Alpine Assault, China gegen GLA: 30 Hz Sieger FactionChina nach 6,0 Spielminuten (Tick 10.710); 60 Hz Sieger FactionChina nach 7,0 Spielminuten (Tick 25.110,
Endhash `F9765C191B1E04BD`, zweiter Lauf identisch) – vor diesem Paket bei 60 Hz FactionChina nach 6,5 Spielminuten (Tick 23.220). Ein Lauf, kein Kriterium.

## 9. Tests

`tests/CnCNew.Simulation.Tests/Tests.RateInvariance.Ground.cs` (synthetische Fixture, je bei 30, 60 und 120 Hz):

| Test | Was er hält |
|---|---|
| `GroundRateInvarianceCourse` | ein Kurs mit vier Abschnitten mit Gelände, ohne Gelände, mit beschädigtem Locomotor und in kurzen Sprüngen: Lage, Höhe, Kurs und Befehlszustand nach jedem Bild sind die von 30 Hz; jede Ankunft fällt in dasselbe Bild und auf dessen letzten Tick; zwischen zwei Bildern liegt das Objekt auf der Geraden zwischen ihnen |
| `GroundRateInvarianceStandingUnits` | der Umweg um drei stehende Einheiten ist der 30-Hz-Umweg |
| `GroundRateInvarianceSnapshotInsideAFrame` | ein Snapshot einen Tick nach einer Entscheidung: bytegleich wiederhergestellt, 400 Bilder lang derselbe Hash wie die laufende Welt |
| `GroundRateInvarianceOrderInsideAFrame` | ein Befehl mitten im Bild: der Rest des Schritts wird zurückgelegt, dann der neue Kurs; ein Befehl im letzten Bild hebt die Ankunft auf |
| `GroundRateInvarianceCollisionsPerFrame` | Quetschen und Umstoßen im selben Bild, auf dessen letztem Tick |
| `GroundRateInvarianceSupplyTrips` | drei Lieferungen eines Sammlers auf denselben Bildern; Lage und Kontostand nach jedem Bild wie bei 30 Hz |

Gegenprobe am Ausgangsstand `7b54374` (eigener Arbeitsbaum, dieselbe Testdatei): fünf der sechs Tests scheitern bei 60 Hz – der Kurs und der Umweg an „die Fahrt endet mit dem
letzten Tick ihres Bildes“ (Tick 0 von 2), der Befehl mitten im Bild, die Kollisionen (Schritte 89 und 150) und die Lieferungen (Ticks 507, 1.109, 1.705 gegen 250, 548, 843 bei
30 Hz). Der Snapshot-Test besteht dort, weil es am Ausgangsstand keinen Zustand innerhalb eines Bilds gibt. Am Endstand bestehen alle sechs bei `--tick-rate` 30, 60 und 120.
