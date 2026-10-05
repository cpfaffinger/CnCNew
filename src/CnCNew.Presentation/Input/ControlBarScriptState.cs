using System.Collections.Immutable;

namespace CnCNew.Presentation.Input;

/// <summary><c>ScienceAvailabilityType</c> (<c>Player.h</c>): a mission script can hide a science from the purchase panel or leave it visible but disabled (<c>PLAYER_SCIENCE_AVAILABILITY</c>).</summary>
public enum ScienceAvailability { Available, Disabled, Hidden }

/// <summary>
/// What a mission script changed about the control bar, kept apart from the catalog (which is immutable game data) and read by <see cref="CommandBarModel"/>:
/// the per-slot overrides of <c>GameLogic::setControlBarOverride</c> (<c>COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE</c>, <c>COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT</c>) and the sciences the local player
/// may or may not buy (<c>Player::setScienceAvailability</c>). The state lives in the presentation layer: the simulation never sees a script's control bar, and a savegame does not keep it
/// (<c>docs/SCRIPT-ENGINE.md</c> §7a).
/// </summary>
public sealed class ControlBarScriptState
{
    private readonly Dictionary<(string Set, int Slot), CommandButtonInfo?> _slots = new();
    private readonly Dictionary<string, ScienceAvailability> _sciences = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Overrides in place (a removal is an override with no button).</summary>
    public int OverrideCount => _slots.Count;

    /// <summary>The button of <paramref name="set"/> slot <paramref name="slot"/> (0-based) when a script overrode it; <paramref name="button"/> is <see langword="null"/> for a removal.</summary>
    public bool TryGetOverride(string set, int slot, out CommandButtonInfo? button)
        => _slots.TryGetValue((set.ToUpperInvariant(), slot), out button);

    /// <summary><c>GameLogic::setControlBarOverride</c>: the slot shows <paramref name="button"/> from now on (nothing for <see langword="null"/>), for every object that uses the command set.</summary>
    public void Override(string set, int slot, CommandButtonInfo? button) => _slots[(set.ToUpperInvariant(), slot)] = button;

    public ScienceAvailability AvailabilityOf(string science) => _sciences.GetValueOrDefault(science);

    /// <summary><c>Player::setScienceAvailability</c>: <see cref="ScienceAvailability.Available"/> removes the science from the disabled and the hidden list.</summary>
    public void SetAvailability(string science, ScienceAvailability availability)
    {
        if (availability == ScienceAvailability.Available) _sciences.Remove(science);
        else _sciences[science] = availability;
    }

    /// <summary>The sciences a script disabled or hid, by name.</summary>
    public ImmutableSortedDictionary<string, ScienceAvailability> Sciences => _sciences.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>A mission ended or a new one begins: every script change is forgotten.</summary>
    public void Clear()
    {
        _slots.Clear();
        _sciences.Clear();
    }

    /// <summary>Parses <c>Available</c>/<c>Disabled</c>/<c>Hidden</c> (<c>ScienceAvailabilityNames</c>, case-insensitive); anything else is unknown, never a default.</summary>
    public static bool TryParseAvailability(string name, out ScienceAvailability availability)
        => Enum.TryParse(name, ignoreCase: true, out availability) && Enum.IsDefined(availability) && !int.TryParse(name, out _);
}
