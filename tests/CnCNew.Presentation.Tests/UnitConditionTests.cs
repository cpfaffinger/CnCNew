using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The model condition bits of units, weapons, movement and death (docs/MODEL-CONDITIONS-UNITS.md): continuous fire, the lean of bikes, the turret turning, jet exhaust and afterburner, parachutes,
/// toppling, the bus' second life, climbing, over water, crushing, splatting and cheering. Synthetic fixtures plus, with <c>--content-dir</c>, retail objects.
/// </summary>
internal static class UnitConditionTests
{
    public static int Run(string[] args)
    {
        var t = new TestHarness("UnitConditions");
        t.Run("continuous fire stages follow the shots at one victim and cool down", ContinuousFire);
        t.Run("a turning bike leans: CENTER_TO_x, x_TO_CENTER", TurnLean);
        t.Run("TURRET_ROTATE while a turret turns toward its aim", TurretRotate);
        t.Run("jet exhaust and afterburner follow the take-off states", JetBits);
        t.Run("parachute and free fall of the chute and its rider", Parachute);
        t.Run("TOPPLED once an object was pushed over", Toppled);
        t.Run("SECOND_LIFE after the bus landed", SecondLife);
        t.Run("CLIMBING on a cliff cell, OVER_WATER of a hover locomotor", ClimbingAndWater);
        t.Run("crushed ends and splatting from the death event", CrushAndSplat);
        t.Run("SPECIAL_CHEERING lasts three seconds", Cheering);
        t.Run("FREEFALL of a sliding avalanche rock", TensileFreefall);
        t.Run("the names the presentation sets are exactly the implemented ones", ProducedNames);
        var index = Array.IndexOf(args, "--content-dir");
        if (index >= 0 && index + 1 < args.Length)
        {
            var directory = args[index + 1];
            t.Run("real data: gattling tank continuous fire", () => RealGattling(directory));
            t.Run("real data: combat bike topples, leans and the car is crushed", () => RealBikeAndCar(directory));
            t.Run("real data: jets and the second life of the battle bus", () => RealJetsAndBus(directory));
        }
        else Console.WriteLine("SKIP UnitConditions real-data tests (pass --content-dir <dir>)");
        return t.Failures;
    }

    // ------------------------------------------------------------------------------------------------ fixture

    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/UnitConditions.ini"] = """
        Locomotor UnitBikeLoco
          Surfaces = GROUND CLIFF
          Speed = 30.0
          Acceleration = 600.0
          Braking = 600.0
          TurnRate = 360.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Locomotor UnitHoverLoco
          Surfaces = GROUND WATER
          Appearance = HOVER
          Speed = 30.0
          Acceleration = 600.0
          Braking = 600.0
          TurnRate = 360.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Locomotor UnitJetLoco
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
        Locomotor UnitTaxiLoco
          Surfaces = GROUND
          Speed = 60.0
          Acceleration = 300.0
          Braking = 999999.0
          TurnRate = 360.0
        End
        Locomotor UnitChuteLoco
          Surfaces = AIR
          TurnRate = 120.0
          Speed = 60.0
          Acceleration = 600.0
          Braking = 600.0
          PreferredHeight = -1.0
          SpeedLimitZ = 30.0
        End
        Weapon UnitGatlingGun
          PrimaryDamage = 1
          AttackRange = 200.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 100
          ContinuousFireOne = 2
          ContinuousFireTwo = 4
          ContinuousFireCoast = 1000
        End
        Weapon UnitPlainGun
          PrimaryDamage = 1
          AttackRange = 200.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 100
        End

        Object UnitGatling
          KindOf = SELECTABLE VEHICLE CAN_ATTACK
          VisionRange = 100.0
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY UnitGatlingGun
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object UnitPlainGunner
          KindOf = SELECTABLE VEHICLE CAN_ATTACK
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = AIUpdateInterface AI01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY UnitPlainGun
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object UnitTarget
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object UnitBike
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL UnitBikeLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = AnimationSteeringUpdate Steer01
            MinTransitionTime = 300
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object UnitHover
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL UnitHoverLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object UnitCar
          KindOf = SELECTABLE VEHICLE
          Geometry = BOX
          GeometryMajorRadius = 11.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 7.0
          GeometryIsSmall = Yes
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = CrushDie Crush01
            DeathTypes = NONE +CRUSHED +SPLATTED
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object UnitTree
          KindOf = SHRUBBERY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = ToppleUpdate Topple01
            KillWhenFinishedToppling = No
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object UnitAirfield
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
        Object UnitJet
          KindOf = SELECTABLE AIRCRAFT CAN_ATTACK VEHICLE
          VisionRange = 600.0
          BuildCost = 100
          BuildTime = 1
          Locomotor = SET_NORMAL UnitJetLoco
          Locomotor = SET_TAXIING UnitTaxiLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = JetAIUpdate Jet01
            TakeoffPause = 300
            ReturnToBaseIdleTime = 15000
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object UnitChute
          KindOf = PARACHUTE UNATTACKABLE
          Locomotor = SET_NORMAL UnitChuteLoco
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
        Object UnitTrooper
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object UnitBus
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Locomotor = SET_NORMAL UnitBikeLoco
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
            DestructionDelay = 500
          End
        End
        """,
    };

    private static SyntheticFixture Fixture() => new(files: Files());

    /// <summary>A 20 x 20 cell map, optionally with a ramp, a lake over the first 100 x 100 units (level 5, the ground is at 0) and one cliff cell.</summary>
    private static TerrainMap Terrain(Func<int, int, byte>? height = null, bool lake = false, (int X, int Y)? cliff = null)
    {
        const int cells = 20, border = 1, width = cells + 2 * border;
        var stride = (width + 7) / 8;
        var cliffBits = new byte[stride * width];
        if (cliff is { } cell)
        {
            var hx = cell.X + border;
            var hy = cell.Y + border;
            cliffBits[hy * stride + (hx >> 3)] |= (byte)(1 << (hx & 7));
        }
        var heights = new byte[width * width];
        if (height is not null)
            for (var y = 0; y < width; y++)
                for (var x = 0; x < width; x++) heights[y * width + x] = height(x, y);
        var blend = new MapBlendTiles(8, [], [], [], [], [..cliffBits], stride, 0, 0, [], [], [], []);
        ImmutableArray<MapPolygonTrigger> triggers = lake ? [new MapPolygonTrigger("Lake", "", 1, true, false, 0, [new(0, 0, 5), new(100, 0, 5), new(100, 100, 5), new(0, 100, 5)])] : [];
        var map = new MapData("synthetic", new MapHeightField(width, width, border, [new MapBoundary(cells, cells)], [..heights]), blend,
            MapProperties.Empty, [], [], [], triggers, [], [], []);
        return TerrainMap.Build(map, "synthetic");
    }

    /// <summary>A world with a frame projector fed like the live game does (events, then the capture), and helpers to order and read.</summary>
    private sealed partial class Rig
    {
        private ulong _sequence;

        public Rig(SyntheticFixture fixture, TerrainMap? terrain = null)
        {
            Catalog = ObjectVisualCatalog.Build(fixture.Graph);
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain?.Fingerprint ?? ""), fixture.Templates, terrain);
            Projector = new FrameProjector(Catalog);
        }

        public ObjectVisualCatalog Catalog { get; }
        public SimulationWorld World { get; }
        public FrameProjector Projector { get; }

        public void Send(Func<ulong, SimCommand> command) => World.Submit(command(++_sequence));

        public EntityId Spawn(string template, int owner, int x, int y, int z = 0, Fixed? angle = null)
        {
            Send(sequence => new SpawnCommand(PlayerId.System, sequence, World.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.FromInt(z), angle ?? Fixed.Zero));
            Step();
            return new EntityId(World.Entities.Where(entity => entity.Template.Name == template).Max(entity => entity.Id.Value));
        }

        public void Step()
        {
            var result = World.Step();
            Projector.Observe(result.Events, World);
            Projector.Advance(World);
        }

        public Entity Get(EntityId id) => World.FindEntity(id)!;

        public ModelConditionSet Bits(EntityId id) => Projector.Project(1f).Drawables.Single(drawable => drawable.Entity == id).Conditions;
    }

    private static string Stage(ModelConditionSet bits)
        => bits.Contains(UnitFlags.ContinuousFireFast) ? "FAST" : bits.Contains(UnitFlags.ContinuousFireMean) ? "MEAN" : bits.Contains(UnitFlags.ContinuousFireSlow) ? "SLOW" : "none";

    private static void AddIfNew(List<string> list, string value)
    {
        if (list.Count == 0 || list[^1] != value) list.Add(value);
    }

    // ------------------------------------------------------------------------------------------------ tests

    private static void ContinuousFire()
    {
        var rig = new Rig(Fixture());
        var gun = rig.Spawn("UnitGatling", 1, 100, 100);
        var plain = rig.Spawn("UnitPlainGunner", 1, 100, 120);
        var target = rig.Spawn("UnitTarget", 2, 150, 100);
        Assert(rig.World.Weapons.TryGet("UnitGatlingGun", out var weapon) && weapon.ContinuousFireOneShots == 2 && weapon.ContinuousFireTwoShots == 4 && weapon.ContinuousFireCoastTicks == 30,
            "ContinuousFireOne/Two/Coast are read from the weapon (1000 ms = 30 ticks)");
        Assert(rig.World.Weapons.TryGet("UnitPlainGun", out var plainWeapon) && plainWeapon.ContinuousFireOneShots == int.MaxValue && plainWeapon.ContinuousFireCoastTicks == 0, "a weapon without the keys keeps INT_MAX / 0");

        var stages = new List<string>();
        var plainStages = new List<string>();
        var shots = 0;
        for (var tick = 0; tick < 150; tick++)
        {
            var entity = rig.Get(gun);
            if (tick < 24 && rig.World.Tick >= entity.WeaponReadyTick)
            {
                rig.Send(sequence => new FireWeaponCommand(new PlayerId(1), sequence, rig.World.Tick, gun, target));
                rig.Send(sequence => new FireWeaponCommand(new PlayerId(1), sequence, rig.World.Tick, plain, target));
                shots++;
            }
            rig.Step();
            AddIfNew(stages, Stage(rig.Bits(gun)));
            AddIfNew(plainStages, Stage(rig.Bits(plain)));
        }
        Assert(shots == 8, $"eight shots were fired ({shots})");
        Assert(stages.SequenceEqual(["none", "MEAN", "FAST", "SLOW", "none"]), $"the stages go up with the shots and drop after the coast time ({string.Join(" > ", stages)})");
        Assert(plainStages.SequenceEqual(["none"]), "a weapon without continuous-fire keys never shows a stage");
        Assert(!rig.Bits(gun).Contains(UnitFlags.ContinuousFireSlow), "no stage is left at the end");
    }

    private static void TurnLean()
    {
        var rig = new Rig(Fixture(), Terrain());
        var bike = rig.Spawn("UnitBike", 1, 100, 100);
        var states = new List<string>();
        string Lean(ModelConditionSet bits) => bits.Contains(UnitFlags.CenterToLeft) ? "CENTER_TO_LEFT" : bits.Contains(UnitFlags.LeftToCenter) ? "LEFT_TO_CENTER"
            : bits.Contains(UnitFlags.CenterToRight) ? "CENTER_TO_RIGHT" : bits.Contains(UnitFlags.RightToCenter) ? "RIGHT_TO_CENTER" : "straight";
        Assert(Lean(rig.Bits(bike)) == "straight", "a bike that stands is upright");

        // heading +x; the goal is due +y: a left turn (counter-clockwise, TURN_POSITIVE)
        rig.Send(sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, rig.World.Tick, bike, Fixed.FromInt(100), Fixed.FromInt(170)));
        for (var tick = 0; tick < 80; tick++)
        {
            rig.Step();
            AddIfNew(states, Lean(rig.Bits(bike)));
        }
        Assert(states.SequenceEqual(["CENTER_TO_LEFT", "LEFT_TO_CENTER", "straight"]), $"a left turn leans left at once, then recentres ({string.Join(" > ", states)})");

        // heading +y; the goal is due +x: a right turn
        states.Clear();
        var row = (int)rig.Get(bike).Y.Floor();
        rig.Send(sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, rig.World.Tick, bike, Fixed.FromInt(170), Fixed.FromInt(row)));
        for (var tick = 0; tick < 80; tick++)
        {
            rig.Step();
            AddIfNew(states, Lean(rig.Bits(bike)));
        }
        Assert(states.SequenceEqual(["CENTER_TO_RIGHT", "RIGHT_TO_CENTER", "straight"]), $"a right turn leans right at once, then recentres ({string.Join(" > ", states)})");

        // an object without the module never leans
        var hover = rig.Spawn("UnitHover", 1, 150, 150);
        rig.Send(sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, rig.World.Tick, hover, Fixed.FromInt(150), Fixed.FromInt(190)));
        for (var tick = 0; tick < 40; tick++) rig.Step();
        Assert(Lean(rig.Bits(hover)) == "straight", "without AnimationSteeringUpdate no turn animation is set");
    }

    private static void TurretRotate()
    {
        var rig = new Rig(Fixture(), Terrain());
        var scout = rig.Spawn("UnitGatling", 1, 100, 100);
        var victim = rig.Spawn("UnitTarget", 2, 100, 200); // due +y of a body facing +x: the turret turns +90 degrees
        var motion = new TurretMotion(6f * MathF.PI / 180f, 0.01f, 0f, 0f, 0f, false, 20);
        var visual = rig.Catalog["UnitGatling"] with { Turrets = [motion, null] };
        var turrets = new TurretTracker();
        var units = new UnitConditionTracker();
        var entity = rig.Get(scout);
        var tick = rig.World.Tick;

        Assert(!turrets.IsRotating(scout.Value), "a turret that was never updated does not rotate");
        turrets.Update(entity, visual, 0, tick, rig.World);
        Assert(!units.Update(entity, rig.World, tick, turrets).Contains(UnitFlags.TurretRotate), "a turret at its natural angle rests");

        turrets.Observe(new SimEvent(default, tick, SimEventKind.WeaponFired, scout, victim, new PlayerId(1), "W", 0), rig.World);
        var rotating = new List<bool>();
        for (var step = 1; step <= 90; step++)
        {
            turrets.Update(entity, visual, 0, tick + step, rig.World);
            rotating.Add(units.Update(entity, rig.World, tick + step, turrets).Contains(UnitFlags.TurretRotate));
        }
        Assert(rotating.Take(14).All(value => value), "TURRET_ROTATE while the turret turns toward the victim (14 full steps, the 15th arrives)");
        Assert(rotating.Skip(14).Take(6).All(value => !value), "it stops when the turret has arrived and holds the aim");
        Assert(rotating.Skip(22).Take(14).Any(value => value) && !rotating[^1], "after the recentre time it turns back (rotating again) and rests at the natural angle");
    }

    private static void JetBits()
    {
        var rig = new Rig(Fixture());
        rig.Send(sequence => new SpawnCommand(PlayerId.System, sequence, rig.World.Tick, "UnitAirfield", new PlayerId(1), Fixed.FromInt(500), Fixed.FromInt(500), Fixed.Zero, Fixed.Zero));
        rig.Step();
        rig.Send(sequence => new GiveMoneyCommand(PlayerId.System, sequence, rig.World.Tick, new PlayerId(1), Fixed.FromInt(1000)));
        rig.Step();
        var airfield = rig.World.Entities.Single(entity => entity.Template.Name == "UnitAirfield");
        rig.Send(sequence => new QueueProductionCommand(new PlayerId(1), sequence, rig.World.Tick, airfield.Id, "UnitJet"));
        var jet = default(Entity);
        for (var tick = 0; tick < 800 && (jet is null || JetOf(jet).Phase != JetAIUpdate.JetPhase.None); tick++)
        {
            rig.Step();
            jet ??= rig.World.Entities.FirstOrDefault(entity => entity.Template.Name == "UnitJet");
        }
        if (jet is null) throw new InvalidOperationException("the airfield produced no jet");
        Assert(JetOf(jet).Phase == JetAIUpdate.JetPhase.None && !JetOf(jet).AllowAirLoco, "the produced jet parked at its space");
        Assert(!rig.Bits(jet.Id).Contains(UnitFlags.JetExhaust) && !rig.Bits(jet.Id).Contains(UnitFlags.JetAfterburner), "a parked jet shows neither exhaust nor afterburner");

        rig.Send(sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, rig.World.Tick, jet.Id, Fixed.FromInt(1200), Fixed.FromInt(500)));
        int afterburnerTicks = 0, exhaustWhileTaxiing = 0, exhaustInFlight = 0, afterburnerOutsideRunway = 0;
        var phasesWithBurner = new HashSet<JetAIUpdate.JetPhase>();
        for (var tick = 0; tick < 1200 && !(JetOf(jet).Phase == JetAIUpdate.JetPhase.None && JetOf(jet).AllowAirLoco && !jet.HasMoveTarget); tick++)
        {
            rig.Step();
            var bits = rig.Bits(jet.Id);
            var jetModule = JetOf(jet);
            if (bits.Contains(UnitFlags.JetAfterburner))
            {
                afterburnerTicks++;
                phasesWithBurner.Add(jetModule.Phase);
                if (jetModule.Phase is not (JetAIUpdate.JetPhase.PauseBeforeTakeoff or JetAIUpdate.JetPhase.TakingOff)) afterburnerOutsideRunway++;
            }
            if (bits.Contains(UnitFlags.JetExhaust))
            {
                if (!jetModule.AllowAirLoco) exhaustWhileTaxiing++;
                else exhaustInFlight++;
            }
        }
        Assert(afterburnerTicks > 0 && afterburnerOutsideRunway == 0, $"the afterburner burns during the pause and the run ({afterburnerTicks} ticks, {string.Join(",", phasesWithBurner)})");
        Assert(phasesWithBurner.Contains(JetAIUpdate.JetPhase.TakingOff), "and during the take-off run");
        Assert(exhaustWhileTaxiing == 0, $"the exhaust needs the aircraft to be allowed to fly ({exhaustWhileTaxiing} taxiing ticks)");
        Assert(exhaustInFlight > 0, "once it may fly and moves, the exhaust shows");
        Assert(!rig.Bits(jet.Id).Contains(UnitFlags.JetAfterburner), "the afterburner is off once airborne");
    }

    private static JetAIUpdate JetOf(Entity entity) => entity.Modules.OfType<JetAIUpdate>().Single();

    private static void Parachute()
    {
        var rig = new Rig(Fixture());
        // A chute without a rider dies at its first update: both appear in one tick and the trooper steps in before the chute acts.
        foreach (var template in new[] { "UnitChute", "UnitTrooper" })
            rig.Send(sequence => new SpawnCommand(PlayerId.System, sequence, rig.World.Tick, template, new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(100), Fixed.FromInt(150), Fixed.Zero));
        rig.Step();
        var chute = rig.World.Entities.Single(entity => entity.Template.Name == "UnitChute").Id;
        var trooper = rig.World.Entities.Single(entity => entity.Template.Name == "UnitTrooper").Id;
        rig.Send(sequence => new EnterContainerCommand(PlayerId.System, sequence, rig.World.Tick, trooper, chute));
        rig.Step();
        var rider = rig.Get(trooper);
        var carrier = rig.Get(chute);
        var module = carrier.Modules.OfType<ParachuteContain>().Single();
        Assert(rider.ContainedBy == chute, "the trooper sits in the chute");

        ModelConditionSet RiderBits() => ConditionDeriver.Derive(rider, rig.World, rig.Catalog["UnitTrooper"], default);
        Assert(!module.Opened && RiderBits().Contains(UnitFlags.Freefall) && !RiderBits().Contains(UnitFlags.Parachuting), "before the chute opens the rider is in free fall");
        Assert(!rig.Bits(chute).Contains(UnitFlags.Parachuting) && !rig.Bits(chute).Contains(UnitFlags.Freefall), "the folded chute has neither bit");

        var tick = 0;
        for (; tick < 200 && !module.Opened; tick++) rig.Step();
        Assert(module.Opened && tick > 0, $"the chute opens after falling its open distance (tick {tick})");
        Assert(rig.Bits(chute).Contains(UnitFlags.Parachuting), "the opened chute shows PARACHUTING");
        Assert(RiderBits().Contains(UnitFlags.Parachuting) && !RiderBits().Contains(UnitFlags.Freefall), "the rider switches from FREEFALL to PARACHUTING");
    }

    private static void Toppled()
    {
        var rig = new Rig(Fixture());
        var tree = rig.Spawn("UnitTree", 0, 100, 100);
        var entity = rig.Get(tree);
        Assert(!rig.Bits(tree).Contains(UnitFlags.Toppled), "an upright tree is not toppled");
        entity.Modules.OfType<ToppleUpdate>().Single().ApplyTopplingForce(rig.World, Fixed.One, Fixed.Zero, Fixed.FromInt(10));
        rig.Step();
        Assert(rig.Bits(tree).Contains(UnitFlags.Toppled), "pushed over: TOPPLED");
        for (var tick = 0; tick < 200; tick++) rig.Step();
        Assert(rig.Bits(tree).Contains(UnitFlags.Toppled), "and it stays set after the fall ended");
    }

    private static void SecondLife()
    {
        var rig = new Rig(Fixture());
        var bus = rig.Spawn("UnitBus", 1, 100, 100);
        var entity = rig.Get(bus);
        Assert(!rig.Bits(bus).Contains(UnitFlags.SecondLife), "a bus on its first life");
        rig.Send(sequence => new DamageCommand(PlayerId.System, sequence, rig.World.Tick, bus, Fixed.FromInt(200), 0, 0));
        var inFlight = false;
        var seenInFlightWithBit = false;
        var landedTick = -1;
        for (var tick = 0; tick < 200; tick++)
        {
            rig.Step();
            var bits = rig.Bits(bus);
            if (entity.Z > Fixed.FromInt(3)) { inFlight = true; if (bits.Contains(UnitFlags.SecondLife)) seenInFlightWithBit = true; }
            if (inFlight && landedTick < 0 && bits.Contains(UnitFlags.SecondLife)) landedTick = tick;
        }
        Assert(inFlight && !seenInFlightWithBit, "while the thrown bus is in the air the bit is not set");
        Assert(landedTick > 0 && rig.Bits(bus).Contains(UnitFlags.SecondLife), "after it hit the ground SECOND_LIFE is set and stays set");
        rig.Send(sequence => new DamageCommand(PlayerId.System, sequence, rig.World.Tick, bus, Fixed.FromInt(1000), 0, 0));
        rig.Step();
        Assert(rig.Get(bus).EffectivelyDead && rig.Bits(bus).Contains(UnitFlags.SecondLife), "a real death later does not clear it");
    }

    private static void ClimbingAndWater()
    {
        var rig = new Rig(Fixture(), Terrain(lake: true, cliff: (12, 12)));
        var climber = rig.Spawn("UnitBike", 1, 125, 125); // cell (12, 12)
        var walker = rig.Spawn("UnitBike", 1, 175, 175);
        rig.Send(sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, rig.World.Tick, climber, Fixed.FromInt(190), Fixed.FromInt(125)));
        rig.Send(sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, rig.World.Tick, walker, Fixed.FromInt(190), Fixed.FromInt(175)));
        rig.Step();
        Assert(rig.Bits(climber).Contains(UnitFlags.Climbing), "a moving object on a cliff cell shows CLIMBING");
        Assert(!rig.Bits(walker).Contains(UnitFlags.Climbing), "on open ground it does not");
        for (var tick = 0; tick < 40; tick++) rig.Step();
        Assert(!rig.Bits(climber).Contains(UnitFlags.Climbing), "left the cliff cell: not climbing any more");

        var hover = rig.Spawn("UnitHover", 1, 50, 50);
        var hoverOnLand = rig.Spawn("UnitHover", 1, 150, 150);
        var boat = rig.Spawn("UnitBike", 1, 60, 60);
        Assert(rig.Bits(hover).Contains(UnitFlags.OverWater), "a hover locomotor over the lake is OVER_WATER");
        Assert(!rig.Bits(hoverOnLand).Contains(UnitFlags.OverWater), "on dry ground it is not");
        Assert(!rig.Bits(boat).Contains(UnitFlags.OverWater), "only hover locomotors set it");
    }

    private static void CrushAndSplat()
    {
        var rig = new Rig(Fixture());
        var units = new UnitConditionTracker();
        var turrets = new TurretTracker();
        int death(string name) => NameTables.DeathType.TryIndexOf(name, out var index) ? index : throw new InvalidOperationException(name);
        SimEvent Died(EntityId victim, EntityId source, int deathType) => new(default, rig.World.Tick, SimEventKind.EntityDied, victim, source, new PlayerId(2), string.Empty, deathType);
        ModelConditionSet After(Entity victim) => units.Update(victim, rig.World, rig.World.Tick, turrets);

        void Case(string name, int crusherX, int crusherY, bool expectFront, bool expectBack, bool validSource = true, string template = "UnitCar", int deathType = DeathTypes.Crushed)
        {
            var victimId = rig.Spawn(template, 1, 100, 100); // heading +x, major radius 11: crush points at 100 +- 5.5
            var victim = rig.Get(victimId);
            var source = validSource ? rig.Spawn("UnitTarget", 2, crusherX, crusherY) : EntityId.Invalid;
            units.Observe(Died(victimId, source, deathType), rig.World);
            var bits = After(victim);
            Assert(bits.Contains(UnitFlags.FrontCrushed) == expectFront && bits.Contains(UnitFlags.BackCrushed) == expectBack,
                $"{name}: front {bits.Contains(UnitFlags.FrontCrushed)} back {bits.Contains(UnitFlags.BackCrushed)}");
        }

        Case("crusher ahead of the car: the front point is nearest", 108, 100, true, false);
        Case("crusher behind the car: the back point is nearest", 92, 100, false, true);
        Case("crusher at the middle: the whole car", 100, 100, true, true);
        Case("no source object: total crush", 0, 0, true, true, validSource: false);
        Case("a normal death does not crush", 100, 100, false, false, deathType: DeathTypes.Normal);
        Case("an object without CrushDie keeps its shape", 100, 100, false, false, template: "UnitTarget");

        var victimId2 = rig.Spawn("UnitTarget", 1, 200, 200);
        units.Observe(Died(victimId2, EntityId.Invalid, death("SPLATTED")), rig.World);
        Assert(After(rig.Get(victimId2)).Contains(UnitFlags.Splatted), "a SPLATTED death sets SPLATTED");
        var other = rig.Spawn("UnitTarget", 1, 220, 220);
        units.Observe(Died(other, EntityId.Invalid, death("BURNED")), rig.World);
        Assert(!After(rig.Get(other)).Contains(UnitFlags.Splatted), "other deaths do not");
        units.Prune(id => id != victimId2.Value);
        Assert(!After(rig.Get(victimId2)).Contains(UnitFlags.Splatted), "pruned objects are forgotten");
    }

    private static void Cheering()
    {
        var rig = new Rig(Fixture());
        var scout = rig.Spawn("UnitTrooper", 1, 100, 100);
        var entity = rig.Get(scout);
        var units = new UnitConditionTracker();
        var turrets = new TurretTracker();
        Assert(!units.Update(entity, rig.World, 10, turrets).Contains(UnitFlags.SpecialCheering), "nobody cheers by default");
        units.Cheer([scout], 10);
        Assert(units.Update(entity, rig.World, 10, turrets).Contains(UnitFlags.SpecialCheering) && units.Update(entity, rig.World, 99, turrets).Contains(UnitFlags.SpecialCheering),
            "the cheer starts at once");
        Assert(!units.Update(entity, rig.World, 100, turrets).Contains(UnitFlags.SpecialCheering), "and ends three seconds (90 ticks) later");
        units.Cheer([scout], 200);
        Assert(units.Update(entity, rig.World, 250, turrets).Contains(UnitFlags.SpecialCheering), "a new cheer restarts the time");

        // through the projector
        rig.Step();
        rig.Projector.Cheer([scout]);
        Assert(rig.Bits(scout).Contains(UnitFlags.SpecialCheering) is false, "the projector captures the bit with the next tick");
        rig.Step();
        Assert(rig.Bits(scout).Contains(UnitFlags.SpecialCheering), "and shows it");
        for (var tick = 0; tick < UnitConditionTracker.CheerTicks + 2; tick++) rig.Step();
        Assert(!rig.Bits(scout).Contains(UnitFlags.SpecialCheering), "and it ends by itself");
    }

    private static void TensileFreefall()
    {
        // The avalanche module needs a slope and a damaged rock; check the bit through the tracker on a rock that the module moves.
        var files = new Dictionary<string, string>(Files())
        {
            ["Data/INI/Object/UnitRock.ini"] = """
            Object UnitRock
              KindOf = SELECTABLE
              Body = ActiveBody Body01
                MaxHealth = 100.0
                InitialHealth = 100.0
              End
              Behavior = TensileFormationUpdate Tensile01
                Enabled = Yes
              End
              Behavior = DestroyDie Destroy01
              End
            End
            """,
        };
        // a steep slope toward +x: 4 height bytes (2.5 units) per 10 units
        var rig = new Rig(new SyntheticFixture(files: files), Terrain((x, _) => (byte)Math.Min(255, 4 * (21 - x))));
        var rock = rig.Spawn("UnitRock", 0, 100, 100, z: 0);
        var entity = rig.Get(rock);
        var freefall = 0;
        var late = 0;
        for (var tick = 0; tick < 320; tick++)
        {
            rig.Step();
            var bits = rig.Bits(rock);
            var module = entity.Modules.OfType<TensileFormationUpdate>().Single();
            if (bits.Contains(UnitFlags.Freefall))
            {
                freefall++;
                Assert(module.Life is > 0 and < 100, $"FREEFALL only while m_life < 100 (life {module.Life})");
            }
            if (module.Life >= 100 && bits.Contains(UnitFlags.Freefall)) late++;
        }
        Assert(freefall > 0, "a rock sliding down a steep slope drops more than 0.2 per tick and shows FREEFALL");
        Assert(late == 0, "never after the first hundred updates");
    }

    private static void ProducedNames()
    {
        string[] implemented =
        [
            "CONTINUOUS_FIRE_SLOW", "CONTINUOUS_FIRE_MEAN", "CONTINUOUS_FIRE_FAST", "JETAFTERBURNER", "JETEXHAUST", "FREEFALL", "SPLATTED", "FRONTCRUSHED", "BACKCRUSHED", "TOPPLED",
            "CLIMBING", "OVER_WATER", "CENTER_TO_LEFT", "CENTER_TO_RIGHT", "LEFT_TO_CENTER", "RIGHT_TO_CENTER", "TURRET_ROTATE", "SECOND_LIFE", "SPECIAL_CHEERING",
        ];
        // The simulation has no model of the behaviour behind these (docs/MODEL-CONDITIONS-UNITS.md): setting them would be invention.
        string[] notDerivable = ["PREATTACK_A", "PREATTACK_B", "PREATTACK_C", "EXPLODED_FLAILING", "EXPLODED_BOUNCING", "JAMMED", "RAISING_FLAG", "PANICKING", "RAPPELLING"];
        foreach (var name in implemented) Assert(ConditionDeriver.ProducedNames.Contains(name), $"{name} is produced");
        foreach (var name in notDerivable) Assert(!ConditionDeriver.ProducedNames.Contains(name), $"{name} is not produced (the simulation does not model it)");
    }

    // ------------------------------------------------------------------------------------------------ real data

    private sealed class Retail
    {
        public Retail(string directory)
        {
            Content = ContentFileSystem.Open(directory);
            Graph = DefinitionCompiler.Compile(new RealSource(Content), ModuleRegistry.Default,
                DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
            Catalog = ObjectVisualCatalog.Build(Graph);
            Templates = TemplateCatalog.Build(Graph, ModuleBindingTable.Default);
            Fingerprint = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(Graph).Item1;
        }

        public ContentFileSystem Content { get; }
        public CompiledDefinitionGraph Graph { get; }
        public ObjectVisualCatalog Catalog { get; }
        public TemplateCatalog Templates { get; }
        public string Fingerprint { get; }
    }

    private sealed class RealSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }

    private static string SelectedConditions(ObjectVisual visual, ModelConditionSet bits)
    {
        var draw = visual.Draws.First(candidate => candidate.Kind == DrawModuleKind.Model);
        var index = BestStateSelector.FindBest(draw, bits);
        return index < 0 ? "none" : draw.States[index].Conditions[0].ToString();
    }

    private static void RealGattling(string directory)
    {
        var retail = new Retail(directory);
        var rig = new Rig(retail);
        var tank = rig.Spawn("ChinaTankGattling", 1, 100, 100);
        var target = rig.Spawn("AmericaBarracks", 2, 150, 100);
        Assert(retail.Templates.TryGet("ChinaTankGattling", out var template) && template.IsInstantiable, "the gattling tank binds");
        var stages = new List<string>();
        var selected = new HashSet<string>();
        for (var tick = 0; tick < 400; tick++)
        {
            if (tick < 90 && rig.World.Tick >= rig.Get(tank).ReadyTickOf(0)) rig.Send(sequence => new FireWeaponCommand(new PlayerId(1), sequence, rig.World.Tick, tank, target));
            rig.Step();
            var bits = rig.Bits(tank);
            AddIfNew(stages, Stage(bits));
            if (bits.Contains(UnitFlags.ContinuousFireMean) || bits.Contains(UnitFlags.ContinuousFireFast)) selected.Add(SelectedConditions(retail.Catalog["ChinaTankGattling"], bits));
        }
        Assert(stages.SequenceEqual(["none", "MEAN", "FAST", "SLOW", "none"]) || stages.SequenceEqual(["none", "MEAN", "SLOW", "none"]), $"the tank spins up and down ({string.Join(" > ", stages)})");
        Assert(selected.Any(value => value.Contains("CONTINUOUS_FIRE")), $"the model states for the stages are selected ({string.Join(" | ", selected)})");
        Console.WriteLine($"       gattling tank stages: {string.Join(" > ", stages)}; selected: {string.Join(" | ", selected)}");
    }

    private static void RealBikeAndCar(string directory)
    {
        var retail = new Retail(directory);

        // the rider gets off: the bike is scuttled and shows TOPPLED
        var rig = new Rig(retail);
        var bike = rig.Spawn("GLAVehicleCombatBike", 1, 100, 100);
        for (var tick = 0; tick < 5; tick++) rig.Step();
        var module = rig.Get(bike).Modules.OfType<RiderChangeContain>().Single();
        Assert(!module.IsScuttled && !rig.Bits(bike).Contains(UnitFlags.Toppled), "a ridden bike stands");
        var rider = rig.World.Entities.First(entity => entity.ContainedBy == bike).Id;
        rig.Send(sequence => new ExitContainerCommand(new PlayerId(1), sequence, rig.World.Tick, rider));
        rig.Step();
        Assert(module.IsScuttled && rig.Bits(bike).Contains(UnitFlags.Toppled), "the deserted bike is scuttled: TOPPLED");
        Assert(SelectedConditions(retail.Catalog["GLAVehicleCombatBike"], rig.Bits(bike)).Contains("TOPPLED"), "and a TOPPLED model state is selected");

        // the lean on real terrain: the bike turns toward a goal on both sides
        var mapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = retail.Content.OpenRead(mapPath)) map = CnCNew.Formats.SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, mapPath);
        var start = terrain.StartPositions.First();
        var driving = new Rig(retail, terrain);
        var cx = (int)start.X.Floor();
        var cy = (int)start.Y.Floor();
        var lean = driving.Spawn("GLAVehicleCombatBike", 1, cx, cy);
        var seen = new HashSet<string>();
        foreach (var (dx, dy) in new[] { (0, 60), (60, 0), (0, -60), (-60, 0), (0, 60) })
        {
            var entity = driving.Get(lean);
            driving.Send(sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, driving.World.Tick, lean, entity.X + Fixed.FromInt(dx), entity.Y + Fixed.FromInt(dy)));
            for (var tick = 0; tick < 90; tick++)
            {
                driving.Step();
                var bits = driving.Bits(lean);
                foreach (var flag in new[] { UnitFlags.CenterToLeft, UnitFlags.LeftToCenter, UnitFlags.CenterToRight, UnitFlags.RightToCenter })
                    if (bits.Contains(flag)) seen.Add(flag.Name);
            }
        }
        Assert(seen.Contains("CENTER_TO_LEFT") && seen.Contains("CENTER_TO_RIGHT") && seen.Contains("LEFT_TO_CENTER") && seen.Contains("RIGHT_TO_CENTER"), $"a driving bike leans both ways ({string.Join(", ", seen.Order())})");
        var leanState = SelectedConditions(retail.Catalog["GLAVehicleCombatBike"], ModelConditionSet.Parse("MOVING CENTER_TO_LEFT"));
        Assert(leanState.Contains("CENTER_TO_LEFT"), $"a lean state is selected ({leanState})");

        // a crushed car
        var cars = new Rig(retail);
        var car = cars.Spawn("CarCamaro", 0, 100, 100);
        Assert(!cars.Bits(car).Contains(UnitFlags.FrontCrushed), "an intact car");
        var crushed = NameTables.DeathType.TryIndexOf("CRUSHED", out var crushedIndex) ? crushedIndex : throw new InvalidOperationException("CRUSHED");
        cars.Send(sequence => new DamageCommand(PlayerId.System, sequence, cars.World.Tick, car, Fixed.FromInt(100000), 0, crushed));
        cars.Step();
        var carBits = cars.Bits(car);
        Assert(carBits.Contains(UnitFlags.FrontCrushed) && carBits.Contains(UnitFlags.BackCrushed), $"killed by a crush without a crusher object: the whole car ({carBits})");
        var crushedState = SelectedConditions(retail.Catalog["CarCamaro"], carBits);
        Assert(crushedState.Contains("FRONTCRUSHED") && crushedState.Contains("BACKCRUSHED"), $"the crushed model state is selected ({crushedState})");
    }

    private static void RealJetsAndBus(string directory)
    {
        var retail = new Retail(directory);

        // a placed jet flies from the start: exhaust once it moves
        var rig = new Rig(retail);
        var jet = rig.Spawn("AmericaJetRaptor", 1, 200, 200);
        Assert(!rig.Bits(jet).Contains(UnitFlags.JetExhaust), "a hovering jet without speed has no exhaust");
        rig.Send(sequence => new SetMoveTargetCommand(new PlayerId(1), sequence, rig.World.Tick, jet, Fixed.FromInt(900), Fixed.FromInt(200)));
        for (var tick = 0; tick < 30; tick++) rig.Step();
        var flying = rig.Bits(jet);
        Assert(flying.Contains(UnitFlags.JetExhaust) && !flying.Contains(UnitFlags.JetAfterburner), $"a flying jet shows the exhaust ({flying})");
        Assert(SelectedConditions(retail.Catalog["AmericaJetRaptor"], flying).Contains("JETEXHAUST"), "and a JETEXHAUST model state is selected");

        // the battle bus second life
        var buses = new Rig(retail);
        var bus = buses.Spawn("GLAVehicleBattleBus", 1, 100, 100);
        Assert(!buses.Bits(bus).Contains(UnitFlags.SecondLife), "a bus on its first life");
        buses.Send(sequence => new DamageCommand(PlayerId.System, sequence, buses.World.Tick, bus, Fixed.FromInt(100000), 0, 0));
        var entity = buses.Get(bus);
        var landed = false;
        var selected = string.Empty;
        for (var tick = 0; tick < 150 && !landed; tick++)
        {
            buses.Step();
            if (!buses.Bits(bus).Contains(UnitFlags.SecondLife)) continue;
            landed = true;
            Assert(!entity.EffectivelyDead && !buses.Bits(bus).Contains(ModelConditions.Dying), "the bus is alive on the ground in its second life");
            selected = SelectedConditions(retail.Catalog["GLAVehicleBattleBus"], buses.Bits(bus));
        }
        Assert(landed, "after its first death the thrown bus lands and gets SECOND_LIFE");
        Assert(selected.Contains("SECOND_LIFE"), $"and a SECOND_LIFE model state is selected ({selected})");
        for (var tick = 0; tick < 150; tick++) buses.Step();
        Assert(entity.EffectivelyDead && buses.Bits(bus).Contains(UnitFlags.SecondLife), "the empty hulk is destroyed later and keeps the bit");
    }

    // The Rig of the retail tests uses the retail templates.
    private sealed partial class Rig
    {
        public Rig(Retail retail, TerrainMap? terrain = null)
        {
            Catalog = retail.Catalog;
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), 1, Players, "presentation-real", retail.Fingerprint, terrain?.Fingerprint ?? ""), retail.Templates, terrain);
            Projector = new FrameProjector(Catalog);
        }
    }
}
