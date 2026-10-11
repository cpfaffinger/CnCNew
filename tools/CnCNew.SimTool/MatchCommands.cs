using System.Globalization;
using CnCNew.Formats;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

namespace CnCNew.SimTool;

/// <summary>
/// <c>match-describe</c> and <c>match-start</c> (phase 24): the match description as a file. One process writes the canonical bytes of a match, any number of other processes
/// build tick 0 (and some ticks more) from those bytes and print the session hash - the check that the same bytes give the same match in another process.
/// </summary>
internal static class MatchCommands
{
    private const string DescribeUsage = "Usage: CnCNew.SimTool match-describe <content-dir> --map <map> --slots <slot>[;<slot>...] --out <file> [--seed N] [--minutes M]\n" +
        "  slot: computer|human|closed|spectator[:<faction>|random[:<start position>|random[:<team>]]]";
    private const string StartUsage = "Usage: CnCNew.SimTool match-start <content-dir> <description-file> [--ticks N]";

    public static int Describe(string[] args)
    {
        var mapPath = ManifestCommands.Option(args, "--map");
        var slotText = ManifestCommands.Option(args, "--slots");
        var outPath = ManifestCommands.Option(args, "--out");
        if (args.Length < 2 || mapPath is null || slotText is null || outPath is null) return ManifestCommands.Fail(DescribeUsage);
        var rate = ToolRate.Current;
        var context = SimContext.Load(args[1], rate);
        using var stream = context.Content.OpenRead(mapPath);
        var terrain = TerrainMap.Build(SageMapDecoder.Decode(stream), mapPath);
        var standard = SkirmishMatchOptions.StandardAt(rate);
        var seed = uint.Parse(ManifestCommands.Option(args, "--seed") ?? "0", CultureInfo.InvariantCulture);
        var minutes = long.Parse(ManifestCommands.Option(args, "--minutes") ?? "60", CultureInfo.InvariantCulture);
        var slots = slotText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ParseSlot).ToArray();
        var description = new MatchDescription(rate.TicksPerSecond, mapPath, terrain.Fingerprint, context.Fingerprint.ToUpperInvariant(), seed == 0 ? standard.WorldSeed : seed ^ 0xC0FFEE06,
            seed == 0 ? standard.AiSeed : seed, checked(minutes * 60 * rate.TicksPerSecond), standard.StartMoney, standard.GraceTicks, standard.SciencePointInterval, [..slots]);
        description.Resolve(context.Catalog, terrain);
        File.WriteAllBytes(outPath, description.Encode());
        Console.WriteLine($"match description {description.Fingerprint}: {mapPath}, {rate}, {slots.Length} slots, {new FileInfo(outPath).Length} bytes -> {outPath}");
        return 0;
    }

    private static MatchSlotDescription ParseSlot(string text)
    {
        var parts = text.Split(':');
        var kind = parts[0].ToLowerInvariant() switch
        {
            "computer" => MatchSlotKind.Computer,
            "human" => MatchSlotKind.Human,
            "closed" => MatchSlotKind.Closed,
            "spectator" => MatchSlotKind.Spectator,
            _ => throw new ArgumentException($"A slot is computer, human, closed or spectator, not '{parts[0]}'."),
        };
        var faction = parts.Length > 1 && !parts[1].Equals("random", StringComparison.OrdinalIgnoreCase) ? parts[1] : string.Empty;
        var start = parts.Length > 2 && !parts[2].Equals("random", StringComparison.OrdinalIgnoreCase) ? int.Parse(parts[2], CultureInfo.InvariantCulture) : -1;
        var team = parts.Length > 3 ? int.Parse(parts[3], CultureInfo.InvariantCulture) : 0;
        return new MatchSlotDescription(kind, faction, team, StartPosition: start);
    }

    public static int Start(string[] args)
    {
        if (args.Length < 3) return ManifestCommands.Fail(StartUsage);
        var description = MatchDescription.Decode(File.ReadAllBytes(args[2]));
        var ticks = long.Parse(ManifestCommands.Option(args, "--ticks") ?? "0", CultureInfo.InvariantCulture);
        // The description names its rate; the process builds its catalog for it, whatever its own default is.
        var context = SimContext.Load(args[1], description.Rate);
        using var stream = context.Content.OpenRead(description.MapPath);
        var map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, description.MapPath);
        var session = SkirmishMatchSession.Start(description, context.Catalog, terrain, map.Objects);
        Console.WriteLine($"description {description.Fingerprint}");
        foreach (var slot in session.Match.Slots)
            Console.WriteLine($"player {slot.Player.Value}: slot {slot.Slot}, {slot.Description.Kind}, {slot.Participant.Faction.Name}, start position {slot.StartPosition}, team {slot.Description.Team}, colour {slot.Color}");
        Console.WriteLine($"tick 0: session {session.StateHash():X16}, world {session.World.StateHash:X16}");
        if (ticks > 0)
        {
            while (session.World.Tick < ticks && !session.IsOver) session.Step();
            Console.WriteLine($"tick {session.World.Tick}: session {session.StateHash():X16}, world {session.World.StateHash:X16}");
        }
        return 0;
    }
}
