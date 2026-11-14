using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The debug options of a match (<see cref="MatchDebugOptions"/>): quick building, neutral buildings that come back and general points at the start. They are part of the match
/// description, so a lockstep match, a save and a replay play them alike, and a match without them is the original game.
/// </summary>
static partial class Tests
{
    private const string DebugOptionsIni = """
        Science SCIENCE_Debug
          SciencePurchasePointCost = 2
        End
        Object SlowUnit
          KindOf = SELECTABLE INFANTRY
          BuildCost = 10
          BuildTime = 20
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object DebugFactory
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
        Upgrade Upgrade_DebugPlayer
          Type = PLAYER
          BuildCost = 100
          BuildTime = 4
        End
        CommandButton Cmd_DebugPlayer
          Command = PLAYER_UPGRADE
          Upgrade = Upgrade_DebugPlayer
        End
        CommandSet DebugLabSet
          1 = Cmd_DebugPlayer
        End
        Object DebugLab
          KindOf = STRUCTURE
          CommandSet = DebugLabSet
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
        End
        """;

    private static IReadOnlyDictionary<string, string> DebugOptionFiles() => new Dictionary<string, string>(ConstructionFiles())
    {
        ["Data/INI/Object/DebugOptions.ini"] = DebugOptionsIni + Environment.NewLine + MapObjectsIni,
    };

    private static SimulationWorld DebugWorld(SimulationRate rate, int buildPercent, int respawnSeconds, out Feeder feeder, out SyntheticFixture fixture, out TerrainMap? terrain, bool withTerrain = true)
    {
        terrain = withTerrain ? NavTerrain() : null;
        var (world, made, built) = RatedWorld(rate, DebugOptionFiles(), terrain);
        fixture = built;
        feeder = made;
        if (buildPercent != 100 || respawnSeconds != 0) world.ConfigureMatchOptions(buildPercent, respawnSeconds);
        return world;
    }

    /// <summary>
    /// Quick build "for human players only" (and the levels 50, 75 and 90 % less): with a computer player (1) and a human (2) each producing the same 20 s unit, the human's takes the
    /// scaled time (build times are whole frames of the original; the percentages 50, 25 and 10 of 600 frames are exact: 300, 150, 60), the computer's the normal 600 frames - at 30, 60
    /// and 120 Hz. For everybody, both are scaled.
    /// </summary>
    public static void QuickBuildForHumanPlayersOnly()
    {
        foreach (var (rate, _) in WorldRates)
        {
            (long Computer, long Human) Frames(int percent, bool humansOnly)
            {
                var terrain = NavTerrain();
                var fixture = new SyntheticFixture(rate, files: DebugOptionFiles());
                ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha", IsComputer: true), new(new PlayerId(2), "Beta")];
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
                var feeder = new Feeder(world);
                if (percent != 100) world.ConfigureMatchOptions(percent, 0, 0, false, humansOnly);
                feeder.Spawn(1, "DebugFactory", x: 100, y: 100);
                feeder.Spawn(2, "DebugFactory", x: 100, y: 150);
                feeder.GiveMoney(1, 1000);
                feeder.GiveMoney(2, 1000);
                world.Step();
                var factories = world.Entities.Where(value => value.Template.Name == "DebugFactory").ToDictionary(value => value.Owner.Value, value => value.Id.Value);
                var ordered = world.Tick;
                feeder.QueueProduction(1, factories[1], "SlowUnit");
                feeder.QueueProduction(2, factories[2], "SlowUnit");
                long computer = -1, human = -1;
                for (var step = 0; step < world.Frames(700) && (computer < 0 || human < 0); step++)
                {
                    world.Step();
                    if (computer < 0 && world.Entities.Any(value => value.Template.Name == "SlowUnit" && value.Owner.Value == 1)) computer = world.InFrames(world.Tick - ordered);
                    if (human < 0 && world.Entities.Any(value => value.Template.Name == "SlowUnit" && value.Owner.Value == 2)) human = world.InFrames(world.Tick - ordered);
                }
                return (computer, human);
            }

            bool Near(long frames, long expected) => Math.Abs(frames - expected) <= 3;
            var normal = Frames(100, false);
            Check.That(Near(normal.Computer, 600) && Near(normal.Human, 600), $"{rate}: both build in 600 frames without the option ({normal}).");
            foreach (var (percent, expected) in new[] { (MatchDebugOptions.QuickBuildPercent, 60L), (MatchDebugOptions.HalfBuildPercent, 300L), (MatchDebugOptions.QuarterBuildPercent, 150L) })
            {
                var everybody = Frames(percent, false);
                Check.That(Near(everybody.Computer, expected) && Near(everybody.Human, expected), $"{rate}: at {percent} % for everybody both take {expected} frames ({everybody}).");
                if (rate != SimulationRate.Hz60 && percent != MatchDebugOptions.QuickBuildPercent) continue;
                var humans = Frames(percent, true);
                Check.That(Near(humans.Computer, 600) && Near(humans.Human, expected), $"{rate}: at {percent} % for human players only the human takes {expected} frames, the computer 600 ({humans}).");
            }
        }
    }

    /// <summary>
    /// The debug money buttons (#336): the command is refused (named, counted) in a match without the option; with it the player who gives it, or every player, computer included, gets
    /// 10,000, at 30, 60 and 120 Hz, and the deposit is no income. In a skirmish session the recording replays to the live hashes with the command in it, and the computer player never gives it.
    /// </summary>
    public static void DebugMoneyButtonsGiveMoneyOnlyWhenOn()
    {
        foreach (var (rate, _) in WorldRates)
        {
            SimulationWorld Make(bool on)
            {
                var terrain = NavTerrain();
                var fixture = new SyntheticFixture(rate, files: DebugOptionFiles());
                ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta", IsComputer: true)];
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
                if (on) world.ConfigureMatchOptions(100, 0, 0, false, false, true);
                return world;
            }

            var off = Make(false);
            var refused = off.Submit(new DebugGiveMoneyCommand(new PlayerId(1), 1, off.Tick, false));
            Check.That(!refused.Accepted && refused.Reason == CommandRejection.DebugOptionOff && off.Check(new DebugGiveMoneyCommand(new PlayerId(1), 2, off.Tick, true)).Reason == CommandRejection.DebugOptionOff, $"{rate}: refused without the option.");
            off.Step();
            Check.That(off.MoneyOf(new PlayerId(1)) == off.MoneyOf(new PlayerId(2)), $"{rate}: nothing was paid without the option.");

            var world = Make(true);
            var start = world.MoneyOf(new PlayerId(1));
            Check.That(world.Submit(new DebugGiveMoneyCommand(new PlayerId(1), 1, world.Tick, false)).Accepted, $"{rate}: accepted with the option.");
            world.Step();
            Check.That(world.MoneyOf(new PlayerId(1)) == start + Fixed.FromInt(10_000) && world.MoneyOf(new PlayerId(2)) == start, $"{rate}: only the giver is paid ({world.MoneyOf(new PlayerId(1))}, {world.MoneyOf(new PlayerId(2))}).");
            Check.That(world.Submit(new DebugGiveMoneyCommand(new PlayerId(2), 1, world.Tick, true)).Accepted, $"{rate}: the all button is accepted.");
            world.Step();
            Check.That(world.MoneyOf(new PlayerId(1)) == start + Fixed.FromInt(20_000) && world.MoneyOf(new PlayerId(2)) == start + Fixed.FromInt(10_000), $"{rate}: every player is paid, the computer too.");
            Check.That(!world.Submit(new DebugGiveMoneyCommand(PlayerId.System, 1, world.Tick, true)).Accepted, $"{rate}: the system is no giver.");
            Check.That(CommandTypes.IsPlayerOrder(CommandTypes.DebugGiveMoney) && CommandTypes.Version == 29 && ReplayFormat.CurrentVersion == 28, $"{rate}: a player order; command table 29, replay 28.");
            var decoded = ReplayFormat.DecodeCommand(ReplayFormat.EncodeCommand(new DebugGiveMoneyCommand(new PlayerId(1), 3, 9, true)));
            Check.That(decoded is DebugGiveMoneyCommand { All: true, Sequence: 3, Tick: 9 }, $"{rate}: the command survives the replay codec.");
        }

        var (fixtureS, terrainS) = (new SyntheticFixture(TestRate.Default, files: DebugSessionFiles()), (TerrainMap?)null);
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        terrainS = TerrainMap.Build(map, "skirmish");
        var participants = ReferenceMatchScenario.Participants(fixtureS.Templates, out _);
        var plain = SkirmishMatchSession.Describe(new SkirmishMatchOptions(0xC0FFEE06, 7, TestRate.Default.Time.OriginalFrames(900), 60, 500, 500, 0), fixtureS.Templates, fixtureS.Fingerprint, terrainS,
            [..participants.Select(participant => new SkirmishSlot(participant))], "Maps/Synthetic/Synthetic.map");
        var human = plain with { Slots = plain.Slots.SetItem(0, plain.Slots[0] with { Kind = MatchSlotKind.Human }), Debug = new MatchDebugOptions(MoneyButtons: true) };
        var session = SkirmishMatchSession.Start(human, fixtureS.Templates, terrainS, [], definitionFingerprint: fixtureS.Fingerprint);
        session.Step();
        var one = new PlayerId(1);
        var before = (session.World.MoneyOf(one), session.World.MoneyOf(new PlayerId(2)));
        Check.That(session.Submit(new DebugGiveMoneyCommand(one, 1, session.World.Tick, true)).Accepted, "The session takes the command.");
        session.Step();
        Check.That(session.World.MoneyOf(one) >= before.Item1 + Fixed.FromInt(9_000) && session.World.MoneyOf(new PlayerId(2)) >= before.Item2 + Fixed.FromInt(9_000), "Both players got the gift in the session.");
        while (!session.IsOver) session.Step();
        Check.That(!session.RecordedCommands.Any(command => command is DebugGiveMoneyCommand && command.Player != one), "The computer player never gave it.");
        var read = ReplayFormat.Read(ReplayFormat.Write(session.ToReplay()));
        var bare = ReplayRunner.Run(read, fixtureS.Templates, fixtureS.Fingerprint, terrain: terrainS);
        Check.That(bare.FinalHash == session.World.StateHash, "The bare replay with the command ends in the live hash.");
        var played = SkirmishReplay.Play(read, fixtureS.Templates, fixtureS.Fingerprint, terrainS, []);
        Check.That(played.FirstDeviation is null && played.FinalSessionHash == session.StateHash(), "Played as a match, the replay deviates nowhere.");
    }

    /// <summary>The debug options are in the description's bytes and its fingerprint; out-of-range values and the older encoding are refused.</summary>
    public static void MatchDebugOptionsAreInTheDescription()
    {
        var (fixture, terrain, _) = StartSyntheticMatch(100);
        var description = SyntheticDescription(fixture, terrain);
        Check.That(MatchDescription.EncodingVersion == 6 && description.Debug.IsDefault && description.Debug == MatchDebugOptions.None, "A description made without options states the original game (encoding 6).");
        var variants = new[]
        {
            description with { Debug = new MatchDebugOptions(BuildTimePercent: MatchDebugOptions.QuickBuildPercent) },
            description with { Debug = new MatchDebugOptions(NeutralRespawnSeconds: 120) },
            description with { Debug = new MatchDebugOptions(StartingGeneralPoints: 5) },
            description with { Debug = new MatchDebugOptions(PlaceBuildingsInFog: true) },
            description with { Debug = new MatchDebugOptions(MatchDebugOptions.QuickBuildPercent, 120, 5, true) },
            description with { Debug = new MatchDebugOptions(MatchDebugOptions.QuickBuildPercent, QuickBuildHumansOnly: true) },
            description with { Debug = new MatchDebugOptions(MatchDebugOptions.HalfBuildPercent) },
            description with { Debug = new MatchDebugOptions(MoneyButtons: true) },
            description with { Debug = new MatchDebugOptions(RechargePercent: MatchDebugOptions.QuarterBuildPercent) },
            description with { Debug = new MatchDebugOptions(RechargePercent: MatchDebugOptions.HalfBuildPercent, QuickBuildHumansOnly: true) },
            description with { Debug = new MatchDebugOptions(MatchDebugOptions.QuarterBuildPercent) },
        };
        var seen = new HashSet<string>(StringComparer.Ordinal) { description.Fingerprint };
        foreach (var changed in variants)
        {
            Check.That(seen.Add(changed.Fingerprint), "Every debug option changes the fingerprint of the description.");
            var decoded = MatchDescription.Decode(changed.Encode());
            Check.That(decoded.Debug == changed.Debug && decoded.Fingerprint == changed.Fingerprint && decoded.Encode().AsSpan().SequenceEqual(changed.Encode()), "The options survive the description's bytes.");
            Check.That(changed.Resolve(fixture.Templates, terrain).Slots.Length == 2, "A description with options resolves.");
        }
        Check.That(variants[4].Equals(MatchDescription.Decode(variants[4].Encode())) && !variants[4].Equals(variants[0]), "Descriptions with other options are not equal.");

        Check.Throws<ArgumentException>(() => (description with { Debug = new MatchDebugOptions(QuickBuildHumansOnly: true) }).Resolve(fixture.Templates, terrain), "recharge level");
        Check.Throws<ArgumentException>(() => (description with { Debug = new MatchDebugOptions(RechargePercent: 5000) }).Resolve(fixture.Templates, terrain), "recharge time");
        var bytes = description.Encode();
        var older = bytes.ToArray();
        older[8] = 4;
        Check.Throws<FormatException>(() => MatchDescription.Decode(older), "Unsupported match description version 4");
        Check.Throws<ArgumentException>(() => (description with { Debug = new MatchDebugOptions(BuildTimePercent: 0) }).Resolve(fixture.Templates, terrain), "build time");
        Check.Throws<ArgumentException>(() => (description with { Debug = new MatchDebugOptions(BuildTimePercent: 5000) }).Resolve(fixture.Templates, terrain), "build time");
        Check.Throws<ArgumentException>(() => (description with { Debug = new MatchDebugOptions(NeutralRespawnSeconds: -1) }).Resolve(fixture.Templates, terrain), "respawn time");
        Check.Throws<ArgumentException>(() => (description with { Debug = new MatchDebugOptions(StartingGeneralPoints: 5_000_000) }).Resolve(fixture.Templates, terrain), "general points");
    }

    /// <summary>
    /// A world with the build time at 10 % makes a unit, researches an upgrade and builds a structure in a tenth of the time (before the power ratio), at 30, 60 and 120 Hz; the cost,
    /// the refund and the original game are untouched, and a world without the option writes nothing new into its state.
    /// </summary>
    public static void QuickBuildTakesATenthOfTheTime()
    {
        foreach (var (rate, _) in WorldRates)
        {
            long UnitFrames(int percent)
            {
                var world = DebugWorld(rate, percent, 0, out var feeder, out _, out _);
                feeder.Spawn(1, "DebugFactory", x: 100, y: 100);
                feeder.GiveMoney(1, 1000);
                world.Step();
                var ordered = world.Tick;
                var ticks = SpawnTicks(world, feeder, "SlowUnit", 1, 700);
                Check.That(ticks.Count == 1, $"{rate}: a unit is made at {percent} %.");
                Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(990), $"{rate}: the cost is not scaled at {percent} % ({world.MoneyOf(new PlayerId(1))}).");
                return world.InFrames(ticks[0] - ordered);
            }

            var normal = UnitFrames(100);
            var quick = UnitFrames(MatchDebugOptions.QuickBuildPercent);
            Check.That(normal is >= 599 and <= 602, $"{rate}: BuildTime 20 s is 600 frames ({normal}).");
            Check.That(quick is >= 59 and <= 62, $"{rate}: at 10 % it is 60 frames ({quick}).");

            long UpgradeFrames(int percent)
            {
                var world = DebugWorld(rate, percent, 0, out var feeder, out var fixture, out _);
                feeder.Spawn(1, "DebugLab", x: 100, y: 100);
                feeder.GiveMoney(1, 1000);
                world.Step();
                var player = new PlayerId(1);
                var upgrade = fixture.Templates.Upgrades.TryGet("Upgrade_DebugPlayer", out var info) ? info.Index : -1;
                var ordered = world.Tick;
                Check.That(feeder.PurchaseUpgrade(1, "Upgrade_DebugPlayer", 1).Accepted, $"{rate}: the upgrade is ordered.");
                world.Step();
                Check.That(world.MoneyOf(player) == Fixed.FromInt(900) && world.UpgradeInProduction(player, upgrade), $"{rate}: the upgrade is paid in full when queued ({world.MoneyOf(player)}).");
                for (var step = 0; step < world.Frames(200) && !world.PlayerUpgrades(player).Has(upgrade); step++) world.Step();
                Check.That(world.PlayerUpgrades(player).Has(upgrade), $"{rate}: the upgrade completes at {percent} %.");
                return world.InFrames(world.Tick - ordered);
            }

            var normalUpgrade = UpgradeFrames(100);
            var quickUpgrade = UpgradeFrames(MatchDebugOptions.QuickBuildPercent);
            Check.That(normalUpgrade is >= 119 and <= 122, $"{rate}: an upgrade of 4 s is 120 frames ({normalUpgrade}).");
            Check.That(quickUpgrade is >= 11 and <= 14, $"{rate}: at 10 % it is 12 frames ({quickUpgrade}).");

            int ConstructionWork(int percent)
            {
                var world = DebugWorld(rate, percent, 0, out var feeder, out _, out _, withTerrain: false);
                feeder.Spawn(1, "Dozer", x: 0, y: 0);
                feeder.GiveMoney(1, 1000);
                world.Step();
                world.Step();
                var order = feeder.Construct(1, 1, "Plant", 60, 0);
                Check.That(order.Accepted, $"{rate}: the dozer is ordered to build ({order.Reason}).");
                world.Step();
                var site = ConstructionFind(world, "Plant");
                var completed = false;
                for (var step = 0; step < world.Frames(400) && !completed; step++)
                    completed = world.Step().Events.Any(value => value.Kind == CnCNew.Simulation.Events.SimEventKind.ConstructionCompleted && value.Entity == site.Id);
                Check.That(completed && !IsSite(site) && site.Body!.Health == site.Body.MaxHealth, $"{rate}: the structure is complete at {percent} %.");
                return site.ConstructionWork;
            }

            Check.That(ConstructionWork(100) == WorkFrames(rate, 60) && ConstructionWork(MatchDebugOptions.QuickBuildPercent) == WorkFrames(rate, 6),
                $"{rate}: a structure of 2 s takes 60 frames of builder work, 6 at 10 %.");

            // Cancelling a unit in production gives the whole price back; the build time does not touch it.
            var cancelWorld = DebugWorld(rate, MatchDebugOptions.QuickBuildPercent, 0, out var cancelFeeder, out _, out _);
            cancelFeeder.Spawn(1, "DebugFactory", x: 100, y: 100);
            cancelFeeder.GiveMoney(1, 1000);
            cancelWorld.Step();
            cancelFeeder.QueueProduction(1, 1, "SlowUnit");
            for (var step = 0; step < cancelWorld.Frames(3); step++) cancelWorld.Step();
            cancelFeeder.CancelProduction(1, 1, 0);
            cancelWorld.Step();
            Check.That(cancelWorld.MoneyOf(new PlayerId(1)) == Fixed.FromInt(1000), $"{rate}: a cancelled unit is refunded in full ({cancelWorld.MoneyOf(new PlayerId(1))}).");

            // The helper itself: at least one frame, whole frames of the original, the normal time left alone.
            var plain = DebugWorld(rate, 100, 0, out _, out _, out _);
            var quickWorld = DebugWorld(rate, MatchDebugOptions.QuickBuildPercent, 0, out _, out _, out _);
            Check.That(plain.ScaleBuildTicks(new PlayerId(1), plain.Frames(600)) == plain.Frames(600) && quickWorld.ScaleBuildTicks(new PlayerId(1), quickWorld.Frames(600)) == quickWorld.Frames(60)
                       && quickWorld.ScaleBuildTicks(new PlayerId(1), quickWorld.Frames(1)) == quickWorld.Frames(1) && quickWorld.ScaleBuildTicks(new PlayerId(1), quickWorld.Frames(15)) == quickWorld.Frames(2) && quickWorld.ScaleBuildTicks(new PlayerId(1), 0) == 0,
                $"{rate}: the scaling is whole frames, rounded up, at least one.");

            // Saved and restored, the option is still on; a world without it writes no new state and has the hash of the original game.
            var live = DebugWorld(rate, MatchDebugOptions.QuickBuildPercent, 0, out _, out var liveFixture, out var liveTerrain);
            for (var step = 0; step < 5; step++) live.Step();
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), liveFixture.Templates, liveFixture.Fingerprint, liveTerrain);
            Check.That(restored.BuildTimePercent == MatchDebugOptions.QuickBuildPercent && restored.StateHash == live.StateHash, $"{rate}: a snapshot keeps the build time.");
            var original = DebugWorld(rate, 100, 0, out _, out _, out _);
            var configured = DebugWorld(rate, 100, 0, out _, out _, out _);
            configured.ConfigureMatchOptions(100, 0);
            for (var step = 0; step < 5; step++)
                Check.That(original.Step().StateHash == configured.Step().StateHash && live.StateHash != original.StateHash, $"{rate}: the options of the original game move no hash.");
            Check.That(!System.Text.Encoding.UTF8.GetString(WorldSnapshot.Write(configured)).Contains("buildTimePercent", StringComparison.Ordinal), $"{rate}: nothing is written for the original game.");
        }
    }

    private static int WorkFrames(SimulationRate rate, int frames) => checked((int)rate.Time.OriginalFrames(frames));

    /// <summary>
    /// A destroyed neutral building of the map comes back after the chosen game seconds - not before - at its place and angle, as a neutral object with the map's properties and full
    /// health; rubble is taken away first, something that stands on the spot holds it back; a save in between continues the same; the option off leaves it gone.
    /// </summary>
    public static void DestroyedNeutralBuildingsComeBack()
    {
        foreach (var (rate, _) in WorldRates)
        {
            const int seconds = 30;
            var world = DebugWorld(rate, 100, seconds, out var feeder, out var fixture, out var terrain);
            ulong sequence = 100;
            PlaceMapObjectCommand Place(string template, int x, int health, MapObjectFlags flags, decimal angle = 0m)
                => new(PlayerId.System, sequence++, world.Tick, template, PlayerId.System, Fixed.FromInt(x), Fixed.FromInt(100), Fixed.Zero, Fixed.FromDecimal(angle), health, flags, 0);
            var flags = MapObjectFlags.Unsellable | MapObjectFlags.Targetable;
            var placed = new[] { world.Submit(Place("MapHut", 40, 50, flags, 0.5m)), world.Submit(Place("MapWreck", 150, 100, MapObjectFlags.None)), world.Submit(Place("MapWreck", 220, 0, MapObjectFlags.None)) };
            Check.That(placed.All(result => result.Accepted), $"{rate}: the system places the objects ({string.Join(", ", placed.Select(result => result.Reason))}).");
            world.Step();
            var hut = world.Entities.Single(entity => entity.Template.Name == "MapHut");
            var wreck = world.Entities.Single(entity => entity.Template.Name == "MapWreck" && entity.X == Fixed.FromInt(150));
            var hutId = hut.Id;
            var hutAngle = hut.Angle;
            Check.That(world.Entities.Count(entity => entity.Template.Name == "MapWreck") == 2, $"{rate}: both wrecks stand (one of them as rubble).");

            for (var step = 0; step < world.Frames(30); step++) world.Step();
            Check.That(world.FindEntity(hutId) is not null, $"{rate}: a building nobody attacks is not replaced.");

            // The hut dies and is removed (DestroyDie); the wreck has no death module and stands as rubble.
            var killedAt = world.Tick;
            foreach (var victim in new[] { hutId, wreck.Id }) world.Submit(new DamageCommand(PlayerId.System, sequence++, world.Tick, victim, Fixed.FromInt(9999), 0, 0));
            world.Step();
            Check.That(hut.Body!.Health == Fixed.Zero, $"{rate}: the hut is dead ({hut.Body.Health}).");
            killedAt = world.Tick;
            Check.That(wreck.IsRubble, $"{rate}: the wreck is rubble.");

            // Something stands on the hut's place when the time is over: a building of the player's own.
            var blockAt = killedAt + world.Time.WholeSecondsToTicks(seconds - 1);
            long backAt = -1;
            var blocker = EntityId.Invalid;
            SimulationWorld? twin = null;
            while (world.Tick < killedAt + world.Time.WholeSecondsToTicks(seconds + 3) && backAt < 0)
            {
                if (world.Tick == blockAt) feeder.Spawn(1, "MapHut", x: 40, y: 100);
                world.Step();
                if (twin is not null) Check.That(twin.Step().StateHash == world.StateHash, $"{rate}: a world restored in the countdown continues identically (tick {world.Tick}).");
                if (world.Tick == blockAt + 1)
                {
                    blocker = world.Entities.Single(entity => entity.Template.Name == "MapHut" && entity.Owner.Value == 1).Id;
                    twin = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
                    Check.That(twin.StateHash == world.StateHash && twin.NeutralRespawnSeconds == seconds, $"{rate}: a snapshot in the countdown keeps it.");
                }
                if (world.Entities.Any(entity => entity.Template.Name == "MapHut" && entity.Owner.IsSystem)) backAt = world.Tick;
            }
            Check.That(backAt < 0, $"{rate}: a building on the spot holds the hut back ({backAt}).");

            // The blocker goes; the hut returns within the second it takes to look again.
            foreach (var target in new[] { world, twin! }) Check.That(target.Submit(new DestroyCommand(new PlayerId(1), 1_000, target.Tick, blocker)).Accepted, $"{rate}: the blocker is removed.");
            var freed = world.Tick;
            for (var step = 0; step < world.Frames(60) && backAt < 0; step++)
            {
                world.Step();
                if (twin is not null) twin.Step();
                if (world.Entities.Any(entity => entity.Template.Name == "MapHut" && entity.Owner.IsSystem)) backAt = world.Tick;
            }
            Check.That(backAt > 0 && backAt - freed <= world.Time.WholeSecondsToTicks(1) + world.Frames(3), $"{rate}: the hut is back within a second of the spot being free ({backAt - freed} ticks).");

            var again = world.Entities.Single(entity => entity.Template.Name == "MapHut" && entity.Owner.IsSystem);
            Check.That(again.Id != hutId && again.X == Fixed.FromInt(40) && again.Y == Fixed.FromInt(100) && again.Angle == hutAngle, $"{rate}: the hut stands where the map put it, turned as it was.");
            Check.That(again.Body!.Health == again.Body.MaxHealth && (again.ScriptDisableMask & (Entity.ScriptUnsellable | Entity.ScriptTargetable)) == (Entity.ScriptUnsellable | Entity.ScriptTargetable),
                $"{rate}: it is whole again with the map's properties ({again.Body.Health}).");
            Check.That(twin is not null && twin.Entities.Any(entity => entity.Template.Name == "MapHut" && entity.Owner.IsSystem && entity.Id == again.Id) && twin.StateHash == world.StateHash,
                $"{rate}: the restored world has it back in the same tick, in the same state.");

            // Not before the countdown: a hut with nothing in the way returns exactly seconds after its death.
            var clean = DebugWorld(rate, 100, seconds, out var cleanFeeder, out _, out _);
            sequence = 500;
            clean.Submit(new PlaceMapObjectCommand(PlayerId.System, sequence++, 0, "MapHut", PlayerId.System, Fixed.FromInt(40), Fixed.FromInt(100), Fixed.Zero, Fixed.Zero, 100, MapObjectFlags.None, 0));
            clean.Step();
            clean.Submit(new DamageCommand(PlayerId.System, sequence++, clean.Tick, new EntityId(1), Fixed.FromInt(9999), 0, 0));
            clean.Step();
            var died = clean.Tick;
            long returned = -1;
            for (var step = 0; step < clean.Frames(30 * (seconds + 10)) && returned < 0; step++)
            {
                clean.Step();
                if (clean.Entities.Any(entity => entity.Template.Name == "MapHut")) returned = clean.Tick;
            }
            var wanted = clean.Time.WholeSecondsToTicks(seconds);
            Check.That(returned - died >= wanted - clean.Frames(1) && returned - died <= wanted + clean.Frames(6), $"{rate}: the hut is back {seconds} s after its death, not before ({clean.InFrames(returned - died)} frames).");

            // Rubble is taken away, and the building comes back whole where the rubble was.
            for (var step = 0; step < world.Frames(30 * 40) && world.FindEntity(wreck.Id) is not null; step++) world.Step();
            var newWreck = world.Entities.Where(entity => entity.Template.Name == "MapWreck" && entity.X == Fixed.FromInt(150)).ToArray();
            Check.That(world.FindEntity(wreck.Id) is null && newWreck.Length == 1 && !newWreck[0].IsRubble && newWreck[0].Body!.Health == newWreck[0].Body!.MaxHealth, $"{rate}: the rubble is replaced by a whole wreck building.");
            Check.That(world.Entities.Count(entity => entity.Template.Name == "MapWreck") == 2, $"{rate}: the wreck that was rubble from the start stays rubble and is never made again.");

            // Off: the building stays gone and no state is written.
            var off = DebugWorld(rate, 100, 0, out var offFeeder, out _, out _);
            off.Submit(new PlaceMapObjectCommand(PlayerId.System, 1, 0, "MapHut", PlayerId.System, Fixed.FromInt(40), Fixed.FromInt(100), Fixed.Zero, Fixed.Zero, 100, MapObjectFlags.None, 0));
            off.Step();
            off.Submit(new DamageCommand(PlayerId.System, 2, off.Tick, new EntityId(1), Fixed.FromInt(9999), 0, 0));
            for (var step = 0; step < off.Frames(30 * (seconds + 10)); step++) off.Step();
            Check.That(!off.Entities.Any(entity => entity.Template.Name == "MapHut") && !System.Text.Encoding.UTF8.GetString(WorldSnapshot.Write(off)).Contains("neutralBuildings", StringComparison.Ordinal),
                $"{rate}: with the option off a destroyed building stays destroyed.");
        }
    }

    private static IReadOnlyDictionary<string, string> DebugSessionFiles() => new Dictionary<string, string>(SkirmishFiles())
    {
        ["Data/INI/Object/DebugSession.ini"] = """
        Science SCIENCE_Debug
          SciencePurchasePointCost = 2
        End
        Rank 1
          RankName = GUI:Rank1
          SkillPointsNeeded = 0
          SciencePurchasePointsGranted = 0
        End
        Rank 2
          RankName = GUI:Rank2
          SkillPointsNeeded = 800
          SciencePurchasePointsGranted = 3
        End
        Object MapHut
          KindOf = STRUCTURE IMMOBILE SELECTABLE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 400.0
            InitialHealth = 400.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>
    /// A skirmish started from a description with the three options: every player has the general points, a human spends them; a soldier is made in a tenth of the time; the neutral
    /// building the map placed comes back; a save at any point continues identically, and the recording replays the match - on a bare world and as a match with the AI audited.
    /// </summary>
    public static void MatchDebugOptionsPlayInASkirmish()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: DebugSessionFiles());
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: [("Player_1_Start", 100m, 200m, 0m), ("Player_2_Start", 500m, 200m, 0m)]);
        var terrain = TerrainMap.Build(map, "skirmish");
        var participants = ReferenceMatchScenario.Participants(fixture.Templates, out _);
        var plain = SkirmishMatchSession.Describe(new SkirmishMatchOptions(0xC0FFEE06, 7, TestRate.Default.Time.OriginalFrames(3_000), 60, 500, 500, 0), fixture.Templates, fixture.Fingerprint, terrain,
            [..participants.Select(participant => new SkirmishSlot(participant))], "Maps/Synthetic/Synthetic.map");
        var human = plain with { Slots = plain.Slots.SetItem(0, plain.Slots[0] with { Kind = MatchSlotKind.Human }) };
        var debug = human with { Debug = new MatchDebugOptions(MatchDebugOptions.QuickBuildPercent, 5, 800) };
        ImmutableArray<MapObject> objects = [new MapObject("MapHut", 20m, 200m, 0m, 0m, 0, new MapProperties([new MapProperty("objectInitialHealth", MapPropertyType.Int, 5, 0m, "")]))];

        SkirmishMatchSession Start(MatchDescription description) => SkirmishMatchSession.Start(description, fixture.Templates, terrain, objects, definitionFingerprint: fixture.Fingerprint);
        var control = Start(human);
        var session = Start(debug);
        control.Step();
        session.Step();
        control.Step();
        session.Step();

        // General points: 800 skill points are rank 2 for every player, with the 3 purchase points of that rank, and a human spends them.
        var (one, two) = (new PlayerId(1), new PlayerId(2));
        Check.That(control.World.SciencePointsOf(one) == 0 && control.World.RankLevelOf(one) == 1 && session.World.SciencePointsOf(one) == 3 && session.World.SciencePointsOf(two) == 3
                   && session.World.RankLevelOf(one) == 2 && session.World.RankLevelOf(two) == 2, $"Both players start at rank 2 with its 3 general points; the match without the option at rank 1 with none ({control.World.RankLevelOf(one)}/{control.World.SciencePointsOf(one)}, {session.World.RankLevelOf(one)}/{session.World.SciencePointsOf(one)}, {session.World.RankLevelOf(two)}/{session.World.SciencePointsOf(two)}).");
        var humanSequence = 1UL;
        Check.That(session.Submit(new PurchaseScienceCommand(one, humanSequence++, session.World.Tick, "SCIENCE_Debug")).Accepted, "The human buys a science of the general's window.");
        session.Step();
        Check.That(session.World.SciencePointsOf(one) == 1 && session.World.Sciences.TryGet("SCIENCE_Debug", out var debugScience) && session.World.PlayerSciences(one).Has(debugScience.Index), "It costs 2 of the 3 points and is owned.");
        Check.That(!control.Submit(new PurchaseScienceCommand(one, 1, control.World.Tick, "SCIENCE_Debug")).Accepted || control.World.SciencePointsOf(one) == 0, "Without the option there is nothing to buy it with.");
        control.Step();

        // The soldier of the human's command centre: 30 frames normally, 3 at 10 %.
        long SoldierFrames(SkirmishMatchSession match, ulong sequence)
        {
            var centre = match.World.Entities.Single(entity => entity.Owner == one && entity.Template.IsKindOf("COMMANDCENTER"));
            var before = match.World.Entities.Count(entity => entity.Owner == one && entity.Template.IsKindOf("INFANTRY"));
            var ordered = match.World.Tick;
            Check.That(match.Submit(new QueueProductionCommand(one, sequence, ordered, centre.Id, centre.Template.Name.Replace("Center", "Soldier", StringComparison.Ordinal))).Accepted, "The soldier is ordered.");
            for (var step = 0; step < match.Frames(120); step++)
            {
                match.Step();
                if (match.World.Entities.Count(entity => entity.Owner == one && entity.Template.IsKindOf("INFANTRY")) > before) return match.World.InFrames(match.World.Tick - ordered);
            }
            return -1;
        }
        var normalFrames = SoldierFrames(control, 2);
        var quickFrames = SoldierFrames(session, humanSequence++);
        Check.That(normalFrames is >= 29 and <= 33 && quickFrames is >= 2 and <= 5, $"A soldier takes 30 frames, at 10 % about 3 ({normalFrames}, {quickFrames}).");

        // The human's soldier shoots the weak hut of the map (5 % of its health); it is back 5 seconds later (the match ends in a win of the computer after about 15 s).
        var hut = session.World.Entities.Single(entity => entity.Template.Name == "MapHut");
        var hutId = hut.Id;
        var soldier = session.World.Entities.First(entity => entity.Owner == one && entity.Template.IsKindOf("INFANTRY"));
        var attack = session.Submit(new FireWeaponCommand(one, humanSequence++, session.World.Tick, soldier.Id, hutId));
        Check.That(attack.Accepted, $"The soldier is ordered to fire at the neutral hut ({attack.Reason}).");
        long killed = -1;
        var saves = new Dictionary<long, byte[]>();
        var hashes = new Dictionary<long, ulong>();
        long back = -1;
        var every = session.World.Time.WholeSecondsToTicks(1);
        while (!session.IsOver)
        {
            if (session.World.Tick % every == 0) saves[session.World.Tick] = session.Save();
            session.Step();
            hashes[session.World.Tick] = session.StateHash();
            var standing = session.World.Entities.Any(entity => entity.Template.Name == "MapHut" && entity.Owner.IsSystem && !entity.DestroyRequested);
            if (killed < 0 && !standing) killed = session.World.Tick;
            if (killed >= 0 && back < 0 && standing) back = session.World.Tick;
        }
        Check.That(killed > 0, $"The hut of the map was destroyed by the soldier ({string.Join(" ", session.World.Entities.Where(entity => entity.Template.Name == "MapHut").Select(entity => entity.Body!.Health))} {soldier.X},{soldier.Y} {session.World.FindEntity(soldier.Id)?.Body?.Health}).");
        var wanted = session.World.Time.WholeSecondsToTicks(5);
        Check.That(back - killed >= wanted - session.Frames(2) && back - killed <= wanted + session.Frames(6), $"The hut is back after its 5 seconds ({session.World.InFrames(back - killed)} frames).");
        Check.That(session.Description.Debug == new MatchDebugOptions(MatchDebugOptions.QuickBuildPercent, 5, 800), "The session carries the options of its description.");
        var save = saves.Where(pair => pair.Key > killed && pair.Key < killed + wanted - every / 2).OrderBy(pair => pair.Key).Select(pair => pair.Value).FirstOrDefault();
        Check.That(save is not null, "A save was made while the hut was gone.");

        // A save in the countdown continues identically, with the options.
        var restored = SkirmishMatchSession.Restore(save!, fixture.Templates, fixture.Fingerprint, terrain);
        Check.That(restored.Description.Debug == session.Description.Debug && restored.World.BuildTimePercent == MatchDebugOptions.QuickBuildPercent && restored.World.NeutralRespawnSeconds == 5, "The restored session has the options.");
        while (!restored.IsOver)
        {
            restored.Step();
            if (hashes[restored.World.Tick] != restored.StateHash()) throw new InvalidOperationException($"The restored session differs after tick {restored.World.Tick - 1}.");
        }
        Check.That(restored.World.Entities.Any(entity => entity.Template.Name == "MapHut" && entity.Owner.IsSystem), "The restored session brought the hut back as well.");

        // The recording replays the match: on a bare world, and as a match whose AI is audited.
        var replay = session.ToReplay();
        var bytes = ReplayFormat.Write(replay);
        var read = ReplayFormat.Read(bytes);
        var bare = ReplayRunner.Run(read, fixture.Templates, fixture.Fingerprint, terrain: terrain);
        Check.That(bare.FinalHash == session.World.StateHash, $"The bare replay ends in the live world's hash ({bare.FinalHash:X16} against {session.World.StateHash:X16}).");
        var played = SkirmishReplay.Play(read, fixture.Templates, fixture.Fingerprint, terrain, objects);
        Check.That(played.FirstDeviation is null && played.FinalWorldHash == session.World.StateHash && played.FinalSessionHash == session.StateHash(), "Played as a match, the replay deviates nowhere and ends in the same hashes.");
    }
}
