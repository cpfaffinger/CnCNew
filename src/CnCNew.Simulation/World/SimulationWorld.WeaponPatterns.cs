using System.Globalization;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// Two things a weapon does across its shots. <c>WeaponTemplate::dealDamageInternal</c>'s historic bonus: <c>HistoricBonusCount</c> hits of one weapon template, anybody's, within
/// <c>HistoricBonusTime</c> and <c>HistoricBonusRadius</c> set off <c>HistoricBonusWeapon</c>: that is how napalm becomes a firestorm. <c>Weapon::privateFireWeapon</c>'s scatter targets: a clip goes
/// out in a fixed pattern, every offset once, in a random order.
/// </summary>
public sealed partial class SimulationWorld
{
    private readonly SortedDictionary<int, List<(long Frame, Fixed X, Fixed Y)>> _historicDamage = [];
    private (Entity Source, Fixed X, Fixed Y)? _scatterShot;

    private void NoteHistoricDamage(Entity? source, WeaponInfo weapon, Fixed x, Fixed y, Fixed z)
    {
        if (!_templates.Weapons.TryGet(weapon.HistoricBonusWeapon, out var bonus) || bonus.Index == weapon.Index) return;
        var frame = Time.FramesIn(_tick);
        if (!_historicDamage.TryGetValue(weapon.Index, out var hits)) _historicDamage[weapon.Index] = hits = [];
        var oldest = frame - weapon.HistoricBonusFrames;
        hits.RemoveAll(hit => hit.Frame < oldest);
        var reach = SquaredReach.Of(weapon.HistoricBonusRadius);
        var count = 0;
        foreach (var hit in hits)
            if (reach.Contains((x - hit.X) * (x - hit.X) + (y - hit.Y) * (y - hit.Y))) count++;
        // "Minus 1 since we include ourselves implicitly."
        if (count < weapon.HistoricBonusCount - 1)
        {
            hits.Add((frame, x, y));
            return;
        }
        // "Clear the list for now to make sure we don't have multiple firestorms."
        hits.Clear();
        _historicDamage.Remove(weapon.Index);
        if (source is not null) FireWeaponAt(source, bonus, null, x, y, z);
    }

    private void DescribeHistoricDamage(IStateWriter writer)
    {
        if (_historicDamage.Count == 0) return;
        writer.BeginArray("historicDamage");
        foreach (var (weapon, hits) in _historicDamage)
            foreach (var (frame, x, y) in hits)
            {
                writer.BeginObject(null);
                writer.Int("weapon", weapon);
                writer.Int("frame", frame);
                writer.Fixed("x", x);
                writer.Fixed("y", y);
                writer.EndObject();
            }
        writer.EndArray();
    }

    private void LoadHistoricDamage(IStateReader state)
    {
        _historicDamage.Clear();
        if (!state.Has("historicDamage")) return;
        foreach (var hit in state.Array("historicDamage"))
        {
            var weapon = checked((int)hit.Int("weapon"));
            if (!_historicDamage.TryGetValue(weapon, out var hits)) _historicDamage[weapon] = hits = [];
            hits.Add((hit.Int("frame"), hit.Fixed("x"), hit.Fixed("y")));
        }
    }

    /// <summary>The offset of this shot from the pattern of its weapon, for the <see cref="FireWeaponAt"/> that follows. A clip that ran dry fills the pattern again (<c>rebuildScatterTargets</c>).</summary>
    private void PickScatterTarget(Entity source, int slot, WeaponInfo weapon, bool emptied)
    {
        if (weapon.ScatterTargets.IsEmpty) return;
        if (source.ScatterTargetsUnused is null)
        {
            var set = ActiveWeaponSet(source)!;
            source.ScatterTargetsUnused = [..Enumerable.Range(0, WeaponSlot.Count).Select(index => set.HasWeapon(index) ? Enumerable.Range(0, _templates.Weapons[set.Weapons[index]].ScatterTargets.Length).ToList() : [])];
        }
        var unused = source.ScatterTargetsUnused[slot];
        if (unused.Count > 0)
        {
            var pick = LogicRandom.Range(0, unused.Count - 1);
            var (x, y) = weapon.ScatterTargets[unused[pick]];
            _scatterShot = (source, x * weapon.ScatterTargetScalar, y * weapon.ScatterTargetScalar);
            // "To erase from a vector, put the last on the one you used and pop the back."
            unused[pick] = unused[^1];
            unused.RemoveAt(unused.Count - 1);
        }
        if (emptied)
        {
            unused.Clear();
            unused.AddRange(Enumerable.Range(0, weapon.ScatterTargets.Length));
        }
    }

    /// <summary>"If I have a set scatter pattern, I need to offset the target": the shot goes to a place on the ground beside the victim.</summary>
    private bool TakeScatterTarget(Entity source, ref Entity? victim, ref Fixed x, ref Fixed y, ref Fixed z)
    {
        if (_scatterShot is not { } shot || shot.Source != source) return false;
        _scatterShot = null;
        if (victim is not null) (x, y, _) = victim.FrameEnd(_tick);
        victim = null;
        x += shot.X;
        y += shot.Y;
        z = GroundHeightAt(x, y);
        return true;
    }
}
