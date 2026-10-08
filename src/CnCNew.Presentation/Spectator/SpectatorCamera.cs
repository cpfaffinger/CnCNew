using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Spectator;

/// <summary>
/// What a spectator's camera may do that a player's may not (docs/SPECTATOR-MODE.md): zoom out past <c>MaxCameraHeight</c> up to a limit of its own
/// (<see cref="RtsCamera.SetMaxHeightAboveGround"/>) and jump to a view of the whole map. The original has no such camera; the numbers here are this client's own.
/// </summary>
public static class SpectatorCamera
{
    /// <summary>The search for <see cref="OverviewHeight"/> gives up above this height (a map of 1,000 x 1,000 cells fits far below it).</summary>
    public const float HighestOverview = 40_000f;

    /// <summary>The pitch of the overview in degrees: steep enough that the far edge of the map is not a thin strip.</summary>
    public const float OverviewPitchDegrees = 70f;

    /// <summary>One wheel notch changes the height by this share (a player's notch is a fixed 10 units, which would need hundreds of notches up here).</summary>
    public const float WheelZoomShare = 0.12f;

    /// <summary>The height after <paramref name="notches"/> wheel notches (positive: away from the user, zooms in), before the camera clamps it.</summary>
    public static float WheelZoom(float height, float notches) => height * MathF.Pow(1f + WheelZoomShare, -notches);

    /// <summary>
    /// The lowest height above ground from which the whole active map boundary is on screen when the camera looks at the map's centre with the given rotation and pitch
    /// (a margin of 3 % of the view on every side), found by bisection on a probe camera; <paramref name="limit"/> when the map does not fit below it.
    /// </summary>
    public static float OverviewHeight(RtsCameraSettings settings, TerrainHeightSampler terrain, Vector2 viewport, float angleRadians, float pitchRadians, float limit = HighestOverview)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(terrain);
        var probe = new RtsCamera(settings, terrain, viewport);
        var max = terrain.BoundaryMax(0);
        probe.SetMaxHeightAboveGround(limit);
        probe.SetAngle(angleRadians);
        probe.SetPitch(pitchRadians);
        var corners = new[] { Vector2.Zero, new Vector2(max.X, 0), max, new Vector2(0, max.Y) };

        bool Fits(float height)
        {
            probe.SetHeightAboveGround(height);
            probe.CenterOn(max / 2);
            probe.SettleZoom();
            var marginX = viewport.X * 0.03f;
            var marginY = viewport.Y * 0.03f;
            foreach (var corner in corners)
            {
                if (!probe.TryWorldToScreen(new Vector3(corner, probe.GroundLevel), out var screen)) return false;
                if (screen.X < marginX || screen.Y < marginY || screen.X > viewport.X - marginX || screen.Y > viewport.Y - marginY) return false;
            }
            return true;
        }

        var low = settings.MaxHeightAboveGround;
        if (Fits(low)) return low;
        if (!Fits(limit)) return limit;
        var high = limit;
        for (var step = 0; step < 24 && high - low > 1f; step++)
        {
            var middle = (low + high) / 2;
            if (Fits(middle)) high = middle; else low = middle;
        }
        return MathF.Ceiling(high);
    }
}
