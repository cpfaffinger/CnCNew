using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #458: a parked jet ordered to attack keeps its order through the take-off, on real data.
static partial class Tests
{
    private sealed class ParkedJetScene
    {
        public required SimulationWorld World;
        public required Entity Jet;
        public required JetAIUpdate Ai;
        public required Entity[] Tanks;
        public ulong Sequence;
        public SubmitResult Send(System.Func<ulong, SimCommand> make) => World.Submit(make(++Sequence));
        public long Frame() => (World.Tick - 1 - Jet.SpawnTick) / World.Time.TicksPerOriginalFrame;
        public long Next() => (World.Tick - Jet.SpawnTick) / World.Time.TicksPerOriginalFrame;
        public void ToOwnFrameStart()
        {
            while (!World.Time.BeginsFrameOf(Jet, World.Tick)) World.Step();
        }
        public bool Parked => Ai.Phase == JetAIUpdate.JetPhase.None && !Ai.AllowAirLoco;
        public void UntilParked(string what)
        {
            for (var tick = 0; tick < World.Frames(30 * 180) && !Parked; tick++) World.Step();
            Check.That(Parked, $"{World.Time.Rate}: {what} ({Ai.Phase}).");
        }
    }

    /// <summary>An airfield with a jet of <paramref name="jetName"/> built and parked, two tanks of the enemy to the east, a strategy center that sees them.</summary>
    private static ParkedJetScene BuildParkedJetScene(DefinitionGraphSource source, SimulationRate rate, string jetName)
    {
        var catalog = TemplateCatalog.Build(source.Graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 5, players, "real-jet-pending-attack", source.Fingerprint), catalog);
        ulong sequence = 0;
        SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
        Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaAirfield", new PlayerId(1), Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, "The airfield spawns.");
        Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaStrategyCenter", new PlayerId(1), Fixed.FromInt(5400), Fixed.FromInt(2350), Fixed.Zero, Fixed.Zero)).Accepted, "The strategy center spawns; it sees both tanks.");
        Check.That(Send(next => new SpawnCommand(new PlayerId(2), next, world.Tick, "ChinaTankBattleMaster", new PlayerId(2), Fixed.FromInt(5400), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, "The tank spawns.");
        Check.That(Send(next => new SpawnCommand(new PlayerId(2), next, world.Tick, "ChinaTankBattleMaster", new PlayerId(2), Fixed.FromInt(5400), Fixed.FromInt(2700), Fixed.Zero, Fixed.Zero)).Accepted, "The second tank spawns.");
        world.Step();
        Check.That(world.Submit(new GiveMoneyCommand(PlayerId.System, ++sequence, world.Tick, new PlayerId(1), Fixed.FromInt(50000))).Accepted, "Money is granted.");
        world.Step();
        var airfield = world.Entities.First(value => value.Template.Name == "AmericaAirfield");
        var tanks = world.Entities.Where(value => value.Template.Name == "ChinaTankBattleMaster").OrderBy(value => value.Id.Value).ToArray();
        var queued = Send(next => new QueueProductionCommand(new PlayerId(1), next, world.Tick, airfield.Id, jetName));
        Check.That(queued.Accepted, $"{rate}: the {jetName} is queued ({queued.Reason}).");
        Entity? jet = null;
        for (var tick = 0; tick < world.Frames(30 * 120) && jet is null; tick++)
        {
            world.Step();
            jet = world.Entities.FirstOrDefault(value => value.Template.Name == jetName);
        }
        Check.That(jet is not null, $"{rate}: the airfield built the {jetName}.");
        var scene = new ParkedJetScene { World = world, Jet = jet!, Ai = jet!.Modules.OfType<JetAIUpdate>().Single(), Tanks = tanks, Sequence = sequence };
        scene.UntilParked($"the {jetName} stands on its space");
        return scene;
    }

    private sealed record DefinitionGraphSource(CompiledDefinitionGraph Graph, string Fingerprint);

    private static DefinitionGraphSource RealGraph(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        return new DefinitionGraphSource(graph, fingerprint);
    }

    /// <summary>
    /// #458: <c>JetAIUpdate::aiDoCommand</c> stores the whole command (<c>m_mostRecentCommand</c>, <c>HAS_PENDING_COMMAND</c>) of a parked jet and starts <c>TAKING_OFF_AWAIT_CLEARANCE</c>;
    /// while the take-off runs a newer command replaces it, and when the take-off is over the idle update gives it again (<c>reconstitute</c>, <c>aiDoCommand</c>) - an attack on an object
    /// with its victim, an attack on a place with its place. An Aurora and a Raptor parked at an airfield, each ordered once to attack a tank and given no order during the take-off: when
    /// they fly they attack that tank and head for their approach target near it (the Raptor across a save in the middle of its take-off), and with the client's stream of shots in flight
    /// (fire whenever the weapon may - no order to fly at the tank again) they hit it. A Raptor ordered to attack one tank and then the other while it taxis attacks the second; one ordered
    /// to attack and then to move flies to the place and attacks nothing. An Aurora ordered once to attack the tank's place takes off and drops its bomb there with no further order.
    /// Before, the attack was stored as a move and its victim lost while the jet taxied. At 30, 60 and 120 Hz, in the same frames of the jet's own.
    /// </summary>
    public static void RealParkedJetKeepsItsAttackThroughTheTakeoff(string directory)
    {
        var source = RealGraph(directory);
        var timelines = new List<(SimulationRate Rate, string Line)>();
        foreach (var rate in TargetRates)
        {
            var line = new List<string>();

            foreach (var jetName in new[] { "AmericaJetAurora", "AmericaJetRaptor" })
            {
                var scene = BuildParkedJetScene(source, rate, jetName);
                var tank = scene.Tanks[0];
                scene.ToOwnFrameStart();
                var ordered = scene.Next();
                var attack = scene.Send(next => new PursueCommand(new PlayerId(1), next, scene.World.Tick, scene.Jet.Id, tank.Id, tank.X, tank.Y));
                Check.That(attack.Accepted, $"{rate}: the {jetName} is ordered to attack ({attack.Reason}).");
                scene.World.Step();
                Check.That(scene.Ai.HasPendingCommand && !scene.World.AcquiredVictimOf(scene.Jet).IsValid, $"{rate}: the parked {jetName} stores the attack and attacks nothing yet.");
                var saved = false;
                var airborne = UntilJetFlies(scene, value =>
                {
                    // The Raptor is saved and restored in the middle of its take-off: the stored attack is part of its state.
                    if (saved || jetName != "AmericaJetRaptor" || value.Ai.Phase != JetAIUpdate.JetPhase.TaxiToTakeoff) return;
                    saved = true;
                    var restored = CnCNew.Simulation.Snapshots.WorldSnapshot.Restore(CnCNew.Simulation.Snapshots.WorldSnapshot.Write(value.World), value.World.Templates, source.Fingerprint, value.World.Terrain);
                    value.World = restored;
                    value.Jet = restored.FindEntity(value.Jet.Id)!;
                    value.Ai = value.Jet.Modules.OfType<JetAIUpdate>().Single();
                    value.Tanks = value.Tanks.Select(item => restored.FindEntity(item.Id)!).ToArray();
                });
                tank = scene.Tanks[0];
                var (world, jet, ai) = (scene.World, scene.Jet, scene.Ai);
                Check.That(jetName != "AmericaJetRaptor" || saved, $"{rate}: the Raptor was saved during its take-off.");
                Check.That(airborne > ordered, $"{rate}: the {jetName} takes off (frame {airborne}, ordered in {ordered}).");
                Check.That(world.AcquiredVictimOf(jet) == tank.Id && !ai.HasPendingCommand,
                    $"{rate}: in the frame it flies the {jetName} attacks the tank it was ordered to (victim {world.AcquiredVictimOf(jet).Value}, the tank {tank.Id.Value}).");
                Check.That(jet.HasMoveTarget && JetGoalDistance(jet.MoveTargetX, jet.MoveTargetY, tank) < Fixed.FromInt(600),
                    $"{rate}: the {jetName} heads for its approach target near the tank ({jet.MoveTargetX}, {jet.MoveTargetY}).");
                var fired = jet.LastFireTick;
                long shot = -1;
                for (var tick = 0; tick < world.Frames(30 * 120) && shot < 0; tick++)
                {
                    // The client's standing order in flight, looked at once in each frame of the jet: fire whenever the weapon may.
                    if (world.Time.BeginsFrameOf(jet, world.Tick) && world.ChooseWeaponInView(jet, tank, FireOrigin.Player).CanFire && !world.IsShotPending(jet, tank.Id))
                        scene.Send(next => new FireWeaponCommand(new PlayerId(1), next, world.Tick, jet.Id, tank.Id));
                    world.Step();
                    if (jet.LastFireTick != fired) shot = scene.Frame();
                }
                Check.That(shot > airborne, $"{rate}: the {jetName} fires at the tank (frame {shot}, airborne in {airborne}).");
                line.Add($"{jetName}: airborne {airborne - ordered}, fired {shot - ordered}");
            }

            // A newer command replaces the stored attack: another victim, or a move.
            foreach (var replaceWithMove in new[] { false, true })
            {
                var scene = BuildParkedJetScene(source, rate, "AmericaJetRaptor");
                var (world, jet, tank, other) = (scene.World, scene.Jet, scene.Tanks[0], scene.Tanks[1]);
                scene.ToOwnFrameStart();
                var ordered = scene.Next();
                Check.That(scene.Send(next => new PursueCommand(new PlayerId(1), next, world.Tick, jet.Id, tank.Id, tank.X, tank.Y)).Accepted, $"{rate}: the Raptor is ordered to attack.");
                var replaced = false;
                var airborne = UntilJetFlies(scene, value =>
                {
                    if (replaced || value.Ai.Phase != JetAIUpdate.JetPhase.TaxiToTakeoff) return;
                    replaced = true;
                    var order = value.Send(next => replaceWithMove
                        ? new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, jet.Id, Fixed.FromInt(3000), Fixed.FromInt(3500))
                        : new PursueCommand(new PlayerId(1), next, world.Tick, jet.Id, other.Id, other.X, other.Y));
                    Check.That(order.Accepted, $"{rate}: the taxiing Raptor takes the newer order ({order.Reason}).");
                });
                Check.That(replaced && airborne > ordered, $"{rate}: the Raptor takes off (frame {airborne}).");
                if (replaceWithMove)
                {
                    Check.That(!world.AcquiredVictimOf(jet).IsValid && jet.HasMoveTarget && JetGoalDistance(jet.MoveTargetX, jet.MoveTargetY, tank) > Fixed.FromInt(1000),
                        $"{rate}: a move given while it taxied replaced the attack (victim {world.AcquiredVictimOf(jet).Value}, heading for {jet.MoveTargetX}, {jet.MoveTargetY}).");
                    line.Add($"move instead: airborne {airborne - ordered}");
                }
                else
                {
                    Check.That(world.AcquiredVictimOf(jet) == other.Id && JetGoalDistance(jet.MoveTargetX, jet.MoveTargetY, other) < Fixed.FromInt(600),
                        $"{rate}: an attack on the other tank given while it taxied replaced the first (victim {world.AcquiredVictimOf(jet).Value}, the other tank {other.Id.Value}).");
                    line.Add($"other victim: airborne {airborne - ordered}");
                }
            }

            // An attack on the tank's place, given once to a parked Aurora.
            {
                var scene = BuildParkedJetScene(source, rate, "AmericaJetAurora");
                var (world, jet, ai, tank) = (scene.World, scene.Jet, scene.Ai, scene.Tanks[1]);
                scene.ToOwnFrameStart();
                var ordered = scene.Next();
                var fired = jet.LastFireTick;
                var attack = scene.Send(next => new AttackPositionCommand(new PlayerId(1), next, world.Tick, ImmutableArray.Create(jet.Id), tank.X, tank.Y, false, WeaponSlot.Best, 0));
                Check.That(attack.Accepted, $"{rate}: the Aurora is ordered to attack the place ({attack.Reason}).");
                world.Step();
                Check.That(ai.HasPendingCommand && world.AttackedPlaceOf(jet) is null, $"{rate}: the parked Aurora stores the attack on the place.");
                var airborne = UntilJetFlies(scene);
                Check.That(airborne > ordered && world.AttackedPlaceOf(jet) == (tank.X, tank.Y), $"{rate}: the Aurora takes off and attacks the place (frame {airborne}, ordered in {ordered}).");
                long shot = -1;
                for (var tick = 0; tick < world.Frames(30 * 120) && shot < 0; tick++)
                {
                    world.Step();
                    if (jet.LastFireTick != fired) shot = scene.Frame();
                }
                Check.That(shot > airborne, $"{rate}: the Aurora drops its bomb on the place without another order (frame {shot}, airborne in {airborne}, {ai.Phase}).");
                line.Add($"place: airborne {airborne - ordered}, fired {shot - ordered}");
            }
            timelines.Add((rate, string.Join("; ", line)));
        }
        foreach (var (rate, text) in timelines) System.Console.WriteLine($"       #458 {rate}: {text}");
        Check.That(timelines.Select(value => value.Line).Distinct().Count() == 1, "The jets take off and fire in the same frames of their own at 30, 60 and 120 Hz.");
    }

    /// <summary>Steps with no order until the jet flies (out of its take-off); the frame of its own it flies in, or -1.</summary>
    private static long UntilJetFlies(ParkedJetScene scene, System.Action<ParkedJetScene>? midway = null)
    {
        for (var tick = 0; tick < scene.World.Frames(30 * 60); tick++)
        {
            scene.World.Step();
            if (scene.Ai.AllowAirLoco && scene.Ai.Phase == JetAIUpdate.JetPhase.None) return scene.Frame();
            midway?.Invoke(scene);
        }
        return -1;
    }

    private static Fixed JetGoalDistance(Fixed x, Fixed y, Entity to)
    {
        var (dx, dy) = (x - to.X, y - to.Y);
        return Fixed.Sqrt(dx * dx + dy * dy);
    }
}
