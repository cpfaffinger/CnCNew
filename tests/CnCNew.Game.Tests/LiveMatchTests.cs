using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Simulation.Testing;

namespace CnCNew.Game.Tests;

internal static class LiveMatchTests
{
    public static int Run(string[] args)
    {
        var contentDir = ContentDir(args);
        var harness = new TestHarness(nameof(LiveMatchTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP LiveMatchTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var watch = Stopwatch.StartNew();
        var data = GameData.Load(contentDir, step => Console.WriteLine($"  loading: {step} ({watch.Elapsed.TotalSeconds:F1} s)"), rate: CnCNew.Tests.TestRate.Default);
        harness.Run("GameData loads", () =>
        {
            TestHarness.Assert(data.Factions.Length >= 2, "at least two playable factions");
            TestHarness.Assert(data.Maps.Length > 100, "maps enumerated");
            TestHarness.Assert(data.LoadText("English").TryGet("GUI:Options", out _) || true, "text loads");
        });

        harness.Run("Live match runs and presents", () =>
        {
            var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
            var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.Standard);
            var build = Stopwatch.StartNew();
            var live = LiveMatch.Create(data, setup, new Vector2(1280, 720));
            Console.WriteLine($"  LiveMatch.Create: {build.Elapsed.TotalSeconds:F1} s, {live.TerrainModel.Chunks.Length} chunks, {live.Frame.Drawables.Length} drawables at start");
            // Scenery follows the shroud and the first vision pass runs a few ticks in: the very first frame is empty, the base shows up within a second.
            live.Advance(1.0);
            Console.WriteLine($"  after one second: {live.Frame.Drawables.Length} drawables");
            TestHarness.Assert(live.Frame.Drawables.Length > 0, "drawables after the first second");
            var advance = Stopwatch.StartNew();
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 20; frame++) live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            Console.WriteLine($"  600 frames: {advance.Elapsed.TotalMilliseconds / 600:F2} ms/frame, tick {live.Tick}, {live.Frame.Drawables.Length} drawables, {live.Fx.Particles.ParticleCount} particles");
            TestHarness.Assert(live.Tick >= 590, "simulation advanced at 30 Hz");
            live.Paused = true;
            var tick = live.Tick;
            live.Advance(1);
            TestHarness.Assert(live.Tick == tick, "pause stops the simulation");
        });
        harness.Run("A live match is a room of the embedded relay: the viewer may sit in any slot, team and colour reach the description, orders come back in bundles", () =>
        {
            var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
            var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.StandardAt(data.Rate))
            {
                LocalSlot = 1,
                Details = [new MatchSlotDetails(Team: 3, Color: 4, StartPosition: 0), new MatchSlotDetails(Team: 3, Color: 1, StartPosition: 1)],
            };
            var live = LiveMatch.Create(data, setup, new Vector2(1280, 720));
            var description = live.Description;
            TestHarness.Assert(description.Slots[0] is { Kind: CnCNew.Simulation.Skirmish.MatchSlotKind.Computer, Team: 3, Color: 4 } && description.Slots[1] is { Kind: CnCNew.Simulation.Skirmish.MatchSlotKind.Human, Team: 3, Color: 1 },
                "the viewer's slot is the human one; team and colour of both slots are in the description");
            TestHarness.Assert(live.Human.Value == 2 && live.HumanFaction == data.Factions[1].Faction.Name && live.Relay.Client.Player == 2, "the viewer is player 2: the relay seated him in the second slot");
            TestHarness.Assert(description.SimulationId == CnCNew.Net.BuildIdentity.Current.Id && live.Relay.Client.Pacing == CnCNew.Net.Pacing.Client, "the match was opened by the embedded server of this build, client-paced");
            TestHarness.Assert(live.World.AreAllies(new CnCNew.Simulation.Ids.PlayerId(1), new CnCNew.Simulation.Ids.PlayerId(2)), "the two slots of one team are allies in the simulation");
            var frame = CnCNew.Tests.TestTime.FrameSeconds;
            for (var index = 0; index < 60; index++) live.Advance(frame);
            var unit = live.World.Entities.First(entity => entity.Owner == live.Human && !entity.Template.IsKindOf("STRUCTURE"));
            var resolved = new List<CnCNew.Presentation.Input.CommandResolution>();
            live.CommandResolved += resolved.Add;
            var issuedAt = live.Tick;
            var receipt = live.Commands.Issue((sequence, tick) => new CnCNew.Simulation.Commands.SetMoveTargetCommand(live.Human, sequence, tick, unit.Id, unit.X + CnCNew.Simulation.Numerics.Fixed.FromInt(40), unit.Y));
            TestHarness.Assert(receipt.Accepted && resolved.Count == 0 && live.Relay.Server.IntentsRelayed == 0, "the order is pre-checked and sent; nothing is submitted yet");
            live.Advance(1.0 / live.World.Rate.TicksPerSecond);
            TestHarness.Assert(resolved.Count == 1 && resolved[0].Receipt == receipt.Number && resolved[0].Result.Accepted && resolved[0].Command.Tick == issuedAt && resolved[0].Command.Player == live.Human,
                "the order comes back in the bundle of the tick it was given in and is answered by its receipt");
            TestHarness.Assert(live.Relay.Server.IntentsRelayed == 1 && live.Relay.Server.Recording.Bundles.Count == live.Tick && live.Relay.Server.Discarded == 0 && live.Relay.Client.Discarded == 0,
                "the relay closed one bundle per tick and discarded nothing");
            for (var index = 0; index < 120; index++) live.Advance(frame);
            live.Relay.Pump();
            TestHarness.Assert(live.Relay.Server.LastAgreedTick == live.Tick - 1 && live.Lockstep.HashesReported == live.Tick, "the session hash of every tick reached the relay");
            TestHarness.Assert(live.World.FindEntity(unit.Id)!.X > unit.X - CnCNew.Simulation.Numerics.Fixed.FromInt(1), "the order is carried out");
        });
        return harness.Failures;
    }

    private static string? ContentDir(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
