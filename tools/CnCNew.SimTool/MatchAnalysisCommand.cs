using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Analysis;
using CnCNew.Game.Data;
using CnCNew.Game.Replays;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Time;

namespace CnCNew.SimTool;

/// <summary>
/// <c>match-analysis</c> (phase 36, docs/MATCH-ANALYSIS.md): the analysis of a replay file of the replay browser - the match played again without a picture, every recorded
/// session hash compared - written as <c>analysis.json</c>, <c>players.tsv</c>, <c>samples.tsv</c>, <c>events.tsv</c> and the graphical report <c>report.html</c>. The game data
/// is loaded at the rate and with the rules the file names. The SHA-256 of every file is printed: two runs over the same file print the same lines.
/// </summary>
internal static class MatchAnalysisCommand
{
    private const string Usage = "Usage: CnCNew.SimTool match-analysis <content-dir> <file.cncreplay> --out <directory> [--title TEXT]";

    public static int Run(string[] args)
    {
        var outDirectory = ManifestCommands.Option(args, "--out");
        if (args.Length < 3 || outDirectory is null) return ManifestCommands.Fail(Usage);
        var file = args[2];
        var bytes = File.ReadAllBytes(file);
        MatchRecordingInfo info;
        try
        {
            info = MatchRecordingFormat.Read(bytes).Info;
        }
        catch (ReplayRefusedException exception)
        {
            Console.Error.WriteLine($"{Path.GetFileName(file)}: {exception.Message}");
            return 2;
        }
        var rules = RuleProfile.Parse(info.Rules);
        var data = GameData.Load(args[1], rate: SimulationRate.FromTicksPerSecond(info.TickRate), rules: rules);
        var name = Path.GetFileNameWithoutExtension(file);
        var watch = Stopwatch.StartNew();
        MatchAnalysis analysis;
        try
        {
            analysis = ReplayAnalysis.Run(data, bytes, source: Path.GetFileName(file));
        }
        catch (ReplayRefusedException exception)
        {
            Console.Error.WriteLine($"{Path.GetFileName(file)}: {exception.Message}");
            return 2;
        }
        var seconds = watch.Elapsed.TotalSeconds;
        Directory.CreateDirectory(outDirectory);
        var title = ManifestCommands.Option(args, "--title") ?? $"Match analysis: {name}";
        var files = MatchAnalysisFormat.Files(analysis).Add(("report.html", Encoding.UTF8.GetBytes(MatchReport.ToHtml(analysis, title))));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Path.GetFileName(file)}: {analysis.MapPath}, {analysis.TickRate} Hz, rules {analysis.Rules}, {AnalysisCharts.Time(analysis.DurationSeconds)} of game time, {analysis.Players.Length} players, {analysis.Samples.Length} samples, {analysis.Events.Length} events; played again in {seconds:F1} s"));
        foreach (var player in analysis.Players)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  player {player.Player} {AnalysisCharts.LabelOf(player)}: {(player.IsWinner ? "won" : player.IsDefeated ? "defeated" : "-")}, collected {player.Collected}, spent {player.Spent}, units built {player.UnitsBuiltCount}, lost {player.UnitsLostCount + player.StructuresLostCount}, kills {player.Kills}, commands {player.Commands}"));
        foreach (var (fileName, content) in files)
        {
            File.WriteAllBytes(Path.Combine(outDirectory, fileName), content);
            Console.WriteLine($"  {Convert.ToHexString(SHA256.HashData(content))}  {content.Length,9}  {fileName}");
        }
        if (analysis.UnresolvedEvents > 0) Console.WriteLine($"  {analysis.UnresolvedEvents} event(s) whose object was gone when they were read (counted nowhere else)");
        return 0;
    }
}
