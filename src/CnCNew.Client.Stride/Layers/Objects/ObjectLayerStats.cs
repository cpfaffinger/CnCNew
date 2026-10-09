namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// Per-frame counters of <see cref="ObjectLayer"/>, refreshed at the end of every <see cref="ObjectLayer.Update"/> call. All counts are for
/// the most recently rendered frame except the cumulative ones, which are noted explicitly.
/// </summary>
internal sealed class ObjectLayerStats
{
    /// <summary>Drawables the presentation handed to this layer this frame (entities, ghosts and unclaimed scenery).</summary>
    public int Drawables;

    /// <summary>Instances actually submitted to the scene (in the frustum, model resolved).</summary>
    public int Drawn;

    /// <summary>Instances outside <c>match.Camera.Frustum</c>, not submitted.</summary>
    public int Culled;

    /// <summary>Pooled entities (rigid and skin) currently idle, available for reuse next frame.</summary>
    public int Pooled;

    /// <summary>Skinned instances re-skinned on the CPU this frame.</summary>
    public int SkinnedDrawn;

    /// <summary>Drawn parts this frame whose turret angle the presentation supplied (the turret bone was written into the pose).</summary>
    public int TurretsDriven;

    /// <summary>Drawn parts this frame with tire bones turned by their distance rolled (W3DTruckDraw).</summary>
    public int WheelsDriven;

    /// <summary>Muzzle flash sub-objects of drawn parts this frame: hidden because the weapon did not just fire / shown by a shot.</summary>
    public int MuzzleFlashesHidden;
    public int MuzzleFlashesShown;
    /// <summary>Headlight sub-objects hidden in the last frame because it is not night (W3DModelDraw::hideAllHeadlights).</summary>
    public int HeadlightsHidden;

    /// <summary>Model parts drawn with a flash tint this frame (a building that is being captured) / since the start (cumulative: a flash lasts five ticks).</summary>
    public int FlashTinted;

    /// <summary>Parts drawn translucent this frame (the building preview of a placement; one per draw module of it).</summary>
    public int Translucent;
    public long FlashTintedTotal;

    /// <summary>Triangles submitted this frame (sum of visible mesh draw counts / 3).</summary>
    public long Triangles;

    /// <summary>Distinct W3D models successfully loaded so far (cumulative).</summary>
    public int ModelsLoaded;

    /// <summary>Distinct model names that failed to load so far (cumulative; each logged once, drawn as a placeholder box).</summary>
    public int ModelsMissing;

    /// <summary>Distinct W3D texture references this layer could not resolve so far (cumulative; the texture cache logs each once).</summary>
    public int TexturesMissing;

    /// <summary>Distinct <c>HIERARCHY.ANIMATION</c> names that failed to resolve so far (cumulative; each logged once).</summary>
    public int MissingAnimations;

    /// <summary>Instances currently drawn with the placeholder box because their model is missing.</summary>
    public int Placeholders;

    /// <summary>Meshes with a disposed or too small buffer (checked once when the smoke-test summary is written; must be zero).</summary>
    public int GpuResourceProblems;

    /// <summary>CPU time spent in <see cref="ObjectLayer.Update"/> this frame.</summary>
    public double FrameMilliseconds;

    /// <summary>Bytes allocated on the calling thread during this frame's <see cref="ObjectLayer.Update"/> (managed heap; should be near zero once pools are warm).</summary>
    public long FrameAllocatedBytes;

    /// <summary>Running mean of <see cref="FrameMilliseconds"/> over every frame measured so far.</summary>
    public double AverageFrameMilliseconds { get; private set; }

    private long _frameCount;
    private double _totalMilliseconds;

    public void RecordFrame(double milliseconds, long allocatedBytes)
    {
        FrameMilliseconds = milliseconds;
        FrameAllocatedBytes = allocatedBytes;
        _frameCount++;
        _totalMilliseconds += milliseconds;
        AverageFrameMilliseconds = _totalMilliseconds / _frameCount;
    }

    public override string ToString() =>
        $"drawables={Drawables} drawn={Drawn} culled={Culled} pooled={Pooled} skinned={SkinnedDrawn} turrets={TurretsDriven} wheels={WheelsDriven} flashes={MuzzleFlashesShown}/{MuzzleFlashesShown + MuzzleFlashesHidden} headlightsHidden={HeadlightsHidden} tinted={FlashTinted} tintedTotal={FlashTintedTotal} translucent={Translucent} triangles={Triangles} " +
        $"models={ModelsLoaded} missingModels={ModelsMissing} missingTextures={TexturesMissing} missingAnimations={MissingAnimations} placeholders={Placeholders} gpuProblems={GpuResourceProblems} " +
        $"frameMs={FrameMilliseconds:F3} avgMs={AverageFrameMilliseconds:F3} allocB={FrameAllocatedBytes}";
}
