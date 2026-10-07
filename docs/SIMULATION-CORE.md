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

### Zeit und Einheiten

Die Tickrate ist ein **Wert der Welt** (Phase 20; Einzelheiten, Zählungen und die offenen Listen in `docs/TICKRATE-MIGRATION.md`):

- **Rate:** `WorldConfiguration.Rate` vom Typ `SimulationRate`. Unterstützt sind genau 30, 60 und 120 Hz (30 · 2ᵏ); alles andere wird beim
  Erzeugen von Welt, Katalog, Snapshot und Replay abgelehnt. Der Katalog (`TemplateCatalog.Build(…, rate, …)`) wird **für eine Rate gebaut**
  – er enthält Ticks und Werte je Tick –, und eine Welt lehnt einen Katalog einer anderen Rate ab. Bis Phase 23 laufen alle Sitzungen mit
  30 Hz; **bei 30 Hz ist jedes Ergebnis unverändert** (Beleg: `SimTool reference-manifest check`, `input-manifest check`).
- **Drei Einheiten für Zeit.** *Tick*: ein Schritt der Simulation (`SimulationWorld.Tick` ist die Zahl der ausgeführten Ticks und zugleich
  die Nummer des nächsten); Zustand, Snapshots, Replays und Befehle tragen Ticks der Rate der Welt. *Bild des Originals*: 1/30 s
  (`LOGICFRAMES_PER_SECOND`, `GameCommon.h:72`; `SimulationRate.OriginalFramesPerSecond` ist die einzige benannte 30 der Simulation) –
  die Einheit aller Konstanten des Originals, aller eigenen Fristen und Rhythmen und der Bildzähler der Kartenskripte. *Sekunden und
  Millisekunden*: die Einheit der Daten. Es gibt keine Wandzeit im Kern.
- **Rasterregel:** Dauern bleiben auf dem 30-Hz-Raster des Originals. Eine Dauer aus den Daten wird wie im Original auf ganze Bilder
  aufgerundet und dann mit Rate/30 multipliziert (eine Nachladezeit von 50 ms dauert bei jeder Rate zwei Bilder); eine Konstante in Bildern
  wird mit Rate/30 multipliziert; was zur Laufzeit gerundet, begrenzt, gezählt oder gewürfelt wird, wird in Bildern gerechnet und erst das
  Ergebnis skaliert (`FrameGrid`: `AtLeastOneFrame`, `CountdownReload`, `DrawFrames`, `FloorToFrame`). Größen je Sekunde werden durch die
  Rate bzw. ihr Quadrat geteilt, additive Rohwerte je Bild durch Rate/30 bzw. dessen Quadrat.
- **Ein Weg:** jede Umrechnung geht über den Zeitdienst `SimulationTime` – zur Laufzeit `world.Time`, beim Katalogbau der Zeitdienst der
  Rate (`configuration.Time`, `timing`). Kein Modul rechnet mit einer Rate. Literale, die Reihenfolge meinen („im nächsten Tick“), werden
  mit `TickOrder.Next` geschrieben und nie skaliert; `SimulationTiming` enthält nur noch, was nicht von der Rate abhängt (`SleepNone`,
  `SleepForever`, Grad → Bogenmaß, Verhältnisse). Der Test `SourceRules` verbietet nackte Tickzahlen in der Simulation
  (`docs/TICKRATE-MIGRATION.md` §10).
- **Nicht erledigt:** richtiges Verhalten bei 60 und 120 Hz. Die Simulation läuft dort durch und ist deterministisch
  (`docs/TICKRATE-MIGRATION.md` §11); Integration je Tick, multiplikative Faktoren je Bild und Abtastung je Tick stehen als Liste für
  Phase 23 in §5 dort.

- **Zahlenart:** Zustand besteht nur aus Ganzzahlen und `Fixed`. `Fixed` ist seit Phase 22 ein Q39.24-Festkomma in
  einem `long` (24 Nachkommabits, Auflösung 2⁻²⁴ ≈ 6·10⁻⁸ Welteinheiten, Bereich ±5,5·10^11; bis Phase 22: Q47.16,
  Auflösung 2⁻¹⁶, Bereich ±1,4·10^14 – alle Rohwerte, Hashes und Formatversionen in den Abschnitten vor §38 stammen
  aus dieser Zeit und sind überholt). Die Zahl der Nachkommabits steht an genau einer Stelle (`Fixed.FractionBits`);
  die Arithmetik (`FixedMath`) ist für eine Bitzahl hergeleitet, nicht für sechzehn (§38, `docs/FIXED-POINT-Q39-24.md`).
  Begründung der Zahlenart: Das Original hält
  binary32-`Real` im Zustand und setzt dafür `setFPMode()` (`INI::load`); Ganzzahlarithmetik ist auf jeder Plattform
  bitgleich. Produkte und Quotienten sind exakt die der 128-Bit-Formeln. Ein Wert über 741.455 darf nicht quadriert
  werden (sein Quadrat verlässt den Bereich und wirft).
- **Rundungsregel (überall):** zur nächsten Zahl, Halbwerte weg von null. Überlauf wirft `OverflowException`;
  nichts wickelt oder sättigt still. Division durch null wirft.
- **Umrechnungen** (`SimulationTime`; R = Rate, f = R/30). Rundung und Reihenfolge je Funktion stehen in
  `docs/TICKRATE-MIGRATION.md` §2; bei 30 Hz ist jede Zeile der frühere Ausdruck.

| Datenwert | Regel | Referenz |
|---|---|---|
| Dauer in ms → ganze Ticks | `ceil(ms · 30 / 1000) · f`, exakt in Ganzzahl-/Dezimalarithmetik; 0 ms = 0 Ticks; negativ → Fehler | `INI::parseDurationUnsignedInt` (`ceilf`) |
| Dauer in ms → Ticks (ungerundet) | `Fixed(ms · R / 1000)` | `INI::parseDurationReal` |
| Sekunden (`BuildTime`) → Ticks | `ceil(s · 30) · f` | – |
| Bilder des Originals → Ticks | `n · f` (exakt) | Konstanten des Originals in Logikbildern |
| Geschwindigkeit pro s → pro Tick | `v / R` | `ConvertVelocityInSecsToFrames` |
| Beschleunigung pro s² → pro Tick² | `a / R²` | `ConvertAccelerationInSecsToFrames` |
| Rohwert je Bild → je Tick (additiv) | `v / f`, je Bild² `v / f²` | Felder, die das Original je Bild anwendet |
| Grad → Bogenmaß | `deg · π / 180`, π auf 28 Stellen | `INI::parseAngleReal` |
| Grad/s → Bogenmaß/Tick | `deg · π / 180 / R` | `ConvertAngularVelocityInDegreesPerSecToRadsPerFrame` |
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
Speichervertrag) und weist ab: leere/abgeschnittene/verstümmelte Dateien, fremde Version/Format, eine nicht unterstützte
Tickrate (seit Phase 20 wird die Welt mit der gespeicherten Rate erzeugt – 30, 60 oder 120 Hz –, und der übergebene Katalog
muss für genau diese Rate gebaut sein; `docs/TICKRATE-MIGRATION.md` §7), abweichenden
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
lehnt ein Replay ab, dessen Fingerabdruck oder Befehlstabellenversion nicht passt, dessen Tickrate nicht unterstützt ist oder
nicht die des Katalogs bzw. der übergebenen Welt ist (`Run(replay, world)` prüft die Rate der Welt), und meldet Abweichungen an
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

- **Stand nach Zusammenführung von Kollision und Spezialfähigkeits-Hülle (1. Oktober 2026, neu zu messen): beide Zweige unten wurden unabhängig
  voneinander auf demselben Phase-9-Stand gemessen und danach gemergt; 91 von 205 Modultypen implementiert (4 neu gegenüber dem Phase-9-Stand:
  `ToppleUpdate`, `StructureToppleUpdate`, `HeightDieUpdate`, `SpecialAbilityUpdate`).** Instanziierbarkeit, Modulverwendungen, Kompatibilitätsbasislinie
  und Referenzmatch-Hash sind nach dem Merge neu zu messen (siehe Abschlussbericht dieser Phase für die frischen Zahlen).
- **Stand nach Phase 10, Kollision (1. Oktober 2026, frisch gemessen): 2.115 Objekte, 1.588 vollständig instanziierbar (75,1 %)**, 527 blockiert; 90 von 205 Modultypen
  implementiert (3 neu: `ToppleUpdate`, `StructureToppleUpdate`, `HeightDieUpdate`, §23), sie decken 15.752 von 19.553 Modulverwendungen (80,6 %); Fingerabdruck `D112D8B5AF345559…`.
  `ToppleUpdate` (76/66) und `HeightDieUpdate` (31/24) verschwinden vollständig aus der Blockerliste; größte verbleibende Blocker: `SpecialAbilityUpdate` 64/36,
  `JetSlowDeathBehavior` 59/0, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/5, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25,
  `BaikonurLaunchPower` 23/23. Retail-Referenzmatch (`SimTool reference-match`): Sieger FactionAmerica bei Tick 10.575 (Replay 10.876 Ticks), Endhash (`final hash`, Run 1)
  `057C6678FE8F3381` (verändert sich gegenüber dem Phase-9-Stand, weil zuvor blockierte Objekte – u. a. Bäume und Höhenstopp-Objekte auf den Startkarten – jetzt instanziierbar
  sind und am Kampf teilnehmen); zwei frische Läufe und eine Fortsetzung ab Tick 5.438 liefern identische Hashes. Der synthetische 216.000-Tick-Stresstest trägt keine der drei
  neuen Module und bleibt unverändert bei `ED6F9329F59662E3`. Kompatibilitätsbasislinie 756 Einträge (vorher 749): 9 `module`-Einträge entfallen (jetzt implementiert), 16 neue
  `deferred-field`-Einträge für reine Präsentationsfelder der drei Module (FX-Listen, `ReorientToppledRubble`, Salven-Timing, `DestroyAttachedParticlesAtHeight`).
- **Stand nach Phase 10, Spezialfähigkeits-Hülle (1. Oktober 2026, frisch gemessen): 2.115 Objekte, 1.530 vollständig instanziierbar (72,3 %)**, 585 blockiert; 88 von 205
  Modultypen implementiert (1 neu: `SpecialAbilityUpdate`, ehrliche Hülle, siehe §23), sie decken 15.730 von 19.553 Modulverwendungen (80,4 %);
  Kompatibilitätsbasislinie 751 Einträge. `SpecialAbilityUpdate` fällt aus der Blockerliste (vorher 64 Objekte/36 nur dadurch blockiert). Der
  Retail-Referenzmatch verändert sich dadurch (Ranger, Jarmen Kell, GLA-Rebellin u. a. sind jetzt vollständig instanziierbar, nicht nur ihre Hülle):
  Sieger FactionAmerica bei Tick 14.550 (Replay 14.851 Ticks), Endhash (`final hash`, Run 1) `3DA826C10BB6781B`; zuvor Tick 13.665 (13.966 Ticks),
  `D4779BE413835B6D`. Details in §23.
- Stand nach den Phase-9-Missionsmodulen (1. Oktober 2026): 2.115 Objekte, 1.494 vollständig instanziierbar (70,6 %), 621 blockiert; 87 von 205 Modultypen implementiert (2 neu: `CommandButtonHuntUpdate`, `LockWeaponCreate` – ehrliche Hüllenmodule, siehe unten), sie decken 15.639 von 19.553 Modulverwendungen (80,0 %); Fingerabdruck `2B9D902AEC46A6DE…`. Größte verbleibende Blocker (Objekte insgesamt / nur dadurch blockiert): `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/36, `JetSlowDeathBehavior` 59/0, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/5, `HeightDieUpdate` 31/24, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `BaikonurLaunchPower` 23/23. **Hüllenmodule:** `CommandButtonHuntUpdate` (86 Verwendungen, keine Schlüssel; wird im Original nur von `TEAM_HUNT_WITH_COMMAND_BUTTON` ausgelöst) hält keinen Zustand, `LockWeaponCreate` sperrt den Primär-Slot (`SlotToLock = PRIMARY` in allen 12 Verwendungen; die Simulation führt nur eine Waffe, ein anderer Slot ist eine `UnsupportedModuleConfigurationException`); die auslösenden Skriptaktionen bleiben fehlend und werden gezählt (`docs/SCRIPT-ENGINE.md` §8). Der Retail-Referenzmatch verändert sich dadurch (Ranger u. a. entstehen jetzt als Startobjekte): Sieger FactionAmerica bei Tick 13.665 (Replay 13.966 Ticks), Endhash (`final hash`, Run 1) `D4779BE413835B6D`; zuvor Tick 13.800 (14.101 Ticks), `95022B80366E841E`. Der Stresstest (216.000 Ticks) bleibt bei `ED6F9329F59662E3`.
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
- Grenzen (Stand Phase 6, siehe §23 für den Phase-10-Stand): `QueueProduction` prüfte weder Voraussetzung noch Fähigkeit des Gebäudes, `MaxSimultaneousOfType`
  wurde nicht geprüft. Reparatur ist (und bleibt) kostenlos – das ist keine Vereinfachung, siehe §23.

## 17. Kraftwerksmodule und CommandCatalog (Phase 6)

`PowerPlantUpgrade` (`EnergyBonus` bei Upgrade), `PowerPlantUpdate` (Stäbe ausfahren, passiv) und `OverchargeBehavior` (Energiebonus mit Gesundheitsverlust, schaltet sich bei niedriger Gesundheit selbst ab) verändern die Energieproduktion des
Spielers über `IWorldServices.AddEnergyProduction`; `ObjectTemplate.EnergyBonus` und `CommandSets` sind neu. `TemplateCatalog.Commands` (`CommandCatalog`) löst `CommandSet` → `CommandButton` auf und
beantwortet `OffersBuild` (welches Objekt was bauen oder produzieren kann); die KI nutzt es für Rollen (§18). Grenzen (Stand Phase 6): kein Eroberungsverhalten, kein
Umschaltbefehl für Overcharge, Reset entfernt auch den Bonus. Seit Phase 10: `OverchargeCommand` und eine reduzierte Kraftwerkseroberung (`CaptureCommand`), §23.

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
- **Sicht:** kreisförmig, keine Höhen-Sichtlinie, Aktualisierung alle 3 Ticks, keine Detektoren. (Beides seit Phase 10 vorhanden: `StealthDetectorUpdate`
  kam mit den Phase-7/8/9-Blockermodulen hinzu, eine Höhen-Sichtlinie für Detektoren seit Phase 10, §23; die kreisförmige Sichtweite/Aufdeckung selbst
  (`VisionMap`, §15) bleibt ohne Höhenverdeckung.)
- **Bau:** `QueueProduction` ohne Voraussetzungs-/Fähigkeitsprüfung, `MaxSimultaneousOfType` ungeprüft, Reparatur kostenlos. (Die ersten zwei seit Phase 10
  behoben, §23; Reparatur ist weiterhin kostenlos, aber das ist keine Vereinfachung – siehe §23.)
- **Kraftwerke:** keine Eroberung, kein Umschaltbefehl für `Overcharge`, Reset entfernt auch den Bonus. (Die ersten zwei seit Phase 10 vorhanden, als
  reduzierter `CaptureCommand` bzw. `OverchargeCommand`, §23.)
- **KI:** Spezialkräfte nicht klassifiziert, Startpositionen der Gegner sind öffentliches Wissen (`HintPositions`), von der Karte nur Lagerhäuser gesetzt, Startgeld und Science-Punkte vom System, keine Flugzeuge/Flugplätze,
  (seit Paket E1 mit Chinook-Wirtschaft, Tunneln und Transportern, §21), Armeezusammensetzung durch Instanziierbarkeit begrenzt, gleichstarke Paarungen können in ein Patt laufen (1 von 16), ohne Detektoren können getarnte Überlebende verborgen
  bleiben.
- **Offen aus früheren Phasen:** Block 0.4 (RNG-Abgleich mit einem kompilierten Original; Projektilflugzeit und `ChinookAIUpdate`/`TransportAIUpdate` seit Paket E1, §21;
  `ToppleUpdate`/`StructureToppleUpdate`/`HeightDieUpdate` und echte `CrushDie`/`SquishCollide`-Kollision seit Phase 10, §23). Seit Phase 10 teilweise geklärt (§23): Wirkung
  von `CommandSet`/Radar, Veteranen-`ArmorSet`/`WeaponSet` (implementiert, aber in Retail-Daten ohne messbare Wirkung), Kraftwerkseroberung und Overcharge-Umschaltbefehl
  (reduzierte Fassungen). Weiterhin offen: `CommandButtonHuntUpdate` und `SpecialAbilityUpdate` als volle Implementierungen (bleiben ehrliche Hüllenmodule).

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

## 24. Phase 10: Befehlsknopf-Fähigkeiten, lokale Seite

Details, Quellverweise und Messwerte in `docs/SCRIPT-ENGINE.md` §5/§7/§8/§9; dieser Abschnitt nennt nur die neuen Schnittstellen.

- **Neues Modul** `SpecialAbilityUpdate` (`Modules/BuiltIn/SpecialPowerModules.cs`): eine `SpecialPowerModuleBase`-Hülle ohne eigene Wirkung (siehe
  `docs/GAME-RULES.md` §6d); Katalogschema in `CnCNew.Data.Registry.ModuleCatalog` (31 im Bestand verwendete Felder, Quelle `SpecialAbilityUpdate.cpp`).
  Modultypen implementiert jetzt 88 von 205 (vorher 87); Instanziierbarkeit 1.530 von 2.115 Objekten (72,3 %, vorher 70,6 %), Modulverwendungen
  abgedeckt 15.730 von 19.553 (80,4 %, vorher 80,0 %); Kompatibilitätsbasislinie **751** Einträge (vorher 747 — fünf neue `deferred-field`-Einträge für
  die Sound-/Partikelfelder des Moduls, ein `MISSING module`-Eintrag entfällt; `IniTool compat-baseline` neu geschrieben, Review-Gegenstand).
- **Neue, additive API** (keine bestehende Signatur geändert):
  - `CnCNew.Simulation.Templates.CommandCatalog.TryGetSpecialPowerAbility(string buttonName, out string specialPower)`: löst einen `CommandButton`
    (`Command = SPECIAL_POWER*`) global auf seinen `SpecialPowerTemplate`-Namen auf; `CommandCatalog.Build` füllt die Tabelle zusätzlich zu den
    bestehenden Baulisten.
  - `MissionSession.LocalPlayerOf(ScriptProgram, string humanSideName = "")` (zusätzlicher Parameter, Standard unverändert) und
    `MissionOptions.HumanSideName` (zusätzlicher Record-Parameter mit Standard `""`): der Host kann die Seite des menschlichen Betrachters benennen,
    unabhängig von `playerIsHuman` (Generals'-Challenge-Karten ohne markierte menschliche Seite). `CampaignController.StartCurrentMission` bekommt
    denselben zusätzlichen Parameter; `CampaignTool audit|playthrough` ein neues `--human-side <Name>`.
- **Zustandsformat:** `MissionSnapshot.Version` **2** (vorher 1): `options.humanSideName` ergänzt; ältere Missionszustände werden mit
  `SnapshotFormatException` abgelehnt (keine stille Migration). Welt-Snapshot (7), Replay (7) und `CommandTypes.Version` (9) sind unverändert.
- **Determinismus:** `SpecialAbilityUpdate` fügt keinen eigenen Zustand hinzu (die geerbte Nachlademaschine von `SpecialPowerModuleBase` deckt es ab);
  der `SourceRules`-Test besteht unverändert. `CommandCatalog`s neue Tabelle wird aus den Definitionen gebaut, nicht zur Laufzeit verändert.

## 25. Phase 10: Bau-/Regelgrenzen, Veteranen-Sätze, Höhen-Sichtlinie, Capture

Quelle und Auftrag: `docs/HANDOFF-PHASE10.md` Punkt 10 (Fortsetzung der in Phase 9 offen gebliebenen Teile A/B). Neue Dateien: `World/SimulationWorld.PowerAndCapture.cs`
(Overcharge-/Capture-Befehl, `MaxSimultaneousOfType`-Zähler teilt sich `World/SimulationWorld.Construction.cs`). Geänderte Dateien: `Commands/Commands.cs` (`CommandTypes.Version` 9 → 10,
Codes 28/29, Ablehnungsgründe 35–38), `Replay/ReplayFormat.cs` (Kodierung der zwei neuen Befehle, Containerformat selbst unverändert), `Events/SimEvent.cs` (`StructureCaptured` = 29),
`Templates/ObjectTemplate.cs` (`MaxSimultaneousOfType`, `VeteranArmor`/`VeteranWeapon`), `World/SimulationWorld.cs` (`ActiveArmor`/`ActiveWeaponIndex` nach Veteranenstufe, `IsDock`,
`AddEnergyProduction`-Nachbarschaft), `World/SimulationWorld.Construction.cs` (`CanProduceTemplate`, `ExceedsMaxSimultaneousOfType`, `CanQueueProductionTemplate`, öffentliches
`CheckConstruct`), `Modules/BuiltIn/Phase5Modules.cs` (`UpgradeMuxModule.Executed`, `ProductionUpdate.Queue`/`CountInQueue`), `Modules/BuiltIn/SpecialPowerModules.cs`
(`SpecialPowerModuleBase.AvailableOnFrame`), `Modules/BuiltIn/Phase5BlockerModules.cs` (`StealthDetectorUpdate`, Höhen-Sichtlinie), `Terrain/TerrainMap.cs` (`HasHeightLineOfSight`),
`Skirmish/SkirmishMatchSession.cs` (`CreateSave`-Sperre), `Testing/ReferenceMatchScenario.cs` (Produktionsauswahl respektiert jetzt Voraussetzungen).

### Bauregeln (Punkt 1)

- **Voraussetzungs- und Fähigkeitsprüfung für Einheiten:** `QueueProductionCommand` prüfte bisher nur `IsInstantiable`, Eigentümerschaft und Geld; `ConstructCommand` (Gebäude) hatte
  seit Phase 6 bereits `CanBuildTemplate`/`PrerequisitesSatisfied`. `CanProduceTemplate` (`SimulationWorld.Construction.cs`) überträgt dieselbe Regel auf produzierte Einheiten: die
  Vorlage darf kein `STRUCTURE` sein (die werden nur über `ConstructCommand` platziert – `Player::canBuildMoreOfType`s eigener `checkProductionInterface`-Unterschied in
  `Player.cpp`), `Buildable = No` ist gesperrt, `Only_By_AI` nur für Computerspieler, die Seite muss zur des Produzenten passen (leere Seite auf beiden Seiten gilt als Freipass, wie bei
  Gebäuden). `PrerequisitesSatisfied` (unverändert, §16) wird jetzt auch für Einheiten aufgerufen. Neuer Ablehnungsgrund `CannotProduceTemplate` (35); `MissingPrerequisite` (12, schon
  vorhanden) wird wiederverwendet, weil die Bedeutung identisch ist. Quelle: `Player::canBuild` (`Player.cpp`), dessen `BSTATUS_*`-Fallunterscheidung Gebäude und Einheiten nicht
  unterscheidet.
- **`MaxSimultaneousOfType`:** `ObjectTemplate.MaxSimultaneousOfType` (0 = unbegrenzt) ist jetzt typisiert (`ThingTemplate::getMaxSimultaneousOfType`). `ExceedsMaxSimultaneousOfType`
  zählt die lebenden, nicht toten Objekte exakt dieser Vorlage des Spielers plus – nur für Nicht-`STRUCTURE`-Vorlagen – alle noch in irgendeiner `ProductionUpdate`-Warteschlange
  dieses Spielers stehenden Einträge derselben Vorlage (`ProductionUpdate.CountInQueue`, neu öffentlich); Quelle `Player::canBuildMoreOfType`/`countExisting` (`Player.cpp`). Gilt für
  `ConstructCommand` und `QueueProductionCommand` gleichermaßen. Neuer Ablehnungsgrund `ProductionLimitReached` (36). **Nicht modelliert:** `MaxSimultaneousLinkKey` (eine
  `NameKeyType`-Gruppierung mehrerer Vorlagen unter einem gemeinsamen Zähler); in `.local/phase2-import-a` kommt der Schlüssel in 17 Dateien vor, aber nie zusammen mit
  `MaxSimultaneousOfType` an genau der gruppierten Stelle, die den Unterschied sichtbar machen würde – die Lücke ist dokumentiert, nicht gemessen folgenlos.
  Gemessene Wirkung auf echte Daten: `AmericaInfantry.ini` (Colonel Burton, `MaxSimultaneousOfType = 1`), `AirforceGeneral.ini`, `TankGeneral.ini` und 21 weitere Objektdateien tragen
  den Schlüssel; die Skirmish-KI besaß vorher keinerlei Kappungs-Bewusstsein und hätte (ohne das hier neue, serverseitige Verbot) beliebig viele Kopien einer eigentlich einmaligen
  Heldeneinheit anfordern können – `SkirmishAi`s eigene `PrerequisitesMet`-Vorprüfung kennt `MaxSimultaneousOfType` weiterhin nicht, verlässt sich also jetzt auf diese serverseitige
  Schranke als Netz (ein abgelehnter Befehl ist für die KI nur ein verlorener Denkschritt, kein Fehler).
- **Reparaturkosten – kein Fehlverhalten, sondern eine bereits korrekte Portierung:** geprüft gegen `DozerAIUpdate.cpp` (`privateRepair`/`DozerPrimaryStateMachine`'s Heilschritt:
  `health = maxHealth * getRepairHealthPerSecond() / LOGICFRAMES_PER_SECOND`, dann `attemptHealingFromSoleBenefactor`) und `RepairDockUpdate.cpp` (`action`:
  `healthToAddPerFrame = (maxHealth - health) / framesForFullHeal`) – **keiner der beiden Reparaturwege im Original zieht Geld ab.** Die bisherige Doku-Formulierung „Reparatur:
  kostenlos … eine Vereinfachung“ (§16/§20, `docs/GAME-RULES.md` §11) war demnach unpräzise: es ist keine Vereinfachung, sondern die treue Portierung beider Quellfunktionen. Es gibt
  deshalb absichtlich **keine** Codeänderung an der bestehenden kostenlosen Reparatur.
- **Kraftwerkseroberung und der `Overcharge`-Befehl** (`World/SimulationWorld.PowerAndCapture.cs`): `OverchargeBehavior` (seit Phase 6 implementiert, `RetailMatchModules.cs`) hatte nie
  einen Befehl, der `Toggle` erreicht – die Schaltfläche `CommandButton.ini`s `Command = TOGGLE_OVERCHARGE` lief ins Leere. `OverchargeCommand` (Typcode 28) prüft Eigentümerschaft,
  Lebendigkeit, Fertigstellung und das Vorhandensein eines `OverchargeBehavior`-Moduls (sonst `CannotToggleOvercharge`, 38) und ruft dann dessen bereits vorhandenes `Toggle` auf – keine
  neue Spielmechanik, nur der fehlende Draht zur bestehenden.
  Kraftwerkseroberung ist dagegen eine echte Lücke: im Original hängt „ein Gebäude einnehmen“ komplett an `SpecialAbilityUpdate` (`ActionManager::canCaptureBuilding` verlangt
  `obj->hasSpecialPower(SPECIAL_*_CAPTURE_BUILDING)`, eine kanalisierte, wissenschafts- und tarnungsabhängige Fähigkeit), und `SpecialAbilityUpdate` ist mit 64 blockierten Objekten (36
  nur dadurch) der zweitgrößte verbleibende Blocker (§11) – ein eigenständiges, mehrere unzusammenhängende Fähigkeiten (Tarnen, Entführen, Hacken, Kampfabsprung, Einnehmen) umfassendes
  Modul, dessen vollständige Umsetzung den Rahmen dieser Phase sprengt. `CaptureCommand` (Typcode 29, Ablehnungsgrund `CannotCapture` = 37) bildet stattdessen nur das **Ergebnis** nach:
  ein beliebiges eigenes, bewegliches (ein `AIUpdateInterface` besitzendes), nicht im Bau befindliches Objekt kann ein für den Spieler sichtbares, fertiges, nicht verkauftes
  `STRUCTURE`-Ziel direkt übernehmen, wenn es entweder einem Feind gehört oder `KINDOF_CAPTURABLE` trägt und nicht einem Verbündeten gehört (reduziertes
  `ActionManager::canCaptureBuilding`), und wenn der Akteur bereits in Reichweite steht (`BoundingCircleRadius` + 2 Zellen, wie die Baumeister-Reichweite in `UpdateTask`, §16) –
  **ohne** Kanalisierungstimer, Wissenschaftsvorbehalt oder Tarnungs-Erkennungs-Check der echten Fähigkeit. `ExecuteCapture` überträgt die statische `EnergyProduction` (wie die
  Skript-Engine-eigene, unveränderte `TransferOwnership` es für `NAMED_TRANSFER_ASSETS_TO_PLAYER` schon tat) und zusätzlich jeden gerade gewährten dynamischen Bonus (ein ausgeführtes
  `PowerPlantUpgrade`, ein aktives `OverchargeBehavior`) – das war die eigentliche Lücke: beide Module hatten keinerlei Besitzerwechsel-Rückruf, der `TransferOwnership` schon vorher
  nicht erreichte. Nicht übertragen/zurückgesetzt: Produktionswarteschlange, Upgrades, Garnisoninhalt. Quelle für den begrenzten Mechanismus-Zuschnitt: `CommandTypes.Capture`s
  XML-Kommentar in `Commands.cs` zitiert dieselbe Stelle in `ActionManager.cpp`.
- **`CommandSet`/Radar – geprüft, keine weitere Wirkung gefunden:** `CommandSet` hat bereits eine echte Spielwirkung (`TemplateCatalog.Commands`/`CommandCatalog.OffersBuild`, von der
  Skirmish-KI für Rollen genutzt, §17/§18); was fehlte, war nicht „Wirkung“, sondern der oben beschriebene `Overcharge`-Befehl – derselbe Fund wie oben, hier nur aus der
  `CommandSet`-Perspektive dokumentiert. Radar wurde gegen die Referenz geprüft: `Player::hasRadar()` (`Player.h`/`.cpp`) wird außerhalb dieser beiden Dateien nirgends in
  `GameLogic` gelesen – keine Spezialkraft, kein Gebäude, keine Sichtregel hängt davon ab. Der einzige Spielwirkung von Radar im Original ist die Minimap-Übersicht und
  Radar-Pings, beides Präsentation. `RadarUpdate`/`RadarUpgrade` (seit Phase 6 implementiert) tragen bereits den vollständigen Simulationsvertrag (`Extended`/`IsExtendComplete`);
  es gibt keine weitere Simulationswirkung nachzuholen.

### Veteranen-`ArmorSet`/`WeaponSet`-Auswahl (Punkt 2)

`ObjectTemplate.VeteranArmor`/`VeteranWeapon` (je ein 4-elementiges Array, Index 1..3 = `Entity.VeterancyLevel`) werden aus `ArmorSet`/`WeaponSet`-Blöcken mit genau einer Bedingung
`VETERAN`/`ELITE`/`HERO` befüllt (`ObjectTemplate.ResolveArmor`/`ResolveWeapon`, `VeterancyConditionIndex`). `SimulationWorld.ActiveArmor`/`ActiveWeaponIndex` wählen zuerst den zur
aktuellen Stufe passenden Veteranensatz, dann `PLAYER_UPGRADE`, zuletzt den Standardsatz. **Modellierungsgrenze (unverändert gegenüber Phase 5, nur erweitert):** nur Sätze mit genau
einer Bedingung werden aufgelöst; ein Satz, der zwei Bedingungen gleichzeitig verlangt (z. B. `PLAYER_UPGRADE VETERAN`), wird nicht unterschieden – der `SparseMatchFinder` des
Originals score-basiert über beliebige Kombinationen zu matchen bleibt offen. **Gemessene Wirkung auf echte Daten: keine.** Eine Volltextsuche über `.local/phase2-import-a` nach
`Conditions` mit `VETERAN`/`ELITE`/`HERO` in einem `ArmorSet`- oder `WeaponSet`-Block ergibt **null Treffer** – kein einziges Zero-Hour-Objekt nutzt diese Bedingungen überhaupt, die
Veteranenstufe wirkt im Original nach wie vor hauptsächlich über Waffenbonus und Gesundheit (§6h in `docs/GAME-RULES.md`). Die Auswahl ist damit für zukünftige Daten korrekt, ändert
aber weder `audit-coverage` noch `reference-match` am echten Bestand.

### Detektoren und Höhen-Sichtlinie (Punkt 3)

`StealthDetectorUpdate` existierte bereits (seit den Phase-7/8/9-Blockermodulen, `Phase5BlockerModules.cs`) und erkannte getarnte Objekte in Reichweite bereits korrekt (geprüft:
`Tests.Phase5StealthDetector` bestand unverändert) – die offene Lücke war nicht die Erkennung selbst, sondern die fehlende Geländeverdeckung. `TerrainMap.HasHeightLineOfSight`
(`Terrain/TerrainMap.cs`) ist eine eigenständige, nicht aus dem Original portierte Sichtlinienprüfung (das Original löst Sichtlinien über `PartitionManager`s
Objekt-zu-Objekt-Tests, die dieses Projekt nicht hat): sie läuft die Gerade zwischen zwei Augenpunkten in Schritten von `CellSize` ab (höchstens `MaxLineOfSightSamples` = 64 Proben;
länger als das wird unterabgetastet – eine benannte, bei den real vorkommenden Sicht-/Erkennungsreichweiten folgenlose Grenze) und vergleicht an jeder Probe die Geländehöhe
(`HeightAt`) gegen die geradlinig interpolierte Höhe der Sichtlinie; die erste Probe, an der das Gelände die Linie übersteigt, blockiert. Das ist reine Verdeckung: eine höher
stehende Beobachterin bekommt keinen Reichweitenbonus, sie wird nur seltener blockiert („sieht über“ niedrigeres Gelände bis zur regulären Reichweitengrenze); nur Geländehöhe zählt,
Gebäude/Bäume/Klippen-als-Hindernis sind keine Geometrie hier. `StealthDetectorUpdate.Update` ruft sie zwischen Detektor und Ziel auf, mit der „Augenhöhe“ = Bodenposition plus volle
`GeometryHeight` des Objekts (kein eigenes Augenhöhenfeld im Katalog – eine benannte Vereinfachung); ohne Terrain (synthetische Fixtures) bleibt das Verhalten exakt wie zuvor (jedes
Ziel in Reichweite ist erreichbar). Die kreisförmige Sichtweiten-/Aufdeckungsberechnung selbst (`VisionMap`, §15) bleibt unverändert ohne Höhenverdeckung – das wäre eine
Schattenwurf-Berechnung pro Zelle und deutlich teurer; nur die Detektor-zu-Ziel-Prüfung (paarweise, so teuer wie der ohnehin vorhandene O(Objekte)-Scan) bekam die Höhenprüfung.

### Fehlende Lese-/Schreib-APIs (Punkt 4, `docs/PRESENTATION-INPUT.md` §10, `docs/GAME-HUD.md`)

Alle fünf in Phase 7/8 (Paket C3) offenen Wünsche sind erledigt, ausnahmslos als kleine, lesende Ergänzungen ohne Protokolländerung:

| API | Datei | Bemerkung |
|---|---|---|
| `SimulationWorld.CheckConstruct(PlayerId, ConstructCommand)` | `World/SimulationWorld.Construction.cs` | öffentlicher Trockenlauf der exakt gleichen privaten Prüfung, die auch `Submit`/`ExecuteCommands` nutzen |
| `ProductionUpdate.Queue` | `Modules/BuiltIn/Phase5Modules.cs` | `ImmutableArray<ProductionQueueItem>`, Kopf zuerst, Vorlage/Kosten/Restticks |
| `SpecialPowerModuleBase.AvailableOnFrame(IWorldServices)` | `Modules/BuiltIn/SpecialPowerModules.cs` | liest bei geteilten Kräften denselben gemeinsamen Bereitschaftsframe wie `IsReady` |
| `UpgradeMuxModule.Executed` | `Modules/BuiltIn/Phase5Modules.cs` | liest den bisher `protected`en `UpgradeMuxState.Executed` |
| `SimulationWorld.IsDock(EntityId)` | `World/SimulationWorld.cs` | ersetzt den Modulnamen-Abgleich (`SupplyCenterDockUpdate` usw.) von außen |

Keine dieser Ergänzungen ändert Snapshot, Replay oder `CommandTypes.Version` – reine Lesezugriffe.

### Skirmish: Start-/Endbarkeit jeder Karte (Punkt 5)

Neues Werkzeug `SimTool skirmish-sweep <content-dir> [--max-ticks N]`: kompiliert den Bestand einmal, läuft dann über jede `.map`-Datei, baut ihr Terrain, und startet bei mindestens
zwei Startpositionen ein kurzes, zweiseitiges `SkirmishMatchScenario` (`FactionAmerica` gegen `FactionAmericaAirForceGeneral`, die ersten zwei aus `ReferenceMatchScenario.Participants`)
mit `--max-ticks` (Standard 3.000) als hartem Budget. „Start- und beendbar“ heißt hier: die Sitzung durchläuft ihren vollen Lebenszyklus (Startobjekte, KI-Denkschritte, Sieg oder
Tick-Budget) ohne Ausnahme – keine Balance- oder Gewinnratenmessung (dafür `ai-match` und die Tabelle in §19).

**Ergebnis (30.09./1.10.2026, `.local/phase2-import-a`, `--max-ticks 3000`):** 150 Karten; 75 ohne jeden Startwegpunkt (Kampagnenkarten, dieselbe Zahl wie `MapTool audit`), 11 mit genau
einem (nicht genug für zwei Spieler); **alle 64 Karten mit mindestens zwei Startpositionen starten und enden sauber** (0 Abstürze; da `--max-ticks` klein gegenüber den in §19 gemessenen
Siegzeiten ist, enden praktisch alle am Tick-Budget statt mit einem Sieger, was für diese Messung irrelevant ist – entscheidend ist der exakte Lebenszyklus ohne Ausnahme). Dauer: 75 s.
Damit ist die seit `docs/HANDOFF-PHASE8.md` offene Abnahme „alle Skirmish-Karten start- und beendbar“ für jede Karte mit genug Startpositionen gemessen und bestanden; Karten ohne
(genug) Startpositionen sind keine Skirmish-Karten (Kampagnenkarten oder Einzelstart-Herausforderungskarten) und kein Fehlbefund.

### Wiederholung der 16-Karten-KI-Übersicht nach dem `SharedReadyFrame`-Fix (§19)

Alle 16 Läufe aus §19s „Wiederholung nach Paket E1“-Tabelle erneut mit denselben Aufrufen (`SimTool ai-match … --max-ticks 45000`, Standardseeds) ausgeführt, da der in §12/§12a
beschriebene `SharedReadyFrame`-Fix nach der letzten Messung dieser Tabelle gelandet war:

| Karte | Paarung | Paket E1 | Phase 10 (jetzt) | Geändert? |
|---|---|---|---|---|
| Alpine Assault | China, GLA | Patt bei 45.000 | Patt bei 45.000 | nein |
| Alpine Assault | America, China | America, Tick 10.575 | America, Tick 10.575 | nein |
| Alpine Assault | GLA, America | GLA, Tick 18.870 | GLA, Tick 18.870 | nein |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 22.155 | China Tank General, Tick 23.325 | **ja** |
| Armored Fury | China, GLA | GLA, Tick 9.240 | GLA, Tick 9.240 | nein |
| Armored Fury | America, China | America, Tick 9.390 | America, Tick 9.390 | nein |
| Armored Fury | GLA, America | GLA, Tick 30.735 | GLA, Tick 30.735 | nein |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 17.430 | China Tank General, Tick 19.020 | **ja** |
| Bitter Winter | China, GLA | China, Tick 32.490 | China, Tick 32.490 | nein |
| Bitter Winter | America, China | America, Tick 19.980 | America, Tick 19.980 | nein |
| Bitter Winter | GLA, America | GLA, Tick 18.240 | GLA, Tick 18.240 | nein |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 34.515 | China Tank General, Tick 27.990 | **ja** |
| Death Valley | China, GLA | China, Tick 39.555 | China, Tick 39.555 | nein |
| Death Valley | America, China | America, Tick 10.155 | America, Tick 10.155 | nein |
| Death Valley | GLA, America | GLA, Tick 16.320 | GLA, Tick 16.320 | nein |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, Tick 19.035 | China Tank General, Tick 15.900 | **ja** |

Jeder der 16 Läufe ist weiterhin mit Lauf 2 und Snapshotfortsetzung identisch. Auffällig und durchgängig: **alle 12 Paarungen ohne `FactionAmericaAirForceGeneral`/`FactionChinaTankGeneral`
sind tickgleich mit der Paket-E1-Tabelle, alle 4 Paarungen mit genau diesen beiden Generälen weichen ab.** Das ist kein Nebeneffekt des `SharedReadyFrame`-Fixes selbst (der schon vor
dieser Messung landete und in §12 bereits mit einem eigenen `ai-match`-Lauf belegt ist), sondern der hier neuen `MaxSimultaneousOfType`-Schranke (oben, Punkt 1): `AirforceGeneral.ini`
und `TankGeneral.ini` tragen beide den Schlüssel für ihre einmaligen General-Einheiten, und `SkirmishAi`s eigene Vorprüfung (`PrerequisitesMet`) kannte diese Schranke nie – vorher
konnte die KI (soweit die Vorlage sonst instanziierbar und erschwinglich war) in der Theorie mehrere Kopien einer eigentlich einmaligen Einheit anfordern, was das Original verbietet;
jetzt wird der zweite Versuch korrekt abgelehnt. Eine abgelehnte Produktion ist für die KI nur ein verlorener Denkschritt (`Backoff`), verschiebt aber nachfolgende Entscheidungen und
damit, über die deterministische Kaskade, den gesamten weiteren Spielverlauf – daher die veränderten, aber weiterhin deterministischen Siegzeiten. Keine Balance-Aussage.

### Skirmish-Speicherstand: Entscheidung (Punkt 6)

**Entscheidung: kein Speichern einer laufenden Skirmish-Sitzung als fortsetzbares Live-Spiel in dieser Phase; `SkirmishMatchSession.CreateSave()` verweigert das laut und dokumentiert,
statt einen stillschweigend unvollständigen Spielstand zu schreiben.**

Zwei Dinge, die ähnlich klingen, aber zu unterscheiden sind:

1. **Ein abgeschlossener oder teilweise aufgezeichneter Lauf speichert und lädt bereits heute exakt:** `ToReplay()` plus ein `WorldSnapshot`-Checkpoint
   (`SkirmishMatchOptions.CheckpointInterval`) ergeben eine tickgleiche Fortsetzung – genau das prüft jeder „snapshot continuation“-Check in `ai-match`/`reference-match` schon
   seit Phase 6/8/9, und braucht keinerlei KI-Zustand, weil ein Replay nur die von der KI bereits erzeugten Befehle erneut ausführt, nie die KI selbst.
2. **Was fehlt, ist das Pausieren einer noch laufenden, nicht vollständig aufgezeichneten Sitzung und ihre Fortsetzung als weiterhin live entscheidende Partie.** Dafür müsste
   `SkirmishAi` selbst den Rundweg überleben: pro Computerspieler hält sie erhebliches, außerhalb von `World` liegendes Erinnerungs- und Ablaufwissen (Feind-/Lagerhaus-Gedächtnis mit
   Zeitstempeln, Auftrags- und Rückzugs-Timer-Tabellen, Produktion-in-Arbeit-Verfolgung, Sammel-/Erkundungsziele, Schlechte-Stellen-Vermeidung, einen eigenen `SageRandom`-Strom – die
   vollständige Feldliste steht in `SkirmishAi.cs`), nichts davon trägt `WorldSnapshot`. Nur die Welt zu schreiben würde beim Laden jede KI mit ausgelöschtem Gedächtnis
   fortsetzen – kein sauberer Neustart, sondern ein leise falscher: die KI „entdeckt“ sofort wieder Feinde und Lagerhäuser, die sie längst kannte, und vergisst laufende Produktion und
   Rückzugs-Zeitsperren, ohne dass irgendetwas einen Fehler meldet.

Ein `SkirmishAiSnapshot`, der Lücke 2 schließt, ist reale, aber begrenzte Fortsetzungsarbeit (ein flacher Zustandsdump, keine neue Mechanik) und bewusst nicht Teil dieser Phase.
Bis es ihn gibt, ist `SkirmishMatchSession.CreateSave()` der eine, dokumentierte, laute Fehlschlagpunkt für ein Live-Sitzungs-Speichern (Test: `Tests.Skirmish.cs` →
`SkirmishSessionRefusesToSave`), statt einer Speicherroutine, die die KI still fallen lässt und einen kaputten Spielstand ausliefert.

### Gemessene Zahlen (1. Oktober 2026, `.local/phase2-import-a`, unverändert gegenüber Phase 9 wo nicht anders vermerkt)

- `SimTool audit-coverage`: 2.115 Objekte, **1.494 instanziierbar (70,6 %) – unverändert**, 621 blockiert, Fingerabdruck `2B9D902AEC46A6DE…` unverändert (keine neue
  Simulationsmodul-Anforderung durch Phase 10).
- `SimTool audit-modules` mit Inhaltsverzeichnis: 19.553 Modulverwendungen, 15.639 abgedeckt (80,0 %) – **unverändert**.
- `IniTool compat-check`: 747 Einträge – **unverändert** (`MaxSimultaneousOfType` ist ein Objekt-Top-Level-Feld, kein Modulfeld, und war nie als `deferred-field` geführt; die neue
  Typisierung ändert daher keine Baseline).
- `SimTool reference-match`: **geändert**, weil `ReferenceMatchScenario`s Zufallsproduktionsauswahl jetzt `CanQueueProductionTemplate` respektiert (vorher konnte sie Vorlagen wählen,
  deren Voraussetzungsgebäude in diesem Szenario nie gebaut werden – das Szenario baut bewusst keine Gebäude außer den Start-Kommandozentralen, §7a) – 13 Fraktionen, 46.284 Befehle
  (0 abgelehnt), Sieger Spieler 10 (`FactionGLA`) bei Tick 17.580, Endhash `EC8267B594CC4D8A`, zwei frische Läufe und eine Snapshotfortsetzung bei Tick 8.940 tickgleich; vorher (Phase
  9) Sieger `FactionAmerica` bei Tick 13.665, `D4779BE413835B6D`. Weder Sieger noch Hash sind ein Qualitätsmerkmal dieses Szenarios (reine Zufallsproduktion ohne Baugebäude);
  entscheidend ist 0 abgelehnte Befehle (die Auswahl ist jetzt wieder vollständig gültig) und die unveränderte Determinismus-/Fortsetzungsgarantie.
- `MapTool audit`: von Phase 10 unberührt (keine Änderung an `CnCNew.Formats`/`SageMapDecoder`/`TerrainMap.Build`/`Classify`; `skirmish-sweep`s 150/150 dekodierte, 0 fehlgeschlagene
  Karten bei identischen 75/11/64-Zahlen bestätigen das indirekt).
- `dotnet build CnCNew.slnx`: weiterhin 0 Fehler, 21 bekannte Warnungen bei sauberem `CnCNew.Client.Stride`-`bin`/`obj`.
- `tests/CnCNew.Simulation.Tests` (synthetisch und `--content-dir`) sowie `tests/CnCNew.Game.Tests` (ohne und mit `--content-dir`): alle bestehen.

## 26. Phase 11: Weltschnittstellen für die Skript-VM

Quelle und Auftrag: `docs/HANDOFF-PHASE11.md` (Skript-Restfamilien); die Skriptseite steht in `docs/SCRIPT-ENGINE.md` §4–§5a. Alle neuen Weltzustände folgen derselben Regel wie
`SimEvent`s Position seit Phase 8: **sie werden nur geschrieben, wenn sie vom Standard abweichen**. Ein Lauf, der keine dieser Schnittstellen benutzt, behält Zustandsform und
Hash von vorher (`SimTool reference-match` weiterhin Sieger FactionGLA bei Tick 19.200, Endhash `28BE35C1174E369F`; `SimTool stress` Endhash `56882F763EABEF67`, vor und nach dieser
Phase gleich – die in früheren Ständen genannte Zahl `ED6F9329F59662E3` stammt aus der Zeit vor Phase 9 und war schon zu Beginn dieser Phase nicht mehr reproduzierbar). Snapshot-,
Replay- und Befehlsversionen bleiben deshalb unverändert (`WorldSnapshot` 7, Replay 7, `MissionSnapshot` 2, `CommandTypes.Version` 10); ein älterer Zustand lädt unverändert, ein neuer
mit benutzten Schnittstellen wäre für eine ältere Version nicht lesbar – bewusst nicht als Versionssprung geführt, weil kein bestehender Lauf betroffen ist (Entscheidung, Review-Gegenstand).

- **Dauerhafte Aufdeckung** (`Terrain/VisionMap.cs`, `World/SimulationWorld.Scripting.cs`): ein dritter Bit je Zelle und Spieler (`PermanentBit`, immer zusammen mit „sichtbar“ und
  „erkundet“); `BeginRefresh` lässt ihn stehen, alles andere wird neu berechnet. `RevealMapPermanently`/`ExploreMap`/`ShroudMap`/`ExploreArea`/`ShroudArea`/`RevealAreaPermanently` setzen
  oder löschen ihn (Quellen: `ScriptActions::doRevealMapEntire`, `doRevealMapEntirePermanently`, `doShroudMapEntire`, `doRevealMapAtWaypoint`, `doShroudMapAtWaypoint`,
  `doRevealMapAtWaypointPermanent`; das Original deckt „einen kurzen Blick“ mit `doShroudReveal` + `undoShroudReveal` im selben Frame auf, das Ergebnis ist „erkundet, nicht sichtbar“, und
  genau das setzen `ExploreMap`/`ExploreArea`). Ein Zustand mit Bit 2 wird beim Laden geprüft (Bit 2 verlangt Bit 0 und 1).
- **Bauschalter** (`PlayerState.CanBuildBase`/`CanBuildUnits`, `SetCanBuild`, `CanBuildStructures`/`CanBuildUnits`): `Player::allowedToBuild` – Strukturen platzieren (`ConstructCommand` →
  `CannotConstruct`) bzw. Einheiten einreihen (`QueueProductionCommand` → `CannotProduceTemplate`) ist gesperrt, solange der Schalter aus ist; kein neuer Ablehnungsgrund, also keine neue
  `CommandTypes.Version`. Serialisiert nur, wenn aus (`canBuildBase`/`canBuildUnits`).
- **Lese-/Schreib-APIs:** `SimulationWorld.WarehouseBoxes(id)` und `SupplyWarehouseDockUpdate.SetCashValue` (`WAREHOUSE_SET_VALUE`: `ceil(cash / ValuePerSupplyBox)` Kisten, wie
  `SupplyWarehouseDockUpdate::setCashValue`), `SpecialPowerInfo.RadiusCursorRadius` (aus `SpecialPower.ini`, für `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST`),
  `CommandCatalog.TryGetButton` (Befehlsart, `Upgrade`, `WeaponSlot` jedes Knopfes; Grundlage der neuen Pfade für Upgrade-/`FIRE_WEAPON`-Knöpfe),
  `ScriptEngine.IsNamedSelected` (Lesezugriff auf die vom Client gemeldete Auswahl).

## 27. Phase 12: Missionsobjekt-Module, Überschreibungs-Beziehungen, Besitzerwechsel

Die Missionen der Kampagne benennen Objekte, die erst mit sieben weiteren Modulen instanziierbar sind. Sie stehen in `Modules/BuiltIn/Phase12Modules.cs` (Schemas in `ModuleCatalog`, Bindungen in
`BuiltInModules`, Test `Phase12ModulesCreateAndBehave`), je Modul mit Quellverweis; was ein Modul der Präsentation überlässt, steht an ihm.

| Modul | Quelle | Verhalten |
| --- | --- | --- |
| `FireWeaponWhenDamagedBehavior` | `FireWeaponWhenDamagedBehavior.cpp` | nach Aktivierung (`StartsActive` oder passendes Upgrade) feuert ein schädigender Treffer eines angenommenen `DamageTypes`-Typs mit mindestens `DamageAmount` die Reaktionswaffe des aktuellen Körperzustands auf das Objekt selbst (`forceFireWeapon(obj, position)`); die Dauerwaffe des Zustands feuert, sooft sie bereit ist; Nachladen = `DelayBetweenShots`, Zustand im Objekt |
| `HordeUpdate` | `HordeUpdate.cpp`, `AIUpdateInterface::evaluateMoraleBonus` | zählt Objekte mit `HordeUpdate` im `Radius` (Verbündete, `KindOf`, ggf. exakte Vorlage, `RubOffRadius`); ab `Count` gilt die Waffenbonus-Bedingung `HORDE` (Bit aus `NameTablesPhase5.WeaponBonusCondition`) |
| `FireWeaponPower` | `FireWeaponPower.cpp` | die Spezialkraft lädt nach und lässt das Objekt `MaxShotsToFire` Schuss seiner Standardwaffe auf Objekt/Ort/sich selbst abgeben, je Schuss `DelayBetweenShots` (ohne Anfahrt/Drehzeit der KI) |
| `BaikonurLaunchPower` | `BaikonurLaunchPower.cpp` | am Zielort entsteht das `DetonationObject` des Kraft-Besitzers (Tod und Waffe des Objekts erledigen den Rest); ohne Ort nur das gemeinsame Nachladen (das Original öffnet nur die Tür); eine nicht erzeugbare Vorlage wird als übersprungene Erzeugung gemeldet |
| `TechBuildingBehavior` | `TechBuildingBehavior.cpp` | ein zerstörtes Technikgebäude wird neutral; `PulseFX` und das Erobert-Modell sind Präsentation (nur die Rate bleibt) |
| `RadiusDecalUpdate` | `RadiusDecalUpdate.cpp` | reine Präsentation (Zielkreis einer Superwaffe), ohne Zustand |
| `FlightDeckBehavior` | `FlightDeckBehavior.cpp` | ehrliche **Hülle**: Felder gebunden, Flugdeck-Betrieb (Start/Landung/Reparatur) nicht simuliert, das Objekt kann existieren (der Träger von `MD_GLA03`, dessen Zerstörung das Missionsziel ist) |

Zustand dieser Module wird nur geschrieben, wenn er vom Anfangswert abweicht (keine Versionssprünge; `WorldSnapshot` 7, Replay 7, `CommandTypes.Version` 10 bleiben). Neu im Katalog: 98 von 205
Modultypen, 1.680 von 2.115 Retail-Objekten instanziierbar (79,4 %), 15.903 von 19.553 Modulverwendungen abgedeckt (81,3 %), Kompatibilitätsbasislinie 765 Einträge (neu gemessen, `baselines/compatibility.json`).

**Überschreibungs-Beziehungen** (`SimulationWorld.RelationshipOverride`): ein Haken `Func<Entity, Entity, Relationship?>`, den `RelationshipBetween` vor der Spielerbeziehung befragt; `AreEnemies(Entity, Entity)`,
die Zielwahl und der Kampf (`SimulationWorld.Combat`) und der Zielerwerb der Einheiten (§33; bis Phase 16 `MissionSession.DriveCombat`) benutzen die Entitätsvariante. Der Haken gehört dem Skript-Motor (`docs/SCRIPT-ENGINE.md` §5, §8 Punkt 19) und ist `null`,
solange keine Überschreibung gesetzt ist – Läufe ohne sie bleiben byteidentisch (Retail-Referenzmatch und Stresstest unverändert).

**`IWorldServices.TransferOwnership`:** die Modulschnittstelle bietet jetzt den Besitzerwechsel an (für `TechBuildingBehavior`; `SimulationWorld.TransferOwnership` aus §22 verschiebt auch die Energieproduktion).
**`StealthUpdate.ScriptUnstealthed`:** Schalter für `NAMED/TEAM_SET_STEALTH_ENABLED`, nur gespeichert, wenn gesetzt.

## 28. Phase 13: Flugzeug-, Fallschirm-, Uplink- und Fahrzeugmodule, Team-Warteschlangen

Phase 13 (`git log b620a50..HEAD`) schließt die Missionsmodule, an denen die Verstärkungs- und Siegketten der Kampagne hingen. Alle Module stehen mit Katalogschema (`ModuleCatalog`), Bindung (`BuiltInModules`),
`Describe`/`Load` (Zustand nur, wenn er vom Anfangswert abweicht) und Test (`Tests.Flight.cs`) im Projekt; was das Original über Physik oder Knochentransformationen löst, ist durch Kinematik ersetzt und am Modul benannt.
Fliegende Lokomotoren fahren weiterhin geradeaus zu einem Zielpunkt (`PreferredHeight`, neu `SpeedLimitZ`).

| Modul | Quelle | Verhalten |
| --- | --- | --- |
| `DeliverPayloadAIUpdate` | `DeliverPayloadAIUpdate.cpp` | Zustandsautomat des Transportflugzeugs: Anflug auf den Lieferpunkt (`DeliveryDistance`, `DoorDelay`, `DropDelay`, `DropOffset`/`DropVariance`, `PutInContainer`), Absetzen der Fracht (Fallschirm über `ParachuteContain`, `IOverrideDestination` für den genauen Ort), `ConsiderNewApproach` (`MaxAttempts`), Verlassen der Karte (`RecoverFromOffMap`, `HeadOffMap`, `ClosestEdgePoint`); stirbt das Flugzeug, stirbt die Besatzung (`TransportContain.OnDie`) |
| `ParachuteContain` | `ParachuteContain.cpp` | der Fallschirm trägt die Fracht: freier Fall bis `OpenDistance`, dann Sinkflug; Landung im Wasser tötet, Fallschaden `FreeFallDamagePercent`; Schwank-/Dämpfungsfelder sind Präsentation |
| `JetSlowDeathBehavior` | `JetSlowDeathBehavior.cpp` | `SlowDeathBehavior` für Flugzeuge: am Boden nur Bodeneffekte und Verschwinden; in der Luft Teilnahme an der Lotterie (`ISlowDeathCandidate`/`SlowDeathLottery`), danach geradeaus weiter mit dem Anteil `FallHowFast` der Schwerkraft, Sekundäreffekte `DelaySecondaryFromInitialDeath`, Aufschlag `FXHitGround`/`OCLHitGround` |
| `CountermeasuresBehavior` | `CountermeasuresBehavior.cpp` | nach dem Upgrade `NumberOfVolleys · VolleySize` Täuschkörper; eine Lenkwaffe auf das Flugzeug (`ICountermeasuresModule.ReportMissile`) wird mit `EvasionRate` markiert (ein Zug des Logikstroms), lenkt nach `MissileDecoyDelay` auf den nächsten Täuschkörper um und richtet dann keinen Schaden an (`DetonateProjectile(damage:false)`); Täuschkörper sind echte Entitäten, kinematisch bewegt |
| `ParticleUplinkCannonUpdate` | `ParticleUplinkCannonUpdate.cpp` | Spezialkraft-Handler (`ISpecialPowerHandler`): Ablauf Ladung/Ausrichtung/Strahl/Abklingen, Strahlzeiten (`WidthGrowTime`, `BeamTravelTime`, `TotalFiringTime`), Schwenk (`SwathOfDeathDistance`/`Amplitude`, Rotationsformel und S-Kurve der Quelle), `TotalDamagePulses` Schadenspulse (`DamagePerSecond`, `DamageRadiusScalar`, Strahlradius `LaserBeamRadius` neu im Vorlagenmodell) entlang des Streifens; `ParticleUplinkCannonUpdate` war der einzige Blocker von 8 Objekten und des Siegziels von `MD_GLA03` |
| `UndeadBody` | `UndeadBody.cpp` | `ActiveBody` mit zwei Leben: tödlicher Schaden lässt einen Trefferpunkt übrig, startet das zweite Leben (`SecondLifeMaxHealth`, volle Gesundheit) und lässt die passenden Langsamtod-Module losen, welches *beginnt* |
| `BattleBusSlowDeathBehavior` | `BattleBusSlowDeathBehavior.cpp` | der erste Tod des Busses: Wurf (`ThrowForce` durch Masse), Landung und Effekte, danach das zweite Leben |
| `SubObjectsUpgrade`, `AnimationSteeringUpdate` | `SubObjectsUpgrade.cpp`, `AnimationSteeringUpdate.cpp` | reine Modellanimation: ehrliche Hüllen mit `WouldUpgrade` bzw. ohne Zustand (das Objekt kann existieren) |

Begleitende Änderungen: `LocomotorInfo.SpeedLimitZ`; `AIUpdateInterface.OrderMove` ist virtuell, `OrderEvacuate(…, thenExit)` für den Rückflug; `IUpgradeModule.WouldUpgrade` (Standardmethode, überschrieben von
den Upgrade-Modulen) liefert die Bereitschaft der Upgrade-Knöpfe (`docs/SCRIPT-ENGINE.md` §8 Punkt 18); `DetonateProjectile(…, damage)`; die Skriptseite (Verstärkung mit Transport, Team-Warteschlangen,
Startaktionen) steht in `docs/SCRIPT-ENGINE.md` §5/§8.

Messung (3. Oktober 2026, `.local/phase2-import-a`; ohne Versionssprung: `WorldSnapshot` 7, Replay 7, `MissionSnapshot` 2, `CommandTypes.Version` 10): Modulkatalog **107 von 205** implementiert (183 Simulations-,
22 Präsentationsmodule), **1.735 von 2.115** Retail-Objekten instanziierbar (82,0 %), Modulverwendungen abgedeckt 16.085 von 19.553 (82,3 %), Kompatibilitätsbasislinie **771** Einträge. Größte verbleibende
Blocker nach Objekten (davon nur durch dieses Modul): `JetAIUpdate` 34/34, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `SlavedUpdate` 20/7, `PropagandaTowerBehavior` 14/13, `UpgradeDie` 13/0,
`HiveStructureBody` 11/4, `RiderChangeContain` 11/11, `DeployStyleAIUpdate` 10/10, `StructureCollapseUpdate` 10/10, `ParkingPlaceBehavior` 9/9. Retail-Referenzmatch: Sieger FactionGLA bei Tick 15.000,
`final hash` (Run 1) `532F19F360C59550` (vorher Tick 19.200, `28BE35C1174E369F`: die neuen Module – am Stand `5416d81` bereits gemessen, vor den Team-Warteschlangen – verändern das Szenario;
Wiederholung und Snapshot-Fortsetzung identisch über 15.301 Ticks, Kettenhash `5B799AA9398CE305`); Stresstest (216.000 Ticks) unverändert `56882F763EABEF67`, Kettenhash `B8E663493F2C73D5`.

## 29. Phase 13, Fortsetzung: Luftlieferungen, Spezialkräfte am Kartenrand, Todeswaffen, Formationen

Der erste Teil von Phase 13 (§28) hat Module für Verstärkungen und die Missionsketten ergänzt. Die Fortsetzung schließt Lücken, die die **Spielbarkeit** der Spezialkräfte betrafen – bis dahin taten alle
Kraftobjekte mit `CREATE_AT_EDGE_*` und `DeliverPayload` nichts (nur `CreationSkipped`-Ereignisse), und `FireWeaponWhenDeadBehavior`/`InstantDeathBehavior` forderten ihre Waffe nur als Ereignis an, das nie ausgeführt wurde.

| Baustein | Quelle | Verhalten |
| --- | --- | --- |
| `DeliverPayload`-Nugget (`PayloadDelivery`, `DeliveryRun`) | `ObjectCreationList.cpp` `DeliverPayloadNugget::create` | `FormationSize` Transporter am Startpunkt (seitlich versetzt, abwechselnd, `FormationSpacing`, `WeaponConvergenceFactor`, `WeaponErrorRadius`, `DelayDeliveryMax`), die `Payload`-Objekte (je optional in `PutInContainer` verpackt) werden mit erzeugt und in den Transporter gesteckt; `SpecialPowerCompletionDie` bekommt den Erzeuger (nur der erste Transporter/die erste Nutzlast). Mit `createOwner=false` (`USE_OWNER_OBJECT`) ist das Objekt selbst der Transporter. Nicht modelliert: `StartAtMaxSpeed` (ein Physik-Stoß), die Zielmarke |
| `OCLSpecialPower` | `OCLSpecialPower.cpp` `doSpecialPowerAtLocation` | die drei Randlagen (`CREATE_AT_EDGE_NEAR_SOURCE`/`_NEAR_TARGET`/`_FARTHEST_FROM_TARGET`: nächster Randpunkt, entfernteste Ecke + 300) und das Ziel als zweiter Punkt der Liste; ohne Terrain weiter `NoTerrainEdge` |
| `DeliverPayloadAIUpdate` (Lauf) | `DeliverPayloadAIUpdate.cpp` | `PreOpenDistance` (nur im Anflug), `FireWeapon` (der Transporter feuert seine Waffe auf Ziel + Versatz, der Insasse wird gelöscht), sichtbare Nutzlast (`VisiblePayloadTemplateName`: Raketen werden mit `VisiblePayloadWeaponTemplate` auf das Ziel gestartet, anderes fällt), `ParachuteDirectly`, `SelfDestructObject`, Tauchflug mit Beschuss (`DiveStart/EndDistance`, Strafen mit der Standardwaffe), Startverzögerung; der gesamte Lauf steht im Zustand |
| `PhysicsBehavior` (Fall) | `PhysicsUpdate.cpp` | ein in der Luft losgelassenes Objekt (Bombe) fällt mit der Schwerkraft, landet und ruht (`KillWhenRestingOnGround` tötet); `HeightDieUpdate` beendet es meist einen Tick früher |
| Todeswaffen | `WeaponStore::createAndFireTempWeapon` | `FireWeaponWhenDeadBehavior`/`InstantDeathBehavior` feuern ihre Waffe am eigenen Ort (`IWorldServices.FireTempWeapon`); das Ereignis bleibt für die Präsentation |
| `FireWeapon`-Nugget | `FireWeaponNugget::create` | die Quelle feuert die Waffe einmal auf den zweiten Punkt; `Attack`, `CreateDebris`, `ApplyRandomForce` bleiben `NuggetNotSimulated` (Effekte/Physik ohne Spielwirkung bzw. Zielbefehl ohne Verbraucher) |
| `ParticleUplinkCannonUpdate` (Wegpunktmodus) | `ParticleUplinkCannonUpdate.cpp` `initiateIntentToDoSpecialPower(way)` | ein Skript (`NAMED_USE_COMMANDBUTTON_ABILITY_USING_WAYPOINT_PATH`) bietet dem Modul den Weg ab dem nächsten Wegpunkt an (`PrepareScriptedPath`); der Strahl fährt mit `ManualFastDrivingSpeed` von Wegpunkt zu Wegpunkt und bleibt am Ende stehen (die Weggabelung wird der Reihe nach, nicht zufällig gewählt) |
| Formationen | `AIStates.cpp` `AIFollowWaypointPathState` (`m_moveAsGroup`), `AIGroup::getSpeed` | `TEAM_FOLLOW_WAYPOINTS(_EXACT)` mit `asTeam`: jedes Mitglied behält seinen Versatz zur Gruppenmitte, alle folgen einem Teamwegpunkt (das erste Mitglied am Ziel schaltet weiter, die anderen wechseln), niemand ist schneller als das langsamste Mitglied (`AIUpdateInterface.SpeedCap`, nur bei Gruppenmarsch gesetzt) |
| Welthaken | `ScriptEngine::adjustToppleDirection`, `GameLogic::setBuildableStatusOverride` | `SimulationWorld.ToppleDirectionOverride`/`BuildableOverride` (Skript-Tabellen, in deren Zustand), `BuildableOf(template)` in Produktion, Bau und `SkirmishAi` |

Messung (3. Oktober 2026): Retail-Referenzmatch und Stresstest ändern sich durch diese Bausteine nicht (das Szenario hat keine Todeswaffen mit Splash, keine Spezialkräfte und keine Gruppenmärsche; gemessen `532F19F360C59550` bzw. `56882F763EABEF67`);
`CampaignTool drive` weiter Sieg und Niederlage 24/24; Test `FlightStrikePowerDeliversBombs` (zwei Bomber, sechs Bomben, Beschädigung des Ziels, Abschlussmeldung, Wiederaufnahme aus dem Snapshot), `FlightUplinkFollowsAScriptedWaypointPath`,
`ScriptTeamFollowsAPathAsAFormation`.

## 30. Phase 14: Luft-, Kisten-/Spezial- und Struktur-/Weltmodule

Drei unabhängig entwickelte Modulgruppen (je ein eigener Worktree, danach zusammengeführt) schließen die Blocker, die `audit-coverage` nach Phase 13 zuoberst führte. Alle Module haben Katalogschema
(`ModuleCatalog`), Bindung (`BuiltInModules`), `Describe`/`Load` (Zustand nur, wenn er vom Anfangswert abweicht) und Tests; was das Original über Physik, Modellknochen oder Darstellung löst, ist am Modul als
Näherung benannt. Beim Zusammenführen wurden zwei parallel entstandene Kollisionsschnittstellen auf **eine** vereinheitlicht (`ICollideModule` mit `WouldLikeToCollideWith`, `Entity.CollideModules`,
`SimulationWorld.DeliverCollisions` der Kistengruppe; die Verteilung der Strukturgruppe entfiel), und `SpawnExtras`/`SpawnSetup` tragen jetzt `Enslaver` und `Producer` nebeneinander (Snapshot-Felder
`enslaver`/`producer`, nur wenn gesetzt).

### 30.1 Luftgruppe (`AirModules.cs`, `JetAIUpdate.cs`, `AirWeaponModules.cs`)

| Modul | Quelle | Verhalten |
| --- | --- | --- |
| `ParkingPlaceBehavior` | `ParkingPlaceBehavior.cpp` | Flugplatz: `NumRows·NumCols` Stellplätze (reserveSpace/releaseSpace, ein Flugzeug behält seinen Platz bis zum Tod oder zur Landung anderswo), eine Piste je Spalte mit genau einem Wartenden (`reserveRunway`, `transferRunwayReservationToNextInLineForTakeoff`, `releaseRunway`), Heilung geparkter Flugzeuge (`HealAmountPerSecond`, alle 6 Ticks), Produktionsausgang (`IProductionExit`: Hangarposition, Hubschrauber am Helipad ohne Platz), Zerstörung tötet alles Geparkte/Startende/Landende; **Geometrie berechnet statt Modellknochen** (Platz-, Rollweg-, Pisten- und Anflugpunkte aus Gebäudeposition, -ausrichtung und Geometrieradien), `defectAllParkedUnits` und Flugdecks nicht modelliert |
| `JetAIUpdate` | `JetAIUpdate.cpp` | Zustandsautomat der Flugzeuge: Hangar→Stellplatz→Ausrichten→Munition laden; Befehl an Geparkten wird gespeichert und löst Piste-Reservieren→Rollen→Pause (`TakeoffPause`, Wartezeit hinter dem Vordermann)→Start (Auftrieb mit dem Quadrat des Pistenanteils)→gespeicherter Befehl aus; Rückkehr bei leerem `RETURN_TO_BASE`-Magazin (Rückkehr-Lokomotor) oder nach `ReturnToBaseIdleTime`, Anflugpunkt, Landeerlaubnis, Gleitflug mit Mindestgeschwindigkeit, Ausrollen, Rollen zum Platz, Gegenmaßnahmen nachladen, Waffe nachladen (anteilig `ClipReloadTime`); ohne Flugplatz kreist der Jet über der Heimatposition und nimmt `OutOfAmmoDamagePerSecond`; Hubschrauber starten/landen senkrecht am Helipad; Lokomotor-Sätze `SET_TAXIING`/Angriff/Rückkehr; kinematisch statt Physik und Wegsuche |
| `SpectreGunshipUpdate` | `SpectreGunshipUpdate.cpp` | Gunship: Anflug auf einen Kreis um das Ziel (Einflugsteigung 0,5–0,8), Transit-Lokomotor bis zum Orbit, Gattling erst im Orbit frei, alle `HowitzerFiringRate` Ticks Zielsuche im Fadenkreuz (Computerspieler auch im Angriffsgebiet), Gattling-Zielpunkt wandert mit `StrafingIncrement`, Haubitze nach `HowitzerFollowLag` mit Zufallsversatz, Abflug nach `OrbitTime`, Gattling wird mit aufgeräumt; der Träger erzeugt das Gunship auf Karten mit Gelände am Kartenrand (`SpectreGunshipDeploymentUpdate`) |
| `MissileLauncherBuildingUpdate` | `MissileLauncherBuildingUpdate.cpp` | Silotür folgt dem Nachladen der Superwaffe: öffnet `DoorOpenTime` vor Bereitschaft, offen bei Bereitschaft, nach Einsatz warten, schließen (höchstens halbe Restzeit), je Zustand eine Effektliste; reine Zustandsfolge ohne eigene Spielwirkung |
| `NeutronMissileUpdate` | `NeutronMissileUpdate.cpp` | Flug von Hand: Start senkrecht, `DistanceToTravelBeforeTurning`, Wendung begrenzt durch `MaxTurnRate` (ohne Arkusfunktionen) zum Punkt über dem Ziel, dann Sturz mit halber Geschwindigkeit, Beschleunigung `RelativeSpeed` minus `ForwardDamping`, Sonderhöhenkurve für `SpecialSpeedTime`; schlägt am Boden ein (`kill`, damit die Todesmodule laufen); ohne Zittern, Abgas, Decal |
| `NeutronMissileSlowDeathBehavior` | `NeutronMissileSlowDeathUpdate.cpp` | `SlowDeathBehavior` mit bis zu neun Explosionsstufen: nach (unaufgerundeter) Verzögerung Umkippen und Schaden im Außenradius (voll im Innenradius, linear fallend, mindestens `MinDamage`, EXPLOSION/EXPLODED nach Rüstung), Boden-FX bei Aktivierung; Verbrennung/Brandfleck nur Darstellung |
| `NeutronBlastBehavior` | `NeutonBlastBehavior.cpp` | Beim Tod des Geschosses im `BlastRadius`: Infanterie und Insassen sterben, Kletterräder sterben, übrige Fahrzeuge (keine Drohnen) werden herrenlos (neutral, gestoppt); Luftziele nur mit `AffectAirborne`, Verbündete nur mit `AffectAllies` |
| `EMPUpdate` | `EMPUpdate.cpp` | EMP-Impuls: nach `StartFadeTime` Wirkung im `EffectRadius` (Standard 200): Infanterie unberührt, fliegende nicht gehärtete Flugzeuge sterben, Gebäude nur Fraktionsgebäude, sonst `DisabledDuration` lang gelähmt (`Entity.EmpDisabledUntil`: keine Bewegung, keine Waffe), `DoesNotAffect=ALLIES` und eigene Gebäude (`DoesNotAffectMyOwnBuildings`) ausgenommen; stirbt nach `Lifetime`; zwei Zufallsziehungen bei der Erzeugung |
| `LeafletDropBehavior` | `EMPUpdate.cpp` | Flugblattbombe: beim Tod lähmt sie feindliche Infanterie und Fahrzeuge im `AffectRadius` (Standard 60) für `DisabledDuration`; die Update-Funktion schläft wie im Original nach dem ersten Tick, solange die Verzögerung nicht abgelaufen ist |
| `BunkerBusterBehavior` | `BunkerBusterBehavior.cpp` | Beim Tod der Bombe (mit `UpgradeRequired` des Besitzers): Insassen des anvisierten Garnison-/Tunnelcontainers werden mit der Insassenwaffe (100 Schaden) herausgezwungen und getötet bzw. sterben, Detonations-FX am Ziel, Schockwellenwaffe dort; Absturz-FX alle `CrashThroughBunkerFXFrequency` Ticks im Kill-Self-Warten; Seismik nur Darstellung |

Begleitende Änderungen: `AIUpdateInterface.IsIdle`/`OrderEnter` virtuell, `ActiveLocomotor` virtuell, `OrderIdle` neu, `Update` ruht unter EMP; `IProductionExit` mit schablonenbewussten Standardmethoden
(`IsFreeToExitFor`, `ClaimExit(world, template)`); `ObjectTemplate.LocomotorSets`, `LocomotorInfo.Lift`, `WeaponInfo.ReloadsAtBase` (ein trockenes `RETURN_TO_BASE`-Magazin lädt nur am Flugplatz nach);
`Entity.EmpDisabledUntil`; `MissileAIUpdate.Victim`/`IsKillingSelf`. **Grenzen:** ~~die Atomraketen starten noch nicht aus dem Silo~~ und ~~ein Jet mit gespeichertem Befehl erscheint dem Skript als „untätig“~~ sind seit Phase 15 **widerlegt** (Echtdatentests: der Start über `UseSpecialPowerCommand` und `OCLSpecialPower` beginnt am Silo und tötet das Ziel; der `IsIdle`-Override des Jets wird über den Basistyp `AIUpdateInterface` gesehen, den die Skripte benutzen); Deaktivierung wirkt nur auf Bewegung und Waffen;
nicht modelliert: Überlaufen eroberter Flugplätze, Trägerdecks, Nebel des Krieges für das Gunship, die Patch-1.01-Einschränkung des EMP (braucht eine Erzeugerverknüpfung).

### 30.2 Kisten-, Kollisions- und Spezialgruppe (`CrateModules.cs`, `SpecialsModules.cs`, `CleanupAndSpyModules.cs`, `BattlePlanModule.cs`)

| Modul | Quelle | Verhalten |
| --- | --- | --- |
| `MoneyCrateCollide` | `CrateCollide.cpp onCollide/isValidToExecute`, `MoneyCrateCollide.cpp executeCrateBehavior` | Überfahrene Kiste zahlt `MoneyProvided` plus den ersten besessenen `UpgradedBoost` an den Spieler des Berührers und verschwindet. Neutrale Berührer, Ziele ohne AI (außer `BuildingPickup`), verbotene Arten, Luftkisten und Fallschirme zahlen nicht. |
| `VeterancyCrateCollide` | `VeterancyCrateCollide.cpp` | Hebt den Berührer, bei `EffectRange` alle eigenen Objekte in Reichweite, um eine Stufe, mit `AddsOwnerVeterancy` um die Stufe des Trägers (Pilot). Pilot nur für eigene Bodenfahrzeuge. Ohne AI und Zielobjekt wird nichts verbraucht, wie im Original (Level-Up-Kisten ohne AI bleiben liegen). |
| `ConvertToCarBombCrateCollide` | `ConvertToCarBombCrateCollide.cpp`, `Object::defect`, `checkAndDetonateBoobyTrap` | Der zum Ziel gesandte Terrorist macht ein Fahrzeug mit `WeaponSet CARBOMB` zur Autobombe (`IS_CARBOMB`, die Welt wählt die CARBOMB-Waffe). Das Fahrzeug läuft zum Terroristen über und erbt dessen Veteranenstufe und Skriptnamen (`ObjectNameTransferred`), der Terrorist ist verbraucht. Eine Sprengfalle am Ziel geht zuerst los. Kein Flugzeug, Boot oder bestehende Autobombe. |
| `ConvertToHijackedVehicleCrateCollide`, `HijackerUpdate` | `ConvertToHijackedVehicleCrateCollide.cpp`, `HijackerUpdate.cpp` | Übernimmt ein feindliches Bodenfahrzeug (nicht `IMMUNE_TO_CAPTURE`, Drohne, beladener Transporter oder schon gekaperter Wagen), setzt `HIJACKED`, gleicht die Veteranenstufe an und beendet dessen Auftrag; der Hijacker versteckt sich im Fahrzeug (ohne `EjectPilotDie` ist er verbraucht), folgt ihm und erscheint, wenn es weg ist, sichtbar und untätig an dessen letzter Position (aus der Luft mit `ParachuteName`-Fallschirm). |
| `SabotageCommandCenterCrateCollide`, `…SuperweaponCrateCollide` | `Sabotage*CrateCollide.cpp` | Ein Saboteur am feindlichen Kommandozentrum startet die Aufladung aller Spezialkräfte neu; ebenso für `FS_SUPERWEAPON`/`FS_STRATEGY_CENTER`. Er muss das Zielobjekt seiner Order sein. |
| `SabotageMilitaryFactoryCrateCollide` | `SabotageMilitaryFactoryCrateCollide.cpp` | Kaserne, Waffenfabrik oder Flugplatz (kein Träger) sind `SabotageDuration` lang deaktiviert; simulierte Wirkung: die Produktionswarteschlange steht (`ProductionUpdate.DisableUntil`). |
| `SabotagePowerPlantCrateCollide` | `SabotagePowerPlantCrateCollide.cpp`, `Energy::setPowerSabotagedTillFrame` | Setzt den „Strom sabotiert“-Tick des Feindspielers (`SabotagePowerDuration`). Ohne Verbraucher in der Simulation (kein Unterstromzustand), nur Zustand. |
| `SabotageSupplyCenterCrateCollide` | `SabotageSupplyCenterCrateCollide.cpp` | Bucht bis zu `StealCashAmount` vom Konto des Feindes auf das des Saboteurs, begrenzt auf dessen Guthaben. |
| `PilotFindVehicleUpdate` | `PilotFindVehicleUpdate.cpp` | Ein untätiger KI-Pilot sucht alle `ScanRate` das nächste gesunde (`MinHealth`) eigene Fahrzeug in `ScanRange`, das sein Kollisionsmodul annimmt, und läuft hin (`OrderApproachGoal`). Menschliche Spieler: schläft. Der einmalige Rückweg zur Basis fehlt (keine Basis-Mitte). |
| `StickyBombUpdate` | `StickyBombUpdate.cpp` (`initStickyBomb`, `update`, `detonate`), `Object::checkAndDetonateBoobyTrap` | Haftet an einem Opfer (folgt ihm, `OffsetZ`), Sprengfalle setzt `BOOBY_TRAPPED`. `detonate` schadet mit `GeometryBasedDamageWeapon` (beide Radien plus Opferumkreis, ohne Beziehungsfilter), löscht den Status und tötet die Bombe. Stirbt das Opfer, geht die Falle los; Verbündete lösen sie nicht aus. Die Skriptaktionen `NAMED_/TEAM_SET_BOOBYTRAPPED` setzen sie an einen Zufallspunkt des Opferumrisses (§30.4). |
| `GrantStealthBehavior` | `GrantStealthBehavior.cpp`, `StealthUpdate::receiveGrant` | Der Radius wächst je Tick um `RadiusGrowRate`; jedes lebende verbündete Objekt der `KindOf` mit `StealthUpdate` erhält dauerhaft Tarnung. Am `FinalRadius` verschwindet der Marker. |
| `WanderAIUpdate` | `WanderAIUpdate.cpp` | Läuft ein untätiges Objekt zu einem Punkt 5 bis 50 weiter in x und y (zwei Züge aus dem Logik-Strom), danach normales AI-Update; schläft nie. |
| `ProjectileStreamUpdate` | `ProjectileStreamUpdate.cpp` | Ringpuffer (20) der Projektile einer Sprühwaffe mit Lücke bei Zielwechsel, Abschneiden vorn, Selbstzerstörung ohne Projektile und Besitzer, `Points` für die Zeichnung. Ohne Spielwirkung; die Waffe erzeugt noch keine Stream-Objekte (`ProjectileStreamName` ist untypisiert). |
| `CleanupHazardUpdate`, `CleanupAreaPower` | `CleanupHazardUpdate.cpp`, `CleanupAreaPower.cpp` | Sucht alle `ScanRate` das nächste `CLEANUP_HAZARD`-Objekt in `ScanRange`, läuft in Waffenreichweite und feuert, bis es weg ist; im Flächenmodus (die Spezialkraft ruft `setCleanupAreaParameters`) wird um den Punkt gesucht, bis nichts bleibt; ein Spieler- oder Skriptbefehl bricht ihn ab. Nur Slot PRIMARY. |
| `SpyVisionUpdate`, `SpyVisionSpecialPower` | `SpyVisionUpdate.cpp`, `SpyVisionSpecialPower.cpp` | Solange aktiv, sieht der Besitzer durch die Augen jedes feindlichen Objekts der `SpyOnKindof` (Referenzzähler je Objekt, in der Sichtberechnung aufgedeckt). Schaltet sich per Upgrade, Spezialkraft (`BaseDuration` plus `BonusDurationPerCaptured`, bei einem Container auf `MaxDuration` gekappt) oder selbstgetaktet ein und aus, beim Entfernen ab. |
| `AssistedTargetingUpdate` | `AssistedTargetingUpdate.cpp`, `Weapon::processRequestAssistance` | Feuert eine Nachbarwaffe mit `RequestAssistRange` auf ein Opfer, greifen alle freien gleichartigen Objekte in Reichweite mit ihrer Slot-Waffe `AssistingClipSize` Schuss an. Feedback-Laser fehlen (Darstellung). |
| `BattlePlanUpdate` | `BattlePlanUpdate.cpp`, `Player::changeBattlePlan`, `applyBattlePlanBonusesForObject` | Zyklus Idle, Unpacking, Active, Packing mit den Animationszeiten. Aktiv: `BATTLEPLAN_*`-Waffenbonus für alle passenden Objekte des Spielers (auch neue und übernommene), Hold the Line halbiert (`HoldTheLinePlanArmorDamageScalar`) den Schaden, Search and Destroy weitet die Aufdeckung. Die Zentrale verdoppelt je nach Plan Lebenspunkte beziehungsweise Sicht und erkennt Tarnung. Beim Packen sind die Truppen `BattlePlanChangeParalyzeTime` gelähmt. Der Plan kommt über `SelectPlan` oder `Initiate` (die Wahl steht in `target.X`: 1 Bombardement, 2 Linie halten, 3 Suchen und Zerstören – ein typisiertes Optionsfeld mit Versionssprung der Befehle fehlt). |

Schnittstellen: Hijack-, Autobomben-, Sabotage- und Pilotenaufträge laufen über `AIUpdateInterface.OrderApproachGoal(world, goal, fromPlayer)` (kein neuer `SimCommand`); neue Ereignisse `CrateCollected` 80,
`ObjectConverted` 81, `Sabotaged` 82, `ObjectNameTransferred` 83 (das Skript nimmt den Namen mit, §30.4). **Näherungen:** Kollisionen feuern nur für Bewegende (wer auf einer Kiste steht, sammelt sie erst beim
nächsten Zug); Hijack und Autobombe übernehmen die Sichtweite nicht; Pilot kehrt nicht zur Basis zurück; Spionagesicht wechselt bei Eroberung nicht den Besitzer; Kampfplan (seit Phase 15 mit typisiertem Feld `UseSpecialPowerCommand.Option` = `OPTION_ONE/TWO/THREE` des Befehlsknopfs, `CommandTypes.Version` 12, Replay 9) skaliert nur die Aufdeckungsreichweite
(nicht die Sichtweite je Einheit), das Bombardement-Geschütz gilt als immer zentriert; Panzerung/Sicht-Skalare werden abgeleitet statt multipliziert, um Festkomma-Drift zu vermeiden.

### 30.3 Struktur- und Weltgruppe (`StructureModules.cs`, `RailroadModules.cs`, `MineModules.cs`, `BridgeModules.cs`, `WorldEffectModules.cs`)

| Modul | Quelle | Verhalten |
| --- | --- | --- |
| `RailroadBehavior` | `RailroadGuideAIUpdate.cpp` (`update`, `loadTrackData`, `createCarriages`, `getPulled`, `updatePositionTrackDistance`, `FindPosByPathDistance`, `onCollide`) | Lokomotive sucht den nächsten Kartenwegpunkt als Anker, die Wegpunktverknüpfungen (`getLink(0)`) sind das Gleis (Schleife erkannt). Beschleunigt `(v+0,02)·Acceleration` bis `SpeedMax`, bremst hinter `…Station`/`…Disembark`/`…PingPong`-Punkten, wartet `WaitAtStationTime` (`SET_TRAIN_HELD` hält sie dort). Waggons aus `CarriageTemplateName` oder unverbundene verbündete Waggons hinter dem Haken; ein Waggon ohne Zieher wird Führungswagen und rollt aus (`Friction`). Offene Strecke: Wagen in den Kulissen/Streckenende (destroy). Lok tötet ab `KillSpeedMin` (sonst Quetschschaden), Zug gegen Zug beide, Basisstrukturen/Demofallen sterben, Objekte mit Physik werden verschoben. Nicht: Audio, Pfadsperre am Bahnhof, Physikimpuls |
| `UpgradeDie` | `UpgradeDie.cpp` | Beim Tod verliert der Erzeuger (`getProducerID`, von der Erzeugungsliste eingetragen) das Objekt-Upgrade `UpgradeToRemove`; ohne Erzeuger nichts |
| `StructureCollapseUpdate` | `StructureCollapseUpdate.cpp` | Nach `onDie` zufällige Verzögerung, dann Einsturz mit `COLLAPSE`-Beschleunigung (Gravitation·(1−`CollapseDamping`)) bis unter die Geometriehöhe; Phasen INITIAL/DELAY/BURST/FINAL wählen je eine FX-/OCL-Liste (eine Logikziehung je Liste), Bursts alle `MinBurstDelay..MaxBurstDelay` (groß mit 1/`BigBurstFrequency`). Zittern und Absenken nur Modell |
| `DeployStyleAIUpdate` | `DeployStyleAIUpdate.cpp` | Zustände READY_TO_MOVE/DEPLOY/READY_TO_ATTACK/UNDEPLOY: ein Feuerbefehl in Reichweite startet das Aufbauen (`UnpackTime`, Status `DEPLOYED`), Schüsse bis dahin verworfen und gezählt (`WeaponNotReady`); ein Bewegungsbefehl packt ein (`PackTime`) und fährt danach; Umkehr eines laufenden Auf-/Abbaus. Das Tor (`IFireGate`) wird erst bei der Ausführung eines akzeptierten Feuerbefehls gefragt, nie bei der Prüfung (sonst änderte ein verworfener Befehl den Zustand). Nicht: Bewacher-Autoentfaltung, Turmzentrierung |
| `FireWeaponCollide` | `FireWeaponCollide.cpp` | Berührt etwas das Objekt, solange alle `RequiredStatus` (z. B. `AFLAME`) und kein `ForbiddenStatus` gesetzt sind, feuert die Kollisionswaffe jedes Mal auf den Berührer; `FireOnce` wirkt wie im Original nicht |
| `FireSpreadUpdate` | `FireSpreadUpdate.cpp`, `FlammableUpdate::tryToIgnite` | Brennt das Objekt, erzeugt es Glut (`OCLEmbers`) und entzündet das nächste noch entzündbare Objekt in `SpreadTryRange` (3D), danach Schlaf `MinSpreadDelay..MaxSpreadDelay` |
| `BridgeBehavior`, `BridgeTowerBehavior`, `BridgeScaffoldBehavior` | `BridgeBehavior.cpp`, `BridgeTowerBehavior.cpp`, `BridgeScaffoldBehavior.cpp` | Schaden/Heilung an die vier Türme im gleichen Anteil (nicht bei Turmquelle); Tod tötet Türme und Objekte ohne Physik auf dem Deck (eigene Box), `BridgeDieFX/OCL` je genau einmal nach ihrem Delay; Heilung aus Rubble belebt; ein Turm gibt Schaden an die anderen Türme und die Brücke weiter, sein Tod tötet die Brücke; das Gerüst bewegt sich RISE → BUILD_ACROSS bzw. TEAR_DOWN_ACROSS → SINK (zerstört) mit Verlangsamung im letzten Viertel, angetrieben nur von außen. Türme per `SetTower`; kein Brückenterrain, keine Layer, kein Gerüstbau |
| `DemoTrapUpdate` | `DemoTrapUpdate.cpp` | Läuft ein Feind (nicht in der Luft, nicht ignorierte Art) in `TriggerDetonationRange`, feuert es `DetonationWeapon` (falls gesetzt) und tötet sich; Verbündete blockieren außer bei `AutoDetonationWithFriendsInvolved`; Modus (Nähe/manuell/Zündung) als Modulzustand statt Waffenslot (kein Befehl verdrahtet); `DetonateWhenKilled` |
| `MinefieldBehavior` | `MinefieldBehavior.cpp` | `NumVirtualMines`: Berührung (nach `DetonatedBy`, Arbeiter nur mit `WorkersDetonate`) löst eine virtuelle Mine am nächsten Footprintpunkt aus (Waffe, Zähler, Gesundheit auf den Anteil, mind. 0,1, `CreationList`), erneut erst nach `RepeatDetonateMoveThresh`; Schaden/Heilung gleichen Zähler und Gesundheit ab; ohne Erzeuger Degeneration. Nicht: Minenräumer-Immunität, Scoot |
| `OCLUpdate` | `OCLUpdate.cpp` | Erzeugt alle `MinDelay..MaxDelay` seine Liste (am Objekt oder Kartenrand), erster Versuch startet nur den Timer; `FactionTriggered`: ruht neutral, Liste nach `PlayerTemplate`-Side des Besitzers |
| `DynamicShroudClearingRangeUpdate` | `DynamicShroudClearingRangeUpdate.cpp` | Sichtweite wächst von 0 auf die Vorlage, hält, schrumpft auf `FinalVision` (Countdown `ShrinkDelay+ShrinkTime`), alle `ChangeInterval` an die Sichtkarte übergeben (`IShroudRangeProvider`); Decals Präsentation |
| `FireOCLAfterWeaponCooldownUpdate` | `FireOCLAfterWeaponCooldownUpdate.cpp` | Zählt Schüsse in Folge der überwachten Waffe, erzeugt nach dem Abklingen/Waffenwechsel bei `MinShotsToCreateOCL` die Liste mit Lebensdauer `OCLLifetimePerSecond`/Deckel; liest seit dem Waffenslot-Modell (§30.6) den echten Slot, auch SECONDARY |
| `FloatUpdate` | `FloatUpdate.cpp` | Aktiviert (`Enabled`) schwimmt das Objekt auf dem Wasserspiegel des Wassergebiets; alle ausgelieferten Nutzungen (Bojen) deaktiviert, Schwanken nur Modell |
| `WaveGuideUpdate`, `DamDie` | `WaveGuideUpdate.cpp`, `DamDie.cpp` | Ab Start deaktiviert; aktiviert und nach `WaveDelay` auf `WaveGuide1` gesetzt, bewegt sich entlang der Wegpunktkette, am Ende zerstört. Je Tick Schaden an allen Punkten der parabolischen Front unterhalb `PreferredHeight` (Brücken ausgenommen), nass, umgeworfen, `DamageAmount` Wasserschaden (Tod FLOODED); `DamDie` aktiviert beim Tod alle `WAVEGUIDE`-Objekte. Nicht: Wasserstand, Uferefekte |
| `CheckpointUpdate`, `EnemyNearUpdate` | `CheckpointUpdate.cpp`, `EnemyNearUpdate.cpp` | Checkpoint: jeden Tick Feind/Verbündeter in Sichtweite, offen bei Verbündetem ohne Feind, Torradius ±1/3 je Tick (nur Zustand, die Wegsuche sieht ihn nicht); EnemyNear: alle `ScanDelayTime` nächster Feind in Sichtweite, Flag `IsEnemyNear` |
| `FirestormDynamicGeometryInfoUpdate` | `DynamicGeometryInfoUpdate.cpp`, `FirestormDynamicGeometryInfoUpdate.cpp` | Nach `InitialDelay` wächst die Geometrie linear in `TransitionTime` (mit `ReverseAtTransitionTime` zurück); alle `DelayBetweenDamageFrames` Flammenschaden (nach Rüstung) an allem im aktuellen Umkreis und nicht über `MaxHeightForDamage` |
| `WeaponBonusUpdate` | `WeaponBonusUpdate.cpp`, `TempWeaponBonusHelper.cpp` | Alle `BonusDelay` erhalten lebende Verbündete (und Passagiere) im Radius mit den geforderten Arten die Bedingung `BonusConditionType` für `BonusDuration`; ein anderer Bonus ersetzt den alten. Zeitgeber in der Quelle (endet mit ihr) |
| `CostModifierUpgrade` | `CostModifierUpgrade.cpp`, `Player::addKindOfProductionCostChange` | Nach dem Upgrade kosten Objekte mit allen `EffectKindOf` für den Besitzer `1+Percentage` (Produkt, ganzzahlig abgerundet; an Produktion und Bau); Tod/Zerstörung nimmt zurück, Eroberung verschiebt zum neuen Besitzer |
| `BoneFXDamage`, `BoneFXUpdate` | `BoneFXDamage.cpp`, `BoneFXUpdate.cpp` | ehrliche Hüllen: Effekte an Modellknochen sind Präsentation, kein Zustand; ein ausgefüllter OCL-Slot macht das Objekt nicht instanziierbar |

Begleitende Änderungen: `FieldSchema` lässt bei Referenzfeldern wie das Original nachfolgende Token zu (`UpgradeToRemove = Upgrade_X ModuleTag_13`); `TerrainMap`-Wegpunkte tragen ihre Id und den ersten Link
(Gleis, Wellenführung); neue `IWorldServices`-Mitglieder und Schnittstellen `IProducerReceiver`, `IFireGate`, `IShroudRangeProvider`, `IOwnerChangeReceiver`. **Grenzen:** Brückenterrain, Brückenschichten und
Gerüstbau fehlen (nur Wahrzeichenbrücken, deren Box das Deck ist); `FireOCLAfterWeaponCooldownUpdate` wirkte vor dem Slotmodell (§30.6) nie, weil jede Nutzung SECONDARY beobachtet; `WaveGuideUpdate` hebt kein Wasser; `WeaponBonusUpdate` hält seine Zeitgeber im Beacon.

### 30.4 Skriptseite (`ScriptEngine.Phase13.cs`)

| Aktion / Bedingung | Quelle | Verhalten |
| --- | --- | --- |
| `NAMED_SET_HELD` | `ScriptActions::doNamedSetHeld` | `Entity.ScriptDisableMask` Bit 1 (`DISABLED_HELD`): AI-Update und Produktion ruhen; vorher nur aufgezeichnet |
| `PLAYER_DISABLE_FACTORIES`, `PLAYER_ENABLE_FACTORIES` | `Player::setObjectsEnabled` | Bit 2 (`OBJECT_STATUS_SCRIPT_DISABLED`) für alle bestehenden Objekte der Vorlage des Spielers; die Produktion steht, später gebaute Objekte sind nicht betroffen (wie im Original) |
| `SET_TRAIN_HELD` | `ScriptActions::doNamedSetTrainHeld` | `RailroadBehavior.SetHeld`: die Lokomotive bleibt am Bahnhof |
| `NAMED_SET_EVAC_LEFT_OR_RIGHT` | `GarrisonContain::exitObjectViaDoor` | `GarrisonContain.EvacDisposition` (1 links, 2 rechts): der Insasse erscheint in einer Tasche neben dem Gebäude (`±¼` der Länge, ½–2 Breiten seitlich) und läuft zu einem Punkt zehn Breiten entfernt (drei Logikziehungen) |
| `NAMED_SET_BOOBYTRAPPED`, `TEAM_SET_BOOBYTRAPPED` | `ScriptActions::doNamedSetBoobytrapped`, `GeometryInfo::makeRandomOffsetOnPerimeter` | das Fallenobjekt wird angefordert, am Zufallspunkt des Opferumrisses (Box: Umriss, sonst Winkel; in das Opferkoordinatensystem gedreht) angeheftet, sobald es existiert (`_pendingTraps`, im Zustand) |
| `BRIDGE_BROKEN`, `BRIDGE_REPAIRED` | `TerrainLogic::isBridgeBroken/isBridgeRepaired` | nur für ein Update nach dem Wechsel des Brückenobjekts in oder aus dem Rubble-Schadenszustand wahr (`_bridgeStates`, im Zustand; die erste Auswertung merkt sich den Ausgangszustand) |
| Namenstransfer (`ObjectNameTransferred`) | `ScriptEngine::transferObjectName` | der Skriptname des verbrauchten Objekts (Autobombe, Hijack, Pilot) gehört danach dem Objekt, das übernommen hat |

Nicht umgesetzt (`CampaignTool coverage`; in keiner Kampagnenkarte benutzt, `CampaignTool usage` leer): `SKIRMISH_BUILD_BUILDING`, `SKIRMISH_BUILD_BASE_DEFENSE_FRONT/_FLANK`,
`SKIRMISH_BUILD_STRUCTURE_FRONT/_FLANK`, `SKIRMISH_ATTACK_NEAREST_GROUP_WITH_VALUE`, `SKIRMISH_PERFORM_COMMANDBUTTON_ON_MOST_VALUABLE_OBJECT`, `AI_PLAYER_BUILD_TYPE_NEAREST_TEAM`,
`NAMED_FIRE_WEAPON_FOLLOWING_WAYPOINT_PATH` – die ersten sechs gehören zu `AISkirmishPlayer` (Bauliste, Basisradius, Seiten-`AIData`), die sich die Simulation mit dem `SkirmishAi`-Ersatz nicht teilt.

### 30.5 Die letzten neun Blockermodule (`LastBlockerModules.cs`, `FerryModules.cs`)

| Modul | Quelle | Verhalten |
| --- | --- | --- |
| `SalvageCrateCollide` | `SalvageCrateCollide.cpp` | Nur `SALVAGER` nehmen die Kiste. Reihenfolge wie im Original: `ARMOR_SALVAGER` nimmt immer die nächste Panzerungsstufe, dann `WEAPON_SALVAGER` mit `WeaponChance` die nächste Waffenstufe, dann trainierbare Einheiten mit `LevelChance` eine Veteranenstufe, sonst Geld zwischen `MinMoney` und `MaxMoney`. Zufallszahlen werden nur bei Chancen ungleich 100 % und bei Min ungleich Max gezogen. Stufen liegen an `Entity.CrateWeaponLevel`/`CrateArmorLevel` und wählen das `CRATEUPGRADE_ONE/TWO`-WeaponSet bzw. `CRATE_UPGRADE_ONE/TWO`-ArmorSet. |
| `UnitCrateCollide` | `UnitCrateCollide.cpp` | Der Spieler des Berührers erhält `UnitCount` Objekte `UnitName`, mit der Ausrichtung des Berührers (ein Ring um den Berührer statt der Kollisionssuche). Ein unbekannter oder nicht instanziierbarer Name lässt die Kiste stehen und meldet `CreationSkipped`. |
| `SabotageFakeBuildingCrateCollide` | `SabotageFakeBuilding.cpp` | Ein feindliches `FS_FAKE`-Gebäude erhält unresistenten Schaden in Höhe seiner Maximalgesundheit (`DETONATED`) und zerfällt sofort. |
| `SabotageInternetCenterCrateCollide` | `SabotageInternetCenterCrateCollide.cpp` | Alle Internet-Center des Opfers schalten ihr `SpyVisionUpdate` für `SabotageDuration` ab; Hacker im getroffenen Center stehen für die Dauer still (`HackInternetAIUpdate.DisableUntil`, kein Geld). Die Markierung `DISABLED_HACKED` am Center selbst wird nicht gespeichert. |
| `SmartBombTargetHomingUpdate` | `SmartBombTargetHomingUpdate.cpp` | Nach `SetTargetPosition` (von `DeliverPayloadAIUpdate` beim Abwurf) wandert x/y jeden Tick im Flug auf `Ziel·(1−s)+Position·s` mit `s` = `CourseCorrectionScalar` auf [0,1] geklemmt. Die Höhe bleibt dem Fall überlassen, der Nullpunkt wird abgelehnt. |
| `SupplyWarehouseCripplingBehavior` | `SupplyWarehouseCripplingBehavior.cpp` | Beim Übergang in `REALLYDAMAGED` wird das Dock gelähmt (`SetDockCrippled`): Anfahrt ja, Einlass nie, ein Lkw zwischen Eintritt und Austritt stirbt, ein nur angefahrener wird idle gesetzt mit erzwungenem Versorgungswunsch. Jeder Treffer startet die Heilungsruhe `SelfHealSupression`, danach heilt es `SelfHealAmount` je `SelfHealDelay`. Ein Objekt ohne Dock wird nicht gelähmt (das Original würde Null dereferenzieren). |
| `RailedTransportAIUpdate` | `RailedTransportAIUpdate.cpp` | Die Fähre fährt Wegpunktpfade `<Präfix>Start%02d`/`End%02d` (bis 32) Hop für Hop ab, wählt anfangs den Pfad mit dem nächsten Endpunkt und ist unterwegs „in transit“ (Dock zu). Innerhalb 5 Einheiten vom Endpunkt oder im Leerlauf öffnet das Dock. `ExecuteRailedTransport` nimmt den nächsten Pfad reihum (kein Befehlstyp – das würde das Replayformat ändern), `OrderEvacuate` entlädt über das Dock. Spielerbefehle außer Evakuieren werden ignoriert. Keine Karte des Retailbestands enthält die Fähre. |
| `RailedTransportContain` | `RailedTransportContain.cpp` | Ein `TransportContain`: nach dem letzten Entfernen öffnet sich das Dock; Austritt nur bei offenem Dock (sonst `NotPermitted`); ein Austrittsbefehl übergibt an das Dock (`unloadSingleObject`, entlädt den ersten der Liste). |
| `RailedTransportDockUpdate` | `RailedTransportDockUpdate.cpp` | Ein Docker innerhalb `ToleranceDistance` wird gehalten, nicht wählbar, zur Fähre gedreht und über `PullInsideDuration` bis 6 Einheiten zur Mitte gezogen, dann in die Fähre gesetzt; Entladen holt den ersten Passagier heraus und schiebt ihn über `PushOutsideDuration` zum Ausgangspunkt (bis 3 Einheiten). Ohne Modellknochen: Warteplatz am Rand des Umkreises, Ausgang eine Hauptachse vor der Fähre. |

Damit ist jedes für die Simulation vorgesehene Modul des Katalogs implementiert (183 von 183); offen bleiben nur die 22 Präsentationsmodule.

### 30.6 Das Waffenslot-Modell (`WeaponSets.cs`, `SimulationWorld.WeaponSlots.cs`, `WeaponSlotState.cs`)

Bis hierher feuerte jedes Objekt eine Waffe (`DefaultWeapon`, `PlayerUpgradeWeapon`, `VeteranWeapon` …); `AssistedTargetingUpdate` und `FireOCLAfterWeaponCooldownUpdate` führten ein eigenes Slot-Buchhalten oder wirkten nie. Das Slotmodell
macht die aktive `WeaponSet`-Auswahl zur einzigen Quelle dafür, welche Waffe ein Objekt feuert; die Sonderauswahlen der anderen Gruppen (Autobombe, Kistenstufen) speisen es.

| Baustein | Quelle | Verhalten |
| --- | --- | --- |
| Waffensätze und Slots | `ThingTemplate::findWeaponTemplateSet`, `WeaponSet.cpp` | Jedes `WeaponSet` der Vorlage wird streng geparst (Primär/Sekundär/Tertiär, `AutoChooseSources`, `PreferredAgainst`, `ShareWeaponReloadTime`, `WeaponLockSharedAcrossSets`); `DefaultWeapon` bleibt Alias des Primärslots des Standardsatzes. Nur Objekte mit mehr als einer Waffe tragen Slotzustand (`Entity.WeaponSlots`), Einwaffen-Objekte behalten Form und Snapshot. |
| Clips und Nachladen je Slot | `Weapon.cpp` (`fireWeapon`, `reloadAmmo`) | Eigener Clip, Bereit-Tick und letzter Schuss je Slot. Ein leerer Clip wartet `ClipReloadTime`, sonst `DelayBetweenShots`; `ShareWeaponReloadTime` setzt alle Slots auf denselben Tick; `RETURN_TO_BASE`-Waffen laden nie per Zeit nach (Bereit-Tick `SleepForever`, nur der Flugplatz füllt). |
| Beste-Waffe-Wahl | `WeaponSet::chooseBestWeaponForTarget` (reduziert) | Von Tertiär abwärts geprüft, bei Gleichstand gewinnt der niedrigere Slot; berücksichtigt `AutoChooseSources` je Befehlsquelle (Spieler/Skript/KI), die Anti-Maske gegen das Ziel, den Schaden nach Rüstung, die Reichweite und `PreferredAgainst`; eine bevorzugte Waffe bleibt auch beim Nachladen. Waffen, die das Ziel nicht verletzen können, werden abgelehnt (außer `UNRESISTABLE`). |
| Sperren | `Object::setWeaponLock`, `SetWeaponLockCommand`, `LockWeaponCreate` | Vorübergehend (Befehlsknopf, Spezialfähigkeit, `CleanupHazard`, `AssistedTargeting`): endet beim leeren Clip oder einem Bewegungsbefehl; dauerhaft (`LockWeaponCreate`, `SWITCH_WEAPON`): nur durch Freigabe oder Satzwechsel. `LockWeaponCreate` ist damit kein Hüllenmodul mehr. |
| Satzauswahl nach Bedingungen | `WeaponSetFlagsOf`, `WeaponSetResolver.Find` | Flags Veteran/Elite/Hero, `PLAYER_UPGRADE`, `CRATEUPGRADE_ONE/TWO` (aus `Entity.CrateWeaponLevel`) und `CARBOMB` (aus `IS_CARBOMB`); kombinierte Sätze (`CRATEUPGRADE_ONE PLAYER_UPGRADE`) werden per dünnem Abgleich gefunden. Ein Satzwechsel (`WeaponSetFlagsChanged`) baut die Slots frisch auf (volle Clips, Sperre gelöst, außer geteilt). |
| `AssistedTargetingUpdate`, `CleanupHazardUpdate` | wie §30.2 | feuern über `TryFireWeaponSlot` aus dem echten Slot und sperren ihn vorübergehend; das eigene Clip-Buchhalten entfällt. |
| `FireOCLAfterWeaponCooldownUpdate` | wie §30.3 | beobachtet jetzt den Slot (auch SECONDARY): aktuelle Waffe, letzter Schuss, Bereit-Tick; erzeugt die Liste nach der Serie oder sofort beim Waffenwechsel – im Retailbestand wirkt es damit erstmals. |
| `JetAIUpdate`, `FireWeaponPower`, `SpectreGunship`, EMP, `DeployStyleAIUpdate` | wie §30.1/§30.3 | `isOutOfSpecialReloadAmmo` gilt, wenn alle `RETURN_TO_BASE`-Waffen leer sind; Nachladezeit ist das Maximum über alle Slots; `FireWeaponPower` ruft `ReloadAllAmmo` und feuert die aktuelle Waffe; die Lähmung verzögert alle Slots (`DelayWeaponsUntil`); das Tor von `DeployStyleAIUpdate` läuft nach der Wahl und vor dem Verbrauch. |
| Befehle und Skripte | `FireWeaponCommand`/`SetWeaponLockCommand`, `FIRE_WEAPON`, `SWITCH_WEAPON` | `FireWeaponCommand` trägt einen optionalen Slot und die Befehlsquelle; Skript-Befehlsknöpfe feuern aus dem Knopf-Slot und sperren ihn. **Versionssprünge:** `WorldSnapshot` 7 → 8, Replay 7 → 8, `CommandTypes.Version` 10 → 11. |

**Näherungen:** die Waffenwahl kennt nur `PREFER_MOST_DAMAGE`; Reichweite ist ein Filter ohne Anlaufen; Turm- und Neigungsbedingungen fehlen; die Garnison-Sonderfälle der Schadensschätzung fehlen; `FireWeaponPower` hat keinen
KI-Angriffsbefehl und keinen An-/Abmarsch; `AssistedTargetingUpdate` bleibt ein Modulschuss ohne die Rückmeldelaser; `CleanupHazardUpdate` setzt das Veteranen-Flag beim Erzeugen nicht.

### 30.7 Messung (3. Oktober 2026, `.local/phase2-import-a`, frisch)

- Modulkatalog **183 von 205** implementiert – alle 183 Simulationsmodule (offen nur die 22 Präsentationsmodule); Instanziierbarkeit **2.115 von 2.115 (100,0 %)**; Modulverwendungen abgedeckt **16.558 von 19.553 (84,7 %)** (der Rest sind
  Präsentationsmodule); Kompatibilitätsbasislinie **751** Einträge. Echtdaten: 2.477 Waffensätze auf 2.115 Objekten, 187 Objekte mit Slotzustand.
- Retail-Referenzmatch: Sieger FactionGLA bei **Tick 13.890**, `final hash` (Run 1) `76F22CB0CA79BEEF`, Kettenhash `35F54CAD40A43E6E`; zweiter Lauf und Snapshot-Fortsetzung bei Tick 7.095 identisch (die Werte der Phase 13 – Tick 15.000,
  `532F19F360C59550` – sind überholt: mehr produzierbare Objekte und die Slot-Waffenwahl verändern das Szenario). Stresstest (216.000 Ticks) unverändert `56882F763EABEF67`, Kettenhash `B8E663493F2C73D5`.
- Versionen: `WorldSnapshot` 8, Replay 8, `CommandTypes.Version` 11, `MissionSnapshot` 2.

## 31. Nachtrag Phase 15 (4. Oktober 2026): Turmzustand, gestufte Übernahme, Flügelflug

Drei Verhaltenslücken, die die gestellten Demos (`docs/DEMO-SCENARIOS.md`) aufdeckten, sind geschlossen; die Einzelheiten stehen in eigenen Dokumenten:

- **Turmzustand mit Feuerfreigabe** (`docs/SIM-TURRET-AIM.md`): typisierte `Turret`/`AltTurret`-Blöcke, `TurretState` je Turm, die Waffe eines vom Turm gesteuerten Slots wartet auf die Ausrichtung;
  `WorldSnapshot` 8 → 9. Die Bemerkung „Turm- und Neigungsbedingungen fehlen“ weiter oben ist damit überholt. Referenzmatch jetzt Tick 13.875, Endhash `482FC8C52D507662`, Kette `34AB50A397B0125C`.
  Nachtrag: dauerhafter Angriffszustand (in `TurretState`, `PursueCommand` Typ 31 für die Verfolgung, Replay 10, `CommandTypes.Version` 13) und Idle-Scan (`TurretAI`-Leerlauf, Zufall aus einem eigenen Hash-Strom statt `LogicRandom`) – Referenzmatch jetzt Tick 15.000, `01C01E3B91EAE18C`, Kette `045DA5259A104F01`.
- **Gestufte Gebäudeübernahme** (`docs/SIM-STAGED-CAPTURE.md`): `SpecialAbilityUpdate` führt für `SPECIAL_INFANTRY_CAPTURE_BUILDING` und `SPECIAL_BLACKLOTUS_CAPTURE_BUILDING` Anlaufen, Auspacken, Vorbereiten
  (Flagge), Besitzerwechsel und Einpacken aus; die Erwähnungen von `SpecialAbilityUpdate` als Hülle in §11, §23, §25 und §30 gelten nur noch für die übrigen Fähigkeiten.
- **Flügelflug und Flugplatzgeometrie** (`docs/SIM-JET-FLIGHT.md`): `Appearance = WINGS` fliegt mit Mindestgeschwindigkeit, begrenzter Kurvenrate und kreist bei `CirclingRadius`; die Start- und Landebahn
  des Flugplatzes kommt aus einer eingespeisten Knochentabelle der Flugplatzmodelle (`ModelGeometry`, Teil von `GameData.Fingerprint`).

## 32. Nachtrag Phase 16 (4. Oktober 2026): Feuerstufen, Vorangriff, beschädigte Locomotoren, Versorgungszentrum und Angriffsgruppen der KI, KI-Übersicht neu

Fachdokumente: `docs/SIM-FIRING-TRACKER.md` (Feuerstufen `CONTINUOUS_FIRE_*` als Waffenbonus, `PreAttackDelay`, `SpeedDamaged` und Geschwister, `CaptureFlash`, Fähigkeit beendet Angriff) und
`docs/SIM-TURRET-AIM.md` §8a (Aufschlüsselung der KI-Verschiebung nach Effekten). Außerdem: `StructureToppleUpdate` erreicht seinen Endzustand (Winkel und Winkelgeschwindigkeit mit 32 zusätzlichen
Nachkommabits, `docs/MODEL-CONDITIONS-BUILDINGS.md` §6); `SimTool` und `CampaignTool` bauen den Katalog mit der Modell-Bone-Tabelle der Flugplätze wie der Client (`ModelGeometryLoader`).
Keine neuen Module, keine Versionssprünge (`WorldSnapshot` 9, Replay 10, `CommandTypes.Version` 13, `MissionSnapshot` 2): aller neue Zustand ist optional und wird nur geschrieben, wenn er gesetzt ist.

### Versorgungszentrum der Skirmish-KI (`AIPlayer::buildBySupplies`, `AIPlayer::findSupplyCenter`)

**Befund.** `SimTool ai-match … --trace N` (neu: je N Ticks Geld, Gebäude, Armee, Sammler, Wellen je Spieler, Kisten auf der Karte und die Strecke Versorgungszentrum–nächstes gefülltes Lager) zeigte, warum die
meisten KI-Spiele im Patt endeten: `SkirmishAi` stellte das Versorgungszentrum wie jedes Gebäude auf Ringe um die Basis, 400 bis 660 Einheiten vom nächsten Lager entfernt. Zwei Sammler brachten so rund 15 Kisten
je 100 Sekunden; beide Seiten standen nach wenigen Minuten bei 0 Credits und führten in 45.000 Ticks zwei bis vier Angriffswellen.

**Umsetzung** (`SkirmishAi.FindSupplySource`, `FindSupplyPlacement`):

* Wahl des Lagers wie `findSupplyCenter`: das der Basis nächste gefüllte Lager (Mindestwert 1.000, halbiert bis über 100), das keinem Feind gehört, in dessen Nähe (`SUPPLY_CENTER_CLOSE_DIST` = 20 Zellen plus
  Radien) kein eigenes Versorgungszentrum steht und das nicht deutlich näher am Feind liegt als an der eigenen Basis (60/40-Regel; die Feindbasis ist nur, was der Spieler von ihr gesehen hat). Wie der
  Computerspieler der Referenz betrachtet die Wahl **alle** Lager der Karte, gesehen oder nicht – die einzige Stelle, an der `SkirmishAi` über die eigene Sicht hinaus weiß.
* Platz wie `buildBySupplies`: drei Zellen vom Lager in Richtung Basis; ist der Platz nicht legal, der erste legale auf wachsenden Quadraten darum (je zwei Zellen breiter, bis zur doppelten
  `SUPPLY_CENTER_CLOSE_DIST`), mit Fußabdruck-, Abstands- und Erreichbarkeitsprüfung. Ein unerkundeter Platz kann nicht bebaut werden (`ConstructCommand`): der Bauarbeiter fährt zuerst hin.
* Ohne jedes Lager auf der Karte bleibt es bei den Ringen um die Basis. Das zweite Versorgungszentrum (ab zehn Armee-Einheiten) entsteht am nächsten freien Lager.

**Wirkung** (Alpine Assault, China gegen GLA, `--trace 3000`): Strecke 128 bzw. 107 statt 587 bzw. 393; China gewinnt bei Tick 7.425 statt Patt. Über die 16 Läufe: 10 statt 5 entschieden.

### Die 16 KI-Läufe (§19) neu, 4. Oktober 2026

Gleiche Aufrufe wie in §19 (`--max-ticks 45000`, Standardseeds); jeder Lauf ist mit Lauf 2 und Snapshot-Fortsetzung identisch.

| Karte | Paarung | erste Turmfassung (`8b8c8a7`) | Angriffszustand (`2b854d3`) | + Feuerstufen, Vorangriff | + beschädigte Locomotoren, Versorgungszentrum am Lager | **jetzt** (+ Angriff in Gruppen) | Endhash jetzt |
|---|---|---|---|---|---|---|---|
| Alpine Assault | China, GLA | China, Tick 43.965 | Patt bei 45.000 | Patt bei 45.000 | China, Tick 7.425 | **China, Tick 7.545** | `39344016A5EE8F91` |
| Alpine Assault | America, China | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | America, Tick 15.615 | **America, Tick 16.800** | `745839146322DDE6` |
| Alpine Assault | GLA, America | America, Tick 12.120 | America, Tick 12.120 | America, Tick 12.120 | America, Tick 9.135 | **America, Tick 10.740** | `DDDFE22494F29A34` |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | AmericaAirForceGeneral, Tick 18.120 | **Patt bei 45.000** | `3CD8D8C4D53675C0` |
| Armored Fury | China, GLA | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | **China, Tick 11.460** | `6CAD71B81D3CE47A` |
| Armored Fury | America, China | America, Tick 41.130 | America, Tick 36.375 | Patt bei 45.000 | Patt bei 45.000 | **America, Tick 33.255** | `63486D2572E76BCE` |
| Armored Fury | GLA, America | America, Tick 22.410 | America, Tick 22.410 | America, Tick 22.410 | America, Tick 27.600 | **America, Tick 40.260** | `9801D2537695316C` |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | AmericaAirForceGeneral, Tick 9.405 | AmericaAirForceGeneral, Tick 9.405 | AmericaAirForceGeneral, Tick 9.405 | Patt bei 45.000 | **Patt bei 45.000** | `E5ED792B12D35CC0` |
| Bitter Winter | China, GLA | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | China, Tick 15.435 | **China, Tick 12.930** | `CB890471FCB4B084` |
| Bitter Winter | America, China | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | **Patt bei 45.000** | `0FDEB99B1DEFAAA6` |
| Bitter Winter | GLA, America | America, Tick 14.565 | America, Tick 14.565 | America, Tick 14.565 | America, Tick 10.800 | **America, Tick 14.430** | `A3A6295689F3F0E5` |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | **Patt bei 45.000** | `0EC68A99E3ED5ACD` |
| Death Valley | China, GLA | China, Tick 8.805 | China, Tick 8.805 | China, Tick 8.805 | China, Tick 7.350 | **China, Tick 11.160** | `DA30DBBC2434E499` |
| Death Valley | America, China | Patt bei 45.000 | China, Tick 35.985 | Patt bei 45.000 | America, Tick 16.260 | **China, Tick 9.975** | `8DB4AA743B8F349A` |
| Death Valley | GLA, America | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | America, Tick 10.110 | **America, Tick 13.755** | `2992C9E0876B2C95` |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | Patt bei 45.000 | **AmericaAirForceGeneral, Tick 32.280** | `C443660F229E157E` |

Entschiedene Läufe: 7, 7, 5, 10, **12** von 16. Die frühere Tabelle „Phase 10“ (16 von 16 entschieden) ist seit der Wegsuche um stehende Einheiten (Phase 15) überholt und wurde bis jetzt nicht neu erhoben.
**Angriff in Gruppen.** Nach dem Versorgungszentrum blieben sechs Patts mit anderem Muster: die Kassen waren nicht mehr leer, aber die während eines Angriffs produzierten Einheiten liefen einzeln in die
gegnerische Verteidigung (Armored Fury, China gegen GLA: GLA produzierte 236 Einheiten und zählte eine einzige Welle, weil der Angriff nie endete). Die Referenz schickt Teams; `SkirmishAi.CommandArmy` führt
deshalb jetzt die Welle als Menge der gemeinsam losmarschierten Einheiten: Nachzügler sammeln sich am Sammelpunkt und folgen, sobald sie mindestens die halbe Angriffsschwelle sind; die Welle gilt als
gebrochen, wenn von ihr weniger als ein Drittel der Schwelle (mindestens drei) übrig ist. Verteidigung (`FindThreat`) nimmt weiter alle. Wirkung: 12 statt 10 entschiedene Läufe – drei gewonnen
(Armored Fury China/GLA und America/China, Death Valley Tank/AirForce), einer verloren (Alpine Assault Tank/AirForce). Bei 16 Läufen ist das kein starker Beleg; übernommen wurde es, weil es dem Vorbild näher ist.
Die vier verbleibenden Patts (dreimal ChinaTankGeneral gegen AmericaAirForceGeneral, Bitter Winter America/China) sind Gegenstand von Phase 17. Keine Balance-Aussage. Stand nach dem Zielerwerb der Einheiten (Phase 17): §33.6.

Weitere Referenzwerte (4. Oktober 2026, `SimTool` Release, Welt-Fingerabdruck mit Bone-Tabelle `CC9641FC1D68F326…`):

| Lauf | Ergebnis |
|---|---|
| `reference-match` | Sieger FactionGLA, Tick 13.500, Endhash `0946FB676DEC615C`, Kette `EA6D12F180930D71`; Lauf 2 und Snapshot-Fortsetzung identisch |
| `stress` (216.000 Ticks) | Endhash `56882F763EABEF67`, Kette `B8E663493F2C73D5` (unverändert) |
| `ai-match` Alpine Assault, China/GLA | China, Tick 7.545, Endhash `39344016A5EE8F91`, Kette `FFF843E719A2C2DD` |
| `ai-match` Alpine Assault, AmericaAirForceGeneral/China | Patt, Endhash `E222322067493EB4`, Kette `F6ED83919F0E7C8F` |
| `ai-match` Alpine Assault, America/GLA | GLA, Tick 8.865, Endhash `542BE1D4891FEB71`, Kette `0B244A5DD2471633` |

Der Weg des Referenzspiels über diesen Tag: Tick 15.000 (`01C01E3B91EAE18C`) → 14.490 (`1104C4CD97A0D4CA`; Feuerstufen und Vorangriffsverzögerung) → 13.500 (beschädigte Locomotoren). Das Referenzszenario benutzt
`SkirmishAi` nicht; die Bone-Tabelle der Werkzeuge und die Fähigkeitsregel änderten es nicht.

## 33. Phase 17 (5. Oktober 2026): Zielerwerb der Einheiten und Angriffsmarsch

Bis Phase 16 wählte die Simulation keine Opfer: das taten drei Befehlsgeber (`MissionSession.DriveCombat`, `SkirmishAi.Fire`, `StandingOrders.FireAtNearestEnemy` der Darstellung), jeder mit eigener Regel
und eigenem Takt, und eine marschierende Einheit verlor ihr Ziel bis zum nächsten Denkschritt ihres Befehlsgebers (`docs/SIM-TURRET-AIM.md` §6, §8a). Jetzt erwirbt jede Einheit ihr Opfer selbst, und es gibt
einen Angriffsmarsch als Befehl. Code: `World/SimulationWorld.Acquisition.cs`, `World/AcquisitionState.cs`, `Scripting/ScriptEngine.Acquisition.cs`; Tests: `Tests.Acquisition.cs`.

### 33.1 Daten: `AutoAcquireEnemiesWhenIdle` streng typisiert

Der Schlüssel war als `Bool` typisiert; `INI::parseBitString32` mit `TheAutoAcquireEnemiesNames` (`AIUpdate.cpp` `AIUpdateModuleData::buildFieldParse`, `AIUpdate.h`) liest aber eine Bitliste. Von den Werten des
Retailbestands (Textsuche über `Data/INI`: 271-mal `Yes`, 62-mal `No`, 28-mal `Yes Stealthed`, 17-mal `No NotWhileAttacking`, 11-mal `No ATTACK_BUILDINGS`, 5-mal `Yes Stealthed ATTACK_BUILDINGS`, 2-mal
`No ATTACK_BUILDINGS Stealthed`, je einmal `YES ATTACK_BUILDINGS` und `Yes Stealthed ATTACK_BUILDINGS Stealthed`) ging bei 65 Zeilen alles nach dem ersten Token als „von der Referenz überlesen“ verloren –
was falsch war: die Referenz liest sie.

* `NameTablesPhase5.AutoAcquireEnemies` (`YES`, `STEALTHED`, `NO`, `NOTWHILEATTACKING`, `ATTACK_BUILDINGS`; Bits `AAS_Idle` 0x01 … `AAS_Idle_Attack_Buildings` 0x10), Feldtyp `Flags` in `AIUpdateInterface` und
  allen abgeleiteten AI-Modulen (`ModuleCatalog`, `AiBase`). Ein unbekanntes Token ist ein Fehler, `NONE` und `+`/`-` verhalten sich wie in `parseBitString32`.
* `AIUpdateData.AutoAcquire` (`[Flags] AutoAcquire`, gleiche Bitwerte) und `AIUpdateData.MoodCheckTicks` (`MoodAttackCheckRate`, Standard `LOGICFRAMES_PER_SECOND * 2` = 60 Ticks wie der Konstruktor von
  `AIUpdateModuleData`; im Bestand 50-mal 250 ms, 4-mal 500 ms). `NO` ist ein eigenes Bit, das niemand prüft: „No ATTACK_BUILDINGS“ setzt zwei Bits und erwirbt im Leerlauf trotzdem nichts.
* `baselines/compatibility.json` ist **unverändert** (751 Einträge, `IniTool compat-check` und ein frisch erzeugter `compat-baseline` sind bytegleich mit der Datei): die verlorenen Tokens waren Diagnosen des
  Graphen, die der Sammelzähler der Basislinie nicht führte. Der Realdatentest prüft jetzt, dass keine `compatibility-ignored-tokens`-Diagnose mehr `AutoAcquireEnemiesWhenIdle` nennt.
* Folge: Definitionssnapshot und Welt-Fingerabdruck ändern sich (das Feld ist ein `Flags`-Wert statt eines `Bool`).

### 33.2 Regeln

Die Referenz ruft `AIUpdateInterface::getNextMoodTarget` von drei Stellen: `AIIdleState::update` (ruhende Einheit), `TurretAI::friend_checkForIdleMoodTarget` aus `TurretAIIdleState::update` (ruhender Turm, während der
Körper etwas anderes tut) und `AIAttackMoveToState::update` (Angriffsmarsch). `StepAcquisition` läuft einmal je Tick nach allen Bewegungen und vor `StepTurrets`, über die Objekte mit AI-Modul in Id-Reihenfolge:

| Zustand der Einheit (`AcquireMode`) | Bedingung | Verhalten | Referenz |
|---|---|---|---|
| Leerlauf | `AIUpdateInterface.IsIdle`, Bit `YES` | sucht ein Opfer **in Waffenreichweite**; feuert, solange es in Reichweite bleibt; verlässt es die Reichweite, wird es aufgegeben und zwei Frames später neu gesucht | `AIIdleState::update` → `aiAttackObject(enemy, NO_MAX_SHOTS_LIMIT, CMD_FROM_AI)` |
| Turm | Körper beschäftigt (fährt, baut, dockt), Bit `YES`, Objekt hat einen zielenden Turm (`TurretInfo.Gates`) | wie Leerlauf, aber nur mit Waffen auf einem Turm; die Einheit fährt weiter | `friend_checkForIdleMoodTarget`, `m_targetWasSetByIdleMood` |
| Angriffsmarsch | `AttackMoveCommand` (kein `YES` nötig) | sucht im Marsch; hält für den Kampf an, feuert, sucht nach dem Tod des Opfers sofort das nächste und marschiert ohne Opfer weiter zum Ziel | `AIAttackMoveToState::update` (`forceRetargetThisFrame`, `friend_endingMove`) |
| keiner | sonst (kein `YES`, getarnt ohne `STEALTHED`, deaktiviert) | nichts | `getNextMoodTarget` gibt `NULL` |

* **Takt** (`m_nextMoodCheckTime`): beim Eintritt in einen der Zustände wird die Suche auf „jetzt + 2 Frames“ gestellt (`resetNextMoodCheckTime`, `ForceIdleMSEC` = 67 ms in `AIData.ini`), danach alle
  `MoodAttackCheckRate`; das erste Intervall nach einem Eintritt ist um bis zu eine halbe Rate verschoben (`m_randomlyOffsetMoodCheck`). Der Zufall dafür ist wie beim Abtasten der Türme ein Hash aus
  (Weltsamen, Entity-Id, Tick) und zieht nichts aus `LogicRandom`.
* **Wer nicht sucht:** Objekte im Bau, verkaufte, per EMP oder Skript (`DISABLED_HELD`) stillgelegte und enthaltene Objekte; getarnte Einheiten ohne Bit `STEALTHED` („Sorry, stealthed and not allowed to idle fire
  when stealthed“; gilt wie in der Referenz nicht für den Angriffsmarsch); schlafende und passive Teams eines Computerspielers („Human units don't have a mood“).
* **Opferwahl** (`AI::findClosestEnemy`): der nächste Feind (Mittelpunktabstand, bei Gleichstand die kleinere Id), der lebt, kein Projektil/`INERT`/Mine ist, nicht in einem Container steckt, für den Spieler der
  Einheit sichtbar ist (`CanPlayerSee`: Nebel) und nicht getarnt und unentdeckt ist (`PartitionFilterStealthedAndUndetected`), und gegen den eine Waffe wirkt, die die Einheit selbst wählen darf
  (`PartitionFilterPossibleToAttack`: `AutoChooseSources` mit `CMD_FROM_AI`, `AntiMask`, Schaden durch die Panzerung, gepinnter Slot).
* **Gebäude:** `PartitionFilterInsignificantBuildings` (`AttackIgnoreInsignificantBuildings = Yes`): ein leeres Gebäude mit Container, das kein Fraktionsgebäude (`FS_*`) ist, nimmt niemand.
  `PartitionFilterRejectBuildings`: andere feindliche Gebäude sind Opfer für Einheiten eines **Computerspielers** (`m_acquireEnemies`) und für Einheiten mit `ATTACK_BUILDINGS`; alle anderen nehmen nur
  `FS_BASE_DEFENSE` und besetzte Gebäude. Eine ruhende Einheit des Menschen schießt also nicht von selbst auf eine Kaserne – wie im Original.
* **Angriffspriorität** (Missionen, `IAcquisitionAdvisor` = `ScriptEngine`): hat die Einheit einen Prioritätssatz, gewinnt die höchste Priorität nach Abzug von einem Punkt je `AttackPriorityDistanceModifier`
  (100) Entfernung, mindestens 1; bei Gleichstand die höhere Rohpriorität, dann der nähere; Priorität 0 wird nie angegriffen. Ohne Satz gilt „der nächste“ (`getDefaultAttackInfo`).
* **Reichweite der Suche** (`AI::getAdjustedVisionRangeForObject`): wer nicht verfolgen darf, sucht nur in Waffenreichweite (`AI::WITHIN_ATTACK_RANGE`). Wer verfolgen darf – nur Bodeneinheiten eines
  Computerspielers **im Angriffsmarsch** –, sucht bis `VisionRange` × `GuardOuterModifierAI` (1,333), bei wachsamen Teams × 1,1 und bei aggressiven × 1,5, läuft dem Opfer nach (`AIAttackApproachTargetState`;
  das Ziel der Verfolgung wird alle 15 Ticks nachgeführt, die Türme folgen als Verfolgung) und gibt es auf, wenn es weiter als zwei Suchradien weg oder nicht mehr sichtbar ist.
* **Schuss:** eine Einheit mit Opfer feuert in jedem Tick, in dem sie darf – derselbe Weg wie ein angenommener `FireWeaponCommand` mit `FireOrigin.Ai` (`ChooseWeapon`, `IFireGate`, Turmausrichtung mit
  Angriffszustand, `PreAttackDelay`, Magazin, `WeaponFired`-Ereignis), aber ohne Befehl. Ein Deploy-Fahrzeug baut dadurch von selbst auf, sobald es ein Opfer hat.
* **Ende:** Opfer tot, unsichtbar, kein Feind mehr oder außer Reichweite (ohne Verfolgung); jeder neue AI-Befehl (`EndAttack`: Bewegung, Idle, Andocken, Bauen, Fähigkeit) löscht Opfer **und** Angriffsmarsch.
* **Befohlene Angriffe:** ein angenommener `FireWeaponCommand` (Ursprung Spieler oder KI, nicht Skript-Knopf) oder `PursueCommand` macht dessen Ziel zum Opfer der Einheit (`Ordered`); solange solche Befehle
  kommen – bis 45 Ticks nach dem Zeitpunkt, an dem die Einheit wieder hätte feuern können –, sucht und feuert die Einheit nicht selbst. So schießt nichts doppelt: `StandingOrders.Attack`, das Referenzszenario
  und `ObjectiveDriver` fahren ihre Angriffe weiter über Befehle.
* **Flugzeuge** (`KINDOF_AIRCRAFT`): halten im Angriffsmarsch nicht an und verfolgen nicht, sie feuern im Vorbeiflug; ein gespeicherter Angriffsmarsch eines parkenden Jets bleibt einer (`JetAIUpdate`
  trägt ihn mit dem aufgeschobenen Befehl).

### 33.3 `AttackMoveCommand`

`AttackMoveCommand(Unit, X, Y)` (Typ 32; `MSG_DO_ATTACKMOVETO` → `aiAttackMoveToPosition`): `AIUpdateInterface.OrderMove(…, attackMove: true)` → `IWorldServices.BeginAttackMove`. Die Welt hält den Zustand
(`AcquisitionState.AttackMove`, Ziel, `Halted`, `Chasing`); für den Kampf nimmt sie der Einheit das Bewegungsziel (`StopInternalMove`) und gibt es ihr danach wieder (`StartInternalMove` zum Ziel), ohne dass
das ein neuer Befehl wäre. Abfragen für Befehlsgeber: `SimulationWorld.IsAttackMoving`, `AttackMoveGoalOf`, `AcquiredVictimOf`. Eine angehaltene Einheit auf Angriffsmarsch hat kein Bewegungsziel; Befehlsgeber, die
„steht still“ als „braucht einen neuen Befehl“ lasen, fragen deshalb `IsAttackMoving` mit (`ScriptEngine.Marching`, `SkirmishAi.CommandArmy`, `StandingOrders`).

**Versionen:** `CommandTypes.Version` 13 → **14**, `ReplayFormat.CurrentVersion` 10 → **11** (alte Replays scheitern an der Befehlstabelle). `WorldSnapshot.Version` bleibt **9**: der neue Zustand je Objekt
(`acquisition`: Opfer, befohlen, Angriffsmarsch mit Ziel, Modus und nächster Suchzeitpunkt) wird nur geschrieben, wenn das Objekt selbst sucht oder angreift, und nur gelesen, wenn er da ist.
`MissionSnapshot` bleibt **2** (`combatIntervalTicks` behält Name und Feld; 0 schaltet den Zielerwerb der Mission ab, `SimulationWorld.SelfAcquisition`).

### 33.4 Die Befehlsgeber

| Befehlsgeber | vorher | jetzt |
|---|---|---|
| `MissionSession` | `DriveCombat`: jedes bewaffnete Objekt feuerte alle 3 Ticks per Befehl auf den nächsten Feind in Reichweite – auch im Gehen, auch mit `AutoAcquire = No`, auch ohne AI-Modul | entfallen; die Sitzung setzt `World.AcquisitionAdvisor = Engine` und zählt `WeaponFired` |
| `ScriptEngine` | Jagd-/Angriffsbefehle als `SetMoveTargetCommand`, Schüsse durch `DriveCombat` | Jagd, Team- und Gebietsangriff als `AttackMoveCommand` (`AttackToward`: eine kämpfende oder schon dorthin marschierende Einheit bekommt keinen neuen Befehl); Angriff auf ein benanntes Objekt zusätzlich als Feuerbefehl (`FireAtOrdered`); Bewegungen wachsamer/aggressiver Teams eines Computerspielers werden Angriffsmärsche (`privateMoveToPosition`, `MAA_Action_To_AttackMove`); Stimmung aus `teamAggressiveness` (`Team.cpp`, `TeamTemplateInfo`) und `TEAM_SET_ATTITUDE` |
| `SkirmishAi` | `Fire()` alle 5 Ticks, `MoveGoalFor` (Annäherung auf 70 % der Reichweite) | `Fire` und `MoveGoalFor` entfallen; `CommandArmy` schickt die Armee per `AttackMoveCommand` zum Gruppenziel |
| `StandingOrders` (Darstellung) | Angriffsbewegung und Wachen feuerten per Befehl auf den nächsten Feind | Angriffsbewegung = `AttackMoveCommand`; kein Feuerbefehl mehr außer beim Angriff auf ein genanntes Ziel |
| `ObjectiveDriver` (Nur-Befehle-Modus) | Bewegungsbefehl zum Opfer, Schüsse durch `DriveCombat` | Angriffsmarsch plus Angriffsbefehl auf das Opfer (`Assault`, `DriveAssaults`) |

### 33.5 Näherungen und was fehlt

* **Keine Verfolgung aus dem Leerlauf.** In der Referenz läuft eine ruhende Einheit eines Computerspielers einem Opfer in ihrem Suchradius nach (`aiAttackObject`); hier bleibt sie stehen und nimmt nur, was in
  Reichweite ist. Verfolgt wird nur im Angriffsmarsch. Grund: die Befehlsgeber stellen ihre Einheiten (Sammelpunkt, Skriptposten); eine Einheit, die ihren Posten verlässt, kollidiert mit deren Buchführung.
* **`AIData.ini` ist nicht typisiert:** `GuardOuterModifierAI` 1,333, `AlertRangeModifier` 1,1, `AggressiveRangeModifier` 1,5, `AttackPriorityDistanceModifier` 100, `ForceIdleMSEC` 67 und
  `AttackIgnoreInsignificantBuildings = Yes` stehen als benannte Konstanten mit den Werten der ausgelieferten Datei im Code. `GuardOuterModifierHuman` (2,2) hat keine Wirkung, weil Einheiten des Menschen ohnehin
  nur in Waffenreichweite suchen.
* **Passive Teams** erwerben nichts (die Referenz lässt sie auf ihren letzten Angreifer antworten); **schlafende** Teams bewegen sich weiter, wenn ein Skript es befiehlt (`MAA_Action_To_Idle` fehlt).
  `NAMED_SET_ATTITUDE` (einzelnes Objekt) bleibt aufgezeichnet ohne Wirkung; `m_attackCommonTarget` (Team greift gemeinsames Ziel an) fehlt.
* **`NOTWHILEATTACKING`** ist gelesen, hat aber keine eigene Wirkung: eine Einheit mit Opfer sucht hier nie ein zweites.
* **Sichtlinie:** `AttackUsesLineOfSight`/`KINDOF_ATTACK_NEEDS_LINE_OF_SIGHT` (`AI::CAN_SEE`) ist nicht geprüft; Sicht ist die des Spielers (Nebel) – die Referenz verlangt den Nebeltest nur für Einheiten des
  Menschen, hier gilt er für alle.
* **Abstände** sind Mittelpunktabstände (die Referenz misst `FROM_BOUNDINGSPHERE_2D`); der leere-Gebäude-Filter fragt „hat einen Container und ist leer“ statt `isGarrisonable`.
* **Aus Containern wird nicht gefeuert** (Garnison, `isPassengerAllowedToFire`): enthaltene Objekte erwerben nichts. `DriveCombat` ließ sie feuern; das war kein belegtes Verhalten.
* **Objekte ohne AI-Modul erwerben nichts** (in der Referenz hat jedes angreifende Objekt eines). `DriveCombat` ließ auch sie schießen.
* **Aufgeschobene Bewegungen:** nur `JetAIUpdate` trägt den Angriffsmarsch durch einen aufgeschobenen Befehl; ein `DeployStyleAIUpdate`-Fahrzeug, das erst einpacken muss, und ein Hacker, der erst abbaut, führen ihn als
  einfache Bewegung aus. `AIAttackMoveToState` wiederholt eine gescheiterte Wegsuche (`ATTACK_RETRY_COUNT`); hier endet der Angriffsmarsch, wenn die Bewegung endet.
* **Einheitenfähigkeit:** `OBJECT_STATUS_IS_USING_ABILITY` wird nicht gesetzt; eine Einheit, die eine Fähigkeit ausführt und dabei als untätig gilt, könnte ein Opfer erwerben.
* **Darstellung und Bedienung** (Angriffsbewegung im Client, selbst schießende ruhende Einheiten des Menschen) hat **kein Mensch gesehen**.

### 33.6 Messwerte (Release, Retaildaten, 5. Oktober 2026)

„Vorher“ ist der Stand `4eea97b`, mit denselben Werkzeugen neu gemessen (er stimmt mit §32 überein); „nachher“ ist dieser Stand.

| Lauf | vorher (`4eea97b`) | nachher |
|---|---|---|
| `SimTool reference-match` | Sieger FactionGLA, Tick 13.500, Endhash `0946FB676DEC615C`, Kette `EA6D12F180930D71` | Sieger FactionGLA, Tick **14.490**, Endhash `23B4B25DF62765D8`, Kette `DE5849AF9F8493C4`; Lauf 2 und Snapshot-Fortsetzung (Tick 7.395) identisch |
| `SimTool stress` (216.000 Ticks) | Endhash `56882F763EABEF67`, Kette `B8E663493F2C73D5` | **unverändert**: Endhash und Kette gleich, also bekommt in der synthetischen Fixture kein Objekt einen Angriffszustand (er stünde im Hash des Objekts) |
| 16 KI-Läufe (`ai-match … --max-ticks 45000`) | 12 von 16 entschieden | **11 von 16** entschieden; alle 16 in Lauf 2 und nach Snapshot-Fortsetzung tickgenau identisch (Tabelle unten) |
| `CampaignTool audit --ticks 900 --campaign CHALLENGE_0` | 7/7 deterministisch, 7/7 identisch nach Speichern/Laden, Ausschalt-Enden Sieg 7/7, Niederlage 6/7 | **unverändert** 7/7, 7/7, 7/7, 6/7 (4.984 statt 4.980 Skripte ausgeführt, 0 Treffer auf fehlende Typen) |
| `CampaignTool drive --campaign CHALLENGE_0` (60.000 Ticks) | Sieg 7/7, Niederlage 7/7 | **unverändert**, jede Zeile (Skript, Tick, Eingriffe) gleich |
| `CampaignTool drive --campaign GLA` | Sieg 5/5, Niederlage 5/5 | **unverändert** 5/5 und 5/5, gleiche Skripte und Ticks; `MD_GLA03` braucht 258 statt 227 Welteingriffe |
| `scripts/audit-scenarios.ps1` | drei Szenen grün | **drei Szenen grün** |
| Tests | – | `Simulation.Tests` Release mit `--long --content-dir`: 348 bestanden (8 neu: 7 synthetisch, 1 Realdaten); `Presentation.Tests` bestanden; `Game.Tests` (Release, `--content-dir`) bestanden |

Das Referenzspiel verschiebt sich (sein Szenario befiehlt weiter jeden Schuss; neu ist, dass Einheiten in den Lücken zwischen seinen Befehlen selbst schießen und dass seine Befehle die eigene Zielwahl der
Einheit sperren). Der Realdatentest `RealAcquisition` lässt einen Crusader und einen Battlemaster ohne jeden Befehl kämpfen: erster Schuss in Tick 3, 13 Schüsse, einer zerstört, zwei Läufe hashgleich.

**Die 16 KI-Läufe** (Standardseeds; „vorher“ deckt sich mit der Spalte „jetzt“ aus §32):

| Karte | Paarung | vorher | Endhash vorher | **nachher** | Endhash nachher | Kette nachher |
|---|---|---|---|---|---|---|
| Alpine Assault | China, GLA | China, Tick 7.545 | `39344016A5EE8F91` | **China, Tick 23.310** | `54DD2C9094141CC8` | `582FA4C6DF853C4B` |
| Alpine Assault | America, China | America, Tick 16.800 | `745839146322DDE6` | **America, Tick 16.965** | `BA10AC1EB0EB8AC9` | `F669459C2FA6E39C` |
| Alpine Assault | GLA, America | America, Tick 10.740 | `DDDFE22494F29A34` | **America, Tick 8.895** | `23A93C58C775A691` | `B29CF881F6B1CC59` |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | Patt | `3CD8D8C4D53675C0` | **Patt** | `2EDDC1B7BF19F28B` | `0C7DD733866C09BA` |
| Armored Fury | China, GLA | China, Tick 11.460 | `6CAD71B81D3CE47A` | **Patt** | `89B05A25738A9E6D` | `35DFAE191CAD2649` |
| Armored Fury | America, China | America, Tick 33.255 | `63486D2572E76BCE` | **America, Tick 19.095** | `4A99580AC54F4EBD` | `AD65CED96F474586` |
| Armored Fury | GLA, America | America, Tick 40.260 | `9801D2537695316C` | **America, Tick 37.365** | `3C7BC00FB4E3E8C1` | `0C664B1DEFEFDF44` |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | Patt | `E5ED792B12D35CC0` | **Patt** | `C8ACAB511BFC8C86` | `15D05EFD38B29EEB` |
| Bitter Winter | China, GLA | China, Tick 12.930 | `CB890471FCB4B084` | **China, Tick 17.220** | `26CD35AC8AA660F5` | `2B86E2038EDFAAB7` |
| Bitter Winter | America, China | Patt | `0FDEB99B1DEFAAA6` | **Patt** | `3C403645D06C2E92` | `6F84497D225021F4` |
| Bitter Winter | GLA, America | America, Tick 14.430 | `A3A6295689F3F0E5` | **America, Tick 8.895** | `AB725263AEF8E3B7` | `CDFCDF66730FF07B` |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | Patt | `0EC68A99E3ED5ACD` | **Patt** | `A4B3ACB444C989B4` | `BA6DCDD5A38758EF` |
| Death Valley | China, GLA | China, Tick 11.160 | `DA30DBBC2434E499` | **China, Tick 8.565** | `9F0F8D7D28E8654D` | `90CDEBE18E7C6B01` |
| Death Valley | America, China | China, Tick 9.975 | `8DB4AA743B8F349A` | **America, Tick 9.330** | `E98A532C90F89642` | `AC321A0CFA9C00E1` |
| Death Valley | GLA, America | America, Tick 13.755 | `2992C9E0876B2C95` | **America, Tick 8.175** | `3F33A4FEDEBA85C1` | `3B00774A84E69278` |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | AmericaAirForceGeneral, Tick 32.280 | `C443660F229E157E` | **ChinaTankGeneral, Tick 32.475** | `828EE15100622D42` | `0C3AF1B086B9428E` |

Entschieden: vorher 12, nachher **11**. Zwei Ausgänge kippen: Armored Fury China/GLA (China bei 11.460 → Patt) und Death Valley Tank/AirForce (AirForce bei 32.280 → ChinaTankGeneral bei 32.475); Death Valley
America/China wechselt den Sieger (China → America). Von den elf entschiedenen Läufen enden sieben früher als vorher und vier später (deutlich: Alpine Assault China/GLA 7.545 → 23.310 und Bitter Winter China/GLA 12.930 →
17.220; knapp: Alpine Assault America/China und Death Valley Tank/AirForce). Bei 16 Läufen ist das keine Aussage über Stärke; **keine Balance-Aussage**.

**Was die Spuren der fünf Patts zeigen** (`SimTool ai-match … --trace N --trace-army`; `--trace-army` ist neu und druckt je Spieler die Armee nach Typ mit „marschiert / Angriffsmarsch / kämpft“, dem
Abstand zum nächsten feindlichen Gebäude und der Selbstauskunft der KI):

| Lauf | Bild bei Tick 45.000 |
|---|---|
| Alpine Assault, Tank/AirForce | China: 7 Gebäude, Armee 0 seit Tick 27.000, 150 Credits, Strecke Zentrum–Lager 956 (das nahe Lager ist leer), Produktion steht bei 74. AirForce: 13 Gebäude, 0 Credits, 4 Sammler, produziert 200 Einheiten – ausschließlich Ranger und Missile Defender –, die in Gruppen von 4 bis 12 an Chinas einer Verteidigung sterben; die erste Welle endet nie (Wellen 1) |
| Armored Fury, China/GLA | China: 9 Gebäude, 0 Credits, Armee 0–2, Strecke 654. GLA: 9 Gebäude, 30 Credits, 295 Einheiten produziert, fast nur Rebellen, die über 180 bis 1.150 Einheiten verteilt anmarschieren und an einer Verteidigung sterben |
| Armored Fury, Tank/AirForce | beide 12–13 Gebäude, 125–150 Credits, Strecken 654 und 521, Armeen 4 und 6 unter der Angriffsschwelle (16 und 22) |
| Bitter Winter, America/China | beide seit Tick 30.000 eingefroren: 25 und 250 Credits, Strecken 1.173 und 1.127, Armeen 2 und 0, die Vorräte der Karte bleiben bei 1.068 Kisten stehen |
| Bitter Winter, Tank/AirForce | AirForce hat 3 Gebäude, keine Armee, keinen Bauarbeiter, keine Kaserne; China 11 Gebäude, aber 125 Credits, Strecke 1.173 und 3 Einheiten unter der Schwelle 12 – es greift nicht mehr an |

Gemeinsam ist allen: am Ende haben beide Seiten kein Geld (das Lager neben dem Versorgungszentrum ist leer, das nächste gefüllte 500 bis 1.200 Einheiten entfernt, für ein weiteres Zentrum reicht das Geld
nicht), und die Armeen bestehen aus der jeweils billigsten Infanterie, die eine einzelne Basisverteidigung nicht überwindet. Der Zielerwerb zeigt in den Spuren keinen Fehler (Einheiten im Angriffsmarsch
kämpfen und marschieren weiter; „kämpft“ und „marschiert“ zugleich sind Verfolgungen). Das ist der Befund aus §32 und `SIM-TURRET-AIM.md` §8a, jetzt mit Zahlen je Lauf.

**Ein Korrekturversuch, gemessen und verworfen:** „eine Armee, die 90 Sekunden nicht gewachsen ist, greift auch unter der Schwelle an“ (für Bitter Winter Tank/AirForce naheliegend). Alle 16 Läufe neu:
dieselben 11 entschieden, die elf mit identischem Endhash, die fünf Patts blieben Patts (nur ihre Hashes änderten sich). Die Änderung ist deshalb nicht im Code. Was die Spuren als Ursache nennen –
Wirtschaft nach dem Leerlaufen des ersten Lagers und die Zusammensetzung der Armee – ist ein eigener Auftrag an `SkirmishAi` (Geld für ein zweites Zentrum zurückhalten, nicht nur das Billigste bauen).

## 34. Phase 17 (5. Oktober 2026): `SkirmishAi` als Agent einer Mission, Leistung ruhig gemessen

### 34.1 Was sich an `SkirmishAi` geändert hat

Der Nur-Befehle-Modus des Zielfahrers (`docs/SCRIPT-ENGINE.md` §9) lässt `SkirmishAi` für den Betrachter einer Mission spielen. Zwei Fehler fielen nur dort auf und sind in der KI selbst behoben; drei
Schalter setzt nur der Missionsagent (ein Skirmish-Match setzt keinen davon).

| Änderung | Wo | Begründung |
| --- | --- | --- |
| Lager einer **nicht feindlichen Seite** zählen als Lager (`Observe`) | `SkirmishAi.Observe` | Missionskarten geben die `SupplyDock`s einem Zivilspieler statt dem Systemspieler; `FindSupplySource` nahm sie schon an, das Gedächtnis `_warehouses` nicht – „warehouses known 0“, kein Sammler wurde geschickt |
| Versorgungszentrum an der Basis nach drei vergeblichen Platzsuchen neben der Quelle (`SupplyPlacementPatience`) | `BuildStructures` | `AIPlayer::buildBySupplies` kennt den Fall „kein legaler Platz“ nicht; hier blieb das Zentrum sonst für immer ungebaut (`GC_ChemGeneral`: Lager am Kartenrand) |
| `Objectives` (Angriffsziele vor der eigenen Wahl) | `AttackTarget` | die Opfer der Missionsziele, wie Briefing und Zielmarken sie einem Spieler nennen |
| `CommitEverything` (Angriff ohne Schwelle, kein Rückzug zum Sammeln) | `CommandArmy` | eine Seite ohne Produktionsgebäude wartete sonst für immer unter der Angriffsschwelle |
| `ObjectiveReserve` (Geld, das die KI nicht anrührt) | `Think` | der Kauf, den ein Missionsziel braucht (Eroberungs-Upgrade) |
| Option `EconomyFirst` (nichts anderes bauen, bis das erste Versorgungszentrum steht) | `BuildStructures` | ohne Einkommen ging das Startgeld der Challenge-Karten in Kraftwerke und Kasernen |

Die 16 KI-gegen-KI-Läufe sind davon unberührt: die Endhashes des gemergten Stands sind die aus §33.6 (dort ohne diese Änderungen gemessen).

### 34.2 Leistung (ruhige Maschine, keine parallelen Läufe, Release, 5. Oktober 2026)

| Lauf | vorher (`4eea97b`) | nachher (`2a66661`) |
| --- | --- | --- |
| `SimTool stress`, Lauf 1 (216.000 Ticks), zweimal gemessen | 13,1 s / 13,3 s (60,5 / 61,6 µs je Tick) | 13,3 s / 13,3 s (61,5 / 61,6 µs je Tick) |
| Endhash / Kette | `56882F763EABEF67` / `B8E663493F2C73D5` | unverändert |
| `SimTool mission-run` `MD_USA05`, 3.000 Ticks (Gesamtlaufzeit des Werkzeugs) | 73,4 s | 66,7 s |
| `SimTool mission-run` `MD_GLA05`, 3.000 Ticks | 135,6 s | 147,9 s |

Der Stresstest zeigt keinen Schwund (die 150 µs je Tick aus Phase 14 waren Last, nicht Code; unter Last maß derselbe Stand heute 166 µs). Die Missionsläufe ändern ihre Hashes, weil der Zielerwerb jetzt in der
Simulation liegt; `MD_GLA05` ist 9 % langsamer, `MD_USA05` 9 % schneller. **Nicht gemacht:** ein Profil der heißesten Module. Die Missionsläufe sind der Ort dafür (die Zahl oben ist die Gesamtlaufzeit des Werkzeugs
mit Laden und Wiederholungsläufen, kein Wert je Tick).

## 35. Phase 17 (5. Oktober 2026): Schwierigkeit eines Computerspielers im Skirmish – Befund, keine Simulationsänderung

Auftrag war, der Simulation eine in den Daten belegte Schwierigkeitsachse für Computerspieler zu geben und die drei Armeen der Skirmish-Einrichtung
(„Easy/Medium/Hard Army“) daran zu binden. Die Prüfung der Referenz ergibt: **keine der in den Daten belegten Wirkungen gilt für einen Computerspieler im Skirmish.**
Es wurde deshalb nichts an der Simulation geändert (kein Feld, kein Snapshot-Eintrag, keine Version); stattdessen bietet die Einrichtung nur noch eine Armee
(`docs/GAME-SHELL.md` §6). Erfunden wurde nichts.

### 35.1 Was die Daten enthalten

| Datei | Inhalt | gemessen |
| --- | --- | --- |
| `Data/INI/GameData.ini` | `WeaponBonus = SOLO_AI_EASY RATE_OF_FIRE 80%`, `SOLO_AI_NORMAL … 100%`, `SOLO_AI_HARD … 120%` | drei Zeilen; für `SOLO_HUMAN_*` gibt es keine `WeaponBonus`-Zeile |
| `Data/INI/GameData.ini` | `HumanSoloPlayerHealthBonus_Easy = 150%`, `_Normal = 100%`, `_Hard = 80%` | `AISoloPlayerHealthBonus_*` kommt nicht vor (Vorgabe 1,0, `GlobalData::GlobalData`) |
| `Data/INI/Default/AIData.ini` | `SideInfo` mit `ResourceGatherersEasy/Normal/Hard` | 12 Seiten; je Seite sind alle drei Werte gleich (2, bei den GLA-Seiten 5) |

Die `WeaponBonus`-Zeilen sind in `CnCNew.Data` bereits streng typisiert (`GameDataSchema`, `FieldType.WeaponBonusEntry`); die Bedingungsnamen `SOLO_*` stehen in der
Namenstabelle. An `baselines/compatibility.json` ändert sich nichts.

### 35.2 Wo die Referenz sie anwendet

| Wirkung | Fundstelle (Datei, Funktion) | gilt im Skirmish? |
| --- | --- | --- |
| Waffenbonus `SOLO_AI_*` / `SOLO_HUMAN_*` auf jedes Objekt des Spielers | `Common/RTS/Player.cpp`, `Player::friend_applyDifficultyBonusesForObject` (aufgerufen aus `Object::setReceivingDifficultyBonus`, `Object.cpp`; `Object::initObject` setzt es, solange `ScriptEngine::getObjectsShouldReceiveDifficultyBonus` gilt) | **nein** – der ganze Rumpf steht unter `if (TheGameLogic->isInSinglePlayerGame())` |
| Lebenspunktefaktor `m_soloPlayerHealthBonusForDifficulty[Spielertyp][Schwierigkeit]` | dieselbe Funktion | **nein**, dieselbe Bedingung; für Computerspieler ohnehin 1,0 |
| Spielmodus | `GameLogic/System/GameLogic.cpp`, `GameLogic::isInSinglePlayerGame`: nur `GAME_SINGLE_PLAYER` (oder dessen Wiedergabe). Das Skirmish-Menü startet `GAME_SKIRMISH` (`SkirmishGameOptionsMenu.cpp`, `reallyDoStart`: `MSG_NEW_GAME` mit `GAME_SKIRMISH`) | – |
| Slot → Schwierigkeit | `GameLogic::startNewGame`: `SLOT_EASY_AI`/`SLOT_MED_AI`/`SLOT_BRUTAL_AI` → `skirmishDifficulty` = `DIFFICULTY_EASY`/`NORMAL`/`HARD`; `Player::initFromDict` gibt es an `AIPlayer::setAIDifficulty`; `Player::getPlayerDifficulty` liefert es | ja – die Achse selbst existiert je Computerspieler |
| Zahl der Sammler | `GameLogic/AI/AIPlayer.cpp`, `AIPlayer::checkForSupplyCenter` (`AISideInfo::m_easy/m_normal/m_hard`) | ja, aber wirkungslos: die ausgelieferten Werte sind je Seite gleich |
| Skripte mit Leicht/Normal/Schwer-Flaggen | `ScriptEngine::executeScript` (`Script::isEasy/isNormal/isHard` gegen `Player::getPlayerDifficulty`), `TeamPrototype::evaluateProductionCondition` (`Common/RTS/Team.cpp`) | ja – das ist der eigentliche Unterschied der drei Armeen (`SkirmishScripts.scb`) |
| Gemeinsames Teamziel | `Team::setTeamTargetObject` (`Team.cpp`): ein Computerspieler der Stufe leicht setzt kein gemeinsames Angriffsziel | ja, aber nur für Skript-Teams |
| Opferwahl beim Angriff auf einen Trupp | `AIAttackSquadState::chooseVictim` (`GameLogic/AI/AIStates.cpp`): leicht zufällig, normal das nächste, schwer alle dasselbe | ja, aber nur über `aiAttackTeam` (Skriptaktion bzw. `AIGroup`) |

Es gibt keine weitere Stelle, die `getPlayerDifficulty`, `getAIDifficulty` oder `m_difficulty` liest (gesucht in `GameEngine/Source/Common` und `GameEngine/Source/GameLogic`).

### 35.3 Folgerung

- Die beiden Zahlenwirkungen der Daten (Feuerrate 80 %/120 %, Lebenspunkte) sind **Kampagnenregeln** („Solo“), keine Skirmish-Regeln. Sie auf Skirmish-Gegner anzuwenden wäre erfundenes Tuning.
- Die im Skirmish wirksame Schwierigkeit des Originals steckt in den KI-Skripten und in zwei Team-Verhalten, die an Skript-Teams hängen. Dieses Projekt führt `SkirmishScripts.scb`
  nicht aus (`SkirmishAi` ersetzt sie, §19/§32) und `SkirmishAi` arbeitet ohne Skript-Teams; eine Nachbildung wäre Imitation, kein Beleg. **Das bleibt der Unterschied zum Original.**
- Der eine Computerspieler dieser Simulation entspricht damit `DIFFICULTY_NORMAL`: kein Bonus, kein Malus. Die Einrichtung bietet deshalb `GUI:Open`, `GUI:Closed` und `GUI:MediumAI`
  („Medium Army“) – ein Eintrag des Originals in seiner Bedeutung, statt dreier, die dasselbe starten. `GameShell.SkirmishDifficultyIgnored` ist entfallen.
- **Nebenbefund, nicht bearbeitet:** dieselbe Funktion gilt in Kampagnenmissionen, und dort wendet diese Simulation sie **nicht** an – `MissionOptions.Difficulty` steuert nur die
  Skriptflaggen (`ScriptEngine`), weder der Waffenbonus `SOLO_AI_EASY/HARD` noch `HumanSoloPlayerHealthBonus_Easy/Hard` wirken; auch die Skriptaktion, die den Bonus je Karte
  ab- und anschaltet (`OBJECT_ALLOW_BONUSES`, `ScriptActions::doEnableOrDisableObjectDifficultyBonuses`), ist nur aufgezeichnet und hat keinen Verbraucher. Auf „normal“ ist das ohne Wirkung (100 %), auf „leicht“/„schwer“ weicht
  eine Mission vom Original ab. Das lag außerhalb dieses Auftrags (Skirmish) und ist eine Entscheidung vor dem Einfrieren der Simulation.

### 35.4 Messung (Release, 5. Oktober 2026)

Kein Quelltext von `CnCNew.Simulation`, `CnCNew.Data` oder `tools/CnCNew.SimTool` wurde geändert; die Läufe bestätigen den Stand und sind zugleich „vorher“ und „nachher“.
Eine Wirkungstabelle Leicht/Schwer gibt es nicht, weil es keine Wirkung gibt; `SimTool ai-match` hat keine Option `--difficulty` bekommen.

| Lauf | Ergebnis | Endhash | Kette | Lauf 2 / Fortsetzung aus Snapshot |
| --- | --- | --- | --- | --- |
| `SimTool reference-match` | FactionGLA bei Tick 14.490 | `23B4B25DF62765D8` | `DE5849AF9F8493C4` | identisch / identisch |
| `SimTool stress` (216.000 Ticks) | – | `56882F763EABEF67` | `B8E663493F2C73D5` | identisch |
| `ai-match` Alpine Assault, `FactionChina,FactionGLA`, 45.000 Ticks | FactionChina bei 23.310 | `54DD2C9094141CC8` | `582FA4C6DF853C4B` | identisch / identisch |
| `ai-match` Alpine Assault, `FactionAmerica,FactionChina` | FactionAmerica bei 16.965 | `BA10AC1EB0EB8AC9` | `F669459C2FA6E39C` | identisch / identisch |
| `ai-match` Alpine Assault, `FactionGLA,FactionAmerica` | FactionAmerica bei 8.895 | `23A93C58C775A691` | `B29CF881F6B1CC59` | identisch / identisch |

## 36. Abschluss Phase 17 (6. Oktober 2026): zweites Versorgungszentrum der KI; Heeresauswahl nach Wert gemessen und verworfen

### 36.1 Befund und Regel

Die fünf Patts aus §33.6 enden alle gleich: das Lager neben dem Versorgungszentrum ist leer, das nächste gefüllte liegt 500 bis 1.200 Einheiten entfernt, und ein weiteres Zentrum plante `SkirmishAi` nur mit
einer Armee von zehn Einheiten (`PlanGoals`) – die es ohne Einkommen nie mehr gab. In der Referenz entscheiden die Skirmish-Skripte, wann ein weiteres Zentrum gebaut wird (`AI_PLAYER_BUILD_SUPPLY_CENTER` →
`AIPlayer::buildBySupplies`, Bedingung `SUPPLY_SOURCE_SAFE` → `AIPlayer::isSupplySourceSafe`); diese Skripte führt das Projekt nicht aus. Die Regel hier nimmt deshalb die Bausteine der Referenz und eine eigene
Auslösebedingung:

| Baustein | Referenz (`GameLogic/AI/AIPlayer.cpp`) | hier (`Skirmish/SkirmishAi.cs`) |
| --- | --- | --- |
| Was ein Zentrum noch sammeln kann | `AIPlayer::queueSupplyTruck`: „Make sure we have supplies near it“ – Lager im Umkreis `SUPPLY_CENTER_CLOSE_DIST` (20 Zellen) plus Radius, nicht feindlich, mit Kisten; ein Zentrum ohne solche Quelle bekommt keine Sammler mehr | `SupplyStockInReach`: Summe der Kistenwerte dieser Lager über alle eigenen Zentren |
| Wohin das nächste Zentrum | `AIPlayer::findSupplyCenter` (schon seit Phase 16: `FindSupplySource`) | unverändert |
| Ob die Quelle sicher ist | `AIPlayer::isSupplySourceSafe` → `isLocationSafe`: kein Feind im Umkreis `SupplyCenterSafeRadius` (AIData.ini: 300) plus Radius; Sammler, Dozer und unbedeutende Gebäude zählen nicht | `SupplySourceSafe`: sichtbare feindliche Einheiten und bewaffnete Gebäude sowie erinnerte bewaffnete Gebäude (nur was der Spieler sieht oder gesehen hat; die Referenz liest die Partition) |
| **Auslöser** | Skript (nicht ausgeführt) | **eigen:** sobald der Vorrat in Reichweite unter 3.000 Credits fällt (`LowSupplyStock`), höchstens vier Zentren; das Ziel steht dann vor allen anderen Bauzielen, und solange das Geld nicht reicht, hält die Heeresproduktion den Preis zurück (die vorhandene Rücklage von `BuildStructures`) |
| Untätige Sammler | – | `OrderCarriers` schickt sie zum nächsten Lager, das noch etwas enthält (vorher: zum nächsten, auch wenn es leer war) |

Die Schwelle 3.000 ist eine eigene Wahl (das Zentrum muss aus dem Resteinkommen noch bezahlbar sein), kein Wert der Daten. Einen Unit-Test hat die Regel nicht: die synthetische Fixture kennt kein
Versorgungszentrum mit Andockmodul; belegt ist sie durch die 16 Läufe unten.

### 36.2 Die 16 KI-Läufe (Release, `ai-match … --max-ticks 45000`, Standardseeds; vorher = Stand `1a1732c`, nachher = `6eb7ae4`)

| Karte | Paarung | vorher | **nachher** | Endhash nachher | Kette nachher |
|---|---|---|---|---|---|
| Alpine Assault | China, GLA | China, Tick 23.310 | China, Tick 23.370 | `0BD4FE5EEB2C37E7` | `BF9939E5A3B45736` |
| Alpine Assault | America, China | America, Tick 16.965 | unverändert | `BA10AC1EB0EB8AC9` | `F669459C2FA6E39C` |
| Alpine Assault | GLA, America | America, Tick 8.895 | unverändert | `23A93C58C775A691` | `B29CF881F6B1CC59` |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | Patt | Patt | `D99B60905846A6B1` | `80EDA8449C86CEE6` |
| Armored Fury | China, GLA | Patt | Patt | `7196F712342A3BDA` | `FF370FDA722BFA3B` |
| Armored Fury | America, China | America, Tick 19.095 | America, Tick 19.095 (anderer Hash) | `0E0E07558397DB62` | `E41C4EF3D1DFBEF4` |
| Armored Fury | GLA, America | America, Tick 37.365 | America, Tick 29.865 | `58B066C7284C21B2` | `64E8453DEE5297E4` |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | Patt | Patt | `7AC8B5574DE21417` | `68F33BE69B8EDF6D` |
| Bitter Winter | China, GLA | China, Tick 17.220 | unverändert | `26CD35AC8AA660F5` | `2B86E2038EDFAAB7` |
| Bitter Winter | America, China | Patt | **America, Tick 33.750** | `38380B920A732955` | `5FED11D99834B1B6` |
| Bitter Winter | GLA, America | America, Tick 8.895 | unverändert | `AB725263AEF8E3B7` | `CDFCDF66730FF07B` |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | Patt | Patt | `9D040D6ACB8E269D` | `24F626B9E8F8CCA2` |
| Death Valley | China, GLA | China, Tick 8.565 | unverändert | `9F0F8D7D28E8654D` | `90CDEBE18E7C6B01` |
| Death Valley | America, China | America, Tick 9.330 | unverändert | `E98A532C90F89642` | `AC321A0CFA9C00E1` |
| Death Valley | GLA, America | America, Tick 8.175 | unverändert | `3F33A4FEDEBA85C1` | `3B00774A84E69278` |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | ChinaTankGeneral, Tick 32.475 | ChinaTankGeneral, Tick 31.770 | `FDAD90E9E139AFE9` | `6E2EE5C9DA09EFD1` |

Entschieden: vorher 11, nachher **12**; kein Sieger wechselt; sieben Läufe sind hashgleich (die Regel greift dort nie), alle 16 sind in Lauf 2 und nach der Snapshot-Fortsetzung identisch. Die vier verbliebenen Patts
(dreimal ChinaTankGeneral gegen AmericaAirForceGeneral, Armored Fury China/GLA) enden weiter mit Armeen aus der billigsten Infanterie vor einer Basisverteidigung. **Keine Balance-Aussage.** Unverändert, weil
das Referenzszenario und der Stresstest `SkirmishAi` nicht benutzen: Referenzmatch Sieger FactionGLA bei Tick 14.490, `23B4B25DF62765D8`, Kette `DE5849AF9F8493C4`; Stresstest `56882F763EABEF67` / `B8E663493F2C73D5`.

### 36.3 Heeresauswahl nach Wert und Reichweite: zwei Messrunden, zurückgenommen

Versucht wurde, `ProduceArmy` nicht mehr zufällig aus dem gerade Bezahlbaren wählen zu lassen, sondern je Produzent die wertvollste Einheit (Preis, verdoppelt, wenn die Waffe weiter reicht als jede gesehene
feindliche Basisverteidigung), Fabriken vor Kasernen, Sparen statt Kleinkauf. Abbruchkriterium der Sitzung: höchstens zwei Messrunden.

| Stand | 16 KI-Läufe entschieden | Was die Spur zeigt |
| --- | --- | --- |
| nur Versorgungsregel (§36.2) | **12** | – |
| Runde 1: Wert = Preis × Reichweitenbonus | 10 | Preis ist kein Kampfwert: America baut 18 `AmericaTankAvenger` (Flugabwehr, 2.000 Credits), die vor der Basis stehen und nicht auf sie schießen können; China baut `ChinaVehicleTroopCrawler` |
| Runde 2: nur Einheiten, deren Waffe Bodenziele trifft (`AntiGround`), keine Transporter | 11 | ein Patt endet (Armored Fury Tank/AirForce: ChinaTankGeneral bei 18.765), zwei Siege werden Patts (Armored Fury und Bitter Winter America/China) |

Der Nur-Befehle-Agent der Missionen (`docs/SCRIPT-ENGINE.md` §9) mit der Fassung aus Runde 2: bei 15.000 Ticks unverändert 1 von 24, bei 60.000 Ticks **2 statt 3** Karten (`MD_CHI02` geht verloren, `MD_CHI01`
endet bei Tick 20.733 statt 18.492). Die Änderung trägt also weder im Skirmish noch in den Missionen und ist **nicht im Code**. Was die Messung lehrt: der Preis taugt nicht als Maß, und Sparen hält die Armee
länger unter der Angriffsschwelle. Ein tragfähiger Ansatz braucht einen Kampfwert je Ziel (Schaden gegen Rüstung, wie `WeaponSet`/`ArmorSet` ihn hergeben) und Teams statt Einzelkäufen
(`AIPlayer::selectTeamToBuild`); das ist eine eigene, vermessene Arbeit hinter dem Programm aus `docs/MULTIPLAYER-PLAN.md`.

## 37. Phase 18 (6. Oktober 2026): Referenzmanifest, Eingangsmanifest und Prüfmodus des inkrementellen Hashs

Keine Änderung an der Simulation. Neu sind zwei Dateien unter `baselines/` und die Werkzeugbefehle, die sie schreiben und prüfen (`tools/CnCNew.SimTool`: `ReferenceManifest.cs`, `InputManifest.cs`,
`IncrementalHashCheck.cs`, `ManifestDocument.cs`, `CanonicalObjectHasher.cs`). Ab hier ist **das Manifest maßgeblich**; Hashwerte in dieser und anderen Dokumentationen sind Abschriften
(`docs/MULTIPLAYER-PLAN.md` §6 Regel 2). Jede Änderung an den beiden Dateien ist Review-Gegenstand wie `baselines/compatibility.json`.

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest check .local/phase2-import-a baselines/reference-manifest.json --set quick
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest check .local/phase2-import-a baselines/reference-manifest.json [--set full] [--only <id>[,<id>]] [--jobs N]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest check .local/phase2-import-a baselines/reference-manifest.json --set quick --verify-incremental
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest write .local/phase2-import-a baselines/reference-manifest.json [--set full]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest list .local/phase2-import-a
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest show .local/phase2-import-a --only <id> [--explain-incremental]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- input-manifest check .local/phase2-import-a baselines/input-manifest.json
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- input-manifest write .local/phase2-import-a baselines/input-manifest.json
```

### 37.1 Dateiform

Beide Dateien sind eine strenge Teilmenge von JSON (`ManifestDocument`): nur Texte, ganze Zahlen, Wahrheitswerte und Objekte; keine Arrays, keine Brüche, kein `null`. Die Schreibweise ist eindeutig: UTF-8 ohne
BOM, LF, zwei Leerzeichen je Ebene, Schlüssel auf jeder Ebene ordinal sortiert (Läufe stehen als Schlüssel in `runs`, also nach Id sortiert), Zahlen in invarianter Schreibweise, ein eigenes Escaping (nur `"`,
`\` und Steuerzeichen unter U+0020; nicht das der Laufzeit, vgl. Befund D9). Die Dateien enthalten keine absoluten Pfade, Zeitstempel, Rechnernamen oder Laufzeiten; Karten stehen als logische Inhaltspfade.
Der Leser lehnt alles außerhalb des Modells ab (auch doppelte Schlüssel und ein BOM). Zeiten stehen nur auf der Konsole (Fortschritt auf `stderr`, das Ergebnis je Lauf in Id-Reihenfolge auf `stdout`).

### 37.2 Läufe des Referenzmanifests

50 Läufe, davon 9 in der Stufe `quick`. `full` heißt „alle“; jede Zeile der Datei trägt ihre Stufe (`set`). `--only` wählt Läufe nach Id, unabhängig von der Stufe; unbekannte Ids sind ein Fehler
(Exitcode 2). Seeds sind die Standardwerte der jeweiligen `…Options.Standard`, Tickrate 30.

| Lauf (Id) | Art | Stufe | Parameter |
|---|---|---|---|
| `reference-match` | Retail-Referenzmatch (`ReferenceMatchScenario`) | quick | 13 Fraktionen, höchstens 216.000 Ticks |
| `stress` | synthetischer Stresstest (`StressScenario`) | quick | 216.000 Ticks, Zielbestand 10.500 |
| `<Karte>_<A>-<B>` (16) | KI gegen KI (`SkirmishMatchScenario`, wie `ai-match`) | 4 quick, 12 full | Alpine Assault, Armored Fury, Bitter Winter, Death Valley × China/GLA, America/China, GLA/America, ChinaTankGeneral/AmericaAirForceGeneral; `--max-ticks 45000` |
| `ai8_TwilightFlame`, `ai8_FortressAvalanche`, `ai8_IronDragon` | acht KIs | full | die ersten acht spielbaren Fraktionen in Katalogreihenfolge (was `ai-match --players 8` nimmt, im Manifest ausgeschrieben), 9.000 Ticks |
| `ai8_TwilightFlame_short` | acht KIs, gekürzt | quick | 3.000 Ticks |
| `skirmish-sweep` | alle Karten mit mindestens zwei Startpositionen, zwei KIs, 3.000 Ticks | full | 150 Karten, 64 gespielt |
| `mission_<Karte>[_<Fraktion>]` (27) | Mission über 3.000 Ticks (`MissionScenario`, wie `mission-run`) | 2 quick, 25 full | je Kampagne die erste und die letzte Mission |

- **Quick-Auswahl der KI-Läufe:** `AlpineAssault_GLA-America`, `BitterWinter_GLA-America`, `DeathValley_America-China`, `DeathValley_China-GLA` – die kürzesten entschiedenen, drei Karten, jede Grundfraktion
  mindestens zweimal. Quick-Missionen: `mission_MD_USA05`, `mission_MD_GLA05`.
- **Acht Spieler:** Twilight Flame (486 × 469 Zellen, die größte Karte mit acht Startpositionen), Fortress Avalanche (418 × 413) und Iron Dragon (531 × 282, anderes Seitenverhältnis). Death Valley hat ebenfalls
  acht Startpositionen, ist aber schon unter den Zwei-Spieler-Karten. 9.000 Ticks (fünf Spielminuten) sind eine Grenze der Laufzeit, kein Spielende: alle drei enden ohne Sieger. Ein solcher Lauf kostet hier
  220 bis 305 s (Aufzeichnung und drei Wiedergaben), bei 45.000 Ticks wären es je Lauf deutlich über 20 Minuten.
- **Missionen:** je Kampagne die erste und die letzte Mission ihrer Reihenfolge, bei nur einer Mission diese. Kampagnen, die dieselbe Karte mit derselben menschlichen Fraktion spielen, teilen sich den Lauf
  (`MD_USA_1_DEMO` und `USA` → `mission_MD_USA01`; der Parameter `campaigns` nennt alle). Challenge-Kampagnen laufen mit ihrer Persona (`HumanFaction`, `ChallengeSides.HumanSideFor`), deshalb neun verschiedene
  Läufe auf `GC_ChinaBoss`. Das ergibt 27 Läufe aus 17 Kampagnen. Die Karte von `MD_CAMPEA_DEMO` fehlt im Bestand; das steht als `unavailable` im Manifest und wird mitgeprüft.
- **Sweep:** ein Eintrag mit Zählern (64 gestartet und beendet, 0 mit Sieger, 0 fehlgeschlagen, 75 Karten ohne und 11 mit nur einer Startposition), Endtick/Endhash/Kette je Karte und einem SHA-256 über
  diese Zeilen (`combinedSha256`). Der Sweep spielt live und wird nicht wiedergegeben.

Die in `docs/HANDOFF-PHASE18.md` und §36.2 genannten Werte (Referenzmatch, Stresstest, 16 KI-Läufe, `MD_USA05`, `MD_GLA05`) stehen unverändert im Manifest.

### 37.3 Was je Lauf steht

`kind`, `set`, `parameters` (Karte, Fraktionen, Seeds, Tickgrenzen, Tickrate) und `results`. Für aufgezeichnete Läufe (Referenzmatch, Stresstest, KI): `endTick`, `outcome` (`winner`/`stalemate`/`none`),
`winner`, `victoryTick`, `finalHash`, `chainHash` (Kette über alle Tick-Hashes von Lauf 1), Zahl der Befehle und SHA-256 des Befehlsstroms (die Replay-Datei ohne Prüfpunkte – unterscheidet „die KI hat anders
entschieden“ von „die Welt hat anders gerechnet“), `identicalInRun2` (zweite frische Wiedergabe, Tick für Tick), `snapshotTick`/`snapshotBytes`/`snapshotSha256` und `identicalAfterSnapshotContinuation`
(Snapshot bei halber Länge, Wiederherstellung, Rest; verglichen mit Lauf 1). Für Missionen zusätzlich Skript- und Objektzähler, `saveTick`, `hashAtSave` (vor dem Speichern), `hashAfterRestore` (aus der
wiederhergestellten Sitzung) und zwei Wahrheitswerte:

- `identicalAfterSaveRestore`: gespeicherte und wiederhergestellte Sitzung laufen gleich weiter – das prüfen `mission-run` und das Kampagnen-Audit;
- `identicalToUninterruptedRunAfterSaveRestore`: beide gleichen außerdem Lauf 1, der nie gespeichert wurde, und der Hash am Speicherpunkt ist vor und nach dem Laden derselbe.

`write` schreibt, was gemessen wurde – auch einen falschen Wahrheitswert, mit Warnung. `check` rechnet alles neu und meldet je Lauf `OK` oder das **erste abweichende Feld** mit erwartetem und tatsächlichem
Wert, dazu abweichende Kopfwerte (`simulation`: Tickrate, `CommandTypes.Version`, Replay-, Snapshot-Versionen) und Läufe, die nur die Datei oder nur das Werkzeug kennt; Exitcode 1 bei jeder Abweichung.

### 37.4 Befund D8 bestätigt: fünf Missionen tragen einen veralteten inkrementellen Hash

In fünf der 27 Missionsläufe ist `identicalToUninterruptedRunAfterSaveRestore` **falsch** (so im Manifest festgehalten): `mission_MD_USA01`, `mission_MD_USA02`, `mission_MD_GLA03`, `mission_MD_GLA05`,
`mission_GC_LaserGeneral_ChinaNukeGeneral`. `identicalAfterSaveRestore` ist überall wahr. Ursache: ein Objekt ändert seinen Zustand, ohne dass die Welt es als geändert kennt; sein alter Hash bleibt in der
Summe, bis etwas alle Hashes neu rechnet – das tut `WorldSnapshot.Write` (`ComputeFullHash`). Die Sitzung hasht deshalb nach dem Speichern anders als ein Lauf, der nie gespeichert hat; die bisherigen Prüfungen
verglichen nur die beiden Fortsetzungen miteinander und sahen es nicht. Für Lockstep heißt das: zwei Rechner mit demselben Zustand melden verschiedene Hashes, sobald einer von ihnen einen Snapshot schreibt.
Die dokumentierten Missionshashes dieser fünf Karten (auch `MD_GLA05` `317B464E8BA98377`) sind Hashes mit veralteten Anteilen.

`--verify-incremental` ruft in Lauf 1 nach jedem Tick `SimulationWorld.VerifyIncrementalHash` auf (Stresstest: alle 1.000 Ticks; die Prüfung frischt die Zwischenstände auf, ein betroffener Lauf weicht danach
auch im Hash ab). `--explain-incremental` nennt zusätzlich Objekt und Felder: es beschreibt jedes Objekt mit `Entity.Describe` und vergleicht dessen Hash mit dem von der Welt gemerkten (`Entity.CachedHash`,
per Reflection gelesen – der einzige Zugriff auf Internes, kein Eingriff in die Simulation). Ergebnis (Stufe `quick` mit Erklärung, dazu die vier übrigen betroffenen Missionen einzeln):

| Lauf | Ticks mit Abweichung | Objekt, Modul, Feld |
|---|---|---|
| Referenzmatch, 4 KI-Läufe, `ai8_TwilightFlame_short`, `MD_USA05` (je Tick), Stresstest (216 Stichproben) | 0 | – |
| `mission_MD_GLA05` | 1 (nach Tick 854) | fünf `SupplyDock`: `SupplyWarehouseDockUpdate.boxes` (z. B. 400 → 740). Stelle: `SupplyWarehouseDockUpdate.SetCashValue` (`Modules/BuiltIn/DockModules.cs`) setzt `Entity.Dirty = true`, statt die Welt zu benachrichtigen – das Objekt steht nicht in der Dirty-Liste, und spätere `MarkDirty`-Aufrufe kehren wegen des gesetzten Flags sofort zurück. Aufrufer: Skriptaktion `WAREHOUSE_SET_VALUE` (`ScriptEngine.ExtendedActions.cs`) |
| `mission_MD_USA01` | 4 (ab Tick 58) | `AmericaVehicleChinook`: `TransportContain.contained[]`, `usedSlots` beim Einsteigen |
| `mission_MD_USA02` | 27 (ab Tick 1.338) | `CivilianBunker01`: `GarrisonContain.contained[]` (`id`, `since`) beim Besetzen; `AmericaCommandCenter`: `OCLSpecialPower.availableOnFrame` (0 → 2.485, Tick 2.455) |
| `mission_MD_GLA03` | 29 (ab Tick 65) | `CivilianBunker01`, `GreekShop`, `GreekHouse2/3`, `AmericaFireBase`: `GarrisonContain.contained[]` |
| `mission_GC_LaserGeneral_ChinaNukeGeneral` | 24 (ab Tick 37) | `GuardTower`: `GarrisonContain.contained[]`; `Lazr_AmericaParticleCannonUplink`: `ParticleUplinkCannonUpdate.pendingPath[]`, `pendingPathTick` (Tick 360) |

Gemeinsam ist den Fällen nach Augenschein, dass ein Skript den Zustand setzt (Besetzen, Einsteigen, Lagerwert, Spezialkraft, Strahlpfad); in keinem KI-Lauf der Stufe `quick` und nicht im Referenzmatch trat
eine Abweichung auf. Nur die Stelle in `SetCashValue` ist im Quelltext nachgelesen; die Aufrufwege der anderen vier Felder sind nicht untersucht. **Nicht behoben** (Auftrag dieser Arbeit: melden, nicht
ändern). Die Korrektur ändert die Hashes der fünf Missionen und gehört in einen eigenen Commit mit neuem Manifest; danach müssen die fünf Wahrheitswerte wahr sein. Nicht mit Erklärung gelaufen: die 12 KI-Läufe,
die drei langen Acht-Spieler-Läufe, der Sweep und die übrigen 21 Missionen der Stufe `full` (deren Fortsetzung nach dem Laden gleicht dem ununterbrochenen Lauf, was einen veralteten Hash am Speicherpunkt bei
Tick 1.500 ausschließt, nicht danach).

### 37.5 Eingangsmanifest

`baselines/input-manifest.json` beantwortet bei einer Abweichung zwischen zwei Rechnern zuerst die Frage, ob schon die Eingänge verschieden sind.

| Abschnitt | Inhalt | Quelle |
|---|---|---|
| `definitions` | SHA-256, Länge und Zahl der Definitionen der Retail-Basisdaten (Karten-Overlays ausgeschlossen) | `DefinitionSnapshotWriter.ComputeFingerprint` |
| `modelGeometry` | Hash der Knochentabelle (Flugplatz-Layouts aus den Modellen), Zahl der Flugplätze | `ModelGeometry.Fingerprint`, gebaut wie im Client (`ModelGeometryLoader`) |
| `worldFingerprint` | was Welt, Replay und Speicherstand prüfen: Definitionen + Knochentabelle | `ModelGeometry.Combine` |
| `catalogs` | je Katalog (`templates`, `weapons`, `upgrades`, `specialPowers`, `sciences`, `commands`, `factions`, `crates`, `creationLists`) und für den ganzen `TemplateCatalog` (`whole`, samt der Globalwerte aus `GameData.ini`) ein Inhalts-Hash, die Zahl der Einträge und der beschriebenen Werte | `CanonicalObjectHasher` |
| `files` | Größe und SHA-256 von `Campaign.ini` und `ChallengeMode.ini` | Content-Manifest |
| `maps` | je Karte (150): Größe und SHA-256 der Datei laut Content-Manifest, Gelände-Fingerabdruck, Zellen, Zahl der Startpositionen | `ContentFileSystem.GetEntry`, `TerrainMap.Fingerprint` |

Die Kataloge haben keine eigene serialisierte Form. `CanonicalObjectHasher` beschreibt deshalb den Objektgraphen selbst: Zahlen, Wahrheitswerte und Enums als Wert, `decimal` invariant, Brüche als Bitmuster,
Texte mit Länge; ein Struct ist seine Felder; ein Objekt ist der SHA-256 aus Typname und allen Instanzfeldern (auch privaten, auch der Basisklassen, ordinal nach Typ und Name sortiert – nicht in der
Reihenfolge der Reflection); Arrays und Listen behalten ihre Reihenfolge, Dictionaries werden nach der Beschreibung des Schlüssels, Mengen nach der des Elements sortiert. Speicheradressen, Hashcodes der
Laufzeit und Einfügereihenfolgen gehen nicht ein. Gemessen: 588.087 beschriebene Werte für den ganzen Katalog, drei unabhängige Prozesse liefern dieselbe Datei.

**Abgedeckt** ist damit alles, was `TemplateCatalog.Build` aus den Definitionen und der Knochentabelle macht (Vorlagen samt geparster Moduldaten und allem, was sie referenzieren, Waffen, Upgrades,
Wissenschaften, Spezialkräfte, Befehlssätze, Fraktionen, Kisten, Erzeugungslisten, Globalwerte).

**Nicht abgedeckt:**
- Code. Delegates werden nur gezählt (366: je Modulbindung Parser und Fabrik); welches Programm die Daten auswertet, steht in keinem der beiden Manifeste (Befund D5: es gibt keine Kennung des Codestands).
- Der Vergleicher eines Dictionaries (ordinal oder ohne Groß-/Kleinschreibung) und statischer Zustand.
- Die dekodierten Kartenobjekte, Skripte und Wegpunkt-Ids sowie ihre Wandlung nach `Fixed` (Befunde D2/D5): der Gelände-Fingerabdruck enthält sie nicht. Sie sind nur mittelbar erfasst – über den Dateihash
  und über die Missions- und KI-Läufe des Referenzmanifests.
- Die Dateihashes werden dem Content-Manifest entnommen, nicht neu berechnet (die Laufzeit prüft beim Öffnen nur die Länge; den Inhalt prüft `AssetTool verify`).
- Die synthetische Fixture des Stresstests (sie ist Quelltext).
- Die Overlay-Definitionen der Karten (`map.ini`): die Läufe benutzen wie der Client die Retail-Basisdaten.

### 37.6 Ausführung und Laufzeiten

Die Läufe sind unabhängig und werden von `--jobs N` Arbeitsthreads abgearbeitet (Standard: halbe Zahl der logischen Prozessoren), die schwersten zuerst. Jeder Arbeiter baut seine eigenen Kataloge (nichts aus
der Simulation wird zwischen Threads geteilt; das Kompilieren läuft nacheinander); alle Arbeiter müssen denselben Welt-Fingerabdruck erreichen, sonst bricht der Lauf ab. Die Ausgabe hängt nicht von der
Reihenfolge ab, in der Läufe fertig werden.

Gemessen am 6. Oktober 2026 auf einem Rechner mit 16 logischen Prozessoren, Release, `--jobs 6`, **nicht ruhig** (andere Prozesse liefen mit): `write --set quick` 283 s; `write --set full` 1.663 s (27,7 min;
davon etwa 13 Minuten zusammen mit einem zweiten eigenen Lauf); `check --set full` zweimal bestanden, 1.599 s (`--jobs 5`) und 1.438 s (24,0 min); `check --set quick` bestanden, 262 s;
`input-manifest write` und `check` je 13 s. `check --set quick --verify-incremental`: 268 s, 60.896 Ticks geprüft, eine Abweichung (`mission_MD_GLA05`, §37.4) und deshalb Exitcode 1 –
solange D8 offen ist, ist das der erwartete Ausgang dieses Modus. Längster Einzellauf ist `mission_MD_USA01`
(632 s für 3.000 Ticks in dreieinhalb Durchgängen).

### 37.7 Tests

`tests/CnCNew.Simulation.Tests/Tests.ReferenceManifest.cs` (drei Tests): kanonische Schreibweise und Rundreise, strenges Lesen, „erstes abweichendes Feld“ an einem synthetischen Manifest, Unabhängigkeit des
Inhalts-Hashs von Einfügereihenfolgen. `ManifestDocument.cs` und `CanonicalObjectHasher.cs` hängen nur von der BCL ab und werden als verknüpfte Quelldateien in das Testprojekt kompiliert (kein Verweis auf ein
ausführbares Projekt, keine neue Abhängigkeit). Die Läufe selbst prüft `check`; ein Test über den realen Bestand wäre derselbe halbstündige Lauf.

### 37.8 Grenzen

- Ein Rechner, ein Betriebssystem, eine .NET-Version: das Manifest ist die Messlatte für die Determinismus-Matrix (Auftrag 3 der Phase 18), nicht ihr Ergebnis.
- Missionen laufen 3.000 Ticks, Acht-Spieler-Läufe 9.000: spätes Spielgeschehen dieser Läufe ist nicht erfasst. Die 16 KI-Läufe decken bis zu 45.000 Ticks ab.
- Der Sweep und die Missionen sind keine Replays; ihr Befehlsstrom steht nicht im Manifest.
- `write --set quick` schreibt eine Datei nur mit den Quick-Läufen; die eingecheckte Datei wird mit `--set full` geschrieben.

## 38. Phase 22 (6. Oktober 2026): Festkomma Q39.24, Formate und neue Referenzwerte

Das vollständige Protokoll (Vorarbeit, Vergleich aller Ergebnisgrößen, Überlaufanalyse, Matrix, Leistung, Zähler bei 60 und 120 Hz) ist `docs/FIXED-POINT-Q39-24.md`. Hier steht, was am Kern gilt.

### 38.1 Numerik

- `Fixed`: `long`, **24 Nachkommabits** (`Fixed.FractionBits`), Auflösung 2⁻²⁴, Bereich ± 5,5 · 10¹¹. Rundung unverändert (zur nächsten Zahl, Halbwerte weg von null), Überlauf wirft.
  Neu: `Fixed.FromFraction(zähler, nenner)` für Konstanten, die ein Bruch sind, `Fixed.Epsilon` (ein Schritt). `ToDecimal()` ist bis 65.536 Einheiten exakt, darüber auf die 28 Stellen eines `decimal`
  gerundet (weit unter einem halben Rohschritt).
- `FixedMath` (`Numerics/FixedMath.cs`) trägt die Arithmetik auf Rohwerten mit der Bitzahl als Argument: Produkt, Quotient (schriftliche Division auf Maschinenworten für große Dividenden), Wurzel
  (64-Bit-Wurzel plus bitweise Erweiterung gegen das exakte 128-Bit-Quadrat), π und seine Bruchteile, Arkustangens-Tabelle, `Atan2` (CORDIC, ein Schritt je Nachkommabit), `Sin`/`Cos`
  (Taylor bis x¹³/x¹²). Konstanten mit 61 Nachkommabits, auf die Auflösung gerundet. Gemessener größter Fehler bei 24 Bit: `Sin` 1,0 · 10⁻⁷, `Cos` 1,2 · 10⁻⁷, `Atan2` 2,7 · 10⁻⁷ rad (bei 16 Bit:
  2,6 · 10⁻⁵, 2,4 · 10⁻⁵, 5,9 · 10⁻⁵). Die frühere Angabe im Quelltext („`Atan2` unter 10⁻³ rad“) war eine Schranke, kein Messwert.
- `FixedTrig.PiFraction(zähler, nenner)` für Winkelkonstanten (π/6 der Bewegung).
- **Das Quadrat eines Werts über 741.455 passt nicht** (die Wurzel der größten Zahl). Die Daten enthalten solche Werte (Reichweiten 999.999 und 1.000.000, „sofort“-Geschwindigkeiten 999.999, Gesundheit 99.999.999,
  `AbilityAbortRange` 10.000.000). Eine Reichweite, ein Radius oder eine Grenze wird deshalb nie mit `*` quadriert, sondern über **`SquaredReach`** (`Numerics/SquaredReach.cs`) mit einem Abstandsquadrat verglichen:
  `SquaredReach.Of(reach).Contains(d²)` ist `d² <= reach * reach`, `ContainsStrictly` ist `<`, mit dem Quadrat in 128 Bit – bitgleich zum alten Ausdruck, wo der rechnen konnte, und „immer innerhalb“, wo das
  Quadrat den Bereich verlässt. `Fixed.MultiplyDivide(a, b, c)` ist `a * b / c` mit beiden Rundungen für ein Produkt, das allein den Bereich verließe (Gesundheitsverhältnis). Der Katalogwert bleibt, was die
  Daten sagen; nichts wird beim Laden gekappt. Der Befund der Phase 22 ist damit behoben, kein Referenzwert hat sich geändert (`docs/FIXED-POINT-Q39-24.md` §5a). Regel: `Tests.SquaredReachSourceRules`
  (kein `x * x` in der Simulation außer Koordinatenkomponenten und 21 benannten Ausnahmen); Werkzeuge: `SimTool rate-audit <content> --ranges`, `SimTool range-audit <content>`.
- **Die Simulation ist nicht insgesamt `checked`**, nur die Operatoren von `Fixed`. Rohwertrechnungen stehen in `checked(…)` oder rechnen in 128 Bit (`VisionMap`, `StructureToppleUpdate`,
  `ConstructionHealthTarget`).
- **Sichtkreise** (`VisionMap`): der Test „Zellmitte im Kreis“ ist bei 16 Nachkommabits definiert (Mittelpunkt und Radius darauf gekürzt), gleich welche Auflösung `Fixed` hat. Das Quadrat eines Radius über
  46.340 Einheiten gilt als „größer als jedes Abstandsquadrat“ (`SquaredRadius`), was es für jede Zelle im Umkreis von 32.768 Einheiten ist; die Differenzen bleiben geprüft.
- **Leerlauf-Scan der Türme:** der Bruchteil eines Zugs sind die niedrigsten 16 Bit des Hashs als führende Binärstellen, weitere Stellen aus den Bits darüber.

### 38.2 Formate

`WorldSnapshot.Version` **10**, `ReplayFormat.CurrentVersion` **12**, `CommandTypes.Version` **15**, `MissionSnapshot.Version` **3**, `SaveGame.Version` **2**. Alle fünf tragen Rohwerte; ein Stand mit 16
Nachkommabits (9 / 11 / 14 / 2 / 1) wird an seiner Version mit klarer Meldung abgelehnt und nie umgedeutet (`Tests.StatesOfSixteenFractionBitsAreRejected`, `Tests.Scripting`, `CampaignTests`). Die
Versionsangaben in den Abschnitten davor sind Geschichte.

### 38.3 Referenzwerte (Messlatte: `baselines/reference-manifest.json`, Stand `d7fca91`)

| Größe | Wert | vorher |
|---|---|---|
| Referenzmatch (`ReferenceMatchOptions.Standard`, Treiber-Startwert jetzt `5EED0007`) | Sieger FactionGLA bei Tick **17.805**, Endhash `D0CA5F4EE456D6CE`, Kette `C5EE1090E1C24D7B` | bis Phase 21: Tick 14.490, `23B4B25DF62765D8` / `DE5849AF9F8493C4` (Startwert `5EED0005`); mit dem neuen Startwert bei 16 Bit: Tick 14.055, `AE9C688309DA37E4` / `FC6DFA759B1DCAF0` |
| Stresstest (216.000 Ticks) | Endhash `56882F763EABEF67` (leerer Endzustand, unverändert), Kette **`754FA4DCB9A41DF7`** | Kette `B8E663493F2C73D5` |
| 16 KI-Läufe | 12 entschieden; fünf mit anderem Ausgang als bei 16 Bit, ein Siegerwechsel (`docs/FIXED-POINT-Q39-24.md` §7.2) | 12 entschieden |
| 27 Missionsläufe, Skirmish-Übersicht, 8-Spieler-Läufe | Ausgänge wie zuvor; neue Hashes | – |
| Kampagne | Zielfahrer Sieg 24/24, Niederlage 24/24 (auf jeder Karte dasselbe Endskript im selben Tick wie bei 16 Bit); Audit 24/24 deterministisch und nach Speichern/Laden identisch, Endproben 13/24 und 15/24; Erreichbarkeit sauber 24/24 und 24/24 | dieselben Zahlen |
| Welt-Fingerabdruck der Eingänge | `49A55E310F780F60` | – |

Der Startwert des Referenzmatchs wurde in einem eigenen Commit gewechselt (`41c3bc0`), weil das Match mit dem alten bei 24 Bit nicht endet; die Erhebung über 36 Startwerte steht in
`docs/FIXED-POINT-Q39-24.md` §6. Die KI-Tabellen in §19, §32 und §36 und alle Hashes in den Abschnitten davor sind überholt.

### 38.4 Werkzeuge

- `SimTool reference-manifest results <content> --out <json> [--commands <dir>] [--set quick|full] [--only …]`: je Lauf, was kein Hash ist (Ausgang, Endtick, Objekte und Geld je Spieler, Befehle nach Typ,
  Ereignisse nach Art; Missionen: Ausgang, Tick und Name des Endskripts, Skriptzähler) – und auf Wunsch der Befehlsstrom als Text mit Mengen in ganzen Einheiten. Zwei Stände, die sich unterscheiden
  **sollen** (Auflösung, Tickrate), werden damit verglichen: Größe für Größe und über die erste abweichende Befehlszeile.
- `SimTool reference-match … [--world-seed HEX] [--driver-seed HEX] [--record-only]`: der Verlauf des Referenzmatchs unter anderen Startwerten.
- `SimTool rate-audit <content> [--tick-rate N] [--ranges [--ranges-above N]]`: läuft auch bei 30 Hz und listet den Wertebereich des Katalogs (größter Betrag, jeder Wert über der Wurzel der größten Zahl;
  mit `--ranges` den größten Betrag jedes Festkommafelds).
- `SimTool range-audit <content> [--frames N] [--distance N]`: jedes Retail-Objekt mit einer Waffe bekommt einen Feuerbefehl auf einen Panzer und einen Soldaten; listet jeden Lauf, der wirft, und die Waffen
  mit Werten über der Quadratgrenze.
- `Tests.FixedScaleSourceRules`: die Skala steht einmal – kein `65536`/`16777216`/`<< 16`/`<< 24` in irgendeinem Projekt (33 benannte Ausnahmen), außerhalb der Simulation nur
  `CnCNew.Presentation.FixedConvert`, kein Rohwert als Zahl.

## 40. Verteilungsvergleich zwischen Raten (Phase 23, 7. Oktober 2026): `ai-survey` und `rate-distribution`

Ein ganzes Match ist chaotisch (§38.3, `docs/FIXED-POINT-Q39-24.md` §7.2): eine Rundung im ersten Gefecht reicht für einen anderen Sieger. Zwei Tickraten lassen sich deshalb nicht Lauf für Lauf
vergleichen, sondern nur als **Verteilungen** über viele Startwerte (`docs/HANDOFF-PHASE23.md` Punkt 4). Dieser Abschnitt beschreibt das Werkzeug dafür. Es ist reiner Werkzeugcode
(`tools/CnCNew.SimTool/AiSurvey.cs`, `RateDistribution.cs`); an der Simulation und an `baselines/` ändert es nichts. **Die Erhebung selbst (640 Matches) ist hier nicht enthalten** – der Abschnitt legt
die Methode und die Grenzen fest, bevor Zahlen vorliegen.

### 40.1 Befehle

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-survey .local/phase2-import-a --out <30.tsv> --tick-rate 30 [--seeds 20] [--first-seed 1] [--minutes 25] [--pairings all|<id>,<id>] [--jobs N]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-survey .local/phase2-import-a --out <60.tsv> --tick-rate 60
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- rate-distribution <30.tsv> <60.tsv> [--out <bericht.md>]
```

- **`ai-survey`** spielt jede der 16 KI-Paarungen des Referenzmanifests (§37.2; die Tabelle kommt aus `ReferenceManifest.AiPairingRuns()`, dieselbe Quelle wie das Manifest) einmal je Startwert, **nur als
  Aufzeichnung** (keine Wiedergabe, keine Snapshot-Fortsetzung). Der Startwert wirkt genau wie bei `ai-match --seed N`: `AiSeed = N`, `WorldSeed = N ^ 0xC0FFEE06`. Das Budget ist **Spielzeit**:
  `MaxTicks = Minuten × 60 × Rate` (25 Minuten = 45.000 Ticks bei 30 Hz = das Budget des Manifests, 90.000 Ticks bei 60 Hz). `--jobs` arbeitet wie `reference-manifest check`: jeder Arbeiter baut eigene
  Kataloge für die Rate, nichts der Simulation wird zwischen Threads geteilt, alle Arbeiter müssen denselben Welt-Fingerabdruck erreichen.
- **Ausgabedatei** (TSV, LF, eine Kopfzeile, sortiert nach Paarung und Startwert, keine Zeitstempel, keine Wandzeit – zwei gleiche Aufrufe liefern dieselben Bytes, unabhängig von `--jobs`):
  `pairing`, `map`, `factionA`, `factionB`, `seed`, `tickRate`, `outcome` (`winner`/`stalemate`, wie die Sitzung es meldet; `failed`, wenn ein Match eine Ausnahme wirft – dann Exitcode 1),
  `winnerFaction`, `winnerSlot` (1 = erstgenannte Fraktion, 2 = zweite, 0 = keiner), `endTick` (Tick des Siegs, ohne Sieger der Tick des Abbruchs), `endGameMilliseconds`
  (`endTick × 1000 / tickRate`, ganzzahlig), `recordedTicks` (gespielte Ticks einschließlich der Nachlaufzeit nach dem Sieg; der Zustand von `finalHash`), `commands` (Zahl der aufgezeichneten Befehle),
  `finalHash` (Zustandshash der Welt am Ende der Aufzeichnung), `firstBarracksTickA/B` (erster Tick, nach dem die Seite ein fertiges Gebäude der Art `FS_BARRACKS` besitzt, −1 = nie),
  `firstAttackTickA/B` (erster Tick, nach dem `SkirmishAiStats.Waves` der Seite über 0 liegt – die erste Angriffswelle der KI, −1 = nie). Die vier Meilensteine werden nach jedem Tick nur gelesen.
  Der Fortschritt geht nach stderr, jede fertige Zeile mit ihrer Laufzeit (ein abgebrochener Lauf hinterlässt seine fertigen Matches im Protokoll).
- **`rate-distribution`** liest zwei Erhebungen (Referenz, Kandidat), **lehnt sie ab**, wenn sich die Mengen aus Paarung und Startwert, die Karten oder die Fraktionen unterscheiden oder eine Datei ein
  fehlgeschlagenes Match enthält, und druckt einen Bericht (Markdown) mit Statistik, Grenze und Urteil je Prüfung. Exitcode 0: keine Prüfung gescheitert; 1: mindestens eine gescheitert; 2: Eingabefehler.
  Eine **nicht prüfbare** Zeile (siehe e) ist weder bestanden noch gescheitert; der Bericht zählt sie getrennt, und sie macht den Exitcode nicht zu 1.

### 40.2 Die Grenzen (vorab festgelegt, nicht an Daten angepasst)

Jede Familie von Prüfungen ist zweiseitig auf dem Niveau α = 0,05. Innerhalb einer Familie mit k Prüfungen gilt Bonferroni (α/k je Prüfung), weil es unter beliebiger Abhängigkeit hält. Mit n Matches je Rate,
x entschiedenen bzw. gewonnenen Matches und den Indizes 30/60 für Referenz und Kandidat:

| | Größe | Prüfung | Grenze |
|---|---|---|---|
| a | Anteil entschiedener Matches, alle Paarungen zusammen | Zwei-Anteile-z-Test mit gepoolter Varianz: p̂ = (x₃₀ + x₆₀)/(n₃₀ + n₆₀), SE = √(p̂(1 − p̂)(1/n₃₀ + 1/n₆₀)), z = (p₆₀ − p₃₀)/SE | bestanden, wenn \|z\| ≤ 1,960 = Φ⁻¹(0,975) |
| b | Siegquote **je Fraktion** (Siege der Fraktion / Matches, an denen sie teilnimmt) | derselbe z-Test je Fraktion, Bonferroni über die k = 5 Fraktionen | bestanden, wenn jedes \|z\| ≤ Φ⁻¹(1 − 0,05/(2·5)) = 2,5758 |
| c | Dauer entschiedener Matches in Spielminuten, **geschichtet nach Paarung** | über die m Paarungen mit mindestens zwei entschiedenen Matches bei beiden Raten: dᵢ = x̄₆₀,ᵢ − x̄₃₀,ᵢ, vᵢ = s²₃₀,ᵢ/n₃₀,ᵢ + s²₆₀,ᵢ/n₆₀,ᵢ, D = Σdᵢ/m, SE = √(Σvᵢ)/m | bestanden, wenn \|D\| ≤ 1,960 · SE |
| d | Siegerverteilung je Paarung (A siegt / B siegt / unentschieden) | exakter Test der 2 × 3-Tafel nach Fisher, Freeman und Halton: Summe der Wahrscheinlichkeiten aller Tafeln mit denselben Randsummen, die höchstens so wahrscheinlich sind wie die beobachtete (vollständige Aufzählung); Bonferroni über 16 Paarungen | bestanden, wenn p ≥ 0,05/16 = 0,003125 |
| e | Dauer entschiedener Matches je Paarung | Welch-Test: t = (x̄₆₀ − x̄₃₀)/√(v₃₀ + v₆₀) mit v = s²/n, Freiheitsgrade ν = (v₃₀ + v₆₀)²/(v₃₀²/(n₃₀ − 1) + v₆₀²/(n₆₀ − 1)), zweiseitiges p = I_{ν/(ν + t²)}(ν/2, 1/2); Bonferroni über 16 Paarungen | bestanden, wenn p ≥ 0,003125; mit weniger als zwei entschiedenen Matches bei einer Rate **nicht prüfbar** |

Begründungen und Einzelheiten:

- **Warum b je Fraktion:** „erstgenannte“ und „zweitgenannte“ Seite bedeuten über Paarungen hinweg nichts. Grundfraktionen kommen in 8 Paarungen vor (160 Matches je Rate), die beiden Generäle in 4 (80).
  Die Prüfungen zweier Fraktionen, die gegeneinander spielen, sind abhängig (der Sieg der einen ist die Niederlage der anderen); Bonferroni braucht keine Unabhängigkeit.
- **Warum c geschichtet:** die Paarungen haben sehr verschiedene Dauern (im Manifest liegen die Siege zwischen Tick 7.815 und 42.840, also zwischen gut 4 und knapp 24 Spielminuten); ein Welch-Test auf den zusammengeworfenen Stichproben würde die Streuung zwischen Paarungen als Rauschen
  zählen und eine Verschiebung des Anteils entschiedener Matches je Paarung als Dauerunterschied missdeuten. Der Bericht druckt zusätzlich das Verhältnis der gepoolten Mittelwerte, nur zur Orientierung.
- **Familiengröße:** der Bericht teilt durch die Zahl der Paarungen bzw. Fraktionen **in der Datei** – bei der vollen Erhebung 16 und 5. Bei e zählen alle Paarungen, auch die nicht prüfbaren (die Zahl
  hängt damit nicht vom Ergebnis ab). Ein Teil-Lauf (`--pairings`) hat entsprechend eine schwächere Korrektur; maßgeblich ist nur die volle Erhebung.
- **Ohne Varianz:** enden alle Matches beider Erhebungen gleich, ist die Differenz 0 und z = 0 (bestanden); zwei Stichproben ohne Streuung bestehen e bei gleichen Mittelwerten und scheitern sonst.
- **Numerik** (`SurveyStatistics`, `double`, invariante Kultur): Φ über die Reihe Φ(x) = ½ + φ(x)·Σ x^(2k+1)/(1·3·…·(2k+1)) unter 3 und den Kettenbruch Q(x) = φ(x)/(x + 1/(x + 2/(x + …))) darüber; das
  Quantil durch Bisektion (keine Tabelle); ln Γ über die Stirling-Reihe nach Verschieben des Arguments über 15; die regularisierte unvollständige Betafunktion über ihren Kettenbruch (modifiziertes
  Lentz-Verfahren) auf der schnell konvergierenden Seite; die Tafeln von d über Logarithmen der Fakultäten.

**Was ein Scheitern bedeutet.** Wären beide Raten gleich verteilt, schlüge jede der fünf Familien mit höchstens 5 % Wahrscheinlichkeit falsch an; erwartet werden höchstens 0,05 falsche Zeilen unter den 5
von b, 0,05 unter den 16 von d und 0,05 unter den 16 von e. Über den ganzen Bericht sind das höchstens 0,25 erwartete Fehlalarme und höchstens 25 % Wahrscheinlichkeit für mindestens einen (22,6 % bei
unabhängigen Familien) – die Familien sind **nicht** gegeneinander korrigiert. Eine einzelne gescheiterte Zeile ist deshalb ein schwaches Indiz, eine gescheiterte Familie mit gleicher Richtung in einer
anderen ein starkes. Der exakte Test d ist diskret und strenger als sein Niveau: bei 20 Matches je Rate und wahrer Siegquote 0,5 verwirft er je Paarung mit Wahrscheinlichkeit 0,0011 statt 0,0031.

### 40.3 Was der Vergleich sehen kann: kleinste nachweisbare Unterschiede (Trennschärfe 80 %)

Berechnet (`rate-distribution` druckt die Tabelle zu jeder Erhebung; die Werte hier gelten für den Plan 16 Paarungen × 20 Startwerte = 320 Matches je Rate):

| Prüfung | n je Rate | Niveau | Ausgangswert | kleinste nachweisbare Änderung |
|---|---|---|---|---|
| a Anteil entschieden | 320 | 0,05 | 0,50 (größte Streuung) | ±0,110 (11 Prozentpunkte) |
| b Siegquote einer Grundfraktion | 160 | 0,01 | 0,50 | ±0,187 |
| b Siegquote eines Generals | 80 | 0,01 | 0,50 | ±0,259 |
| c Dauer, geschichtet | 16 × 20 entschieden | 0,05 | – | 2,802 · SE; bei gleicher Standardabweichung σ in allen Paarungen 0,221 σ |
| d Siegerverteilung einer Paarung | 20 | 0,003125 | 0,00 → | 0,52 |
| | | | 0,10 → | 0,69 |
| | | | 0,25 → | 0,86 |
| | | | 0,50 → | **nicht erreichbar** (auch 0,50 → 1,00 wird nur mit 75 % gefunden) |
| e Dauer einer Paarung | 20 / 10 / 5 entschieden | 0,003125 | – | 1,27 σ / 1,91 σ / 3,20 σ |

- a und b: Normalnäherung des z-Tests, nach der Änderung δ aufgelöst (Bisektion): Φ((δ − z_{1−α/2}·SE₀)/SE₁) ≥ 0,8 mit SE₀ unter der Nullhypothese (gepoolt) und SE₁ unter der Alternative.
- d: **exakte** Trennschärfe durch Aufzählung aller 21 × 21 Ausgangspaare zweier Binomialverteilungen (zwei Kategorien; eine dritte verteilt die Zählungen nur weiter), Suche in Schritten von 0,005; nach
  unten gelten dieselben Werte spiegelbildlich (1,00 → 0,48 usw.).
- e: (t_{1−α/2; 2n−2} + t_{0,8; 2n−2}) · √(2/n) in Standardabweichungen der Matchdauer innerhalb der Paarung (übliche Näherung der nichtzentralen Verteilung); der Bericht rechnet sie mit der gepoolten
  Standardabweichung der Referenz in Minuten um. c: (1,960 + 0,842) · SE mit dem SE der Daten.

**Ehrliche Lesart:** Zusammengefasst sieht der Vergleich eine Verschiebung des Anteils entschiedener Matches ab rund 11 Prozentpunkten und der Siegquote einer Grundfraktion ab rund 19. **Je Paarung sieht er
bei 20 Startwerten nur grobe Umschläge** (eine fast sichere Seite wird zur Münze oder umgekehrt) und Dauerunterschiede über einer Standardabweichung. Ein bestandener Vergleich heißt „kein Unterschied
mindestens dieser Größe gefunden“, nicht „die Raten sind gleich“.

### 40.4 Grenzen der Methode

- Startwerte sind nur der Nummer nach gepaart: derselbe Startwert ist bei zwei Raten nicht dasselbe Match. Die Prüfungen behandeln beide Erhebungen als unabhängige Stichproben.
- Unentschiedene Matches haben keine Dauer. Rutschen lange Matches über das Budget, zeigt sich das in a und d, nicht in c und e; die Dauerprüfungen sind dann zugunsten kürzerer Matches verzerrt.
- Die Matches einer Paarung teilen Karte und Eröffnung (der erste Kasernenbau fällt über Startwerte fast auf denselben Tick); die Streuung zwischen Startwerten stammt aus dem Gefecht. Die Erhebung misst
  die Verteilung über **diese** Startwerte und die vier Karten, nicht „das Spiel“.
- Die Tabelle hat nur fünf Fraktionen und vier Karten; eine Abweichung, die nur andere Fraktionen oder große Karten trifft, bleibt unsichtbar. Balance wird nicht bewertet, nur der Unterschied zwischen
  zwei Raten.
- Die Meilenstein-Spalten (Kaserne, erste Angriffswelle) werden erhoben, aber von `rate-distribution` **nicht geprüft**; sie dienen der Ursachensuche, wenn eine Prüfung scheitert.
- Der Kampagnenteil von Punkt 4 des Auftrags (Zielfahrer: Sieg/Niederlage je Karte, Zeitpunkt des Endskripts) ist nicht Teil dieser Werkzeuge.

### 40.5 Tests und gemessene Stichprobe

`tests/CnCNew.Simulation.Tests` bindet `RateDistribution.cs` als Quelltext ein (`Tests.RateSurvey.cs`, vier Tests, `-- --only "Rate survey"`): Normalverteilung gegen bekannte Werte und gegen das
Simpson-Integral der Dichte; ln Γ, Betafunktion und t-Verteilung gegen geschlossene Formen (I_x(1,b), I_x(a,1), I_x(2,3) = 6x² − 8x³ + 3x⁴, I_x(½,½) = (2/π)·asin √x; t mit 1, 2 und 4 Freiheitsgraden);
der exakte Test gegen von Hand ausgezählte Tafeln (2 × 3 mit Wahrscheinlichkeiten 1/6, 1/3, 1/3, 1/6; Fisher 2 × 2 mit Gleichstand 7462/C(24,12)); Welch von Hand; Rundlauf der Datei, Ablehnung
abweichender Mengen (fehlender Startwert, andere Startwerte, andere Karte, gemischte Rate, doppelte Zeile, fehlgeschlagenes Match) und die Urteile eines Berichts auf zwei kleinen synthetischen Erhebungen.

Stichprobe am 7. Oktober 2026 (Release, 2 Paarungen × 2 Startwerte, **keine Erhebung**): zwei Aufrufe mit verschiedenem `--jobs` liefern bei 30 Hz und bei 60 Hz jeweils bytegleiche Dateien;
`AlpineAssault_China-GLA` mit Startwert 1 endet in der Erhebung wie bei `ai-match … --max-ticks 45000 --seed 1` (Sieger FactionGLA bei Tick 9.930, 1.794 Befehle, 10.231 Ticks, Endhash `BDC0218A0BA5EE96`).
Laufzeit eines Matches über das volle Budget (ein Arbeiter, `ArmoredFury_America-China`, Patt): 13,2 s bei 30 Hz und 24,5 s bei 60 Hz; entschiedene Matches
(`DeathValley_America-China`, Startwerte 2 und 3): 6,0 s und 3,5 s bei 30 Hz (15.076 und 10.261 Ticks), 14,3 s und 7,1 s bei 60 Hz (49.351 und 27.571 Ticks). Dazu kommt einmal je Arbeiter der Katalogbau
(rund 12 bis 16 s, unter einer prozessweiten Sperre nacheinander). Nicht ruhig gemessen, nur zur Planung.
