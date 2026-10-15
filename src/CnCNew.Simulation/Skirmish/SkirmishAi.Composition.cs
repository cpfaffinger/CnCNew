using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Skirmish;

/// <summary>How a <see cref="SkirmishAi"/> chooses among the army units its producers could build next.</summary>
public enum ArmyComposition : byte
{
    /// <summary>Every unit type with the same chance, whatever the enemy fields.</summary>
    Uniform = 0,

    /// <summary>The rule "exchange value": the chance of a unit type grows with what it is expected to destroy of the enemy this player has seen before it is destroyed itself.</summary>
    ExchangeValue = 1,
}

/// <summary>
/// The composition rule "exchange value" (observation D of <c>docs/AI-OBSERVED-2026-10-08.md</c>; the reference does not have it: its skirmish AI builds the teams its scripts
/// name per difficulty and does not look at the enemy unit by unit).
/// <para>
/// What the player has seen of the enemy in the last three minutes (<see cref="_sightings"/>: every enemy object with a body, by type) is the enemy's force E. For a candidate
/// unit u, read from the catalog only:
/// <list type="bullet">
/// <item><b>damage per frame</b> d(a, b) of a against b: the best weapon of a's default weapon set that may be fired at b (ground or airborne), its primary damage times the
/// coefficient of b's armour for the damage type, times shots per logic frame (clip and reload included); a weapon that kills its owner counts as one shot in a lifetime;</item>
/// <item><b>offence</b> O(u) = Σ over E of n(e) · cost(e) · d(u, e) / health(e): the credits of the enemy u destroys per frame while it fires;</item>
/// <item><b>exposure</b> X(u) = 1 / <see cref="LifetimeFrames"/> + Σ over E of n(e) · d(e, u) / health(u): the share of itself u loses per frame, never less than "lives one
/// minute";</item>
/// <item><b>exchange value</b> V(u) = O(u) / (X(u) · cost(u)): the enemy credits destroyed per credit spent on u.</item>
/// </list>
/// The draw gives every candidate the same share of <see cref="VarietyShare"/> (a quarter: a floor of variety, and the only chance of units whose worth the catalog does not
/// show — transports, healers, units that only capture) and divides the rest in proportion to V. Without any sighting, or when no candidate can hurt anything seen, the draw
/// is uniform. Nothing is scanned: the sightings come from <see cref="Observe"/>, which reads the objects the player sees anyway, and the values are cached per pair of types.
/// </para>
/// <para>
/// What the rule does not know: range and speed (a unit that never gets to fire), area damage, garrisons, upgrades and veterancy, stealth, special powers, and the enemy's
/// next unit. It judges the unit a producer makes, not teams.
/// </para>
/// </summary>
public sealed partial class SkirmishAi
{
    /// <summary>The composition a computer player uses when its options name none.</summary>
    public const ArmyComposition DefaultComposition = ArmyComposition.ExchangeValue;

    /// <summary>The lifetime the exposure never goes beyond, in logic frames of the original (60 s): a unit nobody seen so far can hurt is not worth infinitely much.</summary>
    private const int LifetimeFrames = 1800;

    /// <summary>The share of the draw that is divided equally among the candidates, as a fraction 1/n: a quarter.</summary>
    private const int VarietyShare = 4;

    /// <summary>No weapon counts for more than this per shot (the data use 99,999-style values for "kills anything").</summary>
    private static readonly Fixed DamageCap = Fixed.FromInt(100_000);

    /// <summary>Resolution of the draw: the weights of all candidates add up to about this.</summary>
    private const int DrawScale = 10_000;

    private readonly ArmyComposition _composition;
    // Enemy objects seen: id -> type and the tick of the last sighting. Part of the AI's state like its other memories.
    private readonly SortedDictionary<uint, (int Template, long Seen)> _sightings = [];
    // Catalog-derived, never state: damage per frame for a pair of types, and a type's health.
    private readonly Dictionary<long, Fixed> _damagePerFrame = [];
    private readonly Dictionary<int, Fixed> _health = [];

    private void RememberSighting(Entity enemy) => _sightings[enemy.Id.Value] = (enemy.Template.Index, _tick);

    /// <summary>The enemy's force as this player knows it: how many objects of each type it has seen within the memory, in the order of the type index.</summary>
    private SortedDictionary<int, int> EnemyForce()
    {
        List<uint>? stale = null;
        var force = new SortedDictionary<int, int>();
        var memory = _time.OriginalFrames(_strength.SightingFrames);
        foreach (var (id, sighting) in _sightings)
        {
            if (_tick - sighting.Seen > memory) (stale ??= []).Add(id);
            else force[sighting.Template] = force.GetValueOrDefault(sighting.Template) + 1;
        }
        if (stale is not null)
            foreach (var id in stale) _sightings.Remove(id);
        return force;
    }

    /// <summary>The index of the unit to build among <paramref name="units"/>: drawn by exchange value against the enemy seen, uniformly without knowledge. Always one draw from the AI's stream.</summary>
    private int DrawByExchangeValue(List<ObjectTemplate> units)
    {
        var force = EnemyForce();
        var values = new Fixed[units.Count];
        var total = Fixed.Zero;
        if (force.Count > 0)
            for (var index = 0; index < units.Count; index++)
            {
                values[index] = ExchangeValue(units[index], force);
                total += values[index];
            }
        if (total <= Fixed.Zero)
        {
            Stats.UninformedDraws++;
            return _random.Range(0, units.Count - 1);
        }

        Stats.InformedDraws++;
        var floor = DrawScale / VarietyShare / units.Count;
        var rest = Fixed.FromInt(DrawScale - DrawScale / VarietyShare);
        var weights = new int[units.Count];
        var sum = 0;
        for (var index = 0; index < units.Count; index++)
        {
            weights[index] = floor + (int)Fixed.MultiplyDivide(rest, values[index], total).Floor();
            sum += weights[index];
        }
        var roll = _random.Range(0, sum - 1);
        for (var index = 0; index < units.Count; index++)
        {
            if (roll < weights[index]) return index;
            roll -= weights[index];
        }
        return units.Count - 1;
    }

    private Fixed ExchangeValue(ObjectTemplate unit, SortedDictionary<int, int> force)
    {
        var offence = Fixed.Zero;
        var exposure = Fixed.One / Fixed.FromInt(LifetimeFrames);
        var health = HealthOf(unit);
        foreach (var (index, count) in force)
        {
            var enemy = _catalog[index];
            var enemyHealth = HealthOf(enemy);
            // A share of at most one object per frame: an instant-kill weapon is worth one kill, and the sums stay far inside the number range.
            if (enemyHealth > Fixed.Zero) offence += Fixed.FromInt(count) * Fixed.Max(enemy.BuildCost, Fixed.One) * Share(DamagePerFrame(unit, enemy), enemyHealth);
            if (health > Fixed.Zero) exposure += Fixed.FromInt(count) * Share(DamagePerFrame(enemy, unit), health);
        }
        return offence / (exposure * Fixed.Max(unit.BuildCost, Fixed.One));
    }

    private static Fixed Share(Fixed damage, Fixed health) => damage >= health ? Fixed.One : damage / health;

    private Fixed HealthOf(ObjectTemplate template)
    {
        if (_health.TryGetValue(template.Index, out var known)) return known;
        var health = Fixed.Zero;
        foreach (var module in template.Modules)
            if (module.Data is BodyData body) health = body.MaxHealth;
        _health[template.Index] = health;
        return health;
    }

    /// <summary>Damage per logic frame of the original of <paramref name="attacker"/>'s best default weapon against <paramref name="victim"/>; zero when it has none that may be fired at it.</summary>
    private Fixed DamagePerFrame(ObjectTemplate attacker, ObjectTemplate victim)
    {
        var key = ((long)attacker.Index << 32) | (uint)victim.Index;
        if (_damagePerFrame.TryGetValue(key, out var known)) return known;
        var best = Fixed.Zero;
        if (attacker.DefaultWeaponSet is { } set)
        {
            var wanted = victim.IsKindOf("AIRCRAFT") ? WeaponInfo.AntiMaskAirborneVehicle : WeaponInfo.AntiMaskGround;
            foreach (var weaponIndex in set.Weapons)
            {
                if (weaponIndex < 0) continue;
                var weapon = _catalog.Weapons[weaponIndex];
                var type = weapon.DamageType;
                if ((weapon.AntiMask & wanted) == 0 || DamageTypes.IsSubdual(type) || type is DamageTypes.Healing or DamageTypes.Status or DamageTypes.KillPilot or DamageTypes.KillGarrisoned) continue;
                var damage = victim.DefaultArmor.Adjust(type, Fixed.Min(weapon.PrimaryDamage, DamageCap));
                if (damage <= Fixed.Zero) continue;
                // Shots per logic frame: one per delay, or a clip per clip and reload. The catalog holds both in ticks of its rate, on the original's frame grid.
                // A divisor in frames, not a duration: a weapon without a delay fires once per frame.
                var perShot = _time.FramesIn(weapon.DelayBetweenShotsTicks);
                if (perShot <= 0) perShot = 1;
                var reload = _time.FramesIn(weapon.ClipReloadTicks);
                Fixed perFrame;
                if ((weapon.AffectsMask & WeaponInfo.KillsSelf) != 0) perFrame = damage / Fixed.FromInt(LifetimeFrames);
                else if (weapon.ClipSize > 0) perFrame = damage * Fixed.FromInt(weapon.ClipSize) / Fixed.FromInt(weapon.ClipSize * perShot + reload);
                else perFrame = damage / Fixed.FromInt(perShot);
                if (perFrame > best) best = perFrame;
            }
        }
        _damagePerFrame[key] = best;
        return best;
    }
}
