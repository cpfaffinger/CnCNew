using System.Globalization;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Tests;

/// <summary>
/// The shell part of the product survey: a program walks the menus the way a person does - from the main menu it clicks every enabled button of every screen it reaches, in
/// a fresh shell for every click - and looks at each screen as a person would notice a defect: text wider or taller than its window, a window outside the screen, a
/// button another window covers or overlaps, an opened drop-down that something covers or that leaves the screen, a missing picture or word, a callback nobody registered,
/// and a button that does nothing at all.
/// </summary>
internal static class ProductSurveyShell
{
    internal sealed record Config(UiSkin Skin, int Width, int Height, int TextPercent)
    {
        public override string ToString() => $"{Skin.Id} {Width}x{Height} text {TextPercent}%";
    }

    internal static readonly (int Width, int Height)[] Sizes = [(800, 600), (1280, 720), (1920, 1080), (1920, 1200)];

    /// <summary>The screen a path of clicks leads to: its key and the buttons clicked from the main menu.</summary>
    internal sealed record Reached(string Key, string[] Path)
    {
        public string Describe() => Path.Length == 0 ? "start the game: main menu" : "main menu > " + string.Join(" > ", Path.Select(window => window[(window.IndexOf(':') + 1)..]));
    }

    /// <summary>The metrics of the font the client draws with (GDI); the headless shell's synthetic widths elsewhere.</summary>
    public static readonly IUiTextMeasure? Measure = ProductSurveyFonts.Create();

    private static long _root;

    public static GameShell NewShell(GameData data, Config config, string root)
    {
        var directory = Path.Combine(root, (++_root).ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        if (config.TextPercent != 100)
            new SettingsStore(directory).Save(GameSettings.Default with { Accessibility = GameSettings.Default.Accessibility with { UiTextScalePercent = config.TextPercent } });
        return GameShell.Create(data, new FixedShellEnvironment(directory, config.Width, config.Height), viewport: new UiViewport(config.Width, config.Height), textMeasure: Measure, skin: config.Skin);
    }

    /// <summary>The screen's class, its layouts and, for a message box, its title.</summary>
    public static string ScreenKey(ShellScreen screen)
    {
        var layouts = string.Join("+", screen.Tree.Roots.Select(root => root.LayoutFile).Distinct(StringComparer.Ordinal));
        return screen.GetType().Name + ":" + layouts + (screen is MessageBoxScreen box ? ":" + box.Title : string.Empty);
    }

    /// <summary>A state a person can be in: the screen and, because a button may only reveal others (the main menu's Single Player), the set of buttons that can be clicked.</summary>
    public static string Key(ShellScreen screen)
    {
        var buttons = screen.Tree.AllWindows.Where(window => window.Type == WndWindowType.PushButton && window.IsVisible && window.IsEffectivelyEnabled && !window.Rect.IsEmpty && window.Name.Length > 0)
            .Select(window => window.Name).Order(StringComparer.Ordinal);
        var hash = (uint)string.Join(",", buttons).Aggregate(17, (accumulator, character) => unchecked(accumulator * 31 + character));
        return ScreenKey(screen) + "#" + hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>Clicks the named windows one after the other; false when one is missing or cannot be clicked (the path is no longer valid).</summary>
    private static bool Replay(GameShell shell, string[] path)
    {
        foreach (var name in path)
        {
            if (shell.CurrentScreen.Tree.FindByName(name) is not { } window || !window.IsVisible) return false;
            ShellTestInput.Click(shell, name);
            Settle(shell);
        }
        return true;
    }

    private static void Settle(GameShell shell)
    {
        shell.Update(0.1);
        shell.CurrentScreen.Tree.PointerLeft();
    }

    private static string Digest(GameShell shell)
    {
        var text = new StringBuilder();
        text.Append(shell.Phase).Append('|').Append(shell.Stack.Count).Append('|').Append(Key(shell.CurrentScreen)).Append('|');
        foreach (var window in shell.CurrentScreen.Tree.AllWindows)
        {
            text.Append(window.Name).Append(window.IsHidden ? 'h' : 's').Append(window.IsEnabled ? 'e' : 'd').Append(window.IsSelected ? 'x' : '-').Append(window.Text?.Value).Append(window.ProgressPercent);
            if (window.ComboBox is { } combo) text.Append(combo.SelectedIndex).Append(combo.Entries.Count);
            if (window.ListBox is { } list) text.Append(list.Rows.Count).Append(list.SelectedIndex);
            text.Append(';');
        }
        return text.ToString();
    }

    private static IEnumerable<UiWindow> Buttons(GameShell shell)
        => shell.CurrentScreen.Tree.AllWindows.Where(window => window.Type == WndWindowType.PushButton && window.IsVisible && window.IsEffectivelyEnabled && !window.Rect.IsEmpty && window.Name.Length > 0);

    /// <summary>
    /// Walks the menus of one skin at 1280x720: every screen reachable by buttons (up to six clicks deep), and for each button whether anything happens when it is clicked.
    /// </summary>
    public static List<Reached> Explore(GameData data, UiSkin skin, string root, ProductSurveyReport report)
    {
        var config = new Config(skin, 1280, 720, 100);
        var reached = new List<Reached>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string[]>();
        queue.Enqueue([]);
        var clicks = 0;
        while (queue.Count > 0)
        {
            var path = queue.Dequeue();
            var shell = NewShell(data, config, root);
            if (!Replay(shell, path))
            {
                report.Add($"explore|{skin.Id}|{string.Join(">", path)}", "Menus", $"[{skin.Id}] a click path that led to a screen once does not replay: {string.Join(" > ", path)}", "see the key");
                continue;
            }
            var key = Key(shell.CurrentScreen);
            if (!seen.Add(key)) continue;
            var here = new Reached(key, path);
            reached.Add(here);
            foreach (var button in Buttons(shell).Select(button => button.Name).ToArray())
            {
                clicks++;
                var probe = NewShell(data, config, root);
                if (!Replay(probe, path)) continue;
                var before = Digest(probe);
                probe.DrainOutputs();
                try
                {
                    ShellTestInput.Click(probe, button);
                    Settle(probe);
                }
                catch (Exception exception)
                {
                    report.Add($"throws|{skin.Id}|{StripSkin(key)}|{button}", "Menus", $"[{skin.Id}] clicking {button} on {key} throws {exception.GetType().Name}: {exception.Message}", $"{here.Describe()} > click {button}");
                    continue;
                }
                var outputs = probe.DrainOutputs();
                var after = Digest(probe);
                if (outputs.IsDefaultOrEmpty && after == before && !(probe.CurrentScreen.Tree.OpenComboBox is not null))
                {
                    report.Add($"inert|{StripSkin(key)}|{button}", "Menus", $"the button {button} on {key} does nothing when clicked (no screen, output, text or window changes)", $"{here.Describe()} > click {button}");
                    continue;
                }
                if (probe.Phase == ShellPhase.Menus && path.Length < 6 && !seen.Contains(Key(probe.CurrentScreen)) && Key(probe.CurrentScreen) != key)
                    queue.Enqueue([.. path, button]);
            }
        }
        report.Note($"- menus [{skin.Id}]: {reached.Count} screens reached, {clicks} button clicks");
        return reached;
    }

    private static string StripSkin(string key) => key[..key.IndexOf('#')];

    /// <summary>Looks at every reached screen in one configuration as a person would notice a defect.</summary>
    public static void Audit(GameData data, Config config, IReadOnlyList<Reached> screens, string root, ProductSurveyReport report)
    {
        foreach (var reached in screens)
        {
            var shell = NewShell(data, config, root);
            if (!Replay(shell, reached.Path)) continue;
            AuditScreen(shell, config, reached, report);
        }
    }

    private static void AuditScreen(GameShell shell, Config config, Reached reached, ProductSurveyReport report)
    {
        var tree = shell.CurrentScreen.Tree;
        var screen = reached.Key;
        var area = "Menus";
        var skin = config.Skin.Id;
        var configuration = config.ToString();
        void Found(string kind, string subject, string text) => report.Add($"{kind}|{skin}|{StripSkin(screen)}|{subject}", area, $"[{skin}] {text} (screen {screen})", reached.Describe(), configuration);

        UiDrawList list;
        try { list = shell.BuildDrawList(); }
        catch (Exception exception)
        {
            Found("draw-throws", "-", $"drawing the screen throws {exception.GetType().Name}: {exception.Message}");
            return;
        }
        if (list.Statistics.UnresolvedLabels > 0) Found("unresolved-label", "-", $"{list.Statistics.UnresolvedLabels} label(s) without a word (a placeholder is shown)");
        if (list.Statistics.UnresolvedImageSizes > 0) Found("unresolved-image", "-", $"{list.Statistics.UnresolvedImageSizes} picture(s) whose size is unknown (a missing texture)");
        if (tree.UnregisteredCallbacks.Count > 0) Found("unregistered-callback", string.Join(",", tree.UnregisteredCallbacks.Keys.Order(StringComparer.Ordinal)), $"callbacks nobody registered: {string.Join(", ", tree.UnregisteredCallbacks.Keys.Order(StringComparer.Ordinal))}");

        // Text that does not fit its window, measured with the client's font where there is one (ProductSurveyFonts); with the synthetic widths of a headless shell a label counts only
        // when it is clearly too wide (by 15 %). A label one or two pixels too tall is a rounding of the line height, not a defect: it counts from a quarter more than its window.
        var readability = UiReadability.Audit(tree, list);
        foreach (var overflow in readability.Overflows)
        {
            var numbers = System.Text.RegularExpressions.Regex.Matches(overflow.Measure, "[0-9]+").Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
            var needed = numbers[0];
            var room = numbers[^1];
            var text = overflow.Text.Replace((char)10, ' ');
            var kind = overflow.TooWide ? "wider" : "taller";
            if (overflow.TooWide ? needed * 100 <= room * (Measure is null ? 115 : 100) : needed * 100 <= room * 125) continue;
            report.Add($"text-{(overflow.TooWide ? "wide" : "tall")}|{skin}|{config.TextPercent}%", area, $"[{skin}, text size {config.TextPercent} %] labels {kind} than their window",
                $"set More options > Text size to {config.TextPercent} %, open the screen named, look at the label", configuration,
                subject: $"{overflow.Window} \"{text}\" on {StripSkin(screen)} (main menu > {string.Join(" > ", reached.Path.Select(window => window[(window.IndexOf(':') + 1)..]))})");
        }

        // A window outside the screen.
        foreach (var window in tree.AllWindows)
        {
            if (!window.IsVisible || window.Rect.IsEmpty || window.Name.Length == 0) continue;
            var rect = window.Rect;
            if (!window.IsInteractive && window.Type is WndWindowType.User or WndWindowType.StaticText) continue;
            if (rect.X < -1 || rect.Y < -1 || rect.Right > config.Width + 1 || rect.Bottom > config.Height + 1)
                Found("offscreen", window.Name, $"{window.Name} ({window.Type}) lies outside the {config.Width}x{config.Height} screen: x {rect.X}..{rect.Right}, y {rect.Y}..{rect.Bottom}");
        }

        // Buttons covered by something else, and buttons that overlap each other.
        var buttons = tree.AllWindows.Where(window => window.IsInteractive && window.IsVisible && window.IsEffectivelyEnabled && !window.Rect.IsEmpty && window.Name.Length > 0).ToArray();
        foreach (var button in buttons)
        {
            var hit = tree.HitTest(button.Rect.CenterX, button.Rect.CenterY);
            if (hit is null || hit == button || IsInside(hit, button) || IsInside(button, hit)) continue;
            if (button.Rect.CenterX >= config.Width || button.Rect.CenterY >= config.Height) continue;
            Found("covered", button.Name, $"{button.Name} cannot be clicked at its centre: {hit.Name} takes the click");
        }
        for (var first = 0; first < buttons.Length; first++)
            for (var second = first + 1; second < buttons.Length; second++)
            {
                var a = buttons[first];
                var b = buttons[second];
                if (IsInside(a, b) || IsInside(b, a) || a.Type != b.Type) continue;
                var overlap = Overlap(a.Rect, b.Rect);
                if (overlap > 16) Found("overlap", $"{a.Name}+{b.Name}", $"{a.Name} and {b.Name} overlap by {overlap} px2");
            }

        // Opened drop-downs: inside the screen, over every picture, and the click on a row takes it.
        foreach (var combo in tree.AllWindows.Where(window => window.ComboBox is not null && window.IsVisible && window.IsEffectivelyEnabled && !window.Rect.IsEmpty && window.ComboBox.Entries.Count > 0 && window.Name.Length > 0).ToArray())
            AuditDropDown(shell, config, reached, combo.Name, report);
    }

    private static void AuditDropDown(GameShell shell, Config config, Reached reached, string comboName, ProductSurveyReport report)
    {
        var tree = shell.CurrentScreen.Tree;
        var window = tree.FindByName(comboName)!;
        var skin = config.Skin.Id;
        void Found(string kind, string text) => report.Add($"{kind}|{skin}|{StripSkin(reached.Key)}|{comboName}", "Menus", $"[{skin}] {text} ({comboName} on {reached.Key})", reached.Describe() + $" > open {comboName}", config.ToString());
        var button = UiGadgetGeometry.ComboButton(window);
        ShellTestInput.ClickAt(shell, button.CenterX, button.CenterY);
        if (tree.OpenComboBox != window)
        {
            Found("dropdown-does-not-open", "a click on the drop-down button does not open the list");
            return;
        }
        var drop = UiGadgetGeometry.ComboDropDown(window);
        if (drop.X < 0 || drop.Y < 0 || drop.Right > config.Width || drop.Bottom > config.Height)
            Found("dropdown-offscreen", $"the opened list leaves the screen: x {drop.X}..{drop.Right}, y {drop.Y}..{drop.Bottom}");
        var rows = UiGadgetGeometry.ComboVisibleRows(window);
        var commands = shell.BuildDrawList().Commands;
        var lastEntry = window.ComboBox!.Entries[Math.Min(rows, window.ComboBox.Entries.Count) - 1];
        var entryAt = -1;
        for (var index = 0; index < commands.Length; index++)
            if (commands[index] is UiTextDraw text && text.Text == lastEntry && drop.Contains(text.X, text.Y + 1)) entryAt = index;
        if (entryAt < 0) Found("dropdown-entry-not-drawn", $"the list's last visible entry \"{lastEntry}\" is not drawn inside the list");
        else
        {
            var covering = Enumerable.Range(0, commands.Length).Count(index => index > entryAt && commands[index] is UiImageDraw image && image.Destination.Intersects(drop));
            if (covering > 0) Found("dropdown-covered", $"{covering} picture(s) are drawn over the opened list");
        }
        var rowHeight = UiGadgetGeometry.RowHeight(window);
        // Pick the row that is chosen already (or the last visible one), so the click changes nothing else on the screen.
        var visible = Math.Min(rows, window.ComboBox.Entries.Count);
        var pick = window.ComboBox.SelectedIndex >= 0 && window.ComboBox.SelectedIndex < visible ? window.ComboBox.SelectedIndex : visible - 1;
        var rowY = drop.Y + (window.Definition.Flat != WndFlatRole.None ? 1 : 0) + pick * rowHeight + rowHeight / 2;
        ShellTestInput.ClickAt(shell, drop.CenterX, Math.Min(rowY, config.Height - 1));
        if (tree.OpenComboBox is not null) Found("dropdown-row-not-clickable", "a click on a visible row does not pick it (something takes the click, or the list stays open)");
    }

    private static bool IsInside(UiWindow inner, UiWindow outer)
    {
        for (var parent = inner.Parent; parent is not null; parent = parent.Parent)
            if (parent == outer) return true;
        return false;
    }

    private static int Overlap(UiRect a, UiRect b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }
}
