using System.Diagnostics;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.World;
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
      reachability <content-dir> [--campaign NAME] [--difficulty easy|normal|hard]
                                                static: can the mission's own VICTORY/DEFEAT scripts fire with the implemented condition/action types? (clean / degraded / blocked chains)
      script <content-dir> --map TEXT --name SCRIPT
                                                prints one script (conditions, actions) and every script whose actions name it
      coverage                                  every script action and condition type the VM does not execute (missing), by name
      usage <content-dir> --action A[,B] [--condition]
                                                every use of the named script action (or, with --condition, condition) types in the campaign maps: map, script, parameters
      teams <content-dir> --map TEXT [--filter TEXT]
      drive <content-dir> [--campaign NAME] [--map TEXT] [--ticks N] [--explain] [--watch TEMPLATE-TEXT] [--commands-only] [--outcome victory|defeat]
                                                objective driver: satisfies the world-state conditions of the scripts (kill named objects/teams, send units into areas, ...) one by one and
                                                reports whether the mission's OWN VICTORY/DEFEAT action fired (no generic elimination)
                                                --commands-only: no direct world writes (no kills, placements, money, spawns): the army is sent with orders and units are queued at producers
      template <content-dir> --name A[,B]          is the template instantiable, and which modules are missing
      transports <content-dir>                     the reinforcement transports the campaign maps name, and whether each can be created
      objects <content-dir> --map TEXT [--name N]  the named objects a map places: template, team, position, whether the template can be created
      sides <content-dir> [--campaign NAME]       the script players (sides) of every campaign map: name, faction, human flag (to map a chosen general to a map's side)
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
    if (content.Exists("Data/INI/ChallengeMode.ini"))
    {
        using var personaStream = content.OpenRead("Data/INI/ChallengeMode.ini");
        using var personaReader = new StreamReader(personaStream);
        campaigns = campaigns.WithPersonas(personaReader.ReadToEnd());
    }
    return args[0] switch
    {
        "list" => List(content, campaigns),
        "audit" => Audit(content, campaigns, args),
        "sides" => Sides(content, campaigns, args),
        "reachability" => ReachabilityReport(content, campaigns, args),
        "script" => ScriptDump(content, campaigns, args),
        "usage" => ActionUsage(content, campaigns, args),
        "coverage" => Coverage(),
        "drive" => Drive(content, campaigns, args),
        "template" => TemplateInfo(content, args),
        "objects" => ObjectInfo(content, campaigns, args),
        "transports" => TransportInfo(content, campaigns, args),
        "teams" => TeamInfo(content, campaigns, args),
        "playthrough" => Playthrough(content, campaigns, args),
        _ => Fail(Usage),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(args.Contains("--trace") ? $"error: {exception}" : $"error: {exception.Message}");
    return 1;
}

static int Sides(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    var (context, _, _) = Compile(content);
    var filterCampaign = Arg(args, "--campaign");
    foreach (var campaign in campaigns.Campaigns)
    {
        if (filterCampaign is not null && !campaign.Name.Equals(filterCampaign, StringComparison.OrdinalIgnoreCase)) continue;
        Console.WriteLine($"{campaign.Name}{(campaign.PlayerFaction.Length > 0 ? $" (player faction {campaign.PlayerFaction})" : "")}");
        foreach (var mission in campaign.Sequence)
        {
            var program = ScriptProgram.Build(context.LoadMap(mission.MapPath));
            Console.WriteLine($"  {mission.Name,-10} {mission.MapPath}: " + string.Join(", ", program.Players.Select(player => $"{(player.Name.Length == 0 ? "<neutral>" : player.Name)}[{player.Faction}{(player.IsHuman ? ",human" : "")}]")));
        }
    }
    return 0;
}

static int ReachabilityReport(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    var (context, _, _) = Compile(content);
    var filterCampaign = Arg(args, "--campaign");
    var difficulty = (Arg(args, "--difficulty") ?? "normal").ToLowerInvariant() switch { "easy" => ScriptDifficulty.Easy, "hard" => ScriptDifficulty.Hard, _ => ScriptDifficulty.Normal };
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var tally = new Dictionary<string, int>();
    var missingActionScripts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var missingActionMaps = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var maps = 0;
    foreach (var campaign in campaigns.Campaigns)
    {
        if (filterCampaign is not null && !campaign.Name.Equals(filterCampaign, StringComparison.OrdinalIgnoreCase)) continue;
        foreach (var mission in campaign.Sequence)
        {
            if (!seen.Add(mission.MapPath)) continue;
            MapData map;
            try { map = context.LoadMap(mission.MapPath); }
            catch (FileNotFoundException) { Console.WriteLine($"{campaign.Name,-12} {mission.Name,-9} {mission.MapPath}: map not in the content"); continue; }
            maps++;
            var scriptProgram = ScriptProgram.Build(map);
            foreach (var (name, scripts) in Reachability.MissingActionUsage(scriptProgram))
            {
                missingActionScripts[name] = missingActionScripts.GetValueOrDefault(name) + scripts;
                missingActionMaps[name] = missingActionMaps.GetValueOrDefault(name) + 1;
            }
            var analysis = Reachability.Analyze(scriptProgram, difficulty);
            var line = new List<string>();
            foreach (var ending in new[] { "victory", "defeat" })
            {
                var scripts = analysis.Endings.Where(item => item.Ending == ending).ToArray();
                var clean = scripts.Count(item => item.Verdict.Kind == Reachability.Kind.Clean);
                var degraded = scripts.Count(item => item.Verdict.Kind == Reachability.Kind.Degraded);
                var blocked = scripts.Length - clean - degraded;
                var best = clean > 0 ? "CLEAN" : degraded > 0 ? "DEGRADED" : scripts.Length > 0 ? "BLOCKED" : "NONE";
                tally[ending + ":" + best] = tally.GetValueOrDefault(ending + ":" + best) + 1;
                var why = best is "BLOCKED" or "DEGRADED"
                    ? " (" + string.Join("; ", scripts.Where(item => item.Verdict.Kind != Reachability.Kind.Clean).SelectMany(item => item.Verdict.Blockers).Distinct().Take(4)) + ")"
                    : string.Empty;
                line.Add($"{ending} {best} [{clean}/{degraded}/{blocked} of {scripts.Length}]{why}");
            }
            Console.WriteLine($"{campaign.Name,-12} {mission.Name,-9} {Path.GetFileNameWithoutExtension(mission.MapPath),-22} " + string.Join("  |  ", line));
        }
    }
    Console.WriteLine("Unimplemented action types used by the scripts (scripts using it / maps):");
    foreach (var (name, scripts) in missingActionScripts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(40))
        Console.WriteLine($"  {name,-52} {scripts,5} / {missingActionMaps[name]}");
    Console.WriteLine($"{maps} maps analysed ({difficulty}). victory: clean {tally.GetValueOrDefault("victory:CLEAN")}, degraded {tally.GetValueOrDefault("victory:DEGRADED")}, blocked {tally.GetValueOrDefault("victory:BLOCKED")}, no victory script {tally.GetValueOrDefault("victory:NONE")}; " +
                      $"defeat: clean {tally.GetValueOrDefault("defeat:CLEAN")}, degraded {tally.GetValueOrDefault("defeat:DEGRADED")}, blocked {tally.GetValueOrDefault("defeat:BLOCKED")}, no defeat script {tally.GetValueOrDefault("defeat:NONE")}.");
    return 0;
}

static int ScriptDump(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    var (context, _, _) = Compile(content);
    var mapFilter = Arg(args, "--map") ?? string.Empty;
    var name = Arg(args, "--name") ?? string.Empty;
    var mission = campaigns.Campaigns.SelectMany(campaign => campaign.Sequence).FirstOrDefault(item => item.MapPath.Contains(mapFilter, StringComparison.OrdinalIgnoreCase));
    if (mission is null) return Fail("no such map");
    var program = ScriptProgram.Build(context.LoadMap(mission.MapPath));
    static string Text(MapScriptParameter value) => value.Text.Length > 0 ? value.Text : value.Type is MapScriptParameterType.Real or MapScriptParameterType.Angle ? value.Real.ToString(CultureInfo.InvariantCulture) : value.Integer.ToString(CultureInfo.InvariantCulture);
    static string Describe(MapScriptAction action) => $"{action.TypeName}({string.Join(", ", action.Parameters.Select(Text))})";
    foreach (var unit in program.Units.Where(item => item.Name == name))
    {
        var group = unit.GroupIndex >= 0 ? program.Groups[unit.GroupIndex] : null;
        Console.WriteLine($"script '{unit.Name}' list {unit.ListIndex} group {group?.Name ?? "-"} (group active {group?.Group.IsActive}, subroutine {group?.Group.IsSubroutine}) active {unit.Script.IsActive} subroutine {unit.IsSubroutine}");
        foreach (var or in unit.Script.OrConditions)
            Console.WriteLine("  OR: " + string.Join(" AND ", or.AndConditions.Select(condition => $"{condition.TypeName}({string.Join(", ", condition.Parameters.Select(Text))})")));
        foreach (var action in unit.Script.Actions) Console.WriteLine("  do " + Describe(action));
        foreach (var action in unit.Script.FalseActions) Console.WriteLine("  else " + Describe(action));
    }
    foreach (var unit in program.Units)
        foreach (var action in unit.Script.Actions.Concat(unit.Script.FalseActions).Where(item => item.Parameters.Any(value => value.Text == name)))
            Console.WriteLine($"  named by '{unit.Name}': {Describe(action)}");
    foreach (var group in program.Groups.Where(item => item.Name == name)) Console.WriteLine($"group '{group.Name}' active {group.Group.IsActive} subroutine {group.Group.IsSubroutine}, {group.ScriptIndices.Length} scripts");
    return 0;
}

static int Drive(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    var ticks = long.Parse(Arg(args, "--ticks") ?? "60000", CultureInfo.InvariantCulture);
    var filterCampaign = Arg(args, "--campaign");
    var filterMap = Arg(args, "--map");
    var watch = Arg(args, "--watch");
    var watchFlags = (Arg(args, "--watch-flag") ?? string.Empty).Split(",", StringSplitOptions.RemoveEmptyEntries);
    var explain = args.Contains("--explain");
    var commandsOnly = args.Contains("--commands-only");
    var outcomeOnly = Arg(args, "--outcome");
    var agentThreshold = int.Parse(Arg(args, "--agent-threshold") ?? "0", CultureInfo.InvariantCulture);
    var timeline = int.Parse(Arg(args, "--timeline") ?? "0", CultureInfo.InvariantCulture);
    var (context, catalog, fingerprint) = Compile(content);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    int maps = 0, victories = 0, defeats = 0;
    foreach (var campaign in campaigns.Campaigns)
    {
        if (filterCampaign is not null && !campaign.Name.Equals(filterCampaign, StringComparison.OrdinalIgnoreCase)) continue;
        foreach (var mission in campaign.Sequence)
        {
            if (filterMap is not null && !mission.MapPath.Contains(filterMap, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(mission.MapPath)) continue;
            MapData map;
            try { map = context.LoadMap(mission.MapPath); }
            catch (FileNotFoundException) { Console.WriteLine($"{campaign.Name,-12} {mission.Name,-9} {mission.MapPath}: map not in the content"); continue; }
            maps++;
            var terrain = TerrainMap.Build(map, mission.MapPath);
            var options = MissionOptions.Standard with { HumanSideName = campaign.IsChallenge ? ChallengeSides.HumanSideFor(map) : string.Empty, HumanFaction = campaign.IsChallenge ? campaign.PlayerFaction : string.Empty };
            var cells = new List<string>();
            foreach (var victory in new[] { true, false }.Where(value => outcomeOnly is null || outcomeOnly.Equals(value ? "victory" : "defeat", StringComparison.OrdinalIgnoreCase)))
            {
                ObjectiveDriveResult result;
                try { result = MissionObjectiveDriver.Run(options, catalog, fingerprint, terrain, map, victory, ticks, observe: watch is null && watchFlags.Length == 0 && timeline == 0 ? null : CombineWatches(CombineWatches(watch is null ? null : WatchEntities(watch, victory), watchFlags.Length == 0 ? null : WatchFlags(watchFlags, victory)), timeline == 0 ? null : Timeline(timeline)), commandsOnly: commandsOnly, agentThreshold: agentThreshold); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { cells.Add($"{(victory ? "victory" : "defeat")} FAILED {exception.GetType().Name}: {exception.Message}"); continue; }
                if (result.ReachedByScript) { if (victory) victories++; else defeats++; }
                cells.Add($"{(victory ? "victory" : "defeat")} {(result.ReachedByScript ? $"REACHED by '{result.OutcomeScript}' @{result.OutcomeTick}" : "not reached")} ({result.Interventions} interventions: {result.Commands} commands, {result.WorldWrites} world writes)");
                if (explain)
                {
                    foreach (var line in result.Log.Take(40)) Console.WriteLine($"      {(victory ? "V" : "D")} {line}");
                    foreach (var line in result.Unmet) Console.WriteLine($"      {(victory ? "V" : "D")} unmet: {line}");
                }
            }
            Console.WriteLine($"{campaign.Name,-12} {mission.Name,-9} {Path.GetFileNameWithoutExtension(mission.MapPath),-22} {string.Join("  |  ", cells)}");
        }
    }
    Console.WriteLine($"{maps} maps driven ({ticks} ticks): victory reached by the mission's own script on {victories}, defeat on {defeats}.");
    return 0;
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
    // The same world the client simulates (GameData.Load): the airfields' parking and runway points come from the draw models' bones and are part of the fingerprint.
    var geometry = CnCNew.Game.Data.ModelGeometryLoader.Load(graph, CnCNew.Presentation.Objects.ObjectVisualCatalog.Build(graph), content);
    var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, geometry);
    var fingerprint = geometry.Combine(DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256);
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
                var options = MissionOptions.Standard with { HumanSideName = humanSideName.Length > 0 || !campaign.IsChallenge ? humanSideName : ChallengeSides.HumanSideFor(map), HumanFaction = campaign.IsChallenge ? campaign.PlayerFaction : string.Empty };
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

static int TemplateInfo(ContentFileSystem content, string[] args)
{
    var (_, catalog, _) = Compile(content);
    foreach (var name in (Arg(args, "--name") ?? throw new ArgumentException("--name <template[,template]> is required.")).Split(','))
    {
        if (!catalog.TryGet(name, out var template)) { Console.WriteLine($"{name}: no such template"); continue; }
        Console.WriteLine($"{name}: instantiable {template.IsInstantiable}; missing modules: {string.Join(", ", template.MissingModules)}");
    }
    return 0;
}

static int ObjectInfo(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    var (context, catalog, _) = Compile(content);
    var mapFilter = Arg(args, "--map") ?? string.Empty;
    var name = Arg(args, "--name");
    var mission = campaigns.Campaigns.SelectMany(campaign => campaign.Sequence).FirstOrDefault(item => item.MapPath.Contains(mapFilter, StringComparison.OrdinalIgnoreCase));
    if (mission is null) return Fail("no such map");
    var program = ScriptProgram.Build(context.LoadMap(mission.MapPath));
    foreach (var placed in program.Objects.Where(item => name is null ? item.ScriptName.Length > 0 : item.ScriptName == name))
    {
        var known = catalog.TryGet(placed.Template, out var template);
        Console.WriteLine($"{placed.ScriptName,-32} {placed.Template,-34} team '{placed.TeamName}' at ({placed.X:F0},{placed.Y:F0}) {(known ? (template!.IsInstantiable ? "instantiable" : "NOT instantiable: " + string.Join(", ", template.MissingModules)) : "unknown template")}");
    }
    return 0;
}

static int TransportInfo(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    var (context, catalog, _) = Compile(content);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var usedBy = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
    foreach (var mission in campaigns.Campaigns.SelectMany(campaign => campaign.Sequence))
    {
        if (!seen.Add(mission.MapPath)) continue;
        MapData map;
        try { map = context.LoadMap(mission.MapPath); }
        catch (FileNotFoundException) { continue; }
        var program = ScriptProgram.Build(map);
        foreach (var unit in program.Units)
            foreach (var action in unit.Script.Actions.Concat(unit.Script.FalseActions).Where(action => action.TypeName == "CREATE_REINFORCEMENT_TEAM"))
            {
                var called = program.FindTeam(action.Parameters.Length > 0 ? action.Parameters[0].Text : string.Empty);
                if (called is null) continue;
                Console.WriteLine($"  call {Path.GetFileNameWithoutExtension(mission.MapPath)} '{unit.Name}': team '{called.Name}' transport '{called.Transport}' full={called.StartsFull} exit={called.TransportsExit} origin='{called.ReinforcementOrigin}' units={string.Join(",", called.Units.Select(u => $"{u.Type}x{u.MaxCount}"))}");
            }
        foreach (var team in program.Teams.Where(team => team.Transport.Length > 0))
        {
            if (!usedBy.TryGetValue(team.Transport, out var maps)) usedBy[team.Transport] = maps = new SortedSet<string>(StringComparer.Ordinal);
            maps.Add(Path.GetFileNameWithoutExtension(mission.MapPath));
        }
    }
    foreach (var (transport, maps) in usedBy)
    {
        var known = catalog.TryGet(transport, out var template);
        Console.WriteLine($"{transport,-40} {(known ? (template!.IsInstantiable ? "instantiable" : "NOT instantiable: " + string.Join(", ", template.MissingModules)) : "unknown template"),-60} {maps.Count} map(s): {string.Join(", ", maps)}");
    }
    return 0;
}

/// <summary>Prints a line whenever one of the named script flags or scripts changes state (drive --watch-flag NAME[,NAME]; a name that is an active script is reported as script state).</summary>
static Action<MissionSession> WatchFlags(string[] names, bool victory)
{
    var known = new Dictionary<string, string>();
    return session =>
    {
        foreach (var name in names)
        {
            var now = name.StartsWith("area:", StringComparison.Ordinal) && session.Program.FindArea(name[5..]) is { } area
                ? $"entities inside: {string.Join(", ", session.World.Entities.Where(entity => !entity.EffectivelyDead && area.Contains(entity.X, entity.Y) && entity.Template.Locomotor is not null).GroupBy(entity => (entity.Template.Name, entity.Owner.Value)).Select(group => $"{group.Key.Name}/owner{group.Key.Value} x{group.Count()}").Take(8))}"
                : $"flag={session.Engine.FlagValue(name)} scriptActive={session.Engine.IsScriptActive(name)}";
            if (known.TryGetValue(name, out var before) && before == now) continue;
            known[name] = now;
            Console.WriteLine($"      {(victory ? "V" : "D")} @{session.World.Tick} {name}: {now}");
        }
    };
}

static Action<MissionSession>? CombineWatches(Action<MissionSession>? first, Action<MissionSession>? second) => first is null ? second : second is null ? first : session => { first(session); second(session); };

/// <summary>Every <paramref name="every"/> ticks one line per player: armed mobile units, structures, others (drive --timeline).</summary>
static Action<MissionSession> Timeline(int every) => session =>
{
    var world = session.World;
    if (world.Tick % every != 0) return;
    var rows = world.Entities.Where(entity => !entity.EffectivelyDead && !entity.Owner.IsSystem && entity.Body is not null)
        .GroupBy(entity => entity.Owner.Value).OrderBy(group => group.Key)
        .Select(group =>
        {
            var armed = group.Where(entity => entity.Template.Locomotor is not null && !entity.Template.IsKindOf("STRUCTURE") && CnCNew.Simulation.World.SimulationWorld.ActiveWeaponSet(entity) is { WeaponCount: > 0 }).ToList();
            var centre = armed.Count == 0 ? string.Empty : $" c({armed.Average(entity => (double)entity.X.Floor()):F0},{armed.Average(entity => (double)entity.Y.Floor()):F0}) mv{armed.Count(entity => entity.HasMoveTarget)}";
            return $"p{group.Key}: {armed.Count} armed{centre}/{group.Count(entity => entity.Template.IsKindOf("STRUCTURE"))} str/{group.Count()} all ${world.MoneyOf(new CnCNew.Simulation.Ids.PlayerId(group.Key))}";
        });
    Console.WriteLine($"      T @{world.Tick}: {string.Join("  ", rows)}");
};

/// <summary>Prints a line whenever one of the objects whose template name contains <paramref name="text"/> appears, changes owner, loses health class or disappears (drive --watch).</summary>
static Action<MissionSession> WatchEntities(string text, bool victory)
{
    var known = new Dictionary<uint, string>();
    return session =>
    {
        var world = session.World;
        var now = new Dictionary<uint, string>();
        foreach (var entity in world.Entities.Where(entity => entity.Template.Name.Contains(text, StringComparison.OrdinalIgnoreCase)))
            now[entity.Id.Value] = $"{entity.Template.Name} #{entity.Id.Value} owner={entity.Owner.Value} health={entity.Body?.Health.ToString() ?? "-"} dead={entity.EffectivelyDead} at=({entity.X.Floor()},{entity.Y.Floor()})";
        foreach (var (id, line) in now.Where(pair => !known.TryGetValue(pair.Key, out var before) || before != pair.Value))
        {
            Console.WriteLine($"      {(victory ? "V" : "D")} @{world.Tick} watch {line}");
            // who stands near a hurt object: the three closest objects of other sides (a hint at the attacker, not a proof)
            if (known.ContainsKey(id) && world.FindEntity(new CnCNew.Simulation.Ids.EntityId(id)) is { } hurt && hurt.Body is not null && hurt.Body.Health < hurt.Body.MaxHealth && world.Tick % 150 < 2)
            {
                var near = world.Entities.Where(other => other.Owner != hurt.Owner && !other.EffectivelyDead && other.Template.Locomotor is not null)
                    .Select(other => (other, d: (long)((other.X - hurt.X).Floor() * (other.X - hurt.X).Floor() + (other.Y - hurt.Y).Floor() * (other.Y - hurt.Y).Floor())))
                    .OrderBy(pair => pair.d).ThenBy(pair => pair.other.Id.Value).Take(3);
                Console.WriteLine($"          nearest foes: {string.Join(", ", near.Select(pair => $"{pair.other.Template.Name}#{pair.other.Id.Value} owner {pair.other.Owner.Value} d={Math.Sqrt(pair.d):F0}"))}");
            }
        }
        foreach (var id in known.Keys.Where(id => !now.ContainsKey(id))) Console.WriteLine($"      {(victory ? "V" : "D")} @{world.Tick} watch #{id} is gone");
        known = now;
    };
}

static int TeamInfo(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    var (context, catalog, _) = Compile(content);
    var mapFilter = Arg(args, "--map") ?? string.Empty;
    var filter = Arg(args, "--filter") ?? string.Empty;
    var mission = campaigns.Campaigns.SelectMany(campaign => campaign.Sequence).FirstOrDefault(item => item.MapPath.Contains(mapFilter, StringComparison.OrdinalIgnoreCase));
    if (mission is null) return Fail("no such map");
    var program = ScriptProgram.Build(context.LoadMap(mission.MapPath));
    foreach (var team in program.Teams.Where(team => team.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
    {
        var units = string.Join(",", team.Units.Select(unit => $"{unit.Type} {unit.MinCount}..{unit.MaxCount}{(catalog.TryGet(unit.Type, out var template) && !template.IsInstantiable ? "(NOT instantiable)" : string.Empty)}"));
        Console.WriteLine($"{team.Name,-28} owner {team.Owner,-12} {(team.IsSingleton ? "singleton" : $"max {team.MaxInstances}")} units [{units}] transport '{team.Transport}' origin '{team.ReinforcementOrigin}' onUnitDestroyed '{team.OnUnitDestroyedScript}' onCreate '{team.OnCreateScript}'");
    }
    return 0;
}

static int ActionUsage(ContentFileSystem content, CampaignCatalog campaigns, string[] args)
{
    HashSet<string> wanted = (Arg(args, "--action") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
    var missing = args.Contains("--missing");
    if (wanted.Count == 0 && !missing) return Fail("--action is required (or --missing)");
    var conditions = args.Contains("--condition");
    if (missing) wanted = (conditions ? MapScriptCatalog.ConditionTypeNames.Where(name => ScriptCoverage.ConditionSupport(name) == ScriptSupport.Missing) : MapScriptCatalog.ScriptActionTypeNames.Where(name => ScriptCoverage.ActionSupport(name) == ScriptSupport.Missing)).ToHashSet(StringComparer.Ordinal);
    var (context, _, _) = Compile(content);
    static string Text(MapScriptParameter value) => value.Text.Length > 0 ? value.Text : value.Type is MapScriptParameterType.Real or MapScriptParameterType.Angle ? value.Real.ToString(CultureInfo.InvariantCulture) : value.Integer.ToString(CultureInfo.InvariantCulture);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var mission in campaigns.Campaigns.SelectMany(campaign => campaign.Sequence))
    {
        if (!seen.Add(mission.MapPath)) continue;
        MapData map;
        try { map = context.LoadMap(mission.MapPath); }
        catch (FileNotFoundException) { continue; }
        var program = ScriptProgram.Build(map);
        foreach (var unit in program.Units)
        {
            if (conditions)
            {
                foreach (var or in unit.Script.OrConditions)
                    foreach (var condition in or.AndConditions.Where(item => wanted.Contains(item.TypeName)))
                        Console.WriteLine($"{Path.GetFileNameWithoutExtension(mission.MapPath)}: '{unit.Name}': {condition.TypeName}({string.Join(", ", condition.Parameters.Select(Text))})");
                continue;
            }
            foreach (var action in unit.Script.Actions.Concat(unit.Script.FalseActions).Where(item => wanted.Contains(item.TypeName)))
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(mission.MapPath)}: '{unit.Name}': {action.TypeName}({string.Join(", ", action.Parameters.Select(Text))})");
        }
    }
    return 0;
}

static int Coverage()
{
    Console.WriteLine("Missing actions: " + string.Join(", ", MapScriptCatalog.ScriptActionTypeNames.Where(name => ScriptCoverage.ActionSupport(name) == ScriptSupport.Missing)));
    Console.WriteLine("Missing conditions: " + string.Join(", ", MapScriptCatalog.ConditionTypeNames.Where(name => ScriptCoverage.ConditionSupport(name) == ScriptSupport.Missing)));
    var actions = MapScriptCatalog.ScriptActionTypeNames.GroupBy(ScriptCoverage.ActionSupport).ToDictionary(group => group.Key, group => group.Count());
    Console.WriteLine($"Actions: {MapScriptCatalog.ScriptActionTypeNames.Count()} types: " + string.Join(", ", actions.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value}")));
    Console.WriteLine($"Conditions: {MapScriptCatalog.ConditionTypeNames.Count()} types, executed {MapScriptCatalog.ConditionTypeNames.Count(name => ScriptCoverage.ConditionSupport(name) == ScriptSupport.Executed)}.");
    return 0;
}
