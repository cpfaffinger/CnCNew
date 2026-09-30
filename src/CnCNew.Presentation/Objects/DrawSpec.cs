using System.Collections.Immutable;

namespace CnCNew.Presentation.Objects;

/// <summary>What a draw module renders; drives how the client treats a <see cref="DrawSpec"/>.</summary>
public enum DrawModuleKind
{
    /// <summary><c>W3DModelDraw</c> and its subclasses (tank, truck, overlord, police car, supply, dependency, science): condition states.</summary>
    Model,
    /// <summary><c>W3DTreeDraw</c>: one model (<c>ModelName</c>) with optional texture, sway and topple parameters.</summary>
    Tree,
    /// <summary><c>W3DPropDraw</c>: one static model (<c>ModelName</c>).</summary>
    Prop,
    /// <summary><c>W3DLaserDraw</c>: procedural beams between two points.</summary>
    Laser,
    /// <summary><c>W3DProjectileStreamDraw</c>: a textured strip along a projectile stream.</summary>
    ProjectileStream,
    /// <summary><c>W3DRopeDraw</c>: a procedural rope, configured in code only.</summary>
    Rope,
    /// <summary><c>W3DDebrisDraw</c>: a model chosen by the object creation list at runtime.</summary>
    Debris,
    /// <summary><c>W3DTracerDraw</c>: a procedural tracer line.</summary>
    Tracer,
    /// <summary><c>W3DDefaultDraw</c>: the reference's placeholder that draws nothing useful.</summary>
    Default,
}

/// <summary><c>RenderObjClass::AnimMode</c> in <c>TheAnimModeNames</c> order (<c>rendobj.h</c>).</summary>
public enum AnimationMode { Manual, Loop, Once, LoopPingPong, LoopBackwards, OnceBackwards }

/// <summary><c>ACBits</c> of <c>W3DModelDraw.cpp</c> (<c>ACBitsNames</c>), the <c>Flags</c> key of a condition state.</summary>
[Flags]
public enum AnimationFlags
{
    None = 0,
    RandomStart = 1 << 0,
    StartFrameFirst = 1 << 1,
    StartFrameLast = 1 << 2,
    AdjustHeightByConstructionPercent = 1 << 3,
    PristineBonePosInFinalFrame = 1 << 4,
    MaintainFrameAcrossStates = 1 << 5,
    RestartAnimWhenComplete = 1 << 6,
    MaintainFrameAcrossStates2 = 1 << 7,
    MaintainFrameAcrossStates3 = 1 << 8,
    MaintainFrameAcrossStates4 = 1 << 9,
}

/// <summary>
/// One <c>Animation</c>/<c>IdleAnimation</c> entry (<c>W3DModelDraw.cpp parseAnimation</c>). <see cref="Name"/> is the lower-cased
/// <c>hierarchy.animation</c> handle; the reference loads it from <c>&lt;animation&gt;.w3d</c> (<c>WW3DAssetManager::Get_HAnim</c>), so
/// <see cref="File"/> is the part after the first dot and <see cref="Hierarchy"/> the skeleton before it. A name without a dot has no file.
/// </summary>
public sealed record AnimationSpec(string Name, string? Hierarchy, string? File, decimal DistanceCovered, bool IsIdle);

/// <summary>A turret bone pair of a condition state (<c>ModelConditionInfo::m_turrets</c>); bone names are lower case, null for none.</summary>
public sealed record TurretSpec(string? AngleBone, decimal ArtAngleDegrees, string? PitchBone, decimal ArtPitchDegrees)
{
    public static TurretSpec None { get; } = new(null, 0m, null, 0m);
    public bool IsEmpty => AngleBone is null && PitchBone is null;
}

/// <summary>Per weapon slot (PRIMARY, SECONDARY, TERTIARY) bone names of a condition state; lower case, null for none.</summary>
public sealed record WeaponSlotBones(string? FireFxBone, string? RecoilBone, string? MuzzleFlash, string? LaunchBone, string? HideShowBone)
{
    public static WeaponSlotBones None { get; } = new(null, null, null, null, null);
}

/// <summary>One <c>ShowSubObject</c>/<c>HideSubObject</c> entry after the reference's merge by name.</summary>
public sealed record SubObjectVisibility(string SubObject, bool Hide);

/// <summary>One <c>ParticleSysBone = &lt;bone&gt; &lt;particle system&gt;</c> entry.</summary>
public sealed record ParticleSysBoneSpec(string Bone, string ParticleSystem);

/// <summary>
/// One <c>ModelConditionInfo</c>: a <c>DefaultConditionState</c>, a <c>ConditionState</c> (with its <c>AliasConditionState</c> alternatives) or a
/// <c>TransitionState</c>. <see cref="Conditions"/> holds the alternatives: the header line first, then every alias line; each alternative is one
/// conjunctive bit set matched by <see cref="BestStateSelector"/>.
/// </summary>
public sealed record ModelStateSpec(
    int Index,
    bool IsDefault,
    ImmutableArray<ModelConditionSet> Conditions,
    string? TransitionFrom,
    string? TransitionTo,
    string? Model,
    ImmutableArray<AnimationSpec> Animations,
    AnimationMode Mode,
    decimal AnimationSpeedMin,
    decimal AnimationSpeedMax,
    AnimationFlags Flags,
    string? TransitionKey,
    string? WaitForStateToFinishKey,
    TurretSpec Turret,
    TurretSpec AltTurret,
    ImmutableArray<WeaponSlotBones> WeaponBones,
    ImmutableArray<SubObjectVisibility> SubObjects,
    ImmutableArray<ParticleSysBoneSpec> ParticleSysBones)
{
    public bool IsTransition => TransitionFrom is not null;

    /// <summary>The <c>Art/W3D</c> file the state's model loads from, or null for <c>Model = NONE</c>.</summary>
    public string? ModelFile => Model is null ? null : DrawSpec.ModelPath(Model);
}

/// <summary>A module-level key of a draw module kept as text in source order (typed further only where the presentation needs it).</summary>
public sealed record DrawSetting(string Key, string Value);

/// <summary>
/// The compiled, immutable description of one <c>Draw</c> module of an object template. <see cref="States"/> is the ordered
/// <c>m_conditionStates</c> vector (default state included at its position), <see cref="Transitions"/> the <c>m_transitionMap</c>.
/// Non-model draws (tree, prop) get one synthesized default state carrying their <c>ModelName</c>, so selection works uniformly.
/// </summary>
public sealed record DrawSpec(
    string ModuleType,
    string Tag,
    DrawModuleKind Kind,
    ImmutableArray<ModelStateSpec> States,
    int DefaultStateIndex,
    ImmutableArray<ModelStateSpec> Transitions,
    ModelConditionSet IgnoreConditionStates,
    bool OkToChangeModelColor,
    bool AnimationsRequirePower,
    bool ParticlesAttachedToAnimatedBones,
    string? TrackMarks,
    ImmutableArray<string> ExtraPublicBones,
    string? AttachToBoneInAnotherModule,
    ImmutableArray<DrawSetting> Settings)
{
    public static string ModelPath(string model) => $"Art/W3D/{model}.w3d";

    /// <summary>Every distinct turret bone name of the module's states (angle and pitch, main and alternate), ordinal order.</summary>
    public ImmutableArray<string> TurretBones => [..States.Concat(Transitions)
        .SelectMany(state => new[] { state.Turret.AngleBone, state.Turret.PitchBone, state.AltTurret.AngleBone, state.AltTurret.PitchBone })
        .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public string? Setting(string key)
    {
        string? value = null;
        foreach (var setting in Settings)
            if (setting.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) value = setting.Value;
        return value;
    }
}
