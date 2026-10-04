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
            "tank-duel" => CheckTankDuel(shotsAt, history, events, live),
            "capture-building" => CheckCapture(history, events, live, SelectionFlashSettings.FromDefinitions(data.Graph)),
            "jet-strafe" => CheckJet(history, events, live),
            _ => [],
        };
        Console.WriteLine(failures.Count == 0 ? "RESULT: all checks passed" : $"RESULT: {failures.Count} check(s) failed");
        foreach (var failure in failures) Console.WriteLine("  FAIL " + failure);
        return failures.Count == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------------------------------------------------------------ sampling

    private sealed record UnitSample(string Tag, bool Alive, float X, float Y, float Z, float Health, float Speed, float YawDegrees, float? TurretWorldDegrees, int Owner, string Conditions, float? TurretRelativeDegrees = null, Vector3 FlashTint = default);

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
            float? relative = entity.TurretOf(0) is { } reading ? ToFloat(reading.Angle) * 180f / MathF.PI : null;
            units[tag] = new UnitSample(tag, !entity.EffectivelyDead, ToFloat(entity.X) - live.Anchor.X, ToFloat(entity.Y) - live.Anchor.Y, ToFloat(entity.Z), health,
                ToFloat(entity.Speed) * 30f, yaw, turret, entity.Owner.Value, drawable?.Conditions.ToString() ?? "(not drawn)", relative, drawable?.FlashTint ?? Vector3.Zero);
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

    private static List<string> CheckTankDuel(List<(long Tick, string Shooter, string Victim, float Bearing, float Turret, bool HasTurret)> shots, List<Sample> history, List<SimEvent> events, LiveScenario live)
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

        CheckDuelTracking(history, events, live, failures);
        CheckSentinelScan(history, live, failures);
        return failures;
    }

    /// <summary>Everything the persistent attack and the recentre hold promise, measured in every tick and not only at the shots.</summary>
    private static void CheckDuelTracking(List<Sample> history, List<SimEvent> events, LiveScenario live, List<string> failures)
    {
        Console.WriteLine();
        Console.WriteLine($"turret on the victim in every tick between the order and the victim's death (tolerance {AlignmentToleranceDegrees} degrees)");
        var pairs = new[] { ("crusader", "battlemaster"), ("battlemaster", "crusader") };
        var deaths = new Dictionary<string, long>();
        foreach (var simEvent in events)
            if (simEvent.Kind == SimEventKind.EntityDied)
                foreach (var tag in new[] { "crusader", "battlemaster" })
                    if (live.Tags.TryGetValue(tag, out var id) && id == simEvent.Entity) deaths[tag] = simEvent.Tick;
        foreach (var (shooterTag, victimTag) in pairs)
        {
            var victimDeath = deaths.TryGetValue(victimTag, out var tick) ? tick : long.MaxValue;
            var ownDeath = deaths.TryGetValue(shooterTag, out var own) ? own : long.MaxValue;
            // The order was given at tick 5 and takes effect in that tick; a sample carries the state after the step of the tick before it.
            var window = history.Where(sample => sample.Tick > 5 && sample.Tick <= Math.Min(victimDeath, ownDeath) && sample.Units[shooterTag].Alive && sample.Units[victimTag].Alive).ToArray();
            if (window.Length == 0) { failures.Add($"{shooterTag}: no sample between the order and the victim's death"); continue; }
            var aligned = window.Where(sample => TrackingError(sample, shooterTag, victimTag) <= AlignmentToleranceDegrees).Select(sample => sample.Tick).DefaultIfEmpty(-1).Min();
            var after = window.Where(sample => sample.Tick >= aligned && aligned >= 0).ToArray();
            var worst = after.Select(sample => TrackingError(sample, shooterTag, victimTag)).DefaultIfEmpty(float.NaN).Max();
            var off = after.Count(sample => TrackingError(sample, shooterTag, victimTag) > AlignmentToleranceDegrees);
            Console.WriteLine($"  {shooterTag,-13}-> {victimTag,-13} aligned from tick {aligned}, {after.Length} ticks until {(victimDeath == long.MaxValue ? "the end" : "the victim's death at " + victimDeath)}, worst error {worst:F1}, ticks off {off}");
            if (aligned < 0) failures.Add($"{shooterTag}'s turret never pointed at {victimTag}");
            else if (off > 0) failures.Add($"{shooterTag}'s turret was off its victim in {off} ticks after it had aimed (worst {worst:F1} degrees), e.g. during a reload");
            if (aligned > 5 + 20) failures.Add($"{shooterTag} took until tick {aligned} to aim");
        }

        Console.WriteLine();
        Console.WriteLine("after the victim's death the turret holds for RecenterTime, then turns back to its natural angle");
        foreach (var (shooterTag, victimTag) in pairs)
        {
            if (!deaths.TryGetValue(victimTag, out var death) || deaths.ContainsKey(shooterTag)) continue;
            var entity = live.World.FindEntity(live.Tags[shooterTag]);
            if (entity?.Template.Turrets?[0] is not { } info) { failures.Add($"{shooterTag} has no turret template"); continue; }
            var recenter = info.RecenterTicks;
            var natural = ToFloat(info.NaturalAngle) * 180f / MathF.PI;
            // sample k carries the state after the step of tick k-1; the death is noticed in the step of tick `death` (or the next one) and starts the hold there
            var series = history.Where(sample => sample.Tick > death && sample.Units[shooterTag].TurretRelativeDegrees is not null).Select(sample => (sample.Tick, Angle: sample.Units[shooterTag].TurretRelativeDegrees!.Value)).ToArray();
            if (series.Length == 0) { failures.Add($"{shooterTag}: no samples after the victim's death"); continue; }
            var held = series[0].Angle;
            var firstMove = series.FirstOrDefault(item => Math.Abs(item.Angle - held) > 0.05f);
            var moved = firstMove.Tick != 0;
            var rate = ToFloat(info.TurnRate) * 180f / MathF.PI * 0.5f;
            var rest = series.FirstOrDefault(item => Math.Abs(item.Angle - natural) < 0.05f && item.Tick >= firstMove.Tick);
            Console.WriteLine($"  {shooterTag,-13}died victim at {death}, RecenterTime {recenter} ticks, angle held {held:F1}; first moves at tick {(moved ? firstMove.Tick.ToString(CultureInfo.InvariantCulture) : "never")} (earliest {death + recenter + 2}); rests at {natural:F0} from tick {(rest.Tick != 0 ? rest.Tick.ToString(CultureInfo.InvariantCulture) : "never")}");
            if (!moved) { failures.Add($"{shooterTag}'s turret never turns back after the victim died"); continue; }
            if (firstMove.Tick < death + recenter + 2) failures.Add($"{shooterTag}'s turret turned back at tick {firstMove.Tick}, before the recentre time was over (tick {death + recenter + 2})");
            if (firstMove.Tick > death + recenter + 3) failures.Add($"{shooterTag}'s turret waited until tick {firstMove.Tick} to turn back (due at {death + recenter + 2})");
            var expectedRest = firstMove.Tick + (int)MathF.Ceiling(Math.Abs(held - natural) / rate) + 2;
            if (rest.Tick == 0 || rest.Tick > expectedRest) failures.Add($"{shooterTag}'s turret did not come to rest by tick {expectedRest} (rest {rest.Tick})");
        }
    }

    private static float TrackingError(Sample sample, string shooterTag, string victimTag)
    {
        var shooter = sample.Units[shooterTag];
        var victim = sample.Units[victimTag];
        if (shooter.TurretWorldDegrees is not { } turret) return float.NaN;
        var bearing = Wrap(MathF.Atan2(victim.Y - shooter.Y, victim.X - shooter.X));
        return Math.Abs(Wrap((bearing - turret * MathF.PI / 180f))) * 180f / MathF.PI;
    }

    /// <summary>
    /// The bystander Sentry Drone (turret scan 0..360 degrees every 250 ms, recentre time 1000 ms) stands out of everyone's weapon range: its turret has to glance around on its own, hold each angle
    /// for the recentre time, return to the natural angle and wait the (fixed) scan interval; the drawn turret and TURRET_ROTATE follow.
    /// </summary>
    private static void CheckSentinelScan(List<Sample> history, LiveScenario live, List<string> failures)
    {
        Console.WriteLine();
        Console.WriteLine("idle scan of the bystander");
        var entity = live.World.FindEntity(live.Tags["sentinel"]);
        if (entity?.Template.Turrets?[0] is not { CanScan: true } info) { failures.Add("the sentinel has no turret that scans"); return; }
        var series = history.Where(sample => sample.Units["sentinel"].TurretRelativeDegrees is not null).Select(sample => (sample.Tick, Unit: sample.Units["sentinel"])).ToArray();
        if (series.Length < 600) { failures.Add($"only {series.Length} samples of the sentinel"); return; }
        var natural = ToFloat(info.NaturalAngle) * 180f / MathF.PI;
        var maxScan = ToFloat(info.MaxScanAngle) * 180f / MathF.PI;
        var minScan = ToFloat(info.MinScanAngle) * 180f / MathF.PI;
        // excursions: runs of samples away from the natural angle
        var excursions = new List<(int Start, int End, float Peak, int AtPeak)>();
        var index = 0;
        while (index < series.Length)
        {
            if (Math.Abs(series[index].Unit.TurretRelativeDegrees!.Value - natural) < 0.05f) { index++; continue; }
            var start = index;
            var peak = 0f;
            while (index < series.Length && Math.Abs(series[index].Unit.TurretRelativeDegrees!.Value - natural) >= 0.05f)
            {
                if (Math.Abs(series[index].Unit.TurretRelativeDegrees!.Value) > Math.Abs(peak)) peak = series[index].Unit.TurretRelativeDegrees!.Value;
                index++;
            }
            excursions.Add((start, index, peak, series.Skip(start).Take(index - start).Count(item => Math.Abs(item.Unit.TurretRelativeDegrees!.Value - peak) < 0.05f)));
        }

        var complete = excursions.Where(value => value.End < series.Length).ToArray();
        Console.WriteLine($"  {series.Length} ticks: {complete.Count()} scans, relative turret angle {series.Min(item => item.Unit.TurretRelativeDegrees!.Value):F0}..{series.Max(item => item.Unit.TurretRelativeDegrees!.Value):F0} degrees, world angle {series.Where(item => item.Unit.TurretWorldDegrees is not null).Min(item => item.Unit.TurretWorldDegrees!.Value):F0}..{series.Where(item => item.Unit.TurretWorldDegrees is not null).Max(item => item.Unit.TurretWorldDegrees!.Value):F0}");
        if (complete.Length < 4) failures.Add($"the sentinel scanned only {complete.Length} times in {series.Length} ticks");
        if (complete.Any(value => Math.Abs(value.Peak) < minScan - 0.1f || Math.Abs(value.Peak) > maxScan + 0.1f)) failures.Add("a scan angle lies outside MinIdleScanAngle..MaxIdleScanAngle");
        var holds = complete.Select(value => value.AtPeak).ToArray();
        Console.WriteLine($"  scan angles: {string.Join(", ", complete.Take(10).Select(value => value.Peak.ToString("F0", CultureInfo.InvariantCulture)))}; ticks held at the angle: {string.Join(", ", holds.Take(10))} (RecenterTime {info.RecenterTicks} ticks)");
        if (holds.Any(value => value < info.RecenterTicks || value > info.RecenterTicks + 2)) failures.Add($"a scan angle was not held for RecenterTime ({info.RecenterTicks} ticks): {string.Join(", ", holds)}");
        var gaps = complete.Zip(complete.Skip(1), (left, right) => right.Start - left.End).ToArray();
        Console.WriteLine($"  rest between the scans: {string.Join(", ", gaps.Take(10))} ticks (MinIdleScanInterval {info.MinScanIntervalTicks}, MaxIdleScanInterval {info.MaxScanIntervalTicks})");
        if (gaps.Any(gap => gap < info.MinScanIntervalTicks || gap > info.MaxScanIntervalTicks)) failures.Add("the rest between two scans lies outside the scan interval");

        // the drawn turret and TURRET_ROTATE come from the simulation
        var mismatched = 0;
        for (var i = 0; i < series.Length; i++)
        {
            if (series[i].Unit.TurretWorldDegrees is not { } world) continue;
            float Off(int at) => Math.Abs(Wrap((world - series[at].Unit.YawDegrees - series[at].Unit.TurretRelativeDegrees!.Value) * MathF.PI / 180f)) * 180f / MathF.PI;
            // the frame the presentation draws may still carry the previous tick's turret: it has to equal the simulated angle of this sample or of the one before
            if (i == 0 || Off(i) <= 0.6f || Off(i - 1) <= 0.6f) continue;
            mismatched++;
            if (mismatched < 4) Console.WriteLine($"    mismatch t={series[i].Tick}: drawn {world:F1}, simulated {series[i].Unit.TurretRelativeDegrees:F1} (previous {series[i - 1].Unit.TurretRelativeDegrees:F1}), yaw {series[i].Unit.YawDegrees:F1}");
        }

        var changed = 0;
        var rotateOnChange = 0;
        var rotateWithoutChange = 0;
        for (var i = 1; i < series.Length; i++)
        {
            var moved = Math.Abs(series[i].Unit.TurretRelativeDegrees!.Value - series[i - 1].Unit.TurretRelativeDegrees!.Value) > 0.001f;
            var rotating = series[i].Unit.Conditions.Split(' ').Contains("TURRET_ROTATE");
            if (moved) { changed++; if (rotating) rotateOnChange++; }
            else if (rotating) rotateWithoutChange++;
        }

        Console.WriteLine($"  drawn turret off the simulated angle in {mismatched} samples; TURRET_ROTATE on {rotateOnChange} of {changed} turning samples, {rotateWithoutChange} samples with TURRET_ROTATE and no turning");
        if (mismatched > 0) failures.Add($"the drawn turret deviates from the simulated angle in {mismatched} samples");
        if (changed == 0 || rotateOnChange < changed * 0.8) failures.Add($"TURRET_ROTATE is set in only {rotateOnChange} of {changed} samples in which the scanning turret turned");
        if (rotateWithoutChange > 0) failures.Add($"TURRET_ROTATE is set in {rotateWithoutChange} samples in which the turret did not turn");
    }

    private static List<string> CheckCapture(List<Sample> history, List<SimEvent> events, LiveScenario live, SelectionFlashSettings flashSettings)
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

        CheckCaptureFlash(history, events, live, flashSettings, stages, failures);

        var owner = history[^1].Units["barracks"].Owner;
        Console.WriteLine($"  the barracks belongs to player {owner} at the end");
        if (owner != 1) failures.Add($"the barracks belongs to player {owner} instead of the Ranger's player 1");
        return failures;
    }

    /// <summary>
    /// The barracks flashes while the flag goes up (<c>DoCaptureFX</c>): every <c>CaptureFlash</c> event shows in the projected frames as a tint of the barracks' drawable in the Ranger's
    /// saturated house colour, the tint is gone again before the next flash or within the envelope's five ticks, and nothing is tinted outside the flashes.
    /// </summary>
    private static void CheckCaptureFlash(List<Sample> history, List<SimEvent> events, LiveScenario live, SelectionFlashSettings settings, List<(long Tick, string Conditions)> stages, List<string> failures)
    {
        var barracks = live.Tags["barracks"];
        var flashes = events.Where(value => value.Kind == SimEventKind.CaptureFlash && value.Entity == barracks).Select(value => value.Tick).ToArray();
        var tinted = history.Where(sample => sample.Units["barracks"].FlashTint != Vector3.Zero).ToArray();
        var peak = SelectionFlashTracker.Saturate(live.Colors.Of(new PlayerId(1)), settings.SaturationFactor);
        Console.WriteLine($"  capture flashes at ticks {string.Join(", ", flashes)}; the barracks' drawable is tinted in {tinted.Length} frames, peak ({peak.X:F3}, {peak.Y:F3}, {peak.Z:F3}) of the Ranger's house colour");
        if (flashes.Length < 2) { failures.Add($"the barracks flashes {flashes.Length} time(s) while the flag goes up, expected several"); return; }

        // Model conditions are sampled after each tick; the event carries the tick it happened in. The sample that follows the event is the first frame that can show it.
        var raising = stages.Where(stage => stage.Conditions.Split(' ').Contains("RAISING_FLAG")).Select(stage => stage.Tick).DefaultIfEmpty(-1).First();
        var after = stages.Where(stage => stage.Tick > raising && !stage.Conditions.Split(' ').Contains("RAISING_FLAG")).Select(stage => stage.Tick).DefaultIfEmpty(long.MaxValue).First();
        var peaks = 0;
        foreach (var tick in flashes)
        {
            var index = history.FindIndex(sample => sample.Tick > tick);
            if (index < 0) { failures.Add($"no frame was projected after the flash of tick {tick}"); continue; }
            var shown = history[index].Units["barracks"].FlashTint;
            if (Vector3.Distance(shown, peak) < 1e-4f) peaks++;
            else failures.Add($"the frame after the flash of tick {tick} tints the barracks ({shown.X:F3}, {shown.Y:F3}, {shown.Z:F3}) instead of the peak of the Ranger's colour");
            if (history[index].Tick < raising || history[index].Tick > after) failures.Add($"the flash of tick {tick} is outside RAISING_FLAG ({raising}..{after})");
            if (history[index].Units["ranger"].FlashTint != Vector3.Zero) failures.Add($"the Ranger itself is tinted at tick {tick}");
        }

        var stray = tinted.Count(sample => !flashes.Any(tick => sample.Tick > tick && sample.Tick <= tick + 1 + SelectionFlashTracker.LastVisibleAge));
        if (stray > 0) failures.Add($"the barracks is tinted in {stray} frames that follow no flash");
        var gaps = flashes.Zip(flashes.Skip(1), (a, b) => b - a).ToArray();
        // The phase is stepped in whole ticks, so neighbouring gaps jitter by one tick; over the whole preparation they shrink.
        if (gaps[^1] >= gaps[0] || gaps.Zip(gaps.Skip(1), (a, b) => b > a + 1).Any(slower => slower)) failures.Add($"the flashes do not come faster toward the end (gaps {string.Join(", ", gaps)})");
        Console.WriteLine($"  {peaks} of {flashes.Length} flashes show at their peak in the frame that follows, gaps {string.Join(", ", gaps)} ticks, {stray} tinted frames without a flash");
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
