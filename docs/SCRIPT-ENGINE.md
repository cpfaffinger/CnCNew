# Skript-VM und Missionssitzung (Phase 9, Teil A Punkt 2 und 5; Phase 10/11: Erweiterungen)

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
`.TeamEvents.cs` (Teamereignisskripte). Die Weichen der Grundtypen rufen bei unbekanntem Typ nacheinander die Erweiterungen auf; erst wenn keine zuständig ist, wird der Typ als fehlend
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
| **Aufgezeichnet** | `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, `OBJECT_ALLOW_BONUSES` | der Wert wird gespeichert (`SkillPointsModifierOf`, Schalter), aber **kein Simulationsteil liest ihn** (keine Erfahrungsgewinn-Skalierung je Spieler, keine Schwierigkeitsboni); getrennter Zähler `RecordedActions`, weder „ausgeführt“ noch „fehlend“ (§8) |
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
6. **Verstärkung** (`CREATE_REINFORCEMENT_TEAM`): die Einheiten des Teams (je `teamUnitMaxCount`) erscheinen am Ursprung (oder am Ziel) und
   laufen zum Ziel; der Transport samt Beladung (`teamTransport`, `teamStartsFull`) wird **nicht** simuliert (Diagnose
   `reinforcement transport not simulated`).
7. **Hüllenmodule** `CommandButtonHuntUpdate` (nur durch `TEAM_HUNT_WITH_COMMAND_BUTTON` ausgelöst, weiterhin fehlend) und `LockWeaponCreate` (Primär-Slot;
   andere Slots: nicht unterstützte Konfiguration): das Objekt kann existieren. `SpecialAbilityUpdate` ist seit dieser Phase ebenfalls eine Hülle:
   das Modul trägt nur den geteilten Nachlademechanismus jeder Spezialkraft (`SpecialPowerModuleBase`); die eigentliche Anreise-/Verpackungs-/
   Vorbereitungs-/Fähigkeitsobjekt-Inszenierung des Originals (`SpecialAbilityUpdate.cpp`: Ranger-Fähigkeiten, Colonel Burton, Jarmen Kell, der
   GLA-Rebellin Gebäudeeroberung/Fallenlegen, der Hacker-Geldraub) ist nicht simuliert — `NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY*` lösen die Kraft aus
   und starten ihr Nachladen (ein beobachtbarer, echter Effekt: Bereitschaft, Pausen, geteilte Zeitgeber), der inszenierte Effekt selbst bleibt aus.
8. **Befehlsknöpfe global aufgelöst:** `NAMED_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` sucht im Original im `CommandSet` des Trägers, `TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` in der globalen Schaltflächenregistrierung (`TheControlBar->findCommandButton`); da Schaltflächennamen im
   Originalbestand eindeutig sind, löst `CommandCatalog.TryGetSpecialPowerAbility` beide Varianten gleich auf (eine globale Zuordnung
   Schaltflächenname → `SpecialPowerTemplate`, nur für `Command = SPECIAL_POWER*`-Knöpfe). Die Wegpunkt-Varianten
   (`*_AT_WAYPOINT`, `*_USING_WAYPOINT_PATH`) sind seit Phase 11 ausgeführt (§5, §8 Punkt 13); die `TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_*`-/`TEAM_PARTIAL_USE_COMMANDBUTTON`-Familie ist weiterhin fehlend.
9. **Mission ohne Replay** (§3).
10. **Lokale Seite ohne `playerIsHuman`:** `MissionOptions.HumanSideName` überschreibt den Standard, siehe §7.
11. Karten- und Kampagnen-Anforderungen, die das Original aus dem Spiellauf bezieht (Schwierigkeit pro Spieler, Kamera-/Film-Zeiten,
   Fortschrittsprofil), kommen aus `MissionOptions`/Host.

12. **`getTeamNamed`:** die Aufrufer-Instanz eines Teams (Skript-/Sequenzkontext) hat Vorrang vor anderen Instanzen desselben Prototyps (`FindTeam`/`TeamsNamed` tun das jetzt). Die
    übrigen Fälle des Originals (ein nicht aktives Singleton-Team ist `NULL`) sind nicht nachgebildet: `Created` wird auch von Team-Bau und Rekrutierung gesetzt, vorher wirken Aktionen auf
    die schon vorhandenen Mitglieder.
13. **Spezialkräfte:** `NAMED_FIRE_SPECIAL_POWER_AT_*` und die Wegpunkt-Fähigkeiten gehen als gewöhnlicher `UseSpecialPowerCommand` durch die üblichen Bereitschafts-, Wissenschafts- und
    Zielprüfungen (das Original ruft `doSpecialPowerAtLocation` am Modul und prüft die Bereitschaft nicht); `…_USING_WAYPOINT_PATH` nutzt nur den nächsten Pfadpunkt als Ort.
    `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST` sucht den Ort unter den Positionen feindlicher Objekte (Summe `BuildCost` im Wirkradius, mindestens 50) statt auf dem 10×10-Raster des Originals
    in zufälliger Richtung und nimmt alle Feinde des Spielers statt `getSkirmishEnemyPlayer`.
14. **Garnison:** `MayGarrison` bildet `isKindOf(STRUCTURE) && player == 0 || player == eigener` mit „zuletzt eingetreten“ ab; `…NEAREST_BUILDING` sortiert nach Abstand zum Anführer/zur Einheit
    (Internet-Zentren nur für Geldhacker); `PLAYER_GARRISON_ALL_BUILDINGS` zählt freie Plätze über den Auftrag, das Original prüft nur den Stand beim Befehl.
15. **`BUILD_TEAM`:** vereinfachte `AIPlayer::buildSpecificAITeam`: jede fertige Fabrik des Teambesitzers, die die Einheit herstellen darf (`CanQueueProductionTemplate`), ein Befehl je Tick je
    Auftrag, bevorzugt eine untätige; keine Baulistenzuordnung, keine Priorität/kein Vordrängeln, kein `teamExecutesActionsOnCreate`-Startskript, kein Sammeln am Ursprung. Das Team gilt als
    erstellt, sobald jede `MinCount` erfüllt ist; weitere bis `MaxCount` kommen nach. Produzierte Einheiten treten über `EntitySpawned` bei (`AIPlayer::onUnitProduced`).
16. **`RECRUIT_TEAM`:** wie `Team::tryToRecruit` (Priorität, `teamIsAIRecruitable`, `TEAM_AVAILABLE_FOR_RECRUITMENT`, Standardteam = `team<Spielername>` immer rekrutierbar), aber das Team ist sofort
    erstellt (das Original sammelt am Heimatort und aktiviert nach Leerlauf/60 s); „Bauvarianten“ und gehaltene Einheiten sind nicht berücksichtigt; ohne `teamHome` wird nichts rekrutiert.
17. **Aufgezeichnet statt ausgeführt:** `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, `OBJECT_ALLOW_BONUSES` (§5). Wer sie später auswertet, liest `SkillPointsModifierOf` bzw. führt einen Verbraucher ein;
    bis dahin zählen sie weder als ausgeführt noch als fehlend.
18. **Knopf-Bereitschaft** (`SKIRMISH_COMMAND_BUTTON_READY_*`, `SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_*`) kennt nur Spezialkraft-Knöpfe (`isReady` für Upgrade-Knöpfe fehlt).

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

**Stand nach Phase 11 (1. Oktober 2026, Release, 900 Ticks je Lauf, frisch gemessen):**

| Messgröße | Anfang Phase 11 | Ende Phase 11 |
| --- | --- | --- |
| `ScriptCoverage` Bedingungen ausgeführt / fehlend | 63 / 46 | **80 / 29** |
| `ScriptCoverage` Aktionen ausgeführt / weitergereicht / aufgezeichnet / fehlend | 73 / 129 / 0 / 142 | **131 / 129 / 2 / 82** |
| Skripte ausgeführt (24 Karten) | 2.344 | 2.403 |
| Aktionen ausgeführt / weitergereicht | 7.380 / 2.450 | 7.775 / 2.453 |
| Treffer auf fehlende Aktionstypen (Anteil der Ausführungen) | 445 (4,3 %) | **116 (1,1 %)** |
| Treffer auf fehlende Bedingungstypen | 174.152 | **6.411** |
| Endproben durch Ausschalten: Sieg / Niederlage | 10/24 / 15/24 | 10/24 / 15/24 (unverändert) |
| Statische Erreichbarkeit der Siegskripte: sauber / eingeschränkt / blockiert | 4 / 18 / 2 | **21 / 3 / 0** |
| Statische Erreichbarkeit der Niederlageskripte: sauber / eingeschränkt / blockiert | 0 / 24 / 0 | **24 / 0 / 0** |
| Deterministisch (zwei Läufe) / identisch nach Speichern+Laden | 24/24 / 24/24 | 24/24 / 24/24 |

(Die Erreichbarkeit „Anfang Phase 11“ ist dieselbe Analyse gegen den Stand `49cc6e2`; die 25. Karte der Liste, `Maps/CampEADemo/CampEADemo.map`, fehlt im Retail-Bestand.) Verlauf der Erreichbarkeit
(Siegskripte sauber / Niederlageskripte sauber): Stand von Phase 10 4 / 0 → nach den `SKIRMISH_*`-Bedingungen, Aufdeckung, Garnison, Bauschaltern und Team-Bau 7 / 1 → nach Sequenzskripten und `IDLE_ALL_UNITS` 17 / 21 → nach benannten Spezialkräften, benannten Aufdeckungen und Rekrutierung 20 / 24 → nach Teamereignis-Skripten 21 / 24.

**Je Karte** (Endproben: `win@N`/`lose@N` = Ergebnis N Ticks nach der Probe erreicht, `UNMET` = Endskripte vorhanden, Ende nicht erreicht; „Kette“ = statische Erreichbarkeit, Zahlen sauber/eingeschränkt/blockiert von allen Endskripten):

| Kampagne | Mission | Karte | Objekte erzeugt/platziert | Skripte ausgeführt | Aktionen ausgeführt / weitergereicht / fehlend | Sieg-Probe | Niederlage-Probe | Sieg-Kette | Niederlage-Kette |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TRAINING | Mission01 | Training01 | 1444/2040 | 43 | 30 / 57 / 27 | win:UNMET | lose:UNMET | clean (1/0/0 von 1) | clean (1/0/0 von 1) |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | 663/983 | 59 | 753 / 21 / 0 | win@210 | lose@90 | clean (2/0/1 von 3) | clean (3/0/0 von 3) |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | 1551/2300 | 71 | 556 / 30 / 0 | win@180 | lose@90 | clean (3/0/0 von 3) | clean (3/0/0 von 3) |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | 569/681 | 75 | 547 / 21 / 0 | win@180 | lose@90 | clean (3/0/0 von 3) | clean (3/0/0 von 3) |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | 1524/1573 | 932 | 505 / 926 / 0 | win@180 | lose@90 | clean (3/0/0 von 3) | clean (3/0/0 von 3) |
| CHALLENGE_0 | Mission05 | GC_Stealth | 759/961 | 81 | 486 / 20 / 1 | win@180 | lose@90 | clean (3/0/0 von 3) | clean (3/0/0 von 3) |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | 1820/4556 | 103 | 536 / 48 / 1 | win:UNMET | lose:UNMET | clean (3/0/1 von 4) | clean (3/0/1 von 4) |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | 1184/1551 | 63 | 661 / 18 / 0 | win@300 | lose:UNMET | clean (1/0/0 von 1) | clean (3/0/1 von 4) |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | 987/1077 | 51 | 563 / 29 / 0 | win@210 | lose@90 | clean (3/0/0 von 3) | clean (3/0/0 von 3) |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | 1692/2846 | 38 | 74 / 102 / 12 | win:UNMET | lose@151 | clean (1/0/1 von 2) | clean (1/0/0 von 1) |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | 1204/3158 | 58 | 110 / 134 / 0 | win:UNMET | lose@181 | clean (2/0/0 von 2) | clean (1/0/0 von 1) |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | 1179/1753 | 61 | 373 / 78 / 11 | win:UNMET | lose@182 | clean (1/0/1 von 2) | clean (1/0/0 von 1) |
| USA | Mission03 | MD_USA03 | 1617/2153 | 42 | 100 / 70 / 4 | win:UNMET | lose@181 | clean (1/0/2 von 3) | clean (1/0/1 von 2) |
| USA | Mission04 | MD_USA04 | 1164/2027 | 79 | 304 / 49 / 9 | win:UNMET | lose:UNMET | clean (1/0/0 von 1) | clean (1/0/0 von 1) |
| USA | Mission05 | MD_USA05 | 1657/2757 | 74 | 222 / 108 / 0 | win:UNMET | lose:UNMET | clean (1/0/1 von 2) | clean (2/0/0 von 2) |
| GLA | Mission01 | MD_GLA01 | 1312/1780 | 110 | 234 / 172 / 9 | win:UNMET | lose@181 | clean (1/0/1 von 2) | clean (1/0/0 von 1) |
| GLA | Mission02 | MD_GLA02 | 651/1923 | 55 | 258 / 49 / 11 | win@180 | lose:UNMET | clean (1/0/1 von 2) | clean (1/0/0 von 1) |
| GLA | Mission04 | MD_GLA04 | 2494/4271 | 88 | 288 / 97 / 16 | win:UNMET | lose:UNMET | degraded (0/1/1 von 2) | clean (1/0/0 von 1) |
| GLA | Mission05 | MD_GLA05 | 2096/5813 | 78 | 339 / 60 / 1 | win:UNMET | lose:UNMET | degraded (0/1/1 von 2) | clean (1/0/0 von 1) |
| China | Mission01 | MD_CHI01 | 1134/3168 | 17 | 53 / 42 / 1 | win@241 | lose@181 | clean (1/0/0 von 1) | clean (1/0/0 von 1) |
| China | Mission02 | MD_CHI02 | 1549/2321 | 80 | 250 / 161 / 5 | win@241 | lose@241 | clean (1/0/0 von 1) | clean (1/0/0 von 1) |
| China | Mission03 | MD_CHI03 | 1466/2768 | 43 | 285 / 25 / 0 | win:UNMET | lose:UNMET | clean (6/0/1 von 7) | clean (1/0/0 von 1) |
| China | Mission04 | MD_CHI04 | 1550/2038 | 22 | 20 / 37 / 0 | win:UNMET | lose@213 | clean (1/0/0 von 1) | clean (1/0/0 von 1) |
| China | Mission05 | MD_CHI05 | 1823/2749 | 80 | 228 / 99 / 8 | win:UNMET | lose@181 | degraded (0/1/1 von 2) | clean (1/0/0 von 1) |

**Ehrliche Einordnung:** jede Karte lädt, läuft deterministisch, setzt nach Speichern/Laden identisch fort, und die Endskripte jeder Karte sind – bis auf drei mit unwesentlich eingeschränkter Kette (`MD_GLA04`,
`MD_GLA05`, `MD_CHI05`) – mit lauter implementierten Typen erreichbar; Niederlagen sind es auf allen 24. **Es ist weiterhin nicht belegt, dass jede Mission auf ihrem Skriptweg gewonnen oder verloren wird:** die
Ausschalt-Endproben erreichen nur 10 Siege und 15 Niederlagen, weil die übrigen Karten Ziele verlangen (z. B. `MD_USA03`: `END_Final_`/`Win` hängen an `TIMER_EXPIRED`, den Zielzähler stellen), die eine Auslöschung nicht
abfährt. Der Schritt dazwischen – ein Zielfahrer, der die unerfüllten Bedingungen der besten Kette gezielt erfüllt – ist Auftrag von `docs/HANDOFF-PHASE12.md`.

**Blockerliste (Stand Ende Phase 11).** Häufigste noch fehlende Aktionen (Skripte/Karten, statisch): `TEAM_HUNT_WITH_COMMAND_BUTTON` 53/12, `TEAM_SET_ATTITUDE` 42/11, `TEAM_LOAD_TRANSPORTS` 33/8,
`TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_OBJECTTYPE` 30/5, `TEAM_SET_OVERRIDE_RELATION_TO_PLAYER` 27/5, `PLAYER_SET_OVERRIDE_RELATION_TO_TEAM` 19/6, `TEAM_PANIC` 18/4, `TEAM_DECREASE_PRIORITY` 15/3,
`TEAM_PARTIAL_USE_COMMANDBUTTON` 15/3, `TEAM_MOVE_TOWARDS_NEAREST_OBJECT_TYPE` 13/5, `SCRIPTING_OVERRIDE_HULK_LIFETIME` 12/6, `TEAM_GUARD_IN_TUNNEL_NETWORK` 12/9, `TEAM_SET_OVERRIDE_RELATION_TO_TEAM` 11/7; zur Laufzeit
(900 Ticks) treffen am häufigsten `NAMED_SET_HELD` 12/4, `AI_PLAYER_BUILD_UPGRADE` 10/5, `NAMED_SET_UNMANNED_STATUS` 10/1, `TEAM_SET_OVERRIDE_RELATION_TO_PLAYER` 10/1, `TEAM_SET_OVERRIDE_RELATION_TO_TEAM` 9/5.
Fehlende Bedingungen zur Laufzeit: `SUPPLY_SOURCE_SAFE` 3.600, `PLAYER_BUILT_UPGRADE` 900, `PLAYER_COMPLETED_SPECIAL_POWER` 900, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE` 900, `PLAYER_POWER_COMPARE_PERCENT` 81,
`TEAM_STATE_IS` 30 (jeweils eine Karte). Nicht auflösbare Verweise (Auswahl): Verstärkungstransport `CINE_U04_AmericaJetCargoPlane_LOW/HIGH` (36), `CINE_U06_CruiseMissileFLY` nicht instanziierbar (26),
`SupW_AmericaTankAvenger`/`SupW_AmericaVehicleSentryDrone`/`AmericaJetRaptor`/`ChinaVehicleHelix`/`FirestormSmall` nicht instanziierbar; Angriffsprioritäts-Mengen und -Typen, die nur in der (nicht geladenen) Skirmish-Skriptbibliothek
`SkirmishScripts.scb` definiert sind (`'Tanks'`, `'Infantry'`, `'GLA Attack Priorities'` …, je 3–7); `BUILD_TEAM: no factory for 'GLATankMarauder'` (5) – die Mission hat die Fabrik nicht (Teambau wartet); `FIRE_WEAPON`-Knopf ohne Zielobjekt (27,
`Command_GLAInfantryJarmenKellSnipeVehicleAttack` über eine Aktion ohne Ziel) und abgelehnte Upgrade-Knöpfe (`PurchaseUpgradeCommand` verlangt Geld/Voraussetzungen, das Original-Skript nicht unbedingt).
Kartenobjekte mit fehlender Vorlage (Straßen, Bürgersteige, Bäume aus Kartenüberlagerungen) werden weiter übersprungen und gezählt (je Karte mehrere hundert).

**Zielfahrer (`CampaignTool drive`, Phase 11, `MissionObjectiveDriver`; 4.500 Ticks je Lauf):** statt eine Seite auszulöschen, liest der Fahrer die Weltzustands-Bedingungen *aller* Skripte der Karte und erfüllt sie
nacheinander im Abstand von 15 Ticks (benanntes Objekt zerstören, Team zerstören, Spieler auslöschen – jeweils nur für Feinde/Neutrale bei einem Sieg, für die eigene Seite bei einer Niederlage –, Einheiten per
Marschbefehl in Bereiche schicken, Geld geben, Besitzer wechseln); danach laufen die Skripte allein, und gezählt wird nur, wenn **eine `VICTORY`/`DEFEAT`-Aktion des Skripts selbst** feuert. Er schreibt wie `MissionProbe`
direkt in die Welt (Test-/Werkzeugcode, kein Simulationsverhalten), ist deterministisch (Test `ScriptObjectiveDriver`) und kennt nur diese Eingriffe. Ergebnis: **Sieg auf dem Skriptweg 10/24, Niederlage 16/24** (die
Ausschalt-Endproben: 10/24 und 15/24, aber mit anderer Zusammensetzung – der Fahrer erreicht z. B. die Challenge-Siege `END_Final_USA/GLA/China` und die Siege von `MD_GLA04/05` über `CINE_END*`, die die Ausschaltprobe
verfehlt, scheitert dafür an Karten, deren Ziele er nicht erfüllen kann):

| Kampagne | Mission | Karte | Sieg auf dem Skriptweg | Niederlage auf dem Skriptweg |
| --- | --- | --- | --- | --- |
| TRAINING | Mission01 | Training01 | nein (19 Eingriffe) | nein (23 Eingriffe) |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | ja – `END_Final_USA` @285 (3 Eingriffe) | ja – `Lose_END_Final_USA` @150 (4 Eingriffe) |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | ja – `END_Final_GLA` @255 (4 Eingriffe) | ja – `Lose_END_Final_GLA` @132 (5 Eingriffe) |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | ja – `END_Final_China` @255 (4 Eingriffe) | ja – `Lose_END_Final_China` @150 (6 Eingriffe) |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | ja – `END_Final_China` @255 (4 Eingriffe) | ja – `Lose_END_Final_China` @150 (4 Eingriffe) |
| CHALLENGE_0 | Mission05 | GC_Stealth | ja – `END_Final_China` @270 (5 Eingriffe) | ja – `Lose_END_Final_China` @150 (7 Eingriffe) |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | nein (18 Eingriffe) | ja – `Lose_END_Final_USA` @165 (8 Eingriffe) |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | nein (10 Eingriffe) | ja – `Lose_END_Final_USA` @2431 (10 Eingriffe) |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | ja – `END_Final_USA` @285 (4 Eingriffe) | ja – `Lose_END_Final_USA` @150 (4 Eingriffe) |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | nein (6 Eingriffe) | nein (5 Eingriffe) |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | nein (7 Eingriffe) | ja – `Lose_END_Final_` @241 (1 Eingriffe) |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | nein (12 Eingriffe) | nein (10 Eingriffe) |
| USA | Mission03 | MD_USA03 | nein (21 Eingriffe) | ja – `Lose_END_Final_` @271 (12 Eingriffe) |
| USA | Mission04 | MD_USA04 | nein (53 Eingriffe) | nein (23 Eingriffe) |
| USA | Mission05 | MD_USA05 | nein (4 Eingriffe) | nein (1 Eingriffe) |
| GLA | Mission01 | MD_GLA01 | nein (3 Eingriffe) | ja – `Lose_END_Final_` @481 (17 Eingriffe) |
| GLA | Mission02 | MD_GLA02 | ja – `END_Final_` @300 (14 Eingriffe) | nein (15 Eingriffe) |
| GLA | Mission04 | MD_GLA04 | ja – `CINE_END` @1022 (7 Eingriffe) | nein (5 Eingriffe) |
| GLA | Mission05 | MD_GLA05 | ja – `CINE_END04END` @1484 (11 Eingriffe) | nein (19 Eingriffe) |
| China | Mission01 | MD_CHI01 | ja – `END_Final_` @556 (18 Eingriffe) | ja – `Lose_END_Final_` @391 (12 Eingriffe) |
| China | Mission02 | MD_CHI02 | nein (5 Eingriffe) | ja – `Lose_END_Final_` @316 (5 Eingriffe) |
| China | Mission03 | MD_CHI03 | nein (13 Eingriffe) | ja – `Lose_END_Final_` @1442 (10 Eingriffe) |
| China | Mission04 | MD_CHI04 | nein (9 Eingriffe) | ja – `Lose_END_Final_` @378 (8 Eingriffe) |
| China | Mission05 | MD_CHI05 | nein (36 Eingriffe) | ja – `Lose_END_Final_` @421 (25 Eingriffe) |

Warum die übrigen Karten „nein“ sind (Befund aus `drive --explain`, nicht behoben): (1) die Siegskripte hängen an Zeitgebern, die von Zielskripten gestartet werden, deren Bedingungen nicht Weltzustände der obigen Art sind
(z. B. Wellen-/Fortschrittszähler, Spezialkraft-Nutzung, Gefangenenbefreiung `MD_USA03`: `Win` wartet auf `End Mission Timer`); (2) Ziele, die der Fahrer nicht kennt (Objekte bauen, Einheiten transportieren, Spezialkräfte einsetzen,
Karten-/Truppenzustände über `UNIT_HEALTH`); (3) fehlende Karteninhalte (Überlagerungsobjekte, `CINE_*`-Verstärkungen); (4) `Training01` verlangt Spielerhandlungen der Tutorial-Schritte. Der Auftrag, die
unerfüllten Kettenglieder gezielt zu erfüllen und die Eingriffe zu vervollständigen, bleibt `docs/HANDOFF-PHASE12.md` Punkt 1.

**Längere Messung (`CampaignTool audit --ticks 3000 --no-continuation`, Stand vor `AI_PLAYER_BUILD_UPGRADE`):** 24 Karten, 24/24 deterministisch; 7.115 Skripte ausgeführt, 12.931 Aktionen ausgeführt, 7.163 weitergereicht, 167 Treffer auf fehlende
Aktionstypen (0,8 %), 21.181 auf fehlende Bedingungstypen (davon `SUPPLY_SOURCE_SAFE` 12.000 auf einer Karte); häufigste fehlende Aktionen: `AI_PLAYER_BUILD_UPGRADE` 25/9 (seither implementiert), `NAMED_SET_HELD` 13/4,
`NAMED_SET_UNMANNED_STATUS` 10/1, `TEAM_SET_OVERRIDE_RELATION_TO_PLAYER` 10/1, `TEAM_SET_OVERRIDE_RELATION_TO_TEAM` 9/5, `TEAM_WANDER` 9/2, `PLAYER_SET_RANKLEVELLIMIT` 8/8.
