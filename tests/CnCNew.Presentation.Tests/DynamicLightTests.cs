using System.Numerics;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Testing;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The light pulses light the objects in their reach (issue 23), as <c>RTS3DScene::renderOneObject</c>, <c>LightEnvironmentClass</c> and <c>DX8Wrapper::Set_Light_Environment</c>
/// do: until now they lit only the ground.
/// </summary>
internal static class DynamicLightTests
{
    private static readonly Vector3[] Globals = [new(0.7f, 0.7f, 0.65f), new(0.3f, 0.3f, 0.35f), new(0.1f, 0.1f, 0.1f)];
    private static readonly Vector3 Ambient = new(0.3f, 0.3f, 0.3f);

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/DynamicLightFixture.ini"] = """
        Object DlLit
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = DlLitModel
            End
          End
        End
        Object DlUnlit
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = DlUnlitModel
            End
          End
          Draw = W3DModelDraw ModuleTag_02
            ReceivesDynamicLights = No
            DefaultConditionState
              Model = DlUnlitLamp
            End
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("DynamicLight");
        t.Run("the per-vertex attenuation of a pulse is the Direct3D point light of Set_Light_Environment", PointAttenuation);
        t.Run("the attenuation at an object's centre is the far-attenuation ramp from the inner to the outer radius", CentreAttenuation);
        t.Run("a pulse lights an object whose sphere it meets, and adds its colour at the centre to the object's ambient", Reach);
        t.Run("of the global lights and the pulses the four strongest light the vertices; a strong pulse pushes the weakest global light out", Competition);
        t.Run("an almost black pulse is no light, and the ambient stops at one", BlackAndClamp);
        t.Run("ReceivesDynamicLights = No of one draw module keeps the whole drawable out of the pulses", ReceivesFlag);
        return t.Failures;
    }

    private static void PointAttenuation()
    {
        // createLightPulse(pos, colour, 1, radius, ...): inner 1, outer 1 + radius. Attenuation0 1, Attenuation1 0.1 / 1, Attenuation2 8 / outer².
        Near(1.0, DynamicLightEnvironment.PointAttenuation(0f, 1f, 101f), 1e-6, "At the light the attenuation is one.");
        Near(1.0 / (1.0 + 0.1 * 50 + 8.0 / (101.0 * 101.0) * 2500), DynamicLightEnvironment.PointAttenuation(50f, 1f, 101f), 1e-6, "Half way the inverse quadratic of the original.");
        Near(1.0 / (1.0 + 0.1 * 100 + 8.0 / (101.0 * 101.0) * 10000), DynamicLightEnvironment.PointAttenuation(100f, 1f, 101f), 1e-6, "Just inside the range.");
        Assert(DynamicLightEnvironment.PointAttenuation(101f, 1f, 101f) == 0f && DynamicLightEnvironment.PointAttenuation(500f, 1f, 101f) == 0f, "Beyond its range a point light lights nothing.");
        Near(1.0 / (1.0 + 8.0 / 400 * 100), DynamicLightEnvironment.PointAttenuation(10f, 20f, 20f), 1e-6, "Equal radii drop the linear term.");
        Near(0.1, DynamicLightEnvironment.LinearAttenuationFactor(1f, 101f), 1e-7, "The linear factor the shader is handed.");
    }

    private static void CentreAttenuation()
    {
        Assert(DynamicLightEnvironment.CentreAttenuation(0.5f, 1f, 101f) == 1f, "Within the inner radius the light is whole.");
        Near(0.5, DynamicLightEnvironment.CentreAttenuation(51f, 1f, 101f), 1e-6, "Half way between the radii, half.");
        Assert(DynamicLightEnvironment.CentreAttenuation(150f, 1f, 101f) == 0f, "Beyond the outer radius nothing.");
        Assert(DynamicLightEnvironment.CentreAttenuation(5f, 10f, 10f) == 1f && DynamicLightEnvironment.CentreAttenuation(11f, 10f, 10f) == 0f, "Equal radii are a step.");
    }

    private static void Reach()
    {
        var colour = new Vector3(1f, 0.8f, 0.4f);
        LightPulseInstance[] pulses = [new(new Vector3(100f, 0f, 10f), colour, 1f, 101f, 1f)];

        var far = DynamicLightEnvironment.Select(new Vector3(300f, 0f, 10f), 20f, Globals, Ambient, pulses);
        Assert(far.PulseCount == 0 && far.DroppedGlobals == 0 && far.Ambient == Ambient && !far.Any(Ambient), "An object whose sphere the pulse does not meet keeps its light.");

        // 110 units away with a radius of 20: the spheres meet (110 < 20 + 101), the centre lies beyond the outer radius.
        var edge = DynamicLightEnvironment.Select(new Vector3(210f, 0f, 10f), 20f, Globals, Ambient, pulses);
        Assert(edge.PulseCount == 1 && edge.Pulse(0) == 0 && edge.Ambient == Ambient, "A sphere that reaches into the range takes the pulse, though its centre lies outside it.");

        var near = DynamicLightEnvironment.Select(new Vector3(151f, 0f, 10f), 20f, Globals, Ambient, pulses);
        Assert(near.PulseCount == 1 && near.DroppedGlobals == 0, "One pulse fits beside three global lights.");
        Near(0.3 + 0.5 * 0.4, near.Ambient.Z, 1e-5, "The pulse adds its colour, attenuated at the centre (51 of 101: one half), to the ambient.");
        Assert(near.Any(Ambient), "The object is lit by the pulse.");
    }

    private static void Competition()
    {
        var bright = new Vector3(1f, 0.8f, 0.4f);
        LightPulseInstance[] pulses =
        [
            new(new Vector3(0f, 0f, 0f), bright, 1f, 101f, 1f),
            new(new Vector3(5f, 0f, 0f), bright * 0.9f, 1f, 101f, 1f),
            new(new Vector3(-5f, 0f, 0f), bright * 0.05f + new Vector3(0.05f, 0f, 0f), 1f, 101f, 1f),
        ];
        var lights = DynamicLightEnvironment.Select(Vector3.Zero, 10f, Globals, Ambient, pulses);
        // Contributions: pulse 0 1.80, the sun 1.4025, pulse 1 about 1.34 (0.9 of the colour, 0.96 of it at the centre), the second light 0.3325, the third 0.03, pulse 2
        // 0.01: the two bright pulses, the sun and the second light; the third global light falls off the end, the weak third pulse never gets in.
        Assert(lights.PulseCount == 2 && lights.Pulse(0) == 0 && lights.Pulse(1) == 1, $"The two bright pulses light the vertices ({lights.PulseCount}).");
        Assert(lights.DroppedGlobals == 0b100, $"The weakest global light is pushed out ({lights.DroppedGlobals}).");
        Assert(lights.Ambient.X == 1f, "Every pulse that counts adds to the ambient, also the one that found no place, and the ambient stops at one.");

        LightPulseInstance[] faint = [new(new Vector3(115f, 0f, 0f), bright, 1f, 101f, 1f), new(new Vector3(-115f, 0f, 0f), bright, 1f, 101f, 1f)];
        var edge = DynamicLightEnvironment.Select(Vector3.Zero, 20f, Globals, Ambient, faint);
        Assert(edge.PulseCount == 1 && edge.Pulse(0) == 0 && edge.DroppedGlobals == 0, "Two pulses that do not reach the centre: the first takes the free place, the second finds none.");
    }

    private static void BlackAndClamp()
    {
        LightPulseInstance[] black = [new(Vector3.Zero, new Vector3(0.04f, 0.049f, 0f), 1f, 101f, 0.05f)];
        var lights = DynamicLightEnvironment.Select(Vector3.Zero, 10f, Globals, Ambient, black);
        Assert(lights.PulseCount == 0 && lights.Ambient == Ambient, "A colour below 0.05 in every channel is no light (a pulse that has faded so far).");

        LightPulseInstance[] white = [new(Vector3.Zero, Vector3.One, 1f, 101f, 1f)];
        var clamped = DynamicLightEnvironment.Select(Vector3.Zero, 10f, Globals, Ambient, white);
        Assert(clamped.Ambient == Vector3.One, "The ambient is clamped to one per channel.");
        Assert(clamped.DroppedGlobals == 0, "With room for a fourth light nothing is pushed out.");
    }

    private static void ReceivesFlag()
    {
        var fixture = new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: Files());
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);
        Assert(catalog["DlLit"].ReceivesDynamicLights && catalog["DlLit"].Draws[0].ReceivesDynamicLights, "A drawable takes the pulses by default.");
        var unlit = catalog["DlUnlit"];
        Assert(unlit.Draws[0].ReceivesDynamicLights && !unlit.Draws[1].ReceivesDynamicLights, "The key belongs to its module.");
        Assert(!unlit.ReceivesDynamicLights, "One module that says no is enough for the drawable.");
    }
}
