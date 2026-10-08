# Raten-Invarianz: Zusammenführung der sieben Pakete (Phase 23, Paket CONSOLIDATION)

Stand 8. Oktober 2026, Ausgangsstand `b1a47b4` (alle sieben Korrekturpakete PROJECTILES, SCRIPTS, PHYSICS, TIMING, GROUND, FLIGHT, TURRETS sind auf `main`). Dieses Dokument
legt fest, wie die Bilder des Originals der einzelnen Objekte und Module zueinander liegen, was daran geändert wurde, und führt die Ursachen der Fehlschläge, die nach der
Zusammenführung der Pakete übrig waren. Die Dokumente der Pakete (`docs/RATE-INVARIANCE-*.md`) bleiben, wie sie sind; wo dieses Dokument ihnen widerspricht, gilt dieses.
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
3. **Wer im Entscheidungstick ein anderes Objekt oder ein anderes Modul desselben Objekts ansieht, liest dessen Lage am Ende des Bildes, das es bereits begonnen hat**
   (`Entity.FrameEndX/Y/Z` = Lage + noch nicht zurückgelegter Rest des Bildschritts, `Entity.AheadX/Y/Z`). Das ist genau die Tabelle aus 1.2: ein Beweger, der in diesem Tick
   schon dran war, hat sein Bild begonnen und zählt mit seiner Lage nach Schritt T; einer, der noch drankommt, hat sein voriges Bild beendet (Rest null) und zählt mit seiner
   Lage nach Schritt T − 1. Bei 30 Hz ist der Rest immer null, `FrameEnd` ist die Lage, und nichts wird je geschrieben.
4. **Ein Ereignis, das der Beobachter im Entscheidungstick an der Bildend-Lage auslöst (Tod durch `HeightDieUpdate`), setzt das Objekt auf diese Lage** (`Entity.CompleteFrame`):
   es stirbt dort, wo es bei 30 Hz stirbt. Der Beweger legt danach keinen Anteil dieses Bildes mehr zurück (der Rest am Objekt ist seine einzige Wahrheit über das, was fehlt).
5. **Phasen der Welt, die nach allen Updates alle Objekte ansehen und selbst nur alle paar Bilder laufen (Sicht), laufen nach dem letzten Tick ihres Bildes** und lesen die
   augenblickliche Lage; die ist dann bei jedem Objekt in Phase die Lage nach Schritt T. Wer Sicht liest, liest bis dahin den Stand des vorigen Bildes – nie einen, den es bei
   30 Hz zu dieser Spielzeit noch nicht gibt.
6. **Was je Tick läuft, liest die augenblickliche Lage** (Befehlsannahme, Zielerwerb, Feuerfreigabe, Schaden, die Kollisionsprüfung eines Fahrenden am Ende seines eigenen
   Bildes): Reaktion unter einem Bild, höchstens einen Bildschritt des Beobachteten vom 30-Hz-Wert entfernt.
7. **Ein neues Objekt gibt es am Ende des Ticks, der es angefordert hat; sein erstes Bild beginnt ein Bild (r Ticks) danach** – seine Phase ist der Tick seiner Entstehung.
   Module, deren erstes Update ein Ereignis entscheidet oder etwas erzeugt, laufen zum ersten Mal in diesem Tick (`SpawnTick + r`), nicht einen Tick nach dem Entstehen. Ein
   Objekt, das während der Erzeugungsphase angefordert wird (bei 30 Hz „am Ende des nächsten Ticks“), entsteht ein Bild später, nicht einen Tick (§3).
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
| D. gewählt: Regeln 1–8 | drei `Fixed` je Objekt, die nur Beweger oberhalb von 30 Hz schreiben; ein Beobachter addiert sie beim Lesen | bei 30 Hz: drei Felder mehr je Objekt, kein Schreibzugriff; ein Beobachter, der `FrameEnd` liest, zahlt drei Additionen von null. Geändert sind nur Beobachter, an denen ein Fehlschlag gemessen ist (§2); die Scans der Bodenbewegung im heißen Pfad (`StepBlocked`, `StandsStill`) lesen weiter die augenblickliche Lage (§6) | |
