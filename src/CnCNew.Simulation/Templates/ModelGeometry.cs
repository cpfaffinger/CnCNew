using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Templates;

/// <summary>A bone of a model in the object's own frame (x along the heading, y to the left, z up) with the bone's own heading about z (radians).</summary>
public readonly record struct BonePose(Fixed X, Fixed Y, Fixed Z, Fixed Heading);

/// <summary>
/// The points of an airfield model that <c>ParkingPlaceBehavior::buildInfo</c> reads with <c>Object::getSingleLogicalBonePosition</c>, in the object's frame: per parking space
/// (row-major, <c>Runway{col}Parking{row}</c>, <c>Runway{col}Park{row}Han</c>, <c>Runway{col}Prep{row}</c>), per runway (column) <c>RunwayStart{col}</c>/<c>RunwayEnd{col}</c>, and the
/// helipad's <c>HeliPark01</c> when the model has one. <see cref="Rows"/>/<see cref="Cols"/> are the dimensions the loader read the table for; a module whose data disagrees refuses the table.
/// </summary>
public sealed record AirfieldLayout(int Rows, int Cols, ImmutableArray<BonePose> Parking, ImmutableArray<BonePose> Hangar, ImmutableArray<BonePose> Prep,
    ImmutableArray<BonePose> RunwayStart, ImmutableArray<BonePose> RunwayEnd, BonePose? HeliPark);

/// <summary>
/// Model-derived constants the simulation may not read from model files itself (layering: the simulation sees no archives, content or renderer formats). The client layer reads them once from
/// the base-pose skeletons of the draw models at load time and hands them to <see cref="TemplateCatalog.Build"/>; they are part of the world's identity, so <see cref="Fingerprint"/> is
/// folded into the definition fingerprint a world, a replay and a save are checked against (<see cref="Combine"/>, <c>docs/SIM-JET-FLIGHT.md</c>). <see cref="None"/> (the synthetic test
/// fixtures and the headless tools) keeps the computed airfield layout of <see cref="Modules.BuiltIn.ParkingPlaceBehavior"/>.
/// </summary>
/// <summary>
/// One exit path of a container's model (<c>OpenContain::exitObjectViaDoor</c>): the bones <c>ExitStart</c>/<c>ExitEnd</c> (<c>ExitStartNN</c>/<c>ExitEndNN</c> when the module has more than one
/// path) in the object's frame. A bone the model does not have is <see langword="null"/>: <c>Object::getSingleLogicalBonePosition</c> then answers with the object's own position.
/// </summary>
public sealed record ExitPath(BonePose? Start, BonePose? End);

public sealed class ModelGeometry
{
    public static ModelGeometry None { get; } = new([]);

    /// <summary>The exit paths of container templates by name (case-insensitive), one entry per <c>NumberOfExitPaths</c>; a template without an entry has no model the table was read from.</summary>
    public ImmutableDictionary<string, ImmutableArray<ExitPath>> ExitPaths { get; }

    /// <summary>Airfield templates by name (case-insensitive).</summary>
    public ImmutableDictionary<string, AirfieldLayout> Airfields { get; }

    /// <summary>SHA-256 of the canonical text of the table; empty for <see cref="None"/>.</summary>
    public string Fingerprint { get; }

    public bool IsEmpty => Airfields.IsEmpty && ExitPaths.IsEmpty;

    public ModelGeometry(IEnumerable<KeyValuePair<string, AirfieldLayout>> airfields) : this(airfields, []) { }

    public ModelGeometry(IEnumerable<KeyValuePair<string, AirfieldLayout>> airfields, IEnumerable<KeyValuePair<string, ImmutableArray<ExitPath>>> exitPaths)
    {
        Airfields = airfields.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        ExitPaths = exitPaths.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        Fingerprint = IsEmpty ? string.Empty : ComputeFingerprint();
    }

    /// <summary>The definition fingerprint a world built on <paramref name="definitionFingerprint"/> and this table carries: unchanged when the table is empty.</summary>
    public string Combine(string definitionFingerprint)
        => IsEmpty ? definitionFingerprint : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definitionFingerprint + "+model-geometry:" + Fingerprint)));

    private string ComputeFingerprint()
    {
        var text = new StringBuilder("model-geometry-v1\n");
        static string Pose(BonePose pose) => string.Create(CultureInfo.InvariantCulture, $"{pose.X.Raw},{pose.Y.Raw},{pose.Z.Raw},{pose.Heading.Raw}");
        foreach (var pair in Airfields.OrderBy(value => value.Key.ToUpperInvariant(), StringComparer.Ordinal))
        {
            var layout = pair.Value;
            text.Append(CultureInfo.InvariantCulture, $"airfield {pair.Key.ToUpperInvariant()} {layout.Rows}x{layout.Cols}\n");
            foreach (var (name, poses) in new[] { ("parking", layout.Parking), ("hangar", layout.Hangar), ("prep", layout.Prep), ("start", layout.RunwayStart), ("end", layout.RunwayEnd) })
                text.Append(CultureInfo.InvariantCulture, $"  {name} {string.Join(";", poses.Select(Pose))}\n");
            text.Append(CultureInfo.InvariantCulture, $"  helipark {(layout.HeliPark is { } pad ? Pose(pad) : "-")}\n");
        }
        // Written only when the table has exit paths: a table of airfields alone keeps its fingerprint.
        foreach (var pair in ExitPaths.OrderBy(value => value.Key.ToUpperInvariant(), StringComparer.Ordinal))
        {
            static string Bone(BonePose? pose) => pose is { } value ? Pose(value) : "-";
            text.Append(CultureInfo.InvariantCulture, $"exits {pair.Key.ToUpperInvariant()} {string.Join(";", pair.Value.Select(path => Bone(path.Start) + ">" + Bone(path.End)))}\n");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
