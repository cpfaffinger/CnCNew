using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> RetailModuleFiles() => new Dictionary<string, string>(Phase5Files())
    {
        ["Data/INI/Object/RetailModules.ini"] = """
        Upgrade Upgrade_Radar
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        ObjectCreationList OCL_PilotGround
          CreateObject
            ObjectNames = GroundPilot
            Count = 1
          End
        End
        ObjectCreationList OCL_PilotAir
          CreateObject
            ObjectNames = AirPilot
            Count = 1
          End
        End

        Object QuickUnit
          KindOf = SELECTABLE INFANTRY
          BuildCost = 10
          BuildTime = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object DefaultExitFactory
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:10.0 Y:-4.0 Z:0.0
            NaturalRallyPoint = X:20.0 Y:0.0 Z:0.0
          End
        End

        Object QueueExitFactory
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
          Behavior = QueueProductionExitUpdate Exit01
            UnitCreatePoint = X:0.0 Y:8.0 Z:0.0
            NaturalRallyPoint = X:0.0 Y:20.0 Z:0.0
            ExitDelay = 3000
            InitialBurst = 1
          End
        End

        Object Barracks
          KindOf = STRUCTURE
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
            AllowEnemiesInside = No
            AllowNeutralInside = No
          End
        End

        Object Jeep
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = EjectPilotDie Eject01
            GroundCreationList = OCL_PilotGround
            AirCreationList = OCL_PilotAir
            InvulnerableTime = 1000
          End
        End
        Object GroundPilot
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object AirPilot
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End

        Object EliteTank
          KindOf = SELECTABLE VEHICLE
          IsTrainable = Yes
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = VeterancyGainCreate Vet01
            StartingLevel = ELITE
            ScienceRequired = SCIENCE_Intrinsic
          End
        End
        Object VeteranTank
          KindOf = SELECTABLE VEHICLE
          IsTrainable = Yes
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = VeterancyGainCreate Vet01
            StartingLevel = VETERAN
          End
        End
        Object DrillTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = VeterancyGainCreate Vet01
            StartingLevel = HEROIC
          End
          Behavior = PreorderCreate Pre01
          End
        End

        Object RadarBuilding
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = RadarUpdate Radar01
            RadarExtendTime = 3000
          End
          Behavior = RadarUpgrade RadarUp01
            TriggeredBy = Upgrade_Radar
          End
        End

        Object Shack
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = RebuildHoleExposeDie Hole01
            HoleName = ShackHole
            HoleMaxHealth = 100.0
          End
        End
        Object BrokenShack
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = RebuildHoleExposeDie Hole01
            HoleName = HollowHole
            HoleMaxHealth = 100.0
          End
        End
        Object ShackHole
          KindOf = STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = RebuildHoleBehavior Rebuild01
            WorkerObjectName = HoleWorker
            WorkerRespawnDelay = 1000
            HoleHealthRegen%PerSecond = 30%
          End
        End
        Object HollowHole
          KindOf = STRUCTURE
          Behavior = LockWeaponCreate Parking01
            SlotToLock = SECONDARY
          End
        End
        Object HoleWorker
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        """,
    };

    private static List<long> SpawnTicks(SimulationWorld world, Feeder feeder, string template, int count, int maxTicks)
    {
        var ticks = new List<long>();
        var seen = 0;
        for (var i = 0; i < count; i++) feeder.QueueProduction(1, 1, template);
        for (var i = 0; i < maxTicks && ticks.Count < count; i++)
        {
            world.Step();
            var now = CountOf(world, template);
            while (seen < now) { ticks.Add(world.Tick); seen++; }
        }
        return ticks;
    }

    /// <summary>The production exits: units appear at the transformed <c>UnitCreatePoint</c>, and <c>QueueProductionExitUpdate</c> holds finished units back by its exit delay once the initial burst is spent.</summary>
    public static void RetailProductionExits()
    {
        var fixture = new SyntheticFixture(files: RetailModuleFiles());
        Check.That(fixture.Templates.TryGet("DefaultExitFactory", out var factoryTemplate) && factoryTemplate.IsInstantiable && fixture.Templates.TryGet("QueueExitFactory", out var queueTemplate) && queueTemplate.IsInstantiable,
            "Both exit modules bind and leave their producers instantiable.");

        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "DefaultExitFactory", x: 5, y: 5);
        world.Step();
        feeder.GiveMoney(1, 1000);
        world.Step();
        var defaultTicks = SpawnTicks(world, feeder, "QuickUnit", 3, 400);
        Check.That(defaultTicks.Count == 3 && defaultTicks[1] - defaultTicks[0] == 30 && defaultTicks[2] - defaultTicks[1] == 30, $"The default exit is always free: units leave every BuildTime ({string.Join(", ", defaultTicks)}).");
        var unit = world.Entities.First(value => value.Template.Name == "QuickUnit");
        Check.That((unit.X - Fixed.FromInt(15)).Abs() < Fixed.FromDecimal(0.01m) && (unit.Y - Fixed.FromInt(1)).Abs() < Fixed.FromDecimal(0.01m), $"The unit appears at the producer position plus UnitCreatePoint ({unit.X}, {unit.Y}).");

        var queued = NewWorld(fixture);
        var queuedFeeder = new Feeder(queued);
        queuedFeeder.Spawn(1, "QueueExitFactory", x: 0, y: 0);
        queued.Step();
        queuedFeeder.GiveMoney(1, 1000);
        queued.Step();
        var queueTicks = SpawnTicks(queued, queuedFeeder, "QuickUnit", 3, 600);
        Check.That(queueTicks.Count == 3, $"All three queued units eventually leave ({queueTicks.Count}).");
        Check.That(queueTicks[1] - queueTicks[0] is >= 88 and <= 92 && queueTicks[2] - queueTicks[1] is >= 88 and <= 92,
            $"After the one-unit initial burst every exit closes the door for ExitDelay (90 ticks): {string.Join(", ", queueTicks)}.");
        var queuedUnit = queued.Entities.First(value => value.Template.Name == "QuickUnit");
        Check.That(queuedUnit.X.Abs() < Fixed.FromDecimal(0.01m) && (queuedUnit.Y - Fixed.FromInt(8)).Abs() < Fixed.FromDecimal(0.01m), $"The queue exit places the unit at its own create point ({queuedUnit.X}, {queuedUnit.Y}).");

        // snapshot in the middle of the delay
        var live = NewWorld(fixture);
        var liveFeeder = new Feeder(live);
        liveFeeder.Spawn(1, "QueueExitFactory");
        live.Step();
        liveFeeder.GiveMoney(1, 1000);
        live.Step();
        for (var i = 0; i < 3; i++) liveFeeder.QueueProduction(1, 1, "QuickUnit");
        for (var i = 0; i < 50; i++) live.Step();
        Check.That(CountOf(live, "QuickUnit") == 1, "Setup: one unit left, the door is closed.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == live.StateHash, "The exit delay round-trips through a snapshot.");
        for (var i = 0; i < 200; i++)
            Check.That(live.Step().StateHash == restored.Step().StateHash, "The delayed exit is hash-identical after a restore at step " + i + ".");
        Check.That(CountOf(restored, "QuickUnit") == 3, "The restored world released the remaining units.");
    }

    /// <summary><c>HealContain</c> heals its occupants over <c>TimeForFullHeal</c>, then lets them go; vehicles, enemies and a full house are refused.</summary>
    public static void RetailHealContain()
    {
        var fixture = new SyntheticFixture(files: RetailModuleFiles());
        Check.That(fixture.Templates.TryGet("Barracks", out var barracksTemplate) && barracksTemplate.IsInstantiable, "HealContain binds: " + (barracksTemplate is null ? "missing" : string.Join(", ", barracksTemplate.MissingModules)));
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Barracks");
        feeder.Spawn(1, "Rebel", x: 3, y: 3);
        feeder.Spawn(2, "Rebel", x: 3, y: 3);
        feeder.Spawn(1, "Fighter", x: 3, y: 3);
        world.Step();
        var barracks = world.FindEntity(new EntityId(1))!;
        var rebel = world.FindEntity(new EntityId(2))!;
        feeder.Damage(2, 30);
        world.Step();

        // Spawn commands run in player order: 1 Barracks, 2 Rebel, 3 Fighter (player 1), 4 Rebel (player 2).
        feeder.EnterContainer(1, 3, 1);
        feeder.EnterContainer(2, 4, 1);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.CannotEnterContainer] == 2, "A vehicle (AllowInsideKindOf) and an enemy (AllowEnemiesInside = No) are refused.");

        feeder.EnterContainer(1, 2, 1);
        world.Step();
        Check.That(barracks.Container!.ContainCount == 1 && rebel.ContainedBy == barracks.Id, "An allied infantry unit enters.");
        var afterEntry = rebel.Body!.Health;
        Check.That(afterEntry > Fixed.FromInt(20), "Healing starts on the tick of entry.");
        for (var i = 0; i < 12; i++) world.Step();
        Check.That(rebel.Body.Health == Fixed.FromInt(50), "The occupant is healed to full within TimeForFullHeal.");
        Check.That(!rebel.ContainedBy.IsValid && barracks.Container.ContainCount == 0, "A fully healed occupant leaves the building.");

        // a second visit works: the module went back to sleep and wakes on entry
        feeder.Damage(2, 25);
        world.Step();
        feeder.EnterContainer(1, 2, 1);
        for (var i = 0; i < 12; i++) world.Step();
        Check.That(rebel.Body.Health == Fixed.FromInt(50) && !rebel.ContainedBy.IsValid, "The barracks heals a second visitor after it fell asleep.");

        // snapshot with an occupant inside
        var live = NewWorld(fixture);
        var liveFeeder = new Feeder(live);
        liveFeeder.Spawn(1, "Barracks");
        liveFeeder.Spawn(1, "Rebel");
        live.Step();
        liveFeeder.Damage(2, 40);
        live.Step();
        liveFeeder.EnterContainer(1, 2, 1);
        for (var i = 0; i < 4; i++) live.Step();
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == live.StateHash, "An occupied HealContain round-trips.");
        for (var i = 0; i < 12; i++)
            Check.That(live.Step().StateHash == restored.Step().StateHash, "Healing after a restore is hash-identical at step " + i + ".");
        Check.That(!restored.FindEntity(new EntityId(2))!.ContainedBy.IsValid, "The restored occupant was released after the heal.");
    }

    /// <summary><c>EjectPilotDie</c>, <c>VeterancyGainCreate</c>, <c>PreorderCreate</c> and <c>RadarUpdate</c>.</summary>
    public static void RetailSmallModules()
    {
        var fixture = new SyntheticFixture(files: RetailModuleFiles());
        foreach (var name in new[] { "Jeep", "EliteTank", "VeteranTank", "DrillTank", "RadarBuilding", "Shack", "ShackHole", "HoleWorker" })
            Check.That(fixture.Templates.TryGet(name, out var template) && template.IsInstantiable, $"{name} is instantiable: " + (template is null ? "missing" : string.Join(", ", template.MissingModules)));

        // EjectPilotDie: ground list on death
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Jeep");
        world.Step();
        feeder.Kill(1);
        for (var i = 0; i < 3; i++) world.Step();
        Check.That(CountOf(world, "GroundPilot") == 1 && CountOf(world, "AirPilot") == 0, "A vehicle on the ground ejects its pilot through the ground list.");

        // VeterancyGainCreate: the player with the science gets the level, the one without stays regular; untrainable objects are untouched
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "Alpha", "TestFaction"), new PlayerConfiguration(new PlayerId(2), "Beta"));
        var vetWorld = new SimulationWorld(new WorldConfiguration(new WorldId(1), 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var vetFeeder = new Feeder(vetWorld);
        vetFeeder.Spawn(1, "EliteTank");
        vetFeeder.Spawn(1, "DrillTank");
        vetFeeder.Spawn(2, "EliteTank");
        vetFeeder.Spawn(2, "VeteranTank");
        vetWorld.Step();
        Check.That(vetWorld.FindEntity(new EntityId(1))!.VeterancyLevel == 2, "ScienceRequired granted: a trainable object starts at StartingLevel (ELITE).");
        Check.That(vetWorld.FindEntity(new EntityId(3))!.VeterancyLevel == 0, "ScienceRequired missing: the object stays regular.");
        Check.That(vetWorld.FindEntity(new EntityId(4))!.VeterancyLevel == 1, "Without a science requirement the level is granted to every owner");
        Check.That(vetWorld.FindEntity(new EntityId(2))!.VeterancyLevel == 0, "An object that is not IsTrainable is left alone.");

        // RadarUpdate: RadarUpgrade starts the extension
        var radarWorld = NewWorld(fixture);
        var radarFeeder = new Feeder(radarWorld);
        radarFeeder.Spawn(1, "RadarBuilding");
        radarWorld.Step();
        radarFeeder.GiveMoney(1, 100);
        radarWorld.Step();
        var radar = radarWorld.FindEntity(new EntityId(1))!.Modules.OfType<RadarUpdate>().Single();
        Check.That(!radar.RadarActive, "The radar is inactive before the upgrade.");
        radarFeeder.PurchaseUpgrade(1, "Upgrade_Radar", target: 1);
        for (var i = 0; i < 3; i++) radarWorld.Step();
        Check.That(radar.RadarActive && !radar.IsExtendComplete(radarWorld.Tick), "RadarUpgrade activates the radar; the extension is still running.");
        for (var i = 0; i < 100; i++) radarWorld.Step();
        Check.That(radar.IsExtendComplete(radarWorld.Tick), "The extension completes after RadarExtendTime.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(radarWorld), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == radarWorld.StateHash && restored.FindEntity(new EntityId(1))!.Modules.OfType<RadarUpdate>().Single().RadarActive, "The radar state round-trips.");
    }

    /// <summary>A dying GLA structure leaves a hole that regenerates and counts down to its (unmodeled) worker; neutral and unbuildable cases are explicit.</summary>
    public static void RetailRebuildHole()
    {
        var fixture = new SyntheticFixture(files: RetailModuleFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Shack", x: 7, y: 9);
        feeder.Spawn(0, "Shack", x: 50, y: 50);
        feeder.Spawn(1, "BrokenShack", x: 90, y: 90);
        world.Step();
        var events = new List<SimEvent>();
        void Step(int ticks) { for (var i = 0; i < ticks; i++) events.AddRange(world.Step().Events); }
        feeder.Kill(1);
        feeder.Kill(2);
        feeder.Kill(3);
        Step(3);
        var holes = world.Entities.Where(value => value.Template.Name == "ShackHole").ToArray();
        Check.That(holes.Length == 1 && holes[0].Owner.Value == 1 && holes[0].X == Fixed.FromInt(7) && holes[0].Y == Fixed.FromInt(9),
            $"The owner's structure leaves one hole at its place; the neutral one does not ({holes.Length}).");
        var hole = holes[0];
        Check.That(hole.Body!.MaxHealth == Fixed.FromInt(100) && hole.Body.Health == Fixed.FromInt(100), $"HoleMaxHealth replaces the template's health ({hole.Body.Health}/{hole.Body.MaxHealth}).");
        Check.That(events.Any(value => value.Kind == SimEventKind.CreationSkipped && value.Text == "HollowHole" && value.Value == CreationSkipReasons.TemplateNotInstantiable),
            "A hole template that cannot be instantiated is reported, not dropped.");

        feeder.Damage(hole.Id.Value, 50);
        Step(1);
        var damaged = hole.Body.Health;
        Step(40);
        Check.That(hole.Body.Health > damaged && hole.Body.Health <= hole.Body.MaxHealth, $"The hole regenerates ({damaged} -> {hole.Body.Health}).");
        var workerEvents = events.Where(value => value.Kind == SimEventKind.CreationSkipped && value.Text == "HoleWorker").ToArray();
        Check.That(workerEvents.Length == 1 && workerEvents[0].Value == CreationSkipReasons.SettingNotSimulated, "The worker respawn is reported once as not simulated.");
        Step(60);
        Check.That(events.Count(value => value.Kind == SimEventKind.CreationSkipped && value.Text == "HoleWorker") == 1, "The countdown does not repeat.");

        // snapshot with the hole alive
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash, "A world with a hole round-trips through a snapshot.");
        for (var i = 0; i < 20; i++)
            Check.That(world.Step().StateHash == restored.Step().StateHash, "The hole is hash-identical after a restore at step " + i + ".");
    }
}
