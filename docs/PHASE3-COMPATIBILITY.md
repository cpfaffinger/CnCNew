# Kompatibilitätsliste (Phase 3, als Basislinie abgesichert in Phase 4, fortgeschrieben in Phase 5)

Stand: 28. September 2026

Der Compiler ignoriert unbekannte Semantik nie. Nicht benötigte Definitionen, noch nicht
implementierte Simulations-/Präsentationsmodule, zurückgestellte Objekt-/Modulschlüssel, wiederholte
Skalarschlüssel, Diagnosecodes und ausgeschlossene Dateien werden als `CompatibilityEntry` mit Kategorie,
Name, Häufigkeit, Besitzerart und geplantem Ziel in jedem kanonischen Snapshot geführt. Seit Phase 4 ist die
Liste als **geprüfte Basislinie** eingecheckt (`baselines/compatibility.json`); Phase 5 hat sie bewusst mehrfach
neu erzeugt, sobald sich getypte Felder verschoben haben (zuletzt **746 Einträge**, keine Assetinhalte), und
prüft sie gegen Drift:

```powershell
dotnet run --project tools/CnCNew.IniTool -- compat-check .local/content baselines/compatibility.json
```

Der Befehl endet bei jedem neuen (`NEW`), fehlenden (`MISSING`) oder in Häufigkeit, Besitzerart oder Ziel geänderten
(`CHANGED`) Eintrag mit Exitcode 1; der Realdatentest von `CnCNew.Data.Tests --content-dir` prüft dasselbe. Die Basislinie
erzeugt `IniTool compat-baseline`; jede Änderung ist Review-Gegenstand (Vollinventarmodus: alle Kartenoverlays, strikter
Modulkatalog, Retail-Ladeplan).

Kategorien (frisch gemessen, Vollinventar): `definition` (38 Typen, 2.899 Verwendungen), `module` (178 Modultypen,
10.229 Verwendungen – 27 Typen sind inzwischen implementiert, siehe unten), `deferred-field` (184 Schlüssel, davon 37
Objektschlüssel und 147 neue Phase-5-Modul-/Domänenfelder — Weapon/Armor/Upgrade/Science/Locomotor/**Faction**,
12.921 Verwendungen), `duplicate-key` (24 Schlüssel, 120), `diagnostic` (9 Codes, 10.567 Verwendungen) und
`excluded-file` (28 Dateien).

## Definitionsdomänen außerhalb des Typvertrags

38 Typen (2.899 Verwendungen) sind vollständig syntaktisch und werttypisiert erhalten, ihre fachspezifischen Verträge folgen in
den angegebenen späteren Phasen:

- Simulation/Welt/KI (Phase 4–6): AIData, Bridge, Campaign, ChallengeGenerals, CrateData, MapCache, ObjectCreationList, Rank,
  Road, Terrain, WaterSet, WaterTransparency und Weather (ScriptAction/ScriptCondition gehören zu `Scripts.ini`, die nur das
  WorldBuilder-Werkzeug lädt und daher ausgeschlossen ist).
- Präsentation/UI (Phase 7–8): Animation, AudioSettings, CommandMap, ControlBarResizer, ControlBarScheme, Credits,
  DrawGroupInfo, DynamicGameLOD, EvaEvent, HeaderTemplate, InGameUI, Language, MappedImage, MiscAudio, Mouse, MouseCursor,
  MultiplayerColor, MultiplayerSettings, MultiplayerStartingMoneyChoice, OnlineChatColors, ShellMenuScheme, StaticGameLOD,
  Video, WebpageURL und WindowTransition.

Compat-Definitionen haben IDs der Form `compatibility://<typ>/<name>`; sechs gleichnamige Definitionen verschiedener
Typen verschmelzen damit nicht mehr.

## Modul-/Behaviorbestand

Der Katalog `ModuleCatalog` führt alle **205** Modultypen des ausgelieferten Bestands (eine frühere Zählung von 206 enthielt
das Phantommodul `ArmorSet`). **74** sind implementiert (Stand Abschluss Phase 5): die 12 aus Phase 4 plus 62 aus Phase 5 – die
vollständige Liste gibt `SimTool audit-modules <content-dir>` aus, Details und Vereinfachungen stehen in `docs/GAME-RULES.md`. Die übrigen
131 stehen in der Basislinie (davon 22 reine Präsentationsmodule ohne Simulationsbezug) mit Häufigkeit, Besitzerart und Ziel; eine Liste der Verwendungen je Modul liefert
`SimTool audit-modules <content-dir>`. `StrictUnknownModules` scheitert an jedem Namen außerhalb des Katalogs, sodass ein
Tippfehler von einem bekannten, noch nicht implementierten Modul unterscheidbar ist.

## Sichtbare Sonderfälle

- `GeometryMajorRadius = 18.5.0` und `GeometryHeight = 40v.0` sind explizite Compat-Werte (`shipped-double-decimal-radius`,
  `shipped-real-trailing-text`, beide wie `sscanf("%f")` gelesen) und in `compatibility-value` gezählt (2).
- Negative Dauern (`MinLifetime = -1`) wickeln modulo 2^32 wie `sscanf("%u")` (`compatibility-unsigned-wrap`, 10).
- Überzählige Token hinter Einzelwerten (`compatibility-ignored-tokens`, 3).
- Wiederholte Skalarschlüssel im selben Block: der letzte gewinnt (`compatibility-duplicate-key`, 120 Verwendungen in 24
  Schlüsseln; wiederholbare Schlüssel wie `Armor` oder `Locomotor` behalten ihre Vielfachheit und stehen nicht hier).
- Referenzen: 1.550 Verwendungen sind sichtbare Fallbacks im Vollinventar (1.523 in den Basisdaten; neu gemessen; die zusätzlichen echten Referenzen aus der
  Phase-5-Domänentypisierung, siehe `TYPED-DEFINITIONS.md`, lösen alle auf und ändern diese Zahl nicht) – ganz
  überwiegend `compatibility-engine-veterancy-upgrade` (`Upgrade_Veterancy_ELITE`/`_HEROIC`, engineinterne Namen ohne
  INI-Definition, über das vom `DefaultThingTemplate` geerbte `AutoHealBehavior`) und eine kleine Zahl
  `compatibility-unresolved-reference` (`DieByFireUSA`, `Boss_VehicleSentryDrone`); kein stiller Ersatz durch ein anderes
  Ziel.
- **Phase-5-Parserkorrektur:** `SageIniParser` erkannte das `End`-Schlüsselwort nur, wenn die gesamte Zeile exakt `End`
  lautete; das Original (`INI::initFromINIMulti`) vergleicht nur das erste `strtok`-Token und ignoriert den Rest der
  Zeile. Zwei Objekte im Bestand tragen den Tippfehler `End Upgrade_AmericaHallfireDrone`
  (`AmericaVehicle.ini`, `BossGeneral.ini`); ohne die Korrektur blieb der Block offen und verschluckte alle folgenden
  Geschwisterblöcke bis zum nächsten `End`, was unbemerkt blieb, solange kein implementiertes Modul die verschluckten
  Felder validierte. Die Korrektur ändert die Objektzahl (2.113 → 2.115) und den Definitionsfingerabdruck.
- Das Vollinventar wendet alle Kartenoverlays gemeinsam an. Dadurch fehlen Ziele von `ReplaceModule` (52,
  `compatibility-missing-replace-target`) und `RemoveModule` (51, `compatibility-missing-remove-target`, neu gemessen),
  und wiederholte Tags aus mehreren Overlays treten auf (`compatibility-duplicate-module-tag`, 95, neu gemessen; 18 davon
  in den Basisdaten). Ein einzelnes Overlay behält korrekte Semantik; `--maps exclude` liefert die Basisdaten ohne diese
  Aggregatartefakte.
- Vier `AddModule`-Blöcke enthalten kein Modul (`compatibility-add-module-without-module`); ihre Felder wirken auf das
  Objekt, wie im Original.
- Ausgeschlossene Dateien (28) stehen mit Grund in der Liste: Debug-Dateien (`GameDataDebug.ini`, `CommandMapDebug.ini`,
  `CommandMapDemo.ini`), die WorldBuilder-Datei `Scripts.ini`, nicht referenzierte Dateien (`MemoryPools.ini`,
  `Default/ButtonSets.ini`, `Default/Webpages.ini`, Installer-`Options.ini`) und 20 Sprachvarianten.

## Stand nach Phase 6

Phase 6 hat `Locomotor/Surfaces` und `GameData/SellPercentage` streng typisiert und Katalogschemata für `PowerPlantUpdate`, `PowerPlantUpgrade` und `OverchargeBehavior` ergänzt
(`docs/TYPED-DEFINITIONS.md`, Abschnitt „Phase 6“). Die Kompatibilitätsbasislinie `baselines/compatibility.json` hat **741 Einträge** (516 `deferred-field`, 36 `definition`, 9 `diagnostic`,
24 `duplicate-key`, 28 `excluded-file`, 128 `module`; `IniTool compat-check` bestätigt sie, 30. September 2026). Die Zahl `746` und alle früheren Werte sind überholt.
