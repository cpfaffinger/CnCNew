using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using CnCNew.W3D;

using var valid = new MemoryStream();
WriteHeader(valid, 0x100, 11, container: true);
WriteHeader(valid, 0x101, 3, container: false);
valid.Write([1, 2, 3]);
WriteHeader(valid, 0x200, 2, container: false);
valid.Write([4, 5]);

var file = W3dChunkReader.Read(valid);
Assert(file.Length == 29, "File length mismatch.");
Assert(file.Chunks.Count == 2, "Root chunk count mismatch.");
Assert(file.Chunks[0].IsContainer, "Container flag was not decoded.");
Assert(file.Chunks[0].Children.Single().Type == 0x101, "Nested chunk type mismatch.");
Assert(file.AllChunks.Count() == 3, "Recursive chunk count mismatch.");

using var truncated = new MemoryStream();
WriteHeader(truncated, 1, 5, container: false);
truncated.Write([1, 2]);
AssertThrows<InvalidDataException>(() => W3dChunkReader.Read(truncated), "Truncated chunks must fail.");

using var malformedContainer = new MemoryStream();
WriteHeader(malformedContainer, 1, 1, container: true);
malformedContainer.WriteByte(0);
AssertThrows<InvalidDataException>(() => W3dChunkReader.Read(malformedContainer), "Malformed containers must fail.");

using var original = new MemoryStream();
WriteHeader(original, 1, 2, container: false);
original.Write([0x0a, 0xff]);
WriteHeader(original, 2, 1, container: false);
original.WriteByte(0x7f);
var textModeBytes = original.ToArray().SelectMany(value => value == 0x0a ? new byte[] { 0x0d, 0x0a } : [value]).ToArray();
using var textModeDamaged = new MemoryStream(textModeBytes);
var recovered = W3dFileReader.Read(textModeDamaged);
Assert(recovered.RepairedLegacyTextMode, "Text-mode corruption was not reported.");
Assert(recovered.RemovedCarriageReturns == 1, "Removed byte count mismatch.");
Assert(recovered.Data.Length == original.Length, "Recovered byte length mismatch.");
Assert(recovered.File.Chunks.Count == 2, "Recovered file structure mismatch.");

using var meshStream = CreateTriangleMesh();
var meshSource = W3dFileReader.Read(meshStream);
var mesh = W3dMeshDecoder.Decode(meshSource).Single();
Assert(mesh.Name == "TestMesh", "Mesh name mismatch.");
Assert(mesh.Vertices.Count == 3, "Mesh vertex count mismatch.");
Assert(mesh.Triangles.Count == 1, "Mesh triangle count mismatch.");
Assert(mesh.Triangles[0].Vertex2 == 2, "Mesh triangle index mismatch.");
Assert(mesh.Materials.Textures.Single().Name == "test.dds", "Mesh texture name mismatch.");
Assert(mesh.Materials.Shaders.Single().AlphaTestEnabled, "Shader alpha-test state mismatch.");
Assert(mesh.Materials.Shaders[0].SourceBlend == W3dSourceBlend.SourceAlpha, "Shader source blend mismatch.");
Assert(!mesh.Materials.Shaders[0].DepthWriteEnabled, "Shader depth-write state mismatch.");
Assert(mesh.Materials.VertexMaterials.Single().Name == "TestMaterial", "Vertex material name mismatch.");
Assert(mesh.Materials.VertexMaterials[0].Diffuse == new W3dRgb(128, 192, 255), "Vertex material color mismatch.");
Assert(mesh.Materials.VertexMaterials[0].MapperArguments0 == "UPerSec=1", "Vertex mapper arguments mismatch.");
Assert(mesh.Materials.Passes.Single().VertexMaterialIds.Single() == 0, "Vertex material ID mismatch.");
Assert(mesh.Materials.Passes.Single().ShaderIds.Single() == 0, "Shader ID mismatch.");
Assert(mesh.Materials.Passes.Single().DiffuseColors[1] == new W3dRgba(40, 50, 60, 70), "Diffuse vertex color mismatch.");
Assert(mesh.Materials.Passes.Single().TextureStages.Single().TextureIds.Single() == 0, "Mesh texture ID mismatch.");
Assert(mesh.Materials.Passes[0].TextureStages[0].TextureCoordinates.Count == 3, "Mesh UV count mismatch.");
Assert(mesh.MaterialVariants.Single().Kind == W3dMaterialVariantKind.Default, "Default material variant mismatch.");

using var invalidShaderIds = CreateTriangleMesh(shaderId: 1);
AssertThrows<InvalidDataException>(
    () => W3dMeshDecoder.Decode(W3dFileReader.Read(invalidShaderIds)),
    "Out-of-range shader IDs must fail.");

var renderPlanMesh = CreateRenderPlanMesh();
var renderPlan = W3dRenderPlan.Build(renderPlanMesh);
Assert(renderPlan.Count == 2, "Each material pass must produce its own ordered render batch.");
Assert(renderPlan[0].MaterialPassIndex == 0 && renderPlan[1].MaterialPassIndex == 1, "Material pass order changed.");
Assert(renderPlan[0].TextureIds.SequenceEqual(new uint?[] { 0, 1 }), "Both texture stages must be part of the batch key.");
Assert(renderPlan[0].Shader?.AlphaTestEnabled == true, "Alpha-test state was not preserved in the render plan.");
Assert(renderPlan[0].Shader?.SourceBlend == W3dSourceBlend.SourceAlpha, "Source blend was not preserved.");
Assert(renderPlan[0].Shader?.DestinationBlend == W3dDestinationBlend.OneMinusSourceAlpha, "Destination blend was not preserved.");
Assert(renderPlan[0].Shader?.DepthWriteEnabled == false, "Depth-write state was not preserved.");
Assert(renderPlan[0].DetailColorFunction == 2 && renderPlan[0].DetailAlphaFunction == 3,
    "The effective detail functions must come from the legacy Detail fields.");
Assert(renderPlanMesh.IsTwoSided, "Two-sided mesh state was not preserved.");
var plannedColor = W3dRenderPlan.GetVertexColor(renderPlanMesh, renderPlan[0].MaterialPass, 0);
Assert(plannedColor == new W3dRgba(64, 96, 128, 50), "DCG and vertex-material color/opacity were not combined correctly.");
var plannedPass = renderPlan[0].MaterialPass!;
var plannedUv0 = W3dRenderPlan.GetTextureCoordinate(plannedPass.TextureStages[0], 0, 0, 0);
var plannedUv1 = W3dRenderPlan.GetTextureCoordinate(plannedPass.TextureStages[1], 0, 0, 0);
Assert(plannedUv0 == new Vector2(0.25f, 0.25f), "Stage-zero V coordinate was not flipped.");
Assert(plannedUv1 == new Vector2(0.9f, 0.2f), "Per-face stage-one UV selection or V flip failed.");

var contentArgument = Array.FindIndex(args, value => value.Equals("--content-dir", StringComparison.OrdinalIgnoreCase));
if (contentArgument >= 0)
{
    Assert(contentArgument + 1 < args.Length, "--content-dir requires a path.");
    RunRealAssetRenderPlanTests(args[contentArgument + 1]);
}

Console.WriteLine("All W3D chunk reader tests passed.");
return 0;

static void WriteHeader(Stream stream, uint type, uint size, bool container)
{
    Span<byte> header = stackalloc byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(header, type);
    BinaryPrimitives.WriteUInt32LittleEndian(header[4..], size | (container ? 0x80000000u : 0));
    stream.Write(header);
}

static MemoryStream CreateTriangleMesh(uint shaderId = 0)
{
    var body = new MemoryStream();

    WriteHeader(body, W3dChunkType.MeshHeader3, 116, container: false);
    var header = new byte[116];
    BinaryPrimitives.WriteUInt32LittleEndian(header, 0x00040002);
    Encoding.ASCII.GetBytes("TestMesh").CopyTo(header, 8);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), 3);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68), 3);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72), 1);
    WriteVectorToSpan(header, 76, Vector3.Zero);
    WriteVectorToSpan(header, 88, Vector3.One);
    WriteVectorToSpan(header, 100, new Vector3(0.5f));
    WriteSingle(header, 112, 1);
    body.Write(header);

    WriteHeader(body, W3dChunkType.Vertices, 36, container: false);
    WriteVector(body, Vector3.Zero);
    WriteVector(body, Vector3.UnitX);
    WriteVector(body, Vector3.UnitY);

    WriteHeader(body, W3dChunkType.VertexNormals, 36, container: false);
    WriteVector(body, Vector3.UnitZ);
    WriteVector(body, Vector3.UnitZ);
    WriteVector(body, Vector3.UnitZ);

    WriteHeader(body, W3dChunkType.Triangles, 32, container: false);
    Span<byte> triangle = stackalloc byte[32];
    BinaryPrimitives.WriteUInt32LittleEndian(triangle, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(triangle[4..], 1);
    BinaryPrimitives.WriteUInt32LittleEndian(triangle[8..], 2);
    WriteVectorToSpan(triangle, 16, Vector3.UnitZ);
    body.Write(triangle);

    WriteHeader(body, W3dChunkType.MaterialInfo, 16, container: false);
    Span<byte> materialInfo = stackalloc byte[16];
    BinaryPrimitives.WriteUInt32LittleEndian(materialInfo, 1);
    BinaryPrimitives.WriteUInt32LittleEndian(materialInfo[4..], 1);
    BinaryPrimitives.WriteUInt32LittleEndian(materialInfo[8..], 1);
    BinaryPrimitives.WriteUInt32LittleEndian(materialInfo[12..], 1);
    body.Write(materialInfo);

    WriteHeader(body, W3dChunkType.Shaders, 16, container: false);
    body.Write([3, 0, 0, 5, 0, 1, 0, 2, 1, 0, 0, 0, 1, 0, 0, 0]);

    WriteContainer(body, W3dChunkType.VertexMaterials, materials =>
        WriteContainer(materials, W3dChunkType.VertexMaterial, material =>
        {
            WriteHeader(material, W3dChunkType.VertexMaterialName, 13, container: false);
            material.Write(Encoding.ASCII.GetBytes("TestMaterial\0"));
            WriteHeader(material, W3dChunkType.VertexMaterialInfo, 32, container: false);
            var vertexMaterial = new byte[32];
            vertexMaterial[4] = 255;
            vertexMaterial[5] = 255;
            vertexMaterial[6] = 255;
            vertexMaterial[8] = 128;
            vertexMaterial[9] = 192;
            vertexMaterial[10] = 255;
            WriteSingle(vertexMaterial, 20, 8);
            WriteSingle(vertexMaterial, 24, 0.75f);
            material.Write(vertexMaterial);
            WriteHeader(material, W3dChunkType.VertexMapperArgs0, 10, container: false);
            material.Write(Encoding.ASCII.GetBytes("UPerSec=1\0"));
        }));

    WriteContainer(body, W3dChunkType.Textures, textures =>
        WriteContainer(textures, W3dChunkType.Texture, texture =>
        {
            WriteHeader(texture, W3dChunkType.TextureName, 9, container: false);
            texture.Write(Encoding.ASCII.GetBytes("test.dds\0"));
        }));

    WriteContainer(body, W3dChunkType.MaterialPass, pass =>
    {
        WriteUInt32Chunk(pass, W3dChunkType.VertexMaterialIds, 0);
        WriteUInt32Chunk(pass, W3dChunkType.ShaderIds, shaderId);
        WriteHeader(pass, W3dChunkType.DiffuseColors, 12, container: false);
        pass.Write([10, 20, 30, 255, 40, 50, 60, 70, 80, 90, 100, 110]);
        WriteContainer(pass, W3dChunkType.TextureStage, stage =>
        {
            WriteUInt32Chunk(stage, W3dChunkType.TextureIds, 0);
            WriteHeader(stage, W3dChunkType.StageTexCoords, 24, container: false);
            WriteVector2(stage, Vector2.Zero);
            WriteVector2(stage, Vector2.UnitX);
            WriteVector2(stage, Vector2.UnitY);
        });
    });

    var stream = new MemoryStream();
    WriteHeader(stream, W3dChunkType.Mesh, checked((uint)body.Length), container: true);
    body.Position = 0;
    body.CopyTo(stream);
    stream.Position = 0;
    return stream;
}

static W3dMesh CreateRenderPlanMesh()
{
    var shader0 = new W3dShader(
        W3dDepthCompare.LessOrEqual,
        false,
        0,
        W3dDestinationBlend.OneMinusSourceAlpha,
        0,
        W3dPrimaryGradient.Modulate,
        0,
        W3dSourceBlend.SourceAlpha,
        true,
        2,
        3,
        0,
        true,
        7,
        1,
        0);
    var shader1 = shader0 with
    {
        DepthWriteEnabled = true,
        DestinationBlend = W3dDestinationBlend.Zero,
        SourceBlend = W3dSourceBlend.One,
        AlphaTestEnabled = false,
        DetailColorFunction = 0,
        DetailAlphaFunction = 0,
    };
    var vertexMaterial = new W3dVertexMaterial(
        "PlanMaterial",
        0,
        new W3dRgb(255, 255, 255),
        new W3dRgb(128, 192, 255),
        new W3dRgb(0, 0, 0),
        new W3dRgb(0, 0, 0),
        0,
        0.5f,
        0,
        null,
        null);
    var stage0 = new W3dTextureStage(
        [0],
        [new Vector2(0.25f, 0.75f), Vector2.UnitX, Vector2.UnitY],
        []);
    var stage1 = new W3dTextureStage(
        [1],
        [new Vector2(0.1f, 0.2f), new Vector2(0.9f, 0.8f), new Vector2(0.3f, 0.4f)],
        [new W3dTriangleIndices(1, 2, 0)]);
    var pass0 = new W3dMaterialPass(
        [0],
        [0],
        [new W3dRgba(128, 128, 128, 101), new W3dRgba(255, 255, 255, 255), new W3dRgba(255, 255, 255, 255)],
        [stage0, stage1]);
    var pass1 = new W3dMaterialPass([0], [1], [], []);
    var materials = new W3dMaterialData(
        2,
        1,
        2,
        2,
        [shader0, shader1],
        [vertexMaterial],
        [new W3dTextureReference("base.dds"), new W3dTextureReference("detail.dds")],
        [pass0, pass1]);

    return new W3dMesh(
        "PlanMesh",
        "",
        0x00040002,
        0x00002000,
        1,
        0,
        0,
        0,
        Vector3.Zero,
        Vector3.One,
        new Vector3(0.5f),
        1,
        [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
        [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        [new W3dTriangle(0, 1, 2, 0, Vector3.UnitZ, 0)],
        materials,
        [new W3dMaterialVariant(W3dMaterialVariantKind.Default, materials)]);
}

static void RunRealAssetRenderPlanTests(string contentDirectory)
{
    var expectations = new (string Name, int Meshes, int Batches, int EmittedTriangles)[]
    {
        ("ABPrison_A4.W3D", 2, 12, 531),
        ("ABArFrcCmd.W3D", 21, 32, 873),
        ("AVSoloDrn.W3D", 8, 8, 308),
        ("AvMnt_A.W3D", 39, 40, 5388),
        ("AICIAOff_SKN.W3D", 2, 3, 156),
    };

    foreach (var expected in expectations)
    {
        var path = Path.Combine(contentDirectory, "Art", "W3D", expected.Name);
        using var stream = File.OpenRead(path);
        var meshes = W3dMeshDecoder.Decode(W3dFileReader.Read(stream));
        var plans = meshes.Select(W3dRenderPlan.Build).ToArray();
        Assert(meshes.Count == expected.Meshes, $"{expected.Name} mesh count changed.");
        Assert(plans.Sum(plan => plan.Count) == expected.Batches, $"{expected.Name} render batch count changed.");
        Assert(plans.Sum(plan => plan.Sum(batch => batch.TriangleIndices.Count)) == expected.EmittedTriangles,
            $"{expected.Name} emitted triangle count changed.");
    }

    using (var stream = File.OpenRead(Path.Combine(contentDirectory, "Art", "W3D", "AVSoloDrn.W3D")))
    {
        var object01 = W3dMeshDecoder.Decode(W3dFileReader.Read(stream)).Single(mesh => mesh.Name == "OBJECT01");
        var batch = W3dRenderPlan.Build(object01).Single();
        Assert(batch.TextureIds.SequenceEqual(new uint?[] { 0, 1 }), "AVSoloDrn OBJECT01 lost its two ordered stages.");
        Assert(batch.DetailColorFunction == 2, "AVSoloDrn OBJECT01 detail-scale function changed.");
        Assert(object01.Materials.VertexMaterials.Single().MapperArguments1 == "VPerSec=-1.5",
            "AVSoloDrn OBJECT01 second mapper arguments changed.");
    }

    using (var stream = File.OpenRead(Path.Combine(contentDirectory, "Art", "W3D", "AICIAOff_SKN.W3D")))
    {
        var muzzle = W3dMeshDecoder.Decode(W3dFileReader.Read(stream)).Single(mesh => mesh.Name == "MUZZLEFX01");
        var color = W3dRenderPlan.GetVertexColor(muzzle, muzzle.Materials.Passes.Single(), 0);
        Assert(color.Alpha == 0, "AICIAOff muzzle opacity-zero material must produce zero vertex alpha.");
    }
}

static void WriteContainer(Stream stream, uint type, Action<MemoryStream> writePayload)
{
    using var payload = new MemoryStream();
    writePayload(payload);
    WriteHeader(stream, type, checked((uint)payload.Length), container: true);
    payload.Position = 0;
    payload.CopyTo(stream);
}

static void WriteUInt32Chunk(Stream stream, uint type, uint value)
{
    WriteHeader(stream, type, 4, container: false);
    Span<byte> bytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
    stream.Write(bytes);
}

static void WriteVector(Stream stream, Vector3 value)
{
    Span<byte> bytes = stackalloc byte[12];
    WriteVectorToSpan(bytes, 0, value);
    stream.Write(bytes);
}

static void WriteVector2(Stream stream, Vector2 value)
{
    Span<byte> bytes = stackalloc byte[8];
    WriteSingle(bytes, 0, value.X);
    WriteSingle(bytes, 4, value.Y);
    stream.Write(bytes);
}

static void WriteVectorToSpan(Span<byte> bytes, int offset, Vector3 value)
{
    WriteSingle(bytes, offset, value.X);
    WriteSingle(bytes, offset + 4, value.Y);
    WriteSingle(bytes, offset + 8, value.Z);
}

static void WriteSingle(Span<byte> bytes, int offset, float value)
    => BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], BitConverter.SingleToInt32Bits(value));

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<T>(Action action, string message) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
