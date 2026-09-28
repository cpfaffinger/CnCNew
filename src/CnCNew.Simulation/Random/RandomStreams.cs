using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Random;

/// <summary>
/// The named random streams of a world.
/// <list type="bullet">
/// <item><description><c>logic</c>: the only stream simulation code may draw from; part of the tick hash.</description></item>
/// <item><description><c>cosmetic</c> (the reference's "client" stream) and <c>audio</c>: for the presentation layer only.
/// They are saved in snapshots but excluded from the tick hash, so effects can never desynchronise a replay.</description></item>
/// </list>
/// Seed derivation follows <c>InitRandom(seed)</c> of the reference: every stream is seeded with the same world seed through
/// <see cref="SageRandom.Seed"/>; the streams differ only in their independently advancing state. There is no shared or
/// ambient generator anywhere in the simulation.
/// </summary>
public sealed class RandomStreams
{
    public RandomStreams(uint seed)
    {
        Seed = seed;
        Logic = new SageRandom(seed);
        Cosmetic = new SageRandom(seed);
        Audio = new SageRandom(seed);
    }

    public uint Seed { get; }
    public SageRandom Logic { get; }
    public SageRandom Cosmetic { get; }
    public SageRandom Audio { get; }

    public void DescribeLogic(IStateWriter writer) => Logic.Describe(writer, "logic");

    public void DescribePresentation(IStateWriter writer)
    {
        Cosmetic.Describe(writer, "cosmetic");
        Audio.Describe(writer, "audio");
    }

    public void LoadLogic(IStateReader reader) => Logic.Load(reader, "logic");

    public void LoadPresentation(IStateReader reader)
    {
        Cosmetic.Load(reader, "cosmetic");
        Audio.Load(reader, "audio");
    }
}
