using CnCNew.Client.Stride.Ui;
using CnCNew.Content;
using CnCNew.Game.Data;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;
using CnCNew.UiRenderTool;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;

var contentDirectory = GetArgument(args, "--content-dir") ?? throw new ArgumentException("--content-dir is required.");
var outputDirectory = GetArgument(args, "--out-dir") ?? throw new ArgumentException("--out-dir is required.");
Directory.CreateDirectory(outputDirectory);

var fontAudit = args.Contains("--font-audit", StringComparer.OrdinalIgnoreCase);
var backend = (GetArgument(args, "--font-backend") ?? "gdi").ToLowerInvariant() switch
{
    "gdi" => UiFontBackend.Gdi,
    "freetype" => UiFontBackend.FreeType,
    var other => throw new ArgumentException($"--font-backend must be gdi or freetype, got '{other}'."),
};
using var game = new CaptureGame(contentDirectory, outputDirectory, fontAudit, backend);
game.Run();
return game.Failures == 0 ? 0 : 1;

static string? GetArgument(string[] arguments, string name)
{
    var index = Array.FindIndex(arguments, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (index < 0) return null;
    return index + 1 < arguments.Length ? arguments[index + 1] : throw new ArgumentException($"{name} requires a value.");
}

/// <summary>
/// Headless verification host for <c>CnCNew.Client.Stride.Ui.UiRenderer</c> (package S4). Renders a fixed set of real WND
/// menus and hand-built synthetic scenes at four screen sizes into offscreen render targets and saves each as a PNG; run
/// twice and diff the SHA-256 of every file to confirm the renderer is deterministic. Not part of any solution file and not
/// referenced by the client — a standalone check, like the other <c>tools/*</c> programs.
/// </summary>
internal sealed class CaptureGame(string contentDirectory, string outputDirectory, bool fontAudit, UiFontBackend backend) : Game
{
    private static readonly (int Width, int Height)[] Sizes = [(800, 600), (1280, 720), (1920, 1080), (2560, 1080)];

    private UiRenderer? _renderer;
    private GameData? _data;
    private WndHeaderTemplateTable _templates = WndHeaderTemplateTable.Empty;
    private LocalizedTextSource? _text;
    private bool _done;

    public int Failures { get; private set; }

    protected override void Initialize()
    {
        AutoLoadDefaultSettings = false;
        GraphicsDeviceManager.PreferredBackBufferWidth = 64;
        GraphicsDeviceManager.PreferredBackBufferHeight = 64;
        base.Initialize();
        Window.Title = "CnCNew UI Render Tool";
    }

    protected override async Task LoadContent()
    {
        await base.LoadContent();
        Console.WriteLine($"Loading content from '{contentDirectory}' ...");
        _data = GameData.Load(contentDirectory, step => Console.WriteLine($"  {step}"));
        _text = new LocalizedTextSource(_data.LoadText("English"));
        using (var reader = new StreamReader(_data.Content.OpenRead("Data/English/HeaderTemplate.ini")))
            _templates = WndHeaderTemplateTable.Parse(reader.ReadToEnd(), "Data/English/HeaderTemplate.ini");
        if (!_templates.Diagnostics.IsEmpty)
            throw new InvalidDataException($"HeaderTemplate.ini diagnostics: {string.Join("; ", _templates.Diagnostics)}");

        _renderer = new UiRenderer(GraphicsDevice, _data.Content, _data.Images, "English", backend);
        Console.WriteLine($"Mapped images: {_data.Images.Count}, texture pages: {_data.Images.ImagesPerTexture().Length}");
    }

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        if (_done) return;
        _done = true;
        try
        {
            if (fontAudit) FontAudit.Run(_data!, _text!.Text, _templates, outputDirectory);
            else RunCaptures();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAILED: {exception}");
            Failures++;
        }
        finally
        {
            Exit();
        }
    }

    private void RunCaptures()
    {
        var renderer = _renderer!;
        var services = new UiTreeServices(renderer.TextMeasure) { Images = renderer.Images, Text = _text, HeaderTemplates = _templates };
        var content = _data!.Content;

        var realMenus = new (string Name, string File)[]
        {
            ("mainmenu", "Window/Menus/MainMenu.wnd"),
            ("options", "Window/Menus/OptionsMenu.wnd"),
            ("skirmish", "Window/Menus/SkirmishGameOptionsMenu.wnd"),
            ("controlbar", "Window/ControlBar.wnd"),
        };

        const string plainImage = "NuHelpbox-middle";
        const string rotatedImage = "WatermarkGLA";
        if (!_data.Images.TryGet(plainImage, out _)) throw new InvalidDataException($"Expected mapped image '{plainImage}' is missing from the loaded data.");
        if (!_data.Images.TryGet(rotatedImage, out var rotated) || !rotated.IsRotated)
            throw new InvalidDataException($"Expected rotated mapped image '{rotatedImage}' is missing or not rotated in the loaded data.");

        var manifest = new List<string>();
        foreach (var (name, file) in realMenus)
        {
            var layout = WndParser.Parse(content.OpenRead(file), file);
            foreach (var (width, height) in Sizes)
            {
                var tree = new UiWindowTree(new UiViewport(width, height), UiScaleOptions.Original, services);
                tree.AddLayout(layout);
                var list = tree.BuildDrawList();
                Capture(renderer, list, width, height, $"{name}-{width}x{height}", manifest);
            }
        }

        // The layouts authored at 1920x1080 (control bar, in-game quit menu) in the scale mode the HUD uses by default:
        // their header template fonts and their window fonts scale by different bases (see UiScaler.ScaleFontSize).
        foreach (var (name, file) in new[] { ("controlbar-anchor", "Window/ControlBar.wnd"), ("quitmenu-anchor", "Window/Menus/QuitMenu.wnd") })
        {
            var layout = WndParser.Parse(content.OpenRead(file), file);
            foreach (var (width, height) in Sizes)
            {
                var tree = new UiWindowTree(new UiViewport(width, height), UiScaleOptions.Original with { Mode = UiScaleMode.Anchor }, services);
                tree.AddLayout(layout);
                Capture(renderer, tree.BuildDrawList(), width, height, $"{name}-{width}x{height}", manifest);
            }
        }

        foreach (var (width, height) in Sizes)
        {
            var viewport = new UiViewport(width, height);
            Capture(renderer, SyntheticScenes.Text(viewport), width, height, $"text-{width}x{height}", manifest);
            Capture(renderer, SyntheticScenes.Clipping(viewport), width, height, $"clipping-{width}x{height}", manifest);
            Capture(renderer, SyntheticScenes.Images(viewport, plainImage), width, height, $"images-{width}x{height}", manifest);
            Capture(renderer, SyntheticScenes.Transform(viewport, rotatedImage), width, height, $"transform-{width}x{height}", manifest);
        }

        ShellScenes.Capture(_data, renderer.TextMeasure, outputDirectory, (list, width, height, name) => Capture(renderer, list, width, height, name, manifest));
        ShellFlowScenes.Capture(_data, renderer.TextMeasure, outputDirectory, (list, width, height, name) => Capture(renderer, list, width, height, name, manifest));

        File.WriteAllLines(Path.Combine(outputDirectory, "manifest.txt"), manifest);
        var totals = renderer.Stats;
        Console.WriteLine($"Totals: {totals.Format()}");
        Console.WriteLine($"Missing image names: {string.Join(", ", totals.MissingImageNames)}");
        Console.WriteLine($"Missing label keys: {string.Join(", ", totals.MissingLabelKeys)}");
        Console.WriteLine($"Font substitutions: {(renderer.FontSubstitutions.Count == 0 ? "none" : string.Join(", ", renderer.FontSubstitutions.Select(pair => $"{pair.Key} -> {pair.Value}")))}");
        Console.WriteLine($"Texture size mismatches: {(renderer.TextureSizeMismatches.Count == 0 ? "none" : string.Join(", ", renderer.TextureSizeMismatches))}");
        // Sanity check that every placeholder/counter path was actually exercised at least once per size (the synthetic
        // scenes draw one deliberately in each of the Sizes.Length runs; real WND menus may add more of their own, which is
        // fine and reported above, not a bug this tool should hide).
        if (totals.MissingImages < Sizes.Length)
            throw new InvalidDataException($"Expected at least {Sizes.Length} counted missing-image draws (one per size in the Transform scene), got {totals.MissingImages}.");
        if (totals.MissingLabels < Sizes.Length)
            throw new InvalidDataException($"Expected at least {Sizes.Length} counted missing-label draws (one per size in the Text scene), got {totals.MissingLabels}.");
        if (totals.UnbalancedClipPops != Sizes.Length)
            throw new InvalidDataException($"Expected exactly {Sizes.Length} counted unbalanced clip pops (one per size in the Clipping scene), got {totals.UnbalancedClipPops}.");
        if (totals.SubstitutedGlyphs < Sizes.Length)
            throw new InvalidDataException($"Expected at least {Sizes.Length} substituted glyphs (the private-use codepoints in the Text scene), got {totals.SubstitutedGlyphs}.");
    }

    private void Capture(UiRenderer renderer, UiDrawList list, int width, int height, string name, List<string> manifest)
    {
        // Stride's SpriteBatch shader decodes colors and texture samples as sRGB whenever the device runs in Linear color
        // space (regardless of the source texture's own format), and relies on the render target doing the matching
        // hardware re-encode on write; only an _SRgb view actually does that (a plain view silently skips it, crushing
        // every non-endpoint color, which is what a plain UNorm capture target on a Linear device showed during testing).
        // UiRenderer's own textures (atlas, mapped images) stay plain UNorm on purpose: an _SRgb source there would add a
        // second, unwanted hardware decode on top of the shader's own one. See UiRenderer.ToColor for the matching choice.
        var format = GraphicsDevice.ColorSpace == ColorSpace.Linear ? PixelFormat.R8G8B8A8_UNorm_SRgb : PixelFormat.R8G8B8A8_UNorm;
        using var target = Texture.New2D(GraphicsDevice, width, height, format, TextureFlags.RenderTarget | TextureFlags.ShaderResource);
        var commandList = GraphicsContext.CommandList;
        commandList.SetRenderTargetAndViewport(null, target);
        commandList.Clear(target, new Stride.Core.Mathematics.Color4(0, 0, 0, 1));
        renderer.Draw(GraphicsContext, list, width, height);
        var path = Path.Combine(outputDirectory, $"{name}.png");
        using (var stream = File.Create(path))
            target.Save(commandList, stream, ImageFileType.Png);
        var stats = renderer.LastFrame;
        manifest.Add($"{name}.png {width}x{height} commands={list.Commands.Length} {stats.Format()}");
        Console.WriteLine($"  captured {name}.png ({stats.Format()})");
    }
}
