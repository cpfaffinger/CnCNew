using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

/// <summary>Why a structure cannot be placed (<c>ConstructCommand</c> validation order of <c>SimulationWorld.CheckConstruct</c>).</summary>
public enum PlacementIssue
{
    None,
    NoGround,
    BuilderUnavailable,
    UnknownTemplate,
    NotInstantiable,
    CannotConstruct,
    MissingPrerequisite,
    InsufficientFunds,
    /// <summary><c>LBC_OBJECTS_IN_THE_WAY</c>: another footprint.</summary>
    BlockedFootprint,
    /// <summary>The spot was never explored by the player (<c>LBC_SHROUD</c>).</summary>
    Shroud,
    /// <summary><c>LBC_RESTRICTED_TERRAIN</c>: off the map, on water or a cliff, too close to the map's edge.</summary>
    RestrictedTerrain,
    /// <summary><c>LBC_NOT_FLAT_ENOUGH</c>.</summary>
    NotFlatEnough,
    /// <summary><c>LBC_TOO_CLOSE_TO_SUPPLIES</c>.</summary>
    TooCloseToSupplies,
    /// <summary><c>LBC_NO_CLEAR_PATH</c>: the builder cannot get there.</summary>
    NoClearPath,
}

/// <summary>
/// Read-only rule queries over the public simulation API. Where the simulation keeps a check private, the check is rebuilt here from the same
/// public data and named after the simulation method it mirrors; the terrain part of placement calls the simulation's own
/// <see cref="SimulationWorld.CheckBuildSite"/>.
/// </summary>
public static class SimulationQueries
{
    private static readonly string[] SupplyCarrierModules = ["SupplyTruckAIUpdate", "WorkerAIUpdate"];

    public static AIUpdateInterface? AiOf(Entity entity) => entity.Modules.OfType<AIUpdateInterface>().FirstOrDefault();

    public static ProductionUpdate? ProductionOf(Entity entity) => entity.Modules.OfType<ProductionUpdate>().FirstOrDefault();

    public static bool CanMove(Entity entity) => AiOf(entity) is not null;

    /// <summary><c>SimulationWorld.BuilderOf</c>: a dozer or worker AI on a finished object.</summary>
    public static bool IsBuilder(Entity entity) => AiOf(entity) is { CanBuild: true } && !entity.HasStatus(ObjectStatusBits.UnderConstruction);

    public static bool IsSupplyCarrier(Entity entity) => entity.Modules.Any(module => SupplyCarrierModules.Contains(module.ModuleName, StringComparer.Ordinal));

    public static bool HasModule(Entity entity, string moduleName) => entity.Modules.Any(module => module.ModuleName.Equals(moduleName, StringComparison.Ordinal));

    public static bool IsAlive(Entity entity) => !entity.DestroyRequested && !entity.EffectivelyDead;

    /// <summary><c>SimulationWorld.ActiveWeaponIndex</c>: the primary weapon of the weapon set the object's veterancy and upgrades select; -1 when that slot is empty.</summary>
    public static int ActiveWeapon(Entity entity) => SimulationWorld.ActiveWeaponIndex(entity);

    /// <summary>Whether any slot of the active weapon set holds a weapon (an object can be armed through its secondary slot alone).</summary>
    public static bool HasWeapon(Entity entity) => SimulationWorld.ActiveWeaponSet(entity) is { WeaponCount: > 0 };

    /// <summary>The longest range of the object's weapons (<see cref="SimulationWorld.MaxWeaponRange"/>): how close it must get to use any of them.</summary>
    public static Fixed WeaponRange(SimulationWorld world, Entity entity) => world.MaxWeaponRange(entity);

    /// <summary>The <c>FireWeaponCommand</c> preconditions: some weapon may be fired at the target now (<see cref="SimulationWorld.ChooseWeapon"/>, a player's order), the target alive with a body and visible.</summary>
    public static bool CanFireAt(SimulationWorld world, Entity source, Entity target, FireOrigin origin = FireOrigin.Player)
    {
        if (target.DestroyRequested || target.Body is null) return false;
        return world.ChooseWeaponInView(source, target, origin).CanFire && world.CanPlayerSee(source.Owner, target);
    }

    /// <summary>The unit can take an attack order: it has a weapon, or it is a container whose riders may fire (<c>Object::isAbleToAttack</c>, F-60).</summary>
    public static bool CanTakeAttackOrder(SimulationWorld world, Entity entity) => HasWeapon(entity) || world.PassesAttackToRiders(entity) || world.PassesAttackToSlaves(entity);

    /// <summary><see cref="InRange"/>, or for a container without a weapon of its own: one of its riders reaches the target from its fire point.</summary>
    public static bool InRangeOrRiders(SimulationWorld world, Entity source, Entity target)
        => HasWeapon(source) ? InRange(world, source, target)
            : world.PassesAttackToRiders(source) ? world.RidersCanAttack(source, target, out var inRange) && inRange
            : world.OrderedSlaves(source).Any(slave => HasWeapon(slave) && InRange(world, slave, target));

    public static bool InRange(SimulationWorld world, Entity source, Entity target)
    {
        var reach = WeaponRange(world, source);
        var dx = target.X - source.X;
        var dy = target.Y - source.Y;
        // A unit that cannot see its victim from here is not in range: it keeps approaching (outOfWeaponRangeObject, F-55).
        return reach > Fixed.Zero && SquaredReach.Of(reach).Contains(dx * dx + dy * dy) && !world.AttackViewBlocksFiring(source, target);
    }

    /// <summary><c>SimulationWorld.CanBuildTemplate</c>: a buildable structure of the builder's side (<c>Player::canBuild</c>).</summary>
    public static bool CanBuildTemplate(SimulationWorld world, PlayerId player, Entity builder, ObjectTemplate template)
    {
        if (!template.IsKindOf("STRUCTURE")) return false;
        if (template.Buildable.Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
        if (template.Buildable.Equals("Only_By_AI", StringComparison.OrdinalIgnoreCase) && !world.IsComputerPlayer(player)) return false;
        return template.Side.Length == 0 || builder.Template.Side.Length == 0 || template.Side.Equals(builder.Template.Side, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><c>SimulationWorld.PrerequisitesSatisfied</c> (<c>ProductionPrerequisite::isSatisfied</c>), including the <c>Ignore_Prerequisites</c> exemption.</summary>
    public static bool PrerequisitesSatisfied(SimulationWorld world, PlayerId player, ObjectTemplate template)
    {
        if (template.Buildable.Equals("Ignore_Prerequisites", StringComparison.OrdinalIgnoreCase)) return true;
        var sciences = world.PlayerSciences(player);
        foreach (var line in template.Prerequisites)
        {
            if (line.IsScience)
            {
                foreach (var name in line.Alternatives)
                    if (!world.Sciences.TryGet(name, out var science) || !sciences.Has(science.Index)) return false;
                continue;
            }
            var satisfied = world.Entities.Any(entity => entity.Owner == player && !entity.HasStatus(ObjectStatusBits.UnderConstruction)
                                                          && line.Alternatives.Contains(entity.Template.Name, StringComparer.OrdinalIgnoreCase));
            if (!satisfied) return false;
        }
        return true;
    }

    /// <summary>
    /// The lines of a template's <c>Prerequisites</c> block the player does not meet, in file order, by the same test as <see cref="PrerequisitesSatisfied"/> but without the
    /// <c>Ignore_Prerequisites</c> exemption (<c>ProductionPrerequisite::getRequiresList</c> does not look at it): what a description lists under "Requires".
    /// </summary>
    public static ImmutableArray<PrerequisiteLine> MissingPrerequisites(SimulationWorld world, PlayerId player, ObjectTemplate template)
    {
        var missing = ImmutableArray.CreateBuilder<PrerequisiteLine>();
        var sciences = world.PlayerSciences(player);
        foreach (var line in template.Prerequisites)
        {
            var satisfied = line.IsScience
                ? line.Alternatives.All(name => world.Sciences.TryGet(name, out var science) && sciences.Has(science.Index))
                : world.Entities.Any(entity => entity.Owner == player && !entity.HasStatus(ObjectStatusBits.UnderConstruction)
                                               && line.Alternatives.Contains(entity.Template.Name, StringComparer.OrdinalIgnoreCase));
            if (!satisfied) missing.Add(line);
        }
        return missing.ToImmutable();
    }

    /// <summary>
    /// The <c>ConstructCommand</c> validation of <c>SimulationWorld.CheckConstruct</c> as a dry run, in the same order, so the placement ghost
    /// predicts the simulation's verdict exactly.
    /// </summary>
    public static PlacementIssue CheckPlacement(SimulationWorld world, PlayerId player, Entity? builder, string templateName, Fixed x, Fixed y, Fixed angle)
    {
        if (builder is null || builder.DestroyRequested || builder.EffectivelyDead || builder.Owner != player || !IsBuilder(builder)) return PlacementIssue.BuilderUnavailable;
        if (!world.Templates.TryGet(templateName, out var template)) return PlacementIssue.UnknownTemplate;
        if (!template.IsInstantiable) return PlacementIssue.NotInstantiable;
        if (!CanBuildTemplate(world, player, builder, template)) return PlacementIssue.CannotConstruct;
        if (!PrerequisitesSatisfied(world, player, template)) return PlacementIssue.MissingPrerequisite;
        if (world.MoneyOf(player) < template.BuildCost) return PlacementIssue.InsufficientFunds;
        return world.CheckBuildSite(builder, template, x, y, angle) switch
        {
            LegalBuildCode.Ok => PlacementIssue.None,
            LegalBuildCode.RestrictedTerrain => PlacementIssue.RestrictedTerrain,
            LegalBuildCode.NotFlatEnough => PlacementIssue.NotFlatEnough,
            LegalBuildCode.TooCloseToSupplies => PlacementIssue.TooCloseToSupplies,
            LegalBuildCode.NoClearPath => PlacementIssue.NoClearPath,
            LegalBuildCode.Shroud => PlacementIssue.Shroud,
            _ => PlacementIssue.BlockedFootprint,
        };
    }

    /// <summary>
    /// World float to simulation fixed point: rounded to the nearest raw step, so a position that is exact in binary (every pixel of an
    /// orthographic test view) converts exactly.
    /// </summary>
    public static Fixed ToFixed(float value) => FixedConvert.FromSingle(value);

    public static Vector2 GroundOf(Entity entity) => new((float)entity.X.ToDecimal(), (float)entity.Y.ToDecimal());

    /// <summary>A module's saved state as a tree (<see cref="SimModule.Describe"/>): the only public view of queue and timer internals.</summary>
    public static StateNode Describe(SimModule module)
    {
        var writer = new StateTreeWriter();
        module.Describe(writer);
        return writer.Root;
    }
}

/// <summary>A captured <see cref="IStateWriter"/> object: scalar values by name and arrays of child objects.</summary>
public sealed class StateNode
{
    public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<StateNode>> Arrays { get; } = new(StringComparer.Ordinal);

    public long Int(string name, long fallback = 0) => Values.TryGetValue(name, out var value) && value is long number ? number : fallback;
    public bool Bool(string name) => Values.TryGetValue(name, out var value) && value is true;
    public IReadOnlyList<StateNode> Array(string name) => Arrays.TryGetValue(name, out var items) ? items : [];
}

internal sealed class StateTreeWriter : IStateWriter
{
    private readonly Stack<(StateNode Node, List<StateNode>? Array)> _stack = new();

    public StateTreeWriter() => _stack.Push((Root, null));

    public StateNode Root { get; } = new();

    public void BeginObject(string? name)
    {
        var node = new StateNode();
        var (parent, array) = _stack.Peek();
        if (array is not null && name is null) array.Add(node);
        else if (name is not null) parent.Values[name] = node;
        _stack.Push((node, null));
    }

    public void EndObject() => _stack.Pop();

    public void BeginArray(string name)
    {
        var (parent, _) = _stack.Peek();
        var list = new List<StateNode>();
        parent.Arrays[name] = list;
        _stack.Push((parent, list));
    }

    public void EndArray() => _stack.Pop();

    public void Bool(string name, bool value) => _stack.Peek().Node.Values[name] = value;
    public void Int(string name, long value) => _stack.Peek().Node.Values[name] = value;
    public void UInt(string name, ulong value) => _stack.Peek().Node.Values[name] = unchecked((long)value);
    public void Fixed(string name, Fixed value) => _stack.Peek().Node.Values[name] = value;
    public void Text(string name, string value) => _stack.Peek().Node.Values[name] = value;
}
