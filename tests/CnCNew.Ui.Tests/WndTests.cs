using System.Diagnostics;
using CnCNew.Content;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;
using static CnCNew.Ui.Tests.WndFixtures;

namespace CnCNew.Ui.Tests;

/// <summary>Tests of the WND parser, scaling, window runtime and draw list (package B2).</summary>
public static class WndTests
{
    private static int _failures;

    public static readonly UiViewport[] Resolutions =
    [
        new(640, 480), new(800, 600), new(1024, 768), new(1280, 720), new(1280, 800), new(1600, 900),
        new(1920, 1080), new(2560, 1080), new(3440, 1440), new(3840, 1080), new(1080, 1920),
    ];

    public static int Run(string[] args)
    {
        _failures = 0;
        Test(ParserReadsEveryWindowTypeAndField);
        Test(ParserReproducesShippedQuirksExplicitly);
        Test(ParserReportsUnknownAndMalformedInput);
        Test(HeaderTemplatesParseAndOverrideFonts);
        Test(ScalingIsExactIntegerMath);
        Test(ScalingTableKeepsLayoutInvariants);
        Test(FontScalingFollowsModeAndAccessibilityFactor);
        Test(MinimumHitTargetEnlargesSmallControls);
        Test(HitTestingFollowsZOrderAndClipsChildren);
        Test(PushButtonClickRequiresDownAndUpInside);
        Test(MessagesRouteThroughCallbacks);
        Test(CheckBoxAndRadioGroups);
        Test(SlidersTrackAndFinish);
        Test(ListBoxSelectionScrollingAndColumns);
        Test(ComboBoxDropDown);
        Test(TextEntryEditing);
        Test(ProgressBarAndTabControl);
        Test(KeyboardNavigationReachesEveryControl);
        Test(TooltipStateMachine);
        Test(DrawListGolden);
        Test(ThreePieceButtonsTileTheirCenter);
        Test(PushButtonStatesPickTheirDrawData);
        Test(HiliteFollowsThePointerOnly);
        Test(StaticTextHasNoHiliteState);
        Test(HorizontalSliderDrawsBoxesAndThumb);
        Test(KeyboardHomeReceivesKeysWithoutFocus);
        Test(DrawImagesAndDesignSizeCanBeReplaced);

        var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
        if (contentDirectory is not null) Test(() => RealData(contentDirectory));
        Console.WriteLine(_failures == 0 ? "WND tests passed." : $"WND tests: {_failures} failure(s).");
        return _failures;
    }

    private static void Test(Action test)
    {
        try { test(); }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine($"FAIL {test.Method.Name}: {exception}");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (condition) return;
        _failures++;
        Console.Error.WriteLine($"FAIL: {message}");
    }

    private static UiWindowTree Tree(string text, UiViewport? viewport = null, UiScaleOptions? options = null, UiTreeServices? services = null)
    {
        var layout = WndParser.Parse(text, "Test.wnd");
        Check(layout.Diagnostics.All(diagnostic => diagnostic.IsExplained), $"Fixture has unexplained diagnostics: {string.Join("; ", layout.Diagnostics)}");
        var tree = new UiWindowTree(viewport ?? new UiViewport(800, 600), options ?? UiScaleOptions.Original, services ?? new UiTreeServices(new MonoMeasure()));
        tree.AddLayout(layout);
        return tree;
    }

    private static UiWindow Find(UiWindowTree tree, string name) => tree.FindByName(name) ?? throw new InvalidOperationException($"Window {name} not found.");

    private static void Click(UiWindowTree tree, UiWindow window)
    {
        tree.MouseDown(window.Rect.CenterX, window.Rect.CenterY, UiMouseButton.Left);
        tree.MouseUp(window.Rect.CenterX, window.Rect.CenterY, UiMouseButton.Left);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Parser

    private static readonly string AllTypes = WndFile(Window(new Spec("USER", "All.wnd:Root", 0, 0, 800, 600)
    {
        System = "AllSystem",
        Input = "AllInput",
        Children =
        [
            new Spec("PUSHBUTTON", "All.wnd:Push", 10, 10, 110, 40) { Text = "GUI:Push", TooltipText = "TOOLTIP:Push", TooltipDelay = 100, Status = "ENABLED+IMAGE+RIGHT_CLICK" },
            new Spec("RADIOBUTTON", "All.wnd:Radio", 10, 50, 110, 80) { Extra = "  RADIOBUTTONDATA = GROUP: 3;\n" },
            new Spec("CHECKBOX", "All.wnd:Check", 10, 90, 110, 120),
            new Spec("VERTSLIDER", "All.wnd:VSlider", 120, 10, 140, 200) { Extra = "  SLIDERDATA = MINVALUE: 5,\n    MAXVALUE: 50;\n" },
            new Spec("HORZSLIDER", "All.wnd:HSlider", 150, 10, 350, 30),
            new Spec("SCROLLLISTBOX", "All.wnd:List", 150, 40, 350, 200)
            {
                Extra = "  LISTBOXDATA = LENGTH: 20,\n    AUTOSCROLL: 1,\n    AUTOPURGE: 0,\n    SCROLLBAR: 1,\n    MULTISELECT: 1,\n    COLUMNS: 2,\n    COLUMNSWIDTH: 30,\n    COLUMNSWIDTH: 70,\n    FORCESELECT: 1;\n"
                    + Draw("LISTBOXENABLEDUPBUTTONDRAWDATA", Entry("UpArrow", "1 2 3 4", "5 6 7 8")),
            },
            new Spec("ENTRYFIELD", "All.wnd:Entry", 10, 210, 200, 240)
            {
                Text = "Player",
                Extra = "  TEXTENTRYDATA = MAXLEN: 12,\n    SECRETTEXT: 1,\n    NUMERICALONLY: 0,\n    ALPHANUMERICALONLY: 1,\n    ASCIIONLY: 1;\n",
            },
            new Spec("STATICTEXT", "All.wnd:Static", 10, 250, 200, 280) { Text = "GUI:Static", Extra = "  STATICTEXTDATA = CENTERED: 1;\n", Status = "ENABLED+WRAP_CENTERED+NOINPUT" },
            new Spec("PROGRESSBAR", "All.wnd:Progress", 10, 290, 200, 310),
            new Spec("COMBOBOX", "All.wnd:Combo", 10, 320, 200, 345)
            {
                Extra = "  COMBOBOXDATA = ISEDITABLE: 1,\n    MAXCHARS: 8,\n    MAXDISPLAY: 4,\n    ASCIIONLY: 1,\n    LETTERSANDNUMBERS: 0;\n"
                    + Draw("COMBOBOXDROPDOWNBUTTONENABLEDDRAWDATA", Entry("DropDown", "1 1 1 255", "2 2 2 255")),
            },
            new Spec("TABCONTROL", "All.wnd:Tabs", 400, 10, 700, 300)
            {
                Children =
                [
                    new Spec("TABPANE", "All.wnd:Pane0", 400, 40, 700, 300),
                    new Spec("TABPANE", "All.wnd:Pane1", 400, 40, 700, 300) { Children = [new Spec("PUSHBUTTON", "All.wnd:Deep", 420, 60, 500, 90)] },
                ],
            },
        ],
    }), init: "AllInit");

    private static void ParserReadsEveryWindowTypeAndField()
    {
        var layout = WndParser.Parse(AllTypes, "Menus/All.wnd");
        Check(layout.Diagnostics.IsEmpty, $"Clean fixture reported diagnostics: {string.Join("; ", layout.Diagnostics)}");
        Check(layout.FileVersion == 2 && layout.Callbacks == new WndLayoutCallbacks("AllInit", null, null), "Layout block mismatch.");
        var windows = layout.AllWindows().ToDictionary(window => window.Name!);
        Check(windows.Count == 15, $"Expected 15 windows, got {windows.Count}.");
        var types = windows.Values.Select(window => window.Type).ToHashSet();
        foreach (var type in Enum.GetValues<WndWindowType>().Where(type => type != WndWindowType.Unknown))
            Check(types.Contains(type), $"Window type {type} not produced.");
        var root = windows["All.wnd:Root"];
        Check(root.Callbacks == new WndCallbacks("AllSystem", "AllInput", null, null), "Callbacks mismatch.");
        Check(root.Rect == new WndScreenRect(0, 0, 800, 600, 800, 600), "Screen rect mismatch.");
        Check(root.Children.Length == 11, "Child count mismatch.");
        var push = windows["All.wnd:Push"];
        Check(push.Status == (WndStatus.Enabled | WndStatus.Image | WndStatus.RightClick), $"Status flags mismatch: {push.Status}.");
        Check(push.Style == (WndStyle.PushButton | WndStyle.MouseTrack), $"Style mismatch: {push.Style}.");
        Check(push.Text == "GUI:Push" && push.TooltipText == "TOOLTIP:Push" && push.TooltipDelay == 100, "Text fields mismatch.");
        Check(push.Font == new WndFont("Arial", 10, false), "Font mismatch.");
        Check(push.TextColors.Hilite == new WndColor(255, 255, 0, 255) && push.TextColors.DisabledBorder == new WndColor(0, 0, 0, 255), "Text colors mismatch.");
        Check(push.GetDrawData(WndDrawDataKind.Enabled).Entries.Length == 9 && push.GetDrawData(WndDrawDataKind.Enabled)[0].Color == new WndColor(10, 20, 30, 255), "Draw data mismatch.");
        Check(push.GetDrawData(WndDrawDataKind.Enabled)[1].Image is null && push.GetDrawData(WndDrawDataKind.Enabled)[1].Color.IsUndefined, "NoImage/undefined color mismatch.");
        Check(windows["All.wnd:Radio"].RadioButton == new WndRadioButtonData(3), "Radio data mismatch.");
        Check(windows["All.wnd:VSlider"].Slider == new WndSliderData(5, 50), "Slider data mismatch.");
        var list = windows["All.wnd:List"].ListBox!;
        Check(list is { Length: 20, AutoScroll: true, ScrollIfAtEnd: false, AutoPurge: false, ScrollBar: true, MultiSelect: true, Columns: 2, ForceSelect: true }
              && list.ColumnWidthPercentages.SequenceEqual([30, 70]), "List box data mismatch (optional SCROLLIFATEND absent).");
        Check(windows["All.wnd:List"].GetDrawData(WndDrawDataKind.ListBoxEnabledUpButton)[0].Image == "UpArrow", "List part draw data mismatch.");
        Check(windows["All.wnd:Entry"].TextEntry == new WndTextEntryData(12, true, false, true, true), "Text entry data mismatch.");
        Check(windows["All.wnd:Static"].StaticText == new WndStaticTextData(true), "Static text data mismatch.");
        Check(windows["All.wnd:Combo"].ComboBox == new WndComboBoxData(true, 8, 4, true, false), "Combo data mismatch.");
        var tabs = windows["All.wnd:Tabs"].TabControl!;
        Check(tabs is { TabWidth: 50, TabHeight: 20, TabCount: 2 } && tabs.PaneDisabled.SequenceEqual([false, false]), "Tab control data mismatch.");
        Check(windows["All.wnd:Deep"].Rect.Left == 420, "Nested grandchild lost.");
        Check(layout.KeyCounts["WINDOW"] == 15 && layout.KeyCounts["CHILD"] == 14 && layout.KeyCounts["ENDALLCHILDREN"] == 3, "Key counts mismatch.");

        // Line endings and tab indentation do not matter; the result is identical.
        var crlf = WndParser.Parse(AllTypes.Replace("\n", "\r\n", StringComparison.Ordinal).Replace("\n  ", "\n\t ", StringComparison.Ordinal), "Menus/All.wnd");
        Check(crlf.Diagnostics.IsEmpty && crlf.AllWindows().Count() == 15, "CRLF/tab variant differs.");
        Check(System.Text.Json.JsonSerializer.Serialize(crlf) == System.Text.Json.JsonSerializer.Serialize(layout), "CRLF/tab variant parsed differently.");
    }

    private static void ParserReproducesShippedQuirksExplicitly()
    {
        var text = WndFile(Window(new Spec("USER", "Q.wnd:Root", 0, 0, 800, 600)
        {
            Status = "DISABLED+HIDDEN",
            HeaderTemplate = "[NONE]",
            DrawCallback = "[NONE]",
            // Missing ';' after ENABLEDDRAWDATA swallows DISABLEDDRAWDATA like ControlBar.wnd.
            DrawData = Draw("ENABLEDDRAWDATA", Entry("Panel", "1 2 3 4", "5 6 7 8")).TrimEnd('\n', ';') + ",\n"
                + Draw("DISABLEDDRAWDATA", Entry("Other", "9 9 9 9", "9 9 9 9")) + Draw("HILITEDRAWDATA"),
            Extra = "  DATA = 1 2 3;\n",
        }).Replace("HILITE: 255 255 0 255, HILITEBORDER:", "HILITE: 255 255 0 255, ENABLEDBORDER:", StringComparison.Ordinal));
        var layout = WndParser.Parse(text, "Q.wnd");
        var codes = layout.Diagnostics.Select(diagnostic => diagnostic.Code).OrderBy(code => code, StringComparer.Ordinal).ToArray();
        Check(codes.SequenceEqual(["label-mismatch", "obsolete-data-key", "statement-swallows-next-key", "status-disabled-flag"]),
            $"Quirk diagnostics mismatch: {string.Join(", ", codes)}");
        Check(layout.Diagnostics.All(diagnostic => diagnostic.IsExplained), "Quirks must be explained compatibility diagnostics.");
        var root = layout.Windows.Single();
        Check(root.Status == WndStatus.Hidden, "DISABLED must be ignored (window disabled because ENABLED is absent).");
        Check(root.HeaderTemplate is null && root.Callbacks.Draw is null, "[NONE] sentinel must be null.");
        Check(root.GetDrawData(WndDrawDataKind.Enabled)[0].Image == "Panel", "Swallowing statement lost its own data.");
        Check(root.GetDrawData(WndDrawDataKind.Disabled).Entries.IsEmpty, "Swallowed DISABLEDDRAWDATA must not be applied.");
        Check(root.TextColors.HiliteBorder == new WndColor(0, 0, 0, 255), "Mislabelled text color must be read positionally.");
    }

    private static void ParserReportsUnknownAndMalformedInput()
    {
        var text = WndFile(Window(new Spec("USER", "U.wnd:Root", 0, 0, 800, 600)
        {
            Status = "ENABLED+SPARKLY",
            Extra = "  WOBBLE = 3;\n",
            Children = [new Spec("SPINNER", "U.wnd:Spin", 0, 0, 10, 10)],
        }));
        var layout = WndParser.Parse(text, "U.wnd");
        var unknown = layout.Diagnostics.Where(diagnostic => !diagnostic.IsExplained).Select(diagnostic => diagnostic.Code).ToArray();
        Check(unknown.Contains("unknown-flag") && unknown.Contains("unknown-window-key") && unknown.Contains("unknown-window-type"),
            $"Unknown input not reported: {string.Join(", ", unknown)}");
        Check(layout.AllWindows().Any(window => window.Type == WndWindowType.Unknown && window.TypeName == "SPINNER"), "Unknown type must be kept with its name.");
        Check(layout.Diagnostics.All(diagnostic => diagnostic.File == "U.wnd" && diagnostic.Line > 0), "Diagnostics need file and line.");

        var malformed = WndParser.Parse("FILE_VERSION = 2;\nSTARTLAYOUTBLOCK\nENDLAYOUTBLOCK\nWINDOW\n  WINDOWTYPE = USER;\n  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: x 5, CREATIONRESOLUTION: 800 600;\n  NAME = \"M.wnd:A\";\n", "M.wnd");
        Check(malformed.Diagnostics.Any(diagnostic => diagnostic.Code == "malformed-screen-rect")
              && malformed.Diagnostics.Any(diagnostic => diagnostic.Code == "unterminated-window"), "Malformed input not reported.");
        var empty = WndParser.Parse(string.Empty, "E.wnd");
        Check(empty.Diagnostics.Single().Code == "empty-file" && empty.Windows.IsEmpty, "Empty file not reported.");
        var noVersion = WndParser.Parse("WINDOW\n", "N.wnd");
        Check(noVersion.Diagnostics.Any(diagnostic => diagnostic.Code == "missing-file-version"), "Missing FILE_VERSION not reported.");
    }

    private static void HeaderTemplatesParseAndOverrideFonts()
    {
        var table = WndHeaderTemplateTable.Parse("; comment\nHeaderTemplate Big ; trailing\n  Font = \"Generals\"\n  Point = 12\n  Bold = Yes\nEnd\nHeaderTemplate Bad\n  Font = Arial\n  Size = 3\nEnd\n", "HeaderTemplate.ini");
        Check(table.Find("Big") == new WndHeaderTemplate("Big", "Generals", 12, true), "Template not parsed.");
        Check(table.Find("big") is null, "Template lookup must be case-sensitive.");
        Check(table.Diagnostics.Select(diagnostic => diagnostic.Code).OrderBy(code => code, StringComparer.Ordinal)
            .SequenceEqual(["incomplete-template", "malformed-value", "unknown-key"]), $"Template diagnostics: {string.Join(", ", table.Diagnostics)}");

        var text = WndFile(Window(new Spec("USER", "H.wnd:Root", 0, 0, 800, 600)
        {
            Children =
            [
                new Spec("STATICTEXT", "H.wnd:Templated", 0, 0, 100, 20) { HeaderTemplate = "Big" },
                new Spec("STATICTEXT", "H.wnd:Missing", 0, 30, 100, 50) { HeaderTemplate = "Nope" },
            ],
        }));
        var tree = Tree(text, new UiViewport(1920, 1080), services: new UiTreeServices(new MonoMeasure()) { HeaderTemplates = table });
        // 1 + (1920/800 - 1) * 0.7 = 1.98 -> floor(12 * 1.98) = 23; the window FONT (10) stays unscaled.
        Check(Find(tree, "H.wnd:Templated").Font == new UiFont("Generals", 23, true), $"Template font: {Find(tree, "H.wnd:Templated").Font}");
        Check(Find(tree, "H.wnd:Missing").Font == new UiFont("Arial", 10, false), "Unknown template must keep the window font.");
        Check(tree.Statistics.UnknownHeaderTemplates.SequenceEqual([new KeyValuePair<string, int>("Nope", 1)]), "Unknown template not counted.");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Scaling

    private static void ScalingIsExactIntegerMath()
    {
        var rect = new WndScreenRect(100, 100, 250, 130, 800, 600);
        var stretch = UiScaler.ForWindow(rect, null, new UiViewport(1920, 1080), UiScaleMode.Stretch).Transform;
        // Exact: 100 * 1920 / 800 = 240 (the original's float math gives 239 for this value).
        Check(stretch.Map(rect) == UiRect.FromEdges(240, 180, 600, 234), $"Stretch mapping: {stretch.Map(rect)}");
        var uniform = UiScaler.ForWindow(rect, null, new UiViewport(1920, 1080), UiScaleMode.Uniform).Transform;
        // Scale 1080/600 = 1.8, content 1440 wide, pillarbox offset (1920 - 1440) / 2 = 240.
        Check(uniform.Map(rect) == UiRect.FromEdges(420, 180, 690, 234), $"Uniform mapping: {uniform.Map(rect)}");
        var portrait = UiScaler.ForWindow(rect, null, new UiViewport(1080, 1920), UiScaleMode.Uniform).Transform;
        // Scale 1080/800 = 1.35, content 810 high, letterbox offset (1920 - 810) / 2 = 555.
        Check(portrait.Map(rect) == UiRect.FromEdges(135, 690, 337, 730), $"Portrait uniform mapping: {portrait.Map(rect)}");
        Check(UiAxisTransform.FloorDivide(-7, 2) == -4 && UiAxisTransform.FloorDivide(7, 2) == 3, "Floor division must round toward negative infinity.");
        Check(UiScaler.ClassifyAxis(0, 800, 800) == UiAxisAnchor.Stretch && UiScaler.ClassifyAxis(10, 100, 800) == UiAxisAnchor.Start
              && UiScaler.ClassifyAxis(350, 450, 800) == UiAxisAnchor.Center && UiScaler.ClassifyAxis(700, 790, 800) == UiAxisAnchor.End, "Axis classification.");
        var anchored = UiScaler.ForWindow(new WndScreenRect(600, 100, 780, 400, 800, 600), null, new UiViewport(2560, 1080), UiScaleMode.Anchor);
        // Right-anchored group at 21:9: scale 1.8, content 1440 wide, offset 2560 - 1440 = 1120.
        Check(anchored.Transform.Map(new WndScreenRect(600, 100, 780, 400, 800, 600)) == UiRect.FromEdges(2200, 180, 2524, 720) && anchored.Inherited is not null,
            $"Anchor mapping: {anchored.Transform.Map(new WndScreenRect(600, 100, 780, 400, 800, 600))}");
    }

    /// <summary>A menu-like fixture: a stretched background with a title, a right button column, a centered dialog and a bottom bar.</summary>
    private static readonly string MenuLayout = WndFile(Window(new Spec("USER", "Menu.wnd:Background", 0, 0, 800, 600)
    {
        System = "MenuSystem",
        Children =
        [
            new Spec("STATICTEXT", "Menu.wnd:Title", 20, 20, 300, 60) { Text = "GUI:Title" },
            new Spec("USER", "Menu.wnd:Column", 600, 150, 780, 450)
            {
                System = "PassSelectedButtonsToParentSystem",
                Children =
                [
                    new Spec("PUSHBUTTON", "Menu.wnd:ButtonA", 610, 160, 770, 200) { Text = "GUI:A" },
                    new Spec("PUSHBUTTON", "Menu.wnd:ButtonB", 610, 210, 770, 250) { Text = "GUI:B" },
                    new Spec("PUSHBUTTON", "Menu.wnd:ButtonC", 610, 260, 770, 300) { Text = "GUI:C", Status = "ENABLED+HIDDEN" },
                    new Spec("CHECKBOX", "Menu.wnd:Check", 610, 310, 770, 340) { Text = "GUI:Check" },
                    new Spec("PUSHBUTTON", "Menu.wnd:Overlap", 700, 330, 770, 360) { Text = "GUI:Over" },
                ],
            },
            new Spec("USER", "Menu.wnd:Dialog", 250, 200, 550, 400)
            {
                Children =
                [
                    new Spec("SCROLLLISTBOX", "Menu.wnd:List", 260, 210, 540, 340),
                    new Spec("PUSHBUTTON", "Menu.wnd:Ok", 260, 350, 390, 390) { Text = "GUI:Ok" },
                    new Spec("PUSHBUTTON", "Menu.wnd:Cancel", 410, 350, 540, 390) { Text = "GUI:Cancel", Status = "NULL" },
                ],
            },
            new Spec("USER", "Menu.wnd:Bar", 0, 540, 800, 600)
            {
                Children = [new Spec("HORZSLIDER", "Menu.wnd:Volume", 20, 555, 220, 585), new Spec("PUSHBUTTON", "Menu.wnd:Back", 640, 550, 790, 590) { Text = "GUI:Back" }],
            },
        ],
    }) + Window(new Spec("USER", "Menu.wnd:HiRes", 1500, 20, 1900, 120)
    {
        CreationWidth = 1920,
        CreationHeight = 1080,
        Children = [new Spec("PUSHBUTTON", "Menu.wnd:HiResButton", 1520, 30, 1880, 110) { CreationWidth = 1920, CreationHeight = 1080 }],
    }));

    private static bool IsControl(UiWindow window) => window.IsGadget && window.IsVisible && window.IsEffectivelyEnabled;

    /// <summary>
    /// The four corner pixels and the centre of a control at its creation resolution, mapped through the control's transform.
    /// Mapping the authored points (instead of taking the corners of the scaled rectangle) keeps the probe on the same
    /// authored pixel, so a strictly increasing mapping must resolve it to the same window.
    /// </summary>
    private static (int X, int Y)[] Probes(UiWindow window)
    {
        var rect = window.Definition.Rect;
        (int X, int Y)[] authored =
        [
            (rect.Left, rect.Top), (rect.Right - 1, rect.Top), (rect.Left, rect.Bottom - 1), (rect.Right - 1, rect.Bottom - 1),
            (rect.Left + rect.Width / 2, rect.Top + rect.Height / 2),
        ];
        return authored.Select(point => (window.Transform.X.Map(point.X), window.Transform.Y.Map(point.Y))).ToArray();
    }

    private static void ScalingTableKeepsLayoutInvariants()
    {
        // Every control is probed at its own creation resolution (the file mixes 800x600 and 1920x1080 windows).
        var references = new Dictionary<(int, int), UiWindowTree>();
        UiWindowTree ReferenceFor(WndScreenRect rect)
        {
            var key = (rect.CreationWidth, rect.CreationHeight);
            if (!references.TryGetValue(key, out var reference)) references[key] = reference = Tree(MenuLayout, new UiViewport(key.CreationWidth, key.CreationHeight));
            return reference;
        }
        var baseline = Tree(MenuLayout);
        var referenceRects = baseline.AllWindows.ToDictionary(window => window.Order, window => window.Definition.Rect);
        var probes = baseline.AllWindows.Where(IsControl).Select(window =>
            {
                var reference = ReferenceFor(window.Definition.Rect);
                var own = reference.AllWindows.Single(candidate => candidate.Order == window.Order);
                return (window.Order, Hits: Probes(own).Select(point => reference.HitTest(point.X, point.Y)?.Order ?? -1).ToArray());
            })
            .ToArray();
        Check(probes.Length == 10, $"Expected 10 probe controls, got {probes.Length}.");
        var skipped = 0;
        foreach (var mode in Enum.GetValues<UiScaleMode>())
        {
            var tree = Tree(MenuLayout, options: new UiScaleOptions { Mode = mode });
            foreach (var viewport in Resolutions)
            {
                tree.SetViewport(viewport);
                var windows = tree.AllWindows.Where(window => window.IsVisible).ToArray();
                foreach (var window in windows)
                {
                    Check(window.Rect.IsInside(viewport.Bounds) || !window.Definition.Rect.IsInsideCreationArea,
                        $"{mode} {viewport}: {window.Name} {window.Rect} leaves the viewport.");
                    Check(window.Rect.Width > 0 && window.Rect.Height > 0, $"{mode} {viewport}: {window.Name} collapsed.");
                }
                // Overlap relations: identical for Stretch/Uniform; Anchor may only separate windows of different groups.
                for (var i = 0; i < windows.Length; i++)
                    for (var j = i + 1; j < windows.Length; j++)
                    {
                        var a = windows[i];
                        var b = windows[j];
                        if (a.Definition.Rect.CreationWidth != b.Definition.Rect.CreationWidth) continue;
                        var before = Overlaps(referenceRects[a.Order], referenceRects[b.Order]);
                        var after = a.Rect.Intersects(b.Rect);
                        if (mode == UiScaleMode.Anchor) Check(!after || before, $"{mode} {viewport}: new overlap {a.Name} / {b.Name}.");
                        else Check(before == after, $"{mode} {viewport}: overlap of {a.Name} / {b.Name} changed ({before} -> {after}).");
                    }
                // Corner and centre probes resolve to the same control at every resolution that does not shrink the
                // control's creation resolution (shrinking may merge neighbouring pixels, see docs/UI-WND.md).
                foreach (var (order, hits) in probes)
                {
                    var window = tree.AllWindows.Single(candidate => candidate.Order == order);
                    if (viewport.Width < window.Definition.Rect.CreationWidth || viewport.Height < window.Definition.Rect.CreationHeight)
                    {
                        skipped++;
                        continue;
                    }
                    var points = Probes(window);
                    for (var p = 0; p < points.Length; p++)
                    {
                        var hit = tree.HitTest(points[p].X, points[p].Y)?.Order ?? -1;
                        Check(hit == hits[p], $"{mode} {viewport}: probe {p} of {window.Name} hit {hit}, expected {hits[p]}.");
                    }
                }
            }
            // Deterministic: re-layout gives identical rectangles.
            tree.SetViewport(new UiViewport(1920, 1080));
            var first = tree.AllWindows.Select(window => window.Rect).ToArray();
            tree.SetViewport(new UiViewport(640, 480));
            tree.SetViewport(new UiViewport(1920, 1080));
            Check(first.SequenceEqual(tree.AllWindows.Select(window => window.Rect)), $"{mode}: layout is not reproducible.");
        }
        // Per mode: the nine 800x600 controls shrink at 640x480, the 1920x1080 control at seven resolutions.
        Check(skipped == 3 * (9 + 7), $"Skipped shrinking probes: {skipped}.");
    }

    private static bool Overlaps(WndScreenRect a, WndScreenRect b)
        => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    private static void FontScalingFollowsModeAndAccessibilityFactor()
    {
        var hd = new UiViewport(1920, 1080);
        Check(UiScaler.ScaleFontSize(12, true, hd, UiScaleOptions.Original, 800, 600) == 23, "Classic template scaling.");
        Check(UiScaler.ScaleFontSize(12, true, new UiViewport(3840, 1080), UiScaleOptions.Original, 800, 600) == 24, "Classic scaling must cap at 2.");
        Check(UiScaler.ScaleFontSize(12, true, new UiViewport(640, 480), UiScaleOptions.Original, 800, 600) == 12, "Classic scaling must not shrink.");
        Check(UiScaler.ScaleFontSize(12, false, hd, UiScaleOptions.Original, 800, 600) == 12, "Window FONT stays unscaled in Stretch.");
        Check(UiScaler.ScaleFontSize(12, false, hd, new UiScaleOptions { Mode = UiScaleMode.Uniform }, 800, 600) == 21, "Uniform: 12 * 1.8 = 21.6 -> 21.");
        Check(UiScaler.ScaleFontSize(12, false, hd, new UiScaleOptions { Mode = UiScaleMode.Anchor }, 1920, 1080) == 12, "Uniform scale relative to the window's creation resolution.");
        // A header template point size is measured against 800x600 whatever the layout's creation resolution
        // (GlobalLanguage::adjustFontSize): a 1920x1080 layout must not shrink its template fonts to 800/1920.
        foreach (var mode in new[] { UiScaleMode.Uniform, UiScaleMode.Anchor })
        {
            Check(UiScaler.ScaleFontSize(8, true, hd, new UiScaleOptions { Mode = mode }, 1920, 1080) == 14, $"{mode}: template 8 * 1.8 = 14.4 -> 14 in a 1920x1080 layout.");
            Check(UiScaler.ScaleFontSize(8, true, new UiViewport(1280, 720), new UiScaleOptions { Mode = mode }, 1920, 1080) == 9, $"{mode}: template 8 * 1.2 = 9.6 -> 9.");
            Check(UiScaler.ScaleFontSize(8, true, hd, new UiScaleOptions { Mode = mode }, 800, 600) == 14, $"{mode}: the same template size in an 800x600 layout.");
        }
        Check(UiScaler.ScaleFontSize(12, false, hd, new UiScaleOptions { TextScale = 1.5m }, 800, 600) == 18, "Accessibility text scale.");
        Check(UiScaler.ScaleFontSize(1, false, new UiViewport(640, 480), new UiScaleOptions { Mode = UiScaleMode.Uniform, TextScale = 0.1m }, 800, 600) == 1, "Font size floor is 1.");
    }

    private static void MinimumHitTargetEnlargesSmallControls()
    {
        var text = WndFile(Window(new Spec("USER", "T.wnd:Root", 0, 0, 800, 600)
        {
            Children = [new Spec("PUSHBUTTON", "T.wnd:Tiny", 100, 100, 104, 104), new Spec("STATICTEXT", "T.wnd:Label", 200, 100, 204, 104)],
        }));
        var plain = Tree(text);
        Check(plain.HitTest(110, 102)?.Name == "T.wnd:Root", "Without minimum the tiny button is missed.");
        var accessible = Tree(text, options: new UiScaleOptions { MinimumHitTarget = 24 });
        var tiny = Find(accessible, "T.wnd:Tiny");
        Check(tiny.Rect == new UiRect(100, 100, 4, 4) && tiny.HitRect == new UiRect(90, 90, 24, 24), $"Hit rect {tiny.HitRect}.");
        Check(accessible.HitTest(110, 102) == tiny, "Enlarged hit area not used.");
        Check(Find(accessible, "T.wnd:Label").HitRect == Find(accessible, "T.wnd:Label").Rect, "Non-gadget windows keep their hit rect.");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Runtime

    private static void HitTestingFollowsZOrderAndClipsChildren()
    {
        var text = WndFile(Window(new Spec("USER", "Z.wnd:Root", 100, 100, 300, 300)
        {
            Children =
            [
                new Spec("PUSHBUTTON", "Z.wnd:Below", 120, 120, 220, 220),
                new Spec("PUSHBUTTON", "Z.wnd:Above", 150, 150, 250, 250),
                new Spec("PUSHBUTTON", "Z.wnd:Outside", 250, 250, 400, 400),
                new Spec("PUSHBUTTON", "Z.wnd:Disabled", 120, 260, 200, 290) { Status = "NULL" },
                new Spec("PUSHBUTTON", "Z.wnd:Hidden", 210, 120, 290, 140) { Status = "ENABLED+HIDDEN" },
            ],
        }) + Window(new Spec("USER", "Z.wnd:Later", 280, 280, 350, 350)));
        var tree = Tree(text);
        Check(tree.FindByName("Z.wnd:Above")?.Name == "Z.wnd:Above" && tree.FindByName("Z.wnd:Nope") is null, "FindByName.");
        Check(tree.HitTest(160, 160)?.Name == "Z.wnd:Above", "Later sibling must be on top.");
        Check(tree.HitTest(130, 130)?.Name == "Z.wnd:Below", "Lower sibling outside the overlap.");
        Check(tree.HitTest(270, 270)?.Name == "Z.wnd:Outside", "Child inside its parent.");
        Check(tree.HitTest(320, 320)?.Name == "Z.wnd:Later", "Children are clipped to their parent; later root on top.");
        Check(tree.HitTest(380, 380) is null, "Child area outside its parent must not be hit.");
        Check(tree.HitTest(130, 270)?.Name == "Z.wnd:Root", "Disabled windows are skipped.");
        Check(tree.HitTest(250, 130)?.Name == "Z.wnd:Root", "Hidden windows are skipped.");
        Find(tree, "Z.wnd:Above").SetHidden(true);
        Check(tree.HitTest(160, 160)?.Name == "Z.wnd:Below", "Hiding at runtime.");
        Check(tree.HitTest(100, 100) is not null && tree.HitTest(300, 200)?.Name != "Z.wnd:Root", "Containment is half-open.");
        var popup = tree.AddLayout(WndParser.Parse(WndFile(Window(new Spec("USER", "Popup.wnd:Root", 0, 0, 800, 600) { Status = "ENABLED+ABOVE" })), "Popup.wnd"));
        Check(tree.HitTest(160, 160) == popup.Single(), "ABOVE windows cover everything.");
        tree.RemoveLayout("Popup.wnd");
        Check(tree.HitTest(160, 160)?.Name == "Z.wnd:Below" && tree.FindByName("Popup.wnd:Root") is null, "RemoveLayout removes windows and names.");
    }

    private static readonly string ButtonLayout = WndFile(Window(new Spec("USER", "B.wnd:Root", 0, 0, 800, 600)
    {
        System = "BSystem",
        Input = "BInput",
        Children =
        [
            new Spec("PUSHBUTTON", "B.wnd:Button", 100, 100, 200, 140),
            new Spec("PUSHBUTTON", "B.wnd:Down", 100, 150, 200, 190) { Status = "ENABLED+ON_MOUSE_DOWN" },
            new Spec("PUSHBUTTON", "B.wnd:Toggle", 100, 200, 200, 240) { Status = "ENABLED+CHECK_LIKE" },
            new Spec("PUSHBUTTON", "B.wnd:Right", 100, 250, 200, 290) { Status = "ENABLED+RIGHT_CLICK" },
            new Spec("PUSHBUTTON", "B.wnd:NoTrack", 100, 300, 200, 340) { Style = "PUSHBUTTON" },
        ],
    }));

    /// <summary>Messages other than hover and focus notifications.</summary>
    private static bool IsGadgetMessage(UiEvent uiEvent)
        => uiEvent.Kind is not (UiEventKind.FocusChange or UiEventKind.ButtonMouseEntering or UiEventKind.ButtonMouseLeaving);

    private static UiEvent[] Handled(UiWindowTree tree)
        => tree.Dispatch().Where(delivery => delivery.Outcome == UiDeliveryOutcome.Handled).Select(delivery => delivery.Event).ToArray();

    private static void PushButtonClickRequiresDownAndUpInside()
    {
        var tree = Tree(ButtonLayout);
        var received = new List<UiEvent>();
        tree.Register("BSystem", (_, uiEvent) => received.Add(uiEvent));
        var button = Find(tree, "B.wnd:Button");
        Check(tree.MouseMove(150, 120), "Pointer over the button must be consumed.");
        Check(button.IsHilited, "Mouse-track button must hilite.");
        tree.MouseDown(150, 120, UiMouseButton.Left);
        Check(button.IsSelected && tree.Grabbed == button, "Down must press and grab.");
        tree.MouseUp(150, 120, UiMouseButton.Left);
        tree.Dispatch();
        // A click does not move the keyboard focus to a button (GadgetPushButtonInput never calls winSetFocus).
        Check(received.Select(e => e.Kind).SequenceEqual([UiEventKind.ButtonMouseEntering, UiEventKind.ButtonSelected]),
            $"Click events: {string.Join(", ", received.Select(e => e.Kind))}");
        Check(tree.Focus is null, "Clicking a button must not focus it.");
        Check(received.Last() == new UiEvent(UiEventKind.ButtonSelected, "B.wnd:Button", 0, null, "B.wnd:Root"), $"Selected event: {received.Last()}");
        received.Clear();

        tree.MouseDown(150, 120, UiMouseButton.Left);
        tree.MouseMove(500, 500);
        tree.MouseUp(500, 500, UiMouseButton.Left);
        tree.Dispatch();
        Check(received.All(e => e.Kind != UiEventKind.ButtonSelected), "Up outside must not click.");
        Check(!button.IsSelected, "Button must release.");
        received.Clear();

        var down = Find(tree, "B.wnd:Down");
        tree.MouseDown(150, 170, UiMouseButton.Left);
        tree.Dispatch();
        Check(received.Count(e => e.Kind == UiEventKind.ButtonSelected && e.Source == down.Name) == 1, "ON_MOUSE_DOWN fires on down.");
        tree.MouseUp(150, 170, UiMouseButton.Left);
        tree.Dispatch();
        Check(received.Count(e => e.Kind == UiEventKind.ButtonSelected) == 1, "ON_MOUSE_DOWN must not fire again on up.");
        received.Clear();

        var toggle = Find(tree, "B.wnd:Toggle");
        Click(tree, toggle);
        Check(toggle.IsSelected, "Check-like toggles on.");
        Click(tree, toggle);
        tree.Dispatch();
        Check(!toggle.IsSelected && received.Where(e => e.Kind == UiEventKind.ButtonSelected).Select(e => e.Value).SequenceEqual([1, 0]), "Check-like toggle values.");
        received.Clear();

        tree.MouseDown(150, 270, UiMouseButton.Right);
        tree.MouseUp(150, 270, UiMouseButton.Right);
        tree.MouseDown(150, 120, UiMouseButton.Right);
        tree.MouseUp(150, 120, UiMouseButton.Right);
        tree.Dispatch();
        Check(received.Count(e => e.Kind == UiEventKind.ButtonSelectedRight) == 1 && received.Single(e => e.Kind == UiEventKind.ButtonSelectedRight).Source == "B.wnd:Right",
            "Right click only with RIGHT_CLICK status.");
        received.Clear();

        tree.MouseMove(150, 320);
        tree.Dispatch();
        Check(!Find(tree, "B.wnd:NoTrack").IsHilited && received.All(e => e.Source != "B.wnd:NoTrack"), "Without MOUSETRACK there is no hilite or entering message.");
    }

    private static void MessagesRouteThroughCallbacks()
    {
        var text = WndFile(Window(new Spec("USER", "R.wnd:Root", 0, 0, 800, 600)
        {
            System = "RootSystem",
            Input = "RootInput",
            Children =
            [
                new Spec("USER", "R.wnd:Pass", 0, 0, 400, 300)
                {
                    System = "PassSelectedButtonsToParentSystem",
                    Children = [new Spec("PUSHBUTTON", "R.wnd:A", 10, 10, 100, 40), new Spec("SCROLLLISTBOX", "R.wnd:ListA", 10, 50, 200, 200)],
                },
                new Spec("USER", "R.wnd:All", 400, 0, 800, 300)
                {
                    System = "PassMessagesToParentSystem",
                    Children = [new Spec("SCROLLLISTBOX", "R.wnd:ListB", 410, 50, 600, 200)],
                },
                new Spec("USER", "R.wnd:Silent", 0, 300, 400, 600) { Children = [new Spec("PUSHBUTTON", "R.wnd:B", 10, 310, 100, 340)] },
                new Spec("USER", "R.wnd:Custom", 400, 300, 800, 600)
                {
                    System = "UnknownMenuSystem",
                    Input = "GameWinBlockInput",
                    Children = [new Spec("PUSHBUTTON", "R.wnd:C", 410, 310, 500, 340)],
                },
            ],
        }));
        var tree = Tree(text);
        tree.Register("RootSystem", (_, _) => { });
        tree.Register("RootInput", (_, _) => { });
        Find(tree, "R.wnd:ListA").AddListRow(new UiListRow("x"));
        Find(tree, "R.wnd:ListB").AddListRow(new UiListRow("y"));
        tree.MouseMove(700, 700);
        tree.Dispatch();

        Click(tree, Find(tree, "R.wnd:A"));
        var deliveries = tree.Dispatch();
        var selected = deliveries.Single(delivery => delivery.Event.Kind == UiEventKind.ButtonSelected);
        Check(selected.Outcome == UiDeliveryOutcome.Handled && selected.Callback == "RootSystem" && selected.Event.Target == "R.wnd:Root",
            $"PassSelectedButtonsToParentSystem must forward GBM_SELECTED: {selected}");
        tree.SetFocus(Find(tree, "R.wnd:A"));
        Check(tree.Dispatch().Single(delivery => delivery.Event.Kind == UiEventKind.FocusChange).Outcome == UiDeliveryOutcome.Filtered,
            "PassSelectedButtonsToParentSystem must stop other kinds.");
        tree.SetFocus(null);
        tree.Dispatch();

        tree.MouseDown(50, 60, UiMouseButton.Left);
        tree.MouseUp(50, 60, UiMouseButton.Left);
        Check(tree.Dispatch().Single(delivery => delivery.Event.Kind == UiEventKind.ListSelected).Outcome == UiDeliveryOutcome.Filtered,
            "GLM_SELECTED is not forwarded by PassSelectedButtonsToParentSystem.");
        tree.MouseDown(450, 60, UiMouseButton.Left);
        tree.MouseUp(450, 60, UiMouseButton.Left);
        Check(tree.Dispatch().Single(delivery => delivery.Event.Kind == UiEventKind.ListSelected) is { Outcome: UiDeliveryOutcome.Handled, Callback: "RootSystem" },
            "PassMessagesToParentSystem forwards everything.");

        Click(tree, Find(tree, "R.wnd:B"));
        Check(tree.Dispatch().Single(delivery => delivery.Event.Kind == UiEventKind.ButtonSelected) is { Outcome: UiDeliveryOutcome.NoCallback, Callback: null },
            "[None] owner ends the message.");

        Click(tree, Find(tree, "R.wnd:C"));
        Click(tree, Find(tree, "R.wnd:C"));
        tree.Dispatch();
        Check(tree.UnregisteredCallbacks.GetValueOrDefault("UnknownMenuSystem") >= 2, "Unregistered callback names must be counted.");

        tree.SetFocus(Find(tree, "R.wnd:C"));
        Check(tree.KeyDown(UiKey.Escape), "GameWinBlockInput consumes keys.");
        Check(tree.Dispatch().Single(delivery => delivery.Event.Kind == UiEventKind.Key).Outcome == UiDeliveryOutcome.Blocked, "Blocked key.");
        tree.SetFocus(Find(tree, "R.wnd:A"));
        Check(tree.KeyDown(UiKey.Escape), "RootInput receives unconsumed keys.");
        var key = tree.Dispatch().Single(delivery => delivery.Event.Kind == UiEventKind.Key);
        Check(key is { Outcome: UiDeliveryOutcome.Handled, Callback: "RootInput" } && key.Event.Value == (int)UiKey.Escape, $"Key routing: {key}");
    }

    private static void CheckBoxAndRadioGroups()
    {
        var text = WndFile(Window(new Spec("USER", "C.wnd:Root", 0, 0, 800, 600)
        {
            System = "CSystem",
            Children =
            [
                new Spec("CHECKBOX", "C.wnd:Check", 10, 10, 200, 40),
                new Spec("RADIOBUTTON", "C.wnd:R1", 10, 50, 200, 80) { Extra = "  RADIOBUTTONDATA = GROUP: 1;\n" },
                new Spec("RADIOBUTTON", "C.wnd:R2", 10, 90, 200, 120) { Extra = "  RADIOBUTTONDATA = GROUP: 1;\n" },
                new Spec("RADIOBUTTON", "C.wnd:Other", 10, 130, 200, 160) { Extra = "  RADIOBUTTONDATA = GROUP: 2;\n" },
            ],
        }));
        var tree = Tree(text);
        tree.Register("CSystem", (_, _) => { });
        var check = Find(tree, "C.wnd:Check");
        Click(tree, check);
        Check(check.IsSelected, "Check box toggles on up inside.");
        Click(tree, check);
        var values = Handled(tree).Where(e => e.Kind == UiEventKind.ButtonSelected).Select(e => e.Value).ToArray();
        Check(!check.IsSelected && values.SequenceEqual([1, 0]), $"Check box values: {string.Join(",", values)}");
        tree.MouseDown(check.Rect.CenterX, check.Rect.CenterY, UiMouseButton.Left);
        tree.MouseUp(700, 500, UiMouseButton.Left);
        Check(!check.IsSelected, "Check box must not toggle when released outside.");

        var (r1, r2, other) = (Find(tree, "C.wnd:R1"), Find(tree, "C.wnd:R2"), Find(tree, "C.wnd:Other"));
        Click(tree, other);
        Click(tree, r1);
        Click(tree, r2);
        Check(!r1.IsSelected && r2.IsSelected && other.IsSelected, "Radio groups are exclusive per group only.");
        Click(tree, r2);
        Check(Handled(tree).Count(e => e.Kind == UiEventKind.ButtonSelected) == 3, "Selecting an already selected radio sends nothing.");
        r1.SetChecked(true);
        Check(r1.IsSelected && !r2.IsSelected && Handled(tree).Length == 0, "SetChecked is silent but keeps the group exclusive.");
    }

    private static void SlidersTrackAndFinish()
    {
        var text = WndFile(Window(new Spec("USER", "S.wnd:Root", 0, 0, 800, 600)
        {
            System = "SSystem",
            Children = [new Spec("HORZSLIDER", "S.wnd:H", 100, 100, 313, 120), new Spec("VERTSLIDER", "S.wnd:V", 400, 100, 420, 316)],
        }));
        var tree = Tree(text);
        tree.Register("SSystem", (_, _) => { });
        var slider = Find(tree, "S.wnd:H");
        Check(slider.Slider!.Value == 0 && UiGadgetGeometry.SliderThumb(slider) == new UiRect(100, 110, 13, 16), $"Initial thumb {UiGadgetGeometry.SliderThumb(slider)}.");
        // Travel is 200 px for 100 steps: pointer at the thumb centre of value 50 is 100 + 100 + 6.
        tree.MouseDown(206, 110, UiMouseButton.Left);
        Check(slider.Slider.Value == 50, $"Value after press: {slider.Slider.Value}");
        tree.MouseMove(1000, 110);
        Check(slider.Slider.Value == 100, "Drag beyond the end clamps.");
        tree.MouseUp(1000, 110, UiMouseButton.Left);
        var events = Handled(tree).Where(e => e.Source == "S.wnd:H" && e.Kind is UiEventKind.SliderTrack or UiEventKind.SliderDone).ToArray();
        Check(events.Select(e => (e.Kind, e.Value)).SequenceEqual([(UiEventKind.SliderTrack, 50), (UiEventKind.SliderTrack, 100), (UiEventKind.SliderDone, 100)]),
            $"Slider events: {string.Join(", ", events.Select(e => $"{e.Kind}:{e.Value}"))}");
        Check(tree.Focus is null, "Dragging a slider must not focus it (GadgetHorizontalSliderInput never calls winSetFocus).");
        tree.SetFocus(slider);
        tree.KeyDown(UiKey.Left);
        Check(slider.Slider.Value == 99, "Left arrow decreases.");

        var vertical = Find(tree, "S.wnd:V");
        tree.MouseDown(410, 108, UiMouseButton.Left);
        Check(vertical.Slider!.Value == 100, "Vertical sliders have the maximum at the top.");
        tree.MouseUp(410, 108, UiMouseButton.Left);
        vertical.SetSliderValue(-5);
        Check(vertical.Slider.Value == 0, "SetSliderValue clamps.");
    }

    private static void ListBoxSelectionScrollingAndColumns()
    {
        var text = WndFile(Window(new Spec("USER", "L.wnd:Root", 0, 0, 800, 600)
        {
            System = "LSystem",
            Children =
            [
                new Spec("SCROLLLISTBOX", "L.wnd:List", 100, 100, 316, 160)
                {
                    Extra = "  LISTBOXDATA = LENGTH: 5,\n    AUTOSCROLL: 0,\n    AUTOPURGE: 1,\n    SCROLLBAR: 1,\n    MULTISELECT: 0,\n    COLUMNS: 2,\n    COLUMNSWIDTH: 25,\n    COLUMNSWIDTH: 75,\n    FORCESELECT: 0;\n",
                },
            ],
        }));
        var tree = Tree(text);
        tree.Register("LSystem", (_, _) => { });
        var list = Find(tree, "L.wnd:List");
        for (var i = 0; i < 6; i++) list.AddListRow(new UiListRow($"n{i}", $"row {i}"));
        Check(list.ListBox!.Rows.Count == 5 && list.ListBox.Rows[0].Cells[0] == "n1", "AUTOPURGE drops the oldest row at LENGTH.");
        // Font 10 -> row height 12; content 60 px high -> 5 visible rows; scroll bar 16 px.
        Check(UiGadgetGeometry.VisibleRows(list) == 5 && UiGadgetGeometry.ListContent(list) == new UiRect(100, 100, 200, 60), "List geometry.");
        var columns = UiGadgetGeometry.Columns(list, new UiRect(100, 100, 200, 12));
        Check(columns[0] == new UiRect(100, 100, 50, 12) && columns[1] == new UiRect(150, 100, 150, 12), "Column widths.");
        tree.MouseDown(120, 127, UiMouseButton.Left);
        tree.MouseUp(120, 127, UiMouseButton.Left);
        Check(list.ListBox.SelectedIndex == 2, $"Row click selected {list.ListBox.SelectedIndex}.");
        tree.AdvanceTo(300);
        tree.MouseDown(120, 127, UiMouseButton.Left);
        tree.MouseUp(120, 127, UiMouseButton.Left);
        tree.AdvanceTo(2000);
        tree.MouseDown(120, 127, UiMouseButton.Left);
        tree.MouseUp(120, 127, UiMouseButton.Left);
        tree.KeyDown(UiKey.Down);
        tree.MouseDown(120, 127, UiMouseButton.Right);
        var events = Handled(tree).Where(e => e.Source == list.Name && IsGadgetMessage(e)).Select(e => $"{e.Kind}:{e.Value}").ToArray();
        Check(events.SequenceEqual(["ListSelected:2", "ListSelected:2", "ListDoubleClicked:2", "ListSelected:2", "ListSelected:3", "ListRightClicked:2"]),
            $"List events: {string.Join(", ", events)}");
        list.ClearList();
        for (var i = 0; i < 5; i++) list.AddListRow(new UiListRow($"r{i}"));
        Check(tree.MouseWheel(150, 120, -3) && list.ListBox.TopIndex == 0, "Wheel cannot scroll a list that fits.");
    }

    private static void ComboBoxDropDown()
    {
        var text = WndFile(Window(new Spec("USER", "K.wnd:Root", 0, 0, 800, 600)
        {
            System = "KSystem",
            Children = [new Spec("COMBOBOX", "K.wnd:Combo", 100, 100, 300, 120), new Spec("PUSHBUTTON", "K.wnd:Below", 100, 125, 300, 200)],
        }));
        var tree = Tree(text);
        tree.Register("KSystem", (_, _) => { });
        var combo = Find(tree, "K.wnd:Combo");
        foreach (var entry in new[] { "Easy", "Medium", "Hard", "Brutal" }) combo.AddComboEntry(entry);
        Click(tree, combo);
        Check(combo.ComboBox!.IsOpen && tree.OpenComboBox == combo, "Click opens the drop-down.");
        // MAXDISPLAY 3, row height 12: the list covers y 120..156 and lies above the button below.
        Check(UiGadgetGeometry.ComboDropDown(combo) == new UiRect(100, 120, 200, 36), $"Drop-down {UiGadgetGeometry.ComboDropDown(combo)}.");
        Check(tree.InputTarget(150, 140) == combo, "The open drop-down takes input before the windows below.");
        tree.MouseDown(150, 140, UiMouseButton.Left);
        tree.MouseUp(150, 140, UiMouseButton.Left);
        Check(!combo.ComboBox.IsOpen && combo.ComboBox.SelectedIndex == 1, $"Pick selects and closes ({combo.ComboBox.SelectedIndex}).");
        tree.KeyDown(UiKey.Down);
        Click(tree, combo);
        tree.KeyDown(UiKey.Escape);
        Check(!combo.ComboBox.IsOpen, "Escape closes the drop-down.");
        Click(tree, combo);
        Click(tree, Find(tree, "K.wnd:Below"));
        Check(!combo.ComboBox.IsOpen, "Clicking elsewhere closes the drop-down.");
        var events = Handled(tree).Where(e => e.Kind == UiEventKind.ComboSelected).Select(e => e.Value).ToArray();
        Check(events.SequenceEqual([1, 2]), $"Combo events: {string.Join(",", events)}");
    }

    private static void TextEntryEditing()
    {
        var text = WndFile(Window(new Spec("USER", "E.wnd:Root", 0, 0, 800, 600)
        {
            System = "ESystem",
            Children =
            [
                new Spec("ENTRYFIELD", "E.wnd:Name", 100, 100, 300, 120) { Extra = "  TEXTENTRYDATA = MAXLEN: 6,\n    SECRETTEXT: 0,\n    NUMERICALONLY: 0,\n    ALPHANUMERICALONLY: 1,\n    ASCIIONLY: 0;\n" },
                new Spec("ENTRYFIELD", "E.wnd:Port", 100, 130, 300, 150) { Extra = "  TEXTENTRYDATA = MAXLEN: 5,\n    SECRETTEXT: 1,\n    NUMERICALONLY: 1,\n    ALPHANUMERICALONLY: 0,\n    ASCIIONLY: 0;\n" },
            ],
        }));
        var tree = Tree(text);
        tree.Register("ESystem", (_, _) => { });
        var name = Find(tree, "E.wnd:Name");
        Click(tree, name);
        Check(tree.Focus == name, "Click focuses the entry.");
        foreach (var c in "ab c!defgh") tree.Character(c);
        Check(name.TextEntry!.Text == "abcdef", $"Filtered, length-limited text: {name.TextEntry.Text}");
        tree.KeyDown(UiKey.Left);
        tree.KeyDown(UiKey.Left, UiModifiers.Shift);
        tree.KeyDown(UiKey.Left, UiModifiers.Shift);
        Check(name.TextEntry.SelectionStart == 3 && name.TextEntry.SelectionEnd == 5, "Shift+Left selects.");
        tree.Character('X');
        Check(name.TextEntry.Text == "abcXf" && name.TextEntry.Caret == 4, $"Typing replaces the selection: {name.TextEntry.Text}");
        tree.KeyDown(UiKey.Backspace);
        tree.KeyDown(UiKey.Home);
        tree.KeyDown(UiKey.Delete);
        Check(name.TextEntry.Text == "bcf" && name.TextEntry.Caret == 0, $"Backspace/Delete: {name.TextEntry.Text}");
        tree.KeyDown(UiKey.Enter);
        var events = Handled(tree).Where(e => e.Source == name.Name && IsGadgetMessage(e)).ToArray();
        Check(events.Count(e => e.Kind == UiEventKind.EntryUpdateText) == 9 && events.Last() == new UiEvent(UiEventKind.EntryEditDone, name.Name, 0, "bcf", "E.wnd:Root"),
            $"Entry events: {string.Join(", ", events.Select(e => $"{e.Kind}:{e.Text}"))}");
        var port = Find(tree, "E.wnd:Port");
        tree.SetFocus(port);
        foreach (var c in "8a0x8") tree.Character(c);
        Check(port.TextEntry!.Text == "808" && port.TextEntry.DisplayText == "***", "Numerical-only and secret text.");
        // Caret placement: monospace 5 px per character, text starts 4 px inside; x = 115 lies between characters 1 and 2.
        tree.MouseDown(115, 140, UiMouseButton.Left);
        tree.MouseUp(115, 140, UiMouseButton.Left);
        Check(port.TextEntry.Caret == 2, $"Caret from click: {port.TextEntry.Caret}");
    }

    private static void ProgressBarAndTabControl()
    {
        var tree = Tree(AllTypes);
        var progress = Find(tree, "All.wnd:Progress");
        progress.SetProgress(150);
        Check(progress.ProgressPercent == 100, "Progress clamps.");
        progress.SetProgress(25);
        Check(UiGadgetGeometry.ProgressFill(progress) == new UiRect(10, 290, 47, 20), $"Progress fill {UiGadgetGeometry.ProgressFill(progress)}.");
        var tabs = Find(tree, "All.wnd:Tabs");
        Check(!Find(tree, "All.wnd:Pane0").IsHidden && Find(tree, "All.wnd:Pane1").IsHidden, "First pane active after creation.");
        tree.MouseDown(460, 20, UiMouseButton.Left);
        tree.MouseUp(460, 20, UiMouseButton.Left);
        Check(tabs.TabControl!.ActiveTab == 1 && Find(tree, "All.wnd:Pane0").IsHidden && Find(tree, "All.wnd:Deep").IsVisible, "Tab header click switches panes.");
    }

    private static void KeyboardNavigationReachesEveryControl()
    {
        var tree = Tree(MenuLayout);
        tree.Register("MenuSystem", (_, _) => { });
        var order = tree.FocusOrder().Select(window => window.Name).ToArray();
        // Top edge first, then left edge: the list (y 210, x 260) precedes ButtonB (y 210, x 610).
        string[] expected = ["Menu.wnd:HiResButton", "Menu.wnd:ButtonA", "Menu.wnd:List", "Menu.wnd:ButtonB", "Menu.wnd:Check", "Menu.wnd:Overlap", "Menu.wnd:Ok", "Menu.wnd:Back", "Menu.wnd:Volume"];
        Check(order.SequenceEqual(expected), $"Reading order: {string.Join(", ", order)}");
        var visited = new List<string>();
        for (var i = 0; i < expected.Length; i++)
        {
            Check(tree.KeyDown(UiKey.Tab), "Tab is consumed.");
            visited.Add(tree.Focus!.Name);
        }
        Check(visited.SequenceEqual(expected), "Tab visits every enabled control once (hidden and disabled are skipped).");
        tree.KeyDown(UiKey.Tab);
        Check(tree.Focus!.Name == expected[0], "Tab wraps around.");
        tree.KeyDown(UiKey.Tab, UiModifiers.Shift);
        Check(tree.Focus!.Name == expected[^1], "Shift+Tab goes backwards.");
        tree.SetFocus(Find(tree, "Menu.wnd:ButtonA"));
        Check(Find(tree, "Menu.wnd:ButtonA").IsHilited, "Focus hilites buttons.");
        tree.Dispatch();
        tree.KeyDown(UiKey.Enter);
        Check(Handled(tree).Single() == new UiEvent(UiEventKind.ButtonSelected, "Menu.wnd:ButtonA", 0, null, "Menu.wnd:Background"), "Enter activates the focused button.");
        tree.KeyDown(UiKey.Down);
        Check(tree.Focus!.Name == "Menu.wnd:List", "Arrow keys on buttons move the focus in reading order.");
        tree.SetFocus(Find(tree, "Menu.wnd:ButtonB"));
        Find(tree, "Menu.wnd:Column").SetEnabled(false);
        Check(!tree.FocusOrder().Any(window => window.Parent?.Name == "Menu.wnd:Column") && tree.Focus is null, "Disabling a parent removes its controls and the focus.");
    }

    private static void TooltipStateMachine()
    {
        var text = WndFile(Window(new Spec("USER", "T.wnd:Root", 0, 0, 800, 600)
        {
            Children =
            [
                new Spec("PUSHBUTTON", "T.wnd:Default", 10, 10, 100, 40) { TooltipText = "TOOLTIP:Default" },
                new Spec("PUSHBUTTON", "T.wnd:Fast", 10, 50, 100, 80) { TooltipText = "TOOLTIP:Fast", TooltipDelay = 100 },
                new Spec("PUSHBUTTON", "T.wnd:Suppressed", 10, 90, 100, 120) { TooltipText = "TOOLTIP:Fast", Tooltip = "GameWinDefaultTooltip" },
                new Spec("PUSHBUTTON", "T.wnd:Dynamic", 10, 130, 100, 160) { Tooltip = "DynamicTooltip", TooltipDelay = 0 },
                new Spec("PUSHBUTTON", "T.wnd:Missing", 10, 170, 100, 200) { Tooltip = "MissingTooltip", TooltipDelay = 0 },
            ],
        }));
        var tree = Tree(text, services: new UiTreeServices(new MonoMeasure()) { Text = new FakeText(new() { ["TOOLTIP:Fast"] = "Quick" }) });
        tree.RegisterTooltip("DynamicTooltip", (_, window) => $"dynamic {window.Name}");
        tree.AdvanceTo(1000);
        tree.MouseMove(50, 20);
        tree.AdvanceTo(1799);
        Check(tree.Tooltip is null, "Tooltip must wait for the Mouse.ini delay.");
        tree.AdvanceTo(1800);
        Check(tree.Tooltip == new UiTooltip("T.wnd:Default", "TOOLTIP:Default", "TOOLTIP:Default", 50, 20), $"Unresolved label tooltip: {tree.Tooltip}");
        tree.MouseMove(60, 20);
        Check(tree.Tooltip is null, "Moving the pointer hides the tooltip and restarts the delay (the pointer must be still).");
        tree.AdvanceTo(2599);
        Check(tree.Tooltip is null, "The restarted delay has not passed.");
        tree.AdvanceTo(2650);
        Check(tree.Tooltip is { AgeMs: 50 }, $"The age counts from the end of the delay: {tree.Tooltip}");
        tree.MouseMove(50, 60);
        Check(tree.Tooltip is null, "Changing the window restarts the delay.");
        tree.AdvanceTo(2750);
        Check(tree.Tooltip?.Text == "Quick", "TOOLTIPDELAY 100 and resolved label.");
        tree.MouseMove(50, 100);
        tree.AdvanceTo(5000);
        Check(tree.Tooltip is null, "GameWinDefaultTooltip suppresses the tooltip text.");
        tree.MouseMove(50, 140);
        Check(tree.Tooltip?.Text == "dynamic T.wnd:Dynamic", "Registered tooltip callback.");
        tree.MouseMove(50, 180);
        Check(tree.Tooltip is null && tree.Tooltip is null && tree.UnregisteredCallbacks["MissingTooltip"] == 1, "Unregistered tooltip callback counted once per hover.");
        tree.MouseDown(50, 180, UiMouseButton.Left);
        Check(tree.Tooltip is null, "No tooltip while a button is held.");
        Check(ThrowsArgument(() => tree.AdvanceTo(10)), "The clock must not run backwards.");
    }

    private static bool ThrowsArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return true; }
        return false;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Draw list

    private static readonly string GoldenLayout = WndFile(Window(new Spec("USER", "G.wnd:Root", 0, 0, 800, 600)
    {
        Status = "ENABLED+IMAGE",
        DrawData = Draw("ENABLEDDRAWDATA", Entry("Background", "255 255 255 255", "255 255 255 255")),
        Children =
        [
            new Spec("PUSHBUTTON", "G.wnd:Ok", 100, 100, 300, 140)
            {
                Text = "GUI:Ok",
                DrawData = Draw("ENABLEDDRAWDATA", Entry("NoImage", "10 20 30 255", "40 50 60 255"))
                    + Draw("HILITEDRAWDATA", Entry("NoImage", "70 80 90 255", "40 50 60 255"), Entry("NoImage", "90 91 92 255", "93 94 95 255")),
            },
            new Spec("STATICTEXT", "G.wnd:Label", 100, 200, 160, 240) { Text = "GUI:Label", Extra = "  STATICTEXTDATA = CENTERED: 1;\n", Status = "ENABLED+WRAP_CENTERED", DrawData = string.Empty },
            new Spec("CHECKBOX", "G.wnd:Check", 100, 300, 300, 330)
            {
                Text = "GUI:Check",
                DrawData = Draw("ENABLEDDRAWDATA", Undefined, Entry("NoImage", "1 2 3 255", "4 5 6 255"), Entry("NoImage", "7 8 9 255", "10 11 12 255")),
            },
            new Spec("PUSHBUTTON", "G.wnd:Custom", 400, 100, 500, 140) { DrawCallback = "W3DMainMenuDraw" },
            new Spec("PUSHBUTTON", "G.wnd:Invisible", 400, 200, 500, 240) { DrawCallback = "W3DNoDraw" },
        ],
    }));

    private static void DrawListGolden()
    {
        var tree = Tree(GoldenLayout, services: new UiTreeServices(new MonoMeasure()) { Text = new FakeText(new() { ["GUI:Ok"] = "OK", ["GUI:Label"] = "Hello wide world" }) });
        tree.MouseMove(150, 120);
        tree.MouseDown(150, 120, UiMouseButton.Left);
        Find(tree, "G.wnd:Check").SetChecked(true);
        var list = tree.BuildDrawList();
        const string expected = """
            image Background 0,0 800x600 tint 255,255,255,255
            border 100,100 200x40 93,94,95,255
            fill 101,101 198x38 90,91,92,255
            text "OK" key=GUI:Ok font=Arial/10 color=255,255,0,255 shadow=0,0,0,255 at 195,114 center clip 0,0 800x600
            text "Hello" key=GUI:Label font=Arial/10 color=255,255,255,255 shadow=0,0,0,255 at 115,202 center clip 100,200 60x40
            text "wide" key=GUI:Label font=Arial/10 color=255,255,255,255 shadow=0,0,0,255 at 117,214 center clip 100,200 60x40
            text "world" key=GUI:Label font=Arial/10 color=255,255,255,255 shadow=0,0,0,255 at 117,226 center clip 100,200 60x40
            border 100,303 24x24 10,11,12,255
            fill 101,304 22x22 7,8,9,255
            fill 106,309 12x12 255,255,255,255
            text "GUI:Check" key=GUI:Check font=Arial/10 color=255,255,255,255 shadow=0,0,0,255 at 130,309 left clip 0,0 800x600
            custom W3DMainMenuDraw G.wnd:Custom 400,100 100x40

            """;
        var actual = list.Dump();
        Check(actual == expected.Replace("\r\n", "\n", StringComparison.Ordinal), $"Draw list golden mismatch:\n{actual}");
        Check(list.Statistics is { UnresolvedLabels: 1, WindowsDrawn: 6 } && list.Statistics.CustomDraws["W3DMainMenuDraw"] == 1, $"Statistics: {list.Statistics}");
        Check(tree.BuildDrawList().Dump() == actual, "Draw list must be reproducible.");
    }

    private static void ThreePieceButtonsTileTheirCenter()
    {
        var text = WndFile(Window(new Spec("PUSHBUTTON", "P.wnd:Button", 100, 100, 200, 130)
        {
            Status = "ENABLED+IMAGE",
            DrawData = Draw("ENABLEDDRAWDATA", Entry("L", "0 0 0 0", "0 0 0 0"), Undefined, Undefined, Undefined, Undefined, Entry("M", "0 0 0 0", "0 0 0 0"), Entry("R", "0 0 0 0", "0 0 0 0")),
        }));
        var images = new FakeImages(new() { ["L"] = (10, 30), ["M"] = (32, 30), ["R"] = (12, 30) });
        var tree = Tree(text, services: new UiTreeServices(new MonoMeasure()) { Images = images });
        var dump = tree.BuildDrawList().Dump();
        const string expected = """
            clip-push 110,100 78x30
            image M 110,100 32x30 tint 255,255,255,255
            image M 142,100 32x30 tint 255,255,255,255
            image M 174,100 32x30 tint 255,255,255,255
            clip-pop
            image L 100,100 10x30 tint 255,255,255,255
            image R 188,100 12x30 tint 255,255,255,255

            """;
        Check(dump == expected.Replace("\r\n", "\n", StringComparison.Ordinal), $"Three-piece mismatch:\n{dump}");
        var unresolved = Tree(text).BuildDrawList();
        Check(unresolved.Statistics.UnresolvedImageSizes == 3, $"Unknown image sizes must be counted: {unresolved.Statistics.UnresolvedImageSizes}");
    }

    private static string StateLayout(string status) => WndFile(Window(new Spec("USER", "S.wnd:Root", 0, 0, 800, 600)
    {
        System = "SSystem",
        Children =
        [
            new Spec("PUSHBUTTON", "S.wnd:Three", 100, 100, 200, 130)
            {
                Status = status + "+IMAGE",
                Text = "GUI:Three",
                DrawData = Draw("ENABLEDDRAWDATA", Entry("L", "0 0 0 0", "0 0 0 0"), Undefined, Undefined, Undefined, Undefined, Entry("M", "0 0 0 0", "0 0 0 0"), Entry("R", "0 0 0 0", "0 0 0 0"))
                    + Draw("DISABLEDDRAWDATA", Entry("DL", "0 0 0 0", "0 0 0 0"), Undefined, Undefined, Undefined, Undefined, Entry("DM", "0 0 0 0", "0 0 0 0"), Entry("DR", "0 0 0 0", "0 0 0 0"))
                    + Draw("HILITEDRAWDATA", Entry("HL", "0 0 0 0", "0 0 0 0"), Entry("PL", "0 0 0 0", "0 0 0 0"), Undefined, Entry("PM", "0 0 0 0", "0 0 0 0"), Entry("PR", "0 0 0 0", "0 0 0 0"),
                        Entry("HM", "0 0 0 0", "0 0 0 0"), Entry("HR", "0 0 0 0", "0 0 0 0")),
            },
            new Spec("PUSHBUTTON", "S.wnd:One", 100, 200, 200, 230)
            {
                Status = status + "+IMAGE",
                DrawData = Draw("ENABLEDDRAWDATA", Entry("E", "0 0 0 0", "0 0 0 0")) + Draw("DISABLEDDRAWDATA", Entry("D", "0 0 0 0", "0 0 0 0"))
                    + Draw("HILITEDRAWDATA", Entry("H", "0 0 0 0", "0 0 0 0"), Entry("P", "0 0 0 0", "0 0 0 0")),
            },
            new Spec("PUSHBUTTON", "S.wnd:Color", 100, 300, 200, 330)
            {
                Status = status,
                DrawData = Draw("ENABLEDDRAWDATA", Entry("NoImage", "1 1 1 255", "2 2 2 255")) + Draw("DISABLEDDRAWDATA", Entry("NoImage", "3 3 3 255", "4 4 4 255"))
                    + Draw("HILITEDRAWDATA", Entry("NoImage", "5 5 5 255", "6 6 6 255"), Entry("NoImage", "7 7 7 255", "8 8 8 255")),
            },
            new Spec("PUSHBUTTON", "S.wnd:Other", 400, 100, 500, 130),
        ],
    }));

    private static string[] ImagesOf(UiWindowTree tree) => tree.BuildDrawList().Commands.OfType<UiImageDraw>().Select(draw => draw.Image).Distinct().ToArray();

    /// <summary>
    /// The image and colour a push button draws per state, like <c>W3DGadgetPushButtonImageDrawThree</c>,
    /// <c>W3DGadgetPushButtonImageDrawOne</c> and <c>W3DGadgetPushButtonDraw</c>: enabled 0/5/6, hilite 0/5/6 under the
    /// pointer, hilite 1/3/4 while pressed, disabled 0/5/6; the text takes the matching <c>TEXTCOLOR</c>.
    /// </summary>
    private static void PushButtonStatesPickTheirDrawData()
    {
        var images = new FakeImages(new() { ["L"] = (10, 30), ["M"] = (40, 30), ["R"] = (10, 30), ["HL"] = (10, 30), ["HM"] = (40, 30), ["HR"] = (10, 30),
            ["PL"] = (10, 30), ["PM"] = (40, 30), ["PR"] = (10, 30), ["DL"] = (10, 30), ["DM"] = (40, 30), ["DR"] = (10, 30) });
        var services = new UiTreeServices(new MonoMeasure()) { Images = images, Text = new FakeText(new() { ["GUI:Three"] = "Go" }) };
        var tree = Tree(StateLayout("ENABLED"), services: services);
        WndColor TextColor() => tree.BuildDrawList().Commands.OfType<UiTextDraw>().Single().Color;
        WndColor[] Fills() => tree.BuildDrawList().Commands.OfType<UiFillRect>().Select(fill => fill.Color).ToArray();

        Check(ImagesOf(tree).SequenceEqual(["M", "L", "R", "E"]), $"Enabled images: {string.Join(",", ImagesOf(tree))}");
        Check(TextColor() == new WndColor(255, 255, 255, 255) && Fills().Contains(new WndColor(1, 1, 1, 255)), "Enabled text and fill colour.");

        tree.MouseMove(150, 115);
        Check(ImagesOf(tree).SequenceEqual(["HM", "HL", "HR", "E"]), $"Hovered three-piece images: {string.Join(",", ImagesOf(tree))}");
        Check(TextColor() == new WndColor(255, 255, 0, 255), "Hovered text takes the hilite colour.");
        tree.MouseDown(150, 115, UiMouseButton.Left);
        Check(ImagesOf(tree).SequenceEqual(["PM", "PL", "PR", "E"]), $"Pressed three-piece images: {string.Join(",", ImagesOf(tree))}");
        tree.MouseUp(150, 115, UiMouseButton.Left);
        Check(ImagesOf(tree).SequenceEqual(["HM", "HL", "HR", "E"]), "Released over the button: hilite again.");

        tree.MouseMove(150, 215);
        Check(ImagesOf(tree).SequenceEqual(["M", "L", "R", "H"]), $"Hovered single image: {string.Join(",", ImagesOf(tree))}");
        tree.MouseDown(150, 215, UiMouseButton.Left);
        Check(ImagesOf(tree).SequenceEqual(["M", "L", "R", "P"]), $"Pressed single image: {string.Join(",", ImagesOf(tree))}");
        tree.MouseUp(150, 215, UiMouseButton.Left);

        tree.MouseMove(150, 315);
        Check(Fills().Contains(new WndColor(5, 5, 5, 255)), "Hovered colour button takes hilite entry 0.");
        tree.MouseDown(150, 315, UiMouseButton.Left);
        Check(Fills().Contains(new WndColor(7, 7, 7, 255)), "Pressed colour button takes hilite entry 1.");
        tree.MouseUp(150, 315, UiMouseButton.Left);
        tree.MouseMove(700, 500);
        Check(ImagesOf(tree).SequenceEqual(["M", "L", "R", "E"]) && Fills().Contains(new WndColor(1, 1, 1, 255)), "Everything returns to the enabled look once the pointer has left.");

        // No ENABLED status bit: the DISABLED* set, also under the pointer.
        var disabled = Tree(StateLayout("NOFOCUS"), services: services);
        disabled.MouseMove(150, 115);
        Check(ImagesOf(disabled).SequenceEqual(["DM", "DL", "DR", "D"]), $"Disabled images: {string.Join(",", ImagesOf(disabled))}");
        Check(disabled.BuildDrawList().Commands.OfType<UiTextDraw>().Single().Color == new WndColor(128, 128, 128, 255), "Disabled text colour.");
    }

    /// <summary>
    /// The hilite is the pointer's: it ends when the pointer leaves, also after a click (a click must not leave the button
    /// focused and hilited), when the button is hidden while hovered, and when another screen takes the pointer.
    /// </summary>
    private static void HiliteFollowsThePointerOnly()
    {
        var tree = Tree(StateLayout("ENABLED"));
        tree.Register("SSystem", (_, _) => { });
        var button = Find(tree, "S.wnd:Color");
        var other = Find(tree, "S.wnd:Other");
        Click(tree, button);
        Check(button.IsHilited && !button.IsSelected, "Still under the pointer after the click.");
        tree.MouseMove(450, 115);
        Check(!button.IsHilited && other.IsHilited, "A clicked button loses its hilite when the pointer moves to another button.");
        tree.MouseMove(700, 500);
        Check(!other.IsHilited, "Leaving clears the hilite.");

        // Keyboard focus hilites; the pointer leaving the focused button still clears it (GWM_MOUSE_LEAVING is unconditional).
        tree.SetFocus(button);
        Check(button.IsHilited, "Focus hilites.");
        tree.MouseMove(150, 315);
        tree.MouseMove(700, 500);
        Check(!button.IsHilited, "Leaving clears the hilite of the focused button too.");
        tree.SetFocus(null);

        // Hidden while hovered and pressed (a sub-menu switch), shown again later: must come back in the enabled look.
        tree.MouseMove(150, 315);
        tree.MouseDown(150, 315, UiMouseButton.Left);
        Check(button.IsHilited && button.IsSelected, "Hovered and pressed.");
        button.SetHidden(true);
        button.SetHidden(false);
        Check(!button.IsHilited && !button.IsSelected && tree.Hovered is null && tree.Grabbed is null, "Hiding a button drops its hilite and pressed state.");
        tree.MouseUp(150, 315, UiMouseButton.Left);

        // Another screen covers the tree.
        tree.MouseMove(150, 315);
        tree.MouseDown(150, 315, UiMouseButton.Left);
        tree.Dispatch();
        tree.PointerLeft();
        Check(!button.IsHilited && !button.IsSelected && tree.Hovered is null && tree.Grabbed is null, "PointerLeft drops hilite, pressed state and grab.");
        Check(Handled(tree).Select(e => e.Kind).SequenceEqual([UiEventKind.ButtonMouseLeaving]), "PointerLeft sends the leaving message.");
    }

    /// <summary>
    /// Static texts ship with <c>MOUSETRACK</c> and a bright <c>HILITEDRAWDATA</c> fill, but <c>GadgetStaticTextInput</c>
    /// ignores <c>GWM_MOUSE_ENTERING</c>: hovering one changes nothing and sends nothing. The same holds for progress bars.
    /// </summary>
    private static void StaticTextHasNoHiliteState()
    {
        var text = WndFile(Window(new Spec("USER", "T.wnd:Root", 0, 0, 800, 600)
        {
            System = "TSystem",
            Children =
            [
                new Spec("STATICTEXT", "T.wnd:Label", 100, 100, 300, 130) { Text = "GUI:Label" },
                new Spec("PROGRESSBAR", "T.wnd:Bar", 100, 200, 300, 220),
            ],
        }));
        var tree = Tree(text);
        var received = new List<UiEvent>();
        tree.Register("TSystem", (_, uiEvent) => received.Add(uiEvent));
        Check((Find(tree, "T.wnd:Label").Definition.Style & WndStyle.MouseTrack) != 0, "The fixture label carries MOUSETRACK like the shipped ones.");
        var before = tree.BuildDrawList().Dump();
        Check(before.Contains("fill 101,101 198x28 10,20,30,255", StringComparison.Ordinal), $"Label draws its enabled fill:\n{before}");
        foreach (var (x, y) in new[] { (150, 115), (150, 210) })
        {
            Check(tree.MouseMove(x, y), "The pointer is over the window.");
            tree.Dispatch();
            Check(tree.BuildDrawList().Dump() == before, $"Hovering at {x},{y} must not change the picture:\n{tree.BuildDrawList().Dump()}");
        }
        Check(!Find(tree, "T.wnd:Label").IsHilited && !Find(tree, "T.wnd:Bar").IsHilited && received.Count == 0, $"No hilite, no messages: {string.Join(", ", received)}");
    }

    /// <summary>
    /// <c>SaveLoad.wnd</c> keeps its <c>INPUTCALLBACK</c> on a child of the root and the layout's init focuses that child: keys must reach it with no gadget focused,
    /// as long as it is visible; a hidden home or none at all leaves keys to start at the topmost root as before.
    /// </summary>
    private static void KeyboardHomeReceivesKeysWithoutFocus()
    {
        var text = WndFile(Window(new Spec("USER", "K.wnd:Root", 0, 0, 800, 600)
        {
            Children = [new Spec("USER", "K.wnd:Menu", 100, 100, 700, 500) { Input = "MenuInput", Children = [new Spec("PUSHBUTTON", "K.wnd:Back", 200, 400, 300, 440)] }],
        }));
        var tree = Tree(text);
        var keys = new List<UiKey>();
        tree.Register("MenuInput", (_, e) =>
        {
            if (e.Kind == UiEventKind.Key) keys.Add((UiKey)e.Value);
        });
        Check(!tree.KeyDown(UiKey.Escape) && keys.Count == 0, "Without focus and without a home the root has no input callback: nobody gets the key.");
        tree.Dispatch();
        Check(keys.Count == 0, "Still nothing delivered.");

        tree.KeyboardHome = Find(tree, "K.wnd:Menu");
        Check(tree.KeyDown(UiKey.Escape), "With the child as keyboard home the key is received.");
        tree.Dispatch();
        Check(keys.SequenceEqual([UiKey.Escape]), "The child's input callback got Escape without any gadget focus.");

        Find(tree, "K.wnd:Menu").SetHidden(true);
        Check(!tree.KeyDown(UiKey.Escape), "A hidden home receives nothing.");
        tree.Dispatch();
        Check(keys.Count == 1, "No second delivery while the home is hidden.");
        Find(tree, "K.wnd:Menu").SetHidden(false);
        tree.KeyDown(UiKey.Escape);
        tree.Dispatch();
        Check(keys.Count == 2, "The home works again once it is visible (hiding it did not drop it).");
    }

    /// <summary>
    /// <c>winSetEnabledImage</c>/<c>winSetHiliteImage</c>/<c>winSetDisabledImage</c> and <c>winSetSize</c>: a replaced image is what the button draws in that state,
    /// the file's own stays for the others, and a new design size keeps the corner and follows the viewport.
    /// </summary>
    private static void DrawImagesAndDesignSizeCanBeReplaced()
    {
        var images = new FakeImages(new() { ["E"] = (10, 30), ["D"] = (10, 30), ["H"] = (10, 30), ["P"] = (10, 30), ["X"] = (10, 30), ["Y"] = (10, 30), ["Z"] = (10, 30) });
        var services = new UiTreeServices(new MonoMeasure()) { Images = images };
        var tree = Tree(StateLayout("ENABLED"), services: services);
        var one = Find(tree, "S.wnd:One");
        Check(one.DrawImage(WndDrawDataKind.Enabled, 0) == "E" && one.EnabledImage0 == "E", "The file's own image until replaced.");
        one.SetDrawImage(WndDrawDataKind.Enabled, 0, "X");
        Check(ImagesOf(tree).Contains("X") && !ImagesOf(tree).Contains("E"), $"The replaced enabled image is drawn: {string.Join(",", ImagesOf(tree))}");
        one.SetDrawImage(WndDrawDataKind.Hilite, 0, "Y");
        one.SetDrawImage(WndDrawDataKind.Hilite, 1, "Z");
        tree.MouseMove(150, 215);
        Check(ImagesOf(tree).Contains("Y") && !ImagesOf(tree).Contains("H"), $"The replaced hilite image is drawn under the pointer: {string.Join(",", ImagesOf(tree))}");
        tree.MouseDown(150, 215, UiMouseButton.Left);
        Check(ImagesOf(tree).Contains("Z") && !ImagesOf(tree).Contains("P"), $"The replaced hilite image 1 is drawn while pressed: {string.Join(",", ImagesOf(tree))}");
        tree.MouseUp(150, 215, UiMouseButton.Left);
        tree.MouseMove(700, 500);
        Check(one.EnabledImage0 == "X", "SetEnabledImage0 and SetDrawImage(Enabled, 0) are the same entry.");

        var rect = one.Rect;
        one.SetDesignSize(40, 40);
        Check(one.Rect.X == rect.X && one.Rect.Y == rect.Y && one.Rect.Width == 40 && one.Rect.Height == 40, $"Design size keeps the corner: {rect} -> {one.Rect}");
        tree.SetViewport(new UiViewport(1600, 1200), UiScaleOptions.Original);
        Check(one.Rect.Width == 80 && one.Rect.Height == 80, $"and scales with the viewport: {one.Rect}");
        Check(Find(tree, "S.wnd:Three").Rect.Width == 200, "Other windows keep their design rectangle.");
    }

    /// <summary>
    /// The image look of a horizontal slider (<c>W3DGadgetHorizontalSliderImageDraw</c>): a centred row of boxes, filled up
    /// to the value (disabled image 0) and blank after it (disabled image 1), the frame image (hilite 0) behind the row
    /// and the thumb arrow only while hilited. Window 213 wide, box 11 wide plus 2 gap: 16 boxes, 5 pixels left over.
    /// </summary>
    private static void HorizontalSliderDrawsBoxesAndThumb()
    {
        var text = WndFile(Window(new Spec("USER", "H.wnd:Root", 0, 0, 800, 600)
        {
            System = "HSystem",
            DrawData = string.Empty,
            Children =
            [
                new Spec("HORZSLIDER", "H.wnd:Slider", 100, 100, 313, 126)
                {
                    Status = "ENABLED+IMAGE",
                    DrawData = Draw("ENABLEDDRAWDATA", Entry("NoImage", "255 0 0 255", "255 128 128 255"))
                        + Draw("DISABLEDDRAWDATA", Entry("Full", "0 0 0 0", "0 0 0 0"), Entry("Empty", "0 0 0 0", "0 0 0 0"))
                        + Draw("HILITEDRAWDATA", Entry("Frame", "0 0 0 0", "0 0 0 0"), Entry("Arrow", "0 0 0 0", "0 0 0 0")),
                    Extra = "  SLIDERDATA = MINVALUE: 0,\n    MAXVALUE: 100;\n"
                        + Draw("SLIDERTHUMBENABLEDDRAWDATA", Entry("NoImage", "1 1 1 255", "2 2 2 255"))
                        + Draw("SLIDERTHUMBDISABLEDDRAWDATA", Entry("NoImage", "1 1 1 255", "2 2 2 255"))
                        + Draw("SLIDERTHUMBHILITEDRAWDATA", Entry("Arrow", "0 0 0 0", "0 0 0 0"), Entry("ArrowDown", "0 0 0 0", "0 0 0 0")),
                },
            ],
        }));
        var images = new FakeImages(new() { ["Full"] = (11, 13), ["Empty"] = (11, 13), ["Frame"] = (13, 15), ["Arrow"] = (13, 16) });
        var tree = Tree(text, services: new UiTreeServices(new MonoMeasure()) { Images = images });
        tree.Register("HSystem", (_, _) => { });
        var slider = Find(tree, "H.wnd:Slider");
        UiImageDraw[] Draws() => tree.BuildDrawList().Commands.OfType<UiImageDraw>().ToArray();
        int Count(string image) => Draws().Count(draw => draw.Image == image);

        Check(Count("Full") == 0 && Count("Empty") == 16 && Draws().Length == 16, $"At the minimum every box is blank: {tree.BuildDrawList().Dump()}");
        Check(Draws()[0].Destination == new UiRect(102, 100, 11, 11) && Draws()[15].Destination == new UiRect(297, 100, 11, 11), $"Row is centred: {Draws()[0].Destination} .. {Draws()[15].Destination}");
        Check(!tree.BuildDrawList().Commands.Any(command => command is UiFillRect or UiBorderRect), "The image look draws no colour bar and no coloured thumb.");

        slider.SetSliderValue(50);
        Check(Count("Full") == 9 && Count("Empty") == 7, $"Value 50 fills the boxes that start up to the middle: {Count("Full")}/{Count("Empty")}");
        slider.SetSliderValue(100);
        Check(Count("Full") == 16 && Count("Empty") == 0, "The maximum fills every box.");

        // Hovering: the frame row behind the boxes (one more than boxes) and the thumb arrow below the value.
        slider.SetSliderValue(50);
        tree.MouseMove(110, 104);
        Check(slider.IsHilited && Count("Frame") == 17 && Count("Arrow") == 1, $"Hovered slider: {Count("Frame")} frames, {Count("Arrow")} arrows");
        Check(Draws()[0] is { Image: "Frame", Destination: { X: 96, Y: 103, Width: 13, Height: 13 } }, $"Frames come first, half a step left and a third down: {Draws()[0]}");
        Check(Draws()[^1] is { Image: "Arrow" } && Draws()[^1].Destination == new UiRect(200, 110, 13, 16) && Draws()[^1].Destination == UiGadgetGeometry.SliderThumb(slider),
            $"Thumb last, 13x16, ten below the top: {Draws()[^1]}");

        // Dragging: the value follows the pointer, the thumb shows its pressed image, the boxes follow.
        tree.MouseDown(306, 104, UiMouseButton.Left);
        Check(slider.Slider!.Value == 100 && Count("Full") == 16 && Count("ArrowDown") == 1 && Count("Arrow") == 0, $"Pressed at the right end: value {slider.Slider.Value}");
        tree.MouseMove(156, 300);
        Check(slider.Slider.Value == 25 && Count("Full") == 5, $"Dragged (pointer outside the window): value {slider.Slider.Value}, {Count("Full")} filled");
        tree.MouseUp(156, 300, UiMouseButton.Left);
        var kinds = Handled(tree).Where(e => e.Source == "H.wnd:Slider" && e.Kind is UiEventKind.SliderTrack or UiEventKind.SliderDone).Select(e => (e.Kind, e.Value)).ToArray();
        Check(kinds.SequenceEqual([(UiEventKind.SliderTrack, 100), (UiEventKind.SliderTrack, 25), (UiEventKind.SliderDone, 25)]), $"Slider messages: {string.Join(", ", kinds)}");
        Check(!slider.IsHilited && Count("Frame") == 0 && Count("Arrow") == 0 && Count("ArrowDown") == 0, "Released outside: no hilite, no thumb.");

        var unknown = Tree(text);
        Check(unknown.BuildDrawList().Statistics.UnresolvedImageSizes == 1, "An unknown box size is counted.");
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Real data

    private static void RealData(string contentDirectory)
    {
        var content = ContentFileSystem.Open(contentDirectory);
        var files = content.EnumerateFiles("Window/").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var wnd = files.Count(path => path.EndsWith(".wnd", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"Real data: {files.Length} files under Window/ ({wnd} *.wnd, {files.Length - wnd} variants: {string.Join(", ", files.Where(path => !path.EndsWith(".wnd", StringComparison.OrdinalIgnoreCase)))})");
        Check(files.Length == 83, $"Expected 83 files under Window/, found {files.Length}.");

        HeaderTemplates(content, out var templates);
        var parse = Stopwatch.StartNew();
        var layouts = files.Select(path =>
        {
            using var stream = content.OpenRead(path);
            return WndParser.Parse(stream, path);
        }).ToArray();
        parse.Stop();

        var diagnostics = layouts.SelectMany(layout => layout.Diagnostics).ToArray();
        var unexplained = diagnostics.Where(diagnostic => !diagnostic.IsExplained).ToArray();
        foreach (var diagnostic in unexplained) Console.Error.WriteLine(diagnostic);
        Check(unexplained.Length == 0, $"{unexplained.Length} unexplained WND diagnostics.");
        var windows = layouts.SelectMany(layout => layout.AllWindows()).ToArray();
        Console.WriteLine($"  parsed {layouts.Length} files, {windows.Length} windows in {parse.ElapsedMilliseconds} ms; diagnostics {diagnostics.Length} (all explained): "
                          + string.Join(", ", diagnostics.GroupBy(diagnostic => diagnostic.Code).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}")));
        Console.WriteLine("  window types: " + string.Join(", ", windows.GroupBy(window => window.TypeName).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}")));
        Console.WriteLine("  keys: " + string.Join(", ", layouts.SelectMany(layout => layout.KeyCounts)
            .GroupBy(pair => pair.Key).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Sum(pair => pair.Value)}")));
        Console.WriteLine("  creation resolutions: " + string.Join(", ", windows.GroupBy(window => $"{window.Rect.CreationWidth}x{window.Rect.CreationHeight}")
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}")));
        Check(windows.All(window => window.Name is not null), "Every shipped window has a name.");
        Check(windows.All(window => window.Name!.Contains(".wnd:", StringComparison.OrdinalIgnoreCase)), "Names are decorated as File.wnd:Name.");

        var services = new UiTreeServices(new MonoMeasure()) { HeaderTemplates = templates, Images = new UniformImages() };
        var overlapChanges = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var build = new Stopwatch();
        var trees = 0;
        var outsideAuthored = 0;
        var outsideScaled = 0;
        foreach (var mode in Enum.GetValues<UiScaleMode>())
            foreach (var viewport in Resolutions)
                foreach (var layout in layouts)
                {
                    build.Start();
                    var tree = new UiWindowTree(viewport, new UiScaleOptions { Mode = mode }, services);
                    tree.AddLayout(layout);
                    build.Stop();
                    trees++;
                    foreach (var window in tree.AllWindows)
                    {
                        if (!window.Definition.Rect.IsInsideCreationArea) { outsideAuthored++; continue; }
                        if (!window.Rect.IsInside(viewport.Bounds)) outsideScaled++;
                    }
                    CountOverlapChanges(tree, mode, viewport, overlapChanges);
                }
        Console.WriteLine("  overlap relation changes vs. creation resolution (pairs of visible windows with equal creation resolution; '*' = shrinking): "
                          + (overlapChanges.Count == 0 ? "none" : string.Join(", ", overlapChanges.Select(pair => $"{pair.Key} {pair.Value}"))));
        foreach (var (key, count) in overlapChanges)
            Check(key.EndsWith('*') || key.StartsWith("Anchor lost", StringComparison.Ordinal), $"Overlap relation changed without shrinking: {key} {count}");
        Console.WriteLine($"  built {trees} trees ({layouts.Length} files x {Resolutions.Length} resolutions x 3 modes) in {build.ElapsedMilliseconds} ms; "
                          + $"windows authored outside their creation area: {outsideAuthored / (Resolutions.Length * 3)} per pass");
        Check(outsideScaled == 0, $"{outsideScaled} windows authored inside left the viewport.");

        var statistics = new UiWindowTree(new UiViewport(1920, 1080), UiScaleOptions.Original, services);
        foreach (var layout in layouts) statistics.AddLayout(layout);
        var stats = statistics.Statistics;
        Console.WriteLine($"  all layouts in one tree: {stats.Windows} windows, duplicate names {stats.DuplicateNames}, windows without font {stats.WindowsWithoutFont}, unknown header templates: "
                          + (stats.UnknownHeaderTemplates.IsEmpty ? "none" : string.Join(", ", stats.UnknownHeaderTemplates.Select(pair => $"{pair.Key} {pair.Value}"))));

        foreach (var viewport in new[] { new UiViewport(1920, 1080), new UiViewport(1280, 720) })
            foreach (var mode in Enum.GetValues<UiScaleMode>())
            {
                var draw = Stopwatch.StartNew();
                var commands = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var custom = new SortedDictionary<string, int>(StringComparer.Ordinal);
                int labels = 0, total = 0, borders = 0;
                foreach (var layout in layouts)
                {
                    var tree = new UiWindowTree(viewport, new UiScaleOptions { Mode = mode }, services);
                    tree.AddLayout(layout);
                    var list = tree.BuildDrawList();
                    total += list.Commands.Length;
                    labels += list.Statistics.UnresolvedLabels;
                    borders += list.Statistics.BorderStatusWindows;
                    foreach (var (kind, count) in list.CountByKind()) commands[kind] = commands.GetValueOrDefault(kind) + count;
                    foreach (var (name, count) in list.Statistics.CustomDraws) custom[name] = custom.GetValueOrDefault(name) + count;
                    Check(list.Dump() == tree.BuildDrawList().Dump(), $"{layout.FileName}: draw list not reproducible.");
                }
                draw.Stop();
                Console.WriteLine($"  draw {viewport} {mode}: {total} commands in {draw.ElapsedMilliseconds} ms ({string.Join(", ", commands.Select(pair => $"{pair.Key} {pair.Value}"))}); "
                                  + $"unresolved labels {labels} (no text source); custom draw callbacks {custom.Values.Sum()}; drawn windows with BORDER status (not drawn) {borders}");
                if (viewport.Width == 1920 && mode == UiScaleMode.Stretch)
                    Console.WriteLine("  custom draw callbacks (host must implement): " + string.Join(", ", custom.Select(pair => $"{pair.Key} {pair.Value}")));
            }

        RealDataRouting(layouts, services);
        RealDataProbes(layouts, services);
    }

    /// <summary>Counts window pairs whose overlap relation differs from the creation resolution ("gained"/"lost" per mode).</summary>
    private static void CountOverlapChanges(UiWindowTree tree, UiScaleMode mode, UiViewport viewport, SortedDictionary<string, int> changes)
    {
        var windows = tree.AllWindows.Where(window => window.IsVisible && !window.Rect.IsEmpty).ToArray();
        for (var i = 0; i < windows.Length; i++)
        {
            var a = windows[i].Definition.Rect;
            for (var j = i + 1; j < windows.Length; j++)
            {
                var b = windows[j].Definition.Rect;
                if (a.CreationWidth != b.CreationWidth || a.CreationHeight != b.CreationHeight) continue;
                var before = Overlaps(a, b);
                var after = windows[i].Rect.Intersects(windows[j].Rect);
                if (before == after) continue;
                var shrinks = viewport.Width < a.CreationWidth || viewport.Height < a.CreationHeight;
                var key = $"{mode} {(after ? "gained" : "lost")} {viewport}{(shrinks ? "*" : "")}";
                changes[key] = changes.GetValueOrDefault(key) + 1;
            }
        }
    }

    private static void HeaderTemplates(ContentFileSystem content, out WndHeaderTemplateTable templates)
    {
        using var reader = new StreamReader(content.OpenRead("Data/English/HeaderTemplate.ini"));
        templates = WndHeaderTemplateTable.Parse(reader.ReadToEnd(), "Data/English/HeaderTemplate.ini");
        Check(templates.Diagnostics.IsEmpty, $"HeaderTemplate.ini diagnostics: {string.Join("; ", templates.Diagnostics)}");
        Console.WriteLine($"  header templates: {templates.Templates.Count}");
    }

    /// <summary>Where a GBM_SELECTED of every shipped push button ends up: the callback names the shell has to register.</summary>
    private static void RealDataRouting(WndLayout[] layouts, UiTreeServices services)
    {
        var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var layout in layouts)
        {
            var tree = new UiWindowTree(new UiViewport(800, 600), UiScaleOptions.Original, services);
            tree.AddLayout(layout);
            foreach (var button in tree.AllWindows.Where(window => window.Type == WndWindowType.PushButton))
            {
                tree.Post(UiEventKind.ButtonSelected, button);
                var delivery = tree.Dispatch().Single();
                var key = delivery.Outcome == UiDeliveryOutcome.Unregistered ? $"callback {delivery.Callback}" : delivery.Outcome.ToString();
                outcomes[key] = outcomes.GetValueOrDefault(key) + 1;
            }
        }
        Console.WriteLine($"  GBM_SELECTED routing of {outcomes.Values.Sum()} push buttons: {string.Join(", ", outcomes.Select(pair => $"{pair.Key} {pair.Value}"))}");
        Check(outcomes.GetValueOrDefault("callback MainMenuSystem") > 0 && outcomes.GetValueOrDefault("callback SkirmishGameOptionsMenuSystem") > 0,
            "Menu buttons must reach their menu system callbacks.");
    }

    /// <summary>
    /// Corner/centre probes of every visible enabled control against the creation resolution. Mismatches are reported per
    /// resolution; a resolution marked with '*' shrinks at least one axis of the file's creation resolution, where
    /// one-pixel features can merge. Stretch and Uniform must match exactly wherever no axis shrinks.
    /// </summary>
    private static void RealDataProbes(WndLayout[] layouts, UiTreeServices services)
    {
        var summary = new List<string>();
        var mixedFiles = layouts.Where(layout => layout.AllWindows().Select(window => (window.Rect.CreationWidth, window.Rect.CreationHeight)).Distinct().Count() > 1).ToHashSet();
        Console.WriteLine($"  files mixing creation resolutions (probe keys marked 'm'): {string.Join(", ", mixedFiles.Select(layout => layout.FileName))}");
        foreach (var mode in Enum.GetValues<UiScaleMode>())
        {
            var mismatches = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var probes = 0;
            foreach (var layout in layouts)
            {
                var creation = layout.Windows.FirstOrDefault()?.Rect ?? new WndScreenRect(0, 0, 0, 0, 800, 600);
                var reference = new UiWindowTree(new UiViewport(creation.CreationWidth, creation.CreationHeight), UiScaleOptions.Original, services);
                reference.AddLayout(layout);
                var expected = reference.AllWindows.Where(IsControl).Where(window => window.Rect is { Width: > 1, Height: > 1 })
                    .Select(window => (window.Order, Hits: Probes(window).Select(point => reference.HitTest(point.X, point.Y)?.Order ?? -1).ToArray()))
                    .ToArray();
                var tree = new UiWindowTree(new UiViewport(800, 600), new UiScaleOptions { Mode = mode }, services);
                tree.AddLayout(layout);
                var byOrder = tree.AllWindows.ToDictionary(window => window.Order);
                foreach (var viewport in Resolutions)
                {
                    tree.SetViewport(viewport);
                    var shrinks = viewport.Width < creation.CreationWidth || viewport.Height < creation.CreationHeight;
                    var key = $"{viewport}{(shrinks ? "*" : "")}{(mixedFiles.Contains(layout) ? "m" : "")}";
                    foreach (var (order, hits) in expected)
                    {
                        var points = Probes(byOrder[order]);
                        for (var p = 0; p < points.Length; p++)
                        {
                            probes++;
                            if ((tree.HitTest(points[p].X, points[p].Y)?.Order ?? -1) != hits[p])
                                mismatches[key] = mismatches.GetValueOrDefault(key) + 1;
                        }
                    }
                }
            }
            summary.Add($"{mode}: {probes} probes, mismatches {(mismatches.Count == 0 ? "0" : string.Join(" ", mismatches.Select(pair => $"{pair.Key}={pair.Value}")))}");
            if (mode != UiScaleMode.Anchor)
                foreach (var (viewport, count) in mismatches)
                    Check(viewport.EndsWith('*') || viewport.EndsWith('m'), $"{mode} probe mismatches at {viewport} ({count}) where no axis shrinks.");
        }
        Console.WriteLine("  hit probes (corners + centre of every visible enabled control vs. creation resolution): " + string.Join("; ", summary));
    }
}
