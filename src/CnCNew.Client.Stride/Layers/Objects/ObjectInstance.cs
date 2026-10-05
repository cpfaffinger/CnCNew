using CnCNew.Presentation.Objects;
using CnCNew.W3D;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>One skinned sub-object of a live drawable: its pooled entity, its per-frame skin output buffers (reused, never reallocated) and the template it re-skins from.</summary>
internal sealed class SkinRuntime
{
    public required PooledEntity Entity { get; init; }
    public required SkinPart Part { get; init; }

    /// <summary>Sized to <c>Part.Mesh.Vertices.Count</c>; overwritten by <see cref="W3dModel.SkinVertices"/> every visible frame.</summary>
    public required System.Numerics.Vector3[] Positions { get; init; }
    public required System.Numerics.Vector3[] Normals { get; init; }

    /// <summary>One live copy per batch, seeded from <see cref="SkinBatchTemplate.Vertices"/>; only Position/Normal are rewritten per frame.</summary>
    public required W3dRenderVertex[][] LiveVertices { get; init; }

    /// <summary>The flash factor (<see cref="FlashTint"/>) the entity's material overrides were last set for.</summary>
    public uint Flash { get; set; } = FlashTint.None;

    /// <summary>The opacity step (<see cref="ModelAsset.SolidOpacity"/> = solid) the entity's material overrides were last set for.</summary>
    public int Opacity { get; set; } = ModelAsset.SolidOpacity;
}

/// <summary>
/// Persistent per-frame-module state of one <c>Draw</c> module of one live drawable (a drawable with two Model draws, e.g. a hull and a
/// turret drawn as two separate modules, gets two of these). Reused across frames so pooled entities and the animation pose survive as
/// long as the resolved model does not change.
/// </summary>
internal sealed class DrawPartState
{
    public string? ModelName;
    public ModelAsset? Asset;
    public PooledEntity? Rigid;
    public List<SkinRuntime> Skins { get; } = [];
    public PooledEntity? Placeholder;
    public W3dPose? Pose;
    public int StateIndex = -1;
    public AnimationSpec? Animation;
    public W3dAnimationBinding? Binding;
    public double Speed = 1.0;
    public double ElapsedInState;
    public float LastFrame = float.NaN;

    /// <summary>The turret angles last written into <see cref="Pose"/> (main yaw/pitch, alternate yaw/pitch); NaN until the first write.</summary>
    public float[] TurretApplied = [float.NaN, float.NaN, float.NaN, float.NaN];
    public bool InScene;

    /// <summary>Bone index of every muzzle flash sub-object of the current state (<c>WeaponMuzzleFlash</c>) to the weapon slot whose shot shows it; rebuilt when the state changes.</summary>
    public Dictionary<int, int>? MuzzleBoneSlots;

    /// <summary>W3DTruckDraw: the tire bones of the bound model (resolved once per model), the module's rotation multiplier and the wheel angle last written into the pose.</summary>
    public int[]? WheelBones;

    /// <summary>Per entry of <see cref="WheelBones"/>: bit 0 front axle (steers), bit 1 right side.</summary>
    public byte[] WheelRoles = [];
    public float WheelMultiplier;

    /// <summary>Spin, steering angle, the four spring offsets, cab and trailer angle last written into the pose (NaN until the first write).</summary>
    public float[] WheelApplied = [float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN];

    /// <summary>W3DTruckDraw <c>CabBone</c>/<c>TrailerBone</c> (0 when absent), their rotation multipliers and damping, and the angles they currently stand at.</summary>
    public int CabBone;
    public int TrailerBone;
    public float CabFactor;
    public float TrailerFactor;
    public float RotationDamping = 1f;
    public float CabAngle;
    public float TrailerAngle;

    /// <summary>True once <see cref="ModelName"/> was set to something and the asset failed to load (drawn as a placeholder).</summary>
    public bool IsPlaceholder => Placeholder is not null;
}

/// <summary>Everything the object layer keeps between frames for one drawable key (an entity id, or a scenery index above bit 40).</summary>
internal sealed class ObjectInstance
{
    public DrawPartState[] Parts = [];
    public bool SeenThisFrame;
}
