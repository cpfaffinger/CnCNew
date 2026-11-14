using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Testing;

namespace CnCNew.Game.Tests;

/// <summary>The owner's playtest of 8 November 2026: "the fuel bomb still does not work" - a power used the way a player uses it, in a skirmish of a shipped map, on the enemy's command center.</summary>
internal static class FuelAirBombTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(FuelAirBombTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP FuelAirBombTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A Daisy Cutter used on the enemy's command center in a real skirmish detonates over it and hurts it", () => Drop(data, fuelAirBomb: false));
        harness.Run("The upgraded fuel air bomb (SCIENCE_MOAB) used on it detonates and hurts it", () => Drop(data, fuelAirBomb: true));
        return harness.Failures;
    }

    private static void Drop(GameData data, bool fuelAirBomb)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, "FactionAmerica", ["FactionChina"], SkirmishMatchOptions.Standard) { Debug = new MatchDebugOptions(MatchDebugOptions.NormalBuildTime, 0, 20) };
        var live = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        void Run(double seconds)
        {
            for (var frame = 0; frame < seconds * CnCNew.Tests.TestTime.FramesPerSecond; frame++) live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        }
        Run(2);
        var (world, human) = (live.World, live.Human);
        // The rank-5 power needs rank 5 and its purchase, the upgrade of the strategy center is granted as that upgrade grants it.
        world.SetRankLevel(human, 5);
        TestHarness.Assert(live.Commands.Issue((sequence, tick) => new PurchaseScienceCommand(human, sequence, tick, "SCIENCE_DaisyCutter")).Accepted, "the science is bought with the general's points");
        Run(1);
        if (fuelAirBomb) TestHarness.Assert(world.GrantScience(human, "SCIENCE_MOAB"), "the fuel air bomb is granted");
        var center = world.Entities.First(entity => entity.Owner == human && entity.Template.Name == "AmericaCommandCenter");
        var enemy = world.Entities.First(entity => entity.Owner != human && entity.Template.IsKindOf("COMMANDCENTER"));
        var maxHealth = enemy.Body!.MaxHealth;
        var blast = false;
        var lowest = maxHealth;
        live.TickStepped += result =>
        {
            // The enemy's dozers repair it afterwards: the lowest health is what the bomb did.
            lowest = enemy.EffectivelyDead ? CnCNew.Simulation.Numerics.Fixed.Zero : CnCNew.Simulation.Numerics.Fixed.Min(lowest, enemy.Body.Health);
            foreach (var e in result.Events)
                if (e.Kind == SimEventKind.FxRequested && e.Text is "FX_DaisyCutterFinalExplosion" || e.Kind == SimEventKind.WeaponFireRequested && e.Text == "MOABDetonationWeapon") blast = true;
        };
        var use = live.Commands.Issue((sequence, tick) => new UseSpecialPowerCommand(human, sequence, tick, center.Id, "SuperweaponDaisyCutter", SpecialPowerTargetKind.Location, EntityId.Invalid, enemy.X, enemy.Y));
        TestHarness.Assert(use.Accepted, $"the power is used ({use.Reason})");
        Run(45);
        // The gas cloud of the bomb's death (DaisyExplosionScatter, AttachToObject) dies with the bomb that carries it: FXListDie's OrientToObject is true by default,
        // so the effect list is made for the object and attached (FXListDie.h), and a system whose object is gone destroys itself (ParticleSystem::update).
        Run(10);
        TestHarness.Assert(live.Fx.Particles.SystemsNamed("Daisy") == 0, $"no gas system is left over the place ({live.Fx.Particles.SystemCount} systems)");
        TestHarness.Assert(blast, "the bomb detonates (the gas's last phase, or the fuel air bomb's weapon)");
        TestHarness.Assert(lowest < maxHealth, $"the command center under it is hurt ({lowest} of {maxHealth} at the lowest)");
    }
}
