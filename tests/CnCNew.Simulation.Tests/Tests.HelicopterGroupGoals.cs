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
    /// Real data, the owner's playtest of the release (#449): a group of helicopters died far too fast to missile infantry, as if one missile hit the whole group. The radius damage is the
    /// original's (<c>WeaponTemplate::dealDamageInternal</c>: the missile's detonation point against every bounding sphere, <c>FROM_BOUNDINGSPHERE_3D</c>, 5 units for the Missile Defender);
    /// the group stood in one another. The original's <c>aiMoveToPosition</c> enters <c>AIInternalMoveToState</c> at once, whose <c>onEnter</c> adjusts an aircraft's destination to a box of
    /// goal cells nobody else holds and takes it (<c>adjustDestination</c>, <c>updateGoal</c>); so the members of one group order, and the orders of one bundle, see each other's goals.
    /// The port hid every goal taken in the current frame of the original, and four of six Comanches of one group order took the same cell. Six Comanches sent as a group, and sent one
    /// by one to one point in one tick, each hold a box of their own (Comanche: four cells, so two of them are at least 40 units apart on one axis); a volley of four Missile Defenders then
    /// hurts the one it is fired at and no neighbour. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealHelicopterGroupTakesGoalsOfItsOwn(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 5, players, "real-heli-group", fingerprint), catalog);
            ulong sequence = 0;
            SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
            for (var index = 0; index < 6; index++)
                Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaVehicleComanche", new PlayerId(1), Fixed.FromInt(2000 + 60 * index), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, $"{rate}: a Comanche spawns.");
            world.Step();
            List<Entity> helis = [..world.Entities.Where(value => value.Template.Name == "AmericaVehicleComanche").OrderBy(value => value.Id.Value)];
            Check.That(helis.Count == 6, $"{rate}: six Comanches ({helis.Count}).");
            string Where() => string.Join("; ", helis.Select(heli => $"{heli.Id.Value}@({heli.X.ToDecimal():0.#}, {heli.Y.ToDecimal():0.#}) hp {heli.Body!.Health.ToDecimal():0.#}"));
            void Settle()
            {
                for (var tick = 0; tick < world.Frames(30); tick++) world.Step();
                for (var tick = 0; tick < world.Frames(30 * 60) && helis.Any(heli => heli.HasMoveTarget || !world.IsAiIdle(heli)); tick++) world.Step();
                for (var tick = 0; tick < world.Frames(30 * 3); tick++) world.Step();
            }
            void Apart(string what)
            {
                // Two boxes of four cells that do not overlap: at least four cells apart on one axis.
                var least = Fixed.FromInt(40);
                for (var a = 0; a < helis.Count; a++)
                    for (var b = a + 1; b < helis.Count; b++)
                    {
                        var dx = (helis[a].X - helis[b].X).Abs();
                        var dy = (helis[a].Y - helis[b].Y).Abs();
                        Check.That(!helis[a].HasMoveTarget && (dx >= least || dy >= least), $"{rate}: {what}, every Comanche holds a place of its own ({Where()}).");
                    }
            }

            Check.That(Send(next => new GroupMoveCommand(new PlayerId(1), next, world.Tick, [..helis.Select(heli => heli.Id)], Fixed.FromInt(2600), Fixed.FromInt(2600), false, true)).Accepted, $"{rate}: the group is sent.");
            Settle();
            System.Console.WriteLine($"       {rate} after the group order: {Where()}");
            Apart("sent as a group");

            foreach (var heli in helis)
                Check.That(Send(next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, heli.Id, Fixed.FromInt(3000), Fixed.FromInt(2400))).Accepted, $"{rate}: a Comanche is sent to the point.");
            Settle();
            System.Console.WriteLine($"       {rate} after six orders of one tick: {Where()}");
            Apart("sent one by one to one point in one tick");

            var target = helis[2];
            for (var index = 0; index < 4; index++)
                Check.That(Send(next => new SpawnCommand(new PlayerId(2), next, world.Tick, "AmericaInfantryMissileDefender", new PlayerId(2), target.X + Fixed.FromInt(5 * index), target.Y - Fixed.FromInt(110), Fixed.Zero, Fixed.Zero)).Accepted, $"{rate}: a Missile Defender spawns.");
            world.Step();
            var defenders = world.Entities.Where(value => value.Template.Name == "AmericaInfantryMissileDefender").Select(value => value.Id).ToList();
            foreach (var defender in defenders)
                Check.That(Send(next => new FireWeaponCommand(new PlayerId(2), next, world.Tick, defender, target.Id)).Accepted, $"{rate}: a Missile Defender fires at the Comanche.");
            var health = helis.ToDictionary(heli => heli.Id, heli => heli.Body!.Health);
            var impacts = 0;
            for (var tick = 0; tick < world.Frames(30 * 3) && impacts < defenders.Count; tick++)
                impacts += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponImpact && value.Text == "MissileDefenderMissileWeapon");
            System.Console.WriteLine($"       {rate} after the volley ({impacts} impacts): {Where()}");
            Check.That(impacts == defenders.Count, $"{rate}: the four missiles detonate ({impacts}).");
            Check.That(target.Body!.Health < health[target.Id], $"{rate}: the Comanche fired at is hit.");
            Check.That(helis.Where(heli => heli != target).All(heli => heli.Body!.Health == health[heli.Id]), $"{rate}: no neighbour is hit ({Where()}).");
        }
    }
}
