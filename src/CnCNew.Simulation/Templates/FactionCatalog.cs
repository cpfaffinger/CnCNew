using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// A <c>PlayerTemplate</c> resolved for match setup. <see cref="StartingBuilding"/>/<see cref="StartingUnits"/> are kept
/// as template <b>names</b>, not resolved object indices: spawning already goes through <c>SpawnCommand</c>, which takes
/// a name and validates it at submit time, so there is no need for <see cref="FactionCatalog"/> to depend on (or be
/// built in lockstep with) the object template list. A headless match applies a faction by crediting
/// <see cref="StartMoney"/>/<see cref="IntrinsicSciences"/> once at world construction (<c>SimulationWorld</c>,
/// mirroring <c>Player::Player()</c>) and issuing ordinary <c>SpawnCommand</c>s for the starting building/units.
/// </summary>
public sealed record FactionInfo(string Name, Fixed StartMoney, ScienceMask IntrinsicSciences, string StartingBuilding, ImmutableArray<string> StartingUnits, bool IsObserver, bool PlayableSide, string Side = "");

/// <summary>Every <c>PlayerTemplate</c> definition, keyed by name (<c>FactionAmerica</c>, <c>FactionChina</c>, the per-general variants, <c>FactionObserver</c>, <c>FactionCivilian</c>).</summary>
public sealed class FactionCatalog
{
    private readonly ImmutableDictionary<string, FactionInfo> _byName;

    private FactionCatalog(ImmutableArray<FactionInfo> factions)
    {
        Factions = factions;
        _byName = factions.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<FactionInfo> Factions { get; }
    public bool TryGet(string name, out FactionInfo info) => _byName.TryGetValue(name, out info!);

    public static FactionCatalog Build(CompiledDefinitionGraph graph, ScienceCatalog sciences)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sciences);
        var factions = ImmutableArray.CreateBuilder<FactionInfo>();
        foreach (var definition in graph.Definitions.OfType<FactionDefinition>().OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            DefinitionField? Field(string name) => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var startMoney = Field("StartMoney")?.Value.Value is Money money ? Fixed.FromInt(money.Value) : Fixed.Zero;
            var intrinsicNames = Field("IntrinsicSciences")?.Value.Value switch
            {
                DefinitionReference { IsNone: false } single => ImmutableArray.Create(single.Name),
                ImmutableArray<DefinitionReference> many => [..many.Where(value => !value.IsNone).Select(value => value.Name)],
                _ => ImmutableArray<string>.Empty,
            };
            var startingBuilding = Field("StartingBuilding")?.Value.Value is DefinitionReference { IsNone: false } building ? building.Name : string.Empty;
            var startingUnits = ImmutableArray.CreateBuilder<string>();
            for (var slot = 0; slot < 10; slot++)
                if (Field($"StartingUnit{slot}")?.Value.Value is DefinitionReference { IsNone: false } unit)
                    startingUnits.Add(unit.Name);
            var isObserver = Field("IsObserver")?.Value.Value is true;
            var playableSide = Field("PlayableSide")?.Value.Value is true;
            var side = Field("Side")?.Value.OriginalText.Trim() ?? string.Empty;
            factions.Add(new FactionInfo(definition.OriginalName, startMoney, sciences.MaskOf(intrinsicNames), startingBuilding, startingUnits.ToImmutable(), isObserver, playableSide, side));
        }
        return new FactionCatalog(factions.ToImmutable());
    }
}
