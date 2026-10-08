# Erste Beobachtung zweier eingebauter KIs durch einen Menschen (8. Oktober 2026): vier Befunde, Ursachen, Korrekturen, Messung

Stand: 8. Oktober 2026, Branch `worktree-agent-a4e29cbcc29f6bab6` (Basis `main` `5abbeea`). Zum ersten Mal hat ein Mensch – der Projektinhaber – ein Gefecht zweier eingebauter
KIs (`SkirmishAi`) im Client angesehen (`scripts/demo-spectator.ps1`, 30 Hz, eine der fünf Standardpaarungen, Seed nicht notiert). Dieses Dokument nimmt seine vier Beobachtungen
einzeln: reproduziert? (Zahlen), Ursache (Datei, Symbol), Korrektur (Commit), gemessene Wirkung, was bleibt. **Niemand hat das Ergebnis im Fenster gesehen:** alles hier ist
headless gemessen; was nur ein Mensch bestätigen kann, steht in §6.

**Methode.** Ein ganzes Gefecht ist chaotisch; kein Befund stützt sich auf ein einzelnes. Gemessen wird mit `SimTool ai-census` (neu, §5): die 16 Paarungen des Referenzmanifests
(vier Karten × vier Paarungen) mit je 5 Seeds = 80 Gefechte = 160 Spieler-Gefechte, 25 Spielminuten Budget, 30 Hz; dazu `ai-survey` (16 × 20 Seeds = 320 Gefechte). „vorher“ ist
`main` (`5abbeea`), wenn nichts anderes dasteht. Die Stände heißen wie die Commits in §7: **B** (Produktion), **C1** (Zielplätze), **C2** (Einsteigen), **D** (Zusammensetzung).

| Stand | `ai-census`: entschieden von 80 | `ai-survey`: entschieden von 320 |
|---|---|---|
| `main` | 66 | 253 (Vorgabe des Auftrags; nicht neu erhoben) |
| nach B | 73 | 281 |
| nach C1 | 74 | 288 |
| nach C2 | 71 | 284 |
| nach D (Regel für beide Spieler) | 75 | 298 |

Die Unterschiede zwischen B, C1 und C2 liegen innerhalb der Streuung (die halbe Breite des 95-%-Intervalls für die Differenz zweier solcher Anteile ist rund 5 Punkte); der Schritt
von `main` zu B (8,8 Punkte) nicht, der von C2 zu D (4,4 Punkte) liegt an der Grenze. Belastbar für D ist der gepaarte Test in §4.3, nicht diese Spalte.

## 1. Beobachtung A: produzierte Einheiten stehen in der Kaserne

Bekannt, nicht Gegenstand dieses Auftrags (kein produziertes Fahrzeug und kein Soldat irgendeiner Fraktion fährt heraus; die Korrektur „Einheiten verlassen die Fabrik auf dem
Ausfahrtsweg“ liegt auf einem anderen Branch). Für alles Folgende heißt das: die eingebaute KI arbeitet heute mit Einheiten, die am `UnitCreatePoint` im Gebäude entstehen und von
dort zum Sammelpunkt gehen. Sobald jene Korrektur gemergt ist, sind die Zahlen dieses Dokuments neu zu erheben.

## 2. Beobachtung B: „Die China-KI baut keine Panzer, obwohl das Gebäude dafür steht“

### 2.1 Reproduziert – und nicht nur bei China

| Fraktion (Spieler-Gefechte) | mit Fahrzeugfabrik | Fabriken, die je ein Fahrzeug bauten | Infanterie | Fahrzeuge | Fahrzeuganteil | Blicke der Heeresproduktion auf eine Fabrik: Auftrag / „nicht bezahlbar“ |
|---|---|---|---|---|---|---|
| China (40) | 23 | **4** | 2.137 | **6** | 0,3 % | 8 / 28.388 |
| ChinaTankGeneral (20) | 18 | 14 | 1.342 | 57 | 4,1 % | 61 / 35.909 |
| America (40) | 36 | 31 | 3.991 | 161 | 3,9 % | 165 / 55.205 |
| AmericaAirForceGeneral (20) | 20 | 17 | 2.166 | 65 | 2,9 % | 69 / 43.589 |
| GLA (40) | 10 | 10 | 2.202 | 25 | 1,1 % | 25 / 25.514 |

Über alle Fraktionen endeten 188.605 Blicke auf eine Fabrik mit „nichts bezahlbar“ und 328 mit einem Auftrag. Abgelehnt wurde kein einziger Produktionsbefehl (keine Ablehnung
wegen Voraussetzungen, Warteschlange, Strom, `MaxSimultaneousOfType` oder Wissenschaft); Chinas Panzer brauchen keine Voraussetzung, die die KI nicht baut (Battlemaster, Gattling
und Dragon nur die Waffenfabrik); die Fabrik ist nicht blockiert. Die amerikanischen „Fahrzeuge“ vorher sind zu 73 % Sanitätswagen (das billigste, 118 von 161).

### 2.2 Ursache

`Skirmish/SkirmishAi.cs`, `ProduceArmy` (Stand `main`): jeder Produzent kaufte, in der Reihenfolge, in der die Gebäude entstanden sind, eine zufällige Einheit **unter denen, die das
Geld in diesem Augenblick deckt**. Die Kaserne steht zuerst (`PlanGoals`: Kaserne vor Fabrik), und ihre Infanterie ist die billigste Heereseinheit (Infanterie 300, Panzer ab 800). Bei
einem Einkommen von zwei Sammlern überschreitet der Kontostand den Preis eines Soldaten alle paar Sekunden und den eines Panzers nie: die Kaserne gibt jeden Credit aus, bevor die
Fabrik gefragt wird. Gespart wurde nie.

Maßstab Original (`MapTool script-library <content> --show "China N "`, normale Schwierigkeit): nach vier Rotgardisten der ersten Welle besteht jede Welle aus Fahrzeugen –
`China N 1ST Wave Veh` 1 Gattling + 2 Battlemaster, `2ND Wave Tanks` 2 Battlemaster + ECM + Gattling, `3RD Wave Tanks` 4 Battlemaster, `3RD Wave Artillery` 2 Inferno,
`4TH Wave Tanks` 2–4 Battlemaster + 2 Overlord. „Eine China-Armee hat Battlemaster und Gattlings“ ist dort Datum der Skripte (mit Geldgeschenken, `docs/SKIRMISH-AI-ORIGINAL.md` §1.4).

### 2.3 Korrektur: Regel „stehender Heeresauftrag“ (Commit `50718d7`, Test `7a29943`)

Die nächste Heereseinheit wird unter **allem** gezogen, was Kasernen und Fabriken mit Platz in der Warteschlange bauen könnten (Voraussetzungen erfüllt, jeder Einheitentyp mit
derselben Chance, **der Preis wird nicht angesehen**), und die KI spart dann auf genau diese Einheit; für das Heer wird nichts anderes gekauft, bis sie bezahlt ist. Ein Auftrag, der
60 s (`ArmyOrderPatienceFrames`, eigene Wahl) nicht bezahlt werden konnte, weicht einer Ziehung unter dem, was das Geld jetzt deckt – eine Wirtschaft, die stehen geblieben ist, gibt
ihr letztes Geld trotzdem aus (im Zensus 121 von 7.308 Aufträgen). Der Auftrag ist ein Feld im Zustand der KI (`_armyOrder`), neben ihren anderen; Zufall nur aus ihrem eigenen Strom.

### 2.4 Wirkung

| Fraktion | Fabriken, die bauen | Infanterie | Fahrzeuge | Fahrzeuganteil |
|---|---|---|---|---|
| China | 21 von 23 (vorher 4) | 1.033 | **300** (vorher 6) | 22,5 % |
| ChinaTankGeneral | 17 von 17 | 514 | 477 | 48,1 % |
| America | 34 von 35 | 1.282 | 491 | 27,7 % |
| AmericaAirForceGeneral | 20 von 20 | 870 | 266 | 23,4 % |
| GLA | 10 von 10 | 1.654 | 261 | 13,6 % |

China baut jetzt Battlemaster (84), Dragon (83), Gattling (74) und Truppentransporter (59). Die 16 Läufe des
Referenzmanifests (ein Seed): entschieden 12 → 12, der Ausgang wechselt in 7 (vier Patts kommen und gehen, drei Siegerwechsel).

**Was bleibt.** (a) Der Anteil der Infanterie bleibt hoch, weil die Kaserne früher steht als die Fabrik (Median der ersten Fabrik: Tick 4.725) und bis dahin allein gezogen wird. (b) Die Regel wählt blind
(bis D): auch Sanitätswagen, Flugabwehr gegen einen Gegner ohne Flugzeuge, leere Transporter. (c) **Nebenbefund, nicht behoben:** die GLA-KI stellt auf drei der vier Karten (Alpine
Assault, Bitter Winter, Death Valley) genau ein Gebäude fertig, die Kaserne, und nur auf Armored Fury Lager, Tunnel und Waffenhändler – in allen 30 Spieler-Gefechten dort, vorher wie
nachher. Darum „GLA mit Fabrik: 10 von 40“. Ursache nicht untersucht. (d) Die Angriffsschwelle zählt Köpfe (acht Einheiten); eine teurere Armee erreicht sie später.

## 3. Beobachtung C: „Drei Infanteristen draußen, einer steht in der Mitte, zwei laufen um ihn herum“

### 3.1 Der Detektor

`OrbitDetector` (`tools/CnCNew.SimTool/AiCensus.cs`): je Logikbild die Lage jeder beweglichen Bodeneinheit eines Spielers; einmal je Sekunde werden die letzten 10 s beurteilt. Eine
Einheit, die mehr als 150 Einheiten Weg zurücklegt und weniger als 20 vom Fleck kommt, ist eine Episode: **Kreisen** (`orbit`) bei mindestens zwei vollen Drehungen der
Laufrichtung, sonst **Hin und Her** (`shuffle`). Gezählt wird mit dem Ort – am Sammelpunkt der KI (`rally`), nahe dem eigenen Ziel anderswo, unterwegs zu einem fernen Ziel, ohne
Bewegungsauftrag – und getrennt nach Heer und Wirtschaft (Sammler, Dozer). `--orbits <datei>` schreibt je Episode Zustand und Nachbarn, `--watch <id>` verfolgt eine Einheit.

### 3.2 Reproduziert

Auf `main`: 359 Kreis-Episoden von Heereseinheiten in 80 Gefechten, davon **269 am Sammelpunkt, in 74 von 160 Spieler-Gefechten**, bei jeder Fraktion (unter den 359: Rotgardist 235,
Ranger 47, Raketensoldat 32 …). Das Bild ist immer dasselbe, hier das erste:

```
AlpineAssault_America-China, Seed 1, Tick 2580: ChinaInfantryRedguard#27 bei (1087, 596), Weg 250, vom Fleck 19,9, 2,7 Drehungen;
  Ziel (1098, 605) – der Sammelpunkt, 14,1 entfernt; Nachbarn: ChinaInfantryRedguard#23 steht bei (+11, +9), ChinaInfantryRedguard#32 läuft
```

Einer steht genau auf dem Sammelpunkt, die anderen laufen im Abstand der beiden Radien (rund 14) um ihn herum – was der Inhaber gesehen hat. Oft stehen mehrere Einheiten auf
demselben Punkt übereinander (auf `main` bis zu drei, nach B bis zu acht in einer Episode): wer keinen Ausweichschritt mehr findet, geht durch.

### 3.3 Erste Ursache: alle haben denselben Zielpunkt

Alles Produzierte bekommt denselben Punkt (`SetRallyPointCommand`, danach `AttackMoveCommand` der KI). `AIUpdateInterface.Move` (`Modules/BuiltIn/Phase5Modules.cs`): der letzte
Wegpunkt ist das Ziel selbst, angekommen ist, wer innerhalb von `max(CloseEnoughDist, ein Bildschritt)` liegt. Steht dort schon jemand, liegt der Wegpunkt in dessen Kreis; der
Schritt dorthin ist blockiert, `SimulationWorld.AvoidLocalBlockers` dreht ihn um 45° oder 90° – jedes Bild wieder, also im Kreis. Die Umplanung um stehende Einheiten
(`StationaryBlockersAhead` → `ReplanAround`) hilft nicht: einen Umweg, der ankommt, gibt es nicht, und ohne den bleibt der alte Weg.

**Was das Original tut** (gelesen, neu geschrieben, nichts kopiert): jede Einheit bekommt eine eigene Zielzelle. `AIInternalMoveToState::onEnter` (`GameLogic/AI/AIStates.cpp`)
schickt das Ziel durch `Pathfinder::adjustDestination` (`GameLogic/AI/AIPathfind.cpp`): eine quadratische Spirale um die gewünschte Zelle (rechts, hoch, zweimal links, zweimal
runter, dreimal rechts …, höchstens `MAX_CELLS_TO_TRY` = 400), die erste Zelle, die `checkDestination` annimmt – begehbar, nicht Zielzelle eines Verbündeten („Don't usurp your
allies goals“), nicht von einer stehenden Einheit besetzt, die man nicht überfahren kann („Don't move to an occupied cell“) –, wird mit `Pathfinder::updateGoal` reserviert; findet
sich keine, `snapClosestGoalPosition`. Gruppen bekommen zusätzlich versetzte Ziele (`AIGroup::computeIndividualDestination`, `AIGroup.cpp`: der Abstand zur Gruppenmitte, auf
sechs Radien begrenzt, dann ebenfalls `adjustDestination`).

**Korrektur (Commit `3816445`, Test `ccdc12b`):** `SimulationWorld.TryFreeGoalSpot` (`World/SimulationWorld.GoalSpots.cs`), ohne Reservierungstabelle: eine stehende Einheit
beansprucht den Platz, auf dem sie steht, eine laufende den, zu dem sie unterwegs ist (`Entity.MoveTargetX/Y` – Zustand, den jeder Snapshot schon trägt, **kein neuer Zustand**).
Ist das eigene Ziel von einer stehenden Einheit besetzt oder läuft eine mit kleinerer Kennung dorthin, nimmt die Einheit den ersten Platz auf derselben Spirale, den niemand
beansprucht und der in gerader Linie begehbarer Zellen vom Ziel liegt. Einheiten eines Feindes beanspruchen nie (ein Panzer, der auf feindliche Infanterie befohlen ist, fährt
weiter). Die KI schickt eine Einheit, die innerhalb von elf Zellen des Sammelpunkts zur Ruhe gekommen ist, nicht erneut hin (`SkirmishAi.GatherRadius`).

*Für den Merge (das Paket R3-OBSERVERS bearbeitet dieselbe Datei):* der Eingriff in `AIUpdateInterface` ist **ein Block von sechs Zeilen** am Anfang der Bildentscheidung von `Move`,
unmittelbar vor `if (!_pathValid || …) PlanPath(…)`, und **ein Helfer** `GoalSpotCheckDue` mit zwei Konstanten vor `BlockerReplanFrames`. Gefragt wird nur bei einem schlichten
Bewegungsauftrag (kein Dock, keine Bauaufgabe, kein Einsteigeziel, kein Zielobjekt, kein Rückweg, kein Opfer im Visier), nur innerhalb von sechs Zellen des Ziels und nur in jedem
15. Logikbild der Welt, versetzt um die Kennung. Abweichungen vom Original: gefragt wird nahe am Ziel statt bei der Auftragserteilung (so fällt auch ein Platz auf, der unterwegs
besetzt wurde); die Spirale liegt um den genauen Zielpunkt, nicht um die Zellmitte; „erreichbar“ ist die gerade Linie, nicht die Zonentabelle.

**Bildkonvention (`docs/RATE-INVARIANCE-CONSOLIDATION.md` §1):** die Frage fällt in die eine Entscheidung je Logikbild; der Rhythmus ist die Bildnummer der Welt
(`Time.FramesIn(Tick)`), also bei jeder Rate dasselbe Bild; eine Einheit, die bei über 30 Hz gerade den letzten Schritt ihres Weges zurücklegt, zählt wie beim 30-Hz-Schritt als auf
ihrem Ziel stehend (Regel 3). Der Test schickt sechs Einheiten auf einen Punkt: alle kommen nach 193 Bildern an, genau eine steht auf dem Punkt, die anderen auf den Nachbarzellen
(10,10), (−10,0), (−10,10), (10,0), (0,10), niemand steht in einem anderen – und Lage, Blickrichtung und Auftragszustand nach **jedem** Logikbild sind bei 30, 60 und 120 Hz
bitgleich. Ohne die Korrektur laufen zwei der sechs nach 20 s noch.

### 3.4 Zweite Ursache: wer einsteigen soll, läuft um sein Fahrzeug herum

Nach der ersten Korrektur blieben Fälle am Sammelpunkt mit demselben Bild – in der Mitte aber ein **Fahrzeug** (Humvee, Truppentransporter; seit B baut die KI sie), und die
Laufenden hatten den Befehl einzusteigen (`SkirmishAi.UseTransports` → `MoveAndEnterCommand`). Zahlen von `main`: in den 20 KI-Läufen des Referenzmanifests **1.523
Einsteigebefehle und 130 Einstiege**; der Fall „heal inside ChinaVehicleTroopCrawler“ der Raten-Suite war dort „unmeasured“, weil der Rotgardist nie hineinkam.

Ursache: das Fahrzeug war für den Einsteiger ein Hindernis wie jede andere Einheit (`SimulationWorld.Collision.cs`: `AvoidLocalBlockersCore` und die beiden
`StationaryBlockersAhead…` nahmen nur das Zielobjekt einer Annäherung aus), und aufgenommen wurde er erst, wenn seine Bewegung **beendet** war – auf dem Mittelpunkt des Fahrzeugs,
den er nicht erreichen kann (`AIUpdateInterface.UpdateEnter`).

Original: `AIEnterState::onEnter` (`AIStates.cpp`) sagt dem Pfadfinder, das zu betretende Objekt zu ignorieren (`ai->ignoreObstacle(goal)`), und der Container nimmt auf, wer mit
ihm zusammenstößt und ihn als Einsteigeziel hat (`OpenContain::onCollide`, `GameLogic/Object/Contain/OpenContain.cpp`).

**Korrektur (Commit `b93ba6c`, Test `2259f70`):** `AIUpdateInterface.IgnoredObstacle` (Zielobjekt oder Einsteigeziel) ist, was die drei Kollisionsabfragen ausnehmen; `UpdateEnter`
nimmt eine Einheit auf, sobald ihr Kreis den des Containers berührt, geprüft nach einem ganzen Bildschritt. Ein Gebäude behält die alte Regel (der Weg endet an seiner Wand; sein
Umkreis reicht weit über einen rechteckigen Grundriss hinaus). *Für den Merge:* eine Eigenschaft nach `GoalObject`, elf Zeilen in `UpdateEnter`, dreimal ein Wort in
`SimulationWorld.Collision.cs`. Der Test: drei Einsteiger aus drei Richtungen sind nach 51, 52 und 68 Bildern im Fahrzeug, bei 30, 60 und 120 Hz in denselben Bildern; vorher
läuft nach 15 s jeder der drei noch um das Fahrzeug, 18 Einheiten vom Mittelpunkt.

### 3.5 Wirkung (Kreis-Episoden von Heereseinheiten, 80 Gefechte)

| Stand | am Sammelpunkt | Spieler-Gefechte mit einer | nahe dem Ziel anderswo | unterwegs zu fernem Ziel | ohne Auftrag | Einsteigebefehle / Aufnahmen in Container (20 Manifestläufe) |
|---|---|---|---|---|---|---|
| `main` | **269** | **74 von 160** | 8 | 61 | 21 | 1.523 / 130 |
| nach B | 238 | 78 | 8 | 50 | 14 | 2.396 / 1.100 |
| nach C1 | 15 | 12 | 9 | 48 | 2 | 2.343 / 639 |
| nach C2 | **2** | **2 von 160** | 2 | 33 | 2 | 1.469 / 844 |
| nach D | 2 | 1 | 2 | 19 | 2 | 1.726 / 1.001 |

Hin-und-Her-Episoden am Sammelpunkt: 534 → 71 (nach C2).

**Was bleibt.** (a) Zwei Episoden am Sammelpunkt: Einsteiger eines Fahrzeugs, auf dem schon eine abgewiesene Einheit steht (die KI bestellt mehr Einsteiger, als Plätze frei
sind; sie zählt die unterwegs befindlichen nicht). (b) **Unterwegs, nicht behoben:** zwei Einheiten, die sich auf dem Marsch gegenseitig ausweichen und dabei umeinander drehen
(33 Episoden; `AvoidLocalBlockers` kennt seine Grenze: „can oscillate against a mover coming the other way“) – sichtbar als zwei Soldaten, die sich umkreisen, ohne dass einer
steht. (c) **Sammler am Dock, nicht behoben:** Arbeiter und Versorgungslaster kreisen am Lager oder Zentrum, wenn ein anderer dort steht (88 Episoden auf `main`, 235 nach C2,
68 nach D; die Zahl schwankt mit der Zahl der Sammler). Dieselbe Art Ursache wie C1, aber im Dock-Automaten, den die Korrektur bewusst ausnimmt. (d) Die Raten-Suite misst den
Truppentransporter-Fall jetzt und zeigt dabei eine Größe außerhalb der Toleranz, die vorher nicht zu sehen war (§8).

## 4. Beobachtung D: „Die China-KI passt sich nicht an ihren Gegner an“

### 4.1 Tatsachen

**Woraus `ProduceArmy` wählt.** Auf `main`: je Produzent gleich wahrscheinlich unter den gerade bezahlbaren Einheiten – faktisch nur Infanterie, China 52 % Panzerjäger und 47 %
Rotgardisten, gleich gegen wen. Seit B: jeder Einheitentyp aller Produzenten mit derselben Chance. Der Gegner geht in keine der beiden ein. Die Beobachtung stimmt.

**Was die Armeen bauen** (Anteile der produzierten Heereseinheiten, 80 Gefechte, Stand C2; in Klammern: wogegen sie nach den Waffendaten taugen):

| Fraktion | Zusammensetzung |
|---|---|
| China | Panzerjäger 38 % (Fahrzeuge, Gebäude), Rotgardist 34 % (Infanterie), Battlemaster 9 %, Dragon 8 % (Infanterie, Gebäude), Gattling 7 % (Infanterie, Flugzeuge), Truppentransporter 5 % |
| America | Ranger 38 % (Infanterie), Raketensoldat 35 % (Fahrzeuge, Gebäude, Flugzeuge), Sanitätswagen 8 % (nichts), Crusader 7 %, Humvee 6 %, Avenger 5 % (nur Flugzeuge und Geschosse) |
| GLA | Terrorist 34 %, Rebell 29 % (Infanterie), Tunnelverteidiger 25 % (Fahrzeuge, Gebäude), Scorpion 5 %, Gifttransporter 4 %, Vierling 4 % |

**Was was trifft** (Schaden aus `EntityDamaged`, nach Angreifertyp und Art des Opfers, Stand C2): Rotgardist 56 % auf Infanterie, 37 % auf Gebäude; Panzerjäger 18 % Infanterie,
18 % Fahrzeuge, 59 % Gebäude; Dragon 26 % / 16 % / 55 %; Battlemaster 7 % / 28 % / 63 %; Gattling-Panzer 34 % / 24 % / 33 % und 6 % Flugzeuge; Ranger 43 % Infanterie, 52 %
Gebäude; Raketensoldat 8 % / 12 % / 76 %; Crusader 5 % / 45 % / 46 %; Avenger 100 % auf Geschosse – auf kein einziges Bodenziel. Der größte Posten jeder Einheit außer den reinen
Infanteriewaffen ist die gegnerische Basis: die Armeen, die sich hier begegnen, sind überwiegend Infanterie und Gebäude.

**Was das Original tut.** Es passt sich an, aber nicht Einheit für Einheit, sondern mit **Gegenteams** der Skripte (`SkirmishScripts.scb`, gelesen mit
`MapTool script-library <content> --show "China"`). Skripte setzen Flaggen aus der Zahl gegnerischer Objekte einer Objektliste:

```
IF FLAG['_CT_INFANTRY'; 0] AND PLAYER_HAS_OBJECT_COMPARISON['<This Player's Enemy>'; 3; 25; 'Infantry']   DO SET_FLAG['_CT_INFANTRY'; 1]
IF FLAG['_CT_INFANTRY'; 1] AND PLAYER_HAS_OBJECT_COMPARISON['<This Player's Enemy>'; 0; 10; 'Infantry']   DO SET_FLAG['_CT_INFANTRY'; 0]
```

ebenso `_CT_TANK` (ab 8 bzw. 15 `'Tanks'`), `_CT_AIR` (ab 4 `'Aircraft'`), `_CT_BASEDEF` (ab 5 `'Base Defenses'`), `_CT_STEALTH` (ab 5 `'Spy'`). An den Flaggen hängen die
Produktionsbedingungen von Teams mit höherer Priorität (170–215) als die Wellen (100–130): `China CT - Infantry 1` (bis 2 Gattling-Panzer, bis 4 Rotgardisten),
`China CT - Infantry 2` (Dragon, Gattling, Rotgardist), `China CT - Tank 1–3` (Panzerjäger, Battlemaster), `China CT - Air 1/2` (Panzerjäger, Gattling-Panzer),
`China CT - Base Def 1/2` (Inferno-Kanone, Nuklearkanone), `China CT - Stealth 1/2` (Truppentransporter). Die Bedingung zählt die Objekte des Gegners, gesehen oder nicht. Gegen
viel Infanterie baut Chinas Original-KI also Gattling- und Dragon-Panzer **und** Rotgardisten – was der Inhaber erwartet hat. Die eingebaute KI führt keine Skripte aus.

**Der Versuch aus Phase 17** (`docs/SIMULATION-CORE.md` §36.3): je Produzent die teuerste Einheit, verdoppelt bei größerer Reichweite, Fabriken zuerst, Sparen statt Kleinkauf.
Zurückgenommen nach zwei Messrunden über 16 einzelne Läufe (12 → 10 → 11 entschieden): der Preis ist kein Kampfwert (18 Avenger vor einer Basis, auf die sie nicht schießen
können), und das Urteil stand auf einem Seed je Paarung. Die Lehre dort – „ein Kampfwert je Ziel, Schaden gegen Rüstung“ – ist, was hier gebaut ist; und gemessen wird mit 640
Gefechten statt 16.

### 4.2 Die Regel „Tauschwert“ (`ArmyComposition.ExchangeValue`, `Skirmish/SkirmishAi.Composition.cs`)

Was der Spieler in den letzten drei Minuten vom Gegner **gesehen** hat (jedes feindliche Objekt mit Lebenspunkten, nach Typ; die KI merkt es sich in `Observe` neben ihren anderen
Erinnerungen – keine Abfrage der Welt, nichts, was der Spieler nicht wissen kann) ist die Streitmacht E. Für eine Kandidateneinheit u, nur aus dem Katalog:

- **Schaden je Bild** d(a, b): die beste Waffe des Standard-Waffensatzes von a, die auf b gefeuert werden darf (Boden oder Luft): Grundschaden × Rüstungskoeffizient von b für die
  Schadensart × Schüsse je Logikbild (Magazin und Nachladen eingerechnet); eine Waffe, die ihren Träger tötet, zählt als ein Schuss im Leben; höchstens ein Objekt je Bild;
- **Wirkung** O(u) = Σ über E von n(e) · Preis(e) · d(u, e) / Leben(e) – die Credits des Gegners, die u je Bild zerstört, solange sie feuert;
- **Gefährdung** X(u) = 1/1800 + Σ über E von n(e) · d(e, u) / Leben(u) – der Anteil ihrer selbst, den u je Bild verliert; nie weniger als „lebt eine Minute“;
- **Tauschwert** V(u) = O(u) / (X(u) · Preis(u)) – zerstörte gegnerische Credits je ausgegebenem Credit.

Die Ziehung gibt jedem Kandidaten denselben Anteil an einem Viertel (`VarietyShare`: der Boden an Vielfalt, und die einzige Chance für Einheiten, deren Wert der Katalog nicht
zeigt – Transporter, Heiler, Einheiten, die nur erobern) und verteilt den Rest proportional zu V. Ohne Sichtung, oder wenn kein Kandidat etwas Gesehenes verletzen kann, ist die
Ziehung die gleichverteilte von B. Immer genau ein Zug aus dem eigenen, geseedeten Strom der KI. Kosten: je Ziehung (alle paar Sekunden) Kandidaten × gesehene Typen; die
Schadenswerte je Typenpaar werden gemerkt. Der neue Zustand (`_sightings`) liegt im selben Objekt wie der übrige KI-Zustand (Register E11).

Was die Regel **nicht** weiß: Reichweite und Tempo (eine Einheit, die nie zum Schuss kommt), Flächenschaden, Besatzungen in Gebäuden, Upgrades und Veteranenstufen, Tarnung,
Spezialkräfte, die nächste Einheit des Gegners. Sie beurteilt die Einheit eines Produzenten, nicht Teams. Drei Zahlen sind eigene Wahl: drei Minuten Gedächtnis, eine Minute
Lebenszeit als Obergrenze, ein Viertel Vielfalt.

Test (`8d8dcdf`): zwei gleich teure Soldaten aus einem Lager, ein Gewehr (gegen weiche Rüstung) und eine Panzerfaust (gegen harte). Gegen harte Ziele 20 Gewehre zu 82
Panzerfäusten, gegen weiche 80 zu 22, ohne Sichtung oder ohne Regel 48 zu 54; gleicher Seed, gleiche Armee.

### 4.3 Abnahme – vorab festgelegt, nicht nachgestellt

Kriterien (aus dem Auftrag, vor jedem Lauf): **(1)** gepaart, dieselben 16 Paarungen × 20 Seeds (1–20) bei 30 Hz, ein Spieler mit Regel gegen einen ohne im selben Gefecht, beide
Sitzordnungen, 640 Gefechte: die KI mit Regel muss mehr entschiedene Gefechte gewinnen, zweiseitiger exakter Binomialtest, α = 0,05. **(2)** symmetrisch (beide mit Regel, 320
Gefechte): der Anteil entschiedener Gefechte darf nicht um mehr als die halbe Breite des 95-%-Intervalls zweier Anteile unter `main`s 253 von 320 liegen.

Vor der Abnahme lief **eine** Probe mit anderen Seeds (101–105, 160 Gefechte: 86 Siege, 63 Niederlagen, 11 offen), um grobe Fehler auszuschließen; an der Regel wurde danach nichts
geändert. Befehle: `ai-survey --composition exchange,uniform`, `uniform,exchange`, `exchange,exchange`, dann `composition-test`.

| Kriterium | Ergebnis |
|---|---|
| (1) gepaart, 640 Gefechte | KI mit Regel: **339 Siege, 252 Niederlagen**, 49 unentschieden; Siegquote unter den 591 entschiedenen **57,4 %**, exaktes 95-%-Intervall [53,3 %, 61,4 %]; p = **0,0004** → **bestanden** |
| (2) symmetrisch, 320 Gefechte | **298 von 320** entschieden (93,1 %); `main` 253 von 320 (79,1 %); halbe Breite 5,2 Punkte, Grenze 73,8 % → **bestanden** |

Dazu, nicht Teil des Kriteriums: derselbe Stand ohne Regel entscheidet 284 von 320. Je Paarung (Siege mit Regel / Niederlagen / offen): die Regel gewinnt in 12 von 16 Paarungen
mehr, als sie verliert, in 4 gleich viel, in keiner weniger; am deutlichsten Bitter Winter America–China (22/5/13), Bitter Winter ChinaTank–AirForce (26/10/4) und Armored Fury
America–China (25/10/5). In den vier Paarungen mit Gleichstand (20/20) entscheidet die Fraktion, nicht die Regel: zweimal gewinnt America alle 40
Gefechte gegen GLA, zweimal eine Fraktion 34 bzw. 38 von 40.

**Folge:** die Regel ist **Standard** (`SkirmishAi.DefaultComposition = ExchangeValue`, eigener Commit); `SkirmishAiOptions.Composition` und `SkirmishSlot.Composition` wählen je
Spieler.

### 4.4 Was die Regel an den Armeen ändert – und was nicht

Anteile mit Regel (derselbe Zensus, in Klammern ohne): China Dragon **21 %** (8), Battlemaster 3 % (9), Gattling 6 % (7), Truppentransporter 1–2 % (5), Rotgardist 34–37 % (34),
Panzerjäger 33–34 % (38); America Humvee 15–16 % (6), Crusader 8–13 % (7), **Sanitätswagen 2 % (8), Avenger 1 % (5)**; GLA Terrorist **47 %** (34), Fahrzeuge 6 % (13). Von 7.511
Ziehungen waren 4.228 informiert und 3.283 (44 %) fielen vor der ersten Sichtung.

**Ehrlich zur Beobachtung selbst:** „Infanterie kommt, und die China-KI baut Panzerjäger statt eigener Infanterie“ ist damit **nur zum Teil** behoben. Die Regel streicht, was
nichts trifft (Sanitätswagen, Flugabwehr ohne Flugzeuge, leere Transporter), und verschiebt zu dem, was Infanterie und Gebäude billig zerstört (Dragon, Humvee, Terrorist). Der
Anteil der Panzerjäger sinkt aber nur von 38 % auf ein Drittel: fast die Hälfte der Ziehungen fällt, bevor die KI einen Gegner gesehen hat (dann 50 : 50 aus der Kaserne), und die
Regel rechnet dem Panzerjäger seinen Schaden an Gebäuden an – die Hälfte dessen, was er in diesen Gefechten tatsächlich trifft. Gegen wen China spielt, ändert die Mischung kaum
(gegen America wie gegen GLA 21 % Dragon), weil beide Gegner hier überwiegend Infanterie und Gebäude aufstellen. Ob das im Fenster wie „passt sich an“ aussieht, kann nur ein Mensch
sagen.

## 5. Werkzeuge (neu)

| Befehl | Zweck |
|---|---|
| `SimTool ai-census <content> --out <tsv> [--orbits <txt>] [--seeds N] [--first-seed N] [--pairings …] [--jobs N] [--composition a,b] [--watch <id>]` | die Gefechte von `ai-survey`, beobachtet: je Spieler erste Fertigstellung jedes Gebäudes, Produktion je Produzent, Ausgang jedes Blicks der Heeresproduktion (`SkirmishAiStats.ArmyProduction`), Geld alle 50 s, Schaden nach Angreifertyp und Opferart, Verluste, kreisende Einheiten nach Ort |
| `SimTool ai-survey … --composition uniform\|exchange\|default,uniform\|exchange\|default` | Zusammensetzung je Spieler |
| `SimTool composition-test --rule-first <tsv> --rule-second <tsv> --symmetric <tsv> --baseline 253/320 [--out <md>]` | die Abnahme aus §4.3: exakter Binomialtest, Clopper-Pearson-Intervall, Zwei-Anteile-Grenze |

## 6. Was nur ein Mensch bestätigen kann, und was er beim nächsten Mal notieren sollte

- Ob im Fenster nichts mehr kreist. Headless: von 269 Episoden am Sammelpunkt bleiben 2; **unterwegs** drehen sich weiter gelegentlich zwei Einheiten umeinander (§3.5 b), und
  Sammler kreisen am Dock (c). Wer das sieht, hat keinen Rückfall von C gesehen, sondern einen der beiden offenen Fälle.
- Ob die China-Armee „nach Panzern aussieht“ (rund ein Viertel bis ein Drittel Fahrzeuge, vor allem Dragon) und ob die Mischung als Antwort auf den Gegner wirkt (§4.4).
- Wie Einheiten aussehen, die neben dem Sammelpunkt auf Nachbarzellen stehen (ein Raster von 10 Einheiten; Formationen gibt es nicht), und wie ein Soldat aussieht, der beim
  Berühren im Fahrzeug verschwindet.
- Bitte notieren: **den Seed** (das Skript gibt ihn aus: `Seed N`) und die Paarung, die **Spielzeit**, die **Einheiten** (Typ, welcher Spieler) und ob eine davon steht. Mit dem
  Seed ist das Gefecht headless dasselbe: `SimTool ai-census <content> --out x.tsv --orbits x.txt --pairings <Paarung> --first-seed N --seeds 1`.
- Beobachtung A ist offen; sobald ihre Korrektur gemergt ist, ändern sich Wege, Sammelzeiten und alle Zahlen hier.

## 7. Commits, Referenzmanifest, Missionen

`baselines/reference-manifest.json` ist **nicht** neu geschrieben (Auftrag). `reference-manifest check --set full` gegen die Datei des Branch-Ausgangs; „Ergebnisse“ aus
`reference-manifest results`, jeweils gegen den Stand davor. Auf `main` selbst weichen drei Missionsläufe ab (`MD_GLA01`, `MD_USA02`, `MD_USA05`: die Korrektur des verlorenen
Weckrufs, Manifest dort noch ausstehend); das ist aus deren Commit-Nachricht geschlossen und daraus, dass die beiden Werkzeug-Commits dieselben drei Abweichungen zeigen.

| Commit | Inhalt | Läufe mit anderem Hash als das Manifest | Ergebnisvergleich mit dem Stand davor |
|---|---|---|---|
| `8c42f70`, `6d83063` | Werkzeuge: `ai-census`, Zähler `ArmyProduction`, Ort der Episoden, `--watch` | 3 (die von `main`) | – (kein Verhalten geändert) |
| `50718d7` (B), Test `7a29943` | stehender Heeresauftrag | 24 = 3 + die 21 Läufe mit `SkirmishAi` (16 Zweispieler, 4 `ai8_…`, `skirmish-sweep`) | 29 Läufe gleich; 16 Zweispielerläufe: entschieden 12 → 12, Ausgang anders in 7; `skirmish-sweep` andere Prüfsumme; alle 27 Missionsläufe gleich |
| `3816445` (C1), Test `ccdc12b` | freier Zielplatz | 40: die 21 KI-Läufe und 19 Missionsläufe (`GC_ChinaBoss_*` ×9, `GC_LaserGeneral_ChinaNukeGeneral`, `MD_CHI01`, `MD_CHI05`, `MD_GLA01`, `MD_GLA03`, `MD_GLA05`, `MD_USA01`, `MD_USA02`, `MD_USA05`, `Training01`) | 22 Läufe gleich (darunter 12 der 19 Missionen: nur der Hash ist anders); 7 Missionen mit anderen Ereigniszahlen, **kein Ausgang und kein Endskript anders** – z. B. `MD_GLA01` `MoveArrived` 53 → 185, `MD_USA01` 699 → 796; Zweispielerläufe entschieden 12 → 13, Ausgang anders in 3 |
| `b93ba6c` (C2), Test `2259f70` | Einsteigen | dieselben 40; gegenüber C1 ändern sich 18 Hashes (13 Zweispielerläufe, `ai8_FortressAvalanche`, `ai8_IronDragon`, `ai8_TwilightFlame`, `mission_MD_USA01`, `mission_MD_USA02`) | 32 Läufe gleich; Missionen: `MD_USA01` `EntityContained` 216 → 217, `MD_USA02` `MoveArrived` 93 → 92, Ausgang gleich; Zweispielerläufe entschieden 13 → 15, Ausgang anders in 5 |
| `ca02d34`, Test `8d8dcdf`, Werkzeuge `a933fc2` | Regel „Tauschwert“, **aus** | dieselben 40, dieselben Hashes wie C2 (die Messung für C2 lief mit diesem Stand) | – |
| `2451852` | Regel **an** (Standard) | dieselben 40; gegenüber dem Stand davor ändern sich 19 Hashes: die 16 Zweispielerläufe, `ai8_FortressAvalanche`, `ai8_IronDragon`, `ai8_TwilightFlame` – nicht `ai8_TwilightFlame_short`, nicht `skirmish-sweep` (in 3.000 Ticks sieht keine KI einen Gegner), keine Mission | 31 Läufe gleich; Zweispielerläufe entschieden 15 → 14, Ausgang anders in 4 |

Unverändert gegenüber `main` bleiben nach allen Commits: `reference-match`, `stress` und acht Missionsläufe (`GC_AirGeneral_*` ×3, `GC_ChemGeneral_*` ×2,
`GC_SuperWeaponsGeneral_GLADemolitionGeneral`, `GC_TankGeneral_*` ×2).

**Nur-Befehle-Agent der Missionen** (`CampaignTool drive <content> --commands-only --map "/NAME.map" --minutes 34`, 0 Weltschreibungen; er benutzt `SkirmishAi`):

| Stand | `MD_GLA04` | `MD_CHI01` | `MD_CHI02` |
|---|---|---|---|
| `main` | Sieg @1.022 | Sieg @18.252 | Sieg @29.250 |
| nach B | Sieg @1.022 | Sieg @15.504 | **nicht erreicht** (61.200 Ticks) |
| nach C2 (Regel aus) | Sieg @1.022 | Sieg @18.206 | **nicht erreicht** |
| nach D (Regel an) | Sieg @1.022 | Sieg @18.585 | **nicht erreicht** |

**`MD_CHI02` wird seit B in 34 Spielminuten nicht mehr gewonnen** (vorher bei 16:15); C und D ändern daran nichts. Es ist ein einzelner, chaotischer Lauf, und schon der Versuch aus
Phase 17, der ebenfalls sparen ließ, hat genau diese Mission verloren (`docs/SIMULATION-CORE.md` §36.3) – das spricht dafür, dass der Agent dieser Mission mit vielen billigen
Einheiten besser fährt als mit wenigen teuren. Untersucht ist es nicht. Auch mit 60 Spielminuten (108.000 Ticks, Stand D) wird sie nicht gewonnen: das ist kein „später“, sondern
ein Rückschritt dieses einen Laufs, den B verursacht.

## 8. Raten-Suite (`Simulation.Tests -- --content-dir … --only RateEquivalence`)

Größen außerhalb der Toleranz bei 60 / 120 Hz, `main` laut Auftrag gegen diesen Branch: locomotion 2/2 → 2/2, flight 0/3 → 0/3, projectiles 4/4 → 4/4, turrets 4/0 → 4/0,
weapons, economy, timed, scripts 0/0 → 0/0, **ai 24/23 → 22/22**, **overtime 0/0 → 0/1**.

Zu `overtime`: das ist keine Größe, die schlechter geworden ist, sondern eine, die vorher nicht gemessen werden konnte. Auf `main` kommt der Rotgardist des Falls „heal inside
ChinaVehicleTroopCrawler“ nie in den Transporter (drei Größen „unmeasured“, zwei mit dem Wert 0 bei allen Raten „ok“; nachgemessen am Ausgangsstand). Seit C2 steigt er ein – das
erste Bild mit Heilung liegt bei allen drei Raten bei 5,2000 s –, und die Suite sieht zum ersten Mal die Heilung im Transporter: „frames with healing in 120 s“ 151 / 151 / 150.
Die Abweichung liegt im Heilen (`TransportContain`), nicht im Einsteigen; sie ist **nicht behoben** und gehört zu den Raten-Paketen. In der Gruppe sind jetzt 52 statt 55 Größen
ungemessen.

## 9. Verifikation am Endstand (`2451852`)

| Prüfung | Ergebnis |
|---|---|
| `dotnet build CnCNew.slnx` | 0 Warnungen, 0 Fehler |
| `Simulation.Tests` (Release) bei 30 Hz, `-- --tick-rate 60`, `-- --tick-rate 120` | je „All simulation core tests passed“ (502 Tests, darunter die vier neuen) |
| `reference-manifest check --set quick --verify-incremental` | inkrementeller Hash in 67.031 geprüften Ticks gleich dem voll berechneten, **0 Abweichungen**; 7 der 9 Läufe weichen wie erwartet vom Manifest ab, `reference-match` und `stress` OK |
| `input-manifest check` | identisch |
| Raten-Suite | §8 |
| `Game.Tests` (Release, `--content-dir`) | „All game tests passed“ (Zuschauer- und Skirmish-Tests benutzen die KI) |
| `scripts/check-spectator.ps1` | PASS (Alpine Assault America–China, Seed 7: America baut dabei einen Crusader in der Waffenfabrik) |

Nicht gelaufen: `CampaignTool drive` über alle 24 Karten, `audit`, `reachability`, die Szenen-Audits, `reference-manifest check --set full` unter Linux, die Determinismus-Matrix. Die
Missionsläufe des Manifests zeigen keinen geänderten Ausgang (§7); mehr ist für die Kampagne nicht belegt.

## 10. Nicht erledigt, nicht belegt

- Kein Mensch hat irgendetwas davon gesehen (§6).
- Beobachtung D ist nur zum Teil behoben (§4.4); ein Gegenstück zu den Gegenteams des Originals (Schwellen je Objektliste) gibt es nicht.
- GLA baut auf drei von vier Karten nur eine Kaserne (§2.4 c) – gefunden, nicht untersucht.
- Zwei Einheiten, die sich unterwegs umkreisen, und Sammler am Dock (§3.5 b, c) – gefunden, nicht behoben.
- Die KI überbucht Transporter (§3.5 a).
- Die Heilung im Transporter bei 120 Hz (§8).
- **`MD_CHI02` im Nur-Befehle-Modus geht seit B verloren** (§7), auch mit 60 Spielminuten; der Nur-Befehle-Agent gewinnt damit von den drei geprüften Karten zwei statt drei.
- Die 16 Tabellenläufe in `docs/SIMULATION-CORE.md` §19/§36 und alle dortigen Hashes sind überholt; jenes Dokument, `CLAUDE.md`, README und die Handoffs sind auftragsgemäß nicht
  angefasst.
- Balance ist nicht bewertet: „mehr entschiedene Gefechte“ und „gewinnt gegen die KI ohne Regel“ sagen nichts darüber, ob die KI gegen einen Menschen gut spielt.
