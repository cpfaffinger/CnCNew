using System.Collections.Immutable;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The shock wave's model conditions (F-63, step 4; <c>Object::attemptDamage</c>, <c>PhysicsBehavior::update</c>): a tank thrown from the ground shows STUNNED_FLAILING from the
/// shock on and keeps it after it has landed, because its stun ended before the landing - the original's leftover; it never shows STUNNED.
/// </summary>
internal static class ShockConditionTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/ShockConditionFixture.ini"] = """
        Weapon ScBomb
          PrimaryDamage = 1
          PrimaryDamageRadius = 100.0
          AttackRange = 200.0
          DamageType = EXPLOSION
          ShockWaveAmount = 50.0
          ShockWaveRadius = 100.0
          ShockWaveTaperOff = 0.33
        End
        Object ScTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 5.0
          End
        End
        Object ScLauncher
          KindOf = STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("ShockConditions");
        t.Run("a tank thrown from the ground flails from the shock on and keeps flailing after it has landed, never STUNNED", Flailing);
        return t.Failures;
    }

    private static void Flailing()
    {
        var fixture = new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), CnCNew.Tests.TestRate.Default, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        world.Submit(new SpawnCommand(PlayerId.System, 1, world.Tick, "ScLauncher", new PlayerId(1), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, 2, world.Tick, "ScTank", new PlayerId(2), Fixed.FromInt(50), Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Step();
        var tank = world.Entities.Single(entity => entity.Template.Name == "ScTank");
        Assert(world.Weapons.TryGet("ScBomb", out var bomb), "Setup: the bomb is in the catalog.");
        ModelConditionSet Conditions() => ConditionDeriver.Derive(tank, world, null, default);
        Assert(!Conditions().Contains(UnitFlags.StunnedFlailing), "Before the shock: no stun conditions.");
        world.DealWeaponDamage(world.Entities.Single(entity => entity.Template.Name == "ScLauncher"), bomb!, null, Fixed.Zero, Fixed.Zero, Fixed.Zero);
        Assert(Conditions().Contains(UnitFlags.StunnedFlailing) && !Conditions().Contains(UnitFlags.Stunned), "The shock: STUNNED_FLAILING.");
        var flew = false;
        for (var step = 0; step < 3000 && (!flew || tank.InPhysicsFlight); step++)
        {
            world.Step();
            flew |= tank.InPhysicsFlight;
            Assert(!Conditions().Contains(UnitFlags.Stunned), "It never shows STUNNED: its stun ended before it landed.");
        }
        Assert(flew && !tank.InPhysicsFlight && Conditions().Contains(UnitFlags.StunnedFlailing), "Landed, it still flails.");
    }
}
