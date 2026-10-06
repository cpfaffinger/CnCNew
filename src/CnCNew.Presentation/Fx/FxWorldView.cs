using System.Numerics;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// Where an entity is, as far as effects care. <see cref="Heading"/> is radians around +Z (world up), <see cref="Veterancy"/> is
/// 0..3 (REGULAR..HEROIC), <see cref="BoundingRadius"/> is <c>GeometryInfo::getBoundingCircleRadius</c>.
/// </summary>
public readonly record struct FxEntityPose(Vector3 Position, float Heading, string TemplateName, PlayerId Owner, int Veterancy, float BoundingRadius)
{
    /// <summary>The object transform in row-vector convention (rotate about Z, then translate).</summary>
    public Matrix4x4 Transform => Matrix4x4.CreateRotationZ(Heading) * Matrix4x4.CreateTranslation(Position);
}

/// <summary>
/// The read-only world queries effects need. Every member may return null/false when the answer is unknown; the executor then
/// takes the reference's fallback path (for example the drawable position when no muzzle bone exists) and counts the case.
/// </summary>
public interface IFxWorldView
{
    FxEntityPose? GetPose(EntityId entity);

    /// <summary>World position of a named bone of the entity's current model (C2a's object layer), or null.</summary>
    Vector3? GetBonePosition(EntityId entity, string boneName);

    /// <summary>The muzzle (<c>WeaponFireFXBone</c>) of the weapon, or null to use the entity position (<c>Weapon.cpp</c> fallback).</summary>
    Vector3? GetWeaponMuzzle(EntityId entity, string weaponName);

    /// <summary>The template name of a simulation template index (<c>ProductionCompleted.Value</c>).</summary>
    string? GetTemplateName(int templateIndex);

    float GroundHeight(float x, float y);

    /// <summary>True when the local player cannot see the position (<c>FXList::doFXPos</c> shroud test).</summary>
    bool IsShrouded(Vector3 position);
}

/// <summary>
/// An <see cref="IFxWorldView"/> over a live <see cref="SimulationWorld"/>. Call <see cref="Refresh"/> after every
/// <c>SimulationWorld.Step()</c>: entities destroyed during the step are gone from the world when its events are presented, so their
/// last pose from the previous refresh stays available for one more tick (death effects need it). Bones and muzzles come from
/// optional delegates the object layer supplies; there is no shroud model yet (<see cref="IsShrouded"/> is always false).
/// </summary>
public sealed class SimulationFxWorldView(
    SimulationWorld world,
    Func<EntityId, string, Vector3?>? bones = null,
    Func<EntityId, string, Vector3?>? muzzles = null) : IFxWorldView
{
    private Dictionary<uint, FxEntityPose> _current = [];
    private Dictionary<uint, FxEntityPose> _previous = [];

    public void Refresh()
    {
        (_previous, _current) = (_current, _previous);
        _current.Clear();
        foreach (var entity in world.Entities)
            _current[entity.Id.Value] = PoseOf(entity);
    }

    public static FxEntityPose PoseOf(Entity entity)
        => new(new Vector3(ToFloat(entity.X.Raw), ToFloat(entity.Y.Raw), ToFloat(entity.Z.Raw)), ToFloat(entity.Angle.Raw),
            entity.Template.Name, entity.Owner, entity.VeterancyLevel, ToFloat(entity.Template.BoundingCircleRadius.Raw));

    private static float ToFloat(long raw) => FixedConvert.RawToSingle(raw);

    public FxEntityPose? GetPose(EntityId entity)
    {
        if (!entity.IsValid) return null;
        if (world.FindEntity(entity) is { } live) return PoseOf(live);
        if (_current.TryGetValue(entity.Value, out var pose) || _previous.TryGetValue(entity.Value, out pose)) return pose;
        return null;
    }

    public Vector3? GetBonePosition(EntityId entity, string boneName) => bones?.Invoke(entity, boneName);
    public Vector3? GetWeaponMuzzle(EntityId entity, string weaponName) => muzzles?.Invoke(entity, weaponName);

    public string? GetTemplateName(int templateIndex)
        => templateIndex >= 0 && templateIndex < world.Templates.Templates.Length ? world.Templates[templateIndex].Name : null;

    public float GroundHeight(float x, float y)
    {
        var height = world.GroundHeightAt(FixedConvert.FromSingleTruncated(x), FixedConvert.FromSingleTruncated(y));
        return ToFloat(height.Raw);
    }

    public bool IsShrouded(Vector3 position) => false;
}
