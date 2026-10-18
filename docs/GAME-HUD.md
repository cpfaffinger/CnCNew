# In-match HUD and control surface of the human player (package G2)

This document describes `src/CnCNew.Game/Hud/**`: the Stride-free, headless testable layer that provides for the human player
the whole control and display surface model of a running `LiveMatch`. `MatchController` owns the
`GameInputController` (package C3, `CnCNew.Presentation.Input`) and the control bar HUD bound from the original layouts, converts
`InputEvent`s into simulation commands exclusively through `LiveMatch.Commands` (so that `SkirmishMatchSession.ToReplay()` reproduces
everything the player has done) and delivers per frame a `UiDrawList` (package B, `CnCNew.Ui.Runtime`) as well as
screen-space overlay data. No Stride type occurs; the whole class runs headless in tests.

## 1. Call order

Per rendered frame:

1. `controller.Process(inputEvent)` for every pending device event (mouse, keyboard, loss of focus).
2. `liveMatch.Advance(deltaSeconds)` (package C contract, unchanged except for the additive extensions described below).
3. `controller.Update(deltaSeconds)` – applies the camera intent, rebuilds `CommandBar`/`Hud`, updates the message list and the
   `Overlay`.
4. `controller.BuildDrawList(width, height)` – updates the visibility of the dynamic windows and delivers the finished
   `UiDrawList` including the dynamic cameo/costume/cooldown overlays.

`MatchController.WindowRect(decoratedName)` returns the current screen area of a named window (e.g.
`"ControlBar.wnd:ButtonCommand01"`); with it a host can do hit tests without a scaling calculation of its own, and the tests
(`HudTests.cs`) click real buttons through it.

## 2. Additive extensions to `LiveMatch.cs`

`src/CnCNew.Game/Live/LiveMatch.cs` belongs to package C4; G2 has added only four additional, backward-compatible members:

* `ICommandSink Commands` – lazily built `SkirmishSessionCommandSink(Session, Human)`; the only way in which this layer changes
  the world.
* `Action<SimulationWorld>? BeforeTick` – is called in `Advance`'s tick loop immediately **before** `Session.Step()`.
  `MatchController` hooks `Input.Tick(world)` in here (clean up the selection, track standing orders), as the contract of
  `GameInputController.Tick` requires.
* `event Action<TickResult>? TickStepped` – after every tick with the result (state hash, events). Through this `MatchController` feeds
  `MatchStatsTracker` and the message list.

None of the three fields is called by `LiveMatch` itself if nobody listens; the behaviour without `MatchController` is
unchanged (see `LiveMatchTests.cs`, still green).

## 3. Files

| File | Content |
|---|---|
| `MatchController.cs` | Public API: `Process`, `Update`, `BuildDrawList`, `WindowRect`, events `UiRequest`/`MenuActionRequested`/`MatchEnded`, public states `CommandBar`/`Hud`/`Overlay`/`IsMenuOpen`. |
| `HudAssets.cs` | Loads once `Window/ControlBar.wnd`, `Window/Menus/QuitMenu.wnd`, `Data/<language>/HeaderTemplate.ini`, `Data/INI/ControlBarScheme.ini` and the `LocalizedText`/`MappedImageTable` adapters. |
| `ControlBarScheme.cs` | Standalone, small parser for `ControlBarScheme.ini` (no `=`/`;` grammar, see below). |
| `HudTextAdapters.cs` | `MonospaceTextMeasure` (deterministic substitute measure for headless tests/renderers without real glyph metrics), `MappedImageLookup`, `LocalizedTextSource`. |
| `Overlays.cs` | Screen-space records: `SelectionBoxOverlay`, `UnitMarker`, `RallyPointMarker`, `OrderLine`, `PlacementGhostOverlay`, `HudOverlay`. |
| `MatchResult.cs` | `PlayerMatchStats`, `MatchResult`, `MatchStatsTracker` (see section 7). |

## 4. HUD binding: `ControlBar.wnd` and `QuitMenu.wnd`

`MatchController` builds a `UiWindowTree` with `UiScaleMode.Anchor` (derived from `GameSettings.Display.UiScale`: `Stretch`→
`Stretch`, `Uniform`→`Anchor`, `IntegerMultiple`→`Uniform`) and loads both original layouts into it. `ControlBarParent` covers only the
lower strip (`0,740`–`1920,1080` in 1920×1080 creation space) and is therefore classified by `UiScaler` as an anchor group of its
own: X is stretched, Y anchored at the lower edge, one common uniform scale for both axes. The same
transformation applies to every child window of the file (same creation size, same group), which is why `MatchController`
reuses `ControlBarParent.Transform` also for the money/power rectangles read from `ControlBarScheme.ini`
(`Transform.Map(new WndScreenRect(...))`) instead of computing a scaling of its own.

`QuitMenu.wnd` is loaded along with it and hidden immediately (`QuitMenuParent.SetHidden(true)`); `MetaAction.Options` (Esc, see
`GameInputController.DispatchMeta`) is intercepted by `MatchController` itself (not passed on through `UiRequest`) and
opens/closes the menu, with `LiveMatch.Paused` being set/reset. A click on `ButtonReturn` closes it as well
(`MenuActionRequested(MenuAction.Resume)`); the other four buttons (`ButtonSaveLoad`, `ButtonOptions`, `ButtonRestart`,
`ButtonExit`) raise `MenuActionRequested` with the matching value – their implementation (save/load, options menu, restart,
quit) lies with the shell/host (package G1), not with G2.

The two `ObserverPlayerInfoWindow`/`ObserverPlayerListWindow` windows stay visible in the original file; since this package
serves only the human player, they are hidden firmly during construction.

### 4.1 Input routing: HUD against game world

The keyboard always goes directly to `GameInputController.Process` (the `UiWindowTree` keyboard paths are not used; hotkeys,
control groups, camera keys etc. run entirely through package C3). The only exception: if the menu is open, only `Escape`
is evaluated (closes the menu), all other keys are swallowed so that the paused menu acts modally.

For mouse events `MatchController` decides per button press whether the HUD or the game gets the event:

* If the point lies within `LeftHUD` (the minimap frame), `MatchController` handles it itself (section 6), independent
  of the mouse scheme.
* If the menu is open, every click belongs to the HUD (the game below is frozen).
* Otherwise `UiWindowTree.InputTarget(x, y)` decides: if the click hits a visible, enabled window (e.g. a
  control bar button), the tree gets `MouseDown`/`MouseUp` and `Dispatch()`; otherwise `GameInputController.Process` gets
  the event. `MouseMove` always goes to the tree in addition (for hover/tooltip timing), independent of the result.

Important for callers (and for `HudTests.cs`): which control bar windows are visible/enabled at all is updated exclusively in
`BuildDrawList` (`RefreshDynamicWindows`), because the window states (`ButtonCommandNN` etc.) are set directly from
`CommandBarView`. A click on a control bar button therefore works only after `BuildDrawList`
has run at least once after the last change of selection.

### 4.2 `ControlBarSystem`, `QuitMenuSystem`

`ButtonCommandNN`, `ButtonQueueNN` and the other control bar buttons themselves have `SYSTEMCALLBACK = "[None]"`; the event
travels (`PassSelectedButtonsToParentSystem`) up to `ControlBarParent.SYSTEMCALLBACK = "ControlBarSystem"`, which
`MatchController` registers as the only handler:

| Window name | Effect |
|---|---|
| `ButtonCommand01`…`14` | `CommandBarModel.Activate(world, view, slot, commands)` – construction, unit, sale, special power etc. depending on `CommandButtonInfo.Kind`. |
| `ButtonQueue01`…`09` | `CommandModeController.CancelProduction(producer, index)`. |
| `ButtonCancelConstruction` | `CommandModeController.CancelConstruction(producer)`. |
| `ButtonIdleWorker` | `GameInputController.DispatchMeta(MetaAction.SelectNextIdleWorker, …)`. |
| `ButtonOptions` | opens the menu (like Esc). |
| `ButtonGeneral`, `ButtonPlaceBeacon`, `PopupCommunicator` | not implemented (generals points popup, beacon, chat – outside the scope of the brief); produce a parenthesised message line "(not supported in this build: …)" (`HudMessageFeed.ClientNote`), never a silent failure. |

`QuitMenuParent.SYSTEMCALLBACK = "QuitMenuSystem"` is registered analogously (section 4).

## 5. Dynamic HUD content

The elements drawn by the `.wnd` tree itself (window backgrounds, text according to `TEXT`/`TOOLTIPTEXT`) are not enough: cameo images,
hotkey letters, costs, cooldown/build progress overlays as well as money/power exist in the original only through
`DRAWCALLBACK`s such as `W3DCommandBarBackgroundDraw`, `W3DLeftHUDDraw`, `W3DPowerDraw`, which this project does not implement (yet).
`UiWindowTree.BuildDrawList()` already converts every unknown `DRAWCALLBACK` into a `UiCustomDraw(callback, window, rect)`,
which leaves the complete drawing of these elements to a renderer.

`MatchController.BuildDrawList` uses this mechanism unchanged for the 3D view and the minimap:

* `W3DLeftHUDDraw` (from the original file, for the `LeftHUD` window) is the minimap frame; a renderer draws
  `LiveMatch.Minimap` (through `MinimapImage.Compose`) scaled into it preserving the axes (see `TryMinimapWorld`, section 6, for the
  exactly identical computation rule with which `MatchController` computes clicks back).
* `Viewport3D` is **not** an original name, but an addition of this package: a `UiCustomDraw("Viewport3D", "Viewport",
  rect)` whose rectangle covers the whole area above the control bar (`0,0` to `width, ControlBarParent.Rect.Y`), so that
  a host knows where the 3D scene is to be rendered without recomputing the control bar geometry itself.

For everything else `MatchController` supplements the `UiDrawList` delivered by the tree with additional commands (`BuildDynamicCommands`)
before it is returned:

* **Cameo:** `UiImageDraw(button.ButtonImage, slotRect, …)`, provided the named image exists in `GameData.Images` (if it is missing,
  nothing is drawn – no placeholder, no crash; the original would have had no image here either).
* **No text on the icons.** The original prints neither hotkey letters nor costs onto the command icons (`ControlBar::setControlCommand` empties the
  button text, name, cost and hotkey are in the description); the client used to print them and has abandoned that (Phase 16, `docs/TEXT-AUDIT.md`).
  Instead there are **tooltips** (section 5a).
* **Cooldown/reload:** a semi-transparent rectangle that wipes into the button from the top (`ButtonAvailability.NotReady`,
  `ReadyFraction`); the same wipe logic for the first queue entry (`QueueEntry.Progress`).
* **Queue cameo** per visible `ButtonQueueNN`.
* **Money/power:** the window `ControlBar.wnd:MoneyDisplay` carries the text `GUI:ControlBarMoneyDisplay` (`$ %d`) with the money of the local player
  (`InGameUI::update`) and lies at the money rectangle of the scheme (`MoneyUL/LR`, `ControlBarScheme`; `UiWindow.SetScreenRect`); the layout file has there only the
  placeholder "$$$" in a 100x100 rectangle at the top left, which the client used to show that way. The power bar is a filled bar at the rectangle
  `PowerBarUL/LR`, projected through `ControlBarParent.Transform` (section 4). The power bar is proportional to
  `production / consumption` (clamped to `[0,1]`) instead of a fixed bar, which comes close to the original without needing its exact
  bar artwork.

### 5a. Tooltips (Phase 16)

`MatchController` and `GameShell` attach the pointer tooltip of the window under the pointer to the draw list (`TooltipPainter`, `UiTooltipLayout`;
`Mouse::drawTooltip` in Mouse.cpp): box 20 pixels to the right of the pointer (to the left of or above the pointer at the screen edge), wrap at `TooltipWidth` percent of the
screen width, text faded in from the left over `TooltipFillTime` (highlight color on the leading 15 pixels), style from the `Mouse` block of `Mouse.ini`,
font `TooltipFontName` from `Language.ini` (Arial 8) with resolution adjustment. It appears when the pointer has stood still over the window for `TOOLTIPDELAY` (otherwise 800 ms),
and disappears on every movement.

**Description of the command buttons** (`CommandButtonDescription`, after `ControlBar::populateBuildTooltipLayout` in ControlBarPopupDescription.cpp and
`ProductionPrerequisite::getRequiresList`): the three texts of the original popup – name (`TextLabel`, without `&`), cost (`TOOLTIP:Cost`, only if > 0), description (`DescriptLabel`) – as
lines of the pointer tooltip (`TOOLTIPDELAY = 1`). The original appends to the description, and now the client does too:

| Case | Text | Rule |
|---|---|---|
| Unit/building, too little money | blank line, `TOOLTIP:TooltipNotEnoughMoneyToBuild` ("Insufficient funds") | `canMakeUnit` = `CANMAKE_NO_MONEY`; only with a description label and a selected object |
| Unit/building or upgrade, queue full | blank line, `TOOLTIP:TooltipCannotPurchaseBecauseQueueFull` | `CANMAKE_QUEUE_FULL` or `getProductionCount() == MAX_BUILD_QUEUE_BUTTONS` |
| Prerequisite missing | new line `CONTROLBAR:Requirements` ("Requires: %s") with one entry per unfulfilled `Prerequisites` line, separated by ", ": display name of the object; for alternatives the **last two** with `CONTROLBAR:OrRequirement` ("A or B" – `getRequiresList` names only these two); for a science line `CONTROLBAR:GeneralsPromotion` | `getRequiresList` per prerequisite; `Ignore_Prerequisites` is not observed there |
| Upgrade already present (player or selected object) | `PurchasedLabel` of the button, otherwise `TOOLTIP:AlreadyUpgradedDefault`, **instead of** the description, without cost | `hasUpgradeComplete`/`hasUpgrade` |
| Upgrade, science of the button missing | new line "Requires: General's Promotion" | `getScienceVec`, `hasScience` |
| Overcharge button | new line `TOOLTIP:TooltipNukeReactorOverChargeIsOn`/`…IsOff` | `OverchargeBehaviorInterface::isOverchargeActive` |

In addition the two windows that the original describes in the same popup: `ControlBar.wnd:MoneyDisplay` (`CONTROLBAR:Money`, `CONTROLBAR:MoneyDescription`) and `ControlBar.wnd:PowerWindow`
(`CONTROLBAR:Power`, `CONTROLBAR:PowerDescription` with production and consumption of the player); the power window now lies, like the money window, at the rectangle of the scheme
(`ControlBarScheme::init`), where the bar is drawn. The missing lines of a template are supplied by `SimulationQueries.MissingPrerequisites` (the same test as
`PrerequisitesSatisfied`, per line).

**Greyed-out buttons explain themselves:** the tooltip search of the original ignores the enabled state (`GameWindowManager::winProcessMouseEvent`:
`winPointInAnyChild( x, y, TRUE, TRUE )`), the input does not. `UiWindowTree.Tooltip` therefore first asks the deepest visible window under the pointer if it (or a window
above it) is disabled; before, a disabled button never got a tooltip – exactly the buttons that say "Requires: …". Input and highlighting stay unchanged with the
enabled window (`WndTests.TooltipStateMachine`).

**Side finding power window:** `ControlBar.wnd:PowerWindow` is in the layout file as a 100x100 rectangle at the top left (at 1920x1080) with `INPUTCALLBACK = GameWinBlockInput`; as long as it
stayed there, the HUD swallowed every click into the top left corner of the screen (about 67x67 pixels at 1280x720). At the scheme rectangle it now lies in the command bar. This changes the
golden value `hud-drawlist-1280x720.sha256`: the rectangle of the `W3DPowerDraw` entry of the draw list is a different one (the client only counts this entry; the bar is still drawn by
`BuildDynamicCommands`).

**Simplified / not built:** the original shows all of this in `ControlBarPopupDescription.wnd` (a window of its own above the command bar, a draw function of its own
`W3DCommandBarHelpPopupDraw`, underlined hotkey); here it stays the pointer tooltip. Missing are: the texts of the science buttons (`ScienceStore::getNameAndDescription`,
`TOOLTIP:ScienceCost` – the client has no generals points window), the text for a conflicting upgrade (`ConflictingLabel`; the simulation knows no `affectedByUpgrade`), the reasons
"maximum number" and "airfield full" (`TooltipCannotBuild…BecauseMaximumNumber`, `…ParkingFull`), the `GeneralsExp` window, object tooltips (unit under the pointer).
**Not verified:** whether a human finds the lines readable and whether position and speed of the tooltip are any good – none of it has been seen.

**Not implemented** (deliberately outside the scope, see the brief): beacon and diplomacy windows, the generals points popup, the
five `UnitUpgradeN` icons (the simulation supplies only the names of the completed upgrades through
`CommandBarView.Upgrades`, but no icon catalog for them), an object portrait in `CameoWindow`/`WinUnitSelected` (only
visibility depending on `CommandBarContext.Command` is switched, no image content). These gaps are functionally harmless (the
buttons stay usable), but visually incomplete; they are earmarked for a later phase.

## 6. Minimap: click, drag, right-click command

The original does not bind the minimap click logic through the normal gadget system (the `LeftHUD` window is a pure
`USER` window without an interactive gadget type), but directly in the input path. `MatchController.TryMinimapWorld` maps a
screen point, centred exactly and preserving the axes (letterbox), to a world coordinate of `LiveMatch.Minimap` (the same rule with
which a renderer should draw the minimap image in `LeftHUD.Rect`). A left click centres the camera there and keeps up while
dragging; a right click issues, through `CommandModeController.IssueContext`, a move command of the current selection to this place –
independent of the chosen `MouseScheme` (minimap conventions are scheme-independent in the original).

## 7. Selection mode and context commands

`GameInputController.Settings.Scheme` comes from `GameSettings.Controls.AlternateMouse` (`MatchController.MapMouseScheme`); the default since
7 October 2026 is `MouseScheme.RightClickCommands` (select with left, command with right; `docs/PRESENTATION-INPUT.md` §6): a right click on free, passable
ground issues a move command, a left click on empty ground clears the selection, a right click cancels an open mode. With
`AlternateMouse = false` (client: `--mouse classic`) the scheme of the original applies. `HudTests.cs` checks both: the move/attack-move cases run
in the scheme of the original (set explicitly there), a case of its own checks the default.

## 8. Screen-space overlay (`HudOverlay`)

`MatchController.Update` computes per frame:

| Field | Origin |
|---|---|
| `SelectionBox` | `InputState.IsDragging(Left)`/`PressPosition(Left)` – pure reading of the running drag, no state tracking of its own. |
| `Selected` (`UnitMarker`) | `SelectionModel.Selected` projected through `EntityPicker.TryProject`; health fraction from `IBodyModule.Health/MaxHealth`. |
| `RallyPoints` | For every selected unit with `ProductionUpdate`: `SimulationQueries.Describe(production)` reads `hasRally`/`rallyX`/`rallyY` (as `Fixed` in the `StateNode`) – the only public way to this state. |
| `OrderLines` | Standing orders **of the selected own units** (attack, attack move, guard; since Phase 16 no longer those of all units): a line unit → target (for an attack the current position of the victim). A simple move command (`ContextAction.Move`) produces **no** `StandingOrder` and therefore has no line, but an order mark. Waypoints are in `WaypointPaths`. |
| `WaypointPaths` (`WaypointPathOverlay`) | `OrderMarkers.WaypointPaths` (§16): per selected unit with a waypoint order the point list unit → stations, only while Alt is held. |
| `OrderHints` (`OrderHintMarker`) | `OrderHintBook` (§16): the marks of freshly issued orders with their age 0..1. |
| `Placement` | `CommandModeController.Ghost` plus a footprint computed here (`Footprint`): for `Geometry == BOX` the corner points rotated by `Angle` from `GeometryMajorRadius`/`MinorRadius`, otherwise a square from `BoundingCircleRadius` (the simulation supplies no edge lengths for non-box geometries). |
| `HintText` | rough text form of `GameInputController.Hint.Action` (no string table label – only a technical placeholder text for a host that has no translation of its own yet). |
| `Ticker` | The texts of the message list of this frame (see below); what is drawn is the list itself, not this field. |

### Message list (`InGameUI::message`, Phase 16)

**Before:** `MatchController` kept a "ticker" with invented English texts ("Rejected: …", "Cannot do that.", "… ready", "Command rejected"), eight seconds of lifetime – and
**no drawing code read it** (`HudOverlay.Ticker` had no consumer outside `MatchController`). So the player never learned why a command did nothing.

**Now** there is, as in the original, **one** message list (`InGameUI::m_uiMessages`, here `UiMessageLog` from the mission text work: six lines, `MessagePosition`/`MessageFont`/
`MessageColor1/2` from `InGameUI.ini`, fading by the integer computation of the original, gone after 277 logic frames). `HudMessageFeed` (`CnCNew.Game/Hud`) writes the lines of the
game into it, `DISPLAY_TEXT` of the mission scripts writes its own – into the same list: in a mission it belongs to the director (`MissionPresentationDirector.UseMessages`, it fades and
draws it, also while the command bar is hidden), in the skirmish to the HUD (`MatchController.OwnsMessageList`, one update per logic step, drawn after the windows like
`InGameUI::postDraw`). Every line is a label of the string table, chosen where the original chooses it; a missing label appears as `??Label??` and is counted by
`LocalizedText`.

| Occasion | Label (English text) | Source in the original |
|---|---|---|
| Production/construction rejected: no money | `GUI:NotEnoughMoneyToBuild` ("You don't have enough money to build that") | `ControlBar::processCommandUI`, `PlaceEventTranslator::translateGameMessage` |
| Upgrade rejected: no money | `GUI:NotEnoughMoneyToUpgrade` | `UpgradeCenter::canAffordUpgrade` |
| Queue full | `GUI:ProductionQueueFull` | `ControlBar::processCommandUI` |
| Maximum number reached (`MaxSimultaneousOfType`) | `GUI:UnitMaxedOut` | ibid. |
| Building site unexplored | `GUI:CantBuildShroud` | `InGameUI::displayCantBuildMessage` (`LBC_SHROUD`) |
| Building site rejected otherwise | `GUI:CantBuildThere` | ibid. (`LBC_GENERIC_FAILURE`) |
| Building of the player finished | `DOZER:ConstructionComplete` with the display name (`Object.DisplayName`, otherwise `INI:MissingDisplayName`) | `DozerAIUpdate` (completion of the build task) |
| upgrade ordered by the player finished | `UPGRADE:UpgradeComplete` with `Upgrade.DisplayName` | `ProductionUpdate::update` |
| own object damaged by another player | `RADAR:UnitUnderAttack`, `RADAR:HarvesterUnderAttack`, `RADAR:StructureUnderAttack` (buildings with `MP_COUNT_FOR_VICTORY`), otherwise `RADAR:UnderAttack`; at most one line per 250 world units and 10 seconds | `Radar::tryUnderAttackEvent`, `Radar::tryEvent` |
| Player defeated (skirmish only) | `GUI:PlayerHasBeenDefeated` with the army name | `VictoryConditions::update` (runs only if `RecorderClass::isMultiplayer`) |

**Without a line, counted** (`HudMessageFeed.WithoutLine`, key = kind, value = count): every other rejection of the simulation (`rejected <reason> (<command>)`), an invalid command
without a building site (`invalid order` – the original shows the forbidden pointer there, no text), an upgrade without `DisplayName`, and upgrades that nobody ordered
(`upgrade granted, not ordered`: the radar of the command center, `GrantUpgradeCreate`, scripts – the event of the simulation does not distinguish them from a purchased one, therefore the
list remembers the accepted `PurchaseUpgradeCommand`s of the player; without that, "Upgrade Complete: Radar" stood at the beginning of every game). **The client's own line** (parenthesised, like
the speech/movie placeholders): "(not supported in this build: …)" for buttons whose command the simulation does not know.

**Simplified/not implemented:** the simulation reports a "blocked footprint" where the original distinguishes terrain, slope, obstacles, supply proximity and path
(`GUI:CantBuildRestrictedTerrain` …) – all run to `GUI:CantBuildThere`. `GUI:ParkingPlacesFull`, `DOZER:RepairComplete`, `RADAR:Infiltration`, `GUI:OverchargeExhausted`,
the battle plan and disguise messages, `GUI:NothingSelected`/`SelectedAcross…` and the messages of the keyboard shortcuts (`GUI:MessagesOn` …) have no source at this state. The name of the
defeated player is the army name (`PlayerTemplate.DisplayName`), because the session carries no player names. In most cases the greyed-out button already prevents the command
(as in the original: `getCommandAvailability` returns `COMMAND_RESTRICTED` with too little money); the rejection lines then appear only with keyboard shortcuts and races.
**Picture samples (only looked at by ourselves, no acceptance):** `tools/CnCNew.UiRenderTool` writes `hudfeedback-WxH.png` (real command bar, three message lines, pointer on a greyed-out
build button with "Requires: …") and `hudletterbox-WxH.png` (the same lines below the letterbox bars); the client shows the line in the smoke test with
`--match "Maps/Alpine Assault/Alpine Assault.map" --frames 110 --input-script scripts/input/placement-refused-click.txt --capture <png>` (click on the own command center:
"You can't build here" at the top left, preview red).
**Not verified:** look, readability and rhythm of the list in the window – no human has seen it.

## 9. Camera settings

`GameSettings.Controls` (sliders `0..1`) is mapped to `CnCNew.Presentation.Input.CameraSettings` (`MatchController.MapCameraSettings`).
The scroll slider is the value of the original itself: `KeyboardScrollSpeedFactor` = slider value (`OptionsMenu.cpp`: `m_keyboardScrollFactor = Position / 100`,
default 0.5; it affects only key and edge scrolling, `docs/PRESENTATION-INPUT.md` §4a). The rotation slider has no counterpart in the original: at `0.5` the
engine value applies, at the ends it is halved or doubled (a deliberate, documented approximation).

## 10. Match result and statistics (`MatchResult.cs`)

`MatchStatsTracker.Observe(TickResult, world)` is hooked by `MatchController` to `LiveMatch.TickStepped` and evaluates
exclusively fields that the simulation already exposes through `SimEvent` (no additional query of player state):

* **Units built:** an `EntitySpawned` without the `STRUCTURE` KindOf, with a real player (the initial map population
  arises before the first `Advance` call and never reaches `TickStepped`).
* **Buildings built:** `ConstructionCompleted` (carries the owner directly).
* **Units lost:** owner of the unit that died (`EntityDied.Entity`), looked up before a possible removal.
* **Kills:** owner of the attacker (`EntityDied.Other`) if it differs from the victim and neither of the two is the
  system account (corresponds to `Object::scoreTheKill`: no friendly fire, no suicide).
* **Money earned:** `SupplyDeposited` credited to the owner of the warehouse (`Entity`, not `Other` – the event
  carries no player field of its own).

`MatchController.MatchEnded` fires exactly once, as soon as `SkirmishMatchSession.IsOver` is observed for the first time.

## 11. Known limits

* **Object portrait, upgrade icons, generals points, beacon, chat** – see section 5, deliberately outside the scope.
* **`ControlBarScheme.ini`** is read only for money/power rectangles and background image parts; toggle/chat/beacon
  button images, `AnimatingPart` blocks and the four border colors (`ButtonBorder*Color`) are parsed and skipped or not read at
  all, because this package needs them nowhere.
* **Minimap aspect ratio:** `TryMinimapWorld` assumes that a renderer draws the minimap image centred in
  `LeftHUD.Rect` preserving the aspect ratio (letterbox); if a host draws it differently (e.g. stretched), it must either use the same rule or
  add a click conversion of its own.
* **Reproducibility of replays with a running `LiveMatch` (important finding, did not arise in this package):** a
  `LiveMatch` that ran several ticks through `Advance` yields a `World.StateHash` that does **not** agree with a fresh
  `ReplayRunner.Run(session.ToReplay(), …)` of the same recording as soon as the computer opponent issues its first own
  command. This reproduces with a minimal counter-check that uses only `LiveMatch.Create`/`LiveMatch.Advance` –
  entirely without `MatchController`, without input, without HUD code – against real content (`game/content`, map
  "Alpine Assault"). The existing replay tests in `CnCNew.Simulation.Tests` (`Tests.Skirmish.cs`) so far compare only two
  fresh `ReplayRunner.Run` calls against each other, never against the hash of a `SkirmishMatchSession` advanced live – the
  gap was therefore untested so far. `tests/CnCNew.Game.Tests/HudTests.cs` therefore checks only the internally consistent reproducibility
  of two fresh replay runs of the same recording (as the existing simulation tests do as well), not the agreement
  with the live hash. A background brief to search for the cause was started separately (see the final report of package G2).
* **Camera settings mapping** (section 9) is a documented approximation, not a faithful adoption of the original.

## 12. Tests

`tests/CnCNew.Game.Tests/HudTests.cs` (registered in `Program.cs`, `--content-dir` required, otherwise `SKIP`):

* Select the starting dozer by click, erect a structure through the control bar and placement (preferably a power plant, otherwise the
  first available `DozerConstruct`, so that the test also runs for GLA without a power economy), queue a unit at a producer
  (searches itself for the first own producer with a `UnitBuild` button, not hard-coded to
  infantry, since that depends on faction/map), move command and – if the produced unit has an attack-move button –
  attack-move command by click.
* Replay: the recorded command stream contains the move command of the human; two independent fresh
  `ReplayRunner.Run` runs of the same recording agree (see section 11 for the live-hash restriction).
* HUD draw lists at 800×600, 1280×720, 1920×1080 and 2560×1080: not empty, at least the minimap `UiCustomDraw` present,
  all 14 command buttons lie completely within the screen area, the `Viewport3D` area covers the full width.
* A minimap click moves the camera.
* Drag selection selects the starting dozer.
* Ctrl+1 saves, `1` recalls the group.
* Golden hash of `BuildDrawList(1280, 720).Dump()` against `tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256`
  (is written on the first run without an existing file; after that a review item like every other baseline).
* Mouse pointer and marks (Phase 16, §16): cursor per context, Alt waypoints as a path with three station marks, fading order mark.

`dotnet run --project tests/CnCNew.Game.Tests -- --content-dir <dir> --only HudTests` runs only this suite (the whole program needs many minutes on
real data; `--only` takes a comma-separated list of suite names, an unknown name is an error with exit code 2).

## 13. Requests to simulation/presentation

* **`ReplayRunner`/`SkirmishMatchSession` divergence** (section 11) – please have it investigated by the team that is responsible for `LiveMatch.cs`
  or the simulation/presentation coupling; a background brief was created for it.
* The wishes left open by package C3 (`CheckConstruct` dry run, access to production queues, `AvailableOnFrame`
  for special powers, `Executed` for upgrades) have been done since Phase 10 (`SimulationWorld.CheckConstruct`, `ProductionUpdate.Queue`,
  `SpecialPowerModuleBase.AvailableOnFrame`, `UpgradeMuxModule.Executed` — see `docs/PRESENTATION-INPUT.md` §10, `docs/SIMULATION-CORE.md` §23);
  G2 itself had needed no further simulation changes.
* For a faithful object portrait and upgrade icons, `CommandBarView`/`ObjectTemplate` would need a reference to a
  portrait image or an upgrade→image catalog; currently the simulation supplies only names.

## 14. Phase 11: the same command bar for campaign missions (`LiveGame`, `LiveMission`, `MissionController`)

Until Phase 10 `MatchController` (and the Stride layers, `AudioDirector`) saw only a `LiveMatch`, i.e. a skirmish session. Since Phase 11 they see a `LiveGame`
(`src/CnCNew.Game/Live/LiveGame.cs`): the common presentation layer (projected frames, fog, camera, minimap, effects, team colors, 30 Hz loop `Advance`) behind the
abstract view "which simulation is running" (`IsOver`, `Winner`, `Players`, `HumanFaction`, `Commands`, `StepSimulation`). Nothing was copied from `LiveMatch.cs` – the construction of the
layers moved unchanged into the base class, `LiveMatch` stayed the same for its callers (`Create`, `Setup`, `Session`, `Commands`).

- **`LiveMission`** (`Live/LiveMission.cs`) puts a `MissionRun` (`MissionSession`) behind it. The viewer is the local side of the mission (`MissionSession.LocalPlayerOf`,
  including the side chosen by `ChallengeSides`); the camera starts above the centroid of its objects. A tick is `MissionPresentationDirector.BeforeStep` → `Session.Step()`
  (scripts, automatic combat, world) → `AfterStep`; the camera ordered by the script travels per frame over the duration of the script (`OnAdvanced` → `ApplyCameraTo`, since Phase 16; before, one jump per new target – `docs/SCRIPT-ENGINE.md` §7a). Commands of the human go
  through `MissionCommandSink` (`Presentation/Input/CommandSink.cs`) into `MissionSession.Issue`, i.e. through the same sequence counter per player as those of the scripts.
  `Winner` is the viewer on a mission victory, otherwise `null` (the opponents are scripted, no competing human).
- **`MissionController`** (`Hud/MissionController.cs`) puts a `MatchController` over the `LiveMission`, hooks `SelectionModel.SelectionChanged` to `MissionUiBridge.OnSelectionChanged`
  (the selection reaches `NAMED_SELECTED`), delivers `BuildDrawList` with the screen text of the director (message lines, typed military caption, timers and counters, cinematic text, speech/movie placeholders, since Phase 16 also letterbox, fades, result banner; during the letterbox without the command bar) above the HUD, after that the message window (`MissionPopupWindow`, `INGAME_POPUP_MESSAGE`: a window tree of its own from `InGamePopupMessage.wnd`; a window with a pause mark pauses the mission and swallows all events, OK/Enter/Escape close it and lift the pause), locks the world input as long as the script has locked it (`MatchController.WorldInputEnabled`), and reports the end of the mission
  once (`MissionEnded`); after that the host calls `GameShell.MissionFinished(run, controller)`.
- **What scripts change on command bar and HUD** (Phase 16, second session; `MissionController` implements `IMissionHudControl`, wording per output type in `docs/SCRIPT-ENGINE.md` §7a):
  `COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE`/`COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT` change the **command set** of the object type, not a single object: `ControlBarScriptState`
  (`Presentation/Input/ControlBarScriptState.cs`) holds overrides per (set name, slot) – a removed button is an override without a button (`GameLogic::setControlBarOverride`,
  `CommandSet::getCommandButton` asks them first) –, and `CommandBarModel.Build` (single and multiple selection) as well as the build queue images read them through `ButtonOf`. The player thus no longer sees and
  clicks the button on **every** object of this set; a button that is already missing stays away, an unknown button or object type is rejected and reported. In `MD_USA01`
  the script sets 25 such removals in tick 0 (recruiting/upgrade buttons of the factions). `PLAYER_SCIENCE_AVAILABILITY` (only for the local side) acts on `PurchaseScience` buttons
  ("Hidden" = button hidden, "Disabled" = locked, `ButtonReason.NotPurchasable`); that becomes visible only with a generals powers window, which the client does not have. `OPTIONS_SET_DRAWICON_UI_MODE`
  switches `MatchController.IconUiEnabled`: off = no heal icons (selection marks stay). `UNIT_AFFECT_OBJECT_PANEL_FLAGS` "Selectable" = `EntityPicker.SetScriptSelectable`: the pick and the
  selection leave the object out, an existing selection loses it (`SelectionModel.Replace/Change`). States do not go along when a mission is saved/loaded. Tests:
  `MissionEffectsTests` (dummies; real `MD_USA01`: button disappears from the bar of a selected object and comes back, science, selection).
- **Superweapon list** (`Hud/SuperweaponTimers.cs`, `MatchController.Superweapons`, applies to skirmishes and missions): top right, at x = 0.90, y = 0.01 of the screen area (`SuperweaponCountdownPosition` of the
  retail `InGameUI.ini`, now from `InGameUiSettings`), per power with `PublicTimer` and per object one line "Name: m:ss" – the name right-aligned up to x, the time left-aligned from x (`SuperweaponInfo::drawName/drawTime`), in the house color
  of the owner, **the enemy ones too** (an enemy nuclear strike counts down on one's own screen). Rules after `InGameUI::draw`: players by index, powers by name, objects in
  creation order; a construction site shows nothing; a power with a shared timer (`SharedSyncedTimer`) is listed once per player; if the owner lacks the required science, the line is missing;
  the time is `(ready frame − tick) / 30` as an integer (`CommandBarModel.ReadyFrameOf`), a ready power has the ready font (`SuperweaponCountdownReadyFont`, retail: bold) and switches every 15 logic frames (`SuperweaponCountdownFlashDuration` 500 ms) between house color and `SuperweaponCountdownFlashColor`, the flash color in the odd periods (the state `m_superweaponUsedFlashColor` starts true; before, the phase was reversed); the list
  stops above the command bar (0.82 of the view height) with "..." (left-aligned from x in the normal font, like `drawTime` in the original; before, right-aligned). The name is the entry `GUI:<power name>` of the string table; if it is missing, the key itself stands there and
  `SuperweaponTimers.MissingLabels` counts it. Mission scripts hide the whole list (`DisplayEnabled`) or individual objects (`HiddenByScript`, through `IMissionHudControl`). The values (position,
  flash duration and color from `InGameUI.ini`, the fonts from `Language.ini` through `InGameUiStyle`, `docs/TEXT-AUDIT.md` §4.6) are no longer constants; `InGameUiSettings` is strictly typed and checked by `TextAndSettingsTests` on the retail file. **Not adopted:**
  the EVA announcement "superweapon ready", which `drawSuperweaponTimers` also triggers in the original (Eva reports that here through the simulation events), the change of the font on becoming ready with a
  text object of its own, the resumption of the state from a saved game. The named mission timers (`DISPLAY_COUNTDOWN_TIMER`) have been, since the mission text session, at
  `NamedTimerCountdownPosition` (X:0.90 Y:0.666) of the retail file (`MissionTextOverlay.NamedTimers`, `docs/SCRIPT-ENGINE.md` §7a).
- **Stride:** `LiveMatchHost` and all world layers take a `LiveGame`; `CnCNew.Client.Stride --mission <map> [--human-side <Name>]` starts a campaign map (smoke test:
  `--smoke-test --mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map" --content-dir game/content`). Since Phase 12 the Stride shell also draws the HUD draw list
  (`MissionController.BuildDrawList`) and processes the shell outputs (`StartMissionOutput`), §15.
- **Limits:** quick saving in the game stays `MissionPauseMenu` (`MatchController` has its own quit dialog); campaign state and progress are still held by the
  `CampaignController`. The skirmish statistics (`MatchResult`) are filled for missions with the same events (`MatchStatsTracker.Snapshot(Players, Winner, Tick)`), so their counting
  "built/lost/killed" applies there as well, a mission result with objectives/points does not exist. Test: `tests/CnCNew.Game.Tests/MissionHudTests.cs` (real map
  `GC_ChemGeneral`: frames, draw list, command through the sink, selection reaches the VM, end exactly once).

## 15. Phase 12: HUD, input and shell in the Stride client (`ClientRuntime`)

Until Phase 11 the Stride shell drew only the world. Since Phase 12 `src/CnCNew.Client.Stride/ClientRuntime.cs` wires up everything the presentation layer delivers – without touching simulation state, it only
reads what the controllers show, and passes on typed inputs:

- **Input:** `Ui/StrideInputTranslator.cs` translates per frame `InputManager.Events` and the absolute mouse position into the device-neutral `PresentationInput.InputEvent`s (window pixels, origin top left =
  space of the back buffer in which the UI is laid out; move event first if the pointer moved; keys without a name in `KeyNames` are discarded, text passes through as characters). The receiver is
  the screen owner: the shell (menus) or the HUD of the running game (`MatchController`/`MissionController`).
- **Drawing:** `GameShell.BuildDrawList`, `MatchController.BuildDrawList` and `MissionController.BuildDrawList` are drawn with **one** `UiRenderer` above the 3D world (`LiveMatchHost`). The windows that the
  command bar leaves to the host (§5): `W3DLeftHUDDraw` is the minimap frame, centred preserving the aspect ratio as in `MatchController.TryMinimapWorld` (§11) – one dot per object that the viewer sees
  (team color; the projection leaves out what fog/shroud hide); `Viewport3D` needs nothing (the world already lies behind it); every other callback is counted by the renderer and stays undrawn
  (`ClientRuntime.LastFrame`: drawn commands and custom draws left open, for smoke tests).
- **Shell outputs:** `StartMatchOutput`, `StartMissionOutput`, `ExitRequestedOutput` and the display/settings changes are executed; a finished game reports back to the shell exactly once
  (`GameShell.MatchFinished`/`MissionFinished`: result/credits screens), after that the world goes away and the shell appears again. Command line: `--shell` (default with `--content-dir` without
  `--match`/`--mission`/`--model-path`), `--match`, `--mission`, `--smoke-test [--frames N --capture <png>]`.
- **Scheme art of the command bar (found while wiring):** the command bar so far had only the window tree artwork. `ControlBarScheme.ini` supplies the actual background: `BuildSchemeBackground`
  (`ControlBarScheme::drawBackground`) puts the `ImagePart`s of the side of the human in layer order (layer 0 last, on top) below the window tree, each through the same mapping as the money/power
  rectangles; an image that the table does not know is skipped. The portrait window on the right names the image `HardCodedWithControlBarSchemeIni`, which the original replaces by the `RightHUDImage` of the scheme (the
  side logo) (`ResolveHardCodedImages`, new field `ControlBarSchemeInfo.RightHudImage`). That has changed the HUD draw list: the golden hash `hud-drawlist-1280x720.sha256` was deliberately regenerated
  (test `HudTests`).
- **Limits:** font metrics and real glyphs still come from `ShellTextMeasure` (synthetic, `docs/GAME-SHELL.md` §10); video and the sound output of the shell/missions are not part of this package (the output `Audio/AudioOutput` is unchanged);
  the wiring is checked headless by smoke test, not played by a human.
- **Picture sample (2 October 2026):** `--smoke-test --mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map" --frames 60 --capture <png>` shows above the world the minimap with team dots in the frame, the command bar with money display, progress bar and
  empty command grid as well as the speech subtitle of the mission (`(speech: Taunts_Toxin102)`); the portrait window on the right stays empty. The shell (`--shell`) draws 172 commands without missing images/textures/labels/glyphs.

## 16. Phase 16: mouse pointer, waypoints and order marks

Finding from the first tech demo (`docs/DEMO-FINDINGS.md` item 8): the client showed the arrow of the operating system, Alt changed nothing visible, placed waypoints were invisible. The rules (which cursor, which
marks) are in `docs/PRESENTATION-INPUT.md` §11/§12; this section describes what the HUD and the client make of them. None of it writes into the simulation: cursor and marks read selection, `StandingOrders`
and the world; commands still go only as typed commands through the sink.

### Cursor in the HUD (`MatchController`)

| Member | Meaning |
|---|---|
| `Cursor` (`CursorState`) | The cursor of this frame: the arrow in the opened game menu, otherwise `GameInputController.Cursor(PointerOverWindow)`. |
| `PointerOverWindow` | Pointer above command bar, minimap or menu, or a press started there is still held (`UiWindowTree.InputTarget`, `TryMinimapWorld`). Then the arrow applies (`InGameUI`: `underWindow`). |
| `PointerPosition` | Pointer position in window pixels, `null` before the first mouse event. |

`Update` re-reads the context under the resting pointer in every frame (`GameInputController.RefreshHint`) as long as the pointer is above the world: the world moves under it, and the selection can change without
an input event. For missions the same applies through `MissionController.Hud`.

### Software cursor in the client (`CnCNew.Client.Stride/Ui/SoftwareCursor.cs`)

The original lets Windows draw the ANI files (`Win32Mouse::setCursor`, `LoadCursorFromFile`). The client draws the same files itself as the last sprite above the draw list (`ClientRuntime.Draw`), so that the
cursor appears in captures and does not depend on the window system: `GameData.LoadCursors()` decodes the cursors through `CnCNew.Content`, per frame a texture is created on demand (`R8G8B8A8_UNorm` like all UI textures),
drawn 1:1 with the hot spot of the file on the pointer position. The animation clock (`CursorAnimator`) starts anew on every cursor change.

* The operating system cursor is hidden (`Game.IsMouseVisible = false`) as long as the software cursor is available. If the arrow from `Mouse.ini` cannot be loaded, the operating system cursor stays visible
  and the client writes **one** warning to the console. Outside the window the client draws nothing.
* Shell and game menu show the arrow; a running game the cursor of the HUD.
* A cursor without an image is not replaced: `Build`/`InvalidBuild` have no `MouseCursor` block in the retail data, so when placing no cursor is visible, as in the original (the build preview is under the
  pointer). A defined cursor whose file is missing or cannot be decoded is counted per kind (`unresolved` in the report line).
* `--smoke-test` still runs without device input and prints a line `cursor: software; 37 definitions, issues [], 34 files loaded, 235 frames, 0 inverting pixels, missing files [], undecodable [], engine cursors
  without a definition [Build, InvalidBuild]; N frames drawn, …; current <Name> (<file> frame i/n, hot spot x,y)` (measured 4 October 2026; without a pointer: `current none (no pointer position yet)`).
* Capture aids of a fixed-step run: `--cursor-at x,y` sets the pointer through a synthetic mouse event per frame (the same event path as a real mouse); `--waypoints "dx,dy;dx,dy;…"` (with
  `--select-own`) holds Alt and orders one waypoint per offset (metres from the first movable selected unit) through the input layer.

### Waypoint path and order marks (`HudOverlayRenderer`)

| Mark | Drawing | Original |
|---|---|---|
| Waypoint path | line unit → station 1 → … in (64, 128, 255), 2 px; per station a diamond with a centre ring. Three waypoints = three marks, the segment from the unit plus two between the stations. | `W3DWaypointBuffer::drawWaypoints`: line in (0.25; 0.5; 1.0), additive, 1.5 wide, with `EXLaser.tga`; per station the model `SCMNode`. The client has no 3D mark models yet, the station is therefore a 2D diamond in the overlay. |
| Order mark move / waypoint / rally point / enter | ring that contracts from 20 px to 6 px radius and fades in the process, with a centre cross; color like the respective line. | `W3DInGameUI::drawMoveHints`: the model `MoveHintName` plays its animation once and disappears after 40 frames. |
| Order mark attack | four red corner angles that close around the target; the mark follows the target as long as it exists. | `drawAttackHints` is empty in the original; the target flashes there (`SelectionFlash`, already present). The mark is an addition. |
| Rally point | unchanged, the yellow flag at the rally point of the selected production building. | `W3DWaypointBuffer` draws the rally point line of the selection outside the waypoint mode. |

Ground points (stations, rally point, marks, order lines) are now projected onto terrain height (`LiveGame.Heights`), before onto z = 0 – on a hilly map the flag otherwise lay beside the point. A point
behind the camera ends the path at this place.

**Visibility as in the original:** the waypoint path appears only for selected units and only as long as Alt is held; if the player releases Alt, the unit continues to follow the path, but it is no longer
drawn. A station reached disappears from the path, another command (or the death of the unit) removes it entirely.

### Tests and picture samples

* `tests/CnCNew.Game.Tests/HudTests.cs` "Cursor and markers" (real data, Alpine Assault): arrow without a pointer; `Normal` without a selection; `Move` with a selected dozer; Alt switches to
  `Waypoint` without a mouse movement; three Alt clicks give a path with four points whose stations lie at most 2.5 px beside the click points, 33 blue lines (3 segments + 3 × 10 mark lines) and three marks that are gone after 40
  frames; Alt released: path hidden; a simple move command replaces the path and leaves a mark that fades with time; above the command bar the arrow; at the right screen edge the
  scroll cursor direction 0, for which `GameData.LoadCursors()` has the image `SCCScroll0.ani`.
* `scripts/capture-world.ps1`: scenes `match-cursor-move` and `match-waypoints`. Both pictures were produced headless and looked at (4 October 2026): the move cursor (four green arrows pointing inwards) is
  above the ground, the selection cursor above the command center, the scroll arrows point right and down at the right and lower edge respectively; the blue waypoint line leads from the dozer over three diamonds,
  around every station lies the fading ring. Two runs of the waypoint scene yielded the same checksum.

### Not verified, not done

* **No human has seen cursor and marks in the running client** (effect, size, readability, delay of the software cursor compared with the real mouse, behaviour when leaving the window and in full screen).
* The radius cursor of the special powers (`RadiusCursorType`, the effect-radius decals) and the cursor text (`CursorText`) are not drawn.
* 3D marks (`SCMNode`, the move mark model) are missing; the marks are 2D substitutes in the overlay.
* The capture, hack, hijack and sniper cursors come only through command buttons that the input layer still lists as "not supported" (`docs/PRESENTATION-INPUT.md` §6); their images are loaded,
  today they are shown only by buttons with a target mode.

## 17. Phase 16: input playback in the client (`--input-script`) and the finding "dozer does not build"

### 17.1 The test facility

`src/CnCNew.Client.Stride/Testing/InputScript.cs` plays in a sequence of pointer and key events **at the place where otherwise `StrideInputTranslator.Poll` flows into `ClientRuntime.Update`**.
A script thus runs through the real client path: the HUD is laid out for the real back buffer, the text measures come from the renderer, per frame there is one update and one draw list –
a headless test of the `MatchController` sees none of that. The facility never writes into the simulation: everything is an `InputEvent`; the only exception `look` moves only the camera.

Invocation (only with fixed time steps): `--smoke-test --frames N --input-script <file>` together with `--match`, `--mission` or `--scenario`.

Format: one line per step, `#` begins a comment, `FRAME VERB ARGUMENTS` (frames count the updates of the runtime from 1).

| Verb | Meaning |
| --- | --- |
| `move TARGET` | pointer to the target |
| `down [left\|right\|middle] [TARGET]`, `up […]` | press/release a button (without a target at the current pointer position) |
| `click [left\|right\|middle] TARGET` | move and press in this frame, release two frames later on the same pixel |
| `key down\|up KEYCODE` | key (`KeyCode` name, e.g. `S`, `LeftControl`); the button key of the command bar takes effect on `up` (examples: `scripts/input/hotkeys-*.txt`) |
| `look TARGET` | centre the camera on the target (test aid, no input) |

Targets are resolved in the frame of their use: `screen:X,Y`; `own:KINDOF[#N]` and `template:NAME[#N]` (the N-th own living object by id, at its selection centre);
`button:WINDOW` (centre of a HUD window, e.g. `ControlBar.wnd:ButtonOptions`); `command:KIND[:OBJECT]` (the first unlocked command bar slot of this `GuiCommandKind`);
`ground:X,Y`; `ground:+DX,+DY from OBJECT`; `placement from OBJECT` (the first place around the object at which the pending placement would be accepted). As with the real device there is **one**
pointer position per frame: first a move event if it changed, then the button and key events of the frame with this position.

Report at the end (console, prefix `input-script:`): every step with resolved pixel and target, every change of selection, every change of the command mode and of the build preview (valid/invalid with
reason, drawn or not), every issued command with the answer of the simulation (`LiveGame.CommandIssued`: accepted or rejection reason), invalid clicks, construction sites created and
finished, the state at the end and a summary line (`input-script summary: steps=… events=… unresolved=… selectionChanges=… commandsAccepted=… commandsRejected=… invalidOrders=… sitesCreated=…
sitesCompleted=…`). A target that cannot be resolved is reported and counted, never silently skipped; the process then ends with exit code 3.

Regression check: `scripts/check-dozer-build.ps1 [-Quick] [-NoBuild]` (scripts under `scripts/input/`) runs the Release client five times, each time-limited, and checks the summary line:
mission `GC_ChemGeneral` and skirmish `Alpine Assault`, each in 1280x720 and 1920x1080 (building placed and built to completion), as well as clicks during the opening sequence of the mission (must be
discarded, the lock must end). Measured 4 October 2026: 5 of 5 passed; 7 October 2026 with the new mouse default again 5 of 5 (the script uses only clicks
that mean the same in both mouse assignments). `scripts/check-build-hotkeys.ps1 [-Faction …] [-Mouse classic]` checks the build keys in the same way in the scenes
`exit-<faction>-all` (`docs/PRESENTATION-INPUT.md` §3a).

### 17.2 Finding: "in the mission demo I cannot build buildings with the dozer"

Measured with the playback "select dozer → build button → pointer over the ground → click" in the real client:

- **After the opening sequence construction works** in `--mission GC_ChemGeneral` and in `--match Alpine Assault`, each in 1280x720 and 1920x1080: selection, button
  (`ButtonCommand01` at 445,617 59x47 in 1280x720), placement mode, valid preview, `ConstructCommand` accepted, construction site is created, dozer builds to completion (407 ticks). UI scaling,
  renderer text measure and the update/draw order of the client are therefore not the cause.
- **In the first 25 seconds of the mission it failed**: the same sequence from frame 40 gave, in the old client, a valid preview at frame 60 and ten frames later at the same pixel
  `BlockedFootprint` – the click was rejected as "Cannot do that" and the placement mode stayed. Cause: `GC_ChemGeneral` begins with a camera flight over the enemy base
  (tick 0–750: `CAMERA_LETTERBOX_BEGIN`, `DISABLE_INPUT`, `SETUP_CAMERA`, `MOVE_CAMERA_TO` at tick 60 and 330, `RESET_CAMERA` at 681, `ENABLE_INPUT` at 750). Of that the client adopted only the
  camera targets – as a hard jump –, but neither the input lock nor the letterbox: the command bar stayed operable while the script pulled the camera away under the pointer. Whoever
  built during this time clicked on a different world location than the one he saw.
- **Fixed** (§7a in `docs/SCRIPT-ENGINE.md`): the script camera now travels over the duration of the script, `DISABLE_INPUT`/`ENABLE_INPUT` lock the world input (`MatchController.WorldInputEnabled`;
  like `ScriptActions::doDisableInput`, selection, command modes and held keys are cleared, the game menu stays reachable with Esc), the letterbox hides the command bar and shows
  bars with the hint that the mission script has locked the input.
- **Feedback when building** (picture sample, PNGs looked at outside the repo): the build preview was a thin frame with diagonals; it is now additionally hatched
  (`HudOverlayRenderer`, green = allowed, red = not allowed). There is still no ghost model of the building. The construction site is visible during construction (fence and scaffolding parts of the
  model), the dozer visibly drives there.
- **Additionally hardened:** `StrideInputTranslator` now scales the normalised pointer position with the back buffer instead of with the window size; if the window area deviates from the back buffer
  (a 1920x1080 window that the desktop cannot hold; display scaling), a click still hits the button under the pointer. The client writes both sizes at the start
  (`display: back buffer …, window client area …`). That could not be reproduced headless and is **not** verified as the cause.
- **Not verified:** whether the tester built exactly in these 25 seconds. The opening sequence is the only path on which the playback failed in the real client; a human has to confirm in the
  running client that construction now works (window, real mouse, real frame times cannot be checked headless).

### 17.3 Picture sample of the mission texts

The mission texts (`docs/TEXT-AUDIT.md` §4.6, `docs/SCRIPT-ENGINE.md` §7a "Screen texts according to `InGameUI`") were looked at as follows: (1) in the client with
`--smoke-test --mission "Maps/MD_USA01/MD_USA01.map" --content-dir … --size 1280x720 --mute --frames 1500 --filmstrip <folder> --every 20` (the typewriter frame by frame: frame 60 shows 19 characters of the title line, frame 100 the finished title line
with the cursor on the empty second line, frame 160 both lines) and `MD_CHI02` with `--frames 2700 --every 90` (the timer "Reinforcements Arrive In" on the right at 0.90/0.666 beside a caption typing itself);
(2) in `tools/CnCNew.UiRenderTool` as scene `missiontext-WxH.png` (800x600, 1280x720, 1920x1080), which shows all texts of one screen including superweapon list and message window on a background – the
superweapon list and the message lines do not occur in the campaign maps (or only late) and can otherwise be seen only through tests. **Pitfall:** the PNGs of the tool keep the alpha channel of the target surface, translucent areas (message window,
shadows) appear too bright in some viewers; the alpha channel is to be discarded before looking. Motion (rhythm of the typewriter, fading out) and the real client window are thus **not** verified.
## 18. Phase 16: preview model when placing

So far the placement of a building showed only the hatched footprint (`PlacementGhostOverlay`, `HudOverlayRenderer`). The original additionally shows the building itself translucently at the pointer
(`InGameUI::placeBuildAvailable` creates a `Drawable` of the template, `handleBuildPlacements` sets place and angle every frame, `setDrawableOpacity(0.45)`, at a refused place `colorTint(red)`).

**Data path** (every stage tested):
1. `CommandModeController.Ghost` (presentation) – place, angle, valid or `PlacementIssue`; the angle starts at the `PlacementViewAngle` of the template (`docs/PRESENTATION-INPUT.md` §7).
2. `MatchController.Update` (after `LiveGame.Advance`, before the world layers) sets `LiveGame.PlacementGhost` = ghost in placement mode (not with the game menu open), otherwise `null`.
3. `LiveGame.ObjectFrame` = `Frame` plus `PlacementPreview.Create(…)` of the ghost (`DrawableInstance` with `Opacity` 0.45, `FlashTint` red at a refused place, `SceneryIndex = PlacementPreviewIndex`). `Frame` itself stays
   free of it: minimap dots, heal icons and selection read it and never see the preview.
4. `LiveMatchHost` gives the object layer `ObjectFrame`; the layer draws `Opacity < 1` with translucent material variants (`docs/CLIENT-OBJECTS.md`, "Translucent objects").

**Tests:** `PlacementPreviewTests` (presentation, 5 tests: angle in the catalog, valid/refused ghost, nothing to show, state selection), `InputTests` "a placement starts at the template's PlacementViewAngle …",
`HudTests` "Placement preview …" (real data, dozer, build button, hover: `ObjectFrame` has exactly one object more than `Frame`, `Opacity` 0.45, red on the own base structure, gone after the click).

**Visual sample** (client, `--smoke-test --match "Maps/Alpine Assault/Alpine Assault.map" --size 1280x720 --frames 100 --capture …`, scripts `scripts/input/placement-preview.txt` and `placement-refused.txt`, PNGs
looked at outside the repo): select dozer, power plant button, pointer beside the dozer: in the green footprint the power plant stands as a pale, translucent model; on the own base structure
(`BlockedFootprint`) it is tinted reddish, the footprint red. Object layer line: `translucent=1` (one translucent part), `tinted=1` at a refused place.

**Not implemented:** line building (walls: `m_placeIcon[1..]`, `buildTiledLocations` – there is one model, no chain), the faction bib decal under a refused place (`addFactionBibDrawable`), the player color
of the indicator at night (`setIndicatorColor` with night color), a shadow of the preview model. The hatched footprint stays as an aid (the original draws none).

**Not verified:** what the preview model looks like in the running window (readability at 0.45 against bright terrain, red tint on dark textures, shadow and sort order with the other
translucent objects) – what has been seen are two still pictures of the headless run; the red tint is a factor on the surface color (`FlashTint.Gain`), not the light addition of the original.

## 19. Phase 21: time and tick rate

The HUD no longer assumes anywhere that a tick lasts 1/30 s. The values from `InGameUI.ini` are in **original frames** (1/30 s; `InGameUiSettings` computes with `CnCNew.Data.OriginalFrame.PerSecond`, the constant `LogicFramesPerSecond` of its own no longer exists); they are compared with the game clock in original frames (`LiveGame.Clock.OriginalFrames`), never with simulation ticks. At 30 Hz all 54 screen hashes, the HUD draw list hash and the 164 files of the `UiRenderTool` are unchanged (`docs/TICKRATE-MIGRATION.md` §14.2).

| What | Before | Now |
|---|---|---|
| Game loop (`LiveGame.Advance`) | fixed step `1.0 / 30`, at most 8 ticks per frame | step `1.0 / world.Time.TicksPerSecond`; catch-up limit as a time budget of 8 original frames (0.27 s of game time, 16 ticks at 60 Hz) |
| Game clock | – | `LiveGame.Clock` (`GameClock`: tick, rate, interpolation fraction; kept as integers, stands still during pause, runs with the game speed) |
| Message list (`HudMessageFeed.Clock`, `UiMessageLog.Update`) | stamp and one update per tick | stamp in original frames, one update per original frame (`MatchController.OnTickStepped` only on frame boundaries) |
| Under-attack line | `30 * 10` ticks | `OriginalFrame.PerSecond * 10` original frames, compared in ticks of the world |
| Superweapon list | remaining time `(readyFrame - Tick) / 30`, blinking `tick / flashFrames` | remaining time `world.Time.ToWholeSeconds`, blinking on the original-frame clock |
| Named counters of the mission | timer value of the script VM (ticks) divided by 30 | the director converts the timer value into original frames (`CounterForDisplay`), `MissionTextOverlay.NamedTimers` divides by `OriginalFrame.PerSecond` |
| Fog presentation | every `SimulationWorld.VisionIntervalTicks` ticks | every `world.VisionInterval` ticks (the constant is removed) |
| Standing orders (`StandingOrders.Update`) | checked per tick, one fire command per tick | per original frame (on the ticks that begin one): the commands that a standing order produces – and thus the replay content – follow the game time; at 30 Hz the same commands as before |
| Order marks, tooltip, mouse pointer, camera | wall time with a 30 of their own | wall time, the 30 is `OriginalFrame.PerSecond` (`OrderHintBook`, `CameraSettings.FramesPerSecond`, `RtsCamera`) |

Smoke test and input scripts (§17): one drawn frame is one original frame of game time at every rate; the frame numbers of a script stay valid when the client runs with `--tick-rate 60`.

**Open:** the saved-game lists (`MissionPauseMenu`, `LoadGameScreen`) still show the raw tick ("tick N"), and `SaveGame` (version 1) writes no rate; a saved game of another rate is rejected through the definition fingerprint (which contains the rate) and the embedded mission state, not through a field of its own. **Not verified:** look and operation at 60 Hz – no human has seen it.

## 20. Phase 24: orders are answered twice

Since Phase 24 an order of the viewer passes the two-stage command path (`docs/PRESENTATION-INPUT.md` §13): it is pre-checked when it is given and submitted to the simulation
immediately before the step of the tick its scheduler gave it. The HUD hangs on both answers.

| Event of `LiveGame` | When | Who listens |
|---|---|---|
| `CommandPreChecked(command, receipt)` | when the order is given; the command has no sequence number yet | immediate display: the controller adds `Rejected` feedback for a refused pre-check (message list, `NoCanDoSound`), the placement mode ends |
| `CommandIssued(command, result)` | when the order is submitted, with the final answer | statistics, the expected-upgrade message, the client's input report (`--input-script`) |
| `CommandResolved(resolution)` | the same moment, with receipt number, pre-check and final answer | `CommandModeController.OnResolved`: an order refused only now gets its `Rejected` feedback now; `LateRejections` and `LateAcceptances` count the answers that differ from their pre-check |

With no input delay the final answer comes with the next step after the order, and the two answers agree unless the world changed in between. `MatchController` wires
`CommandResolved` to the controller; a mission uses the same events. Tests: `HudTests`, the three "Lockstep probe" tests (real clicks at 0, 4 and 12 ticks of delay; the late
refusal of an order whose unit was destroyed on the way is shown and counted).

## 20a. Phase 37: the observer's HUD

The observer's HUD (`SpectatorHud`) is drawn by the game layer as a draw list of its own, not on a layout of the original; since Phase 37 it has a research tab, a row of
view buttons (everything, players 1 to 8) and the event ticker with clickable lines - docs/SPECTATOR-MODE.md §14.

## 21. Phase 29: the indicators of a match of a server (`NetHud`)

`src/CnCNew.Game/Hud/NetHud.cs`. A match that runs through a server somewhere else (`LiveMatch.Remote`) has a `NetHud` in its `MatchController` (`Net`); a local
match has none. It reads the protocol client of the connection and the playback of the lockstep driver through `INetMatchSource` (`LiveMatchNetSource`; a test hands
in its own) and never the simulation. Everything it shows is this project's own English (`NetText`). No human has seen it.

| Where | What | From |
| --- | --- | --- |
| top right | `Ping 42 ms, buffer 18 ms`, with `, catching up` or `, slowing down` while the game runs more than 2 % off the server's clock to get to its place; green below 120 ms of round trip, yellow below 250 ms, red from there and whenever the server waits for this game | the clock's round trip, the playback delay and its rate adjustment |
| top left | `Observing - this game gives no orders` | a viewer without a seat |
| panel under the top edge | **Waiting for players**: `Bob is 1.2 s behind`; once the game is slowed for him `Bob is 2.6 s behind - dropped in 8 s` (the deadline is the server's, counted down here between two of its messages); `Carol lost the connection - the seat is held for 255 s` | `LagState` with `DropInMs` (docs/NETWORK-PROTOCOL.md section 22.3), `SeatState` |
| banner under it | `Paused by Alice` / `Paused by you`; `The game runs at half speed until everybody has caught up`; `Game speed 150 %` | `PaceState`, `LagState.Throttling` |
| the HUD's message list | what happens once: `Bob lost the connection.` and the other six drop reasons in their own words; `Carol is back.`; `Carol did not come back in time.`; `The games are out of sync since tick N. A diagnostics package is written to DIR`; chat lines `Bob: gl hf`, `Carol (to allies): north` | `PlayerDropped`, `SeatState`, `Desync`, `ChatMessage` |
| above the control bar, left | the chat line being typed: `To all: ...` / `To allies: ...` | Enter opens a line to everybody, Backspace one to the allies (as in the original); Enter sends, Escape drops it; while it is open the keys are letters, not hotkeys |

A desync ends the match: the score screen then carries the server's words and where the diagnostics package was written (`<net-dir>/diagnostics`,
docs/NETWORK-DIAGNOSTICS.md) as a message over it (docs/GAME-SHELL.md section 15.4). The original's `DisconnectScreen.wnd` and `ObserverQuit.wnd` are not used: the
first is built around votes to kick a player, which this server does not have (decision D-23); what it would show - who is waited for and for how long - is the
panel. A match that is watched through `--join --watch` uses the spectator's own HUD and has no `NetHud`.

`--net-stats` prints the same on the console: `net-status:` (the connection line, the quality, the panel, the banner) every five seconds and whenever what the panel
or the banner says changes, and `net-notice:` for every line that went to the message list.

Tests: `NetHudTests` - every state from a scripted source, its words, and with game data its picture in six resolutions (23 states, 138 hashes in
`baselines/net-hud.txt`, no two alike; every `DropReason` is a line of the message list with a hash of its own); `NetShellTests` "In the match" - two games on a
hosted server: the connection line, a chat line to all and one to the allies, a pause that names who asked, read from the HUD of both;
`scripts/check-net-acceptance.ps1` - two window clients name a held seat, a return, a stalled client, a seat given up and a pause in their `net-status` and
`net-notice` lines.

## 22. Phase 38: the HUD in the modern skin

`MatchController` takes its skin from the settings' `display.skin` (the client passes the skin of its menus, or of `--skin`). In the **modern skin** the command bar keeps
its art (the scheme images of `ControlBarScheme.ini`, cameos, portraits) and gets the skin's type (`UiSkin.Derive(..., keepArt: true)`); it is laid out anchored with
`UiScaleOptions.KeepBandAspect`, so the bar keeps its proportions at the height's scale, centred at the bottom, and the band beside it is drawn in the skin's canvas colour
from the bar's top line down (`BarBand`) - the world view ends at the same line across the width. The in-game menu (`QuitMenu.wnd`) is derived like the shell's dialogs.
The network indicators (`NetHud.Skin`) and the observer HUD (`SpectatorHud`) take the skin's palette and type scale; the message list, the tooltips and the superweapon
clocks keep the original's style in both skins. In the original skin nothing changed: the HUD draw list hash and every network HUD hash are the same. Pinned: the HUD of
a match in both skins in six resolutions (`baselines/skin-screens.txt`), the network indicators in the modern skin (`baselines/net-hud-modern.txt`, 138).

The settings' `display.aspect` reaches the camera: `keepVertical` sets `RtsCamera.KeepVerticalView` (the vertical field of view of 4:3 at every aspect: more to the sides);
the default keeps the original's horizontal field of view, as the camera always did.
