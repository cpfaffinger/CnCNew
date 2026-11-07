using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// An order to garrison a building (<c>AIEnterState</c>, <c>OpenContain::onCollide</c>): the unit goes in as soon as its geometry touches the building's, at the wall - it does not walk on to the
/// building's centre.
/// </summary>
static partial class Tests
{
    public static void RealGarrisonEntryAtTheWall(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var entered = new Dictionary<(string, int, int), long>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var (building, fromX, fromY) in new[] { ("StanHotel02", 300, 600), ("StanHotel02", 600, 250), ("StanTownHall01", 900, 640), ("AmericanHome02", 420, 420) })
            {
                var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "garrison");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-garrison", fingerprint, terrain.Fingerprint), catalog, terrain);
                var sequence = 0UL;
                void Spawn(string template, int x, int y)
                    => Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
                Spawn(building, 600, 600);
                Spawn("AmericaInfantryRanger", fromX, fromY);
                for (var i = 0; i < world.Frames(1); i++) world.Step();
                var hall = world.Entities.First(value => value.Template.Name == building);
                var ranger = world.Entities.First(value => value.Template.Name == "AmericaInfantryRanger");
                Check.That(world.Submit(new MoveAndEnterCommand(new PlayerId(1), ++sequence, world.Tick, ranger.Id, hall.Id)).Accepted, "The order to garrison is accepted.");
                Fixed lastX = ranger.X, lastY = ranger.Y;
                var inside = false;
                var frames = 0L;
                for (var step = 0; step < world.Frames(900) && !inside; step++)
                {
                    lastX = ranger.X;
                    lastY = ranger.Y;
                    world.Step();
                    inside = ranger.ContainedBy.IsValid;
                    frames = world.Time.ToOriginalFrames(world.Tick);
                }
                // The same frame of the original at every rate, within one frame.
                if (!entered.TryAdd((building, fromX, fromY), frames))
                    Check.That(Math.Abs(entered[(building, fromX, fromY)] - frames) <= 1, $"{rate} {building} from ({fromX},{fromY}): the ranger is inside in frame {frames}, at 30 Hz in frame {entered[(building, fromX, fromY)]}.");
                Check.That(inside, $"{rate} {building}: the ranger gets into the building.");
                // It went in at the wall: not deeper than a few steps inside the rectangle of the footprint (the box is not rotated here), on the way in from the front, the side or a corner.
                var margin = Fixed.FromInt(12);
                var major = hall.Template.GeometryMajorRadius;
                var minor = hall.Template.GeometryMinorRadius;
                var dx = (lastX - hall.X).Abs();
                var dy = (lastY - hall.Y).Abs();
                Check.That(dx >= major - margin || dy >= minor - margin, $"{rate} {building} from ({fromX},{fromY}): the ranger went in at the wall, not at the centre (it stood at |dx| {dx}, |dy| {dy}; box half sides {major} x {minor}).");
                Check.That(dx <= major + Fixed.FromInt(30) && dy <= minor + Fixed.FromInt(30), $"{rate} {building}: and it went in only when it touched the building (|dx| {dx}, |dy| {dy}).");
            }
        }
    }
}
