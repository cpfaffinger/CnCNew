using CnCNew.Game.Live;
using CnCNew.Net;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;

namespace CnCNew.Game.Lockstep;

/// <summary>
/// The lockstep driver of a client: the tick source and the command scheduler of a game that takes its ticks from a relay (docs/NETWORK-PROTOCOL.md section 5). It is both
/// at once because they are one thing here - a tick may run when its bundle has arrived, and the bundle is what the scheduler hands out.
/// <list type="bullet">
/// <item><b>Orders</b> (<see cref="Schedule"/>) are sent to the relay as intents under their receipt number; they come back in the bundle of the tick the relay gave them,
/// with everybody else's, and are submitted from there (<see cref="Take"/>). Nothing this client orders reaches its own simulation any other way.</item>
/// <item><b>Client-paced</b> (the one client of a local game): time is this driver's own accumulator; when a tick is due it asks the relay to close it and steps as soon as
/// the bundle is there - with an embedded relay that is the same call, so an order is executed in the tick it was given, as before. Pause and speed are granted at once.</item>
/// <item><b>Server-paced</b>: the relay closes the ticks by its clock; this driver releases a tick when its bundle has arrived <b>and</b> its playback time is reached
/// (<see cref="PlaybackControl"/>, docs/NETWORK-PROTOCOL.md section 13): the playback position follows the estimated tick clock of the server by a delay measured from the
/// arrival jitter, and is steered by running a few percent faster or slower, never by jumps. A client that fell behind (a long frame, a garbage collection) finds several
/// ticks due at once and plays them in one frame, up to the frame's limit - the server waits for nobody. Without a clock, or before the first estimate, a tick is due when
/// its bundle is there. Pause and speed are requests to the relay.</item>
/// </list>
/// After every step the session hash goes to the relay at the interval the match start named.
/// </summary>
public sealed class LockstepDriver : ITickDriver, ICommandScheduler
{
    private readonly LockstepClient _client;
    private readonly double _tickSeconds;
    private readonly Action _pump;
    private readonly Func<ulong> _sessionHash;
    private readonly long _lastTick;
    private readonly Dictionary<ulong, ScheduledCommand> _own = [];
    private readonly Queue<(long DueTick, ScheduledCommand Command)> _held = new();
    private double _accumulator;
    private bool _localPause;
    private double _localSpeed = 1;
    private long _requested = -1;
    private int _inputDelayTicks;
    private long _nextTick;
    private long _stalledTick = -1;

    /// <param name="pump">Moves the network one round: what was sent arrives, what arrived is taken in (an embedded relay is pumped here; a remote one only this client).</param>
    /// <param name="sessionHash">The session hash of the simulation this driver steps, read after a step.</param>
    /// <param name="lastTick">The last tick of the match (its length minus one): the playback buffer runs empty toward it.</param>
    public LockstepDriver(LockstepClient client, double tickSeconds, Action pump, Func<ulong> sessionHash, long lastTick)
    {
        if (client.Start is null) throw new InvalidOperationException("The match has not started: there is nothing to drive.");
        _client = client;
        _tickSeconds = tickSeconds;
        _pump = pump;
        _sessionHash = sessionHash;
        _lastTick = lastTick;
        _nextTick = client.StartTick;
        // The tick length as the relay counts it: whole microseconds.
        Playback = new PlaybackControl(client.Playback, 1_000_000L / client.Description!.TickRate);
        if (!ClientPaced && client.Clock is not null) client.BundleArrived = (tick, micros) => Playback.Arrived(tick, micros, client.Sync, client.SinceLastPumpMicros);
    }

    public LockstepClient Client => _client;
    public bool ClientPaced => _client.Pacing == Pacing.Client;

    /// <summary>Server-paced only: when a tick's playback time is reached.</summary>
    public PlaybackControl Playback { get; }

    /// <summary>Server-paced: how many ticks are due beyond the one that runs next - what a frame has to catch up.</summary>
    public long BehindTicks => ClientPaced || !Playback.Synced ? 0 : Math.Max(0, Math.Min(Playback.PositionMicros / Playback.TickMicros - 1, _client.NewestBundleTick) - _nextTick);

    /// <summary>
    /// The lockstep probe on this path: an order is held here for this many ticks before it is sent (as if the way to the relay took that long). 0 by default.
    /// </summary>
    public int InputDelayTicks
    {
        get => _inputDelayTicks;
        set
        {
            if (_held.Count > 0) throw new InvalidOperationException("The input delay cannot be changed while orders are held.");
            _inputDelayTicks = Math.Max(0, value);
        }
    }

    public long HashesReported { get; private set; }
    public long Stalls { get; private set; }

    // ------------------------------------------------------------------------------------------------------- tick driver

    public bool Paused => ClientPaced ? _localPause : _client.Paused;
    public double Speed => ClientPaced ? _localSpeed : _client.SpeedPercent / 100.0;

    public void RequestPause(bool paused)
    {
        _localPause = paused;
        if (!ClientPaced && paused != _client.Paused) _client.RequestPace(paused, _client.SpeedPercent);
    }

    public void RequestSpeed(double speed)
    {
        _localSpeed = speed;
        var percent = (int)Math.Round(speed * 100);
        if (!ClientPaced && percent != _client.SpeedPercent) _client.RequestPace(_client.Paused, percent);
    }

    public void AddTime(double seconds)
    {
        if (ClientPaced)
        {
            if (!_localPause) _accumulator += seconds * _localSpeed;
        }
        else
        {
            _pump();
            if (_client.Clock is { } clock) Playback.Advance(clock.Microseconds, _client.Sync);
        }
    }

    public bool IsDue(long tick)
    {
        if (_client.Phase != ClientPhase.InMatch) return false;
        if (ClientPaced) return !_localPause && _accumulator >= _tickSeconds;
        if (!Playback.TimeReached(tick)) return false;
        if (_client.HasBundle(tick)) return true;
        // The playback time is reached and the bundle is not there: the buffer ran empty. Counted once per tick; the measured lateness of that bundle raises the delay.
        if (Playback.Synced && _stalledTick != tick && tick <= _lastTick)
        {
            _stalledTick = tick;
            Stalls++;
        }
        return false;
    }

    public bool TryReleaseTick(long tick, bool bundlePresent)
    {
        if (!IsDue(tick)) return false;
        if (ClientPaced)
        {
            if (!_client.HasBundle(tick))
            {
                // The orders of this tick first, then the request to close it: on one connection they arrive in that order.
                SendHeld(tick);
                if (_requested != tick)
                {
                    _client.RequestTick(tick);
                    _requested = tick;
                }
                _pump();
            }
            if (!_client.HasBundle(tick))
            {
                Stalls++;
                _accumulator = Math.Min(_accumulator, _tickSeconds);
                return false;
            }
            _accumulator -= _tickSeconds;
            return true;
        }
        SendHeld(tick);
        return true;
    }

    public void LimitBacklog() => _accumulator = Math.Min(_accumulator, _tickSeconds);

    /// <summary>Client-paced: the fraction of the accumulator. Server-paced: where the playback position stands between the last tick played and the next.</summary>
    public double Fraction => ClientPaced ? Math.Clamp(_accumulator / _tickSeconds, 0, 1) : Playback.Fraction(_nextTick);

    public void TickCompleted(long tick)
    {
        _nextTick = tick + 1;
        _client.ReportCompleted(tick);
        var interval = _client.Start!.HashInterval;
        if ((tick - _client.StartTick + 1) % interval != 0) return;
        _client.ReportHash(tick, _sessionHash());
        HashesReported++;
    }

    // --------------------------------------------------------------------------------------------------- command scheduler

    /// <summary>
    /// How many ticks after the one that runs next an order given now is expected to execute. Server-paced that is the playback delay plus the way to the relay, in ticks,
    /// plus the tick the relay has open - an estimate that moves with the network; the tick an order really gets is the relay's decision and comes back in a bundle.
    /// </summary>
    public int DelayTicks => _inputDelayTicks + (ClientPaced ? 0 : PredictedDelayTicks);

    private int PredictedDelayTicks
    {
        get
        {
            if (!Playback.Synced) return 1;
            var way = Math.Max(0, _client.Sync.RoundTripMicros) / 2;
            return (int)Math.Min(600, (Playback.DelayMicros + way + Playback.TickMicros - 1) / Playback.TickMicros + 1);
        }
    }
    public int Pending => _own.Count;

    public void Schedule(ScheduledCommand command, long now)
    {
        _own[(ulong)command.Receipt] = command;
        if (_inputDelayTicks == 0) Send(command);
        else _held.Enqueue((now + _inputDelayTicks, command));
    }

    private void Send(ScheduledCommand command) => _client.SendIntent((ulong)command.Receipt, CommandWire.EncodeIntent(command.Intent));

    private void SendHeld(long tick)
    {
        while (_held.Count > 0 && _held.Peek().DueTick <= tick) Send(_held.Dequeue().Command);
    }

    public bool IsPresent(long tick) => _client.HasBundle(tick);

    public IReadOnlyList<ScheduledCommand> Take(long tick)
    {
        var bundle = _client.TakeBundle(tick);
        if (bundle.Commands.IsEmpty) return [];
        var commands = new List<ScheduledCommand>(bundle.Commands.Length);
        foreach (var entry in bundle.Commands)
        {
            var intent = CommandWire.DecodeIntent(entry);
            // An own order comes back under its receipt; a command of another machine has no receipt and no pre-check here.
            if (entry.Origin == _client.ConnectionId && _own.Remove(entry.Receipt, out var own))
                commands.Add(new ScheduledCommand(own.Receipt, new PlayerId(entry.Player), intent, own.PreCheck, own.IssuedTick));
            else
                commands.Add(new ScheduledCommand(0, new PlayerId(entry.Player), intent, SubmitResult.Ok, tick));
        }
        return commands;
    }
}
