using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Templates;

/// <summary>One <c>CrateObject = Name Weight</c> entry: a weighted alternative inside a <c>CrateData</c> block.</summary>
public sealed record CrateEntry(string ObjectName, Fixed Weight);

/// <summary>
/// One <c>CrateData</c> definition (<c>CrateSystem.cpp CrateTemplate</c>). <see cref="VeterancyLevel"/> is -1 when the
/// victim's rank is not tested (<c>LEVEL_INVALID</c>); <see cref="KilledByType"/> empty and <see cref="KillerScience"/>
/// empty mean the respective condition is not tested.
/// </summary>
public sealed record CrateInfo(string Name, Fixed CreationChance, int VeterancyLevel, ImmutableArray<string> KilledByType,
    string KillerScience, bool OwnedByMaker, ImmutableArray<CrateEntry> Objects);

/// <summary><c>CrateSystem</c> equivalent: every <c>CrateData</c> definition, addressed by name.</summary>
public sealed class CrateCatalog
{
    private readonly ImmutableDictionary<string, CrateInfo> _byName;

    private CrateCatalog(ImmutableArray<CrateInfo> crates)
    {
        Crates = crates;
        _byName = crates.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<CrateInfo> Crates { get; }
    public bool TryGet(string name, out CrateInfo info) => _byName.TryGetValue(name, out info!);

    public static CrateCatalog Build(CompiledDefinitionGraph graph, ScienceCatalog sciences)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(sciences);
        var crates = ImmutableArray.CreateBuilder<CrateInfo>();
        foreach (var definition in graph.Definitions.OfType<CrateDefinition>().OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            DefinitionField? Field(string name) => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            // CrateTemplate::CrateTemplate: m_creationChance = 0, m_veterancyLevel = LEVEL_INVALID, no kind-of, no science, not owned by the maker.
            var chance = Field("CreationChance")?.Value.Value is decimal value ? Fixed.FromDecimal(value) : Fixed.Zero;
            var veterancy = Field("VeterancyLevel")?.Value.Value is EnumValue rank && NameTables.Veterancy.TryIndexOf(rank.Name, out var index) ? index : -1;
            var killedBy = Field("KilledByType")?.Value.Value is FlagsValue flags ? flags.Names : [];
            var science = Field("KillerScience")?.Value.Value is DefinitionReference { IsNone: false } reference ? reference.Name : string.Empty;
            if (science.Length > 0 && !sciences.TryGet(science, out _))
                throw new TemplateBuildException($"Crate '{definition.OriginalName}': KillerScience '{science}' is not a Science definition.");
            var ownedByMaker = Field("OwnedByMaker")?.Value.Value is true;
            var objects = ImmutableArray.CreateBuilder<CrateEntry>();
            foreach (var field in definition.Fields.Where(field => field.Name.Equals("CrateObject", StringComparison.OrdinalIgnoreCase)))
            {
                // parseCrateCreationEntry: "<object> <chance>", exactly two tokens.
                var tokens = field.Value.OriginalText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length != 2 || !decimal.TryParse(tokens[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var weight))
                    throw new TemplateBuildException($"Crate '{definition.OriginalName}': CrateObject '{field.Value.OriginalText}' is not '<object> <chance>'.");
                objects.Add(new CrateEntry(tokens[0], Fixed.FromDecimal(weight)));
            }
            crates.Add(new CrateInfo(definition.OriginalName, chance, veterancy, killedBy, science, ownedByMaker, objects.ToImmutable()));
        }
        return new CrateCatalog(crates.ToImmutable());
    }
}
