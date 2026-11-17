using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// #409: the occupant of a <c>HealContain</c> (a barracks healing an infantryman) that is sold (<c>OpenContain::onSelling</c>) or evacuated (<c>AIGroup::groupEvacuate</c> of a
    /// structure) is told to leave (<c>orderAllPassengersToExit</c> -> <c>aiExit</c>), and <c>AIExitState</c> takes it out through <c>OpenContain::exitObjectViaDoor</c>: on the exit
    /// path's start, walking to its end. Before, it was put on the building's centre and stood there. Two pads with one exit path from the centre to 40 units in front.
    /// </summary>
    public static void SoldOrEvacuatedHealContainLetsItsOccupantOutThroughTheDoor()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var files = new Dictionary<string, string>(RepairFiles())
            {
                ["Data/INI/Object/SoldHealFixture.ini"] = """
                Object DoorPad
                  KindOf = STRUCTURE HEAL_PAD
                  Geometry = BOX
                  GeometryMajorRadius = 20.0
                  GeometryMinorRadius = 20.0
                  GeometryHeight = 10.0
                  GeometryIsSmall = No
                  Body = StructureBody Body01
                    MaxHealth = 500.0
                    InitialHealth = 500.0
                  End
                  Behavior = DestroyDie Destroy01
                  End
                  Behavior = HealContain Heal01
                    ContainMax = 2
                    TimeForFullHeal = 60000
                    AllowInsideKindOf = INFANTRY
                    AllowAlliesInside = Yes
                  End
                End
                Object DoorWalker
                  KindOf = SELECTABLE INFANTRY
                  Locomotor = SET_NORMAL TestLocomotor
                  Body = ActiveBody Body01
                    MaxHealth = 50.0
                    InitialHealth = 50.0
                  End
                  Behavior = DestroyDie Destroy01
                  End
                  Behavior = AIUpdateInterface AI01
                  End
                End
                """,
            };
            var fixture = new SyntheticFixture(rate, files: files);
            var start = new BonePose(Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero);
            var end = new BonePose(Fixed.FromInt(40), Fixed.Zero, Fixed.Zero, Fixed.Zero);
            var geometry = new ModelGeometry([], [new KeyValuePair<string, ImmutableArray<ExitPath>>("DoorPad", [new ExitPath(start, end)])]);
            var templates = TemplateCatalog.Build(fixture.Graph, fixture.Bindings, rate, geometry);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, RepairPlayers, SyntheticFixture.FixtureId, geometry.Combine(fixture.Fingerprint), string.Empty), templates, null);
            var feeder = new Feeder(world);
            // Spawn commands run in order: 1 sold pad, 2 evacuated pad, 3 and 4 their occupants.
            feeder.Spawn(1, "DoorPad", x: 200, y: 200);
            feeder.Spawn(1, "DoorPad", x: 400, y: 200);
            feeder.Spawn(1, "DoorWalker", x: 150, y: 200);
            feeder.Spawn(1, "DoorWalker", x: 350, y: 200);
            world.Step();
            var sold = world.FindEntity(new EntityId(1))!;
            var evacuated = world.FindEntity(new EntityId(2))!;
            var first = world.FindEntity(new EntityId(3))!;
            var second = world.FindEntity(new EntityId(4))!;
            Check.That(feeder.EnterContainer(1, 3, 1).Accepted && feeder.EnterContainer(1, 4, 2).Accepted, $"{rate}: the walkers are sent into the pads.");
            for (var i = 0; i < world.Frames(400) && !(first.ContainedBy == sold.Id && second.ContainedBy == evacuated.Id); i++) world.Step();
            Check.That(first.ContainedBy == sold.Id && second.ContainedBy == evacuated.Id, $"{rate}: each walker is inside its pad.");

            // In the first tick of a frame of the original, at every rate.
            while (world.Tick % world.Time.TicksPerOriginalFrame != 0) world.Step();
            Check.That(feeder.Sell(1, 1).Accepted, $"{rate}: the occupied pad can be sold.");
            Check.That(feeder.Evacuate(1, 2).Accepted, $"{rate}: the other pad can be evacuated.");
            var orderFrame = world.Time.ToOriginalFrames(world.Tick);
            world.Step();
            var frame = world.Time.ToOriginalFrames(world.Tick - 1);
            foreach (var (walker, pad, what) in new[] { (first, sold, "sold"), (second, evacuated, "evacuated") })
            {
                Check.That(!walker.ContainedBy.IsValid, $"{rate}: the walker leaves the {what} pad at once.");
                Check.That(walker.HasMoveTarget, $"{rate}: the walker of the {what} pad walks down the exit path (it stands at ({walker.X}, {walker.Y}), the pad at ({pad.X}, {pad.Y})).");
            }
            Check.That(frame == orderFrame, $"{rate}: the walkers leave in the frame of the order ({frame}, the order in {orderFrame}).");

            for (var i = 0; i < world.Frames(60); i++) world.Step();
            foreach (var (walker, padX, what) in new[] { (first, Fixed.FromInt(200), "sold"), (second, Fixed.FromInt(400), "evacuated") })
                Check.That(walker.X - padX > Fixed.FromInt(20) && (walker.Y - Fixed.FromInt(200)).Abs() < Fixed.FromInt(20),
                    $"{rate}: the walker of the {what} pad walked out of its door, in front of it ({walker.X}, {walker.Y}; the pad at ({padX}, 200)).");

            for (var i = 0; i < world.Frames(300) && world.FindEntity(sold.Id) is not null; i++) world.Step();
            Check.That(world.FindEntity(sold.Id) is null, $"{rate}: the sale completes.");
            Check.That(world.FindEntity(first.Id) is { EffectivelyDead: false, ContainedBy.IsValid: false }, $"{rate}: the walker of the sold pad stands in the world after the sale.");
        }
    }
}
