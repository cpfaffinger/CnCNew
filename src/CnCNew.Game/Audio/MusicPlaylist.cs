using System.Collections.Immutable;
using CnCNew.Audio.Catalog;
using CnCNew.Audio.Selection;

namespace CnCNew.Game.Audio;

/// <summary>
/// The in-game track order of a side: every <c>Game_&lt;SIDE&gt;_NN</c> track of <c>Music.ini</c> that resolves to a file, shuffled
/// with the director's seeded generator (Fisher-Yates), reshuffled when exhausted and never repeating the last track across the seam.
/// <c>Music.ini</c> carries no playlist order, so the shuffle is an own decision, documented in <c>docs/GAME-AUDIO.md</c>.
/// </summary>
public sealed class MusicPlaylist
{
    private readonly ImmutableArray<AudioEventInfo> _tracks;
    private readonly SeededRng _rng;
    private readonly List<AudioEventInfo> _queue = [];
    private string? _last;

    private MusicPlaylist(ImmutableArray<AudioEventInfo> tracks, bool allSides, SeededRng rng)
    {
        _tracks = tracks;
        UsesAllSides = allSides;
        _rng = rng;
    }

    public int Count => _tracks.Length;

    /// <summary>The side has no <c>Game_</c> prefix of its own (e.g. an observer), so every side's tracks are in the list.</summary>
    public bool UsesAllSides { get; }

    public ImmutableArray<AudioEventInfo> Tracks => _tracks;

    /// <summary>Name fragment of the side's tracks: America is <c>USA</c> and China is <c>CHI</c> in <c>Music.ini</c>; null for an unknown side.</summary>
    public static string? PrefixOf(string side)
    {
        if (side.StartsWith("America", StringComparison.OrdinalIgnoreCase)) return "Game_USA_";
        if (side.StartsWith("China", StringComparison.OrdinalIgnoreCase)) return "Game_CHI_";
        if (side.StartsWith("GLA", StringComparison.OrdinalIgnoreCase)) return "Game_GLA_";
        return null;
    }

    public static MusicPlaylist ForSide(AudioCatalog catalog, string side, SeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rng);
        var prefix = PrefixOf(side ?? "");
        var candidates = catalog.MusicTracks
            .Where(track => track.File is { IsResolved: true } && track.Name.StartsWith("Game_", StringComparison.OrdinalIgnoreCase)
                && !track.Name.Contains("Silent", StringComparison.OrdinalIgnoreCase))
            .OrderBy(track => track.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        if (prefix is null) return new MusicPlaylist(candidates, true, rng);
        return new MusicPlaylist([..candidates.Where(track => track.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))], false, rng);
    }

    /// <summary>The next track, or null when the list is empty.</summary>
    public AudioEventInfo? Next()
    {
        if (_tracks.IsEmpty) return null;
        if (_queue.Count == 0)
        {
            _queue.AddRange(_tracks);
            for (var index = _queue.Count - 1; index > 0; index--)
            {
                var other = _rng.NextInt(0, index);
                (_queue[index], _queue[other]) = (_queue[other], _queue[index]);
            }
            // The queue is consumed from the end; avoid the last played track coming up first again.
            if (_queue.Count > 1 && _queue[^1].Name.Equals(_last, StringComparison.OrdinalIgnoreCase))
                (_queue[^1], _queue[0]) = (_queue[0], _queue[^1]);
        }
        var next = _queue[^1];
        _queue.RemoveAt(_queue.Count - 1);
        _last = next.Name;
        return next;
    }
}
