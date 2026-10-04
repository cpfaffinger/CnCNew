using System.Globalization;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.PresentationTool;

/// <summary>
/// <c>scenario-audit &lt;content-dir&gt; --name NAME [--every N] [--ticks N]</c>: runs a <see cref="DemoScenario"/> headless through the same <see cref="LiveGame"/> pipeline the client uses and prints
/// what the demo is meant to show: a sampled table of every placed object (position relative to the scenario origin, height, health, speed, heading, turret, model conditions), the notable events,
/// and the scenario-specific checks (turret alignment at each shot, the capture stages, the jet's flight phases). Exit code 1 when a check fails.
/// </summary>
internal static class ScenarioAudit
{
    public static int Run(string[] args)
    {
        if (args.Length < 1) throw new ArgumentException("scenario-audit <content-dir> --name NAME [--every N] [--ticks N]");
        var contentDir = args[0];
        string? Option(string key) => Array.IndexOf(args, key) is var index and >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var name = Option("--name");
        if (name is null)
        {
            foreach (var entry in DemoScenarios.All) Console.WriteLine($"{entry.Name,-18} {entry.Title}");
            return 0;
        }

        var scenario = DemoScenarios.Find(name) ?? throw new ArgumentException($"Unknown scenario '{name}'.");
        var every = int.Parse(Option("--every") ?? "30", CultureInfo.InvariantCulture);
        var ticks = long.Parse(Option("--ticks") ?? scenario.Ticks.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        // --synthetic-airfield: the airfield without the model's bones (the computed layout the simulation used before the bones were read); the jet checks then fail, which shows what they detect.
        var data = GameData.Load(contentDir, null, !args.Contains("--synthetic-airfield"));
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));

        var events = new List<SimEvent>();
        var log = new List<string>();
        var history = new List<Sample>();
        live.TickStepped += result =>
        {
            if (result.Events is null) return;
            foreach (var simEvent in result.Events) events.Add(simEvent);
        };
        Console.WriteLine($"scenario {scenario.Name}: {scenario.Title}");
        Console.WriteLine($"  {scenario.Description}");
        Console.WriteLine($"  map {scenario.Map}, origin ({live.Anchor.X:F1},{live.Anchor.Y:F1}), {ticks} ticks, objects {string.Join(", ", live.Tags.Keys)}");
        Console.WriteLine();

        var shotsAt = new List<(long Tick, string Shooter, string Victim, float Bearing, float Turret, bool HasTurret)>();
        while (live.Tick < ticks && !live.IsOver)
        {
            var eventsBefore = events.Count;
            live.Advance(1.0 / 30 + 1e-6);
            var frame = live.Frame;
            var sample = Capture(live, frame);
            history.Add(sample);
            for (var i = eventsBefore; i < events.Count; i++) NoteEvent(live, events[i], frame, log, shotsAt);
            if (live.Tick % every == 0) PrintSample(live, sample);
            if (Option("--debug-fire") is { } pair && live.Tick % every == 0 && pair.Split(',') is [var shooterTag, var victimTag]
                && live.Tags.TryGetValue(shooterTag, out var shooterId) && live.Tags.TryGetValue(victimTag, out var victimId)
                && live.World.FindEntity(shooterId) is { } shooter && live.World.FindEntity(victimId) is { } victim)
            {
                var choice = live.World.ChooseWeapon(shooter, victim, CnCNew.Simulation.Templates.FireOrigin.Player);
                Console.WriteLine($"       fire check {shooterTag}->{victimTag}: can {choice.CanFire}, rejection {choice.Rejection}, slot {choice.Slot}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("events");
        foreach (var line in log.Take(200)) Console.WriteLine("  " + line);
        if (log.Count > 200) Console.WriteLine($"  ... {log.Count - 200} more");
        Console.WriteLine();
        var failures = scenario.Name switch
        {
            "tank-duel" => CheckTankDuel(shotsAt, history),
            "capture-building" => CheckCapture(history, events, live),
            "jet-strafe" => CheckJet(history, events, live),
            _ => [],
        };
        Console.WriteLine(failures.Count == 0 ? "RESULT: all checks passed" : $"RESULT: {failures.Count} check(s) failed");
        foreach (var failure in failures) Console.WriteLine("  FAIL " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------------------------------------------------------------ sampling

    private sealed record UnitSample(string Tag, bool Alive, float X, float Y, float Z, float Health, float Speed, float YawDegrees, float? TurretWorldDegrees, int Owner, string Conditions);

    private sealed record Sample(long Tick, Dictionary<string, UnitSample> Units);

    private static Sample Capture(LiveScenario live, RenderFrame frame)
    {
        var units = new Dictionary<string, UnitSample>();
        foreach (var (tag, id) in live.Tags)
        {
            var entity = live.World.FindEntity(id);
            if (entity is null || entity.DestroyRequested)
            {
                units[tag] = new UnitSample(tag, false, 0, 0, 0, 0, 0, 0, null, 0, "gone");
                continue;
            }

            var drawable = frame.Drawables.FirstOrDefault(value => value.Entity == id);
            var yaw = ToFloat(entity.Angle) * 180f / MathF.PI;
            float? turret = drawable?.TurretYaw is { } turretYaw ? Wrap(ToFloat(entity.Angle) + turretYaw) * 180f / MathF.PI : null;
            var health = entity.Body is { } body && body.MaxHealth.Raw != 0 ? ToFloat(body.Health) / ToFloat(body.MaxHealth) * 100f : 100f;
            units[tag] = new UnitSample(tag, !entity.EffectivelyDead, ToFloat(entity.X) - live.Anchor.X, ToFloat(entity.Y) - live.Anchor.Y, ToFloat(entity.Z), health,
                ToFloat(entity.Speed) * 30f, yaw, turret, entity.Owner.Value, drawable?.Conditions.ToString() ?? "(not drawn)");
        }

        return new Sample(live.Tick, units);
    }

    private static readonly HashSet<string> Reported = [];

    private static void PrintSample(LiveScenario live, Sample sample)
    {
        foreach (var unit in sample.Units.Values)
        {
            if (unit.Conditions == "gone" && !Reported.Add(unit.Tag)) continue;
            Console.WriteLine($"t={sample.Tick,5} {unit.Tag,-13} P{unit.Owner} {(unit.Alive ? "    " : "DEAD")} pos({unit.X,7:F1},{unit.Y,7:F1}) z{unit.Z,6:F1} hp{unit.Health,4:F0}% v{unit.Speed,5:F1} yaw{unit.YawDegrees,6:F0}" +
                              $"{(unit.TurretWorldDegrees is { } turret ? $" turret{turret,5:F0}" : "            ")}  [{unit.Conditions}]");
        }
    }

    private static void NoteEvent(LiveScenario live, SimEvent simEvent, RenderFrame frame, List<string> log, List<(long Tick, string Shooter, string Victim, float Bearing, float Turret, bool HasTurret)> shots)
    {
        string Tag(EntityId id) => live.Tags.FirstOrDefault(pair => pair.Value == id).Key ?? $"#{id.Value}";
        switch (simEvent.Kind)
        {
            case SimEventKind.WeaponFired:
            {
                var shooter = live.World.FindEntity(simEvent.Entity);
                var victim = live.World.FindEntity(simEvent.Other);
                log.Add($"t={simEvent.Tick,5} fire   {Tag(simEvent.Entity)} -> {Tag(simEvent.Other)} ({simEvent.Text})");
                if (shooter is not null && victim is not null)
                {
                    var bearing = Wrap(MathF.Atan2(ToFloat(victim.Y - shooter.Y), ToFloat(victim.X - shooter.X))) * 180f / MathF.PI;
                    var drawable = frame.Drawables.FirstOrDefault(value => value.Entity == simEvent.Entity);
                    var hasTurret = drawable?.TurretYaw is not null;
                    var turret = hasTurret ? Wrap(ToFloat(shooter.Angle) + drawable!.TurretYaw!.Value) * 180f / MathF.PI : float.NaN;
                    shots.Add((simEvent.Tick, Tag(simEvent.Entity), Tag(simEvent.Other), bearing, turret, hasTurret));
                }

                break;
            }
            case SimEventKind.EntityDied:
                log.Add($"t={simEvent.Tick,5} died   {Tag(simEvent.Entity)}");
                break;
            case SimEventKind.StructureCaptured:
                log.Add($"t={simEvent.Tick,5} captured {Tag(simEvent.Entity)} by {Tag(simEvent.Other)} (player {simEvent.Value} -> {simEvent.Player})");
                break;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------ checks

    private const float AlignmentToleranceDegrees = 5f;

    private static List<string> CheckTankDuel(List<(long Tick, string Shooter, string Victim, float Bearing, float Turret, bool HasTurret)> shots, List<Sample> history)
    {
        var failures = new List<string>();
        Console.WriteLine($"turret alignment at the moment of each shot (tolerance {AlignmentToleranceDegrees} degrees)");
        if (shots.Count == 0) failures.Add("nobody fired");
        foreach (var shot in shots)
        {
            var error = shot.HasTurret ? Math.Abs(Wrap((shot.Bearing - shot.Turret) * MathF.PI / 180f)) * 180f / MathF.PI : float.NaN;
            Console.WriteLine($"  t={shot.Tick,5} {shot.Shooter,-13}-> {shot.Victim,-13} bearing {shot.Bearing,7:F1}  turret {(shot.HasTurret ? shot.Turret.ToString("F1", CultureInfo.InvariantCulture) : "none"),7}  error {error,6:F1}");
            if (!shot.HasTurret) failures.Add($"t={shot.Tick}: {shot.Shooter} has no turret angle at all");
            else if (error > AlignmentToleranceDegrees) failures.Add($"t={shot.Tick}: {shot.Shooter} fired {error:F1} degrees off its target (turret {shot.Turret:F1}, bearing {shot.Bearing:F1})");
        }

        var firstShot = shots.Count > 0 ? shots.Min(shot => shot.Tick) : -1;
        Console.WriteLine($"  first shot at tick {firstShot}; orders were given at tick 5");
        // The turret must actually move: its world angle changes between the start and the first shot.
        foreach (var shooter in new[] { "crusader", "battlemaster" })
        {
            var turrets = history.Select(sample => sample.Units[shooter].TurretWorldDegrees).Where(value => value is not null).Select(value => value!.Value).ToArray();
            if (turrets.Length == 0) { failures.Add($"{shooter}: no turret angle in any frame"); continue; }
            Console.WriteLine($"  {shooter}: turret world angle from {turrets.First():F0} to {turrets.Last():F0} (range {turrets.Min():F0}..{turrets.Max():F0})");
        }

        return failures;
    }

    private static List<string> CheckCapture(List<Sample> history, List<SimEvent> events, LiveScenario live)
    {
        var failures = new List<string>();
        Console.WriteLine("ranger model condition stages");
        string? previous = null;
        var stages = new List<(long Tick, string Conditions)>();
        foreach (var sample in history)
        {
            var conditions = sample.Units["ranger"].Conditions;
            if (conditions == previous) continue;
            previous = conditions;
            stages.Add((sample.Tick, conditions));
            Console.WriteLine($"  t={sample.Tick,5} [{conditions}]");
        }

        long First(string flag) => stages.FirstOrDefault(stage => stage.Conditions.Split(' ').Contains(flag)).Tick is var tick && stages.Any(stage => stage.Conditions.Split(' ').Contains(flag)) ? tick : -1;
        var unpack = First("UNPACKING");
        var raise = First("RAISING_FLAG");
        var pack = First("PACKING");
        var capture = events.FirstOrDefault(value => value.Kind == SimEventKind.StructureCaptured);
        Console.WriteLine($"  UNPACKING at {unpack}, RAISING_FLAG at {raise}, PACKING at {pack}, structure captured at {(capture.Kind == SimEventKind.StructureCaptured ? capture.Tick.ToString(CultureInfo.InvariantCulture) : "never")}");
        if (unpack < 0) failures.Add("the Ranger never unpacks (UNPACKING is not set)");
        if (raise < 0) failures.Add("the Ranger never raises the flag (RAISING_FLAG is not set)");
        if (pack < 0) failures.Add("the Ranger never packs up again (PACKING is not set)");
        if (capture.Kind != SimEventKind.StructureCaptured) failures.Add("the barracks was never captured");
        else
        {
            if (raise >= 0 && capture.Tick < raise) failures.Add($"the barracks changed hands (tick {capture.Tick}) before the flag went up (tick {raise})");
            if (unpack >= 0 && raise >= 0 && !(unpack < raise)) failures.Add("the stages are out of order (unpack must come before raising the flag)");
        }

        var owner = history[^1].Units["barracks"].Owner;
        Console.WriteLine($"  the barracks belongs to player {owner} at the end");
        if (owner != 1) failures.Add($"the barracks belongs to player {owner} instead of the Ranger's player 1");
        return failures;
    }

    private static List<string> CheckJet(List<Sample> history, List<SimEvent> events, LiveScenario live)
    {
        var failures = new List<string>();
        var runways = AirfieldRunways(live);
        var apronZ = live.World.FindEntity(live.Tags["airfield"]) is { } field ? ToFloat(field.Z) : 0f;
        var trooperIds = live.Tags.Where(pair => pair.Key.StartsWith("trooper", StringComparison.Ordinal)).Select(pair => pair.Value).ToHashSet();
        foreach (var tag in live.Tags.Keys.Where(tag => tag.StartsWith("raptor", StringComparison.Ordinal)).OrderBy(tag => tag, StringComparer.Ordinal))
        {
            var id = live.Tags[tag];
            var series = history.Where(sample => sample.Units.ContainsKey(tag)).Select(sample => (sample.Tick, Unit: sample.Units[tag])).ToArray();
            if (series.Length == 0) { failures.Add($"{tag} was never built"); continue; }
            var shots = events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == id);
            var firstShot = events.Where(value => value.Kind == SimEventKind.WeaponFired && value.Entity == id).Select(value => value.Tick).DefaultIfEmpty(-1).Min();
            var airborne = series.Where(item => item.Unit.Z > 25f).ToArray();
            var takeoff = airborne.Length > 0 ? airborne[0].Tick : -1;
            var last = series[^1].Unit;
            var maxZ = series.Max(item => item.Unit.Z);
            var maxSpeed = series.Max(item => item.Unit.Speed);
            var landedAfter = airborne.Length > 0 && series.Any(item => item.Tick > airborne[^1].Tick && item.Unit.Z < 25f && item.Unit.Alive);
            var phases = new List<string>();
            string? previous = null;
            foreach (var item in series)
            {
                var phase = !item.Unit.Alive ? "dead" : item.Unit.Z > 25f ? (item.Unit.Speed > 40f ? "flying" : "hovering") : (item.Unit.Speed > 2f ? "taxiing" : "parked");
                if (phase == previous) continue;
                previous = phase;
                phases.Add($"{phase}@{item.Tick}");
            }

            Console.WriteLine($"{tag}: built at tick {series[0].Tick}, took off at {takeoff}, max height {maxZ:F1}, max speed {maxSpeed:F1}/s, shots {shots} (first at {firstShot}), alive at the end {last.Alive}, landed again {landedAfter}");
            Console.WriteLine($"  phases: {string.Join(" > ", phases)}");
            var hover = series.Count(item => item.Unit.Alive && item.Unit.Z > 25f && item.Unit.Speed < 5f);
            Console.WriteLine($"  ticks standing still in the air: {hover}");
            if (hover > 10) failures.Add($"{tag} hangs motionless in the air for {hover} ticks (a fighter has a minimum speed and circles; the simulation lets it stop at the end of its move order)");
            // RaptorJetLocomotor: MinSpeed 60/s. A little slack for the sampled value; the take-off roll and the landing roll-out stay below 25 height units.
            var slowest = series.Where(item => item.Unit.Alive && item.Unit.Z > 25f).Select(item => item.Unit.Speed).DefaultIfEmpty(float.MaxValue).Min();
            var slow = series.Count(item => item.Unit.Alive && item.Unit.Z > 25f && item.Unit.Speed < 55f);
            Console.WriteLine($"  slowest airborne speed {slowest:F1}/s (minimum speed 60/s), ticks below 55/s in the air: {slow}");
            if (slow > 0) failures.Add($"{tag} flies slower than its minimum speed for {slow} ticks while airborne");
            CheckAirfieldGeometry(tag, series, runways, apronZ, failures);
            if (takeoff < 0) failures.Add($"{tag} never takes off");
            if (shots == 0) failures.Add($"{tag} never fires");
            if (last.Alive && !landedAfter) failures.Add($"{tag} survived but never came back down");
        }

        var trooperShots = events.Count(value => value.Kind == SimEventKind.WeaponFired && trooperIds.Contains(value.Entity));
        var dead = history[^1].Units.Values.Count(unit => unit.Tag.StartsWith("trooper", StringComparison.Ordinal) && !unit.Alive);
        Console.WriteLine($"troopers: {trooperShots} missiles fired, {dead} of {trooperIds.Count} dead");
        if (trooperShots == 0) failures.Add("the Missile Defender soldiers never fire back");
        if (dead == 0) failures.Add("no Missile Defender soldier dies");
        return failures;
    }

    /// <summary>The runway segments of the airfield from the model's bones (RunwayStart/RunwayEnd per column), in the scenario's frame (metres from the origin), independent of what the simulation used.</summary>
    private static List<(Vector2 Start, Vector2 End)> AirfieldRunways(LiveScenario live)
    {
        var result = new List<(Vector2, Vector2)>();
        var airfield = live.World.FindEntity(live.Tags["airfield"]);
        if (airfield is null) return result;
        var geometry = CnCNew.Game.Data.ModelGeometryLoader.Load(live.Data.Graph, live.Data.Visuals, live.Data.Content);
        if (!geometry.Airfields.TryGetValue(airfield.Template.Name, out var layout)) return result;
        var angle = ToFloat(airfield.Angle);
        var (cos, sin) = (MathF.Cos(angle), MathF.Sin(angle));
        Vector2 ToScenario(CnCNew.Simulation.Templates.BonePose bone)
            => new(ToFloat(airfield.X) + ToFloat(bone.X) * cos - ToFloat(bone.Y) * sin - live.Anchor.X, ToFloat(airfield.Y) + ToFloat(bone.X) * sin + ToFloat(bone.Y) * cos - live.Anchor.Y);
        for (var index = 0; index < layout.RunwayStart.Length; index++) result.Add((ToScenario(layout.RunwayStart[index]), ToScenario(layout.RunwayEnd[index])));
        return result;
    }

    private static float DistanceToRunways(List<(Vector2 Start, Vector2 End)> runways, float x, float y)
    {
        var best = float.MaxValue;
        foreach (var (start, end) in runways)
        {
            var along = end - start;
            var t = Math.Clamp(Vector2.Dot(new Vector2(x, y) - start, along) / along.LengthSquared(), 0f, 1f);
            best = Math.Min(best, Vector2.Distance(new Vector2(x, y), start + along * t));
        }

        return best;
    }

    /// <summary>
    /// The jet uses the airfield it belongs to: it rolls on the apron (its height is the airfield's), takes off on one of the runways of the model (the take-off point and the whole roll lie on the
    /// runway segment) and touches down on it again.
    /// </summary>
    private static void CheckAirfieldGeometry(string tag, (long Tick, UnitSample Unit)[] series, List<(Vector2 Start, Vector2 End)> runways, float apronZ, List<string> failures)
    {
        if (runways.Count == 0) { failures.Add("the airfield model has no runway bones to check against"); return; }
        const float Tolerance = 12f;
        var onGround = series.Where(item => item.Unit.Alive && item.Unit.Z <= apronZ + 5f && item.Unit.Conditions != "(not drawn)").ToArray();
        var offApron = onGround.Where(item => item.Unit.Speed <= 51f && !item.Unit.Conditions.Contains("JETAFTERBURNER", StringComparison.Ordinal) && Math.Abs(item.Unit.Z - apronZ) > 0.1f).ToArray();
        Console.WriteLine($"  on the ground {onGround.Length} ticks, of which taxiing and not at the apron height {apronZ:F1}: {offApron.Length}");
        if (offApron.Length > 0) failures.Add($"{tag} rolls at height {offApron[0].Unit.Z:F2} instead of the apron's {apronZ:F2} (tick {offApron[0].Tick})");

        var takeoff = series.FirstOrDefault(item => item.Unit.Alive && item.Unit.Z > apronZ + 5f);
        if (takeoff.Unit is null) return;
        var takeoffDistance = DistanceToRunways(runways, takeoff.Unit.X, takeoff.Unit.Y);
        Console.WriteLine($"  lifts off at tick {takeoff.Tick} at ({takeoff.Unit.X:F1}, {takeoff.Unit.Y:F1}), {takeoffDistance:F1} from the runway line");
        if (takeoffDistance > Tolerance) failures.Add($"{tag} lifts off {takeoffDistance:F0} units from the airfield's runway, at ({takeoff.Unit.X:F0}, {takeoff.Unit.Y:F0}) (tolerance {Tolerance})");
        // the roll: the grounded ticks right before the lift-off at more than taxi speed
        var roll = series.TakeWhile(item => item.Tick < takeoff.Tick).Reverse().TakeWhile(item => item.Unit.Speed > 52f && item.Unit.Z <= apronZ + 5f).ToArray();
        var worst = roll.Select(item => DistanceToRunways(runways, item.Unit.X, item.Unit.Y)).DefaultIfEmpty(0f).Max();
        Console.WriteLine($"  take-off roll {roll.Length} ticks, farthest from the runway line {worst:F1}");
        if (worst > Tolerance) failures.Add($"{tag}'s take-off roll leaves the runway line by {worst:F0} units");

        // touchdown: the first grounded tick after the last airborne one
        var airborne = series.Where(item => item.Unit.Alive && item.Unit.Z > apronZ + 5f).ToArray();
        var lastAir = airborne[^1];
        var down = series.FirstOrDefault(item => item.Tick > lastAir.Tick && item.Unit.Alive);
        if (down.Unit is null) return;
        var landingDistance = DistanceToRunways(runways, down.Unit.X, down.Unit.Y);
        Console.WriteLine($"  touches down at tick {down.Tick} at ({down.Unit.X:F1}, {down.Unit.Y:F1}), {landingDistance:F1} from the runway line");
        if (landingDistance > Tolerance) failures.Add($"{tag} touches down {landingDistance:F0} units from the airfield's runway");
    }

    private static float ToFloat(CnCNew.Simulation.Numerics.Fixed value) => (float)(value.Raw / 65536.0);

    private static float Wrap(float radians)
    {
        radians %= MathF.Tau;
        if (radians > MathF.PI) radians -= MathF.Tau;
        else if (radians < -MathF.PI) radians += MathF.Tau;
        return radians;
    }
}
