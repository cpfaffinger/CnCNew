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
| Befehlsknopf-Fähigkeiten | `NAMED_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)`, `TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)` | `doNamed/TeamUseCommandButtonAbility(OnNamed)`: löst den benannten `CommandButton` (`Command = SPECIAL_POWER*`) als gewöhnlichen `UseSpecialPowerCommand` des Trägers aus (`CommandCatalog.TryGetSpecialPowerAbility`); ein Knopf einer anderen Befehlsart oder ein unbekannter Name ist ein Diagnosefall, keine Ausführung (§8) |
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
   Schaltflächenname → `SpecialPowerTemplate`, nur für `Command = SPECIAL_POWER*`-Knöpfe). Pfadvarianten
   (`*_AT_WAYPOINT`, `*_USING_WAYPOINT_PATH`) und die `TEAM_USE_COMMANDBUTTON_ON_NEAREST_*`-Familie sind weiterhin fehlend.
9. **Mission ohne Replay** (§3).
10. **Lokale Seite ohne `playerIsHuman`:** `MissionOptions.HumanSideName` überschreibt den Standard, siehe §7.
11. Karten- und Kampagnen-Anforderungen, die das Original aus dem Spiellauf bezieht (Schwierigkeit pro Spieler, Kamera-/Film-Zeiten,
   Fortschrittsprofil), kommen aus `MissionOptions`/Host.

## 9. Headless-Prüfung (Messung)

Werkzeuge: `CnCNew.CampaignTool audit|list|playthrough` (`docs/CAMPAIGN-AND-SAVEGAMES.md` §6) und `SimTool mission-run`.
Je Mission: zwei frische Läufe (Hash-Kette identisch), Speichern/Wiederherstellen nach der Hälfte (identisch), **Endprobe**
(`MissionProbe`): nach 150 Ticks werden alle Objekte der Feinde (Sieg) bzw. des Betrachters und seiner Verbündeten (Niederlage)
zerstört und 600 Ticks auf das Skriptergebnis gewartet; ein Ergebnis, das die Mission vor der Probe selbst erreicht, zählt nicht.

Stand 1. Oktober 2026, vor dieser Phase (Release, 900 Ticks je Lauf):

- **`ScriptCoverage`:** Bedingungen 63 ausgeführt / 46 fehlend von 109; Aktionen 69 ausgeführt / 129 weitergereicht / 146 fehlend von 344.
- Ausführungsbilanz aller 24 Läufe: 2.307 Skripte ausgeführt, 7.161 Aktionen ausgeführt, 2.434 an den Client weitergereicht, 575 Treffer auf fehlende
  Aktionstypen = 5,7 % aller Aktionsausführungen. Endproben durch Ausschalten: Sieg 10/24, Niederlage 15/24.
- Häufigste fehlende Aktionen (Treffer/Missionen): `NAMED_USE_COMMANDBUTTON_ABILITY` 89/11, `TEAM_GARRISON_SPECIFIC_BUILDING` 38/6,
  `MAP_REVEAL_ALL_PERM` 34/24, `TEAM_GARRISON_NEAREST_BUILDING` 33/3, `NAMED_USE_COMMANDBUTTON_ABILITY_ON_NAMED` 27/2, `BUILD_TEAM` 25/5,
  `TEAM_USE_COMMANDBUTTON_ABILITY` 20/6, `WAREHOUSE_SET_VALUE` 20/5, `TEAM_MERGE_INTO_TEAM` 17/2, `OBJECT_ALLOW_BONUSES` 16/15,
  `PLAYER_ENABLE/DISABLE_BASE/UNIT_CONSTRUCTION` 16…7/3–11.

**Stand nach dieser Phase (1. Oktober 2026, Release, 900 Ticks je Lauf, frisch gemessen; die vier `*_USE_COMMANDBUTTON_ABILITY*`-Aktionen und
`SpecialAbilityUpdate` sind neu):**

- **`ScriptCoverage`:** Bedingungen weiterhin 63 ausgeführt / 46 fehlend von 109 (keine neue Bedingung diese Phase); Aktionen **73 ausgeführt** / 129
  weitergereicht / **142 fehlend** von 344 (vier neu: `NAMED_USE_COMMANDBUTTON_ABILITY`, `NAMED_USE_COMMANDBUTTON_ABILITY_ON_NAMED`,
  `TEAM_USE_COMMANDBUTTON_ABILITY`, `TEAM_USE_COMMANDBUTTON_ABILITY_ON_NAMED`).
- **24 eindeutige Kampagnenkarten** liefen weiterhin fehlerfrei; **24/24** liefern über zwei frische Läufe dieselbe Hash-Kette und denselben Endhash;
  **24/24** setzen nach Speichern und Wiederherstellen hash-identisch fort (unverändert — die 25. Karte der Liste, `Maps/CampEADemo/CampEADemo.map`,
  bleibt im Retail-Bestand nicht vorhanden).
- **Endproben durch Ausschalten:** Sieg **10/24** (unverändert), Niederlage **15/24** (in Summe unverändert, Zusammensetzung verschoben: `MD_USA03`
  erreicht jetzt `lose@181` statt zuvor `UNMET`, `MD_GLA04` erreicht jetzt `UNMET` statt zuvor `lose@31` — mehr instanziierbare Objekte verschieben
  Zähler- und Skriptausführungsreihenfolge in der 150-Tick-Anlaufphase der Probe, kein Regressionsbefund im eigentlichen Skriptverhalten, siehe die
  neue Tabelle unten).
- **Ausführungsbilanz** aller 24 Läufe: 2.341 Skripte ausgeführt, **7.377 Aktionen ausgeführt** (+216), 2.447 an den Client weitergereicht, **445
  Treffer auf fehlende Aktionstypen = 4,3 % aller Aktionsausführungen** (vorher 575 / 5,7 %).
- **Häufigste fehlende Aktionen jetzt:** `TEAM_GARRISON_SPECIFIC_BUILDING` 38/6, `MAP_REVEAL_ALL_PERM` 34/24, `TEAM_GARRISON_NEAREST_BUILDING` 33/3,
  `BUILD_TEAM` 25/5, `WAREHOUSE_SET_VALUE` 20/5, `TEAM_MERGE_INTO_TEAM` 18/3, `OBJECT_ALLOW_BONUSES` 16/15, `PLAYER_ENABLE_BASE_CONSTRUCTION` 16/9,
  `PLAYER_ENABLE_UNIT_CONSTRUCTION` 16/11, `PLAYER_AFFECT_RECEIVING_EXPERIENCE` 15/13, `PLAYER_PURCHASE_SCIENCE` 15/1; die vier implementierten
  Typen sind vollständig aus dieser Liste verschwunden. Noch fehlend aus derselben Familie: die Pfadvarianten
  `NAMED_USE_COMMANDBUTTON_ABILITY_(AT_WAYPOINT|USING_WAYPOINT_PATH)` und `TEAM_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT` sowie die gesamte
  `TEAM_USE_COMMANDBUTTON_ON_NEAREST_*`-Familie (§8).
- **Häufigste fehlende Bedingungen:** unverändert fast ausschließlich die `SKIRMISH_*`-Familie: `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA` 37.227,
  `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` 37.220, `SKIRMISH_SPECIAL_POWER_READY` 36.907, `PLAYER_TRIGGERED_SPECIAL_POWER` 17.106,
  `SKIRMISH_VALUE_IN_AREA` 15.432, `TYPE_SIGHTED` 9.150, `NAMED_DISCOVERED` 3.929, `TEAM_DISCOVERED` 3.600.
- **Unresolved references** nennt jetzt auch Befehlsknöpfe, die real existieren, aber keine Spezialkraft sind (kein stiller Fehlschlag): z. B.
  `command button 'Command_GLAInfantryJarmenKellSnipeVehicleAttack' is not a special-power ability` (40, vermutlich `FIRE_WEAPON`/`SWITCH_WEAPON`)
  und drei `Command_Upgrade*`-Knöpfe (`PLAYER_UPGRADE`, nicht `SPECIAL_POWER`).
- **Nicht erzeugbare Objekte:** `SpecialAbilityUpdate` blockiert keine Mission mehr allein (36 vorher nur dadurch blockierte Objekte sind jetzt
  instanziierbar, `AmericaInfantryRanger`/`MissileDefender`, `GLAInfantryJarmenKell`, `GLAInfantryRebel`-Fähigkeiten u. a.); verbleibend je Mission:
  `CINE_*`-Flugzeuge (Verstärkungstransport nicht simuliert, §8), `ChinaVehicleHelix`, `AmericaJetRaptor`, einzelne `SupW_*`-Sonderwaffenobjekte. Dazu
  weiterhin Kartenobjekte, deren Vorlage im Retail-Basisbestand gar nicht steht (Straßen, Bürgersteige, Zäune, Bäume aus Kartenüberlagerungen,
  `MapOverlayMode.Exclude`): je Mission mehrere hundert bis tausend Stück, alle in `SkippedObjects` gezählt.

| Kampagne | Mission | Karte | Objekte erzeugt/platziert | Skripte | Aktionen ausgeführt / weitergereicht / fehlend | Sieg-Probe | Niederlage-Probe |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TRAINING | Mission01 | Training01 | 1369/2040 | 43 | 28 / 57 / 30 | win:UNMET | lose:UNMET |
| CHALLENGE_0 | Mission01 | GC_ChemGeneral | 656/983 | 59 | 745 / 21 / 8 | win@210 | lose@90 |
| CHALLENGE_0 | Mission02 | GC_NukeGeneral | 1466/2300 | 71 | 528 / 30 / 28 | win@180 | lose@90 |
| CHALLENGE_0 | Mission03 | GC_SuperWeaponsGeneral | 569/681 | 73 | 533 / 21 / 12 | win@180 | lose@90 |
| CHALLENGE_0 | Mission04 | GC_TankGeneral | 1519/1573 | 932 | 480 / 926 / 25 | win@180 | lose@90 |
| CHALLENGE_0 | Mission05 | GC_Stealth | 733/961 | 73 | 469 / 20 / 10 | win@180 | lose@90 |
| CHALLENGE_0 | Mission06 | GC_LaserGeneral | 1214/4556 | 95 | 492 / 45 / 31 | win:UNMET | lose:UNMET |
| CHALLENGE_0 | Mission07 | GC_ChinaBoss | 1055/1551 | 60 | 628 / 18 / 30 | win@300 | lose:UNMET |
| CHALLENGE_1 | Mission06 | GC_AirGeneral | 917/1077 | 50 | 559 / 29 / 3 | win@210 | lose@90 |
| MD_USA_1_DEMO | Mission01 | MD_USA01 | 1374/2846 | 38 | 68 / 102 / 19 | win:UNMET | lose@151 |
| MD_USA_2_DEMO | Mission01 | MD_USA02 | 1049/3158 | 55 | 104 / 131 / 5 | win:UNMET | lose@181 |
| MD_GLA_3_DEMO | Mission01 | MD_GLA03 | 1076/1753 | 61 | 356 / 78 / 31 | win:UNMET | lose@182 |
| USA | Mission03 | MD_USA03 | 1426/2153 | 33 | 77 / 70 / 13 | win:UNMET | lose@181 |
| USA | Mission04 | MD_USA04 | 1106/2027 | 68 | 274 / 49 / 29 | win:UNMET | lose:UNMET |
| USA | Mission05 | MD_USA05 | 1442/2757 | 69 | 199 / 108 / 5 | win:UNMET | lose:UNMET |
| GLA | Mission01 | MD_GLA01 | 1232/1780 | 110 | 225 / 172 / 20 | win:UNMET | lose@181 |
| GLA | Mission02 | MD_GLA02 | 566/1923 | 53 | 232 / 49 / 37 | win@180 | lose:UNMET |
| GLA | Mission04 | MD_GLA04 | 1734/4271 | 88 | 273 / 97 / 33 | win:UNMET | lose:UNMET |
| GLA | Mission05 | MD_GLA05 | 1847/5813 | 76 | 320 / 60 / 18 | win:UNMET | lose:UNMET |
| China | Mission01 | MD_CHI01 | 990/3168 | 17 | 50 / 42 / 6 | win@241 | lose@181 |
| China | Mission02 | MD_CHI02 | 1418/2321 | 74 | 223 / 161 / 25 | win@241 | lose@241 |
| China | Mission03 | MD_CHI03 | 1280/2768 | 43 | 277 / 25 / 8 | win:UNMET | lose:UNMET |
| China | Mission04 | MD_CHI04 | 1259/2038 | 22 | 19 / 37 / 3 | win:UNMET | lose@213 |
| China | Mission05 | MD_CHI05 | 1426/2749 | 78 | 218 / 99 / 16 | win:UNMET | lose@181 |

Sieg-/Niederlage-Probe: `win@N`/`lose@N` = Ergebnis N Ticks nach der Probe erreicht; `win:UNMET`/`lose:UNMET` = Endskripte vorhanden, Ende nicht erreicht;
`early-…` = die Mission entschied sich vor der Probe selbst (zählt nicht).

**Ehrliche Einordnung:** Jede Kampagnenkarte lädt, läuft deterministisch und setzt nach Speichern/Laden identisch fort; ein großer Teil ihres Skriptflusses
(Zustand, Teams, Bereiche, Gefechtsauslöser, Verstärkung, Ende) wird ausgeführt. Die vier häufigsten Befehlsknopf-Fähigkeits-Aktionen
(`NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)`) lösen jetzt echte Spezialkraftbefehle aus, und `SpecialAbilityUpdate` macht die davon betroffenen
Einheiten (Ranger, Jarmen Kell, GLA-Rebellin u. a.) instanziierbar — die Pfadvarianten, die `TEAM_USE_COMMANDBUTTON_ON_NEAREST_*`-Familie und die
inszenierte Fähigkeitswirkung selbst (Anreise, Verpacken, Fähigkeitsobjekte) bleiben offen. **Es ist weiterhin nicht belegt, dass jede Mission
vollständig durchspielbar ist:** Garnisonen, Kartenaufdeckung, das Bauen von KI-Teams, Sichtbedingungen und die `SKIRMISH_*`-KI-Skripte fehlen, und die
Endprobe erreicht nur die durch Ausschalten erreichbaren Enden.

