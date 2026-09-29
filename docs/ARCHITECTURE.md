# Zielarchitektur

Die Neuimplementierung wird schichtenweise aufgebaut. Der Simulationskern kennt weder DirectX noch Windows-UI. Dadurch kann er deterministisch, schnell und ohne Grafik getestet werden.

1. `CnCNew.Archives` dient nur als lokales Importwerkzeug für die vorhandenen Spieldaten.
2. Ein Import-VFS führt lose Dateien und BIG-Archive einmalig in originaler Priorität zusammen und materialisiert normale Dateien. Die Laufzeit kennt keine BIG-Container.
3. `CnCNew.Content` öffnet zur Laufzeit ausschließlich normale Dateien anhand von `content-manifest.json`. Das Projekt referenziert weder den Import-VFS noch `CnCNew.Archives`.

`CnCNew.Formats` ergänzt die engineunabhängigen Reader für STR, MAP/SCB/WAK, WND, DDS, ANI,
WAV, MP3 und BIK. `CnCNew.Import` ist die einzige Verbindungsschicht zwischen diesen Readern,
Import-VFS und Archiven: Sie baut den Abhängigkeitsgraphen und publiziert Schema 2 atomar.
`CnCNew.Content` validiert kanonische `asset://`-IDs, Case-Kollisionen, Hashes, Overlayherkunft,
Importeroptionen, Artefakte, Abhängigkeiten und typisierte Fallbacks. Schema 1 ist ausschließlich
ein sichtbarer Migrationslesepfad. Logische Pfade und Windows-sichere physische Namen bleiben
getrennt. Der vollständige Vertrag steht in `docs/CONTENT-PIPELINE.md`.

4. `CnCNew.Data` übersetzt INI-Syntax über einen runtimepfadbasierten `IDefinitionSource` in den
   unveränderlichen `CompiledDefinitionGraph`. Includes, Bedingungen, Overlayvererbung und
   Add/Remove/ReplaceModule werden vor der Vertragsgrenze ausgewertet. Definitionen besitzen
   stabile typisierte IDs, Einheitenwerte, Modulkonfigurationen, Quellpositionen und validierte
   Referenzkanten; `IniNode` verlässt den Compiler nicht. `ModuleRegistry` führt den Katalog aller Modultypen
   (Besitzerarten, Schema, Interfacemaske, Zielphase) ohne Factories; die Bindung an Implementierungen liegt in `CnCNew.Simulation`. Der Compiler folgt der Retail-Ladereihenfolge des Originals, kopiert das `DefaultThingTemplate` mit Interfacemasken-Regeln
   in jedes Objekt und typisiert Object-Köpfe und implementierte Modulfelder streng über Namenstabellen aus den Referenzquellen.
   Kanonische Snapshots und jede noch nicht implementierte Semantik (Kompatibilitätsbasislinie `baselines/compatibility.json`)
   sind reproduzierbar auditierbar. Details stehen in
   `docs/TYPED-DEFINITIONS.md` und `docs/PHASE3-COMPATIBILITY.md`.
5. `CnCNew.W3D` validiert Chunkbäume und decodiert Meshgeometrie, Shaderzustände, Vertexmaterialien, Material-/Shader-IDs, DCG/DIG/SCG-Vertexfarben, Prelit-Varianten, Texturtabellen und UV-Zuordnungen unabhängig vom Renderer. Ein Renderplan gruppiert Dreiecke anhand ihrer vollständigen, geordneten Passzustände. Mapper werden aus Attributbits und streng validierten Argumenten zu rendererunabhängigen Typen kompiliert und aus expliziter Zeit deterministisch ausgewertet. Reparaturen bekannter Auslieferungsschäden erfolgen ausschließlich im Arbeitsspeicher und werden anschließend erneut vollständig validiert.
6. `CnCNew.Images` decodiert Legacy-Bildformate, die Stride nicht direkt unterstützt. Der TGA-Pfad verarbeitet die ausgelieferten unkomprimierten und RLE-komprimierten 24-/32-Bit-Dateien zu normalisiertem RGBA.
7. `CnCNew.Simulation` verwaltet Welt, Objekte, Behaviors, Befehle und festen Tick. Das Projekt hängt ausschließlich von
   `CnCNew.Data` ab: Zustand besteht nur aus Ganzzahlen und Q47.16-Festkomma, der einzige Zufallsstrom der Logik ist der
   Original-Generator (`logic`), es gibt keine Wandzeit, keine Threads und keinen Datei-, Archiv- oder Contentzugriff (ein
   Quelltexttest erzwingt das). Die Welt läuft in festen Tickphasen (Befehle, Timer, Updates, Events, Zerstören, Spawnen, Events,
   Hash); Mutationen während einer Iteration laufen über Queues. Module werden über eine explizite, sortierte,
   reflexionsfreie Bindungstabelle (Name → typisierte Factory) erzeugt, die beim Start gegen die Data-Deskriptoren geprüft wird;
   Präsentationsmodule (`W3D*Draw`, `*ClientUpdate`) werden nie instanziiert. Die Präsentation liest später Snapshots und Events
   und schreibt nur typisierte Befehle. Details in `docs/SIMULATION-CORE.md`. Die darauf aufsetzenden Spielregeln (Phase 5:
   Wirtschaft, Upgrades, Kampf, einfache Bewegung, Produktion, Spezialkräfte, Sieg/Niederlage) bleiben dieselbe Schicht – neue Module und
   Befehle, keine neue Abhängigkeit; Details in `docs/GAME-RULES.md`.
8. `CnCNew.Client.Stride` projiziert ausschließlich Simulations-Snapshots in die zunächst gewählte Stride-Engine. `W3dStrideScene`, `W3dMaterialFactory`, `W3dTextureCache` und der Depth-Write-Pipelineprozessor trennen Meshaufbereitung, Materialübersetzung, Texturlebensdauer und Pipelinezustände vom Programmstart. Ein W3D-Batch wird als ein Stride-Material mit geordneten Pässen ausgeführt; vier UV- und vier Color-Streams erhalten die je Pass unterschiedlichen Daten. Animierte Mapper aktualisieren dynamische Vertexpuffer aus der deterministischen W3D-Auswertung. Der Client bildet Alpha-Test, Source-/Destination-Blend, Depth-Compare/-Write und Two-Sided-Culling ohne stillen Enum-Fallback ab. Der Client baut Strides Standard-Shader mit `Stride.Core.Assets.CompilerApp` in seine lokale Runtime-Datenbank; Game Studio ist keine Buildvoraussetzung.
9. Stride übernimmt Direct3D, Fenster, Eingabe, Audio und Rendering; Legacy-Kompatibilität bleibt in unseren Bibliotheken. Der Editor ist optional.

Die W3D-Dateifelder `ColorMask`, `FogFunc`, `ShaderPreset` und Padding sind im ausgelieferten PC-Bestand PS2-Parameter-Speicher. Entsprechend dem EA-PC-Lader werden sie verlustfrei erhalten, aber nicht als PC-Farbmaske oder Fogzustand interpretiert; PC-Farbschreiben ist aktiv und Datei-Fog ist deaktiviert. Details, Bestandswerte und Bild-Baselines sind in `docs/W3D-MATERIALS.md` festgehalten.

Der Community-Patch wird nicht pauschal über die Originaldaten kopiert. Jede Änderung bleibt einer Overlay-Schicht und einem dokumentierten Fix zugeordnet. So sind Originalverhalten, Bugfix-Modus und optionale Balanceänderungen vergleichbar.

