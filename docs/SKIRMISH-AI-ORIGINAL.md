# Die Skirmish-KI des Originals: Bestandsaufnahme, Entwurf, Umsetzung, Messung

Stand: 8. Oktober 2026, Branch `worktree-agent-a7fb7c6042062e3e9` (Basis `main` `2b11488`). Auftrag des Projektinhabers: die KI des Originals mit ihren Skripten und den drei
Schwierigkeitsgraden übernehmen, reproduzierbar aus einem Seed, und die eigene `SkirmishAi` daneben weiterführen. Dieses Dokument trennt streng zwischen **gemessen**, **umgesetzt, aber
nicht bewertet** und **nicht gemacht**. Nichts davon hat ein Mensch im Client gesehen: der Client spielt weiter mit der eingebauten KI (§6.6).

**Kurzfassung.** Die KI des Originals ist zu zwei Dritteln Daten: `SkirmishScripts.scb` (3.957 Skripte, 1.340 Teamvorlagen) sagt, *was* gebaut wird und wohin Teams gehen; der C++-Teil
(`AISkirmishPlayer` über `AIPlayer`) führt aus: Bauliste, Dozer, Sammler, Teamauswahl, Feindwahl, Verteidigungsring. Die Skript-VM des Projekts führte vor diesem Auftrag bereits alle 33
Bedingungstypen der Bibliothek aus und 48 ihrer 56 Aktionstypen; es fehlten sechs Aktionstypen mit 624 Verwendungen – genau die, die am C++-Teil hängen – und der C++-Teil selbst.
Beides ist jetzt als **zuschaltbare** zweite KI vorhanden (`SkirmishAiKind.Original`, drei Schwierigkeitsgrade je Spieler); die eingebaute KI bleibt Standard und bitgleich
(Eingangsmanifest und Referenzmanifest identisch, §7.1). Der ganze KI-Zustand liegt von Anfang an in Zustand, Hash und Speicherstand der Sitzung.

## 1. Wie das Original einen Skirmish-Computerspieler betreibt

Quellen: `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/…`; jede Aussage nennt Datei und Funktion. Gelesen und neu geschrieben, nichts kopiert.

### 1.1 Start eines Gefechts

| Schritt | Fundstelle | Inhalt |
| --- | --- | --- |
| Seiten der Karte werden Vorlagen | `Source/GameLogic/Map/SidesList.cpp`, `SidesList::prepareForMP_or_Skirmish` | Alle Seiten der Karte wandern nach `m_skirmishSides`; in der Seitenliste bleiben nur `FactionCivilian`-Seiten. Hat **keine** Vorlagenseite eigene Skripte, wird `data\Scripts\SkirmishScripts.scb` gelesen: Skriptlisten (`PlayerScriptsList`), Spielernamen (`ScriptsPlayers`, `ParsePlayersDataChunk`) und Teams (`ScriptTeams`, `ParseTeamsDataChunk`); die Listen werden den Vorlagenseiten **über den Spielernamen** zugeordnet, die Teams der Bibliothek ersetzen die der Karte |
| Ein Spieler je Slot | `Source/GameLogic/System/GameLogic.cpp`, `GameLogic::startNewGame` | Seite `player<Slot>` mit `playerAllies`/`playerEnemies` nach Teamnummer, `multiplayerStartIndex`, Standardteam `teamplayer<Slot>`; im Skirmish `playerIsSkirmish` und aus dem Slotzustand `skirmishDifficulty` (`SLOT_EASY_AI` → leicht, `SLOT_MED_AI` → normal, `SLOT_BRUTAL_AI` → schwer) |
| Skripte und Teams je Spieler | `Source/Common/RTS/Player.cpp`, `Player::initFromDict` | Die Vorlagenseite, deren Fraktion dieselbe **Side** hat wie der Spieler, liefert Skriptliste und Teams; beides wird kopiert und mit der Startposition qualifiziert (`ScriptList::duplicateAndQualify`, `Parameter::qualify` in `ScriptEngine/Scripts.cpp`: Skript-, Gruppen-, Team-, Zähler-, Flag- und Unterprogrammnamen bekommen die Nummer angehängt, ein Side-Parameter mit dem Vorlagennamen wird zum Spielernamen, `<This Team>` bleibt). Bei den Teams werden auch `teamOnCreateScript`, `teamOnIdleScript`, `teamOnUnitDestroyedScript`, `teamOnDestroyedScript`, `teamEnemySightedScript`, `teamAllClearScript`, `teamProductionCondition` und `teamGenericScriptHook0..15` qualifiziert. Ein **menschlicher** Spieler bekommt so die Liste der zivilen Vorlagenseite. Findet sich keine Vorlagenseite der eigenen Side, wird der Spieler „quietly … nonskirmish“ – er läuft ohne KI |
| Siegbedingungen | `GameLogic::startNewGame` | Bei mehr als einem Team werden die 26 Skripte aus `MultiplayerScripts.scb` der neutralen Seite vorangestellt (Ansagen, `VICTORY`/`DEFEAT`/`LOCALDEFEAT` für den lokalen Spieler) |
| Basis | `Source/GameLogic/AI/AISkirmishPlayer.cpp`, `AISkirmishPlayer::newMap`, `adjustBuildList` | Die `SkirmishBuildList` der Side aus `AIData.ini` wird kopiert, so verschoben, dass ihr Kommandozentrum auf der Startposition liegt, und um 135° gedreht (mit `RotateSkirmishBases = Yes` zusätzlich nach dem Neuntel der Karte; ausgeliefert `No`). Die Gebäudewinkel werden nicht mitgedreht. Das Kommandozentrum steht sofort (`buildStructureNow`); `computeCenterAndRadiusOfBase` merkt Mitte und Radius der Liste. **Eigenheit:** `adjustBuildList` prüft für jeden Eintrag die Vorlage des *ersten* Eintrags – die zwölf ausgelieferten Listen beginnen mit dem Kommandozentrum, also wird alles verschoben |

### 1.2 Was C++ je Bild tut (`AIPlayer::update`)

| Teil | Fundstelle (`AI/AIPlayer.cpp`, wo nicht anders genannt) | Verhalten |
| --- | --- | --- |
| Basisbau | `AISkirmishPlayer::doBaseBuilding`, `processBaseBuilding` | Alle zwei Sekunden (sofort, wenn ein Gebäude fertig wurde): ein Gebäude je Runde. Vorrang hat ein **Prioritätseintrag** (von einem Skript verlangt), darüber ein Kraftwerk, wenn Strom fehlt; danach automatische Einträge mit Restwiederholungen. Zerstörte oder eroberte Gebäude warten `RebuildDelayTimeSeconds` (30 s). Baustellen ohne Dozer bekommen einen neuen. Vorher `isLocationSafe`: kein Feind im Umkreis `SupplyCenterSafeRadius` (300) |
| Bau mit Dozer | `AIPlayer::buildStructureWithDozer` | Dozer, volles Geld, Platz; ist der Platz nicht legal, wird in wachsenden Quadraten gesucht (Skirmish: bis 120 Zellen, Schritt vier Zellen). `BuildAssistant::buildObjectNow` prüft **keine Voraussetzungen**; erreicht der Dozer den Platz nicht, wird er versetzt |
| Dozer | `findDozer`, `queueDozer` | Nächster untätiger Dozer; ein Arbeiter, der Vorräte trägt, wird nicht abgezogen; gibt es keinen, wird einer als Prioritätsauftrag bestellt |
| Sammler | `checkForSupplyCenter`, `queueSupplyTruck` | Ein fertiges Versorgungszentrum will `ResourceGatherers<Stufe>` + 1 Sammler, solange ein gefülltes, nicht feindliches Lager in `SUPPLY_CENTER_CLOSE_DIST` (20 Zellen) liegt; fehlende werden als Prioritätsauftrag gebaut, lose Sammler neu angedockt |
| Versorgungszentren | `buildBySupplies`, `findSupplyCenter`, `isSupplySourceSafe` | Nur auf Skriptbefehl (`AI_PLAYER_BUILD_SUPPLY_CENTER`): Lager mit genug Vorrat, ohne eigenes Zentrum daneben, nicht viel näher am Feind; Platz daneben als Prioritätseintrag |
| Rekrutieren vor Ausbilden | `AIPlayer::queueUnits`, `Team::tryToRecruit` | Jeder Auftrag eines Teams nimmt zuerst passende Einheiten aus rekrutierbaren Teams niedrigerer Priorität – vor allem aus dem Standardteam, in dem die Einheiten aufgelöster Teams stehen – im Umkreis `MaxRecruitRadius` (500) um Heimat oder Basismitte; erst dann wird ausgebildet, je Auftrag eine Einheit zur Zeit |
| Teams | `AISkirmishPlayer::doTeamBuilding`, `AIPlayer::selectTeamToBuild`, `isAGoodIdeaToBuildTeam`, `isPossibleToBuildTeam`, `queueUnits`, `checkQueuedTeams`, `checkReadyTeams` | Wenn der Teamtimer abgelaufen ist (`TeamSeconds` 10 s; ÷ 0,6 unter `Poor` 2.000, ÷ 2 über `Wealthy` 7.000): unter allen Teamvorlagen des Spielers, deren **Produktionsbedingung** (ein Skript, `TeamPrototype::evaluateProductionCondition` in `Common/RTS/Team.cpp`) gilt, die noch nicht `teamMaxInstances` Instanzen haben, nicht schon gebaut werden und möglich sind (Fabrik für jeden Typ, eine davon frei, Geld für `TeamResourcesToStart` = 10 % der mittleren Kosten), gewinnt die höchste `teamProductionPriority`; unter gleichen **würfelt** `GameLogicRandomValue(0, count-1)`. Selbst gewählte Teams werden nur an freien Fabriken begonnen; nach `teamInitialIdleFrames` wird ein Team aufgelöst, wenn sein Minimum nicht gebaut oder in Arbeit ist |
| Teamskripte | `Common/RTS/Player.cpp` `Player::update` → `Team::updateGenericScripts` (`Team.cpp`) | Jede Teaminstanz prüft je Bild ihre `teamGenericScriptHook`-Skripte (Bedingungen mit dem Team als `<This Team>`, dann Aktionen; ein Einmal-Skript je Instanz nur einmal). **Hier hängen die Angriffe**: z. B. `China Attack Sequence Flank` am Team `China N 3RD Wave Tanks` |
| Fähigkeiten | `AIPlayer::doUpgradesAndSkills` | Mit Kaufpunkten: der Skillsatz wird einmal **gewürfelt** (`GameLogicRandomValue(0, limit)` über die nicht leeren `SkillSet`-Blöcke der Side), dann werden dessen Wissenschaften in Reihenfolge gekauft |
| Feind | `AISkirmishPlayer::getAiEnemy`, `acquireEnemy` | Alle fünf Sekunden: der Feind bleibt, solange er Einheiten und eine Bauanlage hat; sonst der mit der nächsten Basis; einer, den schon eine andere KI angreift, zählt 500 weiter, einer, der mich angreift, 25 näher |
| Verteidigungsring | `AISkirmishPlayer::buildAIBaseDefenseStructure` | Auf dem Kreis Basisradius + `SkirmishBaseDefenseExtraDistance` (150): „Front“ in Richtung des Pfads `Center<n>`, „Flank“ abwechselnd `Backdoor<n>` und `Flank<n>`; die erste auf der Linie, weitere abwechselnd links und rechts im Abstand von vier Gebäuderadien, bis 60° |
| Superwaffen | `AISkirmishPlayer::computeSuperweaponTarget`, `AIPlayer::computeSuperweaponTarget` | Ziel nach Wert im Wirkradius; Clusterminen **gewürfelt** auf einen der drei eigenen Zugänge |
| Brücken | `updateBridgeRepair`, `AISkirmishPlayer::checkBridges` | Ein Dozer repariert eine zerstörte Brücke auf einem Angriffspfad |

### 1.3 Was die Skripte tun

Gemessen mit `MapTool script-library <content>` (§2). Je Fraktion rund 320 Skripte in 30 bis 32 Gruppen und 94 bis 126 Teamvorlagen. Die Skripte

- bauen die Basis in einer festen Folge (`SKIRMISH_BUILD_BUILDING`, 382 Verwendungen – in den Baulisten ist **nur das Kommandozentrum** automatisch), Versorgungszentren an den Lagern
  (`AI_PLAYER_BUILD_SUPPLY_CENTER`, 164), Verteidigungen (`SKIRMISH_BUILD_BASE_DEFENSE_FRONT/_FLANK`, `SKIRMISH_BUILD_STRUCTURE_FRONT/_FLANK`, zusammen 223) und Upgrades (`AI_PLAYER_BUILD_UPGRADE`, 166);
- führen einen Eskalationszähler (`_ESCALATION`) und Flaggen, an denen die Produktionsbedingungen der Wellen hängen (`_CHINA_3RD_WAVE_N` …);
- definieren die Wellen als Teams mit Priorität, Mindest- und Höchstzahl, Bauzeitgrenze (`teamInitialIdleFrames`, z. B. 3.000 Bilder für vier Battlemaster), Erstell-Skript
  (z. B. „Basis bewachen“) und generischen Skripten (Angriffssequenz über `Center`/`Flank`/`Backdoor`, `SKIRMISH_MOVE_TO_APPROACH_PATH` 69, `SKIRMISH_FOLLOW_APPROACH_PATH` 54, `TEAM_HUNT` 150,
  `TEAM_GUARD_AREA` 671 mit `[Skirmish]MyInnerPerimeter`/`MyOuterPerimeter`/`EnemyInnerPerimeter` und `CombatZone`);
- lösen Spezialkräfte aus (`SKIRMISH_SPECIAL_POWER_READY` 239 Bedingungen, `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST` 107), besetzen Gebäude, erobern Öltürme und herrenlose Fahrzeuge;
- **schenken sich Geld** (`PLAYER_GIVE_MONEY`, 155 Verwendungen), siehe §1.4.

### 1.4 Wie die Schwierigkeit eingeht

| Hebel | Fundstelle | Wirkung im Skirmish |
| --- | --- | --- |
| Skriptflaggen leicht/normal/schwer | `ScriptEngine::executeScript` gegen `Player::getPlayerDifficulty`; für Produktionsbedingungen `TeamPrototype::evaluateProductionCondition` | **der Haupthebel.** Von 3.957 Skripten laufen 986 auf allen Stufen, 1.419 auf normal und schwer, 826 nur schwer, 438 nur normal, 266 nur leicht, 22 leicht und normal. Je Fraktion: leicht 90 bis 113 Skripte, normal 220 bis 244, schwer 253 bis 269 |
| Geldgeschenke | Skripte, z. B. `China AI Bonus Early/Mid/Late/Really Late` (nur schwer) | Die schwere Armee erhält bei Eskalation 5, 20, 40 und 75 zusammen 55.000 Credits; alle Stufen 1.600 zum Start („Give AI money to cover bug“); normal und schwer 4.000 je Basiserweiterung |
| Sammler je Zentrum | `AIPlayer::checkForSupplyCenter`, `AIData.ini` `ResourceGatherersEasy/Normal/Hard` | vorhanden, aber in den ausgelieferten Daten je Side auf allen Stufen gleich (2, GLA 5) |
| Gemeinsames Teamziel | `Team::setTeamTargetObject` (`Team.cpp`) | die leichte Stufe setzt keines |
| Opferwahl eines Trupps | `AIAttackSquadState::chooseVictim` (`AI/AIStates.cpp`) | leicht zufällig, normal das nächste, schwer alle dasselbe |
| Waffen- und Lebenspunktboni `SOLO_AI_*` | `Player::friend_applyDifficultyBonusesForObject` | **nein** – nur in der Kampagne (`docs/SIMULATION-CORE.md` §35) |

### 1.5 Wo der Zufall eingeht

Die Logik zieht aus genau einem Strom (`GameLogicRandomValue`); `InitGameLogicRandom(seed)` wird mit dem Seed der Spielinformation gesetzt (`GameNetwork/LANAPICallbacks.cpp`,
`GameSpy/StagingRoomGameInfo.cpp`), den der Gastgeber aus der Wanduhr nimmt (`LANAPI.cpp`: `setSeed(GetTickCount())`). Danach ist alles deterministisch. Züge der KI: Teamwahl unter
gleicher Priorität (`AIPlayer::selectTeamToBuild`), Skillsatz (`doUpgradesAndSkills`), Clusterminen-Zugang (`AISkirmishPlayer::computeSuperweaponTarget`), die erste Auswertung verzögerter
Skripte (`ScriptEngine.cpp`, 3 Aufrufe), Zufallstimer der Skripte (`SET_RANDOM_TIMER`, `SET_RANDOM_MSEC_TIMER`, 15 Verwendungen), dazu 26 Aufrufe in den Zustandsautomaten der Einheiten
(`AIStates.cpp`, `AIGuard*.cpp`, `AIGroup.cpp`).

## 2. Die Skriptbibliothek in Zahlen (`MapTool script-library <content>`, gemessen)

| | `SkirmishScripts.scb` | `MultiplayerScripts.scb` |
| --- | --- | --- |
| Größe | 2.272.629 Byte, Container `CkMp` | 10.381 Byte |
| Vorlagenspieler / Skriptlisten | 14 / 14 (neutral, 12 Fraktionen, `PlyrCivilian`) | 14 / 14 (nur die neutrale Liste hat Skripte) |
| Skripte | 3.957 (1.810 Unterprogramme, 1.902 einmalig, 366 anfangs inaktiv, 1.047 mit Auswertungsverzögerung) in 378 Gruppen | 26 |
| Bedingungen | 6.956 in 33 Typen | 55 in 7 Typen |
| Aktionen | 11.571 wahr + 26 falsch in 56 Typen | 45 in 7 Typen |
| Teamvorlagen | **1.340 – die Teams stehen in der `.scb`**, nicht in den Karten | 14 (nur Standardteams) |
| davon | 1.285 mit Produktionsbedingung, 1.313 mit Erstell-Skript, 818 mit Zerstört-Skript, 1.765 gesetzte generische Skripthaken, 469 rekrutierbar, 26 mit `teamAutoReinforce`, 18 Singletons, 0 mit Heimatwegpunkt | – |
| weitere Chunks | `ObjectsList`, `PolygonTriggers`, `WaypointsList` (leer, als übersprungen gemeldet) | ebenso |

**Abdeckung der VM vor diesem Auftrag** (Verwendungen in der Skirmish-Bibliothek):

| | Typen | Verwendungen |
| --- | --- | --- |
| Bedingungen ausgeführt | 33 von 33 | 6.956 |
| Aktionen ausgeführt | 48 | 10.885 |
| Aktionen weitergereicht (`MUSIC_SET_TRACK`) | 1 | 76 |
| Aktionen aufgezeichnet (`TEAM_SET_ATTITUDE`; die Stimmung wirkt seit Phase 17 über den Zielerwerb) | 1 | 12 |
| **Aktionen fehlend** | **6** | **624** |

Die sechs: `SKIRMISH_BUILD_BUILDING` 382, `SKIRMISH_BUILD_STRUCTURE_FLANK` 97, `SKIRMISH_BUILD_STRUCTURE_FRONT` 55, `SKIRMISH_BUILD_BASE_DEFENSE_FRONT` 46,
`SKIRMISH_BUILD_BASE_DEFENSE_FLANK` 25, `AI_PLAYER_BUILD_TYPE_NEAREST_TEAM` 19. Dazu kam, was die Zählung nach Typen **nicht** zeigt: die qualifizierten Bereichsnamen
(`[Skirmish]MyOuterPerimeter` 400, `MyInnerPerimeter` 362, `EnemyInnerPerimeter` 50, `EnemyOuterPerimeter` 18 Verwendungen – die VM kannte sie nicht), `<This Player's Enemy>`,
die Schwierigkeit je Spieler, die 1.765 generischen Teamskripte und die Produktionsbedingungen (beides führt nur `AIPlayer`/`Player::update` aus).
**Jetzt:** in einer Sitzung der Original-KI 54 Aktionstypen mit 11.509 Verwendungen ausgeführt, 0 fehlend; zur Laufzeit getroffene fehlende Typen: 0 (§7.3).

**Was die Karten liefern** (64 Skirmish-Karten mit mindestens zwei Startpositionen): keine einzige bringt eigene Skripte einer Vorlagenseite mit (die Bibliothek wird also überall
angehängt); 7 zivile Skriptlisten. Vollständig für **jede** Startposition: `InnerPerimeter<n>` 64 Karten, `OuterPerimeter<n>` 63, Pfad `Center<n>` 61, `Flank<n>` 62, `Backdoor<n>` 60;
`CombatZone` 61. **62 Karten haben die zwölf Vorlagenseiten, zwei nur die drei des Grundspiels** (`Armored Fury`, `Scorched Earth`): dort bekommt ein General-Spieler wie im Original
keine Skripte.

## 3. `AIData.ini` (`SimTool ai-data <content>`, gemessen)

Der Definitions-Compiler führt `AIData` weiter als Kompatibilitätsdefinition (`baselines/compatibility.json`, Kategorie `definition`, 27; **an der Basislinie ändert sich nichts**, 751
Einträge). `CnCNew.Simulation/Skirmish/Original/AiData.cs` liest daraus streng, was die KI braucht: 14 skalare Schlüssel (`StructureSeconds` 0, `TeamSeconds` 10, `Wealthy` 7.000,
`Poor` 2.000, die vier Ratenfaktoren 2 und 0,6, `TeamResourcesToStart` 0,1, `MaxRecruitRadius` 500, `SkirmishBaseDefenseExtraDistance` 150, `RotateSkirmishBases` nein,
`SupplyCenterSafeRadius` 300, `RebuildDelayTimeSeconds` 30), 12 `SideInfo`-Blöcke (Sammler je Stufe, `BaseDefenseStructure1`, je zwei Skillsätze mit sieben Wissenschaften) und
12 `SkirmishBuildList`-Blöcke mit zusammen **214 Gebäuden, davon je Liste genau eines – das Kommandozentrum – automatisch**. 28 weitere gültige Schlüssel der Tabelle liest dieser
Katalog nicht und listet sie (`UnreadKeys`); ein unbekannter Schlüssel, ein fehlender Pflichtschlüssel, eine unlesbare Zahl oder ein Gebäude ohne `Location` ist ein Fehler.

**Warum ein eigener Katalog:** das Eingangsmanifest hasht `TemplateCatalog` als Ganzes; ein neues Mitglied dort hätte es geändert. `AiData` steht daneben und in keinem Manifest.
Die strenge Typisierung im Definitions-Compiler (ein `DefinitionKind` mit Schema, dann fällt der Basislinien-Eintrag `AIData` weg und der Definitions-Snapshot ändert seinen Hash) ist
vorbereitet, aber eine eigene, prüfpflichtige Änderung.

## 4. Die eingebaute `SkirmishAi` im Vergleich

`src/CnCNew.Simulation/Skirmish/SkirmishAi.cs` (1.349 Zeilen) ist eine eigene Entwicklung: alle 15 Bilder ein Denkschritt in fester Reihenfolge, Rollen aus Modulen und `KindOf`,
Planung nur mit Gesehenem und Erinnertem. Ihr Zustand (Gedächtnis, Auftragstabellen, eigener Zufallsstrom) liegt **außerhalb** von Snapshot und Hash (Register E11 in
`docs/MULTIPLAYER-PLAN.md`), `SkirmishMatchSession.CreateSave()` wirft. Bekannte Schwächen laut `docs/SIMULATION-CORE.md` §36/§40: 4 der 16 Referenzläufe und 67 von 320 Läufen der
Erhebung bleiben bei 30 Hz unentschieden; Wirtschaft nach dem ersten Lager; die Armee wird zufällig aus dem Bezahlbaren gekauft; keine Schwierigkeitsgrade. Auch der Nur-Befehle-Agent
der Missionen benutzt sie.

| | `SkirmishAi` (eingebaut) | Original-KI (dieser Auftrag) |
| --- | --- | --- |
| Entscheidungen | Code | Daten (Skripte) + Code |
| Wissen | nur Gesehenes und Erinnertes | alles (wie das Original: keine Sichtprüfung) |
| Zustand in Snapshot/Hash | nein | ja, vollständig |
| Schwierigkeit | keine | drei Stufen je Spieler |
| Sitzung | `SkirmishMatchSession`, als Replay reproduzierbar | `ScriptedSkirmishSession`, wie eine Mission **kein** Replay; Wahrheit ist der Zustand |
| Kartenobjekte | nur Lager | Gebäude und Lager der Karte (§6.4) |

## 5. Entwurf: Alternativen und Empfehlung

**(a) Treue Portierung** – der C++-Kern neu geschrieben, die Bibliothek von der vorhandenen VM ausgeführt. Dafür: die Skripte sind fertig abgestimmte Daten für zwölf Fraktionen und drei
Stufen; die VM deckte sie schon zu 94 % der Verwendungen ab; die Schwierigkeit kommt „geschenkt“ mit. Dagegen: die Sitzung ist nicht mehr als Befehlsreplay reproduzierbar (Skripte
schreiben direkt in die Welt), und die Skriptauswertung kostet Zeit je Tick (§7.4).

**(b) Hybrid/verbessert** – eigene Logik, wo das Original schwach ist. Als *eigene KI-Art* oder Option, nie still in „original“.

**(c) Nur die eingebaute KI ausbauen** – verwirft die 1.340 abgestimmten Teams; die Messung in `docs/SIMULATION-CORE.md` §36.3 zeigt, wie teuer eigene Heeresauswahl zu finden ist.

**Empfohlen und umgesetzt: (a) als zuschaltbare zweite KI, (b) als Vorschlagsliste (§8).** Festlegungen:

- **Determinismus.** Die Simulation ist immer deterministisch. Der „Schalter“ des Auftrags ist der **Seed im Match-Setup** (`ScriptedSkirmishOptions.Seed` → Weltseed → `LogicRandom`):
  ein Test setzt ihn fest, ein normales Spiel zieht ihn außerhalb der Simulation. Alle Zufallszüge der KI gehen durch diesen einen Strom, wie im Original.
- **Zustand von Anfang an im Hash** (Vorgriff auf Phase 24): der C++-Teil lebt im `ScriptEngine` (`ScriptEngine.AiPlayer.cs`); `Describe`/`Load`/`StateHash` enthalten Bauliste, Timer,
  Feind, Skillsatz, Verteidigungswinkel, Produktions- und Hakenzustand in fester Reihenfolge. `WorldSnapshot` ist unverändert (Version 10); der Sitzungszustand hat ein eigenes Format
  `cncnew-scripted-skirmish-state` Version 1.
- **Opt-in.** `ScriptEngineOptions.Ai`/`MissionOptions.Ai` sind `null` für jede Mission; ohne sie nimmt kein neuer Pfad einen anderen Weg. `SkirmishMatchSession` ist unberührt.
- **Zeit.** Die KI denkt auf Ticks, die ein Bild des Originals beginnen; ihre Fristen sind Tick-Fristen aus Bildern/Sekunden über den Zeitdienst (`SourceRules` erzwingt das).
  Sie teilt damit das Raster der Skript-VM. **Achtung Zusammenführung:** die Änderungen an den vorhandenen VM-Dateien sind klein und einzeln (§6.3), weil auf `main` gerade die
  Auswertung der Missions-VM auf Bildgrenzen verlegt wird.
- **Kostenbudget.** Ziel bleibt die Größenordnung der eingebauten KI (Phase 19: 42 µs je Tick für acht). Erreicht ist das nicht (§7.4); der größte Posten ist die Skriptauswertung selbst.

## 6. Was umgesetzt ist

### 6.1 Bausteine

| Baustein | Ort | Aufgabe |
| --- | --- | --- |
| `SageMapDecoder.DecodeScriptLibrary`, `MapScriptLibrary` | `CnCNew.Formats`, `CnCNew.Data/World` | `.scb` dekodieren (drei Chunks, alles andere als übersprungen gemeldet) |
| `AiData` | `Simulation/Skirmish/Original` | §3 |
| `SkirmishScriptAttachment` | `Simulation/Skirmish/Original` | §1.1 als reine Datenumformung: Karte + Bibliothek + Slots → `MapData` (Seite 0 neutral, Seiten 1..N die Slots, dann Zivilisten) |
| `ScriptEngine.AiPlayer.cs` | `Simulation/Scripting` | §1.2, soweit unten nicht ausgenommen; die sechs Aktionen; qualifizierte Bereiche; aktueller Spieler; Schwierigkeit je Liste |
| `ScriptedSkirmishSession` | `Simulation/Skirmish/Original` | Sitzung über `MissionSession`: Startgeld, Starteinheiten, Sieg, Speichern/Laden; ein Slot kann auch von der eingebauten KI gespielt werden (Vergleich) |
| `SkirmishAiKind { Builtin, Original }`, `ScriptDifficulty { Easy, Normal, Hard }` je Slot | ebenda | Auswahl |

### 6.2 Abweichungen vom Original (vollständig)

1. **Voraussetzungen gelten.** Ein Bau ist hier ein gewöhnlicher Spielerbefehl; das Original baut ohne Prüfung. Folge: statt des *ersten* offenen Prioritätseintrags wird der erste
   genommen, den die Regeln jetzt erlauben; einer ohne erfüllte Voraussetzung wartet, ohne die anderen zu blockieren.
2. **Kein Versetzen des Dozers**, wenn er den Bauplatz nicht erreicht.
3. **Kartenobjekte:** Standard sind nur Gebäude und Lager der Karte (§6.4).
4. **Karte aufgedeckt** für Computerspieler (das Original fragt den Nebel beim Platzieren nicht; hier verlangt der Baubefehl ein erkundetes Feld).
5. **Sammler:** nach Wirkung umgesetzt (so viele Sammler wie alle versorgten Zentren zusammen wollen; untätige zum nächsten gefüllten Lager), nicht je Zentrum mit Vorzugsdock.
   Zentren werden in der Welt gesucht, nicht nur in der Bauliste.
6. **Fabriksuche** über alle eigenen Produktionsgebäude und deren `CommandSet` statt nur über die Bauliste.
7. **Nicht umgesetzt:** `selectTeamToReinforce` (`teamAutoReinforce`, 26 Teams – gezählt als Diagnose, wenn ein solches Team gewählt wird), Brückenreparatur, die Verfolgung eines
   GLA-Lochs (der Eintrag wird nach der Wartezeit von einem Arbeiter neu gebaut), das gemeinsame Teamziel und die Opferwahl nach Stufe (§1.4, beide hängen am Trupp-Zustandsautomaten),
   Clusterminen auf die eigenen Zugänge, `teamVeterancy` (4 Teams), `MultiplayerScripts.scb` (die Sitzung entscheidet den Sieg selbst wie `SkirmishMatchSession`).
8. **Stand-ins der Sitzung** wie bei der eingebauten KI: Startgeld 10.000, ein Kaufpunkt alle 2.700 Bilder statt des Rangsystems.
9. **Mensch im Slot:** vorgesehen (zivile Skriptliste, Startbasis über `MissionSession`), aber nur für „Mensch im ersten Slot“ geprüft: die Startbasis eines Menschen nimmt die
   Startposition nach der Zahl der Menschen, nicht nach dem Slot.
10. Was die VM schon vorher näherte (`docs/SCRIPT-ENGINE.md` §8), gilt weiter: Formationen, `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST` ohne Raster, Garnison usw.

### 6.3 Änderungen an vorhandenen VM-Dateien (klein, für die Zusammenführung)

`ScriptEngine.cs`: Option `Ai`, `InitializeAi()` im Konstruktor, `UpdateAiPlayers()` nach der Skriptschleife, Schwierigkeit über `DifficultyOfList`, `<This Player>` über
`CurrentScriptPlayer()`, `<This Player's Enemy>`, `DescribeAi`/`LoadAi`. `ScriptEngine.Actions.cs`/`.Conditions.cs`/`.Extended.cs`/`.Phase13.cs`/`.TeamAbilities.cs`: sechsmal
`_program.FindArea` → `FindAreaQualified`, die Weiche `TryExecuteAiAction`, `AiObserveSpawn`, `SkirmishEnemy()` und `BuildBySupplies` mit KI-Zweig. `ScriptEngine.ExtendedActions.cs`:
`BuildTeam(…, idleOnly)`, Zurückhalten der Teamaufträge, `idleOnly` im Zustand nur, wenn gesetzt. `ScriptProgram.cs`: `GenericScripts`, `AutoReinforce`. `ScriptCoverage.cs`:
`SkirmishAiActions` (die statische Tabelle für Missionen bleibt 187/129/19/9). `MissionSession.cs`: `MissionOptions.Ai`. Ohne `Ai` ist jeder dieser Pfade der alte.

### 6.4 Kartenobjekte: gemessene Begründung

Alpine Assault, China gegen GLA, 4.000 Ticks mit Messfühler (`ai-match … --profile --all-map-objects`): 7.519 µs je Tick, davon `RailroadBehavior` 3.424 µs (13 Aufrufe je Tick – der
Zug der Karte), 596 `StealthUpdate`-Aufrufe je Tick, Welt-Hash 1.011 µs; die Karte bringt 397 Bäume, 22 Laternen, 68 Geräuschobjekte. Ohne diese Objekte (Standard): 165 Objekte am Ende
statt 714 und rund 1,25 ms je Tick. Kein Skript der Bibliothek bezieht sich auf sie. `--all-map-objects` (`ScriptedSkirmishOptions.AllMapObjects`) stellt den Zustand des Originals her.

### 6.5 Tests

`tests/CnCNew.Formats.Tests` (Bibliotheksdekoder, drei Fälle). `tests/CnCNew.Simulation.Tests/Tests.OriginalSkirmishAi.cs`, synthetisch: `AIData` streng (vier Fehlerfälle);
Anhängen wie das Original (Qualifizierung, Verbündete, Mensch, Karte mit eigenen Skripten, fehlende Vorlagenseite); ein Gefecht zweier KIs aus einer Testbibliothek (Kommandozentrum
auf der Startposition, Liste um 135° gedreht, `SKIRMISH_BUILD_BUILDING`, Schwierigkeit je Spieler, `[Skirmish]MyInnerPerimeter`, Teamwahl, Erstell- und generisches Skript, zweiter Lauf
gleich, Speichern/Laden gleich, fremdes Programm abgelehnt); Wiederaufbau und Frontverteidigung; Seed (derselbe gleich, über sechs Seeds beide Skillsätze) und 30/60 Hz (Kaserne zur
selben Spielzeit, Toleranz ein Blick der KI = zwei Sekunden). Mit `--content-dir`: echte Bibliothek auf Alpine Assault (Zahlen aus §2/§3, 0 fehlende Typen, Basen wachsen, gleicher Seed
gleich, anderer Seed anders, Speichern bei Tick 3.000 und Fortsetzen gleich).

### 6.6 Bedienung

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet build -c Release tools/CnCNew.MapTool
dotnet run -c Release --no-build --project tools/CnCNew.MapTool -- script-library <content> [--verbose | --show "<Text im Team- oder Skriptnamen>"]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-data <content>
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-match <content> --map "Maps/Alpine Assault/Alpine Assault.map" --factions FactionChina,FactionGLA `
    --ai original --difficulty medium [--ai-b builtin|original] [--difficulty-b easy|medium|hard] [--seed N] [--max-ticks N | --minutes M] [--trace N] [--profile] [--no-verify] [--all-map-objects]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-survey <content> --out <tsv> --seeds N --minutes 25 --jobs 4 --ai original --difficulty hard --difficulty-b easy
```

`--ai`/`--difficulty` gelten für den ersten Spieler und, wenn `--ai-b`/`--difficulty-b` fehlen, für alle; ohne eine der vier Optionen sind `ai-match` und `ai-survey` unverändert (eingebaute
KI in `SkirmishMatchSession`). `ai-match` prüft einen zweiten frischen Lauf und eine Fortsetzung aus einem Speicherstand (Sitzungshash alle 30 Ticks). **Client:** keine Option ergänzt –
`LiveMatch`/`LiveGame` benutzen `SkirmishMatchSession`; die Zuschauer-Demo kann die Original-KI erst wählen, wenn die Spielschicht eine `ScriptedSkirmishSession` treiben kann
(gleiche Schnittstelle: `Step`, `World`, `Winner`; dazu `Engine.DrainOutputs()` für Musik). Das gehört zum Sitzungskern der Phase 24.

## 7. Messung (8. Oktober 2026, Stand `3c9e267`, Release, 30 Hz, wo nicht anders genannt)

### 7.1 Die eingebaute KI ist unverändert

`SimTool input-manifest check`: identisch. `reference-manifest check --set quick --jobs 3`: 9 Läufe identisch, nach jedem Simulations-Commit; `--set full --jobs 4` am Ende: siehe §7.4.
`ai-match` Alpine Assault `FactionChina,FactionGLA` ohne `--ai`: Sieger FactionChina, 11.011 Ticks, Lauf 2 und Snapshot-Fortsetzung identisch – wie der Manifestlauf
`AlpineAssault_China-GLA`. `IniTool compat-check`: 751 Einträge. Sauberer Solution-Build und `UiRenderTool`: 0 Warnungen. `Formats.Tests`, `Data.Tests` (auch mit Inhalt) grün;
`Simulation.Tests` grün; mit `--content-dir` 439 bestanden, die 10 Fehlschläge sind die zehn Gruppen der Raten-Äquivalenz-Suite, die auf `main` genauso fehlschlagen
(`docs/SIMULATION-CORE.md` §39; Gruppe `ai` unverändert 22/27/28 von 66 Größen).

### 7.2 Erhebungen (`SimTool ai-survey`, 16 Paarungen des Referenzmanifests, 25 Spielminuten Budget, gezählt aus den TSV-Dateien)

| Erhebung | Spiele | entschieden | Slot 1 gewinnt | Slot 2 gewinnt | unentschieden | Median der Dauer entschiedener Spiele |
| --- | --- | --- | --- | --- | --- | --- |
| Original mittel gegen Original mittel, Seeds 1–5 | 80 | **49 (61 %)** | 29 | 20 | 31 | 9,7 Spielminuten (5,9 bis 24,7) |
| Original **schwer** (Slot 1) gegen leicht, Seeds 1–3 | 48 | 45 | **45** | 0 | 3 | 7,8 |
| Original leicht (Slot 1) gegen **schwer**, Seeds 1–3 | 48 | 42 | 0 | **42** | 6 | 7,8 |
| Original mittel (Slot 1) gegen eingebaute KI, Seeds 1–2 | 32 | 21 | 5 | 16 | 11 | 7,2 |
| eingebaute KI (Slot 1) gegen Original mittel, Seeds 1–2 | 32 | 20 | 8 | 12 | 12 | 11,5 |
| Original mittel gegen mittel bei **60 Hz**, Seed 1 (Information) | 16 | 11 (69 %) | 6 | 5 | 5 | 9,6 |

- **Schwer gegen leicht:** die schwere Stufe gewinnt **87 von 96 Spielen (91 %)**, die leichte **keines**, 9 bleiben unentschieden (alle neun in der Paarung ChinaTankGeneral gegen
  AmericaAirForceGeneral; sechs davon auf `Armored Fury`, wo beide Generäle keine Vorlagenseite und damit keine Skripte haben, §2).
- **Mittel gegen mittel:** 61 % entschieden; die eingebaute KI entscheidet bei 30 Hz 253 von 320 (79 %). Nach Paarung (Slot 1/Slot 2/offen): China–GLA 0/20/0, GLA–America 13/0/7,
  Tank–AirForce 10/0/10 (davon 5 auf `Armored Fury` ohne Skripte), America–China 6/0/14. **Keine Balance-Aussage.**
- **Original gegen eingebaut** (64 Spiele, beide Sitzreihenfolgen): Original 17, eingebaut 24, 23 offen. Die eingebaute KI spielt dabei in derselben Sitzung mit ihrem eigenen Wissen
  (nur Gesehenes); es ist ein Vergleich zweier Implementierungen in dieser Simulation, keiner mit dem Originalspiel.
- **Determinismus:** jede Erhebung hat so viele verschiedene Endhashes wie Spiele (anderer Seed, anderer Verlauf); derselbe Seed zweimal: `ai-match` Alpine Assault China/GLA mittel –
  Sieger FactionGLA bei Tick 24.435, Sitzungshash `A3EF42B6F199D73A`, zweiter Lauf an 825 von 825 Stichpunkten gleich, Speichern bei Tick 11.250 (892.130 Byte) und Fortsetzen an 450 von
  450 gleich; dazu die Tests in §6.5.
- **Fehlende Skripttypen zur Laufzeit:** 0 in allen 256 Spielen (`ai-survey` wertet ein Spiel sonst als fehlgeschlagen; fehlgeschlagen: 0).
- **Vor der Rekrutierungsregel** (`AIPlayer::queueUnits`: erst rekrutieren, dann ausbilden; Commit `3c9e267`) entschied dieselbe Erhebung mittel gegen mittel nur 39 von 80 – die Angriffswellen
  liefen aus ihrer Bauzeit, während ihre Einheiten im Standardteam standen (41 aufgelöste Teams in einem Spiel). Einzelne Regeln des C++-Teils wirken also stark; die offenen aus §6.2
  Punkt 7 sind keine Kleinigkeiten.

Was offene Spiele zeigen (Spur eines Laufs, Alpine Assault America gegen China): die Lager der Karte sind nach rund 17 Spielminuten leer, beide Seiten haben Geld nahe null und stehende
Armeen. Ob die Geldquellen der Fraktionen (Abwurfzone, Schwarzmarkt, Hacker) so viel liefern wie im Original, ist **nicht geprüft**.

### 7.3 Kosten je Tick (ruhiger Rechner, `ai-match … --no-verify`, 18.000 Ticks)

| Lauf | Zeit je Tick | davon Skripte und KI (Messfühler `--profile`) | Welt |
| --- | --- | --- | --- |
| 2 Spieler, Alpine Assault, America gegen China, mittel | 629 / 633 µs (zwei Läufe) | 223 µs (Skriptschleife 186, Teamaufträge 14) | Hash 140, Updates 118, Sicht 71 |
| 8 Spieler, Twilight Flame, mittel | 2.790 µs | **1.123 µs** (Skriptschleife 976; allein `SKIRMISH_SPECIAL_POWER_READY` 260 µs bei 52 Aufrufen je Tick) | Updates 639, Hash 480, Sicht 460 |
| zum Vergleich: eingebaute KI, 8 Spieler (Phase 19) | 1.140 µs gesamt | 42 µs | – |

Das Budget (Größenordnung der eingebauten KI) ist **um den Faktor 27 verfehlt**. Der Posten ist die VM: jedes aktive Skript jedes Spielers wird je Tick betrachtet (rund 320 je Spieler),
die generischen Teamskripte je Bild. Das Original tut dasselbe. Bei 60 Hz läuft die Skriptschleife je Tick (doppelt so oft), die KI selbst nur je Bild. Mit allen Kartenobjekten: §6.4.

### 7.4 Volles Referenzmanifest

`reference-manifest check --set full --jobs 4` am Stand `3c9e267`: **50 Läufe identisch**; `input-manifest check`: identisch. Kein `write` auf eines der Manifeste.

## 8. Vorschläge über das Original hinaus (keiner umgesetzt; jeder nur als eigene Option oder KI-Art, nie in „original“)

| # | Vorschlag | Begründung aus den Messungen | Messbar an |
| --- | --- | --- | --- |
| 1 | **Generäle auf Karten ohne ihre Vorlagenseite** bekommen die Skripte der Grundseite (America/China/GLA) | 2 von 64 Karten; dort tut die Original-KI eines Generals nichts | Anteil entschiedener Spiele auf `Armored Fury`, `Scorched Earth` |
| 2 | **Skriptauswertung nach Bedarf**: Bedingungen, die nur Zähler/Flaggen/Timer lesen, erst neu auswerten, wenn sich einer davon geändert hat; `SKIRMISH_SPECIAL_POWER_READY` und `BUILT_BY_PLAYER` über die Besitzer-/Typlisten der Welt (Phase 19) | 1.123 µs je Tick bei acht Spielern | `ai-match --profile`, Endhash unverändert (reine Leistungsänderung) |
| 3 | **Fehlende Regeln des C++-Teils** nachziehen: `selectTeamToReinforce`, gemeinsames Teamziel und Opferwahl nach Stufe, Brückenreparatur, GLA-Loch | gehört noch zu „original“, nicht zu „besser“; die Rekrutierungsregel hob den Anteil entschiedener Spiele von 49 % auf 61 % | Erhebung mittel/mittel, schwer/leicht |
| 4 | **Wirtschaft ohne Lager** als eigene Art „verbessert“: geht der Vorrat in Reichweite aus, Geldgebäude der Seite vorziehen (die Bauliste enthält sie), statt auf die Skriptfolge zu warten | offene Spiele enden mit leeren Lagern und Geld nahe null | Anteil offener Spiele, Geld über die Zeit (`--trace`) |
| 5 | **Heerwert statt fester Wellen** (Kampfwert je Ziel aus `WeaponSet`/`ArmorSet`) als eigene KI-Art auf dem Gerüst der Teams – die Lehre aus `docs/SIMULATION-CORE.md` §36.3 | die eingebaute KI gewinnt gegen „mittel“ 24 zu 17 | Erhebung Art gegen Art |
| 6 | **Die eingebaute `SkirmishAi` auf dasselbe Zustandsgerüst heben** (`Describe`/`Load`/Hash), damit Register E11 für beide KIs erledigt ist | E11 ist für die Original-KI gelöst, für die eingebaute offen | Speichern/Laden-Test wie §6.5 |
| 7 | **Sicht statt Allwissen** als Option der Original-KI (Platzieren nur auf Erkundetem, Feindwahl nur nach Gesehenem) | das Original kennt keinen Nebel; für Menschen fairer | Erhebung gegen die eingebaute KI |

## 9. Offen und nicht geprüft

- **Kein Mensch hat ein Spiel dieser KI gesehen**; der Client kann sie nicht wählen (§6.6). Ob Basen sinnvoll aussehen, Wellen als Wellen ankommen, Verteidigungen richtig stehen: unbekannt.
- **Kein Vergleich mit dem laufenden Originalspiel** (Bauzeiten, Wellenzeitpunkte, Sieganteile). „Wie das Original“ heißt hier: nach dem Quelltext umgesetzt, mit den Abweichungen aus §6.2.
- Standard bleibt die eingebaute KI. Die Umstellung des Standards ist eine eigene Entscheidung und ein eigener Commit des Projektinhabers (dann ändern sich die KI-Läufe des
  Referenzmanifests). Vorbereitet: `SkirmishAiKind`, `ScriptedSkirmishSession`, die Werkzeugoptionen.
- Mehr als zwei Spieler: ein Lauf mit acht (läuft ohne fehlende Typen, kein Sieger in 10 Spielminuten; Determinismus dort nicht eigens geprüft). Teams/Verbündete: nur das Anhängen ist
  getestet, kein Spiel. Mensch im Slot: §6.2 Punkt 9.
- 60 Hz: 16 Spiele als Information; 120 Hz nicht gelaufen.
- `ai-survey`-Spalten für die Original-KI: `commands` ist die Zahl ausgeführter Skriptaktionen, `firstAttack` der erste Tick mit einer eigenen Kampfeinheit näher als 400 an einem
  feindlichen Gebäude (die eingebaute KI meldet dort ihre eigene Wellenzählung) – zwischen den KI-Arten nicht vergleichbar.
- Die Schwierigkeitsauswahl der Shell (`docs/SIMULATION-CORE.md` §35: „eine Armee“) ist nicht angepasst; `docs/SCRIPT-ENGINE.md` und `docs/SIMULATION-CORE.md` sind nicht nachgeführt
  (Auftrag: nicht anfassen).

