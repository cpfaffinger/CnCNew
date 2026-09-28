# Kompatibilitätsliste (Phase 3, als Basislinie abgesichert in Phase 4)

Stand: 28. September 2026

Der Compiler ignoriert unbekannte Semantik nie. Nicht benötigte Definitionen, noch nicht
implementierte Simulations-/Präsentationsmodule, zurückgestellte Objektschlüssel, wiederholte
Skalarschlüssel, Diagnosecodes und ausgeschlossene Dateien werden als `CompatibilityEntry` mit Kategorie,
Name, Häufigkeit, Besitzerart und geplantem Ziel in jedem kanonischen Snapshot geführt. Seit Phase 4 ist die
Liste als **geprüfte Basislinie** eingecheckt (`baselines/compatibility.json`, 344 Einträge, keine Assetinhalte) und
wird gegen Drift geprüft:

```powershell
dotnet run --project tools/CnCNew.IniTool -- compat-check .local/phase2-import-a baselines/compatibility.json
```

Der Befehl endet bei jedem neuen (`NEW`), fehlenden (`MISSING`) oder in Häufigkeit, Besitzerart oder Ziel geänderten
(`CHANGED`) Eintrag mit Exitcode 1; der Realdatentest von `CnCNew.Data.Tests --content-dir` prüft dasselbe. Die Basislinie
erzeugt `IniTool compat-baseline`; jede Änderung ist Review-Gegenstand (Vollinventarmodus: alle Kartenoverlays, strikter
Modulkatalog, Retail-Ladeplan).

Kategorien: `definition` (38 Typen, 2.899 Verwendungen), `module` (193 Modultypen, 12.037 Verwendungen), `deferred-field`
(37 Objektschlüssel, 6.021), `duplicate-key` (39 Schlüssel, 171), `diagnostic` (9 Codes) und `excluded-file` (28 Dateien).

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
das Phantommodul `ArmorSet`). 12 sind seit Phase 4 implementiert (`ActiveBody`, `CreateObjectDie`, `DeletionUpdate`,
`DestroyDie`, `FXListDie`, `HighlanderBody`, `ImmortalBody`, `InactiveBody`, `InstantDeathBehavior`, `KeepObjectDie`,
`LifetimeUpdate`, `StructureBody`). Die übrigen 193 stehen in der Basislinie mit Häufigkeit, Besitzerart und Ziel:
137 für Phase 5 (Spielregeln), 34 für Phase 6 (Welt, Bewegung, KI), 22 Präsentationsmodule für Phase 7 (Draw/ClientUpdate,
nie in der Simulation). Zieleinteilung, Blockart und Interfacemaske jedes Moduls stehen im Katalog; eine Liste der
Verwendungen je Modul liefert `SimTool audit-modules <content-dir>`. `StrictUnknownModules` scheitert an jedem Namen außerhalb
des Katalogs, sodass ein Tippfehler von einem bekannten, noch nicht implementierten Modul unterscheidbar ist.

## Sichtbare Sonderfälle

- `GeometryMajorRadius = 18.5.0` und `GeometryHeight = 40v.0` sind explizite Compat-Werte (`shipped-double-decimal-radius`,
  `shipped-real-trailing-text`, beide wie `sscanf("%f")` gelesen) und in `compatibility-value` gezählt (2).
- Negative Dauern (`MinLifetime = -1`) wickeln modulo 2^32 wie `sscanf("%u")` (`compatibility-unsigned-wrap`, 10).
- Überzählige Token hinter Einzelwerten (`compatibility-ignored-tokens`, 3).
- Wiederholte Skalarschlüssel im selben Block: der letzte gewinnt (`compatibility-duplicate-key`, 171 Verwendungen in 39
  Schlüsseln; wiederholbare Schlüssel wie `Armor` oder `Locomotor` behalten ihre Vielfachheit und stehen nicht hier).
- Referenzen: 1.545 Verwendungen sind sichtbare Fallbacks – 1.541 `compatibility-engine-veterancy-upgrade`
  (`Upgrade_Veterancy_ELITE`/`_HEROIC`, engineinterne Namen ohne INI-Definition, davon 1.518 über das vom
  `DefaultThingTemplate` geerbte `AutoHealBehavior`) und 4 `compatibility-unresolved-reference` (`DieByFireUSA`,
  `Boss_VehicleSentryDrone`); kein stiller Ersatz durch ein anderes Ziel.
- Das Vollinventar wendet alle Kartenoverlays gemeinsam an. Dadurch fehlen Ziele von `ReplaceModule` (52,
  `compatibility-missing-replace-target`) und `RemoveModule` (52, `compatibility-missing-remove-target`), und wiederholte Tags
  aus mehreren Overlays treten auf (`compatibility-duplicate-module-tag`, 113; 18 davon in den Basisdaten). Ein einzelnes
  Overlay behält korrekte Semantik; `--maps exclude` liefert die Basisdaten ohne diese Aggregatartefakte.
- Vier `AddModule`-Blöcke enthalten kein Modul (`compatibility-add-module-without-module`); ihre Felder wirken auf das
  Objekt, wie im Original.
- Ausgeschlossene Dateien (28) stehen mit Grund in der Liste: Debug-Dateien (`GameDataDebug.ini`, `CommandMapDebug.ini`,
  `CommandMapDemo.ini`), die WorldBuilder-Datei `Scripts.ini`, nicht referenzierte Dateien (`MemoryPools.ini`,
  `Default/ButtonSets.ini`, `Default/Webpages.ini`, Installer-`Options.ini`) und 20 Sprachvarianten.
