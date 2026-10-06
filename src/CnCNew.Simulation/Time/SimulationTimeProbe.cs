using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Time;

/// <summary>What a <see cref="SimulationTimeProbe"/> counts.</summary>
public enum TimeProbeKind
{
    /// <summary>A per-tick amount formed at run time came out as zero although its input was not (<c>PerSecondToPerTick</c>, <c>PerOriginalFrameToPerTick</c>, <c>SharePerTick</c>).</summary>
    RunTimeRoundedToZero,
    /// <summary><see cref="FrameGrid.AtLeastOneFrame"/> raised a duration to one logic frame of the original.</summary>
    MinimumOneFrame,
    /// <summary>A duration that should lie on the grid of the original did not (a remainder below one frame was cut off).</summary>
    OffGrid,
    /// <summary><see cref="FrameGrid.FloorToFrame"/> or <see cref="FrameGrid.CeilingToFrame"/> changed its argument (expected between two frame boundaries).</summary>
    RoundedToFrame,
}

/// <summary>One place in the source that a counter belongs to.</summary>
public readonly record struct TimeProbeSite(TimeProbeKind Kind, string Function, string File, int Line);

/// <summary>How the decimal conversions of one function came out while a catalog was built.</summary>
public sealed class TimeProbeQuantisation
{
    public long Calls { get; internal set; }
    public long NonZero { get; internal set; }
    /// <summary>Non-zero exact value, zero result.</summary>
    public long RoundedToZero { get; internal set; }
    /// <summary>Relative error of the result against the exact value above 1 % (includes the ones above 10 % and the zeros).</summary>
    public long AboveOnePercent { get; internal set; }
    /// <summary>Relative error above 10 % (includes the zeros).</summary>
    public long AboveTenPercent { get; internal set; }
    public decimal WorstRelativeError { get; internal set; }
    public decimal WorstExact { get; internal set; }
}

/// <summary>
/// Diagnostic counters of a <see cref="SimulationTime"/>: how often a conversion lost what it was given. <b>Not state</b> – nothing here is read by the
/// simulation, written to a snapshot or hashed, and a time service without a probe (the normal case) does no more than compare a field with null.
/// A tool attaches one to the time service of a rate (<see cref="SimulationTime.Probe"/>), runs, and reads the counters; the time service of a rate
/// is shared by every world of that rate in the process, so a probe counts all of them and is meant for one run at a time.
/// </summary>
public sealed class SimulationTimeProbe
{
    private readonly Dictionary<TimeProbeSite, long> _sites = [];
    private readonly Dictionary<(TimeProbeKind Kind, string Function), long> _calls = [];
    private readonly Dictionary<string, TimeProbeQuantisation> _quantisation = new(StringComparer.Ordinal);

    /// <summary>Hits per place in the source. Unordered: a reader sorts.</summary>
    public IReadOnlyDictionary<TimeProbeSite, long> Sites => _sites;

    /// <summary>Calls per function, whether or not they hit (the denominator of <see cref="Sites"/>).</summary>
    public IReadOnlyDictionary<(TimeProbeKind Kind, string Function), long> Calls => _calls;

    /// <summary>Decimal conversions per function (catalog build).</summary>
    public IReadOnlyDictionary<string, TimeProbeQuantisation> Quantisation => _quantisation;

    internal void Call(TimeProbeKind kind, string function) => _calls[(kind, function)] = _calls.GetValueOrDefault((kind, function)) + 1;

    internal void Hit(TimeProbeKind kind, string function, string file, int line)
    {
        var site = new TimeProbeSite(kind, function, file, line);
        _sites[site] = _sites.GetValueOrDefault(site) + 1;
    }

    internal void RunTime(string function, Fixed input, Fixed result, string file, int line)
    {
        Call(TimeProbeKind.RunTimeRoundedToZero, function);
        if (input != Fixed.Zero && result == Fixed.Zero) Hit(TimeProbeKind.RunTimeRoundedToZero, function, file, line);
    }

    internal void Quantised(string function, decimal exact, Fixed result)
    {
        if (!_quantisation.TryGetValue(function, out var entry)) _quantisation[function] = entry = new TimeProbeQuantisation();
        entry.Calls++;
        if (exact == 0m) return;
        entry.NonZero++;
        if (result == Fixed.Zero) entry.RoundedToZero++;
        var error = Math.Abs((result.ToDecimal() - exact) / exact);
        if (error > OnePercent) entry.AboveOnePercent++;
        if (error > TenPercent) entry.AboveTenPercent++;
        if (error > entry.WorstRelativeError) (entry.WorstRelativeError, entry.WorstExact) = (error, exact);
    }

    private const decimal OnePercent = 0.01m;
    private const decimal TenPercent = 0.1m;
}
