using System.Collections.Immutable;
using CnCNew.Game.Data;
using CnCNew.Ui.Images;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;

namespace CnCNew.Game.Shell;

/// <summary>
/// What the shell needs from its host environment: where to persist settings and how big the desktop is (used to fit a
/// requested resolution). The Stride client supplies the real values; tests supply a temporary directory and a fixed size.
/// </summary>
public interface IShellEnvironment
{
    string SettingsDirectory { get; }
    int DesktopWidth { get; }
    int DesktopHeight { get; }

    /// <summary>Where <see cref="CnCNew.Game.Save.SaveGameStore"/> keeps <c>.cncsave</c> files. Defaults to a <c>Saves</c>
    /// subdirectory of <see cref="SettingsDirectory"/> so a host that only configures settings still gets a working store.</summary>
    string SaveDirectory => Path.Combine(SettingsDirectory, "Saves");

    /// <summary>Where <see cref="CnCNew.Game.Replays.ReplayStore"/> keeps the <c>.cncreplay</c> files the client records: a <c>Replays</c> subdirectory beside the savegames.</summary>
    string ReplayDirectory => Path.Combine(SettingsDirectory, "Replays");
}

/// <summary>A fixed-size desktop backed by a caller-chosen directory (used by tests and by any host that already knows its screen size).</summary>
public sealed class FixedShellEnvironment(string settingsDirectory, int desktopWidth = 1920, int desktopHeight = 1080, string? saveDirectory = null) : IShellEnvironment
{
    public string SettingsDirectory { get; } = settingsDirectory;
    public int DesktopWidth { get; } = desktopWidth;
    public int DesktopHeight { get; } = desktopHeight;
    public string SaveDirectory { get; } = saveDirectory ?? Path.Combine(settingsDirectory, "Saves");
    public string ReplayDirectory { get; } = Path.Combine(settingsDirectory, "Replays");
}

/// <summary>
/// Deterministic, renderer-free text metrics for headless layout and tests. Widths are a fixed fraction of the font's
/// point size per character (no real glyph metrics), so builds are reproducible without a font rasterizer; the Stride
/// client is expected to supply its own <see cref="IUiTextMeasure"/> backed by the actual font.
/// </summary>
public sealed class ShellTextMeasure : IUiTextMeasure
{
    public int MeasureWidth(string text, UiFont font)
    {
        if (text.Length == 0) return 0;
        var charWidth = CharWidth(font);
        return text.Length * charWidth;
    }

    public int LineHeight(UiFont font) => Math.Max(1, font.Size) + 4;

    private static int CharWidth(UiFont font) => Math.Max(1, font.Size * 3 / 5) + (font.Bold ? 1 : 0);
}

/// <summary>Image sizes resolved through the runtime's mapped image table (<c>MappedImage.ini</c> catalog).</summary>
public sealed class ShellImageLookup(MappedImageTable images) : IUiImageLookup
{
    public bool TryGetSize(string imageName, out int width, out int height)
    {
        if (images.TryGet(imageName, out var image))
        {
            width = image.Width;
            height = image.Height;
            return true;
        }
        width = height = 0;
        return false;
    }
}

/// <summary>
/// Resolves <c>TEXT</c>/<c>TOOLTIPTEXT</c> labels through <see cref="LocalizedText"/>, with an explicit, counted fallback
/// table for the handful of shell labels the shipped CSF does not carry (menu strings ShellMenuScheme/network/graphics
/// options never needed in the original, since the corresponding controls were unused or unlocalized placeholders).
/// Every fallback hit is counted in <see cref="FallbackHits"/>, sorted by label, so a documentation drift shows up in tests.
/// </summary>
public sealed class ShellTextSource(LocalizedText text) : IUiTextSource
{
    /// <summary>Labels the shipped English CSF does not define, with the English text the shell substitutes instead.</summary>
    public static readonly ImmutableSortedDictionary<string, string> FallbackLabels = new Dictionary<string, string>
    {
        ["GUI:Munkee"] = string.Empty, // MessageBox/QuitMessageBox placeholder text; replaced by shell-supplied text before use.
        ["Tooltip:LoadProgress"] = "Loading...",
        ["GUI:LanguageFilter"] = "Language Filter",
        ["GUI:DisplayMode"] = "Display Mode",
        ["GUI:FullScreen"] = "Full Screen",
        ["GUI:Windowed"] = "Windowed",
        ["GUI:Borderless"] = "Borderless Window",
        ["GUI:Language"] = "Language",
        ["GUI:AspectRatio"] = "Aspect Ratio",
        ["GUI:UiScale"] = "UI Scale",
        ["GUI:HighContrast"] = "High Contrast UI",
        ["GUI:LargeText"] = "Large Text",
        ["GUI:Difficulty"] = "Difficulty",
        ["GUI:NotAvailable"] = "N/A",
        ["GUI:Observer"] = "Observer",
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    private readonly SortedDictionary<string, int> _fallbackHits = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> FallbackHits => _fallbackHits;

    private readonly SortedDictionary<string, int> _rawNameHits = new(StringComparer.Ordinal);

    /// <summary>Labels (or internal names) for which a screen had no localized text and showed the raw name instead (<see cref="ShellSkirmishText"/>).</summary>
    public IReadOnlyDictionary<string, int> RawNameHits => _rawNameHits;

    public void CountRawName(string label) => _rawNameHits[label] = _rawNameHits.GetValueOrDefault(label) + 1;

    public bool TryResolve(string label, out string value)
    {
        if (text.TryGet(label, out value)) return true;
        if (FallbackLabels.TryGetValue(label, out var fallback))
        {
            value = fallback;
            _fallbackHits[label] = _fallbackHits.GetValueOrDefault(label) + 1;
            return true;
        }
        value = string.Empty;
        return false;
    }
}

/// <summary>Names a map by its retail lookup tag (<c>MAP:AlpineAssault</c>) when the CSF has it, else its file name (counted).</summary>
public sealed class ShellMapNames
{
    private readonly SortedSet<string> _fileNameFallbacks = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> FileNameFallbacks => _fileNameFallbacks;

    public string DisplayName(string mapPath, IReadOnlyDictionary<string, ShellMapEntry> catalog, IUiTextSource? textSource)
    {
        if (catalog.TryGetValue(mapPath, out var entry) && entry.NameLookupTag is { Length: > 0 } tag
            && textSource is not null && textSource.TryResolve(tag, out var localized))
            return localized;
        _fileNameFallbacks.Add(mapPath);
        return Path.GetFileNameWithoutExtension(mapPath);
    }
}
