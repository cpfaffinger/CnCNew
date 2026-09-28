using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// A fixed-size bitset of sciences, one bit per <see cref="ScienceInfo.Index"/> (128 bits: the shipped 96 sciences fit
/// comfortably). Structurally identical to <see cref="UpgradeMask"/> (sciences and upgrades are separate reference
/// domains in the game data, so they get separate mask types rather than sharing index space).
/// </summary>
public readonly struct ScienceMask : IEquatable<ScienceMask>
{
    private readonly ulong _low;
    private readonly ulong _high;

    private ScienceMask(ulong low, ulong high) { _low = low; _high = high; }

    public static ScienceMask Empty => default;
    public static ScienceMask Bit(int index) => index < 64 ? new(1UL << index, 0) : new(0, 1UL << (index - 64));

    public bool Any() => _low != 0 || _high != 0;
    public bool Has(int index) => (this & Bit(index)).Any();
    public ScienceMask Set(int index) => this | Bit(index);
    public bool TestForAll(ScienceMask other) => (_low & other._low) == other._low && (_high & other._high) == other._high;

    public static ScienceMask operator |(ScienceMask a, ScienceMask b) => new(a._low | b._low, a._high | b._high);
    public static ScienceMask operator &(ScienceMask a, ScienceMask b) => new(a._low & b._low, a._high & b._high);
    public static bool operator ==(ScienceMask a, ScienceMask b) => a._low == b._low && a._high == b._high;
    public static bool operator !=(ScienceMask a, ScienceMask b) => !(a == b);
    public bool Equals(ScienceMask other) => this == other;
    public override bool Equals(object? obj) => obj is ScienceMask other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_low, _high);

    public void Describe(IStateWriter writer, string name)
    {
        writer.BeginObject(name);
        writer.UInt("lo", _low);
        writer.UInt("hi", _high);
        writer.EndObject();
    }

    public static ScienceMask Load(IStateReader reader, string name)
    {
        var state = reader.Object(name);
        return new ScienceMask(state.UInt("lo"), state.UInt("hi"));
    }
}

/// <summary>One <c>Science</c> definition with a stable bit index, its purchase cost and its prerequisite chain (names, resolved lazily against the catalog so declaration order does not matter).</summary>
public sealed record ScienceInfo(int Index, string Name, int PurchasePointCost, bool IsGrantable, ImmutableArray<string> PrerequisiteNames);

/// <summary>
/// <c>ScienceStore</c> equivalent: every <c>Science</c> definition with a stable ordinal bit index. Phase 5 scope: a
/// player either owns a science or does not; there is no per-object science and no science-granting-science recursion
/// beyond one prerequisite level deep per <c>PurchaseScienceCommand</c> (the reference itself only checks direct
/// prerequisites at purchase time too — <c>Player::canPurchaseScience</c> — deeper chains are satisfied because the
/// player must have purchased each prerequisite individually first).
/// </summary>
public sealed class ScienceCatalog
{
    private readonly ImmutableDictionary<string, ScienceInfo> _byName;

    private ScienceCatalog(ImmutableArray<ScienceInfo> sciences)
    {
        Sciences = sciences;
        _byName = sciences.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<ScienceInfo> Sciences { get; }
    public bool TryGet(string name, out ScienceInfo info) => _byName.TryGetValue(name, out info!);

    /// <summary>Resolves a set of science names into a mask. Unknown names are ignored (module/definition data may reference "None"/empty).</summary>
    public ScienceMask MaskOf(IEnumerable<string> names)
    {
        var mask = ScienceMask.Empty;
        foreach (var name in names)
            if (TryGet(name, out var info)) mask = mask.Set(info.Index);
        return mask;
    }

    /// <summary><c>Player::hasPrereqsForScience</c>: every direct prerequisite must already be owned.</summary>
    public bool PrerequisitesSatisfied(ScienceMask owned, ScienceInfo science) => owned.TestForAll(MaskOf(science.PrerequisiteNames));

    public static ScienceCatalog Build(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var index = 0;
        var sciences = ImmutableArray.CreateBuilder<ScienceInfo>();
        foreach (var definition in graph.Definitions.OfType<ScienceDefinition>().OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            if (index >= 128) throw new InvalidOperationException("The science catalog exceeds the 128-bit ScienceMask capacity.");
            DefinitionField? Field(string name) => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var cost = Field("SciencePurchasePointCost")?.Value.Value is long points ? checked((int)points) : 0;
            var grantable = Field("IsGrantable")?.Value.Value is not false; // ScienceTemplate ctor default: m_isGrantable = true.
            var prerequisites = Field("PrerequisiteSciences")?.Value.Value switch
            {
                DefinitionReference { IsNone: false } single => ImmutableArray.Create(single.Name),
                ImmutableArray<DefinitionReference> many => [..many.Where(value => !value.IsNone).Select(value => value.Name)],
                _ => ImmutableArray<string>.Empty,
            };
            sciences.Add(new ScienceInfo(index++, definition.OriginalName, cost, grantable, prerequisites));
        }
        return new ScienceCatalog(sciences.ToImmutable());
    }
}
