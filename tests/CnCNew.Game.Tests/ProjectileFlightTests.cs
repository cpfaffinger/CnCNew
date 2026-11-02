using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// The flight of the shipped projectiles (docs/SIMULATION-CORE.md §76; findings UNIT-12, FX-04, FX-05 of the owner's acceptance report), with the launch bones of the real models: the Tomahawk
/// leaves its rack 70 degrees up, climbs, cruises and comes down on the target; a rocket trooper turns to its victim and his rocket leaves along that line; a shell leaves the muzzle
/// of a turret turned toward the target. Numbers instead of an eye: the eye is the owner's.
/// </summary>
internal static class ProjectileFlightTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ProjectileFlightTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ProjectileFlightTests with real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The launch table: the weapons of the shipped models", () => LaunchTable(data));
        harness.Run("The Tomahawk leaves its rack, climbs, cruises and comes down on the target", () => Tomahawk(data));
        harness.Run("The Inferno Cannon's shell leaves the muzzle of the turned turret", () => InfernoShell(data));
        harness.Run("A rocket trooper turns to his victim and his rocket leaves along that line", () => RocketTrooper(data));
        harness.Run("Every shipped launcher's missile reaches its target", () => MissilesArrive(data));
        return harness.Failures;
    }

    private static readonly Vector2 Viewport = new(1280, 720);

    private static float Degrees(Fixed radians) => ToFloat(radians) * 180f / MathF.PI;

    private static float ToFloat(Fixed value) => (float)(value.Raw / (double)Fixed.One.Raw);

    private static void Run(LiveScenario scene, Action each)
    {
        while (!scene.IsOver)
        {
            scene.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            each();
        }
    }

    private static void LaunchTable(GameData data)
    {
        var table = data.Templates.Geometry.WeaponLaunches;
        TestHarness.Assert(table.Count > 100, $"every object with a projectile weapon and a launch bone ({table.Count})");
        TestHarness.Assert(table.TryGetValue("AmericaVehicleTomahawk", out var tomahawk) && !tomahawk.Slots[0].Barrels.IsDefaultOrEmpty, "the Tomahawk launcher has a rack");
        var pitchBone = tomahawk!.Slots[0].Turrets[0];
        TestHarness.Assert(pitchBone.PitchZ > Fixed.Zero, $"its launcher pitches about a bone above the ground ({pitchBone.PitchZ})");
        TestHarness.Assert(table.TryGetValue("ChinaInfantryTankHunter", out var hunter) && hunter.Slots[0].Barrels[0].Z > Fixed.FromInt(5), "the rocket trooper fires from his shoulder");
    }

    private static void Tomahawk(GameData data)
    {
        var scene = LiveScenario.Create(data, AcceptanceScenes.Artillery, Viewport);
        var launcher = scene.World.FindEntity(scene.Tags["tomahawk"])!;
        var target = scene.World.FindEntity(scene.Tags["target"])!;
        var pitches = new List<float>();
        var heights = new List<float>();
        var first = 0u;
        var startHeight = 0f;
        var startBearing = 0f;
        Run(scene, () =>
        {
            foreach (var missile in scene.World.Entities.Where(entity => entity.Template.Name == "TomahawkMissile"))
            {
                var flight = missile.Modules.OfType<MissileAIUpdate>().Single();
                if (first != 0 && missile.Id.Value != first) continue;
                var height = ToFloat(missile.Z - scene.World.GroundHeightAt(missile.X, missile.Y));
                if (first == 0)
                {
                    first = missile.Id.Value;
                    startHeight = height;
                    startBearing = Degrees(FixedTrig.AngleDifference(missile.Angle, FixedTrig.Atan2(target.Y - launcher.Y, target.X - launcher.X)));
                }
                pitches.Add(Degrees(flight.FlightPitch));
                heights.Add(height);
            }
        });
        TestHarness.Assert(heights.Count > 40, $"a Tomahawk was followed ({heights.Count} frames)");
        TestHarness.Assert(startHeight > 10f, $"it starts on its rack, not at the middle of the launcher ({startHeight})");
        TestHarness.Assert(MathF.Abs(startBearing) < 3f, $"it starts toward the target ({startBearing} degrees off)");
        TestHarness.Assert(MathF.Abs(pitches[1] - 70f) < 5f, $"it leaves 70 degrees up, the launcher's FirePitch ({pitches[1]})");
        TestHarness.Assert(heights.Max() > 110f && heights.Max() < 160f, $"it climbs to about its preferred height of 120 ({heights.Max()})");
        TestHarness.Assert(heights.Skip(heights.Count / 3).Take(heights.Count / 3).Min() > 100f, $"it does not drive along the ground ({heights.Skip(heights.Count / 3).Take(heights.Count / 3).Min()})");
        TestHarness.Assert(pitches[^1] < -30f, $"it comes down on the target ({pitches[^1]} degrees)");
        TestHarness.Assert(target.Body!.Health < target.Body.MaxHealth, "and hurts it");
    }

    private static void InfernoShell(GameData data)
    {
        var scene = LiveScenario.Create(data, AcceptanceScenes.Artillery, Viewport);
        var launcher = scene.World.FindEntity(scene.Tags["inferno"])!;
        (float Distance, float Height)? start = null;
        Run(scene, () =>
        {
            if (start is not null || scene.World.Entities.FirstOrDefault(entity => entity.Template.Name == "InfernoTankShell") is not { } shell) return;
            var (dx, dy) = (ToFloat(shell.X - launcher.X), ToFloat(shell.Y - launcher.Y));
            start = (MathF.Sqrt(dx * dx + dy * dy), ToFloat(shell.Z - launcher.Z));
        });
        TestHarness.Assert(start is { Distance: > 10f, Height: > 10f }, $"the shell starts at the muzzle, ahead of and above the hull ({start})");
    }

    /// <summary>
    /// A survey of the shipped launchers: each fires its first missile weapon at a Crusader, or at a Comanche in the air when it cannot fire at the ground, at seven tenths of its range on a
    /// flat world with no terrain. A missile with a small thrust angle must still come to its target; what the kinematic flight did by turning at will the thrust has to do by itself.
    /// </summary>
    private static void MissilesArrive(GameData data)
    {
        var catalog = data.Templates;
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var (flown, missed) = (0, new List<string>());
        foreach (var launcher in catalog.Templates.Where(value => value.IsInstantiable && !value.Name.StartsWith("CINE_", StringComparison.Ordinal) && !value.IsKindOf("PROJECTILE")).OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            if (launcher.DefaultWeapon < 0 || catalog.ProjectileTemplates[launcher.DefaultWeapon] is not (var projectile and >= 0)) continue;
            if (!catalog[projectile].Modules.Any(module => module.Name == "MissileAIUpdate")) continue;
            var weapon = catalog.Weapons[launcher.DefaultWeapon];
            if (weapon.AttackRange <= Fixed.FromInt(20) || weapon.AttackRange > Fixed.FromInt(1000)) continue;
            var range = (int)(weapon.AttackRange * Fixed.FromDecimal(0.7m)).Floor();
            var outcome = Fly(data, catalog, players, launcher.Name, "AmericaTankCrusader", range, 0) ?? Fly(data, catalog, players, launcher.Name, "AmericaVehicleComanche", range, 100);
            if (outcome is null) continue;
            flown++;
            if (!outcome.Value.Hit) missed.Add($"{launcher.Name}/{catalog[projectile].Name} at {range}: {outcome.Value.Detail}");
        }
        Console.WriteLine($"       missile survey: {flown} launchers, {missed.Count} missed");
        foreach (var line in missed) Console.WriteLine("         " + line);
        TestHarness.Assert(flown > 30, $"the survey flew the shipped missiles ({flown})");
        TestHarness.Assert(missed.Count == 0, $"every missile reached its target ({missed.Count} did not)");
    }

    /// <summary>One shot, ordered until it goes out; null when the launcher cannot fire at the victim.</summary>
    private static (bool Hit, string Detail)? Fly(GameData data, TemplateCatalog catalog, ImmutableArray<PlayerConfiguration> players, string launcher, string victimName, int distance, int height)
    {
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), CnCNew.Tests.TestRate.Default, 5, players, "missile-survey", data.Fingerprint), catalog);
        var sequence = new Dictionary<int, ulong>();
        ulong Next(int player) => sequence[player] = sequence.GetValueOrDefault(player) + 1;
        void Spawn(int player, string template, int x, int z) => world.Submit(new SpawnCommand(new PlayerId(player), Next(player), world.Tick, template, new PlayerId(player), Fixed.FromInt(x), Fixed.Zero, Fixed.FromInt(z), Fixed.Zero));
        // An aircraft fires from the air; what needs power gets it. On the ground a jet's rockets hit the dirt under it, and a Patriot battery without power does nothing.
        var template = catalog.Templates.First(value => value.Name == launcher);
        Spawn(1, launcher, 1000, template.IsKindOf("AIRCRAFT") || template.IsKindOf("DRONE") ? 80 : 0);
        Spawn(2, victimName, 1000 + distance, height);
        if (template.IsKindOf("POWERED")) Spawn(1, "AmericaPowerPlant", 400, 0);
        world.Step();
        if (world.Entities.FirstOrDefault(entity => entity.Template.Name == launcher) is not { } source || world.Entities.FirstOrDefault(entity => entity.Template.Name == victimName) is not { } victim) return null;
        var fired = false;
        var rejected = 0;
        for (var tick = 0; tick < world.Time.OriginalFrames(120) && !fired; tick++)
        {
            var result = world.Submit(new FireWeaponCommand(new PlayerId(1), Next(1), world.Tick, source.Id, victim.Id));
            if (!result.Accepted) rejected++;
            var step = world.Step();
            rejected += step.Events.Count(value => value.Kind == SimEventKind.CommandRejected);
            fired = step.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == source.Id);
        }
        if (!fired) return rejected > 0 ? null : (false, "never fired");
        var closest = Fixed.MaxValue;
        for (var tick = 0; tick < world.Time.OriginalFrames(600); tick++)
        {
            var step = world.Step();
            foreach (var impact in step.Events.Where(value => value.Kind == SimEventKind.WeaponImpact && value.HasPosition))
            {
                var (dx, dy, dz) = (impact.X - victim.X, impact.Y - victim.Y, impact.Z - victim.Z);
                var distanceToVictim = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distanceToVictim < closest) closest = distanceToVictim;
            }
            if (victim.DestroyRequested || victim.EffectivelyDead) return (true, "killed");
            if (closest <= Fixed.FromInt(45)) return (true, $"impact {closest} away");
        }
        return (false, closest == Fixed.MaxValue ? "no impact in 20 s" : $"impact {closest} away");
    }

    private static void RocketTrooper(GameData data)
    {
        var scene = LiveScenario.Create(data, AcceptanceScenes.Flames, Viewport);
        var hunter = scene.World.FindEntity(scene.Tags["hunter"])!;
        var offLine = new List<float>();
        var seen = new HashSet<uint>();
        Run(scene, () =>
        {
            foreach (var rocket in scene.World.Entities.Where(entity => entity.Template.Name == "TankHunterMissile"))
                if (seen.Add(rocket.Id.Value) && rocket.Modules.OfType<MissileAIUpdate>().Single() is { Victim.IsValid: true } flight && scene.World.FindEntity(flight.Victim) is { } victim)
                {
                    var bearing = FixedTrig.Atan2(victim.Y - hunter.Y, victim.X - hunter.X);
                    offLine.Add(MathF.Abs(Degrees(FixedTrig.AngleDifference(rocket.Angle, bearing))));
                }
        });
        TestHarness.Assert(offLine.Count >= 2, $"rockets were fired ({offLine.Count})");
        // The bone of his firing pose points a little beside his heading, as it does in the original.
        TestHarness.Assert(offLine.Max() < 25f, $"every rocket leaves toward its victim (worst {offLine.Max()} degrees off)");
    }
}
