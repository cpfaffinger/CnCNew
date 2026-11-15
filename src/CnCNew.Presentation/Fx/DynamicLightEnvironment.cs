using System.Numerics;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// How the light pulses (<see cref="LightPulseInstance"/>, <c>W3DDynamicLight</c>) light one render object, as <c>RTS3DScene::renderOneObject</c> gathers them into a
/// <c>LightEnvironmentClass</c> and <c>DX8Wrapper::Set_Light_Environment</c> hands them to the fixed-function pipeline:
/// <list type="bullet">
/// <item>Only a drawable that receives dynamic lights takes them (<c>Drawable::getReceivesDynamicLights</c>, <c>W3DModelDraw</c>'s <c>ReceivesDynamicLights</c>; the caller decides).</item>
/// <item>A pulse counts when its sphere (its position, its outer radius) meets the object's bounding sphere, and when its colour is not almost black
/// (<c>LightEnvironmentClass::Add_Light</c>: every channel below 0.05 is dropped).</item>
/// <item>Each light that counts adds its colour, attenuated at the object's centre by the far-attenuation ramp (<see cref="CentreAttenuation"/>), to the ambient of the
/// object, which is clamped to one per channel (<c>Pre_Render_Update</c>).</item>
/// <item>At most four lights reach the vertices: the map's global lights first, then the pulses, each inserted before the first light it outshines - the square length of its
/// diffuse colour, a pulse's attenuated at the centre (<c>InputLightStruct::Contribution</c>). A pulse can so push a weak global light out of the object's four; that light then
/// does not light the object (<see cref="DroppedGlobals"/>).</item>
/// <item>A pulse among the four becomes a Direct3D point light (<c>isPointLight</c> in <c>Set_Light_Environment</c>): ambient and diffuse its colour, range its outer radius, and
/// the attenuation <c>1 / (1 + 0.1 / inner * d + 8 / outer² * d²)</c> per vertex (<see cref="PointAttenuation"/>).</item>
/// </list>
/// </summary>
public static class DynamicLightEnvironment
{
    /// <summary><c>LightEnvironmentClass::MAX_LIGHTS</c>.</summary>
    public const int MaxLights = 4;

    /// <summary><c>Add_Light</c> drops a light whose every diffuse channel is below this.</summary>
    public const float AlmostBlack = 0.05f;

    /// <summary>The lights of one object: the pulses among its four (indices into the pulse list, strongest first), the global lights pushed out, the object's ambient.</summary>
    public readonly struct Selection
    {
        private readonly int _pulse0, _pulse1, _pulse2, _pulse3;

        internal Selection(ReadOnlySpan<int> pulses, int droppedGlobals, Vector3 ambient)
        {
            PulseCount = pulses.Length;
            _pulse0 = pulses.Length > 0 ? pulses[0] : -1;
            _pulse1 = pulses.Length > 1 ? pulses[1] : -1;
            _pulse2 = pulses.Length > 2 ? pulses[2] : -1;
            _pulse3 = pulses.Length > 3 ? pulses[3] : -1;
            DroppedGlobals = droppedGlobals;
            Ambient = ambient;
        }

        /// <summary>How many pulses light the object's vertices.</summary>
        public int PulseCount { get; }

        /// <summary>The index of the <paramref name="slot"/>-th pulse into the list it was chosen from.</summary>
        public int Pulse(int slot) => slot switch
        {
            0 when PulseCount > 0 => _pulse0,
            1 when PulseCount > 1 => _pulse1,
            2 when PulseCount > 2 => _pulse2,
            3 when PulseCount > 3 => _pulse3,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

        /// <summary>Bit <c>i</c>: global light <c>i</c> was among the object's lights until a pulse pushed it out.</summary>
        public int DroppedGlobals { get; }

        /// <summary>The object's ambient light: the scene's ambient plus what the pulses add at its centre, clamped to one.</summary>
        public Vector3 Ambient { get; }

        /// <summary>Whether a pulse changes anything for the object.</summary>
        public bool Any(Vector3 sceneAmbient) => PulseCount > 0 || DroppedGlobals != 0 || Ambient != Vector3.Clamp(sceneAmbient, Vector3.Zero, Vector3.One);
    }

    /// <summary>
    /// The lights of an object whose bounding sphere is (<paramref name="center"/>, <paramref name="radius"/>), among the scene's global lights (their diffuse colours, in their order)
    /// and the pulses in the order they were made.
    /// </summary>
    public static Selection Select(Vector3 center, float radius, ReadOnlySpan<Vector3> globalDiffuse, Vector3 sceneAmbient, ReadOnlySpan<LightPulseInstance> pulses)
    {
        Span<float> contribution = stackalloc float[MaxLights];
        // A global light is ~index, a pulse its index.
        Span<int> source = stackalloc int[MaxLights];
        var count = 0;
        var admitted = 0;
        for (var index = 0; index < globalDiffuse.Length; index++)
        {
            if (IsAlmostBlack(globalDiffuse[index])) continue;
            admitted |= 1 << index;
            Insert(globalDiffuse[index].LengthSquared(), ~index, contribution, source, ref count);
        }

        var ambient = sceneAmbient;
        for (var index = 0; index < pulses.Length; index++)
        {
            var pulse = pulses[index];
            if (IsAlmostBlack(pulse.Color)) continue;
            var distance = Vector3.Distance(center, pulse.Position);
            // RTS3DScene::renderOneObject: Spheres_Intersect of the object's and the light's bounding spheres (a point light's radius is its attenuation range).
            if (distance >= radius + pulse.OuterRadius) continue;
            var diffuse = pulse.Color * CentreAttenuation(distance, pulse.InnerRadius, pulse.OuterRadius);
            ambient += diffuse;
            Insert(diffuse.LengthSquared(), index, contribution, source, ref count);
        }

        Span<int> chosen = stackalloc int[MaxLights];
        var chosenCount = 0;
        var kept = 0;
        for (var slot = 0; slot < count; slot++)
        {
            if (source[slot] >= 0) chosen[chosenCount++] = source[slot];
            else kept |= 1 << ~source[slot];
        }

        return new Selection(chosen[..chosenCount], admitted & ~kept, Vector3.Clamp(ambient, Vector3.Zero, Vector3.One));
    }

    /// <summary>
    /// <c>InputLightStruct::Init_From_Point_Or_Spot_Light</c> with <c>FAR_ATTENUATION</c>: one within the inner radius, falling linearly to nothing at the outer one (a step at
    /// the inner radius when both are equal).
    /// </summary>
    public static float CentreAttenuation(float distance, float inner, float outer)
    {
        if (MathF.Abs(outer - inner) < 1e-6f) return distance > inner ? 0f : 1f;
        return Math.Clamp(1f - (distance - inner) / (outer - inner), 0f, 1f);
    }

    /// <summary>
    /// The Direct3D point light of <c>Set_Light_Environment</c>: <c>Attenuation0</c> one, <c>Attenuation1</c> 0.1 / inner (zero when inner and outer are equal),
    /// <c>Attenuation2</c> 8 / outer², nothing beyond its range, the outer radius.
    /// </summary>
    public static float PointAttenuation(float distance, float inner, float outer)
    {
        if (distance >= outer || outer <= 0f) return 0f;
        var linear = MathF.Abs(outer - inner) < 1e-5f ? 0f : 0.1f / inner;
        return 1f / (1f + linear * distance + 8f / (outer * outer) * distance * distance);
    }

    /// <summary>The linear factor of the attenuation (<see cref="PointAttenuation"/>), for a shader that evaluates it per pixel.</summary>
    public static float LinearAttenuationFactor(float inner, float outer) => MathF.Abs(outer - inner) < 1e-5f ? 0f : 0.1f / inner;

    private static bool IsAlmostBlack(Vector3 color) => color.X < AlmostBlack && color.Y < AlmostBlack && color.Z < AlmostBlack;

    /// <summary><c>Add_Light</c>: before the first light it outshines, the weakest falling off the end; at the end while there is room.</summary>
    private static void Insert(float strength, int light, Span<float> contribution, Span<int> source, ref int count)
    {
        for (var slot = 0; slot < count; slot++)
        {
            if (strength <= contribution[slot]) continue;
            for (var move = Math.Min(count, MaxLights - 1); move > slot; move--)
            {
                contribution[move] = contribution[move - 1];
                source[move] = source[move - 1];
            }

            contribution[slot] = strength;
            source[slot] = light;
            count = Math.Min(count + 1, MaxLights);
            return;
        }

        if (count < MaxLights)
        {
            contribution[count] = strength;
            source[count] = light;
            count++;
        }
    }
}
