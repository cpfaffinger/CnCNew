using CnCNew.Data.World;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

public sealed partial class ScriptEngine
{
    /// <summary>
    /// <c>ScriptActions::doCreateReinforcements</c>. The team's units (<c>teamUnitMaxCount</c> of each type) appear at the team's reinforcement origin (or at the destination)
    /// in a row. Without <c>teamTransport</c> they walk to the destination as one group order when they came from an origin. With a transport the reference creates it first and then
    /// <list type="number">
    /// <item><description>with <c>teamStartsFull</c> loads the team's units into the other <c>TRANSPORT</c> members of the team (first fit, largest first — the reference solves a
    /// partition problem);</description></item>
    /// <item><description>loads every remaining unit the transport accepts (<c>isValidContainerFor</c>) into it, starting a further transport at the destination (the reference's
    /// own position, <c>transportCount · majorRadius</c> beside it) when one is full, and — for a transport with a <c>DeliverPayloadAIUpdate</c> — packs each unit into its
    /// <c>PutInContainer</c> parachute first;</description></item>
    /// <item><description>sends every transport: a plane runs its delivery (<c>deliverPayloadViaModuleData</c>), another transport moves and evacuates
    /// (<c>aiMoveToAndEvacuate</c>), with <c>teamTransportsExit</c> it afterwards returns to where it was ordered and is deleted (<c>aiMoveToAndEvacuateAndExit</c>); units that did
    /// not fit walk to the destination.</description></item>
    /// </list>
    /// Deviation: the team counts as created at once (the reference activates a transport team only when the transport has unloaded, a parachute team never). A transport that is
    /// not instantiable is reported (<c>reinforcement transport not instantiable</c>) and the units walk instead.
    /// </summary>
    private void CreateReinforcements(string teamName, string destinationName)
    {
        if (WaypointOrDiagnose(destinationName) is not { } destination) return;
        var prototype = _program.FindTeam(teamName);
        if (prototype is null) { Diagnose($"unknown team '{teamName}'"); return; }
        var origin = prototype.ReinforcementOrigin.Length > 0 ? _program.FindWaypoint(prototype.ReinforcementOrigin) : null;
        var start = origin ?? destination;
        var team = CreateTeam(teamName);
        if (team is null) return;

        ObjectTemplate? transportTemplate = null;
        if (prototype.Transport.Length > 0)
        {
            if (!_world.Templates.TryGet(prototype.Transport, out var found)) Diagnose($"unknown object type '{prototype.Transport}'");
            else if (!found.IsInstantiable) Diagnose($"reinforcement transport not instantiable '{prototype.Transport}'");
            else transportTemplate = found;
        }
        var walkOnArrival = origin is not null && (origin.X != destination.X || origin.Y != destination.Y);
        if (transportTemplate is not null) team.Members.Add(_world.RequestSpawn(transportTemplate.Index, team.Owner, start.X, start.Y, start.Z, Fixed.Zero).Value);

        var row = Fixed.Zero;
        foreach (var unit in prototype.Units)
        {
            if (!_world.Templates.TryGet(unit.Type, out var template)) { Diagnose($"unknown object type '{unit.Type}'"); continue; }
            if (!template.IsInstantiable) { Diagnose($"not instantiable '{unit.Type}'"); continue; }
            for (var index = 0; index < unit.MaxCount; index++)
            {
                // "pos.x = origin.x + 2.25 * j * majorRadius": a row along x, one row per unit type along y.
                var x = start.X + Fixed.FromInt(9) * index / Fixed.FromInt(4) * template.GeometryMajorRadius;
                var id = _world.RequestSpawn(template.Index, team.Owner, x, start.Y + row, start.Z, Fixed.Zero);
                team.Members.Add(id.Value);
                if (team.AttackPriority.Length > 0) _prioritySetOf[id.Value] = team.AttackPriority;
            }
            row += template.GeometryMajorRadius * 2;
        }
        team.Created = true;
        _world.MaterializePendingSpawns();
        if (transportTemplate is not null) DispatchTransports(team, prototype, transportTemplate, destination, destinationName);
        // "if (needToMoveToDestination)": the team walks to the destination as one group (getTeamAsAIGroup, groupMoveToPosition; F-35).
        else if (walkOnArrival) MoveGroup(team.Members.Select(EntityById).OfType<Entity>().ToArray(), destination.X, destination.Y);
    }

    private Entity? EntityById(uint id) => _world.FindEntity(new EntityId(id));

    private void DispatchTransports(ScriptTeam team, ScriptTeamPrototype prototype, ObjectTemplate transportTemplate, ScriptWaypoint destination, string destinationName)
    {
        var members = team.Members.Select(EntityById).Where(member => member is not null).Select(member => member!).ToList();
        var created = members.FirstOrDefault(member => member.Template == transportTemplate);
        if (created is null) return;
        if (prototype.StartsFull) LoadIntoTeamTransports(members, created);

        // The loading loop of the reference: every unit that is not yet contained goes into the created transport, or into a new one when that is full.
        var current = created;
        var transportCount = 1;
        var putIn = created.ModuleArray.OfType<DeliverPayloadAIUpdate>().FirstOrDefault()?.PutInContainer ?? string.Empty;
        foreach (var member in members.ToArray())
        {
            if (member.Template == transportTemplate || member.ContainedBy.IsValid || current.Container is not { } contain) continue;
            if (!contain.IsValidContainerFor(_world, member, false)) continue;
            var spotX = destination.X + Fixed.FromInt(transportCount) * transportTemplate.GeometryMajorRadius;
            if (!contain.IsValidContainerFor(_world, member, true))
            {
                var id = _world.RequestSpawn(transportTemplate.Index, team.Owner, spotX, destination.Y, _world.GroundHeightAt(spotX, destination.Y), Fixed.Zero);
                team.Members.Add(id.Value);
                _world.MaterializePendingSpawns();
                if (EntityById(id.Value) is not { Container: not null } next) continue;
                transportCount++;
                current = next;
                contain = next.Container!;
                members.Add(next);
            }
            var payload = member;
            if (putIn.Length > 0) payload = Parachute(team, putIn, member, spotX, destination.Y) ?? member;
            if (payload != member) members.Add(payload);
            contain.Add(_world, payload);
            _world.Emit(SimEventKind.EntityContained, payload.Id, current.Id, string.Empty, 0);
        }

        foreach (var member in members)
        {
            if (member.DestroyRequested) continue;
            if (member.Template == transportTemplate)
            {
                var plane = member.ModuleArray.OfType<DeliverPayloadAIUpdate>().FirstOrDefault();
                var ai = member.ModuleArray.OfType<AIUpdateInterface>().FirstOrDefault();
                if (plane is not null) plane.DeliverViaModuleData(_world, destination.X, destination.Y);
                else if (ai is not null) ai.OrderEvacuate(_world, true, destination.X, destination.Y, prototype.TransportsExit);
                else Diagnose($"reinforcement transport '{transportTemplate.Name}' has no AI");
            }
            else if (!member.ContainedBy.IsValid && !member.Template.IsKindOf("PARACHUTE")) StartFollowing(member, PositionMode + destinationName);
        }
    }

    /// <summary>The parachute a unit is packed into (<c>putInContainerTemplate</c>); <see langword="null"/> when it cannot take the unit.</summary>
    private Entity? Parachute(ScriptTeam team, string templateName, Entity unit, Fixed x, Fixed y)
    {
        if (!_world.Templates.TryGet(templateName, out var template)) { Diagnose($"unknown object type '{templateName}'"); return null; }
        if (!template.IsInstantiable) { Diagnose($"not instantiable '{templateName}'"); return null; }
        var id = _world.RequestSpawn(template.Index, team.Owner, x, y, unit.Z, Fixed.Zero);
        team.Members.Add(id.Value);
        _world.MaterializePendingSpawns();
        if (EntityById(id.Value) is not { Container: { } chute } parachute) return null;
        if (!chute.IsValidContainerFor(_world, unit, true)) { Diagnose($"'{templateName}' is full or not valid for '{unit.Template.Name}'"); return null; }
        chute.Add(_world, unit);
        _world.Emit(SimEventKind.EntityContained, unit.Id, parachute.Id, string.Empty, 0);
        return parachute;
    }

    /// <summary><c>teamStartsFull</c>: units go into the team's own <c>TRANSPORT</c> objects (not the created one), largest slot count first, first fit by id.</summary>
    private void LoadIntoTeamTransports(List<Entity> members, Entity created)
    {
        var transports = members.Where(member => member != created && member.Template.IsKindOf("TRANSPORT") && member.Container is not null).OrderBy(member => member.Id.Value).ToArray();
        if (transports.Length == 0) return;
        foreach (var unit in members.Where(member => member != created && !member.Template.IsKindOf("TRANSPORT") && !member.ContainedBy.IsValid)
                     .OrderByDescending(member => member.Template.TransportSlotCount).ThenBy(member => member.Id.Value).ToArray())
            foreach (var transport in transports)
            {
                if (!transport.Container!.IsValidContainerFor(_world, unit, true)) continue;
                transport.Container.Add(_world, unit);
                _world.Emit(SimEventKind.EntityContained, unit.Id, transport.Id, string.Empty, 0);
                break;
            }
    }
}
