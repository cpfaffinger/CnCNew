using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data, the owner's playtest of 4 November 2026: three Comanches of one airfield fly to its rally point and stood in one another. The original gives a hovering aircraft a goal
    /// cell of its own (<c>AIInternalMoveToState::onEnter</c>: <c>Pathfinder::adjustDestination</c>, <c>checkDestination</c> with <c>isAircraftThatAdjustsDestination</c>,
    /// <c>updateAircraftGoal</c>); so they end at different places, and so do three sent to one point one after another.
    /// </summary>
    public static void RealHelicoptersTakeGoalsOfTheirOwn(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-heli", fingerprint), catalog);
        ulong sequence = 0;
        SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
        Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaAirfield", new PlayerId(1), Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, "The airfield spawns.");
        world.Step();
        Check.That(world.Submit(new GiveMoneyCommand(PlayerId.System, 1, world.Tick, new PlayerId(1), Fixed.FromInt(50000))).Accepted, "Money is granted.");
        world.Step();
        var airfield = world.Entities.First(value => value.Template.Name == "AmericaAirfield");
        Check.That(Send(next => new SetRallyPointCommand(new PlayerId(1), next, world.Tick, airfield.Id, Fixed.FromInt(2400), Fixed.FromInt(2100))).Accepted, "The rally point is set.");
        for (var index = 0; index < 3; index++)
            Check.That(Send(next => new QueueProductionCommand(new PlayerId(1), next, world.Tick, airfield.Id, "AmericaVehicleComanche")).Accepted, "A Comanche is queued.");
        List<Entity> Comanches() => [..world.Entities.Where(value => value.Template.Name == "AmericaVehicleComanche").OrderBy(value => value.Id.Value)];
        for (var tick = 0; tick < world.Frames(30 * 120) && !(Comanches().Count == 3 && Comanches().All(heli => !heli.HasMoveTarget && world.IsAiIdle(heli))); tick++) world.Step();
        for (var tick = 0; tick < world.Frames(30 * 5); tick++) world.Step();
        var helis = Comanches();
        Check.That(helis.Count == 3, $"The airfield produced three Comanches ({helis.Count}).");
        string Where() => string.Join("; ", helis.Select(heli => $"{heli.Id.Value}@({heli.X.ToDecimal():0.#}, {heli.Y.ToDecimal():0.#})"));
        bool Apart(Fixed least)
        {
            for (var a = 0; a < helis.Count; a++)
                for (var b = a + 1; b < helis.Count; b++)
                {
                    var dx = helis[a].X - helis[b].X;
                    var dy = helis[a].Y - helis[b].Y;
                    if (dx * dx + dy * dy < least * least) return false;
                }
            return true;
        }
        System.Console.WriteLine("       comanches at the rally point: " + Where());
        Check.That(Apart(Fixed.FromInt(10)), $"The three Comanches at the rally point stand at places of their own ({Where()}).");

        foreach (var heli in helis)
        {
            Check.That(Send(next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, heli.Id, Fixed.FromInt(2800), Fixed.FromInt(2600))).Accepted, "A Comanche is sent to the point.");
            // One logic frame of the original between the orders: aircraft that decide in one frame do not see each other's new goals (AircraftGoal.HeldBefore).
            for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
        }
        for (var tick = 0; tick < world.Frames(30 * 60) && helis.Any(heli => heli.HasMoveTarget); tick++) world.Step();
        for (var tick = 0; tick < world.Frames(30 * 5); tick++) world.Step();
        System.Console.WriteLine("       comanches at the ordered point: " + Where());
        Check.That(Apart(Fixed.FromInt(10)), $"Sent to one point one after another, they stand at places of their own ({Where()}).");
    }
}
