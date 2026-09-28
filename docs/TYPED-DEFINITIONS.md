# Typisierter Definitionsvertrag

Stand: 27. September 2026

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
Vektoren, Farbe, Liste, Referenz und Sentinel. Zahlen werden invariant gelesen; NaN, Infinity und
Überläufe sind Fehler. Der ausgelieferte Tippfehler `GeometryMajorRadius = 18.5.0` besitzt als
einziger numerischer Sonderfall den sichtbaren Code `shipped-double-decimal-radius` und wird als
18.5 normalisiert.

Alle übrigen verschachtelten Blöcke werden rekursiv als immutable `DefinitionSection` erhalten.
Damit bleiben insbesondere WeaponSet, ArmorSet, Prerequisites, ConditionState sowie Unterblöcke
von Draw-/Behavior-Konfigurationen vollständig typisiert, ohne Parserknoten öffentlich zu machen.

## Include, Bedingungen und Reihenfolge

Includes werden über `IDefinitionSource` aufgelöst. Der Compiler kennt deshalb nur virtuelle
Runtimepfade. Suchreihenfolge ist Besitzerverzeichnis, exakter Runtimepfad, danach die expliziten
Suchwurzeln (standardmäßig `Data/INI`). Der erste Treffer gewinnt; ein fehlendes Ziel und ein
Include-Zyklus sind Fehler. Derselbe virtuelle Include wird pro Lauf einmal verarbeitet.

`#if`/`#ifdef`, `#else` und `#endif` werden gegen eine explizite, case-insensitive Symbolmenge
ausgewertet. Nicht geschlossene oder falsch verschachtelte Bedingungen sind Fehler. Ohne
Direktiven ist die Ladeordnung: `Data/INI/Default`, übriges `Data/INI`, `Maps`, sonstige Pfade;
innerhalb einer Stufe ordinal case-insensitive. Includes stehen vor ihrem Besitzer. Spätere
Definitionen desselben typisierten Namens überschreiben nur angegebene Felder und erben den Rest.
`ObjectReskin Child Parent` und `Object Child : Parent` verwenden eine explizite Basis; fehlende
Basen und Vererbungszyklen werden abgewiesen.

## Module und Behaviors

Behavior, Draw, Body, ClientUpdate und InheritableModule werden zu `ModuleConfiguration`
kompiliert. `ModuleRegistry` bindet einen Namen deterministisch an erlaubte Besitzerarten,
Feldschema und optional eine Factory. Doppelte Registryeinträge, falsche Besitzer, fehlende
Pflichtfelder, unbekannte Schemaschlüssel und falsche Wertarten sind Fehler. Reflection und
Engine-Typen werden nicht verwendet.

`AddModule` erhält die Reihenfolge eines oder mehrerer Module. `RemoveModule` entfernt per Tag.
`ReplaceModule` ersetzt am vorhandenen Index. Der Vollbestandsmodus diagnostiziert einen
fehlenden Replace-Tag sichtbar, weil alle Kartenoverlays gemeinsam inventarisiert werden, während
zur Laufzeit genau ein Kartenoverlay auf die Basis angewendet wird. Die Runtime darf unbekannte
Module nicht instanziieren; alle derzeit noch nicht simulierten Bestandsmodule stehen in der
Kompatibilitätsliste.

## Referenzen und Snapshots

IDs haben die Form `<kind>://<ordinal-kleingeschriebener-name>`. Der Graph trennt Quellart und
Zielart; damit ist ein gleichnamiges Ziel falschen Typs nicht aufgelöst. `NONE` ist ein typisierter
Sentinel. CommandSet-Slots, TriggeredBy-/ConflictsWith-Listen und die bekannten Object-, Weapon-,
Armor-, Science-, Upgrade-, FX-, Audio-, Locomotor- und SpecialPower-Felder erzeugen Kanten –
auch rekursiv aus Definitions- und Modulsektionen.

`DefinitionSnapshotWriter` schreibt Schema 1 als kanonisches UTF-8-JSON. Definitionen, Felder,
Kanten und Kompatibilität sind ordinal sortiert; Module behalten ihre semantische Reihenfolge.
Absolute Pfade, Zeitstempel und nicht
deterministische Dictionaryreihenfolgen fehlen. Originaltext bleibt ausschließlich für Audit und
Diagnose erhalten.

## Reproduzierbarkeit

```powershell
dotnet run --project tools/CnCNew.IniTool -- compile .local/phase2-import-a .local/phase3-a.json
dotnet run --project tools/CnCNew.IniTool -- compile .local/phase2-import-a .local/phase3-b.json
Get-FileHash -Algorithm SHA256 .local/phase3-a.json,.local/phase3-b.json
```

Die lokalen Snapshots sind generierte Laufzeitdaten und werden nicht committed.

## Bekannte Grenzen (Verifikation vom 28. September 2026)

Eine unabhängige Nachprüfung bestätigte Build, alle acht Testprogramme, 256/256 INIs, die
Bestandszahlen und den Snapshot-SHA-256 (frische Kompilierung ergibt denselben Hash). Zugleich
zeigte sie, dass der Vertrag strukturell vollständig, fachlich aber noch flach ist:

- Die Definitionstypen (`ObjectDefinition`, `WeaponDefinition`, …) sind leere Unterklassen von
  `GameDefinition`; alle Werte liegen in `Fields`/`Sections` als `DefinitionValue`. Es gibt keine
  domänenspezifischen Eigenschaften, keine Pflichtfelder und keine Schlüsselschemata für
  Definitionen – unbekannte Schlüssel an Definitionen werden nicht gemeldet. Schemas existieren nur
  für registrierte Module.
- Die Werttypisierung der Einheiten (Zeit, Distanz, Geschwindigkeit, Winkel, Geld) erfolgt
  heuristisch über Teilstrings des Schlüsselnamens. Es gibt keine Umrechnung in Ticks, Distanz pro
  Tick oder Bogenmaß.
- `Enum`, `Flags`, `Color`, `Vector2` und `Vector3` werden im Vollbestand nie erzeugt. Die realen
  Formen `R:255 G:0 B:0` (9.150 Verwendungen), `X:0 Y:0 Z:0` (6.161) und Bitflag-Listen wie
  `KindOf` (1.964) landen als `List`/`Text`. Von 335.592 Werten sind 135.215 `Text` und 89.511
  `List`.
- Die produktive `ModuleRegistry` ist leer. Alle 206 Bestandsmodule stehen nur deshalb in der
  Kompatibilitätsliste, weil der Compiler jeden unregistrierten Namen automatisch zählt. Die Liste
  ist nicht als geprüfte Basislinie eingecheckt; ein Tippfehler im Modulnamen wäre ununterscheidbar
  von einem bekannten, noch nicht implementierten Modul.
- Die Ladereihenfolge (Stufen `Default`, `Data/INI`, `Maps`, danach ordinal) ist nicht gegen die
  subsystemweise Reihenfolge und die Ladearten (`INI_LOAD_OVERWRITE`, `CREATE_OVERRIDES`,
  `MULTIFILE`) in `GameEngine.cpp` der Referenz abgeglichen. Wiederholte Schlüssel innerhalb eines
  Definitionskopfs überschreiben einander; ob dies für alle wiederholbaren Schlüssel des Originals
  zutrifft, ist nicht geprüft.
- Eine inkrementelle oder gecachte Kompilierung existiert nicht.

Diese Punkte sind als Block 0 sowie als Vorleistung für Phase 5 im Handoff
`HANDOFF-PHASE4.md` erfasst.
