using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.World;

/// <summary>
/// F-59, <c>SpecialPowerModule::createViewObject</c>: a power triggered at a place opens a view there for its owner - the <c>GameData</c>'s <c>SpecialPowerViewObject</c>
/// (<c>SuperweaponPing</c>), its shroud clearing range set to the power's <c>ViewObjectRange</c> and its <c>DeletionUpdate</c>'s lifetime to <c>ViewObjectDuration</c>.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <inheritdoc cref="Modules.IWorldServices.CreateSpecialPowerViewObject"/>
    public void CreateSpecialPowerViewObject(PlayerId owner, int power, Fixed x, Fixed y)
    {
        var info = _templates.SpecialPowers[power];
        // "We don't want a view object at all."
        if (info.ViewObjectRange == Fixed.Zero || info.ViewObjectTicks == 0) return;
        var name = _templates.SpecialPowerViewObject;
        if (name.Length == 0) return;
        if (!_templates.TryGet(name, out var template) || !template.IsInstantiable)
        {
            Emit(SimEventKind.CreationSkipped, EntityId.Invalid, EntityId.Invalid, name, template is null ? CreationSkipReasons.UnknownTemplate : CreationSkipReasons.TemplateNotInstantiable);
            return;
        }
        var setup = new SpawnSetup(false, 0, false, 0, 0, 0, EntityId.Invalid, EntityId.Invalid, Fixed.Zero, EntityId.Invalid,
            DeletionTicks: info.ViewObjectTicks, ShroudRange: info.ViewObjectRange);
        _spawnQueue.Add(new PendingSpawn(Ids.AllocateEntity(), template.Index, owner, x, y, GroundHeightAt(x, y), Fixed.Zero, setup));
    }
}
