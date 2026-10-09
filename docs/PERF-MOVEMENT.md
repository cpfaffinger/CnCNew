# Leistung: Bewegung und Wegsuche (Phase 19)

Stand: 6. Oktober 2026, Arbeitszweig `worktree-agent-a26ca0ad009d62c7a`, Ausgangsstand `74ccdde`. Auftrag: `docs/handoffs/HANDOFF-PHASE19.md`,
Kostenstelle „Bewegung und Wegsuche“ – `AIUpdateInterface` und was es aufruft (`NavigationGrid`, `SimulationWorld.Collision.cs`),
einschließlich der davon abgeleiteten Module (`TransportAIUpdate`, `SupplyTruckAIUpdate`, `DozerAIUpdate`, `AssaultTransportAIUpdate`,
`ChinookAIUpdate`, `WorkerAIUpdate`). Die Messwerkzeuge stehen in `docs/PERFORMANCE.md`; dieses Dokument beschreibt nur, was für diese
Kostenstelle dazugekommen ist, was gemessen wurde, was geändert wurde und was bleibt.

## 1. Die Regel

**Kein Ergebnis ändert sich.** Jede Änderung liefert dieselben Wege (Zelle für Zelle, Wegpunkt für Wegpunkt), dieselben Ausweichschritte,
dieselben Kollisionen in derselben Reihenfolge und damit dieselben Hashes. Geprüft nach jedem Commit mit
`SimTool reference-manifest check … --set quick` (9 Läufe, unverändert) und am Ende mit `--set full` (§8). Zulässig waren nur: schnellere
Rechenwege mit demselben Wert, Zwischenspeichern von Antworten, die sich nachweislich nicht ändern können, Datenstrukturen ohne Allokation,
und das vorzeitige Beenden einer Suche, deren Ergebnis bereits feststeht. Nicht angefasst: Arbeit über Ticks verteilen, Knotenbudgets,
Näherungen, geänderte Gleichstandsregeln (§7).

Was die Wegsuche zwischen zwei Aufrufen behält (Suchdatensätze je Zelle, Warteschlange, Puffer, gemerkte Antworten), ist Arbeitsspeicher
des Gitters und **kein Zustand**: nicht im Hash, nicht im Snapshot. Eine wiederhergestellte Welt beginnt mit leeren Puffern und berechnet
dieselben Wege.

## 2. Messwerkzeug

Die Simulation hat keine Uhr (`docs/PERFORMANCE.md` §1). Für die Bewegung gibt es einen zweiten Haken neben `ITickPhaseObserver`:

- `IMovementProbe` (`src/CnCNew.Simulation/Terrain/MovementProbe.cs`), gesetzt über `NavigationGrid.Probe`, normalerweise `null`, kein
  Zustand. Die Simulation meldet Beginn und Ende von vier Arbeiten (`MovementWork`: `PathSearch`, `StationaryBlockers`, `LocalAvoidance`,
  `FootprintCollisions`) und je Wegsuche ein `PathSearchInfo` mit deterministischen Zählern: Startzelle, Zielzelle, Oberflächen, Ergebnis,
  **expandierte Zellen** (aus der Warteschlange genommene Einträge, wie sie das Budget zählt), Einfügungen in die Warteschlange,
  Fluchtzellen, zeitweise gesperrte Zellen, Zellen und Wegpunkte des Weges, Abstand des Wegendes vom Ziel, und ob die Antwort aus dem
  Gedächtnis kam oder die Suche vorzeitig endete.
- `SimTool perf … --movement [--movement-tick N]` und `perf-mission … --movement` (`tools/CnCNew.SimTool/MovementRecorder.cs`) lesen dort die
  Uhr und drucken: Aufrufe, Summe, µs je Tick, Mittel, Maximum und Allokation je Aufruf der vier Arbeiten; die Suchen nach Ergebnis, nach
  aufrufendem Modul, nach Größe und (für Teilwege) nach Abstand des Wegendes vom Ziel; die fünf Ticks und die fünf Einheiten mit den meisten
  expandierten Zellen; alle Suchen eines Ticks (`--movement-tick`, sonst der teuerste). Die Zähler stehen auch in der `summary`-Zeile
  (`move_*`).
- `SimTool path-bench <content> --map <karte> [--searches N] [--rounds N] [--seed N]` (`tools/CnCNew.SimTool/PathBench.cs`): eine feste
  Liste von Suchen auf dem Gelände einer echten Karte (ohne Gebäude), mehrfach; jede Suche zählt mit ihrem schnellsten Durchgang. Ausgabe:
  expandierte Zellen, ns je expandierter Zelle und eine Prüfsumme über alle zurückgegebenen Wege, die für jeden Stand gleich sein muss, der
  gleich sucht.

**Die Zähler sind die Messlatte.** Der Rechner war während der ganzen Arbeit von vier weiteren Agenten belegt (CPU-Last 100 %); dieselbe
Binärdatei lief je nach Augenblick um den Faktor 2 bis 4 langsamer als in der ruhigen Messung der Phase 18 (die Phase `Vision`, die hier
niemand angefasst hat, brauchte 2.262 bis 2.971 µs je Tick statt ruhig 716). Alle Zeiten in diesem Dokument sind deshalb **nicht ruhig
gemessen**; Vergleiche stammen aus Läufen zweier Stände nebeneinander. Zellen, Aufrufe und Suchen sind dagegen in jedem Lauf gleich.

## 3. Wohin die Zeit ging (Ausgangsstand)

Twilight Flame, 8 KI-Spieler, 54.000 Ticks (Endhash `0A69FC72FF6975CD`, Kette `78D32DCBCE8A99EB`), und `MD_GLA05`, 3.000 Ticks (Endhash
`826CCD82C8536DC2`); Stand `74ccdde` plus Sonde, nicht ruhig gemessen:

| Arbeit | 8 Spieler: Aufrufe | µs je Tick | Mittel µs | `MD_GLA05`: Aufrufe | µs je Tick | Mittel µs |
|---|---:|---:|---:|---:|---:|---:|
| `StationaryBlockersAhead` | 4.773.549 | 1.975 | 22,3 | 24.610 | 1.138 | 138,7 |
| `AvoidLocalBlockers` | 5.267.887 | 1.961 | 20,1 | 36.108 | 1.302 | 108,2 |
| Wegsuche | 83.462 | 829 | 536 | 595 | 1.287 | 6.490 |
| `ResolveFootprintCollisions` | 5.333.813 | 251 | 2,5 | 37.057 | 2.034 | 164,7 |
| Summe | | 5.016 | | | 5.761 | |
| Phase `Updates` im selben Lauf | | 5.849 | | | 11.540 | |

Im Gefecht steckten 86 % der Update-Phase in diesen vier Arbeiten; der Rest von `AIUpdateInterface` (Steuern, Bremsen, Dock- und
Versorgungsautomaten) ist daneben klein. Vier Ursachen:

1. **Festkommaarithmetik über 128-Bit-Division.** `Fixed` rechnete jedes Produkt und jeden Quotienten über `Int128` (eine in Software
   ausgeführte 128-Bit-Division je Operation) und die Wurzel als Newton-Verfahren auf `Int128`. Ein Kreistest kostet drei bis zehn Produkte.
2. **Jede fahrende Bodeneinheit durchläuft je Tick bis zu siebenmal alle Objekte der Welt** (`StationaryBlockersAhead` in jedem Tick, nicht
   alle 30; bis zu fünf `StepBlocked` in `AvoidLocalBlockers`; `ResolveFootprintCollisions`). Je Paar wurde der Hüllkreis neu berechnet
   (`BoundingCircleRadius`: Zeichenkettenvergleich auf `BOX`, für Quader zusätzlich eine Wurzel), und der Aufzähler lief über
   `IReadOnlyCollection<Entity>` (je Durchlauf eine Allokation). In `MD_GLA05` sind es 2.250 Objekte für eine Handvoll fahrender Einheiten.
3. **Wegsuchen, deren Ergebnis feststeht oder niemand braucht.** Von 213.410.509 expandierten Zellen entfielen 105.200.000 auf 526 Suchen,
   die ihr Budget von 200.000 Zellen ausschöpften:
   - 304 davon waren Umwegsuchen (`ReplanAround`, alle 30 Ticks je Einheit mit stehenden Einheiten voraus), deren Ergebnis nur zählt, wenn
     es das Ziel erreicht – der Teilweg wurde verworfen. In `MD_GLA05` waren das 85 Suchen mit 12,3 von 12,34 Millionen Zellen; eine einzige
     Einheit (`AmericaTankCrusader` #2211) flutete 84-mal die Karte.
   - 214 waren Befehle an einen Ort, an dem niemand stehen kann. 131 von 137 solcher Suchen (nach Abzug der Wiederholungen) enden höchstens
     zwei Zellen vor dem Ziel: die Suche hatte ihre Antwort nach einigen hundert Zellen und expandierte danach alles Erreichbare.
   - Dazu Gruppen: Einheiten, die im selben Tick dieselbe Frage stellen (gleiche Startzelle, gleiches Ziel).
4. **Die Suche selbst**: je Nachbarzelle Grenzprüfung, Hinderniszähler, Geländeflags und bis zu zwei `HashSet`-Abfragen (Fluchtzellen,
   zeitweise gesperrte Zellen), vier getrennte Felder je Zelle, je Suche 1.060 bis 1.121 Byte Allokation (Liste, Builder, Queue), eine
   Halde mit bis zu 70.000 Einträgen.

### Der 547-ms-Tick (Tick 52.843 des 8-Spieler-Laufs)

Ruhig gemessen 547 ms (Phase 18), in den Läufen hier 1.196 ms. Die Sonde zeigt: **73 Wegsuchen in einem Tick**, zusammen **5.350.066
Zellen**. Die Liste der Sonde ist auf 60 Zeilen begrenzt; alle 60 kommen von `AIUpdateInterface`, gehen zum selben Ziel (Zelle 436,312),
sind erfolgreich und expandieren je 71.628 bis 82.361 Zellen (Weg 347 bis 363 Zellen, 10 bis 11 Wegpunkte). Es ist eine Infanteriegruppe
(`Infa_ChinaInfantryTankHunter`, `Infa_ChinaInfantryMiniGunner`), die im selben Tick ihren Weg plant (derselbe Befehl oder derselbe Neuplan
nach einer Grundrissänderung; das unterscheidet die Sonde nicht) und deren Mitglieder auf nur sechs verschiedenen Zellen stehen (331,74
dreiundzwanzigmal; 332,75 siebzehnmal; 331,75 elfmal; 316,60 sechsmal; 329,72 zweimal; 333,76 einmal). Das Ziel liegt in einem Gebäudegrundriss (180 Fluchtzellen), der Weg führt um Hindernisse herum, die die
Oktil-Heuristik nicht kennt; deshalb expandiert jede Suche ein Drittel der Karte. Kein Budgetlauf, kein unerreichbares Ziel: dutzendfach
dieselbe teure, richtige Suche.

Nach den Änderungen: dieselben 73 Anfragen, aber unter den 60 gezeigten nur noch **sechs echte Suchen** (je Startzelle eine, zusammen
444.697 Zellen), alle anderen Antworten kommen aus dem Gedächtnis des Gitters (§4.4); die sechs Suchen brauchten im belasteten Lauf zusammen
rund 45 ms. Der Tick ist damit um rund den Faktor 10 billiger und nicht mehr unter den fünf teuersten des Laufs, aber nicht unter dem Soll
von 8 ms: sechs Suchen mit je 72.000 bis 82.000 Zellen bleiben (§6).

## 4. Die Änderungen

Jede Änderung ist ein Commit; die Zahlen stehen auch in der Commit-Nachricht. „Nebeneinander“ heißt: beide Stände liefen gleichzeitig auf
demselben belasteten Rechner.

### 4.1 `Fixed`: Produkt, Quotient und Wurzel ohne 128-Bit-Division (`40fcbc6`)

Das Produkt ist das 128-Bit-Produkt der Beträge (`Math.BigMul`), gerundet durch „Hälfte addieren, schieben“; der Quotient rechnet in
64 Bit, wenn Betrag(a) · 2¹⁶ hineinpasst (sonst der bisherige 128-Bit-Weg); die Wurzel läuft als dasselbe Newton-Verfahren auf 64-Bit-Worten,
wenn raw · 2¹⁶ hineinpasst.

*Warum ergebnisneutral:* „zum nächsten, bei Gleichstand von null weg“ ist auf Beträgen „Hälfte addieren, abschneiden“ – dieselbe Formel
wie vorher, nur ohne allgemeine Division. Das Newton-Verfahren endet von jedem Startwert oberhalb der Wurzel bei ⌊√⌋; der nähere Startwert
ändert die Schrittzahl, nicht das Ergebnis. Überlauf wirft wie zuvor. *Test:* `FixedFastPathsMatchWideArithmetic` vergleicht 1,6 Millionen
Produkte und Quotienten und 1,2 Millionen Wurzeln mit den im Test aufbewahrten 128-Bit-Formeln (Randwerte, zufällige Bitbreiten, die Bereiche
der Simulation). *Nebeneinander, 18.000 Ticks, µs je Tick:* Tick 11.678 → 7.228; `Updates` 7.530 → 3.393; `StationaryBlockers` 2.536 → 1.051;
`LocalAvoidance` 2.269 → 580; `FootprintCollisions` 440 → 106; Wegsuche 1.188 → 955; `Acquisition` 166 → 100. Die Änderung wirkt auf jede
Kostenstelle, die Festkomma rechnet.

### 4.2 Objektdurchläufe eines Bewegungsschritts (`4605da9`, `e5ebf91`, `e0a93fa`, Korrektur `309dc7d`)

- Der Hüllkreis einer Vorlage (`ObjectTemplate.BoundingCircleRadius`) wird je Vorlage einmal berechnet und in einer Tabelle der **Welt**
  nach Vorlagenindex nachgeschlagen (die Vorlage ist unveränderlich, der Wert kann nicht veralten). Der erste Anlauf (`4605da9`) hatte den
  Wert als Feld in die Vorlage gelegt; das änderte kein Simulationsergebnis, aber die Prüfsumme der Kataloge im Eingabemanifest, das die
  Kataloge Feld für Feld beschreibt (`input-manifest check` meldete `catalogs.templates` und `catalogs.whole` als verschieden). Die Korrektur
  stellt `ObjectTemplate.cs` byteidentisch zum Ausgangsstand wieder her. **Merke:** Vorlagen und andere Katalogtypen bekommen in einer
  ergebnisneutralen Phase keine Felder; abgeleitete Werte gehören in die Welt.
- Die Durchläufe benutzen den Struktur-Aufzähler der Objekttabelle statt `IReadOnlyCollection` (keine Allokation, gleiche Reihenfolge).
- Vor dem exakten Kreistest fällt ein Paar weg, wenn eine Koordinate allein weiter auseinanderliegt als die Grenze plus eine Einheit. Eine
  Einheit ist weit mehr als die Rundung eines Produkts (höchstens 1/131.072): (Grenze + 1)² übertrifft Grenze² um mindestens 1, also bleibt
  das gerundete Quadrat der Koordinatendifferenz über dem gerundeten Quadrat der Grenze, für `<` wie für `<=`. Der exakte Test selbst ist
  unverändert.
- Die vier Vorausschaupunkte von `StationaryBlockersAhead` hängen nur von der fahrenden Einheit ab und werden einmal je Aufruf berechnet.
- `StationaryBlockersAhead` antwortet in einer Liste der Welt (der einzige Aufrufer plant sofort und behält nichts).
- **Listen statt aller Objekte:** Die Welt führt zwei Listen in aufsteigender Id-Reihenfolge, gefüllt und geleert genau dort, wo ein Objekt
  in die Objekttabelle kommt und sie verlässt (`TrackCollidable`/`UntrackCollidable`): die Objekte mit Locomotor (nur sie betrachten
  Ausweich- und Stehblocker-Durchlauf) und die Objekte mit `ToppleUpdate` oder `SquishCollide` (nur für sie tun `TryTopple`/`TrySquish`
  etwas). Vorlage und Modulliste eines Objekts ändern sich nie; die Durchläufe besuchen dieselben Objekte in derselben Reihenfolge wie vorher,
  ohne die, die sie übersprangen oder für die nichts geschah. `TryTopple`/`TrySquish` suchen ihr Modul in einer Schleife statt mit
  `OfType().FirstOrDefault()`.
- **Position zuerst:** Die Welt merkt sich den größten Hüllkreis (als Betrag) aller mobilen Objekte, die sie registriert hat. Ein Objekt, das
  auf einer Achse weiter entfernt ist als Radius der Einheit plus dieser Radius plus eine Einheit, besteht den exakten Test auch mit seinem
  eigenen, kleineren Radius nicht und wird nach dem Lesen seiner Position verworfen, bevor irgendetwas anderes von ihm gelesen wird.

*Test:* `MovementScansMatchTheFullScan` vergleicht `AvoidLocalBlockers` und `StationaryBlockersAhead` mit den früheren Volldurchläufen (im
Test aufbewahrt) auf sechs zufälligen Szenen, rund 86.000 Paaren aus Einheit und Schritt, mit Objekten, die zwischen den Runden entstehen und
verschwinden. Der Quetschpfad ist durch die bestehenden Kollisionstests und die Manifestläufe gedeckt (`MD_USA05`, `MD_GLA05`).

### 4.3 Wegsuche: ein Zustandsbyte je Zelle, keine Allokation (`46e7415`)

Dieselbe Suche: derselbe A*, dieselbe Nachbarreihenfolge, dieselben Schlüssel, dieselbe Budgetzählung (auch veraltete Einträge der
Warteschlange zählen), dieselbe Regel für die nächstgelegene Zelle, dieselbe Glättung. Geändert ist, wie sie liest und schreibt: ein Byte je
Zelle (Geländeklasse, „von einem Grundriss bedeckt“, zwei Marken der laufenden Suche für Fluchtzellen und zeitweise gesperrte Zellen), eine
Tabelle mit 32 Einträgen je Suche für „darf betreten werden“, ein Datensatz von 16 Byte je Zelle statt vier Feldern, die vier geraden
Nachbarn werden einmal geprüft und für die Diagonalen wiederverwendet, alle Zwischenergebnisse liegen in Puffern des Gitters (1.121 → 88 Byte
je Suche, das ist das Ergebnis selbst).

*Test:* `PathSearchMatchesTheOriginalSearch` vergleicht 24.000 Suchen auf zehn zufälligen Karten (Klippen, Wände mit Lücken, ein
geschlossener Ring, ein See, Grundrisse, die kommen und gehen, Blocker, Starts und Ziele in Grundrissen, Wiederholungen) Punkt für Punkt mit
der früheren Suche, die im Test aufbewahrt ist; `PathSearchAtTheBudgetMatchesTheOriginalSearch` dasselbe für Suchen am Budget auf einer Karte
mit 520 × 440 Zellen. *Nebeneinander, 18.000 Ticks:* Wegsuche 1.757 → 851 µs je Tick (463 → 225 ns je expandierter Zelle); Zähler
unverändert (29.337 Suchen, 68.227.327 Zellen, 74.908.102 Einfügungen).

### 4.4 Das Gitter merkt sich Antworten, bis sich ein Grundriss ändert (`978d9c1`)

Eine Suche ohne zeitweise Blocker ist eine Funktion von Startzelle, Zielzelle, Oberflächen, Gelände (unveränderlich) und Grundrissen.
Solange kein Grundriss hinzukommt oder verschwindet, hat dieselbe Frage dieselbe Antwort, wer auch fragt und in welchem Tick. Das Gitter
behält bis zu 4.096 Antworten und verwirft alle, sobald `AddFootprint`/`RemoveFootprint` läuft. Nur der letzte Punkt eines Weges, der
ankommt, ist die genaue Zielposition des Fragenden statt einer Zellmitte; er wird beim Wiederausgeben eingesetzt. Suchen um zeitweise Blocker
werden nie gemerkt (ihre Zellen gehören zur Frage).

*Test:* ein Drittel der 24.000 Fragen wird erneut gestellt, in derselben Runde oder nach einer Grundrissänderung, zur Hälfte mit anderer
genauer Zielposition in derselben Zelle. *54.000 Ticks:* 29.315 von 83.462 Suchen aus dem Gedächtnis (35 %).

### 4.5 Eine Umwegsuche, die nicht ankommen kann, hört auf (`a8aeec5`)

`NavigationGrid.FindPathToGoal` liefert den Weg von `FindPath`, wenn er ankommt, sonst `null`; `ReplanAround` benutzt es. Es ist dieselbe
Suche, zusätzlich: Eine Zielzelle, die die Suche nicht betreten darf, beendet sie sofort. Und neben der Suche läuft eine Flut vom Ziel aus
(1.024 Zellen je 1.024 aus der Warteschlange genommenen), rückwärts entlang der Züge der Suche: in eine offene Zelle, diagonal nur zwischen
zwei offenen Zellen. Diese Züge sind in beiden Richtungen dieselben, nur der Start muss selbst nicht offen sein. Ist die Flut vollständig,
ohne den Start getroffen zu haben, gibt es keinen Weg, und die Suche endet; trifft sie den Start, wird sie abgeschaltet.

*Warum ergebnisneutral:* Eine Suche, die ankommt, liefert genau den Weg wie zuvor. Eine, die nicht ankommt, lieferte einen Weg, den der
Aufrufer verwarf. *Test:* jede der 24.000 Fragen zusätzlich über `FindPathToGoal`; Ziele hinter einer geschlossenen Wand; lange Suchen, die
ankommen, während die Flut viele Schritte mitläuft; Ziele, die ihre Blocker einschließen. *`MD_GLA05`:* 12.340.044 → 118.878 expandierte
Zellen im ganzen Lauf (mit §4.7 dann 55.217).

### 4.6 Warteschlange als Ring kleiner Halden (`7c73537`)

Die Reihenfolge, in der eine Suche Zellen aus ihrer Warteschlange nimmt, hängt nur von den eingereihten Werten ab (kleinste Gesamtkosten,
dann kleinster Zellindex), nicht von der Datenstruktur. Die Gesamtkosten der eingereihten Zellen liegen nie unter denen der Zelle, die gerade
expandiert wird (der Oktil-Abstand sinkt je Schritt höchstens um dessen Kosten), und höchstens 70 darüber (der teuerste Schritt, diagonal
auf eine Fluchtzelle, kostet 56, der Abstand wächst um höchstens 14). Die Warteschlange ist deshalb ein Ring aus 128 Halden von Zellindizes,
eine je Gesamtkostenwert. Dieselben Zellen in derselben Reihenfolge, einschließlich der veralteten Einträge, die auf das Budget zählen. Ein
Wert außerhalb des Fensters wäre eine fehlerhafte Suche und wirft. *`path-bench` nebeneinander:* 127,0 → 103,7 ns je expandierter Zelle,
Zellen (13.746.547) und Wegprüfsumme (`A38F733774279489`) gleich.

### 4.7 Eine Suche endet, wenn ihre nächstgelegene Zelle feststeht (`64e5979`)

Nach 256 expandierten Zellen und bei jeder Verdopplung fragt die Suche, ob sich ihre Antwort noch ändern kann, sofern ihre nächstgelegene
Zelle höchstens 16 Zellen vom Ziel entfernt ist:

- Eine Zelle mit *gleichem* Abstand zum Ziel gewinnt nur mit geringeren Kosten (oder gleichen Kosten und kleinerem Index). Jede Zelle, die
  noch expandiert wird, hat mindestens die Gesamtkosten der nächsten Zelle der Warteschlange; sobald diese die Gesamtkosten der
  nächstgelegenen Zelle übersteigen, kostet eine gleich weit entfernte Zelle mehr.
- Eine Zelle mit *kleinerem* Abstand liegt in einem kleinen Block um das Ziel. Eine Flut von den offenen Zellen dieses Blocks aus, rückwärts
  entlang der Züge der Suche, trifft entweder eine Zelle, die diese Suche eingereiht oder expandiert hat (dann ist eine nähere Zelle
  erreichbar: weitersuchen), wächst über 4.096 Zellen (weitersuchen) oder versiegt: dann führt kein Weg vom Start zu einer näheren Zelle,
  denn jeder solche Weg müsste eine Zelle der Flut betreten.

Gilt beides, endet die Suche und liefert den Weg zur nächstgelegenen Zelle. Dessen Vorgängerkette steht fest, weil expandierte Zellen nie
mehr geändert werden: Es ist der Weg, den die Suche nach dem Fluten der Karte geliefert hätte. Das Ziel selbst liegt in dem Block; die Suche
wäre also auch nicht angekommen.

*Test:* die rund 4.000 Teilwege unter den 24.000 Zufallssuchen; dazu auf der Karte mit 520 × 440 Zellen Ziele auf einzelnen Klippenzellen,
auf der Wand, hinter ihr, in und auf einem Klippenring, für zwei Oberflächensätze, jeweils einfach, um Blocker herum und über
`FindPathToGoal`; der Test prüft über die Sonde, dass das frühe Ende eintritt. *54.000 Ticks:* Suchen am Budget 138 → 0; 393 Suchen enden
früh nach zusammen 265.472 Zellen.

## 5. Vorher und nachher (nicht ruhig gemessen)

Ausgangsstand (`74ccdde` plus Sonde) und Endstand liefen nebeneinander auf dem belasteten Rechner. Zähler sind exakt; Zeiten sind um den
Faktor 3 bis 4 höher als auf einem ruhigen Rechner und schwanken zwischen den Läufen (die Spalte „Vision“ zeigt die Last: niemand hat diese
Phase in diesem Zweig angefasst, ruhig waren es 716 µs). „Nachher“ ist der Stand `e0a93fa`; die Korrektur `309dc7d` danach liest denselben
Hüllkreis aus einer Tabelle der Welt statt aus einem Feld der Vorlage und ist nicht eigens nachgemessen.

### 5.1 Twilight Flame, 8 Spieler, 54.000 Ticks (Endhash `0A69FC72FF6975CD`, Kette `78D32DCBCE8A99EB` in beiden)

| Größe | vorher | nachher | Faktor |
|---|---:|---:|---:|
| Phase `Updates`, µs je Tick | 11.812 | 1.670 | 7,1 |
| `AIUpdateInterface`, Summe ms (4.974.122 Aufrufe) | 505.187 | 36.327 | 13,9 |
| `AIUpdateInterface`, Mittel µs je Aufruf | 101,6 | 7,3 | 13,9 |
| `TransportAIUpdate`, Summe ms | 26.367 | 1.624 | 16,2 |
| `SupplyTruckAIUpdate`, Summe ms | 24.956 | 2.548 | 9,8 |
| `DozerAIUpdate`, Summe ms | 8.633 | 1.153 | 7,5 |
| `ChinookAIUpdate`, Summe ms | 10.727 | 6.638 | 1,6 |
| `StationaryBlockersAhead`, µs je Tick (µs je Aufruf) | 4.098 (46,4) | 121 (1,37) | 33,9 |
| `AvoidLocalBlockers`, µs je Tick (µs je Aufruf) | 3.842 (39,4) | 215 (2,20) | 17,9 |
| `ResolveFootprintCollisions`, µs je Tick (µs je Aufruf) | 491 (4,97) | 22 (0,22) | 22,5 |
| Wegsuche, µs je Tick | 1.586 | 231 | 6,9 |
| Wegsuchen (Anfragen) | 83.462 | 83.462 | – |
| davon aus dem Gedächtnis | 0 | 29.315 | – |
| expandierte Zellen | 213.410.509 | 76.822.493 | 2,8 |
| Suchen am Budget (200.000 Zellen) | 526 | 0 | – |
| ns je expandierter Zelle | 401 | 162 | 2,5 |
| Allokation je Suche / je Durchlauf, Byte | 1.060 / 44–134 | 158 / 0 | – |
| teuerster Tick nach Zellen | 52.843: 5.350.066 | 25.198: 1.891.288 | 2,8 |
| Tick 52.843: echte Suchen / Zellen (60 gezeigte Anfragen) | 60 / 4.405.450 | 6 / 444.697 | 9,9 |
| Allokation je Tick, Byte (ganzer Tick) | 95.627 | 75.938 | – |
| Tick, Mittel µs (alle Kostenstellen; Vision 2.971 bzw. 2.262) | 18.661 | 6.895 | 2,7 |

Der mittlere Tick enthält Sicht und Hash, an denen andere arbeiten; sie machen im Endstand 71 % des Ticks aus (`Vision` 2.262, `VisionHash`
1.281, `Hash` 1.332 von 6.895 µs im belasteten Lauf).

### 5.2 `MD_GLA05`, 3.000 Ticks (Endhash `826CCD82C8536DC2`, Kette `10330D7D179054FD` in beiden)

| Größe | vorher | nachher | Faktor |
|---|---:|---:|---:|
| `AIUpdateInterface`, Summe ms (33.465 Aufrufe) | 31.477 | 581 | 54 |
| `AIUpdateInterface`, Mittel µs je Aufruf | 940,6 | 17,4 | 54 |
| `SupplyTruckAIUpdate`, Summe ms | 1.343 | 78 | 17 |
| `TransportAIUpdate`, Summe ms | 844 | 38 | 22 |
| `StationaryBlockersAhead`, µs je Aufruf (24.610 Aufrufe) | 262,9 | 2,98 | 88 |
| `AvoidLocalBlockers`, µs je Aufruf (36.108 Aufrufe) | 207,4 | 3,37 | 62 |
| `ResolveFootprintCollisions`, µs je Aufruf (37.057 Aufrufe) | 296,6 | 8,31 | 36 |
| Wegsuche, Summe ms (595 Anfragen) | 9.283 | 52 | 178 |
| expandierte Zellen | 12.340.044 | 55.217 | 223 |
| Phase `Updates`, µs je Tick | 21.698 | 9.436 | 2,3 |
| Tick, Mittel µs (Vision 5.333 bzw. 6.536, Skripte 8.074 bzw. 11.456) | 46.575 | 38.342 | – |

Die Last war in diesem Lauf noch höher als im Gefecht (ruhig brauchte der Tick dieser Mission 13,5 ms). In der Update-Phase bleibt
`PointDefenseLaserUpdate` (17.761 → 16.005 ms, eine andere Kostenstelle) mit 57 % der Modulzeit; `AIUpdateInterface` liegt bei 2 %. Die 85
Umwegsuchen, die vorher je rund 145.000 Zellen fluteten, enden jetzt nach 256 Zellen (83 über §4.7, bevor §4.5 greift).

### 5.3 Einordnung gegen die Sollwerte

Eine ruhige Messung war nicht möglich. Rechnet man die belasteten Zeiten mit dem Verhältnis um, das die unveränderte Phase `Vision` im
selben Lauf zeigt (2.262 µs statt ruhig 716, Faktor 3,2), läge die Update-Phase des 8-Spieler-Laufs bei rund **0,5 ms je Tick** (ruhig
gemessener Ausgangswert: 3,26 ms) und der Anteil dieser Kostenstelle (Wegsuche und die drei Durchläufe, 589 µs belastet) bei rund
**0,2 ms**. Das ist eine Schätzung, kein Messwert; die ruhige Messung mit `scripts/perf-profile.ps1` steht aus und gehört dem Koordinator.

| Soll (ganzer Tick, alle Kostenstellen) | diese Kostenstelle nach den Änderungen |
|---|---|
| Mittel ≤ 1,0 ms | geschätzt 0,2 ms (Wegsuche rund 70 µs, Durchläufe rund 110 µs); der Rest der Update-Phase rund 0,3 ms |
| 99 % ≤ 3 ms | nicht belegbar unter Last; die Ticks mit Gruppensuchen (rund 2,5 % der Ticks haben zehn oder mehr Anfragen) bestimmen es |
| Maximum ≤ 8 ms | **nicht erreicht**: der teuerste Tick expandiert 1.891.288 Zellen (69 Suchen); bei geschätzt 50 ns je Zelle ruhig sind das rund 90 ms |
| keine Gen-2-Läufe, Allokation nahe 0 | Durchläufe 0 Byte; je Suche 158 Byte (das Ergebnis); die übrigen 76 KB je Tick liegen außerhalb |

## 6. Was bleibt, und warum es ohne Ergebnisänderung nicht weiter sinkt

1. **Viele lange, erfolgreiche Suchen in einem Tick.** Der teuerste Tick (25.198) hat 69 Anfragen mit 1.891.288 Zellen; die Ticks 28.144,
   28.174 und 28.210 je 25 Umwegsuchen mit je rund 41.000 Zellen (eine Gruppe, die alle 30 Ticks um stehende Einheiten herum neu plant und
   deren Umweg ankommt und benutzt wird). Insgesamt entfallen 76,6 von 76,8 Millionen Zellen auf Suchen, die ankommen (33.787 ohne und 19.102
   mit Blockern). Jede ist eine andere Frage (andere Startzelle oder andere Blocker), jede Antwort wird benutzt, und der zurückgegebene Weg
   hängt an der Reihenfolge, in der A* gleichwertige Zellen expandiert: Ein Verfahren, das weniger Zellen expandiert (bessere Heuristik,
   hierarchische Suche, eine Rückwärtssuche vom gemeinsamen Ziel für die ganze Gruppe), liefert bei gleich langen Wegen andere Wege. Exakt
   bleibt nur, jede Zelle billiger zu machen; das ist geschehen (Faktor 2,5), der Rest sind Sprungvorhersage und Halde.
2. **Suchen, deren Ziel in einem großen, getrennten Gebiet liegt.** Der Beweis aus §4.7 gibt nach 4.096 Zellen auf, der aus §4.5 flutet das
   Zielgebiet höchstens so weit, wie die Suche selbst gekommen ist. Liegt das Ziel in einem großen Gebiet, das vom Start getrennt ist (andere
   Seite eines Flusses ohne Übergang), flutet die Suche weiter bis zum Budget. Im 8-Spieler-Lauf kam das nicht mehr vor (0 Suchen am Budget);
   der Test `PathSearchAtTheBudgetMatchesTheOriginalSearch` enthält den Fall absichtlich.
3. **Die Durchläufe sind linear in der Zahl der mobilen Objekte** (im Gefecht rund 200, 1,4 bis 2,2 µs je Durchlauf belastet). Ein
   räumlicher Index würde sie unabhängig von der Objektzahl machen und wäre ergebnisneutral, braucht aber einen Haken an jeder Stelle, die
   eine Position ändert (`Entity.X`/`Y` werden an Dutzenden Stellen geschrieben; `Entity.cs` gehört in dieser Phase einer anderen
   Kostenstelle). Der Durchlauf über alle Objekte bleibt in `DeliverCollisions` für fahrende Objekte mit eigenen Kollisionsmodulen.
4. **`StationaryBlockersAhead` läuft für jede fahrende Einheit in jedem Tick**, nicht alle 30 (der Zähler `_blockerReplanTick` wird nur
   gesetzt, wenn Blocker gefunden wurden). Das zu ändern, wäre eine Verhaltensänderung.
5. **Versorgungsfahrzeuge ohne Ziel**: `ChinookAIUpdate` (482.176 Aufrufe, 13,8 µs belastet) und `SupplyTruckAIUpdate` (8,7 µs) sind jetzt
   nach `AIUpdateInterface` und `HordeUpdate` die teuersten Module des Gefechts. Ein Fahrzeug, das kein Lager findet, pendelt zwischen
   „sucht“ und „sammelt sich“ und durchläuft dabei alle zwei Ticks bis zu viermal alle Objekte (`SupplyNetwork.FindBestWarehouse`/`…Center`
   mit einer Modulsuche je Objekt, `SupplyMachine.Closest` mit `IsKindOf` als Zeichenkettensuche). Ergebnisneutral zu beschleunigen wäre das
   mit einer Liste der Docks und vorberechneten `KindOf`-Bits je Vorlage; nicht gemacht.
6. **Außerhalb dieser Kostenstelle**, aber in der Update-Phase jetzt sichtbar: `HordeUpdate` (118.420 Aufrufe, 65 µs je Aufruf belastet,
   8,5 % der Modulzeit), `PointDefenseLaserUpdate`, und die Arbeit des Schedulers samt Messung (30 % der Phase liegen nicht in Modulen).

## 7. Was ein verhaltensändernder Schritt bräuchte und brächte

Das Maximum von 8 ms ist mit exakten Mitteln nicht erreichbar, solange eine Gruppe von 70 Einheiten im selben Tick 70 Wege plant. Drei
Schritte würden es erreichen; jeder ändert Hashes und braucht nach `docs/handoffs/HANDOFF-PHASE19.md` Punkt 5 einen eigenen Commit mit neu
geschriebenem Manifest und Ergebnisvergleich:

1. **Ein Weg je Gruppe.** Einheiten, die im selben Tick denselben Befehl zum selben Ziel bekommen, teilen eine Suche (vom Gruppenschwerpunkt
   oder vom ersten Mitglied) und laufen ihr eigenes Stück bis zu diesem Weg. Wirkung im Profil: Tick 25.198 von 69 Suchen auf wenige; die
   Summe der Zellen sänke um schätzungsweise die Hälfte. Ändert die Wege aller Gruppenmitglieder außer einem.
2. **Zellbudget je Tick, Reihenfolge nach Id.** Die Welt gibt je Tick eine feste Zahl Zellen frei (etwa 100.000, bei geschätzt 50 ns ruhig
   rund 5 ms); Suchen, die nicht mehr hineinpassen, warten auf den nächsten Tick, die Einheit bleibt so lange stehen oder folgt ihrem alten
   Weg. Deterministisch, weil nach Zählgrößen. Ändert, wann Einheiten losfahren; braucht Zustand (wartende Anfragen) im Snapshot.
3. **Eine andere Suche** (zielgerichtete Heuristik aus einer vorberechneten Zonenkarte wie im Original, oder Jump Point Search): expandiert
   für denselben Weg ein Zehntel der Zellen, liefert bei Gleichstand aber andere Wege. Der größte Hebel für das Mittel der Wegsuche.

Dazu, ebenfalls verhaltensändernd: `StationaryBlockersAhead` nur alle paar Ticks (heute jeder Tick), und der Neuplan aller fahrenden
Bodeneinheiten bei jeder Grundrissänderung irgendwo auf der Karte (`_pathObstacles != navigation.ObstacleSum`) nur für Wege, die die geänderten
Zellen berühren.

## 8. Prüfungen

Alle auf dem Endstand `309dc7d` (Simulationsquellen), sofern nicht anders gesagt:

| Prüfung | Ergebnis |
|---|---|
| `SimTool reference-manifest check … --set quick` nach jedem Commit, der die Simulation ändert | jeweils 9 Läufe identisch (zehnmal) |
| `SimTool reference-manifest check … --set full` | **50 Läufe identisch**, in drei Teilen mit `--only` (23 Gefechte, Durchlauf aller Karten, Referenzmatch und Stresstest; 14 Missionen; 13 Missionen), je rund 21 Minuten bei drei Arbeitern; ein Lauf am Stück hätte unter der Last die Zeitgrenze überschritten |
| `SimTool input-manifest check … baselines/input-manifest.json` | identisch (auf `e0a93fa` nicht: siehe §4.2, behoben mit `309dc7d`) |
| `tests/CnCNew.Simulation.Tests` (Debug) | bestanden, darunter `SourceRules` und die vier neuen Tests |
| `tests/CnCNew.Simulation.Tests -c Release -- --long --content-dir …` | bestanden |
| `dotnet build CnCNew.slnx` nach Löschen von `bin`/`obj` des Stride-Clients | 0 Warnungen, 0 Fehler |
| Hashes der Profilläufe mit Beobachter und Sonde | 8 Spieler, 54.000 Ticks: `0A69FC72FF6975CD` / `78D32DCBCE8A99EB`; 18.000 Ticks: `72CF0F846E0D9AA6` / `5E446FE63C5BEF46`; `MD_GLA05`, 3.000 Ticks: `826CCD82C8536DC2` / `10330D7D179054FD` – in jedem Stand dieselben |

Die Manifestdateien unter `baselines/` sind unverändert. Nicht geprüft: eine ruhige Zeitmessung (§5.3), die übrigen Testprogramme der
Solution (keines davon berührt die geänderten Quellen anders als über die Simulation), der Client.

Neue Tests (`tests/CnCNew.Simulation.Tests/Tests.PerfMovement.cs`), jeweils gegen die im Test aufbewahrte frühere Implementierung, Eingaben
aus einem festen Startwert:

| Test | vergleicht |
|---|---|
| `FixedFastPathsMatchWideArithmetic` | Produkt, Quotient, Wurzel mit den 128-Bit-Formeln |
| `MovementScansMatchTheFullScan` | `AvoidLocalBlockers`, `StationaryBlockersAhead` und den gespeicherten Hüllkreis mit dem Volldurchlauf über alle Objekte |
| `PathSearchMatchesTheOriginalSearch` | 24.000 Wege (mit Wiederholungen, Grundrissänderungen, Blockern, `FindPathToGoal`) mit der ursprünglichen Suche |
| `PathSearchAtTheBudgetMatchesTheOriginalSearch` | Suchen am Budget, lange Umwege, Ziele, an denen niemand stehen kann, auf einer Karte mit 520 × 440 Zellen; prüft über die Sonde, dass die vorzeitigen Enden eintreten |
