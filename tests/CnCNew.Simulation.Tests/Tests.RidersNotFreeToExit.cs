using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Testing;

/// <summary>
/// F-51 (docs/REFORGED-FIXES.md R-23): <c>OpenContain::onDie</c> calls <c>TransportContain::killRidersWhoAreNotFreeToExit</c> before it puts the riders out - every rider that may not
/// leave (<c>isSpecificRiderFreeToExit</c>: here one without a locomotor) is killed, or destroyed with <c>DestroyRidersWhoAreNotFreeToExit</c>. The port put every rider out alive but
/// for a payload plane's.
/// </summary>
static partial class Tests
{
    public static void RidersNotFreeToExitDieWithTheirTransport()
    {
        foreach (var destroy in new[] { false, true })
        {
            var files = new Dictionary<string, string>(Phase5Files())
            {
                ["Data/INI/Object/RiderFixture.ini"] = $$"""
                Locomotor RiderLegs
                  Surfaces = GROUND
                  Speed = 20.0
                End
                Object RiderTruck
                  KindOf = SELECTABLE VEHICLE
                  Body = ActiveBody Body01
                    MaxHealth = 300.0
                    InitialHealth = 300.0
                  End
                  Behavior = DestroyDie Destroy01
                  End
                  Behavior = AIUpdateInterface AI01
                  End
                  Behavior = TransportContain Transport01
                    Slots = 3
                    AllowInsideKindOf = INFANTRY
                    DestroyRidersWhoAreNotFreeToExit = {{(destroy ? "Yes" : "No")}}
                  End
                End
                Object RiderWalker
                  KindOf = SELECTABLE INFANTRY
                  TransportSlotCount = 1
                  Locomotor = SET_NORMAL RiderLegs
                  Body = ActiveBody Body01
                    MaxHealth = 50.0
                    InitialHealth = 50.0
                  End
                  Behavior = AIUpdateInterface AI01
                  End
                  Behavior = DestroyDie Destroy01
                  End
                End
                Object RiderCrate
                  KindOf = SELECTABLE INFANTRY
                  TransportSlotCount = 1
                  Body = ActiveBody Body01
                    MaxHealth = 50.0
                    InitialHealth = 50.0
                  End
                  Behavior = AIUpdateInterface AI01
                  End
                  Behavior = DestroyDie Destroy01
                  End
                End
                """,
            };
            var fixture = new SyntheticFixture(TestRate.Default, files: files);
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "RiderTruck", x: 100, y: 100);
            feeder.Spawn(1, "RiderWalker", x: 100, y: 100);
            feeder.Spawn(1, "RiderCrate", x: 100, y: 100);
            world.Step();
            var truck = world.Entities.Single(entity => entity.Template.Name == "RiderTruck");
            var walker = world.Entities.Single(entity => entity.Template.Name == "RiderWalker");
            var crate = world.Entities.Single(entity => entity.Template.Name == "RiderCrate");
            Check.That(world.TryEnterContainer(walker, truck) && world.TryEnterContainer(crate, truck), "Both riders board the truck.");
            world.Kill(truck.Id);
            for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
            Check.That(world.FindEntity(walker.Id) is { EffectivelyDead: false, ContainedBy.IsValid: false }, "A rider that can stand where the truck died is put out alive.");
            if (destroy) Check.That(world.FindEntity(crate.Id) is null, "With DestroyRidersWhoAreNotFreeToExit a rider without a locomotor is destroyed with the truck.");
            else Check.That(world.FindEntity(crate.Id) is null or { EffectivelyDead: true }, "A rider without a locomotor dies with the truck.");
        }
    }
}
