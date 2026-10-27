using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;

/// <summary>
/// F-49 (docs/REFORGED-FIXES.md R-06): <c>WeaponTemplate::estimateWeaponTemplateDamage</c> gives a sniper's weapon 0 against a structure that has a contain module and nobody inside
/// ("if there is noone home to take the bullet, return 0"), so the weapon cannot be aimed at it; with an occupant the estimate is the armoured damage again. The comment of the
/// port's <c>EstimateDamage</c> named the rule, the code lacked it.
/// </summary>
static partial class Tests
{
    public static void SniperDoesNotTargetAnEmptyStructure()
    {
        var files = new Dictionary<string, string>(Phase5Files())
        {
            ["Data/INI/Object/SniperFixture.ini"] = """
            Weapon FixtureSniperRifle
              PrimaryDamage = 50
              AttackRange = 200.0
              DamageType = SNIPER
              DelayBetweenShots = 100
            End
            Object FixtureSniper
              KindOf = SELECTABLE INFANTRY
              Body = ActiveBody Body01
                MaxHealth = 50.0
                InitialHealth = 50.0
              End
              WeaponSet
                Conditions = None
                Weapon = PRIMARY FixtureSniperRifle
              End
              Behavior = AIUpdateInterface AI01
              End
              Behavior = DestroyDie Destroy01
              End
            End
            Object PlainHut
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 200.0
                InitialHealth = 200.0
              End
              Behavior = DestroyDie Destroy01
              End
            End
            """,
        };
        var fixture = new SyntheticFixture(TestRate.Default, files: files);
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "FixtureSniper", x: 0, y: 0);
        feeder.Spawn(2, "Bunker", x: 60, y: 0);
        feeder.Spawn(2, "PlainHut", x: 0, y: 60);
        feeder.Spawn(2, "Rebel", x: 60, y: 10);
        world.Step();
        var sniper = world.Entities.Single(entity => entity.Template.Name == "FixtureSniper");
        var bunker = world.Entities.Single(entity => entity.Template.Name == "Bunker");
        var hut = world.Entities.Single(entity => entity.Template.Name == "PlainHut");
        var rebel = world.Entities.Single(entity => entity.Template.Name == "Rebel");

        var empty = feeder.FireWeapon(1, sniper.Id.Value, bunker.Id.Value);
        Check.That(!empty.Accepted, $"A sniper cannot aim at an empty structure with a contain module ({empty.Reason}).");
        Check.That(feeder.FireWeapon(1, sniper.Id.Value, hut.Id.Value).Accepted, "A structure without a contain module is no such case.");
        world.Step();

        Check.That(feeder.EnterContainer(2, rebel.Id.Value, bunker.Id.Value).Accepted, "The rebel is ordered into the bunker.");
        for (var tick = 0; tick < world.Frames(60) && bunker.Modules.OfType<CnCNew.Simulation.Modules.IContainerModule>().Single().ContainCount == 0; tick++) world.Step();
        Check.That(bunker.Modules.OfType<CnCNew.Simulation.Modules.IContainerModule>().Single().ContainCount == 1, "The rebel is inside.");
        for (var tick = 0; tick < world.Frames(5); tick++) world.Step();
        Check.That(feeder.FireWeapon(1, sniper.Id.Value, bunker.Id.Value).Accepted, "With somebody home the sniper may aim at the bunker again.");
    }
}
