using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The cursor's question "is the target in range of the selection" (<c>SimulationQueries.InRange</c>, asked on every hover over an enemy and by the
/// standing orders) for a weapon whose range is the "everywhere" value of the data (999,999, the Scud Storm's): the square of that range does not
/// fit a fixed-point number at 24 fraction bits, and the question threw until it compared through <c>SquaredReach</c>
/// (docs/FIXED-POINT-Q39-24.md §5).
/// </summary>
internal static class SentinelRangeTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/SentinelRangeFixture.ini"] = """
        Weapon EverywhereGun
          PrimaryDamage = 10
          AttackRange = 999999.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon NearGun
          PrimaryDamage = 10
          AttackRange = 50.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Object EverywhereLauncher
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY EverywhereGun
          End
        End
        Object NearSoldier
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY NearGun
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(SentinelRangeTests));
        harness.Run(nameof(InRangeAnswersForAnEverywhereRange), InRangeAnswersForAnEverywhereRange);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length) Console.WriteLine("SKIP SentinelRangeTests real-data test (pass --content-dir <dir>)");
        else harness.Run(nameof(RetailObjectsWithAnEverywhereRangeAnswer), () => RetailObjectsWithAnEverywhereRangeAnswer(args[index + 1]));
        return harness.Failures;
    }

    /// <summary>Every retail object that carries a weapon with a range beyond the square limit (the Scud Storms, the carrier, three cinematic soldiers) is asked the cursor's question.</summary>
    private static void RetailObjectsWithAnEverywhereRangeAnswer(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var graph = DefinitionCompiler.Compile(new ContentSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, SimulationRate.Original);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var limit = Fixed.Sqrt(Fixed.MaxValue);
        var asked = new List<string>();
        var weapons = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var template in catalog.Templates.OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            var far = template.WeaponSets.SelectMany(set => set.Weapons).Where(slot => slot >= 0).Select(slot => catalog.Weapons[slot]).Where(weapon => weapon.AttackRange > limit).ToArray();
            if (far.Length == 0) continue;
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 7, Players, "sentinel-range", fingerprint), catalog);
            ulong sequence = 0;
            foreach (var (name, owner, x) in new[] { (template.Name, 1, 300), ("ChinaTankBattleMaster", 2, 700) })
                Assert(world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, name, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero)).Accepted, name + " spawns");
            world.Step();
            var source = world.FindEntity(new EntityId(1))!;
            Assert(SimulationQueries.WeaponRange(world, source) > limit, $"{template.Name}: its reach is beyond the square limit");
            Assert(SimulationQueries.InRange(world, source, world.FindEntity(new EntityId(2))!), $"{template.Name}: a target 400 units away is in range");
            asked.Add(template.Name);
            foreach (var weapon in far) weapons.Add(weapon.Name);
        }
        Console.WriteLine($"     asked {asked.Count} objects ({string.Join(", ", asked)}) carrying {weapons.Count} weapons ({string.Join(", ", weapons)})");
        Assert(weapons.Count == 8 && asked.Count == 11, $"the eight retail weapons with an everywhere range on eleven objects ({weapons.Count} weapons, {asked.Count} objects)");
    }

    private sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }

    private static void InRangeAnswersForAnEverywhereRange()
    {
        var fixture = new SyntheticFixture(SimulationRate.Original, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 7, Players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        ulong sequence = 0;
        void Spawn(string template, int owner, int x, int y)
            => Assert(world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns");
        Spawn("EverywhereLauncher", 1, 100, 100);   // id 1
        Spawn("NearSoldier", 1, 100, 200);          // id 2
        Spawn("NearSoldier", 2, 900, 700);          // id 3
        world.Step();
        var launcher = world.FindEntity(new EntityId(1))!;
        var soldier = world.FindEntity(new EntityId(2))!;
        var target = world.FindEntity(new EntityId(3))!;

        Assert(SimulationQueries.WeaponRange(world, launcher) == Fixed.FromInt(999999), "the launcher reaches 999,999 units");
        Assert(SimulationQueries.InRange(world, launcher, target), "a target 1,000 units away is in a range of 999,999");
        Assert(!SimulationQueries.InRange(world, soldier, target), "and out of a range of 50");
        Assert(SimulationQueries.InRange(world, soldier, soldier), "an ordinary range still answers as before");
    }
}
