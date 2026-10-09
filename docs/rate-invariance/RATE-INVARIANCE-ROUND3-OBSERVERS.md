# Raten-Invarianz, Runde 3: Beobachter (Phase 23, Paket ROUND3-OBSERVERS)

Stand 8. Oktober 2026, Ausgangsstand `37b36b8` (Simulation `3b44506`). Dieses Dokument setzt `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` fort: dieselbe Bildkonvention (§1.3 dort),
dieselbe Methode (erste Abweichung eines Gefechts suchen, den Leser benennen, auf die Konvention stellen, neu messen). „Bild“ heißt Logikbild des Originals (1/30 s), „Tick“ ein
Schritt der Simulation, r = Ticks je Bild (1, 2, 4). Werte stehen als 30 / 60 / 120 Hz. Parallel arbeiten zwei Pakete an anderen Dateien (FLIGHT-3: Flügelflug, Flug- und
Sonderbeweger, Wachdrohne; DEFECT: `CINE_BShipWeaponDetonationObject`/`WakeAt`); deren Fälle sind hier nicht angefasst. **Nichts davon hat ein Mensch gesehen.**

## 1. Ergebnis

Raten-Äquivalenz-Suite (`docs/SIMULATION-CORE.md` §39), Größen außerhalb der Toleranz bei 60 Hz / bei 120 Hz, von 4.307:

| Gruppe | Größen | vorher (`37b36b8`) | **nachher** | Rest gehört zu |
|---|---|---|---|---|
| `locomotion` | 837 | 2 / 2 | **0 / 0** | – |
| `flight` | 74 | 0 / 3 | **0 / 2** | Paket FLIGHT-3 (Flügelflug bei 120 Hz) |
| `weapons` | 1.474 | 0 / 0 | **0 / 0** | – |
| `projectiles` | 743 | 4 / 4 | **0 / 0** | – |
| `turrets` | 216 | 4 / 0 | **4 / 0** | Paket FLIGHT-3 (`DeployStyleAIUpdate`, Wachdrohne) |
| `overtime` | 418 | 0 / 0 | **0 / 0** | – |
| `economy` | 81 | 0 / 0 | **0 / 0** | – |
| `timed` | 383 | 1 / 1 | **1 / 1** | Paket DEFECT (30-Hz-Fehler) |
| `scripts` | 15 | 0 / 0 | **0 / 0** | – |
| `ai` | 66 | 24 / 23 | **0 / 0** | – |
| **Summe** | **4.307** | **35 / 33** | **5 / 3** | |

Die drei Gruppen des Auftrags bestehen bei 60 und 120 Hz: `ai` (alle 66 Größen der drei Paarungen sind bei 30, 60 und 120 Hz **gleich**, nicht nur innerhalb der Toleranz – z. B.
USA gegen China: 696 / 696 / 696 und 424 / 424 / 424 Befehle in acht Minuten), `locomotion` (Mob-Nexus) und `projectiles` (Streusalven). Darüber hinaus sind die drei Gefechte
der Gruppe `ai` über die ganzen acht Minuten (14.400 Bilder) **am Ende jedes Bildes in jedem beschriebenen Feld jedes Objekts gleich** zwischen 30 und 60 Hz und zwischen 30 und
120 Hz (§5; mit den dort genannten vier Ausnahmen der Vergleichsregel), und der Zufallsstrom der Logik läuft gleich.

## 2. Die Kernfrage: kann der Zielerwerb je Tick entscheiden und trotzdem das 30-Hz-Ergebnis liefern?

**Antwort: Er kann in jedem Tick laufen, aber für eine Einheit in Phase darf nur der erste Tick ihres Bildes etwas entscheiden – jeder weitere Tick des Bildes kann die Entscheidung des
ersten nur wiederholen oder von 30 Hz abweichen.** Das ist kein Befund über die Umsetzung, sondern folgt aus der Forderung selbst:

1. Bei 30 Hz entscheidet der Durchlauf (`StepAcquisition`) einmal je Bild, nach den Updates, und sieht dabei jedes Objekt nach seinem ganzen Schritt.
2. Liest der Durchlauf oberhalb von 30 Hz alles, was er ansieht, am Bildende (Lage `Entity.FrameEnd`, Kurs `Entity.FrameEndAngle`, „hat keinen Marschbefehl mehr“ im ersten Tick des
   letzten Bildes, Turm am Ende seines Bildschritts), dann sind seine Eingaben in allen r Ticks eines Bildes dieselben. Entscheidet er im ersten Tick wie 30 Hz, bleibt für die
   Ticks 2 … r nichts zu entscheiden: die Waffe ist nicht mehr bereit, das Opfer ist dasselbe, der nächste Blick liegt auf dem Raster.
3. Wo ein späterer Tick doch etwas anderes entscheidet, ist genau das die Abweichung. Gemessen am Ausgangsstand: eine neue Einheit wurde einen **Tick** nach dem Erscheinen zum
   ersten Mal durchlaufen (bei 30 Hz ist das ein Bild), merkte sich „untätig seit Tick 1“ und sah bei 60 Hz in Tick 5 nach – bei 30 Hz in Bild 3, das ist Tick 6. Die zufällige
   Verschiebung des folgenden Blicks wird aus der Bildnummer gezogen: Bild 2 statt Bild 3, also ein anderer Wert (nächster Blick der beiden Dozer 30 Hz: Tick 79; 60 Hz: Tick 105
   statt 158). Von da an lag **jeder** Blick jeder Einheit neben dem 30-Hz-Blick. Dasselbe nach jeder Ankunft: der Marsch endete im letzten Tick des Bildes, der Durchlauf sah
   „untätig“ einen Tick zu spät und plante den Blick auf einen ungeraden Tick.
4. Ein Schuss, der in Tick 2 eines Bildes fällt, ist kein „feineres“ 30-Hz-Verhalten, sondern ein anderes Spiel: sein Treffer tötet im selben Tick; eine Einheit mit höherer
   Kennung, die bei 30 Hz in diesem Bild das tote Opfer nicht mehr wählt, hat es bei 60 Hz in Tick 1 schon gewählt (und umgekehrt) – andere Schüsse, anderes Nachladen, anderer
   Verlauf.

**Umsetzung** (Commit „a unit decides its attack …“): `StepAcquisition` läuft weiter in jedem Tick über alle Einheiten; eine Einheit wird in den Ticks durchlaufen, mit denen
**ihre** Bilder beginnen (ganze Bilder nach dem Tick ihres Erscheinens – dieselbe Phase, die `EntityFrames.BeginsFrameOf` und die Beweger benutzen; bei 30 Hz jeder Tick), und
liest dabei die Bildend-Sicht. Das ist Regel 1 der Konvention („entschieden wird im ersten Tick des Bildes des Objekts“) für den Zielerwerb, nicht das Raster der Welt.

**Was je Tick bleibt:** Befehle. Ein Feuer-, Verfolgungs- oder Marschbefehl wirkt in dem Tick, in dem er angenommen wird (`ExecuteCommands`), auch mitten im Bild; ein
befohlener Schuss wird vom Befehl ausgelöst, nicht von diesem Durchlauf. Bewegung läuft je Tick. Türme entscheiden je Bild des Turms (Paket TURRETS) und schwenken je Tick.

**Was es kostet, und die Alternativen:**

| Variante | Was sie bedeutet | Kosten | Urteil |
|---|---|---|---|
| A. gewählt: Entscheidung im ersten Tick des Bildes der Einheit, Phase = Tick des Erscheinens | zustandslos (nichts Neues im Snapshot) | Eine Einheit, die **mitten in ihrem Bild** durch einen Befehl eines Menschen frei wird (Stopp, Ankunft eines mitten im Bild befohlenen Marschs), beginnt ihren *eigenen* Zielerwerb bis zu r − 1 Ticks später (höchstens 1/60 s bei 60 Hz, 1/40 s bei 120 Hz); danach wartet sie ohnehin zwei Bilder (`ForceIdleMSEC`). Befohlene Schüsse sind nicht betroffen. Im heißen Pfad bei 30 Hz: ein Vergleich je Einheit und Tick | bitgleich in drei Paarungen über acht Minuten bei 60 und 120 Hz |
| B. je Tick entscheiden, nur die Eingaben am Bildende lesen | nichts gegatet | Jede Stelle, an der der Durchlauf „im nächsten Tick“ weitermacht (Modus­wechsel nach dem Ende eines Angriffsmarschs, erster Durchlauf einer neuen Einheit, Wiederaufnahme nach einem Gefecht, Zustandswechsel nach `EndEngagement`), müsste einzeln auf „im nächsten Bild“ gestellt werden, mit je eigenem Zustand oberhalb von 30 Hz; eine vergessene Stelle verschiebt alle folgenden Blicke der Einheit dauerhaft neben das Raster | für Einheiten in Phase beobachtbar dasselbe wie A, mit mehr Zustand und ohne Gewähr |
| C. Phase der Einheit bei jedem Befehl neu setzen | wie A, aber die Phase ist der Tick des letzten Befehls | ein Feld mehr im Snapshot oberhalb von 30 Hz; beseitigt die Latenz aus A | nicht gemacht: kein gemessener Fall; die Beweger setzen ihre Phase bei einem Befehl bereits neu, der Zielerwerb läse dann eine andere Phase als `BeginsFrameOf` |
| D. Durchlauf nach dem **letzten** Tick des Bildes | alle Lagen sind dann ohne Bildend-Sicht die 30-Hz-Lagen | jeder selbst gewählte Schuss fiele (r − 1)/r Bild später als bei 30 Hz; widerspricht der Zeitkonvention der Suite und Regel 1 | verworfen (wie Alternative B in `RATE-INVARIANCE-CONSOLIDATION.md` §1.4) |

Die Variante A steht in §4 als Verwendung des Objektrasters. Sie ist **nicht** der Ausweg „ganze Logik nur in jedem (Rate/30)-ten Tick der Welt“: die Phase gehört der Einheit, und
ein Befehl wartet auf nichts. Sie ist aber, das sei deutlich gesagt, eine Entscheidung „einmal je Bild“ für den *eigenständigen* Zielerwerb; wer den Wortlaut „Zielerwerb in jedem
Tick“ strenger liest, findet hier die Stelle, an der er mit „dasselbe Spiel wie 30 Hz“ nicht vereinbar ist (Punkte 2 bis 4).

## 3. Ursachen, je Zeile ein Commit

„Erste Abweichung“ ist das erste Bild, nach dem im Gefecht USA gegen China (30 gegen 60 Hz) ein Feld eines Objekts drei Bilder lang abweicht (`rate-compare --ai-diff … --fields`,
§8); die Messreihe wurde in dieser Reihenfolge am jeweils kumulierten Stand erhoben. Suite-Zahlen sind „außerhalb bei 60 / bei 120 Hz“.

| # | Herkunft (`CONSOLIDATION` §6 bzw. neu) | Befund vorher | Korrektur (Datei, Symbol, Regel aus §1.3) | nachher | Commit |
|---|---|---|---|---|---|
| A | §6 „Winkel am Bildende“ | ein Schütze, der mitten im Bild hält, behielt einen Zwischenkurs; seine Rakete startete mit Kurs 0,6289 (30 Hz) gegen 0,7743 (60 Hz) | `World/Entity.cs` (`AheadAngle`, `HasAheadAngle`, `FrameEndAngle`; `CompleteFrame` setzt auch den Kurs), `Phase5Modules.cs` `AdvanceFrame` veröffentlicht den Kurs des Bildendes, `SimulationWorld.Projectiles.cs` `FireWeaponAt` startet damit (Regel 3) | erste Abweichung (Lage) 3543 → 3695 | `e83d88d` |
| B | §6 „Blick des Zielerwerbs“; neu: Phase des Zielerwerbs | erster Durchlauf einer neuen Einheit einen Tick statt ein Bild nach dem Erscheinen: nächster Blick der Dozer nach Bild 2 bei Tick 3 (30 Hz) gegen Tick 5 (60 Hz, wäre 6); der Blick maß augenblickliche Lagen | `SimulationWorld.Acquisition.cs`: `StepAcquisition` durchläuft eine Einheit in den Ticks, mit denen ihre Bilder beginnen; `FindMoodTarget`/`ConsiderMoodTarget` messen von Bildende zu Bildende (Regeln 1, 3; §2) | Tick 6; zusammen mit C: 3695 → 3810 | `f6dacdb` |
| C | 5.3 `Register` (erstes Update) | `AutoFindHealingUpdate` suchte ein halbes Bild vor dem 30-Hz-Scan: `nextScan` eines Missile Defender nach Bild 1198: 0 / 61 | `RepairModules.cs`, `OnSpawn`: erstes Update ein Bild nach dem Erscheinen (Regel 7) | 0 / 0; mit B: 3695 → 3810 | `35952fb` |
| D | neu: Halt durch einen Beobachter | der Durchlauf hält eine Einheit nach den Updates an (Verfolgung endet, Opfer in Reichweite): der Rest des Bildschritts verfiel, ein im selben Durchlauf gegebener Marsch begann im nächsten Tick. Rotgardist 42 in Bild 3810: x 1748,4665 (30 Hz) gegen 1748,7405 | `Phase5Modules.cs`: `StopInternalMoveAtFrameEnd` (vollendet das Bild, hält die nächste Marschentscheidung bis zum nächsten Bild, `moveHoldUntil` nur über 30 Hz); `Engage`, `EndEngagement` (Regel 4) | 3810 → 4430 | `8225a03` |
| E | §6 „nicht umgestellte Leser“: `HordeUpdate` | Nachbarn an der augenblicklichen Lage gezählt; Panzerjäger 44 nach Bild 4430 in der Horde (30 Hz) bzw. nicht (60 Hz) | `Phase12Modules.cs`, `Evaluate`: `Entity.FrameEnd` für die Einheit und die Nachbarn (Regel 3) | 4430 → 4746 | `d02a0d9` |
| F | §6 „Türme (`AimTurretsAt`)“ | Peilung, Neigung und „außer Reichweite“ aus augenblicklichen Lagen und dem augenblicklichen Kurs des Rumpfs; Turm des Humvee 165 nach Bild 4746: −0,0051138 gegen −0,0038214 | `SimulationWorld.Turrets.cs`: `DesiredTurretAngle`, `DesiredTurretPitch`, `KeepsAttackIntent` (Regel 3) | 4746 → 4947 | `c87748a` |
| G | §6 „Schaden im Radius“, Streusalven | eine Detonation maß ihre Opfer an der augenblicklichen Lage; Rakete 224 nach Bild 4947: Gesundheit 90 gegen 100. Suite: Aufschläge von 40 Schüssen Artillerie-Plattform 8 / 7 / 7, Schlachtschiff 21 / 20 / 20 | `SimulationWorld.Combat.cs`, `DealDamageCore`: Hauptopfer und jeder Kandidat in einem Bildschritt am Bildende (Regel 3) | 4947 → 6641; `projectiles` 4 / 4 → 0 / 0 | `202410f` |
| – | Messung der Suite | `Drive` nahm `HasMoveTarget == false` als Ende der Fahrt und las die Lage in diesem Tick | `RateCases.Locomotion.cs`: die Fahrt endet, wenn das Objekt steht (auch nicht mehr in einem Bildschritt). Keine Toleranz, kein Fall, kein 30-Hz-Wert geändert; an diesem Commit ändert sich kein Wert | hält die Messung nach H auf der Größe | `b99bd9c` |
| H | neu: Zeitpunkt der Ankunft (widerspricht `RATE-INVARIANCE-GROUND.md`, dort „mit dem letzten Anteil“) | ein Marsch endete im letzten Tick seines letzten Bildes; wer das Objekt im ersten Tick ansah (Zielerwerb, der Container, in den es läuft, das Aussteigen), fand es noch unterwegs. Ranger 597 betritt die Kaserne in Bild 6641; bei 60 Hz sahen ihn seine Verfolger ein Bild länger draußen | `Phase5Modules.cs`: `BeginArrival` beendet den Marsch im ersten Tick (Regel 1), `Coast` legt den Rest zurück; `SettleFrame` für Einsteigen (`SimulationWorld.PutIntoContainer`), Aussteigen und Zielobjekt; Reichweiten von `UpdateEnter`/`UpdateGoal` am Bildende | 6641 → 11462 | `2117316` |
| I | neu | `UpdateTask` prüfte in jedem Tick, ob die Aufgabe noch gilt, und beendete sie mitten im eigenen Schritt. Dozer 4 in Bild 11462: x 2343,2570 gegen 2343,4346 | `Phase5Modules.cs`, `UpdateTask`: die drei Prüfungen in den Bildticks des Bauenden (§4) | 11462 → 14158 | `3d90f25` |
| J | neu: „im nächsten Tick“ der Zerstörungsphase | ein nach der Zerstörungsphase getötetes Objekt (verzögerter Treffer) wurde einen Tick statt ein Bild später entfernt; die Grundfläche des Versorgungszentrums 14 verließ das Wegenetz ein Bild zu früh, alle planten neu | `World/Entity.cs` (`DestroyNotBefore`), `SimulationWorld.RequestDestroy`/`ProcessDestroys` (Regel 7 für das Entfernen) | 14158 → keine Abweichung in 14.400 Bildern; Befehlsströme gleich; `ai` 24 / 23 → 0 / 0 | `e9f1693` |
| K | §6 Mob-Nexus | `MobMemberSlavedUpdate` las den Nexus augenblicklich: Kurszeit 23,0667 / 25,2167 / 24,9583 s, Kursweg 400,062 / 439,291 / 434,011 | `SlaveModules.cs`: `Entity.FrameEnd` für Mitglied und Nexus (Regel 3) | 23,0667 / 23,0500 / 23,0583 s; 400,062 / 400,083 / 400,083; `locomotion` 2 / 2 → 0 / 0 | `c987fb9` |
| L | Test | – | `Tests.RateInvariance.Observers.cs`: zwölf Einheiten zweier Computerspieler (Gewehre, eine Radiuswaffe, Türme) im Angriffsmarsch; Lage, Kurs, Gesundheit, Befehlszustand, Opfer, Turm und Schüsse jedes von 1.500 Bildern bei 30, 60 und 120 Hz | schlägt am Stand `dc1b8d6` (ohne die Korrekturen) fehl, besteht am Endstand | `3ad8d7b` |
| M | neu: Heilscheibe der Reparatur (erst bei 120 Hz sichtbar, als `ai` schon bestand) | Reparatur je Tick: Kaserne Chinas vor dem Treffer von Bild 11273 bei 250,4999 (30 Hz) gegen 249,9999 (120 Hz) von 1000; der Furcht-Würfel (`ActiveBody`, unter 25 %) fiel zweimal bzw. einmal, der Zufallsstrom lief ab da auseinander; Folge: Dauer eines langsamen Tods 59 gegen 54 Bilder (Bild 11703) | `Phase5Modules.cs`, `UpdateTask`: über 30 Hz die 30-Hz-Scheibe einmal je Bild des Bauenden (Ausweg, §4) | Zufallsströme 30 / 120 Hz: verschieden ab Bild 11273 → nie; USA gegen China 30 / 120: 11703 → keine Abweichung | `64f438f` |
| N | neu: Heilscheibe des Lochs | `RebuildHoleBehavior` heilte je Tick: Loch 467 nach Bild 5707: 488,6667 / 488,7083 / 488,7292 | `RetailMatchModules.cs`: über 30 Hz die Scheibe einmal je Bild des Lochs (Ausweg, §4) | gleich; GLA gegen USA bei 60 und 120 Hz: 5707 → keine Abweichung | `2a433ba` |
| O | §6 „ein Objekt, das mitten im Bild stirbt“ | es blieb an der augenblicklichen Lage liegen; Bergungskiste 177 nach Bild 3791: x 1169,2887 / 1168,8769 / 1168,6710 | `SimulationWorld.OnEntityDied`: vollendet das begonnene Bild (Regel 4 für jeden Tod); `SettleFrame` hält die nächste Entscheidung bis zum Ende des begonnenen Bildes (ein Objekt im langsamen Tod läuft weiter) | gleich; China gegen GLA bei 60 und 120 Hz: 3791 → keine Abweichung | `6c722b7` |

Nach jedem Schritt geprüft wurde der kumulierte Arbeitsstand, nicht jeder Commit einzeln: die Commits sind nachträglich aus dem geprüften Stand geschnitten (jede Ursache für sich,
jeder Commit baut für sich mit 0 Warnungen – in einem Wegwerf-Worktree nachgebaut –, aber nur der Endstand und die in §7 genannten Zwischenstände haben Manifest und Tests
durchlaufen).

## 4. Verwendungen des Auswegs „Originalraster“ und des Objektrasters

| Stelle | Was einmal je Bild läuft | Raster | Grund |
|---|---|---|---|
| `SimulationWorld.StepAcquisition` | die eigenständige Entscheidung einer Einheit (Blick, Angriff, Verfolgen, Halt und Weitermarsch) | das der Einheit (Tick des Erscheinens) | §2: für eine Einheit in Phase kann nur der erste Tick ihres Bildes etwas entscheiden, ohne von 30 Hz abzuweichen. Befehle, Bewegung und befohlene Schüsse bleiben je Tick |
| `AIUpdateInterface.UpdateTask` (drei Prüfungen „lohnt die Aufgabe noch“) | Ziel fort, Bau fertig oder fremd, nichts mehr zu reparieren | das des Bauenden | was dem Ziel nach dem Zug des Bauenden im ersten Tick zustößt, sieht der 30-Hz-Bauende mit seinem nächsten Schritt. Die Arbeit selbst (Baufortschritt) bleibt je Tick |
| `AIUpdateInterface.UpdateTask` (Reparatur) | die Heilscheibe eines Bildes | das des Bauenden | Scheiben je Tick: im ersten Tick eines Bildes ist erst 1/r der Reparatur dieses Bildes eingegangen, ein Treffer in diesem Tick trifft eine andere Gesundheit als bei 30 Hz (§3 #M). Wie `PropagandaTowerBehavior` in der Konsolidierung. Weder Bewegung noch Befehlsannahme noch Zielerfassung |
| `RebuildHoleBehavior.Update` | die Regeneration eines Bildes | das des Lochs | derselbe Grund (§3 #N) |
| `AutoFindHealingUpdate` | unverändert jeder Tick zählt; nur das **erste** Update liegt ein Bild nach dem Erscheinen | – | kein Ausweg: Regel 7 für ein Modul, dessen Update einen Befehl gibt |
| `SimulationWorld.ProcessDestroys` | – (läuft je Tick); ein nach der Zerstörungsphase angefordertes Entfernen wartet ein Bild | Tick der Anforderung | „im nächsten Tick“ der Phasen nach den Updates ist das nächste Bild (wie `ProcessSpawns` in der Konsolidierung) |

Kein Ausweg, sondern die Sicht des 30-Hz-Schritts (Regel 3): der Blick des Zielerwerbs, Schaden im Radius, Turmzielen, `HordeUpdate`, `MobMemberSlavedUpdate`, der Start eines
Geschosses lesen bei jeder Entscheidung wie zuvor und nur eine andere Lage bzw. einen anderen Kurs.

## 5. Die Gefechte der Gruppe `ai`: erste Abweichung

Vergleich am Ende jedes Bildes über alle Felder der Objektbeschreibung (`Entity.Describe`, also alles, was in Snapshot und Hash eingeht). Vergleichsregel: Ganzzahlen und
Festkommawerte der schnelleren Rate gelten als gleich, wenn sie der Wert der langsameren in Spieleinheiten sind (ein Tick im selben Bild, eine Dauer mal r, ein Wert je Tick durch r);
Felder, die es nur oberhalb von 30 Hz gibt (Buchführung der Anteile), zählen nicht; eine Abweichung zählt erst, wenn sie drei Bilder anhält. **Bewusst ausgenommen** (jede ist
ein „Vorauslaufen innerhalb des Bildes“ ohne Wirkung auf eine Entscheidung im ersten Tick, die ersten drei waren schon in der Konsolidierung benannt):

- `nextWake` jedes Moduls (Buchführung des Schedulers);
- `previousHealth` und, solange ein Objekt im Bau ist, `health`/`constructionWork` („der erste Arbeitsschritt zählt ein ganzes Bild“, Paket TIMING: die Baustelle ist am
  Bildende (r − 1)/r Bild voraus, im ersten Tick gleich);
- `DynamicShroudClearingRangeUpdate.current` (integriert ab dem Tick nach dem Entstehen; die an die Sicht weitergereichte Reichweite `appliedRange` ist gleich);
- `weaponReadyTick` (eine Nachladezeit 0 ist oberhalb von 30 Hz „nächstes Bild“ statt „dieser Tick“, `FrameGridTiming.ZeroIsNextFrame`; gesehen an einem Selbstmordattentäter,
  der mit dem Schuss stirbt).

| Paarung | Raten | Ausgangsstand | **Endstand** |
|---|---|---|---|
| USA gegen China | 30 / 60 | Lage ab Bild 3543 (118,10 s) verschieden | **alle Felder gleich, 14.400 Bilder; Befehlsströme gleich (1.120)** |
| USA gegen China | 30 / 120 | nicht gemessen | **alle Felder gleich, 14.400 Bilder** |
| GLA gegen USA | 30 / 60 und 30 / 120 | nicht gemessen | **alle Felder gleich, 14.400 Bilder** |
| China gegen GLA | 30 / 60 und 30 / 120 | nicht gemessen | **alle Felder gleich, 14.400 Bilder** |

Der Weg dorthin (USA gegen China, 30 gegen 60 Hz) steht je Commit in §3. Die Ursachen, die erst bei 120 Hz oder in den anderen Paarungen sichtbar wurden, als die Suite schon
bestand: der Zufallsstrom lief ab Bild 11273 auseinander (#M: Furcht-Würfel beim Unterschreiten von 25 % Gesundheit – bei 30 Hz zweimal, bei 120 Hz einmal, weil die
Reparatur je Tick im ersten Tick erst ein Viertel der Scheibe geheilt hatte: 249,9999 statt 250,4999 von 1000 vor dem Treffer), das Loch eines GLA-Gebäudes (#N) und die Kiste
eines mitten im Bild getöteten Fahrzeugs (#O).

## 6. Was offen ist, mit Einordnung

| Was | Werte bzw. Beleg | Einordnung | Grund, Vorschlag |
|---|---|---|---|
| Heilscheiben je Tick an weiteren Stellen | nicht gemessen (kein Fall in den drei Gefechten) | später behebbar, jede Stelle einzeln | dieselbe Form wie #M/#N: `SlavedUpdate` (Reparaturdrohne, `SlaveModules.cs`), `RepairDockUpdate` (Paket TIMING: „heilt einen Anteil je Tick“), die Regeneration der Insassen in `ContainModules.cs` (`HealthRegenPercentPerSecond`) und in `TransportModules.cs` (Datei des Pakets FLIGHT-3). Solange ein Objekt geheilt und im selben Bild getroffen wird, liegt seine Gesundheit im ersten Tick um (r − 1)/r einer Bildscheibe unter dem 30-Hz-Wert; Schwellen des Körpers (Schadenszustand, Furcht-Würfel, Tod) können in einem anderen Bild fallen |
| „erstes Bild für alle Module“ (Konsolidierung §6) | Geschosse entscheiden im Tick `SpawnTick + 1 + k·r`, also einen Tick **vor** dem Bild, in dem das 30-Hz-Geschoss entscheidet | Entscheidung offen, unverändert | ein Treffer eines Geschosses fällt in den letzten Tick des Vorbilds: der Zielerwerb (erster Tick des Folgebilds) sieht dasselbe wie bei 30 Hz, aber ein Update mit kleinerer Kennung im Folgebild und der KI-Spieler (beobachtet an der Bildgrenze vor dem Schritt) sehen das Opfer ein Bild früher tot. In den drei Gefechten ohne Wirkung auf ein Feld am Bildende; die Erhebung über viele Gefechte (§7) zeigt, wie oft es anderswo wirkt |
| Kollisionsprüfung eines Fahrenden | – | bewusst gelassen (wie Konsolidierung) | `ResolveFootprintCollisions` läuft mit dem letzten Anteil; ein Überfahren tötet im letzten Tick des Bildes, der Zielerwerb des ersten Ticks hat das Opfer noch gesehen. Kein Fall gemessen |
| Befehl eines Menschen mitten im Bild | – | berechtigt verschieden | hat kein 30-Hz-Gegenstück: die Einheit zählt ihre Bewegungsbilder ab dem Befehl, ihr eigener Zielerwerb bleibt in der Phase ihres Erscheinens (§2, Variante A/C) |
| Leser, die nicht umgestellt sind | – | später behebbar, jede Stelle einzeln | Dock und Versorgung (bestehen in der Suite), `SlavedUpdate`, Spezialfähigkeiten, die Skript-VM (läuft im ersten Tick), `AutoHealBehavior.Matches`, `ScanClosestHealPad`, `CleanupAndSpy`-Module. Die Zahl der Stellen, die `X`/`Y` eines anderen Objekts lesen, liegt bei mehreren hundert; umgestellt sind die, an denen eine Abweichung gemessen wurde |
| nicht veröffentlichende Beweger | Chinook im Steigflug: `z` nach dem ersten Tick 20 / 17,5 | Paket FLIGHT-3 | Flug- und Sonderbeweger führen ihren Rest selbst; `--at first` des Werkzeugs zeigt sie |
| Zeitpunkt von `MoveArrived` | erster statt letzter Tick des letzten Bildes | Verhaltensänderung oberhalb von 30 Hz, gewollt | wer das Ereignis oder `HasMoveTarget` als „steht am Ziel“ liest und sofort die **augenblickliche** Lage nimmt, liest einen Anteil vor dem Ziel; umgestellt: Einsteigen, Aussteigen, Zielobjekt, die Messung der Suite. Die Präsentation ist nicht geprüft |
| Leistung bei 60 Hz | – | nicht gemessen | der Zielerwerb läuft je Einheit nur noch in einem von r Ticks (billiger als vorher), die Bildend-Sicht kostet je Kandidat einen Vergleich |

## 7. Prüfläufe

Alle Läufe am Endstand der Simulation (`6c722b7`), Release, wenn nicht anders gesagt.

| Lauf | Ergebnis |
|---|---|
| Raten-Äquivalenz-Suite, alle zehn Gruppen (`--only RateEquivalence --rate-report`) | §1: 5 Größen bei 60 Hz, 3 bei 120 Hz außerhalb (vorher 35 / 33), alle in Fällen der beiden anderen Pakete: `flight` MiG „Landebefehl bis Aufsetzen“ 14,6333 / 14,6333 / 14,5667 s und Raptor „geparkt und nachgeladen“ 60,8000 / 60,8000 / 60,5333 s; `turrets` Wachdrohne (vier Größen, 60 Hz); `timed` `CINE_BShipWeaponDetonationObject`. Keine Gruppe ist schlechter geworden; `flight` 0 / 3 → 0 / 2 ohne gezielte Änderung (der Comanche-Fall besteht) |
| Simulationstests, Release, 30 Hz / `--tick-rate 60` / `--tick-rate 120` | alle grün bei jeder Rate (496 Tests: 495 bestehende, 1 neuer) |
| Simulationstests, Debug, 30 Hz | alle grün |
| `dotnet build CnCNew.slnx` | 0 Warnungen, 0 Fehler (inkrementell) |
| `input-manifest check` | identisch (am Arbeitsstand nach A–G, nach A–L und am Endstand) |
| `reference-manifest check --set quick --jobs 3` | 9 Läufe identisch (am Arbeitsstand nach A–D und nach A–L); **nicht nach jedem Commit einzeln** |
| `reference-manifest check --set full --jobs 4` | **50 Läufe identisch** am Endstand |
| jeder der 17 Commits für sich | baut mit 0 Warnungen (Testprojekt, Wegwerf-Worktree: geprüft für die 13 Commits bis `3ad8d7b`; die vier danach nur am Endstand) |
| Feldvergleich der drei Gefechte (`--ai-diff … --fields`), 30 / 60 und 30 / 120 Hz | sechs Läufe: alle Felder gleich über 14.400 Bilder (§5) |
| Erhebung `ai-survey --seeds 5` bei 30 und 60 Hz (16 Paarungen auf vier ausgelieferten Karten, 25 Spielminuten; Information, kein Kriterium) | 80 Gefechte je Rate, kein Abbruch; entschieden 66 (30 Hz) und 63 (60 Hz). Je Paarung und Seed: derselbe Ausgang und Sieger in 73 von 80; dazu Ende innerhalb einer Sekunde in **54 von 80**; Endzeit auf die Millisekunde gleich in 49; gleiche Befehlszahl in 36. Die übrigen 26 Gefechte laufen also weiter auseinander – auf Karten mit Gelände, Flugzeugen und Docks, die die drei Gefechte der Suite nicht haben (§6) |
| Leistung bei 30 Hz, `perf … Twilight Flame --players 8 --ticks 12000`, Ausgangs-Binärdateien und neue abwechselnd (alt / neu / alt / neu), Mittel ohne die ersten 300 Ticks | 3626,1 / 3267,0 / 3335,5 / 3694,8 µs; Mittel alt 3480,8, neu 3480,9 µs – kein Unterschied; die Streuung zwischen zwei Läufen derselben Binärdatei (rund 10 %) ist größer als jeder denkbare Effekt, der Rechner war nicht ruhig gestellt |

Geänderte Testerwartungen (jeweils im Commit der Ursache begründet): „an idle unit's first look …“ (drei Bilder nach dem Erscheinen statt Tick 1 + 2 Bilder), „the random shift of the mood
check …“ (Raster der Einheit statt der Welt), „turret deadlines and the idle scan …“ (die Messung liest den Flüchtenden am Bildende), drei Bodentests der Runde 1 (Ankunft im ersten statt
im letzten Tick des Bildes; ein Befehl nach der Ankunft storniert sie nicht mehr), „straight-line movement and arrival“ und „ObjectCreationList execution …“ (warten ein Bild statt
einen Tick). Bei 30 Hz ist jede dieser Erwartungen dieselbe wie zuvor.

Nicht gemacht oder nicht geprüft: Manifest und Tests nach jedem einzelnen Commit; eigene Drei-Raten-Tests je Ursache über den Gefechtstest (L) und die angepassten Tests hinaus;
120 Hz in der Erhebung; die Erhebung am Ausgangsstand (der Vergleich „vorher“ fehlt als Zahl); Leistung bei 60 Hz und auf ruhigem Rechner; Kampagne (Zielfahrer, Audit) bei 60 und
120 Hz nach diesen Änderungen; `Game.Tests`, `Presentation.Tests` und der Client (die Ankunft im ersten Tick und das Entfernen ein Bild später ändern oberhalb von 30 Hz, wann
Ereignisse fallen). **Kein Mensch hat etwas davon gesehen.**

## 8. Werkzeug

`SimTool rate-compare <content> --ai-diff A,B --fields [--rates 30,60] [--frames N] [--from BILD] [--persist N] [--at first] [--strict] [--ignore REGEX] [--context N]`:
beide Raten eines Gefechts der Gruppe `ai` nebeneinander, Vergleich aller beschriebenen Felder aller Objekte nach jedem Bild (Regel in §5), dazu das erste Bild, ab dem die
Zufallsströme der Logik verschieden sind. `--at first` vergleicht nach dem ersten Tick jedes Bildes mit Lage, Kurs und Turm am Bildende (die Sicht eines Beobachters dieses
Ticks). `--trace ID[,ID] --trace-from F --trace-to G` druckt die Felder einzelner Objekte, wie sie sich ändern, Tick für Tick bei beiden Raten; `--random-trace F
[--random-frames N]` druckt, welche Phase eines Ticks bzw. welches Modul-Update aus dem Zufallsstrom zieht, und die Ereignisse dieser Ticks. Kein Verhalten.
