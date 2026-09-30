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
Assert(mesh.Materials.VertexMaterials[0].Mapper0 is W3dLinearOffsetTextureMapper,
    "Vertex mapper was not parsed into a typed linear-offset mapper.");
Assert(mesh.Materials.Passes.Single().VertexMaterialIds.Single() == 0, "Vertex material ID mismatch.");
Assert(mesh.Materials.Passes.Single().ShaderIds.Single() == 0, "Shader ID mismatch.");
Assert(mesh.Materials.Passes.Single().DiffuseColors[1] == new W3dRgba(40, 50, 60, 70), "Diffuse vertex color mismatch.");
Assert(mesh.Materials.Passes.Single().DiffuseIllumination[1] == new W3dRgb(4, 5, 6),
    "Diffuse illumination color mismatch.");
Assert(mesh.Materials.Passes.Single().SpecularColors[2] == new W3dRgb(17, 18, 19),
    "Secondary/specular gradient color mismatch.");
Assert(mesh.Materials.Passes.Single().TextureStages.Single().TextureIds.Single() == 0, "Mesh texture ID mismatch.");
Assert(mesh.Materials.Passes[0].TextureStages[0].TextureCoordinates.Count == 3, "Mesh UV count mismatch.");
Assert(mesh.MaterialVariants.Single().Kind == W3dMaterialVariantKind.Default, "Default material variant mismatch.");

using var invalidShaderIds = CreateTriangleMesh(shaderId: 1);
AssertThrows<InvalidDataException>(
    () => W3dMeshDecoder.Decode(W3dFileReader.Read(invalidShaderIds)),
    "Out-of-range shader IDs must fail.");
using var invalidShaderState = CreateTriangleMesh(detailColor: 13);
AssertThrows<InvalidDataException>(
    () => W3dMeshDecoder.Decode(W3dFileReader.Read(invalidShaderState)),
    "Unknown shader function values must fail with a precise decode error.");

var renderPlanMesh = CreateRenderPlanMesh();
var renderPlan = W3dRenderPlan.Build(renderPlanMesh);
Assert(renderPlan.Count == 1, "A triangle's complete pass sequence must stay in one render batch.");
Assert(renderPlan[0].Passes.Select(pass => pass.MaterialPassIndex).SequenceEqual([0, 1]), "Material pass order changed.");
var plannedRenderPass = renderPlan[0].Passes[0];
Assert(plannedRenderPass.TextureIds.SequenceEqual(new uint?[] { 0, 1 }), "Both texture stages must be part of the batch key.");
Assert(plannedRenderPass.Shader?.AlphaTestEnabled == true, "Alpha-test state was not preserved in the render plan.");
Assert(plannedRenderPass.Shader?.SourceBlend == W3dSourceBlend.SourceAlpha, "Source blend was not preserved.");
Assert(plannedRenderPass.Shader?.DestinationBlend == W3dDestinationBlend.OneMinusSourceAlpha, "Destination blend was not preserved.");
Assert(plannedRenderPass.Shader?.DepthWriteEnabled == false, "Depth-write state was not preserved.");
Assert(plannedRenderPass.EffectiveDetailColorFunction == W3dDetailColorFunction.Scale &&
       plannedRenderPass.EffectiveDetailAlphaFunction == W3dDetailAlphaFunction.InverseScale,
    "The effective detail functions must come from the legacy Detail fields.");
Assert(renderPlanMesh.IsTwoSided, "Two-sided mesh state was not preserved.");
var plannedColor = W3dRenderPlan.GetVertexColor(renderPlanMesh, plannedRenderPass.MaterialPass, 0);
Assert(plannedColor == new W3dRgba(64, 96, 128, 50), "DCG and vertex-material color/opacity were not combined correctly.");
var plannedPass = plannedRenderPass.MaterialPass;
var plannedUv0 = W3dRenderPlan.GetTextureCoordinate(plannedPass.TextureStages[0], 0, 0, 0);
AssertNear(plannedUv0, new Vector2(0.25f, 0.25f), 1e-6f, "Stage-zero V coordinate was not flipped.");
AssertNear(W3dRenderPlan.GetTextureCoordinate(plannedPass.TextureStages[1], 0, 0, 0), new Vector2(0.9f, 0.2f), 1e-6f,
    "Per-face stage-one first-corner UV selection or V flip failed.");
AssertNear(W3dRenderPlan.GetTextureCoordinate(plannedPass.TextureStages[1], 0, 1, 1), new Vector2(0.3f, 0.6f), 1e-6f,
    "Per-face stage-one second-corner UV selection or V flip failed.");
AssertNear(W3dRenderPlan.GetTextureCoordinate(plannedPass.TextureStages[1], 0, 2, 2), new Vector2(0.1f, 0.8f), 1e-6f,
    "Per-face stage-one third-corner UV selection or V flip failed.");
AssertThrows<ArgumentOutOfRangeException>(
    () => W3dRenderPlan.GetTextureCoordinate(plannedPass.TextureStages[1], 0, 3, 0),
    "Texture-coordinate corners outside 0..2 must fail.");

RunMapperTests();
RunMaterialFunctionTests();
RunTextureResolutionTests();
SkeletonTests.RunSynthetic();

var contentArgument = Array.FindIndex(args, value => value.Equals("--content-dir", StringComparison.OrdinalIgnoreCase));
if (contentArgument >= 0)
{
    Assert(contentArgument + 1 < args.Length, "--content-dir requires a path.");
    RunRealAssetRenderPlanTests(args[contentArgument + 1]);
    SkeletonTests.RunRealData(args[contentArgument + 1]);
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

static MemoryStream CreateTriangleMesh(uint shaderId = 0, byte detailColor = 0)
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
    body.Write([3, 0, 0, 5, 0, 1, 0, 2, 1, detailColor, 0, 0, 1, 0, 0, 0]);

    WriteContainer(body, W3dChunkType.VertexMaterials, materials =>
        WriteContainer(materials, W3dChunkType.VertexMaterial, material =>
        {
            WriteHeader(material, W3dChunkType.VertexMaterialName, 13, container: false);
            material.Write(Encoding.ASCII.GetBytes("TestMaterial\0"));
            WriteHeader(material, W3dChunkType.VertexMaterialInfo, 32, container: false);
            var vertexMaterial = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(vertexMaterial, 0x00040000);
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
        WriteHeader(pass, W3dChunkType.DiffuseIllumination, 9, container: false);
        pass.Write([1, 2, 3, 4, 5, 6, 7, 8, 9]);
        WriteHeader(pass, W3dChunkType.SpecularColors, 9, container: false);
        pass.Write([11, 12, 13, 14, 15, 16, 17, 18, 19]);
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
        W3dSecondaryGradient.Disabled,
        W3dSourceBlend.SourceAlpha,
        true,
        W3dDetailColorFunction.Scale,
        W3dDetailAlphaFunction.InverseScale,
        0,
        true,
        W3dDetailColorFunction.Blend,
        W3dDetailAlphaFunction.Detail,
        0);
    var shader1 = shader0 with
    {
        DepthWriteEnabled = true,
        DestinationBlend = W3dDestinationBlend.Zero,
        SourceBlend = W3dSourceBlend.One,
        AlphaTestEnabled = false,
        DetailColorFunction = W3dDetailColorFunction.Disabled,
        DetailAlphaFunction = W3dDetailAlphaFunction.Disabled,
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
        null,
        W3dTextureMapper.Uv,
        W3dTextureMapper.Uv);
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
        [],
        [new W3dRgb(10, 20, 30), new W3dRgb(0, 0, 0), new W3dRgb(0, 0, 0)],
        [stage0, stage1]);
    var pass1 = new W3dMaterialPass([0], [1], [], [], [], []);
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
        Assert(plans.Sum(plan => plan.Count) > 0, $"{expected.Name} produced no render batches.");
        Assert(plans.Sum(plan => plan.Sum(batch => batch.TriangleIndices.Count)) == meshes.Sum(mesh => mesh.Triangles.Count),
            $"{expected.Name} emitted triangle count changed.");
        Assert(plans.SelectMany(plan => plan).All(batch => batch.Passes.Select(pass => pass.MaterialPassIndex).SequenceEqual(
                Enumerable.Range(0, batch.Passes.Count))),
            $"{expected.Name} material pass order changed.");
    }

    using (var stream = File.OpenRead(Path.Combine(contentDirectory, "Art", "W3D", "AVSoloDrn.W3D")))
    {
        var object01 = W3dMeshDecoder.Decode(W3dFileReader.Read(stream)).Single(mesh => mesh.Name == "OBJECT01");
        var batch = W3dRenderPlan.Build(object01).Single();
        Assert(batch.Passes.Single().TextureIds.SequenceEqual(new uint?[] { 0, 1 }), "AVSoloDrn OBJECT01 lost its two ordered stages.");
        Assert(batch.Passes.Single().EffectiveDetailColorFunction == W3dDetailColorFunction.Scale,
            "AVSoloDrn OBJECT01 detail-scale function changed.");
        Assert(object01.Materials.VertexMaterials.Single().MapperArguments1 == "VPerSec=-1.5",
            "AVSoloDrn OBJECT01 second mapper arguments changed.");
        Assert(object01.Materials.VertexMaterials.Single().Mapper1 is W3dLinearOffsetTextureMapper,
            "AVSoloDrn OBJECT01 stage-one mapper was not typed.");
    }

    using (var stream = File.OpenRead(Path.Combine(contentDirectory, "Art", "W3D", "AICIAOff_SKN.W3D")))
    {
        var muzzle = W3dMeshDecoder.Decode(W3dFileReader.Read(stream)).Single(mesh => mesh.Name == "MUZZLEFX01");
        var color = W3dRenderPlan.GetVertexColor(muzzle, muzzle.Materials.Passes.Single(), 0);
        Assert(color.Alpha == 0, "AICIAOff muzzle opacity-zero material must produce zero vertex alpha.");
    }
}

static void RunMapperTests()
{
    var positive = (W3dLinearOffsetTextureMapper)W3dTextureMapperParser.Parse(
        0x00040000, 0, "UPerSec=2\nVPerSec=-1\nUScale=0.5\nVScale=2");
    var positiveTransform = W3dTextureMapperEvaluator.Evaluate(positive, 0.25);
    AssertNear(positiveTransform.Scale, new Vector2(0.5f, 2), 1e-6f, "Linear mapper scale changed.");
    AssertNear(positiveTransform.Offset, new Vector2(0.5f, 0.25f), 1e-6f,
        "Positive/negative linear mapper speeds changed.");

    var stageOne = (W3dLinearOffsetTextureMapper)W3dTextureMapperParser.Parse(0x00000400, 1, "VPerSec=-1.5");
    AssertNear(W3dTextureMapperEvaluator.Evaluate(stageOne, 2).Offset, new Vector2(0, 0), 1e-6f,
        "Stage-one mapper wrap changed.");

    var grid = (W3dGridTextureMapper)W3dTextureMapperParser.Parse(
        0x00070000, 0, "FPS=30; shipped comment\nLog1Width=1; shipped spelling\nLast=4");
    AssertNear(W3dTextureMapperEvaluator.Evaluate(grid, 0.1).Offset, new Vector2(0.5f, 0.5f), 1e-6f,
        "Grid mapper frame selection changed.");

    Assert(W3dTextureMapperParser.Parse(0x00010000, 0, null) is W3dEnvironmentTextureMapper { Reflection: true },
        "Environment mapper kind changed.");
    Assert(W3dTextureMapperParser.Parse(0x00020000, 0, null) is W3dEnvironmentTextureMapper { Reflection: false },
        "Classic environment mapper kind changed.");
    AssertThrows<InvalidDataException>(
        () => W3dTextureMapperParser.Parse(0x00040000, 0, "UPerSec=fast"),
        "Invalid mapper floats must fail.");
    AssertThrows<InvalidDataException>(
        () => W3dTextureMapperParser.Parse(0x00040000, 0, "Unknown=1"),
        "Unknown mapper arguments must fail.");
    AssertThrows<InvalidDataException>(
        () => W3dTextureMapperParser.Parse(0x00ff0000, 0, null),
        "Unknown mapper kinds must fail.");
}

static void RunMaterialFunctionTests()
{
    var local = new Vector4(0.2f, 0.4f, 0.6f, 0.25f);
    var detail = new Vector4(0.5f, 0.25f, 0.75f, 0.5f);
    AssertNear4(W3dMaterialEvaluator.ApplyDetail(
            local, detail, W3dDetailColorFunction.Scale2X, W3dDetailAlphaFunction.InverseScale),
        new Vector4(0.2f, 0.2f, 0.9f, 0.625f), 1e-6f, "Detail Scale2X/inverse-alpha changed.");
    AssertNear4(W3dMaterialEvaluator.ApplyDetail(
            local, detail, W3dDetailColorFunction.Subtract, W3dDetailAlphaFunction.Detail),
        new Vector4(0, 0.15f, 0, 0.5f), 1e-6f, "Detail subtract semantics changed.");
    AssertNear4(W3dMaterialEvaluator.ApplyPrimaryGradient(
            local, detail, W3dPrimaryGradient.Add),
        new Vector4(0.7f, 0.65f, 1, 0.5f), 1e-6f, "Primary additive gradient changed.");
    AssertNear4(W3dMaterialEvaluator.ApplyPrimaryGradient(
            local, detail, W3dPrimaryGradient.Modulate2X),
        new Vector4(0.2f, 0.2f, 0.9f, 0.125f), 1e-6f, "Primary Modulate2X gradient changed.");
    AssertThrows<InvalidDataException>(
        () => W3dMaterialEvaluator.ApplyPrimaryGradient(local, detail, W3dPrimaryGradient.BumpEnvironmentMap),
        "Bump gradients without bump inputs must fail explicitly.");

    var metadataShader = CreateRenderPlanMesh().Materials.Shaders[0] with
    {
        ColorMask = 2,
        FogFunction = 2,
        ShaderPreset = 2,
        Padding = 2,
    };
    Assert(metadataShader.PcColorWriteEnabled && metadataShader.PcFogFunction == W3dFogFunction.Disabled,
        "Legacy PS2 parameter bytes must not become PC color-mask or fog state.");
}

static void RunTextureResolutionTests()
{
    var probes = 0;
    var cache = new W3dTexturePathCache(path =>
    {
        probes++;
        return path.Equals("Art/Textures/Test.dds", StringComparison.OrdinalIgnoreCase);
    });
    var first = cache.Resolve("test.tga");
    var second = cache.Resolve("TEST.TGA");
    Assert(first.VirtualPath == "Art/Textures/test.dds", "DDS fallback path or case-insensitive resolution changed.");
    Assert(ReferenceEquals(first, second) && cache.Count == 1, "Texture resolution cache did not reuse its entry.");
    Assert(probes == 3, "Texture resolution cache performed unexpected repeated probes.");
    var missing = W3dTexturePathResolver.Resolve("missing.tga", _ => false);
    Assert(!missing.Found && missing.Diagnostic.Contains("missing.tga", StringComparison.Ordinal),
        "Missing texture diagnostics changed.");
    cache.Clear();
    Assert(cache.Count == 0, "Texture resolution cache clear changed.");
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

static void AssertNear(Vector2 actual, Vector2 expected, float tolerance, string message)
{
    if (Vector2.Distance(actual, expected) > tolerance)
    {
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual} (tolerance {tolerance}).");
    }
}

static void AssertNear4(Vector4 actual, Vector4 expected, float tolerance, string message)
{
    if (Vector4.Distance(actual, expected) > tolerance)
    {
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual} (tolerance {tolerance}).");
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
