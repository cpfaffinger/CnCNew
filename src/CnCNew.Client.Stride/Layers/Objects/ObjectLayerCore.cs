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
        var instanceMatrix = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ(drawable.Yaw) * Matrix4x4.CreateTranslation(position);
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
        if (part.Binding is not null)
        {
            var mode = (W3dAnimationMode)(int)state.Mode;
            var animationFrame = W3dAnimationClock.GetFrame(
                mode, part.Binding.Animation.FrameCount, part.Binding.Animation.FrameRate, part.ElapsedInState, part.Speed);
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
            rigidComponent.Materials[m] = asset.GetTeamMaterial(m, team)!;
        }

        var skeleton = rigidComponent.Skeleton;
        foreach (var rigidPart in asset.RigidParts)
        {
            var boneWorld = pose.GetBoneWorldTransform(rigidPart.BoneIndex);
            var nodeWorld = boneWorld * instanceMatrix;
            ref var node = ref skeleton.NodeTransformations[rigidPart.NodeIndex];
            node.WorldMatrix = nodeWorld.ToStride();
            // HLodClass::Update_Sub_Object_Transforms: Set_Animation_Hidden(!HTree->Get_Visibility(bone)) - the animation's visibility channel shows and hides the sub-objects of its bones.
            var visible = pose.IsBoneVisible(rigidPart.BoneIndex) && ComputeVisible(rigidPart.FullName, rigidPart.DefaultVisible, state.SubObjects);
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
