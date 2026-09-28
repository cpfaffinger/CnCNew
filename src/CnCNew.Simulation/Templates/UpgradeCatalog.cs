using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Templates;

/// <summary>Player-scoped or object-scoped (<c>UPGRADE_TYPE_PLAYER</c>/<c>_OBJECT</c>, Upgrade.cpp <c>UpgradeType</c>).</summary>
public enum UpgradeScope { Player, Object }

/// <summary>
/// One <c>Upgrade</c> definition with a stable bit index. The reference gives every <c>UpgradeTemplate</c> a single-bit
/// <c>UpgradeMaskType</c> (<c>Player::addUpgrade</c>); we do the same with an ordinal index into <see cref="UpgradeMask"/>.
/// </summary>
public sealed record UpgradeInfo(int Index, string Name, UpgradeScope Scope, Fixed BuildCost, long BuildTicks);

/// <summary>
/// A fixed-size bitset of upgrades, one bit per <see cref="UpgradeInfo.Index"/> (128 bits: the shipped 82 upgrades fit
/// comfortably with headroom). Mirrors the reference's <c>UpgradeMaskType::testForAny/testForAll/set/clear</c>.
/// </summary>
public readonly struct UpgradeMask : IEquatable<UpgradeMask>
{
    private readonly ulong _low;
    private readonly ulong _high;

    private UpgradeMask(ulong low, ulong high) { _low = low; _high = high; }

    public static UpgradeMask Empty => default;
    public static UpgradeMask Bit(int index) => index < 64 ? new(1UL << index, 0) : new(0, 1UL << (index - 64));

    public bool Any() => _low != 0 || _high != 0;
    public bool Has(int index) => (this & Bit(index)).Any();
    public UpgradeMask Set(int index) => this | Bit(index);
    public UpgradeMask Clear(int index) => new(_low & ~Bit(index)._low, _high & ~Bit(index)._high);
    public bool TestForAny(UpgradeMask other) => (_low & other._low) != 0 || (_high & other._high) != 0;
    public bool TestForAll(UpgradeMask other) => (_low & other._low) == other._low && (_high & other._high) == other._high;

    public static UpgradeMask operator |(UpgradeMask a, UpgradeMask b) => new(a._low | b._low, a._high | b._high);
    public static UpgradeMask operator &(UpgradeMask a, UpgradeMask b) => new(a._low & b._low, a._high & b._high);
    public static bool operator ==(UpgradeMask a, UpgradeMask b) => a._low == b._low && a._high == b._high;
    public static bool operator !=(UpgradeMask a, UpgradeMask b) => !(a == b);
    public bool Equals(UpgradeMask other) => this == other;
    public override bool Equals(object? obj) => obj is UpgradeMask other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_low, _high);

    public void Describe(IStateWriter writer, string name)
    {
        writer.BeginObject(name);
        writer.UInt("lo", _low);
        writer.UInt("hi", _high);
        writer.EndObject();
    }

    public static UpgradeMask Load(IStateReader reader, string name)
    {
        var state = reader.Object(name);
        return new UpgradeMask(state.UInt("lo"), state.UInt("hi"));
    }
}

public sealed class UpgradeCatalog
{
    private readonly ImmutableDictionary<string, UpgradeInfo> _byName;

    private UpgradeCatalog(ImmutableArray<UpgradeInfo> upgrades)
    {
        Upgrades = upgrades;
        _byName = upgrades.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<UpgradeInfo> Upgrades { get; }
    public bool TryGet(string name, out UpgradeInfo info) => _byName.TryGetValue(name, out info!);

    /// <summary>Resolves a set of upgrade names into a mask. Unknown names are ignored (module data may reference "None"/empty).</summary>
    public UpgradeMask MaskOf(IEnumerable<string> names)
    {
        var mask = UpgradeMask.Empty;
        foreach (var name in names)
            if (TryGet(name, out var info)) mask = mask.Set(info.Index);
        return mask;
    }

    public static UpgradeCatalog Build(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var index = 0;
        var upgrades = ImmutableArray.CreateBuilder<UpgradeInfo>();
        foreach (var definition in graph.Definitions.OfType<UpgradeDefinition>().OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            if (index >= 128) throw new InvalidOperationException("The upgrade catalog exceeds the 128-bit UpgradeMask capacity.");
            var scope = definition.Fields.LastOrDefault(field => field.Name.Equals("Type", StringComparison.OrdinalIgnoreCase))?.Value.Value is EnumValue { Name: "OBJECT" }
                ? UpgradeScope.Object : UpgradeScope.Player;
            var cost = definition.Fields.LastOrDefault(field => field.Name.Equals("BuildCost", StringComparison.OrdinalIgnoreCase))?.Value.Value is Money money
                ? Fixed.FromInt(money.Value) : Fixed.Zero;
            var buildTicks = definition.Fields.LastOrDefault(field => field.Name.Equals("BuildTime", StringComparison.OrdinalIgnoreCase))?.Value.Value is decimal seconds
                ? SimulationTiming.SecondsToTicks(seconds) : 0;
            upgrades.Add(new UpgradeInfo(index++, definition.OriginalName, scope, cost, buildTicks));
        }
        return new UpgradeCatalog(upgrades.ToImmutable());
    }
}
