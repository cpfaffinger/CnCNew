using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-137: what the original tells <c>TheRadar</c> for the sake of one object's story (<c>tryInfiltrationEvent</c>, <c>StealthDetectorUpdate</c>, <c>BattlePlanUpdate</c>,
/// <c>OverchargeBehavior</c>) is a <see cref="SimEventKind.RadarNotice"/> of the simulation, raised at the places the original calls the radar. The simulation does not know whose
/// radar it is; the message feed of the local player's client decides.
/// </summary>
static partial class Tests
{
    private static List<SimEvent> StepCollecting(SimulationWorld world, long ticks, SimEventKind kind)
    {
        var found = new List<SimEvent>();
        for (long index = 0; index < ticks; index++)
            found.AddRange(world.Step().Events.Where(value => value.Kind == kind));
        return found;
    }

    /// <summary>A capture tells the victim's radar once, where <c>SpecialAbilityUpdate::startPreparation</c> does, before the building changes hands.</summary>
    public static void RadarNoticeAtTheStartOfACapture()
    {
        var world = StagedWorld(out _, out var feeder, upgrade: false);
        world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_StagedCapture");
        for (var index = 0; index < world.Frames(35); index++) world.Step();
        Check.That(feeder.Capture(1, 1, 2).Accepted, "The capture is accepted.");
        var notices = new List<SimEvent>();
        long captured = -1;
        for (var index = 0; index < world.Frames(600); index++)
        {
            foreach (var value in world.Step().Events)
            {
                if (value.Kind == SimEventKind.RadarNotice) notices.Add(value);
                else if (value.Kind == SimEventKind.StructureCaptured) captured = world.Tick;
            }
        }
        Check.That(notices.Count == 1 && notices[0].Text == RadarNotices.Infiltration && notices[0].Entity == new EntityId(2) && notices[0].Other == new EntityId(1),
            $"One infiltration notice for the building, by the capturer ({notices.Count} notices).");
        Check.That(captured > notices[0].Tick, $"The notice (tick {notices[0].Tick}) comes before the building changes hands (tick {captured}).");
    }

    /// <summary>A hijack names the vehicle's old owner, so the victim's client can tell that it is the one hijacked.</summary>
    public static void HijackNamesTheFormerOwner()
    {
        var files = new Dictionary<string, string>(CratesAndSpecialsFiles()) { ["Data/INI/Object/GuardHijacker.ini"] = GuardHijackerIni };
        var rate = TestRate.Default;
        var fixture = new SyntheticFixture(rate, files: files);
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "GuardHijacker", owner: 1, x: 0, y: 0);
        feeder.Spawn(2, "EjectingTruck", owner: 2, x: 70, y: 20);
        for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
        var hijacker = world.Entities.Single(value => value.Template.Name == "GuardHijacker");
        var truck = world.Entities.Single(value => value.Template.Name == "EjectingTruck");
        Check.That(feeder.Guard(1, [hijacker.Id.Value], 0, 0).Accepted, "The guard order is taken.");
        var converted = StepCollecting(world, world.Frames(300), SimEventKind.ObjectConverted);
        Check.That(converted.Count == 1 && converted[0].Text == "HIJACKED" && converted[0].Entity == truck.Id && converted[0].Value == 2,
            $"The hijack event names the truck and its former owner, player 2 ({converted.Count} events).");
        Check.That(truck.Owner == new PlayerId(1), "The truck is the hijacker's.");
    }

    /// <summary><c>StealthDetectorUpdate::update</c>: the discovery of a stealthed object is told once - when it was not detected - and only for an object that is not the detector's ally's.</summary>
    public static void RadarNoticeWhenAStealthObjectIsFound()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Spotter", x: 0, y: 0);
        feeder.Spawn(2, "Cloaked", x: 10, y: 0);
        feeder.Spawn(1, "Cloaked", x: 10, y: 0);
        world.Step();
        var ally = world.FindEntity(new EntityId(2))!;
        var enemy = world.FindEntity(new EntityId(3))!;
        var notices = StepCollecting(world, world.Frames(60), SimEventKind.RadarNotice);
        Check.That(enemy.HasStatus(ObjectStatusBits.Index("DETECTED")), "The enemy is detected.");
        Check.That(notices.Count == 1 && notices[0].Text == RadarNotices.StealthRevealed && notices[0].Entity == enemy.Id && notices[0].Other == new EntityId(1),
            $"The discovery is told once, for the enemy, by the detector ({notices.Count} notices, over 60 frames of a scan that goes on).");
        Check.That(notices.All(value => value.Entity != ally.Id), "The detector's own stealthed unit is never revealed.");
    }

    /// <summary><c>BattlePlanUpdate::setStatus</c>, unpacking: the strategy center's radar blip is made when a plan begins to unpack.</summary>
    public static void RadarNoticeWhenABattlePlanUnpacks()
    {
        var (world, feeder, _) = BattlePlanWorld();
        var center = CsSpawn(world, feeder, "PlanCenter", 1, 100, 100);
        CsRun(world, 10);
        Check.That(StepCollecting(world, world.Frames(5), SimEventKind.RadarNotice).Count == 0, "An idle center tells nothing.");
        Check.That(feeder.UseSpecialPower(1, center.Id.Value, "SPECIAL_BattlePlanFixture", SpecialPowerTargetKind.None, option: 3).Accepted, "The plan is chosen.");
        var notices = StepCollecting(world, world.Frames(60), SimEventKind.RadarNotice);
        Check.That(notices.Count == 1 && notices[0].Text == RadarNotices.BattlePlan && notices[0].Entity == center.Id,
            $"One battle plan notice at the center when the plan begins to unpack ({notices.Count} notices; later transitions tell nothing).");
    }

    /// <summary><c>OverchargeBehavior::update</c>: the plant that switches itself off for want of health tells the radar once.</summary>
    public static void RadarNoticeWhenAnOverchargeGivesOut()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: PowerPlantFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "OverStation", owner: 1, x: 50, y: 50);
        world.Step();
        var plant = world.FindEntity(new EntityId(1))!;
        var overcharge = plant.Modules.OfType<OverchargeBehavior>().Single();
        overcharge.Toggle(world);
        var notices = StepCollecting(world, world.Frames(80), SimEventKind.RadarNotice);
        Check.That(!overcharge.Active, "The overcharge gave out.");
        Check.That(notices.Count == 1 && notices[0].Text == RadarNotices.OverchargeExhausted && notices[0].Entity == plant.Id, $"One notice for the plant ({notices.Count}).");
        overcharge.Toggle(world);
        overcharge.Toggle(world);
        Check.That(StepCollecting(world, 1, SimEventKind.RadarNotice).Count == 0, "Switching it on and off by hand tells nothing.");
    }
}
