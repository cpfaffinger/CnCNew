# Flächenflugzeuge: Mindestgeschwindigkeit, Kurvenradius, Kreisen und die Flugplatzgeometrie

**Hinweis (9. Oktober 2026, Phase 23): historisch, 30 Hz.** Tickzahlen, Siegticks und Hashes in diesem Dokument sind bei 30 Hz und an ihrem damaligen Stand gemessen und als Referenzwerte überholt (maßgeblich: `baselines/reference-manifest.json`, seit `ea52f87` bei 60 Hz). Tickangaben gelten als Zahl von Originalbildern weiter. Oberhalb von 30 Hz entscheiden Flügelflug und die Flugfeld-Zustände einmal je Originalbild mit den Konstanten des 30-Hz-Katalogs (`LocomotorFrames`) und fliegen den Schritt in Anteilen; ein Jet-Einsatz ist über 1.500 Bilder bei 30, 60 und 120 Hz bitgleich: `docs/RATE-INVARIANCE-FLIGHT.md`, `docs/RATE-INVARIANCE-ROUND3-FLIGHT.md` (`bbee8ab`, `60fcb57`, `346cadc`, `cc7fd75`). Nicht gemessen: Flugfelder mit Modellknochen und mehrere Jets an einer Startbahn bei 60/120 Hz.

Stand 4. Oktober 2026 (neu messen). Betrifft `CnCNew.Simulation` (`JetAIUpdate`, `AIUpdateInterface`, `ParkingPlaceBehavior`,
`LocomotorInfo`, `ModelGeometry`), `CnCNew.Game` (`ModelGeometryLoader`, `GameData`), `CnCNew.Presentation` (`StandingOrders`)
und die Audits (`PresentationTool scenario-audit`, `W3dTool bones`). Anlass war die Sichtprüfung des Szenarios `jet-strafe`
(`docs/DEMO-SCENARIOS.md`).

## 1. Ausgangslage (gemessen vor der Änderung)

`PresentationTool scenario-audit … --name jet-strafe` schlug fehl: Nach dem Angriff blieb jeder Raptor 32 bzw. 37 Ticks
bewegungslos in 118 m Höhe stehen (Geschwindigkeit 0), bevor er zurückflog. Ursachen im Code:

* `AIUpdateInterface.Move` beendete eine Flugbewegung mit `Entity.Speed = 0` am Ziel; ein Flugzeug mit `MinSpeed > 0` konnte also
  stehen bleiben und in der Kurve (`goalSpeed = 0` innerhalb des Wendekreises) anhalten.
* Ein untätiges, fliegendes `JetAIUpdate`-Objekt hielt seine Position in der Luft (Kommentar im Quelltext: "holds its position
  in the air instead of circling").
* Der Flugplatz kannte keine Modellpunkte: `ParkingPlaceBehavior` rechnete ein synthetisches Layout (Bahn `b + 80` neben dem
  Gebäude, `3a` lang). Der Raptor rollte rund 154 Einheiten südlich des Flugplatzes über Gras und hob dort ab (Messung des
  Menschen, mit dem Audit reproduziert: Abheben bei (−43, −154), 128 Einheiten von der echten Bahn).

## 2. Referenz

`references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Locomotor.cpp`:

| Funktion | Inhalt |
| --- | --- |
| `LocomotorTemplate::friend_parse`-Nachbearbeitung | "WINGS should always have positive minSpeeds (otherwise, they hover)" |
| `moveTowardsPositionWings` | reicht an `moveTowardsPositionOther` weiter (das optionale Höhenkreisen `CIRCLE_FOR_LANDING` ist abgeschaltet) |
| `moveTowardsPositionOther` | dreht mit `rotateTowardsPosition` höchstens um die Drehrate, schiebt entlang der Blickrichtung, Zielgeschwindigkeit nie unter `MinSpeed`; näher als der Bremsweg am Ziel: `MinSpeed` |
| `maintainCurrentPositionWings` | hält eine Position durch Kreisen: Zielpunkt auf einem Kreis vom Radius `CirclingRadius` um `m_maintainPos`, um `π − π/8` (negativer Radius: gespiegelt) vom Peilwinkel zum Mittelpunkt gedreht, Geschwindigkeit `MinSpeed`; Radius 0 → `calcMinTurnRadius` |
| `calcMinTurnRadius` | `MinSpeed / TurnRate` (bei nicht drehfähigen Locomotoren riesig) |
| `locoUpdate_maintainCurrentPosition` | `m_maintainPos` ist die Position, an der das Halten beginnt |

`MinTurnSpeed` wird für `LOCO_WINGS` nicht ausgewertet (nur `moveTowardsPositionWheels`); die Drehrate gilt bei jeder Geschwindigkeit.
`ParkingPlaceBehavior.cpp` (`buildInfo`, `calcPPInfo`): alle Parkplatz-, Rollbahn- und Hangarpunkte sind Bones des Gebäudemodells
(`getSingleLogicalBonePosition`).

## 3. Umsetzung der Flugphysik

**Betroffene Objekte:** nur Objekte mit `JetAIUpdate` **und** einer Locomotor mit `Appearance = WINGS` (Raptor, Stealth Fighter,
Aurora, MiG samt `AirF_`/`Boss_`/`CINE_`/`Lazr_`/`Nuke_`/`SupW_`/`Tank_`/`Infa_`-Varianten, der Marschflugkörper
`CINE_U06_CruiseMissileFLY` der Zwischensequenzen: 27 Objekte im Retailbestand). Nicht betroffen: Hubschrauber (`Comanche`, `JetAIUpdate`
ohne WINGS), `DeliverPayloadAIUpdate` (B-52, A-10, B-3: trotz WINGS-Locomotor), Spectre-Gunships, Drohnen, Vögel und Raketen
(`AIUpdateInterface`/`MissileAIUpdate`). Der Schalter ist `AIUpdateInterface.FliesWithWings` (virtuell, nur `JetAIUpdate` antwortet
ja, und nur solange das Flugzeug fliegen darf, `AllowAirLoco`; das Rollen mit der Taxi-Locomotor bleibt wie es war).

**Bewegung** (`AIUpdateInterface.MoveWinged`/`SteerWinged`, nur ohne Weg-Planer, geradeaus wie bisher für Luft):

* Der Kurs wird je Tick um höchstens `TurnRatePerTick` zum Ziel gedreht.
* Die Geschwindigkeit nähert sich der Zielgeschwindigkeit mit `Acceleration`/`Braking` und bleibt immer `≥ MinSpeed`.
  Zielgeschwindigkeit: `Speed` (bzw. die Gruppengeschwindigkeit), innerhalb des Bremswegs `(v² − MinSpeed²) / (2·Braking)` bis zum Ziel `MinSpeed`.
* Das Flugzeug rückt in Blickrichtung um seine Geschwindigkeit vor (kein Anhalten bei Kurven, keine Sofortdrehung).
* **Ankunft** ohne Anhalten: der Befehl endet (`HasMoveTarget = false`, `MoveArrived`), wenn das Ziel näher als `max(CloseEnoughDist, Speed)`
  ist **oder** hinter dem Flugzeug innerhalb des doppelten Wendekreises liegt (`2 · Speed / TurnRate`; ein Punkt dort ist nicht
  anfliegbar – die Referenz lässt das Flugzeug ihn umkreisen). Die Geschwindigkeit wird dabei nicht angefasst.

**Kreisen** (`JetAIUpdate.CircleStep`, `maintainCurrentPositionWings`): Ein fliegendes, befehlsloses Flugzeug (Phase `None`, `AwaitLanding`
– es wartet auf die Landebahn –, `CirclingDeadAirfield`) steuert den Punkt auf dem Kreis an. Mittelpunkt: das Ziel des zuletzt
beendeten Befehls, sonst die Position, an der das Kreisen begann. Radius `CirclingRadius` (positiv im Uhrzeigersinn, negativ gegen den
Uhrzeigersinn, 0 = `MinSpeed / TurnRate`). **Gemessen:** die Spur stellt sich auf rund `0,93 · Radius` ein (Raptor: 92,8 bei Radius 100), in
Übereinstimmung mit einer unabhängigen Python-Simulation derselben Vorschrift; Radius 0 liefert den kleinsten Kreis (27–30 für den
Testjet). Die INI-Angabe "pos = clockwise" stimmt (die Umlaufrichtung wurde am Kreuzprodukt gemessen). Ein neuer Befehl (`OrderMove`, Rückflug,
Landung) beendet das Kreisen; die Rückkehr zum Flugplatz bei leerer Munition, der Rückflug nach `ReturnToBaseIdleTime` und die Landung
bleiben unverändert.

**Zustand/Schnappschuss:** `circling`, `circleX`, `circleY` werden nur geschrieben, solange das Flugzeug kreist, und optional gelesen
(`reader.Has`); es gibt keinen Versionssprung (`WorldSnapshot` 8, Replay 8, `CommandTypes.Version` 11 unverändert). Die neuen
`LocomotorInfo`-Felder (`IsWings`, `CirclingRadius`) stehen im Katalog, nicht im Zustand. `CirclingRadius` bleibt ein
zurückgestelltes Feld (`baselines/compatibility.json` unverändert): die allgemeine Grammatik tippt es nach dem Namen als `Distance`; die
Bindung akzeptiert jede Zahlform (`decimal`, `long`, `Distance`) und lehnt anderes mit `TemplateBuildException` ab.

**Angriffsüberflug (Präsentation, `StandingOrders`):** Befehle sind Einzelschritte; der Angriffsbefehl wird je Tick neu abgesetzt. Ein
Jäger überfliegt sein Ziel (Bewegung zum Ziel, Feuern im Waffenband), und bleibt dabei ohne Zutun im Kreis um das Ziel hängen
(Kreisradius ≈ 93 liegt unter `MinimumAttackRange` 100: er könnte nie wieder feuern). Deshalb schickt `AttackRunEgress` ein Flugzeug mit
WINGS-Locomotor, das das Ziel überflogen hat (Ziel hinter ihm, noch in Waffenreichweite), um eine Waffenreichweite geradeaus weiter,
und lässt es erst danach wieder das Ziel anfliegen. Sind alle Waffen, die nur am Flugplatz nachladen (`JetAIUpdate.IsOutOfSpecialReloadAmmo`),
leer, endet der Angriffsbefehl (ein Bewegungsbefehl auf die eigene Position macht das Flugzeug untätig, die Rückkehr übernimmt
`JetAIUpdate` wie bisher). Test (`JetOrderTests`, synthetisch, 2400 Ticks): 12 Überflüge, 133 Schüsse alle im Band 90…330, Ausflug je
≈ 372, nie unter der Mindestgeschwindigkeit; das Raketenflugzeug beendet den Angriff mit der letzten Rakete.

## 4. Flugplatzgeometrie aus den Modell-Bones

Die Simulation darf keine Modelldateien lesen (Schichtung). Gewählt (und warum): eine **injizierte, fingerabdruckgesicherte Tabelle**,
die die Spielschicht beim Laden aus den Skeletten liest.

* `CnCNew.Simulation.Templates.ModelGeometry` (`AirfieldLayout`, `BonePose`): je Flugplatz-Objekt pro Platz `Runway{c}Parking{r}`,
  `Runway{c}Park{r}Han`, `Runway{c}Prep{r}`, je Bahn `RunwayStart{c}`/`RunwayEnd{c}`, optional `HeliPark01`; Position im Objektrahmen
  (x vorwärts, y links, z hoch) und Kurs des Bones. `TemplateCatalog.Build(graph, bindings, geometry = null)` nimmt sie entgegen
  (unbekannte Namen: `TemplateBuildException`); ohne Tabelle (synthetische Fixtures, `SimTool`, `CampaignTool`) bleibt das berechnete
  Layout (`ParkingPlaceBehavior` Kopfkommentar).
* `CnCNew.Game.Data.ModelGeometryLoader` (`GameData.Load`): für jedes Objekt mit `ParkingPlaceBehavior` das Modell des
  Standardzustands laden, Ruhepose mit Identitätswurzel (`W3dSkeleton.RestWorldTransforms`, die "pristine bones" von `W3DModelDraw`), jeder
  fehlende Bone ist ein Fehler. Gerundet auf 1/16 Einheit und 1/1024 rad, damit ein letztes Bit der Float-Matrixprodukte weder Tabelle noch
  Fingerabdruck verändert. Im Retailbestand: 9 Flugplätze, 2 Modelle (`ABArFrcCmd`, `NBAirfield`), je 2 × 2 Plätze und 2 Bahnen.
* **Identität:** `ModelGeometry.Fingerprint` (SHA-256 des kanonischen Texts, Rohwerte) wird mit `Combine` in den Definitions-Fingerabdruck
  gefaltet, gegen den Welt, Replay und Spielstand geprüft werden (`GameData.Fingerprint`). Die Tabelle ist reproduzierbar (zweimaliges Laden
  byteidentisch, Test `ModelGeometryTests`) und enthält keine Pfade oder Zeitstempel. Ohne Tabelle bleibt der Fingerabdruck unverändert.
* `ParkingPlaceBehavior.InfoOfSpace` (Referenz `calcPPInfo`): Parkplatz = Parking-Bone (oder Hangar-Bone mit `ParkInHangars`), `RunwayPrep` =
  Prep-Bone, Start/Ende = Bahn-Bones der Spalte, Anflugpunkt `0,75` Bahnlängen hinter dem Ende auf `ApproachHeight`, Ausrichtung = Kurs
  des Bones. Passen Zeilen/Spalten oder der Name nicht zur Tabelle, wirft das Modul `InvalidDataException` (kein stiller Rückfall).
* Abheben und Landen: das Flugzeug rollt zum Bahnanfang, beschleunigt entlang der Bahn und steigt mit dem Quadrat des durchrollten
  Bahnanteils auf `ApproachHeight` (Referenz: `m_maxLift · ratio²`); die Landung gleitet auf das Bahnende und rollt zum Anfang aus (Spiegelbild).
  Die Bahn der echten Modelle ist kurz (`ABArFrcCmd`: 79 Einheiten), das Flugzeug hebt nach ≈ 25 Einheiten Rollstrecke ab (Audit) und erreicht die Anflughöhe am Bahnende.

**Höhe am Boden.** Die Referenz setzt abgestellte und rollende Flugzeuge auf die Höhe ihres Flugplatzes (`LandingDeckHeightOffset` gilt nur
für Träger; `AmericaAirfield` hat 0). Die Simulation nimmt jetzt die **Flugplatzhöhe** (`ParkingPlaceBehavior.ApronHeight`: Objekthöhe + Deckversatz)
statt der Geländehöhe unter dem Flugzeug, das Modell des Rollfelds liegt auf der Höhe seines Ursprungs. Auf der Testkarte (Alpine Assault) ist
das Gelände dort flach (18,8 an allen 13 × 8 Stichproben), also ist der Audit-Wert vorher und nachher gleich: **ein Einsinken ließ sich in der
Simulation nicht messen.** Gemessen am Modell (`W3dTool bones AVRaptor`, "mesh extent"): der Raptor reicht in der Ruhepose 1,95 Einheiten
**unter** seinen Ursprung (z −1,95 … 5,31); er steckt am Boden also so tief im Rollfeld. Das ist ein Darstellungsproblem. Die Simulationshöhe
anzuheben wäre falsch (`IsSignificantlyAboveTerrain` schwelle ≈ 0,64: abgestellte Jets würden zu Luftzielen, und `onDie` des Flugplatzes
unterschiede sie nicht mehr von fliegenden); die Hebung gehört in die Zeichnung (Stride-Schicht, z. B. um den Modell-Tiefgang für Flugzeuge am Boden).

## 5. Tests und Audits

* `tests/CnCNew.Simulation.Tests/Tests.JetFlight.cs`: Mindestgeschwindigkeit/kein Stehen/Hubschrauber unverändert; Drehrate und Schleife; Kreisen
  (im Uhrzeigersinn, dagegen, kleinster Kreis, schwache Bremse); Schnappschuss mitten im Kreis; Flugplatz-Lebenszyklus mit WINGS-Locomotor.
  `Tests.AirModelGeometry.cs`: handgemachte Bone-Tabelle (Hangar, Parken, Rollen, Abheben auf der Bahnlinie, Landen, Schnappschüsse mit gefaltetem
  Fingerabdruck, falsche Abmessungen und unbekannter Name werden abgelehnt). `Tests.JetFlightReal.cs` (`--content-dir`): echter Raptor, MiG,
  Stealth, Aurora fliegen ein Quadrat (Spur lückenlos, Geschwindigkeit in `[MinSpeed, Speed]`, Drehrate eingehalten, danach Kreisen um den letzten
  Punkt), Comanche und B-52 unverändert, zwei Läufe identisch.
* `tests/CnCNew.Presentation.Tests/JetOrderTests.cs` (Abschnitt 3), `tests/CnCNew.Game.Tests/ModelGeometryTests.cs` (`--content-dir`: neun
  Flugplätze, Bones von `AmericaAirfield` gegen `W3dTool bones`, Fingerabdruck gefaltet, zweimal laden identisch).
* `PresentationTool scenario-audit … --name jet-strafe`: prüft zusätzlich zur Schwebe-Prüfung die Mindestgeschwindigkeit in der Luft, die Rollhöhe,
  dass das Abheben, die ganze Startrolle und die Landung auf einer Bahnlinie des Modells liegen (Toleranz 12) – mit `--synthetic-airfield`
  (Flugplatz ohne Modell-Bones) schlagen genau diese Prüfungen fehl (6 Fehler, Abheben 128/183 Einheiten neben der Bahn), mit den Bones laufen sie durch.
* `W3dTool bones <content> <modell> [präfix…]` druckt Bone-Positionen/-Kurse und die Modellausdehnung der Ruhepose.

## 6. Messwerte (4. Oktober 2026, Release, vorher = Stand `c54aa9a`)

| Messung | vorher | nachher |
| --- | --- | --- |
| `SimTool reference-match` | Sieger FactionGLA, Tick 13.890, Endhash `76F22CB0CA79BEEF`, Kette `35F54CAD40A43E6E` | identisch |
| `SimTool stress` (216.000 Ticks) | `56882F763EABEF67`, Kette `B8E663493F2C73D5` | identisch |
| `SimTool ai-match` Alpine Assault, `FactionAmericaAirForceGeneral,FactionChina`, 45.000 Ticks | Patt, Endhash `AF78F914D08B09DD`, Kette `68A8ABB3A7BCFF0C` | identisch, deterministisch, Schnappschuss-Fortsetzung identisch |
| dasselbe mit vertauschten Seiten | Patt, `2B3E66FDB102E504`, Kette `084775445341A724` | identisch |
| `CampaignTool audit --campaign CHALLENGE_5 --map GC_AirGeneral --ticks 900` / `--map GC_Stealth` | 1 Mission deterministisch, identisch nach Speichern/Laden, 0 fehlende Treffer | identisch (gleiche Zahlen) |
| `scenario-audit … --name jet-strafe` | 2 Fehler (Schweben 32/37 Ticks); Raptor 1 starb | alles bestanden: kein Schweben, niedrigste Fluggeschwindigkeit 60,0 bis 65,3/s, beide Raptors starten auf der Bahn, feuern 4 Raketen, landen, laden nach |
| Baseline der Kompatibilitätsliste | 751 Einträge | 751 (kein Feld neu typisiert) |

Die Hashes der Läufe bleiben gleich, weil in diesen Szenarien kein Flächenflugzeug fliegt (die Skirmish-KI baut keine Jets; die geprüften
Kampagnenkarten setzen in 900 Ticks keine ein). Die Flugänderung wirkt in `jet-strafe`, in den Flug-Tests und im Client. Der Fingerabdruck
der Client-Welt (`GameData.Fingerprint`) ändert sich durch die Bone-Tabelle (neue Spielstände/Replays; alte passen nicht mehr).
Für das Szenario `jet-strafe` wurde das Ziel von `trooper2` von `raptor1` auf `raptor2` gelegt: Beide Missile Defender schossen auf denselben
Jet und töteten ihn; so bleibt der Ablauf "starten, feuern, zurückfliegen, landen, nachladen" für beide Jets prüfbar.

## 7. Bekannte Grenzen

* Die Flugbahn ist kinematisch (kein Physikmodell mit Auftrieb/Rollen/Nicken); das Kreisen hat die Eigenschaft der Referenz (Jagd auf einen Kreispunkt),
  nicht ihre Zahlen auf die Float-Stelle genau. Beschädigte Locomotoren (`SpeedDamaged`, `TurnRateDamaged`, `AccelerationDamaged`, `LiftDamaged`) wirken seit dem 4. Oktober 2026 ab `MovementPenaltyDamageState` (`docs/SIM-FIRING-TRACKER.md` §4), auch auf den gewählten Jet-Satz.
* Der Marschflugkörper `CINE_U06_CruiseMissileFLY` ist ein `JetAIUpdate`-Objekt mit WINGS-Locomotor und kreist jetzt am Ziel, statt stehen zu bleiben.
* Die Landung richtet den Kurs weiter am Landebahnbeginn aus (kein eigener Anflugbogen); der Anflugpunkt wird durch den Wendekreis nur grob eingehalten. Bewusst zurückgestellt: ein Anflugbogen ändert nur die Flugbahn der letzten Sekunden und ist ohne Sichtabnahme der Jet-Demo (`scripts/demo-jet-strafe.ps1`) nicht beurteilbar; er kommt nach dieser Abnahme.
* `SimTool` und `CampaignTool` bauen den Katalog seit dem 4. Oktober 2026 mit der Modell-Bone-Tabelle (`ModelGeometryLoader`, wie `GameData.Load`); ihr Welt-Fingerabdruck stimmt mit dem des Clients überein.
  Das Referenzspiel blieb dabei tick- und hashgleich (kein Flugplatz im Szenario).
* Das Einsinken am Boden (Modell-Tiefgang 1,95) ist nur in der Zeichnung zu beheben (Abschnitt 4).
* Die Spielbarkeit durch einen Menschen ist damit nicht belegt; Rauchtests und Audits beweisen Reproduzierbarkeit, nicht Richtigkeit.
