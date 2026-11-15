using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Issue #365: twenty infantry ordered into a bunker that holds five. <c>AIEnterState::update</c> asks <c>ActionManager::canEnterObject(..., CHECK_CAPACITY)</c> in every frame; once the
/// container is full the state fails into <c>AI_IDLE</c> and the unit stands where it is - it does not walk on to the bunker and stand in front of it.
/// </summary>
static partial class Tests
{
    public static void RealUnitsStopWhenTheirContainerIsFull(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        long? fullAt30 = null;
        Dictionary<int, (Fixed X, Fixed Y)>? standAt30 = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "full-bunker");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-full-bunker", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            void Spawn(string template, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            Spawn("ChinaBunker", 800, 600);
            for (var row = 0; row < 4; row++)
                for (var column = 0; column < 5; column++) Spawn("AmericaInfantryRanger", 200 + 20 * column, 560 + 25 * row);
            for (var i = 0; i < world.Frames(1); i++) world.Step();
            var bunker = world.Entities.First(value => value.Template.Name == "ChinaBunker");
            var garrison = (GarrisonContain)bunker.Container!;
            var rangers = world.Entities.Where(value => value.Template.Name == "AmericaInfantryRanger").OrderBy(value => value.Id.Value).ToArray();
            Check.That(rangers.Length == 20, "Setup: twenty rangers.");
            // The client's order for a selection: one enter order per unit, all in the same tick.
            foreach (var ranger in rangers)
                Check.That(world.Submit(new MoveAndEnterCommand(new PlayerId(1), ++sequence, world.Tick, ranger.Id, bunker.Id)).Accepted, "The order to enter is accepted.");
            long? full = null;
            for (var step = 0; step < world.Frames(900) && full is null; step++)
            {
                world.Step();
                if (garrison.ContainCount >= garrison.ContainMax) full = world.Time.ToOriginalFrames(world.Tick);
            }
            Check.That(full is not null, $"{rate}: the bunker fills up ({garrison.ContainCount} of {garrison.ContainMax}).");
            if (fullAt30 is { } frame30) Check.That(Math.Abs(frame30 - full!.Value) <= 1, $"{rate}: the bunker is full in frame {full}, at 30 Hz in frame {frame30}.");
            else fullAt30 = full;
            // Each one that is still outside finds the bunker full at its next frame of the original and gives the order up.
            for (var i = 0; i < world.Frames(2); i++) world.Step();
            var outside = rangers.Where(value => !value.ContainedBy.IsValid).ToArray();
            Check.That(outside.Length == 20 - garrison.ContainMax, $"{rate}: {outside.Length} rangers are outside.");
            foreach (var ranger in outside)
            {
                var ai = ranger.Modules.OfType<AIUpdateInterface>().Single();
                Check.That(!ai.EnterTarget.IsValid, $"{rate}: ranger {ranger.Id.Value} has given up its order to enter the full bunker.");
                Check.That(!ranger.HasMoveTarget, $"{rate}: ranger {ranger.Id.Value} no longer walks to the full bunker.");
            }
            var stopped = outside.ToDictionary(value => (int)value.Id.Value, value => (value.X, value.Y));
            for (var i = 0; i < world.Frames(300); i++) world.Step();
            foreach (var ranger in outside)
            {
                var (x, y) = stopped[(int)ranger.Id.Value];
                var moved = ((ranger.X - x).Abs() + (ranger.Y - y).Abs()).Floor();
                Check.That(moved <= 2, $"{rate}: ranger {ranger.Id.Value} stands where it gave up (it moved {moved} afterwards).");
                Check.That(!ranger.ContainedBy.IsValid, $"{rate}: ranger {ranger.Id.Value} stays outside.");
            }
            // The same places at every rate.
            if (standAt30 is null) standAt30 = outside.ToDictionary(value => (int)value.Id.Value, value => (value.X, value.Y));
            else
                foreach (var ranger in outside)
                {
                    Check.That(standAt30.TryGetValue((int)ranger.Id.Value, out var at30), $"{rate}: ranger {ranger.Id.Value} is outside at 30 Hz too.");
                    var gap = ((ranger.X - at30.X).Abs() + (ranger.Y - at30.Y).Abs()).Floor();
                    Check.That(gap <= 1, $"{rate}: ranger {ranger.Id.Value} stands at ({ranger.X}, {ranger.Y}), at 30 Hz at ({at30.X}, {at30.Y}).");
                }
        }
    }
}
