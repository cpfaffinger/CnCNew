using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// The adopted fixes of the community source (docs/GGC-FIXES.md section 3): each is off in the original profile - the world behaves as before - and on in a profile
// that names it. Every test runs the same scene with both profiles and shows the difference the fix makes, and only that.
static partial class Tests
{
    public static void GameFixProfile()
    {
        const string print = "ABCDEF";
        Check.That(RuleProfile.Original.IsOriginal && RuleProfile.Original.Combine(print) == print, "The original profile leaves the definition fingerprint unchanged.");
        Check.That(RuleProfile.AllFixes.Combine(print) != print && RuleProfile.AllFixes.Combine(print) == RuleProfile.Parse("fixes").Combine(print), "A profile with fixes folds itself into the fingerprint, and the same profile gives the same fingerprint.");
        Check.That(RuleProfile.Parse("poison-kill-credit").Combine(print) != RuleProfile.AllFixes.Combine(print), "Two different profiles give different fingerprints.");
        Check.That(RuleProfile.Parse("flame-kill-credit,poison-kill-credit").Name == "poison-kill-credit+flame-kill-credit", "A profile's name lists its fixes in their numeric order, whatever order they were named in.");
        Check.That(RuleProfile.Parse(RuleProfile.AllFixes.Name).Equals(RuleProfile.AllFixes) && RuleProfile.Parse("original").Equals(RuleProfile.Original), "A profile's name parses back to the profile.");
        Check.Throws<ArgumentException>(() => RuleProfile.Parse("poison-kill-credit,no-such-fix"), "Unknown rule profile or fix 'no-such-fix'"); // never ignored
        Check.That(RuleProfile.Catalogue.Select(info => (int)info.Fix).SequenceEqual(Enumerable.Range(1, RuleProfile.Catalogue.Length)), "The catalogue lists every fix once, by its number.");
        Check.That(RuleProfile.Catalogue.All(info => info.Source.StartsWith("GeneralsMD/", StringComparison.Ordinal) && info.Source.Contains("::", StringComparison.Ordinal)), "Every fix names its file and function in the community source.");

        var original = new SyntheticFixture(TestRate.Default);
        var fixes = new SyntheticFixture(TestRate.Default, rules: RuleProfile.AllFixes);
        Check.That(original.Templates.Rules.IsOriginal && fixes.Templates.Rules.Equals(RuleProfile.AllFixes) && original.Fingerprint != fixes.Fingerprint,
            "A catalog carries its rules, and the fixture's fingerprint names them.");
        var world = NewWorld(fixes);
        var bytes = WorldSnapshot.Write(world);
        Check.Throws<SnapshotFormatException>(() => WorldSnapshot.Restore(bytes, original.Templates, original.Fingerprint), "different definitions"); // a world of one profile is not restored under another
    }

    private static IReadOnlyDictionary<string, string> GameFixFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/GameFixFixture.ini"] = """
        Object FixVictim
          KindOf = SELECTABLE INFANTRY
          ExperienceValue = 40 40 40 40
          Body = ActiveBody Body01
            MaxHealth = 30.0
            InitialHealth = 30.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = PoisonedBehavior Poison01
            PoisonDamageInterval = 100
            PoisonDuration = 3000
          End
          Behavior = FlammableUpdate Fire01
            FlameDamageLimit = 5
            AflameDuration = 3000
            AflameDamageDelay = 100
            AflameDamageAmount = 5
            FlameDamageExpiration = 1000
          End
        End

        Object FixKiller
          KindOf = SELECTABLE VEHICLE
          IsTrainable = Yes
          ExperienceRequired = 0 100 250 600
          ExperienceValue = 10 30 90 300
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>Hits the victim once in the killer's name with <paramref name="damageType"/> and lets the periodic damage finish it; the killer's experience afterwards.</summary>
    private static (long Experience, bool VictimDead, ulong Hash) KillOverTime(RuleProfile rules, int damageType, int deathType, bool restoreMidway = false)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: GameFixFiles(), rules: rules);
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "FixKiller", x: 10, y: 10);
        feeder.Spawn(2, "FixVictim", owner: 2, x: 40, y: 10);
        world.Step();
        var killer = world.FindEntity(new EntityId(1))!;
        var victim = new EntityId(2);
        world.Damage(victim, new DamageInfo(killer.Id, Fixed.FromInt(6), damageType, deathType, false));
        for (var step = 0; step < world.Frames(30) && world.FindEntity(victim) is { EffectivelyDead: false }; step++)
        {
            world.Step();
            if (restoreMidway && step == world.Frames(4))
            {
                world = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                killer = world.FindEntity(new EntityId(1))!;
            }
        }
        for (var step = 0; step < 4; step++) world.Step();
        return (killer.Experience, world.FindEntity(victim) is null or { EffectivelyDead: true }, world.StateHash);
    }

    /// <summary><see cref="GameFix.PoisonKillCredit"/> (<c>PoisonedBehavior::startPoisonedEffects</c>): a kill by poison counts for the poisoner only with the fix.</summary>
    public static void GameFixPoisonKillCredit()
    {
        var original = KillOverTime(RuleProfile.Original, DamageTypes.Poison, DeathTypes.Poisoned);
        var fixedRun = KillOverTime(RuleProfile.Parse("poison-kill-credit"), DamageTypes.Poison, DeathTypes.Poisoned);
        Check.That(original.VictimDead && fixedRun.VictimDead, "In both profiles the poison kills the victim.");
        Check.That(original.Experience == 0, $"The original: the periodic poison damage is dealt in nobody's name, so the poisoner gains nothing ({original.Experience}).");
        Check.That(fixedRun.Experience == 40, $"With the fix the poisoner gains the victim's experience value, 40 ({fixedRun.Experience}).");
        var restored = KillOverTime(RuleProfile.Parse("poison-kill-credit"), DamageTypes.Poison, DeathTypes.Poisoned, restoreMidway: true);
        Check.That(restored.Experience == 40 && restored.Hash == fixedRun.Hash, "The poisoner is part of the saved state: a world restored while the poison works ends in the same state.");
        Check.That(KillOverTime(RuleProfile.Parse("flame-kill-credit"), DamageTypes.Poison, DeathTypes.Poisoned).Experience == 0, "Another fix does not switch this one on.");
    }

    /// <summary><see cref="GameFix.FlameKillCredit"/> (<c>FlammableUpdate::onDamage</c>): a kill by burning counts for the source of the flame only with the fix.</summary>
    public static void GameFixFlameKillCredit()
    {
        var original = KillOverTime(RuleProfile.Original, DamageTypes.Flame, DeathTypes.Burned);
        var fixedRun = KillOverTime(RuleProfile.Parse("flame-kill-credit"), DamageTypes.Flame, DeathTypes.Burned);
        Check.That(original.VictimDead && fixedRun.VictimDead, "In both profiles the fire kills the victim.");
        Check.That(original.Experience == 0, $"The original: the burning damage is dealt in no attacker's name, so the source of the flame gains nothing ({original.Experience}).");
        Check.That(fixedRun.Experience == 40, $"With the fix the source of the flame gains the victim's experience value, 40 ({fixedRun.Experience}).");
        var restored = KillOverTime(RuleProfile.Parse("flame-kill-credit"), DamageTypes.Flame, DeathTypes.Burned, restoreMidway: true);
        Check.That(restored.Experience == 40 && restored.Hash == fixedRun.Hash, "The source of the flame is part of the saved state: a world restored while it burns ends in the same state.");
    }

    /// <summary><see cref="GameFix.NoSabotageOfUnfinishedOrSold"/> (<c>Sabotage*CrateCollide::isValidToExecute</c>): an unfinished or sold building is safe from a saboteur only with the fix.</summary>
    public static void GameFixNoSabotageOfUnfinishedOrSold()
    {
        (Fixed Thief, Fixed Victim, bool SpyUsed) Steal(RuleProfile rules, int statusBit)
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: CratesAndSpecialsFiles(), rules: rules);
            ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.GiveMoney(2, 500);
            world.Step();
            var supply = CsSpawn(world, feeder, "SabSupplyCenter", 2, 200, 300);
            CsRun(world, 5);
            world.SetStatus(supply.Id, 1UL << statusBit);
            var spy = CsSpawn(world, feeder, "FixtureSpy", 1, 100, 300);
            CsAi(spy).OrderApproachGoal(world, supply, false);
            CsRun(world, 120, () => !CsExists(world, spy));
            return (world.MoneyOf(new PlayerId(1)), world.MoneyOf(new PlayerId(2)), !CsExists(world, spy));
        }

        foreach (var (bit, what) in new[] { (ObjectStatusBits.UnderConstruction, "under construction"), (ObjectStatusBits.Sold, "being sold") })
        {
            var original = Steal(RuleProfile.Original, bit);
            var fixedRun = Steal(RuleProfile.Parse("no-sabotage-of-unfinished-or-sold"), bit);
            Check.That(original.SpyUsed && original.Thief == Fixed.FromInt(300) && original.Victim == Fixed.FromInt(200),
                $"The original: a saboteur enters a supply center {what} and steals 300 ({original.Thief}/{original.Victim}).");
            Check.That(!fixedRun.SpyUsed && fixedRun.Thief == Fixed.Zero && fixedRun.Victim == Fixed.FromInt(500),
                $"With the fix the saboteur cannot enter a supply center {what} ({fixedRun.Thief}/{fixedRun.Victim}).");
        }
        var finished = new SyntheticFixture(TestRate.Default, files: CratesAndSpecialsFiles(), rules: RuleProfile.AllFixes);
        Check.That(finished.Templates.Rules.Has(GameFix.NoSabotageOfUnfinishedOrSold), "The fix is part of every fix.");
    }
}
