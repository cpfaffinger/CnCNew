# Stride client: shadows, clouds, sky, weather

What the world layers draw besides terrain, objects and effects. Each part follows one class of the original, which is named; everything computed without Stride lies in `CnCNew.Presentation`
(`Shadows/`, `Terrain/TerrainClouds.cs`, `Terrain/MapEnvironment.cs`, `Fx/SnowField.cs`) with its tests in `tests/CnCNew.Presentation.Tests/ShadowTests.cs`. **No human has seen any of this**;
the pictures below were looked at by the agent that built them. Debug switches: `CNCNEW_SHADOWS=0`, `CNCNEW_CLOUDS=0`, `CNCNEW_WEATHER=0`.

## Order of a frame

`RTS3DScene::Flush` draws terrain, decals, opaque objects, trees, the stencil shadows, then water, translucent meshes and particles. In Stride roads, decals, shadows, water and particles all lie in
the transparent stage, so `StackedBackToFrontSortMode` gives material passes a **layer** in the top byte of the sort key: roads, ground decals, shadow volumes, the shadow's fill, and then everything else
back to front as before. `SkyFirstSortMode` puts the sky box first in the opaque stage.

## Shadows of objects

`Shadow`, `ShadowTexture`, `ShadowSizeX/Y`, `ShadowOffsetX/Y` of a template (`ShadowSpec`). In the data: 1,249 `SHADOW_VOLUME`, 179 `SHADOW_DECAL`, 28 none.

**Decals** (`W3DProjectedShadowManager::queueDecal`, `ShadowDecal`): a rectangle turned with its object, draped on the height field, multiplied into what is drawn (`_PresetMultiplicativeShader`). The name
gets `.tga`, no name is `shadow.tga`; a size of 0 is the size of the model's box. Offset X goes forward, offset Y to the right (the inverse of the height is negative in `addShadow`).
A tree with `DoShadow` has `shadow.tga` as wide as the mean of its box (`W3DTreeBuffer::drawTrees`). The object layer fills `RenderServices.ShadowDecals`, `FxBuilder.EmitShadowDecals` builds them.

**Volumes** (`W3DVolumetricShadow`, `ShadowCasterMesh`, `ShadowVolumeWriter`, `ShadowVolumeRenderer`): the original's stencil shadows.

- Geometry per rigid mesh, built when the model loads: duplicate vertices merged, an edge between two polygons that run along it in opposite directions. A mesh that blends or tests alpha casts nothing unless
  its `CAST_SHADOW` flag is set; a skinned mesh never casts (`initFromHLOD`).
- A silhouette is every edge between a polygon that faces the light and one that does not, and the open edges of one that does (`buildSilhouette`). It is kept per part and found again when the light turns
  more than about 1.4 degrees against the part. A thing that stands still keeps its corners and only copies them.
- The light is `ShadowSun.Direction`, the first light of the objects: the original puts the sun 10,000 units off, so a direction is enough. `ShadowSizeX` of a volume is **not a size**: it is the lowest
  elevation of the sun in degrees (`W3DVolumetricShadowManager::addShadow`), which keeps the shadows of tall things short.
- The sides go down along the light to a floor: a mobile thing 0.1 under its base, one in the air (2 units over the ground) to the lowest point of the map, an immobile one where
  `updateOptimalExtrusionPadding` says. That walk runs once per object: terrain up to 5 units lower takes the floor with it, a drop beyond that makes the light steeper, because terrain casts no shadow and a shadow
  below a cliff would hang in the air (`ShadowSun.OptimalExtrusion`).
- Stencil (`ShadowStencilPipelineProcessor`): sides without colour or depth, front faces that pass the depth test count up, back faces down, with wrapping counters in one draw. Then a rectangle over the screen,
  `DestinationColor / Zero`, where the count is not zero. The colour is 160/255 (`m_shadowColor` 0x7fa0a0a0, alpha unused); a product differs in gamma and linear space by a power only, so the factor is its linear value.
- An object just outside the view is still posed when its shadow reaches into it: the culling box takes in the tip of the shadow.

A `MaterialPass` with `HasTransparency` and a pipeline processor is the whole of the Stride side: no shader of our own. **Direct3D 11 refuses colour factors for alpha:** `new BlendStateDescription(a, b)` uses the
same factors for both and the device creation throws; `ShadowVolumeRenderer.Multiply` sets alpha apart. `FxBlend.Multiply` had this defect since it was written and never met a texture that used it.

Left out: the volume of a skinned mesh (the original has none), `SHADOW_PROJECTION` and the projected kinds (no shipped object names them), a decal on a bridge deck (it lies on the terrain under it), the
placement preview's shadow (`ObjectPlacementShadows`), debris (`Shadow` of a creation list's debris).

## Clouds and the macro texture

`TerrainShader2Stage`: two textures over the map, multiplied into terrain, roads and bridges, each 31.5 cells wide. `TSCloudMed.tga` slides at 0.02 and 0.03 of its width per second of game time and is off
at night (`BaseHeightMapRenderObjClass::useCloud`); `TSNoiseUrb.tga` stands still. Both switched by `UseCloudMap`, `UseLightMap`.

The material of the terrain is a graph without a coordinate that can change per frame (a `ComputeTextureColor`'s offset is a constant of the compiled shader). So the picture moves instead of the coordinate: the cloud
texture is read on the CPU (`CnCNew.Images.DdsReader`) and shifted bilinearly into a dynamic texture when it has moved by an eighth of a texel (`TerrainClouds.Shift`, 16 KB).

## The border of the map and what lies beyond

`W3DShroud`: the first row and column of the shroud texture and everything outside the playable area hold the border's level, black at the start of every map. Here the fog texture has a ring of one texel, sampled by
clamping (`FogMap`): the terrain beyond the playable area is black until a script says `DISABLE_BORDER_SHROUD`, which clears the ring (`ClearAlpha`) so a cut scene can look over the edge. The compositor clears to black
(`W3DDisplay`): beyond the height field there is nothing.

## Sky box

`WaterRenderObjClass::Render`: `new_skybox.W3D` (a cube of 1,500 units without a floor) scaled by `SkyBoxScale`, around the camera at the height `SkyBoxPositionZ`, while a script has said `DRAW_SKYBOX_BEGIN`:
`W3DDisplay::reset` clears `m_drawSkyBox` for every map, whatever `GameData.ini` says. It stands far outside the far plane of a low camera, so `SkyLayer` draws it smaller by a factor around the eye, which changes nothing
on the screen; it writes no depth and is drawn first, because a cube inside the far plane has faces nearer than distant terrain. Five textures, the model's or a map's `WaterTransparency` (`SkyboxTextureN` ...).

## Weather

`W3DSnowManager`, `SnowField`: nothing is simulated. A grid of emitters fills a box around the camera, and the height of a flake is `ceiling - (time * velocity + camera height within the box + start height) mod box`, with a
sine to the side. The same formula is the rain of the maps that name `EXRainDrop.tga`. The base `Weather` block says `SnowEnabled = no`; twelve maps switch it on in their `map.ini`, which the definition graph of a running game
leaves out (`MapOverlayMode.Exclude`), so `MapEnvironment` reads the two blocks it needs from the file beside the map. An unknown key of a `Weather` block is an error. `SHOW_WEATHER` hides and shows it
(`SnowManager::setVisible`). A flake is a sprite of `SnowPointSize * viewport height / distance` pixels within the data's limits, the size a point sprite with `D3DRS_POINTSCALE_C` 1 has, drawn before the sorted particles.
Blocks of 16 emitters are tested against the view; what is below the ground is left to the depth test, as in the original.

## What it costs

Measured with `--smoke-test` (Release, 1920 x 1080, Ryzen 3 4350GE with its integrated Radeon, virtual display):

| Run, `--no-vsync` | Frames | Without shadows, wall | With, wall | Per frame | In the last frame |
| --- | --- | --- | --- | --- | --- |
| `ShellMapMD` as a mission, two runs each | 3,000 | 59.6 / 59.0 s | 60.9 / 60.0 s | about +0.4 ms | 4,386 sides, 34 decals, `shadowMs` 0.10 |
| Twilight Flame, eight computer players, spectator, one run each | 6,000 | 69.7 s | 71.0 s | about +0.2 ms | 5,185 sides, 6 decals, `shadowMs` 0.10 |

Wall time holds the load of 15 to 20 s, the same on both sides, and the difference is CPU and GPU together. With the display's wait the same eight-player match took 119.7 and 119.4 s without shadows and 119.7 s with them: 60 frames a second
held either way, 6,000 frames at a mean of about 8 ms without the wait. That view shows 18 to 26 objects, a player's zoom over a base. **Not measured:** a view full of an army, or the spectator zoomed far out; the stencil's cost grows
with the screen area shadows cover.

The object layer's `shadowMs` is the CPU time of silhouettes, corners and the upload; `object layer:` of a smoke test prints it with `shadowQuads` and `shadowDecals`.

## What was looked at

`Alpine Assault` frame 120 at two heights (a command centre's shadow over its own roof and the ground, a dozer's), frame 2,600 of an autoplay (a grown base with fences), `capture-building` (the ranger's decal), `Bitter Winter`
(snow, the sun's direction on snow), `MD_USA01` at frames 100, 500 and 840 (sky, a low camera, the base from a tower), `ShellMapMD` at frame 300 (a beach, water, decals, palm trees), and the main menu in both skins.
