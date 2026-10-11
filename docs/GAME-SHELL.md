# Stride-free game shell: boot, menus, skirmish setup, result (package G1)

This document describes `src/CnCNew.Game/Shell/**`: a Stride-free, headlessly testable state machine for the
frame around `LiveMatch` (`src/CnCNew.Game/Live/LiveMatch.cs`, not part of this package) – program start, main menu,
options, skirmish setup, handover to the running match and result screen. The layer reads real WND
layouts (`src/CnCNew.Ui/Wnd`, parsed via `WndParser`) and drives them through the callback registry of `UiWindowTree`
(`src/CnCNew.Ui/Runtime`); no Stride type, no Direct3D call occurs in this layer. Reference documents:
`docs/UI-WND.md`, `docs/UI-TEXT-AND-SETTINGS.md`, `docs/PRESENTATION-INPUT.md`.

## 1. State machine (`GameShell`, `ShellPhase`)

```
Boot (GameData.Load in the background thread, ShellGameLoadScreen)
  └─▶ Menus: MainMenu; on top of it Options / QuitConfirm / MapSelect as an overlay, SkirmishSetup / Credits / ChallengeMenu / LoadGame as a replacement (§13)
        └─▶ MatchLoading (SinglePlayerLoadScreen, exactly one drawn frame)
              └─▶ InMatch (the host owns LiveMatch/HUD; the shell draws nothing)
                    └─▶ Results (ScoreScreen, from a real SkirmishMatchSession)
                          └─▶ back to Menus
```

`GameShell` holds a stack of `ShellScreen`s (one screen = a separate `UiWindowTree` instance, so that a modal
dialog has its own focus order and does not seep through into the screen underneath – `FocusOrder()` otherwise covers
the whole tree). Only the topmost screen receives input; `BuildDrawList()` chains the draw lists of the
topmost screen that hides the ones underneath (`ShellScreen.HidesScreensBelow`, like `Shell::push`) and of all screens above it from bottom to top and sums their
statistics (§13). So the main menu no longer shines through skirmish setup, credits, challenge menu and load list.

### Host interface

| Method | Purpose |
|---|---|
| `GameShell.Create(GameData, IShellEnvironment, language = "English", viewport?)` | Start already after boot (tests, or a host that loads content itself). |
| `GameShell.CreateLoading(contentDirectory, IShellEnvironment, language?, viewport?)` | Start in `ShellPhase.Boot`: opens the content synchronously at once (WND/CSF need no compiled definitions), loads `GameData` via `Task.Run` with a progress callback. |
| `Resize(width, height)` | Sets the viewport of all screens in the stack anew. |
| `HandleInput(InputEvent)` | Translates `CnCNew.Presentation.Input` events into `UiWindowTree` calls (`MouseMove/Down/Up/Wheel`, `KeyDown`, `Character`) and calls `Dispatch()`. |
| `Update(dtSeconds)` | Query boot progress, screen transitions (`MatchLoading` → `InMatch`), per-screen `Update` (credits scroll), advance the UI clock. |
| `BuildDrawList()` | Draw list of the visible screens of the stack (§13), post-processed with `ShellCustomDraw`. |
| `DrainOutputs()` | Drains `ShellOutput`s (see below), like `LiveMatch.DrainSoundCues`. |
| `MatchFinished(SkirmishMatchSession, MatchStatistics?)` | Called by the host when the match is over; shows the result screen. |

### Outputs (`ShellOutput`)

`StartMatchOutput(MatchSetup)`, `ExitRequestedOutput`, `SettingsChangedOutput(GameSettings)`,
`PlayUiSoundOutput(string EventName)`, `ApplyResolutionOutput(DisplayMode, Width, Height, VSync)`. The shell knows no
sound files or swap chains; it only names what the host is to do.

## 2. Implemented screens and callbacks

Every screen registers exactly the `SYSTEMCALLBACK`/`INPUTCALLBACK` names that the shipped WND file actually
uses (see source dumps under the scratchpad of the session). Result of the click-through in
`tests/CnCNew.Game.Tests/ShellTests.cs` against `game/content` (re-measure, see below): **0 unregistered
callbacks, 0 unresolved labels** across all eight visited screens.

| Screen (file) | Registered callbacks | Purpose |
|---|---|---|
| `MainMenuScreen` (`MainMenu.wnd`) | `MainMenuSystem`, `MainMenuInput` | One stacked file with all submenus; none of them is `Hidden` in the WND file itself – this corresponds to `MainMenuInit` (`MainMenu.cpp`), which the shell reproduces directly in the constructor: show the entry group `MapBorder2`, hide the four other groups, plus the six faction buttons "Recent Save"/"Load Game" to the left of the column (`showSelectiveButtons(SHOW_NONE)`; Zero Hour's `MainMenu.cpp` never shows them again – see §11). USA/GLA/China/Challenge open the **difficulty question in this file** (`MapBorder4`, §13). |
| `OptionsScreen` (`OptionsMenu.wnd`) | `OptionsMenuSystem`, `OptionsMenuInput` | Resolution, three volume sliders, scroll speed; Accept/Cancel/Defaults. Overlay of the main menu (`getOptionsLayout`, no `Shell::push`). |
| `SkirmishSetupScreen` (`SkirmishGameOptionsMenu.wnd`) | `SkirmishGameOptionsMenuSystem`, `SkirmishGameOptionsMenuInput` | Map, human faction/name, up to 7 computer opponents, starting cash → `MatchSetup`. |
| `SkirmishMapSelectScreen` (`SkirmishMapSelectMenu.wnd`) | `SkirmishMapSelectMenuSystem`, `SkirmishMapSelectMenuInput` | Map list from `Maps/MapCache.ini` (only `isMultiplayer = yes`), names in the second of the two columns. Overlay of the setup, which hides map preview, army/colour/team fields and Start for it (§13). |
| `ScoreScreenScreen` (`ScoreScreen.wnd`) | `ScoreScreenSystem`, `ScoreScreenInput` | Eight result rows from `MatchResult`. |
| `CreditsScreen` (`CreditsMenu.wnd`) | `CreditsMenuInput` | Scrolling credits from `Data/INI/Credits.ini`. |
| `LoadingScreen` (`ShellGameLoadScreen.wnd`, `SinglePlayerLoadScreen.wnd`) | none | Neither file declares a system/input callback anywhere; only `ProgressLoad` is set. |
| `MessageBoxScreen` (`MessageBox.wnd`, `QuitMessageBox.wnd`) | `MessageBoxSystem`/`QuitMessageBoxSystem` | Shared implementation; neither of the two files declares an `INPUTCALLBACK` anywhere either, Escape is therefore deliberately not wired here. |

Built-in callback names (`PassMessagesToParentSystem`, `PassSelectedButtonsToParentSystem`,
`GameWinBlockInput`, `W3DNoDraw`, gadget functions) need no registration – `UiWindowTree` already knows them.

### Campaign and saved-game screens (Phase 10, `CampaignScreens.cs`)

Five further screens, not part of the click-through named above/of the 54 golden hashes (own tests in
`tests/CnCNew.Game.Tests/CampaignShellTests.cs`, `ShellTests.cs`): `ChallengeSelectScreen` (`ChallengeMenu.wnd`), `LoadGameScreen` (`SaveLoad.wnd`),
`MissionBriefingScreen` (`SinglePlayerLoadScreen.wnd` reused), `MissionBannerScreen` (`Victorious.wnd`/`Defeat.wnd`,
no buttons) and `MissionResultScreen` (`ScoreScreen.wnd` reused). `CampaignFactionScreen` (`SinglePlayerMenu.wnd`) and `DifficultySelectScreen`
(`DifficultySelect.wnd`) have no longer existed since Phase 16: the original never opens either of them in Zero Hour (§13). Details and justification per screen:
`docs/CAMPAIGN-AND-SAVEGAMES.md` §7.

### Not in this package

`GameSpyGameOptionsMenu.wnd`, `LanGameOptionsMenu.wnd`, `GameSpyLoadScreen.wnd`, `MultiplayerLoadScreen.wnd`,
`ChallengeLoadScreen.wnd` (multiplayer/campaign) are not part of the scope (see `CLAUDE.md`) and are not
loaded; their callback names therefore consistently appear nowhere in `UnregisteredCallbacks`, because `ShellLayouts`
never parses them.

## 3. Custom draw callbacks (`ShellCustomDraw`)

A `DRAWCALLBACK` that `UiWindowTree` does not know appears as a `UiCustomDraw` command in the draw list
(`UiWindowTree.BuildDrawList`), counted in `UiDrawStatistics.CustomDraws`. `ShellCustomDraw` replaces exactly the two
names that actually occur with visible windows in the implemented screens:

* **`W3DDrawMapPreview`** (`MapWindow` in `SkirmishGameOptionsMenu.wnd`, `WinMapPreview` in
  `SkirmishMapSelectMenu.wnd`) → filled area, frame, centred name of the selected map (or "No map selected").
* **`W3DCreditsMenuDraw`** (`WinTextDraw` in `CreditsMenu.wnd`) → the lines from `ShellCredits`, scrolling vertically
  via `CreditsScreen.Update(dtSeconds)` (deterministic: a function of the accumulated simulation time, never of the
  wall clock).

**`W3DClockDraw`** (`Clock`) and **`W3DShellMenuSchemeDraw`** (`ShellMenuScheme`) from `MainMenu.wnd` appear in
no measured run: both windows are `Hidden` in the WND file itself, and `UiWindowTree.BuildDrawList` aborts for a
hidden window before it evaluates its draw callback at all. The shell does not unhide these windows
(no clock widget, no animated 3D background scheme) – a deliberate, documented simplification, not a missing
registration: draw callbacks are not routed messages, there is nothing to register.

## 4. Localisation (`ShellTextSource`)

Every label runs through `GameData.LoadText(language)` (`LocalizedText`). `ShellTextSource.FallbackLabels` is a
precautionary, counted substitute table for labels that, according to the facts available for some menus, could be missing
(`Tooltip:LoadProgress`, `GUI:LanguageFilter`, display mode/language/aspect ratio/scaling/contrast, for which there are
no gadgets of their own at all in the shipped files). **Measured** (click-through over eight screens, real
retail data, English): **0 fallback hits, 0 unresolved labels in total** – every label that the eight
implemented screens actually draw visibly is already in the shipped CSF. The table remains as a
safety net (e.g. for the case that a tooltip is actually displayed some day) and continues to be
counted in case that changes with more test coverage.

Map names (`ShellMapNames.DisplayName`) resolve `MapCache.ini`'s `nameLookupTag` (e.g. `MAP:AlpineAssault`) through the
same text source; **measured: 0 file name fallbacks** among 64 listed multiplayer maps (of 150
map entries/files in total) – each has a resolvable display name.

## 5. Settings (`GameSettings`, `SettingsStore`)

`OptionsScreen` edits a working copy of `GameSettings` and writes it only on "Accept" via
`SettingsStore` (`settings.json`, atomic, see `src/CnCNew.Ui/Settings/SettingsStore.cs`) and reports
`SettingsChangedOutput`/`ApplyResolutionOutput`. "Cancel" discards the working copy, "Defaults" resets only
display/audio/controls to `GameSettings.Default` (language and skirmish memory remain unchanged).
Wired: resolution (`ComboBoxResolution`, against `Resolutions.Standard` plus the current resolution if it is not
in the standard list), the three volume sliders, the scroll speed (the slider value is the factor of the
original, `docs/PRESENTATION-INPUT.md` §4a) and – since 7 October 2026 – the alternate mouse (`CheckAlternateMouse` ↔
`ControlSettings.AlternateMouse`, default checked, `docs/PRESENTATION-INPUT.md` §6). **Not wired** (no
matching `GameSettings` field, but visible and interactive so that the screen looks authentic): gamma, detail/
anti-aliasing selection, double-click attack move, retaliation, the entire `NetworkParent` group,
the hidden "advanced display options" panel. Display mode (window/full screen), language, aspect ratio and
accessibility text scaling have no gadget of their own in the shipped file and cannot be reached from this screen
(request: a future revision would need either a WND section of its own or the
reuse of one of the inert placeholders).

## 6. Skirmish setup and requests to `LiveMatch`/the simulation

`SkirmishSetupScreen` produces a `MatchSetup` from: map (`SkirmishMapSelectScreen`, only `isMultiplayer` maps),
human faction (`ComboBoxPlayerTemplate0`, including "Random", resolved deterministically via a seed fixed per slot
– not the match seed), up to 7 computer opponents (`ComboBoxPlayer1..7` = `GUI:Open`, `GUI:Closed`, `GUI:MediumAI`; `SkirmishGameOptionsMenuInit` additionally offers `GUI:EasyAI` and `GUI:HardAI`, see below; limited by the
number of start positions of the selected map from `MapCache.ini`), starting cash (`ComboBoxStartingCash` →
`SkirmishMatchOptions.StartMoney`). The map is preset as in the original: the one played last (`SkirmishMemory.LastMap`, `SkirmishPreferences::getPreferredMap`),
otherwise the first multiplayer map of the catalogue in the order of the lower-cased paths (`getDefaultMap(TRUE)`, the `MapCache` is sorted by that); the text
"(no map selected)" no longer exists, without any multiplayer map the name field stays empty and the preview shows only its frame. The second slot starts as "Medium Army"
(the original takes the easy, medium or hard army depending on the number of victories; here there is only one), the name field carries `GUI:Player`. Without at least one computer opponent, Start
simply does nothing (no inline error message – deliberate simplification).

Not wired because `LiveMatch`/`SkirmishMatchOptions` do not support it (requests for a future phase):

* **More than 7 computer opponent slots / own choice of start position**: `LiveMatch.Create` firmly assigns start position 0 to the
  human and iterates the remaining start positions in turn; `ButtonMapStartPosition0..7` in both
  map screens are purely decorative (clicks go nowhere).
* **Teams**: `ComboBoxTeam0..7` is visible and operable, but `SkirmishSlot.Team`/`MatchSetup` have no field for it.
* **Player colour**: `ComboBoxColor0..7` shows only generic "Color N" entries (no real colour palette connected).
* **AI difficulty level**: the original offers three armies per slot (`GUI:EasyAI`/`MediumAI`/`HardAI` = `SLOT_EASY_AI`/`SLOT_MED_AI`/`SLOT_BRUTAL_AI`;
  `GameLogic::startNewGame` turns that into `skirmishDifficulty`). This setup offers **one** ("Medium Army", `GUI:MediumAI`, `ShellSkirmishText.ComputerArmy`), because
  none of the difficulty effects evidenced in the data applies to a computer player in the skirmish and the rest is in the AI scripts not executed here
  (evidence and delimitation: `docs/SIMULATION-CORE.md` §35). Three entries that start the same AI therefore no longer exist; the counter `GameShell.SkirmishDifficultyIgnored`
  has been dropped because no entry promises anything any more that is not kept. The medium entry stays because the one computer player of the simulation
  corresponds to `DIFFICULTY_NORMAL` (no bonus, no malus). `GeneralSettings.DefaultAiDifficulty` is still read by nothing. The change of the setup has
  not been seen by a human.
* **Game speed/superweapon limit**: `SliderGameSpeed`/`CheckboxLimitSuperweapons` are visible but without
  effect.

## 7. Result screen (`MatchResult`, `MatchStatistics`)

`LiveMatch` exposes no tick events; the host therefore has to keep a `MatchStatistics` itself (`Record`
once per `TickResult`, as `SkirmishMatchSession.Step()` delivers them) and hand it to `GameShell.MatchFinished`.
Without a tracker (`statistics: null`) or if `HasData` never became true, every number column shows `-` instead of an
invented zero. Only `SimEventKind` values with an unambiguous player reference are counted: `ProductionCompleted` →
units built, `EntityDied`/`EntityDestroyed` → units lost/destroyed, `ConstructionCompleted` → buildings
built, `StructureSold` → buildings lost, `SupplyDeposited` → resources. **`BuildingsDestroyed` is not
counted** – there is no event that distinguishes a destroyed building from a destroyed unit (request to
the simulation for a future `SimEventKind.StructureDestroyed` or an `IsBuilding` flag on `EntityDestroyed`).
Observers are not modelled in `LiveMatch`; `StaticTextObserverN` stays hidden for every row.

## 8. Focus, keyboard, accessibility

Escape closes the topmost dialog provided the associated WND file declares an `INPUTCALLBACK` at all
(`MainMenu`, `OptionsMenu`, the two skirmish screens, `CreditsMenu`, `ChallengeMenu`, `SaveLoad` do; `MessageBox`/`QuitMessageBox` do not –
that is not a gap of the shell but exactly the behaviour of the shipped file). If the callback is not attached to the root (`SaveLoad.wnd`: to the child `SaveLoadMenu`), the
screen sets `UiWindowTree.KeyboardHome` (the `winSetFocus(parent)` of the layout init); Escape now also works there without a focused gadget (§13). Tab/Shift+Tab, arrow keys,
Enter/space run unchanged through `UiWindowTree.KeyDown` (see `docs/UI-WND.md`); because of its
own `UiWindowTree` instance every screen has its own, self-contained focus order. UI scaling runs via
`UiScaleOptions`/`UiScaler` (`Mode = Uniform`, see the `GameShell` constructor); a control for it in `GameSettings`
(`AccessibilitySettings.UiTextScalePercent`, `HighContrastUi`) already exists but – see section 5 – cannot be reached from
any gadget, because the shipped `OptionsMenu.wnd` provides none for it.

## 9. Tests and measured numbers

`tests/CnCNew.Game.Tests/ShellTests.cs` (suite `ShellTests`, registered in `Program.cs`), against
`game/content`:

* **Click-through**: main menu → single-player submenu → skirmish setup → map selection (click on the first
  row of the real, filtered map list) → back → Start → loading screen → `StartMatchOutput` → a real
  `SkirmishMatchSession` running 600 ticks (via the already existing `LiveMatch.Create`, not rewritten) →
  `MatchFinished` → result screen → back to the main menu → credits → Escape → quit confirmation (No, then Yes).
* **Eight visited screens** (`MainMenu`, `SkirmishSetup`, `SkirmishMapSelect`, `MatchLoading`, `ScoreScreen`,
  `Credits`, `Options`, `QuitConfirm`), each drawn at six resolutions: 800×600, 1024×768, 1280×720, 1920×1080,
  2560×1080 and 640×960 (narrow, portrait-like) – **54 golden hashes** in
  `tests/CnCNew.Game.Tests/baselines/screens.txt` (`SHA-256` of `UiDrawList.Dump()`), two independent runs
  byte-identical.
* **0 unregistered callbacks, 0 unresolved labels** across all 54 measuring points.
* Options: change resolution and music volume, Accept, create a new `GameShell` instance with the same
  `IShellEnvironment` → both values come back unchanged (`settings.json` persistence).
  `CNCNEW_UPDATE_BASELINES=1` rewrites the hash file (after a deliberate change); the default run only compares.
* Boot: `GameShell.CreateLoading` draws something immediately (the loading screen is visible from the first frame), reliably reaches
  `ShellPhase.Menus` after the background loading.

* **Screen stack** (Phase 16, §13, `ShellTests`): only the topmost replacing screen is drawn (skirmish setup, credits, load list, challenge menu: none of the six main menu texts
  appears), options, quit dialog and map selection lie above the screen underneath (command count = sum of both), under the map selection the setup hides the fields of the original and
  restores them; Escape ends the load list without gadget focus; the challenge menu shows biography and Play only after the selection, with resolved texts, and starts the selected campaign with
  the difficulty selected in the main menu.
* **Window transitions** (`ShellTransitionTests.cs`, ten tests against the real layouts, details §12): entry, input with hidden windows, submenus, faction logos, screen tour
  with flash-out/fade-in/renewed entry, all campaign buttons, result screen with numbers counting up, byte-identical repetition, run without animation.

`dotnet build CnCNew.slnx`: unchanged **21 warnings** (the documented Stride asset compiler baseline), **0**
of them new.

## 10. Known limits (summary)

* Campaign selection, briefing, load list, saving in the game and mission completion have been connected since Phase 10
  (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7, `src/CnCNew.Game/Shell/Screens/CampaignScreens.cs`,
  `src/CnCNew.Game/Shell/MissionPresentation.cs`, `src/CnCNew.Game/Hud/MissionPauseMenu.cs`); only multiplayer and
  playback (replays) remain outside the product scope (buttons visible, disabled).
* `RadioButtonUserMaps` delivers an empty list (no import path for own maps in this state).
* No mouse click ends the credits (only Escape) – `WinTextDraw` is a simple `USER` window, and this runtime
  routes only gadget and keyboard messages (see `docs/UI-WND.md`); an "any mouse click on a non-
  gadget window" message does not exist in the message model so far.
* `ShellTextMeasure` delivers synthetic, deterministic character widths (no real glyph metrics) and is now only
  the default for headless tests. A host passes `GameShell.Create(…, textMeasure)` the metrics of the font
  with which it draws; the Stride client has done so since the package of findings in §11 (`ClientRuntime.ForShell` passes
  `UiRenderer.TextMeasure` through) – before, all centred texts sat visibly beside the centre.
* Display mode/language/aspect ratio/accessibility text scaling are present in `GameSettings` but cannot be reached from
  any gadget of the shipped `OptionsMenu.wnd`.
* **Window transitions (§12):** switched on only in the Stride client (`GameShell.Create(…, animateTransitions: true)`, `--no-transitions` switches them off); `Create` without the parameter,
  all tests and the golden hashes run without animation. Not played are the groups of multiplayer screens, of the difficulty menu (here a screen of its own), of the game menu
  in the game (`Quit*`, belongs to the HUD), `GenExpFade`, `ControlBarArrow` and the screen fade at game start (`FadeWholeScreen` after loading) – see §12 "Not implemented".
* **Phase 16 (screen stack, §13):** the main menu stays in the stack under replacing screens (it plays its entry again on returning) but is not drawn. There is no background layer of its
  own: in the original the 3D "shell map" lies behind all menus (deferred, §11), here black; the layouts draw frame and ruler themselves (`MainMenuRuler` at the root of
  `SkirmishGameOptionsMenu.wnd`, `SaveLoad.wnd`, `ScoreScreen.wnd`). Escape in the main menu and in the difficulty question is an addition of this shell (`MainMenuInput` of the original knows no Escape key).
* **Phase 12:** the shell outputs (`StartMatchOutput`, `StartMissionOutput`, `ExitRequestedOutput`, display/settings changes) and the feedback
  (`MatchFinished`/`MissionFinished`) are connected in the Stride client (`ClientRuntime`, `docs/GAME-HUD.md` §15). On start `GameShell` reads `Data/INI/ChallengeMode.ini` and sets, via
  `CampaignCatalog.WithPersonas`, the persona faction of every challenge campaign (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7).

## 11. Findings of the first visual check (demo `shell`, 4 October 2026)

A human played `scripts/demo-shell.ps1` and reported five conspicuous points. Causes, fix and tests:

| Finding | Cause | Fix | Test | What a human still has to check |
|---|---|---|---|---|
| Buttons without a visible change of state | The selection of the draw data was already right (`W3DGadgetPushButtonImageDrawThree/One`, `W3DGadgetPushButtonDraw`: Hilite 0/5/6 under the mouse, Hilite 1/3/4 pressed, Disabled 0/5/6) and drew a change headlessly. What was wrong was the state itself: (a) a click gave the button the keyboard focus (in the original `GadgetPushButtonInput` never calls `winSetFocus`), and the focus highlight survived leaving – every button clicked once stayed highlighted, hovering no longer changed anything about it; (b) a button that was hidden on a change of group kept Hilite/Pressed and came back highlighted; (c) the screen covered by a dialog never learned that the mouse is gone. | `UiWindowTree`: focus by click now only for combo box, list box and text entry; `GWM_MOUSE_LEAVING` clears the highlight unconditionally; hiding clears Hilite/Pressed; new `PointerLeft()`, which `GameShell.Push` calls for the covered screen. | `WndTests.PushButtonStatesPickTheirDrawData`, `WndTests.HiliteFollowsThePointerOnly`; `ShellTests` "Buttons draw hilite and pushed images …" | Whether hovering and pressing are now clear enough in the running client (the shipped images `Buttons-HiLite-*` and `Buttons-Pushed-*` differ only slightly). A complete absence of any highlight could **not** be reproduced headlessly. |
| Sliders in the options menu invisible | The horizontal slider with `IMAGE` status was drawn as a three-part bar from Enabled 0/2/1; these entries are `NoImage`. The original (`W3DGadgetHorizontalSliderImageDraw`) draws no bar but a centred row of square boxes from fixed slots: Disabled 0 = filled, Disabled 1 = empty, Hilite 0 = frame behind the row when highlighted. | `UiDrawBuilder.SliderBoxes`; thumb like the child button from `gogoGadgetSlider` (13×16, 10 below the top edge, Hilite image only when highlighted, pressed entry 1). Dragging already worked. | `WndTests.HorizontalSliderDrawsBoxesAndThumb`; `ShellTests` "Options: sliders draw their boxes and drag …" | Colours and spacing of the boxes against the original; the frame (`linebox`) on hovering. |
| Green background behind headings on hovering | Every gadget with `MOUSETRACK` was highlighted. Static texts carry `MOUSETRACK` and a glaring `HILITEDRAWDATA` fill in the files, but `GadgetStaticTextInput` does not handle `GWM_MOUSE_ENTERING` – the state never arises in the original. | Highlight and enter/leave messages only for gadgets whose input function handles them (button, check box, radio, slider, list box, combo box, text entry). | `WndTests.StaticTextHasNoHiliteState`; `ShellTests` (seven captions of the options menu) | Nothing open known. |
| Grey texts with buttons to the left of the column | The six buttons `Button{USA,GLA,China}{RecentSave,LoadGame}`. They are not `HIDDEN` in `MainMenu.wnd`; `MainMenuInit` hides them via `showSelectiveButtons(SHOW_NONE)`. The shell had only disabled them. | `MainMenuScreen` hides them. | `ShellTests` "Main menu shows only what MainMenuInit leaves visible" | In the original a pair appears on hovering over USA/GLA/China; that is not implemented (saved games carry no faction). |
| Text in the quit dialog too small | `QuitMessageBox.wnd` is authored at 1920×1080, the remaining shell screens at 800×600. In the mode `Uniform`/`Anchor`, `UiScaler.ScaleFontSize` also scaled header template fonts with the creation resolution of the window; but template point sizes always refer to a width of 800 pixels (`GlobalLanguage::adjustFontSize`). Consequence at 16:9: only 600/1080 = 56 % of the intended size (LabelSmall 8 → 5 instead of 9 at 1280×720, 8 instead of 14 at 1920×1080). | Template fonts scale with the uniform factor against 800×600, `FONT` of the window still against the creation resolution. | `WndTests.FontScalingFollowsModeAndAccessibilityFactor`; `ShellTests` "Quit dialog text takes the header template size …" | The same cause hit **all** layouts authored at 1920×1080 with a template font: `ControlBar.wnd` (HUD), `QuitMenu.wnd`, `MessageBox.wnd`, `Diplomacy`, `GeneralsExpPoints`, the shortcut bars, `Victorious`/`Defeat`. Their texts are now 1.8 times as large as before at 16:9 – check in the HUD whether anything overflows. |

Fixed in addition, not reported: the shell set its texts with `ShellTextMeasure` (synthetic widths), drawing
was done with the real font; centred captions therefore sat to the right of the centre (§10).

**Not done:** the animated battle scene behind the main menu (deferred). The transition animations of the original
(`GameWindowTransitions`, `WindowTransitions.ini`, `TheTransitionHandler->setGroup(...)`: fading in of the buttons, unfolding of the groups,
flashing of the logo) have been implemented since Phase 16, see §12; without `animateTransitions` group changes remain hard cuts.

**Golden hashes:** 42 of the 54 values in `baselines/screens.txt` have changed – all screens that contain the main menu
or lie above it (hidden faction buttons), plus the sliders of the skirmish setup and the
font of the quit dialog; `MatchLoading` and `ScoreScreen` are unchanged. `baselines/hud-drawlist-1280x720.sha256`
has changed because of the template fonts of `ControlBar.wnd`. The hashes prove reproducibility, not
correctness.

**Means of inspection:** since then `tools/CnCNew.UiRenderTool` also draws the shell itself (`shell-*.png`, via
`GameShell.Create` and real input events): main menu, hovered and pressed button, options with
sliders (hovered, dragged), quit dialog – see `docs/CLIENT-UI-RENDERING.md` §5.

## 12. Window transitions (`WindowTransitions.ini`, Phase 16)

The original animates the shell via `GameWindowTransitionsHandler` (`GameWindowTransitions.cpp/.h`, `GameWindowTransitionsStyles.cpp`): buttons flash one after another, submenus fold
open and closed, the logo fades in, screen changes go through a black fade, the numbers of the result screen count up. The data are in `Data/INI/WindowTransitions.ini`
(56 groups, 381 window entries); the screens order groups with `TheTransitionHandler->setGroup(name)`, `reverse(name)`, `remove(name)` and wait for `isFinished()`.

### Data path

`WindowTransition` is still a counted compatibility block in the definition graph (56, `baselines/compatibility.json` unchanged, `IniTool compat-check`: 751 entries, definition snapshot unchanged).
The file is read by the strict reader `WindowTransitionTable.Parse` (`src/CnCNew.Ui/Transitions/`), like credits and campaigns (§4/`ShellCredits`): it is a pure display script of the shell, without
a consumer in simulation or presentation graph; typing it in `CnCNew.Data` would only have shifted the snapshot hashes. The reader knows no default value: unknown key, unknown
style, missing style, missing `WinName` (except for the two full-screen styles), duplicate group, negative delay and unterminated block are a `WindowTransitionFormatException` with file and
line (the original silently sets the first style or the empty name here). Real data: 56 groups, 381 entries, all styles known, **all window names resolve in the shipped layouts** (0 open).

### Structure (Stride-free, `CnCNew.Ui`)

| Type | Task | Original |
|---|---|---|
| `WindowTransitionTable` / `TransitionGroupSpec` | groups, window entries (`WinName`, `Style`, `FrameDelay`), `FireOnce` | `parseWindowTransitions`, `parseWindow` |
| `TransitionEffect` + 15 classes | one effect of a window: `Init`, `Update(frame)`, `Reverse`, `Skip`, `Draw`, `IsFinished`, `FrameLength` | `Transition` and styles |
| `TransitionWindow`, `TransitionGroup` | delay per window; group counter that runs forwards or (after `Reverse`) backwards; `Reverse` sets it to the longest `delay + length` | `TransitionWindow`, `TransitionGroup` |
| `WindowTransitionHandler` | `SetGroup`, `Reverse`, `Remove`, `IsFinished`, `Step`, `Advance(seconds)`, `AppendDraw`; current, waiting, drawn and last drawn group | `GameWindowTransitionsHandler` |
| `UiTransitionHost`, `ITransitionWindow` | binds the effects to a `UiWindowTree`: window by decorated name, hiding, reading and setting image/text, sounds | `TheWindowManager->winGetWindowFromId` and `GameWindow` calls |

* **Time:** one transition frame per client frame of the original; the shell runs at 30 FPS there (`GameData.ini`: `UseFPSLimit = Yes`, `FramesPerSecondLimit = 30`). `Advance(seconds)` turns that into whole frames of
  1/30 s (the remainder is carried over), nothing reads wall time – a sequence is a pure function of the stepped frames. `ShellScreen.Update` caps one call at 0.1 s (3 frames) so that a loading hang does not
  play the sequence in one step.
* **Drawing:** an effect hides its window and draws a substitute in its place (`UiBorderRect`/`UiFillRect`/`UiImageDraw`/`UiTextDraw` with alpha), which `GameShell.BuildDrawList` appends behind the draw list of the
  respective screen (in the original `winRepaint` draws the transitions last). The substitute of the button flash is `PushButtonImageDrawThree` (`UiWindowTree.BuildPushButtonImages`: left, middle
  and right image with alpha, without text). Every screen has its own handler (every screen has its own window tree); the handler of the original is global.
* **Input:** as in the original there is no lock in the handler. A window that the effect keeps hidden (buttons up to effect frame 11, groups until the end of their flash) takes no input
  (hidden windows are not hit); beyond that the screens refuse navigation as long as a group is running (`dontAllowTransitions` in the main menu: submenu change, campaign/skirmish/load/credits/
  options buttons and Escape; "Quit" is excepted); a screen that is just fading out (`IsClosing`) no longer gets input. `GameShell.IsAnimating` tells the host (and the tests) when everything stands still.

### The 15 styles (frequency in the file; frames = length of the effect)

| Style | n | Frames | Behaviour (source: `GameWindowTransitionsStyles.cpp`) |
|---|---|---|---|
| `FLASH` | 117 | 8 | window hidden in frames 1–3, white frame and fill with alpha 100/33, 150/66, 200/99, then visible from frame 4 with decaying white (75/50/25/10); sound `GUIBoarderFadeIn` |
| `BUTTONFLASH` | 77 | 17 | 1–3 white flash growing (sound `GUIButtonsFadeIn`), 4–7 button image under decaying white, 8–10 only the image, 11–16 window visible under decaying gradient `Gradient`; backwards the same tables mirrored |
| `WINFADE` | 7 | 10 | the first image of the window with alpha 25 · frame (1–9) |
| `WINSCALEUP` | 47 | 6 | the image grows from the centre in six steps (frames 1–5 drawn); sound `GUILogoMouseOver` |
| `SCORESCALEUP` | 7 | 6 | like `WINSCALEUP`; sound `GUIScoreScreenPictures` |
| `MAINMENUSCALEUP` | 8 | 5 | the Disabled image flies and grows in five steps into `WinGrowMarker`; sound `GUILogoSelect` |
| `MAINMENUMEDIUMSCALEUP` | 10 | 3 | the logo `WinFactionX` grows about its centre to the size of `WinFactionXMedium`; sound `GUILogoMouseOver` |
| `MAINMENUSMALLSCALEDOWN` | 0 | 6 | as above to `…Small`; **not used** by the shipped file (implemented and tested) |
| `TYPETEXT` | 4 | min(text length, 30) | one character per frame, sound `GUITypeText`; backwards it deletes |
| `SCREENFADE` | 1 | 30 | black area over the whole screen, alpha `1/29 · 255 · Frame`; backwards it fades up |
| `COUNTUP` | 56 | min(value/step, 30) | number counts up from 0 in steps of 1, 100 or 1000; sound `GUIScoreScreenTick` |
| `FULLFADE` | 12 | 10 | black with blue frame over the window area, alpha 51·frame up to frame 5 (full black, the window appears), then down again |
| `TEXTONFRAME` | 16 | 1 | the window appears after its delay (+1); a window that is already hidden stays hidden |
| `CONTROLBARARROW` | 1 | 22 | arrow image glides onto `ControlBar.wnd:ButtonGeneral` in 16 frames, fades out in 6 |
| `REVERSESOUND` | 18 | 2 | no image, plays `GUITransitionFade` on frame 1 |

The frame, alpha and step values come from the source code (rewritten, no code copied); every effect is tested with samples (`tests/CnCNew.Ui.Tests/TransitionTests.cs`).

### Which screens play which groups (23 of 56, counted in the test `GroupsAreKnown`)

| Screen | Groups | Trigger (after `MainMenu.cpp`, `SkirmishGameOptionsMenu.cpp`, `ChallengeMenu.cpp`, `PopupSaveLoad.cpp`, `ScoreScreen.cpp`) |
|---|---|---|
| Main menu, first start | `FadeWholeScreen` (backwards: fade in from black), then `MainMenuDefaultMenuLogoFade` | `MainMenuInit` with `FirstTimeRunningTheGame` |
| Main menu, every return | `MainMenuDefaultMenuLogoFade` | `MainMenuInit` after the closing of a screen that has faded the menu out (`OnRevealed`) |
| Submenu single player | remove `MainMenuDefaultMenu`, `MainMenuDefaultMenuBack` backwards, `MainMenuSinglePlayerMenu`; back: remove `…SinglePlayerMenu`, `MainMenuSinglePlayerMenuBack` backwards, `MainMenuDefaultMenu` | buttons single player / back, Escape |
| Submenu load/replay | `MainMenuLoadReplayMenu` / `MainMenuLoadReplayMenuBack` | buttons load/replay / back |
| Faction logos | `MainMenuFactionUS`, `…GLA`, `…China`, `…Training` (for "Challenge"), `…Skirmish` | hovering over the button (set), leaving (backwards); during a transition group the request is remembered (`showLogo`/`showSide`) |
| Main menu → skirmish | `MainMenuSinglePlayerMenuBackSkirmish` backwards (logo removed), the screen opens when everything is finished (`Shell::push` waits for the end of the shutdown animation) | button of the single-player column |
| Main menu → USA / GLA / China / Challenge | `MainMenuFaction<X>` (set and removed, for Challenge only set), `MainMenuSinglePlayerMenuBack<X>` backwards, `MainMenuDifficultyMenu<X>`; back: `…Menu<X>Back` backwards, `MainMenuSinglePlayer<X>MenuFromDiff`; choice: `MainMenuDifficultyMenuBack` backwards, campaign `FadeWholeScreen`, Challenge `MainMenuDifficultyMenuTraining` backwards and then the challenge menu (§13) | buttons of the single-player column, `ButtonEasy/Medium/Hard/DiffBack` |
| Main menu → credits | `MainMenuDefaultMenu` backwards | button credits |
| Main menu → load game | `MainMenuLoadReplayMenuBackTransition` backwards | button load |
| Skirmish setup | `SkirmishGameOptionsMenuFade` (entry and backwards on closing) | `SkirmishGameOptionsMenuUpdate` / `…Shutdown` |
| Challenge selection | `ChallengeMenuFade` (likewise) | `ChallengeMenu.cpp` |
| Load game | `SaveLoadMenuFade` (likewise) | `PopupSaveLoad.cpp` |
| Result screen (skirmish) | `ScoreScreenShow` (rows appear one after another, numbers count up, images grow) | `ScoreScreenInit` |

Options, credits, quit dialog, map selection, briefing and mission result have no group in the original (the options only used the older `AnimateWindowManager`, which the shipped menus
practically no longer call) and remain hard cuts.

### Deviations from the original (deliberate, all named)

1. **First start:** the original waits with the main menu until the mouse is moved by more than 20 pixels or a key is pressed (`notShown`), and then plays `MainMenuFade` (ruler) and the menu. Here it begins
   by itself after the fade; the ruler (`MainMenuRuler`) is there immediately.
2. **Frame 0:** the transition group that has just been started is already drawn in the first frame (`PrimeDraw`); the original only draws after the first `update` (one frame of bare image, e.g. before the fade from black).
3. **Effects without a window** (name not in the screen) end immediately and the name is counted (`MissingWindows`); the original dereferences the null pointer or leaves the state open. `MAINMENUSCALEUP` without `WinGrowMarker` likewise.
4. **`TYPETEXT`** finishes at `Frame == length`; the original only at frame 30, so that a text below 30 characters never finishes the group. Identical at 30 and more characters.
5. **`COUNTUP`/`TEXTONFRAME` on a hidden window** (row of an unoccupied player slot): the window stays hidden. The original makes it visible on its start frame (`COUNTUP`), which only looks good as long as the
   text is empty; here the template text ("Units Built") is in the empty rows, hence the deviation.
6. **Backwards-running group that does not end:** if the counter runs below 0 without all windows reporting finished, the group is ended (`Overrun`, test: never occurs with the shipped data).
7. **Image `Gradient`:** the gradient image of `BUTTONFLASH` (frames 11–16) does not exist in the retail content (`findImageByName` returns NULL in the original, `drawImage(NULL)` draws nothing). Here likewise nothing, the name is in
   `MissingImages`. So the buttons flash up to frame 10 and then appear without a gradient.
8. **Return:** `OnRevealed` plays the entry again when the menu has faded out for another screen; when a dialog (quit) is closed, that does not happen.
9. **State of rest:** the single-player submenu shows the five small faction logos at the bottom edge at rest (`WINSCALEUP` leaves them visible). The hard variant did not show them so far; it does now, so that
   the state of rest with and without animation is the same (see hashes).
10. **Main menu init:** `initialHide()` hides all fifteen logo windows (five factions × three sizes); without that the medium logo stayed after a campaign choice (noticed in the test).

### Golden hashes

`baselines/screens.txt`: **24 of 54** values changed (4 screens × 6 resolutions): `MainMenu-SinglePlayer`, `SkirmishSetup`, `SkirmishMapSelect`, `SkirmishSetup-MapChosen`; all four contain the open
single-player submenu (visible or underneath), the cause exclusively the five logos (item 9). Unchanged: `MainMenu`, `MatchLoading`, `ScoreScreen`, `Credits`, `Options`, `QuitConfirm` (30 values) and the
HUD hash. The transitions themselves change no hash, because without `animateTransitions` the shell creates no group (`NoTransitionsNoAnimation`); the states of rest of the animation are byte-identical in the tests with those without.

### Measured values (30 frames/s, `game/content`, re-measure)

* Main menu: first start 57 frames (1.90 s): fade 29 frames, border from frame 34, logo 40, first button 46, last button 51. Longest forward run in the real-data test (all numbers assumed to be `1234`): 60 frames (2.00 s, `ScoreScreenShow`).
* All 56 groups run forwards and backwards to the end without `Overrun` (real-data test).
* 18 synthetic tests plus real-data test in `TransitionTests` (Ui: reader, effects, handler, clock, real window tree), 10 tests in `ShellTransitionTests` (Game). Two runs of the same sequence: same hash (`1730265A0B3D8B31` for
  20 frames of entry + single-player change + hover).
* `tools/CnCNew.UiRenderTool` writes the sequences as PNG (`docs/CLIENT-UI-RENDERING.md` §7): 49 new images, all 113 PNGs byte-identical in two runs; looked at: entry (black, ruler, logo and buttons in the
  flash), single-player change with growing logos, hover logo, skirmish fade-out/fade-in, result screen.

### Not implemented (named)

* The groups of multiplayer and GameSpy screens (eleven `FULLFADE` groups, `Multi` submenu), of the difficulty menu (`MainMenuDifficulty*`, `…FromDiff`: here the selection is a screen of its own), the game menu groups in
  the game (`QuitNoSave`, `QuitFull` and their `Back` groups; the game menu belongs to the HUD), `GenExpFade` and `ControlBarArrow` (the effects are implemented and tested, but the HUD does not call them).
* The screen fade at game start (`FadeWholeScreen` to black after loading, back at the start of the game): the Stride client has no layer for it above world and HUD; the loading screen switches hard.
* The mouse wait loop of the first start (deviation 1), the bibs/hints of other screens and the battle scene behind the menu.

### Not verified (a human has to look)

* **That the sequences look and feel in the window as in the original.** What has been checked is samples, orders, states and individual PNGs of an offscreen renderer; tempo (30 frames/s against the
  actual frame rate of the window), sound timings (`GUIButtonsFadeIn` per button) and overall impression have **not** been seen or heard.
* Whether the first start without waiting for the mouse is disturbing, whether the missing gradient stage (item 7) is noticeable, whether the 0.1 s cap is perceptible with slow frames.
* The effect at resolutions other than 1280×720 (the substitute areas use the scaled window rectangles; the PNG sequences exist only there).
* (Done in §13: `LoadGameScreen` and Escape without keyboard focus.)
### Phase 16: text comparison with the original (summary, table in `docs/TEXT-AUDIT.md`)

The human found the texts "odd" ("komisch") compared with the original. Examined systematically (data + reference code, no original image available); fixed and effective in the shell area:

| Finding | Cause | Fix | Test |
|---|---|---|---|
| "Generals" font (title, main menu buttons, 294 window texts) up to 85 px too wide, line heights 1 px off | FreeType approximation (91 % compression, unhinted advances) instead of the GDI font of the original (`lfWidth = 0,40·height`, GDI chooses the width: approx. 87 %) | `GdiFontProvider` (`docs/CLIENT-UI-RENDERING.md` §2) | `UiRenderTool` self-check + `--font-audit` (means of inspection) |
| "Alternate Mouse Setu" cut off | Texts of buttons, check boxes and radio buttons were clipped to the window; in the original only static text sets a clip | `UiDrawBuilder.Text` with clip per gadget type | `WndTests` (golden), `SentenceLayoutTests` |
| Line wrapping and centring different | greedy `<=` wrapping without indent and overlap instead of `Build_Sentence_*` | `UiSentenceLayout` | `SentenceLayoutTests` (9 cases, recomputed by hand) |
| Skirmish setup: `FactionAmerica`, `Color 1`, `None` | fixed names instead of `PlayerTemplate.DisplayName` (`INI:FactionAmerica` = "USA"), `MultiplayerColor.TooltipName` (`Color:Gold`), `Team:n` | `ShellSkirmishText` (fallbacks counted in `ShellTextSource.RawNameHits`) | `ShellTests` "Skirmish setup and options show localized words …" |
| Options: entry fields show the word "Entry" | TEXT of the layout file, `OptionsMenuInit` overwrites it | both network fields start empty | the same |
| Credits in Arial 24 bold / 16 bold / 12 | fixed values instead of `CreditsTitleFont`/`CreditsMinorTitleFont`/`CreditsNormalFont` from `Language.ini` with `adjustFontSize` | `LanguageFonts`, `CreditsFonts` | `ShellTests` "Credits lines use the Language.ini credits fonts …" |
| Tooltips were missing entirely | never implemented | `TooltipPainter` in the shell and HUD draw path | `ShellTests` "A window with TOOLTIPTEXT shows the pointer tooltip …" |

**Fixed since (§13): (a) screen stack, (b) `SinglePlayerMenu.wnd`.** Wording of the finding before the fix: (a) *screen stack*: `GameShell.BuildDrawList` drew **every** screen of the stack. In the original `Shell::push` first
shuts down the upper screen (`runShutdown`, then hidden), so that options, skirmish setup and campaign screens replace the main menu; here its button texts
("CHALLENGE", "BACK" …) shine through the new windows, most clearly in the skirmish setup. For that the background (frame, logo, in the original the 3D shadow map) would need a layer of its own.
(b) *`SinglePlayerMenu.wnd`* (intermediate page New/Load/Back after "USA") does not exist in the original (USA leads directly into the difficulty dialog); its texts were in Times New Roman, as
the layout file prescribes. (c) *Skirmish setup* – **fixed** (5 October 2026): "Computer" and "(no map selected)"/"No map selected" are replaced by the labels and the map preset of the original
(section "Skirmish setup" above). Still open: the colour entries carry no colour swatch. (d) Briefing and loading screen have movie backgrounds and progress texts in the original, here a still image.

## 13. Screen stack, difficulty question, load list (Phase 16, menus)

Three findings of the text check (`docs/TEXT-AUDIT.md`, §11 above) with sources in the original (`Shell.cpp`: `push`, `pop`, `shutdownComplete`, `showShellMap`; `MainMenu.cpp`: `MainMenuShutdown`, `shutdownComplete`,
`GBM_SELECTED`, `setupGameStart`, `prepareCampaignGame`, `diffReverseSide`; `SkirmishMapSelectMenu.cpp`: `showSkirmishGameOptionsUnderlyingGUIElements`; `PopupSaveLoad.cpp`: `SaveLoadMenuInput`,
`SaveLoadMenuFullScreenInit`; `ChallengeMenu.cpp`: `ChallengeMenuInit`, `setGeneralBio`, `setEnabledButtons`; code rewritten, nothing copied).

### 13.1 Which screen hides what

`Shell::push` shuts down the upper screen, whose shutdown ends with `layout->hide(TRUE)` (`MainMenuShutdown` → `shutdownComplete`), and only then creates the new one: the main menu is **gone**, not underneath. Not
everything is a `push`:

| Opens | Path in the original | Hides the screen underneath |
|---|---|---|
| Skirmish setup, credits, challenge menu, load list (`SaveLoad.wnd`) | `TheShell->push(...)` | yes (`HidesScreensBelow = true`, the default) |
| Options | `getOptionsLayout` + `runInit` + `hide(FALSE)`, `OptionsMenuParent` is `SEE_THRU` | no: the main menu stays visible behind it (`OptionsScreen`) |
| Map selection | `winCreateLayout("SkirmishMapSelectMenu.wnd")` + `hide(FALSE)` | no, but `showSkirmishGameOptionsUnderlyingGUIElements(FALSE)` hides in the setup `MapWindow`, `StaticTextTeam/Faction/Color`, the fields `ComboBoxTeam/Color/PlayerTemplate0..7`, `TextEntryMapDisplay`, `ButtonSelectMap`, `ButtonStart`, `StaticTextMapPreview` and locks Reset/Back; OK and Back restore them (`SkirmishSetupScreen.SetMapPickerOpen`) |
| Quit dialog, error dialog | `QuitMessageBoxYesNo`/`MessageBoxOk` | no (`MessageBoxScreen`) |
| Mission result above the banner | – | no (addition of this shell: the banner is the background, `MissionResultScreen`) |

`GameShell.BuildDrawList` draws from the lowest screen that, counted from the top, is the first with `HidesScreensBelow` (`FirstDrawnScreen`) up to the top; transition substitute areas and statistics count only these.
Only the topmost screen received input anyway. **Background layer:** not necessary. Frame, ruler and logo do not lie in a layer that stays – the original hides the whole main menu layout
(`MainMenuParent`, `MainMenuRuler`, `Logo`, `MapBorder*`), and the replacing layouts bring their ruler along themselves (`IMAGE: MainMenuRuler` at the root of `SkirmishGameOptionsMenu.wnd`, `SaveLoad.wnd`,
`ScoreScreen.wnd`); what lies behind is the 3D shell map in the original (§11, deferred: here black). `ChallengeMenu.wnd` brings a background of its own (world map).
When the hiding screen is closed, the main menu runs its init again (`doPop` → `runInit`): `OnRevealed` resets it to the root group, now also without animation (`LeaveFor` remembers the departure).

### 13.2 Why `SinglePlayerMenu.wnd` no longer exists

`SinglePlayerMenu.wnd`/`SinglePlayerMenu.cpp` are a legacy of the first Generals: the file is shipped, `SinglePlayerMenuInit` & co. are entered in `FunctionLexicon`, but **nothing in Zero Hour opens them**
(`Shell::push` names them nowhere; the hits in `MainMenu.cpp` are commented out). The path after "USA" in the original: `GBM_SELECTED` of `ButtonUSA/GLA/China` sets the campaign, plays `MainMenuFactionUS` (set and brought to the
final state with `remove(…, TRUE)`), hides `WinFactionUS`, plays `MainMenuSinglePlayerMenuBackUS` backwards and `MainMenuDifficultyMenuUS` – the **difficulty question is part of `MainMenu.wnd`**
(`MapBorder4` with `StaticTextSelectDifficulty`, `ButtonEasy/Medium/Hard/DiffBack`, the large faction logo flies into `WinGrowMarker` via `MAINMENUSCALEUP`). Easy/Medium/Hard call `prepareCampaignGame` →
`MainMenuDifficultyMenuBack` backwards, `FadeWholeScreen`, then the campaign begins; `ButtonDiffBack` (`diffReverseSide`) plays `…MenuUSBack` backwards and `MainMenuSinglePlayerUSAMenuFromDiff`. Challenge asks the same
(`…Training`) and then pushes `ChallengeMenu.wnd` (`setupGameStart` with `launchChallengeMenu`, the difficulty goes to `TheChallengeGenerals`). `DifficultySelect.wnd` (radio buttons) is likewise unused. "Load" is the
button **Load** of the main menu (`MapBorder3`: `ButtonLoadGame` → `SaveLoad.wnd`); Zero Hour never shows the quick-save pairs of the factions (`showSelectiveButtons`). `MainMenuScreen` reproduces this
(`OpenDifficulty`, `ChooseDifficulty`, `CloseDifficulty`; group names as the table `Factions`); `CampaignFactionScreen`/`DifficultySelectScreen` and the two layout names are removed. The state of rest without animation
corresponds to that of the groups (test: `Dump` of both byte-identical, `ShellTransitionTests.CampaignButtons`). One peculiarity of the data remains visible: `MainMenuSinglePlayerMenuBackTraining` fades the title logo out (`WINFADE`
backwards), and `…TrainingMenuFromDiff` does not bring it back – for Challenge the logo is missing on the difficulty question and on going back until the return to the main menu.

### 13.3 Load list and Escape

`SaveLoadMenuInput` is attached to the child `SaveLoadMenu`, not to the root; the layout init focuses it (`winSetFocus`). Without focus `UiWindowTree` fell back to the topmost root and found no callback there, Escape only worked
after a click on a gadget. New: `UiWindowTree.KeyboardHome` – the window that gets keys without gadget focus as long as it is visible (unlike a gadget focus it survives being hidden by a transition group,
`FULLFADE` of `SaveLoadMenu`); `LoadGameScreen` sets it. All other shell screens have the callback at the root (checked against the files) and do not need it. In the original all these input functions handle
only `KEY_ESC`.

### 13.4 Further text findings along the way (not subject of the three briefs)

* **Map list**: `ListboxMap` has two columns (5 % success image, 95 % name); the names were in the first and were cut off after two letters.
* **Start position markers** of the map preview (`ButtonMapStartPosition0..7`): hidden at the beginning in the original and placed by `positionStartSpots`; here they stood as a diagonal over the preview text. The catalogue knows the number of
  start positions, not their location: the markers stay hidden (open).
* **Challenge menu**: biography (`GeneralsBioParent`) and Play are hidden until the selection (`ChallengeMenuInit`), the four lines (`BioNameEntry`, `BioDOBEntry`, `BioBirthplaceEntry`, `BioStrategyEntry` – repurposed in the original for
  name, rank, branch of service, tactics) show the labels of the persona from `ChallengeMode.ini` (`BioNameString`, `BioRankString`, `BioBranchString`, `BioStrategyString`; `CampaignCatalog.Personas`) at once instead of letter by letter
  (`updateBio`), positions that cannot be started are hidden (`setEnabledButtons`), and the buttons carry the medallions of the player template (`MedallionRegular/Hilite/Select`) in their image size (`winSetSize`) – before, the menu was
  invisible (the buttons have no image in the layout) and showed the placeholders `GUI:BioNameEntry` …. For this, new in `CnCNew.Ui`: `UiWindow.SetDrawImage`/`DrawImage` (replace the image per draw state), `SetDesignSize`.
  Not implemented: portrait (`BioPortrait`), taunt/preview sounds, the introductory sequence of the buttons.

### 13.5 Golden values and tool

`baselines/screens.txt`: **24 of 54** values newly recorded (4 screens × 6 resolutions): `Credits`, `SkirmishSetup`, `SkirmishMapSelect`, `SkirmishSetup-MapChosen`. Causes: the main menu (logo, column buttons, faction logos) is
no longer drawn under these screens; in addition, in setup and map selection the hidden start position markers, under the map selection the hidden fields of the setup and the name column of the list. Unchanged: `MainMenu`,
`MainMenu-SinglePlayer`, `MatchLoading`, `ScoreScreen`, `Options`, `QuitConfirm` (options and quit dialog lie above the main menu in the original) and the HUD hash. The hashes prove reproducibility, not correctness.
For this `tools/CnCNew.UiRenderTool` draws `flow-skirmish-after-map-back`, `flow-main-after-skirmish`, `flow-load-replay-menu`, `flow-load-game`, `flow-challenge-difficulty`, `flow-challenge-menu(-picked)` and
`flow-campaign-difficulty` (1280×720 and 1920×1080).

### 13.6 Not verified (a human has to look)

* That the pages look in the window as in the original: what has been checked is draw lists, states and individual PNGs of an offscreen renderer, no original image and no window.
* The **difficulty question in motion** (logo flight, typing of "SELECT DIFFICULTY", way back with `…FromDiff`) and the absence of the title logo for Challenge (peculiarity of the data, not seen against an original).
* That the **options** really lie above the **visible** main menu buttons in the original: that follows from the code (no `push`, `SEE_THRU`), not from an image. If the original does not show them, `OptionsScreen.HidesScreensBelow` would have to become true.
* The **map selection**: which fields of the setup stay visible (only the name column and the lower fields) is read from `showSkirmishGameOptionsUnderlyingGUIElements`, not seen.
* The **challenge medallions** (size, highlight, selected medallion) and the positions on the world map.
* How conspicuous the black behind it appears without the 3D shell map of the original.

## 14. The skirmish setup is a room (Phase 25)

Since Phase 25 a local skirmish runs through the relay like a network match (`docs/NETWORK-PROTOCOL.md` §7): `LiveMatch.Create` opens an embedded server, the setup becomes
the settings of its room, the viewer takes his seat, and the server starts the match and hands back the match description. The setup screen is that room's face, for one
human:

| Box of `SkirmishGameOptionsMenu.wnd` | Travels as | Effect |
|---|---|---|
| `ComboBoxPlayerTemplate<n>` (army) | slot faction; "Random" as an empty name | "Random" is decided by the seed of the description on every machine alike, not by the screen |
| `ComboBoxTeam<n>` | slot team (0: none, 1 to 4) | slots of one team are allies in the simulation (they do not attack each other and win together) |
| `ComboBoxColor<n>` | slot colour index | **carried, not drawn**: the presentation still takes a player's colour from his faction (`TeamColors.Resolve`) |
| `ComboBoxPlayer<n>` (open, closed, computer army) | whether the slot takes part | open and closed slots take no part; with the original AI the three armies are its strengths |
| `ComboBoxStartingCash` | start money | as before |
| (no box) | start position | the slots that take part take the map's start positions in their order; the layout's start position markers stay hidden |

`MatchSetup.Details` holds team, colour and start position per slot; `MatchSetup.LocalSlot` names the viewer's slot (0 from the screen). Nobody else can join this room:
the screen for joining over a network is Phase 29. Test: `ShellTests` (the team and colour boxes arrive in the setup), `LiveMatchTests` (a viewer in the second slot, team
and colour in the description, allies in the simulation). Not seen by a human.
