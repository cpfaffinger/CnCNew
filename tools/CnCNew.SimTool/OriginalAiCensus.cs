using System.Globalization;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Skirmish.Original;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// <c>ai-census ... --ai original [--difficulty ..] [--difficulty-b ..]</c>: what a match of the original skirmish AI looks like in numbers, per match and slot, in the line format
/// of the built-in AI's census (pairing, seed, slot, faction, section, key, value). Times are game seconds, so that two tick rates and the two AIs compare. Observation only.
/// <list type="bullet">
/// <item><c>match</c>: winnerSlot, endSeconds, firstBarracks, firstWarFactory, firstAttack (an own combat unit within 400 of an enemy structure), attackWaves (such an arrival after
/// at least 30 s without one), teamsBuilt (teams the AI activated), disbanded (teams whose build time ran out), structuresBuilt, unitsBuilt, unitsBuilt10 (in the first ten minutes),
/// lost, moneyAt10, moneyEnd, powersUsed.</item>
/// <item><c>built</c>: game second of the first completion per structure type. <c>produced10</c>: units trained in the first ten minutes by type. <c>power</c>: uses per special
/// power. <c>money</c>: cash every 50 s.</item>
/// </list>
/// </summary>
internal static class OriginalAiCensus
{
    private const long MoneySampleFrames = 1500;
    private const long TenMinutesFrames = 18_000;
    private const long WaveGapFrames = 900;
    private const long ProbeFrames = 15;
    private const long AttackReach = 400;

    public static (string Outcome, List<string> Lines) Play(SimContext context, string id, string mapPath, string[] factions, uint seed, long maxTicks, string[] args)
    {
        var (map, terrain, library) = OriginalAi.Load(context, mapPath);
        var slots = OriginalAi.Slots(context, factions, args);
        var session = OriginalAi.Start(context, map, terrain, library, slots, seed, maxTicks, libraryForMissingSides: OriginalAi.LibraryForMissingSides(args));
        var world = session.World;
        var time = world.Time;
        var counts = new SortedDictionary<(int Slot, string Section, string Key), long>();
        void Add(int slot, string section, string key, long value) => counts[(slot, section, key)] = counts.GetValueOrDefault((slot, section, key)) + value;
        void First(int slot, string section, string key, long value) { if (!counts.ContainsKey((slot, section, key))) counts[(slot, section, key)] = value; }
        long Seconds(long tick) => tick / time.TicksPerSecond;
        int SlotOf(PlayerId player) => player.Value >= 1 && player.Value <= slots.Length ? player.Value - 1 : -1;
        var known = new Dictionary<uint, (CnCNew.Simulation.Templates.ObjectTemplate Template, int Slot)>();
        var lastNear = new long[slots.Length];
        Array.Fill(lastNear, long.MinValue / 2);
        var tenMinutes = time.OriginalFrames(TenMinutesFrames);
        var moneyEvery = time.OriginalFrames(MoneySampleFrames);
        var probe = time.OriginalFrames(ProbeFrames);
        var waveGap = time.OriginalFrames(WaveGapFrames);
        var activeTeams = new HashSet<int>();

        while (!session.IsOver)
        {
            var result = session.Step();
            var tick = world.Tick;
            foreach (var item in result.Events)
            {
                switch (item.Kind)
                {
                    case SimEventKind.EntitySpawned:
                        if (world.FindEntity(item.Entity) is { } spawned) known[item.Entity.Value] = (spawned.Template, SlotOf(spawned.Owner));
                        break;
                    case SimEventKind.ConstructionCompleted:
                        if (world.FindEntity(item.Entity) is { } built && SlotOf(built.Owner) is >= 0 and var builtSlot)
                        {
                            First(builtSlot, "built", built.Template.Name, Seconds(item.Tick));
                            Add(builtSlot, "match", "structuresBuilt", 1);
                            if (built.Template.IsKindOf("FS_BARRACKS")) First(builtSlot, "match", "firstBarracks", Seconds(item.Tick));
                            if (built.Template.IsKindOf("FS_WARFACTORY")) First(builtSlot, "match", "firstWarFactory", Seconds(item.Tick));
                        }
                        break;
                    case SimEventKind.ProductionCompleted:
                        if (world.FindEntity(item.Entity) is { } producer && SlotOf(producer.Owner) is >= 0 and var producerSlot)
                        {
                            Add(producerSlot, "match", "unitsBuilt", 1);
                            if (item.Tick <= tenMinutes)
                            {
                                Add(producerSlot, "match", "unitsBuilt10", 1);
                                Add(producerSlot, "produced10", context.Catalog[(int)item.Value].Name, 1);
                            }
                        }
                        break;
                    case SimEventKind.SpecialPowerUsed:
                        if (SlotOf(item.Player) is >= 0 and var powerSlot)
                        {
                            Add(powerSlot, "match", "powersUsed", 1);
                            Add(powerSlot, "power", item.Text.Length > 0 ? item.Text : "?", 1);
                        }
                        break;
                    case SimEventKind.EntityDied:
                        if (known.TryGetValue(item.Entity.Value, out var dead) && dead.Slot >= 0 && !dead.Template.IsKindOf("PROJECTILE")) Add(dead.Slot, "match", "lost", 1);
                        break;
                }
            }
            if (tick % probe == 0)
            {
                foreach (var team in session.Engine.TeamInstances)
                    if (team.Created && team.Members.Count > 0 && SlotOf(team.Owner) is >= 0 and var teamSlot && !team.Prototype.StartsWith("team", StringComparison.Ordinal) && activeTeams.Add(team.Id))
                        Add(teamSlot, "match", "teamsBuilt", 1);
                for (var slot = 0; slot < slots.Length; slot++)
                {
                    if (!NearEnemyStructure(world, slot, slots.Length)) continue;
                    First(slot, "match", "firstAttack", Seconds(tick));
                    if (tick - lastNear[slot] >= waveGap) Add(slot, "match", "attackWaves", 1);
                    lastNear[slot] = tick;
                }
            }
            if (tick % moneyEvery == 0)
                for (var slot = 0; slot < slots.Length; slot++)
                    counts[(slot, "money", "s" + Seconds(tick).ToString("D5", CultureInfo.InvariantCulture))] = (long)world.MoneyOf(ScriptedSkirmishSession.PlayerOf(slot)).Floor();
            if (tick == tenMinutes)
                for (var slot = 0; slot < slots.Length; slot++) counts[(slot, "match", "moneyAt10")] = (long)world.MoneyOf(ScriptedSkirmishSession.PlayerOf(slot)).Floor();
        }

        var missing = session.Engine.MissingActionHits.Values.Sum() + session.Engine.MissingConditionHits.Values.Sum();
        if (missing > 0) throw new InvalidOperationException($"{missing.ToString(CultureInfo.InvariantCulture)} executions of script types without implementation.");
        var winner = session.Winner is { } winnerId ? winnerId.Value : 0;
        var lines = new List<string>();
        for (var slot = 0; slot < slots.Length; slot++)
        {
            var player = ScriptedSkirmishSession.PlayerOf(slot);
            counts[(slot, "match", "winnerSlot")] = winner;
            counts[(slot, "match", "endSeconds")] = Seconds(winner == 0 ? world.Tick : session.VictoryTick);
            counts[(slot, "match", "moneyEnd")] = (long)world.MoneyOf(player).Floor();
            counts[(slot, "match", "disbanded")] = session.Engine.Diagnostics.Where(pair => pair.Key.Contains("disbanded: build time expired", StringComparison.Ordinal)
                && session.Engine.Program.FindTeam(pair.Key.Split('\'')[1]) is { } prototype && session.Engine.Program.FindPlayer(prototype.Owner)?.Id == player).Sum(pair => pair.Value);
            foreach (var key in new[] { "firstBarracks", "firstWarFactory", "firstAttack" })
                if (!counts.ContainsKey((slot, "match", key))) counts[(slot, "match", key)] = -1;
            foreach (var key in new[] { "attackWaves", "teamsBuilt", "structuresBuilt", "unitsBuilt", "unitsBuilt10", "lost", "powersUsed", "moneyAt10" })
                if (!counts.ContainsKey((slot, "match", key))) counts[(slot, "match", key)] = 0;
        }
        foreach (var ((slot, section, key), value) in counts)
            lines.Add(string.Join('\t', id, seed.ToString(CultureInfo.InvariantCulture), (slot + 1).ToString(CultureInfo.InvariantCulture), factions[slot] + "/" + OriginalAi.Describe(slots[slot]), section, key, value.ToString(CultureInfo.InvariantCulture)));
        return (winner == 0 ? "stalemate" : "winner " + factions[winner - 1], lines);
    }

    /// <summary>An own combat unit (no structure, dozer or gatherer) within <see cref="AttackReach"/> of a living structure of another slot.</summary>
    private static bool NearEnemyStructure(SimulationWorld world, int slot, int slots)
    {
        var owner = ScriptedSkirmishSession.PlayerOf(slot);
        foreach (var unit in world.EntitiesOwnedBy(owner).AsSpan())
        {
            if (unit.EffectivelyDead || unit.Template.IsKindOf("STRUCTURE") || unit.Template.IsKindOf("DOZER") || unit.Template.IsKindOf("HARVESTER") || unit.Template.IsKindOf("PROJECTILE")) continue;
            for (var other = 0; other < slots; other++)
            {
                if (other == slot) continue;
                foreach (var structure in world.EntitiesOwnedBy(ScriptedSkirmishSession.PlayerOf(other)).AsSpan())
                {
                    if (structure.EffectivelyDead || !structure.Template.IsKindOf("STRUCTURE") || structure.HasStatus(ObjectStatusBits.UnderConstruction)) continue;
                    var dx = (structure.X - unit.X).Floor();
                    var dy = (structure.Y - unit.Y).Floor();
                    if (dx * dx + dy * dy <= AttackReach * AttackReach) return true;
                }
            }
        }
        return false;
    }
}
