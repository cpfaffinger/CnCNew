namespace CnCNew.Simulation.Time;

/// <summary>
/// Tick adjacency. Some literals in the simulation do not mean time but order: "in the tick after this one", "what the previous tick left behind",
/// "two steps later, after the neighbour had its turn". They describe the sequence of simulation steps and are the same at every tick rate, so they
/// are never scaled with the rate – unlike every duration, which goes through <see cref="SimulationTime"/>.
/// <para>
/// Writing them with these names (instead of a bare <c>1</c>) is what tells the two kinds apart, for the reader and for the source rules:
/// <c>now + TickOrder.Next</c> is order, <c>now + world.Time.OriginalFrames(1)</c> would be a thirtieth of a second.
/// </para>
/// </summary>
public static class TickOrder
{
    /// <summary>The distance from one tick to the next: <c>tick + Next</c> is the following step, <c>tick - Next</c> the preceding one.</summary>
    public const long Next = 1;
}
