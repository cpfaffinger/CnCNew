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
        harness.Run("A damaged building smokes and burns: TransitionDamageFX and the body's bones", () => DamagedBuilding(data));
        harness.Run("A building set on fire by the flame burns: the body's aflame category and the fire one size up", () => BurningBuilding(data));
        harness.Run("A missile carries its weapon's exhaust from its ignition", () => MissileExhaust(data));
        harness.Run("An effect projectile with Model = NULL draws no model", () => NullModel(data));
        harness.Run("A moving truck raises dust, a standing one none", () => TreadDebris(data));
        harness.Run("The acceptance scene acc-flames: what burns and smokes, by system", () => AcceptanceScene(data));
        return harness.Failures;
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
