# Staged building capture (`SpecialAbilityUpdate`)

**Note (9 October 2026, Phase 23): historical, 30 Hz.** Tick counts, victory ticks and hashes in this document were measured at 30 Hz and at their state at the time and are superseded as reference values (authoritative: `baselines/reference-manifest.json`, at 60 Hz since `ea52f87`). Tick figures remain valid as a number of original frames. Above 30 Hz the flash phase grows once per original frame of the preparation (4 / 4 / 4 flashes in 2 s instead of 4 / 9 / 19; in the suite 49 / 49 / 49), and the turning towards the target snaps in against one frame of rotation: `docs/rate-invariance/RATE-INVARIANCE-TURRETS.md` (`d923cea`, `95bcf3d`).

As of: 4 October 2026. Concerns `CnCNew.Simulation` (`Modules/BuiltIn/SpecialAbilityStaging.cs`, `World/SimulationWorld.PowerAndCapture.cs`),
`CnCNew.Presentation` (`Objects/CaptureConditions.cs`) and the demo scenario `capture-building` (`docs/DEMO-SCENARIOS.md`).

## 1. Finding and goal

The first tech demo showed that the capture of a building happened immediately: `CaptureCommand` called `ExecuteCapture`, `SpecialAbilityUpdate` was a
pure shell with the recharge timer of the special power (`docs/GAME-RULES.md` §6d), and the model conditions `UNPACKING`, `RAISING_FLAG` and `PACKING` were never set
(`PresentationTool scenario-audit --name capture-building`: 3 checks failed).

In the original the ability runs in stages: the Ranger walks to the building, unpacks (`UnpackTime`), raises the flag (`PreparationTime`, the building flashes in
his colour), the building changes owner when the preparation ends, then the Ranger packs (`PackTime`). Retail values of the Ranger
(`AmericaInfantry.ini`, `ModuleTag_19`): `StartAbilityRange = 5`, `UnpackTime = 3000`, `PreparationTime = 20000`, `PackTime = 2000` (ms), Black Lotus
(`ChinaInfantry.ini`, `ModuleTag_09`): `StartAbilityRange = 150`, `UnpackTime = 6730`, `PreparationTime = 6000`, `PackTime = 2800`.

Staged are now exactly the two capture powers `SPECIAL_INFANTRY_CAPTURE_BUILDING` (Ranger, Red Guard, Rebel, their Generals variants) and
`SPECIAL_BLACKLOTUS_CAPTURE_BUILDING`. Every other power that carries a `SpecialAbilityUpdate` (flashbang, demo charges, sniper shot, cash theft, hacker attacks ...)
behaves unchanged: only the recharge timer, no handler (`UseSpecialPowerCommand` with `UpdateModuleStartsAttack` is still rejected there with `SpecialPowerHandlerMissing`).

## 2. Sources (read, rewritten)

All references: `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Update/SpecialAbilityUpdate.cpp` and `.../Include/GameLogic/Module/SpecialAbilityUpdate.h`.

| Reimplementation | Original |
| --- | --- |
| `SpecialAbilityUpdate.Step` | `SpecialAbilityUpdate::update` (order of the aborts, choice of phase) |
| `Begin` / `Initiate` | `initiateIntentToDoSpecialPower` (remember the target, `aiIdle`, pack state `PACKED`, with `UnpackTime = 0` `UNPACKED`, end other abilities of the same object) |
| `HandlePacking`, `NeedToPack/Unpack`, `StartUnpacking/StartPacking` | `handlePackingProcessing`, `needToPack`, `needToUnpack`, `startUnpacking`, `startPacking` |
| `IsWithinStartRange`, `WithinSpheres` | `isWithinStartAbilityRange` (distance of the bounding spheres, `FROM_BOUNDINGSPHERE_2D`; `PartitionManager.cpp` `distCalcProc_BoundaryAndBoundary`) |
| `ApproachTarget` | `approachTarget` (`aiMoveToObject`) |
| `TurnToward` | `needToFace`, `startFacing`, `isFacing` (`aiFaceObject`) |
| `StartPreparation` | `startPreparation` (branches `SPECIAL_INFANTRY_CAPTURE_BUILDING` and `SPECIAL_BLACKLOTUS_CAPTURE_BUILDING`) |
| `ContinuePreparation` | `continuePreparation` (abort range, target gone, target allied, recharging of the infantry case every tick) |
| `TriggerEffect` | `triggerAbilityEffect` (experience, garrison case, `defect`, recharging of the Black Lotus) |
| `EndPreparation`, `FinishAbility`, `Exit` | `endPreparation`, `finishAbility`, `onExit` |
| `StagedCaptureRules.CanCapture` | `ActionManager::canCaptureBuilding` (reduced, as before) |

## 3. Sequence in the simulation

`SpecialAbilityUpdate` is now an `IUpdateModule` and an `ISpecialPowerHandler`. It sleeps (`SleepForever`) until `Begin` wakes it, and then runs every tick:

| Stage (`AbilityStage`) | Duration | Content |
| --- | --- | --- |
| `Approaching` | until in range | Target lies outside `StartAbilityRange`: `OrderMove` to the building (not from the player, `ai.LastCommandFromPlayer = false`). As soon as the spheres are close enough, the unit stops and turns towards the target (turn rate of the locomotor; without a locomotor immediately). |
| `Unpacking` | `UnpackTime` | `_packState = Unpacking`; afterwards `Unpacked`. |
| `Preparing` | `PreparationTime` | The recharge of the power begins (`Trigger`), `USING_ABILITY` is set, the unit stands. In the infantry case every tick restarts the recharge (the 15 s count only after the capture), with the Black Lotus only at the moment of the capture. |
| Capture | at the end of `Preparing` | Experience (`AwardXPForTriggering`); a garrisonable target (`GarrisonContain`) is only cleared and not captured (original: "we do not want to set a neutral building to our team if we are not in it"), otherwise `IWorldServices.CaptureStructure` = the existing `ExecuteCapture` (change of owner, energy pool, `StructureCaptured`). |
| `Packing` | `PackTime` | afterwards `FinishAbility`: state and status are cleared, the module sleeps again. |

Durations in ticks (30 per second, rounded up): Ranger/Red Guard/Rebel 90 / 600 / 60, Black Lotus 202 / 180 / 84. `PackUnpackVariationFactor` (newly typed as `Real` in the catalog;
no retail module sets it) would scatter unpack and pack time with `world.LogicRandom`; a draw happens only with a factor above 0.

Range: `StartAbilityRange` applies between the bounding circles of both objects. With `ApproachRequiresLOS` (default) the original then looks for the target in
`StartAbilityRange - 2.5` (a quarter of a pathfinding cell, `UNDERSIZE`); effective is the smaller range (Ranger 2.5, Black Lotus 147.5). The line of sight itself is missing.
Measured: a Ranger stops in front of a `ChinaBarracks` at 66.3 units centre distance (bounding circle of the barracks about 56.8, of the Ranger 7). If an
approach ends without reaching the range (the grid does not let the unit get closer), a remainder of two cells counts as arrived; more than six fruitless approach orders
end the ability (the original re-orders the approach forever).

### Triggers

- **`CaptureCommand`** (existing, replay format and `CommandTypes.Version` unchanged): if the actor has a staged `SpecialAbilityUpdate`, the obligation to already stand in range is dropped.
  What is required is that the power is ready (`UnpauseSpecialPowerUpgrade` applied, recharged); otherwise `SpecialPowerNotReady`. The command calls `SpecialAbilityUpdate.Begin`. An
  actor without such a module keeps the old immediate path (fixtures and tests depend on it).
- **`UseSpecialPowerCommand`** (command button, scripts `*_USE_COMMANDBUTTON_ABILITY*`): now finds the handler (`ISpecialPowerHandler`), `Initiate` calls `Begin`. New in the check path:
  `ISpecialPowerHandler.ValidatesTarget` (default `true`); the capture requires an object target that passes `StagedCaptureRules.CanCapture`, otherwise `InvalidSpecialPowerTarget`.
  Consequence: campaign scripts that let a Ranger "capture" were so far rejected as `SpecialPowerHandlerMissing` and now really run.
- **Release of the power:** the Ranger starts paused (`StartsPaused`), `Upgrade_InfantryCaptureBuilding` lifts the pause. The pause extends the recharge by the pause time: a Ranger
  can capture only 15 s (`ReloadTime`) after the release. The scenario `capture-building` therefore sets `Upgrades` and `PowersReady` (see §6).

### Abort rules (`onExit`)

The ability ends immediately, without packing, if
- the object is dead or is destroyed,
- the player (or a script via a player command) orders the unit to do something else (`AIUpdateInterface.LastCommandFromPlayer`), or it moves after reaching the range,
- the target still exists but is dead, belongs to an ally (owner equal: "captured by a colleague") or is stealthed and undetected, as long as the preparation runs,
- the `SpecialPowerModule` of the power is missing.

If the target has vanished from the world (destroyed), `continuePreparation` aborts the preparation: the unit packs (`PackTime`) and then ends (original: `startPacking(false)`).
If the target leaves the `AbilityAbortRange` (default "unlimited") during the preparation, the same path applies. A second capture command while one is running replaces the run.

## 4. State and determinism

The state (`abilityActive`, pack state, counters, target, range, approach orders, `nextWake`) is **written only as long as an ability is running**; on loading it is missing
and the module sleeps. Thereby the state of all runs without a capture stays byte-identical: **no** version bumps (`WorldSnapshot` 8, replay 8, `CommandTypes.Version` 11,
`MissionSnapshot` 2). The source rule "every `IUpdateModule` stores `nextWake`" is fulfilled. No floating-point numbers, no reference to wall time; the random stream is used only with
`PackUnpackVariationFactor > 0`.

`baselines/compatibility.json` is unchanged (751 entries): the catalog got the field `PackUnpackVariationFactor` (not used in the retail data), all other fields read
(`StartAbilityRange`, `AbilityAbortRange`, `UnpackTime`, `PreparationTime`, `PackTime`, `NeedToFaceTarget`, `DoCaptureFX`, `AwardXPForTriggering`, `ApproachRequiresLOS`) were already typed.

## 5. Presentation

`CnCNew.Presentation/Objects/CaptureConditions.cs` reads `SpecialAbilityUpdate.Stage` (read-only) and sets, called with one line from `ConditionDeriver.Derive`:
`UNPACKING` during `Unpacking`, `RAISING_FLAG` during `Preparing` (original: `startPreparation` replaces `UNPACKING` with `RAISING_FLAG`), with the Black Lotus instead `FIRING_A`
(the hacker typing animation, also `startPreparation`), `PACKING` during `Packing`. `RAISING_FLAG` is now in `ConditionDeriver.ProducedNames`.

Not implemented: the sound (unpack/pack/preparation loop, EVA "building is being stolen"; the tick sound with the flashing is a brief of its own),
the data stream special object `BinaryDataStream` of the Black Lotus and the radar infiltration message. `Stage` and `StageTicksLeft` supply the basis for that.

### Flashing of the building (`DoCaptureFX`)

Sources (read, rewritten; `references/generals-game-code/GeneralsMD/Code/`): `SpecialAbilityUpdate::continuePreparation` (`GameLogic/Object/Update/SpecialAbilityUpdate.cpp`),
`Drawable::flashAsSelected`, `Drawable::saturateRGB`, `Drawable::getSelectionColor`, `TintEnvelope::play`/`update` (`GameClient/Drawable.cpp`) and the light addition in `W3DScene.cpp`
(section "HANDLE THE SPECIAL DRAWABLE-LEVEL COLORING SETTINGS" of the object loop of `RTS3DScene`).

| Layer | Implementation | Original |
| --- | --- | --- |
| Simulation | `SpecialAbilityUpdate.StepCaptureFlash` reports `SimEventKind.CaptureFlash` (`Entity` = building, `Other` = capturer) | `m_captureFlashPhase` in `continuePreparation` |
| Presentation | `SelectionFlashTracker` (`CnCNew.Presentation/Objects/SelectionFlash.cs`): per event the envelope of the building restarts; `DrawableInstance.FlashTint` carries the current colour | `flashAsSelected(&color)`, `getSelectionColor` |
| Colour | House colour of the owner of the **capturer** (`TeamColors`), saturated with `colour * factor - factor / 2` per channel; the factor is `GameData/SelectionFlashSaturationFactor` (retail 0.5), read strictly (`SelectionFlashSettings.FromDefinitions`) | `getIndicatorColor`, `saturateRGB`, `m_selectionFlashSaturationFactor` |
| Envelope | 0 attack and 4 decay ticks: two ticks of full value, then 3/4, 1/2, 1/4, after that nothing (`SelectionFlashTracker.Envelope`) | `TintEnvelope::play(color, 0, 4)`, `TintEnvelope::update` once per 30 Hz frame |
| Client | `Layers/Objects/FlashTint.cs`: factor `1 + 2 * tint` per channel (never below 0) on the surface colour of all rigid and skinned meshes of the drawable, as cached material variants (`ModelAsset.GetTeamMaterial`/`GetSkinMaterial`, `W3dMaterialFactory.Tint`) | Addition of the tint to the ambient light and all global lights of this one drawable |

The tint is signed: with factor 0.5 white becomes (0.25; 0.25; 0.25), pure red (0.25; -0.25; -0.25) – the flash therefore raises the house colour and darkens the
other channels. Ghosts (remembered buildings in the fog) do not flash. Without `FrameProjectorOptions.SelectionFlash` (callers without GameData, e.g. synthetic tests)
explicitly no tint is projected; `LiveGame` always sets the setting. The tint switches with the tick, not with the interpolated frame (the original also updates the
envelope per logic frame).

**Deviation from the original (deliberate, approximation):** the scene lights are common to all objects here, a tint per drawable cannot be added to the lights. The client
therefore multiplies the surface colour; the gain 2 is derived from typical light values (ambient about 0.4, sun about 0.8), not measured. The original brightens shadow sides
relatively more than here. The factor is rounded to 1/32 so that the material variants (four levels per house colour and material) can be cached.

Check aids: the line `object layer:` of the client smoke tests counts `tinted=` (model parts with a tint in the last frame) and `tintedTotal=` (since the start).
`CNCNEW_OBJECT_FLASH=r,g,b` (e.g. `-0.25,-0.25,0.25`) draws all objects permanently with this tint, because a real flash lasts only five ticks.

## 6. Scenario `capture-building`

`DemoScenario` has two new optional properties: `Upgrades` (player upgrades of player 1 from the start, here `Upgrade_InfantryCaptureBuilding`) and `PowersReady`
(the special powers of the placed units are charged, instead of 15 s after appearing). `LiveScenario.Populate` applies them after the placing.

## 7. Measurements (4 October 2026)

`PresentationTool scenario-audit --name capture-building`: **all checks pass.** The Ranger walks from tick 12, stops at x = 83.7 (barracks at 150), `UNPACKING` from tick 139 (90 ticks),
`RAISING_FLAG` from tick 229 (600 ticks), capture tick 828, `PACKING` from tick 829 (60 ticks), end tick 889; the barracks belongs to player 1 at the end.
Flashing (measured with the same audit): 49 `CaptureFlash` events between tick 313 and 822, all within `RAISING_FLAG`; intervals from 35 ticks at the beginning to 6 ticks at the end; in 49 of 49
cases the drawable of the barracks carries in the following projected frame the peak value (-0.25; -0.25; 0.25) of the house colour of the Ranger, in total 245 tinted frames, none without a flash,
the Ranger himself never tinted. The audit fails if one of these does not hold.

Tests: `Tests.StagedCapture.cs` (simulation, synthetic: stage sequence and durations, turning towards the target, Black Lotus from a distance, abort on a destroyed target / player command during the
approach and during unpacking / dead capturer / already captured target, target check of the power, replacement by a second command, command button path, snapshot in every stage, two runs);
`RealStagedCapture` (retail: Ranger, Red Guard, Rebel on `ChinaBarracks` with 90/600/60 ticks, Black Lotus on `AmericaBarracks` with 202/180/84); `CaptureConditionTests.cs`
(presentation); `CaptureFlashTests.cs` (presentation, synthetic: envelope, saturation, colour of the capturer on the building, decay to zero, restart by the next flash and
in the middle of the decay, no tint on other drawables and without the setting). Unchanged: retail reference match (winner FactionGLA, tick 13,890, final hash `76F22CB0CA79BEEF`, chain `35F54CAD40A43E6E`) and stress test (`56882F763EABEF67`, chain `B8E663493F2C73D5`).

## 8. Known limits

- Staged are only the two capture powers; the other `SpecialAbilityUpdate` abilities (demo charges, sniper shot, hacker attacks, cash theft ...) remain recharge shells.
- Booby traps (`BOOBY_TRAPPED`, `checkAndDetonateBoobyTrap`), the skill/rank points (`SkillPointsForTriggering`), `FleeRangeAfterCompletion`, line of sight of the approach and the obligation to collide
  at ranges below 2.5 are missing; the original radar/EVA signal is missing.
- The flashing is checked up to the `RenderFrame` (tests, scenario audit). **Not verified is the picture:** whether brightness, colour cast and tempo of the flashing in the client correspond to the
  original has to be looked at by a human (`scripts\demo-capture-building.ps1`, flashing between tick 313 and 822, at the end almost continuous). Seen by machine was only the
  switch `CNCNEW_OBJECT_FLASH` on trees (clearly recoloured); there is no picture of the flashing barracks, because the camera of the windowless scene runs
  (`--scenario ... --smoke-test --filmstrip`) in the checked state wanders out of the scene (tick 5 still at the location, tick 100 in the map corner; not part of this change, not fixed).
- The light addition of the original is approximated by a factor on the surface colour (see §5); skinned meshes flash along, but as before carry no house colour texture while doing so.
- A foreign command is detected only via `AIUpdateInterface.LastCommandFromPlayer` (move, dock, enter, build, evacuate). A pure fire command (`FireWeaponCommand`) does not touch the AI and was not checked for an abort.
- The bounding sphere here is the 2D bounding circle (`BoundingCircleRadius`); the original computes with the 3D sphere including height and therefore lets the unit stop a little earlier.
- The stopping at the measured distance (Ranger about 66 units from the centre of a barracks, roughly 20 in front of the wall) is the consequence of the sphere rule. Whether it corresponds to the
  original can only be clarified by a visual check by a human.
