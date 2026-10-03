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

`MissionSession.Step()` = `ScriptEngine.Update(tick)` → `DriveCombat(tick)` → `SimulationWorld.Step()` → `ScriptEngine.Observe(events)`.

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
| Befehlsknöpfe, andere Arten | `PLAYER_UPGRADE`/`OBJECT_UPGRADE` (kaufen den genannten Upgrade über `PurchaseUpgradeCommand`), `FIRE_WEAPON` (mit Zielobjekt: `FireWeaponCommand` des Trägers; `WeaponSlot` wird **nicht** beachtet) | `Object::doCommandButton*`; jede andere Befehlsart („has command SELL, not executed“) und ein unbekannter Knopf („unknown command button“) sind je ein eigener gezählter Diagnosefall |
| Befehlsknopf-Fähigkeiten, Wegpunkte | `NAMED_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT`, `…_USING_WAYPOINT_PATH` (nur der nächste Pfadpunkt), `TEAM_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT` | `doNamedUseCommandButtonAbilityAtWaypoint`, `…UsingWaypointPath`, `doTeamUseCommandButtonAbilityAtWaypoint`; das Entlangfliegen des Pfades fehlt (§8) |
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
| **Aufgezeichnet** | `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, `OBJECT_ALLOW_BONUSES`; seit Phase 12 `TEAM_SET_ATTITUDE`, `TEAM_WANDER`, `TEAM_WANDER_IN_PLACE`, `NAMED_SET_HELD`, `PLAYER_SET_RANKLEVELLIMIT`, `PLAYER_SET_RANKLEVEL`, `PLAYER_ADD_RANKLEVEL`, `SCRIPTING_OVERRIDE_HULK_LIFETIME` | der Wert wird gespeichert (`SkillPointsModifierOf`, Schalter, Haltung, Rang, Haltezustand, Wrack-Lebensdauer), aber **kein Simulationsteil liest ihn** (keine Erfahrungsgewinn-Skalierung je Spieler, keine Schwierigkeitsboni, keine KI-Haltung, kein Rangsystem, keine Wrackliste); getrennter Zähler `RecordedActions`, weder „ausgeführt“ noch „fehlend“ (§8) |
| **Weitergereicht** (Präsentation) | Kamera, Ton, Sprache, Film, Musik, Radar, UI-Text, Blende, Countdown-Anzeigen, Eingabesperre u. a. (129 Typen) | als `ScriptOutput` (Tick, Aktion, Parameter als Text) an den Client; `SPEECH_PLAY`/Film/Musik/Kamerabewegung gelten für `PresentationLatencyTicks` als laufend |

Alle übrigen Aktionstypen sind **fehlend**: keine Wirkung, Zähler `MissingActionHits[Typ]`.

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
- **Automatisches Gefecht** (`DriveCombat`): Die Simulation hat keine Zielerfassung im Leerlauf (`AutoAcquireEnemiesWhenIdle` wird nicht
  modelliert; auch die Skirmish-KI schießt ausdrücklich). Die Sitzung lässt jedes bewaffnete Objekt alle `CombatIntervalTicks` (3, versetzt
  nach Objekt-ID) per `FireWeaponCommand` auf den Feind in Reichweite schießen, höchste Angriffspriorität zuerst, dann am nächsten.
  Neutrale schießen nie. Sicht/Reichweite prüft die normale Befehlsvalidierung.

## 8. Abweichungen und Näherungen (vollständig)

1. **Bauanlage** (`ThingTemplate::isBuildFacility`): im Original aus den Voraussetzungen anderer Vorlagen berechnet; hier: Struktur mit
   `ProductionUpdate` oder `DozerAIUpdate`.
2. **Gebäudezählung** (`Player::countBuildings`): lebende `STRUCTURE`-Objekte (Fraktionszählung: zusätzlich `MP_COUNT_FOR_VICTORY`).
3. **Erste Auswertung verzögerter Skripte:** das Original zieht `GameLogicRandomValue(0, 2 s)`; hier ein aus dem Skriptnamen abgeleiteter
   Versatz im selben Bereich (verbraucht keinen Zufallsstrom, den eine wiederhergestellte Welt nicht wiederholen würde).
4. **Angreifer-Historie** liest Besitzer/Typ nach dem Weltschritt; ein im selben Tick gestorbener Angreifer wird mit unbekanntem
   Besitzer gespeichert (das Original speichert die Spielermaske zum Schadenszeitpunkt).
5. **Befehle als Bewegung:** `*_HUNT`, `*_ATTACK_*`, `*_GUARD_*` und Pfadfolge bewegen die Einheit; Schießen übernimmt das automatische
   Gefecht der Sitzung. `*_STOP`/`*_GUARD` (ohne Ziel) halten an. Formationen, Teamgeschwindigkeit (`asTeam`) und Angriffsbewegung mit
   Zwischenzielen gibt es nicht.
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
17. **Aufgezeichnet statt ausgeführt:** `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, `OBJECT_ALLOW_BONUSES` und seit Phase 12 `TEAM_SET_ATTITUDE`, `TEAM_WANDER(_IN_PLACE)`, `NAMED_SET_HELD`, `PLAYER_SET_RANKLEVELLIMIT`,
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
23. **Offene VM-Semantik (Stand Phase 13):** erledigt sind `getTeamNamed` für nicht aktive Singleton-Teams (Punkt 12), Team-Bau mit Warteschlange und Startskript (Punkt 15), Rekrutierung mit Sammeln am Heimatort
    (Punkt 16) und die Bereitschaft von Upgrade-Knöpfen (Punkt 18). Bewusst offen: „untätig“ ohne AI-Zustand (§5a: die Simulation führt keinen Zustandsautomaten der Teams, „untätig“ ist „kein Bewegungs-/Angriffsziel“),
    `…_USING_WAYPOINT_PATH` ohne Pfadflug (Punkt 13: Luftlokomotoren fliegen gerade zu einem Ziel, ein Pfad aus mehreren Wegpunkten braucht Pfadzustand im Flugmodell), `FIRE_WEAPON` ohne `WeaponSlot` (§5: eine
    Vorlage trägt genau eine Standardwaffe, ein Waffenslot-Modell ändert Vorlagen, Waffensätze und Hashes) und Formationen/Angriffsmarsch (`asTeam`). Kein Zielfahrer-Blocker hängt an ihnen (§9).

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
stirbt mit abgeschossenem Flugzeug); `JetSlowDeathBehavior` und `CountermeasuresBehavior` (Täuschkörper lenken Lenkwaffen ab) gehören dazu (`SIMULATION-CORE.md` §28). Nicht umgesetzt bleiben
Formationen/Angriffsmarsch (`*_FOLLOW_WAYPOINTS` mit `asTeam`: die Mitglieder laufen einzeln) und der Pfadflug von `NAMED_USE_COMMANDBUTTON_ABILITY_USING_WAYPOINT_PATH`.

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

Was ein Spieler tut, übernehmen: (1) „zerstöre X“-Blätter schicken die bewaffneten, beweglichen Einheiten des Betrachters mit Bewegungsbefehlen zum nächsten Opfer (die Gefechtsschleife der Sitzung, `DriveCombat`,
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
