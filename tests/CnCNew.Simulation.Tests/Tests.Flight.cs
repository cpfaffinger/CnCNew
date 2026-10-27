using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
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
        CommandButton Cmd_AlphaFlaresUpgrade
          Command = PLAYER_UPGRADE
          Upgrade = Upgrade_AlphaFlares
        End
        CommandButton Cmd_FireUplink
          Command = SPECIAL_POWER
          SpecialPower = UplinkPower
        End
        Weapon FlightMissileGun
          PrimaryDamage = 60
          PrimaryDamageRadius = 5.0
          AttackRange = 450.0
          ProjectileObject = FlightMissile
          AntiAirborneVehicle = Yes
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
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
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
            DestroyRidersWhoAreNotFreeToExit = Yes
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
        SpecialPower UplinkPower
          ReloadTime = 0
        End
        Object UplinkBeam
          KindOf = IMMOBILE
          Draw = W3DLaserDraw ModuleTag_01
            OuterBeamWidth = 40.0
            InnerBeamWidth = 4.0
          End
        End
        Object BetaCannon
          KindOf = SELECTABLE STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = UplinkPower
            UpdateModuleStartsAttack = Yes
          End
          Behavior = ParticleUplinkCannonUpdate Beam01
            SpecialPowerTemplate = UplinkPower
            WidthGrowTime = 300
            BeamTravelTime = 200
            TotalFiringTime = 600
            TotalDamagePulses = 6
            DamagePerSecond = 100.0
            DamageType = PARTICLE_BEAM
            DeathType = NORMAL
            DamageRadiusScalar = 2.0
            ParticleBeamLaserName = UplinkBeam
            SwathOfDeathDistance = 0.0
            SwathOfDeathAmplitude = 0.0
            ManualDrivingSpeed = 60.0
            ManualFastDrivingSpeed = 600.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AlphaBus
          Side = Alpha
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Locomotor = SET_NORMAL SkirmishLoco
          Body = UndeadBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
            SecondLifeMaxHealth = 300.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 40.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = TransportContain Transport01
            Slots = 4
            AllowInsideKindOf = INFANTRY
          End
          Behavior = BattleBusSlowDeathBehavior Bus01
            DeathTypes = ALL -CRUSHED -SPLATTED -EXTRA_4
            ProbabilityModifier = 5
            ThrowForce = 100.0
            PercentDamageToPassengers = 50%
            EmptyHulkDestructionDelay = 1000
            DestructionDelay = 500
            FXStartUndeath = FX_Test
            FXHitGround = FX_Other
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
        Weapon AlphaBombWeapon
          PrimaryDamage = 80
          PrimaryDamageRadius = 30.0
          AttackRange = 1.0
          DelayBetweenShots = 1000
        End
        SpecialPower StrikePower
          ReloadTime = 0
        End
        Object AlphaBomb
          KindOf = PROJECTILE
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 75.0
          End
          Behavior = FireWeaponWhenDeadBehavior Fire01
            DeathWeapon = AlphaBombWeapon
            StartsActive = Yes
          End
          Behavior = HeightDieUpdate Height01
            TargetHeight = 1.0
            TargetHeightIncludesStructures = No
          End
          Behavior = SpecialPowerCompletionDie Completion01
            SpecialPowerTemplate = StrikePower
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AlphaBomber
          Side = Alpha
          KindOf = SELECTABLE VEHICLE TRANSPORT AIRCRAFT
          Locomotor = SET_NORMAL PlaneLoco
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DeliverPayloadAIUpdate Deliver01
            MaxAttempts = 1
            DeliveryDistance = 80.0
          End
          Behavior = TransportContain Transport01
            Slots = 10
            AllowInsideKindOf = PROJECTILE
          End
          Behavior = DestroyDie Destroy01
          End
        End
        ObjectCreationList OCL_AlphaStrike
          DeliverPayload
            Transport = AlphaBomber
            StartAtPreferredHeight = Yes
            StartAtMaxSpeed = Yes
            FormationSize = 2
            FormationSpacing = 40.0
            MaxAttempts = 1
            DropOffset = X:0 Y:0 Z:-2
            DropVariance = X:6 Y:6 Z:0
            DropDelay = 100
            Payload = AlphaBomb 3
            DeliveryDistance = 40.0
          End
        End
        Object AlphaCommander
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = OCLSpecialPower Power01
            SpecialPowerTemplate = StrikePower
            OCL = OCL_AlphaStrike
            CreateLocation = CREATE_AT_EDGE_FARTHEST_FROM_TARGET
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object AlphaDozer
          Side = Alpha
          KindOf = SELECTABLE VEHICLE DOZER
          VisionRange = 400.0
          ShroudClearingRange = 400.0
          Locomotor = SET_NORMAL SkirmishLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
          End
        End
        Object AlphaDepot
          Side = Alpha
          KindOf = SELECTABLE STRUCTURE CASH_GENERATOR
          BuildCost = 100
          BuildTime = 2
          Geometry = BOX
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object ScriptTrap
          KindOf = BOOBY_TRAP NO_COLLIDE MINE
          Body = HighlanderBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = StickyBombUpdate Sticky01
          End
        End
        Object ScriptBridge
          KindOf = STRUCTURE IMMOBILE BRIDGE
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 6.0
          GeometryHeight = 5.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = BridgeBehavior Bridge01
          End
          Behavior = KeepObjectDie Keep01
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
        Run(session, session.Frames(3));
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
        for (var tick = 0; tick < session.Frames(400) && !landed; tick++)
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
        Run(session, session.Frames(400));
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
        Run(session, session.Frames(2));
        var truck = Of(session, "AlphaTruck").Single();
        var troopers = Of(session, "AlphaTrooper");
        Check.That(troopers.Length == 3 && troopers.All(trooper => trooper.ContainedBy == truck.Id), "The truck carries all three troopers from the start.");
        Run(session, session.Frames(300));
        Check.That(troopers.All(trooper => !trooper.ContainedBy.IsValid && !trooper.EffectivelyDead), "The troopers were set down.");
        Check.That(troopers.All(trooper => (trooper.X - Fixed.FromInt(400)).Abs() < Fixed.FromInt(60)), $"They stand at the destination (x {troopers[0].X}).");
        Run(session, session.Frames(400));
        Check.That(Of(session, "AlphaTruck").Length == 0, "With teamTransportsExit the truck drove back to where it was ordered and was deleted.");
    }

    public static void FlightShotDownPlaneDestroysItsPayload()
    {
        var session = StartFlight("ParaTeam");
        Run(session, session.Frames(12));
        var plane = Of(session, "AlphaPlane").Single();
        Check.That(plane.Container!.ContainCount == 2, "The plane still holds both parachutes (its doors are shut on the way).");
        session.World.Kill(plane.Id);
        var startZ = plane.Z;
        Run(session, session.Frames(3));
        Check.That(plane.Z < startZ, "The dying plane starts to fall.");
        Check.That(Of(session, "AlphaChute").Length == 0 && Of(session, "AlphaTrooper").Length == 0, "Riders that cannot get out die with the plane (doors shut).");
        Run(session, session.Frames(200));
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
            Run(session, session.Frames(2));
            var plane = Of(session, "AlphaPlane").Single();
            if (flares) session.World.CompleteUpgrade(plane.Owner, null, "Upgrade_AlphaFlares");
            var module = plane.Modules.OfType<CountermeasuresBehavior>().Single();
            Run(session, session.Frames(60));
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

    public static void FlightParticleUplinkCannonBurnsItsTarget()
    {
        MapObject[] Objects() => [Placed("BetaCannon", 100m, 200m, "teamB", "Cannon"), Placed("AlphaTrooper", 300m, 200m, "teamA", "AtTarget"), Placed("AlphaTrooper", 300m, 225m, "teamA", "Near"), Placed("AlphaTrooper", 300m, 320m, "teamA", "Far")];
        var (map, fixture, terrain) = MissionMap([Scr.Script("Idle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NO_OP")], oneShot: true)], objects: Objects());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(session.SkippedObjects.IsEmpty, "The cannon and its beam instantiate: " + string.Join(",", session.SkippedObjects));
        Run(session, session.Frames(3));
        var cannon = NamedIn(session, "Cannon")!;
        var module = cannon.Modules.OfType<ParticleUplinkCannonUpdate>().Single();
        var owner = cannon.Owner;
        Check.That(session.Issue(owner, (sequence, tick) => new UseSpecialPowerCommand(owner, sequence, tick, cannon.Id, "UplinkPower", SpecialPowerTargetKind.Location, EntityId.Invalid, Fixed.FromInt(300), Fixed.FromInt(200))).Accepted, "The cannon accepts the order.");
        Run(session, 1);
        Check.That(module.InUse, "The order is running.");
        var fired = session.World.Tick;
        Run(session, session.Frames(4));
        Check.That(NamedIn(session, "AtTarget")!.Body!.Health == Fixed.FromInt(100), "The beam has not arrived yet (BeamTravelTime 6 ticks).");
        Run(session, session.Frames(8));
        Check.That(NamedIn(session, "AtTarget")!.Body!.Health < Fixed.FromInt(100), "The growing beam burns what stands at the aim point.");
        var mid = MissionSnapshot.Write(session);
        var restored = MissionSnapshot.Restore(mid, fixture.Templates, fixture.Fingerprint, terrain, map);
        for (var tick = 0; tick < session.Frames(60); tick++)
        {
            var left = session.Step().StateHash;
            var right = restored.Step().StateHash;
            Check.That(left == right, $"A beam restored from a snapshot burns identically (tick {tick}).");
        }
        Check.That(!module.InUse, "The beam has decayed and the use is over.");
        // 18 ticks of beam at 100/s in 6 pulses of 10 (the reference also pulses once more at the very end of the decay).
        Check.That(NamedIn(session, "AtTarget")!.Body!.Health <= Fixed.FromInt(50), $"The aim point took most of the pulses (health {NamedIn(session, "AtTarget")!.Body!.Health}).");
        Check.That(NamedIn(session, "Near")!.Body!.Health > NamedIn(session, "AtTarget")!.Body!.Health && NamedIn(session, "Near")!.Body!.Health < Fixed.FromInt(100), "An object at the edge of the grown beam got only the later pulses.");
        Check.That(NamedIn(session, "Far")!.Body!.Health == Fixed.FromInt(100), "An object outside the beam's radius is untouched.");
        _ = fired;
    }

    public static void FlightBattleBusHasTwoLives()
    {
        var (map, fixture, terrain) = MissionMap([Scr.Script("Idle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NO_OP")], oneShot: true)],
            objects: [Placed("AlphaBus", 300m, 200m, "teamA", "Bus"), Placed("AlphaTrooper", 300m, 200m, "teamA", "Rider"), Placed("AlphaBus", 300m, 300m, "teamA", "Other")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(session.SkippedObjects.IsEmpty, "The bus instantiates: " + string.Join(",", session.SkippedObjects));
        Run(session, session.Frames(3));
        var bus = NamedIn(session, "Bus")!;
        var rider = NamedIn(session, "Rider")!;
        Check.That(session.World.TryEnterContainer(rider, bus), "The trooper boards the bus.");
        var body = bus.Body as UndeadBody ?? throw new InvalidOperationException("The bus has an UndeadBody.");

        // First life: a lethal hit leaves one hit point, then the second life starts with the new maximum and the throw.
        session.World.Damage(bus.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(500), DamageTypes.Explosion, DeathTypes.Normal, false));
        Check.That(!bus.EffectivelyDead && body.IsSecondLife, "The first lethal hit is not a death: the second life began.");
        Check.That(body.MaxHealth == Fixed.FromInt(300) && body.Health == Fixed.FromInt(300), $"The second life is fully healed to 300 ({body.Health}/{body.MaxHealth}).");
        Check.That(rider.Body!.Health == Fixed.FromInt(50), $"The passenger took 50 % of its maximum health ({rider.Body!.Health}).");
        var peak = Fixed.Zero;
        long landed = -1;
        for (var tick = 0; tick < session.Frames(200) && landed < 0; tick++)
        {
            session.Step();
            peak = Fixed.Max(peak, bus.Z);
            if (tick > session.Frames(12) && bus.Z == Fixed.Zero) landed = tick;
        }
        Check.That(peak > Fixed.FromInt(10), $"The bus was thrown into the air (peak {peak}).");
        Check.That(landed > 0, "It came down again.");
        Check.That(!bus.EffectivelyDead, "It survived the throw.");
        // F-53: the landed hulk is held (BattleBusSlowDeathBehavior::update, setDisabled(DISABLED_HELD)); an order to move leaves it where it fell. The landing is decided at the end
        // of a logic frame of the original, which above 30 Hz may be a tick after the bus first touched the ground.
        Run(session, session.Frames(1));
        Check.That((bus.ScriptDisableMask & Entity.ScriptHeld) != 0, "The landed hulk is held.");
        var (fellX, fellY) = (bus.X, bus.Y);
        bus.Modules.OfType<AIUpdateInterface>().First().OrderMove(session.World, bus.X + Fixed.FromInt(80), bus.Y, true);
        Run(session, session.Frames(60));
        Check.That(bus.X == fellX && bus.Y == fellY, $"A held hulk does not drive off ({bus.X}, {bus.Y}).");

        // Second life: a normal death.
        session.World.Damage(bus.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(1000), DamageTypes.Explosion, DeathTypes.Normal, false));
        Check.That(bus.EffectivelyDead, "The second lethal hit kills it.");
        Run(session, session.Frames(40));
        Check.That(Of(session, "AlphaBus").All(entity => entity.Id != bus.Id), "The real death ends in destruction after its delay.");

        // Unresistable damage ignores the first life.
        var other = NamedIn(session, "Other")!;
        session.World.Damage(other.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(500), DamageTypes.Unresistable, DeathTypes.Normal, false));
        Check.That(other.EffectivelyDead, "Unresistable damage kills a bus on its first life.");
    }

    public static void FlightUpgradeButtonReadiness()
    {
        // SKIRMISH_COMMAND_BUTTON_READY for an upgrade button: ready while an upgrade module of the object would react and the object does not have the upgrade yet.
        var ready = Scr.Script("Ready", [Scr.And(Scr.If("SKIRMISH_COMMAND_BUTTON_READY_ALL", Scr.Side(PlayerA), Scr.Team("PlaneOnly"), Scr.Ability("Cmd_AlphaFlaresUpgrade")))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("ready"))]);
        var (map, fixture, terrain) = MissionMap([Scr.Script("Deliver", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("CREATE_REINFORCEMENT_TEAM", Scr.Team("PlaneOnly"), Scr.Waypoint("Far"))], oneShot: true), ready]);
        map = map with { Teams = [..map.Teams, ReinforcementTeam("PlaneOnly", "AlphaPlane", 0)] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(20));
        var before = session.Engine.CounterValue("ready");
        Check.That(before > 0, "The flares upgrade is available to a plane that does not have it.");
        session.World.CompleteUpgrade(Of(session, "AlphaPlane").Single().Owner, null, "Upgrade_AlphaFlares");
        Run(session, session.Frames(2));
        var after = session.Engine.CounterValue("ready");
        Run(session, session.Frames(20));
        Check.That(session.Engine.CounterValue("ready") == after, "Once the upgrade is complete the button is no longer ready.");
        Check.That(Of(session, "AlphaPlane").Single().Modules.OfType<CountermeasuresBehavior>().Single().IsActive, "The upgrade took effect.");
    }

    public static void FlightTeamCreationRunsStartActions()
    {
        var go = Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("CREATE_REINFORCEMENT_TEAM", Scr.Team("Starter"), Scr.Waypoint("Far")), Scr.Do("CREATE_REINFORCEMENT_TEAM", Scr.Team("Quiet"), Scr.Waypoint("Far"))], oneShot: true);
        var start = Scr.Script("StartScript", [Scr.And(Scr.If("CONDITION_FALSE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter("started"))], active: false);
        var (map, fixture, terrain) = MissionMap([go, start]);
        MapProperties Team(string name, bool executes) => new([Scr.Text("teamName", name), Scr.Text("teamOwner", PlayerA), Scr.Flag("teamIsSingleton", true), Scr.Text("teamProductionCondition", "StartScript"), Scr.Flag("teamExecutesActionsOnCreate", executes),
            Scr.Text("teamUnitType1", "AlphaHunter"), new MapProperty("teamUnitMinCount1", MapPropertyType.Int, 1, 0m, string.Empty), new MapProperty("teamUnitMaxCount1", MapPropertyType.Int, 1, 0m, string.Empty)]);
        map = map with { Teams = [..map.Teams, Team("Starter", true), Team("Quiet", false)] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(10));
        Check.That(session.Engine.CounterValue("started") == 1, $"Creating the team with teamExecutesActionsOnCreate ran the true-actions of its production-condition script once; the team without the flag ran nothing ({session.Engine.CounterValue("started")}); the script itself never fires (its condition is false).");
    }

    public static void FlightStrikePowerDeliversBombs()
    {
        // An OCL special power at the farthest edge: two bombers fly in, drop three bombs each on the target, the bombs fall and kill what stands there, and the power reports its completion.
        MapScript Notice() => Scr.Script("Notice", [Scr.And(Scr.If("PLAYER_COMPLETED_SPECIAL_POWER", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.SpecialPower, "StrikePower")))], [Scr.Do("SET_FLAG", Scr.Flag("done"), Scr.Bool(true))], oneShot: true);
        var (map, fixture, terrain) = MissionMap([Notice()], objects: [Placed("AlphaCommander", 100m, 200m, "teamA", "Commander"), Placed("BetaSoldier", 400m, 200m, "teamB", "Victim"), Placed("BetaSoldier", 400m, 300m, "teamB", "Bystander")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(session.SkippedObjects.IsEmpty, "The strike fixture instantiates: " + string.Join(",", session.SkippedObjects));
        Run(session, session.Frames(3));
        var commander = NamedIn(session, "Commander")!;
        var owner = commander.Owner;
        Check.That(session.Issue(owner, (sequence, tick) => new UseSpecialPowerCommand(owner, sequence, tick, commander.Id, "StrikePower", SpecialPowerTargetKind.Location, EntityId.Invalid, Fixed.FromInt(400), Fixed.FromInt(200))).Accepted, "The power is used.");
        Run(session, session.Frames(3));
        var bombers = Of(session, "AlphaBomber");
        Check.That(bombers.Length == 2, $"The formation has two bombers ({bombers.Length}).");
        Check.That(Of(session, "AlphaBomb").Length == 6 && Of(session, "AlphaBomb").All(bomb => bomb.ContainedBy.IsValid), "Each bomber carries its three bombs.");
        Check.That(bombers.All(bomber => bomber.Z == Fixed.FromInt(60) && bomber.X < Fixed.FromInt(100) && bomber.Y < Fixed.FromInt(100)), $"They start at the far corner at their preferred height ({bombers[0].X},{bombers[0].Y},{bombers[0].Z}).");
        var victim = NamedIn(session, "Victim")!;
        var bystander = NamedIn(session, "Bystander")!;
        byte[]? mid = null;
        var seen = new SortedDictionary<string, int>();
        var lastBombs = new SortedDictionary<uint, string>();
        for (var tick = 0; tick < session.Frames(900) && Of(session, "AlphaBomber").Length > 0; tick++)
        {
            foreach (var bomb in Of(session, "AlphaBomb")) lastBombs[bomb.Id.Value] = $"{bomb.X}/{bomb.Y}/{bomb.Z}";
            foreach (var item in session.Step().Events) seen[item.Kind + ":" + item.Text] = seen.GetValueOrDefault(item.Kind + ":" + item.Text) + 1;
            if (tick == session.Frames(60)) mid = MissionSnapshot.Write(session);
        }
        Check.That(victim.EffectivelyDead || victim.Body!.Health < victim.Body.MaxHealth, $"The bombs hurt what stood at the target (victim health {victim.Body!.Health}/{victim.Body.MaxHealth}, bystander {bystander.Body!.Health}, bombs left {Of(session, "AlphaBomb").Length}, seen {string.Join(";", seen.Select(pair => pair.Key + "=" + pair.Value))}, bombs {string.Join(" ", lastBombs.Values)}, tick {session.World.Tick}, events: {string.Join(",", session.World.Entities.Where(entity => entity.Template.Name == "AlphaBomb").Select(entity => $"{entity.X}/{entity.Y}/{entity.Z}"))}).");
        Check.That(!bystander.EffectivelyDead && bystander.Body!.Health == bystander.Body.MaxHealth, "A bystander outside the bomb radius is untouched.");
        Check.That(Of(session, "AlphaBomber").Length == 0, "The bombers flew off the map and were deleted.");
        Check.That(session.Engine.FlagValue("done"), "SpecialPowerCompletionDie told the script engine (PLAYER_COMPLETED_SPECIAL_POWER).");
        var restored = MissionSnapshot.Restore(mid!, fixture.Templates, fixture.Fingerprint, terrain, map);
        for (var tick = 0; tick < restored.Frames(700); tick++) restored.Step();
        Check.That(restored.World.Entities.Count(entity => entity.Template.Name == "AlphaBomber") == 0 && restored.Engine.FlagValue("done"), "A delivery restored from a snapshot during the run ends the same way.");
    }

    public static void FlightUplinkFollowsAScriptedWaypointPath()
    {
        var (map, fixture, terrain) = MissionMap([Scr.Script("Fire", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_USE_COMMANDBUTTON_ABILITY_USING_WAYPOINT_PATH", Scr.Unit("Cannon"), Scr.Ability("Cmd_FireUplink"), Scr.Named(MapScriptParameterType.WaypointPath, "Route"))], oneShot: true)],
            objects: [Placed("BetaCannon", 100m, 200m, "teamB", "Cannon"), Placed("AlphaTrooper", 130m, 200m, "teamA", "AtStart"), Placed("AlphaTrooper", 330m, 200m, "teamA", "AtEnd"), Placed("AlphaTrooper", 220m, 360m, "teamA", "Off")],
            paths: [("Route", [("A", 130m, 200m), ("B", 230m, 200m), ("C", 330m, 200m)])]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(session.SkippedObjects.IsEmpty, "The cannon instantiates: " + string.Join(",", session.SkippedObjects));
        var module = NamedIn(session, "Cannon")!.Modules.OfType<ParticleUplinkCannonUpdate>().Single();
        var positions = new List<(Fixed X, Fixed Y)>();
        for (var tick = 0; tick < session.Frames(80); tick++)
        {
            session.Step();
            if (module.InUse) positions.Add((module.BeamPosition.X, module.BeamPosition.Y));
        }
        Check.That(positions.Count > 10, $"The scripted use runs ({positions.Count} ticks).");
        Check.That(positions.Any(point => (point.X - Fixed.FromInt(230)).Abs() < Fixed.FromInt(30)) && positions.Any(point => point.X >= Fixed.FromInt(325)), "The beam drives along the waypoints to the end of the path.");
        Check.That(positions.All(point => (point.Y - Fixed.FromInt(200)).Abs() < Fixed.FromInt(1)), "It stays on the path line.");
        Check.That(NamedIn(session, "Off")!.Body!.Health == Fixed.FromInt(100), "An object off the path is untouched.");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        for (var tick = 0; tick < session.Frames(60); tick++) Check.That(session.Step().StateHash == restored.Step().StateHash, $"A path-driven beam restored from a snapshot continues identically (tick {tick}).");
    }
}
