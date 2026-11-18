using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.World;

// #452: what the countermeasures of the original report and what their flares do, on real data.
static partial class Tests
{
    /// <summary>
    /// #452, the flares: a flare is an object of its own in the original (<c>CountermeasuresBehavior::launchVolley</c> makes it and gives its own <c>PhysicsBehavior</c> the plane's velocity
    /// and the push), so it flies on whatever becomes of the plane. A Comanche with <c>Upgrade_AmericaCountermeasures</c> launches its first volley over Tank Hunters; in the tick its first flare
    /// appears the Comanche is left alone, disabled by an EMP, or removed from the world, and 15 frames later the flare is in the same place in all three - before, it hung in the air while its
    /// plane was disabled or gone (its plane's module moved it). A world saved after five of those frames and restored flies the flare to the same place. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealCountermeasureFlaresFlyOnWithoutTheirPlane(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
        {
            var ends = new List<(Fixed X, Fixed Y, Fixed Z)>();
            foreach (var fate in new[] { "free", "disabled", "removed", "restored" })
            {
                var world = real.WorldWith(rate, RuleProfile.Product);
                world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_AmericaCountermeasures");
                var air = SpawnReal(world, ("AmericaVehicleComanche", 1, 300, 400)).Single();
                _ = SpawnReal(world, ("ChinaInfantryTankHunter", 2, 560, 380), ("ChinaInfantryTankHunter", 2, 560, 400), ("ChinaInfantryTankHunter", 2, 560, 420),
                    ("ChinaInfantryTankHunter", 2, 580, 390), ("ChinaInfantryTankHunter", 2, 580, 410));
                Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++_sequence, world.Tick, air.Id, Fixed.FromInt(420), Fixed.FromInt(400))).Accepted, $"{rate} {fate}: the Comanche flies out.");
                Entity? flare = null;
                for (var step = 0; step < world.Frames(30 * 40) && flare is null; step++)
                {
                    world.Step();
                    if (air.Body!.Health < air.Body.MaxHealth)
                        world.Submit(new DamageCommand(PlayerId.System, ++_sequence, world.Tick, air.Id, air.Body.MaxHealth, DamageTypes.Healing, 0));
                    flare = world.Entities.Where(value => value.Template.Name == "CountermeasureFlare").OrderBy(value => value.Id.Value).FirstOrDefault();
                }
                Check.That(flare is not null, $"{rate} {fate}: the Comanche launches flares.");
                if (fate == "disabled") world.DisableByEmpUntil(air, world.Tick + world.Frames(30 * 5));
                if (fate == "removed") world.RequestDestroy(air.Id);
                var start = (flare!.X, flare.Y, flare.Z);
                for (var step = 0; step < world.Frames(15); step++)
                {
                    if (fate == "restored" && step == world.Frames(5))
                    {
                        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), world.Templates, world.Configuration.DefinitionFingerprint, world.Terrain);
                        Check.That(restored.StateHash == world.StateHash, $"{rate}: a world saved while its flare flies restores to the same state.");
                        (world, flare) = (restored, restored.FindEntity(flare.Id)!);
                        air = world.FindEntity(air.Id)!;
                    }
                    world.Step();
                }
                if (fate == "disabled") Check.That(world.DisabledFlagsOf(air) != DisabledMask.None, $"{rate}: the Comanche is disabled.");
                if (fate == "removed") Check.That(world.FindEntity(air.Id) is null, $"{rate}: the Comanche is gone.");
                Check.That(!flare.DestroyRequested, $"{rate} {fate}: the flare still burns.");
                var (dx, dy) = (flare.X - start.X, flare.Y - start.Y);
                Check.That(flare.Z < start.Z - Fixed.FromInt(3) && dx * dx + dy * dy > Fixed.One,
                    $"{rate} {fate}: the flare flies on ({dx.ToDecimal():0.00}, {dy.ToDecimal():0.00} across, {start.Z.ToDecimal():0.00} -> {flare.Z.ToDecimal():0.00}).");
                ends.Add((dx, dy, flare.Z - start.Z));
            }
            Check.That(ends.Distinct().Count() == 1,
                $"{rate}: the flare flies the same way whether its plane is free, disabled or gone ({string.Join("; ", ends.Select(end => $"{end.X.ToDecimal():0.000}, {end.Y.ToDecimal():0.000}, {end.Z.ToDecimal():0.000}"))}).");
        }
    }
}
