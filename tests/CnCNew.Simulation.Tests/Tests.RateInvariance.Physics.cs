using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, package "physics": recursions the original runs once per logic frame (a fall, a slide, a train, healing in slices) are the same game at 30, 60
/// and 120 Hz. Each test runs one scene at the three rates on the synthetic fixture and compares the state at every frame boundary of the original – the
/// values of 60 and 120 Hz must be the values of 30 Hz, not merely close to them – and, where something moves, that the ticks in between lie on the way.
/// The tests name their rates and do not follow <c>--tick-rate</c>.
/// </summary>
static partial class Tests
{
    /// <summary>Runs <paramref name="scene"/> at 30, 60 and 120 Hz and hands the three results to <paramref name="compare"/> (reference first).</summary>
    private static void AtThreeRates<T>(Func<SimulationRate, T> scene, Action<T, T, string> compare)
    {
        var reference = scene(SimulationRate.Hz30);
        compare(reference, scene(SimulationRate.Hz60), "60 Hz");
        compare(reference, scene(SimulationRate.Hz120), "120 Hz");
    }

    private static void SameSeries(IReadOnlyList<long> reference, IReadOnlyList<long> other, string what)
    {
        Check.That(reference.Count == other.Count, what + ": " + other.Count + " frames instead of " + reference.Count + ".");
        for (var index = 0; index < reference.Count; index++)
            Check.That(reference[index] == other[index], what + ": frame " + index + " is " + other[index] + " instead of " + reference[index] + ".");
    }

    /// <summary>
    /// <c>ToppleUpdate</c>: the angular velocity changes once per frame of the original, the angle moves every tick. The angle at every frame boundary, the
    /// number of frames of the fall and the frame of the death are those of 30 Hz; above 30 Hz the angle of a tick inside a frame lies between its neighbours.
    /// </summary>
    public static void RateInvarianceToppleFall()
    {
        AtThreeRates(rate =>
        {
            var (world, feeder, fixture) = RatedWorld(rate, CollisionFiles(), CollisionTerrain());
            feeder.Spawn(2, "Tree", x: 40, y: 0);
            world.Step();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var tree = world.FindEntity(new EntityId(1)) ?? throw new InvalidOperationException("No tree.");
            var topple = tree.Modules.OfType<ToppleUpdate>().First();
            // A push of 0.3 units per frame of the original, whatever the rate.
            topple.ApplyTopplingSpeed(world, Fixed.One, Fixed.Zero, Fixed.FromDecimal(0.3m));
            var angles = new List<long>();
            var perFrame = world.Time.TicksPerOriginalFrame;
            var monotone = true;
            long died = -1;
            byte[]? midFrame = null;
            for (var tick = 1; tick <= world.Frames(300) && died < 0; tick++)
            {
                var before = topple.ToppleAngle;
                var result = world.Step();
                if (result.Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity.Value == 1)) died = tick;
                // The first update comes one tick after the push; from there on every frame of the tree is perFrame ticks.
                if (tick >= 2 && (tick - 1) % perFrame == 0) angles.Add(topple.ToppleAngle.Raw);
                if (tick == 2 + perFrame && perFrame > 1) midFrame = WorldSnapshot.Write(world);
                if (tick > 1 && tick <= perFrame && topple.ToppleAngle <= before) monotone = false;
            }
            if (midFrame is not null)
            {
                // A snapshot taken inside a frame of the tree carries the phase and the step of that frame.
                var live = WorldSnapshot.Restore(midFrame, fixture.Templates, fixture.Fingerprint, CollisionTerrain());
                var again = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint, CollisionTerrain());
                for (var tick = 0; tick < world.Frames(40); tick++)
                    Check.That(live.Step().StateHash == again.Step().StateHash, "A tree restored inside a frame keeps falling identically (" + rate.TicksPerSecond + " Hz, step " + tick + ").");
            }
            return (Angles: angles, DiedFrame: died < 0 ? -1 : world.Time.ToOriginalFrames(died - 1), Exact: died < 0 || (died - 1) % perFrame == 0, Monotone: monotone);
        }, (reference, other, rate) =>
        {
            Check.That(reference.DiedFrame > 3, "Setup: the tree falls for several frames at 30 Hz (" + reference.DiedFrame + ").");
            Check.That(other.DiedFrame == reference.DiedFrame && other.Exact, "The tree dies in the same frame of the original at " + rate + ": " + other.DiedFrame + " instead of " + reference.DiedFrame + ".");
            SameSeries(reference.Angles, other.Angles, "Topple angle at " + rate);
            Check.That(other.Monotone, "Inside its first frame the angle advances with every tick at " + rate + ".");
        });
    }

    /// <summary>
    /// Runs a world (standing at a frame boundary) for <paramref name="frames"/> frames of the original and returns, per frame, what <paramref name="sample"/> reads at the frame boundary
    /// followed by every chosen event of the frame (kind, entity, value) mixed into one number – the same list at every rate when the game is the same.
    /// </summary>
    private static List<long> FrameSeries(SimulationWorld world, int frames, Func<long> sample, Func<SimEvent, bool>? events = null)
    {
        var series = new List<long>();
        var perFrame = world.Time.TicksPerOriginalFrame;
        for (var frame = 0; frame < frames; frame++)
        {
            long mixed = 17;
            // The step of tick t at 30 Hz is the step of tick t · perFrame above it: the first frame is the one step of the aligned tick the series starts
            // at, every later one the perFrame steps that end on the next multiple.
            for (var tick = 0; tick < (frame == 0 ? 1 : perFrame); tick++)
                foreach (var item in world.Step().Events)
                    if (events is not null && events(item)) mixed = unchecked(mixed * 31 + (long)item.Kind * 1_000_003 + item.Entity.Value * 7919 + item.Value);
            series.Add(sample());
            if (events is not null) series.Add(mixed);
        }
        return series;
    }

    /// <summary>
    /// <c>StructureToppleUpdate</c>: acceleration and structural decay once per frame of the original, the angle every tick, the crushing shots with the frame's
    /// last tick, done one frame after the building lies flat. Angle, shots (and what they damage) and the frame it is done in are those of 30 Hz.
    /// </summary>
    public static void RateInvarianceStructureTopple()
    {
        foreach (var building in new[] { "Building", "StiffTower" })
            AtThreeRates(rate =>
            {
                var (world, feeder, _) = RatedWorld(rate, CollisionFiles(), CollisionTerrain());
                feeder.Spawn(1, building, x: 0, y: 0);
                // Bystanders on both sides: the shots of the falling edge hurt whoever stands there.
                for (var index = 0; index < 8; index++) feeder.Spawn(2, "Blocker", x: index < 4 ? 30 : -30, y: (index % 4 - 2) * 15);
                world.Step();
                while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
                feeder.Damage(1, 1000);
                var topple = world.FindEntity(new EntityId(1))!.Modules.OfType<StructureToppleUpdate>().Single();
                var done = -1;
                var frame = 0;
                var series = FrameSeries(world, 300, () =>
                {
                    frame++;
                    if (done < 0 && topple.IsToppled) done = frame;
                    return topple.Angle.Raw;
                }, item => item.Kind is SimEventKind.EntityDamaged or SimEventKind.EntityDied or SimEventKind.WeaponFired or SimEventKind.WeaponFireRequested);
                return (Series: series, Done: done);
            }, (reference, other, rate) =>
            {
                Check.That(reference.Done > 20, "Setup: the " + building + " falls and is done at 30 Hz (frame " + reference.Done + ").");
                Check.That(other.Done == reference.Done, "The " + building + " is done in the same frame at " + rate + ": " + other.Done + " instead of " + reference.Done + ".");
                SameSeries(reference.Series, other.Series, building + " angle and crushing at " + rate);
            });
    }

    /// <summary>
    /// <c>TensileFormationUpdate</c> (the avalanche) on a slope that ends in a plain: inertia, friction and the pull of the links once per frame of the original, the rocks move
    /// every tick. Every rock is where it is at 30 Hz at each frame boundary of the slide, the neighbours start sliding in the same frame, the last frame in
    /// which the hurt rock drops (what the presentation shows as FREEFALL) is the same, and the slide ends after 300 frames.
    /// </summary>
    public static void RateInvarianceAvalanche()
    {
        AtThreeRates(rate =>
        {
            var terrain = CnCNew.Simulation.Terrain.TerrainMap.Build(SyntheticMaps.Build(60, 40, 1, height: (x, _) => Math.Max(x - 26, 0)), "slope");
            var (world, feeder, fixture) = RatedWorld(rate, SlaveFiles(), terrain);
            feeder.Spawn(1, "SvRock", x: 300, y: 200);
            feeder.Spawn(1, "SvRock", x: 310, y: 200);
            feeder.Spawn(1, "SvRock", x: 320, y: 215);
            for (var tick = 0; tick < world.Frames(100); tick++) world.Step();
            var rocks = world.Entities.Where(value => value.Template.Name == "SvRock").OrderBy(value => value.Id.Value).ToArray();
            var modules = rocks.Select(rock => ModuleOf<TensileFormationUpdate>(rock)).ToArray();
            var start = rocks[0].X;
            feeder.Damage(rocks[0].Id.Value, 60);
            // The step in which the hurt rock notices is the first tick of the first frame of its slide.
            var perFrame = world.Time.TicksPerOriginalFrame;
            for (var tick = 0; tick < world.Frames(40) && !modules[0].Enabled; tick++) world.Step();
            Check.That(modules[0].Enabled && modules[0].Life == 1, "Setup: the hurt rock started to slide.");
            var between = true;
            for (var tick = 1; tick < perFrame; tick++) world.Step();
            var series = new List<long>();
            var lastDrop = -1;
            var joined = new int[rocks.Length];
            for (var frame = 2; frame <= 340; frame++)
            {
                var (beforeX, beforeZ) = (rocks[0].X, rocks.Select(rock => rock.Z).ToArray());
                for (var tick = 1; tick <= perFrame; tick++)
                {
                    world.Step();
                    // Inside a frame the rock is on the way from where the frame began to where it ends (it slides towards smaller x).
                    if (tick < perFrame && frame is > 5 and < 100 && rocks[0].X >= beforeX) between = false;
                    if (frame == 50 && tick == 1 && perFrame > 1)
                    {
                        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
                        Check.That(restored.StateHash == world.StateHash, "A formation saved inside a frame of its slide round-trips (" + rate.TicksPerSecond + " Hz).");
                    }
                }
                for (var index = 0; index < rocks.Length; index++)
                {
                    series.Add(rocks[index].X.Raw);
                    series.Add(rocks[index].Y.Raw);
                    series.Add(rocks[index].Z.Raw);
                    if (index == 0 && rocks[index].Z < beforeZ[index]) lastDrop = frame;
                    if (joined[index] == 0 && modules[index].Enabled) joined[index] = frame;
                }
            }
            return (Series: series, Slid: start - rocks[0].X, LastDrop: lastDrop, Joined: string.Join(",", joined), Life: world.Time.ToOriginalFrames(modules[0].Life - 1), Between: between);
        }, (reference, other, rate) =>
        {
            Check.That(reference.Slid > Fixed.FromInt(3) && reference.LastDrop > 20, "Setup: the hurt rock slid downhill at 30 Hz (" + reference.Slid + ", last drop in frame " + reference.LastDrop + ").");
            Check.That(other.Joined == reference.Joined, "The neighbours start sliding in the same frames at " + rate + ": " + other.Joined + " instead of " + reference.Joined + ".");
            Check.That(other.LastDrop == reference.LastDrop, "The last frame in which the hurt rock drops is the same at " + rate + ": " + other.LastDrop + " instead of " + reference.LastDrop + ".");
            Check.That(other.Life == reference.Life && reference.Life == 300, "The slide lasts 300 frames at " + rate + " (" + other.Life + ").");
            SameSeries(reference.Series, other.Series, "Rocks of the avalanche at " + rate);
            Check.That(other.Between, "Inside a frame the rock is between the two frame boundaries at " + rate + ".");
        });
    }

    /// <summary>
    /// <c>BattleBusSlowDeathBehavior</c>: the first death (the second life of the <c>UndeadBody</c>) throws the bus; height += speed and speed += gravity once
    /// per frame of the original, the height moves every tick. Height at every frame boundary, apex and the frame of the landing are those of 30 Hz.
    /// </summary>
    public static void RateInvarianceBattleBusThrow()
    {
        AtThreeRates(rate =>
        {
            var (map, fixture, terrain) = MissionMap([Scr.Script("Idle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NO_OP")], oneShot: true)],
                objects: [Placed("AlphaBus", 300m, 200m, "teamA", "Bus")], rate: rate);
            var session = CnCNew.Simulation.Mission.MissionSession.Start(CnCNew.Simulation.Mission.MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            var world = session.World;
            Check.That(world.Time.Rate == rate, "Setup: the mission runs at " + rate.TicksPerSecond + " Hz.");
            for (var tick = 0; tick < world.Frames(3); tick++) session.Step();
            var bus = NamedIn(session, "Bus")!;
            var behavior = bus.Modules.OfType<BattleBusSlowDeathBehavior>().Single();
            // A lethal hit that is not unresistable: the second life begins and the bus is thrown.
            world.Damage(bus.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(500), DamageTypes.Explosion, DeathTypes.Normal, false));
            Check.That(!bus.EffectivelyDead && bus.Body is UndeadBody { IsSecondLife: true }, "Setup: the second life began.");
            var heights = new List<long>();
            var perFrame = world.Time.TicksPerOriginalFrame;
            var (apex, landed, rising) = (Fixed.Zero, -1, true);
            for (var frame = 0; frame < 120; frame++)
            {
                var before = bus.Z;
                for (var tick = 0; tick < (frame == 0 ? 1 : perFrame); tick++)
                {
                    session.Step();
                    // On the way up every tick lifts the bus.
                    if (frame is > 0 and < 5 && bus.Z <= before && tick < perFrame - 1) rising = false;
                }
                heights.Add(bus.Z.Raw);
                apex = Fixed.Max(apex, bus.Z);
                if (landed < 0 && frame > 0 && behavior.HasLanded(world)) landed = frame;
            }
            return (Heights: heights, Apex: apex, Landed: landed, Rising: rising);
        }, (reference, other, rate) =>
        {
            Check.That(reference.Apex > Fixed.FromInt(10) && reference.Landed > 12, "Setup: the bus is thrown and lands at 30 Hz (apex " + reference.Apex + ", landed in frame " + reference.Landed + ").");
            Check.That(other.Landed == reference.Landed, "The bus lands in the same frame at " + rate + ": " + other.Landed + " instead of " + reference.Landed + ".");
            // The throw and the gravity come from the catalog of the rate (per tick, per tick²) and are read back per frame: the last bits of the two values
            // differ between the rates, the heights by less than a thousandth of a unit.
            var tolerance = Fixed.FromDecimal(0.001m).Raw;
            Check.That((other.Apex - reference.Apex).Abs().Raw <= tolerance, "The apex of the throw is the same at " + rate + ": " + other.Apex + " instead of " + reference.Apex + ".");
            Check.That(reference.Heights.Count == other.Heights.Count, "As many frames at " + rate + ".");
            for (var frame = 0; frame < reference.Heights.Count; frame++)
                Check.That(Math.Abs(reference.Heights[frame] - other.Heights[frame]) <= tolerance,
                    "Height of the thrown bus at " + rate + ", frame " + frame + ": " + Fixed.FromRaw(other.Heights[frame]) + " instead of " + Fixed.FromRaw(reference.Heights[frame]) + ".");
            Check.That(other.Rising, "On the way up every tick lifts the bus at " + rate + ".");
        });
    }

    /// <summary>A world on the fixture and the terrain of the structure tests (the track RailA … RailD with a station, a looping track, the wave's path) at <paramref name="rate"/>.</summary>
    private static (SimulationWorld World, SyntheticFixture Fixture, CnCNew.Simulation.Terrain.TerrainMap Terrain) RatedStructuresWorld(SimulationRate rate, bool lake = false)
    {
        var fixture = new SyntheticFixture(rate, files: StructuresFiles());
        var terrain = StructuresTerrain(lake);
        return (new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, StructuresPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain), fixture, terrain);
    }

    private static void RequestSpawn(SimulationWorld world, SyntheticFixture fixture, string template, int owner, int x, int y)
    {
        Check.That(fixture.Templates.TryGet(template, out var info) && info.IsInstantiable, template + " is instantiable.");
        world.RequestSpawn(info.Index, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero);
    }

    /// <summary>
    /// <c>RailroadBehavior</c>: the conductor's recursion (push, acceleration factor, braking factor, friction), the wait at the station, the crush damage
    /// "speed · 10" and the shove by a quarter of the overlap happen once per frame of the original; the train moves every tick. Positions of the locomotive
    /// and its carriages, the conductor's state, the position and health of a vehicle it grinds along and every death are those of 30 Hz at each frame boundary.
    /// </summary>
    public static void RateInvarianceTrain()
    {
        AtThreeRates(rate =>
        {
            var (world, fixture, terrain) = RatedStructuresWorld(rate);
            RequestSpawn(world, fixture, "SwHeavy", 2, 95, 104);
            RequestSpawn(world, fixture, "SwTrooper", 2, 300, 100);
            RequestSpawn(world, fixture, "SwLoco", 0, 50, 100);
            Entity[] Of(string name) => world.Entities.Where(entity => entity.Template.Name == name).OrderBy(entity => entity.Id.Value).ToArray();
            var waited = 0;
            var topSpeed = Fixed.Zero;
            var series = FrameSeries(world, 900, () =>
            {
                long mixed = 0;
                foreach (var name in new[] { "SwLoco", "SwCar", "SwHeavy", "SwTrooper" })
                    foreach (var entity in Of(name))
                        // A carriage that waits in the wings or has left the end of the line is out of the world (it collides with nothing); where it is kept
                        // meanwhile depends on how often it was pulled, not on the game.
                        if (entity.Modules.OfType<RailroadBehavior>().FirstOrDefault() is not ({ IsWaitingInWings: true } or { IsEndOfLine: true }))
                            mixed = unchecked(mixed * 1_000_003 + entity.X.Raw * 31 + entity.Y.Raw * 7 + (entity.Body?.Health.Raw ?? 0) + (entity.EffectivelyDead ? 1 : 0));
                if (Of("SwLoco").FirstOrDefault() is { } loco)
                {
                    var rail = loco.Modules.OfType<RailroadBehavior>().Single();
                    mixed = unchecked(mixed * 31 + (long)rail.Conductor);
                    if (rail.Conductor == RailroadBehavior.ConductorStates.WaitAtStation) waited++;
                    topSpeed = Fixed.Max(topSpeed, rail.Speed * world.Time.TicksPerOriginalFrame);
                }
                return mixed;
            }, item => item.Kind is SimEventKind.EntityDied or SimEventKind.EntityDamaged or SimEventKind.EntityDestroyed);
            return (Series: series, Waited: waited, TopSpeed: topSpeed);
        }, (reference, other, rate) =>
        {
            Check.That(reference.Waited is > 25 and < 40 && reference.TopSpeed > Fixed.FromInt(2), "Setup: the train reached its speed and waited at the station at 30 Hz (" + reference.Waited + " frames, top speed " + reference.TopSpeed + ").");
            Check.That(other.Waited == reference.Waited, "The train waits as many frames at the station at " + rate + ": " + other.Waited + " instead of " + reference.Waited + ".");
            Check.That((other.TopSpeed - reference.TopSpeed).Abs() <= Fixed.FromRaw(4), "The top speed per frame of the original is that of 30 Hz at " + rate + ".");
            SameSeries(reference.Series, other.Series, "Train, carriages, victims and events at " + rate);
        });

        // A save taken inside a frame of the train (only above 30 Hz there is such a tick) carries the phase of the frame and runs on identically.
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, fixture, terrain) = RatedStructuresWorld(rate);
            RequestSpawn(world, fixture, "SwHeavy", 2, 95, 104);
            RequestSpawn(world, fixture, "SwLoco", 0, 50, 100);
            for (var tick = 0; tick < world.Frames(40) + 1; tick++) world.Step();
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
            Check.That(restored.StateHash == world.StateHash, "A train saved inside a frame restores to the same state (" + rate.TicksPerSecond + " Hz).");
            for (var tick = 0; tick < world.Frames(300); tick++)
                Check.That(world.Step().StateHash == restored.Step().StateHash, "The restored train runs on identically at " + rate.TicksPerSecond + " Hz, step " + tick + ".");
        }
    }
}
