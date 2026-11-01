using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;

namespace CnCNew.Game.Data;

/// <summary>
/// The rules a map brings: its <c>map.ini</c> on top of the base data, compiled when a match on it begins (<c>GameLogic::startNewGame</c>, <c>INI_LOAD_CREATE_OVERRIDES</c>). Every map is treated
/// alike, whether it shipped with the game or lies in the user's folder, so a new or a changed map needs nothing but its own file. A <c>map.ini</c> is part of what a match is: its bytes enter the
/// fingerprint, so a machine with another one refuses the match, the save and the replay.
/// </summary>
public static class MapRules
{
    /// <summary>The <c>map.ini</c> beside a map, if it has one.</summary>
    public static string? OverlayOf(ContentFileSystem content, string mapPath)
    {
        var overlay = mapPath[..(mapPath.LastIndexOf('/') + 1)] + "map.ini";
        return content.Exists(overlay) ? overlay : null;
    }

    /// <summary>The fingerprint of the rules of a map, without compiling them: the base data's and the bytes of its <c>map.ini</c>, so every machine with both comes to the same value.</summary>
    public static string FingerprintFor(ContentFileSystem content, string baseFingerprint, string mapPath)
        => OverlayOf(content, mapPath) is { } overlay
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{baseFingerprint}\n{content.FileHash(overlay)}")))
            : baseFingerprint;

    /// <summary>Compiles the definitions with one map's overlay on top. What a stranger's <c>map.ini</c> asks for and this game cannot read is cleaned first and named in <paramref name="notes"/>.</summary>
    public static CompiledDefinitionGraph Compile(ContentFileSystem content, string overlay, TemplateCatalog baseTemplates, ICollection<string> notes)
    {
        string text;
        using (var reader = new StreamReader(content.OpenRead(overlay), System.Text.Encoding.Latin1)) text = reader.ReadToEnd();
        var cleaned = MapRulesText.Clean(text, name => baseTemplates.Weapons.TryGet(name, out _), model => content.Exists($"Art/W3D/{model}.W3D"), notes);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Include };
        return DefinitionCompiler.Compile(new OverlaySource(content, overlay, cleaned), ModuleRegistry.Default, options);
    }
}

/// <summary>The catalog of a map for a host that has no <see cref="GameData"/> (the measuring tools): the same rules a match of the client plays, the last map kept.</summary>
public sealed class MapCatalogs(ContentFileSystem content, TemplateCatalog templates, string fingerprint)
{
    private readonly object _gate = new();
    private (string Fingerprint, TemplateCatalog Templates, CompiledDefinitionGraph Graph)? _last;

    /// <summary>The rules of a map; <paramref name="graph"/> is <see langword="null"/> for a map without a <c>map.ini</c>, which plays the base data.</summary>
    public (TemplateCatalog Templates, string Fingerprint) For(string mapPath, out CompiledDefinitionGraph? graph)
    {
        graph = null;
        if (MapRules.OverlayOf(content, mapPath) is not { } overlay) return (templates, fingerprint);
        var mapFingerprint = MapRules.FingerprintFor(content, fingerprint, mapPath);
        lock (_gate)
        {
            if (_last is not { } last || last.Fingerprint != mapFingerprint)
            {
                var compiled = MapRules.Compile(content, overlay, templates, new List<string>());
                var geometry = ModelGeometryLoader.Load(compiled, ObjectVisualCatalog.Build(compiled), content);
                last = (mapFingerprint, TemplateCatalog.Build(compiled, ModuleBindingTable.Default, templates.Rate, geometry, templates.Rules), compiled);
                _last = last;
            }
            graph = last.Graph;
            return (last.Templates, mapFingerprint);
        }
    }

    public (TemplateCatalog Templates, string Fingerprint) For(string mapPath) => For(mapPath, out _);
}
