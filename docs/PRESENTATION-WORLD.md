# World presentation: terrain, camera, fog of war, minimap

Package C1 of Phase 8. `CnCNew.Presentation` (namespaces `CnCNew.Presentation.Terrain`, `.Camera`, `.Vision`, `.Minimap`)
produces pure data (`ImmutableArray`, `System.Numerics`, RGBA bytes) from the decoded `MapData`, the `CompiledDefinitionGraph` and the simulation state.
The Stride client turns them into GPU resources; Stride types do not occur
here. Dependencies: `CnCNew.Data`, `CnCNew.Simulation` (only `TerrainMap`, `VisionMap`, `Fixed`),
`CnCNew.Images` (`TgaReader`). Files are loaded through a `Func<string, byte[]?>` supplied by the caller;
Formats, Content and Archives are not referenced.

All numbers in this document were measured with `PresentationTool terrain-audit` and `CnCNew.Presentation.Tests --content-dir`
on `game/content` (as of 30 September 2026).

## 1. Conventions

- World space as in the simulation: X east, Y north, Z up, 1 unit = 1 world unit, cell size 10 (`MAP_XY_FACTOR`),
  height = height byte × 0.625 (`MAP_HEIGHT_SCALE`). World (0, 0) is the height vertex (Border, Border)
  (`ADJUST_FROM_INDEX_TO_REAL`); the border cells have negative coordinates and are drawn as well.
- Triangles are oriented counter-clockwise when seen from above (+Z).
- Matrices: `System.Numerics`, row vectors (`clip = world * ViewProjection`), right-handed, clip depth 0..1
  (`Matrix4x4.CreateLookAt`, `CreatePerspectiveFieldOfView`). Stride uses the same row-vector convention; a
  Y-up scene graph has to rotate the Z-up world once or take over the camera matrices directly.
- Screen: pixels, origin top left, Y downwards.
- Textures: RGBA8, row 0 = texture coordinate v = 0 (top).

## 2. Terrain render data

Entry point: `TerrainRenderModel.Build(MapData map, TerrainTextureLookup textures, TerrainRenderOptions options, string name)`.

- `TerrainRenderOptions.FromDefinitions(graph)` reads lighting, water, `AdjustCliffTextures` and
  `Use3WayTerrainBlends` from `GameData.ini`/`Water.ini`.
- `TerrainTextureCatalog.FromDefinitions(graph)` reads the `Terrain` blocks from `Terrain.ini` (terrain type → file under
  `Art/Terrain/`, like `WorldHeightMap::readTexClass`). `TerrainTextureLookup(catalog, loadFile)` decodes and caches the TGAs.

Result `TerrainRenderModel`:

| Field | Content |
| --- | --- |
| `Atlas` (`TerrainAtlas`) | RGBA8 atlas, width 2048, height = occupied height rounded to a multiple of 64; `BuildMipChain()` delivers box-filtered mips down to 1×1 |
| `Regions` | position of every texture class in the atlas |
| `Chunks` (`TerrainChunk`) | 32×32 height cells per chunk with `Bounds` (AABB) and up to three passes |
| `Water` (`WaterSurface`) | water surfaces as triangle meshes with UV animation |
| `CellColors` | average colour of the base tile per height cell (radar colour) |
| `TextureIssues`, `Stats` | counted diagnostics and key figures |

A pass is a `TerrainMesh(Positions, Normals, Colors, Uv, Indices)`: four vertices per cell (corners (x,y), (x+1,y),
(x+1,y+1), (x,y+1)), because neighbouring cells have different texture coordinates; 16-bit indices (at most
32·32·4 = 4096 vertices per chunk). `Colors` carries the static lighting in RGB and the blend weight in A.

### 2.1 Texturing design and rationale

The original draws (`HeightMapRenderObjClass::updateVB`, `WorldHeightMap::getUVData/getAlphaUVData`) every cell with
a base tile and an overlaid blend tile whose opacity is present per corner as vertex alpha 0 or 255 and is
interpolated linearly over the triangle; a third pass (`renderExtraBlendTiles`) lays the 3-way blends on top.
Exactly this structure is adopted because it can be displayed without a shader of its own:

1. **Base** – opaque, depth write on, texture = atlas with the UV set of the pass, colour = vertex colour (lighting).
2. **Blend** – only cells with a blend tile; alpha blending `SrcAlpha/InvSrcAlpha` with vertex alpha, depth test
   less-or-equal, no depth write, same atlas.
3. **ExtraBlend** – like Blend, for the 3-way tiles (`m_extraBlendTileNdxes`).

A simple Stride `Material` with diffuse = texture × vertex colour and transparency from the vertex alpha is sufficient; the
passes share no vertex buffers, in return the client needs no second UV channel and no shaders of its own. One atlas
instead of many single textures keeps the draw calls at one per chunk and pass (like the original, `TEXTURE_WIDTH` 2048).

Rules (rewritten after the named reference functions, GeneralsGameCode `Core/GameEngineDevice/.../WorldHeightMap.cpp`,
`HeightMap.cpp`, `TerrainTex.cpp`):

- **Tiles** (`readTexClass`, `readTiles`, `updateTileTexturePositions`): a class with `TileCount` tiles loads the
  largest square number w×w ≤ `TileCount` of tiles of 64×64 pixels; the file must have at least that many whole tiles.
  The reference always reads the first stored pixel row as the bottom row and ignores the TGA origin bits; this
  is reproduced (`TerrainTextureImage.FlippedVertically`, 0 cases in the content). Tile `firstTile + i + j·w` lies in the
  class block at (i·64, (w−1−j)·64). Every class block gets a 4-pixel border that repeats the opposite edge
  (`TILE_OFFSET` = 8), so that filtering and the first mips do not bleed into neighbouring classes.
- **Quadrant** (`getUVForNdx`): tile value = source tile·4 + quadrant; bit 1 selects the upper, bit 0 the right
  32×32 half; V is inverted relative to Y (U[0..3] = n,x,x,n; V[0..3] = x,x,n,n).
- **Blend patterns** (`getAlphaUVData`): horizontal, vertical, right/left diagonal, long diagonal, inverted (bit 0);
  bit 1 (`FLIPPED_MASK`) forces the diagonal reversal when `Use3WayTerrainBlends` is active. Non-inverted right and
  inverted left diagonals flip the cell diagonal. The diagonal applies to Base and Blend together; without flipping
  it runs from corner 0 to corner 2 as in the simulation.
- **Cliffs** (`getUVForTileIndex`): if the cell has a `CliffInfo` entry of the same texture class, its
  four UVs (in `TEXTURE_WIDTH` units relative to the lower left corner of the class block) replace the quadrant UVs. Otherwise
  the old UV correction (`DO_OLD_UV`) stretches steep cells from 1.5 height units of stretch (diamond 2.4, two-high-
  two-low 2.0, limit 4 tiles) within the class block. Both cases lie in the Base or Blend pass, a
  cliff pass of its own is not needed. If stretching takes place, `|p0−p2| > |p1−p3|` determines the diagonal.
- **Missing textures**: `getUVForNdx` delivers U = V = 0; the cell shows texel (0, 0) of the atlas, for the reference and here
  the same border pixel of the class placed first (here the texel centre is used so that the filter does not wrap).
  Every class that cannot be resolved is in `TextureIssues` with a reason.
- **Custom blend edges** (`customBlendEdgeClass`): alpha 0 as in the reference; the edge pass with the
  edge textures is missing. Such cells do not occur in the content (0).

### 2.2 Lighting

Static vertex lighting after `BaseHeightMapRenderObjClass::doTheLight`: ambient of the first light plus, for each of the
`NumberGlobalLights` (engine default 3, `GlobalData.cpp`) lights, `clamp(dot(−LightPos, n), 0, 1) · Diffuse`, limited to
[0, 1]. Normals by central difference over two cells like `updateVB`. Values from `GameData.ini`
`TerrainLighting<TimeOfDay>{Ambient,Diffuse,LightPos}[2|3]` for `TimeOfDay = AFTERNOON`. Dynamic lights are missing.

### 2.3 Water

`WaterAppearance.FromDefinitions(graph, lighting)` reads `WaterSet <TimeOfDay>` and `WaterTransparency` from `Water.ini`.
`WaterSurfaceBuilder` (called by `Build`) produces one mesh per water polygon trigger:

- Standing water after `WaterRenderObjClass::renderWater`/`drawTrapezoidWater`: quad (0, k, k+1, k+2) for odd k,
  one bilinear patch each with at most 50×50 rectangles of about 8 cells each; height = z of the polygon points;
  UV = position / 150; no UV shift.
- Rivers after `drawRiverWater`: strip between the two banks from `RiverStart`, U across (0.5 / 0), V along the
  river; `UvScrollPerSecond = (0, −0,06)` (`m_riverVOrigin += 0.002` per 30 Hz frame).
- Colour: with a white `StandingWaterColor`, ambient + Σ(−LightPos.z·Diffuse) × `WaterSet DiffuseColor`, alpha from
  `DiffuseColor`; texture `StandingWaterTexture` (`TWWater01.tga`). Draw water two-sided (reference: `D3DCULL_NONE`).

Not reproduced: the slight UV wobble (±0.02) of the standing water, soft water edges via destination alpha
(`TransparentWaterDepth`, values are supplied), the vertex water surfaces of the four campaign maps from `GameData.ini`
(`VertexWater*`), sky reflection.

### 2.4 Height sampler

`TerrainHeightSampler(MapHeightField)` delivers `HeightAt(x, y)` and `NormalAt(x, y)` in `float`, with exactly the
triangle interpolation and edge clamping of `TerrainMap.HeightAt` (`WorldHeightMap::getHeightMapHeight`, two triangles per
cell along (ix,iy)-(ix+1,iy+1)); bilinear would be wrong by up to half a height step compared with the simulation.
Measured: largest deviation from the fixed-point simulation over all cell corners and centres of all 150 maps 0.0050
(tolerance 0.01, float rounding at coordinates up to 7000). The rendered surface deviates from it in flipped cells (blend/cliff
diagonal, 1,029,741 of 30,259,192 cells); picking deliberately follows the simulation surface.
`Boundaries`/`BoundaryMax(i)` deliver the map boundaries, `PlayableMax` the largest (= `TerrainMap.CellsX/Y`).

### 2.5 Memory (measured, 150 maps)

GPU estimate = vertices × 36 bytes (position 12, normal 12, colour 4, UV 8) + indices × 2 + atlas with full mip chain
(RGBA8 uncompressed) + water (24 bytes/vertex, 4 bytes/index).

| Quantity | Value |
| --- | --- |
| largest map | `Maps/MD_GLA05/MD_GLA05.map`, 709×709 cells, 529 chunks, 1,120,734 triangles |
| of which meshes | 87,417,252 bytes (83.4 MB) |
| of which atlas with mips | 24,466,708 bytes (23.3 MB), 2048×2240, 49 texture classes |
| total largest map | 112,115,512 bytes (106.9 MB) – below the target of 300 MB |
| atlas size | 2048 × (192 … 2240), tile 64×64 pixels, cell 32×32 pixels |
| sum of all maps | meshes 5,039.8 MB, atlases 1,820.0 MB, water 12.1 MB |

Mip strategy: full box mip chain like `D3DXFilterTexture(D3DX_FILTER_BOX)`; from about level 3 (the 4-pixel border becomes 0.5 pixels)
class boundaries mix as in the original. Compression (BC1/BC3) would reduce the atlas to a quarter/an eighth, but is
a matter for the client.

## 3. Camera

`RtsCamera(RtsCameraSettings settings, TerrainHeightSampler terrain, Vector2 viewportSize)` implements
`CnCNew.Presentation.IViewProjection` (`ViewportSize`, `ViewProjection`, `GetScreenRay`, `TryScreenToGround`,
`TryWorldToScreen`; file `Camera/IViewProjection.cs`, committed early and stable for package C3). What is reproduced is the
retail camera of the EA sources (`GeneralsMD/.../W3DView.cpp`, `View.cpp`, `LookAtXlat.cpp`); the community changes of
GeneralsGameCode (averaged terrain height, `SCROLL_MULTIPLIER`) are deliberately not adopted.

| Parameter | Value | Source |
| --- | --- | --- |
| `CameraPitch`, `CameraYaw` | 37.5°, 0° | `GameData.ini` |
| `CameraHeight` | 232 | `GameData.ini`; offset height H = ground height + 232 (`initHeightForMap`) |
| `MaxCameraHeight`, `MinCameraHeight` | 310, 120 | `GameData.ini`, height above terrain |
| `CameraAdjustSpeed` | 0.3 per 30 Hz frame | `GameData.ini` (time-independent as 1−0.7^(dt·30)) |
| `ScrollAmountCutoff`, `EnforceMaxCameraHeight` | 50, No | `GameData.ini` |
| `Horizontal/VerticalScrollSpeedFactor` | 1.6 / 2.0 | `GameData.ini` |
| `KeyboardScrollSpeedFactor` | 2.0 (initial value of `ScrollFactor`, the options menu overrides it) | `GameData.ini` |
| `KeyboardCameraRotateSpeed` | 0.1 rad per frame | `GameData.ini` |
| `MaxShakeIntensity`, `MaxShakeRange`, `Shake*Intensity` | 10, 150, 0.5/1/2.5/5/7.5/10 | `GameData.ini` |
| horizontal field of view | 50° | `View::View` (`m_FOV`) |
| Near/Far | 10 / ≥ 1200 (up to the farthest field corner) | `W3DView::setCameraTransform` |
| ground height | terrain at the start point, at most 120 | `W3DView::initHeightForMap` |
| terrain probe | maximum of the pivot and four diagonal points at distance 40 | `getHeightAroundPos`, `TERRAIN_SAMPLE_SIZE` |
| wheel zoom | 10 per notch | `View::zoomIn/zoomOut` |
| scrolling | `SCROLL_AMT` 100, `SCROLL_RESOLUTION` 250, screen edge 3 px | `LookAtXlat.cpp`, `W3DView::scrollBy` |
| mouse rotation | 0.01 rad per pixel | `LookAtXlat.cpp` |
| pitch limits | 0.1° … 89.9° | `View::setPitch` |

Model (`buildCameraTransform`): offset = (0, −H/tan(Pitch₀), H), rotated by (Pitch − Pitch₀) about −X and by `Angle` about Z,
times `Zoom`, times `1 − ground height/(H·Zoom)`; target = (pivot, ground height). `Zoom` follows `(terrain probe + HeightAboveGround)/H`;
during fast scrolling only when the camera gets below the minimum height (`Update(dt, isScrolling)`). The default zoom is the
maximum height (`setZoomToDefault`).

- Map boundaries (`calcCameraConstraints`): the pivot stays within the active map boundary (`ActiveBoundary`, default 0),
  reduced by the ground distance between the screen-centre ray and the 95 % height ray. If the margin is larger than half the map,
  the axis is kept centred (reference: undefined).
- Scrolling: `KeyboardScrollDelta`, `EdgeScrollDelta`, `RightMouseScrollDelta` deliver the reference scroll values (per
  30 Hz frame, scaled with `dt`); `ScrollBy` maps them into the world like `scrollBy` via the view plane at distance 1 and the
  camera rotation.
- Picking: `GetScreenRay` (origin on the near plane, normalised direction) and `TryScreenToGround` (quarter-cell
  steps through the height range of the map, then 24 bisections on `HeightAt`; outside the height field no
  hit). Bridges are missing (see limits). `InverseViewProjection` is available for rays of one's own.
- Culling: `Frustum.FromViewProjection` (Gribb/Hartmann for row vectors, depth 0..1), `Intersects(Aabb)`;
  `VisibleChunks(model)` delivers the chunk indices.
- `GroundFootprint()`: intersection of the four screen corners with the ground plane (`getScreenCornerWorldPointsAtZ`) for the minimap.
- Camera shake (`ViewShake` FX): `Shake(epicenter, intensity, directionRadians)` like `W3DView::shake` (linear falloff
  up to `MaxShakeRange`, spring with damping 0.75 and direction reversal per frame, above `MaxShakeIntensity` the
  reference resets to 3); the random angle comes from the caller. `CameraShakeIntensities.For("STRONG")` delivers the
  INI intensity. `AddShakeOffset` is an additive offset for effects of one's own.

## 4. Fog of war

`FogOfWarGrid(cellsX, cellsY, FogSettings)` or `FogOfWarGrid.ForVision(vision, settings)`:

- `Update(VisionMap, playerSlot)` takes over, per path cell (10 units, grid of the simulation), the state
  `Unexplored`/`Fogged`/`Visible` (bit 0 visible, bit 1 explored). The slot is the player index of the `VisionMap`.
- Brightness levels from `GameData.ini`: `ShroudAlpha` 0, `FogAlpha` 127, `ClearAlpha` 255 (`W3DShroud`: terrain colour ×
  level/255). `Advance(dt)` fades every cell with `FadeLevelsPerSecond` towards the target level (own decision; the original
  switches hard; the simulation updates every 3 ticks). `Snap()` jumps immediately.
- Texture: `CopyLevels` one byte per cell, row by row from cell (0, 0) in the south-west; mapping world → UV =
  (x/(CellsX·10), y/(CellsY·10)), filter bilinearly like the reference. `BuildSmoothedLevels(factor)` delivers the bilinearly
  pre-filtered variant.
- Queries: `StateAt`, `IsVisible`, `IsExplored`, `IsVisibleAt(world)`, `CellOf`, `CellCenter`. Foreign units are
  drawn only in visible cells; building ghosts are a matter for the object package. Cells outside count as unexplored.

## 5. Minimap

`MinimapImage.Build(model, sampler, terrainMap, radarWaterColor, size, boundary = 0)` after `W3DRadar::buildTerrainTexture`:

- Image section = active map boundary (`W3DTerrainLogic::getExtent`), the aspect ratio follows the map (longer side =
  `size`), pixel row 0 = northern edge. `WorldToPixel`/`PixelToWorld` for click camera and minimap commands.
- Land pixels: average colour of the base tile (`getTerrainColorAt`, 64×64 tile averaged to 1 pixel), shaded with
  `interpolateColorForHeight`. The reference passes (average, maximum, minimum) as (hiZ, midZ, loZ) here; with that
  land darkens from the highest point downwards to 60 % – reproduced. Water pixels: `RadarWaterColor`, darkened with
  depth; per pixel the mean over the 3×3 neighbours (water only over water neighbours). Cliffs appear only through the
  height shading as in the original.
- `Compose(fog, dots, footprint, dotSize)`: terrain × fog level, unit dots (`MinimapDot`, the caller filters
  visibility), camera footprint as a white closed line.

## 6. Audit

`dotnet run -c Release --project tools/CnCNew.PresentationTool -- terrain-audit <content-dir> [--map <pfad>]` builds the
render data of every map, prints deterministic numbers per map and in total (two runs byte-identical except for the
designated time line) and checks the height sampler against `TerrainMap.HeightAt`. Exit code ≠ 0 on build or
sampler errors and on unexpected unresolved textures.

Measured (150 maps, all built): 31,747 chunks, 30,259,192 cells, 135,501,748 vertices, 67,750,874 triangles
(60,518,384 Base), 3,556,938 blend cells, 59,307 3-way cells, 0 edge blend cells, 1,029,741 flipped cells,
8,253,119 cliff cells (pathfinder bit), 59,505 cells with stored cliff UV, 193,853 stretched cells,
7,251 cells without texture, 0 invalid tile references, 320 water surfaces (121 rivers, 508,370 triangles), 0 mirrored
terrain TGAs, 0 TGAs with an ID field; run time about 30 s (Release).

Unresolved textures (5, all with a reason, carried as known data errors of the retail content): the maps name the
terrain types `CliffLargeType5` (2 maps), `CliffMediumType6`, `CliffMediumType7` (1 each), `DirtMediumType6` (5) and
`RocksType5b` (2), which are not defined in any INI file (`Terrain.ini` only knows variants such as `CliffMediumType6b`).
The reference does not find them either (`findTerrain` returns null) and draws the cells with UV 0; the same here.
Every other unresolved name makes the audit fail.

## 7. Limits

- Map lighting: the `GlobalLighting` chunk of the map is not decoded by the map decoder (`CnCNew.Formats`); all
  maps use the `GameData` lighting. Night maps therefore look too bright. Request to Formats: decode the chunk.
- `map.ini` overlays (e.g. map-specific `WaterTransparency`) are not taken into account; the
  retail base graph applies.
- Roads (`MapObject.IsRoadPoint`, `Roads.ini`), bridges (`IsBridgePoint`, bridge models, bridge picking and
  radar colour), burn marks/scorches, track marks (`W3DTerrainTracks`), terrain decals, macro texture/cloud shadows,
  sky, background terrain (`W3DTerrainBackground`) and edge blend textures are missing. Roads and bridges need
  object meshes and curve geometry from `W3DRoadBuffer` and belong in a package of their own.
- Shader differences: no multi-texture terrain shader, no dynamic lights, no underwater depth darkening
  (`m_useDepthFade`), no soft water edges; the blend passes require alpha blending over the base pass.
- The rendered terrain diagonal deviates from the simulation surface in flipped cells (picking follows the simulation).
- Camera: no script camera (waypoint flight, rotating, camera lock on objects), no real-zoom mode, no
  `movePivotToGround` tracking of the community version; the fog fades softly instead of hard.
- Minimap: averaged terrain height over all land pixels instead of every second radar pixel; no bridge colour; event
  and hero symbols are a matter for the UI.
- Memory: four vertices per cell in up to three passes (simple material instead of shader); a shared vertex buffer with
  a shader of its own would roughly halve the meshes.

## 8. Integration (Stride client)

1. Load the graph → `TerrainRenderOptions.FromDefinitions`, `TerrainTextureCatalog.FromDefinitions`,
   `RtsCameraSettings.FromDefinitions`, `FogSettings.FromDefinitions(graph, fade)`, `CameraShakeIntensities.FromDefinitions`.
2. Decode the map (`SageMapDecoder`), `TerrainRenderModel.Build`, `TerrainHeightSampler`, `TerrainMap.Build`.
3. Upload the atlas (`BuildMipChain`) as one texture; one mesh per chunk and pass; water with its own texture and
   UV scroll.
4. `RtsCamera` per frame: inputs → `ScrollBy`/`RotateBy`/`ZoomByWheel`, then `Update(dt, isScrolling)`, hand the matrices to
   Stride, `VisibleChunks` for culling.
5. Every `VisionIntervalTicks`: `FogOfWarGrid.Update(world.Vision, slot)`; per frame `Advance(dt)` and `CopyLevels` into
   an R8 texture.
6. Build the minimap once, `Compose` per frame or less often.

Request to the simulation (read-only): a public mapping `PlayerId → Vision-Slot` (`SimulationWorld.SlotOf`
is private); until then the client passes the slot itself: index of the player in ascending `PlayerId` order including the system player 0 on slot 0.
