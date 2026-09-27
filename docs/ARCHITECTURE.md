# Zielarchitektur

Die Neuimplementierung wird schichtenweise aufgebaut. Der Simulationskern kennt weder DirectX noch Windows-UI. Dadurch kann er deterministisch, schnell und ohne Grafik getestet werden.

1. `CnCNew.Archives` dient nur als lokales Importwerkzeug für die vorhandenen Spieldaten.
2. Ein Import-VFS führt lose Dateien und BIG-Archive einmalig in originaler Priorität zusammen und materialisiert normale Dateien. Die Laufzeit kennt keine BIG-Container.
3. `CnCNew.Content` öffnet zur Laufzeit ausschließlich normale Dateien anhand von `content-manifest.json`. Das Projekt referenziert weder den Import-VFS noch `CnCNew.Archives`.
4. `CnCNew.Data` übersetzt INI-Definitionen in validierte, unveränderliche Templates.
5. `CnCNew.W3D` validiert Chunkbäume und decodiert Meshgeometrie, Shaderzustände, Vertexmaterialien, Material-/Shader-IDs, diffuse Vertexfarben, Prelit-Varianten, Texturtabellen und UV-Zuordnungen unabhängig vom Renderer. Reparaturen bekannter Auslieferungsschäden erfolgen ausschließlich im Arbeitsspeicher und werden anschließend erneut vollständig validiert.
6. `CnCNew.Images` decodiert Legacy-Bildformate, die Stride nicht direkt unterstützt. Der TGA-Pfad verarbeitet die ausgelieferten unkomprimierten und RLE-komprimierten 24-/32-Bit-Dateien zu normalisiertem RGBA.
7. `CnCNew.Simulation` verwaltet Welt, Objekte, Behaviors, Befehle und festen Tick.
8. `CnCNew.Client.Stride` projiziert ausschließlich Simulations-Snapshots in die zunächst gewählte Stride-Engine. Der W3D-Pfad gruppiert Dreiecke nach Textur und Shader, führt DCG und Vertexmaterial-Diffuse/Opacity in `COLOR0` zusammen und bildet Alpha-Test, Blend, Depth-Compare/-Write sowie Two-Sided-Culling auf Stride-Zustände ab. Der Client baut Strides Standard-Shader mit `Stride.Core.Assets.CompilerApp` in seine lokale Runtime-Datenbank; Game Studio ist keine Buildvoraussetzung.
9. Stride übernimmt Direct3D, Fenster, Eingabe, Audio und Rendering; Legacy-Kompatibilität bleibt in unseren Bibliotheken. Der Editor ist optional.

Der Community-Patch wird nicht pauschal über die Originaldaten kopiert. Jede Änderung bleibt einer Overlay-Schicht und einem dokumentierten Fix zugeordnet. So sind Originalverhalten, Bugfix-Modus und optionale Balanceänderungen vergleichbar.

