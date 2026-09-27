# W3D-Materialvertrag

Stand: 27. September 2026

## Rendererunabhängige Semantik

`CnCNew.W3D` erhält die Reihenfolge aller Materialpässe und Texture-Stages. Der Renderplan gruppiert
ein Dreieck anhand des vollständigen Zustands aller Pässe; ein späterer Pass wird deshalb nicht als
unabhängiger Batch sortiert. W3D definiert höchstens zwei Texture-Stages pro Pass. Mehr als zwei
Stages sind strukturell ungültig und werden beim Decoding mit Meshdiagnose abgewiesen, nicht erst
im Client mit `NotSupportedException`.

Shaderwerte sind typisierte Enums. Unterstützt und getestet sind alle W3D-Werte für Depth Compare,
Depth Write, Alpha-Test, Source-/Destination-Blend, Primary/Secondary Gradient, Detail Color,
Detail Alpha, Post-Detail Color und Post-Detail Alpha. Der Generals-PC-Lader kopiert beim Laden die
alten `Detail*`-Felder in seinen aktiven Post-Detail-Zustand und ignoriert die serialisierten
`PostDetail*`-Bytes; CnCNew erhält beide verlustfrei und verwendet dieselbe effektive Regel.

Die Dateibytes `ColorMask`, `FogFunc`, `ShaderPreset` und Padding wurden von den Werkzeugen als
PlayStation-2-Parameter A bis D wiederverwendet. Der EA-PC-Lader setzt Farbschreiben immer an und
Datei-Fog immer aus. CnCNew bildet deshalb nicht die häufigen Rohwerte `0/2/2/2` fälschlich als
PC-Renderzustand ab. Die Rohwerte bleiben als `W3dLegacyShaderMetadata` verfügbar.

DCG, DIG und SCG werden getrennt validiert. DCG und Vertexmaterial-Diffuse/Opacity bilden den
primären Color-Stream; SCG bildet den sekundären Gradienten. Unbekannte Shaderwerte fallen nie
stillschweigend auf einen Standardwert zurück.

## Mapper

Der ausgelieferte Bestand verwendet:

- UV;
- Environment (Camera-Space Reflection Vector);
- Classic/Cheap Environment (Camera-Space Normal);
- Linear Offset mit `UPerSec`, `VPerSec`, `UOffset`, `VOffset`, `UScale`, `VScale`, `ClampFix`;
- Grid mit `FPS`, `Log2Width` beziehungsweise der ausgelieferten Schreibweise `Log1Width`, `Last`
  und `Offset`.

Mapperart und Slot stammen aus den Vertexmaterial-Attributbits. Argumente werden invariant und
streng typisiert; unbekannte, doppelte oder ungültige Werte erzeugen eine Diagnose. Linear Offset
übernimmt die historische negative Zeitrichtung des EA-Renderers. Positive und negative
Geschwindigkeiten, Wrap/Clamp, Grid-Frames und beide Slots sind automatisiert getestet.

Gültige, aber im Bestand nicht verwendete Mapperarten (unter anderem Rotate, Sine, Step, ZigZag,
Screen, World-Space- und Bump-Varianten) werden mit genauer Diagnose abgewiesen. Ebenso sind die
Primary-Gradient-Modi Bump Environment und Bump Environment Luminance typisiert, benötigen aber
einen Tangent-Space-Bumppfad und werden außerhalb des auditierten Bestands bewusst hart
diagnostiziert. Add und Modulate2X sind implementiert und CPU-semantisch getestet.

## Stride-Abbildung

Ein kombinierter W3D-Batch wird zu einem Stride-Material mit einem Stride-Pass je W3D-Pass.
Die Passreihenfolge bleibt erhalten. Vier UV-Streams (`pass * 2 + stage`) und vier Color-Streams
(primär/sekundär je Pass) verhindern, dass ein späterer Pass UVs oder Vertexfarben des ersten
Passes erbt. Blend, Depth Compare, Depth Write, Alpha-Test und Culling werden pro Pass gesetzt.
Animated Linear-/Grid-Mapper aktualisieren dynamische Vertexpuffer aus der expliziten Renderzeit.

Texturen werden über das case-insensitive Runtime-Content-Dateisystem gesucht. Kandidaten sind die
angegebene virtuelle Referenz, `Art/Textures/<Datei>` sowie gleichnamige DDS- und TGA-Dateien.
Fehlt eine Textur, erscheint eine genaue Diagnose und ein sichtbares Magenta-Schachbrett; der
Smoke-Test schlägt zusätzlich fehl. Geladene Texturen werden pfadbasiert gecacht und zusammen mit
Vertex-/Indexpuffern deterministisch freigegeben.

## Vollbestandsabnahme

```powershell
dotnet run --project tests/CnCNew.W3D.Tests -- --content-dir .local/content
dotnet run --project tools/CnCNew.W3dTool -- audit .local/content
```

Der Audit verarbeitet 8.904/8.904 Dateien, 70.587 Meshes, 76.212 Materialpässe, 74.374
Texture-Stages, 77.672 Shader und 80.041 Vertexmaterialien. Es gibt 64.962 Einpass- und 5.625
Zweipass-Meshes; pro Pass kommen 2.155-mal keine, 73.740-mal eine und 317-mal zwei Stages vor.
Der Bestand enthält 113.824 DCG-Sätze; DIG und SCG kommen in den ausgelieferten Dateien nicht vor,
ihr Decoder- und Renderpfad ist deshalb über synthetische Regressionstests abgedeckt.
Mapperverteilung: Stage 0 enthält 77.389 UV-, 2.397 Environment-, 253 Linear- und zwei
Grid-Mapper; Stage 1 enthält 79.933 UV- und 108 Linear-Mapper.

## Visuelle Regression

Captures liegen ausschließlich im ignorierten Verzeichnis `.local/visual-verification`; weder
Originalassets noch daraus gerenderte Bilder werden committed. Der Capture-Zeitpunkt verwendet
im Smoke-Modus einen festen 60-Hz-Zeitschritt. Zwei unabhängige Läufe des animierten Modells waren
bitidentisch.

| Pfad | Modell | SHA-256 |
| --- | --- | --- |
| Mehrpass, Alpha-Test, Additiv, Environment | `ABArFrcCmd.W3D` | `9FC7420083E96542CD96ADBCACDF5BD310EC2426E3B389F4144FB20C570E8C22` |
| Zwei Stages, Detailfunktion | `AbbtCmdHQA.W3D` | `D40C4B03135A5AB5D950E7E81AE7E66874EE45FE31C4070B1D82D43E7A079ECC` |
| Animierter Mapper in Stage 1 | `AVSoloDrn.W3D` | `A24DBFEC49658F1ECCDBE99B5D0346F45FA56CD9AF5FE05EC839CE49BFCE7E90` |
| Grid-Mapper | `ExEMPSphere.W3D` | `A6EB428C26002075AC311654985F98597674662DD84E3A3D989F46C6EA35DFD2` |

Beispiel:

```powershell
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test `
  --content-dir .local/content --model-path Art/W3D/AVSoloDrn.W3D `
  --capture .local/visual-verification/animated-stage1.png
```
