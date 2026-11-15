using System.Collections.Immutable;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Terrain;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using NumericsVector3 = System.Numerics.Vector3;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// What the object lights of a frame are made of: the map's global lights and ambient (<see cref="SetScene"/>), the light pulses of the frame (the effect layer), and the objects that
/// take them with their bounding spheres (the object layer, every frame).
/// </summary>
internal sealed class DynamicLighting
{
    private readonly Dictionary<ModelComponent, (NumericsVector3 Center, float Radius)> _targets = new(ReferenceEqualityComparer.Instance);
    private NumericsVector3[] _globalDiffuse = [];
    private Vector3[] _globalLinear = [];
    private Vector3[] _globalToward = [];

    public ColorSpace ColorSpace { get; set; } = ColorSpace.Linear;

    /// <summary>The scene's ambient (<c>RTS3DScene::Get_Ambient_Light</c>: the first object light's ambient).</summary>
    public NumericsVector3 SceneAmbient { get; private set; }

    /// <summary>The light pulses alive in this frame, in the order they were made.</summary>
    public ImmutableArray<LightPulseInstance> Pulses { get; set; } = [];

    /// <summary>Frames that had a pulse, and the model components (one per object's draw module, and per skin) and draws they lit, since the match began.</summary>
    public long FramesWithPulses { get; set; }
    public long ComponentsLit { get; set; }
    public long DrawsLit { get; set; }

    /// <summary>Draws in frames with a pulse whose material takes the pulses, and draws of them whose component was named by the object layer.</summary>
    public long DrawsWithShader { get; set; }
    public long DrawsOfTargets { get; set; }

    public ReadOnlySpan<NumericsVector3> GlobalDiffuse => _globalDiffuse;

    /// <summary>The global lights as <see cref="SceneLighting.Install"/> gives them to Stride: their colour in the device's colour space, the direction towards them.</summary>
    public Vector3 GlobalLinear(int index) => _globalLinear[index];
    public Vector3 GlobalToward(int index) => _globalToward[index];

    public void SetScene(TerrainLighting lighting)
    {
        SceneAmbient = lighting.Lights.IsDefaultOrEmpty ? NumericsVector3.Zero : lighting.Lights[0].Ambient;
        _globalDiffuse = [.. lighting.Lights.Select(light => light.Diffuse)];
        // A light without colour is not installed (SceneLighting), so it lights nothing to take away.
        _globalLinear = [.. lighting.Lights.Select(light => light.Diffuse == NumericsVector3.Zero ? Vector3.Zero : Linear(light.Diffuse))];
        _globalToward = [.. lighting.Lights.Select(light => -Vector3.Normalize(new Vector3(light.Position.X, light.Position.Y, light.Position.Z)))];
    }

    /// <summary>Forgets the objects of the frame before; the object layer names this frame's.</summary>
    public void BeginFrame() => _targets.Clear();

    /// <summary>A model component of an object that takes the pulses, with the object's bounding sphere in the world.</summary>
    public void Add(ModelComponent component, NumericsVector3 center, float radius) => _targets[component] = (center, radius);

    public bool TryGetTarget(ModelComponent component, out (NumericsVector3 Center, float Radius) target) => _targets.TryGetValue(component, out target);

    public Vector3 Linear(NumericsVector3 color) => new Color3(color.X, color.Y, color.Z).ToColorSpace(ColorSpace).ToVector3();
}

/// <summary>
/// Writes the per-draw values of <see cref="DynamicLightColor"/>: for a mesh of an object that takes the pulses the lights <see cref="DynamicLightEnvironment.Select"/> chooses for the object,
/// for every other mesh nothing. The values are written for every draw of every frame, since the per-draw buffers are allocated afresh each frame.
/// </summary>
internal sealed unsafe class DynamicLightRenderFeature(DynamicLighting lighting) : SubRenderFeature
{
    private const int Values = 1 + 2 * DynamicLightColor.PointLights + 2 * DynamicLightColor.DroppedLights;

    private readonly Dictionary<ModelComponent, int> _packedAt = new(ReferenceEqualityComparer.Instance);
    private readonly List<Vector4> _packed = [];
    private ConstantBufferOffsetReference _ambient;
    private ConstantBufferOffsetReference _positions;
    private ConstantBufferOffsetReference _colors;
    private ConstantBufferOffsetReference _droppedColors;
    private ConstantBufferOffsetReference _droppedDirections;

    protected override void InitializeCore()
    {
        var root = (RootEffectRenderFeature)RootRenderFeature;
        _ambient = root.CreateDrawCBufferOffsetSlot(DynamicLightColor.AmbientMember);
        _positions = root.CreateDrawCBufferOffsetSlot(DynamicLightColor.PositionsMember);
        _colors = root.CreateDrawCBufferOffsetSlot(DynamicLightColor.ColorsMember);
        _droppedColors = root.CreateDrawCBufferOffsetSlot(DynamicLightColor.DroppedColorsMember);
        _droppedDirections = root.CreateDrawCBufferOffsetSlot(DynamicLightColor.DroppedDirectionsMember);
    }

    public override void Prepare(RenderDrawContext context)
    {
        _packedAt.Clear();
        _packed.Clear();
        var pulses = lighting.Pulses;
        var anyPulse = !pulses.IsDefaultOrEmpty;
        if (anyPulse) lighting.FramesWithPulses++;
        Span<Vector4> none = stackalloc Vector4[Values];
        none.Clear();
        for (var index = 0; index < RootRenderFeature.RenderNodes.Count; index++)
        {
            var renderNode = RootRenderFeature.RenderNodes[index];
            var layout = renderNode.RenderEffect?.Reflection?.PerDrawLayout;
            if (layout is null) continue;
            var ambient = layout.GetConstantBufferOffset(_ambient);
            if (ambient == -1) continue;
            ReadOnlySpan<Vector4> values = none;
            if (anyPulse)
            {
                lighting.DrawsWithShader++;
                if (renderNode.RenderObject.Source is ModelComponent component && lighting.TryGetTarget(component, out var target))
                {
                    lighting.DrawsOfTargets++;
                    if (Packed(component, target, pulses) is var at and >= 0)
                    {
                        values = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_packed).Slice(at, Values);
                        lighting.DrawsLit++;
                    }
                }
            }

            var buffer = (byte*)renderNode.Resources.ConstantBuffer.Data;
            Write(buffer, ambient, values[..1]);
            Write(buffer, layout.GetConstantBufferOffset(_positions), values.Slice(1, DynamicLightColor.PointLights));
            Write(buffer, layout.GetConstantBufferOffset(_colors), values.Slice(1 + DynamicLightColor.PointLights, DynamicLightColor.PointLights));
            Write(buffer, layout.GetConstantBufferOffset(_droppedColors), values.Slice(1 + 2 * DynamicLightColor.PointLights, DynamicLightColor.DroppedLights));
            Write(buffer, layout.GetConstantBufferOffset(_droppedDirections), values.Slice(1 + 2 * DynamicLightColor.PointLights + DynamicLightColor.DroppedLights, DynamicLightColor.DroppedLights));
        }
    }

    private static void Write(byte* buffer, int offset, ReadOnlySpan<Vector4> values)
    {
        if (offset == -1) return;
        values.CopyTo(new Span<Vector4>(buffer + offset, values.Length));
    }

    /// <summary>Where the values of the component's object begin in <see cref="_packed"/>, or -1 when no pulse changes its light; chosen once per frame and object.</summary>
    private int Packed(ModelComponent component, (NumericsVector3 Center, float Radius) target, ImmutableArray<LightPulseInstance> pulses)
    {
        if (_packedAt.TryGetValue(component, out var known)) return known;
        var selection = DynamicLightEnvironment.Select(target.Center, target.Radius, lighting.GlobalDiffuse, lighting.SceneAmbient, pulses.AsSpan());
        if (!selection.Any(lighting.SceneAmbient))
        {
            _packedAt[component] = -1;
            return -1;
        }

        var at = _packed.Count;
        var ambient = lighting.Linear(selection.Ambient) - lighting.Linear(Vector3Clamp(lighting.SceneAmbient));
        _packed.Add(new Vector4(ambient, selection.PulseCount));
        for (var slot = 0; slot < DynamicLightColor.PointLights; slot++)
        {
            if (slot >= selection.PulseCount) { _packed.Add(Vector4.Zero); continue; }
            var pulse = pulses[selection.Pulse(slot)];
            _packed.Add(new Vector4(pulse.Position.X, pulse.Position.Y, pulse.Position.Z, DynamicLightEnvironment.LinearAttenuationFactor(pulse.InnerRadius, pulse.OuterRadius)));
        }

        for (var slot = 0; slot < DynamicLightColor.PointLights; slot++)
        {
            if (slot >= selection.PulseCount) { _packed.Add(Vector4.Zero); continue; }
            var pulse = pulses[selection.Pulse(slot)];
            _packed.Add(new Vector4(lighting.Linear(pulse.Color), pulse.OuterRadius));
        }

        for (var global = 0; global < DynamicLightColor.DroppedLights; global++)
            _packed.Add((selection.DroppedGlobals & (1 << global)) != 0 ? new Vector4(lighting.GlobalLinear(global), 0f) : Vector4.Zero);
        for (var global = 0; global < DynamicLightColor.DroppedLights; global++)
            _packed.Add((selection.DroppedGlobals & (1 << global)) != 0 ? new Vector4(lighting.GlobalToward(global), 0f) : Vector4.Zero);
        lighting.ComponentsLit++;
        _packedAt[component] = at;
        return at;
    }

    private static NumericsVector3 Vector3Clamp(NumericsVector3 value) => NumericsVector3.Clamp(value, NumericsVector3.Zero, NumericsVector3.One);
}
