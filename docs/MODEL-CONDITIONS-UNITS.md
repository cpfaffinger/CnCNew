# Modellzustände von Einheiten, Waffen, Bewegung und Tod

Stand: 4. Oktober 2026 (Phase 15, Welt-Darstellung). Gegenstand: die Modellzustandsbits (`MODELCONDITION_*`), die Einheiten
benutzen und die die Präsentation bisher nie setzte (`PresentationTool model-audit <dir> --structures`, Abschnitt
„model condition names used by states“). Ergänzt `docs/PRESENTATION-OBJECTS.md` (Bits, die aus dem Simulationszustand folgen) um die
Namen der Einheiten; die Namen der Gebäude, Türen und Docks stehen in der Schwesterarbeit.

Das Original setzt diese Bits **imperativ** aus vielen Modulen (`Object::setModelConditionState`, `Drawable::clearAndSetModelConditionFlags`).
Hier ist jedes Bit entweder eine reine Funktion des Simulationszustands (`ConditionDeriver`, Datei `ConditionDeriver.Units.cs`) oder wird von einem
kleinen, rein darstellenden Zustandsautomaten gesetzt (`UnitConditionTracker`), der einmal je
Simulationstick mit den Ereignissen (`FrameProjector.Observe`) und dem erfassten Objektzustand (`FrameProjector.Advance`) gefüttert wird. Beides ändert die
Simulation nie; der Tracker hält **keinen** Simulationszustand (kein Snapshot, kein Hash). Wo das Bit einen Verlauf braucht, den die Simulation nicht
führt, ist der Automat ein dokumentierter Nachbau der Referenzfunktion, nicht eine Erfindung. Wo die Simulation das auslösende Verhalten gar nicht kennt,
bleibt das Bit ungesetzt und steht unten mit der genauen Lücke (nie ein stiller Ersatz).

Referenzquellen (nur gelesen und neu geschrieben, Pfade unter `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/`):
`Object/FiringTracker.cpp`, `Object/Weapon.cpp`, `Object/WeaponSet.cpp`, `Object/Object.cpp`, `Object/Update/AIUpdate/JetAIUpdate.cpp`,
`Object/Update/AnimationSteeringUpdate.cpp`, `Object/Update/PhysicsUpdate.cpp`, `Object/Update/ToppleUpdate.cpp`, `Object/Update/TensileFormationUpdate.cpp`,
`Object/Update/SpecialAbilityUpdate.cpp`, `Object/Update/AIUpdate/MissileAIUpdate.cpp`, `Object/Update/AIUpdate/RailroadGuideAIUpdate.cpp`, `Object/Locomotor.cpp`,
`Object/Contain/ParachuteContain.cpp`, `Object/Contain/RiderChangeContain.cpp`, `Object/Die/CrushDie.cpp`, `Object/Behavior/SlowDeathBehavior.cpp`,
`Object/Behavior/BattleBusSlowDeathBehavior.cpp`, `Object/Body/ActiveBody.cpp`, `AI/AIStates.cpp`, `AI/TurretAI.cpp`, `AI/AIGroup.cpp`.

## 1. Ergebnis

`model-audit --structures` (Retail-Basisdaten): Namen, die Zustände benutzen: 106; von der Präsentation oder einem `ModelConditionUpgrade` gesetzt:
**52 → 71**; nie gesetzt: **54 → 35**. 19 Namen sind neu gesetzt, 9 weitere sind begründet nicht ableitbar (Tabelle 3).

Neu gesetzt (Tabelle 2): `CONTINUOUS_FIRE_SLOW/MEAN/FAST`, `JETAFTERBURNER`, `JETEXHAUST`, `FREEFALL`, `SPLATTED`, `FRONTCRUSHED`, `BACKCRUSHED`, `TOPPLED`,
`CLIMBING`, `OVER_WATER`, `CENTER_TO_LEFT`, `CENTER_TO_RIGHT`, `LEFT_TO_CENTER`, `RIGHT_TO_CENTER`, `TURRET_ROTATE`, `SECOND_LIFE`, `SPECIAL_CHEERING`.
Außerdem wird `PARACHUTING` jetzt aus dem Fallschirm abgeleitet (vorher nur über das Objektstatusbit `PARACHUTING`, das die Simulation nie setzt).

## 2. Umgesetzt

| Bit | Referenz (Datei, Funktion) | Umsetzung hier | Ableitung |
|---|---|---|---|
| `CONTINUOUS_FIRE_SLOW/MEAN/FAST` | `FiringTracker.cpp` `shotFired`, `update`, `speedUp`, `coolDown`; Schwellen `ContinuousFireOne/Two/Coast` in `Weapon.cpp` (Vorgabe `INT_MAX`/0) | `UnitConditionTracker.ShotFired`/`Update`; `WeaponInfo.ContinuousFireOneShots/TwoShots/CoastTicks` (nur gelesen, `WeaponCatalog.Build`) | Automat je Schütze: aufeinanderfolgende Schüsse auf dasselbe Ziel (`WeaponFired`-Ereignis, Ziel in `Other`; ein Zielwechsel innerhalb der Coast-Zeit zählt weiter), `> One` → MEAN, `> Two` → FAST (exklusiv), nach `nächster möglicher Schuss + Coast` fällt die Stufe auf SLOW und eine Sekunde später auf nichts. Den „nächsten möglichen Schuss“ (`getPossibleNextShotFrame`) liefert `Entity.ReadyTickOf(Slot)` des Slots, der in diesem Tick geschossen hat |
| `JETAFTERBURNER` | `JetAIUpdate.cpp` `friend_enableAfterburners`; an in `JetPauseBeforeTakeoffState::update`, aus in `JetTakeoffOrLandingState::onExit` | `JetAIUpdate.AfterburnersOn` (nur lesend) → `ConditionDeriver.UnitStates` | Phase `PauseBeforeTakeoff` nach dem Neustart des Countdowns (kein Flugzeug mehr voraus) und Phase `TakingOff`; nur Jets (die `HeliAIStateMachine` kennt den Pausenzustand nicht) |
| `JETEXHAUST` | `JetAIUpdate.cpp` `JetAIUpdate::update` (Geschwindigkeit > 0 und `ALLOW_AIR_LOCO`) | `ConditionDeriver.UnitStates` | `JetAIUpdate.AllowAirLoco && Entity.Speed != 0` |
| `PARACHUTING` (Schirm) / `FREEFALL`, `PARACHUTING` (Fallschirmjäger) | `ParachuteContain.cpp` `onContaining`, `update` (`clearAndSetModelConditionState(FREEFALL, PARACHUTING)` beim Öffnen) | `ConditionDeriver.UnitStates` mit `ParachuteContain.Opened` | geöffneter Schirm: PARACHUTING; Insasse eines Schirms: FREEFALL bis zum Öffnen, dann PARACHUTING |
| `FREEFALL` (Lawinenfelsen) | `TensileFormationUpdate.cpp` `update` (`m_life < 100` und Höhenänderung > 0,2) | `TensileFormationUpdate.Life` (nur lesend), `UnitConditionTracker.Update` | `Enabled`, `0 < Life < 100` und `|ΔZ| > 0,2` gegenüber dem vorigen Tick |
| `SPLATTED` | `PhysicsUpdate.cpp` `PhysicsBehavior::update` (Fallschaden tötet, Todesart `SPLATTED`) | `UnitConditionTracker.Died` | `EntityDied`-Ereignis mit Todesart `SPLATTED`; bleibt gesetzt (das Original löscht es nie) |
| `FRONTCRUSHED`, `BACKCRUSHED` | `CrushDie.cpp` `crushLocationCheck`, `CrushDie::onDie` | `UnitConditionTracker.Died`/`CrushLocation` | `EntityDied` mit Todesart `CRUSHED` bei einem Objekt mit `CrushDie`: von Mittelpunkt, vorderem und hinterem Quetschpunkt (halber Hauptradius entlang der Blickrichtung) gewinnt der dem Quetscher (`Other`) nächste; ohne Quetscher `TOTAL_CRUSH` |
| `TOPPLED` | `ToppleUpdate.cpp` `applyTopplingForce`; `RiderChangeContain.cpp` `onRemoving` (`ScuttleStatus`, in allen Retaildaten `TOPPLED`) | `ToppleUpdate.HasToppled`, `RiderChangeContain.IsScuttled` → `ConditionDeriver.UnitStates` | umgestoßenes Objekt (nicht bei `KillWhenStartToppling`, dort stirbt es vor der Zeile); Kampfmotorrad ohne Fahrer |
| `CLIMBING` | `AIStates.cpp` `AIFollowPathState::onEnter`/`update` (bewegtes Objekt auf `CELL_CLIFF`) | `ConditionDeriver.UnitStates` | `Speed != 0 || HasMoveTarget` und `TerrainMap.CellAt(...)` hat `CellFlags.Cliff` |
| `OVER_WATER` | `Locomotor.cpp` `moveTowardsPositionHover`; `RailroadGuideAIUpdate.cpp` (Zug im Tunnel: `z < Boden - 3`) | `LocomotorInfo.IsHover` (`Appearance = HOVER`, nur gelesen), `ConditionDeriver.UnitStates` | Hover-Fortbewegung und `TerrainMap.IsUnderwater(x, y)`; Zug (`RailroadBehavior`) mit `Z < Boden - 3` |
| `CENTER_TO_LEFT`, `LEFT_TO_CENTER`, `CENTER_TO_RIGHT`, `RIGHT_TO_CENTER` | `AnimationSteeringUpdate.cpp` `update`; Drehrichtung `PhysicsBehavior::getTurning` aus `Locomotor.cpp` `rotateObjAroundLocoPivot` (`TURN_POSITIVE`/`NEGATIVE`) | `UnitConditionTracker.StepSteering`/`TurnOf`; `AnimationSteeringUpdate.MinTransitionTicks` (`MinTransitionTime`, jetzt typisiert gelesen) | Zustandsautomat des Originals mit Mindesthaltezeit; „dreht“ heißt: die Blickrichtung hat sich in diesem Tick um genau die Drehrate des Locomotors geändert (so dreht `AIUpdateInterface` bei weiter entferntem Ziel); positiv = links |
| `TURRET_ROTATE` | `TurretAI.cpp` `friend_turnTowardsAngle` | `TurretTracker.IsRotating` (liest `Entity.TurretOf`) | der Turm liegt weiter als einen Schritt vom Zielwinkel (sonst rastet er ein) |
| `SECOND_LIFE` | `BattleBusSlowDeathBehavior.cpp` `update` (nach der Landung des geworfenen Busses, nie gelöscht) | `BattleBusSlowDeathBehavior.HasLanded` (nur lesend) → `ConditionDeriver.UnitStates` | erster Tod hat begonnen und geendet und der Bus liegt am Boden (ein echter Tod in der Luft beendet den Update, der Bus bleibt oben: kein Bit) |
| `SPECIAL_CHEERING` | `AIGroup.cpp` `groupCheer`, `Object.cpp` `setSpecialModelConditionState` (drei Sekunden) | `FrameProjector.Cheer(ids)` → `UnitConditionTracker.Cheer` | 90 Ticks ab dem Aufruf; ein neuer Aufruf startet die Zeit neu. Das Original löst es nur über die Testtaste `ALL_CHEER` aus; der Client ruft `Cheer` für die Auswahl noch nicht auf (`MetaAction.AllCheer` existiert) |

Alle Namen stehen in `ConditionDeriver.ProducedNames` (eigene Zeilen am Ende der Menge); `model-audit` zählt danach nur noch echte Lücken.

## 3. Nicht ableitbar (bewusst nicht gesetzt)

| Bit | Wer setzt es im Original | Was die Simulation nicht kennt |
|---|---|---|
| `EXPLODED_FLAILING`, `EXPLODED_BOUNCING` | `SlowDeathBehavior::beginSlowDeath` (`FlingForce`: Impuls, `MODELCONDITION_EXPLODED_FLAILING`) und `update` (FLAILING → BOUNCING beim Bodenkontakt, `isAboveTerrain`) | `FlingForce`/`FlingPitch` sind getippt, aber unbenutzt (Kommentar von `SlowDeathBehavior`: ein Physikimpuls beim Tod): das Objekt wird nie in die Luft geschleudert, es gibt keinen Flug und keinen Aufprall. Voraussetzung: Wurfphysik des `PhysicsBehavior` mit Höhe |
| `JAMMED` | `ActiveBody::onSubdualChange` → `MissileAIUpdate::projectileNowJammed` (Rakete durch ECM mit Subdual-Schaden ≥ Maximalleben gestört) | Subdual-Schaden wird nicht modelliert (`ActiveBody` meldet `NoEffect`); `MissileAIUpdate` kennt keine Störung |
| `PANICKING` | `AIPanicState`/`AIMoveAwayFromRepulsorsState` (`AIStates.cpp`) | `TEAM_PANIC` startet in der Skript-VM einen gewöhnlichen Wegpunktfolger (nicht unterscheidbar von `FOLLOW_WAYPOINTS`); die Flucht vor Abstoßern gibt es nicht. Voraussetzung: ein eigener Panikzustand im Skriptfolger (`MissionSnapshot`) bzw. in der KI |
| `RAPPELLING` | `AIRappelState` (Chinook-Kampfabwurf `AI_RAPPEL_INTO`), außerdem rückwärts auf Klippen (`AIFollowPathState`) | Kampfabwurf und Abseilen sind nicht simuliert (`ChinookAIUpdate`: „the drop is not simulated“); es gibt keine Rückwärtsfahrt |

`RAISING_FLAG` stand hier bis zur gestuften Gebäudeübernahme; seitdem leitet `CaptureConditions` es ab (`docs/SIM-STAGED-CAPTURE.md` §5).
`PREATTACK_A/B/C` stand hier bis zur Vorangriffsverzögerung der Simulation (`PreAttackDelay`, `docs/SIM-FIRING-TRACKER.md` §3); `ConditionDeriver.Weapon` setzt es jetzt aus `SimulationWorld.PreAttackSlotOf`.

## 4. Prüfung

* Testprogramm `tests/CnCNew.Presentation.Tests/UnitConditionTests.cs`, synthetische Fixture (Gattling-Waffe mit `ContinuousFireOne/Two/Coast`, Fahrzeug mit
  `AnimationSteeringUpdate` auf Terrain, Hover über einem See, Klippenzelle, Flugplatz mit Jet, Schirm mit Fallschirmjäger, Baum mit `ToppleUpdate`, Bus mit
  `UndeadBody`/`BattleBusSlowDeathBehavior`, Auto mit `CrushDie`, Lawinenfels an einem Hang): jedes umgesetzte Bit hat einen Test über den `FrameProjector`
  (Ereignisse wie im Live-Spiel) oder, wo das Ereignis eigens gebaut werden muss (Tod, Quetschen), über den Tracker. Reale Daten (`--content-dir`): Gattling-Panzer
  (`none > MEAN > FAST > SLOW > none`, der Zustand `ATTACKING CONTINUOUS_FIRE_*` wird gewählt), Kampfmotorrad (Abseits des Fahrers `TOPPLED`, alle vier Lehnbits auf der Karte
  Alpine Assault), zerquetschtes Auto (`CarCamaro` wählt das Modell `FRONTCRUSHED BACKCRUSHED`), Raptor (`JETEXHAUST`), Schlachtbus (`SECOND_LIFE` nach der Landung).
* Referenzmatch (`SimTool reference-match`) unverändert: Sieger FactionGLA bei Tick 13.890, Endhash `76F22CB0CA79BEEF`, Kette `35F54CAD40A43E6E`; Stresstest unverändert
  `56882F763EABEF67`, Kette `B8E663493F2C73D5`. Die Simulation bekommt nur lesende Eigenschaften und getippte, ungenutzte Waffen-/Locomotor-/Modul-Felder (kein
  serialisierter Zustand, kein neues Verhalten).

## 5. Grenzen und Befunde

* **Die Tracker sind Darstellung, nicht Simulation.** Sie kennen nur, was seit dem Start der Projektion geschah: nach dem Laden eines Spielstands beginnen
  Lehnzustand, `SPLATTED`/`CRUSHED`-Merker und Jubel leer (die abgeleiteten Bits kommen aus dem Simulationszustand und überleben das Laden). Objekte außerhalb der Sicht
  werden nicht erfasst; ein Feuerstufen-Abfall wird beim nächsten Erfassen nachgeholt.
* **`CONTINUOUS_FIRE_*` kommt aus der Simulation** (`docs/SIM-FIRING-TRACKER.md` §2): die Stufen sind die Waffenbonusbedingungen `CONTINUOUS_FIRE_MEAN/FAST` des Objekts und beschleunigen die Waffe
  (Gattling: 200 % und 400 %); der `UnitConditionTracker` zählt nicht mehr selbst, die Stufen überleben das Laden eines Spielstands.
* **`ATTACKING` wird von der Simulation nie gesetzt** (`IS_ATTACKING` hat keinen Schreiber), obwohl `ConditionDeriver` das Bit ableitet. Viele Zustände heißen
  `ATTACKING <X>` (z. B. `ATTACKING CONTINUOUS_FIRE_MEAN`); die Zustandswahl nimmt dann den besten Teiltreffer, was für die Gattling-Zustände reicht, aber nicht alle Kombinationen trifft.
* **`SPLATTED` und `FRONTCRUSHED`/`BACKCRUSHED` sind in der heutigen Simulation kaum erreichbar** (der Code ist getestet und bereit): `ParachuteContain.OnDie` tötet den Insassen nach dem
  Fallschaden mit `DEATH_NORMAL` (eine Todesart `SPLATTED` ergibt sich nur, wenn schon der Fallschaden tötet), und `SquishCollide` quetscht nur Infanterie, während `CrushDie` nur
  Fahrzeuge tragen (kein Retailobjekt hat beides); das Quetschen von Fahrzeugen durch `PhysicsBehavior::onCollide` fehlt.
* **Fallschirmjäger werden nicht gezeichnet.** `FrameProjector` überspringt enthaltene Objekte; der Insasse eines `ParachuteContain` ist enthalten, also fehlen seine Zustände
  `FREEFALL`/`PARACHUTING` im Bild, obwohl sie abgeleitet werden. Das Original zeichnet ihn an einer Knochenposition des Schirms (`ParachuteContain::updateOffsetsFromBones`).
  Außerdem verbirgt das Original den Schirm bis zum Öffnen (`draw->setDrawableHidden(!m_opened)`), hier zeigt er den Standardzustand.
* `OVER_WATER` kennt nur die Lage, nicht die Bewegungsgeschichte: ein Hover-Objekt, das nie bewegt wurde und im Wasser startet, hat das Bit hier schon, im Original erst nach der
  ersten Bewegung. Hubschrauber und Fallschirme tragen ebenfalls `Appearance = HOVER` und bekommen das Bit über Wasser, wie im Original (ohne Zustand, der es nutzt).
* `CLIMBING` kennt den Zusatz „nicht eingeklemmt“ (`getPinched`) nicht; `RAPPELLING` (rückwärts auf Klippen) entfällt mangels Rückwärtsfahrt.
* `TURRET_ROTATE` folgt dem Turmzustand der Simulation (`docs/SIM-TURRET-AIM.md`): der Turm dreht, sobald ein Feuerbefehl ihn auf das Ziel richtet, hält, stellt sich zurück; keine Leerlaufsuche.
* Die Lehnerkennung nimmt die Drehung aus der Blickrichtungsänderung je Tick. Andere Blickrichtungsänderungen (Taxi, Ausweichen) mit genau der Drehrate würden
  als Lenkung gelten; sie betreffen nur Objekte mit `AnimationSteeringUpdate` (Kampfmotorräder).
