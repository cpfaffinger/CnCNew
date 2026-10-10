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

        return new ModelGeometry(layouts, ReadExitPaths(graph, visuals, library));
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

        return new BonePose(Step(world.X.RoundToStep(PositionBits), PositionBits), Step(world.Y.RoundToStep(PositionBits), PositionBits), Step(world.Z.RoundToStep(PositionBits), PositionBits),
            Step(ExactBoneMath.Atan2ToStep(world.M12, world.M11, HeadingBits), HeadingBits));

        // The one place a single of the model file enters the exact domain: only its bits are read.
        static Dyadic Exact(float component) => Dyadic.FromSingleBits(BitConverter.SingleToInt32Bits(component));
        static Fixed Step(long count, int bits) => Fixed.FromDecimal(count / new decimal(1L << bits));
    }

    /// <summary>The angle of the vector (x, y), both given as single-precision bits, in steps of 1/1024 radian: the arc tangent of the table, exposed for its test.</summary>
    public static long HeadingSteps(int yBits, int xBits) => ExactBoneMath.Atan2ToStep(Dyadic.FromSingleBits(yBits), Dyadic.FromSingleBits(xBits), HeadingBits);
}
