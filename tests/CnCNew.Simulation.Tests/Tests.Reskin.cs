using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// #431: ThingTemplate::isEquivalentTo counts a template made with ObjectReskin as the object it reskins (m_reskinnedFrom, set by ThingFactory::parseObjectDefinition), either way,
// and two reskins of one object as each other; a reskin of a reskin is not its grandparent's equal. The places that compare with it: the weapon flag NOT_SIMILAR
// (Weapon::dealDamageInternal), the MaxSimultaneousOfType count (Player::canBuildMoreOfType, countExisting).
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ReskinFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/ReskinFixture.ini"] = """
        Weapon ReskinBlast
          PrimaryDamage = 10
          PrimaryDamageRadius = 40.0
          AttackRange = 10.0
          DamageType = EXPLOSION
          RadiusDamageAffects = ALLIES ENEMIES NEUTRALS NOT_SIMILAR
        End

        Object ReskinBarracks
          KindOf = SELECTABLE STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 2
          End
        End

        Object Bomber
          KindOf = SELECTABLE INFANTRY
          BuildCost = 30
          BuildTime = 1
          MaxSimultaneousOfType = 1
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        ObjectReskin BomberSkin Bomber
          Geometry = CYLINDER
        End

        ObjectReskin BomberOtherSkin Bomber
        End

        ObjectReskin BomberSkinOfSkin BomberSkin
        End

        Object Bystander
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder) ReskinWorld()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: ReskinFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world));
    }

    private static Entity ReskinSpawn(SimulationWorld world, Feeder feeder, string template, int owner, int x)
    {
        feeder.Spawn(owner, template, owner: owner, x: x, y: 100);
        world.Step();
        return world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).Last();
    }

    public static void ReskinIsEquivalentToWhatItReskins()
    {
        var (world, feeder) = ReskinWorld();
        var templates = world.Templates;
        bool Equivalent(string first, string second)
        {
            Check.That(templates.TryGet(first, out var one) && templates.TryGet(second, out var other), $"Setup: {first} and {second} are templates.");
            templates.TryGet(first, out var a);
            templates.TryGet(second, out var b);
            return a.IsEquivalentTo(b);
        }
        Check.That(templates.TryGet("BomberSkin", out var skin) && skin.ReskinnedFrom == "Bomber", $"The template carries its parent ({skin?.ReskinnedFrom}).");
        Check.That(templates.TryGet("Bomber", out var bomber) && bomber.ReskinnedFrom is null, "An object of its own reskins nothing.");
        Check.That(Equivalent("BomberSkin", "Bomber") && Equivalent("Bomber", "BomberSkin"), "A reskin and the object it reskins are equivalent, either way.");
        Check.That(Equivalent("BomberSkin", "BomberOtherSkin") && Equivalent("BomberOtherSkin", "BomberSkin"), "Two reskins of one object are equivalent.");
        Check.That(Equivalent("BomberSkinOfSkin", "BomberSkin") && Equivalent("BomberSkin", "BomberSkinOfSkin"), "A reskin of a reskin is equivalent to its parent.");
        Check.That(!Equivalent("BomberSkinOfSkin", "Bomber") && !Equivalent("Bomber", "BomberSkinOfSkin") && !Equivalent("BomberSkinOfSkin", "BomberOtherSkin"),
            "One step only: a reskin of a reskin is neither its grandparent's equal nor its parent's sibling's.");
        Check.That(!Equivalent("BomberSkin", "Bystander"), "An unrelated object is not equivalent.");
        Check.That(templates.IsOfType(skin!, "Bomber") && templates.IsOfType(bomber!, "BomberOtherSkin") && !templates.IsOfType(skin!, "Bystander"), "A script's type test follows the reskin.");

        // MaxSimultaneousOfType = 1 (the reskin copies it): a living reskin counts against its parent and the parent against a reskin.
        feeder.GiveMoney(1, 1000);
        var barracks = ReskinSpawn(world, feeder, "ReskinBarracks", 1, 300);
        var living = ReskinSpawn(world, feeder, "BomberSkin", 1, 340);
        Check.That(feeder.QueueProduction(1, barracks.Id.Value, "Bomber").Reason == CommandRejection.ProductionLimitReached, "A living reskin is one of the parent's MaxSimultaneousOfType.");
        Check.That(feeder.QueueProduction(1, barracks.Id.Value, "BomberOtherSkin").Reason == CommandRejection.ProductionLimitReached, "And one of its sibling's.");
        Check.That(feeder.QueueProduction(1, barracks.Id.Value, "BomberSkinOfSkin").Reason == CommandRejection.ProductionLimitReached, "And one of its own reskin's.");
        feeder.Kill(living.Id.Value);
        world.Step();
        Check.That(feeder.QueueProduction(1, barracks.Id.Value, "Bomber").Accepted, "With the reskin dead the parent may be built.");

        // NOT_SIMILAR: the blast spares allies equivalent to its source - the reskin and the parent - and hurts an ally of another kind.
        {
            var (blastWorld, blastFeeder) = ReskinWorld();
            var source = ReskinSpawn(blastWorld, blastFeeder, "BomberSkin", 1, 100);
            var parent = ReskinSpawn(blastWorld, blastFeeder, "Bomber", 1, 110);
            var sibling = ReskinSpawn(blastWorld, blastFeeder, "BomberOtherSkin", 1, 90);
            var grandchild = ReskinSpawn(blastWorld, blastFeeder, "BomberSkinOfSkin", 1, 105);
            var stranger = ReskinSpawn(blastWorld, blastFeeder, "Bystander", 1, 95);
            var enemy = ReskinSpawn(blastWorld, blastFeeder, "Bystander", 2, 120);
            Check.That(blastWorld.Weapons.TryGet("ReskinBlast", out var blast), "Setup: the weapon is in the catalog.");
            blastWorld.DealWeaponDamage(source, blast, enemy, enemy.X, enemy.Y, enemy.Z);
            var full = CnCNew.Simulation.Numerics.Fixed.FromInt(100);
            Check.That(parent.Body!.Health == full && sibling.Body!.Health == full && grandchild.Body!.Health == full,
                $"Allies equivalent to the source are spared (parent {parent.Body!.Health}, sibling {sibling.Body!.Health}, its reskin {grandchild.Body!.Health}).");
            Check.That(stranger.Body!.Health < full && enemy.Body!.Health < full, $"An ally of another kind and the enemy are hurt ({stranger.Body!.Health}, {enemy.Body!.Health}).");
        }
    }
}
