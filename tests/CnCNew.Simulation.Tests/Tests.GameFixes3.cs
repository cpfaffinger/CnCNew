using CnCNew.Data.Definitions;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// The fixes of the community source adopted in Phase 33 (docs/GGC-FIXES.md section 3.1c): each scene runs under the original profile and under a profile that names the fix.
static partial class Tests
{
    /// <summary><see cref="GameFix.TunnelHealOncePerPlayer"/> (<c>Player::update</c>): with two tunnels a rider heals twice as fast in the original, once as fast with the fix.</summary>
    public static void GameFixTunnelHealOncePerPlayer()
    {
        Fixed Healed(RuleProfile rules, int tunnels)
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: TransportFiles(), rules: rules);
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            for (var index = 0; index < tunnels; index++) feeder.Spawn(1, "Tunnel", x: index * 200, y: 0);
            feeder.Spawn(1, "Grunt", x: 30, y: 0);
            world.Step();
            var grunt = Only(world, "Grunt");
            var tunnel = world.Entities.Where(value => value.Template.Name == "Tunnel").OrderBy(value => value.Id.Value).First();
            feeder.Damage(grunt.Id.Value, 50m);
            world.Step();
            Check.That(feeder.EnterContainer(1, grunt.Id.Value, tunnel.Id.Value).Accepted, "The grunt enters the tunnel.");
            var before = grunt.Body!.Health;
            // Five frames of the original: a sixth of the full heal (the fixture's TimeForFullHeal is 1 s), a third with two tunnels.
            for (var tick = 0; tick < world.Frames(5); tick++) world.Step();
            return grunt.Body.Health - before;
        }
        var fix = RuleProfile.Parse("tunnel-heal-once-per-player");
        var single = Healed(RuleProfile.Original, 1);
        Check.That(single > Fixed.Zero && Healed(fix, 1) == single, $"With one tunnel both profiles heal alike ({single}).");
        Check.That(Healed(RuleProfile.Original, 2) == single * Fixed.FromInt(2), $"The original: two tunnels heal twice as fast ({Healed(RuleProfile.Original, 2)} against {single}).");
        Check.That(Healed(fix, 2) == single, $"With the fix two tunnels heal as fast as one ({Healed(fix, 2)}).");
    }

    private static (SimulationWorld World, Feeder Feeder) BattlePlanWorld(RuleProfile rules)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: BattlePlanFiles(), rules: rules);
        var terrain = NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        return (world, new Feeder(world));
    }

    private static void ActivatePlan(SimulationWorld world, Feeder feeder, Entity center, int option)
    {
        var plan = center.Modules.OfType<BattlePlanUpdate>().Single();
        Check.That(feeder.UseSpecialPower(center.Owner.Value, center.Id.Value, "SPECIAL_BattlePlanFixture", SpecialPowerTargetKind.None, option: option).Accepted, $"Battle plan option {option} is accepted.");
        world.Step();
        CsRun(world, 200, () => plan.Status == BattlePlanUpdate.TransitionStatus.Active);
        Check.That(plan.Status == BattlePlanUpdate.TransitionStatus.Active, $"Plan {option} became active.");
    }

    /// <summary>
    /// <see cref="GameFix.BattlePlanAggregate"/> (<c>Player::applyBattlePlanBonusesForPlayerObjects</c>): one player runs bombardment at one center and hold the line at another. In the
    /// original the second plan's bonus clears the first one's switch on every soldier; with the fix the soldier carries both.
    /// </summary>
    public static void GameFixBattlePlanAggregate()
    {
        uint Bits(RuleProfile rules)
        {
            var (world, feeder) = BattlePlanWorld(rules);
            var first = CsSpawn(world, feeder, "PlanCenter", 1, 100, 100);
            var second = CsSpawn(world, feeder, "PlanCenter", 1, 300, 100);
            var soldier = CsSpawn(world, feeder, "PlanSoldier", 1, 20, 100);
            CsRun(world, 10);
            ActivatePlan(world, feeder, first, 1);
            ActivatePlan(world, feeder, second, 2);
            return soldier.WeaponBonusConditions;
        }
        var bombard = BattlePlanBit("BATTLEPLAN_BOMBARDMENT");
        var hold = BattlePlanBit("BATTLEPLAN_HOLDTHELINE");
        var original = Bits(RuleProfile.Original);
        var fixedBits = Bits(RuleProfile.Parse("battle-plan-aggregate"));
        Check.That((original & hold) != 0 && (original & bombard) == 0, "The original: hold the line clears the bombardment switch of the other center.");
        Check.That((fixedBits & hold) != 0 && (fixedBits & bombard) != 0, "With the fix the soldier carries both plans' switches.");
    }

    /// <summary>
    /// <see cref="GameFix.BattlePlanFollowsCapture"/> (<c>BattlePlanUpdate::onCapture</c>): a center running hold the line changes owner. In the original the old owner keeps the plan
    /// and the new one does not get it; with the fix it moves.
    /// </summary>
    public static void GameFixBattlePlanFollowsCapture()
    {
        (int OldPlans, int NewPlans, bool OldBit, bool NewBit) Capture(RuleProfile rules)
        {
            var (world, feeder) = BattlePlanWorld(rules);
            var center = CsSpawn(world, feeder, "PlanCenter", 1, 100, 100);
            var mine = CsSpawn(world, feeder, "PlanSoldier", 1, 20, 100);
            var theirs = CsSpawn(world, feeder, "PlanSoldier", 2, 400, 100);
            CsRun(world, 10);
            ActivatePlan(world, feeder, center, 2);
            Check.That(world.TransferOwnership(center.Id, new PlayerId(2)), "The center changes owner.");
            world.Step();
            var hold = BattlePlanBit("BATTLEPLAN_HOLDTHELINE");
            return (world.BattlePlansActive(new PlayerId(1), BattlePlanKind.HoldTheLine), world.BattlePlansActive(new PlayerId(2), BattlePlanKind.HoldTheLine),
                (mine.WeaponBonusConditions & hold) != 0, (theirs.WeaponBonusConditions & hold) != 0);
        }
        var original = Capture(RuleProfile.Original);
        var fixedPlan = Capture(RuleProfile.Parse("battle-plan-follows-capture"));
        Check.That(original == (1, 0, true, false), $"The original: the old owner keeps the plan ({original}).");
        Check.That(fixedPlan == (0, 1, false, true), $"With the fix the plan moves to the new owner ({fixedPlan}).");
    }

    /// <summary>
    /// <see cref="GameFix.HealedHelicopterToRally"/> (<c>JetAIUpdate::update</c>): a helicopter that healed on its pad takes off again; with a rally point on its airfield it flies
    /// there with the fix and stays over the pad in the original.
    /// </summary>
    public static void GameFixHealedHelicopterToRally()
    {
        Fixed DistanceToRally(RuleProfile rules)
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: AirFixtureFiles, rules: rules);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 9, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, string.Empty), fixture.Templates, null);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
            world.Step();
            feeder.GiveMoney(1, 1000);
            world.Step();
            var airfield = Get(world, "AirTestAirfield");
            Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestHeli").Accepted, "The helicopter is queued.");
            StepUntil(world, () => world.Entities.Any(value => value.Template.Name == "AirTestHeli"), 200, "The helicopter was produced.");
            var craft = Get(world, "AirTestHeli");
            var ai = JetOf(craft);
            StepUntil(world, () => ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None, 300, "The helicopter lifted off.");
            StepUntil(world, () => ai.Phase == JetAIUpdate.JetPhase.ReturningForLanding, 400, "Idle, it returns by itself.");
            StepUntil(world, () => !ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None, 1500, "It settled on the pad.");
            Check.That(feeder.SetRallyPoint(1, airfield.Id.Value, 800, 300).Accepted, "The airfield takes a rally point.");
            feeder.Damage(craft.Id.Value, 10m);
            StepUntil(world, () => ai.AllowAirLoco, 3000, "Healed on the pad, it takes off again.");
            // The nearest it comes to the rally point in 20 s (idle in the air, it flies back to its pad after a while).
            var nearest = Fixed.MaxValue;
            for (var tick = 0; tick < world.Frames(600); tick++)
            {
                world.Step();
                var dx = craft.X - Fixed.FromInt(800);
                var dy = craft.Y - Fixed.FromInt(300);
                nearest = Fixed.Min(nearest, Fixed.Sqrt(dx * dx + dy * dy));
            }
            return nearest;
        }
        var original = DistanceToRally(RuleProfile.Original);
        var fixedFlight = DistanceToRally(RuleProfile.Parse("healed-helicopter-to-rally"));
        Check.That(original > Fixed.FromInt(200), $"The original: the healed helicopter never comes near the rally point ({original} at the nearest).");
        Check.That(fixedFlight < Fixed.FromInt(50), $"With the fix it flies to the rally point ({fixedFlight} at the nearest).");
    }
}

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> PropagandaCarrierFiles()
    {
        var files = new Dictionary<string, string>(SlaveFiles())
        {
            ["Data/INI/Object/PropagandaCarrierFixture.ini"] = """
            Object SvMobileTower
              KindOf = SELECTABLE VEHICLE
              TransportSlotCount = 1
              Body = ActiveBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = PropagandaTowerBehavior Prop01
                Radius = 100.0
                DelayBetweenUpdates = 300
                HealPercentEachSecond = 30%
                UpgradedHealPercentEachSecond = 60%
                UpgradeRequired = Upgrade_SvSubliminal
                AffectsSelf = No
              End
              Behavior = DestroyDie Destroy01
              End
            End
            Object SvCarrier
              KindOf = SELECTABLE VEHICLE TRANSPORT
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = TransportContain Transport01
                Slots = 4
                AllowInsideKindOf = VEHICLE
              End
              Behavior = DestroyDie Destroy01
              End
            End
            """,
        };
        return files;
    }

    /// <summary>
    /// <see cref="GameFix.PropagandaTowerEnclosingContainer"/> (<c>PropagandaTowerBehavior::update</c>): a mobile tower that has covered an ally boards a transport (an enclosing container
    /// that is not itself contained). In the original it keeps working; with the fix it falls silent and the ally loses its bonus.
    /// </summary>
    public static void GameFixPropagandaTowerEnclosingContainer()
    {
        bool StillCovered(RuleProfile rules)
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: PropagandaCarrierFiles(), rules: rules);
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "SvMobileTower", x: 100, y: 100);
            feeder.Spawn(1, "SvPropSoldier", x: 130, y: 100);
            feeder.Spawn(1, "SvCarrier", x: 110, y: 100);
            StepN(world, 2);
            var tower = SlaveEntity(world, "SvMobileTower");
            var soldier = SlaveEntity(world, "SvPropSoldier");
            var carrier = SlaveEntity(world, "SvCarrier");
            var enthusiastic = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("ENTHUSIASTIC", out var bit) ? bit : throw new InvalidOperationException();
            StepN(world, 40);
            Check.That((soldier.WeaponBonusConditions & (1u << enthusiastic)) != 0, "Before boarding the tower covers the soldier.");
            Check.That(feeder.EnterContainer(1, tower.Id.Value, carrier.Id.Value).Accepted, "The tower boards the carrier.");
            StepN(world, 40);
            Check.That(tower.ContainedBy == carrier.Id, "Setup: the tower is inside the carrier.");
            return (soldier.WeaponBonusConditions & (1u << enthusiastic)) != 0;
        }
        Check.That(StillCovered(RuleProfile.Original), "The original: a tower inside an uncontained transport keeps working.");
        Check.That(!StillCovered(RuleProfile.Parse("propaganda-tower-enclosing-container")), "With the fix it falls silent inside any enclosing container.");
    }
}
