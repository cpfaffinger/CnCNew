using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

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

    /// <summary>
    /// <c>ThingTemplate::calcTimeToBuild</c>'s energy part: a build time of <paramref name="baseTicks"/> (whole frames of the original) divided by the production speed of the
    /// player's supply ratio (<c>getEnergySupplyRatio</c>: production / consumption, the production itself without consumers, 0 while sabotaged; at most 1): one less the
    /// shortfall times <c>LowEnergyPenaltyModifier</c>, at least <c>MinLowEnergyProductionSpeed</c>, below full supply at most <c>MaxLowEnergyProductionSpeed</c>, and never 0.
    /// A player without production and consumption has a ratio of 0. The frames are truncated as the reference's integer is. Data without any of the three keys (synthetic
    /// test data; the shipped <c>GameData.ini</c> names them) has no penalty - the reference's zero defaults would make every build below full power a hundred times longer.
    /// </summary>
    public long BuildTicksUnderPower(PlayerId player, long baseTicks)
    {
        var rules = _templates.LowEnergy;
        if (rules == LowEnergyRules.Absent) return baseTicks;
        var production = ProducedEnergyOf(player);
        var consumption = EnergyConsumptionOf(player);
        var ratio = consumption == 0 ? Fixed.FromInt(production) : Fixed.FromInt(production) / Fixed.FromInt(consumption);
        if (ratio > Fixed.One) ratio = Fixed.One;
        var speed = Fixed.Max(Fixed.One - (Fixed.One - ratio) * rules.PenaltyModifier, rules.MinProductionSpeed);
        if (ratio < Fixed.One) speed = Fixed.Min(speed, rules.MaxProductionSpeed);
        if (speed <= Fixed.Zero) speed = LowestProductionSpeed;
        if (speed == Fixed.One) return baseTicks;
        return Time.OriginalFrames((Fixed.FromInt(Time.FramesIn(baseTicks)) / speed).Floor());
    }

    /// <summary>"Design won't make the minimum 0, they promise": 0.01.</summary>
    private static readonly Fixed LowestProductionSpeed = Fixed.FromRaw(Fixed.One.Raw / 100);

    /// <inheritdoc cref="IWorldServices.AddRadar"/>
    public void AddRadar(PlayerId player, bool disableProof)
    {
        if (!_players.TryGetValue(player.Value, out var state)) return;
        state.RadarCount++;
        if (disableProof) state.DisableProofRadarCount++;
    }

    /// <inheritdoc cref="IWorldServices.RemoveRadar"/>
    public void RemoveRadar(PlayerId player, bool disableProof)
    {
        if (!_players.TryGetValue(player.Value, out var state)) return;
        state.RadarCount--;
        if (disableProof) state.DisableProofRadarCount--;
    }

    /// <summary><c>Player::hasRadar</c>: a radar the player has, and not switched off by a brown-out unless one of them is disable-proof.</summary>
    public bool HasRadar(PlayerId player)
        => _players.TryGetValue(player.Value, out var state) && (!state.RadarDisabled || state.DisableProofRadarCount != 0) && state.RadarCount > 0;

    /// <summary><c>Energy::getConsumption</c>: what the player's objects consume.</summary>
    public int EnergyConsumptionOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.EnergyConsumption : 0;

    /// <summary>
    /// <c>Player::onPowerBrownOutChange</c>: the player's radar is switched off during a brown-out (unless a disable-proof one is there) and every <c>POWERED</c> object of the
    /// player is underpowered (<c>doPowerDisable</c>); both end with it.
    /// </summary>
    private void OnPowerBrownOutChange(PlayerId player, bool brownOut)
    {
        // disableRadar / enableRadar: the restriction only; a radar the player does not have is not given.
        if (_players.TryGetValue(player.Value, out var state)) state.RadarDisabled = brownOut;
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
