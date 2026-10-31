using System.Collections.Immutable;
using CnCNew.Game.Data;
using CnCNew.Ui.Runtime;
using CnCNew.Video;

namespace CnCNew.Game.Movies;

/// <summary>Where a movie's sound goes: the game's mixer, with the game's volumes.</summary>
public interface IMovieSound
{
    /// <summary>Starts the sound; the result stops it again.</summary>
    long PlayMovieSound(BinkSound sound);

    void StopMovieSound(long handle);
}

/// <summary>One movie that plays: its picture for the renderer, and its sound while it runs.</summary>
public sealed class MovieClip : IUiVideoSurface
{
    private readonly MoviePlayer _player;
    private readonly IMovieSound? _sound;
    private readonly long _soundHandle;
    private bool _ended;

    internal MovieClip(string name, MoviePlayer player, BinkSound? sound, IMovieSound? output)
    {
        Name = name;
        _player = player;
        if (sound is null || output is null) return;
        _sound = output;
        _soundHandle = output.PlayMovieSound(sound);
    }

    public string Name { get; }
    public int Width => _player.Width;
    public int Height => _player.Height;
    public byte[] Rgba => _player.Rgba;
    public long Version => _player.Version;
    public double DurationSeconds => _player.DurationSeconds;
    public int Frame => _player.Frame;
    public double Seconds => _player.Seconds;
    public bool IsFinished => _player.IsFinished;

    /// <summary>Why the movie ended early, if it did.</summary>
    public string? Failure => _player.Failure;

    internal void Advance(double seconds)
    {
        _player.Advance(seconds);
        if (_player.IsFinished) End();
    }

    /// <summary>Ends the movie now, with its sound.</summary>
    public void Stop()
    {
        _player.Stop();
        End();
    }

    private void End()
    {
        if (_ended) return;
        _ended = true;
        _sound?.StopMovieSound(_soundHandle);
    }
}

/// <summary>
/// The movies of a running client, on the real clock (docs/CLIENT-VIDEO.md): the original's <c>Display</c> holds a full-screen one, its <c>InGameUI</c> one for the radar and a load screen
/// its own. This class opens them by their name in <c>Video.ini</c>, moves them on once per drawn frame and counts what it could not play; a missing or damaged file is a note and
/// never stops the game. The simulation never sees any of it. Nothing here needs a window, so a run without one simply has no director, and everything that asks for a movie
/// behaves as before.
/// </summary>
public sealed class MovieDirector(Func<string, Stream?> open, IMovieSound? sound = null, Action<string>? skipped = null)
{
    private readonly List<MovieClip> _playing = [];
    private readonly SortedDictionary<string, int> _played = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, string> _failed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A director over the movies of the content: <paramref name="skipped"/> hears of each that could not be played.</summary>
    public static MovieDirector For(GameData data, string language, IMovieSound? sound, Action<string>? skipped)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new MovieDirector(name => data.Movies.Find(name, data.Content, language) is { } path ? data.Content.OpenRead(path) : null, sound, skipped);
    }

    /// <summary>The movie over everything (<c>Display::playMovie</c>).</summary>
    public MovieClip? FullScreen { get; private set; }

    /// <summary>The full-screen movie stands alone: nothing runs or takes input under it (the intro, <c>PlayMovieAndBlock</c>).</summary>
    public bool FullScreenIsModal { get; private set; }

    /// <summary>The movie in the radar's window (<c>InGameUI::playMovie</c>).</summary>
    public MovieClip? Radar { get; private set; }

    /// <summary>How often each movie started.</summary>
    public ImmutableSortedDictionary<string, int> Played => _played.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The movies that could not be played, with the reason of the last time.</summary>
    public ImmutableSortedDictionary<string, string> Failed => _failed.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Opens a movie and starts it and its sound; <see langword="null"/>, counted, when it cannot be played.</summary>
    public MovieClip? Open(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            using var stream = open(name);
            if (stream is null) return Skip(name, "no such movie");
            var movie = new BinkMovie(BinkFile.Open(stream));
            var clip = new MovieClip(name, new MoviePlayer(movie), sound is null ? null : movie.DecodeSound(), sound);
            _played[name] = _played.GetValueOrDefault(name) + 1;
            _playing.Add(clip);
            return clip;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException)
        {
            return Skip(name, exception.Message);
        }
    }

    private MovieClip? Skip(string name, string reason)
    {
        _failed[name] = reason;
        skipped?.Invoke(name);
        return null;
    }

    /// <summary>Plays a movie over everything, replacing the one that runs (<c>Display::playMovie</c> stops it first).</summary>
    public MovieClip? PlayFullScreen(string name, bool modal)
    {
        StopFullScreen();
        FullScreen = Open(name);
        FullScreenIsModal = modal && FullScreen is not null;
        return FullScreen;
    }

    public void StopFullScreen()
    {
        FullScreen?.Stop();
        FullScreen = null;
        FullScreenIsModal = false;
    }

    /// <summary>Plays a movie in the radar's window, replacing the one that runs.</summary>
    public MovieClip? PlayRadar(string name)
    {
        Radar?.Stop();
        return Radar = Open(name);
    }

    /// <summary>A game ended: nothing of it plays on.</summary>
    public void StopAll()
    {
        foreach (var clip in _playing) clip.Stop();
        _playing.Clear();
        FullScreen = null;
        FullScreenIsModal = false;
        Radar = null;
    }

    /// <summary>Once per drawn frame, with the real time since the last one.</summary>
    public void Update(double seconds)
    {
        foreach (var clip in _playing) clip.Advance(seconds);
        foreach (var clip in _playing)
            if (clip.Failure is { } failure) _failed[clip.Name] = failure;
        _playing.RemoveAll(clip => clip.IsFinished);
        if (FullScreen is { IsFinished: true })
        {
            FullScreen = null;
            FullScreenIsModal = false;
        }
        if (Radar is { IsFinished: true }) Radar = null;
    }

    /// <summary>The lines of the console's report.</summary>
    public IEnumerable<string> Report()
    {
        if (_played.Count > 0) yield return $"movies played: {string.Join(", ", _played.Select(entry => $"{entry.Key} {entry.Value}x"))}";
        foreach (var (name, reason) in _failed) yield return $"movie not played: {name}: {reason}";
    }
}

/// <summary>
/// The start of the game (<c>Intro.cpp</c>): the EA logo, a second of black, the sizzle, then the shell. The stages of the community's own logo are not part of this game. Escape ends
/// the running stage (<c>WindowXlat</c>: <c>skipCurrentIntroStage</c>); a movie that cannot be played is passed over.
/// </summary>
public sealed class MovieIntro(MovieDirector movies)
{
    public const string LogoMovie = "EALogoMovie";
    public const string SizzleMovie = "Sizzle";
    private const double PauseSeconds = 1.0;

    private int _stage;
    private double _wait;

    public bool IsDone => _stage > 3;

    public void Update(double seconds)
    {
        if (IsDone || movies.FullScreen is not null) return;
        if ((_wait -= seconds) > 0) return;
        _stage++;
        switch (_stage)
        {
            case 1: movies.PlayFullScreen(LogoMovie, modal: true); break;
            case 2: _wait = PauseSeconds; break;
            case 3: movies.PlayFullScreen(SizzleMovie, modal: true); break;
        }
    }

    public void Skip()
    {
        _wait = 0;
        movies.StopFullScreen();
    }
}
