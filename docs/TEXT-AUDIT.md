# TEXT-AUDIT – Why the texts look "odd" (Phase 16, item 4)

As of 5 October 2026 (all numbers in this document were re-measured in this session). Brief: `docs/handoffs/HANDOFF-PHASE16.md` item 4, finding
`docs/DEMO-FINDINGS.md` no. 10. A human found the texts of the demos "odd" („komisch“) compared with the original; one cause (template fonts against 800 pixels of width,
`docs/GAME-SHELL.md` §11) was already fixed, the systematic comparison was missing.

**Method and its limit.** There are no original captures. The evidence is (1) the data of the game (WND layouts, `Language.ini`, `HeaderTemplate.ini`,
`Mouse.ini`, the English CSF), (2) the reference code (`references/ea-generals-zero-hour/GeneralsMD/Code/…`, only read and rewritten, source references below) and
(3) measurements on our own renderer. A pixel comparison with the original is not possible; what is called "fixed" here means "agrees with the reference code", not "looks
like in the original". The final authority remains a human (section 7).

## 1. Summary

Three groups of causes, in the order of their visible effect:

1. **The font itself.** The client rasterised with FreeType and an estimated horizontal compression for the font "Generals", and the edges of the letters were blended in linear space instead of in gamma space
   (brighter, bolder). The original rasterises with GDI and blends in bytes. On the 80 original layouts
   326 of 1,190 window texts (27 %) have a different width with FreeType, together 2,258 px; **294 of them are "Generals" texts** (titles, main menu buttons, headings; 2,224 px,
   up to 85 px per text). 29 of 36 font sizes used have a different line height (mostly 1 px). Fixed: `GdiFontProvider`.
2. **The layout of the text block.** Wrap rule, indent after a wrap, block width, centring, clipping and offsets of the gadgets deviated from `Render2DSentenceClass` and the
   `W3D…Draw` functions. Fixed: `UiSentenceLayout`, clipping per gadget type, offsets of lists, entry fields, combo boxes.
3. **Texts that should not exist like this:** the placeholder "$$$" at the top left, hotkeys and costs printed onto the command icons, the word "Entry" in entry fields, internal
   names (`FactionAmerica`, `Color 1`) in the skirmish setup, fixed font sizes in the credits, **no tooltips**. Fixed (tooltips simplified).

Fixed after that: texts of menus lying underneath showing through (section 7) and the mission texts (section 4.6: fonts from `Language.ini`, positions and colors from `InGameUI.ini`, typewriter text of the military caption, timers, superweapon list, message window).

## 2. How the original draws text (sources)

| Stage | Behaviour | Source |
|---|---|---|
| Font choice | `FontDesc` from `Language.ini` (name, point, bold); template fonts `HeaderTemplate.ini` with `adjustFontSize` = `floor(point · clamp(1 + (width/800 − 1) · 0.7, 1, 2))`; `FONT` of a window stays unchanged, windows without `FONT` get `DefaultWindowFont` (Times New Roman 14) | `GlobalLanguage.cpp` (`adjustFontSize`, `FontDesc::FontDesc`), `HeaderTemplate.cpp` (`populateGameFonts`), `GameWindowManager.cpp` (`WinCreate`), `GameWindowManagerScript.cpp` (`parseFont`, end of `parseGameWindow`) |
| Rasterising | `FontCharsClass::Create_GDI_Font`: height `−MulDiv(point, 96, 72)`; name "Generals" = Arial with `lfWidth = 0.40·height`, `CharOverhang = 0`; `PixelOverlap = height/8` on [0, 4]; `ANTIALIASED_QUALITY`. `Store_GDI_Char`: `ExtTextOutW` (`W` at x = 1) into a 24-bit DIB, width = `GetTextExtentPoint32W` + overlap, alpha = `Pixel >> 4` in an A4R4G4B4 texture | `render2dsentence.cpp` |
| Advance | `Get_Char_Spacing` = width − overlap − `tmOverhang` (no kerning); `Get_Char_Data`: characters ≥ 256 from `UnicodeFontName` ("Arial Unicode MS") | `render2dsentence.cpp`, `W3DGameFont.cpp` (`loadFontData`) |
| Wrap/block | `Build_Sentence_Not_Centered` (wrap at `Cursor.X + word width ≥ wrap width`, space at the beginning of the following line, `extent.X = maxX + overlap`), `Build_Sentence_Centered` (counts the space twice, centres every line on the block width of the other construction) | `render2dsentence.cpp` |
| Drawing | Shadow at (+1, +1) in the border color, then the text; clipping only if the gadget function sets it | `W3DDisplayString.cpp` (`draw`), `W3DStaticText.cpp` (clipping, margin 7, wrap width − 10), `W3DPushButton.cpp` (`drawButtonText`: wrap = width, no clipping, shortcut buttons top left + 2), `W3DCheckBox.cpp` (x = height, no wrap), `W3DRadioButton.cpp` (no wrap), `W3DListBox.cpp` (offset `TEXT_X_OFFSET` = 5, shadow fixed black), `W3DTextEntry.cpp` (entry field: x + 5 + 2, y + 5, clipping, half-width steps; combo box without entry: x + 10) |
| Money | `GadgetStaticTextSetText(MoneyDisplay, format("GUI:ControlBarMoneyDisplay", money))`, position/size from `MoneyUL/LR` of the scheme | `InGameUI.cpp` (`update`), `ControlBarScheme.cpp` |
| Command buttons | `GadgetButtonSetText(button, "")` (no text on the icon), tooltip function `commandButtonTooltip` / popup `ControlBarPopupDescription.wnd` (name, cost, description) | `ControlBar.cpp` (`setControlCommand`), `ControlBarPopupDescription.cpp` (`populateBuildTooltipLayout`) |
| Tooltip | `Mouse::drawTooltip`: box at (x + 20, y), wrap at `TooltipWidth` % of the screen width, fade-in over `TooltipFillTime`, font `TooltipFontName` from `Language.ini` | `Mouse.cpp` (`setCursorTooltip`, `drawTooltip`, `createStreamMessages`, `mouseNotifyResolutionChange`) |
| Credits | `CreditsTitleFont`/`CreditsMinorTitleFont`/`CreditsNormalFont` with `adjustFontSize` | `Credits.cpp` |

## 3. The capture set

`scripts/capture-text.ps1 [-ContentDir …] [-OutDir …] [-NoBuild]` (the pictures are not in the repo, only local): 96 PNGs and a list of the pixel checksums, two runs
bit-identical (all 96 checksums identical).

* `ui/` (tool `tools/CnCNew.UiRenderTool`, 92 pictures): the raw layouts (main menu, options, skirmish setup, command bar, quit menu) in 800x600, 1280x720,
  1920x1080, 2560x1080; the shell through `GameShell` with real inputs in 1280x720 and 1920x1080 (`shell-*`: main menu, states of the buttons, options and sliders, quit dialog;
  `flow-*`: single-player menu, pointer tooltip half faded in and finished, skirmish setup, map selection empty and with a selection, setup with map, loading screen,
  **score screen after a real 600-tick match**, credits, campaign screen USA, difficulty dialog, briefing at the beginning and later);
  plus `font-audit.txt`.
* `client/` (Stride client `--smoke-test --capture`, 4 pictures): `hud-money` (match with selection, money window) and `hud-tooltip` (input script: select the dozer, point at the first
  build command) in both sizes.
* Mission texts: `ui/missiontext-WxH.png` (tool scene `MissionTextScenes`, 800x600, 1280x720, 1920x1080: message lines, military caption half typed, timers and counters, superweapon list, cinematic text, message window); the pictures of the tool have an alpha channel in which translucent areas stay half transparent (a viewer over a bright background shows them too bright; discard the alpha channel before looking). In the client: `--smoke-test --mission … --filmstrip` (`docs/GAME-HUD.md` §17.3).
* Not in the set: transition animations.

## 4. Table: element × deviation × cause

Status: **fixed** (with test or measurement), **simplified**, **open**, **deliberate** (different from the original, with a reason).

### 4.1 Font and glyphs

| Element | Deviation | Cause | Status |
|---|---|---|---|
| Font "Generals" (templates `Title` 20, `MainButton` 15 …; 294 texts in 4 sizes) | 7 % too wide (capitals at 24 px: 390 instead of 364 px), up to 85 px per text, Σ 2,224 px | FreeType approximation "91 % compression"; the original lets GDI choose the width with `lfWidth = 0.40·height` (in reality approx. 87 %) | **fixed** (`GdiFontProvider`), self-check of the tool, `--font-audit` |
| Line height/ascent | 29 of 36 font sizes deviating by 1 px (e.g. Arial 16 px: 18 instead of 17) | FreeType cell metrics (hhea/OS/2, rounded) instead of `tmHeight`/`tmAscent` | **fixed** |
| Character advance | per font 1–19 characters (ASCII + Latin-1) with a different width, 326 texts affected | FreeType: rounded, unhinted advance; GDI: hinted integer advance from `GetTextExtentPoint32` | **fixed** |
| Letter `W`, `PixelOverlap` | – | special cases of `Store_GDI_Char`/`Get_Char_Spacing` (`W` one pixel to the right, overlap added to the block width) | **fixed** (`UiFontFaceMetrics.Overlap`) |
| Edge coverage | 256 instead of 16 levels | original: 4-bit alpha of the A4R4G4B4 texture | **fixed** (raster result only 0, 17 … 255) |
| Overlapping glyph columns | alpha blending instead of OR of the alpha bits | `Blit_Char` combines the first `PixelOverlap` columns with OR | **open**, visible only for characters with overhang |
| `Courier`, `FixedSys` (Language.ini: copyright, military text, debug) | replaced by Arial | FreeType can only do TrueType; the families are raster fonts | **fixed** by GDI (`Courier/12` and `/18` occur in layouts) |
| `Arial Unicode MS` (UnicodeFontName) | not installed on the measuring machine (Office) | GDI silently substitutes Arial; now detected through `GetTextFace`, counted, reported on stderr | **deliberate/counted**; all code points of the English CSF are in Arial except three C1 characters |
| U+0085 (267 texts, above all subtitles "…"), U+0093/0094 | invisible, width 0 ("all#we need support") | translators stored cp1252 codes in UTF-16; GDI has neither glyph nor width for them – the same in the original (`Get_Char_Spacing` = 0) | **deliberate** (no difference from the original, but visibly missing ellipses) |
| Windows version/Arial version | pixels of the edges and individual advances can deviate from the state of 2003 | GDI of this installation | **open**, not measurable without the original |
| Edge brightness (blending) | bright text on a dark background was bolder and more washed out: an edge pixel with 6/15 coverage became 170 instead of 102, 10/15 became 213 instead of 170 (the sample on `shell-options` before the change: 222, 213, 170, 231, 247, 203) | The original blends in the frame buffer in bytes (gamma space); Stride blends with an sRGB target view in linear space (`docs/CLIENT-UI-RENDERING.md` §3). The same coverage gives a brighter edge in linear space | **fixed** for bright text (`UiAlphaCurves.LightOnDark`: stored alpha = `decode(coverage)`, over black exactly the gamma-space value; after the change the edge pixels are 102, 153, 170, 187, 204, 238 – the steps of 17). An approximation over a non-black background; dark text unchanged. Not re-measured in the client window (swap chain) |
| `FONT` in Uniform mode | windows without a template scale with the 4:3 factor, the original leaves `FONT` unscaled (only templates grow with `adjustFontSize`) | layout mode of the shell (`UiScaleMode.Uniform`) | **deliberate** (everything grows evenly; at 1280x720 templates are 1.2 times instead of 1.42 times as large) |
| `DefaultWindowFont` (Times New Roman 14) | `UiWindowTree.FallbackFont` is Arial Unicode MS 12 | measured: **0 of 792 windows with text** have neither `FONT` nor a template, so without effect | **open**, without consequence |
| `LocalFontFile`, `UseHardWordWrap` | not applied | English: no `LocalFontFile`, hard wrap off | not checkable (only English present) |

### 4.2 Layout of the text block

| Element | Deviation | Cause | Status |
|---|---|---|---|
| Line wrap (static text, buttons) | a word was still put on the line at width = wrap width | greedy `>`; original `≥` (`Cursor.X + space + word ≥ width`) | **fixed** (`UiSentenceLayout`, `SentenceLayoutTests`) |
| Wrapped left-aligned line | without indent | `Build_Sentence_Not_Centered` lets the following line begin with the space (an indent one space wide) | **fixed** |
| Block width/centring | centre off by the overlap | `extent.X = maxX + Get_Extra_Overlap` | **fixed** |
| `WRAP_CENTERED` | lines broken and centred differently by one space | `Build_Sentence_Centered` counts the space twice and centres on the block width of the other construction | **fixed** (the height of the block stays that of the other construction, as in the original) |
| Radio buttons | were wrapped | `drawRadioButtonText` sets neither wrap nor centring | **fixed** |
| Buttons, check boxes, radio buttons | text cut off at the window edge ("Alternate Mouse Setu") | clipping to the window, which in the original only static text sets | **fixed** |
| List/combo text | offset 4, border color of the window | `TEXT_X_OFFSET` = 5, shadow fixed `(0, 0, 0, 255)` | **fixed** |
| Entry fields | text centred, offset 4, no scrolling | original: x + 5 + 2, y + 5, clipping, scrolling in half-width steps; read-only combo box: x + 10 | **fixed** (blinking of the cursor not reproduced) |
| Shortcut buttons (`SHORTCUT_BUTTON`) | – | in the original the status arises at run time in the code, not in the layouts; the shortcut bar is missing in the client | **open** |
| `&` hotkey marks (`HOTKEY_TEXT`) | – | only in `ControlBarPopupDescription.wnd`; 385 English texts contain `&` (e.g. "C&C"), there it is never removed – correct, because `ParseHotKey` applies only there | **open** (with the popup) |

### 4.3 Color, outline, shadow

| Element | Deviation | Cause | Status |
|---|---|---|---|
| Shadow | – | (+1, +1) in the border color before the text, transparent border color = no shadow – agrees with `W3DDisplayString::draw` | checked, unchanged |
| Text colors per state | – | `TEXTCOLOR` Enabled/Disabled/Hilite as in the `W3D…Draw` functions | checked, unchanged |

### 4.4 Strings (CSF/STR)

| Element | Deviation | Cause | Status |
|---|---|---|---|
| Placeholders | – | 148 English strings with printf conversions; `GameTextFormatter` checks type and count and throws instead of guessing; money, costs, building counters go through it | checked (`TextAndSettingsTests`) |
| Missing labels | – | `LocalizedText.Get` returns `??Label??` and counts (`MissingLabels`); 0 in the shell pass, in the tool only the intentional ones and `Static Text`, `Version` | checked |
| Unresolved layout texts | 27 texts in 80 layouts (`GUI:Monkey`, `GUI:MockupScreen`, `$$$` …) | placeholders of GUIEdit; they are in layouts that no screen shows, or are replaced by the program code | counted (`font-audit.txt`) |
| `Data/Patch.str` (7 lines) | is not read | the original reads `Generals.csf`/`Generals.str` and `map.str`, never `Patch.str` | checked |
| English only | other languages not checkable | only `Data/English/generals.csf` is shipped | open |

### 4.5 Texts that should not exist like this

| Element | Deviation | Cause | Status |
|---|---|---|---|
| Money display | "$$$" at the top left in the corner plus a number in Arial 10 at the scheme rectangle | `MoneyDisplay` was left with placeholder and 100x100 rectangle; the number was additionally drawn by hand | **fixed** (`$ 10000` in the window at the scheme rectangle, `HudTests.MoneyDisplay`) |
| Command icons | hotkey letter at the top left, cost number at the bottom right | addition of the client, the original prints nothing onto the icons | **fixed** (removed; description as a tooltip) |
| Tooltips | were missing completely (`UiWindowTree.Tooltip` was drawn by no code) | never implemented | **fixed/simplified**: `UiTooltipLayout`, `TooltipPainter`; the command button description follows `populateBuildTooltipLayout` (name, cost, description, "Insufficient funds", "Requires: …", purchased upgrade; `docs/GAME-HUD.md` §5a), but as a pointer tooltip instead of `ControlBarPopupDescription.wnd`; money and power windows describe themselves; object tooltips and science texts are missing |
| Tooltip timing | stayed visible as long as the pointer was in the window | original: the pointer has to stand still for `Delay` (`createStreamMessages`) | **fixed** (`WndTests.TooltipStateMachine`) |
| Options: entry fields | showed "Entry" | TEXT of the layout file, `OptionsMenuInit` sets values | **fixed** (empty) |
| Skirmish: armies, colors, teams | `FactionAmerica`, `Color 1`, `None` | fixed names | **fixed** (USA/China/GLA/Random, Gold/Red/Blue/Green/Orange/Cyan/Purple/Pink, Team:0..4; `ShellTests`) |
| Skirmish: "Computer", "(no map selected)" | fixed English words | no CSF label | **fixed**: the slots offer, like `SkirmishGameOptionsMenuInit`, `GUI:Open`, `GUI:Closed`, `GUI:EasyAI`, `GUI:MediumAI`, `GUI:HardAI`; name field `GUI:Player`; the map is preselected as in the original (`getPreferredMap`/`getDefaultMap`), a state "no map" exists only without any multiplayer map (then empty). **Addendum 5 October 2026:** instead of the three armies a slot now offers only `GUI:MediumAI` ("Medium Army") – the three started the same AI, and no verified difficulty effect applies in the skirmish (`docs/SIMULATION-CORE.md` §35); the counter `GameShell.SkirmishDifficultyIgnored` has been dropped |
| Skirmish: color entries without a color patch | text instead of a coloured area | combo boxes know no entry colors | **open** |
| Credits | fonts Arial 24 bold/16 bold/12 | fixed values | **fixed** (Language.ini + `adjustFontSize`, `ShellTests`); **open:** `COLUMN` pairs, scroll rate (original 2 px per frame, here 40 px/s) |
| Score screen | player names are the army names ("FactionAmerica") | names come from the session | **open** (outside this brief: player name from the setup) |
| Briefing/loading screen | still image instead of movie and progress texts | movies/Bink are missing | **open** (Phase 16, item 5) |
| Mission texts | fixed Arial sizes, centred lines, invented placement in `MissionPresentation.cs` | not through `Language.ini`/`InGameUI.ini` | **fixed**, section 4.6 |
| Cursor text (`drawCursorText`), captions above objects | do not exist | never implemented | **open** |
| Superweapon/timer display | fixed values in the code (position, Arial 10, blink phase) | `InGameUI.ini` was not typed | **fixed**, section 4.6 |
| Skirmish: map list | names cut off after two letters ("Al", "Ar") | names in the first of two columns (5 %/95 %) | **fixed** (second column) |
| Skirmish: map preview | diagonal circles across "No map selected" | `ButtonMapStartPosition0..7` not hidden in the layout, the original hides them until `positionStartSpots` | **fixed/simplified** (hidden; position of the start spots is missing in the catalog) |
| Skirmish: map selection | column headings, fields and start of the setup show through the list | `showSkirmishGameOptionsUnderlyingGUIElements` not implemented | **fixed** (`GAME-SHELL.md` §13.1) |
| Challenge menu | `GUI:BioNameEntry` … as biography, buttons without a picture | placeholders of the layout instead of persona texts and medals | **fixed** (§13.4); **open:** portrait, letter-by-letter entry |

### 4.6 Mission texts (`InGameUI`)

The original draws the texts of the mission scripts from the data of `InGameUI.ini` (positions, colors, times) and `Language.ini` (fonts; `InGameUI::init` replaces every font of the block by the
Language slot of the same name). The client used fixed Arial sizes, centred lines and texts of its own; now `InGameUiSettings` (`CnCNew.Ui/Text`, strictly typed, retail: 29 radius decal sub-blocks counted, keys
without a value run to the constructor defaults of `InGameUI.cpp` and are in `DefaultedKeys`) reads the file and `InGameUiStyle` (`CnCNew.Game/Data`) resolves fonts like all other texts (`LanguageFonts.Resolve`,
`adjustFontSize`). Details and sources: `docs/SCRIPT-ENGINE.md` §7a "Screen texts according to `InGameUI`".

| Element | Deviation (before) | Cause | Status |
|---|---|---|---|
| `DISPLAY_TEXT` | one centred title line, 5 s | no way to the message list | **fixed**: line list at the top left (`MessagePosition` 10, 22 in pixels, `MessageFont` Arial 10 bold adjusted, `MessageColor1/2` alternating, up to 6 lines), fading by the integer computation of the original (`MessageDelayMS / 30 / 1000` = 0; about 9.2 s) |
| `SHOW_MILITARY_CAPTION` | title and text glued together into one line ("KazakhstanEn route"), centred, Arial 18 | line break of the label without effect, fixed font | **fixed**: typewriter (22 frames of start delay `MilitaryCaptionDelayMS`, then one character every second frame, line change with renewed delay, at most 4 lines), first line title font (Courier New bold), further lines text font, block cursor in a rhythm of 10 frames, position (10, 340) scaled from 800x600 to the screen, fading out after the duration (`floor(frames · 0.1)` per step) |
| `DISPLAY_CINEMATIC_TEXT` | fixed Arial 16 bold in the lower bar | – | **fixed**: font of the script (`Name - Size:N [Bold]`) adjusted, centred, wrap width − 20, top edge at 0.9 of the height, white without shadow, `30 · seconds` frames; used by no campaign map, checked only synthetically |
| `DISPLAY_COUNTER` / `DISPLAY_COUNTDOWN_TIMER` | Arial 14 bold, list from the top below the bar | fixed values | **fixed**: stacked upwards from `NamedTimerCountdownPosition` (0.90 / 0.666 of the screen area), right-aligned in the right half, font/color `NamedTimerCountdown*`, at 0:00 ready font and blinking (500 ms = 15 frames) in the flash color |
| Superweapon list | constants in the code (`PositionX` …), Arial 10 from the code, blink phase reversed, "..." right-aligned | `InGameUI.ini` not typed | **fixed**: values and fonts from `InGameUiStyle`, flash color in odd periods (`m_superweaponUsedFlashColor` starts true), "..." left-aligned from x |
| `INGAME_POPUP_MESSAGE` | like a title line, gone after 5 s | never implemented as a window | **fixed/simplified**: `InGamePopupMessage.wnd` with OK button (`MissionPopupWindow`), Enter/Escape on release, pauses and is modal if the script requires it; text color that of the layout (254) instead of `PopupMessageColor` (255) |
| Speech/movie placeholders | Arial 14; no subtitle | fixed font; `doSpeechPlay` was disregarded | **fixed/deliberate**: subtitle `DIALOGEVENT:<Name>Subtitle` as military caption (8 s) if present, not empty and without a leading `*` (English 1 of 2,455); the placeholder ("(speech: Name)", "(movie placeholder: Name)") stays, marked, in the `MessageFont` of the data |
| Hint "(cinematic: the mission script has disabled input)" | invented text | debug aid | **removed** (the original shows nothing) |
| Messages of the game (command rejected, construction finished, attack …) | a "ticker" with invented English texts, drawn by no code | never connected to the message list | **fixed**: `HudMessageFeed` writes the labels of the original into the same list as `DISPLAY_TEXT` (`docs/GAME-HUD.md` §8 "Message list"); what has no line in the original is counted |
| Letterbox bars | one eighth of the screen height, below the texts | invented | **fixed**: `(height − 9/16 · width) / 2` at the top and bottom (800x600: 75, 1024x768: 96, 16:9 and wider: none), above the messages and below the cinematic text, faded in within one second, at the end only the upper one fades out (`W3DDisplay::renderLetterBox`, `W3DDisplay::draw`) |
| Typing click of the military caption | was missing | not wired | **fixed**: per typed character `MilitarySubtitlesTyping` to the sound of the client (`InGameUI::update`); counted in the mission report |
| Result banner ("MISSION ACCOMPLISHED") | fixed Arial 28 bold | no counterpart in the original (result screen/movie) | **deliberate** as an invented placeholder, fixed font, named as such in `MissionOutputCatalog`/docs |
## 5. Measured values (`UiRenderTool --font-audit`, 80 layouts, 1280x720 and 1920x1080, Uniform)

* 36 different resolved fonts (family, pixel height, bold, compression); 1,190 window text lines from 792 windows with text.
* GDI against FreeType: 326 lines with a different width (Σ 2,258 px); of which "Generals": 294 lines, 2,224 px, largest deviation 85 px (template with 24 point, 32 px).
* Line height: 29 of 36 fonts different.
* Advance (capitals A–Z, "Generals" 24 px): GDI 364, FreeType 91 % 390, FreeType 100 % 417.
* Glyph coverage of the English CSF (6,422 strings): 97 different code points (one of them the line break); all from U+0020 on are in Arial except for three C1 characters (U+0085 ×267, U+0093 ×5, U+0094 ×6), which have no glyph in the whole chain.
* Strings with `&`: 385; with printf conversion: 148; with line break: 772.
* Families: `FixedSys`, `Courier`, `Courier New`, `Times New Roman`, `Arial` present; `Arial Unicode MS` substituted (counted).
* Tool run (92 pictures + self-check): `commands=20997 sprites=44459 drawCalls=3735 batches=1368 missingImages=12 missingTextures=0 missingLabels=12 missingGlyphs=0
  substitutedGlyphs=12 customDraws=56 unbalancedClips=4` (the twelve missing labels/images and the twelve substituted glyphs are the intentional ones of the synthetic scenes plus
  `Static Text`/`Version`/`HardCodedWithControlBarSchemeIni` of the raw layouts). New in the briefing scenes: `WarSchoolBar.tga` is 512x512, the table says 1,024x1,024 (counted,
  source rectangles are scaled).

## 6. Golden values

The expected values prove reproducibility, not correctness.

* `tests/CnCNew.Game.Tests/baselines/screens.txt`: **48 of 54** values changed (all screens except `MatchLoading`): `MainMenu`, `MainMenu-SinglePlayer`, `QuitConfirm`, `Credits`,
  `ScoreScreen`, `SkirmishMapSelect`, `SkirmishSetup`, `SkirmishSetup-MapChosen`, six resolutions each. Causes: clipping rectangles of the button/check box texts (now the whole screen), wrap and
  indent, entry field/combo box texts, localised skirmish words, credits fonts. (The hash formation uses the synthetic `ShellTextMeasure`, so the values do not depend on GDI.)
* After that (Phase 16, menus): four of the 54 values (`Credits`, `SkirmishSetup`, `SkirmishMapSelect`, `SkirmishSetup-MapChosen`, six resolutions each, so 24) re-recorded, because the main menu is no longer drawn below these screens
  (`GAME-SHELL.md` §13.5).
* `tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256`: changed (`B2C97FCE…` → `CE98E458…`): the money window carries the text "$ 10000" at the scheme rectangle instead of "$$$" in the 100x100 field, the
  number drawn by hand is dropped.
* `UiRenderTool`: the pictures change as a whole (font); there are no expected values, only the two-run equality.
* After merging with the transition brief, rewrite `screens.txt` (`CNCNEW_UPDATE_BASELINES=1`) and check the line endings (CRLF).

## 7. What is not verified (a human has to look)

1. **Whether the texts now look like in the original.** Everything above is agreement with the reference code; the raster accuracy depends on the Arial version and on the hinting of this installation.
2. **The client window.** All pictures come from the offscreen tool and the `--smoke-test --capture` of the client, never from a real window with a swap chain (gamma behaviour and thus the
   effect of `UiAlphaCurves`, DPI scaling of the window, `UiRenderer` counters in operation).
3. **Tooltips:** look, placement, speed of the fade-in and the simplified command button description against the original popup.
4. **Uniform mode against original scale:** at 1280x720 and larger, texts are different in relation to the windows than in the original, because the shell scales evenly (section 4.1).
5. **Menus showing through** – **fixed** (`docs/GAME-SHELL.md` §13): the shell now draws only the topmost replacing screen (`Shell::push` hides the one below) and what lies above it; options, map selection
   and dialogs are overlays as in the original. A background layer was not necessary (the replacing layouts bring frame and ruler themselves). Before, the button texts of the main menu ("CHALLENGE",
   "BACK", "SKIRMISH" …) showed through the new page, most strongly in `flow-skirmish-setup-map-*.png`.
6. **`SinglePlayerMenu.wnd`** (New/Load/Back after "USA") – **fixed**: it is an heirloom of the first Generals that Zero Hour never opens; the original asks for the difficulty in the main menu (`MapBorder4`), the shell
   likewise (`GAME-SHELL.md` §13.2). The page with Times New Roman no longer exists.
7. **Tooltip of the command buttons:** prerequisites, lack of money and purchased upgrades are now in it (`docs/GAME-HUD.md` §5a); what remains to be looked at is whether the lines are readable in the pointer tooltip – the original has a window of its own for that.
8. **Mission texts (section 4.6):** all times, orders and numbers agree with `InGameUI.cpp`/`W3DDisplay.cpp` and are recorded in tests, but never compared with an original. A human has to look at: how the
   typewriter comes across in motion (rhythm, cursor, fading out), whether Courier New looks like in the original at this size, the position of messages, timers and superweapon list on the screens, the message window
   (button images, text wrap) and the removal of the hint when input is locked.
9. **Update per logic step instead of per frame:** the original fades messages and military caption in `InGameUI::update` once per drawn frame (so faster from 60 frames per second on); here once per
   logic step (30 per second). For the typewriter that makes no difference (it compares with the logic frame), for the fading it does.
10. **Letterbox** – **fixed** after `W3DDisplay::renderLetterBox`: bars `(height − 9/16 · width) / 2`, none at 16:9 and wider; they lie above the messages (y = 22 thus disappears at 4:3
    below the upper bar, as in the original) and below the cinematic text. Deviation: the fade counts logic steps (30 = 1000 ms) instead of wall clock, and the mouse pointer of the client lies
    above the bars (in the original below). To be looked at: a 4:3 window during an intro.
11. **Typing click** – **fixed**: per typed character `MilitarySubtitlesTyping` (`SoundEffects.ini`: `utext1a`, volume 15, `Type = ui player`); on `MD_USA01` 52 clicks requested and
    52 started. **Nobody has heard it**, nor whether 15 clicks per second over the music are pleasant.
12. **Command feedback** – **fixed**: it is in the same message list as `DISPLAY_TEXT` (`docs/GAME-HUD.md` §8). To be looked at: whether the lines at the top left are noticed when a
    building site is rejected.

## 8. Tests and tools

| Check | Place |
|---|---|
| Wrap/block width/centring (9 cases) and tooltip layout (position, edges, fade-in) | `tests/CnCNew.Ui.Tests/SentenceLayoutTests.cs` |
| Tooltip style from `Mouse.ini`, strict errors | `tests/CnCNew.Ui.Tests/TextAndSettingsTests.cs` (`MouseTooltipSettingsAreTypedFromTheGraph`) |
| Clipping and position of the button/check box texts, tooltip time and age | `tests/CnCNew.Ui.Tests/WndTests.cs` (`DrawListGolden`, `TooltipStateMachine`) |
| Money window, command button tooltip, no texts on the icons | `tests/CnCNew.Game.Tests/HudTests.cs` |
| Skirmish words, empty entry fields, pointer tooltip of the shell, credits fonts | `tests/CnCNew.Game.Tests/ShellTests.cs` |
| Screen stack (nothing of the main menu below replacing screens), map selection, challenge menu, Escape in the load list | `tests/CnCNew.Game.Tests/ShellTests.cs` (`ScreenStackDrawing`, `ChallengeMenuBio`, `LoadGameEscapeWithoutFocus`) |
| `KeyboardHome`, image/size replacement of a window | `tests/CnCNew.Ui.Tests/WndTests.cs` |
| `InGameUI.ini` typed (synthetic and retail file, Language slot against INI in size/bold) | `tests/CnCNew.Ui.Tests/TextAndSettingsTests.cs` (`InGameUiSettingsAreTypedFromTheGraph`, `RealInGameUi`) |
| Message list, typewriter frame by frame, fading out, timers, cinematic text, message window, scaling | `tests/CnCNew.Game.Tests/MissionTextTests.cs` (synthetic; with `--content-dir`: `MD_USA01`, `MD_CHI02`, message window in `GC_ChemGeneral`) |
| Messages of the game: labels, position, colors, fading, counting; rejected building site through the real click path | `tests/CnCNew.Game.Tests/HudTests.cs` (`MessageList`, `MessageListFromARefusedPlacement`), `MissionTextTests.cs` (`RealSharedMessageList`) |
| Letterbox (dimensions, fade, order), typing click (synthetic and `MD_USA01` with the real audio director) | `tests/CnCNew.Game.Tests/MissionTextTests.cs` (`LetterboxFollowsTheOriginal`, `TypingClick`, `RealTypingClick`) |
| Button description with prerequisites, lack of money, purchased upgrade; money and power windows | `tests/CnCNew.Game.Tests/HudTests.cs` (`CommandButtonRequirements`, `MoneyAndPowerTooltips`) |
| Tooltip of a disabled window | `tests/CnCNew.Ui.Tests/WndTests.cs` (`TooltipStateMachine`) |
| Picture of the command bar with message lines and "Requires" tooltip, letterbox above the messages (three sizes) | `tools/CnCNew.UiRenderTool` (`HudFeedbackScenes`: `hudfeedback-*.png`, `hudletterbox-*.png`) |
| Mission text picture (all texts of one screen, three sizes) | `tools/CnCNew.UiRenderTool` (`MissionTextScenes`) |
| GDI font (family really Arial, coverage steps of 17, cell metrics, Generals narrower) | `tools/CnCNew.UiRenderTool` (`FontAudit.SelfCheck` on every run) |
| Font comparison, glyph coverage, placeholder/hotkey counting | `tools/CnCNew.UiRenderTool -- --font-audit` |
| Alpha curve for bright text, glyph cache with curve | `tests/CnCNew.Ui.Tests/UiRenderingTests.cs` (`LightTextGetsTheGammaSpaceAlphaCurve`) |
