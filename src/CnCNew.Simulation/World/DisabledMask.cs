namespace CnCNew.Simulation.World;

/// <summary>
/// <c>DisabledMaskType</c> (<c>DisabledTypes.h</c>, the bit order of <c>DisabledTypes.cpp</c> <c>s_bitNameList</c>): the ways an object can be disabled. A disabled object runs only
/// the update modules whose <see cref="Modules.IUpdateModule.DisabledTypesToProcess"/> share a bit with its mask (<c>GameLogic::update</c>).
/// </summary>
[Flags]
public enum DisabledMask : ushort
{
    None = 0,
    Default = 1 << 0,
    Hacked = 1 << 1,
    Emp = 1 << 2,
    Held = 1 << 3,
    Paralyzed = 1 << 4,
    Unmanned = 1 << 5,
    Underpowered = 1 << 6,
    Freefall = 1 << 7,
    Awestruck = 1 << 8,
    Brainwashed = 1 << 9,
    Subdued = 1 << 10,
    ScriptDisabled = 1 << 11,
    ScriptUnderpowered = 1 << 12,

    /// <summary><c>DISABLEDMASK_ALL</c>.</summary>
    All = (1 << 13) - 1,
}

public static class DisabledMasks
{
    /// <summary>The names of <c>DisabledTypes.cpp</c> in bit order.</summary>
    public static readonly string[] Names =
    [
        "DEFAULT", "DISABLED_HACKED", "DISABLED_EMP", "DISABLED_HELD", "DISABLED_PARALYZED", "DISABLED_UNMANNED", "DISABLED_UNDERPOWERED", "DISABLED_FREEFALL",
        "DISABLED_AWESTRUCK", "DISABLED_BRAINWASHED", "DISABLED_SUBDUED", "DISABLED_SCRIPT_DISABLED", "DISABLED_SCRIPT_UNDERPOWERED",
    ];

    /// <summary>The mask of a set of names of the table; an unknown name is an error.</summary>
    public static DisabledMask FromNames(IEnumerable<string> names)
    {
        var mask = DisabledMask.None;
        foreach (var name in names)
        {
            var index = Array.FindIndex(Names, candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidDataException($"'{name}' is not a disabled type.");
            mask |= (DisabledMask)(1 << index);
        }
        return mask;
    }
}
