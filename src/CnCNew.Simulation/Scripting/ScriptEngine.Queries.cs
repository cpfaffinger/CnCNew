using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// The object queries of the conditions that run every tick (docs/PERF-MISSIONS.md): "the player's objects" is read from the world's owner list
/// (<see cref="SimulationWorld.EntitiesOwnedBy"/>: the same members in the same order as a scan over all entities) instead of filtering and sorting the
/// whole world per condition, and the kind tests of a template are looked up once per template. Nothing here is state.
/// </summary>
public sealed partial class ScriptEngine
{
    [Flags]
    private enum TemplateFacts : byte
    {
        Known = 1,
        /// <summary>Not <c>PROJECTILE</c>, <c>INERT</c> or <c>MINE</c>: the template part of <see cref="CountsAsObject"/>.</summary>
        Countable = 2,
        Structure = 4,
        VictoryStructure = 8,
        /// <summary>The template part of <see cref="IsBuildFacility"/>.</summary>
        BuildFacility = 16,
        Inert = 32,
        Projectile = 64,
    }

    // By template index. A template never changes, so each entry is a fixed answer looked up once.
    private TemplateFacts[] _templateFacts = [];

    private TemplateFacts FactsOf(ObjectTemplate template)
    {
        var index = template.Index;
        if (index >= _templateFacts.Length) Array.Resize(ref _templateFacts, Math.Max(index + 1, _templateFacts.Length * 2));
        var facts = _templateFacts[index];
        if (facts != 0) return facts;
        facts = TemplateFacts.Known;
        var inert = template.IsKindOf("INERT");
        var projectile = template.IsKindOf("PROJECTILE");
        if (inert) facts |= TemplateFacts.Inert;
        if (projectile) facts |= TemplateFacts.Projectile;
        if (!projectile && !inert && !template.IsKindOf("MINE")) facts |= TemplateFacts.Countable;
        if (template.IsKindOf("STRUCTURE"))
        {
            facts |= TemplateFacts.Structure;
            if (template.IsKindOf("MP_COUNT_FOR_VICTORY")) facts |= TemplateFacts.VictoryStructure;
            foreach (var module in template.Modules)
                if (module.Name is "ProductionUpdate" or "DozerAIUpdate") { facts |= TemplateFacts.BuildFacility; break; }
        }
        _templateFacts[index] = facts;
        return facts;
    }

    /// <summary><see cref="CountsAsObject"/> with the kinds of the template looked up instead of compared name by name.</summary>
    private bool Counts(Entity entity)
        => !entity.EffectivelyDead && !entity.DestroyRequested && (FactsOf(entity.Template) & TemplateFacts.Countable) != 0;

    /// <summary>
    /// The player's objects that count (<see cref="CountsAsObject"/>) in id order, as a list of its own: the caller may kill, transfer or move them while it
    /// walks the list. Conditions, which only read, walk <see cref="SimulationWorld.EntitiesOwnedBy"/> directly instead.
    /// </summary>
    private List<Entity> PlayerEntities(PlayerId player)
    {
        var result = new List<Entity>();
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
            if (Counts(entity)) result.Add(entity);
        return result;
    }

    private bool PlayerHasAnyObject(PlayerId player)
    {
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
            if (Counts(entity)) return true;
        return false;
    }

    private bool PlayerHasBuildFacility(PlayerId player)
    {
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
            if (Counts(entity) && (FactsOf(entity.Template) & TemplateFacts.BuildFacility) != 0) return true;
        return false;
    }

    private int CountBuildings(PlayerId player, bool victoryRelevantOnly)
    {
        var wanted = victoryRelevantOnly ? TemplateFacts.Structure | TemplateFacts.VictoryStructure : TemplateFacts.Structure;
        var count = 0;
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
            if (Counts(entity) && (FactsOf(entity.Template) & wanted) == wanted) count++;
        return count;
    }

    private int CountOfTypes(PlayerId player, string typeParameter, ScriptArea? area)
    {
        // objectTypesFromParam, resolved once for the whole count: the list of that name if there is one, else the single template name.
        _objectLists.TryGetValue(typeParameter, out var list);
        var count = 0;
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
        {
            if (!Counts(entity)) continue;
            if (list is not null ? !list.Contains(entity.Template.Name) : !string.Equals(entity.Template.Name, typeParameter, StringComparison.Ordinal)) continue;
            if (area is null || area.Contains(entity.X, entity.Y)) count++;
        }
        return count;
    }

    private int CountOfKindInArea(PlayerId player, string kind, ScriptArea area)
    {
        var count = 0;
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
        {
            if (Counts(entity) && entity.Template.IsKindOf(kind) && area.Contains(entity.X, entity.Y)) count++;
        }
        return count;
    }

    /// <summary><c>evaluateSkirmishPlayerHasUnitsInArea</c>: some live, non-inert, non-projectile object of the player is inside the area (mines count here).</summary>
    private bool PlayerHasObjectsInArea(PlayerId player, ScriptArea area)
    {
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
            if (!entity.EffectivelyDead && !entity.DestroyRequested && (FactsOf(entity.Template) & (TemplateFacts.Inert | TemplateFacts.Projectile)) == 0 && area.Contains(entity.X, entity.Y))
                return true;
        return false;
    }

    /// <summary><c>evaluateSkirmishValueInArea</c>: the summed build cost of the player's live, non-inert objects inside the area.</summary>
    private long PlayerValueInArea(PlayerId player, ScriptArea area)
    {
        long total = 0;
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
            if (!entity.EffectivelyDead && !entity.DestroyRequested && (FactsOf(entity.Template) & TemplateFacts.Inert) == 0 && area.Contains(entity.X, entity.Y))
                total += entity.Template.BuildCost.Floor();
        return total;
    }
}
