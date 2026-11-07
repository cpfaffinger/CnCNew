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
public sealed record UnitMarker(EntityId Entity, Vector2 Center, float Radius, float HealthFraction, bool IsOwn, bool IsEnemy, bool IsStructure = false)
{
    /// <summary>Under the pointer and not selected: <c>getMousedOverDrawableID</c> shows the bar and the pips without the selection's ring.</summary>
    public bool Hovered { get; init; }

    /// <summary><c>Drawable::drawAmmo</c>: the clip under the health bar of an object of the viewer's; null for none.</summary>
    public (int Total, int Full)? Ammo { get; init; }

    /// <summary><c>Drawable::drawContained</c>: the seats under the health bar, the infantry first; null for none.</summary>
    public (int Total, int Full, int Infantry)? Seats { get; init; }

    /// <summary>
    /// <c>Drawable::drawUIText</c>: the control group (Ctrl and a digit) the selected object of the viewer belongs to, 0 to 9, or -1 for none; <see cref="GroupText"/> is the
    /// game text of its number (<c>NUMBER:n</c>), drawn at the health bar.
    /// </summary>
    public int GroupNumber { get; init; } = -1;

    public string GroupText { get; init; } = string.Empty;

    /// <summary>Tab chose this object's kind among the kinds of a mixed selection: it is drawn with a second, white ring (the original has no subgroups).</summary>
    public bool ActiveKind { get; init; }
}

/// <summary>An animated icon over an object (<c>Drawable::drawDisabled</c>, <c>drawEnthusiastic</c>): the frame of this moment and its place on the screen.</summary>
public sealed record UnitIcon(EntityId Entity, string Image, CnCNew.Ui.Layout.UiRect Place)
{
    public byte Alpha { get; init; } = 255;
}

/// <summary><c>Drawable::drawConstructPercent</c>: how far an object under construction is, over its centre.</summary>
public sealed record UnitLabel(EntityId Entity, Vector2 At, string Text)
{
    /// <summary>The text's colour; white without one.</summary>
    public CnCNew.Ui.Wnd.WndColor? Color { get; init; }
}

/// <summary><c>Drawable::drawVeterancy</c>: the chevrons at the end of where the health bar would sit, for every object in view with a rank.</summary>
public sealed record VeterancyMarker(EntityId Entity, Vector2 At, int Level);

/// <summary>Which of the original's three healing animations (<c>Animation2D.ini</c>: <c>StructureHeal</c> wrench, <c>VehicleHeal</c> wrench, <c>DefaultHeal</c> cross) a healed object shows.</summary>
public enum HealIconKind { Default, Structure, Vehicle }

/// <summary>The healing icon over an object that was healed within the last three seconds (<c>Drawable::drawHealing</c>): where its health bar would sit, in viewport pixels.</summary>
public sealed record HealIconMarker(EntityId Entity, Vector2 Anchor, float BarWidth, HealIconKind Kind);

/// <summary>A producing structure's rally point, projected to screen space.</summary>
public sealed record RallyPointMarker(EntityId Producer, Vector2 Screen);

/// <summary>A unit's current order path (move/attack-move/attack), as screen points from the unit to its destination.</summary>
public sealed record OrderLine(EntityId Unit, ImmutableArray<Vector2> Points, bool IsAttack);

/// <summary>
/// A queued waypoint path on screen (<see cref="CnCNew.Presentation.Input.WaypointPath"/> projected): <see cref="Points"/>[0] is the unit, every further
/// point a station with its marker. A point behind the camera ends the path there.
/// </summary>
public sealed record WaypointPathOverlay(EntityId Unit, ImmutableArray<Vector2> Points);

/// <summary>A fading marker where an order was just given; <see cref="Age"/> runs from 0 (given) to 1 (gone).</summary>
public sealed record OrderHintMarker(CnCNew.Presentation.Input.OrderHintKind Kind, Vector2 Screen, float Age);

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
    ImmutableArray<string> Ticker,
    ImmutableArray<HealIconMarker> HealIcons = default,
    ImmutableArray<WaypointPathOverlay> WaypointPaths = default,
    ImmutableArray<OrderHintMarker> OrderHints = default,
    ImmutableArray<VeterancyMarker> Ranks = default,
    ImmutableArray<UnitIcon> Icons = default,
    ImmutableArray<UnitLabel> Labels = default)
{
    public static readonly HudOverlay Empty = new(SelectionBoxOverlay.None, [], [], [], PlacementGhostOverlay.None, null, [], []);
}
