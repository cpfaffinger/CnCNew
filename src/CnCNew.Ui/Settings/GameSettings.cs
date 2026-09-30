using System.Collections.Immutable;

namespace CnCNew.Ui.Settings;

public enum DisplayMode { Windowed, Borderless, Fullscreen }

/// <summary>How the 800x600-authored UI maps to the back buffer.</summary>
public enum UiScaleMode
{
    /// <summary>Uniform scale by the smaller axis, centered (menus keep their 4:3 shape).</summary>
    Uniform,
    /// <summary>Stretch to the full back buffer like the original (distorts on non-4:3 displays).</summary>
    Stretch,
    /// <summary>Uniform scale rounded down to a whole multiple for crisp pixels.</summary>
    IntegerMultiple,
}

/// <summary>How the 3D view uses aspect ratios wider than 4:3.</summary>
public enum AspectHandling
{
    /// <summary>Keep the vertical field of view and widen the horizontal one.</summary>
    ExpandHorizontal,
    /// <summary>Render a 4:3 view with side bars.</summary>
    Pillarbox,
    /// <summary>Stretch a 4:3 projection over the full width like the original.</summary>
    Stretch,
}

/// <summary>Skirmish AI difficulty of the original (<c>GameDifficulty</c>: easy, normal, hard).</summary>
public enum AiDifficulty { Easy, Medium, Hard }

public sealed record DisplaySettings(DisplayMode Mode, int Width, int Height, bool VSync, UiScaleMode UiScale, AspectHandling Aspect);

/// <summary>Linear volumes from 0 (silent) to 1 (full).</summary>
public sealed record AudioSettings(float Master, float Music, float Effects, float Voice, float Ambient);

/// <summary>Scroll and rotate speeds are slider positions from 0 to 1 (the original options slider runs 0..100).</summary>
public sealed record ControlSettings(float ScrollSpeed, float CameraRotateSpeed, bool EdgeScrolling);

/// <param name="UiTextScalePercent">100, 125 or 150.</param>
public sealed record AccessibilitySettings(bool HighContrastUi, int UiTextScalePercent, bool ColorBlindSafeTeamColors);

public sealed record GeneralSettings(string Language, bool ShowFps, bool PauseOnFocusLoss, AiDifficulty DefaultAiDifficulty);

/// <summary>What the skirmish setup screen remembers between sessions.</summary>
/// <param name="LastMap">Virtual path of the last map (for example <c>Maps/Alpine Assault/Alpine Assault.map</c>), or <c>null</c>.</param>
/// <param name="LastFactions">Faction names per slot as chosen last time (at most 8).</param>
public sealed record SkirmishMemory(string? LastMap, ImmutableArray<string> LastFactions, int LastStartMoney)
{
    public ImmutableArray<string> LastFactions { get; init; } = LastFactions.IsDefault ? [] : LastFactions;

    public bool Equals(SkirmishMemory? other)
        => other is not null
            && string.Equals(LastMap, other.LastMap, StringComparison.Ordinal)
            && LastFactions.SequenceEqual(other.LastFactions, StringComparer.Ordinal)
            && LastStartMoney == other.LastStartMoney;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(LastMap, StringComparer.Ordinal);
        foreach (var faction in LastFactions) hash.Add(faction, StringComparer.Ordinal);
        hash.Add(LastStartMoney);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The persisted client settings. Immutable; change with <c>with</c> expressions and persist with
/// <see cref="SettingsStore"/>. <see cref="SettingsValidator.Normalize"/> defines the valid ranges.
/// </summary>
public sealed record GameSettings(
    DisplaySettings Display,
    AudioSettings Audio,
    ControlSettings Controls,
    AccessibilitySettings Accessibility,
    GeneralSettings General,
    SkirmishMemory Skirmish)
{
    /// <summary>Schema version written to the settings file.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// First-run values. Volumes follow the retail <c>AudioSettings.ini</c> defaults (sound 80 %, speech 70 %, music
    /// 55 %; ambient uses the 3D sound default of 80 %), the start money follows <c>GameData.ini DefaultStartingCash</c>.
    /// </summary>
    public static GameSettings Default { get; } = new(
        new DisplaySettings(DisplayMode.Windowed, 1280, 720, VSync: true, UiScaleMode.Uniform, AspectHandling.ExpandHorizontal),
        new AudioSettings(Master: 1f, Music: 0.55f, Effects: 0.8f, Voice: 0.7f, Ambient: 0.8f),
        new ControlSettings(ScrollSpeed: 0.5f, CameraRotateSpeed: 0.5f, EdgeScrolling: true),
        new AccessibilitySettings(HighContrastUi: false, UiTextScalePercent: 100, ColorBlindSafeTeamColors: false),
        new GeneralSettings("English", ShowFps: false, PauseOnFocusLoss: true, AiDifficulty.Medium),
        new SkirmishMemory(null, [], 10000));
}
