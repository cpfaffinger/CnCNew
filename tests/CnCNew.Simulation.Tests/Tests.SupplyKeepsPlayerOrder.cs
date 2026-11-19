using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// Rule fix <c>supply-truck-keeps-player-order</c> (31; Zero Hour Reforged, R-36; F-06 of Phase 39): a supply carrier whose dock has just ended enters "wanting" at its next machine
/// step and orders its next dock at the step after; <c>SupplyTruckWantsToPickUpOrDeliverBoxesState::update</c> runs before the transitions and docks without a check, so a player's order
/// given in between is overwritten in the original. Under the fix the carrier keeps it.
/// </summary>
static partial class Tests
{
    public static void SupplyCarrierKeepsAPlayerOrderGivenAsItWantsToDock()
    {
        foreach (var carrier in new[] { "Hauler", "Laborer" })
        foreach (var (rate, factor) in WorldRates)
        foreach (var (rules, keeps) in new[] { (RuleProfile.Original, false), (RuleProfile.Parse("supply-truck-keeps-player-order"), true), (RuleProfile.Product, true) })
        {
            var label = $"{carrier}, {rate}, {rules.Name}";
            var fixture = new SyntheticFixture(rate, files: WorkerBrainFiles(), rules: rules);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, string.Empty), fixture.Templates, null);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Depot", x: -100);
            feeder.Spawn(1, "Warehouse", x: 100);
            feeder.Spawn(1, carrier, x: 0);
            world.Step();
            var unit = world.FindEntity(new EntityId(IdOf(world, carrier)))!;
            var ai = unit.Modules.OfType<AIUpdateInterface>().Single();
            Check.That(feeder.Dock(1, unit.Id.Value, (uint)IdOf(world, "Warehouse")).Accepted, $"{label}: the carrier is sent to the warehouse.");

            // To the end of the dock at the warehouse: the carrier stands idle with its boxes.
            var docked = false;
            for (var step = 0; step < world.Frames(2_000) && !(docked && !ai.IsDocking); step++)
            {
                world.Step();
                docked |= ai.IsDocking;
            }
            Check.That(docked && !ai.IsDocking && ai.IsIdle, $"{label}: the carrier has loaded at the warehouse and stands idle.");
            // The machine's next step (on a frame boundary of the original) enters "wanting"; the one after orders the dock at the depot.
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            world.Step();
            Check.That(ai.IsIdle && !ai.IsDocking, $"{label}: the carrier wants to deliver and has not been ordered to the depot yet.");
            Check.That(feeder.SetMoveTarget(1, unit.Id.Value, 0, 150).Accepted, $"{label}: the player sends it away in that frame.");

            var deposited = false;
            var dockedAgain = false;
            for (var step = 0; step < world.Frames(600); step++)
            {
                deposited |= world.Step().Events.Any(item => item.Kind == SimEventKind.SupplyDeposited);
                dockedAgain |= ai.IsDocking;
            }
            var near = (unit.X - Fixed.Zero).Abs() <= Fixed.FromInt(5) && (unit.Y - Fixed.FromInt(150)).Abs() <= Fixed.FromInt(5);
            if (keeps)
                Check.That(!dockedAgain && !deposited && near && ai.IsIdle && world.MoneyOf(new PlayerId(1)) == Fixed.Zero,
                    $"{label}: the fix keeps the player's order - the carrier goes to the place and stays ({unit.X.ToDecimal()}, {unit.Y.ToDecimal()}; docked {dockedAgain}, deposited {deposited}).");
            else
                Check.That(dockedAgain && deposited && !near,
                    $"{label}: the original overwrites the player's order with the dock at the depot ({unit.X.ToDecimal()}, {unit.Y.ToDecimal()}; docked {dockedAgain}, deposited {deposited}).");
        }
    }
}
