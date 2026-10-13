namespace CnCNew.Net;

/// <summary>
/// Asks a server which rooms it has (protocol version 5, docs/NETWORK-PROTOCOL.md section 22.1) on a connection that has not said its hello yet. The same connection
/// then carries the hello of a <see cref="LockstepClient"/> - from that moment this browser must not be pumped any more, the messages are the client's.
/// </summary>
public sealed class RoomBrowser(IConnection connection)
{
    public RoomListMessage? List { get; private set; }
    /// <summary>How many lists have arrived; a caller that asked again waits for this to rise.</summary>
    public int Lists { get; private set; }
    public int Requests { get; private set; }
    /// <summary>The server refused: it lists nothing, speaks another protocol version, or this connection has asked too often.</summary>
    public RejectMessage? Rejection { get; private set; }
    public long Discarded { get; private set; }
    public bool IsOpen => connection.IsOpen;

    public void Request()
    {
        Requests++;
        connection.Send(MessageCodec.Encode(new RoomListRequestMessage(Protocol.Version)));
    }

    public void Pump()
    {
        while (connection.TryReceive(out var bytes))
        {
            if (!MessageCodec.TryDecode(bytes, out var message, out _)) Discarded++;
            else if (message is RoomListMessage list)
            {
                List = list;
                Lists++;
            }
            else if (message is RejectMessage reject) Rejection = reject;
            else Discarded++;
        }
    }
}
