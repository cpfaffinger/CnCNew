using System.Collections.Immutable;
using CnCNew.Game.Data;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// Real data (#409, #435): the occupant of a barracks leaves by its door. A <c>HealContain</c> is an <c>OpenContain</c> that does not override <c>exitObjectViaDoor</c>, so a healed
/// occupant (<c>HealContain::update</c>) and one told to leave a sold (<c>OpenContain::onSelling</c>) or evacuated building (<c>orderAllPassengersToExit</c>) are set on the bone
/// <c>ExitStart</c> of the building's model with its heading and walk to <c>ExitEnd</c>. Before #435 the port read no exit bones for a <c>HealContain</c>, and the occupant came out on
/// the building's centre. The Internet Center (an <c>InternetHackContain</c>, a <c>TransportContain</c>) always had its door.
/// </summary>
internal static class BarracksDoorTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(BarracksDoorTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP BarracksDoorTests (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Real data (#409, #435): a healed, sold or evacuated barracks and a sold Internet Center let their occupants out by the door bones, at 30, 60 and 120 Hz", () => ByTheDoor(data));
        return harness.Failures;
    }

    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "East"), new(new PlayerId(2), "West")];

    private static void Assert(bool condition, string message) => TestHarness.Assert(condition, message);

    /// <summary>A bone of <paramref name="building"/>'s model in the world; the building's position for a bone the model lacks (<c>Object::getSingleLogicalBonePosition</c>).</summary>
    private static (Fixed X, Fixed Y) On(Entity building, BonePose? bone)
    {
        if (bone is not { } pose) return (building.X, building.Y);
        var (cos, sin) = (FixedTrig.Cos(building.Angle), FixedTrig.Sin(building.Angle));
        return (building.X + pose.X * cos - pose.Y * sin, building.Y + pose.X * sin + pose.Y * cos);
    }

    private static Fixed Distance(Fixed x, Fixed y, (Fixed X, Fixed Y) point) => Fixed.Sqrt((x - point.X) * (x - point.X) + (y - point.Y) * (y - point.Y));

    private static void ByTheDoor(GameData data)
    {
        var exits = data.Templates.Geometry.ExitPaths;
        // Every heal container of the game is a door container now; the three barracks' models (ABBARRACKS, NBBARRACKS, UBBARRACKS) have both bones.
        foreach (var name in new[] { "AmericaBarracks", "ChinaBarracks", "GLABarracks" })
            Assert(exits.TryGetValue(name, out var paths) && paths.Length == 1 && paths[0].Start is not null && paths[0].End is not null, $"{name} has the exit bones ExitStart and ExitEnd");
        var door = exits["AmericaBarracks"][0];
        Console.WriteLine($"       AmericaBarracks: ExitStart {door.Start}, ExitEnd {door.End}");
        // The Internet Center's model has no exit bone: its hacker comes out on the centre and walks to the nearest place it can stand on.
        var hack = exits["ChinaInternetCenter"][0];
        Console.WriteLine($"       ChinaInternetCenter: ExitStart {hack.Start?.ToString() ?? "none"}, ExitEnd {hack.End?.ToString() ?? "none"}");

        string? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var templates = TemplateCatalog.Build(data.Graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, rate, data.Templates.Geometry, data.Rules);
            var terrain = TerrainMap.Build(CnCNew.Simulation.Testing.FlatMap.Build(200, 120, 1), "barracks-door");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, Players, "barracks-door", "barracks-door", terrain.Fingerprint), templates, terrain);
            ulong sequence = 0;
            void Submit(SimCommand command, string what) => Assert(world.Submit(command).Accepted, $"{rate}: {what}");
            void Spawn(string template, int x, int y, Fixed angle)
                => Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, angle), $"{template} spawns");

            // 0 sold, 1 evacuated (turned: the door turns with it), 2 heals its occupant to the end, 3 the Internet Center, sold.
            Spawn("AmericaBarracks", 500, 600, Fixed.Zero);
            Spawn("AmericaBarracks", 900, 600, FixedTrig.Pi / Fixed.FromInt(3));
            Spawn("AmericaBarracks", 1300, 600, FixedTrig.Pi);
            Spawn("ChinaInternetCenter", 1700, 600, Fixed.Zero);
            Spawn("AmericaInfantryRanger", 500, 750, Fixed.Zero);
            Spawn("AmericaInfantryRanger", 900, 750, Fixed.Zero);
            Spawn("AmericaInfantryRanger", 1300, 750, Fixed.Zero);
            Spawn("ChinaInfantryHacker", 1700, 800, Fixed.Zero);
            for (var step = 0; step < world.Time.TicksPerOriginalFrame; step++) world.Step();
            var buildings = world.Entities.Where(value => value.Template.IsKindOf("STRUCTURE")).OrderBy(value => value.Id.Value).ToArray();
            var riders = world.Entities.Where(value => value.Template.IsKindOf("INFANTRY")).OrderBy(value => value.Id.Value).ToArray();
            Assert(buildings.Length == 4 && riders.Length == 4, $"{rate}: four buildings and four soldiers");
            Assert(buildings[0].Container is HealContain && buildings[3].Container is InternetHackContain, $"{rate}: the barracks heals, the Internet Center hacks");
            for (var i = 0; i < 4; i++)
            {
                // Hurt, so the barracks keeps the ranger to heal it (TimeForFullHeal 2 s).
                Submit(new DamageCommand(PlayerId.System, ++sequence, world.Tick, riders[i].Id, riders[i].Body!.MaxHealth / Fixed.FromInt(2), 0, 0), "the soldier is hurt");
                Submit(new MoveAndEnterCommand(new PlayerId(1), ++sequence, world.Tick, riders[i].Id, buildings[i].Id), "the order to enter is accepted");
            }

            // Where each one came out (the tick it left, after that tick's step: it has taken its first step towards the end), where it was sent, and the nearest place it can
            // stand on to ExitEnd (Pathfinder::adjustToPossibleDestination: the barracks' ExitEnd lies inside its own footprint, 110 by 90).
            var cameOut = new (Fixed X, Fixed Y, bool Moves, Fixed TargetX, Fixed TargetY, Fixed EndX, Fixed EndY)?[4];
            var wasInside = new bool[4];
            void Watch()
            {
                for (var i = 0; i < 4; i++)
                {
                    if (riders[i].ContainedBy == buildings[i].Id) wasInside[i] = true;
                    else if (wasInside[i] && cameOut[i] is null)
                    {
                        var building = buildings[i];
                        var (endX, endY) = On(building, (i == 3 ? hack : door).End);
                        world.FindPossibleDestination(riders[i], ref endX, ref endY);
                        cameOut[i] = (riders[i].X, riders[i].Y, riders[i].HasMoveTarget, riders[i].MoveTargetX, riders[i].MoveTargetY, endX, endY);
                    }
                }
            }
            for (var step = 0; step < world.Time.OriginalFrames(30 * 30) && !wasInside.All(value => value); step++) { world.Step(); Watch(); }
            Assert(wasInside.All(value => value), $"{rate}: every soldier went into its building");
            // The two orders in the first tick of a frame of the original.
            while (world.Tick % world.Time.TicksPerOriginalFrame != 0) { world.Step(); Watch(); }
            Assert(riders[0].ContainedBy == buildings[0].Id && riders[1].ContainedBy == buildings[1].Id && riders[3].ContainedBy == buildings[3].Id, $"{rate}: the soldiers to be sent out are still inside");
            Submit(new SellCommand(new PlayerId(1), ++sequence, world.Tick, buildings[0].Id), "the barracks can be sold while it heals");
            Submit(new EvacuateCommand(new PlayerId(1), ++sequence, world.Tick, buildings[1].Id, false, Fixed.Zero, Fixed.Zero), "the other barracks can be evacuated");
            Submit(new SellCommand(new PlayerId(1), ++sequence, world.Tick, buildings[3].Id), "the Internet Center can be sold while it hacks");
            for (var step = 0; step < world.Time.OriginalFrames(30 * 5) && cameOut.Any(value => value is null); step++) { world.Step(); Watch(); }
            Assert(cameOut.All(value => value is not null), $"{rate}: every soldier left its building (the third one healed)");

            var lines = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                var building = buildings[i];
                var path = i == 3 ? hack : door;
                var start = On(building, path.Start);
                var end = On(building, path.End);
                var exit = cameOut[i]!.Value;
                var what = $"{rate}: {riders[i].Template.Name}#{riders[i].Id.Value} of {building.Template.Name}#{building.Id.Value}";
                Assert(Distance(exit.X, exit.Y, start) < Fixed.FromInt(2), $"{what} comes out at ExitStart {start} ({exit.X}, {exit.Y}; the centre ({building.X}, {building.Y}))");
                Assert(exit.Moves && exit.TargetX == exit.EndX && exit.TargetY == exit.EndY,
                    $"{what} walks to the place nearest ExitEnd {end}: ({exit.EndX}, {exit.EndY}) ({exit.TargetX}, {exit.TargetY})");
                lines.Add($"{exit.X.Raw},{exit.Y.Raw}");
            }
            for (var step = 0; step < world.Time.OriginalFrames(30 * 10); step++) world.Step();
            for (var i = 0; i < 3; i++)
            {
                var end = (cameOut[i]!.Value.EndX, cameOut[i]!.Value.EndY);
                Assert(!riders[i].HasMoveTarget && Distance(riders[i].X, riders[i].Y, end) < Fixed.FromInt(10), $"{rate}: ranger {riders[i].Id.Value} stands at the door's end {end} ({riders[i].X}, {riders[i].Y})");
                lines.Add($"{riders[i].X.Raw},{riders[i].Y.Raw}");
            }
            for (var step = 0; step < world.Time.OriginalFrames(30 * 10) && (world.FindEntity(buildings[0].Id) is not null || world.FindEntity(buildings[3].Id) is not null); step++) world.Step();
            Assert(world.FindEntity(buildings[0].Id) is null && world.FindEntity(buildings[3].Id) is null, $"{rate}: both sales complete");
            Assert(riders.All(rider => world.FindEntity(rider.Id) is { EffectivelyDead: false, ContainedBy.IsValid: false }), $"{rate}: every soldier stands in the world after the sales");
            var line = string.Join(";", lines);
            Console.WriteLine($"       {rate}: {line}");
            reference ??= line;
            Assert(line == reference, $"{rate}: the same places as at 30 Hz");
        }
    }
}
