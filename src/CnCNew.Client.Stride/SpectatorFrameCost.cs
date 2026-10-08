using System.Globalization;
using CnCNew.Client.Stride.Layers.Objects;
using CnCNew.Game.Live;

namespace CnCNew.Client.Stride;

/// <summary>
/// What a frame costs the spectator at a player's zoom and far out (docs/SPECTATOR-MODE.md §7): the CPU time of the client's update (simulation, presentation, layer
/// refresh) and of its draw (scene submission and HUD), with the number of objects and terrain chunks in view. It is the process's own time on this machine, measured with
/// a stopwatch around the two calls; what the graphics card needs after the commands are handed over is not in it. The median is the figure to read: single frames that
/// load a model or simulate several ticks at once pull the mean up.
/// </summary>
internal sealed class SpectatorFrameCost
{
    private sealed class Bucket
    {
        public readonly List<double> Update = [];
        public readonly List<double> Draw = [];
        public long Drawn, Culled, Triangles, Chunks;
    }

    private readonly Bucket[] _buckets = [new(), new()];

    public void Note(bool farZoom, double updateMilliseconds, double drawMilliseconds, ObjectLayerStats objects, LiveGame game)
    {
        var bucket = _buckets[farZoom ? 1 : 0];
        bucket.Update.Add(updateMilliseconds);
        bucket.Draw.Add(drawMilliseconds);
        bucket.Drawn += objects.Drawn;
        bucket.Culled += objects.Culled;
        bucket.Triangles += objects.Triangles;
        bucket.Chunks += game.Camera.VisibleChunks(game.TerrainModel).Count;
    }

    public IReadOnlyList<string> Report()
    {
        var lines = new List<string>();
        for (var index = 0; index < _buckets.Length; index++)
        {
            var bucket = _buckets[index];
            var name = index == 0 ? "near" : "far";
            var frames = bucket.Update.Count;
            if (frames == 0)
            {
                lines.Add($"spectator frame cost {name}: frames=0");
                continue;
            }
            bucket.Update.Sort();
            bucket.Draw.Sort();
            static double At(List<double> sorted, double share) => sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * share))];
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"spectator frame cost {name}: frames={frames}, updateMedianMs={At(bucket.Update, 0.5):F2}, drawMedianMs={At(bucket.Draw, 0.5):F2}, update95Ms={At(bucket.Update, 0.95):F2}, draw95Ms={At(bucket.Draw, 0.95):F2}, updateMeanMs={bucket.Update.Sum() / frames:F2}, drawMeanMs={bucket.Draw.Sum() / frames:F2}, objectsDrawn={bucket.Drawn / frames}, objectsCulled={bucket.Culled / frames}, triangles={bucket.Triangles / frames}, terrainChunks={bucket.Chunks / frames}"));
        }
        return lines;
    }
}
