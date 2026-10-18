using System.Collections.Immutable;
using System.Net;
using CnCNew.Game.Live;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Shell.Screens;

/// <summary>
/// <c>NetworkDirectConnect.wnd</c> (docs/GAME-SHELL.md section 15.1): the player's name, the address of a server, and the two ways on - join the server at that
/// address, or host: start a server in this game and enter it like everybody else will. The combo box keeps the addresses typed before. The layout bounds an address
/// to 16 characters (an IPv4 address without a port); this screen lifts that to 64, so that a name or a port fits.
/// </summary>
public sealed class NetworkDirectConnectScreen : ShellScreen
{
    private readonly IShellNetwork _network;

    public NetworkDirectConnectScreen(GameShell shell) : base(shell, ShellLayoutNames.NetworkDirectConnect)
    {
        _network = Shell.Network ?? throw new InvalidOperationException("The multiplayer screens need a network (GameShell.Network).");
        Tree.Register("NetworkDirectConnectSystem", OnSystem);
        Tree.Register("NetworkDirectConnectInput", OnInput);
        var names = new ShellSkirmishText(Shell.Data.Graph, Shell.TextSource);
        W("EditPlayerName").SetEntryText(_network.PlayerName.Length > 0 ? _network.PlayerName : names.Player);
        var remote = W("ComboboxRemoteIP");
        remote.SetEditLimit(64);
        foreach (var server in _network.RecentServers) remote.AddComboEntry(server);
        if (_network.RecentServers.Count > 0) remote.SetComboSelection(0);
        W("StaticLocalIP").SetText(_network.LocalAddresses);
        Transitions?.SetGroup("NetworkDirectConnectFade");
    }

    protected override string? ExitTransitionGroup => "NetworkDirectConnectFade";

    /// <summary>The name as it goes to a server: trimmed and cut to what the protocol takes; null (and a message) when nothing is left.</summary>
    private string? PlayerName()
    {
        var name = W("EditPlayerName").TextEntry!.Text.Trim();
        while (System.Text.Encoding.UTF8.GetByteCount(name) > Protocol.MaxNameBytes) name = name[..^1];
        if (name.Length > 0) return name;
        Shell.ShowError(NetText.TitleCannotConnect, "Enter a player name first.");
        return null;
    }

    private void Host()
    {
        if (PlayerName() is not { } name) return;
        IShellHost host;
        try
        {
            host = _network.Host();
        }
        catch (RemoteRelayException exception)
        {
            Shell.ShowError(NetText.TitleCannotHost, exception.Message);
            return;
        }
        _network.Remember(name, null);
        // The host enters its own server like everybody else - through a socket, with the key it just made pinned, so there is no question about it.
        var server = new IPEndPoint(IPAddress.Loopback, host.LocalEndPoint.Port);
        var lobby = _network.CreateLobby(name);
        lobby.Connect(server, host.Fingerprint);
        Shell.OpenNetLobby(new ShellNetSession(lobby, host, name, server));
    }

    private void Join()
    {
        if (PlayerName() is not { } name) return;
        var typed = W("ComboboxRemoteIP").ComboBox!.Edit.Text.Trim();
        if (_network.Resolve(typed) is not { } server)
        {
            Shell.ShowError(NetText.TitleAddress, typed.Length == 0 ? "Enter the address of a server: an address or a name, with or without a port." : $"'{typed}' is not the address of a server.");
            return;
        }
        _network.Remember(name, typed);
        var lobby = _network.CreateLobby(name);
        lobby.Connect(server);
        Shell.OpenNetLobby(new ShellNetSession(lobby, null, name, server));
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.ButtonSelected) return;
        switch (e.Source)
        {
            case "NetworkDirectConnect.wnd:ButtonHost": Shell.PlaySound(ShellUiSounds.ButtonClick); Host(); break;
            case "NetworkDirectConnect.wnd:ButtonJoin": Shell.PlaySound(ShellUiSounds.ButtonClick); Join(); break;
            case "NetworkDirectConnect.wnd:ButtonBack": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.CloseTop(); break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }
}

/// <summary>
/// <c>LanLobbyMenu.wnd</c> as the front of a server (docs/GAME-SHELL.md section 15.2): the list of its rooms with what somebody needs to choose one, "Host" to open
/// a room of one's own, "Join" to enter the chosen one. On the way here it is also where the connection is made: the question about a server never met, and every
/// way a connection or an entry can fail, are message boxes over it. What the layout has for a local network - the list of players, the chat before a room, the
/// button to the direct-connect screen it came from - has no counterpart on a server and is disabled or hidden (a list of rooms says nothing about people).
/// </summary>
public sealed class NetLobbyScreen : ShellScreen
{
    private readonly ShellNetSession _session;
    private RoomListMessage? _shownRooms;
    private ImmutableArray<ListedRoom> _rooms = [];
    private string _selected = string.Empty;
    private LobbyStage _shownStage = (LobbyStage)(-1);
    private bool _refusedShown;
    private bool _dialogOpen;

    public NetLobbyScreen(GameShell shell, ShellNetSession session) : base(shell, ShellLayoutNames.LanLobbyMenu)
    {
        _session = session;
        Tree.Register("LanLobbyMenuSystem", OnSystem);
        Tree.Register("LanLobbyMenuInput", OnInput);
        W("TextEntryPlayerName").SetEntryText(session.PlayerName);
        W("TextEntryPlayerName").SetEnabled(false);
        W("TextEntryChat").SetEnabled(false);
        W("ButtonEmote").SetEnabled(false);
        W("ButtonClear").SetEnabled(false);
        W("ListboxPlayers").SetEnabled(false);
        W("ButtonDirectConnect").SetHidden(true);
        W("ButtonJoin").SetEnabled(false);
        W("ButtonHost").SetEnabled(false);
        Refresh();
        Transitions?.SetGroup("LanLobbyFade");
    }

    protected override string? ExitTransitionGroup => "LanLobbyFade";

    private INetLobby Lobby => _session.Lobby;

    public override void Update(double dtSeconds)
    {
        base.Update(dtSeconds);
        if (Shell.CurrentScreen == this && !IsClosing) Refresh();
    }

    public override void OnRevealed()
    {
        _dialogOpen = false;
        _shownRooms = null;
        _shownStage = (LobbyStage)(-1);
    }

    private void Refresh()
    {
        var stage = Lobby.Stage;
        var browsing = stage == LobbyStage.Browsing;
        if (stage != _shownStage)
        {
            _shownStage = stage;
            W("ButtonHost").SetEnabled(browsing);
            if (!browsing) W("ButtonJoin").SetEnabled(false);
            switch (stage)
            {
                case LobbyStage.Connecting: W("StaticTextGameInfo").SetText($"Connecting to {_session.Server} ..."); break;
                case LobbyStage.Entering: W("StaticTextGameInfo").SetText($"Entering \"{Lobby.RoomName}\" ..."); break;
                case LobbyStage.Browsing: _shownRooms = null; _refusedShown = false; break;
            }
        }
        switch (stage)
        {
            case LobbyStage.TrustQuestion when !_dialogOpen:
                _dialogOpen = true;
                Shell.AskYesNo(NetText.TitleNewServer, NetText.NewServer(_session.Server, Lobby.ServerFingerprint),
                    yes: () => { Shell.CloseTop(); Lobby.AcceptServer(); },
                    no: () => { Shell.CloseTop(); Shell.CloseNet(); });
                break;
            case LobbyStage.Failed when !_dialogOpen && Lobby.Failure is { } failure:
            {
                _dialogOpen = true;
                var (title, message) = NetText.Failure(failure, _session.Server);
                // A room that turned this client away leaves the server's other rooms open; everything else ends the visit.
                var backToRooms = failure.Kind == LobbyFailureKind.Rejected && failure.Reject is not (RejectReason.ProtocolVersion or RejectReason.Banned or RejectReason.BadIdentity or RejectReason.ServerFull);
                Shell.Tell(title, message, () =>
                {
                    Shell.CloseTop();
                    if (backToRooms) Lobby.LeaveRoom();
                    else Shell.CloseNet();
                });
                break;
            }
            case LobbyStage.Browsing: ShowRooms(); break;
            case LobbyStage.InRoom: Shell.OpenNetRoom(_session); break;
            case LobbyStage.Idle: Shell.CloseNet(); break;
        }
    }

    private string MapName(string path)
        => path.Length == 0 ? string.Empty : Shell.MapCatalog.ContainsKey(path) ? Shell.MapNames.DisplayName(path, Shell.MapCatalog, Shell.TextSource) : Path.GetFileNameWithoutExtension(path);

    private void ShowRooms()
    {
        if (Lobby.ListingRefused)
        {
            if (_refusedShown) return;
            _refusedShown = true;
            W("ListboxGames").ClearList();
            _rooms = [];
            W("ButtonJoin").SetEnabled(false);
            W("StaticTextGameInfo").SetText("This server does not list its rooms.\nChoose Host and enter the name of the room you were given.");
            return;
        }
        if (Lobby.Rooms is not { } rooms || ReferenceEquals(rooms, _shownRooms)) return;
        _shownRooms = rooms;
        _rooms = rooms.Rooms;
        var list = W("ListboxGames");
        list.ClearList();
        foreach (var room in _rooms) list.AddListRow(new UiListRow(NetText.RoomLine(room, MapName(room.MapPath))));
        var index = _rooms.IndexOf(_rooms.FirstOrDefault(room => room.Name == _selected) ?? NoRoom);
        if (index >= 0) list.SetListSelection(index);
        else _selected = string.Empty;
        ShowSelection(rooms);
    }

    private static readonly ListedRoom NoRoom = new(string.Empty, RoomPhase.Room, 0, 0, 0, false, string.Empty, 0, string.Empty);

    private void ShowSelection(RoomListMessage rooms)
    {
        var chosen = _rooms.FirstOrDefault(room => room.Name == _selected);
        W("ButtonJoin").SetEnabled(chosen is not null && Lobby.Stage == LobbyStage.Browsing);
        W("StaticTextGameInfo").SetText(chosen is not null
            ? NetText.RoomInfo(chosen, MapName(chosen.MapPath), BuildIdentity.Current.Id, Shell.Data.Rate.TicksPerSecond)
            : rooms.TotalRooms == 0 ? $"{_session.Server}\nNo room is open on this server.\nChoose Host to open one."
            : $"{_session.Server}\n{rooms.TotalRooms} of {rooms.MaxRooms} rooms open{(rooms.TotalRooms > _rooms.Length ? $" (the first {_rooms.Length} are listed)" : string.Empty)}");
    }

    internal void Enter(string room, string password)
    {
        _session.Room = room;
        _session.Password = password;
        Lobby.Enter(room, password);
    }

    private void JoinSelected()
    {
        if (_rooms.FirstOrDefault(room => room.Name == _selected) is not { } chosen || Lobby.Stage != LobbyStage.Browsing) return;
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        if (chosen.HasPassword) Shell.OpenNetJoinPopup(this, chosen.Name);
        else Enter(chosen.Name, string.Empty);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ListSelected when e.Source == "LanLobbyMenu.wnd:ListboxGames":
                _selected = e.Value >= 0 && e.Value < _rooms.Length ? _rooms[e.Value].Name : string.Empty;
                if (_shownRooms is { } rooms) ShowSelection(rooms);
                break;
            case UiEventKind.ListDoubleClicked when e.Source == "LanLobbyMenu.wnd:ListboxGames":
                _selected = e.Value >= 0 && e.Value < _rooms.Length ? _rooms[e.Value].Name : string.Empty;
                JoinSelected();
                break;
            case UiEventKind.ButtonSelected when e.Source == "LanLobbyMenu.wnd:ButtonJoin": JoinSelected(); break;
            case UiEventKind.ButtonSelected when e.Source == "LanLobbyMenu.wnd:ButtonHost":
                if (Lobby.Stage != LobbyStage.Browsing) break;
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.OpenNetHostPopup(this, _session.PlayerName);
                break;
            case UiEventKind.ButtonSelected when e.Source == "LanLobbyMenu.wnd:ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.CloseNet();
                break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseNet();
    }
}

/// <summary>
/// <c>PopupHostGame.wnd</c>: the name of a room and, if it shall be locked, its password. Whoever names a room first opens it; a name that exists is entered. The
/// description and the three check boxes of the layout (observers, limited armies, statistics) are settings of a server here, not of a room, and are disabled.
/// </summary>
public sealed class NetHostPopupScreen : ShellScreen
{
    public override bool HidesScreensBelow => false;
    private readonly NetLobbyScreen _owner;

    public NetHostPopupScreen(GameShell shell, NetLobbyScreen owner, string playerName) : base(shell, ShellLayoutNames.PopupHostGame)
    {
        _owner = owner;
        Tree.Register("PopupHostGameSystem", OnSystem);
        Tree.Register("PopupHostGameInput", OnInput);
        var name = $"{playerName}'s game";
        while (System.Text.Encoding.UTF8.GetByteCount(name) > Protocol.MaxNameBytes) name = name[..^1];
        W("TextEntryGameName").SetEntryText(name);
        W("TextEntryGamePassword").SetEntryText(string.Empty);
        W("TextEntryGameDescription").SetEnabled(false);
        W("CheckBoxAllowObservers").SetEnabled(false);
        W("CheckBoxLimitArmies").SetEnabled(false);
        W("CheckBoxUseStats").SetEnabled(false);
    }

    private void Create()
    {
        var name = W("TextEntryGameName").TextEntry!.Text.Trim();
        if (name.Length == 0) return;
        while (System.Text.Encoding.UTF8.GetByteCount(name) > Protocol.MaxNameBytes) name = name[..^1];
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        var password = W("TextEntryGamePassword").TextEntry!.Text;
        Shell.CloseTop();
        _owner.Enter(name, password);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.ButtonSelected when e.Source == "PopupHostGame.wnd:ButtonCreateGame": Create(); break;
            case UiEventKind.EntryEditDone when e.Source is "PopupHostGame.wnd:TextEntryGameName" or "PopupHostGame.wnd:TextEntryGamePassword": Create(); break;
            case UiEventKind.ButtonSelected when e.Source == "PopupHostGame.wnd:ButtonCancel": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.CloseTop(); break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }
}

/// <summary><c>PopupJoinGame.wnd</c>: the password of a locked room. The layout has no button to confirm - Enter in the field does, as in the original.</summary>
public sealed class NetJoinPopupScreen : ShellScreen
{
    public override bool HidesScreensBelow => false;
    private readonly NetLobbyScreen _owner;
    private readonly string _room;

    public NetJoinPopupScreen(GameShell shell, NetLobbyScreen owner, string room) : base(shell, ShellLayoutNames.PopupJoinGame)
    {
        _owner = owner;
        _room = room;
        Tree.Register("PopupJoinGameSystem", OnSystem);
        Tree.Register("PopupJoinGameInput", OnInput);
        W("StaticTextGameName").SetText(room);
        W("TextEntryGamePassword").SetEntryText(string.Empty);
        Tree.SetFocus(W("TextEntryGamePassword"));
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        switch (e.Kind)
        {
            case UiEventKind.EntryEditDone when e.Source == "PopupJoinGame.wnd:TextEntryGamePassword":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                var password = e.Text ?? string.Empty;
                Shell.CloseTop();
                _owner.Enter(_room, password);
                break;
            case UiEventKind.ButtonSelected when e.Source == "PopupJoinGame.wnd:ButtonCancel": Shell.PlaySound(ShellUiSounds.ButtonClick); Shell.CloseTop(); break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind == UiEventKind.Key && e.Value == (int)UiKey.Escape) Shell.CloseTop();
    }
}

/// <summary>Who a map picker reports to: the skirmish setup and the room of a server share the picker.</summary>
public interface IMapPickerOwner
{
    void SetSelectedMap(string mapPath);
    void SetMapPickerOpen(bool open);
}

/// <summary>
/// <c>LanGameOptionsMenu.wnd</c> as a room of a server (docs/GAME-SHELL.md section 15.3). A row is a slot of the room's settings: open for a player, closed, a
/// computer army, or taken by a member - whose name stands there, with the lamp beside it lit once it accepted. The host sets map, money and what every slot is; a
/// member takes an open seat, sets army, colour and team of its own seat and accepts; the host starts. Nothing is decided here: every box is a request to the
/// server (<see cref="INetLobby"/>, sent at the lobby's pace), and what the screen shows is what the server said last. A request the server refuses appears as a
/// line in the chat list, with the server's reason.
/// </summary>
public sealed class NetRoomScreen : ShellScreen, IMapPickerOwner
{
    private const int Slots = 8;
    private static readonly ImmutableArray<int> MoneyChoices = [5_000, 10_000, 15_000, 20_000, 30_000, 40_000, 50_000];
    private const int OpenEntry = 0, ClosedEntry = 1, ArmyEntry = 2, SeatEntry = 3;

    private readonly ShellNetSession _session;
    private readonly ShellSkirmishText _names;
    private readonly ImmutableArray<string> _factionChoices;
    private readonly int _colorCount;
    private readonly Dictionary<string, string> _terrainFingerprints = new(StringComparer.Ordinal);
    private RoomStateMessage? _shown;
    private int _chatShown;
    private RejectMessage? _refusalShown;
    private bool _syncing;
    private bool _configuredDefaults;
    private bool _mapPickerOpen;
    private bool _dialogOpen;
    private readonly bool[] _memberCanSit = new bool[Slots];

    public NetRoomScreen(GameShell shell, ShellNetSession session) : base(shell, ShellLayoutNames.LanGameOptionsMenu)
    {
        _session = session;
        _names = new ShellSkirmishText(Shell.Data.Graph, Shell.TextSource);
        _factionChoices = [.. Shell.Data.Factions.Where(p => p.Faction.PlayableSide).Select(p => p.Faction.Name).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal), MatchSetup.RandomFaction];
        var colors = _names.ColorNames();
        _colorCount = colors.Length;
        Tree.Register("LanGameOptionsMenuSystem", OnSystem);
        Tree.Register("LanGameOptionsMenuInput", OnInput);
        for (var slot = 0; slot < Slots; slot++)
        {
            var template = W($"ComboBoxPlayerTemplate{slot}");
            foreach (var name in _factionChoices) template.AddComboEntry(name == MatchSetup.RandomFaction ? _names.Random : _names.Faction(name));
            var color = W($"ComboBoxColor{slot}");
            foreach (var colorName in colors) color.AddComboEntry(colorName);
            var team = W($"ComboBoxTeam{slot}");
            for (var index = 0; index <= 4; index++) team.AddComboEntry(_names.Team(index));
            W($"ButtonAccept{slot}").SetHidden(true);
        }
        var money = W("ComboBoxStartingCash");
        foreach (var value in MoneyChoices) money.AddComboEntry(Shell.FormatMoney(value));
        // Superweapon limits are not a rule of this build's matches.
        W("CheckboxLimitSuperweapons").SetEnabled(false);
        W("ButtonEmote").SetEnabled(false);
        SkirmishSetupScreen.HideStartPositionMarkers(this);
        Sync();
        Transitions?.SetGroup("LanGameOptionsFade");
    }

    protected override string? ExitTransitionGroup => "LanGameOptionsFade";

    private INetLobby Lobby => _session.Lobby;

    public override void Update(double dtSeconds)
    {
        base.Update(dtSeconds);
        if (IsClosing) return;
        if (Shell.CurrentScreen != this)
        {
            // A map picker or a message lies over the room: the room keeps showing what the server says, but opens nothing.
            if (Lobby.Stage == LobbyStage.InRoom) Sync();
            return;
        }
        switch (Lobby.Stage)
        {
            case LobbyStage.InRoom: Sync(); break;
            case LobbyStage.Starting: Shell.BeginNetMatch(_session); break;
            case LobbyStage.Failed when !_dialogOpen && Lobby.Failure is { } failure:
            {
                _dialogOpen = true;
                var (title, message) = NetText.Failure(failure, _session.Server);
                var removed = failure is { Kind: LobbyFailureKind.Rejected, Reject: RejectReason.Kicked };
                Shell.Tell(title, message, () =>
                {
                    Shell.CloseTop();
                    if (removed)
                    {
                        // The host removed this member: the server's other rooms are still there.
                        Lobby.LeaveRoom();
                        Shell.CloseTop();
                    }
                    else Shell.CloseNet();
                });
                break;
            }
            case LobbyStage.Browsing or LobbyStage.Connecting or LobbyStage.Idle: Shell.CloseTop(); break;
        }
    }

    public override void OnRevealed() => _dialogOpen = false;

    // ------------------------------------------------------------------------------------------------- what the server says

    private RoomSettings Settings => Lobby.Room?.Settings ?? RoomSettings.Empty;
    private bool IsHost => Lobby.IsHost;
    private RoomMember? Me => Lobby.Room?.Members.Cast<RoomMember?>().FirstOrDefault(member => member!.Value.ConnectionId == Lobby.ConnectionId);
    private static RoomMember? MemberAt(RoomStateMessage room, int slot) => room.Members.Cast<RoomMember?>().FirstOrDefault(member => member!.Value.Slot == slot);

    private string MapName(string path)
        => path.Length == 0 ? string.Empty : Shell.MapCatalog.ContainsKey(path) ? Shell.MapNames.DisplayName(path, Shell.MapCatalog, Shell.TextSource) : Path.GetFileNameWithoutExtension(path);

    private void Sync()
    {
        if (Lobby.Room is not { } room) return;
        if (IsHost && !_configuredDefaults && room.Settings.Slots.Length == 0)
        {
            // The room is new: the host gives it a map and seats, and takes the first one.
            _configuredDefaults = true;
            if (PreferredMap() is { } map && SettingsFor(map, RoomSettings.Empty) is { } settings)
            {
                Lobby.Configure(settings);
                Lobby.RequestSlot(0, settings.Slots[0].Faction, settings.Slots[0].Team, settings.Slots[0].Color, settings.Slots[0].StartPosition);
            }
        }
        if (!ReferenceEquals(room, _shown))
        {
            _shown = room;
            ShowRoom(room);
            // A member that has no seat takes the first open one (a room without one leaves it watching).
            if (!IsHost && Me is { Slot: < 0 } && FirstOpenSeat(room) is var open and >= 0) TakeSeat(open);
        }
        ShowChat(room);
    }

    private static int FirstOpenSeat(RoomStateMessage room)
    {
        for (var slot = 0; slot < room.Settings.Slots.Length; slot++)
            if (room.Settings.Slots[slot].Kind == MatchSlotKind.Human && MemberAt(room, slot) is null) return slot;
        return -1;
    }

    private void ShowRoom(RoomStateMessage room)
    {
        _syncing = true;
        try
        {
            var settings = room.Settings;
            var me = Me;
            for (var slot = 0; slot < Slots; slot++)
            {
                var exists = slot < settings.Slots.Length;
                var player = W($"ComboBoxPlayer{slot}");
                var template = W($"ComboBoxPlayerTemplate{slot}");
                var color = W($"ComboBoxColor{slot}");
                var team = W($"ComboBoxTeam{slot}");
                var accept = W($"ButtonAccept{slot}");
                player.SetHidden(!exists);
                template.SetHidden(!exists || _mapPickerOpen);
                color.SetHidden(!exists || _mapPickerOpen);
                team.SetHidden(!exists || _mapPickerOpen);
                _memberCanSit[slot] = false;
                if (!exists)
                {
                    accept.SetHidden(true);
                    continue;
                }
                var description = settings.Slots[slot];
                var member = MemberAt(room, slot);
                var mine = member is { } seated && seated.ConnectionId == Lobby.ConnectionId;
                player.ClearCombo();
                if (IsHost)
                {
                    player.AddComboEntry(_names.Open);
                    player.AddComboEntry(_names.Closed);
                    player.AddComboEntry(_names.ComputerArmy);
                    player.AddComboEntry(member is { } taken ? MemberLabel(taken) : _session.PlayerName);
                    player.SetComboSelection(member is not null ? SeatEntry : description.Kind switch { MatchSlotKind.Human => OpenEntry, MatchSlotKind.Computer => ArmyEntry, _ => ClosedEntry });
                    player.SetEnabled(true);
                }
                else if (member is { } taken)
                {
                    player.AddComboEntry(MemberLabel(taken));
                    player.SetComboSelection(0);
                    player.SetEnabled(false);
                }
                else if (description.Kind == MatchSlotKind.Human)
                {
                    player.AddComboEntry(_names.Open);
                    player.AddComboEntry(_session.PlayerName);
                    player.SetComboSelection(0);
                    player.SetEnabled(true);
                    _memberCanSit[slot] = true;
                }
                else
                {
                    player.AddComboEntry(description.Kind == MatchSlotKind.Computer ? _names.ComputerArmy : _names.Closed);
                    player.SetComboSelection(0);
                    player.SetEnabled(false);
                }
                var taking = description.TakesPart && (member is not null || description.Kind == MatchSlotKind.Computer);
                var editable = taking && (mine || (IsHost && description.Kind == MatchSlotKind.Computer));
                var faction = _factionChoices.IndexOf(description.Faction);
                template.SetComboSelection(faction < 0 ? _factionChoices.Length - 1 : faction);
                color.SetComboSelection(description.Color >= 0 && description.Color < _colorCount ? description.Color : -1);
                team.SetComboSelection(Math.Clamp(description.Team, 0, 4));
                template.SetEnabled(editable);
                color.SetEnabled(editable);
                team.SetEnabled(editable);
                // The lamp beside a seat (the layout's ButtonAccept takes no input): lit when its member accepted. The host needs none - its start is its word.
                accept.SetHidden(member is not { IsHost: false, Ready: true });
            }
            // LanGameOptionsMenuInit: the one button is "Start" for the host and "Accept" for everybody else (GUI:Accept); somebody without a seat has nothing to accept.
            var start = W("ButtonStart");
            start.SetHidden(_mapPickerOpen);
            start.SetTextLabel(IsHost ? "GUI:Start" : "GUI:Accept");
            start.SetEnabled(IsHost || me is { Slot: >= 0 });
            W("ButtonSelectMap").SetEnabled(IsHost);
            W("ButtonSelectMap").SetHidden(_mapPickerOpen);
            W("ComboBoxStartingCash").SetEnabled(IsHost);
            W("ComboBoxStartingCash").SetComboSelection(MoneyChoices.IndexOf(settings.StartMoney));
            W("TextEntryMapDisplay").SetText(MapName(settings.MapPath));
        }
        finally
        {
            _syncing = false;
        }
    }

    private static string MemberLabel(RoomMember member) => member.Name;

    private void ShowChat(RoomStateMessage room)
    {
        var list = W("ListboxChatWindowLanGame");
        var chat = Lobby.Chat;
        for (; _chatShown < chat.Count; _chatShown++)
        {
            var line = chat[_chatShown];
            var from = room.Members.Cast<RoomMember?>().FirstOrDefault(member => member!.Value.ConnectionId == line.ConnectionId)?.Name ?? "?";
            list.AddListRow(new UiListRow($"{from}: {line.Text}"));
        }
        if (Lobby.LastRefusal is { } refusal && !ReferenceEquals(refusal, _refusalShown))
        {
            _refusalShown = refusal;
            list.AddListRow(new UiListRow($"* {NetText.Reject(refusal.Reason)}{(refusal.Text.Length > 0 ? $" ({refusal.Text})" : string.Empty)}"));
        }
    }

    // ------------------------------------------------------------------------------------------------- what this client asks for

    private string? PreferredMap()
    {
        if (Shell.Settings.Skirmish.LastMap is { } last && Shell.MapCatalog.TryGetValue(last, out var remembered) && remembered.IsMultiplayer) return remembered.VirtualPath;
        return Shell.MapCatalog.Values.Where(entry => entry.IsMultiplayer).Select(entry => entry.VirtualPath)
            .OrderBy(path => path.Replace('/', '\\').ToLowerInvariant(), StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>The room on <paramref name="mapPath"/>: as many slots as the map has start positions, the ones of <paramref name="current"/> kept as far as they fit.</summary>
    private RoomSettings? SettingsFor(string mapPath, RoomSettings current)
    {
        if (!Shell.MapCatalog.TryGetValue(mapPath, out var entry)) return null;
        if (!_terrainFingerprints.TryGetValue(mapPath, out var fingerprint))
        {
            if (Shell.Network?.TerrainFingerprint(Shell.Data, mapPath) is not { } computed) return null;
            _terrainFingerprints[mapPath] = fingerprint = computed;
        }
        var count = Math.Clamp(entry.StartPositionCount, 2, Slots);
        var slots = ImmutableArray.CreateBuilder<MatchSlotDescription>(count);
        for (var slot = 0; slot < count; slot++)
            slots.Add(slot < current.Slots.Length ? current.Slots[slot] with { StartPosition = slot }
                : new MatchSlotDescription(MatchSlotKind.Human, MatchSetup.RandomFaction, 0, _colorCount == 0 ? -1 : slot % _colorCount, slot));
        var standard = SkirmishMatchOptions.StandardAt(Shell.Data.Rate);
        var maxTicks = Shell.Network is { MatchSeconds: > 0 } network ? Shell.Data.Rate.Time.WholeSecondsToTicks(network.MatchSeconds) : standard.MaxTicks;
        return new RoomSettings(Shell.Data.Rate.TicksPerSecond, mapPath, fingerprint, maxTicks, current.StartMoney > 0 ? current.StartMoney : standard.StartMoney, standard.GraceTicks,
            standard.SciencePointInterval, slots.MoveToImmutable());
    }

    private void TakeSeat(int slot)
    {
        var seat = Settings.Slots[slot];
        Lobby.RequestSlot(slot, seat.Faction, seat.Team, seat.Color, seat.StartPosition);
    }

    private void ConfigureSlot(int slot, MatchSlotDescription description) => Lobby.Configure(Settings with { Slots = Settings.Slots.SetItem(slot, description) });

    public void SetSelectedMap(string mapPath)
    {
        if (!IsHost || SettingsFor(mapPath, Settings) is not { } settings) return;
        Lobby.Configure(settings);
    }

    public void SetMapPickerOpen(bool open)
    {
        _mapPickerOpen = open;
        foreach (var name in HiddenWhileMapPicker) W(name).SetHidden(open);
        W("ButtonBack").SetEnabled(!open);
        _shown = null;
        Sync();
    }

    private static readonly ImmutableArray<string> HiddenWhileMapPicker = ["MapWindow", "StaticTextTeam", "StaticTextFaction", "StaticTextColor", "TextEntryMapDisplay", "StaticTextMapPreview"];

    private void Start()
    {
        if (!IsHost || Lobby.Room is not { } room) return;
        Shell.PlaySound(ShellUiSounds.ButtonClick);
        // A seat nobody took does not hold the match up: it is closed with the start.
        var slots = room.Settings.Slots;
        for (var slot = 0; slot < slots.Length; slot++)
            if (slots[slot].Kind == MatchSlotKind.Human && MemberAt(room, slot) is null) slots = slots.SetItem(slot, slots[slot] with { Kind = MatchSlotKind.Closed });
        if (!slots.SequenceEqual(room.Settings.Slots)) Lobby.Configure(room.Settings with { Slots = slots });
        Lobby.RequestStart();
    }

    private void OnPlayerCombo(int slot, int value)
    {
        if (Lobby.Room is not { } room || slot >= room.Settings.Slots.Length) return;
        var description = room.Settings.Slots[slot];
        var member = MemberAt(room, slot);
        if (!IsHost)
        {
            if (_memberCanSit[slot] && value == 1) TakeSeat(slot);
            return;
        }
        var mine = member is { } seated && seated.ConnectionId == Lobby.ConnectionId;
        if (member is not null && !mine)
        {
            // Somebody sits there: the host removes people with the room, not with this box. Show what is.
            _shown = null;
            return;
        }
        switch (value)
        {
            case SeatEntry when !mine:
                if (description.Kind != MatchSlotKind.Human) ConfigureSlot(slot, description with { Kind = MatchSlotKind.Human });
                Lobby.RequestSlot(slot, description.Faction, description.Team, description.Color, description.StartPosition);
                break;
            case OpenEntry or ClosedEntry or ArmyEntry:
                if (mine) Lobby.LeaveSlot();
                ConfigureSlot(slot, description with { Kind = value switch { OpenEntry => MatchSlotKind.Human, ArmyEntry => MatchSlotKind.Computer, _ => MatchSlotKind.Closed } });
                break;
        }
    }

    private void OnSeatDetail(int slot, Func<MatchSlotDescription, MatchSlotDescription> change)
    {
        if (Lobby.Room is not { } room || slot >= room.Settings.Slots.Length) return;
        var description = change(room.Settings.Slots[slot]);
        if (MemberAt(room, slot) is { } member && member.ConnectionId == Lobby.ConnectionId) Lobby.RequestSlot(slot, description.Faction, description.Team, description.Color, description.StartPosition);
        else if (IsHost && description.Kind == MatchSlotKind.Computer) ConfigureSlot(slot, description);
    }

    private void OnSystem(UiWindowTree tree, UiEvent e)
    {
        if (_syncing) return;
        const string prefix = "LanGameOptionsMenu.wnd:";
        if (!e.Source.StartsWith(prefix, StringComparison.Ordinal)) return;
        var name = e.Source[prefix.Length..];
        switch (e.Kind)
        {
            case UiEventKind.ButtonSelected when name == "ButtonStart":
                if (IsHost) Start();
                else if (Me is { Slot: >= 0 } seated)
                {
                    Shell.PlaySound(ShellUiSounds.ButtonClick);
                    Lobby.SetReady(!seated.Ready);
                }
                break;
            case UiEventKind.ButtonSelected when name == "ButtonBack":
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Lobby.LeaveRoom();
                Shell.CloseTop();
                break;
            case UiEventKind.ButtonSelected when name == "ButtonSelectMap":
                if (!IsHost) break;
                Shell.PlaySound(ShellUiSounds.ButtonClick);
                Shell.OpenNetMapSelect(this);
                break;
            case UiEventKind.EntryEditDone when name == "TextEntryChat":
                if (e.Text is { Length: > 0 } text) Lobby.SendChat(text);
                W("TextEntryChat").SetEntryText(string.Empty);
                break;
            case UiEventKind.ComboSelected:
                Shell.PlaySound(ShellUiSounds.ComboBoxClick);
                if (name == "ComboBoxStartingCash")
                {
                    if (IsHost && Lobby.Room is not null) Lobby.Configure(Settings with { StartMoney = MoneyChoices[Math.Clamp(e.Value, 0, MoneyChoices.Length - 1)] });
                    break;
                }
                if (!char.IsAsciiDigit(name[^1])) break;
                var slot = name[^1] - '0';
                var value = e.Value;
                switch (name[..^1])
                {
                    case "ComboBoxPlayer": OnPlayerCombo(slot, value); break;
                    case "ComboBoxPlayerTemplate": OnSeatDetail(slot, seat => seat with { Faction = _factionChoices[Math.Clamp(value, 0, _factionChoices.Length - 1)] }); break;
                    case "ComboBoxColor": OnSeatDetail(slot, seat => seat with { Color = value }); break;
                    case "ComboBoxTeam": OnSeatDetail(slot, seat => seat with { Team = Math.Clamp(value, 0, 4) }); break;
                }
                break;
        }
    }

    private void OnInput(UiWindowTree tree, UiEvent e)
    {
        if (e.Kind != UiEventKind.Key || e.Value != (int)UiKey.Escape) return;
        Lobby.LeaveRoom();
        Shell.CloseTop();
    }

    public override UiDrawList BuildDrawList()
        => ShellCustomDraw.ExpandMapPreview(Tree.BuildDrawList(), new ShellMapPreviewContext(Settings.MapPath.Length == 0 ? null : MapName(Settings.MapPath)), Tree.Services.TextMeasure, Shell.Skin, Tree.Viewport);
}

/// <summary>What the shared loading screen shows of one participant.</summary>
public sealed record NetLoadRow(string Name, string Faction, int Team, uint ConnectionId, bool Computer);

/// <summary>
/// <c>MultiplayerLoadScreen.wnd</c> (docs/GAME-SHELL.md section 15.5): one row per participant of the match with its name, army, team and how far its game has
/// loaded - what every member reports to the server and the server tells everybody. A computer player has nothing to load. The start-position bubbles and the
/// portrait of the local general have no data behind them here and stay hidden.
/// </summary>
public sealed class NetLoadingScreen : ShellScreen
{
    private const int Rows = 8;
    private readonly ImmutableArray<NetLoadRow> _rows;

    public NetLoadingScreen(GameShell shell, ImmutableArray<NetLoadRow> rows, string mapName) : base(shell, ShellLayoutNames.MultiplayerLoadScreen)
    {
        _rows = rows;
        var names = new ShellSkirmishText(Shell.Data.Graph, Shell.TextSource);
        for (var row = 0; row < Rows; row++)
        {
            var shown = row < rows.Length;
            W($"StaticTextPlayer{row}").SetHidden(!shown);
            W($"StaticTextSide{row}").SetHidden(!shown);
            W($"StaticTextTeam{row}").SetHidden(!shown);
            W($"ProgressLoad{row}").SetHidden(!shown);
            W($"ButtonMapStartPosition{row}").SetHidden(true);
            if (!shown) continue;
            W($"StaticTextPlayer{row}").SetText(rows[row].Computer ? names.ComputerArmy : rows[row].Name);
            W($"StaticTextSide{row}").SetText(rows[row].Faction == MatchSetup.RandomFaction ? names.Random : names.Faction(rows[row].Faction));
            W($"StaticTextTeam{row}").SetText(names.Team(rows[row].Team));
            W($"ProgressLoad{row}").SetProgress(rows[row].Computer ? 100 : 0);
        }
        W("LocalGeneralPortrait").SetHidden(true);
        W("LocalGeneralFeatures").SetHidden(true);
        W("LocalGeneralName").SetText(mapName);
    }

    public void SetProgress(IReadOnlyDictionary<uint, byte> progress)
    {
        for (var row = 0; row < _rows.Length; row++)
            if (!_rows[row].Computer) W($"ProgressLoad{row}").SetProgress(progress.GetValueOrDefault(_rows[row].ConnectionId));
    }

    public static ImmutableArray<NetLoadRow> RowsOf(RoomStateMessage room)
    {
        var rows = ImmutableArray.CreateBuilder<NetLoadRow>();
        for (var slot = 0; slot < room.Settings.Slots.Length; slot++)
        {
            var description = room.Settings.Slots[slot];
            if (!description.TakesPart) continue;
            var member = room.Members.Cast<RoomMember?>().FirstOrDefault(candidate => candidate!.Value.Slot == slot);
            if (description.Kind == MatchSlotKind.Human && member is null) continue;
            rows.Add(new NetLoadRow(member?.Name ?? string.Empty, description.Faction, description.Team, member?.ConnectionId ?? 0, description.Kind == MatchSlotKind.Computer));
        }
        return rows.ToImmutable();
    }
}
