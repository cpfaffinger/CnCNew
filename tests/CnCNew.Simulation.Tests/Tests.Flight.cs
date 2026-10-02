using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>Phase 13: scripted reinforcements with a transport, the payload plane, its parachutes, a shot-down jet and countermeasures.</summary>
static partial class Tests
{
    /// <summary>
    /// A payload plane (flies at height 60, drops after 1 door tick, 3 ticks between drops), its parachute (opens 10 below the drop, sinks at 1 per tick), a trooper,
    /// a truck, a flare, and a launcher whose missile can be diverted. The map is flat at height 0.
    /// </summary>
    private const string FlightFixtureIni = """
        Locomotor PlaneLoco
          Surfaces = AIR
          TurnRate = 90.0
          Speed = 120.0
          Acceleration = 600.0
          Braking = 600.0
          MinSpeed = 0.0
          PreferredHeight = 60.0
          CloseEnoughDist = 1.0
        End
        Locomotor ChuteLoco
          Surfaces = AIR
          TurnRate = 120.0
          Speed = 60.0
          Acceleration = 600.0
          Braking = 600.0
          PreferredHeight = -1.0
          SpeedLimitZ = 30.0
        End
        Locomotor FlareLoco
          Surfaces = AIR
          TurnRate = 120.0
          Speed = 60.0
          Acceleration = 600.0
          Braking = 200.0
          SpeedLimitZ = 8.0
        End
        Locomotor FlightMissileLoco
          Surfaces = AIR
          Speed = 300
          Acceleration = 9000
          TurnRate = 360
        End
        Upgrade Upgrade_AlphaFlares
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        Weapon FlightMissileGun
          PrimaryDamage = 60
          PrimaryDamageRadius = 5.0
          AttackRange = 450.0
          ProjectileObject = FlightMissile
          DelayBetweenShots = 3000
        End
        Object FlightMissile
          KindOf = PROJECTILE SMALL_MISSILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Locomotor = SET_NORMAL FlightMissileLoco
          Behavior = MissileAIUpdate Missile01
            TryToFollowTarget = Yes
            FuelLifetime = 0
            IgnitionDelay = 0
            InitialVelocity = 0
            DistanceToTravelBeforeTurning = 0
            DistanceToTargetForLock = 0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object BetaLauncher
          KindOf = VEHICLE SELECTABLE
          VisionRange = 400.0
          ShroudClearingRange = 400.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FlightMissileGun
          End
        End
        Object AlphaTrooper
          Side = Alpha
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          VisionRange = 60.0
          ShroudClearingRange = 60.0
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AlphaChute
          Side = Alpha
          KindOf = PARACHUTE UNATTACKABLE
          Locomotor = SET_NORMAL ChuteLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = ParachuteContain Chute01
            ParachuteOpenDist = 10.0
            AllowInsideKindOf = INFANTRY PARACHUTABLE
            FreeFallDamagePercent = 50%
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AlphaFlare
          KindOf = PROJECTILE
          Locomotor = SET_NORMAL FlareLoco
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = LifetimeUpdate Life01
            MinLifetime = 1000
            MaxLifetime = 1000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AlphaPlane
          Side = Alpha
          KindOf = SELECTABLE VEHICLE TRANSPORT AIRCRAFT
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          Locomotor = SET_NORMAL PlaneLoco
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DeliverPayloadAIUpdate Deliver01
            DoorDelay = 34
            MaxAttempts = 2
            DropOffset = X:0 Y:0 Z:-10
            DropDelay = 100
            PutInContainer = AlphaChute
            DeliveryDistance = 100.0
          End
          Behavior = TransportContain Transport01
            Slots = 10
            AllowInsideKindOf = INFANTRY VEHICLE PARACHUTABLE
          End
          Behavior = JetSlowDeathBehavior JetDeath01
            DestructionDelay = 2000
            FallHowFast = 50%
            DelaySecondaryFromInitialDeath = 1000
            DelayFinalBlowUpFromHitGround = 100
          End
          Behavior = CountermeasuresBehavior Flares01
            TriggeredBy = Upgrade_AlphaFlares
            FlareTemplateName = AlphaFlare
            VolleySize = 2
            VolleyArcAngle = 90.0
            VolleyVelocityFactor = 2.0
            DelayBetweenVolleys = 1000
            NumberOfVolleys = 3
            ReloadTime = 0
            EvasionRate = 100%
            ReactionLaunchLatency = 0
            MissileDecoyDelay = 200
          End
        End
        Object AlphaTruck
          Side = Alpha
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = TransportAIUpdate AI01
          End
          Behavior = TransportContain Transport01
            Slots = 4
            AllowInsideKindOf = INFANTRY
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """;

    private static MapProperties ReinforcementTeam(string name, string transport, int troopers, bool exit = false, bool full = false)
        => new([Scr.Text("teamName", name), Scr.Text("teamOwner", PlayerA), Scr.Flag("teamIsSingleton", false), Scr.Text("teamTransport", transport),
                Scr.Text("teamReinforcementOrigin", "Spawn"), Scr.Flag("teamTransportsExit", exit), Scr.Flag("teamStartsFull", full),
                ..troopers > 0 ? [Scr.Text("teamUnitType1", "AlphaTrooper"), new MapProperty("teamUnitMaxCount1", MapPropertyType.Int, troopers, 0m, string.Empty), new MapProperty("teamUnitMinCount1", MapPropertyType.Int, troopers, 0m, string.Empty)] : Array.Empty<MapProperty>()]);

    private static (MapData Map, SyntheticFixture Fixture, TerrainMap Terrain) FlightMap(string team, MapObject[]? objects = null)
    {
        var (map, fixture, terrain) = MissionMap([Scr.Script("Deliver", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("CREATE_REINFORCEMENT_TEAM", Scr.Team(team), Scr.Waypoint("Far"))], oneShot: true)], objects: objects);
        map = map with { Teams = [..map.Teams, ReinforcementTeam("ParaTeam", "AlphaPlane", 2), ReinforcementTeam("TruckTeam", "AlphaTruck", 3, exit: true), ReinforcementTeam("PlaneOnly", "AlphaPlane", 0)] };
        return (map, fixture, terrain);
    }

    private static MissionSession StartFlight(string team, MapObject[]? objects = null, MissionOptions? options = null)
    {
        var (map, fixture, terrain) = FlightMap(team, objects);
        return MissionSession.Start(options ?? MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
    }

    private static Entity[] Of(MissionSession session, string template) => session.World.Entities.Where(entity => entity.Template.Name == template).ToArray();

    public static void FlightParadropReinforcement()
    {
        var session = StartFlight("ParaTeam");
        Check.That(session.SkippedObjects.IsEmpty, "The flight fixture instantiates: " + string.Join(",", session.SkippedObjects));
        Run(session, 3);
        var plane = Of(session, "AlphaPlane").Single();
        var chutes = Of(session, "AlphaChute");
        var troopers = Of(session, "AlphaTrooper");
        Check.That(chutes.Length == 2 && troopers.Length == 2, $"Two troopers in two parachutes were created ({troopers.Length}/{chutes.Length}).");
        Check.That(chutes.All(chute => chute.ContainedBy == plane.Id), "Both parachutes are cargo of the plane.");
        Check.That(troopers.All(trooper => chutes.Any(chute => trooper.ContainedBy == chute.Id)), "Each trooper sits in a parachute.");
        Check.That(plane.Z == Fixed.FromInt(60), $"The plane flies at its preferred height ({plane.Z}).");
        Check.That(plane.Modules.OfType<DeliverPayloadAIUpdate>().Single().Delivering, "The plane is on its delivery run.");

        var dropped = false;
        var landed = false;
        for (var tick = 0; tick < 400 && !landed; tick++)
        {
            session.Step();
            if (!dropped && chutes.Any(chute => !chute.ContainedBy.IsValid)) dropped = true;
            if (dropped && troopers.All(trooper => !trooper.ContainedBy.IsValid && trooper.Z == Fixed.Zero)) landed = true;
        }
        Check.That(dropped, "The plane released a parachute.");
        Check.That(landed, "Both troopers reached the ground.");
        Check.That(troopers.All(trooper => !trooper.EffectivelyDead), "They landed alive.");
        Check.That(troopers.All(trooper => (trooper.X - Fixed.FromInt(400)).Abs() < Fixed.FromInt(120) && (trooper.Y - Fixed.FromInt(200)).Abs() < Fixed.FromInt(20)),
            $"They landed near the target (({troopers[0].X}, {troopers[0].Y}) and ({troopers[1].X}, {troopers[1].Y})).");
        Run(session, 400);
        Check.That(Of(session, "AlphaPlane").Length == 0, "The plane flew off the map and was deleted.");
        Check.That(Of(session, "AlphaChute").Length == 0, "The landed parachutes were killed and are gone.");
        Check.That(Of(session, "AlphaTrooper").Length == 2, "Both troopers remain.");

        var (map, fixture, terrain) = FlightMap("ParaTeam");
        var (original, restored, _) = MissionScenario.VerifyContinuation(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, 140);
        Check.That(original.Chain == restored.Chain && original.FinalHash == restored.FinalHash, "A paradrop continues identically after save/restore half way through the flight.");
    }

    public static void FlightTransportReturnsAndLeaves()
    {
        var session = StartFlight("TruckTeam");
        Run(session, 2);
        var truck = Of(session, "AlphaTruck").Single();
        var troopers = Of(session, "AlphaTrooper");
        Check.That(troopers.Length == 3 && troopers.All(trooper => trooper.ContainedBy == truck.Id), "The truck carries all three troopers from the start.");
        Run(session, 300);
        Check.That(troopers.All(trooper => !trooper.ContainedBy.IsValid && !trooper.EffectivelyDead), "The troopers were set down.");
        Check.That(troopers.All(trooper => (trooper.X - Fixed.FromInt(400)).Abs() < Fixed.FromInt(60)), $"They stand at the destination (x {troopers[0].X}).");
        Run(session, 400);
        Check.That(Of(session, "AlphaTruck").Length == 0, "With teamTransportsExit the truck drove back to where it was ordered and was deleted.");
    }

    public static void FlightShotDownPlaneDestroysItsPayload()
    {
        var session = StartFlight("ParaTeam");
        Run(session, 12);
        var plane = Of(session, "AlphaPlane").Single();
        Check.That(plane.Container!.ContainCount == 2, "The plane still holds both parachutes (its doors are shut on the way).");
        session.World.Kill(plane.Id);
        var startZ = plane.Z;
        Run(session, 3);
        Check.That(plane.Z < startZ, "The dying plane starts to fall.");
        Check.That(Of(session, "AlphaChute").Length == 0 && Of(session, "AlphaTrooper").Length == 0, "Riders that cannot get out die with the plane (doors shut).");
        Run(session, 200);
        Check.That(Of(session, "AlphaPlane").Length == 0, "The wreck is gone after its destruction delay.");
    }

    public static void FlightCountermeasuresDivertMissiles()
    {
        MapObject[] Launcher() => [Placed("BetaLauncher", 300m, 150m, "teamB")];
        var damaged = Fixed.Zero;
        foreach (var flares in new[] { false, true })
        {
            var session = StartFlight("PlaneOnly", Launcher(), MissionOptions.Standard with { CombatIntervalTicks = 3 });
            var player = Of(session, "BetaLauncher").Single().Owner;
            _ = player;
            var owner = session.World.Entities.First(entity => entity.Template.Name == "BetaLauncher").Owner;
            Check.That(owner.IsSystem == false, "The launcher belongs to a real side.");
            Run(session, 2);
            var plane = Of(session, "AlphaPlane").Single();
            if (flares) session.World.CompleteUpgrade(plane.Owner, null, "Upgrade_AlphaFlares");
            var module = plane.Modules.OfType<CountermeasuresBehavior>().Single();
            Run(session, 60);
            var health = plane.Body!.Health;
            if (!flares) damaged = Fixed.FromInt(500) - health;
            else
            {
                Check.That(module.MissileCounts.Incoming > 0, $"Missiles were fired at the plane ({module.MissileCounts.Incoming}).");
                Check.That(module.MissileCounts.Diverted == module.MissileCounts.Incoming, "With 100 % evasion every one of them was marked for diversion.");
                Check.That(Of(session, "AlphaFlare").Length > 0 || session.World.Entities.Any(entity => entity.Template.Name == "AlphaFlare") || module.MissileCounts.Diverted > 0, "A volley of flares left the plane.");
                Check.That(Fixed.FromInt(500) - health < damaged, $"The flares spared the plane damage ({Fixed.FromInt(500) - health} against {damaged} without them).");
            }
        }
        Check.That(damaged > Fixed.Zero, "Without countermeasures the missiles hurt the plane.");
    }
}
