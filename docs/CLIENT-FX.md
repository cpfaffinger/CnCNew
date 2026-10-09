# Stride client: effect layer (`FxLayer`)

As of: 30 September 2026 (package S3)

`CnCNew.Client.Stride.Layers.FxLayer` draws what `CnCNew.Presentation.Fx` (`docs/PRESENTATION-FX.md`) delivers per
frame: particles (camera-aligned or ground-aligned sprites, streaks), tracers and ground decals (scorch marks). `FxFrame`
does not deliver FX models (unused in the retail content, see below); muzzle flashes are regular particle systems and
need no special case. The layer only reads what `FxExecutor.Capture` hands out as `FxFrame`, and never writes back into
the simulation or the presentation.

## Architecture

```
FxExecutor.Capture(isVisible)          FxLayer.Update
        │  FxFrame                            │
        ▼                                      ▼
   FxBuilder.Build  ──── quads ────▶  FxGeometry (one dynamic vertex buffer, static index buffer)
        │  Runs (material, quad range, depth)
        ▼
   FxRunPool.Draw  ──── points pooled entities at quad ranges
```

- **`FxVertex`/`FxGeometry`** (`Layers/Fx/FxVertex.cs`, `FxGeometry.cs`): position, packed RGBA, one UV coordinate;
  every primitive is a quad (4 corners, 6 static indices). A CPU array is filled per frame, only the used
  prefix is uploaded with `SetData` (WriteDiscard). `DefaultMaxQuads = 32768`; a full buffer counts
  `DroppedQuads` instead of silently losing quads.
- **`FxMaterials`** (`FxMaterials.cs`): four Stride materials for the W3D sprite shader presets (`Additive`, `Alpha`,
  `AlphaTest`, `Multiply`), unlit via `MaterialEmissiveMapFeature(UseAlpha=true)` (texture × vertex colour or only
  vertex colour), manual `BlendState` per preset, `DepthFunction=LessEqual`, `CullMode=None`. `AlphaTest` uses
  `MaterialTransparencyCutoffFeature` with reference 96/255 like `dwShaderPresets[SHADER_ALPHATEST]` in `pointgr.cpp` and
  writes depth; the three blend presets write no depth (`RenderServices.DepthWrites`, shared with
  `W3dDepthWritePipelineProcessor`).
- **`FxRunPool`** (`FxRunPool.cs`): per material key a growing pool of single-mesh entities that all reference the same
  vertex/index buffer. Every "run" (contiguous quad range with one material) gets a free slot per frame;
  `StartLocation`/`DrawCount` point at the range, the bounding box lies at the target position on
  the view axis. Unused slots are deactivated (`ModelComponent.Enabled = false`), never removed, so that the
  transparent render stage reuses them between two frames without allocating anew.
- **`FxBuilder`** (`FxBuilder.cs`): builds the quads and runs from an `FxFrame` (see below).
- **`FxStats`/`FxStatsTotals`** (`FxStats.cs`): numbers of one frame and their aggregation over a run respectively
  (peak values, averages, summed skip counters). `FxLayer.Dispose` prints one line if `--smoke-test` is among the
  command-line arguments.
- **`FxDemo`** (`FxDemo.cs`): diagnostic mode via the environment variable `CNCNEW_FX_DEMO` (see below).

`FxLayer` passes a non-allocating closure in as the camera frustum filter for `FxExecutor.Capture`
(`Frustum.Intersects` against a box around particle position/size). With that, only visible particles flow into the
build step at all; the culled ones are nevertheless in `ParticleFrame.Culled` and in the statistics.

### Order and sorting

`FxBuilder.Build` follows the drawing order of the W3D particle passes:

1. **Decals** first (`EmitDecals`): per decal one polygon laid onto a height grid (`n = clamp(⌈2R/cell size⌉, 1, 8)`
   cells per axis, every corner at `TerrainHeightSampler.HeightAt` plus 0.3 units of lift), frustum-filtered via an
   extended Aabb, alpha blend, descending depth in fixed steps (the farthest first), fading
   via `ExpireFrame`/`FadeFrames`.
2. **Ground-aligned sprites and alpha-test sprites** (`EmitUnsortedParticles`): an allocation-free counting sort
   by batch preserves the instance order within a batch; this group is not sorted globally (like the
   reference, `SHADER_ALPHATEST` draws by depth test and ground-aligned particles are not sorted in), but
   only combined into one run per batch.
3. **All camera-aligned blended sprites, streaks and tracers** (`EmitSorted`) in one common back-to-front
   sort by depth (`Array.Sort` over preallocated arrays, no LINQ). Consecutive entries with the same
   material are combined into one run; with that the number of draw calls follows the texture changes along the
   sorted list, not the particle count. From `FxBuilder.MaxSortedRuns` (512) runs on, the rest of the sorted list is
   instead grouped per material (still depth-sorted *within* each group); that keeps the slot count bounded even in
   extreme cases and is counted as `DeferredItems`.

The depth of a run controls the position of the pooled entity on the view axis (`eye + forward * D`), at which the
transparent render stage performs its own back-to-front comparison of the bounding-box centres
(`ModelComponent`/`ModelRenderProcessor`, see `Rendering/W3dDepthWritePipelineProcessor.cs`). Decal runs get
depths near the far plane in descending fixed steps (order of emission), sorted runs get the
mean of their item depths, clamped to `[Near·1,5, Far·0,98]` and with a minimum distance to the previous run, so that
two runs never get the same depth.

### Primitives

- **Camera-aligned sprites**: corners around the axes `a = right·cosθ + up·sinθ`, `b = -right·sinθ + up·cosθ`, edge length
  `size` (not radius), UV TL(0,0) BL(0,1) BR(1,1) TR(1,0) — like `PointGroupClass::Render` (`pointgr.cpp`).
- **Ground-aligned sprites**: the same formula with the world axes X/Y instead of camera axes; every corner is additionally shifted by
  the terrain height difference to the centre, so that the flat quad does not float or sink on a slope.
- **Streaks**: a polyline band through the particles of a system in instance order (`W3DParticleSystemManager` passes
  them in this order to `LineRenderer`, see `W3DParticleSys.cpp`); the width follows the particle size, the
  UV runs over the distance travelled. The first edge is set to RGBA 0 as in the original (`RGBAArray[0] = 0`), to
  remove the hard tear-off edge at the end. A streak segment with fewer than 2 points is discarded and counted
  (`SkippedShortStreaks`).
- **Tracers**: camera-aligned band from `Head` to `Head + Direction·Length`, untextured, alpha blend, minimum width
  1.5 pixels (from `WorldPerPixelAtUnitDepth`, so that a tracer at a great distance does not shrink below one pixel).
- **Additive particles** output only RGB (`ParticleShader.Additive` ignores alpha like `ParticleSys.cpp`, which
  skips the alpha keyframe progress for additive systems); `VolumeParticle` is drawn like a normal sprite
  (the reference varies only the emission here, not the rendering).

### Linear colour space correction

If `GraphicsDevice.ColorSpace == Linear`, the packed RGB vertex colours are converted through a 256-byte sRGB→linear table
(alpha stays unchanged), so that `texture × vertex colour` in the linear pipeline gives the same look as the
reference in gamma space. The table is built once per `FxBuilder`.

## Known limits (never silent, always counted)

| What | Why not drawn | Counter |
|---|---|---|
| `ParticleKind.Drawable` | No W3D particle model render path in this package; in the retail content 1 of 1,087 particle systems affected | `SkippedDrawable` |
| `ParticleKind.Smudge` | Screen-space distortion (back buffer sample), no post-processing access in this package | `SkippedSmudge` |
| `ParticleShader.None`, empty texture | No drawing state definable | `SkippedUnusable` |
| `LightPulseInstance` | No pooled point lights in this package; the number of active pulses is counted, not drawn | `NotDrawnLights` |
| `RayEffectRequest` | 0 uses in the retail content (`docs/PRESENTATION-FX.md`); beam drawables are not part of this package | `NotDrawnRays` |
| FX models | `FxFrame` delivers none (no FxNuggetKind for them); muzzle flashes are particle systems and are drawn regularly | – |

`FxExecutor.Capture` delivers `RayEffects` and `ViewShakes` once (they are cleared afterwards). `FxLayer` is the
only caller of `Capture` in the Stride client layer and keeps the most recently captured `FxFrame`
(`FxLayer.LastFrame`) available for later consumers (camera shake, beam rendering) instead of discarding it.

## Diagnostic mode `CNCNEW_FX_DEMO`

For a quick visual check without waiting for a real skirmish:

- `CNCNEW_FX_DEMO=ring` (or `1`): every 45 logic frames, a ring of effects is laid around the camera centre through the public API of `match.Fx`
  (`Particles.Spawn`, `ExecuteFxListAtPosition`, `Decals.AddScorch`):
  one particle system template each per (shader, kind, ground alignment) class (first occurrence sorted by name), the
  first two FXLists with a `Tracer` nugget, and four scorch marks of different types. The console prints once
  the chosen templates/FXLists.
- `CNCNEW_FX_DEMO=bench`: builds a synthetic frame with 10,000 particles (mixed materials, ground- and
  camera-aligned orientation, 100 in streaks) and measures the mean time of `FxBuilder.Build` over 400 repetitions;
  nothing is spawned.

Both modes run only if the variable is set, and change nothing in the simulation.

## Measured values

Headless capture, `game/content`, map "Alpine Assault", 1600×900, `dotnet build src/CnCNew.Client.Stride -c
Release` (0 errors, the same 21 warnings as before this package – all of them known `NU1901`/`NU1903` and
AssetCompiler/YamlAssemblyRegistry notes, no new ones):

- **Demo ring** (`CNCNEW_FX_DEMO=ring`, 400 frames, `--height 120 --pitch 50`): last frame 2,457 active particles, 32
  culled, 1,516 drawn, 9 streaks, 4 decals, 7 batches, 24 runs, 24 draw calls, 2,557 quads, 245,472 bytes of
  upload, 0.23 ms CPU (build+pool+upload); run mean 0.50 ms, maximum 53.9 ms (JIT warm-up of the first frames).
  Two independent runs with identical arguments give the same screen output:
  SHA-256 `6614963C3EC78E46ADBE101795F1358A7556041CAD6FFAB7211B73778098D2F4` (both runs).
- **Real skirmish** (`--autoplay --seed 7 --faction FactionGLA --opponent FactionChina`, 12,000 frames = 400 s):
  over the run 2 tracers and 42 light pulses (not drawn, see table above) logged, 0 dropped quads,
  0 unknown batches. Two independent runs with identical arguments give the same statistics line and the same
  image: SHA-256 `C5855B1BF20E2492C91F200BA4293808200274CF9E86E687845808DF7736E704` (both runs). The camera stands at the
  default view over the map centre; the skirmish itself took place outside the field of view, which is why no
  effects are visible in the image although the pipeline processed them (honestly shown in the statistics, not in the image).
- **CPU budget for 10,000 active particles** (`CNCNEW_FX_DEMO=bench`, mean over 400 repetitions of a synthetic
  frame with mixed materials, random depth and 1,000 streak particles in 100 streaks): `fx bench: 10000 live
  particles, 5400 drawn, 9900 quads, 517 runs, 1.735 ms per build (avg of 400)` — within the budget of 2 ms CPU,
  although the random depth order in this extreme case (5 materials, practically randomly interleaved) reaches the
  run upper limit `FxBuilder.MaxSortedRuns` (512) and groups the rest of the list per material; a
  runtime-dependent value, re-measure when needed.

These values are snapshots (see CLAUDE.md, "No trust in numbers from docs"); re-measure on changes to the particle
catalog, map or budget.

## Public API of this package

- `FxLayer` implements `IWorldLayer` (`Attach`, `Update`, `Dispose`) like the other world layers.
- `FxLayer.Stats` (`FxStats`): numbers of the most recently drawn frame.
- `FxLayer.LastFrame` (`FxFrame?`): the most recently captured snapshot, including the ray/view-shake requests
  delivered once.
- Everything else under `Layers/Fx/**` (`FxGeometry`, `FxMaterials`, `FxRunPool`, `FxBuilder`, `FxStats`, `FxDemo`) is
  `internal` and intended only for this layer.


## Time and tick rate (Phase 21)

The FX layer of the client draws what `FxExecutor.Capture` delivers and keeps no clock of its own. Since Phase 21, `LiveGame` steps the effect clock once per **original frame** (1/30 s of game time) instead of once per simulation tick (`docs/PRESENTATION-FX.md`, "Time and tick rate"): at 30 Hz that is the same, at 60 Hz the particle image still changes 30 times per second. `FxDemo` (`CNCNEW_FX_DEMO=ring`) counts `match.Fx.Frame` and with that places its ring every 45 original frames (1.5 s) at every rate.

The client has the option `--tick-rate N` (30, 60 or 120 Hz; the default is `GameData.DefaultSessionRate`, 30 Hz until Phase 23). In the smoke test one drawn frame is one original frame of game time at every rate (one tick at 30 Hz, two at 60 Hz); `--frames`, `--every` and the frame numbers of an input script therefore mean the same game time at every rate, and a capture at frame N shows the same point in time. The FX bench (`CNCNEW_FX_DEMO=bench`) is unaffected by this.

Not verified: the look of the effects at 60 Hz (no human has seen it).

## Source references

- `pointgr.cpp` (`PointGroupClass::Render`): quad construction, UV order, edge length = `Size`, shader presets
  (`dwShaderPresets`), alpha-test reference 96/255.
- `W3DParticleSys.cpp`: handing the streak points to `LineRenderer`, `RGBAArray[0] = 0`.
- `ParticleSys.cpp` (`Particle::update`, `ParticleSystemTemplate`): additive systems skip the
  alpha keyframe progress; particle size is the full edge length, not the radius.
