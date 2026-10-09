# Runtime-Contentvertrag und Formatmatrix

Stand: 27. September 2026

## Schichtengrenzen

`CnCNew.IO` und `CnCNew.Archives` bilden ausschließlich die Importseite. `CnCNew.Formats`,
`CnCNew.Images`, `CnCNew.Data` und `CnCNew.W3D` validieren Dateisemantik ohne Engine-Typen.
`CnCNew.Import` verbindet diese Reader, baut den Abhängigkeitsgraphen und veröffentlicht den
materialisierten Bestand. `CnCNew.Content` kennt weder BIG noch das Import-VFS und öffnet zur
Laufzeit nur normale Dateien aus einem veröffentlichten Schema-2-Manifest.

## Schema 2

`content-manifest.json` ist kanonisches UTF-8-JSON. Es enthält die Schema-Version, den Namen und
die Version des Importers sowie deterministisch sortierte Optionen und Assets. Jedes Asset trägt:

- eine logische ID `asset://` plus ordinal kleingeschriebenen, normalisierten virtuellen Pfad;
- den case-erhaltenden virtuellen Pfad und eine typisierte Formatklasse;
- SHA-256 und Bytezahl der Quelle;
- ausgewählte Overlayquelle, Priorität und alle verdrängten Kandidaten;
- sortierte direkte Abhängigkeiten mit Status `resolved` oder `fallback`;
- den sicheren relativen Pfad, Hash und die Größe jedes erzeugten Runtimeartefakts;
- typisierte Diagnosen, insbesondere für erhaltene Dateien außerhalb des Produktscopes.

Pfade verwenden `/`, sind relativ und dürfen keine leeren, `.`- oder `..`-Segmente enthalten.
Vergleiche erfolgen ordinal ohne Groß-/Kleinschreibung; reine Case-Kollisionen sind ein harter
Importfehler. Physische Windows-Segmente werden prozentkodiert, etwa `Data/*` als `Data/%2A`.
Schema 1 bleibt lesbar und erhält eine sichtbare `legacy-manifest`-Diagnose. Neue Importe
schreiben nur Schema 2; unbekannte Schema-Versionen werden abgewiesen.

## Import, Cache und Veröffentlichung

`runtime-import` hasht und validiert jede gewählte Overlayquelle, extrahiert Referenzen und baut
den Graphen. Ein unveränderter SHA-256-Eintrag wird aus dem publizierten Artefakt übernommen.
Abhängigkeiten werden in jedem Lauf deterministisch neu berechnet. Maschinenpfade und Zeitstempel
gehen nicht in das Ergebnis ein.

Der Lauf entsteht in einem eindeutigen Geschwister-Stagingverzeichnis. Erst nach erfolgreicher
Format-, Referenz- und Manifestvalidierung wird das alte Ziel umbenannt und Staging atomar an
seine Stelle gesetzt. Bei Fehlern wird das alte Ziel wiederhergestellt. Tests prüfen Cachetreffer,
Invalidierung, gleiche Frischimporte und den unveränderten Bestand nach einem Importfehler.

## Formatmatrix des ausgelieferten Bestands

| Dateien | Formatklasse | Strategie und validierte Semantik |
| ---: | --- | --- |
| 8.904 | W3D | Runtime-Decoder; Chunkbaum, Grenzen, Meshes, Materialien und Texturreferenzen; Animation, Hierarchie und HLOD bleiben als validierte Chunkdomäne für Phase 7 erhalten |
| 8.475 | WAV | externer freier Runtime-Decoder; RIFF/WAVE-Länge, Chunkgrenzen, `fmt ` und `data` |
| 6.602 | DDS | Runtime-Decoder; Header, Dimensionen, Mips, BC1/2/3 und RGB24/32 samt Payloadgröße |
| 826 | TGA | Runtime-Decoder; 24/32 Bit, raw/RLE, Dimensionen, Orientierung und Pixelstrom |
| 256 | INI | Runtime-Decoder; SAGE-Syntax und Dateireferenzen; typisierte Spieldaten folgen in Phase 3 |
| 150 | MAP | Runtime-Decoder; 145 EAR/RefPack-, vier rohe CkMp- und eine ZL5/zlib-Variante; Dekompressionsgröße, Symboltabelle, Chunk-IDs, Versionen, Größen und Dateiende |
| 104 | ANI | Runtime-Decoder; RIFF/ACON; der in allen Dateien identische Size-plus-8-Schreibfehler ist explizit zugelassen |
| 80 | WND | Runtime-Decoder; `FILE_VERSION = 2`, Strukturklammern und Referenzen |
| 70 | BIK | externer freier Decoderpfad (FFmpeg/libav); Version, Dateigröße, Frames, Dimensionen und Framerate |
| 56 | MP3 | externer freier Decoderpfad; ID3-Version/-Größe und erster MPEG-Framesync |
| 24 | WAK | Runtime-Decoder; 20-Byte-Recordlayout, Länge und endliche Koordinaten |
| 12 | STR | Runtime-Decoder; Kommentare, Schlüssel, gequotete Werte, `END` und Duplikate |
| 4 | SCB | Runtime-Decoder; CkMp-Signatur und plausible Objektanzahl |

Partikel und FX liegen als INI-Definitionen mit W3D-, Textur- und Audioreferenzen vor. Die
übrigen 119 Dateien sind klassifiziert: 31 Installations-/Miles-Binaries, 46 Dokumente, sieben
Installerbilder, 18 opaque Installationsmetadaten, ein ungenutzter CSF-Katalog und 16
Legacyshader. Sie bleiben zur Herkunftstreue erhalten, sind aber nicht Teil des Produktscopes.
Keine Erweiterung bleibt unklassifiziert.

## Graph und Fallbacks

Von 41.527 direkten Referenzen werden 41.308 aufgelöst. Die Suche prüft kanonischen Pfad,
Besitzerordner, eindeutigen Dateinamen und für W3D `Art/Textures` samt DDS/TGA-Austausch. Echte
Zyklen sind Fehler; WND-Selbsterwähnungen werden nicht als Abhängigkeit interpretiert.

219 Verwendungen in 128 Assets sind bewusst typisiert: 164 `visible-magenta-texture`, 51
`silent-audio` und vier `disabled-missing-map` (nicht ausgelieferte Karten einschließlich der
EA-Demokampagne). Video besitzt den dokumentierten `skip-video`-Fallback, falls der freie externe
Decoder fehlt; im ausgelieferten Graphen wird er nicht benötigt. Es gibt keinen stillen Fallback.

## Reproduzierbare Abnahme

> Historischer Abschnitt (Abnahme der Phase 2, 27. September 2026). Die hier genannten Verzeichnisse
> `.local/game-original`, `.local/content` (damals Schema 1) und `.local/phase2-import-a`/`-b` gibt es nicht mehr.
> Heute erzeugt `scripts/dev-content.ps1` den Entwicklerbestand aus der Steam-Installation nach `game/content`
> (Schema 2, ignoriertes Verzeichnis `game/` in der Wurzel des Repositorys); der Weg für Anwender steht in
> `docs/INSTALLATION.md`.

```powershell
dotnet run --project tools/CnCNew.AssetTool -- audit-formats .local/content
dotnet run --project tools/CnCNew.AssetTool -- audit-dependencies .local/content
dotnet run --project tools/CnCNew.AssetTool -- runtime-import .local/game-original .local/phase2-import-a
dotnet run --project tools/CnCNew.AssetTool -- runtime-import .local/game-original .local/phase2-import-b
dotnet run --project tools/CnCNew.AssetTool -- verify .local/phase2-import-a
```

Beide Frischimporte erzeugten 25.682 Assets und denselben Manifest-SHA-256
`ACDF0B6CF98538469AA05F62E23D2A83BD3E17A911433B4EA73373FFFB1245FF`. Der folgende Lauf traf
25.682/25.682 Cacheeinträge. `verify` prüfte alle Artefakte ohne BIG-Zugriff. Lokale Importe und
das 27.174.097 Byte große Manifest bleiben ignoriert und werden nicht committed.
