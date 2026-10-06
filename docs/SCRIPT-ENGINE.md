# Skript-VM und Missionssitzung (Phase 9, Teil A Punkt 2 und 5; Phase 10–12: Erweiterungen)

Dieses Dokument beschreibt die **Ausführung** der World-Builder-Skripte (Dekodierung: `docs/MAP-SCRIPTS.md`) in
`CnCNew.Simulation`: die Skript-VM (`ScriptEngine`), die Missionssitzung (`MissionSession`), ihren speicherbaren Zustand
(`MissionSnapshot`) und die automatisierten Headless-Prüfungen. Kampagnenablauf und Savegames stehen in
`docs/CAMPAIGN-AND-SAVEGAMES.md`.

Quellen (Algorithmus verstanden, neu geschrieben, nicht kopiert): `GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/`
`ScriptEngine.cpp` (`update`, `executeScript`, `evaluateConditions`, `executeActions`, `evaluateCounter/Flag/Timer`,
`setCounter/Timer/adjustTimer`, `enable/disableScript`, `callSubroutine`, `checkConditionsForTeamNames`,
`AttackPriorityInfo`), `ScriptConditions.cpp` (jede implementierte Bedingung, Funktion in der Tabelle §4),
`ScriptActions.cpp` (jede implementierte Aktion, Funktion in der Tabelle §5), `AI/AIPlayer.cpp` (`newMap`,
`buildStructureNow`), `Common/RTS/Team.cpp` (`allInside`, `someInsideSomeOutside`, `hasAnyBuildFacility`).

## 1. Schichtung

| Baustein | Ort | Aufgabe |
| --- | --- | --- |
| `ScriptProgram` | `Simulation/Scripting` | unveränderliche, für die Ausführung umgeformte Skriptdaten einer Karte: Skripte/Gruppen in Laufreihenfolge, Namensindizes mit der Erster-Treffer-Regel von `findScript`/`findGroup`, Spieler (Sides), Teamprototypen (Einheiten, Transport, Ursprung), Wegpunkte, Wegpunktpfade (`waypointPathLabel1..3` + Links, Schleifenerkennung), Trigger-Bereiche (Polygon, Even-Odd-Strahltest), platzierte Objekte; strukturelle Prüfsumme (`Fingerprint`) |
| `ScriptEngine` | `Simulation/Scripting` | der veränderliche Zustand und der Tick: Zähler/Timer, Flags, Skript-/Gruppenaktivierung, Auswertungszeitplan, benannte Objekte, Teaminstanzen, Objekttyplisten, Folgeaufträge (Pfade, Jagd, Angriff), Angriffsprioritätssätze, Schadenshistorie, Ergebnis; `Describe`/`Load`/`StateHash` |
| `ScriptCoverage` | `Simulation/Scripting` | die Unterstützungstabelle (ausgeführt / weitergereicht / **aufgezeichnet** / fehlend) für alle 109 Bedingungs- und 344 Aktionstypen; `Validate()` prüft sie gegen `MapScriptCatalog` |
| `MissionSession` | `Simulation/Mission` | baut aus einer Karte Welt, Spieler, Beziehungen, platzierte Objekte, Baulisten und menschliche Startbasis; ein Tick = Skripte → automatisches Gefecht → Weltschritt; einziger Befehlsannehmer mit einem Sequenzzähler pro Spieler |
| `MissionSnapshot` | `Simulation/Mission` | kanonischer, versionierter Zustand (Welt-Snapshot + Skriptzustand + Sitzungszähler) |
| `MissionScenario`, `MissionProbe` | `Simulation/Testing` | Headless-Läufe, Speichern/Laden-Fortsetzung, Endprobe für Sieg/Niederlage |

`ScriptEngine` ist seit Phase 11 auf Teildateien verteilt (eine `partial class`, ein Zustand): `ScriptEngine.cs` (Kern, Tick, Zustand, `Describe`/`Load`), `.Conditions.cs`/`.Actions.cs`
(die Weiche der Grundtypen), `.Extended.cs` (die `SKIRMISH_*`-, Sicht- und Spezialkraft-Bedingungen, `Observe`-Erweiterungen), `.ExtendedActions.cs` (Aufdeckung, Garnison, Bauschalter,
Team-Bau/-Merge, Wegpunkt-Varianten der Befehlsknöpfe), `.Sequential.cs` (Sequenzskripte), `.Powers.cs` (benannte Spezialkräfte, benannte Aufdeckungen, Rekrutierung, Aufgezeichnetes),
`.TeamEvents.cs` (Teamereignisskripte), `.TeamAbilities.cs` (Phase 12: Überschreibungs-Beziehungen, die `TEAM_ALL_USE_COMMANDBUTTON_*`-Familie, Jagd mit Befehlsknopf, Transportbeladung, Panik, Blickrichtung, Tarnschalter,
unbemannt/gehalten, Rang-/Prioritäts-/Haltungswerte). Die Weichen der Grundtypen rufen bei unbekanntem Typ nacheinander die Erweiterungen auf; erst wenn keine zuständig ist, wird der Typ als fehlend
gezählt. Neuer Zustand (Angriffshistorie je Spieler, ausgelöste Spezialkräfte, Teambau-Aufträge, Sequenzläufer, benannte Aufdeckungen, Aufgezeichnetes, Teamereigniszustand) wird in
`Describe`/`Load` nur geschrieben, wenn er nicht leer ist (die Form eines Laufs ohne diese Eigenschaften bleibt byteidentisch).

Die VM hängt nur von `CnCNew.Data` ab (`SourceRules` gilt): keine Wandzeit, keine ambienten Zufallszahlen (Zufallstimer ziehen aus
`SimulationWorld.LogicRandom`), kein `float`/`double`, keine Dateizugriffe. Alle Sammlungen, die in Zustand oder Hash eingehen, sind
`Sorted*` mit ordinalem Vergleich.

## 2. Ein Tick

`MissionSession.Step()` = `ScriptEngine.Update(tick)` → `SimulationWorld.Step()` (darin der Zielerwerb der Einheiten, `docs/SIMULATION-CORE.md` §33) → `ScriptEngine.Observe(events)`.
Bis Phase 16 stand dazwischen `DriveCombat(tick)`, der Gefechtstreiber der Sitzung; er ist entfallen.

`ScriptEngine.Update` endet seit Phase 11 mit `UpdateFollowers` → `ProcessTeamEvents` (§5b) → `ProcessSequences` (§5a) → `ProcessTeamBuilds`; die Menge „Objekte, die in diesem Tick einen
Befehl bekamen“ (`_busyThisTick`) wird zu Beginn geleert, weil ein Befehl erst im Weltschritt wirkt, die Sequenzlogik das Objekt des Originals aber sofort als beschäftigt behandeln muss.
Der Kern folgt `ScriptEngine::update`: Endspiel-Zeitgeber (`VICTORY`/`DEFEAT` starten 120 Ticks
`FRAMES_TO_SHOW_WIN_LOSE_MESSAGE`, danach läuft keine Skriptauswertung mehr, `QUICKVICTORY` 1 Tick); Zähler mit `isCountdownTimer`
zählen bis −1 herunter; dann je Spieler-Skriptliste die Top-Level-Skripte, danach die aktiven, nicht als Unterprogramm markierten
Gruppen, jeweils ohne Unterprogramme. `ExecuteScript` beachtet `IsActive`, Schwierigkeitsflags, `DelayEvaluationSeconds`
(nächster Auswertungszeitpunkt), wertet den ODER-von-UND-Baum mit Kurzschluss aus und führt Wahr- bzw. Falsch-Aktionen aus;
`IsOneShot` deaktiviert das Skript nach dem Wahr-Zweig (beim Falsch-Zweig nur im Fall ohne Teambezug – wie im Original).
Bedingungen, die einen Team-Parameter nennen, werden je Instanz des Teams ausgewertet (`checkConditionsForTeamNames`: mehrfach
instanziierbare Prototypen haben Vorrang vor Singletons); `<This Team>`, `<This Player>` und `<Local Player>` werden aufgelöst.
`CALL_SUBROUTINE` ruft Gruppen (nur aktive Unterprogramm-Gruppen) und Einzelskripte (nur Unterprogramme), Rekursionstiefe 32
(Überschreitung wird als Diagnose gezählt).

**Bildzähler und Tickrate (Phase 20, `docs/TICKRATE-MIGRATION.md` §1 Fall (f), §8).** Die Tickrate ist ein Wert der Welt; die VM rechnet über `_world.Time`. *Skaliert* wird jeder Wert, der in der
Simulation zu einem Tick-Countdown wird: die Bildzahlen der Karte (`SET_TIMER`, `SET_RANDOM_TIMER` – der Zufall wird in Bildern gezogen, dann skaliert –, die `*_FOR_FRAMECOUNT`-Aktionen,
`teamInitialIdleFrames`), Sekundenwerte (`SET_MILLISECOND_TIMER` und Verwandte, `DelayEvaluationSeconds`: auf ganze Bilder des Originals aufgerundet, dann mal Rate/30), der Endspiel-Zeitgeber
(`EndGameDelayFrames` = 120 Bilder; `QUICKVICTORY` bleibt ein Update, das ist Reihenfolge), der erste Auswertungstick verzögerter Skripte (`NameHash % 61` Bilder), die Präsentationslatenz
(`PresentationLatencyTicks`, trotz des Namens in Bildern) und die eigenen Rhythmen (Neuziel alle 30 Bilder, Übernahme-Wiederholung, Versorgungsscan). Die „Ticks“ in diesem Abschnitt sind Werte bei
30 Hz. *Nicht skaliert* wird, was die Simulation als Text verlässt: die `Arguments` eines `ScriptOutput` sind die Parameter der Karte, eine Bildzahl darunter (`CAMERA_FADE_*`,
`CAMERA_BW_MODE_*`) ist bei jeder Rate eine Zahl von Bildern des Originals (1/30 s); `ScriptOutput.Tick` ist ein Tick der Welt. Der Wert eines Timers (`CounterValue`) ist Zustand in **Ticks**.
Offen für Phase 23: die VM wertet jedes aktive Skript in **jedem Tick** aus und zählt Timer je Tick – ein Skript, das je Auswertung einen Zähler erhöht oder einen Timer gegen ein Bildliteral
vergleicht, läuft bei 60 Hz anders (`docs/TICKRATE-MIGRATION.md` §5.4).

`Observe` nimmt die Ereignisse des Ticks entgegen: `EntityDamaged` → letzter Angreifer eines Objekts (Besitzer, Typ; Grundlage der
`*_ATTACKED_BY_*`-Bedingungen, nie zurückgesetzt wie `getLastDamageInfo`), `EntityContained` → letzter Spieler, der ein Gebäude betrat.

### Objekte entstehen sofort

Das Original legt Kartenobjekte beim Laden und Skriptobjekte in der Aktion selbst an (`newObject`), nicht am Tickende. Die Welt
registriert Spawns am Ende von `Step`; `SimulationWorld.MaterializePendingSpawns()` zieht das vor (Kartenladen, `CREATE_*`,
Verstärkung). Ohne das wäre `PLAYER_ALL_DESTROYED` im ersten Tick für jeden Spieler wahr und jede Mission verloren (so wurde es
beim ersten Real-Lauf entdeckt).

## 3. Befehle und Weltzugriff

Befehle an Einheiten (`MOVE_*`, Pfade, Jagd, Angriff) sind **normale Spielerbefehle** (`SetMoveTargetCommand`) des Besitzers über
`MissionSession.Issue` – also mit derselben Validierung, demselben Sequenzzähler (je Spieler streng steigend über alle
Befehlsquellen) und denselben Ablehnungsgründen. Direkte Weltänderungen, die kein Befehl abdeckt, nutzen die Weltschnittstelle:
`RequestSpawn`, `Kill`, `RequestDestroy`, `Damage`, `AddMoney`/`WithdrawMoney`, sowie neu `SimulationWorld.SetMoney`, `SetRelationship`
und `TransferOwnership` (verschiebt auch die Energieproduktion) in `SimulationWorld.Scripting.cs`.

**Folge:** Eine Mission ist **nicht** als `ReplayFile` reproduzierbar (Skriptaktionen sind keine aufgezeichneten Befehle). Wahrheit
ist der Snapshot (`MissionSnapshot`); der Beweis ist „gespeichert und wiederhergestellt läuft hash-identisch weiter“.

## 4. Bedingungen

Unterstützung laut `ScriptCoverage` (Zahlen der Tabelle: `ScriptSupportTableIsConsistent`, im Testlauf ausgegeben). Jede Zeile nennt die
Referenzfunktion in `ScriptConditions.cpp`/`ScriptEngine.cpp`.

| Typ | Referenz | Anmerkung |
| --- | --- | --- |
| `CONDITION_TRUE/FALSE`, `COUNTER`, `FLAG`, `TIMER_EXPIRED` | `ScriptEngine::evaluateCondition/Counter/Flag/Timer` | `FLAG` ist auch durch eine UI-Interaktion erfüllt (`SignalUiInteraction`), ein nie gestarteter Timer ist nicht abgelaufen |
| `PLAYER_ALL_DESTROYED`, `…_ALL_BUILDFACILITIES_DESTROYED` | `evaluateAllDestroyed/AllBuildFacilitiesDestroyed` | Bauanlage = Struktur mit `ProductionUpdate`/`DozerAIUpdate` (§8) |
| `PLAYER_HAS_CREDITS` | `evaluatePlayerHasCredits` | Parameter links des Vergleichs, wie dort |
| `PLAYER_HAS_N_OR_FEWER_(FACTION_)BUILDINGS`, `PLAYER_HAS_(NO_)POWER` | `evaluatePlayerHasNOrFewer*`, `evaluatePlayerHasPower` | Kräfteparameter-Reihenfolge (Spieler zuerst); Strom = Produktion ≥ 0 |
| `PLAYER_HAS_OBJECT_COMPARISON`, `…_UNIT_TYPE_IN_TRIGGER_AREA`, `…_UNIT_KIND_IN_TRIGGER_AREA`, `BUILT_BY_PLAYER`, `PLAYER_LOST_OBJECT_TYPE` | `evaluatePlayerUnitCondition`, `…UnitTypeInArea`, `…UnitKindInArea`, `evaluateBuiltByPlayer`, `evaluatePlayerLostObjectType` | Objekttypen: benannte Liste (`OBJECTLIST_ADDOBJECTTYPE`) oder Einzeltyp |
| `PLAYER_ACQUIRED_SCIENCE`, `START_POSITION_IS`, `MISSION_ATTEMPTS`, `SKIRMISH_PLAYER_FACTION`, `PLAYER_DESTROYED_N_BUILDINGS_PLAYER` | `evaluateScienceAcquired`, `evaluateSkirmishStartPosition`, `evaluateMissionAttempts`, `evaluateSkirmishPlayerIsFaction`, `evaluatePlayerDestroyedNOrMoreBuildings` | Versuche kommen aus dem Kampagnenfortschritt; die letzte ist im Original ein nicht implementierter Stub, der **immer FALSE** liefert, und ist hier ebenso ausgeführt |
| `NAMED_DESTROYED/NOT_DESTROYED/DYING/TOTALLY_DEAD/CREATED/OWNED_BY_PLAYER`, `UNIT_HEALTH`, `UNIT_HAS_OBJECT_STATUS` | `evaluateNamedUnit*`, `evaluateNamedCreated/OwnedByPlayer`, `evaluateUnitHealth`, `evaluateUnitHasObjectStatus` | „hat existiert“ (`didUnitExist`) gilt, sobald das Objekt einmal in der Welt lebte |
| `NAMED_INSIDE/OUTSIDE_AREA`, `NAMED_ENTERED/EXITED_AREA`, `TEAM_INSIDE_AREA_ENTIRELY/PARTIALLY`, `TEAM_OUTSIDE_AREA_ENTIRELY`, `TEAM_ENTERED/EXITED_AREA_*` | `evaluateNamedInsideArea`, `Team::allInside`, `Team::someInsideSomeOutside` | Oberflächenmaske des Parameters gegen `Locomotor.Surfaces`; Eintritt/Austritt merken den Zustand der letzten Auswertung |
| `TEAM_HAS_UNITS`, `TEAM_DESTROYED`, `TEAM_CREATED`, `TEAM_OWNED_BY_PLAYER` | `evaluateHasUnits`, `evaluateIsDestroyed`, `evaluateTeamCreated`, `evaluateTeamOwnedByPlayer` | |
| `NAMED_/TEAM_ATTACKED_BY_PLAYER/OBJECTTYPE` | `evaluateNamed/TeamAttackedByPlayer/Type` | Angreifer wird nach dem Weltschritt gelesen (§2) |
| `NAMED_/TEAM_REACHED_WAYPOINTS_END` | `evaluateNamed/TeamReachedWaypointsEnd` | „irgendein Mitglied“ für Teams |
| `TEAM_ALL_HAS_OBJECT_STATUS`, `TEAM_SOME_HAVE_OBJECT_STATUS` | `evaluateTeamHasObjectStatus` | |
| `BUILDING_ENTERED_BY_PLAYER` | `evaluateBuildingEntered` | letzter eintretender Spieler |
| `MULTIPLAYER_ALLIED_VICTORY/DEFEAT`, `MULTIPLAYER_PLAYER_DEFEAT` | `evaluateMultiplayer*` (`VictoryConditions`) | bezogen auf den Betrachter (`LocalPlayer`) |
| `HAS_FINISHED_SPEECH/AUDIO/VIDEO`, `MUSIC_TRACK_HAS_COMPLETED`, `CAMERA_MOVEMENT_FINISHED`, `NAMED_SELECTED` | `evaluate*HasCompleted`, `isCameraMovementFinished`, `evaluateNamedSelected` | Host-Zustand (§6) |

| `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA`, `SKIRMISH_PLAYER_IS_OUTSIDE_AREA`, `SKIRMISH_NAMED_AREA_EXIST` | `evaluateSkirmishPlayerHasUnitsInArea`, `…IsOutsideArea`, `…NamedAreaExists` | der Spieler hat ein lebendes, nicht inertes Nicht-Geschoss-Objekt im Bereich; „außerhalb“ verlangt, dass Spieler und Bereich existieren; der Zwischenspeicher des Originals (`didEnterOrExit`) ist reine Beschleunigung und entfällt |
| `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` | `evaluateSkirmishPlayerHasBeenAttackedByPlayer`, `ActiveBody::attemptDamage` (`Player::setAttackedBy`) | Paare (Opfer-Besitzer, Angreifer-Besitzer) aus den `EntityDamaged`-Ereignissen, nie zurückgesetzt |
| `SKIRMISH_VALUE_IN_AREA` | `evaluateSkirmishValueInArea` | Summe der `BuildCost` der lebenden, nicht inerten Objekte des Spielers im Bereich gegen den Vergleich |
| `SKIRMISH_SPECIAL_POWER_READY`, `SKIRMISH_COMMAND_BUTTON_READY_ALL/PARTIAL` | `evaluateSkirmishSpecialPowerIsReady`, `evaluateSkirmishCommandButtonIsReady` | irgendein fertiges, nicht im Bau befindliches Objekt des Spielers trägt die Kraft bereit (`ISpecialPowerModule.IsReady`, Wissenschaft vorhanden); der „deaktiviert“-Test des Originals entfällt (§8); Knopf: nur Spezialkraft-Knöpfe |
| `SKIRMISH_PLAYER_HAS_COMPARISON_GARRISONED`, `SKIRMISH_PLAYER_HAS_DISCOVERED_PLAYER` | `evaluateSkirmishPlayerHasComparisonGarrisoned`, `…HasDiscoveredPlayer` | besetzte `GarrisonContain`-Objekte des Spielers; irgendein Objekt des ersten Spielers in einer Zelle, die der zweite gerade sieht |
| `PLAYER_TRIGGERED_SPECIAL_POWER`(`_FROM_NAMED`) | `evaluatePlayerSpecialPowerFromUnitTriggered`, `ScriptEngine::isSpecialPowerTriggered` (`removeFromList = TRUE`) | aus `SpecialPowerUsed`-Ereignissen; die Bedingung **verbraucht** den Eintrag, wie im Original |
| `NAMED_BUILDING_IS_EMPTY`, `NAMED_DISCOVERED`, `TEAM_DISCOVERED` | `evaluateIsBuildingEmpty`, `evaluateNamedDiscovered`, `evaluateTeamDiscovered` | Behälter ohne Insassen; „entdeckt“ = in einer für den Spieler sichtbaren Zelle (oder dessen eigen) und nicht getarnt-ohne-Erkennung |
| `TYPE_SIGHTED`, `ENEMY_SIGHTED` | `evaluateTypeSighted`, `evaluateEnemySighted` | Objekte im `VisionRange`-Kreis des Beobachters, lebend, nicht getarnt (außer erkannt/verkleidet); Beziehung 0 Feind/1 neutral/2 Freund |

Alle übrigen Bedingungstypen sind **fehlend**: Auswertung = `false` und Zähler `MissingConditionHits[Typ]` (nie still).

## 5. Aktionen

| Gruppe | Typen | Referenz |
| --- | --- | --- |
| Zustand | `SET_FLAG`, `SET_COUNTER`, `INCREMENT/DECREMENT_COUNTER`, `SET_TIMER`, `SET_MILLISECOND_TIMER`, `SET_RANDOM_TIMER`, `SET_RANDOM_MSEC_TIMER`, `STOP_TIMER`, `RESTART_TIMER`, `ADD_TO/SUB_FROM_MSEC_TIMER`, `ENABLE/DISABLE_SCRIPT`, `CALL_SUBROUTINE`, `OBJECTLIST_ADD/REMOVEOBJECTTYPE`, `NO_OP` | `ScriptEngine.cpp` (`setFlag` … `callSubroutine`), `doObjectTypeListMaintenance` |
| Ausgang | `VICTORY`, `DEFEAT`, `QUICKVICTORY` | `doVictory/doDefeat/doQuickVictory` |
| Spieler | `PLAYER_SET_MONEY`, `PLAYER_GIVE_MONEY`, `PLAYER_RELATES_PLAYER`, `PLAYER_GRANT_SCIENCE`, `PLAYER_KILL`, `PLAYER_TRANSFER_OWNERSHIP_PLAYER`, `NAMED_/TEAM_TRANSFER…` | `doSetMoney`, `doGiveMoney`, `updatePlayerRelationTowardPlayer`, … |
| Objekte/Teams | `NAMED_KILL/DELETE/DAMAGE`, `TEAM_KILL/DELETE(_LIVING)`, `DAMAGE_MEMBERS_OF_TEAM`, `CREATE_OBJECT`, `UNIT_SPAWN_NAMED_LOCATION_ORIENTATION`, `CREATE_NAMED/UNNAMED_ON_TEAM_AT_WAYPOINT`, `CREATE_REINFORCEMENT_TEAM` | `doNamedKill` …, `doCreateObject`, `createUnitOnTeamAt`, `doCreateReinforcements` |
| Befehle | `MOVE_NAMED_UNIT_TO`, `MOVE_TEAM_TO`, `*_FOLLOW_WAYPOINTS(_EXACT)`, `*_STOP`, `*_GUARD*`, `*_HUNT`, `PLAYER_HUNT`, `*_ATTACK_*` | `doMoveToWaypoint`, `doTeamFollowWaypoints`, … (siehe §8) |
| Angriffsprioritäten | `SET_ATTACK_PRIORITY_THING/KIND_OF`, `SET_DEFAULT_ATTACK_PRIORITY`, `NAMED_/TEAM_APPLY_ATTACK_PRIORITY_SET` | `setPriorityThing/Kind/Default`, `AttackPriorityInfo::getPriority` |
| Befehlsknopf-Fähigkeiten | `NAMED_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)`, `TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` | `doNamed/TeamUseCommandButtonAbility(OnNamed)`: löst den benannten `CommandButton` (`Command = SPECIAL_POWER*`) als gewöhnlichen `UseSpecialPowerCommand` des Trägers aus (`CommandCatalog.TryGetSpecialPowerAbility`); ein Knopf einer anderen Befehlsart oder ein unbekannter Name ist ein Diagnosefall, keine Ausführung (§8) |
| Befehlsknöpfe, andere Arten | `PLAYER_UPGRADE`/`OBJECT_UPGRADE` (kaufen den genannten Upgrade über `PurchaseUpgradeCommand`), `FIRE_WEAPON` (mit Zielobjekt: `FireWeaponCommand` des Trägers aus dem Slot des Knopfes, der Slot wird gesperrt; `SWITCH_WEAPON` sperrt dauerhaft) | `Object::doCommandButton*`; jede andere Befehlsart („has command SELL, not executed“) und ein unbekannter Knopf („unknown command button“) sind je ein eigener gezählter Diagnosefall |
| Befehlsknopf-Fähigkeiten, Wegpunkte | `NAMED_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT`, `…_USING_WAYPOINT_PATH` (für den Partikelstrahl entlang des Pfades, sonst nur der nächste Pfadpunkt), `TEAM_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT` | `doNamedUseCommandButtonAbilityAtWaypoint`, `…UsingWaypointPath`, `doTeamUseCommandButtonAbilityAtWaypoint`; das Entlangfliegen des Pfades gibt es nur für den Partikelstrahl (§8 Punkt 23) |
| Spezialkräfte direkt | `NAMED_FIRE_SPECIAL_POWER_AT_WAYPOINT`/`_AT_NAMED`, `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST`, `NAMED_SET/ADD_SPECIAL_POWER_COUNTDOWN`, `NAMED_STOP/START_SPECIAL_POWER_COUNTDOWN` | `doNamedFireSpecialPowerAtWaypoint/AtNamed`, `doSkirmishFireSpecialPowerAtMostCost`, `doNamedSet/AddSpecialPowerCountdown`, `doNamedStopSpecialPowerCountdown` (`SetReadyFrame`, `PauseCountdown`) |
| Kartensicht | `MAP_REVEAL_ALL(_PERM/_UNDO_PERM)`, `MAP_SHROUD_ALL`, `MAP_REVEAL/SHROUD_AT_WAYPOINT`, `MAP_REVEAL_PERMANENTLY_AT_WAYPOINT`/`MAP_UNDO_REVEAL_PERMANENTLY_AT_WAYPOINT` | `doRevealMapEntire(Permanently)`, `doShroudMapEntire`, `doRevealMapAtWaypoint`, `doShroudMapAtWaypoint`, `doRevealMapAtWaypointPermanent` (`docs/SIMULATION-CORE.md` §26); leerer Spielername = der Betrachter (`getHumanPlayerMask`) |
| Behälter | `NAMED_ENTER_NAMED`, `TEAM_ENTER_NAMED`, `NAMED/TEAM_GARRISON_SPECIFIC_BUILDING`, `NAMED/TEAM_GARRISON_NEAREST_BUILDING`, `PLAYER_GARRISON_ALL_BUILDINGS`, `NAMED_EXIT_ALL`, `TEAM_EXIT_ALL`, `EXIT_SPECIFIC_BUILDING`, `NAMED_EXIT_BUILDING`, `TEAM_EXIT_ALL_BUILDINGS`, `PLAYER_EXIT_ALL_BUILDINGS` | `doNamedEnterNamed`, `doTeamEnterNamed`, `doUnit/TeamGarrisonSpecificBuilding`, `doUnit/TeamGarrisonNearestBuilding`, `Player::garrisonAllUnits`, `doNamedExitAll`, `doTeamExitAll`, `doExitSpecificBuilding`, `doUnitExitBuilding`, `doTeamExitAllBuildings`; Eintreten = `MoveAndEnterCommand`, Verlassen = `ExitContainerCommand`/`EvacuateCommand` |
| Bau und Wirtschaft | `PLAYER_ENABLE/DISABLE_BASE/UNIT_CONSTRUCTION`, `PLAYER_PURCHASE_SCIENCE`, `AI_PLAYER_BUILD_UPGRADE`, `WAREHOUSE_SET_VALUE`, `BUILD_TEAM`, `TEAM_MERGE_INTO_TEAM`, `RECRUIT_TEAM`, `TEAM_AVAILABLE_FOR_RECRUITMENT`, `IDLE_ALL_UNITS` | `doPlayerEnable/Disable…Construction` (`Player::allowedToBuild`), `doPlayerPurchaseScience`, `doBuildUpgrade`/`AIPlayer::buildUpgrade` (Spieler-Upgrade über den gewöhnlichen Kauf, ohne Fabriksuche), `doSetWarehouseValue`, `doBuildTeam`/`AIPlayer::buildSpecificAITeam`, `doMergeTeamIntoTeam`, `doRecruitTeam`/`AIPlayer::recruitSpecificAITeam`/`Team::tryToRecruit`, `doIdleAllPlayerUnits` |
| Sequenzskripte | `TEAM/UNIT_EXECUTE_SEQUENTIAL_SCRIPT(_LOOPING)`, `TEAM/UNIT_STOP_SEQUENTIAL_SCRIPT`, `TEAM/UNIT_GUARD/IDLE_FOR_FRAMECOUNT`, `TEAM_SPIN_FOR_FRAMECOUNT`, `TEAM_WAIT_FOR_NOT_CONTAINED_ALL/PARTIAL`, `SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_ALL/PARTIAL` | §5a |
| Überschreibungs-Beziehungen (Phase 12) | `TEAM_SET/REMOVE_OVERRIDE_RELATION_TO_TEAM`, `TEAM_SET/REMOVE_OVERRIDE_RELATION_TO_PLAYER`, `PLAYER_SET/REMOVE_OVERRIDE_RELATION_TO_TEAM`, `TEAM_REMOVE_ALL_OVERRIDE_RELATIONS` | `doTeamSetOverrideRelationToTeam`, `…ToPlayer`, `doPlayerSetOverrideRelationToTeam`, `doTeamRemoveAllOverrideRelations` (`Team::setOverrideTeamRelationship`/`setOverridePlayerRelationship`, `Team::getRelationship` mit der Reihenfolge Team→Team, Team→Spieler, Spieler→Team, danach Spielerbeziehung). In der Welt: `SimulationWorld.RelationshipOverride` (ein Haken, der je Objektpaar die Beziehung liefern kann; sortierter, gespeicherter Zustand im `ScriptEngine`); Kampf, Zielwahl und `AreEnemies(Entity, Entity)` benutzen `RelationshipBetween` |
| Befehlsknopf-Familie (Phase 12) | `TEAM_HUNT_WITH_COMMAND_BUTTON`, `TEAM_ALL_USE_COMMANDBUTTON_ON_NAMED`, `…_ON_NEAREST_ENEMY_UNIT/_ENEMY_BUILDING/_ENEMY_BUILDING_CLASS/_KINDOF/_GARRISONED_BUILDING/_OBJECTTYPE`, `TEAM_PARTIAL_USE_COMMANDBUTTON` | `doTeamHuntWithCommandButton`, `doTeamUseCommandButtonOnNamed`, `doTeamUseCommandButtonOnNearest*`, `doTeamPartialUseCommandButton`; der Knopf geht als gewöhnlicher Befehl (Spezialkraft, `FIRE_WEAPON`, Upgrade) an die Träger; `…ON_NEAREST_*` wählt je Träger das nächste passende Ziel, die Jagd setzt einen Auftrag (`_buttonHunts`), der alle paar Ticks das nächste Ziel neu sucht (die Hülle `CommandButtonHuntUpdate` trägt den Knopf, nicht die Suche) |
| Team-Bewegung und -Zustand (Phase 12) | `TEAM_LOAD_TRANSPORTS`, `TEAM_MOVE_TOWARDS_NEAREST_OBJECT_TYPE`, `TEAM_GUARD_IN_TUNNEL_NETWORK`, `TEAM_PANIC`, `NAMED/TEAM_FACE_WAYPOINT`, `NAMED/TEAM_SET_STEALTH_ENABLED`, `NAMED/TEAM_SET_UNMANNED_STATUS`, `DELETE_ALL_UNMANNED`, `TEAM_INCREASE/DECREASE_PRIORITY` | `doTeamLoadTransports`, `doTeamMoveTowardsNearestObjectType`, `doTeamGuardInTunnelNetwork`, `doTeamPanic`, `doNamed/TeamFaceWaypoint`, `doNamed/TeamSetStealthEnabled`, `doNamed/TeamSetUnmannedStatus`, `doDeleteAllUnmanned`, `TeamPrototype::increaseAIPriorityForSuccess`/`decreaseAIPriorityForFailure` (§8 Punkt 19–22 für die Näherungen; die Priorität liest nur `RECRUIT_TEAM`) |
| Team-Ausrichtung, Verkauf, Einstellungen (Phase 13) | `TEAM_FACE_NAMED`, `PLAYER_SELL_EVERYTHING`; aufgezeichnet: `NAMED_SET_STOPPING_DISTANCE`, `NAMED_SET_REPULSOR`, `SET_BASE_CONSTRUCTION_SPEED` | `doTeamFaceNamed` (jedes Mitglied hält an und blickt zum Objekt; ohne Drehrate), `doPlayerSellEverything` → `Player::sellEverythingUnderTheSun` (ein `SellCommand` je Struktur, die Welt prüft wie bei einem Spieler), `doNamedSetStoppingDistance`/`doNamedSetRepulsor`/`doSetBaseConstructionSpeed` (kein Verbraucher; `RecordedSetting`) |
| Karten- und KI-Aktionen (Phase 13, Fortsetzung) | `NAMED_SET_TOPPLE_DIRECTION`, `TECHTREE_MODIFY_BUILDABILITY_OBJECT`, `RESUME_SUPPLY_TRUCKING`, `LOCALDEFEAT`, `UNIT_MOVE_TOWARDS_NEAREST_OBJECT_TYPE`, `TEAM_CAPTURE_NEAREST_UNOWNED_FACTION_UNIT`, `SKIRMISH_MOVE_TO_APPROACH_PATH`, `SKIRMISH_FOLLOW_APPROACH_PATH`, `TEAM_GUARD_SUPPLY_CENTER`, `AI_PLAYER_BUILD_SUPPLY_CENTER`, `TEAM_SET_STATE`, `NAMED_FACE_NAMED`, `UNIT_DESTROY_ALL_CONTAINED`, `NAMED_RECEIVE_UPGRADE`, `PLAYER_REPAIR_NAMED_STRUCTURE`; ohne Wirkung wie im Original: `PLAYER_CREATE_TEAM_FROM_CAPTURED_UNITS`, `TEAM_COLLECT_NEARBY_FOR_TEAM`, `DEBUG_CRASH_BOX` | `ScriptEngine.Phase13.cs`: `setToppleDirection` (Tabelle, die `ToppleUpdate`/`StructureToppleUpdate` über `IWorldServices` lesen), `setBuildableStatusOverride` (`SimulationWorld.BuildableOf`), `setUnitsShouldIdleOrResume(false)` (`SupplyCarrierAI.ForceWanting`), `doLocalDefeat` (= Niederlage), `doMoveUnitTowardsNearest`, `doTeamCaptureNearestUnownedFactionUnit` (die Mitglieder laufen zum nächsten unbemannten Objekt, wer es erreicht, übernimmt es und es zählt als *erobert*, `Entity.Captured`), `doTeamMoveToSkirmishApproachPath`/`doTeamFollowSkirmishApproachPath` (Pfad `Bezeichnung + (Start-Index des Feindes + 1)`, `ScriptPlayer.StartIndex`; Feind = der Betrachter, der Rückfall des Originals), `AIPlayer::guardSupplyCenter`/`buildBySupplies`/`findSupplyCenter` (Lager mit genug Vorrat, ohne eigenen Geldgenerator in der Nähe, nicht viel näher am Feind; Bauplatz in wachsenden Ringen, ein Bauarbeiter baut, ohne Geld wartet der Auftrag), `doSetTeamState`, `doNamedFaceNamed`, `doDestroyAllContained`, `doUnitReceiveUpgrade`, `Player::repairStructure` (der nächste Bauarbeiter bekommt den Reparaturbefehl) |
| Einstellungen ohne Verbraucher (Phase 13, Fortsetzung) | `NAMED_SET_ATTITUDE`, `TEAM_SET_REPULSOR`, `SET_STOPPING_DISTANCE`, `CHOOSE_VICTIM_ALWAYS_USES_NORMAL`, `SET_CAVE_INDEX`, `PLAYER_ADD_SKILLPOINTS`, `PLAYER_SELECT_SKILLSET` (aufgezeichnet) | je ein Wert in `RecordedSetting` (Schlüssel `AKTION|Gegenstand`), nur im Zustand, wenn gesetzt |
| Bedingungen (Phase 13, Fortsetzung) | `TEAM_STATE_IS`/`_IS_NOT`, `NAMED_HAS_FREE_CONTAINER_SLOTS`, `UNIT_EMPTIED`, `PLAYER_POWER_COMPARE_PERCENT`, `PLAYER_EXCESS_POWER_COMPARE_VALUE`, `SKIRMISH_UNOWNED_FACTION_UNIT_EXISTS`, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE`, `SUPPLY_SOURCE_SAFE`/`_ATTACKED`, `PLAYER_BUILT_UPGRADE(_FROM_NAMED)`, `PLAYER_COMPLETED_SPECIAL_POWER(_FROM_NAMED)`, `PLAYER_MIDWAY_SPECIAL_POWER(_FROM_NAMED)`, `PLAYER_HAS_SCIENCEPURCHASEPOINTS`, `PLAYER_CAN_PURCHASE_SCIENCE`, `SKIRMISH_SUPPLIES_VALUE_WITHIN_DISTANCE`, `SKIRMISH_PLAYER_HAS_PREREQUISITE_TO_BUILD`, `SKIRMISH_PLAYER_HAS_COMPARISON_CAPTURED_UNITS`; immer falsch wie im Original: `OBSOLETE_SCRIPT_1/2`, `DEFUNCT_PLAYER_SELECTED_GENERAL(_FROM_NAMED)`, `*_COMPLETED_SEQUENTIAL_EXECUTION` | `ScriptEngine.Phase13.cs`: die Zustandszeichenkette von `TEAM_SET_STATE`; Stromverhältnis aus den negativen `EnergyProduction`-Werten der Objekte (die Simulation führt nur die Summe); `evaluateUnitHasEmptied` mit dem Zustand je Objekt; die konsumierenden Listen `isUpgradeComplete`/`isSpecialPowerComplete` aus den Ereignissen `UpgradeCompleted`/`SpecialPowerCompleted` (`SpecialPowerCompletionDie` meldet seinen Träger mit Erzeuger); `AIPlayer::isSupplySourceSafe/Attacked` (Radius 250 aus `AI.cpp`, Zwei-Sekunden-Zwischenspeicher des Originals entfällt); die Antwort der Tech-Gebäude-Bedingung wird wie im Original dauerhaft gemerkt; `PLAYER_MIDWAY_…`: das Original ruft `notifyOfMidwaySpecialPower` nie, die Liste bleibt leer |
| Haltezustände, Fallen, Brücken (Phase 14) | `NAMED_SET_HELD`, `PLAYER_DISABLE_FACTORIES`, `PLAYER_ENABLE_FACTORIES`, `SET_TRAIN_HELD`, `NAMED_SET_EVAC_LEFT_OR_RIGHT`, `NAMED_SET_BOOBYTRAPPED`, `TEAM_SET_BOOBYTRAPPED`; Bedingungen `BRIDGE_BROKEN`, `BRIDGE_REPAIRED`; der Skriptname folgt einer Übernahme (`ObjectNameTransferred`) | `ScriptEngine.Phase13.cs`: `Entity.ScriptDisableMask` (Bit 1 `DISABLED_HELD`: AI und Produktion ruhen; Bit 2 `OBJECT_STATUS_SCRIPT_DISABLED`: Produktion steht), `RailroadBehavior.SetHeld`, `GarrisonContain.EvacDisposition`, `_pendingTraps` (Fallenobjekt am Zufallspunkt des Opferumrisses, `StickyBombUpdate.InitStickyBombAtOffset`), `_bridgeStates` (ein Update lang wahr nach dem Wechsel in/aus Rubble); Einzelheiten und Quellen: `docs/SIMULATION-CORE.md` §30.4 |
| **Aufgezeichnet** | `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, `OBJECT_ALLOW_BONUSES`; seit Phase 12 `TEAM_SET_ATTITUDE`, `TEAM_WANDER`, `TEAM_WANDER_IN_PLACE`, `PLAYER_SET_RANKLEVELLIMIT`, `PLAYER_SET_RANKLEVEL`, `PLAYER_ADD_RANKLEVEL`, `SCRIPTING_OVERRIDE_HULK_LIFETIME` | der Wert wird gespeichert (`SkillPointsModifierOf`, Schalter, Haltung, Rang, Haltezustand, Wrack-Lebensdauer), aber **kein Simulationsteil liest ihn** (keine Erfahrungsgewinn-Skalierung je Spieler, keine Schwierigkeitsboni, keine KI-Haltung, kein Rangsystem, keine Wrackliste; `NAMED_SET_HELD` ist seit Phase 14 ausgeführt); getrennter Zähler `RecordedActions`, weder „ausgeführt“ noch „fehlend“ (§8) |
| **Weitergereicht** (Präsentation) | Kamera, Ton, Sprache, Film, Musik, Radar, UI-Text, Blende, Countdown-Anzeigen, Eingabesperre u. a. (129 Typen) | als `ScriptOutput` (Tick, Aktion, Parameter als Text) an den Client; `SPEECH_PLAY`/Film/Musik/Kamerabewegung gelten für `PresentationLatencyTicks` als laufend |

Alle übrigen Aktionstypen sind **fehlend**: keine Wirkung, Zähler `MissingActionHits[Typ]`. Stand Phase 14 (`CampaignTool coverage`): Bedingungen 109 von 109 ausgeführt; Aktionen **187 ausgeführt, 129 weitergereicht, 19 aufgezeichnet, 9 fehlend**
(`SKIRMISH_BUILD_BUILDING`, `SKIRMISH_BUILD_BASE_DEFENSE_FRONT/_FLANK`, `SKIRMISH_BUILD_STRUCTURE_FRONT/_FLANK`, `SKIRMISH_ATTACK_NEAREST_GROUP_WITH_VALUE`, `SKIRMISH_PERFORM_COMMANDBUTTON_ON_MOST_VALUABLE_OBJECT`, `AI_PLAYER_BUILD_TYPE_NEAREST_TEAM`,
`NAMED_FIRE_WEAPON_FOLLOWING_WAYPOINT_PATH`); keine der neun kommt in einer Kampagnenkarte vor (`CampaignTool usage` leer). Die ersten sechs gehören zu `AISkirmishPlayer` (Bauliste, Basisradius, Seiten-`AIData`), die sich die Simulation mit
dem `SkirmishAi`-Ersatz (§5c) nicht teilt.

Nicht auflösbare Verweise zur Laufzeit (unbekannter Spieler/Team/Wegpunkt/Bereich/Objekttyp/Wissenschaft, nicht instanziierbarer Typ,
zu kurze Parameterliste, Rekursion) zählt `Diagnostics`; `ExplainScript(name)` liefert je Bedingung eines Skripts das aktuelle
Ergebnis (Werkzeug/Test, verändert kantengetriggerte Bedingungen).

## 5a. Sequenzskripte (Phase 11)

Quelle: `ScriptEngine::evaluateAndProgressAllSequentialScripts`, `appendSequentialScript`, `cleanupSequentialScript`, `setSequentialTimer`, `ScriptActions::doTeam/UnitStartSequentialScript`,
`doTeam/UnitStopSequentialScript`, `doTeam/UnitIdle/GuardForFramecount`, `doTeamSpinForFramecount`. Das ist der Weg, auf dem Kampagnenskripte ein Team oder eine Einheit durch eine
Befehlsliste führen (422 Skripte auf 21 der 24 Karten benutzen `TEAM_EXECUTE_SEQUENTIAL_SCRIPT`).

- Ein **Läufer** (`SequenceRunner`) gehört einem Team oder einem Objekt und hat eine Warteschlange von Skripten (`SequenceItem`: Skript, Wiederholungen, Befehlszeiger, Wartezeit, „halten“). Ein
  weiterer Aufruf für dasselbe Team/Objekt hängt hinten an. `TEAM_EXECUTE_SEQUENTIAL_SCRIPT` stoppt das Team zuerst (`groupIdle`).
- Jeden Tick: ist die Gruppe **untätig** (kein Bewegungsziel, kein stehender Folgeauftrag, in diesem Tick kein Befehl erteilt) und keine Wartezeit offen (`FramesToWait < 1`), oder ist die
  Wartezeit genau 0, läuft **eine** Aktion der Wahr-Liste des Skripts (die Bedingungen des Skripts werden nie ausgewertet). Bleibt die Gruppe danach untätig, läuft derselbe Läufer im
  selben Tick weiter (bis 256 Schritte); ein Befehl (Bewegung, Folgeauftrag, Eintritt) macht sie beschäftigt, eine `*_FOR_FRAMECOUNT`-Aktion setzt die Wartezeit, die dann tickweise
  herabzählt.
- Ende des Skripts: `Loops != 0` hängt eine frische Kopie hinten an (`-1` = ewig; `…_LOOPING(n)` heißt n Läufe, intern n−1 Wiederholungen); danach fällt das Skript aus der Warteschlange.
  Ein Team ohne lebende Mitglieder oder ein totes/zerstörtes Objekt beendet den Läufer samt aller dahinter wartenden Skripte (`cleanupSequentialScript(…, TRUE)`).
- `TEAM_WAIT_FOR_NOT_CONTAINED_ALL/PARTIAL` und `SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_ALL/PARTIAL` behandelt der Läufer selbst (Haltemarke statt Aktion); außerhalb eines Sequenzskripts
  sind sie wie im Original unzulässig („should never be used“) und werden als Diagnose gezählt. `<This Team>`/`<This Object>` lösen sich auf das Team/Objekt des Läufers auf.
- **Zustand:** der Läufer wird gespeichert (`sequences`, nur wenn vorhanden); der Fortsetzungstest (`ScriptSequentialScripts`) deckt einen laufenden Läufer ab.
- **Abweichung:** „untätig“ ist hier „kein Bewegungsziel und kein Folgeauftrag“ – eine Einheit, die schießt oder eine Fähigkeit ausführt, gilt als untätig (das automatische Gefecht der
  Sitzung kennt keinen AI-Zustand); `AIGroup::isIdle` ist exakt „alle beweglichen Mitglieder untätig“.

## 5b. Teamereignis-Skripte (Phase 11)

Quelle: `Team::updateState`, `Team::notifyTeamOfObjectDeath`, `ScriptEngine::runScript`; Schlüssel `teamOnCreateScript`, `teamOnIdleScript`, `teamEnemySightedScript`, `teamAllClearScript`,
`teamOnDestroyedScript`, `teamDestroyedThreshold`, `teamOnUnitDestroyedScript` (`ScriptTeamPrototype`). Die Skripte laufen als Unterprogramm mit dem Team als `<This Team>`
(`CallSubroutine`; ein Name, der kein Unterprogramm ist oder fehlt, wird als Diagnose gezählt). Ohne sie sind Wellenzähler und viele Missionsenden unerreichbar (z. B. `MD_CHI04`: der Zähler der
sechsten Welle hängt an `teamOnUnitDestroyedScript`).

- **Erstellt:** läuft einmal, sobald das Team `Created` ist; danach wird die Schwelle für „zerstört“ gesetzt: `curUnits − curUnits·threshold`, auf 0…`curUnits−1` begrenzt (Standard-Schwelle 0 heißt
  also „schon bei der ersten Verlustmeldung“, wie im Original).
- **Einheit zerstört:** je `EntityDied` eines Mitglieds, ausgeführt am Anfang des nächsten Updates (die Warteschlange wird gespeichert).
- **Zerstört:** wenn die Zahl lebender Mitglieder sich ändert und die Schwelle erreicht ist; läuft einmal (`threshold = −1` danach).
- **Gegner gesichtet/Lage klar:** je Tick, ob irgendein lebendes Mitglied einen Feind im eigenen `VisionRange` hat; der Wechsel löst das Skript aus (nur solange mindestens ein Mitglied lebt).
- **Untätig:** zweimal hintereinander untätig (`isIdle && wasIdle`) löst es aus, bei jedem weiteren Tick wieder.
- Zustand je Team (`eventsCreateHandled`, `eventsCurUnits`, `eventsThreshold`, `eventsSeeEnemy`, `eventsWasIdle`) wird nur für Prototypen mit mindestens einem Ereignisskript geschrieben.

## 5c. Die `SKIRMISH_*`-Familie: Entscheidung (Phase 11)

Zwei Wege standen offen (`docs/HANDOFF-PHASE11.md` Punkt 1): die Familie portieren oder `SkirmishAi` für Gegner der Challenge-Karten einhängen. **Entschieden: Portierung.** Begründung:
(a) die Bedingungen sind klein, rein lesend und je Funktion in `ScriptConditions.cpp` abgrenzbar (§4); (b) sie stehen in den Karten-Skripten selbst und steuern dort das Verhalten der
Kartenseiten (die häufigste fehlende Bedingung auf den Challenge-Karten, 37.227 Treffer bei `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA`) – `SkirmishAi` wäre ein Ersatz *statt* der Skriptlogik und
würde die Missionsdaten umgehen, statt sie auszuführen; (c) die Skirmish-KI des Originals (`AISkirmishPlayer`) ist ohnehin die Ausführung genau dieser Skripte plus Bauschlange, nicht etwas
Eigenes daneben. **Was fehlt:** `SKIRMISH_SUPPLIES_VALUE_WITHIN_DISTANCE`, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE`, `SKIRMISH_UNOWNED_FACTION_UNIT_EXISTS`, `SKIRMISH_PLAYER_HAS_PREREQUISITE_TO_BUILD`,
`SKIRMISH_PLAYER_HAS_COMPARISON_CAPTURED_UNITS`, `SUPPLY_SOURCE_SAFE/ATTACKED` (Lieferquellen-Modell der KI), sowie die Aktionen `SKIRMISH_MOVE_TO_APPROACH_PATH`/`SKIRMISH_FOLLOW_APPROACH_PATH`,
`AI_PLAYER_BUILD_SUPPLY_CENTER`, `TEAM_GUARD_SUPPLY_CENTER` – sie hängen am KI-Spieler des Originals, den die Kampagnen-Sitzung nicht hat (die Gegner sind rein
geskriptet).

## 6. Host-Schnittstelle

`RegisterNamed`, `AssignToTeam` (Sitzung, beim Platzieren), `SignalUiInteraction(flag)`, `SelectNamed(name)`,
`CompletePresentation(name)`, `DrainOutputs()`. Ohne Client gilt `PresentationLatencyTicks = 0`: Sprache/Film/Kamera sind sofort
„fertig“ (Headless-Läufe); ein Client darf eine Latenz setzen oder `CompletePresentation` selbst melden.

## 7. Missionssitzung

- **Spieler** je benannter Seite (Seite 0 = neutral/`PlayerId.System`), Fraktion nur, wenn sie eine echte `PlayerTemplate` ist (sonst
  Notiz). **Beziehungen** aus `playerAllies`/`playerEnemies`; alles nicht Genannte ist neutral (Standard von `Player::getRelationship`).
- **Platzierte Objekte** gehören dem Spieler ihres `originalOwner`-Teams (ohne Team: neutral), mit `objectInitialHealth`,
  `objectName` (→ benanntes Objekt) und Teamzugehörigkeit. Nicht instanziierbare oder unbekannte Vorlagen zählt `SkippedObjects`
  (Hauptgrund: Kartenüberlagerungs-Definitionen, die die Retail-Basisdaten nicht enthalten, und noch fehlende Module).
- **Bauliste** (`AIPlayer::newMap`/`buildStructureNow`): für Computerspieler entstehen die `InitiallyBuilt`-Einträge fertig, benannt nach
  `buildingName`, mit Anfangsgesundheit.
- **Startbasis menschlicher Spieler:** besitzt eine menschliche Seite mit echter Fraktion nichts auf der Karte (Generals'-Challenge-Karten),
  startet sie wie ein Skirmish-Spieler: Basisgebäude auf `Player_N_Start` plus Startfahrzeuge im Ring.
- **Lokale Seite** (`<Local Player>`, `MULTIPLAYER_*`-Bedingungen): `MissionSession.LocalPlayerOf` nimmt die erste Seite, die die Karte selbst
  `playerIsHuman` setzt; `MissionOptions.HumanSideName` (Version 2 des Missionszustands) lässt den Host stattdessen eine beliebige Seite beim Namen
  nennen — bewusst unabhängig von `playerIsHuman`, weil die Generals'-Challenge-Karten die gewählte General-Seite als gewöhnliche KI-Seite modellieren
  und die „Mensch ist das“-Entscheidung dem Host überlassen (`GC_LaserGeneral` markiert keine Seite als Mensch). Ein nicht vorhandener Name fällt auf
  denselben Standard zurück und hinterlässt eine Notiz in `MissionSession.Notes`, nie still. `CampaignController.StartCurrentMission` und
  `CampaignTool audit|playthrough --human-side <Name>` reichen die Wahl durch; die eigentliche General-Auswahl bleibt Sache der Shell-Anbindung
  (`docs/HANDOFF-PHASE10.md` Teil 2).
- **Gefecht** (seit Phase 17, `docs/SIMULATION-CORE.md` §33): die Einheiten wählen ihre Opfer selbst (`AutoAcquireEnemiesWhenIdle`, `MoodAttackCheckRate`); der frühere Gefechtstreiber
  `DriveCombat` (jedes bewaffnete Objekt schoss alle drei Ticks auf den nächsten Feind in Reichweite, auch im Gehen und ohne Rücksicht auf sein `AutoAcquire`) ist entfallen. Der Skript-Motor ist
  `IAcquisitionAdvisor` der Welt: er liefert die Angriffspriorität (`AttackPriorityInfo`; Priorität 0 wird nie angegriffen, je 100 Einheiten Entfernung ein Punkt Abzug) und die Stimmung des Teams
  (`teamAggressiveness` der Karte, überschrieben durch `TEAM_SET_ATTITUDE`): schlafende und passive Teams eines Computerspielers erwerben nichts, wachsame und aggressive machen aus jedem
  Bewegungsbefehl des Skripts einen Angriffsmarsch (`MAA_Action_To_AttackMove`) und suchen 1,1- bzw. 1,5-mal so weit. Jagd-, Team- und Gebietsangriffe (`UpdateFollowers`) sind Angriffsmärsche
  (`AttackMoveCommand`) zum Ziel; der Angriff auf ein benanntes Objekt schickt zusätzlich den Schuss als Befehl, sobald eine Waffe reicht (`FireAtOrdered`), weil das benannte Opfer nicht das
  sein muss, das die Einheit selbst wählen würde. `MissionOptions.CombatIntervalTicks` ist kein Intervall mehr: 0 schaltet den Zielerwerb der Mission ab (Tests), jeder andere Wert lässt ihn an
  (Name und Snapshot-Feld bleiben, `MissionSnapshot` 2). `MissionCombatStats.Shots` zählt jetzt alle `WeaponFired`-Ereignisse.

## 7a. Mission im Client: was `--mission` tut und was von den `ScriptOutput`s zu sehen ist (Phase 16, Punkt 5)

**Ablauf.** `CnCNew.Client.Stride --mission <Karte>` lädt die Karte mit `MissionRun.Load` (dieselbe Sitzung wie die Headless-Tests und `CampaignTool`), die Seite des Menschen ist
`MissionSession.LocalPlayerOf` (bzw. `--human-side`). Jeder Logiktick des Clients ist `MissionPresentationDirector.BeforeStep` → `MissionSession.Step()` → `AfterStep` (`LiveMission.StepSimulation`);
der Konsolenbericht am Ende nennt Frames, Welt-Tick und Sitzungsschritte – in allen Messläufen gleich, die Skript-VM läuft also jeden Tick. Ein Ladebildschirm oder Briefing gehört zur Shell
(`--shell`), `--mission` beginnt direkt mit Tick 0 der Karte.

**Zeitverhalten.** `--mission` startet mit `PresentationLatencyTicks = LiveMission.ClientPresentationLatencyTicks` (1.800 Ticks = 60 s). Der Director meldet das echte Ende selbst
(`CompletePresentation`): Kamerafahrten nach ihrer Skriptdauer, Sprache/Klang/Film nach einer Platzhalterdauer von 3 s (es gibt keine Längenangabe je Klangereignis), Musik sofort. Die Latenz ist nur
die Grenze, nach der die VM nicht länger wartet, damit nichts ewig hängt. **Offen:** Missionen, die die Shell startet (`GameShell` → `CampaignController.StartCurrentMission()`), laufen weiter mit
Latenz 0 – `CAMERA_MOVEMENT_FINISHED`/`HAS_FINISHED_*` gelten dort sofort, die Kamera fährt trotzdem (zeitgesteuerte Sequenzen wie die von `GC_ChemGeneral` sind davon nicht betroffen).

**Angehaltene Zeit (`FREEZE_TIME`/`UNFREEZE_TIME`, zweite Sitzung).** Das ist ein Simulationsvorgang, kein Hostvorgang: `GameLogic::update` führt `ScriptEngine::UPDATE` aus und kehrt bei
`isTimeFrozenScript()` zurück – Objekte, Kampf und der Bildzähler stehen, die Skripte laufen weiter (Zeitgeber `m_counters[i].value--` je Aufruf, Sequenzskripte, `UNFREEZE_TIME` selbst). Genau das tut
`MissionSession.Step` jetzt (`ScriptEngine.TimeFrozen`, in `Forward` gesetzt und von `End` gelöscht = `forceUnfreezeTime`; gespeichert unter `timeFrozen`, nur solange gesetzt, also bleiben
alle anderen Snapshots und Hashes unverändert). Der Host darf die Simulation dafür **nicht** selbst anhalten: dann gäbe es auch kein Skript mehr, das auftaut. Er braucht aber eine
Uhr, die weiterläuft: `MissionPresentationDirector.Clock` = Welt-Tick + Zahl der eingefrorenen Schritte (ohne Einfrieren gleich dem Welt-Tick); Kamerafahrten, Blenden, Untertitel,
Grauschleier und Bewegungsunschärfe laufen auf ihr, so gibt `CAMERA_MOVEMENT_FINISHED` auch bei stehender Welt frei. Grenzen: Befehle, die ein Skript in einem eingefrorenen Schritt gibt, werden
in der Warteschlange gesammelt und beim Auftauen ausgeführt (das Original befiehlt direkt; ein wiederholter Bewegungsbefehl ist harmlos, ein wiederholter Spawn wäre es nicht); verzögert
ausgewertete Skripte (`DelayEvaluationSeconds`) vergleichen mit dem Welt-Tick und warten wie im Original, bis die Zeit weiterläuft; `CAMERA_MOD_FREEZE_TIME` (die Sicht friert die Logik während
einer Kamerafahrt ein) ist nicht abgebildet, keine Kampagnenkarte benutzt es.

**Was der Director aus den Ausgaben macht** (`src/CnCNew.Game/Shell/MissionPresentation.cs`, ohne Stride; der Client liest nur diesen Zustand):

- **Kamera** (`ApplyCameraTo(RtsCamera, Sekunden)`, je Frame aus `LiveGame.OnAdvanced`; bei Pause 0): Fahrt zum Wegpunkt über die Skriptdauer mit weichem An- und Auslauf
  (`ScriptActions::doMoveCameraTo` → `View::moveCameraTo`; die Ease-Zeiten des Skripts werden durch eine feste S-Kurve ersetzt), Schnitt mit Blickrichtung/Zoom/Neigung
  (`doSetupCamera`), Zoom und Neigung als Faktor der Standardwerte (`doZoomCamera`, `doPitchCamera`, `cameraModFinalZoom/Pitch`), Drehung (`doRotateCamera`), Blick zum Wegpunkt
  (`cameraModLookToward`; das Vorzeichen des Kamerawinkels wird an der Kamera gemessen, nicht angenommen), Zurücksetzen (`doResetCamera`), Verfolgen eines benannten Objekts
  (`doCameraFollowNamed`). Ohne aktive Skriptfahrt bleibt die Kamera dem Spieler. Grenzen: Zoom und Neigung werden auf den spielbaren Bereich der `RtsCamera` begrenzt (Nahaufnahmen der
  Zwischensequenzen sind dadurch weiter weg als im Original), `MOVE_CAMERA_ALONG_WAYPOINT_PATH` fährt geradlinig zum ersten Wegpunkt.
- **Eingabesperre** (`DISABLE_INPUT`/`ENABLE_INPUT`, `ScriptActions::doDisableInput`): `MissionController` setzt `MatchController.WorldInputEnabled`; alles außer Esc (Spielmenü) wird verworfen, der
  Zeiger scrollt nicht, beim Sperren werden Auswahl, Befehlsmodi und gehaltene Tasten gelöscht. Ein Sieg oder eine Niederlage hebt die Sperre auf.
- **Letterbox und Blenden:** `CAMERA_LETTERBOX_BEGIN/END` blenden die Befehlsleiste aus und zeichnen zwei Balken (`doLetterBoxMode`: `HideControlBar`), `CAMERA_FADE_ADD/SUBTRACT/MULTIPLY` eine
  bildschirmfüllende Fläche (Weiß bzw. Schwarz) mit dem Verlauf Anstieg – Halten – Abfall; `CAMERA_FADE_SATURATE` hat keine Darstellung.
- **Text** (seit der Missionstext-Sitzung nach den Daten des Originals, Einzelheiten im Abschnitt „Bildschirmtexte nach `InGameUI`“ unten): `DISPLAY_TEXT` als Meldungszeile oben links, `SHOW_MILITARY_CAPTION` als
  getippte Beschriftung unten links, `DISPLAY_CINEMATIC_TEXT` zentriert bei 0,9 der Höhe, Zähler und Countdown (`DISPLAY_COUNTER`, `DISPLAY_COUNTDOWN_TIMER`, Wert aus `ScriptEngine.CounterValue`, Countdown als Minuten:Sekunden)
  bei `NamedTimerCountdownPosition`, `INGAME_POPUP_MESSAGE` als Meldungsfenster mit OK-Knopf, Ergebnisbanner bei `VICTORY`/`DEFEAT` („MISSION ACCOMPLISHED“/„MISSION FAILED“, erfundener Platzhaltertext in fester Schrift).
  Die frühere Hinweiszeile während der Sperre gibt es nicht mehr (das Original zeigt nichts).
- **Ton: Wiedergabe** (`MissionPresentationDirector.AudioControl.PlayScripted` ← `GameAudio.PlayScripted`): `SPEECH_PLAY`, `SOUND_PLAY_NAMED`, `PLAY_SOUND_EFFECT`, `PLAY_SOUND_EFFECT_AT` spielen das benannte Klangereignis ohne
  Position (`AudioDirector.PlayUi`), `MUSIC_SET_TRACK` startet das Stück (`AudioDirector.SetMusicTrack`), danach läuft die Playlist der Seite weiter. Für Sprache zeigt der Director den Untertitel
  `DIALOGEVENT:<Name>Subtitle` wie `doSpeechPlay` (als Missionsbeschriftung, 8 s; nur wenn die Tabelle ihn hat, er nicht leer ist und nicht mit `*` beginnt – die englische Retailtabelle verbirgt 2.454 von 2.455) und daneben einen gekennzeichneten
  Platzhalter mit dem Ereignisnamen. Was nicht startet (unbekannter Name, keine Datei), steht mit Grund unter „problems“.
- **Film:** `MOVIE_PLAY_FULLSCREEN`/`MOVIE_PLAY_RADAR` zeigen den Filmnamen als Textplatzhalter (kein Bink-Decoder, dauerhafte Grenze).
- **Radar:** `RADAR_CREATE_EVENT`, `OBJECT_/TEAM_CREATE_RADAR_EVENT` werden als weißer, blinkender Punkt auf der Minimap gezeichnet (`RadarBlipsForDisplay`, 8 s). In den Messläufen kam genau ein
  Radarereignis vor (`MD_USA01`, Tick 3.191); es wurde der Minimap übergeben, im Bild aber nicht eigens geprüft.

Zweite Sitzung (alle Ausgabetypen, die Kampagnenkarten ausgeben, siehe die Tabellen unten; `Game/Shell/MissionOutputCatalog.cs` führt je Typ Zustand und Beschreibung/Grund):

- **Ton** (`IMissionAudioControl`, von `GameAudio` umgesetzt): Lautstärken, Ereignis-Override, Stummschaltung, Entfernen, Objektton, Eva-Schalter, Ambient-Pause – `docs/GAME-AUDIO.md`, Abschnitt „Skriptsteuerung des Tons“.
- **Kamera** (`ApplyCameraTo`): `SCREEN_SHAKE(Typ 0..5)` ruft `RtsCamera.Shake` mit der `GameData`-Stärke des Typs (`ShakeSubtleIntensity` … `ShakeCineInsaneIntensity`, `CameraShakeIntensities`) am Blickpunkt
  auf (`W3DView::shake`: volle Stärke, abklingend mit 0,75 je Logikbild und umkehrend; die Richtung zieht das Original aus dem Client-Zufall, hier eine Goldwinkelfolge); `MAP_SWITCH_BORDER(n)` setzt
  `RtsCamera.ActiveBoundary` (die Kamera bleibt im Rahmen dieser Kartengrenze; eine Grenze, die die Karte nicht hat, wird gemeldet; die Minimap behält Grenze 0); `CAMERA_LOOK_TOWARD_OBJECT` dreht
  zur Position des Objekts und hält `Dauer + Haltezeit` als Kamerabewegung; `CAMERA_TETHER_NAMED` ist als Verfolgen genähert (Spielraum ignoriert).
- **Bildfilter** (`MissionPresentationDirector.PostEffects`, gezeichnet von `ScreenFilters` im Stride-Client über der fertigen Welt und unter dem HUD): `CAMERA_BW_MODE_BEGIN/END(Bilder)` rampt den
  Grauanteil über die Bilder (`ScreenBWFilter::set`); `CAMERA_MOTION_BLUR_JUMP(Wegpunkt, Sättigen)` ist der Zählautomat von `ScreenMotionBlurFilter::postRender`: +5 je Logikbild bis 60 (12 Bilder), am
  Scheitel Schnitt der Kamera zum Wegpunkt (`TheTacticalView->lookAt`), dann −5 bis 0 (Zoomfaktor `sqrt(1 − Zähler/60 · 0,9)`, bis zu 30 weitere Schichten mit 1 % Zoom und Alpha 0x15, bei der
  sättigenden Variante 2 % und additiv). Ein Filter ersetzt den anderen (`setViewFilter`). **Näherungen, nicht Kopien:** der Grauschleier ist der **Rotkanal** allein (`SwizzleMode.RRRR` ist alles, was
  der Sprite-Batch an Kanalmischung bietet; das Original mischt 0,30/0,59/0,11), das Bild wirkt wie durch ein Rotfilter fotografiert; die Unschärfe summiert die Schichten über Sprite-Zeichnungen
  der kopierten Bildfläche statt über Texturkoordinaten im Shader. Ob beides gut aussieht, kann nur ein Mensch beurteilen (Bildprobe unten).
- **Objekte** (`IMissionObjectEffects`, `LiveMission` → `FrameProjector`): `NAMED_FLASH`/`TEAM_FLASH` (Hausfarbe) und `…_WHITE` blitzen alle 15 Logikbilder `Sekunden · 2`-mal (`Drawable::setFlashCount`,
  `DRAWABLE_FRAMES_PER_FLASH`), jedes Blitzen ist die Tint-Hüllkurve 0 Anstieg/4 Abfall der Einnahme (`SelectionFlashTracker.StartScripted/Advance`), die Farbe ungesättigt (`colorFlash(&color)`);
  `NAMED_CUSTOM_COLOR` ersetzt die Hausfarbe eines Objekts (`FrameProjector.SetHouseColor`, Wert ARGB, 0 = zurück); das wirkt auch auf die Minimap-Punkte, wie im Original (`Radar.cpp`).
- **Befehlsleiste und HUD** (`IMissionHudControl`, `MissionController`): `COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE(Knopf, Objekttyp)` entfernt den Knopf aus dem Befehlssatz des Objekttyps **für jedes Objekt, das
  diesen Satz benutzt** (`GameLogic::setControlBarOverride`, Schlüssel Satzname + Platz; `CommandBarModel.RemoveButton/AddButton`, Zustand in `ControlBarScriptState`), `…_ADD_BUTTON_…_SLOT` setzt ihn in
  einen Platz (1-basiert); `PLAYER_SCIENCE_AVAILABILITY` (nur für die lokale Seite) macht Wissenschaften in `PurchaseScience`-Knöpfen verborgen/gesperrt; `OPTIONS_SET_DRAWICON_UI_MODE` lässt die
  Heilsymbole weg; `DISABLE_/ENABLE_SPECIAL_POWER_DISPLAY` und `NAMED_HIDE_/NAMED_SHOW_SPECIAL_POWER_DISPLAY` schalten die **Superwaffen-Liste** (oben rechts „Name: m:ss“ je Macht mit öffentlichem
  Zeitgeber, neu in dieser Sitzung, `SuperweaponTimers`, `docs/GAME-HUD.md` §14) ganz oder je Objekt aus und ein; `UNIT_/TEAM_AFFECT_OBJECT_PANEL_FLAGS` hat für **Selectable** einen Verbraucher (`EntityPicker.SetScriptSelectable`: nicht wählbar, fliegt aus der Auswahl), die anderen Flags
  (`Enabled`, `Powered`, `Indestructible`, `Unsellable`, `AI Recruitable`, `Player Targetable`) brauchen Simulationszustand und stehen als Problem im Bericht.

**Bildschirmtexte nach `InGameUI` (Phase 16, Missionstexte).** Quellen: `InGameUI.cpp` (`message`/`addMessageText`, `update`, `postDraw`, `militarySubtitle`, `addNamedTimer`, `popupMessage`), `ScriptActions.cpp`
(`doDisplayText`, `doMilitaryCaption`, `doDisplayCinematicText`, `doInGamePopupMessage`, `doDisplayCounter`/`…CountdownTimer`), `W3DDisplay.cpp` (`draw`: Filmtext), `InGamePopupMessage.cpp`; neu geschrieben, nicht kopiert.
Die Werte kommen aus den Daten: `InGameUiSettings` (`CnCNew.Ui/Text`, die typisierte `InGameUI.ini`: Positionen, Farben, Zeiten) und `Language.ini` (Schriften; `InGameUI::init` überschreibt jede Schrift des Blocks durch den
gleichnamigen Language-Platz, immer, weil der Standardname eines Platzes nie leer ist – gemessen stimmen Größe und Fettdruck in der Retail-Datei überein, nur der unzitierte Name „Courier New“ der INI wird von
`parseAsciiString` zu „Courier“), zusammengefasst in `InGameUiStyle` (`CnCNew.Game/Data`), skaliert wie jeder Text (`GlobalLanguage::adjustFontSize`, `LanguageFonts.Resolve`; 1280x720: Arial 10 → 14, Courier New 12 → 17).
Ohne `MissionPresentationDirector.TextStyle` (oder ohne Textmaß) wird kein Text gezeichnet und der erste Textausgang ist ein gemeldetes Problem – nie eine erfundene Größe. Ein Label, das der Host nicht auflösen kann,
erscheint als sich selbst und steht unter „problems“ (`label … has no text`).

| Ausgabe | Weg im Original | Ort | Schrift, Farbe | Zeit und Verhalten |
| --- | --- | --- | --- | --- |
| `DISPLAY_TEXT` | `InGameUI::message` → `addMessageText` | `MessagePosition` (10, 22) in **unskalierten** Pixeln, eine Zeile unter der anderen (Höhe der Schrift), älteste oben | `MessageFont` (Arial 10 fett), `MessageColor1`/`MessageColor2` abwechselnd (verglichen mit der Farbe der vorigen Zeile, auch einer verblassten), Schatten schwarz mit dem Alpha der Zeile | höchstens 6 Zeilen; `MessageDelayMS / 30 / 1000` ist **ganzzahlig 0**, die Zeile verliert ab dem ersten Bild `floor(Alter · 0,01)` Alpha je Schritt (nichts bis Alter 99, dann 1, ab 200 je 2) und verschwindet bei Alter 277 (9,2 s) |
| `SHOW_MILITARY_CAPTION` | `militarySubtitle`, `m_militarySubtitle` in `update`/`postDraw` | `MilitaryCaptionPosition` (10, 340), x mit Breite/800, y mit Höhe/600 skaliert; eine Zeile unter der anderen, eine leere Zeile hat keine Höhe | erste Zeile `MilitaryCaptionTitleFont` (Courier New fett), weitere `MilitaryCaptionFont`; `MilitaryCaptionColor` (weiß), Schatten schwarz | Schreibmaschine: erstes Zeichen nach `MilitaryCaptionDelayMS` (`Language.ini`, 750 ms = 22 Bilder), dann je `MilitaryCaptionSpeed` (1) Bild – der Vergleich ist „kleiner als“, also ein Zeichen je **zweitem** Bild –; ein Zeilenumbruch beginnt die nächste Zeile und wartet erneut 22 Bilder; höchstens 4 Zeilen (Rest verworfen); Blockcursor (Höhe der Schrift, 0,8 breit) hinter dem letzten Zeichen, schaltet alle 10 Bilder; nach der Dauer (`30 · ms / 1000` Bilder) verliert die Farbe `floor((Bild − Ende) · 0,1)` Alpha je Schritt, bei Unterschreiten von 0 ist sie weg (76 Schritte); eine neue Beschriftung ersetzt die alte; leerer Text oder Dauer ≤ 0 erzeugt keine (gemeldet); `MilitaryCaptionRandomizeTyping` ist im Original ohne Wirkung |
| `DISPLAY_CINEMATIC_TEXT` | `doDisplayCinematicText`, `W3DDisplay::draw` | Oberkante bei `0,9 · Höhe`, zentriert, Umbruch bei Breite − 20 (`UiSentenceLayout`, zentrierter Aufbau; x = 20, wenn breiter als der Schirm) | die Schrift aus dem Skript-String `Name - Size:N [Bold]` (angepasst), weiß, kein Schatten | `30 · Sekunden` Bilder; die Zerlegung des Strings ist im Original fehlerhaft (bricht an Leerzeichen im Namen), hier sauber geteilt; kein Kampagnenskript benutzt die Aktion |
| `DISPLAY_COUNTER` / `…COUNTDOWN_TIMER` | `addNamedTimer`, `postDraw` | `NamedTimerCountdownPosition` (0,90 / 0,666 der Bildfläche), je Zeile nach **oben** um die Schrifthöhe, Reihenfolge nach Name; ab x ≥ 0,5 rechtsbündig (`reverseXDir`) | `NamedTimerCountdownNormalFont`/`…Color` (Arial 10, weiß); bei 0:00 `…ReadyFont` und Blinken zwischen Farbe und `…FlashColor` alle `…FlashDuration` (15 Bilder), Blinkfarbe in den ungeraden Perioden | Text „Label m:ss“ (Countdown, `floor(Bilder / 30)`, nie negativ) oder „Label Wert“ (Zähler, roher Wert); `DISABLE_COUNTDOWN_TIMER_DISPLAY` blendet alle aus |
| Superwaffenliste | `postDraw` | `SuperweaponCountdownPosition` (0,90 / 0,01), `docs/GAME-HUD.md` §14 | `SuperweaponCountdown*Font` (bereit fett), Hausfarbe, Blinkfarbe `…FlashColor` | wie dort; „...“ ab 0,82 der Höhe |
| `INGAME_POPUP_MESSAGE` | `popupMessage` → `InGamePopupMessageInit` | x/y in Prozent (0..100) der Bildfläche, Breite ≥ 50 Pixel; Text Umbruch Breite − 14, OK unten rechts (`MissionPopupWindow`) | die Fenster aus `InGamePopupMessage.wnd` (Schrift der Vorlage; Textfarbe 254 statt `PopupMessageColor`) | steht bis OK, Enter oder Escape (beim Loslassen); mit Pausenmarke pausiert die Mission (`LiveGame.Paused`) und das Fenster ist modal; ohne bleibt die Welt außerhalb des Fensters bedienbar |
| `SPEECH_PLAY` | `doSpeechPlay`: Untertitel `DIALOGEVENT:<Name>Subtitle` über `militarySubtitle` (8000 ms), nur wenn vorhanden, nicht leer, kein führendes `*` | wie die Missionsbeschriftung | wie diese | englisch fast nie (2.454 von 2.455 mit `*`); daneben der Platzhalter „(speech: Name)“ unten mittig in der `MessageFont`, 3 s |
| Film | – (Platzhalter, kein Bink) | unten mittig (in der Letterbox im unteren Balken) | `MessageFont` der Daten, weiß | 3 s; gekennzeichneter Platzhalter |

Alle Zeiten laufen auf der Präsentationsuhr (`MissionPresentationDirector.Clock`, Welt-Tick plus eingefrorene Schritte), also weiter bei angehaltener Zeit; im Original verringert `update` dafür `lifetime`/`blockBeginFrame`/`incrementOnFrame`
je Bild. Der Zustand ist reine Funktion der Bilder seit dem Beginn (`UiMessageLog`, `MilitaryCaptionState` in `Shell/MissionText.cs`, ein `Update(frame)` je Schritt), die Zeichenlisten baut `MissionTextOverlay`; beide sind
ohne Renderer und Welt testbar. **Grenzen:** das Original aktualisiert je gezeichnetem Bild, hier je Logikschritt (beim Verblassen sichtbar, nicht bei der Schreibmaschine);
das Ergebnisbanner ist erfunden. **Seit dem 5. Oktober 2026 (dritte Sitzung) dem Original nachgezogen:** (1) der **Tippklick**: jedes getippte Zeichen (kein Zeilenumbruch) fragt
`MilitarySubtitlesTyping` beim Ton des Hosts an (`InGameUI::update`: `static AudioEventRTS click("MilitarySubtitlesTyping")`); der Bericht nennt `mission caption typing: N characters typed,
M clicks started (…)`, ohne Ton `M = 0` mit Grund. (2) die **Letterbox** nach `W3DDisplay::renderLetterBox`: zwei schwarze Rechtecke der Höhe `(Höhe − 9/16 · Breite) / 2` oben und unten
(800x600: 75 Pixel, 1024x768: 96, 1280x1024: 152; bei 16:9 und breiter **keine**, die Rechtecke lägen außerhalb des Bildes), ihr Alpha ist die Blendstufe (`LETTER_BOX_FADE_TIME` 1000 ms,
hier 30 Logikschritte der Präsentationsuhr), beim Ende blendet nur der **obere** Balken aus (der Aufruf für den unteren ist im Original auskommentiert, er verschwindet sofort);
Reihenfolge wie `W3DDisplay::draw`: Meldungen, Beschriftung und Zeitgeber (`InGameUI::postDraw`), darüber die Balken, darüber der Filmtext (`MissionPresentationDirector.LetterboxBars`,
`LetterboxFadeLevel`). Die Befehlsleiste verschwindet weiterhin sofort (`ScriptActions::doLetterBoxMode`: `HideControlBar`). (3) die **Meldungsliste** ist eine: die Zeilen des HUD
(`HudMessageFeed`, `docs/GAME-HUD.md` §8) und `DISPLAY_TEXT` teilen sich `UiMessageLog` (`UseMessages`). **Nicht belegt:** das Aussehen im Vergleich zum Original und im Bewegtbild (`docs/TEXT-AUDIT.md` §7 Punkt 8–12), das Meldungsfenster im echten Spiel
(keine Kampagnenkarte öffnet es in den ersten 4000 Bildern; `Training01` öffnet es erst später, bei einer Spielerhandlung), Filmtext (von keiner Karte benutzt).
**Zähler.** Jeder Ausgabetyp hat im Katalog (`MissionOutputCatalog`) genau einen von drei Zuständen: *dargestellt* (ein Verbraucher im Client wirkt dort, wo der Spieler es sieht, hört oder dadurch
eingeschränkt ist), *nicht dargestellt* (mit Grund, siehe unten) oder *anderswo erledigt* (die Simulation macht es selbst; nur `FREEZE_TIME`/`UNFREEZE_TIME`). Der Director zählt je Ausgabetyp `emitted`
(vom Skript ausgegeben), `forwarded` (an einen Verbraucher übergeben: Kamera, Overlay, Ton, Radar, Eingabe, Befehlsleiste/HUD, Objektschicht, Bildfilter) und `displayed` (der Verbraucher existiert in diesem
Host **und** der Typ hat einen Verbraucher: die Kamera wurde angewendet, das Overlay gezeichnet, die Sperre abgefragt, die Minimap hat die Punkte geholt, der Renderer hat die Filter abgefragt, eine
Befehlsleiste/Objektschicht ist angeschlossen; Ton zählt nur, wenn eine Stimme startete oder die Steuerung wirkte). Ein Headless-Lauf ohne Host meldet deshalb „weitergereicht“, aber nie „dargestellt“; ein
Typ ohne Verbraucher hat `displayed` 0, auch wenn ein Modell seinen Wert behält (`PLAYER_SCIENCE_AVAILABILITY`). `NotDisplayed = emitted − displayed − handledElsewhere`. Der Client schreibt die Zähler
beim Beenden und im Rauchtest auf die Konsole (`mission: …`, `mission outputs: … displayed …, handled elsewhere …, not displayed …`,
`mission output <TYP>: emitted …, forwarded …, displayed …, handled elsewhere …, not displayed … (wie/warum)`, `mission output problems: …`, `mission presentation: input locked for … ticks, locked at exit …`,
`mission screen filters: …`, dazu die Zeile `audio script control: …` des Audioberichts); `--mission-journal N` druckt zusätzlich die ersten N darstellbaren Ausgaben mit Tick.
`CampaignTool outputs <Inhalt> [--maps]` zählt statisch, welche weitergereichten Typen die 24 Kampagnenkarten verwenden und in welchem Zustand sie sind (Exitcode 1, wenn ein Typ im Katalog fehlt).

**Messung (5. Oktober 2026, zweite Sitzung; `--smoke-test --mission … --size 1280x720 --mute`, Release, feste Zeitschritte; frisch gemessen):**

| Karte | Bilder / VM-Schritte | Welt-Tick am Ende | Seite des Menschen | Typen | ausgegeben | weitergereicht | dargestellt | anderswo | nicht dargestellt | Eingabe gesperrt (Ticks) | am Ende gesperrt |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `GC_ChemGeneral` | 3000 / 3000 | 3000 | `ThePlayer` (FactionAmericaAirForceGeneral) | 14 | 29 | 29 | **29** | 0 | **0** | 750 | nein |
| `MD_GLA03` | 3000 / 3000 | 3000 | `ThePlayer` (FactionGLA) | 43 | 124 | 95 | **87** | 0 | **37** | 1750 | nein |
| `MD_USA01` | 3900 / 3900 | **3223** | `The_Player` (FactionAmerica) | 51 | 223 | 191 | **173** | **2** | **48** | 3132 | nein |

(Die Messung vom 4. Oktober, vor dieser Sitzung: dargestellt 17 / 35 / 72 und nicht dargestellt 12 / 89 / 151.) Fehlende Aktions- oder Bedingungstreffer: 0 auf allen drei Karten. Gemeldete
Probleme: `GC_ChemGeneral` keine; `MD_GLA03`: `ENABLE_OBJECT_SOUND` nennt ein Objekt, das es dann nicht gibt (×1); `MD_USA01`: `MUSIC_SET_TRACK 'Silence'` startet nicht (×1, die Stille gibt es nicht als
Stück), `PLAY_SOUND_EFFECT_AT 'Cin_CombatCycleMainLoop'` startet nicht (×1: dasselbe Skript hat das Ereignis bei Tick 75 mit `SOUND_DISABLE_TYPE` stummgeschaltet und erst bei Tick 555 per Override wieder laut gestellt), `UNIT_AFFECT_OBJECT_PANEL_FLAGS` „Indestructible“ ohne
Verbraucher (×2). `MD_USA01` zeigt die angehaltene Zeit: 3900 Bilder, aber Welt-Tick 3223 – 677 Schritte stand die Welt, während der Grauschleier und die Kamerafahrt der Spionageaufnahme liefen (der
Client meldet 677 durch den Filter gezeichnete Bilder). `GC_ChemGeneral` hat ab Tick 675 die Bewegungsunschärfe: 23 gefilterte Bilder, wie der Zählautomat vorgibt.

Bildprobe (`--filmstrip`, PNGs außerhalb des Repos angesehen): Grauschleier in `MD_USA01` (Rotkanal-Grau: Schnee und Gestein hell, Wasser/Vegetation dunkel – eine Spionagekamera-Anmutung, kein
Luminanzbild), Strahlenunschärfe auf das Hauptquartier in `GC_ChemGeneral` zum Zeitpunkt des Schnitts. **Nicht beurteilt:** ob die Unschärfe der des Originals gleicht (Schichtsumme statt Shader), wie das
Wackeln der Kamera im Bewegtbild wirkt (Einzelbilder zeigen eine Verschiebung von wenigen Pixeln nicht), und der Ton (kein Gehör). **Aufgefallen und nicht Gegenstand dieser Sitzung:** die erste Einstellung
von `MD_USA01` (Tick 0–400, `SETUP_CAMERA(CINE_CameraPos01, 0.2, -0.45, …)`) zeigt über dem Fels nur die Löschfarbe (Cornflower-Blau) statt des Bodens – ob die Kamera dort so tief/flach liegt, dass das
Gelände vor ihr nicht gezeichnet wird, oder ob es abgeschnitten wird, ist **nicht untersucht**; es ist keine der hier behandelten Ausgaben und gehört zu den Kamera-Befunden.

**Alle Ausgabetypen der 24 Kampagnenkarten** (`CampaignTool outputs <Inhalt> [--maps]`: statische Zählung der weitergereichten Aktionstypen in den Skripten, dazu der Zustand aus
`MissionOutputCatalog`; 92 Typen, 6.977 Verwendungen; **69 dargestellt, 2 von der Simulation erledigt, 21 nicht dargestellt**; vorher 35 mit und 57 ohne Verbraucher; 37 weitergereichte Typen
kommen in keiner Kampagnenkarte vor und stehen nur im Katalog):

**Mit Verbraucher im Client** (Zustand „dargestellt“; Verwendungen = Aktionen in den Skripten aller Kampagnenkarten, Karten = Karten mit mindestens einer Verwendung):

| `ScriptOutput`-Typ | Verbraucher | Verw. | Karten |
| --- | --- | ---: | --- |
| `COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT` | Befehlsleiste (`ControlBarScriptState`) | 7 | MD_CHI02, MD_CHI04, MD_GLA02, MD_USA05 |
| `COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE` | Befehlsleiste (`ControlBarScriptState`) | 148 | 14 von 24 |
| `CAMERA_BW_MODE_BEGIN` | Bildfilter (`PostEffects`, Stride `ScreenFilters`) | 5 | MD_USA01, MD_USA05 |
| `CAMERA_BW_MODE_END` | Bildfilter (`PostEffects`, Stride `ScreenFilters`) | 5 | MD_USA01, MD_USA05 |
| `CAMERA_MOTION_BLUR_JUMP` | Bildfilter (`PostEffects`, Stride `ScreenFilters`) | 12 | GC_AirGeneral, GC_ChemGeneral, GC_LaserGeneral, GC_Stealth, GC_SuperWeaponsGeneral, GC_TankGeneral, MD_USA05, Training01 |
| `DISABLE_INPUT` | Eingabesperre (`WorldInputEnabled`) | 139 | 24 von 24 |
| `ENABLE_INPUT` | Eingabesperre (`WorldInputEnabled`) | 35 | 24 von 24 |
| `OPTIONS_SET_DRAWICON_UI_MODE` | HUD (`IMissionHudControl`) | 65 | 16 von 24 |
| `UNIT_AFFECT_OBJECT_PANEL_FLAGS` | HUD (`IMissionHudControl`) | 32 | GC_LaserGeneral, MD_CHI01, MD_GLA02, MD_GLA03, MD_GLA04, MD_USA01, MD_USA03, MD_USA04 |
| `DISABLE_SPECIAL_POWER_DISPLAY` | HUD: Superwaffen-Liste (`SuperweaponTimers`) | 3 | GC_ChinaBoss, MD_GLA01, MD_GLA03 |
| `ENABLE_SPECIAL_POWER_DISPLAY` | HUD: Superwaffen-Liste (`SuperweaponTimers`) | 3 | GC_ChinaBoss, MD_GLA03 |
| `NAMED_HIDE_SPECIAL_POWER_DISPLAY` | HUD: Superwaffen-Liste (`SuperweaponTimers`) | 31 | GC_LaserGeneral, GC_NukeGeneral, GC_Stealth, GC_SuperWeaponsGeneral, MD_CHI01, MD_CHI04, MD_CHI05, MD_GLA03, MD_USA04 |
| `NAMED_SHOW_SPECIAL_POWER_DISPLAY` | HUD: Superwaffen-Liste (`SuperweaponTimers`) | 20 | GC_LaserGeneral, GC_NukeGeneral, GC_Stealth, GC_SuperWeaponsGeneral, MD_CHI05, MD_GLA03, MD_USA04 |
| `CAMERA_FOLLOW_NAMED` | Kamera (`ApplyCameraTo`) | 2 | MD_USA03 |
| `CAMERA_LOOK_TOWARD_OBJECT` | Kamera (`ApplyCameraTo`) | 9 | GC_NukeGeneral, MD_GLA01, Training01 |
| `CAMERA_LOOK_TOWARD_WAYPOINT` | Kamera (`ApplyCameraTo`) | 11 | GC_ChinaBoss, MD_CHI02, MD_CHI04, MD_GLA02, MD_GLA03, MD_GLA04, MD_USA01, MD_USA02, MD_USA03, MD_USA05 |
| `CAMERA_MOD_FINAL_LOOK_TOWARD` | Kamera (`ApplyCameraTo`) | 3 | MD_GLA05, Training01 |
| `CAMERA_MOD_LOOK_TOWARD` | Kamera (`ApplyCameraTo`) | 82 | 14 von 24 |
| `CAMERA_MOD_SET_FINAL_PITCH` | Kamera (`ApplyCameraTo`) | 15 | GC_NukeGeneral, MD_CHI01, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA03, Training01 |
| `CAMERA_MOD_SET_FINAL_ZOOM` | Kamera (`ApplyCameraTo`) | 14 | MD_CHI01, MD_CHI03, MD_CHI05, MD_GLA03, MD_GLA04, MD_USA01, MD_USA03, Training01 |
| `CAMERA_STOP_FOLLOW` | Kamera (`ApplyCameraTo`) | 4 | MD_USA03 |
| `CAMERA_STOP_TETHER_NAMED` | Kamera (`ApplyCameraTo`) | 2 | Training01 |
| `CAMERA_TETHER_NAMED` | Kamera (`ApplyCameraTo`) | 2 | Training01 |
| `MOVE_CAMERA_ALONG_WAYPOINT_PATH` | Kamera (`ApplyCameraTo`) | 1 | MD_GLA03 |
| `MOVE_CAMERA_TO` | Kamera (`ApplyCameraTo`) | 152 | 23 von 24 |
| `PITCH_CAMERA` | Kamera (`ApplyCameraTo`) | 60 | MD_CHI02, MD_CHI04, MD_CHI05, MD_GLA01, MD_GLA03, MD_GLA05, MD_USA01, MD_USA02, MD_USA03, MD_USA04, MD_USA05, Training01 |
| `RESET_CAMERA` | Kamera (`ApplyCameraTo`) | 25 | 17 von 24 |
| `ROTATE_CAMERA` | Kamera (`ApplyCameraTo`) | 54 | 23 von 24 |
| `SETUP_CAMERA` | Kamera (`ApplyCameraTo`) | 152 | 23 von 24 |
| `ZOOM_CAMERA` | Kamera (`ApplyCameraTo`) | 91 | 17 von 24 |
| `MAP_SWITCH_BORDER` | Kamera: Kartenrand (`ActiveBoundary`) | 31 | GC_ChinaBoss, MD_CHI01, MD_CHI04, MD_CHI05, MD_GLA01, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05, Training01 |
| `SCREEN_SHAKE` | Kamera: Wackeln (`RtsCamera.Shake`) | 96 | MD_CHI01, MD_GLA01, MD_GLA04, MD_USA01, MD_USA03, MD_USA05 |
| `OBJECT_CREATE_RADAR_EVENT` | Minimap (blinkender Punkt) | 152 | GC_ChinaBoss, MD_CHI02, MD_CHI03, MD_CHI05, MD_GLA01, MD_GLA02, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `RADAR_CREATE_EVENT` | Minimap (blinkender Punkt) | 88 | MD_CHI02, MD_CHI04, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA02, MD_USA03, Training01 |
| `TEAM_CREATE_RADAR_EVENT` | Minimap (blinkender Punkt) | 11 | MD_CHI05, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA03, MD_USA04 |
| `NAMED_CUSTOM_COLOR` | Objektschicht (`FrameProjector`) | 198 | GC_LaserGeneral, MD_CHI05, MD_GLA01, MD_GLA03, MD_GLA04, MD_GLA05 |
| `NAMED_FLASH` | Objektschicht (`FrameProjector`) | 76 | MD_CHI01, MD_CHI02, MD_GLA02, MD_GLA03, MD_USA04, MD_USA05, Training01 |
| `NAMED_FLASH_WHITE` | Objektschicht (`FrameProjector`) | 32 | MD_GLA01, MD_GLA04, MD_GLA05, MD_USA01, MD_USA04, MD_USA05 |
| `TEAM_FLASH` | Objektschicht (`FrameProjector`) | 3 | MD_CHI01, MD_GLA05 |
| `TEAM_FLASH_WHITE` | Objektschicht (`FrameProjector`) | 1 | MD_GLA04 |
| `CAMERA_FADE_ADD` | Overlay (Balken, Blende) | 14 | GC_ChinaBoss, GC_LaserGeneral, MD_CHI01, MD_GLA01, MD_GLA03, MD_USA01, MD_USA05, Training01 |
| `CAMERA_FADE_MULTIPLY` | Overlay (Balken, Blende) | 166 | 24 von 24 |
| `CAMERA_LETTERBOX_BEGIN` | Overlay (Balken, Blende) | 144 | 24 von 24 |
| `CAMERA_LETTERBOX_END` | Overlay (Balken, Blende) | 37 | 24 von 24 |
| `DISABLE_COUNTDOWN_TIMER_DISPLAY` | Overlay (Text) | 64 | 20 von 24 |
| `DISPLAY_COUNTDOWN_TIMER` | Overlay (Text) | 3 | MD_CHI02, MD_GLA05, MD_USA05 |
| `DISPLAY_COUNTER` | Overlay (Text) | 3 | MD_CHI03, MD_GLA04 |
| `HIDE_COUNTDOWN_TIMER` | Overlay (Text) | 4 | MD_CHI02, MD_GLA05, MD_USA05 |
| `HIDE_COUNTER` | Overlay (Text) | 7 | MD_CHI03, MD_GLA04, MD_GLA05 |
| `INGAME_POPUP_MESSAGE` | Overlay (Text) | 14 | Training01 |
| `SHOW_MILITARY_CAPTION` | Overlay (Text) | 269 | 17 von 24 |
| `MUSIC_SET_VOLUME` | Ton: Busfaktor (`SetScriptVolume`) | 24 | GC_ChemGeneral, GC_LaserGeneral, GC_Stealth, GC_SuperWeaponsGeneral, GC_TankGeneral, MD_CHI05, MD_GLA02, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `SOUND_SET_VOLUME` | Ton: Busfaktor (`SetScriptVolume`) | 281 | 23 von 24 |
| `SPEECH_SET_VOLUME` | Ton: Busfaktor (`SetScriptVolume`) | 188 | GC_AirGeneral, GC_NukeGeneral, GC_TankGeneral, MD_CHI02, MD_GLA01, MD_GLA03, MD_GLA05, MD_USA03 |
| `AUDIO_OVERRIDE_VOLUME_TYPE` | Ton: Schalter (`AudioDirector.Script`) | 63 | GC_LaserGeneral, MD_CHI01, MD_CHI04, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `AUDIO_RESTORE_VOLUME_ALL_TYPE` | Ton: Schalter (`AudioDirector.Script`) | 21 | 15 von 24 |
| `DISABLE_OBJECT_SOUND` | Ton: Schalter (`AudioDirector.Script`) | 31 | MD_CHI01, MD_CHI03, MD_CHI05, MD_GLA01, MD_GLA03, MD_GLA05, MD_USA01, MD_USA03 |
| `ENABLE_OBJECT_SOUND` | Ton: Schalter (`AudioDirector.Script`) | 64 | MD_CHI02, MD_CHI03, MD_CHI05, MD_GLA01, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA03 |
| `EVA_SET_ENABLED_DISABLED` | Ton: Schalter (`AudioDirector.Script`) | 68 | 17 von 24 |
| `SOUND_DISABLE_TYPE` | Ton: Schalter (`AudioDirector.Script`) | 300 | 14 von 24 |
| `SOUND_ENABLE_ALL` | Ton: Schalter (`AudioDirector.Script`) | 20 | GC_ChinaBoss, GC_NukeGeneral, MD_CHI01, MD_CHI02, MD_CHI03, MD_CHI04, MD_GLA02, MD_GLA03, MD_GLA04, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `SOUND_ENABLE_TYPE` | Ton: Schalter (`AudioDirector.Script`) | 21 | MD_CHI02, MD_USA01, MD_USA03 |
| `SOUND_REMOVE_ALL_DISABLED` | Ton: Schalter (`AudioDirector.Script`) | 17 | MD_CHI01, MD_CHI04, MD_CHI05, MD_GLA02, MD_GLA03, MD_USA01, MD_USA03, MD_USA04, MD_USA05 |
| `SOUND_REMOVE_TYPE` | Ton: Schalter (`AudioDirector.Script`) | 3 | MD_USA02 |
| `MUSIC_SET_TRACK` | Ton: Wiedergabe (`GameAudio.PlayScripted`) | 710 | 24 von 24 |
| `PLAY_SOUND_EFFECT` | Ton: Wiedergabe (`GameAudio.PlayScripted`) | 120 | 18 von 24 |
| `PLAY_SOUND_EFFECT_AT` | Ton: Wiedergabe (`GameAudio.PlayScripted`) | 69 | GC_AirGeneral, MD_CHI02, MD_CHI04, MD_GLA03, MD_USA01, MD_USA03, MD_USA05 |
| `SOUND_PLAY_NAMED` | Ton: Wiedergabe (`GameAudio.PlayScripted`) | 1 | MD_GLA02 |
| `SPEECH_PLAY` | Ton: Wiedergabe (`GameAudio.PlayScripted`) | 1668 | 24 von 24 |

**Von der Simulation erledigt** (Zustand „anderswo“):

| `ScriptOutput`-Typ | Verw. | Karten |
| --- | ---: | --- |
| `FREEZE_TIME` | 3 | MD_USA01, Training01 |
| `UNFREEZE_TIME` | 3 | MD_USA01, Training01 |

**Nicht dargestellt** (Zustand „nicht dargestellt“, Grund in der Liste darunter):

| `ScriptOutput`-Typ | Verw. | Karten |
| --- | ---: | --- |
| `CAMEO_FLASH` | 4 | MD_GLA02 |
| `CAMERA_ADD_SHAKER_AT` | 2 | MD_GLA01, MD_GLA02 |
| `CAMERA_MOD_FREEZE_ANGLE` | 1 | GC_ChinaBoss |
| `DEBUG_MESSAGE_BOX` | 3 | GC_LaserGeneral, MD_GLA03, MD_GLA04 |
| `DISABLE_BORDER_SHROUD` | 27 | 14 von 24 |
| `DISABLE_SCORING` | 24 | 15 von 24 |
| `DRAW_SKYBOX_BEGIN` | 24 | 14 von 24 |
| `DRAW_SKYBOX_END` | 19 | MD_CHI02, MD_CHI04, MD_CHI05, MD_GLA01, MD_GLA02, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA03, MD_USA04, MD_USA05, Training01 |
| `ENABLE_BORDER_SHROUD` | 19 | MD_CHI01, MD_CHI02, MD_CHI03, MD_CHI05, MD_GLA01, MD_GLA02, MD_GLA03, MD_GLA05, MD_USA01, MD_USA02, MD_USA03, MD_USA04, MD_USA05 |
| `ENABLE_SCORING` | 20 | 15 von 24 |
| `OPTIONS_SET_OCCLUSION_MODE` | 73 | 18 von 24 |
| `OPTIONS_SET_PARTICLE_CAP_MODE` | 70 | 18 von 24 |
| `OVERSIZE_TERRAIN` | 52 | 15 von 24 |
| `PLAYER_EXCLUDE_FROM_SCORE_SCREEN` | 60 | MD_CHI01, MD_CHI02, MD_CHI04, MD_GLA03, MD_GLA04, MD_GLA05, MD_USA01, MD_USA02, MD_USA03, Training01 |
| `PLAYER_SCIENCE_AVAILABILITY` | 201 | MD_CHI01, MD_CHI02, MD_CHI03, MD_CHI04, MD_GLA01, MD_GLA02, MD_GLA03, MD_GLA04, MD_USA01, MD_USA02, MD_USA03, MD_USA04, Training01 |
| `RADAR_ENABLE` | 2 | MD_GLA04, Training01 |
| `RADAR_FORCE_ENABLE` | 11 | MD_CHI01, MD_CHI02, MD_CHI04, MD_GLA01, MD_GLA03, MD_GLA04, MD_USA02, MD_USA03, MD_USA04 |
| `RESIZE_VIEW_GUARDBAND` | 35 | MD_CHI02, MD_CHI04, MD_CHI05, MD_GLA01, MD_GLA02, MD_GLA03, MD_USA01, MD_USA02, MD_USA03, MD_USA04, MD_USA05 |
| `SET_FPS_LIMIT` | 49 | 15 von 24 |
| `SHOW_WEATHER` | 7 | MD_CHI05, MD_USA03 |
| `WATER_CHANGE_HEIGHT_OVER_TIME` | 2 | MD_CHI04, MD_GLA04 |

**Gründe für „nicht dargestellt“** (je Typ steht der Wortlaut in `MissionOutputCatalog.cs`):

- **Wertung** (`ENABLE_SCORING`, `DISABLE_SCORING`, `PLAYER_EXCLUDE_FROM_SCORE_SCREEN`): der Missions-Ergebnisbildschirm (`MissionResultScreen`) zeigt eine Zeile ohne Zahlen. Das Original zählt bei
  ausgeschalteter Wertung nichts mit (`ScoreKeeper` fragt `isScoringEnabled()`) und listet Seiten mit `setListInScoreScreen(false)` nicht; der Director behält beides (`ScoringEnabled`,
  `ScoreScreenExcluded`), ein Statistikbildschirm läse es dort.
- **Wissenschaften** (`PLAYER_SCIENCE_AVAILABILITY`, 201 Verwendungen in 13 Karten): die Befehlsleiste kennt die Verfügbarkeit (`ControlBarScriptState`; „Hidden“ = der `PurchaseScience`-Knopf fehlt,
  „Disabled“ = gesperrt; Test gegen die Retaildaten), aber der Client hat kein Generals-Powers-Fenster, in dem man Wissenschaften kauft – es gibt also nichts zu sehen. Gilt nur für die lokale Seite;
  andere Seiten kaufen über die KI/Simulation, die das nicht liest (im Bericht als Problem „availability of a side other than the local player“).
- **Radar** (`RADAR_ENABLE`, `RADAR_FORCE_ENABLE`, …): der Client kennt keine Radaranlagenregel, die Minimap ist immer an; es gibt nichts zu erzwingen.
- **Himmel und Rand** (`DRAW_SKYBOX_*`, `ENABLE_/DISABLE_BORDER_SHROUD`, `OVERSIZE_TERRAIN`, `RESIZE_VIEW_GUARDBAND`): der Himmelskörper `new_skybox` wird von keiner Schicht gezeichnet (der Himmel
  über dem Horizont ist die Löschfarbe); die Fog-Textur deckt nur die spielbare Fläche, das Gelände dahinter nimmt ihren Randwert, einen eigenen Schattenring für die aktive Grenze gibt es nicht;
  die Geländeschicht zeichnet das ganze Höhenfeld samt Rand (nichts zu „übergrößern“); der Client schneidet keine Drawables an einem Guard-Band ab. Der Director behält `SkyboxRequested` und
  `BorderShroudEnabled`.
- **Render-Optionen** (`OPTIONS_SET_OCCLUSION_MODE`, `OPTIONS_SET_PARTICLE_CAP_MODE`, `SET_FPS_LIMIT`): Optionen des Originals (Marker für Einheiten hinter Gebäuden, dynamische Partikelgrenze,
  Bildratenbegrenzung), die der Client nicht hat oder nicht von Skripten steuern lässt.
- **Kamera und Welt** (`CAMERA_ADD_SHAKER_AT`: der Dreh-Wackler braucht einen Kamerarollversatz, den `RtsCamera` nicht hat; `CAMERA_MOD_FREEZE_ANGLE`; `WATER_CHANGE_HEIGHT_OVER_TIME`: die Wasserebene
  hat eine feste Höhe, das Original verschiebt auch den logischen Wasserstand; `SHOW_WEATHER`: weder Schnee noch Regen werden gezeichnet; `CAMEO_FLASH`: kein Blinken von Knopfbildern; `DEBUG_MESSAGE_BOX`).

Tests: `tests/CnCNew.Game.Tests/MissionEffectsTests.cs` (Katalog vollständig; Skriptausgaben → Ton, Objekte, HUD mit Attrappen, „dargestellt braucht den Verbraucher“; die Uhr läuft bei angehaltener Zeit;
Wackeln; Kartenrand; die Zählautomaten von Grauschleier und Unschärfe; mit `--content-dir`: `MD_USA01` 3900 Bilder durch das echte HUD und die echte Kamera, entfernte und wieder eingesetzte Befehlsknöpfe
in der Leiste eines gewählten Objekts, Wissenschaften und Auswahl, Hausfarbe und Blitzen im projizierten Bild), `AudioDirectorTests` (Skriptsteuerung am gerenderten Ausgang), `Audio.Tests`
(`SetVoiceVolume`), `Presentation.Tests` (`SelectionFlashTracker.StartScripted`), `Simulation.Tests` (angehaltene Zeit, Speichern/Laden eingefroren), sowie
`tests/CnCNew.Game.Tests/MissionPresentationTests.cs` (Letterbox, Sperre, Text, Zähler, Zeitverlauf einer Kamerabewegung, „dargestellt braucht einen Host“) und `MissionHudTests.cs` (echte
Karte `GC_ChemGeneral`: Sperre und Letterbox der Eröffnung, verworfene Klicks, Rückgabe nach rund 25 s, Kamera am Ende über der eigenen Basis, Ergebnisbanner); `scripts/check-dozer-build.ps1`
prüft dasselbe im echten Client (`docs/GAME-HUD.md` §17).

**Fund beim Messen (erste Sitzung):** `MD_GLA03` beendete den Client im ersten Frame mit einer unbehandelten `InvalidDataException` – das Modell `cineexplbox` enthält nur eine Kollisionsbox und kein Mesh.
`ModelAssetCache` behandelt ein nicht verwendbares Modell jetzt wie ein fehlendes (einmal gemeldet, gezählt, nichts gezeichnet).

**Weiter offen (Sprache und Kamera):** keine Klanglänge (die Untertitel gibt es, das Englische verbirgt sie fast alle) – `HAS_FINISHED_AUDIO` gilt nach 3 s, längere Ansagen überlappen die nächste; Kamerapfade entlang der
Wegpunktverbindungen, die Ease-Zeiten des Skripts; `CAMERA_MOD_FREEZE_TIME`, `CAMERA_MOD_SET_FINAL_SPEED_MULTIPLIER`, `CAMERA_MOD_SET_ROLLING_AVERAGE`, `CAMERA_SET_DEFAULT`,
`CAMERA_ENABLE_/DISABLE_SLAVE_MODE`, `CAMERA_MOVE_HOME`, `MOVE_CAMERA_TO_SELECTION` (von keiner Kampagnenkarte benutzt); die Zustände der Skriptänderungen (Befehlsleiste, Hausfarben, Auswahlsperre,
Lautstärken) sind Hostzustand und gehen beim Speichern/Laden einer Mission **nicht** mit (`MissionSnapshot` kennt nur die Simulation und die Skript-VM): nach dem Laden gilt die Befehlsleiste wieder
unverändert, bis das Skript den Zustand erneut setzt.

## 8. Abweichungen und Näherungen (vollständig)

1. **Bauanlage** (`ThingTemplate::isBuildFacility`): im Original aus den Voraussetzungen anderer Vorlagen berechnet; hier: Struktur mit
   `ProductionUpdate` oder `DozerAIUpdate`.
2. **Gebäudezählung** (`Player::countBuildings`): lebende `STRUCTURE`-Objekte (Fraktionszählung: zusätzlich `MP_COUNT_FOR_VICTORY`).
3. **Erste Auswertung verzögerter Skripte:** das Original zieht `GameLogicRandomValue(0, 2 s)`; hier ein aus dem Skriptnamen abgeleiteter
   Versatz im selben Bereich (verbraucht keinen Zufallsstrom, den eine wiederhergestellte Welt nicht wiederholen würde).
4. **Angreifer-Historie** liest Besitzer/Typ nach dem Weltschritt; ein im selben Tick gestorbener Angreifer wird mit unbekanntem
   Besitzer gespeichert (das Original speichert die Spielermaske zum Schadenszeitpunkt).
5. **Befehle als Bewegung:** `*_HUNT`, `*_ATTACK_*`, `*_GUARD_*` und Pfadfolge bewegen die Einheit; Schießen übernimmt das automatische
   Gefecht der Sitzung. `*_STOP`/`*_GUARD` (ohne Ziel) halten an. Formationen und Teamgeschwindigkeit (`asTeam`) gibt es seit Phase 13 (Punkt 23); eine Angriffsbewegung
   mit Zwischenzielen gibt es nicht.
6. **Verstärkung** (`CREATE_REINFORCEMENT_TEAM`, `ScriptEngine.Reinforcements.cs`, Phase 13): ohne `teamTransport` erscheinen die Einheiten des Teams (je `teamUnitMaxCount`) am Ursprung (oder am Ziel) und
   laufen zum Ziel; mit `teamTransport` entstehen die Transporter am Ursprung, das Team wird eingeladen (`teamStartsFull`: schon beim Spawn; sonst steigt es am Ziel zu), `DeliverPayloadAIUpdate` fliegt
   den Anflug und setzt ab (Landung oder Fallschirm über `ParachuteContain`), und `teamTransportsExit` schickt den Transporter danach von der Karte. Fallschirmspringer landen auf dem Boden unter dem
   Absetzpunkt; die Reste des Originals (Formationsversatz beim Absetzen, Chinook-Landung mit Rampe) sind nicht nachgebildet.
7. **Hüllenmodule** `CommandButtonHuntUpdate` (seit Phase 12 durch `TEAM_HUNT_WITH_COMMAND_BUTTON` ausgelöst und ausgeführt, §5) und `LockWeaponCreate` (Primär-Slot;
   andere Slots: nicht unterstützte Konfiguration): das Objekt kann existieren. `SpecialAbilityUpdate` ist seit dieser Phase ebenfalls eine Hülle:
   das Modul trägt nur den geteilten Nachlademechanismus jeder Spezialkraft (`SpecialPowerModuleBase`); die eigentliche Anreise-/Verpackungs-/
   Vorbereitungs-/Fähigkeitsobjekt-Inszenierung des Originals (`SpecialAbilityUpdate.cpp`: Ranger-Fähigkeiten, Colonel Burton, Jarmen Kell, der
   GLA-Rebellin Gebäudeeroberung/Fallenlegen, der Hacker-Geldraub) ist nicht simuliert — `NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY*` lösen die Kraft aus
   und starten ihr Nachladen (ein beobachtbarer, echter Effekt: Bereitschaft, Pausen, geteilte Zeitgeber), der inszenierte Effekt selbst bleibt aus.
8. **Befehlsknöpfe global aufgelöst:** `NAMED_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` sucht im Original im `CommandSet` des Trägers, `TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` in der globalen Schaltflächenregistrierung (`TheControlBar->findCommandButton`); da Schaltflächennamen im
   Originalbestand eindeutig sind, löst `CommandCatalog.TryGetSpecialPowerAbility` beide Varianten gleich auf (eine globale Zuordnung
   Schaltflächenname → `SpecialPowerTemplate`, nur für `Command = SPECIAL_POWER*`-Knöpfe). Die Wegpunkt-Varianten
   (`*_AT_WAYPOINT`, `*_USING_WAYPOINT_PATH`) sind seit Phase 11 ausgeführt (§5, §8 Punkt 13); die `TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_*`-/`TEAM_PARTIAL_USE_COMMANDBUTTON`-Familie ist seit Phase 12 ausgeführt (§5).
9. **Mission ohne Replay** (§3).
10. **Lokale Seite ohne `playerIsHuman`:** `MissionOptions.HumanSideName` überschreibt den Standard, siehe §7.
11. Karten- und Kampagnen-Anforderungen, die das Original aus dem Spiellauf bezieht (Schwierigkeit pro Spieler, Kamera-/Film-Zeiten,
   Fortschrittsprofil), kommen aus `MissionOptions`/Host.

12. **`getTeamNamed`:** die Aufrufer-Instanz eines Teams (Skript-/Sequenzkontext) hat Vorrang vor anderen Instanzen desselben Prototyps (`FindTeam`/`TeamsNamed` tun das). Seit Phase 13 gilt auch die übrige Regel
    des Originals: ein **nicht aktives** Singleton-Team (`ScriptTeam.Created` = `isActive`) ist `NULL` – `TEAM_DESTROYED`, Mitgliederabfragen und Aktionen sehen es nicht; Singletons werden beim Laden ohne
    Startaktionen vorab angelegt und erst durch Aktivierung (`TEAM_CREATED`-Ereignis) sichtbar.
13. **Spezialkräfte:** `NAMED_FIRE_SPECIAL_POWER_AT_*` und die Wegpunkt-Fähigkeiten gehen als gewöhnlicher `UseSpecialPowerCommand` durch die üblichen Bereitschafts-, Wissenschafts- und
    Zielprüfungen (das Original ruft `doSpecialPowerAtLocation` am Modul und prüft die Bereitschaft nicht); `…_USING_WAYPOINT_PATH` nutzt nur den nächsten Pfadpunkt als Ort.
    `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST` sucht den Ort unter den Positionen feindlicher Objekte (Summe `BuildCost` im Wirkradius, mindestens 50) statt auf dem 10×10-Raster des Originals
    in zufälliger Richtung und nimmt alle Feinde des Spielers statt `getSkirmishEnemyPlayer`.
14. **Garnison:** `MayGarrison` bildet `isKindOf(STRUCTURE) && player == 0 || player == eigener` mit „zuletzt eingetreten“ ab; `…NEAREST_BUILDING` sortiert nach Abstand zum Anführer/zur Einheit
    (Internet-Zentren nur für Geldhacker); `PLAYER_GARRISON_ALL_BUILDINGS` zählt freie Plätze über den Auftrag, das Original prüft nur den Stand beim Befehl.
15. **`BUILD_TEAM`** (Phase 13: `ScriptEngine.TeamQueues.cs`, nach `AIPlayer::buildSpecificAITeam`/`checkQueuedTeams`/`checkReadyTeams`): jede fertige Fabrik des Teambesitzers, die die Einheit herstellen darf
    (`CanQueueProductionTemplate`), ein Befehl je Tick je Auftrag, bevorzugt eine untätige; das Team steht in der **Bauwarteschlange** und ist unsichtbar (`Created` falsch). Eine gebaute Einheit tritt über
    `EntitySpawned` bei und läuft zum Heimatort (`aiFollowExitProductionPath`). Sind alle Typen fertig, geht das Team in die **Bereitschaftswarteschlange**; laufen `teamInitialIdleFrames` ab (0: nie), ohne dass
    das Minimum gebaut oder in Produktion ist, wird das Team aufgelöst. Solange ein Mitglied untätig ist, führt ein Team mit `teamExecutesActionsOnCreate` die Wahr-Aktionen seines
    `teamProductionCondition`-Skripts aus (`<This Team>` = das Team). Weiterhin ohne Baulistenzuordnung und Priorität/Vordrängeln.
16. **`RECRUIT_TEAM`:** wie `Team::tryToRecruit` (Priorität, `teamIsAIRecruitable`, `TEAM_AVAILABLE_FOR_RECRUITMENT`, Standardteam = `team<Spielername>` immer rekrutierbar); seit Phase 13 beginnt das Team in der
    Bereitschaftswarteschlange: die Mitglieder gehen zum Heimatort, aktiv wird es, wenn alle untätig sind, nach 60 Sekunden oder – mit Startaktionen – sobald ein Mitglied untätig ist. „Bauvarianten“ und gehaltene
    Einheiten sind nicht berücksichtigt; ohne `teamHome` wird nichts rekrutiert.
17. **Aufgezeichnet statt ausgeführt:** `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, `OBJECT_ALLOW_BONUSES` und seit Phase 12 `TEAM_SET_ATTITUDE`, `TEAM_WANDER(_IN_PLACE)`, `PLAYER_SET_RANKLEVELLIMIT`,
    `PLAYER_SET_RANKLEVEL`, `PLAYER_ADD_RANKLEVEL`, `SCRIPTING_OVERRIDE_HULK_LIFETIME` (§5; Lesezugriffe `AttitudeOf`, `RankLevelOf`, `RankLimit`, `HulkLifetimeOverrideTicks`, `SkillPointsModifierOf`). Wer sie später
    auswertet, führt einen Verbraucher ein (KI-Haltung, Rangsystem, Wrackliste, Erfahrungsgewinn); bis dahin zählen sie weder als ausgeführt noch als fehlend.
18. **Knopf-Bereitschaft** (`SKIRMISH_COMMAND_BUTTON_READY_*`, `SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_*`): Spezialkraft-Knöpfe über die Kraft; seit Phase 13 auch Upgrade-Knöpfe (`PLAYER_UPGRADE`/`OBJECT_UPGRADE`),
    deren Bereitschaft `IUpgradeModule.WouldUpgrade` der Module des Trägers meldet (Original: `isReady` → `canUpgrade`); andere Knopftypen sind nicht bereit.
19. **Überschreibungs-Beziehungen** (Phase 12): die Tabellen (Team→Team, Team→Spieler, Spieler→Team) gehören dem `ScriptEngine`, die Welt fragt sie über `RelationshipOverride`; die Teamzuordnung eines Objekts wird je Tick
    und Mitgliederzahl zwischengespeichert (`TeamOfEntity`, erstes Team nach Id gewinnt bei Mehrfachzugehörigkeit). Das Original hängt die Überschreibung an das `Team`-Objekt, das ein Objekt nur einmal besitzt.
20. **Befehlsknopf-Familie** (Phase 12): `…ON_NEAREST_*` nimmt als Mitte den ganzzahligen Mittelwert der lebenden Mitglieder, prüft die acht nächsten Kandidaten der Reihe nach, bis die Welt den Knopf für den ersten Träger
    annimmt, und lässt dann alle Träger denselben Knopf auf dasselbe Ziel benutzen (das Original wählt ein Ziel je Träger über `PartitionManager::getClosestObject` mit eigenen Filtern); die Jagd mit Spezialkraft-Knopf sucht
    alle `max(ScanRate, 1 s)` Ticks den nächsten Feind in `ScanRange` für einen untätigen Jäger (`CommandButtonHuntUpdate::scanClosestTarget`), die Jagd mit Waffenknopf ist `TEAM_HUNT`; Enter-Knöpfe (Entführung,
    Autobombe, Sabotage) und alle anderen Befehlsarten werden wie im Original abgelehnt und als Diagnose gezählt.
21. **Transport** (Phase 12): `TEAM_LOAD_TRANSPORTS` verteilt Passagiere nach `TransportSlotCount` per First-Fit (größte zuerst) statt der Packungsoptimierung von `doLoadAllTransports`; es gibt weiterhin keinen
    Verstärkungstransport (Punkt 6): `CINE_*`-Flugzeuge der Kampagnenkarten sind nicht instanziierbar und die Chinook-Lieferung von `CreateReinforcementTeam` fehlt.
22. **Kleinzustand** (Phase 12): `TEAM_PANIC` läuft den Pfad ab dem nächsten Wegpunkt, ohne den Panik-Locomotor-Satz; `NAMED/TEAM_FACE_WAYPOINT` setzt die Blickrichtung sofort statt mit Drehrate;
    `NAMED/TEAM_SET_UNMANNED_STATUS` überträgt das Objekt an den neutralen Spieler (kein `DISABLED_UNMANNED`-Zustand), `DELETE_ALL_UNMANNED` entfernt es danach; `NAMED/TEAM_SET_STEALTH_ENABLED` setzt einen Schalter am
    `StealthUpdate` (`ScriptUnstealthed`, nur gespeichert, wenn gesetzt); `TEAM_INCREASE/DECREASE_PRIORITY` ändert die Priorität nur für `RECRUIT_TEAM`, `BUILD_TEAM` liest sie nicht (Punkt 15);
    `TEAM_GUARD_IN_TUNNEL_NETWORK` geht in den nächsten eigenen Tunnel und betritt ihn (ohne Wachzustand im Netz).
23. **Offene VM-Semantik (Stand Phase 14):** erledigt sind `getTeamNamed` für nicht aktive Singleton-Teams (Punkt 12), Team-Bau mit Warteschlange und Startskript (Punkt 15), Rekrutierung mit Sammeln am Heimatort
    (Punkt 16), die Bereitschaft von Upgrade-Knöpfen (Punkt 18), **Formationen** (`TEAM_FOLLOW_WAYPOINTS(_EXACT)` mit `asTeam`: Gruppenversatz, gemeinsamer Teamwegpunkt, Geschwindigkeit des langsamsten Mitglieds via
    `AIUpdateInterface.SpeedCap`), der **Pfadflug** von `NAMED_USE_COMMANDBUTTON_ABILITY_USING_WAYPOINT_PATH` für den Partikelstrahl (`ParticleUplinkCannonUpdate.PrepareScriptedPath`), `FIRE_WEAPON` mit `WeaponSlot`
    (Waffenslot-Modell, `docs/SIMULATION-CORE.md` §30.6; der Befehlsknopf-Slot wird gesperrt) und „untätig“ unter Einbezug des Zustands der KI (`IsIdle`: auch kein eigener Auftrag der Skript-KI, Flugzeuge über
    `JetAIUpdate.IsIdle` noch nicht). Bewusst offen: ein vollständiger Mannschafts-Zustandsautomat des Originals (§5a), der Pfadflug für andere Flugkörper als den Strahl und die neun fehlenden Aktionen (§5).
    Kein Zielfahrer-Blocker hängt an ihnen (§9).

## 9. Headless-Prüfung (Messung)

Werkzeuge: `CnCNew.CampaignTool audit|list|playthrough` (`docs/CAMPAIGN-AND-SAVEGAMES.md` §6) und `SimTool mission-run`.
Je Mission: zwei frische Läufe (Hash-Kette identisch), Speichern/Wiederherstellen nach der Hälfte (identisch), **Endprobe**
(`MissionProbe`): nach 150 Ticks werden alle Objekte der Feinde (Sieg) bzw. des Betrachters und seiner Verbündeten (Niederlage)
zerstört und 600 Ticks auf das Skriptergebnis gewartet; ein Ergebnis, das die Mission vor der Probe selbst erreicht, zählt nicht.

**`CampaignTool reachability`** (statisch, Phase 11): für jedes Skript, das `VICTORY`/`QUICKVICTORY` bzw. `DEFEAT` ausführt, werden die Voraussetzungen der Kette gesammelt – Bedingungen
(`FLAG`, `COUNTER`, `TIMER_EXPIRED` brauchen ein Skript, das den Wert setzt; alle anderen Bedingungstypen sind Blätter), Freischaltung (`ENABLE_SCRIPT`/`CALL_SUBROUTINE`, aktiv von Anfang an oder als
Teamereignisskript vom Motor gerufen) und die Skripte, die darin vorkommen – und gegen `ScriptCoverage` geprüft. Ergebnis je Endskript: **sauber** (alle Typen auf der besten Kette implementiert), **eingeschränkt**
(feuert, aber ein Skript der Kette führt eine unimplementierte Aktion aus) oder **blockiert** (eine Bedingung der Kette ist nicht implementiert oder liest etwas, das niemand setzt). Das ist eine obere Schranke dessen, was die VM
erreichen kann, **kein** Beweis, dass ein Spieler gewinnen kann: Zustandsbedingungen (Team zerstört, Objekt tot, Bereich) gelten als erfüllbar. `CampaignTool script --map … --name …` zeigt ein Skript und alle, die es nennen.

**Stand nach Phase 12 (2. Oktober 2026, Release, 900 Ticks je Lauf, frisch gemessen; Vergleich mit dem Stand am Ende von Phase 11):**

| Messgröße | Ende Phase 11 | Ende Phase 12 |
| --- | --- | --- |
| `ScriptCoverage` Bedingungen ausgeführt / fehlend | 80 / 29 | 80 / 29 (unverändert) |
| `ScriptCoverage` Aktionen ausgeführt / weitergereicht / aufgezeichnet / fehlend | 131 / 129 / 2 / 82 | 160 / 129 / 10 / 45 (Phase 13: **162 / 129 / 13 / 40**) |
| Skripte ausgeführt (24 Karten) | 2.403 | 2.403 |
| Aktionen ausgeführt / weitergereicht | 7.775 / 2.453 | 7.821 / 2.452 |
| Treffer auf fehlende Aktionstypen (Anteil der Ausführungen) | 116 (1,1 %) | **27 (0,3 %)** |
| Treffer auf fehlende Bedingungstypen | 6.411 | 6.411 |
| Endproben durch Ausschalten: Sieg / Niederlage | 10/24 / 15/24 | **13/24** / 15/24 |
| **Zielfahrer auf dem Skriptweg: Sieg / Niederlage** (Standard 12.000 Ticks; Phase 13: 60.000) | 10/24 / 16/24 | **22/24 / 24/24** (Phase 13: 24/24 / 24/24) |
| Statische Erreichbarkeit der Siegskripte: sauber / eingeschränkt / blockiert | 21 / 3 / 0 | **24 / 0 / 0** |
| Statische Erreichbarkeit der Niederlageskripte: sauber / eingeschränkt / blockiert | 24 / 0 / 0 | 24 / 0 / 0 |
| Deterministisch (zwei Läufe) / identisch nach Speichern+Laden | 24/24 / 24/24 | 24/24 / 24/24 |
| Modulkatalog implementiert / Instanziierbarkeit (Retail-Objekte) | 91/205 / 1.624 (76,8 %) | **98/205 / 1.680 (79,4 %)** |

(Die drei „eingeschränkten“ Siegketten von Ende Phase 11 (`MD_GLA04`, `MD_GLA05`, `MD_CHI05`) sind durch die Phase-12-Aktionen sauber geworden.
Die 25. Karte der Liste, `Maps/CampEADemo/CampEADemo.map`, fehlt im Retail-Bestand.) Vorher/nachher je
Einzelmessung, 3.000 Ticks ohne Fortsetzungsprobe: siehe unten.

**Stand nach Phase 13 (3. Oktober 2026, Release, 900 Ticks, frisch gemessen):** 24/24 deterministisch, 24/24 identisch nach Speichern/Laden; 2.406 Skripte, 7.828 Aktionen ausgeführt, 2.452 weitergereicht,
27 Treffer auf fehlende Aktionstypen (0,3 %), 6.411 auf fehlende Bedingungstypen; Ausschalt-Endproben Sieg 13/24, Niederlage 15/24 (alle unverändert gegenüber Ende Phase 12, die Team-Warteschlangen
verändern die Probenergebnisse nicht); Zielfahrer **Sieg 24/24, Niederlage 24/24** (60.000 Ticks); statische Erreichbarkeit Sieg und Niederlage sauber 24/24.

**Stand nach Phase 14 (3. Oktober 2026, Release, frisch gemessen; alle 183 Simulationsmodule, 2.115 von 2.115 Objekten instanziierbar, Waffenslot-Modell):** `CampaignTool audit --ticks 900` (24 Karten, je Kampagne einzeln gelaufen,
weil die Gesamtlaufzeit auf einer belasteten Maschine über eine Stunde lag): 24/24 deterministisch, 24/24 identisch nach Speichern/Laden, 6.007 Skripte ausgeführt, **0 Treffer auf fehlende Aktions- und Bedingungstypen**;
Ausschalt-Endproben Sieg 13/24, Niederlage 15/24 (unverändert gegenüber Phase 13). Zielfahrer (`drive`, 60.000 Ticks) **Sieg 24/24, Niederlage 24/24**; statische Erreichbarkeit Sieg und Niederlage sauber 24/24. Der Weg dorthin hatte
zwei Rückschritte, die Folge der neuen Module waren und nicht des Fahrers: (1) `MD_GLA03` – der Gegner hat jetzt fliegende Hubschrauber, Raptor-Jets und Schlachtschiffe; sie töten die vom Fahrer eroberte Partikelkanone und die ins
Festland gesetzten Einheiten, ehe das Intro (Flagge `INTRO_DONE`, Tick 1.751) die `MAINLAND`-Skripte freischaltet. Der Fahrer setzt deshalb eine Anwesenheitsbedingung erst um, wenn ihr Skript live ist (`Leaf.Deferred = !ready`),
und repariert eroberte Superwaffen-Träger und platzierte Einheiten jeden Tick (`KeepCapturedCarriersAlive`, als Weltschreibung gezählt). (2) `MD_GLA04` (Niederlage) – der billigste Zweig bringt eine Einheit in `TF_Area`, was über
`If Captured`/`If Got Enough Toxins` unumkehrbar das Siegende startet; bei gleicher Blattzahl bevorzugt der Planer jetzt Pläne ohne Platzierung in einen Bereich (`Leaf.Risky`). Werkzeug-Ergänzungen: `drive --watch-flag NAME[,NAME]`
(Flagge/Skriptzustand, `area:NAME` listet die Objekte im Bereich), `--watch` zeigt Positionen und die nächsten Feinde verletzter Objekte, `coverage` druckt die Zähler. Der Zielfahrer ersetzt weiter Spielerhandlungen durch Welteingriffe
– „ja“ belegt die Skriptkette, nicht die Spielbarkeit.

**Je Karte** (Audit mit 900 Ticks; Endproben: `@N` = Ergebnis N Ticks nach der Probe erreicht, „nicht erreicht“ = Endskripte vorhanden, Ende nicht erreicht; Aktionen = ausgeführt / weitergereicht / fehlende Treffer;
Objekte = erzeugt/platziert; je Kampagne mit der Persona-Fraktion aus `ChallengeMode.ini`, `CampaignTool audit` reicht sie durch):

| Kampagne | Mission | Karte | Objekte | Skripte ausgeführt | Aktionen | Sieg-Probe | Niederlage-Probe |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TRAINING | Mission01 | Training01 | 1448/2040 | 43 | 43 / 57 / 4 | @360 | nicht erreicht |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | 682/983 | 59 | 753 / 21 / 0 | @210 | @90 |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | 1642/2300 | 71 | 553 / 33 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | 584/681 | 75 | 547 / 21 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | 1563/1573 | 932 | 505 / 926 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission05 | GC_Stealth | 770/961 | 81 | 486 / 20 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | 1837/4556 | 94 | 516 / 44 / 0 | @180 | @90 |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | 1245/1551 | 63 | 661 / 18 / 0 | @300 | nicht erreicht |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | 994/1077 | 51 | 563 / 29 / 0 | @210 | @90 |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | 1703/2846 | 38 | 77 / 102 / 1 | nicht erreicht | @151 |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | 1221/3158 | 59 | 110 / 134 / 0 | nicht erreicht | nicht erreicht |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | 1193/1753 | 61 | 383 / 78 / 0 | nicht erreicht | @182 |
| USA | Mission03 | MD_USA03 | 1628/2153 | 42 | 103 / 70 / 0 | nicht erreicht | @181 |
| USA | Mission04 | MD_USA04 | 1195/2027 | 85 | 320 / 49 / 6 | @243 | nicht erreicht |
| USA | Mission05 | MD_USA05 | 1689/2757 | 74 | 222 / 108 / 0 | nicht erreicht | nicht erreicht |
| GLA | Mission01 | MD_GLA01 | 1315/1780 | 110 | 239 / 172 / 2 | nicht erreicht | @181 |
| GLA | Mission02 | MD_GLA02 | 661/1923 | 55 | 263 / 49 / 4 | @180 | nicht erreicht |
| GLA | Mission04 | MD_GLA04 | 2503/4271 | 88 | 294 / 97 / 4 | nicht erreicht | nicht erreicht |
| GLA | Mission05 | MD_GLA05 | 2122/5813 | 78 | 340 / 60 / 0 | nicht erreicht | nicht erreicht |
| China | Mission01 | MD_CHI01 | 1156/3168 | 17 | 53 / 42 / 0 | @241 | @181 |
| China | Mission02 | MD_CHI02 | 1646/2321 | 85 | 260 / 161 / 4 | @241 | @241 |
| China | Mission03 | MD_CHI03 | 1475/2768 | 44 | 286 / 25 / 0 | nicht erreicht | nicht erreicht |
| China | Mission04 | MD_CHI04 | 1624/2038 | 22 | 20 / 37 / 0 | nicht erreicht | @213 |
| China | Mission05 | MD_CHI05 | 1867/2749 | 79 | 231 / 99 / 2 | nicht erreicht | @181 |

**Ehrliche Einordnung:** jede Karte lädt, läuft deterministisch, setzt nach Speichern/Laden identisch fort, die Endskripte aller 24 Karten sind statisch mit lauter implementierten Typen erreichbar, und der
**Zielfahrer erreicht auf allen 24 Karten den Sieg und die Niederlage auf dem eigenen Skriptweg** (Tabelle weiter unten). Die Ausschalt-Endproben (nur Auslöschung) sind weiterhin schwächer (Sieg 13/24),
weil viele Karten Ziele verlangen (z. B. `MD_USA03`: `END_Final_`/`Win` hängen an `TIMER_EXPIRED`, den Zielzähler stellen), die eine Auslöschung nicht abfährt – der Fahrer fährt sie ab. Die zwei Siege, die in
Phase 12 fehlten (`MD_GLA03`: `ParticleUplinkCannonUpdate`, `MD_CHI04`: Wellenmission), sind seit Phase 13 erreicht. **Das belegt die Skriptkette, nicht die Spielbarkeit:** der Fahrer ersetzt Spielerhandlungen durch Welteingriffe.

**Blockerliste (Stand Ende Phase 12; statisch Skripte/Karten laut `CampaignTool reachability`).** Noch fehlende Aktionen (alle in den Ketten ohne Wirkung auf die 24 Endwege):
`SKIRMISH_MOVE_TO_APPROACH_PATH` 10/3, `SKIRMISH_FOLLOW_APPROACH_PATH` 7/3, `AI_PLAYER_BUILD_SUPPLY_CENTER` 5/1, `SET_TRAIN_HELD` 5/1, `TEAM_CAPTURE_NEAREST_UNOWNED_FACTION_UNIT` 5/4,
`TEAM_GUARD_SUPPLY_CENTER` 5/1, `PLAYER_SELECT_SKILLSET` 4/2, `RESUME_SUPPLY_TRUCKING` 4/1, `NAMED_SET_TOPPLE_DIRECTION` 3/3, `PLAYER_SELL_EVERYTHING` 3/2, `SET_BASE_CONSTRUCTION_SPEED` 3/1,
`TEAM_SET_BOOBYTRAPPED` 3/1, `NAMED_SET_BOOBYTRAPPED` 2/1, `NAMED_SET_EVAC_LEFT_OR_RIGHT` 2/1, `NAMED_SET_REPULSOR` 2/2 und fünf Einzelfälle (`LOCALDEFEAT`, `NAMED_SET_STOPPING_DISTANCE`, `TEAM_FACE_NAMED`,
`TECHTREE_MODIFY_BUILDABILITY_OBJECT`, `UNIT_MOVE_TOWARDS_NEAREST_OBJECT_TYPE`); zur Laufzeit (900 Ticks) treffen `TEAM_SET_BOOBYTRAPPED` 6, `NAMED_SET_TOPPLE_DIRECTION` 6, `RESUME_SUPPLY_TRUCKING` 4,
`PLAYER_SELECT_SKILLSET` 4. Fehlende Bedingungen zur Laufzeit: `SUPPLY_SOURCE_SAFE` 3.600, `PLAYER_BUILT_UPGRADE` 900, `PLAYER_COMPLETED_SPECIAL_POWER` 900, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE` 900,
`PLAYER_POWER_COMPARE_PERCENT` 81, `TEAM_STATE_IS` 30 (jeweils eine Karte; unverändert). Vor Phase 12 standen die Gruppen `TEAM_HUNT_WITH_COMMAND_BUTTON` 53/12, `TEAM_SET_ATTITUDE` 42/11,
`TEAM_LOAD_TRANSPORTS` 33/8, `TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_OBJECTTYPE` 30/5, `TEAM_SET_OVERRIDE_RELATION_TO_PLAYER` 27/5, `PLAYER_SET_OVERRIDE_RELATION_TO_TEAM` 19/6, `TEAM_PANIC` 18/4 an der Spitze; sie sind
ausgeführt oder aufgezeichnet (§5). Nicht auflösbare Verweise (Stand Phase 12; die `CINE_U04_AmericaJetCargoPlane_*`-Transporter sind seit Phase 13 instanziierbar, `CINE_U06_CruiseMissileFLY` bleibt an `JetAIUpdate` blockiert): `AmericaJetRaptor`,
`ChinaVehicleHelix`, `FirestormSmall` u. a.; Angriffsprioritäts-Mengen aus der nicht geladenen Skirmish-Skriptbibliothek `SkirmishScripts.scb`; `BUILD_TEAM: no factory for …`; abgelehnte Upgrade-Knöpfe.
Kartenobjekte mit fehlender Vorlage (Straßen, Bürgersteige, Bäume aus Kartenüberlagerungen, `GLADemoTrap`/`GLAStingerSite` u. a.) werden weiter übersprungen und gezählt (je Karte mehrere hundert).
**Verstärkung mit Transport** (Handoff Punkt 3, Phase 13): `CREATE_REINFORCEMENT_TEAM` mit `teamTransport`/`teamStartsFull` erzeugt die Transporter am Startpunkt (`ScriptEngine.Reinforcements.cs`),
lädt das Team ein (`LoadIntoTeamTransports`) und schickt sie über `DeliverPayloadAIUpdate` (Anflug, Absetzen, Neuanflug, Rückflug von der Karte; `ParachuteContain` für Fallschirmabsetzer, Besatzung
stirbt mit abgeschossenem Flugzeug); `JetSlowDeathBehavior` und `CountermeasuresBehavior` (Täuschkörper lenken Lenkwaffen ab) gehören dazu (`SIMULATION-CORE.md` §28). Formationen
(`*_FOLLOW_WAYPOINTS` mit `asTeam`) und der Pfadflug des Partikelstrahls sind seit der Fortsetzung von Phase 13 umgesetzt (Punkt 23).

**Zielfahrer (`CampaignTool drive`, `MissionObjectiveDriver` + `ObjectiveDriver`/`ObjectiveDriver.Leaves`; Phase 11 erster Entwurf, Phase 12 zielgerichtet; Standard seit Phase 13 60.000 Ticks je Lauf, vorher 12.000):** der Fahrer
erreicht den Ausgang einer Mission auf dem **eigenen Skriptweg**, ohne eine Seite auszulöschen: gezählt wird nur, wenn eine `VICTORY`/`DEFEAT`-Aktion eines Missionsskripts selbst feuert. Er ist
Test-/Werkzeugcode (er schreibt wie `MissionProbe` direkt in die Welt, Befehle laufen über den Sitzungszähler) und deterministisch (Test `ScriptObjectiveDriverPlansBackwards`,
`…MovesCountersTheRightWay`, `ScriptExplainLeavesTheEngineUntouched`).

Arbeitsweise (rückwärts vom Endskript, alle 15 Ticks neu geplant, reiner Funktionswert des Motorzustands – `ExplainScript` ist seit Phase 12 zustandsneutral und stellt Bereichszustand, Zähler, ausgelöste
Kräfte, fehlende Bedingungen und Diagnosen nach der Auswertung wieder her):

1. **Plan je Endskript.** Ein Skript feuert, wenn es aktiv (und, als Unterprogramm, gerufen) ist und ein ODER-Zweig seiner Bedingungen gilt. Eine Zeitgeber-/Zähler-/Flag-Bedingung braucht ein Skript, das
   den Wert setzt (rekursiv geplant, Zyklen blockieren nur diesen Weg); alles andere ist ein **Blatt** (Weltzustand, den der Fahrer herstellen kann) oder ein **Blocker** (kein Eingriff bekannt). Alternativen
   (mehrere Freischalter, Setzer, ODER-Zweige) werden nach Blockern, dann Blättern geordnet, der billigste Weg gewinnt; Teamereignisskripte gelten als aufgerufen, sobald irgendein Team, das sie nennt, ein
   lebendes Mitglied hat.
2. **Gegenrichtung.** Skripte, die den anderen Ausgang erreichen (`_tainted`: Skripte mit der entgegengesetzten Endaktion und alle, deren Ziele *ausschließlich* dorthin führen – Verteiler wie
   `Win_Loss_Enable - X` schalten beide Enden frei und zählen deshalb nicht), werden nicht erfüllt; ein Blatt, das die Seite auslöschen würde, die gewonnen werden soll, wird zurückgehalten, bis eine Zeit lang
   nichts anderes Fortschritt macht. **Abwehrblätter** (nur im Siegfall): Einheiten feindlicher Seiten, die dort stehen, wo die Mission bei Ankunft verliert (Fluchtbereich, Basis), werden entfernt – der Ersatz für
   den verteidigenden Spieler.
3. **Zähler richtungsbewusst:** ein Zähler wird nur in die Richtung gestellt, die die Bedingung erfüllt (`>`/`<`/`=`), nicht blind gesetzt.
4. **Stillstand.** Als Fortschritt gilt, was die Skriptfingerabdruck-Signatur ändert (welche Skripte/Gruppen an sind, Flags, Zähler, welche Zeitgeber gestartet sind – nicht der Zeitgeberstand); steht sie
   still und kein Eingriff greift, ist der Lauf ins Stocken geraten. Dann wird die **Vereinigung** aller machbaren Alternativen statt des billigsten Weges erfüllt (Zeitgeber und Wellen laufen, bis sie ablaufen).
5. **Blätter (`ObjectiveDriver.Leaves`):** benanntes Objekt zerstören/besitzen, Team zerstören/auslöschen, Spieler besiegen, Anzahl von Objekten eines Typs/einer Art (ggf. in einem Bereich) auf das Nächste
   ziehen, das den Vergleich erfüllt (überschüssige zerstören oder ein vorhandenes Objekt des Spielers in den Bereich stellen, sonst neu erzeugen), Team in/aus Bereich (Marschbefehl, nach zwei erfolglosen Versuchen
   hinsetzen), Geld, Wissenschaft, Besitzerwechsel, Objektzustand, Gesundheit. Kantenbedingungen (Bereich betreten/verlassen, Objekttyp verloren) werden zurückgehalten, bis ihr Skript live ist. Alles andere
   steht als Blocker in `Unmet`, nie still übergangen.
6. **Diagnose bei Misserfolg:** `Unmet` nennt je Endskript, was der Plan noch offen sah, die Spur der durchlaufenen Skripte (Zustand, Unterprogramm/Verzögerung, Bedingungen) und die lebenden Objekte beider
   Seiten am Ende (`Survivors`: Vorlagenzahlen, häufigste zuerst – was ein „alles zerstören“-Blatt nicht entfernen konnte). `CampaignTool drive --explain --trace` gibt das aus; `template`, `objects` und
   `transports` zeigen Karteninhalt, Objektlisten und Transporter.

**Messung (final, `CampaignTool drive`, Release, 60.000 Ticks, Persona wie im Spiel; frisch gemessen am 3. Oktober 2026):** **Sieg auf dem Skriptweg 24/24, Niederlage 24/24** (Stand Ende Phase 12: Sieg 22/24;
Ende Phase 11: Sieg 10/24, Niederlage 16/24):

| Kampagne | Mission | Karte | Sieg auf dem Skriptweg | Niederlage auf dem Skriptweg |
| --- | --- | --- | --- | --- |
| TRAINING | Mission01 | Training01 | ja – `Player Wins` @420 (1 Eingriff) | ja – `Player Loses` @2716 (2 Eingriffe) |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | ja – `END_Final_USA` @270 (1 Eingriff) | ja – `Lose_END_Final_USA` @150 (1 Eingriff) |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | ja – `END_Final_USA` @240 (1 Eingriff) | ja – `Lose_END_Final_USA` @150 (1 Eingriff) |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | ja – `END_Final_USA` @240 (1 Eingriff) | ja – `Lose_END_Final_USA` @150 (1 Eingriff) |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | ja – `END_Final_USA` @240 (1 Eingriff) | ja – `Lose_END_Final_USA` @150 (1 Eingriff) |
| CHALLENGE_0 | Mission05 | GC_Stealth | ja – `END_Final_USA` @240 (2 Eingriffe) | ja – `Lose_END_Final_USA` @150 (1 Eingriff) |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | ja – `END_Final_USA` @240 (1 Eingriff) | ja – `Lose_END_Final_USA` @150 (1 Eingriff) |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | ja – `End the mission` @360 (4 Eingriffe) | ja – `Lose_END_Final_USA` @2431 (2 Eingriffe) |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | ja – `END_Final_GLA` @270 (1 Eingriff) | ja – `Lose_END_Final_GLA` @150 (1 Eingriff) |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | ja – `END_Final_USA` @785 (1 Eingriff) | ja – `Lose_END_Final_USA` @211 (1 Eingriff) |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | ja – `END_Final_` @2718 (4 Eingriffe) | ja – `Lose_END_Final_` @12361 (30 Eingriffe) |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | ja – `CINE_ENDVictory` @3613 (12 Eingriffe) | ja – `Lose_END_Final_` @242 (1 Eingriff) |
| USA | Mission03 | MD_USA03 | ja – `CINE_AVVictory` @1509 (3 Eingriffe) | ja – `Lose_END_Final_` @241 (1 Eingriff) |
| USA | Mission04 | MD_USA04 | ja – `END_Final_USA` @303 (9 Eingriffe) | ja – `Lose_END_Final_USA` @617 (20 Eingriffe) |
| USA | Mission05 | MD_USA05 | ja – `END_Fade 04` @1443 (4 Eingriffe) | ja – `Lose_END_Final_` @1280 (6 Eingriffe) |
| GLA | Mission01 | MD_GLA01 | ja – `Cheer1Done` @526 (3 Eingriffe) | ja – `Lose_END_Final_` @241 (1 Eingriff) |
| GLA | Mission02 | MD_GLA02 | ja – `END_Final_` @240 (1 Eingriff) | ja – `Lose_END_Final_` @572 (5 Eingriffe) |
| GLA | Mission04 | MD_GLA04 | ja – `CINE_END` @1022 (1 Eingriff) | ja – `Lose_END_Final_` @241 (1 Eingriff) |
| GLA | Mission05 | MD_GLA05 | ja – `CINE_END04END` @1394 (1 Eingriff) | ja – `Lose_END_Final_` @1036 (1 Eingriff) |
| China | Mission01 | MD_CHI01 | ja – `END_Final_` @301 (1 Eingriff) | ja – `Lose_END_Final_` @241 (1 Eingriff) |
| China | Mission02 | MD_CHI02 | ja – `END_Final_` @301 (4 Eingriffe) | ja – `Lose_END_Final_` @301 (1 Eingriff) |
| China | Mission03 | MD_CHI03 | ja – `Cheer1Done` @1722 (6 Eingriffe) | ja – `Lose_END_Final_` @4471 (1 Eingriff) |
| China | Mission04 | MD_CHI04 | ja – `END_Final_` @48348 (13 Eingriffe) | ja – `Lose_END_Final_` @661 (3 Eingriffe) |
| China | Mission05 | MD_CHI05 | ja – `CINE_ENDVictory` @2662 (20 Eingriffe) | ja – `Lose_END_Final_` @241 (1 Eingriff) |

**Wie die zwei früheren „nein“ geschlossen wurden (Phase 13):**

- **`MD_GLA03` (Sieg):** das Siegskript wartet darauf, dass der Partikel-Uplink-Strahl den unsterblichen Träger trifft. `ParticleUplinkCannonUpdate` (`SIMULATION-CORE.md` §28) stellt Strahlzeiten, Schwenk und Schadenspulse
  bereit, der Fahrer fängt den Träger (`SKIRMISH_SPECIAL_POWER_READY`-Blatt: Kraft beim Träger erfassen, aufladen und auslösen) – 12 Eingriffe, Sieg `CINE_ENDVictory` @3.613.
- **`MD_CHI04` (Sieg):** die Wellenmission braucht `UndeadBody` (zwei Leben), `BattleBusSlowDeathBehavior`, `SubObjectsUpgrade` und `AnimationSteeringUpdate` auf den Wellenfahrzeugen; mit ihnen schließen sich die
  Wellenketten, `END_Final_` feuert bei Tick 48.348 (13 Eingriffe). Deshalb ist der Standard von `drive` jetzt 60.000 Ticks (vorher 12.000).

**Was „ja“ nicht heißt:** der Fahrer ersetzt Spielerhandlungen durch Welteingriffe (platzierte Einheiten, entfernte Gegner in Abwehrbereichen, gesetzte Besitzer, Signale für UI-Flags, z. B. bei `Training01`);
„ja“ belegt, dass die *Skriptkette* der Mission bis zum Ausgang läuft und die VM alle Typen auf diesem Weg beherrscht – nicht, dass ein Spieler die Mission mit den implementierten Einheiten tatsächlich
gewinnen kann. Lange Läufe: `MD_USA02` verliert erst bei Tick 11.476 (30 Eingriffe), `MD_CHI03` bei 4.471.

**Längere Messung (`CampaignTool audit --ticks 3000 --no-continuation`, 24 Karten, Release, 2. Oktober 2026):** 24/24 deterministisch; 7.119 Skripte ausgeführt, 13.021 Aktionen ausgeführt, 7.152 weitergereicht, **35 Treffer
auf fehlende Aktionstypen (0,3 %)**, 21.181 auf fehlende Bedingungstypen (davon `SUPPLY_SOURCE_SAFE` 12.000 auf einer Karte, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE`, `PLAYER_COMPLETED_SPECIAL_POWER` und
`PLAYER_BUILT_UPGRADE` je 3.000); Ausschalt-Endproben Sieg 13/24, Niederlage 15/24. Vorher (Ende Phase 11, vor `AI_PLAYER_BUILD_UPGRADE`): 7.115 Skripte, 12.931 Aktionen, 7.163 weitergereicht, 167 Treffer auf fehlende
Aktionstypen (0,8 %), 21.181 auf fehlende Bedingungstypen. Häufigste fehlende Aktionen jetzt: `TEAM_SET_BOOBYTRAPPED` 6, `NAMED_SET_TOPPLE_DIRECTION` 6, `TECHTREE_MODIFY_BUILDABILITY_OBJECT` 5, `RESUME_SUPPLY_TRUCKING` 4,
`PLAYER_SELECT_SKILLSET` 4 (vorher `AI_PLAYER_BUILD_UPGRADE` 25/9, `NAMED_SET_HELD` 13/4, `NAMED_SET_UNMANNED_STATUS` 10/1, `TEAM_SET_OVERRIDE_RELATION_TO_PLAYER` 10/1, `TEAM_SET_OVERRIDE_RELATION_TO_TEAM` 9/5, `TEAM_WANDER` 9/2,
`PLAYER_SET_RANKLEVELLIMIT` 8/8 – alle seither implementiert oder aufgezeichnet).

**Wiederholung nach Phase 13 (3. Oktober 2026, `audit --ticks 3000 --no-continuation`, je Kampagne parallel, 24 Karten):** 24/24 deterministisch und identisch nach Speichern/Wiederherstellen; 7.124 Skripte, 13.016 Aktionen
ausgeführt, 7.155 weitergereicht, 35 Treffer auf fehlende Aktionstypen (0,3 %), 21.181 auf fehlende Bedingungstypen; Ausschalt-Endproben Sieg 13/24, Niederlage 15/24 – gegenüber Phase 12 praktisch unverändert; die
Team-Warteschlangen und die `getTeamNamed`-Regel verschieben die Karten also nicht.

### Nur-Befehle-Modus des Zielfahrers (Phase 13: Handoff Punkt 1, „Spielbarkeit statt Welteingriffe“)

`CampaignTool drive --commands-only [--outcome victory|defeat]` (`MissionObjectiveDriver.Run(…, commandsOnly: true)`, `ObjectiveDriver.Commands.cs`) schreibt **nichts direkt in die Welt**: jedes Blatt trägt eine Art
(`LeafKind`: Weltschreibung, Befehl, UI-Signal), Weltschreibungen (Töten, Platzieren, Geld, Wissenschaft, Besitzerwechsel, Gesundheit, Erzeugen, Aufdecken, die Abwehrblätter) werden **abgelehnt, gezählt und
berichtet** (`ObjectiveDriveResult.Declined`, Zeile `declined world write` in `Unmet`); der Ergebnisbericht trennt jetzt Befehle (`Commands`: Bewegungsbefehle, Produktionsbefehle, die Befehle des Agenten) und
Weltschreibungen (`WorldWrites`) – auch im Normalmodus (`drive` zeigt „N interventions: C commands, W world writes“).

Was ein Spieler tut, übernehmen: (1) „zerstöre X“-Blätter schicken die bewaffneten, beweglichen Einheiten des Betrachters mit einem Angriffsmarsch zum nächsten Opfer und geben es ihnen als Angriffsbefehl (seit Phase 17: `Assault`/`DriveAssaults`; vorher Bewegungsbefehle und die Gefechtsschleife der Sitzung, `DriveCombat`,
schießt, sobald Ziele in Reichweite sind); (2) „Anzahl von Typ X“-Blätter stellen Produktion bei einem eigenen Produzenten an (`QueueProductionCommand` nach `CanQueueProductionTemplate`), Einheiten außerhalb des Bereichs
gehen zu Fuß dorthin (kein Teleport); (3) **Wirtschaft und Heer spielt der Skirmish-Agent**: `SkirmishAi` (`CnCNew.Simulation/Skirmish`, „spielt nur über typisierte Befehle“) steuert den Betrachter durch den
Befehlszähler der Sitzung (`command with { Sequence, Tick }`, damit Skripte, Fahrer und Agent einen Zähler teilen); er baut Basis und Wirtschaft, produziert, sammelt ein Heer und greift in Wellen an, mit den Positionen
der Feindstrukturen vom Kartenstart als Hinweisen (was ein Spieler auf der Minimap sieht). Der Fahrer hält seine Angriffsbefehle zurück, bis der Agent im Angriff ist (einzeln eintreffende Einheiten sterben einzeln).
Ohne Fraktion mit Startgebäude (z. B. `Training01`) gibt es keinen Agenten.

**Messung (3. Oktober 2026, Release, `--commands-only --outcome victory --ticks 15000`, 24 Karten):** **0 Weltschreibungen auf allen Karten, Sieg über die eigenen Skripte auf 1 von 24** – und dort (`MD_GLA04`,
`CINE_END` @1022) endet das Skript ohne Zutun des Spielers; auf den übrigen 23 Karten nicht erreicht:

| Kampagne | Mission | Karte | Sieg nur mit Befehlen (15.000 Ticks) | Befehle (Fahrer + Agent) |
| --- | --- | --- | --- | --- |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | nicht erreicht | 4529 |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | nicht erreicht | 619 |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | nicht erreicht | 7899 |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | nicht erreicht | 1070 |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | nicht erreicht | 3417 |
| CHALLENGE_0 | Mission05 | GC_Stealth | nicht erreicht | 3534 |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | nicht erreicht | 6325 |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | nicht erreicht | 3469 |
| China | Mission01 | MD_CHI01 | nicht erreicht | 3003 |
| China | Mission02 | MD_CHI02 | nicht erreicht | 7292 |
| China | Mission03 | MD_CHI03 | nicht erreicht | 2871 |
| China | Mission04 | MD_CHI04 | nicht erreicht | 353 |
| China | Mission05 | MD_CHI05 | nicht erreicht | 8389 |
| GLA | Mission01 | MD_GLA01 | nicht erreicht | 35 |
| GLA | Mission02 | MD_GLA02 | nicht erreicht | 376 |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | nicht erreicht | 3860 |
| GLA | Mission04 | MD_GLA04 | ja – Skript endet selbst @1022 | 104 |
| GLA | Mission05 | MD_GLA05 | nicht erreicht | 929 |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | nicht erreicht | 813 |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | nicht erreicht | 2191 |
| USA | Mission03 | MD_USA03 | nicht erreicht | 12 |
| USA | Mission04 | MD_USA04 | nicht erreicht | 13330 |
| USA | Mission05 | MD_USA05 | nicht erreicht | 557 |
| TRAINING | Mission01 | Training01 | nicht erreicht | 0 |

**Warum es scheitert (ehrlich, nicht behoben):** (a) Der Agent ist `SkirmishAi`, kein Kampagnenspieler: auf `GC_ChemGeneral` baut er Basis und produziert 97 Einheiten, greift mit **einer** Welle an, verliert sie gegen die vorgefertigte
GLA-Basis (Tunnelnetze, Bunker, Quad-Kanonen) und hat danach kein Heer mehr (`agent: … 1 attack waves … army now 0`). (b) Viele Ziele sind Weltzustände, die nur eine Schreibung erreicht: Geld, Wissenschaft,
Besitzer, UI-Flags, Aufdeckung – sie stehen als `declined world write` in `Unmet`. (c) Kampagnenkarten verlangen Abläufe (Konvois eskortieren, Gebäude erobern, Skriptzeitpunkte), die weder der Agent noch die
Blätter als Befehle kennen. (d) 15.000 Ticks (8,3 Spielminuten) sind für Basisbau plus Angriff knapp; längere Läufe kosten Minuten je Karte (der Agent plant alle paar Ticks). **Ein „Spieler-Agent“, der die Kampagne wirklich
gewinnt, ist Gegenstand des nächsten Handoffs** (`docs/HANDOFF-PHASE14.md`): Angriffswellen mit Rückzug und Verstärkung, Eroberung (`CaptureCommand`), Spezialkräfte, eine KI, die Missionsziele liest.
Der Normalmodus bleibt bei Sieg und Niederlage 24/24 mit Welteingriffen (`Interventions` je Karte: Tabelle oben); der Nur-Befehle-Modus ist der Maßstab, an dem sich der Abstand zur Spielbarkeit messen lässt
(Tests `ScriptCommandsOnlyDriverUsesOrdersNotWrites`, `…DeclinesWhatOnlyAWriteCanDo`: auf einer synthetischen Karte erreicht er den Sieg nur mit Befehlen, und Geld wird abgelehnt und benannt).

**Stand nach Phase 14 (4. Oktober 2026, Release, je Kampagne einzeln mit 15.000 Ticks gelaufen, `drive --commands-only --outcome victory`):** unverändert **1 von 24 Karten** (`MD_GLA04`, Skript endet selbst @1022, 0 Weltschreibungen, 86 Befehle); die
anderen 23 Karten „nicht erreicht“, darunter alle Challenge-Karten (7 + `GC_AirGeneral`), `Training01`, `MD_CHI01–05`, `MD_GLA01/02/03/05`, `MD_USA01–05`. Die neuen Module (Flugzeuge, Kisten, Strukturen, Waffenslots) haben den Abstand zur Spielbarkeit damit
nicht verkleinert: die Ursachen (a)–(d) oben gelten unverändert; ob der Gegner, der jetzt tatsächlich fliegt und schießt (Raptor, Comanche, Schlachtschiffe), den Agenten früher trifft, wurde nicht untersucht. Die USA-Karten
brauchen auch im Nur-Befehle-Modus über eine Stunde je Lauf (14.864 Befehle auf `MD_USA04`). Der Maßstab bleibt Gegenstand des nächsten Handoffs (`docs/HANDOFF-PHASE15.md`, Punkt 1).

**Stand 4. Oktober 2026 abends (Release, je Karte einzeln, `drive --commands-only --outcome victory --ticks 15000`, je 2.400 s Zeitgrenze; `SkirmishAi` mit Versorgungszentrum am Lager, `docs/SIMULATION-CORE.md` §32):**
unverändert **1 Karte** (`MD_GLA04`, @1022, 92 Befehle, 0 Weltschreibungen); 22 Karten „nicht erreicht“ mit 0 Weltschreibungen (`Training01`: 0 Befehle – der Agent findet dort nichts zu tun), `MD_USA01` nach 2.400 s
abgebrochen (kein Ergebnis). Die Zeitgrenze von 15.000 Ticks ist **nicht** der Grund: vier Karten (`GC_ChemGeneral`, `GC_NukeGeneral`, `GC_TankGeneral`, `MD_CHI01`) liefen mit `--ticks 60000` (und der in Gruppen
angreifenden KI) ebenfalls ohne Sieg, mit 1.945 bis 11.243 Befehlen. Die KI-Korrekturen, die 12 statt 5 von 16 Skirmish-Läufen entscheiden, verändern diese Zahl also nicht: der Agent baut und greift an, aber er
erfüllt keine Missionsziele – das bleibt der Kern von Phase 17.

Neu gemessen am selben Tag (Stand `096290b`, vor den KI-Korrekturen; der Fahrer und das Audit benutzen `SkirmishAi` nicht): `drive` (60.000 Ticks) **Sieg 24/24, Niederlage 24/24**; `audit --ticks 900` 24/24
deterministisch, 24/24 identisch nach Speichern/Laden, 0 Treffer auf fehlende Typen, Ausschalt-Endproben **Sieg 13/24, Niederlage 15/24** (je Karte einzeln gelaufen, 24 Karten einschließlich `Training01`).
### Phase 17: die erste unerfüllte Bedingung je Karte als Befehl (5. Oktober 2026, gemessen)

Ausgangsfrage des Handoffs: nicht mehr Zeit, sondern je Karte die erste unerfüllte Bedingung der Siegkette (`drive --commands-only --outcome victory --explain`) als Befehl ausdrücken. Die Vorher-Messung
(Stand `4eea97b`, 15.000 Ticks, je Karte einzeln, 8 Läufe parallel): **1 von 24** (`MD_GLA04`, das Skript endet selbst), `MD_USA01` nach 1.800 s abgebrochen. Auf 9 der 24 Karten stand als erste offene
Bedingung eine **abgelehnte Weltschreibung**, auf den übrigen ein Angriffsblatt, das der Agent nicht gewann.

**Was der Nur-Befehle-Modus jetzt statt einer Weltschreibung tut (`ObjectiveDriver.Commands.cs`, `ObjectiveDriver.Leaves.cs`):**

| Bedingung der Kette | vorher (Nur-Befehle) | jetzt |
| --- | --- | --- |
| Anzahl eines Typs/einer Art eines **Feindes** zu hoch (`PLAYER_HAS_OBJECT_COMPARISON`, `…_IN_TRIGGER_AREA`), `PLAYER_HAS_N_OR_FEWER_(FACTION_)BUILDINGS`, `PLAYER_LOST_OBJECT_TYPE`, `SKIRMISH_PLAYER_IS_OUTSIDE_AREA`, Teamereignisse „zerstört“/„Mitglied zerstört“ | abgelehnt („destroy the surplus …“) | **Angriff**: die Opfer werden Ziel des Heeres; jede bewaffnete Einheit bekommt ihr nächstes Opfer als Angriffsbefehl (`FireWeaponCommand`, sobald in Reichweite – Einheiten eines Menschen wählen Gebäude und neutrale Objekte nicht selbst) und, wenn der Agent sie nicht führt, einen `AttackMoveCommand` dorthin |
| `NAMED_OWNED_BY_PLAYER` für den Betrachter | abgelehnt („hand … to …“) | **`CaptureCommand`** einer Einheit, die die Eroberungsfähigkeit trägt (`SpecialAbilityUpdate` mit `SPECIAL_*_CAPTURE_BUILDING`); ist die Fähigkeit pausiert, wird das auslösende Upgrade gekauft (`UnpauseSpecialPowerUpgrade.TriggeredBy`, `PurchaseUpgradeCommand`; der Agent hält den Preis zurück, `SkirmishAi.ObjectiveReserve`), fehlt eine solche Einheit, wird die billigste produziert; ein noch nicht gesehenes Gebäude bekommt einen Späher |
| `SKIRMISH_SPECIAL_POWER_READY` ohne eigenen Träger | abgelehnt („capture a carrier …“) | Eroberung des Trägers wie oben; ein eigener, noch ladender Träger wird abgewartet (kein Sofort-Aufladen) |
| `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA` | abgelehnt („bring a unit …“) | Marschbefehl einer eigenen Einheit in den Bereich |
| `NAMED_DISCOVERED`, `TEAM_DISCOVERED`, `SKIRMISH_PLAYER_HAS_DISCOVERED_PLAYER` (Betrachter sieht) | abgelehnt (Karte aufdecken) | die nächste eigene Einheit geht hin |
| `PLAYER_ACQUIRED_SCIENCE` (Betrachter) | abgelehnt | `PurchaseScienceCommand` |
| `NAMED_/TEAM_ATTACKED_BY_PLAYER`, `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` (Betrachter greift an) | abgelehnt (Schadenspunkt) | echter Angriff |
| `UNIT_HEALTH` eines feindlichen Objekts nach unten | abgelehnt | Angriff |
| Flag, das kein Skript setzt (UI-Knopf) | abgelehnt | **UI-Signal** (`LeafKind.Signal`): der Spieler drückt den Knopf; keine Weltschreibung |

Zwei Verschärfungen gehören dazu, damit „Befehl“ ehrlich bleibt: ein Nur-Befehle-Lauf kommandiert **nur Einheiten des Betrachters** (ein Marschbefehl an die Einheit einer anderen Seite wird abgelehnt und als
`order '…' of another side` berichtet – vorher wurde er still erteilt), und er greift nie die eigene Seite an. Die reduzierte Sofort-Eroberung der Simulation (jede Einheit, die neben einem Gebäude steht) benutzt
der Fahrer auf Realdaten nicht; nur in der synthetischen Fixture, die keine Einheit mit Fähigkeit kennt. Weiter abgelehnt und benannt: Geld (`PLAYER_HAS_CREDITS`), fremder Besitz, Gesundheit nach oben, Erzeugen
ohne Produzenten.

**Agent (`SkirmishAi` als Wirtschafts- und Heeresführer des Betrachters):** die Opfer der Angriffsblätter sind sein Angriffsziel (`Objectives`; ein Heer, ein Ziel, statt zweier Befehlsgeber an denselben
Einheiten); eine Seite ohne Produktionsgebäude greift mit allem an, was sie hat, sobald ein Ziel benannt ist (`CommitEverything` – sonst wartete ein festes Missionsheer unter der Angriffsschwelle für immer);
bis das erste Versorgungszentrum steht, wird nichts anderes begonnen (`EconomyFirst`). Zwei Fehler der KI, die nur auf Missionskarten auffielen, sind behoben: Lager einer nicht feindlichen Seite zählen als
Lager (Missionskarten geben die `SupplyDock`s einem Zivilspieler, der Agent kannte „0 Lager“ und schickte keinen Sammler), und ein Versorgungszentrum, das neben seiner Quelle dreimal keinen Platz findet,
wird an der Basis gebaut (auf `GC_ChemGeneral` wurde nie eines gebaut, das Startgeld ging in Kraftwerke und Kasernen). Beides ändert die 16 KI-gegen-KI-Läufe nicht (Hashes identisch, `SIMULATION-CORE.md` §33).

**Je Karte: erste unerfüllte Bedingung vorher, was jetzt versucht wird, Ergebnis (gemergter Stand `2a66661` mit Zielerwerb der Einheiten in der Simulation; 0 Weltschreibungen auf allen Karten):**

| Karte | erste unerfüllte Bedingung vorher | jetzt | 15.000 Ticks | 60.000 Ticks |
| --- | --- | --- | --- | --- |
| Training01 | Angriff auf `SCUD Storm Target` (kein Heer, kein Agent: die Seite besitzt anfangs nichts) | unverändert, 0 Befehle | nicht erreicht | nicht gemessen |
| GC_ChemGeneral | alle Baugebäude von `SkirmishGLA` zerstören; Agent ohne Versorgungszentrum | Angriffsziel des Agenten, Wirtschaft steht, Heer verliert | nicht erreicht | nicht erreicht |
| GC_NukeGeneral | alle Baugebäude von `AIOpponent` | wie oben | nicht erreicht | nicht erreicht |
| GC_SuperWeaponsGeneral | alle Baugebäude von `AIOpponent` | wie oben | nicht erreicht | nicht erreicht |
| GC_TankGeneral | alle Baugebäude von `AIOpponent` | wie oben (Infanterie gegen Gatling-Kanonen, Lager am Start erschöpft) | nicht erreicht | nicht erreicht |
| GC_Stealth | alles von `AIOpponent`, `AIOpponent2` | wie oben | nicht erreicht | nicht erreicht |
| GC_LaserGeneral | alles von `PlyrAmericaLaserGeneral` | wie oben | nicht erreicht | nicht erreicht |
| GC_ChinaBoss | **abgelehnt:** Überschuss von `Boss_Airfield/_Barracks/_WarFactory/_CommandCenter/_VehicleDozer` | Angriff auf diese Gebäude | nicht erreicht | nicht erreicht |
| GC_AirGeneral | alles von `AirGen_Grainger` | Angriffsziel des Agenten | nicht erreicht | nicht erreicht |
| MD_USA01 | (Vorher-Lauf abgebrochen) | Angriff: `GLA_Forces_LF` darf kein Gebäude in `Launch_Facility_Area` behalten | nicht erreicht | nicht gemessen |
| MD_USA02 | **abgelehnt:** Überschuss `GLA_Buildings` von `GLA_attackers` | Angriff; `Truck01` gehört einer anderen Seite (Befehl abgelehnt) | nicht erreicht | nicht gemessen |
| MD_USA03 | `Burton` in `End Game Mountain Area`, drei Lagergebäude | Burton gehört (noch) nicht dem Betrachter: Befehl abgelehnt | nicht erreicht | nicht gemessen |
| MD_USA04 | Angriff auf sechs benannte GLA-Gebäude | Angriff, Heer kommt nicht an | nicht erreicht | Lauf nach 3.300 s abgebrochen |
| MD_USA05 | **abgelehnt:** vier Toxin-Gebäude übergeben | Eroberung (Upgrade kaufen, Ranger schicken) | nicht erreicht | nicht erreicht |
| MD_GLA01 | 4 `ShiekLimo` von `PlyrGLA` in `AirfieldArea` | Marschbefehle (eine Limousine lebt noch) | nicht erreicht | nicht erreicht |
| MD_GLA02 | **abgelehnt:** `Primary enemy CC` übergeben | Eroberung; keine Einheit mit Fähigkeit und kein Produzent (abgelehnt, benannt) | nicht erreicht | nicht gemessen |
| MD_GLA03 | **abgelehnt:** Einheit in `MAINLAND`, Träger der Partikelkanone erobern | Marsch, Späher, Eroberung | nicht erreicht | nicht erreicht |
| MD_GLA04 | – (Skript endet selbst) | – | **ja** @1022 | – |
| MD_GLA05 | alles von `AIOpponent` | Angriffsziel des Agenten | nicht erreicht | nicht erreicht |
| MD_CHI01 | alles von `GLA_Enemy` | Angriffsziel des Agenten | nicht erreicht | **ja** – `END_Final_` @18.492 |
| MD_CHI02 | **abgelehnt:** Überschuss `Factory List` von `BG1`–`BG4` | Angriff | nicht erreicht | **ja** – `END_Final_` @29.605 |
| MD_CHI03 | Angriff auf `STATUE-00…` | Angriff | nicht erreicht | nicht erreicht |
| MD_CHI04 | **abgelehnt:** Mitglied von `Wave 6a Team 5` zerstören | Angriff, sobald die Welle existiert (Abwehrmission: es gibt noch nichts anzugreifen) | nicht erreicht | nicht erreicht |
| MD_CHI05 | **abgelehnt:** Überschuss der GLA-Gebäudetypen von `ENEMY_GLA` | Angriff | nicht erreicht | nicht erreicht |

**Ergebnis:** bei 15.000 Ticks unverändert **1 von 24**; mit 60.000 Ticks (18 Karten gemessen) **3 Karten** (`MD_GLA04`, `MD_CHI01`, `MD_CHI02`) – die ersten zwei Siege, die der Agent nur mit Befehlen
erkämpft (vorher gewann er mit 60.000 Ticks keine der vier nachgemessenen Karten, darunter `MD_CHI01`). Auf dem Stand vor dem Merge des Zielerwerbs (nur die Fahrer-Änderungen, `e2ea1a9`) dieselben drei
Karten (`MD_CHI01` @18.747, `MD_CHI02` @23.238). Laufzeiten: der Zielerwerb in der Simulation ersetzt die Gefechtsschleife der Sitzung; `MD_USA01` braucht für 15.000 Ticks 972 s statt über 1.800 s,
`Training01` 170 s statt 672 s (jeweils unter paralleler Last gemessen, keine Leistungsaussage).

**Woran es jetzt scheitert (gemessen, nicht behoben):** kein Lauf endet mehr an einer abgelehnten Schreibung, die ein Spieler als Befehl geben könnte (verbleibende Ablehnungen: Einheiten anderer Seiten auf
`MD_USA02`/`MD_USA03`, fehlende Eroberungseinheit auf `MD_GLA02`). Es scheitert am Gefecht und an der Wirtschaft: (a) die Challenge-Gegner sind vorgebaute Festungen mit sechsstelligem Guthaben; der Agent
produziert zufällig aus dem, was er gerade bezahlen kann – überwiegend Infanterie –, und verliert sie Welle um Welle an der Basisverteidigung (`GC_TankGeneral`: nach 48.000 Ticks 1 Einheit, 25 Credits, das
Lager am Start ist leer); (b) er erschließt kein zweites Lager, wenn das erste leer ist (dieselbe Ursache wie die fünf Patts der KI-Läufe, `SIMULATION-CORE.md` §33.6); (c) Missionsabläufe (Eskorte auf
`MD_GLA01`, Abwehrwellen auf `MD_CHI04`, Inselkarten mit Transport auf `MD_USA04`, Einheiten, die das Skript erst später übergibt, auf `MD_USA03`/`Training01`) kennt weder Agent noch Blatt. Der nächste Schritt
ist deshalb Heereszusammensetzung und Nachschub des Agenten (Reichweite gegen Basisverteidigung, Wert statt Zufall, zweites Versorgungszentrum), danach je Karte der Ablauf.

Tests: `ScriptCommandsOnlyDriverAttacksAnEnemySurplus`, `…CapturesInsteadOfHandingOver`, `…OrdersOnlyItsOwnUnits` (dazu das UI-Signal) neben den beiden bestehenden. Der Normalmodus des Fahrers ist
unverändert: **Sieg 24/24, Niederlage 24/24** (60.000 Ticks, gemergter Stand), Audit 24/24 deterministisch und identisch nach Speichern/Laden, 0 Treffer auf fehlende Typen, Ausschalt-Endproben 13/24 und
15/24, Erreichbarkeit sauber 24/24 und 24/24.

**Abschluss-Sitzung Phase 17 (6. Oktober 2026, Stand `6eb7ae4`, alle 24 Karten bei 15.000 und 60.000 Ticks, je Karte einzeln, 6 Läufe parallel):** mit der Versorgungsregel der KI (`docs/SIMULATION-CORE.md` §36.1) unverändert **1 von 24** bei 15.000 Ticks (`MD_GLA04`) und **3 von 24** bei 60.000 Ticks (`MD_GLA04` @1022, `MD_CHI01` @18.492, `MD_CHI02` @29.605 – dieselben Ticks wie vorher, die Regel greift in diesen Läufen nicht ein); `MD_USA01` und `MD_USA04` bei 60.000 Ticks nach 3.600 s abgebrochen, die übrigen 19 „nicht erreicht“ mit 0 Weltschreibungen. Damit sind erstmals auch `Training01`, `MD_USA02`, `MD_USA03` und `MD_GLA02` mit 60.000 Ticks gemessen (nicht erreicht). Die Heeresauswahl nach Wert und Reichweite verschlechterte den Agenten (60.000 Ticks: 2 statt 3, `MD_CHI02` verloren) und ist zurückgenommen (§36.3). Normalmodus des Fahrers unverändert Sieg 24/24, Niederlage 24/24.
