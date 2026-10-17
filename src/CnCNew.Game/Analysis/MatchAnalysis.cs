using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CnCNew.Game.Analysis;

/// <summary>How many of one type a player built, lost or finished, and their value at list price (<c>BuildCost</c>).</summary>
public sealed record AnalysisCount(string Template, int Count, long Value);

/// <summary>
/// One player of an analysed match. Money values are whole credits. <paramref name="Spent"/> is the list price of what the player began or got: every unit when it leaves its
/// factory, every building when its construction begins (less the buildings whose construction was cancelled), every upgrade when it completes - not what the player's money
/// paid (cost modifiers and refunds of cancelled production are not seen by the analysis). <paramref name="CommandsPerMinute"/> counts the player's commands in every whole
/// minute of game time from the start of the match; a computer player's commands are counted as well.
/// </summary>
public sealed record AnalysisPlayer(
    int Player, string Name, string Faction, int Team, bool IsComputer, string Ai, int ColorRgb, bool IsWinner, bool IsDefeated,
    long Collected, long Spent, int Kills, long KillsValue,
    ImmutableArray<AnalysisCount> UnitsBuilt, ImmutableArray<AnalysisCount> UnitsLost, ImmutableArray<AnalysisCount> StructuresBuilt, ImmutableArray<AnalysisCount> StructuresLost,
    int StructuresSold, int Upgrades, int Sciences, int SpecialPowers, int Superweapons, int Commands, ImmutableArray<int> CommandsPerMinute)
{
    public int UnitsBuiltCount => UnitsBuilt.Sum(item => item.Count);
    public int UnitsLostCount => UnitsLost.Sum(item => item.Count);
    public long UnitsLostValue => UnitsLost.Sum(item => item.Value);
    public int StructuresLostCount => StructuresLost.Sum(item => item.Count);
    public long StructuresLostValue => StructuresLost.Sum(item => item.Value);
}

/// <summary>
/// One player's state at one moment of the match: the samples lie every <see cref="MatchAnalysis.SampleSeconds"/> of game time on the original's frame grid (the frame the
/// world has just finished), plus one at the end. Cumulative fields count from the start of the analysis. <paramref name="Regions"/> is the number of the map's
/// <see cref="MatchAnalysis.RegionsPerSide"/>² regions the player controls: the regions in which the value of its army and finished buildings is larger than every other
/// player's (workers do not count).
/// </summary>
public sealed record AnalysisSample(
    long Frame, int Player, long Money, long Collected, long IncomePerMinute, long Spent,
    long ArmyValue, int ArmyUnits, long InfantryValue, long VehicleValue, long AircraftValue, int Workers, int Structures,
    int PowerProduced, int PowerUsed, int Regions, int UnitsLost, int StructuresLost, int Kills)
{
    public long Second => Frame / CnCNew.Data.OriginalFrame.PerSecond;
}

/// <summary>
/// One event of the match with its place: <c>unit-lost</c> and <c>structure-lost</c> (a unit, worker or building died; <paramref name="Other"/> is the player of its killer, 0 for
/// none), <c>structure-built</c>, <c>structure-sold</c>, <c>captured</c> (<paramref name="Player"/> is the new owner, <paramref name="Other"/> the old one), <c>upgrade</c>,
/// <c>science</c>, <c>special-power</c> and <c>superweapon</c> (a power of a building of the kind <c>FS_SUPERWEAPON</c>; the place is the target of the command), <c>defeated</c>.
/// <paramref name="X"/> and <paramref name="Y"/> are whole world units, -1 when the event has no place.
/// </summary>
public sealed record AnalysisEvent(long Frame, string Kind, int Player, int Other, string Name, long Value, int X, int Y)
{
    public long Second => Frame / CnCNew.Data.OriginalFrame.PerSecond;
}

/// <summary>A heat map over the map: <see cref="MatchAnalysis.HeatGridSize"/>² cells, row by row from the map's origin; <paramref name="Player"/> 0 is everybody.</summary>
public sealed record AnalysisHeat(string Kind, int Player, ImmutableArray<int> Cells)
{
    public int Maximum => Cells.IsDefaultOrEmpty ? 0 : Cells.Max();
}

/// <summary>
/// The statistics of one match (docs/MATCH-ANALYSIS.md), computed by <see cref="MatchAnalyzer"/> from what the simulation tells every tick - its events and its world - while
/// the match runs or while its replay is played again. The same match gives the same analysis: it holds no wall time, no culture and no order of a hash table, and its
/// export (<see cref="MatchAnalysisFormat"/>) is the same bytes on every run. Times are frames of the original (30 to a second of game time) at every tick rate.
/// </summary>
public sealed record MatchAnalysis(
    string MapPath, int TickRate, string Rules, long StartFrame, long EndFrame, int Winner, string Ending, string Source,
    int MapWidth, int MapHeight,
    ImmutableArray<AnalysisPlayer> Players, ImmutableArray<AnalysisSample> Samples, ImmutableArray<AnalysisEvent> Events, ImmutableArray<AnalysisHeat> Heat,
    int UnresolvedEvents)
{
    public const string Format = "cncnew-match-analysis";
    public const int SchemaVersion = 1;
    public const int SampleSeconds = 5;
    public const int RegionsPerSide = 8;
    public const int HeatGridSize = 32;

    public long DurationSeconds => (EndFrame - StartFrame) / CnCNew.Data.OriginalFrame.PerSecond;

    public AnalysisPlayer? PlayerOf(int player) => Players.FirstOrDefault(item => item.Player == player);

    /// <summary>The samples of one player in time order.</summary>
    public IEnumerable<AnalysisSample> SamplesOf(int player) => Samples.Where(sample => sample.Player == player);

    public AnalysisHeat? HeatOf(string kind, int player) => Heat.FirstOrDefault(item => item.Kind == kind && item.Player == player);
}

/// <summary>
/// The export of a <see cref="MatchAnalysis"/>: one JSON document with the schema name and version and three TSV tables (players, samples, events). Every number is written
/// with the invariant culture, every line ends with "\n", and the order is the analysis' own - so two exports of the same analysis are the same bytes.
/// </summary>
public static class MatchAnalysisFormat
{
    private static string Hex(int rgb) => rgb.ToString("X6", CultureInfo.InvariantCulture);

    public static byte[] ToJson(MatchAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            json.WriteString("format", MatchAnalysis.Format);
            json.WriteNumber("schemaVersion", MatchAnalysis.SchemaVersion);
            json.WriteString("source", analysis.Source);
            json.WriteString("map", analysis.MapPath);
            json.WriteNumber("tickRate", analysis.TickRate);
            json.WriteString("rules", analysis.Rules);
            json.WriteNumber("startFrame", analysis.StartFrame);
            json.WriteNumber("endFrame", analysis.EndFrame);
            json.WriteNumber("durationSeconds", analysis.DurationSeconds);
            json.WriteNumber("winner", analysis.Winner);
            json.WriteString("ending", analysis.Ending);
            json.WriteNumber("sampleSeconds", MatchAnalysis.SampleSeconds);
            json.WriteNumber("regionsPerSide", MatchAnalysis.RegionsPerSide);
            json.WriteNumber("heatGridSize", MatchAnalysis.HeatGridSize);
            json.WriteNumber("mapWidth", analysis.MapWidth);
            json.WriteNumber("mapHeight", analysis.MapHeight);
            json.WriteNumber("unresolvedEvents", analysis.UnresolvedEvents);
            json.WriteStartArray("players");
            foreach (var player in analysis.Players)
            {
                json.WriteStartObject();
                json.WriteNumber("player", player.Player);
                json.WriteString("name", player.Name);
                json.WriteString("faction", player.Faction);
                json.WriteNumber("team", player.Team);
                json.WriteBoolean("computer", player.IsComputer);
                json.WriteString("ai", player.Ai);
                json.WriteString("color", Hex(player.ColorRgb));
                json.WriteBoolean("winner", player.IsWinner);
                json.WriteBoolean("defeated", player.IsDefeated);
                json.WriteNumber("collected", player.Collected);
                json.WriteNumber("spent", player.Spent);
                json.WriteNumber("kills", player.Kills);
                json.WriteNumber("killsValue", player.KillsValue);
                json.WriteNumber("structuresSold", player.StructuresSold);
                json.WriteNumber("upgrades", player.Upgrades);
                json.WriteNumber("sciences", player.Sciences);
                json.WriteNumber("specialPowers", player.SpecialPowers);
                json.WriteNumber("superweapons", player.Superweapons);
                json.WriteNumber("commands", player.Commands);
                json.WriteStartArray("commandsPerMinute");
                foreach (var count in player.CommandsPerMinute) json.WriteNumberValue(count);
                json.WriteEndArray();
                Counts(json, "unitsBuilt", player.UnitsBuilt);
                Counts(json, "unitsLost", player.UnitsLost);
                Counts(json, "structuresBuilt", player.StructuresBuilt);
                Counts(json, "structuresLost", player.StructuresLost);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("samples");
            foreach (var sample in analysis.Samples)
            {
                json.WriteStartObject();
                json.WriteNumber("frame", sample.Frame);
                json.WriteNumber("player", sample.Player);
                json.WriteNumber("money", sample.Money);
                json.WriteNumber("collected", sample.Collected);
                json.WriteNumber("incomePerMinute", sample.IncomePerMinute);
                json.WriteNumber("spent", sample.Spent);
                json.WriteNumber("armyValue", sample.ArmyValue);
                json.WriteNumber("armyUnits", sample.ArmyUnits);
                json.WriteNumber("infantryValue", sample.InfantryValue);
                json.WriteNumber("vehicleValue", sample.VehicleValue);
                json.WriteNumber("aircraftValue", sample.AircraftValue);
                json.WriteNumber("workers", sample.Workers);
                json.WriteNumber("structures", sample.Structures);
                json.WriteNumber("powerProduced", sample.PowerProduced);
                json.WriteNumber("powerUsed", sample.PowerUsed);
                json.WriteNumber("regions", sample.Regions);
                json.WriteNumber("unitsLost", sample.UnitsLost);
                json.WriteNumber("structuresLost", sample.StructuresLost);
                json.WriteNumber("kills", sample.Kills);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("events");
            foreach (var item in analysis.Events)
            {
                json.WriteStartObject();
                json.WriteNumber("frame", item.Frame);
                json.WriteString("kind", item.Kind);
                json.WriteNumber("player", item.Player);
                json.WriteNumber("other", item.Other);
                json.WriteString("name", item.Name);
                json.WriteNumber("value", item.Value);
                json.WriteNumber("x", item.X);
                json.WriteNumber("y", item.Y);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("heat");
            foreach (var heat in analysis.Heat)
            {
                json.WriteStartObject();
                json.WriteString("kind", heat.Kind);
                json.WriteNumber("player", heat.Player);
                json.WriteStartArray("cells");
                foreach (var cell in heat.Cells) json.WriteNumberValue(cell);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void Counts(Utf8JsonWriter json, string name, ImmutableArray<AnalysisCount> counts)
    {
        json.WriteStartArray(name);
        foreach (var item in counts)
        {
            json.WriteStartObject();
            json.WriteString("template", item.Template);
            json.WriteNumber("count", item.Count);
            json.WriteNumber("value", item.Value);
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }

    /// <summary>A text field of a TSV row: tabs and line breaks would break the table and are written as spaces.</summary>
    private static string Cell(string text) => text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static string Row(params object[] cells)
        => string.Join('\t', cells.Select(cell => cell switch
        {
            string text => Cell(text),
            bool flag => flag ? "1" : "0",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => Cell(cell?.ToString() ?? string.Empty),
        })) + "\n";

    public static string PlayersTsv(MatchAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var text = new StringBuilder();
        text.Append(Row("player", "name", "faction", "team", "computer", "ai", "color", "winner", "defeated", "collected", "spent", "unitsBuilt", "unitsLost", "unitsLostValue",
            "structuresBuilt", "structuresLost", "structuresLostValue", "kills", "killsValue", "structuresSold", "upgrades", "sciences", "specialPowers", "superweapons", "commands"));
        foreach (var player in analysis.Players)
            text.Append(Row(player.Player, player.Name, player.Faction, player.Team, player.IsComputer, player.Ai, Hex(player.ColorRgb), player.IsWinner, player.IsDefeated,
                player.Collected, player.Spent, player.UnitsBuiltCount, player.UnitsLostCount, player.UnitsLostValue, player.StructuresBuilt.Sum(item => item.Count),
                player.StructuresLostCount, player.StructuresLostValue, player.Kills, player.KillsValue, player.StructuresSold, player.Upgrades, player.Sciences, player.SpecialPowers,
                player.Superweapons, player.Commands));
        return text.ToString();
    }

    public static string SamplesTsv(MatchAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var text = new StringBuilder();
        text.Append(Row("frame", "second", "player", "money", "collected", "incomePerMinute", "spent", "armyValue", "armyUnits", "infantryValue", "vehicleValue", "aircraftValue",
            "workers", "structures", "powerProduced", "powerUsed", "regions", "unitsLost", "structuresLost", "kills"));
        foreach (var sample in analysis.Samples)
            text.Append(Row(sample.Frame, sample.Second, sample.Player, sample.Money, sample.Collected, sample.IncomePerMinute, sample.Spent, sample.ArmyValue, sample.ArmyUnits,
                sample.InfantryValue, sample.VehicleValue, sample.AircraftValue, sample.Workers, sample.Structures, sample.PowerProduced, sample.PowerUsed, sample.Regions,
                sample.UnitsLost, sample.StructuresLost, sample.Kills));
        return text.ToString();
    }

    public static string EventsTsv(MatchAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var text = new StringBuilder();
        text.Append(Row("frame", "second", "kind", "player", "other", "name", "value", "x", "y"));
        foreach (var item in analysis.Events) text.Append(Row(item.Frame, item.Second, item.Kind, item.Player, item.Other, item.Name, item.Value, item.X, item.Y));
        return text.ToString();
    }

    /// <summary>The files of an export by name, in a fixed order: <c>analysis.json</c>, <c>players.tsv</c>, <c>samples.tsv</c>, <c>events.tsv</c>.</summary>
    public static ImmutableArray<(string Name, byte[] Bytes)> Files(MatchAnalysis analysis)
        =>
        [
            ("analysis.json", ToJson(analysis)),
            ("players.tsv", Encoding.UTF8.GetBytes(PlayersTsv(analysis))),
            ("samples.tsv", Encoding.UTF8.GetBytes(SamplesTsv(analysis))),
            ("events.tsv", Encoding.UTF8.GetBytes(EventsTsv(analysis))),
        ];
}
