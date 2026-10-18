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

/// <summary>
/// How the 3D view uses a window wider (or narrower) than 4:3. Schema 1 named three ways that the client never applied (its camera always kept the horizontal field
/// of view); schema 2 names the two the camera draws (docs/UI-MODERN.md section 7).
/// </summary>
public enum AspectHandling
{
    /// <summary>The horizontal field of view is the original's at every aspect; a wide window shows less of the ground from top to bottom (what the client always drew).</summary>
    KeepHorizontal,
    /// <summary>The vertical field of view is the original's at 4:3; a wide window shows more of the ground to the left and right.</summary>
    KeepVertical,
}

/// <summary>
/// The interface skin (docs/UI-MODERN.md): the original's pictures, or the modern skin derived from the original's layouts at run time. A presentation
/// choice only; the original is the default until the owner decides (D-21).
/// </summary>
public enum UiSkinName { Original, Modern }

/// <summary>Skirmish AI difficulty of the original (<c>GameDifficulty</c>: easy, normal, hard).</summary>
public enum AiDifficulty { Easy, Medium, Hard }

public sealed record DisplaySettings(DisplayMode Mode, int Width, int Height, bool VSync, UiScaleMode UiScale, AspectHandling Aspect, UiSkinName Skin = UiSkinName.Original);

/// <summary>Linear volumes from 0 (silent) to 1 (full).</summary>
/// <param name="Device">The name of the sound device to play on, as the system lists it; empty is the system's default device.</param>
public sealed record AudioSettings(float Master, float Music, float Effects, float Voice, float Ambient, string Device = "");

/// <summary>Scroll and rotate speeds are slider positions from 0 to 1 (the original options slider runs 0..100).</summary>
/// <param name="AlternateMouse">The original's "alternate mouse setup" (<c>GlobalData::m_useAlternateMouse</c>): the left button only selects, the right
/// button orders. The original ships with it off; this client ships with it on (owner's decision, docs/PRESENTATION-INPUT.md section 2a).</param>
public sealed record ControlSettings(float ScrollSpeed, float CameraRotateSpeed, bool EdgeScrolling, bool AlternateMouse = true);

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
    public const int CurrentVersion = 2;

    /// <summary>
    /// First-run values. Volumes follow the retail <c>AudioSettings.ini</c> defaults (sound 80 %, speech 70 %, music
    /// 55 %; ambient uses the 3D sound default of 80 %), the start money follows <c>GameData.ini DefaultStartingCash</c>.
    /// </summary>
    public static GameSettings Default { get; } = new(
        new DisplaySettings(DisplayMode.Windowed, 1280, 720, VSync: true, UiScaleMode.Uniform, AspectHandling.KeepHorizontal),
        new AudioSettings(Master: 1f, Music: 0.55f, Effects: 0.8f, Voice: 0.7f, Ambient: 0.8f),
        new ControlSettings(ScrollSpeed: 0.5f, CameraRotateSpeed: 0.5f, EdgeScrolling: true, AlternateMouse: true),
        new AccessibilitySettings(HighContrastUi: false, UiTextScalePercent: 100, ColorBlindSafeTeamColors: false),
        new GeneralSettings("English", ShowFps: false, PauseOnFocusLoss: true, AiDifficulty.Medium),
        new SkirmishMemory(null, [], 10000));
}
