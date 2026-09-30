using System.Collections.Frozen;
using System.Numerics;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;

namespace CnCNew.Presentation.Input;

/// <summary>
/// <c>Mouse::MouseCursor</c> with the <c>Mouse.ini</c> names of <c>Mouse.cpp</c>'s <c>CursorININames</c>. <see cref="Build"/> and
/// <see cref="InvalidBuild"/> are engine cursors without a <c>MouseCursor</c> block in the retail data.
/// </summary>
public enum CursorKind
{
    Normal, Arrow, Scroll, Target, Move, AttackMove, AttackObj, ForceAttackObj, ForceAttackGround, Build, InvalidBuild, GenericInvalid, Select,
    EnterFriendly, EnterAggressive, SetRallyPoint, GetRepaired, GetHealed, DoRepair, ResumeConstruction, CaptureBuilding, SnipeVehicle,
    LaserGuidedMissiles, TankHunterTntAttack, StabAttack, PlaceRemoteCharge, PlaceTimedCharge, Defector, Dock, FireFlame, FireBomb, PlaceBeacon,
    DisguiseAsVehicle, Waypoint, OutRange, StabAttackInvalid, PlaceChargeInvalid, Hack, ParticleUplinkCannon,
}

public static class CursorNames
{
    private static readonly FrozenDictionary<string, CursorKind> ByName = new Dictionary<string, CursorKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["Normal"] = CursorKind.Normal, ["Arrow"] = CursorKind.Arrow, ["Scroll"] = CursorKind.Scroll, ["Target"] = CursorKind.Target, ["Move"] = CursorKind.Move,
        ["AttackMove"] = CursorKind.AttackMove, ["AttackObj"] = CursorKind.AttackObj, ["ForceAttackObj"] = CursorKind.ForceAttackObj,
        ["ForceAttackGround"] = CursorKind.ForceAttackGround, ["Build"] = CursorKind.Build, ["InvalidBuild"] = CursorKind.InvalidBuild,
        ["GenericInvalid"] = CursorKind.GenericInvalid, ["Select"] = CursorKind.Select, ["EnterFriendly"] = CursorKind.EnterFriendly,
        ["EnterAggressive"] = CursorKind.EnterAggressive, ["SetRallyPoint"] = CursorKind.SetRallyPoint, ["GetRepaired"] = CursorKind.GetRepaired,
        ["GetHealed"] = CursorKind.GetHealed, ["DoRepair"] = CursorKind.DoRepair, ["ResumeConstruction"] = CursorKind.ResumeConstruction,
        ["CaptureBuilding"] = CursorKind.CaptureBuilding, ["SnipeVehicle"] = CursorKind.SnipeVehicle, ["LaserGuidedMissiles"] = CursorKind.LaserGuidedMissiles,
        ["TankHunterTNTAttack"] = CursorKind.TankHunterTntAttack, ["StabAttack"] = CursorKind.StabAttack, ["PlaceRemoteCharge"] = CursorKind.PlaceRemoteCharge,
        ["PlaceTimedCharge"] = CursorKind.PlaceTimedCharge, ["Defector"] = CursorKind.Defector, ["Dock"] = CursorKind.Dock, ["FireFlame"] = CursorKind.FireFlame,
        ["FireBomb"] = CursorKind.FireBomb, ["PlaceBeacon"] = CursorKind.PlaceBeacon, ["DisguiseAsVehicle"] = CursorKind.DisguiseAsVehicle,
        ["Waypoint"] = CursorKind.Waypoint, ["OutRange"] = CursorKind.OutRange, ["StabAttackInvalid"] = CursorKind.StabAttackInvalid,
        ["PlaceChargeInvalid"] = CursorKind.PlaceChargeInvalid, ["Hack"] = CursorKind.Hack, ["ParticleUplinkCannon"] = CursorKind.ParticleUplinkCannon,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<CursorKind, string> ByKind = ByName.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    public static bool TryParse(string name, out CursorKind kind) => ByName.TryGetValue(name.Trim(), out kind);

    /// <summary>The <c>MouseCursor</c> block name the renderer loads.</summary>
    public static string IniName(CursorKind kind) => ByKind[kind];
}

/// <summary>What a click at the cursor would do (<c>evaluateContextCommand</c> message types, reduced to what this layer can issue).</summary>
public enum ContextAction
{
    None,
    Select,
    Move,
    AddWaypoint,
    AttackObject,
    AttackObjectAfterMoving,
    ForceAttackObject,
    /// <summary>Shown, but the simulation has no fire-at-position command: executing it reports <see cref="FeedbackKind.Unsupported"/>.</summary>
    ForceAttackGround,
    ResumeConstruction,
    Dock,
    Repair,
    GetRepaired,
    Enter,
    SetRallyPoint,
    Invalid,
}

public readonly record struct ContextHint(ContextAction Action, CursorKind Cursor, EntityId Target, Vector3? Position)
{
    /// <summary>A click with this hint issues an order rather than selecting.</summary>
    public bool IsOrder => Action is not (ContextAction.None or ContextAction.Select or ContextAction.Invalid);
}

public enum FeedbackKind
{
    /// <summary><c>InGameUI::createMoveHint</c>: a ground marker at the destination.</summary>
    MoveMarker,
    WaypointMarker,
    RallyMarker,
    AttackMarker,
    EnterMarker,
    /// <summary>A click that cannot become an order (<c>GENERIC_INVALID</c>, the no-can-do sound).</summary>
    InvalidOrder,
    /// <summary>The order exists in the original but the simulation offers no command for it.</summary>
    Unsupported,
    /// <summary>The simulation refused the command at submission (<see cref="CommandFeedback.Rejection"/>).</summary>
    Rejected,
}

/// <summary>
/// A short-lived indicator for the renderer. <see cref="DurationFrames"/> follows the original: a move hint lives 40 client frames
/// (<c>W3DInGameUI::drawMoveHints</c>).
/// </summary>
public sealed record CommandFeedback(FeedbackKind Kind, Vector3 Position, EntityId Target, long Tick, int DurationFrames, CommandRejection Rejection = 0, string Detail = "");

/// <summary>The building placement preview (<c>InGameUI::handleBuildPlacements</c>): where, how rotated, and whether the simulation would accept it.</summary>
public sealed record PlacementGhost(string Template, Vector3 Position, float Angle, bool IsValid, PlacementIssue Reason, bool Anchored);

public enum CommandModeKind
{
    None,
    AttackMove,
    Guard,
    /// <summary>A button with a <c>NEED_TARGET</c> option waits for its target click (<c>InGameUI::setGUICommand</c>).</summary>
    ButtonTarget,
    /// <summary>A <c>DOZER_CONSTRUCT</c> button waits for the placement (<c>InGameUI::placeBuildAvailable</c>).</summary>
    Placement,
}

public enum ActivationResult
{
    Issued,
    EnteredMode,
    Rejected,
    /// <summary>The button kind has no simulation command (or no target handling) yet; reported, never ignored.</summary>
    Unsupported,
    /// <summary>No selected object can carry out the button.</summary>
    NoSource,
}
