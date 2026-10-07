using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    /// <summary>
    /// The standard victims, tried in this order: an armoured vehicle, a soldier, a structure, and a helicopter for weapons that only reach the air. The first one
    /// the weapon may be fired at stands for the weapon (<c>SimulationWorld.ChooseWeapon</c>: anti mask and a damage above zero against its armor).
    /// </summary>
    private static readonly string[] StandardVictims = ["ChinaTankOverlord", "ChinaInfantryRedguard", "AmericaPowerPlant", "AmericaVehicleComanche"];

    private const string AirVictim = "AmericaVehicleComanche";

    private static string WeaponKey(WeaponInfo weapon)
    {
        static string Raw(Fixed value) => value.Raw.ToString(CultureInfo.InvariantCulture);
        static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        return string.Join(',', Number(weapon.DelayBetweenShotsTicks), Number(weapon.ClipSize), Number(weapon.ClipReloadTicks), Number(weapon.PreAttackDelayTicks), weapon.PreAttackType,
            Raw(weapon.PrimaryDamage), Raw(weapon.PrimaryRadius), Raw(weapon.SecondaryDamage), Raw(weapon.SecondaryRadius), Number(weapon.DamageType), Raw(weapon.AttackRange), Raw(weapon.MinimumAttackRange),
            Raw(weapon.WeaponSpeed), Raw(weapon.MinWeaponSpeed), weapon.ScaleWeaponSpeed ? "s" : "-", weapon.ProjectileObject, Raw(weapon.ScatterRadius), Raw(weapon.ScatterRadiusVsInfantry),
            Number(weapon.ContinuousFireOneShots), Number(weapon.ContinuousFireTwoShots), Number(weapon.ContinuousFireCoastTicks), Number(weapon.AntiMask), weapon.ReloadsAtBase ? "b" : "-",
            weapon.DamageDealtAtSelfPosition ? "self" : "-", Number(weapon.AffectsMask), weapon.Bonuses.IsEmpty ? "-" : "bonus:" + weapon.Name);
    }

    private static bool IsAircraft(ObjectTemplate template) => template.Modules.Any(module => module.Name is "JetAIUpdate" or "DeliverPayloadAIUpdate" or "ChinookAIUpdate") || template.Locomotor is { IsWings: true };

    private static RateCaseGroup WeaponCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, Weapons);
        var catalog = catalogs.Reference;
        // Carriers standing on the ground first (their shots do not depend on a flight path), aircraft after them; by name within each.
        var carriers = catalog.Templates.Where(template => template.IsInstantiable && !template.WeaponSets.IsDefaultOrEmpty && !template.IsKindOf("PROJECTILE"))
            .OrderBy(template => IsAircraft(template) ? 1 : 0).ThenBy(template => template.Name, StringComparer.Ordinal).ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var carried = new HashSet<int>();
        var unreachable = new HashSet<int>();
        var chosen = 0;
        var airborne = 0;
        // Two passes: the weapons of the default sets first, then those only a veterancy, upgrade or crate set holds.
        foreach (var conditional in new[] { false, true })
            foreach (var carrier in carriers)
                foreach (var set in carrier.WeaponSets)
                {
                    if (set.Conditions != 0 != conditional) continue;
                    var reachable = ReachableWeaponSet(carrier, set);
                    for (var slot = 0; slot < WeaponSlot.Count; slot++)
                    {
                        if (!set.HasWeapon(slot)) continue;
                        var weapon = catalog.Weapons[set.Weapons[slot]];
                        if (!reachable)
                        {
                            unreachable.Add(weapon.Index);
                            continue;
                        }
                        carried.Add(weapon.Index);
                        if (!seen.Add(WeaponKey(weapon))) continue;
                        chosen++;
                        if (IsAircraft(carrier)) airborne++;
                        var (carrierName, weaponName, weaponSlot, flags) = (carrier.Name, weapon.Name, slot, set.Conditions);
                        var id = flags == 0 ? $"{weaponName}@{carrierName}" : string.Create(CultureInfo.InvariantCulture, $"{weaponName}@{carrierName}+set{flags}");
                        builder.Add(id, scene => MeasureWeapon(scene, carrierName, weaponName, weaponSlot, flags), carrierName);
                    }
                }
        unreachable.ExceptWith(carried);
        builder.Note(string.Create(CultureInfo.InvariantCulture,
            $"{catalog.Weapons.Weapons.Length} weapon templates, {carried.Count} of them in a default, veterancy, player-upgrade or crate weapon set of an instantiable object; {chosen} distinct by timing, damage, range, speed and projectile " +
            $"(the first carrier by name stands for each, default sets and ground carriers first; {airborne} only on aircraft), {carried.Count - chosen} merged away"));
        builder.NotCovered(string.Create(CultureInfo.InvariantCulture,
            $"weapons: {unreachable.Count} weapon templates only in weapon sets the suite cannot switch on (car bomb, hijack, mine clearing, rider sets) and {catalog.Weapons.Weapons.Length - carried.Count - unreachable.Count} that no object carries in a weapon set (fired by modules, creation lists and special powers; the modules that fire them are cases of the groups projectiles, overtime and timed where staged)"));
        builder.NotCovered("weapons: weapon bonuses of battle plans and of upgrades that do not switch the weapon set; a veterancy set is fired with the veterancy bonus of its level");
        builder.NotCovered("weapons: the effective range of weapons carried only by aircraft (the carrier moves; its flight is the subject of the group flight)");
        builder.NotCovered("weapons: weapons that only reach projectiles, mines or parachutes (no standard victim); they appear with every quantity unmeasured");
        return builder.Done();
    }

    private const int VeterancyBits = 0b111, PlayerUpgradeBit = 3, CrateOneBit = 4, CrateTwoBit = 5;

    /// <summary>Whether the suite can make <paramref name="set"/> the active set of a freshly spawned carrier: by its veterancy level, the player-upgrade flag and the crate level.</summary>
    private static bool ReachableWeaponSet(ObjectTemplate carrier, WeaponSetInfo set)
    {
        var flags = set.Conditions;
        if (flags == 0) return carrier.DefaultWeaponSet == set;
        if ((flags & ~(uint)(VeterancyBits | 1 << PlayerUpgradeBit | 1 << CrateOneBit | 1 << CrateTwoBit)) != 0) return false;
        if (System.Numerics.BitOperations.PopCount(flags & VeterancyBits) > 1 || (flags & (1u << CrateOneBit)) != 0 && (flags & (1u << CrateTwoBit)) != 0) return false;
        return WeaponSetResolver.Find(carrier.WeaponSets, flags) == set;
    }

    /// <summary>Spawns a carrier and switches the weapon set with the given conditions on (0: the default set).</summary>
    private static Entity SpawnCarrier(RateScene scene, string name, uint flags, int x, int y)
    {
        var carrier = scene.Spawn(name, 1, x, y);
        if (flags == 0) return carrier;
        var level = (flags & VeterancyBits) switch { 1 => 1, 2 => 2, 4 => 3, _ => 0 };
        if (level > 0) scene.World.SetVeterancy(carrier.Id, level);
        if ((flags & (1u << PlayerUpgradeBit)) != 0) carrier.HasPlayerUpgradeWeapon = true;
        if ((flags & (1u << CrateOneBit)) != 0) carrier.CrateWeaponLevel = 1;
        if ((flags & (1u << CrateTwoBit)) != 0) carrier.CrateWeaponLevel = 2;
        scene.World.MarkDirty(carrier);
        scene.StepFrames(1);
        return carrier;
    }

    private static void Heal(RateScene scene, Entity entity)
        => scene.Order(0, (_, sequence, now) => new DamageCommand(Ids.PlayerId.System, sequence, now, entity.Id, entity.Body!.MaxHealth, DamageTypes.Healing, 0));

    private static bool MayTarget(RateScene scene, Entity source, Entity victim, int slot)
        => scene.World.ChooseWeapon(source, victim, FireOrigin.Player, slot).Rejection is not (CommandRejection.InvalidWeaponTarget or CommandRejection.NoWeapon);

    /// <summary>The first standard victim the weapon may be fired at, found in a world of its own; null when there is none.</summary>
    private static string? ChooseVictim(RateScene scene, string carrier, int slot, uint flags)
    {
        var probe = scene.Fresh();
        var source = SpawnCarrier(probe, carrier, flags, 1000, 1000);
        var victims = new List<Entity>();
        for (var index = 0; index < StandardVictims.Length; index++)
            if (probe.Has(StandardVictims[index])) victims.Add(probe.Spawn(StandardVictims[index], 2, 1500, 600 + index * 300));
        // The helicopter is an airborne target once it has climbed.
        probe.StepFrames(RateScene.Seconds(6));
        foreach (var victim in victims)
            if (probe.Alive(victim) && probe.Alive(source) && MayTarget(probe, source, victim, slot)) return victim.Template.Name;
        return null;
    }

    private static ImmutableArray<RateQuantity> MeasureWeapon(RateScene scene, string carrierName, string weaponName, int slot, uint flags)
    {
        var quantities = new RateQuantities(scene);
        if (!scene.Catalogs.Reference.Weapons.TryGet(weaponName, out var reference)) throw new InvalidOperationException($"Unknown weapon {weaponName}.");
        var victimName = ChooseVictim(scene, carrierName, slot, flags);
        if (victimName is null) return UnmeasuredWeapon(quantities);

        // Distance: six tenths of the range, between the minimum range and the range for a weapon with a minimum, never more than 600 units (the 30 Hz data decide, the same at every rate).
        var far = Fixed.Min(reference.AttackRange, Fixed.FromInt(1000));
        var distance = reference.MinimumAttackRange > Fixed.Zero ? (reference.MinimumAttackRange + Fixed.Max(far, reference.MinimumAttackRange + Fixed.FromInt(40))) / Fixed.FromInt(2) : far * 6 / Fixed.FromInt(10);
        var source = SpawnCarrier(scene, carrierName, flags, 1000, 2000);
        var victim = scene.Spawn(victimName, 2, Fixed.FromInt(1000) + distance, Fixed.FromInt(2000));
        if (victimName == AirVictim) scene.StepFrames(RateScene.Seconds(6));
        scene.Align();

        var shots = new List<long>();
        var damage = Fixed.Zero;
        long firstDamage = -1;
        var start = scene.World.Tick;
        var end = start + scene.Time.WholeSecondsToTicks(60);
        while (scene.World.Tick < end)
        {
            if (scene.Alive(source) && scene.Alive(victim))
            {
                if (scene.Time.IsOriginalFrameBoundary(scene.World.Tick) && victim.Body is { } body && body.Health * 2 < body.MaxHealth) Heal(scene, victim);
                scene.Fire(source, victim, slot);
            }
            var result = scene.Step();
            foreach (var item in result.Events)
            {
                if (item.Kind == SimEventKind.WeaponFired && item.Entity == source.Id && item.Text == weaponName) shots.Add(item.Tick - start);
                if (item.Kind != SimEventKind.EntityDamaged || item.Entity != victim.Id) continue;
                damage += Fixed.FromRaw(item.Value);
                if (firstDamage < 0 && shots.Count > 0) firstDamage = item.Tick - start - shots[0];
            }
        }

        var clip = reference.ClipSize;
        // A carrier with a turret turns it to the victim first: the first shot is then an event of movement, not a duration from the data.
        var turreted = scene.Reference(carrierName).Turrets is not null;
        if (turreted) quantities.Event("order to first shot (after the turret traverse)", shots.Count > 0 ? shots[0] : -1);
        else quantities.Exact("order to first shot", shots.Count > 0 ? shots[0] : -1);
        // A quantity that does not exist for this weapon at this rate (no second shot, no clip, fewer than twenty shots) is left out; where another rate has it, its absence here fails.
        if (shots.Count > 1 && clip != 1) quantities.Exact("shot spacing", shots[1] - shots[0]);
        if (clip > 1 && shots.Count >= clip) quantities.Exact("first clip, first to last shot", shots[clip - 1] - shots[0]);
        if (clip > 0 && shots.Count > clip) quantities.Exact("reload", shots[clip] - shots[clip - 1]);
        if (clip > 0 && shots.Count > 2 * clip) quantities.Exact("second reload", shots[2 * clip] - shots[2 * clip - 1]);
        if (shots.Count >= 20) quantities.Exact("time of the 20th shot", shots[19] - shots[0]);
        if (shots.Count > 1) quantities.Exact("time of the last shot within 60 s", shots[^1] - shots[0]);
        quantities.Count("shots in 60 s", shots.Count);
        quantities.Amount("damage in 60 s", damage);
        if (firstDamage >= 0) quantities.Event("first shot to first damage", firstDamage);
        if (!IsAircraft(scene.Reference(carrierName))) quantities.Distance("effective range", EffectiveRange(scene, carrierName, victimName, slot, flags, reference));
        return quantities.Done();
    }

    /// <summary>No standard victim: the weapon is listed with its shots unmeasured.</summary>
    private static ImmutableArray<RateQuantity> UnmeasuredWeapon(RateQuantities quantities)
        => quantities.Count("shots in 60 s", 0, false).Amount("damage in 60 s", Fixed.Zero, false).Done();

    /// <summary>
    /// The largest centre distance at which the world lets the weapon fire at the standard victim, by bisection to a sixteenth of a unit (a fresh victim per
    /// step; the question is the one a fire order is validated with). Capped at 2,800 units, the room of the map.
    /// </summary>
    private static Fixed EffectiveRange(RateScene scene, string carrierName, string victimName, int slot, uint flags, WeaponInfo reference)
    {
        var probe = scene.Fresh();
        var source = SpawnCarrier(probe, carrierName, flags, 1000, 2000);
        bool Reaches(Fixed distance)
        {
            var victim = probe.Spawn(victimName, 2, Fixed.FromInt(1000) + distance, Fixed.FromInt(2000));
            var reaches = probe.Alive(source) && probe.World.ChooseWeapon(source, victim, FireOrigin.Player, slot).Rejection != CommandRejection.OutOfRange;
            probe.Kill(victim);
            probe.StepFrames(1);
            return reaches;
        }
        var cap = Fixed.FromInt(2800);
        var high = Fixed.Min(cap, reference.AttackRange * 2 + Fixed.FromInt(50));
        var low = Fixed.Min(high, Fixed.Max(reference.MinimumAttackRange + Fixed.FromInt(1), Fixed.FromInt(1)));
        if (victimName == AirVictim) return reference.AttackRange >= cap ? cap : BisectAir(scene, carrierName, slot, flags, low, high);
        if (!Reaches(low)) return Fixed.Zero;
        if (Reaches(high)) return high;
        var step = Fixed.FromFraction(1, 16);
        while (high - low > step)
        {
            var middle = (low + high) / Fixed.FromInt(2);
            if (Reaches(middle)) low = middle;
            else high = middle;
        }
        return low;
    }

    /// <summary>The same for the airborne victim, which has to climb first: every step of the bisection waits six seconds in a world of its own.</summary>
    private static Fixed BisectAir(RateScene scene, string carrierName, int slot, uint flags, Fixed low, Fixed high)
    {
        bool Reaches(Fixed distance)
        {
            var probe = scene.Fresh();
            var source = SpawnCarrier(probe, carrierName, flags, 1000, 2000);
            var victim = probe.Spawn(AirVictim, 2, Fixed.FromInt(1000) + distance, Fixed.FromInt(2000));
            probe.StepFrames(RateScene.Seconds(6));
            return probe.Alive(source) && probe.Alive(victim) && probe.World.ChooseWeapon(source, victim, FireOrigin.Player, slot).Rejection != CommandRejection.OutOfRange;
        }
        if (!Reaches(low)) return Fixed.Zero;
        if (Reaches(high)) return high;
        var step = Fixed.FromInt(1);
        while (high - low > step)
        {
            var middle = (low + high) / Fixed.FromInt(2);
            if (Reaches(middle)) low = middle;
            else high = middle;
        }
        return low;
    }
}
