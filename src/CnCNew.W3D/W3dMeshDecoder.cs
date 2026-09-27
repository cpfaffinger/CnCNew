using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace CnCNew.W3D;

public static class W3dMeshDecoder
{
    private const int MeshHeader3Size = 116;
    private const int Vector3Size = 12;
    private const int TriangleSize = 32;
    private const int ShaderSize = 16;
    private const int VertexMaterialSize = 32;

    public static IReadOnlyList<W3dMesh> Decode(W3dReadResult source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var data = source.Data.Span;
        var meshes = new List<W3dMesh>();
        foreach (var chunk in source.File.AllChunks)
        {
            if (chunk.Type == W3dChunkType.Mesh && chunk.IsContainer)
            {
                meshes.Add(DecodeMesh(data, chunk));
            }
        }

        return meshes;
    }

    private static W3dMesh DecodeMesh(ReadOnlySpan<byte> data, W3dChunk meshChunk)
    {
        var headerChunk = GetSingleChild(meshChunk, W3dChunkType.MeshHeader3, required: true)!;
        if (headerChunk.Size != MeshHeader3Size)
        {
            throw new InvalidDataException(
                $"Mesh header at {headerChunk.HeaderOffset} has {headerChunk.Size} bytes; expected {MeshHeader3Size}.");
        }

        var header = Slice(data, headerChunk);
        var version = ReadUInt32(header, 0);
        var attributes = ReadUInt32(header, 4);
        var name = ReadFixedString(header.Slice(8, 16));
        var containerName = ReadFixedString(header.Slice(24, 16));
        var triangleCount = ToCount(ReadUInt32(header, 40), "triangle", name);
        var vertexCount = ToCount(ReadUInt32(header, 44), "vertex", name);
        var materialCount = ToCount(ReadUInt32(header, 48), "material", name);
        var sortLevel = BinaryPrimitives.ReadInt32LittleEndian(header[56..]);
        var vertexChannels = ReadUInt32(header, 68);
        var faceChannels = ReadUInt32(header, 72);
        var boundsMinimum = ReadVector3(header, 76);
        var boundsMaximum = ReadVector3(header, 88);
        var sphereCenter = ReadVector3(header, 100);
        var sphereRadius = ReadSingle(header, 112);

        ValidateFinite(boundsMinimum, $"mesh '{name}' minimum bounds");
        ValidateFinite(boundsMaximum, $"mesh '{name}' maximum bounds");
        ValidateFinite(sphereCenter, $"mesh '{name}' sphere center");
        if (!float.IsFinite(sphereRadius) || sphereRadius < 0)
        {
            throw new InvalidDataException($"Mesh '{name}' has invalid sphere radius {sphereRadius}.");
        }

        var vertices = ReadVectors(meshChunk, W3dChunkType.Vertices, vertexCount, required: vertexCount > 0, data, name);
        var normals = ReadVectors(meshChunk, W3dChunkType.VertexNormals, vertexCount, required: false, data, name);
        var triangles = ReadTriangles(meshChunk, triangleCount, vertexCount, data, name);
        var materialVariants = ReadMaterialVariants(meshChunk, vertexCount, triangleCount, data, name);
        var materials = SelectMaterialVariant(materialVariants, attributes);

        return new W3dMesh(
            name,
            containerName,
            version,
            attributes,
            materialCount,
            sortLevel,
            vertexChannels,
            faceChannels,
            boundsMinimum,
            boundsMaximum,
            sphereCenter,
            sphereRadius,
            vertices,
            normals,
            triangles,
            materials,
            materialVariants);
    }

    private static IReadOnlyList<W3dMaterialVariant> ReadMaterialVariants(
        W3dChunk mesh,
        int vertexCount,
        int triangleCount,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var variants = new List<W3dMaterialVariant>();
        if (mesh.Children.Any(chunk => chunk.Type == W3dChunkType.MaterialInfo))
        {
            variants.Add(new W3dMaterialVariant(
                W3dMaterialVariantKind.Default,
                ReadMaterials(mesh, vertexCount, triangleCount, data, meshName)));
        }

        foreach (var child in mesh.Children)
        {
            var kind = child.Type switch
            {
                W3dChunkType.PrelitUnlit => W3dMaterialVariantKind.PrelitUnlit,
                W3dChunkType.PrelitVertex => W3dMaterialVariantKind.PrelitVertex,
                W3dChunkType.PrelitLightmapMultiPass => W3dMaterialVariantKind.PrelitLightmapMultiPass,
                W3dChunkType.PrelitLightmapMultiTexture => W3dMaterialVariantKind.PrelitLightmapMultiTexture,
                _ => (W3dMaterialVariantKind?)null,
            };
            if (kind is not null)
            {
                variants.Add(new W3dMaterialVariant(
                    kind.Value,
                    ReadMaterials(child, vertexCount, triangleCount, data, meshName)));
            }
        }

        return variants;
    }

    private static W3dMaterialData SelectMaterialVariant(
        IReadOnlyList<W3dMaterialVariant> variants,
        uint attributes)
    {
        var requested = (attributes & 0x0f000000u) switch
        {
            0x01000000u => W3dMaterialVariantKind.PrelitUnlit,
            0x02000000u => W3dMaterialVariantKind.PrelitVertex,
            0x04000000u => W3dMaterialVariantKind.PrelitLightmapMultiPass,
            0x08000000u => W3dMaterialVariantKind.PrelitLightmapMultiTexture,
            _ => W3dMaterialVariantKind.Default,
        };
        return variants.FirstOrDefault(variant => variant.Kind == requested)?.Materials
            ?? variants.FirstOrDefault(variant => variant.Kind == W3dMaterialVariantKind.Default)?.Materials
            ?? variants.FirstOrDefault()?.Materials
            ?? W3dMaterialData.Empty;
    }

    private static W3dMaterialData ReadMaterials(
        W3dChunk mesh,
        int vertexCount,
        int triangleCount,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var infoChunk = GetSingleChild(mesh, W3dChunkType.MaterialInfo, required: true)!;

        if (infoChunk.Size != 16)
        {
            throw new InvalidDataException($"Mesh '{meshName}' material info has {infoChunk.Size} bytes; expected 16.");
        }

        var info = Slice(data, infoChunk);
        var passCount = ToCount(ReadUInt32(info, 0), "material pass", meshName);
        var vertexMaterialCount = ToCount(ReadUInt32(info, 4), "vertex material", meshName);
        var shaderCount = ToCount(ReadUInt32(info, 8), "shader", meshName);
        var textureCount = ToCount(ReadUInt32(info, 12), "texture", meshName);

        var shaders = ReadShaders(mesh, shaderCount, data, meshName);
        var vertexMaterials = ReadVertexMaterials(mesh, vertexMaterialCount, data, meshName);

        var texturesContainer = GetSingleChild(mesh, W3dChunkType.Textures, required: textureCount > 0);
        var textures = new List<W3dTextureReference>();
        if (texturesContainer is not null)
        {
            foreach (var texture in texturesContainer.Children.Where(chunk => chunk.Type == W3dChunkType.Texture))
            {
                textures.Add(ReadTexture(texture, data, meshName));
            }
        }

        if (textures.Count != textureCount)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' declares {textureCount} textures but contains {textures.Count} definitions.");
        }

        var passes = new List<W3dMaterialPass>();
        foreach (var pass in mesh.Children.Where(chunk => chunk.Type == W3dChunkType.MaterialPass))
        {
            passes.Add(ReadMaterialPass(
                pass,
                vertexCount,
                triangleCount,
                vertexMaterialCount,
                shaderCount,
                textureCount,
                data,
                meshName));
        }

        if (passes.Count != passCount)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' declares {passCount} material passes but contains {passes.Count}.");
        }

        return new W3dMaterialData(
            passCount,
            vertexMaterialCount,
            shaderCount,
            textureCount,
            shaders,
            vertexMaterials,
            textures,
            passes);
    }

    private static IReadOnlyList<W3dShader> ReadShaders(
        W3dChunk parent,
        int count,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var chunk = GetSingleChild(parent, W3dChunkType.Shaders, required: count > 0);
        if (chunk is null)
        {
            return [];
        }

        var expectedSize = checked(count * ShaderSize);
        if (chunk.Size != expectedSize)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' shader chunk has {chunk.Size} bytes; expected {expectedSize}.");
        }

        var payload = Slice(data, chunk);
        var result = new W3dShader[count];
        for (var index = 0; index < count; index++)
        {
            var offset = index * ShaderSize;
            var depthCompare = payload[offset];
            var depthWrite = payload[offset + 1];
            var destinationBlend = payload[offset + 3];
            var primaryGradient = payload[offset + 5];
            var sourceBlend = payload[offset + 7];
            var texturing = payload[offset + 8];
            var alphaTest = payload[offset + 12];
            if (depthCompare > 7 || depthWrite > 1 || destinationBlend > 6 || primaryGradient > 5 ||
                sourceBlend > 3 || texturing > 1 || alphaTest > 1)
            {
                throw new InvalidDataException($"Mesh '{meshName}' shader {index} contains an invalid render-state value.");
            }

            result[index] = new W3dShader(
                (W3dDepthCompare)depthCompare,
                depthWrite != 0,
                payload[offset + 2],
                (W3dDestinationBlend)destinationBlend,
                payload[offset + 4],
                (W3dPrimaryGradient)primaryGradient,
                payload[offset + 6],
                (W3dSourceBlend)sourceBlend,
                texturing != 0,
                payload[offset + 9],
                payload[offset + 10],
                payload[offset + 11],
                alphaTest != 0,
                payload[offset + 13],
                payload[offset + 14],
                payload[offset + 15]);
        }

        return result;
    }

    private static IReadOnlyList<W3dVertexMaterial> ReadVertexMaterials(
        W3dChunk parent,
        int count,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var container = GetSingleChild(parent, W3dChunkType.VertexMaterials, required: count > 0);
        if (container is null)
        {
            return [];
        }

        var definitions = container.Children.Where(chunk => chunk.Type == W3dChunkType.VertexMaterial).ToArray();
        if (definitions.Length != count)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' declares {count} vertex materials but contains {definitions.Length} definitions.");
        }

        var result = new W3dVertexMaterial[definitions.Length];
        for (var index = 0; index < definitions.Length; index++)
        {
            result[index] = ReadVertexMaterial(definitions[index], index, data, meshName);
        }

        return result;
    }

    private static W3dVertexMaterial ReadVertexMaterial(
        W3dChunk definition,
        int index,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var nameChunk = GetSingleChild(definition, W3dChunkType.VertexMaterialName, required: true)!;
        var infoChunk = GetSingleChild(definition, W3dChunkType.VertexMaterialInfo, required: true)!;
        if (infoChunk.Size != VertexMaterialSize)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' vertex material {index} has {infoChunk.Size} info bytes; expected {VertexMaterialSize}.");
        }

        var payload = Slice(data, infoChunk);
        var shininess = ReadSingle(payload, 20);
        var opacity = ReadSingle(payload, 24);
        var translucency = ReadSingle(payload, 28);
        if (!float.IsFinite(shininess) || !float.IsFinite(opacity) || !float.IsFinite(translucency))
        {
            throw new InvalidDataException($"Mesh '{meshName}' vertex material {index} has a non-finite scalar.");
        }

        return new W3dVertexMaterial(
            ReadFixedString(Slice(data, nameChunk)),
            ReadUInt32(payload, 0),
            ReadRgb(payload, 4),
            ReadRgb(payload, 8),
            ReadRgb(payload, 12),
            ReadRgb(payload, 16),
            shininess,
            opacity,
            translucency,
            ReadOptionalString(definition, W3dChunkType.VertexMapperArgs0, data),
            ReadOptionalString(definition, W3dChunkType.VertexMapperArgs1, data));
    }

    private static W3dTextureReference ReadTexture(W3dChunk texture, ReadOnlySpan<byte> data, string meshName)
    {
        var nameChunk = GetSingleChild(texture, W3dChunkType.TextureName, required: true)!;
        var name = ReadFixedString(Slice(data, nameChunk));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidDataException($"Mesh '{meshName}' contains an empty texture name.");
        }

        return new W3dTextureReference(name);
    }

    private static W3dMaterialPass ReadMaterialPass(
        W3dChunk pass,
        int vertexCount,
        int triangleCount,
        int vertexMaterialCount,
        int shaderCount,
        int textureCount,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var vertexMaterialIds = ReadOptionalIds(
            pass, W3dChunkType.VertexMaterialIds, vertexCount, vertexMaterialCount, "vertex material", data, meshName);
        var shaderIds = ReadOptionalIds(
            pass, W3dChunkType.ShaderIds, triangleCount, shaderCount, "shader", data, meshName);
        var diffuseColors = ReadDiffuseColors(pass, vertexCount, data, meshName);
        var stages = new List<W3dTextureStage>();
        foreach (var stage in pass.Children.Where(chunk => chunk.Type == W3dChunkType.TextureStage))
        {
            stages.Add(ReadTextureStage(stage, vertexCount, triangleCount, textureCount, data, meshName));
        }

        return new W3dMaterialPass(vertexMaterialIds, shaderIds, diffuseColors, stages);
    }

    private static IReadOnlyList<uint> ReadOptionalIds(
        W3dChunk parent,
        uint type,
        int expandedCount,
        int tableCount,
        string description,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var chunk = GetSingleChild(parent, type, required: false);
        if (chunk is null)
        {
            return [];
        }

        var ids = ReadUInt32Array(chunk, data, meshName, $"{description} IDs");
        if (ids.Length != 1 && ids.Length != expandedCount)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' has {ids.Length} {description} IDs; expected 1 or {expandedCount}.");
        }

        if (ids.Any(id => id >= tableCount))
        {
            throw new InvalidDataException($"Mesh '{meshName}' contains a {description} ID outside its table.");
        }

        return ids;
    }

    private static IReadOnlyList<W3dRgba> ReadDiffuseColors(
        W3dChunk parent,
        int vertexCount,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var chunk = GetSingleChild(parent, W3dChunkType.DiffuseColors, required: false);
        if (chunk is null)
        {
            return [];
        }

        var expectedSize = checked(vertexCount * 4);
        if (chunk.Size != expectedSize)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' diffuse-color chunk has {chunk.Size} bytes; expected {expectedSize}.");
        }

        var payload = Slice(data, chunk);
        var colors = new W3dRgba[vertexCount];
        for (var index = 0; index < colors.Length; index++)
        {
            var offset = index * 4;
            colors[index] = new W3dRgba(payload[offset], payload[offset + 1], payload[offset + 2], payload[offset + 3]);
        }

        return colors;
    }

    private static W3dTextureStage ReadTextureStage(
        W3dChunk stage,
        int vertexCount,
        int triangleCount,
        int textureCount,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var textureIdChunk = GetSingleChild(stage, W3dChunkType.TextureIds, required: true)!;
        var textureIds = ReadUInt32Array(textureIdChunk, data, meshName, "texture IDs");
        if (textureIds.Length != 1 && textureIds.Length != triangleCount)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' has {textureIds.Length} texture IDs; expected 1 or {triangleCount}.");
        }

        if (textureIds.Any(id => id != uint.MaxValue && id >= textureCount))
        {
            throw new InvalidDataException($"Mesh '{meshName}' contains a texture ID outside its texture table.");
        }

        var coordinatesChunk = GetSingleChild(stage, W3dChunkType.StageTexCoords, required: false);
        Vector2[] coordinates = coordinatesChunk is null
            ? []
            : ReadVector2Array(coordinatesChunk, data, meshName);
        var faceIndicesChunk = GetSingleChild(stage, W3dChunkType.PerFaceTexCoordIds, required: false);
        W3dTriangleIndices[] faceIndices = faceIndicesChunk is null
            ? []
            : ReadTriangleIndices(faceIndicesChunk, triangleCount, coordinates.Length, data, meshName);
        if (faceIndices.Length == 0 && coordinates.Length != 0 && coordinates.Length != vertexCount)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' has {coordinates.Length} stage coordinates without per-face indices; expected {vertexCount}.");
        }

        return new W3dTextureStage(textureIds, coordinates, faceIndices);
    }

    private static uint[] ReadUInt32Array(
        W3dChunk chunk,
        ReadOnlySpan<byte> data,
        string meshName,
        string description)
    {
        if (chunk.Size % sizeof(uint) != 0)
        {
            throw new InvalidDataException($"Mesh '{meshName}' {description} chunk is not uint32-aligned.");
        }

        var payload = Slice(data, chunk);
        var result = new uint[chunk.Size / sizeof(uint)];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = ReadUInt32(payload, index * sizeof(uint));
        }

        return result;
    }

    private static Vector2[] ReadVector2Array(W3dChunk chunk, ReadOnlySpan<byte> data, string meshName)
    {
        const int vector2Size = 8;
        if (chunk.Size % vector2Size != 0)
        {
            throw new InvalidDataException($"Mesh '{meshName}' texture-coordinate chunk is not Vector2-aligned.");
        }

        var payload = Slice(data, chunk);
        var result = new Vector2[chunk.Size / vector2Size];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = new Vector2(ReadSingle(payload, index * vector2Size), ReadSingle(payload, index * vector2Size + 4));
        }

        return result;
    }

    private static W3dTriangleIndices[] ReadTriangleIndices(
        W3dChunk chunk,
        int triangleCount,
        int coordinateCount,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var expectedSize = checked(triangleCount * 12);
        if (chunk.Size != expectedSize)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' per-face texture-coordinate chunk has {chunk.Size} bytes; expected {expectedSize}.");
        }

        var payload = Slice(data, chunk);
        var result = new W3dTriangleIndices[triangleCount];
        for (var index = 0; index < result.Length; index++)
        {
            var offset = index * 12;
            result[index] = new W3dTriangleIndices(
                ReadUInt32(payload, offset),
                ReadUInt32(payload, offset + 4),
                ReadUInt32(payload, offset + 8));
            if (result[index].Index0 >= coordinateCount ||
                result[index].Index1 >= coordinateCount ||
                result[index].Index2 >= coordinateCount)
            {
                throw new InvalidDataException($"Mesh '{meshName}' has a per-face texture-coordinate index out of range.");
            }
        }

        return result;
    }

    private static IReadOnlyList<Vector3> ReadVectors(
        W3dChunk parent,
        uint type,
        int count,
        bool required,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var chunk = GetSingleChild(parent, type, required);
        if (chunk is null)
        {
            return [];
        }

        var expectedSize = checked(count * Vector3Size);
        if (chunk.Size != expectedSize)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' chunk 0x{type:X8} has {chunk.Size} bytes; expected {expectedSize}.");
        }

        var payload = Slice(data, chunk);
        var values = new Vector3[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = ReadVector3(payload, index * Vector3Size);
            ValidateFinite(values[index], $"mesh '{meshName}' vector {index} in chunk 0x{type:X8}");
        }

        return values;
    }

    private static IReadOnlyList<W3dTriangle> ReadTriangles(
        W3dChunk parent,
        int count,
        int vertexCount,
        ReadOnlySpan<byte> data,
        string meshName)
    {
        var chunk = GetSingleChild(parent, W3dChunkType.Triangles, required: count > 0);
        if (chunk is null)
        {
            return [];
        }

        var expectedSize = checked(count * TriangleSize);
        if (chunk.Size != expectedSize)
        {
            throw new InvalidDataException(
                $"Mesh '{meshName}' triangle chunk has {chunk.Size} bytes; expected {expectedSize}.");
        }

        var payload = Slice(data, chunk);
        var triangles = new W3dTriangle[count];
        for (var index = 0; index < count; index++)
        {
            var offset = index * TriangleSize;
            var vertex0 = ReadUInt32(payload, offset);
            var vertex1 = ReadUInt32(payload, offset + 4);
            var vertex2 = ReadUInt32(payload, offset + 8);
            if (vertex0 >= vertexCount || vertex1 >= vertexCount || vertex2 >= vertexCount)
            {
                throw new InvalidDataException(
                    $"Mesh '{meshName}' triangle {index} references a vertex outside 0..{vertexCount - 1}.");
            }

            var normal = ReadVector3(payload, offset + 16);
            var distance = ReadSingle(payload, offset + 28);
            ValidateFinite(normal, $"mesh '{meshName}' triangle {index} normal");
            if (!float.IsFinite(distance))
            {
                throw new InvalidDataException($"Mesh '{meshName}' triangle {index} has invalid plane distance.");
            }

            triangles[index] = new W3dTriangle(
                vertex0,
                vertex1,
                vertex2,
                ReadUInt32(payload, offset + 12),
                normal,
                distance);
        }

        return triangles;
    }

    private static W3dChunk? GetSingleChild(W3dChunk parent, uint type, bool required)
    {
        var matches = parent.Children.Where(chunk => chunk.Type == type).Take(2).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidDataException(
                $"W3D container 0x{parent.Type:X8} at {parent.HeaderOffset} has duplicate chunk 0x{type:X8}.");
        }

        if (matches.Length == 0 && required)
        {
            throw new InvalidDataException(
                $"W3D container 0x{parent.Type:X8} at {parent.HeaderOffset} is missing chunk 0x{type:X8}.");
        }

        return matches.SingleOrDefault();
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, W3dChunk chunk)
        => data.Slice(checked((int)chunk.DataOffset), chunk.Size);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private static float ReadSingle(ReadOnlySpan<byte> data, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data[offset..]));

    private static Vector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
        => new(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8));

    private static W3dRgb ReadRgb(ReadOnlySpan<byte> data, int offset)
        => new(data[offset], data[offset + 1], data[offset + 2]);

    private static string? ReadOptionalString(W3dChunk parent, uint type, ReadOnlySpan<byte> data)
    {
        var chunk = GetSingleChild(parent, type, required: false);
        return chunk is null ? null : ReadFixedString(Slice(data, chunk));
    }

    private static string ReadFixedString(ReadOnlySpan<byte> data)
    {
        var terminator = data.IndexOf((byte)0);
        return Encoding.Latin1.GetString(terminator >= 0 ? data[..terminator] : data).TrimEnd();
    }

    private static int ToCount(uint value, string kind, string meshName)
    {
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"Mesh '{meshName}' {kind} count {value} is too large.");
        }

        return (int)value;
    }

    private static void ValidateFinite(Vector3 value, string description)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new InvalidDataException($"Invalid {description}.");
        }
    }
}
