# Skript-VM und Missionssitzung (Phase 9, Teil A Punkt 2 und 5)

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
| `ScriptCoverage` | `Simulation/Scripting` | die Unterstützungstabelle (ausgeführt / weitergereicht / fehlend) für alle 109 Bedingungs- und 344 Aktionstypen; `Validate()` prüft sie gegen `MapScriptCatalog` |
| `MissionSession` | `Simulation/Mission` | baut aus einer Karte Welt, Spieler, Beziehungen, platzierte Objekte, Baulisten und menschliche Startbasis; ein Tick = Skripte → automatisches Gefecht → Weltschritt; einziger Befehlsannehmer mit einem Sequenzzähler pro Spieler |
| `MissionSnapshot` | `Simulation/Mission` | kanonischer, versionierter Zustand (Welt-Snapshot + Skriptzustand + Sitzungszähler) |
| `MissionScenario`, `MissionProbe` | `Simulation/Testing` | Headless-Läufe, Speichern/Laden-Fortsetzung, Endprobe für Sieg/Niederlage |

Die VM hängt nur von `CnCNew.Data` ab (`SourceRules` gilt): keine Wandzeit, keine ambienten Zufallszahlen (Zufallstimer ziehen aus
`SimulationWorld.LogicRandom`), kein `float`/`double`, keine Dateizugriffe. Alle Sammlungen, die in Zustand oder Hash eingehen, sind
`Sorted*` mit ordinalem Vergleich.

## 2. Ein Tick

`MissionSession.Step()` = `ScriptEngine.Update(tick)` → `DriveCombat(tick)` → `SimulationWorld.Step()` → `ScriptEngine.Observe(events)`.

`ScriptEngine.Update` folgt `ScriptEngine::update`: Endspiel-Zeitgeber (`VICTORY`/`DEFEAT` starten 120 Ticks
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
| **Weitergereicht** (Präsentation) | Kamera, Ton, Sprache, Film, Musik, Radar, UI-Text, Blende, Countdown-Anzeigen, Eingabesperre u. a. (129 Typen) | als `ScriptOutput` (Tick, Aktion, Parameter als Text) an den Client; `SPEECH_PLAY`/Film/Musik/Kamerabewegung gelten für `PresentationLatencyTicks` als laufend |

Alle übrigen Aktionstypen sind **fehlend**: keine Wirkung, Zähler `MissingActionHits[Typ]`.

Nicht auflösbare Verweise zur Laufzeit (unbekannter Spieler/Team/Wegpunkt/Bereich/Objekttyp/Wissenschaft, nicht instanziierbarer Typ,
zu kurze Parameterliste, Rekursion) zählt `Diagnostics`; `ExplainScript(name)` liefert je Bedingung eines Skripts das aktuelle
Ergebnis (Werkzeug/Test, verändert kantengetriggerte Bedingungen).

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
7. **Hüllenmodule** `CommandButtonHuntUpdate` (nur durch `TEAM_HUNT_WITH_COMMAND_BUTTON` ausgelöst) und `LockWeaponCreate` (Primär-Slot;
   andere Slots: nicht unterstützte Konfiguration): das Objekt kann existieren; die auslösenden Aktionen bleiben fehlend und werden gezählt.
8. **Mission ohne Replay** (§3).
9. Karten- und Kampagnen-Anforderungen, die das Original aus dem Spiellauf bezieht (Schwierigkeit pro Spieler, Kamera-/Film-Zeiten,
   Fortschrittsprofil), kommen aus `MissionOptions`/Host.

## 9. Headless-Prüfung (Messung)

Werkzeuge: `CnCNew.CampaignTool audit|list|playthrough` (`docs/CAMPAIGN-AND-SAVEGAMES.md` §6) und `SimTool mission-run`.
Je Mission: zwei frische Läufe (Hash-Kette identisch), Speichern/Wiederherstellen nach der Hälfte (identisch), **Endprobe**
(`MissionProbe`): nach 150 Ticks werden alle Objekte der Feinde (Sieg) bzw. des Betrachters und seiner Verbündeten (Niederlage)
zerstört und 600 Ticks auf das Skriptergebnis gewartet; ein Ergebnis, das die Mission vor der Probe selbst erreicht, zählt nicht.

Stand 1. Oktober 2026 (Release, 900 Ticks je Lauf; frisch messen):

- **`ScriptCoverage`:** Bedingungen 63 ausgeführt / 46 fehlend von 109; Aktionen 69 ausgeführt / 129 weitergereicht / 146 fehlend von 344.
  Retail-Häufigkeit (`MapTool audit`): die fünf häufigsten Bedingungen sind ausgeführt, die sechs häufigsten Aktionen ausgeführt bzw. (`SPEECH_PLAY`) an den Client weitergereicht.
- **24 eindeutige Kampagnenkarten** (17 Kampagnen, 85 Missionen; die Challenge-Kampagnen teilen Karten) liefen fehlerfrei; die 25. Karte der Liste,
  `Maps/CampEADemo/CampEADemo.map`, ist im Retail-Bestand nicht vorhanden (Prüfung `CampaignTool list`).
  **24/24** liefern über zwei frische Läufe dieselbe Hash-Kette und denselben Endhash; **24/24** setzen nach Speichern und Wiederherstellen
  bei Tick 450 hash-identisch fort.
- **Endproben durch Ausschalten:** Sieg **10/24**, Niederlage **15/24** der Karten erreichen ihr skriptdefiniertes Ende. Die übrigen haben Endskripte
  mit Bedingungen, die erst aus einem längeren Missionsverlauf wahr werden (Timer, Flags, Kartenbereiche, Aufträge, die Menschen auslösen); das
  ist kein Nachweis, dass sie nicht funktionieren, sondern dass die Probe sie nicht erreicht (`--explain` nennt die falschen Bedingungen).
- **Ausführungsbilanz** aller 24 Läufe: 2.307 Skripte ausgeführt, 7.161 Aktionen ausgeführt, 2.434 an den Client weitergereicht, **575 Treffer auf fehlende
  Aktionstypen = 5,7 % aller Aktionsausführungen** (vor der zweiten Ausbaustufe 1.272 / 13,5 %).
- **Häufigste fehlende Aktionen** (Treffer / Missionen): `NAMED_USE_COMMANDBUTTON_ABILITY` 89/11, `TEAM_GARRISON_SPECIFIC_BUILDING` 38/6,
  `MAP_REVEAL_ALL_PERM` 34/24, `TEAM_GARRISON_NEAREST_BUILDING` 33/3, `NAMED_USE_COMMANDBUTTON_ABILITY_ON_NAMED` 27/2, `BUILD_TEAM` 25/5,
  `TEAM_USE_COMMANDBUTTON_ABILITY` 20/6, `WAREHOUSE_SET_VALUE` 20/5, `TEAM_MERGE_INTO_TEAM` 17/2, `OBJECT_ALLOW_BONUSES` 16/15,
  `PLAYER_ENABLE/DISABLE_BASE/UNIT_CONSTRUCTION` 16…7/3–11.
- **Häufigste fehlende Bedingungen:** fast ausschließlich die `SKIRMISH_*`-Familie (Skripte der Original-Skirmish-KI, die die Challenge-Karten
  mitbringen): `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA` 37.227, `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` 37.220, `SKIRMISH_SPECIAL_POWER_READY`
  36.907, `SKIRMISH_VALUE_IN_AREA` 15.432; dazu `PLAYER_TRIGGERED_SPECIAL_POWER` 17.106, `TYPE_SIGHTED` 9.150, `NAMED/TEAM_DISCOVERED` (Sicht).
- **Nicht erzeugbare Objekte** (Vorlage vorhanden, aber Modul fehlt, je Mission gezählt): zuerst `AmericaInfantryRanger`/`MissileDefender` und
  `GLAInfantryJarmenKell` (`SpecialAbilityUpdate`), `CINE_*`-Flugzeuge, `ChinaVehicleHelix`, `AmericaJetRaptor`. Dazu Kartenobjekte, deren Vorlage im
  Retail-Basisbestand gar nicht steht (Straßen, Bürgersteige, Zäune, Bäume aus Kartenüberlagerungen, `MapOverlayMode.Exclude`): je Mission
  mehrere hundert bis tausend Stück, alle in `SkippedObjects` gezählt.

| Kampagne | Mission | Karte | Objekte erzeugt/platziert | Skripte | Aktionen ausgeführt / weitergereicht / fehlend | Sieg-Probe | Niederlage-Probe |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TRAINING | Mission01 | Training01 | 1326/2040 | 45 | 37 / 57 / 30 | win:UNMET | lose:UNMET |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | 646/983 | 64 | 751 / 27 / 8 | win@210 | lose@90 |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | 1466/2300 | 71 | 528 / 30 / 28 | win@180 | lose@90 |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | 569/681 | 73 | 518 / 21 / 27 | win@180 | lose@90 |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | 1519/1573 | 932 | 480 / 926 / 25 | win@180 | lose@90 |
| CHALLENGE_0 | Mission05 | GC_Stealth | 732/961 | 73 | 443 / 20 / 36 | win@180 | lose@90 |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | 1184/4556 | 95 | 492 / 45 / 31 | win:UNMET | lose:UNMET |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | 1034/1551 | 63 | 631 / 22 / 30 | win@300 | lose:UNMET |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | 917/1077 | 50 | 559 / 29 / 3 | win@210 | lose@90 |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | 1326/2846 | 38 | 57 / 102 / 30 | win:UNMET | lose@151 |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | 1013/3158 | 56 | 103 / 132 / 7 | win:UNMET | lose@181 |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | 1020/1753 | 60 | 355 / 78 / 31 | win:UNMET | lose@182 |
| USA | Mission03 | MD_USA03 | 1395/2153 | 33 | 74 / 70 / 16 | win:UNMET | lose:UNMET |
| USA | Mission04 | MD_USA04 | 1100/2027 | 68 | 273 / 49 / 30 | win:UNMET | lose:UNMET |
| USA | Mission05 | MD_USA05 | 1429/2757 | 69 | 197 / 108 / 7 | win:UNMET | lose:UNMET |
| GLA | Mission01 | MD_GLA01 | 1213/1780 | 110 | 224 / 172 / 21 | win:UNMET | lose@181 |
| GLA | Mission02 | MD_GLA02 | 526/1923 | 53 | 232 / 49 / 37 | win@180 | lose:UNMET |
| GLA | Mission04 | MD_GLA04 | 1709/4271 | 44 | 131 / 73 / 71 | win:UNMET | lose@31 |
| GLA | Mission05 | MD_GLA05 | 1784/5813 | 76 | 320 / 60 / 18 | win:UNMET | lose:UNMET |
| China | Mission01 | MD_CHI01 | 973/3168 | 17 | 50 / 42 / 6 | win@241 | lose@181 |
| China | Mission02 | MD_CHI02 | 1418/2321 | 74 | 215 / 161 / 33 | win@241 | lose@241 |
| China | Mission03 | MD_CHI03 | 1242/2768 | 43 | 272 / 25 / 13 | win:UNMET | lose:UNMET |
| China | Mission04 | MD_CHI04 | 1243/2038 | 22 | 19 / 37 / 3 | win:UNMET | lose@213 |
| China | Mission05 | MD_CHI05 | 1413/2749 | 78 | 200 / 99 / 34 | win:UNMET | lose@181 |

Sieg-/Niederlage-Probe: `win@N`/`lose@N` = Ergebnis N Ticks nach der Probe erreicht; `win:UNMET`/`lose:UNMET` = Endskripte vorhanden, Ende nicht erreicht;
`early-…` = die Mission entschied sich vor der Probe selbst (zählt nicht).

**Ehrliche Einordnung:** Jede Kampagnenkarte lädt, läuft deterministisch und setzt nach Speichern/Laden identisch fort; ein großer Teil ihres Skriptflusses
(Zustand, Teams, Bereiche, Gefechtsauslöser, Verstärkung, Ende) wird ausgeführt. **Es ist nicht belegt, dass jede Mission vollständig durchspielbar ist:**
Befehlsknopf-Fähigkeiten (`*_USE_COMMANDBUTTON_ABILITY`), Garnisonen, Kartenaufdeckung, das Bauen von KI-Teams, Sichtbedingungen und die
`SKIRMISH_*`-KI-Skripte fehlen, und die Endprobe erreicht nur die durch Ausschalten erreichbaren Enden.

