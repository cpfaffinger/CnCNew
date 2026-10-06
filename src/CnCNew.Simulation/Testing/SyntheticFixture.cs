using System.Collections.Immutable;
using System.Text;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.Snapshots;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Testing;

/// <summary>An <see cref="IDefinitionSource"/> over in-memory INI text (no file system access).</summary>
public sealed class InMemoryDefinitionSource(IReadOnlyDictionary<string, string> files) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => files.Keys;
    public Stream OpenRead(string virtualPath) => new MemoryStream(Encoding.Latin1.GetBytes(files[virtualPath]));
    public bool Exists(string virtualPath) => files.ContainsKey(virtualPath);
}

/// <summary>
/// The small, legally unproblematic synthetic definition set that the phase-4 tests, the stress test and the replay tool run
/// on. It mirrors the shapes found in the shipped data (default template with body and die module, explicit
/// <c>DestroyDie</c> next to other die modules, lifetime and deletion updates) without reproducing any original content.
/// </summary>
public sealed class SyntheticFixture
{
    public const string FixtureId = "synthetic-lifecycle-v1";

    public static IReadOnlyDictionary<string, string> Files { get; } = new Dictionary<string, string>
    {
        ["Data/INI/Default/Object.ini"] = """
        Object DefaultThingTemplate
          Side = Civilian
          KindOf = NONE
          BuildTime = 1
          Geometry = SPHERE
          GeometryMajorRadius = 1.0
          GeometryMinorRadius = 1.0
          GeometryHeight = 1.0
          GeometryIsSmall = Yes
          Body = InactiveBody ModuleTag_DefaultInactiveBody
          End
          Behavior = DestroyDie ModuleTag_DefaultDestroyDie
          End
          Draw = W3DDefaultDraw ModuleTag_DefaultW3DDefaultDraw
          End
        End
        """,
        ["Data/INI/FXList.ini"] = "FXList FX_Test\nEnd\nFXList FX_Other\nEnd\n",
        ["Data/INI/Weapon.ini"] = "Weapon TestWeapon\n  PrimaryDamage = 10\nEnd\n",
        ["Data/INI/ObjectCreationList.ini"] = "ObjectCreationList OCL_BarrelDebris\nEnd\nObjectCreationList OCL_Wreck\nEnd\nObjectCreationList OCL_Damaged\nEnd\nObjectCreationList OCL_ReallyDamaged\nEnd\nObjectCreationList OCL_Rubble\nEnd\n",
        ["Data/INI/Object/Fixture.ini"] = """
        Object Barrel
          KindOf = SELECTABLE STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
            DeathTypes = ALL -CRUSHED
          End
          Behavior = FXListDie Fx01
            DeathFX = FX_Test
            DeathTypes = ALL -CRUSHED
          End
          Behavior = CreateObjectDie Oc01
            CreationList = OCL_BarrelDebris
            DeathTypes = ALL -CRUSHED
          End
          Draw = W3DModelDraw Draw01
          End
        End

        Object Tank
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = InstantDeathBehavior Death01
            FX = FX_Test FX_Other
            OCL = OCL_Wreck
            Weapon = TestWeapon
          End
        End

        Object Building
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = KeepObjectDie Rubble01
          End
        End

        Object Spark
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = LifetimeUpdate Life01
            MinLifetime = 500
            MaxLifetime = 1500
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = FXListDie Fx01
            DeathFX = FX_Other
          End
        End

        Object Puff
          Behavior = DeletionUpdate Delete01
            MinLifetime = 300
            MaxLifetime = 900
          End
        End

        Object Ghost
          Body = ImmortalBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
        End

        Object Highlander
          Body = HighlanderBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Prop
        End

        Object Sleeper
          Body = ActiveBody Body01
            MaxHealth = 20.0
            InitialHealth = 20.0
          End
          Behavior = DestroyDie Destroy01
            ExemptStatus = UNDER_CONSTRUCTION
            VeterancyLevels = ALL -VETERAN
          End
          Behavior = FXListDie Fx01
            DeathFX = FX_Test
            StartsActive = No
          End
        End
        """,
    };

    /// <param name="rate">The tick rate the catalog is built for; worlds created from this fixture must be configured for the same rate.</param>
    public SyntheticFixture(SimulationRate rate, ModuleBindingTable? bindings = null, IReadOnlyDictionary<string, string>? files = null)
    {
        Bindings = bindings ?? ModuleBindingTable.Default;
        Graph = DefinitionCompiler.Compile(new InMemoryDefinitionSource(files ?? Files), Bindings.Registry,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, StrictReferences = true });
        Fingerprint = rate.Combine(DefinitionSnapshotWriter.ComputeFingerprint(Graph).Sha256);
        Templates = TemplateCatalog.Build(Graph, Bindings, rate);
    }

    public ModuleBindingTable Bindings { get; }
    public CompiledDefinitionGraph Graph { get; }
    public string Fingerprint { get; }
    public TemplateCatalog Templates { get; }

    /// <summary>The tick rate <see cref="Templates"/> was built for.</summary>
    public SimulationRate Rate => Templates.Rate;

}
