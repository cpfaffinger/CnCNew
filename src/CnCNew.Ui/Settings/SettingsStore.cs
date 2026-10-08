using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CnCNew.Ui.Settings;

public enum SettingsLoadStatus
{
    /// <summary>No settings file exists; <see cref="GameSettings.Default"/> is used.</summary>
    Defaults,
    Loaded,
    /// <summary>The file had an older schema version and was migrated in memory (the next save writes the current one).</summary>
    Migrated,
    /// <summary>The file was unreadable; it was moved aside to <see cref="SettingsLoadResult.BrokenFile"/> and defaults are used.</summary>
    RecoveredFromCorruptFile,
}

/// <param name="FileVersion">Schema version found in the file (0 when there was no readable file).</param>
public sealed record SettingsLoadResult(
    GameSettings Settings,
    SettingsLoadStatus Status,
    int FileVersion,
    ImmutableArray<SettingsIssue> Issues,
    string? BrokenFile);

/// <summary>One schema step from <see cref="FromVersion"/> to <c>FromVersion + 1</c>, applied to the raw JSON object.</summary>
public sealed record SettingsMigration(int FromVersion, string Description, Func<JsonObject, JsonObject> Apply);

/// <summary>A settings file whose schema this build cannot read; the file is left untouched.</summary>
public sealed class SettingsVersionException(string message) : Exception(message);

/// <summary>
/// JSON persistence of <see cref="GameSettings"/> in a caller-chosen directory (the client uses
/// <c>%APPDATA%\CnCNew</c>). Writes are atomic (temporary file, flushed, then renamed over the target) and
/// deterministic (fixed property order, invariant number format, <c>\n</c> line ends). Reading never guesses: every
/// missing, mistyped, unknown or out-of-range value is replaced or clamped and reported as a <see cref="SettingsIssue"/>;
/// an unparsable file is kept aside as <c>settings.json.broken</c>; a newer schema version is an error and the file
/// is not touched; an older one runs the explicit <see cref="SettingsMigration"/> chain.
/// </summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    /// <summary>Schema steps shipped with this build. Version 1 is the first schema, so there are none yet.</summary>
    public static ImmutableArray<SettingsMigration> Migrations { get; } = [];

    private readonly ImmutableArray<SettingsMigration> _migrations;

    public SettingsStore(string directory, IEnumerable<SettingsMigration>? migrations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        _migrations = migrations is null ? Migrations : [..migrations];
        var steps = _migrations.Select(step => step.FromVersion).ToArray();
        if (steps.Distinct().Count() != steps.Length)
            throw new ArgumentException("Two migrations start at the same schema version.", nameof(migrations));
        if (steps.Any(version => version < 0 || version >= GameSettings.CurrentVersion))
            throw new ArgumentException($"Migrations must start below the current schema version {GameSettings.CurrentVersion}.", nameof(migrations));
    }

    public string Directory { get; }
    public string FilePath => Path.Combine(Directory, FileName);
    public string BrokenFilePath => FilePath + ".broken";
    private string TempFilePath => FilePath + ".tmp";

    public SettingsLoadResult Load()
    {
        var issues = new List<SettingsIssue>();
        if (File.Exists(TempFilePath))
            issues.Add(new SettingsIssue(FileName, "stale-temp-file", $"'{FileName}.tmp' is left over from an interrupted save; it is ignored and overwritten by the next save."));
        if (!File.Exists(FilePath))
            return new SettingsLoadResult(GameSettings.Default, SettingsLoadStatus.Defaults, 0, [..issues], null);

        var bytes = File.ReadAllBytes(FilePath);
        // A hand-edited file may carry a UTF-8 byte order mark, which the JSON reader rejects.
        var json = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes[3..] : bytes;
        JsonObject root;
        int version;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow }) as JsonObject
                ?? throw new JsonException("The root is not a JSON object.");
            version = root["version"] is JsonValue value && value.TryGetValue<int>(out var number)
                ? number
                : throw new JsonException("The integer property 'version' is missing.");
        }
        catch (JsonException exception)
        {
            File.Move(FilePath, BrokenFilePath, overwrite: true);
            issues.Add(new SettingsIssue(FileName, "corrupt", $"The settings file is unreadable ({exception.Message}); it was moved to '{FileName}.broken' and defaults are used."));
            return new SettingsLoadResult(GameSettings.Default, SettingsLoadStatus.RecoveredFromCorruptFile, 0, [..issues], BrokenFilePath);
        }

        if (version > GameSettings.CurrentVersion)
            throw new SettingsVersionException(
                $"'{FilePath}' has settings schema version {version}, newer than version {GameSettings.CurrentVersion} of this build. The file was left unchanged.");
        var migrated = false;
        for (var current = version; current < GameSettings.CurrentVersion; current++)
        {
            var step = _migrations.SingleOrDefault(value => value.FromVersion == current)
                ?? throw new SettingsVersionException($"There is no migration from settings schema version {current} to {current + 1}.");
            root = step.Apply(root);
            root["version"] = current + 1;
            issues.Add(new SettingsIssue("version", "migrated", $"Migrated from schema version {current} to {current + 1}: {step.Description}."));
            migrated = true;
        }

        var settings = SettingsJson.Read(root, issues);
        var (normalized, validation) = SettingsValidator.Normalize(settings);
        issues.AddRange(validation);
        return new SettingsLoadResult(normalized, migrated ? SettingsLoadStatus.Migrated : SettingsLoadStatus.Loaded, version, [..issues], null);
    }

    /// <summary>Validates, then writes atomically. Returns the validation issues; the normalized values are what is stored.</summary>
    public ImmutableArray<SettingsIssue> Save(GameSettings settings)
    {
        var (normalized, issues) = SettingsValidator.Normalize(settings);
        System.IO.Directory.CreateDirectory(Directory);
        var bytes = SettingsJson.Write(normalized);
        using (var stream = new FileStream(TempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(TempFilePath, FilePath, overwrite: true);
        return issues;
    }
}

/// <summary>Fixed-order JSON layout of schema version <see cref="GameSettings.CurrentVersion"/>.</summary>
public static class SettingsJson
{
    public static byte[] Write(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", GameSettings.CurrentVersion);

            writer.WriteStartObject("display");
            writer.WriteString("mode", Name(settings.Display.Mode));
            writer.WriteNumber("width", settings.Display.Width);
            writer.WriteNumber("height", settings.Display.Height);
            writer.WriteBoolean("vsync", settings.Display.VSync);
            writer.WriteString("uiScale", Name(settings.Display.UiScale));
            writer.WriteString("aspect", Name(settings.Display.Aspect));
            writer.WriteEndObject();

            writer.WriteStartObject("audio");
            writer.WriteNumber("master", settings.Audio.Master);
            writer.WriteNumber("music", settings.Audio.Music);
            writer.WriteNumber("effects", settings.Audio.Effects);
            writer.WriteNumber("voice", settings.Audio.Voice);
            writer.WriteNumber("ambient", settings.Audio.Ambient);
            writer.WriteEndObject();

            writer.WriteStartObject("controls");
            writer.WriteNumber("scrollSpeed", settings.Controls.ScrollSpeed);
            writer.WriteNumber("cameraRotateSpeed", settings.Controls.CameraRotateSpeed);
            writer.WriteBoolean("edgeScrolling", settings.Controls.EdgeScrolling);
            writer.WriteBoolean("alternateMouse", settings.Controls.AlternateMouse);
            writer.WriteEndObject();

            writer.WriteStartObject("accessibility");
            writer.WriteBoolean("highContrastUi", settings.Accessibility.HighContrastUi);
            writer.WriteNumber("uiTextScalePercent", settings.Accessibility.UiTextScalePercent);
            writer.WriteBoolean("colorBlindSafeTeamColors", settings.Accessibility.ColorBlindSafeTeamColors);
            writer.WriteEndObject();

            writer.WriteStartObject("general");
            writer.WriteString("language", settings.General.Language);
            writer.WriteBoolean("showFps", settings.General.ShowFps);
            writer.WriteBoolean("pauseOnFocusLoss", settings.General.PauseOnFocusLoss);
            writer.WriteString("defaultAiDifficulty", Name(settings.General.DefaultAiDifficulty));
            writer.WriteEndObject();

            writer.WriteStartObject("skirmish");
            if (settings.Skirmish.LastMap is null) writer.WriteNull("lastMap");
            else writer.WriteString("lastMap", settings.Skirmish.LastMap);
            writer.WriteStartArray("lastFactions");
            foreach (var faction in settings.Skirmish.LastFactions) writer.WriteStringValue(faction);
            writer.WriteEndArray();
            writer.WriteNumber("lastStartMoney", settings.Skirmish.LastStartMoney);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    /// <summary>Binds a JSON object of the current schema; deviations become issues and defaults, never exceptions.</summary>
    public static GameSettings Read(JsonObject root, List<SettingsIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(issues);
        var d = GameSettings.Default;
        var reader = new Reader(issues);
        reader.Unknown(root, "", ["version", "display", "audio", "controls", "accessibility", "general", "skirmish"]);

        var display = reader.Section(root, "display", ["mode", "width", "height", "vsync", "uiScale", "aspect"]);
        var audio = reader.Section(root, "audio", ["master", "music", "effects", "voice", "ambient"]);
        var controls = reader.Section(root, "controls", ["scrollSpeed", "cameraRotateSpeed", "edgeScrolling", "alternateMouse"]);
        var accessibility = reader.Section(root, "accessibility", ["highContrastUi", "uiTextScalePercent", "colorBlindSafeTeamColors"]);
        var general = reader.Section(root, "general", ["language", "showFps", "pauseOnFocusLoss", "defaultAiDifficulty"]);
        var skirmish = reader.Section(root, "skirmish", ["lastMap", "lastFactions", "lastStartMoney"]);

        return new GameSettings(
            new DisplaySettings(
                reader.Enum(display, "display.mode", d.Display.Mode),
                reader.Int(display, "display.width", d.Display.Width),
                reader.Int(display, "display.height", d.Display.Height),
                reader.Bool(display, "display.vsync", d.Display.VSync),
                reader.Enum(display, "display.uiScale", d.Display.UiScale),
                reader.Enum(display, "display.aspect", d.Display.Aspect)),
            new AudioSettings(
                reader.Float(audio, "audio.master", d.Audio.Master),
                reader.Float(audio, "audio.music", d.Audio.Music),
                reader.Float(audio, "audio.effects", d.Audio.Effects),
                reader.Float(audio, "audio.voice", d.Audio.Voice),
                reader.Float(audio, "audio.ambient", d.Audio.Ambient)),
            new ControlSettings(
                reader.Float(controls, "controls.scrollSpeed", d.Controls.ScrollSpeed),
                reader.Float(controls, "controls.cameraRotateSpeed", d.Controls.CameraRotateSpeed),
                reader.Bool(controls, "controls.edgeScrolling", d.Controls.EdgeScrolling),
                reader.Bool(controls, "controls.alternateMouse", d.Controls.AlternateMouse)),
            new AccessibilitySettings(
                reader.Bool(accessibility, "accessibility.highContrastUi", d.Accessibility.HighContrastUi),
                reader.Int(accessibility, "accessibility.uiTextScalePercent", d.Accessibility.UiTextScalePercent),
                reader.Bool(accessibility, "accessibility.colorBlindSafeTeamColors", d.Accessibility.ColorBlindSafeTeamColors)),
            new GeneralSettings(
                reader.String(general, "general.language", d.General.Language)!,
                reader.Bool(general, "general.showFps", d.General.ShowFps),
                reader.Bool(general, "general.pauseOnFocusLoss", d.General.PauseOnFocusLoss),
                reader.Enum(general, "general.defaultAiDifficulty", d.General.DefaultAiDifficulty)),
            new SkirmishMemory(
                reader.String(skirmish, "skirmish.lastMap", d.Skirmish.LastMap, nullable: true),
                reader.StringArray(skirmish, "skirmish.lastFactions", d.Skirmish.LastFactions),
                reader.Int(skirmish, "skirmish.lastStartMoney", d.Skirmish.LastStartMoney)));
    }

    /// <summary>Enum values are stored as camelCase names (<c>Borderless</c> becomes <c>borderless</c>).</summary>
    public static string Name<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    private sealed class Reader(List<SettingsIssue> issues)
    {
        public void Unknown(JsonObject node, string path, string[] known)
        {
            foreach (var name in node.Select(pair => pair.Key).Where(name => !known.Contains(name, StringComparer.Ordinal)).Order(StringComparer.Ordinal))
                issues.Add(new SettingsIssue(Join(path, name), "unknown-property", $"'{Join(path, name)}' is not a setting of schema {GameSettings.CurrentVersion}; it is ignored."));
        }

        public JsonObject? Section(JsonObject root, string name, string[] known)
        {
            if (!root.TryGetPropertyValue(name, out var node) || node is null)
            {
                issues.Add(new SettingsIssue(name, "missing", $"Section '{name}' is missing; its defaults are used."));
                return null;
            }
            if (node is not JsonObject section)
            {
                issues.Add(new SettingsIssue(name, "invalid-type", $"Section '{name}' is not an object; its defaults are used."));
                return null;
            }
            Unknown(section, name, known);
            return section;
        }

        public int Int(JsonObject? section, string path, int fallback)
            => Value(section, path, fallback, "an integer", (JsonValue value, out int result) => value.TryGetValue(out result));

        public bool Bool(JsonObject? section, string path, bool fallback)
            => Value(section, path, fallback, "true or false", (JsonValue value, out bool result) => value.TryGetValue(out result));

        public float Float(JsonObject? section, string path, float fallback)
            => Value(section, path, fallback, "a number", (JsonValue value, out float result) =>
            {
                if (value.TryGetValue(out double number) && double.IsFinite(number)) { result = (float)number; return true; }
                result = 0; return false;
            });

        public T Enum<T>(JsonObject? section, string path, T fallback) where T : struct, System.Enum
            => Value(section, path, fallback, $"one of {string.Join(", ", System.Enum.GetValues<T>().Select(Name))}", (JsonValue value, out T result) =>
            {
                result = default;
                if (!value.TryGetValue(out string? text)) return false;
                foreach (var candidate in System.Enum.GetValues<T>())
                    if (Name(candidate) == text) { result = candidate; return true; }
                return false;
            });

        public string? String(JsonObject? section, string path, string? fallback, bool nullable = false)
        {
            if (nullable && section is not null && section.TryGetPropertyValue(Leaf(path), out var node) && node is null) return null;
            return Value(section, path, fallback, "a string", (JsonValue value, out string? result) => value.TryGetValue(out result));
        }

        public ImmutableArray<string> StringArray(JsonObject? section, string path, ImmutableArray<string> fallback)
        {
            if (section is null) return fallback;
            if (!section.TryGetPropertyValue(Leaf(path), out var node))
            {
                issues.Add(new SettingsIssue(path, "missing", $"'{path}' is missing; using the default."));
                return fallback;
            }
            if (node is not JsonArray array || array.Any(item => item is not JsonValue value || !value.TryGetValue<string>(out _)))
            {
                issues.Add(new SettingsIssue(path, "invalid-type", $"'{path}' is not an array of strings; using the default."));
                return fallback;
            }
            return [..array.Select(item => item!.GetValue<string>())];
        }

        private delegate bool TryConvert<T>(JsonValue value, out T result);

        private T Value<T>(JsonObject? section, string path, T fallback, string expected, TryConvert<T> convert)
        {
            if (section is null) return fallback;
            if (!section.TryGetPropertyValue(Leaf(path), out var node))
            {
                issues.Add(new SettingsIssue(path, "missing", $"'{path}' is missing; using the default {fallback}."));
                return fallback;
            }
            if (node is JsonValue value && value.GetValueKind() is not JsonValueKind.Null && convert(value, out var result))
                return result;
            issues.Add(new SettingsIssue(path, "invalid-type", $"'{path}' = {node?.ToJsonString() ?? "null"} is not {expected}; using the default {fallback}."));
            return fallback;
        }

        private static string Leaf(string path) => path[(path.LastIndexOf('.') + 1)..];
        private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";
    }
}
