using System.Diagnostics;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.Snapshots;
using CnCNew.Formats;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Save;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

const string Usage = """
    Usage: CnCNew.CampaignTool <command> <content-dir> [options]
      list <content-dir>                        campaigns and missions of Campaign.ini, validated against the content (maps exist, chains close)
      audit <content-dir> [--campaign NAME] [--map TEXT] [--ticks N] [--no-probes] [--no-continuation] [--explain]
                                                every campaign mission headless: two fresh runs, save/restore continuation, scripted-ending probes,
                                                script coverage (missing action/condition hits, unresolved references)
      playthrough <content-dir> --campaign NAME [--ticks N]
                                                plays a whole campaign headless: starts each mission, forces its victory ending, carries the progress
                                                and a savegame from mission to mission
    """;

if (args.Length < 2)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    var content = ContentFileSystem.Open(args[1]);
    using var catalogStream = content.OpenRead("Data/INI/Campaign.ini");
    var campaigns = CampaignCatalog.Parse(catalogStream);
    return args[0] switch
    {
        "list" => List(content, campaigns),
        "audit" => Audit(content, campaigns, args),
        "playthrough" => Playthrough(content, campaigns, args),
        _ => Fail(Usage),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

static string? Arg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static int List(ContentFileSystem content, CampaignCatalog campaigns)
{
    Console.WriteLine($"{campaigns.Campaigns.Length} campaigns, {campaigns.Campaigns.Sum(campaign => campaign.Missions.Length)} missions.");
    foreach (var campaign in campaigns.Campaigns)
    {
        Console.WriteLine($"  {campaign.Name}{(campaign.IsChallenge ? " [challenge]" : "")}: first {campaign.FirstMission}, {campaign.Missions.Length} missions{(campaign.PlayerFaction.Length > 0 ? $", faction {campaign.PlayerFaction}" : "")}");
        foreach (var mission in campaign.Sequence) Console.WriteLine($"    {mission.Name,-10} {mission.MapPath}{(mission.NextMission.Length > 0 ? $" -> {mission.NextMission}" : " (last)")}");
    }
    foreach (var key in campaigns.UnknownKeys) Console.WriteLine($"  unknown key: {key}");
    var problems = campaigns.Validate(content.Exists);
    foreach (var problem in problems) Console.WriteLine($"  PROBLEM: {problem}");
    Console.WriteLine(problems.IsEmpty ? "Campaign.ini is consistent with the content." : $"{problems.Length} problem(s).");
    return problems.IsEmpty && campaigns.UnknownKeys.IsEmpty ? 0 : 1;
}

static (MissionContext Context, TemplateCatalog Catalog, string Fingerprint) Compile(ContentFileSystem content)
{
    var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
    var graph = DefinitionCompiler.Compile(new ContentDefinitionSource(content), ModuleRegistry.Default, options);
    var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default);
    var fingerprint = DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256;
    return (new MissionContext(catalog, fingerprint, path =>
    {
        using var stream = content.OpenRead(path);
        return SageMapDecoder.Decode(stream);
    }), catalog, fingerprint);
}

static int Audit(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    var ticks = long.Parse(Arg(args, "--ticks") ?? "1800", CultureInfo.InvariantCulture);
    var filterCampaign = Arg(args, "--campaign");
    var filterMap = Arg(args, "--map");
    var probes = !args.Contains("--no-probes");
    var continuation = !args.Contains("--no-continuation");
    var explain = args.Contains("--explain");
    var humanSideName = Arg(args, "--human-side") ?? string.Empty;
    var (context, catalog, fingerprint) = Compile(content);
    var missing = new SortedDictionary<string, long>(StringComparer.Ordinal);
    var missingMissions = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var missingConditions = new SortedDictionary<string, long>(StringComparer.Ordinal);
    var missingConditionMissions = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var diagnostics = new SortedDictionary<string, long>(StringComparer.Ordinal);
    var skippedTemplates = new SortedDictionary<string, int>(StringComparer.Ordinal);
    int run = 0, deterministic = 0, continued = 0, victoryProbes = 0, victoryReached = 0, defeatProbes = 0, defeatReached = 0, failed = 0;
    long executedActions = 0, forwardedActions = 0, missingActionTotal = 0, missingConditionTotal = 0, executedScripts = 0;
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var watch = Stopwatch.StartNew();
    foreach (var campaign in campaigns.Campaigns)
    {
        if (filterCampaign is not null && !campaign.Name.Equals(filterCampaign, StringComparison.OrdinalIgnoreCase)) continue;
        foreach (var mission in campaign.Sequence)
        {
            if (filterMap is not null && !mission.MapPath.Contains(filterMap, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(mission.MapPath)) continue;
            try
            {
                var map = context.LoadMap(mission.MapPath);
                var terrain = TerrainMap.Build(map, mission.MapPath);
                var options = MissionOptions.Standard with { HumanSideName = humanSideName };
                var first = MissionScenario.Run(options, catalog, fingerprint, terrain, map, ticks);
                var second = MissionScenario.Run(options, catalog, fingerprint, terrain, map, ticks);
                var same = first.Chain == second.Chain && first.FinalHash == second.FinalHash;
                var restoredOk = true;
                if (continuation)
                {
                    var (original, restored, _) = MissionScenario.VerifyContinuation(options, catalog, fingerprint, terrain, map, Math.Min(ticks, 900));
                    restoredOk = original.Chain == restored.Chain && original.FinalHash == restored.FinalHash;
                }
                string win = "-", lose = "-";
                if (probes)
                {
                    var victory = MissionProbe.Run(options, catalog, fingerprint, terrain, map, true);
                    var defeat = MissionProbe.Run(options, catalog, fingerprint, terrain, map, false);
                    victoryProbes++;
                    defeatProbes++;
                    if (victory.Reached) victoryReached++;
                    if (defeat.Reached) defeatReached++;
                    win = victory.Reached ? $"win@{victory.TicksToOutcome}" : victory.DecidedBeforeProbe ? $"win:early-{victory.Outcome}[{victory.Report.OutcomeScript}]" : victory.EndingScripts.IsEmpty ? "win:no-script" : "win:UNMET";
                    lose = defeat.Reached ? $"lose@{defeat.TicksToOutcome}" : defeat.DecidedBeforeProbe ? $"lose:early-{defeat.Outcome}" : defeat.EndingScripts.IsEmpty ? "lose:no-script" : "lose:UNMET";
                    if (explain)
                    {
                        foreach (var line in victory.Unmet) Console.WriteLine($"      victory unmet: {line}");
                        foreach (var line in defeat.Unmet) Console.WriteLine($"      defeat unmet: {line}");
                    }
                }
                run++;
                if (same) deterministic++;
                if (restoredOk) continued++;
                executedActions += first.ExecutedActions;
                forwardedActions += first.ForwardedActions;
                executedScripts += first.ExecutedScripts;
                missingActionTotal += first.MissingActionTotal;
                missingConditionTotal += first.MissingConditionTotal;
                foreach (var item in first.MissingActions) { missing[item.Name] = missing.GetValueOrDefault(item.Name) + item.Count; missingMissions[item.Name] = missingMissions.GetValueOrDefault(item.Name) + 1; }
                foreach (var item in first.MissingConditions) { missingConditions[item.Name] = missingConditions.GetValueOrDefault(item.Name) + item.Count; missingConditionMissions[item.Name] = missingConditionMissions.GetValueOrDefault(item.Name) + 1; }
                foreach (var item in first.Diagnostics) diagnostics[item.Name] = diagnostics.GetValueOrDefault(item.Name) + item.Count;
                foreach (var note in first.Notes) Console.WriteLine($"      note: {note}");
                foreach (var item in first.Skipped) skippedTemplates[item.Template] = skippedTemplates.GetValueOrDefault(item.Template) + item.Count;
                Console.WriteLine($"{campaign.Name,-12} {mission.Name,-9} {Path.GetFileNameWithoutExtension(mission.MapPath),-24} objs {first.PlacedObjects - first.SkippedObjects,5}/{first.PlacedObjects,-5} scripts {first.ExecutedScripts,4} acts {first.ExecutedActions,5} fwd {first.ForwardedActions,4} miss {first.MissingActionTotal,4}/{first.MissingConditionTotal,-8} {(same ? "det" : "DIVERGES")} {(restoredOk ? "sav" : "SAVE-DIVERGES")} {win} {lose} [{watch.Elapsed.TotalSeconds:F0}s]");
            }
            catch (Exception exception)
            {
                failed++;
                Console.WriteLine($"{campaign.Name,-12} {mission.Name,-9} {mission.MapPath}: FAILED {exception.GetType().Name}: {exception.Message}");
            }
        }
    }
    Console.WriteLine();
    Console.WriteLine($"{run} missions run ({failed} failed to load or run), {deterministic} identical on a second run, {continued} identical after save/restore; {watch.Elapsed.TotalSeconds:F0} s.");
    if (probes) Console.WriteLine($"Scripted endings reached by elimination: victory {victoryReached}/{victoryProbes}, defeat {defeatReached}/{defeatProbes} (an ending the mission reached by itself before the probe does not count).");
    var actionTotal = executedActions + forwardedActions + missingActionTotal;
    Console.WriteLine($"Scripts executed {executedScripts}; actions executed {executedActions}, forwarded {forwardedActions}, missing hits {missingActionTotal} ({(actionTotal == 0 ? 0 : 100.0 * missingActionTotal / actionTotal):F1} % of action executions); missing condition hits {missingConditionTotal}.");
    Console.WriteLine("Missing actions (hits / missions):");
    foreach (var (name, count) in missing.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(30)) Console.WriteLine($"  {name,-48} {count,8} / {missingMissions[name]}");
    Console.WriteLine("Missing conditions (hits / missions):");
    foreach (var (name, count) in missingConditions.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(20)) Console.WriteLine($"  {name,-48} {count,8} / {missingConditionMissions[name]}");
    Console.WriteLine("Unresolved references:");
    foreach (var (name, count) in diagnostics.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(25)) Console.WriteLine($"  {name,-60} {count,8}");
    Console.WriteLine("Placed objects not instantiable or unknown (templates):");
    foreach (var (name, count) in skippedTemplates.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(25)) Console.WriteLine($"  {name,-40} {count,8}");
    return failed == 0 && run == deterministic && run == continued ? 0 : 1;
}

static int Playthrough(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    var name = Arg(args, "--campaign") ?? throw new ArgumentException("--campaign <name> is required.");
    var ticks = long.Parse(Arg(args, "--ticks") ?? "900", CultureInfo.InvariantCulture);
    var humanSideName = Arg(args, "--human-side") ?? string.Empty;
    var campaign = campaigns.Find(name) ?? throw new ArgumentException($"Unknown campaign {name}.");
    var (context, _, _) = Compile(content);
    var controller = new CampaignController(context, campaign, CampaignProgress.Begin(campaign, ScriptDifficulty.Normal));
    var ok = true;
    while (!controller.IsFinished)
    {
        var mission = controller.CurrentMission;
        var run = controller.StartCurrentMission(humanSideName: humanSideName);
        var session = run.Session;
        while (session.World.Tick < 150 && !session.IsOver) session.Step();
        var save = SaveGame.Write(run, controller.Progress, $"{campaign.Name} {mission.Name}");
        var loaded = SaveGame.Load(save, context, campaigns);
        var reloadOk = loaded.Run.Session.StateHash() == session.StateHash() && loaded.Progress is not null && loaded.Progress.CurrentMission == mission.Name;
        var local = MissionSession.LocalPlayerOf(session.Program, session.Options.HumanSideName);
        foreach (var entity in session.World.Entities.Where(entity => !entity.Owner.IsSystem && session.World.AreEnemies(local, entity.Owner)).OrderBy(entity => entity.Id.Value).ToArray()) session.World.Kill(entity.Id);
        while (session.World.Tick < ticks && !session.IsOver) session.Step();
        if (session.Outcome == MissionOutcome.None)
        {
            Console.WriteLine($"  {mission.Name}: no outcome after {session.World.Tick} ticks (save {save.Length} bytes, reload {(reloadOk ? "ok" : "BAD")}); the ending is not reachable by elimination.");
            ok = false;
            break;
        }
        var conclusion = controller.Conclude(run);
        Console.WriteLine($"  {mission.Name}: {conclusion} at tick {session.Engine.OutcomeTick} (save {save.Length} bytes, reload {(reloadOk ? "ok" : "BAD")}); next {(controller.IsFinished ? "campaign finished" : controller.Progress.CurrentMission)}");
        if (conclusion == MissionConclusion.Defeat || !reloadOk) { ok = false; break; }
    }
    Console.WriteLine(ok ? $"Campaign {campaign.Name} played through: {controller.Progress.Completed.Count} missions." : $"Campaign {campaign.Name} did not finish.");
    return ok ? 0 : 1;
}
