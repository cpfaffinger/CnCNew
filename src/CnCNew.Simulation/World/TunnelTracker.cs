using CnCNew.Simulation.Ids;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

/// <summary>
/// <c>TunnelTracker</c> (<c>TunnelTracker.cpp</c>): one per player. Every registered tunnel of the player shares one contain list, so an
/// object that entered one tunnel can leave through any other. Kept in registration/entry order, like the reference's
/// <c>m_tunnelIDs</c>/<c>m_containList</c>; the entry tick of every rider drives the healing (<c>getContainedByFrame</c>). The world owns the
/// tracker, hashes it with the player and writes it into snapshots. Not modeled: the nemesis bookkeeping (<c>updateNemesis</c>) that steers the
/// tunnel defenders' AI.
/// </summary>
public sealed class TunnelTracker
{
    private readonly List<EntityId> _tunnels = [];
    private readonly List<EntityId> _riders = [];
    private readonly List<long> _since = [];

    /// <summary>Registered tunnels in registration order (<c>m_tunnelIDs</c>).</summary>
    public IReadOnlyList<EntityId> Tunnels => _tunnels;

    /// <summary>Objects inside the network in entry order (<c>m_containList</c>).</summary>
    public IReadOnlyList<EntityId> Riders => _riders;

    public int TunnelCount => _tunnels.Count;
    public int ContainCount => _riders.Count;

    public bool IsRegistered(EntityId tunnel) => _tunnels.Contains(tunnel);
    public bool Contains(EntityId rider) => _riders.Contains(rider);

    public long EnteredAt(EntityId rider)
    {
        var index = _riders.IndexOf(rider);
        return index < 0 ? -1 : _since[index];
    }

    /// <summary><c>onTunnelCreated</c>.</summary>
    internal void AddTunnel(EntityId tunnel)
    {
        if (!_tunnels.Contains(tunnel)) _tunnels.Add(tunnel);
    }

    /// <summary><c>onTunnelDestroyed</c>, list part: true when it was the last tunnel (the caller caves the network in).</summary>
    internal bool RemoveTunnel(EntityId tunnel)
    {
        _tunnels.Remove(tunnel);
        return _tunnels.Count == 0;
    }

    /// <summary><c>addToContainList</c>.</summary>
    internal void AddRider(EntityId rider, long tick)
    {
        _riders.Add(rider);
        _since.Add(tick);
    }

    /// <summary><c>removeFromContain</c>.</summary>
    internal bool RemoveRider(EntityId rider)
    {
        var index = _riders.IndexOf(rider);
        if (index < 0) return false;
        _riders.RemoveAt(index);
        _since.RemoveAt(index);
        return true;
    }

    internal void Clear()
    {
        _riders.Clear();
        _since.Clear();
    }

    internal void Describe(IStateWriter writer)
    {
        writer.BeginArray("tunnels");
        foreach (var tunnel in _tunnels)
        {
            writer.BeginObject(null);
            writer.UInt("id", tunnel.Value);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("tunnelRiders");
        for (var index = 0; index < _riders.Count; index++)
        {
            writer.BeginObject(null);
            writer.UInt("id", _riders[index].Value);
            writer.Int("since", _since[index]);
            writer.EndObject();
        }
        writer.EndArray();
    }

    internal void Load(IStateReader reader)
    {
        _tunnels.Clear();
        Clear();
        foreach (var item in reader.Array("tunnels")) _tunnels.Add(new EntityId(checked((uint)item.UInt("id"))));
        foreach (var item in reader.Array("tunnelRiders")) AddRider(new EntityId(checked((uint)item.UInt("id"))), item.Int("since"));
    }
}
