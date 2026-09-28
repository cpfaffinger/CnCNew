using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Random;

/// <summary>
/// Port of the additive carry generator of <c>RandomValue.cpp</c> (<c>randomValue</c>, <c>seedRandom</c>,
/// <c>GetGameLogicRandomValue</c>): six 32-bit words, add-with-carry across the words, then a carry-propagating increment
/// of the low word. Algorithm and constants are taken over unchanged so that golden values equal the original; the golden
/// sequences in the tests were computed by an independent transcription (see docs/SIMULATION-CORE.md).
/// <para>
/// Deviation: the original real-valued variant multiplies by a binary32 factor; <see cref="Range(Fixed,Fixed)"/> uses the
/// exact rational <c>lo + (hi - lo) * r / (2^32 - 1)</c> in fixed point instead (never exceeds <c>hi</c>).
/// </para>
/// </summary>
public sealed class SageRandom
{
    private static readonly string[] WordNames = ["w0", "w1", "w2", "w3", "w4", "w5"];
    private readonly uint[] _seed = new uint[6];

    public SageRandom(uint seed) => Seed(seed);

    public void Seed(uint seed)
    {
        unchecked
        {
            var ax = seed;
            ax += 0xf22d0e56;
            _seed[0] = ax;
            ax += 0x883126e9 - 0xf22d0e56;
            _seed[1] = ax;
            ax += 0xc624dd2f - 0x883126e9;
            _seed[2] = ax;
            ax += 0x0702c49c - 0xc624dd2f;
            _seed[3] = ax;
            ax += 0x9e353f7d - 0x0702c49c;
            _seed[4] = ax;
            ax += 0x6fdf3b64 - 0x9e353f7d;
            _seed[5] = ax;
        }
    }

    /// <summary>Next raw 32-bit value (<c>randomValue</c>).</summary>
    public uint NextUInt32()
    {
        unchecked
        {
            uint carry = 0;
            var ax = Add(_seed[5], _seed[4], ref carry);
            _seed[4] = ax;
            ax = Add(ax, _seed[3], ref carry);
            _seed[3] = ax;
            ax = Add(ax, _seed[2], ref carry);
            _seed[2] = ax;
            ax = Add(ax, _seed[1], ref carry);
            _seed[1] = ax;
            ax = Add(ax, _seed[0], ref carry);
            _seed[0] = ax;

            if (++_seed[5] == 0)
            {
                if (++_seed[4] == 0)
                {
                    if (++_seed[3] == 0)
                    {
                        if (++_seed[2] == 0)
                        {
                            if (++_seed[1] == 0)
                            {
                                ++_seed[0];
                                ++ax;
                            }
                        }
                    }
                }
            }

            return ax;
        }
    }

    /// <summary>Inclusive integer range (<c>GetGameLogicRandomValue(lo, hi)</c>), including its wrap-around behaviour.</summary>
    public int Range(int lo, int hi)
    {
        unchecked
        {
            var delta = (uint)(hi - lo + 1);
            if (delta == 0) return hi;
            return (int)(NextUInt32() % delta) + lo;
        }
    }

    /// <summary>Inclusive fixed-point range; returns <paramref name="hi"/> without drawing when the range is empty.</summary>
    public Fixed Range(Fixed lo, Fixed hi)
    {
        var delta = hi - lo;
        if (delta <= Fixed.Zero) return hi;
        var draw = NextUInt32();
        var scaled = (Int128)delta.Raw * draw / uint.MaxValue;
        return Fixed.FromRaw(checked(lo.Raw + (long)scaled));
    }

    public void CopyState(Span<uint> destination) => _seed.CopyTo(destination);

    public void SetState(ReadOnlySpan<uint> source)
    {
        if (source.Length != 6) throw new ArgumentException("A generator state has six words.", nameof(source));
        source.CopyTo(_seed);
    }

    public void Describe(IStateWriter writer, string name)
    {
        writer.BeginObject(name);
        for (var index = 0; index < 6; index++) writer.UInt(WordNames[index], _seed[index]);
        writer.EndObject();
    }

    public void Load(IStateReader reader, string name)
    {
        var state = reader.Object(name);
        for (var index = 0; index < 6; index++) _seed[index] = checked((uint)state.UInt(WordNames[index]));
    }

    // ADC(SUM, A, B, C): SUM = A + B + C; C = (SUM < A) || (SUM < B)
    private static uint Add(uint a, uint b, ref uint carry)
    {
        unchecked
        {
            var sum = a + b + carry;
            carry = sum < a || sum < b ? 1u : 0u;
            return sum;
        }
    }
}
