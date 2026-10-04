using System.Collections.Immutable;

namespace CnCNew.W3D;

/// <summary>What happens to one sub-object of the most detailed LOD when a renderer draws the model (the rules <c>ModelAsset.Build</c> of the client applies).</summary>
public enum W3dPartDisposition
{
    /// <summary>Drawn: renderable, has triangles, starts visible.</summary>
    Drawn,

    /// <summary>Drawn only after a condition state shows it (<c>W3D_MESH_FLAG_HIDDEN</c>, <c>ShowSubObject</c>).</summary>
    HiddenByDefault,

    /// <summary>Box, obsolete AABOX/OBBOX or collision-flagged geometry: never drawn, by design.</summary>
    CollisionOnly,

    /// <summary>Shadow geometry: never drawn into the colour pass, by design.</summary>
    ShadowOnly,

    /// <summary>A null object (a bone marker): nothing to draw, by design.</summary>
    NullObject,

    /// <summary>A mesh without triangles.</summary>
    NoTriangles,
}

/// <summary>One sub-object of the most detailed LOD with its fate. <see cref="CoveredTriangles"/> is the number of triangles the render plan puts into a batch.</summary>
public sealed record W3dPartAudit(string Name, int BoneIndex, W3dPartDisposition Disposition, int Triangles, int CoveredTriangles, int Batches, int Passes, bool Skinned, bool TwoSided, bool CameraFacing);

/// <summary>
/// Mesh-by-mesh accounting of one model: which sub-objects exist, which a renderer draws, which it leaves out and why, and whether the render plan
/// loses triangles on the way (<see cref="LostTriangles"/> must be zero). Nothing is dropped silently: every sub-object has a <see cref="W3dPartDisposition"/>.
/// </summary>
public sealed record W3dModelDrawAudit(
    string Model,
    int LodCount,
    ImmutableArray<W3dPartAudit> Parts,
    int Aggregates,
    int Proxies,
    int Unresolved,
    int Unreferenced,
    int IgnoredChunks)
{
    public int TotalTriangles => Parts.Sum(part => part.Triangles);
    public int DrawnTriangles => Parts.Where(part => part.Disposition == W3dPartDisposition.Drawn).Sum(part => part.CoveredTriangles);
    public int HiddenByDefaultTriangles => Parts.Where(part => part.Disposition == W3dPartDisposition.HiddenByDefault).Sum(part => part.CoveredTriangles);
    public int ExcludedTriangles => Parts.Where(part => part.Disposition is W3dPartDisposition.CollisionOnly or W3dPartDisposition.ShadowOnly).Sum(part => part.Triangles);

    /// <summary>Triangles of drawable parts that the render plan did not put into any batch (must be zero).</summary>
    public int LostTriangles => Parts.Where(part => part.Disposition is W3dPartDisposition.Drawn or W3dPartDisposition.HiddenByDefault).Sum(part => part.Triangles - part.CoveredTriangles);

    public int CameraFacingParts => Parts.Count(part => part.CameraFacing && part.Disposition is W3dPartDisposition.Drawn or W3dPartDisposition.HiddenByDefault);

    public static W3dModelDrawAudit Analyze(W3dModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var parts = ImmutableArray.CreateBuilder<W3dPartAudit>();
        foreach (var sub in model.GetLod(model.LodCount - 1))
        {
            var triangles = sub.Mesh?.Triangles.Count ?? 0;
            var covered = 0;
            var batches = 0;
            var passes = 0;
            if (sub.IsRenderable && sub.Mesh is { } mesh && triangles > 0)
            {
                var plan = W3dRenderPlan.Build(mesh);
                batches = plan.Count;
                covered = plan.Sum(batch => batch.TriangleIndices.Distinct().Count());
                passes = plan.Sum(batch => batch.Passes.Count);
            }

            var disposition = sub.Kind switch
            {
                W3dSubObjectKind.NullObject => W3dPartDisposition.NullObject,
                W3dSubObjectKind.Box => W3dPartDisposition.CollisionOnly,
                _ when sub.IsCollisionOnly => W3dPartDisposition.CollisionOnly,
                _ when sub.IsShadowOnly => W3dPartDisposition.ShadowOnly,
                _ when triangles == 0 => W3dPartDisposition.NoTriangles,
                _ when !sub.IsVisibleByDefault => W3dPartDisposition.HiddenByDefault,
                _ => W3dPartDisposition.Drawn,
            };
            parts.Add(new W3dPartAudit(sub.Name, sub.BoneIndex, disposition, triangles, covered, batches, passes, sub.IsSkinned, sub.IsTwoSided, sub.IsCameraFacing));
        }

        return new W3dModelDrawAudit(model.Name, model.LodCount, parts.ToImmutable(), model.Aggregates.Length, model.Proxies.Length, model.UnresolvedSubObjects.Length,
            model.UnreferencedMeshes.Length, model.IgnoredChunks.Length);
    }
}
