using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #376: what leaves the map is gone only beyond the map's border (<c>getExtentIncludingBorder</c>): the Spectre gunship's departure (<c>SpectreGunshipUpdate::isPointOffMap</c>)
/// and a Chinook that heads off the map after an evacuation with exit (<c>ChinookHeadOffMapState</c>).
/// </summary>
static partial class Tests
{
    /// <summary>A flat map with a border of 5 cells (50 units); by default 60 by 60 cells, a playable area about 500 units wide.</summary>
    private static TerrainMap HeadOffMapTerrain(int cells = 60) => TerrainMap.Build(SyntheticMaps.Build(cells, cells, 5, (_, _) => 0), "head-off-map-" + cells);

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
    /// The departing gunship flies on over the map's border and is deleted only in the frame that takes it beyond the border, not at the playable edge, at the end of that frame,
    /// in the same frame of the original at 30, 60 and 120 Hz.
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
            Check.That(frames[gone].OffBorder, $"{rate}: it is deleted at the end of the frame that takes it beyond the border ({Describe(frames[gone])}).");
            reference ??= gone;
            Check.That(gone == reference, $"{rate}: the gunship is deleted in departure frame {gone} (30 Hz: {reference}).");
        }
    }

    /// <summary>
    /// A Chinook appears at <paramref name="originX"/>, <paramref name="originY"/>, is sent to unload at a drop zone with exit (<c>aiMoveToAndEvacuateAndExit</c>) where a logic frame
    /// of the original begins, and is followed frame by frame from the order on.
    /// </summary>
    private static List<(HeadOffFrame Frame, bool Idle, bool Moving)> ChinookHeadOffRun(SimulationRate rate, int factor, int cells, int originX, int originY, int dropX, int dropY, int frames,
        int restoreAt = -1)
    {
        var fixture = new SyntheticFixture(rate, files: TransportFiles());
        var terrain = HeadOffMapTerrain(cells);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 11, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Rotorcraft", owner: 1, x: originX, y: originY);
        world.Step();
        var chinook = world.Entities.Single(value => value.Template.Name == "Rotorcraft");
        while (world.Tick < world.Time.OriginalFrames(25)) world.Step();
        var ai = chinook.Modules.OfType<AIUpdateInterface>().First();
        ai.OrderEvacuate(world, true, Fixed.FromInt(dropX), Fixed.FromInt(dropY), thenExit: true);
        var result = new List<(HeadOffFrame, bool, bool)>();
        SimulationWorld? restored = null;
        for (var frame = 0; frame < frames; frame++)
        {
            if (frame == restoreAt)
            {
                // The way home survives a save: the restored world follows the original to the end of the run.
                restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
                Check.That(restored.StateHash == world.StateHash, $"{rate}: the Chinook on its way home restores to the same hash.");
            }
            for (var tick = 0; tick < factor; tick++)
            {
                var hash = world.Step().StateHash;
                if (restored is not null) Check.That(restored.Step().StateHash == hash, $"{rate}: the restored world follows the original (frame {frame}).");
            }
            result.Add((HeadOffFrameOf(world, chinook), ai.IsIdle, chinook.HasMoveTarget));
            if (!result[^1].Item1.Alive) break;
        }
        return result;
    }

    /// <summary>
    /// <c>ChinookHeadOffMapState</c>: after an evacuation with exit the Chinook flies back to where it was ordered, as a move of at most 20 seconds, and is destroyed when that move
    /// has ended only beyond the map with its border (<c>getExtentIncludingBorder</c>). Back at a place inside the border, or stopped by the 20 seconds inside the map, it hovers on,
    /// not idle. In the same frame of the original at 30, 60 and 120 Hz.
    /// </summary>
    public static void AChinookHeadsOffTheMapBeyondTheBorder()
    {
        int? beyond = null, inside = null, stopped = null;
        foreach (var (rate, factor) in WorldRates)
        {
            // Back to a place beyond the border (50 units): destroyed when it arrives there.
            var run = ChinookHeadOffRun(rate, factor, 60, -80, 250, 300, 250, 1500);
            var gone = run.FindIndex(value => !value.Frame.Alive);
            Check.That(gone > 0, $"{rate}: the Chinook sent home beyond the border is destroyed (frames followed: {run.Count}).");
            Check.That(run[gone].Frame.OffBorder && run.Take(gone).All(value => !value.Idle), $"{rate}: it is destroyed beyond the border, not idle on the way ({Describe(run[gone].Frame)}).");
            beyond ??= gone;
            Check.That(gone == beyond, $"{rate}: destroyed in frame {gone} after the order (30 Hz: {beyond}).");
        }
        foreach (var (rate, factor) in WorldRates)
        {
            // Back to a place beyond the playable edge but inside the border: it hovers there, not idle, and is not destroyed.
            var run = ChinookHeadOffRun(rate, factor, 60, -30, 250, 300, 250, 1500);
            var last = run[^1];
            Check.That(last.Frame.Alive, $"{rate}: the Chinook home inside the border is not destroyed ({Describe(last.Frame)}).");
            Check.That(last.Frame.OffPlayable && !last.Frame.OffBorder && !last.Moving && !last.Idle && last.Frame.X == Fixed.FromInt(-30) && last.Frame.Y == Fixed.FromInt(250),
                $"{rate}: it hovers at its origin beyond the playable edge, not idle ({Describe(last.Frame)}, idle {last.Idle}, moving {last.Moving}).");
            var home = run.FindIndex(value => value.Frame.X == Fixed.FromInt(-30) && !value.Moving);
            inside ??= home;
            Check.That(home == inside, $"{rate}: it arrives at its origin in frame {home} (30 Hz: {inside}).");
        }
        foreach (var (rate, factor) in WorldRates)
        {
            // A way back longer than 20 seconds (150 units a second, 3,000 units): the Chinook stops inside the map and hovers there.
            var run = ChinookHeadOffRun(rate, factor, 400, -100, 1000, 3600, 1000, 2600, restoreAt: 1100);
            var last = run[^1];
            Check.That(last.Frame.Alive && !last.Frame.OffPlayable && !last.Moving && !last.Idle,
                $"{rate}: after 20 seconds on its way back the Chinook stops inside the map and hovers there, not idle ({Describe(last.Frame)}, idle {last.Idle}, moving {last.Moving}).");
            var halt = run.FindLastIndex(value => value.Moving) + 1;
            var atDrop = run.FindIndex(value => value.Frame.X == Fixed.FromInt(3600));
            var leaving = atDrop < 0 ? -1 : run.FindIndex(atDrop, value => value.Frame.X < Fixed.FromInt(3600));
            Check.That(leaving < 1100 && halt > 1100, $"{rate}: setup - the save is taken on the way home (left {leaving}, stopped {halt}).");
            Check.That(leaving > 0 && halt - leaving >= 598 && halt - leaving <= 601, $"{rate}: it stops 20 seconds after it set off home (left the drop zone in frame {leaving}, stopped in frame {halt}).");
            stopped ??= halt;
            Check.That(halt == stopped, $"{rate}: it stops in frame {halt} after the order (30 Hz: {stopped}).");
            Check.That(run.Skip(halt).All(value => value.Frame.X == last.Frame.X), $"{rate}: it stays where it stopped.");
        }
    }
}
