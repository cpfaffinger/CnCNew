# Raten-Invarianz, vierte Runde: das erste Bild und ganze Gefechte auf ausgelieferten Karten (Phase 23, Paket ROUND4)

Stand 8. Oktober 2026, Branch `worktree-agent-a8540b703e4f5eeca`, Ausgangsstand `203806e` (`main`). Auftrag: ganze Gefechte auf **ausgelieferten** Karten sollen bei 30, 60 und
120 Hz dasselbe Spiel sein – das Retail-Referenzmatch und die Gefechte der eingebauten KI in den Paarungen des Referenzmanifests. Methode wie in den Runden davor: erste
abweichende Stelle suchen (`rate-compare --reference-diff` bzw. das neue `--map-diff`), Ursache benennen, auf die Bildkonvention stellen
(`docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §1.3), ein Commit, neu messen. „Bild“ heißt Logikbild des Originals (1/30 s), „Tick“ ein Schritt der Simulation, r = Ticks je Bild
(1, 2, 4). Werte stehen als 30 / 60 / 120 Hz. **Nichts davon hat ein Mensch gesehen.**

**Bei 30 Hz ist nichts anders:** `input-manifest check` identisch und `reference-manifest check --set quick` (9 Läufe) identisch für jeden Commit, der die Simulation ändert
(§7 sagt, welche am Arbeitsstand und welche nachträglich in einem Wegwerf-Worktree geprüft sind); `--set full` am Ende: 50 Läufe identisch. Kein Katalogtyp hat ein neues Feld, kein
Referenzwert wurde geschrieben, keine Toleranz und kein Fall der Suite wurde angefasst.

## 1. Die drei Kennzahlen

### 1.1 Retail-Referenzmatch (`ReferenceMatchScenario`)

| | Ausgangsstand `203806e` | Endstand |
|---|---|---|
| erstes abweichendes Bild 30 gegen 60 Hz (`rate-compare --reference-diff --fields`, Kataloge der Tests) | Bild 0 (43 Felder) | **keine Abweichung**: jedes verglichene Feld jedes Objekts am Ende jedes Bilds des ganzen Matchs gleich |
| erstes abweichendes Bild 30 gegen 120 Hz | Bild 0 | **keine Abweichung** im ganzen Match |
| Befehle des Generators im ganzen Match (`SimTool reference-match --tick-rate N --record-only`, Kataloge des Werkzeugs) | 26.626 / 28.930 / 25.309 | **26.626 / 26.626 / 26.626** |
| Sieger und Siegzeit | FactionGLA in Tick 14.130 / 29.130 / 49.560, das ist Bild 14.130 / 14.565 / 12.390 (7,85 / 8,09 / 6,88 Spielminuten) | **FactionGLA in Tick 14.130 / 28.260 / 56.520: Bild 14.130 bei jeder Rate** |
| Test „Real data: retail reference match, determinism and snapshot continuation“ bei `--tick-rate 120` | rot (kein Sieger in 216.000 Ticks, Kataloge der Tests) | **grün**: Sieger FactionGLA in Tick 56.520 (Bild 14.130), 26.626 Befehle – dasselbe Match; das Budget des Tests ist unverändert |

Die im Auftrag genannten Siegzeiten 9,9 / 9,3 / 7,9 Minuten stammen von einem älteren Stand (`4d9d9e5`); gemessen am Ausgangsstand `203806e` sind es die Werte der Tabelle.

### 1.2 Die 16 Paarungen des Manifests auf ihren Karten (Seed des Manifests)

Erstes Bild, nach dem ein beschriebenes Feld eines Objekts drei Bilder lang abweicht (`rate-compare --map-diff A,B --map KARTE --fields --rates 30,60`, Kataloge des Werkzeugs,
bis 45.000 Bilder; ausgenommen sind die in §6 genannten fünf Felder). Sieger und Bild des Siegs aus `ai-match … --minutes 25 --tick-rate N`.

| Paarung | erste Abweichung vorher | **nachher** | Sieger im Bild, 30 Hz | 60 Hz vorher | **60 Hz nachher** | gleich? |
|---|---|---|---|---|---|---|
| Alpine Assault, America/China | 1306 | 37408 | – (kein Sieger) | – | – | ja (vorher ja) |
| Alpine Assault, China/GLA | 3060 | **keine** | China 8595 | China 8595 | China 8595 | ja (vorher ja) |
| Alpine Assault, ChinaTank/AirForce | 3764 | 13883 | ChinaTank 18300 | ChinaTank 17160 | ChinaTank 18510 | **nein** (7 s; vorher nein) |
| Alpine Assault, GLA/America | 4665 | 9802 (nach dem Sieg) | America 9660 | America 9660 | America 9660 | ja (vorher ja) |
| Armored Fury, America/China | 920 | 23868 | America 37050 | kein Sieger | America 37050 | **ja** (vorher nein) |
| Armored Fury, China/GLA | 2837 | **keine** | – | China 39435 | – | **ja** (vorher nein) |
| Armored Fury, ChinaTank/AirForce | 964 | **keine** | ChinaTank 10860 | ChinaTank 10860 | ChinaTank 10860 | ja (vorher ja) |
| Armored Fury, GLA/America | 964 | 12817 | America 36180 | America 25500 | America 36180 | **ja** (vorher nein) |
| Bitter Winter, America/China | 3921 | **keine** | China 22005 | **America** 20805 | China 22005 | **ja** (vorher anderer Sieger) |
| Bitter Winter, China/GLA | 3510 | **keine** | China 8385 | China 8385 | China 8385 | ja (vorher ja) |
| Bitter Winter, ChinaTank/AirForce | 4337 | 19949 | ChinaTank 30150 | ChinaTank 22635 | ChinaTank 32685 | **nein** (85 s; vorher nein) |
| Bitter Winter, GLA/America | 3170 | **keine** | America 8730 | America 8730 | America 8730 | ja (vorher ja) |
| Death Valley, America/China | 3509 | **keine** | America 8700 | America 8700 | America 8700 | ja (vorher ja) |
| Death Valley, China/GLA | 3262 | **keine** | China 12930 | China 12930 | China 12930 | ja (vorher ja) |
| Death Valley, ChinaTank/AirForce | 3667 | 23085 (nach dem Sieg) | ChinaTank 22860 | ChinaTank 15630 | ChinaTank 22860 | **ja** (vorher nein) |
| Death Valley, GLA/America | 4723 | 10681 (nach dem Sieg) | America 10530 | America 10545 | America 10530 | **ja** (vorher 15 Bilder daneben) |

Zusammen: Sieger und Bild des Siegs (bzw. „kein Sieger in 45.000 Bildern“) stimmen bei 30 und 60 Hz in **14 von 16** Paarungen überein (vorher 8); in **8 von 16** ist jedes
verglichene Feld jedes Objekts über das ganze Gefecht gleich (vorher 0), in drei weiteren bis nach dem Sieg. 120 Hz ist für die Paarungen **nicht gemessen**.

### 1.3 Erhebung über 320 Gefechte (`ai-survey`, 16 Paarungen × 20 Seeds, 25 Spielminuten), 30 gegen 60 Hz

| | Ausgangsstand | Endstand |
|---|---|---|
| derselbe Ausgang und Sieger | 295 von 320 | **317 von 320** |
| dazu Ende innerhalb einer Sekunde | **192 von 320** | **287 von 320** |
| Endzeit gleich | 180 | 285 |
| gleiche Befehlszahl | 164 | 282 |

Gezählt mit einer eigenen Auswertung der beiden TSV-Dateien (Ausgang und Sieger gleich, `endGameMilliseconds` höchstens 1000 auseinander); der Auftrag nennt für den
Ausgangsstand 188, die eigene Messung am selben Stand ergibt 192. Die 30-Hz-Erhebung ist die des Ausgangsstands (bei 30 Hz ändert kein Commit etwas; sie wurde am Endstand
nicht wiederholt).

## 2. Ursachen, je Zeile ein Commit

„Referenz“ ist die erste bleibende Abweichung des Referenzmatchs 30 gegen 60 Hz, „AA Am/Ch“ die der Paarung America gegen China auf Alpine Assault, „AF Am/Ch“ dieselbe Paarung
auf Armored Fury – jeweils unmittelbar vor und nach dem Commit am kumulierten Stand. Suite (`--only RateEquivalence`, 4.388 Größen) vor und nach jedem Commit: jede Gruppe 0 / 0.

| # | Kandidat des Auftrags bzw. Herkunft | Befund vorher (30 / 60 Hz) | Korrektur (Datei, Symbol, Regel aus `CONSOLIDATION` §1.3) | nachher | Commit |
|---|---|---|---|---|---|
| 1 | 1 (Regel 7, Chinook) | Start-Chinooks steigen ab dem Tick nach dem Erscheinen: Höhe nach Bild 2: 10 / 12,5 | `TransportModules.cs`, `ChinookAIUpdate.Update`: erstes Update ein Bild nach dem Erscheinen | 10 / 10; Referenz weiter Bild 0 (nächste Ursache) | `2837f1e` |
| 2 | neu (Messgerät) | der Generator des Referenzmatchs gab die Dock-Befehle in Tick 1, mitten im Bild des Erscheinens: jeder Sammler stand fortan einen Anteil neben allem in Phase | `Testing/ReferenceMatchScenario.cs`, `Generate`/`IssueDockOrders`: im ersten Tick von Bild 1 (bei 30 Hz Tick 1) | Befehle in 3.000 Bildern 5710 / 5684 → 5710 / 5710; Referenz Bild 0 → 2 | `4b1713d` |
| 3 | neu | Objekt mit **zwei** KI-Modulen (Chinook des Luftwaffengenerals: `AIUpdateInterface` und `ChinookAIUpdate`, beide bewegen es): das zweite nahm das Tempo des ersten aus dem Tempo je Tick mal r; Tempo je Bild roh 2236962 / 2236961 | `World/Entity.cs` (`SpeedPerFrame`, nur über 30 Hz), `Phase5Modules.cs` `FrameSpeed`/`SetFrameSpeed`/`ClearFrameSpeed` | gleich; Referenz Bild 2 → 26 | `3b5c781` |
| 4 | 3 (Docks) | ein Andockender ohne Wartezeit (derselbe Chinook: sein Dock-Automat gehört dem `AIUpdateInterface`) bekam je Tick eine Kiste: Lager nach Bild 26/27/28: 400, 399, 398 / 399, 397, 395 | `DockMachine.cs`, `ActionDelay`: an Versorgungsdocks ist „Wartezeit 0“ ein Bild (`ZeroIsNextFrame`) | gleich; Referenz Bild 26 → 776 | `d9b5490` |
| 5 | 1 („einen Tick später“) | `HelicopterSlowDeathBehavior.BeginSlowDeath` weckte im nächsten Tick: Helix 49 nach Bild 778: Rotor-Zähler 28 / 27, Falltempo roh −190888 / −286332 | `TransportModules.cs`: erster Schritt der Spirale ein Bild nach dem Tod | gleich; Referenz Bild 776 → 1742 | `96be02e` |
| 6 | neu | `PointDefenseLaserUpdate`: ein Scan ohne Ziel zählt die Schusspause nicht herunter (bei 30 Hz ein ganzes Bild ohne Zählen); über 30 Hz zählten die übrigen Ticks dieses Bilds weiter: nach Bild 1739/1741/1743: 14, 12, 10 Ticks gegen 14, 11, 8 | `ProjectileModules.cs`, `PointDefenseLaserUpdate.Update` (`_quietFrame`, nur über 30 Hz) | 14, 12, 10; Referenz Bild 1742 → 3466; das 60-Hz-Match endet erstmals im Bild des 30-Hz-Matchs | `887b51e` |
| 7 | `CONSOLIDATION` §6 „Kollisionsprüfung eines Fahrenden“ (dort bewusst gelassen) | Überfahren, Umstoßen und Kollisionsmodule wurden mit dem **letzten** Anteil geprüft: eine Kiste wurde im letzten Tick eines Bilds eingesammelt, ihr Zufallszug fiel nach statt vor dem eines Lasers (Wartezeit 2 Bilder / 0) | `Phase5Modules.cs` `AdvanceFrame`/`CollideWhereTheFrameEnds`, `SimulationWorld.Collision.cs` `FootprintsOverlapAtFrameEnd`, `TryTopple`, `TrySquish`: Prüfung im **ersten** Tick, der Fahrende steht dabei am Bildende, alle anderen werden am Bildende gelesen (Regeln 1, 3, 4) | gleiche Reihenfolge; Referenz Bild 3466 → 9394; Befehle im ganzen Match 26626 / 28811 → 26626 / 26626 | `e910174` |
| 8 | 3 (Leser) | „in der Luft?“ (`isSignificantlyAboveTerrain`) wurde aus der augenblicklichen Höhe beantwortet: sinkende Rakete 0,592 / 0,838 über Grund, der Laser nahm bei 30 Hz Ziel 10374, bei 60 Hz 10372 | `IWorldServices.IsSignificantlyAboveTerrainAtFrameEnd` (`SimulationWorld.CreationLists.cs`); Leser: Laser-Scan, `VictimAntiMask`, `EstimateDamage`, Radiusschaden, Turmneigung (Regel 3) | gleich; Referenz Bild 9394 → **keine Abweichung im ganzen Match** | `134d074` |
| 9 | 5 (Gelände) | ein Bodenfahrer, der an sein Bildende gesetzt wird (`Entity.CompleteFrame`: in Reichweite gekommen, angehalten, gestorben, eingestiegen), behielt die Höhe des zurückgelegten Anteils: Rotgardist am Hang z 19,2160 / 19,1639. Erste Abweichung in 14 der 16 Paarungen | `Phase5Modules.cs` `AdvanceFrame` veröffentlicht die Höhe des Bildendes (`Entity.SetAheadHeight`) | gleich; zusammen mit #10: AA Ch/GLA Bild 3060 → 7068 | `b000106` |
| 10 | 5 (Gelände), 2 (Chinook-Höhenschritt) | der Chinook hielt seine Höhe über dem Boden der Stelle nach dem ersten Anteil statt nach dem ganzen Schritt: z 145,3442 / 145,3568 | `TransportModules.cs`, `ChinookAIUpdate.Update`: Boden am Ende der Bewegung des Bilds | gleich; AF Am/Ch Bild 920 → 6528 | `b62ccc4` |
| 11 | neu (Folge von #3, erst bei 120 Hz sichtbar) | das zweite KI-Modul entschied seinen Schritt von der Stelle nach einem **Anteil** des ersten: Chinook 21 ab Bild 10 einen Rohwert daneben, ab Bild 18 Tempo je Bild 1,7778 / – / 1,9556 | `Phase5Modules.cs` `DecideBehindAnotherMover`; `ChinookAIUpdate` veröffentlicht die Summe beider Reste | Referenz 30 gegen 120 Hz: Bild 10 → siehe §1.1 | `10e0031` |
| 12 | 2 (Konstanten je Bild) | Beschleunigung einer Rakete je Bild² aus dem Tickwert zurückgerechnet: 2,4 ist roh 40265318 / 40265320; Flammen- und Giftstrahl flogen einige Rohwerte neben dem 30-Hz-Strahl (der in `RATE-SWITCH-OUTER-TESTS.md` §7.3 offene Befund) | `ProjectileModules.cs`, `MissileAIUpdate.Launch`: Tempo und Beschleunigung aus `LocomotorFrames` | gleich; AA Ch/GLA Bild 7068 → keine Abweichung; AF Am/Ch 6528 → 6638 | `ec9f90a` |
| 13 | 3 (`CleanupAndSpy`-Module) | `CleanupHazardUpdate` (Sanitäter) entschied in jedem Tick aus augenblicklichen Lagen, sein Zähler verließ das Raster. Erste Abweichung in 5 Paarungen (Bild 6717 bis 7948) | `CleanupAndSpyModules.cs`: entscheidet in den Bildticks des Besitzers, liest am Bildende, hält am Bildende (Regeln 1, 3; Form des Lasers) | AA Am/Ch Bild 7439 → 14140 | `7880aed` |
| 14 | 3 (Einsteigen) | `OpenContain.Add`/`Remove` setzten den Insassen an die augenblickliche Lage des fahrenden Transporters: Ranger 1182 x 1293,280 / 1293,985 | `ContainModules.cs`: `Entity.FrameEnd` (Regel 3) | gleich; AA Am/Ch 14140 → 16783 | `8fb48ac` |
| 15 | 3 (Zielplätze; der Befund aus `RATE-SWITCH-OUTER-TESTS.md` §7.3, Bild 10532) | wer einer produzierten Einheit Platz macht, sah sie über 30 Hz schon „unterwegs zum Ausgang“ (bei 30 Hz bekommt sie ihren Befehl erst nach der Platzwahl): Ausweichplatz (1296,27 \| 1755,86) / (1324,35 \| 1805,47) | `SimulationWorld.ExitGoals.cs`, `GoalIsFree`: die Einheit, deren Platz gerade gewählt wird, zählt an ihrem Standort | gleich; AA Am/Ch 16783 → 31784 | `8327cd9` |
| 16 | 1 („einen Tick später“) | ein Marsch, den der Zielerwerb (nach den Updates) einer stehenden Einheit gibt, begann bei immer wachen Modulen (`AssaultTransportAIUpdate`, Sammler) im nächsten Tick statt im nächsten Bild | `Phase5Modules.cs` `BeginMoveWithNextFrame`, `SimulationWorld.Acquisition.cs` (Verfolgung, Weitermarsch) | AA Am/Ch 31784 → 37408 | `66607e2` |
| 17 | neu | ein schwenkender Turm (Flammenwerfer), der auf seinem Ziel steht, wechselt je Schritt die Seite; ein Schritt ohne Drehung belegte kein Bild: Seite nach Bild 6636/37/38: wahr, falsch, wahr / falsch, falsch, falsch | `SimulationWorld.Turrets.cs`, `StepTurret`: `OccupyFrame` | gleich; AF Am/Ch 6638 → 17946 | `43acce8` |
| 18 | `OBSERVERS` §6 „ein Objekt, das mitten im Bild stirbt“, für Türme | der Turm eines Objekts, das nach der Turmphase stirbt, blieb auf dem ersten Anteil stehen: 0,10472 / 0,08727 rad | `SimulationWorld.Turrets.cs` `CompleteTurretFrames`, `SimulationWorld.OnEntityDied` (Regel 4) | gleich; AF Am/Ch 17946 → 19217 | `36a2404` |
| 19 | 3 (Spezialverhalten) | `AssaultTransportAIUpdate` führte seine Gruppe in jedem Tick: der Schuss des Vorbilds wurde einen Tick statt ein Bild später bemerkt, die Truppen stiegen einen Anteil zu früh aus | `HackerAndAssaultModules.cs`: Gruppe einmal je Bild des Transporters, Lagen am Bildende (Ausweg, §4) | AF Am/Ch 19217 → 19538 | `015e975` |
| 20 | neu | ein Fahrender, den ein später drankommendes Modul im Tick seiner Entscheidung „jetzt“ weckt, macht bei 30 Hz einen zweiten ganzen Schritt; über 30 Hz verfiel der Rest des ersten: x 2174,5054 / 2174,2344 | `Phase5Modules.cs`, `Move`: der begonnene Schritt wird erst vollendet | gleich; AF Am/Ch 19538 → 20952 | `69e013e` |
| 21 | 1 (Regel 7), 3 (Leser) | `PilotFindVehicleUpdate` sah zum ersten Mal einen Tick nach dem Erscheinen nach (danach immer im letzten Tick eines Bilds); Ziele von `OrderApproachGoal`/`OrderEnter` aus augenblicklichen Lagen; das Nachzielen einer Annäherung fiel auf den letzten Anteil | `CrateModules.cs` (`OnSpawn`, `Scan`), `Phase5Modules.cs` (`OrderApproachGoal`, `OrderEnter`, `UpdateGoal`) | AF Am/Ch 20952 → 23868 | `713c662` |

Werkzeug (kein Verhalten): `rate-compare --map-diff` (`e57e46f`), `--slack N` (`75db0bb`), Erscheinungstick einer verfolgten Einheit und `--commands N` (`cb1a493`); §8. Test:
`4b026c6` (§5).

## 3. Regel 7 („erstes Bild“): was gemacht ist und was die allgemeine Fassung kostet

Die allgemeine Fassung („jedes Update eines neuen Objekts frühestens `SpawnTick + r`“) wurde am Ausgangsstand noch einmal probeweise gemessen (eine Zeile in
`SimulationWorld.DeferSpawnPhaseOrders`, nicht übernommen): Simulationstests ohne Realdaten **13 rot bei 60 Hz, 15 bei 120 Hz** (von 512). Rot waren die Drei-Raten-Tests von
Fallschirm, Chinook, Flugschritt im Snapshot, Flugbeobachter und dynamischer Aufdeckung, dazu Feuersturm, Feindnähe-Scan, Checkpoint, Fähre (2), Chinook (2),
`FireOCLAfterWeaponCooldownUpdate`, bei 120 Hz noch Tunnelheilung und die Ereigniszustellung. Jeder dieser Tests stellt seine Szene auf „einen Tick nach dem Erscheinen“ ein; die
Module selbst führen ihre Phase teils als eigenen Zähler ab dem ersten Update. Deshalb Modul für Modul, dort, wo eine gemessene Abweichung beginnt:

| Modul | Stand |
|---|---|
| `ChinookAIUpdate` | erstes Bild ein Bild nach dem Erscheinen (#1); vier Szenen stellen sich jetzt auf dieses Bild ein (bei 30 Hz dieselben Ticks) |
| `HelicopterSlowDeathBehavior` (erster Schritt nach dem Tod) | ein Bild nach dem Tod (#5); vier Szenen umgestellt |
| `PilotFindVehicleUpdate` | ein Bild nach dem Erscheinen (#21) |
| Geschosse (`DumbProjectileBehavior`, `MissileAIUpdate`) | waren am Ausgangsstand schon umgestellt („The first step is decided one logic frame after the launch“); die Aussage der Runde 3 „entscheiden einen Tick vor dem 30-Hz-Bild“ gilt nicht mehr |
| `DeliverPayloadAIUpdate` (Frachtflugzeug), Fallschirm, Fähre, dynamische Aufdeckung, Feuersturm, `FireOCLAfterWeaponCooldownUpdate`, Feindnähe-Scan, Checkpoint | **nicht umgestellt**: keine gemessene erste Abweichung in den Läufen dieser Runde |

## 4. Verwendungen des Auswegs „Originalraster“ und des Objektrasters

| Stelle | Was einmal je Bild läuft | Raster | Grund |
|---|---|---|---|
| `CleanupHazardUpdate.Update` | Scan, Zielverlust, Annäherungsbefehl und Schuss des Moduls | das des Besitzers (`BeginsFrameOf`) | Zustandsautomat des Originals mit einem Schritt je Bild; in jedem Tick entschieden verlässt sein Zähler das Raster (Form von `PointDefenseLaserUpdate`). Die Bewegung der Einheit und Befehle an sie bleiben je Tick |
| `AssaultTransportAIUpdate.Update` | Mitgliederliste, Blick nach dem eigenen Schuss, Befehle an die Truppen | das des Transporters | derselbe Grund: „im nächsten Update“ ist ein Bild. Die Bewegung des Transporters läuft je Tick |
| `PointDefenseLaserUpdate.Update` (`_quietFrame`) | die Schusspause ruht für den Rest eines Bilds, dessen Entscheidungstick ein leerer Scan war | das des Besitzers | bei 30 Hz zählt dieses Bild nicht; kein neues Raster, nur die Folge des bestehenden |
| `DockMachine.ActionDelay` | die Aktion eines Versorgungsdocks an einem Andockenden ohne Wartezeit | ab dem Betreten | „beim nächsten Update“ ist ein Bild (`ZeroIsNextFrame`) |
| `SimulationWorld.StepTurret` (`OccupyFrame` beim Seitenwechsel) | der Seitenwechsel eines schwenkenden Turms | das des Turms | ein Schritt des Turms ist ein Bild |

Kein Ausweg, sondern die Sicht des 30-Hz-Schritts (Regeln 3 und 4): die Kollisionsprüfung eines Fahrenden läuft weiter einmal je Bild, jetzt im ersten statt im letzten Tick;
alle übrigen Korrekturen lesen nur eine andere Lage oder Höhe oder verschieben „einen Tick später“ auf „ein Bild später“.

## 5. Tests

- **Neu:** „Real data: the retail reference match gives the same commands in the same frames at 30, 60 and 120 Hz“ (`Tests.RateInvariance.Round4.cs`, nennt seine Raten selbst,
  45 s): die ersten 6.000 Bilder des Referenzmatchs bei den drei Raten aufgezeichnet, die Befehlsströme in Spielzeit verglichen (der Generator befiehlt aus dem, was er von der
  Welt sieht, Lagen auf den Rohwert genau): 13.504 Befehle, bei den drei Raten gleich. Am Ausgangsstand trennen sich die Ströme in der ersten Sekunde.
- **Umgestellte Szenen** (bei 30 Hz dieselben Ticks, die Erwartungen unverändert): Chinook-Steigflug, Chinook im Snapshot, Chinook-Beobachter, zwei Chinook-Tests in
  `Tests.Transports.cs` (#1); Hubschrauber-Absturz in vier Tests (#5: die Spur beginnt ein Bild nach dem Tod; „75 Updates nach dem Tod“ ist `death + 75 Bilder`).
- **Geänderte Erwartung** (#7, ersetzt die Festlegung des Pakets GROUND): „Rate invariance, ground: crush and topple are sampled once per logic frame“ verlangt beide Ereignisse
  jetzt im **ersten** Tick des Bilds der 30-Hz-Ereignisse statt im letzten.
- Für die Ursachen #2 bis #4, #6, #8 bis #21 gibt es **keinen eigenen Drei-Raten-Test auf der synthetischen Fixture**; sie sind durch die Feldvergleiche der ganzen Gefechte
  (§1) und, soweit sie im Referenzmatch vor Bild 6.000 wirken (#1 bis #7), durch den neuen Test getragen.

## 6. Was Gefechte noch trennt, und was offen ist

Die zwei Paarungen, deren Ausgang noch abweicht, und die sechs, in denen ein Feld vor dem Ende abweicht, trennen sich an diesen Stellen (30 gegen 60 Hz, Seed des Manifests):

| Paarung | Bild | Was abweicht | Stand |
|---|---|---|---|
| Alpine Assault America/China; Armored Fury GLA/America | 37408; 12817 | Turm einer `AmericaFireBase`: im Bild, in dem der Zielerwerb der Einheit ergebnislos nachsieht (`acquisition.nextCheck` wird gesetzt), beginnt der Turm bei 60 Hz im **zweiten Tick desselben Bilds** zu drehen, bei 30 Hz ein Bild später (Winkel am Bildende −0,3975 / −0,3888) | Ursache nicht bis zum Symbol verfolgt. Ein Versuch („ein Turmschritt, der nur den Zustand ändert, belegt sein Bild“, `SimulationWorld.StepTurret`) änderte das erste abweichende Bild nicht und ist zurückgenommen |
| Alpine Assault ChinaTank/AirForce; Armored Fury America/China | 13883; 23868 | Kurs eines Rangers (nur `.angle`, Lage gleich): −1,2617 bzw. 2,2411 bei 30 Hz gegen einen anderen Kurs bei 60 Hz | nicht verfolgt (Kandidat: eine Fähigkeit oder ein Halt, der mitten in einer Drehung endet) |
| Bitter Winter ChinaTank/AirForce | 19949 | Tempo eines Gattling-Panzers (1,3333 je Bild gegen einen anderen Wert) | nicht verfolgt |
| Alpine Assault GLA/America; Death Valley GLA/America; Death Valley ChinaTank/AirForce | 9802; 10681; 23085 | Turm eines Humvee bzw. `acquisition.nextCheck` eines Dragon-Panzers, jeweils **nach** dem Sieg (in den 300 Bildern Nachlauf) | dasselbe Muster wie die FireBase; ohne Wirkung auf Sieger und Siegzeit |

Die Erhebung (§1.3) zeigt dasselbe im Großen: 33 von 320 Gefechten enden bei 60 Hz noch nicht innerhalb einer Sekunde des 30-Hz-Endes (vorher 128), 3 mit anderem Ausgang
(vorher 25).

Bewusst vom Feldvergleich ausgenommen (`--ignore`), jede ein „Vorauslaufen innerhalb des Bildes“ ohne Wirkung auf eine Entscheidung; die ersten drei wie in
`docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md` §5:

- `nextWake` jedes Moduls, `previousHealth`;
- `weaponReadyTick` und – neu, derselbe Fall – `dockMachine.nextActionTick` (eine Wartezeit 0 ist über 30 Hz „nächstes Bild“ statt „dieser Tick“);
- `DynamicShroudClearingRangeUpdate.current` (integriert ab dem Tick nach dem Entstehen; die an die Sicht weitergereichte `appliedRange` ist gleich). In 8 der 16 Paarungen war
  das am Ausgangsstand formal die erste Abweichung (Bild 1813/1814); sie ist nicht behoben, sondern ausgenommen.

Weiter offen, mit Einordnung:

| Was | Beleg | Einordnung |
|---|---|---|
| Objekte mit zwei KI-Modulen aus der Sicht eines Beobachters | `--reference-diff --at first`: Kurs des abstürzenden Helix 49 ab Bild 778 (der Kurs der Spirale wird nicht veröffentlicht); der Kurs zweier Beweger eines Objekts ist zwischen zwei Bildenden nur ungefähr | später behebbar; am Bildende gleich |
| Regel 7 für die in §3 genannten Module | – | später, je Modul mit seiner Szene |
| Konstanten `Wert je Tick × r` (Kandidat 2) | Spectre, Fallschirm, Leuchtkörper, Partikelstrahl, abgeschossener Jet rechnen weiter so; nur der Chinook-Höhenschritt (Boden) und die Raketen (#12) sind angefasst | offen: keine gemessene erste Abweichung in dieser Runde |
| Heilscheiben je Tick (Kandidat 4) | `RepairDockUpdate` heilt bereits einmal je Bild (`_actionPhase`, vor dieser Runde); `SlavedUpdate` und die Regeneration der Insassen (`ContainModules.cs`, `TransportModules.cs`) sind **nicht** angefasst | offen: keine gemessene erste Abweichung |
| Skript-VM, Spezialfähigkeiten, `SlavedUpdate` als Leser (Kandidat 3) | nicht angefasst | offen |
| `IsSignificantlyAboveTerrain` an den übrigen Lesern | Minen, Kisten, Brücken, `IsAirborne` lesen weiter die augenblickliche Höhe | später, je Stelle |
| Brücken, Wasser, Klippen | kein eigener Fall; die vier Karten des Manifests haben Hänge, auf denen #9/#10 gemessen sind | nicht untersucht |

## 7. Prüfläufe

| Lauf | Ergebnis |
|---|---|
| `input-manifest check`, `reference-manifest check --set quick --jobs 3` | identisch bzw. 9 Läufe identisch für **jeden** Commit, der die Simulation ändert: am Arbeitsstand unmittelbar vor dem Commit für `2837f1e`, `4b1713d`, `3b5c781`, `d9b5490`, `96be02e`, `887b51e`, `e910174`, `134d074`, `ec9f90a`, `8fb48ac`, `66607e2`, `713c662`; nachträglich je Commit in einem Wegwerf-Worktree gebaut und geprüft (weil mehrere Ursachen gemeinsam gemessen und dann getrennt committet wurden): `b000106`, `b62ccc4`, `10e0031`, `7880aed`, `8327cd9`, `43acce8`, `36a2404`, `015e975`, `69e013e` |
| `reference-manifest check --set full --jobs 5`, `input-manifest check` | **50 Läufe identisch**, Eingangsmanifest identisch, am Endstand `713c662` |
| `ai-survey` bei 30 Hz am Endstand gegen den Ausgangsstand | 320 Gefechte, alle 320 Zeilen (Ausgang, Endtick, Befehlszahl, Endhash) identisch zur Erhebung am Ausgangsstand |
| Raten-Äquivalenz-Suite (`--only RateEquivalence`, 4.388 Größen) | jede der zehn Gruppen 0 / 0 nach jedem Commit, der die Simulation ändert (gemessen an den oben genannten Arbeitsständen), und am Endstand |
| Simulationstests, Release, ohne Realdaten, 30 Hz / `--tick-rate 60` / `--tick-rate 120` | jeweils alle 512 grün an jedem der genannten Arbeitsstände |
| Simulationstests, Release, mit Realdaten (`--content-dir`), Endstand | bei 30 Hz, `--tick-rate 60` und `--tick-rate 120` jeweils **alle 542 grün** (am Ausgangsstand bei 120 Hz rot: das Referenzmatch); die Suite darin: jede Gruppe 0 / 0 |
| Simulationstests, Debug, 30 Hz (`dotnet run --project tests/CnCNew.Simulation.Tests`) | alle 512 grün |
| `dotnet build CnCNew.slnx` | 0 Warnungen, 0 Fehler (inkrementell, `bin`/`obj` von `CnCNew.Client.Stride` nicht gelöscht) |
| Leistung bei 30 Hz (`SimTool perf … Twilight Flame --players 8 --ticks 12000`, alt / neu / alt / neu) | Mittel ohne die ersten 300 Ticks: 1045,0 / 1056,8 / 1064,4 / 1057,1 µs je Tick; alt im Mittel 1054,7, neu 1057,0 (+0,2 %; die beiden alten Läufe liegen 1,9 % auseinander): kein messbarer Unterschied. Der Rechner war nicht ruhig gestellt |

Zwischenfall, behoben: die vier Commits `b000106` bis `ec9f90a` waren zuerst mit einem Hunk-Werkzeug ohne Kontext geschnitten; zwei Zwischenstände bauten nicht. Die vier Commits
sind neu geschnitten (gleicher Endbaum), jeder baut und ist einzeln geprüft.

Nicht gemacht oder nicht geprüft: die 16 Paarungen und die Erhebung bei 120 Hz; die Original-KI (`--original-diff`, der Fall aus `RATE-SWITCH-OUTER-TESTS.md` §7.3 Bild 10532) am
Endstand; Kampagne (Zielfahrer, Audit) bei 60 und 120 Hz; `Game.Tests`, `Presentation.Tests`, der Client und die Szenen-Audits (die Kollisionsprüfung im ersten Tick und die
Gruppenlogik des Transporters ändern oberhalb von 30 Hz, wann Ereignisse fallen); eigene Drei-Raten-Tests je Ursache auf der synthetischen Fixture (§5); Leistung bei 60 Hz.
**Kein Mensch hat etwas davon gesehen.**

## 8. Werkzeug

- `SimTool rate-compare <content> --map-diff A,B --map KARTE [--seed N] --fields [--rates X,Y] [--frames N] [--commands N]` und die Optionen von `--fields`: genau das Gefecht von
  `ai-match --map KARTE --factions A,B` (Kataloge des Werkzeugs mit Modellgeometrie, Seeds des Manifests, wenn `--seed` fehlt) bei zwei Raten nebeneinander; `--commands N` druckt
  die Befehle der letzten N Bilder vor dem Ende des Vergleichs (der Zustand der KI liegt außerhalb der Welt und wird nicht verglichen).
- `--slack N`: Festkommawerte, die höchstens N Rohwerte auseinanderliegen, gelten als gleich (um hinter eine bekannte Rundungsabweichung zu sehen; die Messung ist 0).
- `--trace` druckt beim ersten Blick auf ein Objekt den Tick seines Erscheinens und dessen Lage im Bild der Welt.
