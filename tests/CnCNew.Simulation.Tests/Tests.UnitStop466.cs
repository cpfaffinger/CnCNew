using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #466: a ground unit's stop on real data.
static partial class Tests
{
    /// <summary>
    /// #466: the player's stop (<c>MSG_DO_STOP</c> -> <c>groupIdle</c> -> <c>aiIdle</c>) puts the unit into <c>AI_IDLE</c>; the idle state's locomotor goal is none, and a ground locomotor's
    /// <c>maintainCurrentPositionOther</c> scrubs its velocity to zero, so the unit stands at once where the order finds it. A Battlemaster and a Ranger driving east, stopped in the first
    /// tick of a frame of their own: from the tick of the order on they stand on that place with no move left, and they stand there seconds later; above 30 Hz a stop given in the second
    /// tick of the frame finds them where that frame ends, which is where the 30 Hz stop of the next frame finds them. The same places at 30, 60 and 120 Hz. Before, the stop was
    /// a move to the place the player had seen the unit at - behind it when the order was late.
    /// </summary>
    public static void RealGroundUnitStopsWhereTheOrderFindsIt(string directory)
    {
        var source = RealGraph(directory);
        var results = new List<(SimulationRate Rate, string Line)>();
        foreach (var rate in TargetRates)
        {
            var line = new List<string>();
            foreach (var name in new[] { "ChinaTankBattleMaster", "AmericaInfantryRanger" })
                foreach (var inside in new[] { false, true })
                {
                    var catalog = TemplateCatalog.Build(source.Graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
                    var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
                    var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 5, players, "real-unit-stop", source.Fingerprint), catalog);
                    ulong sequence = 0;
                    Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, name, new PlayerId(1), Fixed.FromInt(1000), Fixed.FromInt(1000), Fixed.Zero, Fixed.Zero)).Accepted,
                        $"{rate}: the {name} spawns.");
                    world.Step();
                    var unit = world.Entities.Single(value => value.Template.Name == name);
                    for (var tick = 0; tick < world.Frames(10); tick++) world.Step();
                    while (!world.Time.BeginsFrameOf(unit, world.Tick)) world.Step();
                    Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++sequence, world.Tick, unit.Id, Fixed.FromInt(3000), Fixed.FromInt(1000))).Accepted, $"{rate}: the {name} is sent east.");
                    // Three seconds of driving, then the start of a frame of its own.
                    for (var tick = 0; tick < world.Frames(90); tick++) world.Step();
                    while (!world.Time.BeginsFrameOf(unit, world.Tick)) world.Step();
                    // "inside": one tick later - the frame's second tick above 30 Hz, the next frame's start at 30 Hz, where the 30 Hz order of that moment arrives.
                    if (inside) world.Step();
                    Check.That(unit.HasMoveTarget && unit.X > Fixed.FromInt(1030), $"{rate}: the {name} drives ({unit.X}).");
                    var found = unit.FrameEnd(world.Tick);
                    var stop = world.Submit(new StopCommand(new PlayerId(1), ++sequence, world.Tick, unit.Id));
                    Check.That(stop.Accepted, $"{rate}: the {name} takes the stop ({stop.Reason}).");
                    world.Step();
                    Check.That(!unit.HasMoveTarget && unit.X == found.X && unit.Y == found.Y,
                        $"{rate}: the stopped {name} stands where the order found it ({unit.X}, {unit.Y}; found at {found.X}, {found.Y}).");
                    for (var tick = 0; tick < world.Frames(90); tick++) world.Step();
                    Check.That(!unit.HasMoveTarget && unit.X == found.X && unit.Y == found.Y && world.IsAiIdle(unit),
                        $"{rate}: three seconds later it stands there, idle ({unit.X}, {unit.Y}).");
                    line.Add($"{name}{(inside ? " inside" : "")}: {found.X.Raw},{found.Y.Raw}");
                }
            results.Add((rate, string.Join("; ", line)));
        }
        foreach (var (rate, text) in results) System.Console.WriteLine($"       #466 {rate}: {text}");
        Check.That(results.Select(value => value.Line).Distinct().Count() == 1, "The units stop on the same places at 30, 60 and 120 Hz.");
    }
}
