using System.Numerics;
using CnCNew.Audio.Catalog;

namespace CnCNew.Audio.Mixing;

/// <summary>
/// Listener placement and zoom volume of <c>AudioManager::update</c> (<c>GameAudio.cpp</c>): the microphone sits on the
/// line from the ground look-at point towards the camera, at <c>MicrophoneDesiredHeightAboveTerrain</c> but never more
/// than <c>MicrophoneMaxPercentageBetweenGroundAndCamera</c> of the way; 3D sounds get louder as the camera zooms in.
/// </summary>
public static class Microphone
{
    /// <param name="lookAt">The view position (<c>View::getPosition</c>); like the reference, its z enters the ground-to-camera vector while the microphone base uses <paramref name="terrainHeight"/>.</param>
    /// <param name="terrainHeight">Terrain height at <paramref name="lookAt"/>.</param>
    /// <param name="camera">Camera position.</param>
    /// <returns>The listener position and the factor for the 3D sound volume (<c>set3DVolumeAdjustment</c>).</returns>
    public static (Vector3 Position, float ZoomVolume) Place(Vector3 lookAt, float terrainHeight, Vector3 camera, AudioSettingsInfo settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var desiredHeight = settings.MicrophoneDesiredHeightAboveTerrain;
        var maxPercentage = settings.MicrophoneMaxPercentageBetweenGroundAndCamera;
        var groundToCamera = camera - lookAt;
        var scale = camera.Z <= desiredHeight || groundToCamera.Z <= 0f
            ? maxPercentage
            : Math.Min(maxPercentage, desiredHeight / groundToCamera.Z);
        var microphone = new Vector3(lookAt.X, lookAt.Y, terrainHeight) + groundToCamera * scale;

        var boost = settings.ZoomSoundVolumePercentageAmount;
        var zoomVolume = 1f - boost;
        if (boost > 0f)
        {
            var distance = Vector3.Distance(camera, microphone);
            if (distance < settings.ZoomMinDistance)
                zoomVolume = 1f;
            else if (distance < settings.ZoomMaxDistance)
                zoomVolume = 1f - (distance - settings.ZoomMinDistance) / (settings.ZoomMaxDistance - settings.ZoomMinDistance) * boost;
        }
        return (microphone, zoomVolume);
    }
}
