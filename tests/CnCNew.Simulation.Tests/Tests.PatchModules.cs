using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// Phase 32: two engine modules the shipped data never uses and the community patch does (docs/PATCH-LAYER.md).
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> PatchModuleFiles()
    {
        var files = new Dictionary<string, string>(CratesAndSpecialsFiles())
        {
            ["Data/INI/Object/PatchModulesFixture.ini"] = """
            Upgrade Upgrade_StatusBits
              Type = OBJECT
              BuildCost = 10
              BuildTime = 0.1
            End

            Object StatusBitsThing
              KindOf = SELECTABLE VEHICLE
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = StatusBitsUpgrade StatusBits01
                TriggeredBy = Upgrade_StatusBits
                StatusToSet = NO_ATTACK_FROM_AI UNSELECTABLE
                StatusToClear = UNSELECTABLE
              End
            End

            Object PatchDropzone
              KindOf = STRUCTURE FS_SUPPLY_DROPZONE
              Geometry = CYLINDER
              GeometryMajorRadius = 15.0
              GeometryMinorRadius = 15.0
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = OCLUpdate Ocl01
                OCL = None
                MinDelay = 60000
                MaxDelay = 60000
              End
            End

            Object DropzoneSpy
              KindOf = SELECTABLE INFANTRY
              Geometry = CYLINDER
              GeometryMajorRadius = 2.0
              GeometryMinorRadius = 2.0
              Locomotor = SET_NORMAL CrateLoco
              Body = ActiveBody Body01
                MaxHealth = 50.0
                InitialHealth = 50.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = AIUpdateInterface AI01
              End
              Behavior = SabotageSupplyDropzoneCrateCollide SabDrop01
                BuildingPickup = Yes
                StealCashAmount = 400
              End
            End
            """,
        };
        return files;
    }

    private static SimulationWorld PatchModuleWorld(SyntheticFixture fixture)
    {
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
        return new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
    }

    /// <summary><c>StatusBitsUpgrade::upgradeImplementation</c>: the upgrade sets <c>StatusToSet</c>, then clears <c>StatusToClear</c>; nothing happens before it, and a saved world keeps the bits.</summary>
    public static void PatchModuleStatusBitsUpgrade()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: PatchModuleFiles());
        var world = PatchModuleWorld(fixture);
        var feeder = new Feeder(world);
        var thing = CsSpawn(world, feeder, "StatusBitsThing", 1, 50, 50);
        var noAttack = ObjectStatusBits.Index("NO_ATTACK_FROM_AI");
        var unselectable = ObjectStatusBits.Index("UNSELECTABLE");
        Check.That(!thing.HasStatus(noAttack) && !thing.HasStatus(unselectable), "Before the upgrade no bit is set.");
        feeder.GiveMoney(1, 1000);
        world.Step();
        feeder.GrantUpgrade(1, "Upgrade_StatusBits", target: thing.Id.Value);
        CsRun(world, 30, () => thing.HasStatus(noAttack));
        Check.That(thing.HasStatus(noAttack), "The upgrade sets NO_ATTACK_FROM_AI.");
        Check.That(!thing.HasStatus(unselectable), "A bit in both masks is set first and cleared after, as the reference does: UNSELECTABLE ends cleared.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.FindEntity(thing.Id)!.HasStatus(noAttack) && restored.StateHash == world.StateHash, "The bits are part of the saved state.");
    }

    /// <summary>
    /// <c>SabotageSupplyDropzoneCrateCollide::executeCrateBehavior</c>: a saboteur at an enemy drop zone restarts its <c>OCLUpdate</c> timer and steals up to <c>StealCashAmount</c>
    /// (clamped to what the owner has); a friendly drop zone is not a target.
    /// </summary>
    public static void PatchModuleSabotageSupplyDropzone()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: PatchModuleFiles());
        var world = PatchModuleWorld(fixture);
        var feeder = new Feeder(world);
        feeder.GiveMoney(2, 300);
        world.Step();
        var zone = CsSpawn(world, feeder, "PatchDropzone", 2, 200, 200);
        CsRun(world, 30);
        var timer = zone.Modules.OfType<OCLUpdate>().Single();
        var before = timer.NextCreationTick;
        Check.That(before > world.Tick, $"The drop zone's timer runs ({before} after {world.Tick}).");
        CsRun(world, 300);
        var spy = CsSpawn(world, feeder, "DropzoneSpy", 1, 100, 200);
        CsAi(spy).OrderApproachGoal(world, zone, false);
        long hit = -1;
        CsRun(world, 200, () => { if (!CsExists(world, spy)) hit = world.Tick; return hit >= 0; });
        Check.That(hit >= 0, "The saboteur reaches the drop zone and is used up.");
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(300) && world.MoneyOf(new PlayerId(2)) == Fixed.Zero, $"The theft is clamped to what the owner has ({world.MoneyOf(new PlayerId(1))}/{world.MoneyOf(new PlayerId(2))}).");
        Check.That(timer.NextCreationTick > before && timer.NextCreationTick >= hit, $"The timer starts again from the sabotage ({before} -> {timer.NextCreationTick}, hit at {hit}).");

        var friend = CsSpawn(world, feeder, "DropzoneSpy", 2, 150, 200);
        CsAi(friend).OrderApproachGoal(world, zone, false);
        CsRun(world, 200);
        Check.That(CsExists(world, friend), "A drop zone of the own side is no target.");
    }
}
