using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Presentation;
using CnCNew.Presentation.Terrain;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.W3D;

namespace CnCNew.Game.Data;

/// <summary><c>W3DBridge::load</c>: the sections of a bridge's model, in the model's space, for <see cref="BridgeMeshBuilder"/>.</summary>
public static class BridgeArtLoader
{
    /// <summary>The art of one damage state; nothing when the block names no model, the content does not have it, or it has no left section (<c>load</c> returns false).</summary>
    public static BridgeArt? Load(W3dAssetLibrary library, BridgeVisual visual, Fixed scale)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(visual);
        if (visual.Model.Length == 0) return null;
        W3dModel model;
        try
        {
            model = library.GetModel(visual.Model);
        }
        catch (W3dMissingAssetException)
        {
            return null;
        }

        return Section(model, ".BRIDGE_LEFT") is { } left
            ? new BridgeArt(visual.Texture, FixedConvert.ToSingle(scale), left, Section(model, ".BRIDGE_SPAN"), Section(model, ".BRIDGE_RIGHT"))
            : null;
    }

    private static BridgeArtSection? Section(W3dModel model, string suffix)
    {
        if (BridgeSection.Find(model, suffix) is not { Mesh: { } mesh } subObject) return null;
        var transform = model.Skeleton.RestWorldTransforms[subObject.BoneIndex];
        var uv = mesh.Materials.Passes.Count > 0 && mesh.Materials.Passes[0].TextureStages.Count > 0 ? mesh.Materials.Passes[0].TextureStages[0].TextureCoordinates : [];
        var positions = ImmutableArray.CreateBuilder<Vector3>(mesh.Vertices.Count);
        var normals = ImmutableArray.CreateBuilder<Vector3>(mesh.Vertices.Count);
        var coordinates = ImmutableArray.CreateBuilder<Vector2>(mesh.Vertices.Count);
        for (var index = 0; index < mesh.Vertices.Count; index++)
        {
            positions.Add(Vector3.Transform(mesh.Vertices[index], transform));
            normals.Add(Vector3.TransformNormal(index < mesh.Normals.Count ? mesh.Normals[index] : Vector3.UnitZ, transform));
            // MeshModelClass reads V flipped.
            coordinates.Add(index < uv.Count ? new Vector2(uv[index].X, 1f - uv[index].Y) : Vector2.Zero);
        }

        var indices = ImmutableArray.CreateBuilder<int>(mesh.Triangles.Count * 3);
        foreach (var triangle in mesh.Triangles)
        {
            indices.Add(checked((int)triangle.Vertex0));
            indices.Add(checked((int)triangle.Vertex1));
            indices.Add(checked((int)triangle.Vertex2));
        }

        return new BridgeArtSection(positions.MoveToImmutable(), normals.MoveToImmutable(), coordinates.MoveToImmutable(), indices.MoveToImmutable());
    }
}
