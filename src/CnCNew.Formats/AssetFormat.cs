namespace CnCNew.Formats;

public sealed record AssetFormatInfo(string Type, string Strategy, bool ProductScope, string? Version = null);

public static class AssetFormat
{
    public static AssetFormatInfo Classify(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ini" => new("sage/ini", "runtime-decoder", true),
        ".str" => new("sage/str", "runtime-decoder", true),
        ".map" => new("sage/map", "runtime-decoder", true),
        ".scb" => new("sage/script-binary", "runtime-decoder", true),
        ".wak" => new("sage/walk-mesh", "runtime-decoder", true),
        ".wnd" => new("sage/window-layout", "runtime-decoder", true),
        ".w3d" => new("westwood/w3d", "runtime-decoder", true),
        ".dds" => new("image/dds", "runtime-decoder", true),
        ".tga" => new("image/tga", "runtime-decoder", true),
        ".ani" => new("image/x-win-ani", "runtime-decoder", true),
        ".wav" => new("audio/wav", "external-runtime-decoder", true),
        ".mp3" => new("audio/mpeg", "external-runtime-decoder", true),
        ".bik" => new("video/bink", "external-runtime-decoder", true),
        ".csf" => new("localization/csf", "import-metadata", false),
        ".bmp" or ".ico" => new("installer/image", "out-of-scope", false),
        ".exe" or ".dll" or ".asi" or ".m3d" or ".flt" => new("installer/binary", "out-of-scope", false),
        ".pdf" or ".doc" or ".txt" or ".vdf" or ".cfg" or ".lcf" => new("installer/document", "out-of-scope", false),
        ".pso" or ".vso" => new("renderer/precompiled-shader", "out-of-scope", false),
        ".dat" or ".sec" or ".bak" or ".wnd_hideip" or ".wnd_hidemail" or ".016" or ".256" or "" =>
            new("installer/opaque", "out-of-scope", false),
        var extension => new("unknown/" + extension.TrimStart('.'), "unclassified", false),
    };
}
