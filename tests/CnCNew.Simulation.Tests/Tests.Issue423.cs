using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Issue #423: four small differences to the original - recruiting a held unit, a stunned unit's locomotor, the repulsor filter's "able to attack", a self-upgrade that starts active.</summary>
static partial class Tests
{
    /// <summary>
    /// <c>Team::tryToRecruit</c> skips an object that <c>isDisabledByType(DISABLED_HELD)</c> ("Don't recruit held units"): a soldier a script holds (<c>NAMED_SET_HELD</c>) is never recruited,
    /// the free ones of the same team are.
    /// </summary>
    public static void AHeldUnitIsNotRecruited()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Recruit", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_SET_HELD", Scr.Unit("Held"), Scr.Bool(true)), Scr.Do("RECRUIT_TEAM", Scr.Team("Strike"), Scr.Real(0m))], oneShot: true),
        ], objects: [Placed("BetaSoldier", 100m, 200m, "teamPlayerB", "Free1"), Placed("BetaSoldier", 120m, 200m, "teamPlayerB", "Held"), Placed("BetaSoldier", 140m, 200m, "teamPlayerB", "Free2")]);
        var strikeTeam = new MapProperties([Scr.Text("teamName", "Strike"), Scr.Text("teamOwner", PlayerB), Scr.Flag("teamIsSingleton", true), Scr.Text("teamHome", "Far"),
            new MapProperty("teamProductionPriority", MapPropertyType.Int, 5, 0m, string.Empty),
            Scr.Text("teamUnitType1", "BetaSoldier"), new MapProperty("teamUnitMinCount1", MapPropertyType.Int, 1, 0m, string.Empty), new MapProperty("teamUnitMaxCount1", MapPropertyType.Int, 3, 0m, string.Empty)]);
        map = map with { Teams = [..map.Teams, strikeTeam, new([Scr.Text("teamName", "teamPlayerB"), Scr.Text("teamOwner", PlayerB), Scr.Flag("teamIsSingleton", true)])] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(10));
        var (free1, held, free2) = (NamedIn(session, "Free1")!, NamedIn(session, "Held")!, NamedIn(session, "Free2")!);
        Check.That((session.World.DisabledFlagsOf(held) & DisabledMask.Held) != DisabledMask.None, "Setup: the script holds the soldier.");
        var strike = session.Engine.TeamInstances.First(team => team.Prototype == "Strike");
        Check.That(strike.Members.Count == 2 && !strike.Members.Contains(held.Id.Value) && strike.Members.Contains(free1.Id.Value) && strike.Members.Contains(free2.Id.Value),
            $"A held soldier is not recruited, the free ones are ({strike.Members.Count} members, held one in: {strike.Members.Contains(held.Id.Value)}).");
    }

    // A property: the static fields of the partial class are set in no defined order across its files.
    private static IReadOnlyDictionary<string, string> StunFiles => new Dictionary<string, string>(ShockFiles)
    {
        ["Data/INI/Object/StunFixture.ini"] = """
        Locomotor StunWheels
          Surfaces = GROUND
          Speed = 20
          Acceleration = 300
          Braking = 300
          TurnRate = 3600
        End
        Locomotor StunFeet
          Surfaces = GROUND
          Speed = 20
          Acceleration = 300
          Braking = 300
          TurnRate = 3600
          StickToGround = Yes
        End
        Object StunSoldier
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL StunFeet
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = PhysicsBehavior Physics01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object StunRover
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL StunWheels
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 5.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>
    /// <c>Locomotor::locoUpdate_moveTowardsPosition</c> and <c>locoUpdate_moveTowardsAngle</c> return at once while the physics module is stunned, and only the physics update's
    /// integration - which a held object skips - ends the stun (<c>PhysicsUpdate.cpp</c> 695-708). A soldier a shock wave hits while it is held keeps its stun after its flight is over;
    /// the soldier sticks to the ground (no rates), so its physics sleeps (<c>calcSleepTime</c>) and, let go, it does not walk off on its order: the stun stays. A rover the shock gave
    /// rates stays awake (<c>HAS_PITCHROLLYAW</c>): let go, its first update turns it and ends the stun, and it drives. Both survive a save. The port moved the soldier at once and never
    /// ended the rover's stun (it dropped the rates at rest). At 30, 60 and 120 Hz.
    /// </summary>
    public static void AStunnedUnitsLocomotorDoesNothing()
    {
        static bool IsStunned(Entity entity) => entity.Modules.OfType<PhysicsBehavior>().Single().Stunned;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: StunFiles);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "ShockLauncher", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "StunSoldier", owner: 2, x: 50, y: 0);
            feeder.Spawn(2, "StunRover", owner: 2, x: -50, y: 0);
            world.Step();
            var soldier = world.Entities.Single(value => value.Template.Name == "StunSoldier");
            var rover = world.Entities.Single(value => value.Template.Name == "StunRover");
            var (soldierPhysics, roverPhysics) = (soldier.Modules.OfType<PhysicsBehavior>().Single(), rover.Modules.OfType<PhysicsBehavior>().Single());
            world.SetScriptDisable(soldier, Entity.ScriptHeld, true);
            world.SetScriptDisable(rover, Entity.ScriptHeld, true);
            Check.That(world.Weapons.TryGet("ShockBomb", out var bomb), "Setup: the bomb is in the catalog.");
            world.DealWeaponDamage(world.Entities.Single(value => value.Template.Name == "ShockLauncher"), bomb!, null, Fixed.Zero, Fixed.Zero, Fixed.Zero);
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            Check.That(soldierPhysics.Stunned && roverPhysics.Stunned && !soldier.InPhysicsFlight && !rover.InPhysicsFlight, $"{rate}: setup - both are held, stunned and at rest.");
            Check.That(feeder.SetMoveTarget(2, soldier.Id.Value, 50, 200).Accepted && feeder.SetMoveTarget(2, rover.Id.Value, -50, 200).Accepted, $"{rate}: setup - both take a move.");
            world.Step();

            // A save while the stun outlasts the flight keeps it.
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
            Check.That(restored.StateHash == world.StateHash, $"{rate}: a restored world has the same hash.");
            foreach (var current in new[] { world, restored })
            {
                var heldSoldier = current.Entities.Single(value => value.Template.Name == "StunSoldier");
                var heldRover = current.Entities.Single(value => value.Template.Name == "StunRover");
                Check.That(IsStunned(heldSoldier) && IsStunned(heldRover), $"{rate}: the stun outlasts the flight, also in a restored world.");
                current.SetScriptDisable(heldSoldier, Entity.ScriptHeld, false);
                current.SetScriptDisable(heldRover, Entity.ScriptHeld, false);
                for (var step = 0; step < current.Frames(60); step++) current.Step();
                Check.That(heldSoldier.X == Fixed.FromInt(50) && heldSoldier.Y == Fixed.Zero && IsStunned(heldSoldier),
                    $"{rate}: let go, the soldier's locomotor does nothing - it is still stunned and has not walked ({heldSoldier.X}, {heldSoldier.Y}).");
                Check.That(!IsStunned(heldRover) && heldRover.Modules.OfType<PhysicsBehavior>().Single() is { Stunned: false, Flying: false } && heldRover.Y > Fixed.FromInt(20),
                    $"{rate}: let go, the rover's first update ends its stun and it drives ({heldRover.Y}, stunned {IsStunned(heldRover)}, flying {heldRover.InPhysicsFlight}, move {heldRover.HasMoveTarget}).");
            }
            Check.That(restored.StateHash == world.StateHash, $"{rate}: the restored world went on as the other.");
        }
    }
}
