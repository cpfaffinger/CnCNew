using System.Numerics;
using CnCNew.Presentation.Objects;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// A portable structure is drawn on its host's bone (<c>W3DDependencyModelDraw::adjustTransformMtx</c>) with the host's chassis in front, as the client composes it (issue 326).
/// The drawn form of a world matrix must give the matrix back.
/// </summary>
internal static class DependencyPlacementTests
{
    public static int Run(string[] args)
    {
        var t = new TestHarness("DependencyPlacement");
        t.Run("a matrix of a tilted host and a bone decomposes into place, heading, pitch and roll that the client composes back", RoundTrip);
        t.Run("a level host gives no chassis", Level);
        return t.Failures;
    }

    private static Matrix4x4 Compose(Vector3 position, float yaw, ChassisPose c)
        => Matrix4x4.CreateRotationX(-c.Roll) * Matrix4x4.CreateRotationY(c.Pitch) * Matrix4x4.CreateTranslation(0f, 0f, c.LiftZ) * Matrix4x4.CreateRotationZ(yaw) * Matrix4x4.CreateTranslation(position);

    private static void RoundTrip()
    {
        var bone = Matrix4x4.CreateRotationZ(0.3f) * Matrix4x4.CreateTranslation(2f, -1f, 6f);
        var host = new ChassisPose(0.12f, -0.08f, 0.5f, 0f, 0f, 0f, 0f, 0f);
        var world = bone * Compose(new Vector3(100f, 50f, 3f), 1.1f, host);
        var (position, yaw, chassis) = FrameProjector.Decompose(world);
        var back = Compose(position, yaw, chassis);
        foreach (var (a, b) in new[] { (world.M11, back.M11), (world.M12, back.M12), (world.M13, back.M13), (world.M21, back.M21), (world.M22, back.M22), (world.M23, back.M23),
                     (world.M31, back.M31), (world.M32, back.M32), (world.M33, back.M33), (world.M41, back.M41), (world.M42, back.M42), (world.M43, back.M43) })
            Assert(MathF.Abs(a - b) < 1e-4f, $"the drawn form gives the matrix back ({a} against {b})");
        Assert(chassis.Pitch != 0f && chassis.Roll != 0f, "the host's tilt is taken over");
        Assert(position.Z > 3f + 5f, $"the bone lies above the host's place ({position.Z})");
    }

    private static void Level()
    {
        var (position, yaw, chassis) = FrameProjector.Decompose(Matrix4x4.CreateRotationZ(0.7f) * Matrix4x4.CreateTranslation(1f, 2f, 3f));
        Assert(chassis.IsLevel && MathF.Abs(yaw - 0.7f) < 1e-5f && position == new Vector3(1f, 2f, 3f), "heading and place only");
    }
}
