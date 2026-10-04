# Modellbedingungen der Gebäude, Docks, Türen und Packzustände

Stand: 4. Oktober 2026 (neu messen). Gehört zu `docs/PRESENTATION-OBJECTS.md` (Zustandsableitung) und `docs/CLIENT-OBJECTS.md`
(Weltdarstellung); Auslöser ist `docs/DEMO-FINDINGS.md` (Modellzustände, die nie gesetzt werden, zeigen falsche oder fehlende
Modellteile). Dieses Dokument behandelt die 27 Namen der Gebäude-, Tür-, Dock-, Pack- und Weltzustände, die `PresentationTool model-audit
--structures` als „never set“ meldete (25 sind jetzt abgeleitet, 2 nicht ableitbar).

## 1. Ergebnis

`dotnet run -c Release --project tools/CnCNew.PresentationTool -- model-audit <dir> --structures`:

| | vorher | nachher |
|---|---|---|
| von Zuständen benutzte Namen | 106 | 106 |
| von der Präsentation gesetzt (oder `ModelConditionUpgrade`) | 52 | 77 |
| nie gesetzt | **54** | **29** |

25 Namen verlassen die Liste: `DOOR_1..4_OPENING`, `DOOR_1..4_CLOSING`, `DOOR_1..4_WAITING_OPEN`, `DOOR_1..3_WAITING_TO_CLOSE`
(15 Türnamen), `CONSTRUCTION_COMPLETE`, `DOCKING_ACTIVE`, `PACKING`, `UNPACKING`, `CAPTURED`, `SMOLDERING`, `POST_COLLAPSE`,
`SPECIAL_DAMAGED`, `ENEMYNEAR`, `OVER_WATER`. Aus dem Auftrag bleiben **zwei** Namen offen (Abschnitt 5): `PREORDER` und
`DOCKING_BEGINNING`. Die übrigen 27 Namen der Liste gehören nicht zu diesem Auftrag (Einheitenzustände: Vorangriff, Dauerfeuer, Düsen,
Freifall, Überrollen, Klettern, Lenken, Explosionsarten, …).

`PACKING`/`UNPACKING` sind **nur teilweise** abgedeckt: 22 der 58 Objekte mit diesen Zuständen (4 Einsatzgeräte, 5 Hacker, 8
Partikelkanonen-Varianten, 5 Reparaturdrohnen) bekommen sie, die 36 Objekte mit `SpecialAbilityUpdate` (Ranger, Colonel Burton, Black
Lotus, Rebell, Roter Gardist, CIA-Offizier und ihre Varianten) nicht – siehe Abschnitt 5. `DOOR_1_OPENING` ist ebenso nur teilweise
abgedeckt (Abschnitt 5: Baikonur, Flugdeck, Containertüren).

## 2. Prinzip

Der Client setzt diese Bits im Original imperativ aus vielen Modulen heraus. Hier ist jedes Bit eine reine Funktion des Zustands, den ein
Simulationsmodul ohnehin hält, oder – wo die Bits einer Zeitschaltung folgen, die die Simulation nicht kennt – eine kleine Präsentations-
Verfolgung, die einmal je simuliertem Tick fortgeschrieben wird. Beides verändert die Simulation nicht.

* `StructureConditions.Apply` (`src/CnCNew.Presentation/Objects/StructureConditions.cs`, aufgerufen am Ende von `ConditionDeriver.Derive`)
  liest den Zustand des jeweiligen Moduls: Siloklappe, Strategiezentrum, Spectre, Technikgebäude, `EnemyNearUpdate`, Einsturz, Hubschrauber-
  wrack, Partikelkanone, Zug, Einsatzgerät, Hacker, Docks. Zustandslos, deterministisch, jederzeit neu berechenbar.
* `StructureConditionTracker` (selbe Datei, aufgerufen von `FrameProjector.CaptureEntity`, Ergebnis wird mit der Ableitung vereinigt)
  hält je Entität eine kleine Tabelle: die Produktionstüren, `CONSTRUCTION_COMPLETE`, die Türverriegelungen des Bombers und des
  Kontrollpostens, den Reparaturarm der Drohne. Die Tabelle wird über Zeitstempel fortgeschrieben, nicht über Tickzähler; eine Lücke
  zwischen zwei Abtastungen (Objekt außer Sicht) verliert keinen Übergang. `Prune` räumt Einträge verschwundener Entitäten (wie `TurretTracker`).
* Die Simulation wurde nur **lesend** erweitert (ein Commit, `feat(simulation): read-only state …`): öffentliche Eigenschaften an Modulen,
  die den Zustand bereits halten, dazu die Türzeiten von `ProductionUpdate` (`ProductionData`, im Katalog `Opaque`, daher aus dem
  Originaltext gelesen – keine Katalogänderung, kein Hash der Definitionen ändert sich) und `LocomotorInfo.IsHover`. Kein Snapshot-Feld, kein
  Replay-Feld, kein Tick ändert sich. Gemessen: Retail-Referenzmatch Sieger FactionGLA bei Tick 13.890, Endhash `76F22CB0CA79BEEF`, Kette
  `35F54CAD40A43E6E` (unverändert); Stresstest Endhash `56882F763EABEF67`, Kette `B8E663493F2C73D5` (unverändert).

## 3. Die Türen der Produktionsgebäude

Quelle: `ProductionUpdate.cpp` (`update`, `updateDoors`, `setHoldDoorOpen`), `ParkingPlaceBehavior.cpp` (`buildInfo`, `reserveDoorForExit`,
`reserveSpace`, `releaseSpace`, `purgeDead`), Daten `NumDoorAnimations`, `DoorOpeningTime`, `DoorWaitOpenTime`, `DoorCloseTime`,
`ConstructionCompleteDuration` (Millisekunden, als `parseDurationUnsignedInt` aufgerundet auf Ticks).

**Original.** Ist die Einheit fertig und der Ausgang frei (`reserveDoorForExit`), öffnet `update` die Tür des Ausgangs (`DOOR_n_OPENING`),
setzt `CONSTRUCTION_COMPLETE`, und **erzeugt die Einheit erst, wenn die Tür vollständig offen ist** (`m_doorWaitOpenFrame != 0`). Danach
hält `updateDoors` die Tür `DoorWaitOpenTime` offen (`DOOR_n_WAITING_OPEN`, bei jeder weiteren Einheit erneuert), schließt sie
`DoorCloseTime` lang (`DOOR_n_CLOSING`) und löscht dann alle Bits. Ist die Tür beim nächsten fertigen Stück noch im Schließen, springt sie
auf „wartet offen“ zurück. `setHoldDoorOpen` (Flughafen) hält eine Tür offen und öffnet eine geschlossene sofort. Ein Ausgang liefert immer
Tür 1 (`DefaultProductionExitUpdate`, `QueueProductionExitUpdate`, `SupplyCenterProductionExitUpdate`, `SpawnPointProductionExitUpdate`);
nur `ParkingPlaceBehavior` liefert die Tür des ersten freien Stellplatzes (Tür = Zeile mal Spalte, in Reihenfolge von `buildInfo`).

**Simulation.** `ProductionUpdate` hat keine Tür: die Einheit entsteht in dem Tick, in dem der Eintrag fertig ist. Die Verfolgung läuft
deshalb auf der Uhr der Simulation und lässt die Tür **`DoorOpeningTime` vor dem Erscheinen** der Einheit beginnen (die Restzeit des
ersten Eintrags der Warteschlange ist bekannt: `ProductionUpdate.TryGetHead`). Die Tür ist damit offen, wenn die Einheit erscheint; ab
da gilt `updateDoors` Schritt für Schritt (gleiche Übergänge, gleiche strikten Vergleiche, gleicher Rücksprung einer schließenden Tür,
gleiches Offenhalten). Der ganze Ablauf ist gegenüber dem Original um `DoorOpeningTime` nach vorn verschoben (Öffnen beginnt früher,
Schließen auch); die Gesamtdauer der Einheit ist die der Simulation. **Das ist eine Näherung**: sie zeigt die Einheit durch eine offene
Tür statt aus einer geschlossenen; die Simulation lässt nichts auf die Tür warten. Kann der Ausgang nicht frei geben
(`IProductionExit.IsFreeToExitFor`: Flughafen voll, Warteschlangenausgang in der Pause), öffnet sich nichts; ein fertiges Stück, das
wartet, hält die Tür offen (wie die Wiederholung von `update` im Original). `PRODUCED_AT_HELIPAD` braucht keine Tür (`DOOR_NONE_NEEDED`).

**Flughafen.** Der Stellplatz `k` hat Tür `k+1`. Eine Tür ist „gehalten“, solange der Platz besetzt oder für einen Ausgang reserviert ist
(`ParkingPlaceBehavior.OccupantOf`, `IsReservedForExit`); gehalten öffnet sie sich und bleibt offen (`setHoldDoorOpen`). Wird der Platz frei, ist
die Wartezeit meist längst abgelaufen und die Tür schließt sofort (ebenso im Original: `now - m_doorWaitOpenFrame > m_doorWaitOpenTime`).

**`CONSTRUCTION_COMPLETE`.** Beginnt zusammen mit dem Öffnen der Tür (oder im Tick der Entstehung, wenn das Gebäude keine Türen hat) und endet,
wenn mehr als `ConstructionCompleteDuration` vergangen ist.

Gebäude, deren `ProductionUpdate` keine `NumDoorAnimations` hat (Propagandazentrum, Attrappen der Waffenhändler), zeigen auch im Original
nie Türbits, obwohl ihre Modelle Türzustände enthalten: `ProductionUpdate::update` fasst die Türen nur bei `m_numDoorAnimations > 0` an.

## 4. Die Namen im Einzelnen

| Name | Status | Quelle im Original (Datei, Funktion) | gelesener Simulationszustand |
|---|---|---|---|
| `DOOR_1_OPENING`, `_WAITING_OPEN`, `_CLOSING` (Kommandozentralen, Waffenfabriken, Waffenhändler, Flughäfen: die 34 Objekte mit `NumDoorAnimations` über null) | implementiert (Näherung, Abschnitt 3) | `ProductionUpdate.cpp` `update`, `updateDoors` | `ProductionUpdate` Kopf der Warteschlange, `Data` (Türzeiten), `IProductionExit` |
| `DOOR_1..4_*` des Flughafens | implementiert (Näherung) | `ParkingPlaceBehavior.cpp` `buildInfo`, `reserveSpace`, `releaseSpace`; `ProductionUpdate.cpp` `setHoldDoorOpen` | `ParkingPlaceBehavior.OccupantOf`, `IsReservedForExit`, `SpaceCount` |
| `DOOR_1_OPENING/_WAITING_OPEN/_WAITING_TO_CLOSE/_CLOSING` der Atomrakete | implementiert | `MissileLauncherBuildingUpdate.cpp` `switchToState` | `MissileLauncherBuildingUpdate.Door` |
| `DOOR_1..3_OPENING/_WAITING_TO_CLOSE/_CLOSING` des Strategiezentrums | implementiert | `BattlePlanUpdate.cpp` `setStatus` (Tür n = Plan n) | `BattlePlanUpdate.Status`, neu `CurrentPlan` |
| `DOOR_1_OPENING/_CLOSING` der Spectre | implementiert | `SpectreGunshipUpdate.cpp` `initiateIntentToDoSpecialPower`, `update` (Bahn), `disengageAndDepartAO` | `SpectreGunshipUpdate.Status` |
| `DOOR_1_OPENING/_CLOSING` der Frachter und Bomber | implementiert (mit Latch) | `DeliverPayloadAIUpdate.cpp` `DeliveringState::onEnter`/`onExit` | neu `DeliverPayloadAIUpdate.DoorsOpen`; „schon einmal geöffnet“ merkt der Tracker |
| `DOOR_1_OPENING/_CLOSING` des Kontrollpostens | implementiert (mit Latch) | `CheckpointUpdate.cpp` `update` (nur bei Wechsel von Freund/Feind) | neu `CheckpointUpdate.AllyNear`, `EnemyNear` |
| `CONSTRUCTION_COMPLETE` | implementiert | `ProductionUpdate.cpp` `update` | wie Türen, `ConstructionCompleteTicks` |
| `DOCKING_ACTIVE` (Docker) | implementiert | `DockUpdate.cpp` `onDockReached`/`onExitReached`; `AIDock.cpp` `AIDockMoveToDockState::onExit` | neu `AIUpdateInterface.IsDockActive` (Zustand `ProcessDock`) |
| `DOCKING_ACTIVE` und `DOCKING` (Dock) | implementiert | `DockUpdate.cpp` `onEnterReached`/`onDockReached`/`onExitReached` (setzen dieselben Bits auf `me` und `docker`) | neu `SimulationWorld.GetDockActivity` (`DockerInside`, aktiver Docker im Zustand `ProcessDock`) |
| `PACKING`/`UNPACKING` der Einsatzgeräte (Atomkanone, Patriot-artige) | implementiert | `DeployStyleAIUpdate.cpp` `setMyState` | neu `DeployStyleAIUpdate.IsUnpacking`/`IsPacking` |
| `PACKING`/`UNPACKING` (+ `FIRING_A`) der Hacker | implementiert | `HackInternetAIUpdate.cpp` `UnpackingState`, `HackInternetState`, `PackingState` | neu `HackInternetAIUpdate.IsUnpacking`/`IsPacking`, `IsHacking` |
| `PACKING`/`UNPACKING`, `DEPLOYED` der Partikelkanone | implementiert | `ParticleUplinkCannonUpdate.cpp` `update`, `setLogicalStatus` | neu `ParticleUplinkCannonUpdate.StatusAt` (Uhr, Bereitschaft der Kraft, Strahlzeiten) |
| `PACKING`/`UNPACKING` (+ `FIRING_B`/`FIRING_C`) der Reparaturdrohne | implementiert (mit Latch) | `SlavedUpdate.cpp` `onObjectCreated`, `endRepair`, `setRepairState`, `setRepairModelConditionStates` | neu `SlavedUpdate.Repair`, `CanRepair` |
| `CAPTURED` | implementiert | `TechBuildingBehavior.cpp` `update`, `onDie` | Besitzer ist eine spielbare Seite (`SimulationWorld.IsPlayableSide`), Objekt lebt |
| `SMOLDERING` | implementiert | `FlammableUpdate.cpp` `update` (setzt `BURNED` und `SMOLDERING` zusammen, nichts löscht sie) | Objektstatus `BURNED` |
| `POST_COLLAPSE` (Einsturz) | implementiert; löscht `RUBBLE` wie das Original | `StructureCollapseUpdate.cpp` `update`; `StructureToppleUpdate.cpp` `update` | `StructureCollapseUpdate.IsCollapsed`, neu `StructureToppleUpdate.IsToppled` (siehe Abschnitt 6) |
| `POST_COLLAPSE` (Lawinenbrocken) | implementiert | `TensileFormationUpdate.cpp` `update` (jeder Lauf nach dem Auslösen) | `TensileFormationUpdate.Enabled` |
| `SPECIAL_DAMAGED` | implementiert | `HelicopterSlowDeathUpdate.cpp` `update` (nach dem Aufprall) | neu `HelicopterSlowDeathBehavior.HasHitGround` |
| `ENEMYNEAR` | implementiert | `EnemyNearUpdate.cpp` `update` | `EnemyNearUpdate.IsEnemyNear` |
| `OVER_WATER` (Hover) | implementiert | `Locomotor.cpp` `moveTowardsPositionHover` | neu `LocomotorInfo.IsHover` (`Appearance = HOVER`), `TerrainMap.IsUnderwater` an der Position |
| `OVER_WATER` (Zug im Tunnel) | implementiert | `RailroadGuideAIUpdate.cpp` `update` (Position mehr als 3 unter dem Boden) | `RailroadBehavior` vorhanden, Höhe der Entität gegen `GroundHeightAt` |
| `DOCKING_BEGINNING` | **nicht ableitbar** | `DockUpdate.cpp` `onEnterReached`..`onDockReached` | siehe Abschnitt 5 |
| `PREORDER` | **nicht ableitbar** | `PreorderCreate.cpp` `onBuildComplete`, `Player.cpp` (`playerIsPreorder`) | siehe Abschnitt 5 |
| `DOOR_4_WAITING_TO_CLOSE` | wird von keinem Modul gesetzt und von keinem Zustand benutzt | – | – |

## 5. Nicht ableitbar, und warum

* **`DOCKING_BEGINNING`.** Gesetzt zwischen `onEnterReached` und `onDockReached`, also solange der Docker vom Eingang zum Dockpunkt fährt.
  In der Simulation fallen Eingang, Dockpunkt und Ausgang zusammen (`DockMachine`: keine Knochen, alle Wege sind die Position des
  Dockers), `MoveToEntry` und `MoveToDock` gehen im selben Tick durch. Die Spanne hat keinen Tick Länge. Gebraucht wird sie nur von
  Alias-Zuständen des Jarmen Kell (`MOVING DOCKING_BEGINNING` ist Alias von `MOVING`), sichtbar fehlt also nichts. Wer die Strecke
  braucht, muss der Simulation Dockgeometrie geben.
* **`PREORDER`.** `PreorderCreate::onBuildComplete` setzt das Bit, wenn `Player::didPlayerPreorder()` gilt; das ist das Schlüsselwort
  `playerIsPreorder` des Spielerverzeichnisses einer Karte, gesetzt aus dem Online-Profil eines Spielers, der das Spiel vorbestellt hat.
  Die Simulation hat diese Eigenschaft nicht, offline ist sie nie wahr. Die Zustände sind die Vorbesteller-Dekoration der Kommandozentralen
  und bleiben ungenutzt – so zeigt sie auch das Original einem Spieler ohne Vorbestellung.
* **`PACKING`/`UNPACKING` für `SpecialAbilityUpdate`** (36 Objekte: Ranger, Colonel Burton, Black Lotus, Rebell, Roter Gardist und
  Varianten). Das Original führt vor der Fähigkeit eine Auspack-/Zielanfahrt-/Einpack-Zustandsmaschine
  (`SpecialAbilityUpdate.cpp` `startUnpacking`, `startPacking`, `handlePackingProcessing`); die Simulation hat für das Modul nur die
  Wiederaufladung (`SpecialAbilityUpdate : SpecialPowerModuleBase`, Klassenkommentar: „none of that per-ability staging is modelled“).
  Ohne die Phase gibt es keinen Zustand, aus dem man ein Bit ablesen könnte. Fehlt: die Phasen mit ihren Zeiten (`UnpackTime`, `PackTime`,
  `PackUnpackVariationFactor`, ein Zufallswert je Phase) und die Anfahrt.
* **`DOOR_1_OPENING` für `BaikonurLaunchPower`** (23 Objekte, vor allem Kinematik-Requisiten). `doSpecialPower` ohne Ort setzt das Bit und
  löscht es nie; der Ablauf mit Ort (`doSpecialPowerAtLocation`) setzt es nicht. Die Simulation hält nicht fest, ob die Kraft einmal ohne Ort
  benutzt wurde (`BaikonurLaunchPower.Apply` kehrt dann nur zurück). Ein Bit dafür wäre ein neues, serialisiertes Feld (Hashänderung bei
  Läufen, die die Kraft so benutzen), das hier bewusst nicht angelegt wurde.
* **`DOOR_2..`/`DOOR_1_*` des Flugzeugträgers** (`FlightDeckBehavior.cpp`, ein Objekt). Die Simulation hat ein leeres
  `FlightDeckBehavior` (Klassenkommentar: Schale), der Träger startet und landet nichts.
* **`DOOR_1_OPENING`/`_CLOSING` der Amphibie und der Fähre** (`OpenContain.cpp` `exitObjectViaDoor`, `exitObjectInAHurry`, ein bis zwei
  Objekte). Die Container der Simulation entfernen Insassen ohne Türphase; `DoorOpenTime` ist im Katalog `Opaque` und wird nirgends gelesen.

## 6. Auffälligkeiten und Grenzen

* **`StructureToppleUpdate` erreicht `TOPPLESTATE_DONE` (behoben).** Bei der Retail-`StructuralIntegrity` von 0,75 beträgt die Winkelbeschleunigung
  des ersten Ticks `0,02 · sin(0,001) · 0,25 ≈ 5 · 10⁻⁶` und liegt unter der Auflösung von `Fixed` (1/65536); mit `Fixed`-Feldern blieb die
  Geschwindigkeit null (`TallTower02` war nach 60.000 Ticks noch `Toppling`). Winkel und Winkelgeschwindigkeit führen jetzt 32 zusätzliche
  Nachkommabits (das Produkt der drei `Fixed`-Rohwerte ist bei 48 Bits exakt); ein Turm mit den Retailwerten liegt nach rund 104 Ticks Fall
  flach (`ln(π/2 / 0,001) / √(0,02 · 0,25)`), der Snapshot schreibt die Zusatzbits nur, wenn sie ungleich null sind (stehende Gebäude
  behalten ihren Zustandshash). Der Test verlangt `POST_COLLAPSE` für `TallTower02` jetzt unbedingt.
* **Die Produktionstüren sind eine Näherung** (Abschnitt 3): um `DoorOpeningTime` nach vorn verschoben, Einheit erscheint durch die offene Tür.
  Ein Mensch muss prüfen, ob das Öffnen der Tür gegen die Animationslänge der Modelle (`DoorOpeningTime` ist die Spielzeit, das
  Modell spielt seine Animation in dieser Zeit) stimmt.
* **`OVER_WATER` für Hover** ändert das Original nur, solange sich das Objekt bewegt; stehend behält es den letzten Wert. Hier folgt das
  Bit der Position. Gleich, außer ein Objekt wird ohne Bewegung auf Land oder Wasser gesetzt. Jeder Hubschrauber mit `Appearance = HOVER`
  bekommt das Bit über Wasser (wie im Original; kein Zustand verwendet es).
* **Partikelkanone:** der Reset-Fall des Originals (eine bereite Kanone, deren Uhr zurückgesetzt wird, packt sich ein) wird nicht gelesen:
  Leerlauf. Die Antennenphasen kommen aus der Uhr, nicht aus einem gespeicherten Zustand, genau wie im Original.
* **Reparaturdrohne:** der Arm folgt `setRepairModelConditionStates` mit einem Bit zugleich: `PACKING` (Ruhe), `UNPACKING` (Auspacken und
  Bereitschaft danach), `FIRING_B` (Ausfahren und Schweißen), `FIRING_C` (Einfahren und Bereitschaft danach). Die beiden `FIRING_*` sind
  hier also die des Reparaturarms, nicht die der Waffenableitung (`ConditionDeriver.Weapon`).
* **`OVER_WATER` ohne Gelände:** ohne `TerrainMap` gibt es kein Wasser, ein Schwebefahrzeug zeigt das Bit dann nie.
* **Hacker:** `FIRING_A` während des Hackens wird mit dieser Ableitung gesetzt (`HackInternetState::onEnter`), ohne `USING_WEAPON_A`.
* **Kontrollposten und Bomber** brauchen die Abtastung jedes Ticks, um den Latch zu führen; ein Kontrollposten, den der Spieler erst nach
  einem Wechsel sieht, zeigt den Zustand des ersten gesehenen Wechsels.
* **Frischer Sweep:** `RealSweep` (alle 2.115 Retail-Objekte, 6 Ticks, `FrameProjector`) wirft nichts und zeigt `CAPTURED` ×7 (Technik-
  gebäude mit Spielerbesitz), `PACKING` ×4 (Drohnen), `DOOR_1_OPENING` ×1 (ein Flughafen, dem ein Flugzeug seinen Platz gibt),
  `DOOR_1_CLOSING` ×1 (Kontrollposten mit Feind in Sicht) und `SPECIAL_DAMAGED` ×2 (CINE-Comanche, die als Wrack entstehen).

## 7. Tests und Messungen

`tests/CnCNew.Presentation.Tests/StructureConditionTests.cs` (eine eigene Suite, 11 synthetische Tests, mit `--content-dir` ein zwölfter):

* Türzyklus der Waffenfabrik Tick für Tick gegen `DoorOpeningTime`/`DoorWaitOpenTime`/`DoorCloseTime`/`ConstructionCompleteDuration`;
  zweite Einheit hält die Tür offen; eine schließende Tür springt zurück; Gebäude ohne Türen zeigen nichts.
* Flughafen: Platzhaltung öffnet die Tür und hält sie, Freigabe schließt sofort; Produktion öffnet die Tür des ersten freien Platzes.
* Kontrollposten (kein Bit ohne Wechsel; Freund: `OPENING`; Feind: `CLOSING`) und `ENEMYNEAR`.
* Versorgungsdock: `DOCKING` und `DOCKING_ACTIVE` auf Docker und Dock über dieselben Ticks (Lager und Zentrale), kein `DOCKING_BEGINNING`.
* Bomber, Partikelkanone (Stufen und Bits Tick für Tick, mit Feuern), Reparaturdrohne (Reihenfolge der Armbits `UNPACKING` → `FIRING_B` → `FIRING_C` → `PACKING`), Hover über einem See.
* Lücken zwischen Abtastungen und `Prune`.
* Mit Retaildaten: `AmericaWarFactory` (Türzeiten 98/90/120 Ticks, `CONSTRUCTION_COMPLETE` 45 Ticks, Zeitleiste
  `204: OPENING CONSTRUCTION_COMPLETE | 303: WAITING_OPEN | 394: CLOSING | 515: leer`), Strategiezentrum (Tür 2 für „Halten der Linie“),
  Atomrakete (Tür vor Bereitschaft), Atomkanone (Aufklappen), Ölturm (`CAPTURED`), Baum (`SMOLDERING`), Bank (Einsturz → `POST_COLLAPSE`
  statt `RUBBLE`), Hacker (Aufbauen → `FIRING_A` → Einpacken), Comanche, Zug im Tunnel, und der Sweep über alle Objekte.

Der Zustand aller Suiten bei Abschluss: Simulation (einfach und mit `--content-dir`), Präsentation, Game (`--content-dir`), Ui, Data
(`--content-dir`) bestanden; sauberer Solution-Build 11 Warnungen (die erklärten Stride-Hinweise), 0 neue. Referenzmatch und Stresstest
unverändert (Abschnitt 2).
