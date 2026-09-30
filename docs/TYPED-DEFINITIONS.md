# Typisierter Definitionsvertrag

Stand: 28. September 2026 (Phase 3 gehärtet durch Block 0 der Phase 4; Weapon/Armor/Upgrade/Science/Locomotor
domänentypisiert in Block 0.1 der Phase 5, siehe letzten Abschnitt)

## Schicht und öffentlicher Vertrag

`CnCNew.Data` kompiliert SAGE-INI in einen unveränderlichen `CompiledDefinitionGraph`. Der
öffentliche Graph enthält keine `IniNode`-Instanzen. Parserknoten existieren ausschließlich
während der Kompilierung; Simulation und spätere Systeme erhalten abgeleitete Definitionen,
`DefinitionId`, typisierte Werte, Module und aufgelöste Referenzkanten. Die Bibliothek hat keine
Abhängigkeit auf Archive, Import-VFS, Stride oder Präsentation.

Eigene Definitionstypen bestehen für Object, Weapon, WeaponSet, Armor, Damage, Upgrade, Science,
Player, Faction, CommandButton, CommandSet, FXList/FX, AudioEvent, DialogEvent, MusicTrack,
AmbientStream, Locomotor, ExperienceLevel, SpecialPower sowie GameData/GlobalData. Name,
Originalschreibweise und virtuelle Quellposition bleiben erhalten. `DefinitionValue` trennt
Boolean, Integer, endliche Dezimalzahl, Zeit, Distanz, Geschwindigkeit, Winkel, Prozent, Geld,
Vektoren, Farbe, Farbschlüsselbild, Ganzzahlliste, Enum, Flags, Liste, Referenz und Sentinel.
Zahlen werden invariant gelesen; NaN, Infinity und Überläufe sind Fehler.

Alle übrigen verschachtelten Blöcke werden rekursiv als immutable `DefinitionSection` erhalten
(WeaponSet, ArmorSet, Prerequisites, ConditionState, …).

## Wertgrammatik (Block 0.3)

Unbekannte Token sind Fehler, nie `Text`.

- **Farben** `R:n G:n B:n [A:n]` und **Vektoren** `X:n Y:n [Z:n]` werden schlüsselunabhängig streng geparst
  (Labels ohne Groß-/Kleinschreibung, `:` wirkt wie ein Trenner wie bei `INI::getNextSubToken`, daher sind auch
  `X: 60.0  Y:  35.0   Z:0.0`, `X:-30 Y:15: Z:10` und `X:.071` im Bestand gültig). Farbkomponenten sind
  0–255; ein `//`-Kommentar hinter einer Farbe ist erlaubt; `R:n G:n B:n <frame>` (Partikel-Farbschlüsselbilder) ist der
  eigene Wert `ColorKeyframe`. Fehlerhafte Formen scheitern.
- **Enums und Flags** hängen an **strikten Namenstabellen**, mechanisch aus den Referenzquellen übernommen
  (`NameTables`, Reihenfolge = Bitindex): `KindOf` (120, `KindOf.cpp`), `ObjectStatus` (45), `DamageType` (38), `DeathType`
  (21), `Veterancy` (4), `Geometry` (3), `RadarPriority`, `BuildableStatus`, `BuildCompletion`, `EditorSorting`, `Shadow`,
  `LocomotorSet`. Flags haben die Semantik von `BitFlags::parse`: einfache Listen, `NONE`, `+`/`−`-Deltas (nicht
  mischbar); relative Deltas werden gegen den geerbten Wert aufgelöst. `DeathTypes`/`VeterancyLevels` folgen
  `INI::parseDeathTypeFlags` (Start immer `ALL`, `ALL`/`NONE`/`+X`/`−X`).
- **Einzeltoken-Typen** lesen wie das Original nur das erste Token; überzählige Token werden als
  `compatibility-ignored-tokens` sichtbar (3 Fälle im Bestand, etwa `ExperienceRequired` mit fünf Werten,
  `EditorSorting = MISC_MAN_MADE CLEARED_BY_BUILD`). Ein vereinzeltes `=` im Wert verschwindet wie bei `INI::m_seps`.
- **Reelle Zahlen** folgen `sscanf("%f")`: exakte Zahlen sind getypt, Zahlen mit Resttext (die zwei ausgelieferten Tippfehler
  `GeometryMajorRadius = 18.5.0` und `GeometryHeight = 40v.0`) sind explizite `Compatibility`-Werte
  (`shipped-double-decimal-radius`, `shipped-real-trailing-text`), gar keine Zahl ist ein Fehler.
- **Dauern** (`FieldType.DurationMs`) folgen `sscanf("%u")`: negative Zahlen wickeln modulo 2^32 und werden als
  `compatibility-unsigned-wrap` gemeldet (10 Fälle in Kartenoverlays).

## Schlüsselschemata (Block 0.3)

- **Object-Kopf:** `ObjectSchema` enthält alle Schlüssel von `ThingTemplate::s_objectFieldParseTable` (96 Schlüssel, davon 43 `Opaque`). Typisiert:
  `KindOf`, `Geometry*`, `Shadow`, `RadarPriority`, `Buildable`, `BuildCompletion`, `EditorSorting` (Enums/Flags),
  Sichtweiten/Radien (Distanz), `BuildCost`/`RefundValue` (Geld, 0–65535), `BuildTime` (Sekunden – vorher als Millisekunden
  fehlgetypt), `PlacementViewAngle` (Winkel), `DisplayColor` (Farbe), `Experience*`/`SkillPointValue` (4 Ganzzahlen),
  Booleans (`Yes`/`No` ohne Groß-/Kleinschreibung), `CommandSet`, `BuildVariations` (Referenzen), `Locomotor`
  (`TaggedReferenceList`: Setname + Locomotor-Referenzen, je Setname genau eine Liste). Ein **unbekannter Objektschlüssel ist
  ein Fehler**; Audio-/Portrait-/Cameo-Schlüssel sind bekannt, aber `Opaque` und stehen mit Häufigkeit in der
  Kompatibilitätsliste (`deferred-field`, 37 Schlüssel, 6.021 Verwendungen). `ObjectReskin` darf nur die Schlüssel der
  Reskin-Tabelle setzen.
- **Module:** Jeder implementierte Modultyp hat ein strenges Feldschema (Schlüssel, Wertart, Pflicht, Herkunft); der Compiler
  prüft Blockart (`Behavior`/`Body`/`Draw`/`ClientUpdate`) gegen den Katalog, unbekannte und doppelte Schlüssel, falsche
  Wertarten und Pflichtfelder.
- **Übrige Definitionsdomänen** bleiben flach (Phase 5), siehe „Bekannte Grenzen“.

## Ladereihenfolge (Block 0.4)

`OriginalLoadPlan` bildet die Retail-Ladereihenfolge von `GameEngine.cpp` (`initSubsystem`: Default-Datei, Hauptdatei,
Verzeichnis), `GameAudio.cpp`, `GameLOD.cpp`, `Image.cpp` und `GameLogic.cpp` ab. Verzeichnisse laden zuerst die Dateien
direkt im Verzeichnis, danach die Unterverzeichnisse, jeweils sortiert (`INI::loadDirectory`). Jede INI-Datei ist klassifiziert
(`Retail`, `MapOverlay`, `DebugOnly`, `ToolOnly`, `Unreferenced`, `LanguageVariant`, `Unclassified`); ausgeschlossene Dateien stehen
mit Grund in der Kompatibilitätsliste (28 Dateien: `GameDataDebug.ini`, `CommandMapDebug.ini`, `CommandMapDemo.ini`, `Scripts.ini`
(nur WorldBuilder), `MemoryPools.ini`, zwei unreferenzierte Default-Dateien, `Options.ini` des Installers und 20 Sprachvarianten
(16 anderer Sprachen, 4 Windows-9x)). Die Sprache ist wählbar (`English`). Ein strikter Lauf schließt unklassifizierte Dateien aus
und meldet sie.

`IniTool load-order-report` listet alle Definitionen mit mehr als einem Vorkommen (478, davon 431 über mehrere Dateien) mit
Dateireihenfolge und Gewinner. **Ergebnis:** 18 Gewinner ändern sich gegenüber der Phase-3-Reihenfolge – 16 `MappedImage`-Namen
(`HandCreated` lädt nach `TextureSize_512`, `Image.cpp`; Phase 3 sortierte umgekehrt) und 2 Upgrades zwischen Kartenoverlays
(`MD_USA03_END`/`MD_USA03_END1`, im Aggregat ohne Original-Entsprechung). Bei keinem Paar hängt die relative Dateireihenfolge
von einer nicht belegten Annahme ab (Bericht: 0 „ORDER-NOT-SOURCED“); die nicht durch Quelltext geordneten GameClient-Dateien
definieren keine gemeinsamen Namen. Ohne Kartenoverlays gibt es keinen doppelten Objektnamen: Doppelte `Object`-Namen kommen nur in
Karten-`map.ini` vor (`INI_LOAD_CREATE_OVERRIDES`); in den Basisdaten betreffen Mehrfachdefinitionen die Paare Default-/Hauptdatei
(GameData, AIData, Multiplayer), die `MappedImage`-Paare und Wiederholungen innerhalb einer Datei (Terrain, FXList, Upgrade,
ObjectCreationList).

Kartenoverlays: `MapOverlayMode.Include` wendet alle 27 Karten nach den Basisdaten in Pfadreihenfolge an (Vollinventar,
Aggregat); `Exclude` liefert die Retail-Basisdaten. Zur Laufzeit gilt genau ein Overlay pro Karte
(`GameLogic::startNewGame`); die Simulation verwendet den Basisbestand.

**Compat-IDs** tragen jetzt den Deklarationstyp (`compatibility://<typ>/<name>`), weil gleichnamige Definitionen
verschiedener Typen (sechs im Bestand, etwa `MappedImage`/`EvaEvent`) vorher zu einer verschmolzen.

## Objektvererbung (Block 0.4)

Wie `ThingFactory::newTemplate` kopiert jedes neue `Object` das `DefaultThingTemplate` (Felder, Module, WeaponSet/ArmorSet).
Danach gilt `ThingTemplate::parseModuleName`/`ModuleInfo::clearCopiedFromDefaultEntries` mit den **Interfacemasken** aus dem
Modulkatalog: Ein explizites Modul entfernt alle geerbten Defaultmodule, die ein Interface mit ihm teilen (z. B. ersetzt
`ActiveBody` den `InactiveBody`, ein Todesmodul das Default-`DestroyDie`); als `InheritableModule` markierte Module bleiben (das
Default-`AutoHealBehavior` nur, wenn das Objekt trainierbar ist), als `OverrideableByLikeKind` markierte
(`StealthUpdate`) nur für Vehikel/Infanterie/Score/tragbare Strukturen und nicht für Luftfahrzeuge, Gebäude usw.
Die Auswertung ist wie im Original **reihenfolgeabhängig** (sie liest `KindOf`/`IsTrainable` zum Zeitpunkt der
Moduldeklaration). Die erste explizite `WeaponSet`/`ArmorSet` ersetzt die geerbten einmalig; `UnitSpecificSounds/-FX` ersetzen
vorhandene. `ObjectReskin Kind Eltern` kopiert die Eltern, erlaubt nur Erscheinungsschlüssel und ersetzt geerbte Draw-Module.
`AddModule`/`RemoveModule`/`ReplaceModule`/`InheritableModule`/`OverrideableByLikeKind` wenden ihre Kinder mit den Feldern des
Objekts an – auch **nicht geschlossene `AddModule`-Blöcke** (`MD_CHI03`, `GLAStatue`) setzen Felder und Sektionen des Objekts, statt
ein Phantommodul `ArmorSet` zu erzeugen. `ReplaceModule` entfernt und hängt den Ersatz **am Ende** an, verlangt denselben
Modultyp und ein neues Tag; `RemoveModule` auf ein fehlendes Tag wird im Aggregat als Diagnose gezählt. Wiederholte Modultags
im selben Objekt bleiben beide erhalten (Retail; nur der Debug-Build bricht ab) und sind als
`compatibility-duplicate-module-tag` gezählt (18 Fälle in den Basisdaten). Die Syntax `Object Kind : Eltern` gibt es im
Original nicht; sie bleibt als Erweiterung für Tests erhalten und wird gezählt (im Bestand 0).

## Wiederholte Schlüssel (Block 0.4)

Im Bestand kommen Schlüssel innerhalb eines Kopfes mehrfach vor (`Armor = …` 860 Wiederholungen, `Locomotor` 343, `ScatterTarget` 51,
`MapCache supplyPosition` 1.472 u. a.). Phase 3 überschrieb sie still (`RemoveAll`), wodurch etwa alle `Armor`-Koeffizienten
bis auf den letzten verloren gingen. Jetzt gilt eine begründete Tabelle (`RepeatableKeys`, je Eintrag mit der Parserfunktion
des Originals): **Accumulate** (jedes Vorkommen bleibt, Reihenfolge erhalten) für `ScatterTarget`, `StandardPublicBone`,
`supplyPosition`, `techPosition`, `Animation Image`, `Credits Text/Style`, `CrateObject`, `TransitionToFX/OCL`;
**indiziert** (ein Vorkommen ersetzt das gleichindizierte an Ort und Stelle, sonst angehängt) für `Armor` (Schadenstyp),
`MajorFX`/`MinorFX` (Schadenstyp), `VeterancyMajorFX/MinorFX`, `WeaponBonus` (Bedingung + Feld) und `Object Locomotor`
(Setname). Alle übrigen wiederholten Schlüssel sind Skalare: der letzte gewinnt, jede Wiederholung im selben Block ist
gezählt (`duplicate-key`, 39 Schlüssel, 171 Verwendungen im Vollinventar).

## Modulregistrierung (Block 0.2)

`ModuleDescriptor` enthält Name, Blockart, Interfacemaske (aus den Referenz-Headern, `getInterfaceMask()`), Besitzerarten,
Feldschema, `CompatibilityOnly` und Zielphase und **keine Factory**. Der Katalog `ModuleCatalog` führt alle 205 Modultypen des
Bestands: 12 implementiert (siehe `SIMULATION-CORE.md`), 137 Simulationsmodule für Phase 5, 34 für Phase 6, 22
Präsentationsmodule (Draw/ClientUpdate) für Phase 7. Die Bindung an Implementierungen liegt in `CnCNew.Simulation`.
`StrictUnknownModules` scheitert an jedem Namen, der nicht im Katalog steht; ein Tippfehler im Namen ist damit von einem
bekannten, noch nicht implementierten Modul unterscheidbar.

## Kompatibilitätsbasislinie (Block 0.1)

`baselines/compatibility.json` (344 Einträge: Kategorie, Name, Besitzerarten, Häufigkeit, Zielphase; keine Assetinhalte) hält
die geprüfte Liste fest: Definitionstypen ohne Vertrag, nicht implementierte Module, zurückgestellte Objektschlüssel,
Doppelschlüssel, Diagnosecodes und ausgeschlossene Dateien. `IniTool compat-check <dir> <baseline>` beendet sich bei jedem
neuen, fehlenden oder in Häufigkeit oder Ziel geänderten Eintrag mit Exitcode 1; der Realdatentest der Datentests prüft
dasselbe. Neu erzeugen: `IniTool compat-baseline`. Änderungen an der Datei sind Review-Gegenstand.

## Referenzen und Snapshots

IDs haben die Form `<kind>://<ordinal-kleingeschriebener-name>`. Der Graph trennt Quellart und Zielart; ein gleichnamiges Ziel
falschen Typs ist nicht aufgelöst. `NONE` ist ein typisierter Sentinel. CommandSet-Slots, TriggeredBy-/ConflictsWith-Listen,
`Locomotor`-Sets und die bekannten Object-, Weapon-, Armor-, Science-, Upgrade-, FX-, Audio-, Locomotor- und
SpecialPower-Felder erzeugen Kanten – auch rekursiv aus Definitions- und Modulsektionen. Die engineinternen Namen
`Upgrade_Veterancy_ELITE`/`_HEROIC` haben keine INI-Definition und tragen den eigenen Fallback
`compatibility-engine-veterancy-upgrade`.

`DefinitionSnapshotWriter` schreibt Schema 2 als kanonisches UTF-8-JSON (Schema 1 hatte keine Modulblockart und keine
neuen Wertarten); `ComputeFingerprint` streamt denselben Snapshot in SHA-256 (Replay-Fingerabdruck). Definitionen, Felder,
Kanten und Kompatibilität sind ordinal sortiert; Module und wiederholte Schlüssel behalten ihre Reihenfolge. Absolute Pfade,
Zeitstempel und nicht deterministische Dictionaryreihenfolgen fehlen.

## Reproduzierbarkeit und Zahlen

```powershell
dotnet run --project tools/CnCNew.IniTool -- compile .local/phase2-import-a .local/phase4-a.json
dotnet run --project tools/CnCNew.IniTool -- compile .local/phase2-import-a .local/phase4-b.json
Get-FileHash -Algorithm SHA256 .local/phase4-a.json,.local/phase4-b.json
```

Referenzwerte (28. September 2026, Vollinventar mit allen Kartenoverlays, zwei unabhängige Läufe byteidentisch):

| | Phase 3 (Ende) | nach Block 0 |
|---|---:|---:|
| geladene INI-Dateien | 256 | 228 (28 ausgeschlossen) |
| Syntaxblöcke | 66.623 | 65.873 |
| Definitionen | 12.677 | 12.573 (typisiert unverändert 9.876; Compat 2.801 → 2.697) |
| Moduleinträge / Schlüssel | 16.360 / 335.592 | 16.360 / 333.061 |
| Referenzen aufgelöst | 12.441 von 12.468 | 13.134 von 14.679 |
| sichtbare Fallbacks | 27 | 1.545 (1.541 Veterancy-Pseudoupgrades, davon 1.518 aus dem geerbten Default-`AutoHealBehavior`; 4 echte Lücken) |
| Modulkatalog | 206 beobachtete Namen (mit Phantom) | 205 (12 implementiert) |
| Snapshot | 112.664.082 Byte | 142.302.102 Byte |
| SHA-256 | `14154C51…C675D4` | `131ED730F2AD4DB85D6F50CE91A7059358CA32D6728F8EB6E91E6CF8775E58A4` |

Retail-Basisdaten ohne Kartenoverlays (`--maps exclude`): 146 Dateien, 63.745 Blöcke, 12.548 Definitionen (2.113 Objekte),
16.045 Module, 327.293 Schlüssel, 12.902 von 14.420 Referenzen aufgelöst, Snapshot 141.720.050 Byte, SHA-256
`9B49E9DD1EE403C6797B32AD66F63CF3C0E538CEB510A84CEFF4AD5257FCCD38`.

Erklärung der Abweichungen des Hashes: (1) 28 Dateien ausgeschlossen (Debug, Werkzeug, andere Sprachen); (2) Compat-IDs mit
Typpräfix; (3) neue Wertarten (Farbe, Farbschlüsselbild, Vektor, Enum, Flags, Ganzzahllisten, gebundene Locomotorlisten);
(4) wiederholte Schlüssel behalten Vielfachheit (die 53 `Armor`-Definitionen behalten 884 Koeffizientenzeilen statt eines Wertes je Schadenstyp-Überschreibung); (5) `DefaultThingTemplate` wird in jedes
Objekt kopiert (mehr Felder und Module, +1.518 geerbte Pseudoupgrade-Referenzen); (6) Modulblockart im Snapshot; (7) Kompatibilitäts-
kategorien erweitert. Werteverteilung ohne die geerbten Defaultkopien: von 333.053 typisierten Werten sind 128.006 `Text` (vorher
135.215 von 335.592) und 74.128 `List` (vorher 89.511); neu getypt sind 388 Farben, 8.696 Farbschlüsselbilder, 6.316 Vektoren,
5.134 Flags, 5.044 Enums und 908 Ganzzahllisten. Alle Werte einschließlich Vererbung: 412.756 (159.030 `Text`, 75.138 `List`).

## Bekannte Grenzen (nach Block 0 der Phase 4)

- Die Definitionstypen außer Object (`WeaponDefinition`, …) haben weiter keine domänenspezifischen Eigenschaften, Pflichtfelder
  oder Schlüsselschemata; ihre 30.000 Kopfwerte sind zu einem großen Teil noch heuristisch typisiert. Schuld für Phase 5, mit
  Zahlen aus `IniTool debt-report`: Armor 884 Kopffelder (alle `List`), Weapon 6.248 (2.449 `Text`/`List`), Locomotor 3.599
  (552), AudioEvent 8.442 (5.006), Fx 58.459 (27.623), CommandButton 5.240 (4.413), Upgrade 427 (263), Science 456 (254),
  SpecialPower 542 (193), Faction 409 (333), GameData 323 (85).
- Die Einheitentypisierung dieser Domänen erfolgt weiter heuristisch über Teilstrings des Schlüsselnamens; die Umrechnung in
  Ticks und Distanz pro Tick leistet die Simulation nur für die Felder der implementierten Module.
- Zwei Compat-Gruppen sind bewusst grob: Definitionen ohne Vertrag (Kompatibilitätsliste, 38 Typen) und `Opaque`-Objektschlüssel.
- Die Reihenfolge der GameClient-Subsysteme untereinander ist nicht aus dem Quelltext belegt; der Bericht weist nach, dass
  daraus kein Gewinnerwechsel folgt.
- Eine inkrementelle oder gecachte Kompilierung existiert nicht.

*(Die oben aufgeführte Schuld für Armor, Weapon, Upgrade, Science und Locomotor ist inzwischen abgetragen – siehe unten.)*

## Block 0.1 der Phase 5: Domänenschemata für Weapon, Armor, Upgrade, Science, Locomotor

`DomainSchemas.For(DefinitionKind)` (`src/CnCNew.Data/Compilation/DomainSchemas.cs`) hängt an `DefinitionCompiler.ApplyProperty`
zwischen der Object-Schemaprüfung und dem generischen Fallback: Je Definitionskopf ein `SchemaSet` mit den mechanisch
relevanten Feldern; nicht mechanisch benötigte, aber reale Schlüssel bleiben `Opaque` (gezählt als `deferred-field`,
Kategorie `phase-5-domain-field-typing`, nie stillschweigend verworfen).

- **`WeaponSchema`** (`Weapon.cpp TheWeaponTemplateFieldParseTable`): 15 mechanisch relevante Felder typisiert
  (`PrimaryDamage`, `PrimaryDamageRadius`, `SecondaryDamage`, `SecondaryDamageRadius`, `AttackRange`,
  `MinimumAttackRange`, `DamageType`, `DeathType`, `WeaponSpeed`, `MinWeaponSpeed`, `ClipSize`, `ClipReloadTime`,
  `DelayBetweenShots`, `AutoReloadsClip`); ~60 verbleibende Schlüssel bleiben deferred. Neue Wertarten:
  **`ShotDelayValue`** (`FieldType.ShotDelayMs`) für `DelayBetweenShots` – akzeptiert sowohl die einfache Form (auch mit
  Nachkommastellen, `WeaponTemplate::parseShotDelay` liest real, nicht nur ganzzahlig) als auch die vier im Bestand
  vorkommenden `Min:x Max:y`-Fälle (z. B. `ScudStormMissile`).
- **`ArmorSchema`**: ein Feld, `Armor = <Default|Schadensart> <Prozent>`, wiederholbar und nach erstem Token indiziert
  (`RepeatPolicy.IndexedByFirstToken` – ohne diese Angabe hätte jede Wiederholung fälschlich als `duplicate-key`
  gezählt). Neue Wertart **`CoefficientValue`** (`FieldType.CoefficientByEnum`, parametrisiert über `InnerType`, damit
  derselbe Mechanismus später auch `DamageFX`-Beträge tragen kann).
- **`UpgradeSchema`/`ScienceSchema`/`LocomotorSchema`**: analog, Felder und Quellen im Katalog kommentiert.
- **Wichtige Nebenkorrektur:** Der generische Referenz-Erkenner (`DefinitionValueParser.TryReferenceKind`) kannte das
  Feld `ArmorSet` (→ `DefinitionKind.Armor`), aber **nicht** das gleichnamige Feld `Armor` innerhalb eines
  `ArmorSet`-Blocks (`ArmorSet { Armor = <Name> End }`) – ein bis dahin unbemerkter Fehler, weil Phase 4 `ArmorSet`-Inhalte
  nie auswertete. Ohne die Ergänzung `"armor" => DefinitionKind.Armor` blieb der Wert unaufgelöster Text, und
  `ObjectTemplate.ResolveArmor` fiel für jedes Objekt auf den Alle-Koeffizienten-1,0-Standard zurück (entdeckt durch den
  neuen Phase-5-Kampf-Integrationstest, nicht durch den Realdatentest – die 100 %-Koeffizienten von `NoArmor` verdeckten
  den Fehler zufällig). Nach der Korrektur lösen 2.260 zusätzliche Referenzen auf (alle erfolgreich,
  13.207/14.753 → 15.467/17.013 im Vollinventar).
- **Zahlen** (`IniTool debt-report`, Vollinventar): Armor 884 Kopffelder, **0 unbenannt**; Weapon 6.248 Kopffelder,
  1.738 unbenannt (vorher 2.449); Upgrade 427/228 (vorher 263); Science 456/168 (vorher 254); Locomotor 3.599/552
  (unverändert – die 552 sind bewusst deferred, siehe `LocomotorSchema`-Kommentar).

## Block 0.1 fortgesetzt: `FactionSchema` (Spieler/Fraktionen)

`PlayerTemplate.cpp TheFieldParseTable` (~35 Schlüssel) liefert zwölf mechanisch relevante Felder für den headless
Referenzlauf: `Side`, `PlayableSide`, `IsObserver`, `StartMoney` (`PlayerTemplate::parseStartMoney`: ein einfacher
Geldbetrag, `Money::setStartingCash`), `IntrinsicSciences` (Referenzliste auf `Science`),
`IntrinsicSciencePurchasePoints`, `StartingBuilding` sowie `StartingUnit0`–`StartingUnit9` (je eine `Object`-Referenz).
Die übrigen ~23 Schlüssel (Kommandoleisten-Zuordnung, Bildschirm-/Audio-Ressourcen, Tooltips, Medaillons) bleiben
deferred – reine Präsentation. Zahlen: 409 Kopffelder über 15 Fraktionen, 294 unbenannt (vorher 333; 41 zusätzliche
Referenzen lösen alle auf).

`CnCNew.Simulation.Templates.FactionCatalog` liest `StartingBuilding`/`StartingUnit0-9` bewusst als **Namen**, nicht als
aufgelöste `ObjectTemplate`-Indizes: Spawnen läuft ohnehin über `SpawnCommand`, das einen Namen nimmt und ihn beim
Einreihen validiert, sodass der Fraktionskatalog nicht in derselben Reihenfolge wie die Objektvorlagenliste gebaut
werden muss. `SimulationWorld` schreibt `StartMoney`/`IntrinsicSciences` einmalig bei der Konstruktion in den
Spielerzustand (`Player::Player()`), sofern `PlayerConfiguration.Faction` gesetzt ist.

## Block 0.1 fortgesetzt: `ObjectCreationList`, `CrateData` und `GameData/Gravity`

- **`ObjectCreationList`** ist eine eigene Definitionsart (`ObjectCreationListDefinition.Sections`, 295 Definitionen im
  Vollinventar, 294 im Retail-Bestand). `ObjectCreationListSchema` typisiert die sechs Nugget-Arten streng nach
  `ObjectCreationList.cpp` (`CreateObject`, `CreateDebris`, `ApplyRandomForce`, `FireWeapon`, `Attack`) sowie
  `DeliverPayload` (`DeliverPayloadAIUpdate.cpp`, `RadiusDecal.cpp`); unbekannte Schlüssel und Nugget-Arten sind Fehler.
  Modulfelder mit Listenverweisen (`OCL`, `DamagedOCL`, `RubbleOCL`, …) sind jetzt Referenzen der Art `ObjectCreationList` und werden
  im Vollinventar aufgelöst. Neue Wertarten: `RetailAlternatingNames` (behält die 1., 3., … Namen, siehe unten) und
  `CountedReference` (`<Name> [Anzahl]`, `DeliverPayloadNugget::parsePayload`).
- **Retail-Eigenheit als gezählter Kompatibilitätsfall:** `ObjectNames` (`CreateObject`, Referenzen auf `Object`) und
  `ModelNames` (`CreateDebris`) folgen `parseDebrisObjectNames`: die Retail-Schleife speichert ein Token und liest das
  nächste, ohne es zu speichern; es zählen nur das 1., 3., … Token (Wertart `RetailAlternatingNames`). Die verworfenen
  Token laufen über `FieldSchema.IgnoredTokenCount` in den Kompatibilitätsbestand statt stillschweigend zu verschwinden.- **`CrateData`** (`CrateSystem.cpp`): 9 Definitionen als Definitionsart `Crate`. `CreationChance`, `VeterancyLevel`,
  `KilledByType`, `KillerScience` und `OwnedByMaker` sind typisiert; `CrateObject <Name> <Gewicht>` bleibt als
  `deferred-field` (11 Vorkommen, Ziel `phase-5-domain-field-typing`), weil das Namens-Gewichts-Paar erst beim Aufbau des
  Simulationskatalogs (`CrateCatalog`) mit Fehlermeldung bei Formfehlern gelesen wird. `CreateCrateDie.CrateData` ist ein
  Referenzlistenfeld auf `Crate`.
- **`GameData/Gravity`** ist typisiert und aus der Kompatibilitätsbasislinie entfernt; die Simulation braucht es für
  `Thing::isSignificantlyAboveTerrain` (`docs/GAME-RULES.md` §6b).
- Die Kompatibilitätsbasislinie hat danach **784 Einträge** (vorher 787).

## Block 0.1 abgeschlossen: `SpecialPower`

`SpecialPowerSchema` (`SpecialPower.cpp SpecialPowerTemplate::m_specialPowerFieldParse`) typisiert die Definitionsart `SpecialPower`
streng: `ReloadTime` (Dauer), `RequiredScience` (Referenz auf `Science`, im Vollinventar aufgelöst), `PublicTimer`, `Enum`
(Namenstabelle `SpecialPowerType`, 67 Einträge inklusive `EARLY_*`-, `AIRF_*`-Varianten), `DetectionTime`, `SharedSyncedTimer`,
`ViewObjectDuration`, `ViewObjectRange`, `RadiusCursorRadius`, `ShortcutPower`. `InitiateSound`, `InitiateAtLocationSound` und
`AcademyClassify` bleiben als `deferred-field` (Präsentation/Statistik, 79 Verwendungen). Die zehn Spezialkraftmodule
(`SpecialAbility`, `OCLSpecialPower`, `CashBountyPower`, `CashHackSpecialPower`, `SpecialPowerCreate`, `GrantUpgradeCreate`,
`GrantScienceUpgrade`, `UnpauseSpecialPowerUpgrade`, `SpecialPowerCompletionDie`, `SpectreGunshipDeploymentUpdate`) haben Katalogschemata
und Bindungen (`docs/GAME-RULES.md` §6d); `SpecialAbility/InitiateSound` (85 Verwendungen) ist ebenfalls ein gezählter Kompatibilitätsfall.
Die Referenzauflösung steigt dadurch im Vollinventar auf 18.050/19.600, die Kompatibilitätsbasislinie sinkt auf **761 Einträge**, der
Definitionssnapshot (Schema 2) hat 144.745.050 Byte (SHA-256 `D0C06A2B06CD7065474A3EB729E36B87E1815DAE2D461E564D023EAA019295CF`).
## Abschluss von Phase 5: weitere Schemata und Zahlen

Nach dem Spezialkraft-Subsystem sind folgende Felder und Module streng typisiert worden (jeweils mit Neuerzeugung der
Kompatibilitätsbasislinie; Details der Semantik in `docs/GAME-RULES.md`):

- **Versorgung** (§6e): `SupplyCenterDockUpdate`, `SupplyWarehouseDockUpdate`, `SupplyTruckAIUpdate`, `WorkerAIUpdate`,
  `DozerAIUpdate`, `AutoDepositUpdate` als Katalogschemata sowie `GameData/ValuePerSupplyBox` (`Player::getSupplyBoxValue`).
- **`WeaponBonus`** (Weapon und GameData) ist ein strenger Eintrag `<Bedingung> <Feld> <Prozent>` auf den Namenstabellen der
  Referenz (`WeaponBonusConditionType`, `WeaponBonusField`); die `HealthBonus_*`-Felder von `GameData` sind typisierte Prozentwerte (§6h).
- **Reparatur** (§6f): `RepairDockUpdate` und `AutoFindHealingUpdate`.
- **Kampf** (§6g): `FireWeaponUpdate` und `ReplaceObjectUpgrade` als Katalogschemata; `CrushDie` als Todesmodul mit dem gemeinsamen
  Todesfilter (`DieMuxData`) ohne eigene Felder.

Neu gemessen (`IniTool compile .local/phase2-import-a`, Vollinventar mit allen Kartenoverlays): 228 geladene INI-Dateien (28 nach
Ladeplan ausgeschlossen), 65.875 Blöcke, 12.574 Definitionen, 16.380 Module, 333.061 Schlüssel, 18.138/19.688 Referenzen aufgelöst
(1.550 sichtbare Fallbacks); Kompatibilität: 518 `deferred-field`-Einträge (20.606 Verwendungen), 36 `definition` (2.590), 9
`diagnostic` (4.617), 24 `duplicate-key` (120), 28 `excluded-file` (28), 131 `module` (4.151); die Basislinie hat **746 Einträge**.
Kanonischer Definitionssnapshot (Schema 2): 144.813.963 Byte, SHA-256 `D98CE0154ADF721DC58E9FF1FE769ADA646A058CE7B9AA9A8F0AC99254E5AB67`;
zwei unabhängige Läufe sind byteidentisch. Retail-Basisdaten (`--maps exclude`): 146 geladene INI-Dateien (110 ausgeschlossen), 63.747
Blöcke, 12.550 Definitionen (2.115 Objekte), 16.065 Module, 327.293 Schlüssel, 17.748/19.271 Referenzen aufgelöst (1.523 Fallbacks),
144.203.312 Byte, SHA-256 `9F5307957A55CD4683E8A79CBEB079320BE69400802DA38D2FFDDB200713E264` (Fingerabdruck `9F5307957A55CD46`). Die früheren
Zahlen oben (`761 Einträge`, `144.745.050 Byte`) sind überholt.

## Phase 6: Typisierung von Locomotor-`Surfaces`, `SellPercentage` und Kartenmodell

- **`Locomotor/Surfaces`** ist ein Flags-Feld auf der Namenstabelle `LocomotorSurface` (`NameTables.Phase5.cs`: `GROUND`, `WATER`, `CLIFF`, `AIR`, `RUBBLE`) statt eines Kompatibilitätswerts;
  die Navigation (`SIMULATION-CORE.md` §14) liest es als `LocomotorSurfaces`.
- **`GameData/SellPercentage`** ist ein typisierter Prozentwert (`GameDataSchema`), Grundlage des Verkaufserlöses (`SIMULATION-CORE.md` §16).
- **Kraftwerksmodule:** Katalogschemata für `PowerPlantUpdate` (`RodsExtendTime`), `PowerPlantUpgrade` (Upgrade-Mux) und `OverchargeBehavior`
  (`HealthPercentToDrainPerSecond`, `NotAllowedWhenHealthBelowPercent`).
- **Kartenmodell:** `CnCNew.Data.World.MapData` (`MapModel.cs`) ist ein unveränderlicher Vertragstyp ohne Renderer-, Archiv- oder Dateisystembezug; er ist keine Definitionsdomäne und
  erscheint nicht im Definitionssnapshot (`SIMULATION-CORE.md` §13).

Neu gemessen am 30. September 2026 (`IniTool compile .local/phase2-import-a`, Vollinventar): 228 geladene INI-Dateien (28 ausgeschlossen), 65.875 Blöcke, 12.574 Definitionen, 16.380 Module,
333.061 Schlüssel, 18.138/19.688 Referenzen aufgelöst (1.550 sichtbare Fallbacks); Kompatibilität: 516 `deferred-field`-Einträge (20.401 Verwendungen), 36 `definition` (2.590),
9 `diagnostic` (4.599), 24 `duplicate-key` (120), 28 `excluded-file` (28), 128 `module` (4.133); die Basislinie hat **741 Einträge** (`compat-check`: alle stimmen).
Definitionssnapshot (Schema 2): 144.832.736 Byte, SHA-256 `AEB5108DDA27ABAC7445452AA2743B794AD0BF1102F2687F8D7D8F7B87D2ADB7`, zwei Läufe byteidentisch.
Retail-Basisdaten (`--maps exclude`): 12.550 Definitionen (2.115 Objekte), Snapshot 144.221.861 Byte, SHA-256 `1248585D0BAAB79F5202FDB81F26A2B00962A9D393B0CE1FF93C5FFDF6A5C6E7`.
`IniTool audit`: 256 von 256 INI-Dateien, 66.625 Blöcke. Frühere Zahlen (`746 Einträge`, `761 Einträge`, `144.813.963 Byte`) sind überholt.
