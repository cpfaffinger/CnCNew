# Weltpräsentation: Terrain, Kamera, Nebel des Krieges, Minimap

Paket C1 der Phase 8. `CnCNew.Presentation` (Namensräume `CnCNew.Presentation.Terrain`, `.Camera`, `.Vision`, `.Minimap`)
erzeugt aus dem dekodierten `MapData`, dem `CompiledDefinitionGraph` und dem Simulationszustand reine Daten
(`ImmutableArray`, `System.Numerics`, RGBA-Bytes). Der Stride-Client macht daraus GPU-Ressourcen; Stride-Typen kommen
hier nicht vor. Abhängigkeiten: `CnCNew.Data`, `CnCNew.Simulation` (nur `TerrainMap`, `VisionMap`, `Fixed`),
`CnCNew.Images` (`TgaReader`). Dateien werden über einen vom Aufrufer gelieferten `Func<string, byte[]?>` geladen;
Formats, Content und Archive werden nicht referenziert.

Alle Zahlen in diesem Dokument sind mit `PresentationTool terrain-audit` und `CnCNew.Presentation.Tests --content-dir`
auf `game/content` gemessen (Stand 30. September 2026).

## 1. Konventionen

- Weltraum wie in der Simulation: X Ost, Y Nord, Z oben, 1 Einheit = 1 Welteinheit, Zellgröße 10 (`MAP_XY_FACTOR`),
  Höhe = Höhenbyte × 0,625 (`MAP_HEIGHT_SCALE`). Welt (0, 0) ist der Höhenvertex (Border, Border)
  (`ADJUST_FROM_INDEX_TO_REAL`); die Borderzellen haben negative Koordinaten und werden mitgezeichnet.
- Dreiecke sind von oben (+Z) gesehen gegen den Uhrzeigersinn orientiert.
- Matrizen: `System.Numerics`, Zeilenvektoren (`clip = world * ViewProjection`), rechtshändig, Clip-Tiefe 0..1
  (`Matrix4x4.CreateLookAt`, `CreatePerspectiveFieldOfView`). Stride verwendet dieselbe Zeilenvektor-Konvention; ein
  Y-oben-Szenengraph muss die Z-oben-Welt einmal drehen oder die Kameramatrizen direkt übernehmen.
- Bildschirm: Pixel, Ursprung oben links, Y nach unten.
- Texturen: RGBA8, Zeile 0 = Texturkoordinate v = 0 (oben).

## 2. Terrain-Renderdaten

Einstieg: `TerrainRenderModel.Build(MapData map, TerrainTextureLookup textures, TerrainRenderOptions options, string name)`.

- `TerrainRenderOptions.FromDefinitions(graph)` liest Beleuchtung, Wasser, `AdjustCliffTextures` und
  `Use3WayTerrainBlends` aus `GameData.ini`/`Water.ini`.
- `TerrainTextureCatalog.FromDefinitions(graph)` liest die `Terrain`-Blöcke aus `Terrain.ini` (Terraintyp → Datei unter
  `Art/Terrain/`, wie `WorldHeightMap::readTexClass`). `TerrainTextureLookup(catalog, loadFile)` dekodiert und cacht die TGAs.

Ergebnis `TerrainRenderModel`:

| Feld | Inhalt |
| --- | --- |
| `Atlas` (`TerrainAtlas`) | RGBA8-Atlas, Breite 2048, Höhe = belegte Höhe auf Vielfaches von 64 gerundet; `BuildMipChain()` liefert Box-gefilterte Mips bis 1×1 |
| `Regions` | Position jeder Texturklasse im Atlas |
| `Chunks` (`TerrainChunk`) | 32×32 Höhenzellen je Chunk mit `Bounds` (AABB) und bis zu drei Pässen |
| `Water` (`WaterSurface`) | Wasserflächen als Dreiecksnetze mit UV-Animation |
| `CellColors` | Durchschnittsfarbe der Basiskachel je Höhenzelle (Radarfarbe) |
| `TextureIssues`, `Stats` | gezählte Diagnosen und Kennzahlen |

Ein Pass ist ein `TerrainMesh(Positions, Normals, Colors, Uv, Indices)`: vier Vertices je Zelle (Ecken (x,y), (x+1,y),
(x+1,y+1), (x,y+1)), weil benachbarte Zellen verschiedene Texturkoordinaten haben; 16-Bit-Indizes (höchstens
32·32·4 = 4096 Vertices je Chunk). `Colors` trägt die statische Beleuchtung in RGB und das Blendgewicht in A.

### 2.1 Texturierungsdesign und Begründung

Das Original zeichnet (`HeightMapRenderObjClass::updateVB`, `WorldHeightMap::getUVData/getAlphaUVData`) jede Zelle mit
einer Basiskachel und einer überlagerten Blendkachel, deren Deckkraft pro Ecke als Vertex-Alpha 0 oder 255 vorliegt und
über das Dreieck linear interpoliert wird; ein dritter Pass (`renderExtraBlendTiles`) legt die 3-Wege-Blends darüber.
Genau diese Struktur wird übernommen, weil sie ohne eigenen Shader darstellbar ist:

1. **Base** – opak, Tiefenschreiben an, Textur = Atlas mit UV-Satz des Passes, Farbe = Vertexfarbe (Beleuchtung).
2. **Blend** – nur Zellen mit Blendkachel; Alpha-Blending `SrcAlpha/InvSrcAlpha` mit Vertex-Alpha, Tiefentest
   kleiner-gleich, kein Tiefenschreiben, gleicher Atlas.
3. **ExtraBlend** – wie Blend, für die 3-Wege-Kacheln (`m_extraBlendTileNdxes`).

Ein einfaches Stride-`Material` mit Diffuse = Textur × Vertexfarbe und Transparenz aus dem Vertex-Alpha genügt; die
Pässe teilen keine Vertexpuffer, dafür braucht der Client keinen zweiten UV-Kanal und keine eigenen Shader. Ein Atlas
statt vieler Einzeltexturen hält die Drawcalls bei einem pro Chunk und Pass (wie das Original, `TEXTURE_WIDTH` 2048).

Regeln (neu geschrieben nach den genannten Referenzfunktionen, GeneralsGameCode `Core/GameEngineDevice/.../WorldHeightMap.cpp`,
`HeightMap.cpp`, `TerrainTex.cpp`):

- **Kacheln** (`readTexClass`, `readTiles`, `updateTileTexturePositions`): Eine Klasse mit `TileCount` Kacheln lädt die
  größte Quadratzahl w×w ≤ `TileCount` Kacheln zu 64×64 Pixeln; die Datei muss mindestens so viele ganze Kacheln haben.
  Die Referenz liest die erste gespeicherte Pixelzeile immer als unterste Zeile und ignoriert die TGA-Ursprungsbits; das
  wird nachgebildet (`TerrainTextureImage.FlippedVertically`, im Bestand 0 Fälle). Kachel `firstTile + i + j·w` liegt im
  Klassenblock bei (i·64, (w−1−j)·64). Jeder Klassenblock bekommt einen 4-Pixel-Rand, der die gegenüberliegende Kante
  wiederholt (`TILE_OFFSET` = 8), damit Filterung und die ersten Mips nicht in Nachbarklassen bluten.
- **Quadrant** (`getUVForNdx`): Kachelwert = Quellkachel·4 + Quadrant; Bit 1 wählt die obere, Bit 0 die rechte
  32×32-Hälfte; V ist gegenüber Y invertiert (U[0..3] = n,x,x,n; V[0..3] = x,x,n,n).
- **Blendmuster** (`getAlphaUVData`): horizontal, vertikal, rechte/linke Diagonale, lange Diagonale, invertiert (Bit 0);
  Bit 1 (`FLIPPED_MASK`) erzwingt die Diagonalumkehr bei aktivem `Use3WayTerrainBlends`. Nicht invertierte rechte und
  invertierte linke Diagonalen kippen die Zelldiagonale. Die Diagonale gilt für Base und Blend gemeinsam; ohne Kippen
  läuft sie wie in der Simulation von Ecke 0 nach Ecke 2.
- **Klippen** (`getUVForTileIndex`): Hat die Zelle einen `CliffInfo`-Eintrag derselben Texturklasse, ersetzen dessen
  vier UVs (in `TEXTURE_WIDTH`-Einheiten relativ zur linken unteren Ecke des Klassenblocks) die Quadranten-UVs. Sonst
  streckt die alte UV-Korrektur (`DO_OLD_UV`) steile Zellen ab 1,5 Höheneinheiten Streckung (Diamant 2,4, zwei-hoch-
  zwei-tief 2,0, Grenze 4 Kacheln) innerhalb des Klassenblocks. Beide Fälle liegen im Base- bzw. Blendpass, einen
  eigenen Klippenpass braucht es nicht. Wird gestreckt, bestimmt `|p0−p2| > |p1−p3|` die Diagonale.
- **Fehlende Texturen**: `getUVForNdx` liefert U = V = 0; die Zelle zeigt Texel (0, 0) des Atlas, bei Referenz und hier
  derselbe Randpixel der zuerst platzierten Klasse (hier wird die Texelmitte benutzt, damit der Filter nicht umbricht).
  Jede nicht auflösbare Klasse steht mit Grund in `TextureIssues`.
- **Benutzerdefinierte Blendkanten** (`customBlendEdgeClass`): Alpha 0 wie in der Referenz; der Kantenpass mit den
  Edge-Texturen fehlt. Im Bestand kommen solche Zellen nicht vor (0).

### 2.2 Beleuchtung

Statische Vertexbeleuchtung nach `BaseHeightMapRenderObjClass::doTheLight`: Ambient des ersten Lichts plus für jedes der
`NumberGlobalLights` (Engine-Vorgabe 3, `GlobalData.cpp`) Lichter `clamp(dot(−LightPos, n), 0, 1) · Diffuse`, auf [0, 1]
begrenzt. Normalen per Zentraldifferenz über zwei Zellen wie `updateVB`. Werte aus `GameData.ini`
`TerrainLighting<TimeOfDay>{Ambient,Diffuse,LightPos}[2|3]` für `TimeOfDay = AFTERNOON`. Dynamische Lichter fehlen.

### 2.3 Wasser

`WaterAppearance.FromDefinitions(graph, lighting)` liest `WaterSet <TimeOfDay>` und `WaterTransparency` aus `Water.ini`.
`WaterSurfaceBuilder` (aufgerufen von `Build`) erzeugt je Wasser-Polygontrigger ein Netz:

- Stehendes Wasser nach `WaterRenderObjClass::renderWater`/`drawTrapezoidWater`: Viereck (0, k, k+1, k+2) für ungerade k,
  je ein bilinearer Patch mit höchstens 50×50 Rechtecken zu je etwa 8 Zellen; Höhe = z der Polygonpunkte;
  UV = Position / 150; keine UV-Verschiebung.
- Flüsse nach `drawRiverWater`: Streifen zwischen den beiden Ufern ab `RiverStart`, U quer (0,5 / 0), V entlang des
  Flusses; `UvScrollPerSecond = (0, −0,06)` (`m_riverVOrigin += 0.002` je 30-Hz-Frame).
- Farbe: bei weißer `StandingWaterColor` Ambient + Σ(−LightPos.z·Diffuse) × `WaterSet DiffuseColor`, Alpha aus
  `DiffuseColor`; Textur `StandingWaterTexture` (`TWWater01.tga`). Wasser zweiseitig zeichnen (Referenz: `D3DCULL_NONE`).

Nicht nachgebildet: das leichte UV-Wabern (±0,02) des stehenden Wassers, weiche Wasserkanten über Ziel-Alpha
(`TransparentWaterDepth`, Werte werden mitgeliefert), die Vertex-Wasserflächen der vier Kampagnenkarten aus `GameData.ini`
(`VertexWater*`), Himmelsreflexion.

### 2.4 Höhenabtaster

`TerrainHeightSampler(MapHeightField)` liefert `HeightAt(x, y)` und `NormalAt(x, y)` in `float`, mit genau der
Dreiecksinterpolation und Randklemmung von `TerrainMap.HeightAt` (`WorldHeightMap::getHeightMapHeight`, zwei Dreiecke je
Zelle entlang (ix,iy)-(ix+1,iy+1)); bilinear wäre gegenüber der Simulation um bis zu eine halbe Höhenstufe falsch.
Gemessen: größte Abweichung zur Festkomma-Simulation über alle Zellecken und -mitten aller 150 Karten 0,0050
(Toleranz 0,01, Float-Rundung bei Koordinaten bis 7000). Die gerenderte Fläche weicht in gekippten Zellen (Blend-/Klippen-
Diagonale, 1.029.741 von 30.259.192 Zellen) davon ab; Picking folgt bewusst der Simulationsfläche.
`Boundaries`/`BoundaryMax(i)` liefern die Kartengrenzen, `PlayableMax` die größte (= `TerrainMap.CellsX/Y`).

### 2.5 Speicher (gemessen, 150 Karten)

GPU-Schätzung = Vertices × 36 Byte (Position 12, Normale 12, Farbe 4, UV 8) + Indizes × 2 + Atlas mit voller Mipkette
(RGBA8 unkomprimiert) + Wasser (24 Byte/Vertex, 4 Byte/Index).

| Größe | Wert |
| --- | --- |
| größte Karte | `Maps/MD_GLA05/MD_GLA05.map`, 709×709 Zellen, 529 Chunks, 1.120.734 Dreiecke |
| davon Netze | 87.417.252 Byte (83,4 MB) |
| davon Atlas mit Mips | 24.466.708 Byte (23,3 MB), 2048×2240, 49 Texturklassen |
| gesamt größte Karte | 112.115.512 Byte (106,9 MB) – unter dem Ziel von 300 MB |
| Atlasgröße | 2048 × (192 … 2240), Kachel 64×64 Pixel, Zelle 32×32 Pixel |
| Summe aller Karten | Netze 5.039,8 MB, Atlanten 1.820,0 MB, Wasser 12,1 MB |

Mip-Strategie: volle Box-Mipkette wie `D3DXFilterTexture(D3DX_FILTER_BOX)`; ab etwa Stufe 3 (4-Pixel-Rand wird 0,5 Pixel)
mischen Klassengrenzen wie im Original. Kompression (BC1/BC3) würde den Atlas auf ein Viertel/Achtel senken, ist aber
Sache des Clients.

## 3. Kamera

`RtsCamera(RtsCameraSettings settings, TerrainHeightSampler terrain, Vector2 viewportSize)` implementiert
`CnCNew.Presentation.IViewProjection` (`ViewportSize`, `ViewProjection`, `GetScreenRay`, `TryScreenToGround`,
`TryWorldToScreen`; Datei `Camera/IViewProjection.cs`, früh committet und stabil für Paket C3). Nachgebildet ist die
Retail-Kamera der EA-Quellen (`GeneralsMD/.../W3DView.cpp`, `View.cpp`, `LookAtXlat.cpp`); die Community-Änderungen von
GeneralsGameCode (gemittelte Geländehöhe, `SCROLL_MULTIPLIER`) sind bewusst nicht übernommen.

| Parameter | Wert | Quelle |
| --- | --- | --- |
| `CameraPitch`, `CameraYaw` | 37,5°, 0° | `GameData.ini` |
| `CameraHeight` | 232 | `GameData.ini`; Offsethöhe H = Bodenhöhe + 232 (`initHeightForMap`) |
| `MaxCameraHeight`, `MinCameraHeight` | 310, 120 | `GameData.ini`, Höhe über Gelände |
| `CameraAdjustSpeed` | 0,3 je 30-Hz-Frame | `GameData.ini` (zeitunabhängig als 1−0,7^(dt·30)) |
| `ScrollAmountCutoff`, `EnforceMaxCameraHeight` | 50, No | `GameData.ini` |
| `Horizontal/VerticalScrollSpeedFactor` | 1,6 / 2,0 | `GameData.ini` |
| `KeyboardScrollSpeedFactor` | 2,0 (Startwert von `ScrollFactor`, Optionsmenü überschreibt) | `GameData.ini` |
| `KeyboardCameraRotateSpeed` | 0,1 rad je Frame | `GameData.ini` |
| `MaxShakeIntensity`, `MaxShakeRange`, `Shake*Intensity` | 10, 150, 0,5/1/2,5/5/7,5/10 | `GameData.ini` |
| horizontales Sichtfeld | 50° | `View::View` (`m_FOV`) |
| Near/Far | 10 / ≥ 1200 (bis zur fernsten Feldecke) | `W3DView::setCameraTransform` |
| Bodenhöhe | Gelände am Startpunkt, höchstens 120 | `W3DView::initHeightForMap` |
| Geländeprobe | Maximum aus Pivot und vier Diagonalpunkten im Abstand 40 | `getHeightAroundPos`, `TERRAIN_SAMPLE_SIZE` |
| Rad-Zoom | 10 je Raste | `View::zoomIn/zoomOut` |
| Scrollen | `SCROLL_AMT` 100, `SCROLL_RESOLUTION` 250, Bildrand 3 px | `LookAtXlat.cpp`, `W3DView::scrollBy` |
| Maus-Rotation | 0,01 rad je Pixel | `LookAtXlat.cpp` |
| Pitch-Grenzen | 0,1° … 89,9° | `View::setPitch` |

Modell (`buildCameraTransform`): Offset = (0, −H/tan(Pitch₀), H), um (Pitch − Pitch₀) um −X und um `Angle` um Z gedreht,
mal `Zoom`, mal `1 − Bodenhöhe/(H·Zoom)`; Ziel = (Pivot, Bodenhöhe). `Zoom` folgt `(Geländeprobe + HeightAboveGround)/H`;
beim schnellen Scrollen nur, wenn die Kamera unter die Mindesthöhe gerät (`Update(dt, isScrolling)`). Standardzoom ist die
Maximalhöhe (`setZoomToDefault`).

- Kartengrenzen (`calcCameraConstraints`): Der Pivot bleibt in der aktiven Kartengrenze (`ActiveBoundary`, Vorgabe 0),
  verkleinert um den Bodenabstand zwischen Bildmitte- und 95-%-Höhe-Strahl. Ist der Rand größer als die halbe Karte,
  wird die Achse mittig gehalten (Referenz: undefiniert).
- Scrollen: `KeyboardScrollDelta`, `EdgeScrollDelta`, `RightMouseScrollDelta` liefern die Referenz-Scrollwerte (je
  30-Hz-Frame, mit `dt` skaliert); `ScrollBy` bildet sie wie `scrollBy` über die Sichtebene im Abstand 1 und die
  Kamerarotation in die Welt ab.
- Picking: `GetScreenRay` (Ursprung auf der Near-Ebene, normierte Richtung) und `TryScreenToGround` (Viertelzellen-
  Schritte durch den Höhenbereich der Karte, danach 24 Bisektionen auf `HeightAt`; außerhalb des Höhenfelds kein
  Treffer). Brücken fehlen (siehe Grenzen). `InverseViewProjection` steht für eigene Strahlen bereit.
- Culling: `Frustum.FromViewProjection` (Gribb/Hartmann für Zeilenvektoren, Tiefe 0..1), `Intersects(Aabb)`;
  `VisibleChunks(model)` liefert die Chunkindizes.
- `GroundFootprint()`: Schnitt der vier Bildecken mit der Bodenebene (`getScreenCornerWorldPointsAtZ`) für die Minimap.
- Kamerawackeln (`ViewShake`-FX): `Shake(epicenter, intensity, directionRadians)` wie `W3DView::shake` (linearer Abfall
  bis `MaxShakeRange`, Feder mit Dämpfung 0,75 und Richtungsumkehr je Frame, oberhalb `MaxShakeIntensity` setzt die
  Referenz auf 3 zurück); der Zufallswinkel kommt vom Aufrufer. `CameraShakeIntensities.For("STRONG")` liefert die
  INI-Intensität. `AddShakeOffset` ist ein additiver Versatz für eigene Effekte.

## 4. Nebel des Krieges

`FogOfWarGrid(cellsX, cellsY, FogSettings)` bzw. `FogOfWarGrid.ForVision(vision, settings)`:

- `Update(VisionMap, playerSlot)` übernimmt pro Pfadzelle (10 Einheiten, Raster der Simulation) den Zustand
  `Unexplored`/`Fogged`/`Visible` (Bit 0 sichtbar, Bit 1 erkundet). Der Slot ist der Spielerindex der `VisionMap`.
- Helligkeitsstufen aus `GameData.ini`: `ShroudAlpha` 0, `FogAlpha` 127, `ClearAlpha` 255 (`W3DShroud`: Terrainfarbe ×
  Stufe/255). `Advance(dt)` blendet jede Zelle mit `FadeLevelsPerSecond` zur Zielstufe (eigene Entscheidung; das Original
  schaltet hart um; die Simulation aktualisiert alle 3 Ticks). `Snap()` springt sofort.
- Textur: `CopyLevels` ein Byte je Zelle, zeilenweise ab Zelle (0, 0) im Südwesten; Abbildung Welt → UV =
  (x/(CellsX·10), y/(CellsY·10)), bilinear filtern wie die Referenz. `BuildSmoothedLevels(factor)` liefert die bilinear
  vorgefilterte Variante.
- Abfragen: `StateAt`, `IsVisible`, `IsExplored`, `IsVisibleAt(world)`, `CellOf`, `CellCenter`. Fremde Einheiten werden
  nur in sichtbaren Zellen gezeichnet; Gebäude-Geister sind Sache des Objektpakets. Zellen außerhalb gelten als unerkundet.

## 5. Minimap

`MinimapImage.Build(model, sampler, terrainMap, radarWaterColor, size, boundary = 0)` nach `W3DRadar::buildTerrainTexture`:

- Bildausschnitt = aktive Kartengrenze (`W3DTerrainLogic::getExtent`), Seitenverhältnis folgt der Karte (längere Seite =
  `size`), Pixelzeile 0 = Nordrand. `WorldToPixel`/`PixelToWorld` für Klick-Kamera und Minimap-Befehle.
- Landpixel: Durchschnittsfarbe der Basiskachel (`getTerrainColorAt`, 64×64-Kachel auf 1 Pixel gemittelt), geschattet mit
  `interpolateColorForHeight`. Die Referenz übergibt dabei (Durchschnitt, Maximum, Minimum) als (hiZ, midZ, loZ); damit
  dunkelt Land vom höchsten Punkt abwärts bis 60 % ab – nachgebildet. Wasserpixel: `RadarWaterColor`, mit Tiefe
  abgedunkelt; je Pixel Mittel über die 3×3-Nachbarn (Wasser nur über Wassernachbarn). Klippen erscheinen nur über die
  Höhenschattierung wie im Original.
- `Compose(fog, dots, footprint, dotSize)`: Terrain × Nebelstufe, Einheitenpunkte (`MinimapDot`, Sichtbarkeit filtert der
  Aufrufer), Kamera-Footprint als weiße geschlossene Linie.

## 6. Audit

`dotnet run -c Release --project tools/CnCNew.PresentationTool -- terrain-audit <content-dir> [--map <pfad>]` baut die
Renderdaten jeder Karte, gibt je Karte und in Summe deterministische Zahlen aus (zwei Läufe byteidentisch bis auf die
ausgewiesene Zeitzeile) und prüft den Höhenabtaster gegen `TerrainMap.HeightAt`. Exitcode ≠ 0 bei Bau- oder
Abtasterfehlern und bei unerwarteten ungelösten Texturen.

Gemessen (150 Karten, alle gebaut): 31.747 Chunks, 30.259.192 Zellen, 135.501.748 Vertices, 67.750.874 Dreiecke
(60.518.384 Base), 3.556.938 Blendzellen, 59.307 3-Wege-Zellen, 0 Kantenblendzellen, 1.029.741 gekippte Zellen,
8.253.119 Klippenzellen (Pfadfinder-Bit), 59.505 Zellen mit gespeicherter Klippen-UV, 193.853 gestreckte Zellen,
7.251 Zellen ohne Textur, 0 ungültige Kachelverweise, 320 Wasserflächen (121 Flüsse, 508.370 Dreiecke), 0 gespiegelte
Terrain-TGAs, 0 TGAs mit ID-Feld; Laufzeit etwa 30 s (Release).

Ungelöste Texturen (5, alle mit Grund, als bekannte Datenfehler des Retail-Bestands geführt): Die Karten nennen die
Terraintypen `CliffLargeType5` (2 Karten), `CliffMediumType6`, `CliffMediumType7` (je 1), `DirtMediumType6` (5) und
`RocksType5b` (2), die in keiner INI-Datei definiert sind (`Terrain.ini` kennt nur Varianten wie `CliffMediumType6b`).
Die Referenz findet sie ebenfalls nicht (`findTerrain` liefert null) und zeichnet die Zellen mit UV 0; so auch hier.
Jeder andere ungelöste Name lässt das Audit fehlschlagen.

## 7. Grenzen

- Kartenbeleuchtung: Der `GlobalLighting`-Chunk der Karte wird vom Kartendecoder (`CnCNew.Formats`) nicht dekodiert; alle
  Karten nutzen die `GameData`-Beleuchtung. Nachtkarten wirken daher zu hell. Wunsch an Formats: Chunk dekodieren.
- `map.ini`-Überlagerungen (z. B. kartenspezifisches `WaterTransparency`) werden nicht berücksichtigt; es gilt der
  Retail-Basisgraph.
- Straßen (`MapObject.IsRoadPoint`, `Roads.ini`), Brücken (`IsBridgePoint`, Brückenmodelle, Brücken-Picking und
  Radarfarbe), Brandspuren/Scorches, Fahrspuren (`W3DTerrainTracks`), Terrain-Dekals, Makrotextur/Wolkenschatten,
  Himmel, Hintergrundterrain (`W3DTerrainBackground`) und Kantenblend-Texturen fehlen. Straßen und Brücken brauchen
  Objekt-Meshes und Kurvengeometrie aus `W3DRoadBuffer` und gehören in ein eigenes Paket.
- Shaderunterschiede: kein Mehrfachtexturen-Terrainshader, keine dynamischen Lichter, keine Unterwasser-Tiefenabdunklung
  (`m_useDepthFade`), keine Wasser-Weichkanten; die Blendpässe benötigen Alpha-Blending über dem Basispass.
- Die gerenderte Geländediagonale weicht in gekippten Zellen von der Simulationsfläche ab (Picking folgt der Simulation).
- Kamera: keine Skriptkamera (Wegpunktflug, Rotieren, Kamerasperre auf Objekte), kein Real-Zoom-Modus, kein
  `movePivotToGround`-Nachführen der Community-Fassung; der Nebel blendet weich statt hart.
- Minimap: gemittelte Geländehöhe über alle Landpixel statt jedes zweiten Radarpixels; keine Brückenfarbe; Ereignis-
  und Helden-Symbole sind Sache der UI.
- Speicher: vier Vertices je Zelle in bis zu drei Pässen (einfaches Material statt Shader); ein geteilter Vertexpuffer mit
  eigenem Shader würde die Netze etwa halbieren.

## 8. Integration (Stride-Client)

1. Graph laden → `TerrainRenderOptions.FromDefinitions`, `TerrainTextureCatalog.FromDefinitions`,
   `RtsCameraSettings.FromDefinitions`, `FogSettings.FromDefinitions(graph, fade)`, `CameraShakeIntensities.FromDefinitions`.
2. Karte dekodieren (`SageMapDecoder`), `TerrainRenderModel.Build`, `TerrainHeightSampler`, `TerrainMap.Build`.
3. Atlas (`BuildMipChain`) als eine Textur hochladen; je Chunk und Pass ein Mesh; Wasser mit eigener Textur und
   UV-Scroll.
4. `RtsCamera` je Frame: Eingaben → `ScrollBy`/`RotateBy`/`ZoomByWheel`, dann `Update(dt, isScrolling)`, Matrizen an
   Stride übergeben, `VisibleChunks` zum Culling.
5. Alle `VisionIntervalTicks`: `FogOfWarGrid.Update(world.Vision, slot)`; je Frame `Advance(dt)` und `CopyLevels` in
   eine R8-Textur.
6. Minimap einmal bauen, je Frame oder seltener `Compose`.

Wunsch an die Simulation (nur lesend): eine öffentliche Abbildung `PlayerId → Vision-Slot` (`SimulationWorld.SlotOf`
ist privat); bis dahin übergibt der Client den Slot selbst: Index des Spielers in aufsteigender `PlayerId`-Reihenfolge einschließlich des Systemspielers 0 auf Slot 0.
