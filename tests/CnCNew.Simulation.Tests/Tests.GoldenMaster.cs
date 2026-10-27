using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// Golden master for the damage chain: the expected shot ticks, per-shot damage and kill tick are recomputed here by an
    /// independent model of the reference rules instead of being read back from the simulation.
    /// Sources (references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/Object):
    /// <c>Armor.cpp ArmorTemplate::adjustDamage</c> (damage * coefficient[type], floored at 0; UNRESISTABLE bypasses armor),
    /// <c>Weapon.cpp Weapon::privateFireWeapon</c> (after a shot the next one is allowed <c>DelayBetweenShots</c> later, or
    /// <c>ClipReloadTime</c> later when that shot emptied the clip), <c>ActiveBody.cpp attemptDamage</c> (health floors at 0, death when it reaches 0).
    /// Milliseconds convert to ticks at 30 per second (<c>LOGICFRAMES_PER_SECOND</c>): 500 ms = 15, 2000 ms = 60.
    /// </summary>
    public static void Phase5GoldenDamageChain()
    {
        // Golden armor arithmetic, by hand: 20 * 100% = 20, 20 * 50% = 10, 40 * 50% = 20.
        foreach (var (armored, expectedDamage, expectedShots) in new[] { (false, 20, 5), (true, 10, 10) })
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Fighter", x: 0, y: 0);
            feeder.Spawn(2, "Fighter", x: 10, y: 0);
            world.Step();
            if (armored)
            {
                feeder.GiveMoney(2, 500);
                world.Step();
                feeder.GrantUpgrade(2, "Upgrade_Armor", target: 2);
                world.Step();
            }

            var shotTicks = new List<long>();
            var damages = new List<long>();
            long diedTick = -1;
            for (var i = 0; i < world.Frames(400) && diedTick < 0; i++)
            {
                feeder.FireWeapon(1, 1, 2);
                var result = world.Step();
                foreach (var simEvent in result.Events)
                {
                    if (simEvent.Kind == SimEventKind.WeaponFired) { shotTicks.Add(simEvent.Tick); damages.Add(simEvent.Value); }
                    if (simEvent.Kind == SimEventKind.EntityDied && simEvent.Entity.Value == 2) diedTick = simEvent.Tick;
                }
            }

            // Independent model: ClipSize 2, delay 15, reload 60; the first shot is the observed first tick.
            var expectedTicks = new List<long>();
            long ready = shotTicks[0];
            var clip = 2;
            for (var shot = 0; shot < expectedShots; shot++)
            {
                expectedTicks.Add(ready);
                clip--;
                if (clip == 0) { ready += world.Frames(60); clip = 2; }
                else ready += world.Frames(15);
            }
            var label = armored ? "half armor" : "no armor";
            Check.That(shotTicks.SequenceEqual(expectedTicks), $"{label}: shot ticks {string.Join(",", shotTicks)} must equal the model {string.Join(",", expectedTicks)}.");
            Check.That(damages.All(value => value == Fixed.FromInt(expectedDamage).Raw), $"{label}: every shot deals {expectedDamage} after the armor coefficient.");
            Check.That(diedTick == expectedTicks[^1], $"{label}: the target dies on the shot that brings 100 health to 0 (tick {diedTick} vs {expectedTicks[^1]}).");
        }
    }

    /// <summary>
    /// Golden master for the production clock: <c>ThingTemplate::calcTimeToBuild</c> is <c>BuildTime * LOGICFRAMES_PER_SECOND</c>
    /// (BuildTime 2 = 60 ticks) and <c>ProductionUpdate</c> builds the queue head first, so consecutive units complete exactly that many ticks apart.
    /// The money ledger follows <c>Player::getMoney</c>: the cost leaves the account when queued.
    /// </summary>
    public static void Phase5GoldenProduction()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Factory", x: 5, y: 5);
        world.Step();
        feeder.GiveMoney(1, 1000);
        world.Step();
        feeder.QueueProduction(1, 1, "Fighter");
        feeder.QueueProduction(1, 1, "Fighter");
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(600), "Two Fighters at BuildCost 200 leave 600 of 1000.");

        var completed = new List<long>();
        for (var i = 0; i < world.Frames(400); i++)
            foreach (var simEvent in world.Step().Events)
                if (simEvent.Kind == SimEventKind.ProductionCompleted) completed.Add(simEvent.Tick);
        Check.That(completed.Count == 2, $"Both queued units complete (got {completed.Count}).");
        Check.That(completed[1] - completed[0] == world.Frames(2 * 30), $"Consecutive completions are BuildTime * 30 = 60 ticks apart (got {completed[1] - completed[0]}).");
        Check.That(world.Entities.Count(value => value.Template.Name == "Fighter") == 2 && world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(600), "Exactly two Fighters exist and no further money moved.");
    }
}
