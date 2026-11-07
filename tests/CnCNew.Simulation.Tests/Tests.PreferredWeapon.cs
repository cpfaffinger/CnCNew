using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// The owner's decision (docs/SIMULATION-CORE.md §87): a weapon preferred against a victim holds the choice while its clip has rounds, not while it reloads an empty clip.
// WeaponSet::chooseBestWeaponForTarget keeps a preferred weapon that merely reloads, so the original's Comanche hovers until its missiles are back.
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> PreferredWeaponFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/PreferredWeapon.ini"] = """
        Weapon PwCannon
          PrimaryDamage = 5
          AttackRange = 150.0
          DamageType = ARMOR_PIERCING
          ClipSize = 0
          DelayBetweenShots = 200
        End
        Weapon PwMissile
          PrimaryDamage = 40
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 2
          AutoReloadsClip = Yes
          ClipReloadTime = 6000
          DelayBetweenShots = 300
        End
        Object PwHelicopter
          KindOf = SELECTABLE VEHICLE CAN_ATTACK
          VisionRange = 300.0
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY PwCannon
            Weapon = SECONDARY PwMissile
            PreferredAgainst = SECONDARY VEHICLE STRUCTURE
            AutoChooseSources = PRIMARY FROM_PLAYER FROM_SCRIPT FROM_AI
            AutoChooseSources = SECONDARY FROM_PLAYER FROM_SCRIPT FROM_AI
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object PwBuilding
          KindOf = STRUCTURE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 1000000.0
            InitialHealth = 1000000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>The weapon slots the helicopter fired at the building over twenty seconds, as (cannon shots, missile shots).</summary>
    private static (int Cannon, int Missiles) PreferredWeaponShots(SimulationRate rate, RuleProfile rules)
    {
        var fixture = new SyntheticFixture(rate, files: PreferredWeaponFiles(), rules: rules);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "PwHelicopter", x: 100, y: 100);
        feeder.Spawn(2, "PwBuilding", x: 160, y: 100);
        for (var step = 0; step < world.Frames(3); step++) world.Step();
        var helicopter = world.Entities.First(value => value.Template.Name == "PwHelicopter");
        var building = world.Entities.First(value => value.Template.Name == "PwBuilding");
        // An order from the player: the weapon is chosen by the preference, not by the AI's own search.
        var order = feeder.FireWeapon(1, helicopter.Id.Value, building.Id.Value);
        Check.That(order.Accepted, $"{rate}: setup: the attack order is accepted ({order}).");
        int cannon = 0, missiles = 0;
        for (var step = 0; step < world.Frames(600); step++)
        {
            foreach (var value in world.Step().Events)
            {
                if (value.Kind != SimEventKind.WeaponFired || value.Entity != helicopter.Id) continue;
                if (value.Text.Contains("Missile", StringComparison.Ordinal)) missiles++; else cannon++;
            }
            // The attack order lasts a few frames; give it again while the target stands.
            if (step % world.Frames(2) == 0 && !world.IsShotPending(helicopter, building.Id)) feeder.FireWeapon(1, helicopter.Id.Value, building.Id.Value);
        }
        return (cannon, missiles);
    }

    public static void PreferredWeaponYieldsWhileItsClipReloads()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var original = PreferredWeaponShots(rate, RuleProfile.Original);
            Check.That(original.Missiles >= 2 && original.Cannon == 0, $"{rate}: under the original rules the missiles are preferred even while they reload: no cannon shot ({original.Cannon} cannon, {original.Missiles} missiles).");
            var fix = PreferredWeaponShots(rate, RuleProfile.Parse("preferred-weapon-yields-while-clip-empty"));
            Check.That(fix.Cannon > 10 && fix.Missiles >= 2, $"{rate}: with the fix the cannon fires while the clip reloads ({fix.Cannon} cannon, {fix.Missiles} missiles).");
        }
    }
}
