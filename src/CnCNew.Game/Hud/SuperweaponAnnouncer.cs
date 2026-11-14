using CnCNew.Presentation.Fx;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Hud;

/// <summary>
/// The announcer's two superweapon events that belong to the objects and not to a launch (the launch is <see cref="SoundCueDeriver"/>'s):
/// <c>EVA_SuperweaponDetected_*</c> when a structure that carries a particle cannon, a neutron missile or a Scud Storm is finished
/// (<c>Player::onStructureConstructionComplete</c>), and <c>EVA_SuperweaponReady_*</c> when such a power turns ready (<c>InGameUI::draw</c>, the timer loop: only where the timer is listed, so not for
/// an object under construction, one a script hid, or a power whose science the owner lacks; once per period of readiness, never in frame 0; a power with a shared timer once per player).
/// Both are asked for the owner's player in the own form; the director turns it into the allied or enemy form for a viewer who is not the owner, as for a launch.
/// </summary>
public sealed class SuperweaponAnnouncer
{
    private readonly HashSet<(uint Entity, int Power)> _readyPlayed = [];
    private readonly HashSet<(int Player, int Power)> _sharedSeen = [];
    private readonly HashSet<(uint Entity, int Power)> _seen = [];

    /// <summary>Objects a mission script hid from the timer list (<c>NAMED_HIDE_SPECIAL_POWER_DISPLAY</c>); they are not looked at.</summary>
    public Func<EntityId, bool>? HiddenByScript { get; set; }

    private static string? Weapon(string type)
    {
        var name = SoundCueDeriver.SuperweaponOf(type);
        return name is "ParticleCannon" or "Nuke" or "ScudStorm" ? name : null;
    }

    /// <summary>Finished structures of this tick's events: one "detected" cue per superweapon power they carry.</summary>
    public void Observe(IEnumerable<SimEvent> events, SimulationWorld world, List<SoundCue> output)
    {
        foreach (var item in events)
        {
            if (item.Kind != SimEventKind.ConstructionCompleted || world.FindEntity(item.Entity) is not { } structure) continue;
            foreach (var module in structure.Modules.OfType<ISpecialPowerModule>())
            {
                if (module.PowerIndex < 0 || Weapon(world.SpecialPowers[module.PowerIndex].Type) is not { } weapon) continue;
                output.Add(new SoundCue(world.Tick, $"SuperweaponDetected_{SoundCueDeriver.OwnForm}_{weapon}", SoundCueKind.Eva, null, structure.Id, structure.Owner, 1f, 2,
                    "Player::onStructureConstructionComplete EVA_SuperweaponDetected"));
            }
        }
    }

    /// <summary>The timer loop of the moment (call once per original frame): a cue for every power that turned ready since the last call.</summary>
    public void Update(SimulationWorld world, List<SoundCue> output)
    {
        _sharedSeen.Clear();
        _seen.Clear();
        foreach (var entity in world.Entities.OrderBy(entity => entity.Id.Value))
        {
            if (entity.DestroyRequested || entity.EffectivelyDead || entity.HasStatus(ObjectStatusBits.UnderConstruction)) continue;
            foreach (var module in entity.Modules.OfType<ISpecialPowerModule>())
            {
                if (module.PowerIndex < 0) continue;
                var power = world.SpecialPowers[module.PowerIndex];
                if (!power.PublicTimer) continue;
                if (power.RequiredScience >= 0 && !world.PlayerSciences(entity.Owner).Has(power.RequiredScience)) continue;
                if (HiddenByScript?.Invoke(entity.Id) == true) continue;
                var key = (entity.Id.Value, power.Index);
                _seen.Add(key);
                if (power.SharedSynced && !_sharedSeen.Add((entity.Owner.Value, power.Index))) continue;
                if (module.IsReady(world))
                {
                    if (_readyPlayed.Add(key) && world.Tick > 0 && Weapon(power.Type) is { } weapon)
                        output.Add(new SoundCue(world.Tick, $"SuperweaponReady_{SoundCueDeriver.OwnForm}_{weapon}", SoundCueKind.Eva, null, entity.Id, entity.Owner, 1f, 2,
                            "InGameUI::draw EVA_SuperweaponReady"));
                }
                else _readyPlayed.Remove(key);
            }
        }
        _readyPlayed.RemoveWhere(key => !_seen.Contains(key));
    }
}
