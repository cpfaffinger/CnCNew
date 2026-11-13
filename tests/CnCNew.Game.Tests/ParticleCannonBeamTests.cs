using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The owner's playtest of 10 November 2026 (issue 299), the USA superweapon on real data: the particle cannon fired and its beam was neither seen nor steerable by the pointer.
/// In the original the shortcut bar's launch ends with <c>CommandTranslator::issueSpecialPowerCommand</c> selecting the source ("because we just launched a special power via shortcut, and the
/// special power accepts input from the player (particle uplink cannon, spectre gunship), simply select the object now"), after which a click on the map drives the beam
/// (<c>ParticleUplinkCannonUpdate::setSpecialPowerOverridableDestination</c>: <c>ManualDrivingSpeed</c> a second, <c>ManualFastDrivingSpeed</c> after a second click within
/// <c>DoubleClickToFastDriveDelay</c>); the beam itself is the laser drawables the module creates (<c>createGroundToOrbitLaser</c>, <c>createOrbitToTargetLaser</c>, <c>createConnectorLasers</c>)
/// and the flares of its outer nodes and base (<c>setClientStatus</c>).
/// </summary>
internal static class ParticleCannonBeamTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ParticleCannonBeamTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ParticleCannonBeamTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The cannon launched from the shortcut bar is selected, the pointer is the uplink cannon's and a click drives the beam at the manual speed, a second click fast", () => Steering(data));
        harness.Run("The beam's ground-hit effects (GroundHitFX) burn under the beam, not at the building", () => GroundHitUnderTheBeam(data));
        harness.Run("The beam burns scorch marks of ScorchMarkScalar times its radius where it stands, and they stay on its path", () => ScorchesAlongThePath(data));
        harness.Run("The firing cannon's launch flare (BeamLaunchFX) plays at the dish's fire bone", () => LaunchFlareAtTheDish(data));
        harness.Run("The beam: the laser from the dish up into the orbit, the connector lasers and the flares while it fires, the laser from the orbit down to the aim point from its birth, narrowing at the end, steered by clicks", () => Beam(data));
        return harness.Failures;
    }

    private sealed class Scene
    {
        public required LiveScenario Live { get; init; }
        public required MatchController Controller { get; init; }
        public required ParticleUplinkCannonUpdate Module { get; init; }
        public required CnCNew.Simulation.World.Entity Cannon { get; init; }
        public void Frame()
        {
            Live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }

        public void Seconds(double seconds)
        {
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * seconds; frame++) Frame();
        }
    }

    /// <summary>A cannon with power and its power ready; the player presses the button of the shortcut bar and clicks on the ground. Returns once the order is given.</summary>
    private static Scene FireFromShortcut(GameData data, out Vector3 aimed)
    {
        var scenario = new DemoScenario("cannon-beam", "cannon-beam", "cannon-beam", 6000,
            [new ScenarioUnit("cannon", "AmericaParticleCannonUplink", 1, -60, -60, 0f), new ScenarioUnit("plant1", "AmericaPowerPlant", 1, -140, 0, 0f), new ScenarioUnit("plant2", "AmericaPowerPlant", 1, -140, 70, 0f)], [], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        var cannon = live.World.Entities.First(entity => entity.Template.Name == "AmericaParticleCannonUplink");
        var scene = new Scene { Live = live, Controller = controller, Module = cannon.Modules.OfType<ParticleUplinkCannonUpdate>().Single(), Cannon = cannon };
        scene.Seconds(0.2);
        // The scenario's "powers ready" is overwritten when the building's construction ends; the owner waits four minutes for it.
        foreach (var power in cannon.Modules.OfType<CnCNew.Simulation.Modules.ISpecialPowerModule>()) power.SetReadyFrame(live.World.Tick);
        scene.Frame();
        controller.BuildDrawList(1280, 720);
        TestHarness.Assert(scene.Module.StatusAt(live.World) == ParticleUplinkCannonUpdate.UplinkStatus.ReadyToFire, $"the cannon is ready ({scene.Module.StatusAt(live.World)})");
        var button = controller.Powers.FirstOrDefault(value => value.Button.Name.Contains("ParticleUplinkCannon", StringComparison.OrdinalIgnoreCase));
        TestHarness.Assert(button is { IsEnabled: true }, $"the shortcut bar offers the cannon ({string.Join(", ", controller.Powers.Select(value => value.Button.Name))})");
        TestHarness.Assert(controller.Input.Selection.Selected.IsEmpty, "nothing is selected: the player pressed the bar, not the building");
        TestHarness.Assert(controller.PressPowerButton(controller.Powers.IndexOf(button!) + 1), "the press on the bar is taken");
        var first = new Vector2(640, 330);
        aimed = live.Camera.TryScreenToGround(first, out var point) ? point : throw new InvalidOperationException("the aim is not on the ground");
        controller.Process(new MouseMoveEvent(10_000, first));
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        controller.Process(new MouseButtonEvent(10_100, MouseButton.Left, true, first));
        controller.Process(new MouseButtonEvent(10_150, MouseButton.Left, false, first));
        return scene;
    }

    private static float Distance(Point3 from, float x, float y) => MathF.Sqrt(MathF.Pow((float)from.X.ToDecimal() - x, 2) + MathF.Pow((float)from.Y.ToDecimal() - y, 2));

    private static ImmutableArray<StreamInstance> Streams(Scene scene, string texture) => [.. scene.Live.Fx.Capture().ProjectileStreams.Where(stream => stream.Texture.StartsWith(texture, StringComparison.OrdinalIgnoreCase) && stream.Laser is not null)];

    private static void Beam(GameData data)
    {
        var scene = FireFromShortcut(data, out _);
        var (live, module) = (scene.Live, scene.Module);
        TestHarness.Assert(data.AttachedEffects.Uplinks.TryGetValue("AmericaParticleCannonUplink", out var spec) && spec is { OuterCount: 5, BeamLaser: "ParticleUplinkCannon_OrbitalLaser" }, "the module's draw keys are read");

        // Firing, the beam still on its way (BeamTravelTime 2.5 s): the laser from the dish up into the orbit grows, five connector lasers join the outer nodes, five flares burn there.
        scene.Seconds(1);
        TestHarness.Assert(module.StatusAt(live.World) == ParticleUplinkCannonUpdate.UplinkStatus.Firing, $"the cannon fires ({module.StatusAt(live.World)})");
        var columns = Streams(scene, "EXNoise02");
        TestHarness.Assert(columns.Length == 1, $"one laser stands over the dish while the beam travels ({columns.Length})");
        var column = columns[0];
        TestHarness.Assert(Math.Abs(column.Points[^1].Z - column.Points[0].Z - 500f) < 0.01f && Math.Abs(column.Points[0].X - column.Points[^1].X) < 0.01f, $"it runs 500 units straight up from the dish ({column.Points[0]} to {column.Points[^1]})");
        var cannonAt = new Vector2((float)scene.Cannon.X.ToDecimal(), (float)scene.Cannon.Y.ToDecimal());
        TestHarness.Assert(Vector2.Distance(new Vector2(column.Points[0].X, column.Points[0].Y), cannonAt) < 40f && column.Points[0].Z > 0f, $"and its foot is on the dish ({column.Points[0]}; the building at {cannonAt})");
        var full = data.AttachedEffects.Lasers["ParticleUplinkCannon_OrbitalLaser"].Draw(Vector3.Zero, Vector3.UnitZ, (_, _) => 0f, 0).Laser!.Bands[0].Width;
        var grown = column.Laser!.Bands[0].Width;
        TestHarness.Assert(grown > 0f && grown < full * 0.7f, $"it is still growing to its full width of {full} ({grown})");
        var connectors = Streams(scene, "EXLaser");
        TestHarness.Assert(connectors.Length == 5 && connectors.Select(stream => stream.Points[^1]).Distinct().Count() == 1 && connectors.Select(stream => stream.Points[0]).Distinct().Count() == 5, $"five lasers run from the outer nodes to the one connector ({connectors.Length})");
        TestHarness.Assert(live.Attached.UplinkSystems.Length == 5, $"the flares of the five outer nodes burn ({live.Attached.UplinkSystems.Length})");
        TestHarness.Assert(Streams(scene, "EXNoise02").Length == 1, "no beam stands on the ground yet");

        // The beam is born: the laser from the orbit down to the aim point, 500 units above it, and it grows to its full width in WidthGrowTime (2 s).
        scene.Seconds(2.0);
        var beams = Streams(scene, "EXNoise02");
        var ground = (float)module.BeamPosition.Z.ToDecimal();
        TestHarness.Assert(beams.Length == 2, $"the beam stands beside the column ({beams.Length})");
        var down = beams.Single(stream => stream.Points[0].Z > stream.Points[^1].Z && Math.Abs(stream.Points[^1].X - (float)module.BeamPosition.X.ToDecimal()) < 0.01f);
        TestHarness.Assert(Math.Abs(down.Points[0].Z - down.Points[^1].Z - 500f) < 0.01f && Math.Abs(down.Points[^1].Z - ground) < 1f && Math.Abs(down.Points[0].X - down.Points[^1].X) < 0.01f,
            $"it comes straight down from 500 units above the ground to the ground ({down.Points[0]} to {down.Points[^1]}; the ground at {ground})");
        scene.Seconds(2.0);
        TestHarness.Assert(Math.Abs(Streams(scene, "EXNoise02").Single(stream => stream.Points[0].Z > stream.Points[^1].Z).Laser!.Bands[0].Width - full) < 0.01f, "and at full width after the growth time");

        // The pointer drives the beam; the drawn beam goes with it.
        var far = new Vector2(420, 130);
        var farGround = live.Camera.TryScreenToGround(far, out var target) ? target : throw new InvalidOperationException("not on the ground");
        scene.Controller.Process(new MouseMoveEvent(40_000, far));
        scene.Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        scene.Controller.Process(new MouseButtonEvent(40_100, MouseButton.Right, true, far));
        scene.Controller.Process(new MouseButtonEvent(40_150, MouseButton.Right, false, far));
        var before = down.Points[^1];
        scene.Seconds(1);
        down = Streams(scene, "EXNoise02").Single(stream => stream.Points[0].Z > stream.Points[^1].Z && stream.Points[0].X - stream.Points[^1].X is > -0.01f and < 0.01f && Vector2.Distance(new Vector2(stream.Points[^1].X, stream.Points[^1].Y), cannonAt) > 60f);
        TestHarness.Assert(Vector2.Distance(new Vector2(down.Points[^1].X, down.Points[^1].Y), new Vector2(before.X, before.Y)) is > 15f and < 25f
            && Vector2.Distance(new Vector2(down.Points[^1].X, down.Points[^1].Y), new Vector2(farGround.X, farGround.Y)) < Vector2.Distance(new Vector2(before.X, before.Y), new Vector2(farGround.X, farGround.Y)),
            $"after a second the drawn beam has moved 20 units towards the click ({before} to {down.Points[^1]})");
        TestHarness.Assert(Math.Abs(down.Points[^1].X - (float)module.BeamPosition.X.ToDecimal()) < 1f && Math.Abs(down.Points[^1].Y - (float)module.BeamPosition.Y.ToDecimal()) < 1f, "it is where the module has it");

        // The firing ends at TotalFiringTime (10 s); the column narrows, then the beam, and nothing is left.
        scene.Seconds(4.7);
        TestHarness.Assert(module.StatusAt(live.World) == ParticleUplinkCannonUpdate.UplinkStatus.PostFire, $"the shot is over ({module.StatusAt(live.World)})");
        var narrowing = Streams(scene, "EXNoise02").Select(stream => stream.Laser!.Bands[0].Width).Order().ToArray();
        TestHarness.Assert(narrowing.Length == 2 && narrowing[0] < full - 0.5f && narrowing[0] > 0f && Math.Abs(narrowing[1] - full) < 0.01f, $"the column narrows at once, the beam on the ground after its travel time ({string.Join(", ", narrowing)})");
        TestHarness.Assert(Streams(scene, "EXLaser").Length == 5, "the connector lasers go on at medium strength");
        scene.Seconds(3);
        narrowing = [.. Streams(scene, "EXNoise02").Select(stream => stream.Laser!.Bands[0].Width).Order()];
        TestHarness.Assert(narrowing.Length == 1 && narrowing[0] > 0f && narrowing[0] < full, $"then the beam alone narrows ({string.Join(", ", narrowing)})");
        scene.Seconds(8);
        TestHarness.Assert(Streams(scene, "EXNoise02").IsEmpty && Streams(scene, "EXLaser").IsEmpty && live.Attached.UplinkSystems.IsEmpty, "when it has packed up no laser and no flare is left");
        TestHarness.Assert(!module.InUse, "and the power is over");
    }

    /// <summary>The particles of the running systems of a template, by place.</summary>
    private static Vector3[] ParticlesOf(Scene scene, string template)
    {
        var particles = scene.Live.Fx.Particles;
        return [.. particles.Capture().Particles.Where(particle => template.Equals(particles.TemplateNameOf(particle.SystemId), StringComparison.OrdinalIgnoreCase)).Select(particle => particle.Position)];
    }

    /// <summary>
    /// <c>ParticleUplinkCannonUpdate::update</c>: with every scorch mark the beam plays its <c>GroundHitFX</c> (magma, sparks, a shock wave, fire and a flare) with <c>FXList::doFXPos</c> at
    /// <c>m_currentTargetPosition</c>. The port played every effect list a module asked for at the object's own place, so the ground hit burned on the building.
    /// </summary>
    private static void GroundHitUnderTheBeam(GameData data)
    {
        var scene = FireFromShortcut(data, out _);
        var (live, module) = (scene.Live, scene.Module);
        var cannonAt = new Vector2(FixedConvert.ToSingle(scene.Cannon.X), FixedConvert.ToSingle(scene.Cannon.Y));
        scene.Seconds(1);
        TestHarness.Assert(ParticlesOf(scene, "ParticleUplinkCannon_Magma").Length == 0, "nothing hits the ground before the beam is born");
        // The beam is born (BeamTravelTime 2.5 s) and burns for a while.
        scene.Seconds(3);
        var beam = new Vector2(FixedConvert.ToSingle(module.BeamPosition.X), FixedConvert.ToSingle(module.BeamPosition.Y));
        TestHarness.Assert(Vector2.Distance(beam, cannonAt) > 80f, $"setup: the beam burns away from the building ({beam}; the building at {cannonAt})");
        foreach (var system in new[] { "ParticleUplinkCannon_Magma", "ParticleUplinkCannon_Shockwave", "ParticleCannonUplink_HitFlare" })
        {
            var hits = ParticlesOf(scene, system);
            TestHarness.Assert(hits.Length > 0 && hits.All(at => Vector2.Distance(new Vector2(at.X, at.Y), beam) < 40f),
                $"{system} burns under the beam ({hits.Length} particles, first {hits.FirstOrDefault()}; the beam at {beam}, the building at {cannonAt})");
        }
    }

    /// <summary>
    /// <c>ParticleUplinkCannonUpdate::update</c>, "Create scorch marks periodically": <c>TotalScorchMarks</c> times in the beam's life <c>GameClient::addScorch</c> at the beam, <c>ScorchMarkScalar</c>
    /// times its current radius (<c>LaserUpdate::getCurrentLaserRadius</c>, half the <c>OuterBeamWidth</c> as it grows) wide; the marks stay. The port left none.
    /// </summary>
    private static void ScorchesAlongThePath(GameData data)
    {
        var scene = FireFromShortcut(data, out _);
        var (live, module) = (scene.Live, scene.Module);
        TestHarness.Assert(data.AttachedEffects.Uplinks["AmericaParticleCannonUplink"].ScorchMarkScalar == 2.4f, "the scorch scalar is read");
        scene.Seconds(1);
        TestHarness.Assert(live.Fx.Decals.Snapshot().All(decal => decal.Kind != DecalKind.Scorch), "no scorch before the beam is born");
        scene.Seconds(3);
        var beam = new Vector2(FixedConvert.ToSingle(module.BeamPosition.X), FixedConvert.ToSingle(module.BeamPosition.Y));
        var scorches = live.Fx.Decals.Snapshot().Where(decal => decal.Kind == DecalKind.Scorch).ToArray();
        var full = data.AttachedEffects.Lasers["ParticleUplinkCannon_OrbitalLaser"].OuterWidth * 0.5f * 2.4f;
        TestHarness.Assert(scorches.Length >= 2 && scorches.All(decal => Vector2.Distance(new Vector2(decal.Position.X, decal.Position.Y), beam) < 1f && decal.Radius <= full + 0.01f)
            && scorches.Any(decal => decal.Radius > full * 0.5f),
            $"scorch marks lie where the beam burns, up to 2.4 times its radius of {full / 2.4f} ({string.Join(", ", scorches.Select(decal => $"{decal.Position} r{decal.Radius:F1}"))})");

        // Driven away, the beam leaves its scorches behind; they stay when the shot is over.
        var far = new Vector2(420, 130);
        scene.Controller.Process(new MouseMoveEvent(40_000, far));
        scene.Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        scene.Controller.Process(new MouseButtonEvent(40_100, MouseButton.Right, true, far));
        scene.Controller.Process(new MouseButtonEvent(40_150, MouseButton.Right, false, far));
        scene.Seconds(16);
        TestHarness.Assert(!module.InUse, "the shot is over");
        var trail = live.Fx.Decals.Snapshot().Where(decal => decal.Kind == DecalKind.Scorch).ToArray();
        Console.WriteLine($"       {scorches.Length} scorches while the beam stood, {trail.Length} after the shot");
        TestHarness.Assert(trail.Length > scorches.Length && trail.Length <= 20 && trail.Select(decal => decal.Position).Distinct().Count() > 3,
            $"the scorches of TotalScorchMarks (20) lie along the beam's path and stay ({trail.Length}: {string.Join(", ", trail.Select(decal => $"{decal.Position.X:F0},{decal.Position.Y:F0}"))})");
    }

    /// <summary>
    /// <c>ParticleUplinkCannonUpdate::update</c>: while the cannon fires it plays its <c>BeamLaunchFX</c> every <c>DelayBetweenLaunchFX</c> with <c>FXList::doFXPos</c> at
    /// <c>m_laserOriginPosition</c>, the fire bone of the raised dish (<c>calculateUpBonePositions</c>). The port played it at the building's foot, inside the model.
    /// </summary>
    private static void LaunchFlareAtTheDish(GameData data)
    {
        var scene = FireFromShortcut(data, out _);
        var live = scene.Live;
        TestHarness.Assert(data.AttachedEffects.Uplinks["AmericaParticleCannonUplink"].BeamLaunchFx == "FX_ParticleUplinkCannon_BeamLaunchIteration", "the launch effect is read");
        var cannonZ = FixedConvert.ToSingle(scene.Cannon.Z);
        scene.Seconds(1);
        var dish = live.Attached.UplinkLaunchPlace(scene.Cannon.Id, "FX_ParticleUplinkCannon_BeamLaunchIteration");
        Console.WriteLine($"       the dish at {dish} (the building's foot at {cannonZ})");
        TestHarness.Assert(dish is { } known && known.Z > cannonZ + 20f, $"the dish's fire bone is known and high on the building ({dish}; the building stands at {cannonZ})");
        TestHarness.Assert(live.Attached.UplinkLaunchPlace(scene.Cannon.Id, "FX_ParticleUplinkCannon_BeamHitsGround") is null, "no other effect list of the cannon is moved");
        var flares = ParticlesOf(scene, "ParticleUplinkCannon_LaunchFlare");
        TestHarness.Assert(flares.Length > 0 && flares.All(at => Vector3.Distance(at, dish!.Value) < 30f), $"the launch flare plays at the dish ({flares.Length} particles, first {flares.FirstOrDefault()}; the dish at {dish})");
    }

    private static void Steering(GameData data)
    {
        var scene = FireFromShortcut(data, out var aimed);
        var (live, controller, module) = (scene.Live, scene.Controller, scene.Module);
        scene.Seconds(0.5);
        TestHarness.Assert(module.InUse && module.IsSteerable, "the cannon fires");
        TestHarness.Assert(controller.Input.Selection.Selected.Length == 1 && controller.Input.Selection.Selected[0] == scene.Cannon.Id, $"the launch from the shortcut bar selects the cannon ({controller.Input.Selection.Selected.Length} selected)");

        // The pointer over the map is the uplink cannon's.
        var second = new Vector2(420, 130);
        var secondGround = live.Camera.TryScreenToGround(second, out var target) ? target : throw new InvalidOperationException("the second place is not on the ground");
        controller.Process(new MouseMoveEvent(20_000, second));
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(controller.Cursor.Kind == CursorKind.ParticleUplinkCannon, $"the pointer is the uplink cannon's ({controller.Cursor.Kind})");

        // A click drives the beam towards it at the manual speed of 20 units a second.
        var distance = Distance(module.BeamPosition, secondGround.X, secondGround.Y);
        TestHarness.Assert(distance > 150f, $"setup: the second place is far from the beam ({distance}; first {aimed})");
        controller.Process(new MouseButtonEvent(20_100, MouseButton.Right, true, second));
        controller.Process(new MouseButtonEvent(20_150, MouseButton.Right, false, second));
        // The beam is born BeamTravelTime (2.5 s) after the order and drives from then on; what is clicked before is where it starts to drive to.
        scene.Seconds(2.5);
        var before = module.BeamPosition;
        scene.Seconds(2);
        var slow = Distance(module.BeamPosition, (float)before.X.ToDecimal(), (float)before.Y.ToDecimal());
        TestHarness.Assert(Math.Abs(slow - 40f) < 2f, $"the beam covers 20 units a second towards the click ({slow} in two seconds)");
        TestHarness.Assert(Distance(module.BeamPosition, secondGround.X, secondGround.Y) < distance - 38f, "and it came closer to it");

        // A second click inside the double click delay (a half second) drives it at the fast speed of 40.
        scene.Seconds(1);
        controller.Process(new MouseButtonEvent(30_100, MouseButton.Right, true, second));
        controller.Process(new MouseButtonEvent(30_150, MouseButton.Right, false, second));
        scene.Seconds(0.1);
        controller.Process(new MouseButtonEvent(30_300, MouseButton.Right, true, second));
        controller.Process(new MouseButtonEvent(30_350, MouseButton.Right, false, second));
        scene.Seconds(0.2);
        before = module.BeamPosition;
        scene.Seconds(1);
        var fast = Distance(module.BeamPosition, (float)before.X.ToDecimal(), (float)before.Y.ToDecimal());
        TestHarness.Assert(Math.Abs(fast - 40f) < 2f, $"after a second click the beam covers 40 units a second ({fast})");
        TestHarness.Assert(controller.Input.Selection.Selected.Length == 1, "and the cannon stays selected");
    }
}
