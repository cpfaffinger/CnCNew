using System.Numerics;
using CnCNew.Game.Live;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;

namespace CnCNew.PresentationTool;

/// <summary>
/// The headless checks of the acceptance scenes (<see cref="AcceptanceScenes"/>, <c>docs/ACCEPTANCE.md</c>): one sample per original frame of every tagged object, as the simulation
/// has it and as the presentation draws it, and per scene the facts a person is asked to look at, as far as numbers can state them. A green audit says the scene shows what it is
/// meant to show; it does not say that it looks right.
/// </summary>
internal sealed class AcceptanceAudit
{
    private sealed record Row(bool Alive, float X, float Y, float Speed, float Yaw, bool Contained, float Health, bool Drawn, string Conditions, bool Healing, float WheelAngle, float Pitch, float Roll,
        float WheelTravel, float Construction);

    private readonly LiveScenario _live;
    private readonly List<Dictionary<string, Row>> _frames = [];
    private readonly Dictionary<string, float> _radius = new(StringComparer.OrdinalIgnoreCase);
    private float _highestStranger;
    private float _groundZ = float.NaN;

    private AcceptanceAudit(LiveScenario live) => _live = live;

    public static AcceptanceAudit? Create(LiveScenario live)
        => AcceptanceScenes.All.Any(scene => scene.Name == live.Scenario.Name) ? new AcceptanceAudit(live) : null;

    private static float F(CnCNew.Simulation.Numerics.Fixed value) => CnCNew.Presentation.FixedConvert.ToSingle(value);

    public void Sample(RenderFrame frame)
    {
        var rows = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        var tagged = new HashSet<EntityId>();
        foreach (var (tag, id) in _live.Tags)
        {
            tagged.Add(id);
            var entity = _live.World.FindEntity(id);
            if (entity is null || entity.DestroyRequested)
            {
                rows[tag] = new Row(false, 0, 0, 0, 0, false, 0, false, "gone", false, 0, 0, 0, 0, 0);
                continue;
            }
            _radius[tag] = F(entity.Template.BoundingCircleRadius);
            if (float.IsNaN(_groundZ)) _groundZ = F(entity.Z);
            var drawable = frame.Drawables.FirstOrDefault(value => value.Entity == id);
            var health = entity.Body is { } body && body.MaxHealth.Raw != 0 ? F(body.Health) / F(body.MaxHealth) : 1f;
            rows[tag] = new Row(!entity.EffectivelyDead, F(entity.X) - _live.Anchor.X, F(entity.Y) - _live.Anchor.Y, F(entity.Speed) * _live.World.Time.TicksPerSecond, F(entity.Angle),
                entity.ContainedBy.IsValid, health, drawable is not null, drawable?.Conditions.ToString() ?? "(not drawn)", drawable?.ShowsHealing ?? false,
                drawable?.Chassis.WheelAngle ?? 0f, drawable?.Chassis.Pitch ?? 0f, drawable?.Chassis.Roll ?? 0f, drawable?.WheelTravel ?? 0f, drawable?.ConstructionProgress01 ?? 0f);
        }
        _frames.Add(rows);
        // Anything drawn that the scene did not place is a projectile or debris; how high it flies tells an arc from a straight line.
        foreach (var drawable in frame.Drawables)
            if (drawable.Entity.IsValid && !drawable.IsStatic && !tagged.Contains(drawable.Entity) && !float.IsNaN(_groundZ))
                _highestStranger = MathF.Max(_highestStranger, drawable.Position.Z - _groundZ);
    }

    public List<string> Check(List<SimEvent> events)
    {
        var failures = new List<string>();
        Console.WriteLine("acceptance scene facts");
        switch (_live.Scenario.Name)
        {
            case "acc-march": CheckMarch(failures); break;
            case "acc-wheels": CheckWheels(failures); break;
            case "acc-board": CheckBoard(failures); break;
            case "acc-artillery": CheckArtillery(events, failures); break;
            case "acc-repair": CheckRepair(failures); break;
            case "acc-firefight": CheckFirefight(events, failures); break;
            case "acc-build": CheckBuild(failures); break;
        }
        Console.WriteLine();
        return failures;
    }

    // The sample of original frame N (1-based: the list starts with the first frame the audit advanced).
    private Dictionary<string, Row> At(long frame) => _frames[(int)Math.Clamp(frame - 1, 0, _frames.Count - 1)];

    private string TagOf(EntityId id) => _live.Tags.FirstOrDefault(pair => pair.Value == id).Key ?? $"#{id.Value}";

    private float PathLength(string tag, long from, long to)
    {
        var length = 0f;
        for (var frame = from + 1; frame <= to; frame++)
        {
            var a = At(frame - 1)[tag];
            var b = At(frame)[tag];
            if (a.Alive && b.Alive && !a.Contained && !b.Contained) length += Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
        }
        return length;
    }

    private void CheckApart(IEnumerable<string> tags, long frame, string when, List<string> failures)
    {
        var list = tags.Where(tag => At(frame)[tag] is { Alive: true, Contained: false }).ToArray();
        var closest = float.MaxValue;
        for (var first = 0; first < list.Length; first++)
            for (var second = first + 1; second < list.Length; second++)
            {
                var a = At(frame)[list[first]];
                var b = At(frame)[list[second]];
                var apart = Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
                var need = 0.9f * (_radius[list[first]] + _radius[list[second]]);
                closest = MathF.Min(closest, apart - need);
                if (apart < need) failures.Add($"{when}: {list[first]} and {list[second]} stand {apart:F1} apart, inside each other (bounding radii {_radius[list[first]]:F1} and {_radius[list[second]]:F1})");
            }
        Console.WriteLine($"  {when}: smallest margin between two bounding circles {closest:F1}");
    }

    private void CheckDrawn(List<string> failures)
    {
        foreach (var tag in _live.Tags.Keys)
        {
            var missing = _frames.Count(rows => rows.TryGetValue(tag, out var row) && row is { Alive: true, Contained: false, Drawn: false });
            if (missing > 0) failures.Add($"{tag}: not drawn in {missing} frame(s) while alive and outside a transport");
        }
    }

    private void CheckMarch(List<string> failures)
    {
        var tags = _live.Tags.Keys.ToArray();
        for (var leg = 0; leg < AcceptanceScenes.MarchLegs.Length; leg++)
        {
            var start = AcceptanceScenes.MarchLegs[leg];
            var end = Math.Min(start + AcceptanceScenes.MarchLegFrames - 1, _frames.Count);
            var goal = new Vector2(leg % 2 == 0 ? AcceptanceScenes.MarchEast : AcceptanceScenes.MarchWest, 0);
            foreach (var tag in tags)
            {
                var from = At(start)[tag];
                var to = At(end)[tag];
                var stillMoving = Enumerable.Range(0, 30).Count(back => At(end - back)[tag].Speed > 0.5f);
                var turning = Enumerable.Range(0, 30).Max(back => MathF.Abs(Wrap(At(end - back)[tag].Yaw - to.Yaw)));
                var offGoal = Vector2.Distance(new Vector2(to.X, to.Y), goal);
                var straight = Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y));
                var path = PathLength(tag, start, end);
                var arrival = Enumerable.Range((int)start, (int)(end - start + 1)).FirstOrDefault(frame => Enumerable.Range(frame, (int)Math.Min(15, end - frame + 1)).All(later => At(later)[tag].Speed <= 0.5f) && frame > start + 15);
                Console.WriteLine($"  leg {leg + 1} {tag,-8} rests {offGoal,5:F1} from the goal, path {path,6:F1} for {straight,6:F1} straight, at rest from frame {arrival} (+{arrival - start})");
                // Known finding S2 (below) for the soldiers, who arrive last; a vehicle that does not come to rest is a new failure.
                var prefix = tag.StartsWith("ranger", StringComparison.Ordinal) ? "KNOWN S2 " : string.Empty;
                if (stillMoving > 0) failures.Add($"{prefix}leg {leg + 1}: {tag} still walks in {stillMoving} of the last 30 frames, {AcceptanceScenes.MarchLegFrames / 30} s after the order");
                else if (turning > 0.05f) failures.Add($"leg {leg + 1}: {tag} still turns in the last 30 frames ({turning * 180 / MathF.PI:F0} degrees)");
                if (offGoal > 110f) failures.Add($"leg {leg + 1}: {tag} rests {offGoal:F1} from the goal");
                // Known finding (docs/ACCEPTANCE.md, "Befunde fuer die Simulation" S2): who arrives after the vehicles is routed around the parked group to a free place on its far side.
                if (path > 1.4f * straight + 20f && arrival > 0) failures.Add($"KNOWN S2 leg {leg + 1}: {tag} walked {path:F1} for {straight:F1} straight and rests only {arrival - start} frames after the order (detour around the group)");
            }
            CheckApart(tags, end, $"end of leg {leg + 1}", failures);
        }
        CheckDrawn(failures);
    }

    private void CheckWheels(List<string> failures)
    {
        var course = AcceptanceScenes.WheelCourse;
        for (var corner = 0; corner < course.Length; corner++)
        {
            var frame = Math.Min(AcceptanceScenes.WheelFirstFrame + (corner + 1) * AcceptanceScenes.WheelLegFrames - 1, _frames.Count);
            var row = At(frame)["humvee"];
            var off = Vector2.Distance(new Vector2(row.X, row.Y), new Vector2(course[corner].X, course[corner].Y));
            Console.WriteLine($"  corner {corner + 1} ({course[corner].X},{course[corner].Y}): humvee {off:F1} away at frame {frame}, speed {row.Speed:F1}");
            if (off > 20f) failures.Add($"humvee is {off:F1} from corner {corner + 1} when the next order comes");
        }
        foreach (var tag in new[] { "humvee", "dozer" })
        {
            var rows = _frames.Select(frame => frame[tag]).ToArray();
            var steer = rows.Max(row => MathF.Abs(row.WheelAngle));
            var nod = rows.Max(row => MathF.Abs(row.Pitch));
            var lean = rows.Max(row => MathF.Abs(row.Roll));
            var last = rows[^1];
            var travel = MathF.Abs(last.WheelTravel - rows[0].WheelTravel);
            // A jitter is a pitch that changes sign from frame to frame while the vehicle stands.
            var jitter = 0;
            for (var index = 2; index < rows.Length; index++)
                if (rows[index].Speed <= 0.01f && rows[index - 1].Speed <= 0.01f && (rows[index].Pitch - rows[index - 1].Pitch) * (rows[index - 1].Pitch - rows[index - 2].Pitch) < -1e-9f) jitter++;
            Console.WriteLine($"  {tag}: largest wheel angle {steer * 180 / MathF.PI:F1} degrees, largest body pitch {nod * 180 / MathF.PI:F2} and roll {lean * 180 / MathF.PI:F2} degrees, wheel travel {travel:F1}, at the end wheel angle {last.WheelAngle * 180 / MathF.PI:F1}, pitch {last.Pitch * 180 / MathF.PI:F2}, speed {last.Speed:F1}; pitch reversals while standing {jitter}");
            if (travel < 1f) failures.Add($"{tag}: the wheels did not turn with the distance (wheel travel {travel:F2})");
            if (last.Speed > 0.5f) failures.Add($"{tag}: still moving at the end");
            if (MathF.Abs(last.WheelAngle) > 0.03f) failures.Add($"{tag}: the wheels are still turned at rest ({last.WheelAngle * 180 / MathF.PI:F1} degrees)");
            if (MathF.Abs(last.Pitch) > 0.01f) failures.Add($"{tag}: the body has not settled at rest (pitch {last.Pitch * 180 / MathF.PI:F2} degrees)");
            if (tag == "humvee" && steer < 0.05f) failures.Add("humvee: the wheels never steer");
            // The Humvee's locomotor has no pitch from acceleration (ForwardAccelerationPitchFactor 0), and hardly rolls (LateralAccelerationRollFactor 0.1); the dozer nods (factor 0.5).
            if (tag == "dozer" && nod < 0.002f) failures.Add("dozer: the body never nods when it starts or brakes");
        }
        CheckDrawn(failures);
    }

    private void CheckBoard(List<string> failures)
    {
        var rangers = Enumerable.Range(1, AcceptanceScenes.BoardSquad).Select(index => $"ranger{index}").ToArray();
        void Boarding(string name, long order, long deadline)
        {
            foreach (var tag in rangers)
            {
                var inside = Enumerable.Range((int)order, (int)(deadline - order)).FirstOrDefault(frame => At(frame)[tag].Contained);
                if (inside == 0) { failures.Add($"{name}: {tag} is not in the Humvee by frame {deadline}"); continue; }
                var from = At(order)[tag];
                var humvee = At(order)["humvee"];
                var straight = MathF.Max(0f, Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(humvee.X, humvee.Y)) - _radius["humvee"]);
                var path = PathLength(tag, order, inside);
                Console.WriteLine($"  {name}: {tag} boards in frame {inside} (+{inside - order}), walked {path:F1} for {straight:F1} to the vehicle's edge");
                if (path > 1.5f * straight + 25f) failures.Add($"{name}: {tag} walked {path:F1} for {straight:F1} (around the vehicle)");
            }
        }
        void Leaving(string name, long order)
        {
            var frame = Math.Min(order + 120, _frames.Count);
            var humvee = At(frame)["humvee"];
            foreach (var tag in rangers)
            {
                var row = At(frame)[tag];
                if (!row.Alive) { failures.Add($"{name}: {tag} is dead"); continue; }
                if (row.Contained) { failures.Add($"{name}: {tag} is still inside 120 frames after the order"); continue; }
                var apart = Vector2.Distance(new Vector2(row.X, row.Y), new Vector2(humvee.X, humvee.Y));
                Console.WriteLine($"  {name}: {tag} stands {apart:F1} from the Humvee's centre (radii {_radius[tag]:F1} and {_radius["humvee"]:F1}), speed {row.Speed:F1}");
                if (apart > 90f) failures.Add($"{name}: {tag} stands {apart:F1} from the Humvee");
            }
            // Known finding (docs/ACCEPTANCE.md, "Befunde fuer die Simulation" S1): evacuated riders are put on the transport's centre and stay there.
            var stacked = new List<string>();
            CheckApart(rangers.Append("humvee"), frame, name, stacked);
            if (stacked.Count > 0) failures.Add($"KNOWN S1 {name}: {stacked.Count} pairs of Rangers and Humvee stand inside each other after the exit (first: {stacked[0]})");
        }
        Boarding("first boarding", AcceptanceScenes.BoardFirst, AcceptanceScenes.BoardDrive);
        Leaving("first exit", AcceptanceScenes.BoardOut);
        Boarding("second boarding", AcceptanceScenes.BoardSecond, AcceptanceScenes.BoardDriveBack);
        Leaving("second exit", AcceptanceScenes.BoardOutAgain);
        CheckDrawn(failures);
    }

    private void CheckArtillery(List<SimEvent> events, List<string> failures)
    {
        foreach (var tag in new[] { "tomahawk", "inferno" })
        {
            var shots = events.Where(simEvent => simEvent.Kind == SimEventKind.WeaponFired && TagOf(simEvent.Entity) == tag).Select(simEvent => simEvent.Tick).ToArray();
            Console.WriteLine($"  {tag}: {shots.Length} shots, frames {string.Join(", ", shots.Take(8))}");
            if (shots.Length < 2) failures.Add($"{tag} fired {shots.Length} shot(s), expected at least 2");
        }
        var target = _frames[^1]["target"];
        var firstHurt = _frames.FindIndex(rows => rows["target"].Health < 0.999f) + 1;
        Console.WriteLine($"  target: health {target.Health * 100:F0} % at the end, first damage in frame {firstHurt}; highest flying object {_highestStranger:F1} above the ground");
        if (target.Health > 0.97f) failures.Add($"the target was hardly hit (health {target.Health * 100:F0} %)");
        if (_highestStranger < 15f) failures.Add($"no projectile was drawn higher than {_highestStranger:F1} above the ground (no arc)");
        CheckDrawn(failures);
    }

    private void CheckRepair(List<string> failures)
    {
        long hit = AcceptanceScenes.RepairHit, order = AcceptanceScenes.RepairOrder, end = _frames.Count;
        var hurt = At(hit + 5)["plant"];
        var done = Enumerable.Range((int)order, (int)(end - order)).FirstOrDefault(frame => At(frame)["plant"].Health >= 0.999f);
        var icon = Enumerable.Range((int)hit, (int)(end - hit) + 1).Where(frame => At(frame)["plant"].Healing).ToArray();
        var firstRise = Enumerable.Range((int)order, (int)(end - order)).FirstOrDefault(frame => At(frame)["plant"].Health > hurt.Health + 1e-4f);
        var looksWhole = Enumerable.Range((int)order, (int)(end - order)).FirstOrDefault(frame => !At(frame)["plant"].Conditions.Contains("DAMAGED", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"  health {hurt.Health * 100:F0} % after the hit [{hurt.Conditions}], first repair step in frame {firstRise}, undamaged model state from frame {looksWhole} ({(looksWhole > 0 ? At(looksWhole)["plant"].Health * 100 : 0):F0} %), whole again in frame {done}, wrench shown in {icon.Length} frames ({(icon.Length > 0 ? $"{icon[0]} to {icon[^1]}" : "never")})");
        if (MathF.Abs(hurt.Health - (1f - AcceptanceScenes.RepairDamagePercent / 100f)) > 0.02f) failures.Add($"the hit left {hurt.Health * 100:F0} % health, expected {100 - AcceptanceScenes.RepairDamagePercent:F0} %");
        if (!hurt.Conditions.Contains("DAMAGED", StringComparison.OrdinalIgnoreCase)) failures.Add($"the damaged building does not show a damaged model state [{hurt.Conditions}]");
        if (done == 0) failures.Add($"the building is not whole again by frame {end}");
        if (looksWhole == 0) failures.Add("the building never returns to its undamaged model state");
        if (icon.Length == 0) failures.Add("the wrench symbol was never shown");
        else
        {
            if (firstRise > 0 && icon[0] > firstRise + 45) failures.Add($"the wrench appears in frame {icon[0]}, {icon[0] - firstRise} frames after the first repair step");
            if (done > 0 && icon[^1] > done + 100) failures.Add($"the wrench is still shown in frame {icon[^1]}, {icon[^1] - done} frames after the building was whole");
        }
        CheckDrawn(failures);
    }

    private void CheckFirefight(List<SimEvent> events, List<string> failures)
    {
        var shots = events.Where(simEvent => simEvent.Kind == SimEventKind.WeaponFired).GroupBy(simEvent => TagOf(simEvent.Entity)).ToDictionary(group => group.Key, group => group.Count());
        var deaths = events.Where(simEvent => simEvent.Kind == SimEventKind.EntityDied).Select(simEvent => $"{TagOf(simEvent.Entity)}@{simEvent.Tick}").ToArray();
        Console.WriteLine($"  shots: {string.Join(", ", shots.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value}"))}");
        Console.WriteLine($"  deaths: {string.Join(", ", deaths)}");
        if (shots.GetValueOrDefault("gattling") < 10) failures.Add($"the Gattling fired {shots.GetValueOrDefault("gattling")} shots, expected at least 10");
        var rangers = Enumerable.Range(1, AcceptanceScenes.FirefightRangers).Sum(index => shots.GetValueOrDefault($"ranger{index}"));
        var rebels = Enumerable.Range(1, AcceptanceScenes.FirefightRebels).Sum(index => shots.GetValueOrDefault($"rebel{index}"));
        if (rangers < 3) failures.Add($"the Rangers fired {rangers} shots, expected at least 3");
        if (rebels < 3) failures.Add($"the Rebels fired {rebels} shots, expected at least 3");
        if (deaths.Length == 0) failures.Add("nobody died");
        var firing = _frames.Select(rows => rows["gattling"].Conditions).Where(conditions => conditions.Contains("FIRING", StringComparison.OrdinalIgnoreCase)).ToArray();
        var states = firing.Distinct().Take(6).ToArray();
        Console.WriteLine($"  gattling: drawn firing in {firing.Length} frames; states {string.Join(" | ", states)}");
        if (firing.Length == 0) failures.Add("the Gattling is never drawn in a firing state");
        if (firing.Length >= _frames.Count - 30) failures.Add("the Gattling is drawn firing in (nearly) every frame, also before and after the fight");
        CheckDrawn(failures);
    }

    private void CheckBuild(List<string> failures)
    {
        foreach (var (tag, order) in new[] { ("plant", AcceptanceScenes.BuildFirstOrder), ("barracks", AcceptanceScenes.BuildSecondOrder) })
        {
            var rows = _frames.Select((frame, index) => (Frame: index + 1, Row: frame.GetValueOrDefault(tag))).Where(entry => entry.Row is { Alive: true }).ToArray();
            if (rows.Length == 0) { failures.Add($"{tag}: the structure never appeared"); continue; }
            var appeared = rows[0].Frame;
            var finished = rows.FirstOrDefault(entry => entry.Row!.Construction >= 0.999f && !entry.Row.Conditions.Contains("CONSTRUCT", StringComparison.OrdinalIgnoreCase)).Frame;
            var falling = 0;
            for (var index = 1; index < rows.Length; index++) if (rows[index].Row!.Construction < rows[index - 1].Row!.Construction - 1e-4f) falling++;
            var building = rows.Where(entry => entry.Row!.Conditions.Contains("CONSTRUCT", StringComparison.OrdinalIgnoreCase)).Select(entry => entry.Row!.Conditions).Distinct().Take(4).ToArray();
            Console.WriteLine($"  {tag}: site appears in frame {appeared} (+{appeared - order} after the order), finished in frame {finished}, progress fell back {falling} times, states while building: {string.Join(" | ", building)}; at the end [{rows[^1].Row!.Conditions}] health {rows[^1].Row!.Health * 100:F0} %");
            if (building.Length == 0) failures.Add($"{tag}: never drawn in a construction state");
            if (finished == 0) failures.Add($"{tag}: not finished at the end of the scene");
            if (falling > 0) failures.Add($"{tag}: the construction progress fell back {falling} times");
            if (rows[^1].Row!.Health < 0.999f) failures.Add($"{tag}: finished with {rows[^1].Row!.Health * 100:F0} % health");
            var undrawn = rows.Count(entry => !entry.Row!.Drawn);
            if (undrawn > 0) failures.Add($"{tag}: not drawn in {undrawn} frame(s)");
        }
        CheckDrawn(failures);
    }

    private static float Wrap(float radians)
    {
        while (radians > MathF.PI) radians -= 2 * MathF.PI;
        while (radians < -MathF.PI) radians += 2 * MathF.PI;
        return radians;
    }
}
