using System.Diagnostics;
using System.Numerics;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Data.Definitions;
using CnCNew.Game.Live;
using CnCNew.Presentation.Objects;
using CnCNew.Presentation.Terrain;
using CnCNew.W3D;
using Color4 = Stride.Core.Mathematics.Color4;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// Draws every <see cref="DrawableInstance"/> of the live match's <see cref="RenderFrame"/> as an animated W3D model: buildings, units,
/// props and scenery. See <c>docs/CLIENT-OBJECTS.md</c> for the instancing strategy, team-color handling and known limits.
///
/// Rigid parts of one model share one Stride <see cref="global::Stride.Rendering.Model"/> and skeleton: every renderable rigid sub-object
/// of the most detailed LOD becomes one skeleton node (index <c>partIndex + 1</c>, node 0 is an unused root), flagged
/// <c>EnableRender | OverrideWorldMatrix</c> so <c>SkeletonUpdater.UpdateNode</c> never recomputes its world matrix from the hierarchy —
/// it leaves whatever this layer wrote there. Every visible frame this layer writes <c>boneWorld * instanceMatrix</c> into each node
/// (verified against the decompiled <c>Stride.Rendering.SkeletonUpdater</c>) and toggles the node's <c>EnableRender</c> bit for
/// sub-object hide/show, so one pooled entity draws an entire (possibly multi-part) rigid model; only the skinned sub-objects
/// (<see cref="SkinPart"/>, CPU-skinned per <see cref="W3dModel.SkinVertices"/>) need one pooled entity of their own each, because their
/// vertex buffer is per-instance.
///
/// This is the real implementation; <see cref="CnCNew.Client.Stride.Layers.ObjectLayer"/> is the thin <c>IWorldLayer</c> that
/// <c>WorldLayers.CreateAll()</c> instantiates by that name and simply forwards to an instance of this class.
/// </summary>
internal sealed class ObjectLayerCore : IWorldLayer
{
    public string Name => "object";

    public ObjectLayerStats Stats { get; } = new();

    private RenderServices _services = null!;
    private LiveGame _match = null!;
    private ModelAssetCache _assets = null!;
    private EntityPool _pool = null!;
    private global::Stride.Rendering.Model _placeholderModel = null!;
    private readonly Dictionary<long, ObjectInstance> _instances = [];
    private readonly HashSet<string> _missingAnimations = [];
    private readonly List<long> _toRemove = [];
    private readonly Stopwatch _stopwatch = new();
    private bool _smokeTest;
    private int _frameLimit = int.MaxValue;
    private int _frameCounter;
    private bool _summaryPrinted;

    public void Attach(RenderServices services, LiveGame match)
    {
        _services = services;
        _match = match;
        _assets = new ModelAssetCache(services);
        _pool = new EntityPool();
        _placeholderModel = PlaceholderBox.Build(services.GraphicsDevice, _assets.Indices, new Color4(1f, 0.15f, 0.85f, 1f));

        var args = Environment.GetCommandLineArgs();
        _smokeTest = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        var framesIndex = Array.FindIndex(args, a => a.Equals("--frames", StringComparison.OrdinalIgnoreCase));
        if (framesIndex >= 0 && framesIndex + 1 < args.Length && int.TryParse(args[framesIndex + 1], out var frames))
        {
            _frameLimit = frames;
        }
    }

    public void Update(in WorldFrame frame)
    {
        _stopwatch.Restart();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var stats = Stats;
        stats.Drawables = frame.Objects.Drawables.Length;
        stats.Drawn = 0;
        stats.Culled = 0;
        stats.SkinnedDrawn = 0;
        stats.TurretsDriven = 0;
        stats.WheelsDriven = 0;
        stats.MuzzleFlashesHidden = 0;
        stats.MuzzleFlashesShown = 0;
        stats.Triangles = 0;
        stats.Placeholders = 0;

        foreach (var drawable in frame.Objects.Drawables)
        {
            var key = KeyOf(drawable);
            if (!_instances.TryGetValue(key, out var instance))
            {
                instance = new ObjectInstance();
                _instances[key] = instance;
            }

            instance.SeenThisFrame = true;
            UpdateDrawable(drawable, instance, frame);
        }

        _toRemove.Clear();
        foreach (var (key, instance) in _instances)
        {
            if (instance.SeenThisFrame) instance.SeenThisFrame = false;
            else _toRemove.Add(key);
        }

        foreach (var key in _toRemove)
        {
            ReleaseInstance(_instances[key]);
            _instances.Remove(key);
        }

        stats.ModelsLoaded = _assets.LoadedCount;
        stats.ModelsMissing = _assets.MissingCount;
        stats.TexturesMissing = _services.Textures.MissingCount;
        stats.MissingAnimations = _missingAnimations.Count;
        stats.Pooled = _pool.PooledCount;

        _stopwatch.Stop();
        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        stats.RecordFrame(_stopwatch.Elapsed.TotalMilliseconds, allocatedAfter - allocatedBefore);

        _frameCounter++;
        if (_smokeTest && !_summaryPrinted && _frameCounter >= _frameLimit)
        {
            _summaryPrinted = true;
            stats.GpuResourceProblems = _assets.CountGpuResourceProblems();
            Console.WriteLine($"object layer: {stats}");
            if (stats.GpuResourceProblems > 0)
                throw new InvalidDataException($"{stats.GpuResourceProblems} mesh buffer(s) of the object layer are disposed or too small; triangles would be missing.");
        }
    }

    public void Dispose()
    {
        if (_smokeTest && !_summaryPrinted)
        {
            _summaryPrinted = true;
            Console.WriteLine($"object layer: {Stats}");
        }

        foreach (var instance in _instances.Values) ReleaseInstance(instance);
        _instances.Clear();
        _assets.Dispose();
    }

    private static long KeyOf(DrawableInstance drawable) =>
        drawable.Entity.IsValid ? drawable.Entity.Value : (1L << 40) | (uint)drawable.SceneryIndex;

    private void UpdateDrawable(DrawableInstance drawable, ObjectInstance instance, in WorldFrame frame)
    {
        var selections = drawable.Selections;
        if (instance.Parts.Length != selections.Length)
        {
            foreach (var old in instance.Parts) ReleasePart(old);
            instance.Parts = new DrawPartState[selections.Length];
            for (var i = 0; i < instance.Parts.Length; i++) instance.Parts[i] = new DrawPartState();
        }

        for (var i = 0; i < selections.Length; i++)
        {
            var draw = drawable.Visual.Draws[i];
            var selection = selections[i];
            var part = instance.Parts[i];
            if (draw.Kind is not (DrawModuleKind.Model or DrawModuleKind.Tree or DrawModuleKind.Prop) || selection.Model is null || selection.StateIndex < 0)
            {
                ReleasePart(part);
                continue;
            }

            UpdatePart(drawable, draw, selection, part, frame);
        }
    }

    private void UpdatePart(DrawableInstance drawable, DrawSpec draw, DrawModuleSelection selection, DrawPartState part, in WorldFrame frame)
    {
        var modelName = selection.Model!;
        if (part.ModelName != modelName)
        {
            BindModel(part, modelName);
        }

        var state = draw.States[selection.StateIndex];
        var asset = part.Asset;

        if (part.StateIndex != selection.StateIndex)
        {
            part.StateIndex = selection.StateIndex;
            ChooseAnimation(drawable, asset, state, part);
            part.MuzzleBoneSlots = MuzzleBones(state, asset);
        }

        var scale = drawable.Scale > 0 ? drawable.Scale : 1f;
        var z = drawable.Position.Z;
        if (asset is not null && state.Flags.HasFlag(AnimationFlags.AdjustHeightByConstructionPercent))
        {
            // W3DModelDraw::adjustTransformMtx: mtx.Translate_Z(-height + height*pct/100); GeometryInfo::getMaxHeightAbovePosition is
            // approximated here by the asset's rest-pose max Z above its pivot (visual bounds, not the original's collision geometry).
            var progress = Math.Clamp(drawable.ConstructionProgress01, 0f, 1f);
            z -= asset.Height * (1f - progress);
        }

        var position = new Vector3(drawable.Position.X, drawable.Position.Y, z);
        // Drawable::applyPhysicsXform: Translate(0, 0, lift), Rotate_Y(pitch), Rotate_X(-roll) in the object's own frame, before the placement.
        var chassis = drawable.Chassis.IsLevel
            ? Matrix4x4.Identity
            : Matrix4x4.CreateRotationX(-drawable.Chassis.Roll) * Matrix4x4.CreateRotationY(drawable.Chassis.Pitch) * Matrix4x4.CreateTranslation(0f, 0f, drawable.Chassis.LiftZ);
        var instanceMatrix = Matrix4x4.CreateScale(scale) * chassis * Matrix4x4.CreateRotationZ(drawable.Yaw) * Matrix4x4.CreateTranslation(position);
        var radius = asset?.Radius ?? Math.Max(drawable.SelectionRadius, 2f);
        var aabb = new Aabb(new Vector3(position.X - radius, position.Y - radius, position.Z - radius),
            new Vector3(position.X + radius, position.Y + radius, position.Z + radius));

        if (!_match.Camera.Frustum.Intersects(aabb))
        {
            if (part.InScene) RemovePartFromScene(part);
            Stats.Culled++;
            return;
        }

        if (!part.InScene) AddPartToScene(part);

        if (asset is null)
        {
            part.Placeholder!.Component.Skeleton.NodeTransformations[0].WorldMatrix = instanceMatrix.ToStride();
            Stats.Placeholders++;
            Stats.Drawn++;
            return;
        }

        part.ElapsedInState += frame.DeltaSeconds;
        var pose = part.Pose!;
        if (drawable.TurretYaw is not null && !state.Turret.IsEmpty) Stats.TurretsDriven++;
        if (part.WheelBones is { Length: > 0 } && part.WheelMultiplier != 0f) Stats.WheelsDriven++;
        // W3DModelDraw::handleClientTurretPositioning: Capture_Bone + Control_Bone on the turret bones; a changed angle needs the pose evaluated again.
        if (ApplyTurret(state.Turret, drawable.TurretYaw, drawable.TurretPitch, asset, pose, part.TurretApplied, 0)
            | ApplyTurret(state.AltTurret, drawable.AltTurretYaw, drawable.AltTurretPitch, asset, pose, part.TurretApplied, 2)
            | (draw.ModuleType == "W3DTruckDraw" && ApplyWheels(draw, drawable, asset, pose, part, frame.DeltaSeconds)))
            part.LastFrame = float.NaN;
        if (part.Binding is not null)
        {
            var mode = (W3dAnimationMode)(int)state.Mode;
            // A manual animation stands on one frame: the first, or the last with START_FRAME_LAST (W3DModelDraw::setAnimationFrame at the state change).
            var manualFrame = state.Flags.HasFlag(AnimationFlags.StartFrameLast) ? part.Binding.Animation.FrameCount - 1 : 0;
            var animationFrame = W3dAnimationClock.GetFrame(
                mode, part.Binding.Animation.FrameCount, part.Binding.Animation.FrameRate, part.ElapsedInState, part.Speed, manualFrame);
            if (float.IsNaN(part.LastFrame) || MathF.Abs(animationFrame - part.LastFrame) > 1e-3f)
            {
                pose.Evaluate(part.Binding, animationFrame, Matrix4x4.Identity);
                part.LastFrame = animationFrame;
            }
        }
        else if (float.IsNaN(part.LastFrame))
        {
            pose.SetRestPose(Matrix4x4.Identity);
            part.LastFrame = 0;
        }

        asset.RefreshAnimatedBatches(frame.CommandList, frame.TotalSeconds);

        var team = drawable.TeamColor;
        var rigidModel = asset.GetRigidModel(team);
        var rigidComponent = part.Rigid!.Component;
        if (rigidComponent.Model != rigidModel)
        {
            rigidComponent.Model = rigidModel;
            rigidComponent.Materials.Clear();
        }

        for (var m = 0; m < rigidModel.Materials.Count; m++)
        {
            rigidComponent.Materials[m] = asset.GetTeamMaterial(m, team, drawable.Fogged || ForceFog)!;
        }

        var skeleton = rigidComponent.Skeleton;
        foreach (var rigidPart in asset.RigidParts)
        {
            var boneWorld = pose.GetBoneWorldTransform(rigidPart.BoneIndex);
            var nodeWorld = boneWorld * instanceMatrix;
            ref var node = ref skeleton.NodeTransformations[rigidPart.NodeIndex];
            // HLodClass::Update_Sub_Object_Transforms: Set_Animation_Hidden(!HTree->Get_Visibility(bone)) - the animation's visibility channel shows and hides the sub-objects of its bones.
            var visible = pose.IsBoneVisible(rigidPart.BoneIndex) && ComputeVisible(rigidPart.FullName, rigidPart.DefaultVisible, state.SubObjects);
            // W3DModelDraw::hideAllMuzzleFlashes / setMuzzleFlashHidden: the sub-object on a WeaponMuzzleFlash bone is hidden until the weapon fires.
            if (part.MuzzleBoneSlots is { Count: > 0 } flashes && flashes.TryGetValue(rigidPart.BoneIndex, out var flashSlot))
            {
                visible = pose.IsBoneVisible(rigidPart.BoneIndex) && drawable.HasFired(flashSlot);
                if (visible) Stats.MuzzleFlashesShown++; else Stats.MuzzleFlashesHidden++;
            }
            // The EnableRender bit alone did not hide the mesh in the running client (hiding every part of a tank left it fully drawn, 4 Oct 2026): a hidden part also gets a
            // degenerate world matrix, which no triangle survives.
            node.WorldMatrix = visible ? nodeWorld.ToStride() : default;
            node.Flags = visible ? node.Flags | global::Stride.Rendering.ModelNodeFlags.EnableRender : node.Flags & ~global::Stride.Rendering.ModelNodeFlags.EnableRender;
        }

        for (var m = 0; m < rigidModel.Meshes.Count; m++)
        {
            var mesh = rigidModel.Meshes[m];
            if ((skeleton.NodeTransformations[mesh.NodeIndex].Flags & global::Stride.Rendering.ModelNodeFlags.EnableRender) != 0)
            {
                Stats.Triangles += mesh.Draw.DrawCount / 3;
            }
        }

        UpdateSkins(part, asset, pose, state, drawable, instanceMatrix, frame);
        Stats.Drawn++;
    }

    /// <summary>Debug switch for screenshot checks: <c>CNCNEW_OBJECT_FOG=1</c> draws every object as if it stood in the fog (like <c>CNCNEW_TERRAIN_REVEAL</c> for the terrain).</summary>
    private static readonly bool ForceFog = System.Environment.GetEnvironmentVariable("CNCNEW_OBJECT_FOG") == "1";

    private static Dictionary<int, int>? MuzzleBones(ModelStateSpec state, ModelAsset? asset)
    {
        if (asset is null || state.WeaponBones.IsDefaultOrEmpty) return null;
        Dictionary<int, int>? result = null;
        for (var slot = 0; slot < state.WeaponBones.Length; slot++)
        {
            if (state.WeaponBones[slot].MuzzleFlash is not { } name) continue;
            foreach (var match in asset.Model.FindBones(name)) (result ??= [])[match.BoneIndex] = slot;
        }

        return result;
    }

    /// <summary>The tire bone settings of <c>W3DTruckDraw</c> with their axle (front steers) and side; the "Mid" bones follow the nearest axle.</summary>
    private static readonly (string Key, bool Front, bool Right)[] TireBoneKeys =
    [
        ("LeftFrontTireBone", true, false), ("RightFrontTireBone", true, true), ("LeftRearTireBone", false, false), ("RightRearTireBone", false, true),
        ("MidLeftFrontTireBone", true, false), ("MidRightFrontTireBone", true, true), ("MidLeftRearTireBone", false, false), ("MidRightRearTireBone", false, true),
        ("MidLeftMidTireBone", false, false), ("MidRightMidTireBone", false, true),
    ];

    /// <summary>
    /// <c>W3DTruckDraw::doDrawModule</c>: every tire bone turns about its axle (<c>Rotate_Y</c>) by the module's <c>TireRotationMultiplier</c> times the distance rolled; the front
    /// wheels also steer (<c>Rotate_Z</c> by the wheel angle of the locomotor's <c>FrontWheelTurnAngle</c>) and every wheel follows its spring offset (<c>Adjust_Z_Translation</c>,
    /// <see cref="CnCNew.Presentation.Objects.SuspensionTracker"/>); a cab or trailer bone swings with the steering angle times its multiplier, damped by <c>RotationDamping</c>.
    /// Left out: the powerslide addition and the path clamp of the cab angle.
    /// </summary>
    private static bool ApplyWheels(DrawSpec draw, DrawableInstance drawable, ModelAsset asset, W3dPose pose, DrawPartState part, double deltaSeconds)
    {
        float Number(string key, float fallback) => draw.Setting(key) is { } text && float.TryParse(Tokens(text).FirstOrDefault(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
        if (part.WheelBones is null)
        {
            var bones = new List<int>();
            var roles = new List<byte>();
            foreach (var (key, front, right) in TireBoneKeys)
                if (draw.Setting(key) is { } name && Tokens(name).FirstOrDefault() is { } token && asset.Model.FindBone(token) is > 0 and var bone)
                {
                    bones.Add(bone);
                    roles.Add((byte)((front ? 1 : 0) | (right ? 2 : 0)));
                }

            part.WheelBones = [.. bones];
            part.WheelRoles = [.. roles];
            part.WheelMultiplier = Number("TireRotationMultiplier", 0f);
            part.CabBone = draw.Setting("CabBone") is { } cab && Tokens(cab).FirstOrDefault() is { } cabToken && asset.Model.FindBone(cabToken) is > 0 and var cabIndex ? cabIndex : 0;
            part.TrailerBone = draw.Setting("TrailerBone") is { } trailer && Tokens(trailer).FirstOrDefault() is { } trailerToken && asset.Model.FindBone(trailerToken) is > 0 and var trailerIndex ? trailerIndex : 0;
            part.CabFactor = Number("CabRotationMultiplier", 0f);
            part.TrailerFactor = Number("TrailerRotationMultiplier", 0f);
            part.RotationDamping = Number("RotationDamping", 1f);
        }

        var changed = false;
        var chassis = drawable.Chassis;
        if (part.CabBone > 0)
        {
            // The original damps once per drawn frame at 30 frames per second; here the same fraction per elapsed 1/30 s.
            var blend = 1f - MathF.Pow(1f - Math.Clamp(part.RotationDamping, 0f, 1f), (float)(deltaSeconds * 30.0));
            var cabStep = (chassis.WheelAngle * part.CabFactor - part.CabAngle) * blend;
            var trailerStep = part.TrailerBone > 0 ? (-chassis.WheelAngle * part.TrailerFactor - part.TrailerAngle) * blend : 0f;
            if (MathF.Abs(cabStep) > 1e-5f || MathF.Abs(trailerStep) > 1e-5f || float.IsNaN(part.WheelApplied[6]))
            {
                part.CabAngle += cabStep;
                pose.SetBoneOverride(part.CabBone, Matrix4x4.CreateRotationZ(part.CabAngle));
                if (part.TrailerBone > 0)
                {
                    part.TrailerAngle += trailerStep;
                    pose.SetBoneOverride(part.TrailerBone, Matrix4x4.CreateRotationZ(part.TrailerAngle));
                }

                part.WheelApplied[6] = 0f;
                changed = true;
            }
        }

        if (part.WheelBones.Length == 0 || part.WheelMultiplier == 0f) return changed;
        var spin = (part.WheelMultiplier * drawable.WheelTravel) % MathF.Tau;
        var applied = part.WheelApplied;
        if (!float.IsNaN(applied[0]) && MathF.Abs(spin - applied[0]) < 1e-4f && MathF.Abs(chassis.WheelAngle - applied[1]) < 1e-4f
            && MathF.Abs(chassis.FrontLeft - applied[2]) < 1e-4f && MathF.Abs(chassis.FrontRight - applied[3]) < 1e-4f
            && MathF.Abs(chassis.RearLeft - applied[4]) < 1e-4f && MathF.Abs(chassis.RearRight - applied[5]) < 1e-4f) return changed;
        applied[0] = spin; applied[1] = chassis.WheelAngle; applied[2] = chassis.FrontLeft; applied[3] = chassis.FrontRight; applied[4] = chassis.RearLeft; applied[5] = chassis.RearRight;
        for (var i = 0; i < part.WheelBones.Length; i++)
        {
            var role = part.WheelRoles[i];
            var front = (role & 1) != 0;
            var right = (role & 2) != 0;
            var offset = front ? (right ? chassis.FrontRight : chassis.FrontLeft) : (right ? chassis.RearRight : chassis.RearLeft);
            var matrix = Matrix4x4.CreateRotationY(spin) * (front ? Matrix4x4.CreateRotationZ(chassis.WheelAngle) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(0f, 0f, offset);
            pose.SetBoneOverride(part.WheelBones[i], matrix);
        }

        return true;
    }

    private static string[] Tokens(string text) => text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static bool ApplyTurret(TurretSpec spec, float? yaw, float? pitch, ModelAsset asset, W3dPose pose, float[] applied, int slot)
    {
        if (spec.IsEmpty) return false;
        const float DegreesToRadians = MathF.PI / 180f;
        var changed = false;
        if (spec.AngleBone is { } angleBone && yaw is { } turretYaw && asset.Model.FindBone(angleBone) is > 0 and var yawBone)
        {
            var angle = turretYaw + (float)spec.ArtAngleDegrees * DegreesToRadians;
            if (float.IsNaN(applied[slot]) || MathF.Abs(angle - applied[slot]) > 1e-4f)
            {
                pose.SetTurretYaw(yawBone, angle);
                applied[slot] = angle;
                changed = true;
            }
        }

        if (spec.PitchBone is { } pitchBone && pitch is { } turretPitch && asset.Model.FindBone(pitchBone) is > 0 and var pitchBoneIndex)
        {
            var angle = turretPitch + (float)spec.ArtPitchDegrees * DegreesToRadians;
            if (float.IsNaN(applied[slot + 1]) || MathF.Abs(angle - applied[slot + 1]) > 1e-4f)
            {
                pose.SetTurretPitch(pitchBoneIndex, angle);
                applied[slot + 1] = angle;
                changed = true;
            }
        }

        return changed;
    }

    private void UpdateSkins(
        DrawPartState part, ModelAsset asset, W3dPose pose, ModelStateSpec state, DrawableInstance drawable, Matrix4x4 instanceMatrix, in WorldFrame frame)
    {
        if (part.Skins.Count != asset.SkinParts.Length)
        {
            RebuildSkins(part, asset, drawable.TeamColor);
        }

        for (var i = 0; i < part.Skins.Count; i++)
        {
            var skin = part.Skins[i];
            var visible = pose.IsBoneVisible(skin.Part.BoneIndex) && ComputeVisible(skin.Part.FullName, skin.Part.DefaultVisible, state.SubObjects);
            skin.Entity.Component.Enabled = visible;
            if (!visible) continue;

            W3dModel.SkinVertices(skin.Part.Mesh, pose.BoneWorldTransforms, skin.Positions, skin.Normals);
            var runtimeModel = skin.Entity.Component.Model!;
            for (var b = 0; b < skin.Part.Batches.Count; b++)
            {
                var template = skin.Part.Batches[b];
                var vertices = skin.LiveVertices[b];
                for (var v = 0; v < vertices.Length; v++)
                {
                    var source = template.SourceIndices[v];
                    ref var vertex = ref vertices[v];
                    vertex.Position = skin.Positions[source].ToStride();
                    vertex.Normal = skin.Normals[source].ToStride();
                }

                var buffer = (global::Stride.Graphics.Buffer)runtimeModel.Meshes[b].Draw.VertexBuffers[0].Buffer;
                buffer.SetData(frame.CommandList, vertices);
                Stats.Triangles += vertices.Length / 3;
            }

            // SkinVertices (root = Identity, same as the rigid pose) leaves output vertices in model-local space, like a rigid part's raw
            // mesh vertices; the node's world matrix places that space in the world, exactly like a rigid part's node.
            var skinSkeleton = skin.Entity.Component.Skeleton;
            skinSkeleton.NodeTransformations[1].WorldMatrix = instanceMatrix.ToStride();
            Stats.SkinnedDrawn++;
        }
    }

    private void RebuildSkins(DrawPartState part, ModelAsset asset, ColorValue team)
    {
        foreach (var skin in part.Skins)
        {
            RemoveFromSceneIfNeeded(part, skin.Entity);
            _pool.Return(skin.Entity);
        }

        part.Skins.Clear();
        foreach (var skinPart in asset.SkinParts)
        {
            var entity = _pool.Rent();
            var (runtimeModel, liveVertices) = skinPart.CreateRuntimeModel(_services.GraphicsDevice);
            entity.Component.Model = runtimeModel;
            for (var b = 0; b < skinPart.Batches.Count; b++)
            {
                if (skinPart.Batches[b].IsHouseColor)
                {
                    HouseColor.TintVertexColors(liveVertices[b], team, skinPart.Batches[b].Batch.Passes.Count);
                }
            }

            if (part.InScene) _services.Scene.Entities.Add(entity.Entity);
            part.Skins.Add(new SkinRuntime
            {
                Entity = entity,
                Part = skinPart,
                Positions = new Vector3[skinPart.Mesh.Vertices.Count],
                Normals = new Vector3[skinPart.Mesh.Vertices.Count],
                LiveVertices = liveVertices,
            });
        }
    }

    private void BindModel(DrawPartState part, string modelName)
    {
        ReleaseSkins(part);
        part.ModelName = modelName;
        part.Asset = _assets.Get(modelName);
        part.StateIndex = -1;
        part.Binding = null;
        part.Animation = null;
        part.LastFrame = float.NaN;
        Array.Fill(part.TurretApplied, float.NaN);
        part.WheelBones = null;
        Array.Fill(part.WheelApplied, float.NaN);
        part.CabAngle = 0f;
        part.TrailerAngle = 0f;
        part.ElapsedInState = 0;

        if (part.Asset is not null)
        {
            part.Pose = new W3dPose(part.Asset.Model.Skeleton);
            if (part.Rigid is null)
            {
                part.Rigid = _pool.Rent();
                if (part.InScene) _services.Scene.Entities.Add(part.Rigid.Entity);
            }

            if (part.Placeholder is not null)
            {
                RemoveFromSceneIfNeeded(part, part.Placeholder);
                _pool.Return(part.Placeholder);
                part.Placeholder = null;
            }
        }
        else
        {
            part.Pose = null;
            if (part.Rigid is not null)
            {
                RemoveFromSceneIfNeeded(part, part.Rigid);
                _pool.Return(part.Rigid);
                part.Rigid = null;
            }

            if (part.Placeholder is null)
            {
                part.Placeholder = _pool.Rent();
                part.Placeholder.Component.Model = _placeholderModel;
                if (part.InScene) _services.Scene.Entities.Add(part.Placeholder.Entity);
            }
        }
    }

    /// <summary>Deterministic per-instance choice: a non-idle <c>Animation</c> entry of the state if any exist, else the idle one; the frame's random-start jitter (if flagged) and playback speed are picked the same way.</summary>
    private void ChooseAnimation(DrawableInstance drawable, ModelAsset? asset, ModelStateSpec state, DrawPartState part)
    {
        part.Binding = null;
        part.Animation = null;
        part.LastFrame = float.NaN;
        part.ElapsedInState = 0;
        part.Speed = 1.0;
        if (asset is null || state.Animations.Length == 0) return;

        var seed = Hash(drawable.Entity.IsValid ? drawable.Entity.Value : unchecked((uint)drawable.SceneryIndex), part.StateIndex);
        var nonIdleCount = 0;
        foreach (var candidate in state.Animations)
            if (!candidate.IsIdle) nonIdleCount++;
        var poolIsNonIdle = nonIdleCount > 0;
        var poolCount = poolIsNonIdle ? nonIdleCount : state.Animations.Length;
        var pick = (int)(seed % (uint)poolCount);
        var skip = pick;
        AnimationSpec? chosen = null;
        foreach (var candidate in state.Animations)
        {
            if (poolIsNonIdle && candidate.IsIdle) continue;
            if (skip-- == 0) { chosen = candidate; break; }
        }

        part.Animation = chosen;
        if (chosen?.File is not { } file || chosen.Hierarchy is null) return;

        var binding = _assets.Library.GetAnimation(chosen.Name);
        if (binding is null)
        {
            if (_missingAnimations.Add(chosen.Name))
            {
                Console.Error.WriteLine($"warning: object layer could not resolve animation '{chosen.Name}' (file '{file}').");
            }

            return;
        }

        part.Binding = binding;
        var min = (double)state.AnimationSpeedMin;
        var max = (double)state.AnimationSpeedMax;
        var fraction = ((seed >> 8) % 1000) / 1000.0;
        part.Speed = max > min ? min + (max - min) * fraction : (min > 0 ? min : 1.0);
        if (state.Flags.HasFlag(AnimationFlags.RandomStart) && binding.Animation.DurationSeconds > 0)
        {
            // Different instances of the same unit should not play in lockstep; jitter the starting point within the clip's duration.
            var jitterFraction = ((seed >> 20) % 1000) / 1000.0;
            part.ElapsedInState = jitterFraction * binding.Animation.DurationSeconds;
        }
    }

    /// <summary>The reference's sub-object merge: a name matches the part's full name or the suffix after its container dot; later rules win.</summary>
    private static bool ComputeVisible(string fullName, bool defaultVisible, System.Collections.Immutable.ImmutableArray<SubObjectVisibility> rules)
    {
        var visible = defaultVisible;
        foreach (var rule in rules)
        {
            if (Matches(fullName, rule.SubObject)) visible = !rule.Hide;
        }

        return visible;

        static bool Matches(string fullName, string ruleName)
        {
            if (fullName.Equals(ruleName, StringComparison.OrdinalIgnoreCase)) return true;
            var dot = fullName.IndexOf('.');
            var suffix = dot < 0 ? fullName : fullName[(dot + 1)..];
            return suffix.Equals(ruleName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static uint Hash(uint key, int salt)
    {
        unchecked
        {
            var hash = 2166136261u ^ key;
            hash = (hash * 16777619u) ^ (uint)salt;
            hash *= 16777619u;
            return hash;
        }
    }

    private void AddPartToScene(DrawPartState part)
    {
        if (part.Rigid is not null) _services.Scene.Entities.Add(part.Rigid.Entity);
        if (part.Placeholder is not null) _services.Scene.Entities.Add(part.Placeholder.Entity);
        foreach (var skin in part.Skins) _services.Scene.Entities.Add(skin.Entity.Entity);
        part.InScene = true;
    }

    private void RemovePartFromScene(DrawPartState part)
    {
        if (part.Rigid is not null) _services.Scene.Entities.Remove(part.Rigid.Entity);
        if (part.Placeholder is not null) _services.Scene.Entities.Remove(part.Placeholder.Entity);
        foreach (var skin in part.Skins) _services.Scene.Entities.Remove(skin.Entity.Entity);
        part.InScene = false;
    }

    private void RemoveFromSceneIfNeeded(DrawPartState part, PooledEntity entity)
    {
        if (part.InScene) _services.Scene.Entities.Remove(entity.Entity);
    }

    private void ReleaseSkins(DrawPartState part)
    {
        foreach (var skin in part.Skins)
        {
            RemoveFromSceneIfNeeded(part, skin.Entity);
            _pool.Return(skin.Entity);
        }

        part.Skins.Clear();
    }

    private void ReleasePart(DrawPartState part)
    {
        if (part.Rigid is not null)
        {
            RemoveFromSceneIfNeeded(part, part.Rigid);
            _pool.Return(part.Rigid);
            part.Rigid = null;
        }

        ReleaseSkins(part);
        if (part.Placeholder is not null)
        {
            RemoveFromSceneIfNeeded(part, part.Placeholder);
            _pool.Return(part.Placeholder);
            part.Placeholder = null;
        }

        part.ModelName = null;
        part.Asset = null;
        part.Pose = null;
        part.StateIndex = -1;
        part.Binding = null;
        part.Animation = null;
        part.LastFrame = float.NaN;
        part.ElapsedInState = 0;
        part.InScene = false;
    }

    private void ReleaseInstance(ObjectInstance instance)
    {
        foreach (var part in instance.Parts) ReleasePart(part);
    }
}
