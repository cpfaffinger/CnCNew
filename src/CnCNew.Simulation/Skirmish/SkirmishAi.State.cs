using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.Skirmish;

/// <summary>
/// The state of a <see cref="SkirmishAi"/> as part of the session state: <see cref="Describe"/> feeds the AI's hash and the session's savegame from one routine, <see cref="Load"/>
/// reads it back into an AI that was constructed with the same options over the restored world.
/// <para>
/// Rule (checked by the source test "the state of SkirmishAi names every field"): every instance field of the class is written here and read here, or it is listed in
/// <see cref="NotState"/> with the reason why it is none. A map or a set is written sorted by its key, because nothing acts in its order (the same test forbids enumerating
/// the unordered ones); a list is written in its own order.
/// </para>
/// </summary>
public sealed partial class SkirmishAi
{
    /// <summary>Fields that are deliberately not part of the state, each with its reason. An entry that names no field makes the source test fail.</summary>
    public static readonly ImmutableArray<(string Field, string Reason)> NotState =
    [
        ("_world", "the world the AI plays in; the session restores it"),
        ("_catalog", "definitions; guarded by the definition fingerprint"),
        ("_submit", "the session's command path"),
        ("_options", "construction options; they follow from the match description"),
        ("_side", "from the options"),
        ("_composition", "from the options"),
        ("_plan", "from the options' tactics profile"),
        ("_strength", "from the options' strength"),
        ("_time", "the world's time service"),
        ("_fireInterval", "from the rate"),
        ("_thinkInterval", "from the rate"),
        ("_phase", "from the player number and the rate"),
        ("_tick", "set from the world at the start of every update before anything reads it"),
        ("_visible", "rebuilt by every observation before a think reads it"),
        ("_units", "rebuilt by every observation before a think reads it"),
        ("_structures", "rebuilt by every observation before a think reads it"),
        ("_sites", "rebuilt by every observation before a think reads it"),
        ("_enemyTargets", "rebuilt by every observation before a think reads it"),
        ("_roles", "a pure function of a template"),
        ("_damagePerFrame", "a pure function of two templates and the rate"),
        ("_health", "a pure function of a template"),
        ("_stateHash", "cache of the hash over this description"),
        ("_stateHashValid", "cache of the hash over this description"),
    ];

    private ulong _stateHash;
    private bool _stateHashValid;

    /// <summary>
    /// A 64-bit hash over <see cref="Describe"/>. The state changes only inside an update that observes (and through the owner's settings), so the value is kept between them:
    /// asking for it in every tick costs one comparison.
    /// </summary>
    public ulong StateHash()
    {
        if (_stateHashValid) return _stateHash;
        var hasher = new HashStateWriter();
        Describe(hasher);
        _stateHash = hasher.Hash;
        _stateHashValid = true;
        return _stateHash;
    }

    private void StateChanged() => _stateHashValid = false;

    public void Describe(IStateWriter writer)
    {
        writer.Int("player", _player.Value);
        // Written only when set, so the state of an AI without a profile and at medium strength is what it was before the profiles.
        if (NamedTactics is { } tactics) writer.Int("tactics", (int)tactics);
        if (Strength != AiStrength.Medium) writer.Int("strength", (int)Strength);
        _random.Describe(writer, "random");
        writer.UInt("sequence", _sequence);
        writer.Fixed("budget", _budget);
        writer.Fixed("reserve", _reserve);
        writer.Int("supplyPlacementFailures", _supplyPlacementFailures);
        writer.Bool("attacking", _attacking);
        writer.Int("threshold", _threshold);
        writer.Int("maxThreshold", _maxThreshold);
        writer.Int("stagnantPeak", _stagnantPeak);
        writer.Int("stagnantSince", _stagnantSince);
        DescribePoint(writer, "rally", _rally);
        DescribePoint(writer, "scout", _scout);
        writer.Int("scoutAt", _scoutAt);
        writer.Bool("scoutRevisit", _scoutRevisit);
        DescribePoint(writer, "armyGoal", _armyGoal);
        DescribePoint(writer, "objective", _objective);
        writer.Int("objectiveSince", _objectiveSince);
        writer.Text("threat", _threat);
        writer.Bool("commitEverything", _commitEverything);
        writer.Fixed("objectiveReserve", _objectiveReserve);
        writer.BeginArray("objectives");
        foreach (var (x, y) in _objectives) WritePoint(writer, x, y);
        writer.EndArray();

        writer.Bool("hasArmyOrder", _armyOrder is not null);
        if (_armyOrder is { } armyOrder)
        {
            writer.UInt("armyOrderProducer", armyOrder.Producer.Value);
            writer.Int("armyOrderUnit", armyOrder.Unit.Index);
            writer.Int("armyOrderSince", armyOrder.Since);
        }

        DescribeMemory(writer, "enemies", _enemies);
        DescribeMemory(writer, "warehouses", _warehouses);

        writer.BeginArray("sightings");
        foreach (var (id, sighting) in _sightings)
        {
            writer.BeginObject(null);
            writer.UInt("id", id);
            writer.Int("template", sighting.Template);
            writer.Int("seen", sighting.Seen);
            writer.EndObject();
        }
        writer.EndArray();

        DescribeTicks(writer, "retryAt", _retryAt);
        DescribeTicks(writer, "idleSince", _idleSince);
        DescribeTicks(writer, "boarding", _boarding);

        writer.BeginArray("powerRetry");
        foreach (var (key, until) in _powerRetry.OrderBy(pair => pair.Key.Source).ThenBy(pair => pair.Key.Power, StringComparer.Ordinal))
        {
            writer.BeginObject(null);
            writer.UInt("source", key.Source);
            writer.Text("power", key.Power);
            writer.Int("until", until);
            writer.EndObject();
        }
        writer.EndArray();

        writer.BeginArray("scienceRetry");
        foreach (var (name, until) in _scienceRetry.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.BeginObject(null);
            writer.Text("science", name);
            writer.Int("until", until);
            writer.EndObject();
        }
        writer.EndArray();

        writer.BeginArray("orders");
        foreach (var (id, goal) in _orders.OrderBy(pair => pair.Key))
        {
            writer.BeginObject(null);
            writer.UInt("id", id);
            writer.Fixed("x", goal.X);
            writer.Fixed("y", goal.Y);
            writer.Int("tick", goal.Tick);
            writer.EndObject();
        }
        writer.EndArray();

        writer.BeginArray("siteWatch");
        foreach (var (id, watch) in _siteWatch.OrderBy(pair => pair.Key))
        {
            writer.BeginObject(null);
            writer.UInt("id", id);
            writer.Int("work", watch.Work);
            writer.Int("strikes", watch.Strikes);
            writer.Int("lastResume", watch.LastResume);
            writer.EndObject();
        }
        writer.EndArray();

        writer.BeginArray("rallySet");
        foreach (var id in _rallySet.Order())
        {
            writer.BeginObject(null);
            writer.UInt("id", id);
            writer.EndObject();
        }
        writer.EndArray();

        writer.BeginArray("wave");
        foreach (var id in _wave)
        {
            writer.BeginObject(null);
            writer.UInt("id", id);
            writer.EndObject();
        }
        writer.EndArray();

        // A list: written in its own order.
        writer.BeginArray("badSpots");
        foreach (var (x, y) in _badSpots) WritePoint(writer, x, y);
        writer.EndArray();

        // A list: written in its own order.
        writer.BeginArray("inFlight");
        foreach (var (role, until) in _inFlight)
        {
            writer.BeginObject(null);
            writer.Int("role", (int)role);
            writer.Int("until", until);
            writer.EndObject();
        }
        writer.EndArray();

        writer.BeginArray("clearedHints");
        foreach (var (hint, at) in _clearedHints.OrderBy(pair => pair.Key.X.Raw).ThenBy(pair => pair.Key.Y.Raw))
        {
            writer.BeginObject(null);
            writer.Fixed("x", hint.X);
            writer.Fixed("y", hint.Y);
            writer.Int("at", at);
            writer.EndObject();
        }
        writer.EndArray();

        writer.BeginArray("givenUp");
        foreach (var (x, y) in _givenUp.OrderBy(value => value.X.Raw).ThenBy(value => value.Y.Raw)) WritePoint(writer, x, y);
        writer.EndArray();

        writer.BeginArray("seenAt");
        foreach (var (cell, at) in _seenAt.OrderBy(pair => pair.Key))
        {
            writer.BeginObject(null);
            writer.Int("cell", cell);
            writer.Int("at", at);
            writer.EndObject();
        }
        writer.EndArray();

        writer.BeginArray("unreachable");
        foreach (var cell in _unreachable.Order())
        {
            writer.BeginObject(null);
            writer.Int("cell", cell);
            writer.EndObject();
        }
        writer.EndArray();

        // The two lists below are remembered answers. They are state all the same: what a producer offers is asked of the world when the answer is first needed
        // (SimulationWorld.BuildableOf can be overridden by a script), so a restored AI must go on with the answer it had, not with a fresh one.
        writer.Int("builderTemplateCountAtCache", _builderTemplateCountAtCache);
        writer.BeginArray("builderTemplates");
        foreach (var index in _builderTemplates)
        {
            writer.BeginObject(null);
            writer.Int("template", index);
            writer.EndObject();
        }
        writer.EndArray();
        DescribeTemplateLists(writer, "producible", _producible);
        DescribeTemplateLists(writer, "structureCandidates", _structureCandidates);

        writer.BeginObject("stats");
        DescribeStats(writer);
        writer.EndObject();
    }

    /// <summary>Reads <see cref="Describe"/> back. The AI must have been constructed with the options of the one that wrote it (the player is checked), over the restored world.</summary>
    public void Load(IStateReader reader)
    {
        if (reader.Int("player") != _player.Value) throw new InvalidDataException($"The AI state belongs to player {reader.Int("player")}, not to player {_player.Value}.");
        var tactics = reader.Has("tactics") ? (int?)reader.Int("tactics") : null;
        var strength = reader.Has("strength") ? (int)reader.Int("strength") : (int)AiStrength.Medium;
        if (tactics != (int?)NamedTactics || strength != (int)Strength)
            throw new InvalidDataException($"The AI state was written for tactics {tactics?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"} and strength {strength}, this AI plays {NamedTactics?.ToString() ?? "none"} at {Strength}.");
        _random.Load(reader, "random");
        _sequence = reader.UInt("sequence");
        _budget = reader.Fixed("budget");
        _reserve = reader.Fixed("reserve");
        _supplyPlacementFailures = checked((int)reader.Int("supplyPlacementFailures"));
        _attacking = reader.Bool("attacking");
        _threshold = checked((int)reader.Int("threshold"));
        _maxThreshold = checked((int)reader.Int("maxThreshold"));
        _stagnantPeak = checked((int)reader.Int("stagnantPeak"));
        _stagnantSince = reader.Int("stagnantSince");
        _rally = LoadPoint(reader, "rally");
        _scout = LoadPoint(reader, "scout");
        _scoutAt = reader.Int("scoutAt");
        _scoutRevisit = reader.Bool("scoutRevisit");
        _armyGoal = LoadPoint(reader, "armyGoal");
        _objective = LoadPoint(reader, "objective");
        _objectiveSince = reader.Int("objectiveSince");
        _threat = reader.Text("threat");
        _commitEverything = reader.Bool("commitEverything");
        _objectiveReserve = reader.Fixed("objectiveReserve");
        _objectives = [..reader.Array("objectives").Select(ReadPoint)];

        _armyOrder = reader.Bool("hasArmyOrder")
            ? new ArmyOrder(new EntityId(checked((uint)reader.UInt("armyOrderProducer"))), TemplateAt(reader.Int("armyOrderUnit")), reader.Int("armyOrderSince"))
            : null;

        LoadMemory(reader, "enemies", _enemies);
        LoadMemory(reader, "warehouses", _warehouses);

        _sightings.Clear();
        foreach (var item in reader.Array("sightings")) _sightings[checked((uint)item.UInt("id"))] = (checked((int)item.Int("template")), item.Int("seen"));

        LoadTicks(reader, "retryAt", _retryAt);
        LoadTicks(reader, "idleSince", _idleSince);
        LoadTicks(reader, "boarding", _boarding);

        _powerRetry.Clear();
        foreach (var item in reader.Array("powerRetry")) _powerRetry[(checked((uint)item.UInt("source")), item.Text("power"))] = item.Int("until");
        _scienceRetry.Clear();
        foreach (var item in reader.Array("scienceRetry")) _scienceRetry[item.Text("science")] = item.Int("until");
        _orders.Clear();
        foreach (var item in reader.Array("orders")) _orders[checked((uint)item.UInt("id"))] = new Goal(item.Fixed("x"), item.Fixed("y"), item.Int("tick"));
        _siteWatch.Clear();
        foreach (var item in reader.Array("siteWatch"))
            _siteWatch[checked((uint)item.UInt("id"))] = new SiteWatch(checked((int)item.Int("work")), checked((int)item.Int("strikes")), item.Int("lastResume"));
        _rallySet.Clear();
        foreach (var item in reader.Array("rallySet")) _rallySet.Add(checked((uint)item.UInt("id")));
        _wave.Clear();
        foreach (var item in reader.Array("wave")) _wave.Add(checked((uint)item.UInt("id")));
        _badSpots.Clear();
        foreach (var item in reader.Array("badSpots")) _badSpots.Add(ReadPoint(item));
        _inFlight.Clear();
        foreach (var item in reader.Array("inFlight")) _inFlight.Add(((Role)checked((int)item.Int("role")), item.Int("until")));
        _clearedHints.Clear();
        foreach (var item in reader.Array("clearedHints")) _clearedHints[(item.Fixed("x"), item.Fixed("y"))] = item.Int("at");
        _givenUp.Clear();
        foreach (var item in reader.Array("givenUp")) _givenUp.Add(ReadPoint(item));
        _seenAt.Clear();
        foreach (var item in reader.Array("seenAt")) _seenAt[checked((int)item.Int("cell"))] = item.Int("at");
        _unreachable.Clear();
        foreach (var item in reader.Array("unreachable")) _unreachable.Add(checked((int)item.Int("cell")));

        _builderTemplateCountAtCache = checked((int)reader.Int("builderTemplateCountAtCache"));
        _builderTemplates.Clear();
        foreach (var item in reader.Array("builderTemplates")) _builderTemplates.Add(TemplateAt(item.Int("template")).Index);
        LoadTemplateLists(reader, "producible", _producible);
        LoadTemplateLists(reader, "structureCandidates", _structureCandidates);

        LoadStats(reader.Object("stats"));
        StateChanged();
    }

    private ObjectTemplate TemplateAt(long index)
        => index >= 0 && index < _catalog.Templates.Length ? _catalog[checked((int)index)] : throw new InvalidDataException($"The AI state names the unknown template index {index}.");

    private static void WritePoint(IStateWriter writer, Fixed x, Fixed y)
    {
        writer.BeginObject(null);
        writer.Fixed("x", x);
        writer.Fixed("y", y);
        writer.EndObject();
    }

    private static (Fixed X, Fixed Y) ReadPoint(IStateReader reader) => (reader.Fixed("x"), reader.Fixed("y"));

    private static void DescribePoint(IStateWriter writer, string name, (Fixed X, Fixed Y)? point)
    {
        writer.BeginObject(name);
        writer.Bool("set", point is not null);
        if (point is { } value)
        {
            writer.Fixed("x", value.X);
            writer.Fixed("y", value.Y);
        }
        writer.EndObject();
    }

    private static (Fixed X, Fixed Y)? LoadPoint(IStateReader reader, string name)
    {
        var point = reader.Object(name);
        return point.Bool("set") ? (point.Fixed("x"), point.Fixed("y")) : null;
    }

    private static void DescribeMemory(IStateWriter writer, string name, SortedDictionary<uint, Remembered> memory)
    {
        writer.BeginArray(name);
        foreach (var entry in memory.Values)
        {
            writer.BeginObject(null);
            writer.UInt("id", entry.Id.Value);
            writer.Int("template", entry.Template.Index);
            writer.Int("owner", entry.Owner.Value);
            writer.Fixed("x", entry.X);
            writer.Fixed("y", entry.Y);
            writer.Int("seen", entry.Seen);
            writer.Bool("structure", entry.IsStructure);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadMemory(IStateReader reader, string name, SortedDictionary<uint, Remembered> memory)
    {
        memory.Clear();
        foreach (var item in reader.Array(name))
        {
            var id = checked((uint)item.UInt("id"));
            memory[id] = new Remembered(new EntityId(id), TemplateAt(item.Int("template")), new PlayerId(checked((int)item.Int("owner"))), item.Fixed("x"), item.Fixed("y"),
                item.Int("seen"), item.Bool("structure"));
        }
    }

    private static void DescribeTicks(IStateWriter writer, string name, IDictionary<uint, long> ticks)
    {
        writer.BeginArray(name);
        foreach (var (id, tick) in ticks.OrderBy(pair => pair.Key))
        {
            writer.BeginObject(null);
            writer.UInt("id", id);
            writer.Int("tick", tick);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private static void LoadTicks(IStateReader reader, string name, IDictionary<uint, long> ticks)
    {
        ticks.Clear();
        foreach (var item in reader.Array(name)) ticks[checked((uint)item.UInt("id"))] = item.Int("tick");
    }

    private static void DescribeTemplateLists(IStateWriter writer, string name, Dictionary<int, ImmutableArray<ObjectTemplate>> lists)
    {
        writer.BeginArray(name);
        foreach (var (key, list) in lists.OrderBy(pair => pair.Key))
        {
            writer.BeginObject(null);
            writer.Int("key", key);
            writer.BeginArray("templates");
            foreach (var template in list)
            {
                writer.BeginObject(null);
                writer.Int("template", template.Index);
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadTemplateLists(IStateReader reader, string name, Dictionary<int, ImmutableArray<ObjectTemplate>> lists)
    {
        lists.Clear();
        foreach (var item in reader.Array(name))
            lists[checked((int)item.Int("key"))] = [..item.Array("templates").Select(entry => TemplateAt(entry.Int("template")))];
    }

    private void DescribeStats(IStateWriter writer)
    {
        writer.Int("commands", Stats.Commands);
        writer.Int("rejected", Stats.Rejected);
        writer.Int("constructions", Stats.Constructions);
        writer.Int("resumes", Stats.Resumes);
        writer.Int("cancelledSites", Stats.CancelledSites);
        writer.Int("productions", Stats.Productions);
        writer.Int("moveOrders", Stats.MoveOrders);
        writer.Int("powerUses", Stats.PowerUses);
        writer.Int("sciencePurchases", Stats.SciencePurchases);
        writer.Int("repairs", Stats.Repairs);
        writer.Int("dockOrders", Stats.DockOrders);
        writer.Int("waves", Stats.Waves);
        writer.Int("idleThinks", Stats.IdleThinks);
        writer.Int("tunnelEntries", Stats.TunnelEntries);
        writer.Int("evacuations", Stats.Evacuations);
        writer.Int("transportLoads", Stats.TransportLoads);
        writer.Int("attackTicks", Stats.AttackTicks);
        writer.Int("informedDraws", Stats.InformedDraws);
        writer.Int("uninformedDraws", Stats.UninformedDraws);
        writer.Int("objectivesGivenUp", Stats.ObjectivesGivenUp);
        writer.BeginArray("rejectionsByReason");
        foreach (var (reason, count) in Stats.RejectionsByReason)
        {
            writer.BeginObject(null);
            writer.Int("reason", (int)reason);
            writer.Int("count", count);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("armyProduction");
        foreach (var (key, count) in Stats.ArmyProduction)
        {
            writer.BeginObject(null);
            writer.Text("key", key);
            writer.Int("count", count);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadStats(IStateReader reader)
    {
        Stats.Commands = checked((int)reader.Int("commands"));
        Stats.Rejected = checked((int)reader.Int("rejected"));
        Stats.Constructions = checked((int)reader.Int("constructions"));
        Stats.Resumes = checked((int)reader.Int("resumes"));
        Stats.CancelledSites = checked((int)reader.Int("cancelledSites"));
        Stats.Productions = checked((int)reader.Int("productions"));
        Stats.MoveOrders = checked((int)reader.Int("moveOrders"));
        Stats.PowerUses = checked((int)reader.Int("powerUses"));
        Stats.SciencePurchases = checked((int)reader.Int("sciencePurchases"));
        Stats.Repairs = checked((int)reader.Int("repairs"));
        Stats.DockOrders = checked((int)reader.Int("dockOrders"));
        Stats.Waves = checked((int)reader.Int("waves"));
        Stats.IdleThinks = checked((int)reader.Int("idleThinks"));
        Stats.TunnelEntries = checked((int)reader.Int("tunnelEntries"));
        Stats.Evacuations = checked((int)reader.Int("evacuations"));
        Stats.TransportLoads = checked((int)reader.Int("transportLoads"));
        Stats.AttackTicks = reader.Int("attackTicks");
        Stats.InformedDraws = checked((int)reader.Int("informedDraws"));
        Stats.UninformedDraws = checked((int)reader.Int("uninformedDraws"));
        Stats.ObjectivesGivenUp = checked((int)reader.Int("objectivesGivenUp"));
        Stats.RejectionsByReason.Clear();
        foreach (var item in reader.Array("rejectionsByReason")) Stats.RejectionsByReason[(Commands.CommandRejection)checked((int)item.Int("reason"))] = checked((int)item.Int("count"));
        Stats.ArmyProduction.Clear();
        foreach (var item in reader.Array("armyProduction")) Stats.ArmyProduction[item.Text("key")] = checked((int)item.Int("count"));
    }
}
