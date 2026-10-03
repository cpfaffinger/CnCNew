using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.Presentation.Objects;

/// <summary>One model condition bit; <see cref="Id"/> is the stable index into <see cref="ModelConditions.Names"/>.</summary>
public readonly record struct ModelConditionFlag(int Id)
{
    public string Name => ModelConditions.Names[Id];
    public override string ToString() => Name;
}

/// <summary>
/// The retail model condition table (<c>ModelState.h ModelConditionFlagType</c>, names from <c>BitFlags.cpp
/// ModelConditionFlags::s_bitNameList</c>, Zero Hour, <c>ALLOW_SURRENDER</c> undefined). Ids are the table order and never change:
/// the reference stores them in save files. Lookup is case-insensitive like <c>INI::scanIndexList</c>.
/// </summary>
public static class ModelConditions
{
    public static ImmutableArray<string> Names { get; } =
    [
        "TOPPLED", "FRONTCRUSHED", "BACKCRUSHED", "DAMAGED", "REALLYDAMAGED", "RUBBLE", "SPECIAL_DAMAGED", "NIGHT", "SNOW",
        "PARACHUTING", "GARRISONED", "ENEMYNEAR", "WEAPONSET_VETERAN", "WEAPONSET_ELITE", "WEAPONSET_HERO",
        "WEAPONSET_CRATEUPGRADE_ONE", "WEAPONSET_CRATEUPGRADE_TWO", "WEAPONSET_PLAYER_UPGRADE",
        "DOOR_1_OPENING", "DOOR_1_CLOSING", "DOOR_1_WAITING_OPEN", "DOOR_1_WAITING_TO_CLOSE",
        "DOOR_2_OPENING", "DOOR_2_CLOSING", "DOOR_2_WAITING_OPEN", "DOOR_2_WAITING_TO_CLOSE",
        "DOOR_3_OPENING", "DOOR_3_CLOSING", "DOOR_3_WAITING_OPEN", "DOOR_3_WAITING_TO_CLOSE",
        "DOOR_4_OPENING", "DOOR_4_CLOSING", "DOOR_4_WAITING_OPEN", "DOOR_4_WAITING_TO_CLOSE",
        "ATTACKING", "PREATTACK_A", "FIRING_A", "BETWEEN_FIRING_SHOTS_A", "RELOADING_A",
        "PREATTACK_B", "FIRING_B", "BETWEEN_FIRING_SHOTS_B", "RELOADING_B",
        "PREATTACK_C", "FIRING_C", "BETWEEN_FIRING_SHOTS_C", "RELOADING_C",
        "TURRET_ROTATE", "POST_COLLAPSE", "MOVING", "DYING", "AWAITING_CONSTRUCTION", "PARTIALLY_CONSTRUCTED",
        "ACTIVELY_BEING_CONSTRUCTED", "PRONE", "FREEFALL", "ACTIVELY_CONSTRUCTING", "CONSTRUCTION_COMPLETE",
        "RADAR_EXTENDING", "RADAR_UPGRADED", "PANICKING", "AFLAME", "SMOLDERING", "BURNED",
        "DOCKING", "DOCKING_BEGINNING", "DOCKING_ACTIVE", "DOCKING_ENDING", "CARRYING", "FLOODED", "LOADED",
        "JETAFTERBURNER", "JETEXHAUST", "PACKING", "UNPACKING", "DEPLOYED", "OVER_WATER", "POWER_PLANT_UPGRADED",
        "CLIMBING", "SOLD", "RAPPELLING", "ARMED", "POWER_PLANT_UPGRADING", "SPECIAL_CHEERING",
        "CONTINUOUS_FIRE_SLOW", "CONTINUOUS_FIRE_MEAN", "CONTINUOUS_FIRE_FAST", "RAISING_FLAG", "CAPTURED",
        "EXPLODED_FLAILING", "EXPLODED_BOUNCING", "SPLATTED", "USING_WEAPON_A", "USING_WEAPON_B", "USING_WEAPON_C",
        "PREORDER", "CENTER_TO_LEFT", "LEFT_TO_CENTER", "CENTER_TO_RIGHT", "RIGHT_TO_CENTER",
        "RIDER1", "RIDER2", "RIDER3", "RIDER4", "RIDER5", "RIDER6", "RIDER7", "RIDER8",
        "STUNNED_FLAILING", "STUNNED", "SECOND_LIFE", "JAMMED", "ARMORSET_CRATEUPGRADE_ONE", "ARMORSET_CRATEUPGRADE_TWO",
        "USER_1", "USER_2", "DISGUISED",
    ];

    private static readonly ImmutableDictionary<string, int> Index =
        Names.Select((name, index) => (name, index)).ToImmutableDictionary(pair => pair.name, pair => pair.index, StringComparer.OrdinalIgnoreCase);

    public static int Count => Names.Length;

    public static bool TryParse(string name, out ModelConditionFlag flag)
    {
        if (Index.TryGetValue(name, out var id)) { flag = new ModelConditionFlag(id); return true; }
        flag = default;
        return false;
    }

    public static ModelConditionFlag Parse(string name)
        => TryParse(name, out var flag) ? flag : throw new ArgumentException($"'{name}' is not a model condition (ModelConditionFlags::s_bitNameList).", nameof(name));

    /// <summary>
    /// <c>BitFlags::parse</c> (<c>BitFlagsIO.h</c>): <c>NONE</c> clears and ends the list, <c>+X</c>/<c>-X</c> modify <paramref name="start"/>, a
    /// plain list replaces it (cleared at the first plain name). Mixing plain and +/- tokens and unknown names are
    /// reported through <paramref name="errors"/> (the reference throws); valid tokens are still applied.
    /// </summary>
    public static ModelConditionSet ParseList(IEnumerable<string> tokens, ModelConditionSet start, List<string> errors)
    {
        var set = start;
        var normal = false;
        var delta = false;
        foreach (var token in tokens)
        {
            if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                if (normal || delta) errors.Add("mixed-none");
                return ModelConditionSet.Empty;
            }
            var isDelta = token[0] is '+' or '-';
            if (isDelta ? normal : delta) errors.Add("mixed-plus-minus");
            var name = isDelta ? token[1..] : token;
            if (!TryParse(name, out var flag)) { errors.Add("unknown:" + name); continue; }
            if (isDelta) { delta = true; set = token[0] == '+' ? set.With(flag) : set.Without(flag); }
            else
            {
                if (!normal) set = ModelConditionSet.Empty;
                normal = true;
                set = set.With(flag);
            }
        }
        return set;
    }

    public static readonly ModelConditionFlag Toppled = Parse("TOPPLED");
    public static readonly ModelConditionFlag Damaged = Parse("DAMAGED");
    public static readonly ModelConditionFlag ReallyDamaged = Parse("REALLYDAMAGED");
    public static readonly ModelConditionFlag Rubble = Parse("RUBBLE");
    public static readonly ModelConditionFlag Night = Parse("NIGHT");
    public static readonly ModelConditionFlag Snow = Parse("SNOW");
    public static readonly ModelConditionFlag Parachuting = Parse("PARACHUTING");
    public static readonly ModelConditionFlag Garrisoned = Parse("GARRISONED");
    public static readonly ModelConditionFlag WeaponSetVeteran = Parse("WEAPONSET_VETERAN");
    public static readonly ModelConditionFlag WeaponSetElite = Parse("WEAPONSET_ELITE");
    public static readonly ModelConditionFlag WeaponSetHero = Parse("WEAPONSET_HERO");
    public static readonly ModelConditionFlag WeaponSetPlayerUpgrade = Parse("WEAPONSET_PLAYER_UPGRADE");
    public static readonly ModelConditionFlag Attacking = Parse("ATTACKING");
    public static readonly ModelConditionFlag FiringA = Parse("FIRING_A");
    public static readonly ModelConditionFlag BetweenFiringShotsA = Parse("BETWEEN_FIRING_SHOTS_A");
    public static readonly ModelConditionFlag ReloadingA = Parse("RELOADING_A");
    public static readonly ModelConditionFlag UsingWeaponA = Parse("USING_WEAPON_A");

    // Per weapon slot (A = primary, B = secondary, C = tertiary): WeaponSet::getModelConditionForWeaponSlot.
    public static readonly ModelConditionFlag[] Firing = [FiringA, Parse("FIRING_B"), Parse("FIRING_C")];
    public static readonly ModelConditionFlag[] BetweenFiringShots = [BetweenFiringShotsA, Parse("BETWEEN_FIRING_SHOTS_B"), Parse("BETWEEN_FIRING_SHOTS_C")];
    public static readonly ModelConditionFlag[] Reloading = [ReloadingA, Parse("RELOADING_B"), Parse("RELOADING_C")];
    public static readonly ModelConditionFlag[] UsingWeapon = [UsingWeaponA, Parse("USING_WEAPON_B"), Parse("USING_WEAPON_C")];
    public static readonly ModelConditionFlag Moving = Parse("MOVING");
    public static readonly ModelConditionFlag Dying = Parse("DYING");
    public static readonly ModelConditionFlag AwaitingConstruction = Parse("AWAITING_CONSTRUCTION");
    public static readonly ModelConditionFlag PartiallyConstructed = Parse("PARTIALLY_CONSTRUCTED");
    public static readonly ModelConditionFlag ActivelyBeingConstructed = Parse("ACTIVELY_BEING_CONSTRUCTED");
    public static readonly ModelConditionFlag ActivelyConstructing = Parse("ACTIVELY_CONSTRUCTING");
    public static readonly ModelConditionFlag RadarExtending = Parse("RADAR_EXTENDING");
    public static readonly ModelConditionFlag RadarUpgraded = Parse("RADAR_UPGRADED");
    public static readonly ModelConditionFlag Aflame = Parse("AFLAME");
    public static readonly ModelConditionFlag Burned = Parse("BURNED");
    public static readonly ModelConditionFlag Docking = Parse("DOCKING");
    public static readonly ModelConditionFlag Carrying = Parse("CARRYING");
    public static readonly ModelConditionFlag Loaded = Parse("LOADED");
    public static readonly ModelConditionFlag Deployed = Parse("DEPLOYED");
    public static readonly ModelConditionFlag PowerPlantUpgraded = Parse("POWER_PLANT_UPGRADED");
    public static readonly ModelConditionFlag PowerPlantUpgrading = Parse("POWER_PLANT_UPGRADING");
    public static readonly ModelConditionFlag Sold = Parse("SOLD");
    public static readonly ModelConditionFlag Disguised = Parse("DISGUISED");
    public static readonly ModelConditionFlag Rider1 = Parse("RIDER1");
}

/// <summary>An immutable set of model conditions (<c>ModelConditionFlags</c>, a <c>BitFlags&lt;MODELCONDITION_COUNT&gt;</c>).</summary>
public readonly struct ModelConditionSet : IEquatable<ModelConditionSet>
{
    private readonly ulong _low;
    private readonly ulong _high;

    static ModelConditionSet()
    {
        if (ModelConditions.Count > 128) throw new InvalidOperationException("ModelConditionSet holds at most 128 flags.");
    }

    private ModelConditionSet(ulong low, ulong high)
    {
        _low = low;
        _high = high;
    }

    public static ModelConditionSet Empty => default;

    public static ModelConditionSet Of(params ModelConditionFlag[] flags)
    {
        var set = Empty;
        foreach (var flag in flags) set = set.With(flag);
        return set;
    }

    /// <summary>Parses a blank-separated list with <see cref="ModelConditions.ParseList"/>; any error throws.</summary>
    public static ModelConditionSet Parse(string text)
    {
        var errors = new List<string>();
        var set = ModelConditions.ParseList(text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries), Empty, errors);
        return errors.Count == 0 ? set : throw new FormatException($"Invalid model condition list '{text}': {string.Join(", ", errors)}.");
    }

    public bool Any => (_low | _high) != 0;
    public int Count => BitOperations.PopCount(_low) + BitOperations.PopCount(_high);

    public bool Contains(ModelConditionFlag flag) => flag.Id < 64 ? (_low & (1UL << flag.Id)) != 0 : (_high & (1UL << (flag.Id - 64))) != 0;

    public ModelConditionSet With(ModelConditionFlag flag)
        => flag.Id < 64 ? new(_low | (1UL << flag.Id), _high) : new(_low, _high | (1UL << (flag.Id - 64)));

    public ModelConditionSet Without(ModelConditionFlag flag)
        => flag.Id < 64 ? new(_low & ~(1UL << flag.Id), _high) : new(_low, _high & ~(1UL << (flag.Id - 64)));

    public ModelConditionSet Union(ModelConditionSet other) => new(_low | other._low, _high | other._high);
    public ModelConditionSet Intersect(ModelConditionSet other) => new(_low & other._low, _high & other._high);
    public ModelConditionSet Except(ModelConditionSet other) => new(_low & ~other._low, _high & ~other._high);

    public bool IsSubsetOf(ModelConditionSet other) => (_low & ~other._low) == 0 && (_high & ~other._high) == 0;
    public bool Overlaps(ModelConditionSet other) => ((_low & other._low) | (_high & other._high)) != 0;

    /// <summary><c>BitFlags::countIntersection</c>: bits set in both.</summary>
    public int CountIntersection(ModelConditionSet other) => Intersect(other).Count;

    /// <summary><c>BitFlags::countInverseIntersection</c>: bits of <paramref name="other"/> that are not set here.</summary>
    public int CountInverseIntersection(ModelConditionSet other) => other.Except(this).Count;

    public IEnumerable<ModelConditionFlag> Flags()
    {
        for (var id = 0; id < ModelConditions.Count; id++)
            if (Contains(new ModelConditionFlag(id))) yield return new ModelConditionFlag(id);
    }

    public bool Equals(ModelConditionSet other) => _low == other._low && _high == other._high;
    public override bool Equals(object? obj) => obj is ModelConditionSet other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_low, _high);
    public static bool operator ==(ModelConditionSet a, ModelConditionSet b) => a.Equals(b);
    public static bool operator !=(ModelConditionSet a, ModelConditionSet b) => !a.Equals(b);

    /// <summary>Names in id order, blank separated; <c>NONE</c> for the empty set.</summary>
    public override string ToString() => Any ? string.Join(' ', Flags().Select(flag => flag.Name)) : "NONE";
}
