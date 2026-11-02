using System.Diagnostics;
using System.Globalization;

namespace CnCNew.Client.Stride;

/// <summary>
/// --frame-report: how long the frames of a run took and what the long ones did instead of drawing. A person sees a stutter; this says whether it was a model read in the
/// middle of a frame, a texture, a collection or the match itself.
/// </summary>
internal static class FrameReport
{
    private const double LongFrameMilliseconds = 25;
    private const int LongFramesListed = 24;

    public static bool Enabled { get; set; }

    private static readonly List<double> Frames = [];
    private static readonly List<string> LongFrames = [];
    private static double _modelMilliseconds, _textureMilliseconds, _modelTotal, _textureTotal, _modelWorst, _textureWorst;
    private static int _models, _textures, _modelCount, _textureCount, _longCount, _longWithLoading, _longWithCollection;
    private static int _gen0, _gen1, _gen2;

    public static void Preload(int models, double milliseconds)
    {
        if (Enabled) Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"frame-report: {models} models read before the match in {milliseconds:F0} ms"));
        (_modelMilliseconds, _textureMilliseconds, _models, _textures) = (0, 0, 0, 0);
    }

    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static void Model(long start)
    {
        if (!Enabled) return;
        var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _modelMilliseconds += milliseconds;
        _modelWorst = Math.Max(_modelWorst, milliseconds);
        _models++;
    }

    public static void Texture(long start)
    {
        if (!Enabled) return;
        var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _textureMilliseconds += milliseconds;
        _textureWorst = Math.Max(_textureWorst, milliseconds);
        _textures++;
    }

    public static void Frame(double updateMilliseconds, double drawMilliseconds)
    {
        if (!Enabled) return;
        var total = updateMilliseconds + drawMilliseconds;
        Frames.Add(total);
        var (gen0, gen1, gen2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var collected = gen2 > _gen2 ? "gen2" : gen1 > _gen1 ? "gen1" : gen0 > _gen0 ? "gen0" : "-";
        if (total >= LongFrameMilliseconds)
        {
            _longCount++;
            if (_models + _textures > 0) _longWithLoading++;
            if (collected != "-") _longWithCollection++;
            if (LongFrames.Count < LongFramesListed)
                LongFrames.Add(string.Create(CultureInfo.InvariantCulture, $"frame-report: frame {Frames.Count}: {total:F1} ms (update {updateMilliseconds:F1}, draw {drawMilliseconds:F1}), models {_models} in {_modelMilliseconds:F1} ms, textures {_textures} in {_textureMilliseconds:F1} ms, collection {collected}"));
        }

        _modelTotal += _modelMilliseconds;
        _textureTotal += _textureMilliseconds;
        _modelCount += _models;
        _textureCount += _textures;
        (_modelMilliseconds, _textureMilliseconds, _models, _textures) = (0, 0, 0, 0);
        (_gen0, _gen1, _gen2) = (gen0, gen1, gen2);
    }

    public static IEnumerable<string> Finish()
    {
        if (!Enabled || Frames.Count == 0) yield break;
        var sorted = Frames.Order().ToArray();
        double Quantile(double share) => sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * share))];
        yield return string.Create(CultureInfo.InvariantCulture, $"frame-report: {Frames.Count} frames, median {Quantile(0.5):F1} ms, 99 % {Quantile(0.99):F1} ms, longest {sorted[^1]:F1} ms; {_longCount} of at least {LongFrameMilliseconds} ms, {_longWithLoading} of them while loading, {_longWithCollection} with a collection");
        yield return string.Create(CultureInfo.InvariantCulture, $"frame-report: in frames: {_modelCount} models in {_modelTotal:F0} ms (longest {_modelWorst:F1}), {_textureCount} textures in {_textureTotal:F0} ms (longest {_textureWorst:F1})");
        foreach (var line in LongFrames) yield return line;
    }
}
