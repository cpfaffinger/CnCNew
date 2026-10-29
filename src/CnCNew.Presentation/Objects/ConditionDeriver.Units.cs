using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>The model condition flags of units, weapons, movement and death (docs/MODEL-CONDITIONS-UNITS.md); the stateful ones live in <see cref="UnitConditionTracker"/>.</summary>
public static class UnitFlags
{
    public static readonly ModelConditionFlag Toppled = ModelConditions.Parse("TOPPLED");
    public static readonly ModelConditionFlag FrontCrushed = ModelConditions.Parse("FRONTCRUSHED");
    public static readonly ModelConditionFlag BackCrushed = ModelConditions.Parse("BACKCRUSHED");
    public static readonly ModelConditionFlag TurretRotate = ModelConditions.Parse("TURRET_ROTATE");
    public static readonly ModelConditionFlag Freefall = ModelConditions.Parse("FREEFALL");
    public static readonly ModelConditionFlag Parachuting = ModelConditions.Parse("PARACHUTING");
    public static readonly ModelConditionFlag Climbing = ModelConditions.Parse("CLIMBING");
    public static readonly ModelConditionFlag OverWater = ModelConditions.Parse("OVER_WATER");
    public static readonly ModelConditionFlag JetAfterburner = ModelConditions.Parse("JETAFTERBURNER");
    public static readonly ModelConditionFlag JetExhaust = ModelConditions.Parse("JETEXHAUST");
    public static readonly ModelConditionFlag SpecialCheering = ModelConditions.Parse("SPECIAL_CHEERING");
    public static readonly ModelConditionFlag Splatted = ModelConditions.Parse("SPLATTED");
    public static readonly ModelConditionFlag SecondLife = ModelConditions.Parse("SECOND_LIFE");
    public static readonly ModelConditionFlag StunnedFlailing = ModelConditions.Parse("STUNNED_FLAILING");
    public static readonly ModelConditionFlag Stunned = ModelConditions.Parse("STUNNED");
    public static readonly ModelConditionFlag ContinuousFireSlow = ModelConditions.Parse("CONTINUOUS_FIRE_SLOW");
    public static readonly ModelConditionFlag ContinuousFireMean = ModelConditions.Parse("CONTINUOUS_FIRE_MEAN");
    public static readonly ModelConditionFlag ContinuousFireFast = ModelConditions.Parse("CONTINUOUS_FIRE_FAST");
    public static readonly ModelConditionFlag CenterToLeft = ModelConditions.Parse("CENTER_TO_LEFT");
    public static readonly ModelConditionFlag LeftToCenter = ModelConditions.Parse("LEFT_TO_CENTER");
    public static readonly ModelConditionFlag CenterToRight = ModelConditions.Parse("CENTER_TO_RIGHT");
    public static readonly ModelConditionFlag RightToCenter = ModelConditions.Parse("RIGHT_TO_CENTER");
}

public static partial class ConditionDeriver
{
    /// <summary>The ground height at which the reference's train drawable counts as inside a tunnel (<c>RailroadGuideAIUpdate::update</c>: three units below the ground).</summary>
    private static readonly Fixed TunnelDepth = Fixed.FromInt(3);

    /// <summary>
    /// The bits of units that are a pure function of the module state the simulation keeps (docs/MODEL-CONDITIONS-UNITS.md names, per bit, the reference function that sets it):
    /// <c>JetAIUpdate::update</c> (JETEXHAUST) and <c>friend_enableAfterburners</c> (JETAFTERBURNER), <c>ParachuteContain</c> (PARACHUTING of the opened chute and FREEFALL/PARACHUTING of its rider),
    /// <c>RiderChangeContain</c> scuttle and <c>ToppleUpdate::applyTopplingForce</c> (TOPPLED), <c>BattleBusSlowDeathBehavior::update</c> (SECOND_LIFE), <c>AIFollowPathState</c> (CLIMBING on a cliff cell),
    /// <c>Locomotor::moveTowardsPositionHover</c> and <c>RailroadGuideAIUpdate</c> (OVER_WATER).
    /// </summary>
    private static ModelConditionSet UnitStates(Entity entity, SimulationWorld world, ModelConditionSet set)
    {
        // ParachuteContain::onContaining / update: the rider has FREEFALL until the chute opens, PARACHUTING after.
        if (entity.ContainedBy.IsValid && world.FindEntity(entity.ContainedBy)?.Container is ParachuteContain carrier)
            set = set.With(carrier.Opened ? UnitFlags.Parachuting : UnitFlags.Freefall);

        var modules = entity.Modules;
        for (var index = 0; index < modules.Count; index++)
        {
            switch (modules[index])
            {
                case JetAIUpdate jet:
                    // JetAIUpdate::update: velocity > 0 while the aircraft may fly.
                    if (jet.AllowAirLoco && entity.Speed != Fixed.Zero) set = set.With(UnitFlags.JetExhaust);
                    if (jet.AfterburnersOn) set = set.With(UnitFlags.JetAfterburner);
                    break;
                case ParachuteContain { Opened: true } when !entity.ContainedBy.IsValid:
                    // ParachuteContain::update: the chute opens (clearAndSetModelConditionState(FREEFALL, PARACHUTING)).
                    set = set.With(UnitFlags.Parachuting);
                    break;
                case RiderChangeContain { IsScuttled: true }:
                    // RiderChangeContain::onRemoving: the deserted bike gets ScuttleStatus (TOPPLED in all shipped data).
                    set = set.With(UnitFlags.Toppled);
                    break;
                case ToppleUpdate { HasToppled: true }:
                    set = set.With(UnitFlags.Toppled);
                    break;
                case PhysicsBehavior physics when physics.ShowsFlailing || physics.ShowsStunned:
                    // Object::attemptDamage's shock wave and PhysicsBehavior::update (F-63): STUNNED_FLAILING from the shock until a stunned landing, STUNNED after it.
                    if (physics.ShowsFlailing) set = set.With(UnitFlags.StunnedFlailing);
                    if (physics.ShowsStunned) set = set.With(UnitFlags.Stunned);
                    break;
                case BattleBusSlowDeathBehavior bus when bus.HasLanded(world):
                    set = set.With(UnitFlags.SecondLife);
                    break;
                case RailroadBehavior when world.Terrain is not null && entity.Z < world.GroundHeightAt(entity.X, entity.Y) - TunnelDepth:
                    set = set.With(UnitFlags.OverWater);
                    break;
            }
        }

        if (world.Terrain is { } terrain && (entity.Speed != Fixed.Zero || entity.HasMoveTarget))
        {
            // AIFollowPathState: a moving object on a cliff cell shows CLIMBING (in addition to MOVING).
            var cellX = TerrainMap.CellOf(entity.X);
            var cellY = TerrainMap.CellOf(entity.Y);
            if (terrain.IsInside(cellX, cellY) && (terrain.CellAt(cellX, cellY) & CellFlags.Cliff) != 0) set = set.With(UnitFlags.Climbing);
        }

        // Locomotor::moveTowardsPositionHover: a hover locomotor over water (terrain below the water level) has OVER_WATER; the flag only changes while the object moves, so a position is all it needs.
        if (entity.Template.Locomotor is { IsHover: true } && world.Terrain is { } water && water.IsUnderwater(entity.X, entity.Y)) set = set.With(UnitFlags.OverWater);
        return set;
    }
}
