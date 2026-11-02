using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.W3D;

namespace CnCNew.Game.Data;

/// <summary>
/// Reads the airfield bones the simulation needs (<see cref="ModelGeometry"/>) from the base-pose skeletons of the airfields' draw models. The simulation may not open model files, so this is where
/// <c>ParkingPlaceBehavior::buildInfo</c>'s <c>getSingleLogicalBonePosition</c> calls are answered: for every object with a <c>ParkingPlaceBehavior</c> the default condition state's model is loaded
/// and the bones <c>Runway{c}Parking{r}</c>, <c>Runway{c}Park{r}Han</c>, <c>Runway{c}Prep{r}</c>, <c>RunwayStart{c}</c>, <c>RunwayEnd{c}</c> (and <c>HeliPark01</c> when present) are read from the rest pose with an
/// identity root (the "pristine" bones of <c>W3DModelDraw</c>). A missing model or bone is an error, never a default. Coordinates are rounded to 1/16 unit and headings to 1/1024 radian.
/// <para>
/// The table is part of the world fingerprint, so it has to be the same on every machine. It is therefore not read from the skeleton's single-precision matrices
/// (products that depend on the vector instructions and on fused multiply-add of the processor, and an arc tangent of the C runtime) but computed from the
/// pivots' numbers as the file states them, without rounding (<see cref="ExactTransform"/>), with a whole-number arc tangent (<see cref="ExactBoneMath"/>).
/// </para>
/// </summary>
public static class ModelGeometryLoader
{
    /// <summary>Coordinates are multiples of 2^-4 = 1/16 unit, headings of 2^-10 = 1/1024 radian.</summary>
    private const int PositionBits = 4;
    private const int HeadingBits = 10;

    public static ModelGeometry Load(CompiledDefinitionGraph graph, ObjectVisualCatalog visuals, ContentFileSystem content)
    {
        var library = new W3dAssetLibrary(path => content.Exists(path) ? content.OpenRead(path) : null);
        var layouts = new SortedDictionary<string, AirfieldLayout>(StringComparer.Ordinal);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>().OrderBy(value => value.OriginalName, StringComparer.Ordinal))
        {
            var module = definition.Modules.FirstOrDefault(value => value.Name.Equals("ParkingPlaceBehavior", StringComparison.Ordinal));
            if (module is null) continue;
            var rows = Count(definition, module, "NumRows");
            var cols = Count(definition, module, "NumCols");
            var model = ModelOf(definition, visuals);
            layouts[definition.OriginalName] = ReadLayout(library, definition.OriginalName, model, rows, cols);
        }

        return new ModelGeometry(layouts, ReadExitPaths(graph, visuals, library), ReadFirePoints(graph, visuals, library), ReadBridgeDecks(graph, library), ReadWeaponLaunches(graph, visuals, library), ReadRopes(graph, visuals, library));
    }

    /// <summary>The fraction bits of a launch bone's direction.</summary>
    private const int DirectionBits = 16;

    private static readonly ModelConditionSet[] FiringConditions = [ModelConditionSet.Parse("FIRING_A"), ModelConditionSet.Parse("FIRING_B"), ModelConditionSet.Parse("FIRING_C")];

    /// <summary>
    /// The launch points of every object one of whose weapons launches a projectile (<c>ModelConditionInfo::validateWeaponBarrelInfo</c>, <c>W3DModelDraw::getProjectileLaunchOffset</c>), for
    /// the state the object shows when the slot fires: the first draw module that has barrels for the slot gives them. The pristine bones of a state are those of the first or, with
    /// <c>PRISTINE_BONE_POS_IN_FINAL_FRAME</c>, the last frame of its first animation (<c>validateCachedBones</c>), times the template's <c>Scale</c>.
    /// </summary>
    private static SortedDictionary<string, WeaponLaunchPoints> ReadWeaponLaunches(CompiledDefinitionGraph graph, ObjectVisualCatalog visuals, W3dAssetLibrary library)
    {
        var launchers = graph.Definitions.OfType<WeaponDefinition>()
            .Where(weapon => weapon.Fields.Any(field => field.Name.Equals("ProjectileObject", StringComparison.OrdinalIgnoreCase)))
            .Select(weapon => weapon.OriginalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var points = new SortedDictionary<string, WeaponLaunchPoints>(StringComparer.Ordinal);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>().OrderBy(value => value.OriginalName, StringComparer.Ordinal))
        {
            if (!FiresProjectiles(definition, launchers) || !visuals.TryGet(definition.OriginalName, out var visual)) continue;
            var scale = Fixed.FromDecimal(ScaleOf(definition));
            var slots = ImmutableArray.CreateBuilder<LaunchSlot>(FiringConditions.Length);
            for (var slot = 0; slot < FiringConditions.Length; slot++) slots.Add(ReadLaunchSlot(definition, visual, library, slot, scale));
            if (slots.Any(slot => !slot.Barrels.IsEmpty)) points[definition.OriginalName] = new WeaponLaunchPoints(slots.MoveToImmutable());
        }

        return points;
    }

    private static bool FiresProjectiles(ObjectDefinition definition, HashSet<string> launchers)
    {
        foreach (var section in definition.Sections)
        {
            if (!section.Declaration.Equals("WeaponSet", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var field in section.Fields)
                if (field.Name.Equals("Weapon", StringComparison.OrdinalIgnoreCase)
                    && field.Value.OriginalText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries) is [_, var weapon, ..] && launchers.Contains(weapon)) return true;
        }

        return false;
    }

    private static LaunchSlot ReadLaunchSlot(ObjectDefinition definition, ObjectVisual visual, W3dAssetLibrary library, int slot, Fixed scale)
    {
        foreach (var draw in visual.Draws)
        {
            if (BestStateSelector.FindBest(draw, FiringConditions[slot]) is not (var index and >= 0) || index >= draw.States.Length) continue;
            var state = draw.States[index];
            if (state.Model is not { } model) continue;
            var bones = slot < state.WeaponBones.Length ? state.WeaponBones[slot] : WeaponSlotBones.None;
            if (bones is { LaunchBone: null, FireFxBone: null, RecoilBone: null, MuzzleFlash: null }) continue;
            W3dModel loaded;
            try
            {
                loaded = library.GetModel(model);
            }
            catch (W3dMissingAssetException exception)
            {
                throw new InvalidDataException($"{definition.OriginalName}: model '{model}' cannot be loaded: {exception.Message}");
            }

            var skeleton = loaded.Skeleton;
            var pose = new StatePose(skeleton, FirstAnimation(state, skeleton, library), state.Flags.HasFlag(AnimationFlags.PristineBonePosInFinalFrame));
            var barrels = ReadBarrels(bones, skeleton, pose, scale);
            if (barrels.IsEmpty) continue;
            return new LaunchSlot(barrels, [ReadLaunchTurret(state.Turret, skeleton, pose, scale), ReadLaunchTurret(state.AltTurret, skeleton, pose, scale)]);
        }

        return new LaunchSlot([], []);
    }

    /// <summary>The state's first animation, if it can be had; a state without one keeps the rest pose (the original's model shows its current animation or none).</summary>
    private static W3dAnimationBinding? FirstAnimation(ModelStateSpec state, W3dSkeleton skeleton, W3dAssetLibrary library)
    {
        if (state.Animations.IsDefaultOrEmpty || !state.Animations[0].Name.Contains('.', StringComparison.Ordinal)) return null;
        try
        {
            return library.GetAnimation(state.Animations[0].Name) is { } binding && binding.PivotCount <= skeleton.BoneCount ? binding : null;
        }
        catch (Exception exception) when (exception is W3dMissingAssetException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>validateWeaponBarrelInfo</c>: barrels are <c>NAME01</c>, <c>NAME02</c>, ... while any of the four bones of a number exists; a number without a launch bone is the identity;
    /// without any numbered barrel the plain names give one.
    /// </summary>
    private static ImmutableArray<LaunchBarrel> ReadBarrels(WeaponSlotBones bones, W3dSkeleton skeleton, StatePose pose, Fixed scale)
    {
        bool Has(string? name, string suffix) => name is not null && skeleton.FindBone(name + suffix) > 0;
        LaunchBarrel Barrel(string suffix) => Has(bones.LaunchBone, suffix) ? pose.Barrel(skeleton.FindBone(bones.LaunchBone + suffix), scale) : LaunchBarrel.Origin;
        var barrels = ImmutableArray.CreateBuilder<LaunchBarrel>();
        for (var number = 1; number <= 99; number++)
        {
            var suffix = number.ToString("00", CultureInfo.InvariantCulture);
            if (!Has(bones.FireFxBone, suffix) && !Has(bones.RecoilBone, suffix) && !Has(bones.MuzzleFlash, suffix) && !Has(bones.LaunchBone, suffix)) break;
            barrels.Add(Barrel(suffix));
        }

        if (barrels.Count == 0 && (Has(bones.FireFxBone, string.Empty) || Has(bones.RecoilBone, string.Empty) || Has(bones.MuzzleFlash, string.Empty) || Has(bones.LaunchBone, string.Empty)))
            barrels.Add(Barrel(string.Empty));
        return barrels.ToImmutable();
    }

    private static LaunchTurret ReadLaunchTurret(TurretSpec turret, W3dSkeleton skeleton, StatePose pose, Fixed scale)
    {
        (Fixed X, Fixed Y, Fixed Z) Pivot(string? bone) => bone is not null && skeleton.FindBone(bone) is var index and > 0 ? pose.Point(index, scale) : default;
        var (angle, pitch) = (Pivot(turret.AngleBone), Pivot(turret.PitchBone));
        return new LaunchTurret(angle.X, angle.Y, angle.Z, pitch.X, pitch.Y, pitch.Z, Radians(turret.ArtAngleDegrees), Radians(turret.ArtPitchDegrees));
    }

    private static Fixed Radians(decimal degrees) => FixedTrig.PiFraction(1, 180) * Fixed.FromDecimal(degrees);

    /// <summary>The bones of a state: its model's rest pose under the first or last frame of an animation, computed exactly as the rest pose is (<see cref="BoneTransform"/>).</summary>
    private sealed class StatePose(W3dSkeleton skeleton, W3dAnimationBinding? animation, bool finalFrame)
    {
        private readonly int _frame = animation is null || !finalFrame ? 0 : animation.Animation.FrameCount - 1;

        private ExactTransform Transform(int bone)
        {
            var world = ExactTransform.Identity;
            for (var index = bone; index > 0;)
            {
                if (animation is not null && index < animation.PivotCount)
                {
                    animation.SampleStored(index, _frame, out var translation, out var rotation);
                    world = world.Then(ExactTransform.FromPivot(Exact(rotation.X), Exact(rotation.Y), Exact(rotation.Z), Exact(rotation.W), Exact(translation.X), Exact(translation.Y), Exact(translation.Z)));
                }

                var pivot = skeleton.RestPivots[index];
                world = world.Then(ExactTransform.FromPivot(Exact(pivot.Rotation.X), Exact(pivot.Rotation.Y), Exact(pivot.Rotation.Z), Exact(pivot.Rotation.W),
                    Exact(pivot.Translation.X), Exact(pivot.Translation.Y), Exact(pivot.Translation.Z)));
                var parent = skeleton.Parents[index];
                if (parent < 0 || parent >= index) throw new InvalidDataException($"bone {index} has the parent {parent}; a parent must precede its children.");
                index = parent;
            }

            return world;
        }

        public (Fixed X, Fixed Y, Fixed Z) Point(int bone, Fixed scale)
        {
            var world = Transform(bone);
            return (Step(world.X.RoundToStep(PositionBits), PositionBits) * scale, Step(world.Y.RoundToStep(PositionBits), PositionBits) * scale, Step(world.Z.RoundToStep(PositionBits), PositionBits) * scale);
        }

        public LaunchBarrel Barrel(int bone, Fixed scale)
        {
            var (x, y, z) = Point(bone, scale);
            var world = Transform(bone);
            return new LaunchBarrel(x, y, z, Step(world.M11.RoundToStep(DirectionBits), DirectionBits), Step(world.M12.RoundToStep(DirectionBits), DirectionBits), Step(world.M13.RoundToStep(DirectionBits), DirectionBits));
        }

        private static Fixed Step(long count, int bits) => Fixed.FromDecimal(count / new decimal(1L << bits));
    }

    /// <summary>The fraction bits the lateral extent of a bridge model is kept with.</summary>
    private const int BridgeExtentBits = 16;

    /// <summary>
    /// The width of the deck of every <c>Bridge</c> block (<c>W3DBridge::load</c>): the extent across the bridge of the vertices of the pristine model's <c>BRIDGE_LEFT</c> section at its rest
    /// pose ("we assume all sections are the same width"). A block whose model is not in the content, or has no such section, gets no entry: the original then makes no bridge of it.
    /// </summary>
    private static SortedDictionary<string, BridgeDeck> ReadBridgeDecks(CompiledDefinitionGraph graph, W3dAssetLibrary library)
    {
        var decks = new SortedDictionary<string, BridgeDeck>(StringComparer.Ordinal);
        foreach (var definition in graph.Definitions.OfType<BridgeDefinition>().OrderBy(value => value.OriginalName, StringComparer.Ordinal))
        {
            if (definition.Fields.LastOrDefault(value => value.Name.Equals("BridgeModelName", StringComparison.OrdinalIgnoreCase))?.Value.Value is not string { Length: > 0 } name) continue;
            W3dModel model;
            try
            {
                model = library.GetModel(name);
            }
            catch (W3dMissingAssetException)
            {
                continue;
            }
            // Create_Render_Obj takes the last sub-object whose name begins so.
            var left = BridgeSection.Find(model, ".BRIDGE_LEFT");
            if (left?.Mesh is not { } mesh) continue;
            var transform = BoneTransform(model.Skeleton, left.BoneIndex);
            var (lowest, highest) = (long.MaxValue, long.MinValue);
            foreach (var vertex in mesh.Vertices)
            {
                var (x, y, z) = (Exact(vertex.X), Exact(vertex.Y), Exact(vertex.Z));
                var across = (x * transform.M12 + y * transform.M22 + z * transform.M32 + transform.Y).RoundToStep(BridgeExtentBits);
                lowest = Math.Min(lowest, across);
                highest = Math.Max(highest, across);
            }
            if (lowest > highest) continue;
            decks[definition.OriginalName] = new BridgeDeck(Fixed.FromDecimal(lowest / new decimal(1L << BridgeExtentBits)), Fixed.FromDecimal(highest / new decimal(1L << BridgeExtentBits)));
        }
        return decks;
    }

    /// <summary>The bone names <c>GameData.ini</c>'s <c>StandardPublicBone</c> makes public for every model that matter here; any other name must be an <c>ExtraPublicBone</c> of the draw module.</summary>
    private static readonly string[] StandardPublicBones = ["FirePoint"];

    /// <summary>The containers whose riders <c>OpenContain::redeployOccupants</c> puts at <c>FIREPOINTnn</c> (the Helix puts them above itself, <c>HelixContain::redeployOccupants</c>).</summary>
    private static readonly string[] FirePointTransports = ["TransportContain", "OverlordContain"];

    private static readonly ModelConditionSet[] GarrisonConditions =
        [ModelConditionSet.Parse("GARRISONED"), ModelConditionSet.Parse("GARRISONED DAMAGED"), ModelConditionSet.Parse("GARRISONED REALLYDAMAGED")];

    /// <summary>
    /// The rider points of every garrison and fire-point transport (<see cref="ContainerFirePoints"/>): for each condition the original asks for, the draw modules in order give the bones
    /// <c>PREFIX01</c>, <c>PREFIX02</c>, ... of their best state until one is missing (<c>W3DModelDraw::getPristineBonePositionsForConditionState</c>), summed up to the limit
    /// (<c>Drawable::getPristineBonePositions</c>); a bone that is not public is not found. The pristine bones are taken with the template's <c>Scale</c> as root (<c>validateCachedBones</c>).
    /// </summary>
    private static SortedDictionary<string, ContainerFirePoints> ReadFirePoints(CompiledDefinitionGraph graph, ObjectVisualCatalog visuals, W3dAssetLibrary library)
    {
        var points = new SortedDictionary<string, ContainerFirePoints>(StringComparer.Ordinal);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>().OrderBy(value => value.OriginalName, StringComparer.Ordinal))
        {
            var garrison = definition.Modules.FirstOrDefault(value => value.Name.Equals("GarrisonContain", StringComparison.Ordinal));
            var transport = definition.Modules.FirstOrDefault(value => FirePointTransports.Contains(value.Name, StringComparer.Ordinal));
            if ((garrison is null && transport is null) || !visuals.TryGet(definition.OriginalName, out var visual)) continue;
            var scale = Fixed.FromDecimal(ScaleOf(definition));
            ImmutableArray<BonePose> Read(ModelConditionSet conditions, string prefix, int max)
            {
                var poses = ImmutableArray.CreateBuilder<BonePose>();
                foreach (var draw in visual.Draws)
                {
                    if (poses.Count >= max) break;
                    if (BestStateSelector.FindBest(draw, conditions) is not (var state and >= 0) || state >= draw.States.Length || draw.States[state].Model is not { } model) continue;
                    W3dSkeleton skeleton;
                    try
                    {
                        skeleton = library.GetModel(model).Skeleton;
                    }
                    catch (W3dMissingAssetException exception)
                    {
                        throw new InvalidDataException($"{definition.OriginalName}: model '{model}' cannot be loaded: {exception.Message}");
                    }
                    for (var number = 1; number <= 99 && poses.Count < max; number++)
                    {
                        var name = prefix + number.ToString("00", CultureInfo.InvariantCulture);
                        if (!IsPublic(draw, name) || skeleton.FindBone(name) is not (var bone and >= 0)) break;
                        var pose = RestPose(skeleton, bone);
                        poses.Add(pose with { X = pose.X * scale, Y = pose.Y * scale, Z = pose.Z * scale });
                    }
                }
                return poses.ToImmutable();
            }

            var sets = ImmutableArray<ImmutableArray<BonePose>>.Empty;
            var stations = ImmutableArray<BonePose>.Empty;
            if (garrison is not null)
            {
                if (Flag(definition, garrison, "IsEnclosingContainer", defaultValue: true))
                    sets = [.. GarrisonConditions.Select(conditions => Read(conditions, "FIREPOINT", ContainerFirePoints.MaxGarrisonPoints))];
                else
                    stations = Read(GarrisonConditions[0], "STATION", Math.Min(Count(definition, garrison, "ContainMax"), ContainerFirePoints.MaxGarrisonPoints));
            }
            var riders = transport is null ? ImmutableArray<BonePose>.Empty : Read(ModelConditionSet.Empty, "FIREPOINT", ContainerFirePoints.MaxTransportPoints);
            // PassengersInTurret: the points turn with the turret about its bone (getSingleLogicalBonePositionOnTurret, the turret's launch offset of the default state).
            BonePose? pivot = null;
            if (transport is not null && !riders.IsEmpty && Flag(definition, transport, "PassengersInTurret", defaultValue: false))
                pivot = TurretPivot(definition, visual, library, scale) ?? throw new InvalidDataException($"{definition.OriginalName}: PassengersInTurret without a turret bone in its model.");
            points[definition.OriginalName] = new ContainerFirePoints(sets, stations, riders, pivot);
        }

        return points;
    }

    /// <summary>The rest pose of the main turret's angle bone of the first draw module that names one in its default state, scaled as the pristine bones are.</summary>
    private static BonePose? TurretPivot(ObjectDefinition definition, ObjectVisual visual, W3dAssetLibrary library, Fixed scale)
    {
        foreach (var draw in visual.Draws)
        {
            if (BestStateSelector.FindBest(draw, ModelConditionSet.Empty) is not (var state and >= 0) || state >= draw.States.Length) continue;
            if (draw.States[state] is not { Model: { } model, Turret.AngleBone: { } bone }) continue;
            var skeleton = library.GetModel(model).Skeleton;
            if (skeleton.FindBone(bone) is not (var index and >= 0)) throw new InvalidDataException($"{definition.OriginalName}: turret bone '{bone}' is not in model '{model}'.");
            var pose = RestPose(skeleton, index);
            return pose with { X = pose.X * scale, Y = pose.Y * scale, Z = pose.Z * scale };
        }
        return null;
    }

    /// <summary><c>doSingleBoneName</c>: a public name makes itself and its numbered variations public.</summary>
    private static bool IsPublic(DrawSpec draw, string bone)
    {
        static bool Covers(string name, string bone)
            => bone.Equals(name, StringComparison.OrdinalIgnoreCase)
                || (bone.Length == name.Length + 2 && bone.StartsWith(name, StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(bone[^2]) && char.IsAsciiDigit(bone[^1]));
        return StandardPublicBones.Any(name => Covers(name, bone)) || draw.ExtraPublicBones.Any(name => Covers(name.Trim(), bone));
    }

    private static decimal ScaleOf(ObjectDefinition definition)
    {
        var field = definition.Fields.LastOrDefault(value => value.Name.Equals("Scale", StringComparison.OrdinalIgnoreCase));
        if (field is null) return 1m;
        return decimal.TryParse(field.Value.OriginalText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0
            ? scale
            : throw new InvalidDataException($"{definition.OriginalName}: Scale is not a positive number ('{field.Value.OriginalText.Trim()}').");
    }

    private static bool Flag(ObjectDefinition definition, ModuleConfiguration module, string key, bool defaultValue)
    {
        var field = module.Fields.LastOrDefault(value => value.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (field is null) return defaultValue;
        var text = field.Value.OriginalText.Trim();
        if (text.Equals("Yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
        throw new InvalidDataException($"{definition.OriginalName}: {module.Name} {key} is not Yes or No ('{text}').");
    }

    /// <summary>The contain modules whose riders leave through <c>OpenContain::exitObjectViaDoor</c> (the garrison, the tunnel, the ferry and the parachute have exits of their own).</summary>
    private static readonly string[] DoorContainers = ["TransportContain", "HelixContain", "OverlordContain", "RiderChangeContain", "InternetHackContain"];

    /// <summary>
    /// The exit bones of every object with a door container: <c>ExitStart</c>/<c>ExitEnd</c> for one path, <c>ExitStart01</c>..<c>ExitEndNN</c> for more (<c>OpenContain::exitObjectViaDoor</c>), read
    /// from the model of the state without any condition. An object without a model, or a model without the bone, gets no pose for it: the original then uses the
    /// object's position (<c>Object::getSingleLogicalBonePosition</c> returns it when the bone is not found). A model that is named and cannot be loaded is an error.
    /// </summary>
    private static SortedDictionary<string, ImmutableArray<ExitPath>> ReadExitPaths(CompiledDefinitionGraph graph, ObjectVisualCatalog visuals, W3dAssetLibrary library)
    {
        var exits = new SortedDictionary<string, ImmutableArray<ExitPath>>(StringComparer.Ordinal);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>().OrderBy(value => value.OriginalName, StringComparer.Ordinal))
        {
            var module = definition.Modules.FirstOrDefault(value => DoorContainers.Contains(value.Name, StringComparer.Ordinal));
            if (module is null) continue;
            var count = module.Fields.Any(value => value.Name.Equals("NumberOfExitPaths", StringComparison.OrdinalIgnoreCase)) ? Count(definition, module, "NumberOfExitPaths") : 1;
            if (count == 0) continue;
            W3dSkeleton? skeleton = null;
            if (visuals.TryGet(definition.OriginalName, out var visual))
                foreach (var draw in visual.Draws)
                    // The state a drawable without any condition shows (W3DModelDrawModuleData::findBestInfo of the empty set): the pristine bones of an undamaged, idle object.
                    if (BestStateSelector.FindBest(draw, ModelConditionSet.Empty) is var state and >= 0 && state < draw.States.Length && draw.States[state].Model is { } model)
                    {
                        try
                        {
                            skeleton = library.GetModel(model).Skeleton;
                        }
                        catch (W3dMissingAssetException exception)
                        {
                            throw new InvalidDataException($"{definition.OriginalName}: model '{model}' cannot be loaded: {exception.Message}");
                        }
                        break;
                    }
            BonePose? Bone(string name) => skeleton is not null && skeleton.FindBone(name) is var index and >= 0 ? RestPose(skeleton, index) : null;
            var paths = ImmutableArray.CreateBuilder<ExitPath>(count);
            for (var path = 1; path <= count; path++)
            {
                var suffix = count > 1 ? path.ToString("00", CultureInfo.InvariantCulture) : string.Empty;
                paths.Add(new ExitPath(Bone("ExitStart" + suffix), Bone("ExitEnd" + suffix)));
            }
            exits[definition.OriginalName] = paths.MoveToImmutable();
        }

        return exits;
    }

    /// <summary>The bones <c>RopeStartNN</c> and <c>RopeEndNN</c> of every object with a <c>ChinookAIUpdate</c> (<c>getPristineBonePositions("RopeStart", 1, ..)</c>): consecutive from 01 until the first missing.</summary>
    private static SortedDictionary<string, ImmutableArray<ExitPath>> ReadRopes(CompiledDefinitionGraph graph, ObjectVisualCatalog visuals, W3dAssetLibrary library)
    {
        var ropes = new SortedDictionary<string, ImmutableArray<ExitPath>>(StringComparer.Ordinal);
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>().OrderBy(value => value.OriginalName, StringComparer.Ordinal))
        {
            if (!definition.Modules.Any(value => value.Name == "ChinookAIUpdate") || !visuals.TryGet(definition.OriginalName, out var visual)) continue;
            foreach (var draw in visual.Draws)
            {
                if (BestStateSelector.FindBest(draw, ModelConditionSet.Empty) is not (var state and >= 0) || state >= draw.States.Length || draw.States[state].Model is not { } model) continue;
                var skeleton = library.GetModel(model).Skeleton;
                if (skeleton is null) continue;
                var pairs = ImmutableArray.CreateBuilder<ExitPath>();
                for (var index = 1; ; index++)
                {
                    var suffix = index.ToString("00", CultureInfo.InvariantCulture);
                    if (skeleton.FindBone("RopeStart" + suffix) is not (var start and >= 0) || skeleton.FindBone("RopeEnd" + suffix) is not (var end and >= 0)) break;
                    pairs.Add(new ExitPath(RestPose(skeleton, start), RestPose(skeleton, end)));
                }
                if (pairs.Count > 0) ropes[definition.OriginalName] = pairs.ToImmutable();
                break;
            }
        }
        return ropes;
    }

    private static int Count(ObjectDefinition definition, ModuleConfiguration module, string key)
    {
        var field = module.Fields.LastOrDefault(value => value.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (field is null) return 0;
        return int.TryParse(field.Value.OriginalText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 0
            ? count
            : throw new InvalidDataException($"{definition.OriginalName}: {module.Name} {key} is not a count ('{field.Value.OriginalText.Trim()}').");
    }

    private static string ModelOf(ObjectDefinition definition, ObjectVisualCatalog visuals)
    {
        if (!visuals.TryGet(definition.OriginalName, out var visual)) throw new InvalidDataException($"{definition.OriginalName}: the airfield has no visual.");
        foreach (var draw in visual.Draws)
            if (draw.DefaultStateIndex >= 0 && draw.DefaultStateIndex < draw.States.Length && draw.States[draw.DefaultStateIndex].Model is { } model)
                return model;
        throw new InvalidDataException($"{definition.OriginalName}: the airfield's default condition state names no model, so its parking and runway bones are unknown.");
    }

    private static AirfieldLayout ReadLayout(W3dAssetLibrary library, string template, string modelName, int rows, int cols)
    {
        W3dSkeleton skeleton;
        try
        {
            skeleton = library.GetModel(modelName).Skeleton;
        }
        catch (W3dMissingAssetException exception)
        {
            throw new InvalidDataException($"{template}: model '{modelName}' cannot be loaded: {exception.Message}");
        }

        BonePose Bone(string name)
        {
            var index = skeleton.FindBone(name);
            if (index < 0) throw new InvalidDataException($"{template}: model '{modelName}' has no bone '{name}'.");
            try
            {
                return RestPose(skeleton, index);
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException($"{template}: model '{modelName}', bone '{name}': {exception.Message}");
            }
        }

        var parking = ImmutableArray.CreateBuilder<BonePose>(rows * cols);
        var hangar = ImmutableArray.CreateBuilder<BonePose>(rows * cols);
        var prep = ImmutableArray.CreateBuilder<BonePose>(rows * cols);
        for (var row = 0; row < rows; row++)
            for (var col = 0; col < cols; col++)
            {
                parking.Add(Bone($"Runway{col + 1}Parking{row + 1}"));
                hangar.Add(Bone($"Runway{col + 1}Park{row + 1}Han"));
                prep.Add(Bone($"Runway{col + 1}Prep{row + 1}"));
            }

        var start = ImmutableArray.CreateBuilder<BonePose>(cols);
        var end = ImmutableArray.CreateBuilder<BonePose>(cols);
        for (var col = 0; col < cols; col++)
        {
            start.Add(Bone($"RunwayStart{col + 1}"));
            end.Add(Bone($"RunwayEnd{col + 1}"));
        }

        BonePose? pad = skeleton.FindBone("HeliPark01") >= 0 ? Bone("HeliPark01") : null;
        return new AirfieldLayout(rows, cols, parking.MoveToImmutable(), hangar.MoveToImmutable(), prep.MoveToImmutable(), start.MoveToImmutable(), end.MoveToImmutable(), pad);
    }

    /// <summary>
    /// A bone's rest pose in model space with an identity root: position and heading (the angle of the bone's x axis in the ground plane,
    /// <c>atan2(M12, M11)</c>), rounded to the table's resolution. Computed exactly from the pivots (<c>world = local * parentWorld</c>, bone 0 is the identity).
    /// </summary>
    public static BonePose RestPose(W3dSkeleton skeleton, int bone)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        if (bone < 0 || bone >= skeleton.BoneCount) throw new ArgumentOutOfRangeException(nameof(bone));
        var world = BoneTransform(skeleton, bone);
        return new BonePose(Step(world.X.RoundToStep(PositionBits), PositionBits), Step(world.Y.RoundToStep(PositionBits), PositionBits), Step(world.Z.RoundToStep(PositionBits), PositionBits),
            Step(ExactBoneMath.Atan2ToStep(world.M12, world.M11, HeadingBits), HeadingBits));

        static Fixed Step(long count, int bits) => Fixed.FromDecimal(count / new decimal(1L << bits));
    }

    /// <summary>The one place a single of the model file enters the exact domain: only its bits are read.</summary>
    private static Dyadic Exact(float component) => Dyadic.FromSingleBits(BitConverter.SingleToInt32Bits(component));

    private static ExactTransform BoneTransform(W3dSkeleton skeleton, int bone)
    {
        var world = ExactTransform.Identity;
        for (var index = bone; index > 0;)
        {
            var pivot = skeleton.RestPivots[index];
            world = world.Then(ExactTransform.FromPivot(
                Exact(pivot.Rotation.X), Exact(pivot.Rotation.Y), Exact(pivot.Rotation.Z), Exact(pivot.Rotation.W),
                Exact(pivot.Translation.X), Exact(pivot.Translation.Y), Exact(pivot.Translation.Z)));
            var parent = skeleton.Parents[index];
            if (parent < 0 || parent >= index) throw new InvalidDataException($"bone {index} has the parent {parent}; a parent must precede its children.");
            index = parent;
        }
        return world;
    }

    /// <summary>The angle of the vector (x, y), both given as single-precision bits, in steps of 1/1024 radian: the arc tangent of the table, exposed for its test.</summary>
    public static long HeadingSteps(int yBits, int xBits) => ExactBoneMath.Atan2ToStep(Dyadic.FromSingleBits(yBits), Dyadic.FromSingleBits(xBits), HeadingBits);
}

/// <summary>The sections of a bridge model (<c>W3DBridge::load</c>).</summary>
internal static class BridgeSection
{
    /// <summary>The sub-object <c>Create_Render_Obj</c> gets: of those whose name begins with the model's name and <paramref name="suffix"/>, the last one, of any level of detail.</summary>
    public static W3dSubObject? Find(W3dModel model, string suffix)
    {
        W3dSubObject? found = null;
        foreach (var subObject in model.SubObjects)
            if (subObject.Name.StartsWith(model.Name + suffix, StringComparison.OrdinalIgnoreCase)) found = subObject;
        return found;
    }
}
