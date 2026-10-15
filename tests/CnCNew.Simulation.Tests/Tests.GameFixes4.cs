using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// The fixes of the community source adopted in Phase 34, after the fidelity corrections F-34, F-38 and F-39 of the original's own behaviour (docs/GGC-FIXES.md section 3.1d):
// each scene runs under the original profile and under a profile that names the fix.
static partial class Tests
{
    /// <summary>
    /// <see cref="GameFix.DoorExitToRally"/> (<c>OpenContain::getRallyPoint</c>): a heal pad that is also a factory with a rally point (a barracks) lets a healed patient out
    /// through its door. In the original the patient stops in front of the door; with the fix it walks on to the factory's rally point.
    /// </summary>
    public static void GameFixDoorExitToRally()
    {
        (Fixed X, Fixed Y) Released(RuleProfile rules)
        {
            var files = new Dictionary<string, string>(RepairFiles());
            files["Data/INI/Object/HealBarracksFixture.ini"] = """
            Object HealBarracks
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
                TimeForFullHeal = 300
                AllowInsideKindOf = INFANTRY
                AllowAlliesInside = Yes
              End
              Behavior = ProductionUpdate Prod01
                MaxQueueEntries = 2
              End
              Behavior = DefaultProductionExitUpdate Exit01
                UnitCreatePoint = X:0.0 Y:0.0 Z:0.0
                NaturalRallyPoint = X:30.0 Y:0.0 Z:0.0
              End
            End
            """;
            var fixture = new SyntheticFixture(TestRate.Default, files: files, rules: rules);
            var geometry = new ModelGeometry([], [new KeyValuePair<string, ImmutableArray<ExitPath>>("HealBarracks",
                [new ExitPath(new BonePose(Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero), new BonePose(Fixed.FromInt(40), Fixed.Zero, Fixed.Zero, Fixed.Zero))])]);
            var templates = TemplateCatalog.Build(fixture.Graph, fixture.Bindings, TestRate.Default, geometry, rules);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, RepairPlayers, SyntheticFixture.FixtureId, geometry.Combine(fixture.Fingerprint), string.Empty), templates, null);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "HealBarracks", x: 200, y: 200);
            feeder.Spawn(1, "Patient", x: 150, y: 200);
            world.Step();
            var patient = Get(world, "Patient");
            var barracks = Get(world, "HealBarracks");
            Check.That(feeder.SetRallyPoint(1, barracks.Id.Value, 200, 330).Accepted, "The barracks' rally point is set.");
            feeder.Damage(patient.Id.Value, 20);
            world.Step();
            Check.That(feeder.EnterContainer(1, patient.Id.Value, barracks.Id.Value).Accepted, "The patient is sent in.");
            RunUntilContained(world, patient, 400);
            Check.That(patient.ContainedBy == barracks.Id, "Setup: the patient is inside.");
            for (var tick = 0; tick < world.Frames(100) && patient.ContainedBy.IsValid; tick++) world.Step();
            Check.That(!patient.ContainedBy.IsValid, "Setup: the healed patient was let out.");
            for (var tick = 0; tick < world.Frames(450); tick++) world.Step();
            return (patient.X, patient.Y);
        }
        static bool Near((Fixed X, Fixed Y) at, int x, int y) => (at.X - Fixed.FromInt(x)).Abs() < Fixed.FromInt(15) && (at.Y - Fixed.FromInt(y)).Abs() < Fixed.FromInt(15);
        var original = Released(RuleProfile.Original);
        Check.That(Near(original, 240, 200), $"The original: the patient stays in front of the door ({original.X}, {original.Y}).");
        var fixedAt = Released(RuleProfile.Parse("door-exit-to-rally"));
        Check.That(Near(fixedAt, 200, 330), $"With the fix it walks on to the rally point ({fixedAt.X}, {fixedAt.Y}).");
    }

    /// <summary>
    /// <see cref="GameFix.BikeTrainableFromRider"/> (<c>RiderChangeContain::onContaining</c>): a trainable bike ridden by an untrainable rider gains a level by experience in the
    /// original; with the fix it is as untrainable as its rider.
    /// </summary>
    public static void GameFixBikeTrainableFromRider()
    {
        int LevelAfterExperience(RuleProfile rules)
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: SlaveFiles(), rules: rules);
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "SvBike", x: 100, y: 100);
            StepN(world, 6);
            var bike = SlaveEntity(world, "SvBike");
            var rebel = SlaveEntity(world, "SvBikeRebel");
            Check.That(rebel.ContainedBy == bike.Id && !rebel.Template.IsTrainable && bike.Template.IsTrainable, "Setup: an untrainable rider drives a trainable bike.");
            world.AddExperiencePoints(bike, 250);
            SnapshotContinues(world, fixture);
            return bike.VeterancyLevel;
        }
        Check.That(LevelAfterExperience(RuleProfile.Original) == 2, "The original: the bike ranks up although its rider cannot.");
        Check.That(LevelAfterExperience(RuleProfile.Parse("bike-trainable-from-rider")) == 0, "With the fix the bike is as untrainable as its rider.");
    }

    private static IReadOnlyDictionary<string, string> TallBoarderFiles() => new Dictionary<string, string>(TransportFiles())
    {
        ["Data/INI/Object/TallBoarderFixture.ini"] = """
        Object TallGrunt
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 3.0
          GeometryHeight = 30.0
          TransportSlotCount = 1
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>
    /// <see cref="GameFix.EnterNeedsVerticalOverlap"/> (<c>AIEnterState::update</c>): a boarder 30 units tall waits under a hovering Chinook that lands for it. In the original
    /// it goes in once the Chinook stands on the ground; with the fix as soon as the Chinook is low enough to overlap it - some frames earlier, in the air.
    /// </summary>
    public static void GameFixEnterNeedsVerticalOverlap()
    {
        (long Frame, Fixed Height) Boarded(RuleProfile rules)
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: TallBoarderFiles(), rules: rules);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 11, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Rotorcraft", x: 150, y: 150);
            for (var tick = 0; tick < world.Frames(23); tick++) world.Step();
            var chinook = Get(world, "Rotorcraft");
            Check.That(chinook.Z == Fixed.FromInt(100), "Setup: the Chinook hovers at its height.");
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            feeder.Spawn(1, "TallGrunt", x: 140, y: 150);
            for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
            var grunt = Get(world, "TallGrunt");
            Check.That(feeder.MoveAndEnter(1, grunt.Id.Value, chinook.Id.Value).Accepted, "The grunt is ordered in.");
            var start = world.Tick;
            for (var tick = 0; tick < world.Frames(60) && !grunt.ContainedBy.IsValid; tick++) world.Step();
            Check.That(grunt.ContainedBy == chinook.Id, "The grunt boarded.");
            return (world.InFrames(world.Tick - start), chinook.Z);
        }
        var original = Boarded(RuleProfile.Original);
        Check.That(original.Height == Fixed.Zero, $"The original: the grunt boards the landed Chinook (frame {original.Frame}, height {original.Height}).");
        var fixedRun = Boarded(RuleProfile.Parse("enter-needs-vertical-overlap"));
        Check.That(fixedRun.Height > Fixed.Zero && fixedRun.Height <= Fixed.FromInt(30) && fixedRun.Frame < original.Frame,
            $"With the fix it boards the Chinook in the air once they overlap (frame {fixedRun.Frame}, height {fixedRun.Height}; the original in frame {original.Frame}).");
    }
}
