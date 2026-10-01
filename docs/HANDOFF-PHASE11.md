# N+1-Handoff: Phase 9 abschließen (Fortsetzung) – Skript-Restfamilien, Kampagnen-Shell-Feinschliff, Missions-HUD

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`. Lies zuerst `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`,
`docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§9 „Status“ und „Bekannte Grenzen“), `docs/SCRIPT-ENGINE.md` (besonders §8/§9),
`docs/CAMPAIGN-AND-SAVEGAMES.md`, `docs/MAP-SCRIPTS.md`, `docs/SIMULATION-CORE.md` (§12, §19–§25), `docs/GAME-RULES.md` §10/§11,
`docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`. `docs/HANDOFF-PHASE9.md` und `docs/HANDOFF-PHASE10.md` bleiben als
Verlauf; ihre Zahlen sind veraltet, ihre offenen Punkte stehen – überarbeitet und mit aktuellem Stand – unten.

Dieser Auftrag setzt weiterhin `IMPLEMENTATION-PLAN.md` §9 fort. **§9 ist noch nicht abgeschlossen:** die Abnahme verlangt „alle
Skirmish-Karten start- und beendbar, jede Kampagnenmission besteht ihre wesentlichen Erfolgs- und Fehlerpfade, Speichern/Laden setzt
einen identischen Simulationszustand fort". Skirmish-Start/-Ende und Speichern/Laden sind jetzt belegt; **der Rest – Missionen
bestehen ihre Erfolgs-/Fehlerpfade auf dem vom Skript vorgesehenen Weg, nicht nur per Auslöschungsprobe – ist der Kern dieses Auftrags.**

## Ehrlicher Status (Ausgangslage, Stand 1. Oktober 2026)

Vier parallele Pakete sind seit `docs/HANDOFF-PHASE10.md` gemerged (Commits `7579d8f`…`b5d618d` Shell-Anbindung, `01d3d72`…`c4d1128`
Kollision, `849aa07`…`1cf7e84` Skript-Befehlsknopf-Fähigkeiten, `fb17f71`…`3be6684` Bau-/Regelgrenzen; vier Merge-Commits
`b5d618d`/`d96ca36`/`bc0ca37`/`6daba54`). Konflikte in `baselines/compatibility.json`, `docs/SIMULATION-CORE.md` (§23–§25),
`docs/CAMPAIGN-AND-SAVEGAMES.md` und zwei fest kodierten Testerwartungen wurden von Hand aufgelöst; die komplette Testmatrix (zwölf
Testprogramme, synthetisch und mit `--content-dir`) ist grün.

**Fertig und gemessen:**

- **Kollision**: `ToppleUpdate`, `StructureToppleUpdate`, `HeightDieUpdate`, echtes `CrushDie`/`SquishCollide` über
  Fußabdrucküberlappung, eine bewusst einfache lokale Ausweichbewegung (`docs/SIMULATION-CORE.md` §23).
  Instanziierbarkeit dadurch 70,6 % → 75,1 %.
- **Skript-Befehlsknopf-Fähigkeiten**: `NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)`, `SpecialAbilityUpdate` als ehrliche Hülle,
  wählbare lokale Seite für Karten ohne `playerIsHuman` (`docs/SIMULATION-CORE.md` §24, `docs/SCRIPT-ENGINE.md` §5/§7).
- **Bau-/Regelgrenzen**: Voraussetzungs-/Fähigkeitsprüfung in `QueueProduction`, `MaxSimultaneousOfType`, `OverchargeCommand`,
  reduzierter `CaptureCommand`, Veteranen-`ArmorSet`/`WeaponSet`, Höhen-Sichtlinie für Detektoren, die fehlenden Lese-APIs
  (`CheckConstruct`, `ProductionUpdate.Queue`, `AvailableOnFrame`, `UpgradeMuxModule.Executed`, `IsDock`); `CommandTypes.Version`
  9 → **10** (neue Codes 28/29, Ablehnungsgründe 35–38). Alle 64 Skirmish-Karten mit ≥2 Startpositionen starten und enden fehlerfrei
  (`SimTool skirmish-sweep`); die 16-Karten-KI-Übersicht ist neu vermessen (`docs/SIMULATION-CORE.md` §25). Skirmish-Speicherstände
  sind explizit nicht unterstützt (`SkirmishMatchSession.CreateSave()` wirft `NotSupportedException`, getestet) – Replay/Checkpoint
  deckt abgeschlossene/laufende Skirmish-Partien bereits ab.
- **Kampagnen-Shell**: Hauptmenü → Fraktions-/Challenge-Auswahl → Schwierigkeit → echte Mission mit echtem Briefingtext; Ladeliste,
  Speichern im Spiel, Missionsabschluss (Sieg/Niederlage); `ScriptOutput`-Wiedergabe (Kamera, Text, Radar) spielt tatsächlich und
  meldet über `CompletePresentation`/`SelectNamed`/`SignalUiInteraction` an die VM zurück – mit Test gegen eine echte, zeitgesteuerte
  Kamerabewegungssequenz (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7).

**Nach dem heutigen Merge aller vier Pakete frisch gemessen** (vor dieser Phase neu zu bestätigen, insbesondere `CampaignTool audit`,
das die Bau-/Regelgrenzen-Änderungen noch nicht gesehen hat – die letzte Messung in `docs/SCRIPT-ENGINE.md` §9 stammt nur vom
Skript-Befehlsknopf-Paket):

- Modulkatalog **91 von 205** implementiert; Instanziierbarkeit **1.624 von 2.115 (76,8 %)**; Modulverwendungen abgedeckt
  **15.843 von 19.553 (81,0 %)**; Kompatibilitätsbasislinie **760** Einträge (`IniTool compat-baseline`, neu geschrieben, geprüft).
- Retail-Referenzmatch (`SimTool reference-match`): Sieger FactionGLA bei Tick 19.200, Endhash `28BE35C1174E369F`; zwei frische Läufe
  und eine Fortsetzung ab Tick 9.750 liefern identische Hashes (Sieger und Hash ändern sich gegenüber früheren Ständen, weil die
  Zufallsproduktion des Szenarios jetzt Voraussetzungen respektiert – kein Regressionsbefund, siehe `docs/SIMULATION-CORE.md` §25).
- `dotnet build CnCNew.slnx`: 0 Fehler, 21 bekannte Warnungen bei **sauberem** `CnCNew.Client.Stride`-`bin`/`obj`.
- Alle zwölf Testprogramme aus `CLAUDE.md` bestehen, synthetisch und mit `--content-dir .local/phase2-import-a`.

## Nicht erledigt (Auftrag dieser Phase)

1. **Die `SKIRMISH_*`-Skriptfamilie entscheiden** (seit `docs/HANDOFF-PHASE10.md` aufgeschoben, immer noch offen): Mit Abstand die
   häufigsten fehlenden Bedingungen auf den Challenge-Karten (`SKIRMISH_PLAYER_HAS_UNITS_IN_AREA` 37.227 Treffer,
   `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` 37.220, `SKIRMISH_SPECIAL_POWER_READY` 36.907, `SKIRMISH_VALUE_IN_AREA` 15.432 –
   `docs/SCRIPT-ENGINE.md` §9). Zwei gangbare Wege, beide akzeptabel, aber **triff die Entscheidung und dokumentiere sie**:
   a) die `Skirmish`-Familie der Bedingungen/Aktionen portieren (Quelle: `GeneralsMD/.../ScriptEngine/ScriptConditions.cpp`/
   `ScriptActions.cpp`, Suche nach `SKIRMISH_`); b) die bestehende, bereits funktionierende `SkirmishAi` als Ersatz für
   Challenge-Gegner-KI einsetzen (prüfen, ob sie heute schon in Missionen eingehängt werden kann, oder was fehlt).
2. **Die restlichen, am häufigsten fehlenden Aktionen** (Reihenfolge nach Treffern, `docs/SCRIPT-ENGINE.md` §9, nach dem
   Bau-/Regelgrenzen-Merge neu zu messen, da sich die Rangfolge verschieben kann):
   - `TEAM_GARRISON_SPECIFIC_BUILDING`/`TEAM_GARRISON_NEAREST_BUILDING`/`TEAM_/NAMED_GARRISON_*`-Familie, `*_EXIT_*`,
     `NAMED_ENTER_NAMED` (Garnison – nutzt die bestehende `GarrisonContain`/`TransportContain`-Infrastruktur, aber ohne
     skriptgesteuerte Auslösung).
   - `MAP_REVEAL_ALL_PERM`/`MAP_REVEAL_AT_WAYPOINT`/`MAP_SHROUD_*` (braucht dauerhafte Aufdeckung in
     `src/CnCNew.Simulation/World/SimulationWorld.Vision.cs` – heute nur tickbasierte Sicht, keine "permanent enthüllt"-Markierung;
     kleine additive Erweiterung dort, kein Umbau).
   - `BUILD_TEAM`/`TEAM_MERGE_INTO_TEAM`/`RECRUIT_TEAM` (muss in die bestehende Produktions-/Team-Infrastruktur einhängen, jetzt mit
     den neuen Voraussetzungsprüfungen aus Phase 10 – prüfen, ob ein Skript-ausgelöster Bau dieselben Regeln respektieren soll wie
     ein Spielerbefehl, vermutlich ja).
   - `WAREHOUSE_SET_VALUE`, `TEAM_SET_OVERRIDE_RELATION_*`, `OBJECT_ALLOW_BONUSES`,
     `PLAYER_ENABLE_BASE_CONSTRUCTION`/`PLAYER_ENABLE_UNIT_CONSTRUCTION` (braucht einen Baufreigabe-Schalter pro Spieler, den es
     heute nicht gibt – kleine additive Ergänzung an `SimulationWorld`), `PLAYER_AFFECT_RECEIVING_EXPERIENCE`,
     `PLAYER_PURCHASE_SCIENCE`.
   - `NAMED_USE_COMMANDBUTTON_ABILITY_(AT_WAYPOINT|USING_WAYPOINT_PATH)`, `TEAM_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT`,
     `TEAM_USE_COMMANDBUTTON_ON_NEAREST_*` (Pfadvarianten der in Phase 10 gebauten Befehlsknopf-Fähigkeiten).
3. **Fehlende Bedingungen jenseits der `SKIRMISH_*`-Familie**: `PLAYER_TRIGGERED_SPECIAL_POWER`/`*_COMPLETED_*`/`*_MIDWAY_*` (braucht
   ein `SpecialPowerUsed`-Ereignis – prüfen, was die Simulation beim Auslösen einer Spezialkraft heute schon als `SimEvent` meldet),
   `TYPE_SIGHTED`, `NAMED_DISCOVERED`/`TEAM_DISCOVERED` (Sicht-/Entdeckungsbedingungen, nutzen vermutlich `VisionMap` direkt).
4. **Unbekannte Befehlsknöpfe aus `docs/SCRIPT-ENGINE.md` §9 klären**: `Command_GLAInfantryJarmenKellSnipeVehicleAttack` (40 Treffer,
   vermutlich `FIRE_WEAPON`/`SWITCH_WEAPON`-Fähigkeit, keine Spezialkraft) und drei `Command_Upgrade*`-Knöpfe (`PLAYER_UPGRADE`,
   nicht `SPECIAL_POWER`) – `CommandCatalog.TryGetSpecialPowerAbility` soll diese Fälle sauber als "andere Befehlsart" statt als
   Fehler behandeln, mit eigenem gezähltem Pfad.
5. **Reinforcement mit Transport** (`teamTransport`, `teamStartsFull`, `CINE_*`-Flugzeuge, Fallschirm) und **echte Formationen/
   Angriffsbewegung** (`*_FOLLOW_WAYPOINTS` mit `asTeam`) – blockiert mehrere Missionen vollständig (`docs/SCRIPT-ENGINE.md` §9:
   Verstärkungstransport ist nicht simuliert).
6. **Generals'-Challenge-General-Wahl in der Shell**: `ChallengeSelectScreen` muss den gewählten General noch auf den Seitennamen
   der jeweiligen Karte abbilden (pro Karte verschieden, z. B. `PlyrLaserGeneral` → `GC_LaserGeneral`) und über
   `StartCurrentMission(humanSideName:)` durchreichen (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7, „Noch offen“). Die Engine-Seite steht.
7. **Eine Missions-HUD** (Kamera/Auswahl/Befehlsleiste) parallel zu `LiveMatch`/`MatchController`, die bleiben Skirmish-typisiert.
   `MissionPresentationDirector`/`MissionUiBridge`/`MissionPauseMenu` sind bewusst headless testbare, renderer-neutrale Bausteine
   dafür (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7), aber kein vollständiger Ersatz: eine Kampagnenmission lässt sich heute von der
   Shell aus starten und beenden, aber nicht mit einer echten Befehlsleiste/Produktion spielen. Prüfen, ob `MatchController` dafür
   erweitert oder ein eigener `MissionController` gebaut wird.
8. **`PresentationLatencyTicks` fehlt als Parameter** in `CampaignController.StartCurrentMission` (bekannte Grenze aus Phase 10,
   `docs/CAMPAIGN-AND-SAVEGAMES.md` §7) – ein kleiner additiver Parameter wäre sinnvoll, falls ein künftiger Host keinen
   `MissionPresentationDirector` mitbringt.
9. **Nach jeder Aktions-/Bedingungsgruppe:** `ScriptCoverage` aktualisieren, `docs/SCRIPT-ENGINE.md` §4/§5/§8/§9 neu schreiben
   (Tabelle, Prozentsätze, häufigste Lücken), `CampaignTool audit --ticks 900` und `playthrough` neu laufen lassen, Vorher-/
   Nachher-Zahlen festhalten. **Ziel:** jede der 24 Kampagnenkarten erreicht Sieg und Niederlage auf dem vom eigenen Skript
   vorgesehenen Weg (nicht nur per `MissionProbe`-Auslöschung); wo das nach bestem Bemühen nicht gelingt, ehrlich benennen warum
   (fehlende Bedingung/Aktion, Kartenüberlagerung fehlt, etc.), nicht stillschweigend bei der Auslöschungsprobe stehen bleiben.

## Nicht verhandelbare Regeln (unverändert, siehe `CLAUDE.md`)

- Unbekannte Schlüssel/Module/Enums/Referenzen fallen nie still auf Standardwerte zurück; jeder Fall ist explizit, gezählt,
  dokumentiert und gegen `baselines/compatibility.json` abgesichert. Neue Skriptaktionen/-bedingungen: in `ScriptCoverage`
  eintragen (Test `ScriptSupportTableIsConsistent` prüft die Namen), Quellfunktion zitieren (`ScriptActions.cpp`/
  `ScriptConditions.cpp`, nie pauschal), Test in `tests/CnCNew.Simulation.Tests/Tests.Scripting.cs`, Zeile in
  `docs/SCRIPT-ENGINE.md` §4/§5 (Abweichungen in §8). Neue Module: Katalogschema, Bindung, Test, Eintrag in
  `docs/SIMULATION-CORE.md`, Baseline (`IniTool compat-baseline`) – Änderungen daran sind Review-Gegenstand.
- Determinismus: sortierte Sammlungen, keine Wandzeit, kein `float`/`double`, kein `Random.Shared`; `SourceRules` gilt für die
  gesamte Skript-VM. Jeder neue Skriptzustand gehört in `ScriptEngine.Describe`/`Load`, sonst weicht eine wiederhergestellte
  Mission ab (der Fortsetzungstest fängt das).
- `TreatWarningsAsErrors=true`; 21 erklärte Warnungen sind der Referenzwert bei einem **sauberen** Build (siehe `CLAUDE.md`); jede
  andere neue Warnung ist ein Fehler, bis sie erklärt ist.
- Snapshot-/Replay-/Missionsformate: Änderungen erhöhen die jeweilige Version (`WorldSnapshot` 7, Replay 7, `MissionSnapshot` 2,
  `CommandTypes.Version` 10); ältere werden mit klarer Meldung abgewiesen.
- Fremder Code nur lesen, nicht kopieren; jede portierte Aktion/Bedingung einzeln mit Datei und Funktion zitieren. Das gilt
  besonders für die `SKIRMISH_*`-Familie, falls du dich für Port statt Ersatz entscheidest.
- Bestandszahlen immer neu messen, nie aus diesem Dokument übernehmen – insbesondere `CampaignTool audit`, das nach dem
  Bau-/Regelgrenzen-Merge noch nicht neu gelaufen ist.
- Nur lokal arbeiten, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber; am Ende
  `docs/HANDOFF-PHASE12.md` schreiben und committen.
- **Zeilenenden erhalten:** das Repo ist überwiegend LF, einzelne Dateien (`README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`,
  `docs/IMPLEMENTATION-PLAN.md` u. a.) sind CRLF; beim Skript-Editieren `newline=''` verwenden, `git diff --stat` prüfen.
- Praktisch: Bei paralleler Arbeit an mehreren Agenten/Worktrees teilen sich `ScriptEngine.Actions.cs`/`ScriptEngine.Conditions.cs`
  dieselbe Switch-Anweisung – **das ist der häufigste Merge-Konflikt dieser Phase.** Entweder nacheinander arbeiten oder vorher
  genau festlegen, welcher Agent welchen Aktions-/Bedingungsblock bekommt, und nach dem Mergen `baselines/compatibility.json`
  sowie die fest kodierte Modulliste in `tests/CnCNew.Data.Tests/Program.cs` (`ModuleCatalogIsExplicitAndConsistent`) von Hand
  neu abgleichen (beides ist in dieser Phase schon zweimal in Konflikt geraten). Große Editskripte als Datei im Scratchpad
  ausführen statt komplexer Heredocs; ein laufender Audit-Prozess sperrt die Release-DLLs; `tools/CnCNew.UiRenderTool` bleibt
  außerhalb der Solution.

## Abschlussbericht

Zahlen mit Commit-Hashes, implementierte Skriptaktionen/-bedingungen mit Quellverweisen, die `SKIRMISH_*`-Entscheidung und ihre
Begründung, Determinismus-/Test-/Fortsetzungsergebnisse, die `CampaignTool audit`-Tabelle vorher/nachher (insbesondere wie viele
Missionen jetzt Sieg/Niederlage auf dem vorgesehenen Weg statt per Auslöschungsprobe erreichen), Instanziierungsquote und
Blockerliste vorher/nachher, bekannte Grenzen, `git status` und Bestätigung „kein Remote, nichts gepusht“.
