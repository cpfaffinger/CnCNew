using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #470: what a stop reaches besides the unit: riders, a garrison's riders, slaves, and a stopped stealth unit's next look.
static partial class Tests
{
    private sealed class StopScene
    {
        public required SimulationWorld World;
        public required TemplateCatalog Catalog;
        public ulong Sequence = 100;
        public Entity Create(string name, int owner, int x, int y)
            => World.CreateNow(Catalog.Templates.Single(template => template.Name == name), new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero);
        public SubmitResult Stop(Entity unit) => World.Submit(new StopCommand(unit.Owner, ++Sequence, World.Tick, unit.Id));
        public long Frame(Entity of) => (World.Tick - of.SpawnTick) / World.Time.TicksPerOriginalFrame;
    }

    private static StopScene NewStopScene(DefinitionGraphSource source, SimulationRate rate)
    {
        var catalog = TemplateCatalog.Build(source.Graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 5, players, "real-stop-reaches", source.Fingerprint), catalog);
        world.Step();
        return new StopScene { World = world, Catalog = catalog };
    }

    /// <summary>Steps until <paramref name="done"/> holds, at most <paramref name="frames"/> frames of the original; whether it held.</summary>
    private static bool StepsUntil(SimulationWorld world, System.Func<bool> done, int frames)
    {
        for (var tick = 0; tick < world.Frames(frames); tick++)
        {
            if (done()) return true;
            world.Step();
        }
        return done();
    }

    /// <summary>
    /// #470, real data: <c>AIGroup::groupIdle</c> and <c>privateIdle</c> reach further than the stopped object. (1) A Humvee whose two Rangers fire from it at an enemy truck is stopped:
    /// <c>privateIdle</c> idles every rider with an AI, so the Rangers' attacks end in the tick of the stop (they look again later). (2) A town house garrisoned by two Rangers firing at the
    /// truck - an object without an AI - is stopped: <c>makeMemberStop</c> idles its riders. (3) A GLA Stinger Site whose soldiers fire at the truck is stopped:
    /// <c>SpawnBehavior::orderSlavesToGoIdle</c> idles its slaves. (4) Colonel Burton, unstealthed by his own attack on the truck, is stopped by the player: "we want to not autoacquire
    /// until he is able to stealth again" - his next look waits his <c>StealthDelay</c> (2 s) and a draw of up to a second, and before it he is stealthed and so looks no more; he fires
    /// no further shot. Before, the stop reached the object alone, and Burton fired again two frames after it. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealStopReachesRidersSlavesAndStealth(string directory)
    {
        var source = RealGraph(directory);
        var lines = new List<(SimulationRate Rate, string Line)>();
        foreach (var rate in TargetRates)
        {
            var line = new List<string>();

            // (1) and (2): riders of a transport with an AI, riders of a garrisoned building without one.
            foreach (var holder in new[] { "AmericaVehicleHumvee", "GermanTownHouse1" })
            {
                var scene = NewStopScene(source, rate);
                var world = scene.World;
                var container = scene.Create(holder, 1, 1000, 1000);
                var riders = new List<Entity>();
                for (var index = 0; index < 2; index++)
                {
                    var ranger = scene.Create("AmericaInfantryRanger", 1, 1000, 1000);
                    Check.That(world.TryEnterContainer(ranger, container), $"{rate}: setup - a Ranger enters the {holder}.");
                    riders.Add(ranger);
                }
                scene.Create("ChinaVehicleSupplyTruck", 2, 1110, 1000);
                Check.That(StepsUntil(world, () => riders.All(rider => world.AcquiredVictimOf(rider).IsValid), 30 * 10), $"{rate}: the Rangers in the {holder} attack the truck.");
                while (!world.Time.BeginsFrameOf(container, world.Tick)) world.Step();
                var stop = scene.Stop(container);
                Check.That(stop.Accepted, $"{rate}: the {holder} takes the stop ({stop.Reason}).");
                world.Step();
                Check.That(riders.All(rider => !world.AcquiredVictimOf(rider).IsValid), $"{rate}: the stop of the {holder} ends its riders' attacks.");
                Check.That(StepsUntil(world, () => riders.All(rider => world.AcquiredVictimOf(rider).IsValid), 30 * 10), $"{rate}: idle again, the Rangers look for the truck again.");
                line.Add($"{holder} ok");
            }

            // (3) the slaves of a spawner.
            {
                var scene = NewStopScene(source, rate);
                var world = scene.World;
                var site = scene.Create("GLAStingerSite", 1, 1000, 1000);
                Check.That(StepsUntil(world, () => world.Entities.Count(value => value.Template.Name == "GLAInfantryStingerSoldier") == 3, 30 * 10), $"{rate}: the site has its three soldiers.");
                var soldiers = world.Entities.Where(value => value.Template.Name == "GLAInfantryStingerSoldier").ToArray();
                scene.Create("ChinaVehicleSupplyTruck", 2, 1150, 1000);
                Check.That(StepsUntil(world, () => soldiers.Any(soldier => world.AcquiredVictimOf(soldier).IsValid), 30 * 10), $"{rate}: the site's soldiers attack the truck.");
                while (!world.Time.BeginsFrameOf(site, world.Tick)) world.Step();
                Check.That(scene.Stop(site).Accepted, $"{rate}: the Stinger Site takes the stop.");
                world.Step();
                Check.That(soldiers.All(soldier => !world.AcquiredVictimOf(soldier).IsValid), $"{rate}: the stop of the site ends its soldiers' attacks.");
                line.Add("stinger site ok");
            }

            // (4) a stealth unit's next look after a player's stop.
            {
                var scene = NewStopScene(source, rate);
                var world = scene.World;
                var burton = scene.Create("AmericaInfantryColonelBurton", 1, 1000, 1000);
                var truck = scene.Create("ChinaVehicleSupplyTruck", 2, 1100, 1000);
                StepsUntil(world, () => false, 30 * 3);
                // The player's attack: the client's fire order whenever the weapon may.
                var fired = burton.LastFireTick;
                Check.That(StepsUntil(world, () =>
                {
                    if (burton.LastFireTick != fired) return true;
                    if (world.Time.BeginsFrameOf(burton, world.Tick)) world.Submit(new FireWeaponCommand(new PlayerId(1), ++scene.Sequence, world.Tick, burton.Id, truck.Id));
                    return false;
                }, 30 * 10), $"{rate}: Burton fires at the truck.");
                while (!world.Time.BeginsFrameOf(burton, world.Tick)) world.Step();
                var stealthed = ObjectStatusBits.Index("STEALTHED");
                Check.That(!burton.HasStatus(stealthed), $"{rate}: firing, Burton is not stealthed.");
                var stoppedAt = scene.Frame(burton);
                Check.That(scene.Stop(burton).Accepted, $"{rate}: Burton takes the stop.");
                fired = burton.LastFireTick;
                var again = StepsUntil(world, () => burton.LastFireTick != fired, 30 * 6);
                Check.That(!again && truck is { EffectivelyDead: false }, $"{rate}: the stopped Burton fires no further shot ({(again ? "fired in frame " + (scene.Frame(burton) - stoppedAt) : "none")}).");
                Check.That(burton.HasStatus(stealthed) && !world.AcquiredVictimOf(burton).IsValid, $"{rate}: he is stealthed again and attacks nothing.");
                line.Add("burton ok");
            }
            lines.Add((rate, string.Join("; ", line)));
        }
        foreach (var (rate, text) in lines) System.Console.WriteLine($"       #470 {rate}: {text}");
    }

    /// <summary>
    /// #470 (#467's carrier through a script): <c>ScriptActions::doNamedStop</c> calls <c>aiIdle</c> on the carrier, whose <c>FlightDeckBehavior::aiDoCommand</c> takes <c>AICMD_IDLE</c>
    /// from a script as from a player - the jets in the air are called home. A carrier of a mission sends its front jet at an enemy; a script stops the carrier (<c>NAMED_STOP</c>):
    /// the deck holds the idle order and the jet lands on it. At 30, 60 and 120 Hz.
    /// </summary>
    public static void ScriptStopsACarrier()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var files = new Dictionary<string, string>
            {
                ["Data/INI/FXList.ini"] = DeckFiles["Data/INI/FXList.ini"],
                ["Data/INI/Upgrade.ini"] = DeckFiles["Data/INI/Upgrade.ini"],
                ["Data/INI/Object/AirFixture.ini"] = DeckFiles["Data/INI/Object/AirFixture.ini"],
                ["Data/INI/Object/DeckFixture.ini"] = DeckFiles["Data/INI/Object/DeckFixture.ini"],
            };
            var (map, fixture, terrain) = MissionMap([
                Scr.Script("Halt", [Scr.And(Scr.If("FLAG", Scr.Flag("halt"), Scr.Bool(true)))], [Scr.Do("NAMED_STOP", Scr.Unit("Carrier"))], oneShot: true),
            ], objects: [Placed("DeckTestCarrier", 300m, 200m, "teamA", "Carrier"), Placed("DeckTestVictim", 560m, 200m, "teamB", "Victim")], extraFiles: files, rate: rate);
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            Run(session, session.Frames(3));
            var world = session.World;
            var carrier = NamedIn(session, "Carrier")!;
            var victim = NamedIn(session, "Victim")!;
            var deck = carrier.Modules.OfType<FlightDeckBehavior>().Single();
            var front = deck.OccupantOf(0);
            Check.That(front.IsValid && world.Submit(new PursueCommand(carrier.Owner, 1000, world.Tick, carrier.Id, victim.Id, victim.X, victim.Y)).Accepted, $"{rate}: the carrier takes an attack order.");
            var jet = world.FindEntity(front)!;
            Check.That(StepsUntil(world, () => world.IsAirborneTarget(jet), 900), $"{rate}: the front jet flies.");
            session.Engine.SignalUiInteraction("halt");
            Run(session, session.Frames(2));
            Check.That(deck.Order == DeckOrder.Idle, $"{rate}: the script's stop is the deck's idle order ({deck.Order}).");
            Check.That(StepsUntil(world, () => !JetOf(jet).AllowAirLoco, 3600), $"{rate}: the jet lands on the carrier ({JetOf(jet).Phase}).");
            Check.That(session.Engine.MissingActionHits.Count == 0, $"{rate}: no action counted as missing.");
        }
    }
}
