using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.Schema;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Timers;
using CnCNew.Simulation.World;

/// <summary>Assigns per-player sequences and submits commands to a world.</summary>
sealed class Feeder(SimulationWorld world)
{
    private readonly Dictionary<int, ulong> _sequence = [];
    private ulong Next(int player) => _sequence[player] = _sequence.GetValueOrDefault(player) + 1;

    public SubmitResult Spawn(int player, string template, long? tick = null, int owner = 0, int x = 0, int y = 0)
        => world.Submit(new SpawnCommand(new PlayerId(player), Next(player), tick ?? world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero));

    public SubmitResult Kill(uint id, long? tick = null, int player = 0)
        => world.Submit(new KillCommand(new PlayerId(player), Next(player), tick ?? world.Tick, new EntityId(id)));

    public SubmitResult Destroy(uint id, long? tick = null, int player = 0)
        => world.Submit(new DestroyCommand(new PlayerId(player), Next(player), tick ?? world.Tick, new EntityId(id)));

    public SubmitResult Damage(uint id, decimal amount, int damageType = 0, int deathType = 0, long? tick = null, int player = 0)
        => world.Submit(new DamageCommand(new PlayerId(player), Next(player), tick ?? world.Tick, new EntityId(id), Fixed.FromDecimal(amount), damageType, deathType));

    public SubmitResult Timer(uint target, long delay, long repeat, TimerActionKind action, decimal amount = 0, int tag = 0, long? tick = null)
        => world.Submit(new SetTimerCommand(PlayerId.System, Next(0), tick ?? world.Tick, new EntityId(target), delay, repeat, action, Fixed.FromDecimal(amount), DamageTypes.Explosion, tag));

    public SubmitResult CancelTimer(ulong id, long? tick = null)
        => world.Submit(new CancelTimerCommand(PlayerId.System, Next(0), tick ?? world.Tick, new TimerId(id)));

    // ----------------------------------------------------------------------------------------------------- phase 5

    public SubmitResult GiveMoney(int target, decimal amount, long? tick = null)
        => world.Submit(new GiveMoneyCommand(PlayerId.System, Next(0), tick ?? world.Tick, new PlayerId(target), Fixed.FromDecimal(amount)));

    public SubmitResult PurchaseUpgrade(int player, string upgrade, uint? target = null, long? tick = null)
        => world.Submit(new PurchaseUpgradeCommand(new PlayerId(player), Next(player), tick ?? world.Tick, upgrade, target is { } id ? new EntityId(id) : null));

    public SubmitResult QueueProduction(int player, uint producer, string template, long? tick = null)
        => world.Submit(new QueueProductionCommand(new PlayerId(player), Next(player), tick ?? world.Tick, new EntityId(producer), template));

    public SubmitResult CancelProduction(int player, uint producer, int slot, long? tick = null)
        => world.Submit(new CancelProductionCommand(new PlayerId(player), Next(player), tick ?? world.Tick, new EntityId(producer), slot));

    public SubmitResult SetMoveTarget(int player, uint target, int x, int y, long? tick = null)
        => world.Submit(new SetMoveTargetCommand(new PlayerId(player), Next(player), tick ?? world.Tick, new EntityId(target), Fixed.FromInt(x), Fixed.FromInt(y)));

    public SubmitResult FireWeapon(int player, uint source, uint target, long? tick = null)
        => world.Submit(new FireWeaponCommand(new PlayerId(player), Next(player), tick ?? world.Tick, new EntityId(source), new EntityId(target)));

    public SubmitResult GiveSciencePoints(int target, int amount, long? tick = null)
        => world.Submit(new GiveSciencePointsCommand(PlayerId.System, Next(0), tick ?? world.Tick, new PlayerId(target), amount));

    public SubmitResult PurchaseScience(int player, string science, long? tick = null)
        => world.Submit(new PurchaseScienceCommand(new PlayerId(player), Next(player), tick ?? world.Tick, science));
}

static partial class Tests
{
    private static readonly ImmutableArray<PlayerConfiguration> TwoPlayers = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    public static SimulationWorld NewWorld(SyntheticFixture fixture, uint seed = 1)
        => new(new WorldConfiguration(new WorldId(1), seed, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);

    private static string Describe(SimEvent simEvent) => $"{simEvent.Kind}:{simEvent.Entity}:{simEvent.Text}:{simEvent.Value}";

    public static void Lifecycle()
    {
        var fixture = new SyntheticFixture();
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        Check.That(world.Tick == 0 && world.Metrics.Entities == 0, "A new world is empty at tick 0.");
        Check.That(feeder.Spawn(1, "Barrel", x: 10, y: 20).Accepted, "Spawn accepted.");
        var result = world.Step();
        Check.That(result.Tick == 0 && world.Tick == 1, "Step executes tick 0 and advances.");
        var spawned = result.Events.Single(value => value.Kind == SimEventKind.EntitySpawned);
        Check.That(spawned.Entity.Value == 1 && spawned.Text == "Barrel" && spawned.Player.Value == 1, "Spawn event names the entity, template and owner.");
        var barrel = world.FindEntity(new EntityId(1))!;
        Check.That(barrel.Owner.Value == 1 && barrel.X == Fixed.FromInt(10) && barrel.Y == Fixed.FromInt(20) && world.Metrics.Modules == 4, "The entity has its owner, position and four simulation modules.");
        Check.That(barrel.Body!.Health == Fixed.FromInt(100), "Initial health comes from the body data.");

        feeder.Damage(1, 30);
        result = world.Step();
        Check.That(barrel.Body.Health == Fixed.FromInt(70), "Damage reduces health.");
        Check.That(result.Events.Single().Kind == SimEventKind.EntityDamaged && result.Events.Single().Value == Fixed.FromInt(30).Raw, "One damage event with the amount.");
        feeder.Damage(1, 500);
        result = world.Step();
        var died = result.Events.Select(Describe).ToArray();
        Check.That(died.SequenceEqual([
            $"EntityDamaged:1::{Fixed.FromInt(70).Raw}", "EntityDied:1::0", "EntityDestroyed:1:Barrel:0", "FxRequested:1:FX_Test:0", "CreationListRequested:1:OCL_BarrelDebris:0",
        ]), "Death order: damage, death, then die modules in module order: " + string.Join(" | ", died));
        Check.That(world.FindEntity(new EntityId(1)) is null && world.Metrics.Entities == 0 && world.Metrics.Modules == 0 && world.Metrics.DestroyedTotal == 1, "The destroyed entity is removed at the end of the same tick.");
        feeder.Damage(1, 5);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.UnknownEntity] == 1, "Damaging a removed entity is rejected and counted.");
        feeder.Spawn(2, "Prop");
        result = world.Step();
        Check.That(result.Events.Single(value => value.Kind == SimEventKind.EntitySpawned).Entity.Value == 2, "Entity ids are never reused.");
        Check.That(world.Metrics.SpawnedTotal == 2, "Spawn counter.");
    }

    public static void BodyAndDieModules()
    {
        var fixture = new SyntheticFixture();
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        foreach (var template in new[] { "Tank", "Building", "Ghost", "Highlander", "Prop", "Sleeper" }) feeder.Spawn(1, template);
        world.Step();
        uint IdOf(string name) => world.Entities.Single(value => value.Template.Name == name).Id.Value;
        string[] Events(Action act)
        {
            act();
            return world.Step().Events.Select(Describe).ToArray();
        }

        var tankId = IdOf("Tank");
        var tank = Events(() => feeder.Damage(tankId, 300));
        Check.That(tank.SequenceEqual([$"EntityDamaged:{tankId}::{Fixed.FromInt(300).Raw}", $"EntityDied:{tankId}::0", $"FxRequested:{tankId}:FX_Test:0",
            $"FxRequested:{tankId}:FX_Other:0", $"CreationListRequested:{tankId}:OCL_Wreck:0", $"WeaponFireRequested:{tankId}:TestWeapon:0", $"EntityDestroyed:{tankId}:Tank:0"]),
            "InstantDeathBehavior: effects, creation lists, weapons, then removal: " + string.Join(" | ", tank));

        var buildingId = IdOf("Building");
        var building = Events(() => feeder.Kill(buildingId));
        Check.That(building.SequenceEqual([$"EntityDamaged:{buildingId}::{Fixed.FromInt(1000).Raw}", $"EntityDied:{buildingId}::0"]), "KeepObjectDie: the corpse stays: " + string.Join(" | ", building));
        var corpse = world.FindEntity(new EntityId(buildingId))!;
        Check.That(corpse.EffectivelyDead && corpse.Body!.Health == Fixed.Zero, "A dead structure keeps existing at zero health.");
        Check.That(Events(() => feeder.Damage(buildingId, 10)).Length == 0, "Dead entities take no further damage.");
        feeder.Destroy(buildingId);
        world.Step();
        Check.That(world.FindEntity(new EntityId(buildingId)) is null, "Destroy removes it.");

        var ghostId = IdOf("Ghost");
        feeder.Damage(ghostId, 1000);
        feeder.Kill(ghostId, world.Tick + 1);
        world.Step();
        world.Step();
        var ghost = world.FindEntity(new EntityId(ghostId))!;
        Check.That(ghost.Body!.Health == Fixed.One && !ghost.EffectivelyDead, "ImmortalBody never drops below one hit point and is never dead, even when killed.");

        var highlanderId = IdOf("Highlander");
        Check.That(Events(() => feeder.Damage(highlanderId, 1000)).Length == 1 && world.FindEntity(new EntityId(highlanderId))!.Body!.Health == Fixed.One, "HighlanderBody leaves one hit point against normal damage.");
        var unresistable = Events(() => feeder.Damage(highlanderId, 1, DamageTypes.Unresistable));
        Check.That(unresistable.Any(value => value.StartsWith("EntityDied", StringComparison.Ordinal)) && world.FindEntity(new EntityId(highlanderId)) is null, "Unresistable damage kills a highlander and its DestroyDie removes it.");

        var propId = IdOf("Prop");
        Check.That(world.FindEntity(new EntityId(propId))!.EffectivelyDead, "InactiveBody entities are effectively dead from the start.");
        Check.That(Events(() => feeder.Damage(propId, 100)).Length == 0, "Inactive bodies ignore normal damage.");
        var propKill = Events(() => feeder.Kill(propId));
        Check.That(propKill.SequenceEqual([$"EntityDied:{propId}::0", $"EntityDestroyed:{propId}:Prop:0"]), "A kill runs the die modules of an inactive body (default DestroyDie).");

        var sleeperId = IdOf("Sleeper");
        world.SetStatus(new EntityId(sleeperId), 1UL << ObjectStatusBits.UnderConstruction);
        var exempt = Events(() => feeder.Kill(sleeperId));
        Check.That(exempt.Length == 2 && world.FindEntity(new EntityId(sleeperId)) is not null, "ExemptStatus keeps DestroyDie from running: " + string.Join(" | ", exempt));
        // (the entity is now a corpse; use a fresh one for the veterancy and dormant-FXListDie checks)
        feeder.Spawn(1, "Sleeper");
        world.Step();
        var second = world.Entities.Where(value => value.Template.Name == "Sleeper").Max(value => value.Id.Value);
        world.SetVeterancy(new EntityId(second), 1);
        var veteran = Events(() => feeder.Kill(second));
        Check.That(veteran.Length == 2 && world.FindEntity(new EntityId(second)) is not null, "VeterancyLevels ALL -VETERAN excludes veterans: " + string.Join(" | ", veteran));
        feeder.Spawn(1, "Sleeper");
        world.Step();
        var third = world.Entities.Where(value => value.Template.Name == "Sleeper").Max(value => value.Id.Value);
        var regular = Events(() => feeder.Kill(third));
        Check.That(regular.SequenceEqual([$"EntityDamaged:{third}::{Fixed.FromInt(20).Raw}", $"EntityDied:{third}::0", $"EntityDestroyed:{third}:Sleeper:0"]),
            "A regular entity is destroyed; the dormant FXListDie (StartsActive = No) requests nothing: " + string.Join(" | ", regular));

        // Death types: DestroyDie of Barrel excludes CRUSHED deaths.
        feeder.Spawn(1, "Barrel");
        world.Step();
        var barrel = world.Entities.Single(value => value.Template.Name == "Barrel").Id.Value;
        var crushed = Events(() => feeder.Damage(barrel, 500, DamageTypes.Explosion, DeathTypes.Crushed));
        Check.That(crushed.Any(value => value.StartsWith("EntityDied", StringComparison.Ordinal)) && crushed.All(value => !value.StartsWith("EntityDestroyed", StringComparison.Ordinal)) && crushed.All(value => !value.StartsWith("FxRequested", StringComparison.Ordinal)),
            "Death type filter: a crushed barrel dies but no die module applies: " + string.Join(" | ", crushed));
        // Subdual and special damage types have no effect on health in phase 4.
        feeder.Spawn(1, "Highlander");
        world.Step();
        var again = world.Entities.Single(value => value.Template.Name == "Highlander").Id.Value;
        Check.That(Events(() => feeder.Damage(again, 10, DamageTypes.SubdualUnresistable)).Length == 0 && world.FindEntity(new EntityId(again))!.Body!.Health == Fixed.FromInt(50), "Subdual damage does not change health.");
        var healed = Events(() => feeder.Damage(again, 5, DamageTypes.Explosion));
        Check.That(healed.Length == 1 && world.FindEntity(new EntityId(again))!.Body!.Health == Fixed.FromInt(45), "Damage of 5 leaves 45.");
        Events(() => feeder.Damage(again, 100, DamageTypes.Healing));
        Check.That(world.FindEntity(new EntityId(again))!.Body!.Health == Fixed.FromInt(50), "Healing is capped at the maximum health.");
    }

    public static void LifetimeAndDeletion()
    {
        var fixture = new SyntheticFixture();
        const uint seed = 4242;
        var world = NewWorld(fixture, seed);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Spark");
        feeder.Spawn(1, "Spark");
        feeder.Spawn(1, "Puff");
        world.Step();
        // Spawn order is id order and each OnSpawn draws from the logic stream: 500..1500 ms = 15..45 ticks, 300..900 ms = 9..27 ticks.
        var reference = new SageRandom(seed);
        var sparkA = Math.Max(1, reference.Range(15, 45));
        var sparkB = Math.Max(1, reference.Range(15, 45));
        var puff = Math.Max(1, reference.Range(9, 27));
        var deaths = new Dictionary<uint, (long Tick, string Kind)>();
        for (var tick = 1; tick < 60; tick++)
        {
            foreach (var simEvent in world.Step().Events)
                if (simEvent.Kind is SimEventKind.EntityDied or SimEventKind.EntityDestroyed)
                    deaths.TryAdd(simEvent.Entity.Value, (simEvent.Tick, simEvent.Kind.ToString()));
        }
        Check.That(deaths[1] == (sparkA, "EntityDied") && deaths[2] == (sparkB, "EntityDied"), $"Sparks are killed at their drawn lifetime ({sparkA}, {sparkB}); got {deaths[1]}, {deaths[2]}.");
        Check.That(deaths[3] == (puff, "EntityDestroyed"), $"The puff is destroyed (not killed) at its lifetime {puff}; got {deaths[3]}.");
        Check.That(world.Metrics.Entities == 0 && world.Metrics.ScheduledUpdates == 0, "Expired entities leave no scheduled updates behind.");

        // A lifetime of zero is raised to one tick.
        var files = new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/Zero.ini"] = "Object Zero\n  Behavior = DeletionUpdate D\n    MinLifetime = 0\n    MaxLifetime = 0\n  End\nEnd\nObject Forever\n  Behavior = DeletionUpdate D\n    MinLifetime = -1\n    MaxLifetime = -1\n  End\nEnd\n",
        };
        var custom = new SyntheticFixture(files: files);
        var zeroWorld = NewWorld(custom, 9);
        var zeroFeeder = new Feeder(zeroWorld);
        zeroFeeder.Spawn(1, "Zero");
        zeroFeeder.Spawn(1, "Forever");
        zeroWorld.Step();
        var removed = zeroWorld.Step().Events.Single(value => value.Kind == SimEventKind.EntityDestroyed);
        Check.That(removed.Entity.Value == 1 && removed.Tick == 1, "A zero lifetime becomes one tick.");
        var forever = zeroWorld.FindEntity(new EntityId(2))!;
        Check.That(forever.Modules.OfType<DeletionUpdate>().Single().DieTick == 128849019, "-1 wraps to 4294967295 ms = 128849019 ticks like the reference (MD_GLA01 SalvageCrate).");
    }

    private static (SimulationWorld World, ProbeLog Log, SyntheticFixture Fixture) ProbeWorld()
    {
        var log = new ProbeLog();
        var registry = new ModuleRegistry(ModuleRegistry.Default.Descriptors.Append(new ModuleDescriptor("ProbeModule", ModuleBlockKind.Behavior,
            ModuleInterfaces.Update, ImmutableHashSet.Create(ModuleOwnerKind.Object), [
                new FieldSchema("FirstWake", FieldType.Integer), new FieldSchema("Sleep", FieldType.Integer), new FieldSchema("DestroyTarget", FieldType.Integer),
                new FieldSchema("DestroyAtTick", FieldType.Integer), new FieldSchema("EmitAtTick", FieldType.Integer),
            ], false, null, ModuleCategory.Simulation)));
        static long Field(ModuleConfiguration configuration, string name, long fallback)
            => configuration.Fields.FirstOrDefault(field => field.Name == name)?.Value.Value is long value ? value : fallback;
        var probe = new ModuleBinding<ProbeData>("ProbeModule", ModuleOwnerKind.Object,
            (configuration, _) => new ProbeData(Field(configuration, "FirstWake", 0), Field(configuration, "Sleep", CnCNew.Simulation.Time.SimulationTiming.SleepForever),
                (uint)Field(configuration, "DestroyTarget", 0), Field(configuration, "DestroyAtTick", -1), Field(configuration, "EmitAtTick", -1)),
            data => new ProbeModule(log, data));
        var bindings = new ModuleBindingTable(ModuleBindingTable.Default.Bindings.Append(probe).OrderBy(value => value.Name, StringComparer.Ordinal), registry);
        var files = new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/Probes.ini"] = """
            Object Wake4
              Behavior = ProbeModule P
                FirstWake = 4
                Sleep = 3
              End
            End
            Object Wake5
              Behavior = ProbeModule P
                FirstWake = 5
                Sleep = 3
              End
            End
            Object TwoProbes
              Behavior = ProbeModule PA
                FirstWake = 5
                Sleep = 100
              End
              Behavior = ProbeModule PB
                FirstWake = 5
                Sleep = 100
              End
            End
            Object Killer
              Behavior = ProbeModule P
                FirstWake = 5
                Sleep = 100
                DestroyTarget = 2
                DestroyAtTick = 5
              End
            End
            Object Victim
              Behavior = ProbeModule P
                FirstWake = 5
                Sleep = 1
              End
            End
            Object Emitter
              Behavior = ProbeModule P
                FirstWake = 2
                Sleep = 100
                EmitAtTick = 2
              End
            End
            """,
        };
        var fixture = new SyntheticFixture(bindings, files);
        return (NewWorld(fixture), log, fixture);
    }

    public static void UpdateOrderAndMutation()
    {
        {
            var (world, log, _) = ProbeWorld();
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Wake5");   // id 1
            feeder.Spawn(1, "Wake5");   // id 2
            feeder.Spawn(1, "Wake4");   // id 3
            feeder.Spawn(1, "TwoProbes"); // id 4
            for (var tick = 0; tick < 8; tick++) world.Step();
            var updates = log.Entries.Where(value => value.StartsWith("update", StringComparison.Ordinal)).ToArray();
            // Wake tick 4: entity 3; wake tick 5: entities 1, 2, 4 (twice: module order); then the sleeping ones by (wake, id).
            Check.That(updates.Take(6).SequenceEqual(["update:3:4", "update:1:5", "update:2:5", "update:4:5", "update:4:5", "update:3:7"]), "Update order is (wake tick, entity id, module index): " + string.Join(" ", updates));
            Check.That(log.Entries.Where(value => value.StartsWith("spawn", StringComparison.Ordinal)).SequenceEqual(["spawn:1:0", "spawn:2:0", "spawn:3:0", "spawn:4:0", "spawn:4:0"]), "OnSpawn runs in entity id order (and module order) in the spawn phase.");
        }
        {
            var (world, log, _) = ProbeWorld();
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Killer");  // id 1 destroys id 2 in tick 5
            feeder.Spawn(1, "Victim");  // id 2 would update at ticks 5, 6, 7...
            for (var tick = 0; tick < 8; tick++) world.Step();
            var entries = log.Entries;
            Check.That(entries.Contains("update:1:5") && !entries.Contains("update:2:5"), "A destroyed entity receives no further updates in the same tick even though its update was due: " + string.Join(" ", entries));
            Check.That(entries.Contains("event:2:EntityDestroyed:5"), "Its events are still delivered in that tick.");
            Check.That(entries.IndexOf("event:2:EntityDestroyed:5") < entries.IndexOf("remove:2:5"), "Events are delivered before the entity is removed.");
            Check.That(!entries.Any(value => value.StartsWith("update:2:", StringComparison.Ordinal)), "It never updates again.");
            Check.That(world.FindEntity(new EntityId(2)) is null && world.Metrics.ScheduledUpdates == 1, "The destroyed entity's update left the scheduler.");
        }
        {
            var (world, log, _) = ProbeWorld();
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Emitter");
            for (var tick = 0; tick < 4; tick++) world.Step();
            Check.That(log.Entries.Contains("event:1:FxRequested:2"), "An event raised in the update phase is delivered to its subject in the same tick.");
        }
        {
            // Mutation queues: a spawn requested by a command appears at the end of the same tick, is never updated in it,
            // and a Destroy of the same tick removes it no earlier than the next destroy phase.
            var (world, log, _) = ProbeWorld();
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Victim");
            world.Step();
            Check.That(log.Entries.Contains("spawn:1:0") && !log.Entries.Any(value => value.StartsWith("update", StringComparison.Ordinal)), "No update happens in the tick of creation.");
            feeder.Destroy(1, player: 1);
            world.Step();
            Check.That(world.FindEntity(new EntityId(1)) is null && log.Entries.Contains("remove:1:1"), "Destroy by the owner removes the entity in the destroy phase.");
        }
    }

    public static void TimerBehaviour()
    {
        var fixture = new SyntheticFixture();
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Barrel");
        world.Step(); // tick 0, entity 1
        var fired = new List<(long Tick, long Tag, uint Entity)>();
        void Run(int ticks)
        {
            for (var index = 0; index < ticks; index++)
                foreach (var simEvent in world.Step().Events.Where(value => value.Kind == SimEventKind.TimerFired))
                    fired.Add((simEvent.Tick, simEvent.Value, simEvent.Entity.Value));
        }
        feeder.Timer(1, 5, 3, TimerActionKind.Notify, tag: 11);          // timer 1: due tick 6, then every 3 ticks
        feeder.Timer(0, 4, 0, TimerActionKind.Notify, tag: 22);          // timer 2: world timer, due tick 5, once
        feeder.Timer(1, 4, 0, TimerActionKind.Notify, tag: 33);          // timer 3: due tick 5, fires after timer 2 (id order)
        Run(1);                                                          // tick 1 executes the commands
        Run(15);
        Check.That(fired.Select(value => (value.Tick, value.Tag)).SequenceEqual([(5, 22), (5, 33), (6, 11), (9, 11), (12, 11), (15, 11)]),
            "Timers fire ordered by (due tick, id); repeats re-arm relative to the firing tick: " + string.Join(" ", fired.Select(value => $"{value.Tick}:{value.Tag}")));
        Check.That(fired.Single(value => value.Tag == 22).Entity == 0, "A world timer has no entity.");
        Check.That(world.Metrics.Timers == 1, "Only the repeating timer remains.");
        Check.That(feeder.CancelTimer(1).Accepted, "Cancel accepted.");
        Run(1);
        Check.That(world.Metrics.Timers == 0, "Cancelled timers leave the table.");
        fired.Clear();
        Run(10);
        Check.That(fired.Count == 0, "A cancelled timer never fires.");
        feeder.CancelTimer(1);
        Run(1);
        Check.That(world.RejectionCounts[(int)CommandRejection.UnknownTimer] == 1, "Cancelling an unknown timer is rejected.");

        // Damage timers kill their owner, and removing the owner removes its timers.
        feeder.Timer(1, 0, 2, TimerActionKind.Damage, 25);
        Run(12);
        Check.That(world.FindEntity(new EntityId(1)) is null && world.Metrics.Timers == 0, "A damage timer kills the barrel (4 hits of 25); the timer disappears with its owner.");
        feeder.Spawn(1, "Barrel");
        Run(1);
        var second = world.Entities.Single().Id.Value;
        feeder.Timer(second, 3, 0, TimerActionKind.Kill);
        feeder.Timer(second, 50, 5, TimerActionKind.Notify);
        Run(1);
        Check.That(world.Metrics.Timers == 2, "Two timers are owned by the entity.");
        Run(6);
        Check.That(world.FindEntity(new EntityId(second)) is null && world.Metrics.Timers == 0, "Kill timer runs the die modules; the entity's other timers are cancelled on removal.");
        feeder.Spawn(1, "Building");
        Run(1);
        var building = world.Entities.Single().Id.Value;
        feeder.Timer(building, 2, 0, TimerActionKind.Destroy);
        Run(4);
        Check.That(world.FindEntity(new EntityId(building)) is null, "Destroy timer removes the entity without die modules.");
        Check.That(!feeder.Timer(0, 5, 0, TimerActionKind.Damage, 1).Accepted || world.RejectionCounts[(int)CommandRejection.InvalidArguments] == 1, "A world timer may only notify.");
    }

    public static void CommandValidationAndOrder()
    {
        var fixture = new SyntheticFixture();
        var world = NewWorld(fixture);
        // Execution order is (player id, sequence) regardless of submission order.
        var p1 = new PlayerId(1);
        var p2 = new PlayerId(2);
        world.Submit(new SpawnCommand(p2, 1, 3, "Barrel", PlayerId.System, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(p1, 1, 3, "Tank", PlayerId.System, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(p1, 2, 3, "Puff", PlayerId.System, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
        Check.That(world.Metrics.PendingCommands == 3, "Future commands wait in the queue.");
        for (var tick = 0; tick < 4; tick++) world.Step();
        var byId = world.Entities.OrderBy(value => value.Id.Value).Select(value => $"{value.Id}:{value.Template.Name}:{value.Owner}").ToArray();
        Check.That(byId.SequenceEqual(["1:Tank:1", "2:Puff:1", "3:Barrel:2"]), "Commands of one tick run ordered by player then sequence: " + string.Join(" ", byId));

        var feeder = new Feeder(world);
        var current = world.Tick;
        void Rejects(SubmitResult result, CommandRejection reason, string what)
        {
            Check.That(!result.Accepted && result.Reason == reason, $"{what}: expected {reason}, got {(result.Accepted ? "accepted" : result.Reason.ToString())}.");
        }
        Rejects(world.Submit(new KillCommand(p1, 2, current, new EntityId(1))), CommandRejection.Duplicate, "Same (player, sequence) twice");
        Rejects(world.Submit(new KillCommand(p1, 2, current + 5, new EntityId(1))), CommandRejection.Duplicate, "Duplicate at another tick");
        Rejects(world.Submit(new DestroyCommand(p1, 2, current, new EntityId(1))), CommandRejection.Duplicate, "Duplicate with other content");
        Rejects(world.Submit(new DestroyCommand(p1, 1, current, new EntityId(1))), CommandRejection.OutOfOrder, "A sequence below the last accepted one");
        Check.That(feeder.Spawn(1, "Barrel", current).Accepted == false, "A fresh sender restarting at sequence 1 is refused.");
        Rejects(world.Submit(new DestroyCommand(p1, 0, current, new EntityId(1))), CommandRejection.InvalidArguments, "Sequence 0");
        var late = world.Submit(new DestroyCommand(p1, 10, current - 1, new EntityId(1)));
        Rejects(late, CommandRejection.Late, "A tick that already ran");
        Rejects(world.Submit(new DestroyCommand(new PlayerId(9), 1, current, new EntityId(1))), CommandRejection.UnknownPlayer, "Unknown player");
        Rejects(world.Submit(new KillCommand(p1, 11, current, new EntityId(1))), CommandRejection.NotPermitted, "System-only command by a player");
        Rejects(world.Submit(new SpawnCommand(p1, 12, current, "NoSuchTemplate", PlayerId.System, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero)), CommandRejection.UnknownTemplate, "Unknown template");
        Rejects(world.Submit(new SpawnCommand(p1, 13, current, "Barrel", p2, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero)), CommandRejection.NotPermitted, "Spawning for another player");
        Rejects(world.Submit(new SpawnCommand(p1, 14, current, "Barrel", new PlayerId(7), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero)), CommandRejection.UnknownPlayer, "Unknown owner");
        Rejects(world.Submit(new SetTimerCommand(PlayerId.System, 1, current, new EntityId(1), -1, 0, TimerActionKind.Notify, Fixed.Zero, 0, 0)), CommandRejection.InvalidArguments, "Negative delay");
        Rejects(world.Submit(new DamageCommand(PlayerId.System, 2, current, new EntityId(1), Fixed.FromInt(-5), 0, 0)), CommandRejection.InvalidArguments, "Negative damage");
        Rejects(world.Submit(new DamageCommand(PlayerId.System, 3, current, new EntityId(1), Fixed.FromInt(5), 99, 0)), CommandRejection.InvalidArguments, "Invalid damage type");
        // A sequence lower than the last accepted one is out of order.
        Check.That(world.Submit(new DestroyCommand(p2, 20, current, new EntityId(3))).Accepted, "A later sequence is accepted.");
        Rejects(world.Submit(new DestroyCommand(p2, 19, current, new EntityId(3))), CommandRejection.OutOfOrder, "A lower sequence");

        // Execution-time validation: ownership and existence.
        Check.That(world.Submit(new DestroyCommand(p1, 30, current, new EntityId(3))).Accepted, "Accepted at submit time; checked at execution.");
        Check.That(world.Submit(new DestroyCommand(p1, 31, current, new EntityId(999))).Accepted, "Unknown targets are detected at execution.");
        var result = world.Step();
        var rejections = result.Events.Where(value => value.Kind == SimEventKind.CommandRejected).Select(value => (CommandRejection)value.Value).ToArray();
        Check.That(rejections.TakeLast(2).SequenceEqual([CommandRejection.NotPermitted, CommandRejection.UnknownEntity]), "Execution rejections are events: " + string.Join(",", rejections));
        Check.That(rejections.Length == world.RejectionCounts.Sum(), "Every counted rejection raised exactly one event.");
        Check.That(world.FindEntity(new EntityId(3)) is null, "The owner's destroy (player 2, sequence 20) executed first.");
        Check.That(world.RejectionCounts[(int)CommandRejection.Duplicate] == 3 && world.RejectionCounts[(int)CommandRejection.NotPermitted] == 3, "Rejections are counted per reason.");
        Check.That(world.Metrics.PendingEvents == 0, "Rejection events queued outside a tick were delivered by the next tick.");
    }
}
