# Audio (AudioDirector, Eva announcements, music, device output)

As of: 4 October 2026 (package S5 of 30 September 2026; wiring in the client: Phase 16, item 1)

> **State of the wiring (4 October 2026, `docs/DEMO-FINDINGS.md` #7):** until Phase 15 this document described only a **library** – `AudioDirector` and `AudioOutput` were created nowhere in the Stride client,
> the demos were silent. Since Phase 16, item 1, `ClientRuntime` creates the sound for `--match`, `--mission`, `--scenario` and `--shell` (section "Wiring in the client").
> **This is verified by counters, tests and reproducible WAV recordings – not by listening:** nobody has judged the result at a loudspeaker so far (volume ratios,
> intelligibility, latency, whether the device sounds at all). Where "plays" is written below, it means: the request reaches the mixer and is contained in the recording.

`CnCNew.Game.Audio` makes the game audible: it translates `SoundCue`s (from `CnCNew.Presentation.Fx`), selection/command
events of the presentation and the logic beat of the running match (`CnCNew.Game.Live.LiveMatch`) into commands for the
device-free `SoftwareMixer` (`CnCNew.Audio`). `CnCNew.Client.Stride/Audio/AudioOutput.cs` reads the mixer out and
writes it through `waveOut` (`winmm.dll`) to the Windows output device. Between the two ends there is
no further coupling: `CnCNew.Game.Audio` knows no Stride, `AudioOutput` knows no game rules.

## Entry points for the integration

| Type | Task |
|---|---|
| `AudioDirector.Create(data, options?, settings?)` | Builds the mixer from `AudioSettings.ini` (`MixerOptions.FromSettings`), loads `Eva.ini`, delivers an `IDisposable` director |
| `director.Update(live, dt)` | Per rendered frame: listener position from the camera, pause, enqueue `live.DrainSoundCues()`, Eva beat, music sequence, ambient, bus volumes |
| `director.PlayMenuMusic()` / `StartMatchMusic(side)` / `StopMusic()` | Music state (menu vs. match) |
| `director.PlayUi(name)` | Non-positioned events (clicks, `MiscAudio` hooks) |
| `director.OnUnitSelected(template, count)` / `OnUnitOrdered(template, kind)` | Unit announcements (see below) |
| `director.NotifyInsufficientFunds()` / `OnMovieRequested(name)` | Hooks that `LiveMatch` does not trigger itself (see gaps) |
| `director.ApplySettings(settings)` | Volume sliders of the options screen |
| `director.Mixer` / `director.Stats` | `SoftwareMixer` for the output layer, counters for every request not played |
| `director.Muted` | Master volume 0, without touching the sliders; requests and counters continue |
| `GameAudio.Create(data, settings?)` | One object per client: `EnterShell()`, `Attach(live, hud)`/`Detach()`, `Update(dt)`, `PlayUi(name)`, `PlayHook(miscAudioKey)`, `ApplySettings`, `Muted`, `Report()` |
| `OfflineAudioRenderer(mixer, wavPath?)` | Renders the mixer without a device (`Pump(dt)`), optionally into a WAV file; `Finish()` delivers the SHA-256 of the samples |
| `AudioOutput(mixer)` (`CnCNew.Client.Stride`) | `Start()` opens the device and starts the pump thread, `Dispose()` cleans up |
| `ClientAudio` (`CnCNew.Client.Stride`) | Chooses the consumer of the mixer (device, recording, discard) and prints the audio report |

Above the director lies `GameAudio` (`CnCNew.Game/Audio/GameAudio.cs`, no Stride): one object per client process that owns the director
and attaches it to what is currently on the screen (shell or running game). The client talks only to `GameAudio`
(section "Wiring in the client").

The integrator calls `AudioDirector.Update` once per `LiveMatch.Advance` call and passes the same `LiveMatch` instance
in. `Update` reads the camera (`live.Camera.Target`/`Eye`), the terrain (`live.Heights`), the pause
(`live.Paused`) and the effect sound cues executed since the last call (`live.DrainSoundCues()`) itself; only
selection/commands, EVA events without a sound cue origin and movie sequences do not come from `LiveMatch` and must be
reported explicitly by the UI/input layer (`OnUnitSelected`, `OnUnitOrdered`, `NotifyInsufficientFunds`,
`OnMovieRequested`).

## Wiring in the client (Phase 16, item 1)

**Lifetime.** `ClientRuntime` creates exactly one `ClientAudio` (`CnCNew.Client.Stride/Audio/ClientAudio.cs`) in its constructor and releases it in
`Dispose`. `ClientAudio` owns a `GameAudio` (director + mixer) and **exactly one** consumer of the mixer:

| Call | Consumer | Device opened |
|---|---|---|
| normal (window) | `AudioOutput` (`waveOut`, own pump thread) | yes |
| normal, device cannot be opened | one console line `warning: no audio device available, the game runs silently (…)`, after that `OfflineAudioRenderer` without a file (renders and discards) | no |
| `--smoke-test` | `OfflineAudioRenderer` without a file | no |
| `--audio-capture <verzeichnis>` (with or without `--smoke-test`) | `OfflineAudioRenderer`, writes `<verzeichnis>/audio.wav` (16 bit, stereo, mixer rate) | no |

The substitute consumer is necessary, not cosmetic: without `Render` calls the command queue of the mixer would never be emptied and the counters would stand still.
"No device opened" holds for the code of this project; whether Stride's own `AudioSystem` (part of every `Stride.Engine.Game` instance) touches a device at start is **not investigated**.
`--mute` starts muted (`GameAudio.Muted`). Per frame `ClientRuntime.Update` calls `ClientAudio.Update(sekunden)` after the game or shell step:
first `GameAudio.Update` (with a running game `AudioDirector.Update(live, dt)`, otherwise `AudioDirector.Tick(null, dt)` for music and buses), then – without a device –
`OfflineAudioRenderer.Pump(dt)`.

**Sources.** What reaches the director and from where:

| Source | Path | Counter (`AudioSource`) |
|---|---|---|
| Simulation events (shots, explosions/deaths via FXList `Sound` nuggets, construction, production finished, upgrade, special power, enter/leave, money, `VoiceCreated`/`VoiceTaskComplete`/`VoiceFear`, rejected commands, **capture tick**) | `LiveGame.Advance` executes `FxExecutor.ExecuteEvents` per tick; `SoundCueDeriver` derives the cues (table in `docs/PRESENTATION-FX.md`); `AudioDirector.Update` fetches them with `live.DrainSoundCues()` | `Simulation` |
| EVA (`BaseUnderAttack`, `UnitLost`, `BuildingLost`, `UpgradeComplete` from cues; `LowPower` per tick from the energy balance; `InsufficientFunds` from a rejected order) | cues of the kind `Eva` → `EvaAnnouncer`; `InsufficientFunds` via `MatchController.CommandFeedbackObserved` | `Eva` |
| Unit voice on selection | `SelectionModel.SelectionChanged` of the HUD: the first **own** unit that newly enters the selection speaks (`VoiceSelect`/`VoiceGroupSelect`); a shrinking selection stays silent | `UnitVoice` |
| Unit voice on command | `MatchController.CommandFeedbackObserved` (every `CommandFeedback` of the frame): `MoveMarker`/`WaypointMarker` → `VoiceMove`, `AttackMarker` → `VoiceAttack`, `EnterMarker` → `VoiceEnter`; the speaker is the first own selected unit | `UnitVoice` |
| UI in the game | `MatchController.ButtonClicked` (command bar, game menu) → MiscAudio `GUIClickSound`; `InvalidOrder`/`Unsupported` → MiscAudio `NoCanDoSound` | `Ui` |
| UI in the shell | `PlayUiSoundOutput` of the shell (`GUIClick`, `GUIComboBoxClick`) → `GameAudio.PlayUi` | `Ui` |
| Ambient loops (`SoundAmbient`) | `AudioDirector.UpdateAmbient` from the projected frame | `Ambient` |
| Music | shell: `Shell` in a loop (`GameAudio.EnterShell`, also after the end of the game); game: shuffled `Game_<SEITE>_*` list of the player's side (`GameAudio.Attach` → `StartMatchMusic`) | `Music` |
| Sliders | `SettingsChangedOutput` of the shell → `GameAudio.ApplySettings` (takes effect with the next rendered block) | – |

**Capture tick.** The simulation reports `SimEventKind.CaptureFlash` (`Entity` = building, `Other` = capturing unit) every time the building flashes during the
capture. `SoundCueDeriver` turns that into a positioned effect cue with the event name from MiscAudio `DefectorTimerTickSound` (retail: `DefectorTimerTick`,
`world shrouded everyone`) at the position of the **building**, without a player index – like `SpecialAbilityUpdate::continuePreparation` (`SpecialAbilityUpdate.cpp`), which gives the event the
object id of the target. If the MiscAudio key is missing, the cue is named like the key and lands in `UnresolvedEvents` instead of staying silent.

**Muting and sliders.** `GameSettings.Audio` has five sliders and **no** mute field, the options screen no mute switch. Muted is therefore a runtime state
(`AudioDirector.Muted`/`GameAudio.Muted`, `--mute`): master volume 0, all requests continue to be made, mixed and counted (a muted run has the same counters).
The sliders take effect on applying in the options screen of the shell; moving them without applying is not audible (only then does the shell report `SettingsChangedOutput`).
`--match`/`--mission`/`--scenario` start with `GameSettings.Default`, not with the saved settings.

**Measurability.** On exit (in the `--smoke-test` too) the client prints the audio report (`ClientAudio.Finish`, lines begin with `audio `):

- `audio output:` consumer; without a device frames, peak level and SHA-256 of the rendered 16-bit samples, with a device the underruns;
- `audio source <Quelle>: requested, started, ended, dropped` per `AudioSource` – *requested*: requests that reached the director; *started*: handed to the mixer;
  *ended*: no longer played by the mixer (finished, stopped, displaced **or rejected by the mixer** – the breakdown is only mixer-wide in the next line);
  *dropped*: never became a voice (unknown event, missing file, distance, rate limiting, not for the local player, missing voice key …).
  An EVA request from a cue counts as *requested* under `Simulation`, but neither as started nor as dropped: it continues under `Eva`;
- `audio mixer:` rendered frames, started/ended/stopped/displaced/rejected voices, clipped samples;
- `audio outcomes:` every outcome by name (`played`, `dropped:*`, `eva:requested`, `note:*`);
- `audio started events:` started voices per event name (here there is e.g. `DefectorTimerTick=46`);
- `audio unknown event names`, `audio missing or undecodable sound files` (path and error), `audio missing unit voice keys`, `audio unknown eva events`,
  `audio unknown MiscAudio keys`, `audio skipped movies` – each by name, `none` if empty.

**Reproducible recording.** `OfflineAudioRenderer.Pump(dt)` renders per frame exactly the frames that are missing up to the new total time, as **one** mixer block. With fixed
time steps (`--smoke-test`: 1/30 s) block sizes and command sequence are the same in every run, for that the mixer is bit-identical. So that this holds for the client too,
`ClientRuntime` reads **no device input** in a run with fixed time steps: before, the random position of the real mouse (edge scrolling) shifted the camera and with it the
listener, and two runs differed exactly in the positioned sounds (measured: music identical, differences only in the period of the capture ticks). Measured on
4 October 2026 (Release, `--scenario capture-building --smoke-test --frames 1500 --audio-capture <dir>`): two runs, the same file. The concrete checksums depend on
data, scene, frame count and machine/runtime (floating point of the mixer) and are therefore not kept as a target value. A run with real frame time (`--audio-capture` without
`--smoke-test`) is **not** reproducible; the device output is not anyway (own thread, block boundaries by device clock).

**Measured (4 October 2026, Release, device-free; re-measure):**

| Run | Simulation (req/start/end/drop) | Other | Mixer | missing/unknown |
|---|---|---|---|---|
| `--scenario capture-building --smoke-test --frames 1500` | 50 / 49 / 49 / 0 | `DefectorTimerTick=49`, EVA 1 (`EvaUSA_UpgradeComplete`), music 1 (`Game_USA_05`) | 51 started, 36 finished, 14 replaced by `Limit`, 0 clipped, peak 0.71 | 0 |
| `--match "Maps/Alpine Assault/Alpine Assault.map" --smoke-test` (90 frames) | 2 / 0 / 0 / 1 | EVA 1, music 1 | 2 started, peak 0.67 | 0 |
| the same match, `--autoplay --select-own --frames 2700` | 60 / 35 / 35 / 24 (`not-local` 13, `ui-other-player` 11) | selection voice 1 (`CommandCenterUSASelect`), ambient 2, EVA 1, music 1 | 40 started, **189 clipped samples, peak 1.00** | 0 |

Conspicuous and not assessed: the EVA announcement "Upgrade complete" right at the start of the game (an upgrade granted at the start reports `UpgradeCompleted`), eleven `NoCanDoSound` in the
autoplay run (rejected commands of the AI that plays the human's seat) and the clipping in the longer match. Whether it should sound like that can only be decided by a listening test.

**Tests** (`tests/CnCNew.Game.Tests/AudioDirectorTests.cs`, prefix "Wiring"): `CaptureFlash` → cue at the building → started voice, and missing key → unknown event;
selection/command/click/rejection → director; missing file counted per source, no substitute sound; mute and sliders at 0 give digital silence with the same counters, unmuting takes effect
in the next block; offline rendering without a device does not throw and writes the same WAV file twice with fixed steps; real data: the scene `capture-building` runs with HUD and
`GameAudio`, the tick starts, the Ranger answers the selection, and the world hash is the same with and without sound.

**Not wired / open:**

- **Script sound in missions** has been connected since Phase 16, item 5 (`MissionPresentationDirector.Audio` ← `GameAudio.PlayScripted`): `SPEECH_PLAY`, `SOUND_PLAY_NAMED`,
  `PLAY_SOUND_EFFECT` and `PLAY_SOUND_EFFECT_AT` play the named event without a position (`AudioDirector.PlayUi`, source `Ui` in the report), `MUSIC_SET_TRACK` starts the track
  (`AudioDirector.SetMusicTrack`), after that the playlist of the side continues. The volume and type switches of the scripts have been connected since the second session (section "Script control
  of the sound" below). **Still open:** the position of `PLAY_SOUND_EFFECT_AT`, sound lengths for `HAS_FINISHED_AUDIO` (placeholder 3 s); counters and table in `docs/SCRIPT-ENGINE.md` §7a.
- **Sounds of the special ability itself** (`UnpackSound`, `PackSound`, `TriggerSound`, `PrepSoundLoop` of `SpecialAbilityUpdate`; retail: Black Lotus, Hacker, Colonel Burton –
  the infantry capture has none) and the completion voices (`VoiceCaptureBuildingComplete` …): the simulation does not report the points in time as an event.
- `VoiceGarrison`/`VoiceGuard` on commands (the feedback does not distinguish entering and garrisoning), building selection (buildings without `VoiceSelect` appear as
  `dropped:no-voice-key`), the terrorist-in-car voices from MiscAudio.
- The shell constant `GUISliderMoved` does not exist in the retail content (there `GUISliderBeep`); it is currently sent by no screen and would be counted as an unknown event.
- The options dialog in the running game (`MenuAction.Options`) is not wired in the client, so no sliders there either.
- `AudioOutput` has no automatic test (neither the success nor the failure path; both need an audio device or its absence respectively); what is verified is only that the client starts with a window and
  runs 45&nbsp;s without a crash and without a warning. Nothing was heard.
- Reference place of the click sound: `MiscAudio.h` describes `m_guiClickSound` as "snd to play when user presses button in GUI"; the call site in the original was not
  searched for, the rule "command bar/menu button → `GUIClickSound`" is our own.

## Script control of the sound (Phase 16, item 5, second session)

Mission scripts turn the sound loud, quiet, mute and off (the cinematic sequences of the campaign do that almost throughout: in the 24 campaign maps `SOUND_SET_VOLUME` is set 281 times,
`SPEECH_SET_VOLUME` 188 times, `SOUND_DISABLE_TYPE` 300 times, `EVA_SET_ENABLED_DISABLED` 68 times). The state lies in `AudioDirector.Script.cs`, the script reaches it through
`MissionPresentationDirector.AudioControl` (`IMissionAudioControl`, implemented by `GameAudio`); none of it touches the simulation. Reference: `ScriptActions.cpp`
(`doAudioSetVolume`, `doSoundOverrideVolume`, `doSoundEnableType`, `doSoundRemoveAllDisabled`, `doSoundRemoveType`, `doEnableObjectSound`, `doEvaEnabledDisabled`, `doAmbientSoundsPause`)
and `GameAudio.cpp` (`AudioManager::setVolume`, `setAudioEventVolumeOverride`, `setAudioEventEnabled`, `addAudioEvent`).

| Output | Effect in the client |
|---|---|
| `MUSIC_SET_VOLUME`, `SOUND_SET_VOLUME`, `SPEECH_SET_VOLUME` (percent) | Factor 0..1 (limited) that **multiplies** the slider of the player per bus (`m_musicVolume = m_scriptMusicVolume * m_systemMusicVolume`). Music → bus `Music`; `SOUND_SET_VOLUME` sets, like `AudioAffect_Sound \| AudioAffect_Sound3D`, the 2D sound (`Ui`) and the 3D sound (`Sfx`, `Ambient`); speech → `Voice` (EVA, unit voices, mission speech) |
| `AUDIO_OVERRIDE_VOLUME_TYPE(Ereignis, Prozent)` | The volume of the catalog event is **replaced** (200 = twice as loud as in the catalog; the random volume offset of the instance is added as in the original): for all voices started from now on and – through `SoftwareMixer.SetVoiceVolume` – for those currently playing (`adjustVolumeOfPlayingAudio`) |
| `AUDIO_RESTORE_VOLUME_TYPE(Ereignis)`, `AUDIO_RESTORE_VOLUME_ALL_TYPE` | Remove the override (all). Running voices stay as they are (the original does not touch them here either) |
| `SOUND_DISABLE_TYPE`, `SOUND_ENABLE_TYPE`, `SOUND_ENABLE_ALL` | Muting is an override of 0: `Start` throws the request away as `dropped:script-muted` (`AudioManager::addAudioEvent` cuts events below the minimum volume off as mute); enabling removes the override |
| `SOUND_REMOVE_ALL_DISABLED`, `SOUND_REMOVE_TYPE` | The running voices of all muted events or of the one event respectively are stopped (tracked through `Tracked.Event` and the ambient entries) |
| `ENABLE_OBJECT_SOUND`, `DISABLE_OBJECT_SOUND` | The ambient loop of a named object stays off (`Drawable::enableAmbientSoundFromScript`); a running loop ends with the next ambient pass |
| `SOUND_AMBIENT_PAUSE`, `SOUND_AMBIENT_RESUME` | No ambient loop as long as paused |
| `EVA_SET_ENABLED_DISABLED` | `EvaAnnouncer.Enabled` (`Eva::setEvaEnabled`): on switching, the noted events are deleted; as long as off, `Update` does not run at all (no checks, no expiry) |

An event name that the audio catalog does not know is not stored, but counted under "audio unknown event names". The end of the game (`GameAudio.Detach`, also on entering the shell)
calls `ResetScriptState` (`AudioManager::reset`): script volumes back to 1, no overrides, Eva on. Music is deliberately excluded from `SOUND_DISABLE_TYPE` (tracks run through the
playlist, not through `Start`). The audio report has a line `audio script control: …` for this (volumes, overrides, muted requests, removed voices).

**Verified** by `AudioDirectorTests.ScriptControlsTheSound` (measured at the **rendered** output: 50 % script volume halves the amplitude of the `Ui`/`Voice` bus, 0 % is silent, an
override of 0.5 halves the event, a running loop becomes quieter after the override, `SOUND_REMOVE_TYPE` removes the voice from the mixer, Eva is silent and loses the
noted event, state is reset), `Audio.Tests` (`SetVoiceVolume`) and `MissionEffectsTests` (script outputs → `IMissionAudioControl`). In the client: `MD_USA01` reports after 900 frames
"11 event overrides" and `dropped:script-muted=1`. **Not verified:** how the cinematic sounds sound as a result (no hearing), and whether the muted event names hit all voices that the
original hits (the name equality follows the catalog; events with random variants carry one name).

## Bus assignment

The mixer knows five buses (`AudioBus`: `Music`, `Sfx`, `Voice`, `Ambient`, `Ui`); the four sliders of the options screen
(`GameSettings.Audio`: `Master`, `Music`, `Effects`, `Voice`, `Ambient`) are combined as follows (`AudioDirector.SyncBuses`):

| Bus | Formula | Reason |
|---|---|---|
| `Music` | `Music × (pausiert ? 0.4 : 1) × Skript(Musik)` | duck on pausing, no stop (menu stays audible) |
| `Sfx` | `Effects × Zoom × Skript(3D-Ton)` | 3D effects follow the camera zoom like `AudioManager::update`/`set3DVolumeAdjustment` |
| `Ambient` | `Ambient × Zoom × Skript(3D-Ton)` | the same zoom curve, own slider |
| `Voice` | `Voice × Skript(Sprache)` | speech/EVA/unit announcements, no zoom (reference: `DefaultSpeechVolume` is not 3D) |
| `Ui` | `Effects × Effective2DSoundVolume/DefaultSoundVolume × Skript(2D-Ton)` | keeps the retail ratio from `Relative2DVolume` between 2D and 3D effects, without the zoom |

`Master` goes unchanged to `SoftwareMixer.SetMasterVolume`. Changes are sent only if they deviate by more than
0.002 from the value sent last (noise suppression for the command queue).

Which bus an effect sound cue gets is decided by `PlayRequest.ForEvent` in `CnCNew.Audio` (music → `Music`,
streaming/dialog → `Voice`, otherwise `Sfx` with a position set, otherwise `Ui`); `AudioDirector.Dispatch` overrides that
explicitly to `Voice` for `SoundCueKind.Voice` cues (Fear/Created/TaskComplete announcements stay positioned, though).

## Intake of a sound cue (`AudioDirector.Dispatch`)

1. **Branch off Eva/music/UI cues.** `SoundCueKind.Eva` goes to the `EvaAnnouncer` (`Request`, see below) instead of to the
   mixer; a cue for a player other than `view.Human` is discarded and counted (`dropped:eva-other-player`).
   `SoundCueKind.Music` is not supported (the music sequence runs on its own, see below) and counted.
   `SoundCueKind.UiSound` (from `FxCatalog.MiscAudio`, emitted for AI players too) is discarded if `Player`
   is neither the system nor the human.
2. **Resolve the catalog entry.** An unknown name lands in `Stats.UnresolvedEvents` (counted by name), never
   silently discarded.
3. **`shouldPlayLocally`** (port of `AudioManager::shouldPlayLocally`, `GameAudio.cpp`, without observer mode – the
   human is always the local player): none of the flags `PLAYER`/`ALLIES`/`ENEMIES`/`EVERYONE` set or
   `EVERYONE` set → always audible; `PLAYER` without an owner for a UI sound → audible (reference special case for
   playerless UI sounds); no owner otherwise → `dropped:no-owner`; `PLAYER` → only the owner itself;
   `ALLIES` → only an allied player, not the own one; `ENEMIES` → only an enemy player; otherwise
   `dropped:not-local`. Music is always local. UI sounds are not checked against this rule (in the
   reference they mostly have `PLAYER UI` or `EVERYONE` anyway, and their `SoundCueKind` already carries the system rule).
4. **Shroud.** A positioned request not marked as `SHROUDED` is discarded if `view.IsShrouded`
   reports the position as invisible to the human (`dropped:shrouded`).
5. **Advance distance/volume.** `Spatial.Attenuation` with the catalog ranges estimates the volume at the listener;
   below `MinSampleVolume` or beyond `MaxRange` the request is discarded already here (`dropped:distance`/`dropped:quiet`),
   before the file is decoded at all. `GLOBAL` and `CRITICAL` events are excluded from this as in the mixer.
6. **Selection and playback.** `SoundSelector.Pick` with the director's own `SeededRng` and one
   `SoundSelectionState` per event name chooses variant/delay/pitch/volume; `PlayRequest.ForEvent` decodes through
   `SoundCache.GetOrLoad` (failed files count as `dropped:no-decodable-sound`, no empty substitute sounds).

The `SoundCueRateLimiter` (see `docs/PRESENTATION-FX.md`) filters before `Dispatch` runs: at most 3 cues per
event name in 10 ticks, at most 48 cues per tick (`AudioDirectorOptions.CueLimits`), without a distance cut (our own
cut comes from step 5). During a pause all incoming cues are discarded and counted as `dropped:paused`,
not processed.

## EVA announcements (`EvaCatalog`, `EvaAnnouncer`)

`Eva.ini` has no typed schema in `CnCNew.Data`; `EvaCatalog.Parse` reads the raw file directly (line parser,
comments from `;`, unknown keys throw with the line number). A repetition of the same `EvaEvent` name
merges into the same object (last block wins per field, like `EvaCheckInfo`'s in-place update in the reference);
that is our own, documented simplification, since retail contains no such case. `TimeBetweenChecksMS` and
`ExpirationTimeMS` are rounded up to logic frames as in the simulation core (`ceil(ms · 30 / 1000)`); `-1` for
`ExpirationTimeMS` means, as in the original comment ("`-1` means FOREVER", `BeaconDetected`), never expire
(`int.MaxValue` frames). Default values without an explicit field: `Priority=1`, `FramesBetweenChecks=900`,
`FramesToExpire=150` (`EvaCheckInfo::EvaCheckInfo`).

`EvaAnnouncer` ports `Eva::update`/`processPlayingMessages` (`Eva.cpp`) as a pure clock:

- `Request(name)` marks an event for the next evaluation (`Eva::setShouldPlay`); an unknown name is
  rejected and counted (`UnknownEvents`), never silently ignored.
- `Update(frame, lowPower, speaking, side)` runs **per simulated logic tick** (not per rendered frame – a
  rendered frame can comprise several ticks through `LiveMatch`'s catch-up mechanism; the director recognises that through
  `view.Tick` and evaluates at most once per new tick number). From frame&nbsp;2 on, marked events are enqueued as
  checks (no duplicate per event as long as a check is still running), flags are cleared on every
  call. Expired checks not yet played are counted (`Expired`) and removed. As long as `speaking`
  is true, nothing is selected. Otherwise the highest priority among the checks not yet played wins;
  if the event has no `SideSounds` for `side`, that is counted (`SideMissing`) and nothing is played – the check
  nevertheless counts as "played" and starts its cooldown, exactly as in the original.
- Deviation from the reference: events are visited in file order instead of in `EvaMessage` enum order
  (with a priority tie that can change the selection; in the retail content no pair of simultaneously
  waiting events has the same priority, hence without an observable difference). The sound variant is chosen with the
  director's own `SeededRng`, not with the audio RNG of the reference.
- `LowPower` is derived from `SimulationWorld.EnergyProductionOf(Human) < 0` (`Energy::hasSufficientPower` substitute)
  and checked every tick as in the reference, independently of `Request`.

EVA events covered by the director: `BuildingLost`, `UnitLost`, `BaseUnderAttack`, `UpgradeComplete` (all through
`SoundCueDeriver`, see `docs/PRESENTATION-FX.md`), `LowPower` (checked every beat) and `InsufficientFunds`
(`NotifyInsufficientFunds`, since `LiveMatch` does not report purchase rejections as a `SimEvent`). **Not derivable and therefore
not triggered** (present in `Eva.ini`, but without a source in the simulation/presentation so far):
superweapon events (`SuperweaponDetected/Launched/Ready_*`), `AllyUnderAttack`, `BeaconDetected`,
`Enemy/OwnBlackLotusDetected`, `Enemy/OwnJarmenKellDetected`, `Enemy/OwnColonelBurtonDetected`, `GeneralLevelUp`,
`VehicleStolen`, `BuildingStolen`, `BuildingBeingStolen`, `BuildingSabotaged`, `CashStolen`,
`SuperweaponLaunched_Enemy_Sneak_Attack`, `SuperweaponLaunched_Enemy_GPS_Scrambler`. They stay loaded in `EvaCatalog`
(audit/real-data test show them as never requested), but are not an error of this package – the underlying
simulation events do not exist yet.

## Music (`MusicPlaylist`)

`Music.ini` contains no playlist order (the reference chooses tracks from the WW script); the playback sequence is
therefore our own, documented decision: `MusicPlaylist.ForSide` collects all `Game_<SIDE>_*` tracks with a
resolved file (without `Game_Silent`), `Next()` delivers an order shuffled deterministically by Fisher-Yates with the
director seed, which is reshuffled on exhaustion and in doing so does not immediately repeat the track played
last. The prefix per side: `America* → Game_USA_`, `China* → Game_CHI_`, `GLA* → Game_GLA_`; an unknown
side (e.g. `Observer`) falls back to all resolved `Game_*` tracks (`UsesAllSides`, counted as
`note:music-all-sides`).

`AudioDirector.PlayMenuMusic()` plays `Shell` in a loop; `StartMatchMusic(side)` starts the sequence without a loop
per track, with `MusicGapSeconds` (default 3&nbsp;s) of pause between two tracks. The end of a track is recognised as soon as the
mixer no longer lists the voice as active after the next `Render` (`IsActive` or the `RenderedFrames` comparison
for a request not yet rendered, since `IsActive` is updated only after the next `Render`). A
failing track (file missing, does not decode) is counted (`MusicFailed`) and the next one is tried; if a
full round fails, music counts as unavailable (`Stats.MusicAvailable = false`), without further attempts in
`Update` – no silent endless retry.

## Ambient drawable sounds

`Drawable::startAmbientSound` (reference) is reproduced as a periodic re-evaluation
(`AudioDirector.UpdateAmbient`, every `AmbientUpdateSeconds` = 0.25&nbsp;s): for every visible, non-ghost
`DrawableInstance` with `SoundAmbient` in the object catalog and within its (possibly global) range, sorting is by
distance and entity/scenery id; the nearest `MaxAmbientVoices` (default 12) keep a looping
positioned voice on the `Ambient` bus that follows the position (`SetVoicePosition`, only on movement beyond
0.5&nbsp;units) and ends with a short fade as soon as the drawable leaves the selection. A non-looping
ambient event is retried at the earliest after `TimeBetweenDrawableSounds` (on failure too), so that a
permanently failing sound is not retried on every update.

## Unit announcements

`OnUnitSelected(template, count)` plays `VoiceGroupSelect` if more than one unit is selected **and** the
object has this key (reference rule: if the key is missing, it stays with `VoiceSelect`), otherwise `VoiceSelect`.
`OnUnitOrdered(template, kind)` maps `VoiceMove`/`VoiceAttack`/`VoiceGuard`/`VoiceEnter`/`VoiceGarrison`. Both
play 2D on the `Voice` bus (`Streaming = true`, no position pool) and replace the previous unit announcement with
a short fade (`UnitVoiceFadeSeconds`, default 0.08&nbsp;s) – our own simplification: the reference plays
announcements per unit independently, this client lets only one "speak" at a time, which makes mass selections audible
without flooding the voice pool. An object without the matching key is counted
(`Stats.MissingUnitVoices`, by key name), never silently skipped. `VoiceFear` (from
`SoundCueKind.Voice` cues of `SimEventKind.VoiceFearRequested`), `VoiceCreated`/`VoiceTaskComplete` stay
positioned on the `Voice` bus, since they come from the simulation and have a world location.

## Movie sequences (Bink)

`OnMovieRequested(name)` exists because menu/campaign will request movie sequences as soon as these packages are in place.
The runtime contains **no Bink decoder** (the binary format is proprietary and cannot lawfully be decoded without the
official Bink SDK); every call is counted (`Stats.SkippedMovies`, by movie name) and otherwise does nothing –
no placeholder sound, no fake. That is a known, permanent limit of this project, not an implementation error
of this package.

## Device output (`CnCNew.Client.Stride/Audio/AudioOutput.cs`)

The mixer already renders finished interleaved stereo float PCM; the output layer therefore carries no state per voice. `AudioOutput` opens, through the classic
Windows API `waveOut` (`winmm.dll`, P/Invoke, no NuGet package), the default device with 16-bit stereo PCM at the sample rate of the mixer and keeps `BufferCount` = 4 buffers of
882 frames each (20&nbsp;ms at 44100&nbsp;Hz, about 80&nbsp;ms latency) in the queue. A thread of its own (`AudioOutput.Pump`, slightly raised priority, every 4&nbsp;ms) looks for buffers
that the device has finished (`WHDR_DONE`), fills each with one `SoftwareMixer.Render` block and enqueues them again. The float→short clamping happens here, not in the mixer (which stays
device-independent).

**Why not Stride's audio layer (change of 5 October 2026):** the first version used `Stride.Audio` (`AudioEngine`, `DynamicSoundSource`, `SoundInstance`). On the development machine
`AudioLayer.SourceCreate` triggered a native access violation when building the `SoundInstance` (`0xC0000005`, with every constructor variant; engine and listener could be built, three playback devices and the
audio service were present). A native crash cannot be caught: every demo with a window ended after the line "Starte Demo" without a message, only the smoke tests (device-free) ran. Cause in the
Stride driver not investigated further; the output no longer uses Stride audio. Stride's own `AudioSystem` (part of every `Game` instance) is not affected by this.

**No device available:** `waveOutOpen` delivers an error code (no hardware, device exclusively occupied), or a later call fails. `Start()` catches that at this one device boundary
(`catch (Exception) when (exception is not OutOfMemoryException)`), counts it in `DeviceFailures`, remembers the reason in `FailureReason`, releases everything created and lets the client continue silently;
`IsAvailable` stays `false`, no state is faked; the one console warning is printed by `ClientAudio`. **Underruns** (`Underruns`): counts every pump pass in which all buffers were already finished,
i.e. the device played silence in the meantime. That is a signal for dropouts, not evidence that one hears them.

## Real-data test (`tests/CnCNew.Game.Tests/AudioDirectorTests.cs`)

The real-data test (`--content-dir`) loads `GameData`, creates a `LiveMatch` (Alpine Assault, both sides
computer-controlled), lets `AudioDirector.Update` and `SoftwareMixer.Render` run 600 rendered frames (20&nbsp;s of
game time) in parallel with `LiveMatch.Advance` and renders the same sequence twice independently in doing so. What is
checked: the SHA-256 of the entire output is byte-identical between both runs (determinism), the amplitude level
(peak) lies above an audibility threshold (no silence), the cue resolution is counted instead of concealed
(`Stats.UnresolvedEvents`), and one render block (33&nbsp;ms of output) needs clearly less than 33&nbsp;ms of
computing time. A recording of the first three seconds goes as a 16-bit stereo WAV into the scratchpad of the session (path
through the environment variable `CNCNEW_AUDIO_SCRATCH`, otherwise `%TEMP%`), never into the repository.

## Time and tick rate (Phase 21)

The sound no longer assumes anywhere that a tick lasts 1/30 s. At 30 Hz the sound recordings of the client are byte-identical to before (`docs/TICKRATE-MIGRATION.md` §14.2).

- **Clock.** `IAudioGameView.Tick` is replaced by `IAudioGameView.Clock` (`GameClock` from `LiveGame.Clock`: tick, rate of the world, interpolation fraction).
- **Eva.** `Eva.ini` names milliseconds, the original converts them into logic frames (`parseDurationUnsignedInt`) and counts `TheGameLogic->getFrame()`. `EvaCatalog` converts with `OriginalFrame.PerSecond` into **original frames** (default values 900 and 150 frames = 30 s and 5 s), and `AudioDirector.UpdateEva` gives the announcer the original frame number of the game time (`view.Clock.OriginalFrames`) – once per original frame instead of once per tick. Lockout time, expiry, the grace period "before frame 2" and the "one frame earlier" of `processPlayingMessages` thereby last equally long at every rate.
- **Flood protection.** `AudioDirectorOptions.CueLimits` counts per time: at most 3 per event name in 10 original frames, at most 48 per original frame (`SoundCueLimits.WindowFrames`/`MaxPerFrame`, formerly `WindowTicks`/`MaxPerTick`). The counter of the statistics is called `DroppedByFrameCap`.
- **Under-attack message** (`SoundCueDeriver`, `HudMessageFeed`): 10 s as original frames, compared in ticks of the world.
- **Mission.** The typing sound of the subtitles follows the typing, which runs once per original frame of the presentation clock (`docs/SCRIPT-ENGINE.md` §7a).
- Unchanged on wall-clock time: music, crossfades, pause fade, ambient loops, mixer.

Time-scale test (`AudioDirectorTests`, "Time scale: Eva's wait …"): with the event requested in every tick, two announcements lie 30.0000 s apart at 30, 60 and 120 Hz. **Not verified:** how it sounds at 60 Hz – no human has heard it.
