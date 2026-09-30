using System.Collections.Immutable;

namespace CnCNew.Ui.Images;

/// <param name="VirtualPath">The first existing candidate, or <c>null</c> when the texture does not exist.</param>
/// <param name="Candidates">Probed paths in probe order.</param>
public sealed record MappedTexture(string TextureFile, int Images, string? VirtualPath, ImmutableArray<string> Candidates)
{
    public bool Found => VirtualPath is not null;
    public bool Localized => VirtualPath is not null && VirtualPath.StartsWith("Data/", StringComparison.OrdinalIgnoreCase);
}

public sealed record MappedTextureReport(ImmutableArray<MappedTexture> Textures)
{
    public ImmutableArray<MappedTexture> Missing => [..Textures.Where(texture => !texture.Found)];
    public int ImagesWithMissingTexture => Textures.Where(texture => !texture.Found).Sum(texture => texture.Images);
}

/// <summary>
/// Resolves the texture pages of mapped images the way <c>GameFileClass::Set_Name</c> (W3DFileSystem.cpp) finds a
/// <c>.tga</c>/<c>.dds</c> file: first the localized folder <c>Data/&lt;Language&gt;/Art/Textures/</c> (texts baked into
/// images), then <c>Art/Textures/</c>. The WW3D loader also accepts a <c>.dds</c> file with the same stem for a
/// <c>.tga</c> name (<c>DDSFileClass</c>, ddsfile.cpp), so each folder is probed with the <c>.dds</c> stem after the
/// literal name; which of the two formats the renderer prefers is not decided here, this is an existence report.
/// Existence comes from the caller (case-insensitive like the original file system).
/// </summary>
public static class MappedImageTextures
{
    public static ImmutableArray<string> Candidates(string textureFile, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textureFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (textureFile.Contains('/') || textureFile.Contains('\\') || textureFile.Contains(".."))
            throw new ArgumentException($"Texture file '{textureFile}' must be a plain file name.", nameof(textureFile));
        var dds = Path.ChangeExtension(textureFile, ".dds");
        return [..new[]
        {
            $"Data/{language}/Art/Textures/{textureFile}",
            $"Data/{language}/Art/Textures/{dds}",
            $"Art/Textures/{textureFile}",
            $"Art/Textures/{dds}",
        }.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public static MappedTextureReport Resolve(MappedImageTable table, string language, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(exists);
        return new MappedTextureReport([..table.ImagesPerTexture().Select(pair =>
        {
            var candidates = Candidates(pair.TextureFile, language);
            return new MappedTexture(pair.TextureFile, pair.Images, candidates.FirstOrDefault(exists), candidates);
        })]);
    }
}
