using System.Collections.Immutable;

namespace CnCNew.W3D;

/// <summary>
/// Every top-level object of one W3D file, decoded. Known render-object kinds this runtime does not use (emitters,
/// dazzles, lights, ...) are listed in <see cref="IgnoredChunks"/>; unknown top-level chunks are decode errors.
/// </summary>
public sealed record W3dAssetFile(
    W3dReadResult Source,
    ImmutableArray<W3dMesh> Meshes,
    ImmutableArray<W3dHierarchy> Hierarchies,
    ImmutableArray<W3dHlod> Hlods,
    ImmutableArray<W3dAnimation> Animations,
    ImmutableArray<W3dBox> Boxes,
    ImmutableArray<W3dNullObject> NullObjects,
    ImmutableArray<W3dIgnoredChunk> IgnoredChunks)
{
    public static W3dAssetFile Read(Stream stream) => Decode(W3dFileReader.Read(stream));

    public static W3dAssetFile Decode(W3dReadResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var data = source.Data.Span;
        var hierarchies = ImmutableArray.CreateBuilder<W3dHierarchy>();
        var hlods = ImmutableArray.CreateBuilder<W3dHlod>();
        var animations = ImmutableArray.CreateBuilder<W3dAnimation>();
        var boxes = ImmutableArray.CreateBuilder<W3dBox>();
        var nullObjects = ImmutableArray.CreateBuilder<W3dNullObject>();
        var ignored = ImmutableArray.CreateBuilder<W3dIgnoredChunk>();
        foreach (var chunk in source.File.Chunks)
        {
            switch (chunk.Type)
            {
                case W3dChunkType.Mesh:
                    break;
                case W3dChunkType.Hierarchy:
                    hierarchies.Add(W3dHierarchyDecoder.Decode(data, chunk));
                    break;
                case W3dChunkType.Hlod:
                    hlods.Add(W3dHlodDecoder.Decode(data, chunk));
                    break;
                case W3dChunkType.Animation or W3dChunkType.CompressedAnimation:
                    animations.Add(W3dAnimationDecoder.Decode(data, chunk));
                    break;
                case W3dChunkType.Box:
                    boxes.Add(W3dHlodDecoder.DecodeBox(data, chunk));
                    break;
                case W3dChunkType.NullObject:
                    nullObjects.Add(W3dHlodDecoder.DecodeNullObject(data, chunk));
                    break;
                case W3dChunkType.Emitter:
                case W3dChunkType.Dazzle:
                case W3dChunkType.Light:
                case W3dChunkType.MorphAnimation:
                case W3dChunkType.HModel:
                case W3dChunkType.LodModel:
                case W3dChunkType.Collection:
                case W3dChunkType.Points:
                case W3dChunkType.Aggregate:
                case W3dChunkType.Sphere:
                case W3dChunkType.Ring:
                case W3dChunkType.Lightscape:
                case W3dChunkType.SoundRenderObject:
                case W3dChunkType.ShaderMesh:
                    ignored.Add(new W3dIgnoredChunk(chunk.Type, GetIgnoredKind(chunk.Type), chunk.HeaderOffset));
                    break;
                default:
                    throw new InvalidDataException(
                        $"Unknown top-level W3D chunk 0x{chunk.Type:X8} at {chunk.HeaderOffset}.");
            }
        }

        return new W3dAssetFile(
            source,
            [.. W3dMeshDecoder.Decode(source)],
            hierarchies.ToImmutable(),
            hlods.ToImmutable(),
            animations.ToImmutable(),
            boxes.ToImmutable(),
            nullObjects.ToImmutable(),
            ignored.ToImmutable());
    }

    public static string GetIgnoredKind(uint type) => type switch
    {
        W3dChunkType.Emitter => "emitter",
        W3dChunkType.Dazzle => "dazzle",
        W3dChunkType.Light => "light",
        W3dChunkType.MorphAnimation => "morph-animation",
        W3dChunkType.HModel => "hmodel",
        W3dChunkType.LodModel => "lod-model",
        W3dChunkType.Collection => "collection",
        W3dChunkType.Points => "points",
        W3dChunkType.Aggregate => "aggregate",
        W3dChunkType.Sphere => "sphere",
        W3dChunkType.Ring => "ring",
        W3dChunkType.Lightscape => "lightscape",
        W3dChunkType.SoundRenderObject => "sound-render-object",
        W3dChunkType.ShaderMesh => "shader-mesh",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not an ignored chunk kind."),
    };
}

public sealed record W3dIgnoredChunk(uint Type, string Kind, long HeaderOffset);
