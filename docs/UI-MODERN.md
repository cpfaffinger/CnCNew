# UI-MODERN – the modern skin beside the original's

Stage 3, milestone S3-M5 (`docs/plan/STAGE-3-CHANGES.md`), Phase 38. This document is the design decision of the milestone and was
committed **before any code or picture of the skin existed** (section 1 to 9); the measurements are appended to it afterwards (section 10
on). A skin is a choice of the presentation, never a change of the game: it touches no simulation, no manifest, no rule profile and no
format of a match, a replay or a savegame.

## 1. What is fixed beforehand

- **The modern skin is the default since Phase 53** (D-21, 31 October 2026), the original stays selectable; a settings file that names a skin keeps it. The original's pictures stay as they are; every golden hash recorded before
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

**Replaced by section 17** after the owner's acceptance of 1 November 2026: a bar in proportion left a black rectangle at either side and read as a fixed width. What follows is the decision of Phase 38.

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

## 10. What was built (Phase 38, 18 October 2026)

Sections 1 to 9 are the decision as it was committed (`8464d5c`) before any code; this section and the following ones say what was built and where it departs from them.

| Commit | What |
|---|---|
| `8464d5c` | this document's sections 1 to 9 |
| `2ee9a2d` | the skin layer (`CnCNew.Ui.Skins`: `UiSkin`, `ModernLayout`, `UiReadability`), the shell, the HUD, the self-drawn screens through it, settings schema 2, "More options", the client's `--skin`, display mode and sound device, `UiRenderTool --skins` |
| `7e6458c`, `5ce44a2` | two defects of the analysis screen found by drawing it in both skins (section 12) |
| `7f32642` | both skins pinned, the scripted person, `scripts/check-skins.ps1` |
| `c50b725` | "More options": the label of the wide-screen view shortened (it wrapped at 800 x 600 in the original skin) |

**Where it departs from sections 1 to 9:**

- **The type scale** (section 4) was measured before it was pinned and changed once: the first scale (10, 11, 12, 15, 20) overflowed the original's windows 33 times in
  the readability check (a line of Segoe UI is about 1.19 times as high as one of Arial at the same size, and the windows were made for Arial: headings and titles
  in windows 24 and 36 units high, "Starting Cash" wrapped in its box). The built scale is **caption 10, body 10, control 11, heading 12, title 17** points at 600
  lines. The smallest sizes of R3 (b) are the caption's and did not change: 13, 16 and 24 pixels at 800 x 600, 1280 x 720 and 1920 x 1080.
- **The way to "More options"** (section 7): the original's hidden `ButtonKeyboardOptions` has a font (Times New Roman 14) and a place (beyond the right edge) of its
  own; the button is instead a copy of the options' Defaults button made when the screen opens, at the free place right of the title (476 to 632, 21 to 53 of the
  layout), as the replay list's "Analysis" button of Phase 36 is a copy of a shipped button. It is still the one window this project adds to the original's options.
- **The rows of "More options"** (section 7): interface skin, display mode, "wide-screen view" (the 3D view at a window wider than 4:3), text size, sound device and the
  hotkey list. The UI scaling of the original skin (D-21's integer scaling) is **not** offered: it is the owner's open decision, and `IntegerMultiple` is not integer
  scaling in the HUD (it maps to uniform). Schema 1's three aspect values (expand, pillarbox, stretch) were never applied by any client; schema 2 has the two the
  camera draws: the original's horizontal field of view (the default, what the client always drew) and the vertical field of view of 4:3 (more to the sides).
  A file of schema 1 migrates to the default.
- **Check boxes** (not in section 2): the modern derivation lets a check box's window reach to its right neighbour (or its parent's edge), 4 units short; the original
  draws a longer label unclipped beyond the window, and with Segoe UI more labels are longer than their box.
- **Drop shadows** (section 5): the modern skin draws none; text over pictures keeps the original's only where the art is kept (the command bar).
- **Tooltips and the message list** kept the original's style (`Mouse.ini`, `InGameUI.ini`) in both skins until Phase 39; section 15 has what the modern skin draws now.

## 11. Measured (thresholds of section 9)

| Threshold | Result |
|---|---|
| **R1** the original unchanged | **met, with one named exception:** every hash recorded before the phase passes - the shell 54, the replay list 54, the network screens 336, the network HUD 138, the observer 144 at 30, 60 and 120 Hz, the HUD draw list - except 42 of the 54 analysis hashes, re-recorded by the two fixes of section 12 (`7e6458c`: 42, `5ce44a2`: 24 of those 42). The update run that wrote the modern files rewrote every original file byte-identically. A test (`SkinTests`, R1) checks that the original skin's layout of each of the 23 shell layouts is the shipped object itself and that the menus lay out uniformly as before |
| **R2** both skins pinned | **met:** 780 hashes of the modern skin beside the original's (`screens-modern.txt` 54, `replay-screens-modern.txt` 54, `analysis-screens-modern.txt` 54, `net-screens-modern.txt` 336, `net-hud-modern.txt` 138, `observer-screens-modern.txt` 144 = 48 at each of 30, 60, 120 Hz) and 36 in `skin-screens.txt` (the options, "More options" and the HUD of a match, in both skins, six resolutions); the modern files hold at 30 and 120 Hz like the original's |
| **R3 (a)** no label wider than its window (modern) | **met:** 0 in 45 pictures (15 screens at 800 x 600, 1280 x 720, 1920 x 1080), measured with the GDI font of the renderer (`UiRenderTool --skins`, `skin-audit.txt`). The original skin, the comparison: 3 (the options' "Alternate Mouse Setup" in every size) |
| **R3 (b)** smallest text (modern) | **met:** 13 / 16 / 24 px at 800 x 600 / 1280 x 720 / 1920 x 1080. The original: 11 / 12 / 19 px |
| **R3 (c)** contrast | **met** (`Ui.Tests`, `SkinTests`): text 7.35:1 to 16.31:1 on every surface, muted 6.35 to 8.80, accent 7.17 to 9.94, good and bad at least 5.23 on canvas, panel and control, disabled 3.42 and 4.21 |
| **R4** a scripted person | **met:** `scripts/shell/skin-person.txt` (51 steps: options > More options > Modern > Accept > Single Player > Skirmish > a match > the score screen > the analysis, two pages > the replay list > the playback to the recorded end), without a window in `Game.Tests` (`SkinTests`) and in the window client (`scripts/check-skins.ps1`, step `skins`); the settings keep `"skin": "modern"` and the next start is in the modern skin |
| **R5** no change of the game | **met:** both manifests unchanged (tier 4 at the end of the phase, section 14) |

**Reported, not a threshold:** 21 labels of the modern skin are **taller** than their window - the seven column headings of the score screen ("Units Created" and
the like) wrap to two lines of the caption size in windows 32 units high: 34 / 42 / 64 px in 32 / 39 / 58, the lines reach 1 to 3 px above and below the window, into
empty space. The original: 1 (this project's own "More options" label at 800 x 600, shortened afterwards).

## 12. Found on the way

- **The analysis screen showed no header line** since Phase 36 in either skin: `ScoreScreen.wnd` hides `ChallengeWinLossText`, and the screen set its text without
  showing it, so no page title, map or winner was drawn (`7e6458c`). The tests read the header's text, not the picture; the window check of Phase 36 waited for the
  text too.
- **A chart title ran into its top axis label** ("Money" over "10 000") in both skins (`5ce44a2`).
- **The run's `--skin` was dropped when the skirmish setup saved its last map:** the shell cleared its override whenever the saved skin differed from the drawn one;
  it now does so only when the saved skin changes (`2ee9a2d`, tested in `SkinTests`).
- **The client applied a pending resolution in every frame** after the options were accepted (`ApplyChanges` each frame since the options screen existed); it is now
  taken once (`2ee9a2d`).

## 13. Not done, not verified

- **Everything a human judges:** whether the modern skin is easier to read and to use than the original (the acceptance of S3-M5), whether it looks good, whether the
  palette and the type fit the game, whether the proportional command bar plays better than the stretched one, whether the hotkey list is useful. The pictures were
  looked at by the agent while the skin was built (PNG of `UiRenderTool --skins`), which is not an acceptance.
- Geometry of its own for single screens (section 2, way (a)); the modern skin's screens keep the original's proportions.
- The campaign screens and the mission HUD are not walked by the scripted person (pinned in both skins since Phase 39, section 15).
- Integer scaling of the original skin (D-21), offered nowhere.
- Display mode and sound device are applied by the client; a change of either was not observed by a person (the full screen, a second sound device).

## 14. The completion of Phase 38 (R5)

- **Tier 4 with 56 of 56 steps ok at `89490f5`** (7,282 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; the
  reference manifest 50 of 50 runs identical and the input manifest identical - the skin touches no simulation; the rate-equivalence suite; the long run; the client
  checks; every network step; `replay-browser`; the new `skins`; `patch-layer`; the staged scenes). The gate (16 of 16) ran before the first push.
- Campaign at 60 Hz: reachability clean 24/24 and 24/24; audit 24 of 24 identical on a second run and after save/restore, end probes 13/24 and 15/24; objective driver
  victory 24/24 and defeat 24/24 - as before the phase.
- `scripts/perf-profile.ps1` with the binaries of `d718187` and of `89490f5`: every one of the ten runs with the same final hash on both sides, means within -5.9 % and
  +0.3 % (the simulation is unchanged).
- Clean room of `89490f5` (`C:\cr38`): every step ok - 0 warnings, the known export, input manifest identical, the quick set 9 of 9, the packages free of original data.

## 15. Phase 39: the open ends of the modern skin

- **Tooltips:** the modern skin draws the pointer tooltip (`Mouse::drawTooltip`'s place - the window tree's `TOOLTIPTEXT` and the command bar's descriptions alike) as a
  panel of its palette (panel, border, text, accent for the highlighted line, shadow) with its text in the body step; the timing of `Mouse.ini` stays
  (`TooltipPainter.Build(..., skin)`, used by the shell and the HUD).
- **The message list, the superweapon clocks and the other in-game texts:** `InGameUiStyle.WithSkin` gives every role of `InGameUI.ini` the skin's family at the type
  step its original size falls in (`UiSkinStyle.StepOf`); the colours - the message colours, a player's house colour on a clock - stay the original's. The original skin
  returns the style unchanged.
- **Pinned:** `SkinTests` adds the HUD of a match with a line in the message list and the money display's tooltip, in both skins and six resolutions (`skin-screens.txt`
  now 48 hashes; the test checks that the line is shown and that the tooltip is a panel of the modern palette only in the modern skin). `CampaignPinTests` (new) pins the
  campaign's way - the difficulty question of the main menu, the briefing of `MD_USA01` and that mission's HUD after its opening cinematic - in six resolutions:
  `campaign-screens.txt` and `campaign-screens-modern.txt`, 30 hashes each (the two shell screens once, the mission's HUD per rate, because it is a picture of a real
  mission). The opening cinematic of `MD_USA01` runs longer than 50 s; the test gives it up to 15,000 drawn frames.
- **Not done:** the scripted person still does not walk the campaign; nobody has looked at the tooltip or the message list in the modern skin.

## 16. The owner's findings: panels, gadgets, columns (2 November 2026)

The first acceptance by a person (`CnCNew-acceptance-report-2026-11-01`) said of the sub-menus: "no transparency", "the columns and cells are not properly designed", "dropdown menu and sliders are strange". What was decided:

- **The battle behind the menus shows through.** A full-screen backdrop is no longer the opaque canvas but `ModernLayout.Scrim` (the canvas colour at alpha 96), and a group is a translucent panel (`Panel`, alpha 214) with a soft edge of three outlines. Without a shell map the same windows lie on the black of the cleared picture, so the pictures of the tests look darker than the game. The score screen has the shell map behind it in the modern skin too (`UpdateShellMap`); in the original skin it stays the blank window of the original. A dialog over another screen (`ModernScreens.IsDialog`) is nearly opaque (alpha 245), because what lies behind it is text. Blending is linear: a little of a light colour goes a long way, which is why every second row is `ModernLayout.Row` (60, 74, 94 at alpha 40) and not a white of low alpha.
- **Every gadget of the skin is drawn by a function of its own** (`UiDrawBuilder.Flat.cs`, for windows whose definition carries a `WndFlatRole`; the windows of the original skin never reach it): a button with an accent bar at its left edge under the pointer, a check box with a real tick, a combo box as a field with a chevron and its text in the middle of the field, a drop-down with rows that have air, a row under the pointer, a thin position bar, and the place above the box when the screen ends below it; a slider as a thin track with the part up to the value in the accent and an 8 x 20 unit thumb on it; a list with rows with air, every second row lighter, a rule between columns and a selected row with the accent at its left edge; fields that touch in the layout stand as cells with a gap, because a field draws a unit inside its window.
- **`UiGadgetGeometry` knows the flat gadgets**, so what is drawn is what answers: row height, drop-down place and the slider's thumb are the same numbers for the pointer and the picture.
- **A screen stays together.** In a wide window the anchored scale sent every group to its nearest edge, which spread the buttons of the score screen, put the replay list's "Analysis" and the options' "More options" outside their panels and spaced the five medals of solo play 259, 495, 500 and 258 pixels apart (SHELL-06). Now every group of a screen but the main menu's column stays in the middle (`WndWindow.GroupCentered`, `UiScaler.Centered`); the main menu keeps its column at the edge and only its row of medals in the middle.
- **Geometry of its own for single screens** (`ModernScreens`, only numbers of this project in the layout's 800 x 600): the skirmish setup's headings stand over their columns on one line, the score screen has one set of columns for headings, pictures and rows with every second row filled, its "Tactical Analysis" stands over its list. This is way (a) of section 2 for the few screens that need it: the windows keep their names, parents and behaviour.
- **What was chrome in one picture:** the general's window (`GeneralsExpPoints.wnd`) was one picture with its whole frame; it is a panel now, and a button with a label is chrome whatever its draw data say (it was taken for a picture and lost its frame). The world map of the Generals' Challenge is not chrome: the medals stand on the countries. It moves from the backdrop into the screen's frame (`WndFlatRole.Picture`), cut to it, slightly translucent, with the title on a band of the panel colour.
- **Colours of the game as text:** a player's colour stays what it is on the map, but as text on a dark panel it is lifted towards white until it has 4.5:1 (`ModernLayout.Readable`): the score table, the lines of the analysis.
- **The analysis** (REPLAY-01): the pages stand as a row of tabs with the current one in the accent, every chart on a card of its own with a heavier base line, ticks on the time axis, the last value of each line marked, the legend once under the cards. The original skin's page is unchanged.

**What the picture tests do not show:** a picture of `UiRenderTool --skins` has no battle behind it. The translucency was judged in the window client over the shell map (1280 x 720, PrintWindow), at the skirmish setup, the options, solo play, the direct connect screen and the lobby. `UiRenderTool` flattens the alpha of its PNG now: a picture viewer laid translucent fills over white, which is why the panels of Phase 38's pictures looked light grey and those of the game did not.

## 17. The command bar: a band across the whole width

The owner (SKIN-02, HUD-06): "it does not properly stretch at resolution, it seems to be a fixed width and has left and right a black rectangle". **`ModernControlBar` replaces section 3.** The bar fills the width of the window at every aspect ratio and nothing of it is stretched art:

- **The band** runs from a line at 438 of 600 to the bottom edge, in the skin's canvas colour, with one line and a band of the panel colour at its top. The scheme's art is not drawn at all.
- **Two sides at one scale.** Everything is placed at the window's uniform scale (`Scale`). The radar with its four small buttons stands at the left edge, the command buttons, the portrait and the production queue at the right edge. The windows of `ControlBar.wnd` stay the windows the HUD drives: `Place` gives each its screen rectangle (`SetScreenRect`), so clicks, tests and scripts find them by name.
- **What the width adds goes to the selection plate** between the two sides: for one object its name and health, for several the count and a picture of every kind with its number. At 4:3 and narrower the two sides meet and there is no plate; at 21:9 the plate is wider and nothing else changes. Below 80 units the plate is left out.
- **The strip above** holds the under-attack lamp, the money, the power bar across all the rest (a bar is a value: it may be as long as there is room), the numbers used and produced at its end, and the general's button.
- **The general's button** is a cell of the skin with the rank in words and, as a thin bar along its bottom edge, the way to the next rank; it has the accent when points wait. This replaces the experience bar of the art and the scheme's button picture, and the portrait moves 34 units towards the edge into the room that leaves.
- **Every place of the command grid has its well**, so the fourteen cells read as a grid when a selection fills only some of them.
- The pictures of the bar stay the game's: cameos, portraits, the side's logo, the radar and the four small buttons.

Measured by picture at 800 x 600, 1280 x 720, 1920 x 1080 (`UiRenderTool --skins`: `hud`, `hud-building`, `hud-units`). `keepBandAspect` of Phase 38 is gone.

## 18. Defects of both skins found on the way

| Finding | Cause | What was done |
|---|---|---|
| SKIN-03 "gradients in select boxes" | A combo box drew entry 0 of its edit box over the whole box; entry 0 is the left end of a text entry | Three pieces, like `W3DGadgetTextEntryImageDraw` (original skin only: the modern skin has no pictures there) |
| SKIN-03 main buttons stay in the options | `OptionsMenu` lay over the main menu, as in the original (`getOptionsLayout`, `hide(FALSE)`) | The options hide what lies below, in both skins. **A deviation from the original in the original skin**, at the owner's word: the panel cut the buttons in half |
| SHELL-04 sliders offset | The thumb hung 10 units below the window's top edge (`HORIZONTAL_SLIDER_THUMB_POSITION`), which in the original's image slider no one sees, because the boxes are the picture; the flat slider showed it under its bar | The flat thumb stands on its track; the original's geometry is unchanged |
| HUD-02 tooltip rebuilds | Every move of a pixel restarted the delay | A tooltip that is up stays at its place until another window is under the pointer; before it is up, a move still restarts the delay. The text can change under it (a medal of the skirmish setup's honours) |
| HUD-07 radar outline lags | The frame was part of the radar's picture, composed four times a second | The picture holds terrain, fog and dots; the frame is lines of every drawn frame (`AddRadarOutline`, and the viewer's own frame in the spectator HUD) |
| INPUT-05 box during placement | The HUD drew a box for any drag | Only the plain pointer draws one |
| SHELL-03 no grey | The cameo was laid over the disabled button in full colour | `Restricted` draws grey, `CantAfford` dark (144), `NotReady` in colour under its clock, as `W3DGadgetPushButtonImageDraw`. `UiImageDraw.Grayscale`: the renderer makes a grey copy of the page on the CPU, since its pages are block-compressed and it has no second shader. The general's window uses it too |
| HUD-09 no text entry | The client never turned Stride's text input on: a real keyboard gave keys and no characters, while a script types characters directly, which is why no check ever saw it | `FollowTextFocus`: on while a text field or the chat line has the focus, and on anew after a press, because Stride's mouse takes the focus back with every press. Checked with characters posted to the hidden edit control in the window client; **no real keyboard could be used on this machine** (the session has no input desktop) |
| MISS-09 briefing history | Captions are sentences and the list did not wrap them | Wrapped at the list's width as `GadgetListBoxAddEntryText` does |
| SHELL-03 no tactical analysis | The panel's list was never filled | `TacticalAdvice` (docs/MATCH-ANALYSIS.md) |
