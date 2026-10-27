using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// The central disable rule (<c>GameLogic::update</c>, Phase 40, docs/SIMULATION-CORE.md §54): a disabled object runs only the update modules whose
/// <c>getDisabledTypesToProcess</c> shares a type with its disabled mask. A producer processes <c>DISABLED_HELD</c> by default (<c>ProductionUpdateModuleData</c>) and the
/// types its data name: an EMP stops its queue, holding it does not, a script disable does.
/// </summary>
static partial class Tests
{
    public static void DisableRuleStopsTheModulesThatDoNotProcessIt()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(RetailModuleFiles())
        {
            ["Data/INI/Object/DisableRule.ini"] = """
            Object PoweredFactory
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = ProductionUpdate Prod01
                MaxQueueEntries = 3
                DisabledTypesToProcess = DISABLED_HELD DISABLED_EMP
              End
              Behavior = DefaultProductionExitUpdate Exit01
                UnitCreatePoint = X:10.0 Y:-4.0 Z:0.0
                NaturalRallyPoint = X:20.0 Y:0.0 Z:0.0
              End
            End
            """,
        });
        // QuickUnit takes one second; each case runs three seconds.
        int Produced(string factory, System.Action<SimulationWorld, Entity> disable)
        {
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            feeder.Spawn(1, factory, x: 300, y: 300);
            world.Step();
            feeder.GiveMoney(1, 1000);
            world.Step();
            var producer = world.Entities.Single(entity => entity.Template.Name == factory);
            disable(world, producer);
            feeder.QueueProduction(1, producer.Id.Value, "QuickUnit");
            for (var tick = 0; tick < world.Frames(90); tick++) world.Step();
            return CountOf(world, "QuickUnit");
        }

        Check.That(Produced("DefaultExitFactory", (_, _) => { }) == 1, "Setup: a factory makes its unit in three seconds.");
        Check.That(Produced("DefaultExitFactory", (world, factory) => world.DisableByEmpUntil(factory, world.Tick + world.Frames(300))) == 0,
            "An EMP stops the queue of a factory: its ProductionUpdate processes only DISABLED_HELD.");
        Check.That(Produced("DefaultExitFactory", (world, factory) => world.SetScriptDisable(factory, Entity.ScriptHeld, true)) == 1,
            "A held factory goes on producing (ProductionUpdate's default DisabledTypesToProcess is DISABLED_HELD).");
        Check.That(Produced("DefaultExitFactory", (world, factory) => world.SetScriptDisable(factory, Entity.ScriptDisabled, true)) == 0,
            "A script-disabled factory makes nothing.");
        Check.That(Produced("PoweredFactory", (world, factory) => world.DisableByEmpUntil(factory, world.Tick + world.Frames(300))) == 1,
            "A factory whose data name DISABLED_EMP produces through an EMP.");
        Check.That(Produced("DefaultExitFactory", (world, factory) => world.DisableByEmpUntil(factory, world.Tick + world.Frames(15))) == 1,
            "After a short EMP the queue goes on.");
    }

    /// <summary>
    /// <c>Object::checkDisabledStatus</c> runs after the updates of a frame and clears a disable whose frame has come: a factory disabled before its update in frame f "until"
    /// f + 10 skips the updates of frames f to f + 10 - eleven frames - and its unit comes eleven frames late. The port let it go one frame early.
    /// </summary>
    public static void DisableHoldsThroughItsLastFrame()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: RetailModuleFiles());
        var frame = 0L;
        long Completed(int disableFrames)
        {
            var world = NewWorld(fixture);
            frame = world.Frames(1);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "DefaultExitFactory", x: 300, y: 300);
            world.Step();
            feeder.GiveMoney(1, 1000);
            world.Step();
            var factory = world.Entities.Single(entity => entity.Template.Name == "DefaultExitFactory");
            while (world.Tick % world.Frames(1) != 0) world.Step();
            feeder.QueueProduction(1, factory.Id.Value, "QuickUnit");
            world.Step();
            while (world.Tick % world.Frames(1) != 0) world.Step();
            var start = world.Tick;
            if (disableFrames > 0) world.DisableByEmpUntil(factory, world.Tick + world.Frames(disableFrames));
            while (CountOf(world, "QuickUnit") == 0 && world.Tick < start + world.Frames(200)) world.Step();
            return world.Tick - start;
        }

        var free = Completed(0);
        var disabled = Completed(10);
        Check.That(disabled - free == 11 * frame, $"A disable until ten frames on holds eleven frames of updates ({free} against {disabled} ticks).");
    }

    /// <summary>
    /// <c>AIUpdate::update</c> steps its turrets (<c>updateTurretAI</c>) only while the object is not paralyzed, unmanned, EMP'd, subdued or hacked - and not at all while the AI is
    /// skipped: an EMP'd tank's turret stands where it was and turns on when the disable ends.
    /// </summary>
    public static void DisabledTurretStands()
    {
        var (world, feeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var tank = Slotted(world, "TurretTank");
        feeder.FireWeapon(1, tank.Id.Value, IdOf(world, "TurretTarget"));
        for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
        var turning = tank.TurretOf(0)!.Value.Angle;
        Check.That(turning != CnCNew.Simulation.Numerics.Fixed.Zero, "Setup: the turret turns towards the target.");
        world.DisableByEmpUntil(tank, world.Tick + world.Frames(10));
        for (var tick = 0; tick < world.Frames(10); tick++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle == turning, $"An EMP'd tank's turret stands ({turning.Raw} -> {tank.TurretOf(0)!.Value.Angle.Raw} raw).");
        for (var tick = 0; tick < world.Frames(5); tick++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle != turning, "It turns on when the disable is over.");
    }

    /// <summary>
    /// <c>BattlePlanUpdate</c>'s <c>paralyzeTroop</c> disables the object (<c>DISABLED_PARALYZED</c>), not only its AI: the turret stands with the rest of what does not process it,
    /// and the unit acts again when the paralysis is over. The port paralyzed the AI module alone, and the turret went on turning.
    /// </summary>
    public static void ParalysisDisablesTheObject()
    {
        var (world, feeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var tank = Slotted(world, "TurretTank");
        feeder.FireWeapon(1, tank.Id.Value, IdOf(world, "TurretTarget"));
        for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
        var turning = tank.TurretOf(0)!.Value.Angle;
        world.ParalyzeTroops(tank.Owner, ["VEHICLE"], [], world.Tick + world.Frames(10));
        Check.That((tank.DisabledFlags(world.Tick) & DisabledMask.Paralyzed) != 0, "The paralyzed tank carries DISABLED_PARALYZED.");
        for (var tick = 0; tick < world.Frames(10); tick++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle == turning, $"A paralyzed tank's turret stands ({turning.Raw} -> {tank.TurretOf(0)!.Value.Angle.Raw} raw).");
        for (var tick = 0; tick < world.Frames(5); tick++) world.Step();
        Check.That(tank.DisabledFlags(world.Tick) == DisabledMask.None && tank.TurretOf(0)!.Value.Angle != turning, "It turns on when the paralysis is over.");
    }
}
