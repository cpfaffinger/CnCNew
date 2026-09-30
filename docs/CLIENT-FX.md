# Stride-Client: Effektschicht (`FxLayer`)

Stand: 30. September 2026 (Paket S3)

`CnCNew.Client.Stride.Layers.FxLayer` zeichnet, was `CnCNew.Presentation.Fx` (`docs/PRESENTATION-FX.md`) pro Frame
liefert: Partikel (kamera- oder bodenausgerichtete Sprites, Streaks), Tracer und Bodendecals (Brandflecken). FX-Modelle
liefert `FxFrame` nicht (im Retail-Bestand ungenutzt, siehe unten); Mündungsfeuer sind reguläre Partikelsysteme und
brauchen keinen Sonderfall. Der Layer liest nur, was `FxExecutor.Capture` als `FxFrame` herausgibt, und schreibt nie in
die Simulation oder die Präsentation zurück.

## Architektur

```
FxExecutor.Capture(isVisible)          FxLayer.Update
        │  FxFrame                            │
        ▼                                      ▼
   FxBuilder.Build  ──── quads ────▶  FxGeometry (ein dynamischer Vertexpuffer, statischer Indexpuffer)
        │  Runs (Material, Quad-Bereich, Tiefe)
        ▼
   FxRunPool.Draw  ──── zeigt gepoolte Entities auf Quad-Bereiche
```

- **`FxVertex`/`FxGeometry`** (`Layers/Fx/FxVertex.cs`, `FxGeometry.cs`): Position, gepacktes RGBA, eine UV-Koordinate;
  jedes Primitiv ist ein Quad (4 Ecken, 6 statische Indizes). Ein CPU-Array wird pro Frame befüllt, nur das genutzte
  Präfix wird mit `SetData` (WriteDiscard) hochgeladen. `DefaultMaxQuads = 32768`; ein voller Puffer zählt
  `DroppedQuads`, statt Quads stillschweigend zu verlieren.
- **`FxMaterials`** (`FxMaterials.cs`): vier Stride-Materialien für die W3D-Sprite-Shader-Presets (`Additive`, `Alpha`,
  `AlphaTest`, `Multiply`), unlit über `MaterialEmissiveMapFeature(UseAlpha=true)` (Textur × Vertexfarbe oder nur
  Vertexfarbe), manueller `BlendState` je Preset, `DepthFunction=LessEqual`, `CullMode=None`. `AlphaTest` nutzt
  `MaterialTransparencyCutoffFeature` mit Referenz 96/255 wie `dwShaderPresets[SHADER_ALPHATEST]` in `pointgr.cpp` und
  schreibt Tiefe; die drei Blend-Presets schreiben keine Tiefe (`RenderServices.DepthWrites`, gemeinsam mit
  `W3dDepthWritePipelineProcessor`).
- **`FxRunPool`** (`FxRunPool.cs`): pro Materialschlüssel ein wachsender Pool von Einzel-Mesh-Entities, die alle denselben
  Vertex-/Indexpuffer referenzieren. Jeder „Run“ (zusammenhängender Quad-Bereich mit einem Material) bekommt pro Frame
  einen freien Slot; `StartLocation`/`DrawCount` zeigen auf den Bereich, die Bounding Box liegt an der Zielposition auf
  der Sichtachse. Ungenutzte Slots werden deaktiviert (`ModelComponent.Enabled = false`), nie entfernt, damit der
  transparente Renderstage sie zwischen zwei Frames wiederverwendet ohne neu zu allozieren.
- **`FxBuilder`** (`FxBuilder.cs`): baut aus einem `FxFrame` die Quads und Runs (siehe unten).
- **`FxStats`/`FxStatsTotals`** (`FxStats.cs`): Zahlen eines Frames beziehungsweise deren Lauf-Aggregation
  (Spitzenwerte, Mittelwerte, aufsummierte Skip-Zähler). `FxLayer.Dispose` druckt eine Zeile, wenn `--smoke-test` in den
  Kommandozeilenargumenten steht.
- **`FxDemo`** (`FxDemo.cs`): Diagnosemodus über die Umgebungsvariable `CNCNEW_FX_DEMO` (siehe unten).

`FxLayer` reicht als Kamerafrustum-Filter für `FxExecutor.Capture` eine nicht allozierende Closure herein
(`Frustum.Intersects` gegen eine Box um Partikelposition/-größe). Damit fließen nur sichtbare Partikel überhaupt in den
Build-Schritt; die Kultierten stehen trotzdem in `ParticleFrame.Culled` und in den Statistiken.

### Reihenfolge und Sortierung

`FxBuilder.Build` folgt der Zeichenreihenfolge der W3D-Partikelpasses:

1. **Decals** zuerst (`EmitDecals`): pro Decal ein an ein Höhenraster gelegtes Polygon (`n = clamp(⌈2R/Zellgröße⌉, 1, 8)`
   Zellen je Achse, jede Ecke auf `TerrainHeightSampler.HeightAt` plus 0,3 Einheiten Abhebung), frustumgefiltert über eine
   erweiterte Aabb, Alphablend, absteigende Tiefe in festen Schritten (die am weitesten entfernten zuerst), Verblassen
   über `ExpireFrame`/`FadeFrames`.
2. **Bodenausgerichtete Sprites und Alphatest-Sprites** (`EmitUnsortedParticles`): ein allozierungsfreier Countingsort
   nach Batch erhält die Instanzreihenfolge innerhalb eines Batches; diese Gruppe wird nicht global sortiert (wie die
   Referenz zieht `SHADER_ALPHATEST` per Tiefentest und bodenausgerichtete Partikel werden nicht einsortiert), sondern
   nur pro Batch zu einem Run zusammengefasst.
3. **Alle kamerafest geblendeten Sprites, Streaks und Tracer** (`EmitSorted`) in einer gemeinsamen Rückwärts-nach-vorn-
   Sortierung nach Tiefe (`Array.Sort` über vorbelegte Arrays, kein LINQ). Aufeinanderfolgende Einträge mit demselben
   Material werden zu einem Run zusammengefasst; damit folgt die Zahl der Draw Calls den Texturwechseln entlang der
   sortierten Liste, nicht der Partikelzahl. Ab `FxBuilder.MaxSortedRuns` (512) Runs wird der Rest der sortierten Liste
   stattdessen pro Material gruppiert (weiterhin tiefensortiert *innerhalb* jeder Gruppe); das hält die Slotzahl auch in
   Extremfällen beschränkt und wird als `DeferredItems` gezählt.

Die Tiefe eines Runs steuert die Position der gepoolten Entity auf der Sichtachse (`eye + forward * D`), an der der
transparente Renderstage seinen eigenen Rückwärts-nach-vorn-Vergleich der Bounding-Box-Zentren vornimmt
(`ModelComponent`/`ModelRenderProcessor`, siehe `Rendering/W3dDepthWritePipelineProcessor.cs`). Decal-Runs bekommen
Tiefen nahe der Fernebene in absteigenden festen Schritten (Reihenfolge der Emission), sortierte Runs bekommen den
Mittelwert ihrer Item-Tiefen, geklemmt auf `[Near·1,5, Far·0,98]` und mit einem Mindestabstand zum vorherigen Run, damit
zwei Runs nie dieselbe Tiefe bekommen.

### Primitive

- **Kamerafeste Sprites**: Ecken um die Achsen `a = right·cosθ + up·sinθ`, `b = -right·sinθ + up·cosθ`, Kantenlänge
  `size` (nicht Radius), UV TL(0,0) BL(0,1) BR(1,1) TR(1,0) — wie `PointGroupClass::Render` (`pointgr.cpp`).
- **Bodenausgerichtete Sprites**: dieselbe Formel mit den Weltachsen X/Y statt Kameraachsen; jede Ecke wird zusätzlich um
  die Geländehöhendifferenz zur Mitte verschoben, damit das flache Quad an einer Steigung nicht schwebt oder versinkt.
- **Streaks**: ein Polyline-Band durch die Partikel eines Systems in Instanzreihenfolge (`W3DParticleSystemManager` reicht
  sie in dieser Reihenfolge an `LineRenderer`, siehe `W3DParticleSys.cpp`); die Breite folgt der Partikelgröße, die
  UV läuft über die gelaufene Distanz. Die erste Kante wird wie im Original auf RGBA 0 gesetzt (`RGBAArray[0] = 0`), um
  die harte Abrisskante am Ende zu entfernen. Ein Streak-Segment mit weniger als 2 Punkten wird verworfen und gezählt
  (`SkippedShortStreaks`).
- **Tracer**: kamerafestes Band von `Head` bis `Head + Direction·Length`, ungetexturt, Alphablend, Mindestbreite
  1,5 Pixel (aus `WorldPerPixelAtUnitDepth`, damit ein Tracer auf große Entfernung nicht unter ein Pixel schrumpft).
- **Additive Partikel** geben nur RGB aus (`ParticleShader.Additive` ignoriert Alpha wie `ParticleSys.cpp`, das den
  Alpha-Keyframe-Fortschritt für additive Systeme überspringt); `VolumeParticle` wird wie ein normales Sprite gezeichnet
  (die Referenz variiert hier nur die Emission, nicht das Rendering).

### Lineare Farbraumkorrektur

Ist `GraphicsDevice.ColorSpace == Linear`, werden die gepackten RGB-Vertexfarben über eine 256-Byte-sRGB→linear-Tabelle
umgerechnet (Alpha bleibt unverändert), damit `Textur × Vertexfarbe` im linearen Pipeline dieselbe Optik ergibt wie die
Referenz im Gammaraum. Die Tabelle wird einmal pro `FxBuilder` gebaut.

## Bekannte Grenzen (nie stillschweigend, immer gezählt)

| Was | Warum nicht gezeichnet | Zähler |
|---|---|---|
| `ParticleKind.Drawable` | Kein W3D-Partikelmodell-Renderpfad in diesem Paket; im Retail-Bestand 1 von 1.087 Partikelsystemen betroffen | `SkippedDrawable` |
| `ParticleKind.Smudge` | Bildschirmraum-Verzerrung (Backbuffer-Sample), kein Postprocessing-Zugriff in diesem Paket | `SkippedSmudge` |
| `ParticleShader.None`, leere Textur | Kein Zeichenzustand definierbar | `SkippedUnusable` |
| `LightPulseInstance` | Keine gepoolten Punktlichter in diesem Paket; die Zahl aktiver Pulse wird gezählt, nicht gezeichnet | `NotDrawnLights` |
| `RayEffectRequest` | Im Retail-Bestand 0 Verwendungen (`docs/PRESENTATION-FX.md`); Beam-Drawables sind nicht Teil dieses Pakets | `NotDrawnRays` |
| FX-Modelle | `FxFrame` liefert keine (kein FxNuggetKind dafür); Mündungsfeuer sind Partikelsysteme und werden regulär gezeichnet | – |

`FxExecutor.Capture` liefert `RayEffects` und `ViewShakes` einmalig aus (sie werden danach geleert). `FxLayer` ist der
einzige Aufrufer von `Capture` in der Stride-Client-Schicht und hält das zuletzt aufgenommene `FxFrame`
(`FxLayer.LastFrame`) für spätere Konsumenten (Kamera-Shake, Beam-Rendering) bereit, statt es zu verwerfen.

## Diagnosemodus `CNCNEW_FX_DEMO`

Für eine schnelle Sichtprüfung ohne auf ein echtes Gefecht zu warten:

- `CNCNEW_FX_DEMO=ring` (oder `1`): alle 45 Logikframes wird über die öffentliche API von `match.Fx`
  (`Particles.Spawn`, `ExecuteFxListAtPosition`, `Decals.AddScorch`) ein Ring aus Effekten um das Kamerazentrum gelegt:
  je ein Partikelsystem-Template pro (Shader, Kind, Bodenausrichtung)-Klasse (erstes Vorkommen nach Namen sortiert), die
  ersten zwei FXLists mit einem `Tracer`-Nugget, und vier Brandflecken unterschiedlicher Typen. Die Konsole druckt einmal
  die gewählten Templates/FXLists.
- `CNCNEW_FX_DEMO=bench`: baut einen synthetischen Frame mit 10.000 Partikeln (gemischte Materialien, Boden- und
  kamerafeste Ausrichtung, 100 in Streaks) und misst die mittlere Zeit von `FxBuilder.Build` über 400 Wiederholungen;
  es wird nichts gespawnt.

Beide Modi laufen nur, wenn die Variable gesetzt ist, und ändern nichts an der Simulation.

## Gemessene Werte

Headless-Capture, `.local/phase2-import-a`, Karte „Alpine Assault“, 1600×900, `dotnet build src/CnCNew.Client.Stride -c
Release` (0 Fehler, gleiche 21 Warnungen wie vor diesem Paket – alle bekannten `NU1901`/`NU1903`- und
AssetCompiler/YamlAssemblyRegistry-Hinweise, keine neuen):

- **Demoring** (`CNCNEW_FX_DEMO=ring`, 400 Frames, `--height 120 --pitch 50`): letzter Frame 2.457 aktive Partikel, 32
  kulliert, 1.516 gezeichnet, 9 Streaks, 4 Decals, 7 Batches, 24 Runs, 24 Draw Calls, 2.557 Quads, 245.472 Byte
  Upload, 0,23 ms CPU (Build+Pool+Upload); Lauf-Mittel 0,50 ms, Maximum 53,9 ms (JIT-Einschwingen der ersten Frames).
  Zwei unabhängige Läufe mit identischen Argumenten ergeben dieselbe Bildschirmausgabe:
  SHA-256 `6614963C3EC78E46ADBE101795F1358A7556041CAD6FFAB7211B73778098D2F4` (beide Läufe).
- **Echtes Skirmish** (`--autoplay --seed 7 --faction FactionGLA --opponent FactionChina`, 12.000 Frames = 400 s):
  über den Lauf 2 Tracer und 42 Lichtpulse (nicht gezeichnet, siehe Tabelle oben) protokolliert, 0 abgebrochene Quads,
  0 unbekannte Batches. Zwei unabhängige Läufe mit identischen Argumenten ergeben dieselbe Statistikzeile und dasselbe
  Bild: SHA-256 `C5855B1BF20E2492C91F200BA4293808200274CF9E86E687845808DF7736E704` (beide Läufe). Die Kamera steht auf dem
  Standardblick über die Kartenmitte; das Gefecht selbst fand außerhalb des Sichtfelds statt, weshalb im Bild keine
  Effekte sichtbar sind, obwohl die Pipeline sie verarbeitet hat (ehrlich in der Statistik ausgewiesen, nicht im Bild).
- **CPU-Budget für 10.000 aktive Partikel** (`CNCNEW_FX_DEMO=bench`, Mittel über 400 Wiederholungen eines synthetischen
  Frames mit gemischten Materialien, zufälliger Tiefe und 1.000 Streak-Partikeln in 100 Streaks): `fx bench: 10000 live
  particles, 5400 drawn, 9900 quads, 517 runs, 1.735 ms per build (avg of 400)` — innerhalb des Budgets von 2 ms CPU,
  obwohl die zufällige Tiefenreihenfolge in diesem Extremfall (5 Materialien, praktisch zufällig verschachtelt) die
  Run-Obergrenze `FxBuilder.MaxSortedRuns` (512) erreicht und den Rest der Liste pro Material gruppiert; ein
  laufzeitabhängiger Wert, bei Bedarf neu messen.

Diese Werte sind Momentaufnahmen (siehe CLAUDE.md, „Kein Vertrauen auf Zahlen aus Docs“); bei Änderungen an Partikel-
katalog, Karte oder Budget neu messen.

## Öffentliche API dieses Pakets

- `FxLayer` implementiert `IWorldLayer` (`Attach`, `Update`, `Dispose`) wie die anderen Weltschichten.
- `FxLayer.Stats` (`FxStats`): Zahlen des zuletzt gezeichneten Frames.
- `FxLayer.LastFrame` (`FxFrame?`): die zuletzt aufgenommene Momentaufnahme, einschließlich der einmalig ausgelieferten
  Ray-/ViewShake-Anforderungen.
- Alles andere unter `Layers/Fx/**` (`FxGeometry`, `FxMaterials`, `FxRunPool`, `FxBuilder`, `FxStats`, `FxDemo`) ist
  `internal` und nur für diese Schicht gedacht.

## Quellenverweise

- `pointgr.cpp` (`PointGroupClass::Render`): Quadaufbau, UV-Reihenfolge, Kantenlänge = `Size`, Shader-Presets
  (`dwShaderPresets`), Alphatest-Referenz 96/255.
- `W3DParticleSys.cpp`: Übergabe der Streak-Punkte an `LineRenderer`, `RGBAArray[0] = 0`.
- `ParticleSys.cpp` (`Particle::update`, `ParticleSystemTemplate`): additive Systeme überspringen den
  Alpha-Keyframe-Fortschritt; Partikelgröße ist die volle Kantenlänge, nicht der Radius.
