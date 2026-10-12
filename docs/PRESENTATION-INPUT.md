# Input, selection, command modes and hotkeys (package C3)

This document describes `src/CnCNew.Presentation/Input/**`: the renderer-neutral layer between the device events of the
Stride client and the typed simulation commands. It reads the `SimulationWorld` (read-only) and writes exclusively through
an `ICommandSink` (`Issue(Func<ulong sequence, long tick, SimCommand>)`); the live adapter `SkirmishSessionCommandSink` calls
`SkirmishMatchSession.Issue`, so that every input ends up as a command in the replay. Stride types do not occur; the camera is addressed only through
`Camera/IViewProjection` (package C1). Floating point is allowed here; world positions are converted into fixed point only when a command is created,
with `SimulationQueries.ToFixed` (rounding to the nearest raw value, 16 fractional bits).

Reference sources (read, not copied; paths relative to `references/generals-game-code`): `Core/.../MessageStream/SelectionXlat.cpp`,
`CommandXlat.cpp`, `GUICommandTranslator.cpp`, `PlaceEventTranslator.cpp`, `HotKey.cpp`, `MetaEvent.cpp`, `LookAtXlat.cpp`,
`Core/.../GUI/ControlBar/ControlBar.cpp`, `ControlBarCommand.cpp`, `ControlBarMultiSelect.cpp`, `ControlBarCommandProcessing.cpp`,
`Core/GameEngine/Include/GameClient/MetaEvent.h`, `ControlBar.h`, `GeneralsMD/.../GameClient/InGameUI.cpp`,
`GeneralsMD/.../Common/System/BuildAssistant.cpp`, `GeneralsMD/.../GameLogic/AI/AIGroup.cpp`, `GeneralsMD/.../Common/RTS/Player.cpp`.

## 1. Entry points for the integration

| Type | Task |
|---|---|
| `GameInputController` | Pipeline: `Process(InputEvent, world, view)` per device event, `Tick(world)` once per logic tick **before** `Step`, `UpdateCamera(view, dt)` per frame, `BuildCommandBar(world)`, event `UiRequest(MetaAction)` for menu/pause/chat/screenshot etc., `Hint` (cursor and action under the mouse), `RefreshHint(world, view)` (re-read the context under the resting pointer), `Cursor(pointerOverWindow)`, `IsScrolling`/`ScrollOffset` (§11). |
| `MouseCursorCatalog`, `CursorSet`, `CursorAnimator` | Cursor table from `Mouse.ini`, decoded cursor files, animation clock (§11). |
| `OrderMarkers`, `OrderHintBook` | Waypoint paths of the selection and short-lived command markers in world coordinates (§12). |
| `InputState`, `InputEvent` records | Raw events (mouse, wheel, keys with `KeyCode`, text, loss of focus) with a millisecond clock supplied by the caller. |
| `HotkeyMap` | `CommandMap.ini` from text (`Parse`) or definition graph (`FromGraph`), `WithEngineDefaults`, `Rebind`, `Unbind`, `ToSettings`/`ApplySettings`, `Conflicts`, `Issues`, `Compatibility`. |
| `SelectionModel` | Selection, groups, `SelectionChanged`, `Prune`. |
| `CommandModeController` | Context commands, modes, placement ghost (`Ghost`), `Cursor`, `DrainFeedback()`, `Orders` (standing orders). |
| `CommandBarModel`, `CommandButtonCatalog` | Command bar as data, activation by slot or hotkey, `Hud(world, player)`. |
| `CameraInputController` | `CameraIntent` (scroll, rotation, zoom, centring, reset). |
| `ICommandSink`, `SkirmishSessionCommandSink` | Command output. |

The translation order follows `GameClient.cpp` (meta keys 20, control bar hotkeys 25, placement 30, GUI command 40, selection 50,
camera 60, context commands 70).

## 2. Event model

`InputState.Apply` delivers gestures (`PointerGesture`): `Click`, `DoubleClick`, `DragStarted`, `DragMoved`, `DragEnded`, `DragCancelled`.

| Rule | Value | Source |
|---|---|---|
| A press becomes a drag as soon as one axis deviates by **more than** 25 px | `DragTolerance = 25` | `Mouse.ini`, `SelectionXlat.cpp` (start of drag), `MetaEvent.cpp onMouseEvent` (point region) |
| Right click only up to 250 ms of hold time | `DragToleranceMS = 250` | `Mouse.ini`, `Mouse::isClick` |
| Middle click (camera reset) up to 167 ms, at most 5 px of travel | – | `LookAtXlat.cpp` |
| Double click: second press at most 500 ms after the first click and at most 4 px away; a double click consumes the pair | Windows default | `WM_LBUTTONDBLCLK` (`Win32Mouse.cpp`) |
| Loss of focus: all keys/buttons released, running drag → `DragCancelled`, modifier metas ended | – | own definition |

Modifiers (`KeyModifiers`) result from held modifier keys; left and right are equivalent (`MappableKeyModState`).

## 3. Keyboard: `CommandMap.ini` and `HotkeyMap`

* **Representation in the graph:** `CommandMap` blocks are top-level types unknown to the compiler and are present as
  `CompatibilityDefinition` with id `commandmap/NAME` and the original text of the fields. `HotkeyMap.FromGraph` reads these, `HotkeyMap.Parse`
  reads raw text with a small parser of its own (blocks `CommandMap NAME` … `End`, comments with `;`).
* **Fields** (`TheMetaMapFieldParseTable`): `Key` (`KeyNames`), `Transition` (`DOWN`/`UP`/`DOUBLEDOWN`), `Modifiers` (`NONE`, `CTRL`, `ALT`,
  `SHIFT`, `CTRL_ALT`, `SHIFT_CTRL`, `SHIFT_ALT`, `SHIFT_ALT_CTRL`), `UseableIn` (bit list `SHELL GAME OBSERVER`), `Category`
  (`CategoryListName`), `DisplayName`, `Description` (string labels that the UI translates). Name comparisons ignore case like
  `INI::scanLookupList`. The last definition of an action wins (`MetaMap::parseMetaMap`).
* **Never silent:** unknown action names (debug/cheat names too), keys, transitions, modifiers, contexts, categories, fields, missing
  `Key` and syntax errors land in `Issues`; the affected entry is discarded. The spelling `Feld Wert` without `=` is valid in the original
  (the tokenizer treats `=` as a separator, `INI::initFromINIMulti`) and is accepted, but counted in `Compatibility`.
* **Conflicts:** same key, same modifier state, same transition and overlapping contexts. They are reported in `Conflicts`
  and never resolved: `Resolve` delivers – like the original loop pass – all matching actions in action order.
* **Dispatch** (`GameInputController.OnKey`, after `MetaEventTranslator::onKeyPressed`/`onKeyModStateRemoved`): a modifier alone is the
  binding `KEY_NONE` + state (that is how `BEGIN_FORCEATTACK`/`WAYPOINTS`/`PREFER_SELECTION` begin). On release every combination no longer
  covered fires its `UP` binding, independently of the release order. Consequence of the original semantics: Shift under a held Ctrl is
  the state `SHIFT_CTRL`, for which nothing is bound. Autorepeat triggers no metas. `DOUBLEDOWN` is parsed but never fires (commented out
  in the original).
* **Control bar hotkeys** (`HotKey.cpp`): the character after the first `&` in the translated `TextLabel`, in lower case; triggered on
  release without a modifier; a key already assigned is ignored for later slots (`addHotKey`); a visible but disabled
  button is not triggered (`HotkeyActivation.Disabled`). The translation is supplied by the caller as a `Func<string, string?>`, so that no
  dependency on the UI arises. A meta on the **key press** does not take the key away from the button (§3a). **Deviation:** if the press
  has already stopped the selection (`S` = STOP) and a **stop button** carries the same key, it is not pressed once more on release; the
  original gives the stop command twice.
* **Engine actions** (`MetaAction.CameraScrollUp` … `Guard`, settings names `ENGINE_*`): the original reads the arrow keys raw
  (`LookAtXlat.cpp`); `WithEngineDefaults` therefore binds them as `Source = Engine`. Attack move and guard are control bar buttons in the
  original (`&A`, `&G` in the label) and stay unbound by default, but can be assigned freely for the options.
* **Settings:** `ToSettings` delivers for every action `MODIFIKATOREN TASTE ÜBERGANG` (e.g. `CTRL KEY_1 DOWN`) or `UNBOUND`, canonically
  sorted; `ApplySettings` reports unknown actions and unreadable values as `Issues` and then leaves the binding unchanged.

### 3a. Build keys: keys that are also a meta (7 October 2026, measured; not accepted by a human)

Question of the project owner: do the keys in the build menu work? Measured with real data for America, China and GLA (builder, command center, barracks, war factory;
`Game.Tests` → `HotkeyTests`, per button one real key press through `MatchController.Process`) and in the real client (`scripts/check-build-hotkeys.ps1`, key events through
`--input-script`):

* The chain itself was in order: the `&` in the English CSF text is read (`CommandButtonCatalog.HotkeyOf`), `StrideInputTranslator` passes keys on, the HUD gives them to
  `GameInputController.OnKey`, the button is pressed on release. Nested pages do not exist in the command sets of the game (a set has at most 14 places).
* **What did not work** were all buttons whose key `CommandMap.ini` at the same time binds, without a modifier, on the key press to a meta. In the retail data these are seven
  letters: `E` (`SELECT_MATCHING_UNITS`), `F` (`TOGGLE_FAST_FORWARD_REPLAY`), `H` (`VIEW_COMMAND_CENTER`), `Q` (`SELECT_ALL`), `S` (`STOP`), `W` (`SELECT_ALL_AIRCRAFT`),
  `X` (`SCATTER`). Affected in the sets checked: `S` – Scorpion, Saboteur, Sentry Drone, Listening Outpost (and the Spy Satellite power); `F` – airfield (America, China);
  `E` – ECM Tank, Battle Bus. Before the fix: America 10 of 11, China 13 of 14, GLA 16 of 18 unlocked build/production keys of the first scene; afterwards, with power plant
  and supply depot in the scene, 16 of 16, 18 of 18 and 19 of 19.
* Cause: a deviation of our own ("a meta on pressing suppresses the button key", intended against the double stop command). The original suppresses nothing:
  `MetaEventTranslator::translateGameMessage` (`MetaEvent.cpp`, priority 20) discards only the raw key message whose key **and transition** an entry matches – for a
  `DOWN` entry therefore the press –, and `HotKeyTranslator::translateGameMessage` (`HotKey.cpp`, priority 25) works on `MSG_RAW_KEY_UP`. Both happen: `S` stops and
  then presses the button. That is how it is here now too; only a meta on the **release** of the same key prevents the button.
* Disabled buttons (missing prerequisite, no money) do not react to their key, as in the original. The texts come from the English CSF; another language would have other
  letters (not checked, there is only the English table in the content).

```powershell
.\scripts\check-build-hotkeys.ps1                 # real client, three factions, four units each by key and two buildings (one placed)
dotnet run -c Release --no-build --project tests/CnCNew.Game.Tests -- --content-dir game/content --only HotkeyTests   # every key, with table
.\scripts\demo-factory-exits.ps1 -Faction America   # to try yourself: select the builder, R / B / F ...; select the factory, C / S ...
```

## 4. Camera input

`CameraInputController.Update(input, hotkeys, viewport, dt)` is a pure function of the input plus one-time requests.

| Input | Effect | Value/source |
|---|---|---|
| Arrow keys (engine binding, the modifier must match exactly: Ctrl+arrow selects units) | `Scroll`, per second direction × horizontal/vertical factor × scroll option | `GameData.ini`: 1.6 / 2.0; option 0.5 (see §4a) |
| Cursor in the 3 px edge strip (can be switched off, not during keyboard scroll) | like keys | `edgeScrollSize = 3` (`LookAtXlat.cpp`) |
| Right drag | scroll proportional to the distance from the anchor, limited to half the screen size | `LookAtXlat.cpp`, `RightMouseAlwaysScrolls` |
| Middle drag | rotation 0.01 rad per pixel | `LookAtXlat.cpp` |
| Middle click, `CAMERA_RESET` | `Reset` | `LookAtXlat.cpp` |
| Wheel | zoom −10 height units per notch | `ZoomHeightPerSecond` |
| `BEGIN/END_CAMERA_ROTATE_*`, `…_ZOOM_*` | 0.1 rad or 10 units per logic frame (30/s) | `KeyboardCameraRotateSpeed`, `InGameUI.cpp` |
| Group double press, `VIEW_TEAMn`, next unit, `VIEW_COMMAND_CENTER` | `CenterOn` (world position x, y) | `SelectionXlat.cpp`, `CommandXlat.cpp` |

`Scroll` is camera-relative and dimensionless (1 = base speed of the camera for one second); the conversion into metres is done by the
camera owner (C1).

### 4a. Scroll speed (7 October 2026, measured; not accepted by a human)

Source: `LookAtXlat.cpp`, `LookAtTranslator::translateGameMessage`, branch `MSG_FRAME_TICK`; `W3DView.cpp`, `W3DView::scrollBy`; `GlobalData.cpp`,
`GlobalData::parseGameDataDefinition`; `OptionsMenu.cpp`, `OptionPreferences::getScrollFactor` and `saveOptions`.

* Keys (`SCROLL_KEY`) and screen edge (`SCROLL_SCREENEDGE`) use the same formula in the original: per client frame (30 per second)
  `Faktor × SCROLL_AMT (100) × m_keyboardScrollFactor`, factor = `HorizontalScrollSpeedFactor` 1.6 or `VerticalScrollSpeedFactor` 2.0.
* `m_keyboardScrollFactor` is **not** the value `KeyboardScrollSpeedFactor = 2.0` from `GameData.ini`: directly after reading it in,
  `parseGameDataDefinition` overwrites it with the option of the player (`getScrollFactor`: slider position 0–100 divided by 100, without a saved setting
  `m_keyboardDefaultScrollFactor` = 0.5). The slider of the options writes the same value (`val / 100`).
* `scrollBy` takes one scroll unit as `250 / Breite × 2 × tan(FOV/2)` world units on the image plane (at 1280 pixels of width 0.1822); in y the
  aspect ratio is added, and only the share `sin(Neigung)` lies on the ground.

Until this fix `CameraSettings` computed with the `GameData.ini` value 2.0 instead of with the option 0.5 – edge and keys scrolled **four times** as fast as
the original; the slider moreover scaled horizontal/vertical factor *and* keyboard factor (quadratically). Now `CameraSettings.KeyboardScrollSpeedFactor` is
the option itself (`ControlSettings.ScrollSpeed`, default 0.5; slider fully left = 0 = no key/edge scroll as in the original, fully right = 1.0).

| Distance (1280 × 720, default zoom, slider in the middle position) | before | now | original |
|---|---|---|---|
| Screen edge/keys sideways, world units per second | 1,748.6 | 437.2 | 437.2 (2,400 scroll units) |
| Screen edge/keys forwards | 2,365.6 | 591.4 | 591.4 (3,000 scroll units) |
| Right drag, 100 pixels sideways | 136.6 | 136.6 | 874.3 (`1,6 × Pixelabstand` per frame, no relation to half the screen width) |
| Right drag, maximum value sideways (half the screen width) | 874.3 | 874.3 | 5,595.5 |

The scroll runs on wall-clock time (`CameraIntent.Scroll` × `dt`), not on the tick rate; the test `Camera.edge, key and right-drag scroll speed …` measures one
second with 30 and with 144 frames. In the original the speed depends on the resolution (at 800 pixels of width 699.5 instead of 437.2 sideways) – that is
adopted. **Open deviation (only reported):** the right drag is limited here to the key value at full deflection and is thereby, at 1280 pixels,
6.4 times slower than in the original.

## 5. Selection

### Pick rules (`EntityPicker`)

The original intersects a ray with the rendered geometry (`W3DView::pickDrawable`). This layer knows no meshes, therefore:

1. **Selectable** (`Object::isSelectable` + `CanSelectDrawable`): `ALWAYS_SELECTABLE`, or `SELECTABLE` without status `UNSELECTABLE`/`MASKED`, not
   dead, not in the process of removal, not in a container.
2. **Visible** to the local player (`SimulationWorld.CanPlayerSee`: allies always, otherwise visible cell and no undetected stealth).
3. **Under the cursor** if the cursor lies in the projected bounding circle: radius = largest screen distance of the projected points
   centre + (r,0,0), (0,r,0), (0,0,h/2) with r = `BoundingCircleRadius`, at least 8 px. What is projected is the geometry centre
   (`Z + CenterZDelta`).
4. **Precedence:** smallest depth along the view ray (the ray hits the front object first), then smaller screen distance, then
   smaller id.
5. **Box:** all selectable, visible objects whose projected centre lies in the rectangle (like `iterateDrawablesInRegion`), by id.

### Selection rules (`SelectionModel.SelectFromCandidates`, `SelectionTranslator::onMouseLeftClick`)

* Own objects in the click/box are selected, own buildings however only if exactly one building was hit (or the building is the only
  selectable thing). Several own buildings without a unit empty the selection – a peculiarity of the original, deliberately reproduced.
* Without own objects: exactly one enemy, otherwise exactly one neutral, otherwise exactly one ally is selected **for information**
  (`IsCommandable == false`); enemy + neutral + ally at once: nothing.
* Shift (`BEGIN_PREFER_SELECTION`) extends, unless the selection contains something foreign or an own building; Shift-click on something selected
  removes it. An empty box changes nothing.
* Double click on an own, mass-selectable unit (not a building): all identical templates on the screen, with Alt on the whole
  map; Shift keeps the previous mass-selectable selection; with Ctrl no double click (Ctrl = force attack). Equality here is
  equality of template names (the original uses `isEquivalentTo`, which includes reskins/build variations).
* `SELECT_MATCHING_UNITS` (E): first the screen, with zero new additions the map. `SELECT_ALL` (Q) / `SELECT_ALL_AIRCRAFT`: without `DOZER`,
  `HARVESTER`, `IGNORES_SELECT_ALL` (or only `AIRCRAFT`); a selection with something excluded is replaced, otherwise extended; screen,
  otherwise map. `MaxSelectionSize` (retail 0 = unlimited) applies only here, as in the original.
* `SELECT_NEXT/PREV_UNIT`, `…_WORKER`: ring pass over own mobile, not contained objects in id order from the first selected one;
  replaces the selection and centres. There is no Tab cycling (Tab is `DIPLOMACY`).
* **Groups** (`onMetaCreateTeam`/`SelectTeam`/`AddTeam`/`ViewTeam`, `Player::processCreateTeamGameMessage`): Ctrl+N saves the own
  selected ones and removes them from all other groups; N selects the living own members; a second press of the same group
  within 500 ms (strictly less) centres on the last member (highest id) and selects anew only if not all are selected;
  Shift+N adds (a building that is selected or stands as the first member empties beforehand); Alt+N only centres.
* **Pruning** (`Prune`, per tick): removes deleted, dead, contained, unselectable, sold (`SOLD`) and foreign objects as soon as they are no
  longer selected alone or no longer visible; groups lose dead ones and ones that have become foreign. Every change triggers `SelectionChanged`;
  a selection change cancels an open target or placement mode (`ControlBar.cpp`).

## 6. Commands

### Mouse schemes

**Default since 7 October 2026: `RightClickCommands`** (left selects, right commands) – decision of the project owner, deviating from the original, which is shipped with
`m_useAlternateMouse = FALSE` (`OptionPreferences::getAlternateMouseModeEnabled`). The scheme of the original stays selectable:

* Setting `controls.alternateMouse` (`ControlSettings.AlternateMouse`, default `true`) in `settings.json`; an older file without the key loads with the
  visible note `controls.alternateMouse:missing` and the default.
* Options screen: the check box `OptionsMenu.wnd:CheckAlternateMouse` is now wired (ticked = right commands); "Accept" saves, the next
  game started uses the choice.
* Command line of the client: `--mouse classic` (original: left selects and commands) or `--mouse alternate`; overrides the setting for this run.
* In the code: `GameInputSettings.Scheme` (default likewise `RightClickCommands`), from the settings through `MatchController.MapMouseScheme`.

Independently of the scheme: the left click completes placement and target modes (attack move, guard, button with target), the right click cancels them, right
drag scrolls, the minimap commands with right. The `--input-script` checks (`scripts/check-dozer-build.ps1`, `check-build-hotkeys.ps1`) use only such
scheme-independent clicks (selection, command button, placement) and run with both schemes. **Not accepted by a human.**

| | `LeftClickCommands` (retail default, `m_useAlternateMouse = FALSE`; `--mouse classic`) | `RightClickCommands` ("alternate mouse", default) |
|---|---|---|
| Left click on object | command if the context gives a valid command and Shift is not pressed (`contextCommandForNewSelection`), otherwise selection | selection |
| Left click on ground | context command (move, rally point, waypoint …) | deselect everything |
| Right click | cancel an open mode, otherwise deselect everything | cancel an open mode, otherwise context command |
| Left drag / right drag | box / camera scroll | box / camera scroll |

Escape (`OPTIONS`) first cancels an open mode and only after that reports `UiRequest(Options)` (deviation: the original knows no
keyboard cancel).

### Context commands (`CommandModeController.Evaluate`, order of `evaluateContextCommand`)

| Condition (first matching one wins) | `ContextAction` | Cursor (`Mouse.ini`) | Simulation command(s) |
|---|---|---|---|
| Selection empty/foreign | `None`/`Select` | `Normal`/`Select` | – |
| Own target and Shift | `Select` | `Select` | – |
| Alt (`BEGIN_WAYPOINTS`), ground, mobile units | `AddWaypoint` | `Waypoint` | first point immediately `SetMoveTargetCommand`, further ones loaded afterwards |
| Ctrl + object with body, armed | `ForceAttackObject` | `ForceAttackObj` | standing attack → `FireWeaponCommand`/`SetMoveTargetCommand` |
| Ctrl + ground | `ForceAttackGround` | `ForceAttackGround` | **none** (missing in the simulation), `FeedbackKind.Unsupported` |
| Own construction site, builder selected | `ResumeConstruction` | `ResumeConstruction` | `ResumeConstructCommand` (first builder) |
| All selected ones supply carriers, own supply center or any supply depot (`SELECTION_ALL`) | `Dock` | `Dock` | `DockCommand` per unit |
| Builder with repair rate, own damaged finished building | `Repair` | `DoRepair` | `RepairCommand` per builder |
| Damaged mobile units, own/allied `RepairDockUpdate` | `GetRepaired` | `GetRepaired` | `DockCommand` per damaged unit |
| Container, `IsValidContainerFor` (simulation) for at least one unit | `Enter` | `EnterFriendly` | `EnterContainerCommand` per valid unit |
| Enemy with body, one armed unit in range | `AttackObject` | `AttackObj` | standing attack |
| Enemy, armed, reachable only after moving | `AttackObjectAfterMoving` | `OutRange` | standing attack (pursuit) |
| Enemy, not attackable | `Invalid` | `GenericInvalid` | – |
| Ground, all selected ones produce (`SELECTION_ALL`) | `SetRallyPoint` | `SetRallyPoint` | `SetRallyPointCommand` per producer |
| Other object | `Select` | `Select` | – |
| Ground, mobile units | `Move` | `Move` | `SetMoveTargetCommand` per unit, same target position |
| Otherwise (e.g. only buildings + ground) | `Invalid` | `GenericInvalid` | – |

Commands always go out in ascending id order. Not reproduced: hijacking, car bomb, sabotage, salvaging, capturing buildings,
hacking (no simulation commands), the formation/group offset logic of `groupMoveToPosition` (all units receive the same point) and
the check "fast path available" for the move cursor.

### Modes and buttons (`Activate`, `processCommandUI`)

* Immediate: `UNIT_BUILD` → `QueueProductionCommand`, `PLAYER_UPGRADE`/`OBJECT_UPGRADE` → `PurchaseUpgradeCommand`, `PURCHASE_SCIENCE` → first science
  of the list not owned, `SELL` → `SellCommand` per building, `STOP`, `EVACUATE` → `ExitContainerCommand` per occupant,
  `DOZER_CONSTRUCT_CANCEL` → `CancelConstructCommand`, `SPECIAL_POWER` without target → `UseSpecialPowerCommand(None)`, `SELECT_ALL_UNITS_OF_TYPE`.
  Queue icon → `CancelProduction`, cancelling a construction site → `CancelConstruction`.
* Target mode (`NEED_TARGET_*`): `ATTACK_MOVE`, `GUARD*`, `SET_RALLY_POINT`, special powers with object or position target, `FIRE_WEAPON` with
  object target. An object matching the options (enemy/neutral/ally, own ones count as allied) has precedence over the position; the
  cursor is `CursorName` or `InvalidCursorName`, otherwise `Target`/`GenericInvalid` (like `InGameUI::evaluateContextCommand`: if `getCursorIndex` delivers
  no cursor, `Mouse::CROSS` applies). A name that is not an engine cursor is listed in `CommandButtonCatalog.UnknownCursorNames`; in the retail data that is
  exactly `DaisyCutter` (§11). Right click/Escape/selection change cancel.
* `SPECIAL_POWER_FROM_SHORTCUT`: the source is the own object with the readiest module (ready first, then smallest id;
  `findMostReadyShortcutSpecialPowerOfType`), independently of the selection.
* Unsupported button kinds (hack, hijacking, sabotage, car bomb, combat drop, weapon switch, overcharge, beacon, rail transport,
  upgrade cancel, special power construction, `EXIT_CONTAINER` inventory, `FIRE_WEAPON` on position/mine clearing) deliver `ActivationResult.Unsupported`,
  count in `UnsupportedCounts` and are in the bar as `Restricted`/`NotSupportedBySimulation`.

### Standing orders (`StandingOrders`)

The simulation knows only single steps (move, one shot, dock). Multi-tick commands of the original are therefore held by the input layer itself, which
repeats the single steps in `Tick`, like the skirmish AI; every repeated command goes through the sink and is in the replay.

* **Attack** (`groupAttackObject`): in range → `FireWeaponCommand` as soon as reloaded (with the same preconditions as the simulation,
  hence without rejections); out of range → `SetMoveTargetCommand` to the target, anew only when the target has moved more than 10 m or the
  unit stands; arrived in range → halt in place. Ends when the target dies or becomes invisible.
* **Attack move** (meta `ENGINE_ATTACK_MOVE` or the button `ATTACK_MOVE` of the command bar, then click): one `AttackMoveCommand` per unit (since Phase 17, command table 14). Marching,
  stopping for a fight and marching on is done by the simulation (`docs/SIMULATION-CORE.md` §33); the entry in `StandingOrders` now only says that the unit is on an attack move,
  and ends with it (`SimulationWorld.IsAttackMoving`). The input layer no longer sends a fire command for that.
* **Guard** (all three guard kinds the same, without pursuit): to the position; there the unit chooses its victims itself (target acquisition of the simulation when idle, provided its `AutoAcquireEnemiesWhenIdle`
  allows it). Here too no fire command of the input layer any more.
* **Target choice in general**: resting units of the human have shot on their own at enemies in range since Phase 17 (before never, except with an attack, attack move or guard command); only the
  attack on a target named by the player is still driven from here. Not seen by a human.
* **Waypoints** (`MSG_ADD_WAYPOINT`): the first Alt-click orders the movement immediately and begins the order (`Destination`), every further one appends a
  station (`Waypoints`); on arrival (no move target any more) the next station is ordered as a `SetMoveTargetCommand`, after the last one the
  order ends. The simulation knows no waypoint path; the queue is purely input layer, every step is in the replay. Display: §12.
* **Stop** (`groupIdle`): delete standing orders and `SetMoveTargetCommand` to the own position.
* **Scatter** (`AIGroup::groupScatter`): centroid of the mobile units, farthest first, the centroid moves 0.01 m to the west
  per unit; target = position + direction × 4 × bounding radius.

### Feedback

`CommandFeedback` (for short-lived displays): `MoveMarker` (40 client frames, `W3DInGameUI::drawMoveHints`), `WaypointMarker`, `RallyMarker`,
`AttackMarker` (at the target), `EnterMarker`, `InvalidOrder`, `Unsupported` (with a designation) and `Rejected` (with the `CommandRejection` of the simulation).
The five markers become visible command markers through `OrderHintBook` (§12); the three rejections stay text and sound.

## 7. Placement

`DOZER_CONSTRUCT` chooses the first selected builder that is not busy and opens the placement mode. The ghost follows the cursor;
a left press anchors (outside the terrain: cancel), dragging by at least 5 px (`PLACEMENT_DRAG_THRESHOLD_DIST`) rotates to the
direction anchor → cursor (`InGameUI.cpp`), releasing places: valid → `ConstructCommand(builder, template, x, y, angle)`, invalid →
`InvalidOrder` with a reason, anchor released, mode stays (`PlaceEventTranslator.cpp`). Cursor `Build`/`InvalidBuild` (engine cursors without a
`MouseCursor` block: when placing, no cursor is to be seen, as in the original, §11).

**Initial angle (Phase 16):** the ghost begins – like the placement icon of the original (`InGameUI::placeBuildAvailable`: `build->getPlacementViewAngle()`) – with the `PlacementViewAngle` of the template
(retail: 242 templates with a value not equal to 0 in the object INIs – -45 degrees 139 times, -135 degrees 85 times, also -44, 45, 135 and 180; `ObjectVisual.PlacementViewAngle`, radians), and a click without dragging places with this angle (`ConstructCommand.Angle`). Before it was
always 0. The value comes through `CommandSettings.PlacementViewAngle` (a function template name → angle; the HUD controller fills it from the visual catalog), without the function it stays 0.

**Preview model:** the ghost (`PlacementGhost`: template, place, angle, valid or reason) is the input of `PlacementPreview.Create` (`CnCNew.Presentation/Objects`), which builds a `DrawableInstance` from it:
the model of the normal state of the template, translucent with 0.45 (`placementOpacity` in `InGameUI.cpp`), with the red flash tint (1, 0, 0) for a refused place (`illegalBuildColor`; `Drawable::colorTint` is a
frozen `colorFlash`, here `DrawableInstance.FlashTint`). No model, no ground under the pointer or unknown template: no preview model. The object layer of the client draws it
(`docs/CLIENT-OBJECTS.md`, `docs/GAME-HUD.md` §18). Cursor: the original shows **no** pointer when placing (`Mouse::CROSS` has no block in `Mouse.ini`; with `m_winCursors`, `Win32Mouse::setCursor` then sets
`SetCursor(NULL)`, the DX8 path leaves the pointer hidden) – unchanged (§11).

`SimulationQueries.MissingPrerequisites` delivers the unfulfilled `Prerequisites` lines of a template (the same test as `PrerequisitesSatisfied`, per line, without the
`Ignore_Prerequisites` exception) for the "Requires" line of the button description (`docs/GAME-HUD.md` §5a).
`SimulationQueries.CheckPlacement` is a dry run of `SimulationWorld.CheckConstruct` in the same order: builder available →
template known → instantiable → buildable (`STRUCTURE`, `Buildable`, side) → prerequisites (`Ignore_Prerequisites`) → money →
`NavigationGrid.IsFootprintLegal` (map, cliff, water, foreign footprint; **the same code path as the simulation**) →
`IsPositionExploredBy` (`LBC_SHROUD`). The remaining checks are rebuilt from public data, because `CheckConstruct` is private.
Not checked, because the simulation does not check them either: ground unevenness (`AllowedHeightVariationForBuilding`), units in the way,
distance to supply depots (`SupplyBuildBorder`), distance to the map edge, line building (walls).

## 8. Command bar (`CommandBarModel`)

* **Context** (`evaluateContextUI`): nothing selected → `Empty`; something foreign → `Foreign`; everything sold/`IGNORED_IN_GUI` → `BeingSold`; one
  construction site → `UnderConstruction` (only cancel); no command set → `NoCommandSet`; otherwise `Command` or `MultiSelect`.
* **Command set:** own `CommandSet` field, replaced by the set of the most recently executed `CommandSetUpgrade` (module index order; the
  alternative set if the player or the object owns `TriggerAlt` – `CommandSetUpgrade::upgradeImplementation`). The execution state
  is read through `SimModule.Describe`.
* **Grid:** 14 slots (`ButtonCommand01..14`); set entries 15–18 are never shown by the retail bar (`SlotsBeyondGrid`).
* **Multiple selection** (`ControlBarMultiSelect.cpp`): only `OK_FOR_MULTI_SELECT` buttons of the first object; a slot is dropped if a further
  object has a different button there (except `ATTACK_MOVE`); active if at least one object can execute it; `Sources` names these objects.
* **Availability** (`getCommandAvailability`, order as there): `SCRIPT_ONLY` → hidden; `NEED_SPECIAL_POWER_SCIENCE` without the science
  → hidden; `POWERED` with a negative energy balance without `IGNORES_UNDERPOWERED` → `Underpowered` (excepted are sell, evacuate, exit,
  delete beacon, rally point, stop, weapon switch; `Player.cpp doPowerDisable`); `NEED_UPGRADE` → `MissingUpgrade`; `NOT_QUEUEABLE` with
  production running → `ProducerBusy`; then per kind: buildable `No`/`Only_By_AI` → hidden, no builder / busy → `NotABuilder` /
  `BuilderBusy`, queue full → `QueueFull`, not instantiable → `NotInstantiable` (own, explicit addition), prerequisite →
  `MissingPrerequisite`, money → `InsufficientFunds`; upgrades: owned → `CantAfford`/`AlreadyOwned`, without production (object upgrade) →
  `NoProducer`, science → `MissingScience`; science purchase: `AlreadyOwned`, `MissingPrerequisite`, `NotPurchasable`,
  `InsufficientSciencePoints`; special power not ready → `NotReady`/`Recharging` with `ReadyFraction` (from `availableOnFrame` or the
  shared readiness frame); weapon reloading → `NotReady`/`Reloading`; empty container → `EmptyContainer`.
* **Queue:** up to 9 entries (`MAX_BUILD_QUEUE_BUTTONS`) with template, icon (image of the matching `UNIT_BUILD` button), cost and
  progress (only the head runs) from `ProductionUpdate.Describe`.
* **HUD** (`Hud`): money, energy production (net balance + consumption, hence including power plant bonuses), consumption (sum of negative
  `EnergyProduction` of finished objects), purchase points, special powers of the player (unlocked with science, per power the readiest object).

## 9. Measured numbers (real data `game/content`, 30.09.2026)

* `Data/English/CommandMap.ini`: 96 entries, 0 issues, 0 conflicts, 10 fields without `=` (`Description` for `VIEW_TEAM0..9`). `FromGraph`
  delivers the same 96 bindings; the 10 entries lack the description there, because the INI parser of the data layer reads a line without `=` as
  a statement instead of as a field. The UI should therefore take the descriptions from `Parse` (or the data layer adopts the rule).
* Command buttons: 820 (7 without `Command`, reference default `NONE`: the `NonCommand_*` buttons), 472 command sets, 7 set entries beyond
  slot 14, 0 issues.
* Command bars (player America, 10,000 money, without sciences): command center slot 1 `Command_ConstructAmericaDozer` available,
  slot 13/14 rally point/sell, Gunship (slot 2) hidden; dozer slot 1 power plant available, several buildings with
  `MissingPrerequisite` or `NotInstantiable`; Crusader slot 11/13/14 attack move/guard/stop available; barracks slot 1 Ranger
  (currently `NotInstantiable`), slot 13/14 rally point/sell.
* Test program `tests/CnCNew.Presentation.Tests`: 14 synthetic tests (`InputTests`) plus 1 real-data test; two runs deliver
  byte-identical outputs. The acceptance test drives, through the event pipeline, box selection → Ctrl+1 → right-click move → dozer click →
  hotkey `P` → placement → `1` → right click and checks the exact command sequence; the recorded commands give the same
  state hash in the `ReplayRunner`.

## 10. Limits and missing simulation API

The layer reads the living `SimulationWorld`; an immutable read snapshot does not exist yet. Missing from the simulation (or
helpful as purely reading accesses) are:

1. ~~`SimulationWorld.CheckConstruct(ConstructCommand)` as a public dry run~~ **done (Phase 10):** `SimulationWorld.CheckConstruct(PlayerId, ConstructCommand)`
   is now `public` (`SimulationWorld.Construction.cs`) and the exact logic that `SimulationQueries.CheckPlacement` had to rebuild until now; the two
   can no longer drift apart.
2. ~~Public read accesses to the production queue, `SpecialPowerModuleBase.AvailableOnFrame`, `UpgradeMuxModule.Executed`~~ **done (Phase 10):**
   `ProductionUpdate.Queue` (`ImmutableArray<ProductionQueueItem>`, template/cost/remaining ticks, head first), `SpecialPowerModuleBase.AvailableOnFrame(IWorldServices)`
   (reads, for shared powers, the same common readiness frame as `IsReady`) and `UpgradeMuxModule.Executed` are now public.
3. ~~A public dock marker~~ **done (Phase 10):** `SimulationWorld.IsDock(EntityId)` replaces the module name matching from outside; `IDockModule`
   stays internal, but is no longer needed.
4. Commands for: firing at a position (`FORCE_ATTACK_GROUND`, `FIRE_WEAPON` with `NEED_TARGET_POS`), permanent attacking/attack move/
   guarding/waypoints as AI states (today repeated on the client side), formation movement, hijacking, sabotage, hacking, combat drop,
   weapon switch, beacon, upgrade cancel, special power construction, line building. **Present since Phase 10:** `OverchargeCommand` (`TOGGLE_OVERCHARGE`,
   switches `OverchargeBehavior.Toggle`) and, as a deliberately simplified "capture", `CaptureCommand` (direct takeover including the energy pool effect by
   any own mobile object standing in range — without the channelling timer/science reservation/stealth check of the real
   ability attached to `SpecialAbilityUpdate`; see `docs/SIMULATION-CORE.md` §23 and the comment of `CommandTypes.Capture` for the delimitation).
5. Energy: the simulation keeps only the net balance; production/consumption of the HUD are reconstructed from template values, and the
   "underpowered" state of `POWERED` objects is represented only in the bar (the simulation deactivates nothing).
6. `EnterContainerCommand` takes effect immediately without an approach path (simulation state Phase 5).

Further deliberate simplifications: `CommandSetUpgrade` is evaluated on display instead of being stored on triggering; equality for
double click/`E` by template name; `DOUBLEDOWN` never triggered; `GUARD_FLYING_UNITS_ONLY` like normal guarding; `SELECT_HERO`,
`SELECT_NEXT_IDLE_WORKER`, camera bookmarks (`SAVE/VIEW_VIEW`) and `VIEW_LAST_RADAR_EVENT` are passed on as `UiRequest`.

No new NuGet dependency.

## 11. Mouse pointer (Phase 16)

Reference sources (read, not copied; `references/ea-generals-zero-hour/GeneralsMD/Code`): `GameEngine/Source/GameClient/Input/Mouse.cpp`
(`TheMouseCursorFieldParseTable`, `CursorInfo::CursorInfo`, `CursorININames`, `Mouse::parseIni`), `GameEngineDevice/Source/Win32Device/GameClient/Win32Mouse.cpp`
(`initCursorResources`, `setCursor`), `GameEngineDevice/Source/W3DDevice/GameClient/W3DMouse.cpp` (`setCursor`, `setCursorDirection`),
`GameEngine/Source/GameClient/InGameUI.cpp` (`setMouseCursor`, `setScrolling`, the cursor switch at the end of `evaluateContextCommand`'s hint handling),
`GameEngine/Source/GameClient/MessageStream/LookAtXlat.cpp` (`setScrolling`/`stopScrolling`, `MSG_FRAME_TICK`).

### Data and decoding

* **`Mouse.ini`:** `MouseCursor` blocks are top-level types unknown to the compiler and are present as `CompatibilityDefinition` with id `mousecursor/NAME`
  (baseline unchanged). `MouseCursorCatalog.FromGraph` reads them like `Mouse::parseIni`: `Texture` (file name), `Image`, `Directions`, `Frames`, `FPS`, `HotSpot`
  (`X:.. Y:..`); the fields of the 3D/text modes unused in the retail game (`W3DModel`, `W3DAnim`, `W3DScale`, `Loop`, `CursorText`, `CursorTextColor`,
  `CursorTextDropColor`) are recognised and not evaluated. **Never silent:** a block name outside `CursorININames`, an unknown field, an unreadable value
  or a block without `Texture` is a `MouseCursorIssue`, and the block is not used. `WithoutDefinition` names the engine cursors without a block.
* **Files:** `Data/Cursors/<Texture>.ani`, with `Directions > 1` `Data/Cursors/<Texture><Richtung>.ani` (`Win32Mouse::initCursorResources`). The runtime code opens them
  through `CnCNew.Content` (`GameData.LoadCursors()`); `CursorSet.Load` itself takes only two delegates (exists, open), the presentation layer stays without file access.
* **Decoder:** `CnCNew.Images/CursorReader.cs` (next to `TgaReader`, because like it it delivers pixels; `CnCNew.Formats` continues to check only the RIFF structure on import).
  `ReadAni` reads `RIFF ACON`: header `anih` (frames, steps, default rate in jiffies = 1/60 s, flags), optional tables `rate` and `seq `, `LIST fram` with one
  `icon` chunk per frame; `ReadCur` reads the image directory of a `.cur`/`.ico` file and the bitmaps (1, 4, 8, 24 or 32 bit, colour and mask rows from bottom to
  top, padded to four bytes, hotspot from the directory entry). Result: `AniCursor` (frames as RGBA with hotspot, step sequence with duration, `FrameIndexAt(ms)`).
  Written from the public format descriptions, not from foreign code. Everything not understood is an `InvalidDataException`: unknown chunk, raw bitmap frames
  (`AF_ICON` missing), compressed or PNG bitmaps, wrong table lengths, step onto a non-existent frame, hotspot outside the image, truncated file.
  Two declared special cases: the retail cursors write the full file length into the RIFF size field (like `LegacyAssetReaders.ReadRiff`); a rate of 0 counts as one jiffy.
  "Invert screen" pixels (mask set, colour not black) cannot be represented by a blended cursor: they are decoded opaque in their colour and counted
  (`CursorImage.InvertedPixels`).
* **Hotspot and tempo** come from the cursor file, as in the Windows cursor mode of the original (`LoadCursorFromFile`); `HotSpot`/`Frames`/`FPS` from `Mouse.ini` belong to the
  texture modes (`RM_DX8`, `RM_POLYGON`) and are not in the retail data.
* **Loaded content:** `CursorSet` holds per cursor kind the file(s), shares files used several times and keeps `MissingFiles`, `UndecodableFiles` (with reason), `Unusable`
  (kinds without an image). `Resolve(state, ms, out frame)` delivers `false` if a kind has no block or its file is missing – **no other cursor steps in**.
  `CursorAnimator` keeps the clock: a cursor change (a direction change too) restarts the animation, and a defined cursor without an image is counted per kind.

### Which cursor (`GameInputController.Cursor`)

Order (the first matching row applies):

| Situation | Cursor | Original |
|---|---|---|
| The view scrolls (arrow keys, screen edge, right drag; `IsScrolling` from the last `UpdateCamera`) | `Scroll` in the direction of the scroll | `InGameUI::setScrolling` sets `Mouse::SCROLL`, `W3DMouse::setCursorDirection` chooses the direction |
| Pointer over a window (command bar, minimap, menu) or no mouse event yet | `Arrow` | `underWindow` → `Mouse::ARROW` |
| Placement mode | `Build` / `InvalidBuild` | `MOUSEMODE_BUILD_PLACE` |
| Target mode of a button | `CursorName` or `InvalidCursorName`, otherwise `Target` / `GenericInvalid`; attack move `AttackMove`, guard `Target` | `MOUSEMODE_GUI_COMMAND` |
| otherwise | the cursor of the context under the pointer (table §6) | `MOUSEMODE_DEFAULT` |

**Direction of the scroll cursor** (`CursorSet.DirectionIndex`, after `setCursorDirection`): angle of the scroll offset in screen space (y downwards), rounded to
`Directions` steps; frame 0 points to the right, the frames run clockwise (0 right, 2 down, 4 left, 6 up). Checked against the images: `SCCScroll0` points to the
right, `SCCScroll2` down. Without an offset (right press without movement) frame 0 applies.

**Modifier keys take effect immediately:** after *every* event – a key event too – `Process` re-evaluates the context under the pointer; Ctrl (`BEGIN_FORCEATTACK`),
Alt (`BEGIN_WAYPOINTS`) and Shift (`BEGIN_PREFER_SELECTION`) therefore change the cursor without mouse movement, their release and a loss of focus reset it.
Order like `evaluateContextCommand`: Shift over an own object → `Select`; Alt over ground with a mobile selection → `Waypoint` (overrides every target under the
pointer, an enemy too); Ctrl → `ForceAttackObj` over an object, `ForceAttackGround` over ground. In addition `RefreshHint` re-reads the context per frame, because the
world moves under a resting pointer (the HUD calls it, `docs/GAME-HUD.md` §16).

### Context → block → file (retail data, measured 4 October 2026)

| Context (`ContextAction` / mode) | `CursorKind` (block in `Mouse.ini`) | File | Frames |
|---|---|---|---|
| nothing selected, shell, over windows | `Normal` / `Arrow` | `SCCPointer.ani` | 1 |
| own or other object, Shift | `Select` | `SCCSelect.ani` | 8 |
| Move | `Move` | `SCCMove.ani` | 7 |
| Waypoint (Alt) | `Waypoint` | `SCCWaypoint.ani` | 2 |
| Attack move (mode) | `AttackMove` | `SCCAttMov.ani` | 7 |
| Attack in range, out of range, force attack (Ctrl) on object and ground, target mode without cursor name | `AttackObj`, `OutRange`, `ForceAttackObj`, `ForceAttackGround`, `Target` | `SCCAttack.ani` (the block `OutRange` names `SCCAttack`; `SCCOutRange` is commented out there) | 8 |
| not allowed | `GenericInvalid` | `SCCNoAction.ani` | 1 |
| Enter, dock, get repaired | `EnterFriendly`, `Dock`, `GetRepaired` | `SCCEnter.ani` | 3 |
| Repair (builder) | `DoRepair` | `SCCRepair.ani` | 7 |
| Resume construction | `ResumeConstruction` | `SCCResumeC.ani` | 23 |
| Set rally point | `SetRallyPoint` | `SCCRallyPnt.ani` | 5 |
| Capture building (only via button) | `CaptureBuilding` | `SCCHostile2.ani` | 11 |
| Scrolling | `Scroll` (`Directions = 8`) | `SCCScroll0.ani` … `SCCScroll7.ani` | 1 each |
| Placing | `Build`, `InvalidBuild` | **no block**: no cursor (`Win32Mouse::setCursor` then sets a resource that was never loaded, hence no cursor; read from the source code, not observed on the running original) | – |

Total content: 37 blocks, 0 issues; 34 files loaded (of 52 under `Data/Cursors`; the remaining ones, such as the `*_S` variants, are named by no block), 235 frames, 0 inverting
pixels, no missing or undecodable file; without a block only `Build` and `InvalidBuild`. The input layer can show 19 kinds of its own accord, the command buttons name 20
cursor names (19 of them engine cursors) – together 33 kinds, all with an image. The one name without an engine cursor is `DaisyCutter` (`Command_CrateDrop`,
`Command_CrateDropFromShortcut`): the original shows the cross for it (`Mouse::CROSS`), the input layer `Target` – counted in `CommandButtonCatalog.UnknownCursorNames`.

### Tests

* `tests/CnCNew.Images.Tests` (`CursorReaderTests`, synthetic files from `CursorFixtures`, no excerpt of an original file): 24-bit and 4-bit cursors, mask,
  inverting pixels, ANI with and without `seq `/`rate`, frame by time, the size field special case and ten rejected files.
* `tests/CnCNew.Presentation.Tests` (`CursorTests`): blocks and issues of the table; shared, missing and undecodable files; direction table; animation with
  restart; cursor per context, mode and modifier key **without mouse movement**, over windows, when scrolling at edge, corner, by key and right drag – and in doing so not a
  single command; with `--content-dir` the real-data test (numbers above; every showable kind has a block, every direction its file, an image with the hotspot inside the image and no
  completely transparent frame).

## 12. Waypoint and command markers (Phase 16)

Reference sources: `GameEngineDevice/Source/W3DDevice/GameClient/W3dWaypointBuffer.cpp` (`drawWaypoints`, `setDefaultLineStyle`),
`GameEngineDevice/Source/W3DDevice/GameClient/W3DInGameUI.cpp` (`drawMoveHints`, `drawAttackHints`), `GameEngine/Source/GameClient/InGameUI.cpp` (`createMoveHint`,
`MAX_MOVE_HINTS`), `GameEngine/Source/GameClient/MessageStream/CommandXlat.cpp` (waypoint mode in `evaluateContextCommand`, `issueMoveToLocationCommand`).

* **`OrderMarkers.WaypointPaths(world, selection, orders, waypointMode)`** delivers, per selected own unit with a waypoint order, a `WaypointPath`: start point
  (where the unit stands) and the stations still lying ahead of it (`Destination`, then `Waypoints`). Like `drawWaypoints` only in waypoint mode (Alt held) and only for the
  selection. Three waypoints give three stations – three markers, the segment from the unit and two between the stations. A station reached drops out (the standing
  order advances), another command or the death of the unit removes the path, after the last station it ends.
* **`OrderHintBook`** turns `CommandFeedback` into short-lived markers (`OrderHint`: kind, world position, target object, birth time, lifetime): `MoveMarker`, `WaypointMarker`,
  `RallyMarker`, `AttackMarker`, `EnterMarker`. Lifetime = `DurationFrames` client frames at 30 per second, hence 40 frames = 1333 ms (`drawMoveHints`: `elapsed <= 40`);
  at most 256 markers, the oldest gives way (`MAX_MOVE_HINTS`, ring buffer). `InvalidOrder`, `Unsupported` and `Rejected` are not markers (`Observe` delivers `false`).
  One marker per command, not per unit (a group command is one message, `createMoveHint`).
* **Ctrl and Alt checked against the original:** Alt shows the waypoint cursor and – as long as held – the paths of the selection; every click in waypoint mode is a
  waypoint, on an object too (`evaluateContextCommand`: "Override any other commands with waypoint commands"). Ctrl changes only cursor and command (force attack), the
  original draws nothing for it. **Deviations:** the station and command markers are 2D drawings in the overlay instead of the 3D models (`docs/GAME-HUD.md` §16); the
  attack marker is an addition (`drawAttackHints` is empty); the lines of standing attack/guard orders of the selection stay as an aid (the original draws none);
  the original leaves out the move voice in waypoint mode if the unit is already moving (`CommandXlat.cpp`, `isEffectivelyMoving`) – not reproduced here.
* **Tests** (`CursorTests`): three Alt-clicks → one command to the simulation, path with start and three stations, three marker feedbacks; not selected or Alt released →
  no path; arrival at station 1 → two stations, start at the unit; a further Alt-click appends, a simple command removes the path, after the last station
  it is gone; no rejected commands. Markers: lifetime 1333 ms, age 0.5 at half time, expiry per marker, rally point marker, rejections are not markers, ring of 256.

## 13. Command intent, pre-check, result; the tick source (Phase 24)

Until Phase 24 an order was a command: the input layer asked the sink, the sink submitted it in the current tick and returned the simulation's answer. On a network both are
wrong - the server assigns the tick an order is executed in, and whether it is accepted is only known there. Since Phase 24 the game has **one command path in two stages and one
tick source**, for a skirmish, a mission and a staged scene alike; what differs between "local" and "network" lies behind two interfaces.

### 13.1 The two stages (`CommandPath`)

| Stage | When | What happens |
|---|---|---|
| 1 `Issue` | the moment the order is given | the input layer hands over a **command intent**: the command without its tick and its sequence number, as the function that builds it once both are known. It gets a `CommandReceipt` back at once: a number, and the result of the **local pre-check** - `SimulationWorld.Check`, the simulation's own validation against the world as it is now, which changes nothing (`docs/SIMULATION-CORE.md` §45.5). The intent goes to the scheduler |
| 2 `SubmitDue` | immediately before the step of the tick the scheduler gave the order | the commands of that tick - its **bundle** - are submitted to the session in the bundle's order (by player, then by receipt number); the session assigns tick and sequence number and records the command. The simulation's answer is final and is reported with the receipt number (`CommandResolution`) |

- `ICommandSink.Issue` returns the receipt. A caller that only asks "did it go through so far" reads `Accepted` and `Reason` as before (the receipt converts to a
  `SubmitResult`); `ICommandSink.DelayTicks` tells how long an order is on its way.
- `ICommandScheduler` decides the tick: `LocalCommandScheduler(delayTicks)` executes an order given at tick T at tick T + delay; with a delay of 0 that is the tick it was
  given in. On the network the server takes this place (Phase 25). `IsPresent(tick)` says whether a tick's bundle is complete - locally always.
- **Display, sound and statistics hang on both answers.** The pre-check drives what the player sees and hears at once (`LiveGame.CommandPreChecked`; the controller's
  `Rejected` feedback for a refused pre-check, the placement mode that ends). The final answer drives what counts (`LiveGame.CommandIssued` with the command as submitted,
  `CommandResolved` with the resolution). **An answer that differs from its pre-check is counted** (`CommandPath.Deviations`,
  `CommandModeController.LateRejections`/`LateAcceptances`) and, when it is a refusal, shown: the order gets the `Rejected` feedback it would have got at once.
- `SkirmishSessionCommandSink` and `MissionCommandSink` remain as the sinks **without a scheduler** for headless drivers that step a session themselves (tests, tools): the
  order is submitted in the tick it is given and the receipt's pre-check is the final answer. A game does not use them.
- A spectator's sink refuses every order without building it (receipt number 0), as before.

**Standing orders** (`StandingOrders`, the attack on a named victim and waypoint paths, which give commands from what the world shows once per original frame) go the same way
and know the delay: while a unit's command from there is on its way (`delayTicks`), or the order that set the standing order has not been executed yet, the unit is not judged
again - the world would still show the state the command was given for, and the order would give it a second time. With a delay of 0 nothing waits. The mutation check: without
this rule the test counts 62 repeated commands at a delay of 4 ticks.

### 13.2 The tick source (`ITickDriver`)

`LiveGame.Advance` no longer owns the time. It tells the driver how much wall time passed and asks it, tick by tick, whether the next one may run:
`TryReleaseTick(tick, bundlePresent)`. **A tick whose command bundle is not present does not run**, however much time has passed (counted in `Stalls`). For every released
tick: `BeforeTick` (the HUD's bookkeeping, standing orders), then `CommandPath.SubmitDue()`, then the step.

- `LocalTickDriver` is the accumulator the game always had (at most eight original frames per drawn frame, the backlog dropped beyond one tick).
- **Pause and speed are requests.** `LiveGame.Paused` and `LiveGame.Speed` keep their names; setting them calls `RequestPause`/`RequestSpeed`, reading them gives what the
  driver granted. The local driver grants at once; a networked one need not. The spectator's speed keys and the in-game menu's pause go through the same two properties.
- `LiveGame.TickDriver` can be replaced by a host that takes its ticks from somewhere else (Phase 25).

### 13.3 The lockstep probe (`--input-delay-ticks N`)

`LiveGame.UseInputDelay(n)` (client option `--input-delay-ticks N`, 0 to 600) plays the game as if every order travelled to a server and back: an order is executed N ticks
after it was given; 4 and 12 ticks are around 67 and 200 ms at 60 Hz. The client prints a line for every order whose final answer differs from its pre-check.

| What | Checked by | Result |
|---|---|---|
| d = 0 changes nothing | *with no delay the path records the bytes of immediate submission* (replay bytes and end hash); *the tick driver and the command path record the match of direct submission* (a real match with a human's group orders: replay bytes and session hash); the reference manifest (no run has a human) | identical |
| the order inside a tick | *the order of two players' commands inside a tick is the bundle's*: two players order groups to one point in opposite order of giving, d = 0 and d = 4 | the same world; the control (executed in the other order) gives another |
| delay | *a delayed order is executed d ticks later and answered with its receipt* (d = 0, 4, 12): pre-check and receipt at once, nothing submitted before its tick, every order answered once in the order given | passes |
| late refusal | the unit of an order dies while the order is on its way: the pre-check had passed, the execution refuses; counted as a deviation, the HUD shows the refusal (*the click path with every order executed 0, 4 and 12 ticks after it was given*, through `MatchController` and real clicks) | passes |
| standing orders | *standing orders give no command twice while one is on its way* (d = 0, 4, 12): the attack destroys its target, each waypoint is ordered once, no command arrives late | passes |
| mission | *a mission runs with orders executed 4 and 12 ticks later* (`GC_ChemGeneral`): every order answered at tick + d, the human shares the session's sequence counter with the scripts without a refused number | passes |
| a missing bundle | *a tick whose bundle is missing does not run*; a driver that releases nothing holds the game | passes |

**Not claimed:** how the game feels with 67 or 200 ms between click and reaction. That needs a human (`--input-delay-ticks 4`, `--input-delay-ticks 12`); nobody has tried it.

### 13.4 What stays local in a mission

A mission uses the same path and the same tick source, with the local scheduler and the local driver. One thing in it is input into the simulation that no bundle carries: the
presentation reports back to the script VM (a camera move or a speech has ended, `PresentationLatencyTicks`, the selection, UI signals; register E21 of
`docs/MULTIPLAYER-PLAN.md`). That feedback depends on what one machine showed and when, so it stays restricted to local missions - and it is exactly what would stand in the
way of a networked campaign, which is not in scope.

### 13.5 The logic random stream

`SimulationWorld.LogicRandom` is public and nothing outside the simulation draws from it; in lockstep a single draw from the presentation would be a desync. The source test of
the game layer (`Game.Tests`, `SourceRuleTests`) now forbids the name in `CnCNew.Game`, `CnCNew.Presentation`, `CnCNew.Ui` and `CnCNew.Client.Stride`; the tools that read its
state to compare two runs are outside those projects.

### 13.6 Since Phase 25: the scheduler and the driver of a skirmish are the relay's

A skirmish no longer uses the local scheduler and the local driver: `LockstepDriver` (`CnCNew.Game.Lockstep`) is both, on top of the relay's client
(`docs/NETWORK-PROTOCOL.md` §6). What changed in this document's terms:

- `ITickDriver.IsDue(tick)` is asked first; then the per-tick bookkeeping runs (`BeforeTick`, **once per tick**); then `TryReleaseTick` closes or awaits the tick. So the
  commands standing orders give in a tick still reach that tick's bundle, and a tick that has to wait for its bundle does not get its bookkeeping twice.
  `ITickDriver.TickCompleted(tick)` follows the step (the hash report).
- A bundle carries the commands of every player. `CommandPath` submits them all; a command of another machine has receipt 0 and no pre-check to deviate from, and
  `LiveGame` raises `CommandIssued` and `CommandResolved` for the viewer's own commands only.
- The lockstep probe of a skirmish (`LiveMatch.UseInputDelay`, `--input-delay-ticks`) lives in the driver: it holds every order N ticks before it sends it.
- `SkirmishSessionCommandSink` and `MissionCommandSink` are removed. A mission, a staged scene and a headless test use `CommandPath` with the local scheduler and
  `LocalTickDriver`, or submit to the session themselves.

### 13.7 Since Phase 27: a match on a server somewhere else

`LiveMatch.Join(data, remote, viewport)` is the third entry of a skirmish beside `Create` and `Restore` (`docs/NETWORK-PROTOCOL.md` §18.6). Nothing of the command path
or the tick source is new: the same `LockstepDriver` is `ITickDriver` and `ICommandScheduler`, only its client talks to a `RemoteRelay` (a UDP socket and the system's
clock) instead of the embedded `LocalRelay`. What differs for the viewer follows from the room being server-paced:

- A tick is released when its bundle has arrived **and** its playback time is reached (`PlaybackControl`); the seconds a frame hands to `Advance` are not what moves
  the match. A smoke test, whose frames are fixed steps, therefore plays a joined match in real time; `--join-until-tick T` ends it by the tick instead of by `--frames`.
- An order is executed some ticks after it was given (`ICommandSink.DelayTicks`: the playback delay plus the way to the server); `StandingOrders` wait that long, as
  with the lockstep probe of §13.3.
- Pause and speed are requests the server may refuse (§18.5 there); `LiveGame.Paused` and `Speed` show what the server says.
- What a frame produced - orders, hash reports - is sent at the end of `Advance` (`LiveGame.OnAdvanced`), not with the next frame.
- The viewer plays the seat the room gave the client (`MatchSetup.LocalSlot`); without a seat the match is a spectator's (`MatchSetup.Spectator`), with the spectator's
  sink that refuses orders.

The client's options: `--join ADDRESS:PORT [--room NAME] [--seat N | --watch] [--player-name NAME]`, and for the check `scripts/check-net-join.ps1`
`--join-until-tick T`, `--join-order-every N` (every N ticks one unit of the viewer is ordered to move, through `LiveGame.Commands` - the sink the HUD uses),
`--join-result FILE`, `--diagnostics DIR`. Exit code 4 and one line on standard error when the server is not there, the room turns the client away or the match cannot
be built here. **There is no screen for any of it** - no server address field, no list of rooms, no lobby with other people's names; the shell's skirmish setup still
opens the embedded server. That is Phase 29. **Not verified:** a human has not seen a joined match; the check plays it in a smoke test.
