# Deterministischer Simulationskern (Phase 4, erweitert in Phase 5 und 6)

Stand: 30. September 2026 (Phase 6 abgeschlossen, Phase 8 Paket E1: Projektilflugzeit, Transporter, Tunnel, Chinook; §1–§12 beschreiben den Kern und die Phase-5-Regeln, `docs/GAME-RULES.md`
die Spielregeln, §13–§20 die Phase-6-Erweiterungen: Terrain, Navigation, Sicht, Bau, Kraftwerke, Skirmish-KI, §21 das Paket E1)

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
| 5a | Verzögerte Treffer | fällige Treffer von Waffen ohne Projektilobjekt in Einfügereihenfolge (`WeaponStore::update`, Phase 8, §21) |
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
geschlossenen `AddModule`, dessen Felder das Original auf das Objekt anwendet). Phase 5 hat **35** dieser 205 Typen
implementiert (12 aus Phase 4 plus 23 neue: `StealthUpdate`, `AutoHealBehavior`, die elf `*Upgrade`-Module,
`AIUpdateInterface`, `ProductionUpdate`, `FlammableUpdate`, `TransitionDamageFX`, `SlowDeathBehavior`,
`FireWeaponWhenDeadBehavior`, `StealthDetectorUpdate`, `PoisonedBehavior`, `GarrisonContain`, `TransportContain` –
siehe `docs/GAME-RULES.md` §3/§6/§6a). `CnCNew.Simulation` besitzt die
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

Die Module ab Phase 5 (Versorgung und Andocken, Spawner, Reparatur, `FireWeaponUpdate`, `ReplaceObjectUpgrade`, `CrushDie`, Erfahrung und
Veteranenstufen, Teams) sind mit Katalogschema, Bindung, Tests und Semantik in `docs/GAME-RULES.md` §6e–§6h und §7 beschrieben; die
tagesaktuelle Zählung liefert `SimTool audit-modules`.

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
  ein `CreationListRequested`-Event (leere Liste, wie in Kartenoverlays zum Abschalten benutzt, fordert nichts an) und
  führt die Liste seit Phase 5 aus (`SimulationWorld.CreateFromList`, `docs/GAME-RULES.md` §6b);
  **`InstantDeathBehavior`** in der Reihenfolge Effekte → Erzeugungslisten → Waffen (`WeaponFireRequested`) und danach
  Zerstörung. Erzeugungslisten werden ausgeführt (eine Liste je Modul, Zug wie im Original); `FxRequested` und
  `WeaponFireRequested` bleiben Verträge für die Präsentation beziehungsweise spätere Phasen.
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

Typisiert, tick-adressiert, versionierte Typtabelle, jetzt `CommandTypes.Version = 9` (Phase 5 hat 13 Typen ergänzt, Version 6
brachte `DockCommand`, Version 7 `SetRelationshipCommand`, Version 8 die sechs Baubefehle der Phase 6, Version 9 `MoveAndEnterCommand` und `EvacuateCommand`; bestehende Codes ändern sich nie):

| Code | Typ | Zweck | nur System |
|---:|---|---|:-:|
| 1 | `SpawnCommand` | Entity aus Template erzeugen (Owner 0 = Absender; fremde Owner nur System) | |
| 2 | `DestroyCommand` | Entity entfernen (Besitzer oder System) | |
| 3 | `DamageCommand` | Rohschaden | ✔ |
| 4 | `KillCommand` | wie `Object::kill` | ✔ |
| 5 | `SetTimerCommand` | Timer am Entity oder an der Welt (Welttimer nur Notify) | ✔ |
| 6 | `CancelTimerCommand` | Timer löschen | ✔ |
| 7 | `GiveMoneyCommand` | Guthaben eines Spielers direkt gutschreiben (Szenario-/Testwerkzeug; der Referenzmatch schreibt es nicht mehr) | ✔ |
| 8 | `PurchaseUpgradeCommand` | Upgrade kaufen (spieler- oder objektgebunden), Kosten sofort abgebucht | |
| 9 | `QueueProductionCommand` | Einheit in die Produktionswarteschlange eines Entity einreihen | |
| 10 | `CancelProductionCommand` | Warteschlangenplatz stornieren, volle Rückerstattung | |
| 11 | `SetMoveTargetCommand` | Geradlinigen Bewegungsauftrag setzen (Phase-5-Bewegungsmodell) | |
| 12 | `FireWeaponCommand` | Direkter Waffenschuss auf ein Ziel (kein Auto-Ziel, keine Sichtlinie) | |
| 13 | `GiveSciencePointsCommand` | "Science Purchase Points" eines Spielers direkt gutschreiben (Rang aus Erfahrung ist nicht modelliert) | ✔ |
| 14 | `PurchaseScienceCommand` | Science kaufen (`Player::attemptToPurchaseScience`), sofort abgeschlossen | |
| 15 | `EnterContainerCommand` | Entity in einen Container stellen (`ContainModuleInterface::addToContain`), sofort, keine Pfadsuche | |
| 16 | `ExitContainerCommand` | Entity aus seinem Container entfernen (Besitzer der Einheit oder des Containers) | |
| 17 | `UseSpecialPowerCommand` | Spezialkraft eines Objekts auslösen (`ActionManager::canDoSpecialPower`, Ziel: keines, Objekt oder Ort) | |
| 18 | `DockCommand` | Ein Objekt zum Andocken an ein Dock schicken (`aiDock`: Versorger liefern ab oder holen, das Dock wird ihr bevorzugtes) | |
| 19 | `SetRelationshipCommand` | Gerichtete Beziehung eines Spielers zu einem anderen setzen (`Player::setPlayerRelationship`) | ✔ |
| 20 | `ConstructCommand` | Baumeister (Dozer/Arbeiter) setzt ein Gebäude an eine Position mit Winkel (`MSG_DOZER_CONSTRUCT`, §16) | |
| 21 | `SellCommand` | Gebäude verkaufen (§16) | |
| 22 | `SetRallyPointCommand` | Sammelpunkt eines Produktionsgebäudes auf einen Ort setzen (§16) | |
| 23 | `ResumeConstructCommand` | Unterbrochene Baustelle mit einem (anderen) Baumeister fortsetzen (`DozerAIUpdate::resumeConstruction`) | |
| 24 | `CancelConstructCommand` | Baustelle abbrechen, Kosten werden erstattet | |
| 25 | `RepairCommand` | Baumeister repariert ein beschädigtes eigenes Gebäude (`MSG_REPAIR`, kostenlos, §16) | |
| 26 | `MoveAndEnterCommand` | Eigenes Objekt läuft zu einem Container (Transporter, Tunnel, Gebäude) und steigt bei Ankunft ein (`aiEnter`, §21) | |
| 27 | `EvacuateCommand` | Besitzer entlädt einen Container, beweglich wahlweise erst an einer Position; ein Tunnel entlässt das ganze Netz (`MSG_EVACUATE`, §21) | |

`Submit` prüft sofort und lehnt **explizit, gezählt und als `CommandRejected`-Event** ab: `Late` (Tick liegt zurück),
`Duplicate` (gleiche (Spieler, Sequenz) wie die zuletzt akzeptierte), `OutOfOrder` (kleinere Sequenz), `UnknownPlayer`,
`NotPermitted`, `InvalidArguments` (Sequenz 0, negative Werte, ungültige Typen), `UnknownTemplate` (unbekannt oder nicht
instanziierbar). Zur Ausführungszeit folgen `UnknownEntity`, `NotPermitted` (fremdes Entity) und `UnknownTimer`; Phase 5
ergänzt `InsufficientFunds`, `AlreadyOwned`, `UnknownUpgrade`, `NoWeapon`, `WeaponNotReady`, `OutOfRange`, `QueueFull`,
`CannotMove`, `CannotProduce`, `UnknownScience`, `ScienceNotPurchasable`, `InsufficientSciencePurchasePoints`,
`MissingPrerequisite`, `CannotEnterContainer` (KindOf-/Beziehungsfilter, Kapazität oder ein zu stark beschädigter/toter
Container), `NotContained` sowie für Spezialkräfte `UnknownSpecialPower`, `MissingScience`, `SpecialPowerNotReady`,
`InvalidSpecialPowerTarget` und `SpecialPowerHandlerMissing` sowie für das Andocken `CannotDock` (Code 30; Details und Numerik:
`docs/GAME-RULES.md` §4/§1a/§6a/§6d/§6e). Phase 6 ergänzt `TargetNotVisible` (31, `FireWeaponCommand` auf ein für den Spieler unsichtbares Ziel, §15), `InvalidPlacement` (32, Bauplatz
unzulässig: Gelände, Fußabdruck, Hindernis oder unerkundet), `CannotConstruct` (33: Baumeister, Vorlage, Voraussetzung oder Rolle passen nicht) und `CannotSell` (34). Sequenzen sind pro Spieler streng
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
sortiert): Kopf `format = cncnew-world-snapshot`, `version = 7` (2 ergänzte die Teams der Spielertabelle, 3 das Computerspieler-Flag, 4 den Geländefingerabdruck, 5 die Sichtkarten – der Sicht-Hash ist Teil des Zustands-Hashes –, 6 Bau-, Verkaufs- und Sammelpunktzustand, 7 siehe unten), `tickRate`, `definitionFingerprint`, `fixture`, `worldId`,
`seed`, `stateHash` (Version 7, Phase 8: Tunnelnetz je Spieler, verzögerte Treffer, Startdaten eines Projektils in der Spawn-Queue, Ereignisposition), danach `state` (Tick, ID-Zähler, `logic`-Zustand, Spielertabelle, Ablehnungszähler, Spawn-/Destroy-Queue,
Events, Entities mit Modulzuständen, Timer, Befehle) und `presentation` (`cosmetic`, `audio`). **SHA-256** identifiziert einen
Snapshot (`WorldSnapshot.Sha256`). `Restore` ist bewusst minimal und intern gehalten (Phase 9 besitzt den späteren
Speichervertrag) und weist ab: leere/abgeschnittene/verstümmelte Dateien, fremde Version/Format/Tickrate, abweichenden
Definitionsfingerabdruck, abweichende Templates (Name und Index werden gegenseitig geprüft) und jeden Zustand, dessen
wiederberechneter Hash nicht dem gespeicherten entspricht (Integritätsprüfung).

**Energiewiederherstellung (Phase 6).** Die Energieproduktion eines Spielers wird nach dem Laden der Entities explizit aus dem Snapshot
gesetzt, denn `PowerPlantUpgrade` und `OverchargeBehavior` verändern sie über `IWorldServices.AddEnergyProduction` und würden
sonst beim Registrieren der Module doppelt oder gar nicht angerechnet (der Integritätshash schlug im Test dadurch fehl).

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

**Format v2 (Phase 5).** Die Spielertabelle trägt zusätzlich den Fraktionsnamen (leer = keine Fraktion), damit ein Replay
ohne Nebeninformation einem Fraktionsmatch zugeordnet werden kann; Version 1 wird mit klarer Meldung abgewiesen. Außerdem
kodiert das Format jetzt **alle 17 Befehlstypen** (zuvor nur die sechs aus Phase 4): `GiveMoney`, `PurchaseUpgrade`,
`QueueProduction`, `CancelProduction`, `SetMoveTarget`, `FireWeapon`, `GiveSciencePoints`, `PurchaseScience`,
`EnterContainer`, `ExitContainer` und `UseSpecialPower` sind hinzugekommen. `Tests.ReferenceMatch` schreibt und liest jeden Typ zurück und
vergleicht die Befehle; ein unbekannter Typ im Schreiber ist ein Fehler, keine stille Auslassung.

**Format v3 und v4.** v3 ergänzt je Spieler die Team-Nummer (`PlayerConfiguration.Team`, 0 = kein Team), v4 das Computerspieler-Flag
(`IsComputer`, Voraussetzung für `AutoFindHealingUpdate`). Die Befehlstabelle kodiert inzwischen **alle 19 Befehlstypen**
(`DockCommand` und `SetRelationshipCommand` sind hinzugekommen); ältere Formatversionen werden mit klarer Meldung abgewiesen.

**Format v7 (Phase 8, E1).** Kodiert zusätzlich `MoveAndEnterCommand` (u32 Akteur, u32 Container) und `EvacuateCommand` (u32 Container, bool Position,
i64 X, i64 Y roh); Befehlstabelle Version 9, **alle 27 Befehlstypen**; v6 wird mit klarer Meldung abgewiesen (`Tests.Transports`).

**Format v5 und v6 (Phase 6).** v5 trägt den Geländefingerabdruck im Kopf (`TerrainMap.Fingerprint`, 32 Byte; der Runner lehnt ein
Replay ab, dessen Gelände nicht passt, wie beim Definitionsfingerabdruck); v6 kodiert die sechs Baubefehle. Die Befehlstabelle
umfasst damit **alle 25 Befehlstypen**; `Tests.Construction` (`ConstructionSnapshotAndReplay`) prüft Snapshot und Replay mit Baubefehlen.

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
dotnet run -c Release --project tools/CnCNew.SimTool -- reference-match .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- ai-match .local/phase2-import-a --map "Maps/Alpine Assault/Alpine Assault.map" --factions China,GLA --max-ticks 45000
dotnet run --project tools/CnCNew.MapTool -- audit .local/phase2-import-a
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

Erstmessung am 28. September 2026 (Release, `dotnet run -c Release`) auf: AMD64 Family 25 Model 68 Stepping 1
(AuthenticAMD), 16 logische Prozessoren, 28 GiB RAM, Windows 11 (10.0.26200), .NET 10.0.12, Workstation-GC:

| Größe | Messwert |
|---|---|
| Ticks | 216.000 (zwei Stunden Spielzeit) |
| Befehle | 914.394 (Replay-Datei 47.094.803 Byte im Format v2, 216 Checkpoints) |
| Spawn/Destroy | 401.237 / 401.237 |
| gleichzeitig lebende Entities | Spitze 15.775 (Ziel ≥ 10.000) |
| Module / Timer / geplante Updates (Spitze) | 42.105 / 7.099 / 30 |
| Laufzeit je 216.000-Tick-Lauf | 22 s (Ø 102 µs je Tick), langsamster Tick 30,7 ms |
| Aufnahme des Szenarios (Treiber + Welt) | 37 s |
| Allokation `Step()` im Beharrungszustand | Ø 1.350 Byte je Tick unter Churn (Entity- und Modulobjekte der Spawns); **0 Byte** bei ruhender Welt (Test) |
| Snapshot bei Tick 108.000 | 10.262.399 Byte für 13.220 Entities (≈ 776 Byte je Entity), Restore 0,7 s (Restore-Zeit nicht neu gemessen) |
| Peak Working Set des Prozesses | 532 MiB (einschließlich der 915.000 Befehlsobjekte des Aufnehmers) |
| Fehlerbefehle | 2.170 `UnknownEntity`-Ablehnungen, in jedem Lauf identisch |

Ergebnisse: drei frische Läufe liefern identische Tickhashes über alle 216.000 Ticks (Kettenhash `E230DA5F04821478`,
Endhash `A299C38BFE07681D`); der Lauf mit Snapshot bei Tick 108.000 und Fortsetzung liefert dieselben 216.000 Hashes;
Entity-, Modul-, Update-, Timer-, Event-, Befehls-, Spawn- und Destroy-Tabellen stehen am Ende auf null; kein Deadlock
(keine Threads, alle Schleifen begrenzt); der inkrementelle Hash stimmte in jeder Stichprobe mit der vollständigen
Neuberechnung überein.

**Neumessung am 29. September 2026** (nach Erzeugungslistenausführung und `CreateCrateDie`; Kette und Endhash haben sich
geändert, weil `CreateObjectDie`/`InstantDeathBehavior` jetzt Zufallszüge verbrauchen und Objekte erzeugen): Kettenhash
`36211DD938214B9C`, Endhash `9E4AF85CBFBB84C6`, 401.237 Spawns und ebenso viele Destroys, Ø 54,7 µs je Tick, Snapshot
bei Tick 108.000: 10.262.399 Byte. Alle Läufe (unabhängig wiederholt, mit Snapshotfortsetzung) stimmen tickweise überein. Die
Befehls-, Entity- und Fehlerzahlen sowie Snapshotgröße der Tabelle sind ebenfalls neu gemessen (Replay v2); die übrigen Größen nicht.

**Neumessung nach dem Spezialkraft-Subsystem** (29. September 2026, zehn weitere Module; die synthetische Fixture ist
unverändert, aber Spielerzustand und Snapshot tragen jetzt Kopfgeldrate und geteilte Bereitschaftsframes): Kettenhash
`76A8FCEEEE625672`, Endhash `4A99EE46FD623A09`, 914.394 Befehle, Spitze 15.775 Entities, Snapshot bei Tick 108.000:
10.262.573 Byte (SHA-256 `A14A06E08A3C05796FF59E5B94E9AC1F0E31F45A5CABE4E1285C3A578AC896A0`), Hash nach Restore
`1D2E96E0BB12303A`; drei frische Läufe und die Snapshotfortsetzung stimmen tickweise überein, Stresslauf 1.950 Byte je Tick,
ruhender Tick 0 Byte (der Bereitschaftszustand nutzt eine Indexschleife über die sortierte Liste, keine Enumeratorallokation).

**Neumessung zum Abschluss von Phase 5** (29. September 2026, 74 gebundene Module, Supply, Erfahrung, Teams, Reparatur, Flächenschaden; die
synthetische Fixture ist unverändert, Snapshot Version 3, Replay Format v4): Kettenhash `8E1FA50D21433123`, Endhash `D80B367CEEF6D72D`,
Replay 47.094.813 Byte (SHA-256 `AF97976870D69C52E48C889A753E955D85C7DDE94F5061A3F8AEF4610310B0CC`), 914.394 Befehle, 216 verifizierte
Checkpoints, Ø 59,9 µs je Tick (12,9 s je Lauf), langsamster Tick 21,07 ms, Spitze 15.775 Entities, 42.064 Module und 7.048 Timer, 401.237
Spawns und ebenso viele Destroys, 2.170 `UnknownEntity`-Ablehnungen, Snapshot bei Tick 108.000: 11.135.228 Byte (SHA-256
`0372D5025C1C2433435BB03F06E77ACC1E40D5D7B55C8A136718D221C8CEAFBA`), Restore 0,66 s, Hash nach Restore `413FAD6458E026C6`, Allokation
1.994 Byte je Tick unter Churn, Spitzenarbeitsspeicher 596 MiB. Drei frische Läufe und die Snapshotfortsetzung stimmen tickweise überein;
alle Tabellen stehen am Ende auf null. Die älteren Neumessungen oben sind überholt und nur als Verlauf stehen geblieben.

**Neumessung am 30. September 2026** (Phase 6, Release, gleiche Maschine; Snapshot v6, Replay v6, Sichtkarten im Zustands-Hash;
das synthetische Szenario nutzt weder Gelände noch Navigation noch Bau): 216.000 Ticks in 13,0 s (60,2 µs je Tick), 914.394 Befehle,
langsamster Tick 18,93 ms, Allokation 2.054 Byte je Tick unter Churn, Spitzen 15.775 Entities / 42.064 Module / 29 geplante Updates /
7.048 Timer, Kettenhash `C298F5F74993D6CD`, Endhash `D1521974C861A4B9`. Snapshot bei Tick 108.000: 11.862.400 Byte für 13.220 Entities,
SHA-256 `7E3DBFDCBCD4D46502FA0C571598E826343C14D23F97A0D2919BF53661009A60`, Restore 0,59 s (Hash `ACCDC2E5315991DB`). Working Set (Spitze)
596 MiB. Drei frische Läufe und die Snapshotfortsetzung stimmen tickweise überein; 401.237 Spawns/Destroys und 2.170 `UnknownEntity`-Ablehnungen
wie zuvor. Kettenhash und Endhash unterscheiden sich von denen vom 29. September, weil sich die Zustandsdarstellung (Sicht-Hash,
Terrain-Fingerabdruck) und die Stealth-Regel (§15) geändert haben; das Szenario selbst blieb unverändert.

**Neumessung Phase 8, Paket E1** (30. September 2026, Release, gleiche Maschine, parallel zu einer KI-Auswertung gemessen, daher langsamer; Snapshot v7, Replay v7):
216.000 Ticks, 914.394 Befehle, Replay 47.094.815 Byte, Lauf 1 20,6 s (95,5 µs je Tick), langsamster Tick 31,34 ms, Allokation 2.158 Byte je Tick unter Churn,
Spitzen 15.775 Entities / 42.064 Module / 29 geplante Updates / 7.048 Timer, **Kettenhash `3AECEB0579E460AF`, Endhash `ED6F9329F59662E3`**; Snapshot bei
Tick 108.000 11.862.512 Byte (SHA-256 `6A8D0B9BD9F64F22384FF8A39D7D1AA25F0C8D0D959527D6365CC3E572DBBA8C`), Restore 0,72 s (Hash `3D5CD16AC1FEAA30`), Working Set 567 MiB.
Drei frische Läufe und die Fortsetzung stimmen tickweise überein, 401.237 Spawns/Destroys, 2.170 `UnknownEntity`. Die Hashes ändern sich, weil der globale
Zustand jetzt Tunnelnetz je Spieler und die (leere) Liste verzögerter Treffer beschreibt; die synthetische Fixture hat keine Projektile.

**Budgets** (mit Messwert als Grundlage, etwa 2,7-facher Spielraum): 216.000-Tick-Lauf dieses Szenarios ≤ 60 s;
Ø ≤ 300 µs je Tick; langsamster Tick ≤ 100 ms (echtes Echtzeitbudget, 33,3 ms bei 30 Hz, wird erst mit den Regeln der
Phase 5 unter realer Last festgelegt); Working Set ≤ 1 GiB; ruhender Tick 0 Byte, Churn ≤ 4 KiB je Tick; Snapshot ≤ 1 KiB
je Entity. Die Zahlen sind Regressionsschwellen, keine Leistungszusagen für das Spiel.

## 11. Modulabdeckung des Bestands

`SimTool audit-coverage` kompiliert den Retail-Basisbestand (ohne Kartenoverlays; Phase 5 hat den INI-Parser korrigiert –
siehe §12 – wodurch sich die Objektzahl von 2.113 auf **2.115** und der Fingerabdruck ändern; 12.550 Definitionen,
Fingerabdruck `2FAE6DCC…`) und instanziiert jedes Objekt, das nur implementierte oder Präsentationsmodule besitzt:

- **Stand nach Phase 10, Kollision (1. Oktober 2026, frisch gemessen): 2.115 Objekte, 1.588 vollständig instanziierbar (75,1 %)**, 527 blockiert; 90 von 205 Modultypen
  implementiert (3 neu: `ToppleUpdate`, `StructureToppleUpdate`, `HeightDieUpdate`, §23), sie decken 15.752 von 19.553 Modulverwendungen (80,6 %); Fingerabdruck `D112D8B5AF345559…`.
  `ToppleUpdate` (76/66) und `HeightDieUpdate` (31/24) verschwinden vollständig aus der Blockerliste; größte verbleibende Blocker: `SpecialAbilityUpdate` 64/36,
  `JetSlowDeathBehavior` 59/0, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/5, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25,
  `BaikonurLaunchPower` 23/23. Retail-Referenzmatch (`SimTool reference-match`): Sieger FactionAmerica bei Tick 10.575 (Replay 10.876 Ticks), Endhash (`final hash`, Run 1)
  `057C6678FE8F3381` (verändert sich gegenüber dem Phase-9-Stand, weil zuvor blockierte Objekte – u. a. Bäume und Höhenstopp-Objekte auf den Startkarten – jetzt instanziierbar
  sind und am Kampf teilnehmen); zwei frische Läufe und eine Fortsetzung ab Tick 5.438 liefern identische Hashes. Der synthetische 216.000-Tick-Stresstest trägt keine der drei
  neuen Module und bleibt unverändert bei `ED6F9329F59662E3`. Kompatibilitätsbasislinie 756 Einträge (vorher 749): 9 `module`-Einträge entfallen (jetzt implementiert), 16 neue
  `deferred-field`-Einträge für reine Präsentationsfelder der drei Module (FX-Listen, `ReorientToppledRubble`, Salven-Timing, `DestroyAttachedParticlesAtHeight`).
- **Stand nach den Phase-9-Missionsmodulen (1. Oktober 2026): 2.115 Objekte, 1.494 vollständig instanziierbar (70,6 %)**, 621 blockiert; 87 von 205 Modultypen implementiert (2 neu: `CommandButtonHuntUpdate`, `LockWeaponCreate` – ehrliche Hüllenmodule, siehe unten), sie decken 15.639 von 19.553 Modulverwendungen (80,0 %); Fingerabdruck `2B9D902AEC46A6DE…`. Größte verbleibende Blocker (Objekte insgesamt / nur dadurch blockiert): `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/36, `JetSlowDeathBehavior` 59/0, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/5, `HeightDieUpdate` 31/24, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `BaikonurLaunchPower` 23/23. **Hüllenmodule:** `CommandButtonHuntUpdate` (86 Verwendungen, keine Schlüssel; wird im Original nur von `TEAM_HUNT_WITH_COMMAND_BUTTON` ausgelöst) hält keinen Zustand, `LockWeaponCreate` sperrt den Primär-Slot (`SlotToLock = PRIMARY` in allen 12 Verwendungen; die Simulation führt nur eine Waffe, ein anderer Slot ist eine `UnsupportedModuleConfigurationException`); die auslösenden Skriptaktionen bleiben fehlend und werden gezählt (`docs/SCRIPT-ENGINE.md` §8). Der Retail-Referenzmatch verändert sich dadurch (Ranger u. a. entstehen jetzt als Startobjekte): Sieger FactionAmerica bei Tick 13.665 (Replay 13.966 Ticks), Endhash (`final hash`, Run 1) `D4779BE413835B6D`; zuvor Tick 13.800 (14.101 Ticks), `95022B80366E841E`. Der Stresstest (216.000 Ticks) bleibt bei `ED6F9329F59662E3`.
- **Stand nach Phase 8, Paket E1 (30. September 2026, frisch gemessen): 2.115 Objekte, 1.470 vollständig instanziierbar (69,5 %)**, 645 blockiert;
  85 von 205 Modultypen implementiert (8 neu: `DumbProjectileBehavior`, `MissileAIUpdate`, `PointDefenseLaserUpdate`, `TransportAIUpdate`, `ChinookAIUpdate`,
  `HelicopterSlowDeathBehavior`, `TunnelContain`, `HelixContain`, §21), sie decken 15.543 von 19.553 Modulverwendungen (79,5 %); Fingerabdruck
  `2A31BC2A83D63A58…`. Alle 1.470 Objekte werden in einer Welt erzeugt (9.670 Module). Nicht implementiert: 72 Module „Phase 5“, 26 „Phase 6“, 22 „Phase 7“.
  Größte verbleibende Blocker: `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0,
  `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/5, `HeightDieUpdate` 31/24, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25,
  `BaikonurLaunchPower` 23/23. Der Stand nach Phase 6 steht als Verlauf darunter.
- Verlauf (Stand nach Phase 6, 30. September 2026): 2.115 Objekte, 1.393 vollständig instanziierbar (65,9 %), 722 blockiert;
  77 von 205 Modultypen sind implementiert (12 aus Phase 4, 62 aus Phase 5, 3 aus Phase 6: `PowerPlantUpdate`, `PowerPlantUpgrade`, `OverchargeBehavior`;
  183 Simulations- und 22 Präsentationsmodule im Katalog), sie decken 15.382 von 19.553 Modulverwendungen (78,7 %); der Fingerabdruck des Retail-Basisbestands ist `1248585D0BAAB79F…`.
  Alle 1.393 Objekte werden in einer Welt erzeugt (8.925 Module). Die folgenden Zahlen beschreiben den Stand am Ende von Phase 5 und bleiben als Verlauf stehen.
- Verlauf (Ende Phase 5): 2.115 Objekte, 1.384 vollständig instanziierbar (65,4 %), 731 blockiert; 74 von 205 Modultypen implementiert (12 aus Phase 4, 62 aus Phase 5), sie decken 15.364 von 19.553
  Modulverwendungen (78,6 %). Gegenüber Phase 4 (26/2.113,
  1,2 %) ein Sprung um den Faktor ~46, getragen von Block 0.2, den elf `*Upgrade`-Modulen, `AIUpdateInterface`,
  `ProductionUpdate`, den sechs Blockermodulen aus §6/§6a, dem Containment-Paar `GarrisonContain`/`TransportContain` und den
  vierzehn Modulen aus `docs/GAME-RULES.md` §6c (`PhysicsBehavior`, `SquishCollide`, die drei Produktionsausgänge,
  `BaseRegenerateUpdate`, `GenerateMinefieldBehavior`, `HealContain`, Lochmodule usw. – zusammen +336 Objekte) sowie den
  zehn Spezialkraftmodulen aus `docs/GAME-RULES.md` §6d (+25 Objekte, darunter die 13 Kommandozentralen der Fraktionen).
  `CreateCrateDie` und die Erzeugungslistenausführung (`docs/GAME-RULES.md` §6b) schalten allein kein Objekt frei. Der Abschluss von
  Phase 5 ergänzte (1.224 → 1.384, +160 Objekte): Versorgungswirtschaft und Spawner (§6e), Reparaturdock und `AutoFindHealingUpdate`
  (§6f), `FireWeaponUpdate`, `ReplaceObjectUpgrade` und `CrushDie` (§6g), Erfahrungsmodule (§6h).
- Alle 1.384 instanziierbaren Objekte werden in einer Welt erzeugt und laufen im Realdatentest identisch zweimal durch
  (Test `--content-dir`); der Stresstest (`SimTool stress`, §10) nutzt eine synthetische Fixture und ist von den neuen
  Modulen bis auf den Zustandsumfang unberührt (Hashes neu gemessen, §10). Der Referenzmatch auf Retail-Daten
  (`docs/GAME-RULES.md` §7a) instanziiert reale Einheiten, Produktionsgebäude und Kommandozentralen aller 13 Fraktionen ohne
  übersprungenes Startobjekt.
- Verbleibende häufigste Blocker (Objekte / davon nur durch dieses Modul blockiert, frische Messung vom 30. September):
  `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0,
  `MissileAIUpdate` 44/29, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/2, `HeightDieUpdate` 31/15,
  `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `TransportAIUpdate` 24/13, `BaikonurLaunchPower` 23/23. Die noch nicht
  implementierten Module verteilen sich nach der im Katalog vermerkten Phase auf 77 (Phase 5), 29 (Phase 6) und 22 (Phase 7). `ToppleUpdate` braucht echte Kollisionserkennung (durch Lesen der
  Referenzquelle verifiziert, siehe `docs/HANDOFF-PHASE5.md`); Phase 6 hat bewusst keine Kollision eingeführt (§14), das Modul bleibt offen. Die vollständige, tagesaktuelle Liste liefert
  das Werkzeug.
- **Regel für Aktualisierungsmodule:** jedes Modul, das `IUpdateModule` implementiert, speichert und lädt seinen Weckzeitpunkt
  (`nextWake`). `SimulationWorld.Register` weckt wiederhergestellte Module mit negativem Weckzeitpunkt sonst ab dem
  Wiederherstellungstick neu (`_tick + SleepNone`), was die Fortsetzung nach einem Snapshot vom Originallauf abweichen lässt.
  Der `SourceRules`-Test erzwingt die Zeichenkette `"nextWake"` in jeder solchen Klasse.

## 12. Bekannte Grenzen und technische Schulden

- **Phase 6 (abgeschlossen)**: Gelände, Navigation, Sicht, Bau und Skirmish-KI sind umgesetzt, aber vereinfacht; die Grenzen stehen gesammelt in §20 und in
  `docs/IMPLEMENTATION-PLAN.md` §6.
- **Phase 5 (abgeschlossen)**: Wirtschaft mit Versorgung, Upgrades, Sciences, direkter Kampf mit Flächenschaden und
  `WeaponBonus`, Erfahrung und Veteranenstufen, Teams und Diplomatie, Garnison/Transport, Reparaturdock, einfache Bewegung,
  Produktion und Sieg/Niederlage sind implementiert (`docs/GAME-RULES.md`), aber bewusst vereinfacht; Terrain, Pfadsuche, Bau und Verkauf sind seit Phase 6 vorhanden (§13–§16), offen bleiben
  Kollision (Projektil-Flugzeit ist seit Phase 8 umgesetzt, §21), die Wirkung von `CommandSet` und Radar und die Rüstungs-/Waffensatzauswahl nach Veteranenstufe. Jede Vereinfachung steht mit Quellverweis im Modul- oder
  Katalog-Kommentar; die Liste der Grenzen steht in `docs/GAME-RULES.md` §10.
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
- `Restore` ist intern; Vorwärts-/Rückwärtskompatibilität von Snapshots (aktuell Version 7, ältere werden abgewiesen) ist erst Thema von Phase 9.
- Der Zustands-Hash ist nicht kryptographisch und die Snapshot-JSON-Größe (≈ 485 Byte je Entity) ist nicht optimiert.
- Vom Kern erzeugte Anforderungen (`FxRequested`, `WeaponFireRequested`, `VoiceFearRequested`, `CrateRequested`) werden
  nicht ausgeführt; sie sind Verträge für Präsentation. `CreationListRequested` ist Protokoll: die Liste wird in der Welt
  ausgeführt (`CreateFromList`); nicht simulierte Nuggets (`CreateDebris`, `ApplyRandomForce`, `FireWeapon`, `Attack`,
  `DeliverPayload`) melden sich mit `CreationSkipped`. Weitere Abweichungen von `ObjectCreationList.cpp` (Zugreihenfolge
  wegen verzögertem Spawn, flaches Terrain, `FixedTrig` statt binary32, einmaliger `LifetimeUpdate`-Zug) stehen in
  `docs/GAME-RULES.md` §6b.
- Die Definitionsdomänen außer Object, Weapon, Armor, Upgrade, Science und Locomotor sind weiterhin flach typisiert
  (Zahlen in `TYPED-DEFINITIONS.md`): CommandButton, Faction, GameData; `SpecialPower` ist seit Phase 5 streng typisiert. `ObjectCreationList`, `CrateData` und
  `GameData/Gravity` sind typisiert.
- Der Referenzmatch (`ReferenceMatchScenario`, `SimTool reference-match`) läuft auf synthetischen und auf Retail-Daten
  deterministisch und replaybar (13 von 13 Fraktionen mit ihren echten Kommandozentralen, ohne
  Dozer/Arbeiter, Supply und Spezialkräfte im Befehlsstrom; Messung in `docs/GAME-RULES.md` §7a). Golden-Master-
  Szenarien existieren für die Schadenskette und die Produktionsuhr; Supply und Erfahrung stehen aus. Übrige Mechaniken sind
  durch gezielte Integrationstests (`Tests.Phase5.cs`) und den 216.000-Tick-Stresstest mit realen Objekten abgesichert.
- **Live/Replay-Lücke bei `SharedReadyFrame` (gefunden in Phase 8, Paket G2; behoben in Phase 9):** `SpecialPowerBase.SharedReadyFrame()`
  (`src/CnCNew.Simulation/Modules/BuiltIn/SpecialPowerModules.cs`) legte den geteilten Bereitschafts-Tick eines `SharedSynced`-Spezialkraft
  verzögert an, beim *ersten* `IsReady()`-Zugriff, und schrieb ihn dabei in `SimulationWorld`. Für Objekte, die die Kraft von Anfang an besitzen
  (kein `PurchaseScienceCommand`, also kein `OnSpecialPowerCreation`), war dieser lesende Zugriff der einzige Anlegezeitpunkt. Die Skirmish-KI
  ruft `IsReady()` in ihrer eigenen Entscheidungslogik in jedem echten Tick ab; ein reiner `ReplayRunner`-Lauf führt keinen KI-Code aus und
  erreichte diesen Zugriff nie zum gleichen Tick (wenn überhaupt). Dadurch konnte `SkirmishMatchSession.Step()` (live) einen anderen
  `World.StateHash` liefern als `ReplayRunner.Run` über denselben `Session.ToReplay()`-Mitschnitt, reproduziert an `LiveMatch` auf
  „Alpine Assault“: `players[2].sharedReadyFrames` hatte nach einem KI-`ConstructCommand` bei Tick 13/14 acht Einträge live gegenüber null im
  Replay (Diagnose mit `WorldSnapshot.Write` beider Welten verglichen; kein Effekt der Präsentationsschicht, reproduzierte auch mit reinem
  `Session.Step()`). Betroffen waren ausschließlich `SharedSynced`-Spezialkräfte ohne expliziten Gewährungsbefehl.
  **Fix (Phase 9):** `SpecialPowerModuleBase.OnSpawn` legt den Bereitschafts-Tick jetzt für nicht im Bau befindliche Objekte deterministisch an
  (`SharedReadyFrame(world)` statt der bisherigen Auslassung für `SharedSynced`-Kräfte); `IsReady()` liest ihn nur noch, ohne ihn selbst
  anzulegen. Objekte, die noch im Bau sind, erhalten ihn weiterhin über `OnSpecialPowerCreation` (Baufertigstellung via `SpecialPowerCreate`,
  oder Wissenschaftsgewährung via `AddScience`) — dieselben Anlegezeitpunkte wie bei nicht geteilten Kräften. **Nicht betroffen** waren und sind
  alle in dieser Datei gemessenen Zahlen (Referenzmatch-, Stresstest- und KI-Lauf-Hashes in §19 und in `CLAUDE.md`): die entstehen aus Live-gegen-
  Live-Vergleichen (zwei unabhängige Läufe derselben Session), die von dieser Lücke nie berührt wurden, weil beide Läufe dieselbe KI-
  Abfragefolge produzieren. Neu gemessen nach dem Fix: Der Retail-Referenzmatch (`SimTool reference-match`) liefert unverändert Endhash
  `4DCB46FE3D2E8214` (Sieger FactionAmerica, Tick 13.800) — dieses Szenario durchläuft `ReferenceMatchScenario`, nicht `SkirmishAi`, und
  berührt die gefixte Stelle nicht. Ein Skirmish-KI-Lauf, der die Stelle sehr wohl berührt (`SimTool ai-match .local/phase2-import-a --map
  "Maps/Alpine Assault/Alpine Assault.map" --factions FactionAmericaAirForceGeneral,FactionChina --max-ticks 45000`; America benutzt die
  geteilte Spezialkraft 8-mal), ändert sein Ergebnis wie erwartet (KI-Timing verschiebt sich): Sieger FactionChina bei Tick 16.155 (vorher nicht
  mit diesem exakten Aufruf vermessen, daher kein Vorher/Nachher-Hash), Endhash `0FDF97B63EFECC6A`, weiterhin über zwei frische Läufe und
  Snapshotfortsetzung identisch. Die 16-Karten-KI-Übersicht in §19 (Phase-6-/Paket-E1-Vergleich) wurde für diesen Fix **nicht** neu vermessen;
  das ist eine eigene, umfangreiche Balance-Erhebung und keine reine Determinismusprüfung – wer ihre Zahlen braucht, muss sie neu laufen lassen.
  Der 216.000-Tick-Stresstest (`SimTool stress`, synthetische Fixture) bleibt exakt gleich (`ED6F9329F59662E3`, geprüft durch Vergleich vor/nach
  dem Fix bei sonst unverändertem Code) — die Fixture trägt keine `SharedSynced`-Spezialkraft.

## 12a. Live/Replay-Determinismus: Testlücke (geschlossen)

Die bestehenden Replay-Tests (`Tests.ReferenceMatch.cs`, `Tests.Skirmish.cs`, `Tests.State.cs`) verglichen bisher ausschließlich zwei
`ReplayRunner`-Läufe untereinander oder den Sieger, nie den `World.StateHash` eines live gelaufenen `SkirmishMatchSession` gegen einen
`ReplayRunner`-Lauf über dessen eigenen `ToReplay()`-Mitschnitt. Deshalb hatte kein bestehender Test die oben beschriebene Lücke gefunden; sie
wurde erst in Paket G2 (`CnCNew.Game.Tests`) beim Testen eines echten Live-Matches sichtbar. Phase 9 ergänzt genau diesen Vergleich:
`Tests.Skirmish.cs` → `SkirmishLiveSessionMatchesItsOwnReplay` startet eine `SkirmishMatchSession` direkt (ohne `SkirmishMatchScenario`s
Umweg über eine Kopie), lässt sie bis `IsOver` laufen, vergleicht `session.World.StateHash` mit `ReplayRunner.Run(session.ToReplay())` und
prüft zusätzlich, dass die KI die geteilte Spezialkraft (`AlphaShared`/`BetaShared`, `SharedSyncedTimer = Yes` auf der Kommandozentrale der
Testfixtur) tatsächlich benutzt hat, damit der Test die Lücke wirklich durchläuft, statt sie nur zufällig nicht zu treffen. Der Test schlägt
auf dem unbehobenen Code fehl (verifiziert) und besteht mit dem Fix oben.

## 13. Gelände und Karten (Phase 6)

`CnCNew.Formats` dekodiert die Kartendatei (`SageMapDecoder`; Container CkMp, EAR (RefPack) und ZL5, Blend-Tile-Versionen 6–8) in ein
unveränderliches `MapData` (`CnCNew.Data.World`: Höhenraster, Blend-Tiles, Objekte, Wegpunkte und -verbindungen, Polygontrigger,
Bauliste). Die Simulation liest kein Kartenformat: `TerrainMap` (`CnCNew.Simulation/Terrain`) wird aus `MapData` gebaut und ist
das einzige Geländeobjekt, das die Welt kennt.

- Raster mit `CellSize` 10 Weltkoordinaten, `CellFlags` `Cliff` (aus dem Blend-Tile-Kliffmerkmal der Karte) und `Water` (Polygontrigger mit Wasserfläche). Die Höhe wird aus dem Höhenraster
  in Festkomma abgetastet.
- Wegpunkte (`Player_N_Start` liefert `StartPositions`), Wasserflächen und ein **Fingerabdruck** (kanonisch, nie Pfade oder Zeit).
  Der Fingerabdruck steht im Snapshot (v4) und im Replay (v5); ein Replay auf einem anderen Gelände wird abgewiesen.
- Nicht übernommen (`MapTool audit` zählt sie): `GlobalLighting` (150 Karten, 130.800 Byte) und `SidesList`/`PlayerScriptsList` (150 Karten, 9.542.461 Byte), also
  Beleuchtung, Spielerlisten und Skripte der Karte; Skripte gehören nicht zu Phase 6.
- **`MapTool audit`** (Realdaten, 30. September): 150 Karten dekodiert, 0 fehlgeschlagen; Container CkMp 4 / EAR 145 / ZL5 1; Blend-Tile-Versionen v6 3 / v7 31 / v8 116;
  30.391.581 Höhenpunkte, 30.259.192 Zellen, davon 8.253.119 Kliffzellen; 281.480 Objekte (0 wegen Höhenbereich verworfen), 25.463 Wegpunkte, 16.489 Wegpunktverbindungen,
  höchstens 8 Startwegpunkte je Karte (75 Karten ohne Startwegpunkt), 2.488 Polygontrigger (davon 526 Wasser), 1.631 Seiten, 7.704 Teams, 1.356 Bauliste-Einträge.
  Gelände: 150 Karten gebaut, 16.475.736 Pfadzellen (3.278.645 Kliff, 1.856.627 Wasser), 258 Startpositionen, größte Ausdehnung 589 Zellen, 0 abgewiesen;
  Digest der Fingerabdrücke `723F6640405C5445189D523BA257D182149CBFCEC01A6C8F0B053031FB130459`.
- **Bodenpfade zwischen Startpositionen:** 462 Paare, 456 vollständig, 6 nur teilweise, 0 blockiert, 1.679 Wegpunkte, Digest
  `C775CD5CD41E90BC7C55E054E6C0F04EC60A0E02D29DFC5705359B1128BF8481`. Die sechs Teilpfade sind eine bekannte Grenze (§20).

## 14. Navigation und Bewegung (Phase 6)

`NavigationGrid` ist ein 8-fach verbundener A* auf dem Zellraster: Kosten 10 (gerade) und 14 (diagonal), **kein Eckenschneiden**, Gleichstände nach Zellindex,
Erweiterungsbudget 200.000 je Anfrage (danach ist das Ergebnis `Partial` oder `Blocked`, nie ein stiller Ersatzweg). Zellklassen in dieser Vorrangfolge: Hindernis,
Kliff, Wasser, frei; erlaubt ist, was die `Surfaces` des Locomotors zulassen (jetzt als Flags-Feld typisiert statt als Kompatibilitätswert).

- **Hindernisse** sind Gebäude und andere `OBSTACLE`-Entities. Der Fußabdruck sind die Zellen, deren Mittelpunkt in der um eine Viertelzelle vergrößerten Form liegt, plus die Zelle
  des Objekts selbst (Näherung, keine Kopie der Pathfinder-Klassifikation des Originals). `ObstacleSum` ist eine reihenfolgeunabhängige
  Summe der Hindernisse; ändert sie sich, planen laufende Bewegungen neu.
- **Fluchtzellen:** Steht eine Einheit (Radius 12 Zellen) oder ihr Ziel (Radius 8) in einem Hindernis, dürfen Zellen des Hindernisses mit Zuschlag 4 betreten werden. Das verhindert
  eingeschlossene Einheiten und ist eine bewusst benannte Vereinfachung, kein Original-Verhalten.
- `IsFootprintLegal` ist der Gelände-/Fußabdruck-Teil von `BuildAssistant::isLocationLegalToBuild` (§16).
- `AIUpdateInterface` folgt geplanten Pfaden (`PathStatus` `Found`/`Partial`/`Blocked`), plant bei geänderter `ObstacleSum` neu, begrenzt die Drehung (`TurnRatePerTick`,
  `FixedTrig` bekam `Atan2`, `AngleDifference`, `Normalize`) und setzt Bodeneinheiten auf die Geländehöhe. Ein teilweiser oder fehlender Pfad beendet den Befehl; es gibt keine
  stille Ersatzbewegung.
- Grenzen: Punktförmige Einheiten (kein echtes Blockieren von Zellen durch andere Einheiten), ein flacher A* mit Budget (keine Hierarchie, keine Brücken), Fußabdruck genähert.
  Seit Phase 10 (§23) gibt es eine begrenzte lokale Ausweichbewegung gegen andere Einheiten (`AvoidLocalBlockers`, nicht Teil der Pfadplanung) sowie echte
  Crush-/Squish-/Topple-Kollision, sobald sich zwei Fußabdrücke überlappen (`ResolveFootprintCollisions`); eine vollständige Steuerung/Vermeidung (Flocking) bleibt offen.

## 15. Sicht und Kriegsnebel (Phase 6)

`VisionMap` hält je Spieler und Zelle ein Byte (Bit 0 sichtbar, Bit 1 erkundet) und wird alle `VisionIntervalTicks` = 3 Ticks neu berechnet; der Sicht-Hash gehört zum Zustands-Hash
(Snapshot v5). Aufdeckung ist kreisförmig, ohne Höhen-Sichtlinie.

- Objekte mit Sichtweite decken für ihren Besitzer und dessen Verbündete auf; `REVEAL_TO_ALL` deckt für alle auf; Baustellen decken nur ihren `BoundingCircleRadius` auf.
- `CanPlayerSee` schließt enthaltene Entities und nicht entdeckte `STEALTHED`-Entities aus; `EntitiesVisibleTo` liefert in aufsteigender Entity-ID-Reihenfolge. Diese Abfragen sind die einzige
  Informationsquelle der Skirmish-KI (§18).
- `FireWeaponCommand` auf ein für den Spieler unsichtbares Ziel wird mit `TargetNotVisible` abgelehnt.
- Fehlerbehebung: `STEALTHED` wird nur gesetzt, wenn `CAN_STEALTH` besteht (angeboren, `StealthUpgrade`, `GrantStealth`); vorher wurde es unabhängig davon gesetzt und ließ alle Objekte getarnt erscheinen.
- Grenzen: keine Höhenverdeckung, Aktualisierung nur alle 3 Ticks, kein Detektor-Modul (getarnte Einheiten bleiben verborgen, bis das Modul existiert).

## 16. Bau, Verkauf, Reparatur und Sammelpunkte (Phase 6)

Ein Bauwerk ist von Anfang an ein echtes Entity mit `UNDER_CONSTRUCTION`; seine Gesundheit steigt linear mit der Arbeit des Baumeisters über `BuildTime`
(`SimulationWorld.Construction.cs`). `CheckConstruct` prüft in dieser Reihenfolge: Baumeister existiert → Erlaubnis → `BuilderOf` (Baumeister passt zur Vorlage über `CommandCatalog`) →
Vorlage → `CanBuildTemplate` (`STRUCTURE`, `Buildable` Yes/Only_By_AI, Seite) → Voraussetzungen (`PrerequisitesSatisfied`: Science- und Objektzeilen, nur fertige Objekte) →
Guthaben → Bauplatz (`IsFootprintLegal` plus erkundet). Die Kosten werden beim Start abgebucht; Abbruch erstattet sie.

- **Verkauf:** `SaleDurationTicks` = 179, Erlös = `BuildCost` × `SellPercentage` (jetzt typisiert). Der Verkauf beendet laufende Produktion des Gebäudes.
- **Reparatur:** kostenlos (`AIUpdateInterface.IsRepairable`), eine Vereinfachung.
- **Sammelpunkt:** `SetRallyPointCommand` setzt den Punkt, zu dem neu produzierte Einheiten laufen.
- Snapshot v6 trägt Bau-, Verkaufs- und Sammelpunktzustand; das Replay v6 die sechs Befehle.
- Grenzen: `QueueProduction` prüft weder Voraussetzung noch Fähigkeit des Gebäudes, `MaxSimultaneousOfType` wird nicht geprüft, Reparatur ist kostenlos.

## 17. Kraftwerksmodule und CommandCatalog (Phase 6)

`PowerPlantUpgrade` (`EnergyBonus` bei Upgrade), `PowerPlantUpdate` (Stäbe ausfahren, passiv) und `OverchargeBehavior` (Energiebonus mit Gesundheitsverlust, schaltet sich bei niedriger Gesundheit selbst ab) verändern die Energieproduktion des
Spielers über `IWorldServices.AddEnergyProduction`; `ObjectTemplate.EnergyBonus` und `CommandSets` sind neu. `TemplateCatalog.Commands` (`CommandCatalog`) löst `CommandSet` → `CommandButton` auf und
beantwortet `OffersBuild` (welches Objekt was bauen oder produzieren kann); die KI nutzt es für Rollen (§18). Grenzen: kein Eroberungsverhalten, kein Umschaltbefehl für Overcharge, Reset entfernt auch den Bonus.

## 18. Skirmish-KI (Phase 6)

`SkirmishAi` (`Simulation/Skirmish/SkirmishAi.cs`) ist ein reiner Befehlsgeber je Computerspieler: alle 15 Ticks (`ThinkInterval`) entscheidet sie in fester Reihenfolge – Arbeiter,
Gebäude, Baustellen, Reparatur, Science, Spezialkräfte, Armee, Sammelpunkte, Armeebefehle, Versorger – und schreibt ausschließlich typisierte Befehle (`FireInterval` 5). Sie ist eine eigenständige Neuentwicklung,
kein Port des Skriptsystems des Originals.

- **Rollen** ergeben sich aus Modulen und `KindOf` (Kommandozentrale, Versorgungsgebäude, Produktionsgebäude je Klasse, Kraftwerk, Verteidigung, Baumeister, Versorger, Kampfeinheit), nie aus Namen.
- **Informationsregel:** Die KI plant nur mit `EntitiesVisibleTo`, einem Gedächtnis gesehener Gegner (`MemoryTicks` 900, Wiederbesuch alle `RevisitTicks` 3600) und den öffentlichen Startpositionen
  (`HintPositions`). Der Test `SkirmishAiOnlyUsesWhatItSees` prüft in einem 60-Tick-Fenster, dass sie nichts verwendet, was sie nicht sieht oder erinnert.
- Konstanten: `RetryTicks` 300, `OrderRefreshTicks` 150, Angriffsschwelle 8–30 Einheiten (`MinAttackThreshold`/`MaxAttackThreshold`), höchstens 400 Platzprüfungen je Denkschritt (`PlacementChecksPerThink`), `DefenseRadius` 450,
  `EngageRadius` 260, Reparatur unter 70 % Gesundheit.
- **`SkirmishMatchScenario`** (`SimTool ai-match`): kompiliert den Bestand ohne Kartenoverlays, baut das Gelände, setzt Lagerhäuser der Karte, gibt jeder Fraktion ihre Startobjekte, 10.000 Startgeld und alle 2.700 Ticks einen Science-Punkt (Ersatz für das nicht vorhandene Rangsystem, vom System gutgeschrieben) und lässt alle Computerspieler bis zum Sieg oder `--max-ticks` laufen. Exitcode 0 nur, wenn Lauf 2 und die Fortsetzung aus einem Snapshot
  tickgleich sind (`SkirmishMatchOptions.Standard`: Weltseed `0xC0FFEE06`, KI-Seed `0x5EED0006`, 108.000 Ticks, Checkpoint 300).

## 19. Ergebnisse der KI-gegen-KI-Läufe (30. September 2026)

Vier Karten (Alpine Assault, Armored Fury, Bitter Winter, Death Valley) × vier Paarungen, höchstens 45.000 Ticks; **jeder der 16 Läufe ist mit Lauf 2 und Snapshotfortsetzung identisch**.
15 Läufe enden mit einem Sieger, einer (Alpine Assault, America gegen China) bleibt bis zum Limit unentschieden.

| Karte | Paarung | Ergebnis |
|---|---|---|
| Alpine Assault | China, GLA | China, Tick 38.655 |
| Alpine Assault | America, China | Patt bei 45.000 |
| Alpine Assault | GLA, America | GLA, Tick 10.980 |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 22.335 |
| Armored Fury | China, GLA | GLA, Tick 7.335 |
| Armored Fury | America, China | China, Tick 15.840 |
| Armored Fury | GLA, America | GLA, Tick 6.330 |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 16.695 |
| Bitter Winter | China, GLA | China, Tick 28.515 |
| Bitter Winter | America, China | China, Tick 18.480 |
| Bitter Winter | GLA, America | GLA, Tick 9.465 |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 23.565 |
| Death Valley | China, GLA | China, Tick 21.600 |
| Death Valley | America, China | China, Tick 20.565 |
| Death Valley | GLA, America | GLA, Tick 8.355 |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 19.095 |

America gewann in Phase 6 nie: Die vier America-Fraktionen hatten keine Versorgungswirtschaft, weil `ChinookAIUpdate`/`TransportAIUpdate` fehlten (seit Paket E1 vorhanden, Wiederholung unten). Das ist keine Balance-Aussage,
sondern ein Nebenbefund der Modullücke. Der Retail-Referenzmatch (`SimTool reference-match`, 13 Fraktionen, kein Startobjekt übersprungen) endet nach Phase 6 mit
Sieger Spieler 12 (`FactionGLAStealthGeneral`) bei Tick 26.880, Replay 27.181 Ticks, Endhash `5C370ED383EC9F6B`, Replay-Datei 949.193 Byte (SHA-256 `DCEE89309520371C0288EDB44309DA212AEE167D129343B6BB55BEA9F08A78C2`), 27 Checkpoints;
Lauf 2 (Kettenhash `EB8BDC1482BC20DF`) und die Fortsetzung bei Tick 13.590 sind identisch. 27.607 Befehle (1.240 Produktion, 7.194 Bewegung, 13 Andocken, 18.591 Schüsse) in 7,8 s.

### Wiederholung nach Paket E1 (Phase 8: Chinook, Transporter, Tunnel, Projektilflugzeit)

Gleiche Aufrufe (`SimTool ai-match … --factions <A>,<B> --max-ticks 45000`, Standardseeds), alle 16 Läufe, ohne Auswahl. **Jeder Lauf ist mit Lauf 2 und
Snapshotfortsetzung identisch**, und eine zweite, unabhängige Auswertung mit einem frisch gebauten Werkzeug lieferte dieselben 16 Ergebnisse.

| Karte | Paarung | Phase 6 | Paket E1 |
|---|---|---|---|
| Alpine Assault | China, GLA | China, Tick 38.655 | **Patt bei 45.000** |
| Alpine Assault | America, China | Patt bei 45.000 | **America, Tick 10.575** |
| Alpine Assault | GLA, America | GLA, Tick 10.980 | GLA, Tick 18.870 |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 22.335 | China Tank General, Tick 22.155 |
| Armored Fury | China, GLA | GLA, Tick 7.335 | GLA, Tick 9.240 |
| Armored Fury | America, China | China, Tick 15.840 | **America, Tick 9.390** |
| Armored Fury | GLA, America | GLA, Tick 6.330 | GLA, Tick 30.735 |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 16.695 | China Tank General, Tick 17.430 |
| Bitter Winter | China, GLA | China, Tick 28.515 | China, Tick 32.490 |
| Bitter Winter | America, China | China, Tick 18.480 | **America, Tick 19.980** |
| Bitter Winter | GLA, America | GLA, Tick 9.465 | GLA, Tick 18.240 |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 23.565 | China Tank General, Tick 34.515 |
| Death Valley | China, GLA | China, Tick 21.600 | China, Tick 39.555 |
| Death Valley | America, China | China, Tick 20.565 | **America, Tick 10.155** |
| Death Valley | GLA, America | GLA, Tick 8.355 | GLA, Tick 16.320 |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 19.095 | China Tank General, Tick 19.035 |

Ehrliche Einordnung: **America gewinnt jetzt 4 von 4 Läufen gegen China** (vorher 0 von 4, darunter das frühere Patt), **gegen GLA weiterhin 0 von 4**, und
die Air-Force-General-Paarung verliert weiter 4 von 4. Das frühere Patt (Alpine Assault, America gegen China) ist verschwunden, dafür gibt es **ein neues Patt**
(Alpine Assault, China gegen GLA: China steht am Ende mit 29 Objekten, GLA mit 7, darunter Kommandozentrale, Tunnel und Arbeiter; China greift nur einmal an).
15 von 16 Läufen enden also weiterhin mit einem Sieger. Die America-Siege gegen China sind nicht allein der Chinook-Wirtschaft zuzuschreiben: in drei der
vier Siege gibt America keinen einzigen Dockbefehl (die Chinooks versorgen selbstständig über die Versorgungsmaschine), aber die Humvees (jetzt instanziierbar,
`TransportAIUpdate`) und die Flugzeit der chinesischen Flammenprojektile (Drachenpanzer treffen nicht mehr sofort) verschieben das Kräfteverhältnis ebenfalls;
eine Zerlegung nach Ursachen wurde nicht gemessen. GLA-Siege dauern im Mittel länger (Tunnelheilung, Flugzeit der Raketen). Keine Balance-Aussage.
## 20. Grenzen der Phase 6

- **Navigation:** punktförmige Einheiten (seit Phase 10, §23, mit begrenzter lokaler Ausweichbewegung und echter Crush-/Squish-/Topple-Kollision bei Überlappung, aber ohne echte
  Steuerung/Flocking), flacher A* mit Budget, genäherter Fußabdruck mit Fluchtzellen (Zuschlag), Locomotoren nur über `Surfaces`; ein teilweiser oder fehlender Pfad beendet den
  Befehl. Von 462 Startpositionspaaren sind 6 nur teilweise begehbar.
- **Sicht:** kreisförmig, keine Höhen-Sichtlinie, Aktualisierung alle 3 Ticks, keine Detektoren.
- **Bau:** `QueueProduction` ohne Voraussetzungs-/Fähigkeitsprüfung, `MaxSimultaneousOfType` ungeprüft, Reparatur kostenlos.
- **Kraftwerke:** keine Eroberung, kein Umschaltbefehl für `Overcharge`, Reset entfernt auch den Bonus.
- **KI:** Spezialkräfte nicht klassifiziert, Startpositionen der Gegner sind öffentliches Wissen (`HintPositions`), von der Karte nur Lagerhäuser gesetzt, Startgeld und Science-Punkte vom System, keine Flugzeuge/Flugplätze,
  (seit Paket E1 mit Chinook-Wirtschaft, Tunneln und Transportern, §21), Armeezusammensetzung durch Instanziierbarkeit begrenzt, gleichstarke Paarungen können in ein Patt laufen (1 von 16), ohne Detektoren können getarnte Überlebende verborgen
  bleiben.
- **Offen aus früheren Phasen:** Block 0.4 (RNG-Abgleich mit einem kompilierten Original; Projektilflugzeit und `ChinookAIUpdate`/`TransportAIUpdate` seit Paket E1, §21;
  `ToppleUpdate`/`StructureToppleUpdate`/`HeightDieUpdate` und echte `CrushDie`/`SquishCollide`-Kollision seit Phase 10, §23), Wirkung von `CommandSet`/Radar,
  Veteranen-`ArmorSet`/`WeaponSet`, `CommandButtonHuntUpdate` und `SpecialAbilityUpdate`.

## 21. Phase 8, Paket E1: Projektilflugzeit, Transporter, Tunnel, Chinook

Regeln mit Quellverweisen: `docs/GAME-RULES.md` §6i (Flugzeit) und §6j (Transporter, Tunnel, Chinook). Kurzfassung der Kernänderungen:

- **Acht neue Module** mit Katalogschema (`ModuleCatalog`, Felder aus den FieldParse-Tabellen, gegen den Bestand geprüft), Bindung und Tests:
  `DumbProjectileBehavior`, `MissileAIUpdate`, `PointDefenseLaserUpdate` (`ProjectileModules.cs`); `TransportAIUpdate`, `ChinookAIUpdate`,
  `HelicopterSlowDeathBehavior`, `TunnelContain`, `HelixContain` (`TransportModules.cs`). `PointDefenseLaserUpdate` und `HelicopterSlowDeathBehavior` kamen hinzu,
  weil sonst kein Chinook der Air-Force-Generals bzw. gar kein Chinook instanziierbar geworden wäre.
- **Flugzeit im Kern** (`SimulationWorld.Projectiles.cs`): `FireWeaponAt` ersetzt den Sofortschaden; verzögerte Treffer sind eine geordnete Liste im globalen
  Zustand (Hash, Snapshot) und werden nach der Zerstörungsphase ausgewertet (Tickphase 5a); Projektilobjekte entstehen über die Spawn-Queue, ihre Startdaten
  (`ProjectileLaunch`) liegen im Queue-Eintrag. Neue Ereignisse `ProjectileLaunched` (27) und `WeaponImpact` (28); Ereignisse mit Ort tragen `HasPosition` und
  X/Y/Z. Neuer Überspringgrund `CreationSkipReasons.ProjectileNotInstantiable` (10).
- **Festkomma:** Bézierpunkte aus der Bernsteinform (keine aufsummierten Vorwärtsdifferenzen), Kurvenlänge nach `BezierSegment::getApproximateLength` mit Rekursionsgrenze 24,
  Flugticks `ceil` auf Rohwerten (`(d + v − 1) / v`), Richtungen über `Fixed.Sqrt`, Schwerkraft `GameData.ini Gravity / 900` je Tick²
  (`TemplateCatalog.GravityPerTickSquared`), Winkel über `FixedTrig`. Kein `float`/`double` (`SourceRules`).
- **Tunnelnetz:** `TunnelTracker` je Spieler im Weltzustand (Hash und Snapshot); `TunnelContain` ist `IContainerModule` auf dem gemeinsamen Netz,
  `IWorldAttached` verbindet es beim Registrieren (auch nach Restore), `ISellModule` bildet `onSelling` ab.
- **Befehle 26/27** (`MoveAndEnterCommand`, `EvacuateCommand`), `CommandTypes.Version = 9`; **Snapshot v7, Replay v7** (ältere werden mit klarer Meldung abgewiesen).
- **Skirmish-KI:** Chinooks als Versorger, Tunnel (Heilung und Abkürzung), Transporter (Infanterie laden/entladen); siehe §19 für die Auswertung.

**Messungen (30. September 2026, alle zweimal ausgeführt und verglichen):**

| Größe | Wert |
|---|---|
| Vollinventar (`IniTool compile`) | 228 INI-Dateien (28 ausgeschlossen), 65.875 Blöcke, 12.574 Definitionen, 16.380 Module, 333.061 Schlüssel, 18.321/19.871 Referenzen (1.550 Fallbacks), Snapshot 144.895.033 Byte, SHA-256 `09B84AC13823DC02C250F6BC95507BC58E13CF270E028E0DC1B5E55AA5391AA5` |
| Retail-Basis (`--maps exclude`) | 146 INI-Dateien, 12.550 Definitionen, Snapshot 144.284.158 Byte, SHA-256 `2A31BC2A83D63A58752FD5F98B7C47215978AD8DD3C1B2870A83DA31DDDEAFE7` |
| Kompatibilitätsbasislinie | 749 Einträge (vorher 741): 16 neue `deferred-field` (Präsentationsfelder der neuen Module: Seile/Rotorwind des Chinook, Partikel/Knochen/Ton der Helikopter-Absturzbehandlung, Ausstiegs-/Anzeigefelder des `HelixContain`), 8 `module`-Einträge entfallen (jetzt implementiert), `compatibility-module` 4.133 → 3.974 Verwendungen |
| Module | 205 Typen, 85 implementiert, 1.470 von 2.115 Objekten instanziierbar (69,5 %), 15.543 von 19.553 Verwendungen (79,5 %) |
| Stresstest | Endhash `ED6F9329F59662E3`, Kettenhash `3AECEB0579E460AF` (§10) |
| Retail-Referenzmatch | 13 Fraktionen, America jetzt mit Chinook-Versorgung (keine `supply note`); Sieger Spieler 1 (FactionAmerica) bei Tick 13.800, Replay 14.101 Ticks, 1.197.235 Byte (SHA-256 `80BB3FF93421C4D7EB1F7FEA2ADCF3BBA5BB0A2B006F828EB5C3A78199AF3B98`, zwei Aufnahmen byteidentisch), 36.880 Befehle, 428 Lieferungen mit 114.450 Guthaben, Endhash `4DCB46FE3D2E8214`, Kettenhash `795E3C9868716E1B`, Fortsetzung ab Tick 7.050 identisch |

**Grenzen:** Projektile kollidieren nicht (Punkt 9 des Handoffs); Täuschkörper, Störsender, Marschflughöhe, veteranenabhängige Detonationseffekte, Abseilen,
Feuern aus Containern, Flugfeldheilung des Chinook, Nemesis-Zielwahl der Tunnelverteidiger und Bunkerbrecher-Effekte fehlen; Chinook-Höhenwechsel und
Helikopterabsturz sind kinematisch statt über `PhysicsBehavior`. 40 von 157 Projektilwaffen feuern ein nicht instanziierbares Objekt und fallen sichtbar
(`CreationSkipped`) auf den projektillosen Flugzeitpfad zurück.
## 22. Phase 9: Skript-VM, Missionssitzung und Zustandsformate

Die Simulation trägt seit Phase 9 zusätzlich die Ausführung der World-Builder-Skripte (`Scripting/`), die Missionssitzung und ihren Zustand (`Mission/`); Entwurf,
Quellverweise, Abdeckungstabellen, Abweichungen und Messwerte stehen in `docs/SCRIPT-ENGINE.md`, Kampagne und Savegame in `docs/CAMPAIGN-AND-SAVEGAMES.md`.

- **Neue Weltzugriffe** (`World/SimulationWorld.Scripting.cs`): `HasPlayer`, `SetMoney`, `SetRelationship`, `TransferOwnership` (Besitzerwechsel inkl. Energieproduktion),
  `RequestSpawn(…, healthPercent)`, `MaterializePendingSpawns()` (legt angeforderte Spawns sofort an, wie `newObject` im Original). Kein Befehlscode, keine Änderung an Snapshot (Version 7)
  oder Replay (Version 7); `CommandTypes.Version` bleibt 9.
- **Zustand:** `MissionSnapshot` Version 1 (`cncnew-mission-state`) bettet den unveränderten Welt-Snapshot als Roh-JSON ein und ergänzt Skriptzustand (`ScriptEngine.Describe`, in
  einem `IStateWriter`-Durchlauf für Hash und Datei), Befehlssequenzen je Spieler und die Startoptionen; `MissionSession.StateHash` = Welt- und Skripthash.
- **Determinismus:** alle Skriptsammlungen sind sortiert; Zufallstimer ziehen aus `LogicRandom`; der erste Auswertungszeitpunkt verzögerter Skripte ist namensabgeleitet statt zufällig
  (Abweichung 3 in `docs/SCRIPT-ENGINE.md` §8). Der Test `SourceRules` gilt für den neuen Code unverändert und besteht.
- **Nicht replayfähig:** Skriptaktionen (Erzeugen, Töten, Geld, Besitzerwechsel, Beziehungen) sind direkte Weltänderungen, keine aufgezeichneten Befehle; Missionen werden über Snapshots
  verglichen, nicht über `ReplayRunner`.

## 23. Phase 10, Teil B Punkt 9: Topple, Höhenstopp und echte Crush-/Squish-Kollision

Quellen (Datei, Funktion) und Kompatibilitätsfragen stehen an den Modulen selbst (`Modules/BuiltIn/ToppleAndFallModules.cs`); diese Sektion fasst Semantik, Vereinfachungen und
Messwerte zusammen. Alle drei Module sind reine `CnCNew.Data`-Konsumenten (keine Wandzeit, kein `float`/`double`, keine Threads) und bestehen `SourceRules`.

### `ToppleUpdate` (`ToppleUpdate.cpp`)

Ein aufrechtes Objekt (Baum, Laterne, Zaun), das von einem Objekt mit `CrusherLevel` > 1 gerammt wird (`onCollide`, hier `SimulationWorld.Collision.cs` → `TryTopple`), kippt: eine
skalare Winkelgeschwindigkeit/-beschleunigung treibt eine Akkumulation von 0 bis zu einer festen Grenze (`π/2 − π/64`, wie im Original); wird die Grenze mit positiver
Geschwindigkeit erreicht, dämpft `BounceVelocityPercent` die Geschwindigkeit, bis sie unter eine feste Schwelle (0,01) fällt und das Objekt zur Ruhe kommt. `KillWhenStartToppling`
tötet sofort (keine Fallanimation); sonst tötet `KillWhenFinishedToppling` (Standard an) mit dem Todestyp `TOPPLED`, sobald das Kippen zur Ruhe kommt, und optional auch den per
`StumpName` erzeugten Stumpf (`KillStumpWhenToppled`). `ToppleLeftOrRightOnly` schränkt die Fallrichtung auf die nähere der beiden um ±90° gedrehten Objektachsen ein
(`angleClosestTo`) – das beeinflusst, wohin Leiche und Stumpf tatsächlich zu liegen kommen, nicht nur die Optik. **Nicht modelliert** (rein kosmetisch, da keine 3D-Transformation
existiert): die Ebenen-Realignment-Drehung aus `applyTopplingForce` (`ReorientToppledRubble`), Topple-/Bounce-FX und die Partikel-/Soundeffekte des Treffers; diese Felder sind
`Opaque` im Katalogschema. Eine Fehlabstimmung der drei Prozentfelder kann (wie im Original) zu einem stabilen Grenzzyklus führen, der die Schwelle nie unterschreitet – kein Fehler
der Portierung, sondern eine Eigenschaft der Formel; die Testfixtur wählt Werte, die zuverlässig innerhalb weniger Ticks konvergieren.

### `StructureToppleUpdate` (`StructureToppleUpdate.cpp`)

Reagiert auf den normalen Tod des Gebäudes (`DieMuxData`, Standardmaske): nach einer zufälligen Verzögerung (`MinToppleDelay`/`MaxToppleDelay`, aus `LogicRandom` gezogen) kippt das
Gebäude in eine aus der Angreiferposition abgeleitete Richtung (plus ±π/8 Jitter, ebenfalls aus `LogicRandom` – deterministisch, nicht der nicht-synchronisierte
`GameLogicRandomValueReal` des Originals) über eine sinusförmig beschleunigte, durch `StructuralIntegrity`/`StructuralDecay` gedämpfte Akkumulation bis `π/2`. Während des Kippens
feuert `CrushingWeaponName` Bodentreffer entlang der Falllinie, sobald der Neigungswinkel ≤ π/6 ist (`applyCrushingDamage`), über die bestehende Waffeninfrastruktur
(`IWorldServices.FireWeaponAt`). **Vereinfacht:** das Original feuert pro Tick ein Raster aus Schüssen über die gesamte Gebäudebreite (`doDamageLine`); portiert ist nur der
vorderste Punkt der Linie (`m_lastCrushedLocation`-Fortschritt), die seitliche Streuung entfällt – weniger Flächenschaden unter einem einstürzenden Gebäude, nicht keiner; das
Modul meldet das einmal je Objekt explizit (`SimEventKind.CreationSkipped`, `CreationSkipReasons.SettingNotSimulated`, Text „StructureToppleUpdate crushing grid sweep“). FX-Listen,
die Salven-Kadenz (`Min/MaxToppleBurstDelay`) und die `OCL`/`AngleFX`-Trümmerlisten sind `Opaque` (reine Optik); das Gebäude ändert keinen Modellzustand und wird nie selbst entfernt
(das Modul ist sein einziges Die-Modul und löst keine Zerstörung aus – wie die Original-Trümmer, die als Objekt bestehen bleiben).

### `HeightDieUpdate` (`HeightDieUpdate.cpp`)

Ein reiner Höhentest, jeden Tick nach `InitialDelay`: stirbt (`IWorldServices.Kill`), sobald die Z-Position unter die Zielhöhe (Geländehöhe plus `TargetHeight`, oder – mit
`TargetHeightIncludesStructures` – die Höhe des höchsten `STRUCTURE`-Objekts im eigenen `BoundingCircleRadius`, falls größer) fällt, außer `OnlyWhenMovingDown` ist gesetzt und das
Objekt bewegt sich gerade nicht abwärts. Ein enthaltenes Objekt (Transport) wird nie geprüft. `SnapToGroundOnDeath` setzt beim Sterben die Z-Position exakt auf die Geländehöhe; auch
ohne das Flag verhindert die gleiche Zeile ein Absinken unter das Gelände. **Nicht modelliert:** die Brücken-/Tunnel-Ebenenhöhe, die das Original zusätzlich zur flachen
Geländehöhe berücksichtigt (keine Pfadebenen in dieser Simulation) und `DestroyAttachedParticlesAtHeight` (reine Partikel-Aufräum-Optik, `Opaque`).

### Echte Kollision für `CrushDie`/`SquishCollide`

Beide Module bestanden seit Phase 5 nur, damit ihre Objekte instanziierbar werden; ihre Wirkung (Zerquetschen-Flaggen bzw. Zerquetschen-Tod) wurde nie ausgelöst, weil es keine
Kollisionserkennung gab (vgl. die Kommentare an den Modulen selbst). Seit Phase 10 ruft `AIUpdateInterface.Move`/`MoveStraight` (`Modules/BuiltIn/Phase5Modules.cs`) nach jeder
Positionsänderung eines Bodenobjekts `IWorldServices.ResolveFootprintCollisions(mover, stepX, stepY)` auf (implementiert in der neuen Datei `World/SimulationWorld.Collision.cs`):

- **Topple** (`TryTopple`): überlappen sich die Begrenzungskreise von Angreifer und Ziel und hat der Angreifer `CrusherLevel` > 1, wird `ToppleUpdate.ApplyTopplingForce` auf dem
  Ziel aufgerufen (Richtung vom Angreifer zum Ziel, Geschwindigkeit = aktuelle Geschwindigkeit des Angreifers) – keine Allianzprüfung, wie im Original.
- **Squish** (`TrySquish`): ist das Ziel kein Verbündeter des Angreifers (`RelationshipOf`), sein `SquishCollide` vorhanden, die beiden Begrenzungskreise mit einem auf 1,0 verkleinerten
  Zielradius überlappen (wie im Original: „der Panzer muss die Infanterie wirklich treffen“) und verringert der tatsächliche Schritt dieses Ticks (`stepX`/`stepY`, nicht eine aus
  `Angle`/`Speed` neu berechnete Geschwindigkeit – das vermeidet Vorzeichenfehler durch `Sin`/`Cos`-Rundung exakt auf der Fahrlinie) den quadrierten Abstand zum Ziel, stirbt das
  Ziel sofort (`DamageType CRUSH`, `DeathType CRUSHED`, Schaden `HUGE_DAMAGE_AMOUNT`, unresistierbar in der Höhe, aber als normaler Schaden durchgereicht).
- `mover.Template.CrusherLevel`/`CrushableLevel` sind neue, strikt typisierte Felder auf `ObjectTemplate` (`Templates/ObjectTemplate.cs`, `Object`-Schlüssel `CrusherLevel`/
  `CrushableLevel`, Standard 0/255 wie im Original); `CrushableLevel` wird noch nicht ausgewertet (im Original nur für `canCrushOrSquish`, das in dieser Simulation nicht existiert –
  jede Einheit mit `SquishCollide` ist quetschbar, unabhängig von ihrem eigenen `CrushableLevel`; eine künftige Phase kann das nachtragen).

### Lokale Ausweichbewegung (`AvoidLocalBlockers`, keine Originalfunktion)

Kein Original-Mechanismus, sondern eine bewusst einfache Ergänzung, damit Bodeneinheiten einander nicht klaglos durchdringen: Bevor `AIUpdateInterface` den geplanten Schritt zum
nächsten Wegpunkt anwendet, prüft `AvoidLocalBlockers`, ob die Zielposition innerhalb des Begrenzungskreises einer anderen lebenden, beweglichen Einheit (`Template.Locomotor` gesetzt
– Gebäude blockieren bereits über `NavigationGrid`) liegt; falls ja, wird der Schrittvektor nacheinander um vier feste Winkel gedreht (±45°, ±90°) und erneut geprüft, der erste freie
Versuch gewinnt. Bleiben alle vier Versuche blockiert, bewegt sich die Einheit unverändert in den Blocker hinein (was dort ggf. `ResolveFootprintCollisions` auslöst). **Ehrliche
Grenzen:** das ist ein beschränktes Ausweichen, keine Steuerung/kein Flocking – es schaut nur einen Schritt voraus, stimmt sich nicht mit dem Blocker ab und kann mit einer
entgegenkommenden Einheit pendeln; es wirkt nur mit vorhandenem `NavigationGrid` (nicht im terrainlosen Testmodus) und ändert nie den geplanten Pfad selbst, nur den einzelnen
Schritt.

### Nicht verhandelbare Einträge

Katalogschema und Bindung: `src/CnCNew.Data/Registry/ModuleCatalog.cs`, `src/CnCNew.Simulation/Modules/BuiltIn/ToppleAndFallModules.cs`,
`src/CnCNew.Simulation/Modules/BuiltIn/BuiltInModules.cs` (Registrierung). Tests: `tests/CnCNew.Simulation.Tests/Tests.Collision.cs` (fünf Testfälle: Crush/Squish,
`ToppleUpdate` inklusive Fortsetzungstest nach einem Snapshot mitten im Fall, `StructureToppleUpdate`, `HeightDieUpdate`, lokale Ausweichbewegung), registriert in
`tests/CnCNew.Simulation.Tests/Program.cs`. Kompatibilitätsbasislinie nach `IniTool compat-baseline`: 756 Einträge (vorher 749, siehe §11). Additive Erweiterungen an geteilten
Dateien, die andere Agenten parallel bearbeiten könnten: zwei neue Methoden auf `IWorldServices`/`SimulationWorld` (`AvoidLocalBlockers`, `ResolveFootprintCollisions`, beide in der
neuen Datei `World/SimulationWorld.Collision.cs`), zwei neue Felder auf `ObjectTemplate` (`CrusherLevel`, `CrushableLevel`), ein neuer Eintrag in der Bindungsliste
`BuiltInModules.cs` und zwei minimale Aufrufe (keine Logikänderung) im Bewegungscode von `AIUpdateInterface` (`Modules/BuiltIn/Phase5Modules.cs`).
