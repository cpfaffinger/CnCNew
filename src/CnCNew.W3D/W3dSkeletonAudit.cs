using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.W3D;

/// <summary>Deterministic result of <see cref="W3dSkeletonAudit"/>; every list is sorted ordinally.</summary>
public sealed record W3dSkeletonAuditReport(
    int FileCount,
    ImmutableSortedDictionary<string, long> Counts,
    ImmutableArray<string> Failures,
    ImmutableArray<string> UnresolvedHierarchyReferences,
    ImmutableArray<string> UnresolvedSubObjects,
    ImmutableArray<string> QuaternionFindings,
    ImmutableArray<string> AmbiguousHierarchyNames,
    ImmutableArray<string> RepairedFiles)
{
    public long this[string key] => Counts.GetValueOrDefault(key);

    public IEnumerable<string> FormatLines()
    {
        yield return $"Decoded {FileCount - Failures.Length}/{FileCount} W3D files ({Failures.Length} failures).";
        foreach (var (key, value) in Counts)
        {
            yield return $"  {key} = {value}";
        }

        foreach (var (title, items) in new[]
                 {
                     ("Failures", Failures),
                     ("Unresolved hierarchy references", UnresolvedHierarchyReferences),
                     ("Unresolved HLOD sub-objects", UnresolvedSubObjects),
                     ("Quaternion/NaN findings", QuaternionFindings),
                     ("Hierarchy names defined in several files", AmbiguousHierarchyNames),
                     ("Text-mode repaired files", RepairedFiles),
                 })
        {
            yield return $"{title}: {items.Length}";
            foreach (var item in items)
            {
                yield return $"  {item}";
            }
        }
    }
}

/// <summary>
/// Decodes every W3D file (hierarchies, HLODs, animations, meshes with skins), builds every model, binds every animation
/// to its hierarchy and samples it at the first, middle and last frame. Decode and assembly errors are failures;
/// missing references and suspicious values are findings.
/// </summary>
public static class W3dSkeletonAudit
{
    private const float UnitTolerance = 0.001f;

    /// <param name="decodeTime">Optional accumulator for the wall-clock time spent reading and decoding files; kept out of
    /// the report so that reports stay reproducible.</param>
    public static W3dSkeletonAuditReport Run(
        IReadOnlyList<string> assetNames,
        W3dAssetLibrary library,
        System.Diagnostics.Stopwatch? decodeTime = null)
    {
        ArgumentNullException.ThrowIfNull(assetNames);
        ArgumentNullException.ThrowIfNull(library);
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var failures = new List<string>();
        var unresolvedHierarchies = new List<string>();
        var unresolvedSubObjects = new List<string>();
        var quaternionFindings = new List<string>();
        var repaired = new List<string>();
        var hierarchyDefinitions = new Dictionary<string, List<(string File, int Pivots)>>(StringComparer.OrdinalIgnoreCase);
        var maximumBones = 0L;

        foreach (var assetName in assetNames.Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                decodeTime?.Start();
                W3dAssetFile? file;
                try
                {
                    file = library.ReadFile(assetName);
                }
                finally
                {
                    decodeTime?.Stop();
                }

                if (file is null)
                {
                    throw new FileNotFoundException($"Resolver does not know '{library.GetVirtualPath(assetName)}'.");
                }
                if (file.Source.RepairedLegacyTextMode)
                {
                    repaired.Add($"{assetName} ({file.Source.RemovedCarriageReturns} CR bytes removed)");
                }

                AuditFile(assetName, file);
            }
            catch (Exception exception)
            {
                // Every failure is kept by name; programming errors surface with their type instead of aborting the run.
                failures.Add($"{assetName}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        counts["bones.max"] = maximumBones;
        var ambiguous = hierarchyDefinitions
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => $"{pair.Key.ToUpperInvariant()}: " + string.Join(", ", pair.Value
                .OrderBy(definition => definition.File, StringComparer.OrdinalIgnoreCase)
                .Select(definition => $"{definition.File}({definition.Pivots})")))
            .ToList();

        return new W3dSkeletonAuditReport(
            assetNames.Count,
            counts.ToImmutableSortedDictionary(StringComparer.Ordinal),
            Sorted(failures),
            Sorted(unresolvedHierarchies),
            Sorted(unresolvedSubObjects),
            Sorted(quaternionFindings),
            Sorted(ambiguous),
            Sorted(repaired));

        void AuditFile(string assetName, W3dAssetFile file)
        {
            Increment("files.decoded");
            foreach (var ignored in file.IgnoredChunks)
            {
                Increment($"ignored.{ignored.Kind}");
            }

            foreach (var hierarchy in file.Hierarchies)
            {
                Increment("hierarchies");
                Add("bones.total", hierarchy.Pivots.Length);
                maximumBones = Math.Max(maximumBones, hierarchy.Pivots.Length);
                Add("hierarchies.pivot-fixups", hierarchy.PivotFixups.IsEmpty ? 0 : 1);
                if (!hierarchyDefinitions.TryGetValue(hierarchy.Name, out var definitions))
                {
                    hierarchyDefinitions.Add(hierarchy.Name, definitions = []);
                }

                definitions.Add((assetName, hierarchy.Pivots.Length));
                foreach (var pivot in hierarchy.Pivots)
                {
                    CheckQuaternion($"{assetName}: hierarchy '{hierarchy.Name}' pivot '{pivot.Name}'", pivot.Rotation);
                    if (!W3dBinary.IsFinite(pivot.Translation))
                    {
                        quaternionFindings.Add($"{assetName}: hierarchy '{hierarchy.Name}' pivot '{pivot.Name}' has a non-finite translation");
                    }
                }
            }

            foreach (var mesh in file.Meshes)
            {
                Increment("meshes");
                if (mesh.IsSkin)
                {
                    Increment("meshes.skinned");
                    Add("meshes.skinned.vertices", mesh.VertexBoneIndices.Length);
                }

                Add("meshes.hidden", mesh.IsHidden ? 1 : 0);
                Add("meshes.camera-facing", mesh.IsCameraFacing ? 1 : 0);
                Add("meshes.cast-shadow", mesh.CastsShadow ? 1 : 0);
                Add("meshes.collision-flags", mesh.CollisionTypes != 0 ? 1 : 0);
            }

            Add("boxes", file.Boxes.Length);
            Add("null-objects", file.NullObjects.Length);
            foreach (var animation in file.Animations)
            {
                AuditAnimation(assetName, file, animation);
            }

            if (file.Hlods.Length > 0 || (file.Meshes.Length > 0 && file.Hierarchies.Length == 0))
            {
                AuditModel(assetName, file);
            }
        }

        void AuditModel(string assetName, W3dAssetFile file)
        {
            W3dModel model;
            try
            {
                model = W3dModel.Create(assetName, file, library.GetSkeleton);
            }
            catch (W3dMissingAssetException exception)
            {
                unresolvedHierarchies.Add($"{assetName}: model hierarchy '{exception.AssetName}'");
                return;
            }

            Increment(file.Hlods.Length > 0 ? "models.hlod" : "models.bare-mesh");
            Increment($"models.hierarchy-source.{model.HierarchySource}");
            Add("hlods.lod-arrays", file.Hlods.Sum(hlod => hlod.Lods.Length));
            Add("hlods.aggregates", model.Aggregates.Length);
            Add("hlods.proxies", model.Proxies.Length);
            Add("model.sub-objects", model.SubObjects.Length);
            Add("model.sub-objects.renderable", model.SubObjects.Count(subObject => subObject.IsRenderable));
            Add("model.sub-objects.boxes", model.SubObjects.Count(subObject => subObject.Kind == W3dSubObjectKind.Box));
            Add("model.sub-objects.skinned", model.SubObjects.Count(subObject => subObject.IsSkinned));
            Add("model.sub-objects.hidden-by-default", model.SubObjects.Count(subObject => subObject.IsRenderable && !subObject.IsVisibleByDefault));
            Add("model.unreferenced-meshes", model.UnreferencedMeshes.Length);
            Add("model.unresolved-sub-objects", model.UnresolvedSubObjects.Length);
            foreach (var entry in model.UnresolvedSubObjects)
            {
                unresolvedSubObjects.Add($"{assetName}: '{entry.Name}' on bone {entry.BoneIndex}");
            }

            var pose = model.CreatePose();
            pose.SetRestPose(Matrix4x4.Identity);
            CheckPose($"{assetName}: rest pose", pose);
            foreach (var subObject in model.SubObjects.Where(subObject => subObject.IsSkinned))
            {
                var positions = new Vector3[subObject.Mesh!.Vertices.Count];
                var normals = new Vector3[positions.Length];
                W3dModel.SkinVertices(subObject.Mesh, pose.BoneWorldTransforms, positions, normals);
                if (positions.Any(position => !W3dBinary.IsFinite(position)))
                {
                    quaternionFindings.Add($"{assetName}: skin '{subObject.Name}' produces non-finite rest-pose vertices");
                }
            }
        }

        void AuditAnimation(string assetName, W3dAssetFile file, W3dAnimation animation)
        {
            Increment($"animations.{animation.Compression}");
            Add("animations.frames", animation.FrameCount);
            Add("animations.surplus-channel-bytes", animation.SurplusChannelBytes);
            foreach (var channel in animation.Channels)
            {
                Increment($"channels.{animation.Compression}.{channel.Type}");
                if (channel.Pivot == 0)
                {
                    Increment("channels.root-pivot-ignored");
                }

                CheckChannel(assetName, animation, channel);
            }

            foreach (var channel in animation.VisibilityChannels)
            {
                Increment(channel is W3dTimeCodedVisibilityChannel ? "channels.visibility.TimeCoded" : "channels.visibility.None");
            }

            var skeleton = library.ResolveAnimationSkeleton(file, animation);
            if (skeleton is null)
            {
                unresolvedHierarchies.Add($"{assetName}: animation '{animation.FullName}' hierarchy '{animation.HierarchyName}'");
                return;
            }

            var binding = animation.Bind(skeleton.BoneCount);
            Add("channels.discarded-beyond-hierarchy", binding.DiscardedChannels.Length);
            Add("channels.euler-ignored", binding.IgnoredEulerChannels);
            var pose = new W3dPose(skeleton);
            foreach (var frame in new[] { 0f, (animation.FrameCount - 1) * 0.5f, animation.FrameCount - 1 })
            {
                pose.Evaluate(binding, frame, Matrix4x4.Identity);
                CheckPose($"{assetName}: animation '{animation.FullName}' frame {frame}", pose);
            }

            Increment("animations.bound");
        }

        void CheckChannel(string assetName, W3dAnimation animation, W3dMotionChannel channel)
        {
            var values = channel.Values;
            var context = $"{assetName}: animation '{animation.FullName}' {channel.Type} channel of pivot {channel.Pivot}";
            if (channel.Type != W3dChannelType.Quaternion)
            {
                if (values.Any(value => !float.IsFinite(value)))
                {
                    quaternionFindings.Add($"{context} has non-finite values");
                }

                return;
            }

            var nonUnit = 0;
            for (var offset = 0; offset < values.Length; offset += 4)
            {
                var quaternion = new Quaternion(values[offset], values[offset + 1], values[offset + 2], values[offset + 3]);
                if (!W3dBinary.IsFinite(quaternion) || MathF.Abs(quaternion.Length() - 1) > UnitTolerance)
                {
                    nonUnit++;
                }
            }

            Add("channels.quaternion-samples", values.Length / 4);
            if (nonUnit > 0)
            {
                Add("channels.quaternion-samples.non-unit", nonUnit);
                quaternionFindings.Add($"{context} has {nonUnit} non-unit or non-finite quaternions");
            }
        }

        void CheckQuaternion(string context, Quaternion quaternion)
        {
            if (!W3dBinary.IsFinite(quaternion) || MathF.Abs(quaternion.Length() - 1) > UnitTolerance)
            {
                quaternionFindings.Add($"{context} rotation {quaternion} is not a unit quaternion");
            }
        }

        void CheckPose(string context, W3dPose pose)
        {
            foreach (var matrix in pose.BoneWorldTransforms)
            {
                if (!IsFinite(matrix))
                {
                    quaternionFindings.Add($"{context} yields non-finite bone matrices");
                    return;
                }
            }
        }

        void Increment(string key) => Add(key, 1);

        void Add(string key, long value) => counts[key] = counts.GetValueOrDefault(key) + value;
    }

    private static bool IsFinite(Matrix4x4 matrix)
        => float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14) &&
           float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24) &&
           float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34) &&
           float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);

    private static ImmutableArray<string> Sorted(List<string> items) => [.. items.Order(StringComparer.Ordinal)];
}
