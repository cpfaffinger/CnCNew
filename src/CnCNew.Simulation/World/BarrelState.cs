using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

/// <summary><c>Weapon::m_curBarrel</c> and <c>m_numShotsForCurBarrel</c> per weapon slot of an object that fires from more than one barrel; null on every other object.</summary>
internal sealed class BarrelState
{
    public readonly int[] Current = [0, 0, 0];
    public readonly int[] ShotsLeft;

    public BarrelState(int shotsPerBarrel) => ShotsLeft = [shotsPerBarrel, shotsPerBarrel, shotsPerBarrel];

    public void Describe(IStateWriter writer)
    {
        writer.BeginArray("barrels");
        for (var slot = 0; slot < Current.Length; slot++)
        {
            writer.BeginObject(null);
            writer.Int("current", Current[slot]);
            writer.Int("shots", ShotsLeft[slot]);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public static BarrelState? Load(IStateReader reader)
    {
        if (!reader.Has("barrels")) return null;
        var state = new BarrelState(1);
        var slot = 0;
        foreach (var item in reader.Array("barrels"))
        {
            if (slot >= state.Current.Length) throw new InvalidDataException("More barrel entries than weapon slots.");
            state.Current[slot] = checked((int)item.Int("current"));
            state.ShotsLeft[slot] = checked((int)item.Int("shots"));
            slot++;
        }
        return state;
    }
}
