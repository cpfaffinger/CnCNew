using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    private const string TurretVictim = "ChinaTankOverlord";

    private static string TurretKey(TurretInfo turret)
    {
        static string Raw(Fixed value) => value.Raw.ToString(CultureInfo.InvariantCulture);
        static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        return string.Join(',', Raw(turret.TurnRate), Raw(turret.PitchRate), Raw(turret.NaturalAngle), Raw(turret.NaturalPitch), Raw(turret.FirePitch), Number(turret.RecenterTicks), turret.AllowsPitch ? "p" : "-",
            turret.FiresWhileTurning ? "f" : "-", Raw(turret.MinScanAngle), Raw(turret.MaxScanAngle), Number(turret.MinScanIntervalTicks), Number(turret.MaxScanIntervalTicks),
            string.Join('/', turret.SweepAngle.Select(Raw)), string.Join('/', turret.SweepSpeed.Select(Raw)));
    }

    /// <summary>The lowest weapon slot of the default set the first turret controls and that reaches the ground, or -1.</summary>
    private static int TurretSlot(ObjectTemplate template, WeaponCatalog weapons)
    {
        if (template.Turrets?[0] is not { Gates: true } turret || template.DefaultWeaponSet is not { } set) return -1;
        for (var slot = 0; slot < WeaponSlot.Count; slot++)
            if (set.HasWeapon(slot) && turret.Controls(slot) && (weapons[set.Weapons[slot]].AntiMask & WeaponInfo.AntiMaskGround) != 0) return slot;
        return -1;
    }

    private static RateCaseGroup TurretCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, Turrets);
        var catalog = catalogs.Reference;
        var candidates = catalog.Templates.Where(template => template.IsInstantiable && !IsAircraft(template) && TurretSlot(template, catalog.Weapons) >= 0)
            .OrderBy(template => template.Name, StringComparer.Ordinal).ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var template in candidates)
        {
            if (!seen.Add(TurretKey(template.Turrets![0]!))) continue;
            var (name, slot) = (template.Name, TurretSlot(template, catalog.Weapons));
            builder.Add(name, scene => MeasureTurret(scene, name, slot), name, TurretVictim);
        }
        builder.Note(string.Create(CultureInfo.InvariantCulture,
            $"{candidates.Length} retail ground objects aim a ground weapon with a turret; {seen.Count} distinct turrets (turn rate, recentre time, scan and sweep values), the first object by name stands for each, {candidates.Length - seen.Count} merged away"));
        builder.NotCovered("turrets: second turrets and linked turrets, pitch (anti-air aim) and turrets on aircraft; turrets that only aim anti-air weapons");
        return builder.Done();
    }

    private static readonly Fixed TurretAtRest = Fixed.FromFraction(1, 1000);

    private static ImmutableArray<RateQuantity> MeasureTurret(RateScene scene, string name, int slot)
    {
        var quantities = new RateQuantities(scene);
        var template = scene.Reference(name);
        var turretInfo = template.Turrets![0]!;
        var weapon = scene.Catalogs.Reference.Weapons[template.DefaultWeaponSet!.Weapons[slot]];
        var far = Fixed.Min(weapon.AttackRange, Fixed.FromInt(1000));
        var distance = weapon.MinimumAttackRange > Fixed.Zero ? (weapon.MinimumAttackRange + Fixed.Max(far, weapon.MinimumAttackRange + Fixed.FromInt(40))) / Fixed.FromInt(2) : far * 6 / Fixed.FromInt(10);

        // (1) and (2): the victim stands a quarter turn to the left and straight behind; the body looks east and does not move
        foreach (var (label, dx, dy) in new[] { ("traverse 90", 0, 1), ("traverse 180", -1, 0) })
        {
            var aiming = label == "traverse 90" ? scene : scene.Fresh();
            var source = aiming.Spawn(name, 1, 2000, 2000);
            var victim = aiming.Spawn(TurretVictim, 2, Fixed.FromInt(2000) + distance * dx, Fixed.FromInt(2000) + distance * dy);
            aiming.Align();
            long firstShot = -1;
            var angleAtShot = Fixed.Zero;
            var budget = aiming.Time.OriginalFrames(RateScene.Seconds(60));
            for (long step = 1; step <= budget && firstShot < 0; step++)
            {
                if (aiming.Alive(source) && aiming.Alive(victim)) aiming.Fire(source, victim, slot);
                foreach (var item in aiming.Step().Events)
                    if (item.Kind == SimEventKind.WeaponFired && item.Entity == source.Id && firstShot < 0)
                    {
                        firstShot = step - 1;
                        angleAtShot = source.TurretOf(0)?.Angle ?? Fixed.Zero;
                    }
            }
            quantities.Event(label + ": order to first shot", firstShot);
            quantities.Angle(label + ": turret angle at the first shot", angleAtShot, firstShot >= 0);
            if (label != "traverse 90" || firstShot < 0) continue;

            // return to rest: the victim is removed, the turret holds, turns back and comes to rest at its natural angle
            aiming.Kill(victim);
            aiming.Step();
            var held = aiming.StepUntil(() => source.TurretOf(0) is { } reading && (reading.Angle - angleAtShot).Abs() > TurretAtRest, RateScene.Seconds(60));
            var rested = aiming.StepUntil(() => source.TurretOf(0) is { Rotating: false } reading && FixedTrig.AngleDifference(reading.Angle, turretInfo.NaturalAngle).Abs() <= TurretAtRest, RateScene.Seconds(120));
            quantities.Event("return: victim gone to turret leaving its aim", held);
            quantities.Event("return: leaving the aim to rest", held >= 0 ? rested : -1);
        }

        // (3) the idle scan: nobody around for two minutes
        if (turretInfo.CanScan)
        {
            var idle = scene.Fresh();
            var source = idle.Spawn(name, 1, 2000, 2000);
            idle.Align();
            var scans = 0;
            long first = -1, third = -1;
            var largest = Fixed.Zero;
            var moving = false;
            var steps = idle.Time.WholeSecondsToTicks(120);
            for (long step = 1; step <= steps; step++)
            {
                idle.Step();
                if (source.TurretOf(0) is not { } reading) break;
                var away = FixedTrig.AngleDifference(turretInfo.NaturalAngle, reading.Angle).Abs();
                largest = Fixed.Max(largest, away);
                var now = away > TurretAtRest;
                if (now && !moving)
                {
                    scans++;
                    if (scans == 1) first = step;
                    if (scans == 3) third = step;
                }
                moving = now;
            }
            quantities.Exact("idle scan: start of the first glance", Elapsed(first));
            quantities.Event("idle scan: start of the third glance", Elapsed(third));
            quantities.Count("idle scan: glances in 120 s", scans);
            quantities.Angle("idle scan: largest angle from rest", largest);
        }
        return quantities.Done();
    }
}
