using CnCNew.Game.Data;
using CnCNew.Ui.Images;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.Game.Hud;

/// <summary>
/// Everything <see cref="MatchController"/> loads once from <see cref="GameData"/> to build the in-match HUD: the two retail
/// layouts it binds (<c>ControlBar.wnd</c>, the in-game <c>QuitMenu.wnd</c>), the header template table of the chosen language,
/// the control bar background scheme per faction side, and the text/image adapters for <see cref="CnCNew.Ui.Runtime.UiWindowTree"/>.
/// Building it opens content files but performs no simulation or rendering work.
/// </summary>
public sealed class HudAssets
{
    public const string ControlBarPath = "Window/ControlBar.wnd";
    public const string QuitMenuPath = "Window/Menus/QuitMenu.wnd";
    public const string SchemePath = "Data/INI/ControlBarScheme.ini";

    private HudAssets(WndLayout controlBar, WndLayout quitMenu, WndHeaderTemplateTable headerTemplates, ControlBarSchemeTable schemes,
        LocalizedText text, MappedImageTable images)
    {
        ControlBar = controlBar;
        QuitMenu = quitMenu;
        HeaderTemplates = headerTemplates;
        Schemes = schemes;
        Text = text;
        Images = images;
    }

    public WndLayout ControlBar { get; }
    public WndLayout QuitMenu { get; }
    public WndHeaderTemplateTable HeaderTemplates { get; }
    public ControlBarSchemeTable Schemes { get; }
    public LocalizedText Text { get; }
    public MappedImageTable Images { get; }

    public static HudAssets Load(GameData data, string language)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        WndLayout ReadLayout(string path, string fileName)
        {
            using var stream = data.Content.OpenRead(path);
            return WndParser.Parse(stream, fileName);
        }

        var controlBar = ReadLayout(ControlBarPath, "ControlBar.wnd");
        var quitMenu = ReadLayout(QuitMenuPath, "QuitMenu.wnd");

        var headerTemplatePath = $"Data/{language}/HeaderTemplate.ini";
        WndHeaderTemplateTable headerTemplates;
        if (data.Content.Exists(headerTemplatePath))
        {
            using var stream = data.Content.OpenRead(headerTemplatePath);
            using var reader = new StreamReader(stream);
            headerTemplates = WndHeaderTemplateTable.Parse(reader.ReadToEnd(), headerTemplatePath);
        }
        else headerTemplates = WndHeaderTemplateTable.Empty;

        ControlBarSchemeTable schemes;
        if (data.Content.Exists(SchemePath))
        {
            using var stream = data.Content.OpenRead(SchemePath);
            using var reader = new StreamReader(stream);
            schemes = ControlBarSchemeTable.Parse(reader.ReadToEnd());
        }
        else schemes = ControlBarSchemeTable.Parse(string.Empty);

        var text = data.LoadText(language);
        return new HudAssets(controlBar, quitMenu, headerTemplates, schemes, text, data.Images);
    }

    public UiTreeServices BuildTreeServices(IUiTextMeasure measure) => new(measure)
    {
        Images = new MappedImageLookup(Images),
        Text = new LocalizedTextSource(Text),
        HeaderTemplates = HeaderTemplates,
    };
}
