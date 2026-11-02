using System.Numerics;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Presentation.Camera;

/// <summary><c>TerrainLogic::pickBridge</c>: where a ray from the camera meets a deck. The plane goes through three corners of the deck, and the point counts when it lies on the bridge.</summary>
public static class BridgePick
{
    /// <summary>The first bridge of the list the ray hits, as the original takes it.</summary>
    public static Vector3? Hit(IReadOnlyList<TerrainBridge> bridges, Vector3 origin, Vector3 direction)
    {
        foreach (var bridge in bridges)
        {
            var (left, right, far) = (Corner(bridge.FromLeft), Corner(bridge.FromRight), Corner(bridge.ToLeft));
            var normal = Vector3.Cross(right - left, far - left);
            var facing = Vector3.Dot(normal, direction);
            if (facing == 0f) continue;
            var distance = Vector3.Dot(normal, left - origin) / facing;
            if (distance < 0f) continue;
            var point = origin + direction * distance;
            if (bridge.IsPointOnBridge(FixedConvert.FromSingle(point.X), FixedConvert.FromSingle(point.Y))) return point;
        }
        return null;
    }

    private static Vector3 Corner(DeckPoint point) => new(FixedConvert.ToSingle(point.X), FixedConvert.ToSingle(point.Y), FixedConvert.ToSingle(point.Z));
}
