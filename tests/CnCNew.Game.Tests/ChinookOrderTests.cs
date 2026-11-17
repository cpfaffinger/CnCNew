using System.Collections.Immutable;
using CnCNew.Game.Data;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// Real data: what the Chinooks of the four American factions do for the player's orders, at 30, 60 and 120 Hz (the catalogs are built from one load of the data for each rate,
/// with the rope and dock bones of the shipped models).
/// </summary>
internal static class ChinookOrderTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ChinookOrderTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ChinookOrderTests (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        var catalogs = new[] { 30, 60, 120 }.Select(ticks => TemplateCatalog.Build(data.Graph, ModuleBindingTable.Default, SimulationRate.FromTicksPerSecond(ticks), data.Templates.Geometry, data.Rules)).ToArray();
        harness.Run("Real data (#416): the AirForce Chinook's AI is its last AI module - it gathers and does its combat drop at every rate", () => AirForceChinookIsAChinook(data, catalogs));
        var buttons = CommandButtonCatalog.Build(data.Graph);
        harness.Run("Real data (#415): the unload button of a hovering Chinook lands it, lets the riders out and takes off again, for every American faction at every rate", () => HoveringChinookUnloads(data, catalogs, buttons));
        return harness.Failures;
    }

    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "America"), new(new PlayerId(2), "China")];

    private static void Assert(bool condition, string message) => TestHarness.Assert(condition, message);

    private sealed class Bench
    {
        private static readonly TerrainMap Terrain = TerrainMap.Build(CnCNew.Simulation.Testing.FlatMap.Build(120, 120, 1), "chinook-orders");
        private readonly Dictionary<int, ulong> _sequence = [];

        public Bench(GameData data, TemplateCatalog catalog)
            => World = new(new WorldConfiguration(new WorldId(1), catalog.Rate, 7, Players, "chinook-orders", data.Fingerprint, Terrain.Fingerprint), catalog, Terrain);

        public SimulationWorld World { get; }

        public ulong Next(int player) => _sequence[player] = _sequence.GetValueOrDefault(player) + 1;

        public Entity Spawn(int player, string template, int x, int y)
        {
            Assert(World.Submit(new SpawnCommand(new PlayerId(player), Next(player), World.Tick, template, new PlayerId(player), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns.");
            World.Step();
            return World.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).Last();
        }

        public void Seconds(int seconds)
        {
            for (var step = 0; step < World.Time.OriginalFrames(seconds * CnCNew.Data.OriginalFrame.PerSecond); step++) World.Step();
        }
    }

    /// <summary>
    /// #416: <c>AirF_AmericaVehicleChinook</c> carries an <c>AIUpdateInterface</c> before its <c>ChinookAIUpdate</c>. <c>Object::Object</c> keeps the last AI module as the object's AI
    /// (Object.cpp: <c>m_ai = ai</c> for every module that has one, the assertion against two is debug-only), so this Chinook is a Chinook: ordered to a pile it brings the boxes to the
    /// center, and its combat drop lets the rangers down its ropes.
    /// </summary>
    private static void AirForceChinookIsAChinook(GameData data, TemplateCatalog[] catalogs)
    {
        const string chinookName = "AirF_AmericaVehicleChinook";
        foreach (var catalog in catalogs)
        {
            var rate = catalog.Rate;
            foreach (var variant in new[] { "gathers", "drops" })
            {
                var bench = new Bench(data, catalog);
                var world = bench.World;
                var chinook = bench.Spawn(1, chinookName, 200, 200);
                var ais = chinook.Modules.OfType<AIUpdateInterface>().ToArray();
                Assert(ais.Length == 2 && ais[1].ModuleName == "ChinookAIUpdate", $"{rate}: the AirForce Chinook carries two AI modules, the Chinook's last ({string.Join(", ", ais.Select(ai => ai.ModuleName))})");
                Assert(ReferenceEquals(chinook.Ai, ais[1]), $"{rate}: its AI is the last one, as Object::Object keeps it");
                if (variant == "gathers")
                {
                    bench.Spawn(1, "AmericaSupplyCenter", 300, 300);
                    var pile = bench.Spawn(0, "SupplyPile", 560, 380);
                    Assert(world.Submit(new DockCommand(new PlayerId(1), bench.Next(1), world.Tick, chinook.Id, pile.Id)).Accepted, $"{rate}: the click on the pile orders the AirForce Chinook to dock");
                    long deposits = 0;
                    for (var step = 0; step < world.Time.OriginalFrames(150 * CnCNew.Data.OriginalFrame.PerSecond) && deposits == 0; step++)
                        deposits += world.Step().Events.Count(ev => ev.Kind == SimEventKind.SupplyDeposited && ev.Entity == chinook.Id);
                    Assert(deposits > 0, $"{rate}: the AirForce Chinook brought supplies to the center within 150 s");
                    continue;
                }
                var rangers = Enumerable.Range(0, 3).Select(_ => bench.Spawn(1, "AirF_AmericaInfantryRanger", 200, 200)).ToArray();
                foreach (var ranger in rangers) Assert(world.TryEnterContainer(ranger, chinook), $"{rate}: a ranger boards");
                bench.Seconds(2);
                Assert(world.Submit(new CombatDropCommand(new PlayerId(1), bench.Next(1), world.Tick, [chinook.Id], EntityId.Invalid, Fixed.FromInt(600), Fixed.FromInt(200))).Accepted, $"{rate}: the combat drop is taken");
                var roped = false;
                for (var step = 0; step < world.Time.OriginalFrames(90 * CnCNew.Data.OriginalFrame.PerSecond) && rangers.Any(ranger => ranger.ContainedBy.IsValid || ranger.Z > Fixed.Zero); step++)
                {
                    world.Step();
                    roped |= rangers.Any(ranger => !ranger.ContainedBy.IsValid && ranger.Z > Fixed.Zero && !ranger.EffectivelyDead);
                }
                Assert(roped, $"{rate}: a ranger came down a rope");
                foreach (var ranger in rangers)
                    Assert(!ranger.ContainedBy.IsValid && ranger.Z == Fixed.Zero && !ranger.EffectivelyDead && ranger.X > Fixed.FromInt(550),
                        $"{rate}: every ranger stands on the ground at the place ({ranger.X}, {ranger.Z}, in {ranger.ContainedBy})");
            }
        }
    }

    /// <summary>
    /// #415: the unload button of a Chinook in the air (<c>Command_ChinookUnload</c>, <c>GUI_COMMAND_EVACUATE</c>). The original posts <c>MSG_EVACUATE</c>, and <c>AIGroup::groupEvacuate</c> gives an
    /// aircraft that is an airborne target <c>aiMoveToAndEvacuate</c> to its own place on the ground below: <c>ChinookAIUpdate</c>'s <c>MOVE_TO_AND_EVAC</c> lands, lets the riders out
    /// and takes off again (<c>EVAC_AND_TAKEOFF</c>, <c>TAKING_OFF</c>). Pressed through the input layer for the Chinook of each American faction.
    /// </summary>
    private static void HoveringChinookUnloads(GameData data, TemplateCatalog[] catalogs, CommandButtonCatalog buttons)
    {
        Assert(buttons.TryGetButton("Command_ChinookUnload", out var unload) && unload.Kind == GuiCommandKind.Evacuate, "Command_ChinookUnload is an evacuate button");
        foreach (var catalog in catalogs)
        {
            var rate = catalog.Rate;
            foreach (var (chinookName, rangerName) in new[]
                     {
                         ("AmericaVehicleChinook", "AmericaInfantryRanger"), ("AFG_AmericaVehicleChinook", "AirF_AmericaInfantryRanger"),
                         ("AirF_AmericaVehicleChinook", "AirF_AmericaInfantryRanger"), ("Lazr_AmericaVehicleChinook", "Lazr_AmericaInfantryRanger"),
                         ("SupW_AmericaVehicleChinook", "SupW_AmericaInfantryRanger"),
                     })
            {
                var bench = new Bench(data, catalog);
                var world = bench.World;
                var chinook = bench.Spawn(1, chinookName, 400, 400);
                var rangers = Enumerable.Range(0, 3).Select(_ => bench.Spawn(1, rangerName, 300, 300)).ToArray();
                foreach (var ranger in rangers) Assert(world.TryEnterContainer(ranger, chinook), $"{rate} {chinookName}: a ranger boards");
                bench.Seconds(5);
                var height = chinook.Template.Locomotor!.PreferredHeight;
                Assert(world.IsAirborneTarget(chinook) && chinook.Z >= height - Fixed.FromInt(1), $"{rate} {chinookName}: the Chinook hovers at its height ({chinook.Z})");
                var (hoverX, hoverY) = (chinook.X, chinook.Y);
                var sink = new WorldSink(world);
                var controller = new CommandModeController(new PlayerId(1), sink, new SelectionModel(new PlayerId(1)));
                Assert(controller.Activate(world, unload, [chinook]) == ActivationResult.Issued, $"{rate} {chinookName}: the press is issued");
                var landed = false;
                for (var step = 0; step < world.Time.OriginalFrames(40 * CnCNew.Data.OriginalFrame.PerSecond) && rangers.Any(ranger => ranger.ContainedBy.IsValid); step++)
                {
                    world.Step();
                    landed |= chinook.Z == Fixed.Zero;
                }
                Assert(landed, $"{rate} {chinookName}: the Chinook landed");
                foreach (var ranger in rangers)
                    Assert(!ranger.ContainedBy.IsValid && !ranger.EffectivelyDead && ranger.Z == Fixed.Zero, $"{rate} {chinookName}: every ranger is out on the ground ({ranger.Z}, in {ranger.ContainedBy})");
                var off = Fixed.Sqrt((chinook.X - hoverX) * (chinook.X - hoverX) + (chinook.Y - hoverY) * (chinook.Y - hoverY));
                Assert(off <= Fixed.FromInt(5), $"{rate} {chinookName}: it landed where it hovered ({off} away)");
                bench.Seconds(15);
                Assert(chinook.Z >= height - Fixed.FromInt(1), $"{rate} {chinookName}: and took off again ({chinook.Z})");
            }
        }
    }

    private sealed class WorldSink(SimulationWorld world) : ICommandSink
    {
        private ulong _sequence = 1000;

        public CommandReceipt Issue(Func<ulong, long, SimCommand> make)
        {
            var command = make(++_sequence, world.Tick);
            return new CommandReceipt((long)_sequence, world.Submit(command));
        }
    }
}
