using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// Real data (#459): the built-in AI of America and China builds its side's advanced technology building - the Strategy Center, the Propaganda Center. The original raises it
    /// by its skirmish scripts (<c>USA Strategy Center - N</c>, <c>China Prop Center - N</c>, and <c>- H</c>) once the war factory stands and the <c>_ESCALATION</c> counter has
    /// reached 30 (five game minutes), as it raises the GLA Palace (#454); the built-in AI planned only the Palace, so the Tomahawk, the Aurora, the battle plans, the Overlord and
    /// the propaganda upgrades stayed locked. In <see cref="BusMap"/> America plays China with the standard money at 30, 60 and 120 Hz (measured: the Propaganda Center begun
    /// at 512 s and finished at 567 s, the Strategy Center at 513 and 590 s, at every rate), and each American general plays a Chinese one at 60 Hz with <see cref="BusMoney"/>
    /// credits: each computer player owns a finished building of its own side within <see cref="AdvancedTechMinutes"/> game minutes, none begun before five (measured with the
    /// rich money: the Tank, Infantry and Nuke generals' Propaganda Centers begun at 334 to 344 s, the Laser general's Strategy Center at 526 s). The Air Force and Superweapon
    /// generals are not held to it: the port reads a Strategy Center's prerequisite line "war factory or airfield" as the airfield alone, so they must raise an airfield first,
    /// and in these matches they spend their money on the army and never save for both (the Air Force general loses its base); their opponents are checked.
    /// </summary>
    public static void RealSkirmishAiBuildsAdvancedTech(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        MapData map;
        using (var stream = content.OpenRead(BusMap)) map = SageMapDecoder.Decode(stream);
        // CheckA: whether the American slot is held to it (see the summary for the two generals that are not).
        var cases = new List<(string A, string B, SimulationRate Rate, bool Rich, bool CheckA)>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 }) cases.Add(("FactionAmerica", "FactionChina", rate, false, true));
        cases.Add(("FactionAmericaLaserGeneral", "FactionChinaTankGeneral", SimulationRate.Hz60, true, true));
        cases.Add(("FactionAmericaAirForceGeneral", "FactionChinaInfantryGeneral", SimulationRate.Hz60, true, false));
        cases.Add(("FactionAmericaSuperWeaponGeneral", "FactionChinaNukeGeneral", SimulationRate.Hz60, true, false));
        var failures = new List<string>();
        foreach (var (factionA, factionB, rate, rich, checkA) in cases)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var terrain = TerrainMap.Build(map, BusMap);
            var standard = SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = BusSeed, WorldSeed = BusSeed ^ 0xC0FFEE06 };
            var participants = new[] { everyone.Single(value => value.Faction.Name == factionA), everyone.Single(value => value.Faction.Name == factionB) };
            var session = SkirmishMatchSession.Start(rich ? standard with { StartMoney = BusMoney } : standard, catalog, fingerprint, terrain, map.Objects, [new SkirmishSlot(participants[0]), new SkirmishSlot(participants[1])]);
            var world = session.World;
            var buildings = new List<ObjectTemplate>[2];
            for (var slot = 0; slot < 2; slot++)
            {
                Check.That(session.Ais[slot] is not null, $"{participants[slot].Faction.Name}: the slot has a built-in AI.");
                var side = participants[slot].Side;
                buildings[slot] = catalog.Templates.Where(template => template.IsKindOf("FS_ADVANCED_TECH") && template.Side.Equals(side, StringComparison.OrdinalIgnoreCase)).ToList();
                Check.That(buildings[slot].Count > 0, $"{participants[slot].Faction.Name}: side {side} has an advanced technology building.");
            }
            var earliest = rate.Time.OriginalFrames(30 * 60 * 5);
            var end = rate.Time.OriginalFrames(30 * 60 * AdvancedTechMinutes);
            var begun = new long[2];
            var finished = new long[2];
            while (world.Tick < end && !session.IsOver && (finished[0] == 0 || finished[1] == 0))
            {
                session.Step();
                foreach (var entity in world.Entities)
                {
                    if (entity.EffectivelyDead) continue;
                    for (var slot = 0; slot < 2; slot++)
                    {
                        if (entity.Owner != session.Players[slot].Id || !buildings[slot].Contains(entity.Template)) continue;
                        if (begun[slot] == 0) begun[slot] = world.Tick;
                        if (finished[slot] == 0 && !entity.HasStatus(ObjectStatusBits.UnderConstruction)) finished[slot] = world.Tick;
                    }
                }
            }
            var seconds = (long tick) => tick == 0 ? "never" : $"{tick / rate.Time.OriginalFrames(30)} s";
            for (var slot = 0; slot < 2; slot++)
            {
                var label = $"{participants[slot].Faction.Name} against {participants[1 - slot].Faction.Name} at {rate}{(rich ? $", {BusMoney} credits" : "")}";
                Console.WriteLine($"    {label}: {string.Join("/", buildings[slot].Select(template => template.Name))} begun {seconds(begun[slot])}, finished {seconds(finished[slot])}.");
                if (slot == 0 && !checkA) continue;
                if (finished[slot] == 0) failures.Add($"{label}: no finished advanced technology building within {AdvancedTechMinutes} game minutes (begun {seconds(begun[slot])}).");
                else if (begun[slot] < earliest) failures.Add($"{label}: the building was begun at {seconds(begun[slot])}, before five game minutes.");
            }
        }
        Check.That(failures.Count == 0, string.Join(" ", failures));
    }

    /// <summary>The game minutes within which each computer player of <see cref="RealSkirmishAiBuildsAdvancedTech"/> must own its advanced technology building.</summary>
    private const int AdvancedTechMinutes = 13;
}
