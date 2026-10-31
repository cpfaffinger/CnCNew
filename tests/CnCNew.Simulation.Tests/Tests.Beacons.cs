using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary><c>GameLogic::onPlaceBeacon</c>, <c>onRemoveBeacon</c> (F-24): a beacon is an object of the player, at most <c>MaxBeaconsPerPlayer</c> of them.</summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> BeaconFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/BeaconFixture.ini"] = """
        Object TestBeacon
          KindOf = UNATTACKABLE SELECTABLE NO_COLLIDE ALWAYS_VISIBLE STRUCTURE IMMOBILE
          Body = InactiveBody ModuleTag_01
          End
        End

        PlayerTemplate BeaconFaction
          Side = Test
          BeaconName = TestBeacon
        End

        MultiplayerSettings
          MaxBeaconsPerPlayer = 2
        End
        """,
    };

    private static SimulationWorld BeaconWorld()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: BeaconFiles());
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha", "BeaconFaction"), new(new PlayerId(2), "Beta", "BeaconFaction"), new(new PlayerId(3), "Gamma")];
        return new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
    }

    public static void BeaconsStandAndAreLimited()
    {
        var world = BeaconWorld();
        var player = new PlayerId(1);
        // A player that has nothing is defeated, and its order stands for nothing.
        Check.That(world.IsPlayerDefeated(player) && !world.Submit(new PlaceBeaconCommand(player, 2, world.Tick, Fixed.FromInt(10), Fixed.FromInt(20))).Accepted, "A defeated player places no beacon.");

        var feeder = new Feeder(world);
        feeder.Spawn(1, "Tank", owner: 1);
        world.Step();
        Check.That(!world.IsPlayerDefeated(player), "The player has something.");
        Check.That(world.Submit(new PlaceBeaconCommand(player, 3, world.Tick, Fixed.FromInt(10), Fixed.FromInt(20))).Accepted, "The order is taken.");
        Check.That(world.Submit(new PlaceBeaconCommand(player, 4, world.Tick, Fixed.FromInt(30), Fixed.FromInt(20))).Accepted, "A second one: the check counts what stands.");
        var result = world.Step();
        Check.That(world.BeaconCount(player) == 2, $"Two beacons stand ({world.BeaconCount(player)}).");
        var placed = result.Events.Where(item => item.Kind == SimEventKind.BeaconPlaced).ToArray();
        Check.That(placed.Length == 2 && placed.All(item => item.Player == player && world.FindEntity(item.Entity) is { } beacon && world.IsBeacon(beacon)), "Each beacon is announced with its player.");

        Check.That(!world.Submit(new PlaceBeaconCommand(player, 5, world.Tick, Fixed.FromInt(50), Fixed.FromInt(20))).Accepted && world.Check(new PlaceBeaconCommand(player, 6, world.Tick, Fixed.FromInt(50), Fixed.FromInt(20))).Reason == CommandRejection.TooManyBeacons, "A third is refused for its limit.");
        Check.That(world.RejectionCounts[(int)CommandRejection.TooManyBeacons] == 1, "And counted.");
        Check.That(!world.Submit(new PlaceBeaconCommand(new PlayerId(3), 1, world.Tick, Fixed.Zero, Fixed.Zero)).Accepted, "A player without a beacon template has none.");

        // The owner's beacons among a selection go; another's beacon and any other object stay.
        var beacons = placed.Select(item => item.Entity).ToArray();
        var fighter = world.Entities.First(entity => !world.IsBeacon(entity));
        Check.That(world.Submit(new RemoveBeaconCommand(new PlayerId(2), 1, world.Tick, [beacons[0]])).Accepted && world.Submit(new RemoveBeaconCommand(player, 7, world.Tick, [beacons[1], fighter.Id])).Accepted, "The orders are taken.");
        world.Step();
        Check.That(world.FindEntity(beacons[0]) is not null && world.FindEntity(beacons[1]) is null && world.FindEntity(fighter.Id) is not null && world.BeaconCount(player) == 1, "Only the owner's beacon goes.");
        Check.That(world.Submit(new PlaceBeaconCommand(player, 8, world.Tick, Fixed.FromInt(50), Fixed.FromInt(20))).Accepted, "The limit counts what stands: there is room again.");
    }

    public static void BeaconCommandsRoundTrip()
    {
        var player = new PlayerId(1);
        var fixture = new SyntheticFixture(TestRate.Default, files: BeaconFiles());
        Check.That(fixture.Templates.MaxBeaconsPerPlayer == 2 && fixture.Templates.BeaconTemplates.SequenceEqual(["TestBeacon"]), "The limit and the template come from the data.");
        PlaceBeaconCommand place = new(player, 7, 9, Fixed.FromInt(10), Fixed.FromInt(20));
        RemoveBeaconCommand remove = new(player, 8, 9, [new EntityId(5), new EntityId(6)]);
        Check.That(CommandTypes.IsPlayerOrder(CommandTypes.PlaceBeacon) && CommandTypes.IsPlayerOrder(CommandTypes.RemoveBeacon), "Both are orders of a player.");
        Check.That(ReplayFormat.DecodeCommand(ReplayFormat.EncodeCommand(place)).Equals(place) && ReplayFormat.DecodeCommand(ReplayFormat.EncodeCommand(remove)).Equals(remove), "Both round-trip in the replay format.");
        Check.That(!BeaconWorld().Submit(new PlaceBeaconCommand(player, 1, 0, Fixed.FromInt(1_000_000), Fixed.Zero)).Accepted, "A point beyond any map is refused by the limit of the wire.");
    }
}
