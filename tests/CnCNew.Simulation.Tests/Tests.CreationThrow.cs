using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-66, the throw of a created object (<c>ObjectCreationList.cpp</c>, <c>doStuffToObj</c>): an object with a physics module that a creation list makes with <c>SEND_IT_FLYING</c> gets
/// the drawn spin rates and an upward force and flies; with <c>SEND_IT_OUT</c> it is pushed along the ground.
/// </summary>
static partial class Tests
{
    private const string CreationThrowIni = """
        Object CtCrate
          KindOf = SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 1.0
          End
        End
        Object CtSource
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object CtWreck
          KindOf = SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 1.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 5000
          End
        End
        Locomotor CtLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 30.0
          Acceleration = 30.0
          Braking = 30.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object CtTrooper
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL CtLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 1.0
          End
        End
        ObjectCreationList OCL_CtTrooperOut
          CreateObject
            ObjectNames = CtTrooper
            Disposition = SEND_IT_OUT
            DispositionIntensity = 2.0
          End
        End
        ObjectCreationList OCL_CtWreckOut
          CreateObject
            ObjectNames = CtWreck
            Disposition = SEND_IT_OUT
            DispositionIntensity = 2.0
          End
        End
        ObjectCreationList OCL_CtFlying
          CreateObject
            ObjectNames = CtCrate
            Disposition = SEND_IT_FLYING
            DispositionIntensity = 2.0
          End
        End
        ObjectCreationList OCL_CtOut
          CreateObject
            ObjectNames = CtCrate
            Disposition = SEND_IT_OUT
            DispositionIntensity = 2.0
          End
        End
        ObjectCreationList OCL_CtOutRough
          CreateObject
            ObjectNames = CtCrate
            Disposition = SEND_IT_OUT
            DispositionIntensity = 2.0
            ExtraFriction = 15.0
          End
        End
        ObjectCreationList OCL_CtInherit
          CreateObject
            ObjectNames = CtCrate
            Disposition = LIKE_EXISTING INHERIT_VELOCITY
          End
        End
        ObjectCreationList OCL_CtLike
          CreateObject
            ObjectNames = CtCrate
            Disposition = LIKE_EXISTING
          End
        End
        """;

    /// <summary>
    /// <c>SEND_IT_FLYING</c>: the crate rises (the force is upward by a third of up to three times the intensity), falls back, lands and comes to rest away from where it was made;
    /// <c>SEND_IT_OUT</c>: it slides along the ground and stops. The same resting place at 30, 60 and 120 Hz, and a snapshot in the flight continues identically. Before, both stood
    /// where they were made.
    /// </summary>
    public static void CreationListThrowsWhatItCreates()
    {
        (long X, long Y, Fixed Highest) Run(SimulationRate rate, string list, bool save)
        {
            var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(CreationThrowIni));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            var source = GdSpawn(world, feeder, "CtSource", 1, 100, 100);
            world.CreateFromList(source, list, EntityId.Invalid, false);
            world.Step();
            var crate = world.Entities.Single(value => value.Template.Name == "CtCrate");
            var highest = crate.Z;
            for (var step = 0; step < world.Frames(300); step++)
            {
                if (save && step == world.Frames(5))
                {
                    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    for (var follow = 0; follow < restored.Frames(30); follow++)
                        Check.That(restored.Step().StateHash == world.Step().StateHash, $"{rate}: the restored world follows the flight (step {follow}).");
                }
                world.Step();
                highest = Fixed.Max(highest, crate.Z);
            }
            var physics = crate.Modules.OfType<PhysicsBehavior>().Single();
            Check.That(!physics.Flying && crate.Z == Fixed.Zero, $"{rate} {list}: the crate rests on the ground ({crate.Z}).");
            Check.That(crate.X != Fixed.FromInt(100) || crate.Y != Fixed.FromInt(100), $"{rate} {list}: it rests away from where it was made ({crate.X}, {crate.Y}).");
            return (crate.X.Raw, crate.Y.Raw, highest);
        }

        var flying = Run(SimulationRate.Hz30, "OCL_CtFlying", false);
        Check.That(flying.Highest > Fixed.FromInt(1), $"SEND_IT_FLYING: the crate flew ({flying.Highest}).");
        var outward = Run(SimulationRate.Hz30, "OCL_CtOut", false);
        Check.That(outward.Highest == Fixed.Zero, $"SEND_IT_OUT: the crate stays on the ground ({outward.Highest}).");
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate, "OCL_CtFlying", false);
            Check.That((other.X, other.Y) == (flying.X, flying.Y), $"{rate}: the crate rests where it rests at 30 Hz.");
            var outOther = Run(rate, "OCL_CtOut", false);
            Check.That((outOther.X, outOther.Y) == (outward.X, outward.Y), $"{rate}: the pushed crate rests where it rests at 30 Hz.");
        }
        Run(TestRate.Default, "OCL_CtFlying", true);
    }

    /// <summary>
    /// A thrown object that dies inside the step of a frame (above 30 Hz) is put where the frame ends, as at 30 Hz, and its flight adds nothing more of that frame: before, the
    /// flight went on covering the shares of the frame it had already completed, and the object stood a share beyond the frame's end (a ranger pushed out of its dying barracks
    /// that stopped to attack walked on from the wrong place at 60 Hz, and a whole match of two computer players went another way than at 30 Hz).
    /// </summary>
    public static void ThrownObjectThatDiesInsideAFrameStopsAtItsEnd()
    {
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(CreationThrowIni));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            var source = GdSpawn(world, feeder, "CtSource", 1, 100, 100);
            world.CreateFromList(source, "OCL_CtWreckOut", EntityId.Invalid, false);
            world.Step();
            var wreck = world.Entities.Single(value => value.Template.Name == "CtWreck");
            var frame = world.Time.TicksPerOriginalFrame;
            // Two whole frames of the slide, then the first tick of the third: the rest of that frame's step is still to be covered.
            while ((world.Tick - wreck.SpawnTick) % frame != 0 || world.Tick - wreck.SpawnTick < 2 * frame) world.Step();
            world.Step();
            Check.That(wreck.IsInsideFrameStep, $"{rate}: the wreck slides inside the step of its frame.");
            var end = wreck.FrameEnd(world.Tick);
            Check.That(feeder.Kill(wreck.Id.Value).Accepted, $"{rate}: the kill is accepted.");
            while ((world.Tick - wreck.SpawnTick) % frame != 0) world.Step();
            Check.That(wreck.Body!.Health <= Fixed.Zero, $"{rate}: the wreck died.");
            Check.That((wreck.X, wreck.Y) == (end.X, end.Y), $"{rate}: it stands where the frame it died in ends ({wreck.X}, {wreck.Y} against {end.X}, {end.Y}).");
        }
    }

    /// <summary>
    /// F-74: a soldier pushed out by a creation list, with an order to walk, walks off when it rests - in the same frame and along the same places at 30, 60 and 120 Hz. Before, above
    /// 30 Hz the flight ended in a frame's first tick and the soldier's movement, which had held still for the flight, decided its first step in the frame's later ticks.
    /// </summary>
    public static void PushedSoldierWalksOffInTheFrameAfterItRests()
    {
        string? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(CreationThrowIni));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (_, _) => 0), "pushed");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            var source = GdSpawn(world, feeder, "CtSource", 1, 300, 300);
            world.CreateFromList(source, "OCL_CtTrooperOut", EntityId.Invalid, false);
            world.Step();
            var trooper = world.Entities.Single(value => value.Template.Name == "CtTrooper");
            Check.That(trooper.InPhysicsFlight, $"{rate}: the soldier slides.");
            Check.That(feeder.SetMoveTarget(1, trooper.Id.Value, 500, 300).Accepted, $"{rate}: the soldier is told to walk.");
            // The soldier's place at the end of each of its frames, by the number of the frame since it appeared.
            var frames = new SortedDictionary<long, string>();
            for (var step = 0; step < world.Frames(300); step++)
            {
                world.Step();
                if ((world.Tick - trooper.SpawnTick) % world.Time.TicksPerOriginalFrame == 0)
                    frames[(world.Tick - trooper.SpawnTick) / world.Time.TicksPerOriginalFrame] = $"{trooper.X.Raw},{trooper.Y.Raw},{(trooper.InPhysicsFlight ? 1 : 0)}";
            }
            Check.That(trooper.X > Fixed.FromInt(450), $"{rate}: the soldier walked off ({trooper.X}).");
            var line = string.Join(";", frames.Where(pair => pair.Key >= 2 && pair.Key <= 280).Select(pair => $"{pair.Key}:{pair.Value}"));
            if (reference is null) reference = line;
            else
            {
                var a = line.Split(';');
                var b = reference.Split(';');
                var first = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(index => a[index] != b[index], -1);
                Check.That(first < 0 && a.Length == b.Length, $"{rate}: the soldier is where 30 Hz has it at the end of every frame (first difference: {(first < 0 ? "-" : a[first] + " against " + b[first])}).");
            }
        }
    }

    /// <summary>
    /// The rest of F-66 (<c>doStuffToObj</c>): <c>ExtraFriction</c> is added to the frictions of the pushed crate (<c>setExtraFriction</c>), so the same push slides it a shorter way;
    /// <c>INHERIT_VELOCITY</c> gives a crate made by a flying crate the source's velocity as a force, so it flies on and rests away from where it was made. The same resting places at
    /// 30, 60 and 120 Hz. Before, the extra friction was not read and the inherited velocity not applied: the rough crate slid as far as the smooth one, and the made crate stood.
    /// </summary>
    public static void CreationListFrictionAndInheritedVelocity()
    {
        (SimulationWorld World, Feeder Feeder) Make(SimulationRate rate)
        {
            var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(CreationThrowIni));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            return (world, new Feeder(world));
        }

        (long X, long Y) Slide(SimulationRate rate, string list)
        {
            var (world, feeder) = Make(rate);
            var source = GdSpawn(world, feeder, "CtSource", 1, 100, 100);
            world.CreateFromList(source, list, EntityId.Invalid, false);
            for (var step = 0; step < world.Frames(300); step++) world.Step();
            var crate = world.Entities.Single(value => value.Template.Name == "CtCrate");
            Check.That(!crate.Modules.OfType<PhysicsBehavior>().Single().Flying, $"{rate} {list}: the crate rests.");
            return (crate.X.Raw - Fixed.FromInt(100).Raw, crate.Y.Raw - Fixed.FromInt(100).Raw);
        }

        (long X, long Y, long MadeX, long MadeY) Inherit(SimulationRate rate)
        {
            var (world, feeder) = Make(rate);
            var source = GdSpawn(world, feeder, "CtSource", 1, 100, 100);
            world.CreateFromList(source, "OCL_CtOut", EntityId.Invalid, false);
            world.Step();
            var flyer = world.Entities.Single(value => value.Template.Name == "CtCrate");
            // Three whole frames of the slide, then the crate makes another where its frame ends.
            while ((world.Tick - flyer.SpawnTick) % world.Time.TicksPerOriginalFrame != 0 || world.Tick - flyer.SpawnTick < 3 * world.Time.TicksPerOriginalFrame) world.Step();
            Check.That(flyer.InPhysicsFlight, $"{rate}: the first crate still slides.");
            var (madeX, madeY) = (flyer.X, flyer.Y);
            world.CreateFromList(flyer, "OCL_CtInherit", EntityId.Invalid, false);
            world.Step();
            var made = world.Entities.Where(value => value.Template.Name == "CtCrate").OrderBy(value => value.Id.Value).Last();
            for (var step = 0; step < world.Frames(300); step++) world.Step();
            Check.That(!made.Modules.OfType<PhysicsBehavior>().Single().Flying, $"{rate}: the made crate rests.");
            return (made.X.Raw, made.Y.Raw, madeX.Raw, madeY.Raw);
        }

        static long Length((long X, long Y) way) => Math.Abs(way.X) + Math.Abs(way.Y);
        var smooth = Slide(SimulationRate.Hz30, "OCL_CtOut");
        var rough = Slide(SimulationRate.Hz30, "OCL_CtOutRough");
        Check.That(Length(rough) > 0 && Length(rough) < Length(smooth), $"ExtraFriction: the rough crate slides a shorter way ({Length(rough)} against {Length(smooth)}).");
        var inherited = Inherit(SimulationRate.Hz30);
        Check.That((inherited.X, inherited.Y) != (inherited.MadeX, inherited.MadeY), "INHERIT_VELOCITY: the made crate flies on from where it was made.");
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            Check.That(Slide(rate, "OCL_CtOutRough") == rough, $"{rate}: the rough crate rests where it rests at 30 Hz.");
            Check.That(Inherit(rate) == inherited, $"{rate}: the made crate rests where it rests at 30 Hz.");
        }
    }

    /// <summary>
    /// F-77: a creation list takes its source's heading where the source's frame ends. A soldier turns round to walk off; inside the turn of a frame (above 30 Hz, after the frame's first
    /// tick) it makes a crate <c>LIKE_EXISTING</c>, whose heading is the one the soldier has at the end of that frame - the same at 30, 60 and 120 Hz. Before, above 30 Hz the crate took
    /// the soldier's heading inside the turn.
    /// </summary>
    public static void CreationListTakesTheSourceHeadingWhereItsFrameEnds()
    {
        long? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(CreationThrowIni));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (_, _) => 0), "turning");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            var trooper = GdSpawn(world, feeder, "CtTrooper", 1, 300, 300);
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            Check.That(feeder.SetMoveTarget(1, trooper.Id.Value, 200, 300).Accepted, $"{rate}: the soldier is told to walk off behind it.");
            // Five whole frames of the turn, then the first tick of the sixth: above 30 Hz the soldier is inside that frame's turn.
            for (var step = 0; step < world.Frames(5) + 1; step++) world.Step();
            Check.That(rate == SimulationRate.Hz30 || trooper.HasAheadAngle, $"{rate}: the soldier turns inside its frame.");
            world.CreateFromList(trooper, "OCL_CtLike", EntityId.Invalid, false);
            world.Step();
            var crate = world.Entities.Single(value => value.Template.Name == "CtCrate");
            if (reference is null) reference = crate.Angle.Raw;
            else Check.That(crate.Angle.Raw == reference, $"{rate}: the crate has the heading 30 Hz gives it ({crate.Angle.Raw} against {reference}).");
        }
    }
}
