using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// Conditions of the <c>SKIRMISH_*</c> family and the sight/special-power conditions (Phase 11). They live in their own file so the switch of
/// <see cref="EvaluateCondition"/> stays the place of the engine-level types; <see cref="TryEvaluateExtendedCondition"/> is called from its default branch.
/// Every function cites its reference (<c>ScriptConditions.cpp</c>).
/// </summary>
public sealed partial class ScriptEngine
{
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int DetectedBit = ObjectStatusBits.Index("DETECTED");
    private static readonly int DisguisedBit = ObjectStatusBits.Index("DISGUISED");
    private static readonly int UnderConstructionBit = ObjectStatusBits.UnderConstruction;

    /// <summary><c>Player::setAttackedBy</c> (<c>ActiveBody::attemptDamage</c>): the pairs (victim owner, attacker owner) that ever happened, never reset.</summary>
    private readonly SortedSet<(int Victim, int Attacker)> _attackedBy = [];

    /// <summary><c>ScriptEngine::notifyOfTriggeredSpecialPower</c>: the triggered powers (player, power name, source object) waiting to be consumed by a condition.</summary>
    private readonly List<(int Player, string Power, uint Source)> _triggeredPowers = [];

    private bool TryEvaluateExtendedCondition(MapCondition condition, out bool result)
    {
        var p = condition.Parameters;
        var type = condition.TypeName;
        result = false;
        switch (type)
        {
            case "SKIRMISH_PLAYER_HAS_UNITS_IN_AREA":
                // evaluateSkirmishPlayerHasUnitsInArea: the reference caches the answer until a team entered/left an area or the object count changed (a pure
                // optimisation); the answer itself is "some live, non-inert, non-projectile object of the player is inside the area".
                result = Has(type, p, 2) && AreaOrDiagnose(Str(p, 1)) is { } unitsArea && ResolvePlayer(Str(p, 0)) is { } unitsOwner && PlayerHasObjectsInArea(unitsOwner.Id, unitsArea);
                return true;
            case "SKIRMISH_PLAYER_IS_OUTSIDE_AREA":
                // evaluateSkirmishPlayerIsOutsideArea: both the player and the area must exist, then the negation of the previous condition.
                result = Has(type, p, 2) && AreaOrDiagnose(Str(p, 1)) is { } outsideTarget && ResolvePlayer(Str(p, 0)) is { } outsideOwner && !PlayerHasObjectsInArea(outsideOwner.Id, outsideTarget);
                return true;
            case "SKIRMISH_NAMED_AREA_EXIST":
                // evaluateSkirmishNamedAreaExists: the named trigger area exists on the map.
                result = Has(type, p, 2) && _program.FindArea(Str(p, 1)) is not null;
                return true;
            case "SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER":
                result = Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } victim && ResolvePlayer(Str(p, 1)) is { } attacker && _attackedBy.Contains((victim.Id.Value, attacker.Id.Value));
                return true;
            case "SKIRMISH_VALUE_IN_AREA":
            {
                // evaluateSkirmishValueInArea: the summed build cost of the player's live, non-inert objects inside the area, compared against the parameter.
                if (!Has(type, p, 4) || ResolvePlayer(Str(p, 0)) is not { } valueOwner || AreaOrDiagnose(Str(p, 3)) is not { } valueArea) return true;
                long total = 0;
                foreach (var entity in _world.Entities)
                    if (entity.Owner == valueOwner.Id && !entity.EffectivelyDead && !entity.DestroyRequested && !entity.Template.IsKindOf("INERT") && valueArea.Contains(entity.X, entity.Y))
                        total += entity.Template.BuildCost.Floor();
                result = Compare(Int(p, 1), total, Int(p, 2));
                return true;
            }
            case "SKIRMISH_SPECIAL_POWER_READY":
                result = Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } powerOwner && SkirmishSpecialPowerReady(powerOwner.Id, Str(p, 1));
                return true;
            case "SKIRMISH_PLAYER_HAS_COMPARISON_GARRISONED":
            {
                // evaluateSkirmishPlayerHasComparisonGarrisoned: how many of the player's garrisonable containers hold somebody.
                if (!Has(type, p, 3) || ResolvePlayer(Str(p, 0)) is not { } garrisonOwner) return true;
                var garrisoned = PlayerEntities(garrisonOwner.Id).Count(entity => entity.Container is { ContainCount: > 0 } && entity.Template.Modules.Any(module => module.Name == "GarrisonContain"));
                result = Compare(Int(p, 1), garrisoned, Int(p, 2));
                return true;
            }
            case "SKIRMISH_PLAYER_HAS_DISCOVERED_PLAYER":
                // evaluateSkirmishPlayerHasDiscoveredPlayer: some object of the first player stands in a cell the second player currently sees.
                result = Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } seen && ResolvePlayer(Str(p, 1)) is { } seeing && _world.Entities.Any(entity => entity.Owner == seen.Id && !entity.DestroyRequested && _world.IsPositionVisibleTo(seeing.Id, entity.X, entity.Y));
                return true;
            case "PLAYER_TRIGGERED_SPECIAL_POWER":
            case "PLAYER_TRIGGERED_SPECIAL_POWER_FROM_NAMED":
            {
                // evaluatePlayerSpecialPowerFromUnitTriggered -> ScriptEngine::isSpecialPowerTriggered(removeFromList = TRUE): consuming, optionally for one source object.
                var wantsUnit = type == "PLAYER_TRIGGERED_SPECIAL_POWER_FROM_NAMED";
                if (!Has(type, p, wantsUnit ? 3 : 2)) return true;
                var source = EntityId.Invalid;
                if (wantsUnit)
                {
                    if (NamedEntity(Str(p, 2)) is not { } fromUnit) return true;
                    source = fromUnit.Id;
                }
                if (ResolvePlayer(Str(p, 0)) is not { } triggerer) return true;
                var index = _triggeredPowers.FindIndex(entry => entry.Player == triggerer.Id.Value && entry.Power == Str(p, 1) && (!source.IsValid || entry.Source == source.Value));
                if (index < 0) return true;
                _triggeredPowers.RemoveAt(index);
                result = true;
                return true;
            }
            case "SKIRMISH_COMMAND_BUTTON_READY_ALL":
            case "SKIRMISH_COMMAND_BUTTON_READY_PARTIAL":
                // evaluateSkirmishCommandButtonIsReady (the player parameter is unused there as well).
                result = Has(type, p, 3) && CommandButtonReady(Str(p, 1), Str(p, 2), type.EndsWith("ALL", StringComparison.Ordinal));
                return true;
            case "NAMED_BUILDING_IS_EMPTY":
                // evaluateIsBuildingEmpty: an existing container without occupants (a non-container is not "empty").
                result = Has(type, p, 1) && NamedEntity(Str(p, 0)) is { Container: { } container } && container.ContainCount == 0;
                return true;
            case "NAMED_DISCOVERED":
                // evaluateNamedDiscovered: the object is currently clear of shroud for the player and not stealthed-and-undetected.
                result = Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } discovered && ResolvePlayer(Str(p, 1)) is { } discoverer && IsDiscoveredBy(discovered, discoverer.Id);
                return true;
            case "TEAM_DISCOVERED":
                result = Has(type, p, 2) && FindTeam(Str(p, 0)) is { } discoveredTeam && ResolvePlayer(Str(p, 1)) is { } teamDiscoverer && LiveMembers(discoveredTeam).Any(member => IsDiscoveredBy(member, teamDiscoverer.Id));
                return true;
            case "TYPE_SIGHTED":
            {
                // evaluateTypeSighted: within the unit's vision range there is a live, visible-by-stealth-rules object of the player with the type.
                if (!Has(type, p, 3) || NamedEntity(Str(p, 0)) is not { } watcher || ResolvePlayer(Str(p, 2)) is not { } sightedOwner) return true;
                result = SightedObjects(watcher).Any(entity => entity.Owner == sightedOwner.Id && TypeMatches(Str(p, 1), entity));
                return true;
            }
            case "ENEMY_SIGHTED":
            {
                // evaluateEnemySighted: like TYPE_SIGHTED for any object of the player whose relation to the watcher is the requested one (0 enemy, 1 neutral, 2 friend).
                if (!Has(type, p, 3) || NamedEntity(Str(p, 0)) is not { } scout || ResolvePlayer(Str(p, 2)) is not { } sightedSide || !Enum.IsDefined((Relationship)Int(p, 1))) return true;
                var wanted = (Relationship)Int(p, 1);
                result = SightedObjects(scout).Any(entity => entity.Owner == sightedSide.Id && _world.RelationshipOf(scout.Owner, entity.Owner) == wanted);
                return true;
            }
            default:
                return false;
        }
    }

    private bool PlayerHasObjectsInArea(PlayerId player, ScriptArea area)
    {
        foreach (var entity in _world.Entities)
            if (entity.Owner == player && !entity.EffectivelyDead && !entity.DestroyRequested && !entity.Template.IsKindOf("INERT") && !entity.Template.IsKindOf("PROJECTILE") && area.Contains(entity.X, entity.Y))
                return true;
        return false;
    }

    /// <summary>
    /// <c>evaluateSkirmishSpecialPowerIsReady</c>: does any of the player's finished, usable objects carry the power ready? Without a disabled-status model
    /// the "disabled" test of the reference is dropped (<c>docs/SCRIPT-ENGINE.md</c> §8); an unknown power name never becomes true (and is counted once per call).
    /// </summary>
    private bool SkirmishSpecialPowerReady(PlayerId player, string powerName)
    {
        if (!_world.SpecialPowers.TryGet(powerName, out var info))
        {
            Diagnose($"unknown special power '{powerName}'");
            return false;
        }
        foreach (var entity in PlayerEntities(player))
        {
            if (entity.HasStatus(UnderConstructionBit)) continue;
            if (SpecialPowerRouting.FindModule(entity, info.Index) is not { } module) continue;
            if (info.RequiredScience >= 0 && !_world.PlayerSciences(player).Has(info.RequiredScience)) continue;
            if (module.IsReady(_world)) return true;
        }
        return false;
    }

    /// <summary>The stealth rule of <c>evaluateNamedDiscovered</c>: a stealthed object is not visible unless detected or disguised.</summary>
    private static bool IsHiddenByStealth(Entity entity)
        => entity.HasStatus(StealthedBit) && !entity.HasStatus(DetectedBit) && !entity.HasStatus(DisguisedBit);

    private bool IsDiscoveredBy(Entity entity, PlayerId player)
        => !IsHiddenByStealth(entity) && !entity.ContainedBy.IsValid && (entity.Owner == player || _world.IsPositionVisibleTo(player, entity.X, entity.Y));

    /// <summary>The objects <c>iterateObjectsInRange(vision range)</c> of the watcher finds with the alive/non-stealthed filters of the sight conditions, in id order.</summary>
    private IEnumerable<Entity> SightedObjects(Entity watcher)
    {
        var range = watcher.Template.VisionRange;
        var limit = range * range;
        foreach (var entity in _world.Entities.OrderBy(value => value.Id.Value))
        {
            if (entity == watcher || entity.EffectivelyDead || entity.DestroyRequested || IsHiddenByStealth(entity)) continue;
            if (DistanceSquared(watcher, entity.X, entity.Y) <= limit) yield return entity;
        }
    }

    private void ObserveExtended(CnCNew.Simulation.Events.SimEvent item)
    {
        switch (item.Kind)
        {
            case CnCNew.Simulation.Events.SimEventKind.EntityDamaged when item.Other.IsValid:
                // ActiveBody::attemptDamage: the victim's player records the attacker's player (when the source object still exists).
                if (_world.FindEntity(item.Entity) is { } hurt && _world.FindEntity(item.Other) is { } source && !hurt.Owner.IsSystem)
                    _attackedBy.Add((hurt.Owner.Value, source.Owner.Value));
                break;
            case CnCNew.Simulation.Events.SimEventKind.EntityDied:
                // Team::notifyTeamOfObjectDeath: every member's death runs the team's unit-destroyed script (queued for the next update).
                foreach (var team in _teams.Values)
                    if (team.Members.Contains(item.Entity.Value) && _program.FindTeam(team.Prototype) is { OnUnitDestroyedScript.Length: > 0 } dying) _pendingTeamScripts.Add((team.Id, dying.OnUnitDestroyedScript));
                break;
            case CnCNew.Simulation.Events.SimEventKind.EntitySpawned:
                if (_world.FindEntity(item.Entity) is { } produced) JoinProducedUnit(produced);
                break;
            case CnCNew.Simulation.Events.SimEventKind.SpecialPowerUsed:
                // SpecialPowerModule::triggerSpecialPower -> notifyOfTriggeredSpecialPower(owner, power, source).
                _triggeredPowers.Add((item.Player.Value, item.Text, item.Entity.Value));
                break;
        }
    }

    private void DescribeExtended(IStateWriter writer)
    {
        DescribeTeamBuilds(writer);
        DescribeSequences(writer);
        DescribePowerState(writer);
        DescribeTeamAbilityState(writer);
        if (_pendingTeamScripts.Count > 0)
        {
            writer.BeginArray("pendingTeamScripts");
            foreach (var (teamId, script) in _pendingTeamScripts)
            {
                writer.BeginObject(null);
                writer.Int("team", teamId);
                writer.Text("script", script);
                writer.EndObject();
            }
            writer.EndArray();
        }
        // Written only when non-empty: a run that never used these keeps the serialized form (and hash) it had before they existed.
        if (_attackedBy.Count > 0)
        {
        writer.BeginArray("attackedBy");
        foreach (var (victim, attacker) in _attackedBy)
        {
            writer.BeginObject(null);
            writer.Int("victim", victim);
            writer.Int("attacker", attacker);
            writer.EndObject();
        }
        writer.EndArray();
        }
        if (_triggeredPowers.Count > 0)
        {
        writer.BeginArray("triggeredPowers");
        foreach (var (player, power, source) in _triggeredPowers)
        {
            writer.BeginObject(null);
            writer.Int("player", player);
            writer.Text("power", power);
            writer.UInt("source", source);
            writer.EndObject();
        }
        writer.EndArray();
        }
    }

    private void LoadExtended(IStateReader reader)
    {
        LoadTeamBuilds(reader);
        LoadSequences(reader);
        LoadPowerState(reader);
        LoadTeamAbilityState(reader);
        _pendingTeamScripts.Clear();
        if (reader.Has("pendingTeamScripts"))
            foreach (var item in reader.Array("pendingTeamScripts")) _pendingTeamScripts.Add((checked((int)item.Int("team")), item.Text("script")));
        _attackedBy.Clear();
        if (reader.Has("attackedBy")) foreach (var item in reader.Array("attackedBy")) _attackedBy.Add((checked((int)item.Int("victim")), checked((int)item.Int("attacker"))));
        _triggeredPowers.Clear();
        if (reader.Has("triggeredPowers")) foreach (var item in reader.Array("triggeredPowers")) _triggeredPowers.Add((checked((int)item.Int("player")), item.Text("power"), checked((uint)item.UInt("source"))));
    }
}
