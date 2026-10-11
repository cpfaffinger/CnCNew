namespace CnCNew.Game.Live;

/// <summary>
/// Who decides which ticks exist. <see cref="LiveGame.Advance"/> owns no time of its own: it tells the driver how much wall time passed and asks it, tick by tick, whether the
/// next one may run. Locally that is an accumulator (<see cref="LocalTickDriver"/>); on the network the server's ticks arrive with their command bundles, and a tick whose
/// bundle is not there yet does not run however much time has passed. Pause and speed are <b>requests</b>: the local driver grants them at once, a networked one need not.
/// </summary>
public interface ITickDriver
{
    /// <summary>Whether the game stands still (the granted state, not the request).</summary>
    bool Paused { get; }

    /// <summary>The speed factor in force: 1 is real time.</summary>
    double Speed { get; }

    void RequestPause(bool paused);
    void RequestSpeed(double speed);

    /// <summary>Wall time passed since the last call, in seconds; once per <see cref="LiveGame.Advance"/> while the game runs.</summary>
    void AddTime(double seconds);

    /// <summary>
    /// Whether tick <paramref name="tick"/> may run now. <paramref name="bundlePresent"/> says whether its command bundle is there; a driver must not release a tick without
    /// it. A released tick is consumed: the next call asks for the next tick.
    /// </summary>
    bool TryReleaseTick(long tick, bool bundlePresent);

    /// <summary>The frame's budget of ticks is used up while more are due: the driver drops what it owes beyond one tick, so a slow frame is not followed by a burst.</summary>
    void LimitBacklog();

    /// <summary>How far the present is between the last tick and the next one, 0 to 1: the interpolation fraction of the frame.</summary>
    double Fraction { get; }

    /// <summary>Ticks that were due but not released because their bundle was missing (a local game never counts one).</summary>
    long Stalls { get; }
}

/// <summary>The local driver: the wall-time accumulator <see cref="LiveGame"/> always had, with pause and speed granted as they are asked for.</summary>
public sealed class LocalTickDriver(double tickSeconds) : ITickDriver
{
    private double _accumulator;

    public bool Paused { get; private set; }
    public double Speed { get; private set; } = 1;
    public long Stalls { get; private set; }

    public void RequestPause(bool paused) => Paused = paused;
    public void RequestSpeed(double speed) => Speed = speed;

    public void AddTime(double seconds)
    {
        if (!Paused) _accumulator += seconds * Speed;
    }

    public bool TryReleaseTick(long tick, bool bundlePresent)
    {
        if (Paused || _accumulator < tickSeconds) return false;
        if (!bundlePresent)
        {
            // The tick waits for its commands; the time it is owed does not pile up behind it.
            Stalls++;
            _accumulator = Math.Min(_accumulator, tickSeconds);
            return false;
        }
        _accumulator -= tickSeconds;
        return true;
    }

    public void LimitBacklog() => _accumulator = Math.Min(_accumulator, tickSeconds);

    public double Fraction => Math.Clamp(_accumulator / tickSeconds, 0, 1);
}
