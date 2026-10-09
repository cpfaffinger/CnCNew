# Türme in der Simulation: Zielen vor dem Schuss, Angriffszustand und Leerlaufabtasten (`TurretAI`)

**Hinweis (9. Oktober 2026, Phase 23): historisch, 30 Hz.** Tickzahlen, Siegticks und Hashes in diesem Dokument sind bei 30 Hz und an ihrem damaligen Stand gemessen und als Referenzwerte überholt (maßgeblich: `baselines/reference-manifest.json`, seit `ea52f87` bei 60 Hz). Tickangaben gelten als Zahl von Originalbildern weiter. Oberhalb von 30 Hz entscheidet ein Turm seinen Schritt einmal je Originalbild **des Turms** mit der Drehrate des 30-Hz-Katalogs und geht ihn in Teilen je Tick; der Winkel ist an jedem Bildende bitgleich, die Feuerfreigabe fällt ins selbe Bild, `TurretWaits` zählt Bilder, und Peilung und Neigung werden am Bildende des Opfers gelesen: `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md` (`32eb25c`, `6e0c691`, `276a8de`, `dc888d3`), `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md` (`c87748a`), `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` (`43acce8`, `36a2404`). Offen: der Turm einer `AmericaFireBase` beginnt in zwei Gefechten auf ausgelieferten Karten bei 60 Hz einen Tick statt ein Bild nach einem ergebnislosen Blick zu drehen (nicht bis zum Symbol verfolgt).

Stand 4. Oktober 2026 (Messwerte neu gemessen). Quellen der Referenz: `GeneralsMD/Code/GameEngine/Source/GameLogic/AI/TurretAI.cpp` und
`Include/GameLogic/TurretAI.h` (Zustandsmaschine, `friend_turnTowardsAngle`/`friend_turnTowardsPitch`, `TurretAIData::buildFieldParse`, `TurretAIIdleState`,
`TurretAIIdleScanState`, `TurretAIHoldTurretState`, `TurretAIRecenterTurretState`, `TurretAI::setTurretTargetObject`),
`AI/AIStates.cpp` (`AIAttackAimAtTargetState::onEnter/update`, `AIAttackFireWeaponState::update`, `AIAttackApproachTargetState::onEnter`, `AIAttackState::onEnter/update/onExit`),
`Object/Update/AIUpdate.cpp` (`getWhichTurretForCurWeapon`, `getTurretRotAndPitch`, `setTurretTargetObject`). Nichts davon ist kopiert; der Ablauf ist verstanden und für die
befehlsgetriebene Kampfsimulation dieses Projekts neu geschrieben.

## 1. Befund

Die Simulation kannte keinen Turmzustand. Ein `FireWeaponCommand` löste den Schuss sofort aus, sobald Waffe, Reichweite und Sicht stimmten; die Darstellung
(`TurretTracker`) drehte den gezeichneten Turm erst *nach* dem Schuss und riet dabei die Richtung. Das Szenario `tank-duel` (`PresentationTool scenario-audit`)
zeigte es: der Crusader schoss in Tick 5 mit einem Turm, der 45° neben dem Ziel stand.

In der Referenz dreht die KI den Turm zuerst (`TurretAIAimTurretState`), und nur wenn er ausgerichtet ist, feuert der Zustand `TURRETAI_FIRE`
(`AIAttackFireWeaponState`) – für Waffen auf einem Turm feuert der Turm-Automat, nicht der Körper (`AIAttackAimAtTargetState::update` gibt für Türme mit Drehrate
immer `STATE_CONTINUE` zurück: „The Body can never return Success if the weapon is on the turret“).

Die erste Fassung dieses Modells kannte zwei Lücken, die jetzt geschlossen sind (Abschnitt 3.3 und 3.5): Ein Angriff war ein Strom einzelner Feuerbefehle, der Turm hielt sein Ziel nur
„Bereitschaft der Waffe + Rückstellzeit“ nach dem letzten Befehl und drehte danach zurück, auch wenn die Einheit weiter angriff (Nachladen, Anmarsch). Und ein Turm ohne Ziel blieb in Ruhe,
obwohl die Daten von rund 40 Türmen ein zufälliges Umsehen verlangen.

## 2. Daten: `Turret`/`AltTurret` streng typisiert (`CnCNew.Data`)

`Schema/TurretSchema.cs` enthält **jeden** Schlüssel von `TurretAIData::buildFieldParse`; der Compiler (`DefinitionCompiler.CompileModule`) übersetzt die
verschachtelten Blöcke `Turret`/`AltTurret` implementierter Module damit statt mit der heuristischen Generik:

| Schlüssel | Typ | Einheit/Bedeutung |
|---|---|---|
| `TurretTurnRate`, `TurretPitchRate` | Real | Grad je Sekunde (`parseAngularVelocityReal`) |
| `NaturalTurretAngle`, `NaturalTurretPitch`, `FirePitch`, `MinPhysicalPitch`, `GroundUnitPitch`, `MinIdleScanAngle`, `MaxIdleScanAngle` | Angle | Grad |
| `TurretFireAngleSweep`, `TurretSweepSpeedModifier` | Waffenslot + Angle/Real, je Slot wiederholbar | Spreizwinkel / Drehfaktor |
| `ControlledWeaponSlots` | Flags (`PRIMARY`/`SECONDARY`/`TERTIARY`), **Pflicht** | `TurretAI::TurretAI`: „MUST specify controlled weapon slots“ |
| `RecenterTime`, `MinIdleScanInterval`, `MaxIdleScanInterval` | DurationMs | Millisekunden, auf Ticks aufgerundet |
| `AllowsPitch`, `InitiallyDisabled`, `FiresWhileTurning` | Bool | |

Ein unbekannter Schlüssel oder ein fehlendes `ControlledWeaponSlots` ist ein Compilerfehler, kein stiller Standardwert. `InterTurretDelay` gibt es in der Referenz
nur unter `INTER_TURRET_DELAY`, das der ausgelieferte Build nicht definiert; kein Objekt setzt es.

`baselines/compatibility.json` ändert sich in genau einem Eintrag: `compatibility-ignored-tokens` 132 → **208**. Die 76 neuen Fälle sind die Anmerkungen
`// turn rate, in degrees per sec` hinter Werten von `TurretTurnRate`/`TurretPitchRate`; der Referenzparser liest das erste Token und ignoriert den Rest, das wird jetzt
gezählt statt unsichtbar zu sein. Die Zahl der Basislinieneinträge bleibt 751. Waffenfelder wurden nicht neu typisiert: `AcceptableAimDelta` (Weapon/AcceptableAimDelta,
44 Fälle, weiter `deferred-field`) wirkt in der Referenz nur auf das **Körper**-Zielen von Waffen ohne Turm (`AIAttackAimAtTargetState`), nicht auf Türme; der Turm
benutzt die feste Schwelle `REL_THRESH` (siehe 3.4).

Die Abtastfelder (`MinIdleScanAngle`, `MaxIdleScanAngle`, `MinIdleScanInterval`, `MaxIdleScanInterval`) waren schon typisiert und im Katalog nur nicht gelesen; die Daten ändern sich dafür nicht,
`IniTool compat-check` meldet unverändert 751 Einträge.

## 3. Simulation

### 3.1 Vorlage und Zustand

* `Templates/TurretInfo.cs`: `TurretInfo` (eine Turmvorlage in Radiant je Tick und Ticks) und `TurretSet` (Haupt- und Zweitturm, `TurretsLinked` des AI-Moduls);
  `ObjectTemplate.Turrets` ist `null` für Objekte ohne Block. Fehlende Schlüssel haben die Konstruktorwerte von `TurretAIData` (0,01 rad je Frame, zwei Sekunden Rückstellzeit,
  Abtastintervall 9.999.999 Frames, kein Abtastwinkel).
* `TurretInfo.Gates`: nur ein Turm mit Drehrate > 0 und nicht `InitiallyDisabled` nimmt am Zielen teil (siehe 5). `TurretInfo.CanScan`: `Gates`, ein Abtastwinkel größer 0 und ein echtes
  Intervall (kleiner als der Standardwert); sonst sieht `TurretAIIdleScanState::onEnter` (`minA == 0 && maxA == 0` → sofort `STATE_SUCCESS`) nie etwas zu tun.
* `World/TurretState.cs`: je Turm Winkel (relativ zur Fahrzeugrichtung), Neigung, Ziel, `HoldUntil`, Waffenslot des letzten Befehls, `Rotating`, Sweep-Zustand sowie neu der Angriffszustand
  (`Persistent`, `Pursuing`, `LoseInterestAt`) und die Abtastung (`Scanning`, `ScanAngle`, `NextScan`, `ScanDraws`). `Entity.Turrets`, öffentlich lesbar als `Entity.TurretOf(index)` →
  `TurretReading(Angle, Pitch, Rotating)`. `SimulationWorld.AttackTargetOf(entity)` nennt das Opfer des Angriffs einer Einheit (Ziel ihres ersten Turms mit Angriffszustand).

### 3.2 Phasen (`World/SimulationWorld.Turrets.cs`)

Die Referenz hat einen Turm-Zustandsautomaten (`IDLE`, `IDLESCAN`, `AIM`, `FIRE`, `HOLD`, `RECENTER`), der vom Angriffszustand der KI gesteuert wird. Hier folgen die Phasen aus wenigen Feldern;
`StepTurrets` dreht einmal je Tick (nach allen Bewegungen des Ticks, in Entity-Id-Reihenfolge) jeden Turm, der nicht in Ruhe ist oder abtasten kann:

| Phase | Bedingung | Verhalten | Referenz |
|---|---|---|---|
| Zielen | Ziel gültig und (Angriff oder Zeit ≤ `HoldUntil`) | Drehung um höchstens `TurretTurnRate` je Tick zum Opfer; liegt der Rest unter einem Schritt, rastet der Turm ein; `TURRET_ROTATE` solange er nicht angekommen ist; Neigung analog | `TurretAIAimTurretState`, `friend_turnTowardsAngle/Pitch` |
| Halten | kein Ziel, Zeit ≤ `HoldUntil` | der Winkel bleibt | `TurretAIHoldTurretState` (`m_timestamp = frame + RecenterTime`) |
| Rückstellen | kein Ziel, `HoldUntil` verstrichen, Winkel ≠ Ruhewinkel | Drehung zum `NaturalTurretAngle`/`NaturalTurretPitch` mit halber Rate; danach Ruhe | `TurretAIRecenterTurretState` |
| Ruhe | Ruhewinkel, `HoldUntil` = 0 | nichts (abtastende Türme: warten auf `NextScan`) | `TurretAIIdleState` |
| Abtasten | `Scanning` | Drehung zu Ruhewinkel + `ScanAngle` mit halber Rate; angekommen → Halten | `TurretAIIdleScanState` |

Ein Turm eines Objekts im Bau (`UNDER_CONSTRUCTION`) stellt weder zurück noch tastet er ab (`TurretAIRecenterTurretState::update`, `TurretAIIdleScanState::update` warten dort).

### 3.3 Angriffszustand: das Ziel bleibt, bis der Angriff endet

**Befund der Referenz.** Ein Angriffsbefehl setzt die Einheit in `AIAttackState`. Dessen Teilautomat (`AttackStateMachine`: Anmarsch `AIAttackApproachTargetState`, Zielen `AIAttackAimAtTargetState`,
Feuern `AIAttackFireWeaponState`) ruft in `onEnter` des Anmarschs, in jedem `update` des Zielzustands und beim Neuwählen des Opfers `AIUpdateInterface::setTurretTargetObject(turret, victim)` auf, solange er
läuft, also auch beim Anmarsch und beim Nachladen. Der Turm bleibt im Zustand `AIM`/`FIRE`, bis `AIAttackState::onExit` die Ziele aller Türme auf `NULL` setzt (→ `TURRETAI_HOLD`, `RecenterTime`,
`RECENTER`, `IDLE`). `onExit` läuft, wenn das Opfer tot ist (`AIAttackState::update`: `isEffectivelyDead` → `STATE_SUCCESS`), wenn die Einheit einen neuen AI-Befehl bekommt (Bewegung,
Idle/Stopp, Andocken, Betreten, Bauen …: der neue Zustand ersetzt den alten) oder wenn der Angriff scheitert (kein Waffenfenster, Opfer nicht mehr feindlich, „keine Verfolgung erlaubt“ im Anmarsch).
Der Turm selbst bricht das Zielen ab (`TurretAIAimTurretState::update` → `STATE_FAILURE` → `HOLD`), wenn `getAbleToAttackSpecificObject(ATTACK_CONTINUED_TARGET, …)` scheitert (Opfer im Nebel,
getarnt, verschwunden); Reichweite spielt für das *Hauptziel* keine Rolle (`isPrimaryEnemy`).

**Umsetzung.** Der Angriff ist die Absicht, das Opfer zu bekämpfen; sie lebt in den Türmen (`TurretState.Persistent`, `Target`), weil das ihr einziger Verbraucher ist. Ein Turm ohne Angriffszustand
verhält sich wie vorher (einmaliges Zielen, 3.4). Eintritt:

* ein **angenommener** `FireWeaponCommand` mit Ursprung Spieler oder KI (`FireOrigin.Player`/`Ai`; auch `MissionSession` und `SkirmishAi`): die Türme des gewählten Slots (bei `TurretsLinked` alle) bekommen das Opfer.
  Ein abgewiesener Befehl (außer Reichweite, Waffe lädt nach, Opfer unsichtbar) beginnt nichts.
* ein neuer **`PursueCommand`** (Befehlstabelle 12 → 13, Replay-Format 9 → 10): Bewegung zu einem Punkt (X, Y) *zum Angriff auf* ein Opfer – `AIAttackApproachTargetState::onEnter` („If we have a turret,
  start aiming“). Die Türme der Waffe, die die Einheit gegen das Opfer einsetzen würde (`AttackSlotIgnoringRange`: wie `ChooseWeapon` ohne Reichweiten- und Bereitschaftsfilter), folgen dem Opfer schon,
  wenn es noch weit außerhalb der Reichweite ist. Der Befehl gilt auch als „bleib stehen und greif an“ (Ziel = eigene Position). Er schießt nichts; geschossen wird weiter per `FireWeaponCommand`.
  Neu ist der Befehl, weil sich eine Verfolgung sonst nicht von einem echten Bewegungsbefehl unterscheiden ließe (beide sind `SetMoveTargetCommand`); der alte Befehl wurde dafür nicht verändert.
* nicht: `FireOrigin.Script` (ein einmaliger Knopfbefehl wird nicht wiederholt, verhält sich wie früher), modulgesteuerte Schüsse (`TryFireWeaponSlot` u. a.) – beide behalten das alte Verhalten
  „Bereitschaft der Waffe + max(`RecenterTime`, 15 Ticks) nach dem letzten Befehl“ (`HoldUntil`).

Dauer: kein Ablauf, solange der Angriff besteht. Er endet (`StopAiming` → Ziel gelöscht, `HoldUntil` = jetzt + `RecenterTime`, dann Rückstellen) bei

| Ende | Umsetzung | Referenz |
|---|---|---|
| Opfer tot oder verschwunden | in `StepTurret` im selben Tick | `AIAttackState::update` (`isEffectivelyDead`), `friend_getTurretTarget` |
| neuer AI-Befehl an die Einheit | `IWorldServices.EndAttack(entity)`, aufgerufen von `AIUpdateInterface.OrderMove`/`OrderIdle`/`OrderDock`/`StartTask` (Bewegung, Idle, Andocken, Betreten, Bauen/Reparieren; also auch Skript- und Modulbefehle) | der neue Zustand ersetzt `AI_ATTACK` |
| Opfer für den Besitzer nicht mehr angreifbar (`CanPlayerSee`: Nebel, getarnt und nicht entdeckt, im Container) | je Tick geprüft | `TurretAIAimTurretState::update` (`ATTACK_CONTINUED_TARGET`) |
| Opfer außerhalb der Reichweite *jeder* Waffe länger als 45 Ticks, **sofern die Einheit nicht verfolgt** (`Pursuing`) | `KeepsAttackIntent`, `LoseInterestAt` | `AIAttackApproachTargetState::onEnter` gibt für Einheiten ohne Verfolgungserlaubnis (`isAllowedToChase`) `STATE_FAILURE` zurück |
| ein neuer Feuerbefehl auf ein anderes Opfer | ersetzt das Ziel | `AIAttackFireWeaponState`/`notifyNewVictimChosen` |
| der Angreifer stirbt | der Turm wird nicht mehr gesteppt | – |

`Pursuing` merkt sich, dass der Angriff mit einem `PursueCommand` begann; ein späterer `FireWeaponCommand` auf dasselbe Opfer behält es. Eine Einheit, die verfolgt, gibt wegen der Entfernung nie auf; die
Darstellung (`StandingOrders`) beendet ihren Befehl, wenn das Opfer verschwindet oder unsichtbar wird. Einheiten, deren Angriff nur aus Feuerbefehlen besteht (KI-Einheiten, Angriffsmarsch), geben ein Opfer auf,
das aus jeder Reichweite ist – gehend oder stehend. Die Zahl 45 Ticks ist eine Annäherung (kurze Lücken zwischen zwei Verfolgungsbefehlen dürfen den Turm nicht zurückdrehen).

**Darstellung.** `StandingOrders` (`Presentation/Input`) nutzt `PursueCommand` statt `SetMoveTargetCommand` für den Anmarsch und für das Anhalten in Reichweite und sendet ihn einmal, wenn die Einheit in
Reichweite steht, aber nachlädt, ohne dass ein Turm schon ein Opfer hat. Flugzeuge (haben keine Türme) bekommen weiter `SetMoveTargetCommand`. `SimTool reference-match` benutzt für das Annähern ebenfalls `PursueCommand`.

### 3.4 Feuerfreigabe

`FireWeaponCommand` (Ausführung in `SimulationWorld.Step` → `ExecuteCommands`): nach Waffenwahl, Sicht und `IFireGate` ruft `AimTurretsAt(source, slot, victim, Angriff)` die Türme auf, die den gewählten
Slot steuern (bei `TurretsLinked` alle): jeder bekommt das Ziel (`setTurretTargetObject`, aufgerufen von `AIAttackAimAtTargetState`), und die Waffe feuert nur, wenn **alle** ausgerichtet sind:

* Winkel: Abweichung zur *aktuellen* Zielrichtung ≤ `REL_THRESH` = 0,035 rad (≈ 2°, `TurretAIAimTurretState::update`); bei einer Waffe mit `TurretFireAngleSweep` < Sweepwinkel
  (`turnAlignedToNemesis = fabs(angleDiff) < sweep`).
* Neigung (nur `AllowsPitch` und Rate > 0): Differenz zur Sollneigung ≤ 2°. Sollneigung: `FirePitch`, sonst die Erhebung zum Ziel (von der Oberkante des Schützen, wie `v.z -= maxHeightAbovePosition/2`),
  nie unter `MinPhysicalPitch`; mit `GroundUnitPitch` ein Bogen, der mit der Entfernung zu unbeweglichen oder fahrenden Bodenzielen wächst.
* Sweep: nach einem Schuss ist der Sweep 3 Ticks aktiv (`ENABLE_SWEEP_FRAME_COUNT`); der Turm zielt dann abwechselnd ±Sweepwinkel neben das Ziel mit `TurretSweepSpeedModifier` als Drehfaktor.

Ist ein Turm nicht ausgerichtet, bleibt der Befehl **angenommen** (kein `CommandRejected`-Ereignis, also kein „NoCanDoSound“ und keine HUD-Meldung bei jedem Tick des Drehens), es fällt kein Schuss, und
`SimulationWorld.TurretWaits` zählt es. Nach dem Schuss verlängert `NoteTurretShot` bei einem einmaligen Zielen den Halte-Zeitpunkt (ein Angriff braucht das nicht) und schaltet den Sweep ein.

Gleiches gilt für das modulinterne `TryFireActiveWeapon` (ungenutzt). `TryFireWeaponSlot` (Module, die ihre Angriffsfolge selbst fahren und bei einem Fehlschlag aufgeben, z. B.
`AssistedTargetingUpdate`) richtet die Türme aus (einmaliges Zielen), wartet aber nicht.

### 3.5 Leerlaufabtasten (`TurretAIIdleState`, `TurretAIIdleScanState`)

Der Ablauf der Referenz für einen Turm ohne Ziel: `IDLE` wartet ein zufälliges Intervall `MinIdleScanInterval`..`MaxIdleScanInterval` (`resetIdleScan`: `GameLogicRandomValue`), dann `IDLESCAN`:
zufälliger Winkel `MinIdleScanAngle + Zufall(0, Max − Min)` mit zufälligem Vorzeichen, Drehung zu `NaturalTurretAngle` + Winkel mit halber Rate; angekommen → `HOLD` (`RecenterTime`) → `RECENTER` →
`IDLE` mit neuem Intervall. Ein Angriff unterbricht jeden Schritt; danach geht es über `HOLD`/`RECENTER` zurück in `IDLE`.

Umsetzung wie oben (`ScheduleScan`, `IdleScanStep`, `ScanStep`):

* Abtastende Türme (`TurretInfo.CanScan`) sind immer im Schrittverzeichnis. Der Zeitpunkt des ersten Abtastens wird beim ersten Schritt des Turms gezogen (`NextScan = Tick + Intervall`) und nach jedem
  Zurückstellen neu.
* **Zufall:** die Referenz zieht aus dem gemeinsamen Logikstrom (`GameLogicRandomValue`), sodass jeder ruhende Turm alle anderen Verbraucher verschiebt. Hier ist jede Ziehung ein Hash aus
  (Weltsamen, Entity-Id, Turmindex, Ziehungszähler `ScanDraws`) (SplitMix64-Mischung, ganzzahlig): gleiche Eingaben liefern dieselben Werte, Replays und Snapshots stimmen überein, und
  `world.LogicRandom` wird **nicht** berührt (Test: eine Welt mit und eine ohne abtastenden Turm haben nach 1500 Ticks denselben Zustand des Logikstroms). Die Werte unterscheiden sich von denen der
  Referenz, die Verteilung (gleichverteilt im Intervall, gleichverteilt im Winkelbereich, Vorzeichen 50 : 50) nicht. Reihenfolge der Ziehungen wie in der Referenz: Intervall, Winkel, Vorzeichen.
* Die Daten: 38 `Turret`-Blöcke setzen ein Abtastintervall (50 Objekte einschließlich der Reskins der Generäle haben einen abtastenden Turm): die Sentry-Drohne (alle 250 ms, bis 360°),
  Patriot-Batterie und Gattling-Kanone (250 ms, 360°), die Feuerbasis (550–950 ms, 360°), Strategiezentrum (Hauptturm; 500–1000 ms, 60°; sein `AltTurret` ist `InitiallyDisabled`), die Technicals (5–10 s, 30–60°), das ECM-Fahrzeug (Intervall 0, genau 180°,
  `RecenterTime` 0: es dreht sich ununterbrochen hin und zurück, wie in der Referenz).
* Selbstzielen: `TurretAI::friend_checkForIdleMoodTarget` (ein ruhender Turm wählt von sich aus ein Opfer, `AutoAcquireEnemiesWhenIdle`) ist seit Phase 17 Teil des Zielerwerbs der Simulation
  (`docs/SIMULATION-CORE.md` §33): ein ruhender Turm bekommt sein Opfer von der Einheit, auch wenn deren Körper fährt; ein Opfer unterbricht das Abtasten wie jeder Angriff.

### 3.6 Snapshot, Hash, Determinismus

* `Entity.Describe` schreibt `turrets` nur, solange ein Turm vom Ruhezustand abweicht (oder abtasten kann); ebenso `turretWaits` im Weltteil erst nach dem ersten Warten. Neu und nur bei gesetztem
  Zustand: `persistent`, `pursuing`, `loseInterestAt`, `scanning` + `scanAngle`, `nextScan`, `scanDraws`. Welten ohne Türme (der 216.000-Tick-Stresstest) behalten Serialisierung und Hash.
* `WorldSnapshot.Version` bleibt **9**: die neuen Felder sind optional und werden nur gelesen, wenn vorhanden; ein Snapshot der ersten Fassung (ohne sie) lädt unverändert (sein Turm verhält sich
  als einmaliges Zielen). Läufe mit Türmen haben dennoch andere Hashes (Abschnitt 8).
* `CommandTypes.Version` 12 → **13** (`PursueCommand` = 31), `ReplayFormat.CurrentVersion` 9 → **10** (der neue Befehl hat eine Nutzlast: Einheit, Opfer, X, Y). Alte Replays scheitern an der
  Befehlstabelle (`ReplayRunner` prüft sie) und wären wegen der geänderten Kampfzeiten ohnehin an den Prüfsummen gescheitert.
* Alle Größen sind `Fixed`/ganzzahlig; Winkel über `FixedTrig` (CORDIC-`Atan2`, Fehler < 1e-3 rad, weit unter der 2°-Schwelle). Kein `float`, keine Wandzeit, kein `Random`.

## 4. Darstellung

`TurretTracker` (jetzt `Presentation/Objects/TurretTracker.cs`) liest `Entity.TurretOf` und merkt sich nur, ob ein Turm sich drehte (`MODELCONDITION_TURRET_ROTATE` über `IsRotating`). `TurretMotion` (die
Rohtext-Auswertung der Turmblöcke), `TurretTracker.Observe` und `ObjectVisual.Turrets` sind entfernt; die Stride-Schicht (`ObjectLayerCore.ApplyTurret`) ist unverändert, `DrawableInstance.TurretYaw/TurretPitch/
AltTurret*` kommen aus dem Simulationszustand. Damit folgt auch das Abtasten ohne eigenen Code: gezeichneter Turm und `TURRET_ROTATE` kommen aus demselben Zustand (das Audit `tank-duel` prüft es an
der Sentry-Drohne; das gezeichnete Bild kann dem Simulationsstand um einen Tick nachlaufen).

## 5. Ausnahmen: was feuert wie vorher

| Fall | Verhalten | Begründung |
|---|---|---|
| Objekt ohne `Turret`-Block (Infanterie, Gebäude ohne Turm, Flugzeuge) | feuert sofort | kein `m_turretAI` |
| Turm mit `TurretTurnRate = 0` (18 Blöcke der Retaildaten; der „Scheinturm“ der Referenz, z. B. des Comanche) | feuert sofort, Turm bleibt mittig | `AIAttackAimAtTargetState::update`: „incapable of turning, turn ourself“ → Körper zielt |
| `InitiallyDisabled = Yes` (4 Blöcke, die Strategiezentren; zusammen mit den Scheintürmen 22 Objekte mit nicht zielendem Turm) | wie vorher; tastet auch nicht ab | die Referenz schaltet sie über `BattlePlanUpdate`/`DeployStyleAIUpdate` ein (`setTurretEnabled`); `BattlePlanModule` schaltet hier keinen Turm |
| Slot, den kein Turm steuert (`ControlledWeaponSlots`) | feuert sofort | `getWhichTurretForCurWeapon` = `TURRET_INVALID` |
| `FireOrigin.Script` (Einzelbefehl einer Skript-Schaltfläche) | feuert sofort, der Turm dreht trotzdem, **kein** Angriffszustand | der Befehl wird nicht wiederholt |
| Modulgesteuerte Schüsse (`TryFireWeaponSlot`, `FireCurrentWeaponAtPosition`, Flugzeuge, Projektile) | wie vorher (einmaliges Zielen) | eigener Ablauf |
| Ziel als Position (Bodenangriff) | kein Zielen | `FireWeaponCommand` kennt nur Objektziele |

`FiresWhileTurning` hat in `TurretAI.cpp` keine Wirkung auf die Feuerfreigabe (es steuert nur das Ende des Drehgeräuschs, `m_continuousFireExpirationFrame`); es ist typisiert und gelesen, aber ohne Folge.

## 6. Bekannte Grenzen (ehrlich)

* **Der Angriff lebt nur in den Türmen.** Eine Einheit ohne Turm hat keinen Angriffszustand; wer ihn braucht, müsste ihn auf die Einheit heben. Die Verfolgung selbst (Fahren zum Opfer, Anhalten in
  Reichweite, Neuberechnen) macht weiter die Darstellung (`StandingOrders`) bzw. die Befehlsgeber; die Simulation kennt nur „Verfolgung läuft“ (`PursueCommand`).
* **Annäherungen am Angriffsende:** Aufgeben nach 45 Ticks außerhalb jeder Reichweite statt der Zustandsregeln der Referenz (`isAllowedToChase`, Stealth, Garnison, Zeit in `AIAttackApproachTargetState`);
  die Bewegungsbefehle, die die Referenz als „neuer AI-Befehl“ kennt, sind die der `AIUpdateInterface`-Methoden (`OrderMove`, `OrderIdle`, `OrderDock`, Bauen). `OrderEvacuate` ohne Bewegung beendet
  den Angriff nicht – wie in der Referenz: `AIUpdateInterface::privateEvacuate` ruft nur `orderAllPassengersToExit` und wechselt den AI-Zustand nicht (mit Ziel, `aiMoveToAndEvacuate`, läuft es über
  `OrderMove` und beendet ihn). Eine Einheitenfähigkeit beendet Angriff und Bewegung: `SpecialAbilityUpdate::initiateIntentToDoSpecialPower` ruft `aiIdle` („Clear any old AI before starting this special
  ability“), umgesetzt beim `UseSpecialPowerCommand` für jedes Objekt mit einem `SpecialAbilityUpdate` der Kraft (die gestufte Übernahme tat es schon selbst; Test `TurretAttackEndsWhenTheUnitStartsAnAbility`).
  Spezialkräfte ohne `SpecialAbilityUpdate` (Gebäudekräfte, OCL-Kräfte) berühren den AI-Zustand auch in der Referenz nicht.
* **Selbstzielen (seit Phase 17 umgesetzt, vorher bewusst zurückgestellt):** `friend_checkForIdleMoodTarget` und `AutoAcquireEnemiesWhenIdle` sind nachgebildet – nicht im Turm, sondern als Angriffszustand der
  Einheit (`Entity.Acquisition`, `World/SimulationWorld.Acquisition.cs`), der den Türmen ihr Opfer gibt (`AimTurretsForAttack`) und es ihnen am Ende des Angriffs wieder nimmt (`StopTurrets`). Die drei Befehlsgeber
  (`MissionSession.DriveCombat`, `SkirmishAi.Fire`, `StandingOrders.FireAtNearestEnemy`) wählen keine Opfer mehr; ein Opfer, das ein Befehlsgeber ausdrücklich nennt (`FireWeaponCommand`, `PursueCommand`), sperrt die
  eigene Zielwahl der Einheit, solange solche Befehle kommen. Regeln, Quellen, Näherungen und Messwerte: `docs/SIMULATION-CORE.md` §33. Weiter nicht nachgebildet: die Verfolgung aus dem Leerlauf (eine ruhende Einheit
  eines Computerspielers läuft ihrem Opfer nicht nach; nur im Angriffsmarsch) und die Antwort einer passiven Einheit auf ihren letzten Angreifer.
* **Eigener Zufallsstrom für das Abtasten:** gleiche Verteilung wie die Referenz, andere Werte (3.5). Die Abtastung wartet nicht auf Aktivierung durch Energie, EMP oder Deaktivierung
  (`m_enabled` der Referenz entspricht nur `Gates`).
* **Körper zielt nicht:** Waffen ohne Turm feuern weiter ohne Drehung des Körpers (`AcceptableAimDelta` bleibt `deferred-field`); fahrende Einheiten drehen den Körper nur durch ihre Bewegung.
* `DeployStyleAIUpdate`: `ALIGNING_TURRETS` (Turm vor dem Einpacken mittig) wird nicht betreten; `TurretsFunctionOnlyWhenDeployed` ist weiter nur durch das Feuer-Gate (Waffen) umgesetzt.
* Bei `TurretsLinked` (3 der 4 Schlachtschiffe) feuert jede Waffe erst, wenn beide Türme zeigen; die Referenz lässt den jeweils ausgerichteten Turm feuern.
* Das Feuern geschieht im Tick nach dem Ausrichten (der Befehl prüft den Zustand am Ende des Vortags gegen die aktuelle Zielrichtung); die Referenz feuert einen Update später im Turm-Automaten.
* Die Sichtbarkeitsregel (Opfer unsichtbar beendet das Zielen) ist mit `STEALTHED` und mit Nebel getestet (`TurretAttackEndsWhenTheVictimFallsIntoTheFog`: das Opfer bleibt in Reichweite und ungetarnt, das einzige Objekt, das es sah, verschwindet).
* Sicht- und Darstellungsänderungen brauchen weiter eine Sichtprüfung durch einen Menschen (Rauchtests beweisen Reproduzierbarkeit, nicht Richtigkeit).

## 7. Tests und Audits

* `Simulation.Tests` (`Tests.Turrets.cs`): Vorlagenmodell, Standardwerte und strenge Fehler; Drehung mit 6°/Tick und Wartezeit bis Tick +15; Körperrichtung; Ausnahmen aus Abschnitt 5; Rückstellen, Ruhezustand
  (kein `turrets` im Snapshot), einmaliges Zielen (Skriptbefehl) und totes Ziel; zwei Türme mit Firing-Pitch, Sweep, `TurretsLinked`; Snapshot mitten in der Drehung und Determinismus; echte Daten: Crusader (180°/s),
  Battlemaster, keine Türme beim Ranger, vier Schlachtschiffe mit Zweitturm (3 verbunden), Sweepwaffen, Duell Crusader gegen Battlemaster.
* `Simulation.Tests` (`Tests.TurretAttack.cs`): Angriff überlebt Befehlslücken (300 Ticks ohne Befehl) und eine Verfolgung (Fahrt am Opfer vorbei, Turmfehler ≤ 2,5° ab Tick 10, kein Befehl wartet),
  ein laufendes Opfer wird verfolgt, eine Bewegung beendet den Angriff, Halten genau `RecenterTime`, dann Rückstellen; totes Opfer; Aufgeben außer Reichweite (stehend 45 Ticks, gehend 45 Ticks, Verfolgung nie); toter Angreifer;
  Skriptbefehl ist kein Angriff, neues Opfer ersetzt das alte; unsichtbares Opfer beendet das Zielen; Snapshot mitten in der Verfolgung und im Abtasten setzt identisch fort, `PursueCommand` im Replay;
  Abtasten: Winkelbereich, Haltezeit `RecenterTime`, Intervall, beide Vorzeichen, deterministisch, Logikstrom unberührt, anderer Weltsamen andere Werte, Unterbrechung durch einen Angriff und Wiederaufnahme;
  echte Daten: Crusader verfolgt einen Battlemaster (Zielfehler 0,00° ab Tick 60, 7 Schüsse, kein Befehl wartet), Sentry-Drohne, Patriot und Technical tasten innerhalb ihres Bereichs ab (zwei Läufe gleich), 50 abtastende Objekte.
* `Presentation.Tests`: Turm folgt Befehl/Schuss/Rückstellung aus der Simulation (`TurretFollowsOrders`, Rückstellen jetzt nach dem Tod des Opfers), `TURRET_ROTATE` durch den Projektor (`TurretRotate`),
  Standing orders (`PursueCommand` für den Anmarsch), echte Daten: alle Turmblöcke typisiert.
* `PresentationTool scenario-audit … --name tank-duel` (Standardlauf, Ausgabe unten): jeder Schuss mit 0,0° Winkelfehler; **in jedem Tick** zwischen Befehl und Tod des Opfers bleibt der Turm innerhalb 5° (Crusader ab Tick 13,
  Battlemaster ab Tick 6, 0 Ticks außerhalb), nach dem Tod des Battlemasters hält der Crusader-Turm 45° genau bis Tick 445 und dreht ab Tick 446 zurück (Tod 384 + `RecenterTime` 60 Ticks + 2), in Ruhe ab Tick 460;
  die Sentry-Drohne tastet 9-mal ab (Winkel −170°…62°, je 31 Ticks gehalten bei `RecenterTime` 30, genau 8 Ticks Ruhe dazwischen = `MinIdleScanInterval` 8), der gezeichnete Turm stimmt mit dem simulierten überein,
  `TURRET_ROTATE` steht in 518 von 537 drehenden Samples (die übrigen sind die einrastenden Schritte). `capture-building` und `jet-strafe` bleiben grün (`scripts/audit-scenarios.ps1`).

## 8. Messwerte (Release, Retaildaten, 4. Oktober 2026)

| Lauf | vor der ersten Turmfassung | Stand der ersten Turmfassung (vorher) | jetzt (Angriffszustand + Abtasten) |
|---|---|---|---|
| `SimTool reference-match` | Sieger FactionGLA, Tick 13.890, Endhash `76F22CB0CA79BEEF` | Sieger FactionGLA, Tick 13.875, Endhash `482FC8C52D507662`, Kette `34AB50A397B0125C` | Sieger FactionGLA, Tick **15.000**, Endhash `01C01E3B91EAE18C`, Kette `045DA5259A104F01`; Run 2 und Snapshot-Fortsetzung identisch |
| `SimTool stress` (216.000 Ticks, synthetisch, ohne Türme) | Endhash `56882F763EABEF67`, Kette `B8E663493F2C73D5` | unverändert | **unverändert** |
| `ai-match` China/GLA (Alpine Assault) | China, Tick 43.275, Endhash `757327AE69B87C2A` | China, Tick 43.965, Endhash `AE34F9421AA76C9A`, Kette `DF948386BA2C5C98` | **Patt** (kein Sieger in 45.000 Ticks; China hat noch Gebäude, GLA nur Waffenhändler und zwei Tunnelnetze), Endhash `BE5A7A4A37403928`, Kette `1AF7BD86DED30A1B` |
| `ai-match` AmericaAirForce/China | Patt, Endhash `AF78F914D08B09DD` | Patt, Endhash `D31EB9C8679D6632`, Kette `2536E3A7B8CB6620` | Patt, Endhash `21BC6E1596A8702E`, Kette `4B231593B1ACA841` |
| `ai-match` America/GLA | America, Tick 16.110, Endhash `ED637C7C3E4FFF9E` | unverändert (`2B93BACCDB0F6450`) | America, Tick 16.110, Endhash `03CED6D83F6BD0A1`, Kette `0F03F254211B30BA` (die Feuerbasen tasten ab) |
| `ai-match` ChinaTank/AmericaLaser (neu) | – | – | Patt, Endhash `8C6F376D82A0F548`, Kette `77C4A5F4F42006D7` |
| `CampaignTool audit --ticks 900 --campaign CHALLENGE_0` | – | 7/7 deterministisch, 7/7 identisch nach Speichern/Laden; Ausschalt-Enden Sieg 7/7, Niederlage 6/7 | **unverändert**: 7/7, 7/7, Sieg 7/7, Niederlage 6/7 |

Alle `ai-match`-Läufe: zwei unabhängige Läufe und die Fortsetzung ab einem Snapshot in der Mitte sind tickgenau identisch. Die Hashes ändern sich aus zwei Gründen: Türme folgen ihrem Opfer jetzt ununterbrochen
(auch zwischen den Schüssen und beim Anmarsch, dadurch andere Treffzeitpunkte), und alle abtastenden Türme (Patriot, Gattling-Kanone, Feuerbasis, Sentry-Drohne …) drehen sich im Leerlauf, was den Zustandshash jedes Ticks
ändert, auch wenn kein Kampf stattfindet; der Stresstest hat keine Türme und bleibt gleich. Beim Referenzspiel kommt hinzu, dass das Szenario seine Annäherung jetzt als `PursueCommand` sendet. Der Ausgang des
KI-Spiels China/GLA kippt von „Sieg in Tick 43.965“ zu „Patt“; die Ursache ist in Abschnitt 8a aufgeschlüsselt (die Regel „ein neuer Befehl beendet das Zielen“; das Spiel war schon vorher knapp vor dem Tick-Limit). Es wurde nichts
aus anderem Anlass verändert.

Die Werte der Spalte „jetzt“ sind seit den Feuerstufen, der Vorangriffsverzögerung, den beschädigten Locomotoren und der Platzierung des KI-Versorgungszentrums überholt; aktuelle Referenzwerte stehen in `docs/SIMULATION-CORE.md` §32.

## 8a. Aufschlüsselung der KI-Verschiebung (4. Oktober 2026, gemessen)

Offen war, warum `ai-match` China/GLA auf Alpine Assault seit dem Angriffszustand im Patt endet (vorher China bei Tick 43.965). Die Effekte wurden einzeln aus- und eingeschaltet: ein Wegwerf-Worktree
auf dem Stand `2b854d3` bekam je Variante einen Ein-Zeilen-Patch, das Werkzeug wurde je Variante in ein eigenes Verzeichnis gebaut, dann liefen alle 16 Paarungen aus `SIMULATION-CORE.md` §19
(`--max-ticks 45000`, Standardseeds). Jeder der 128 Läufe (acht Stände) ist mit Lauf 2 und der Snapshot-Fortsetzung identisch.

| Variante | Stand | Patch |
|---|---|---|
| G | `8b8c8a7` (erste Turmfassung) | – |
| I | `2b854d3` | Idle-Scan aus, Angriffszustand aus, `EndAttack` ohne Wirkung |
| D | `2b854d3` | Idle-Scan aus, Angriffszustand aus |
| B | `2b854d3` | Idle-Scan aus (`TurretInfo.CanScan` = falsch) |
| C | `2b854d3` | Angriffszustand aus (`FireWeaponCommand` zielt wie ein Einzelschuss, `TurretAimOrder.Shot`) |
| J | `2b854d3` | `EndAttack` ohne Wirkung (ein neuer Befehl beendet das Zielen nicht) |
| A | `2b854d3` | – (auch `d713531` gemessen: in allen 16 Läufen hashgleich) |

Ergebnis je Lauf (Sieger und Tick oder Patt, dahinter die ersten acht Stellen des Endhashes):

| Karte | Paarung | G | I | D | B | C | J | A |
|---|---|---|---|---|---|---|---|---|
| Alpine Assault | China, GLA | China 43.965 `AE34F942` | China 43.965 `AE34F942` | Patt `553A2186` | Patt `553A2186` | Patt `BE5A7A4A` | China 44.100 `A8EC7BF4` | Patt `BE5A7A4A` |
| Alpine Assault | America, China | Patt `63EEA99F` | Patt `63EEA99F` | Patt `63EEA99F` | Patt `2616F0B2` | Patt `131253B5` | Patt `B748B8DD` | Patt `B748B8DD` |
| Alpine Assault | GLA, America | America 12.120 `5BADFE2A` | America 12.120 `5BADFE2A` | America 12.120 `5BADFE2A` | America 12.120 `5BADFE2A` | America 12.120 `83FA72B7` | America 12.120 `83FA72B7` | America 12.120 `83FA72B7` |
| Alpine Assault | ChinaTank, AirForce | Patt `36BD9041` | Patt `36BD9041` | Patt `36BD9041` | Patt `F6C1297B` | Patt `61CB3CC2` | Patt `710439C5` | Patt `710439C5` |
| Armored Fury | China, GLA | Patt `1DA176CF` | Patt `1DA176CF` | Patt `1DA176CF` | Patt `F8B0885E` | Patt `7B9BC049` | Patt `6F0CA3F1` | Patt `6F0CA3F1` |
| Armored Fury | America, China | America 41.130 `12495B5C` | America 41.130 `12495B5C` | America 44.325 `871BA9DA` | America 35.175 `C8675A5A` | America 40.875 `D8620C00` | Patt `4035F8F0` | America 36.375 `D3A4AAD6` |
| Armored Fury | GLA, America | America 22.410 `B5CD779E` | America 22.410 `B5CD779E` | America 22.410 `B5CD779E` | America 22.410 `B5CD779E` | America 22.410 `29601DCA` | America 22.410 `29601DCA` | America 22.410 `29601DCA` |
| Armored Fury | ChinaTank, AirForce | AirForce 9.405 `2261402C` | AirForce 9.405 `2261402C` | AirForce 9.405 `2261402C` | AirForce 9.405 `2261402C` | AirForce 9.405 `DF5D46B1` | AirForce 9.405 `DF5D46B1` | AirForce 9.405 `DF5D46B1` |
| Bitter Winter | China, GLA | Patt `C9F239FC` | Patt `C9F239FC` | Patt `C9F239FC` | Patt `C9F239FC` | Patt `94F023A5` | Patt `2C65DC45` | Patt `2C65DC45` |
| Bitter Winter | America, China | Patt `D660891B` | Patt `D660891B` | Patt `D660891B` | Patt `A64316B6` | Patt `C9D7E892` | Patt `781E31A8` | Patt `781E31A8` |
| Bitter Winter | GLA, America | America 14.565 `752A8CA8` | America 14.565 `752A8CA8` | America 14.565 `752A8CA8` | America 14.565 `752A8CA8` | America 14.565 `3922E188` | America 14.565 `3922E188` | America 14.565 `3922E188` |
| Bitter Winter | ChinaTank, AirForce | Patt `4F07AF52` | Patt `4F07AF52` | Patt `4F07AF52` | Patt `E61AB63B` | Patt `92F1F0A9` | Patt `75A58BD4` | Patt `75A58BD4` |
| Death Valley | China, GLA | China 8.805 `DFB8BB3F` | China 8.805 `DFB8BB3F` | China 8.805 `DFB8BB3F` | China 8.805 `DFB8BB3F` | China 8.805 `4F687B3B` | China 8.805 `4F687B3B` | China 8.805 `4F687B3B` |
| Death Valley | America, China | Patt `B27C7202` | Patt `B27C7202` | Patt `B27C7202` | Patt `B27C7202` | Patt `82DEB8C7` | China 35.985 `4A4BF443` | China 35.985 `4A4BF443` |
| Death Valley | GLA, America | Patt `2C7EE0E2` | Patt `2C7EE0E2` | Patt `2C7EE0E2` | Patt `C0947735` | Patt `8252C18F` | Patt `41808E02` | Patt `41808E02` |
| Death Valley | ChinaTank, AirForce | Patt `2BF5DE1C` | Patt `2BF5DE1C` | Patt `2BF5DE1C` | Patt `2BF5DE1C` | Patt `A619B93E` | Patt `5532EEC9` | Patt `5532EEC9` |

Befunde:

* **Die Zerlegung ist vollständig.** Variante I ist in allen 16 Läufen hashgleich mit der ersten Turmfassung G. Zwischen den beiden Ständen gibt es also genau drei wirksame Änderungen für KI-Spiele:
  den Idle-Scan, den dauerhaften Angriffszustand und – bisher nicht als eigener Effekt benannt – **„ein neuer Befehl beendet das Zielen“** (`EndAttack` in `OrderMove`/`OrderIdle`/`OrderDock`/Bauauftrag;
  es wirkt auch auf das einmalige Zielen, deshalb unterscheidet sich D von G).
* **`PursueCommand` spielt in `ai-match` keine Rolle:** `SkirmishAi` sendet ihn nie (nur `StandingOrders` der Darstellung und das Referenzszenario). Im Referenzspiel dagegen ist er der Haupteffekt:
  ohne ihn (`SetMoveTargetCommand` für die Annäherung) endet es bei Tick 15.225 statt 15.000, ohne alle drei Effekte bei Tick 13.875 wie die erste Turmfassung; der Idle-Scan ändert dort nichts
  (Endhash und Kette gleich), der Angriffszustand der Feuerbefehle nur die Kette.
* **China/GLA auf Alpine Assault kippt allein durch „Befehl beendet das Zielen“:** mit der Regel Patt (A, B, C, D), ohne sie China bei Tick 43.965 (G, I) bzw. 44.100 (J). In diesem Spiel hat der Angriffszustand
  keinen Einfluss (A = C, B = D), der Idle-Scan ändert nur den Hash. Das Spiel war schon vorher 1.035 Ticks vom Limit entfernt; eine verlorene Schussgelegenheit (der Turm einer Einheit, die einen neuen
  Marschbefehl bekommt, hält seinen Winkel, statt dem letzten Ziel zu folgen) reicht für die Verschiebung.
* **Kein systematischer Effekt:** G, I, J und A entscheiden 7 von 16 Läufen, B, C und D 6; einzelne knappe Spiele kippen in beide Richtungen (Death Valley America/China: Patt in G, China bei 35.985 in A;
  Armored Fury America/China: America zwischen 35.175 und 44.325, Patt in J). 13 der 16 Paarungen haben in jeder Variante denselben Ausgang.
* **Die Patts haben eine andere Ursache.** In jedem Patt stehen beide Seiten am Ende mit 0 bis wenigen hundert Credits und wenigen Objekten da (die Kartenvorräte sind erschöpft, 2–4 Angriffswellen in 45.000 Ticks):
  ein Befund der Skirmish-KI (Wirtschaft, Angriffsschwelle), nicht der Türme. Er besteht seit Phase 15 (8 statt 10 entschiedene Läufe nach der Wegsuche um stehende Einheiten) und gehört zu Phase 17.

**Entscheidung:** keine Verhaltenskorrektur an Haltezeit, Sichtregel oder Idle-Scan. „Ein neuer Befehl beendet das Zielen“ ist das Verhalten der Referenz (`AIAttackState::onExit` setzt die Turmziele auf `NULL`,
sobald ein anderer AI-Zustand den Angriff ersetzt); es zurückzunehmen würde ein KI-Ergebnis reparieren, indem ein belegtes Verhalten gefälscht wird. Der Idle-Scan während eines Angriffs ist schon ausgeschlossen
(ein Ziel unterbricht ihn). Was der Referenz gegenüber fehlt, ist der Zielerwerb durch die Einheit selbst (Angriffsmarsch, `friend_checkForIdleMoodTarget`): dort verliert eine marschierende Einheit ihr Ziel nicht
dauerhaft, weil sie sich das nächste selbst sucht. Bis Phase 16 übernahm das der Befehlsgeber im nächsten Denkschritt.

**Nachtrag Phase 17 (5. Oktober 2026):** der Zielerwerb durch die Einheit und der Angriffsmarsch sind umgesetzt (`docs/SIMULATION-CORE.md` §33). "Ein neuer Befehl beendet das Zielen" gilt unverändert
(`EndAttack` löscht jetzt auch den Angriffszustand der Einheit); die Einheit sucht sich danach selbst das nächste Opfer: im Angriffsmarsch zwei Frames nach dem Befehl, sonst sobald sie wieder ruht. Die 16 KI-Läufe sind
dort neu erhoben; die Spalten dieses Abschnitts beschreiben den Stand davor.
## 9. Nachziehen in anderen Dokumenten (nicht Teil dieser Änderung)

`docs/SIMULATION-CORE.md` (Waffenwahl-Näherungen „Turm- und Neigungsbedingungen fehlen“, Abschnitt zur Waffenwahl), `docs/PRESENTATION-OBJECTS.md` (Zeile 47 `TURRET_ROTATE`, Abschnitt zu `TurretYaw`: „null, weil die Simulation
keinen Turmwinkel führt“), `docs/MODEL-CONDITIONS-UNITS.md` (Zeilen zu `TurretTracker`), `docs/CLIENT-OBJECTS.md`, `docs/DEMO-FINDINGS.md`, `README.md`/`ROADMAP.md`/`IMPLEMENTATION-PLAN.md`
(Snapshotversion 9, Turmzustand, Befehlstabelle 13, Replay 10) und `CLAUDE.md` (Referenzwerte) beschreiben noch den Stand ohne Turmzustand beziehungsweise ohne Angriffszustand.
