using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// A player leaves a running match (<see cref="PlayerLeaveCommand"/>, <c>MSG_SELF_DESTRUCT</c>; docs/SIMULATION-CORE.md §47): what happens to its objects is fixed here.
/// </summary>
internal static partial class Tests
{
    private static readonly ImmutableArray<PlayerConfiguration> ThreePlayers = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta"), new(new PlayerId(3), "Gamma")];

    private static IReadOnlyDictionary<string, string> PlayerLeaveFiles() => new Dictionary<string, string>(TransportFiles())
    {
        ["Data/INI/Object/PlayerLeaveFixture.ini"] = """
        Object OilTower
          KindOf = STRUCTURE TECH_BUILDING IMMOBILE
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) PlayerLeaveWorld()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: PlayerLeaveFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 3, ThreePlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Hauler4x4", x: 0, y: 0);
        feeder.Spawn(1, "Grunt", x: 30, y: 0);
        feeder.Spawn(1, "Grunt", x: 300, y: 300);
        feeder.Spawn(1, "OilTower", x: 600, y: 0);
        feeder.Spawn(2, "Grunt", x: 0, y: 600);
        feeder.Spawn(3, "Grunt", x: 600, y: 600);
        feeder.GiveMoney(1, 700);
        feeder.GiveMoney(2, 50);
        world.Step();
        var truck = Get(world, "Hauler4x4");
        var rider = world.Entities.First(entity => entity.Template.Name == "Grunt" && entity.Owner == new PlayerId(1) && entity.X == Fixed.FromInt(30));
        Check.That(feeder.MoveAndEnter(1, rider.Id.Value, truck.Id.Value).Accepted, "The grunt is ordered into the truck.");
        for (var tick = 0; tick < world.Frames(200) && !rider.ContainedBy.IsValid; tick++) world.Step();
        Check.That(rider.ContainedBy == truck.Id, "The grunt sits in the truck.");
        return (world, feeder, fixture);
    }

    private static int Living(SimulationWorld world, int player) => world.Entities.Count(entity => entity.Owner == new PlayerId(player) && !entity.EffectivelyDead && !entity.DestroyRequested);

    /// <summary>A lost connection: the riders are put out, everything of the player dies, a tech building becomes neutral, nobody else is touched.</summary>
    public static void PlayerLeaveKillsEverythingOfThePlayer()
    {
        var (world, _, fixture) = PlayerLeaveWorld();
        var tower = Get(world, "OilTower");
        var others = Living(world, 2) + Living(world, 3);
        Check.That(world.Submit(new PlayerLeaveCommand(new PlayerId(1), 900, world.Tick + 2, false)).Accepted, "The leave is accepted for a later tick.");
        Check.That(world.Submit(new PlayerLeaveCommand(PlayerId.System, 900, world.Tick, false)).Reason == CommandRejection.NotPermitted, "The system is no player that could leave.");
        Check.That(world.Submit(new PlayerLeaveCommand(new PlayerId(9), 1, world.Tick, false)).Reason == CommandRejection.UnknownPlayer, "An unknown player cannot leave.");

        // Pending in a snapshot, and in the replay encoding.
        var bytes = WorldSnapshot.Write(world);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, null);
        Check.That(restored.StateHash == world.StateHash && WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "A snapshot with the pending leave restores to the same world.");
        var command = new PlayerLeaveCommand(new PlayerId(2), 7, 12, true);
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, new string('A', 64), "fixture", 1, new WorldId(1), ThreePlayers, 20, 5);
        var read = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, [command], [])));
        Check.That(read.Commands.Length == 1 && read.Commands[0] == command && ReplayFormat.DecodeCommand(ReplayFormat.EncodeCommand(command)) == command, "The leave round-trips through the replay format.");

        Check.That(Living(world, 1) == 4, "Before the leave the player has its truck, two grunts and the tower.");
        for (var tick = 0; tick < world.Frames(4); tick++)
        {
            world.Step();
            restored.Step();
            world.VerifyIncrementalHash();
            Check.That(restored.StateHash == world.StateHash, $"Live and restored world agree {tick + 1} ticks later.");
        }
        Check.That(Living(world, 1) == 0 && world.IsPlayerDefeated(new PlayerId(1)), $"Everything of the player is dead ({Living(world, 1)} left).");
        Check.That(!world.Entities.Any(entity => entity.ContainedBy.IsValid), "Nobody is left inside a container.");
        Check.That(world.FindEntity(tower.Id) is { EffectivelyDead: false } kept && kept.Owner == PlayerId.System, "The tech building stands and belongs to nobody.");
        Check.That(Living(world, 2) + Living(world, 3) == others && world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(700), "The other players and the leaver's money are untouched.");
    }

    /// <summary>Leaving by choice: the first living ally in player order inherits objects and money; without one everything dies.</summary>
    public static void PlayerLeaveHandsOverToALivingAlly()
    {
        var (world, _, _) = PlayerLeaveWorld();
        // Player 3 is allied with 1 in both directions; player 2 only one way, which is no alliance.
        world.SetRelationship(new PlayerId(1), new PlayerId(3), Relationship.Allies);
        world.SetRelationship(new PlayerId(3), new PlayerId(1), Relationship.Allies);
        world.SetRelationship(new PlayerId(1), new PlayerId(2), Relationship.Allies);
        var before = Living(world, 3);
        var owned = Living(world, 1);
        Check.That(world.Submit(new PlayerLeaveCommand(new PlayerId(1), 900, world.Tick, true)).Accepted, "The leave is accepted.");
        for (var tick = 0; tick < world.Frames(4); tick++) world.Step();
        world.VerifyIncrementalHash();
        Check.That(Living(world, 1) == 0 && Living(world, 3) == before + owned, $"The ally owns what the leaver owned ({Living(world, 3)} of {before + owned}).");
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.Zero && world.MoneyOf(new PlayerId(3)) == Fixed.FromInt(700) && world.MoneyOf(new PlayerId(2)) == Fixed.FromInt(50), "The money went to the ally and to nobody else.");
        Check.That(world.Entities.Any(entity => entity.ContainedBy.IsValid && entity.Owner == new PlayerId(3)), "A rider that changed hands stays in its transport.");

        // The ally is dead: the same order kills.
        var (alone, feeder, _) = PlayerLeaveWorld();
        alone.SetRelationship(new PlayerId(1), new PlayerId(3), Relationship.Allies);
        alone.SetRelationship(new PlayerId(3), new PlayerId(1), Relationship.Allies);
        foreach (var entity in alone.Entities.Where(entity => entity.Owner == new PlayerId(3)).ToArray()) feeder.Kill(entity.Id.Value);
        for (var tick = 0; tick < alone.Frames(4); tick++) alone.Step();
        Check.That(alone.IsPlayerDefeated(new PlayerId(3)), "The ally is defeated.");
        Check.That(alone.Submit(new PlayerLeaveCommand(new PlayerId(1), 900, alone.Tick, true)).Accepted, "The leave is accepted.");
        for (var tick = 0; tick < alone.Frames(4); tick++) alone.Step();
        Check.That(Living(alone, 1) == 0 && Living(alone, 3) == 0 && alone.MoneyOf(new PlayerId(1)) == Fixed.FromInt(700), "Without a living ally everything dies and the money stays.");
        Check.That(alone.TryGetWinner() == new PlayerId(2), "The last player with objects has won.");
    }
}
