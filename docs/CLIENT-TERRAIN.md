# Terrain layer of the Stride client

Responsible: `src/CnCNew.Client.Stride/Layers/TerrainLayer.cs` and
`src/CnCNew.Client.Stride/Layers/Terrain/**`. The layer shows terrain, water, fog of war/shroud
and a minimap texture of a `LiveMatch` in Stride. It reads exclusively the already
prepared `TerrainRenderModel` (`CnCNew.Presentation.Terrain`) and the `FogOfWarGrid`
(read-only); it does not include any simulation logic of its own.

## Data flow

`TerrainRenderModel` supplies per map section:

- **Chunks** (`TerrainChunk`): three meshes per chunk (Base, Blend, ExtraBlend/3-way), four
  vertices per cell each, `ushort` indices. Colour = static lighting (RGB) and blend weight (A).
- **Water** (`WaterSurface[]`): individual water surfaces with texture, colour, optional scroll
  (rivers) and `IsRiver`.
- **Atlas**: one texture with all tile types including the mip chain (`BuildMipChain()`).

`TerrainLayer.Attach` builds GPU resources from this once; `Update` only updates the fog texture,
water scroll, minimap and chunk visibility.

## One draw pass instead of three

The reference game draws the base, blend and 3-way pass separately on top of each other with alpha blending.
That is unnecessary for terrain and would make the sorting against the transparent water harder.
`TerrainMeshMerger.Merge` instead folds the three passes per chunk into one vertex array:

- The base pass defines vertices and indices (four vertices/cell, as in the model).
- Blend and 3-way cells are assigned to the matching base cell via the position of their first vertex
  (`Attach`) and supply a second and third UV set (`Uv1`/`Uv2`) plus their
  blend weight in `Color0.A` and `Color1.A` respectively. Cells without an overlay get weight 0.
- The overlay can be triangulated along the other diagonal than the base pass; the
  merged mesh always keeps the base split. `DiagonalMismatches` counts that (on the tested
  150 maps typically single-digit, see the measurements below) – purely cosmetic, because the
  tile colour per triangle deviates hardly visibly through interpolation.
- `TerrainMeshMerger.Validate` checks after the merge the index bounds and that only cells with
  an assigned overlay carry a blend weight; every inconsistency throws `InvalidDataException`.
  That runs synchronously in `Attach` – verifiable without a GPU, because `TerrainMeshMerger` and
  `TerrainVertex` do not reference Stride.

The material graph (`TerrainMaterials.CreateTerrain`) computes per pixel
`lerp(lerp(atlas(uv0), atlas(uv1), w1), atlas(uv2), w2) * lighting * fog` and writes the
result into the emissive channel. One pass, opaque, `CullMode.Back`, depth test and
depth write active.

## Winding and coordinate system

Simulation and Stride share the same right-handed world (X east, Y north, Z up); camera view/
projection are taken over unchanged via `ToStride()`, there is no mirroring. The
triangles of the model are counter-clockwise (CCW) seen from above, but Direct3D/Stride by default
treat clockwise (CW) as the front face. `Attach` therefore swaps, after the
validation, `Indices[t+1]` and `Indices[t+2]` per triangle, `CullMode.Back` stays active. Without
this swap the map was culled away completely (only sky blue visible) – the first
visibility test of this phase.

## Water

`WaterMeshBuilder.Build` groups `WaterSurface` surfaces by texture, `IsRiver` and scroll
speed into `WaterBatch`es and builds one contiguous vertex array per group
(testable without Stride). Animated batches (`Scroll != 0`) get a `Dynamic` vertex buffer;
`ApplyScroll(seconds)` rewrites `Uv = BaseUv + frac(Scroll * t)` per frame.

The water material graph (`TerrainMaterials.CreateWater`) multiplies texture, vertex colour and
fog factor, but replaces the alpha channel again with the unfogged product (`SubstituteAlpha`):
fogged water thus stays exactly as opaque as unfogged water, only darker. Water is drawn in the
transparent Stride stage after the opaque terrain (`CullMode.None`, no depth write,
blend `SourceAlpha/InverseSourceAlpha`, with `WaterAppearance.AdditiveBlending` instead
`SourceAlpha/One`). Because terrain goes first, opaque and with depth write, it hides water where
the terrain height lies above the water plane – just as in the original only real depressions/coasts appear
flooded although the water surface in the model often spans the whole map (see the measurements).

**Checked** (visually, see the screenshots section): lake shore (Bitter Winter, irregular coastline
against snow), river with scroll animation (FloodedPlains, visible wave offset between frame 1
and frame 600), alpha blending at the shoreline, depth behaviour in an oblique view. **Not
checked**: `WaterAppearance.AdditiveBlending == true` (none of the eleven probed maps uses it) and
`WaterAppearance.TransparentWaterDepth` (is currently not evaluated – water has constant
opacity independent of the water depth below the surface; that is a known gap).

## Fog of War / Shroud

`FogTexture` is an `R8_UNorm` texture with one texel per cell of the playable area
(`FogOfWarGrid.CellsX/CellsY`), created `Dynamic`. `UpdateFog` reads `match.Fog.CopyLevels(...)`
at most every `FogInterval` (1/20 s) and uploads only if the content has changed
(`SequenceEqual` comparison against the last upload) – on a map standing still the
upload is practically dropped altogether. The values 0/127/255 (Shroud/Fog/Clear) end up unchanged in the texel; the
terrain and water materials sample them via a common fog UV (`world.xy * FogUvScale`,
`Clamp`, `Linear` filtering provides soft edges at shroud/fog borders) and multiply the
result onto the emissive colour. In the (according to measurement) linear colour space pipeline of the device
the factor is squared (`FogFactor`, `ComputeBinaryColor` Multiply with itself), so that a
fog texel (127/255 ≈ 0.5) gives the same brightness drop on the screen as in the
reference renderer, which multiplies in gamma space.

`FogTexture` is public (`TerrainLayer.FogTexture`, `FogUvScale`) so that the object layer
can darken units and buildings with the same fog without maintaining a second fog texture. As of Phase 15
the object layer does **not** use it: static map scenery is instead hidden in the presentation
(`FrameProjector`) until its ground is explored, and drawn undarkened afterwards.

**Debug environment variable** `CNCNEW_TERRAIN_REVEAL=1`: overwrites every sample with 255 (fully
visible) without touching the simulation – intended only for screenshot verification.

**Known limitation**: the fog texture has no initial data at the first attach (`Dynamic`
without `DataBox`); the first sample is always uploaded, independent of the comparison. The very
first rendered frame – before `Update` ran for the first time – therefore still shows the
GPU zero initialisation, i.e. complete shroud (black), even if the start position
already had vision. From the second frame (at the latest after `FogInterval`) the state is correct.
For a game that starts with the first simulated tick that is a frame nobody sees;
documented because the brief explicitly compared frame 1 against frame 1800 and the difference
would otherwise look like a bug.

## Minimap

`TerrainLayer.MinimapTexture` (RGBA, row 0 = north) is produced from `match.Minimap.Compose(fog, dots,
footprint, dotSize)` (`CnCNew.Presentation.Minimap.MinimapImage`, not part of this
layer). Recomposed automatically every 0.25 s (`MinimapInterval`) if fog or the
camera footprint have changed. `UpdateMinimap(commandList, dots, cameraFootprint = null,
dotSize = 2)` is the public entry point for the HUD/UI layer: `dots` are world positions
with colour (filter out enemies hidden in the fog before the call – the layer knows no
player affiliation), `cameraFootprint` overrides, for testing, the rectangle derived from `match.Camera
.GroundFootprint()`. Without a call the minimap shows only terrain, fog and the
camera footprint.

**Debug environment variable** `CNCNEW_TERRAIN_DUMP=<directory>`: writes `minimap.png`, `terrain.png` and `fog.png` at every minimap
update (own minimal PNG writer,
`PngDump`, without a third-party image library) – served in this phase as the visual check without
screenshot tooling.

## Lighting

Terrain and water colour come exclusively from the vertex colours of the model (already provided
with static lighting by the import) and texture-dependent modulation; the material graph
uses only the emissive channel, never diffuse+light. The sun/lighting of the Stride host therefore has
no influence on terrain – as demanded by the brief.

## Colour space

The device runs at runtime with `ColorSpace.Linear` (measured, not assumed). The atlas is
therefore uploaded as `R8G8B8A8_UNorm_SRgb` (the GPU decodes on sampling), while
vertex light colours and water colours from the model are in gamma space and are therefore decoded once on the
CPU by lookup table (`PackedColor.SrgbDecodeTable`) before they flow into the packed
vertex colours (`TerrainMeshMerger.Merge`, `WaterMeshBuilder.Build`). The fog factor is,
as described above, squared. Without these corrections everything would be visibly too dark or too bright
compared with the reference renderer, which multiplies in gamma space.

## Visibility culling

`Update` queries `match.Camera.VisibleChunks(model)` per frame and switches only the
`ModelComponent`s whose visibility has changed (`Enabled` flag), instead of re-evaluating the
complete chunk list every time. That keeps the non-visible part of the map out of the
Stride render pipeline without frustum mathematics of its own in the layer.

## Public API for other layers

```csharp
Texture FogTexture          // R8_UNorm, one texel per fog cell, valid after Attach()
Vector2 FogUvScale          // world.xy * FogUvScale gives the fog UV
Texture MinimapTexture      // RGBA, row 0 = north, valid after Attach()
long GpuBytes               // GPU memory held by this layer
double AttachMilliseconds   // wall time of the last Attach()

void UpdateMinimap(CommandList commandList, IReadOnlyList<MinimapDot> dots,
                    IReadOnlyList<Vector2>? cameraFootprint = null, int dotSize = 2);
```

`FogTexture`/`FogUvScale` are intended for the coming object layer (darkening units/buildings in the
same fog); `UpdateMinimap` for the HUD/UI layer.

## Measurements (this phase, `--content-dir game/content`, Release)

Eleven maps probed (attach log), six of them checked visually with screenshots (desert, snow,
water/river, cliffs, see below):

| Map | Cells (playable) | Chunks | Blend cells | 3-way (diagonal mismatch) | Water batches | Attach | GPU |
|---|---|---|---|---|---|---|---|
| Alpine Assault | 239×259 | 156 | 26,630 | 370 (4) | 0 | 131–168 ms | 39.4 MiB |
| Bombardment Beach | 275×275 | 169 | 20,831 | 406 (0) | 1 | 167 ms | 54.4 MiB |
| FloodedPlains | 300×300 | 144 | 23,571 | 1,133 (4) | 1 (river, animated) | 182–204 ms | 33.4 MiB |
| Bitter Winter | 240×320 | 180 | 25,920 | 328 (1) | 1 (lake) | 161–211 ms | 46.8 MiB |
| Dark Mountain | 376×370 | 289 | 18,772 | 539 (6) | 1 | 274 ms | 63.1 MiB |
| Fortress Avalanche | 418×413 | 289 | 25,629 | 185 (0) | 1 | 204–272 ms | 76.6 MiB |

All values clearly below the budget of ~2 s attach time for a map of the 150 class.
Per-frame CPU (`CNCNEW_TERRAIN_PROFILE=1`, own `Stopwatch` around `Update`, Fortress Avalanche,
largest probed map, 900 frames autoplay): **0.266 ms/frame** on average, below the
1 ms budget. Determinism: two independent runs of the same camera/map combination yield
byte-identical screenshot SHA-256 (`CFF6C66C4708683A…` twice, Fortress Avalanche).

`dotnet build CnCNew.slnx`: 21 warnings in this measurement (solution build), of which the ten
known `NU1901`/`NU1903` from the Stride 4.3 build tools; the remaining eleven are
`AssetCompiler`/`YamlAssemblyRegistry` notes of the Stride content build about other assemblies
(`CnCNew.W3D`, `CnCNew.Ui`, `CnCNew.Simulation`, …) and appear only in the solution build, not
in the project build of `src/CnCNew.Client.Stride` alone (there still exactly 10 warnings). They
arise independently of the changes of this layer (none of the changed files touches the
content build) and are presumably due to simultaneous work of other layer packages in their own
worktrees; to be clarified when merging all packages, not in this phase.

## Screenshots (visual check)

Taken with `--smoke-test --capture` at different camera angles (pitch 15°–89°, height
400–20,000, several zoom levels) and, where needed for the visual check, `CNCNEW_TERRAIN_REVEAL=1`:

- **Cliffs/rock**: Alpine Assault (rock face with snow cover, soft blend to the meadow),
  Fortress Avalanche (two snow-covered cliffs in an oblique view with sky, no visible
  chunk seams despite a field of view over several of the 289 chunks).
- **Desert**: Bombardment Beach (cracked sand texture, soft transition to a lighter sand patch).
- **Snow**: Bitter Winter (two snow variants in the blend, transition without an edge).
- **Water**: FloodedPlains (river, wave texture, scroll animation visibly shifted between frame 1 and 600,
  clean shoreline in an oblique view without Z-fighting), Bitter Winter (lake with
  irregular coastline against snow).
- **Fog/Shroud**: Alpine Assault frame 1 without `REVEAL` completely black (see the limitation
  above), frame 1800 with `--autoplay` shows a revealed area around the start position with a soft
  edge to the shroud border.
- **Minimap**: `CNCNEW_TERRAIN_DUMP` yields `terrain.png` (top view, north up) and
  `minimap.png` (with a white camera footprint rectangle) for Alpine Assault, congruent with the
  top-view capture of the same map.

Screenshots were in the scratchpad of the session (`…/scratchpad/s1/`), not in the repository.

## Known limits

- `WaterAppearance.TransparentWaterDepth` is not evaluated; water opacity is constant.
- `WaterAppearance.AdditiveBlending == true` is provided for in the code, but observable on none of the
  probed maps and therefore visually unchecked.
- Diagonal mismatches between base and overlay pass (see the table) are counted but not
  fixed; the merged mesh keeps the base triangulation, the overlay can thereby interpolate slightly distorted at the
  diagonal of an affected cell. Visually conspicuous on none of the checked maps.
- The first rendered frame after `Attach` shows complete shroud before the first
  `Update` tick uploads the fog sample (see the fog section).
- Object lighting (units/buildings) is not part of this layer (since Phase 15: `Rendering/SceneLighting`
  with the object values from `GameData`, not from the `GlobalLighting` chunk of the map, which is skipped);
  `FogTexture`/`FogUvScale` are ready, but nobody consumes them so far – objects in the fog are
  not darkened.
- Roads (map road objects) are drawn by the layer as alpha-blended strips on the terrain (`docs/CLIENT-ROADS.md`); bridges (`FLAG_BRIDGE_POINT*`) are not roads and are not drawn here.
