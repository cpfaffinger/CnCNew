# Leistung: der Zustands-Hash (`TickPhase.Hash`)

Stand: Phase 19, Kostenstelle „Zustands-Hash“ (`docs/handoffs/HANDOFF-PHASE19.md` Punkt 4; Ausgangslage `docs/PERFORMANCE.md` §6/§7).
Ausgangscommit `74ccdde`, Branch `worktree-agent-ac725b90d346064f7`, 6. Oktober 2026.

**Die eiserne Regel dieser Arbeit:** kein Hashwert ändert sich. Jeder Tick-Hash, Endhash und Kettenhash jedes Laufs bleibt
bitgleich, und der inkrementelle Hash ist in jedem Tick gleich der vollen Neuberechnung. Alles, was hier bleibt, ist eine
andere *Berechnung* derselben Werte. Eine andere Hash-*Konstruktion* wird in §7 vorgeschlagen, nicht umgesetzt.

**Alle Zeiten in diesem Dokument sind nicht ruhig gemessen.** Der Rechner (AMD Ryzen 7 PRO 6850U, 16 logische Prozessoren)
war während der ganzen Arbeit von vier weiteren Agenten belegt; die Tickzeiten lagen zeitweise um das Zwei- bis Fünffache
über den ruhigen Werten aus `docs/PERFORMANCE.md` §6. Vergleiche stammen deshalb aus **gleichzeitig laufenden** Prozessen
(alter und neuer Stand nebeneinander, gleiche Last) und aus **Zählern**, die das Werkzeug deterministisch mitführt (Objekte,
Wörter, Bytes). Die Zähler sind exakt, die Zeiten sind Verhältnisse unter Last. Die ruhige Messung steht aus.

**Kurzfassung.** Geblieben sind drei wertneutrale Änderungen (§4.1–4.3): die Hash-Phase kostet unter gleicher Last noch
rund zwei Drittel (Gefecht mit 8 Spielern: Mittel 598 → 426 µs; `MD_USA05` 1.880 → 1.263; `MD_GLA05` 2.520 → 1.679), die
Allokation in der Phase ist weg. Zwei weitergehende Versuche sind gemessen und **nicht** übernommen (§4.4, §4.5): der eine
halbiert die Hash-Phase der Missionen noch einmal, nimmt dem Hash aber einen Schutz, auf den sich heute Stellen ohne
`MarkDirty` verlassen (zurückgenommen, als benannte Entscheidung für den Koordinator beschrieben); der andere war sicher,
brachte aber nichts. Nebenbei gefunden: **ein bestehender D8-Fehler im Ausgangsstand**, der einen Wert des
Referenzmanifests betrifft (§4.6, nicht behoben, weil die Korrektur diesen Wert ändert), und ein verdeckter, der behoben
ist. Was bleibt, ist Struktur: §6 und der Vorschlag in §7.

## 1. Wie der Hash heute gebaut ist

- `StateHash.Mix(hash, wort)` ist ein SplitMix64-Schritt: `z = hash + wort + Konstante`, zwei Xorshift-Multiplikationen,
  ein Xorshift. `HashStateWriter` faltet damit **sequentiell**: jeder Aufruf von `IStateWriter` ist ein Wort (ein Text:
  seine Länge, dann **ein Wort je Zeichen**). Feldnamen gehen nicht in den Hash, nur Werte und Struktur
  (`BeginObject` = `0xB1`, `EndObject` = `0xE1`, `BeginArray` = `0xB2`, `EndArray` = `0xE2`).
- Je Objekt: `HashEntity` = Faltung von `Entity.Describe` ab `StateHash.Seed`. Die Beschreibung ist: eigene Felder des
  Objekts (ID, Vorlagenindex, **Vorlagenname als Text**, Besitzer, Position, Status, …, Türme, Feuerstufen, Erfassung),
  dann für jedes Modul ein Rahmen (`BeginObject`, Index, **Modulname als Text**, **Modul-Tag als Text**, `BeginObject`),
  der eigene Zustand des Moduls (`SimModule.Describe`) und zwei `EndObject`.
- Welt: `_entitySum` ist die **Summe** (mod 2⁶⁴) der Objekt-Hashes; dazu Summen der Zeitgeber und der anstehenden Befehle.
  `FinishHash` faltet in jedem Tick den globalen Teil (Tick, ID-Zähler, Zufallsströme, Spieler mit Geld/Upgrades/…,
  verzögerter Schaden, Ablehnungen, Spawn-Warteschlange, Sicht-Hash) und die drei Summen.
- Inkrementell: `MarkDirty(entity)` trägt ein Objekt in die Dirty-Liste ein; `ComputeIncrementalHash` beschreibt genau
  diese Objekte neu und korrigiert die Summe um `neu − alt`. `ComputeFullHash` beschreibt alles (Konstruktor, Snapshot,
  Prüfung); `VerifyIncrementalHash` vergleicht beide.

Drei Eigenschaften dieser Konstruktion bestimmen alles Weitere:

1. **Die Faltung ist eine Abhängigkeitskette.** Jedes `Mix` braucht das Ergebnis des vorigen. Der Zustand nach einem
   Textstück hängt von jedem Wort davor ab (Position, Status, Module davor). Ein vorab berechneter „Zustand nach dem
   Modulnamen“ ist deshalb nur an derselben Stelle desselben Stroms mit demselben Vorlauf gültig – also praktisch nie.
   Vorab berechnen lassen sich die **Wörter**, nicht der Hashzustand (§4.2).
2. **Der Wecktick jedes Update-Moduls wird gehasht** (`nextWake`, Pflicht laut `SourceRules`). Ein Modul, das jeden Tick
   läuft, ändert damit in jedem Tick den Hash seines Objekts – auch wenn es sonst nichts tut. „Als geändert markiert,
   obwohl sich nichts geändert hat“ gibt es in dieser Konstruktion nicht: der Test `UpdateChangeWatch` prüft über
   149.155 echte Modul-Updates aller 183 Modultypen, dass **jedes** Update die Beschreibung seines Objekts verändert,
   und der Zähler `HashWork.Unchanged` zeigt 0,00–0,53 % neu beschriebene Objekte mit unverändertem Hash.
3. **Der Scheduler markiert jedes Objekt, dessen Modul gelaufen ist, als Ganzes.** Fast jedes Objekt trägt ein Modul, das
   jeden Tick läuft (`StealthUpdate`). Diese Markierung deckt deshalb nebenbei jede Stelle zu, die ein solches Objekt von
   außen ändert und `MarkDirty` vergisst (§4.4). Das ist kein Entwurf, aber es ist heute ein Teil der Sicherheit.

## 2. Messwerkzeug

`SimTool perf` / `perf-mission` (Befehle in `docs/PERFORMANCE.md` §3) geben in Sonde (b) zusätzlich aus:

- **Zähler des inkrementellen Hashs** (`SimulationWorld.HashWorkDone`, kein Zustand): neu beschriebene Objekte, gefaltete
  Wörter, Objekte mit unverändertem Hash; Allokation innerhalb der Hash-Phase.
- **Die zwei Hälften der Phase** auf dem Endzustand, in warmer Schleife: Wörter aufschreiben; falten in einer Kette; falten
  nebeneinander (`StateHash.FoldStreams`).
- **Je Modultyp**, was ein `Describe` schreibt und allokiert (gezählt, nicht gestoppt).
- Mit `--prefix-probe` (kostet Tickzeit, nicht für Zeitmessungen): alle 250 Ticks werden alle Objekte in zwei
  aufeinanderfolgenden Ticks aufgeschrieben und verglichen: Anteil der Wörter vor der ersten Änderung; die geänderten
  Objekte zerlegt in eigene Felder / Modulzustände / Rahmen; Objekte, an denen sich nur Weckticks bewegt haben; welche
  Modultypen hinter „genau ein Modul geändert“ stehen.
- Ein **Prototyp** für §7 (ein anderer Hash, wird nur gemessen): Modulname und Tag als je ein Wort.

## 3. Gemessene Anteile (Ausgangsstand)

Zähler über den ganzen Lauf, Mittel je Tick:

| Lauf | Objekte neu beschrieben je Tick | Wörter je Tick | Wörter je Objekt | davon eigene Felder | davon Modulzustand | davon Modulrahmen (Index, Name, Tag) | Hash unverändert |
|---|---:|---:|---:|---:|---:|---:|---:|
| Twilight Flame, 8 Spieler, 18.000 Ticks | 173,3 (89 % der Objekte) | 134.637 | 777 | 72 | 60 | 645 (83 %) | 0,17 % |
| `MD_USA05`, 3.000 Ticks | 1.311,8 (70 %) | 426.247 | 325 | 55 | 15 | 255 (78 %) | 0,00 % |
| `MD_GLA05`, 3.000 Ticks | 1.738,8 (77 %) | 553.584 | 318 | 53 | 13 | 252 (79 %) | 0,53 % |

Rund 75 % aller Wörter sind Textzeichen (Endzustand Twilight Flame: 138.755 von 184.273), fast alle aus Modulname und
Tag; der eigene Zustand aller Module zusammen ist 5–8 % der Wörter.

Woher die Zeit kommt:

| Anteil | Befund |
|---|---|
| **Falten** | Die Phase ist an die Latenz der einen Kette gebunden: 3,4–4,0 ns je Wort in einer Kette (isolierter Mikrobenchmark; unter Last 4–10 ns). Bei 777 Wörtern sind das rund 3 µs je Objekt – im Ausgangsstand drei Viertel der Phase im warmen Zustand. |
| **Beschreiben** | 0,8–1,2 µs je Objekt warm (Twilight Flame), davon der größte Teil die zwei Texte je Modul durch die Schreiber-Schnittstelle. Im Lauf kommt hinzu, dass jedes neu beschriebene Objekt samt aller Module (auch der schlafenden) aus kaltem Speicher gelesen wird. Nach §4.1/§4.2 ist das der größere Teil der Phase (§4.5). |
| **Zu viele Objekte „geändert“** | In den Missionen haben sich an **95,5 % (`MD_USA05`) bzw. 92,7 % (`MD_GLA05`)** der neu beschriebenen Objekte **nur Weckticks** bewegt (Twilight Flame 27,7 %). Dahinter steht fast allein `StealthUpdate` (92–94 % der Fälle „genau ein Modul geändert“ in den Missionen): das Modul trägt fast jedes Objekt, es läuft jeden Tick, und fast kein Objekt kann sich tarnen. In Twilight Flame teilen sich `StealthUpdate` und `GenerateMinefieldBehavior` (wartet jeden Tick auf sein Upgrade) je rund 8 %, dann `SupplyWarehouseDockUpdate` 4,5 %, `ChinookAIUpdate` 3 %. |
| **Globaler Teil** | 7–10 µs je Tick in den Missionen (10–12 Seiten), 0,3–0,5 % der Phase. Nicht angefasst. |
| **Allokation in der Phase** | 3.042 Bytes je Tick (Twilight Flame), 3.739 (`MD_USA05`) von 82–218 KB je Tick insgesamt: zur Laufzeit zusammengesetzte Feldnamen (`"start" + "X"`, `"tower" + index`), die der Hash-Schreiber nie ansieht. |

Die Vermutung des Handoffs „`MarkDirty` für jedes Modul-Update, ob sich etwas ändert oder nicht“ trifft den Kern nicht:
es ändert sich immer etwas (der Wecktick). Und der naheliegende Schluss „dann für diese Objekte weniger tun“ ist genau
die Stelle, an der diese Arbeit einen Hash verfälscht hat (§4.4).

## 4. Die Änderungen

### 4.1 Vier Objekte nebeneinander falten (`369fe0d`) – bleibt

`ComputeIncrementalHash` lässt die geänderten Objekte ihre Wörter in einen wiederverwendeten Puffer schreiben
(`WordStateWriter`: dieselben Wörter in derselben Reihenfolge, die `HashStateWriter` mischen würde) und faltet sie mit
`StateHash.FoldStreams`: vier Objekte gleichzeitig, jedes ab dem Startwert, jedes Wort durch dasselbe `Mix`. Ein Prozessor
wartet in einer Kette die meiste Zeit auf die Multiplikation; vier voneinander unabhängige Ketten überlappen sich.

**Wertneutral, weil** jeder Strom genau die Wörter in genau der Reihenfolge durch genau die Funktion bekommt wie zuvor;
nur die Reihenfolge, in der die Ströme *untereinander* vorankommen, ist anders, und sie teilen keinen Zustand. Die
Objekt-Hashes werden in Listenreihenfolge nacheinander in die Summe eingerechnet wie bisher. `ComputeFullHash` und
`VerifyIncrementalHash` benutzen weiter den einfachen `HashStateWriter`: die volle Berechnung bleibt ein unabhängiger
Richter über den neuen Weg. Tests: `FoldStreams` gegen den sequentiellen Hash für 400 zufällige Strommengen (leere Ströme,
weniger als vier); `WordStateWriter` gegen `HashStateWriter` für jede Aufrufart.

Keine Threads, keine Vektortypen, kein unsicherer Code: nur vier lokale Variablen in einer Schleife.

### 4.2 Die Wörter der Modulrahmen vorbereiten (`158aee7`) – bleibt

Index, Name und Tag eines Moduls ändern sich nach dem Anhängen nie. Die Welt hält ihre Wörter je Vorlage und Modulplatz
(einmal geschrieben von `Entity.DescribeModuleFrame`, der einzigen Definition des Rahmens); der Wort-Schreiber des
inkrementellen Hashs kopiert sie, statt zwei Zeichenketten je Modul durch die Schnittstelle zu schicken.

**Warum nicht der Hashzustand:** siehe §1, Eigenschaft 1 – der Zustand vor dem Rahmen ist bei jedem Objekt und in jedem
Tick ein anderer. Vorbereitet sind die kodierten Wörter; die Faltung sieht dieselben Wörter in derselben Reihenfolge.
**Wertneutral, weil** die vorbereiteten Wörter von derselben Routine stammen, die jeder andere Schreiber weiter Aufruf
für Aufruf bekommt (voller Hash, Snapshot, Werkzeuge). Test: die Wortbeschreibung jeder Vorlage der synthetischen Fixture
und aller 2.115 instanziierbaren Objekte der echten Daten, lebend und sterbend, faltet zum Hash der ausgeschriebenen
Beschreibung.

### 4.3 Keine Feldnamen für einen Schreiber bauen, der sie verwirft (`e4290ca`) – bleibt

`IStateWriter.KeepsNames` (Standard `true`; `false` für die beiden Hash-Schreiber). Fünf Stellen, die einen Namen zur
Laufzeit zusammensetzen (`WritePoint` der Geschosse und der Flugzeuge, `FireWeaponWhenDamagedBehavior`, Brückentürme,
späte Ablehnungsgründe), geben dem Hash den nackten Namen und bauen den echten nur für Schreiber, die Namen behalten.
**Wertneutral, weil** Namen nie in den Hash gehen; Werte und Reihenfolge sind unberührt. Test: jede verglichene
Beschreibung geht zusätzlich an einen Schreiber, der Namen behält; er muss zum selben Hash mischen und darf innerhalb
eines Objekts keinen Namen zweimal sehen (ein gekürzter Name, der ihn erreichte, würde kollidieren).

### 4.4 Ein Modul allein hashen, wenn sein Update dafür bürgt (`cf36a42`) – **zurückgenommen** (`791bbf5`)

**Die Idee.** Ein Update-Modul verspricht am Ende seines eigenen `Update`, dass sich außerhalb seines eigenen Zustands
nichts geändert hat (`StealthUpdate`, wenn der Status des Objekts derselbe ist; `GenerateMinefieldBehavior`, solange es
auf sein Upgrade wartet). Der Scheduler markiert dann nur das Modul; der Hash hält die Wörter des Objekts, ersetzt die
des einen Moduls und setzt die Faltung ab einem Prüfpunkt vor diesem Modul fort. Das Objekt wird **nicht** neu
beschrieben.

**Die Wirkung** (drei Stände gleichzeitig, starke Last; Hash-Phase Mittel / Median in µs): `MD_GLA05` 5.237,9 / 3.672,7
(Ausgang) → 4.084,8 / 2.854,3 (nach §4.3) → 1.860,2 / 1.346,7; `MD_USA05` 4.599,3 / 2.747,3 → 3.056,8 / 1.788,3 →
1.809,8 / 1.085,6; Twilight Flame unverändert (1.235 → 1.199). 90,6–92,4 % der Missionsobjekte gingen den Modulweg, die
gefalteten Wörter fielen von 426.247 auf 173.733 bzw. von 553.584 auf 177.145 je Tick.

**Geprüft** (dieser Stand, jeder Tick gegen die volle Neuberechnung): Schnellsatz (9 Läufe), die fünf D8-Missionen,
Referenzmatch und 21 weitere Missionen – 34 verschiedene Läufe. Eine Abweichung, in
`mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral`; sie tritt **im Ausgangsstand genauso auf** und gehört nicht zu
diesem Versuch (§4.6). Dem Versuch selbst ist in diesen Läufen kein Fehler nachgewiesen.

**Warum trotzdem zurückgenommen.** Wegen Eigenschaft 3 aus §1: bis dahin deckt die ganze Markierung des Schedulers jede
Stelle zu, die ein Objekt mit einem jeden Tick laufenden Modul von außen ändert und `MarkDirty` vergisst – das Objekt
wird ohnehin jeden Tick neu beschrieben. Das Versprechen *eines* Moduls kann nicht dafür bürgen, was *anderer* Code in
sein Objekt schreibt. Dass es solche Stellen gibt, ist belegt: eine wurde beim Lesen gefunden
(`StealthUpdate.MarkDetected`, aus dem Update des Detektors gerufen; unter dem Versuch wäre der Hash des entdeckten
Objekts veraltet), eine zweite ist der bestehende Fehler aus §4.6 – und der ist der Phase-18-Prüfung entgangen. Weitere
sind auf Pfaden zu erwarten, die kein geprüfter Lauf nimmt (Spielerbefehle, späte Missionsphasen). Der Versuch macht aus
jeder solchen verdeckten Stelle einen falschen Hash. Nach der Regel dieser Arbeit („im Zweifel markiert lassen“) ist das
keine Optimierung, sondern eine Entscheidung über das Risiko – und die gehört dem Koordinator: der Commit `cf36a42` ist
mit `git revert 791bbf5` wieder da.

**Richtigstellung zur Historie:** Die Nachricht des Revert-Commits `791bbf5` schreibt die Abweichung in
`mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral` dem Versuch zu. Das war zum Zeitpunkt des Reverts die Annahme und
ist falsch; die Gegenprobe am Ausgangsstand (§4.6) kam danach. Der Grund für die Rücknahme ist der oben genannte.

**Was davon bleibt (Commit `fix(sim)` nach dem Revert):** `StealthUpdate.MarkDetected` markiert das entdeckte Objekt
selbst. Kein Hashwert ändert sich dadurch (das Objekt ist durch sein jeden Tick laufendes Modul ohnehin markiert). Test
`Determinism D8: detection from another entity's update reaches the tick hash`: der Hash wird unmittelbar nach dem Aufruf
geprüft, bevor ein Tick ihn zudecken kann; ohne die Korrektur scheitert er („changed without MarkDirty: 1 Cloaked“).

**Was der Versuch zeigt:** der große Hebel der Missionen ist nicht das Falten, sondern das **Nicht-Beschreiben** – und das
ist erst ohne Vorbehalt zu haben, wenn die Markierung ohne den Schutz des Schedulers trägt (§7 C).

### 4.5 Die Faltung hinter einem verglichenen, unveränderten Anfang fortsetzen – **gemessen, nicht übernommen**

Die sichere Variante derselben Idee, ohne Versprechen: jedes oft beschriebene Objekt wird weiter **ganz beschrieben**
(der Scheduler markiert wie immer); sein Anfang (eigene Felder, Zustände der vorderen Module, die Wörter dazwischen) wird
**Wort für Wort** mit einer gehaltenen Kopie verglichen, und nur wenn er gleich ist, setzt die Faltung an einem Prüfpunkt
dahinter fort. Wertneutral nach Konstruktion (die Faltung läuft von links nach rechts; ein gleicher Anfang hat denselben
Faltungszustand), keine neue D8-Fläche. Gebaut, getestet (jeder Tick geprüft), der Schnellsatz mit `--verify-incremental`
lief für die erste Fassung ohne Abweichung (60.896 Ticks). Der Quelltext ist nicht im Branch.

Gemessen (gleichzeitig mit dem Stand nach §4.3): die gefalteten Wörter fielen in `MD_GLA05` von 553.584 auf 158.576–203.703
je Tick, in `MD_USA05` von 426.247 auf 162.027–215.274 (66,7–98,1 % der Objekte fortgesetzt, 207–249 Wörter Anfang im
Mittel) – **die Zeit aber nicht verlässlich**: `MD_USA05` 1.783 → 1.288 und 1.498 → 1.458 µs; `MD_GLA05` 1.913 → 1.641,
2.004 → 2.482 und 1.946 → 1.979–2.141 µs in drei Messungen; Twilight Flame 2–9 % **langsamer** (524 → 562–571, 585 → 598).
Nach §4.1 kostet ein Wort im Falten noch rund 1,1 ns; der Vergleich des Anfangs und die zusätzlich berührten
Speicherzeilen kosten etwa so viel, wie die gesparte Faltung bringt. Der Rest der Phase ist das Beschreiben, und das
bleibt. Nicht übernommen: Komplexität ohne belegten Gewinn.

### 4.6 Fund: ein bestehender D8-Fehler im Ausgangsstand (`PauseCountdown`) – **nicht behoben, gemeldet**

`SpecialPowerModuleBase.PauseCountdown` (gerufen von den Skriptaktionen `NAMED_STOP_SPECIAL_POWER_COUNTDOWN` /
`NAMED_START_SPECIAL_POWER_COUNTDOWN` und von `UnpauseSpecialPowerUpgrade`) ändert `pausedCount`, `pausedOnFrame` und
`availableOnFrame`, ohne das Objekt zu markieren. Am **Ausgangscommit `74ccdde`** meldet
`reference-manifest check --set full --only mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral --explain-incremental`:

> INCREMENTAL HASH MISMATCH in 1 of 3000 verified ticks; first: after tick 2100 (690 objects): Incremental state hash
> `15DC6118968B6B1A` differs from the full hash `C9B158ECE41535CA` at tick 2101 (changed without MarkDirty: 659
> `SupW_AmericaParticleCannonUplink`) – `modules.[3 SpecialAbility].state.availableOnFrame`: 7201 → 9301,
> `pausedCount`: 1 → 0.

Folgen, wie in `docs/DETERMINISM-FINDINGS.md` §6 beschrieben: der Wert dieses Laufs im Referenzmanifest (Kette
`CC877D4FC06A1DF9`, Endhash `69721DF608A6C803`) ist der Hash eines veralteten Zwischenstands; der Lauf hasht anders,
sobald etwas den vollen Hash berechnet (im Prüfmodus: Kette `E7EAD89208DD9861`). Ohne Prüfmodus besteht der Lauf das
Manifest – deshalb ist es bisher nicht aufgefallen; die Phase-18-Prüfung lief je Karte 1.500 Ticks, der Fehler tritt bei
Tick 2.100 auf.

Die Korrektur ist eine Zeile (`world.MarkDirty(Entity)` am Ende von `PauseCountdown`) und **ändert genau diesen
Referenzwert**: Kette `ADCCB4A1B93CBAD7`, Endhash `5E436B3BD943DFC2`, Snapshot-SHA-256 `5C52347C…6426E`; der inkrementelle
Hash ist dann in allen 3.000 Ticks gleich dem vollen, und die übrigen 49 Läufe des Manifests bleiben identisch (mit der
Korrektur gemessen: 20 + 21 + 8 Läufe identisch, 718.191 Ticks ohne Abweichung geprüft). Sie ist **nicht im Branch**: diese
Arbeit durfte keinen Hashwert und das Manifest nicht ändern. Empfehlung: eigener Commit des Koordinators mit dem neu
geschriebenen Wert dieses einen Laufs, wie bei `99a02fe`/`df523e4`; ein Test dafür (Aufruf von `PauseCountdown`,
`VerifyIncrementalHash` unmittelbar danach) scheitert heute und besteht mit der Zeile.

## 5. Vorher/nachher (Stand des Branches)

**Nicht ruhig gemessen.** Hash-Phase, µs je Tick, Mittel / Median; beide Stände einer Zeile liefen gleichzeitig, sonst
nichts von dieser Arbeit. Das ist die Messung mit der geringsten Fremdlast des Tages (der Ausgangsstand lag dabei 11–13 %
über seinen ruhigen Werten aus `docs/PERFORMANCE.md` §6.2: 530,8 / 1.681,2 / 2.271,3 µs).

| Lauf | Ausgang `74ccdde` | Stand des Branches `2f815d7` | Faktor (Mittel) | Tick ohne die ersten 300, Mittel |
|---|---:|---:|---:|---:|
| Twilight Flame, 8 Spieler, 18.000 Ticks | 598,1 / 647,0 | 425,8 / 456,2 | 1,40 | 7.841 → 7.495 |
| `MD_USA05`, 3.000 Ticks | 1.880,1 / 1.798,2 | 1.263,4 / 1.215,7 | 1,49 | 8.694 → 8.131 |
| `MD_GLA05`, 3.000 Ticks | 2.520,2 / 2.464,6 | 1.679,1 / 1.638,4 | 1,50 | 20.007 → 20.580 |

(Die Tickzeiten der letzten Spalte enthalten die Arbeit der anderen Agenten an anderen Kostenstellen nicht; ihre Streuung
zwischen zwei gleichzeitigen Prozessen ist größer als der Gewinn der Hash-Phase in `MD_GLA05`.)

Dieselben Paare unter stärkerer Last (frühere Messungen, jeweils gleichzeitig, „nachher“ = `e4290ca`): Twilight Flame
847,1 / 917,4 → 584,5 / 623,2 und 1.736,1 / 1.233,3 → 1.235,4 / 832,2; `MD_USA05` 2.288,9 / 2.203,9 → 1.497,9 / 1.441,9
und 4.599,3 / 2.747,3 → 3.056,8 / 1.788,3; `MD_GLA05` 3.057,9 / 2.991,4 → 2.003,7 / 1.965,7 und 5.237,9 / 3.672,7 →
4.084,8 / 2.854,3 – Faktoren 1,3 bis 1,5. Einzelschritte (gleichzeitig mit dem Vorgänger): Twilight Flame 9.000 Ticks
1.436,9 → 1.275,2 (§4.1) → 978,2 (§4.2) → 875,0 (§4.3, eigener Lauf); `MD_USA05` 1.500 Ticks 3.176,2 → 2.515,9 → 2.097,6
→ 1.994,0.

Auf die ruhigen Ausgangswerte übertragen wären das rund 380 / 1.130 / 1.510 µs – **eine Hochrechnung, keine Messung**.

Stresstest (`SimTool stress`, 216.000 Ticks, „Run 1“, µs je Tick, abwechselnd Ausgang / Branch): 97,5 / 85,5 – 112,4 / 50,8
– 62,8 / 54,7. Endhash `56882F763EABEF67`, Kette `B8E663493F2C73D5` in allen sechs Läufen. Die Streuung zwischen zwei
Läufen desselben Stands ist so groß wie der Unterschied; eine Verschlechterung ist nicht erkennbar.

Zähler (exakt, vom Rechner unabhängig):

| Lauf | Objekte je Tick | Wörter gefaltet je Tick | Allokation in der Hash-Phase, Bytes je Tick vorher → nachher |
|---|---:|---:|---:|
| Twilight Flame, 8 Spieler, 18.000 Ticks | 173,3 | 134.637 (unverändert) | 3.042 → 26 |
| `MD_USA05`, 3.000 Ticks | 1.311,8 | 426.247 (unverändert) | 3.739 → 209 |
| `MD_GLA05`, 3.000 Ticks | 1.738,8 | 553.584 (unverändert) | (nicht erhoben) → 209 |

Die verbleibende Allokation ist das einmalige Wachsen des Wortpuffers. Die Allokation des ganzen Ticks sinkt entsprechend
(Twilight Flame 81.903 → 77.881 Bytes je Tick).

Warme Schleife auf dem Endzustand (unter Last, deshalb nur das Verhältnis): eine Kette 4,1–10 ns je Wort, nebeneinander
1,1 (isolierter Mikrobenchmark) bis 2,2–4,5 ns je Wort; Aufschreiben 1,5–2,1 → rund 1,0 ns je Wort mit vorbereiteten
Rahmen.

Endhash und Kette sind in jedem dieser Läufe die des Ausgangsstands: Twilight Flame 18.000 Ticks `72CF0F846E0D9AA6` /
`5E446FE63C5BEF46`, `MD_USA05` `1CB28E6AA923BDEF` / `0816C3438CB7F689`, `MD_GLA05` `826CCD82C8536DC2` /
`10330D7D179054FD`.

## 6. Was bleibt, und warum es in dieser Konstruktion nicht weiter sinkt

| Rest | Größe | Warum er bleibt |
|---|---|---|
| **Beschreiben** jedes markierten Objekts mit allen Modulen | Missionen: 1.300–1.700 Objekte je Tick, von denen sich an 93–96 % nur ein Wecktick bewegt hat; nach §4.1/§4.2 der größere Teil der Phase | Wer nicht alles liest, sieht nicht, was eine Stelle ohne `MarkDirty` geändert hat (§4.4). Ohne verlässliche Markierung je Modul oder Feld geht es nicht anders. |
| **Falten** aller Wörter eines markierten Objekts | 318–777 Wörter je Objekt, davon 78–83 % Modulrahmen, die sich nie ändern | Die Rahmen stehen zwischen den Zuständen in derselben Kette. Den Anfang zu überspringen ist möglich, bringt nach §4.5 aber nichts mehr. |
| Faltung selbst | rund 1,1 ns je Wort nebeneinander (isoliert) | Vier Ketten lasten den einen Multiplizierer des Kerns aus; acht Ketten waren im Mikrobenchmark nicht schneller. Vektorbefehle sind im Kern verboten (`SourceRules`) und haben keine 64-Bit-Multiplikation. |
| Globaler Teil | 7–10 µs je Tick | 0,3–0,5 % der Phase. |

Geprüft und verworfen, außer §4.4 und §4.5:

- **Module mit leerem `Describe` überspringen:** 2–12 % der Module; bräuchte eine erklärte Eigenschaft je Modulklasse.
  Zu wenig Ertrag für eine weitere Stelle, die stimmen muss.
- **Acht statt vier Ketten:** im Mikrobenchmark gleich schnell.

## 7. Vorschlag für den Koordinator: eine andere Konstruktion (nicht umgesetzt)

Nach Handoff Punkt 4 ist das eine eigene, benannte Entscheidung. Die Messung sagt: der Sollwert der Phase 19 (Tick im
Mittel ≤ 1,0 ms) lässt für den Hash, wenn man ihm ein Zehntel des Ticks zugesteht, rund 100 µs; mit der heutigen
Konstruktion bleiben auf ruhigem Rechner geschätzt 380 µs im Gefecht mit 8 Spielern und 1,1–1,5 ms in den Missionen (§5).
Der Rest ist Struktur, nicht Code. Drei Teile, in der Reihenfolge ihres Ertrags für die Missionen **umgekehrt** zur
Reihenfolge ihres Risikos:

**A. Modulrahmen als ein Wort, Objekt-Hash aus Teil-Hashes** (ändert jeden Hashwert, sonst nichts).

- Je Vorlage und Modulplatz ein vorab berechneter **Rahmenschlüssel** (die heutige Faltung von Index, Name, Tag, einmal).
- `Modul-Hash = Fold(Mix(Mix(Seed, Objekt-ID), Rahmenschlüssel), Zustandswörter des Moduls)`.
- `Objekt-Hash = Fold(Fold(Seed, eigene Felder), Modul-Hash₀ … Modul-Hashₙ₋₁)` (Reihenfolge bleibt erhalten).
- Wirkung auf die Wortzahl (gemessen mit dem Prototyp-Schreiber des Werkzeugs, Namen als je ein Wort): 254 statt 787
  Wörter je Objekt (Twilight Flame), 110 statt 294 (`MD_USA05`), 107 statt 302 (`MD_GLA05`). Mit Teil-Hashes entfallen
  zusätzlich die übrigen Rahmenwörter: rund 150 (72 eigene + 60 Zustand + 17 Modul-Hashes) bzw. 76 und 72.
- Das senkt das **Falten** auf ein Fünftel bis ein Viertel und das Aufschreiben um die Rahmen. Das **Beschreiben** der
  Module bleibt. Hochrechnung aus Wörtern × 1,1 ns (isoliert gemessen), **keine Messung**: Twilight Flame rund 173 ×
  (0,3 µs Beschreiben + 0,17 µs Falten) ≈ 80 µs je Tick; Missionen 1.300–1.700 × (0,3–0,5 µs + 0,08 µs) ≈ 0,5–1,0 ms.
  Die Zeiten des Prototyps im Werkzeug waren unter der Last dieses Rechners nicht verwertbar; die Wortzahlen sind exakt.

**B. Den Wecktick eines jeden Tick laufenden Moduls nicht als absolute Zahl hashen** (ändert jeden Hashwert und die
Bedeutung eines gehashten Felds).

- Ein Modul, das „im nächsten Tick wieder“ sagt, trägt keine Information außer „wach“: der Tick steht im globalen Teil.
  Gehasht würde „wach“ (ein festes Wort) statt `tick + 1`; ein späterer Wecktick weiter als Zahl. Der Snapshot behält die
  Zahl (das Wiederherstellen braucht sie).
- Dann sind die 93–96 % der Missionsobjekte, an denen sich nur Weckticks bewegen, **wirklich unverändert**. Für sich
  allein spart das noch nichts (sie werden weiter markiert und beschrieben); es ist die Voraussetzung für C.

**C. Markierung, die ohne den Schutz des Schedulers trägt** (ändert keinen Hashwert, ist aber der Umbau aus
`docs/DETERMINISM-FINDINGS.md` §13 Punkt 2).

- Heute markiert der Scheduler jedes Objekt, dessen Modul lief; das deckt fehlende `MarkDirty`-Aufrufe zu (§4.4, §4.6:
  eine verdeckte Stelle beim Lesen gefunden, eine nicht verdeckte ist im Ausgangsstand ein echter Fehler). Erst wenn jede Änderung eines gehashten Felds selbst markiert
  (Setter, oder ein Prüflauf, der jede Schreibstelle erreicht), darf ein Update melden „nichts geändert“, und der
  Scheduler darf darauf verzichten.
- Mit B und C zusammen fielen in den Missionen rund 1.200–1.650 der 1.300–1.700 Objekte je Tick ganz aus der Phase. Der
  Versuch in §4.4 hat das Ausmaß gemessen, ohne B (der Nachlauf hinter dem Modul wurde noch gefaltet): Faktor 2,5 bis 2,8
  gegenüber dem Ausgang unter Last. Mit B entfiele auch der Nachlauf.

**Was für A und B neu geschrieben werden müsste:** jeder Tick-, End- und Kettenhash des Referenzmanifests (50 Läufe), die
Snapshot-Hashes dort (der Snapshot trägt den Zustands-Hash), die Prüfpunkte in aufgezeichneten Replays (Replay-Format
+1), die goldenen Werte der Tests (Stresstest `56882F763EABEF67` / `B8E663493F2C73D5`, Referenzmatch, Missionswerte) und
die Referenzwerte in `CLAUDE.md` und den Dokumenten. **Was gleich bleiben muss und den Nachweis trägt:** Sieger, Endtick,
Siegtick, Befehlszahl, `commandStreamSha256`, abgelehnte Befehle und die Objektlisten jedes Laufs; das Eingangsmanifest.
Die Phasen 22 und 23 schreiben das Manifest ohnehin neu – dort kostet der Wechsel am wenigsten. C braucht kein neues
Manifest, aber den Prüfmodus auf jedem Pfad, den Spielerbefehle erreichen.

## 8. Abschlussprüfungen

Stand `2f815d7` (der Code-Stand des Branches; danach nur dieses Dokument), 6. Oktober 2026, Release-Werkzeug aus einer
Kopie des Ausgabeverzeichnisses:

| Prüfung | Ergebnis |
|---|---|
| `SimTool reference-manifest check <content> baselines/reference-manifest.json --set full` | **50 Läufe identisch** (2.092 s mit 6 Aufträgen nebeneinander) |
| dasselbe mit `--verify-incremental` | 718.191 Ticks geprüft; **49 Läufe ohne Abweichung**, darunter `MD_GLA05`, `MD_USA01` (3.677 Ticks), `MD_USA02`, `MD_GLA03`, `GC_LaserGeneral`, `MD_USA05` (je 3.000), der Referenzmatch (14.791), der Stresstest (216 Prüfpunkte) und die Gefechts-Durchsicht (192.000); **1 Abweichung** in `mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral`, identisch am Ausgangscommit (§4.6) |
| `--set quick` und `--set quick --verify-incremental` nach jedem der drei bleibenden Commits | 9 Läufe identisch; 0 Abweichungen in 60.896 Ticks |
| `SimTool input-manifest check <content> baselines/input-manifest.json` | identisch |
| `SimTool stress` | `56882F763EABEF67`, Kette `B8E663493F2C73D5` (sechs Läufe, §5) |
| Referenzmatch (im Manifest und im Test „retail match“) | FactionGLA bei Tick 14.490, wie im Manifest |
| `tests/CnCNew.Simulation.Tests` Debug | 353 bestanden, 0 gescheitert |
| `tests/CnCNew.Simulation.Tests -c Release -- --long --content-dir <content>` | 368 bestanden, 0 gescheitert (darunter der 216.000-Tick-Lauf und `SourceRules`) |
| `dotnet build CnCNew.slnx` nach Löschen von `bin`/`obj` von `src/CnCNew.Client.Stride` | 0 Warnungen, 0 Fehler |

Die Manifestdateien sind unverändert.

Nicht geprüft: die übrigen Testprogramme der Solution (`CnCNew.Game.Tests` mit seinen `DeterminismTests`), der Client,
ein zweiter Rechner, eine ruhige Messung.

Für den Koordinator bleiben damit drei Dinge zu entscheiden: der bestehende D8-Fehler aus §4.6 (eine Zeile, ein
Manifestwert), der zurückgenommene Versuch aus §4.4 (Faktor 2,5–2,8 statt 1,5 in den Missionen gegen den Schutz des
Schedulers) und die andere Konstruktion aus §7.
