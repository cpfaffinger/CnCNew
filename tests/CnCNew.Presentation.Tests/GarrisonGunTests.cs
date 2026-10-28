using System.Collections.Immutable;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The garrison gun (F-60, step 8; <c>GarrisonContain::putObjectAtGarrisonPoint</c>, <c>trackTargets</c>, <c>updateEffects</c>): a rifleman garrisoned in a bunker that fires at an enemy
/// shows a <c>GarrisonGun</c> drawable at his point, facing the enemy, with FIRING_A in the five frames after each shot (<c>MUZZLE_FLASH_LIFETIME</c>), and none once he has left.
/// </summary>
internal static class GarrisonGunTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/GarrisonGunFixture.ini"] = """
        Weapon GgRifle
          PrimaryDamage = 1
          AttackRange = 100.0
          DamageType = SMALL_ARMS
          DelayBetweenShots = 1000
        End
        Object GarrisonGun
          KindOf = SELECTABLE IMMOBILE
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = GgBarrel
            End
            ConditionState = FIRING_A
              Model = GgBarrel
            End
          End
          Draw = W3DModelDraw ModuleTag_02
            DefaultConditionState
              Model = None
            End
            ConditionState = FIRING_A
              Model = GgFlash
            End
          End
          Body = InactiveBody ModuleTag_03
          End
        End
        Object GgBunker
          KindOf = STRUCTURE
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = GgBunk
            End
          End
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 5
          End
        End
        Object GgRifleman
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          VisionRange = 150.0
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = GgMan
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY GgRifle
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
          End
        End
        Object GgDummy
          KindOf = SELECTABLE INFANTRY
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = GgMan
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("GarrisonGun");
        t.Run("a garrisoned rifleman's gun barrel stands at his point facing his victim, flashes in the five frames after a shot at 30, 60 and 120 Hz, and goes when he leaves", Gun);
        return t.Failures;
    }

    private static void Gun()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: Files());
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
            world.Submit(new SpawnCommand(PlayerId.System, 1, world.Tick, "GgBunker", new PlayerId(1), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
            world.Submit(new SpawnCommand(PlayerId.System, 2, world.Tick, "GgRifleman", new PlayerId(1), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
            world.Submit(new SpawnCommand(PlayerId.System, 3, world.Tick, "GgDummy", new PlayerId(2), Fixed.Zero, Fixed.FromInt(80), Fixed.Zero, Fixed.Zero));
            world.Step();
            var bunker = world.Entities.Single(entity => entity.Template.Name == "GgBunker");
            var rifleman = world.Entities.Single(entity => entity.Template.Name == "GgRifleman");
            Assert(world.TryEnterContainer(rifleman, bunker), $"{rate}: setup - the rifleman enters the bunker.");
            var projector = new FrameProjector(ObjectVisualCatalog.Build(fixture.Graph));
            DrawableInstance? GunOf() => projector.Project(1f).Drawables.SingleOrDefault(drawable => drawable.TemplateName == FrameProjector.GarrisonGunTemplate);

            var shots = new List<long>();
            var flashing = new List<long>();
            var ticksPerFrame = world.Time.TicksPerOriginalFrame;
            for (var step = 0; step < world.Time.OriginalFrames(90); step++)
            {
                var before = rifleman.LastFireTickOf(0);
                world.Step();
                if (rifleman.LastFireTickOf(0) != before) shots.Add(rifleman.LastFireTickOf(0));
                projector.Advance(world);
                if (GunOf() is { } gun && gun.Conditions.Contains(ModelConditions.FiringA)) flashing.Add(projector.Tick);
            }
            Assert(shots.Count >= 2, $"{rate}: setup - the garrisoned rifleman fires ({shots.Count} shots).");
            var gunNow = GunOf();
            Assert(gunNow is { Entity.IsValid: false } && gunNow.Position.X == 0f && gunNow.Position.Y == 0f && MathF.Abs(gunNow.Yaw - MathF.PI / 2) < 0.001f,
                $"{rate}: a gun barrel stands at his point and faces the enemy to the north ({gunNow?.Position}, {gunNow?.Yaw}).");
            // The flash of the first shot: from the frame after it for five frames (in ticks of the rate).
            var first = shots[0];
            var window = flashing.Where(tick => tick > first && tick < shots[1]).ToArray();
            Assert(window.Length == 5 * ticksPerFrame && window[0] == first + ticksPerFrame,
                $"{rate}: FIRING_A from the frame after the shot for five frames ({window.Length} ticks from {(window.Length > 0 ? window[0] : -1)}, shot {first}).");

            world.Submit(new ExitContainerCommand(new PlayerId(1), 10, world.Tick, rifleman.Id));
            for (var step = 0; step < world.Time.OriginalFrames(5); step++) world.Step();
            projector.Advance(world);
            Assert(!rifleman.ContainedBy.IsValid && GunOf() is null, $"{rate}: once he has left, the gun is gone.");
        }
    }
}
