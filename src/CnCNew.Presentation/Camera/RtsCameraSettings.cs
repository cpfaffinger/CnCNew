using CnCNew.Data.Definitions;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Camera;

/// <summary>
/// Camera parameters. <see cref="FromDefinitions"/> reads every INI-backed value strictly from <c>GameData.ini</c>
/// (<c>GlobalData.cpp TheGlobalDataFieldParseTable</c>); the remaining constants are engine constants cited at each property.
/// Heights and distances are world units, angles degrees, speeds per 30 Hz frame as in the reference.
/// </summary>
public sealed record RtsCameraSettings(
    float PitchDegrees,
    float YawDegrees,
    float CameraHeight,
    float MaxHeightAboveGround,
    float MinHeightAboveGround,
    float AdjustSpeed,
    float ScrollAmountCutoff,
    bool EnforceMaxCameraHeight,
    float HorizontalScrollSpeedFactor,
    float VerticalScrollSpeedFactor,
    float KeyboardScrollFactor,
    float KeyboardRotateSpeed,
    float MaxShakeIntensity,
    float MaxShakeRange)
{
    /// <summary><c>View::View</c>: <c>m_FOV = 50°</c>, horizontal (<c>Set_View_Plane(hfov, -1)</c> derives the vertical one from the aspect).</summary>
    public float HorizontalFovDegrees { get; init; } = 50f;

    /// <summary><c>W3DView::setCameraTransform</c>: near plane <c>MAP_XY_FACTOR</c> = 10.</summary>
    public float NearPlane { get; init; } = 10f;

    /// <summary><c>W3DView::setCameraTransform</c>: far plane 1200 (the renderer extends it to cover the whole height field).</summary>
    public float MinFarPlane { get; init; } = 1200f;

    /// <summary><c>W3DView::initHeightForMap</c>: the fixed ground level of the camera never exceeds 120.</summary>
    public float MaxGroundLevel { get; init; } = 120f;

    /// <summary><c>TERRAIN_SAMPLE_SIZE</c> = 4 cells: <c>getHeightAroundPos</c> samples the pivot and four diagonal points this far away.</summary>
    public float TerrainSampleSize { get; init; } = 40f;

    /// <summary><c>View::zoomIn/zoomOut</c>: one mouse-wheel notch changes the desired height above ground by 10.</summary>
    public float WheelZoomStep { get; init; } = 10f;

    /// <summary><c>LookAtTranslator</c>: <c>SCROLL_AMT</c> = 100 per frame for keys and screen edges (retail value).</summary>
    public float ScrollAmount { get; init; } = 100f;

    /// <summary><c>W3DView::scrollBy</c>: <c>SCROLL_RESOLUTION</c> = 250 pixels per scroll unit.</summary>
    public float ScrollResolution { get; init; } = 250f;

    /// <summary><c>LookAtTranslator</c>: middle-mouse rotation, 0.01 radians per pixel.</summary>
    public float MouseRotateRadiansPerPixel { get; init; } = 0.01f;

    /// <summary><c>LookAtTranslator</c>: screen-edge scrolling starts within 3 pixels of the edge.</summary>
    public int EdgeScrollPixels { get; init; } = 3;

    /// <summary><c>View::setPitch</c> clamp.</summary>
    public float MinPitchDegrees { get; init; } = 0.1f;
    public float MaxPitchDegrees { get; init; } = 89.9f;

    public static RtsCameraSettings FromDefinitions(CompiledDefinitionGraph graph)
    {
        var gameData = WorldDefinitionReader.GameData(graph);
        float Real(string key) => WorldDefinitionReader.Real(gameData, key);
        return new RtsCameraSettings(
            Real("CameraPitch"),
            Real("CameraYaw"),
            Real("CameraHeight"),
            Real("MaxCameraHeight"),
            Real("MinCameraHeight"),
            Real("CameraAdjustSpeed"),
            Real("ScrollAmountCutoff"),
            WorldDefinitionReader.Boolean(gameData, "EnforceMaxCameraHeight"),
            Real("HorizontalScrollSpeedFactor"),
            Real("VerticalScrollSpeedFactor"),
            Real("KeyboardScrollSpeedFactor"),
            Real("KeyboardCameraRotateSpeed"),
            Real("MaxShakeIntensity"),
            Real("MaxShakeRange"));
    }
}

/// <summary>The <c>CameraShakeType</c> intensities of <c>GameData.ini</c> (<c>ShakeSubtleIntensity</c> ... <c>ShakeCineInsaneIntensity</c>).</summary>
public sealed record CameraShakeIntensities(float Subtle, float Normal, float Strong, float Severe, float CineExtreme, float CineInsane)
{
    public static CameraShakeIntensities FromDefinitions(CompiledDefinitionGraph graph)
    {
        var gameData = WorldDefinitionReader.GameData(graph);
        float Real(string key) => WorldDefinitionReader.Real(gameData, key);
        return new CameraShakeIntensities(Real("ShakeSubtleIntensity"), Real("ShakeNormalIntensity"), Real("ShakeStrongIntensity"),
            Real("ShakeSevereIntensity"), Real("ShakeCineExtremeIntensity"), Real("ShakeCineInsaneIntensity"));
    }

    /// <summary>Intensity of a <c>ViewShake</c> FX type name (SUBTLE, NORMAL, STRONG, SEVERE, CINE_EXTREME, CINE_INSANE).</summary>
    public float For(string shakeType) => shakeType.ToUpperInvariant() switch
    {
        "SUBTLE" => Subtle,
        "NORMAL" => Normal,
        "STRONG" => Strong,
        "SEVERE" => Severe,
        "CINE_EXTREME" => CineExtreme,
        "CINE_INSANE" => CineInsane,
        _ => throw new ArgumentException($"'{shakeType}' is not a camera shake type.", nameof(shakeType)),
    };
}
