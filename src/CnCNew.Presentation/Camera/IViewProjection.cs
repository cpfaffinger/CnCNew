using System.Numerics;

namespace CnCNew.Presentation.Camera;

/// <summary>
/// The renderer-neutral view of the current camera: world space is the simulation's (x east, y north, z up, metres), screen space is
/// pixels with the origin in the top-left corner of the viewport.
/// </summary>
public interface IViewProjection
{
    Matrix4x4 ViewProjection { get; }

    Vector2 ViewportSize { get; }

    /// <summary>Intersects the view ray through <paramref name="screen"/> with the terrain; false when the ray misses the ground.</summary>
    bool TryScreenToGround(Vector2 screen, out Vector3 world);

    /// <summary>Projects a world point; false when it lies behind the camera or outside the clip volume.</summary>
    bool TryWorldToScreen(Vector3 world, out Vector2 screen);

    /// <summary>The world-space view ray through <paramref name="screen"/> (normalized direction).</summary>
    bool TryGetRay(Vector2 screen, out Vector3 origin, out Vector3 direction);
}
