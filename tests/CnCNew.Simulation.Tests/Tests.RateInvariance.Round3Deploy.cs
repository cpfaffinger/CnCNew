using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, third round (<c>docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md</c> §3): <c>DeployStyleAIUpdate</c>. A unit becomes ready to attack in its update; the fire command that finds it
/// ready is the one a logic frame of the original later, at every rate, although commands arrive every tick. The tests name their rates and ignore <c>--tick-rate</c>.
/// </summary>
static partial class Tests
{
    private readonly record struct DeployBits(char State, bool Deployed, long Turret, long Health, long X, long Y)
    {
        public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{State}{(Deployed ? " DEPLOYED" : string.Empty)} turret {Turret} victim {Health} at {X}, {Y}");
    }

    private static char DeployLetter(DeployStyleAIUpdate ai) => ai.IsPacked ? 'M' : ai.IsUnpacking ? 'U' : ai.IsDeployed ? 'A' : ai.IsPacking ? 'P' : '?';

    /// <summary>
    /// The deploying unit of the structures fixture (unpack 30 frames, pack 15, a turret of 180 degrees per second) with its victim a quarter turn to the left, driven by a fire
    /// order in EVERY tick (a player's attack is such a stream): it unpacks, turns its turret, fires; a move order packs it and it drives; fire orders stop it and it unpacks again.
    /// State, DEPLOYED status, turret angle, the victim's health and the unit's position are compared raw after every logic frame of the original.
    /// </summary>
    public static void RateDeployStyleIsBitIdentical()
    {
        var tracks = new List<DeployBits>[FlightRates.Length];
        var summary = new string[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], StructuresFiles(), terrain: false);
            world.SelfAcquisition = false;
            feeder.Spawn(1, "SwDeployer", owner: 1, x: 100, y: 100);
            feeder.Spawn(2, "SwTarget", owner: 2, x: 100, y: 160);
            StepFrames(world, 4);
            var deployer = Only(world, "SwDeployer");
            var target = Only(world, "SwTarget");
            var ai = deployer.Modules.OfType<DeployStyleAIUpdate>().Single();
            var deployedBit = ObjectStatusBits.Index("DEPLOYED");
            var track = tracks[index] = [];
            var log = new List<string>();
            var last = '?';
            var shots = 0;
            for (var frame = 0; frame < 260; frame++)
            {
                if (frame == 100) Check.That(feeder.SetMoveTarget(1, deployer.Id.Value, 60, 100).Accepted, "The move order is accepted.");
                for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++)
                {
                    if (frame is < 100 or >= 160) feeder.FireWeapon(1, deployer.Id.Value, target.Id.Value);
                    var result = world.Step();
                    shots += result.Events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == deployer.Id);
                    var letter = DeployLetter(ai);
                    if (letter == last) continue;
                    last = letter;
                    // In quarters of a frame, so that a change in the middle of a frame shows.
                    log.Add($"{letter}@{InFrames(FrameQuarters(world, result.Tick) - 16)}");
                }
                track.Add(new DeployBits(DeployLetter(ai), deployer.HasStatus(deployedBit), deployer.TurretOf(0)?.Angle.Raw ?? 0, target.Body!.Health.Raw, deployer.X.Raw, deployer.Y.Raw));
            }
            summary[index] = $"{string.Join(" ", log)}; {shots} shots";
            Check.That(log.Count(entry => entry[0] == 'A') == 2 && log.Any(entry => entry[0] == 'P') && shots > 0 && deployer.X < Fixed.FromInt(100), $"{FlightRates[index]}: the unit deployed, fired, packed, drove and deployed again ({summary[index]}).");
        }
        Console.WriteLine($"       deploying unit, states (M packed, U unpacking, A ready to attack, P packing; @ frames after the first fire order) and shots: {summary[0]}");
        for (var index = 1; index < FlightRates.Length; index++)
        {
            Check.That(summary[index] == summary[0], $"deploying unit: {FlightRates[index]} goes through [{summary[index]}], 30 Hz through [{summary[0]}].");
            for (var frame = 0; frame < tracks[0].Count; frame++)
                Check.That(tracks[index][frame] == tracks[0][frame], $"deploying unit: after frame {frame + 1} {FlightRates[index]} is [{tracks[index][frame]}], 30 Hz is [{tracks[0][frame]}].");
        }
    }

    /// <summary>
    /// Retail data: every object with a <c>DeployStyleAIUpdate</c> (Sentry Drone, Nuke Cannon, Inferno Cannon, Tomahawk, artillery platforms and their variants) is given a fire
    /// order in every tick at a victim in range, then a move order. The times from the first order to "ready to attack", to the first shot, and from the move order to "packed" are
    /// the same at 30, 60 and 120 Hz to the tick.
    /// </summary>
    public static void RealDeployStyleTimesAreTheSame(string directory)
    {
        var catalogs = RateCatalogsOf(directory);
        var reference = catalogs.Reference;
        var names = reference.Templates.Where(template => template.IsInstantiable && template.Modules.Any(module => module.Name == "DeployStyleAIUpdate") && template.DefaultWeaponSet is not null)
            .Select(template => template.Name).Order(StringComparer.Ordinal).ToArray();
        Check.That(names.Length > 0, "Retail objects with a DeployStyleAIUpdate exist.");
        var failures = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var deployedOnes = 0;
        foreach (var name in names)
        {
            var template = reference.Templates.First(value => value.Name == name);
            var set = template.DefaultWeaponSet!;
            var slot = Enumerable.Range(0, WeaponSlot.Count).Where(set.HasWeapon).DefaultIfEmpty(-1).First();
            if (slot < 0) continue;
            var weapon = reference.Weapons[set.Weapons[slot]];
            var far = Fixed.Min(weapon.AttackRange, Fixed.FromInt(1000));
            var distance = weapon.MinimumAttackRange > Fixed.Zero ? (weapon.MinimumAttackRange + Fixed.Max(far, weapon.MinimumAttackRange + Fixed.FromInt(40))) / Fixed.FromInt(2) : far * 6 / Fixed.FromInt(10);
            var times = new long[RateCatalogs.Rates.Length][];
            for (var index = 0; index < RateCatalogs.Rates.Length; index++)
            {
                var scene = new RateScene(catalogs, RateCatalogs.Rates[index]);
                var unit = scene.Spawn(name, 1, 2000, 2000);
                var victim = scene.Spawn("ChinaTankOverlord", 2, Fixed.FromInt(2000), Fixed.FromInt(2000) + distance);
                scene.Align();
                var ai = unit.Modules.OfType<DeployStyleAIUpdate>().Single();
                long ready = -1, shot = -1, packed = -1;
                var budget = scene.Time.OriginalFrames(RateScene.Seconds(40));
                for (long step = 1; step <= budget && shot < 0; step++)
                {
                    if (scene.Alive(unit) && scene.Alive(victim)) scene.Fire(unit, victim, slot);
                    var result = scene.Step();
                    if (ready < 0 && ai.IsDeployed) ready = scene.Units(step - 1);
                    if (result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == unit.Id)) shot = scene.Units(step - 1);
                }
                if (ready >= 0 && scene.Alive(unit))
                {
                    scene.Align();
                    scene.Move(unit, 2000, 1500);
                    var steps = scene.StepUntil(() => ai.IsPacked, RateScene.Seconds(40));
                    packed = steps < 0 ? -1 : scene.Units(steps);
                }
                times[index] = [ready, shot, packed];
            }
            string Show(int which) => $"{InFrames(times[0][which])} / {InFrames(times[1][which])} / {InFrames(times[2][which])}";
            var line = $"ready {Show(0)}, first shot {Show(1)}, packed {Show(2)}";
            if (times[0][0] >= 0) deployedOnes++;
            seen[line] = seen.GetValueOrDefault(line) + 1;
            if (seen[line] == 1) Console.WriteLine($"       {name}: {line} frames");
            for (var which = 0; which < 3; which++)
                if (times[1][which] != times[0][which] || times[2][which] != times[0][which]) failures.Add($"{name}: {new[] { "ready to attack", "first shot", "packed" }[which]} after {Show(which)} frames");
        }
        Console.WriteLine($"       {names.Length} retail objects with a DeployStyleAIUpdate and a weapon, {deployedOnes} deployed for the order, {seen.Count} distinct results (the first object of each is printed)");
        Check.That(deployedOnes > 0, "At least one retail object deployed for its fire order.");
        Check.That(failures.Count == 0, string.Join(" | ", failures));
    }
}
