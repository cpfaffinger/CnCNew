using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
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
    /// <c>StructureCollapseUpdate</c>: height -= velocity and velocity -= gravity once per frame of the original, the visual height moves every tick. The
    /// height at every frame boundary (within the last bits of the catalog's gravity), the frames of the bursts and of the end are those of 30 Hz.
    /// </summary>
    public static void RateInvarianceStructureCollapse()
    {
        AtThreeRates(rate =>
        {
            var (world, fixture, _) = RatedStructuresWorld(rate);
            RequestSpawn(world, fixture, "SwBuilding", 0, 300, 200);
            world.Step();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var building = world.Entities.Single(entity => entity.Template.Name == "SwBuilding");
            var collapse = building.Modules.OfType<StructureCollapseUpdate>().Single();
            world.Kill(building.Id);
            var (done, frame, sinking) = (-1, 0, true);
            var previous = Fixed.Zero;
            var heights = new List<long>();
            var events = FrameSeries(world, 200, () =>
            {
                heights.Add(collapse.VisualHeight.Raw);
                if (done < 0 && collapse.IsCollapsed) done = frame;
                frame++;
                return 0;
            }, item => item.Kind is SimEventKind.FxRequested or SimEventKind.EntitySpawned);
            // A second run, tick by tick: once the building sinks, every tick lowers it.
            var (again, againFixture, _) = RatedStructuresWorld(rate);
            RequestSpawn(again, againFixture, "SwBuilding", 0, 300, 200);
            again.Step();
            var other = again.Entities.Single(entity => entity.Template.Name == "SwBuilding");
            var sinks = other.Modules.OfType<StructureCollapseUpdate>().Single();
            again.Kill(other.Id);
            for (var tick = 0; tick < again.Frames(200) && !sinks.IsCollapsed; tick++)
            {
                again.Step();
                if (previous < Fixed.FromInt(-1) && sinks.VisualHeight >= previous) sinking = false;
                previous = sinks.VisualHeight;
            }
            return (Heights: heights, Events: events, Done: done, Sinking: sinking);
        }, (reference, other, rate) =>
        {
            Check.That(reference.Done > 15, "Setup: the building collapses at 30 Hz (done in frame " + reference.Done + ").");
            Check.That(other.Done == reference.Done, "The collapse ends in the same frame at " + rate + ": " + other.Done + " instead of " + reference.Done + ".");
            SameSeries(reference.Events, other.Events, "Effects and debris of the collapse at " + rate);
            var tolerance = Fixed.FromDecimal(0.001m).Raw;
            for (var frame = 0; frame < reference.Heights.Count; frame++)
                Check.That(Math.Abs(reference.Heights[frame] - other.Heights[frame]) <= tolerance,
                    "Visual height at " + rate + ", frame " + frame + ": " + Fixed.FromRaw(other.Heights[frame]) + " instead of " + Fixed.FromRaw(reference.Heights[frame]) + ".");
            Check.That(other.Sinking, "Once it sinks, every tick lowers the building at " + rate + ".");
        });
    }

    /// <summary>
    /// <c>BridgeScaffoldBehavior</c>: the speed follows the way left once per frame of the original (a geometric approach over the last quarter), the
    /// scaffold moves every tick. Its position at every frame boundary and the frames in which it changes its motion are those of 30 Hz, through the rise,
    /// the building across, a reversal in mid-motion, the tearing down and the sinking.
    /// </summary>
    public static void RateInvarianceBridgeScaffold()
    {
        AtThreeRates(rate =>
        {
            var (world, fixture, terrain) = RatedStructuresWorld(rate);
            RequestSpawn(world, fixture, "SwScaffold", 0, 100, 100);
            world.Step();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var scaffold = world.Entities.Single(entity => entity.Template.Name == "SwScaffold");
            var module = scaffold.Modules.OfType<BridgeScaffoldBehavior>().Single();
            module.SetPositions((Fixed.FromInt(100), Fixed.FromInt(100), Fixed.Zero), (Fixed.FromInt(100), Fixed.FromInt(100), Fixed.FromInt(30)), (Fixed.FromInt(160), Fixed.FromInt(100), Fixed.FromInt(30)));
            // 4 and 3 units per frame of the original; the module takes its speeds per tick.
            module.SetSpeeds(world.PerFrame(Fixed.FromInt(4)), world.PerFrame(Fixed.FromInt(3)));
            module.SetMotion(world, BridgeScaffoldBehavior.Motion.Rise);
            var series = new List<long>();
            var perFrame = world.Time.TicksPerOriginalFrame;
            var onTheWay = true;
            for (var frame = 0; frame < 400 && world.FindEntity(scaffold.Id) is not null; frame++)
            {
                var before = scaffold.Z;
                for (var tick = 0; tick < (frame == 0 ? 1 : perFrame); tick++)
                {
                    // The reversal comes from outside (a script) between two ticks: before the tick that is the same game time at every rate.
                    if (frame == 120 && tick == perFrame - 1) module.ReverseMotion(world);
                    world.Step();
                    // While it rises at full speed every tick lifts it.
                    if (frame is > 0 and < 5 && tick < perFrame - 1 && scaffold.Z <= before) onTheWay = false;
                    if (frame == 20 && tick == 0 && perFrame > 1)
                    {
                        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
                        Check.That(restored.StateHash == world.StateHash, "A scaffold saved inside a frame of its motion round-trips (" + rate.TicksPerSecond + " Hz).");
                    }
                }
                series.Add(scaffold.X.Raw);
                series.Add(scaffold.Z.Raw);
                series.Add((long)module.CurrentMotion);
            }
            return (Series: series, Gone: world.FindEntity(scaffold.Id) is null, OnTheWay: onTheWay);
        }, (reference, other, rate) =>
        {
            Check.That(reference.Gone && reference.Series.Count > 3 * 150, "Setup: the scaffold rose, built across, was reversed, torn down and sank at 30 Hz (" + reference.Series.Count / 3 + " frames).");
            SameSeries(reference.Series, other.Series, "Scaffold position and motion at " + rate);
            Check.That(other.OnTheWay, "While it rises every tick lifts the scaffold at " + rate + ".");
        });
    }

    /// <summary>
    /// <c>RailedTransportDockUpdate</c> (the ferry's dock): a docker is pulled in until it is within 6 units, a rider is pushed out until it is within 3 units
    /// of the exit. Both move every tick; whether they have arrived is asked once per frame of the original, so the passenger is inside and the rider is free
    /// in the same frame as at 30 Hz, and they are where they are at 30 Hz at every frame boundary (within the rounding of the share per tick).
    /// The dock is internal to the simulation, so its action is called the way the docker's machine calls it.
    /// </summary>
    public static void RateInvarianceFerryDock()
    {
        AtThreeRates(rate =>
        {
            var terrain = FerryTerrain();
            var fixture = new SyntheticFixture(rate, files: LastBlockerFiles());
            System.Collections.Immutable.ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            var ferry = CsSpawn(world, feeder, "LbFerry", 1, 390, 110);
            var ai = ferry.Modules.OfType<RailedTransportAIUpdate>().Single();
            for (var tick = 0; tick < world.Frames(80) && (ai.InTransit || tick < 2); tick++) world.Step();
            Check.That(!ai.InTransit && ai.DockIsOpen, "Setup: the ferry lies at the end of its path with its dock open.");
            // Let it come to rest: the pull is aimed at the ferry.
            var rest = ferry.X - Fixed.One;
            for (var tick = 0; tick < world.Frames(200) && (rest != ferry.X || tick % world.Frames(10) != 0); tick++)
            {
                if (tick % world.Frames(10) == 0) rest = ferry.X;
                world.Step();
            }
            Check.That(rest == ferry.X && !ferry.HasMoveTarget, "Setup: the ferry has come to rest.");
            // The passenger appears at the same place relative to the ferry at every rate (where the ferry stops is the business of its drive).
            Check.That(fixture.Templates.TryGet("LbPassenger", out var passengerTemplate), "Setup: the passenger template exists.");
            var passengerId = world.RequestSpawn(passengerTemplate.Index, new PlayerId(1), ferry.X - Fixed.FromInt(37), ferry.Y + Fixed.FromInt(11), Fixed.Zero, Fixed.Zero);
            world.Step();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var passenger = world.FindEntity(passengerId) ?? throw new InvalidOperationException("No passenger.");
            var dock = ferry.Modules.Single(module => module.ModuleName == "RailedTransportDockUpdate");
            dock.GetType().GetMethod("Action")!.Invoke(dock, [world, passenger]);
            Check.That(ai.DockIsLoadingOrUnloading, "Setup: the dock pulls the passenger in.");
            // The dock acts in the very tick of the request, and so does the exit order: a frame of the pull or push is the perFrame ticks from there (at 30 Hz
            // the one tick).
            var perFrame = world.Time.TicksPerOriginalFrame;
            var places = new List<long>();
            var (inside, free, frame) = (-1, -1, 0);
            var (insideTick, freeTick, ticks) = (-1, -1, 0);
            for (; frame < 100 && inside < 0; frame++)
            {
                for (var tick = 0; tick < perFrame; tick++)
                {
                    world.Step();
                    ticks++;
                    if (insideTick < 0 && passenger.ContainedBy == ferry.Id) insideTick = ticks;
                }
                if (passenger.ContainedBy == ferry.Id) inside = frame;
                else
                {
                    places.Add((passenger.X - ferry.X).Raw);
                    places.Add((passenger.Y - ferry.Y).Raw);
                }
            }
            Check.That(inside >= 0, "Setup: the passenger got inside (" + rate.TicksPerSecond + " Hz).");
            Check.That(feeder.ExitContainer(1, passenger.Id.Value).Accepted, "Setup: the exit order is accepted.");
            ticks = 0;
            for (frame = 0; frame < 100 && free < 0; frame++)
            {
                for (var tick = 0; tick < perFrame; tick++)
                {
                    world.Step();
                    ticks++;
                    if (freeTick < 0 && !passenger.ContainedBy.IsValid && !ai.DockIsLoadingOrUnloading) freeTick = ticks;
                }
                if (!passenger.ContainedBy.IsValid && !ai.DockIsLoadingOrUnloading) free = frame;
                else if (!passenger.ContainedBy.IsValid)
                {
                    places.Add((passenger.X - ferry.X).Raw);
                    places.Add((passenger.Y - ferry.Y).Raw);
                }
            }
            return (Places: places, Inside: inside, Free: free, InsideFrames: insideTick * 30 / rate.TicksPerSecond, FreeFrames: freeTick * 30 / rate.TicksPerSecond, Whole: insideTick % perFrame == 0 && freeTick % perFrame == 0);
        }, (reference, other, rate) =>
        {
            Check.That(reference.Inside > 10 && reference.Free > 10, "Setup: pull-in and push-out take a while at 30 Hz (" + reference.Inside + " and " + reference.Free + " frames).");
            Check.That(other.Inside == reference.Inside, "The passenger is inside in the same frame at " + rate + ": " + other.Inside + " instead of " + reference.Inside + ".");
            Check.That(other.Whole && other.InsideFrames == reference.InsideFrames && other.FreeFrames == reference.FreeFrames,
                "Arrival comes with the last tick of the same frame at " + rate + ": inside after " + other.InsideFrames + " frames, free after " + other.FreeFrames + " (30 Hz: " + reference.InsideFrames + " and " + reference.FreeFrames + ").");
            Check.That(other.Free == reference.Free, "The rider is free in the same frame at " + rate + ": " + other.Free + " instead of " + reference.Free + ".");
            Check.That(other.Places.Count == reference.Places.Count, "As many frames on the way at " + rate + ".");
            var tolerance = Fixed.FromDecimal(0.001m).Raw;
            for (var index = 0; index < reference.Places.Count; index++)
                Check.That(Math.Abs(reference.Places[index] - other.Places[index]) <= tolerance,
                    "Place of the passenger at " + rate + ", value " + index + ": " + Fixed.FromRaw(other.Places[index]) + " instead of " + Fixed.FromRaw(reference.Places[index]) + ".");
        });
    }

    /// <summary>
    /// <c>WaveGuideUpdate</c> (the flood of a broken dam): the wave moves every tick, its front wets and hurts what it reaches once per frame of the original.
    /// The wave is where it is at 30 Hz at every frame boundary (within the rounding of its step per tick), every object in its way is hurt in the same
    /// frame, and the wave is destroyed at the end of its path in the same frame.
    /// </summary>
    public static void RateInvarianceWaveGuide()
    {
        AtThreeRates(rate =>
        {
            var (world, fixture, _) = RatedStructuresWorld(rate);
            RequestSpawn(world, fixture, "SwDam", 0, 50, 50);
            RequestSpawn(world, fixture, "SwWave", 0, 10, 10);
            // Objects along and beside the wave's path (WaveGuide1 at 100,250 to WaveGuide2 at 500,250).
            foreach (var (x, y) in new[] { (150, 250), (200, 250), (263, 262), (331, 241), (397, 255), (440, 249) }) RequestSpawn(world, fixture, "SwWalker", 1, x, y);
            for (var tick = 0; tick < world.Frames(10); tick++) world.Step();
            var wave = world.Entities.Single(entity => entity.Template.Name == "SwWave");
            var dam = world.Entities.Single(entity => entity.Template.Name == "SwDam");
            var walkers = world.Entities.Where(entity => entity.Template.Name == "SwWalker").OrderBy(entity => entity.Id.Value).ToArray();
            world.Kill(dam.Id);
            var places = new List<long>();
            var (gone, frame) = (-1, 0);
            var events = FrameSeries(world, 500, () =>
            {
                var live = world.FindEntity(wave.Id);
                if (gone < 0 && (live is null || live.DestroyRequested)) gone = frame;
                places.Add(live is null ? 0 : live.X.Raw);
                places.Add(live is null ? 0 : live.Y.Raw);
                frame++;
                long mixed = 0;
                foreach (var walker in walkers) mixed = unchecked(mixed * 31 + (walker.Body?.Health.Raw ?? 0) + (walker.EffectivelyDead ? 1 : 0) + (walker.HasStatus(ObjectStatusBits.Index("WET")) ? 2 : 0));
                return mixed;
            }, item => item.Kind is SimEventKind.EntityDamaged or SimEventKind.EntityDied or SimEventKind.EntityDestroyed);
            return (Events: events, Places: places, Gone: gone, Wet: walkers.Count(walker => walker.HasStatus(ObjectStatusBits.Index("WET"))));
        }, (reference, other, rate) =>
        {
            Check.That(reference.Gone > 50 && reference.Wet >= 4, "Setup: the wave ran its path and flooded what was in its way at 30 Hz (gone in frame " + reference.Gone + ", " + reference.Wet + " wet).");
            Check.That(other.Gone == reference.Gone, "The wave is destroyed in the same frame at " + rate + ": " + other.Gone + " instead of " + reference.Gone + ".");
            SameSeries(reference.Events, other.Events, "What the wave wets, hurts and kills at " + rate);
            var tolerance = Fixed.FromDecimal(0.001m).Raw;
            for (var index = 0; index < reference.Places.Count; index++)
                Check.That(Math.Abs(reference.Places[index] - other.Places[index]) <= tolerance,
                    "Place of the wave at " + rate + ", value " + index + ": " + Fixed.FromRaw(other.Places[index]) + " instead of " + Fixed.FromRaw(reference.Places[index]) + ".");
        });
    }

    /// <summary>
    /// <c>DynamicShroudClearingRangeUpdate</c>: the clearing range grows and shrinks by the original's amount per frame, spread over the ticks of the frame.
    /// The range handed to the vision map is bit-equal to 30 Hz at every frame boundary of its timeline (grow 1 s, hold, shrink 1 s to the final range).
    /// </summary>
    public static void RateInvarianceShroudRange()
    {
        AtThreeRates(rate =>
        {
            var (world, fixture, _) = RatedStructuresWorld(rate);
            RequestSpawn(world, fixture, "SwReveal", 1, 500, 300);
            CnCNew.Simulation.Modules.BuiltIn.DynamicShroudClearingRangeUpdate? module = null;
            var (widest, last) = (Fixed.Zero, Fixed.Zero);
            var series = FrameSeries(world, 130, () =>
            {
                module ??= world.Entities.Single(entity => entity.Template.Name == "SwReveal").Modules.OfType<DynamicShroudClearingRangeUpdate>().Single();
                if (!module.TryGetShroudRange(out var range)) return -1;
                widest = Fixed.Max(widest, range);
                last = range;
                return range.Raw;
            });
            return (Series: series, Widest: widest, Last: last);
        }, (reference, other, rate) =>
        {
            Check.That(reference.Widest == Fixed.FromInt(100) && reference.Last == Fixed.FromInt(20), "Setup: the range grew to 100 and shrank to 20 at 30 Hz (" + reference.Widest + ", " + reference.Last + ").");
            SameSeries(reference.Series, other.Series, "Shroud clearing range at " + rate);
        });
    }

    /// <summary>
    /// Healing in slices (<c>HealContain</c>, <c>GarrisonContain</c>, <c>RepairDockUpdate</c>): the slice of the original (the amount over the duration in
    /// frames) is given once per frame of the original. The health of the healed object is bit-equal to 30 Hz at every frame boundary, it is healed in as
    /// many frames, and it is full (and leaves the healing building) in the same frame.
    /// </summary>
    public static void RateInvarianceHealingSlices()
    {
        foreach (var (files, building, patient) in new[] { (RetailModuleFiles(), "Barracks", "Rebel"), (Phase5Files(), "Bunker", "Rebel") })
            AtThreeRates(rate =>
            {
                var (world, feeder, _) = RatedWorld(rate, files);
                feeder.Spawn(1, building);
                feeder.Spawn(1, patient, x: 3, y: 3);
                world.Step();
                var house = world.Entities.First(entity => entity.Template.Name == building);
                var rebel = world.Entities.First(entity => entity.Template.Name == patient);
                feeder.Damage(rebel.Id.Value, 30);
                world.Step();
                while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
                Check.That(world.TryEnterContainer(rebel, house), "Setup: the hurt " + patient + " enters the " + building + ".");
                var (healed, before, full) = (0, rebel.Body!.Health, -1);
                var frame = 0;
                var series = FrameSeries(world, 30, () =>
                {
                    if (rebel.Body.Health > before) healed++;
                    before = rebel.Body.Health;
                    if (full < 0 && rebel.Body.Health == rebel.Body.MaxHealth) full = frame;
                    frame++;
                    return rebel.Body.Health.Raw * 2 + (rebel.ContainedBy.IsValid ? 1 : 0);
                });
                return (Series: series, Healed: healed, Full: full);
            }, (reference, other, rate) =>
            {
                Check.That(reference.Full > 3 && reference.Healed > 3, "Setup: the " + building + " heals its occupant over several frames at 30 Hz (full in frame " + reference.Full + ", " + reference.Healed + " frames with healing).");
                Check.That(other.Healed == reference.Healed && other.Full == reference.Full,
                    building + " at " + rate + ": " + other.Healed + " frames with healing, full in frame " + other.Full + " (30 Hz: " + reference.Healed + ", " + reference.Full + ").");
                SameSeries(reference.Series, other.Series, "Health of the occupant of the " + building + " at " + rate);
            });

        // The repair dock: its action is called the way a docked object's machine calls it, once per tick, until it says the docker is done.
        AtThreeRates(rate =>
        {
            var (world, feeder, _) = RatedWorld(rate, RepairFiles());
            feeder.Spawn(1, "RepairPad", x: 100);
            feeder.Spawn(1, "Truck", x: 100, y: 60);
            world.Step();
            var pad = world.Entities.First(entity => entity.Template.Name == "RepairPad");
            var truck = world.Entities.First(entity => entity.Template.Name == "Truck");
            feeder.Damage(truck.Id.Value, 61);
            world.Step();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            var dock = pad.Modules.Single(module => module.ModuleName == "RepairDockUpdate");
            var action = dock.GetType().GetMethod("Action")!;
            var perFrame = world.Time.TicksPerOriginalFrame;
            var series = new List<long>();
            var (healed, done, docked) = (0, -1, true);
            for (var frame = 0; frame < 30; frame++)
            {
                var before = truck.Body!.Health;
                for (var tick = 0; tick < (frame == 0 ? 1 : perFrame); tick++)
                {
                    if (docked && !(bool)action.Invoke(dock, [world, truck])!)
                    {
                        docked = false;
                        done = frame;
                    }
                    world.Step();
                }
                if (truck.Body.Health > before) healed++;
                series.Add(truck.Body.Health.Raw);
            }
            return (Series: series, Healed: healed, Done: done);
        }, (reference, other, rate) =>
        {
            Check.That(reference.Healed == 9 && reference.Done == 9, "Setup: the dock heals in nine frames at 30 Hz and lets go in the tenth (" + reference.Healed + ", " + reference.Done + ").");
            Check.That(other.Healed == reference.Healed && other.Done == reference.Done,
                "Repair dock at " + rate + ": " + other.Healed + " frames with healing, done in frame " + other.Done + " (30 Hz: " + reference.Healed + ", " + reference.Done + ").");
            SameSeries(reference.Series, other.Series, "Health of the docked truck at " + rate);
        });
    }

    /// <summary>
    /// <c>HeightDieUpdate</c>: the height is looked at once per frame of the original. An object below its target height dies with the last tick of the
    /// same frame as at 30 Hz - at once (on a plateau above it), after its initial delay, and when a tall structure appears beside it.
    /// </summary>
    public static void RateInvarianceHeightDie()
    {
        AtThreeRates(rate =>
        {
            long DeathTick(string template, CnCNew.Simulation.Terrain.TerrainMap terrain, bool hider)
            {
                var (world, feeder, _) = RatedWorld(rate, CollisionFiles(), terrain);
                feeder.Spawn(1, template, x: 0, y: 0);
                for (var tick = 1; tick <= world.Frames(40); tick++)
                {
                    // The tall structure appears 20 frames later, between two ticks that are the same game time at every rate.
                    if (hider && tick == world.Frames(20) + 1) feeder.Spawn(2, "TallHider", x: 1, y: 0);
                    if (world.Step().Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity.Value == 1)) return tick - 1;
                }
                return -1;
            }
            var ticks = new[] { DeathTick("GroundedFaller", CollisionTerrain(height: (_, _) => 40), false), DeathTick("DelayedFaller", CollisionTerrain(), false), DeathTick("Faller", CollisionTerrain(), true) };
            return (Frames: ticks.Select(tick => tick < 0 ? -1 : tick * 30 / rate.TicksPerSecond).ToArray(), Whole: ticks.All(tick => tick % rate.TicksPerOriginalFrame == 0));
        }, (reference, other, rate) =>
        {
            Check.That(reference.Frames.All(frame => frame > 0), "Setup: the three objects die at 30 Hz (" + string.Join(", ", reference.Frames) + ").");
            Check.That(other.Whole && other.Frames.SequenceEqual(reference.Frames),
                "HeightDieUpdate kills with the last tick of the same frame at " + rate + ": frames " + string.Join(", ", other.Frames) + " (30 Hz: " + string.Join(", ", reference.Frames) + ").");
        });
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
