using CnCNew.Content;
using CnCNew.Images;

if (args is not ["audit-tga", var contentDirectory])
{
    Console.Error.WriteLine("Usage: CnCNew.ImageTool audit-tga <content-directory>");
    return 2;
}

try
{
    var content = ContentFileSystem.Open(contentDirectory);
    var paths = content.EnumerateFiles()
        .Where(path => path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    long pixels = 0;
    foreach (var path in paths)
    {
        using var stream = content.OpenRead(path);
        var image = TgaReader.Read(stream);
        pixels += checked((long)image.Width * image.Height);
    }

    Console.WriteLine($"Decoded {paths.Length}/{paths.Length} TGA files and {pixels} pixels from direct content.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 1;
}
