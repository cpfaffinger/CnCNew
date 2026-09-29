# N+1-Handoff: Phase 5 – Restumfang, danach Phase 6 (Welt, Terrain, Bewegung, Sicht, Skirmish-KI)

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`. Lies zuerst `CLAUDE.md`, `README.md`,
`docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md`, `docs/GAME-RULES.md` und `docs/SIMULATION-CORE.md`.
`docs/HANDOFF-PHASE5.md` bleibt als Verlauf der Phase-5-Blöcke erhalten (Zahlen dort sind veraltet).

## Ehrlicher Status von Phase 5

Phase 5 ist **nicht vollständig abgenommen**. Das Abnahmekriterium („headless Referenzmatch aller Fraktionen ohne Sonderpfade“
und Golden-Master-Szenarien gegen bekannte Originalwerte) ist nur teilweise erfüllt:

- Erreicht: Wirtschaft, Sciences, Upgrades, direkter Kampf, einfache Bewegung, Produktion, Garrison/Transport, Erzeugungslisten,
  Kisten, Spezialkraft-Subsystem, „letzter Überlebender“ als Sieg. Der Referenzmatch läuft auf Retail-Daten mit allen 13 Fraktionen
  und deren **echten Kommandozentralen**, deterministisch, mit Replay und Snapshotfortsetzung.
- Sonderpfade im Referenzmatch: 13 Startobjekte werden übersprungen (9 Dozer, 4 GLA-Arbeiter, weil deren KI-Module fehlen), es gibt
  kein Supply (Geld nur aus Startkapital und Kopfgeld/Kisten) und der Generator gibt keinen `UseSpecialPowerCommand` aus.
- Fehlt: Teams/Diplomatie, Supply/Transport der Wirtschaft, Reparatur/Verkauf, Radarwirkung, Flächenschaden, Erfahrung und
  veteranenbasierte `ArmorSet`/`WeaponSet`-Auswahl, Dozer-/Arbeiter-KI, Command-Set-Wirkung.
- Fehlt: Golden Master für Supply und Erfahrung; Block 0.4 (RNG-Abgleich mit einem kompilierten Original, kein Compiler verfügbar).
- Bewusste Vereinfachungen der Spezialkräfte (`docs/GAME-RULES.md` §6d): kein Konstruktionsmodell, kein Disabled-Zustand, kein
  `createOwner=false`, keine `OCLAdjustPositionToPassable`-Anpassung, kein Kanonenboot-Anflug/-KI, keine View-Objekte, kein EVA/Ton,
  keine Skriptbenachrichtigungen, Kopfgeld ohne Teammodell, `SpecialPowerCompletionDie` ist inert, die drei Randpositionen
  (`CREATE_AT_EDGE_*`) erzeugen nichts (`CreationSkipped(NoTerrainEdge)`), lösen aber Kraft und Nachladung aus.

Entscheidung für den nächsten Durchlauf: **Zuerst den Phase-5-Rest schließen, soweit er ohne Terrain möglich ist**
(Abschnitt „Teil A“), dann Phase 6 (Teil B) beginnen. Was Terrain/Kollision braucht, gehört nach Phase 6, nicht in
Phase-5-Ersatzlösungen.

## Reproduktionsbasis (Stand Ende des Spezialkraft-Blocks; vor dem Weiterarbeiten neu messen)

Commits des letzten Blocks: `5f34b41` (Schema/Katalog/Baseline), `9efbf2c` (Subsystem, Befehl, Module), `1de0b6c` (Tests);
danach die Dokumentation. Alle lokal, kein Remote.

- Build: 0 Fehler, genau die 10 bekannten NU1901/NU1903-Warnungen. Alle Testprogramme grün, auch `--long` im Release.
- Vollinventar: 228 INI-Dateien (28 ausgeschlossen), 65.875 Blöcke, 12.574 Definitionen, 16.380 Module, 333.061 Schlüssel,
  18.050/19.600 Referenzen aufgelöst (1.550 Fallbacks), Snapshot 144.745.050 Byte, SHA-256
  `D0C06A2B06CD7065474A3EB729E36B87E1815DAE2D461E564D023EAA019295CF`.
- Retail-Basis (`--maps exclude`): 146 Dateien (110 ausgeschlossen), 63.747 Blöcke, 12.550 Definitionen, 16.065 Module,
  327.293 Schlüssel, 2.115 Objekte, 17.658/19.181 Referenzen, SHA-256
  `7110C5BA69917279ADD6B0270754AA081ADEF2DC37166FFB598CAF2313F979DA`.
- Kompatibilitätsbasislinie 761 Einträge (Vollinventar: deferred-field 519 Einträge/20.936 Verwendungen, definition 36/2.590,
  diagnostic 9/4.907, duplicate-key 24/120, excluded-file 28/28, module 145/4.441); `compat-check` besteht.
- Modulkatalog 205 Typen, **60 implementiert** (12 Phase 4, 24 + 14 + 10 Phase 5); Instanziierbarkeit **1.224 von 2.115
  (57,9 %)**, 891 blockiert; Modulverwendungen 15.078 von 19.553 (77,1 %). Asset-Runtimebestand 25.682 Dateien.
- Stresstest (`SimTool stress`, 216.000 Ticks): Endhash `4A99EE46FD623A09`, Kettenhash `76A8FCEEEE625672`, 914.394 Befehle,
  Spitze 15.775 Entities, Snapshot bei Tick 108.000 mit 10.262.573 Byte, Restore-Hash `1D2E96E0BB12303A`, 0 B/Tick im Leerlauf.
- Referenzmatch (`SimTool reference-match`, Retail): 13 Fraktionen, 13.978 Befehle (625 Produktion, 2.153 Bewegung, 10.875
  Schüsse), Sieger Spieler 1 (Amerika) bei Tick 11.385 (6,3 Min.), Replay 11.686 Ticks/462.194 Byte/11 Checkpoints, 43
  `UnknownEntity`-Ablehnungen, Endhash `83EBD54549BB1DB5`, Kettenhash `928D78E6DD172836`, Fortsetzung ab Tick 5.843 identisch.
- Befehle: 17 Typen, `CommandTypes.Version = 5`, Replay-Format v2; Ablehnungscodes 1–29 (25–29 Spezialkraft).

## Teil A – Rest von Phase 5 (in dieser Reihenfolge; jeweils Tests, Realdatenaudit, Doku, zwei Läufe)

Miss vor jedem Schritt mit `SimTool audit-coverage .local/phase2-import-a`, was ein Modul **allein** freischaltet. Stand
(Objekte insgesamt / nur dieses Modul fehlt): `CommandButtonHuntUpdate` 79/10, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/2,
`JetSlowDeathBehavior` 59/0, `MissileAIUpdate` 44/29, `CrushDie` 42/39, `CountermeasuresBehavior` 40/0, `FireWeaponUpdate` 36/26,
`SpawnBehavior` 35/6, `AutoFindHealingUpdate` 34/10, `JetAIUpdate` 34/2, `HeightDieUpdate` 31/15, `DeliverPayloadAIUpdate` 28/1,
`MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `TransportAIUpdate` 24/13, `BaikonurLaunchPower` 23/23,
`ReplaceObjectUpgrade` 20/16, `SlavedUpdate` 20/7, `RepairDockUpdate` 17/16, `DozerAIUpdate` 10/10.

1. **Supply-Wirtschaft** (`SupplyCenterDockUpdate`, `SupplyWarehouseDockUpdate`, `SupplyTruckAIUpdate`, `WorkerAIUpdate`,
   `DozerAIUpdate`, `DockUpdate`-Basis): Erst die Anforderungen am Original lesen (`references/`), dann als eigenes
   Dock-/Wirtschaftsmodell mit Katalogschemata bauen. Danach die 13 übersprungenen Startobjekte des Referenzmatches einsetzen und
   Golden Master für Supply-Einnahmen mit unabhängiger Nachrechnung ergänzen. Das ist der größte offene Punkt für das
   Abnahmekriterium „ohne Sonderpfade“.
2. **Erfahrung und Veteranenstufen**: Erfahrungsvergabe beim Kill, `ExperienceTracker`, `ArmorSet`/`WeaponSet`-Auswahl nach
   Stufe, Golden Master.
3. **Teams/Diplomatie** als Datenmodell (Kopfgeld und Siegregel auf Teams heben, siehe „Kopfgeld ohne Teammodell“).
4. **Reparatur/Verkauf** (`RepairDockUpdate`, `AutoFindHealingUpdate` – Achtung: das Fixture `Blocked` in
   `tests/CnCNew.Simulation.Tests/Tests.Basics.cs` benutzt `AutoFindHealingUpdate` als Beispiel für ein nicht implementiertes
   Modul; auf ein anderes umstellen, sobald es implementiert ist).
5. **Weitere Modulfreischaltungen nach Messung**, ohne Terrain/Kollision: `CommandButtonHuntUpdate`, `SpecialAbilityUpdate`
   (Handler der Einheitenfähigkeiten, erst dadurch werden die `SpecialAbility`-Kräfte im Befehlsstrom nützlich),
   `MissileAIUpdate`, `FireWeaponUpdate`, `ReplaceObjectUpgrade`, `CountermeasuresBehavior`, `JetAIUpdate`. Den
   Referenzmatch-Generator um `UseSpecialPowerCommand` erweitern, sobald sinnvolle Ziele existieren.
6. **Radarwirkung, Flächenschaden**, Command-Set-Wirkung (welche Buttons ein Objekt anbietet).
7. Block 0.4 (RNG-Abgleich) bleibt offen, solange kein kompiliertes Original zur Verfügung steht; nicht raten.

Erst wenn 1–3 stehen und der Referenzmatch ohne übersprungene Startobjekte läuft, darf Phase 5 als abgeschlossen
dokumentiert werden; bleiben Grenzen, benenne sie in `IMPLEMENTATION-PLAN.md` ehrlich.

## Teil B – Phase 6: Welt, Terrain, Bewegung, Sicht, Skirmish-KI

Umfang laut `docs/IMPLEMENTATION-PLAN.md` §6: MAP-Daten zu spielbarer Welt (Heightfield, Wasser, Bridges, Waypoints,
Triggerbereiche, Props), Footprints, Kollision, Bodentypen, Formationen, deterministische Pfadsuche mit lokaler Vermeidung,
Sichtweite/Shroud/Fog/Tarnung, Skirmish-KI (Build-Pläne, Angriff, Verteidigung, Fähigkeitsnutzung). Damit werden auch die
Terrain-/Physik-Module möglich, die in Phase 5 bewusst offen blieben (`ToppleUpdate` 76/66, `CrushDie` 42/39,
`HeightDieUpdate` 31/15, echte `PhysicsBehavior`-Kräfte, `OCLAdjustPositionToPassable`, `CREATE_AT_EDGE_*`-Positionen).

Regeln für die Schichtung: Terrain und Navigation als Datenmodell in `CnCNew.Data`/`CnCNew.Simulation` (ganzzahlig/Festkomma,
keine Gleitkommazahlen, `SourceRules` beachten); Kartenformate über `CnCNew.Formats`/`CnCNew.Import`, Laufzeitzugriff nur über
`CnCNew.Content`. Die Karten-Overlays der Vollinventarmessung (`--maps include`) sind der Einstieg.

## Nicht verhandelbare Regeln (unverändert, siehe `CLAUDE.md`)

- Unbekannte Schlüssel/Module/Enums/Referenzen fallen nie still auf Standardwerte zurück; jeder Fall ist explizit, gezählt,
  dokumentiert und gegen `baselines/compatibility.json` abgesichert.
- Determinismus: kanonische Sortierung, keine Wandzeit, keine `Dictionary`-/`HashSet`-Reihenfolgeabhängigkeit; zwei unabhängige
  Läufe byteidentisch. `CnCNew.Simulation` hängt nur von `CnCNew.Data` ab (`SourceRules`-Test); jedes `IUpdateModule` enthält
  `nextWake`.
- `TreatWarningsAsErrors=true`; nur die zehn bekannten NU1901/NU1903-Warnungen sind erklärt.
- Bestandszahlen immer neu messen, nie aus Docs oder diesem Handoff übernehmen.
- Fremder Code darf gelesen, nicht unbesehen kopiert werden; jeder portierte Algorithmus braucht einen Quellverweis (Datei,
  Funktion).
- Neue Befehle erhöhen `CommandTypes.Version` (aktuell 5) und bekommen eigene Codes; bestehende ändern sich nie.
- Nur lokal arbeiten, kein Remote, nichts pushen; mehrere fachlich geschlossene Commits; Working Tree am Ende sauber;
  am Ende dieses Durchlaufs `docs/HANDOFF-PHASE7.md` (oder einen neuen Fortsetzungsauftrag) schreiben und committen.

## Abschlussbericht

Zahlen (mit Commit-Hashes), implementierte Regeln/Module, Determinismus-, Test-, Stress- und Replayergebnisse,
Modulabdeckung und Instanziierungsquote vorher/nachher mit Blockerliste, Golden-Master-Herleitungen, bekannte Grenzen und
technische Schulden, `git status` und Bestätigung „kein Remote, nichts gepusht“.
