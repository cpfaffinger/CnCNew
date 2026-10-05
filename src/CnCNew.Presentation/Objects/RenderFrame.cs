using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Objects;

/// <summary>The state a draw module shows: index into <see cref="DrawSpec.States"/> (-1: the module has no states) and its model (null: none).</summary>
public readonly record struct DrawModuleSelection(int StateIndex, string? Model);

/// <summary>
/// One thing to draw in a <see cref="RenderFrame"/>. Entities carry their <see cref="Entity"/> id and <see cref="SceneryIndex"/> -1; map
/// scenery carries <see cref="EntityId.Invalid"/> and its index in the projector's scenery list. <see cref="Position"/> and
/// <see cref="Yaw"/> (radians, counter-clockwise about +Z like the simulation's angle) are already interpolated.
/// <see cref="Selections"/> is parallel to <see cref="ObjectVisual.Draws"/>.
/// </summary>
public sealed record DrawableInstance(
    EntityId Entity,
    int SceneryIndex,
    ObjectVisual Visual,
    Vector3 Position,
    float Yaw,
    PlayerId Owner,
    ColorValue TeamColor,
    float Health01,
    float ConstructionProgress01,
    ModelConditionSet Conditions,
    ImmutableArray<DrawModuleSelection> Selections,
    float? TurretYaw,
    float? TurretPitch,
    bool IsStatic,
    bool IsGhost,
    float? AltTurretYaw = null,
    float? AltTurretPitch = null,
    float WheelTravel = 0f,
    int FiredSlots = 0,
    bool ShowsHealing = false,
    bool Fogged = false,
    ChassisPose Chassis = default,
    Vector3 FlashTint = default,
    float Opacity = 1f)
{
    // FlashTint: what Drawable::getSelectionColor returns while the selection flash envelope plays (SelectionFlashTracker): a signed colour added to the lights of this drawable; zero: none.

    public bool HasFlashTint => FlashTint != Vector3.Zero;

    // Chassis: the body tilt/lift and the wheel steering and spring offsets of a treaded or wheeled vehicle (SuspensionTracker); level for everything else.

    // Opacity: 1 draws the drawable solid; below 1 the object layer draws every surface of it translucent (Drawable::setDrawableOpacity). Only the placement preview uses it
    // (PlacementPreview.Opacity); entities and scenery stay at 1.

    /// <summary>The <see cref="SceneryIndex"/> of the building preview that follows the cursor while a structure is placed (an entity-less, scenery-less drawable needs a key of its own).</summary>
    public const int PlacementPreviewIndex = -2;

    public bool IsPlacementPreview => SceneryIndex == PlacementPreviewIndex;

    // Fogged: explored but not currently visible to the viewer (remembered structure, scenery out of sight). The original draws such objects through the shroud texture, darkened like the ground.

    /// <summary>Muzzle flash sub-objects are shown for the weapon slots whose bit is set here (<see cref="FrameProjector.MuzzleFlashTicks"/> after the last shot).</summary>
    public bool HasFired(int slot) => (FiredSlots & (1 << slot)) != 0;

    public string TemplateName => Visual.TemplateName;
    public float Scale => Visual.Scale;
    public float SelectionRadius => Visual.SelectionRadius;
    public ImmutableArray<string> Shadow => Visual.Shadow;
    public bool IsStructure => Visual.IsStructure;
    public bool IsUnit => Visual.IsUnit;

    /// <summary>The first selected model over all draw modules, or null.</summary>
    public string? PrimaryModel => Selections.Select(selection => selection.Model).FirstOrDefault(model => model is not null);
}

/// <summary>
/// An immutable picture of the world for one rendered frame: entities in ascending id order (ghosts included, flagged), then scenery in
/// index order. <see cref="Tick"/> is the last simulated tick, <see cref="Alpha"/> the interpolation factor towards it.
/// </summary>
public sealed record RenderFrame(long Tick, float Alpha, PlayerId? Viewer, ImmutableArray<DrawableInstance> Drawables)
{
    public static RenderFrame Empty { get; } = new(-1, 1f, null, []);
}
