using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules;

/// <summary>
/// <c>UpgradeMuxData</c> (Module.cpp): the trigger/conflict/removal masks shared by every upgrade-activated module. Masks are
/// resolved once, at template-build time, against the world's <see cref="UpgradeCatalog"/> — the reference recomputes them
/// lazily on first use and then caches; our compiled definition graph is immutable, so resolving once up front is equivalent
/// and avoids the reference's clear-the-name-vector caching trick entirely.
/// </summary>
public sealed record UpgradeMuxData(UpgradeMask Activation, UpgradeMask Conflicting, ImmutableArray<string> RemovalNames, bool RequiresAllTriggers, ImmutableArray<string> TriggerNames)
{
    public static UpgradeMuxData Parse(ModuleConfiguration configuration, UpgradeCatalog catalog)
    {
        var triggers = ModuleConfigReaderNames(configuration, "TriggeredBy");
        var conflicts = ModuleConfigReaderNames(configuration, "ConflictsWith");
        var removals = ModuleConfigReaderNames(configuration, "RemovesUpgrades");
        return new UpgradeMuxData(catalog.MaskOf(triggers), catalog.MaskOf(conflicts), removals, ModuleConfigReaderBool(configuration, "RequiresAllTriggers"), triggers);
    }

    private static ImmutableArray<string> ModuleConfigReaderNames(ModuleConfiguration configuration, string name)
        => BuiltIn.ModuleConfigReader.Names(configuration, name);

    private static bool ModuleConfigReaderBool(ModuleConfiguration configuration, string name)
        => BuiltIn.ModuleConfigReader.Bool(configuration, name, false);

    public bool IsTriggeredBy(string upgradeName) => TriggerNames.Any(value => value.Equals(upgradeName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// <c>UpgradeMux</c> (UpgradeModule.cpp): the exact activation state machine, ported verbatim from the quoted reference source
/// (<c>wouldUpgrade</c>, <c>attemptUpgrade</c>, <c>testUpgradeConditions</c>, <c>resetUpgrade</c>, <c>giveSelfUpgrade</c>).
/// Composed into a module (not inherited: C# has no multiple inheritance) by holding one instance and forwarding calls.
/// </summary>
public sealed class UpgradeMuxState(UpgradeMuxData data)
{
    public bool Executed { get; private set; }

    public bool WouldUpgrade(UpgradeMask keyMask)
    {
        if (!data.Activation.Any() || !keyMask.Any() || Executed) return false;
        if (keyMask.TestForAny(data.Conflicting)) return false;
        return data.RequiresAllTriggers ? keyMask.TestForAll(data.Activation) : keyMask.TestForAny(data.Activation);
    }

    /// <summary>Unlike <see cref="WouldUpgrade"/>: ignores <see cref="Executed"/>, and an empty activation mask (conflicts-only) matches.</summary>
    public bool TestUpgradeConditions(UpgradeMask keyMask)
    {
        if (keyMask.Any() && keyMask.TestForAny(data.Conflicting)) return false;
        if (!data.Activation.Any()) return true;
        return data.RequiresAllTriggers ? keyMask.TestForAll(data.Activation) : keyMask.TestForAny(data.Activation);
    }

    /// <summary>The un-upgrade path: re-arms the module so a fresh <see cref="WouldUpgrade"/>/activation can fire again later.</summary>
    public bool ResetUpgrade(UpgradeMask keyMask)
    {
        if (!keyMask.TestForAny(data.Activation) || !Executed) return false;
        Executed = false;
        return true;
    }

    /// <summary>Order: FX (not modeled, presentation) → remove conflicting upgrades → the module's own effect → mark executed.</summary>
    public bool AttemptUpgrade(IWorldServices world, Entity entity, UpgradeMask keyMask, Action<IWorldServices> implementation)
    {
        if (!WouldUpgrade(keyMask)) return false;
        foreach (var name in data.RemovalNames) world.RemoveUpgradeByName(entity, name);
        implementation(world);
        Executed = true;
        return true;
    }

    public void Describe(IStateWriter writer) => writer.Bool("executed", Executed);
    public void Load(IStateReader reader) => Executed = reader.Bool("executed");
}
