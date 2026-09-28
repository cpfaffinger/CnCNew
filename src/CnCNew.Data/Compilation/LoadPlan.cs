using System.Collections.Immutable;

namespace CnCNew.Data.Compilation;

/// <summary>How a shipped INI file participates in the retail load sequence of the original engine.</summary>
public enum IniFileClass
{
    /// <summary>Loaded at start-up by a subsystem (<c>INI_LOAD_OVERWRITE</c>).</summary>
    Retail,
    /// <summary>Per-map <c>map.ini</c>, loaded on top of the base data with <c>INI_LOAD_CREATE_OVERRIDES</c>.</summary>
    MapOverlay,
    /// <summary>Loaded only by <c>_DEBUG</c>/<c>_INTERNAL</c> builds or debug-cheat builds.</summary>
    DebugOnly,
    /// <summary>Loaded only by tools (WorldBuilder) or not at all by the game.</summary>
    ToolOnly,
    /// <summary>Shipped but not referenced by any loader of the reference sources, or not a definition file.</summary>
    Unreferenced,
    /// <summary>Language- or platform-specific file of a language that is not selected.</summary>
    LanguageVariant,
    /// <summary>Not covered by the plan; loaded after all retail files unless the plan is strict.</summary>
    Unclassified,
}

public enum MapOverlayMode
{
    /// <summary>Full-inventory mode: every map overlay is applied after the base data (documented aggregate).</summary>
    Include,
    /// <summary>Retail base data only.</summary>
    Exclude,
}

public sealed record PlannedFile(string Path, IniFileClass Class, int Stage, int Order, string Subsystem, string Reason, bool OrderSourced = true);

public sealed record LoadPlanResult(ImmutableArray<PlannedFile> Loaded, ImmutableArray<PlannedFile> Excluded);

/// <summary>
/// The retail INI load sequence of Zero Hour, reconstructed from <c>GameEngine.cpp</c> (<c>initSubsystem</c> order),
/// <c>SubsystemInterface.cpp</c> (default file, main file, directory), <c>GameAudio.cpp</c>, <c>GameLOD.cpp</c>,
/// <c>Image.cpp</c> and <c>GameLogic.cpp</c> (map overlays). Directories load top-level files first, then
/// sub-directories, each sorted case-insensitively (<c>INI::loadDirectory</c>).
/// </summary>
public static class OriginalLoadPlan
{
    private const string Ini = "Data/INI/";

    private sealed record Step(bool IsDirectory, string Path, string Subsystem, string Source, bool Sourced);

    private static Step F(string path, string subsystem, string source, bool sourced = true) => new(false, path, subsystem, source, sourced);
    private static Step D(string path, string subsystem, string source, bool sourced = true) => new(true, path, subsystem, source, sourced);

    private static IEnumerable<Step> Steps()
    {
        const string engine = "GameEngine.cpp initSubsystem";
        yield return F(Ini + "Default/GameData.ini", "GlobalData", engine);
        yield return F(Ini + "GameData.ini", "GlobalData", engine);
        yield return F(Ini + "GameLOD.ini", "GameLODManager", "GameLOD.cpp");
        yield return F(Ini + "GameLODPresets.ini", "GameLODManager", "GameLOD.cpp");
        yield return F(Ini + "Default/Water.ini", "Water", "GameEngine.cpp");
        yield return F(Ini + "Water.ini", "Water", "GameEngine.cpp");
        yield return F(Ini + "Default/Weather.ini", "Weather", "GameEngine.cpp");
        yield return F(Ini + "Weather.ini", "Weather", "GameEngine.cpp");
        yield return F(Ini + "Default/Science.ini", "ScienceStore", engine);
        yield return F(Ini + "Science.ini", "ScienceStore", engine);
        yield return F(Ini + "Default/Multiplayer.ini", "MultiplayerSettings", engine);
        yield return F(Ini + "Multiplayer.ini", "MultiplayerSettings", engine);
        yield return F(Ini + "Default/Terrain.ini", "TerrainTypes", engine);
        yield return F(Ini + "Terrain.ini", "TerrainTypes", engine);
        yield return F(Ini + "Default/Roads.ini", "TerrainRoads", engine);
        yield return F(Ini + "Roads.ini", "TerrainRoads", engine);
        yield return F("Data/{language}/Language.ini", "GlobalLanguage", "GlobalLanguage.cpp");
        yield return F(Ini + "AudioSettings.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "Default/Music.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "Music.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "Default/SoundEffects.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "SoundEffects.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "Default/Speech.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "Speech.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "Default/Voice.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "Voice.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "MiscAudio.ini", "Audio", "GameAudio.cpp");
        yield return F(Ini + "Rank.ini", "RankInfoStore", engine);
        yield return F(Ini + "Default/PlayerTemplate.ini", "PlayerTemplateStore", engine);
        yield return F(Ini + "PlayerTemplate.ini", "PlayerTemplateStore", engine);
        yield return F(Ini + "ParticleSystem.ini", "ParticleSystemManager", "ParticleSys.cpp");
        yield return F(Ini + "Default/FXList.ini", "FXListStore", engine);
        yield return F(Ini + "FXList.ini", "FXListStore", engine);
        yield return F(Ini + "Weapon.ini", "WeaponStore", engine);
        yield return F(Ini + "Default/ObjectCreationList.ini", "ObjectCreationListStore", engine);
        yield return F(Ini + "ObjectCreationList.ini", "ObjectCreationListStore", engine);
        yield return F(Ini + "Locomotor.ini", "LocomotorStore", engine);
        yield return F(Ini + "Default/SpecialPower.ini", "SpecialPowerStore", engine);
        yield return F(Ini + "SpecialPower.ini", "SpecialPowerStore", engine);
        yield return F(Ini + "DamageFX.ini", "DamageFXStore", engine);
        yield return F(Ini + "Armor.ini", "ArmorStore", engine);
        yield return F(Ini + "Default/Object.ini", "ThingFactory", engine);
        yield return D(Ini + "Object", "ThingFactory", engine);
        yield return F(Ini + "Default/Upgrade.ini", "UpgradeCenter", engine);
        yield return F(Ini + "Upgrade.ini", "UpgradeCenter", engine);
        // GameClient initialisation. The reference does not fix an order between these files; no definition type
        // occurs in more than one of them, which the load-order report verifies on every run.
        yield return F(Ini + "Animation2D.ini", "Anim2D", "Anim2D.cpp", false);
        yield return F(Ini + "Mouse.ini", "Mouse", "Mouse.cpp", false);
        yield return F(Ini + "InGameUI.ini", "InGameUI", "InGameUI.cpp", false);
        yield return F(Ini + "Default/Video.ini", "VideoPlayer", "VideoPlayer.cpp");
        yield return F(Ini + "Video.ini", "VideoPlayer", "VideoPlayer.cpp");
        yield return D(Ini + "MappedImages/TextureSize_512", "ImageCollection", "Image.cpp");
        yield return D(Ini + "MappedImages/HandCreated", "ImageCollection", "Image.cpp");
        yield return F(Ini + "Default/CommandButton.ini", "ControlBar", "ControlBar.cpp", false);
        yield return F(Ini + "CommandButton.ini", "ControlBar", "ControlBar.cpp", false);
        yield return F(Ini + "CommandSet.ini", "ControlBar", "ControlBar.cpp", false);
        yield return F(Ini + "Default/ControlBarScheme.ini", "ControlBarScheme", "ControlBarScheme.cpp", false);
        yield return F(Ini + "ControlBarScheme.ini", "ControlBarScheme", "ControlBarScheme.cpp", false);
        yield return F(Ini + "ControlBarResizer.ini", "ControlBarResizer", "ControlBarResizer.cpp", false);
        yield return F(Ini + "Default/ShellMenuScheme.ini", "ShellMenuSchemeManager", "ShellMenuScheme.cpp", false);
        yield return F(Ini + "ShellMenuScheme.ini", "ShellMenuSchemeManager", "ShellMenuScheme.cpp", false);
        yield return F(Ini + "WindowTransitions.ini", "GameWindowTransitionsHandler", "GameWindowTransitions.cpp", false);
        yield return F("Data/{language}/HeaderTemplate.ini", "HeaderTemplateManager", "HeaderTemplate.cpp", false);
        yield return F(Ini + "Eva.ini", "Eva", "Eva.cpp", false);
        yield return F(Ini + "Credits.ini", "CreditsManager", "Credits.cpp", false);
        yield return F(Ini + "DrawGroupInfo.ini", "GameClient", "GameClient.cpp", false);
        yield return F(Ini + "ChallengeMode.ini", "ChallengeGenerals", "ChallengeGenerals.cpp", false);
        yield return F(Ini + "Campaign.ini", "CampaignManager", "CampaignManager.cpp", false);
        yield return F(Ini + "Webpages.ini", "WebBrowser", "WebBrowser.cpp", false);
        yield return F("Maps/MapCache.ini", "MapCache", "MapUtil.cpp", false);
        yield return F(Ini + "Default/AIData.ini", "AI", engine);
        yield return F(Ini + "AIData.ini", "AI", engine);
        yield return F(Ini + "Default/Crate.ini", "CrateSystem", engine);
        yield return F(Ini + "Crate.ini", "CrateSystem", engine);
        yield return F("Data/{language}/CommandMap.ini", "MetaMap", engine);
        yield return F(Ini + "CommandMap.ini", "MetaMap", engine);
    }

    private static readonly ImmutableDictionary<string, (IniFileClass Class, string Reason)> Fixed =
        new Dictionary<string, (IniFileClass, string)>(StringComparer.OrdinalIgnoreCase)
        {
            [Ini + "GameDataDebug.ini"] = (IniFileClass.DebugOnly, "GameEngine.cpp loads it only #if _DEBUG || _INTERNAL"),
            [Ini + "CommandMapDebug.ini"] = (IniFileClass.DebugOnly, "GameEngine.cpp loads it only #if _DEBUG || _INTERNAL"),
            [Ini + "CommandMapDemo.ini"] = (IniFileClass.DebugOnly, "GameEngine.cpp loads it only #if _ALLOW_DEBUG_CHEATS_IN_RELEASE"),
            ["Data/Scripts/Scripts.ini"] = (IniFileClass.ToolOnly, "loaded only by the WorldBuilder tool (WorldBuilder.cpp)"),
            [Ini + "MemoryPools.ini"] = (IniFileClass.Unreferenced, "read by the memory pool initialiser (MemoryInit.cpp), not an INI definition file"),
            [Ini + "Default/ButtonSets.ini"] = (IniFileClass.Unreferenced, "no loader in the reference sources"),
            [Ini + "Default/Webpages.ini"] = (IniFileClass.Unreferenced, "no loader in the reference sources"),
            ["RedistInstallers/Options_Helper/Options.ini"] = (IniFileClass.Unreferenced, "installer helper options file, not game definitions"),
        }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly ImmutableHashSet<string> LanguageFiles = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
        "Language.ini", "HeaderTemplate.ini", "CommandMap.ini");

    public static LoadPlanResult Plan(IEnumerable<string> paths, string language, MapOverlayMode maps, bool strict)
    {
        var normalized = paths.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var steps = Steps().Select(step => step with { Path = step.Path.Replace("{language}", language, StringComparison.Ordinal) }).ToArray();
        var loaded = new List<PlannedFile>();
        var excluded = new List<PlannedFile>();
        foreach (var path in normalized)
        {
            if (Fixed.TryGetValue(path, out var known))
            {
                excluded.Add(new PlannedFile(path, known.Class, -1, 0, "excluded", known.Reason));
                continue;
            }
            var stage = -1;
            var order = 0;
            for (var index = 0; index < steps.Length && stage < 0; index++)
            {
                var step = steps[index];
                if (!step.IsDirectory)
                {
                    if (path.Equals(step.Path, StringComparison.OrdinalIgnoreCase)) { stage = index; order = 0; }
                }
                else if (path.StartsWith(step.Path + "/", StringComparison.OrdinalIgnoreCase)
                    && path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                {
                    // INI::loadDirectory: files directly in the directory first, then files of sub-directories.
                    var rest = path[(step.Path.Length + 1)..];
                    stage = index;
                    order = rest.Contains('/') ? 1 : 0;
                }
            }
            if (stage >= 0)
            {
                loaded.Add(new PlannedFile(path, IniFileClass.Retail, stage, order, steps[stage].Subsystem, steps[stage].Source, steps[stage].Sourced));
                continue;
            }
            if (IsLanguageFile(path, out var fileLanguage, out var fileName))
            {
                excluded.Add(new PlannedFile(path, IniFileClass.LanguageVariant, -1, 0, "excluded",
                    fileName.Contains("9x", StringComparison.OrdinalIgnoreCase)
                        ? "Windows 9x variant, not loaded on a current Windows"
                        : $"language '{fileLanguage}' is not the selected language '{language}'"));
                continue;
            }
            if (path.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/map.ini", StringComparison.OrdinalIgnoreCase))
            {
                if (maps == MapOverlayMode.Exclude)
                    excluded.Add(new PlannedFile(path, IniFileClass.MapOverlay, -1, 0, "excluded", "map overlays are excluded from this compilation"));
                else
                    loaded.Add(new PlannedFile(path, IniFileClass.MapOverlay, steps.Length + 1, 0, "map overlay", "GameLogic.cpp startNewGame (INI_LOAD_CREATE_OVERRIDES)"));
                continue;
            }
            if (strict)
                excluded.Add(new PlannedFile(path, IniFileClass.Unclassified, -1, 0, "excluded", "not covered by the retail load plan"));
            else
                loaded.Add(new PlannedFile(path, IniFileClass.Unclassified, steps.Length, 0, "unclassified", "not covered by the retail load plan"));
        }

        static string Key(string path) => path.Replace('/', '\\').ToLowerInvariant();
        return new LoadPlanResult(
            [..loaded.OrderBy(value => value.Stage).ThenBy(value => value.Order).ThenBy(value => Key(value.Path), StringComparer.Ordinal)],
            [..excluded.OrderBy(value => value.Path, StringComparer.Ordinal)]);
    }

    private static bool IsLanguageFile(string path, out string language, out string fileName)
    {
        language = fileName = string.Empty;
        var parts = path.Split('/');
        if (parts.Length != 3 || !parts[0].Equals("Data", StringComparison.OrdinalIgnoreCase)) return false;
        var stem = Path.GetFileNameWithoutExtension(parts[2]);
        var baseName = stem.EndsWith("9x", StringComparison.OrdinalIgnoreCase) ? stem[..^2] : stem;
        if (!LanguageFiles.Contains(baseName + ".ini")) return false;
        language = parts[1];
        fileName = parts[2];
        return true;
    }

    public static string Normalize(string path) => string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
}
