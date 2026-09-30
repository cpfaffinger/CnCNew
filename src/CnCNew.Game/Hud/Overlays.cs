using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Ids;

namespace CnCNew.Game.Hud;

/// <summary>The drag-select rectangle in screen pixels while the left button is held over open ground.</summary>
public sealed record SelectionBoxOverlay(bool Active, Vector2 Start, Vector2 Current)
{
    public static readonly SelectionBoxOverlay None = new(false, Vector2.Zero, Vector2.Zero);
}

/// <summary>A selected (or single foreign, for-information) unit's on-screen marker: a selection circle and a health bar.</summary>
public sealed record UnitMarker(EntityId Entity, Vector2 Center, float Radius, float HealthFraction, bool IsOwn, bool IsEnemy);

/// <summary>A producing structure's rally point, projected to screen space.</summary>
public sealed record RallyPointMarker(EntityId Producer, Vector2 Screen);

/// <summary>A unit's current order path (move/attack-move/attack), as screen points from the unit to its destination.</summary>
public sealed record OrderLine(EntityId Unit, ImmutableArray<Vector2> Points, bool IsAttack);

/// <summary>The construction ghost footprint while <see cref="CnCNew.Presentation.Input.CommandModeKind.Placement"/> is active.</summary>
public sealed record PlacementGhostOverlay(bool Active, ImmutableArray<Vector2> FootprintScreen, bool IsValid)
{
    public static readonly PlacementGhostOverlay None = new(false, [], false);
}

/// <summary>
/// Everything <see cref="MatchController"/> derives every frame for screen-space rendering that is not expressed as
/// <see cref="CnCNew.Ui.Runtime.UiDrawList"/> commands: selection feedback, order lines, the placement ghost and short-lived hint
/// text. The host projects nothing itself; every point here is already in viewport pixels.
/// </summary>
public sealed record HudOverlay(
    SelectionBoxOverlay SelectionBox,
    ImmutableArray<UnitMarker> Selected,
    ImmutableArray<RallyPointMarker> RallyPoints,
    ImmutableArray<OrderLine> OrderLines,
    PlacementGhostOverlay Placement,
    string? HintText,
    ImmutableArray<string> Ticker)
{
    public static readonly HudOverlay Empty = new(SelectionBoxOverlay.None, [], [], [], PlacementGhostOverlay.None, null, []);
}
