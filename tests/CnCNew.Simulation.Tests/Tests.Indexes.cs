using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 19 (docs/PERF-MISSIONS.md): the lists and lookups that replaced scans over all entities give what the scan gives - the same members in the same order -
/// on worlds changed at random with a fixed seed (spawn, death, removal, owner change, fights), from the first tick, when first asked in the middle of a run,
/// and in a restored session.
/// </summary>
static partial class Tests
{
    /// <summary>A small generator of its own, so that the test neither draws from nor depends on the simulation's streams.</summary>
    private sealed class Dice(uint seed)
    {
        private uint _state = seed;
        public int Next(int exclusiveMax)
        {
            _state = unchecked(_state * 1664525u + 1013904223u);
            return (int)((_state >> 8) % (uint)exclusiveMax);
        }
    }

    private static readonly string[] IndexTemplates = ["AlphaSoldier", "BetaSoldier", "AlphaHero", "AlphaBunker", "AlphaTech", "AlphaGunner", "AlphaTrap"];
    private static readonly string[] IndexKinds = ["INFANTRY", "infantry", "STRUCTURE", "SELECTABLE", "TECH_BUILDING", "PROJECTILE", "NO_SUCH_KIND"];

    /// <summary>One random change of the world: a spawn, a kill, an owner change or nothing; then one to three ticks (unless <paramref name="step"/> is off).</summary>
    private static void RandomWorldChange(MissionSession session, SyntheticFixture fixture, Dice dice, bool step = true, string[]? templates = null)
    {
        templates ??= IndexTemplates;
        var world = session.World;
        var entities = world.Entities.ToArray();
        switch (dice.Next(6))
        {
            case 0:
            case 1:
            {
                Check.That(fixture.Templates.TryGet(templates[dice.Next(templates.Length)], out var template), "The fixture knows the template.");
                world.RequestSpawn(template.Index, new PlayerId(dice.Next(3)), Fixed.FromInt(20 + dice.Next(560)), Fixed.FromInt(20 + dice.Next(360)), Fixed.Zero, Fixed.Zero);
                break;
            }
            case 2:
                if (entities.Length > 0) world.Kill(entities[dice.Next(entities.Length)].Id);
                break;
            case 3:
            case 4:
                if (entities.Length > 0) world.TransferOwnership(entities[dice.Next(entities.Length)].Id, new PlayerId(dice.Next(3)));
                break;
        }
        if (!step) return;
        for (var ticks = 1 + dice.Next(3); ticks > 0; ticks--) session.Step();
    }

    private static void CheckListsAgainstScan(SimulationWorld world, IEnumerable<string> kinds, string when, bool modules = true)
    {
        foreach (var owner in new[] { 0, 1, 2, 99 })
        {
            var scan = world.Entities.Where(entity => entity.Owner.Value == owner).ToArray();
            var list = world.EntitiesOwnedBy(new PlayerId(owner));
            Check.That(list.Count == scan.Length && list.AsSpan().SequenceEqual(scan), $"{when}: the owner list of player {owner} has the members of the scan in the scan's order ({list.Count} against {scan.Length}).");
            for (var index = 0; index < scan.Length; index++) Check.That(ReferenceEquals(list[index], scan[index]), $"{when}: the indexer of the owner list agrees with its span.");
        }
        foreach (var kind in kinds)
        {
            var scan = world.Entities.Where(entity => entity.Template.IsKindOf(kind)).ToArray();
            var list = world.EntitiesOfKind(kind);
            Check.That(list.Count == scan.Length && list.AsSpan().SequenceEqual(scan), $"{when}: the kind list '{kind}' has the members of the scan in the scan's order ({list.Count} against {scan.Length}).");
        }
        if (!modules) return;
        // By an interface modules implement and by a module class.
        var updating = world.Entities.Where(entity => entity.Modules.Any(module => module is CnCNew.Simulation.Modules.IUpdateModule)).ToArray();
        Check.That(world.EntitiesWithModule<CnCNew.Simulation.Modules.IUpdateModule>().AsSpan().SequenceEqual(updating), $"{when}: the list of objects with an update module equals the scan ({updating.Length}).");
        var hunters = world.Entities.Where(entity => entity.Modules.Any(module => module is CnCNew.Simulation.Modules.BuiltIn.CommandButtonHuntUpdate)).ToArray();
        Check.That(world.EntitiesWithModule<CnCNew.Simulation.Modules.BuiltIn.CommandButtonHuntUpdate>().AsSpan().SequenceEqual(hunters), $"{when}: the list of objects with a CommandButtonHuntUpdate equals the scan ({hunters.Length}).");
    }

    private static MapObject[] IndexStartObjects()
    {
        var dice = new Dice(0x1DE7);
        var objects = new List<MapObject>();
        for (var index = 0; index < 24; index++)
            objects.Add(Placed(IndexTemplates[dice.Next(IndexTemplates.Length)], 20 + dice.Next(560), 20 + dice.Next(360), index % 2 == 0 ? "teamA" : "teamB"));
        return [..objects];
    }

    public static void WorldEntityListsMatchScan()
    {
        var (map, fixture, terrain) = MissionMap([], objects: IndexStartObjects());
        // "early" asks from the first tick on (lists kept by the hooks); "late" runs the same changes and asks some lists only in the middle and at the end
        // (lists built from the table when first asked).
        var early = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var late = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var earlyDice = new Dice(0xC0FFEE);
        var lateDice = new Dice(0xC0FFEE);
        string[] lateKinds = ["STRUCTURE", "TECH_BUILDING"];
        var removed = 0;
        var lastOwner = new Dictionary<uint, int>();
        var ownerChanges = 0;
        for (var round = 0; round < 400; round++)
        {
            var before = early.World.Entities.Count;
            RandomWorldChange(early, fixture, earlyDice);
            RandomWorldChange(late, fixture, lateDice);
            if (early.World.Entities.Count < before) removed++;
            foreach (var entity in early.World.Entities)
            {
                if (lastOwner.TryGetValue(entity.Id.Value, out var owner) && owner != entity.Owner.Value) ownerChanges++;
                lastOwner[entity.Id.Value] = entity.Owner.Value;
            }
            CheckListsAgainstScan(early.World, IndexKinds.Except(lateKinds), $"round {round}");
            Check.That(early.World.StateHash == late.World.StateHash, "Asking for the lists does not change the world: both sessions hash the same.");
            if (round == 200) CheckListsAgainstScan(late.World, lateKinds, "first asked in round 200", modules: false);
            if (round > 200) CheckListsAgainstScan(early.World, lateKinds, $"round {round}");

            if (round % 100 != 50) continue;
            // A restored session builds its lists from its own table: the same ids in the same order as the session it was saved from.
            var restored = MissionSnapshot.Restore(MissionSnapshot.Write(early), fixture.Templates, fixture.Fingerprint, terrain, map);
            CheckListsAgainstScan(restored.World, IndexKinds, $"restored in round {round}");
            foreach (var owner in new[] { 0, 1, 2 })
                Check.That(restored.World.EntitiesOwnedBy(new PlayerId(owner)).AsSpan().ToArray().Select(entity => entity.Id.Value)
                    .SequenceEqual(early.World.EntitiesOwnedBy(new PlayerId(owner)).AsSpan().ToArray().Select(entity => entity.Id.Value)), "The restored owner list has the ids of the original one.");
            foreach (var kind in IndexKinds)
                Check.That(restored.World.EntitiesOfKind(kind).AsSpan().ToArray().Select(entity => entity.Id.Value)
                    .SequenceEqual(early.World.EntitiesOfKind(kind).AsSpan().ToArray().Select(entity => entity.Id.Value)), "The restored kind list has the ids of the original one.");
        }
        CheckListsAgainstScan(late.World, IndexKinds, "asked at the end only");
        Check.That(removed > 20 && ownerChanges > 20 && early.World.Entities.Count > 20, $"The run removed objects ({removed}), changed owners ({ownerChanges}) and kept a populated world ({early.World.Entities.Count}).");
        Check.That(early.World.EntitiesOfKind("PROJECTILE").Count == 0 && early.World.EntitiesOwnedBy(new PlayerId(99)).Count == 0, "A kind nobody has and an owner nobody is give empty lists.");
        Check.That(early.World.EntitiesWithModule<CnCNew.Simulation.Modules.IUpdateModule>().Count > 10, "Objects with update modules are in the world at the end.");
    }

    private const int ProbeCounts = 14;

    /// <summary>The scripts whose conditions are read with <see cref="ScriptEngine.ExplainScript"/>: inactive, one condition per OR branch.</summary>
    private static MapScript[] ObjectQueryProbes()
    {
        MapScript Probe(string name, IEnumerable<MapCondition> conditions) => Scr.Script(name, [..conditions.Select(condition => Scr.And(condition))], [], active: false);
        var scripts = new List<MapScript>
        {
            Scr.Script("Init", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("OBJECTLIST_ADDOBJECTTYPE", Scr.Named(MapScriptParameterType.ObjectTypeList, "Guns"), Scr.Type("AlphaGunner")),
                Scr.Do("OBJECTLIST_ADDOBJECTTYPE", Scr.Named(MapScriptParameterType.ObjectTypeList, "Guns"), Scr.Type("BetaSoldier"))], oneShot: true),
        };
        foreach (var player in new[] { PlayerA, PlayerB })
        {
            scripts.Add(Probe("Flags" + player, [
                Scr.If("PLAYER_ALL_DESTROYED", Scr.Side(player)),
                Scr.If("PLAYER_ALL_BUILDFACILITIES_DESTROYED", Scr.Side(player)),
                Scr.If("SKIRMISH_PLAYER_HAS_UNITS_IN_AREA", Scr.Side(player), Scr.Area("LeftArea")),
                Scr.If("BUILT_BY_PLAYER", Scr.Type("AlphaSoldier"), Scr.Side(player)),
                Scr.If("BUILT_BY_PLAYER", Scr.Type("Guns"), Scr.Side(player)),
            ]));
            IEnumerable<MapCondition> Thresholds(Func<int, MapCondition> make) => Enumerable.Range(0, ProbeCounts).Select(make);
            scripts.Add(Probe("Buildings" + player, Thresholds(count => Scr.If("PLAYER_HAS_N_OR_FEWER_BUILDINGS", Scr.Side(player), Scr.Int(count)))));
            scripts.Add(Probe("Victory" + player, Thresholds(count => Scr.If("PLAYER_HAS_N_OR_FEWER_FACTION_BUILDINGS", Scr.Side(player), Scr.Int(count)))));
            scripts.Add(Probe("Type" + player, Thresholds(count => Scr.If("PLAYER_HAS_OBJECT_COMPARISON", Scr.Side(player), Scr.Compare(Scr.GreaterEqual), Scr.Int(count), Scr.Type("AlphaSoldier")))));
            scripts.Add(Probe("List" + player, Thresholds(count => Scr.If("PLAYER_HAS_OBJECT_COMPARISON", Scr.Side(player), Scr.Compare(Scr.GreaterEqual), Scr.Int(count), Scr.Type("Guns")))));
            scripts.Add(Probe("TypeArea" + player, Thresholds(count => Scr.If("PLAYER_HAS_COMPARISON_UNIT_TYPE_IN_TRIGGER_AREA", Scr.Side(player), Scr.Compare(Scr.GreaterEqual), Scr.Int(count), Scr.Type("Guns"), Scr.Area("LeftArea")))));
            scripts.Add(Probe("KindArea" + player, Thresholds(count => Scr.If("PLAYER_HAS_COMPARISON_UNIT_KIND_IN_TRIGGER_AREA", Scr.Side(player), Scr.Compare(Scr.GreaterEqual), Scr.Int(count),
                Scr.Named(MapScriptParameterType.KindOf, "INFANTRY"), Scr.Area("LeftArea")))));
            scripts.Add(Probe("Value" + player, Thresholds(count => Scr.If("SKIRMISH_VALUE_IN_AREA", Scr.Side(player), Scr.Compare(Scr.GreaterEqual), Scr.Int(count * 50), Scr.Area("LeftArea")))));
        }
        return [..scripts];
    }

    /// <summary>What the conditions answered before they read the owner list: a filter over every entity of the world, sorted by id.</summary>
    private static string ObjectQueriesByScan(MissionSession session, string playerName, int player)
    {
        var world = session.World;
        var area = session.Program.FindArea("LeftArea")!;
        var guns = session.Engine.ObjectListTypes("Guns");
        bool Counts(Entity entity) => !entity.EffectivelyDead && !entity.DestroyRequested && !entity.Template.IsKindOf("PROJECTILE") && !entity.Template.IsKindOf("INERT") && !entity.Template.IsKindOf("MINE");
        var mine = world.Entities.Where(entity => entity.Owner.Value == player && Counts(entity)).OrderBy(entity => entity.Id.Value).ToArray();
        string Row(string name, Func<int, bool> answer) => name + playerName + ":" + string.Concat(Enumerable.Range(0, ProbeCounts).Select(count => answer(count) ? '1' : '0'));
        var buildings = mine.Count(entity => entity.Template.IsKindOf("STRUCTURE"));
        var victory = mine.Count(entity => entity.Template.IsKindOf("STRUCTURE") && entity.Template.IsKindOf("MP_COUNT_FOR_VICTORY"));
        var soldiers = mine.Count(entity => entity.Template.Name == "AlphaSoldier");
        var listed = mine.Count(entity => guns.Contains(entity.Template.Name));
        var listedInArea = mine.Count(entity => guns.Contains(entity.Template.Name) && area.Contains(entity.X, entity.Y));
        var kindInArea = mine.Count(entity => !entity.Template.IsKindOf("INERT") && entity.Template.IsKindOf("INFANTRY") && area.Contains(entity.X, entity.Y));
        long value = 0;
        var unitsInArea = false;
        foreach (var entity in world.Entities)
        {
            if (entity.Owner.Value != player || entity.EffectivelyDead || entity.DestroyRequested || entity.Template.IsKindOf("INERT") || !area.Contains(entity.X, entity.Y)) continue;
            value += entity.Template.BuildCost.Floor();
            if (!entity.Template.IsKindOf("PROJECTILE")) unitsInArea = true;
        }
        var facility = mine.Any(entity => entity.Template.IsKindOf("STRUCTURE") && entity.Template.Modules.Any(module => module.Name is "ProductionUpdate" or "DozerAIUpdate"));
        bool[] flags = [mine.Length == 0, !facility, unitsInArea, soldiers != 0, listed != 0];
        return string.Join(";", "Flags" + playerName + ":" + string.Concat(flags.Select(flag => flag ? '1' : '0')), Row("Buildings", count => count >= buildings), Row("Victory", count => count >= victory),
            Row("Type", count => soldiers >= count), Row("List", count => listed >= count), Row("TypeArea", count => listedInArea >= count), Row("KindArea", count => kindInArea >= count),
            Row("Value", count => value >= count * 50));
    }

    private static string ObjectQueriesByEngine(MissionSession session, string playerName)
        => string.Join(";", new[] { "Flags", "Buildings", "Victory", "Type", "List", "TypeArea", "KindArea", "Value" }
            .Select(name => name + playerName + ":" + string.Concat(session.Engine.ExplainScript(name + playerName).Select(item => item.Result ? '1' : '0'))));

    public static void ScriptObjectQueriesMatchScan()
    {
        var (map, fixture, terrain) = MissionMap(ObjectQueryProbes(), objects: IndexStartObjects());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var dice = new Dice(0xBADC0DE);
        var answers = new HashSet<string>(StringComparer.Ordinal);
        for (var round = 0; round < 400; round++)
        {
            RandomWorldChange(session, fixture, dice);
            // Also right after a change that no tick has processed yet: a kill marks the object, a requested spawn is not in the table.
            if (round % 3 == 0 && session.World.Entities.Count > 0) session.World.Kill(session.World.Entities.ElementAt(dice.Next(session.World.Entities.Count)).Id);
            if (round % 5 == 0 && session.World.Entities.Count > 0) session.World.TransferOwnership(session.World.Entities.ElementAt(dice.Next(session.World.Entities.Count)).Id, new PlayerId(1 + dice.Next(2)));
            foreach (var (name, id) in new[] { (PlayerA, 1), (PlayerB, 2) })
            {
                var byScan = ObjectQueriesByScan(session, name, id);
                var byEngine = ObjectQueriesByEngine(session, name);
                Check.That(byScan == byEngine, $"Round {round}: the object conditions of {name} answer what the scan over the world answers.\n  scan   {byScan}\n  engine {byEngine}");
                answers.Add(byScan);
            }
            if (round % 100 != 70) continue;
            var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
            foreach (var (name, id) in new[] { (PlayerA, 1), (PlayerB, 2) })
                Check.That(ObjectQueriesByEngine(restored, name) == ObjectQueriesByScan(restored, name, id) && ObjectQueriesByEngine(restored, name) == ObjectQueriesByEngine(session, name),
                    $"Round {round}: a restored session answers the object conditions of {name} like the scan and like the session it was saved from.");
        }
        Check.That(answers.Count > 100, $"The run went through many different answers ({answers.Count}).");
        Check.That(session.Engine.Diagnostics.Count == 0, "No condition of the probes was malformed: " + string.Join("; ", session.Engine.Diagnostics.Keys));
    }

    public static void EnemyOwnerSearchesMatchPerObjectSearch()
    {
        // Two sessions with the same battle. In "perObject" a script gives team teamA the relation "allies" to itself, which is what its members are to each
        // other anyway: no relation changes, but the engine now has an override, so target acquisition and the teams' enemy-sighted test ask per pair of
        // objects over the whole world. "byOwner" has no override and walks the lists of the enemy players. Both must run the same battle tick for tick.
        MapScript Sub(string name, string counter) => Scr.Script(name, [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("INCREMENT_COUNTER", Scr.Int(1), Scr.Counter(counter))], subroutine: true);
        MapScript[] Scripts(bool overrideRelation) => [
            Sub("OnSight", "sighted"), Sub("OnClear", "clear"),
            Scr.Script("Relate", [Scr.And(Scr.If("CONDITION_TRUE"))], overrideRelation ? [Scr.Do("TEAM_SET_OVERRIDE_RELATION_TO_TEAM", Scr.Team("teamA"), Scr.Team("teamA"), Scr.Int((int)Relationship.Allies))] : [], oneShot: true),
        ];
        MapProperties Wave() => new([Scr.Text("teamName", "Wave"), Scr.Text("teamOwner", PlayerB), Scr.Flag("teamIsSingleton", true), Scr.Text("teamEnemySightedScript", "OnSight"), Scr.Text("teamAllClearScript", "OnClear")]);
        var objects = IndexStartObjects().Concat(Enumerable.Range(0, 6).Select(index => Placed("BetaSoldier", 60 + 90 * index, 40 + 55 * index, "Wave"))).ToArray();
        (MissionSession Session, SyntheticFixture Fixture) Start(bool overrideRelation)
        {
            var (map, fixture, terrain) = MissionMap(Scripts(overrideRelation), objects: objects);
            map = map with { Teams = [..map.Teams, Wave()] };
            return (MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map), fixture);
        }
        var (byOwner, ownerFixture) = Start(false);
        var (perObject, objectFixture) = Start(true);
        var ownerDice = new Dice(0x5EED);
        var objectDice = new Dice(0x5EED);
        for (var round = 0; round < 300; round++)
        {
            RandomWorldChange(byOwner, ownerFixture, ownerDice);
            RandomWorldChange(perObject, objectFixture, objectDice);
            Check.That(byOwner.World.Tick == perObject.World.Tick && byOwner.World.StateHash == perObject.World.StateHash, $"Round {round} (tick {byOwner.World.Tick}): both sessions have the same world.");
            Check.That(byOwner.Engine.CounterValue("sighted") == perObject.Engine.CounterValue("sighted") && byOwner.Engine.CounterValue("clear") == perObject.Engine.CounterValue("clear"),
                $"Round {round}: the enemy-sighted and all-clear scripts ran equally often ({byOwner.Engine.CounterValue("sighted")}/{byOwner.Engine.CounterValue("clear")} against {perObject.Engine.CounterValue("sighted")}/{perObject.Engine.CounterValue("clear")}).");
        }
        Check.That(byOwner.World.RelationshipOverride is null && perObject.World.RelationshipOverride is not null, "Only the second session has a relationship override.");
        Check.That(byOwner.CombatStats.Shots > 20 && byOwner.Engine.CounterValue("sighted") > 0 && byOwner.Engine.CounterValue("clear") > 0,
            $"The battle was one: {byOwner.CombatStats.Shots} shots, enemy sighted {byOwner.Engine.CounterValue("sighted")} times, all clear {byOwner.Engine.CounterValue("clear")} times.");
    }

    /// <summary>What <c>WorldEffectModules.Closest(...) is not null</c> answered when it walked every entity of the world and took the edge distance of each.</summary>
    private static bool AnyNearByScan(SimulationWorld world, Entity me, Fixed range, bool enemies)
    {
        var stealthed = CnCNew.Simulation.Modules.ObjectStatusBits.Index("STEALTHED");
        var detected = CnCNew.Simulation.Modules.ObjectStatusBits.Index("DETECTED");
        foreach (var other in world.Entities)
        {
            if (other.Id == me.Id || other.EffectivelyDead || other.DestroyRequested || other.ContainedBy.IsValid || other.Owner.IsSystem && !enemies) continue;
            var relationship = world.RelationshipOf(me.Owner, other.Owner);
            if (enemies ? relationship != Relationship.Enemies : relationship != Relationship.Allies) continue;
            if (other.Template.IsKindOf("STRUCTURE") && SimulationWorld.ActiveWeaponSet(other) is not { WeaponCount: > 0 }) continue;
            if (enemies && other.HasStatus(stealthed) && !other.HasStatus(detected)) continue;
            var dx = other.X - me.X;
            var dy = other.Y - me.Y;
            var gap = Fixed.Sqrt(dx * dx + dy * dy) - me.Template.BoundingCircleRadius - other.Template.BoundingCircleRadius;
            if (gap < Fixed.Zero) gap = Fixed.Zero;
            if (gap <= range) return true;
        }
        return false;
    }

    public static void CheckpointScanMatchesWorldScan()
    {
        // Checkpoints of both players and of nobody in a world where nothing moves or shoots by itself; soldiers, bunkers and more checkpoints come, go and
        // change sides at random. What a checkpoint saw in a tick is what the scan over the world says about the state that tick started with.
        var files = new Dictionary<string, string>
        {
            ["Data/INI/Object/IndexFixture.ini"] = """
                Object IdxCheckpoint
                  KindOf = STRUCTURE IMMOBILE
                  VisionRange = 80.0
                  Geometry = BOX
                  GeometryMajorRadius = 5.0
                  GeometryMinorRadius = 10.0
                  GeometryHeight = 10.0
                  GeometryIsSmall = No
                  Body = StructureBody Body01
                    MaxHealth = 100.0
                    InitialHealth = 100.0
                  End
                  Behavior = CheckpointUpdate Check01
                    ScanDelayTime = 1000
                  End
                End
                """,
        };
        string[] templates = ["AlphaSoldier", "BetaSoldier", "AlphaHero", "AlphaBunker", "AlphaGunner", "IdxCheckpoint", "AlphaSoldier", "BetaSoldier"];
        var dice = new Dice(0xC4EC);
        var objects = new List<MapObject>();
        for (var index = 0; index < 40; index++)
            objects.Add(Placed(index % 5 == 0 ? "IdxCheckpoint" : templates[dice.Next(templates.Length)], 20 + dice.Next(560), 20 + dice.Next(360), index % 2 == 0 ? "teamA" : "teamB"));
        var (map, fixture, terrain) = MissionMap([], objects: [..objects], extraFiles: files);
        var session = MissionSession.Start(MissionOptions.Standard with { CombatIntervalTicks = 0 }, fixture.Templates, fixture.Fingerprint, terrain, map);
        session.Step();
        var answers = new int[4];
        var compared = 0;
        for (var round = 0; round < 600; round++)
        {
            RandomWorldChange(session, fixture, dice, step: false, templates: templates);
            var expected = new Dictionary<uint, (bool Enemy, bool Ally)>();
            foreach (var entity in session.World.Entities)
            {
                if (entity.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.CheckpointUpdate>().FirstOrDefault() is null) continue;
                var range = entity.Template.VisionRange;
                expected[entity.Id.Value] = (AnyNearByScan(session.World, entity, range, enemies: true), AnyNearByScan(session.World, entity, range, enemies: false));
            }
            var shots = session.CombatStats.Shots;
            session.Step();
            Check.That(session.CombatStats.Shots == shots, "Nothing fires in this world, so nothing dies between a checkpoint's scan and the comparison.");
            foreach (var (id, (enemy, ally)) in expected)
            {
                if (session.World.FindEntity(new EntityId(id)) is not { } entity) continue;
                var module = entity.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.CheckpointUpdate>().First();
                Check.That(module.EnemyNear == enemy && module.AllyNear == ally,
                    $"Round {round}: checkpoint {id} of player {entity.Owner.Value} saw enemy {module.EnemyNear}, ally {module.AllyNear}; the scan over the world says enemy {enemy}, ally {ally}.");
                answers[(enemy ? 1 : 0) + (ally ? 2 : 0)]++;
                compared++;
            }
        }
        Check.That(compared > 3000 && answers.All(count => count > 50), $"Checkpoints were compared {compared} times, in all four situations ({string.Join("/", answers)}).");
    }

    /// <summary>The square root as it was computed before its first guess was improved: Newton's iteration from max(raw, scale).</summary>
    private static long ReferenceSqrtRaw(long raw)
    {
        if (raw == 0) return 0;
        var target = (Int128)raw * Fixed.ScaleRaw;
        var x = (Int128)raw > Fixed.ScaleRaw ? (Int128)raw : Fixed.ScaleRaw;
        while (true)
        {
            var next = (x + target / x) / 2;
            if (next >= x) break;
            x = next;
        }
        return (long)x;
    }

    public static void FixedSqrtMatchesReferenceIteration()
    {
        var dice = new Dice(0x50A7);
        var values = new List<long> { 0, 1, 2, 3, 65535, 65536, 65537, 4L * 65536, long.MaxValue, long.MaxValue - 1, 1L << 62, (1L << 62) - 1, (1L << 32) + 1 };
        for (var bit = 0; bit < 63; bit++) values.AddRange([1L << bit, (1L << bit) + 1, (1L << bit) - 1]);
        // Perfect squares and their neighbours: where a rounding slip would show.
        for (var index = 0; index < 400; index++)
        {
            var root = Fixed.FromRaw(1 + dice.Next(1 << 24) * (long)(1 + dice.Next(1 << 6)));
            var square = (Int128)root.Raw * root.Raw / Fixed.ScaleRaw;
            if (square > long.MaxValue - 2 || square < 2) continue;
            values.AddRange([(long)square - 1, (long)square, (long)square + 1]);
        }
        // Squared distances as the simulation has them, and arbitrary bit patterns.
        for (var index = 0; index < 20000; index++) values.Add(dice.Next(72_000_000) * Fixed.ScaleRaw + dice.Next(65536));
        for (var index = 0; index < 20000; index++) values.Add((((long)dice.Next(1 << 24) << 39) | ((long)dice.Next(1 << 24) << 15) | (uint)dice.Next(1 << 15)) >> dice.Next(40));
        foreach (var raw in values)
        {
            if (raw < 0) continue;
            var expected = ReferenceSqrtRaw(raw);
            Check.That(Fixed.Sqrt(Fixed.FromRaw(raw)).Raw == expected, $"Sqrt of raw {raw} is {Fixed.Sqrt(Fixed.FromRaw(raw)).Raw}, the reference iteration gives {expected}.");
            // And it is the floor of the root: r^2 <= raw * scale < (r + 1)^2.
            var target = (Int128)raw * Fixed.ScaleRaw;
            Check.That((Int128)expected * expected <= target && ((Int128)expected + 1) * ((Int128)expected + 1) > target, $"The reference root of raw {raw} is the floor of the exact root.");
        }
        Check.That(values.Count > 40000, "The comparison covered tens of thousands of values.");
    }

    public static void KindLookupMatchesComparerLookup()
    {
        // IsKindOf was ImmutableArray.Contains(kind, OrdinalIgnoreCase); the loop that replaced it answers the same for every kind and spelling.
        var fixture = new SyntheticFixture(files: ScriptFiles());
        string[] kinds = ["INFANTRY", "infantry", "Infantry", "STRUCTURE", "SELECTABLE", "FS_BASE_DEFENSE", "TECH_BUILDING", "SUPPLY_SOURCE", "PROJECTILE", "INERT", "MINE", "", "INFANTR", "INFANTRYX", "ıNFANTRY"];
        var checkedTemplates = 0;
        var hits = 0;
        for (var index = 0; fixture.Templates.TryGet(IndexTemplates[index % IndexTemplates.Length], out var template) && index < IndexTemplates.Length; index++)
        {
            checkedTemplates++;
            foreach (var kind in kinds.Concat(template.KindOf).Concat(template.KindOf.Select(name => name.ToLowerInvariant())))
            {
                var expected = template.KindOf.Contains(kind, StringComparer.OrdinalIgnoreCase);
                Check.That(template.IsKindOf(kind) == expected, $"{template.Name}.IsKindOf('{kind}') is {template.IsKindOf(kind)}, the comparer lookup says {expected}.");
                if (expected) hits++;
            }
        }
        Check.That(checkedTemplates == IndexTemplates.Length && hits > 20, $"Every template was asked ({checkedTemplates}), with kinds it has ({hits}) and kinds it has not.");
    }
}
