using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Presentation.Fx;
using CnCNew.Simulation.Modules;

namespace CnCNew.Game.Tests;

/// <summary>
/// The continuous effects objects carry (docs/PRESENTATION-FX.md, "Attached effects"), in staged scenes on a real map: the Dragon tank's flame (its projectile's
/// <c>BoneFXUpdate</c> trail), a building that is damaged (<c>TransitionDamageFX</c> and the body's fire and smoke), units set on fire, a missile's exhaust, and the particle
/// systems of model condition states. Each scene counts what was created and checks the particle systems by their template names - a program instead of an eye; whether
/// the flames look like the original's is a judgement of a person.
/// </summary>
internal static class AttachedEffectsTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(AttachedEffectsTests));
        harness.Run("The catalog: bone effects, damage transitions, the body's seven categories and the weapons' exhaust are read from the data", Catalog);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP AttachedEffectsTests with real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Real data: the catalog of the shipped objects", () => ShippedCatalog(data));
        harness.Run("The Dragon tank's flame: its projectiles carry the trail of BoneFXUpdate, and what it hits burns", () => DragonFlame(data));
        harness.Run("The Black Lotus's data stream: an arc from the unit to its target while the hack runs", () => DataStream(data));
        harness.Run("F-158: a Patriot battery that joins another's attack draws its data stream to the victim for the laser object's life", () => PatriotDataStream(data));
        harness.Run("W3DLaserDraw: the data stream is green, tiled by its width and scrolls along the beam as the data says", () => LaserLooks(data, args[index + 1]));
        harness.Run("A damaged building smokes and burns: TransitionDamageFX and the body's bones", () => DamagedBuilding(data));
        harness.Run("A building set on fire by the flame burns: the body's aflame category and the fire one size up", () => BurningBuilding(data));
        harness.Run("A missile carries its weapon's exhaust from its ignition", () => MissileExhaust(data));
        harness.Run("An effect projectile with Model = NULL draws no model", () => NullModel(data));
        harness.Run("A moving truck raises dust, a standing one none", () => TreadDebris(data));
        harness.Run("The Spectre's gattling strafes the ground with smoke while the gunship is seen", () => SpectreStrafe(data));
        harness.Run("A muzzle on a turret turns with the turret", () => TurretMuzzle(data));
        harness.Run("The acceptance scene acc-flames: what burns and smokes, by system", () => AcceptanceScene(data));
        return harness.Failures;
    }

    /// <summary>The owner saw every shot leave the barrel where it would be with the turret facing forward: effects read the rest pose of the model.</summary>
    private static void TurretMuzzle(GameData data)
    {
        TestHarness.Assert(data.Visuals.TryGet("AmericaTankCrusader", out var visual), "the Crusader has a visual");
        var state = visual.Draws.SelectMany(draw => draw.States).First(item => item.Model is not null && item.Turret.AngleBone is not null && !item.WeaponBones.IsDefaultOrEmpty && item.WeaponBones[0].FireFxBone is not null);
        var muzzle = state.WeaponBones[0].FireFxBone!;
        var model = state.Model!;
        TestHarness.Assert(state.Turret.AngleBone is not null, "its state names a turret");
        var pivot = data.ModelBones.Find(model, state.Turret.AngleBone!)!.Value;
        Matrix4x4 At(float yaw, string bone) => data.ModelBones.FindPosed(model, bone, new TurretPose(state.Turret.AngleBone, yaw, null, null, null, null, null, null))
            ?? data.ModelBones.FindPosed(model, bone + "01", new TurretPose(state.Turret.AngleBone, yaw, null, null, null, null, null, null))
            ?? throw new InvalidOperationException($"no bone {bone}");
        var forward = At(0f, muzzle);
        var aside = At(MathF.PI / 2, muzzle);
        var from = forward.Translation - pivot;
        var to = aside.Translation - pivot;
        TestHarness.Assert(from.Length() > 1f, "the muzzle stands off the pivot");
        TestHarness.Assert(MathF.Abs(to.X + from.Y) < 0.01f && MathF.Abs(to.Y - from.X) < 0.01f && MathF.Abs(to.Z - from.Z) < 0.01f, $"a quarter turn of the turret turns the muzzle: {from} -> {to}");
        var along = Vector3.TransformNormal(Vector3.UnitX, aside);
        var before = Vector3.TransformNormal(Vector3.UnitX, forward);
        TestHarness.Assert(MathF.Abs(along.X + before.Y) < 0.01f && MathF.Abs(along.Y - before.X) < 0.01f, "and the direction the fire effect is handed");
    }

    private static void Catalog()
    {
        TestHarness.Assert(AttachedEffectCatalog.Empty.Objects.IsEmpty && AttachedEffectCatalog.Empty.BodyRules.IsEmpty, "the empty catalog is empty");
    }

    private static void ShippedCatalog(GameData data)
    {
        var catalog = data.AttachedEffects;
        TestHarness.Assert(catalog.Problems.IsEmpty, $"every slot of the shipped data is read ({string.Join("; ", catalog.Problems.Take(5))})");
        TestHarness.Assert(catalog.BodyRules.Length == 7 && catalog.BodyRules[AttachedEffectCatalog.SmokeSmall] is { Prefix: "smokes", Max: > 0 } && catalog.BodyRules[AttachedEffectCatalog.Aflame].Max > 0,
            $"the body's seven categories from GameData ({string.Join(", ", catalog.BodyRules.Select(rule => $"{rule.Prefix}:{rule.System}:{rule.Max}"))})");
        TestHarness.Assert(catalog.Objects.TryGetValue("DragonTankFlameProjectile", out var flame) && flame.BoneFx.Count(slot => slot.State == 0 && slot.IsParticleSystem) == 3,
            "the Dragon tank's flame projectile has its three pristine particle systems (the patch took out the three that do nothing)");
        var transitions = catalog.Objects.Values.Count(template => !template.Transition.IsDefaultOrEmpty);
        var bodies = catalog.Objects.Values.Count(template => template.HasActiveBody);
        TestHarness.Assert(transitions > 100 && bodies > 500, $"damage transitions ({transitions}) and active bodies ({bodies})");
        TestHarness.Assert(catalog.WeaponExhausts.Count > 20, $"weapons with an exhaust ({catalog.WeaponExhausts.Count})");
        foreach (var name in catalog.Objects.Values.SelectMany(template => template.BoneFx.Where(slot => slot.IsParticleSystem).Select(slot => slot.Name)
                     .Concat(template.Transition.Where(slot => slot.IsParticleSystem).Select(slot => slot.Name))).Distinct().Order(StringComparer.Ordinal))
            TestHarness.Assert(data.Fx.FindParticleSystem(name) is not null, $"the particle system '{name}' exists");
    }

    private static readonly Vector2 Viewport = new(1280, 720);

    private static LiveScenario Stage(GameData data, string name, ImmutableArray<ScenarioUnit> units, ImmutableArray<ScenarioOrder> orders, long frames)
        => LiveScenario.Create(data, new DemoScenario(name, name, name, frames, units, orders, 0f, 0f) { HumanFaction = "FactionChina", OpponentFaction = "FactionAmerica" }, Viewport);

    private static void Run(LiveScenario scene, Action? each = null)
    {
        while (!scene.IsOver)
        {
            scene.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            each?.Invoke();
        }
    }

    /// <summary>The template names of the particle systems running now (the particle world keeps the template of each system).</summary>
    private static ImmutableArray<string> Running(LiveGame game) => game.Fx.Particles.RunningTemplates;

    /// <summary><c>W3DLaserDraw::doDrawModule</c> and the special object of <c>SpecialAbilityUpdate</c>.</summary>
    private static void DataStream(GameData data)
    {
        var catalog = AttachedEffectCatalog.Build(data.Graph);
        TestHarness.Assert(catalog.Lasers.TryGetValue("BinaryDataStream", out var spec) && spec is { Segments: 10, ArcHeight: 30f, Width: 5f } && spec.Texture.Length > 0, "the stream's draw module is read");
        var owner = data.Graph.Definitions.OfType<CnCNew.Data.Definitions.ObjectDefinition>().First(definition => catalog.LaserOwners.ContainsKey(definition.OriginalName) && definition.OriginalName.Contains("Lotus", StringComparison.OrdinalIgnoreCase));
        TestHarness.Assert(owner is not null, "the Black Lotus is one of its owners");
        var points = spec!.Points(new Vector3(0, 0, 10), new Vector3(100, 0, 10), (_, _) => 0f);
        TestHarness.Assert(points.Length == 11 && Math.Abs(points[0].X) < 1e-3f && Math.Abs(points[^1].X - 100f) < 1e-3f, "ten segments between the two ends");
        TestHarness.Assert(Math.Abs(points[5].Z - 40f) < 1e-3f && Math.Abs(points[0].Z - 10f) < 1e-3f, "the arc is highest in the middle and nothing at the ends");
        TestHarness.Assert(spec.Points(new Vector3(0, 0, 0), new Vector3(100, 0, 0), (_, _) => 50f).All(point => point.Z >= 52f), "it skims the ground and does not go through it");
    }

    /// <summary>
    /// <c>AssistedTargetingUpdate::makeFeedbackLaser</c> and <c>W3DLaserDraw</c>: the owner saw no animation when Patriot batteries pass a target on. The first battery fires; the second, out
    /// of reach of the victim, joins and draws <c>PatriotBinaryDataStream</c> from itself to the place the victim stood, for the 600 ms of the laser object's <c>DeletionUpdate</c> (18 frames).
    /// </summary>
    private static void PatriotDataStream(GameData data)
    {
        var catalog = data.AttachedEffects;
        TestHarness.Assert(catalog.Lasers.TryGetValue("PatriotBinaryDataStream", out var spec) && spec is { Segments: 5, ArcHeight: 30f, Width: 5f } && spec.Texture.Length > 0, "the stream's draw module is read");
        TestHarness.Assert(catalog.LaserFrames.GetValueOrDefault("PatriotBinaryDataStream") == 18, $"it lives 18 frames ({catalog.LaserFrames.GetValueOrDefault("PatriotBinaryDataStream")})");
        var scene = LiveScenario.Create(data, new DemoScenario("attached-patriots", "patriots", "patriots", 300,
            [new ScenarioUnit("a", "AmericaPatriotBattery", 2, 0f, 0f), new ScenarioUnit("b", "AmericaPatriotBattery", 2, 0f, 190f), new ScenarioUnit("p1", "AmericaPowerPlant", 2, 300f, -100f),
             new ScenarioUnit("p2", "AmericaPowerPlant", 2, 300f, -200f), new ScenarioUnit("dozer", "ChinaVehicleDozer", 1, -180f, 0f)],
            [new ScenarioOrder(30, "a", ScenarioOrderKind.Attack, "dozer")], 0f, 0f) { HumanFaction = "FactionChina", OpponentFaction = "FactionAmerica" }, Viewport);
        var framesWithBeam = 0;
        long? born = null;
        var scrolls = new SortedDictionary<long, float>();
        var borns = new Dictionary<long, long>();
        float Expected(long frame) { var value = spec!.ScrollRate * (frame - borns[frame]) / 30.0; return (float)(value - Math.Floor(value)); }
        var reached = false;
        var firstFrame = -1L;
        var lastFrame = -1L;
        Vector3? victimAt = null;
        Run(scene, () =>
        {
            if (scene.Tags.TryGetValue("dozer", out var dozerId) && scene.World.FindEntity(dozerId) is { } dozer && scene.Attached.Stats.Tracked > 0) victimAt ??= new Vector3((float)dozer.X.ToDecimal(), (float)dozer.Y.ToDecimal(), 0f);
            var beam = scene.Attached.ProjectileStreams.FirstOrDefault(stream => stream.Texture.StartsWith("EXBinaryStream32", StringComparison.OrdinalIgnoreCase) && stream.Points.Length >= 2);
            if (beam is null) return;
            framesWithBeam++;
            var frame = scene.World.Tick / scene.World.Time.TicksPerOriginalFrame;
            if (!scrolls.ContainsKey(frame - 1)) born = frame;
            scrolls[frame] = beam.Laser!.ScrollV;
            borns[frame] = born!.Value;
            if (firstFrame < 0) firstFrame = frame;
            lastFrame = frame;
            var b = scene.World.FindEntity(scene.Tags["b"])!;
            var start = beam.Points[0];
            var end = beam.Points[^1];
            if (Math.Abs(start.X - (float)b.X.ToDecimal()) < 2f && Math.Abs(start.Y - (float)b.Y.ToDecimal()) < 2f && victimAt is { } victim && Math.Abs(end.X - victim.X) < 40f && Math.Abs(end.Y - victim.Y) < 40f) reached = true;
        });
        Console.WriteLine($"       patriots: frames with the stream {framesWithBeam} ({firstFrame}..{lastFrame}); {scene.Attached.Stats}");
        TestHarness.Assert(framesWithBeam > 0, "the data stream is drawn");
        TestHarness.Assert(scrolls.Count > 1 && scrolls.All(pair => Math.Abs(pair.Value - Expected(pair.Key)) < 1e-4f), $"its texture is where {Math.Abs(spec!.ScrollRate)} per second puts it in every frame ({scrolls.Count} frames)");
        TestHarness.Assert(scrolls.Where(pair => borns[pair.Key] < pair.Key).All(pair => scrolls.TryGetValue(pair.Key - 1, out var before) && Math.Abs(before - pair.Value) > 1e-4f), "and moves in every frame of the original, once, at any rate");
        TestHarness.Assert(reached, "it runs from the assisting battery to the place of the victim");
        TestHarness.Assert(framesWithBeam <= 18 * 2, $"and only for the life of the laser object ({framesWithBeam} frames)");
        TestHarness.Assert(scene.Fx.Capture().ProjectileStreams.Length == scene.Attached.ProjectileStreams.Length, "the frame the renderer gets carries it");
    }

    /// <summary>
    /// The owner saw only white outlines that did not move. <c>W3DLaserDraw</c> draws the (grey on black) texture additive in the colour of the laser - the inner colour times its alpha for a
    /// single beam -, U across the beam and V along it, tiled so that it is not stretched (length over width times the texture's width over height times <c>TilingScalar</c>, at most 50) and
    /// moved along by <c>ScrollRate</c> every second of the original's time. Checked on the shipped data: every laser object's keys, the Patriot's colour and tiling by the real texture
    /// file, the scroll at the three rates by the original frame.
    /// </summary>
    private static void LaserLooks(GameData data, string contentDir)
    {
        var catalog = data.AttachedEffects;
        TestHarness.Assert(catalog.Lasers.Count >= 10, $"the shipped lasers are read ({catalog.Lasers.Count})");
        var colourless = catalog.Lasers.Where(pair => pair.Value.NumBeams > 0 && pair.Value.InnerColor.W <= 0f).Select(pair => pair.Key).ToArray();
        TestHarness.Assert(colourless.Length == 0, $"every laser has an inner colour ({string.Join(", ", colourless)})");

        var spec = catalog.Lasers["PatriotBinaryDataStream"];
        TestHarness.Assert(spec.NumBeams == 1 && spec.Tile && spec.Segments == 5 && Math.Abs(spec.ScrollRate + 0.25f) < 1e-6f && Math.Abs(spec.TilingScalar - 0.66f) < 1e-6f && spec.SegmentOverlapRatio == 0f,
            $"the keys: one beam, tiled, five segments, scroll {spec.ScrollRate}, scalar {spec.TilingScalar}");
        TestHarness.Assert(spec.InnerColor == new Vector4(0f, 1f, 0f, 180f / 255f), $"inner colour R:0 G:255 B:0 A:180 ({spec.InnerColor})");
        var bands = spec.Bands();
        TestHarness.Assert(bands.Length == 1 && bands[0].Width == 5f && Vector3.Distance(bands[0].Color, new Vector3(0f, 180f / 255f, 0f)) < 1e-6f, "one band, 5 wide, green at the inner alpha (not the texture's own white)");

        // A laser object with several beams: widths and colours step from the inner to the outer one, the outermost first.
        var many = catalog.Lasers.Values.First(laser => laser.NumBeams > 2);
        var manyBands = many.Bands();
        TestHarness.Assert(manyBands.Length == many.NumBeams && Math.Abs(manyBands[0].Width - many.OuterWidth) < 1e-5f && Math.Abs(manyBands[^1].Width - many.Width) < 1e-5f && manyBands[0].Width >= manyBands[^1].Width,
            $"{many.Texture}: {many.NumBeams} bands from the outer width {many.OuterWidth} to the inner {many.Width}");

        // The tiling by the texture file the data names.
        var path = CnCNew.W3D.W3dTexturePathResolver.Resolve(spec.Texture, candidate => File.Exists(Path.Combine(contentDir, candidate))).VirtualPath;
        TestHarness.Assert(path is not null, $"{spec.Texture} resolves in the content");
        var header = File.ReadAllBytes(Path.Combine(contentDir, path!));
        var height = BitConverter.ToInt32(header, 12);
        var width = BitConverter.ToInt32(header, 16);
        TestHarness.Assert(width == 32 && height == 256, $"the texture is {width} x {height}: narrow and tall");
        var draw = spec.Draw(new Vector3(0f, 0f, 20f), new Vector3(300f, 0f, 20f), (_, _) => 0f, 0);
        var laser = draw.Laser!;
        TestHarness.Assert(laser.Segments.Length == 5 && laser.Bands.Length == 1 && laser.ScrollV == 0f, "five segments, one band, no scroll on the first frame");
        var length = Vector3.Distance(laser.Segments[2].From, laser.Segments[2].To);
        var expected = length / 5f * (width / (float)height) * 0.66f;
        TestHarness.Assert(Math.Abs(laser.TileFactor(length, laser.Bands[0].Width, width / (float)height) - expected) < 1e-5f && expected is > 0.5f and < 50f, $"a segment of {length:F1} units tiles {expected:F2} times, not once");
        TestHarness.Assert(laser.TileFactor(1000000f, 5f, 1f) == LaserDraw.MaxTileFactor, "and never more than 50 times");
        var untiled = spec with { Tile = false };
        TestHarness.Assert(untiled.Draw(Vector3.Zero, new Vector3(300f, 0f, 0f), (_, _) => 0f, 0).Laser!.TileFactor(100f, 5f, 0.125f) == 1f, "a laser that does not tile shows its texture once per segment");

        // The scroll depends on the frames of the original only: -0.25 per second is a 30th of that per frame, wrapped into [0, 1).
        float Scroll(long frames) => spec.Draw(Vector3.Zero, new Vector3(300f, 0f, 0f), (_, _) => 0f, frames).Laser!.ScrollV;
        TestHarness.Assert(Math.Abs(Scroll(30) - 0.75f) < 1e-6f && Math.Abs(Scroll(60) - 0.5f) < 1e-6f && Math.Abs(Scroll(120) - 0f) < 1e-6f && Math.Abs(Scroll(1) - (1f - 0.25f / 30f)) < 1e-6f, "a second moves it a quarter of the texture's length against the beam's direction");
    }

    private static void DragonFlame(GameData data)
    {
        var scene = Stage(data, "attached-dragon",
            [new ScenarioUnit("dragon", "ChinaTankDragon", 1, 0f, 0f), new ScenarioUnit("r1", "AmericaInfantryRanger", 2, 70f, 0f), new ScenarioUnit("r2", "AmericaInfantryRanger", 2, 75f, 12f),
             new ScenarioUnit("r3", "AmericaInfantryRanger", 2, 75f, -12f)],
            [new ScenarioOrder(15, "dragon", ScenarioOrderKind.Attack, "r1")], 240);
        var trails = 0;
        var aflame = 0;
        var streams = 0;
        var aflameStatus = ObjectStatusBits.Index("AFLAME");
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Run(scene, () =>
        {
            foreach (var name in Running(scene)) names.Add(name);
            if (Running(scene).Any(name => name.StartsWith("FlameThrowerTrail", StringComparison.OrdinalIgnoreCase))) trails++;
            if (scene.World.Entities.Any(entity => entity.HasStatus(aflameStatus))) aflame++;
            if (scene.Attached.ProjectileStreams.Any(stream => stream.Texture.StartsWith("EXfthrowerStream", StringComparison.OrdinalIgnoreCase) && stream.Points.Length >= 2)) streams++;
        });
        var stats = scene.Attached.Stats;
        Console.WriteLine($"       dragon: {stats}; frames with a flame trail {trails}; frames with a burning unit {aflame}; systems seen: {string.Join(", ", names)}");
        TestHarness.Assert(stats.BoneFxSystems > 0 && trails > 10, $"the flame's trail runs ({stats.BoneFxSystems} systems, in {trails} frames)");
        TestHarness.Assert(stats.StreamProjectiles > 0 && streams > 10, $"the flame's stream is drawn through its projectiles ({stats.StreamProjectiles} projectiles, a band in {streams} frames)");
        TestHarness.Assert(scene.Fx.Capture().ProjectileStreams.Length == scene.Attached.ProjectileStreams.Length, "the frame the renderer gets carries the streams");
        TestHarness.Assert(aflame == 0 || stats.BodySystems + stats.ModelStateSystems > 0, "what burns shows it");
    }

    private static void DamagedBuilding(GameData data)
    {
        var scene = Stage(data, "attached-building",
            [new ScenarioUnit("plant", "AmericaPowerPlant", 2, 80f, 0f), new ScenarioUnit("dozer", "ChinaVehicleDozer", 1, -40f, 0f)],
            [new ScenarioOrder(10, "plant", ScenarioOrderKind.Damage, X: 40f), new ScenarioOrder(60, "plant", ScenarioOrderKind.Damage, X: 35f)], 150);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Run(scene, () => { foreach (var name in Running(scene)) names.Add(name); });
        var stats = scene.Attached.Stats;
        Console.WriteLine($"       building: {stats}; systems seen: {string.Join(", ", names)}; bones the model lacks: {string.Join(", ", scene.Attached.MissingBoneNames)}");
        TestHarness.Assert(stats.TransitionSystems + stats.BodySystems + stats.ModelStateSystems > 0, "the damaged building shows fire or smoke");
        TestHarness.Assert(names.Any(name => name.Contains("Smoke", StringComparison.OrdinalIgnoreCase) || name.Contains("Fire", StringComparison.OrdinalIgnoreCase)), "smoke or fire runs at it");
    }

    private static void BurningBuilding(GameData data)
    {
        var scene = Stage(data, "attached-burning",
            [new ScenarioUnit("dragon", "ChinaTankDragon", 1, 0f, 0f), new ScenarioUnit("barracks", "AmericaBarracks", 2, 95f, 0f)],
            [new ScenarioOrder(15, "dragon", ScenarioOrderKind.Attack, "barracks")], 420);
        var aflameStatus = ObjectStatusBits.Index("AFLAME");
        var burningFrames = 0;
        var fireWhileBurning = 0;
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Run(scene, () =>
        {
            if (!scene.Tags.TryGetValue("barracks", out var id) || scene.World.FindEntity(id) is not { } barracks || !barracks.HasStatus(aflameStatus)) return;
            burningFrames++;
            var running = Running(scene);
            foreach (var name in running) names.Add(name);
            if (running.Any(name => name.StartsWith("FireBuilding", StringComparison.OrdinalIgnoreCase) || name.StartsWith("FireFaction", StringComparison.OrdinalIgnoreCase))) fireWhileBurning++;
        });
        var stats = scene.Attached.Stats;
        Console.WriteLine($"       burning: {stats}; frames the barracks burned {burningFrames}, with fire on it {fireWhileBurning}; systems seen while it burned: {string.Join(", ", names)}");
        TestHarness.Assert(burningFrames > 0, "the flame sets the barracks on fire (FlammableUpdate)");
        TestHarness.Assert(fireWhileBurning > burningFrames / 2, $"while it burns, fire runs at its bones ({fireWhileBurning} of {burningFrames} frames)");
    }

    private static void MissileExhaust(GameData data)
    {
        var scene = Stage(data, "attached-missile",
            [new ScenarioUnit("hunter", "ChinaInfantryTankHunter", 1, 0f, 0f), new ScenarioUnit("tank", "AmericaTankCrusader", 2, 120f, 0f)],
            [new ScenarioOrder(15, "hunter", ScenarioOrderKind.Attack, "tank")], 180);
        Run(scene);
        var stats = scene.Attached.Stats;
        Console.WriteLine($"       missile: {stats}");
        TestHarness.Assert(stats.ExhaustSystems > 0, $"the missiles carry an exhaust ({stats.ExhaustSystems})");
    }

    private static void TreadDebris(GameData data)
    {
        // The shipped tanks name no tread debris; the GLA's trucks raise dust (W3DTruckDraw Dust).
        var scene = Stage(data, "attached-dust", [new ScenarioUnit("truck", "GLAVehicleRocketBuggy", 1, 0f, 0f)], [new ScenarioOrder(30, "truck", ScenarioOrderKind.MoveTo, X: 150f, Y: 0f)], 240);
        long beforeMove = -1;
        Run(scene, () => { if (beforeMove < 0 && scene.World.Tick >= scene.World.Time.OriginalFrames(25)) beforeMove = scene.Attached.Stats.MotionSystems; });
        var stats = scene.Attached.Stats;
        Console.WriteLine($"       treads: {stats}");
        TestHarness.Assert(beforeMove == 0 && stats.MotionSystems > 0, $"no debris while it stands, debris once it drives ({beforeMove}, {stats.MotionSystems})");
    }

    /// <summary><c>SpectreGunshipUpdate::update</c>: the gattling's smoke on the ground toward the target, once a frame while the gunship orbits and fires and the viewer sees it.</summary>
    private static void SpectreStrafe(GameData data)
    {
        var scenario = new DemoScenario("attached-spectre", "attached-spectre", "attached-spectre", 2400,
            [new ScenarioUnit("centre", "AirF_AmericaCommandCenter", 1, -260, -80, 0f)],
            [new ScenarioOrder(30, "centre", ScenarioOrderKind.UsePower, X: 40f, Y: 0f, Template: "AirF_SuperweaponSpectreGunship")], 0f, 0f)
        {
            HumanFaction = "FactionAmericaAirForceGeneral",
            OpponentFaction = "FactionChina",
            PowersReady = true,
            Sciences = ["SCIENCE_SpectreGunship1"],
        };
        var scene = LiveScenario.Create(data, scenario, Viewport);
        long beforeOrbit = -1;
        Run(scene, () => { if (beforeOrbit < 0 && scene.World.Tick >= scene.World.Time.OriginalFrames(60)) beforeOrbit = scene.Attached.StrafeSystemsStarted; });
        Console.WriteLine($"       spectre: {scene.Attached.StrafeSystemsStarted} strafing systems");
        TestHarness.Assert(beforeOrbit == 0 && scene.Attached.StrafeSystemsStarted > 30, $"no smoke before the gunship orbits, a system a frame while it strafes ({beforeOrbit}, {scene.Attached.StrafeSystemsStarted})");
    }

    private static void AcceptanceScene(GameData data)
    {
        var scene = LiveScenario.Create(data, AcceptanceScenes.Flames, Viewport);
        foreach (var at in new long[] { 45, 120, 330 })
        {
            while (scene.World.Tick < scene.World.Time.OriginalFrames(at)) scene.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            var particles = scene.Fx.Particles.Capture().Particles;
            var bySystem = particles.GroupBy(particle => particle.SystemId)
                .Select(group => (Name: scene.Fx.Particles.TemplateNameOf(group.Key) ?? "?", Count: group.Count(), Largest: group.Max(particle => particle.Size)))
                .GroupBy(entry => entry.Name).Select(group => $"{group.Key} x{group.Count()} ({group.Sum(entry => entry.Count)} particles, largest {group.Max(entry => entry.Largest):0})")
                .Order(StringComparer.Ordinal);
            Console.WriteLine($"       frame {at}: {string.Join("; ", bySystem)}");
        }
        TestHarness.Assert(scene.Attached.Stats.BoneFxSystems > 0 && scene.Attached.Stats.BodySystems + scene.Attached.Stats.TransitionSystems > 0, "the scene shows the flame and what burns");
    }

    private static void NullModel(GameData data)
    {
        TestHarness.Assert(data.Visuals.TryGet("DragonTankFlameProjectile", out var visual) && visual.Draws.All(draw => draw.States.All(state => state.Model is null)),
            "the flame projectile's Model = NULL is no model to load (no placeholder is drawn for it)");
    }
}
