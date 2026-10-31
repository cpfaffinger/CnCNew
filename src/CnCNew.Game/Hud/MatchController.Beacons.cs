using System.Collections.Immutable;
using CnCNew.Game.Live;
using CnCNew.Simulation.Ids;

namespace CnCNew.Game.Hud;

/// <summary>
/// The beacon of a match of a server (<c>ControlBar.wnd:ButtonPlaceBeacon</c>, <c>MSG_META_PLACE_BEACON</c>, <c>MSG_META_REMOVE_BEACON</c>): the button shows only in a game of several
/// humans (<c>GameLogic::isInMultiplayerGame</c>), and is grey at the limit. A click on the ground places the beacon; the delete key takes the player's own selected beacons away and
/// hides another's on this machine (<c>BeaconClientUpdate::hideBeacon</c>).
/// </summary>
public sealed partial class MatchController
{
    private const string BeaconButton = "ControlBar.wnd:ButtonPlaceBeacon";

    /// <summary><c>GameLogic::isInMultiplayerGame</c> and <c>isPlayerActive</c>: a player of a match of a server.</summary>
    public bool BeaconsEnabled { get; set; }

    private bool BelowBeaconLimit => Match.World.BeaconCount(Match.Human) < Match.World.Templates.MaxBeaconsPerPlayer;

    private void ArmBeacon()
    {
        if (!BeaconsEnabled || !BelowBeaconLimit) return;
        if (Input.CommandBar?.Catalog.TryGetButton("Command_PlaceBeacon", out var button) == true) Input.Commands.Activate(Match.World, button, []);
    }

    private void RemoveSelectedBeacons()
    {
        if (!BeaconsEnabled) return;
        var beacons = Input.Selection.Selected.Where(id => Match.World.FindEntity(id) is { } entity && Match.World.IsBeacon(entity)).ToImmutableArray();
        if (beacons.IsEmpty) return;
        Input.Commands.RemoveBeacons(beacons);
        var others = beacons.Where(id => Match.World.FindEntity(id)?.Owner != Match.Human).ToArray();
        foreach (var beacon in others) Match.HideBeacon(beacon);
        if (others.Length > 0) Input.Selection.Replace(Input.Selection.Selected.Except(others).ToArray());
    }

    private void RefreshBeaconButton()
    {
        var button = _tree.FindByName(BeaconButton);
        button?.SetHidden(!BeaconsEnabled);
        if (BeaconsEnabled) button?.SetEnabled(BelowBeaconLimit);
    }
}
