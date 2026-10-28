using CnCNew.Simulation.Ids;

namespace CnCNew.Simulation.World;

/// <summary>
/// <c>Energy</c> (<c>Energy.cpp</c>) and <c>Player::onPowerBrownOutChange</c>: a player's energy keeps production and consumption apart. Every change of either asks whether the
/// player has sufficient power and puts <c>DISABLED_UNDERPOWERED</c> on every <c>POWERED</c> object of the player (or takes it off) - a brown-out stops a Patriot's AI, a
/// Gattling cannon's turret, a superweapon's timer; a sabotaged power plant counts as no production until its time is over.
/// </summary>
public sealed partial class SimulationWorld
{
    private const string PoweredKind = "POWERED";

    /// <summary>Set while a snapshot rebuilds the world: the objects' underpowered flags are restored, not recomputed by their registration.</summary>
    private bool _restoringEnergy;

    /// <summary>The players whose <c>PowerSabotagedUntil</c> is set (derived; counted again when a snapshot is restored).</summary>
    private int _runningPowerSabotages;

    /// <summary><c>Energy::objectEnteringInfluence</c> (<paramref name="entering"/>) and <c>objectLeavingInfluence</c>: the template's <c>EnergyProduction</c>, a consumption when negative.</summary>
    private void ApplyTemplateEnergy(PlayerId player, int energy, bool entering)
    {
        if (energy == 0 || !_players.TryGetValue(player.Value, out var state)) return;
        var sign = entering ? 1 : -1;
        state.EnergyProduction += sign * energy;
        if (energy < 0) state.EnergyConsumption -= sign * energy;
        if (!_restoringEnergy) OnPowerBrownOutChange(player, !HasSufficientPower(player));
    }

    /// <summary><c>Energy::hasSufficientPower</c>: production covers consumption, and the power is not sabotaged.</summary>
    public bool HasSufficientPower(PlayerId player)
    {
        if (!_players.TryGetValue(player.Value, out var state)) return true;
        if (_tick < state.PowerSabotagedUntil) return false;
        return state.EnergyProduction >= 0;
    }

    /// <summary><c>Energy::getProduction</c>: what the player's objects produce; nothing while the power is sabotaged.</summary>
    public int ProducedEnergyOf(PlayerId player)
        => !_players.TryGetValue(player.Value, out var state) || _tick < state.PowerSabotagedUntil ? 0 : state.EnergyProduction + state.EnergyConsumption;

    /// <summary><c>Energy::getConsumption</c>: what the player's objects consume.</summary>
    public int EnergyConsumptionOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.EnergyConsumption : 0;

    /// <summary>
    /// <c>Player::onPowerBrownOutChange</c>: every <c>POWERED</c> object of the player is underpowered during a brown-out and no longer after it (<c>doPowerDisable</c>). The radar part
    /// waits for the player's radar.
    /// </summary>
    private void OnPowerBrownOutChange(PlayerId player, bool brownOut)
    {
        foreach (var entity in EntitiesOwnedBy(player).AsSpan())
        {
            if (entity.Underpowered == brownOut || !entity.Template.IsKindOf(PoweredKind)) continue;
            entity.Underpowered = brownOut;
            MarkDirty(entity);
        }
    }

    /// <summary><c>Player::update</c>: a sabotage whose frame has passed ends, and the brown-out follows the power again.</summary>
    private void EndPowerSabotages()
    {
        // The players are looked at only while a sabotage runs: an idle tick allocates nothing.
        if (_runningPowerSabotages == 0) return;
        foreach (var (id, state) in _players)
        {
            if (state.PowerSabotagedUntil == 0 || Time.FramesIn(_tick) <= Time.FramesIn(state.PowerSabotagedUntil)) continue;
            state.PowerSabotagedUntil = 0;
            _runningPowerSabotages--;
            OnPowerBrownOutChange(new PlayerId(id), !HasSufficientPower(new PlayerId(id)));
        }
    }
}
