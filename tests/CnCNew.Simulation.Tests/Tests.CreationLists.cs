using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> CreationListFiles() => new Dictionary<string, string>(Phase5Files())
    {
        ["Data/INI/Object/CreationListFixture.ini"] = """
        ObjectCreationList OCL_Three
          CreateObject
            ObjectNames = Bit
            Count = 3
            Offset = X:2 Y:0 Z:0
          End
        End
        ObjectCreationList OCL_Spread
          CreateObject
            ObjectNames = Bit
            Count = 4
            SpreadFormation = Yes
            MinDistanceAFormation = 5
            MinDistanceBFormation = 5
            MaxDistanceFormation = 10
          End
        End
        ObjectCreationList OCL_Contained
          CreateObject
            ObjectNames = Rebel
            PutInContainer = Bunker
          End
        End
        ObjectCreationList OCL_Inside
          CreateObject
            ObjectNames = Rebel
            ContainInsideSourceObject = Yes
          End
        End
        ObjectCreationList OCL_Health
          CreateObject
            ObjectNames = Bit
            MinHealth = 50%
            MaxHealth = 50%
          End
        End
        ObjectCreationList OCL_Veteran
          CreateObject
            ObjectNames = Bit
            InheritsVeterancy = Yes
          End
          CreateObject
            ObjectNames = Plain
            InheritsVeterancy = Yes
          End
        End
        ObjectCreationList OCL_Lifetime
          CreateObject
            ObjectNames = Timed
            MinLifetime = 1000
            MaxLifetime = 1000
          End
        End
        ObjectCreationList OCL_Wreckage
          CreateObject
            ObjectNames = Bit
          End
        End
        ObjectCreationList OCL_Skips
          CreateObject
            ObjectNames = Pending
          End
          CreateObject
            ObjectNames = Bit
            PutInContainer = Pending
          End
          CreateObject
            ObjectNames = Bit
            InvulnerableTime = 1000
            DiesOnBadLand = Yes
          End
          CreateDebris
            ModelNames = Debris
          End
          CreateObject
          End
        End
        ObjectCreationList OCL_Airborne
          CreateObject
            ObjectNames = Bit
            SkipIfSignificantlyAirborne = Yes
          End
        End
        ObjectCreationList OCL_Random
          CreateObject
            ObjectNames = Bit Plain
            Count = 6
            MinHealth = 10%
            MaxHealth = 90%
            SpreadFormation = Yes
            MinDistanceAFormation = 1
            MinDistanceBFormation = 3
            MaxDistanceFormation = 10
          End
        End

        Object Bit
          KindOf = INFANTRY
          IsTrainable = Yes
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Plain
          KindOf = INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Timed
          KindOf = INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = LifetimeUpdate Life01
            MinLifetime = 100000
            MaxLifetime = 100000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Pending
          KindOf = INFANTRY
          Behavior = SpawnBehavior Stand01
            CanReclaimOrphans = Yes
          End
        End
        Object Wreckable
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = CreateObjectDie Oc01
            CreationList = OCL_Wreckage
            TransferPreviousHealth = Yes
          End
        End
        """,
    };

    private static SimulationWorld NewCreationListWorld(SyntheticFixture fixture, uint seed = 5)
        => new(new WorldConfiguration(new WorldId(1), TestRate.Default, seed, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);

    public static void Phase5CreationLists()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: CreationListFiles());
        var world = NewCreationListWorld(fixture);
        var feeder = new Feeder(world);
        Check.That(world.CreationLists.Lists.Length == 16 && world.CreationLists.TryGet("OCL_Spread", out var spread) && spread.Nuggets[0].SpreadFormation && spread.Nuggets[0].Count == 4,
            "The catalog holds every list with typed nuggets: " + world.CreationLists.Lists.Length);

        feeder.Spawn(1, "Plain", owner: 1, x: 100, y: 50);
        world.Step();
        var source = world.Entities.Single();
        IReadOnlyList<SimEvent> Run(string list, EntityId? requester = null, bool transfer = false)
        {
            world.CreateFromList(source, list, requester ?? EntityId.Invalid, transfer);
            return world.Step().Events;
        }
        IEnumerable<Entity> Created(string template) => world.Entities.Where(value => value.Template.Name == template && value.Id != source.Id);
        void Clear() { foreach (var entity in world.Entities.Where(value => value.Id != source.Id).ToArray()) world.RequestDestroy(entity.Id); world.Step(); }

        // ---- count, offset and ownership
        var events = Run("OCL_Three");
        var bits = Created("Bit").ToArray();
        Check.That(bits.Length == 3 && bits.All(value => value.Owner == source.Owner && value.X == Fixed.FromInt(102) && value.Y == Fixed.FromInt(50)),
            "Count creates that many objects at the source, owned by the source's player and shifted by the rotated offset: " + string.Join(" ", bits.Select(value => $"{value.Owner.Value}@{value.X},{value.Y}")));
        Check.That(events.Count(value => value.Kind == SimEventKind.CreationListRequested && value.Text == "OCL_Three") == 1 && events.Count(value => value.Kind == SimEventKind.EntitySpawned) == 3,
            "One request event and one spawn event per object.");
        Clear();

        // ---- spread formation
        Run("OCL_Spread");
        bits = Created("Bit").ToArray();
        var ring = bits.Select(value => (value.X - Fixed.FromInt(100)) * (value.X - Fixed.FromInt(100)) + (value.Y - Fixed.FromInt(50)) * (value.Y - Fixed.FromInt(50))).ToArray();
        Check.That(bits.Length == 4 && ring.All(value => value > Fixed.FromInt(24) && value < Fixed.FromInt(26)),
            "SpreadFormation places every object at the innermost ring (radius 5): " + string.Join(", ", ring.Select(value => value.ToString())));
        Check.That(bits.Select(value => (value.X.Raw, value.Y.Raw)).Distinct().Count() == 4, "Each formation member draws its own start angle.");
        Clear();

        // ---- container
        events = Run("OCL_Contained");
        var bunker = Created("Bunker").Single();
        var rebel = Created("Rebel").Single();
        Check.That(bunker.Owner == source.Owner && events.Any(value => value.Kind == SimEventKind.EntityContained && value.Entity == rebel.Id && value.Other == bunker.Id),
            "PutInContainer creates the container and puts the object inside it.");
        Clear();

        // ---- contain inside the source object
        Run("OCL_Inside");
        // Destroyed in the spawn phase, removed by the next logic frame's destroy phase (the next tick at 30 Hz).
        for (var i = 0; i < world.Frames(1); i++) world.Step();
        Check.That(!Created("Rebel").Any(), "A source without a container destroys the object created for it.");
        Clear();
        feeder.Spawn(1, "Bunker", owner: 1, x: 200, y: 200);
        world.Step();
        var host = world.Entities.Single(value => value.Template.Name == "Bunker");
        world.CreateFromList(host, "OCL_Inside", EntityId.Invalid, false);
        events = world.Step().Events;
        rebel = Created("Rebel").Single();
        Check.That(events.Any(value => value.Kind == SimEventKind.EntityContained && value.Entity == rebel.Id && value.Other == host.Id), "ContainInsideSourceObject adds the object to the source's container.");
        world.Kill(host.Id);
        world.Step();
        Clear();

        // ---- health, veterancy, lifetime
        Run("OCL_Health");
        Check.That(Created("Bit").Single().Body!.Health == Fixed.FromInt(50), "MinHealth/MaxHealth set the initial health percentage.");
        Clear();
        world.SetVeterancy(source.Id, 2);
        Run("OCL_Veteran");
        Check.That(Created("Bit").Single().VeterancyLevel == 2 && Created("Plain").Single().VeterancyLevel == 0,
            "InheritsVeterancy passes the level on to trainable templates only.");
        Clear();
        world.SetVeterancy(source.Id, 0);
        Run("OCL_Lifetime");
        var timed = Created("Timed").Single();
        var spawnedAt = world.Tick;
        while (world.FindEntity(timed.Id) is not null && world.Tick < spawnedAt + world.Frames(200)) world.Step();
        Check.That(world.FindEntity(timed.Id) is null && world.InFrames(world.Tick - spawnedAt) is >= 25 and <= 35, "MinLifetime/MaxLifetime override the object's own lifetime range: " + (world.Tick - spawnedAt));

        // ---- skipped members are reported, never dropped
        events = Run("OCL_Skips");
        var skips = events.Where(value => value.Kind == SimEventKind.CreationSkipped).Select(value => $"{value.Text}:{value.Value}").ToArray();
        Check.That(skips.SequenceEqual([
            $"Pending:{CreationSkipReasons.TemplateNotInstantiable}", $"Pending:{CreationSkipReasons.ContainerNotInstantiable}",
            $"InvulnerableTime:{CreationSkipReasons.SettingNotSimulated}", $"DiesOnBadLand:{CreationSkipReasons.SettingNotSimulated}",
            $"CreateDebris:{CreationSkipReasons.NuggetNotSimulated}", $"CreateObject:{CreationSkipReasons.NoObjectNamed}"]),
            "Every skipped nugget, template and setting raises exactly one event: " + string.Join(" | ", skips));
        Check.That(Created("Bit").Count() == 1, "Only the member that can be simulated is created.");
        Clear();
        events = Run("OCL_DoesNotExist");
        Check.That(events.Count(value => value.Kind == SimEventKind.CreationSkipped && value.Value == CreationSkipReasons.UnknownList) == 1, "An unknown list is an explicit event.");

        // ---- airborne source
        Check.That(world.Submit(new SpawnCommand(new PlayerId(2), 1, world.Tick, "Plain", new PlayerId(2), Fixed.FromInt(300), Fixed.FromInt(300), Fixed.FromInt(50), Fixed.Zero)).Accepted, "Spawn of an airborne source.");
        world.Step();
        var flyer = world.Entities.Single(value => value.Z == Fixed.FromInt(50));
        world.CreateFromList(flyer, "OCL_Airborne", EntityId.Invalid, false);
        world.Step();
        Check.That(!Created("Bit").Any(), "SkipIfSignificantlyAirborne creates nothing for a source high above the terrain.");
        world.CreateFromList(source, "OCL_Airborne", EntityId.Invalid, false);
        world.Step();
        Check.That(Created("Bit").Count() == 1, "A grounded source creates the object.");
        Clear();

        // ---- TransferPreviousHealth through CreateObjectDie
        feeder.Spawn(1, "Wreckable", owner: 1, x: 400, y: 400);
        world.Step();
        var wreck = world.Entities.Single(value => value.Template.Name == "Wreckable");
        feeder.Damage(wreck.Id.Value, 30);
        world.Step();
        feeder.Damage(wreck.Id.Value, 500);
        world.Step();
        world.Step();
        Check.That(Created("Bit").Single().Body!.Health == Fixed.FromInt(70), "TransferPreviousHealth applies the damage the wreck had taken to the created object.");
    }

    public static void Phase5CreationListDeterminism()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: CreationListFiles());
        SimulationWorld Build()
        {
            var world = NewCreationListWorld(fixture, 11);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Plain", owner: 1, x: 10, y: 10);
            world.Step();
            var source = world.Entities.Single();
            world.SetVeterancy(source.Id, 1);
            world.CreateFromList(source, "OCL_Random", EntityId.Invalid, false);
            world.CreateFromList(source, "OCL_Veteran", EntityId.Invalid, false);
            world.CreateFromList(source, "OCL_Lifetime", EntityId.Invalid, false);
            return world;
        }
        var (first, second) = (Build(), Build());
        Check.That(first.StateHash == second.StateHash && first.Metrics.PendingSpawns > 0, "Two runs queue identical spawns: " + first.Metrics.PendingSpawns);

        // The queued spawn setup (health, veterancy, lifetime, containment) must survive a snapshot.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(first), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == first.StateHash && restored.Metrics == first.Metrics, "The spawn queue round-trips through the snapshot.");
        for (var tick = 0; tick < first.Frames(90); tick++)
            Check.That(first.Step().StateHash == restored.Step().StateHash && second.Step().StateHash == first.StateHash, "Restored and independent worlds stay hash-identical.");
        var healths = first.Entities.Where(value => value.Template.Name is "Bit" or "Plain" && value.Id.Value != 1).Select(value => value.Body!.Health).ToArray();
        Check.That(healths.Length >= 6 && healths.Any(value => value < Fixed.FromInt(100)) && healths.Distinct().Count() > 1, "The random health range produced varied health values.");
        Check.That(first.Entities.All(value => value.Template.Name != "Timed"), "The overridden lifetime ran out in every world.");
    }

    public static void FixedTrigAccuracy()
    {
        Check.That(FixedTrig.Sin(Fixed.Zero) == Fixed.Zero && FixedTrig.Cos(Fixed.Zero) == Fixed.One, "Sin(0) = 0 and Cos(0) = 1 exactly.");
        var worst = 0.0;
        for (var raw = -3 * FixedTrig.TwoPi.Raw; raw <= 3 * FixedTrig.TwoPi.Raw; raw += 97)
        {
            var angle = Fixed.FromRaw(raw);
            var (sin, cos) = (FixedTrig.Sin(angle), FixedTrig.Cos(angle));
            worst = Math.Max(worst, Math.Max(Math.Abs((double)sin.ToDecimal() - Math.Sin((double)angle.ToDecimal())), Math.Abs((double)cos.ToDecimal() - Math.Cos((double)angle.ToDecimal()))));
        }
        Check.That(worst < 0.0001, "Sin and Cos stay within 1e-4 of the exact functions over three turns: " + worst);
    }
}
