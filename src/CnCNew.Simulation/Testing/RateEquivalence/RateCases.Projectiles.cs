using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    /// <summary>The object every projectile of this group is launched from and at: a heavy tank, so that a hit never kills.</summary>
    private const string ProjectilePlatform = "ChinaTankOverlord";

    private const int ScatterShots = 40;

    private static string ProjectileKind(ObjectTemplate projectile)
    {
        foreach (var module in projectile.Modules)
            if (module.Name is "MissileAIUpdate" or "NeutronMissileUpdate" or "DumbProjectileBehavior") return module.Name;
        return "other";
    }

    private static RateCaseGroup ProjectileCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, Projectiles);
        var catalog = catalogs.Reference;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var withProjectile = 0;
        var notInstantiable = 0;
        var byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var weapon in catalog.Weapons.Weapons.OrderBy(weapon => weapon.Name, StringComparer.Ordinal))
        {
            if (weapon.ProjectileObject.Length == 0) continue;
            withProjectile++;
            if (!catalog.TryGet(weapon.ProjectileObject, out var projectile) || !projectile.IsInstantiable)
            {
                notInstantiable++;
                continue;
            }
            static string Raw(Fixed value) => value.Raw.ToString(CultureInfo.InvariantCulture);
            var key = string.Join(',', weapon.ProjectileObject, Raw(weapon.WeaponSpeed), Raw(weapon.MinWeaponSpeed), weapon.ScaleWeaponSpeed ? "s" : "-", Raw(weapon.ScatterRadius), Raw(weapon.AttackRange), Raw(weapon.MinimumAttackRange));
            if (!seen.Add(key)) continue;
            var kind = ProjectileKind(projectile);
            byKind[kind] = byKind.GetValueOrDefault(kind) + 1;
            var name = weapon.Name;
            builder.Add($"{kind}/{name}", scene => MeasureProjectile(scene, name), ProjectilePlatform);
        }
        var kinds = string.Join(", ", byKind.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Value} {pair.Key}")));
        builder.Note(string.Create(CultureInfo.InvariantCulture,
            $"{withProjectile} weapons launch a projectile object ({notInstantiable} of them a projectile that is not instantiable); {seen.Count} distinct by projectile, speed, scatter and range: {kinds}; {withProjectile - notInstantiable - seen.Count} merged away"));
        builder.Note($"every projectile is launched by the world's weapon routine (SimulationWorld.FireWeaponAt) from a standing {ProjectilePlatform}, not from its retail carrier: the launch point is the tank's centre");
        builder.NotCovered("projectiles: launch points on weapon bones and the launcher's own motion (aircraft); shots without a projectile object are the flight times of the group weapons");
        return builder.Done();
    }

    /// <summary>One shot, followed from the weapon routine to the impact.</summary>
    private sealed class Shot
    {
        public long FlightSteps { get; set; } = -1;
        public Fixed ImpactX { get; set; }
        public Fixed ImpactY { get; set; }
        public Fixed VictimX { get; set; }
        public Fixed VictimY { get; set; }
        public bool Landed { get; set; }
        public Fixed Apex { get; set; }
        public Fixed Path { get; set; }
    }

    private static Shot Follow(RateScene scene, Entity source, WeaponInfo weapon, Entity? victim, Fixed x, Fixed y, long budgetFrames)
    {
        var shot = new Shot();
        scene.Align();
        scene.World.FireWeaponAt(source, weapon, victim, x, y, Fixed.Zero);
        Entity? projectile = null;
        var (lastX, lastY, lastZ) = (source.X, source.Y, source.Z + source.Template.CenterZDelta);
        var (seenX, seenY, seenZ) = (source.X, source.Y, source.Z + source.Template.CenterZDelta);
        var budget = scene.Time.OriginalFrames(budgetFrames);
        for (long step = 1; step <= budget; step++)
        {
            var result = scene.Step();
            foreach (var item in result.Events)
            {
                if (item.Kind == SimEventKind.ProjectileLaunched && item.Other == source.Id && item.Text == weapon.Name && item.Entity.IsValid) projectile ??= scene.World.FindEntity(item.Entity);
                if (item.Kind != SimEventKind.WeaponImpact || item.Other != source.Id || item.Text != weapon.Name) continue;
                shot.FlightSteps = step;
                shot.ImpactX = item.X;
                shot.ImpactY = item.Y;
                if (victim is not null) (shot.VictimX, shot.VictimY, _) = victim.FrameEnd(scene.World.Tick);
            }
            if (projectile is not null && scene.World.FindEntity(projectile.Id) == projectile)
            {
                // Where its frame ends: a missile that dies without an impact event was last seen a frame before at 30 Hz, and only a tick before above it otherwise.
                (seenX, seenY, seenZ) = projectile.FrameEnd(scene.World.Tick);
                // The victim where the projectile's flight ended: what its blast does to the victim afterwards is not the impact.
                if (victim is not null && !shot.Landed) (shot.VictimX, shot.VictimY, _) = victim.FrameEnd(scene.World.Tick);
                shot.Landed |= projectile.EffectivelyDead;
                shot.Apex = Fixed.Max(shot.Apex, projectile.Z);
                // The path is sampled once per frame of the original: the same chords at every rate.
                if (scene.Time.IsOriginalFrameBoundary(scene.World.Tick))
                {
                    var (dx, dy, dz) = (projectile.X - lastX, projectile.Y - lastY, projectile.Z - lastZ);
                    shot.Path += Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
                    (lastX, lastY, lastZ) = (projectile.X, projectile.Y, projectile.Z);
                }
            }
            else if (projectile is not null && shot.FlightSteps < 0)
            {
                // A projectile that ends without the world's impact event (the neutron missile deals its damage itself): its removal ends the flight, where it was last seen.
                shot.FlightSteps = step;
                shot.ImpactX = seenX;
                shot.ImpactY = seenY;
            }
            if (shot.FlightSteps >= 0) break;
        }
        // The rest of the last frame of the original, when the flight ended inside one.
        var (restX, restY, restZ) = (seenX - lastX, seenY - lastY, seenZ - lastZ);
        shot.Path += Fixed.Sqrt(restX * restX + restY * restY + restZ * restZ);
        return shot;
    }

    private static ImmutableArray<RateQuantity> MeasureProjectile(RateScene scene, string weaponName)
    {
        var quantities = new RateQuantities(scene);
        if (!scene.Catalogs.Reference.Weapons.TryGet(weaponName, out var reference)) throw new InvalidOperationException($"Unknown weapon {weaponName}.");
        if (!scene.Catalog.Weapons.TryGet(weaponName, out var weapon)) throw new InvalidOperationException($"Unknown weapon {weaponName}.");
        // The same distance rule as the weapons: six tenths of the range (at most 1,000 units), or the middle between the minimum range and the range.
        var far = Fixed.Min(reference.AttackRange, Fixed.FromInt(1000));
        var distance = reference.MinimumAttackRange > Fixed.Zero ? (reference.MinimumAttackRange + Fixed.Max(far, reference.MinimumAttackRange + Fixed.FromInt(40))) / Fixed.FromInt(2) : far * 6 / Fixed.FromInt(10);
        distance = Fixed.Max(distance, Fixed.FromInt(30));
        var aimX = Fixed.FromInt(1000) + distance;
        var aimY = Fixed.FromInt(2000);

        // (1) at a point on the ground
        {
            var source = scene.Spawn(ProjectilePlatform, 1, 1000, 2000);
            var shot = Follow(scene, source, weapon, null, aimX, aimY, RateScene.Seconds(90));
            var hit = shot.FlightSteps >= 0;
            quantities.Event("ground point: flight time", Elapsed(shot.FlightSteps));
            quantities.Distance("ground point: impact distance from the aim point", RateMeasure.Distance(shot.ImpactX, shot.ImpactY, aimX, aimY), hit);
            quantities.Distance("ground point: apex height", shot.Apex, hit);
            quantities.Distance("ground point: path length", shot.Path, hit);
        }

        // (2) at a standing victim
        {
            var standing = scene.Fresh();
            var source = standing.Spawn(ProjectilePlatform, 1, 1000, 2000);
            var victim = standing.Spawn(ProjectilePlatform, 2, aimX, aimY);
            var shot = Follow(standing, source, weapon, victim, aimX, aimY, RateScene.Seconds(90));
            var hit = shot.FlightSteps >= 0;
            quantities.Event("standing victim: flight time", Elapsed(shot.FlightSteps));
            // A blast may have thrown the victim in the frame of the impact: it is read where that frame ends, as the moving victim below.
            quantities.Distance("standing victim: impact distance from the victim", RateMeasure.Distance(shot.ImpactX, shot.ImpactY, shot.VictimX, shot.VictimY), hit);
        }

        // (3) at a victim that drives across the line of fire (a guided projectile follows, a ballistic one lands where the victim was)
        {
            var moving = scene.Fresh();
            var source = moving.Spawn(ProjectilePlatform, 1, 1000, 2000);
            var victim = moving.Spawn(ProjectilePlatform, 2, aimX, aimY);
            moving.Align();
            moving.Move(victim, aimX, aimY + Fixed.FromInt(600));
            moving.StepFrames(RateScene.Seconds(2));
            var shot = Follow(moving, source, weapon, victim, victim.X, victim.Y, RateScene.Seconds(90));
            var hit = shot.FlightSteps >= 0;
            quantities.Event("moving victim: flight time", Elapsed(shot.FlightSteps));
            // The impact falls on the first tick of a logic frame of the original, in which the victim has covered one share of that frame's step; the 30 Hz world shows it
            // after the whole step. The victim is read where its frame ends (Entity.FrameEnd; at 30 Hz its position), so the suite adds no offset below one frame.
            quantities.Distance("moving victim: impact distance from the victim", RateMeasure.Distance(shot.ImpactX, shot.ImpactY, shot.VictimX, shot.VictimY), hit);
        }

        // (4) scatter: forty shots launched in one step at the same point, the mean distance of the impacts from it
        if (reference.ScatterRadius > Fixed.Zero)
        {
            var scattered = scene.Fresh();
            var source = scattered.Spawn(ProjectilePlatform, 1, 1000, 2000);
            scattered.Align();
            for (var index = 0; index < ScatterShots; index++) scattered.World.FireWeaponAt(source, weapon, null, aimX, aimY, Fixed.Zero);
            var impacts = 0;
            var sum = Fixed.Zero;
            long last = -1;
            var budget = scattered.Time.OriginalFrames(RateScene.Seconds(90));
            for (long step = 1; step <= budget && impacts < ScatterShots; step++)
                foreach (var item in scattered.Step().Events)
                {
                    if (item.Kind != SimEventKind.WeaponImpact || item.Other != source.Id || item.Text != weaponName) continue;
                    impacts++;
                    sum += RateMeasure.Distance(item.X, item.Y, aimX, aimY);
                    last = step;
                }
            quantities.Count("scatter: impacts of forty shots", impacts);
            quantities.Distance("scatter: mean impact distance from the aim point", impacts > 0 ? sum / Fixed.FromInt(impacts) : Fixed.Zero, impacts > 0);
            quantities.Event("scatter: last impact", Elapsed(last));
        }
        return quantities.Done();
    }
}
