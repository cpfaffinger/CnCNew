using System.Collections.Immutable;
using CnCNew.Ui.Wnd;

namespace CnCNew.Ui.Skins;

/// <summary>
/// Where the modern skin gives a window of a shipped layout another place (docs/UI-MODERN.md section 16): the geometry of its own for single screens that section 2 left for later. The
/// original's rectangles were drawn around pictures - a heading starts wherever its art needed it to - and the skin has real columns and cells instead. Only numbers of this project are
/// written here, in the layout's 800 x 600; every window keeps its name, its parent and its behaviour.
/// </summary>
internal static class ModernScreens
{
    private static readonly ImmutableDictionary<string, (int Left, int Top, int Right, int Bottom)> Places = new Dictionary<string, (int, int, int, int)>
    {
        // The skirmish setup: a heading stands over its column, on one line.
        ["SkirmishGameOptionsMenu.wnd:StaticTextPlayers"] = (46, 88, 193, 112),
        ["SkirmishGameOptionsMenu.wnd:StaticTextColor"] = (193, 88, 280, 112),
        ["SkirmishGameOptionsMenu.wnd:StaticTextFaction"] = (280, 88, 491, 112),
        ["SkirmishGameOptionsMenu.wnd:StaticTextTeam"] = (490, 88, 569, 112),
        ["SkirmishGameOptionsMenu.wnd:StaticTextMapPreview"] = (580, 88, 747, 112),
        ["SkirmishGameOptionsMenu.wnd:StaticTextTitle"] = (46, 43, 300, 79),
        // The score screen: the same columns over the heading, the pictures and the rows.
        ["ScoreScreen.wnd:StaticTextPlayerName"] = (65, 34, 186, 66),
        ["ScoreScreen.wnd:StaticTextUnitsBuilt"] = (186, 33, 258, 66),
        ["ScoreScreen.wnd:StaticTextUnitsLost"] = (258, 33, 330, 66),
        ["ScoreScreen.wnd:StaticTextUnitsDestroyed"] = (330, 33, 402, 66),
        ["ScoreScreen.wnd:StaticTextBuildingsConstructed"] = (402, 33, 490, 66),
        ["ScoreScreen.wnd:StaticTextBuildingsLost"] = (490, 33, 574, 66),
        ["ScoreScreen.wnd:StaticTextBuildingsDestroyed"] = (574, 33, 658, 66),
        ["ScoreScreen.wnd:StaticTextSuppliesCollected"] = (658, 33, 756, 66),
        ["ScoreScreen.wnd:CameoUnitsCreated"] = (197, 72, 247, 120),
        ["ScoreScreen.wnd:CameoUnitsLost"] = (269, 72, 319, 120),
        ["ScoreScreen.wnd:CameoUnitsDestroyed"] = (341, 72, 391, 120),
        ["ScoreScreen.wnd:CameoBuildingCreated"] = (421, 72, 471, 120),
        ["ScoreScreen.wnd:CameoBuildingLost"] = (507, 72, 557, 120),
        ["ScoreScreen.wnd:CameoBuildingDestroyed"] = (591, 72, 641, 120),
        ["ScoreScreen.wnd:CameoSuppliesCollected"] = (682, 72, 732, 120),
        // The heading of the panel stands over the list, not on it.
        ["ScoreScreen.wnd:StaticTextWarSchool"] = (69, 382, 735, 410),
        ["ScoreScreen.wnd:ListboxWarschoolAdvice"] = (69, 410, 735, 540),
    }.ToImmutableDictionary();

    /// <summary>A window that opens over another screen: what lies behind it is text, not a battle, and must not read through.</summary>
    public static bool IsDialog(string? name)
        => name is not null && (name.StartsWith("Popup", StringComparison.Ordinal) || name.StartsWith("MessageBox", StringComparison.Ordinal) || name.StartsWith("QuitMessageBox", StringComparison.Ordinal)
            || name.StartsWith("SkirmishMapSelectMenu", StringComparison.Ordinal) || name.StartsWith("LanMapSelectMenu", StringComparison.Ordinal) || name.StartsWith("QuitMenu", StringComparison.Ordinal)
            || name.StartsWith("Diplomacy", StringComparison.Ordinal));

    /// <summary>A heading of a page starts at the left edge of what stands under it.</summary>
    public static bool StartsLeft(string? name) => name is "SkirmishGameOptionsMenu.wnd:StaticTextTitle" or "ScoreScreen.wnd:StaticTextWarSchool";

    public static WndScreenRect Place(WndWindow window)
    {
        if (window.Name is not { } name || !Places.TryGetValue(name, out var place)) return window.Rect;
        return window.Rect with { Left = place.Left, Top = place.Top, Right = place.Right, Bottom = place.Bottom };
    }

    /// <summary>The cells of the score table: a line of cells with a row number, filled every second row.</summary>
    private static readonly ImmutableArray<string> ScoreCells =
    [
        "ScoreScreen.wnd:StaticTextPlayer", "ScoreScreen.wnd:StaticTextUnitsBuilt", "ScoreScreen.wnd:StaticTextUnitsLost", "ScoreScreen.wnd:StaticTextUnitsDestroyed",
        "ScoreScreen.wnd:StaticTextBuildingsBuilt", "ScoreScreen.wnd:StaticTextBuildingsLost", "ScoreScreen.wnd:StaticTextBuildingsDestroyed", "ScoreScreen.wnd:StaticTextResources",
    ];

    /// <summary>The row of a cell of the score table, or -1.</summary>
    public static int ScoreRow(string? name)
    {
        if (name is null) return -1;
        foreach (var prefix in ScoreCells)
            if (name.Length == prefix.Length + 1 && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsAsciiDigit(name[^1])) return name[^1] - '0';
        return -1;
    }
}
