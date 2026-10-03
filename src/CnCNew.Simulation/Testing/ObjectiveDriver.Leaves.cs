using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

/// <summary>
/// The world-state leaves of the objective planner: for each condition type the driver knows how to make true, the intervention (an order, a kill, a placed unit, money, a
/// science, ownership); every other type is a <em>blocker</em>. Orders go through the session's command counter like a player's; kills, placements and money write to the
/// world directly, like <see cref="MissionProbe"/> does.
/// </summary>
internal sealed partial class ObjectiveDriver
{
    private const string LocalPlayerName = "<Local Player>";
    private const int GuardMargin = 150;

    private PlayerId? PlayerOf(string name)
    {
        if (name == LocalPlayerName) return _local;
        return _program.FindPlayer(name) is { } player ? player.Id : null;
    }

    private IEnumerable<Entity> LiveOf(PlayerId owner)
        => _world.Entities.Where(entity => entity.Owner == owner && !entity.EffectivelyDead && !entity.DestroyRequested).OrderBy(entity => entity.Id.Value);

    private Entity? Named(string name)
    {
        foreach (var (key, id) in _engine.NamedObjects)
            if (key == name && _world.FindEntity(new EntityId(id)) is { DestroyRequested: false } entity) return entity;
        return null;
    }

    private IEnumerable<Entity> LiveTeamMembers(string prototype)
        => _engine.TeamInstances.Where(team => team.Prototype == prototype).SelectMany(team => team.Members)
            .Select(id => _world.FindEntity(new EntityId(id))).Where(entity => entity is { EffectivelyDead: false, DestroyRequested: false }).Select(entity => entity!).OrderBy(entity => entity.Id.Value);

    private bool Matches(string typeParameter, Entity entity)
    {
        var list = _engine.ObjectListTypes(typeParameter);
        return list.Count > 0 ? list.Contains(entity.Template.Name) : entity.Template.Name == typeParameter;
    }

    private string FirstTemplate(string typeParameter) => _engine.ObjectListTypes(typeParameter) is { Count: > 0 } list ? list.First() : typeParameter;

    private static bool Compare(int comparison, long left, long right) => comparison switch
    {
        0 => left < right,
        1 => left <= right,
        2 => left == right,
        3 => left >= right,
        4 => left > right,
        5 => left != right,
        _ => false,
    };

    /// <summary>The count that satisfies <c>count &lt;op&gt; n</c> closest to the current one.</summary>
    private static long TargetCount(int comparison, long n, long current) => comparison switch
    {
        0 => n - 1,
        1 => n,
        2 => n,
        3 => n,
        4 => n + 1,
        5 => current == n ? n + 1 : current,
        _ => n,
    };

    /// <summary>A point inside the area: the centroid when it lies inside, else the inside point of a coarse grid over the bounding box that is closest to it.</summary>
    private (Fixed X, Fixed Y)? AreaSpot(string name)
    {
        if (_program.FindArea(name) is not { Points.Length: > 0 } area) return null;
        long sumX = 0, sumY = 0, minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
        foreach (var (x, y) in area.Points)
        {
            sumX += x; sumY += y;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        var centreX = sumX / area.Points.Length;
        var centreY = sumY / area.Points.Length;
        if (area.Contains(Fixed.FromInt(centreX), Fixed.FromInt(centreY))) return (Fixed.FromInt(centreX), Fixed.FromInt(centreY));
        long bestX = 0, bestY = 0, bestDistance = long.MaxValue;
        var stepX = Math.Max(1, (maxX - minX) / 16);
        var stepY = Math.Max(1, (maxY - minY) / 16);
        for (var y = minY; y <= maxY; y += stepY)
            for (var x = minX; x <= maxX; x += stepX)
            {
                if (!area.Contains(Fixed.FromInt(x), Fixed.FromInt(y))) continue;
                var distance = (x - centreX) * (x - centreX) + (y - centreY) * (y - centreY);
                if (distance < bestDistance) { bestDistance = distance; bestX = x; bestY = y; }
            }
        return bestDistance == long.MaxValue ? (Fixed.FromInt(centreX), Fixed.FromInt(centreY)) : (Fixed.FromInt(bestX), Fixed.FromInt(bestY));
    }

    private bool Send(Entity entity, (Fixed X, Fixed Y) spot)
    {
        if (entity.Owner.IsSystem || entity.Template.Locomotor is null) return false;
        var owner = entity.Owner;
        var id = entity.Id;
        if (!_session.Issue(owner, (sequence, tick) => new SetMoveTargetCommand(owner, sequence, tick, id, spot.X, spot.Y)).Accepted) return false;
        Commands++;
        return true;
    }

    /// <summary>Puts an object on a spot (the fallback when an order does not get it there in time).</summary>
    private bool Place(Entity entity, (Fixed X, Fixed Y) spot)
    {
        if (_commandsOnly) return false;
        // Only mobile objects: a structure's footprint is registered in the navigation grid at its spawn position and would be removed from the wrong cells on its death.
        if (entity.EffectivelyDead || entity.DestroyRequested || entity.ContainedBy.IsValid || entity.Template.Locomotor is null || entity.Template.IsKindOf("STRUCTURE")) return false;
        entity.X = spot.X;
        entity.Y = spot.Y;
        entity.Z = _world.GroundHeightAt(spot.X, spot.Y);
        entity.HasMoveTarget = false;
        _world.MarkDirty(entity);
        return true;
    }

    /// <summary>An order for the first two attempts (every few seconds), a placement after that.</summary>
    private bool GoTo(Entity entity, (Fixed X, Fixed Y) spot, int attempt)
        => _commandsOnly ? Send(entity, spot) : attempt < 2 ? Send(entity, spot) || Place(entity, spot) : Place(entity, spot);

    private Leaf KillTeamLeaf(string team) => Destroy("killteam:" + team, $"destroy team '{team}'", 20, 30, () => LiveTeamMembers(team).Where(member => !WouldHarm(member.Owner) || _allowHarm).ToArray());

    private Leaf KillPlayerLeaf(string key, string name, PlayerId player, Func<Entity, bool> which) => Destroy(key + ":" + name, $"destroy everything of '{name}'", 20, 60,
        () => WouldHarm(player) && !_allowHarm ? [] : LiveOf(player).Where(which).ToArray());

    /// <summary>Whether destroying this side's objects would end the mission the wrong way: the viewer's own side in a victory run, the viewer's enemies in a defeat run.</summary>
    private bool WouldHarm(PlayerId owner)
        => !owner.IsSystem && (_victory ? owner == _local || _world.AreAllies(_local, owner) : _world.AreEnemies(_local, owner));

    private Plan Guarded(PlayerId? owner, Func<Plan> plan)
        => owner is { } side && WouldHarm(side) ? Harmful(plan()) : plan();

    private static Plan Harmful(Plan plan)
    {
        foreach (var leaf in plan.Leaves) leaf.Harm = true;
        return plan;
    }


    private PlayerId? TeamOwner(string team) => _program.FindTeam(team) is { } prototype ? PlayerOf(prototype.Owner) : null;

    /// <summary>
    /// The leaf (or blocker) of a world-state condition that is false right now. <paramref name="ready"/> says the script owning it is live: edge conditions (entered/exited an
    /// area, lost an object type) only count at the moment they are evaluated, so their leaves wait until it is.
    /// </summary>
    private Plan LeafFor(MapCondition condition, bool ready)
    {
        var p = condition.Parameters;
        var type = condition.TypeName;
        string Text(int index) => index < p.Length ? p[index].Text : string.Empty;
        int Int(int index) => index < p.Length ? p[index].Integer : 0;
        Plan Edge(Leaf leaf)
        {
            leaf.Deferred = !ready;
            return Plan.Of(leaf);
        }
        Plan Unhandled() => Plan.Blocked($"{type}({string.Join(", ", p.Select(value => value.Text.Length > 0 ? value.Text : value.Integer.ToString(System.Globalization.CultureInfo.InvariantCulture)))})");

        switch (type)
        {
            // ---- named objects
            case "NAMED_DESTROYED":
            case "NAMED_DYING":
            case "NAMED_TOTALLY_DEAD":
            {
                var name = Text(0);
                return Guarded(Named(name)?.Owner, () => Plan.Of(Destroy("kill:" + name, $"destroy named '{name}'", 6, 30,
                    () => Named(name) is { } entity && (!WouldHarm(entity.Owner) || _allowHarm) ? [entity] : [])));
            }
            case "NAMED_NOT_DESTROYED": return Plan.Blocked($"named '{Text(0)}' must stay alive but is destroyed");
            case "NAMED_CREATED": return new Plan();
            case "NAMED_OWNED_BY_PLAYER":
            {
                var name = Text(0);
                var owner = Text(1);
                return Plan.Of(new Leaf($"own:{name}:{owner}", $"hand '{name}' to '{owner}'", 3, 30, _ =>
                {
                    if (Named(name) is not { } entity || PlayerOf(owner) is not { } player) return false;
                    return _world.TransferOwnership(entity.Id, player);
                }));
            }
            case "UNIT_HEALTH":
            {
                var name = Text(0);
                var comparison = Int(1);
                var percent = Int(2);
                return Plan.Of(new Leaf($"health:{name}:{comparison}:{percent}", $"set health of '{name}' ({comparison} {percent} %)", 4, 30, _ =>
                {
                    if (Named(name) is not { Body: { } body } || body.MaxHealth <= Fixed.Zero) return false;
                    var target = TargetCount(comparison, percent, 0);
                    if (target < 0) return false;
                    var wanted = body.MaxHealth * Fixed.FromInt((int)Math.Clamp(target, 1, 100)) / Fixed.FromInt(100);
                    var current = body.Health;
                    if (current > wanted) _world.Damage(Named(name)!.Id, new DamageInfo(EntityId.Invalid, current - wanted, DamageTypes.Unresistable, DeathTypes.Normal, false));
                    else if (current < wanted) _world.Damage(Named(name)!.Id, new DamageInfo(EntityId.Invalid, wanted - current, DamageTypes.Healing, DeathTypes.Normal, false));
                    return true;
                }));
            }
            case "NAMED_INSIDE_AREA":
            case "NAMED_ENTERED_AREA":
            {
                var name = Text(0);
                var area = Text(1);
                var leaf = new Leaf($"go:{name}:{area}", $"send named '{name}' to area '{area}'", 4, 90, attempt =>
                {
                    if (Named(name) is not { } entity || AreaSpot(area) is not { } spot) return false;
                    return GoTo(entity, spot, attempt);
                }, edge: type == "NAMED_ENTERED_AREA") { Kind = LeafKind.Order };
                return Edge(leaf);
            }
            case "NAMED_SELECTED":
            {
                var name = Text(0);
                return Plan.Of(new Leaf("select:" + name, $"select named '{name}'", 3, 15, _ => { _engine.SelectNamed(name); return true; }) { Kind = LeafKind.Signal });
            }

            // ---- teams
            case "TEAM_DESTROYED": return Guarded(TeamOwner(Text(0)), () => Plan.Of(KillTeamLeaf(Text(0))));
            case "TEAM_HAS_UNITS":
            case "TEAM_CREATED": return new Plan();
            case "TEAM_INSIDE_AREA_ENTIRELY":
            case "TEAM_INSIDE_AREA_PARTIALLY":
            case "TEAM_ENTERED_AREA_ENTIRELY":
            case "TEAM_ENTERED_AREA_PARTIALLY":
            {
                var team = Text(0);
                var area = Text(1);
                var entering = type.StartsWith("TEAM_ENTERED", StringComparison.Ordinal);
                var leaf = new Leaf($"goteam:{team}:{area}", $"send team '{team}' to area '{area}'", 4, 90, attempt =>
                {
                    if (AreaSpot(area) is not { } spot) return false;
                    var moved = false;
                    foreach (var member in LiveTeamMembers(team).ToArray()) moved |= GoTo(member, spot, attempt);
                    return moved;
                }, edge: entering) { Kind = LeafKind.Order };
                return Edge(leaf);
            }

            // ---- players
            case "PLAYER_ALL_DESTROYED":
            {
                var name = Text(0);
                return PlayerOf(name) is { } player ? Guarded(player, () => Plan.Of(KillPlayerLeaf("killplayer", name, player, _ => true))) : Unhandled();
            }
            case "PLAYER_ALL_BUILDFACILITIES_DESTROYED":
            {
                var name = Text(0);
                return PlayerOf(name) is { } player
                    ? Guarded(player, () => Plan.Of(KillPlayerLeaf("killfacilities", name, player, entity => entity.Template.IsKindOf("STRUCTURE") && entity.Template.Modules.Any(module => module.Name is "ProductionUpdate" or "DozerAIUpdate"))))
                    : Unhandled();
            }
            case "MULTIPLAYER_ALLIED_VICTORY":
                if (!_victory) return Unhandled();
                return Plan.Of(Destroy("killenemies", "destroy every enemy object", 20, 60,
                    () => _world.Entities.Where(entity => !entity.Owner.IsSystem && !entity.EffectivelyDead && _world.AreEnemies(_local, entity.Owner)).OrderBy(entity => entity.Id.Value).ToArray()));
            case "MULTIPLAYER_ALLIED_DEFEAT":
            case "MULTIPLAYER_PLAYER_DEFEAT":
                return Plan.Of(new Leaf("killown", "destroy every object of the viewer and its allies", 20, 60, _ =>
                {
                    var victims = _world.Entities.Where(entity => !entity.Owner.IsSystem && !entity.EffectivelyDead && (entity.Owner == _local || _world.AreAllies(_local, entity.Owner))).OrderBy(entity => entity.Id.Value).Select(entity => entity.Id).ToArray();
                    if (victims.Length == 0) return false;
                    foreach (var id in victims) _world.Kill(id);
                    return true;
                }));
            case "PLAYER_HAS_CREDITS":
            {
                var name = Text(2);
                var comparison = Int(1);
                var amount = Int(0);
                return Plan.Of(new Leaf($"money:{name}:{comparison}:{amount}", $"set '{name}' credits ({amount} {comparison})", 3, 30, _ =>
                {
                    if (PlayerOf(name) is not { } player || !_world.HasPlayer(player)) return false;
                    // The condition reads "amount <op> balance".
                    var balance = comparison switch { 0 => amount + 1, 1 => amount, 2 => amount, 3 => amount, 4 => Math.Max(0, amount - 1), 5 => amount + 1, _ => amount };
                    _world.SetMoney(player, Fixed.FromInt(balance));
                    return true;
                }));
            }
            case "PLAYER_HAS_N_OR_FEWER_BUILDINGS":
            case "PLAYER_HAS_N_OR_FEWER_FACTION_BUILDINGS":
            {
                var name = Text(0);
                var limit = Int(1);
                var faction = type.Contains("FACTION", StringComparison.Ordinal);
                return Plan.Of(new Leaf($"buildings:{name}:{limit}", $"reduce '{name}' to {limit} buildings", 20, 30, _ =>
                {
                    if (PlayerOf(name) is not { } player) return false;
                    var buildings = LiveOf(player).Where(entity => ScriptEngine.CountsAsObject(entity) && entity.Template.IsKindOf("STRUCTURE") && (!faction || entity.Template.IsKindOf("MP_COUNT_FOR_VICTORY"))).ToArray();
                    if (buildings.Length <= limit) return false;
                    foreach (var building in buildings.Skip(Math.Max(limit, 0))) _world.Kill(building.Id);
                    return true;
                }));
            }
            case "PLAYER_HAS_OBJECT_COMPARISON":
                return CountLeaf(Text(0), Int(1), Int(2), Text(3), null, null);
            case "PLAYER_HAS_COMPARISON_UNIT_TYPE_IN_TRIGGER_AREA":
                return CountLeaf(Text(0), Int(1), Int(2), Text(3), Text(4), null);
            case "PLAYER_HAS_COMPARISON_UNIT_KIND_IN_TRIGGER_AREA":
                return CountLeaf(Text(0), Int(1), Int(2), string.Empty, Text(4), Text(3));
            case "BUILT_BY_PLAYER":
            {
                var playerParameter = p.FirstOrDefault(value => value.Type == MapScriptParameterType.Side)?.Text ?? string.Empty;
                var typeParameter = p.FirstOrDefault(value => value.Type == MapScriptParameterType.ObjectType)?.Text ?? string.Empty;
                return CountLeaf(playerParameter, 3, 1, typeParameter, null, null);
            }
            case "PLAYER_LOST_OBJECT_TYPE":
            {
                var name = Text(0);
                var objectType = Text(1);
                return Edge(new Leaf($"lose:{name}:{objectType}", $"destroy one '{objectType}' of '{name}'", 6, 30, _ =>
                {
                    if (PlayerOf(name) is not { } player) return false;
                    var victim = LiveOf(player).FirstOrDefault(entity => Matches(objectType, entity));
                    if (victim is null) return false;
                    _world.Kill(victim.Id);
                    return true;
                }, edge: true));
            }
            case "PLAYER_ACQUIRED_SCIENCE":
            {
                var name = Text(0);
                var science = Text(1);
                return Plan.Of(new Leaf($"science:{name}:{science}", $"grant '{name}' the science '{science}'", 2, 30, _ => PlayerOf(name) is { } player && _world.GrantScience(player, science)));
            }
            case "SKIRMISH_PLAYER_HAS_UNITS_IN_AREA":
            {
                var name = Text(0);
                var area = Text(1);
                return Plan.Of(new Leaf($"present:{name}:{area}", $"bring a unit of '{name}' into area '{area}'", 4, 90, attempt =>
                {
                    if (PlayerOf(name) is not { } player || AreaSpot(area) is not { } spot) return false;
                    var unit = LiveOf(player).FirstOrDefault(entity => entity.Template.Locomotor is not null && !entity.Template.IsKindOf("STRUCTURE") && ScriptEngine.CountsAsObject(entity));
                    return unit is not null && GoTo(unit, spot, attempt);
                }));
            }
            case "SKIRMISH_PLAYER_IS_OUTSIDE_AREA":
            {
                var name = Text(0);
                var area = Text(1);
                return Plan.Of(new Leaf($"vacate:{name}:{area}", $"clear '{name}' out of area '{area}'", 10, 60, _ =>
                {
                    if (PlayerOf(name) is not { } player || _program.FindArea(area) is not { } zone) return false;
                    var inside = LiveOf(player).Where(entity => zone.Contains(entity.X, entity.Y)).Select(entity => entity.Id).ToArray();
                    if (inside.Length == 0) return false;
                    foreach (var id in inside) _world.Kill(id);
                    return true;
                }));
            }
            case "SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER":
            {
                var victimName = Text(0);
                var attackerName = Text(1);
                return Plan.Of(new Leaf($"attack:{victimName}:{attackerName}", $"let '{attackerName}' hurt '{victimName}'", 3, 30, _ => Hurt(PlayerOf(attackerName), LiveOfPlayer(victimName))));
            }
            case "SKIRMISH_PLAYER_HAS_DISCOVERED_PLAYER":
            {
                var seeing = Text(1);
                return Plan.Of(new Leaf("reveal:" + seeing, $"reveal the map to '{seeing}'", 2, 60, _ =>
                {
                    if (PlayerOf(seeing) is not { } player) return false;
                    _world.RevealMapPermanently(player, true);
                    return true;
                }));
            }
            case "NAMED_DISCOVERED":
            case "TEAM_DISCOVERED":
            {
                var seeing = Text(1);
                return Plan.Of(new Leaf("reveal:" + seeing, $"reveal the map to '{seeing}'", 2, 60, _ =>
                {
                    if (PlayerOf(seeing) is not { } player) return false;
                    _world.RevealMapPermanently(player, true);
                    return true;
                }));
            }
            case "NAMED_ATTACKED_BY_PLAYER":
            {
                var name = Text(0);
                var attackerName = Text(1);
                return Plan.Of(new Leaf($"hurt:{name}:{attackerName}", $"let '{attackerName}' hurt '{name}'", 3, 30, _ => Hurt(PlayerOf(attackerName), Named(name) is { } named ? [named] : [])));
            }
            case "TEAM_ATTACKED_BY_PLAYER":
            {
                var team = Text(0);
                var attackerName = Text(1);
                return Plan.Of(new Leaf($"hurtteam:{team}:{attackerName}", $"let '{attackerName}' hurt team '{team}'", 3, 30, _ => Hurt(PlayerOf(attackerName), LiveTeamMembers(team).Take(1))));
            }
            case "NAMED_ATTACKED_BY_OBJECTTYPE":
            case "TEAM_ATTACKED_BY_OBJECTTYPE":
            {
                var subject = Text(0);
                var objectType = Text(1);
                var isTeam = type.StartsWith("TEAM", StringComparison.Ordinal);
                return Plan.Of(new Leaf($"hurttype:{subject}:{objectType}", $"let a '{objectType}' hurt '{subject}'", 3, 30, _ =>
                {
                    var victim = isTeam ? LiveTeamMembers(subject).FirstOrDefault() : Named(subject);
                    var source = _world.Entities.Where(entity => !entity.EffectivelyDead && !entity.DestroyRequested && Matches(objectType, entity)).OrderBy(entity => entity.Id.Value).FirstOrDefault();
                    if (victim is null || source is null) return false;
                    _world.Damage(victim.Id, new DamageInfo(source.Id, Fixed.One, DamageTypes.Unresistable, DeathTypes.Normal, false));
                    return true;
                }));
            }
            case "SKIRMISH_SPECIAL_POWER_READY":
            {
                // What a player cannot do is use a power nobody of its side carries: hand it the first carrier of another side (a captured superweapon) and, since holding it
                // for the whole recharge is a fight the driver does not play, let the power finish recharging at once (like a script's NAMED_SET_SPECIAL_POWER_COUNTDOWN 0).
                var playerName = Text(0);
                var powerName = Text(1);
                if (PlayerOf(playerName) is not { } owner || !_world.SpecialPowers.TryGet(powerName, out var power)) return new Plan();
                bool Carries(Entity entity) => !entity.EffectivelyDead && !entity.DestroyRequested && SpecialPowerRouting.FindModule(entity, power.Index) is not null;
                bool Recharge(Entity carrier)
                {
                    if (SpecialPowerRouting.FindModule(carrier, power.Index) is not { } module) return false;
                    module.SetReadyFrame(_world.Tick);
                    return true;
                }
                var own = _world.Entities.Where(entity => entity.Owner == owner && Carries(entity)).OrderBy(entity => entity.Id.Value).ToArray();
                if (own.Length > 0)
                    return own.Any(entity => SpecialPowerRouting.FindModule(entity, power.Index)!.IsReady(_world)) ? new Plan()
                        : Plan.Of(new Leaf($"recharge:{playerName}:{powerName}", $"finish recharging '{powerName}' of '{playerName}'", 2, 60, _ => Recharge(own[0])));
                return Plan.Of(new Leaf($"carrier:{playerName}:{powerName}", $"capture a carrier of '{powerName}' for '{playerName}'", 2, 60, _ =>
                    _world.Entities.Where(entity => entity.Owner != owner && !entity.Owner.IsSystem && Carries(entity)).OrderBy(entity => entity.Id.Value).FirstOrDefault() is { } carrier
                    && _world.TransferOwnership(carrier.Id, owner) && _capturedCarriers.Add(carrier.Id.Value) && Recharge(carrier)));
            }
            case "SKIRMISH_COMMAND_BUTTON_READY_ALL":
            case "SKIRMISH_COMMAND_BUTTON_READY_PARTIAL":
                return new Plan();
            default:
                return Unhandled();
        }
    }

    private IEnumerable<Entity> LiveOfPlayer(string name) => PlayerOf(name) is { } player ? LiveOf(player) : [];

    /// <summary>One point of damage to the first victim, credited to an object of <paramref name="attacker"/> (the attacker history the engine reads comes from the damage events).</summary>
    private bool Hurt(PlayerId? attacker, IEnumerable<Entity> victims)
    {
        if (attacker is not { } player) return false;
        var source = LiveOf(player).FirstOrDefault();
        var victim = victims.FirstOrDefault(entity => entity.Owner != player);
        if (source is null || victim is null) return false;
        _world.Damage(victim.Id, new DamageInfo(source.Id, Fixed.One, DamageTypes.Unresistable, DeathTypes.Normal, false));
        return true;
    }

    /// <summary>
    /// "<c>player</c> has <c>count &lt;op&gt; n</c> objects of the type (or kind), optionally inside an area": destroy the surplus, or bring that many up — a unit of the
    /// player's already in the world first (placed into the area), a new one of the type otherwise.
    /// </summary>
    private Plan CountLeaf(string playerName, int comparison, int n, string typeParameter, string? area, string? kind)
    {
        var key = $"count:{playerName}:{typeParameter}{kind}:{area}:{comparison}:{n}";
        return Plan.Of(new Leaf(key, $"make '{playerName}' have {comparison} {n} of '{typeParameter}{kind}'{(area is null ? "" : $" in '{area}'")}", 12, 30, attempt =>
        {
            if (PlayerOf(playerName) is not { } player) return false;
            var zone = area is null ? null : _program.FindArea(area);
            if (area is not null && zone is null) return false;
            bool Counts(Entity entity) => (kind is null ? Matches(typeParameter, entity) : !entity.Template.IsKindOf("INERT") && entity.Template.IsKindOf(kind))
                                          && (zone is null || zone.Contains(entity.X, entity.Y));
            var current = LiveOf(player).Count(Counts);
            var target = TargetCount(comparison, n, current);
            if (target < 0) return false;
            if (current > target)
            {
                if (_commandsOnly) { _declined.Add($"destroy the surplus of '{typeParameter}{kind}' of '{playerName}'"); return false; }
                foreach (var surplus in LiveOf(player).Where(Counts).Skip((int)target).ToArray()) _world.Kill(surplus.Id);
                return true;
            }
            if (current == target) return false;
            (Fixed X, Fixed Y)? anchor = area is null ? (LiveOf(player).FirstOrDefault() is { } first ? (first.X, first.Y) : null) : AreaSpot(area);
            if (anchor is not { } spot) return false;
            for (var made = current; made < target; made++)
            {
                if (kind is not null || area is not null)
                {
                    // A unit the player already has elsewhere walks (is placed) there; only an exact type can be created from nothing.
                    var mover = LiveOf(player).FirstOrDefault(entity => !Counts(entity) && entity.Template.Locomotor is not null && !entity.Template.IsKindOf("STRUCTURE") && ScriptEngine.CountsAsObject(entity)
                                                                         && (kind is null ? Matches(typeParameter, entity) : entity.Template.IsKindOf(kind)));
                    if (mover is not null) { GoTo(mover, spot, attempt); continue; }
                }
                if (kind is not null || !_world.Templates.TryGet(FirstTemplate(typeParameter), out var template) || !template.IsInstantiable) return made > current;
                if (_commandsOnly)
                {
                    if (Produce(player, template.Name)) continue;
                    _declined.Add($"create '{template.Name}' for '{playerName}' (no producer can queue it)");
                    return made > current;
                }
                _world.RequestSpawn(template.Index, player, spot.X + Fixed.FromInt(made - current), spot.Y, _world.GroundHeightAt(spot.X, spot.Y), Fixed.Zero);
                _world.MaterializePendingSpawns();
            }
            return true;
        }) { Kind = _commandsOnly ? LeafKind.Order : LeafKind.World });
    }
}

internal sealed partial class ObjectiveDriver
{
    /// <summary>
    /// What a victory run does about the conditions of the defeat sequence that it can keep false: units of a hostile side standing where the mission loses when one of them gets there
    /// (an escape area, a base), found in the conditions of every defeat-leading script (<c>_tainted</c>). The leaf removes those units whenever the condition would hold — the driver's
    /// stand-in for the player defending the objective. Conditions about the viewer's own losses cannot be prevented this way and are left alone.
    /// </summary>
    private List<Leaf> BuildDefense()
    {
        var leaves = new List<Leaf>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in _program.Units.Where(unit => _tainted.Contains(unit.Index)))
            foreach (var or in unit.Script.OrConditions)
                foreach (var condition in or.AndConditions)
                    if (PreventLeaf(condition) is { } leaf && keys.Add(leaf.Key)) leaves.Add(leaf);
        return leaves;
    }

    private Leaf? PreventLeaf(MapCondition condition)
    {
        var p = condition.Parameters;
        string Text(int index) => index < p.Length ? p[index].Text : string.Empty;
        int Int(int index) => index < p.Length ? p[index].Integer : 0;
        string player, area;
        int comparison, n;
        Func<Entity, bool> counts;
        switch (condition.TypeName)
        {
            case "PLAYER_HAS_COMPARISON_UNIT_KIND_IN_TRIGGER_AREA":
            {
                player = Text(0); comparison = Int(1); n = Int(2); area = Text(4);
                var kind = Text(3);
                counts = entity => !entity.Template.IsKindOf("INERT") && entity.Template.IsKindOf(kind);
                break;
            }
            case "PLAYER_HAS_COMPARISON_UNIT_TYPE_IN_TRIGGER_AREA":
            {
                player = Text(0); comparison = Int(1); n = Int(2); area = Text(4);
                var typeParameter = Text(3);
                counts = entity => Matches(typeParameter, entity);
                break;
            }
            case "SKIRMISH_PLAYER_HAS_UNITS_IN_AREA":
                player = Text(0); area = Text(1); comparison = 3; n = 1;
                counts = entity => ScriptEngine.CountsAsObject(entity) && !entity.Template.IsKindOf("PROJECTILE");
                break;
            default:
                return null;
        }
        // Only conditions that become true when hostile units arrive can be guarded (a count that must stay below something is the other way round).
        if (comparison is 0 or 1 || PlayerOf(player) is not { } owner || !_world.AreEnemies(_local, owner)) return null;
        return new Leaf($"defend:{player}:{area}:{comparison}:{n}", $"keep '{player}' out of area '{area}'", 100000, 15, _ =>
        {
            if (_program.FindArea(area) is not { Points.Length: > 0 } zone) return false;
            // The scripts look every tick, the driver every few: a unit has to be removed before it arrives, so the guard reaches a margin around the area.
            long minX = zone.Points.Min(point => point.X) - GuardMargin, maxX = zone.Points.Max(point => point.X) + GuardMargin, minY = zone.Points.Min(point => point.Y) - GuardMargin, maxY = zone.Points.Max(point => point.Y) + GuardMargin;
            var near = LiveOf(owner).Where(entity => counts(entity) && entity.X >= Fixed.FromInt(minX) && entity.X <= Fixed.FromInt(maxX) && entity.Y >= Fixed.FromInt(minY) && entity.Y <= Fixed.FromInt(maxY)).ToArray();
            if (near.Length == 0) return false;
            foreach (var entity in near) _world.Kill(entity.Id);
            return true;
        });
    }
}
