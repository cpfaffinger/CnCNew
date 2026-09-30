using System.Globalization;

namespace CnCNew.Client.Stride.Layers.Fx;

/// <summary>
/// What the effect layer did in one rendered frame. Everything the layer cannot draw is counted here (<c>Skipped*</c>, <c>NotDrawn*</c>),
/// never dropped silently. <see cref="CpuMs"/> covers building, run placement and the vertex upload; <see cref="CaptureMs"/> is the
/// presentation side (<c>FxExecutor.Capture</c>) and is reported separately because it is not part of this layer.
/// </summary>
internal readonly record struct FxStats(
    int ParticlesLive,
    int ParticlesCulled,
    int ParticlesDrawn,
    int Streaks,
    int Tracers,
    int Decals,
    int Batches,
    int Runs,
    int DrawCalls,
    int Quads,
    int UploadBytes,
    int DroppedQuads,
    int SkippedDrawable,
    int SkippedSmudge,
    int SkippedUnusable,
    int SkippedShortStreaks,
    int DeferredItems,
    int NotDrawnLights,
    int NotDrawnRays,
    double CpuMs,
    double CaptureMs);

/// <summary>Accumulates <see cref="FxStats"/> over a run for the one-line summary printed in smoke mode.</summary>
internal sealed class FxStatsTotals
{
    private double _cpuSum;
    private double _captureSum;

    public long Frames { get; private set; }

    public int PeakParticlesLive { get; private set; }

    public int PeakParticlesDrawn { get; private set; }

    public int PeakDrawCalls { get; private set; }

    public int PeakQuads { get; private set; }

    public int PeakUploadBytes { get; private set; }

    public double MaxCpuMs { get; private set; }

    public double MaxCaptureMs { get; private set; }

    public long DroppedQuads { get; private set; }

    public long SkippedDrawable { get; private set; }

    public long SkippedSmudge { get; private set; }

    public long SkippedUnusable { get; private set; }

    public long SkippedShortStreaks { get; private set; }

    public long DeferredItems { get; private set; }

    public long NotDrawnLights { get; private set; }

    public long NotDrawnRays { get; private set; }

    public long StreaksDrawn { get; private set; }

    public long TracersDrawn { get; private set; }

    public long DecalsDrawn { get; private set; }

    public int Materials { get; set; }

    public int PoolSlots { get; set; }

    public void Add(in FxStats stats)
    {
        Frames++;
        _cpuSum += stats.CpuMs;
        _captureSum += stats.CaptureMs;
        PeakParticlesLive = Math.Max(PeakParticlesLive, stats.ParticlesLive);
        PeakParticlesDrawn = Math.Max(PeakParticlesDrawn, stats.ParticlesDrawn);
        PeakDrawCalls = Math.Max(PeakDrawCalls, stats.DrawCalls);
        PeakQuads = Math.Max(PeakQuads, stats.Quads);
        PeakUploadBytes = Math.Max(PeakUploadBytes, stats.UploadBytes);
        MaxCpuMs = Math.Max(MaxCpuMs, stats.CpuMs);
        MaxCaptureMs = Math.Max(MaxCaptureMs, stats.CaptureMs);
        DroppedQuads += stats.DroppedQuads;
        SkippedDrawable += stats.SkippedDrawable;
        SkippedSmudge += stats.SkippedSmudge;
        SkippedUnusable += stats.SkippedUnusable;
        SkippedShortStreaks += stats.SkippedShortStreaks;
        DeferredItems += stats.DeferredItems;
        NotDrawnLights += stats.NotDrawnLights;
        NotDrawnRays += stats.NotDrawnRays;
        StreaksDrawn += stats.Streaks;
        TracersDrawn += stats.Tracers;
        DecalsDrawn += stats.Decals;
    }

    public double AverageCpuMs => Frames == 0 ? 0 : _cpuSum / Frames;

    public double AverageCaptureMs => Frames == 0 ? 0 : _captureSum / Frames;

    public string Format(in FxStats last)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(c,
            $"fx stats: frames={Frames} | last: live={last.ParticlesLive} drawn={last.ParticlesDrawn} culled={last.ParticlesCulled} streaks={last.Streaks} "
            + $"tracers={last.Tracers} decals={last.Decals} batches={last.Batches} runs={last.Runs} draw-calls={last.DrawCalls} quads={last.Quads} "
            + $"upload-bytes={last.UploadBytes} cpu-ms={last.CpuMs:F3} | peak: live={PeakParticlesLive} drawn={PeakParticlesDrawn} "
            + $"draw-calls={PeakDrawCalls} quads={PeakQuads} upload-bytes={PeakUploadBytes} | cpu-ms avg={AverageCpuMs:F3} max={MaxCpuMs:F3} "
            + $"capture-ms avg={AverageCaptureMs:F3} max={MaxCaptureMs:F3} | materials={Materials} pool-slots={PoolSlots} | "
            + $"totals: streaks={StreaksDrawn} tracers={TracersDrawn} decals={DecalsDrawn} dropped-quads={DroppedQuads} deferred-items={DeferredItems} | "
            + $"skipped: drawable={SkippedDrawable} smudge={SkippedSmudge} unusable={SkippedUnusable} short-streaks={SkippedShortStreaks} | "
            + $"not drawn: lights={NotDrawnLights} rays={NotDrawnRays}");
    }
}
