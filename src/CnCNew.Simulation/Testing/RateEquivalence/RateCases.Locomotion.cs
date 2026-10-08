using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    /// <summary>The position, speed and heading of one object after every step of a move order.</summary>
    private sealed class Track
    {
        public List<Fixed> X { get; } = [];
        public List<Fixed> Y { get; } = [];
        public List<Fixed> Z { get; } = [];
        public List<Fixed> Speed { get; } = [];

        /// <summary>
        /// The speed per frame of the original, as the movement of an <see cref="AIUpdateInterface"/> computes with it (for every other object <see cref="Entity.Speed"/> times the
        /// ticks per frame): above 30 Hz the speed per tick is that value divided by 2 or 4 and cannot tell the top speed from one step of the resolution below it.
        /// </summary>
        public List<Fixed> FrameSpeed { get; } = [];

        private AIUpdateInterface? _ai;
        private bool _aiKnown;
        public List<Fixed> Angle { get; } = [];

        /// <summary>Steps from the order to the end of the move (-1: still moving when the budget ran out).</summary>
        public long EndStep { get; set; } = -1;

        /// <summary>The move ended with <see cref="SimEventKind.MoveArrived"/> (not with a blocked path).</summary>
        public bool Arrived { get; set; }

        public int Steps => X.Count - 1;

        public void Record(Entity unit)
        {
            X.Add(unit.X);
            Y.Add(unit.Y);
            Z.Add(unit.Z);
            Speed.Add(unit.Speed);
            if (!_aiKnown)
            {
                _aiKnown = true;
                foreach (var module in unit.Modules)
                    if (module is AIUpdateInterface ai) { _ai = ai; break; }
            }
            FrameSpeed.Add(_ai is not null ? _ai.SpeedPerOriginalFrame(Stride) : unit.Speed * Stride);
            Angle.Add(unit.Angle);
        }

        public Fixed TopSpeed => Speed.Aggregate(Fixed.Zero, Fixed.Max);

        // "At top speed" is read from the speed per frame (FrameSpeed) and sampled like a length (Stride): once per frame of the original, at the end of the frame. A ground object
        // decides its speed once per frame and covers the frame's step in shares; a sample inside a frame pairs the new speed with a position between two frames, which no 30 Hz
        // sample can show. Only a top speed that never shows at the end of a frame (a track that does not start on one) falls back to every sample.
        public int FirstAtTopSpeed
        {
            get
            {
                var top = FrameSpeed.Aggregate(Fixed.Zero, Fixed.Max);
                if (top <= Fixed.Zero) return -1;
                for (var index = 0; index < FrameSpeed.Count; index += Stride)
                    if (FrameSpeed[index] == top) return index;
                return FrameSpeed.IndexOf(top);
            }
        }

        public int LastAtTopSpeed
        {
            get
            {
                var top = FrameSpeed.Aggregate(Fixed.Zero, Fixed.Max);
                if (top <= Fixed.Zero) return -1;
                var end = FrameSpeed.Count - 1;
                for (var index = end - end % Stride; index >= 0; index -= Stride)
                    if (FrameSpeed[index] == top) return index;
                return FrameSpeed.LastIndexOf(top);
            }
        }

        /// <summary>Recorded steps per sample of a length: the ticks of one frame of the original, so that a curve is measured with the same chord at every rate.</summary>
        public int Stride { get; init; } = 1;

        /// <summary>The length of the polyline through the recorded positions, sampled once per frame of the original from <paramref name="from"/> on.</summary>
        public Fixed Length(int from, int to)
        {
            var sum = Fixed.Zero;
            for (var index = from; index < to; index = Math.Min(index + Stride, to))
            {
                var next = Math.Min(index + Stride, to);
                sum += RateMeasure.Distance(X[index], Y[index], X[next], Y[next]);
            }
            return sum;
        }

        public Fixed Length() => Length(0, Steps);
    }

    /// <summary>Orders <paramref name="unit"/> to a point and records it after every step until the order ends or the budget runs out.</summary>
    private static Track Drive(RateScene scene, Entity unit, Fixed x, Fixed y, long budgetFrames, Track? into = null)
    {
        var track = into ?? new Track { Stride = scene.Rate.TicksPerOriginalFrame };
        if (track.X.Count == 0) track.Record(unit);
        var before = track.Steps;
        track.EndStep = -1;
        track.Arrived = false;
        scene.Move(unit, x, y);
        var budget = scene.Time.OriginalFrames(budgetFrames);
        for (long step = 1; step <= budget; step++)
        {
            var result = scene.Step();
            track.Record(unit);
            foreach (var item in result.Events)
                if (item.Kind == SimEventKind.MoveArrived && item.Entity == unit.Id) track.Arrived = true;
            // The move is over when the object stands: above 30 Hz the order ends in the first tick of the move's last logic frame (the tick of the 30 Hz step) and the object
            // covers the rest of that frame's step in the ticks after it. At 30 Hz nothing is ever inside a frame's step.
            if ((unit.HasMoveTarget || unit.IsInsideFrameStep) && scene.Alive(unit)) continue;
            track.EndStep = before + step;
            break;
        }
        return track;
    }

    private static bool IsGroundDriver(ObjectTemplate template)
    {
        if (template.Locomotor is not { } locomotor || locomotor.Speed <= Fixed.Zero || !template.IsInstantiable) return false;
        if (locomotor.IsWings || template.IsKindOf("PROJECTILE") || template.IsKindOf("SMALL_MISSILE") || template.IsKindOf("BALLISTIC_MISSILE")) return false;
        var drives = false;
        foreach (var module in template.Modules)
        {
            // Movers of their own (flight group, rails, payload planes, random walkers) are not driven by a plain move order to a point.
            if (module.Name is "JetAIUpdate" or "RailedTransportAIUpdate" or "DeliverPayloadAIUpdate" or "WanderAIUpdate" or "MissileAIUpdate" or "RailroadBehavior" or "SlavedUpdate" or "MobMemberSlavedUpdate") return false;
            if (module.Name == "AIUpdateInterface" || module.Name.EndsWith("AIUpdate", StringComparison.Ordinal)) drives = true;
        }
        return drives;
    }

    private static string LocomotorKey(LocomotorInfo locomotor)
    {
        static string Raw(Fixed value) => value.Raw.ToString(CultureInfo.InvariantCulture);
        var text = string.Join(',', Raw(locomotor.Speed), Raw(locomotor.Acceleration), Raw(locomotor.TurnRatePerTick), Raw(locomotor.Braking), Raw(locomotor.MinSpeed), Raw(locomotor.CloseEnoughDist),
            ((long)locomotor.Surfaces).ToString(CultureInfo.InvariantCulture), Raw(locomotor.PreferredHeight), Raw(locomotor.SpeedLimitZ), Raw(locomotor.Lift), locomotor.IsHover ? "h" : "-",
            locomotor.PenaltyDamageState.ToString(CultureInfo.InvariantCulture));
        return locomotor.Damaged is { } damaged ? text + "|" + LocomotorKey(damaged) : text;
    }

    private static RateCaseGroup LocomotionCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, Locomotion);
        var candidates = catalogs.Reference.Templates.Where(IsGroundDriver).OrderBy(template => template.Name, StringComparer.Ordinal).ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chosen = 0;
        foreach (var template in candidates)
        {
            if (!seen.Add(LocomotorKey(template.Locomotor!))) continue;
            chosen++;
            var name = template.Name;
            builder.Add(name, scene => MeasureLocomotion(scene, name), name);
        }
        builder.Note(string.Create(CultureInfo.InvariantCulture,
            $"{candidates.Length} retail objects are driven by a move order on a non-winged locomotor; {chosen} distinct locomotors (30 Hz values), the first object by name stands for each, {candidates.Length - chosen} merged away"));
        builder.NotCovered("locomotion: objects whose locomotor changes with an upgrade or a locomotor set other than SET_NORMAL (only the normal set of each object is driven)");
        builder.NotCovered("locomotion: movement over slopes, water and cliffs (the map of the suite is flat)");
        return builder.Done();
    }

    private static readonly Fixed TurnMargin = Fixed.FromFraction(1, 50);

    private static ImmutableArray<RateQuantity> MeasureLocomotion(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var reference = scene.Reference(name).Locomotor!;
        var perSecond = Fixed.MultiplyDivide(reference.Speed, Fixed.FromInt(Time.SimulationRate.OriginalFramesPerSecond), Fixed.One);
        // Twelve seconds at top speed, between 200 and 1,500 units: the same distance at every rate, because it is taken from the 30 Hz catalog.
        var run = Math.Clamp((perSecond * 12).Floor(), 200, 1500);
        var runSeconds = (Fixed.FromInt(run) / perSecond).Ceiling() * 3 + 60;
        var side = Math.Clamp((perSecond * 4).Floor(), 100, 500);

        // (1) straight run: acceleration, top speed, braking, arrival
        {
            var unit = scene.Spawn(name, 1, 1000, 2000);
            scene.Align();
            var track = Drive(scene, unit, Fixed.FromInt(1000 + run), Fixed.FromInt(2000), RateScene.Seconds(runSeconds));
            var first = track.FirstAtTopSpeed;
            var last = track.LastAtTopSpeed;
            quantities.Distance("top speed per second", track.TopSpeed * scene.Rate.TicksPerSecond);
            quantities.Event("time to top speed", Elapsed(first));
            quantities.Distance("distance to top speed", first >= 0 ? RateMeasure.Distance(track.X[0], track.Y[0], track.X[first], track.Y[first]) : Fixed.Zero, first >= 0);
            var ended = track.EndStep >= 0 && track.Arrived;
            quantities.Distance("braking distance", ended && last >= 0 ? track.Length(last, track.Steps) : Fixed.Zero, ended && last >= 0);
            quantities.Event("braking time", ended && last >= 0 ? track.EndStep - last : -1);
            quantities.Event("arrival time", ended ? Elapsed(track.EndStep) : -1);
            quantities.Distance("arrival error", RateMeasure.Distance(unit.X, unit.Y, Fixed.FromInt(1000 + run), Fixed.FromInt(2000)), ended);
            quantities.Distance("path length", track.Length(), ended);
        }

        // (2) turning: the time until the heading has changed by a quarter and by a half turn, on an order to a point beside and behind the object
        foreach (var (label, dx, dy, angle) in new[] { ("turn 90", 0, 1, FixedTrig.HalfPi), ("turn 180", -1, 0, FixedTrig.Pi) })
        {
            var turning = scene.Fresh();
            var unit = turning.Spawn(name, 1, 2000, 2000);
            turning.Align();
            var start = unit.Angle;
            turning.Move(unit, 2000 + dx * (int)run, 2000 + dy * (int)run);
            var steps = turning.StepUntil(() => FixedTrig.AngleDifference(start, unit.Angle).Abs() >= angle - TurnMargin, RateScene.Seconds(120));
            quantities.Event(label + " time", steps);
        }

        // (3) a square course: four legs, each ordered in the step the previous one ended
        {
            var course = scene.Fresh();
            var unit = course.Spawn(name, 1, 1500, 1500);
            course.Align();
            var corners = new[] { (1500 + (int)side, 1500), (1500 + (int)side, 1500 + (int)side), (1500, 1500 + (int)side), (1500, 1500) };
            var track = new Track { Stride = course.Rate.TicksPerOriginalFrame };
            var complete = true;
            foreach (var (x, y) in corners)
            {
                Drive(course, unit, Fixed.FromInt(x), Fixed.FromInt(y), RateScene.Seconds(runSeconds), track);
                if (track.EndStep >= 0 && track.Arrived) continue;
                complete = false;
                break;
            }
            quantities.Event("course time", complete ? Elapsed(track.Steps) : -1);
            quantities.Distance("course path length", track.Length(), complete);
            quantities.Distance("course end error", RateMeasure.Distance(unit.X, unit.Y, Fixed.FromInt(1500), Fixed.FromInt(1500)), complete);
        }

        // (4) the damaged constants (SpeedDamaged, AccelerationDamaged, TurnRateDamaged) on the same straight run
        if (reference.Damaged is not null && scene.Reference(name).Modules.Any(module => module.Name.EndsWith("Body", StringComparison.Ordinal)))
        {
            var damaged = scene.Fresh();
            var unit = damaged.Spawn(name, 1, 1000, 2000);
            if (unit.Body is not null)
            {
                // A twentieth of the maximum health is below both damage thresholds of the retail data.
                damaged.HurtTo(unit, 1, 20);
                damaged.Align();
                var track = Drive(damaged, unit, Fixed.FromInt(1000 + run), Fixed.FromInt(2000), RateScene.Seconds(runSeconds * 4));
                var ended = track.EndStep >= 0 && track.Arrived;
                quantities.Distance("damaged top speed per second", track.TopSpeed * damaged.Rate.TicksPerSecond);
                quantities.Event("damaged time to top speed", Elapsed(track.FirstAtTopSpeed));
                quantities.Event("damaged arrival time", ended ? Elapsed(track.EndStep) : -1);
            }
        }

        return quantities.Done();
    }
}
