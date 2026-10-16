using System.Collections.Immutable;
using System.Numerics;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// A tracer line (<c>W3DTracerDraw</c>): from <see cref="Head"/> along <see cref="Direction"/> for <see cref="Length"/>, <see cref="Width"/>
/// wide, fading linearly to 0 until <see cref="ExpireFrame"/>, moving <see cref="Speed"/> per frame.
/// </summary>
public readonly record struct TracerInstance(Vector3 Head, Vector3 Direction, float Length, float Width, Vector3 Color, float Opacity, float Speed, long ExpireFrame);

/// <summary>A <c>RayEffect</c> request (<c>GameClient::createRayEffectByTemplate</c>): the beam drawable template and its end points.</summary>
public readonly record struct RayEffectRequest(string TemplateName, Vector3 Start, Vector3 End);

/// <summary>
/// A dynamic point light (<c>W3DDisplay::createLightPulse</c> + <c>W3DDynamicLight::On_Frame_Update</c>): full color within
/// <see cref="InnerRadius"/> (always 1), attenuated to 0 at <see cref="OuterRadius"/>; <see cref="Factor"/> is the current fade factor.
/// </summary>
public readonly record struct LightPulseInstance(Vector3 Position, Vector3 Color, float InnerRadius, float OuterRadius, float Factor);

/// <summary>A <c>ViewShake</c> request at an epicenter; the camera turns it into motion with <see cref="CameraShake"/>.</summary>
public readonly record struct ViewShakeRequest(ViewShakeType Type, Vector3 Epicenter);

public enum DecalKind { Scorch, Fx }

/// <summary>
/// A ground decal. Scorches use the 3x3 atlas <c>EXScorch01.tga</c> (<c>BaseHeightMapRenderObjClass::updateScorches</c>: cells spaced
/// 1.5 apart in a 4-unit texture); <see cref="UvRect"/> is (u0, v0, u1, v1). <see cref="ExpireFrame"/> null means permanent.
/// </summary>
public readonly record struct DecalInstance(DecalKind Kind, string Texture, Vector3 Position, float Radius, float Rotation, Vector4 Color, Vector4 UvRect,
    long CreatedFrame, long? ExpireFrame, int FadeFrames);

/// <summary>
/// A capped ring of decals. Scorches follow <c>BaseHeightMapRenderObjClass::addScorch</c>: capacity 500 (<c>MAX_SCORCH_MARKS</c>), the
/// oldest is dropped when full, and a mark of the same type within a quarter radius in X, Y and radius is a duplicate.
/// </summary>
public sealed class DecalBuffer(int capacity = 500)
{
    public const string ScorchTexture = "EXScorch01.tga";
    private readonly List<DecalInstance> _decals = [];

    public int Capacity { get; } = capacity;
    public int Count => _decals.Count;
    public long Dropped { get; private set; }
    public long Duplicates { get; private set; }

    public bool AddScorch(Vector3 position, float radius, int scorchType, long frame)
    {
        var limit = radius / 4f;
        var uv = ScorchUv(scorchType);
        foreach (var decal in _decals)
            if (decal.Kind == DecalKind.Scorch && MathF.Abs(position.X - decal.Position.X) < limit && MathF.Abs(position.Y - decal.Position.Y) < limit
                && MathF.Abs(radius - decal.Radius) < limit && decal.UvRect == uv)
            {
                Duplicates++;
                return false;
            }
        Add(new DecalInstance(DecalKind.Scorch, ScorchTexture, position, radius, 0f, Vector4.One, uv, frame, null, 0));
        return true;
    }

    public void Add(DecalInstance decal)
    {
        if (_decals.Count >= Capacity)
        {
            _decals.RemoveAt(0);
            Dropped++;
        }
        _decals.Add(decal);
    }

    public void Expire(long frame) => _decals.RemoveAll(decal => decal.ExpireFrame is { } end && frame >= end);

    public ImmutableArray<DecalInstance> Snapshot() => [.._decals];

    /// <summary>The atlas cell of a scorch type (out-of-range types use cell 0 like the reference).</summary>
    public static Vector4 ScorchUv(int scorchType)
    {
        const int perRow = 3;
        if (scorchType is < 0 or >= 9) scorchType = 0;
        var u = (scorchType % perRow) * 1.5f;
        var v = (scorchType / perRow) * 1.5f;
        return new Vector4(u / (perRow + 1), v / (perRow + 1), (u + 1f) / (perRow + 1), (v + 1f) / (perRow + 1));
    }
}

/// <summary>
/// The camera shake of <c>W3DView::shake</c>/<c>W3DView::updateView</c>: each request adds the type's intensity scaled by
/// <c>1 - distance/MaxShakeRange</c> (2D distance from the view position; nothing beyond the range) in a random direction; each frame
/// the offset is intensity x direction, the intensity decays by 0.75 and the direction flips; below 0.01 it stops. Like the
/// reference, an intensity above <c>MaxShakeIntensity</c> is reset to 3.0 (not clamped to the maximum).
/// </summary>
public sealed class CameraShake(FxSettings settings, FxRandom random)
{
    private float _cos, _sin;

    public float Intensity { get; private set; }
    public Vector2 Offset { get; private set; }

    /// <summary>The intensity a request contributes at <paramref name="viewPosition"/>, before the random direction.</summary>
    public static float FalloffIntensity(FxSettings settings, ViewShakeRequest request, Vector2 viewPosition)
    {
        var distance = Vector2.Distance(new Vector2(request.Epicenter.X, request.Epicenter.Y), viewPosition);
        if (distance > settings.MaxShakeRange) return 0f;
        return settings.ShakeIntensity(request.Type) * (1f - distance / settings.MaxShakeRange);
    }

    public void Add(ViewShakeRequest request, Vector2 viewPosition)
    {
        var angle = random.NextReal(0f, 2f * MathF.PI);
        _cos = MathF.Cos(angle);
        _sin = MathF.Sin(angle);
        var distance = Vector2.Distance(new Vector2(request.Epicenter.X, request.Epicenter.Y), viewPosition);
        if (distance > settings.MaxShakeRange) return;
        Intensity += FalloffIntensity(settings, request, viewPosition);
        if (Intensity > settings.MaxShakeIntensity) Intensity = 3f;
    }

    public Vector2 Update()
    {
        if (Intensity > 0.01f)
        {
            Offset = new Vector2(Intensity * _cos, Intensity * _sin);
            Intensity *= 0.75f;
            _cos = -_cos;
            _sin = -_sin;
        }
        else
        {
            Intensity = 0f;
            Offset = Vector2.Zero;
        }
        return Offset;
    }
}

/// <summary>Everything the renderer draws for one frame; one-shot requests (rays, shakes) are drained with the frame.</summary>
public sealed record FxFrame(
    long Frame,
    ParticleFrame Particles,
    ImmutableArray<TracerInstance> Tracers,
    ImmutableArray<LightPulseInstance> Lights,
    ImmutableArray<DecalInstance> Decals,
    ImmutableArray<RayEffectRequest> RayEffects,
    ImmutableArray<ViewShakeRequest> ViewShakes)
{
    /// <summary>The projectile streams (<c>W3DProjectileStreamDraw</c>): textured bands through the flying projectiles of one weapon, oldest first.</summary>
    public ImmutableArray<StreamInstance> ProjectileStreams { get; init; } = [];
}

/// <summary>
/// One run of a projectile stream (<c>W3DProjectileStreamDraw::doDrawModule</c>, a <c>SegmentedLineClass</c>): the points of the projectiles in flight, oldest first; the
/// texture tiled <see cref="TileFactor"/> times per segment, <see cref="Width"/> world units wide, drawn additive.
/// </summary>
public sealed record StreamInstance(ImmutableArray<Vector3> Points, string Texture, float Width, float TileFactor);
