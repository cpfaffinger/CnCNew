# Client object layer (Stride)

Draws every `DrawableInstance` of a running match (units, buildings, props, scenery) as an
animated W3D model. Source: `src/CnCNew.Client.Stride/Layers/ObjectLayer.cs` (thin
`IWorldLayer` that `WorldLayers.CreateAll()` instantiates under this name) and
`src/CnCNew.Client.Stride/Layers/Objects/**` (the actual implementation, `ObjectLayerCore`).
`ObjectLayer` passes `Attach`/`Update`/`Dispose`/`Stats` on unchanged to `ObjectLayerCore`, because
`WorldLayers.cs` expects a parameterless `ObjectLayer` in the namespace `CnCNew.Client.Stride.Layers`;
the remaining sources live together under `Layers/Objects` in the namespace
`CnCNew.Client.Stride.Layers.Objects`.

## Coordinates and placement

The presentation delivers world coordinates (X east, Y north, Z up, right-handed) identical to the
simulation space; W3D meshes are also Z-up, so vertex positions and normals from
`W3dMesh` are taken over unchanged (no Y swap, no centring, no scaling –
`ObjectRenderVertexBuilder`, deliberately a separate, leaner copy of `W3dStrideScene.AddVertex`
without its console frame logic). The instance matrix is
`CreateScale(scale) * CreateRotationZ(yaw) * CreateTranslation(position)` (row-vector convention,
`world = local * parent`); W3D "front" is +X, sim yaw 0 is also +X.

## Instancing strategy: one skeleton node per rigid part

Every W3D model is loaded once via `W3dAssetLibrary` (from `CnCNew.W3D`, bound to
`RenderServices.Content`) and compiled into a `ModelAsset`
(`ModelAssetCache`/`ModelAsset.Build`). The highest LOD level is split into rigid and skinned
render objects:

- **Rigid parts** share **one** Stride `Model` and **one** skeleton: node 0 is an
  unused root, every rigid render object gets node `Index+1` with the flags
  `EnableRender | OverrideWorldMatrix`. According to the decompiled `Stride.Rendering.SkeletonUpdater`
  (`UpdateNode`), this pair of flags skips the automatic world matrix computation from the hierarchy
  completely – the layer writes `boneWorld * instanceMatrix` directly into
  `ModelComponent.Skeleton.NodeTransformations[node].WorldMatrix` every visible frame (bone world matrix from a
  `W3dPose`, evaluated with `root = Identity`, since the instance matrix already contains the root). With that,
  **one** pooled `PooledEntity` (entity + `ModelComponent`) draws a complete,
  multi-part rigid model; nothing is cloned per instance, except for HOUSECOLOR vertex colour
  (see below).
- Sub-object visibility (default from `W3dSubObject.IsVisibleByDefault`, overridden by
  `ModelStateSpec.SubObjects` in declaration order, last match wins, matching by
  full name or suffix after the dot, `OrdinalIgnoreCase` – `RenderObjClass::Get_Sub_Object_By_Name`)
  is **set anew every visible frame** by toggling the `EnableRender` bit of the node;
  a model change resets it to the model default anyway via `ModelComponent.CheckSkeleton`/`ModelUpdated`.
- **Skinned parts** (169 in the full content) get a single-node model of their own per instance
  (`SkinPart.CreateRuntimeModel`, skeleton and materials shared, but separate, dynamic
  vertex buffers per batch – a buffer cannot be shared by several instances with different poses).
  Every visible frame: `W3dModel.SkinVertices(mesh, pose.BoneWorldTransforms, …)`
  delivers positions/normals in model space (root = Identity, as with rigid parts); a
  `SourceIndices` array maps every output vertex (present in duplicate per triangle) to its
  source vertex, colour/UV stay from the template. The node (index 1) gets the same
  instance matrix as rigid parts.
- Not resolved: camera-facing billboards (`W3dSubObject.IsCameraFacing`), HLOD aggregates
  and proxies. Since Phase 15 turrets follow the shots (`DrawableInstance.TurretYaw/TurretPitch`, see
  "Phase 15: corrections of the world presentation"); without a shot the rest pose remains.

Non-indexed triangle list batches come from `W3dRenderPlan.Build`; a single
identity index buffer that grows on demand (`SharedIndexBuffer`) serves every batch size, since only
the prefix up to the respective vertex count is read. Batches with an animated mapper
(`W3dLinearOffsetTextureMapper`/`W3dGridTextureMapper`) get a dynamic vertex buffer that
is recomputed at most once per frame and asset (`ModelAsset.RefreshAnimatedBatches`,
shared across all instances of the same asset – the UV distortion depends only on the time, not
on the instance).

## Team colour

Two independent mechanisms, separate in the original (`W3DAssetManager.cpp Recolor_Mesh`):

1. **ZHC textures** (`remapAlphaTexture32Bit`, `DO_HUE_SHIFT`): texture names with the prefix "ZHC" are
   expected as alpha mask TGAs (fourth letter "A"); the alpha channel is a mask
   (`mask = 255 - alpha`), masked pixels take over the hue of the team colour and scale
   their saturation with the team saturation, the output alpha is always 255. Implemented in
   `Layers/Objects/HouseColor.Recolor` (HSV conversion rewritten from the description, not
   copied from the original) and connected to `W3dMaterialFactory.Create` via `W3dTextureCache.HouseColor`
   (additive extension, see diff in `W3dTextureCache.cs`). Cached per (texture, team colour);
   unsupported ZHC variants (no TGA, no alpha mask byte) are warned about once
   and drawn uncoloured. The neutral team colour (`TeamColors.Neutral`, RGB 200/200/200) runs
   through the same path as every other colour.
2. **HOUSECOLOR vertex colour** (`Recolor_Vertex_Material`): meshes whose name (after the container
   dot) begins with "HOUSECOLOR" (`_strnicmp(…, 10)`) tint ambient/diffuse of the
   vertex material in the original. Here: `HouseColor.TintVertexColors` replaces the RGB of colour channel 0 (and channel 2,
   if the batch has two material passes) with the team colour, alpha is preserved. Since the colour
   is baked into the vertex buffer, this needs a **model variant per team**: `ModelAsset`
   recognises on loading whether HOUSECOLOR meshes occur at all (`HasHouseColorMeshes`); only then
   does `GetRigidModel(team)` lazily build a variant (shared skeleton, shared material list,
   only the affected `Mesh` objects are replaced with a new vertex buffer), cached per
   team hex code. For skinned HOUSECOLOR batches, tinting is instead done once on the (re)build of the
   skin runtime model (known limitation: a change of owner after the first build
   does not re-colour the skin, since afterwards only position/normal are recomputed).

## States, animation, construction height

The presentation has already selected the matching `ModelStateSpec` per draw module
(`DrawableInstance.Selections`, `BestStateSelector`); from it the layer only selects which
animation from `ModelStateSpec.Animations` is played: deterministically from a hash of the
entity/scenery ID and the state index, preferably a non-idle animation, otherwise idle. With
`AnimationFlags.RandomStart` an equally deterministic time offset is added to the start time,
so that identical units do not animate in sync. The playback speed comes from
`AnimationSpeedMin/Max`, interpolated equally deterministically. `W3dAnimationClock.GetFrame` delivers
the frame from `AnimationMode`/frame rate/elapsed time; `W3dPose.Evaluate` is only called
when the frame has changed by more than ~0.001. If the animation is missing
(`W3dAssetLibrary.GetAnimation` returns `null`), the rest pose remains, the name is counted and
warned about once (`ObjectLayerStats.MissingAnimations`). Since Phase 15 `AnimationMode.Manual` stands on
the start frame of the state (doors, packages); the flags `StartFrameFirst/Last` and
`MaintainFrameAcrossStates*` are still not evaluated. Turrets, tyres and muzzle flash: see
"Phase 15: corrections of the world presentation".

`AnimationFlags.AdjustHeightByConstructionPercent` lowers the instance by
`height * (1 - construction progress)` (`W3DModelDraw::adjustTransformMtx`, `references/generals-game-code/
Core/GameEngineDevice/Source/W3DDevice/GameClient/Drawable/Draw/W3DModelDraw.cpp`); the height is the
maximum Z extent of the rest pose above the pivot (`ModelAsset.Height`), an approximation of the
original collision geometry (`GeometryInfo::getMaxHeightAbovePosition`) via the visible bounds.

## Culling and statistics

`match.Camera.Frustum.Intersects(Aabb)` with a sphere-derived AABB around the instance position
(radius from the rest pose of all parts, approx. 25 % + 2 units of margin, `ModelAsset.Radius`) – a
deliberate simplification compared with a rotated bounding box; for rotated, elongated objects
the culling can turn out somewhat more conservative than necessary. `ObjectLayerStats` counts per frame drawables,
drawn, hidden, pooled entities, skinned draws, triangles and, cumulatively,
loaded/missing models, missing textures, missing animations and placeholders; in addition
frame milliseconds and bytes allocated per thread (`Stopwatch`/
`GC.GetAllocatedBytesForCurrentThread`). In `--smoke-test` mode the line is written to the console once when
`--frames` is reached (or on exit).

Drawables no longer seen return their pooled entities (`EntityPool`, a single
free store for rigid, skinned and placeholder entities, since only `ModelComponent.Model`
is exchanged); instances that are hidden but still present are only removed from the scene,
not released, and are reused on return into the visible area.

## Missing models

If `W3dAssetLibrary.GetModel` fails (file or hierarchy missing), the name is warned about and
counted once (`ObjectLayerStats.ModelsMissing`); the instance gets a visibly
pink placeholder box instead (`PlaceholderBox`, unit cube, unpainted, placed via the same
skeleton node trick as rigid parts) and counts in `ObjectLayerStats.Placeholders`. In
all test runs (Alpine Assault, three factions, several hundred frames) no missing
models, textures or animations occurred.

## Known limitations

As of Phase 15 (re-measured with `PresentationTool model-audit`, see below); earlier entries on black
back faces and missing turrets are superseded by the Phase 15 corrections.

- **Model states not set:** after the additional work of 4 October 2026 the presentation sets 95 of 106 state names (before 52; details and approximations in
  `docs/MODEL-CONDITIONS-BUILDINGS.md`, `docs/MODEL-CONDITIONS-UNITS.md`). Eight remain unset, each because the simulation does not know the behaviour: `EXPLODED_BOUNCING/FLAILING` (nothing is flung away), `JAMMED` (no jammer),
  `PANICKING`, `RAPPELLING` (no rope descent), `PREORDER` (online profile feature), `DOCKING_BEGINNING` (entry and exit fall into one tick). `PREATTACK_A/B/C` has been set by the simulation since 4 October 2026
  (`PreAttackDelay`, `docs/SIM-FIRING-TRACKER.md`), `RAISING_FLAG` since the staged capture. Doors of factories are an
  approximation (the simulation has no door; the opening begins `DoorOpeningTime` before the appearance of the unit).
- **Suspension and steering** have been built since 4 October 2026 (`SuspensionTracker`: tilt to the terrain and when accelerating, steering angle, four suspension travels, cabin sway); what is missing is
  the lift effect when rolling over something, the powerslide and the travel limit of the cabin.
- **Roads** are drawn (`docs/CLIENT-ROADS.md`); the `GlobalLighting` chunk of the map is still skipped: the object lighting comes from `GameData`
  (`TerrainObjectsLighting*`), not from the map.
- **Fog:** map objects without a simulation entity are hidden until their ground is explored; remembered buildings and explored scenery that is currently not visible are
  darkened (`DrawableInstance.Fogged`, brightness 0.25 like the squared fog value of the terrain; test aid `CNCNEW_OBJECT_FOG=1`). Skinned parts (infantry) are not darkened.
- **Hiding of parts (correction 4 October 2026):** toggling the `EnableRender` bit of a skeleton node hides **no** mesh in the running client (a test that hid all parts of a
  tank left it fully drawn). As a result, muzzle flash and every `HideSubObject` of all objects were permanently visible. A hidden part now additionally gets a
  degenerate (zero) world matrix; captures prove it (tick 30 without, tick 66 – one tick after the shot – with muzzle flash). Test aid: `CNCNEW_FX=0` hides the particle layer in order to distinguish model parts
  from effects.
- Camera-facing billboards, HLOD aggregates/proxies are not drawn (counted in
  `W3dModel.UnresolvedSubObjects`/`Aggregates`/`Proxies`; `model-audit`: 10 models with entries that cannot be
  resolved; 47 models do not load because the file is missing in the assets – map scenery `cb…`).
- `StartFrameFirst/Last`, `MaintainFrameAcrossStates*` (see above).
- Environment mapping texture stages use a fixed normal/view direction
  (`ObjectRenderVertexBuilder.FixedViewDirection`, approx. (0, −0.6, 0.8) normalised) instead of a
  real per-pixel reflection, because the geometry is built once per asset and shared by all
  instances.
- No blob/contact shadow (optional item of the brief): not implemented.
- HOUSECOLOR vertex colouring of a skinned part does not update on a change of owner after
  the first build (see above).

## Phase 15: corrections of the world presentation

The trigger was the first visual check by a human (`docs/DEMO-FINDINGS.md`): "half of the walls
are missing", tyres pointing upwards, no turrets, buildings in the fog without terrain. The causes were not, as
suspected, missing mesh parts (the mesh audit counts all triangles as drawn) but four
independent errors of the presentation:

1. **Index buffer released.** When growing, `SharedIndexBuffer` replaced the GPU buffer and released the
   old one immediately, although models already built still referenced it; their parts disappeared
   or showed foreign triangles. Now replaced buffers stay alive until `Dispose` (`_superseded`);
   `ModelAsset.CountGpuResourceProblems` counts dead/missing GPU resources (`ObjectLayerStats`,
   `gpuProblems`), and the `--smoke-test` aborts on a problem.
2. **Winding order.** W3D triangles are oriented counter-clockwise, Stride's front face is
   clockwise; the terrain layer already reverses the indices, the object layer (and `W3dStrideScene`)
   did not. Consequence: back-face culling removed the outsides of the walls and showed the insides.
   Both builders now emit the corners 0,2,1 (`ObjectRenderVertexBuilder`, `W3dStrideScene`).
3. **No lighting in the game.** The live scene had no directional lights; objects were black
   or only ambient. `Rendering/SceneLighting` builds sun and ambient from `TerrainLighting`, whose
   object values `ObjectsFromDefinitions` reads from `GameData` (`TerrainObjectsLighting*`). Stride lights
   point along −Z of the entity; `RotationFromForward` rotates accordingly.
4. **Bone visibility ignored.** Parts that the HLOD/the animation hides via a visibility channel
   (`Set_Animation_Hidden`) were drawn; muzzle flash sub-objects were permanently visible.
   Both now follow the bone visibility channel; muzzle flash appears only after a shot.

In addition, the states that did not exist before:

- **Turrets:** since 4 October 2026 the simulation has a turret state (`docs/SIM-TURRET-AIM.md`): the turret turns to the target before the shot, the weapon waits for the alignment.
  `TurretTracker` now only reads `Entity.TurretOf`; turn rate, rest position and elevation angle come from the typed `Turret`/`AltTurret` blocks and control the turret and
  elevation bones (`Capture_Bone/Control_Bone` technique). The second turret (`AltTurret`) is separate.
- **Tyres** (`W3DTruckDraw`): rotation angle = multiplier · distance travelled (`WheelTravel` from
  the position changes); steering and suspension are missing (see limitations).
- **Muzzle flash** (`MuzzleFlashTicks`, `FiredSlots`): sub-objects `MUZZLEFX*`/`BARRELFX*` only briefly
  after a shot of the associated slot.
- **Unexplored scenery** (`FrameProjector`): map objects without an entity appear only when their
  ground is explored, and then stay (remembered) – before, buildings stood in the black.
- **Healing symbols** (`Drawable::drawHealing`): wrench (`SCPWrench000–009`, ping-pong 60 ms) for
  buildings/vehicles, cross (`SCPCross000–015`, 30 ms) otherwise; condition: health below maximum, healing
  within 3 s (`ShowsHealing`). Drawn by the new **HUD overlay** (`Game/Hud/HudOverlayRenderer`):
  selection frames, rings (units) or corner brackets (buildings), health bars, command lines, rally point
  flags and build preview. Until Phase 15 the overlay was computed but never drawn.
- **Dock state:** the original sets `DOCKING` between entry and exit (`DockUpdate::onEnterReached`
  to `onExitReached`), not already on approach; the supply Chinook now lowers its net at the depot,
  not in flight to it (`AIUpdateInterface.IsInsideDock`).

### Map angles (item 5 of the demo findings)

`PresentationTool angle-audit` checks, on `ObjectsList` rows of elongated templates (fences, walls), which
convention aligns the rows in their longitudinal direction. On 150 maps (161 elongated of 1,520
placed templates, 11,313 row samples) the **stored angle unchanged** aligns 64.6 % of the samples
to within 5° (7,313); sign reversal 22.1 %, quarter turn 3.2 %, both 23.5 % (the shares overlap, one
sample can satisfy several conventions). For continuous fence rows the stored convention is
clearly the best (`ChainLinkFence` 90.7 % against 23.1 %). The conversion (radians, sign, +X front axis)
is thus **measured to be plausible**; the reported finding "rotated wrongly" is **not reproduced**.
The probable cause of the impression was winding and lighting (items 2 and 3 above).
**Not verified:** that a human now perceives the map objects as rotated correctly.

### Visual check of the world presentation (checklist for the human)

`scripts/capture-world.ps1` captures a fixed set of scenes (`--smoke-test --capture`, fixed
map, seed, camera; the images are under `%TEMP%\cncnew-captures`, never in the repo; scenes run
one after another, not in parallel). `manifest.txt` contains **preliminary** SHA-256 over the decoded pixels (not over the
PNG file: its bytes vary for the same pixels); they prove reproducibility, not correctness, and golden hashes only come into being after the acceptance.
**Measured limit of the reproducibility (4 October 2026):** nine of the eleven scenes deliver the same pixel hash over repeated runs; the two autoplay scenes `match-base-late` and
`match-combat` deviate between runs by 1–11 pixels (flag/particle edges). The phenomenon is not new – the same autoplay run at the state before Phase 15 deviates by up to 93 pixels –, the cause has not been found
(Stride/GPU time dependence suspected, not verified). For these two scenes a hash is therefore not suitable as a golden hash; a pixel comparison with tolerance would be necessary. The earlier statements "identical SHA-256
over two runs" (Phase 7, below) applied to the pairs compared at the time, not in general. Every row below
is to be ticked off by a human against the capture; without this acceptance Phase 15 does not count as confirmed.

| Scene | To be seen | Accepted |
| --- | --- | --- |
| `viewer-commandcenter` | closed walls, star emblem at the front, no black lattice skeleton | ☐ |
| `viewer-barracks` | all walls, fence, flagpole | ☐ |
| `viewer-crusader` | hull, turret, barrel in rest pose, tracks at the bottom | ☐ |
| `viewer-dozer` | dozer in the model viewer: tyres at the bottom, blade at the front (the viewer camera is fixed, the view unfamiliar) | ☐ |
| `match-start` | base with wall details, rest of the map black, no buildings in the black | ☐ |
| `match-lowangle` | flat camera: walls lit, not black | ☐ |
| `match-selected` | corner brackets + bar on the command center, ring + bar on the dozer | ☐ |
| `match-fog-unexplored` | unexplored terrain with oil derrick: everything black | ☐ |
| `match-base-late` | developed base, team colour, gun turrets, fences at the edges | ☐ |
| `match-combat` | barrel points at the target, muzzle flash, tyres/turrets not in rest pose | ☐ |
| `mission-start` | challenge base with buildings, lighting as in the skirmish | ☐ |

Additionally to be checked in the running game (cannot be captured): tyres rotate when driving; a dozer
repairs a building → wrench appears; the Chinook lowers the net only above the depot;
infantry does not stay standing in the footprint of the depot; units go around a working dozer.

### Measurement of the mesh audit (`PresentationTool model-audit <dir> --structures`, re-measured)

The audit commands (`model-audit`, `angle-audit`) were created in this phase; a "before" table therefore does not
exist with the tool – the "before" is the observation of the human and the
cause analysis above (buffer, winding, light), not a number.

| Quantity | Value |
| --- | --- |
| models selected by object states | 6,041 (4,933 used by buildings); checked 4,886; not loadable 47 |
| sub-objects (finest LOD) | 56,746; drawn 56,615; hidden by default 4; collision only 127 |
| triangles | 1,585,417 in total, **1,585,401 drawn**, 16 hidden by default, 0 lost in the render plan |
| parts drawn two-sided | 246 |
| hidden sub-objects never shown | 4 (`NBGATTLING.MUZZLEFX01/02`, `UBUNDTUNN(_S).BARRELFX01`) |
| model states never set | 11 of 106 names (before 54; list in the limitations) |

## Measured numbers (Alpine Assault, `game/content`, re-measure if needed)

- FactionAmerica vs. FactionChina, start capture: 590 drawables in total, 10 drawn in the view cone,
  78 models loaded, 2,080 triangles, 0 missing models/textures/animations,
  **0.19–0.27 ms layer time per frame, 0 additionally allocated bytes** in the steady state
  (two independent runs at 300 and 1,500 frames respectively: identical SHA-256 of the PNG capture).
- FactionGLA vs. FactionAmerica, `--autoplay --frames 6000`: 612 drawables in total, 16 drawn,
  9 skinned infantry instances, 124 models, 6,133 triangles, 0.38–0.60 ms layer time,
  168 bytes/frame (Stride-internal buffer update of the skin meshes); two independent runs
  yielded the same SHA-256.
- The brief names "~600 drawables under 4 ms" as the budget: all test maps actually deliver
  ~590–612 drawables **per frame in total** (not only in the field of view), and exactly this amount
  passes through the state/culling selection every frame – measured 0.19–0.60 ms, far within the
  budget. Only a smaller part (10–26 in these captures) additionally passes the frustum test
  and goes through the more expensive pose/skin/node update; the RTS camera limits the
  visible area such that in no test run were all ~600 visible at the same time – the reported
  time is therefore the actually measured full-load number, not an extrapolation.
- Visual check (PNG captures, `--capture`): buildings, vehicles, skinned infantry, all three
  tested factions with correct team colour (blue/red/green flags and banner surfaces),
  animation over time (different SHA-256 at frame 100 vs. 130 with otherwise the same setup).

## Source references

- `references/generals-game-code/Core/GameEngineDevice/Source/W3DDevice/GameClient/Drawable/Draw/
  W3DModelDraw.cpp`: `adjustTransformMtx` (construction height), `handleClientTurretPositioning`
  (approximation from shot events, see Phase 15), `adjustAnimation`/`doDrawModule` (animation selection on
  state change, here replaced by a deterministic hash instead of randomness).
- `W3DAssetManager.cpp`: `Recolor_Mesh`, `Recolor_Vertex_Material`, `remapAlphaTexture32Bit`
  (team colour, see above and `HouseColor.cs`).
- `colorspace.h`: `RGB_To_HSV`/`HSV_To_RGB` (rewritten from the description, not copied).
- Stride internals verified by decompilation (`ilspycmd`, `Stride.Rendering.dll` 4.3.0.2507):
  `SkeletonUpdater.UpdateNode` (behaviour of `OverrideWorldMatrix`), `ModelComponent.Update`
  (root node special case), `IndexingDictionary<T>` (`Clear`, `null` removes an entry).

## Translucent objects (Phase 16, preview model when placing)

`DrawableInstance.Opacity` (default 1) is `Drawable::setDrawableOpacity`: below 1 the object layer draws every surface of the drawable translucently. Today only `PlacementPreview` sets a value
(0.45, `docs/GAME-HUD.md` §18); entities and scenery stay at 1.

* **Material:** `W3dMaterialFactory.Opacity` (set around `Create`, like `Dim` and `Tint`). An opaque pass becomes an alpha-blended one (`SourceAlpha`/`InverseSourceAlpha`) with the alpha of the opacity (the alpha
  of an opaque texture is arbitrary); a pass with alpha test keeps its holes (alpha times opacity); a pass that already blends with `SourceAlpha` scales its alpha; additive and modulating
  passes scale the colour. The depth write stays that of the shader, so that one does not see the back through the front.
* **Cache:** `ModelAsset` holds the variants per (material slot, team colour, dimmed, flash, opacity in whole percent) and for skin parts per (part, batch, flash, opacity); a preview model therefore costs
  no more material builds after the first frame. `SolidOpacity = 100` is the state without a variant.
* **Counter:** `translucent=` in the line `object layer:` (parts of the frame drawn translucently).
* All of this does not apply to shadows (the layer draws none for the preview) and not to parts that the hide mechanism hides via the zero matrix (those stay invisible).
