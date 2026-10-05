using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// The building that follows the cursor while a structure is placed (<c>InGameUI::placeBuildAvailable</c> creates a drawable of the template,
/// <c>handleBuildPlacements</c> moves and turns it every frame): the model of the template in its normal state, translucent
/// (<see cref="Opacity"/>, <c>placementOpacity</c>) and, where the simulation would refuse the spot, tinted red (<see cref="IllegalTint"/>, <c>illegalBuildColor</c>
/// through <c>Drawable::colorTint</c>, which is a locked colour flash). It is a <see cref="DrawableInstance"/> like any other, so the object layer draws it with the
/// models, animations and house colour it already knows; only its <see cref="DrawableInstance.Opacity"/> and <see cref="DrawableInstance.FlashTint"/> differ.
/// Built from a <see cref="PlacementGhost"/> alone (where, how turned, legal or not); it reads nothing of the world.
/// <para>
/// Not done: the line of tiles for walls and fences dragged out (<c>m_placeIcon[1..]</c>, <c>buildTiledLocations</c>), the faction bib decal under an illegal spot
/// (<c>addFactionBibDrawable</c>) and the owner's indicator colour on a night map.
/// </para>
/// </summary>
public static class PlacementPreview
{
    /// <summary><c>placementOpacity</c> of <c>InGameUI.cpp</c>.</summary>
    public const float Opacity = 0.45f;

    /// <summary><c>illegalBuildColor</c> of <c>InGameUI.cpp</c> (1, 0, 0): the colour added by the locked flash.</summary>
    public static readonly Vector3 IllegalTint = new(1f, 0f, 0f);

    /// <summary>
    /// The drawable for <paramref name="ghost"/>, or null when there is nothing to show: the pointer is off the terrain (<see cref="PlacementIssue.NoGround"/>) or the
    /// template has no visual in <paramref name="catalog"/> or no model to draw.
    /// </summary>
    public static DrawableInstance? Create(ObjectVisualCatalog catalog, PlacementGhost ghost, PlayerId owner, TeamColors? colors)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ghost);
        if (ghost.Reason == PlacementIssue.NoGround || !catalog.TryGet(ghost.Template, out var visual) || !visual.HasModel) return null;
        var selections = FrameProjector.Select(visual, ModelConditionSet.Empty);
        return new DrawableInstance(
            EntityId.Invalid, DrawableInstance.PlacementPreviewIndex, visual, ghost.Position, ghost.Angle, owner, colors?.Of(owner) ?? TeamColors.Neutral,
            1f, 1f, ModelConditionSet.Empty, selections, null, null, false, false,
            FlashTint: ghost.IsValid ? Vector3.Zero : IllegalTint, Opacity: Opacity);
    }
}
