using System.Text;
using CnCNew.Ui.Runtime;

namespace CnCNew.Ui.Tests;

/// <summary>Synthetic WND text builders and fake renderer services for the WND tests (no original data).</summary>
internal static class WndFixtures
{
    public const string Undefined = "IMAGE: NoImage, COLOR: 255 255 255 0, BORDERCOLOR: 255 255 255 0";

    public static string WndFile(string windows, string init = "[None]")
        => $"FILE_VERSION = 2;\nSTARTLAYOUTBLOCK\n  LAYOUTINIT = {init};\n  LAYOUTUPDATE = [None];\n  LAYOUTSHUTDOWN = [None];\nENDLAYOUTBLOCK\n{windows}";

    /// <summary>A draw data key with the given entries padded to nine undefined entries.</summary>
    public static string Draw(string key, params string[] entries)
    {
        var all = entries.Concat(Enumerable.Repeat(Undefined, 9 - entries.Length));
        return $"  {key} = {string.Join(",\n      ", all)};\n";
    }

    public static string Entry(string image, string color, string border) => $"IMAGE: {image}, COLOR: {color}, BORDERCOLOR: {border}";

    public sealed record Spec(string Type, string Name, int Left, int Top, int Right, int Bottom)
    {
        public string Status { get; init; } = "ENABLED";
        public string? Style { get; init; }
        public string System { get; init; } = "[None]";
        public string Input { get; init; } = "[None]";
        public string Tooltip { get; init; } = "[None]";
        public string DrawCallback { get; init; } = "[None]";
        public string HeaderTemplate { get; init; } = "[None]";
        public int TooltipDelay { get; init; } = -1;
        public string? TooltipText { get; init; }
        public string? Text { get; init; }
        public string? DrawData { get; init; }
        public string Extra { get; init; } = string.Empty;
        public int CreationWidth { get; init; } = 800;
        public int CreationHeight { get; init; } = 600;
        public Spec[] Children { get; init; } = [];
    }

    public static string Window(Spec spec)
    {
        var builder = new StringBuilder();
        builder.Append("WINDOW\n");
        builder.Append($"  WINDOWTYPE = {spec.Type};\n");
        builder.Append($"  SCREENRECT = UPPERLEFT: {spec.Left} {spec.Top},\n               BOTTOMRIGHT: {spec.Right} {spec.Bottom},\n");
        builder.Append($"               CREATIONRESOLUTION: {spec.CreationWidth} {spec.CreationHeight};\n");
        builder.Append($"  NAME = \"{spec.Name}\";\n");
        builder.Append($"  STATUS = {spec.Status};\n");
        builder.Append($"  STYLE = {spec.Style ?? (spec.Type == "USER" ? "USER" : spec.Type + "+MOUSETRACK")};\n");
        builder.Append($"  SYSTEMCALLBACK = \"{spec.System}\";\n  INPUTCALLBACK = \"{spec.Input}\";\n");
        builder.Append($"  TOOLTIPCALLBACK = \"{spec.Tooltip}\";\n  DRAWCALLBACK = \"{spec.DrawCallback}\";\n");
        builder.Append("  FONT = NAME: \"Arial\", SIZE: 10, BOLD: 0;\n");
        builder.Append($"  HEADERTEMPLATE = \"{spec.HeaderTemplate}\";\n");
        builder.Append($"  TOOLTIPDELAY = {spec.TooltipDelay};\n");
        if (spec.TooltipText is not null) builder.Append($"  TOOLTIPTEXT = \"{spec.TooltipText}\";\n");
        if (spec.Text is not null) builder.Append($"  TEXT = \"{spec.Text}\";\n");
        builder.Append("  TEXTCOLOR = ENABLED: 255 255 255 255, ENABLEDBORDER: 0 0 0 255,\n");
        builder.Append("              DISABLED: 128 128 128 255, DISABLEDBORDER: 0 0 0 255,\n");
        builder.Append("              HILITE: 255 255 0 255, HILITEBORDER: 0 0 0 255;\n");
        builder.Append(spec.DrawData ?? Draw("ENABLEDDRAWDATA", Entry("NoImage", "10 20 30 255", "40 50 60 255"))
            + Draw("DISABLEDDRAWDATA", Entry("NoImage", "64 64 64 255", "32 32 32 255"))
            + Draw("HILITEDRAWDATA", Entry("NoImage", "70 80 90 255", "40 50 60 255")));
        builder.Append(spec.Extra);
        builder.Append(GadgetData(spec));
        if (spec.Children.Length > 0)
        {
            foreach (var child in spec.Children) builder.Append("  CHILD\n").Append(Window(child));
            builder.Append("  ENDALLCHILDREN\n");
        }
        builder.Append("END\n");
        return builder.ToString();
    }

    private static string GadgetData(Spec spec)
    {
        bool Has(string key) => spec.Extra.Contains(key, StringComparison.Ordinal);
        return spec.Type switch
        {
            "STATICTEXT" when !Has("STATICTEXTDATA") => "  STATICTEXTDATA = CENTERED: 0;\n",
            "RADIOBUTTON" when !Has("RADIOBUTTONDATA") => "  RADIOBUTTONDATA = GROUP: 1;\n",
            "HORZSLIDER" or "VERTSLIDER" when !Has("SLIDERDATA") => "  SLIDERDATA = MINVALUE: 0,\n    MAXVALUE: 100;\n",
            "SCROLLLISTBOX" when !Has("LISTBOXDATA") =>
                "  LISTBOXDATA = LENGTH: 10,\n    AUTOSCROLL: 0,\n    SCROLLIFATEND: 0,\n    AUTOPURGE: 1,\n    SCROLLBAR: 1,\n    MULTISELECT: 0,\n    COLUMNS: 1,\n    FORCESELECT: 0;\n",
            "ENTRYFIELD" when !Has("TEXTENTRYDATA") =>
                "  TEXTENTRYDATA = MAXLEN: 16,\n    SECRETTEXT: 0,\n    NUMERICALONLY: 0,\n    ALPHANUMERICALONLY: 0,\n    ASCIIONLY: 0;\n",
            "COMBOBOX" when !Has("COMBOBOXDATA") =>
                "  COMBOBOXDATA = ISEDITABLE: 0,\n    MAXCHARS: 16,\n    MAXDISPLAY: 3,\n    ASCIIONLY: 0,\n    LETTERSANDNUMBERS: 0;\n",
            "TABCONTROL" when !Has("TABCONTROLDATA") =>
                "  TABCONTROLDATA = TABORIENTATION: 0,\n    TABEDGE: 0,\n    TABWIDTH: 50,\n    TABHEIGHT: 20,\n    TABCOUNT: 2,\n    PANEBORDER: 0,\n    PANEDISABLED: 2, 0, 0;\n",
            _ => string.Empty,
        };
    }

    /// <summary>Monospace fake: every character is half the point size wide, lines are size + 2 high.</summary>
    public sealed class MonoMeasure : IUiTextMeasure
    {
        public int MeasureWidth(string text, UiFont font) => text.Length * Math.Max(1, font.Size / 2);
        public int LineHeight(UiFont font) => font.Size + 2;
    }

    public sealed class FakeImages(Dictionary<string, (int Width, int Height)> sizes) : IUiImageLookup
    {
        public bool TryGetSize(string imageName, out int width, out int height)
        {
            if (sizes.TryGetValue(imageName, out var size))
            {
                (width, height) = size;
                return true;
            }
            width = height = 0;
            return false;
        }
    }

    /// <summary>Every image is 16x16; used for the real-data runs where the mapped image table is not part of this package.</summary>
    public sealed class UniformImages : IUiImageLookup
    {
        public bool TryGetSize(string imageName, out int width, out int height)
        {
            width = height = 16;
            return true;
        }
    }

    public sealed class FakeText(Dictionary<string, string> strings) : IUiTextSource
    {
        public bool TryResolve(string label, out string text) => strings.TryGetValue(label, out text!);
    }
}
