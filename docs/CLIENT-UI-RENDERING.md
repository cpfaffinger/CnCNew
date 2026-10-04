# CLIENT-UI-RENDERING – Stride-2D-Renderer für UiDrawList

Paket S4: der Stride-Renderer, der eine `UiDrawList` (Paket B2, `docs/UI-WND.md`) tatsächlich auf den Bildschirm bringt.
Zwei Schichten:

- `src/CnCNew.Ui/Rendering/**` – Stride-freie Logik (Fontauflösung, Textlayout, Glyph-Atlas-Packung, Clip-Stack,
  Bildrotation, Statistik). Referenziert nur `CnCNew.Ui.Layout`/`Runtime`/`Images`, keine Engine, kein Dateisystem außer
  über injizierte Schnittstellen. Unit-getestet in `tests/CnCNew.Ui.Tests/UiRenderingTests.cs`.
- `src/CnCNew.Client.Stride/Ui/**` – die dünne Stride-Schicht: FreeType-Fontprovider über die Windows-Registry,
  Texturcache für gemappte Bilder, Glyph-Atlas als GPU-Texturen, und `UiRenderer`, der `SpriteBatch` benutzt.

Verifiziert mit `tools/CnCNew.UiRenderTool` (siehe Abschnitt 5): echte Menüs aus `.local/phase2-import-a` plus
synthetische Szenen, gerendert in vier Auflösungen, als PNG in ein Scratchpad-Verzeichnis exportiert und über zwei
unabhängige Läufe byteidentisch verglichen (SHA-256).

## 1. Zeichenmodell

`UiRenderer.Draw(GraphicsContext, UiDrawList, width, height)` bzw. für die manuelle Nutzung durch HUD-Code
`Begin(context, width, height)` / `Execute(command)` (oder direkt `DrawRect`/`DrawBorder`/`DrawImage`/`DrawText`/
`DrawTexture`/`PushClip`/`PopClip`) / `End()`. Ein Frame ist ein `SpriteBatch`; jede Änderung des Scissor-Rechtecks
beendet den offenen Batch und öffnet einen neuen (`SelectScissor`) – ein Draw, der vollständig innerhalb seines Clips
liegt, braucht keinen Scissor-Wechsel und bleibt im laufenden Batch. Reihenfolge ist Zeichenreihenfolge: spätere Befehle
liegen über früheren, wie im Original (`drawWindow`, `W3DDisplay::drawImage`/`drawLine`).

Primitiven:

- `UiFillRect`/`UiBorderRect`: ein Quad aus dem 4×4-Weißblock jeder Atlas-Seite (innere 2×2-Texel, damit kein
  Bilinear-Bleeding an den Rändern auftritt), mit der Zielfarbe als Vertexfarbe multipliziert.
- `UiImageDraw`: Auflösung über die injizierte `MappedImageTable`; `ROTATED_90_CLOCKWISE`-Bilder werden einmalig auf der
  CPU aufgerichtet (`UiImagePixels.UprightRotated`, gegen `Image::parseImageStatus`/`W3DDisplay::drawImage` geprüft) und
  als eigene kleine Textur hochgeladen. Spiegelungen über `SpriteEffects`.
- `UiTextDraw`: Zeilenlayout aus `UiTextRuns.Layout` (ganzzahlige Advances, kein Kerning, wie
  `Render2DSentenceClass::Get_Text_Extents`), Glyphen aus dem Atlas, Schattenfarbe bei (+1, +1) vor der Hauptfarbe
  gezeichnet.
- `UiPushClip`/`UiPopClip`: `UiClipStack`, Schnitt mit dem Ausgangsviewport als Boden; ein überzähliges Pop wird gezählt,
  nicht angewandt.
- `UiCustomDraw`: der laufende Batch wird geflusht, dann ruft der Renderer `CustomDraw` (Host-Callback für Minimap,
  Radar, Portraits) mit dem `GraphicsContext` auf.

## 2. Schrift

Kein `Stride.Graphics.SpriteFont` (dynamische GDI-Rasterung ist plattform-/DPI-abhängig und nicht deterministisch über
Läufe), kein `System.Drawing`. Stattdessen FreeType über `Stride.SharpFont` (das Stride selbst mitliefert, `freetype.dll`
liegt im `Stride.Graphics`-Paket unter `runtimes/win-x64/native` und landet automatisch im Ausgabeverzeichnis).

- **Fontauflösung** (`UiFontPolicy`): 96 DPI fest, Pixelgröße `(punkte·96+36)/72` (`MulDiv`-Rundung wie
  `FontCharsClass::Create_GDI_Font`, render2dsentence.cpp). Der Fontname `Generals` ist im Original Arial mit
  `lfWidth = Höhe·0,40`; da Arials mittlere Zeichenbreite 0,4414 em beträgt, entspricht das 91 % horizontaler Stauchung
  (`UiFontPolicy.GeneralsWidthPercent`).
- **Fontdatei-Katalog** (`FontFileCatalog`, `WindowsFontCatalog`): liest
  `HKLM/HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts`, zerlegt Namen wie `Arial Bold (TrueType)` oder
  `Microsoft YaHei & Microsoft YaHei UI (TrueType)`. Nur `.ttf`/`.ttc`/`.otf`; Kursivschnitte werden nie gewählt; Fett
  ohne passenden Fettschnitt fällt auf Regular zurück (gezählt in `UiRenderer.FontSubstitutions`, einmalig auf stderr
  gemeldet).
- **Fallback-Kette** (`UiFontChain`): fehlt die angeforderte Familie ganz, oder fehlt einem Zeichen der Glyph, wird die
  nächste aus `UiFontPolicy.FallbackFamilies` geöffnet (Arial, Segoe UI, Arial Unicode MS, Microsoft YaHei,
  Malgun Gothic, Yu Gothic, MS Gothic, Segoe UI Symbol) – lazy, nur bei Bedarf. Kein Font irgendwo installiert wirft
  `FileNotFoundException`, es gibt keinen eingebetteten Ersatz.
- **Rasterung**: `FT_LOAD_RENDER` in einem Aufruf. Ein zweistufiges `LoadGlyph` + separates `RenderGlyph` – identisch zur
  FreeType-Standardempfehlung und in einem isolierten Testprogramm erfolgreich – lieferte im Stride-Prozess
  konsequent `PixelMode.None` (unberechnete Bitmap); die kombinierte Flagge ist zuverlässig in beiden Fällen und wird
  daher durchgängig verwendet (`FreeTypeFace.Rasterize`). Ergebnis: weiße RGBA-Glyphen mit der Abdeckung in Alpha,
  gepackt in `UiGlyphAtlas` (Shelf-Packing, deterministisch bei gleicher Aufrufreihenfolge).
- **Bekannte Grenze**: Die Glyphen sind pixelgenau zur FreeType-Rasterung dieser Windows-Installation, nicht zur
  originalen GDI-Rasterung von `CreateFont`/`ExtTextOut`. Kantenglättung, Hinting und Zeichenbreiten weichen in
  Subpixel-Details vom Original ab; sichtbar wird das höchstens bei sehr kleinen Schriftgrößen. Für die tatsächlich
  installierten Familien (Arial, Segoe UI) ist die Deckung vollständig; ein System ohne diese Familien nutzt den
  nächsten Fallback und zählt das in `FontSubstitutions`.

## 3. Farbraum (keine Gammakorrektur)

`UiRenderer.ToColor` gibt rohe sRGB-Bytes zurück (`R/255, G/255, B/255, A/255`), ohne `Color4.ToLinear()`.
`UiImageTextures`/`AtlasPageTextures` verwenden nie ein `_SRgb`-Pixelformat, DDS-Laden geschieht mit `loadAsSRGB: false`.
Grund: Stride läuft mit `GraphicsDevice.ColorSpace == Linear`; `SpriteBatch`s Standard-Shader dekodiert Vertexfarbe *und*
Textur-Sample bereits selbst als sRGB, sobald das Gerät linear arbeitet, und verlässt sich darauf, dass das gebundene
Render-Target (in Stride normalerweise eine `_SRgb`-Sicht der Swapchain) das Ergebnis beim Schreiben zurückkodiert. Ein
eigenes `ToLinear()` hier hätte doppelt dekodiert (empirisch geprüft: führte zu fast schwarzen Flächen aus
`WndColor(20,24,32,255)`); eine `_SRgb`-Quelltextur hätte zusätzlich zur Shader-Dekodierung eine zweite,
Hardware-seitige Dekodierung ausgelöst und ebenfalls abgedunkelt. Nettoeffekt der gewählten Kombination: sRGB rein,
sRGB raus – wie die ungefärbte GDI/D3D9-Kompositierung des Originals; nur die Zwischenrechnung läuft linear, was
weiche Kanten und durchscheinende Überlagerungen um wenige 8-Bit-Stufen von einer naiven Byte-Mischung abweichen lässt.
Das Aufnahmewerkzeug (Abschnitt 5) muss dieselbe Konvention einhalten und wählt daher `_SRgb` nur für sein
Offscreen-Render-Target, nie für die UI-eigenen Texturen (`CaptureGame.Capture`, an `GraphicsDevice.ColorSpace`
gekoppelt).

## 4. Öffentliche API (für Menü- und HUD-Code)

```csharp
internal sealed class UiRenderer : IDisposable
{
    UiRenderer(RenderServices services);                                              // aus dem Weltlayer-Setup
    UiRenderer(GraphicsDevice, ContentFileSystem, MappedImageTable, string language);  // direkt, z. B. im Tool

    Action<GraphicsContext, UiCustomDraw>? CustomDraw { get; set; }
    UiTextMetrics TextMeasure { get; }     // für UiTreeServices.TextMeasure
    IUiImageLookup Images { get; }         // für UiTreeServices.Images
    string Language { get; }

    void Draw(GraphicsContext, UiDrawList, int width, int height);   // ein ganzer Frame
    void Begin(GraphicsContext, int width, int height);
    void Execute(UiDrawCommand);
    void PushClip(UiRect); void PopClip();
    void DrawRect(UiRect, WndColor); void DrawBorder(UiRect, WndColor);
    void DrawImage(string name, UiRect dest, WndColor tint, bool flipH = false, bool flipV = false);
    void DrawTexture(Texture, UiRect dest, UiRect? source, WndColor tint);  // Host-Texturen (Minimap, Portraits)
    void DrawText(UiTextDraw);
    void End();

    UiRenderStats Stats { get; }       // kumulativ seit Konstruktion/ResetStats()
    UiRenderStats LastFrame { get; }   // letzter Begin…End-Block
    void ResetStats();
    IReadOnlyDictionary<string,string> FontSubstitutions { get; }
    IReadOnlyCollection<string> TextureSizeMismatches { get; }
}
```

Ein Integrator (Menüschale, HUD) hängt `UiRenderer` an dieselbe `RenderServices`-Instanz wie die 3D-Welt, ruft pro Frame
entweder `Draw(context, tree.BuildDrawList(), width, height)` für eine ganze Fensterebene oder die einzelnen
`Draw*`/`Push/PopClip`-Methoden zwischen eigenem `Begin`/`End` für HUD-Elemente, die nicht über `UiWindowTree` laufen
(z. B. eine Minimap-Textur via `DrawTexture`). `TextMeasure`/`Images` werden unverändert an `UiTreeServices` gereicht,
damit Layout und Zeichnen dieselben Schriftmetriken sehen.

## 5. Verifikationswerkzeug

`tools/CnCNew.UiRenderTool` (nicht in `CnCNew.slnx`, `dotnet build tools/CnCNew.UiRenderTool/CnCNew.UiRenderTool.csproj`
bzw. `dotnet run --project tools/CnCNew.UiRenderTool -- --content-dir <pfad> --out-dir <ausgabe>`). Bindet die
Client-Stride-Quellen (außer dessen `Program.cs`) direkt ein, da `UiRenderer` intern ist, und rendert mit einem eigenen
Offscreen-`Texture`-Render-Target (kein sichtbares Fenster nötig) in vier Auflösungen (800×600, 1280×720, 1920×1080,
2560×1080):

- vier echte WND-Layouts aus `.local/phase2-import-a`: `Window/Menus/MainMenu.wnd`, `OptionsMenu.wnd`,
  `SkirmishGameOptionsMenu.wnd`, `Window/ControlBar.wnd` (HUD-artige Szene);
- vier synthetische Szenen (`SyntheticScenes.cs`): Umlaute/Fett/Schatten/nicht aufgelöstes Label/fehlender Glyph,
  verschachtelte Clips samt überzähligem Pop, getönte/deaktivierte/gedrückte/gespiegelte Bilder, sowie eine
  rotierte Textur (`WatermarkGLA`, ein echtes `ROTATED_90_CLOCKWISE`-Bild aus `SCShellUserInterface512.INI`) neben
  einem absichtlich unbekannten Bildnamen.

Seit der ersten Sichtprüfung der Hülle (`docs/GAME-SHELL.md` §11) kommen dazu:

- `shell-*-{1280x720,1920x1080}.png` (`ShellScenes.cs`): die Hülle so, wie ein Spieler sie sieht – über
  `GameShell.Create` mit der Textmetrik des Renderers und echten Eingabeereignissen, also mit dem Init-Code der Screens.
  Szenen: `main`, `main-hover`, `main-pressed`, `options`, `options-label-hover`, `options-slider-hover`,
  `options-slider-drag`, `options-slider-drag-outside`, `options-button-hover`, `main-after-options`, `quit`,
  `quit-hover`. Die rohen `mainmenu-*`/`options-*`-Bilder darüber zeigen dagegen die WND-Datei ohne Init-Code.
- `controlbar-anchor-*` und `quitmenu-anchor-*`: die bei 1920×1080 verfassten Layouts im Modus `Anchor`, um die
  Schriftgrößen aus Header-Templates zu beurteilen.

Lauf vom 4. Oktober 2026 mit diesen Szenen (64 PNGs): `commands=14296 sprites=30456 drawCalls=2231 batches=889
missingImages=12 missingTextures=0 missingLabels=12 missingGlyphs=0 substitutedGlyphs=12 customDraws=52
unbalancedClips=4` (die vier zusätzlichen fehlenden Bilder sind `HardCodedWithControlBarSchemeIni` in
`controlbar-anchor`); zwei Läufe ergeben für alle 64 PNGs identische SHA-256-Summen. Die Zahlen des folgenden Absatzes
gelten für den früheren Umfang.

Letzter Lauf (32 PNGs, `.local/phase2-import-a`, neu messen): `commands=5778 sprites=12542 drawCalls=854 batches=361
missingImages=8 missingTextures=0 missingLabels=12 missingGlyphs=0 substitutedGlyphs=12 customDraws=28
unbalancedClips=4`. Die acht fehlenden Bilder sind das eine absichtlich falsche pro Auflösung (`transform`-Szene, 4×)
plus ein echtes fehlendes ControlBar-Bild (`HardCodedWithControlBarSchemeIni`, 4×; die Steuerleiste referenziert
offenbar ein zur Laufzeit vom C++-Code zusammengesetztes Schema-Bild, das die statische `MappedImageTable` nicht
kennt – eine ehrliche, gezählte Lücke, kein stiller Fallback). Die zwölf fehlenden Label sind das eine absichtliche
(`text`-Szene, 4×) plus zwei echte in `OptionsMenu`/`SkirmishGameOptionsMenu` (`Static Text`, `Version`, je 4×) –
Fenster, deren `TEXT` als Label statt als Literal geparst wird (Paket B2, `UiDrawBuilder`), aber in der englischen
Stringtabelle nicht existiert; auch das wird gezählt statt verschluckt. Zwei unabhängige Läufe ergeben für alle 32 PNGs
identische SHA-256-Summen (Determinismus bestätigt).

**Bekannte, nicht zu behebende Eigenheit dieses Werkzeugs:** `MainMenu.wnd` zeigt mehrere sich überlappende
Beschriftungen (z. B. „SOLO PLAY“ über einem zweiten, kleineren Text). Das Werkzeug baut den Fensterbaum einmalig mit
`AddLayout` + `BuildDrawList` und simuliert keine der ursprünglichen C++-Sichtbarkeitsumschaltungen (Popup-Gruppen,
Ladespielstand-Auswahl); das Original zeigt zur Laufzeit nur eine der überlagerten Gruppen. Das ist eine Eigenschaft
des Testaufbaus (und potentiell von Paket B2s `UiWindowTree`), keine Auffälligkeit des hier gebauten Renderers – jeder
einzelne Zeichenbefehl wird korrekt und deterministisch ausgeführt.

## 6. Zähler (`UiRenderStats`)

`Commands`, `Sprites`, `DrawCalls` (Texturwechsel-Läufe), `Batches` (Begin/End-Paare, ein Batch pro Frame plus einer je
Clip-Wechsel), `MissingImages`/`MissingTextures`/`MissingLabels`/`MissingGlyphs`/`SubstitutedGlyphs`, `CustomDraws`,
`UnbalancedClipPops`, dazu sortierte Namenslisten der ersten vier. Nichts, was einen sichtbaren Platzhalter erzeugt
(Magenta-Füllung für Bilder, Magenta-Text mit Unterstrich für Label, Notdef-Box für Glyphen), bleibt ungezählt – siehe
`docs/IMPLEMENTATION-PLAN.md`/CLAUDE.md, „nie still auf Standardwerte zurückfallen“, auf die Präsentationsschicht
übertragen.
