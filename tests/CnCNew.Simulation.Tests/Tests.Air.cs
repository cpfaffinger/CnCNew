using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// The air group: airfields and aircraft (<c>ParkingPlaceBehavior</c>, <c>JetAIUpdate</c>), the Spectre gunship, silo doors, the neutron missile with its blast, EMP and leaflet pulses and the
/// bunker buster. The fixture is small and synthetic (flat map without terrain; aircraft fly at a preferred height of 60, the airfield has two rows and one column so that both aircraft share one runway).
/// </summary>
static partial class Tests
{
    private static readonly IReadOnlyDictionary<string, string> AirFixtureFiles = new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/FXList.ini"] = "FXList FX_Test\nEnd\nFXList FX_Other\nEnd\nFXList AirDoorFx\nEnd\nFXList AirOpenFx\nEnd\nFXList AirLaunchFx\nEnd\nFXList AirNukeFx\nEnd\nFXList AirIgnitionFx\nEnd\nFXList AirBustFx\nEnd\nFXList AirCrashFx\nEnd\n",
        ["Data/INI/Upgrade.ini"] = "Upgrade Upgrade_AirBusters\n  Type = PLAYER\n  BuildCost = 10\n  BuildTime = 1\nEnd\n",
        ["Data/INI/Object/AirFixture.ini"] = """
        Locomotor AirJetLoco
          Surfaces = AIR
          Speed = 150.0
          Acceleration = 450.0
          Braking = 60.0
          MinSpeed = 60.0
          TurnRate = 360.0
          Lift = 120.0
          PreferredHeight = 60.0
          CloseEnoughDist = 4.0
        End
        Locomotor AirFastLoco
          Surfaces = AIR
          Speed = 300.0
          Acceleration = 900.0
          Braking = 60.0
          MinSpeed = 60.0
          TurnRate = 360.0
          Lift = 120.0
          PreferredHeight = 60.0
          CloseEnoughDist = 4.0
        End
        Locomotor AirTaxiLoco
          Surfaces = GROUND
          Speed = 60.0
          Acceleration = 300.0
          Braking = 999999.0
          TurnRate = 360.0
        End
        Locomotor AirHeliLoco
          Surfaces = AIR
          Speed = 120.0
          Acceleration = 300.0
          Braking = 300.0
          TurnRate = 360.0
          Lift = 120.0
          PreferredHeight = 40.0
          CloseEnoughDist = 4.0
        End
        Locomotor AirGunshipLoco
          Surfaces = AIR
          Speed = 120.0
          Acceleration = 900.0
          Braking = 900.0
          TurnRate = 180.0
          PreferredHeight = 50.0
        End
        Locomotor AirTransitLoco
          Surfaces = AIR
          Speed = 240.0
          Acceleration = 900.0
          Braking = 900.0
          TurnRate = 180.0
          PreferredHeight = 50.0
        End

        Weapon AirJetMissile
          PrimaryDamage = 20.0
          AttackRange = 2000.0
          ClipSize = 2
          ClipReloadTime = 2000
          DelayBetweenShots = 100
          AutoReloadsClip = RETURN_TO_BASE
        End
        Weapon AirHeliGun
          PrimaryDamage = 5.0
          AttackRange = 2000.0
          ClipSize = 1
          ClipReloadTime = 1000
          DelayBetweenShots = 100
          AutoReloadsClip = RETURN_TO_BASE
        End

        Object AirTestAirfield
          Side = Alpha
          KindOf = STRUCTURE FS_AIRFIELD SELECTABLE
          BuildCost = 100
          Geometry = BOX
          GeometryMajorRadius = 40.0
          GeometryMinorRadius = 30.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = ParkingPlaceBehavior Park01
            NumRows = 2
            NumCols = 1
            HasRunways = Yes
            ApproachHeight = 20.0
            HealAmountPerSecond = 60.0
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 5
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirTestJet
          Side = Alpha
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE
          BuildCost = 100
          BuildTime = 1
          Locomotor = SET_NORMAL AirJetLoco
          Locomotor = SET_TAXIING AirTaxiLoco
          Locomotor = SET_SUPERSONIC AirFastLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AirJetMissile
          End
          Behavior = JetAIUpdate Jet01
            TakeoffPause = 300
            ReturnToBaseIdleTime = 1500
            OutOfAmmoDamagePerSecond = 30%
            AttackLocomotorType = SET_SUPERSONIC
            AttackLocomotorPersistTime = 1000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirTestHeli
          Side = Alpha
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE PRODUCED_AT_HELIPAD
          BuildCost = 100
          BuildTime = 1
          Locomotor = SET_NORMAL AirHeliLoco
          Locomotor = SET_TAXIING AirTaxiLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AirHeliGun
          End
          Behavior = JetAIUpdate Heli01
            NeedsRunway = No
            KeepsParkingSpaceWhenAirborne = No
            ReturnToBaseIdleTime = 1500
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AirDummy
          Side = Beta
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static SimulationWorld AirWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(files: AirFixtureFiles);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 9, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    private static void StepUntil(SimulationWorld world, Func<bool> condition, int maxTicks, string failure)
    {
        for (var tick = 0; tick < maxTicks; tick++)
        {
            if (condition()) return;
            world.Step();
        }
        if (!condition()) throw new InvalidOperationException(failure);
    }

    private static JetAIUpdate JetOf(Entity entity) => entity.Modules.OfType<JetAIUpdate>().Single();

    /// <summary>An airfield with one jet produced at it, parked and ready: (world, feeder, airfield, parking module, jet).</summary>
    private static (SimulationWorld World, Feeder Feeder, Entity Airfield, ParkingPlaceBehavior Parking, Entity Jet, SyntheticFixture Fixture) ParkedJet(bool dummy = true)
    {
        var world = AirWorld(out var fixture, out var feeder);
        Check.That(fixture.Templates.TryGet("AirTestJet", out var jet) && jet.IsInstantiable && fixture.Templates.TryGet("AirTestAirfield", out var field) && field.IsInstantiable,
            "The airfield and the jet bind (nothing missing): " + string.Join(",", fixture.Templates.Templates.Where(value => !value.IsInstantiable).SelectMany(value => value.MissingModules)));
        feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
        if (dummy) feeder.Spawn(2, "AirDummy", owner: 2, x: 1500, y: 500);
        world.Step();
        feeder.GiveMoney(1, 1000);
        world.Step();
        var airfield = Get(world, "AirTestAirfield");
        Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet").Accepted, "The jet is queued at the airfield.");
        StepUntil(world, () => world.Entities.Any(value => value.Template.Name == "AirTestJet"), 200, "The airfield produced the jet.");
        var aircraft = Get(world, "AirTestJet");
        StepUntil(world, () => JetOf(aircraft).Phase == JetAIUpdate.JetPhase.None, 600, "The produced jet drove to its space and finished loading: " + JetOf(aircraft).Phase);
        return (world, feeder, airfield, airfield.Modules.OfType<ParkingPlaceBehavior>().Single(), aircraft, fixture);
    }

    /// <summary>
    /// A produced jet leaves the hangar to its space (the airfield's reservation), takes off on an order through the runway states, flies at its preferred height, empties its clip on the enemy,
    /// returns for ammunition, lands, taxis back to its space and reloads; the airfield heals it while it stands there; the whole run survives a snapshot at every phase.
    /// </summary>
    public static void AirJetFlightLifecycle()
    {
        var (world, feeder, airfield, parking, jet, fixture) = ParkedJet();
        var ai = JetOf(jet);
        var info = parking.InfoOfSpace(world, 0);
        Check.That(parking.OccupantOf(0) == jet.Id && ai.Airfield == airfield.Id && !ai.AllowAirLoco, "The jet holds the first space of its airfield and may not fly yet.");
        Check.That((jet.X - info.ParkingSpace.X).Abs() < Fixed.FromDecimal(0.1m) && (jet.Y - info.ParkingSpace.Y).Abs() < Fixed.FromDecimal(0.1m) && jet.Z == Fixed.Zero, $"It stands on its parking space ({jet.X}, {jet.Y}) vs ({info.ParkingSpace.X}, {info.ParkingSpace.Y}).");
        Check.That(FixedTrig.AngleDifference(jet.Angle, info.ParkingOrientation).Abs() <= Fixed.FromDecimal(0.002m), "It faces the parking direction.");
        Check.That(jet.WeaponClip == 2, "Its clip is full.");

        // the parked jet is healed by the airfield
        world.Damage(jet.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(100), DamageTypes.Explosion, DeathTypes.Normal, false));
        var hurt = jet.Body!.Health;
        for (var i = 0; i < 40; i++) world.Step();
        Check.That(jet.Body.Health > hurt, $"The airfield heals the parked jet ({hurt} -> {jet.Body.Health}).");

        // take-off on a move order
        var phases = new List<JetAIUpdate.JetPhase>();
        Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1200, 500).Accepted, "The move order is accepted.");
        var tookOff = false;
        for (var tick = 0; tick < 900; tick++)
        {
            world.Step();
            if (phases.Count == 0 || phases[^1] != ai.Phase) phases.Add(ai.Phase);
            if (ai.Phase == JetAIUpdate.JetPhase.None && ai.AllowAirLoco && jet.HasMoveTarget) { tookOff = true; break; }
        }
        Check.That(tookOff, "The jet got airborne: " + string.Join(">", phases));
        Check.That(phases.SequenceEqual([JetAIUpdate.JetPhase.TaxiToTakeoff, JetAIUpdate.JetPhase.PauseBeforeTakeoff, JetAIUpdate.JetPhase.TakingOff, JetAIUpdate.JetPhase.None]),
            "The take-off runs through the runway states: " + string.Join(">", phases));
        Check.That(parking.OccupantOf(0) == jet.Id, "A jet keeps its space while airborne.");
        StepUntil(world, () => !jet.HasMoveTarget, 900, "The jet reaches its destination.");
        Check.That((jet.X - Fixed.FromInt(1200)).Abs() < Fixed.FromInt(10) && (jet.Y - Fixed.FromInt(500)).Abs() < Fixed.FromInt(10), $"It arrived at the order's destination ({jet.X}, {jet.Y}).");
        for (var i = 0; i < 90 && jet.Z < Fixed.FromInt(60); i++) world.Step();
        Check.That(jet.Z == Fixed.FromInt(60), $"It flies at the locomotor's preferred height ({jet.Z}).");

        // fire both missiles: the clip is empty and does not reload by time
        var dummy = Get(world, "AirDummy");
        Check.That(feeder.FireWeapon(1, jet.Id.Value, dummy.Id.Value).Accepted, "The first shot is accepted.");
        for (var i = 0; i < 5; i++) world.Step();
        Check.That(feeder.FireWeapon(1, jet.Id.Value, dummy.Id.Value).Accepted, "The second shot is accepted.");
        world.Step();
        Check.That(jet.WeaponClip == 0 && jet.WeaponReadyTick >= CnCNew.Simulation.Time.SimulationTiming.SleepForever, $"A RETURN_TO_BASE weapon never reloads by time (clip {jet.WeaponClip}, ready {jet.WeaponReadyTick}).");
        Check.That(ai.IsOutOfSpecialReloadAmmo(world), "The jet knows it is out of special-reload ammunition.");

        // it returns, lands, taxis, reloads
        var back = new List<JetAIUpdate.JetPhase>();
        var snapshots = 0;
        for (var tick = 0; tick < 2500; tick++)
        {
            world.Step();
            if (back.Count == 0 || back[^1] != ai.Phase)
            {
                back.Add(ai.Phase);
                if (snapshots++ < 14)
                {
                    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    Check.That(restored.StateHash == world.StateHash, $"A snapshot in phase {ai.Phase} restores to the same hash.");
                    for (var follow = 0; follow < 40; follow++)
                        Check.That(restored.Step().StateHash == world.Step().StateHash, $"The restored world follows the original through phase {ai.Phase} (step {follow}).");
                }
            }
            if (back.Count > 2 && ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco) break;
        }
        Check.That(!ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None, "The jet is parked again: " + string.Join(">", back));
        Check.That(back.Contains(JetAIUpdate.JetPhase.ReturningForLanding) && back.Contains(JetAIUpdate.JetPhase.AwaitLanding) && back.Contains(JetAIUpdate.JetPhase.Landing)
            && back.Contains(JetAIUpdate.JetPhase.TaxiFromLanding) && back.Contains(JetAIUpdate.JetPhase.OrientForParking) && back.Contains(JetAIUpdate.JetPhase.ReloadAmmo), "The return runs through every landing state: " + string.Join(">", back));
        Check.That(jet.WeaponClip == 2 && jet.WeaponReadyTick <= world.Tick, $"The clip is full again (clip {jet.WeaponClip}).");
        Check.That((jet.X - info.ParkingSpace.X).Abs() < Fixed.FromDecimal(0.1m) && (jet.Y - info.ParkingSpace.Y).Abs() < Fixed.FromDecimal(0.1m) && jet.Z == Fixed.Zero, $"It is back on its space ({jet.X}, {jet.Y}, {jet.Z}).");
        Check.That(parking.OccupantOf(0) == jet.Id, "It still holds its space.");
    }

    /// <summary>Two jets share one runway: the second waits as next in line, then starts after the first; an idle jet returns on its own after <c>ReturnToBaseIdleTime</c> and a full airfield holds production back.</summary>
    public static void AirRunwayQueueIdleReturnAndFullAirfield()
    {
        var (world, feeder, airfield, parking, first, _) = ParkedJet(dummy: false);
        Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet").Accepted, "A second jet is queued.");
        StepUntil(world, () => world.Entities.Count(value => value.Template.Name == "AirTestJet") == 2, 200, "The second jet was produced.");
        var second = world.Entities.Where(value => value.Template.Name == "AirTestJet").OrderBy(value => value.Id.Value).Last();
        StepUntil(world, () => JetOf(second).Phase == JetAIUpdate.JetPhase.None, 600, "The second jet parked: " + JetOf(second).Phase);
        Check.That(parking.OccupantOf(0) == first.Id && parking.OccupantOf(1) == second.Id, "Each jet holds one of the two spaces.");
        Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet").Accepted, "A third jet can be queued.");
        for (var i = 0; i < 150; i++) world.Step();
        Check.That(world.Entities.Count(value => value.Template.Name == "AirTestJet") == 2, "With both spaces taken the airfield holds the finished third jet back (nothing was created).");
        Check.That(!parking.IsFreeToExit, "The airfield has no free exit.");

        // both jets are ordered away at the same moment: one runway
        var firstJet = JetOf(first);
        var secondJet = JetOf(second);
        feeder.SetMoveTarget(1, first.Id.Value, 1200, 500);
        feeder.SetMoveTarget(1, second.Id.Value, 1200, 560);
        long firstTakeoff = -1, secondTakeoff = -1, secondWaited = 0;
        var secondAtPrep = false;
        for (var tick = 0; tick < 1500 && (firstTakeoff < 0 || secondTakeoff < 0 || firstJet.Phase == JetAIUpdate.JetPhase.TakingOff || secondJet.Phase == JetAIUpdate.JetPhase.TakingOff); tick++)
        {
            world.Step();
            if (firstTakeoff < 0 && firstJet.Phase == JetAIUpdate.JetPhase.TakingOff) firstTakeoff = world.Tick;
            if (secondTakeoff < 0 && secondJet.Phase == JetAIUpdate.JetPhase.TakingOff) secondTakeoff = world.Tick;
            if (secondJet.Phase == JetAIUpdate.JetPhase.AwaitTakeoff) secondWaited++;
            if (secondJet.Phase == JetAIUpdate.JetPhase.PauseBeforeTakeoff && !secondAtPrep)
            {
                secondAtPrep = true;
                var prep = parking.InfoOfSpace(world, 1).RunwayPrep;
                Check.That((second.X - prep.X).Abs() < Fixed.FromDecimal(0.1m) && (second.Y - prep.Y).Abs() < Fixed.FromDecimal(0.1m), "The jet that queued waits at the taxi point, not on the runway start.");
            }
            Check.That(!(firstJet.Phase == JetAIUpdate.JetPhase.TakingOff && secondJet.Phase == JetAIUpdate.JetPhase.TakingOff) || first.Z >= Fixed.FromInt(20), "Two jets never use the runway at the same time (the first has left the ground when the second starts).");
        }
        Check.That(firstTakeoff > 0 && secondTakeoff > firstTakeoff, $"The second jet starts its run after the first (first {firstTakeoff}, second {secondTakeoff}).");
        Check.That(secondWaited > 0 && secondAtPrep, "The second jet waited for the runway as next in line.");
        StepUntil(world, () => !first.HasMoveTarget && !second.HasMoveTarget && firstJet.AllowAirLoco && secondJet.AllowAirLoco, 1500, "Both jets reached their destinations.");

        // idle for ReturnToBaseIdleTime (45 ticks): both come home by themselves, ammunition untouched
        StepUntil(world, () => firstJet.Phase == JetAIUpdate.JetPhase.ReturningForLanding || secondJet.Phase == JetAIUpdate.JetPhase.ReturningForLanding, 120, "An idle jet starts to return after ReturnToBaseIdleTime.");
        StepUntil(world, () => !firstJet.AllowAirLoco && !secondJet.AllowAirLoco && firstJet.Phase == JetAIUpdate.JetPhase.None && secondJet.Phase == JetAIUpdate.JetPhase.None, 4000, "Both idle jets landed and parked: " + firstJet.Phase + "/" + secondJet.Phase);
        Check.That(parking.OccupantOf(0) == first.Id && parking.OccupantOf(1) == second.Id && first.Z == Fixed.Zero && second.Z == Fixed.Zero, "Each is back on its own space.");

        // a space frees up: the third jet appears
        world.Kill(second.Id);
        StepUntil(world, () => world.Entities.Count(value => value.Template.Name == "AirTestJet" && !value.EffectivelyDead) == 2 && world.Entities.Any(value => value.Template.Name == "AirTestJet" && value.Id.Value > second.Id.Value), 200, "The third jet was released as soon as a space was free.");
    }

    /// <summary>A destroyed airfield takes its parked and taxiing aircraft with it, spares those in flight; a jet that is out of ammunition with no airfield left circles and burns (OutOfAmmoDamagePerSecond).</summary>
    public static void AirDestroyedAirfield()
    {
        var (world, feeder, airfield, parking, first, _) = ParkedJet();
        Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet").Accepted, "A second jet is queued.");
        StepUntil(world, () => world.Entities.Count(value => value.Template.Name == "AirTestJet") == 2, 200, "The second jet was produced.");
        var second = world.Entities.Where(value => value.Template.Name == "AirTestJet").OrderBy(value => value.Id.Value).Last();
        StepUntil(world, () => JetOf(second).Phase == JetAIUpdate.JetPhase.None, 600, "The second jet parked.");
        var dummy = Get(world, "AirDummy");
        feeder.SetMoveTarget(1, first.Id.Value, 900, 500);
        StepUntil(world, () => JetOf(first).AllowAirLoco && !first.HasMoveTarget && first.Z == Fixed.FromInt(60), 1500, "The first jet is flying.");
        world.Kill(airfield.Id);
        for (var i = 0; i < 5; i++) world.Step();
        Check.That(second.EffectivelyDead, "The parked jet died with its airfield.");
        Check.That(!first.EffectivelyDead, "The jet in flight survived the airfield.");
        Check.That(feeder.FireWeapon(1, first.Id.Value, dummy.Id.Value).Accepted, "It fires its first missile.");
        for (var i = 0; i < 5; i++) world.Step();
        Check.That(feeder.FireWeapon(1, first.Id.Value, dummy.Id.Value).Accepted, "It fires its second missile.");
        StepUntil(world, () => JetOf(first).Phase == JetAIUpdate.JetPhase.CirclingDeadAirfield, 600, "Out of ammunition without an airfield the jet circles over where the airfield was: " + JetOf(first).Phase);
        var health = first.Body!.Health;
        for (var i = 0; i < 30; i++) world.Step();
        Check.That(first.Body.Health < health - Fixed.FromInt(30) || first.EffectivelyDead, $"It takes 30 % of its maximum health per second ({health} -> {first.Body.Health}).");
        StepUntil(world, () => first.EffectivelyDead || first.DestroyRequested, 200, "It burns out.");
    }

    /// <summary>A helicopter produced at a helipad needs no space and no runway: it rises, flies, comes back when idle and settles on the pad.</summary>
    public static void AirHelicopterUsesThePad()
    {
        var world = AirWorld(out var fixture, out var feeder);
        Check.That(fixture.Templates.TryGet("AirTestHeli", out var heli) && heli.IsInstantiable, "The helicopter binds.");
        feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
        world.Step();
        feeder.GiveMoney(1, 1000);
        world.Step();
        var airfield = Get(world, "AirTestAirfield");
        var parking = airfield.Modules.OfType<ParkingPlaceBehavior>().Single();
        Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestHeli").Accepted, "The helicopter is queued.");
        StepUntil(world, () => world.Entities.Any(value => value.Template.Name == "AirTestHeli"), 200, "The helicopter was produced.");
        var craft = Get(world, "AirTestHeli");
        var ai = JetOf(craft);
        var pad = parking.HeliPark(world);
        Check.That(craft.X == pad.X && craft.Y == pad.Y && Enumerable.Range(0, parking.SpaceCount).All(index => !parking.OccupantOf(index).IsValid), "It appears on the pad and uses no parking space.");
        var phases = new List<JetAIUpdate.JetPhase> { ai.Phase };
        StepUntil(world, () => { if (phases[^1] != ai.Phase) phases.Add(ai.Phase); return ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None && craft.Z > pad.Z; }, 300, "The helicopter lifted off: " + string.Join(">", phases));
        Check.That(phases.Contains(JetAIUpdate.JetPhase.TakingOff) && !phases.Contains(JetAIUpdate.JetPhase.TaxiToTakeoff), "A helicopter takes off without taxiing: " + string.Join(">", phases));
        Check.That(feeder.SetMoveTarget(1, craft.Id.Value, 800, 500).Accepted, "It answers a move order.");
        StepUntil(world, () => !craft.HasMoveTarget && craft.Z == Fixed.FromInt(40), 600, "It flew to the destination at its preferred height.");
        StepUntil(world, () => ai.Phase == JetAIUpdate.JetPhase.ReturningForLanding, 200, "Idle, it returns by itself.");
        StepUntil(world, () => !ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None, 1500, "It settled on the pad again: " + ai.Phase);
        Check.That(craft.X == pad.X && craft.Y == pad.Y && craft.Z == pad.Z, $"It stands on the pad again ({craft.X}, {craft.Y}, {craft.Z}).");
        Check.That(Enumerable.Range(0, parking.SpaceCount).All(index => !parking.OccupantOf(index).IsValid), "Still no space was used.");
    }
}
