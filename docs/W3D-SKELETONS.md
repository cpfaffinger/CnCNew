# W3D hierarchies, animations and models

As of: 30 September 2026

Besides meshes and materials (`W3D-MATERIALS.md`), `CnCNew.W3D` now delivers whole models: skeleton
(hierarchy), HLOD with sub-objects per bone, uncompressed and compressed animations,
visibility channels, skinning influences and a renderer-independent pose evaluation. The layer
knows neither Stride nor `CnCNew.Content`; files come through a resolver
`Func<string, Stream?>` (virtual path → stream, `null` = missing).

## Entry points for integrators

| Type | Purpose |
| --- | --- |
| `W3dAssetLibrary(resolver, "Art/W3D")` | Loads and caches thread-safely `GetModel(name)`, `GetSkeleton(hierarchyName)`, `GetAnimation("HIER.ANIM")`. |
| `W3dAssetFile.Read(stream)` | Decodes all top-level objects of a file (meshes, hierarchies, HLODs, animations, boxes, null objects). |
| `W3dModel` | Shared, immutable model: `Skeleton`, `SubObjects` (LOD, bone, mesh/box, flags), `FindBone`, `FindBones`, `FindSubObject`, `CreatePose()`, static `SkinVertices`. |
| `W3dPose` | Instance state: `SetRestPose(root)`, `Evaluate(binding, frame, root, rawSampling)`, `BoneWorldTransforms`, `IsBoneVisible`, `SetTurretYaw/SetTurretPitch/SetRecoil/SetBoneOverride`. |
| `W3dAnimationBinding` | Animation bound to a pivot count; `Sample(pivot, frame, …)`. |
| `W3dAnimationClock.GetFrame(mode, frameCount, frameRate, elapsedSeconds, speed)` | Time → frame for `Loop`, `Once`, `OnceBackwards`, `LoopBackwards`, `LoopPingPong`, `Manual`. |
| `W3dSkeletonAudit.Run(names, library)` | Full-content check, used by `W3dTool audit-skeletons` and the real-data test. |

Matrices follow the `System.Numerics` row-vector convention: `world = local * parentWorld`,
points are transformed with `Vector3.Transform(p, M)`. Bone 0 is the root and receives the
object matrix passed in. Rigid sub-objects are drawn with the world matrix of their bone;
skins (`IsSkinned`) are deformed into the space of the root matrix by `SkinVertices` and ignore
their HLOD bone. Model, skeleton and animation binding are immutable and are shared by all
instances; only `W3dPose` per instance is expensive (one matrix, one bool and one optional
override per bone).

## Verified chunk layouts

All sizes were checked against the real content (`game/content/Art/W3D`, 8,897 files) by
hex/script analysis; the source of the structures is `w3d_file.h` (GeneralsMD/WW3D2).
Little endian, names are null-terminated Latin-1 fields of fixed length.

| Chunk | Id | Layout (bytes) | Finding in the content |
| --- | --- | --- | --- |
| HIERARCHY_HEADER | 0x101 | Version u32, Name[16], NumPivots u32, Center vec3 = 36 | always 36, version 4.1 |
| PIVOTS | 0x102 | per pivot: Name[16], Parent u32 (0xFFFFFFFF = root), Translation vec3, Euler vec3, Rotation quat (x,y,z,w) = 60 | parents always before children, pivot 0 root |
| PIVOT_FIXUPS | 0x103 | per pivot 4×3 float (MAX matrix, row 3 = translation) = 48 | in all 6,481 hierarchies, informative only |
| ANIMATION_HEADER | 0x201 | Version u32, Name[16], HierarchyName[16], NumFrames u32, FrameRate u32 = 44 | version 4.1, 30 fps |
| ANIMATION_CHANNEL | 0x202 | FirstFrame u16, LastFrame u16, VectorLen u16, Flags(type) u16, Pivot u16, Pad u16, then (Last−First+1)·VectorLen float | 21,490 channels with surplus (see below) |
| BIT_CHANNEL | 0x203 | FirstFrame u16, LastFrame u16, Flags u16, Pivot u16, Default u8, then ⌈n/8⌉ bytes LSB-first | size exactly 9 + ⌈n/8⌉ |
| COMPRESSED_ANIMATION_HEADER | 0x281 | like 0x201, but FrameRate u16 + Flavor u16 | version 0.1, flavor 0 |
| COMPRESSED_ANIMATION_CHANNEL (time-coded) | 0x282 | NumTimeCodes u32, Pivot u16, VectorLen u8, Flags u8, then per key TimeCode u32 + VectorLen float | size exact, first key frame 0, strictly increasing |
| COMPRESSED_ANIMATION_CHANNEL (adaptive delta) | 0x282 | NumFrames u32, Pivot u16, VectorLen u8, Flags u8, Scale f32, then frame-0 vector float and packets | not in the content, tested synthetically |
| COMPRESSED_BIT_CHANNEL | 0x283 | NumTimeCodes u32, Pivot u16, Flags u8, Default u8, then keys u32 (MSB = state) | not in the content, tested synthetically |
| HLOD_HEADER | 0x701 | Version u32, LodCount u32, Name[16], HierarchyName[16] = 40 | version 1.0, always 1 LOD |
| HLOD_LOD_ARRAY / AGGREGATE_ARRAY / PROXY_ARRAY | 0x702 / 0x705 / 0x706 | Container: SUB_OBJECT_ARRAY_HEADER (0x703: ModelCount u32, MaxScreenSize f32 = 8), then SUB_OBJECT (0x704: BoneIndex u32, Name[32] = 36) | only LOD arrays; 0 aggregates, 0 proxies |
| BOX | 0x740 | Version u32, Attributes u32, Name[32], Color rgb+pad, Center vec3, Extent vec3 = 68 | 679 boxes, attribute 1 (oriented) or 2 (aligned) |
| NULL_OBJECT | 0x750 | Version u32, Attributes u32, Pad[2] u32, Name[32] = 48 | not in the content |
| VERTEX_INFLUENCES | 0x0E (in the mesh) | per vertex BoneIdx u16 + 6 bytes of padding = 8 | 169 skins, padding always 0 |

Channel types (`Flags`): 0/1/2 = translation X/Y/Z (VectorLen 1), 3/4/5 = Euler XR/YR/ZR (VectorLen 1),
6 = quaternion (VectorLen 4). The enumerators `TIMECODED_*`/`ADAPTIVEDELTA_*` (7–14) are never used by the
loaders (`hrawanim.cpp`/`hcanim.cpp` `add_channel`) as a channel type; compressed channels, too,
carry 0/1/2/6. Bit channel type 0 = visibility; the original silently discards other values –
here they are a decode error.

## Strictness and deliberate compatibility cases

Decode errors (`InvalidDataException`) are: unknown top-level chunks, unknown or duplicate
sub-chunks, wrong structure sizes, parents behind their children, hierarchy/animation version before
3.0 (the root shift of the original is not implemented, the content has no such
files), unknown channel types and vector lengths, channels outside the frame count, time codes that
do not start at frame 0 or do not increase strictly, unknown flavor, duplicate channels of the same
type for a pivot, skin padding not equal to 0 (later SAGE games store second bones and
weights there), skin bones outside the skeleton and HLOD bones outside the skeleton.

Explicitly counted, non-fatal cases:

- **Surplus of uncompressed channels**: an exporter error wrote more floats than the frame range
  states; `MotionChannelClass::Load_W3D` (motchan.cpp) skips them. So does CnCNew, counted in
  `W3dAnimation.SurplusChannelBytes` (content: 8,333,276 bytes).
- **Channels beyond the hierarchy**: `HRawAnimClass::Load_W3D` discards channels with pivot ≥
  pivot count. `W3dAnimationBinding.DiscardedChannels` lists them (content: 233).
- **Euler channels** are loaded, but never evaluated by the original; `IgnoredEulerChannels` counts
  them (content: 0).
- **Unresolvable HLOD entries** (export leftovers such as splines, `RECTANGLE01`, `... TOE0`): the
  asset manager of the original returns `null`, `HLodClass` skips them. CnCNew lists them in
  `W3dModel.UnresolvedSubObjects` (content: 45).
- **Ignored render objects**: Emitter, Dazzle, Light, Morph-Animation, HModel, LOD-Model,
  Collection, Points, Aggregate, Sphere, Ring, Lightscape, Sound, ShaderMesh are kept as
  `W3dIgnoredChunk` with kind and offset, not interpreted (content: 3 emitters, otherwise 0).
- **CRLF text-mode damage**: five `UISabotr_*` animations are repaired through the existing reconstruction
  in `W3dFileReader` and are reported by name in the audit.

## Evaluation rules

**Base pose** (`htree.cpp` `HTreeClass::read_pivots`, `Base_Update`): local matrix per pivot
`R(Rotation) * T(Translation)`; world = local · parent world; pivot 0 receives the root matrix, its
file transformation is ignored. The Euler angles of the pivots are redundant and are only
stored.

**Animation** (`HTreeClass::Anim_Update`): per pivot 1..n−1
`local = Override · R(qAnim) · T(tAnim) · base`. Animation values are deltas to the base pose.
Pivots without a channel receive translation 0 and the unit quaternion, i.e. the base pose. Pivots from the
pivot count of the animation hierarchy onwards stay in the base pose and visible. Root channels are
ignored (as in the original, loop from pivot 1).

**Uncompressed** (`hrawanim.cpp` `Get_Translation`/`Get_Orientation`): `frame0 = round_half_even(frame − 0.499999)`
(x87 `fistp` of `WWMath::Float_To_Long`), `frame1 = frame0 + 1`, wrapped to 0 for `frame1 ≥ NumFrames`;
translation linear, rotation by slerp. Frames outside `First..Last` of a channel yield
identity (0 or the unit quaternion), not the base pose. Retail EA uses for raw animations
the non-interpolating overload `HTreeClass::Anim_Update(root, HRawAnimClass*, frame)`
(`round_half_even(frame)`, wrap to 0); GeneralsGameCode enables the interpolation by default
(`WW3D_ENABLE_RAW_ANIM_INTERPOLATION`). `W3dRawSampling.Interpolated` (default) and
`W3dRawSampling.Nearest` represent both variants.

**Slerp** (`quat.cpp` `Fast_Slerp`, C fallback): shortest arc (a negative dot product flips the
target quaternion), linear below `1 − cos θ < 1e−8`, no renormalisation. The table-based
`Fast_Acos`/`Fast_Sin` are replaced by exact `MathF` functions.

**Time-coded** (`motchan.cpp` `TimeCodedMotionChannelClass::Get_Vector`, `Get_QuatVector`,
`binary_search_index`): the last key with time ≤ `(uint)frame` is searched for. If it is the last key
or the following key carries the MSB ("binary movement"), the value of the key found applies unchanged
(step), otherwise linear or slerp with `ratio = (frame − t1)/(t2 − t1)`.

**Adaptive delta** (`motchan.cpp` `AdaptiveDeltaMotionChannelClass::decompress`, `getframe`,
filter table `filtertable`): frame 0 is a float vector at the start of the data. Frame f ≥ 1 lies in
group `(f−1) >> 4` at nibble `(f−1) & 15`; per group and component a 9-byte packet follows
(filter index + 16 nibbles, even nibbles in the low, odd ones in the high half-byte, signed
−8..7). Value(f) = value(f−1) + nibble · filter[index] · Scale. Filter table: entries 0–15 are
1e−8 … 1e7, entries 16–255 are `1 − sin(90° · i / 240)` (angle computed in double, `sinf`).
CnCNew decodes once into a dense table; the order of additions corresponds to the
incremental original decoding. Query: `frame1 = (uint)frame`, following frame clamped to the last
frame (no wrap), translation linear, rotation by slerp.

**Visibility**: a raw bit channel (`BitChannelClass::Get_Bit`) yields the default outside `First..Last`,
otherwise the bit `frame − First`; frame index `(int)frame`. Time-coded (`TimeCodedBitChannelClass::Get_Bit`):
last key with time ≤ frame, before the first key, key 0 applies; the stored default is not used by the
original. Bones without a channel are visible. Sub-objects are initially visible
if they are renderable and the mesh flag `HIDDEN` is missing (`MeshClass` calls `Set_Hidden`).

**Overrides** (`pivot.cpp` `PivotClass::Capture_Update`, `W3DModelDraw.cpp`
`handleClientTurretPositioning`, `handleClientRecoil`): a captured bone receives, after base and
animation, a post-multiplication in its own space and is forced visible. Turret: `Rotate_Z(yaw)`,
barrel pitch: `Rotate_Y(−pitch)`, recoil: `Translate_X(−shift)`. The root cannot be captured
(W3DModelDraw treats bone 0 as "not found"). The variant with
world translation (`WorldSpaceTranslation`) is not implemented, because W3DModelDraw does not use it.

**Time → frame** (`animobj.cpp` `Compute_Current_Frame`): playable range 0..NumFrames−1, one
loop comprises NumFrames−1 frames. `Loop`: `t mod (n−1)`; `Once`: clamped to n−1;
`OnceBackwards`: `(n−1) − t`, clamped at 0; `LoopBackwards`: `(n−1) − (t mod (n−1))`;
`LoopPingPong`: triangle wave with period 2(n−1). The original advances incrementally; the
closed form is equivalent for monotonic time, but deviates at the "still too far" resets of the
original (crossing a whole period in one step) and at the boundary of
`LoopBackwards` at exactly a multiple of the period (n−1 instead of 0).

**Bone lookup** (`W3DModelDraw.cpp` `doSingleBoneName`, `findSingleBone`,
`rendobj.cpp` `Get_Sub_Object_By_Name`): `FindBone` is exact and case-insensitive (first hit).
`FindBones("MUZZLE")` yields the undecorated name and `MUZZLE01..MUZZLE99` up to the first gap
as bones (bone 0 counts as not found); only if nothing exists as a bone are
the same names searched for as sub-objects of the most detailed LOD (full name, then suffix after the
dot) and their bones used. This is how INI names from `Turret`, `TurretPitch`,
`WeaponFireFXBone`, `WeaponLaunchBone`, `WeaponRecoilBone` and `WeaponMuzzleFlash` are resolved.

**Skinning** (`meshgeometry.cpp` `MeshGeometryClass::get_deformed_vertices`): every vertex is attached to
exactly one bone, position = vertex · bone world, normal = rotation part without
renormalisation.

**Name resolution**: model `X` and hierarchy `X` are in `X.W3D`, animation `HIER.ANIM` in
`ANIM.W3D` (`assetmgr.cpp` `Get_HAnim`, `Get_HTree`). The original registers hierarchies
"first loaded wins" (`htreemgr.cpp` `Load_Tree`) and thus depends on the load order. CnCNew
resolves deterministically: a model uses the hierarchy of the same name in its own file, otherwise
`<HierarchyName>.W3D`; an animation likewise the hierarchy of its own file, otherwise
`<HierarchyName>.W3D`. Twelve hierarchy names are defined several times in the content (partly with
a different pivot count, such as `CVBAIKTRANSNR_D` with 14/10/10); the audit lists them.

## Measured values (`W3dTool audit-skeletons`, Release)

Measured on 30 September 2026 on `game/content`, two runs byte-identical
(stdout SHA-256 `FEE71ECF45DF6CBABF5FB371546A4E0C52AA1C8BB773B144CAB3895D22C21EE7`):

| Quantity | Value |
| --- | --- |
| W3D files in `Art/W3D` / decoded without error | 8,897 / 8,897 |
| Hierarchies / bones in total / maximum | 6,481 / 134,535 / 345 |
| HLOD models / models from bare meshes | 6,505 / 1,172 |
| Hierarchy source own file / external / default | 6,409 / 96 / 1,172 |
| Animations uncompressed / time-coded / adaptive delta | 4,074 / 21 / 0 |
| Channels uncompressed X / Y / Z / Q | 14,272 / 12,360 / 15,718 / 36,350 |
| Channels time-coded X / Y / Z / Q | 581 / 569 / 562 / 722 |
| Visibility channels (raw / time-coded) | 1,042 / 0 |
| Quaternion samples / not normalised or NaN | 4,650,500 / 0 |
| Meshes / skins / skin vertices | 70,585 / 169 / 22,710 |
| Sub-objects / renderable / boxes / initially hidden | 71,263 / 70,585 / 678 / 48 |
| Unresolvable HLOD entries | 45 |
| Unresolvable hierarchy references | 6 (animations `GENERALSKEL.GENERAL_*`, hierarchy missing in the content) |
| Discarded channels beyond the hierarchy | 233 |
| Surplus of uncompressed channels | 8,333,276 bytes |
| Ignored chunks | 3 emitters |
| Files repaired from text mode | 5 |
| Reading + decoding all files | 3.9 s (Release, incl. file I/O) |
| Whole audit incl. model building, binding, pose samples | 4.8 s |

The real-data test (`dotnet run --project tests/CnCNew.W3D.Tests -- --content-dir …`) requires 0
errors and checks these counts as well as `AVCrusader` (the turret rotates the barrel around the turret pivot,
`TurretFX01` by prefix), `ABArFrcCmd` with an embedded animation, `AIRngr_SKN` with
`AIRngr_SKL.AIRngr_STA` (the skin moves) and the time-coded animation `AIOFCR_SKL.AICIAOFF_PLNT`.

## Known limits

- Aggregate and proxy arrays are decoded and provided on the model, but not resolved into
  objects; the content contains none.
- LOD switching by `MaxScreenSize` is not implemented; all 6,505 HLODs have exactly one
  LOD. `W3dModel` delivers all LODs with their index.
- Two-animation blend (`Blend_Update`) and animation combinations (`Combo_Update`) are not
  implemented; the pose evaluation knows one animation plus overrides.
- World translation for captured bones, pre-3.0 files and morph animations are not
  supported (not in the content or not used by Generals).
- Adaptive-delta channels and time-coded bit channels are tested only synthetically, because the content
  contains none.
- `Fast_Slerp` computes with table approximations in the original; CnCNew computes exactly, the poses
  can therefore deviate within the range of the table errors.
- Meshes of a model are decoded completely on loading (not lazily); the memory is, however,
  allocated once per model and shared by all instances through `W3dAssetLibrary`.
- Emitters and all other ignored render object kinds are only counted, not interpreted.
