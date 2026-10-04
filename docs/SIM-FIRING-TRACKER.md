# Feuerstufen, Vorangriffsverzögerung und beschädigte Locomotoren in der Simulation

Stand 4. Oktober 2026 (Messwerte neu gemessen). Quellen der Referenz: `GeneralsMD/Code/GameEngine/Source/GameLogic/Object/FiringTracker.cpp` (`shotFired`, `update`, `speedUp`, `coolDown`),
`Object/Weapon.cpp` (`Weapon::preFireWeapon`, `getPreAttackDelay`, `getStatus`, `WeaponTemplate::getPreAttackDelay`), `Object/Object.cpp` (`preFireCurrentWeapon`, `adjustModelConditionForWeaponStatus`),
`AI/AIStates.cpp` (`AIAttackFireWeaponState::onEnter/update/onExit`), `Object/Locomotor.cpp` (`getMaxSpeedForCondition`, `getMaxTurnRate`, `getMaxAcceleration`, `LocomotorTemplate::validate`),
`Object/Update/SpecialAbilityUpdate.cpp` (`initiateIntentToDoSpecialPower`, `continuePreparation`). Nichts davon ist kopiert; die Abläufe sind verstanden und neu geschrieben.

## 1. Befund

Drei Verhaltensweisen der Waffen und der Bewegung waren nur als Bild oder gar nicht vorhanden:

* `CONTINUOUS_FIRE_SLOW/MEAN/FAST` leitete die Darstellung aus den Schussereignissen ab (`UnitConditionTracker`); die Simulation kannte die Stufen nicht. Der Gattling-Panzer drehte den Lauf schneller, schoss aber
  nie schneller (`WeaponBonus = CONTINUOUS_FIRE_MEAN RATE_OF_FIRE 200%` der Waffe blieb ohne Wirkung).
* `PreAttackDelay` (29 Retailwaffen: 250 ms bis 3.000 ms, 20 je Schuss, 5 je Angriff, 4 je Magazin) wurde nicht gelesen: jeder Schuss fiel im Tick des Befehls. `PREATTACK_A/B/C` konnte deshalb kein Modellzustand sein.
* `SpeedDamaged`, `TurnRateDamaged`, `AccelerationDamaged`, `LiftDamaged` (507 Zeilen in `Locomotor.ini`) wurden nicht gelesen: schwer beschädigte Einheiten fuhren und flogen wie neue.

## 2. Feuerstufen (`SimulationWorld.FiringTracker.cs`)

Zustand je Objekt (`FiringState`, nur vorhanden, solange etwas gesetzt ist): Schüsse in Folge, Opfer, Abkühl-Tick, „langsam“. Die Stufen „mittel“ und „schnell“ sind die Waffenbonusbedingungen
`CONTINUOUS_FIRE_MEAN`/`CONTINUOUS_FIRE_FAST` des Objekts (`Entity.WeaponBonusConditions`, exklusiv, nicht kumulativ); die Feuerrate folgt damit ohne weiteren Code aus `WeaponInfo.DelayBetweenShotsWith`.

| Ablauf | Umsetzung | Referenz |
|---|---|---|
| Schuss auf dasselbe Opfer | Zähler + 1 | `shotFired` |
| Schuss auf ein anderes Opfer innerhalb der Auslaufzeit | Zähler + 1, neues Opfer („we will not spin down“) | `shotFired` (`now < m_frameToStartCooldown`) |
| sonst | Zähler = 1 | `shotFired` |
| Abkühl-Tick | nächster möglicher Schuss + `ContinuousFireCoast`; ohne Auslaufzeit 0 (kein Zeitablauf) | `getPossibleNextShotFrame() + coast` |
| Zähler > `ContinuousFireOne` | keine Stufe → mittel | `speedUp` |
| Zähler > `ContinuousFireTwo` | mittel → schnell | `speedUp` |
| Zähler unter der Schwelle der aktuellen Stufe | zurück auf nichts, „langsam“ gesetzt | `coolDown` |
| Tick nach dem Abkühl-Tick | Stufe fällt direkt auf nichts („straight to zero“), „langsam“ für eine Sekunde, dann Ende | `update`, `coolDown` |

Der Tracker wird **nach** dem Schuss benachrichtigt (`Object::fireCurrentWeapon`): der Schuss, der eine Stufe erreicht, ist noch mit der alten Verzögerung getaktet. Getestet (`FiringTrackerSpinsUpCoastsAndCoolsDown`):
Abstände 18, 18, 18, 18, 9, 9, 9, 4 … Ticks bei 600 ms, `ContinuousFireOne = 3`, `ContinuousFireTwo = 6`; Zielwechsel in der Auslaufzeit hält die Stufe; nach dem letzten Schuss 30–36 Ticks schnell, 31 Ticks
langsam, dann nichts; ein Snapshot mitten im Lauf setzt identisch fort.

Die Darstellung liest die Stufe aus der Simulation (`SimulationWorld.ContinuousFireOf`); die eigene Zählung des `UnitConditionTracker` ist entfernt, die Stufen überleben jetzt das Laden eines Spielstands.

Nicht umgesetzt (Teile desselben Trackers der Referenz): die Dauerfeuer-Tonschleife (`FireSoundLoopTime`, Ton), `AutoReloadWhenIdle` (`m_frameToForceReload`) und der Bonus `TARGET_FAERIE_FIRE`.

## 3. Vorangriffsverzögerung (`PreAttackDone`)

`WeaponInfo.PreAttackDelayTicks` und `PreAttackType` (`PER_SHOT` – der Konstruktorwert –, `PER_ATTACK`, `PER_CLIP`; ein unbekannter Wert ist ein Fehler beim Katalogbau). Beide Schlüssel bleiben in `CnCNew.Data`
`deferred-field` und werden wie `ContinuousFireOne` aus dem Quelltext des Felds gelesen (Basislinie unverändert).

Ein `FireWeaponCommand` (Spieler oder KI), dessen Waffe sonst feuern würde – gewählt, in Reichweite, bereit, Turm ausgerichtet –, beginnt stattdessen das Aufziehen: Slot, Opfer und Endtick stehen im `FiringState`,
der Befehl bleibt **angenommen** (kein `CommandRejected`), es fällt kein Schuss. Ein späterer Befehl desselben Angriffs feuert, sobald der Endtick erreicht ist. Die Verzögerung entfällt
(`Weapon::getPreAttackDelay`: „Look for a reason to return zero“)

* bei `PER_CLIP`, wenn das Magazin angebrochen ist,
* bei `PER_ATTACK`, wenn schon ein Schuss in Folge auf dieses Opfer gefallen ist (der Zähler aus Abschnitt 2; er wird deshalb auch für solche Waffen geführt),

und sie skaliert mit dem Waffenbonus `PRE_ATTACK`. Abbruch: ein neuer Befehl an die Einheit (`EndAttack`, wie `AIAttackFireWeaponState::onExit`: `setPreAttackFinishedFrame(0)`), ein anderes Opfer, oder – als
Näherung, weil die Simulation das Ende eines Angriffs ohne Befehl nicht sieht – eine Sekunde ohne Folgebefehl nach dem Endtick. Ein einmaliger Skript-Knopfbefehl (`FireOrigin.Script`) und modulgesteuerte Schüsse
(`TryFireWeaponSlot`, `TryFireActiveWeapon`, Flugzeuge) feuern wie vorher ohne Aufziehen.

`SimulationWorld.PreAttackSlotOf(entity)` nennt den aufziehenden Slot; `ConditionDeriver.Weapon` setzt daraus `PREATTACK_A/B/C` mit `USING_WEAPON_A/B/C`. Getestet (`PreAttackDelayWindsUpBeforeTheShot`,
`UnitConditionTests.PreAttack`): je Schuss 15 Ticks vor jedem Schuss (Schüsse bei 15 und 60), je Angriff nur vor dem ersten Schuss auf ein Opfer und erneut nach einem Zielwechsel, je Magazin vor dem ersten von drei
Schüssen; ein Bewegungsbefehl verwirft das Aufziehen; Snapshot mitten im Aufziehen; Bildfolge `PREATTACK_A > FIRING_A > BETWEEN_FIRING_SHOTS_A`.

## 4. Beschädigte Locomotoren

`LocomotorInfo.Damaged` trägt die Konstanten mit `SpeedDamaged`, `TurnRateDamaged`, `AccelerationDamaged`, `LiftDamaged` (ein fehlender Schlüssel behält den Normalwert, wie `LocomotorTemplate::validate`; fehlen alle, gibt
es keine beschädigte Form). `AIUpdateInterface.ActiveLocomotor` liefert sie, sobald der Körper mindestens so beschädigt ist wie `GameData.ini MovementPenaltyDamageState` (Retail und Konstruktorwert: `REALLYDAMAGED`);
Jets wählen erst ihren Satz (Rollen, Angriff, Rückflug) und dann dessen beschädigte Form. Getestet (`DamagedLocomotorSlowsAReallyDamagedObject`): bei 50 % Leben unverändert, bei 20 % ein Drittel der Strecke.

Nicht erfasst: Objekte, die ihren Locomotor nicht über `AIUpdateInterface` lesen (Projektile, das Spectre-Gunship, `WorldEffectModules`).

## 5. Fähigkeit beendet Bewegung und Angriff, Blinken der Übernahme

* `SpecialAbilityUpdate::initiateIntentToDoSpecialPower` ruft `aiIdle` („Clear any old AI before starting this special ability“): jede Einheitenfähigkeit (ein `SpecialAbilityUpdate` zur benutzten Kraft) hält die
  Einheit an und beendet ihren Angriff; vorher tat das nur die gestufte Gebäudeübernahme (`docs/SIM-TURRET-AIM.md` §6).
* `SimEventKind.CaptureFlash` (Gebäude, einnehmende Einheit): `continuePreparation` mit `DoCaptureFX` erhöht eine Phase je Tick um ein Drittel des abgelaufenen Anteils der Vorbereitung; jedes Mal, wenn ihr ganzzahliger
  Teil von ungerade auf gerade wechselt, blinkt das Gebäude. Bei 60 Ticks Vorbereitung vier Blinker mit Abständen 11, 8, 8 Ticks (`StagedCaptureFlashesTheBuilding`). Farbe und Ton sind Sache der Darstellung
  (`docs/SIM-STAGED-CAPTURE.md`).

## 6. Snapshot, Hash, Versionen

`FiringState` wird nur geschrieben, solange er vom Ruhezustand abweicht (`firing`), die Vorangriffsfelder nur während des Aufziehens, die Blinkphase nur während einer Übernahme; `WorldSnapshot.Version` bleibt **9**,
ältere Snapshots laden unverändert. Befehlstabelle (13) und Replay-Format (10) sind unverändert. Läufe mit Dauerfeuer- oder Vorangriffswaffen haben andere Hashes als vorher.

| Lauf | vorher (`2b854d3`) | jetzt |
|---|---|---|
| `SimTool reference-match` | Sieger FactionGLA, Tick 15.000, Endhash `01C01E3B91EAE18C`, Kette `045DA5259A104F01` | siehe README / `docs/SIMULATION-CORE.md` §19 (nach jeder Simulationsänderung neu gemessen) |
| `SimTool stress` (216.000 Ticks, synthetisch) | Endhash `56882F763EABEF67` | **unverändert** |

## 7. Bekannte Grenzen

* Das Aufziehen kennt den Angriffszustand der Referenz nicht: sein Ende ohne Befehl wird über eine Sekunde ohne Folgebefehl angenähert; `LeechRangeWeapon` (Reichweite bleibt während des Aufziehens gültig) fehlt.
* `PreAttackDelay`/`PreAttackType`/`ContinuousFire*` sind in `CnCNew.Data` weiter generisch getippt (`deferred-field`); der Katalog prüft sie beim Bau streng.
* Sicht- und Tonwirkung (schnellerer Lauf, Aufzieh-Animation, Blinken) braucht die Sichtprüfung durch einen Menschen.
