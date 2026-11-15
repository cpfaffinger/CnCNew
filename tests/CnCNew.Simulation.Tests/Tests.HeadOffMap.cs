using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #376: what leaves the map is gone only beyond the map's border (<c>getExtentIncludingBorder</c>): the Spectre gunship's departure (<c>SpectreGunshipUpdate::isPointOffMap</c>).
/// </summary>
static partial class Tests
{
    /// <summary>A flat map of 60 by 60 cells with a border of 5 cells (50 units): the playable area is about 500 units wide.</summary>
    private static TerrainMap HeadOffMapTerrain() => TerrainMap.Build(SyntheticMaps.Build(60, 60, 5, (_, _) => 0), "head-off-map");

    /// <summary>One frame end of an aircraft: alive, where, and whether it is beyond the playable edge or beyond the border.</summary>
    private readonly record struct HeadOffFrame(bool Alive, Fixed X, Fixed Y, bool OffPlayable, bool OffBorder);

    private static HeadOffFrame HeadOffFrameOf(SimulationWorld world, Entity entity)
    {
        var terrain = world.Terrain!;
        var (x, y) = (entity.X, entity.Y);
        var offPlayable = x < Fixed.Zero || y < Fixed.Zero || x > terrain.ExtentX || y > terrain.ExtentY;
        // getExtentIncludingBorder: from minus the border to the field's width less the border, the point strictly inside (isInRegionNoZ).
        var border = Fixed.FromInt(terrain.Border * TerrainMap.CellSize);
        var highX = Fixed.FromInt((terrain.VertexWidth - terrain.Border) * TerrainMap.CellSize);
        var highY = Fixed.FromInt((terrain.VertexHeight - terrain.Border) * TerrainMap.CellSize);
        var offBorder = !(-border < x && x < highX && -border < y && y < highY);
        return new(world.FindEntity(entity.Id) is { EffectivelyDead: false }, x, y, offPlayable, offBorder);
    }

    private static string Describe(HeadOffFrame frame)
        => $"{frame.Alive} {frame.X.ToDecimal():0.000},{frame.Y.ToDecimal():0.000} off-playable {frame.OffPlayable} off-border {frame.OffBorder}";

    /// <summary>A Spectre gunship strikes near the east edge, orbits and departs; its frame ends from the start of the departure on.</summary>
    private static List<HeadOffFrame> SpectreDepartureRun(SimulationRate rate, int factor)
    {
        var fixture = new SyntheticFixture(rate, files: AirWeaponFixtureFiles);
        var terrain = HeadOffMapTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 13, AirPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "AirGunship", owner: 1, x: 300, y: 250);
        world.Step();
        var ship = world.Entities.Single(value => value.Template.Name == "AirGunship");
        var spectre = ship.Modules.OfType<SpectreGunshipUpdate>().Single();
        // The power recharges for 4 seconds after the gunship appeared; the order is given where a logic frame of the original begins.
        while (world.Tick < world.Time.OriginalFrames(130)) world.Step();
        Check.That(feeder.UseSpecialPower(1, ship.Id.Value, "AirGunshipPower", SpecialPowerTargetKind.Location, x: 420, y: 250).Accepted, $"{rate}: the power is used.");
        for (var frame = 0; frame < 600 && spectre.Status != SpectreGunshipUpdate.GunshipStatus.Departing; frame++)
            for (var tick = 0; tick < factor; tick++) world.Step();
        Check.That(spectre.Status == SpectreGunshipUpdate.GunshipStatus.Departing, $"{rate}: the gunship departs after its orbit ({spectre.Status}).");
        var result = new List<HeadOffFrame>();
        for (var frame = 0; frame < 400; frame++)
        {
            for (var tick = 0; tick < factor; tick++) world.Step();
            result.Add(HeadOffFrameOf(world, ship));
            if (!result[^1].Alive) break;
        }
        return result;
    }

    /// <summary>
    /// The departing gunship flies on over the map's border and is deleted only in the frame that takes it beyond the border, not at the playable edge, in the same frame of the
    /// original at 30, 60 and 120 Hz.
    /// </summary>
    public static void ASpectreGunshipDepartsBeyondTheBorder()
    {
        int? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var frames = SpectreDepartureRun(rate, factor);
            var gone = frames.FindIndex(value => !value.Alive);
            Check.That(gone > 1, $"{rate}: the departing gunship leaves the map and is deleted (first frame without it: {gone} of {frames.Count}).");
            Check.That(frames.Take(gone).Any(value => value.OffPlayable),
                $"{rate}: the gunship still flies beyond the playable edge (last frame alive: {Describe(frames[gone - 1])}).");
            Check.That(frames.Take(gone).All(value => !value.OffBorder), $"{rate}: no frame of the alive gunship ends beyond the border (last: {Describe(frames[gone - 1])}).");
            if (factor == 1) Check.That(frames[gone].OffBorder, $"{rate}: it is deleted in the frame that takes it beyond the border ({Describe(frames[gone])}).");
            reference ??= gone;
            Check.That(gone == reference, $"{rate}: the gunship is deleted in departure frame {gone} (30 Hz: {reference}).");
        }
    }
}
