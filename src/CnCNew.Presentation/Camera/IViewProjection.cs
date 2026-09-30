using System.Numerics;

namespace CnCNew.Presentation;

/// <summary>
/// Renderer-neutral view/projection contract used by picking and selection. World space is the
/// simulation's space (X east, Y north, Z up, 1 unit = 1 world unit, cell size 10). Screen space is
/// pixels with the origin at the top-left corner of the viewport, X right, Y down.
/// </summary>
public interface IViewProjection
{
    /// <summary>Viewport size in pixels.</summary>
    Vector2 ViewportSize { get; }

    /// <summary>World-to-clip matrix (row-vector convention of <see cref="System.Numerics"/>: clip = world * M).</summary>
    Matrix4x4 ViewProjection { get; }

    /// <summary>World-space ray through a screen point; <paramref name="direction"/> is normalized.</summary>
    void GetScreenRay(Vector2 screen, out Vector3 origin, out Vector3 direction);

    /// <summary>Intersects the screen ray with the terrain. False when the ray misses the map.</summary>
    bool TryScreenToGround(Vector2 screen, out Vector3 world);

    /// <summary>Projects a world point to screen pixels. False when the point is behind the camera.</summary>
    bool TryWorldToScreen(Vector3 world, out Vector2 screen);
}
