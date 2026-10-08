using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Spectator;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>The spectator's camera (zoom limit, whole-map overview) and the simulated camera of a computer player (docs/SPECTATOR-MODE.md); synthetic terrain and orders only.</summary>
internal static class SpectatorTests
{
    private static readonly Vector2 Viewport = new(1280, 720);
    private static readonly PlayerId One = new(1);
    private static readonly PlayerId Two = new(2);

    public static int Run(string[] args)
    {
        var t = new TestHarness("Spectator");
        t.Run("a player's zoom limit holds until the spectator raises it", ZoomLimit);
        t.Run("the far camera still sees the whole height field", FarPlane);
        t.Run("the overview height shows the whole map and no lower height does", Overview);
        t.Run("an order becomes a point of interest; other players' orders do not", InterestFromOrders);
        t.Run("a heavier interest takes the camera at once, a lighter one after the dwell time", Dwell);
        t.Run("a wave is followed at the centre of its units", Wave);
        t.Run("the camera glides to a near interest and cuts to a far one", GlideAndCut);
        t.Run("the same orders at the same game time give the same camera at 30, 60 and 120 ticks per second", RateIndependent);
        return t.Failures;
    }

    private static void ZoomLimit()
    {
        var settings = CameraTests.Settings;
        var camera = new RtsCamera(settings, CameraTests.Flat(), Viewport);
        camera.SetHeightAboveGround(5000);
        Near(settings.MaxHeightAboveGround, camera.HeightAboveGround, 1e-3, "a player cannot zoom out past MaxCameraHeight");
        Near(settings.MaxHeightAboveGround, camera.MaxHeightAboveGround, 1e-3, "the limit is the game data's");

        camera.SetMaxHeightAboveGround(2000);
        Near(settings.MaxHeightAboveGround, camera.HeightAboveGround, 1e-3, "raising the limit does not move the camera");
        camera.SetHeightAboveGround(5000);
        Near(2000, camera.HeightAboveGround, 1e-3, "the spectator's limit");
        camera.SettleZoom();
        Near(2000, camera.Eye.Z - camera.GroundLevel, 1, "the eye is that high above the ground");
        camera.SetHeightAboveGround(10);
        Near(settings.MinHeightAboveGround, camera.HeightAboveGround, 1e-3, "the lower limit is unchanged");

        camera.SetHeightAboveGround(1500);
        camera.SetMaxHeightAboveGround(100);
        Near(settings.MaxHeightAboveGround, camera.MaxHeightAboveGround, 1e-3, "a limit below the player's is the player's");
        Near(settings.MaxHeightAboveGround, camera.HeightAboveGround, 1e-3, "and the camera comes down to it");
        camera.SetMaxHeightAboveGround(2000);
        camera.SetHeightAboveGround(1500);
        camera.SetMaxHeightAboveGround(null);
        Near(settings.MaxHeightAboveGround, camera.HeightAboveGround, 1e-3, "dropping the limit clamps the height again");

        Near(310 * 1.12, SpectatorCamera.WheelZoom(310, -1), 1e-2, "one notch towards the user zooms out by the share");
        Near(310, SpectatorCamera.WheelZoom(SpectatorCamera.WheelZoom(310, -3), 3), 1e-2, "and back");
        Assert(SpectatorCamera.WheelZoom(2000, 1) < 2000, "a notch away zooms in");
    }

    private static void FarPlane()
    {
        var terrain = CameraTests.Hills();
        var camera = new RtsCamera(CameraTests.Settings, terrain, Viewport);
        camera.SetMaxHeightAboveGround(6000);
        camera.SetHeightAboveGround(6000);
        camera.SettleZoom();
        var max = terrain.FieldMax;
        var min = terrain.FieldMin;
        foreach (var corner in new[] { min, max, new Vector2(min.X, max.Y), new Vector2(max.X, min.Y) })
            Assert(Vector3.Distance(camera.Eye, new Vector3(corner, terrain.MinHeight)) < camera.FarPlane, $"corner {corner} lies before the far plane {camera.FarPlane}");
        Assert(camera.TryScreenToGround(Viewport / 2, out var hit) && Vector2.Distance(new Vector2(hit.X, hit.Y), camera.Pivot) < 5f, "picking still works from up there");
    }

    private static void Overview()
    {
        var settings = CameraTests.Settings;
        var terrain = CameraTests.Flat(300);
        var pitch = SpectatorCamera.OverviewPitchDegrees * MathF.PI / 180f;
        var height = SpectatorCamera.OverviewHeight(settings, terrain, Viewport, 0f, pitch);
        Assert(height > settings.MaxHeightAboveGround && height < SpectatorCamera.HighestOverview, $"a 3000 x 3000 map needs more than a player's height and less than the search limit: {height}");

        bool AllOnScreen(float at)
        {
            var camera = new RtsCamera(settings, terrain, Viewport);
            camera.SetMaxHeightAboveGround(SpectatorCamera.HighestOverview);
            camera.SetPitch(pitch);
            camera.SetHeightAboveGround(at);
            camera.CenterOn(terrain.BoundaryMax(0) / 2);
            camera.SettleZoom();
            var max = terrain.BoundaryMax(0);
            foreach (var corner in new[] { Vector2.Zero, max, new Vector2(0, max.Y), new Vector2(max.X, 0) })
                if (!camera.TryWorldToScreen(new Vector3(corner, camera.GroundLevel), out var screen) || screen.X < 0 || screen.Y < 0 || screen.X > Viewport.X || screen.Y > Viewport.Y) return false;
            return true;
        }

        Assert(AllOnScreen(height), "all four map corners are on screen from the overview height");
        Assert(!AllOnScreen(height * 0.8f), "and not from four fifths of it");
        Near(900, SpectatorCamera.OverviewHeight(settings, terrain, Viewport, 0f, pitch, limit: 900), 1e-3, "a limit below the fitting height is returned as it is");
        var small = SpectatorCamera.OverviewHeight(settings, CameraTests.Flat(20), Viewport, 0f, pitch);
        Near(settings.MaxHeightAboveGround, small, 1e-3, "a map that fits at a player's height needs no more");
    }

    private static Fixed F(float value) => FixedConvert.FromSingle(value);

    private static ConstructCommand Construct(PlayerId player, long tick, float x, float y) => new(player, 1, tick, new EntityId(1), "Barracks", F(x), F(y), Fixed.Zero);

    private static readonly Dictionary<uint, Vector2> Positions = new()
    {
        [10] = new Vector2(100, 100), [11] = new Vector2(130, 100), [12] = new Vector2(100, 160), [20] = new Vector2(500, 500), [21] = new Vector2(2000, 2000),
    };

    private static Vector2? PositionOf(EntityId id) => Positions.TryGetValue(id.Value, out var position) ? position : null;

    private static void InterestFromOrders()
    {
        var camera = new AiCamera(One, new Vector2(50, 50), 1);
        Assert(camera.Interest.Kind == AiInterestKind.Home && camera.Position == new Vector2(50, 50), "starts at home");

        camera.Observe(10, [Construct(Two, 10, 900, 900)], PositionOf);
        Assert(camera.Interest.Kind == AiInterestKind.Home && camera.OrdersSeen == 0, "another player's order is not seen");

        camera.Observe(10, [Construct(One, 10, 300, 200)], PositionOf);
        Assert(camera.Interest.Kind == AiInterestKind.Construction && camera.Target == new Vector2(300, 200) && camera.Switches == 1, $"a building placed: {camera.Interest}");

        var producer = new AiCamera(One, Vector2.Zero, 1);
        producer.Observe(5, [new QueueProductionCommand(One, 1, 5, new EntityId(20), "Tank")], PositionOf);
        Assert(producer.Interest.Kind == AiInterestKind.Production && producer.Target == new Vector2(500, 500) && producer.Interest.Subject == new EntityId(20), "a unit queued: the production building");
        producer.Observe(6, [new QueueProductionCommand(One, 2, 6, new EntityId(999), "Tank")], PositionOf);
        Assert(producer.OrdersSeen == 1, "an order about an object that is gone has no place");

        var power = new AiCamera(One, Vector2.Zero, 1);
        power.Observe(5, [new UseSpecialPowerCommand(One, 1, 5, new EntityId(20), "Strike", SpecialPowerTargetKind.Location, EntityId.Invalid, F(1200), F(800))], PositionOf);
        Assert(power.Interest.Kind == AiInterestKind.Power && Vector2.Distance(power.Target, new Vector2(1200, 800)) < 0.01f, "a power at a location: the location");
        var dock = new AiCamera(One, Vector2.Zero, 1);
        dock.Observe(5, [new DockCommand(One, 1, 5, new EntityId(10), new EntityId(20)), new PurchaseScienceCommand(One, 2, 5, "Science")], PositionOf);
        Assert(dock.Interest.Kind == AiInterestKind.Supply && dock.Target == new Vector2(500, 500) && dock.OrdersSeen == 1, "a truck sent to a dock: the dock; a science has no place");

        var both = new AiCamera(One, Vector2.Zero, 1);
        both.Observe(5, [Construct(One, 5, 300, 200), new DockCommand(One, 2, 5, new EntityId(10), new EntityId(20))], PositionOf);
        Assert(both.Interest.Kind == AiInterestKind.Construction, "of two orders in one tick the heavier one counts");
    }

    private static void Dwell()
    {
        var settings = new AiCameraSettings();
        var camera = new AiCamera(One, Vector2.Zero, 1, settings);
        camera.Observe(100, [Construct(One, 100, 300, 200)], PositionOf);
        var queue = new QueueProductionCommand(One, 2, 0, new EntityId(20), "Tank");
        camera.Observe(101, [queue], PositionOf);
        Assert(camera.Interest.Kind == AiInterestKind.Construction, "a lighter interest waits");
        camera.Observe(100 + settings.DwellFrames(AiInterestKind.Construction) - 1, [queue], PositionOf);
        Assert(camera.Interest.Kind == AiInterestKind.Construction, "until the dwell time has passed");
        camera.Observe(100 + settings.DwellFrames(AiInterestKind.Construction), [queue], PositionOf);
        Assert(camera.Interest.Kind == AiInterestKind.Production && camera.Switches == 2, "then it takes the camera");

        var before = camera.Interest.Tick;
        camera.Observe(before + 1, [new UseSpecialPowerCommand(One, 3, before + 1, new EntityId(20), "Strike", SpecialPowerTargetKind.Location, EntityId.Invalid, F(900), F(900))], PositionOf);
        Assert(camera.Interest.Kind == AiInterestKind.Power && camera.Switches == 3, "a heavier interest does not wait");

        // The same building queueing again continues the interest: no new switch, and the dwell time is not extended for ever.
        var repeat = new AiCamera(One, Vector2.Zero, 1, settings);
        repeat.Observe(10, [queue], PositionOf);
        repeat.Observe(40, [queue], PositionOf);
        Assert(repeat.Switches == 1 && repeat.Interest.Tick == 10, "the same building again is the same interest");
        repeat.Observe(10 + settings.DwellFrames(AiInterestKind.Production), [new DockCommand(One, 4, 0, new EntityId(10), new EntityId(21))], PositionOf);
        Assert(repeat.Interest.Kind == AiInterestKind.Supply, "and gives way after its dwell time counted from its start");
    }

    private static void Wave()
    {
        var camera = new AiCamera(One, new Vector2(110, 120), 1);
        AttackMoveCommand March(uint unit) => new(One, unit, 50, new EntityId(unit), F(1500), F(1500));
        camera.Observe(50, [March(10), March(11)], PositionOf);
        Assert(camera.Interest.Kind == AiInterestKind.Move && camera.Interest.Units == 2, "two units are a move, not a wave");

        var wave = new AiCamera(One, new Vector2(110, 120), 1);
        wave.Observe(50, [March(10), March(11), March(12)], PositionOf);
        Assert(wave.Interest.Kind == AiInterestKind.Attack && wave.Interest.Units == 3, "three units are a wave");
        Assert(Vector2.Distance(wave.Target, new Vector2(110, 120)) < 0.01f, $"at the centre of the units, not at the goal: {wave.Target}");

        // The units march: the camera's target is their centre every tick.
        var marching = new Dictionary<uint, Vector2> { [10] = new(400, 100), [11] = new(430, 100), [12] = new(400, 160) };
        wave.Advance(id => marching.TryGetValue(id.Value, out var at) ? at : null);
        Assert(Vector2.Distance(wave.Target, new Vector2(410, 120)) < 0.01f, $"follows the units: {wave.Target}");
        marching.Remove(10);
        wave.Advance(id => marching.TryGetValue(id.Value, out var at) ? at : null);
        Assert(Vector2.Distance(wave.Target, new Vector2(415, 130)) < 0.01f, "a unit that died no longer counts");
        marching.Clear();
        wave.Advance(id => marching.TryGetValue(id.Value, out var at) ? at : null);
        Assert(Vector2.Distance(wave.Target, new Vector2(415, 130)) < 0.01f, "with all units gone the camera stays where the wave was last seen");

        // The next orders of the same wave continue it.
        var again = new AiCamera(One, new Vector2(110, 120), 1);
        again.Observe(50, [March(10), March(11), March(12)], PositionOf);
        again.Observe(80, [March(10), March(11), March(12)], PositionOf);
        Assert(again.Switches == 1 && again.Interest.Tick == 50, "orders to a wave under way are the same interest");
    }

    private static void GlideAndCut()
    {
        var settings = new AiCameraSettings();
        var camera = new AiCamera(One, Vector2.Zero, 1, settings);
        camera.Observe(1, [Construct(One, 1, 300, 0)], PositionOf);
        for (var tick = 0; tick < settings.SmoothingFrames; tick++) camera.Advance(PositionOf);
        Near(300 * (1 - Math.Exp(-1)), camera.Position.X, 0.5, "after one time constant the camera has covered 63 % of the way");
        Assert(camera.Cuts == 0, "a near interest is no cut");
        for (var tick = 0; tick < 20 * settings.SmoothingFrames; tick++) camera.Advance(PositionOf);
        Near(300, camera.Position.X, 0.01, "and arrives");

        camera.Observe(1000, [Construct(One, 1000, 300 + settings.CutDistance + 50, 0)], PositionOf);
        camera.Advance(PositionOf);
        Assert(camera.Cuts == 1 && camera.Position == camera.Target, "an interest further than the cut distance is a cut");
    }

    private static void RateIndependent()
    {
        var positions = new List<Vector2>();
        var kinds = new List<AiInterestKind>();
        foreach (var ticksPerFrame in new[] { 1, 2, 4 })
        {
            var camera = new AiCamera(One, Vector2.Zero, ticksPerFrame);
            // Orders at original frames 10 (a building), 40 (a unit: must wait) and 140 (a unit: the dwell time of the building is over); the camera is read at frame 150.
            for (long tick = 0; tick <= 150 * ticksPerFrame; tick++)
            {
                if (tick == 10 * ticksPerFrame) camera.Observe(tick, [Construct(One, tick, 300, 200)], PositionOf);
                if (tick == 40 * ticksPerFrame || tick == 140 * ticksPerFrame) camera.Observe(tick, [new QueueProductionCommand(One, 2, tick, new EntityId(20), "Tank")], PositionOf);
                if (tick == 139 * ticksPerFrame) kinds.Add(camera.Interest.Kind);
                camera.Advance(PositionOf);
            }
            kinds.Add(camera.Interest.Kind);
            positions.Add(camera.Position);
        }
        Assert(kinds.Where((_, index) => index % 2 == 0).All(kind => kind == AiInterestKind.Construction), "the building holds the camera for its dwell time at every rate");
        Assert(kinds.Where((_, index) => index % 2 == 1).All(kind => kind == AiInterestKind.Production), "and gives way at the same game time at every rate");
        // The glide is sampled once per tick, so the three cameras differ by less than the way one original frame covers (here 360 units to go, a fifteenth of it per frame).
        Assert(Vector2.Distance(positions[0], positions[1]) < 12f && Vector2.Distance(positions[0], positions[2]) < 12f, $"the camera is at the same place within one frame of its glide: {string.Join(" / ", positions)}");
        Assert(Vector2.Distance(positions[0], new Vector2(300, 200)) > 100f && Vector2.Distance(positions[0], new Vector2(500, 500)) > 100f, "and it is under way, not at either end");
    }
}
