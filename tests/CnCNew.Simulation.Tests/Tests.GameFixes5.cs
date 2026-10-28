using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// The fixes of the community source adopted in Phase 41 (docs/GGC-FIXES.md section 3.1): each scene runs under the original profile and under a profile that
// names the fix, and shows the difference the fix makes.
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ContactBomberFiles() => new Dictionary<string, string>(AttackGateFiles())
    {
        ["Data/INI/Object/ContactBomberFixture.ini"] = """
        Weapon BomberCharge
          PrimaryDamage = 200
          AttackRange = 5.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End

        Object LosBomber
          KindOf = SELECTABLE INFANTRY ATTACK_NEEDS_LINE_OF_SIGHT
          VisionRange = 150.0
          Locomotor = SET_NORMAL LosLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY BomberCharge
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
        End
        """,
    };

    /// <summary>
    /// <see cref="GameFix.ContactWeaponNotViewBlocked"/> (<c>Pathfinder::isAttackViewBlockedByObstacle</c>): a bomber with a contact charge stands in the cells of its own building, an
    /// enemy building beside it. In the original those cells block its view of the enemy building - the acquisition passes it by and a pursuit looks for another place; with the fix
    /// an attacker whose current weapon is a contact weapon is never view-blocked. A soldier with a gun stands at the same place and stays blocked under the fix.
    /// </summary>
    public static void GameFixContactWeaponNotViewBlocked()
    {
        (bool Bomber, bool Soldier) Blocked(RuleProfile rules)
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: ContactBomberFiles(), rules: rules);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(30, 30, 1), "bomber");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "ViewWall", owner: 1, x: 50, y: 150);
            feeder.Spawn(2, "ViewWall", owner: 2, x: 90, y: 150);
            feeder.Spawn(1, "LosBomber", owner: 1, x: 64, y: 150);
            feeder.Spawn(1, "LosSoldier", owner: 1, x: 64, y: 152);
            world.Step();
            var target = world.Entities.First(value => value.Template.Name == "ViewWall" && value.Owner == new PlayerId(2));
            return (world.IsAttackViewBlocked(world.Entities.First(value => value.Template.Name == "LosBomber"), target),
                    world.IsAttackViewBlocked(world.Entities.First(value => value.Template.Name == "LosSoldier"), target));
        }

        var original = Blocked(RuleProfile.Original);
        var fixedRun = Blocked(RuleProfile.Parse("contact-weapon-not-view-blocked"));
        Check.That(original.Bomber && original.Soldier, "The original: the cells of their own building block both the bomber's and the soldier's view of the enemy building.");
        Check.That(!fixedRun.Bomber, "With the fix the bomber's contact charge is never view-blocked.");
        Check.That(fixedRun.Soldier, "The soldier's gun is not a contact weapon and stays blocked.");
    }
}
