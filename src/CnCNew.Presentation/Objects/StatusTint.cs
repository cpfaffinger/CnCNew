using System.Numerics;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// <c>TintEnvelope</c> for what lasts (<c>Drawable::update</c>, the ladder of <c>TINT_STATUS_*</c>): a disabled object goes dark over 30 frames of the original and stays dark, an object that takes
/// subdual damage goes blue over 150; disabled goes first. <c>DISABLED_HELD</c>, a script's disable and <c>DISABLED_UNMANNED</c> tint nobody (<c>Object::setDisabledUntil</c>).
/// </summary>
public sealed class StatusTintTracker
{
    /// <summary><c>DARK_GRAY_DISABLED_COLOR</c>.</summary>
    public static readonly Vector3 Disabled = new(-0.5f, -0.5f, -0.5f);

    /// <summary><c>SUBDUAL_DAMAGE_COLOR</c>.</summary>
    public static readonly Vector3 Subdual = new(-0.2f, -0.2f, 0.8f);

    private const int DisabledFrames = 30;
    private const int SubdualFrames = 150;
    private const DisabledMask NoTint = DisabledMask.Held | DisabledMask.ScriptDisabled | DisabledMask.Unmanned;

    private readonly Dictionary<uint, (Vector3 Color, int Attack, int Age)> _tints = [];

    /// <summary>The colour the entity asks for now, zero for none.</summary>
    public static Vector3 Wanted(SimulationWorld world, Entity entity)
    {
        if ((world.DisabledFlagsOf(entity) & ~NoTint) != DisabledMask.None) return Disabled;
        return entity.Body is ActiveBody { SubdualDamage.Raw: > 0 } ? Subdual : Vector3.Zero;
    }

    /// <summary>One original frame of the envelope of the entity.</summary>
    public void Step(uint entity, Vector3 wanted)
    {
        _tints.TryGetValue(entity, out var tint);
        if (wanted != Vector3.Zero)
        {
            var attack = wanted == Disabled ? DisabledFrames : SubdualFrames;
            // A new colour starts from where the old one stands.
            _tints[entity] = (wanted, attack, Math.Min(tint.Color == wanted || tint.Age == 0 ? tint.Age + 1 : tint.Age, attack));
        }
        else if (tint.Age > 1) _tints[entity] = tint with { Age = tint.Age - 1 };
        else _tints.Remove(entity);
    }

    public Vector3 TintAt(uint entity) => _tints.TryGetValue(entity, out var tint) ? tint.Color * (tint.Age / (float)tint.Attack) : Vector3.Zero;

    /// <summary>Forgets those that are gone.</summary>
    public void Prune(Func<uint, bool> alive)
    {
        foreach (var id in _tints.Keys.Where(id => !alive(id)).ToArray()) _tints.Remove(id);
    }
}
