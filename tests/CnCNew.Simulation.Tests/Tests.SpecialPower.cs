using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> SpecialPowerFiles() => new Dictionary<string, string>(Phase5Files())
    {
        ["Data/INI/Object/SpecialPowerFixture.ini"] = """
        Science SCIENCE_Strike
          IsGrantable = Yes
          SciencePurchasePointCost = 1
        End
        Science SCIENCE_Strike2
          IsGrantable = Yes
        End
        Science SCIENCE_Bounty
          IsGrantable = Yes
          SciencePurchasePointCost = 1
        End
        Science SCIENCE_Locked
          IsGrantable = No
        End

        Upgrade Upgrade_Unpause
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_Scholar
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_ScholarLocked
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_Marker
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_Granted
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End

        SpecialPower SPECIAL_Airdrop
          Enum = SPECIAL_PARADROP_AMERICA
          ReloadTime = 10000
        End
        SpecialPower SPECIAL_Gated
          Enum = SPECIAL_CARPET_BOMB
          ReloadTime = 5000
          RequiredScience = SCIENCE_Strike
        End
        SpecialPower SPECIAL_Edge
          Enum = SPECIAL_NAPALM_STRIKE
          ReloadTime = 3000
        End
        SpecialPower SPECIAL_Paused
          Enum = SPECIAL_EMP_PULSE
          ReloadTime = 2000
        End
        SpecialPower SPECIAL_Hack
          Enum = SPECIAL_CASH_HACK
          ReloadTime = 1000
        End
        SpecialPower SPECIAL_Bounty
          Enum = SPECIAL_DEFECTOR
          ReloadTime = 1000
          RequiredScience = SCIENCE_Bounty
        End
        SpecialPower SPECIAL_Gunship
          Enum = SPECIAL_SPECTRE_GUNSHIP
          ReloadTime = 4000
        End
        SpecialPower SPECIAL_Shared
          Enum = SPECIAL_CLUSTER_MINES
          ReloadTime = 4000
          SharedSyncedTimer = Yes
        End
        SpecialPower SPECIAL_Defaulted
          Enum = SPECIAL_DAISY_CUTTER
        End

        ObjectCreationList OCL_Drop
          CreateObject
            ObjectNames = Drop
            Count = 2
            Disposition = LIKE_EXISTING
          End
        End
        ObjectCreationList OCL_DropUp
          CreateObject
            ObjectNames = DropUp
            Count = 1
          End
        End

        Object Drop
          KindOf = INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object DropUp
          KindOf = INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        Object Grunt
          KindOf = INFANTRY
          BuildCost = 100
          Body = ActiveBody Body01
            MaxHealth = 15.0
            InitialHealth = 15.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Center
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = OCLSpecialPower Drop01
            SpecialPowerTemplate = SPECIAL_Airdrop
            OCL = OCL_Drop
            UpgradeOCL = SCIENCE_Strike OCL_DropUp
            CreateLocation = CREATE_AT_LOCATION
          End
          Behavior = OCLSpecialPower Gated01
            SpecialPowerTemplate = SPECIAL_Gated
            OCL = OCL_Drop
            CreateLocation = CREATE_ABOVE_LOCATION
          End
          Behavior = OCLSpecialPower Edge01
            SpecialPowerTemplate = SPECIAL_Edge
            OCL = OCL_Drop
          End
          Behavior = OCLSpecialPower Paused01
            SpecialPowerTemplate = SPECIAL_Paused
            StartsPaused = Yes
            OCL = OCL_Drop
            CreateLocation = CREATE_AT_LOCATION
          End
          Behavior = UnpauseSpecialPowerUpgrade Unpause01
            SpecialPowerTemplate = SPECIAL_Paused
            TriggeredBy = Upgrade_Unpause
          End
        End

        Object Hacker
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = CashHackSpecialPower Hack01
            SpecialPowerTemplate = SPECIAL_Hack
            MoneyAmount = 50
            UpgradeMoneyAmount = SCIENCE_Strike 80
          End
        End

        Object Banker
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = CashBountyPower Bounty01
            SpecialPowerTemplate = SPECIAL_Bounty
            Bounty = 20%
          End
        End

        Object Granter
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = GrantUpgradeCreate GrantPlayer01
            UpgradeToGrant = Upgrade_Granted
          End
          Behavior = GrantUpgradeCreate GrantObject01
            UpgradeToGrant = Upgrade_Marker
            ExemptStatus = UNDER_CONSTRUCTION
          End
        End

        Object Scholar
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = GrantScienceUpgrade Grant01
            TriggeredBy = Upgrade_Scholar
            GrantScience = SCIENCE_Strike2
          End
          Behavior = GrantScienceUpgrade Grant02
            TriggeredBy = Upgrade_ScholarLocked
            GrantScience = SCIENCE_Locked
          End
        End

        Object Gunship
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = SpecialAbility Gun01
            SpecialPowerTemplate = SPECIAL_Gunship
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpectreGunshipDeploymentUpdate Spectre3
            SpecialPowerTemplate = SPECIAL_Gunship
            RequiredScience = SCIENCE_Strike2
            GunshipTemplateName = Drop
            CreateLocation = CREATE_AT_EDGE_NEAR_TARGET
          End
          Behavior = SpectreGunshipDeploymentUpdate Spectre1
            SpecialPowerTemplate = SPECIAL_Gunship
            RequiredScience = SCIENCE_Strike
            GunshipTemplateName = Drop
          End
        End

        Object SharedPlain
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = SpecialAbility Shared01
            SpecialPowerTemplate = SPECIAL_Shared
          End
        End
        Object SharedFresh
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = SpecialPowerCreate Create01
          End
          Behavior = SpecialAbility Shared01
            SpecialPowerTemplate = SPECIAL_Shared
          End
        End
        Object Defaulted
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = SpecialAbility Default01
            SpecialPowerTemplate = SPECIAL_Defaulted
          End
        End
        """,
    };

    private static uint IdOf(SimulationWorld world, string template, int nth = 0)
        => world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).ElementAt(nth).Id.Value;

    private static void StepTo(SimulationWorld world, long tick)
    {
        while (world.Tick < tick) world.Step();
    }

    private static SimulationWorld PowerWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(TestRate.Default, files: SpecialPowerFiles());
        var world = NewWorld(fixture);
        feeder = new Feeder(world);
        return world;
    }

    private static bool Rejected(SubmitResult result, CommandRejection reason) => !result.Accepted && result.Reason == reason;

    /// <summary>The command surface of <c>ActionManager::canDoSpecialPower</c> and <c>OCLSpecialPower</c> (block 0.6): every refusal, recharge, science-dependent OCL, create locations and the pause/unpause pair.</summary>
    public static void SpecialPowerUse()
    {
        var world = PowerWorld(out var fixture, out var feeder);
        Check.That(fixture.Templates.TryGet("Center", out var center) && center.IsInstantiable, "The special-power modules bind: the carrier is instantiable.");
        Check.That(world.SpecialPowers.TryGet("SPECIAL_Airdrop", out var airdrop) && airdrop.ReloadTicks == world.Frames(300) && world.SpecialPowers.TryGet("SPECIAL_Gated", out var gated) && gated.RequiredScience >= 0,
            "SpecialPower definitions carry reload ticks (10,000 ms = 300 ticks) and the required science.");
        Check.That(world.SpecialPowers.DefaultedReloadCount == 1, "Exactly the definition without ReloadTime takes the DefaultSpecialPower reload time.");

        feeder.Spawn(1, "Center", x: 5, y: 6);
        world.Step();
        var id = IdOf(world, "Center");
        var reload = airdrop.ReloadTicks;

        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Nope"), CommandRejection.UnknownSpecialPower), "An unknown power name is rejected.");
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Hack"), CommandRejection.UnknownSpecialPower), "A defined power the object does not carry is rejected.");
        Check.That(Rejected(feeder.UseSpecialPower(2, id, "SPECIAL_Airdrop"), CommandRejection.NotPermitted), "Only the owner may use a power.");
        Check.That(Rejected(feeder.UseSpecialPower(1, 999, "SPECIAL_Airdrop"), CommandRejection.UnknownEntity), "An unknown source is rejected.");
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Airdrop", SpecialPowerTargetKind.Object, 999), CommandRejection.InvalidSpecialPowerTarget), "An object target that does not exist is rejected.");
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Airdrop", (SpecialPowerTargetKind)9), CommandRejection.InvalidSpecialPowerTarget), "An undefined target kind is rejected.");
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Gated", SpecialPowerTargetKind.Location, x: 1, y: 1), CommandRejection.MissingScience), "A power whose science is not owned is rejected.");
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Airdrop", SpecialPowerTargetKind.Location, x: 1, y: 1), CommandRejection.SpecialPowerNotReady), "A power that is still recharging is rejected.");

        StepTo(world, reload - 1);
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Airdrop", SpecialPowerTargetKind.Location, x: 1, y: 1), CommandRejection.SpecialPowerNotReady), "The recharge started with the spawn in tick 0, so it is not ready at tick 299.");
        world.Step();
        Check.That(feeder.UseSpecialPower(1, id, "SPECIAL_Airdrop", SpecialPowerTargetKind.Location, x: 40, y: 50).Accepted, "A recharged power is accepted.");
        var result = world.Step();
        var used = result.Events.Single(value => value.Kind == SimEventKind.SpecialPowerUsed);
        Check.That(used.Entity.Value == id && used.Text == "SPECIAL_Airdrop" && used.Player.Value == 1, "The use is reported as an event.");
        var drops = world.Entities.Where(value => value.Template.Name == "Drop").ToArray();
        Check.That(drops.Length == 2 && drops.All(value => value.X == Fixed.FromInt(40) && value.Y == Fixed.FromInt(50) && value.Z == Fixed.Zero && value.Owner.Value == 1),
            "CREATE_AT_LOCATION creates the OCL at the target, owned by the user.");
        var usedAt = world.Tick - 1;
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Airdrop"), CommandRejection.SpecialPowerNotReady), "Using the power restarts its recharge.");
        StepTo(world, usedAt + reload - 1);
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Airdrop"), CommandRejection.SpecialPowerNotReady), "The second recharge lasts the full reload time.");
        world.Step();
        Check.That(feeder.UseSpecialPower(1, id, "SPECIAL_Airdrop").Accepted, "A use without a target is accepted.");
        world.Step();
        var atSource = world.Entities.Where(value => value.Template.Name == "Drop").OrderBy(value => value.Id.Value).Skip(2).ToArray();
        Check.That(atSource.Length == 2 && atSource.All(value => value.X == Fixed.FromInt(5) && value.Y == Fixed.FromInt(6)), "A use without a target creates the OCL at the source object.");

        // Science: the owned science switches the OCL and wakes the science-gated power (Player::addScience -> onSpecialPowerCreation, instantly ready).
        feeder.GiveSciencePoints(1, 1);
        world.Step();
        feeder.PurchaseScience(1, "SCIENCE_Strike");
        world.Step();
        Check.That(feeder.UseSpecialPower(1, id, "SPECIAL_Gated", SpecialPowerTargetKind.Location, x: 7, y: 8).Accepted, "Owning the required science makes the gated power ready at once.");
        world.Step();
        var lifted = world.Entities.Where(value => value.Template.Name == "Drop" && value.Z == Fixed.FromInt(300)).ToArray();
        Check.That(lifted.Length == 2 && lifted.All(value => value.X == Fixed.FromInt(7) && value.Y == Fixed.FromInt(8)), "CREATE_ABOVE_LOCATION lifts the origin by 300.");
        StepTo(world, world.Tick + reload);
        Check.That(feeder.UseSpecialPower(1, id, "SPECIAL_Airdrop", SpecialPowerTargetKind.Location, x: 9, y: 9).Accepted, "The airdrop is ready again.");
        world.Step();
        Check.That(CountOf(world, "DropUp") == 1, "The first UpgradeOCL whose science the owner has replaces the default OCL.");

        // Terrain edges do not exist: the power still fires and recharges, and the missing delivery is reported.
        StepTo(world, world.Tick + world.Frames(100));
        var before = world.Entities.Count;
        Check.That(feeder.UseSpecialPower(1, id, "SPECIAL_Edge", SpecialPowerTargetKind.Location, x: 1, y: 1).Accepted, "An edge power is accepted.");
        result = world.Step();
        var skipped = result.Events.Where(value => value.Kind == SimEventKind.CreationSkipped).ToArray();
        Check.That(skipped.Length == 1 && skipped[0].Text == "CREATE_AT_EDGE_NEAR_SOURCE" && skipped[0].Value == CreationSkipReasons.NoTerrainEdge && world.Entities.Count == before,
            "An edge create location reports NoTerrainEdge and creates nothing.");
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Edge"), CommandRejection.SpecialPowerNotReady), "The skipped edge power still recharged.");

        // StartsPaused + UnpauseSpecialPowerUpgrade.
        StepTo(world, world.Tick + world.Frames(400));
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Paused"), CommandRejection.SpecialPowerNotReady), "A paused power never becomes ready, however long it waits.");
        feeder.GiveMoney(1, 100);
        world.Step();
        feeder.PurchaseUpgrade(1, "Upgrade_Unpause", id);
        StepTo(world, world.Tick + 3);
        Check.That(Rejected(feeder.UseSpecialPower(1, id, "SPECIAL_Paused"), CommandRejection.SpecialPowerNotReady), "Unpausing adds the paused time back, so the countdown restarts from the pause point.");
        StepTo(world, world.Tick + world.Frames(61));
        Check.That(feeder.UseSpecialPower(1, id, "SPECIAL_Paused").Accepted, "After the remaining countdown the unpaused power is ready.");
        world.Step();
    }

    /// <summary>SharedSyncedTimer, SpecialPowerCreate, GrantUpgradeCreate, GrantScienceUpgrade and the gunship handler.</summary>
    public static void SpecialPowerWiring()
    {
        var world = PowerWorld(out _, out var feeder);
        world.Upgrades.TryGet("Upgrade_Granted", out var granted);
        world.Upgrades.TryGet("Upgrade_Marker", out var marker);

        // GrantUpgradeCreate is onBuildComplete: nothing in the spawn tick, done at the first update, one frame of the original after the spawn.
        feeder.Spawn(1, "Granter");
        world.Step();
        var granter = world.FindEntity(new EntityId(IdOf(world, "Granter")))!;
        Check.That(!world.PlayerUpgrades(new PlayerId(1)).Has(granted.Index) && !granter.CompletedUpgrades.Has(marker.Index), "GrantUpgradeCreate does nothing in the spawn tick.");
        for (var step = 0; step < world.Frames(1); step++) world.Step();
        Check.That(world.PlayerUpgrades(new PlayerId(1)).Has(granted.Index) && granter.CompletedUpgrades.Has(marker.Index), "The player-scoped upgrade goes to the owner, the object-scoped one to the object.");

        // GrantScienceUpgrade: only a grantable science is granted.
        feeder.Spawn(1, "Scholar");
        world.Step();
        var scholar = IdOf(world, "Scholar");
        feeder.GiveMoney(1, 100);
        world.Step();
        feeder.PurchaseUpgrade(1, "Upgrade_ScholarLocked", scholar);
        StepTo(world, world.Tick + 3);
        world.Sciences.TryGet("SCIENCE_Locked", out var locked);
        world.Sciences.TryGet("SCIENCE_Strike2", out var strike2);
        Check.That(!world.PlayerSciences(new PlayerId(1)).Has(locked.Index), "A science that is not IsGrantable is refused.");

        // The gunship handler serves the highest owned level; without any it refuses the use.
        feeder.Spawn(1, "Gunship");
        world.Step();
        var gunship = IdOf(world, "Gunship");
        StepTo(world, world.Tick + world.Frames(130));
        Check.That(Rejected(feeder.UseSpecialPower(1, gunship, "SPECIAL_Gunship", SpecialPowerTargetKind.Location, x: 3, y: 3), CommandRejection.SpecialPowerHandlerMissing),
            "A power whose effect is an update module with no matching handler is refused, not silently ignored.");
        feeder.PurchaseUpgrade(1, "Upgrade_Scholar", scholar);
        StepTo(world, world.Tick + 3);
        Check.That(world.PlayerSciences(new PlayerId(1)).Has(strike2.Index), "GrantScienceUpgrade grants the science.");
        Check.That(feeder.UseSpecialPower(1, gunship, "SPECIAL_Gunship", SpecialPowerTargetKind.Location, x: 3, y: 3).Accepted, "The owned science selects the handler.");
        var skipped = world.Step().Events.Single(value => value.Kind == SimEventKind.CreationSkipped);
        Check.That(skipped.Text == "CREATE_AT_EDGE_NEAR_TARGET" && skipped.Value == CreationSkipReasons.NoTerrainEdge, "The first matching handler (the highest level) is the one that runs.");
        Check.That(Rejected(feeder.UseSpecialPower(1, gunship, "SPECIAL_Gunship", SpecialPowerTargetKind.Location, x: 3, y: 3), CommandRejection.SpecialPowerNotReady), "The handler started the recharge.");

        // SharedSyncedTimer: one timer per player and power; SpecialPowerCreate makes a new carrier ready at once.
        feeder.Spawn(1, "SharedPlain");
        world.Step();
        var plain = IdOf(world, "SharedPlain");
        Check.That(Rejected(feeder.UseSpecialPower(1, plain, "SPECIAL_Shared"), CommandRejection.SpecialPowerNotReady), "A shared timer that did not exist starts counting on first use.");
        feeder.Spawn(1, "SharedFresh");
        world.Step();
        var fresh = IdOf(world, "SharedFresh");
        Check.That(Rejected(feeder.UseSpecialPower(1, fresh, "SPECIAL_Shared"), CommandRejection.SpecialPowerNotReady), "SpecialPowerCreate runs one frame of the original after the spawn, not in it.");
        for (var step = 0; step < world.Frames(1); step++) world.Step();
        Check.That(feeder.UseSpecialPower(1, fresh, "SPECIAL_Shared").Accepted, "onSpecialPowerCreation makes the shared power ready at once.");
        world.Step();
        Check.That(Rejected(feeder.UseSpecialPower(1, plain, "SPECIAL_Shared"), CommandRejection.SpecialPowerNotReady), "Using the power on one carrier restarts the timer of every carrier of that player.");
        world.SpecialPowers.TryGet("SPECIAL_Shared", out var shared);
        StepTo(world, world.Tick + shared.ReloadTicks);
        Check.That(feeder.UseSpecialPower(1, plain, "SPECIAL_Shared").Accepted, "After the reload the other carrier is ready.");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.SpecialPowerHandlerMissing] >= 1, "Rejections are counted per reason.");

        // No ReloadTime: DefaultSpecialPower's 1,000 ms.
        feeder.Spawn(1, "Defaulted");
        world.Step();
        var defaulted = IdOf(world, "Defaulted");
        world.SpecialPowers.TryGet("SPECIAL_Defaulted", out var fallback);
        Check.That(fallback.ReloadTicks == world.Frames(30), "An absent ReloadTime uses 1,000 ms (30 ticks).");
        StepTo(world, world.Tick + fallback.ReloadTicks);
        Check.That(feeder.UseSpecialPower(1, defaulted, "SPECIAL_Defaulted").Accepted, "A plain SpecialAbility with no handler is used like any power.");
    }

    /// <summary><c>CashHackSpecialPower</c> and <c>CashBountyPower</c> (with the kill payout of <c>Player::doBountyForKill</c>).</summary>
    public static void SpecialPowerCash()
    {
        var world = PowerWorld(out _, out var feeder);
        feeder.Spawn(1, "Hacker");
        feeder.Spawn(2, "Grunt");
        world.Step();
        var hacker = IdOf(world, "Hacker");
        var victim = IdOf(world, "Grunt");
        feeder.GiveMoney(2, 60);
        world.Step();
        StepTo(world, world.Tick + world.Frames(40));
        Check.That(Rejected(feeder.UseSpecialPower(1, hacker, "SPECIAL_Hack"), CommandRejection.InvalidSpecialPowerTarget) && Rejected(feeder.UseSpecialPower(1, hacker, "SPECIAL_Hack", SpecialPowerTargetKind.Location, x: 1, y: 1), CommandRejection.InvalidSpecialPowerTarget),
            "A cash hack needs an object target.");
        Check.That(feeder.UseSpecialPower(1, hacker, "SPECIAL_Hack", SpecialPowerTargetKind.Object, victim).Accepted, "The hack is accepted.");
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(50) && world.MoneyOf(new PlayerId(2)) == Fixed.FromInt(10), "The hack steals MoneyAmount from the victim's owner.");
        StepTo(world, world.Tick + world.Frames(40));
        feeder.UseSpecialPower(1, hacker, "SPECIAL_Hack", SpecialPowerTargetKind.Object, victim);
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(60) && world.MoneyOf(new PlayerId(2)) == Fixed.Zero, "A poorer victim loses only what it has.");

        feeder.GiveMoney(2, 200);
        feeder.GiveSciencePoints(1, 1);
        world.Step();
        feeder.PurchaseScience(1, "SCIENCE_Strike");
        StepTo(world, world.Tick + world.Frames(40));
        feeder.UseSpecialPower(1, hacker, "SPECIAL_Hack", SpecialPowerTargetKind.Object, victim);
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(140) && world.MoneyOf(new PlayerId(2)) == Fixed.FromInt(120), "An owned UpgradeMoneyAmount science raises the stolen amount.");

        // CashBountyPower: no ratio until the science arrives, then Bounty of the victim's cost per kill.
        feeder.Spawn(1, "Banker");
        feeder.Spawn(1, "Fighter");
        feeder.Spawn(2, "Grunt", x: 5);
        feeder.Spawn(2, "Grunt", x: 5);
        world.Step();
        var fighter = IdOf(world, "Fighter");
        var grunts = world.Entities.Where(value => value.Template.Name == "Grunt" && value.Owner.Value == 2 && value.X == Fixed.FromInt(5)).OrderBy(value => value.Id.Value).Select(value => value.Id.Value).ToArray();
        Check.That(world.CashBountyOf(new PlayerId(1)) == Fixed.Zero, "Without the science the bounty ratio stays zero.");
        var money = world.MoneyOf(new PlayerId(1));
        feeder.FireWeapon(1, fighter, grunts[0]);
        StepTo(world, world.Tick + world.Frames(30));
        Check.That(world.FindEntity(new EntityId(grunts[0])) is null && world.MoneyOf(new PlayerId(1)) == money, "The first kill pays nothing.");
        feeder.GiveSciencePoints(1, 1);
        world.Step();
        feeder.PurchaseScience(1, "SCIENCE_Bounty");
        world.Step();
        Check.That(world.CashBountyOf(new PlayerId(1)) > Fixed.Zero, "Granting the science raises the owner's bounty ratio (onSpecialPowerCreation).");
        feeder.FireWeapon(1, fighter, grunts[1]);
        StepTo(world, world.Tick + world.Frames(30));
        Check.That(world.FindEntity(new EntityId(grunts[1])) is null && world.MoneyOf(new PlayerId(1)) == money + Fixed.FromInt(20), "The next kill pays 20% of the victim's build cost.");
        Check.That(world.CashBountyOf(new PlayerId(2)) == Fixed.Zero, "The other player is unaffected.");
    }

    /// <summary>A snapshot taken mid-recharge, mid-pause and with a shared timer continues hash-identically, and the special-power command survives the replay file.</summary>
    public static void SpecialPowerSnapshot()
    {
        var live = PowerWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "Center", x: 5, y: 6);
        feeder.Spawn(1, "SharedFresh");
        feeder.Spawn(1, "Granter");
        for (var i = 0; i < 3; i++) live.Step();
        var center = IdOf(live, "Center");
        var fresh = IdOf(live, "SharedFresh");
        feeder.UseSpecialPower(1, fresh, "SPECIAL_Shared");
        StepTo(live, live.Frames(200));
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == live.StateHash, "The special-power state round-trips (recharge frames, pause counts, shared timers, wake ticks).");
        var fork = feeder.ForkTo(restored);
        for (var i = 0; i < live.Frames(400); i++)
        {
            if (live.Tick == live.Frames(320))
            {
                Check.That(feeder.UseSpecialPower(1, center, "SPECIAL_Airdrop", SpecialPowerTargetKind.Location, x: 11, y: 12).Accepted && fork.UseSpecialPower(1, center, "SPECIAL_Airdrop", SpecialPowerTargetKind.Location, x: 11, y: 12).Accepted,
                    "The power is ready in both the live and the restored world.");
            }
            Check.That(live.Step().StateHash == restored.Step().StateHash, "Live and restored worlds are hash-identical at step " + i + ".");
        }
        Check.That(CountOf(restored, "Drop") == 2, "The use after the restore took effect.");

        var command = new UseSpecialPowerCommand(new PlayerId(1), 12, 12, new EntityId(4), "SPECIAL_Airdrop", SpecialPowerTargetKind.Location, new EntityId(0), Fixed.FromInt(3), Fixed.FromInt(-4));
        var objectCommand = new UseSpecialPowerCommand(new PlayerId(2), 13, 13, new EntityId(5), "SPECIAL_Hack", SpecialPowerTargetKind.Object, new EntityId(6), Fixed.Zero, Fixed.Zero);
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, TestRate.Default.TicksPerSecond, fixture.Fingerprint, SyntheticFixture.FixtureId, 1, new WorldId(1), TwoPlayers, 10, 5);
        ImmutableArray<SimCommand> commands = [command, objectCommand];
        var read = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, commands, [])));
        Check.That(read.Commands.SequenceEqual(commands), "UseSpecialPowerCommand survives the replay file.");
    }

    /// <summary>Real data: every science/OCL/power name the special-power modules use resolves, and the catalog counts its defaults.</summary>
    public static void SpecialPowerRealData(TemplateCatalog catalog)
    {
        var powers = catalog.SpecialPowers.Powers;
        Check.That(powers.Length > 60, $"The retail data defines many special powers ({powers.Length}).");
        Check.That(powers.Select(value => value.Index).SequenceEqual(Enumerable.Range(0, powers.Length)), "Power indices are dense and stable.");
        Console.WriteLine($"       {powers.Length} special powers, {catalog.SpecialPowers.DefaultedReloadCount} on the DefaultSpecialPower reload time, {powers.Count(value => value.SharedSynced)} shared");
    }
}
