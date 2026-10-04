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
/// identity root (the "pristine" bones of <c>W3DModelDraw</c>). A missing model or bone is an error, never a default. Coordinates are rounded to 1/16 unit and headings to 1/1024 radian, so a
/// last-bit difference of the single-precision matrix products cannot change the table (and with it the world fingerprint) from one machine to the next.
/// </summary>
public static class ModelGeometryLoader
{
    private const decimal PositionStep = 16m;
    private const decimal HeadingStep = 1024m;

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

        return new ModelGeometry(layouts);
    }

    private static int Count(ObjectDefinition definition, ModuleConfiguration module, string key)
    {
        var field = module.Fields.LastOrDefault(value => value.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (field is null) return 0;
        return int.TryParse(field.Value.OriginalText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count >= 0
            ? count
            : throw new InvalidDataException($"{definition.OriginalName}: ParkingPlaceBehavior {key} is not a count ('{field.Value.OriginalText.Trim()}').");
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
            var matrix = skeleton.RestWorldTransforms[index];
            return new BonePose(Position(matrix.M41), Position(matrix.M42), Position(matrix.M43),
                Fixed.FromDecimal(decimal.Round((decimal)MathF.Atan2(matrix.M12, matrix.M11) * HeadingStep) / HeadingStep));
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

    private static Fixed Position(float value) => Fixed.FromDecimal(decimal.Round((decimal)value * PositionStep) / PositionStep);
}
