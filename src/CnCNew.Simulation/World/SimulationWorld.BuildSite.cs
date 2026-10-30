using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.World;

/// <summary><c>LegalBuildCode</c> (<c>BuildAssistant.h</c>): why a place is no building site.</summary>
public enum LegalBuildCode : byte
{
    Ok,
    RestrictedTerrain,
    NotFlatEnough,
    ObjectsInTheWay,
    Shroud,
}

public sealed partial class SimulationWorld
{
    /// <summary>
    /// <c>BuildAssistant::isLocationLegalToBuild</c> for a player's order (<c>TERRAIN_RESTRICTIONS | NO_OBJECT_OVERLAP | SHROUD_REVEALED</c>): the shroud comes first, "since returning other
    /// errors for shrouded areas could be used to game the system". A world without terrain has no site rules.
    /// </summary>
    public LegalBuildCode CheckBuildSite(PlayerId player, ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        if (Navigation is null) return LegalBuildCode.Ok;
        if (!Navigation.Terrain.IsOnMap(x, y)) return LegalBuildCode.RestrictedTerrain;
        return !IsPositionExploredBy(player, x, y) ? LegalBuildCode.Shroud : CheckBuildGround(template, x, y, angle);
    }

    /// <summary>The same without the shroud, for a computer player's search (<c>TERRAIN_RESTRICTIONS | NO_OBJECT_OVERLAP</c>, <c>AIPlayer::buildStructureNow</c>).</summary>
    public LegalBuildCode CheckBuildGround(ObjectTemplate template, Fixed x, Fixed y, Fixed angle)
    {
        if (Navigation is not { } navigation) return LegalBuildCode.Ok;
        if (!navigation.Terrain.IsOnMap(x, y)) return LegalBuildCode.RestrictedTerrain;
        if (!navigation.IsFootprintClear(template, x, y, angle)) return LegalBuildCode.ObjectsInTheWay;
        return BuildSiteTerrain.Check(navigation.Terrain, template, x, y, angle, _templates.BuildSite);
    }
}
