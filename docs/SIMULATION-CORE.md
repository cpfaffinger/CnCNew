# Deterministischer Simulationskern (Phase 4, erweitert in Phase 5)

Stand: 28. September 2026 (Phase 5 in Arbeit, siehe `docs/GAME-RULES.md` für die Phase-5-Regeln:
Wirtschaft, Upgrades, Kampf, einfache Bewegung, Produktion, Sieg/Niederlage)

`CnCNew.Simulation` ist die headless laufende, deterministische Welt: fester Tick, stabile IDs,
reproduzierbare Zufallszahlen, Befehlsqueue, Objekt-/Modul-Lebenszyklus, Timer, Events,
kanonische Snapshots, Zustands-Hashes und ein Replay-Harness. Das Projekt hängt ausschließlich von
`CnCNew.Data` ab (kein Stride, keine UI, kein Content-/Archiv-/Dateisystemzugriff). Ein Test
(`SourceRules`) erzwingt das mechanisch: verbotene Konstrukte im Quelltext (Wandzeit, ambiente
Zufallszahlen, `float`/`double`, Threads, Dateizugriff, Reflection) und jede Assemblyreferenz außer
`CnCNew.Data` und der BCL lassen den Test scheitern.

Die Fachgrundlagen der Definitionsdaten (Block 0 der Phase) stehen in `TYPED-DEFINITIONS.md` und
`PHASE3-COMPATIBILITY.md`; dieses Dokument beschreibt den Kern selbst.

## 1. Tick und Numerik

- **Takt:** 30 Logikframes pro Sekunde (`LOGICFRAMES_PER_SECOND`, `GameCommon.h:72`). `SimulationWorld.Tick`
  ist die Zahl der ausgeführten Ticks und zugleich die Nummer des nächsten. Es gibt keine Wandzeit im Kern.
- **Zahlenart:** Zustand besteht nur aus Ganzzahlen und `Fixed`. `Fixed` ist ein Q47.16-Festkomma in
  einem `long` (16 Nachkommabits, Auflösung 1/65536 Weltinheit, Bereich ±1,4·10^14). Begründung: Das Original hält
  binary32-`Real` im Zustand und setzt dafür `setFPMode()` (`INI::load`); Ganzzahlarithmetik ist auf jeder Plattform
  bitgleich. Produkte und Quotienten laufen über `Int128`.
- **Rundungsregel (überall):** zur nächsten Zahl, Halbwerte weg von null. Überlauf wirft `OverflowException`;
  nichts wickelt oder sättigt still. Division durch null wirft.
- **Umrechnungen** (`SimulationTiming`, alle explizit):

| Datenwert | Regel | Referenz |
|---|---|---|
| Dauer in ms → ganze Ticks | `ceil(ms · 30 / 1000)`, exakt in Ganzzahl-/Dezimalarithmetik; 0 ms = 0 Ticks; negativ → Fehler | `INI::parseDurationUnsignedInt` (`ceilf`) |
| Dauer in ms → Ticks (ungerundet) | `Fixed(ms · 30 / 1000)` | `INI::parseDurationReal` |
| Sekunden (`BuildTime`) → Ticks | `ceil(s · 30)` | – |
| Geschwindigkeit pro s → pro Tick | `v / 30` | `ConvertVelocityInSecsToFrames` |
| Beschleunigung pro s² → pro Tick² | `a / 900` | `ConvertAccelerationInSecsToFrames` |
| Grad → Bogenmaß | `deg · π / 180`, π auf 28 Stellen | `INI::parseAngleReal` |
| Grad/s → Bogenmaß/Tick | `deg · π / 180 / 30` | `ConvertAngularVelocityInDegreesPerSecToRadsPerFrame` |
| Prozent | Verhältnis (`50 %` → 0,5) als `Fixed` | `INI::parsePercentToReal` |

- **Abweichung Dauern:** Das Original rechnet `ceilf((Real)msec · (30.0f/1000.0f))` in binary32. Ein Test emuliert das
  in IEEE-binary32 und vergleicht mit der exakten Regel über 0 bis 5.000.000 ms (≈ 83 Minuten): **6.310 von 5.000.001 Werten
  weichen ab**, alle um genau einen Tick nach unten (24-Bit-Mantisse); der erste Unterschied liegt bei 4.369.067 ms
  (≈ 73 Minuten). Alle Dauern unter 4.000.000 ms (≈ 66 Minuten) sind identisch. Der Kern rechnet exakt.
- **Grenzwerte** (getestet): 0 ms, 1 ms, 33/34 ms (Rundung), 4.294.967.295 ms (128.849.019 Ticks), Überlauf von
  `decimal`/`long`, negative Dauern, `Fixed`-Über- und Unterlauf, `Halbwert`-Rundung bei Multiplikation, Division und
  Dezimalkonvertierung, `Floor`/`Ceiling`/`Round` bei negativen Werten. Negative Dauern in den Daten folgen
  `sscanf("%u")`: `MinLifetime = -1` (Karte `MD_GLA01`) ergibt 4.294.967.295 ms und wird als
  `compatibility-unsigned-wrap` sichtbar gemacht.

## 2. IDs und kanonische Ordnung

- `EntityId` beginnt bei 1, **0 ist ungültig** (`INVALID_ID`, `GameLogic::allocateObjectID`: `m_nextObjID = 1`, danach
  `+1`). IDs werden **nie wiederverwendet** (das Original vergibt ebenfalls monoton; die Lückensuche steht dort als
  `@todo`). `EventId`, `TimerId` und `CommandId` sind ebenfalls monotone Zähler ab 1; `PlayerId` 0 ist die eingebaute
  Systeminstanz (Skripte, Werkzeuge, Tests), echte Spieler beginnen bei 1; `WorldId` vergibt der Ersteller der Welt.
- Alle Zähler sind Teil des Zustands (Hash und Snapshot).
- **Update-Reihenfolge** hängt weder von `Dictionary`-/`HashSet`-Reihenfolge noch von Threads ab: Updates nach
  (Weckzeit, Entity-ID, Modulindex), Befehle nach (Spieler-ID, Sequenz), Timer nach (Fälligkeit, Timer-ID), Zerstörungen
  in aufsteigender Entity-ID, Spawns in Reservierungsreihenfolge (= aufsteigende ID), Events nach Event-ID.

### Sleepy-Update-Modell

Übernommen wird das Modell des Originals (`GameLogic::m_sleepyUpdates`, `UpdateModule`): Jedes Updatemodul hat einen
absoluten Weckzeitpunkt, ein Min-Heap mit Rückverweis (`indexInLogic`) hält die geplanten Module, `update()` liefert
die Schlafdauer (≥ 1, `UPDATE_SLEEP_NONE = 1`, `UPDATE_SLEEP_FOREVER = 0x3fffffff`), Weckzeiten werden auf
`UPDATE_SLEEP_FOREVER` begrenzt, `setWakeFrame` aus dem eigenen `update()` wird ignoriert, und der Sonderfall
„bereits wach, will nächsten Tick“ ändert nichts (`friend_awakenUpdateModule`).

Bewusste Abweichungen:
1. **Totale Ordnung statt Heap-Zufall.** Das Original vergleicht nur `(Frame << 2) | Phase`; bei Gleichstand entscheidet die
   Heapstruktur. Der Kern sortiert zusätzlich nach Entity-ID und Modulindex; die Reihenfolge ist damit unabhängig von der
   Heapimplementierung.
2. **Keine Update-Phasen.** `SleepyUpdatePhase` (0..3) wird noch von keinem implementierten Modul benutzt; alle Module
   laufen in Phase 0. Sobald ein Modul Phasen braucht, wird der Schlüssel erweitert (Grenze).
3. **„Forever“ = nicht eingeplant.** Module mit Weckzeit ≥ `UPDATE_SLEEP_FOREVER` liegen nicht im Heap (das Original hält
   sie am Heapende); dadurch kehrt die Tabelle nach Ablauf aller Lebensdauern auf null zurück.
4. **Zerstörte Entities.** Ein Entity, das in diesem Tick zur Zerstörung markiert wurde, wird nicht mehr aktualisiert
   (das Original prüft das nicht im Sleepy-Loop und löscht erst am Frameende).

## 3. Zufallszahlen

- **Algorithmus:** `SageRandom` ist eine 1:1-Portierung von `RandomValue.cpp` (`randomValue`, `seedRandom`,
  `GetGameLogicRandomValue`): sechs 32-Bit-Wörter, Addition mit Übertrag über alle Wörter, danach übertragsfortpflanzendes
  Inkrement des untersten Wortes. Auch die Randfälle des Originals bleiben erhalten: `delta = hi - lo + 1` läuft als
  `unsigned` über (voller Bereich liefert `hi` ohne Zug), Bereiche mit `hi < lo` verhalten sich wie im Original.
- **Streams und Seedableitung** (`RandomStreams`): `logic`, `cosmetic` (im Original „client“) und `audio`. Jeder Stream
  wird wie in `InitRandom(seed)` mit demselben Weltseed über `seedRandom` initialisiert und schreitet danach unabhängig
  fort. Nur `logic` darf die Simulation ziehen und ist Teil des Tick-Hashes. `cosmetic` und `audio` gehören der
  Präsentation, stehen im Snapshot, aber **nicht** im Hash. Es gibt keinen ambienten Generator (`Random.Shared` ist
  verboten und wird mechanisch geprüft).
- **Abweichung Real-Zufall:** Das Original multipliziert mit einem binary32-Faktor; `Range(Fixed, Fixed)` rechnet exakt
  `lo + (hi − lo) · r / (2^32 − 1)` in Festkomma (überschreitet `hi` nie).
- **Golden-Werte, unabhängig berechnet.** Auf dieser Maschine steht kein C++-Compiler (`gcc`, `clang`, `cl` fehlen), die
  Referenz konnte daher nicht kompiliert werden. Stattdessen entstand eine getrennte Python-Transkription
  (`randomValue`, `seedRandom`, `range` mit expliziten 32-Bit-Masken, direkt aus `RandomValue.cpp` abgeschrieben, ohne den
  C#-Code anzusehen). Getestet sind die ersten 8 Rohwerte und der Zustand danach für die Seeds `0`, `1`, `0x12345678`,
  `0xFFFFFFFF`; Bereichsziehungen `[0,99]` und `[-5,5]` (je 12 Werte); der Zustand nach **1.000.000 Zügen** (Seed
  `0x12345678`: `d020b74b dafd10ec e5c0df3d 405d1ab8 3717abd5 8222d41c`); konstruierte Übertragsketten einschließlich der
  vollen Inkrement-Kaskade (`[0,0,0,0,0,0xFFFFFFFF]` → Rückgabe 0 und Nullzustand); der Vollbereichsfall. Die
  Handrechnung ist damit die dokumentierte Ersatzbeweisführung; ein Abgleich mit dem kompilierten Original bleibt als
  Schuld für einen Rechner mit Compiler offen (Phase 5 braucht ihn ohnehin für die Golden-Master-Szenarien).

## 4. Welt, Tickphasen und Lebenszyklus

`SimulationWorld.Step()` führt genau diese Phasen in dieser Reihenfolge aus:

| # | Phase | Inhalt |
|---|---|---|
| 1 | Befehle | Befehle mit Ziel-Tick = aktueller Tick, geordnet nach (Spieler, Sequenz); Spawn reserviert eine ID und stellt in die Spawn-Queue, Destroy markiert, Damage/Kill wirken sofort, Timer entstehen |
| 2 | Timer | fällige Timer nach (Fälligkeit, ID); Wiederholung setzt `Fälligkeit = aktueller Tick + Intervall` |
| 3 | Updates | Sleepy-Scheduler; markierte Entities werden übersprungen; Rückgabewert bestimmt die nächste Weckzeit |
| 4 | Events | Zustellung in Event-ID-Reihenfolge; während der Zustellung erzeugte Events kommen im selben Durchlauf dran |
| 5 | Zerstören | markierte Entities in aufsteigender ID: `OnRemove`, Scheduler-Einträge und eigene Timer entfernen, Tabelle leeren |
| 6 | Spawnen | reservierte Entities in ID-Reihenfolge erzeugen; Module erhalten `OnSpawn` (dürfen `logic` ziehen); Updates werden eingeplant |
| 7 | Events | Events der Spawn-Phase werden zugestellt |
| 8 | Hash | Tick-Zähler steigt, Zustands-Hash wird berechnet |

Regeln:
- **Mutationen während einer Iteration laufen über Queues.** Ein Spawn wird erst in Phase 6 sichtbar (ein Entity wird also
  nie in dem Tick aktualisiert, in dem es entsteht; frühester Update ist der nächste Tick), eine Zerstörung erst in
  Phase 5. Das Original erzeugt Objekte sofort; die Spawn-Queue ist eine bewusste, dokumentierte Abweichung – die ID wird
  aber schon bei der Anforderung vergeben, sodass der Anforderer sie kennt.
- **Ein am Tickende zerstörtes Entity** (`RequestDestroy`) erhält in diesem Tick keine weiteren Updates, aber sein
  `EntityDestroyed`-Event und alle bis dahin erzeugten Events werden zugestellt, **bevor** es entfernt wird (`OnRemove`
  läuft danach).
- **Tod ist nicht Zerstörung.** `Kill` löst Todesmodule aus; ein Entity ohne zerstörendes Todesmodul (`KeepObjectDie`)
  bleibt als Leiche bestehen, wie im Original. `Destroy` entfernt ohne Todesmodule.
- **Schutz vor Endlosschleifen:** Updates und Events pro Tick sind hart begrenzt (50 bzw. 20 Millionen); Überschreitung
  wirft. Es gibt keine Threads und keine Sperren; ein Deadlock ist konstruktionsbedingt ausgeschlossen (der Test
  `SourceRules` verbietet `Thread`, `Task.Run`, `Parallel`, `lock`).

## 5. Module

### Bindung

`CnCNew.Data` führt Deskriptoren ohne Factory (`ModuleDescriptor`: Name, Blockart, Interfacemaske, Besitzerarten,
Feldschema, `CompatibilityOnly`, Zielphase, Kategorie); der Katalog enthält alle **205** Modultypen des Bestands (eine
frühere Zählung von 206 enthielt das Phantommodul `ArmorSet`, entstanden aus einem in `Maps/MD_CHI03/map.ini` nie
geschlossenen `AddModule`, dessen Felder das Original auf das Objekt anwendet). Phase 5 hat **33** dieser 205 Typen
implementiert (12 aus Phase 4 plus 21 neue: `StealthUpdate`, `AutoHealBehavior`, die elf `*Upgrade`-Module,
`AIUpdateInterface`, `ProductionUpdate`, `FlammableUpdate`, `TransitionDamageFX`, `SlowDeathBehavior`,
`FireWeaponWhenDeadBehavior`, `StealthDetectorUpdate`, `PoisonedBehavior` – siehe `docs/GAME-RULES.md` §3/§6).
`CnCNew.Simulation` besitzt die
explizite, ordinal sortierte, reflexionsfreie Bindungstabelle `BuiltInModules.Bindings` (Name → `ModuleBinding<TData>` mit
Parser und Factory) und `ModuleBindingTable` prüft sie beim Erzeugen gegen die Deskriptoren:

| Fehlerfall | Ergebnis |
|---|---|
| unsortierte Tabelle, doppelter Name | `ModuleBindingException` |
| Name ohne Deskriptor | `ModuleBindingException` (unbekanntes Modul) |
| Deskriptor `CompatibilityOnly` (nicht implementiert) | `ModuleBindingException` |
| Präsentationsmodul (Draw/ClientUpdate) | `ModuleBindingException` |
| falsche Besitzerart | `ModuleBindingException` |
| implementiertes Simulationsmodul ohne Bindung | `ModuleBindingException` |

`StrictUnknownModules` besteht auf den Bestandsdaten; der Test `catalog == beobachtete Modulmenge` (205 Namen) läuft gegen
`.local/phase2-import-a`. Nicht implementierte Module bleiben sichtbare Kompatibilitätsfälle: ein Objekt mit einem
solchen Modul ist **nicht instanziierbar** und nennt es in `ObjectTemplate.MissingModules`; Draw-/ClientUpdate-Module
werden gelistet (`PresentationModules`), aber nie instanziiert.

### Auswahl mit Zahlen

Grundlage: alle Moduleinträge der Basisobjekte im Retail-Bestand einschließlich der vom `DefaultThingTemplate` geerbten
(19.529 Verwendungen in 2.113 Objekten; `SimTool audit-modules <content-dir>`). Die zwölf implementierten Module decken
5.497 Verwendungen (28,1 %):

| Modul | Verwendungen | Objekte | Rolle |
|---|---:|---:|---|
| `DestroyDie` | 1.295 | 1.295 | Todesmodul (Default und explizit) |
| `ActiveBody` | 1.217 | 1.215 | Körper mit Gesundheit |
| `FXListDie` | 1.144 | 1.008 | Todesmodul, Effektanforderung |
| `CreateObjectDie` | 629 | 536 | Todesmodul, Erzeugungslistenanforderung |
| `InactiveBody` | 453 | 453 | Default-Körper (Props) |
| `StructureBody` | 365 | 365 | Gebäudekörper |
| `LifetimeUpdate` | 136 | 136 | zeitgesteuerter Tod |
| `InstantDeathBehavior` | 114 | 56 | sofortiger Tod mit Effekten |
| `KeepObjectDie` | 62 | 62 | Leiche behalten |
| `ImmortalBody` | 42 | 42 | unsterblicher Körper |
| `HighlanderBody` | 21 | 21 | Körper mit 1-HP-Boden |
| `DeletionUpdate` | 19 | 19 | zeitgesteuerte Zerstörung |

Kriterium: höchste Häufigkeit unter den Modulen, die ohne weiteres Subsystem (Waffen, Bewegung, KI, Upgrades, Spieler)
auskommen und Erzeugen, Ticken, Ereignisse, Sterben und Zerstören vollständig abdecken. `SlowDeathBehavior` (1.623) und
`FlammableUpdate` (843) waren zu diesem Zeitpunkt häufiger, brauchten aber jeweils ein eigenes mechanisches Modell
(gewichtete Sterbelotterie beziehungsweise Brand-/Schaden-über-Zeit-Zustandsmaschine) und folgten mit Phase 5 (siehe
`docs/GAME-RULES.md` §3, Unterabschnitt „Schadenszustände, Brand, Gift und Tod").

### Semantik der implementierten Module

- **Körper** (`ActiveBody`-Familie): Startgesundheit `InitialHealth`, Deckel `MaxHealth`, Boden 0. Tod ist der Übergang
  „Gesundheit ≤ 0 und vorherige > 0“ (`ActiveBody::attemptDamage`) und ruft die Todesmodule genau einmal in Modulreihenfolge
  auf; tote Entities nehmen keinen Schaden mehr. `Kill` = `DAMAGE_UNRESISTABLE` mit Kill-Flag und Betrag = maximale
  Gesundheit (`Object::kill`). Heilung (`DAMAGE_HEALING`) wird begrenzt. `ImmortalBody`: `delta = max(delta, −Gesundheit + 1)`,
  nie tot. `HighlanderBody`: Betrag `min(Betrag, Gesundheit − 1)` außer bei `UNRESISTABLE`. `InactiveBody`: ab Spawn
  „effectively dead“, nur unresistabler Schaden wirkt und löst die Todesmodule einmal aus.
- **Todesfilter** (`DieMuxData`): `DeathTypes` (Start immer `ALL`, `+X`/`−X`/`ALL`/`NONE`), `VeterancyLevels`,
  `ExemptStatus`, `RequiredStatus` mit den Regeln aus `DieModule.cpp`.
- **`DestroyDie`** fordert die Zerstörung an; **`KeepObjectDie`** tut nichts; **`FXListDie`** erzeugt ein
  `FxRequested`-Event (bei `StartsActive = No` ruht es – die Aktivierung gehört zum Upgradesystem); **`CreateObjectDie`**
  ein `CreationListRequested`-Event (leere Liste, wie in Kartenoverlays zum Abschalten benutzt, fordert nichts an);
  **`InstantDeathBehavior`** in der Reihenfolge Effekte → Erzeugungslisten → Waffen (`WeaponFireRequested`) und danach
  Zerstörung. Ausführen der Anforderungen ist Präsentation beziehungsweise Phase 5.
- **`LifetimeUpdate`/`DeletionUpdate`:** beim Spawn wird einmal `logic.Range(min, max)` gezogen (mindestens 1 Tick), das
  Ablaufmodul schläft bis dahin; `LifetimeUpdate` **tötet**, `DeletionUpdate` **zerstört**.
- **Feldschemata** stehen in `ModuleCatalog` (Quelle je Feld: `ActiveBody.cpp`, `DieModule.cpp`, `LifetimeUpdate.h`, …) und
  wurden gegen jede Vorkommnis im Bestand geprüft; unbekannte Schlüssel, doppelte Schlüssel, falsche Wertarten und fehlende
  Pflichtfelder (`MinLifetime`/`MaxLifetime`) sind Fehler.

Bewusst **nicht** in Phase 4: Rüstungskoeffizienten (Koeffizient 1), Schadensmultiplikatoren, Schadenszustände und
-effekte, das Furchtsignal samt `GameLogicRandomValue`-Zug (`ActiveBody::attemptDamage`), Subdual-Schaden (alle
Subdual-Typen wirken nicht auf Gesundheit), `KILLPILOT`/`KILL_GARRISONED`/`STATUS`-Schaden, das Konstruktorobjekt der
`StructureBody`. Die Zufallsverbräuche des Originals müssen für die Golden-Master-Vergleiche der Phase 5 ergänzt werden.

## 6. Befehle

Typisiert, tick-adressiert, versionierte Typtabelle, jetzt `CommandTypes.Version = 3` (Phase 5 hat acht Typen ergänzt;
bestehende Codes ändern sich nie):

| Code | Typ | Zweck | nur System |
|---:|---|---|:-:|
| 1 | `SpawnCommand` | Entity aus Template erzeugen (Owner 0 = Absender; fremde Owner nur System) | |
| 2 | `DestroyCommand` | Entity entfernen (Besitzer oder System) | |
| 3 | `DamageCommand` | Rohschaden | ✔ |
| 4 | `KillCommand` | wie `Object::kill` | ✔ |
| 5 | `SetTimerCommand` | Timer am Entity oder an der Welt (Welttimer nur Notify) | ✔ |
| 6 | `CancelTimerCommand` | Timer löschen | ✔ |
| 7 | `GiveMoneyCommand` | Guthaben eines Spielers direkt gutschreiben (Szenario-/Testwerkzeug) | ✔ |
| 8 | `PurchaseUpgradeCommand` | Upgrade kaufen (spieler- oder objektgebunden), Kosten sofort abgebucht | |
| 9 | `QueueProductionCommand` | Einheit in die Produktionswarteschlange eines Entity einreihen | |
| 10 | `CancelProductionCommand` | Warteschlangenplatz stornieren, volle Rückerstattung | |
| 11 | `SetMoveTargetCommand` | Geradlinigen Bewegungsauftrag setzen (Phase-5-Bewegungsmodell) | |
| 12 | `FireWeaponCommand` | Direkter Waffenschuss auf ein Ziel (kein Auto-Ziel, keine Sichtlinie) | |
| 13 | `GiveSciencePointsCommand` | "Science Purchase Points" eines Spielers direkt gutschreiben (kein Rang-/Erfahrungssystem) | ✔ |
| 14 | `PurchaseScienceCommand` | Science kaufen (`Player::attemptToPurchaseScience`), sofort abgeschlossen | |

`Submit` prüft sofort und lehnt **explizit, gezählt und als `CommandRejected`-Event** ab: `Late` (Tick liegt zurück),
`Duplicate` (gleiche (Spieler, Sequenz) wie die zuletzt akzeptierte), `OutOfOrder` (kleinere Sequenz), `UnknownPlayer`,
`NotPermitted`, `InvalidArguments` (Sequenz 0, negative Werte, ungültige Typen), `UnknownTemplate` (unbekannt oder nicht
instanziierbar). Zur Ausführungszeit folgen `UnknownEntity`, `NotPermitted` (fremdes Entity) und `UnknownTimer`; Phase 5
ergänzt `InsufficientFunds`, `AlreadyOwned`, `UnknownUpgrade`, `NoWeapon`, `WeaponNotReady`, `OutOfRange`, `QueueFull`,
`CannotMove`, `CannotProduce`, `UnknownScience`, `ScienceNotPurchasable`, `InsufficientSciencePurchasePoints`,
`MissingPrerequisite` (Details und Numerik: `docs/GAME-RULES.md` §4/§1a). Sequenzen sind pro Spieler streng
aufsteigend; Befehle desselben Ticks laufen nach (Spieler-ID, Sequenz), unabhängig von der Einreihreihenfolge. Die Zähler
je Grund gehören zum Zustand. **Wichtig:** `Validate()` prüft beim `Submit()` gegen den *aktuellen* Weltzustand, nicht
gegen den Zustand nach zuvor im selben Tick eingereihten Befehlen – ein `GiveMoneyCommand` und ein darauf aufbauender
`PurchaseUpgradeCommand` im selben Tick sehen die Gutschrift erst, wenn dazwischen ein `Step()` ausgeführt wurde.

## 7. Snapshots und Hashes

**Zustands-Hash.** Ein 64-Bit-Hash aus SplitMix64-Schritten (`StateHash.Mix`), nicht kryptographisch (er erkennt
Abweichungen, er widersteht keinen Angriffen). Begründung der Wahl: Es ist keine Paketabhängigkeit nötig
(`System.IO.Hashing` wäre ein neues NuGet-Paket), das Ergebnis ist unabhängig von Endianness und Hardwareintrinsics, und
die Eingabe sind ohnehin kanonisierte Ganzzahlen. **Inkrementell:** Pro Entity wird der Hash über dieselbe
`Describe`-Traversierung berechnet, die auch den Snapshot schreibt (ein einziger Ort für „was ist Zustand“); die
Entity-Hashes werden addiert, ein Entity wird nur neu gehasht, wenn der Kern es in diesem Tick berührt hat (jeder Aufruf in
ein Modul markiert es). Timer und ausstehende Befehle sind ebenso Summen; die kleinen globalen Teile (Tick, ID-Zähler,
`logic`-Zustand, Spielertabelle, Ablehnungszähler, Spawn-/Destroy-Queue, ausstehende Events) werden jeden Tick neu
gehasht. `VerifyIncrementalHash()` vergleicht mit einer vollständigen Neuberechnung (Tests und Stresslauf prüfen das
regelmäßig, der Snapshot-Restore immer).

**Snapshot.** `WorldSnapshot.Write` erzeugt kanonisches UTF-8-JSON (feste Eigenschaftsreihenfolge, nur Ganzzahlen –
`Fixed` als Rohwert –, keine Pfade oder Zeitstempel, jede Sammlung nach ID beziehungsweise (Tick, Spieler, Sequenz)
sortiert): Kopf `format = cncnew-world-snapshot`, `version = 1`, `tickRate`, `definitionFingerprint`, `fixture`, `worldId`,
`seed`, `stateHash`, danach `state` (Tick, ID-Zähler, `logic`-Zustand, Spielertabelle, Ablehnungszähler, Spawn-/Destroy-Queue,
Events, Entities mit Modulzuständen, Timer, Befehle) und `presentation` (`cosmetic`, `audio`). **SHA-256** identifiziert einen
Snapshot (`WorldSnapshot.Sha256`). `Restore` ist bewusst minimal und intern gehalten (Phase 9 besitzt den späteren
Speichervertrag) und weist ab: leere/abgeschnittene/verstümmelte Dateien, fremde Version/Format/Tickrate, abweichenden
Definitionsfingerabdruck, abweichende Templates (Name und Index werden gegenseitig geprüft) und jeden Zustand, dessen
wiederberechneter Hash nicht dem gespeicherten entspricht (Integritätsprüfung).

**Definitionsfingerabdruck.** SHA-256 des kanonischen Definitionssnapshots, beim Schreiben gestreamt
(`DefinitionSnapshotWriter.ComputeFingerprint`), ohne den 142-MB-Snapshot im Speicher zu halten.

## 8. Replay

Binärformat, little endian: `CNCRPLAY`, u32 Formatversion, u32 Befehlstabellenversion, u32 Tickrate, 32 Byte
Definitionsfingerabdruck, Fixture-ID, u32 Seed, u32 Welt-ID, Spielertabelle, End-Tick, Checkpointintervall, Befehle
(sortiert nach (Tick, Spieler, Sequenz)), Checkpoints (Tick, Hash) und ein abschließendes SHA-256 über alles Vorherige.
`ReplayFormat.Read` weist ab: falsche Magic, fremde Version (vor der Hashprüfung, mit klarer Meldung), abgeschnittene oder
verstümmelte Dateien (Hashabweichung), überzählige Bytes, unbekannte Befehlstypen, unsortierte Befehle. `ReplayRunner`
lehnt ein Replay ab, dessen Fingerabdruck, Befehlstabellenversion oder Tickrate nicht passt, und meldet Abweichungen an
Checkpoints als `ReplayDivergenceException` mit Tick und Hashes. Die Fortsetzung (Snapshot → Restore → Weiterlauf) nutzt
denselben Runner und beginnt beim Tick der Welt.

## 9. Abnahmebefehle

```powershell
dotnet build CnCNew.slnx
dotnet run --project tests/CnCNew.Simulation.Tests
dotnet run --project tests/CnCNew.Simulation.Tests -- --content-dir .local/phase2-import-a
dotnet run -c Release --project tests/CnCNew.Simulation.Tests -- --long
dotnet run --project tests/CnCNew.Data.Tests -- --content-dir .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- stress
dotnet run -c Release --project tools/CnCNew.SimTool -- replay-record .local/stress.replay
dotnet run -c Release --project tools/CnCNew.SimTool -- replay-run .local/stress.replay
dotnet run -c Release --project tools/CnCNew.SimTool -- hash .local/stress.replay
dotnet run -c Release --project tools/CnCNew.SimTool -- audit-modules .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- audit-coverage .local/phase2-import-a
dotnet run --project tools/CnCNew.IniTool -- compat-check .local/phase2-import-a baselines/compatibility.json
dotnet run --project tools/CnCNew.IniTool -- load-order-report .local/phase2-import-a
```

## 10. Stress, Speicher und Budgets

`SimTool stress` nimmt das synthetische Szenario (`StressScenario`, Fixture `synthetic-lifecycle-v1`, ausschließlich
eigene, rechtlich unbedenkliche Definitionen) als Replay auf und führt es mehrfach aus. Der Treiber beobachtet nur
Welt-Events und einen eigenen `SageRandom`; Last: Rampe auf 10.500 langlebige Entities, danach je Tick ein Kill/Destroy
mit Ersatz-Spawn, alle 2 Ticks ein Funke, alle 3 Ticks ein Puff, alle 40 Ticks ein unsterbliches Entity, zufälliger Schaden,
Besitzer-Destroys, Timer (wiederholende Notify- und Damage-Timer an etwa 45 % der neuen Entities, Welttimer), bewusst
ungültige Befehle; die letzten 6.000 Ticks zerstören alle Überlebenden.

Gemessen am 28. September 2026 (Release, `dotnet run -c Release`) auf: AMD64 Family 25 Model 68 Stepping 1
(AuthenticAMD), 16 logische Prozessoren, 28 GiB RAM, Windows 11 (10.0.26200), .NET 10.0.12, Workstation-GC:

| Größe | Messwert |
|---|---|
| Ticks | 216.000 (zwei Stunden Spielzeit) |
| Befehle | 914.771 (Replay-Datei 47.118.372 Byte, 216 Checkpoints) |
| Spawn/Destroy | 401.229 / 401.229 |
| gleichzeitig lebende Entities | Spitze 15.774 (Ziel ≥ 10.000) |
| Module / Timer / geplante Updates (Spitze) | 42.105 / 7.099 / 30 |
| Laufzeit je 216.000-Tick-Lauf | 22 s (Ø 102 µs je Tick), langsamster Tick 30,7 ms |
| Aufnahme des Szenarios (Treiber + Welt) | 37 s |
| Allokation `Step()` im Beharrungszustand | Ø 1.350 Byte je Tick unter Churn (Entity- und Modulobjekte der Spawns); **0 Byte** bei ruhender Welt (Test) |
| Snapshot bei Tick 108.000 | 6.418.840 Byte für 13.220 Entities (≈ 485 Byte je Entity), Restore 0,7 s |
| Peak Working Set des Prozesses | 532 MiB (einschließlich der 915.000 Befehlsobjekte des Aufnehmers) |
| Fehlerbefehle | 2.166 `UnknownEntity`-Ablehnungen, in jedem Lauf identisch |

Ergebnisse: drei frische Läufe liefern identische Tickhashes über alle 216.000 Ticks (Kettenhash `E230DA5F04821478`,
Endhash `A299C38BFE07681D`); der Lauf mit Snapshot bei Tick 108.000 und Fortsetzung liefert dieselben 216.000 Hashes;
Entity-, Modul-, Update-, Timer-, Event-, Befehls-, Spawn- und Destroy-Tabellen stehen am Ende auf null; kein Deadlock
(keine Threads, alle Schleifen begrenzt); der inkrementelle Hash stimmte in jeder Stichprobe mit der vollständigen
Neuberechnung überein.

**Budgets** (mit Messwert als Grundlage, etwa 2,7-facher Spielraum): 216.000-Tick-Lauf dieses Szenarios ≤ 60 s;
Ø ≤ 300 µs je Tick; langsamster Tick ≤ 100 ms (echtes Echtzeitbudget, 33,3 ms bei 30 Hz, wird erst mit den Regeln der
Phase 5 unter realer Last festgelegt); Working Set ≤ 1 GiB; ruhender Tick 0 Byte, Churn ≤ 4 KiB je Tick; Snapshot ≤ 1 KiB
je Entity. Die Zahlen sind Regressionsschwellen, keine Leistungszusagen für das Spiel.

## 11. Modulabdeckung des Bestands

`SimTool audit-coverage` kompiliert den Retail-Basisbestand (ohne Kartenoverlays; Phase 5 hat den INI-Parser korrigiert –
siehe §12 – wodurch sich die Objektzahl von 2.113 auf **2.115** und der Fingerabdruck ändern; 12.550 Definitionen,
Fingerabdruck `2FAE6DCC…`) und instanziiert jedes Objekt, das nur implementierte oder Präsentationsmodule besitzt:

- **2.115 Objekte, 694 vollständig instanziierbar (32,8 %)**, 1.421 blockiert. Gegenüber Phase 4 (26/2.113, 1,2 %) ein
  Sprung um den Faktor ~27, getragen von Block 0.2 (`StealthUpdate`, `AutoHealBehavior` – beide vom `DefaultThingTemplate`
  geerbt und fast immer ruhend), den elf `*Upgrade`-Modulen, `AIUpdateInterface`, `ProductionUpdate` und den sechs
  weiteren Blockermodulen dieses Abschnitts (`FlammableUpdate`, `TransitionDamageFX`, `SlowDeathBehavior`,
  `FireWeaponWhenDeadBehavior` +3, `StealthDetectorUpdate` +9, `PoisonedBehavior` +0).
- Alle 694 instanziierbaren Objekte werden in einer Welt erzeugt, ticken 216.000 Ticks im Stresstest ohne Leck oder
  Nichtdeterminismus (`SimTool stress`, §10) und laufen im Realdatentest identisch zweimal durch (Test `--content-dir`).
- Verbleibende häufigste Blocker (Objekte / davon nur durch dieses Modul blockiert, nach einer frischen Messung):
  `PhysicsBehavior` 739/119, `GarrisonContain` 200/167, `CreateCrateDie` 184/0, `SquishCollide` 170/2,
  `SpecialAbility` 88/0, `TransportContain` 85/2, `CommandButtonHuntUpdate` 79/0, `ToppleUpdate` 76/66.
  `GarrisonContain`/`TransportContain` brauchen ein komplettes Containment-Subsystem (Kapazität, Ein-/Aussteigen,
  Garnisonspunkte); `PhysicsBehavior`/`SquishCollide` brauchen Terrain-/Kollisionsphysik (Phase 6); `CreateCrateDie`
  bräuchte einen neuen `Crate`-Katalog und eine Beziehungs-/Diplomatieprüfung für 0 sofortigen Nutzen – alle drei sind
  bewusst zurückgestellt (Begründung: `Phase5BlockerModules`-Doc-Kommentar, `docs/HANDOFF-PHASE5.md`). `ToppleUpdate`
  (66 allein) ist frisch sichtbar geworden und noch nicht bewertet. Die vollständige, tagesaktuelle Liste liefert das
  Werkzeug.

## 12. Bekannte Grenzen und technische Schulden

- **Phase 5 (aktueller Stand)**: Wirtschaft, Upgrades, direkter Kampf, einfache Bewegung, Produktion und
  Sieg/Niederlage sind implementiert (`docs/GAME-RULES.md`), aber bewusst vereinfacht: kein Terrain/Pfadsuche, kein
  Flächenschaden (nur Volltreffer), keine `WeaponBonus`-Multiplikatoren, nur zwei `ArmorSet`/`WeaponSet`-Bedingungen
  (`None`, `PLAYER_UPGRADE`), kein Sciences-/Tech-Baum, keine Teams/Diplomatie, kein Garrison/Transport/Supply, keine
  Reparatur/Verkauf/Radar-Wirkung, keine Erfahrungs-/Veteranenvergabe aus Kämpfen. Jede Vereinfachung steht mit
  Quellverweis im Modul- oder Katalog-Kommentar.
- Ein INI-Parser-Fehler wurde in Phase 5 behoben: `SageIniParser` verglich das `End`-Schlüsselwort gegen die *gesamte*
  Zeile statt (wie das Original, `INI::initFromINIMulti`, `strtok`) nur gegen ihr erstes Token; zwei Objekte
  (`AmericaVehicle.ini`, `BossGeneral.ini`) tragen einen Tippfehler `End Upgrade_AmericaHallfireDrone`, den das Original
  toleriert, der alte Parser aber als offenen Block fortsetzte. Dadurch ändert sich die Objektzahl leicht (2.113 → 2.115)
  und der Definitionsfingerabdruck.
- Die Update-Phasen des Originals (`SleepyUpdatePhase`) sind nicht modelliert.
- Der Fear-Sound-Zufallsverbrauch (`ActiveBody::attemptDamage`, `GameLogicRandomValue(0,99)` bei Unterschreiten der
  25-%-Schwelle) ist portiert (Block 0.3 abgeschlossen, `VoiceFearRequested`-Event, Test `Phase5FearSoundRng`).
- Der Abgleich des RNG mit einem kompilierten Original steht aus (kein Compiler verfügbar); die Handrechnung ist unabhängig
  und reicht als Beleg für Algorithmusgleichheit, nicht als Beleg für die Weiterverwendung in Netzwerkspielen.
- `Restore` ist intern; Vorwärts-/Rückwärtskompatibilität von Snapshots (Version 1) ist erst Thema von Phase 9.
- Der Zustands-Hash ist nicht kryptographisch und die Snapshot-JSON-Größe (≈ 485 Byte je Entity) ist nicht optimiert.
- Vom Kern erzeugte Anforderungen (`FxRequested`, `CreationListRequested`, `WeaponFireRequested`, `VoiceFearRequested`)
  werden nirgends ausgeführt; sie sind Verträge für Präsentation.
- Die Definitionsdomänen außer Object, Weapon, Armor, Upgrade, Science und Locomotor sind weiterhin flach typisiert
  (Zahlen in `TYPED-DEFINITIONS.md`): CommandButton, SpecialPower, Faction, GameData, ObjectCreationList.
- Kein headless Referenzmatch aller Fraktionen und keine Golden-Master-Szenarien gegen Originalwerte (Phase-5-Punkte 7/8
  aus `docs/HANDOFF-PHASE5.md` stehen noch aus); die neuen Phase-5-Mechaniken sind stattdessen durch gezielte
  Integrationstests (`Tests.Phase5.cs`) und den 216.000-Tick-Stresstest mit realen Objekten abgesichert.
