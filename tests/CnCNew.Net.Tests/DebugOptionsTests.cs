using CnCNew.Net;
using CnCNew.Simulation.Skirmish;

// The debug options of a match travel in the room's settings (protocol 10) and reach every client's match description.
static partial class NetTests
{
    public static void RoomCarriesTheDebugOptions()
    {
        var debug = new MatchDebugOptions(MatchDebugOptions.QuickBuildPercent, 120, 5, PlaceBuildingsInFog: true, QuickBuildHumansOnly: true);
        var settings = TwoSeats() with { Debug = debug };

        // ---- The wire: the options survive the bytes, are part of what the room's settings are compared by, and an out-of-range value is refused as a malformed message.
        var bytes = MessageCodec.Encode(new RoomConfigMessage(settings));
        Check.That(MessageCodec.TryDecode(bytes, out var decoded, out var error) && decoded is RoomConfigMessage { Settings.Debug: { BuildTimePercent: 10, NeutralRespawnSeconds: 120, StartingGeneralPoints: 5, PlaceBuildingsInFog: true, QuickBuildHumansOnly: true } }, $"the options survive the wire ({error})");
        Check.That(!settings.SameAs(TwoSeats()) && settings.SameAs(((RoomConfigMessage)decoded!).Settings) && !settings.SameAs(settings with { Debug = debug with { StartingGeneralPoints = 6 } }) && !settings.SameAs(settings with { Debug = debug with { PlaceBuildingsInFog = false } }), "the options count in the comparison of settings");
        Check.That(Protocol.Version == 11 && !MessageCodec.TryDecode(MessageCodec.Encode(new RoomConfigMessage(TwoSeats() with { Debug = new MatchDebugOptions(BuildTimePercent: 0) })), out _, out var refused) && refused.Length > 0, "a build time of 0 % is a malformed message");
        Check.That(!MessageCodec.TryDecode(MessageCodec.Encode(new RoomConfigMessage(TwoSeats() with { Debug = new MatchDebugOptions(NeutralRespawnSeconds: -5) })), out _, out _), "a negative respawn time is a malformed message");

        // ---- The description: settings and description convert both ways with the options.
        var description = settings.ToDescription(Released.Id, Definitions, 1, 2);
        Check.That(description.Debug == debug && description.ToRoomSettings(withSeed: false).Debug == debug, "the options travel through the description and back");
        Check.That(description.Fingerprint != (settings with { Debug = MatchDebugOptions.None }).ToDescription(Released.Id, Definitions, 1, 2).Fingerprint, "another option is another match");

        // ---- The room: the server shows the options to everybody, hands them to every client's description, refuses what is out of range and keeps them when the host changes the room.
        var net = new Net();
        var host = net.Join();
        var guest = net.Join();
        net.Pump();
        host.Configure(settings);
        net.Pump();
        Check.That(net.Server.Settings.Debug == debug && guest.Room!.Settings.Debug == debug, "everybody sees the options of the room");
        host.Configure(settings with { StartMoney = 20_000 });
        net.Pump();
        Check.That(net.Server.Settings.Debug == debug && net.Server.Settings.StartMoney == 20_000, "a change of the money keeps the options");
        host.Configure(settings with { Debug = new MatchDebugOptions(BuildTimePercent: 5000) });
        net.Pump();
        Check.That(net.Server.Settings.Debug == debug, "options out of range never reach the room (the message is refused when it is read)");
        host.Configure(settings with { Debug = MatchDebugOptions.None });
        net.Pump();
        Check.That(net.Server.Settings.Debug.IsDefault && guest.Room!.Settings.Debug.IsDefault, "the host takes them back");
        host.Configure(settings);
        net.Pump();
        host.RequestSlot(0, "Alpha", startPosition: 0);
        guest.RequestSlot(1, "Beta", startPosition: 1);
        guest.SetReady(true);
        net.Pump();
        host.RequestStart();
        net.Pump();
        Check.That(net.Clients.All(client => client.Phase == ClientPhase.Loading && client.Description!.Debug == debug && client.Description.Equals(host.Description)), "the match starts with the options in every client's description");
    }
}
