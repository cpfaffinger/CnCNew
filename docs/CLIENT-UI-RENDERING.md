# CLIENT-UI-RENDERING – Stride-2D-Renderer für UiDrawList

Paket S4: der Stride-Renderer, der eine `UiDrawList` (Paket B2, `docs/UI-WND.md`) tatsächlich auf den Bildschirm bringt.
Zwei Schichten:

- `src/CnCNew.Ui/Rendering/**` – Stride-freie Logik (Fontauflösung, Textlayout, Glyph-Atlas-Packung, Clip-Stack,
  Bildrotation, Statistik). Referenziert nur `CnCNew.Ui.Layout`/`Runtime`/`Images`, keine Engine, kein Dateisystem außer
  über injizierte Schnittstellen. Unit-getestet in `tests/CnCNew.Ui.Tests/UiRenderingTests.cs`.
- `src/CnCNew.Client.Stride/Ui/**` – die dünne Stride-Schicht: GDI-Fontprovider (`GdiFontProvider`, wie das Original; FreeType
  bleibt als `UiFontBackend.FreeType` zum Vergleich), Texturcache für gemappte Bilder, Glyph-Atlas als GPU-Texturen, und
  `UiRenderer`, der `SpriteBatch` benutzt.

Verifiziert mit `tools/CnCNew.UiRenderTool` (siehe Abschnitt 5): echte Menüs aus `game/content` plus
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
- `UiTextDraw`: eine Zeile aus `UiTextRuns.Layout` (ganzzahlige Advances, kein Kerning, wie
  `Render2DSentenceClass::Get_Text_Extents`), Glyphen aus dem Atlas, Schattenfarbe bei (+1, +1) vor der Hauptfarbe
  gezeichnet. Zeilenumbruch, Blockbreite und Zeilenposition kommen **nicht** aus dem Renderer, sondern vom Fensterbaum
  (`UiSentenceLayout`, nach `Build_Sentence_Not_Centered`/`Build_Sentence_Centered`, `docs/TEXT-AUDIT.md` Abschnitt 2).
- `UiPushClip`/`UiPopClip`: `UiClipStack`, Schnitt mit dem Ausgangsviewport als Boden; ein überzähliges Pop wird gezählt,
  nicht angewandt.
- `UiCustomDraw`: der laufende Batch wird geflusht, dann ruft der Renderer `CustomDraw` (Host-Callback für Minimap,
  Radar, Portraits) mit dem `GraphicsContext` auf.

## 2. Schrift

Kein `Stride.Graphics.SpriteFont` und kein `System.Drawing`. Das Original rastert jedes Zeichen mit einer **GDI-Schrift**
(`FontCharsClass::Create_GDI_Font`/`Store_GDI_Char`, render2dsentence.cpp: `CreateFont`, `ExtTextOutW` in eine 24-Bit-DIB, Breite aus
`GetTextExtentPoint32W`); der Client macht dasselbe über P/Invoke (`GdiFontProvider`, `gdi32`/`user32`) und ist damit auf Windows die
Vorlage selbst statt einer Annäherung. Die frühere FreeType-Fassung (91-%-Stauchung für „Generals“, ungehintete Vorschübe) ist als
`UiFontBackend.FreeType` erhalten; `UiRenderTool --font-backend freetype` und `--font-audit` vergleichen beide (Messwerte in
`docs/TEXT-AUDIT.md`).

- **Fontauflösung** (`UiFontPolicy`): 96 DPI fest, Pixelhöhe `(punkte·96+36)/72` (`MulDiv`-Rundung wie `Create_GDI_Font`). Der Name
  `Generals` ist Arial mit `lfWidth = Höhe·0,40` (`(int)(Höhe·0,40f)`); **die Breite wählt GDI selbst** (es skaliert Arial auf die
  Durchschnittsbreite), gemessen sind das etwa 87 % der natürlichen Breite (Großbuchstaben bei 24 px: 364 statt 417 px), nicht die 91 % der
  früheren Näherung. `UiFontSpec.WidthPercent == 91` kennzeichnet weiterhin „Generals“, für GDI zählt nur dieses Kennzeichen.
- **Zellmaße und Vorschub** (`GdiFontFace`): Zeilenhöhe `tmHeight`, Ascent `tmAscent`, Vorschub `GetTextExtentPoint32W` je Zeichen (kein Kerning) minus
  `tmOverhang` (0 bei „Generals“), `W` ein Pixel breiter (`xOrigin`); `PixelOverlap = Höhe/8` auf [0, 4] begrenzt, das Original addiert sie zur
  Blockbreite (`Get_Extra_Overlap`, `UiFontFaceMetrics.Overlap`, `IUiTextMeasure.ExtraOverlap`).
- **Deckung**: die Glyphen liegen im Original als A4R4G4B4-Textur; `pixel_value >> 4` ist das Alpha (16 Stufen), das Rasterergebnis hat also nur
  die Werte 0, 17, …, 255. `GdiFontFace.Rasterize` bildet das nach (zugeschnitten auf die belegte Fläche, `BearingY = Ascent − oberste Zeile`).
  Nicht nachgebildet: die ODER-Verknüpfung der `PixelOverlap` Spalten, mit der sich benachbarte Glyphen im Original überlagern (der Atlas mischt
  über Alpha); sie betrifft nur Zeichen mit Überstand.
- **Ersetzungen sind sichtbar**: GDI ersetzt eine fehlende Familie stillschweigend. `GdiFontProvider` prüft nach `SelectObject` mit `GetTextFace`, ob
  die Familie wirklich die angeforderte ist; sonst probiert er `UiFontPolicy.FallbackFamilies`, meldet `angefordert → verwendet` einmal auf stderr
  und führt es in `UiRenderer.FontSubstitutions`. Nur wenn nichts davon installiert ist, gilt GDIs Ersatz (ebenfalls gemeldet). Auf dem
  Messrechner fehlt `Arial Unicode MS` (`UnicodeFontName`, kommt mit Office): GDI nimmt Arial, 97 verschiedene Codepunkte der englischen
  Zeichenketten liegen bis auf drei C1-Steuerzeichen (U+0085/0093/0094, kein Glyph, Breite 0 wie im Original) alle in Arial.
- **Fallback-Kette** (`UiFontChain`): fehlt einem Zeichen der Glyph der Familie (`GetGlyphIndicesW`), wird die nächste aus
  `UiFontPolicy.FallbackFamilies` geöffnet (Arial, Segoe UI, Arial Unicode MS, Microsoft YaHei, Malgun Gothic, Yu Gothic, MS Gothic, Segoe UI
  Symbol) – lazy, nur bei Bedarf; gezählt als `SubstitutedGlyphs`. Das Original nimmt für Zeichen ≥ 256 immer `UnicodeFontName`; die Kette ist
  eine Obermenge davon.
- **Rasterschriften**: `Courier` und `FixedSys` (Language.ini) sind Rasterschriften; GDI hat sie, FreeType hatte sie nicht (Ersatz Arial).
- **Bekannte Grenze**: Die Pixel sind die GDI-Rasterung **dieser** Windows-Installation (Arial-Version, Hinting). Das Original lief mit der
  Arial-Fassung seiner Zeit (2003); Unterschiede in einzelnen Vorschüben oder Kantenpixeln sind möglich und ohne Original nicht messbar.
  `UiRenderTool` prüft bei jedem Lauf Invarianten, die auf jeder Installation gelten (`FontAudit.SelfCheck`: Familie wirklich Arial, Deckung nur in
  17er-Stufen, Zellhöhe = Ascent + Descent, Überlappung, Generals deutlich schmaler als Arial).

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
**Textkanten (Phase 16):** Der Linearraum-Blend hat einen Nachteil, den das Original nicht hat: es blendet in Bytes. 50 % Deckung weißer Schrift auf Schwarz ergibt im Original Byte 128, hier 188 –
helle Schrift wirkte fett und verwaschen (gemessen am Randpixel mit 6/15 Deckung: 170 statt 102). `UiAlphaCurves.LightOnDark` speichert deshalb für helle Schrift
(`IsLight`: Leuchtdichte der Farbbytes ≥ ½) als Alpha `decode(Deckung)`; über Schwarz kommt dann wieder genau der Gammaraum-Wert heraus (die Randpixel sind danach die
17er-Stufen der 4-Bit-Deckung), über nicht schwarzem Grund ist es eine Näherung. Dunkle Schrift und Schatten behalten die Deckung. Der Renderer hält dafür einen zweiten
`UiGlyphCache` auf demselben Atlas, nur auf Geräten mit `ColorSpace.Linear`.

Das Aufnahmewerkzeug (Abschnitt 5) muss dieselbe Konvention einhalten und wählt daher `_SRgb` nur für sein
Offscreen-Render-Target, nie für die UI-eigenen Texturen (`CaptureGame.Capture`, an `GraphicsDevice.ColorSpace`
gekoppelt).

## 4. Öffentliche API (für Menü- und HUD-Code)

```csharp
internal sealed class UiRenderer : IDisposable
{
    UiRenderer(RenderServices services);                                              // aus dem Weltlayer-Setup
    UiRenderer(GraphicsDevice, ContentFileSystem, MappedImageTable, string language, UiFontBackend backend = UiFontBackend.Gdi);  // direkt, z. B. im Tool

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

- vier echte WND-Layouts aus `game/content`: `Window/Menus/MainMenu.wnd`, `OptionsMenu.wnd`,
  `SkirmishGameOptionsMenu.wnd`, `Window/ControlBar.wnd` (HUD-artige Szene);
- vier synthetische Szenen (`SyntheticScenes.cs`): Umlaute/Fett/Schatten/nicht aufgelöstes Label/fehlender Glyph,
  verschachtelte Clips samt überzähligem Pop, getönte/deaktivierte/gedrückte/gespiegelte Bilder, sowie eine
  rotierte Textur (`WatermarkGLA`, ein echtes `ROTATED_90_CLOCKWISE`-Bild aus `SCShellUserInterface512.INI`) neben
  einem absichtlich unbekannten Bildnamen.

Phase 16 (Textvergleich, `docs/TEXT-AUDIT.md`) fügt hinzu: `--font-audit` (GDI gegen FreeType auf allen 80 Originallayouts, Glyphdeckung der
Zeichenketten; schreibt `font-audit.txt` statt Bilder), `--font-backend gdi|freetype` (Standard `gdi`), die Selbstprüfung der GDI-Schrift zu
Beginn jedes Laufs und die `flow-*`-Szenen (`ShellFlowScenes.cs`): Einzelspielermenü, Zeigertooltip (halb eingeblendet und fertig),
Skirmish-Einrichtung, Kartenwahl (leer und mit Auswahl), Einrichtung mit Karte, Ladeschirm, Abschlussbildschirm nach einem kurzen Gefecht,
Credits, Kampagnenbildschirm, Schwierigkeitsdialog, Briefing (Anfang und später). `scripts/capture-text.ps1` fährt alles inklusive der
Client-Szenen `hud-money` und `hud-tooltip` (Eingabeskript) in 1280x720 und 1920x1080 und schreibt die Pixel-Prüfsummen (96 Bilder, zwei Läufe
identisch). Lauf vom 5. Oktober 2026: `commands=20997 sprites=44459 drawCalls=3735 batches=1368 missingImages=12 missingTextures=0 missingLabels=12 missingGlyphs=0
substitutedGlyphs=12 customDraws=56 unbalancedClips=4`, eine Textur mit abweichender Größe (`WarSchoolBar.tga`, Datei 512x512, Tabelle 1.024x1.024; gezählt, Quellrechtecke skaliert).

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

Letzter Lauf (32 PNGs, `game/content`, neu messen): `commands=5778 sprites=12542 drawCalls=854 batches=361
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

## 7. Fensterübergänge im Renderer (Phase 16)

Die Übergangsanimationen der Hülle (`docs/GAME-SHELL.md` §12) brauchen vom Renderer **nichts Neues**: die Ersatzbilder der Effekte sind gewöhnliche `UiDrawCommand`s – `UiBorderRect` und `UiFillRect` mit Alpha (weißes
Aufblitzen, schwarze Blende), `UiImageDraw` mit Alpha-Tönung (Logo-Einblendung, wachsende Bilder, die drei Teile einer Schaltfläche) und `UiTextDraw` (getippter Text) – und werden nach der Zeichenliste des jeweiligen
Screens angehängt. `UiRenderer.DrawRect`/`DrawBorder`/`DrawImage` blenden mit dem Alpha der Farbe; die Reihenfolge der Befehle ist die Zeichenreihenfolge. Zwei kleine Erweiterungen der Fensterschicht dahinter:
`UiWindowTree.BuildPushButtonImages(fenster, alpha)` (linkes/mittleres/rechtes Bild einer Schaltfläche mit Alpha, ohne Text; `PushButtonImageDrawThree`) und `UiWindow.SetEnabledImage0` (`winSetEnabledImage`).

**Sichtprüfung:** `tools/CnCNew.UiRenderTool` schreibt zusätzlich zu den Bildern aus §5 die Folgen der animierten Hülle (`TransitionScenes.cs`, 1280×720, 30 Frames/s, `transition-NAME-fNN-1280x720.png`, N = Frames ab
Beginn der Folge):

| Folge | Bilder (Frames) | zeigt |
|---|---|---|
| `entry` | 0, 12, 24, 30, 34, 37, 40, 43, 46, 50, 54, 58, `settled` | schwarz, Einblendung des Lineals, Rahmen und Logo, Knöpfe blitzen einer nach dem anderen auf, Ruhezustand |
| `single` | 1, 4, 8, 12, 16, 20, 24, 28, 34, `settled` | Wurzelknöpfe blitzen aus, Rahmen und fünf Knöpfe blitzen ein, die Fraktionslogos wachsen |
| `hover-usa`, `hover-usa-away` | 1, 2, 3, 6 / 1, 2, 6 | das USA-Logo wächst auf die mittlere Größe und zurück |
| `skirmish-out`, `skirmish-in` | 2, 6, 10 / 1, 3, 4, 5, 6, 8, 10 | Einzelspieler-Spalte blitzt aus; die Einrichtung blendet über Schwarz ein (Inhalt erscheint bei Frame 5) |
| `score` | 0, 1, 8, 12, 20, 30, 40, 55, 70 | Zeilen erscheinen nacheinander, Zahlen zählen hoch (echtes 900-Tick-Gefecht) |

Lauf vom 5. Oktober 2026: 113 PNGs (64 aus §5 + 49 Folgenbilder), `commands=19265 sprites=42613 drawCalls=3532 batches=1320 missingImages=12 missingTextures=0 missingLabels=12 missingGlyphs=0 substitutedGlyphs=12
customDraws=52 unbalancedClips=4` (die zwölf fehlenden Bilder sind dieselben wie in §5: kein `Gradient` mehr – der Effekt zeichnet das im Retail-Bestand fehlende Verlaufsbild gar nicht erst, `docs/GAME-SHELL.md`
§12 Punkt 7); zwei Läufe ergeben für alle 113 Dateien identische SHA-256-Summen. Angesehen wurden Einstieg (f00/f24/f37/f46/settled), Einzelspieler (f08/f20/settled), Hover (f06), Skirmish (out f06, in f04/f05)
und Ergebnisbildschirm (f30). **Das ersetzt keine Sichtprüfung im laufenden Fenster:** Tempo, Ton und Eindruck sind nicht beurteilt.
