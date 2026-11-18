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
        harness.Run("LaserUpdate: the laser objects' muzzle and target particle systems stand at the beam's ends for the laser's life", () => LaserFlares(data));
        harness.Run("#443, #444: a point-defence laser that shoots a missile down draws its beam, though the missile is gone in the tick of the shot", () => PointDefenseBeams(data));
        harness.Run("A damaged building smokes and burns: TransitionDamageFX and the body's bones", () => DamagedBuilding(data));
        harness.Run("A building set on fire by the flame burns: the body's aflame category and the fire one size up", () => BurningBuilding(data));
        harness.Run("A missile carries its weapon's exhaust from its ignition", () => MissileExhaust(data));
        harness.Run("An effect projectile with Model = NULL draws no model", () => NullModel(data));
        harness.Run("The nuclear missile is drawn nose first with its exhaust and the delivery decal, falls in sight, and its blast makes every system of FX_Nuke", () => NuclearMissile(data));
        harness.Run("A particle of the nuclear fallout grows, glows and fades as RadiationFieldLarge's keys say", () => FalloutParticle(data));
        harness.Run("A moving truck raises dust, a standing one none", () => TreadDebris(data));
        harness.Run("The Spectre's gattling strafes the ground with smoke while the gunship is seen", () => SpectreStrafe(data));
        harness.Run("A muzzle on a turret turns with the turret", () => TurretMuzzle(data));
        harness.Run("#369: a broken hydrant sprays water - the model EXHydrant is a W3D emitter that runs at the WaterSpray object for its life", () => HydrantSpray(data));
        harness.Run("#398: nothing an object carries emits after the object has left - model state systems, emitters, the body's fire and smoke, bone and transition effects", () => NothingOutlivesItsObject(data));
        harness.Run("The acceptance scene acc-flames: what burns and smokes, by system", () => AcceptanceScene(data));
        return harness.Failures;
    }

    /// <summary>
    /// #398: when an object is deleted the original stops everything it carries - <c>W3DModelDraw</c>'s destructor <c>stopClientParticleSystems</c>, <c>ActiveBody::onDelete</c>'s
    /// <c>deleteAllParticleSystems</c>, <c>BoneFXUpdate</c>'s <c>killRunningParticleSystems</c>, and a system attached to an object that is gone destroys itself (<c>TransitionDamageFX::onDelete</c>
    /// relies on that); each calls <c>ParticleSystem::destroy</c>, which stops the emission and lets the particles in the air finish. A W3D emitter that was a state's model leaves the scene with
    /// the drawable. A scene where things die in every way: a GLA building burnt and destroyed (its rebuild hole stays), a hydrant whose spray ends, missiles with their exhaust, a Dragon's
    /// flame with its trail. Every system and emitter attached to an object is noted while the object is there; two drawn frames after it left the world, or after it died and is no
    /// longer drawn, none of them may still emit.
    /// </summary>
    private static void NothingOutlivesItsObject(GameData data)
    {
        var scene = Stage(data, "attached-outlives",
            [new ScenarioUnit("barracks", "GLABarracks", 2, 120f, 60f), new ScenarioUnit("hydrant", "FireHydrantRed", 2, 60f, -60f), new ScenarioUnit("dragon", "ChinaTankDragon", 1, 0f, 60f),
             new ScenarioUnit("hunter", "ChinaInfantryTankHunter", 1, 0f, -140f), new ScenarioUnit("tank", "AmericaTankCrusader", 2, 120f, -140f)],
            [new ScenarioOrder(10, "hydrant", ScenarioOrderKind.Damage, X: 200f), new ScenarioOrder(10, "barracks", ScenarioOrderKind.Damage, X: 60f),
             new ScenarioOrder(15, "dragon", ScenarioOrderKind.Attack, "barracks"), new ScenarioOrder(15, "hunter", ScenarioOrderKind.Attack, "tank"),
             new ScenarioOrder(240, "barracks", ScenarioOrderKind.Damage, X: 100f)], 480);
        var systems = new Dictionary<uint, HashSet<int>>();
        var emitters = new Dictionary<uint, HashSet<int>>();
        var goneFor = new Dictionary<uint, int>();
        var outlived = new List<string>();
        var departed = 0;
        Run(scene, () =>
        {
            var drawn = scene.ObjectFrame.Drawables.Select(drawable => drawable.Entity.Value).ToHashSet();
            foreach (var entity in scene.World.Entities)
            {
                foreach (var id in scene.Fx.Particles.EmittingAttachedTo(entity.Id)) (systems.TryGetValue(entity.Id.Value, out var set) ? set : systems[entity.Id.Value] = []).Add(id);
                foreach (var id in scene.Fx.ModelEmitters.EmittingAt(entity.Id)) (emitters.TryGetValue(entity.Id.Value, out var set) ? set : emitters[entity.Id.Value] = []).Add(id);
            }
            foreach (var id in systems.Keys.Union(emitters.Keys).ToList())
            {
                var entity = scene.World.FindEntity(new CnCNew.Simulation.Ids.EntityId(id));
                var gone = entity is null || (entity.EffectivelyDead && !drawn.Contains(id));
                if (!gone) { goneFor.Remove(id); continue; }
                var frames = goneFor[id] = goneFor.GetValueOrDefault(id) + 1;
                if (frames == 1) departed++;
                if (frames <= 2) continue;
                var name = entity?.Template.Name ?? "removed";
                foreach (var system in systems.GetValueOrDefault(id) ?? [])
                    if (scene.Fx.Particles.IsEmitting(system)) outlived.Add($"{id} {name}: system {system} {scene.Fx.Particles.TemplateNameOf(system)}");
                foreach (var emitter in emitters.GetValueOrDefault(id) ?? [])
                    if (scene.Fx.ModelEmitters.IsEmitting(emitter)) outlived.Add($"{id} {name}: emitter {emitter}");
            }
        });
        var stats = scene.Attached.Stats;
        Console.WriteLine($"       {systems.Count} objects carried systems, {emitters.Count} emitters, {departed} departures; {stats}");
        TestHarness.Assert(departed >= 4 && stats.BodySystems > 0 && stats.ModelEmitters > 0 && stats.ExhaustSystems > 0 && stats.BoneFxSystems > 0,
            $"objects with fire, smoke, an emitter, an exhaust and a trail left the scene ({departed}; {stats})");
        TestHarness.Assert(outlived.Count == 0, $"nothing still emits for an object that left ({outlived.Count}: {string.Join("; ", outlived.Distinct().Take(8))})");

        // A dead object stays in the world while it is no longer drawn: the Dragon's hull smokes (TransitionDamageFX's RubbleParticleSystem) through its slow death of 18 s, and a
        // script takes the viewer's sight of the map away (the staging's reveal undone, SHROUD_MAP) while it sinks. What it carried stops then; it does not run on until the hull is removed.
        var dying = Stage(data, "attached-dying-unseen",
            [new ScenarioUnit("hull", "ChinaTankDragonDeadHull", 2, 0f, 0f), new ScenarioUnit("dozer", "ChinaVehicleDozer", 1, -900f, -900f)], [], 150);
        var carried = new HashSet<int>();
        var switched = -1L;
        var unseenFrames = 0;
        var stillEmitting = new List<string>();
        Run(dying, () =>
        {
            if (!dying.Tags.TryGetValue("hull", out var id)) return;
            var hull = dying.World.FindEntity(id);
            if (switched < 0)
            {
                foreach (var system in dying.Fx.Particles.EmittingAttachedTo(id)) carried.Add(system);
                if (hull is { EffectivelyDead: true } && dying.ObjectFrame.Drawables.Any(drawable => drawable.Entity == id) && dying.Fx.Particles.EmittingAttachedTo(id).Length > 0)
                {
                    switched = dying.World.Tick;
                    var viewer = new CnCNew.Simulation.Ids.PlayerId(1);
                    dying.World.RevealMapPermanently(viewer, false);
                    dying.World.ShroudMap(viewer);
                }
                return;
            }
            if (hull is null || dying.ObjectFrame.Drawables.Any(drawable => drawable.Entity == id)) return;
            if (++unseenFrames <= 2) return;
            foreach (var system in carried)
                if (dying.Fx.Particles.IsEmitting(system)) stillEmitting.Add($"{system} {dying.Fx.Particles.TemplateNameOf(system)}");
        });
        Console.WriteLine($"       the dying hull carried {carried.Count} systems; the map shrouded at tick {switched}, then {unseenFrames} drawn frames in the world and not drawn");
        TestHarness.Assert(carried.Count > 0 && switched >= 0 && unseenFrames > 2, $"the hull smoked, died in view and stayed in the world unseen ({carried.Count}, {switched}, {unseenFrames})");
        TestHarness.Assert(stillEmitting.Count == 0, $"nothing it carried emits while it dies unseen ({string.Join("; ", stillEmitting.Distinct().Take(8))})");
    }

    /// <summary>
    /// Issue 369: <c>WaterSpray</c> (<c>OCL_FireHydrantExplode</c>, made where a hydrant dies, gone after its <c>LifetimeUpdate</c> of 5 s) draws <c>EXHydrant</c>, a W3D file of one particle
    /// emitter; the original's asset manager makes a <c>ParticleEmitterClass</c> of it. The emitter runs while the object stands and stops with it; its particles fly up and fall.
    /// </summary>
    private static void HydrantSpray(GameData data)
    {
        var spec = data.ModelBones.FindEmitter("EXHydrant");
        TestHarness.Assert(spec is { Texture: "exhydrant.tga", Shader: ParticleShader.AlphaTest, BurstSize: 5 } && spec.NotReproduced.IsEmpty,
            $"EXHydrant runs as an alpha-tested emitter of five-particle bursts ({spec}; problems: {string.Join("; ", data.ModelBones.EmitterProblems)})");
        TestHarness.Assert(data.ModelBones.FindEmitter("AVCrusader") is null, "a model of meshes is no emitter");
        var scene = Stage(data, "attached-hydrant",
            [new ScenarioUnit("hydrant", "FireHydrantRed", 2, 60f, 0f), new ScenarioUnit("dozer", "ChinaVehicleDozer", 1, -40f, 0f)],
            [new ScenarioOrder(10, "hydrant", ScenarioOrderKind.Damage, X: 200f)], 330);
        var sprayFrames = 0;
        var mostParticles = 0;
        var highest = float.MinValue;
        var sprayLived = false;
        var stoppedAfterSpray = false;
        var sprayTexture = true;
        Run(scene, () =>
        {
            var spray = scene.World.Entities.FirstOrDefault(entity => entity.Template.Name == "WaterSpray" && !entity.EffectivelyDead);
            var emitters = scene.Fx.ModelEmitters.Stats;
            if (spray is not null)
            {
                sprayLived = true;
                if (emitters.Emitting > 0) sprayFrames++;
                mostParticles = Math.Max(mostParticles, emitters.Particles);
                var ground = CnCNew.Presentation.FixedConvert.ToSingle(spray.Z);
                var frame = scene.Fx.Capture().Particles;
                // The emitter's particles carry its (negative) id and the batch of its texture; the hydrant's death effect adds particle systems of its own.
                foreach (var particle in frame.Particles.Where(particle => particle.SystemId < 0))
                {
                    sprayTexture &= frame.Batches[particle.Batch].Texture == "exhydrant.tga";
                    highest = Math.Max(highest, particle.Position.Z - ground);
                }
            }
            else if (sprayLived && emitters.Emitting == 0) stoppedAfterSpray = true;
        });
        Console.WriteLine($"       hydrant: spray emitted in {sprayFrames} frames, at most {mostParticles} particles, highest {highest:F1} above the ground; {scene.Attached.Stats}");
        TestHarness.Assert(sprayLived, "the dead hydrant leaves its WaterSpray");
        TestHarness.Assert(scene.Attached.Stats.ModelEmitters >= 1 && sprayFrames > 100, $"the spray's model emits while it stands ({sprayFrames} frames)");
        // 30.3 bursts of five a second for 1.5 s: about 225 particles at once (the buffer holds 378).
        TestHarness.Assert(mostParticles is > 150 and <= 378, $"the buffer fills to its lifetime's worth ({mostParticles})");
        TestHarness.Assert(sprayTexture, "the spray's particles draw its texture");
        // Up at 80 a second against 100 down: 32 units; the outward 7 a second points along the creation place, which can point up: at most 87^2 / 200 = 37.8.
        TestHarness.Assert(highest is > 32f and < 38f, $"the water climbs 32 to 38 units before it falls ({highest:F1})");
        TestHarness.Assert(stoppedAfterSpray, "the emitter stops when the spray's life ends");
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
        var many = catalog.Lasers.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).First(laser => laser.NumBeams > 2 && laser.OuterWidth >= laser.Width);
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

    /// <summary>
    /// <c>LaserUpdate::clientUpdate</c>: a laser object's <c>MuzzleParticleSystem</c> is made at the beam's start and its <c>TargetParticleSystem</c> at the end the first time it updates,
    /// both follow the ends, and they end with the laser object. Shipped data: which laser objects name which systems, and a Laser General Paladin's shot (a laser that lives 95 ms).
    /// </summary>
    private static void LaserFlares(GameData data)
    {
        var lasers = data.AttachedEffects.Lasers;
        (string, string) Names(string laser) => (lasers[laser].MuzzleSystem, lasers[laser].TargetSystem);
        TestHarness.Assert(Names("AvengerPointDefenseLaserBeam") == ("PaladinPointDefenseLaserFlare", "AvengerDestroySmoke"), "the Avenger's point defense laser: a flare at the muzzle, smoke at the target");
        TestHarness.Assert(Names("AirF_PointDefenseLaserBeam") == ("PaladinPointDefenseLaserFlare", "GenericLaserFlare") && Names("SupW_PointDefenseDroneLaserBeam") == ("PaladinPointDefenseLaserFlare", "GenericLaserFlare"), "the Air Force's and the drones' point defense lasers");
        TestHarness.Assert(Names("Lazr_PaladinLaserBeam") == ("GenericBlueLaserFlare", "GenericBlueLaserFlare") && Names("Lazr_CrusaderLaserBeam") == ("PaladinPointDefenseLaserFlare", "GenericRedLaserFlare")
            && Names("Lazr_PatriotLaserBeam") == ("PaladinPointDefenseLaserFlare", "GenericRedLaserFlare") && Names("Lazr_HowitzerLaserBeam") == ("GenericLaserFlare", "GenericLaserFlare"), "the Laser General's beams");
        TestHarness.Assert(Names("AvengerLaserBeam") == ("PaladinPointDefenseLaserFlare", "AvengerStarburst"), "the Avenger's cannon beam: a flare and a starburst");
        TestHarness.Assert(Names("PatriotBinaryDataStream") == ("", ""), "the Patriot's data stream names none");
        var named = lasers.Where(pair => pair.Value.MuzzleSystem.Length + pair.Value.TargetSystem.Length > 0).ToArray();
        TestHarness.Assert(named.Length >= 10, $"every laser object that names one is read ({named.Length})");
        var unknown = named.SelectMany(pair => new[] { pair.Value.MuzzleSystem, pair.Value.TargetSystem }).Where(name => name.Length > 0 && data.Fx.FindParticleSystem(name) is null).Distinct().ToArray();
        TestHarness.Assert(unknown.Length == 0, $"and every system they name exists ({string.Join(", ", unknown)})");
        TestHarness.Assert(data.AttachedEffects.LaserFrames["Lazr_PaladinLaserBeam"] == 3, "the Paladin's laser lives 95 ms, three frames, by its LifetimeUpdate");

        var scene = LiveScenario.Create(data, new DemoScenario("attached-laser-flares", "paladin", "paladin", 300,
            [new ScenarioUnit("a", "Lazr_AmericaTankPaladin", 2, 0f, 0f), new ScenarioUnit("dozer", "ChinaVehicleDozer", 1, 120f, 0f)],
            [new ScenarioOrder(30, "a", ScenarioOrderKind.Attack, "dozer")], 0f, 0f) { HumanFaction = "FactionChina", OpponentFaction = "FactionAmerica" }, Viewport);
        var shotFrames = new SortedSet<long>();
        var withFlares = new SortedSet<long>();
        var placed = 0;
        var ran = false;
        Run(scene, () =>
        {
            var frame = scene.World.Tick / scene.World.Time.TicksPerOriginalFrame;
            var beam = scene.Attached.ProjectileStreams.FirstOrDefault(stream => stream.Texture.StartsWith("EXLaser4", StringComparison.OrdinalIgnoreCase) && stream.Laser is not null);
            var flares = scene.Attached.FlareSystems;
            if (beam is not null) shotFrames.Add(frame);
            if (flares.Length > 0) withFlares.Add(frame);
            if (flares.Length > 0 && Running(scene).Contains("GenericBlueLaserFlare")) ran = true;
            if (beam is null || flares.Length != 2) return;
            foreach (var particle in scene.Fx.Particles.Capture().Particles)
            {
                var at = particle.Position;
                if (particle.SystemId == flares[0] && Vector3.Distance(at, beam.Points[0]) < 40f) placed++;
                if (particle.SystemId == flares[1] && Vector3.Distance(at, beam.Points[^1]) < 40f) placed++;
            }
        });
        Console.WriteLine($"       paladin: beam in frames {string.Join(",", shotFrames)}; flares in frames {string.Join(",", withFlares)}; {placed} particles near the ends");
        TestHarness.Assert(shotFrames.Count > 0, "the Paladin's beam is drawn");
        TestHarness.Assert(withFlares.SetEquals(shotFrames), "the two systems exist exactly while the beam is drawn, and are gone when the laser dies");
        TestHarness.Assert(placed > 0, "and their particles stand at the muzzle and at the target");
        TestHarness.Assert(ran, "the flare systems run");
    }

    /// <summary>
    /// <c>Weapon::createLaser</c>: the beam is made in the shot with the victim's place of that moment, and <c>LaserUpdate</c> keeps that place when the victim is gone. A point-defence laser
    /// kills a missile in the tick it fires, so the missile has left the world before the frame is drawn. A Paladin and an Avenger stand while two Tank Hunters fire at each; for every
    /// missile a laser shoots down a laser beam is drawn that ends where the missile was last seen.
    /// </summary>
    private static void PointDefenseBeams(GameData data)
    {
        foreach (var tank in new[] { "AmericaTankPaladin", "AmericaTankAvenger" })
        {
            var scene = Stage(data, "attached-point-defense-" + tank,
                [new ScenarioUnit("tank", tank, 2, 0f, 0f), new ScenarioUnit("h1", "ChinaInfantryTankHunter", 1, 150f, 0f), new ScenarioUnit("h2", "ChinaInfantryTankHunter", 1, -150f, 0f)],
                [new ScenarioOrder(15, "h1", ScenarioOrderKind.Attack, "tank"), new ScenarioOrder(15, "h2", ScenarioOrderKind.Attack, "tank")], 300);
            var seen = new Dictionary<uint, Vector3>();
            var shotDown = new Dictionary<uint, Vector3>();
            var drawn = new SortedSet<uint>();
            scene.TickStepped += result =>
            {
                foreach (var simEvent in result.Events)
                    if (simEvent.Kind == CnCNew.Simulation.Events.SimEventKind.WeaponFired && simEvent.Text.Contains("PointDefenseLaser", StringComparison.Ordinal)
                        && scene.World.FindEntity(simEvent.Other) is null && seen.TryGetValue(simEvent.Other.Value, out var last))
                        shotDown[simEvent.Other.Value] = last;
                foreach (var entity in scene.World.Entities)
                    if (entity.Template.IsKindOf("PROJECTILE"))
                        seen[entity.Id.Value] = new Vector3(CnCNew.Presentation.FixedConvert.ToSingle(entity.X), CnCNew.Presentation.FixedConvert.ToSingle(entity.Y), CnCNew.Presentation.FixedConvert.ToSingle(entity.Z));
            };
            Run(scene, () =>
            {
                foreach (var beam in scene.Attached.ProjectileStreams.Where(stream => stream.Laser is not null))
                    foreach (var (id, at) in shotDown)
                        if (Vector3.Distance(beam.Points[^1], at) < 20f) drawn.Add(id);
            });
            Console.WriteLine($"       {tank}: {shotDown.Count} missiles shot down by the laser, a beam drawn to {drawn.Count} of them");
            TestHarness.Assert(shotDown.Count > 0, $"{tank}: the laser shoots missiles down");
            TestHarness.Assert(drawn.Count == shotDown.Count, $"{tank}: a beam is drawn to every missile the laser shot down ({drawn.Count} of {shotDown.Count})");
        }
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

    /// <summary>
    /// The owner (14 November 2026) saw no missile come down and the explosion start at once. <c>NeutronMissileUpdate</c> flies the missile from the silo 300 up, over to 500
    /// above the target and straight down onto it; <c>doAttack</c> builds its matrix from its heading (nose up as it climbs, nose down as it falls), <c>doLaunch</c> attaches the
    /// weapon's <c>ProjectileExhaust</c> (<c>NeutronMissileExhaust</c> with its lens flare), and <c>projectileFireAtObjectOrPosition</c> puts the delivery decal at the target for
    /// the owner. <c>HeightDieUpdate</c> kills it 100 above the ground, and its slow death plays <c>FX_Nuke</c> and makes the radiation field.
    /// </summary>
    private static void NuclearMissile(GameData data)
    {
        // Long enough for the radiation field's own puddles, which its weapon's SuspendFXDelay holds back for 300 frames after the field appears (#355).
        var scenario = new DemoScenario("attached-nuke", "attached-nuke", "attached-nuke", 540,
            [new ScenarioUnit("silo", "ChinaNuclearMissileLauncher", 1, -300f, 0f), new ScenarioUnit("power", "ChinaPowerPlant", 1, -300f, -200f),
             new ScenarioUnit("power2", "ChinaPowerPlant", 1, -300f, 200f), new ScenarioUnit("target", "AmericaBarracks", 2, 300f, 0f)],
            [new ScenarioOrder(30, "silo", ScenarioOrderKind.UsePower, X: 300f, Y: 0f, Template: "SuperweaponNeutronMissile")], 0f, 0f)
        { HumanFaction = "FactionChina", OpponentFaction = "FactionAmerica", PowersReady = true };
        var scene = LiveScenario.Create(data, scenario, Viewport);
        var target = new Vector2(scene.Anchor.X + 300f, scene.Anchor.Y);
        var silo = scene.Tags["silo"];
        long frame = -1, launched = -1, highest = -1, died = -1, climbingNoseUp = 0, fallingNoseDown = 0, decalFrames = 0, decalAfterDeath = 0;
        var top = float.MinValue;
        var exhaust = false;
        var systems = new SortedSet<string>(StringComparer.Ordinal);
        while (!scene.IsOver)
        {
            scene.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            var now = scene.World.Tick / scene.World.Time.TicksPerOriginalFrame;
            if (now == frame) continue;
            frame = now;
            // The silo's SpecialPowerCreate starts the power's clock when the silo is complete, after the staging made it ready: ready it again before the order.
            if (frame == 5 && scene.World.FindEntity(silo) is { } launcher)
                foreach (var module in launcher.Modules)
                    if (module is ISpecialPowerModule power) power.SetReadyFrame(scene.World.Tick);
            foreach (var name in scene.Fx.Particles.RunningTemplates) systems.Add(name);
            var missile = scene.World.Entities.FirstOrDefault(entity => entity.Template.Name == "NeutronMissile");
            if (missile is null) continue;
            if (launched < 0) launched = frame;
            if (missile.EffectivelyDead)
            {
                if (died < 0) died = frame;
                decalAfterDeath += scene.Attached.Decals.Length;
                continue;
            }
            exhaust |= scene.Fx.Particles.RunningTemplates.Contains("NeutronMissileExhaust");
            if (scene.Attached.Decals.Any(decal => Vector2.Distance(new Vector2(decal.Position.X, decal.Position.Y), target) < 1f && Math.Abs(decal.Radius - 210f) < 0.5f)) decalFrames++;
            var z = CnCNew.Presentation.FixedConvert.ToSingle(missile.Z);
            if (z > top) { top = z; highest = frame; }
            var drawn = scene.ObjectFrame.Drawables.FirstOrDefault(drawable => drawable.Entity == missile.Id);
            if (drawn?.PrimaryModel is null) continue;
            var overTarget = Vector2.Distance(new Vector2(drawn.Position.X, drawn.Position.Y), target) < 5f;
            if (!overTarget && frame - launched < 50 && drawn.Chassis.Pitch < -1.5f) climbingNoseUp++;
            if (overTarget && drawn.Chassis.Pitch > 1.5f) fallingNoseDown++;
        }
        Console.WriteLine($"       nuke: launched {launched}, highest {top:0} at {highest}, died {died}; climbing nose up {climbingNoseUp}, falling nose down {fallingNoseDown} frames; decal {decalFrames} frames; systems {string.Join(", ", systems)}");
        TestHarness.Assert(launched > 0 && died > highest && top > 450f, $"the missile was fired, climbed above its target and came down ({launched}, {top:0}, {highest}, {died})");
        TestHarness.Assert(climbingNoseUp >= 20, $"it climbs from the silo nose up ({climbingNoseUp} frames)");
        TestHarness.Assert(fallingNoseDown >= 30, $"it is drawn nose down over its target for more than a second before it detonates ({fallingNoseDown} frames)");
        TestHarness.Assert(exhaust, "it carries its weapon's exhaust (NeutronMissileExhaust) while it flies");
        TestHarness.Assert(decalFrames >= died - launched - 2 && decalAfterDeath == 0, $"its owner sees the delivery decal at the target while it flies, and not after ({decalFrames} of {died - launched}, {decalAfterDeath})");
        var nuke = data.Fx.FindFxList("FX_Nuke");
        TestHarness.Assert(nuke is not null, "FX_Nuke exists");
        var expected = nuke!.Nuggets.OfType<ParticleSystemFxNugget>().Select(nugget => nugget.Name).Concat(["NeutronMissileExhaust", "NeutronMissleLenzflare", "RadiationFieldLarge"]);
        foreach (var name in expected) TestHarness.Assert(systems.Contains(name), $"the system {name} was made");
    }

    /// <summary>
    /// <c>Particle::update</c> on the fallout's puddles (<c>RadiationFieldLarge</c>, the radiation field's weapon effect): ground-aligned, additive, each living 90 frames - from black
    /// to orange at frame 50 and back to black at 90 while it grows by <c>SizeRate</c>, damped by <c>SizeRateDamping</c> every frame, and drifts up 0.01 a frame; one to three new
    /// ones every ten frames at random places of the circle. The field seethes by these puddles appearing, swelling and fading one over the other.
    /// </summary>
    private static void FalloutParticle(GameData data)
    {
        var template = data.Fx.FindParticleSystem("RadiationFieldLarge");
        TestHarness.Assert(template is { IsGroundAligned: true, Shader: ParticleShader.Additive }, "the field's puddles lie on the ground and add their light");
        var world = new ParticleWorld(new ParticleBudget(), new FxRandom(349), data.Fx.FindParticleSystem);
        world.Spawn(template!, new ParticleSpawn(new Vector3(100f, 100f, 10f)));
        var trace = new List<ParticleInstance>();
        var places = new HashSet<(int, int)>();
        uint? followed = null;
        var gone = false;
        for (var step = 0; step < 120; step++)
        {
            world.Step();
            var particles = world.Capture().Particles;
            foreach (var particle in particles) places.Add(((int)particle.Position.X, (int)particle.Position.Y));
            followed ??= particles.IsDefaultOrEmpty ? null : particles[0].Personality;
            if (gone || followed is not { } id) continue;
            var found = particles.Where(particle => particle.Personality == id).ToArray();
            if (found.Length == 0) gone = true;
            else trace.Add(found[0]);
        }
        TestHarness.Assert(trace.Count is >= 88 and <= 90, $"a puddle lives its 90 frames ({trace.Count})");
        TestHarness.Assert(trace.Zip(trace.Skip(1)).All(pair => pair.Second.Size > pair.First.Size) && trace[30].Size > 70f && trace[^1].Size < 131f,
            $"it swells all its life, fast at first (30 -> {trace[30].Size:0} after a second -> {trace[^1].Size:0})");
        var brightest = trace.Select((particle, index) => (Index: index, Sum: particle.Color.X + particle.Color.Y + particle.Color.Z)).MaxBy(entry => entry.Sum);
        TestHarness.Assert(trace[0].Color.X < 0.1f && brightest.Index is >= 40 and <= 60 && trace[brightest.Index].Color.X > 0.75f && trace[^1].Color.X < trace[brightest.Index].Color.X / 2f,
            $"it glows up from black to orange at frame {brightest.Index} and fades again (red {trace[0].Color.X:0.00} -> {trace[brightest.Index].Color.X:0.00} -> {trace[^1].Color.X:0.00})");
        TestHarness.Assert(trace[^1].Position.Z - trace[0].Position.Z is > 0.5f and < 1.2f, $"it drifts up 0.01 a frame ({trace[^1].Position.Z - trace[0].Position.Z:0.00})");
        TestHarness.Assert(places.Count >= 9, $"for the system's 90 frames new puddles keep appearing at other places ({places.Count})");
    }

    private static void NullModel(GameData data)
    {
        TestHarness.Assert(data.Visuals.TryGet("DragonTankFlameProjectile", out var visual) && visual.Draws.All(draw => draw.States.All(state => state.Model is null)),
            "the flame projectile's Model = NULL is no model to load (no placeholder is drawn for it)");
    }
}
