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
}
