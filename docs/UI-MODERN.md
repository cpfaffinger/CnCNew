# UI-MODERN – the modern skin beside the original's

Stage 3, milestone S3-M5 (`docs/plan/STAGE-3-CHANGES.md`), Phase 38. This document is the design decision of the milestone and was
committed **before any code or picture of the skin existed** (section 1 to 9); the measurements are appended to it afterwards (section 10
on). A skin is a choice of the presentation, never a change of the game: it touches no simulation, no manifest, no rule profile and no
format of a match, a replay or a savegame.

## 1. What is fixed beforehand

- **The original skin stays the default** until the owner decides (D-21). Its pictures stay as they are; every golden hash recorded before
  this phase (shell 54, replay list 54, analysis 54, network screens 336, network HUD 138, observer 144) stays valid unchanged.
- **One contract, one renderer.** Both skins produce `UiDrawList`s (fill, border, image, text, line, clip, custom) and are drawn by the one
  `UiRenderer` with the GDI font path (`docs/CLIENT-UI-RENDERING.md`). There is no second renderer.
- **Nothing of the game in the repository.** The modern skin carries no picture, no layout file and no text of the original. What it uses of
  the game - the window structure of a layout, a picture, a text - it loads and derives at run time from `game/content`, like everything
  else. A layout the skin needs that the original does not have is built at run time from a shipped one (section 7), never written down.
- **Selectable:** `--skin original|modern` on the client, the setting `display.skin`, and a choice in the options (section 7).

## 2. The question of geometry: native layouts or integer scaling

The original's layouts are 800 x 600 and the original stretches both axes to the display (`parseScreenRect`). The port draws the menus
uniformly with borders and the HUD anchored (`docs/UI-VANILLA-FIT.md`). Three ways were weighed:

| Way | What it gives | What it costs | Verdict |
|---|---|---|---|
| (a) Hand-made native layouts, one per screen | free geometry for every resolution | about 1,700 windows on the 23 layouts the shell drives (64 on the main menu, 73 on the skirmish setup, 113 on the score screen, 91 on the options), each placed again for wide, tall and 4:3 windows; a second copy of every screen's structure to keep in step with the code that drives it | rejected as the means of this skin; named as the way for a later redesign of single screens |
| (b) Integer scaling of the 800 x 600 layouts | crisp art (each texel a whole number of pixels) | at 1920 x 1080 the factor is 1 (1080 / 600 = 1.8 rounds down): an 800 x 600 island with 1,120 x 480 pixels of border; at 2560 x 1080 the same; text no larger than at 800 x 600 | rejected for the modern skin; it remains the option D-21 names for the **original** skin (`Display.UiScale = IntegerMultiple`), which this phase does not decide |
| (c) **Derived native layouts** | the original's window tree - names, order, behaviour, callbacks - derived at run time, every rectangle computed in the window's own pixels; nothing is a stretched or a scaled-down bitmap, because the chrome is drawn from rectangles | one derivation rule for every layout instead of 23 layouts; geometry follows the original's proportions | **chosen** |

**The rule of (c), exactly:**

1. The layout is read from the game at run time and derived per window (`UiSkin.Derive`); the screen code finds the same windows by the
   same names, so every screen, every script of `ShellScript` and every test that clicks a window works in both skins.
2. Placement is the anchored uniform scale (`UiScaleMode.Anchor`): a window that covers the whole creation area is a canvas stretched to
   the window (no letterbox, no pillarbox - the modern backdrop is a flat colour, which stretches without distortion); every other top-level
   group is placed at one scale (the smaller of width / 800 and height / 600) and stuck to the edge or the centre nearest to it, so a dialog
   keeps its inner proportions in a wide, a 4:3 and a tall window alike.
3. A layout whose windows are pictures keeps them as pictures at their uniform size (section 6).

## 3. The command bar at 16:9

The original stretches the command bar to the width of the screen (icons 1.33 times wider at 16:9). **The modern skin keeps its
proportions:** the bar is placed at the uniform scale by the height of the window, centred at the bottom, and the band to its left and
right is filled with the skin's panel colour from the bar's top line to the bottom edge, so the world view ends at the same line across the
whole width and no world shows beside the bar. The bar's art (the scheme images of `ControlBarScheme.ini`), its cameos and its portrait
stay the original's and are not stretched. Integer scaling is not used. The original skin keeps the stretched bar.

## 4. Typography

- **Family:** `Segoe UI`, the user interface family of Windows since Vista, rasterised through the same GDI path as the original's Arial
  (`GdiFontProvider`); nothing is shipped, and the fallback chain of the renderer is unchanged (a missing family is reported, never silent).
  The original's `Generals` face (Arial at 87 % of its width) and Times New Roman are not used by the modern skin.
- **Type scale** (point sizes at the 600 lines of the creation height, scaled with the window's uniform scale; the renderer turns points
  into pixels at 96 DPI):

  | Step | Points at 600 lines | Pixels at 800 x 600 | at 1280 x 720 | at 1920 x 1080 | Used for |
  |---|---|---|---|---|---|
  | Caption | 10 | 13 | 16 | 24 | the original's sizes up to 8 |
  | Body | 11 | 15 | 17 | 25 | sizes 9 and 10 |
  | Control | 12 | 16 | 19 | 28 | sizes 11 and 12 |
  | Heading | 15, bold | 20 | 24 | 36 | sizes 13 to 16 |
  | Title | 20, bold | 27 | 32 | 48 | sizes 17 and more |

  A window's size is taken from its header template (`HeaderTemplate.ini`) when it has one, otherwise from its `FONT`; a bold original
  stays bold. The setting `accessibility.uiTextScalePercent` (100, 125, 150) multiplies every size in both skins (at 100 % nothing changes).
- **Measured with the font the renderer draws:** text is centred and wrapped from the widths of the font the renderer rasterises
  (`UiRenderer.TextMeasure`), so a centred label sits where the renderer draws it. The headless tests measure with the synthetic measure of
  the shell, as they always have; the readability check (section 9, R3) measures with the GDI font.

## 5. Colours and contrast

One palette, dark and cool, with one warm accent (the original's yellow, carried over as an accent only):

| Token | Value (R, G, B) | Used for |
|---|---|---|
| Canvas | 15, 20, 27 | full-screen backdrops, the band beside the command bar |
| Panel | 24, 32, 42 | dialogs, panels, list boxes, the self-drawn plates |
| Control | 36, 48, 65 | buttons, entry fields, combo boxes, slider tracks |
| Hover | 47, 64, 88 | a control under the pointer |
| Selected | 45, 79, 124 | a pressed or selected control, a selected row |
| Border | 60, 74, 94 | the one-pixel outline of panels and controls |
| Accent | 242, 179, 61 | the outline of a hilited control, a checked box, a slider's thumb, a heading of a self-drawn page |
| Text | 238, 241, 245 | all text |
| Muted | 169, 180, 194 | secondary text (axis labels, captions, hints) |
| Disabled | 118, 130, 146 | the text of a disabled control |
| Good / Bad | 126, 220, 138 / 255, 122, 102 | quality and warnings of the self-drawn screens |

Contrast ratios by WCAG 2 relative luminance, computed from these values before any picture was made: Text on every surface 7.35:1 (on
Selected) to 16.31:1 (on Canvas); Muted on Canvas, Panel and Control 6.35:1 to 8.80:1; Accent on Canvas, Panel and Control 7.17:1 to
9.94:1; Disabled on Panel and Control 3.42:1 and 4.21:1. Player colours (the team colours of the game) are drawn as the game sets them.
The text shadow of the original (a drop colour at +1, +1) is dropped in the modern skin except over pictures (cameos, the world).

## 6. What stays the original's

Kept as the game draws them, loaded at run time: cameos and portraits, map previews, the faction and side logos, the score screen's
category pictures, the art of the command bar (`ControlBarScheme.ini`), the radar and the minimap, the cursors, every sound and the
timings of the window transitions, and **every text** (the game's string table and this project's own English lines). Drawn by the skin
instead: full-screen backdrops (an image that covers the whole creation area becomes the Canvas colour), and the chrome of every gadget -
push buttons, check boxes, radio buttons, sliders, list boxes and their scroll bars, combo boxes and their drop-downs, entry fields,
progress bars, tab controls - as flat rectangles of the palette by state (enabled, hilited, pressed or selected, disabled). An image of a
plain window smaller than the screen (a logo, a cameo, a portrait) is a picture and stays.

The screens the game draws itself and the original has no layout for - the analysis pages, the observer HUD and its ticker, the network
indicators of a match, the superweapon clocks - draw through a **style** (palette and type scale): the original skin's style is exactly the
colours and fonts these screens use today (their pictures do not change), the modern skin's is the palette and type scale above.

## 7. The options screen and the way to the skin

The original's `OptionsMenu.wnd` reaches resolution, volumes, scroll speed and the mouse setup. A second options screen reaches what it does
not: **interface skin** (original, modern), **display mode** (windowed, borderless, full screen), **aspect** of the 3D view (wider field of
view, pillarbox, stretched), **UI scaling** of the original skin (uniform, stretched, integer - D-21's option, stored only), **text scale**
(100, 125, 150 %), **audio device** (the devices the client finds, or the system's default), and a **hotkey view** (every command of
`CommandMap.ini` and its key, read only). It is built at run time from the shipped `OptionsMenu.wnd` - its parent, backdrop, Accept and
Back - with rows made from its own label, combo box and list box windows under names, places and callbacks of this project; no line of a
layout is written down. It is reached from the original options through the button the original's layout carries and hides
(`ButtonKeyboardOptions`, which opened the keyboard options of an earlier game): shown at a free place with the words "More options". That
is the one change of a picture of the original skin - the options menu, which no golden hash pinned before this phase and which is pinned in
both skins from now on. A changed skin takes effect when Accept is pressed: the shell builds its screens again in the chosen skin.

## 8. What a skin is in code

- `CnCNew.Ui.Skins.UiSkin` (Stride-free): its name, the scale options it lays out with, `Derive(WndLayout)` (the original skin returns the
  layout unchanged - the same object), and its `UiSkinStyle` (palette, font family, type scale) for the self-drawn screens.
- `GameShell.Skin`, `MatchController`, the spectator HUD, the network HUD and the analysis screen take the skin; the original skin is the
  default everywhere, so every caller that names none draws as before.
- The setting `display.skin` (settings schema 2, with a migration from 1 that writes `original`).

## 9. How it is checked (thresholds committed before the first measurement)

By machine, each a test or a check of a tier of `scripts/test.ps1`:

- **R1 - the original is unchanged:** every golden hash recorded before this phase passes unchanged at its rate; a test checks that the
  original skin's derivation returns the shipped layout itself.
- **R2 - both skins pinned:** every screen pinned for the original is pinned for the modern skin in the same six resolutions (800 x 600,
  1024 x 768, 1280 x 720, 1920 x 1080, 2560 x 1080, 640 x 960), the observer per rate; the new options screen in both skins.
- **R3 - readability of the modern skin, measured with the GDI font of the renderer** on the shell's screens at 800 x 600, 1280 x 720 and
  1920 x 1080: (a) no line of a label wider than the window it belongs to - 0 overflows; (b) the smallest text at least 13 pixels high at
  800 x 600, 16 at 1280 x 720 and 24 at 1920 x 1080; (c) every text colour of the palette against the surface it is drawn on at least 4.5:1
  (Disabled at least 3:1). The same numbers are reported for the original skin as the comparison, without a threshold.
- **R4 - a scripted person:** without a developer argument, a `ShellScript` starts in the original skin with a fresh settings directory,
  opens the options and "More options", chooses the modern skin, accepts, and walks main menu, skirmish setup, a short match, the score
  screen, the analysis and the replay list with its playback - headless in `Game.Tests` and in the window client.
- **R5 - no change of the game:** both manifests identical; tier 4.

**Only a human can judge** - and nothing here counts it as passed: whether the modern skin is easier to read and to use than the original
(the acceptance of S3-M5), whether it looks good, whether the colours and the type fit the game, whether the proportional command bar plays
better than the stretched one.
