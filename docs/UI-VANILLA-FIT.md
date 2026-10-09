# UI against the unmodified layouts of the Steam master

As of 9 October 2026. Until this day all real-data checks ran against a runtime content that had been imported from a
**modified** installation (ControlBarPro including the 1080p fix, HD command bar, HD decals, extended
LAN menu, custom maps). The only supported master is now the fresh English Steam installation
(runtime content `master-import-a`, 25,608 files). This document records what in the UI code was tailored to the mod,
what the original does, what was changed and what is open.

**What this assessment is and what it is not:** still images (PNG) were compared with what the source code of the
original engine describes (`references/ea-generals-zero-hour/GeneralsMD/Code/...`). No image was placed next to an image of the
running original, motion was not seen, and none of this is an acceptance by the project owner.

## 1. What the difference of the data is

| | Mod content | Steam master |
|---|---|---|
| `Window/ControlBar.wnd` | 105 windows, all `CREATIONRESOLUTION: 1920 1080`; `ControlBarParent` 0,740–1920,1080; the scheme-controlled windows (options, worker, communicator, general's button, toggle, money, power) as placeholders 0,0–100,100; own images `ControlBarPro*` | 98 windows, all `800 600`; `ControlBarParent` 0,416–**799,599**; the scheme-controlled windows without an image at provisional places |
| `Data/INI/ControlBarScheme.ini` | 14 schemes, `ScreenCreationRes X:1920 Y:1080` | 14 schemes, `ScreenCreationRes X:800 Y:600` |
| `Window/Menus/QuitMessageBox.wnd`, `MessageBox.wnd` | 1920×1080; Yes already on the place of Ok, No on that of Cancel; templates `LabelSmall`/`Button`; logo hidden | 800×600; three button places (Ok, No, Cancel), Yes parked one row higher; templates `LabelRegular`/`MainButton`; logo visible |
| `Window/Menus/QuitMenu.wnd` | 1920×1080, background image `ControlBarProQuitMenuWindow` | 800×600, no background image, logo at the top |
| `Window/Menus/MainMenu.wnd` | from `WindowZH.big` (208,561 bytes): the mod installation had replaced `PatchWindow.big` | from `PatchWindow.big` (212,069 bytes): additional button `ButtonCustomMission` between Skirmish and Back |
| `Data/English/HeaderTemplate.ini` | 19 templates (four `ControlBarProScrollListBox*`) | 15 templates |
| `Data/INI/InGameUI.ini` | `MessagePosition = X:10 Y:22` | `MessagePosition = X:10 Y:10` (only deviating line) |
| Full-screen windows | – | 14 layouts write the full screen as 0,0–**799,599**, 19 as 0,0–800,600 |

The remaining 36 deviating layout files differ in such a way that the drawn scenes stay pixel-identical
(see section 5: 112 of 162 images of the tool scenes were already the same between mod and master before any change).

## 2. What the original does (with source)

- **Layout → screen:** `parseScreenRect` (`GameWindowManagerScript.cpp`) reads `UPPERLEFT`, `BOTTOMRIGHT` and
  `CREATIONRESOLUTION` of every window and scales both axes independently with `Display / CreationResolution`
  (`xScale`, `yScale`). The creation resolution is in the file per window; nothing is assumed. At 16:9 the
  command bar of the original is thereby as wide as the screen and horizontally stretched 1.33 times.
- **Scheme → screen:** `ControlBarSchemeManager::setControlBarSchemeByPlayer` sets
  `m_multiplyer = Display / m_ScreenCreationRes`; `ControlBarScheme::drawBackground`/`drawForeground` draw every
  `ImagePart` at `Position * multi`; `ControlBarScheme::init` computes `resMultiplier` the same way and with it sets position and
  size of `PopupCommunicator`, `ButtonIdleWorker`, `ButtonOptions`, `ButtonPlaceBeacon`, `MoneyDisplay`, `PowerWindow`,
  `ButtonGeneral`, `ButtonLarge` and `WinUAttack` (`winSetPosition`/`winSetSize`) as well as their state images
  (`GadgetButtonSetEnabledImage`, `…HiliteImage`, `…HiliteSelectedImage`, `…DisabledImage`), the image of
  `ExpBarForeground`, the logo (`ControlBar::updateRightHUDImage`) and the image of empty queue places
  (`ControlBar::updateBuildQueueDisabledImages`). Layers 5–3 lie under the windows (`W3DCommandBarBackgroundDraw`),
  2–0 above them (`W3DCommandBarForegroundDraw`; `CONTROL_BAR_SCHEME_FOREGROUND_IMAGE_LAYERS` = 3, `ControlBarScheme.h`).
- **Right window:** `ControlBar::setPortraitByObject` shows, for a selected object, its `SelectPortrait` in
  `CameoWindow` and takes the status `IMAGE` from `RightHUD`; without an object `RightHUD` shows the logo (`W3DRightHUDDraw`
  draws only with `IMAGE`). `W3DGadgetPushButtonImageDrawOne` (`W3DPushButton.cpp`) draws only an image, never a box.
- **Message boxes:** `GameWindowManager::gogoMessageBox` (`GameWindowManager.cpp`) reads the places of `ButtonOk`,
  `ButtonNo`, `ButtonCancel`, puts Yes on the first, No on the last (with Cancel: on the middle one).
- **Font:** `GlobalLanguage::adjustFontSize` always relates template sizes to 800 pixels of width; that was already
  implemented generally (`UiScaler.ScaleFontSize`) and needed no change.

## 3. What was tailored to the mod

All places found with number or name literals (search for `1920`, `1080`, `800`, `600`, `*Pro`, window names):

| Place (state before the changes) | What | Verdict |
|---|---|---|
| `src/CnCNew.Game/Hud/MatchController.cs:677, 692, 696, 765` | scheme rectangles as `WndScreenRect(…, 1920, 1080)` | **Assumption.** Worked correctly only by chance: `UiTransform.Map` ignores the creation size of the rectangle passed in. Fixed. |
| `src/CnCNew.Game/Hud/ControlBarScheme.cs` | did not read `ScreenCreationRes`; comment "1920x1080 creation space"; unknown keys silently passed over; skipped the line after an `AnimatingPart` | **Assumption / silent fallback.** Fixed. |
| `src/CnCNew.Ui/Layout/UiScaler.cs:103` (`ClassifyAxis`), `:133` | "full extent" only for `high >= extent` | **Assumption** (fits 0–1920, not 0–799). Consequence: bar at 16:9 centred in 4:3 width. Fixed. |
| `MatchController.ResolveHardCodedImages` | logo only via the image name `HardCodedWithControlBarSchemeIni`, which only the mod layout carries | **Assumption.** Never drawn with the master. Fixed. |
| `MatchController` (missing) | scheme-controlled buttons neither placed nor given images | **Gap, hidden by the mod** (there placeholders 0,0–100,100 and art in the background). Fixed. |
| `src/CnCNew.Ui/Runtime/UiDrawBuilder.cs:49` | window with `W3DGadgetPushButtonImageDraw` without an image as a black box | wrong independently of the mod (visible there too). Fixed. |
| `src/CnCNew.Game/Shell/Screens/MessageBoxScreen.cs` | buttons only shown/hidden, not put on the places | **Gap, hidden by the mod.** Fixed. |
| `src/CnCNew.Game/Data/GameData.cs` (`LoadText`) | `Data/Patch.str` not read | gap; visible with the master (missing text). Fixed, see section 6. |
| `tests/CnCNew.Game.Tests` | quit dialog "is a 1920x1080 layout", `LabelSmall`, sizes 36/14/21; message position 10,22 | expectations of the mod. Re-measured. |
| `scripts/input/clicks-during-intro.txt:5` | `screen:474,640` as "first command button" | position of the mod layout (in the master the second button). Now `button:ControlBar.wnd:ButtonCommand01`. |
| `src/CnCNew.Ui/Wnd/WndParser.cs:391, 397` | 800×600 as a substitute for a broken `SCREENRECT` | **general**: only with an error diagnostic, the creation resolution is otherwise read from the file. |
| `src/CnCNew.Ui/Layout/UiScaler.cs:68–69` | `DefaultDisplayWidth/Height` 800×600 | **general**: `DEFAULT_DISPLAY_WIDTH` of the original for `adjustFontSize`. |
| `src/CnCNew.Ui/Settings/Resolution.cs`, `ShellSupport.cs:25` | resolution lists, desktop size of the test environment | **general**, no relation to layouts. |

All 106 decorated window names that `src/`, `tools/` and `scripts/` use exist in the layouts of the master
(six of them are prefixes of numbered windows). No place in the code names a `*Pro` image.

**Images that the master does not have:** exactly one. All 14 schemes name `QueueButtonImage SCBigButton`; no
`MappedImages` entry defines it (just as little in the mod content). The original gets a null image for it and leaves the
empty queue places without an image. The name is in `MatchController.MissingSchemeImages`; no substitute is drawn.

## 4. What was changed

Order of the commits (branch of the session):

| Commit | Kind | Content |
|---|---|---|
| `fix(ui): anchor mode takes a window written with its last pixel as full extent` | Code | `UiScaler.FullExtentSlack` = 1: 0–799 of 800 counts as full extent; the bar reaches from edge to edge as in the original |
| `test(w3d)`, `test(ui)`, `chore(baselines): rewrite the compatibility baseline …` | Re-measured | W3D inventory, `Window/` file count, message values, known missing labels, compatibility baseline |
| `fix(hud): place the control bar scheme by its own ScreenCreationRes` | Code | parser reads `ScreenCreationRes`, rejects a scheme without it, counts unread keys; `SchemeRectOnScreen` |
| `fix(hud): the control bar scheme places and pictures the bar's own windows` | Code | buttons, logo, portrait, foreground/background layers, no box for image windows, `MissingSchemeImages` |
| `fix(shell): the retail main menu's custom mission button has a text and is inert` | Code | `Data/Patch.str`, button disabled |
| `fix(shell): message boxes put Yes and No at the layout's button places` | Code | `UiWindow.SetDesignPosition`, `gogoMessageBox` places |
| `test(game): the message position of the retail InGameUI.ini …` | Re-measured | 10,22 → 10,10 |
| `chore(baselines): shell screen hashes against the Steam master` | Golden | `tests/CnCNew.Game.Tests/baselines/screens.txt`: 12 of 54 lines new |
| `chore(baselines): HUD draw list hash against the Steam master` | Golden | `tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256` |
| `chore(tools)`, `fix(scripts)`, `docs` | Rest | comment in the `UiRenderTool`, input script, this document |

**Golden files:** both contain exclusively SHA-256 values (`Name@Größe=Hash` or one hash), no draw list text
and no strings from the string table of the game. Both were regenerated twice, byte-identical. For the
`UiRenderTool` there is **no** golden file in the repo (its images never lie in the repo; what is checked is the equality of two
runs: 162 of 162 images pixel-identical). Note outside the golden files: some test sources compare against
English display texts as literals (for example `HudTests.cs`/`MissionTextTests.cs` against the text of
`GUI:CantBuildThere`); that is older than this work and was not touched.

## 5. Images (outside the repo)

Base directory: `%TEMP%\claude\C--Users-cp-RiderProjects-CnCNew\029094c2-0de8-428b-9dfa-9dd14c0f3e8e\scratchpad\vanilla\`

| Directory | Content |
|---|---|
| `render-mod\` | `UiRenderTool`, all scenes, mod content (comparison) |
| `render-vanilla-before\` | the same scenes, master, code before the changes |
| `render-vanilla-after\`, `render-vanilla-after2\` | master, code afterwards, two runs (pixel-identical) |
| `client-vanilla-before\`, `client-vanilla-after\` | client `--smoke-test --scenario exit-america-all` with input script, each at 800×600, 1280×720, 1920×1080: `strip-<Größe>\frame-NNNNN.png` (every 30 frames), `shell-<Größe>.png` |
| `compare-mod-vs-vanilla-before.txt` | pixel comparison mod against master before the changes (112 the same, 50 different) |

Frames of the film strip: 30 idle with logo; 60 dozer selected, build menu, tooltip; 90 war factory with
queue, money tooltip (fading in); 120 barracks with queue, power tooltip; 150 command center; 600
game menu. The most telling pairs:

1. `client-vanilla-before\strip-1280x720\frame-00060.png` ↔ `client-vanilla-after\…\frame-00060.png` – bar centred with
   black holes against bar over the whole width with button column, general's button, toggle and portrait.
2. `…\frame-00030.png` (both) – without a selection: black box against logo of the side.
3. `render-vanilla-before\shell-quit-1280x720.png` ↔ `render-vanilla-after\shell-quit-1280x720.png` – Yes/No offset against one row.
4. `render-vanilla-before\flow-single-player-1280x720.png` ↔ `render-vanilla-after\…` – magenta `GUI:CustomMission` against a labelled, disabled button.

## 6. Assessment per screen

| Screen | Compared with | Finding before | Cause | Status |
|---|---|---|---|---|
| Command bar idle | `parseScreenRect`, `ControlBarScheme::init/drawBackground` | centred in 4:3 width, world visible left and right; button column on the left and tab at the top right black; on the right a black box instead of the logo | `UiScaler.ClassifyAxis`; missing scheme windows; logo tied to the mod name | fixed |
| Dozer selected | `setPortraitByObject` | black box in the portrait window | `UiDrawBuilder` | fixed: portrait; upgrade icons are still missing |
| Building with queue | `populateBuildQueue` | images placed correctly | – | empty places without an image as in the original (`SCBigButton` is missing in the data) |
| Several units | – | **not assessed**: the input script selected only a building in the scene | – | open |
| Money, power | `ControlBarScheme::init`, `W3DPowerDraw` | money at the scheme place; power as a single-colour bar | – | the bar is not a reproduction of `W3DPowerDraw` (tiles `PowerPointG/Y/R`, logarithmic, slider): open, not caused by the mod |
| Tooltip of the buttons | `ControlBar::populateBuildTooltipLayout` | pointer tooltip over the bar | deliberate (no `ControlBarPopupDescription.wnd`) | unchanged; covers buttons and money, unlike the original |
| Radar | – | map with true aspect in the frame (stretched at 16:9) | – | unchanged; **not checked against the source code of the original** (`W3DLeftHUDDraw`, `W3DRadar`) |
| Message list | `InGameUI.ini` | correct, now from y = 10 | data value | tests re-measured |
| General's powers (shortcut bar), experience window, diplomacy, chat | – | **cannot be assessed**: the client does not load `GenPowersShortcutBar*.wnd`, `GeneralsExpPoints.wnd`, `Diplomacy.wnd`, `InGameChat.wnd` | function not built | open; general's button, communicator and beacon still report "not supported"; experience bar (`W3DCommandBarGenExpDraw`) and attack flashing (`triggerRadarAttackGlow`) are missing |
| Game menu (`QuitMenu.wnd`) | layout | correct (logo, five buttons, translucent) | – | fine |
| Main menu | layout | as with the mod | – | fine; the shell scales uniformly with borders (deliberate, `docs/GAME-SHELL.md`), the original stretches |
| Single-player menu | layout, `Data/Patch.str` | magenta `GUI:CustomMission` | text only in `Patch.str` | text present, button disabled. **Not verified** what the Steam exe does with the button: the published source code knows neither the file nor the window |
| Skirmish setup, map choice, options, loading screen, final screen, load/save, credits | pixel comparison mod ↔ master | pixel-identical | – | unchanged |
| Quit dialog | `gogoMessageBox` | Yes on the left, No one row lower in the middle | missing placement | fixed |
| Victory/defeat, mission pause menu (`PopupSaveLoad.wnd`) | – | **not looked at** (no scene); the tests pass | – | open |

**Decision for the project owner:** the command bar now follows the original and is horizontally stretched at 16:9
(icons 1.33 times wider). Whoever wants it undistorted and centred sets `Display.UiScale` to `IntegerMultiple`
(uniform scaling); the default `Uniform` → `Anchor` stretches like the original, `Stretch` likewise.

## 7. Re-measured inventory numbers (Steam master)

| Quantity | Steam master | documented before (mod content, partly older) |
|---|---|---|
| Files (`AssetTool verify`) | 25,608 | 25,682 |
| INI files parsed (`IniTool audit`) | 224 of 224, 66,304 blocks | – |
| Full inventory: loaded / excluded | 216 / 8 | 228 / 28 |
| Full inventory: blocks / definitions / modules / keys | 65,754 / 12,459 / 16,380 / 332,464 | 65,875 / 12,574 / 16,380 / 333,061 |
| Full inventory: references resolved / total / fallbacks | 19,110 / 20,660 / 1,550 | 18,321 / 19,871 / 1,550 |
| Definition snapshot full inventory | 144,981,631 bytes, SHA-256 `33B54BDFED4E36D83DA20B1E901ABF0DF64E9D6809DAD2FCFBFFCA7127DA874E` | 144,895,548 bytes, `0A9D10F4…` |
| `--maps exclude`: loaded / excluded | 134 / 90 | – |
| `--maps exclude`: blocks / definitions / modules / keys / objects | 63,626 / 12,435 / 16,065 / 326,696 / 2,115 | 12,550 definitions, 2,115 objects |
| `--maps exclude`: references | 18,702 / 20,225 / 1,523 | – |
| Definition snapshot `--maps exclude` (two runs the same) | 144,364,843 bytes, SHA-256 `3E9AAB0DC388AE8CBF9C15384A6DE024B3F83ACF8EDDD821AA121E38CF6564BE` | 144,284,673 bytes, `2B9D902A…` |
| `MapTool audit` | 150 of 150 maps; 462 start pairs, 456 complete, 6 partial | the same |
| Map scripts | 1,631 lists, 23,906 scripts, 2,294 groups, 33,890 conditions, 82,563 actions | the same |
| `W3dTool audit` | 8,901 files, 70,571 meshes, 2,094,998 triangles | – |
| `W3dTool audit-skeletons` | 8,894 files, 6,478 hierarchies, 6,502 HLODs | 8,897 / 6,481 / 6,505 |
| `ImageTool audit-tga` | 804 of 804 | – |
| `PresentationTool object-audit` | 2,115 objects, 1,847 with a model, 2,928 draw modules | – |
| `PresentationTool fx-audit` | 429 FXLists, 1,453 nuggets, 1,087 particle systems | – |
| `PresentationTool terrain-audit` | 150 of 150 maps, 248 terrain types, 5 known missing textures | – |
| `SimTool audit-modules` | 183 of 205 module types; 16,558 of 19,553 uses (84.7 %) | the same |
| `SimTool audit-coverage` | 2,115 of 2,115 objects, fingerprint `AC622E33080D89EB` | 2,115 of 2,115 |
| Compatibility baseline | 731 entries | 751 |
| WND content | 80 files under `Window/`, 1,667 windows, all 800×600, 0 parser diagnostics | 83 files, 1,814 windows (347 at 1920×1080), 12 diagnostics |

Logs of the measurement: `…\scratchpad\vanilla\rebaseline\`.

**Compatibility baseline 751 → 731:** 20 `excluded-file` entries drop out (`HeaderTemplate.ini`/`Language.ini` of
Brazilian, French, German, Italian, Polish, Spanish; plus the `…9x.ini` of Chinese and Korean – the mod brought these files
along). Two entries change the number: `definition 'HeaderTemplate'` 19 → 15 (four `ControlBarProScrollListBox*`),
`definition 'MappedImage'` 1,486 → 1,369 (117 blocks in twelve mod files under `MappedImages/HandCreated/`: eleven `*Pro.ini`
and `BlankTexture.ini`). Nothing is added.

**`Window/` 83 → 80:** missing are `Menus/GameSpyLoginProfile.wnd_hidemail`, `Menus/NetworkDirectConnect.wnd_hideip`,
`Menus/OptionsMenu.wnd_hideip` (variants of the mod). No code loads them; `src/CnCNew.Formats/AssetFormat.cs:27` only
classifies the extensions.

**W3D 8,897 → 8,894:** `Art/W3D/ADMtlPipL.W3D`, `ADMtlPipL1.W3D`, `ADMtlPipL2.W3D` came from `CustomContentMaps.big`.

## 8. Checks at the end (measured 9 October 2026, Steam master)

| Program | without content | with `--content-dir <Master>` |
|---|---|---|
| `Archives`, `Content`, `IO`, `Images`, `Formats`, `Import` | passed | – (no real-data mode used) |
| `Data.Tests` | passed | passed (baseline 731 entries) |
| `W3D.Tests` | passed | passed |
| `Ui.Tests` | passed | passed |
| `Presentation.Tests` | passed | passed |
| `Audio.Tests` | passed | passed |
| `Simulation.Tests` (Release, without `--long`) | passed | passed |
| `Game.Tests` (Release) | passed | passed |

`dotnet build CnCNew.slnx` (with deleted `bin`/`obj` of the client) and `dotnet build tools/CnCNew.UiRenderTool`: 0 warnings each.
`IniTool compat-check`: 731 entries match. `scripts/check-dozer-build.ps1` 5 of 5, `check-build-hotkeys.ps1` 3 of 3,
`check-spectator.ps1` passed, `audit-scenarios.ps1` all three scenes passed – each with `-ContentDir <Master>`.
Not run: `Simulation.Tests -- --long`, the reference and input manifests (are rewritten elsewhere),
`scripts/audit-vulnerable.ps1`, `capture-world.ps1`, `capture-text.ps1`.

## 9. What still looks wrong or could not be assessed

- No image was seen by a human; no comparison next to the running original.
- Multiple selection, victory/defeat and the pause menu of the mission have not been looked at.
- Power bar, experience bar, attack flashing, upgrade icons, description window of the command bar,
  general's powers, diplomacy and chat are not a reproduction of the original or not built (section 6).
- `ButtonCustomMission`: behaviour of the Steam exe unknown; here labelled and disabled.
- The shell (menus) continues to scale uniformly with borders instead of stretching like the original – an earlier, deliberate
  decision that has nothing to do with the mod.
- `IniTool load-order-report` names "excluded files: 8" but lists only two; the baseline carries eight. Not investigated.
