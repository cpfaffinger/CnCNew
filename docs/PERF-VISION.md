# Leistung: Sicht und Sicht-Hash (Phase 19, Kostenstelle `Vision` / `VisionHash`)

Stand 6. Oktober 2026. Gegenstand sind die beiden Tickphasen `TickPhase.Vision` (`SimulationWorld.RevealVision`) und
`TickPhase.VisionHash` (Hash über die Sichtzellen), Dateien `src/CnCNew.Simulation/Terrain/VisionMap.cs` und
`src/CnCNew.Simulation/World/SimulationWorld.Vision.cs`. **Kein Ergebnis ist geändert:** die Zellbytes nach jeder Auffrischung, der
Wert des Sicht-Hashs, der Zustands-Hash, die Snapshots und alle Abfragen sind dieselben wie vorher; das unveränderte Referenzmanifest
besteht (§5). Die Hashkonstruktion selbst ist **nicht** angefasst; warum sie die verbleibende Hauptlast ist und was ein Prototyp einer
anderen Konstruktion bringt, steht in §7 als Entscheidungsvorlage.

**Alle Zeiten in diesem Dokument sind nicht ruhig gemessen.** Der Rechner war während der ganzen Arbeit von mehreren parallelen
Aufträgen belegt (Tickzeiten zwei- bis viermal so hoch wie im ruhigen Profil aus `docs/PERFORMANCE.md` §6). Verglichen wird deshalb
(a) über **deterministische Zähler** (Zellen, Bytes – unabhängig von der Last) und (b) über **gleichzeitig laufende** Werkzeugstände
(alt und neu teilen sich die Störung). Die ruhige Nachmessung mit `scripts/perf-profile.ps1` steht aus.

## 1. Ausgangslage

Ruhig gemessen am Ende von Phase 18 (`docs/PERFORMANCE.md` §6.2, Mittel je Tick in µs; die Sicht läuft nur jeden dritten Tick, eine
Auffrischung kostet also das Dreifache):

| Lauf | Zellen × Slots | `Vision` | `VisionHash` |
|---|---|---:|---:|
| Twilight Flame, 8 Spieler | 486×469 × 9 = 2.051.406 Bytes | 716 | 379 |
| Twilight Flame, 4 / 2 Spieler | × 5 / × 3 | 407 / 216 | 212 / 129 |
| `MD_USA05` | 527×521 × 11 = 3.020.237 | 921 | 562 |
| `MD_GLA05` | 589×589 × 13 = 4.509.973 | 1.532–1.670 | 822–841 |

Beide Phasen wuchsen mit Kartenfläche × Spielerzahl, nicht mit dem, was sich ändert: `BeginRefresh` ging über jedes Zellbyte jedes
Slots, `Reveal` prüfte je schauendem Objekt und Betrachter jede Zelle des umschließenden Quadrats gegen den Radius, `Hash` mischte
jedes Zellbyte. Dabei ändern sich je Auffrischung im Mittel nur **266 Zellbytes** (Twilight Flame, 8 Spieler, 18.000 Ticks) bzw.
**1.071** (`MD_GLA05`, 3.000 Ticks) – gemessen mit `--vision-probe` (§8).

## 2. Wie Sicht und Sicht-Hash definiert sind (unverändert)

- **Zustand:** je Slot (Systemspieler = Slot 0, dann die Spieler in Listenreihenfolge) ein Byte je Wegzelle: Bit 0 sichtbar, Bit 1
  erkundet, Bit 2 dauerhaft aufgedeckt (immer zusammen mit den beiden anderen). Gültige Werte sind 0, 2, 3 und 7; `Load` lehnt alles
  andere ab.
- **Auffrischung** (jeden `VisionIntervalTicks` = 3. Tick, nach den Spawns): „sichtbar“ wird vergessen (dauerhafte Zellen bleiben 7),
  dann deckt jedes lebende, nicht enthaltene Objekt mit positiver Reichweite einen Kreis auf (Zellmitte innerhalb des Radius) – für
  den Besitzer und dessen Verbündete, bei `REVEAL_TO_ALL` für alle, zusätzlich für Spione (`VisionSpiedBy`). Reichweite:
  `ShroudClearingRange` bzw. der Umkreis einer Baustelle, Modul-Override, `ShroudScalar`, Sichtbonus des Kampfplans.
- **Skriptaktionen** zwischen zwei Auffrischungen schreiben Zellen direkt (dauerhaft aufdecken und zurücknehmen, erkunden, verhüllen;
  ganze Karte oder Kreis).
- **Sicht-Hash:** `VisionMap.Hash()` – **eine** Kette aus `StateHash.Mix` (ein SplitMix64-Schritt je Wort) über alle Zellbytes aller
  Slots in Slot- und Zellreihenfolge, acht Bytes je Wort (erstes Byte niederwertig), je Slot abgeschlossen durch ein Wort aus den
  restlichen Bytes (null, wenn keine übrig sind). Der Wert steht als `visionHash` in der globalen Beschreibung und damit in jedem
  Zustands-Hash.

`VisionMap.Hash()`, `BeginRefreshReference()` und `RevealReference()` sind der wörtlich erhaltene alte Code. Sie definieren das
Ergebnis und sind das Orakel der Tests; `VisionMap.ReferenceMode` (Werkzeug: `--vision-reference`) lässt eine Welt vollständig damit
rechnen.

## 3. Was geändert wurde und warum es ergebnisneutral ist

Alles Neue ist Buchführung neben den Zellen: **kein Zustand**, nie gehasht, nie gespeichert, aus den Zellen wiederherstellbar (nach
`Load` und nach einem Referenzdurchgang wird der betroffene Slot einmal vollständig behandelt).

### 3.1 Sicht-Hash setzt die Kette am ersten geänderten Byte fort (`cd99d8a`)

Jeder Kettenschritt hängt vom vorigen ab. Ein geändertes Byte erzwingt deshalb, dass **jedes spätere Wort jedes späteren Slots** neu
gemischt wird; wiederverwenden lässt sich exakt nur der Teil der Kette **vor** der ersten Änderung. `VisionMap.CurrentHash()`:

1. hält den Wert, eine Kopie der Zellen im zuletzt gehashten Stand und den Kettenwert vor jedem 64. Wort (512 Zellbytes);
2. vergleicht die seit dem letzten Aufruf beschriebenen Zeilen je Slot mit der Kopie (`CommonPrefixLength`, hört beim ersten
   Unterschied auf); ohne Unterschied gilt der gehaltene Wert;
3. setzt sonst am Kettenwert vor dem ersten Unterschied fort und mischt von dort bis zum Ende.

Die Wörter werden als Ganzes gelesen statt Byte für Byte zusammengesetzt (auf Little-Endian dasselbe Wort; auf Big-Endian wird
getauscht – der Wert bleibt maschinenunabhängig). Das allein macht den Durchgang gut doppelt so schnell; die Kette selbst bleibt die
Grenze (ein Schritt wartet auf den vorigen: rund 13 Takte je acht Bytes, nicht parallelisierbar).

*Ergebnisneutral, weil:* der Wert ist per Konstruktion der von `Hash()` über dieselben Zellen (Kettenwert vor Wort *k* hängt nur von
den Bytes davor ab; die sind laut Vergleich unverändert). Geprüft nach jeder Auffrischung ganzer Läufe (`--vision-probe`: „always
equal“) und in den Tests. Nebenwirkung: Skriptaktionen und `Load` rechnen den Hash nicht mehr selbst; er wird berechnet, wenn der
Zustands-Hash ihn liest (mehrere Skriptaktionen eines Ticks kosten einen Durchgang statt je einen).

### 3.2 Auffrischung löscht und deckt zeilenweise in Läufen auf (`1de652f`)

- **Vergessen:** Der Durchgang über jede Zelle ändert eine Zelle nur, wenn sie sichtbar und nicht dauerhaft ist. Solche Zellen
  entstehen nur durch einen Kreis der Auffrischung oder durch `Load`; je Slot und Zeile wird deshalb die Spanne gemerkt, in die Kreise
  geschrieben haben (nach `Load`: alles), und nur dort gelöscht – acht Zellen je Schritt, mit der Zellregel des alten Codes für **jeden**
  Bytewert (nicht nur die gültigen).
- **Aufdecken:** In einer Zeile ist der quadrierte Abstand konvex in der Spalte; die Zellen im Kreis sind also **ein** Lauf um die
  Spalte, die dem Mittelpunkt am nächsten liegt. Eine Zeile näher am Mittelpunkt enthält den Lauf der vorigen, eine fernere liegt in
  ihm – die beiden Enden wandern nur nach außen oder nur nach innen (zwei Radiusprüfungen je Zeile plus die Schritte). Der Lauf wird am
  Stück geschrieben. Die Prüfung je Zelle ist dieselbe Ganzzahlrechnung wie vorher.

*Ergebnisneutral, weil:* dieselbe Zellmenge je Kreis, dieselbe Zellregel beim Vergessen; die Reihenfolge der Kreise ist für die Zellen
gleichgültig (es werden nur Bits gesetzt).

### 3.3 Betrachtertabellen ohne Allokation (`b3ef269`)

`RevealVision` legte je Auffrischung eine Liste und ein Array je Spieler, ein Array aller Spieler und einen LINQ-Abschluss an und
fragte jedes schauende Objekt per Namensvergleich nach `REVEAL_TO_ALL`. Die Tabellen werden jetzt gehalten und neu gefüllt (Bündnisse
werden weiterhin in jeder Auffrischung gelesen). *Ergebnisneutral:* dieselben Kreise für dieselben Betrachter. Zeitlich nicht messbar
(innerhalb der Streuung); es entfallen die Allokationen der Phase.

Der Commit hatte außerdem die Antwort auf `REVEAL_TO_ALL` als öffentliche Eigenschaft der Vorlage vorberechnet
(`ObjectTemplate.RevealsToAll`). **Das war ein Fehler:** das Eingangsmanifest beschreibt jeden öffentlichen Wert des Vorlagenkatalogs,
die beiden Kataloghashes (`templates`, `whole`) änderten sich, `input-manifest check` meldete von `b3ef269` bis `dfa9745` zwei
Abweichungen. Das Referenzmanifest war nicht betroffen. `6f15118` nimmt die Eigenschaft zurück (`ObjectTemplate.cs` ist wieder Byte für
Byte die Datei von `74ccdde`); die Auffrischung fragt wieder `IsKindOf("REVEAL_TO_ALL")` je schauendem Objekt. Lehre für die übrigen
Kostenstellen: **eine neue öffentliche Eigenschaft an einem Katalogtyp ist eine Manifeständerung**, auch wenn sie nur einen vorhandenen
Wert zwischenspeichert.

### 3.4 Ein Slot mit unveränderten Kreisen bleibt unberührt (`c4253ae`)

Die Kreise einer Auffrischung werden gesammelt (`Reveal` zwischen `BeginCollectedRefresh()` und `EndCollectedRefresh()`, so heißen die
beiden Aufrufe seit 3.6) und erst am Ende angewendet. Sind die Kreise eines Slots
dieselben wie in der Auffrischung davor **und** hat seither nichts anderes seine Zellen beschrieben (keine Skriptaktion, kein `Load`,
kein Referenzdurchgang), wird der Slot nicht angefasst. *Ergebnisneutral, weil:* „Sichtbares vergessen und dieselben Kreise wieder
zeichnen“ endet mit den Bytes, mit denen es begann. In Missionen gilt das für rund drei Viertel aller Slot-Auffrischungen (Seiten,
deren Objekte stehen).

### 3.5 Ein Slot mit wenigen Änderungen wird nur um diese herum neu gezeichnet (`425522f`)

Die Kreise werden sortiert (für die Zellen gleichgültig) und mit denen der letzten Auffrischung abgeglichen: *gegangen*, *geblieben*,
*gekommen*. Dann je Slot einer von zwei Wegen:

- **Flicken:** nur die Zellen der gegangenen Kreise vergessen; jeden gebliebenen Kreis innerhalb des Zellrechtecks jedes gegangenen
  Kreises neu zeichnen; die gekommenen Kreise zeichnen.
- **Neu zeichnen** (wie 3.2): alles Sichtbare des Slots vergessen, alle Kreise zeichnen.

Gewählt wird der Weg mit weniger Arbeit, gezählt in Zellen der Rechtecke, über die er ginge (ganzzahlig, deterministisch); hat etwas
anderes die Zellen beschrieben, wird immer neu gezeichnet. *Ergebnisneutral, weil:* beide Wege enden mit „sichtbar genau dort, wo ein
Kreis dieser Auffrischung oder eine dauerhafte Marke liegt“ – außerhalb der gegangenen Kreise wurde nichts vergessen, dort zeigen die
gebliebenen Kreise weiter; innerhalb wird alles neu gezeichnet, was hineinreicht. Die Wahl des Wegs kann das Ergebnis deshalb nicht
beeinflussen. Ein Kreis ganz neben der Karte deckt keine Zelle auf und zählt nicht.

### 3.6 Die schlichte Form bleibt erhalten (`dfa9745`)

`c4253ae` hatte `BeginRefresh()`/`Reveal()` selbst auf „sammeln“ umgestellt. Damit schrieb ein `Reveal` außerhalb der Welt seine Zellen
nicht mehr sofort – zwei Tests der Präsentation (`tests/CnCNew.Presentation.Tests/VisionTests.cs`), die eine `VisionMap` von Hand
füllen, schlugen in den Ständen `c4253ae` und `425522f` fehl. Produktcode war nicht betroffen (nur die Welt ruft `Reveal`, und die
schloss die Auffrischung ab); der Fehler fiel erst im Abschlusslauf der Präsentationstests auf. Seit der Korrektur gilt wieder der alte
Vertrag: `BeginRefresh()` vergisst sofort, `Reveal()` schreibt sofort; die gesammelte Form hat eigene Namen
(`BeginCollectedRefresh()`/`EndCollectedRefresh()`), und nur die Welt benutzt sie. Wer die schlichte Form benutzt, erzwingt für die
betroffenen Slots beim nächsten gesammelten Durchgang ein vollständiges Neuzeichnen. Falsche Verschachtelung wirft
`InvalidOperationException`. Der Weg der Welt ist derselbe Code wie in 3.5.

## 4. Tests

`tests/CnCNew.Simulation.Tests/Tests.VisionEquivalence.cs` (Commit `62787e0`, mit `425522f` und der Korrektur 3.6 erweitert), Orakel ist jeweils die alte
Berechnung über jede Zelle:

- **Zufallsfolgen auf sechs Kartenformen** (ganze Hashwörter mit Stützstelle genau am Ende; Rest von sieben Bytes; kleiner als zwei
  Wörter; eine Spalte; …), fester Startwert, je 700 Schritte: Auffrischungen mit Schauern, die bleiben, wandern, entstehen und
  verschwinden; Kreise auf, über und neben dem Kartenrand; Radius null, negativ, riesig, kleiner als eine Zelle; jede Skriptoperation;
  `Load` in frische und in laufende Karten; eine Auffrischung im Referenzmodus mittendrin; mehrere Änderungen vor einem Hashaufruf.
  Dazu die schlichte Form (sofort schreibend) zwischen gesammelten Auffrischungen und einzelne Kreise außerhalb jeder Auffrischung.
  Verglichen werden Zellbytes, voller Hash und gehaltener Hash. Der Test verlangt, dass jede Form alle drei Wege einer Auffrischung
  (unberührt, geflickt, neu gezeichnet) oft genommen hat.
- **Gehaltener Hash:** mischt nichts ohne Änderung, setzt hinter dem unveränderten Anfang fort (Bytezahlen geprüft); ein Slot mit
  denselben Kreisen bleibt unberührt, nach einer Skriptaktion wird er neu gezeichnet.
- **Zwei Welten mit denselben Befehlen über 1.500 Ticks**, eine im Referenzmodus: Einheiten, die fahren, sterben und entstehen,
  Baustellen, ein `REVEAL_TO_ALL`-Objekt, wechselnde Bündnisse zwischen drei Spielern, jede Aufdeck- und Verhüllaktion (auch für den
  Systemspieler), drei Snapshot-Wiederherstellungen (auch einen Tick nach einer Auffrischung). In jedem Tick gleich: Zustands-Hash,
  Zellbytes, Sicht-Hash, `EntitiesVisibleTo` aller Spieler; die Snapshots beider Welten sind bytegleich.

Gegenprobe: zwei absichtlich eingebaute Fehler (beschriebene Zeilen nicht gemeldet; Überlappungsprüfung um eine Zeile daneben) lassen
die Tests fehlschlagen.

## 5. Prüfungen gegen das Manifest

`SimTool reference-manifest check … --set quick` nach jedem der sieben Simulations-Commits (3.1 bis 3.6 und `6f15118`): jeweils
„9 runs identical“. Die Manifestdateien sind unverändert.

Abschluss (wegen der Rechnerlast in fünf Teilen mit `--only` gefahren, zusammen alle 50 Läufe):

| Prüfung | Stand `dfa9745` | Endstand `6f15118` |
|---|---|---|
| `reference-manifest check --set full` (9 + 12 + 4 + 18 + 7 Läufe) | 50 Läufe identisch | 50 Läufe identisch |
| `input-manifest check` | **2 Abweichungen** (Kataloghashes, siehe 3.3) | identisch |
| `reference-manifest check --only mission_MD_USA05 --verify-incremental` | 3.000 Ticks geprüft, 0 Abweichungen | 3.000 Ticks geprüft, 0 Abweichungen |
| `tests/CnCNew.Simulation.Tests` (Debug) | 353 bestanden | 353 bestanden |
| `tests/CnCNew.Simulation.Tests -c Release -- --long --content-dir …` | 367 bestanden | 367 bestanden |
| `tests/CnCNew.Presentation.Tests` | bestanden (an `c4253ae` und `425522f` zwei Fehlschläge, siehe 3.6) | bestanden |
| `tests/CnCNew.Game.Tests -c Release -- --only LiveMatchTests,HudTests` | bestanden | bestanden |
| sauberer `dotnet build CnCNew.slnx` (`bin`/`obj` des Clients gelöscht) | 0 Warnungen, 0 Fehler | 0 Warnungen, 0 Fehler |

Zwischen `dfa9745` und `6f15118` liegt eine einzige Ausdrucksänderung (vorberechneter Wahrheitswert gegen den Aufruf, der ihn
berechnet). Endhash und Kette der drei Messläufe sind in jedem Stand dieselben wie am Ausgangsstand
`74ccdde`: Twilight Flame 8 Spieler 18.000 Ticks `72CF0F846E0D9AA6` / `5E446FE63C5BEF46`, `MD_GLA05` 3.000 Ticks `826CCD82C8536DC2` /
`10330D7D179054FD`, `MD_USA05` 3.000 Ticks `1CB28E6AA923BDEF` / `0816C3438CB7F689`.

## 6. Vorher / nachher

### 6.1 Zähler je Auffrischung (deterministisch)

| Größe | Twilight Flame, 8 Spieler, 18.000 Ticks | `MD_GLA05`, 3.000 Ticks | `MD_USA05`, 3.000 Ticks |
|---|---:|---:|---:|
| Zellbytes der Tabelle | 2.051.406 | 4.509.973 | 3.020.237 |
| Kreise | 167,8 | 423,8 | 330,3 |
| geänderte Zellbytes (Mittel) | 266,0 | 1.070,6 | nicht erhoben |
| **gelöschte Zellbytes** vorher → nachher | 2.051.406 → 105.837 (5,2 %) | 4.509.973 → 50.961 (1,1 %) | 3.020.237 → 25.613 (0,8 %) |
| **Radiusprüfungen** vorher → nachher | 758.108 → 39.404 | 1.780.134 → 33.653 | nicht erhoben → 13.676 |
| **geschriebene Zellbytes** vorher → nachher | 593.692 → 580.715 | 1.380.146 → 329.382 | 681.611 → 131.678 |
| Kreise nicht voll gezeichnet | 8,0 | 384,5 | 298,1 |
| Slot-Auffrischungen unberührt / geflickt / neu | 20,4 / 7,7 / 71,9 % | 76,2 / 15,7 / 8,1 % | 77,8 / 18,4 / 3,7 % |
| **gemischte Hash-Bytes** vorher → nachher | 2.051.406 → 1.641.700 (80,0 %) | 4.509.973 → 3.514.582 (77,9 %) | 3.020.237 → 1.683.018 (55,7 %) |
| verglichene Bytes (neu) | 7.279 | 19.853 | 40.610 |

„Geschriebene Zellbytes vorher“ für `MD_USA05` ist der Stand nach 3.2 (gleiche Zellmenge wie vorher). Auffrischungen ganz ohne
geändertes Byte: 114 von 6.000 (Twilight Flame), 0 von 1.000 (`MD_GLA05`).

### 6.2 Zeiten (**nicht ruhig gemessen**; Mittel je Tick in µs; je Zeile liefen die genannten Stände gleichzeitig)

| Lauf | Stände (gleichzeitig) | `Vision` | `VisionHash` |
|---|---|---:|---:|
| Twilight Flame, 8 Spieler | **Ausgang `74ccdde` → Endstand `dfa9745`** | **3.028,2 → 330,4** | **1.500,4 → 558,0** |
| | Ausgang → 3.1 → 3.2 → 3.3 | 2.287,6 → 2.361,7 → 426,4 → 449,9 | 1.699,8 → 630,8 → 540,7 → 588,0 |
| | Ausgang → 3.3 → 3.4 | 2.528,7 → 440,6 → 379,7 | 1.945,2 → 618,4 → 592,3 |
| | 3.4 → 3.5 | 224,1 → 214,9 | 387,6 → 383,4 |
| `MD_GLA05` | **Ausgang → Endstand** | **7.984,3 → 498,6** | **4.024,9 → 1.221,4** |
| | Ausgang → 3.1 → 3.2 → 3.3 | 5.425,4 → 5.508,7 → 942,1 → 887,1 | 2.925,7 → 1.252,8 → 1.137,9 → 1.164,2 |
| | 3.3 → 3.4 | 1.020,0 → 639,6 | 1.180,7 → 1.150,3 |
| | 3.4 → 3.5 | 604,1 → 373,9 | 1.075,2 → 1.116,6 |
| `MD_USA05` | **Ausgang → Endstand** | **3.136,8 → 234,3** | **1.668,2 → 438,6** |
| | Ausgang → 3.3 | 5.092,9 → 1.004,5 | 3.465,5 → 725,1 |
| | 3.3 → 3.4 | 531,5 → 271,4 | 444,8 → 421,9 |
| | 3.4 → 3.5 | 465,3 → 301,6 | 605,1 → 541,8 |

Die Last schwankte zwischen den Läufen stark (mittlere Tickzeit des Ausgangsstands auf Twilight Flame zwischen 16,4 und 18,6 ms, ruhig
wären es rund 5); vergleichbar sind nur Werte derselben Zeile. Verhältnis nachher/vorher – direkt (fette Zeilen) und verkettet über die
Einzelschritte:

| Lauf | `Vision` direkt / verkettet | `VisionHash` direkt / verkettet |
|---|---:|---:|
| Twilight Flame, 8 Spieler | 0,11 / 0,16 (6- bis 9-mal weniger) | 0,37 / 0,30–0,35 (rund 3-mal weniger) |
| `MD_GLA05` | 0,062 / 0,064 (rund 16-mal weniger) | 0,30 / 0,40 (2,5- bis 3,3-mal weniger) |
| `MD_USA05` | 0,075 / 0,065 (13- bis 15-mal weniger) | 0,26 / 0,21 (4- bis 5-mal weniger) |

Die ganze mittlere Tickzeit im direkten Paar: Twilight Flame 18.442 → 15.237 µs, `MD_GLA05` 47.424 → 31.700 µs, `MD_USA05`
16.461 → 12.854 µs (bei dieser Last nur als Richtung zu lesen). Allokation je Tick im Twilight-Flame-Lauf: 81.950 → 80.168 Bytes (die
Tabellen aus 3.3).

### 6.3 Was bleibt (µs je Auffrischung)

Gemessen, **nicht ruhig** (direktes Paar, Endstand `dfa9745`; Mittel je Tick × 3):

| Lauf | Aufdecken | Hash | mittlere Tickzeit desselben Laufs |
|---|---:|---:|---:|
| Twilight Flame, 8 Spieler | 991 | 1.674 | 15.237 |
| `MD_GLA05` | 1.496 | 3.664 | 31.700 |
| `MD_USA05` | 703 | 1.316 | 12.854 |

**Hochrechnung** auf die ruhigen Ausgangswerte aus §1 mit den Verhältnissen aus 6.2 (eine Rechnung, keine Messung; der ruhige
Twilight-Flame-Lauf hatte zudem 54.000 statt 18.000 Ticks):

| Lauf | Aufdecken je Auffrischung | Hash je Auffrischung | beide je Tick |
|---|---:|---:|---:|
| Twilight Flame, 8 Spieler | ≈ 235–350 | ≈ 340–425 | ≈ 190–260 (vorher 1.095) |
| `MD_GLA05` | ≈ 285–320 | ≈ 740–1.010 | ≈ 340–445 (vorher 2.373–2.492) |
| `MD_USA05` | ≈ 180–210 | ≈ 350–445 | ≈ 180–220 (vorher 1.483) |

Gegen das Ziel der Phase (Tickmittel ≤ 1 ms bei 8 Spielern) ist die Sicht damit von rund 1,1 ms auf rund ein Fünftel bis ein Viertel
Millisekunde je Tick gefallen; davon ist mehr als die Hälfte der Hash.

## 7. Was nicht weiter sinkt, und warum

### 7.1 Der Hash: die Kette lässt nicht mehr zu (Entscheidungsvorlage)

Nach 3.1 werden je Auffrischung immer noch 56–80 % aller Zellbytes gemischt, obwohl sich im Mittel 266 bzw. 1.071 Bytes ändern. Das
liegt nicht an der Umsetzung, sondern an der Konstruktion: Die erste Änderung liegt fast immer im ersten Spieler-Slot (bei acht
Spielern nach einem Fünftel der Kette), und alles dahinter hängt von ihr ab. Es gibt keine exakte inkrementelle Form einer
SplitMix-Kette über alle Bytes: Der Schritt `h ← F(h + w + c)` ist nicht linear, zwei Kettenstücke lassen sich nicht zusammensetzen,
ohne das zweite neu zu rechnen. Der volle Durchgang selbst ist durch die Abhängigkeit der Schritte begrenzt und mit dem wortweisen
Lesen ausgereizt (keine Intrinsics, kein `unsafe`).

**Vorschlag (ändert den Wert des Sicht-Hashs und damit jeden Zustands-Hash – nicht Teil dieser Arbeit):** eine zweistufige
Konstruktion.

- Zeilenwert `R(slot, zeile)` = Kette über die Wörter der Zeile, begonnen mit `Mix(Seed, slot << 32 | zeile)`;
- Sicht-Hash = Kette über alle Zeilenwerte in Slot- und Zeilenreihenfolge.

Reihenfolgeempfindlich wie bisher, derselbe Mischer, dieselbe Güteklasse (erkennt Abweichung, widersteht keinem Angriff). Neu zu
rechnen sind nur die Zeilen, die sich wirklich geändert haben (die Buchführung aus 3.1 kennt sie schon), plus die Kette über die
Zeilenwerte (4.221 Schritte auf Twilight Flame mit 8 Spielern, 7.657 auf `MD_GLA05`).

Prototyp im Messwerkzeug (`--vision-probe`, nicht im Produkt; gemessen im selben, **nicht ruhigen** Lauf wie der gehaltene Hash des
Stands 3.1):

| Lauf | geänderte Zeilen je Auffrischung | Prototyp je Auffrischung | gehaltener Hash (3.1) im selben Lauf | Verhältnis |
|---|---:|---:|---:|---:|
| Twilight Flame, 8 Spieler, 18.000 Ticks | 183,4 von 4.221 | 127,4 µs | 1.423,1 µs | 11-mal weniger |
| `MD_GLA05`, 3.000 Ticks | 100,5 von 7.657 | 167,9 µs | 3.462,9 µs | 21-mal weniger |

Der Prototyp misst das Neu-Hashen der geänderten Zeilen und die Kette über alle Zeilenwerte, nicht das Finden der Zeilen (der Vergleich
mit der Kopie kostet heute 7.000–40.000 Bytes je Auffrischung, also wenige µs). Eine dritte Stufe (Slotwert über die Zeilenwerte des
Slots) würde auch die obere Kette auf die geänderten Slots begrenzen. Hochgerechnet auf ruhige Werte bliebe vom Hash etwa ein Zehntel
bis ein Zwanzigstel: rund 30–40 µs je Auffrischung auf Twilight Flame, rund 35–50 µs auf `MD_GLA05`.

Preis: Der Wert ändert sich, also **jeder** Hash des Referenzmanifests (alle 50 Läufe) – nach `docs/handoffs/HANDOFF-PHASE19.md` Punkt 4 ein
eigener Commit mit einer Ursache, neu geschriebenem Manifest und dem Nachweis, dass die *Ergebnisse* (Sieger, Endtick, Objektlisten)
aller Läufe gleich bleiben. Die Zellbytes und der Snapshot-Inhalt ändern sich nicht; eine Snapshot-Version muss nicht steigen, solange
der Sicht-Hash dort nicht gespeichert wird (er wird beim Laden neu berechnet). Empfehlung: zusammen mit den anderen wertändernden
Hash-Entscheidungen der Phase in einem Zug entscheiden, nicht einzeln.

### 7.2 Das Aufdecken im Gefecht: Einheiten fahren durch die eigene Basis

In Missionen ist das Aufdecken nach 3.4/3.5 nahe an „nur was sich ändert“ (0,8–1,1 % der Tabelle gelöscht, 19–24 % der früheren
Schreibarbeit). Im Gefecht mit acht KI-Spielern nicht: 72 % der Slot-Auffrischungen zeichnen den ganzen Slot neu, weil die fahrenden
Einheiten (Sammler, Armee) die großen, sich überlappenden Kreise der eigenen Gebäude schneiden und das Flicken dann mehr Zellen
berührte als das Neuzeichnen. Die Schreibarbeit (581.000 Zellbytes je Auffrischung für 266 geänderte) ist speichergebunden.

Was das exakt beheben würde, ist das Verfahren des Originals: **ein Zähler je Zelle und Slot** („wie viele Kreise decken diese Zelle“),
den ein wandernder Kreis nur in der Sichel zwischen alter und neuer Lage ändert; sichtbar ist, was Zähler > 0 hat oder dauerhaft ist.
Das ist ergebnisgleich machbar (Skriptaktionen und `Load` erzwingen wie heute einen vollen Abgleich des Slots), kostet aber zwei Bytes
je Zelle und Slot (4 MB auf Twilight Flame mit 8 Spielern, 9 MB auf `MD_GLA05`), eine Zuordnung alter zu neuer Kreise je Objekt und
eine eigene Testreihe. Nicht umgesetzt: Der Ertrag wäre geschätzt ein weiteres Drittel bis die Hälfte der verbleibenden Aufdeckzeit im
Gefecht (≈ 100–200 µs je Auffrischung ruhig), und der Hash (7.1) ist die größere verbleibende Stelle.

### 7.3 Nicht Teil dieser Kostenstelle

- `FogOfWarGrid.Update` (Spielschicht, `LiveGame.SyncAfterTick`) liest die Sicht des Menschen je Auffrischung Zelle für Zelle über
  `IsVisible`/`IsExplored` (zwei Aufrufe je Zelle). Seine Eingaben sind unverändert. `VisionMap.CellBytes(slot)` gibt jetzt die rohen
  Bytes heraus; damit ließe sich der Nebel zeilenweise und nur für beschriebene Zeilen übernehmen (Punkt 7 des Handoffs, anderer
  Auftrag).
- `Save`/`Load` der Sicht (Base64 je Slot) stehen nicht im Tick.
- Speicher: die Buchführung kostet ein weiteres Byte je Zelle und Slot (Kopie des gehashten Stands) plus 1/64 davon für die
  Kettenwerte und acht Bytes je Zeile und Slot für die Zeilenspannen – auf Twilight Flame mit 8 Spielern rund 2,1 MB, auf `MD_GLA05`
  rund 4,7 MB.

## 8. Werkzeug

`SimTool perf` und `perf-mission` (siehe `docs/PERFORMANCE.md` §3) drucken unter Sonde (a) jetzt zusätzlich:

- den Vergleich des gehaltenen Hashs mit dem vollen Durchgang auf dem Endzustand;
- die **Zähler** der Sichtarbeit (`VisionMap.Counters`: Auffrischungen, gelöschte und geschriebene Zellbytes, Kreise,
  Radiusprüfungen, Anteil unberührter / geflickter / neu gezeichneter Slots, Hashaufrufe, gemischte und verglichene Bytes). Sie sind
  deterministisch und damit auch auf belegter Maschine vergleichbar.

Neue Optionen:

- `--vision-probe`: nach jeder Auffrischung die Zahl der geänderten Zellbytes und Zeilen, der Vergleich gehaltener Hash gegen vollen
  Durchgang, und die Zeit des Prototyps aus 7.1. Die Sonde kostet Zeit; sie steht in „unattributed“, nicht in den Phasen.
- `--vision-reference`: die Welt rechnet Sicht und Hash mit dem alten Code über jede Zelle (für A/B-Läufe mit demselben Werkzeug).
  Endhash und Kette sind dieselben.
