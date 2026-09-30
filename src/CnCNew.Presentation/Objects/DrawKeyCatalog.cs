using System.Collections.Immutable;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// The key tables of every retail draw module, transcribed from the reference field parse tables (<c>buildFieldParse</c> of each
/// <c>W3D*DrawModuleData</c>, <c>W3DModelDraw.cpp parseConditionState</c>). A key outside these tables is an explicit
/// <c>unknown-draw-key</c> diagnostic; only the entries of <see cref="AllowList"/> are tolerated by the audit.
/// </summary>
public static class DrawKeyCatalog
{
    /// <summary><c>W3DModelDrawModuleData::buildFieldParse</c> without the four condition-state entries (handled as blocks/aliases).</summary>
    public static ImmutableArray<string> ModelDrawKeys { get; } =
    [
        "InitialRecoilSpeed", "MaxRecoilDistance", "RecoilDamping", "RecoilSettleSpeed", "OkToChangeModelColor", "AnimationsRequirePower",
        "ParticlesAttachedToAnimatedBones", "MinLODRequired", "ProjectileBoneFeedbackEnabledSlots", "TrackMarks", "ExtraPublicBone",
        "AttachToBoneInAnotherModule", "IgnoreConditionStates", "ReceivesDynamicLights",
    ];

    /// <summary><c>W3DModelDrawModuleData::parseConditionState myFieldParse</c>.</summary>
    public static ImmutableArray<string> ConditionStateKeys { get; } =
    [
        "Model", "Turret", "TurretArtAngle", "TurretPitch", "TurretArtPitch", "AltTurret", "AltTurretArtAngle", "AltTurretPitch",
        "AltTurretArtPitch", "ShowSubObject", "HideSubObject", "WeaponFireFXBone", "WeaponRecoilBone", "WeaponMuzzleFlash",
        "WeaponLaunchBone", "WeaponHideShowBone", "Animation", "IdleAnimation", "AnimationMode", "TransitionKey",
        "WaitForStateToFinishIfPossible", "Flags", "ParticleSysBone", "AnimationSpeedFactorRange",
    ];

    private static readonly string[] TankKeys = ["TreadDebrisLeft", "TreadDebrisRight", "TreadAnimationRate", "TreadPivotSpeedFraction", "TreadDriveSpeedFraction"];

    private static readonly string[] TruckKeys =
    [
        "Dust", "DirtSpray", "PowerslideSpray", "LeftFrontTireBone", "RightFrontTireBone", "LeftRearTireBone", "RightRearTireBone",
        "MidLeftFrontTireBone", "MidRightFrontTireBone", "MidLeftRearTireBone", "MidRightRearTireBone", "MidLeftMidTireBone", "MidRightMidTireBone",
        "TireRotationMultiplier", "PowerslideRotationAddition", "CabBone", "TrailerBone", "CabRotationMultiplier", "TrailerRotationMultiplier", "RotationDamping",
    ];

    /// <summary><c>W3DTankTruckDrawModuleData</c> repeats the truck keys without the mid tires, cab and trailer, plus the tank tread keys.</summary>
    private static readonly string[] TankTruckKeys =
    [
        "Dust", "DirtSpray", "PowerslideSpray", "LeftFrontTireBone", "RightFrontTireBone", "LeftRearTireBone", "RightRearTireBone",
        "MidLeftFrontTireBone", "MidRightFrontTireBone", "MidLeftRearTireBone", "MidRightRearTireBone", "TireRotationMultiplier", "PowerslideRotationAddition",
        "TreadDebrisLeft", "TreadDebrisRight", "TreadAnimationRate", "TreadPivotSpeedFraction", "TreadDriveSpeedFraction",
    ];

    private static readonly string[] TreeKeys =
    [
        "ModelName", "TextureName", "MoveOutwardTime", "MoveInwardTime", "MoveOutwardDistanceFactor", "DarkeningFactor", "ToppleFX", "BounceFX",
        "StumpName", "KillWhenFinishedToppling", "DoTopple", "InitialVelocityPercent", "InitialAccelPercent", "BounceVelocityPercent",
        "MinimumToppleSpeed", "SinkDistance", "SinkTime", "DoShadow",
    ];

    private static readonly string[] LaserKeys =
    [
        "NumBeams", "InnerBeamWidth", "OuterBeamWidth", "InnerColor", "OuterColor", "MaxIntensityLifetime", "FadeLifetime", "Texture",
        "ScrollRate", "Tile", "Segments", "ArcHeight", "SegmentOverlapRatio", "TilingScalar",
    ];

    /// <summary>Module type → kind, whether it parses condition states, and its module-level keys (source file of the table).</summary>
    public static ImmutableSortedDictionary<string, DrawModuleInfo> Modules { get; } = new[]
    {
        Model("W3DModelDraw", "W3DModelDraw.cpp"),
        Model("W3DOverlordAircraftDraw", "W3DOverlordAircraftDraw.cpp (W3DModelDrawModuleData)"),
        Model("W3DTankDraw", "W3DTankDraw.cpp", TankKeys),
        Model("W3DOverlordTankDraw", "W3DOverlordTankDraw.cpp (W3DTankDrawModuleData)", TankKeys),
        Model("W3DTruckDraw", "W3DTruckDraw.cpp", TruckKeys),
        Model("W3DOverlordTruckDraw", "W3DOverlordTruckDraw.cpp (W3DTruckDrawModuleData)", TruckKeys),
        Model("W3DPoliceCarDraw", "W3DPoliceCarDraw.cpp (W3DTruckDrawModuleData)", TruckKeys),
        Model("W3DTankTruckDraw", "W3DTankTruckDraw.cpp", TankTruckKeys),
        Model("W3DDependencyModelDraw", "W3DDependencyModelDraw.cpp", "AttachToBoneInContainer"),
        Model("W3DSupplyDraw", "W3DSupplyDraw.cpp", "SupplyBonePrefix"),
        Model("W3DScienceModelDraw", "W3DScienceModelDraw.cpp", "RequiredScience"),
        new DrawModuleInfo("W3DTreeDraw", DrawModuleKind.Tree, false, [..TreeKeys], "W3DTreeDraw.cpp"),
        new DrawModuleInfo("W3DPropDraw", DrawModuleKind.Prop, false, ["ModelName"], "W3DPropDraw.cpp"),
        new DrawModuleInfo("W3DLaserDraw", DrawModuleKind.Laser, false, [..LaserKeys], "W3DLaserDraw.cpp"),
        new DrawModuleInfo("W3DProjectileStreamDraw", DrawModuleKind.ProjectileStream, false, ["Texture", "Width", "TileFactor", "ScrollRate", "MaxSegments"], "W3DProjectileStreamDraw.cpp"),
        new DrawModuleInfo("W3DRopeDraw", DrawModuleKind.Rope, false, [], "W3DRopeDraw.cpp (ModuleData, no keys)"),
        new DrawModuleInfo("W3DDebrisDraw", DrawModuleKind.Debris, false, [], "W3DDebrisDraw.cpp (ModuleData, no keys)"),
        new DrawModuleInfo("W3DTracerDraw", DrawModuleKind.Tracer, false, [], "W3DTracerDraw.cpp (ModuleData, no keys)"),
        new DrawModuleInfo("W3DDefaultDraw", DrawModuleKind.Default, false, [], "W3DDefaultDraw.cpp (ModuleData, no keys)"),
    }.ToImmutableSortedDictionary(info => info.ModuleType, info => info, StringComparer.OrdinalIgnoreCase);

    private static DrawModuleInfo Model(string type, string source, params string[] extra)
        => new(type, DrawModuleKind.Model, true, [..ModelDrawKeys, ..extra], source);

    /// <summary>
    /// Keys the audit tolerates although no reference table knows them. Every entry names the module type (or <c>*</c>), the key and why it is
    /// harmless. The retail data needs no entry; the table exists so a future exception is a reviewed, documented change.
    /// </summary>
    public static ImmutableArray<DrawKeyAllowance> AllowList { get; } = [];

    public static bool IsAllowListed(string moduleType, string key)
        => AllowList.Any(entry => (entry.ModuleType == "*" || entry.ModuleType.Equals(moduleType, StringComparison.OrdinalIgnoreCase))
                                  && entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}

public sealed record DrawModuleInfo(string ModuleType, DrawModuleKind Kind, bool HasConditionStates, ImmutableArray<string> Keys, string Source)
{
    public bool Knows(string key) => Keys.Contains(key, StringComparer.OrdinalIgnoreCase);
}

public sealed record DrawKeyAllowance(string ModuleType, string Key, string Reason);
