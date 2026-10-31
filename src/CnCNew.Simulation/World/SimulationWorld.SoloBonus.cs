using CnCNew.Data.Definitions;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

/// <summary>
/// The solo bonuses of a single player game (<c>Player::friend_applyDifficultyBonusesForObject</c>, D-6; <c>docs/SIMULATION-CORE.md</c> §67): by the chosen difficulty and by who owns
/// it, an object takes a factor on its health and a weapon bonus condition. A skirmish has none; a script may switch them off and on for every object.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>The difficulty of a single player game (0 easy, 1 normal, 2 hard), or -1 in any other game.</summary>
    private int _soloDifficulty = -1;

    /// <summary><c>ScriptEngine::m_objectsShouldReceiveDifficultyBonus</c>.</summary>
    private bool _objectsReceiveDifficultyBonus = true;

    public int SoloDifficulty => _soloDifficulty;

    /// <summary>Called before the first object of a single player game exists.</summary>
    public void BeginSinglePlayer(int difficulty)
    {
        if (difficulty is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(difficulty));
        _soloDifficulty = difficulty;
    }

    /// <summary><c>ScriptActions::doEnableOrDisableObjectDifficultyBonuses</c>.</summary>
    public void SetObjectsReceiveDifficultyBonus(bool receive)
    {
        foreach (var entity in _entities.Values.OrderBy(value => value.Id.Value)) SetReceivingDifficultyBonus(entity, receive);
        _objectsReceiveDifficultyBonus = receive;
    }

    private void ApplySoloBonus(Entity entity)
    {
        if (_soloDifficulty >= 0 && _objectsReceiveDifficultyBonus) SetReceivingDifficultyBonus(entity, true);
    }

    /// <summary><c>Object::setReceivingDifficultyBonus</c>. As there, what is taken back is what the owner of the moment gives, whoever owned the object when it was given.</summary>
    private void SetReceivingDifficultyBonus(Entity entity, bool receive)
    {
        if (receive == entity.ReceivingDifficultyBonus || _soloDifficulty < 0) return;
        entity.ReceivingDifficultyBonus = receive;
        var computer = !_players.TryGetValue(entity.Owner.Value, out var owner) || owner.Id.IsSystem || owner.IsComputer;
        var factor = _templates.SoloHealthBonus[(computer ? 3 : 0) + _soloDifficulty];
        if (factor != Numerics.Fixed.One && entity.Body is IMaxHealthAdjustable body)
        {
            var maxHealth = entity.Body.MaxHealth;
            body.AdjustMaxHealth(this, (receive ? maxHealth * factor : maxHealth / factor) - maxHealth, "PRESERVE_RATIO");
        }
        var bit = 1u << (SoloHumanEasy + (computer ? 3 : 0) + _soloDifficulty);
        entity.WeaponBonusConditions = receive ? entity.WeaponBonusConditions | bit : entity.WeaponBonusConditions & ~bit;
        MarkDirty(entity);
    }

    private static readonly int SoloHumanEasy = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("SOLO_HUMAN_EASY", out var index) ? index : throw new InvalidOperationException("SOLO_HUMAN_EASY");

    private void DescribeSoloBonus(IStateWriter writer)
    {
        if (_soloDifficulty >= 0) writer.Int("soloDifficulty", _soloDifficulty);
        if (!_objectsReceiveDifficultyBonus) writer.Bool("noDifficultyBonus", true);
    }

    private void LoadSoloBonus(IStateReader reader)
    {
        _soloDifficulty = reader.Has("soloDifficulty") ? checked((int)reader.Int("soloDifficulty")) : -1;
        _objectsReceiveDifficultyBonus = !reader.Has("noDifficultyBonus");
    }
}
