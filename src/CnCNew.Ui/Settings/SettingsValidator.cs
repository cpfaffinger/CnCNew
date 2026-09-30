using System.Collections.Immutable;

namespace CnCNew.Ui.Settings;

/// <summary>
/// One explicit deviation found while loading, migrating or validating settings. <see cref="Path"/> is the JSON path
/// (<c>audio.master</c>); codes: <c>clamped</c>, <c>snapped</c>, <c>invalid-value</c>, <c>invalid-type</c>,
/// <c>missing</c>, <c>unknown-property</c>, <c>migrated</c>, <c>corrupt</c>, <c>stale-temp-file</c>.
/// </summary>
public sealed record SettingsIssue(string Path, string Code, string Message);

/// <summary>Range rules of every setting. Out-of-range values are clamped or replaced and always reported.</summary>
public static class SettingsValidator
{
    public const float MinimumUnit = 0f;
    public const float MaximumUnit = 1f;
    public const int MaximumStartMoney = 1_000_000;
    public const int MaximumFactions = 8;
    public const int MaximumLanguageLength = 32;
    public static ImmutableArray<int> TextScales { get; } = [100, 125, 150];

    public static (GameSettings Settings, ImmutableArray<SettingsIssue> Issues) Normalize(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var issues = new List<SettingsIssue>();
        var defaults = GameSettings.Default;

        var display = settings.Display ?? Replace(issues, "display", defaults.Display);
        display = display with
        {
            Mode = Enum(issues, "display.mode", display.Mode, defaults.Display.Mode),
            Width = Clamp(issues, "display.width", display.Width, Resolutions.Minimum.Width, Resolutions.Maximum.Width),
            Height = Clamp(issues, "display.height", display.Height, Resolutions.Minimum.Height, Resolutions.Maximum.Height),
            UiScale = Enum(issues, "display.uiScale", display.UiScale, defaults.Display.UiScale),
            Aspect = Enum(issues, "display.aspect", display.Aspect, defaults.Display.Aspect),
        };

        var audio = settings.Audio ?? Replace(issues, "audio", defaults.Audio);
        audio = new AudioSettings(
            Unit(issues, "audio.master", audio.Master, defaults.Audio.Master),
            Unit(issues, "audio.music", audio.Music, defaults.Audio.Music),
            Unit(issues, "audio.effects", audio.Effects, defaults.Audio.Effects),
            Unit(issues, "audio.voice", audio.Voice, defaults.Audio.Voice),
            Unit(issues, "audio.ambient", audio.Ambient, defaults.Audio.Ambient));

        var controls = settings.Controls ?? Replace(issues, "controls", defaults.Controls);
        controls = controls with
        {
            ScrollSpeed = Unit(issues, "controls.scrollSpeed", controls.ScrollSpeed, defaults.Controls.ScrollSpeed),
            CameraRotateSpeed = Unit(issues, "controls.cameraRotateSpeed", controls.CameraRotateSpeed, defaults.Controls.CameraRotateSpeed),
        };

        var accessibility = settings.Accessibility ?? Replace(issues, "accessibility", defaults.Accessibility);
        if (!TextScales.Contains(accessibility.UiTextScalePercent))
        {
            var snapped = TextScales.OrderBy(value => Math.Abs((long)value - accessibility.UiTextScalePercent)).ThenBy(value => value).First();
            issues.Add(new SettingsIssue("accessibility.uiTextScalePercent", "snapped",
                $"{accessibility.UiTextScalePercent} % is not one of {string.Join(", ", TextScales)} %; using {snapped} %."));
            accessibility = accessibility with { UiTextScalePercent = snapped };
        }

        var general = settings.General ?? Replace(issues, "general", defaults.General);
        if (!IsLanguageName(general.Language))
        {
            issues.Add(new SettingsIssue("general.language", "invalid-value",
                $"Language '{general.Language}' is not a name of 1 to {MaximumLanguageLength} ASCII letters; using {defaults.General.Language}."));
            general = general with { Language = defaults.General.Language };
        }
        general = general with { DefaultAiDifficulty = Enum(issues, "general.defaultAiDifficulty", general.DefaultAiDifficulty, defaults.General.DefaultAiDifficulty) };

        var skirmish = settings.Skirmish ?? Replace(issues, "skirmish", defaults.Skirmish);
        if (skirmish.LastMap is { } map && !IsVirtualPath(map))
        {
            issues.Add(new SettingsIssue("skirmish.lastMap", "invalid-value", $"'{map}' is not a relative virtual path; it is forgotten."));
            skirmish = skirmish with { LastMap = null };
        }
        var factions = skirmish.LastFactions.Where(IsFactionName).ToList();
        if (factions.Count != skirmish.LastFactions.Length)
            issues.Add(new SettingsIssue("skirmish.lastFactions", "invalid-value",
                $"{skirmish.LastFactions.Length - factions.Count} faction name(s) are not [A-Za-z0-9_]+ and were dropped."));
        if (factions.Count > MaximumFactions)
        {
            issues.Add(new SettingsIssue("skirmish.lastFactions", "clamped", $"{factions.Count} factions exceed the {MaximumFactions} player slots; the rest was dropped."));
            factions = factions.Take(MaximumFactions).ToList();
        }
        skirmish = skirmish with
        {
            LastFactions = [..factions],
            LastStartMoney = Clamp(issues, "skirmish.lastStartMoney", skirmish.LastStartMoney, 0, MaximumStartMoney),
        };

        return (new GameSettings(display, audio, controls, accessibility, general, skirmish), [..issues]);
    }

    public static bool IsLanguageName(string? value)
        => !string.IsNullOrEmpty(value) && value.Length <= MaximumLanguageLength && value.All(char.IsAsciiLetter);

    public static bool IsFactionName(string? value)
        => !string.IsNullOrEmpty(value) && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    public static bool IsVirtualPath(string value)
        => value.Length > 0 && !value.Contains('\\') && !value.StartsWith('/') && !value.Contains(':')
            && value.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));

    private static T Replace<T>(List<SettingsIssue> issues, string path, T fallback)
    {
        issues.Add(new SettingsIssue(path, "missing", $"Section '{path}' is missing; defaults are used."));
        return fallback;
    }

    private static int Clamp(List<SettingsIssue> issues, string path, int value, int minimum, int maximum)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        if (clamped != value) issues.Add(new SettingsIssue(path, "clamped", $"{value} is outside {minimum}..{maximum}; using {clamped}."));
        return clamped;
    }

    private static float Unit(List<SettingsIssue> issues, string path, float value, float fallback)
    {
        if (!float.IsFinite(value))
        {
            issues.Add(new SettingsIssue(path, "invalid-value", $"{value} is not a finite number; using {fallback}."));
            return fallback;
        }
        var clamped = Math.Clamp(value, MinimumUnit, MaximumUnit);
        if (clamped != value) issues.Add(new SettingsIssue(path, "clamped", $"{value} is outside 0..1; using {clamped}."));
        return clamped;
    }

    private static T Enum<T>(List<SettingsIssue> issues, string path, T value, T fallback) where T : struct, System.Enum
    {
        if (System.Enum.IsDefined(value)) return value;
        issues.Add(new SettingsIssue(path, "invalid-value", $"{value} is not a {typeof(T).Name}; using {fallback}."));
        return fallback;
    }
}
