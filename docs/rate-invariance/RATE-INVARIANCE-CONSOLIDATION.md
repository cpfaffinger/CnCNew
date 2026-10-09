# Raten-Invarianz: Zusammenführung der sieben Pakete (Phase 23, Paket CONSOLIDATION)

Stand 8. Oktober 2026, Ausgangsstand `b1a47b4` (alle sieben Korrekturpakete PROJECTILES, SCRIPTS, PHYSICS, TIMING, GROUND, FLIGHT, TURRETS sind auf `main`). Dieses Dokument
legt fest, wie die Bilder des Originals der einzelnen Objekte und Module zueinander liegen, was daran geändert wurde, und führt die Ursachen der Fehlschläge, die nach der
Zusammenführung der Pakete übrig waren. Die Dokumente der Pakete (`docs/rate-invariance/RATE-INVARIANCE-*.md`) bleiben, wie sie sind; wo dieses Dokument ihnen widerspricht, gilt dieses.
„Bild“ heißt Logikbild des Originals (1/30 s), „Tick“ ein Schritt der Simulation, r = Ticks je Bild (1, 2, 4). Werte stehen als 30 / 60 / 120 Hz.
**Nichts davon hat ein Mensch gesehen.**

## 1. Die Konvention

### 1.1 Das Problem

Jedes Paket zählt „das eigene Bild des Objekts“ für sich: je Modul (`FramePhase`, `subStep`, `frameTicksLeft`, `FlightFrameStep`, Turm-`SubSteps`), am Raster der Welt
(`IsOriginalFrameBoundary`: Skript-VM, Spectre, Zufallszüge, Dock- und Versorgungs-Automat) oder ab dem Entstehen (`SpawnTick`). Jedes für sich ist richtig: ein einzelnes Objekt
steht nach jedem seiner Bilder bitgleich dort, wo der 30-Hz-Schritt es hinstellt. Wo aber zwei Module eines Objekts oder zwei Objekte einander **ansehen**, war nicht festgelegt,
*in welchem Tick* angesehen wird und *welche Lage* der Beobachter dann liest. Gemessene Folgen am Ausgangsstand:

- `HeightDieUpdate` (sieht einmal je Bild nach) und das Geschoss desselben Objekts (entscheidet einmal je Bild) liegen im selben Tick, aber das Geschoss hat in diesem Tick erst
  1/r seines Bildschritts zurückgelegt: Neutronenrakete im Test 94 / 94 / 95 Bilder Sturzflug; Inferno-Granate Ablage 1,258 / 0,000 / 0,000, Weglänge 247,837 / 257,625 / 257,625.
- Mine (TIMING: vergleicht den Weg einmal je Bild *ab der ersten Berührung der Einheit*) und Bodenbewegung (GROUND: meldet Überlappung einmal je Bild *des Fahrenden*): zwei
  Raster für dieselbe Meldung; liegen sie auf verschiedenen Ticks, bleibt die Mine stumm (Test „Structures: mines detonate …“ bei 60 und 120 Hz).
- Der Schleier wird nach dem **ersten** Tick seines Bildes aufgefrischt: Leser der Sicht sehen eine Änderung bis zu (r − 1)/r Bild früher als bei 30 Hz (TURRETS, Befund 3).
- Objekte, die ein anderes Objekt erzeugt (Fahrer, Mob-Mitglieder, der eigene Sammler des Versorgungszentrums), erscheinen einen **Tick** nach ihrem Träger statt ein Bild.

### 1.2 Was bei 30 Hz gilt (die Definition)

In einem Tick T von 30 Hz laufen die Phasen Befehle → Updates (in der Reihenfolge Weckzeit, Objektkennung, Modulindex) → Zielerwerb/Türme → Ereignisse → Zerstören → verzögerte
Treffer → Erzeugen → Sicht. Daraus folgt, was jeder Beobachter sieht:

| Beobachter in Tick T | sieht ein anderes Objekt (oder Modul) |
|---|---|
| Befehl (Phase 1) | im Zustand nach Schritt T − 1 |
| Update, das **nach** dem Update des Beobachteten läuft (größere Kennung bzw. größerer Modulindex) | nach Schritt T |
| Update, das **vor** ihm läuft | nach Schritt T − 1 |
| Phasen nach den Updates (Zielerwerb, Türme, verzögerte Treffer, Sicht) | alle nach Schritt T |
| ein in Tick T angefordertes neues Objekt | gibt es am Ende von Tick T; sein erstes Update läuft in T + 1; ein während der Erzeugungsphase angefordertes erscheint am Ende von T + 1 |

### 1.3 Die Festlegung für r > 1

Das Bild f der Welt sind die Ticks r·f … r·f + r − 1. Ein Objekt, das an einer Bildgrenze entsteht oder befohlen wird, ist **in Phase** mit der Welt; eines, das mitten im Bild
einen Befehl bekommt, zählt seine Bilder ab diesem Tick (Befehlsannahme bleibt je Tick). Für jedes Objekt gilt, in seiner Phase:

1. **Entschieden wird im ersten Tick des Bildes.** Größen zweiter Ordnung, Zustandsautomaten und Ereignisse „einmal je Bild“ fallen auf den ersten Tick – in Sekunden der
   Zeitpunkt des 30-Hz-Ticks (so halten es alle sieben Pakete; die Zeitkonvention der Suite, `docs/SIMULATION-CORE.md` §39.1, verlangt es).
2. **Die Lage erreicht ihren 30-Hz-Wert mit dem letzten Tick des Bildes**; dazwischen liegt das Objekt auf der Geraden dorthin (Unterteilung, ein Anteil je Tick).
3. **Wer ein anderes Objekt oder ein anderes Modul desselben Objekts ansieht, um etwas zu entscheiden, liest dessen Lage am Ende des Bildes, das es bereits begonnen hat**
   (`Entity.FrameEnd(tick)` = Lage + noch nicht zurückgelegter Rest des Bildschritts, `Entity.AheadX/Y/Z`). Das ist genau die Tabelle aus 1.2: ein Beweger, der in diesem Tick
   schon dran war, hat sein Bild begonnen und zählt mit seiner Lage nach Schritt T; einer, der noch drankommt, hat sein voriges Bild beendet (Rest null) und zählt mit seiner
   Lage nach Schritt T − 1. Für Objekte in Phase ist die Antwort in jedem Tick eines Bildes dieselbe wie die des 30-Hz-Schritts; sie kippt nicht mitten im Bild. Bei 30 Hz ist
   der Rest immer null, `FrameEnd` ist die Lage, und nichts wird je geschrieben. Ein Rest, den sein Beweger weder in diesem noch im vorigen Tick angefasst hat (angehalten,
   gelähmt, schlafen gelegt), zählt nicht mehr (`Entity.AheadTick`). Der Beweger selbst rechnet weiter mit seiner eigenen Lage.
4. **Ein Ereignis, das der Beobachter im Entscheidungstick an der Bildend-Lage auslöst (Tod durch `HeightDieUpdate`), setzt das Objekt auf diese Lage** (`Entity.CompleteFrame`):
   es stirbt dort, wo es bei 30 Hz stirbt. Der Beweger legt danach keinen Anteil dieses Bildes mehr zurück (der Rest am Objekt ist seine einzige Wahrheit über das, was fehlt).
5. **Phasen der Welt, die nach allen Updates alle Objekte ansehen und selbst nur alle paar Bilder laufen (Sicht), laufen nach dem letzten Tick ihres Bildes** und lesen die
   augenblickliche Lage; die ist dann bei jedem Objekt in Phase die Lage nach Schritt T. Wer Sicht liest, liest bis dahin den Stand des vorigen Bildes – nie einen, den es bei
   30 Hz zu dieser Spielzeit noch nicht gibt.
6. **Was nicht umgestellt ist, liest die augenblickliche Lage** (§6 nennt die Stellen: der Blick des Zielerwerbs, Schaden im Radius, Dock, Einsteigen, die Kollisionsprüfung
   eines Fahrenden am Ende seines eigenen Bildes, alle Flug- und Sonderbeweger, die ihren Rest nicht veröffentlichen): höchstens einen Bildschritt des Beobachteten vom
   30-Hz-Wert entfernt. Befehle werden in jedem Tick angenommen; ein Objekt, das mitten im Bild der Welt befohlen wird, hat seine eigene Phase.
7. **Ein neues Objekt gibt es am Ende des Ticks, der es angefordert hat; sein erstes Bild beginnt ein Bild (r Ticks) danach** – seine Phase ist der Tick seiner Entstehung.
   Ein Objekt, das während der Erzeugungsphase angefordert wird (bei 30 Hz „am Ende des nächsten Ticks“), entsteht ein Bild später, nicht einen Tick. Ein Befehl, den ein
   Objekt in der Erzeugungsphase bekommt (Sammelpunkt), wird mit seinem ersten Bild ausgeführt. Ein Modul, dessen erstes Update ein Ereignis entscheidet oder etwas erzeugt,
   läuft zum ersten Mal in diesem Tick (`SpawnTick + r`). **Ausnahme, bewusst gelassen:** ein Modul, das ab dem Entstehen integriert oder seine Updates zählt, beginnt weiter
   mit dem Tick nach dem Entstehen (die Pakete und ihre Tests sind darauf gebaut; §6 „erstes Bild für alle Module“ nennt, was die allgemeine Regel gemessen kostet).
8. **Eine Meldung hat ein Raster, das des Meldenden.** Wer eine Meldung je Bild empfängt (`ICollideModule.OnCollide` von einem Fahrenden), führt kein eigenes Raster dafür.

Die Phase gehört damit dem **Objekt** (Tick der Entstehung bzw. des Befehls, der es in Bewegung setzt), nicht dem Modul: zwei Module eines Objekts, die beide „ab dem Entstehen“
oder beide „ab dem Schuss“ zählen, entscheiden im selben Tick und in der Reihenfolge ihres Modulindex, wie bei 30 Hz. Ein eigener Phasenzähler eines Moduls ist nur noch die
Buchführung dieser einen Phase (er wird nicht in einen gemeinsamen Zähler am Objekt umgebaut: das wäre ein Umbau aller Beweger ohne gemessenen Nutzen und änderte den Inhalt
jedes 60-Hz-Snapshots).

### 1.4 Verworfene Alternativen

| Alternative | Was sie bedeutet | Kosten | Warum verworfen |
|---|---|---|---|
| A. Alle Objekte am Raster der Welt (Bildphase erzwungen) | jede Entscheidung fällt auf `tick % r == 0`; ein Befehl mitten im Bild wartet bis zur nächsten Grenze oder beginnt mit einem Teilbild | im heißen Pfad keine; aber jeder Beweger der sieben Pakete müsste umgestellt werden | ein wartender Befehl ist der verbotene Ausweg „Originalraster“ für Bewegung und Befehlsannahme (bis zu r − 1 Ticks Latenz); ein Teilbild hat kein 30-Hz-Gegenstück und ist nicht bitgleich. Die Beobachtung innerhalb eines Ticks (wer war schon dran?) löst es ohnehin nicht – dafür bräuchte es zusätzlich Regel 3 |
| B. Beobachten im **letzten** Tick des Bildes, Lage „am letzten vollendeten Bild“ lesbar | Entscheidungen und Ereignisse fallen auf den letzten Tick; ein Beweger, der im Tick noch drankommt, wird mit seiner Lage vom Bildanfang gelesen | dieselbe Buchführung wie Regel 3 (ein zweites Lage-Tripel je Beweger), dazu jede Entscheidung (r − 1) Ticks später | verschiebt jede Zeit der Suite um (r − 1)/r Bild gegen ihre Zeitkonvention („was im Schritt des Befehls geschieht, hat die Zeit 0“): alle `ExactTime`-Größen der sieben Pakete fielen heraus |
| C. Reihenfolge: erst alle Beweger, dann alle Beobachter | ein zweiter Durchlauf je Tick | der Scheduler weckt Module einzeln; ein zweiter Durchlauf verdoppelt die Arbeit auch bei 30 Hz und ändert dort die Reihenfolge | ändert 30 Hz |
| D. gewählt: Regeln 1–8 | drei `Fixed` und ein Tick je Objekt, die nur Beweger oberhalb von 30 Hz schreiben; ein Beobachter addiert sie beim Lesen | bei 30 Hz: vier Felder mehr je Objekt, kein Schreibzugriff. Die Scans der Bodenbewegung im heißen Pfad (`StepBlocked`, `StationaryBlockersAhead`) und die Reichweitenfrage verzweigen einmal je Aufruf in eine Kopie mit der Bildend-Sicht, die bei 30 Hz nie läuft; je Kandidat kommt bei 30 Hz nichts dazu. Geändert sind nur Beobachter, an denen ein Fehlschlag oder die erste Abweichung eines Gefechts gemessen ist (§2, §5) | |

## 2. Ursachen, je Zeile ein Commit

Werte 30 / 60 / 120 Hz. „Suite“ ist die Raten-Äquivalenz-Suite (`docs/SIMULATION-CORE.md` §39), Zahlen sind „Größen außerhalb der Toleranz bei 60 Hz / bei 120 Hz“ der genannten
Gruppe unmittelbar vor und nach dem Commit. Nach jedem Commit, der die Simulation ändert: `input-manifest check` identisch und `reference-manifest check --set quick` 9 Läufe
identisch (§8).

| # | §5-Eintrag (`TICKRATE-MIGRATION.md`) bzw. Herkunft | Befund vorher | Korrektur (Datei, Symbol, Regel aus §1.3) | nachher | Commit |
|---|---|---|---|---|---|
| 1 | 5.2 `HeightDieUpdate` gegen die Geschosse (Pakete PHYSICS und PROJECTILES) | Test „the neutron missile flies the 30 Hz flight“ rot: Sturzflug 94 / 94 / 95 Bilder; Inferno-Granate Ablage 1,258 / 0,000 / 0,000, Weglänge 247,837 / 257,625 / 257,625 | `World/Entity.cs` (`AheadX/Y/Z`, `AheadTick`, `FrameEnd`, `SetAhead`, `CoverShare`, `CompleteFrame`); `DumbProjectileBehavior`, `MissileAIUpdate`, `NeutronMissileUpdate` veröffentlichen den Rest; `HeightDieUpdate` liest das Bildende und lässt das Objekt dort sterben (Regeln 3, 4) | 94 × 3; 1,258 × 3 und 247,837 × 3; `projectiles` 10 / 9 → 6 / 6 | `ad2b9e9` |
| 2 | 5.2 Nachführung der Geschosse auf ein fahrendes Opfer | das Geschoss las das Opfer einen Anteil weit im Schritt; 89 von 91 „moving victim: impact distance“ nicht bitgleich zu 30 Hz (innerhalb der Toleranz) | `AIUpdateInterface.AdvanceFrame` veröffentlicht den Rest des Bodenschritts; `DumbProjectileBehavior` (Start, Bahnanpassung) und `MissileAIUpdate` (Start, `FollowedVictim`) lesen `FrameEnd` (Regel 3) | zusammen mit #3: 89 → 2 nicht bitgleich; `projectiles` 6 / 6 → 6 / 10 (nur mit der alten Messung) | `910644d` |
| 3 | – (Messung der Suite) | „moving victim: impact distance“ las das Opfer im Tick des Aufschlags an seiner augenblicklichen Lage: ein Versatz unter einem Bild, den die Suite nicht hinzufügen soll (§39.1) | `RateCases.Projectiles.cs`: das Opfer wird am Bildende gelesen (`Entity.FrameEnd`; bei 30 Hz die Lage). Keine Toleranz, kein Fall, kein 30-Hz-Wert geändert | Inferno 31,400 / 31,067 / 30,900 → 31,400 × 3; `projectiles` 6 / 10 → 6 / 6 | `43443f5` |
| 4 | 5.3 (Art „erstes Update einen Tick nach …“): Neutronenexplosion | `NeutronMissileSlowDeathBehavior.BeginSlowDeath` weckte „im nächsten Tick“: alle Druckwellen ein halbes bzw. drei Viertel Bild zu früh; fahrendes Opfer 119,250 / 118,583 / 118,250 | `AirWeaponModules.cs`: ein Bild nach dem Tod | 119,250 / 118,917 / 118,750 (innerhalb; der Rest ist die Lage eines Opfers, das mitten im Bild stirbt); `projectiles` 6 / 6 → 4 / 4 | `ee4ef91` |
| 5 | 5.4 `MinefieldBehavior` (Paket TIMING) gegen `ResolveFootprintCollisions` (Paket GROUND) | Test „Structures: mines detonate …“ rot bei `--tick-rate 60` und `120`: zwei Raster für eine Meldung, die Mine blieb stumm | `MineModules.cs`, `OnCollide`: kein eigenes Raster mehr, die Meldung kommt je Bild des Fahrenden (Regel 8); der Zustand `phase` entfällt | Test grün bei 30 / 60 / 120 Hz; `timed` unverändert, Minenfälle in Schaden und Ereignissen gleich | `0136733` |
| 6 | – (Messung der Suite) | drei Sichtfälle fragten nach jedem Tick, ob eine Stelle gesehen wird, und nahmen diesen Tick als Zeit; die Sicht ist ein Zustand des Bildendes | `RateScene.StepUntilFrameEnd`, `RateCases.Timed.cs`: am Bildende gelesen, ab dem ersten Schritt des Bildes gezählt (die Form von `a6fdfea`) | 0,0000 s bei jeder Rate vor und nach #7 (ohne die Berichtigung nach #7: 0,0000 / 0,0167 / 0,0250) | `f55c16d` |
| 7 | neu (Paket TURRETS, Befund 3): Auffrischung der Sicht | nach dem ersten Tick ihres Bildes; Leser sahen eine Änderung bis zu (r − 1)/r Bild früher | `SimulationWorld.Step`/`StepObserved`, `_visionPhase`: nach dem letzten Tick des Bildes (Regel 5) | Auffrischung für ein Objekt aus Bild 1: Ticks 3 / 6 / 12 → 3 / 7 / 15; alle Gruppen unverändert | `a8eed4a` |
| 8 | – (ohne Verhaltensänderung) | `HeightDieUpdate` zählte sein Bild ab dem eigenen ersten Update (`FramePhase`) | `EntityFrames.BeginsFrameOf` (neue Datei `Time/FrameGrid.Consolidation.cs`): die Phase ist der Tick der Entstehung | dieselben Ticks; der Zustand `framePhase` entfällt | `f0770fe` |
| 9 | 5.3 `ProcessSpawns` | ein in der Erzeugungsphase angefordertes Objekt (Fahrer des Kampfrads, Aufbau des Overlord) erschien einen Tick nach dem Träger und stieg einen weiteren Tick später ein; Kampfrad „Zeit bis Höchsttempo“ 2,0333 / 2,5167 / 1,3250 s | `SimulationWorld.ProcessSpawns` (`PendingSpawn.NotBefore`, nur über 30 Hz gesetzt und geschrieben); `ContainChainModules.EnterPayload` sieht in den Bildticks des Trägers nach (Regel 7) | 2,0333 / 2,0500 / 2,0583 s; `locomotion` 12 / 12 → 4 / 3 | `f8ce7c4` |
| 10 | 5.3 `Register` (erstes Update): `SpawnBehavior` | der eigene Sammler des Versorgungszentrums und die Mob-Mitglieder entstanden ein halbes bzw. drei Viertel Bild zu früh; Laster-Einkommen in 5 min 6000 / 6300 / 6300 | `SpawnModules.cs`, `OnSpawn`: erstes Update ein Bild nach dem Entstehen | 6000 × 3; Chinook erste Lieferung 31,6333 × 3; `economy` 7 / 10 → 4 / 5, `locomotion` 4 / 3 → 2 / 2 | `21a6f7d` |
| 11 | 5.3 (TIMING, §5 „drei Einzelfälle“) | leerer Fallschirm „Tötung bis Entfernung“ 3,0000 / 2,9833 / 2,9750 s, Mob-Mitglied ohne Nexus 7,0000 / 6,9833 / 6,9750 s | `FlightModules.cs` `ParachuteContain.Update`, `SlaveModules.cs` `MobMemberSlavedUpdate.Update`: nur das Aufgeben wartet auf das erste Bild des Objekts | 3,0000 × 3 und 7,0000 × 3; `timed` 3 / 3 → 1 / 1 | `9ba608c` |
| 12 | neu (Bodenbewegung, mit `rate-compare --ai-diff` gefunden) | ein Tempo je Bild unter einem Rohwert je Tick (hier 1 Rohwert beim Bremsen) ging über 30 Hz verloren: `FrameSpeed()` nahm das veröffentlichte Tempo 0 für „angehalten“; Folgebild 1 Rohwert zu langsam | `Phase5Modules.cs`, `FrameSpeed`; das Modul löscht `_frameSpeed`, wo es selbst anhält | Tempo je Bild in Rohwerten, Bilder 2253–2255: 1864136 / 1 / 1864136 bei 30 Hz; bei 60 Hz vorher 1864136 / 0 / 1864135, nachher wie 30 Hz | `3f046f8` |
| 13 | 5.2 (GROUND, offen: „zwei fahrende Objekte, die sich begegnen“) | `StepBlocked` und `StationaryBlockersAhead` lasen einen Beweger, der im Tick schon dran war, einen Anteil weit; Sammler-Umlauf 27,4000 / 27,3333 / 27,4000 s | `SimulationWorld.Collision.cs`: `StepBlockedAtFrameEnd`, `StationaryBlockersAheadAtFrameEnd`, `StandsStillAtFrameEnd` (über 30 Hz; Regel 3; wer den letzten Schritt seiner Fahrt zurücklegt, steht) | alle Sammlergrößen bei jeder Rate gleich; `economy` 4 / 5 → 0 / 0; neuer Drei-Raten-Test (acht Läufer, bitgleich über 400 Bilder) | `2db1fc6`, Test `305da79` |
| 14 | 5.3 `Register`: Befehl in der Erzeugungsphase | eine mit Sammelpunkt produzierte Einheit fuhr einen Tick nach dem Erscheinen los und stand fortan einen Anteil vor allen Objekten in Phase | `SimulationWorld.DeferSpawnPhaseOrders` (über 30 Hz; Regel 7) | erstes Bild mit abweichender Lage im Gefecht USA gegen China: 1140 (38,00 s) → 3467 (115,57 s); `flight` 0 / 4 → 0 / 3 | `cc7d92e` |
| 15 | 5.4 `StepAcquisition`/`AutoFire` (Lage beim Angriff) | Verfolgungsziel, „in Reichweite“ und Schuss wurden an einer Lage mitten im Bild entschieden | `SimulationWorld.Acquisition.cs` `Engage`, `WeaponSlots.cs` `WithinWeaponRange`, `Projectiles.cs` `FireWeaponAt`: Bildend-Sicht über 30 Hz; wer in Reichweite kommt, hält am Bildende (Regeln 3, 4) | erstes Bild mit abweichender Lage 3467 → 3537 → 3540 → 3543 (118,10 s); Suite unverändert (`weapons` 0 / 0, `turrets` 4 / 0, `projectiles` 4 / 4) | `04e2fc8` |
| 16 | 5.6 Heilung in Scheiben: `PropagandaTowerBehavior` | „Bilder mit Heilung“ 750 / 751 / 751, volle Gesundheit 26,8667 / 26,9000 / 26,9000 s | `FieldEffectModules.cs`, `Give`: die Scheibe des Originals einmal je Bild des Turms (Ausweg, §3) | 750 × 3, 26,8667 × 3; `overtime` 2 / 2 → 0 / 0 | `f05af4f` |

Werkzeug (kein Verhalten): `SimTool rate-compare <content> --ai-diff A,B [--rates 30,60] [--context N] [--dump DIR] [--state [--positions] [--frames N] [--watch ID --watch-from BILD]]`
(`d5ce353`, `551f11a`) – der erste abweichende Befehl bzw. das erste Bild, nach dem ein Objekt zwischen zwei Raten abweicht.

## 3. Verwendungen des Auswegs „Originalraster“ in diesem Paket

| Stelle | Was einmal je Bild läuft | Raster | Grund |
|---|---|---|---|
| `PropagandaTowerBehavior.Give` | die Heilscheibe (Bonus und Scan bleiben je Tick) | das des Turms (`BeginsFrameOf`) | das Original heilt einmal je Bild; Scheiben je Tick sind einzeln gerundet und summieren sich nicht zur Bildscheibe. Weder Bewegung noch Befehlsannahme noch Zielerfassung |
| `ContainChainModules.EnterPayload` | das Nachsehen eines Trägers, ob seine Erstbesatzung erschienen ist | das des Trägers | ein Zustandswechsel „ein Update nach dem Erscheinen“; nicht unterteilbar |
| `SimulationWorld.Step` (Sicht) | unverändert alle drei Bilder; nur die Phase im Bild ist neu (letzter Tick) | Welt | – |

Kein Ausweg, sondern die Sicht des 30-Hz-Schritts (Regel 3): `HeightDieUpdate`, die Scans der Bodenbewegung, `Engage`, `WithinWeaponRange`, `FireWeaponAt` laufen in jedem Tick bzw.
bei jeder Entscheidung wie zuvor und lesen nur eine andere Lage.

## 4. Die Gruppe `ai`: erste Abweichung, mit Zahlen

Fall „skirmish FactionAmerica against FactionChina“, 30 gegen 60 Hz, mit `rate-compare --ai-diff` (Befehlsstrom in Spielzeit) und `--state --positions` (Lage aller Objekte am Ende
jedes Bildes). Am Ausgangsstand waren der erste Befehl und der erste Bauauftrag gleich, 1.120 gegen 1.168 Befehle in acht Minuten.

| Stand | erster abweichender Befehl | erstes Bild mit abweichender Lage | Ursache der Abweichung an dieser Stelle |
|---|---|---|---|
| nach #11 (`9ba608c`) | Befehl 121, 113,4667 s (Angriffsziel 1927,66 gegen 1927,71) | nicht gemessen | die acht Infanteristen der ersten Welle Chinas weichen einander aus; `StepBlocked` las den Vordermann einen Anteil weit (#13) |
| + #12 allein | 113,4667 s | 1140 (38,00 s) | der Versorgungs-Chinook steigt einen Tick nach dem Erscheinen (Sammelpunkt-Befehl in der Erzeugungsphase, #14) |
| + #13, #14 ohne #12 | Befehl 135, 114,4333 s | 2255 (75,17 s) | Tempo je Bild von 1 Rohwert geht beim Bremsen verloren (#12) |
| + #12, #13, #14 (`cc7d92e`) | Befehl 158, 116,4333 s | 3467 (115,57 s) | Ziel einer Verfolgung aus der augenblicklichen Lage des Opfers (#15) |
| + Verfolgung am Bildende | Befehl 194, 119,9333 s | 3537 (117,90 s) | „in Reichweite“ kippt mitten im Bild, die Einheit hält einen halben Schritt früher (#15) |
| + Reichweite und Halt am Bildende | Befehl 182, 119,4667 s | 3540 (118,00 s) | das Ziel einer Rakete aus der augenblicklichen Lage (#15) |
| + Schuss am Bildende (`04e2fc8`, Endstand) | Befehl 182, **119,4667 s** | **3543 (118,10 s)** | der Kurs eines Schützen, der mitten in seinem Bild angehalten hat: die Rakete startet mit einem Zwischenwinkel (der Kurs gehört noch nicht zur Bildend-Sicht) |

Das Gefecht beginnt bei 117,9 s. Bis dahin – Aufbau, Wirtschaft, zwei Wellen im Anmarsch, 3.537 Bilder, alle Objekte – sind die Welten bei 30 und 60 Hz in der Lage **bitgleich**
(vorher ab Bild 1140 nicht mehr). Die verbleibende Abweichung ist **kein** berechtigtes Ansprechen unter einem Bild, sondern die nächste nicht umgestellte Lesestelle (§6); jede
weitere kostete bisher zwei bis siebzig Bilder. Die Zahlen der Gruppe (Befehle in drei Minuten usw.) bleiben deshalb außerhalb: 25 / 24 → 24 / 23. Nicht gemessen: 120 Hz, die
beiden anderen Paarungen.

## 5. Suite am Endstand

Alle zehn Gruppen, 4.307 Größen, Release, Realdaten, `--only RateEquivalence --rate-report` am Endstand der Simulation (`f05af4f`). „Vorher“ ist der Lauf am Stand `62fbb6a`
(alle sieben Pakete), „Start“ der erste Lauf der Suite (§39.6).

| Gruppe | Größen | Start 60 / 120 Hz | vorher 60 / 120 Hz | **nachher 60 / 120 Hz** | Größen mit Fehlschlag | Fälle mit Fehlschlag |
|---|---|---|---|---|---|---|
| `locomotion` | 837 | 152 / 211 | 12 / 12 | **2 / 2** | 2 | 1 |
| `flight` | 74 | 21 / 21 | 0 / 4 | **0 / 3** | 3 | 3 |
| `weapons` | 1.474 | 43 / 57 | 0 / 0 | **0 / 0** | 0 | 0 |
| `projectiles` | 743 | 54 / 103 | 10 / 9 | **4 / 4** | 4 | 2 |
| `turrets` | 216 | 28 / 36 | 4 / 0 | **4 / 0** | 4 | 1 |
| `overtime` | 418 | 80 / 84 | 2 / 2 | **0 / 0** | 0 | 0 |
| `economy` | 81 | 22 / 25 | 7 / 10 | **0 / 0** | 0 | 0 |
| `timed` | 383 | 53 / 56 | 3 / 3 | **1 / 1** | 1 | 1 |
| `scripts` | 15 | 4 / 5 | 0 / 0 | **0 / 0** | 0 | 0 |
| `ai` | 66 | 22 / 27 | 25 / 24 | **24 / 23** | 26 | 3 |
| **Summe** | **4.307** | **479 / 625** | **63 / 64** | **35 / 33** | **40** | **12** |

Ohne die Gruppe `ai` sind es 11 Größen bei 60 Hz und 10 bei 120 Hz in 9 Fällen; jede steht in §6. Bei jeder Rate ungemessen bleiben 207 Größen (unverändert die Fälle aus §39.5;
`timed` 36 → 34).

## 6. Was noch fehlschlägt oder offen ist, mit Einordnung

| Was | Werte 30 / 60 / 120 Hz | Einordnung | Grund, Vorschlag |
|---|---|---|---|
| `locomotion`, Mob-Nexus: Kurszeit, Kursweg (2 / 2) | 23,0667 / 25,2167 / 25,0250 s; 400,062 / 439,291 / 435,284 | später behebbar | der Nexus fährt um seine Mitglieder; `MobMemberSlavedUpdate` liest Lage und Ziel des Nexus augenblicklich und zählt ab dem Tick nach dem Entstehen. Bildend-Sicht dort (Regel 3) |
| `projectiles`, Streusalven (4 / 4) | Aufschläge von 40: Artillerie-Plattform 8 / 7 / 7, Schlachtschiff 21 / 20 / 20 | später behebbar, nicht nachverfolgt | vierzig Geschosse im selben Tick; die erste Detonation trifft die Nachbarn im Flug (`DetonateProjectile` liest den Radius an der augenblicklichen Lage) |
| `turrets`, Sentry-Drohne (4 / 0) | Winkel beim ersten Schuss 1,57080 / 1,55771 / 1,57080 | später behebbar, nicht untersucht | `DeployStyleAIUpdate`: Status bei 60 Hz in Tick 65 statt 64 (Befund TURRETS); der Auslöser des Tors ist nicht gefunden |
| `flight`, nur 120 Hz (0 / 3) | MiG Landebefehl bis Aufsetzen 14,6333 / 14,6333 / 14,5667 s; Raptor „geparkt und nachgeladen“ 60,8000 / 60,8000 / 60,5333 s; Comanche „Kehre bis Ankunft“ 13,3333 / 13,3667 / 13,3833 s | später behebbar (größerer Umbau) | Flügelflug und `JetAIUpdate` rechnen an rund 35 Stellen mit `Wert je Tick × Ticks je Bild` und führen `Entity.Speed` je Tick; `LocomotorFrames` passt (die Tabelle trägt Tempo, Beschleunigung, Drehrate, Bremsen, Mindesttempo je Bild), verlangt aber ein eigenes Tempo je Bild im Modul wie bei der Bodenbewegung. Der Fallschirmabwurf („Flugzeug weg“ 49,2667 / 49,2500 / 49,2417 s) liegt seit #14 innerhalb |
| `timed`, `CINE_BShipWeaponDetonationObject` (1 / 1) | – / 0,0667 / 0,0667 s | **30-Hz-Fehler**, braucht eine Änderung bei 30 Hz | `LifetimeUpdate` (1 Bild) und `SlowDeathBehavior` (1 Bild) sind bei 30 Hz im selben Tick fällig; `WakeAt` verwirft die Weckbitte „nächster Tick“ eines noch wachen Moduls, dessen Update sieht die Frist nicht erreicht und schläft für immer: das Objekt endet nie. Über 30 Hz ist das Modul in diesem Tick nicht wach (es lief einen Tick nach dem Entstehen), die Bitte greift. Die Raten gleich zu machen hieße, den Fehler nachzubauen (Regel 7 für alle Module **und** „schon wach“ auf „innerhalb des nächsten Bildes“ erweitert; mit Regel 7 allein blieb der Fall gemessen bei – / 0,0667 / 0,0667). Vorschlag für einen eigenen Commit, der das Manifest ändern darf: `SlowDeathBehavior.Update` gibt den Rest bis zur Frist zurück statt `SleepForever` |
| `ai` (24 / 23) | Befehle in drei Minuten usw. | chaotische Folge einer behebbaren Ursache | §4: die erste Abweichung liegt jetzt im ersten Gefecht (118,1 s); davor bitgleich |
| Winkel am Bildende | – | später behebbar | `Entity.FrameEnd` kennt nur die Lage; ein Schütze, der mitten im Bild hält, behält einen Zwischenkurs (erste Abweichung des Gefechts, §4) |
| ein Objekt, das mitten im Bild stirbt | Neutronenrakete, fahrendes Opfer 119,250 / 118,917 / 118,750 | später behebbar | es bleibt an der augenblicklichen Lage liegen; bei 30 Hz stirbt es nach seinem ganzen Schritt. `CompleteFrame` beim Tod wäre die Regel 4 für alle |
| nicht umgestellte Leser (Regel 6) | – | später behebbar, jede Stelle einzeln | Blick des Zielerwerbs (`FindMoodTarget`), Schaden im Radius, Türme (`AimTurretsAt`), Dock und Einsteigen, `MobMemberSlavedUpdate`, `SlavedUpdate`, Spezialfähigkeiten, `SkirmishAi.Observe` (liest vor dem Schritt an einer Bildgrenze: schon richtig), Skript-VM (läuft im ersten Tick: liest Objekte in Phase einen Anteil weit) |
| nicht veröffentlichende Beweger | – | später behebbar | alle Flug- und Sonderbeweger (`FlightFrameStep`, `JetAIUpdate`, Fallschirm, Absturz, Zug, Lawine, Fähre, Welle, freier Fall) führen ihren Rest selbst; ein Beobachter sieht sie augenblicklich |
| Kollisionsprüfung eines Fahrenden | – | bewusst gelassen | `ResolveFootprintCollisions` läuft mit dem letzten Anteil (GROUND) an der augenblicklichen Lage: exakt gegen Stehende und gegen Fahrende, die im Tick vorher dran waren; ein späterer Fahrender ist (r − 1)/r seines Schritts weit. Ereignisse der Prüfung fallen auf den letzten Tick des Bildes (Mine weg 4,8000 / 4,8167 / 4,8250 s, innerhalb) |
| **erstes Bild für alle Module** | – | Entscheidung offen | probeweise gemessen (nicht übernommen): jedes Update eines neuen Objekts frühestens `SpawnTick + r`. Suite: `locomotion` 12 / 12 → 10 / 10, `economy` 7 / 10 → 4 / 5, aber `projectiles` 4 / 4 → 24 / 24 und `timed` 3 / 3 → 5 / 5, weil Module, die ihre Updates ab dem ersten zählen (`HeightDieUpdate` – inzwischen umgestellt –, `FirestormDynamicGeometryInfoUpdate`), und Integratoren (Flügelflug, Fallschirm, Chinook, dynamische Aufdeckung, Fähre) um r − 1 Ticks später liefen: 6 Drei-Raten-Tests und 6 bis 8 weitere Tests bei `--tick-rate 60`/`120` schlugen fehl. Die schmale Lösung (#9, #10, #11, #14) behebt dieselben Suite-Fälle ohne diese Tests. Folge der Ausnahme: ein Integrator, der mit dem Entstehen beginnt (ein steigender Chinook ohne Sammelpunkt, ein fallender Fallschirm), vollendet sein Bild im ersten statt im letzten Tick eines Bildes der Welt |
| Gesundheit einer Baustelle am Bildende | 5,997 / 8,495 (60 Hz) im ersten Bild der Arbeit | bewusst gelassen | „der erste Arbeitsschritt zählt ein ganzes Bild“ (TIMING): die Bauzeit ist exakt, der Zwischenstand liegt (r − 1)/r Bild voraus |
| Leistung | – | nicht gemessen | bei 30 Hz kommen je Objekt vier Felder dazu, in `HeightDieUpdate`, beim Geschoss-Start und in `WithinWeaponRange`/`FireWeaponAt`/`Engage` je ein Vergleich; die Scans der Bodenbewegung verzweigen einmal je Aufruf. Kein Vergleichslauf zweier Binärverzeichnisse |

Aus dem Auftrag geprüft, ohne Änderung: die Feuerwand des Drachenpanzers ist am Ausgangsstand dieses Pakets gleich (Schaden in 60 s 14.605,5 × 3, mit Upgrade 18.167,625 × 3,
Schüsse 838 × 3); der ECM-Panzer besteht in allen Größen (Winkel nach 180° −2,09440 × 3); die vier Baufälle sind exakt (Kraftwerk 15,2333 × 3, China 15,0333 × 3, GLA-Kaserne
14,6333 × 3); verkettete `StepUntil` wurden nicht systematisch durchgesehen (kein Fall der Gruppen `timed`/`turrets` schlägt deswegen fehl).

## 7. Die Helfer nebeneinander (nur Bestandsaufnahme, nichts geändert)

| Helfer (Datei) | Was | Rundung | Zählweise | Benutzer |
|---|---|---|---|---|
| `FrameGrid` (`Time/FrameGrid.cs`) | Dauern auf dem Raster: `FloorToFrame`, `CeilingToFrame`, `AtLeastOneFrame`, `DrawFrames`, `CountdownReload` | ganze Bilder | – | überall, seit Phase 20 |
| `FrameGridTiming.ZeroIsNextFrame` (`FrameGrid.Timing.cs`) | Frist 0 hinter einer Abfrage je Tick = ein Bild | – | – | Waffen-Nachladen, Kampfplan, `FireWeaponUpdate` |
| `ProjectileFrameGrid.FrameShare` (`FrameGrid.Projectiles.cs`) | Anteil eines Bildschritts | `Fixed`-Division Schritt / n; der **letzte** Anteil nimmt den Rest | `subStep` 0 … n − 1 (0 = Entscheidung), Zustand im Modul | `MissileAIUpdate`, `NeutronMissileUpdate`, `PhysicsBehavior`, Lenkbombe |
| `DumbProjectileBehavior.FlyToward` | Anteil zum Bahnpunkt | (Punkt − Lage) / verbleibende Anteile, neu je Tick; der letzte setzt den Punkt | wie oben | Granaten |
| `ProjectileFrameGrid.PerTickToPerOriginalFrame` | Tempo je Bild des 30-Hz-Katalogs aus dem Wert je Tick | Rekonstruktion über Dezimalzahl mit vier Stellen, sonst × n | – | Geschosse, `FlightTicks` |
| `FramePhysics.FrameSlice`/`FramePart` (`FrameGrid.Physics.cs`) | Anteil eines Bildschritts | `floor(Schritt · k / n) − floor(Schritt · (k − 1) / n)` (Schiebeoperation, rundet nach −∞), Summe exakt | `subTick` 1 … n über `FramePhase` (Zustand 0 … n − 1) | Umstürzen, Gebäudesturz, Zug, Battle Bus, Einsturz, Aufdeckung |
| `FramePhysics.PerTickAsPerOriginalFrame` / `…Squared…` | Katalogwert je Tick → je Bild | × n bzw. × n² (letzte Bits je Rate verschieden) | – | Paket PHYSICS |
| `FlightFrames.PerFrame`/`PerFrameSquared`/`PerTick` (`FrameGrid.Flight.cs`) | dasselbe; `PerTick` teilt den Rohwert | × n, × n²; Division schneidet zur Null ab | – | Flug, Jet, Fallschirm, Spectre, Chinook |
| `FlightFrameStep` | Rest eines Bildschritts | Anteil = Rohwert / n zur Null abgeschnitten; der **erste** Tick nimmt den Rest (das Objekt wird um n − 1 Anteile zurückgesetzt) | `left` n − 1 … 0, Zustand als Objekt im Snapshot | Flügelflug, Jet, Absturz, Fallschirm, Chinook, Spectre |
| `AIUpdateInterface.AdvanceFrame` (`Phase5Modules.cs`) | Anteil des Bodenschritts | `Roh · (i + 1) / n − Roh · i / n` mit Ganzzahldivision (zur Null), Summe exakt | `frameTicksLeft` n … 1, `frameDue` | Bodenbewegung |
| `LocomotorFrames` (`Templates/LocomotorFrames.cs`) | Locomotor-Konstanten je Bild | die 30-Hz-Umrechnung derselben Quellzahlen (statische Tabelle, Schlüssel Kataloginstanz); sonst × n, × n² | – | Bodenbewegung |
| `TurretInfo.RatesPerOriginalFrame`, `TurretState.SubSteps` | Drehraten je Bild; Anteil des Turmschritts | Tabelle wie oben; Anteil = Rest / verbleibende Teilschritte abwärts, der letzte setzt das Ziel | `SubSteps` | Türme |
| neu: `Entity.Ahead…`/`FrameEnd`/`CoverShare`/`CompleteFrame`, `EntityFrames.BeginsFrameOf` | der veröffentlichte Rest; die Bildticks eines Objekts | keine eigene Rundung (der Beweger liefert seine Anteile) | `AheadTick` | Beobachter (§2) |

**Vorschlag für später (ändert Ergebnisse bei 60/120 Hz, nie bei 30 Hz; eigener Schritt mit der Suite als Maß):** *ein* Anteilshelfer in der Form von `FramePhysics.FrameSlice`
(Summe exakt, vorzeichenrichtig, kein Zustand außer der Nummer des Ticks im Bild) und der Rest am Objekt (`Entity.Ahead…`) als einzige Buchführung dessen, was fehlt – dann
entfallen `subStep`, `FlightFrameStep`, `frameTicksLeft`/`frameStep` und `FramePhase` als je eigener Zustand, und jeder Beweger veröffentlicht von selbst. Für Konstanten je Bild
das Muster von `LocomotorFrames` (Tabelle neben dem Katalogtyp mit der 30-Hz-Umrechnung der Quellzahlen) statt `× n` und statt der Dezimal-Rekonstruktion.

## 8. Prüfläufe

| Lauf | Ergebnis |
|---|---|
| `input-manifest check` | identisch, nach jedem der 14 Commits, die die Simulation ändern (`f0770fe` zusammen mit `f8ce7c4` geprüft), und am Ende |
| `reference-manifest check --set quick --jobs 3` | 9 Läufe identisch, nach jedem dieser Commits (jeweils mit frisch gebautem `SimTool`; die drei reinen Mess- und Werkzeug-Commits `43443f5`, `f55c16d`, `d5ce353`/`551f11a` ohne eigenen Lauf) |
| `reference-manifest check --set full --jobs 4` | **50 Läufe identisch**, am Stand `305da79` (letzter Commit an der Simulation: `f05af4f`) |
| `dotnet build CnCNew.slnx` | 0 Warnungen, 0 Fehler (inkrementell, `bin`/`obj` von `CnCNew.Client.Stride` nicht gelöscht) |
| Simulationstests, Release, ohne Realdaten, 30 Hz | alle 495 grün (494 bestehende, 1 neuer) |
| dieselben mit `--tick-rate 60` | **alle grün** (am Ausgangsstand rot: „Structures: mines detonate …“) |
| dieselben mit `--tick-rate 120` | **alle grün** (am Ausgangsstand rot: derselbe Test; dazu bei jeder Rate „the neutron missile flies the 30 Hz flight“) |
| Simulationstests, Debug, 30 Hz (`dotnet run --project tests/CnCNew.Simulation.Tests`) | alle grün |
| Zielfahrer, 24 Kampagnenkarten (`CampaignTool drive <content> --map "/NAME.map" [--tick-rate N]`, je Karte ein Prozess, Kopie des Binärverzeichnisses) | 30 Hz: Sieg 24 / 24, Niederlage 24 / 24. 60 Hz: 24 / 24 und 24 / 24, auf jeder Karte dasselbe Endskript und dieselbe Spielzeit wie bei 30 Hz (höchstens ein Bild Abstand). 120 Hz: 24 / 24 und 24 / 24, ebenso (z. B. `MD_CHI04`: Sieg in Tick 48.258 / 96.516 / 193.032, dasselbe Bild) |

Zwischen `d5ce353` und `551f11a` war der Quelltexttest „Determinism rules hold in the simulation sources“ rot (eine Zahl ohne invariante Kultur in der Diagnose); in dieser Spanne
liegen `3f046f8`, `2db1fc6` und `cc7d92e`, deren übrige Tests grün waren.

Geänderte Testerwartungen: „Tick rate, world: the shroud is refreshed every three logic frames“ (Auffrischung nach dem letzten Tick des Bildes: Ticks 3 / 7 / 15) und „Turret
attack: a victim that falls into the fog ends the aim“ (wartet zwei Bilder statt zwei Ticks auf die erste Auffrischung), beide in `a8eed4a`. Neuer Test: „Rate invariance,
consolidation: movers that meet are where the 30 Hz movers are“ (`Tests.RateInvariance.Consolidation.cs`; schlägt ohne #13 bei 60 Hz nach Bild 15 fehl).

Nicht gemacht oder nicht geprüft: eigene Drei-Raten-Tests für #1 bis #11 und #14 bis #16 über die genannten hinaus (die Suite und die beiden vorher roten Tests tragen sie); die
erste Abweichung des Gefechts bei 120 Hz und für die beiden anderen Paarungen; Leistung; der Debug-Lauf mit Realdaten; Tests und Suite der Präsentation
(`Presentation.Tests`, `Game.Tests`) – die Bildend-Sicht ändert nur die Simulation oberhalb von 30 Hz. **Kein Mensch hat etwas davon gesehen.**
