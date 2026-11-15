using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>#376 on the retail data: the real Spectre gunship and the real Chinook leave the map only beyond its border.</summary>
static partial class Tests
{
    /// <summary>
    /// On a flat map with a border of 30 cells (300 units), at the default rate: a real Spectre gunship (<c>AirF_AmericaJetSpectreGunship1</c>) struck near the edge departs, flies on
    /// beyond the playable edge and is deleted in the frame that takes it beyond the border; a real <c>AmericaVehicleChinook</c> sent to unload with exit is destroyed when it is
    /// back at its origin beyond the border, and hovers on, not idle, when its origin lies inside the border (as on MD_USA02, whose 'USA Spawn01' lies 293 units off the map).
    /// </summary>
    public static void RealHeadOffMapBeyondTheBorder(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var terrain = TerrainMap.Build(SyntheticMaps.Build(200, 200, 30, (_, _) => 0), "real-head-off-map");
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var border = Fixed.FromInt(terrain.Border * TerrainMap.CellSize);
        var highX = Fixed.FromInt((terrain.VertexWidth - terrain.Border) * TerrainMap.CellSize);
        var highY = Fixed.FromInt((terrain.VertexHeight - terrain.Border) * TerrainMap.CellSize);
        bool OffBorder(Fixed x, Fixed y) => !(-border < x && x < highX && -border < y && y < highY);
        bool OffPlayable(Fixed x, Fixed y) => x < Fixed.Zero || y < Fixed.Zero || x > terrain.ExtentX || y > terrain.ExtentY;

        SimulationWorld NewWorld() => new(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-head-off-map", fingerprint, terrain.Fingerprint), catalog, terrain);
        Entity Spawn(SimulationWorld world, string template, int x, int y)
        {
            Check.That(world.Submit(new SpawnCommand(new PlayerId(1), 1, world.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns.");
            world.Step();
            return world.Entities.Single(value => value.Template.Name == template);
        }

        // The Spectre gunship, struck 150 units inside the east edge.
        {
            var world = NewWorld();
            var ship = Spawn(world, "AirF_AmericaJetSpectreGunship1", (int)terrain.ExtentX.ToDecimal() - 400, 600);
            var spectre = ship.Modules.OfType<SpectreGunshipUpdate>().Single();
            Check.That(world.SpecialPowers.TryGet("AirF_SuperweaponSpectreGunship", out var power), "The real gunship power exists.");
            var target = new SpecialPowerTarget(SpecialPowerTargetKind.Location, EntityId.Invalid, terrain.ExtentX - Fixed.FromInt(150), Fixed.FromInt(600));
            spectre.Initiate(world, power!.Index, target);
            for (var tick = 0; tick < world.Frames(9000) && spectre.Status != SpectreGunshipUpdate.GunshipStatus.Departing; tick++) world.Step();
            Check.That(spectre.Status == SpectreGunshipUpdate.GunshipStatus.Departing, $"The real gunship departs after its orbit ({spectre.Status}).");
            var beyondEdge = false;
            var last = (ship.X, ship.Y);
            for (var tick = 0; tick < world.Frames(3000) && world.FindEntity(ship.Id) is not null; tick++)
            {
                last = (ship.X, ship.Y);
                world.Step();
                if (world.FindEntity(ship.Id) is not null)
                {
                    beyondEdge |= OffPlayable(ship.X, ship.Y);
                    Check.That(!OffBorder(ship.X, ship.Y), $"The real gunship is alive only inside the border ({ship.X}, {ship.Y}).");
                }
            }
            Check.That(world.FindEntity(ship.Id) is null, "The real gunship leaves the map and is deleted.");
            Check.That(beyondEdge, "It flew on beyond the playable edge before it was deleted.");
            Check.That(OffBorder(ship.X, ship.Y) && !OffBorder(last.X, last.Y), $"It is deleted in the step that takes it beyond the border (from {last.X}, {last.Y} to {ship.X}, {ship.Y}).");
            Console.WriteLine($"       real head-off-map: the Spectre gunship deleted at ({ship.X.ToDecimal():0.0}, {ship.Y.ToDecimal():0.0}); the border ends at {(-border).ToDecimal()} and {highX.ToDecimal()}");
        }

        // The Chinook, back to an origin inside the border and to one beyond it.
        foreach (var (originY, gone) in new[] { (-150, false), (-350, true) })
        {
            var world = NewWorld();
            var chinook = Spawn(world, "AmericaVehicleChinook", 500, originY);
            for (var tick = 0; tick < world.Frames(60); tick++) world.Step();
            var ai = chinook.Modules.OfType<AIUpdateInterface>().First();
            ai.OrderEvacuate(world, true, Fixed.FromInt(700), Fixed.FromInt(500), thenExit: true);
            var landed = false;
            for (var tick = 0; tick < world.Frames(5400) && world.FindEntity(chinook.Id) is not null; tick++)
            {
                world.Step();
                landed |= chinook.Z <= world.GroundHeightAt(chinook.X, chinook.Y) && chinook.Y > Fixed.FromInt(400);
            }
            Check.That(landed, $"origin {originY}: the real Chinook landed at the drop zone.");
            if (gone)
                Check.That(world.FindEntity(chinook.Id) is null && OffBorder(chinook.X, chinook.Y), $"origin {originY}: back beyond the border, the real Chinook is destroyed ({chinook.X}, {chinook.Y}).");
            else
                Check.That(world.FindEntity(chinook.Id) is not null && !chinook.HasMoveTarget && !ai.IsIdle && chinook.Y == Fixed.FromInt(originY) && OffPlayable(chinook.X, chinook.Y),
                    $"origin {originY}: back inside the border, the real Chinook hovers there, not idle ({chinook.X}, {chinook.Y}, idle {ai.IsIdle}).");
        }
    }
}
