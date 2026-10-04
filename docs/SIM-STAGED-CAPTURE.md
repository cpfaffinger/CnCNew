# Gestufte Gebäudeübernahme (`SpecialAbilityUpdate`)

Stand: 4. Oktober 2026. Betrifft `CnCNew.Simulation` (`Modules/BuiltIn/SpecialAbilityStaging.cs`, `World/SimulationWorld.PowerAndCapture.cs`),
`CnCNew.Presentation` (`Objects/CaptureConditions.cs`) und das Demo-Szenario `capture-building` (`docs/DEMO-SCENARIOS.md`).

## 1. Befund und Ziel

Die erste Tech-Demo zeigte, dass die Übernahme eines Gebäudes sofort geschah: `CaptureCommand` rief `ExecuteCapture` auf, `SpecialAbilityUpdate` war eine
reine Hülle mit dem Aufladetimer der Spezialkraft (`docs/GAME-RULES.md` §6d), und die Modellzustände `UNPACKING`, `RAISING_FLAG` und `PACKING` wurden nie gesetzt
(`PresentationTool scenario-audit --name capture-building`: 3 Checks scheiterten).

Im Original läuft die Fähigkeit in Stufen ab: Der Ranger geht zum Gebäude, packt aus (`UnpackTime`), hisst die Flagge (`PreparationTime`, das Gebäude blinkt in
seiner Farbe), das Gebäude wechselt den Besitzer, wenn die Vorbereitung endet, dann packt der Ranger ein (`PackTime`). Retail-Werte des Rangers
(`AmericaInfantry.ini`, `ModuleTag_19`): `StartAbilityRange = 5`, `UnpackTime = 3000`, `PreparationTime = 20000`, `PackTime = 2000` (ms), Black Lotus
(`ChinaInfantry.ini`, `ModuleTag_09`): `StartAbilityRange = 150`, `UnpackTime = 6730`, `PreparationTime = 6000`, `PackTime = 2800`.

Gestuft sind jetzt genau die beiden Übernahmekräfte `SPECIAL_INFANTRY_CAPTURE_BUILDING` (Ranger, Red Guard, Rebell, ihre Generals-Varianten) und
`SPECIAL_BLACKLOTUS_CAPTURE_BUILDING`. Jede andere Kraft, die ein `SpecialAbilityUpdate` trägt (Flashbang, Sprengladungen, Scharfschuss, Geldraub, Hackerangriffe ...),
verhält sich unverändert: nur der Aufladetimer, kein Handler (`UseSpecialPowerCommand` mit `UpdateModuleStartsAttack` wird dort weiterhin mit `SpecialPowerHandlerMissing` abgelehnt).

## 2. Quellen (gelesen, neu geschrieben)

Alle Verweise: `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/SpecialAbilityUpdate.cpp` und `.../Include/GameLogic/Module/SpecialAbilityUpdate.h`.

| Reimplementierung | Original |
| --- | --- |
| `SpecialAbilityUpdate.Step` | `SpecialAbilityUpdate::update` (Reihenfolge der Abbrüche, Phasenwahl) |
| `Begin` / `Initiate` | `initiateIntentToDoSpecialPower` (Ziel merken, `aiIdle`, Packzustand `PACKED`, bei `UnpackTime = 0` `UNPACKED`, andere Fähigkeiten desselben Objekts beenden) |
| `HandlePacking`, `NeedToPack/Unpack`, `StartUnpacking/StartPacking` | `handlePackingProcessing`, `needToPack`, `needToUnpack`, `startUnpacking`, `startPacking` |
| `IsWithinStartRange`, `WithinSpheres` | `isWithinStartAbilityRange` (Abstand der Begrenzungskugeln, `FROM_BOUNDINGSPHERE_2D`; `PartitionManager.cpp` `distCalcProc_BoundaryAndBoundary`) |
| `ApproachTarget` | `approachTarget` (`aiMoveToObject`) |
| `TurnToward` | `needToFace`, `startFacing`, `isFacing` (`aiFaceObject`) |
| `StartPreparation` | `startPreparation` (Zweige `SPECIAL_INFANTRY_CAPTURE_BUILDING` und `SPECIAL_BLACKLOTUS_CAPTURE_BUILDING`) |
| `ContinuePreparation` | `continuePreparation` (Abbruchreichweite, Ziel weg, Ziel verbündet, Aufladen des Infanteriefalls jeden Tick) |
| `TriggerEffect` | `triggerAbilityEffect` (Erfahrung, Garnisonsfall, `defect`, Aufladen des Black Lotus) |
| `EndPreparation`, `FinishAbility`, `Exit` | `endPreparation`, `finishAbility`, `onExit` |
| `StagedCaptureRules.CanCapture` | `ActionManager::canCaptureBuilding` (reduziert, wie bisher) |

## 3. Ablauf in der Simulation

`SpecialAbilityUpdate` ist jetzt ein `IUpdateModule` und ein `ISpecialPowerHandler`. Es schläft (`SleepForever`), bis `Begin` es weckt, und läuft dann jeden Tick:

| Stufe (`AbilityStage`) | Dauer | Inhalt |
| --- | --- | --- |
| `Approaching` | bis in Reichweite | Ziel liegt außerhalb von `StartAbilityRange`: `OrderMove` zum Gebäude (nicht vom Spieler, `ai.LastCommandFromPlayer = false`). Sobald die Kugeln nah genug sind, hält die Einheit an und dreht sich zum Ziel (Drehrate des Locomotors; ohne Locomotor sofort). |
| `Unpacking` | `UnpackTime` | `_packState = Unpacking`; danach `Unpacked`. |
| `Preparing` | `PreparationTime` | Die Aufladung der Kraft beginnt (`Trigger`), `USING_ABILITY` wird gesetzt, die Einheit steht. Beim Infanteriefall startet jeder Tick die Aufladung neu (die 15 s zählen erst nach der Übernahme), beim Black Lotus erst im Moment der Übernahme. |
| Übernahme | am Ende von `Preparing` | Erfahrung (`AwardXPForTriggering`); ein garnisonierbares Ziel (`GarrisonContain`) wird nur geräumt und nicht übernommen (Original: „we do not want to set a neutral building to our team if we are not in it“), sonst `IWorldServices.CaptureStructure` = das bestehende `ExecuteCapture` (Besitzerwechsel, Energie-Pool, `StructureCaptured`). |
| `Packing` | `PackTime` | danach `FinishAbility`: Zustand und Status werden gelöscht, das Modul schläft wieder. |

Dauern in Ticks (30 pro Sekunde, aufgerundet): Ranger/Red Guard/Rebell 90 / 600 / 60, Black Lotus 202 / 180 / 84. `PackUnpackVariationFactor` (im Katalog neu als `Real` getypt;
kein Retail-Modul setzt ihn) würde Entpack- und Packzeit mit `world.LogicRandom` streuen, gezogen wird nur bei einem Faktor über 0.

Reichweite: `StartAbilityRange` gilt zwischen den Begrenzungskreisen beider Objekte. Mit `ApproachRequiresLOS` (Standard) sucht das Original das Ziel anschließend in
`StartAbilityRange - 2,5` (ein Viertel Wegfindungszelle, `UNDERSIZE`); wirksam ist die kleinere Reichweite (Ranger 2,5, Black Lotus 147,5). Die Sichtlinie selbst fehlt.
Gemessen: Ein Ranger bleibt vor einer `ChinaBarracks` bei 66,3 Einheiten Mittelpunktabstand stehen (Begrenzungskreis der Kaserne etwa 56,8, des Rangers 7). Endet eine
Anreise, ohne die Reichweite zu erreichen (das Raster lässt die Einheit nicht näher), gilt ein Rest von zwei Zellen als angekommen; mehr als sechs ergebnislose Anreisebefehle
beenden die Fähigkeit (das Original ordert die Anreise ewig neu).

### Auslöser

- **`CaptureCommand`** (bestehend, Replay-Format und `CommandTypes.Version` unverändert): Hat der Akteur ein gestuftes `SpecialAbilityUpdate`, entfällt die Pflicht, schon in Reichweite zu stehen.
  Verlangt wird, dass die Kraft bereit ist (`UnpauseSpecialPowerUpgrade` angewendet, nachgeladen); sonst `SpecialPowerNotReady`. Der Befehl ruft `SpecialAbilityUpdate.Begin`. Ein
  Akteur ohne so ein Modul behält den alten Sofortpfad (Fixtures und Tests hängen daran).
- **`UseSpecialPowerCommand`** (Befehlsknopf, Skripte `*_USE_COMMANDBUTTON_ABILITY*`): findet jetzt den Handler (`ISpecialPowerHandler`), `Initiate` ruft `Begin`. Neu im Prüfpfad:
  `ISpecialPowerHandler.ValidatesTarget` (Standard `true`); die Übernahme verlangt ein Objektziel, das `StagedCaptureRules.CanCapture` besteht, sonst `InvalidSpecialPowerTarget`.
  Folge: Kampagnenskripte, die einen Ranger „einnehmen“ lassen, wurden bisher als `SpecialPowerHandlerMissing` abgelehnt und laufen jetzt wirklich.
- **Freigabe der Kraft:** Der Ranger startet pausiert (`StartsPaused`), `Upgrade_InfantryCaptureBuilding` löst die Pause. Die Pause verlängert die Aufladung um die Pausenzeit: Ein Ranger
  kann erst 15 s (`ReloadTime`) nach der Freigabe einnehmen. Das Szenario `capture-building` setzt deshalb `Upgrades` und `PowersReady` (siehe §6).

### Abbruchregeln (`onExit`)

Die Fähigkeit endet sofort, ohne Einpacken, wenn
- das Objekt tot ist oder zerstört wird,
- der Spieler (oder ein Skript über einen Spielerbefehl) der Einheit etwas anderes befiehlt (`AIUpdateInterface.LastCommandFromPlayer`), oder sie sich nach Erreichen der Reichweite bewegt,
- das Ziel noch existiert, aber tot ist, einem Verbündeten gehört (Besitzer gleich: „captured by a colleague“) oder getarnt und unentdeckt ist, solange die Vorbereitung läuft,
- das `SpecialPowerModule` der Kraft fehlt.

Ist das Ziel aus der Welt verschwunden (zerstört), bricht `continuePreparation` die Vorbereitung ab: Die Einheit packt (`PackTime`) und beendet dann (Original: `startPacking(false)`).
Verlässt das Ziel während der Vorbereitung die `AbilityAbortRange` (Standard „unbegrenzt“), gilt derselbe Weg. Ein zweiter Übernahmebefehl, während einer läuft, ersetzt den Lauf.

## 4. Zustand und Determinismus

Der Zustand (`abilityActive`, Packzustand, Zähler, Ziel, Reichweite, Anreisebefehle, `nextWake`) wird **nur geschrieben, solange eine Fähigkeit läuft**; beim Laden fehlt er
und das Modul schläft. Dadurch bleibt der Zustand aller Läufe ohne Übernahme byte-identisch: **keine** Versionssprünge (`WorldSnapshot` 8, Replay 8, `CommandTypes.Version` 11,
`MissionSnapshot` 2). Die Quelltextregel „jedes `IUpdateModule` speichert `nextWake`“ ist erfüllt. Keine Gleitkommazahlen, kein Wandzeitbezug; der Zufallsstrom wird nur bei
`PackUnpackVariationFactor > 0` verwendet.

`baselines/compatibility.json` ist unverändert (751 Einträge): Der Katalog bekam das Feld `PackUnpackVariationFactor` (nicht in den Retail-Daten verwendet), alle übrigen gelesenen
Felder (`StartAbilityRange`, `AbilityAbortRange`, `UnpackTime`, `PreparationTime`, `PackTime`, `NeedToFaceTarget`, `DoCaptureFX`, `AwardXPForTriggering`, `ApproachRequiresLOS`) waren bereits typisiert.

## 5. Darstellung

`CnCNew.Presentation/Objects/CaptureConditions.cs` liest `SpecialAbilityUpdate.Stage` (schreibgeschützt) und setzt, aufgerufen mit einer Zeile aus `ConditionDeriver.Derive`:
`UNPACKING` während `Unpacking`, `RAISING_FLAG` während `Preparing` (Original: `startPreparation` ersetzt `UNPACKING` durch `RAISING_FLAG`), beim Black Lotus stattdessen `FIRING_A`
(die Hacker-Tippanimation, ebenfalls `startPreparation`), `PACKING` während `Packing`. `RAISING_FLAG` steht jetzt in `ConditionDeriver.ProducedNames`.

Nicht umgesetzt: das Blinken des Gebäudes in der Farbe des Einnehmers (`DoCaptureFX`, `flashAsSelected`), der Ton (Entpack-/Pack-/Vorbereitungsschleife, EVA „Gebäude wird gestohlen“),
der Datenstrom-Spezialobjekt `BinaryDataStream` des Black Lotus und die Radar-Infiltrationsmeldung. `Stage` und `StageTicksLeft` liefern die Grundlage dafür.

## 6. Szenario `capture-building`

`DemoScenario` hat zwei neue optionale Eigenschaften: `Upgrades` (Spielerupgrades des Spielers 1 von Anfang an, hier `Upgrade_InfantryCaptureBuilding`) und `PowersReady`
(die Spezialkräfte der gesetzten Einheiten sind aufgeladen, statt 15 s nach dem Erscheinen). `LiveScenario.Populate` wendet sie nach dem Platzieren an.

## 7. Messwerte (4. Oktober 2026)

`PresentationTool scenario-audit --name capture-building`: **alle Checks bestehen.** Ranger geht ab Tick 12, bleibt bei x = 83,7 stehen (Kaserne bei 150), `UNPACKING` ab Tick 139 (90 Ticks),
`RAISING_FLAG` ab Tick 229 (600 Ticks), Übernahme Tick 828, `PACKING` ab Tick 829 (60 Ticks), Ende Tick 889; die Kaserne gehört am Ende Spieler 1.

Tests: `Tests.StagedCapture.cs` (Simulation, synthetisch: Stufenfolge und Dauern, Drehen zum Ziel, Black Lotus aus der Ferne, Abbruch bei zerstörtem Ziel / Spielerbefehl in der
Anreise und im Auspacken / totem Einnehmer / bereits übernommenem Ziel, Zielprüfung der Kraft, Ersetzen durch einen zweiten Befehl, Befehlsknopfweg, Snapshot in jeder Stufe, zwei Läufe);
`RealStagedCapture` (Retail: Ranger, Red Guard, Rebell auf `ChinaBarracks` mit 90/600/60 Ticks, Black Lotus auf `AmericaBarracks` mit 202/180/84); `CaptureConditionTests.cs`
(Darstellung). Unverändert: Retail-Referenzmatch (Sieger FactionGLA, Tick 13.890, Endhash `76F22CB0CA79BEEF`, Kette `35F54CAD40A43E6E`) und Stresstest (`56882F763EABEF67`, Kette `B8E663493F2C73D5`).

## 8. Bekannte Grenzen

- Gestuft sind nur die zwei Übernahmekräfte; die übrigen `SpecialAbilityUpdate`-Fähigkeiten (Sprengladungen, Scharfschuss, Hackerangriffe, Geldraub ...) bleiben Aufladehüllen.
- Fallen (`BOOBY_TRAPPED`, `checkAndDetonateBoobyTrap`), die Skill-/Rangpunkte (`SkillPointsForTriggering`), `FleeRangeAfterCompletion`, Sichtlinie der Annäherung und die Pflicht zur Kollision
  bei Reichweiten unter 2,5 fehlen; das Original-Radar-/EVA-Signal und das Blinken fehlen.
- Erkannt wird ein fremder Befehl nur über `AIUpdateInterface.LastCommandFromPlayer` (Bewegen, Andocken, Betreten, Bauen, Evakuieren). Ein reiner Feuerbefehl (`FireWeaponCommand`) fasst die KI nicht an und wurde nicht auf einen Abbruch geprüft.
- Die Begrenzungskugel ist hier der 2D-Begrenzungskreis (`BoundingCircleRadius`); das Original rechnet mit der 3D-Kugel inklusive Höhe und lässt die Einheit daher etwas früher anhalten.
- Das Anhalten in der gemessenen Entfernung (Ranger etwa 66 Einheiten vom Mittelpunkt einer Kaserne, ungefähr 20 vor der Wand) ist die Folge der Kugelregel. Ob es dem Original
  entspricht, kann nur eine Sichtprüfung durch einen Menschen klären.
