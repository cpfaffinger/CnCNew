using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data (#451): the approach of an airborne attacker is <c>AIUpdateInterface::computeAttackPath</c>'s - "airborne objects don't pathfind" - a flight straight to
    /// <c>Weapon::computeApproachTarget</c>: 0.9 of the weapon's range from the victim on the line from the victim to the attacker, moved to the nearest box of goal cells that no other
    /// aircraft holds and from which the weapon reaches (<c>Pathfinder::adjustTargetDestination</c>), and taken (<c>updateGoal</c>). The port flew a pursuing aircraft at its victim's place.
    /// Four Comanches sent in one bundle to attack a Battlemaster, and one sent at a Scorpion, fly to places within their range - none over its victim -, each in a box of its own, and fire
    /// from there. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealHelicoptersApproachToTheirRange(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var (victimName, count) in new[] { ("ChinaTankBattleMaster", 4), ("GLATankScorpion", 1) })
            {
                var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 5, players, "real-heli-approach", fingerprint), catalog);
                ulong sequence = 0;
                SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
                for (var index = 0; index < count; index++)
                    Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaVehicleComanche", new PlayerId(1), Fixed.FromInt(2000 + 60 * index), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, $"{rate}: a Comanche spawns.");
                Check.That(Send(next => new SpawnCommand(new PlayerId(2), next, world.Tick, victimName, new PlayerId(2), Fixed.FromInt(2350), Fixed.FromInt(2400), Fixed.Zero, Fixed.Zero)).Accepted, $"{rate}: the {victimName} spawns.");
                world.Step();
                List<Entity> helis = [..world.Entities.Where(value => value.Template.Name == "AmericaVehicleComanche").OrderBy(value => value.Id.Value)];
                var victim = world.Entities.Single(value => value.Template.Name == victimName);
                // Lift off.
                for (var tick = 0; tick < world.Frames(60); tick++) world.Step();

                // Weapon range 200 (less a quarter cell, RATIONALIZE_ATTACK_RANGE): the approach target lies 0.9 of it from the victim, adjusted by at most a few cells.
                decimal Gap(Fixed x, Fixed y) => (decimal)System.Math.Sqrt((double)(((x - victim.X) * (x - victim.X) + (y - victim.Y) * (y - victim.Y)).ToDecimal()));
                string Where() => string.Join("; ", helis.Select(heli => $"{heli.Id.Value}@({heli.X.ToDecimal():0.#}, {heli.Y.ToDecimal():0.#}) -> ({heli.MoveTargetX.ToDecimal():0.#}, {heli.MoveTargetY.ToDecimal():0.#}) d {Gap(heli.X, heli.Y):0.#}"));
                foreach (var heli in helis)
                    Check.That(Send(next => new PursueCommand(new PlayerId(1), next, world.Tick, heli.Id, victim.Id, victim.X, victim.Y)).Accepted, $"{rate}: a Comanche is ordered to attack.");
                world.Step();
                var goals = helis.ToDictionary(heli => heli.Id.Value, heli => (X: heli.MoveTargetX, Y: heli.MoveTargetY));
                foreach (var heli in helis)
                {
                    var goal = Gap(heli.MoveTargetX, heli.MoveTargetY);
                    Check.That(heli.HasMoveTarget && goal >= 150m && goal <= 215m, $"{rate}, {victimName}: Comanche {heli.Id.Value} flies to a place within its range, not to its victim ({Where()}).");
                }
                for (var tick = 0; tick < world.Frames(30 * 30) && helis.Any(heli => heli.HasMoveTarget); tick++) world.Step();
                // Above 30 Hz the move ends in the first tick of its last frame and the rest of the step is covered in that frame's shares (#457): read where the frame ends.
                for (var tick = 0; tick < world.Frames(1) && helis.Any(heli => heli.IsInsideFrameStep); tick++) world.Step();
                System.Console.WriteLine($"       {rate} {victimName}: {Where()}");
                for (var a = 0; a < helis.Count; a++)
                {
                    var (goalX, goalY) = goals[helis[a].Id.Value];
                    Check.That(!helis[a].HasMoveTarget && Gap(helis[a].X, helis[a].Y) >= 150m && Gap(helis[a].X, helis[a].Y) <= 215m
                               && helis[a].X == goalX && helis[a].Y == goalY, $"{rate}, {victimName}: Comanche {helis[a].Id.Value} hovers at its approach target within its range ({Where()}).");
                    for (var b = a + 1; b < helis.Count; b++)
                    {
                        // Boxes of four cells that do not overlap: their points at least four cells apart on one axis.
                        var other = goals[helis[b].Id.Value];
                        var dx = (goalX - other.X).Abs();
                        var dy = (goalY - other.Y).Abs();
                        Check.That(dx >= Fixed.FromInt(40) || dy >= Fixed.FromInt(40), $"{rate}, {victimName}: Comanches {helis[a].Id.Value} and {helis[b].Id.Value} hold boxes of their own ({Where()}).");
                    }
                }

                var fired = new HashSet<uint>();
                for (var tick = 0; tick < world.Frames(30 * 5) && fired.Count < count && victim.Body!.Health > Fixed.Zero; tick++)
                {
                    if (world.Time.IsOriginalFrameBoundary(world.Tick))
                        foreach (var heli in helis.Where(heli => !fired.Contains(heli.Id.Value)))
                            Send(next => new FireWeaponCommand(new PlayerId(1), next, world.Tick, heli.Id, victim.Id));
                    foreach (var simEvent in world.Step().Events)
                        if (simEvent.Kind == SimEventKind.WeaponFired && helis.Any(heli => heli.Id == simEvent.Entity)) fired.Add(simEvent.Entity.Value);
                }
                Check.That(fired.Count == count, $"{rate}, {victimName}: every Comanche fires from its place ({fired.Count} of {count}).");
            }
        }
    }
}
