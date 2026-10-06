using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> CrateFiles() => new Dictionary<string, string>(Phase5Files())
    {
        ["Data/INI/Object/CrateFixture.ini"] = """
        CrateData AlwaysCrate
          CreationChance = 1.0
          CrateObject = FixtureCrate 1.0
        End
        CrateData NeverCrate
          CreationChance = 0
          CrateObject = FixtureCrate 1.0
        End
        CrateData ScienceCrate
          CreationChance = 1.0
          KilledByType = VEHICLE
          KillerScience = SCIENCE_Intrinsic
          CrateObject = PendingCrate 1.0
          OwnedByMaker = Yes
        End
        CrateData EliteCrate
          CreationChance = 1.0
          VeterancyLevel = ELITE
          CrateObject = FixtureCrate 1.0
        End
        CrateData ShortWeights
          CreationChance = 1.0
          CrateObject = FixtureCrate 0.0
        End

        Object FixtureCrate
          KindOf = CRATE
        End
        Object PendingCrate
          KindOf = CRATE
          Behavior = SpawnBehavior Stand01
            CanReclaimOrphans = Yes
          End
        End

        Object AlwaysVictim
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = CreateCrateDie Crate01
            CrateData = AlwaysCrate
          End
        End
        Object NeverVictim
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = CreateCrateDie Crate01
            CrateData = NeverCrate
            CrateData = ShortWeights
          End
        End
        Object ScienceVictim
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = CreateCrateDie Crate01
            CrateData = ScienceCrate
            CrateData = EliteCrate
          End
        End
        """,
    };

    private static ulong RandomFingerprint(SimulationWorld world)
    {
        Span<uint> state = stackalloc uint[6];
        world.LogicRandom.CopyState(state);
        ulong hash = 14695981039346656037UL;
        foreach (var word in state) hash = (hash ^ word) * 1099511628211UL;
        return hash;
    }

    public static void Phase5CreateCrateDie()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: CrateFiles());
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha", "TestFaction"), new(new PlayerId(2), "Beta")];
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 7, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);

        Check.That(world.Crates.Crates.Length == 5, "The catalog holds every CrateData definition.");
        Check.That(world.Crates.TryGet("EliteCrate", out var elite) && elite.VeterancyLevel == 2 && elite.Objects.Length == 1, "VeterancyLevel is typed (ELITE = 2).");
        Check.That(world.Crates.TryGet("ScienceCrate", out var science) && science.OwnedByMaker && science.KilledByType.SequenceEqual(new[] { "VEHICLE" }) && science.KillerScience == "SCIENCE_Intrinsic",
            "KilledByType, KillerScience and OwnedByMaker are typed.");

        // One shooter per victim because of shot delays; ids are looked up by position since spawn commands are ordered per player.
        void SpawnAt(string template, int owner, int x) => feeder.Spawn(owner, template, owner: owner, x: x, y: 0);
        SpawnAt("Fighter", 1, 0); SpawnAt("Fighter", 1, 1); SpawnAt("Fighter", 1, 2); SpawnAt("Fighter", 1, 3);
        SpawnAt("Fighter", 2, 4);
        SpawnAt("AlwaysVictim", 1, 10);
        SpawnAt("AlwaysVictim", 2, 20);
        SpawnAt("NeverVictim", 2, 30);
        SpawnAt("ScienceVictim", 2, 40);
        SpawnAt("ScienceVictim", 1, 5);
        world.Step();
        uint IdAt(int x) => world.Entities.Single(value => value.X == Fixed.FromInt(x)).Id.Value;
        var (f1, f2, f3, f4, f5) = (IdAt(0), IdAt(1), IdAt(2), IdAt(3), IdAt(4));
        var (allyVictim, enemyVictim, neverVictim, scienceVictim, noScienceVictim) = (IdAt(10), IdAt(20), IdAt(30), IdAt(40), IdAt(5));
        IReadOnlyList<SimEvent> Requests(IReadOnlyList<SimEvent> events) => events.Where(value => value.Kind == SimEventKind.CrateRequested).ToList();

        var before = RandomFingerprint(world);
        feeder.FireWeapon(1, f1, allyVictim);
        var result = world.Step();
        Check.That(world.FindEntity(new EntityId(allyVictim)) is null, "The own-side victim died.");
        Check.That(Requests(result.Events).Count == 0, "Killing an ally never produces a crate.");
        Check.That(RandomFingerprint(world) == before, "The ally exit consumes no random numbers.");

        feeder.FireWeapon(1, f2, enemyVictim);
        result = world.Step();
        var requested = Requests(result.Events);
        Check.That(requested.Count == 1 && requested[0].Text == "FixtureCrate" && requested[0].Value == PlayerId.System.Value, "An enemy kill requests exactly the neutral crate.");
        world.Step();
        Check.That(world.Entities.Any(value => value.Template.Name == "FixtureCrate" && value.Owner == PlayerId.System && value.X == Fixed.FromInt(20)),
            "An instantiable crate is spawned at the victim's position, owned by the system player.");

        before = RandomFingerprint(world);
        feeder.FireWeapon(1, f3, neverVictim);
        result = world.Step();
        Check.That(world.FindEntity(new EntityId(neverVictim)) is null && Requests(result.Events).Count == 0, "CreationChance 0 requests nothing.");
        Check.That(RandomFingerprint(world) != before, "The failed creation-chance draw still consumes the reference's draw.");

        feeder.FireWeapon(1, f4, scienceVictim);
        result = world.Step();
        requested = Requests(result.Events);
        Check.That(requested.Count == 1 && requested[0].Text == "PendingCrate" && requested[0].Value == 2,
            "Killer science and kind-of pass: the crate is requested and owned by the victim's player; the elite-only entry is skipped.");
        world.Step();
        Check.That(!world.Entities.Any(value => value.Template.Name == "PendingCrate"), "A crate whose modules are not fully implemented is reported but never spawned.");

        feeder.FireWeapon(2, f5, noScienceVictim);
        result = world.Step();
        Check.That(world.FindEntity(new EntityId(noScienceVictim)) is null && Requests(result.Events).Count == 0, "A killer without the required science gets no crate.");
    }
}
