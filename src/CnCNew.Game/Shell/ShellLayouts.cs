using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Shell;

/// <summary>
/// Loads the WND layouts and the header template table the shell needs, once, from <see cref="GameData.Content"/>.
/// Layout files live under <c>Window/Menus/</c>; an unshipped file is an error (the shell never falls back to a
/// synthesized layout), so every name here must match a real file.
/// </summary>
public sealed class ShellLayouts
{
    private readonly Dictionary<string, WndLayout> _byName = new(StringComparer.Ordinal);

    private ShellLayouts(WndHeaderTemplateTable headerTemplates) => HeaderTemplates = headerTemplates;

    public WndHeaderTemplateTable HeaderTemplates { get; }

    public WndLayout Get(string name) => _byName.TryGetValue(name, out var layout)
        ? layout
        : throw new KeyNotFoundException($"Layout '{name}' was not loaded.");

    public static ShellLayouts Load(ContentFileSystem content, string language, params string[] names)
    {
        var shell = new ShellLayouts(LoadHeaderTemplates(content, language));
        foreach (var name in names)
        {
            var path = $"Window/Menus/{name}.wnd";
            if (!content.Exists(path)) throw new FileNotFoundException($"Shipped WND layout '{path}' is missing from the content directory.", path);
            using var stream = content.OpenRead(path);
            shell._byName[name] = WndParser.Parse(stream, $"{name}.wnd");
        }
        return shell;
    }

    private static WndHeaderTemplateTable LoadHeaderTemplates(ContentFileSystem content, string language)
    {
        var path = $"Data/{language}/HeaderTemplate.ini";
        if (!content.Exists(path)) return WndHeaderTemplateTable.Empty;
        using var stream = content.OpenRead(path);
        using var reader = new StreamReader(stream);
        return WndHeaderTemplateTable.Parse(reader.ReadToEnd(), path);
    }
}

/// <summary>Names of every shipped WND layout the shell drives (see <c>docs/GAME-SHELL.md</c> for what each screen does).</summary>
public static class ShellLayoutNames
{
    public const string MainMenu = "MainMenu";
    public const string OptionsMenu = "OptionsMenu";
    public const string SkirmishGameOptionsMenu = "SkirmishGameOptionsMenu";
    public const string SkirmishMapSelectMenu = "SkirmishMapSelectMenu";
    public const string ScoreScreen = "ScoreScreen";
    public const string CreditsMenu = "CreditsMenu";
    public const string ShellGameLoadScreen = "ShellGameLoadScreen";
    public const string SinglePlayerLoadScreen = "SinglePlayerLoadScreen";
    public const string MessageBox = "MessageBox";
    public const string QuitMessageBox = "QuitMessageBox";

    public static readonly ImmutableArray<string> All =
    [
        MainMenu, OptionsMenu, SkirmishGameOptionsMenu, SkirmishMapSelectMenu, ScoreScreen, CreditsMenu,
        ShellGameLoadScreen, SinglePlayerLoadScreen, MessageBox, QuitMessageBox,
    ];
}
