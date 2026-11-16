using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
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

    /// <summary>
    /// #409 on the real data, at 30, 60 and 120 Hz: a ranger healed in its barracks walks out of the door when the barracks is sold, and when another barracks is evacuated;
    /// a hacker in the Internet Center (an <c>InternetHackContain</c>, which is a <c>TransportContain</c> and always had its door) walks out when the center is sold.
    /// </summary>
    public static void RealSoldBarracksLetsItsOccupantOutThroughTheDoor(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 120, 1, (_, _) => 0), "sold-barracks");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "sold-barracks", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            void Spawn(string template, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            Spawn("AmericaBarracks", 500, 600);
            Spawn("AmericaBarracks", 900, 600);
            Spawn("ChinaInternetCenter", 1300, 600);
            Spawn("AmericaInfantryRanger", 500, 750);
            Spawn("AmericaInfantryRanger", 900, 750);
            Spawn("ChinaInfantryHacker", 1300, 800);
            for (var i = 0; i < world.Frames(1); i++) world.Step();
            var buildings = world.Entities.Where(value => value.Template.IsKindOf("STRUCTURE")).OrderBy(value => value.Id.Value).ToList();
            var riders = world.Entities.Where(value => value.Template.IsKindOf("INFANTRY")).OrderBy(value => value.Id.Value).ToList();
            Check.That(buildings.Count == 3 && riders.Count == 3, $"{rate}: three buildings and three soldiers.");
            Check.That(buildings[0].Container is HealContain && buildings[2].Container is InternetHackContain, $"{rate}: the barracks heals, the Internet Center hacks.");
            for (var i = 0; i < 3; i++)
            {
                // Hurt, so the barracks keeps the ranger to heal it.
                Check.That(world.Submit(new DamageCommand(PlayerId.System, ++sequence, world.Tick, riders[i].Id, riders[i].Body!.MaxHealth / Fixed.FromInt(2), 0, 0)).Accepted, "The soldier is hurt.");
                Check.That(world.Submit(new MoveAndEnterCommand(new PlayerId(1), ++sequence, world.Tick, riders[i].Id, buildings[i].Id)).Accepted, $"{rate}: the order to enter is accepted.");
            }
            for (var step = 0; step < world.Frames(900) && Enumerable.Range(0, 3).Any(i => riders[i].ContainedBy != buildings[i].Id); step++) world.Step();
            Check.That(Enumerable.Range(0, 3).All(i => riders[i].ContainedBy == buildings[i].Id), $"{rate}: every soldier is inside its building.");

            while (world.Tick % world.Time.TicksPerOriginalFrame != 0) world.Step();
            Check.That(world.Submit(new SellCommand(new PlayerId(1), ++sequence, world.Tick, buildings[0].Id)).Accepted, $"{rate}: the barracks can be sold while it heals.");
            Check.That(world.Submit(new EvacuateCommand(new PlayerId(1), ++sequence, world.Tick, buildings[1].Id, false, Fixed.Zero, Fixed.Zero)).Accepted, $"{rate}: the other barracks can be evacuated.");
            Check.That(world.Submit(new SellCommand(new PlayerId(1), ++sequence, world.Tick, buildings[2].Id)).Accepted, $"{rate}: the Internet Center can be sold while it hacks.");
            for (var step = 0; step < world.Frames(30) && riders.Any(rider => rider.ContainedBy.IsValid); step++) world.Step();
            Check.That(riders.All(rider => !rider.ContainedBy.IsValid), $"{rate}: every soldier leaves its building.");
            for (var step = 0; step < world.Frames(90); step++) world.Step();
            for (var i = 0; i < 3; i++)
            {
                var distance = Fixed.Sqrt((riders[i].X - buildings[i].X) * (riders[i].X - buildings[i].X) + (riders[i].Y - buildings[i].Y) * (riders[i].Y - buildings[i].Y));
                Check.That(distance > buildings[i].Template.GeometryMajorRadius,
                    $"{rate}: {riders[i].Template.Name}#{riders[i].Id.Value} walked out of {buildings[i].Template.Name}, {distance} from its centre (its radius {buildings[i].Template.GeometryMajorRadius}).");
            }
            for (var step = 0; step < world.Frames(300) && (world.FindEntity(buildings[0].Id) is not null || world.FindEntity(buildings[2].Id) is not null); step++) world.Step();
            Check.That(world.FindEntity(buildings[0].Id) is null && world.FindEntity(buildings[2].Id) is null, $"{rate}: both sales complete.");
            Check.That(riders.All(rider => world.FindEntity(rider.Id) is { EffectivelyDead: false, ContainedBy.IsValid: false }), $"{rate}: every soldier stands in the world after the sales.");
        }
    }
}
