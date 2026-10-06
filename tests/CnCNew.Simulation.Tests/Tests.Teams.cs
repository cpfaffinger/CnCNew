using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>Players 1 and 2 form team 7, player 3 is alone in team 9, player 4 has no team.</summary>
    private static readonly ImmutableArray<PlayerConfiguration> TeamPlayers =
    [
        new(new PlayerId(1), "Alpha", Team: 7), new(new PlayerId(2), "Beta", Team: 7),
        new(new PlayerId(3), "Gamma", Team: 9), new(new PlayerId(4), "Delta"),
    ];

    private static IReadOnlyDictionary<string, string> TeamFiles() => new Dictionary<string, string>(VeterancyFiles())
    {
        ["Data/INI/Object/TeamsFixture.ini"] = """
        Object Prize
          KindOf = SELECTABLE INFANTRY
          BuildCost = 100
          ExperienceValue = 10 30 90 300
          Body = ActiveBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static SimulationWorld TeamsWorld(IReadOnlyDictionary<string, string> files, out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: files);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TeamPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    private static Relationship[,] RelationMatrix(SimulationWorld world)
    {
        var matrix = new Relationship[5, 5];
        for (var from = 0; from <= 4; from++)
            for (var to = 0; to <= 4; to++) matrix[from, to] = world.RelationshipOf(new PlayerId(from), new PlayerId(to));
        return matrix;
    }

    /// <summary>The relation matrix from teams, the directed override command, its validation, and the round trips through snapshot and replay file.</summary>
    public static void TeamsRelationshipMatrix()
    {
        var world = TeamsWorld(TeamFiles(), out var fixture, out var feeder);
        var matrix = RelationMatrix(world);
        for (var from = 1; from <= 4; from++)
            for (var to = 1; to <= 4; to++)
            {
                var expected = from == to || (from <= 2 && to <= 2) ? Relationship.Allies : Relationship.Enemies;
                Check.That(matrix[from, to] == expected, $"Team default {from} -> {to}: expected {expected}, got {matrix[from, to]}.");
            }
        Check.That(Enumerable.Range(0, 5).All(other => matrix[0, other] == (other == 0 ? Relationship.Allies : Relationship.Neutral) && (other == 0 || matrix[other, 0] == Relationship.Neutral)),
            "The system authority (neutral objects) is neutral to every player and its own ally.");
        Check.That(world.RelationshipOf(new PlayerId(1), new PlayerId(99)) == Relationship.Neutral, "An unknown player is neutral.");
        Check.That(world.AreAllies(new PlayerId(1), new PlayerId(2)) && !world.AreAllies(new PlayerId(1), new PlayerId(1)) && !world.AreAllies(new PlayerId(1), new PlayerId(3)),
            "AreAllies needs two distinct mutual allies.");
        Check.That(world.AreEnemies(new PlayerId(1), new PlayerId(4)) && !world.AreEnemies(new PlayerId(1), new PlayerId(2)) && !world.AreEnemies(new PlayerId(1), PlayerId.System)
            && !world.AreEnemies(new PlayerId(1), new PlayerId(1)), "AreEnemies is false for allies, itself and neutral objects.");

        // Validation.
        Check.That(world.Submit(new SetRelationshipCommand(new PlayerId(1), 1, 0, new PlayerId(1), new PlayerId(3), Relationship.Neutral)).Reason == CommandRejection.NotPermitted,
            "A regular player may not rewrite relations.");
        var system = PlayerId.System;
        Check.That(world.Submit(new SetRelationshipCommand(system, 1, 0, new PlayerId(1), new PlayerId(1), Relationship.Neutral)).Reason == CommandRejection.InvalidArguments, "A relation to oneself is refused.");
        Check.That(world.Submit(new SetRelationshipCommand(system, 2, 0, system, new PlayerId(1), Relationship.Neutral)).Reason == CommandRejection.InvalidArguments, "The system authority has no relations to set.");
        Check.That(world.Submit(new SetRelationshipCommand(system, 3, 0, new PlayerId(1), new PlayerId(9), Relationship.Neutral)).Reason == CommandRejection.UnknownPlayer, "An unknown player is refused.");
        Check.That(world.Submit(new SetRelationshipCommand(system, 4, 0, new PlayerId(1), new PlayerId(3), (Relationship)7)).Reason == CommandRejection.InvalidArguments, "An undefined relationship is refused.");
        Check.That(world.Submit(new SetRelationshipCommand(system, 5, 0, new PlayerId(1), new PlayerId(3), Relationship.Neutral)).Accepted, "A valid override is accepted.");
        Check.That(world.Submit(new SetRelationshipCommand(system, 6, 0, new PlayerId(1), new PlayerId(2), Relationship.Enemies)).Accepted, "Cancelling one direction of an alliance is accepted.");
        world.Step();

        Check.That(world.RelationshipOf(new PlayerId(1), new PlayerId(3)) == Relationship.Neutral && world.RelationshipOf(new PlayerId(3), new PlayerId(1)) == Relationship.Enemies,
            "Relations are directed: the reverse direction keeps its team default.");
        Check.That(!world.AreEnemies(new PlayerId(1), new PlayerId(3)) && world.AreEnemies(new PlayerId(3), new PlayerId(1)), "Neutral is not enemy, but the other side still sees an enemy.");
        Check.That(world.AreEnemies(new PlayerId(1), new PlayerId(2)) && !world.AreEnemies(new PlayerId(2), new PlayerId(1)) && !world.AreAllies(new PlayerId(1), new PlayerId(2)) && !world.AreAllies(new PlayerId(2), new PlayerId(1)),
            "A one-sided cancellation ends the mutual alliance.");

        // Snapshot: teams and overrides survive, and two restored copies stay identical.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash, "Teams and relation overrides round-trip through a snapshot.");
        var before = RelationMatrix(world);
        var after = RelationMatrix(restored);
        for (var from = 0; from <= 4; from++)
            for (var to = 0; to <= 4; to++) Check.That(before[from, to] == after[from, to], $"Relation {from} -> {to} survives the snapshot.");
        var forkFeeder = feeder.ForkTo(restored);
        feeder.Spawn(1, "Tank", x: 1);
        forkFeeder.Spawn(1, "Tank", x: 1);
        for (var i = 0; i < world.Frames(30); i++) Check.That(world.Step().StateHash == restored.Step().StateHash, "The restored world continues identically (tick " + i + ").");

        // Replay file: teams in the header, the command in the stream.
        var command = new SetRelationshipCommand(system, 7, 12, new PlayerId(3), new PlayerId(4), Relationship.Allies);
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, fixture.Fingerprint, SyntheticFixture.FixtureId, 1, new WorldId(1), TeamPlayers, 20, 5);
        ImmutableArray<SimCommand> commands = [command];
        var read = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, commands, [])));
        Check.That(read.Commands.SequenceEqual(commands), "SetRelationshipCommand survives the replay file.");
        Check.That(read.Header.Players.SequenceEqual(TeamPlayers), "The replay header keeps every player's team.");
    }

    /// <summary>Spawns a one-hit-point Prize for <paramref name="victimOwner"/> and has the recruit of player 1 shoot it dead.</summary>
    private static void KillPrize(SimulationWorld world, Feeder feeder, uint shooter, int victimOwner)
    {
        feeder.Spawn(victimOwner, "Prize", x: 10);
        world.Step();
        var prize = world.Entities.Where(value => value.Template.Name == "Prize" && value.Owner.Value == victimOwner).OrderBy(value => value.Id.Value).Last();
        for (var tick = 0; tick < world.Frames(200) && world.FindEntity(prize.Id) is { } alive && alive.Body!.Health > Fixed.Zero; tick++)
        {
            feeder.FireWeapon(1, shooter, prize.Id.Value);
            world.Step();
        }
        Check.That(world.FindEntity(prize.Id) is not { } left || left.Body!.Health <= Fixed.Zero, "The prize dies.");
    }

    /// <summary><c>Object::scoreTheKill</c>: experience and bounty need the killer's owner to see the victim's owner as an enemy.</summary>
    public static void TeamsKillScoring()
    {
        var world = TeamsWorld(TeamFiles(), out _, out var feeder);
        feeder.Spawn(1, "Recruit", x: 0);
        world.Step();
        var recruit = world.FindEntity(new EntityId(IdOf(world, "Recruit")))!;
        world.RaiseCashBounty(new PlayerId(1), Fixed.FromDecimal(0.5m));
        var money = world.MoneyOf(new PlayerId(1));

        KillPrize(world, feeder, recruit.Id.Value, 2);
        Check.That(recruit.Experience == 0 && world.MoneyOf(new PlayerId(1)) == money, "Killing a team mate scores neither experience nor bounty.");

        KillPrize(world, feeder, recruit.Id.Value, 3);
        Check.That(recruit.Experience == 10 && world.MoneyOf(new PlayerId(1)) == money + Fixed.FromInt(50), "Killing an enemy of another team pays 10 experience and half of the 100 cost.");

        KillPrize(world, feeder, recruit.Id.Value, 4);
        Check.That(recruit.Experience == 20 && world.MoneyOf(new PlayerId(1)) == money + Fixed.FromInt(100), "Killing an enemy without a team scores as well.");

        feeder.SetRelationship(1, 3, Relationship.Neutral);
        world.Step();
        KillPrize(world, feeder, recruit.Id.Value, 3);
        Check.That(recruit.Experience == 20 && world.MoneyOf(new PlayerId(1)) == money + Fixed.FromInt(100), "Neutral kills score nothing (the reference needs ENEMIES).");

        feeder.SetRelationship(1, 3, Relationship.Enemies);
        feeder.SetRelationship(1, 2, Relationship.Enemies);
        world.Step();
        KillPrize(world, feeder, recruit.Id.Value, 2);
        Check.That(recruit.Experience == 30 && world.MoneyOf(new PlayerId(1)) == money + Fixed.FromInt(150), "After a declared enmity the former team mate scores again.");
    }

    /// <summary>The match ends when all survivors are mutual allies of the first survivor; a one-sided relation breaks it.</summary>
    public static void TeamsVictoryConditions()
    {
        var world = TeamsWorld(TeamFiles(), out _, out var feeder);
        for (var player = 1; player <= 4; player++) feeder.Spawn(player, "Tank", x: player);
        world.Step();
        uint TankOf(int owner) => world.Entities.Single(value => value.Owner.Value == owner).Id.Value;
        Check.That(world.TryGetWinner() is null && world.TryGetWinningAlliance().IsEmpty, "Four players in three camps: undecided.");

        feeder.Kill(TankOf(4));
        world.Step();
        Check.That(world.TryGetWinner() is null, "Player 3 is still an enemy of the team.");

        feeder.Kill(TankOf(3));
        world.Step();
        var alliance = world.TryGetWinningAlliance();
        Check.That(alliance.SequenceEqual([new PlayerId(1), new PlayerId(2)]) && world.TryGetWinner() == new PlayerId(1), "Team 7 wins together; the lowest id is reported as the winner.");

        feeder.SetRelationship(2, 1, Relationship.Neutral);
        world.Step();
        Check.That(world.TryGetWinner() is null, "The alliance test is mutual: one side turning neutral revokes the victory.");

        feeder.SetRelationship(2, 1, Relationship.Allies);
        feeder.Kill(TankOf(1));
        world.Step();
        Check.That(world.TryGetWinningAlliance().SequenceEqual([new PlayerId(2)]) && world.TryGetWinner() == new PlayerId(2), "The last player of a team wins alone.");

        feeder.Kill(TankOf(2));
        world.Step();
        Check.That(world.TryGetWinner() is null && world.TryGetWinningAlliance().IsEmpty, "Nobody left: no winner.");
    }

    /// <summary><c>OpenContain</c> gates by the container owner's relationship to the candidate (allies, enemies, neutral).</summary>
    public static void TeamsContainRelationship()
    {
        var world = TeamsWorld(RetailModuleFiles(), out _, out var feeder);
        feeder.Spawn(1, "Barracks");
        feeder.Spawn(2, "Rebel", x: 3, y: 3);
        feeder.Spawn(3, "Rebel", x: 3, y: 3);
        feeder.Spawn(3, "Rebel", x: 3, y: 3);
        world.Step();
        var barracks = world.Entities.Single(value => value.Template.Name == "Barracks");
        var teamMate = world.Entities.Single(value => value.Template.Name == "Rebel" && value.Owner.Value == 2);
        var enemies = world.Entities.Where(value => value.Template.Name == "Rebel" && value.Owner.Value == 3).OrderBy(value => value.Id.Value).ToArray();
        long Refused() => world.RejectionCounts[(int)CommandRejection.CannotEnterContainer];

        feeder.EnterContainer(2, teamMate.Id.Value, barracks.Id.Value);
        world.Step();
        Check.That(teamMate.ContainedBy == barracks.Id && Refused() == 0, "A team mate of the barracks' owner is an ally: AllowAlliesInside = Yes admits it.");

        feeder.EnterContainer(3, enemies[0].Id.Value, barracks.Id.Value);
        world.Step();
        Check.That(!enemies[0].ContainedBy.IsValid && Refused() == 1, "A player of another team is an enemy: AllowEnemiesInside = No refuses it.");

        feeder.SetRelationship(1, 3, Relationship.Neutral);
        world.Step();
        feeder.EnterContainer(3, enemies[0].Id.Value, barracks.Id.Value);
        world.Step();
        Check.That(!enemies[0].ContainedBy.IsValid && Refused() == 2, "A neutral relation is refused by AllowNeutralInside = No.");

        feeder.SetRelationship(1, 3, Relationship.Allies);
        world.Step();
        feeder.EnterContainer(3, enemies[1].Id.Value, barracks.Id.Value);
        world.Step();
        Check.That(enemies[1].ContainedBy == barracks.Id && Refused() == 2, "An explicit alliance admits the former enemy.");
    }
}
