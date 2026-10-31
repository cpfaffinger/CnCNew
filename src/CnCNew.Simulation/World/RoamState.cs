using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

/// <summary>Which of the two states the original defines as each other's successors an object is in (<c>AIStates.cpp</c>, <c>defineState</c>).</summary>
internal enum RoamKind : byte
{
    /// <summary><c>AI_WANDER_IN_PLACE</c>: <c>AIWanderInPlaceState</c>.</summary>
    WanderInPlace = 1,

    /// <summary><c>AI_MOVE_AWAY_FROM_REPULSORS</c>: <c>AIMoveAwayFromRepulsorsState</c>.</summary>
    AwayFromRepulsors = 2,
}

/// <summary>
/// The object wanders around a point or runs from a repulsor; it does so until an order replaces the state. An object that does neither has none, so it keeps its serialized form
/// (and hash). <see cref="Timer"/> and <see cref="WaitFrames"/> are <c>m_timer</c> and <c>m_waitFrames</c>: how many logic frames of the original lie between two looks for a repulsor.
/// </summary>
internal sealed class RoamState
{
    public RoamKind Kind;
    public Fixed OriginX;
    public Fixed OriginY;
    public int Timer;
    public int WaitFrames;

    /// <summary><c>AIUpdateInterface::m_repulsor1</c> and <c>m_repulsor2</c>: the repulsor the object runs from and the one before it.</summary>
    public uint Repulsor1;
    public uint Repulsor2;

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("roam");
        writer.Int("kind", (int)Kind);
        writer.Fixed("originX", OriginX);
        writer.Fixed("originY", OriginY);
        writer.Int("timer", Timer);
        writer.Int("waitFrames", WaitFrames);
        if (Repulsor1 != 0) writer.UInt("repulsor1", Repulsor1);
        if (Repulsor2 != 0) writer.UInt("repulsor2", Repulsor2);
        writer.EndObject();
    }

    public static RoamState? Load(IStateReader reader)
    {
        if (!reader.Has("roam")) return null;
        var item = reader.Object("roam");
        return new RoamState
        {
            Kind = (RoamKind)item.Int("kind"),
            OriginX = item.Fixed("originX"),
            OriginY = item.Fixed("originY"),
            Timer = checked((int)item.Int("timer")),
            WaitFrames = checked((int)item.Int("waitFrames")),
            Repulsor1 = item.Has("repulsor1") ? checked((uint)item.UInt("repulsor1")) : 0,
            Repulsor2 = item.Has("repulsor2") ? checked((uint)item.UInt("repulsor2")) : 0,
        };
    }
}
