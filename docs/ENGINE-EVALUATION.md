# Engine-Evaluierung

## Entscheidung für den ersten Vertical Slice: Stride

Stride wird als Rendering-, Fenster-, Eingabe-, Audio- und Editor-Unterbau erprobt. Die Entscheidung ist zunächst durch einen Vertical Slice abgesichert und bleibt revidierbar, weil Simulation und Dateiformate keine Stride-Typen referenzieren dürfen.

Der lokale Game-Studio-MSI-Schritt kann auf einzelnen Systemen mit Windows-Installer-Code 1619 scheitern. Der Client verwendet deshalb reproduzierbare NuGet-Pakete und benötigt Game Studio nicht zum Bauen oder Starten. Der Editor bleibt ein optionales Werkzeug.

Der code-only Client bindet den offiziellen `Stride.Core.Assets.CompilerApp` ein. Dadurch werden die Standard-SDSL-Shader der referenzierten Stride-Pakete beim Build case-sensitiv in die Runtime-Datenbank importiert. Der reale W3D-Smoke-Test lädt `Art/W3D/ABArFrcCmd.W3D`, erzeugt 21 Quellmeshes und rendert sie mit zustandsabhängiger Shader-/Materialgruppierung über Direct3D 11. Alpha-Test, Blendfaktoren, Depth-Compare/-Write, Vertexfarben und Two-Sided-Culling werden ohne Game Studio zur Laufzeit gesetzt. Die von NuGet gemeldeten `NU1901`-/`NU1903`-Befunde in transitiven Buildwerkzeugen von Stride 4.3.0.2507 bleiben sichtbar und sind als Upgrade-Aufgabe nachzuverfolgen.

### Gründe

- Open Source unter MIT-Lizenz.
- Engine und öffentliche Spiel-API sind C#-/.NET-zentriert.
- Die aktuelle Toolchain unterstützt .NET 10 und passt damit zur lokal installierten SDK-Version.
- Windows und Direct3D 11 werden direkt unterstützt; weitere Backends bleiben möglich.
- Szenen, Rendering, Shader, Audio, Eingabe, Content-Pipeline und Editor müssen nicht neu entwickelt werden.
- Standard-C#-Projektstruktur erleichtert Rider, Tests und Profiling.

### Was Stride nicht übernehmen soll

- deterministische RTS-Simulation und fester Simulations-Tick
- Generals-INI-Semantik, Objekt-Templates und Behavior-System
- BIG-Overlay-Priorität und Legacy-Dateiformate
- Wegfindungs- und Skirmish-KI-Kompatibilität
- Missionsskripte, Kampagnenzustand und späterer Netzwerk-Lockstep

Diese Bereiche bilden die Identität und Kompatibilität des Spiels. Sie bleiben in engine-unabhängigen .NET-Bibliotheken.

## Alternativen

### Godot

Godot ist MIT-lizenziert, ausgereift und unterstützt unter Windows Direct3D 12 beziehungsweise einen Direct3D-11-Kompatibilitätsrenderer. C# ist unterstützt, der Engine-Kern und die primäre Erweiterungsarchitektur sind jedoch nicht .NET-zentriert. Für dieses Projekt wäre die Grenze zwischen einem umfangreichen C#-Simulationskern und Godots Node-/Resource-Modell zusätzlicher Integrationsaufwand.

### MonoGame

MonoGame ist ein schlankes, bewährtes C#-Framework und unterstützt .NET 10. Es liefert jedoch bewusst wesentlich weniger Engine- und Editorfunktionalität. Terrain, Szenen-/Asset-Tools, Materialsystem, Partikel, UI und viele Produktionswerkzeuge müssten in größerem Umfang selbst gebaut werden. Es bleibt eine gute Rückfalloption, falls Strides Content-Pipeline die direkte Nutzung der Legacy-Assets zu stark behindert.

## Abbruchkriterien für den Stride-Spike

- W3D-Meshes und Texturen lassen sich nicht effizient zur Laufzeit oder offline zuführen.
- Große Terrain- und Einheitenmengen erzeugen nicht beherrschbare CPU-/GPU-Kosten.
- Strides Build-/Content-Pipeline erzwingt eine Kopplung des Simulationskerns.
- Ein reproduzierbarer, headless laufender Simulations-Test wird durch die Client-Integration verhindert.

## Vertical Slice

1. Fenster und freie RTS-Kamera unter Direct3D 11.
2. Eine extrahierte Skirmish-Heightmap samt einfacher Textur darstellen.
3. Ein W3D-Modell mit Material und Animation laden.
4. 100–500 einfache Einheiten aus einem headless Simulations-Snapshot zeichnen.
5. Auswahl, Move-Befehl und deterministischen festen Tick verbinden.

Erst wenn dieser Spike die Abbruchkriterien besteht, wird Stride zur dauerhaften Client-Engine erklärt.

