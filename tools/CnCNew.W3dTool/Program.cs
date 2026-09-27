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
    long materialVariants = 0;
    long twoSidedMeshes = 0;
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
                materialVariants += mesh.MaterialVariants.Count;
                twoSidedMeshes += mesh.IsTwoSided ? 1 : 0;
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
        $"{shaderIds} shader IDs and {diffuseColors} diffuse vertex colors across {materialVariants} material variants.");
    Console.WriteLine($"Found {twoSidedMeshes} two-sided meshes.");
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
