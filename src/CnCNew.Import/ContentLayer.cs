using System.Security.Cryptography;
using System.Text;
using CnCNew.IO;

namespace CnCNew.Import;

/// <summary>
/// A layer of files an import puts above the installation (docs/PATCH-LAYER.md): a community patch. The content it produces is a content directory of its own;
/// its definitions - and with them the definition fingerprint every world, replay, save and match description carries - are the patch's, so a machine with the
/// original content and one with the layer refuse each other's matches by that fingerprint. The layer is named in the content manifest (<see cref="WithLayer"/>).
/// </summary>
/// <param name="Name">Short name, written into the manifest and the provenance of every replaced file (<c>overlay:NAME:PATH</c>).</param>
/// <param name="Source">Where the files come from: the patch and the commit of its source.</param>
/// <param name="Files">Virtual path to physical file, for every file of the layer.</param>
/// <param name="Excluded">The patch's files the layer does not take, by reason - every one counted, none silently dropped.</param>
public sealed record ContentLayer(string Name, string Source, IReadOnlyDictionary<string, string> Files, IReadOnlyDictionary<string, int> Excluded)
{
    public GameFileOverlay Overlay => new(Name, Files);

    /// <summary>SHA-256 over the layer's virtual paths (lower case, ordinal order) and the SHA-256 of each file: the identity of what the layer puts over the game.</summary>
    public string Fingerprint
    {
        get
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var (path, physical) in Files.OrderBy(pair => pair.Key.ToLowerInvariant(), StringComparer.Ordinal))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(path.ToLowerInvariant() + "\n"));
                using var stream = File.OpenRead(physical);
                hash.AppendData(SHA256.HashData(stream));
            }
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
    }
}

public static class ContentLayerManifest
{
    /// <summary>The importer options of a manifest made with <paramref name="layer"/>: the layer's name, source, file count and fingerprint. Unchanged without a layer.</summary>
    public static SortedDictionary<string, string> WithLayer(this SortedDictionary<string, string> options, ContentLayer? layer)
    {
        if (layer is null) return options;
        options["layer"] = layer.Name;
        options["layerSource"] = layer.Source;
        options["layerFiles"] = layer.Files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        options["layerSha256"] = layer.Fingerprint;
        return options;
    }
}

/// <summary>
/// The core of GeneralsGamePatch (the community patch "Patch104pZH", <c>references/generals-game-patch</c>) as a <see cref="ContentLayer"/>: the files of its core
/// bundles (<c>ModBundleCoreItems.json</c>) that it ships as they are - INI, window layouts, map scripts and strings, W3D models, ready textures, cursors. What its own
/// tool chain must first convert (Photoshop and TIFF sources into DDS/TGA, Blender scenes into W3D, string sources into CSF) is not taken and is counted by reason.
/// </summary>
public static class GeneralsGamePatchLayer
{
    public const string Name = "generals-game-patch-core";
    public const string EditedFolder = "GameFilesEdited";

    /// <param name="patchDirectory">The patch's <c>Patch104pZH</c> directory (the one holding <c>GameFilesEdited</c>).</param>
    /// <param name="sourceDescription">The patch and the commit its files are from, for the manifest.</param>
    public static ContentLayer Open(string patchDirectory, string sourceDescription)
    {
        var root = Path.Combine(Path.GetFullPath(patchDirectory), EditedFolder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"'{root}' is missing: name the patch's Patch104pZH directory.");
        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var excluded = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var physical in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.GetRelativePath(root, physical).Replace('\\', '/');
            var reason = Classify(path);
            if (reason is null)
            {
                if (!files.TryAdd(path, physical)) throw new InvalidDataException($"The patch names '{path}' twice (differing only in case).");
                continue;
            }
            excluded[reason] = excluded.GetValueOrDefault(reason) + 1;
        }
        return new ContentLayer(Name, sourceDescription, files, excluded);
    }

    /// <summary>Null when the file is part of the layer; otherwise why it is not.</summary>
    public static string? Classify(string path)
    {
        var p = path.Replace('\\', '/');
        var extension = Path.GetExtension(p).ToLowerInvariant();
        var directory = (Path.GetDirectoryName(p) ?? string.Empty).Replace('\\', '/');
        if (p.StartsWith("Data/INI/", StringComparison.OrdinalIgnoreCase) && extension == ".ini") return null;
        if (p.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) && extension is ".ini" or ".str") return null;
        if (p.StartsWith("Window/", StringComparison.OrdinalIgnoreCase) && extension == ".wnd") return null;
        if (directory.Equals("Art/W3D", StringComparison.OrdinalIgnoreCase) && extension == ".w3d") return null;
        if (directory.Equals("Art/Textures", StringComparison.OrdinalIgnoreCase) && extension is ".tga" or ".dds") return null;
        if (directory.Equals("Data/Cursors", StringComparison.OrdinalIgnoreCase) && extension == ".ani") return null;
        if (p.StartsWith("Art/Textures/", StringComparison.OrdinalIgnoreCase)) return "texture source the patch converts into DDS/TGA (Photoshop, TIFF or a TGA to be mip-mapped)";
        if (p.StartsWith("Art/Models/", StringComparison.OrdinalIgnoreCase)) return "Blender scene the patch builds into W3D";
        if (extension == ".str") return "string source the patch compiles into a CSF string table";
        if (extension is ".txt" or ".md" or ".gitignore") return "note or placeholder of the patch's repository";
        return "not part of the patch's core bundles";
    }
}
