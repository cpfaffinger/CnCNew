# CnCNew

Lokale C#/.NET-Neuimplementierung der für *Generals: Zero Hour* benötigten SAGE-Laufzeit. Der aktuelle Scope umfasst Singleplayer, Skirmish und Kampagne; Multiplayer folgt später.

Dieses Arbeitsverzeichnis bleibt lokal. Originale Spieldateien, entpackte Assets und heruntergeladene Referenz-Repositories liegen in ignorierten Verzeichnissen und sind nicht Bestandteil des neuen Quellcodes.

## Aktueller Stand

- lokale, unveränderte Kopie der vorhandenen Steam-Installation
- .NET-10-Solution
- validierender Reader für `BIGF`- und `BIG4`-Archive
- sicherer Einzel- und Batch-Extractor
- SHA-256-Inventarisierung der lokalen Installation
- materialisierte Laufzeitdaten: 25.682 normale Dateien, keine BIG-Container
- manifestbasiertes `CnCNew.Content`-Dateisystem ohne Archiv-Abhängigkeit
- SAGE-INI-Parser erfolgreich über 256/256 INI-Dateien geprüft
- W3D-Chunk- und Meshdecoder erfolgreich über 8.904/8.904 Dateien und 70.587 Meshes geprüft
- W3D-Materialpässe, 77.672 Shaderzustände, 80.041 Vertexmaterialien, Material-/Shader-IDs, DCG/DIG/SCG-Vertexfarben, Texturtabellen und UV-Daten werden validiert und rendererunabhängig decodiert
- alle vier Prelit-Materialwrapper werden als eigenständige Varianten erhalten; der ausgelieferte Bestand verwendet sie derzeit nicht
- fünf bereits ausgeliefert beschädigte `UISabotr_*`-Binärdateien werden verlustarm im Speicher rekonstruiert und erneut validiert
- eigener validierender TGA-Decoder erfolgreich über 826/826 Dateien und 100.666.368 Pixel geprüft
- Phase 1 „W3D-Material- und Renderingtreue“ ist abgeschlossen: 64.962 Einpass- und 5.625 Zweipass-Meshes sowie 317 zweistufige Pässe sind im Vollbestand nachgewiesen
- Phase 2 „Assetformate und Runtime-Contentvertrag“ ist abgeschlossen: 25.682/25.682 Dateien sind klassifiziert und validiert, Schema 2 enthält 41.527 auditierbare Referenzen
- Phase 3 „Typisierte Spieldaten und Modulregistrierung“ ist abgeschlossen: 256/256 INIs und 66.623 Blöcke werden zu 12.677 unveränderlichen Definitionen, 16.360 Modulen und einem typisierten Referenzgraphen kompiliert
- zwei unabhängige, 112.664.082 Byte große Definitionssnapshots sind byteidentisch (`14154C512DED47C4AD72B651F8FCB0BA702EACBF8678C51DDAB7D125BEC675D4`); 12.441/12.468 Referenzen sind aufgelöst, 27 Verwendungen sind sichtbare Kompatibilitätsfälle
- atomarer, inkrementeller Runtimeimport mit kanonischen IDs/Pfaden, SHA-256, Overlayherkunft, Importerversion, Artefakten, Abhängigkeiten und typisierten Fallbacks
- zwei unabhängige Frischimporte sowie ein 25.682/25.682-Cachelauf erzeugten dasselbe kanonische Manifest; Details stehen in `docs/CONTENT-PIPELINE.md`
- typisierte UV-, Environment-, Classic-Environment-, Linear-Offset- und Grid-Mapper; positive und negative Geschwindigkeiten sowie beide Stage-Slots werden deterministisch ausgewertet
- echter Stride-/Direct3D-11-Pfad mit geordneten Mehrfachpässen, bis zu zwei Texture-Stages je Pass, DCG/SCG-Streams, animierten UV-Puffern, Alpha-Test, allen Blendfaktoren des Bestands, Depth-Compare/-Write und Two-Sided-Culling
- case-insensitive DDS-/TGA-Auflösung, sichtbarer Magenta-Fallback mit Diagnose, Texturcache und vollständige GPU-Ressourcenfreigabe
- reproduzierbare lokale PNG-Regressionen für Mehrpass/Alpha/Additiv, zweistufige Detailmaterialien, Linear-/Grid-Animation und Stage-1-Animation; Baseline-Hashes stehen in `docs/W3D-MATERIALS.md`
- Strides Standard-Shader werden reproduzierbar durch den offiziellen Assetcompiler gebaut; Game Studio ist nicht erforderlich

## Lokale Verzeichnisse

- `.local/game-original`: unveränderte Installationskopie
- `.local/assets/by-archive`: getrennt nach Ursprungsarchiv entpackte Daten
- `.local/content`: zusammengeführter, offen lesbarer Laufzeit-Content ohne BIG-Container
- `.local/manifests`: reproduzierbare Datei- und Archivlisten
- `references`: nur lesbare Downloads von EA und den Community-Projekten

## Befehle

```powershell
dotnet build CnCNew.slnx
dotnet run --project tests/CnCNew.Archives.Tests
dotnet run --project tests/CnCNew.Content.Tests
dotnet run --project tests/CnCNew.Data.Tests
dotnet run --project tests/CnCNew.IO.Tests
dotnet run --project tests/CnCNew.Images.Tests
dotnet run --project tests/CnCNew.Formats.Tests
dotnet run --project tests/CnCNew.Import.Tests
dotnet run --project tools/CnCNew.AssetTool -- inventory .local/game-original .local/manifests/game-original.json
dotnet run --project tools/CnCNew.AssetTool -- extract-all .local/game-original .local/assets/by-archive
dotnet run --project tools/CnCNew.AssetTool -- materialize .local/game-original .local/content
dotnet run --project tools/CnCNew.AssetTool -- audit-formats .local/content
dotnet run --project tools/CnCNew.AssetTool -- audit-dependencies .local/content
dotnet run --project tools/CnCNew.AssetTool -- runtime-import .local/game-original .local/phase2-import-a
dotnet run --project tools/CnCNew.AssetTool -- verify .local/phase2-import-a
dotnet run --project tools/CnCNew.IniTool -- audit .local/content
dotnet run --project tools/CnCNew.IniTool -- compile .local/phase2-import-a .local/phase3-definitions.json
dotnet run --project tools/CnCNew.ImageTool -- audit-tga .local/content
dotnet run --project tools/CnCNew.W3dTool -- audit .local/content
dotnet run --project tools/CnCNew.W3dTool -- inspect .local/content Art/W3D/ABArFrcCmd.W3D
dotnet run --project tests/CnCNew.W3D.Tests -- --content-dir .local/content
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir .local/content --model-path Art/W3D/ABArFrcCmd.W3D
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir .local/content --model-path Art/W3D/AVSoloDrn.W3D --capture .local/visual-verification/animated-stage1.png
```

Der Stride-Assetcompiler 4.3.0.2507 bringt derzeit einige von NuGet als verwundbar gemeldete transitive **Buildwerkzeuge** mit. `NU1901` und `NU1903` bleiben beim Client-Build sichtbar, werden dort aber nicht als Fehler behandelt. Sie sind kein stillschweigend ignorierter Befund und müssen bei einem Stride-Update oder einer isolierten Content-Pipeline entfernt werden.

## Rechtliche Trennung

Der EA-Quellcode und die Community-Projekte stehen unter GPLv3 mit jeweils zu prüfenden Zusatzbedingungen. Markenrechte und Rechte an den ausgelieferten Medien/Assets werden dadurch nicht übertragen. Da dieses Projekt derzeit ausschließlich lokal verwendet wird, werden keine Original-Assets verteilt.

BIG-Dateien sind ausschließlich Eingabe für den einmaligen lokalen Import. Das neue Spiel liest zur Laufzeit normale Dateien aus `.local/content`; `content-manifest.json` hält die gewählte Overlay-Quelle und alle verdrängten Varianten nachvollziehbar fest.

Der typisierte Definitionsvertrag, Include-/Overlayregeln und das Snapshotformat stehen in
`docs/TYPED-DEFINITIONS.md`; die vollständige, nicht stille Phase-3-Kompatibilitätsstufe steht in
`docs/PHASE3-COMPATIBILITY.md`.

