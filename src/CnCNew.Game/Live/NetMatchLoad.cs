using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Game.Data;
using CnCNew.Game.Lockstep;
using CnCNew.Net;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Game.Live;

/// <summary>
/// The match of a room, built behind the shared load screen (docs/GAME-SHELL.md section 15.5): what <see cref="LiveMatch.Join(GameData, RemoteRelay, Vector2)"/>
/// does in one blocking call, in stages of one call each, so that between two stages a frame is drawn, the connection is pumped and the server is told how far this
/// game is - the percentage every member's load screen shows. The stages are the work there is: the map and its terrain, the session, the word to the server, and
/// then the wait for its go, which comes when every member has built the same tick 0. Between the session and the word to the server the caller finds the match in
/// <see cref="Built"/> and prepares what it shows of it (a window client its world layers): the server's clock starts with the go, and whatever a game still has
/// to do then is time it is behind. Whoever calls <see cref="Step"/> pumps the connection between the calls.
/// </summary>
public sealed class NetMatchLoad(GameData data, RemoteRelay remote, Vector2 viewport)
{
    private int _stage;
    private MapData? _map;
    private TerrainMap? _terrain;
    private LiveMatch? _match;

    /// <summary>What this game last reported to the server: 0, 10, 60, 90 or 100.</summary>
    public int Percent { get; private set; }

    /// <summary>The match as soon as its session is built, before the server is told; null before.</summary>
    public LiveMatch? Built => _match;

    /// <summary>The match once the server let it begin; null before.</summary>
    public LiveMatch? Match => _stage == 5 ? _match : null;

    /// <summary>
    /// One stage. True when the match runs (<see cref="Match"/>). Throws <see cref="RemoteRelayException"/> with a message fit to show when the match cannot be
    /// built here, the server ended it before it began, or the connection is gone.
    /// </summary>
    public bool Step()
    {
        var client = remote.Client;
        if (!client.IsOpen) throw new RemoteRelayException("The connection to the server ended while the match was loading.");
        switch (_stage)
        {
            case 0:
                Report(10);
                _stage = 1;
                return false;
            case 1:
                (_map, _terrain) = LiveMatch.MapOf(data, client.Description ?? throw new RemoteRelayException("The server started no match."));
                Report(60);
                _stage = 2;
                return false;
            case 2:
                _match = LiveMatch.Build(data, remote, _map!, _terrain!, viewport, wait: false, report: false);
                Report(90);
                _stage = 3;
                return false;
            case 3:
                _match!.ReportLoaded();
                Percent = 100;
                _stage = 4;
                return false;
            case 4:
                if (client.Phase == ClientPhase.Ended)
                    throw new RemoteRelayException(client.Desync is not null ? "The server ended the match before it began: the games did not build the same start."
                        : "The server ended the match before it began.");
                if (client.Phase != ClientPhase.InMatch) return false;
                _stage = 5;
                return true;
            default:
                return true;
        }
    }

    private void Report(int percent)
    {
        Percent = percent;
        remote.Client.ReportLoadProgress(percent);
        remote.Flush();
    }
}
