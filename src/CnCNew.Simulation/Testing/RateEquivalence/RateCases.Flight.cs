using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    private const int FlightMapCells = 600;

    /// <summary>The special power module of <paramref name="source"/> that carries the power <paramref name="powerName"/>.</summary>
    private static SpecialPowerModuleBase PowerOf(RateScene scene, Entity source, string powerName)
    {
        if (!scene.World.SpecialPowers.TryGet(powerName, out var info)) throw new InvalidOperationException($"Unknown special power {powerName}.");
        foreach (var module in source.Modules)
            if (module is SpecialPowerModuleBase power && power.PowerIndex == info.Index) return power;
        throw new InvalidOperationException($"{source.Template.Name} does not carry {powerName}.");
    }

    /// <summary>Steps until the power is ready (at most <paramref name="budgetFrames"/> frames of the original) and returns the steps that took, -1 when it never was.</summary>
    private static long WaitReady(RateScene scene, SpecialPowerModuleBase power, long budgetFrames)
        => power.IsReady(scene.World) ? 0 : scene.StepUntil(() => power.IsReady(scene.World), budgetFrames);

    private static void UsePowerAt(RateScene scene, Entity source, string powerName, int x, int y)
        => scene.Order(source.Owner.Value, (owner, sequence, now) => new UseSpecialPowerCommand(owner, sequence, now, source.Id, powerName, SpecialPowerTargetKind.Location, EntityId.Invalid, Fixed.FromInt(x), Fixed.FromInt(y)));

    /// <summary>Queues <paramref name="template"/> at <paramref name="factory"/> and steps until the new object exists; returns it with the steps from the order.</summary>
    private static (Entity? Unit, long Steps) Produce(RateScene scene, Entity factory, string template, long budgetFrames)
    {
        var queued = scene.World.Tick;
        scene.Order(factory.Owner.Value, (owner, sequence, now) => new QueueProductionCommand(owner, sequence, now, factory.Id, template));
        Entity? unit = null;
        var steps = scene.StepUntil(() => (unit = scene.Find(template, queued)) is not null, budgetFrames);
        return (unit, steps);
    }

    private static RateCaseGroup FlightCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, Flight);
        foreach (var (airfield, aircraft) in new[] { ("AmericaAirfield", "AmericaJetRaptor"), ("AmericaAirfield", "AmericaJetAurora"), ("ChinaAirfield", "ChinaJetMIG"), ("AmericaAirfield", "AmericaVehicleComanche") })
            builder.Add($"sortie {aircraft}", scene => MeasureSortie(scene, airfield, aircraft), airfield, aircraft);
        foreach (var helicopter in new[] { "AmericaVehicleComanche", "AmericaVehicleChinook" })
            builder.Add($"helicopter {helicopter}", scene => MeasureHelicopter(scene, helicopter), helicopter);
        builder.Add("paradrop SuperweaponParadropAmerica", MeasureParadrop, "AmericaCommandCenter", "AmericaJetCargoPlane", "AmericaParachute", "AmericaInfantryRanger");
        builder.Add("jet strafe AmericaJetRaptor", MeasureJetAttack, "AmericaAirfield", "AmericaJetRaptor", "ChinaTankOverlord");
        builder.Note("the catalogs carry no model geometry: parking places and runways are the fallback points of the template geometry, not the bones of the airfield models");
        builder.NotCovered("flight: runway and parking points from the model bones (the client's world; the suite builds without model geometry so that test and tool agree)");
        builder.NotCovered("flight: the other 30 JetAIUpdate objects (the general variants share the locomotors of Raptor, Aurora, MiG and Comanche; not verified per object)");
        return builder.Done();
    }

    /// <summary>Production at the airfield, take-off on a move order, cruise, a turn back, landing on an enter order and the taxi to the parking place.</summary>
    private static ImmutableArray<RateQuantity> MeasureSortie(RateScene scene, string airfieldName, string aircraftName)
    {
        scene.MapCells = FlightMapCells;
        var quantities = new RateQuantities(scene);
        var airfield = scene.Spawn(airfieldName, 1, 2000, 3000);
        // The Aurora asks for a strategy center next to the airfield.
        if (scene.Has("AmericaStrategyCenter") && scene.Reference(aircraftName).Side == "America") scene.Spawn("AmericaStrategyCenter", 1, 1000, 1000);
        scene.GiveMoney(1, 50000);
        scene.StepFrames(1);
        scene.Align();
        var (aircraft, built) = Produce(scene, airfield, aircraftName, RateScene.Seconds(180));
        quantities.Exact("production time", built);
        if (aircraft is null) return quantities.Done();
        var ai = aircraft.Modules.OfType<JetAIUpdate>().Single();
        // A helicopter keeps no parking place: it leaves the pad and hovers; a jet rolls to its place and stands.
        var helicopter = !scene.Reference(aircraftName).Locomotor!.IsWings;
        var parked = scene.StepUntil(() => ai.Phase == JetAIUpdate.JetPhase.None && !aircraft.HasMoveTarget && (helicopter || !ai.AllowAirLoco), RateScene.Seconds(180));
        quantities.Event(helicopter ? "produced to hovering" : "produced to parked", parked);
        if (parked < 0) return quantities.Done();
        var (homeX, homeY) = (aircraft.X, aircraft.Y);
        scene.StepFrames(RateScene.Seconds(1));
        scene.Align();

        var track = new Track { Stride = scene.Rate.TicksPerOriginalFrame };
        track.Record(aircraft);
        scene.Move(aircraft, 4500, 3000);
        long airborne = -1;
        long arrived = -1;
        var budget = scene.Time.OriginalFrames(RateScene.Seconds(180));
        for (long step = 1; step <= budget && arrived < 0; step++)
        {
            scene.Step();
            track.Record(aircraft);
            if (airborne < 0 && ai.AllowAirLoco) airborne = step;
            if (airborne >= 0 && !aircraft.HasMoveTarget && ai.Phase == JetAIUpdate.JetPhase.None) arrived = step;
        }
        quantities.Event("take-off: order to airborne", Elapsed(airborne));
        quantities.Event("order to arrival", Elapsed(arrived));
        quantities.Distance("cruise speed per second", track.TopSpeed * scene.Rate.TicksPerSecond);
        quantities.Distance("height at arrival", aircraft.Z, arrived >= 0);
        quantities.Distance("flight path length", track.Length(), arrived >= 0);
        if (arrived < 0) return quantities.Done();

        // The next measurement begins on a frame of the original like the first one: the arrival was seen after one tick, which is a whole frame at 30 Hz and a quarter of one at 120 Hz.
        scene.Align();
        var heading = aircraft.Angle;
        scene.Move(aircraft, 3000, 3000);
        var reversed = scene.StepUntil(() => FixedTrig.AngleDifference(heading, aircraft.Angle).Abs() >= FixedTrig.Pi - TurnMargin * 5, RateScene.Seconds(60));
        quantities.Event("turn back: order to reversed heading", reversed);
        var back = scene.StepUntil(() => !aircraft.HasMoveTarget && ai.Phase == JetAIUpdate.JetPhase.None, RateScene.Seconds(120));
        quantities.Event("reversed heading to arrival", back);

        // No command of a player sends an aircraft home (MoveAndEnterCommand needs a container, an airfield is none): the order the AI itself gives.
        scene.Align();
        ai.OrderLand(scene.World, airfield, true);
        var down = scene.StepUntil(() => !ai.AllowAirLoco, RateScene.Seconds(240));
        quantities.Event("landing: order to touchdown", down);
        var home = down < 0 ? -1 : scene.StepUntil(() => ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco, RateScene.Seconds(120));
        quantities.Event("touchdown to parked", home);
        quantities.Distance("parking position error", RateMeasure.Distance(aircraft.X, aircraft.Y, homeX, homeY), home >= 0);
        return quantities.Done();
    }

    /// <summary>A helicopter that appears on the ground away from an airfield: climb to its height, a straight flight, the stop.</summary>
    private static ImmutableArray<RateQuantity> MeasureHelicopter(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var reference = scene.Reference(name).Locomotor!;
        var helicopter = scene.Spawn(name, 1, 1000, 2000);
        scene.Align();
        var wanted = reference.PreferredHeight - Fixed.One;
        var climbed = helicopter.Z >= wanted ? 0 : scene.StepUntil(() => helicopter.Z >= wanted, RateScene.Seconds(60));
        quantities.Event("climb to the preferred height", climbed);
        scene.StepFrames(RateScene.Seconds(2));
        scene.Align();
        var track = Drive(scene, helicopter, Fixed.FromInt(2500), Fixed.FromInt(2000), RateScene.Seconds(180));
        var ended = track.EndStep >= 0;
        quantities.Distance("top speed per second", track.TopSpeed * scene.Rate.TicksPerSecond);
        quantities.Event("time to top speed", Elapsed(track.FirstAtTopSpeed));
        quantities.Event("arrival time", ended ? Elapsed(track.EndStep) : -1);
        quantities.Distance("arrival error", RateMeasure.Distance(helicopter.X, helicopter.Y, Fixed.FromInt(2500), Fixed.FromInt(2000)), ended);
        quantities.Distance("height at arrival", helicopter.Z, ended);
        quantities.Distance("lowest height in flight", track.Z.Aggregate(Fixed.MaxValue, Fixed.Min), ended);
        return quantities.Done();
    }

    /// <summary>The paradrop of the American command center: the plane from the map edge, five parachutes, five Rangers on the ground (DeliverPayloadAIUpdate, ParachuteContain).</summary>
    private static ImmutableArray<RateQuantity> MeasureParadrop(RateScene scene)
    {
        scene.MapCells = FlightMapCells;
        var quantities = new RateQuantities(scene);
        var center = scene.Spawn("AmericaCommandCenter", 1, 1500, 3000);
        scene.World.GrantScience(scene.One, "SCIENCE_Paradrop1");
        scene.StepFrames(1);
        var power = PowerOf(scene, center, "SuperweaponParadropAmerica");
        if (WaitReady(scene, power, RateScene.Seconds(300)) < 0) return quantities.Exact("order to plane", -1).Done();
        scene.Align();
        var start = scene.World.Tick;
        UsePowerAt(scene, center, "SuperweaponParadropAmerica", 3000, 3000);

        long plane = -1, firstDrop = -1, lastDrop = -1, firstLanding = -1, lastLanding = -1, planeGone = -1;
        Entity? aircraft = null;
        var landed = 0;
        var spread = Fixed.Zero;
        var budget = scene.Time.OriginalFrames(RateScene.Seconds(120));
        for (long step = 1; step <= budget && planeGone < 0; step++)
        {
            var result = scene.Step();
            if (aircraft is null && scene.Find("AmericaJetCargoPlane", start) is { } found) (aircraft, plane) = (found, step);
            foreach (var item in result.Events)
            {
                // A parachute leaves the plane; a soldier leaves his parachute on the ground.
                if (item.Kind != SimEventKind.EntityUncontained || scene.World.FindEntity(item.Entity) is not { } rider) continue;
                if (aircraft is not null && item.Other == aircraft.Id)
                {
                    if (firstDrop < 0) firstDrop = step;
                    lastDrop = step;
                }
                else if (rider.Template.Name == "AmericaInfantryRanger")
                {
                    landed++;
                    spread += RateMeasure.Distance(rider.X, rider.Y, Fixed.FromInt(3000), Fixed.FromInt(3000));
                    if (firstLanding < 0) firstLanding = step;
                    lastLanding = step;
                }
            }
            if (aircraft is not null && !scene.Alive(aircraft)) planeGone = step;
        }
        quantities.Event("order to plane", Elapsed(plane));
        quantities.Event("order to first drop", Elapsed(firstDrop));
        quantities.Event("first to last drop", firstDrop >= 0 ? lastDrop - firstDrop : -1);
        quantities.Event("order to first landing", Elapsed(firstLanding));
        quantities.Event("order to last landing", Elapsed(lastLanding));
        quantities.Count("soldiers landed", landed);
        quantities.Distance("mean landing distance from the target", landed > 0 ? spread / Fixed.FromInt(landed) : Fixed.Zero, landed > 0);
        quantities.Event("order to plane gone", Elapsed(planeGone));
        return quantities.Done();
    }

    /// <summary>The jet-strafe scene of the client audits: a parked Raptor is ordered to attack a tank, empties its clip, returns and reloads.</summary>
    private static ImmutableArray<RateQuantity> MeasureJetAttack(RateScene scene)
    {
        scene.MapCells = FlightMapCells;
        var quantities = new RateQuantities(scene);
        var airfield = scene.Spawn("AmericaAirfield", 1, 2000, 3000);
        var tank = scene.Spawn("ChinaTankOverlord", 2, 4200, 3000);
        scene.GiveMoney(1, 50000);
        scene.StepFrames(1);
        var (raptor, _) = Produce(scene, airfield, "AmericaJetRaptor", RateScene.Seconds(180));
        if (raptor is null) return quantities.Event("order to first shot", -1).Done();
        var ai = raptor.Modules.OfType<JetAIUpdate>().Single();
        if (scene.StepUntil(() => ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco, RateScene.Seconds(180)) < 0) return quantities.Event("order to first shot", -1).Done();
        scene.StepFrames(RateScene.Seconds(1));
        scene.Align();
        var start = scene.World.Tick;
        var clip = raptor.WeaponClip;
        scene.Order(1, (owner, sequence, now) => new PursueCommand(owner, sequence, now, raptor.Id, tank.Id, tank.X, tank.Y));
        long firstShot = -1, lastShot = -1, reloaded = -1;
        var shots = 0;
        var budget = scene.Time.OriginalFrames(RateScene.Seconds(240));
        for (long step = 1; step <= budget && reloaded < 0; step++)
        {
            if (scene.Alive(tank) && raptor.WeaponClip > 0) scene.Fire(raptor, tank);
            var result = scene.Step();
            foreach (var item in result.Events)
            {
                if (item.Kind != SimEventKind.WeaponFired || item.Entity != raptor.Id) continue;
                shots++;
                if (firstShot < 0) firstShot = step;
                lastShot = step;
            }
            if (shots > 0 && ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco && raptor.WeaponClip == clip) reloaded = step;
        }
        quantities.Event("order to first shot", Elapsed(firstShot));
        quantities.Event("order to last shot", Elapsed(lastShot));
        quantities.Count("shots of the sortie", shots);
        quantities.Event("order to parked and reloaded", Elapsed(reloaded));
        return quantities.Done();
    }
}
