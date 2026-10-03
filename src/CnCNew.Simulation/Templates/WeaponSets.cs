using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Simulation.Templates;

/// <summary><c>WeaponSlotType</c> (WeaponSet.h): the three weapon slots of a weapon set.</summary>
public static class WeaponSlot
{
    public const int Primary = 0, Secondary = 1, Tertiary = 2, Count = 3;

    /// <summary>The "no explicit slot" value of <see cref="Commands.FireWeaponCommand.Slot"/>: the best weapon is chosen.</summary>
    public const int Best = -1;

    public static bool TryParse(string name, out int slot) => NameTablesPhase5.WeaponSlot.TryIndexOf(name.Trim(), out slot);

    public static string NameOf(int slot) => NameTablesPhase5.WeaponSlot.Canonical(slot);
}

/// <summary>
/// <c>CommandSourceMask</c> bits (AI.h <c>TheCommandSourceMaskNames</c>): which kinds of order may make a unit choose a weapon on its own
/// (<c>AutoChooseSources</c>). The bit position is the <c>CommandSourceType</c> value, so <see cref="FromPlayer"/> is bit 0.
/// </summary>
public static class CommandSourceBits
{
    public const uint FromPlayer = 1, FromScript = 2, FromAi = 4, FromDozer = 8, DefaultSwitchWeapon = 16;

    /// <summary><c>WeaponTemplateSet::clear</c>: "by default, allow autochoosing from any CommandSource".</summary>
    public const uint All = 0xFFFFFFFFu;

    public static NameTable Names { get; } = new("CommandSourceMask", "AI.h TheCommandSourceMaskNames",
        "FROM_PLAYER", "FROM_SCRIPT", "FROM_AI", "FROM_DOZER", "DEFAULT_SWITCH_WEAPON");
}

/// <summary>Who ordered a shot: the <c>CommandSourceType</c> the weapon choice reads (<c>WeaponSet::chooseBestWeaponForTarget</c>'s <c>cmdSource</c>).</summary>
public enum FireOrigin : byte
{
    /// <summary><c>CMD_FROM_PLAYER</c>: a human player's attack order.</summary>
    Player = 0,
    /// <summary><c>CMD_FROM_SCRIPT</c>: a mission script's order.</summary>
    Script = 1,
    /// <summary><c>CMD_FROM_AI</c>: a computer player's order or an idle unit's own acquisition.</summary>
    Ai = 2,
}

/// <summary>
/// One <c>WeaponSet</c> block of an object (<c>WeaponTemplateSet</c>): its conditions, the weapon of each slot (an index into the
/// <see cref="WeaponCatalog"/>, -1 when the slot is empty) and the per-slot choice data the reference keeps next to it.
/// </summary>
/// <param name="Index">Position among the object's weapon sets, in declaration order (the order <c>SparseMatchFinder</c> breaks ties by).</param>
/// <param name="Conditions">Bit mask over <see cref="NameTablesPhase5.WeaponSetCondition"/> (<c>WeaponSetFlags</c>); 0 for <c>Conditions = None</c>.</param>
/// <param name="Weapons">Weapon index per slot, <see cref="WeaponSlot.Count"/> entries.</param>
/// <param name="AutoChoose">Per slot, the <see cref="CommandSourceBits"/> that may auto-choose it (<c>AutoChooseSources</c>; default all).</param>
/// <param name="PreferredAgainst">Per slot, the <c>KindOf</c> names the weapon is always preferred against (<c>PreferredAgainst</c>; default none).</param>
/// <param name="ShareReloadTime"><c>ShareWeaponReloadTime</c>: all slots wait for the same shot delay or clip reload.</param>
/// <param name="LockSharedAcrossSets"><c>WeaponLockSharedAcrossSets</c>: a weapon lock survives a change to this set.</param>
public sealed record WeaponSetInfo(int Index, uint Conditions, ImmutableArray<int> Weapons, ImmutableArray<uint> AutoChoose,
    ImmutableArray<ImmutableArray<string>> PreferredAgainst, bool ShareReloadTime, bool LockSharedAcrossSets)
{
    /// <summary>How many slots hold a weapon.</summary>
    public int WeaponCount
    {
        get
        {
            var count = 0;
            foreach (var weapon in Weapons)
                if (weapon >= 0) count++;
            return count;
        }
    }

    public bool HasWeapon(int slot) => (uint)slot < WeaponSlot.Count && Weapons[slot] >= 0;

    /// <summary>The weapon of the primary slot, -1 when the set has none (a set of spell-like secondary weapons only).</summary>
    public int Primary => Weapons[WeaponSlot.Primary];
}

/// <summary>Reads the <c>WeaponSet</c> blocks of an object (<c>WeaponTemplateSet::parseWeaponTemplateSet</c>) and picks the set for a condition mask.</summary>
public static class WeaponSetResolver
{
    /// <summary>
    /// Every <c>WeaponSet</c> section of <paramref name="definition"/> in declaration order. Nothing falls back silently: an unknown slot, weapon,
    /// condition, command source or <c>KindOf</c> name is a <see cref="TemplateBuildException"/> (the reference's <c>INI</c> parser throws on all of them).
    /// Keys the reference does not know in a <c>WeaponSet</c> block are rejected as well.
    /// </summary>
    public static ImmutableArray<WeaponSetInfo> Resolve(ObjectDefinition definition, WeaponCatalog weapons)
    {
        var sets = ImmutableArray.CreateBuilder<WeaponSetInfo>();
        foreach (var section in definition.Sections.Where(value => value.Declaration.Equals("WeaponSet", StringComparison.OrdinalIgnoreCase)))
        {
            string Where() => $"Object '{definition.OriginalName}', WeaponSet #{sets.Count}";
            var conditions = 0u;
            var slotWeapons = new[] { -1, -1, -1 };
            var autoChoose = new[] { CommandSourceBits.All, CommandSourceBits.All, CommandSourceBits.All };
            var preferred = new ImmutableArray<string>[] { [], [], [] };
            var shared = false;
            var lockShared = false;
            foreach (var field in section.Fields)
            {
                var tokens = Tokens(field.Value.OriginalText);
                switch (field.Name.ToUpperInvariant())
                {
                    case "CONDITIONS":
                        conditions = ParseConditions(tokens, Where);
                        break;
                    case "WEAPON":
                    {
                        var slot = Slot(tokens, field.Name, Where);
                        if (tokens.Length != 2) throw new TemplateBuildException($"{Where()}: 'Weapon = {field.Value.OriginalText.Trim()}' needs a slot and a weapon name.");
                        if (tokens[1].Equals("None", StringComparison.OrdinalIgnoreCase)) slotWeapons[slot] = -1;
                        else if (weapons.TryGet(tokens[1], out var weapon)) slotWeapons[slot] = weapon.Index;
                        else throw new TemplateBuildException($"{Where()}: weapon '{tokens[1]}' is not a Weapon definition.");
                        break;
                    }
                    case "AUTOCHOOSESOURCES":
                    {
                        var slot = Slot(tokens, field.Name, Where);
                        autoChoose[slot] = ParseSources(tokens.AsSpan(1), Where);
                        break;
                    }
                    case "PREFERREDAGAINST":
                    {
                        var slot = Slot(tokens, field.Name, Where);
                        preferred[slot] = ParseKinds(tokens.AsSpan(1), Where);
                        break;
                    }
                    case "SHAREWEAPONRELOADTIME":
                        shared = Bool(field, Where);
                        break;
                    case "WEAPONLOCKSHAREDACROSSSETS":
                        lockShared = Bool(field, Where);
                        break;
                    default:
                        throw new TemplateBuildException($"{Where()}: unknown WeaponSet key '{field.Name}'.");
                }
            }
            sets.Add(new WeaponSetInfo(sets.Count, conditions, [..slotWeapons], [..autoChoose], [..preferred], shared, lockShared));
        }
        return sets.ToImmutable();
    }

    private static string[] Tokens(string text) => text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static int Slot(string[] tokens, string key, Func<string> where)
    {
        if (tokens.Length == 0 || !WeaponSlot.TryParse(tokens[0], out var slot))
            throw new TemplateBuildException($"{where()}: '{key}' does not start with a weapon slot name (PRIMARY, SECONDARY, TERTIARY).");
        return slot;
    }

    private static bool Bool(DefinitionField field, Func<string> where)
        => field.Value.Value is bool flag ? flag : throw new TemplateBuildException($"{where()}: '{field.Name}' must be Yes or No, not '{field.Value.OriginalText.Trim()}'.");

    /// <summary><c>WeaponSetFlags::parseFromINI</c> (<c>BitFlags::parse</c>): plain names or <c>NONE</c>; the relative <c>+</c>/<c>-</c> form is not used by any shipped weapon set and is refused.</summary>
    private static uint ParseConditions(string[] tokens, Func<string> where)
    {
        var mask = 0u;
        foreach (var token in tokens)
        {
            if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                if (mask != 0 || tokens.Length != 1) throw new TemplateBuildException($"{where()}: NONE may not be mixed with other conditions.");
                return 0;
            }
            if (token[0] is '+' or '-') throw new TemplateBuildException($"{where()}: relative condition '{token}' is not supported.");
            if (!NameTablesPhase5.WeaponSetCondition.TryIndexOf(token, out var bit)) throw new TemplateBuildException($"{where()}: unknown weapon set condition '{token}'.");
            mask |= 1u << bit;
        }
        return mask;
    }

    /// <summary><c>INI::parseBitString32</c> over <c>TheCommandSourceMaskNames</c>: the first normal token starts from an empty mask, <c>NONE</c> clears it.</summary>
    private static uint ParseSources(ReadOnlySpan<string> tokens, Func<string> where)
    {
        var mask = 0u;
        foreach (var token in tokens)
        {
            if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                if (mask != 0 || tokens.Length != 1) throw new TemplateBuildException($"{where()}: NONE may not be mixed with other command sources.");
                return 0;
            }
            if (token[0] is '+' or '-') throw new TemplateBuildException($"{where()}: relative command source '{token}' is not supported.");
            if (!CommandSourceBits.Names.TryIndexOf(token, out var bit)) throw new TemplateBuildException($"{where()}: unknown command source '{token}'.");
            mask |= 1u << bit;
        }
        return mask;
    }

    /// <summary><c>KindOfMaskType::parseFromINI</c>: plain names or <c>NONE</c> (an empty mask: preferred against nothing in particular).</summary>
    private static ImmutableArray<string> ParseKinds(ReadOnlySpan<string> tokens, Func<string> where)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var token in tokens)
        {
            if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                if (names.Count != 0 || tokens.Length != 1) throw new TemplateBuildException($"{where()}: NONE may not be mixed with other kinds.");
                return [];
            }
            if (token[0] is '+' or '-') throw new TemplateBuildException($"{where()}: relative KindOf '{token}' is not supported.");
            if (!NameTables.KindOf.TryIndexOf(token, out var index)) throw new TemplateBuildException($"{where()}: unknown KindOf '{token}'.");
            names.Add(NameTables.KindOf.Canonical(index));
        }
        return names.ToImmutable();
    }

    /// <summary>
    /// <c>SparseMatchFinder::findBestInfoSlow</c> for weapon sets: the set sharing the most condition bits with <paramref name="flags"/> wins; among equals the
    /// one with the fewest condition bits the object does not have; among those the first declared. A set whose conditions are only partly present still
    /// beats the unconditional one (the reference does not require every condition), and an empty list yields null.
    /// </summary>
    public static WeaponSetInfo? Find(ImmutableArray<WeaponSetInfo> sets, uint flags)
    {
        WeaponSetInfo? best = null;
        var bestMatch = 0;
        var bestExtra = 999;
        foreach (var set in sets)
        {
            var match = System.Numerics.BitOperations.PopCount(flags & set.Conditions);
            var extra = System.Numerics.BitOperations.PopCount(~flags & set.Conditions);
            if (match > bestMatch || (match >= bestMatch && extra < bestExtra))
            {
                best = set;
                bestMatch = match;
                bestExtra = extra;
            }
        }
        return best;
    }
}
