using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using CnCNew.W3D;

internal static class SkeletonTests
{
    private const float Tolerance = 1e-4f;

    public static void RunSynthetic()
    {
        HierarchyDecoding();
        HlodAndBoxDecoding();
        RawAnimationPose();
        TimeCodedAnimation();
        AdaptiveDeltaAnimation();
        AnimationClock();
        ModelAssemblyBoneLookupAndSkinning();
        ProceduralOverrides();
        StrictnessFailures();
    }

    // ---------------------------------------------------------------- synthetic fixtures

    private static void HierarchyDecoding()
    {
        var file = Decode(Hierarchy("TESTSKL",
            ("ROOTTRANSFORM", -1, Vector3.Zero, Quaternion.Identity),
            ("BONE1", 0, new Vector3(1, 2, 3), Quaternion.Identity),
            ("BONE2", 1, new Vector3(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2))));
        var hierarchy = file.Hierarchies.Single();
        Check(hierarchy.Name == "TESTSKL" && hierarchy.Pivots.Length == 3, "Hierarchy header/pivot count mismatch.");
        Check(hierarchy.Pivots[2].ParentIndex == 1 && hierarchy.Pivots[0].ParentIndex == -1, "Pivot parents mismatch.");
        Check(hierarchy.Pivots[1].Translation == new Vector3(1, 2, 3), "Pivot translation mismatch.");
        Check(hierarchy.PivotFixups.Length == 3 && hierarchy.PivotFixups[1].M41 == 1, "Pivot fixups were not decoded as 4x3 rows.");

        var skeleton = W3dSkeleton.FromHierarchy(hierarchy);
        Check(skeleton.FindBone("bone2") == 2 && skeleton.FindBone("missing") == -1, "Case-insensitive bone lookup failed.");
        // Bone2's rest world position is bone1's (1,2,3) plus (10,0,0); its own rotation does not move its origin.
        Near(skeleton.RestWorldTransforms[2].Translation, new Vector3(11, 2, 3), "Rest world transform mismatch.");
        Near(Vector3.Transform(Vector3.UnitX, skeleton.RestWorldTransforms[2]), new Vector3(11, 3, 3),
            "Pivot rotation must rotate child-space points about +Z.");
    }

    private static void HlodAndBoxDecoding()
    {
        var file = Decode(Concat(
            Hlod("MODEL", "MODELSKL", [(1, "MODEL.MESH"), (2, "MODEL.BOX")], proxies: [(2, "PROXY01")]),
            Box("MODEL.BOX", attributes: 0x11, new Vector3(1, 2, 3), new Vector3(4, 5, 6))));
        var hlod = file.Hlods.Single();
        Check(hlod.Name == "MODEL" && hlod.HierarchyName == "MODELSKL", "HLOD header mismatch.");
        Check(hlod.Lods.Single().SubObjects.Length == 2 && hlod.Lods[0].SubObjects[1] == new W3dHlodSubObject(2, "MODEL.BOX"),
            "HLOD sub-objects mismatch.");
        Check(hlod.Lods[0].MaxScreenSize == 1000, "HLOD max screen size mismatch.");
        Check(hlod.Proxies.Single() == new W3dHlodSubObject(2, "PROXY01") && hlod.Aggregates.IsEmpty, "HLOD proxy array mismatch.");
        var box = file.Boxes.Single();
        Check(box.IsOriented && box.CollisionTypes == 1 && box.Center == new Vector3(1, 2, 3) && box.Extent == new Vector3(4, 5, 6),
            "Box decoding mismatch.");
    }

    private static void RawAnimationPose()
    {
        var skeleton = TwoBoneChain();
        // Frame 0 identity, frame 1: 90 degrees about Z on BONE1; BONE1 X translation 0 -> 2; BONE2 visible only at frame 1.
        var ninety = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        var animation = Decode(RawAnimation("ANIM", "CHAIN", frames: 2,
        [
            RawChannel(pivot: 1, W3dChannelType.Quaternion, 0, [0, 0, 0, 1, ninety.X, ninety.Y, ninety.Z, ninety.W]),
            RawChannel(pivot: 1, W3dChannelType.X, 0, [0, 2], surplusFloats: 2),
            RawChannel(pivot: 7, W3dChannelType.Z, 0, [5, 5]),
            BitChannel(pivot: 2, firstFrame: 1, lastFrame: 1, defaultValue: false, 0b1),
        ])).Animations.Single();
        Check(animation.FullName == "CHAIN.ANIM" && animation.Compression == W3dAnimationCompression.None, "Raw animation header mismatch.");
        Check(animation.SurplusChannelBytes == 8, "Surplus channel bytes were not counted.");
        var binding = animation.Bind(skeleton.BoneCount);
        Check(binding.DiscardedChannels.SequenceEqual(["Z@7"]), "Channels beyond the hierarchy must be discarded and listed.");

        var pose = new W3dPose(skeleton);
        pose.Evaluate(binding, 0, Matrix4x4.Identity);
        Near(pose.GetBoneWorldTransform(2).Translation, new Vector3(10, 0, 0), "Frame 0 pose mismatch.");
        Check(!pose.IsBoneVisible(2) && pose.IsBoneVisible(1), "Bit channel default outside its range mismatch.");

        pose.Evaluate(binding, 1, Matrix4x4.Identity);
        Near(pose.GetBoneWorldTransform(1).Translation, new Vector3(2, 0, 0), "Translation channel mismatch.");
        Near(pose.GetBoneWorldTransform(2).Translation, new Vector3(2, 10, 0), "Rotation channel must swing the child to +Y.");
        Check(pose.IsBoneVisible(2), "Bit channel frame 1 must be visible.");

        pose.Evaluate(binding, 0.5f, Matrix4x4.Identity);
        var half = 10 * MathF.Sqrt(0.5f);
        Near(pose.GetBoneWorldTransform(2).Translation, new Vector3(1 + half, half, 0), "Slerp/lerp at frame 0.5 mismatch.");

        // Past the last frame the second sample wraps to frame 0 (HRawAnimClass::Get_Translation).
        pose.Evaluate(binding, 1.5f, Matrix4x4.Identity);
        Near(pose.GetBoneWorldTransform(1).Translation, new Vector3(1, 0, 0), "Raw interpolation must wrap to frame 0.");

        pose.Evaluate(binding, 0.6f, Matrix4x4.Identity, W3dRawSampling.Nearest);
        Near(pose.GetBoneWorldTransform(2).Translation, new Vector3(2, 10, 0), "Nearest raw sampling must round 0.6 to frame 1.");

        var root = Matrix4x4.CreateTranslation(100, 0, 0);
        pose.Evaluate(binding, 1, root);
        Near(pose.GetBoneWorldTransform(2).Translation, new Vector3(102, 10, 0), "Root transform must prefix the chain.");

        pose.SetRestPose(Matrix4x4.Identity);
        Near(pose.GetBoneWorldTransform(2).Translation, new Vector3(10, 0, 0), "Rest pose mismatch.");
        Throws<ArgumentOutOfRangeException>(() => pose.Evaluate(binding, 2, Matrix4x4.Identity), "Frames beyond the animation must fail.");
    }

    private static void TimeCodedAnimation()
    {
        var ninety = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        const uint binary = W3dTimeCodedMotionChannel.BinaryMovementFlag;
        var animation = Decode(CompressedAnimation("TC", "CHAIN", frames: 10, flavor: 0,
        [
            TimeCodedChannel(1, W3dChannelType.X, [(0, [0]), (4, [8]), (6 | binary, [20])]),
            TimeCodedChannel(1, W3dChannelType.Quaternion, [(0, [0, 0, 0, 1]), (4, [ninety.X, ninety.Y, ninety.Z, ninety.W])]),
            TimeCodedBitChannel(2, [0, 3 | W3dTimeCodedVisibilityChannel.StateMask, 7]),
        ])).Animations.Single();
        Check(animation.Compression == W3dAnimationCompression.TimeCoded, "Time-coded flavor mismatch.");
        var x = (W3dTimeCodedMotionChannel)animation.Channels[0];
        Near(x.GetScalar(2), 4, "Time-coded linear interpolation mismatch.");
        Near(x.GetScalar(5.5f), 8, "A binary-movement key must hold the previous value until it is reached.");
        Near(x.GetScalar(6), 20, "The binary-movement key must apply at its frame.");
        Near(x.GetScalar(9), 20, "The last key must hold to the end.");

        var pose = new W3dPose(TwoBoneChain());
        var binding = animation.Bind(3);
        pose.Evaluate(binding, 2, Matrix4x4.Identity);
        var half = 10 * MathF.Sqrt(0.5f);
        Near(pose.GetBoneWorldTransform(2).Translation, new Vector3(4 + half, half, 0), "Time-coded pose mismatch.");
        Check(!pose.IsBoneVisible(2), "Time-coded bit before the first set key must be off.");
        pose.Evaluate(binding, 3, Matrix4x4.Identity);
        Check(pose.IsBoneVisible(2), "Time-coded bit key 3 must switch visibility on.");
        pose.Evaluate(binding, 8, Matrix4x4.Identity);
        Check(!pose.IsBoneVisible(2), "Time-coded bit key 7 must switch visibility off.");
    }

    private static void AdaptiveDeltaAnimation()
    {
        Near(W3dAdaptiveDeltaMotionChannel.GetFilter(8), 1.0f, "Filter table power-of-ten entry mismatch.");
        Near(W3dAdaptiveDeltaMotionChannel.GetFilter(16), 1.0f, "Filter table generated entry 0 must be 1 - sin(0).");
        Near(W3dAdaptiveDeltaMotionChannel.GetFilter(136), 1 - MathF.Sin(MathF.PI / 4), "Filter table 45-degree entry mismatch.");

        // 20 frames = frame 0 plus 19 deltas in two 16-frame groups; X uses filter 8 (1.0) with scale 0.5.
        var deltas = Enumerable.Range(1, 19).Select(frame => (sbyte)(frame % 2 == 0 ? -3 : 4)).ToArray();
        var expected = new float[20];
        expected[0] = 1.5f;
        for (var frame = 1; frame < 20; frame++)
        {
            expected[frame] = expected[frame - 1] + deltas[frame - 1] * 0.5f;
        }

        var animation = Decode(CompressedAnimation("AD", "CHAIN", frames: 20, flavor: 1,
        [
            AdaptiveDeltaChannel(1, W3dChannelType.X, 20, 0.5f, [1.5f], [deltas], filterIndex: 8),
            AdaptiveDeltaChannel(1, W3dChannelType.Quaternion, 20, 1.0f, [0, 0, 0, 1],
                [new sbyte[19], new sbyte[19], new sbyte[19], new sbyte[19]], filterIndex: 16),
        ])).Animations.Single();
        var x = (W3dAdaptiveDeltaMotionChannel)animation.Channels[0];
        Check(animation.Compression == W3dAnimationCompression.AdaptiveDelta, "Adaptive-delta flavor mismatch.");
        for (var frame = 0; frame < 20; frame++)
        {
            Near(x.Values[frame], expected[frame], $"Adaptive-delta round trip mismatch at frame {frame}.");
        }

        Near(x.GetScalar(16.5f), (expected[16] + expected[17]) / 2, "Adaptive-delta interpolation across the group boundary mismatch.");
        Near(x.GetScalar(19), expected[19], "Adaptive-delta last frame must clamp its successor.");
        var binding = animation.Bind(3);
        var pose = new W3dPose(TwoBoneChain());
        pose.Evaluate(binding, 3, Matrix4x4.Identity);
        Near(pose.GetBoneWorldTransform(1).Translation, new Vector3(expected[3], 0, 0), "Adaptive-delta pose mismatch.");
    }

    private static void AnimationClock()
    {
        // 11 frames at 10 fps: the loop period is 10 frames (FrameCount - 1), as in Compute_Current_Frame.
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.Loop, 11, 10, 1.25), 2.5f, "Loop wrap mismatch.");
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.Once, 11, 10, 3), 10, "Once must clamp to the last frame.");
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.OnceBackwards, 11, 10, 0.3), 7, "Once-backwards mismatch.");
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.OnceBackwards, 11, 10, 5), 0, "Once-backwards must clamp at 0.");
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.LoopBackwards, 11, 10, 1.3), 7, "Loop-backwards mismatch.");
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.LoopPingPong, 11, 10, 1.3), 7, "Ping-pong return leg mismatch.");
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.LoopPingPong, 11, 10, 2.4), 4, "Ping-pong second cycle mismatch.");
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.Loop, 11, 10, 1, speedMultiplier: 0.5), 5, "Speed multiplier mismatch.");
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.Manual, 11, 10, 99, manualFrame: 3), 3, "Manual frame mismatch.");
        Near(W3dAnimationClock.GetFrame(W3dAnimationMode.Loop, 1, 30, 5), 0, "Single-frame animations stay at frame 0.");
        Check(W3dAnimationClock.IsComplete(W3dAnimationMode.Once, 11, 10) && !W3dAnimationClock.IsComplete(W3dAnimationMode.Loop, 11, 10),
            "Completion rule mismatch.");
        Throws<ArgumentOutOfRangeException>(() => W3dAnimationClock.GetFrame(W3dAnimationMode.Loop, 11, 10, -1), "Negative time must fail.");
    }

    private static void ModelAssemblyBoneLookupAndSkinning()
    {
        var skeletonFile = Hierarchy("TANKSKL",
            ("ROOTTRANSFORM", -1, Vector3.Zero, Quaternion.Identity),
            ("TURRET", 0, new Vector3(0, 0, 2), Quaternion.Identity),
            ("TURRETEL", 1, new Vector3(1, 0, 0), Quaternion.Identity),
            ("MUZZLE01", 2, new Vector3(3, 0, 0), Quaternion.Identity),
            ("MUZZLE02", 2, new Vector3(3, 1, 0), Quaternion.Identity),
            ("MUZZLE04", 2, new Vector3(3, 2, 0), Quaternion.Identity),
            ("WEAPONA", 1, new Vector3(0, 0, 1), Quaternion.Identity));
        var modelFile = Concat(
            Mesh("TANK", "HULL", skinBones: null, attributes: 0x00008000),
            Mesh("TANK", "SKIN", skinBones: [1, 3, 0], attributes: 0x00020000),
            Mesh("TANK", "FLASH", skinBones: null, attributes: 0x00001000),
            Mesh("TANK", "FXBONE01", skinBones: null, attributes: 0),
            Box("TANK.BOUNDINGBOX", 0x1, Vector3.Zero, Vector3.One),
            Hlod("TANK", "TANKSKL",
                [(0, "TANK.HULL"), (0, "TANK.SKIN"), (3, "TANK.FLASH"), (6, "TANK.FXBONE01"), (0, "TANK.BOUNDINGBOX"), (1, "TANK.SPLINE")]));
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Art/W3D/TANKSKL.W3D"] = skeletonFile,
            ["Art/W3D/TANK.W3D"] = modelFile,
        };
        var requests = new List<string>();
        var library = new W3dAssetLibrary(path =>
        {
            requests.Add(path);
            return files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes, writable: false) : null;
        });
        var model = library.GetModel("TANK");
        Check(ReferenceEquals(model, library.GetModel("tank")), "Models must be cached case-insensitively.");
        Check(model.HierarchySource == W3dHierarchySource.External && model.Skeleton.BoneCount == 7, "External hierarchy resolution failed.");
        Check(requests.Count(path => path.Equals("Art/W3D/TANKSKL.W3D", StringComparison.Ordinal)) == 1, "Skeleton must be read once.");
        Check(model.SubObjects.Length == 5 && model.UnresolvedSubObjects.Single().Name == "TANK.SPLINE",
            "Unresolved HLOD entries must be listed, not dropped silently.");
        var hull = model.SubObjects.Single(subObject => subObject.Name == "TANK.HULL");
        var flash = model.SubObjects.Single(subObject => subObject.Name == "TANK.FLASH");
        var box = model.SubObjects.Single(subObject => subObject.Kind == W3dSubObjectKind.Box);
        var skin = model.SubObjects.Single(subObject => subObject.IsSkinned);
        Check(hull.IsRenderable && hull.IsVisibleByDefault && hull.CastsShadow, "Rigid mesh flags mismatch.");
        Check(flash.IsRenderable && !flash.IsVisibleByDefault, "Hidden meshes must start invisible.");
        Check(!box.IsRenderable && box.IsCollisionOnly, "Boxes must be collision-only.");

        Check(model.FindBones("Muzzle").Select(match => match.Name).SequenceEqual(["Muzzle01", "Muzzle02"]),
            "Numbered bone lookup must stop at the first gap.");
        Check(model.FindBones("TURRET").Single() == new W3dBoneMatch("TURRET", 1, false), "Unadorned bone lookup mismatch.");
        Check(model.FindBones("TurretEL").Single().BoneIndex == 2, "Turret pitch bone lookup mismatch.");
        Check(model.FindBones("WEAPONA").Single().BoneIndex == 6, "Weapon bone lookup mismatch.");
        Check(model.FindBones("FXBONE").Single() == new W3dBoneMatch("FXBONE01", 6, true), "Sub-object fallback lookup mismatch.");
        Check(model.FindBones("ROOTTRANSFORM").IsEmpty, "Bone 0 counts as not found, as in W3DModelDraw.");
        Check(model.FindBones("NOPE").IsEmpty, "Missing bones must yield no match.");

        var pose = model.CreatePose();
        pose.SetTurretYaw(1, MathF.PI / 2);
        pose.SetRestPose(Matrix4x4.CreateTranslation(0, 0, 10));
        var positions = new Vector3[3];
        var normals = new Vector3[3];
        W3dModel.SkinVertices(skin.Mesh!, pose.BoneWorldTransforms, positions, normals);
        // Vertex 0 (origin) on TURRET at (0,0,12); vertex 1 (1,0,0) on MUZZLE01, which sits at (4,0,0) in the turned turret
        // space, so (5,0,0) turned by 90 degrees; vertex 2 (1,0,0) on the root.
        Near(positions[0], new Vector3(0, 0, 12), "Skinned vertex on the turret bone mismatch.");
        Near(positions[1], new Vector3(0, 5, 12), "Skinned vertex on a child bone mismatch.");
        Near(positions[2], new Vector3(1, 0, 10), "Skinned vertex on the root mismatch.");
        Near(normals[0], new Vector3(0, 1, 0), "Skinned normals must rotate without translation.");

        files.Remove("Art/W3D/TANKSKL.W3D");
        var fresh = new W3dAssetLibrary(path => files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null);
        Throws<W3dMissingAssetException>(() => fresh.GetModel("TANK"), "Missing hierarchies must fail explicitly.");
        Throws<W3dMissingAssetException>(() => fresh.GetModel("GHOST"), "Missing model files must fail explicitly.");
        Check(fresh.GetAnimation("TANKSKL.NOPE") is null, "Missing animations must resolve to null.");
    }

    private static void ProceduralOverrides()
    {
        var skeleton = TwoBoneChain();
        var pose = new W3dPose(skeleton);
        pose.SetTurretYaw(1, MathF.PI / 2);
        pose.SetRestPose(Matrix4x4.Identity);
        Near(pose.GetBoneWorldTransform(2).Translation, new Vector3(0, 10, 0), "Turret yaw must rotate children about +Z.");
        pose.SetTurretPitch(1, MathF.PI / 2);
        pose.SetRestPose(Matrix4x4.Identity);
        Near(pose.GetBoneWorldTransform(2).Translation, new Vector3(0, 0, 10), "Positive turret pitch must raise the barrel (Rotate_Y(-pitch)).");
        pose.SetRecoil(2, 1.5f);
        pose.SetRestPose(Matrix4x4.Identity);
        Near(Vector3.Transform(Vector3.Zero, pose.GetBoneWorldTransform(2)), new Vector3(0, 0, 8.5f),
            "Recoil must shift the bone along its own -X.");

        var animation = Decode(RawAnimation("HIDE", "CHAIN", frames: 1,
            [BitChannel(pivot: 1, firstFrame: 0, lastFrame: 0, defaultValue: false, 0)])).Animations.Single();
        pose.ClearBoneOverrides();
        pose.Evaluate(animation.Bind(3), 0, Matrix4x4.Identity);
        Check(!pose.IsBoneVisible(1), "Visibility channel must hide the bone.");
        pose.SetTurretYaw(1, 0);
        pose.Evaluate(animation.Bind(3), 0, Matrix4x4.Identity);
        Check(pose.IsBoneVisible(1), "Captured bones are forced visible (HTreeClass::Anim_Update).");
        Throws<ArgumentOutOfRangeException>(() => pose.SetTurretYaw(0, 1), "The root bone cannot be captured.");
    }

    private static void StrictnessFailures()
    {
        Fails(Hierarchy("BAD", ("ROOT", -1, Vector3.Zero, Quaternion.Identity), ("CHILD", 2, Vector3.Zero, Quaternion.Identity),
            ("LATE", 0, Vector3.Zero, Quaternion.Identity)), "Parents after their children must fail.");
        Fails(Container(W3dChunkType.Hierarchy, HierarchyHeader("OLD", 1, version: 0x00020000)), "Pre-3.0 hierarchies must fail.");
        Fails(Container(W3dChunkType.Hierarchy, HierarchyHeader("NOPIV", 1), Chunk(0x0199, [1])), "Unknown hierarchy children must fail.");
        Fails(RawAnimation("A", "S", 2, [RawChannel(1, W3dChannelType.Quaternion, 0, [0, 0, 0, 1], lastFrameOverride: 1)]),
            "Truncated channels must fail.");
        Fails(RawAnimation("A", "S", 2, [RawChannel(1, (W3dChannelType)9, 0, [0, 1])]), "Unsupported channel types must fail.");
        Fails(RawAnimation("A", "S", 2, [RawChannel(1, W3dChannelType.X, 1, [0, 1])]), "Channels past the last frame must fail.");
        Fails(CompressedAnimation("A", "S", 5, 0, [TimeCodedChannel(1, W3dChannelType.X, [(1, [0]), (2, [1])])]),
            "Time codes not starting at frame 0 must fail.");
        Fails(CompressedAnimation("A", "S", 5, 2, []), "Unknown compression flavors must fail.");
        Fails(Concat(Hlod("M", "S", [(0, "M.A")]), Chunk(0x0999, [0])), "Unknown top-level chunks must fail.");
        Fails(Mesh("C", "BADSKIN", skinBones: [0, 0, 0], attributes: 0x00020000, influencePadding: 1),
            "Non-zero influence padding (multi-bone skins) must fail.");
        var duplicate = Decode(RawAnimation("A", "S", 2,
        [
            RawChannel(1, W3dChannelType.X, 0, [0, 1]),
            RawChannel(1, W3dChannelType.X, 0, [0, 1]),
        ])).Animations.Single();
        Throws<InvalidDataException>(() => duplicate.Bind(2), "Duplicate channels for one pivot must fail.");
        var emitter = Decode(Concat(Container(W3dChunkType.Emitter, Chunk(0x501, [0])), Mesh("C", "M", null, 0)));
        Check(emitter.IgnoredChunks.Single().Kind == "emitter", "Known unused chunks must be listed as ignored.");
    }

    // ---------------------------------------------------------------- real data

    public static void RunRealData(string contentDirectory)
    {
        var directory = Path.Combine(contentDirectory, "Art", "W3D");
        var library = new W3dAssetLibrary(path =>
        {
            var physical = Path.Combine(contentDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(physical) ? File.OpenRead(physical) : null;
        });
        var names = Directory.EnumerateFiles(directory, "*.w3d")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToArray();
        var stopwatch = Stopwatch.StartNew();
        var decodeTime = new Stopwatch();
        var report = W3dSkeletonAudit.Run(names, library, decodeTime);
        stopwatch.Stop();
        Check(report.Failures.IsEmpty, "Real W3D decode failures:\n" + string.Join('\n', report.Failures.Take(20)));
        Check(report.FileCount == 8894 && report["hierarchies"] == 6478 && report["models.hlod"] == 6502,
            $"Real W3D inventory changed ({report.FileCount} files, {report["hierarchies"]} hierarchies, {report["models.hlod"]} HLODs).");
        Check(report["animations.None"] == 4074 && report["animations.TimeCoded"] == 21 && report["animations.AdaptiveDelta"] == 0,
            "Real animation counts changed.");
        Check(report["meshes.skinned"] == 169 && report["model.unresolved-sub-objects"] == 45,
            "Real skin or unresolved sub-object counts changed.");
        Check(report.UnresolvedHierarchyReferences.Length == 6 &&
              report.UnresolvedHierarchyReferences.All(entry => entry.Contains("GENERALSKEL", StringComparison.Ordinal)),
            "Only the six GENERALSKEL animations may lack their hierarchy.");
        Check(report.QuaternionFindings.IsEmpty, "Unexpected quaternion/NaN findings:\n" + string.Join('\n', report.QuaternionFindings.Take(10)));
        Console.WriteLine(
            $"W3D skeleton audit: {report.FileCount} files decoded in {decodeTime.Elapsed.TotalSeconds:F2} s, " +
            $"full audit {stopwatch.Elapsed.TotalSeconds:F2} s.");

        var crusader = library.GetModel("AVCrusader");
        var turret = crusader.FindBone("TURRET");
        var barrel = crusader.FindBone("BARREL01");
        Check(crusader.HierarchySource == W3dHierarchySource.OwnFile && turret == 2 && barrel == 3, "AVCrusader bones changed.");
        Check(crusader.FindBones("TurretFX").Single().Name == "TurretFX01", "AVCrusader TurretFX01 lookup failed.");
        var crusaderPose = crusader.CreatePose();
        Near(crusaderPose.GetBoneWorldTransform(turret).Translation, new Vector3(-1.1874434f, 0, 7.2349358f), "AVCrusader turret rest position changed.", 1e-3f);
        var barrelRest = crusaderPose.GetBoneWorldTransform(barrel).Translation;
        crusaderPose.SetTurretYaw(turret, MathF.PI / 2);
        crusaderPose.SetRestPose(Matrix4x4.Identity);
        var barrelTurned = crusaderPose.GetBoneWorldTransform(barrel).Translation;
        var turretPosition = crusaderPose.GetBoneWorldTransform(turret).Translation;
        Near(barrelTurned - turretPosition,
            Vector3.Transform(barrelRest - turretPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)),
            "AVCrusader barrel must orbit the turret pivot.", 1e-3f);
        Check(crusader.SubObjects.Any(subObject => subObject.BoneIndex == turret && subObject.IsRenderable), "AVCrusader turret mesh missing.");

        var command = library.GetModel("ABArFrcCmd");
        var commandAnimation = library.GetAnimation("ABARFRCCMD.ABARFRCCMD")
            ?? throw new InvalidOperationException("ABArFrcCmd embedded animation missing.");
        var commandPose = command.CreatePose();
        commandPose.Evaluate(commandAnimation, commandAnimation.Animation.FrameCount / 2f, Matrix4x4.Identity);
        Check(command.SubObjects.Length > 0, "ABArFrcCmd sub-objects missing.");

        var ranger = library.GetModel("AIRngr_SKN");
        var idle = library.GetAnimation("AIRngr_SKL.AIRngr_STA") ?? throw new InvalidOperationException("AIRngr_STA missing.");
        Check(ranger.SubObjects.Any(subObject => subObject.IsSkinned), "AIRngr_SKN has no skin.");
        Check(idle.Animation.Compression == W3dAnimationCompression.None && idle.DiscardedChannels.IsEmpty, "AIRngr_STA binding changed.");
        var rangerPose = ranger.CreatePose();
        var skin = ranger.SubObjects.First(subObject => subObject.IsSkinned).Mesh!;
        var restPositions = new Vector3[skin.Vertices.Count];
        var animatedPositions = new Vector3[skin.Vertices.Count];
        W3dModel.SkinVertices(skin, rangerPose.BoneWorldTransforms, restPositions, []);
        rangerPose.Evaluate(idle, idle.Animation.FrameCount / 2f, Matrix4x4.Identity);
        W3dModel.SkinVertices(skin, rangerPose.BoneWorldTransforms, animatedPositions, []);
        Check(restPositions.Zip(animatedPositions).Any(pair => Vector3.Distance(pair.First, pair.Second) > 0.01f),
            "AIRngr_STA must move skin vertices.");

        var planted = library.GetAnimation("AIOFCR_SKL.AICIAOFF_PLNT");
        Check(planted is { Animation.Compression: W3dAnimationCompression.TimeCoded }, "AICIAOFF_PLNT must be a time-coded animation.");
    }

    // ---------------------------------------------------------------- builders

    private static W3dSkeleton TwoBoneChain()
        => W3dSkeleton.FromHierarchy(Decode(Hierarchy("CHAIN",
            ("ROOTTRANSFORM", -1, Vector3.Zero, Quaternion.Identity),
            ("BONE1", 0, Vector3.Zero, Quaternion.Identity),
            ("BONE2", 1, new Vector3(10, 0, 0), Quaternion.Identity))).Hierarchies.Single());

    private static byte[] Hierarchy(string name, params (string Name, int Parent, Vector3 Translation, Quaternion Rotation)[] pivots)
    {
        var pivotBytes = new byte[pivots.Length * 60];
        var fixupBytes = new byte[pivots.Length * 48];
        for (var index = 0; index < pivots.Length; index++)
        {
            var pivot = pivotBytes.AsSpan(index * 60, 60);
            Name(pivot, pivots[index].Name, 16);
            BinaryPrimitives.WriteUInt32LittleEndian(pivot[16..], pivots[index].Parent < 0 ? uint.MaxValue : (uint)pivots[index].Parent);
            WriteVector(pivot, 20, pivots[index].Translation);
            WriteFloats(pivot, 44, pivots[index].Rotation.X, pivots[index].Rotation.Y, pivots[index].Rotation.Z, pivots[index].Rotation.W);
            var fixup = fixupBytes.AsSpan(index * 48, 48);
            WriteFloats(fixup, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, index, 0, 0);
        }

        return Container(W3dChunkType.Hierarchy,
            HierarchyHeader(name, pivots.Length),
            Chunk(W3dChunkType.Pivots, pivotBytes),
            Chunk(W3dChunkType.PivotFixups, fixupBytes));
    }

    private static byte[] HierarchyHeader(string name, int pivots, uint version = 0x00040001)
    {
        var header = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(header, version);
        Name(header.AsSpan(4), name, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)pivots);
        return Chunk(W3dChunkType.HierarchyHeader, header);
    }

    private static byte[] Hlod(string name, string hierarchy, (int Bone, string Name)[] subObjects, (int Bone, string Name)[]? proxies = null)
    {
        var header = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x00010000);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        Name(header.AsSpan(8), name, 16);
        Name(header.AsSpan(24), hierarchy, 16);
        var parts = new List<byte[]> { Chunk(W3dChunkType.HlodHeader, header), Array(W3dChunkType.HlodLodArray, subObjects) };
        if (proxies is not null)
        {
            parts.Add(Array(W3dChunkType.HlodProxyArray, proxies));
        }

        return Container(W3dChunkType.Hlod, [.. parts]);

        static byte[] Array(uint type, (int Bone, string Name)[] entries)
        {
            var arrayHeader = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(arrayHeader, (uint)entries.Length);
            WriteFloats(arrayHeader, 4, 1000);
            var children = new List<byte[]> { Chunk(W3dChunkType.HlodSubObjectArrayHeader, arrayHeader) };
            foreach (var entry in entries)
            {
                var payload = new byte[36];
                BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)entry.Bone);
                Name(payload.AsSpan(4), entry.Name, 32);
                children.Add(Chunk(W3dChunkType.HlodSubObject, payload));
            }

            return Container(type, [.. children]);
        }
    }

    private static byte[] Box(string name, uint attributes, Vector3 center, Vector3 extent)
    {
        var payload = new byte[68];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x00010000);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), attributes);
        Name(payload.AsSpan(8), name, 32);
        WriteVector(payload, 44, center);
        WriteVector(payload, 56, extent);
        return Chunk(W3dChunkType.Box, payload);
    }

    private static byte[] Mesh(string container, string name, ushort[]? skinBones, uint attributes, byte influencePadding = 0)
    {
        var header = new byte[116];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x00040002);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), attributes);
        Name(header.AsSpan(8), name, 16);
        Name(header.AsSpan(24), container, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), 3);
        WriteVector(header, 88, Vector3.One);
        var vertices = new byte[36];
        WriteVector(vertices, 0, Vector3.Zero);
        WriteVector(vertices, 12, new Vector3(1, 0, 0));
        WriteVector(vertices, 24, new Vector3(1, 0, 0));
        var normals = new byte[36];
        WriteVector(normals, 0, Vector3.UnitX);
        WriteVector(normals, 12, Vector3.UnitX);
        WriteVector(normals, 24, Vector3.UnitX);
        var parts = new List<byte[]>
        {
            Chunk(W3dChunkType.MeshHeader3, header),
            Chunk(W3dChunkType.Vertices, vertices),
            Chunk(W3dChunkType.VertexNormals, normals),
        };
        if (skinBones is not null)
        {
            var influences = new byte[skinBones.Length * 8];
            for (var index = 0; index < skinBones.Length; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(influences.AsSpan(index * 8), skinBones[index]);
                influences[index * 8 + 2] = influencePadding;
            }

            parts.Add(Chunk(W3dChunkType.VertexInfluences, influences));
        }

        return Container(W3dChunkType.Mesh, [.. parts]);
    }

    private static byte[] RawAnimation(string name, string hierarchy, int frames, byte[][] channels)
        => Container(W3dChunkType.Animation, [AnimationHeader(W3dChunkType.AnimationHeader, name, hierarchy, frames, 0x00040001, 30, null), .. channels]);

    private static byte[] CompressedAnimation(string name, string hierarchy, int frames, ushort flavor, byte[][] channels)
        => Container(W3dChunkType.CompressedAnimation,
            [AnimationHeader(W3dChunkType.CompressedAnimationHeader, name, hierarchy, frames, 0x00000001, 30, flavor), .. channels]);

    private static byte[] AnimationHeader(uint type, string name, string hierarchy, int frames, uint version, int frameRate, ushort? flavor)
    {
        var header = new byte[44];
        BinaryPrimitives.WriteUInt32LittleEndian(header, version);
        Name(header.AsSpan(4), name, 16);
        Name(header.AsSpan(20), hierarchy, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(36), (uint)frames);
        if (flavor is { } value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(40), (ushort)frameRate);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(42), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), (uint)frameRate);
        }

        return Chunk(type, header);
    }

    private static byte[] RawChannel(
        int pivot,
        W3dChannelType type,
        int firstFrame,
        float[] values,
        int surplusFloats = 0,
        int? lastFrameOverride = null)
    {
        var vectorLength = type == W3dChannelType.Quaternion ? 4 : 1;
        var payload = new byte[12 + (values.Length + surplusFloats) * 4];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)firstFrame);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2),
            (ushort)(lastFrameOverride ?? firstFrame + values.Length / vectorLength - 1));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)vectorLength);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)type);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), (ushort)pivot);
        WriteFloats(payload, 12, values);
        return Chunk(W3dChunkType.AnimationChannel, payload);
    }

    private static byte[] BitChannel(int pivot, int firstFrame, int lastFrame, bool defaultValue, params byte[] bits)
    {
        var payload = new byte[9 + bits.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)firstFrame);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)lastFrame);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)pivot);
        payload[8] = defaultValue ? (byte)1 : (byte)0;
        bits.CopyTo(payload, 9);
        return Chunk(W3dChunkType.BitChannel, payload);
    }

    private static byte[] TimeCodedChannel(int pivot, W3dChannelType type, (uint TimeCode, float[] Values)[] keys)
    {
        var vectorLength = type == W3dChannelType.Quaternion ? 4 : 1;
        var payload = new byte[8 + keys.Length * (vectorLength + 1) * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)keys.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)pivot);
        payload[6] = (byte)vectorLength;
        payload[7] = (byte)type;
        for (var key = 0; key < keys.Length; key++)
        {
            var offset = 8 + key * (vectorLength + 1) * 4;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), keys[key].TimeCode);
            WriteFloats(payload, offset + 4, keys[key].Values);
        }

        return Chunk(W3dChunkType.CompressedAnimationChannel, payload);
    }

    private static byte[] TimeCodedBitChannel(int pivot, uint[] keys)
    {
        var payload = new byte[8 + keys.Length * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)keys.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)pivot);
        for (var index = 0; index < keys.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8 + index * 4), keys[index]);
        }

        return Chunk(W3dChunkType.CompressedBitChannel, payload);
    }

    /// <summary>Test-side encoder: per component, 16-frame groups of 9-byte packets (filter index + 16 nibbles).</summary>
    private static byte[] AdaptiveDeltaChannel(
        int pivot,
        W3dChannelType type,
        int frames,
        float scale,
        float[] initial,
        sbyte[][] deltasPerComponent,
        byte filterIndex)
    {
        var vectorLength = initial.Length;
        var groups = (frames - 1 + 15) / 16;
        var payload = new byte[12 + vectorLength * 4 + groups * 9 * vectorLength];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)frames);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)pivot);
        payload[6] = (byte)vectorLength;
        payload[7] = (byte)type;
        WriteFloats(payload, 8, scale);
        WriteFloats(payload, 12, initial);
        for (var group = 0; group < groups; group++)
        {
            for (var component = 0; component < vectorLength; component++)
            {
                var packet = 12 + vectorLength * 4 + (group * vectorLength + component) * 9;
                payload[packet] = filterIndex;
                for (var nibble = 0; nibble < 16; nibble++)
                {
                    var frame = group * 16 + nibble;
                    if (frame >= frames - 1)
                    {
                        break;
                    }

                    var value = deltasPerComponent[component][frame] & 0xf;
                    payload[packet + 1 + nibble / 2] |= (byte)(nibble % 2 == 0 ? value : value << 4);
                }
            }
        }

        return Chunk(W3dChunkType.CompressedAnimationChannel, payload);
    }

    private static byte[] Chunk(uint type, byte[] payload)
    {
        var bytes = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, type);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] Container(uint type, params byte[][] children)
    {
        var payload = Concat(children);
        var bytes = Chunk(type, payload);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length | 0x80000000u);
        return bytes;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    private static W3dAssetFile Decode(byte[] bytes) => W3dAssetFile.Read(new MemoryStream(bytes, writable: false));

    private static void Fails(byte[] bytes, string message) => Throws<InvalidDataException>(() => Decode(bytes), message);

    private static void Name(Span<byte> target, string name, int length)
    {
        var encoded = Encoding.ASCII.GetBytes(name);
        Check(encoded.Length < length, "Fixture name too long.");
        encoded.CopyTo(target);
    }

    private static void WriteVector(Span<byte> target, int offset, Vector3 value) => WriteFloats(target, offset, value.X, value.Y, value.Z);

    private static void WriteFloats(Span<byte> target, int offset, params float[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(target[(offset + index * 4)..], values[index]);
        }
    }

    // ---------------------------------------------------------------- assertions

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Near(float actual, float expected, string message)
    {
        if (MathF.Abs(actual - expected) > Tolerance)
        {
            throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }

    private static void Near(Vector3 actual, Vector3 expected, string message, float tolerance = Tolerance)
    {
        if (Vector3.Distance(actual, expected) > tolerance)
        {
            throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
