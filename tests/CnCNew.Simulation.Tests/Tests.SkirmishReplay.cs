using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

/// <summary>Phase 24, step 5: the replay of a skirmish carries its match description, and its playback lets the computer players decide again and checks them against the recording.</summary>
static partial class Tests
{
    /// <summary>A recorded match with a human slot that gives orders (group moves among them) and a computer player.</summary>
    private static (SyntheticFixture Fixture, TerrainMap Terrain, CnCNew.Data.World.MapData Map, SkirmishMatchSession Session, ReplayFile Replay) RecordHumanAgainstAi(long ticks)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: SkirmishFiles());
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish");
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var options = new SkirmishMatchOptions(0xC0FFEE06, 7, ticks, 60, 500, 500, 0);
        var session = SkirmishMatchSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map.Objects,
            [new SkirmishSlot(participants[0], IsHuman: true), new SkirmishSlot(participants[1 % participants.Length])]);
        var human = session.Players[0].Id;
        while (!session.IsOver)
        {
            var tick = session.World.Tick;
            if (tick > 0 && tick % 97 == 0)
            {
                var units = session.World.Entities.Where(entity => entity.Owner == human && !entity.Template.IsKindOf("STRUCTURE") && !entity.EffectivelyDead).Select(entity => entity.Id).ToImmutableArray();
                if (units.Length > 0)
                {
                    var x = Fixed.FromInt(120 + (int)(tick % 5) * 30);
                    session.Issue(human, (sequence, at) => new GroupMoveCommand(human, sequence, at, units, x, Fixed.FromInt(240), false));
                }
            }
            session.Step();
        }
        return (fixture, terrain, map, session, ReplayFormat.Read(ReplayFormat.Write(session.ToReplay())));
    }

    public static void SkirmishReplayPlaysTheMatchAndAuditsTheAi()
    {
        var (fixture, terrain, map, session, replay) = RecordHumanAgainstAi(6_000);
        Check.That(ReplayFormat.CurrentVersion == 14, "The replay format with the match description in its header is version 14.");
        var description = SkirmishReplay.DescriptionOf(replay.Header);
        Check.That(description is not null && description.Equals(session.Description), "The header of a skirmish replay is the match description, byte for byte.");
        var humanCommands = replay.Commands.Count(command => command.Player == session.Players[0].Id);
        var aiCommands = replay.Commands.Count(command => command.Player == session.Players[1].Id);
        Check.That(humanCommands >= 3 && aiCommands >= 30, $"The recording holds orders of the human ({humanCommands}) and of the computer player ({aiCommands}).");

        var played = SkirmishReplay.Play(replay, fixture.Templates, fixture.Fingerprint, terrain, map.Objects);
        Check.That(played.FirstDeviation is null, $"The playback produces the recorded commands of the system and the computer player ({played.FirstDeviation}).");
        Check.That(played.HumanCommands == humanCommands && played.AuditedCommands == replay.Commands.Length - humanCommands, "Human commands are fed in, every other command is audited.");
        Check.That(played.FinalWorldHash == session.World.StateHash && played.FinalSessionHash == session.StateHash() && played.EndTick == session.World.Tick, "The playback ends in the state of the recorded match.");
        var bare = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, terrain: terrain);
        Check.That(bare.FinalHash == played.FinalWorldHash, "The playback with the AI and the playback of the bare world end in the same world.");

        // One recorded order of the computer player changed: the audit names that tick and that player.
        var target = replay.Commands.Where(command => command.Player == session.Players[1].Id && command is QueueProductionCommand or ConstructCommand).ElementAt(3);
        var forged = replay with { Commands = replay.Commands.Remove(target) };
        var dropped = SkirmishReplay.Play(forged, fixture.Templates, fixture.Fingerprint, terrain, map.Objects);
        Check.That(dropped.FirstDeviation is { } missing && missing.Tick == target.Tick && missing.Player == target.Player && dropped.EndTick == target.Tick + 1,
            $"A recorded order of the computer player that is missing is reported at its tick and player, and the playback stops there ({dropped.FirstDeviation}; expected tick {target.Tick}).");

        // One order of the human removed: the computer player meets another world and decides differently somewhere later - the audit shows where.
        var firstHuman = replay.Commands.First(command => command.Player == session.Players[0].Id);
        var withoutHuman = replay with { Commands = replay.Commands.Remove(firstHuman) };
        var changed = SkirmishReplay.Play(withoutHuman, fixture.Templates, fixture.Fingerprint, terrain, map.Objects, stopAtFirstDeviation: false);
        Check.That(changed.HumanCommands == humanCommands - 1, "The changed recording feeds one order fewer.");
        Check.That(changed.FinalWorldHash != session.World.StateHash, "Without one order of the human the match is another match.");

        // A replay without a description (a bare world fixture) is refused by this playback, and a description for other definitions by its fingerprint.
        Check.Throws<ReplayRejectedException>(() => SkirmishReplay.Play(replay with { Header = replay.Header with { MatchDescription = string.Empty } }, fixture.Templates, fixture.Fingerprint, terrain, map.Objects), "no match description");
        Check.Throws<ReplayRejectedException>(() => SkirmishReplay.Play(replay, fixture.Templates, new string('B', 64), terrain, map.Objects), "different definitions");
        var other = session.Description with { DefinitionFingerprint = new string('B', 64) };
        Check.Throws<ReplayRejectedException>(() => SkirmishReplay.Play(replay with { Header = replay.Header with { MatchDescription = Convert.ToBase64String(other.Encode()) } }, fixture.Templates, fixture.Fingerprint, terrain, map.Objects), "cannot be built here");
    }
}
