using System.Linq;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

/// <summary>
/// #304: a mover's look for standing units ahead (<c>StationaryBlockersAhead</c>) counted an object that was already destroyed in the tick - a pilot that had just climbed into its
/// vehicle (<c>VeterancyCrateCollide</c>), still in the world until the end of the tick. At 30 Hz the pilot's update destroyed it before the later mover looked, and the mover planned
/// a detour around it; above 30 Hz the look in the frame's first tick came before the pilot's step ended (Armored Fury America against China, a Crusader at frame 23,014).
/// </summary>
static partial class Tests
{
    /// <summary>A unit standing in a mover's way is a blocker ahead; once destroyed in the tick it is none, at 30, 60 and 120 Hz.</summary>
    public static void ADestroyedUnitIsNoStandingBlocker()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, F138Files(), NavTerrain());
            feeder.Spawn(1, "F138Gunner", owner: 1, x: 60, y: 100);
            feeder.Spawn(1, "F138Gunner", owner: 1, x: 90, y: 100);
            world.Step();
            F138ToFrameStart(world);
            var gunners = world.Entities.Where(value => value.Template.Name == "F138Gunner").OrderBy(value => value.Id.Value).ToList();
            var (mover, standing) = (gunners[0], gunners[1]);
            Check.That(world.StationaryBlockersAhead(mover, Fixed.FromInt(40), Fixed.Zero).Count == 1, $"{rate}: the unit standing ahead is a blocker.");
            world.RequestDestroy(standing.Id);
            Check.That(standing.DestroyRequested && !standing.EffectivelyDead, $"{rate}: setup - the unit is destroyed, not dead.");
            var ahead = world.StationaryBlockersAhead(mover, Fixed.FromInt(40), Fixed.Zero);
            Check.That(ahead.Count == 0, $"{rate}: a unit destroyed in this tick stands in nobody's way ({ahead.Count} blockers).");
        }
    }
}
