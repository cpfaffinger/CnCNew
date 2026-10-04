using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// Model conditions of a unit that is capturing a building (<see cref="SpecialAbilityUpdate"/>, docs/SIM-STAGED-CAPTURE.md). The reference sets them from inside
/// <c>SpecialAbilityUpdate</c> (<c>startUnpacking</c>, <c>startPreparation</c>, <c>startPacking</c>, <c>onExit</c>); here they are read from the module's stage:
/// <c>UNPACKING</c> while it unpacks, <c>RAISING_FLAG</c> during the preparation of the infantry capture (<c>startPreparation</c> swaps <c>UNPACKING</c> for it),
/// <c>FIRING_A</c> during the Black Lotus's preparation (the hacker's typing animation), <c>PACKING</c> while it packs up.
/// </summary>
public static class CaptureConditions
{
    public static readonly ModelConditionFlag Unpacking = ModelConditions.Parse("UNPACKING");
    public static readonly ModelConditionFlag Packing = ModelConditions.Parse("PACKING");
    public static readonly ModelConditionFlag RaisingFlag = ModelConditions.Parse("RAISING_FLAG");
    public static readonly ModelConditionFlag FiringA = ModelConditions.Parse("FIRING_A");

    public static ModelConditionSet Apply(Entity entity, SimulationWorld world, ModelConditionSet set)
    {
        foreach (var module in entity.Modules)
        {
            if (module is not SpecialAbilityUpdate { IsActive: true } ability) continue;
            switch (ability.Stage)
            {
                case AbilityStage.Unpacking:
                    set = set.With(Unpacking);
                    break;
                case AbilityStage.Preparing:
                    set = set.With(ability.IsHackStyle(world) ? FiringA : RaisingFlag);
                    break;
                case AbilityStage.Packing:
                    set = set.With(Packing);
                    break;
            }
        }
        return set;
    }
}
