namespace CnCNew.Video;

/// <summary>
/// A movie on a clock: the caller says how much real time passed, the player decodes what is due and keeps the newest picture as 8-bit RGBA. Frame n is due at n / rate seconds, and
/// a frame stands for one frame's time, so the movie ends at frames / rate. Every frame depends on the one before it, so a late frame is decoded all the same; it is only not
/// converted to a picture. A frame that cannot be decoded ends the movie (<see cref="Failure"/>); the picture stays.
/// </summary>
public sealed class MoviePlayer
{
    private readonly BinkMovie _movie;
    private double _clock;

    public MoviePlayer(BinkMovie movie)
    {
        _movie = movie ?? throw new ArgumentNullException(nameof(movie));
        Rgba = new byte[movie.Width * movie.Height * 4];
        DurationSeconds = movie.FrameCount / movie.FramesPerSecond;
        Decode();
    }

    public int Width => _movie.Width;
    public int Height => _movie.Height;

    /// <summary>The newest picture, <c>Width * Height * 4</c> bytes.</summary>
    public byte[] Rgba { get; }

    /// <summary>Goes up with every new picture, so a consumer uploads each one once.</summary>
    public long Version { get; private set; }

    public double DurationSeconds { get; }

    /// <summary>The frames decoded so far.</summary>
    public int Frame => _movie.FramesDecoded;

    public double Seconds => _clock;

    public bool IsFinished { get; private set; }

    /// <summary>Why the movie ended early; <see langword="null"/> when it did not.</summary>
    public string? Failure { get; private set; }

    /// <summary>Moves the clock on by <paramref name="seconds"/> of real time and decodes what is due.</summary>
    public void Advance(double seconds)
    {
        if (IsFinished) return;
        _clock += seconds;
        if (_clock >= DurationSeconds)
        {
            IsFinished = true;
            return;
        }
        Decode();
    }

    /// <summary>Ends the movie now; the picture stays.</summary>
    public void Stop() => IsFinished = true;

    private void Decode()
    {
        var due = Math.Min(_movie.FrameCount, (int)(_clock * _movie.FramesPerSecond) + 1);
        if (_movie.FramesDecoded >= due) return;
        try
        {
            while (_movie.FramesDecoded < due) _movie.DecodeNextFrame();
        }
        catch (InvalidDataException exception)
        {
            Failure = $"frame {_movie.FramesDecoded}: {exception.Message}";
            IsFinished = true;
            return;
        }
        _movie.CopyRgba(Rgba);
        Version++;
    }
}
