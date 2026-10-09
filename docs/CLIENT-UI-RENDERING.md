# CLIENT-UI-RENDERING – Stride 2D renderer for UiDrawList

Package S4: the Stride renderer that actually brings a `UiDrawList` (package B2, `docs/UI-WND.md`) onto the screen.
Two layers:

- `src/CnCNew.Ui/Rendering/**` – Stride-free logic (font resolution, text layout, glyph atlas packing, clip stack,
  image rotation, statistics). References only `CnCNew.Ui.Layout`/`Runtime`/`Images`, no engine, no file system except
  through injected interfaces. Unit-tested in `tests/CnCNew.Ui.Tests/UiRenderingTests.cs`.
- `src/CnCNew.Client.Stride/Ui/**` – the thin Stride layer: GDI font provider (`GdiFontProvider`, like the original; FreeType
  remains as `UiFontBackend.FreeType` for comparison), texture cache for mapped images, glyph atlas as GPU textures, and
  `UiRenderer`, which uses `SpriteBatch`.

Verified with `tools/CnCNew.UiRenderTool` (see section 5): real menus from `game/content` plus
synthetic scenes, rendered in four resolutions, exported as PNG into a scratchpad directory and compared byte-identically over two
independent runs (SHA-256).

## 1. Drawing model

`UiRenderer.Draw(GraphicsContext, UiDrawList, width, height)` or, for manual use by HUD code,
`Begin(context, width, height)` / `Execute(command)` (or directly `DrawRect`/`DrawBorder`/`DrawImage`/`DrawText`/
`DrawTexture`/`PushClip`/`PopClip`) / `End()`. A frame is one `SpriteBatch`; every change of the scissor rectangle
ends the open batch and opens a new one (`SelectScissor`) – a draw that lies completely within its clip
needs no scissor change and stays in the running batch. Order is drawing order: later commands
lie over earlier ones, as in the original (`drawWindow`, `W3DDisplay::drawImage`/`drawLine`).

Primitives:

- `UiFillRect`/`UiBorderRect`: a quad from the 4×4 white block of every atlas page (inner 2×2 texels, so that no
  bilinear bleeding occurs at the edges), multiplied with the target colour as vertex colour.
- `UiImageDraw`: resolution through the injected `MappedImageTable`; `ROTATED_90_CLOCKWISE` images are set upright once on the
  CPU (`UiImagePixels.UprightRotated`, checked against `Image::parseImageStatus`/`W3DDisplay::drawImage`) and
  uploaded as a small texture of their own. Mirroring via `SpriteEffects`.
- `UiTextDraw`: one line from `UiTextRuns.Layout` (integer advances, no kerning, like
  `Render2DSentenceClass::Get_Text_Extents`), glyphs from the atlas, shadow colour drawn at (+1, +1) before the main
  colour. Line wrapping, block width and line position do **not** come from the renderer, but from the window tree
  (`UiSentenceLayout`, after `Build_Sentence_Not_Centered`/`Build_Sentence_Centered`, `docs/TEXT-AUDIT.md` section 2).
- `UiPushClip`/`UiPopClip`: `UiClipStack`, intersection with the initial viewport as the floor; a surplus pop is counted,
  not applied.
- `UiCustomDraw`: the running batch is flushed, then the renderer calls `CustomDraw` (host callback for minimap,
  radar, portraits) with the `GraphicsContext`.

## 2. Font

No `Stride.Graphics.SpriteFont` and no `System.Drawing`. The original rasterises every character with a **GDI font**
(`FontCharsClass::Create_GDI_Font`/`Store_GDI_Char`, render2dsentence.cpp: `CreateFont`, `ExtTextOutW` into a 24-bit DIB, width from
`GetTextExtentPoint32W`); the client does the same via P/Invoke (`GdiFontProvider`, `gdi32`/`user32`) and is thereby on Windows the
original itself instead of an approximation. The earlier FreeType version (91 % squeeze for "Generals", unhinted advances) is kept as
`UiFontBackend.FreeType`; `UiRenderTool --font-backend freetype` and `--font-audit` compare both (measured values in
`docs/TEXT-AUDIT.md`).

- **Font resolution** (`UiFontPolicy`): 96 DPI fixed, pixel height `(punkte·96+36)/72` (`MulDiv` rounding like `Create_GDI_Font`). The name
  `Generals` is Arial with `lfWidth = Höhe·0,40` (`(int)(Höhe·0,40f)`); **GDI chooses the width itself** (it scales Arial to the
  average width), measured that is about 87 % of the natural width (capital letters at 24 px: 364 instead of 417 px), not the 91 % of the
  earlier approximation. `UiFontSpec.WidthPercent == 91` still marks "Generals", for GDI only this marker counts.
- **Cell dimensions and advance** (`GdiFontFace`): line height `tmHeight`, ascent `tmAscent`, advance `GetTextExtentPoint32W` per character (no kerning) minus
  `tmOverhang` (0 for "Generals"), `W` one pixel wider (`xOrigin`); `PixelOverlap = Höhe/8` limited to [0, 4], the original adds it to the
  block width (`Get_Extra_Overlap`, `UiFontFaceMetrics.Overlap`, `IUiTextMeasure.ExtraOverlap`).
- **Coverage**: in the original the glyphs lie as an A4R4G4B4 texture; `pixel_value >> 4` is the alpha (16 levels), so the raster result has only
  the values 0, 17, …, 255. `GdiFontFace.Rasterize` reproduces that (cropped to the occupied area, `BearingY = Ascent − oberste Zeile`).
  Not reproduced: the OR combination of the `PixelOverlap` columns with which neighbouring glyphs overlap in the original (the atlas blends
  via alpha); it affects only characters with overhang.
- **Substitutions are visible**: GDI silently substitutes a missing family. After `SelectObject`, `GdiFontProvider` checks with `GetTextFace` whether
  the family really is the requested one; otherwise it tries `UiFontPolicy.FallbackFamilies`, reports `angefordert → verwendet` once on stderr
  and lists it in `UiRenderer.FontSubstitutions`. Only if none of them is installed does GDI's substitute apply (also reported). On the
  measuring machine `Arial Unicode MS` is missing (`UnicodeFontName`, comes with Office): GDI takes Arial, the 97 different code points of the English
  strings all lie in Arial except for three C1 control characters (U+0085/0093/0094, no glyph, width 0 as in the original).
- **Fallback chain** (`UiFontChain`): if a character lacks the glyph of the family (`GetGlyphIndicesW`), the next one from
  `UiFontPolicy.FallbackFamilies` is opened (Arial, Segoe UI, Arial Unicode MS, Microsoft YaHei, Malgun Gothic, Yu Gothic, MS Gothic, Segoe UI
  Symbol) – lazily, only when needed; counted as `SubstitutedGlyphs`. The original always takes `UnicodeFontName` for characters ≥ 256; the chain is
  a superset of that.
- **Raster fonts**: `Courier` and `FixedSys` (Language.ini) are raster fonts; GDI has them, FreeType did not (substitute Arial).
- **Known limit**: the pixels are the GDI rasterisation of **this** Windows installation (Arial version, hinting). The original ran with the
  Arial version of its time (2003); differences in individual advances or edge pixels are possible and not measurable without the original.
  On every run `UiRenderTool` checks invariants that hold on every installation (`FontAudit.SelfCheck`: family really Arial, coverage only in
  steps of 17, cell height = ascent + descent, overlap, Generals clearly narrower than Arial).

## 3. Colour space (no gamma correction)

`UiRenderer.ToColor` returns raw sRGB bytes (`R/255, G/255, B/255, A/255`), without `Color4.ToLinear()`.
`UiImageTextures`/`AtlasPageTextures` never use an `_SRgb` pixel format, DDS loading happens with `loadAsSRGB: false`.
Reason: Stride runs with `GraphicsDevice.ColorSpace == Linear`; `SpriteBatch`'s default shader already decodes vertex colour *and*
texture sample itself as sRGB as soon as the device works linearly, and relies on the bound
render target (in Stride normally an `_SRgb` view of the swap chain) encoding the result back on writing. A
`ToLinear()` of our own here would have decoded twice (checked empirically: led to almost black areas from
`WndColor(20,24,32,255)`); an `_SRgb` source texture would have triggered, in addition to the shader decoding, a second,
hardware-side decoding and likewise darkened. Net effect of the chosen combination: sRGB in,
sRGB out – like the uncoloured GDI/D3D9 compositing of the original; only the intermediate computation runs linearly, which
lets soft edges and translucent overlays deviate by a few 8-bit steps from a naive byte blend.
**Text edges (Phase 16):** the linear-space blend has a disadvantage that the original does not have: it blends in bytes. 50 % coverage of white text on black gives byte 128 in the original, 188 here –
light text looked bold and washed out (measured at the edge pixel with 6/15 coverage: 170 instead of 102). `UiAlphaCurves.LightOnDark` therefore stores, for light text
(`IsLight`: luminance of the colour bytes ≥ ½), `decode(Deckung)` as alpha; over black exactly the gamma-space value then comes out again (the edge pixels are afterwards the
steps of 17 of the 4-bit coverage), over a non-black ground it is an approximation. Dark text and shadows keep the coverage. For this the renderer keeps a second
`UiGlyphCache` on the same atlas, only on devices with `ColorSpace.Linear`.

The capture tool (section 5) must keep the same convention and therefore chooses `_SRgb` only for its
offscreen render target, never for the UI's own textures (`CaptureGame.Capture`, coupled to
`GraphicsDevice.ColorSpace`).

## 4. Public API (for menu and HUD code)

```csharp
internal sealed class UiRenderer : IDisposable
{
    UiRenderer(RenderServices services);                                              // from the world layer setup
    UiRenderer(GraphicsDevice, ContentFileSystem, MappedImageTable, string language, UiFontBackend backend = UiFontBackend.Gdi);  // directly, e.g. in the tool

    Action<GraphicsContext, UiCustomDraw>? CustomDraw { get; set; }
    UiTextMetrics TextMeasure { get; }     // for UiTreeServices.TextMeasure
    IUiImageLookup Images { get; }         // for UiTreeServices.Images
    string Language { get; }

    void Draw(GraphicsContext, UiDrawList, int width, int height);   // a whole frame
    void Begin(GraphicsContext, int width, int height);
    void Execute(UiDrawCommand);
    void PushClip(UiRect); void PopClip();
    void DrawRect(UiRect, WndColor); void DrawBorder(UiRect, WndColor);
    void DrawImage(string name, UiRect dest, WndColor tint, bool flipH = false, bool flipV = false);
    void DrawTexture(Texture, UiRect dest, UiRect? source, WndColor tint);  // host textures (minimap, portraits)
    void DrawText(UiTextDraw);
    void End();

    UiRenderStats Stats { get; }       // cumulative since construction/ResetStats()
    UiRenderStats LastFrame { get; }   // last Begin…End block
    void ResetStats();
    IReadOnlyDictionary<string,string> FontSubstitutions { get; }
    IReadOnlyCollection<string> TextureSizeMismatches { get; }
}
```

An integrator (menu shell, HUD) attaches `UiRenderer` to the same `RenderServices` instance as the 3D world, and calls per frame
either `Draw(context, tree.BuildDrawList(), width, height)` for a whole window layer or the individual
`Draw*`/`Push/PopClip` methods between its own `Begin`/`End` for HUD elements that do not go through `UiWindowTree`
(e.g. a minimap texture via `DrawTexture`). `TextMeasure`/`Images` are passed unchanged to `UiTreeServices`,
so that layout and drawing see the same font metrics.

## 5. Verification tool

`tools/CnCNew.UiRenderTool` (not in `CnCNew.slnx`, `dotnet build tools/CnCNew.UiRenderTool/CnCNew.UiRenderTool.csproj`
or `dotnet run --project tools/CnCNew.UiRenderTool -- --content-dir <pfad> --out-dir <ausgabe>`). Includes the
client Stride sources (except its `Program.cs`) directly, because `UiRenderer` is internal, and renders with its own
offscreen `Texture` render target (no visible window needed) in four resolutions (800×600, 1280×720, 1920×1080,
2560×1080):

- four real WND layouts from `game/content`: `Window/Menus/MainMenu.wnd`, `OptionsMenu.wnd`,
  `SkirmishGameOptionsMenu.wnd`, `Window/ControlBar.wnd` (HUD-like scene);
- four synthetic scenes (`SyntheticScenes.cs`): umlauts/bold/shadow/unresolved label/missing glyph,
  nested clips including a surplus pop, tinted/disabled/pressed/mirrored images, as well as a
  rotated texture (`WatermarkGLA`, a real `ROTATED_90_CLOCKWISE` image from `SCShellUserInterface512.INI`) next to
  a deliberately unknown image name.

Phase 16 (text comparison, `docs/TEXT-AUDIT.md`) adds: `--font-audit` (GDI against FreeType on all 80 original layouts, glyph coverage of the
strings; writes `font-audit.txt` instead of images), `--font-backend gdi|freetype` (default `gdi`), the self-check of the GDI font at the
start of every run and the `flow-*` scenes (`ShellFlowScenes.cs`): single-player menu, pointer tooltip (half faded in and finished),
skirmish setup, map choice (empty and with a selection), setup with map, loading screen, final screen after a short match,
credits, campaign screen, difficulty dialog, briefing (start and later). `scripts/capture-text.ps1` runs everything including the
client scenes `hud-money` and `hud-tooltip` (input script) in 1280x720 and 1920x1080 and writes the pixel checksums (96 images, two runs
identical). Run of 5 October 2026: `commands=20997 sprites=44459 drawCalls=3735 batches=1368 missingImages=12 missingTextures=0 missingLabels=12 missingGlyphs=0
substitutedGlyphs=12 customDraws=56 unbalancedClips=4`, one texture with a deviating size (`WarSchoolBar.tga`, file 512x512, table 1,024x1,024; counted, source rectangles scaled).

Since the first visual check of the shell (`docs/GAME-SHELL.md` §11) the following are added:

- `shell-*-{1280x720,1920x1080}.png` (`ShellScenes.cs`): the shell as a player sees it – through
  `GameShell.Create` with the text metrics of the renderer and real input events, i.e. with the init code of the screens.
  Scenes: `main`, `main-hover`, `main-pressed`, `options`, `options-label-hover`, `options-slider-hover`,
  `options-slider-drag`, `options-slider-drag-outside`, `options-button-hover`, `main-after-options`, `quit`,
  `quit-hover`. The raw `mainmenu-*`/`options-*` images above, in contrast, show the WND file without init code.
- `controlbar-anchor-*` and `quitmenu-anchor-*`: the layouts authored at 1920×1080 in the mode `Anchor`, to judge the
  font sizes from header templates.

Run of 4 October 2026 with these scenes (64 PNGs): `commands=14296 sprites=30456 drawCalls=2231 batches=889
missingImages=12 missingTextures=0 missingLabels=12 missingGlyphs=0 substitutedGlyphs=12 customDraws=52
unbalancedClips=4` (the four additional missing images are `HardCodedWithControlBarSchemeIni` in
`controlbar-anchor`); two runs give identical SHA-256 sums for all 64 PNGs. The numbers of the following paragraph
apply to the earlier scope.

Last run (32 PNGs, `game/content`, re-measure): `commands=5778 sprites=12542 drawCalls=854 batches=361
missingImages=8 missingTextures=0 missingLabels=12 missingGlyphs=0 substitutedGlyphs=12 customDraws=28
unbalancedClips=4`. The eight missing images are the one deliberately wrong one per resolution (`transform` scene, 4×)
plus one real missing ControlBar image (`HardCodedWithControlBarSchemeIni`, 4×; the command bar apparently
references a scheme image assembled at run time by the C++ code, which the static `MappedImageTable` does not
know – an honest, counted gap, no silent fallback). The twelve missing labels are the one deliberate one
(`text` scene, 4×) plus two real ones in `OptionsMenu`/`SkirmishGameOptionsMenu` (`Static Text`, `Version`, 4× each) –
windows whose `TEXT` is parsed as a label instead of as a literal (package B2, `UiDrawBuilder`), but does not exist in the English
string table; that too is counted instead of swallowed. Two independent runs give identical SHA-256 sums for all 32 PNGs
(determinism confirmed).

**Known peculiarity of this tool that is not to be fixed:** `MainMenu.wnd` shows several overlapping
captions (e.g. "SOLO PLAY" over a second, smaller text). The tool builds the window tree once with
`AddLayout` + `BuildDrawList` and simulates none of the original C++ visibility switches (popup groups,
saved-game selection); the original shows only one of the overlaid groups at run time. That is a property
of the test setup (and potentially of package B2's `UiWindowTree`), not an anomaly of the renderer built here – every
single drawing command is executed correctly and deterministically.

## 6. Counters (`UiRenderStats`)

`Commands`, `Sprites`, `DrawCalls` (texture change runs), `Batches` (Begin/End pairs, one batch per frame plus one per
clip change), `MissingImages`/`MissingTextures`/`MissingLabels`/`MissingGlyphs`/`SubstitutedGlyphs`, `CustomDraws`,
`UnbalancedClipPops`, plus sorted name lists of the first four. Nothing that produces a visible placeholder
(magenta fill for images, magenta text with underscore for labels, notdef box for glyphs) stays uncounted – see
`docs/IMPLEMENTATION-PLAN.md`/CLAUDE.md, "never fall back silently to default values", carried over to the presentation
layer.

## 7. Window transitions in the renderer (Phase 16)

The transition animations of the shell (`docs/GAME-SHELL.md` §12) need **nothing new** from the renderer: the stand-in images of the effects are ordinary `UiDrawCommand`s – `UiBorderRect` and `UiFillRect` with alpha (white
flash, black fade), `UiImageDraw` with alpha tint (logo fade-in, growing images, the three parts of a button) and `UiTextDraw` (typed text) – and are appended after the draw list of the respective
screen. `UiRenderer.DrawRect`/`DrawBorder`/`DrawImage` blend with the alpha of the colour; the order of the commands is the drawing order. Two small extensions of the window layer behind it:
`UiWindowTree.BuildPushButtonImages(fenster, alpha)` (left/middle/right image of a button with alpha, without text; `PushButtonImageDrawThree`) and `UiWindow.SetEnabledImage0` (`winSetEnabledImage`).

**Visual check:** in addition to the images from §5, `tools/CnCNew.UiRenderTool` writes the sequences of the animated shell (`TransitionScenes.cs`, 1280×720, 30 frames/s, `transition-NAME-fNN-1280x720.png`, N = frames from
the start of the sequence):

| Sequence | Images (frames) | shows |
|---|---|---|
| `entry` | 0, 12, 24, 30, 34, 37, 40, 43, 46, 50, 54, 58, `settled` | black, fade-in of the ruler, frame and logo, buttons flash up one after the other, rest state |
| `single` | 1, 4, 8, 12, 16, 20, 24, 28, 34, `settled` | root buttons flash out, frame and five buttons flash in, the faction logos grow |
| `hover-usa`, `hover-usa-away` | 1, 2, 3, 6 / 1, 2, 6 | the USA logo grows to the medium size and back |
| `skirmish-out`, `skirmish-in` | 2, 6, 10 / 1, 3, 4, 5, 6, 8, 10 | single-player column flashes out; the setup fades in over black (content appears at frame 5) |
| `score` | 0, 1, 8, 12, 20, 30, 40, 55, 70 | rows appear one after the other, numbers count up (real 900-tick match) |

Run of 5 October 2026: 113 PNGs (64 from §5 + 49 sequence images), `commands=19265 sprites=42613 drawCalls=3532 batches=1320 missingImages=12 missingTextures=0 missingLabels=12 missingGlyphs=0 substitutedGlyphs=12
customDraws=52 unbalancedClips=4` (the twelve missing images are the same as in §5: no `Gradient` any more – the effect does not draw the gradient image missing in the retail content in the first place, `docs/GAME-SHELL.md`
§12 item 7); two runs give identical SHA-256 sums for all 113 files. Looked at were entry (f00/f24/f37/f46/settled), single player (f08/f20/settled), hover (f06), skirmish (out f06, in f04/f05)
and score screen (f30). **That does not replace a visual check in the running window:** tempo, sound and impression are not judged.
