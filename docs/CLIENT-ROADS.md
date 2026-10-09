# Roads in the Stride client

Responsible: `src/CnCNew.Presentation/Terrain/Road*.cs` (renderer-neutral model),
`src/CnCNew.Client.Stride/Layers/Terrain/RoadMeshBuilder.cs`, `TerrainMaterials.CreateRoad` and
`TerrainLayer.CreateRoads` (drawing). The roads of a map are map objects with the
`FLAG_ROAD_POINT1`/`FLAG_ROAD_POINT2` bits set (`MapObject.h`); their object name is the name of a `Road` block
from `Roads.ini`. The reference game builds textured strips from them in `W3DRoadBuffer` that lie on the
height field. This document describes what of that was rebuilt, how it was measured and what
is only approximated. Foreign code was read and rewritten, not copied; the source references
are given at each function.

Until Phase 15, road points were only counted by `StaticScenery` (`SceneryReport.RoadOrBridgePoints`)
and discarded. That has stayed: road points are not scenery objects, they go through
`RoadModel`.

## Data source

- **Map**: `MapObject.Flags` (`CnCNew.Data.World`). Newly named were the bits that so far existed only
  numerically, `FlagRoadCornerAngled` (0x08, `FLAG_ROAD_CORNER_ANGLED`), `FlagRoadCornerTight` (0x40,
  `FLAG_ROAD_CORNER_TIGHT`) and `FlagRoadJoin` (0x80, `FLAG_ROAD_JOIN`); `FlagRoadPoint1/2` (0x02/0x04)
  already existed. A road segment is a pair of objects *immediately one after the other* in the `ObjectsList`
  (first `POINT1`, then `POINT2`); the order of the file is preserved (`WorldHeightMap.cpp`
  appends objects).
- **`Roads.ini`**: `Road <Name>` with `Texture`, `RoadWidth` and `RoadWidthInTexture`
  (`TerrainRoads.cpp`, `m_terrainRoadFieldParseTable`). The data layer still keeps `Road` and `Bridge`
  as *compatibility blocks* (`baselines/compatibility.json`: `Road` 63, `Bridge` 27, target "phase-specific
  typed definition compiler") – like `Terrain`/`WaterSet`. There was therefore **no change to the
  `CnCNew.Data` schema or baseline**: `RoadCatalog.FromDefinitions` reads the three keys strictly through
  `WorldDefinitionReader` (a missing key or a wrong value type throws, nothing falls back to default
  values) and `GameData.MaxRoadSegments`. The order of the templates is that of the declaration
  (`IniSourceLocation`), not the canonical name order of the graph: it determines the ids
  (`TerrainRoadCollection::m_idCounter`, from 1) and thus which road type is drawn first when the
  stacking is equal.
- **Bridges** (`FLAG_BRIDGE_POINT1/2`, `Bridge` blocks) are models with towers and damage states
  and are not part of this.

Names are compared as in `TerrainRoadCollection::findRoad` (`AsciiString::operator==`: case-sensitive).
A road point with an unknown template name is **not** drawn but counted
(`RoadStats.UnknownTemplates`); the reference game silently draws it with the first road type at
width 8 (`addMapObjects`, `found == false`). In the real content this concerns three segments with an *empty* name
in `Maps/USA02/USA02.map` – a known data error, explicitly allowed in `road-audit` and in the real-data test
(any other unknown name makes both fail).

## Structure of the model (`CnCNew.Presentation`)

`RoadModel.Build(objects, heights, catalog, lighting)` is pure and deterministic (only
`float` arithmetic on `Vector2`/`Vector3`, no time/randomness/order of hash tables).

1. **`RoadLayout`** (topology, port of `W3DRoadBuffer::loadRoads`):
   - `addMapObjects`/`addMapObject`: point pairs become segments; if both points coincide, `x` is
     shifted by 0.25; identical segments (both directions) are discarded; segments that lie
     end to end (`count == 1`) are chained in and re-sorted (`checkLinkBefore/After`,
     `moveRoadSegTo`); then a second pass with the final counters. Edge/top/bottom edge
     (`top`/`bottom`) lie `width × RoadWidthInTexture / 2` beside the centre line.
   - `updateCountsAndFlags`: for each end point the number of *other* segments of the same type that
     end there (1 = chain, 2 = T, 3 = crossing).
   - `insertTeeIntersections`: 2 → `insertTee` (first `insertY`; "almost straight" from −cos 30°, otherwise Y;
     an arm with |cos| > 0.5 against the through direction gives the angled H piece, otherwise the
     straight T), 3 → `insert4Way`. The segment ends involved are shifted away from the junction
     (`offset3Way`, `offsetH`, `offsetY`, `offset4Way`) and the junction piece is appended as an additional segment.
   - `insertCurveSegments`/`insertCurveSegmentAt`: bends in a chain are either laid as a curve of
     30° sections on a circle radius `1.5 × width` (flag `…CORNER_TIGHT`: 0.5), or – for an angle
     < 27° (`count < 0.9`), `FLAG_ROAD_CORNER_ANGLED` or if the curve would be "clipped out of
     existence" (`DOT_LIMIT`) – mitred with `miter` (intersection of the edge lines).
   - `insertCrossTypeJoins`: ends with `FLAG_ROAD_JOIN` and without a connection get an alpha blend
     (`ALPHA_JOIN`); if the end lies on a straight segment of *another* type, it aligns itself with that
     and `adjustStacking` draws the joining type above it.
   - `MaxRoadSegments` (4000) limits the number of segments as in the original (map objects *and* inserted
     pieces); whatever goes beyond is counted (`DroppedBySegmentBudget`) instead of disappearing without a word.
2. **`RoadGeometry`** (triangles, port of `preloadRoadSegment`, `loadCurve`, `loadTee`, `loadY`, `loadH`,
   `loadAlphaJoin`, `loadFloatSection`, `loadFloat4PtSection`): each piece is a quad, divided bilinearly
   into columns (one per cell, `uCount = length/10 + 1`, at least 2). Each column is set to the
   **largest corner height** of the cells below it (`getMaxCellHeight`, `TerrainHeightSampler.MaxCellHeight`),
   *flat across the road*, and lies `HeightScale / 8` (0.078) above it (`FLOAT_AMOUNT`, against
   Z-fighting). Columns that lie on the straight line between their neighbours (deviation < 1.1 height steps)
   are dropped (`MAX_ERROR`); in doing so the swapped weighting of the reference (weight of the neighbours by
   distance to the *current* column instead of to the other one) is adopted deliberately, because it determines which
   columns are dropped. A column has two vertices (lower/upper edge); the UVs run from the
   position relative to the reference point: `u = uOffset + U/(4·width)`, `v = vOffset − V/(4·width)`
   (one texture width covers four road widths, the texture repeats).
3. **Texture layout**: every road texture is a 512×512 strip atlas with fixed rectangles for
   the pieces (offsets in 512ths, from `W3DRoadBuffer`; strip, curve, Y and four-way looked up in the
   `TRTwoLane.dds` we decoded ourselves):

   | Piece | `uOffset` | `vOffset` | Remark |
   |---|---|---|---|
   | Segment | 0 | 85 | strip at the top, width `RoadWidthInTexture` |
   | Curve | 4 | 255 (tight: 425) | wedge shape, quad fitted to the wedge |
   | T / four-way | 425 | 255 / 425 | width `1.03 ×` (`TEE_WIDTH_ADJUSTMENT`) |
   | Y | 255 | 226 | pentagon, quad also covers transparent corners |
   | H (and mirrored) | 202 | 364 | angled branch |
   | Alpha join | 106 | 425 | 48 pixels + 8 pixels of overlap |

4. **Lighting**: like `RoadSegment::updateSegLighting`, the static terrain lighting of the
   *nearest height vertex* (`x/10 + 0.5` truncated, plus border) – here from
   `TerrainLighting.Shade(TerrainHeightSampler.VertexNormal(…))`, the same formula as `TerrainRenderModel`
   (a test compares the two). Alpha of the vertex color is 255; the texture determines the transparency.
5. **Output**: `RoadMesh` per template (`Template`, `Texture`, `Stacking`, positions, UVs, colors, indices,
   bounds); triangles are counter-clockwise seen from above (the reference leaves that to the
   segment direction), order of the pieces as in `loadRoadsInVertexAndIndexBuffers`: segment, curve, T,
   four-way, Y, H, H mirrored, alpha join.

## Drawing (`CnCNew.Client.Stride`)

- `RoadMeshBuilder.Build` turns the meshes into `WaterVertex` buffers (position, color, UV, fog UV) and
  decodes the lighting in the linear color space as for the water (`PackedColor.SrgbDecodeTable`).
- `TerrainMaterials.CreateRoad` corresponds to `RoadShader2Stage` (`ST_ROAD_BASE`): texture × vertex color ×
  fog, `SRCALPHA`/`INVSRCALPHA`, depth test `LessEqual`, **no depth writing**, texture repeated
  (`Wrap`), double-sided. The fog texture is the same as for terrain and water; the fog multiplies only
  RGB, the alpha stays (shadowed roads are not more transparent). Important: the emissive feature
  discards the alpha of the texture if `UseAlpha` is missing – without it the transparent corners of the
  Y and H pieces appeared as grey rectangles (first seen that way and fixed).
- `TerrainLayer.CreateRoads` builds one mesh per road template and creates the entities in stacking order
  (`Stacking`, then declaration order, like `drawRoads`). Stride sorts transparent meshes by
  the distance of their bounding box; so that the order does not depend on the camera, all
  road meshes get **the same** bounding box (the union of all): equal keys keep the
  creation order. A texture that cannot be resolved makes `Attach` fail (no
  magenta replacement for roads).
- The road occludes no object (depth test) and is occluded by objects; fog/shroud darken it like the
  terrain (seen at the shroud edge of `Desert Fury`, see below).
- Debug: `CNCNEW_ROADS=0` switches the roads off (comparison with/without); `CNCNEW_TERRAIN_REVEAL=1` shows
  the whole map (roads mostly lie outside the starting view).

## Measured values (`PresentationTool road-audit game/content`, as of 4 October 2026; re-measure)

- 150 maps built, 0 errors, **130** maps with roads.
- **40,960** point pairs → **40,801** segments (156 duplicates discarded, 7 unpaired `POINT1` objects, 3 with
  an empty name discarded), in total **69,047** pieces and **206,956** triangles.
- Pieces: segment 40,801, curve piece 20,446 (from 8,229 curved corners; 18,231 mitred corners, of which 390
  aborted curve attempts), T 3,518, four-way 506, Y 546, angled H 276 + 233 mirrored, alpha join 2,721
  (1,420 against another road type).
- 59 of 63 `Road` templates are used; `StreetHoleCover` (`TRStrtHoleCvr.tga`) has no texture in the
  content, but is used by no map – all textures used resolve through `W3dTexturePathResolver`
  (`.tga` → `Art/Textures/*.dds`).
- Largest map: `MD_GLA05_END` with 1,811 pairs, 2,879 pieces, 8,628 triangles – far below
  `MaxRoadSegments` 4000 (no map hits it).
- In the client (`Attach`, Release, one run, not deterministic): `Alpine Assault` 313 segments, 1,718 triangles,
  6 meshes in 73 ms (model building, texture loading, buffers); `Death Valley` 363 segments, 2,976 triangles, 12 meshes in 73 ms.
  The time is in the `terrain:` line of the client.
- Baseline unchanged: `IniTool compat-check` reports 751 entries (the `Road` blocks remain compatibility values).
- Check: no vertex non-finite or more than 300 units outside the point box of its map;
  two runs yield identical meshes.

## Visual check

Captures in the scratchpad of the session (not in the repo; `scripts/capture-world.ps1` now contains the
scenes `roads-junctions`, `roads-crossing`, `roads-overview`, all with `CNCNEW_TERRAIN_REVEAL=1`). What a
human should tick off against the pictures:

- The road is a continuous band, follows the terrain, has the expected width and the centre line
  continues cleanly in the curves and junctions (seen: gravel road with curves and T piece, asphalt with
  a four-way crossing rotated by 45°, Y crossing with zebra stripes, angled junctions, railway tracks, parking lot lines,
  sidewalks beside asphalt and paving, airfield with markings on `Red Rock`).
- Junctions are closed, no gaps at the edges. The edges of the Y/H piece are faded out softly
  (no grey rectangle – that was the alpha error found).
- Roads disappear in the shroud with the terrain (`Desert Fury`, starting view).
- **Open for the eye of a human**: small dark squares at some outer sidewalk corners (`Bear Town
  Beatdown`, sidewalk + paving; probably overlap of the mitred corner, presumably similar in the original), and
  the overlap of many marking types on `GC_LaserGeneral` (seen here only via `Red Rock`/`Bear Town`).
  Smoke tests and hashes prove reproducibility, not correctness.

## Deviations and limits

- **Unknown templates** are counted, not (as in the original) drawn with the first road type.
- **Uninitialised fields** of the original (`isJoin`, `count` on generated pieces) are assumed to be
  `false`/0; for pieces that the algorithm inserts itself, that is the only sensible reading.
- **No view-area rebuild**: the original loads only the segments in view into dynamic buffers
  (`MaxRoadVertex` 3000 / `MaxRoadIndex` 5000 per type) and leaves pieces out on overflow. Here everything is in
  static buffers, there is nothing to overflow (208 k triangles for all maps together).
- **No cloud/noise passes** (`ST_ROAD_BASE_NOISE*`) – the same absence as for the terrain.
- **No recalculation on terrain change**: roads are built once on creation. On changed terrain
  (construction of structures, `updateCenter`) the original calls `updateLighting` and rebuilds the buffers; the client
  does not deform the terrain.
- **Dynamic lights** (`loadLit4PtSection`) are disabled in the original (`#if 0`) and are missing here as well.
- **Order of overlapping road types** depends on the Stride sort key (same bounding box); it is
  repeatable, but not guaranteed through an API. If it turns out differently after a Stride change, the
  consequence would be a wrong overlap of markings, not an error.
- **Floating point**: sine/cosine/square root come from the .NET runtime; tiny deviations from the
  `WWMath` approximations of the original are possible and meaningless for the presentation.
- **Mitred corners** (`miter`) use infinite line intersections like `LineSegClass::Find_Intersection`;
  almost parallel edges can produce long spikes. In the 150 maps that does not occur (audit limit 300).
- **Bridges** are not part of this piece, and the simulation does not see roads (no lanes, no terrain change).

## Tests and tools

- `tests/CnCNew.Presentation.Tests/RoadTests.cs` (synthetic, without original data): catalog, straight segment
  including UV/lighting/height, columns on the hill, 90° curve, mitre, T/H/Y/crossing, alpha join and
  stacking, counting (unknown, duplicate, unpaired, budget), determinism and winding, sampler
  (`MaxCellHeight`, vertex normals against the terrain model).
- `RoadRealDataTests.cs` (with `--content-dir`): 63 templates, all 150 maps build, every texture resolves, golden
  numbers (40,960 / 40,801 / 206,956), determinism on `Alpine Assault`.
- `dotnet run -c Release --project tools/CnCNew.PresentationTool -- road-audit game/content [--map "Maps/…"]`
  (with `--map` additionally junction positions and mesh bounds for captures).
