# UI-WND – window layouts, scaling, runtime and draw list

Package B2 of the presentation layer (`src/CnCNew.Ui/Wnd`, `Layout`, `Runtime`; tests in `tests/CnCNew.Ui.Tests/WndTests.cs`).
`CnCNew.Ui` depends only on `CnCNew.Data` (currently without types used from it): no Stride, no simulation, no
`CnCNew.Content`, no archives. Parsers take `string`, `ReadOnlySpan<byte>` or `Stream`; the caller supplies the files.
The library reads no wall time, generates no random numbers and renders nothing itself.

## 1. Entry points for integrators

| Task | API |
| --- | --- |
| Parse `.wnd` | `WndParser.Parse(stream \| bytes \| text, fileName)` → `WndLayout` (windows, `WndDiagnostic`s, key count) |
| Header templates | `WndHeaderTemplateTable.Parse(text, "Data/English/HeaderTemplate.ini")` |
| Window tree | `new UiWindowTree(viewport, UiScaleOptions, new UiTreeServices(textMeasure) { Images, Text, HeaderTemplates, DefaultTooltipDelayMs })`, `AddLayout(layout)`, `RemoveLayout(fileName)`, `SetViewport(viewport, options)` |
| Search | `FindByName("MainMenu.wnd:ButtonSinglePlayer")` (case matters, the first created window wins) |
| Input | `MouseMove/MouseDown/MouseUp/MouseWheel`, `KeyDown(UiKey, UiModifiers)`, `Character(char)`, `AdvanceTo(ms)`; return value `true` = the UI consumed the event |
| Messages | `Register(callbackName, Action<UiWindowTree, UiEvent>)`, `RegisterTooltip(name, Func<…, string?>)`, `Dispatch()` → `UiDelivery`s, `UnregisteredCallbacks`, `Post(kind, source)` |
| Gadget state | `UiWindow.SetHidden/SetEnabled/SetText/SetTextLabel/SetChecked/SetProgress/AddListRow/SetListSelection/AddComboEntry/SetComboSelection/SetSliderValue/SetEntryText/SelectTab` (silent, like the `Gadget*Set*` helpers of the original) |
| Drawing | `BuildDrawList()` → `UiDrawList` (commands + `UiDrawStatistics`, `Dump()` for goldens) |
| Renderer interfaces | `IUiTextMeasure` (width, line height), `IUiImageLookup` (size of a mapped image, e.g. via the `MappedImageTable` of package B1), `IUiTextSource` (labels such as `GUI:Options`) |

## 2. Format (checked against the real data)

Grammar after `GameWindowManagerScript.cpp` (`winCreateFromScript`, `parseLayoutBlock`, `parseWindow`, `parseChildWindows`,
table `gameWindowFieldList`), rewritten:

- First statement `FILE_VERSION = 2;`, from version 2 a block `STARTLAYOUTBLOCK … ENDLAYOUTBLOCK` with `LAYOUTINIT`,
  `LAYOUTUPDATE`, `LAYOUTSHUTDOWN` (names without quotation marks; `[None]` → `null`).
- Keys are tokens separated by whitespace; a value reaches from the token after `=` to the next `;`. Line endings
  (`\r\n`/`\n`) and tabs are meaningless (test with a CRLF/tab variant serialised byte-identically). `;` is the end of a statement,
  not a comment marker – the WND files contain no comments.
- `WINDOW` begins a window: first `WINDOWTYPE`, then `SCREENRECT = UPPERLEFT: x y, BOTTOMRIGHT: x y, CREATIONRESOLUTION: w h`,
  after that arbitrary fields, `CHILD` opens the list of children (before every child `WINDOW` there is `CHILD` again), `ENDALLCHILDREN`
  closes it, `END` closes the window. File order = Z order (later = on top), like `addWindowToParent` +
  `drawWindow` (children drawn backwards).
- Fields: `NAME` (`File.wnd:Name`), `STATUS`/`STYLE` (`+` lists over `WindowStatusNames`/`WindowStyleNames`, `NULL` = empty),
  `SYSTEMCALLBACK`, `INPUTCALLBACK`, `TOOLTIPCALLBACK`, `DRAWCALLBACK`, `HEADERTEMPLATE` (quotation marks, `[None]`/`[NONE]` → `null`),
  `FONT = NAME: "…", SIZE: n, BOLD: n`, `TOOLTIPDELAY`, `TOOLTIPTEXT`, `TEXT`, `TEXTCOLOR` (six RGBA values),
  `IMAGEOFFSET`, the nine entries `IMAGE: name, COLOR: r g b a, BORDERCOLOR: r g b a` of every `*DRAWDATA` family
  (`ENABLED/DISABLED/HILITE` plus list box, slider thumb and combo box parts), gadget data `LISTBOXDATA`
  (optional `SCROLLIFATEND`, `COLUMNSWIDTH` only with more than one column), `COMBOBOXDATA`, `SLIDERDATA`, `RADIOBUTTONDATA`,
  `STATICTEXTDATA`, `TEXTENTRYDATA`, `TABCONTROLDATA`. Obsolete keys `DATA` and `TOOLTIP` are recognised and reported as a
  compatibility case.
- Window types: `USER, PUSHBUTTON, RADIOBUTTON, CHECKBOX, VERTSLIDER, HORZSLIDER, SCROLLLISTBOX, ENTRYFIELD, STATICTEXT,
  PROGRESSBAR, TABCONTROL, TABPANE, COMBOBOX`; unknown types are kept as `WndWindowType.Unknown` with the original name.
- `255 255 255 0` is `GAME_COLOR_UNDEFINED` (`Color.h`); the draw list omits colours with alpha 0.
- `TEXT` of an `ENTRYFIELD` is literal text (`setWindowText` does not translate it), otherwise a label.

**Diagnostics.** Every deviation is a `WndDiagnostic(Code, Severity, File, Line, Message)`. `Compatibility` = an explained peculiarity
occurring in the shipped files that is treated exactly as in the original; `Unknown` and `Error` are
unexplained and must not occur in the real data. Explained cases:

| Code | Occurrences | Behaviour (like the original) |
| --- | --- | --- |
| `status-disabled-flag` | 2 (`ControlBar.wnd`) | `DISABLED` is not a status name; `parseBitString` ignores it, the window is disabled because `ENABLED` is missing |
| `statement-swallows-next-key` | 4 (`ControlBar.wnd`) | `ENABLEDDRAWDATA` without `;` swallows `DISABLEDDRAWDATA`; `parseDrawData` reads nine entries, the rest is lost, the disabled data stay undefined |
| `label-mismatch` | 6 (`ControlBar`, `Diplomacy`, `GeneralsExpPoints`, `InGameChat`) | `TEXTCOLOR` with `ENABLEDBORDER` in the place of `HILITEBORDER`; the original discards label words unchecked and reads by position |
| `obsolete-data-key`, `obsolete-tooltip-key`, `field-after-children`, `children-closed-by-end`, `child-default-color` | 0 | tested only synthetically |

## 3. Scaling (`UiScaler`, `UiScaleOptions`)

Windows are authored in a creation resolution (`CREATIONRESOLUTION`, real data: 800x600 and 1920x1080) in absolute
screen coordinates. Both edges are always mapped; the size is their difference, common edges stay
common. Mapping per axis `offset + floor(v * numerator / denominator)` in exact integer arithmetic (`long`, rounding towards
minus infinity). The original computes `(Int)(x * (Real)W / (Real)cw)` in `float` and lies one pixel lower for some values
(100 · 2.4f = 239.99998 → 239 instead of 240); this deviation is deliberate.

| Mode | Rule |
| --- | --- |
| `Stretch` (original, `parseScreenRect`) | x and y independently: `W/cw`, `H/ch` |
| `Uniform` | one factor `s = min(W/cw, H/ch)`; the creation area is centred (letterbox/pillarbox) |
| `Anchor` | Group rule: a window that covers the whole creation area (backgrounds) is stretched; its children form groups of their own. Every other group root chooses one anchor per axis that all descendants share: full extent → stretch, otherwise by the position of the centre (first third → start, last third → end, otherwise centre) with factor `s`. Offsets are `0`, `(W − floor(cw·s))/2` or `W − floor(cw·s)`. |

Fonts: `Stretch` follows the original – header template fonts grow with `GlobalLanguage::adjustFontSize`
(classically `1 + (W/800 − 1) · ResolutionFontAdjustment`, limited to [1, 2], rounded down; `Language.ini`: 0.7), `FONT`
of the window stays unchanged. `Uniform`/`Anchor` scale the `FONT` of the window with the uniform factor of its
creation resolution, a header template font by contrast with the uniform factor against 800x600 – template point sizes
always refer, as in `adjustFontSize`, to 800 pixels of width, also in a layout authored at 1920x1080 (otherwise
its texts shrink to 800/1920). After that the accessibility factor `TextScale` multiplies; minimum size 1 (`decimal`, rounded down).
`MinimumHitTarget` (pixels, 0 = off) enlarges the hit area of interactive gadgets symmetrically without moving them;
children stay clipped to the parent area.

Checked invariants (table 640x480, 800x600, 1024x768, 1280x720, 1280x800, 1600x900, 1920x1080, 2560x1080, 3440x1440,
3840x1080, 1080x1920, all three modes):

- Every visible window authored within the creation area stays in the viewport (real data: 0 violations; 25 windows per pass
  are authored outside and exempt).
- Overlap relations (pairs of the same creation resolution): `Stretch`/`Uniform` identical as long as no axis shrinks;
  when scaling down, overlaps can only be lost (monotone mapping), new ones never arise. `Anchor` can dissolve overlaps
  between different groups (real data 1080x1920: 125 pairs, other resolutions 1 each), but creates no new ones.
- The hit test of the four corner pixels and of the centre of every visible, enabled gadget (authored points mapped through the transformation of the
  gadget) yields the same window as in the creation resolution: exactly for `Stretch`/`Uniform` if no axis
  shrinks (strictly monotone mapping). Real data: 62,315 samples per mode, 0 deviations without shrinking; with shrinking
  (e.g. 640x480: 699) or in files with mixed creation resolution (`Diplomacy`, `GeneralsExpPoints`,
  `DisconnectScreen`, `MessageBox`, `QuitMessageBox`) the deviations are counted and printed. `Anchor` without shrinking:
  30 deviations at 1080x1920, otherwise at most 1 per resolution (overlaps across groups).
- Re-layout is reproducible; two test runs yield identical output apart from time measurements.

## 4. Runtime (`UiWindowTree`, `UiWindow`)

- **State:** hidden, enabled (own bits as in the original; effective via the parents), hilited, selected, focus,
  text (label or plain text), gadget states (`UiListBoxState`, `UiSliderState`, `UiComboBoxState`, `UiTextEditState`,
  `UiTabControlState`, progress in percent).
- **Z order:** roots in insertion order, levels `BELOW` < normal < `ABOVE` (`winRepaint`); children in file order.
- **Hit test** (`GameWindow::winPointInChild`): deepest visible, enabled window; later siblings lie on top;
  children only within their parent area; disabled and hidden windows are skipped; containment half-open
  `[x, x+w)` (the original checks `<=`). Input target: first the open combo list ("lone window"), `NOINPUT` windows
  swallow the event except as part of a combo box (`winProcessMouseEvent`).
- **Mouse:** grab window on left press; the release counts only within the grab window (click = press and release in the
  button). Enter/leave (`GBM_MOUSE_ENTERING/LEAVING`) and the highlight only for gadgets with `MOUSETRACK` whose
  input function handles `GWM_MOUSE_ENTERING` (button, check box, radio, slider, list box, combo box, text entry) –
  not for static text, progress bar and tab control, although the files give them `MOUSETRACK` and `HILITE*` data.
  Leaving clears the highlight unconditionally (also on the focused gadget), hiding or disabling
  clears hilite and pressed, `PointerLeft()` reports to the tree that another screen has taken over the mouse. During
  a grab the hover tracking rests as in the original. A click takes the keyboard focus only with combo box, list box
  and text entry (`winSetFocus` in their input functions), not with buttons, check boxes, radios and sliders.
- **Gadgets:** push button (hover, pressed, click; `ON_MOUSE_DOWN`, `CHECK_LIKE`, `RIGHT_CLICK`), check box (toggle on
  release in the window), radio groups (group + file like `unselectOtherRadioOfGroup`), slider (dragging, a click jumps,
  arrow keys; thumb 13 or 16 units of the creation resolution, scaled), list box (selection, multiple selection,
  double click via `DoubleClickMs` of the tree clock, right click, wheel, scrollbar arrows/track, column widths from
  `COLUMNSWIDTH`, `LENGTH` with `AUTOPURGE`), combo box (dropping down, selection, Escape/outside click closes, `MAXDISPLAY`,
  editable), text entry (caret, insert/delete, shift selection, filters `NUMERICALONLY`/`ALPHANUMERICALONLY`/`ASCIIONLY`,
  `MAXLEN`, `SECRETTEXT`), progress bar, tab control (tab headers along the top edge, exactly one pane visible).
- **Keyboard/accessibility:** Tab/Shift+Tab walk through all focusable gadgets (visible, effectively enabled, neither
  `NOFOCUS` nor `NOINPUT`) in reading order (top edge, then left edge, then creation order; no
  row bundling). Enter/Space trigger buttons, check boxes, radios; arrow keys move the focus between buttons
  (`winNextTab`/`winPrevTab`), change slider/list/combo selection and move the caret. Focused buttons are hilited
  (`GWM_INPUT_FOCUS`), that is the visible focus mark.

### Message and callback model

Gadgets send to their owner = parent window (root gadgets to themselves). `Dispatch()` delivers the waiting events
in order (also those created by handlers) to the `SYSTEMCALLBACK` of the receiver:

- `[None]`, `GameWinDefaultSystem`, `Gadget*System` → `NoCallback` (the default or gadget system function ignores them).
- `PassMessagesToParentSystem` → on to the parent window; `PassSelectedButtonsToParentSystem` → only `GBM_SELECTED`,
  `GBM_SELECTED_RIGHT`, `GBM_MOUSE_ENTERING/LEAVING`, `GEM_EDIT_DONE` on, otherwise `Filtered` (`GameWindowManager.cpp`).
- registered name → `Handled`; unknown name → `Unregistered`, counted in `UnregisteredCallbacks` (never silent).
- Unconsumed keys go as `UiEventKind.Key` from the focus (without focus from the topmost visible root) upwards to the
  next `INPUTCALLBACK` (`winProcessKey`): `GameWinBlockInput` → `Blocked`, `GameWinDefaultInput`/`[None]` → on,
  unknown names are counted and passed on (in the original: no function).

Real data: `GBM_SELECTED` of all 440 push buttons (83 files, a tree of its own per file) ends up at 49 different menu callbacks (e.g. `MainMenuSystem` 30,
`SkirmishGameOptionsMenuSystem` 13, `ControlBarSystem` 68), 16 end without a callback. The shell has to register these names.

Messages sent (`UiEventKind`): `GBM_MOUSE_ENTERING`, `GBM_MOUSE_LEAVING`, `GBM_SELECTED` (button, check box, radio;
value 1/0 with toggles), `GBM_SELECTED_RIGHT`, `GSM_SLIDER_TRACK`, `GSM_SLIDER_DONE`, `GLM_SELECTED`, `GLM_DOUBLE_CLICKED`,
`GLM_RIGHT_CLICKED`, `GCM_SELECTED`, `GCM_UPDATE_TEXT`, `GEM_UPDATE_TEXT`, `GEM_EDIT_DONE`, `GGM_FOCUS_CHANGE`, key (`GWM_CHAR`).

Not sent (deliberately): `GGM_LEFT_DRAG` (dragging of movable `DRAGGABLE` windows is not implemented), `GGM_SET_LABEL`,
`GGM_GET_LABEL`, `GGM_RESIZED`, `GGM_CLOSE`, `GBM_SET_SELECTION`, `GSM_SET_SLIDER`, `GSM_SET_MIN_MAX`, `GLM_ADD_ENTRY`,
`GLM_DEL_ENTRY`, `GLM_DEL_ALL`, `GLM_SET_SELECTION`, `GLM_GET_SELECTION`, `GLM_TOGGLE_MULTI_SELECTION`, `GLM_GET_TEXT`,
`GLM_SET_UP_BUTTON`, `GLM_SET_DOWN_BUTTON`, `GLM_SET_SLIDER`, `GLM_SCROLL_BUFFER`, `GLM_UPDATE_DISPLAY`, `GLM_GET/SET_ITEM_DATA`,
`GCM_ADD_ENTRY`, `GCM_DEL_ENTRY`, `GCM_DEL_ALL`, `GCM_GET_TEXT`, `GCM_SET_TEXT`, `GCM_EDIT_DONE`, `GCM_GET/SET_ITEM_DATA`,
`GCM_GET/SET_SELECTION`, `GEM_GET_TEXT`, `GEM_SET_TEXT`, `GPM_SET_PROGRESS` – the setters/getters are methods on `UiWindow`;
raw `GWM_*` input messages (`GWM_CREATE`, `GWM_DESTROY`, `GWM_SCRIPT_CREATE`, `GWM_INPUT_FOCUS`, `GWM_IME_*`) are replaced by
the input methods.

Deliberate deviations: `GSM_SLIDER_DONE` also for horizontal sliders (the original sends it only with the vertical thumb) and
after arrow keys; the arrow keys of the horizontal slider are not swapped as in `GadgetHorizontalSliderInput`;
vertical sliders have the maximum at the top; Enter/Space trigger on buttons already on pressing.

### Tooltips

Pure state machine over `AdvanceTo(ms)` (the clock must not run backwards). A window shows its tooltip if it
has been hovered for `TOOLTIPDELAY` ms (≥ 0) or `DefaultTooltipDelayMs` (`Mouse.ini TooltipDelayTime`, retail 800);
a change of window restarts, a held button suppresses it. Precedence like `winProcessMouseEvent`: a
`TOOLTIPCALLBACK` before `TOOLTIPTEXT`; `GameWinDefaultTooltip` shows nothing (9 windows in the 83 files), unregistered names are counted once per
hover; labels are resolved via `IUiTextSource`, otherwise delivered with the label as text.

## 5. Draw list (`UiDrawList`)

Commands: `UiFillRect`, `UiBorderRect` (1 px inside), `UiImageDraw` (mapped image name, target rectangle, tint, mirroring),
`UiTextDraw` (one set line: text, label key, font, colour, shadow colour = `TEXTCOLOR` border, position,
alignment, clip), `UiPushClip`/`UiPopClip`, `UiCustomDraw` (unimplemented `DRAWCALLBACK`, to be drawn by the host).
Order like `winRepaint`/`drawWindow`; `SEE_THRU` skips only the window itself; `W3DNoDraw`/`GameWinDefaultDraw`
draw nothing; `W3DGameWinDefaultDraw` draws the default background. State → data set: disabled (own
`ENABLED` bit missing) → `DISABLED*`, hilited → `HILITE*`, otherwise `ENABLED*`. Gadget rules (sources `W3DGameWindow.cpp`,
`W3DPushButton.cpp`, `W3DCheckBox.cpp`, `W3DRadioButton.cpp`, `W3DStaticText.cpp`, headers `Gadget*.h`):

- Default: with `IMAGE` image 0 (plus `IMAGEOFFSET`), otherwise border and fill of entry 0.
- Push button: with a centre image (enabled entry 5) three-part left 0/1, centre 5/3, right 6/4 (selected), otherwise one image
  (enabled+selected takes hilite 1 like the original); colour variant entry 0/1; text centred, wrapping to the width,
  `WRAP_CENTERED` centres every line.
- Check box: box on the left (height − 2·3), entries 1/2; in the colour variant additionally a filled check mark; text from x + height.
- Radio: three-part 0/1/2, selected hilite 3/4/5.
- Static text: wrapping at width − 10, left 7 or centred, vertically centred, clip to the window.
- Text entry: three-part 0/2/1, selection in the hilite text colour, caret (1 px) in focus; list box: rows (row height =
  `LineHeight` of the font), selection three-part 1/3/2 or colour 1, cell text per column, scrollbar from the
  `LISTBOX*`/`SLIDERTHUMB*` data; combo box: edit field, arrow button, open list last (on top); horizontal slider with
  `IMAGE` (`W3DGadgetHorizontalSliderImageDraw`): centred row of square boxes (edge = width of the filled
  image at display scale, spacing 2), up to the value disabled 0, after that disabled 1, when highlighted a row of
  hilite 0 behind it; without `IMAGE` border and fill of entry 0; vertical slider with `IMAGE` three-part 0/2/1. Thumb of the
  horizontal slider 13x16, 10 below the top edge (`gogoGadgetSlider`), from `SLIDERTHUMB*` by state, while dragging
  entry 1; progress: background 0/2/1, bar 4/6/5 in the clip of the fill width (colour variant entry 0 and 4).
- Centre parts are tiled under a clip (at most 256 tiles, otherwise stretched). Unknown image sizes are counted in
  `UnresolvedImageSizes` (ends then half the height, centre stretched); unresolved labels in `UnresolvedLabels`.

Golden tests compare `Dump()` with literal expectations (background, pressed button, wrapped static text,
check box, custom draw; three-part button with tiles).

## 6. Real-data numbers (`dotnet run --project tests/CnCNew.Ui.Tests -- --content-dir game/content`)

- *As of 9 October 2026 (content from the Steam master installation, `docs/UI-VANILLA-FIT.md`): 80 files, all `*.wnd` and all 800×600; the three variants below existed only in the earlier, modded content, and the number 83 in this document is historical.*
- `Window/`: 83 files = 80 `*.wnd` + 3 variants (`OptionsMenu.wnd_hideip`, `NetworkDirectConnect.wnd_hideip`,
  `GameSpyLoginProfile.wnd_hidemail`), all parsed; the game does not load the variants via their name.
- 1,814 windows (of which 1,675 in the 80 `*.wnd`): STATICTEXT 582, USER 450, PUSHBUTTON 440, COMBOBOX 123, CHECKBOX 66,
  SCROLLLISTBOX 40, ENTRYFIELD 39, PROGRESSBAR 34, RADIOBUTTON 24, HORZSLIDER 15, VERTSLIDER 1; no TABCONTROL/TABPANE.
  Creation resolution 800x600: 1,467, 1920x1080: 347.
- Keys (excerpt): WINDOW 1,814, CHILD 1,731, ENDALLCHILDREN 261, TEXT 880, TOOLTIPTEXT 159, STATICTEXTDATA 582,
  COMBOBOXDATA 123, LISTBOXDATA 40, TEXTENTRYDATA 39, RADIOBUTTONDATA 24, SLIDERDATA 16, HEADERTEMPLATE 1,782
  (the test prints the complete list).
- Diagnostics: 12, all explained (table in section 2), 0 unexplained. All windows carry a decorated name.
- `HeaderTemplate.ini`: 19 templates, 0 diagnostics, no unknown template names; no window without a font. In one tree
  with all layouts: 211 duplicate names (variants and multiple files; `FindByName` yields the window created first).
- Run times (Debug, single measurement, JIT included): parsing of all 83 files ≈ 0.4 s; 2,739 trees (83 × 11 resolutions
  × 3 modes) ≈ 0.08 s; draw lists of all files per resolution and mode 15–30 ms.
- Draw list 1920x1080 `Stretch`: 8,357 commands (images 6,443, text 703, borders 326, fills 254, clip 305/305,
  custom 21); 1280x720 `Stretch`: 6,412; `Uniform`/`Anchor` 7,008 and 5,492 respectively (smaller tile count). With an image-size fake of
  16x16 and without a text source (671 unresolved labels).
- `DRAWCALLBACK`s to be drawn by the host: `W3DDrawMapPreview` 8, `W3DMainMenuDraw` 3, 1 each of `IMECandidateMainDraw`,
  `IMECandidateTextAreaDraw`, `W3DCommandBarBackgroundDraw`, `W3DCommandBarForegroundDraw`, `W3DCommandBarGenExpDraw`,
  `W3DCommandBarHelpPopupDraw`, `W3DCreditsMenuDraw`, `W3DLeftHUDDraw`, `W3DPowerDraw`, `W3DRightHUDDraw`.

## 7. Limits

- `ShellMenuScheme` (`Data/INI/ShellMenuScheme.ini`, in the `CompiledDefinitionGraph` as a compatibility definition) is not
  read here; menu transitions (`WindowTransitions.ini`, `LAYOUTINIT` animations) are the task of the shell (`docs/GAME-SHELL.md` §12; the handler lives in `CnCNew.Ui/Transitions`).
- Modal windows (`winSetModal`), mouse capture by callbacks, dragging of `DRAGGABLE` windows, IME, video buffers,
  `USE_OVERLAY_STATES`/`FLASHING`/`NOT_READY` overlays, hotkey text and clock overlays of the control bar are not implemented.
- The old border `WIN_STATUS_BORDER` (`winDrawBorder`) is not drawn, only counted (`BorderStatusWindows`; real data: 37 drawn windows per resolution, hidden ones not counted).
- Tab control only minimal (tab headers at the top, `TABORIENTATION`/`TABEDGE`/`PANEBORDER` unused); none occurs in the real data.
- The colour variants of check box, radio, slider and scrollbar are simplified; scrollbar track and thumb one-part.
- Reading order without row bundling; row height = `LineHeight` without extra spacing; text wrapping only at spaces.
- Image sizes, font metrics and translations are supplied by the host; `UniformImages` in the real-data test is a fake.
- `CnCNew.Ui` uses no new NuGet dependency.
