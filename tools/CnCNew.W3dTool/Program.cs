using CnCNew.Content;
using CnCNew.W3D;

if (args is ["inspect", var inspectContentDirectory, var virtualPath])
{
    try
    {
        var content = ContentFileSystem.Open(inspectContentDirectory);
        using var stream = content.OpenRead(virtualPath);
        var result = W3dFileReader.Read(stream);
        var meshes = W3dMeshDecoder.Decode(result);
        Console.WriteLine(
            $"{virtualPath}: {result.File.AllChunks.Count()} chunks, {meshes.Count} meshes, " +
            $"repaired={result.RepairedLegacyTextMode}");
        foreach (var mesh in meshes)
        {
            Console.WriteLine(
                $"  {mesh.ContainerName}.{mesh.Name}: {mesh.Vertices.Count} vertices, " +
                $"{mesh.Triangles.Count} triangles, {mesh.MaterialCount} materials, " +
                $"bounds=({mesh.BoundsMinimum})..({mesh.BoundsMaximum})");
            if (mesh.Materials.Textures.Count > 0)
            {
                Console.WriteLine($"    textures: {string.Join(", ", mesh.Materials.Textures.Select(texture => texture.Name))}");
            }
        }

        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"error: {exception.Message}");
        return 1;
    }
}

if (args is not ["audit", var contentDirectory])
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  CnCNew.W3dTool audit <content-directory>");
    Console.Error.WriteLine("  CnCNew.W3dTool inspect <content-directory> <virtual-w3d-path>");
    return 2;
}

try
{
    var content = ContentFileSystem.Open(contentDirectory);
    var paths = content.EnumerateFiles()
        .Where(path => path.EndsWith(".w3d", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var failures = new List<string>();
    var typeCounts = new Dictionary<uint, long>();
    long chunks = 0;
    long containers = 0;
    long meshes = 0;
    long vertices = 0;
    long triangles = 0;
    long textureReferences = 0;
    long materialPasses = 0;
    long textureStages = 0;
    long shaders = 0;
    long vertexMaterials = 0;
    long vertexMaterialIds = 0;
    long shaderIds = 0;
    long diffuseColors = 0;
    long diffuseIllumination = 0;
    long specularColors = 0;
    long materialVariants = 0;
    long twoSidedMeshes = 0;
    var passCountCounts = new Dictionary<int, long>();
    var stageCountCounts = new Dictionary<int, long>();
    var shaderStateCounts = new Dictionary<string, long>(StringComparer.Ordinal);
    var mapperArgumentCounts = new Dictionary<string, long>(StringComparer.Ordinal);
    var mapperKindCounts = new Dictionary<string, long>(StringComparer.Ordinal);
    var passSequenceCounts = new Dictionary<string, long>(StringComparer.Ordinal);
    var featureExamples = new Dictionary<string, string>(StringComparer.Ordinal);
    var repairedFiles = new List<string>();

    foreach (var path in paths)
    {
        try
        {
            using var stream = content.OpenRead(path);
            var result = W3dFileReader.Read(stream);
            if (result.RepairedLegacyTextMode)
            {
                repairedFiles.Add($"{path} ({result.RemovedCarriageReturns} inserted CR bytes removed)");
            }

            foreach (var chunk in result.File.AllChunks)
            {
                chunks++;
                containers += chunk.IsContainer ? 1 : 0;
                typeCounts[chunk.Type] = typeCounts.GetValueOrDefault(chunk.Type) + 1;
            }

            foreach (var mesh in W3dMeshDecoder.Decode(result))
            {
                meshes++;
                vertices += mesh.Vertices.Count;
                triangles += mesh.Triangles.Count;
                textureReferences += mesh.Materials.Textures.Count;
                materialPasses += mesh.Materials.Passes.Count;
                textureStages += mesh.Materials.Passes.Sum(pass => pass.TextureStages.Count);
                shaders += mesh.Materials.Shaders.Count;
                vertexMaterials += mesh.Materials.VertexMaterials.Count;
                vertexMaterialIds += mesh.Materials.Passes.Sum(pass => pass.VertexMaterialIds.Count);
                shaderIds += mesh.Materials.Passes.Sum(pass => pass.ShaderIds.Count);
                diffuseColors += mesh.Materials.Passes.Sum(pass => pass.DiffuseColors.Count);
                diffuseIllumination += mesh.Materials.Passes.Sum(pass => pass.DiffuseIllumination.Count);
                specularColors += mesh.Materials.Passes.Sum(pass => pass.SpecularColors.Count);
                materialVariants += mesh.MaterialVariants.Count;
                twoSidedMeshes += mesh.IsTwoSided ? 1 : 0;
                if (mesh.IsTwoSided)
                {
                    featureExamples.TryAdd("two-sided", path);
                }
                Increment(passCountCounts, mesh.Materials.Passes.Count);
                foreach (var pass in mesh.Materials.Passes)
                {
                    Increment(stageCountCounts, pass.TextureStages.Count);
                    if (mesh.Materials.Passes.Count > 1)
                    {
                        featureExamples.TryAdd("multiple-passes", path);
                    }

                    if (pass.TextureStages.Count > 1)
                    {
                        featureExamples.TryAdd($"{pass.TextureStages.Count}-stages", path);
                    }

                    if (pass.TextureStages.Any(stage => stage.PerFaceTextureCoordinateIndices.Count > 0))
                    {
                        featureExamples.TryAdd("per-face-uv", path);
                    }
                }

                if (mesh.Materials.Passes.Count > 1)
                {
                    Increment(passSequenceCounts, string.Join(" -> ", mesh.Materials.Passes.Select((pass, passIndex) =>
                    {
                        var shaderId = pass.ShaderIds.Count == 0 ? (uint?)null : pass.ShaderIds[0];
                        var shader = shaderId is uint id ? mesh.Materials.Shaders[checked((int)id)] : null;
                        return $"p{passIndex}[{shader?.SourceBlend}/{shader?.DestinationBlend},write={shader?.DepthWriteEnabled},stages={pass.TextureStages.Count}]";
                    })));
                }

                foreach (var shader in mesh.Materials.Shaders)
                {
                    if (shader.AlphaTestEnabled)
                    {
                        featureExamples.TryAdd("alpha-test", path);
                    }

                    if (shader.SourceBlend == W3dSourceBlend.One && shader.DestinationBlend == W3dDestinationBlend.One)
                    {
                        featureExamples.TryAdd("additive", path);
                    }

                    if (shader.DetailColorFunction != W3dDetailColorFunction.Disabled ||
                        shader.DetailAlphaFunction != W3dDetailAlphaFunction.Disabled)
                    {
                        featureExamples.TryAdd("detail-function", path);
                    }
                    Increment(shaderStateCounts,
                        $"depth={shader.DepthCompare};write={shader.DepthWriteEnabled};mask={shader.ColorMask};" +
                        $"dst={shader.DestinationBlend};fog={shader.FogFunction};primary={shader.PrimaryGradient};" +
                        $"secondary={shader.SecondaryGradient};src={shader.SourceBlend};textured={shader.TexturingEnabled};" +
                        $"detailColor={shader.DetailColorFunction};detailAlpha={shader.DetailAlphaFunction};" +
                        $"preset={shader.ShaderPreset};alphaTest={shader.AlphaTestEnabled};" +
                        $"postColor={shader.PostDetailColorFunction};postAlpha={shader.PostDetailAlphaFunction};pad={shader.Padding}");
                }

                foreach (var material in mesh.Materials.VertexMaterials)
                {
                    Increment(mapperKindCounts, $"slot0:{(material.Attributes >> 16) & 0xff}");
                    Increment(mapperKindCounts, $"slot1:{(material.Attributes >> 8) & 0xff}");
                    if (material.Mapper0 is W3dGridTextureMapper || material.Mapper1 is W3dGridTextureMapper)
                    {
                        featureExamples.TryAdd("grid-mapper", path);
                    }

                    if (material.Mapper0 is W3dEnvironmentTextureMapper || material.Mapper1 is W3dEnvironmentTextureMapper)
                    {
                        featureExamples.TryAdd("environment-mapper", path);
                    }
                    if (!string.IsNullOrWhiteSpace(material.MapperArguments0))
                    {
                        Increment(mapperArgumentCounts, $"slot0:{material.MapperArguments0}");
                        featureExamples.TryAdd("mapper", path);
                    }

                    if (!string.IsNullOrWhiteSpace(material.MapperArguments1))
                    {
                        Increment(mapperArgumentCounts, $"slot1:{material.MapperArguments1}");
                        featureExamples.TryAdd("mapper-slot1", path);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            failures.Add($"{path}: {exception.Message}");
        }
    }

    Console.WriteLine(
        $"Parsed {paths.Length - failures.Count}/{paths.Length} W3D files, " +
        $"{chunks} chunks, {containers} containers, {typeCounts.Count} distinct chunk types, " +
        $"{repairedFiles.Count} text-mode recoveries.");
    Console.WriteLine($"Decoded {meshes} meshes, {vertices} vertices and {triangles} triangles.");
    Console.WriteLine(
        $"Decoded {textureReferences} texture references, {materialPasses} material passes and {textureStages} texture stages.");
    Console.WriteLine(
        $"Decoded {shaders} shaders, {vertexMaterials} vertex materials, {vertexMaterialIds} vertex-material IDs, " +
        $"{shaderIds} shader IDs, {diffuseColors} diffuse colors, {diffuseIllumination} diffuse-illumination colors and " +
        $"{specularColors} secondary/specular colors across {materialVariants} material variants.");
    Console.WriteLine($"Found {twoSidedMeshes} two-sided meshes.");
    Console.WriteLine($"Pass counts: {FormatCounts(passCountCounts)}");
    Console.WriteLine($"Texture stages per pass: {FormatCounts(stageCountCounts)}");
    Console.WriteLine("Shader states:");
    foreach (var pair in shaderStateCounts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
    {
        Console.WriteLine($"  {pair.Value,8}  {pair.Key}");
    }

    Console.WriteLine("Mapper arguments:");
    foreach (var pair in mapperArgumentCounts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
    {
        Console.WriteLine($"  {pair.Value,8}  {pair.Key}");
    }

    Console.WriteLine($"Mapper kinds: {FormatCounts(mapperKindCounts)}");
    Console.WriteLine("Multiple-pass sequences:");
    foreach (var pair in passSequenceCounts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
    {
        Console.WriteLine($"  {pair.Value,8}  {pair.Key}");
    }

    Console.WriteLine("Feature examples:");
    foreach (var pair in featureExamples.OrderBy(pair => pair.Key))
    {
        Console.WriteLine($"  {pair.Key}: {pair.Value}");
    }
    Console.WriteLine("Most frequent chunk types:");
    foreach (var pair in typeCounts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(20))
    {
        Console.WriteLine($"  0x{pair.Key:X8}  {pair.Value}");
    }

    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    foreach (var repairedFile in repairedFiles)
    {
        Console.WriteLine($"Recovered: {repairedFile}");
    }

    return failures.Count == 0 ? 0 : 1;
}

catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}

static void Increment<TKey>(Dictionary<TKey, long> counts, TKey value) where TKey : notnull
    => counts[value] = counts.GetValueOrDefault(value) + 1;

static string FormatCounts<TKey>(Dictionary<TKey, long> counts) where TKey : notnull
    => string.Join(", ", counts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
