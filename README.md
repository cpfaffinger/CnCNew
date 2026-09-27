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
- W3D-Materialpässe, 77.672 Shaderzustände, 80.041 Vertexmaterialien, Material-/Shader-IDs, diffuse Vertexfarben, Texturtabellen und UV-Daten werden validiert und rendererunabhängig decodiert
- alle vier Prelit-Materialwrapper werden als eigenständige Varianten erhalten; der ausgelieferte Bestand verwendet sie derzeit nicht
- fünf bereits ausgeliefert beschädigte `UISabotr_*`-Binärdateien werden verlustarm im Speicher rekonstruiert und erneut validiert
- eigener validierender TGA-Decoder erfolgreich über 826/826 Dateien und 100.666.368 Pixel geprüft
- echter Stride-/Direct3D-11-Smoke-Test: `ABArFrcCmd.W3D` wird aus dem offenen Content geladen und mit 21 Meshes sowie 14 zustandsabhängigen Originaltextur-Instanzen gerendert
- der W3D-Renderer übernimmt Vertexfarben/Opacity, Alpha-Test, Blendfaktoren, Depth-Compare/-Write und Two-Sided-Culling in Stride
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
dotnet run --project tools/CnCNew.AssetTool -- inventory .local/game-original .local/manifests/game-original.json
dotnet run --project tools/CnCNew.AssetTool -- extract-all .local/game-original .local/assets/by-archive
dotnet run --project tools/CnCNew.AssetTool -- materialize .local/game-original .local/content
dotnet run --project tools/CnCNew.IniTool -- audit .local/content
dotnet run --project tools/CnCNew.ImageTool -- audit-tga .local/content
dotnet run --project tools/CnCNew.W3dTool -- audit .local/content
dotnet run --project tools/CnCNew.W3dTool -- inspect .local/content Art/W3D/ABArFrcCmd.W3D
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir .local/content --model-path Art/W3D/ABArFrcCmd.W3D
```

Der Stride-Assetcompiler 4.3.0.2507 bringt derzeit einige von NuGet als verwundbar gemeldete transitive **Buildwerkzeuge** mit. `NU1901` und `NU1903` bleiben beim Client-Build sichtbar, werden dort aber nicht als Fehler behandelt. Sie sind kein stillschweigend ignorierter Befund und müssen bei einem Stride-Update oder einer isolierten Content-Pipeline entfernt werden.

## Rechtliche Trennung

Der EA-Quellcode und die Community-Projekte stehen unter GPLv3 mit jeweils zu prüfenden Zusatzbedingungen. Markenrechte und Rechte an den ausgelieferten Medien/Assets werden dadurch nicht übertragen. Da dieses Projekt derzeit ausschließlich lokal verwendet wird, werden keine Original-Assets verteilt.

BIG-Dateien sind ausschließlich Eingabe für den einmaligen lokalen Import. Das neue Spiel liest zur Laufzeit normale Dateien aus `.local/content`; `content-manifest.json` hält die gewählte Overlay-Quelle und alle verdrängten Varianten nachvollziehbar fest.

