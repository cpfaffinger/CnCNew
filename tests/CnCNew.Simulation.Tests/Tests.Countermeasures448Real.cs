using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.World;

// The owner's playtest of release 0.0.1, on real data.
static partial class Tests
{
    /// <summary>
    /// #448: "Comanches and Raptors do not visibly use countermeasures." A Comanche hovering over Tank Hunters and a Raptor circling over them, both with
    /// <c>Upgrade_AmericaCountermeasures</c> and kept alive, are shot at; the first missile a flare will take (<c>EvasionRate</c>) starts the volleys
    /// (<c>CountermeasuresBehavior::update</c>): <c>VolleySize</c> flares every <c>DelayBetweenVolleys</c>, <c>NumberOfVolleys</c> times, the Comanche's again after its
    /// <c>ReloadTime</c>, the Raptor's not before it lands (<c>ReloadTime = 0</c>). Each flare flies as the reference's physics flies it (<c>launchVolley</c>: the plane's velocity and a
    /// push of the speed times <c>VolleyVelocityFactor</c> over the flare's mass, -10 per frame for a plane that stands; <c>applyFrictionalForces</c> with the flare locomotor's
    /// <c>Extra2DFriction</c>): from the hovering Comanche the two flares are thrown 18 units back past its tail and 3.5 units to its sides - before they stayed within a unit of its
    /// pivot, under its hull; from the Raptor they fly on with it for a third of a second and stay close to its path - before they were flung up to 300 units ahead and aside. A flare
    /// sinks on after its plane has died (it stood still in the air). At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealCountermeasureFlaresFlyAsTheOriginal(string directory)
    {
        var real = new RealTargets(directory);
        foreach (var rate in TargetRates)
            foreach (var (name, hover) in new[] { ("AmericaVehicleComanche", true), ("AmericaJetRaptor", false) })
            {
                var world = real.WorldWith(rate, RuleProfile.Product);
                world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_AmericaCountermeasures");
                var air = SpawnReal(world, (name, 1, 300, 400)).Single();
                var countermeasures = air.Modules.OfType<CountermeasuresBehavior>().Single();
                Check.That(countermeasures.IsActive, $"{rate} {name}: the research gives it its countermeasures.");
                _ = SpawnReal(world, ("ChinaInfantryTankHunter", 2, 560, 380), ("ChinaInfantryTankHunter", 2, 560, 400), ("ChinaInfantryTankHunter", 2, 560, 420),
                    ("ChinaInfantryTankHunter", 2, 580, 390), ("ChinaInfantryTankHunter", 2, 580, 410));
                Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++_sequence, world.Tick, air.Id, Fixed.FromInt(420), Fixed.FromInt(400))).Accepted, $"{rate} {name}: it flies out.");

                var known = world.Entities.Select(value => value.Id).ToHashSet();
                // Per flare: the frame it appeared, where and with which heading of the plane, and its place 20 frames later.
                var flares = new List<(Entity Flare, long Frame, Fixed X, Fixed Y, Fixed Angle, Fixed PlaneSpeed)>();
                var offsets = new List<(Fixed Forward, Fixed Lateral, Fixed PlaneSpeed)>();
                var tracked = new HashSet<uint>();
                for (var step = 0; step < world.Frames(30 * 40); step++)
                {
                    var (planeX, planeY, planeZ) = world.VelocityOf(air);
                    var planeSpeed = Fixed.Sqrt(planeX * planeX + planeY * planeY + planeZ * planeZ);
                    world.Step();
                    if (air.Body!.Health < air.Body.MaxHealth)
                        world.Submit(new DamageCommand(PlayerId.System, ++_sequence, world.Tick, air.Id, air.Body.MaxHealth, DamageTypes.Healing, 0));
                    foreach (var entity in world.Entities)
                        if (known.Add(entity.Id) && entity.Template.Name == "CountermeasureFlare") flares.Add((entity, world.InFrames(world.Tick), entity.X, entity.Y, entity.Angle, planeSpeed));
                    foreach (var flare in flares)
                    {
                        if (world.InFrames(world.Tick) - flare.Frame != 20 || !tracked.Add(flare.Flare.Id.Value) || flare.Flare.DestroyRequested) continue;
                        var (dx, dy) = (flare.Flare.X - flare.X, flare.Flare.Y - flare.Y);
                        var (cos, sin) = (FixedTrig.Cos(flare.Angle), FixedTrig.Sin(flare.Angle));
                        offsets.Add((dx * cos + dy * sin, dy * cos - dx * sin, flare.PlaneSpeed));
                    }
                }
                var data = air.Template.Modules.Select(module => module.Data).OfType<CountermeasuresData>().Single();
                var frames = flares.Select(value => value.Frame).Distinct().ToList();
                var (incoming, diverted) = countermeasures.MissileCounts;
                Check.That(incoming > 0 && diverted > 0, $"{rate} {name}: missiles are reported, and some taken by flares ({diverted} of {incoming}).");
                var sequence = frames.Take(data.NumberOfVolleys).ToList();
                var between = world.InFrames(data.TicksBetweenVolleys);
                Check.That(sequence.Count == data.NumberOfVolleys && sequence.Zip(sequence.Skip(1)).All(pair => pair.Second - pair.First == between)
                    && flares.Count(value => sequence.Contains(value.Frame)) == data.NumberOfVolleys * data.VolleySize,
                    $"{rate} {name}: {data.NumberOfVolleys} volleys of {data.VolleySize} flares, {between} frames apart (volleys at {string.Join(", ", frames)}).");
                if (hover)
                    Check.That(frames.Count > data.NumberOfVolleys, $"{rate} {name}: after its reload time it launches again ({frames.Count} volleys).");
                else
                    Check.That(frames.Count == data.NumberOfVolleys, $"{rate} {name}: without an airfield its flares are not restocked ({frames.Count} volleys).");

                Check.That(offsets.Count >= data.VolleySize, $"{rate} {name}: the flares are followed for 20 frames ({offsets.Count}).");
                foreach (var (forward, lateral, planeSpeed) in offsets)
                {
                    if (hover)
                    {
                        // -10 per frame times 2 over a mass of 5 at 60 degrees off the back: 2 units a frame backwards for the nine motive frames of flight, 3.5 aside for one.
                        Check.That(forward < Fixed.FromInt(-16) && forward > Fixed.FromInt(-20) && lateral.Abs() > Fixed.FromInt(3) && lateral.Abs() < Fixed.FromInt(4),
                            $"{rate} {name}: a flare is thrown back past the tail ({forward.ToDecimal():0.00} along, {lateral.ToDecimal():0.00} across the heading).");
                    }
                    else
                    {
                        // The plane's velocity for the motive frames, at most the push's 0.4 times its speed aside for one frame.
                        Check.That(forward > Fixed.Zero && forward < planeSpeed * Fixed.FromInt(14) && lateral.Abs() < planeSpeed / Fixed.FromInt(2) + Fixed.FromInt(2),
                            $"{rate} {name}: a flare flies on with the jet and stays by its path ({forward.ToDecimal():0.00} along, {lateral.ToDecimal():0.00} across, the jet at {planeSpeed.ToDecimal():0.00} a frame).");
                    }
                }
            }

        // A flare outlives its plane: the Comanche dies right after a volley, its flares sink on.
        foreach (var rate in TargetRates)
        {
            var world = real.WorldWith(rate, RuleProfile.Product);
            world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_AmericaCountermeasures");
            var air = SpawnReal(world, ("AmericaVehicleComanche", 1, 300, 400)).Single();
            _ = SpawnReal(world, ("ChinaInfantryTankHunter", 2, 560, 380), ("ChinaInfantryTankHunter", 2, 560, 400), ("ChinaInfantryTankHunter", 2, 560, 420),
                ("ChinaInfantryTankHunter", 2, 580, 390), ("ChinaInfantryTankHunter", 2, 580, 410));
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++_sequence, world.Tick, air.Id, Fixed.FromInt(420), Fixed.FromInt(400))).Accepted, $"{rate}: the Comanche flies out.");
            Entity? flare = null;
            for (var step = 0; step < world.Frames(30 * 40) && flare is null; step++)
            {
                world.Step();
                if (air.Body!.Health < air.Body.MaxHealth)
                    world.Submit(new DamageCommand(PlayerId.System, ++_sequence, world.Tick, air.Id, air.Body.MaxHealth, DamageTypes.Healing, 0));
                flare = world.Entities.FirstOrDefault(value => value.Template.Name == "CountermeasureFlare");
            }
            Check.That(flare is not null, $"{rate}: the Comanche launches flares.");
            Check.That(world.Submit(new DamageCommand(PlayerId.System, ++_sequence, world.Tick, air.Id, air.Body!.MaxHealth * Fixed.FromInt(10), DamageTypes.Explosion, 0)).Accepted, $"{rate}: the Comanche is shot down.");
            for (var step = 0; step < world.Frames(2); step++) world.Step();
            Check.That(air.EffectivelyDead, $"{rate}: the Comanche is dead.");
            var height = flare!.Z;
            for (var step = 0; step < world.Frames(15); step++) world.Step();
            Check.That(!flare.DestroyRequested && flare.Z < height - Fixed.FromInt(3), $"{rate}: its flare sinks on ({height.ToDecimal():0.00} -> {flare.Z.ToDecimal():0.00}).");
        }
    }
}
