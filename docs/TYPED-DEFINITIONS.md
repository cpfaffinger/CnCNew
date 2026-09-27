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
Armor-, Science-, Upgrade-, FX-, Audio-, Locomotor- und SpecialPower-Felder erzeugen Kanten.

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
