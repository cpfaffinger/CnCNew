using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Net;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>What the in-match indicators read of a match of a server (docs/GAME-HUD.md section 21). <see cref="LiveMatchNetSource"/> is the real one; a test hands in its own.</summary>
public interface INetMatchSource
{
    bool Watching { get; }
    uint OwnConnection { get; }
    /// <summary>Round trip to the server in milliseconds; -1 before the clock has its first samples.</summary>
    int RoundTripMs { get; }
    /// <summary>How far behind the server's ticks this game plays on purpose, so that a late bundle does not make it wait (the playback delay).</summary>
    int DelayMs { get; }
    /// <summary>How much faster or slower than the server's clock this game runs right now to get to its place, in per mille.</summary>
    int RateAdjustPerMille { get; }
    IReadOnlyDictionary<uint, LagStateMessage> Lag { get; }
    IReadOnlyList<PlayerDroppedMessage> Dropped { get; }
    IReadOnlyDictionary<int, SeatStateMessage> HeldSeats { get; }
    /// <summary>Every change of a seat's hold in the order the server told it: held, returned, given up.</summary>
    IReadOnlyList<SeatStateMessage> SeatEvents { get; }
    bool Paused { get; }
    uint PausedBy { get; }
    int SpeedPercent { get; }
    DesyncMessage? Desync { get; }
    IReadOnlyList<ChatMessage> Chat { get; }
    /// <summary>The name of a member by its connection; null when the room never told this client of it.</summary>
    string? NameOf(uint connectionId);
    /// <summary>The name of whoever sits (or sat) in a player's seat.</summary>
    string? NameOfPlayer(int player);
    void SendChat(string text, bool alliesOnly);
    /// <summary>Real time in milliseconds, for the countdowns between two messages of the server.</summary>
    long NowMs { get; }
}

public enum NetQuality { Unknown, Good, Fair, Poor }

/// <summary>One line of the "waiting" panel: who the match waits for, and until when.</summary>
public sealed record NetWaitingLine(string Text);

/// <summary>What the indicators show in one frame.</summary>
public sealed record NetStatusView(string Connection, NetQuality Quality, ImmutableArray<NetWaitingLine> Waiting, string? Banner, bool Watching, string? ChatEntry)
{
    public static NetStatusView Empty { get; } = new(string.Empty, NetQuality.Unknown, [], null, false, null);
}

/// <summary>
/// The indicators of a match of a server over the HUD (docs/GAME-HUD.md section 21): how good the connection is (round trip, playback delay, catching up); who the
/// match waits for, how far behind that game is and when the server drops it; whose seat is held and for how long; who paused; that this game only watches; the line
/// of chat being typed. What happens once - a player dropped (with the server's reason), came back, the games fell out of sync, a chat line - goes to the HUD's
/// message list. Enter opens a chat line to everybody, Backspace one to the allies (as in the original); Enter sends, Escape drops it. Everything shown is this
/// project's own English (<see cref="NetText"/>): the original has no words for a relay's states. Nothing here reads or changes the simulation.
/// </summary>
public sealed class NetHud
{
    public const int FairRoundTripMs = 120, PoorRoundTripMs = 250;
    private const int MaxChatChars = 120;

    private readonly INetMatchSource _source;
    private readonly Action<string> _notice;
    private int _droppedSeen, _chatSeen, _seatEventsSeen;
    private bool _desyncSeen;
    private readonly Dictionary<int, (SeatHold State, long SinceMs, int HoldMs)> _seats = [];
    private readonly Dictionary<uint, (long SinceMs, int DropInMs, int LagMs)> _lag = [];
    private string _chatText = string.Empty;

    /// <param name="notice">Where a line that happens once goes (the HUD's message list).</param>
    public NetHud(INetMatchSource source, Action<string> notice)
    {
        _source = source;
        _notice = notice;
        _chatSeen = source.Chat.Count;
    }

    public bool ChatOpen { get; private set; }
    public bool ChatToAllies { get; private set; }
    public NetStatusView View { get; private set; } = NetStatusView.Empty;
    /// <summary>Where a desync package of this game is written, if it writes one: the notice of a desync names it.</summary>
    public string? DiagnosticsDirectory { get; set; }
    public long NoticesGiven { get; private set; }
    /// <summary>Called with every notice and with the status line whenever what the panel or the banner says changes: the client's <c>--net-stats</c> prints them.</summary>
    public Action<string>? Trace { get; set; }
    private string _traced = string.Empty;
    public long ChatSent { get; private set; }

    /// <summary>Takes an input event when it belongs to the chat line; true when it was consumed (the HUD then gives no order for it).</summary>
    public bool HandleInput(InputEvent inputEvent, bool menuOpen)
    {
        if (menuOpen) return false;
        if (!ChatOpen)
        {
            if (inputEvent is not KeyEvent { IsDown: true, Key: KeyCode.Enter or KeyCode.KeypadEnter or KeyCode.Backspace } open) return false;
            ChatOpen = true;
            ChatToAllies = open.Key == KeyCode.Backspace;
            _chatText = string.Empty;
            return true;
        }
        switch (inputEvent)
        {
            case TextInputEvent typed:
                if (!char.IsControl(typed.Character) && _chatText.Length < MaxChatChars) _chatText += typed.Character;
                return true;
            case KeyEvent { IsDown: true, Key: KeyCode.Backspace }:
                if (_chatText.Length > 0) _chatText = _chatText[..^1];
                return true;
            case KeyEvent { IsDown: true, Key: KeyCode.Escape }:
                ChatOpen = false;
                return true;
            case KeyEvent { IsDown: true, Key: KeyCode.Enter or KeyCode.KeypadEnter }:
                var text = _chatText.Trim();
                while (System.Text.Encoding.UTF8.GetByteCount(text) > Protocol.MaxChatBytes) text = text[..^1];
                if (text.Length > 0)
                {
                    _source.SendChat(text, ChatToAllies);
                    ChatSent++;
                }
                ChatOpen = false;
                return true;
            // While a line is typed the keys are letters, not hotkeys.
            case KeyEvent:
                return true;
            default:
                return false;
        }
    }

    private string Name(uint connection) => _source.NameOf(connection) ?? $"Player (connection {connection})";
    private string PlayerName(int player) => _source.NameOfPlayer(player) ?? $"Player {player}";

    private void Notice(string line)
    {
        NoticesGiven++;
        _notice(line);
        Trace?.Invoke($"net-notice: {line}");
    }

    /// <summary>Once a frame: what the server said since the last one becomes notices, and the view of this frame is put together.</summary>
    public void Update()
    {
        var now = _source.NowMs;
        for (; _droppedSeen < _source.Dropped.Count; _droppedSeen++)
        {
            var dropped = _source.Dropped[_droppedSeen];
            Notice($"{(dropped.Player > 0 ? PlayerName(dropped.Player) : Name(dropped.ConnectionId))} {NetText.Drop(dropped.Reason)}.");
        }
        for (; _chatSeen < _source.Chat.Count; _chatSeen++)
        {
            var line = _source.Chat[_chatSeen];
            Notice($"{Name(line.ConnectionId)}{(line.AlliesOnly ? " (to allies)" : string.Empty)}: {line.Text}");
        }
        if (_source.Desync is { } desync && !_desyncSeen)
        {
            _desyncSeen = true;
            Notice(NetText.DesyncNotice(desync.Tick, DiagnosticsDirectory));
        }
        for (; _seatEventsSeen < _source.SeatEvents.Count; _seatEventsSeen++)
        {
            var seat = _source.SeatEvents[_seatEventsSeen];
            // A held seat stands in the waiting panel while it is held; the two ways a hold ends are told once.
            if (seat.State == SeatHold.Held) _seats[seat.Player] = (seat.State, now, seat.HoldMs);
            else
            {
                _seats.Remove(seat.Player);
                Notice(seat.State == SeatHold.Returned ? $"{PlayerName(seat.Player)} is back." : $"{PlayerName(seat.Player)} did not come back in time.");
            }
        }
        var waiting = ImmutableArray.CreateBuilder<NetWaitingLine>();
        var halfSpeed = false;
        foreach (var (connection, lag) in _source.Lag.OrderBy(pair => pair.Key))
        {
            if (lag.LagMs <= 0)
            {
                _lag.Remove(connection);
                continue;
            }
            if (!_lag.TryGetValue(connection, out var seen) || seen.DropInMs != lag.DropInMs || seen.LagMs != lag.LagMs) _lag[connection] = seen = (now, lag.DropInMs, lag.LagMs);
            halfSpeed |= lag.Throttling;
            var behind = (lag.LagMs / 1000.0).ToString("F1", CultureInfo.InvariantCulture);
            var who = connection == _source.OwnConnection ? "This game" : Name(connection);
            // The server says how long it will still wait when it said so last; between two of its messages the time runs here.
            var dropIn = lag.DropInMs < 0 ? -1 : Math.Max(0, (int)Math.Ceiling((lag.DropInMs - (now - seen.SinceMs)) / 1000.0));
            waiting.Add(new NetWaitingLine(dropIn < 0 ? $"{who} is {behind} s behind" : $"{who} is {behind} s behind - dropped in {dropIn} s"));
        }
        foreach (var stale in _lag.Keys.Where(connection => !_source.Lag.ContainsKey(connection)).ToArray()) _lag.Remove(stale);
        foreach (var (player, seat) in _seats.OrderBy(pair => pair.Key))
        {
            if (seat.State != SeatHold.Held) continue;
            var left = Math.Max(0, (int)Math.Ceiling((seat.HoldMs - (now - seat.SinceMs)) / 1000.0));
            waiting.Add(new NetWaitingLine($"{PlayerName(player)} lost the connection - the seat is held for {left} s"));
        }
        string? banner = null;
        if (_source.Paused) banner = _source.PausedBy != 0 ? $"Paused by {(_source.PausedBy == _source.OwnConnection ? "you" : Name(_source.PausedBy))}" : "Paused";
        else if (halfSpeed) banner = "The game runs at half speed until everybody has caught up";
        else if (_source.SpeedPercent != 100) banner = $"Game speed {_source.SpeedPercent} %";

        var roundTrip = _source.RoundTripMs;
        var quality = roundTrip < 0 ? NetQuality.Unknown : roundTrip >= PoorRoundTripMs ? NetQuality.Poor : roundTrip >= FairRoundTripMs ? NetQuality.Fair : NetQuality.Good;
        if (_source.Lag.TryGetValue(_source.OwnConnection, out var own) && own.LagMs > 0) quality = NetQuality.Poor;
        var connection1 = roundTrip < 0 ? "Connecting ..." : $"Ping {roundTrip} ms, buffer {_source.DelayMs} ms";
        if (Math.Abs(_source.RateAdjustPerMille) >= 20) connection1 += _source.RateAdjustPerMille > 0 ? ", catching up" : ", slowing down";
        var entry = ChatOpen ? $"{(ChatToAllies ? "To allies" : "To all")}: {_chatText}_" : null;
        View = new NetStatusView(connection1, quality, waiting.ToImmutable(), banner, _source.Watching, entry);
        if (Trace is null) return;
        // Without the counted-down seconds: a line per change of what is said, not per second.
        var said = $"{string.Join(" | ", View.Waiting.Select(row => System.Text.RegularExpressions.Regex.Replace(row.Text, "[0-9.]+ s", "N s")))}; {View.Banner}";
        if (said == _traced) return;
        _traced = said;
        Trace(StatusLine());
    }

    private static readonly WndColor Shadow = new(0, 0, 0, 255);
    private static readonly WndColor Panel = new(0, 0, 0, 170);
    private static readonly WndColor White = new(255, 255, 255, 255);
    private static readonly WndColor Yellow = new(255, 220, 90, 255);

    private static WndColor ColorOf(NetQuality quality) => quality switch
    {
        NetQuality.Good => new WndColor(120, 230, 120, 255),
        NetQuality.Fair => new WndColor(255, 220, 90, 255),
        NetQuality.Poor => new WndColor(255, 110, 90, 255),
        _ => new WndColor(200, 200, 200, 255),
    };

    /// <summary>The draw commands of the indicators for a screen of this size; they lie over the HUD and under the pointer.</summary>
    public ImmutableArray<UiDrawCommand> Build(int width, int height, IUiTextMeasure measure)
    {
        var view = View;
        var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
        // The texts scale with the height of the screen like the HUD's own (600 is the height the layouts were made for).
        var size = Math.Max(9, (int)Math.Round(10.0 * height / 600));
        var font = new UiFont("Arial", size, true);
        var line = measure.LineHeight(font) + 2;
        var clip = new UiRect(0, 0, width, height);
        var margin = Math.Max(6, width / 100);

        void Text(string text, int x, int y, WndColor color, UiTextAlignment alignment = UiTextAlignment.Left)
            => commands.Add(new UiTextDraw(text, null, font, color, Shadow, x, y, alignment, clip));

        // Top right: the connection, in the colour of its quality.
        if (view.Connection.Length > 0)
        {
            var textWidth = measure.MeasureWidth(view.Connection, font);
            Text(view.Connection, width - margin - textWidth, margin, ColorOf(view.Quality));
        }
        // Top left: this game only watches.
        if (view.Watching) Text("Observing - this game gives no orders", margin, margin, Yellow);

        // Under the top edge, in the middle: who the match waits for.
        var y = margin + line + margin;
        if (view.Waiting.Length > 0)
        {
            var title = "Waiting for players";
            var panelWidth = Math.Max(measure.MeasureWidth(title, font), view.Waiting.Max(row => measure.MeasureWidth(row.Text, font))) + 4 * margin;
            var panel = new UiRect((width - panelWidth) / 2, y, panelWidth, (view.Waiting.Length + 1) * line + 2 * margin);
            commands.Add(new UiFillRect(panel, Panel));
            commands.Add(new UiBorderRect(panel, Yellow));
            Text(title, panel.X + 2 * margin, panel.Y + margin, Yellow);
            for (var index = 0; index < view.Waiting.Length; index++) Text(view.Waiting[index].Text, panel.X + 2 * margin, panel.Y + margin + (index + 1) * line, White);
            y = panel.Y + panel.Height + margin;
        }
        if (view.Banner is { } banner)
        {
            var bannerWidth = measure.MeasureWidth(banner, font) + 4 * margin;
            var box = new UiRect((width - bannerWidth) / 2, y, bannerWidth, line + 2 * margin);
            commands.Add(new UiFillRect(box, Panel));
            Text(banner, box.X + 2 * margin, box.Y + margin, White);
        }
        // Above the control bar, on the left: the chat line being typed.
        if (view.ChatEntry is { } entry)
        {
            var entryWidth = Math.Min(width - 2 * margin, Math.Max(width / 3, measure.MeasureWidth(entry, font) + 4 * margin));
            var box = new UiRect(margin, (int)(height * 0.62), entryWidth, line + 2 * margin);
            commands.Add(new UiFillRect(box, Panel));
            commands.Add(new UiBorderRect(box, White));
            Text(entry, box.X + 2 * margin, box.Y + margin, White);
        }
        return commands.ToImmutable();
    }

    /// <summary>The lines of <c>--net-stats</c> that say what the indicators show: the same values, for the console.</summary>
    public string StatusLine()
        => $"net-status: {View.Connection}; quality {View.Quality}; waiting [{string.Join(" | ", View.Waiting.Select(row => row.Text))}]; banner {View.Banner ?? "none"}; watching {(View.Watching ? "yes" : "no")}; notices {NoticesGiven}; chat sent {ChatSent}";
}

/// <summary>The indicators' view of a real match: the protocol client of the connection and the playback of the lockstep driver.</summary>
public sealed class LiveMatchNetSource(LiveMatch match) : INetMatchSource
{
    private readonly LockstepClient _client = match.Remote?.Client ?? throw new InvalidOperationException("This match has no server.");
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
    // The room as it was when the match started: who sat where. The room's later states name whoever is still connected.
    private readonly ImmutableArray<RoomMember> _members = match.Remote?.Client.Room?.Members ?? [];
    private readonly RoomSettings _settings = match.Remote?.Client.Room?.Settings ?? RoomSettings.Empty;

    public bool Watching => match.Setup.Spectator;
    public uint OwnConnection => _client.ConnectionId;
    // The last one known: while the match is paused the clock takes no samples, and the line shall not fall back to "connecting".
    public int RoundTripMs => _client.Sync.HasEstimate ? _roundTripMs = (int)(_client.Sync.RoundTripMicros / 1000) : _roundTripMs;
    private int _roundTripMs = -1;
    public int DelayMs => (int)(match.Lockstep.Playback.DelayMicros / 1000);
    public int RateAdjustPerMille => match.Lockstep.Playback.RateAdjustPerMille;
    public IReadOnlyDictionary<uint, LagStateMessage> Lag => _client.Lag;
    public IReadOnlyList<PlayerDroppedMessage> Dropped => _client.Dropped;
    public IReadOnlyDictionary<int, SeatStateMessage> HeldSeats => _client.HeldSeats;
    public IReadOnlyList<SeatStateMessage> SeatEvents => _client.SeatEvents;
    public bool Paused => _client.Paused;
    public uint PausedBy => _client.PausedBy;
    public int SpeedPercent => _client.SpeedPercent;
    public DesyncMessage? Desync => _client.Desync;
    public IReadOnlyList<ChatMessage> Chat => _client.Chat;
    public long NowMs => _watch.ElapsedMilliseconds;

    public string? NameOf(uint connectionId)
    {
        foreach (var member in _client.Room?.Members ?? [])
            if (member.ConnectionId == connectionId) return member.Name;
        foreach (var member in _members)
            if (member.ConnectionId == connectionId) return member.Name;
        return null;
    }

    public string? NameOfPlayer(int player)
    {
        // Players are numbered over the slots that take part, in slot order.
        var number = 0;
        for (var slot = 0; slot < _settings.Slots.Length; slot++)
        {
            if (!_settings.Slots[slot].TakesPart || ++number != player) continue;
            foreach (var member in _members)
                if (member.Slot == slot) return member.Name;
            return null;
        }
        return null;
    }

    public void SendChat(string text, bool alliesOnly) => _client.SendChat(text, alliesOnly);
}
