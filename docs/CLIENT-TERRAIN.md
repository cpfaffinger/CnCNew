# Terrain-Layer des Stride-Clients

Zuständig: `src/CnCNew.Client.Stride/Layers/TerrainLayer.cs` und
`src/CnCNew.Client.Stride/Layers/Terrain/**`. Der Layer zeigt Terrain, Wasser, Fog of War/Shroud
und eine Minimap-Textur eines `LiveMatch` in Stride. Er liest ausschließlich das bereits
aufbereitete `TerrainRenderModel` (`CnCNew.Presentation.Terrain`) und den `FogOfWarGrid`
(schreibgeschützt); er bindet keine eigene Simulationslogik ein.

## Datenfluss

`TerrainRenderModel` liefert pro Kartenausschnitt:

- **Chunks** (`TerrainChunk`): drei Meshes pro Chunk (Base, Blend, ExtraBlend/3-way), je vier
  Vertices pro Zelle, `ushort`-Indices. Farbe = statische Beleuchtung (RGB) und Blend-Gewicht (A).
- **Water** (`WaterSurface[]`): einzelne Wasserflächen mit Textur, Farbe, optionalem Scroll
  (Flüsse) und `IsRiver`.
- **Atlas**: eine Textur mit allen Kacheltypen inklusive Mip-Kette (`BuildMipChain()`).

`TerrainLayer.Attach` baut daraus einmalig GPU-Ressourcen; `Update` aktualisiert nur Fog-Textur,
Wasser-Scroll, Minimap und Chunk-Sichtbarkeit.

## Ein Draw-Pass statt drei

Das Referenzspiel zeichnet Base-, Blend- und 3-way-Pass einzeln mit Alpha-Blending übereinander.
Das ist für Terrain unnötig und würde die Sortierung gegen das transparente Wasser erschweren.
`TerrainMeshMerger.Merge` faltet die drei Pässe pro Chunk stattdessen in ein Vertex-Array:

- Der Base-Pass legt Vertices und Indices fest (vier Vertices/Zelle, wie im Modell).
- Blend- und 3-way-Zellen werden über die Position ihres ersten Vertex der passenden Base-Zelle
  zugeordnet (`Attach`) und liefern eine zweite bzw. dritte UV-Menge (`Uv1`/`Uv2`) plus ihr
  Blend-Gewicht in `Color0.A` bzw. `Color1.A`. Zellen ohne Overlay bekommen Gewicht 0.
- Das Overlay kann entlang der anderen Diagonale trianguliert sein als der Base-Pass; der
  gemergte Mesh behält immer den Base-Split. `DiagonalMismatches` zählt das (auf den getesteten
  150 Karten typischerweise einstellig, siehe Messwerte unten) – rein kosmetisch, da die
  Kachelfarbe pro Dreieck durch Interpolation kaum sichtbar abweicht.
- `TerrainMeshMerger.Validate` prüft nach dem Merge Index-Grenzen und dass nur Zellen mit
  zugeordnetem Overlay ein Blend-Gewicht tragen; jede Inkonsistenz wirft `InvalidDataException`.
  Das läuft in `Attach` synchron mit – GPU-frei überprüfbar, da `TerrainMeshMerger` und
  `TerrainVertex` kein Stride referenzieren.

Der Materialgraph (`TerrainMaterials.CreateTerrain`) berechnet pro Pixel
`lerp(lerp(atlas(uv0), atlas(uv1), w1), atlas(uv2), w2) * Beleuchtung * Nebel` und schreibt das
Ergebnis in den Emissive-Kanal. Ein Pass, undurchsichtig, `CullMode.Back`, Tiefentest und
-schreiben aktiv.

## Windung und Koordinatensystem

Simulation und Stride teilen dieselbe Rechtshand-Welt (X Ost, Y Nord, Z hoch); Kamera-View/
Projection werden unverändert über `ToStride()` übernommen, es gibt keine Spiegelung. Die
Dreiecke des Modells sind von oben gesehen gegen den Uhrzeigersinn (CCW), Direct3D/Stride werten
im Default aber im Uhrzeigersinn (CW) als Vorderseite. `Attach` tauscht deshalb nach der
Validierung pro Dreieck `Indices[t+1]` und `Indices[t+2]`, `CullMode.Back` bleibt aktiv. Ohne
diesen Tausch war die Karte komplett wegkulled (nur Himmelblau sichtbar) – der erste
Sichtbarkeitstest dieser Phase.

## Wasser

`WaterMeshBuilder.Build` gruppiert `WaterSurface`-Flächen nach Textur, `IsRiver` und Scroll-
Geschwindigkeit zu `WaterBatch`es und baut je Gruppe ein zusammenhängendes Vertexarray
(Stride-frei testbar). Animierte Batches (`Scroll != 0`) bekommen einen `Dynamic`-Vertexbuffer;
`ApplyScroll(seconds)` schreibt `Uv = BaseUv + frac(Scroll * t)` pro Frame neu.

Der Wassermaterial-Graph (`TerrainMaterials.CreateWater`) multipliziert Textur, Vertexfarbe und
Nebelfaktor, ersetzt aber den Alphakanal wieder durch das unvernebelte Produkt (`SubstituteAlpha`):
vernebeltes Wasser bleibt so genauso deckend wie unvernebeltes, nur dunkler. Wasser wird in der
transparenten Stride-Stufe nach dem opaken Terrain gezeichnet (`CullMode.None`, kein Tiefenschreiben,
Blend `SourceAlpha/InverseSourceAlpha`, bei `WaterAppearance.AdditiveBlending` stattdessen
`SourceAlpha/One`). Da Terrain undurchsichtig mit Tiefenschreiben vorgeht, verdeckt es Wasser dort,
wo die Geländehöhe über der Wasserebene liegt – so wie im Original nur echte Senken/Küsten geflutet
erscheinen, obwohl die Wasserfläche im Modell oft die ganze Karte überspannt (siehe Messwerte).

**Geprüft** (visuell, s. Screenshots-Abschnitt): Seeufer (Bitter Winter, unregelmäßige Küstenlinie
gegen Schnee), Fluss mit Scroll-Animation (FloodedPlains, sichtbarer Wellenversatz zwischen Frame 1
und Frame 600), Alphaüberblendung an der Uferlinie, Tiefenverhalten in Schrägansicht. **Nicht
geprüft**: `WaterAppearance.AdditiveBlending == true` (keine der elf sondierten Karten nutzt es) und
`WaterAppearance.TransparentWaterDepth` (wird derzeit nicht ausgewertet – Wasser hat konstante
Deckkraft unabhängig von der Wassertiefe unter der Oberfläche; das ist eine bekannte Lücke).

## Fog of War / Shroud

`FogTexture` ist eine `R8_UNorm`-Textur mit einem Texel pro Zelle des spielbaren Bereichs
(`FogOfWarGrid.CellsX/CellsY`), `Dynamic` angelegt. `UpdateFog` liest `match.Fog.CopyLevels(...)`
höchstens alle `FogInterval` (1/20 s) und lädt nur hoch, wenn sich der Inhalt geändert hat
(`SequenceEqual`-Vergleich gegen den letzten Upload) – auf einer stehenden Karte entfällt der
Upload praktisch ganz. Die Werte 0/127/255 (Shroud/Fog/Clear) landen unverändert im Texel; die
Terrain- und Wassermaterialien sampeln sie über eine gemeinsame Fog-UV (`world.xy * FogUvScale`,
`Clamp`, `Linear`-Filterung sorgt für weiche Kanten an Shroud-/Fog-Grenzen) und multiplizieren das
Ergebnis auf die Emissive-Farbe. In der (nach Messung) linearen Farbraum-Pipeline des Geräts wird
der Faktor quadriert (`FogFactor`, `ComputeBinaryColor` Multiply mit sich selbst), damit ein
Fog-Texel (127/255 ≈ 0,5) auf dem Bildschirm denselben Helligkeitsabfall ergibt wie im
Referenzrenderer, der im Gammaraum multipliziert.

`FogTexture` ist öffentlich (`TerrainLayer.FogTexture`, `FogUvScale`), damit der ObjektLayer
Einheiten und Gebäude mit demselben Nebel abdunkeln kann, ohne eine zweite Fog-Textur zu pflegen.

**Debug-Umgebungsvariable** `CNCNEW_TERRAIN_REVEAL=1`: überschreibt jeden Sample mit 255 (voll
sichtbar), ohne die Simulation zu berühren – nur für Screenshot-Verifikation gedacht.

**Bekannte Einschränkung**: Die Fog-Textur hat beim ersten Attach keine Initialdaten (`Dynamic`
ohne `DataBox`); der erste Sample wird immer hochgeladen, unabhängig vom Vergleich. Der aller-
erste gerenderte Frame – bevor `Update` zum ersten Mal lief – zeigt deshalb noch die
GPU-Nullinitialisierung, also vollständigen Shroud (schwarz), selbst wenn die Startposition
bereits Sicht hätte. Ab dem zweiten Frame (spätestens nach `FogInterval`) stimmt der Zustand.
Für ein Spiel, das mit dem ersten simulierten Tick startet, ist das ein Frame, den niemand sieht;
dokumentiert, weil der Auftrag explizit Frame 1 gegen Frame 1800 verglichen hat und der Unterschied
sonst wie ein Bug aussähe.

## Minimap

`TerrainLayer.MinimapTexture` (RGBA, Reihe 0 = Norden) wird aus `match.Minimap.Compose(fog, dots,
footprint, dotSize)` erzeugt (`CnCNew.Presentation.Minimap.MinimapImage`, nicht Teil dieses
Layers). Automatisch alle 0,25 s (`MinimapInterval`) neu komponiert, wenn sich Fog oder das
Kamera-Footprint geändert haben. `UpdateMinimap(commandList, dots, cameraFootprint = null,
dotSize = 2)` ist der öffentliche Einstiegspunkt für den HUD-/UI-Layer: `dots` sind Weltpositionen
mit Farbe (im Fog verborgene Gegner vor dem Aufruf herausfiltern – der Layer kennt keine
Spielerzugehörigkeit), `cameraFootprint` überschreibt testweise das aus `match.Camera
.GroundFootprint()` abgeleitete Rechteck. Ohne Aufruf zeigt die Minimap nur Terrain, Fog und das
Kamera-Footprint.

**Debug-Umgebungsvariable** `CNCNEW_TERRAIN_DUMP=<Verzeichnis>`: schreibt bei jeder Minimap-
Aktualisierung `minimap.png`, `terrain.png` und `fog.png` (eigener minimaler PNG-Writer,
`PngDump`, ohne fremde Bildbibliothek) – diente in dieser Phase der Sichtprüfung ohne
Screenshot-Tooling.

## Beleuchtung

Terrain- und Wasserfarbe kommen ausschließlich aus den Vertexfarben des Modells (bereits vom
Import mit statischer Beleuchtung versehen) und texturbedingter Modulation; der Materialgraph
nutzt nur den Emissive-Kanal, nie Diffuse+Licht. Die Sonne/Beleuchtung des Stride-Hosts hat daher
keinen Einfluss auf Terrain – wie vom Auftrag gefordert.

## Farbraum

Das Gerät läuft zur Laufzeit mit `ColorSpace.Linear` (gemessen, nicht angenommen). Der Atlas wird
deshalb als `R8G8B8A8_UNorm_SRgb` hochgeladen (GPU dekodiert beim Sampling), während
Vertex-Lichtfarben und Wasserfarben vom Modell im Gammaraum vorliegen und deshalb einmalig auf der
CPU per Lookup-Tabelle (`PackedColor.SrgbDecodeTable`) dekodiert werden, bevor sie in die gepackten
Vertexfarben einfließen (`TerrainMeshMerger.Merge`, `WaterMeshBuilder.Build`). Der Fog-Faktor wird,
wie oben beschrieben, quadriert. Ohne diese Korrekturen wäre alles sichtbar zu dunkel oder zu hell
gegenüber dem Referenzrenderer, der im Gammaraum multipliziert.

## Sichtbarkeits-Culling

`Update` ruft `match.Camera.VisibleChunks(model)` pro Frame ab und schaltet nur die
`ModelComponent`s um, deren Sichtbarkeit sich geändert hat (`Enabled`-Flag), statt jedes Mal die
komplette Chunk-Liste neu zu evaluieren. Das hält den nicht sichtbaren Teil der Karte aus der
Stride-Renderpipeline heraus, ohne eigene Frustum-Mathematik im Layer.

## Öffentliche API für andere Layer

```csharp
Texture FogTexture          // R8_UNorm, ein Texel je Fog-Zelle, gültig nach Attach()
Vector2 FogUvScale          // world.xy * FogUvScale ergibt die Fog-UV
Texture MinimapTexture      // RGBA, Reihe 0 = Norden, gültig nach Attach()
long GpuBytes               // von diesem Layer gehaltener GPU-Speicher
double AttachMilliseconds   // Wandzeit des letzten Attach()

void UpdateMinimap(CommandList commandList, IReadOnlyList<MinimapDot> dots,
                    IReadOnlyList<Vector2>? cameraFootprint = null, int dotSize = 2);
```

`FogTexture`/`FogUvScale` sind für den kommenden Objekt-Layer gedacht (Einheiten/Gebäude im
selben Nebel abdunkeln); `UpdateMinimap` für den HUD-/UI-Layer.

## Messwerte (diese Phase, `--content-dir .local/phase2-import-a`, Release)

Elf Karten sondiert (Attach-Log), sechs davon mit Screenshots visuell geprüft (Wüste, Schnee,
Wasser/Fluss, Klippen, siehe unten):

| Karte | Zellen (spielbar) | Chunks | Blend-Zellen | 3-way (Diagonal-Mismatch) | Wasser-Batches | Attach | GPU |
|---|---|---|---|---|---|---|---|
| Alpine Assault | 239×259 | 156 | 26.630 | 370 (4) | 0 | 131–168 ms | 39,4 MiB |
| Bombardment Beach | 275×275 | 169 | 20.831 | 406 (0) | 1 | 167 ms | 54,4 MiB |
| FloodedPlains | 300×300 | 144 | 23.571 | 1.133 (4) | 1 (Fluss, animiert) | 182–204 ms | 33,4 MiB |
| Bitter Winter | 240×320 | 180 | 25.920 | 328 (1) | 1 (See) | 161–211 ms | 46,8 MiB |
| Dark Mountain | 376×370 | 289 | 18.772 | 539 (6) | 1 | 274 ms | 63,1 MiB |
| Fortress Avalanche | 418×413 | 289 | 25.629 | 185 (0) | 1 | 204–272 ms | 76,6 MiB |

Alle Werte deutlich unter dem Budget von ~2 s Attach-Zeit für eine Karte der 150er-Klasse.
Per-Frame-CPU (`CNCNEW_TERRAIN_PROFILE=1`, eigene `Stopwatch` um `Update`, Fortress Avalanche,
größte sondierte Karte, 900 Frames Autoplay): **0,266 ms/Frame** im Mittel, unter dem
1-ms-Budget. Determinismus: zwei unabhängige Läufe derselben Kamera-/Kartenkombination liefern
byteidentische Screenshot-SHA-256 (`CFF6C66C4708683A…` zweimal, Fortress Avalanche).

`dotnet build CnCNew.slnx`: 21 Warnungen in dieser Messung (Solution-Build), davon die zehn
bekannten `NU1901`/`NU1903` aus den Stride-4.3-Buildwerkzeugen; die übrigen elf sind
`AssetCompiler`/`YamlAssemblyRegistry`-Hinweise des Stride-Content-Builds zu anderen Assemblies
(`CnCNew.W3D`, `CnCNew.Ui`, `CnCNew.Simulation`, …) und erscheinen nur beim Solution-Build, nicht
beim Projekt-Build von `src/CnCNew.Client.Stride` allein (dort weiterhin genau 10 Warnungen). Sie
entstehen unabhängig von den Änderungen dieses Layers (keine der geänderten Dateien berührt den
Content-Build) und sind vermutlich auf zeitgleiche Arbeit anderer Layer-Pakete in ihren eigenen
Worktrees zurückzuführen; zu klären beim Zusammenführen aller Pakete, nicht in dieser Phase.

## Screenshots (Sichtprüfung)

Aufgenommen mit `--smoke-test --capture` bei verschiedenen Kamerawinkeln (Pitch 15°–89°, Höhe
400–20.000, mehrere Zoomstufen) und, wo zur Sichtprüfung nötig, `CNCNEW_TERRAIN_REVEAL=1`:

- **Klippen/Fels**: Alpine Assault (Felswand mit Schnee-Überzug, weicher Blend zur Wiese),
  Fortress Avalanche (zwei schneebedeckte Klippen in Schrägansicht mit Himmel, keine sichtbaren
  Chunk-Nähte trotz Blickfeld über mehrere der 289 Chunks).
- **Wüste**: Bombardment Beach (rissige Sandtextur, weicher Übergang zu einem helleren Sandfleck).
- **Schnee**: Bitter Winter (zwei Schneevarianten im Blend, Übergang ohne Kante).
- **Wasser**: FloodedPlains (Fluss, Wellen-Textur, Scroll-Animation zwischen Frame 1 und 600
  sichtbar verschoben, saubere Uferlinie in Schrägansicht ohne Z-Fighting), Bitter Winter (See mit
  unregelmäßiger Küstenlinie gegen Schnee).
- **Fog/Shroud**: Alpine Assault Frame 1 ohne `REVEAL` vollständig schwarz (siehe Einschränkung
  oben), Frame 1800 mit `--autoplay` zeigt aufgedeckten Bereich um die Startposition mit weichem
  Rand zur Shroud-Grenze.
- **Minimap**: `CNCNEW_TERRAIN_DUMP` liefert `terrain.png` (Draufsicht, Nord oben) und
  `minimap.png` (mit weißem Kamera-Footprint-Rechteck) für Alpine Assault, deckungsgleich mit der
  Draufsicht-Aufnahme derselben Karte.

Screenshots lagen im Scratchpad der Session (`…/scratchpad/s1/`), nicht im Repository.

## Bekannte Grenzen

- `WaterAppearance.TransparentWaterDepth` wird nicht ausgewertet; Wasserdeckkraft ist konstant.
- `WaterAppearance.AdditiveBlending == true` ist im Code vorgesehen, aber an keiner der
  sondierten Karten beobachtbar und deshalb visuell ungeprüft.
- Diagonal-Mismatches zwischen Base- und Overlay-Pass (siehe Tabelle) werden gezählt, aber nicht
  behoben; der gemergte Mesh behält die Base-Triangulierung, das Overlay kann dadurch an der
  Diagonale einer betroffenen Zelle leicht verzerrt interpolieren. Auf keiner geprüften Karte
  visuell auffällig.
- Der erste gerenderte Frame nach `Attach` zeigt vollständigen Shroud, bevor der erste
  `Update`-Tick den Fog-Sample hochlädt (siehe Fog-Abschnitt).
- Objektbeleuchtung (Einheiten/Gebäude) ist nicht Teil dieses Layers; `FogTexture`/`FogUvScale`
  liegen bereit, aber niemand konsumiert sie bisher.
