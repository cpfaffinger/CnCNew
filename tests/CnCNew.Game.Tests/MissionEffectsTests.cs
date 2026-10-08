using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Input;
using CnCNew.Presentation.Terrain;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Terrain;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Wnd;
using static CnCNew.Game.Tests.MissionPresentationTests;

namespace CnCNew.Game.Tests;

/// <summary>Records what the director hands to the host's sound (<see cref="IMissionAudioControl"/>); a volume or a muted event that never reaches it fails the test.</summary>
internal sealed class FakeAudio : IMissionAudioControl
{
    public List<string> Calls { get; } = [];
    public bool PlayScripted(string action, string eventName) { Calls.Add($"play {action} {eventName}"); return true; }
    public void SetVolume(MissionVolume which, float percent) => Calls.Add($"volume {which} {percent:0}");
    public void OverrideEventVolume(string eventName, float percent) => Calls.Add($"override '{eventName}' {percent:0}");
    public void SetEventEnabled(string eventName, bool enabled) => Calls.Add($"enabled '{eventName}' {enabled}");
    public int RemoveDisabledEvents() { Calls.Add("remove disabled"); return 0; }
    public int RemoveEvent(string eventName) { Calls.Add($"remove '{eventName}'"); return 0; }
    public void SetEvaEnabled(bool enabled) => Calls.Add($"eva {enabled}");
    public bool SetObjectSoundEnabled(EntityId entity, bool enabled) { Calls.Add($"object sound {enabled}"); return true; }
    public void SetAmbientPaused(bool paused) => Calls.Add($"ambient paused {paused}");
}

file sealed class FakeObjects : IMissionObjectEffects
{
    public List<string> Calls { get; } = [];
    public bool Flash(EntityId entity, Vector3? color, int seconds) { Calls.Add($"flash {(color is { } c ? $"{c.X:0.#},{c.Y:0.#},{c.Z:0.#}" : "house")} {seconds}s"); return true; }
    public bool SetHouseColor(EntityId entity, uint argb) { Calls.Add($"color {argb:X8}"); return true; }
}

file sealed class FakeHud : IMissionHudControl
{
    public List<string> Calls { get; } = [];
    public bool RemoveCommandButton(string buttonName, string objectType) { Calls.Add($"remove {buttonName} {objectType}"); return true; }
    public bool AddCommandButton(string buttonName, string objectType, int slot) { Calls.Add($"add {buttonName} {objectType} {slot}"); return true; }
    public bool SetScienceAvailability(string science, string availability) { Calls.Add($"science {science} {availability}"); return true; }
    public void SetIconUiEnabled(bool enabled) => Calls.Add($"icons {enabled}");
    public bool SetObjectSelectable(EntityId entity, bool selectable) { Calls.Add($"selectable {selectable}"); return true; }
    public void SetSpecialPowerDisplay(bool enabled) => Calls.Add($"power display {enabled}");
    public bool SetObjectSpecialPowerDisplay(EntityId entity, bool visible) { Calls.Add($"object power display {visible}"); return true; }
}

/// <summary>
/// The script outputs that change the sound, the control bar, single objects, the camera and the screen filters (<c>docs/SCRIPT-ENGINE.md</c> §7a): synthetic missions drive the director and fake hosts, real missions
/// (<c>--content-dir</c>) drive the real HUD, object layer and camera. A type counts as displayed only when its consumer acted.
/// </summary>
internal static class MissionEffectsTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(MissionEffectsTests));
        harness.Run("The output catalog describes every forwarded output type and says which state it is in", CatalogIsComplete);
        harness.Run("Sound outputs reach the audio control with the script's values; displayed means the control acted", SoundOutputsReachTheAudioControl);
        harness.Run("Object, HUD and flag outputs reach their consumers; what has none is reported, not shown", ObjectAndHudOutputsReachTheirConsumers);
        harness.Run("While time is frozen the presentation clock goes on: a camera move finishes and thaws the world", PresentationClockRunsWhileTimeIsFrozen);
        harness.Run("SCREEN_SHAKE shakes the real camera deterministically with the GameData intensity of its type", ScreenShakeShakesTheCamera);
        harness.Run("MAP_SWITCH_BORDER confines the camera to the border; a border the map lacks is reported", BorderSwitchConfinesTheCamera);
        harness.Run("The grey-scale and the motion blur follow the original's frame counts; the cut happens at the blur's apex", ScreenFiltersFollowTheOriginalFrameCounts);
        harness.Run("The superweapon list: a ready power is bold and flashes white every 15 frames, and the list ends in an ellipsis above the control bar", SuperweaponDrawRules);

        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP MissionEffectsTests real-data (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("MD_USA01 headless: freeze, shake, grey-scale, border, volumes, flashes and the removed buttons all reach their consumers", () => RealIntro(data));
        harness.Run("A removed command-bar button is gone from the bar of a selected object, and comes back when the script adds it", () => RealCommandBar(data));
        harness.Run("Sciences a script hides or disables are hidden or restricted PurchaseScience buttons; an unselectable object leaves the selection", () => RealScienceAndSelection(data));
        harness.Run("A script's custom house colour and colour flash show in the object layer's frame", () => RealColorsAndFlashes(data));
        harness.Run("The superweapon list shows every public-timer power with its owner's colour, and the scripts' switches hide it", () => RealSuperweaponList(data));
        return harness.Failures;
    }

    // ---------------------------------------------------------------------------------------------------- helpers

    private static MapScriptParameter Text(string value) => Named(MapScriptParameterType.ObjectType, value);

    /// <summary>One step of the host loop: director before, session, director after.</summary>
    private static void StepOnce(MissionSession session, MissionPresentationDirector director)
    {
        director.BeforeStep();
        session.Step();
        director.AfterStep();
    }

    /// <summary>
    /// One original frame of the mission: the screen filters and texts advance once per original frame, whatever the tick rate. It is also the step that holds exactly one
    /// update of the script VM, so a signal given before it (<c>SignalUiInteraction</c>) has been answered after it.
    /// </summary>
    private static void StepFrame(MissionSession session, MissionPresentationDirector director)
    {
        for (var tick = 0; tick < CnCNew.Tests.TestTime.Frames(session.World, 1); tick++) StepOnce(session, director);
    }

    private static MapScript OnFlag(string name, string flag, params MapScriptAction[] actions)
        => new(name, string.Empty, string.Empty, string.Empty, true, true, true, true, true, false, 0, [new MapOrCondition([If("FLAG", Named(MapScriptParameterType.Flag, flag), Bool(true))])], [..actions], []);

    // ---------------------------------------------------------------------------------------------------- the table

    private static void CatalogIsComplete()
    {
        foreach (var type in ScriptCoverage.ForwardedActions.Append("VICTORY").Append("DEFEAT"))
        {
            var info = MissionOutputCatalog.Describe(type);
            TestHarness.Assert(MissionOutputCatalog.All.Any(entry => entry.Action == type), $"{type} has an entry in the output catalog");
            TestHarness.Assert(info.Description.Length > 0 && (info.State != MissionOutputState.NotDisplayed || info.Description.StartsWith("not displayed: ", StringComparison.Ordinal)), $"{type} says what it does or why it does not ({info.Description})");
        }
        TestHarness.Assert(MissionOutputCatalog.Describe("NOT_A_REAL_OUTPUT").State == MissionOutputState.NotDisplayed, "an unknown type is never reported as shown");
        foreach (var type in new[] { "SOUND_SET_VOLUME", "MUSIC_SET_VOLUME", "SPEECH_SET_VOLUME", "AUDIO_OVERRIDE_VOLUME_TYPE", "SOUND_DISABLE_TYPE", "EVA_SET_ENABLED_DISABLED", "COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE", "SCREEN_SHAKE",
                     "CAMERA_BW_MODE_BEGIN", "CAMERA_MOTION_BLUR_JUMP", "NAMED_FLASH_WHITE", "NAMED_CUSTOM_COLOR", "MAP_SWITCH_BORDER" })
            TestHarness.Assert(MissionOutputCatalog.Describe(type).State == MissionOutputState.Displayed, $"{type} has a consumer");
        TestHarness.Assert(MissionOutputCatalog.Describe("FREEZE_TIME").State == MissionOutputState.HandledElsewhere, "FREEZE_TIME is the simulation's");
        TestHarness.Assert(MissionOutputCatalog.Describe("SET_FPS_LIMIT").State == MissionOutputState.NotDisplayed, "SET_FPS_LIMIT has no consumer, with its reason");
    }

    // ---------------------------------------------------------------------------------------------------- sound

    private static void SoundOutputsReachTheAudioControl()
    {
        var all = Script("Sound", If("CONDITION_TRUE"),
        [
            Do("SOUND_SET_VOLUME", Real(20)), Do("MUSIC_SET_VOLUME", Real(80)), Do("SPEECH_SET_VOLUME", Real(0)),
            Do("AUDIO_OVERRIDE_VOLUME_TYPE", Text("EventA"), Real(70)), Do("AUDIO_RESTORE_VOLUME_TYPE", Text("EventA")), Do("AUDIO_RESTORE_VOLUME_ALL_TYPE"),
            Do("SOUND_DISABLE_TYPE", Text("EventB")), Do("SOUND_ENABLE_TYPE", Text("EventB")), Do("SOUND_ENABLE_ALL"), Do("SOUND_REMOVE_ALL_DISABLED"), Do("SOUND_REMOVE_TYPE", Text("EventC")),
            Do("EVA_SET_ENABLED_DISABLED", Bool(false)), Do("SOUND_AMBIENT_PAUSE"), Do("SOUND_AMBIENT_RESUME"),
        ]);
        var objectSound = OnFlag("ObjectSound", "go", Do("DISABLE_OBJECT_SOUND", Named(MapScriptParameterType.Unit, "Hero1")), Do("ENABLE_OBJECT_SOUND", Named(MapScriptParameterType.Unit, "Hero1")),
            Do("DISABLE_OBJECT_SOUND", Named(MapScriptParameterType.Unit, "Nobody")));
        var session = Start([all, objectSound], [Placed("Barrel", 50m, 50m, "Hero", "Hero1")]);
        var director = new MissionPresentationDirector(session);
        var audio = new FakeAudio();
        director.AudioControl = audio;
        StepFrame(session, director);
        StepFrame(session, director);
        session.Engine.SignalUiInteraction("go");
        StepFrame(session, director);

        var expected = new[]
        {
            "volume Sound 20", "volume Music 80", "volume Speech 0", "override 'EventA' 70", "override 'EventA' -100", "override '' -100", "enabled 'EventB' False", "enabled 'EventB' True", "enabled '' True",
            "remove disabled", "remove 'EventC'", "eva False", "ambient paused True", "ambient paused False", "object sound False", "object sound True",
        };
        TestHarness.Assert(audio.Calls.SequenceEqual(expected), $"the control got the script's values in the script's order ({string.Join(" | ", audio.Calls)})");
        var counts = director.Counts().ToDictionary(count => count.Action);
        foreach (var type in new[] { "SOUND_SET_VOLUME", "MUSIC_SET_VOLUME", "SPEECH_SET_VOLUME", "AUDIO_OVERRIDE_VOLUME_TYPE", "AUDIO_RESTORE_VOLUME_TYPE", "AUDIO_RESTORE_VOLUME_ALL_TYPE", "SOUND_DISABLE_TYPE", "SOUND_ENABLE_TYPE",
                     "SOUND_ENABLE_ALL", "SOUND_REMOVE_ALL_DISABLED", "SOUND_REMOVE_TYPE", "EVA_SET_ENABLED_DISABLED", "SOUND_AMBIENT_PAUSE", "SOUND_AMBIENT_RESUME" })
            TestHarness.Assert(counts[type] is { Emitted: 1, Forwarded: 1, Displayed: 1 }, $"{type}: the control acted, so it is displayed ({counts[type]})");
        TestHarness.Assert(counts["DISABLE_OBJECT_SOUND"] is { Emitted: 2, Forwarded: 1, Displayed: 1 } && counts["ENABLE_OBJECT_SOUND"] is { Emitted: 1, Displayed: 1 }, "a named object that does not exist is not displayed");
        TestHarness.Assert(director.Problems.Keys.Any(key => key.StartsWith("DISABLE_OBJECT_SOUND: unknown named object", StringComparison.Ordinal)), "and it is reported");

        // Without a control nothing is claimed.
        var bare = Start([all], null);
        var bareDirector = new MissionPresentationDirector(bare);
        StepOnce(bare, bareDirector);
        var bareCounts = bareDirector.Counts().ToDictionary(count => count.Action);
        TestHarness.Assert(bareCounts["SOUND_SET_VOLUME"] is { Emitted: 1, Forwarded: 0, Displayed: 0 } && bareDirector.Problems.ContainsKey("SOUND_SET_VOLUME: no audio control connected"), "without an audio control the volume is reported, not shown");
    }

    // ---------------------------------------------------------------------------------------------------- objects and HUD

    private static void ObjectAndHudOutputsReachTheirConsumers()
    {
        var hero = Named(MapScriptParameterType.Unit, "Hero1");
        var script = OnFlag("Scene", "go",
            Do("NAMED_FLASH_WHITE", hero, Int(5)), Do("NAMED_FLASH", hero, Int(3)), Do("NAMED_CUSTOM_COLOR", hero, Int(-16711681)),
            Do("COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE", Text("Command_A"), Text("TypeA")), Do("COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT", Text("Command_B"), Text("TypeA"), Int(3)),
            Do("PLAYER_SCIENCE_AVAILABILITY", Named(MapScriptParameterType.Side, "Hero"), Text("SCIENCE_X"), Text("Hidden")),
            Do("PLAYER_SCIENCE_AVAILABILITY", Named(MapScriptParameterType.Side, "Nobody"), Text("SCIENCE_X"), Text("Hidden")),
            Do("OPTIONS_SET_DRAWICON_UI_MODE", Bool(false)),
            Do("UNIT_AFFECT_OBJECT_PANEL_FLAGS", hero, Text("Selectable"), Bool(false)), Do("UNIT_AFFECT_OBJECT_PANEL_FLAGS", hero, Text("Indestructible"), Bool(true)),
            Do("ENABLE_SCORING"), Do("DISABLE_SCORING"), Do("PLAYER_EXCLUDE_FROM_SCORE_SCREEN", Named(MapScriptParameterType.Side, "Hero")), Do("DISABLE_SPECIAL_POWER_DISPLAY"));
        var session = Start([script], [Placed("Barrel", 50m, 50m, "Hero", "Hero1")]);
        var director = new MissionPresentationDirector(session);
        StepFrame(session, director);
        StepFrame(session, director);
        session.Engine.SignalUiInteraction("go");
        StepFrame(session, director);

        var counts = director.Counts().ToDictionary(count => count.Action);
        TestHarness.Assert(counts["NAMED_FLASH_WHITE"] is { Emitted: 1, Forwarded: 0, Displayed: 0 } && director.Problems.ContainsKey("NAMED_FLASH_WHITE: no object layer connected"), "without an object layer a flash is reported, not shown");
        TestHarness.Assert(counts["COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE"] is { Forwarded: 0, Displayed: 0 } && director.Problems.ContainsKey("COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE: no HUD connected"), "without a HUD a removed button is reported, not shown");

        var again = Start([script], [Placed("Barrel", 50m, 50m, "Hero", "Hero1")]);
        var host = new MissionPresentationDirector(again);
        var objects = new FakeObjects();
        var hud = new FakeHud();
        host.ObjectEffects = objects;
        host.Hud = hud;
        StepFrame(again, host);
        StepFrame(again, host);
        again.Engine.SignalUiInteraction("go");
        StepFrame(again, host);

        TestHarness.Assert(objects.Calls.SequenceEqual(["flash 1,1,1 5s", "flash house 3s", "color FF00FFFF"]), $"flash white, flash in the house colour and the custom colour (an ARGB integer) reach the object layer ({string.Join(" | ", objects.Calls)})");
        TestHarness.Assert(hud.Calls.SequenceEqual(["remove Command_A TypeA", "add Command_B TypeA 3", "science SCIENCE_X Hidden", "icons False", "selectable False", "power display False"]),
            $"the HUD got the removals, the science of the local player only, the icon switch, the Selectable flag and the superweapon list switch ({string.Join(" | ", hud.Calls)})");
        counts = host.Counts().ToDictionary(count => count.Action);
        TestHarness.Assert(counts["NAMED_FLASH_WHITE"] is { Displayed: 1 } && counts["NAMED_CUSTOM_COLOR"] is { Displayed: 1 }, "flashes and colours are displayed once the object layer acted");
        TestHarness.Assert(counts["COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE"] is { Displayed: 1 } && counts["COMMANDBAR_ADD_BUTTON_OBJECTTYPE_SLOT"] is { Displayed: 1 } && counts["OPTIONS_SET_DRAWICON_UI_MODE"] is { Displayed: 1 }, "the control bar and the icon switch are displayed once the HUD acted");
        TestHarness.Assert(counts["PLAYER_SCIENCE_AVAILABILITY"] is { Emitted: 2, Forwarded: 1, Displayed: 0, State: MissionOutputState.NotDisplayed }, $"the science is kept by the control bar but never counted as displayed ({counts["PLAYER_SCIENCE_AVAILABILITY"]})");
        TestHarness.Assert(host.Problems.ContainsKey("PLAYER_SCIENCE_AVAILABILITY: unknown player 'Nobody'"), "a side that does not exist is reported");
        TestHarness.Assert(counts["UNIT_AFFECT_OBJECT_PANEL_FLAGS"] is { Emitted: 2, Forwarded: 1, Displayed: 1 } && host.Problems.Keys.Any(key => key.Contains("flag 'Indestructible' has no consumer", StringComparison.Ordinal)),
            "only the Selectable flag has a consumer; the Indestructible one is listed as a problem");
        TestHarness.Assert(!host.ScoringEnabled && host.ScoreScreenExcluded.Contains("Hero") && !host.SpecialPowerDisplayEnabled, "the scoring and display switches are kept for the screens that read them");
        TestHarness.Assert(counts["DISABLE_SCORING"] is { Displayed: 0, State: MissionOutputState.NotDisplayed }, "and are honestly not displayed");
    }

    // ---------------------------------------------------------------------------------------------------- time

    private static void PresentationClockRunsWhileTimeIsFrozen()
    {
        var waypoint = Waypoint("Target", 1, 150m, 150m);
        var freeze = Script("Freeze", If("CONDITION_TRUE"), [Do("FREEZE_TIME"), Do("MOVE_CAMERA_TO", Named(MapScriptParameterType.Waypoint, "Target"), Real(1), Real(0), Real(0), Real(0))]);
        var thaw = Script("Thaw", If("CAMERA_MOVEMENT_FINISHED"), [Do("UNFREEZE_TIME"), Do("SET_FLAG", Named(MapScriptParameterType.Flag, "thawed"), Bool(true))], oneShot: true);
        // A latency the camera key could never expire by (the engine counts it in world ticks, which stand still): only the director's own clock can finish the move.
        var (map, fixture, terrain) = Mission([freeze, thaw], [waypoint]);
        var session = MissionSession.Start(MissionOptions.Standard with { PresentationLatencyTicks = 1_000_000 }, fixture.Templates, fixture.Fingerprint, terrain, map);
        var director = new MissionPresentationDirector(session);

        StepOnce(session, director);
        TestHarness.Assert(session.Engine.TimeFrozen, "the script froze time");
        var frozenAt = session.World.Tick;
        var steps = 1;
        for (; steps < CnCNew.Tests.TestTime.Frames(session.World, 200) && !session.Engine.FlagValue("thawed"); steps++) StepOnce(session, director);
        TestHarness.Assert(session.Engine.FlagValue("thawed"), $"CAMERA_MOVEMENT_FINISHED came from the director's clock although the world tick stood still (after {steps} steps)");
        TestHarness.Assert(steps >= CnCNew.Tests.TestTime.Frames(session.World, 30) && steps <= CnCNew.Tests.TestTime.Frames(session.World, 36), $"a one second camera move takes one second of steps ({steps})");
        // Two ticks, not a time: the step that freezes and the step that thaws.
        TestHarness.Assert(session.World.Tick <= frozenAt + 2, $"the world stood still meanwhile (frozen at {frozenAt}, now {session.World.Tick})");
        TestHarness.Assert(director.Clock >= frozenAt + CnCNew.Tests.TestTime.Frames(session.World, 30), $"the presentation clock went on ({director.Clock})");
        var counts = director.Counts().ToDictionary(count => count.Action);
        TestHarness.Assert(counts["FREEZE_TIME"] is { Emitted: 1, Forwarded: 0, Displayed: 0, State: MissionOutputState.HandledElsewhere } && counts["FREEZE_TIME"].HandledElsewhere == 1 && counts["FREEZE_TIME"].NotDisplayed == 0,
            $"FREEZE_TIME is counted as handled by the simulation, not as missing ({counts["FREEZE_TIME"]})");

        // A fade started before the freeze keeps its schedule on the presentation clock.
        var fade = Script("Fade", If("CONDITION_TRUE"), [Do("FREEZE_TIME"), Do("CAMERA_FADE_ADD", Real(0), Real(1), Int(10), Int(10), Int(10))]);
        var (map2, fixture2, terrain2) = Mission([fade]);
        var session2 = MissionSession.Start(MissionOptions.Standard, fixture2.Templates, fixture2.Fingerprint, terrain2, map2);
        var director2 = new MissionPresentationDirector(session2);
        for (var step = 0; step < 15; step++) StepOnce(session2, director2);
        TestHarness.Assert(director2.BuildOverlayCommands(1280, 720).OfType<CnCNew.Ui.Runtime.UiFillRect>().Any(fill => fill.Rect.Width == 1280), "the fade is on screen at the top of its hold although the world tick never moved");
    }

    // ---------------------------------------------------------------------------------------------------- camera

    private static void ScreenShakeShakesTheCamera()
    {
        var shake = Script("Shake", If("CONDITION_TRUE"), [Do("SCREEN_SHAKE", Int(3)), Do("SCREEN_SHAKE", Int(9))]);
        (float[] Offsets, string[] Problems, MissionOutputCount Count) Run(bool withIntensities)
        {
            var (map, fixture, terrain) = Mission([shake]);
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            var director = new MissionPresentationDirector(session) { ShakeIntensities = withIntensities ? new CameraShakeIntensities(0.5f, 1f, 2.5f, 3f, 4f, 6f) : null };
            StepOnce(session, director);
            var camera = new RtsCamera(MinimalCameraSettings() with { MaxShakeIntensity = 10f, MaxShakeRange = 500f }, new TerrainHeightSampler(map.HeightField), new Vector2(1280, 720));
            var offsets = new List<float>();
            director.ApplyCameraTo(camera, CnCNew.Tests.TestTime.FrameSeconds);
            for (var frame = 0; frame < 8; frame++)
            {
                camera.Update(CnCNew.Tests.TestTime.FrameSecondsF, false);
                offsets.Add(camera.ShakeOffset.Length());
            }
            return ([..offsets], [..director.Problems.Keys], director.Counts().Single(count => count.Action == "SCREEN_SHAKE"));
        }

        var first = Run(true);
        TestHarness.Assert(first.Offsets.Length == 8 && Math.Abs(first.Offsets[0] - 3f) < 0.05f, $"SEVERE shakes with the GameData intensity 3 at once ({first.Offsets[0]:F3})");
        TestHarness.Assert(first.Offsets[1] < first.Offsets[0] && first.Offsets[2] < first.Offsets[1] && first.Offsets[7] < 0.6f, $"and decays by 0.75 per logic frame ({string.Join(", ", first.Offsets.Select(value => value.ToString("F2")))})");
        TestHarness.Assert(first.Count is { Emitted: 2, Forwarded: 1, Displayed: 1 } && first.Problems.Contains("SCREEN_SHAKE: shake type must be 0..5"), $"a shake type the original has not is reported and not counted ({first.Count})");
        TestHarness.Assert(first.Offsets.SequenceEqual(Run(true).Offsets), "two runs shake identically");
        var missing = Run(false);
        TestHarness.Assert(missing.Problems.Contains("SCREEN_SHAKE: no shake intensities connected") && missing.Count.Displayed == 0, "without the GameData intensities a shake is reported, never invented");
    }

    private static void BorderSwitchConfinesTheCamera()
    {
        var script = OnFlag("Border", "go", Do("MAP_SWITCH_BORDER", Int(1)));
        var missing = OnFlag("Missing", "missing", Do("MAP_SWITCH_BORDER", Int(7)));
        var (map, fixture, terrain) = Mission([script, missing], null, boundaries: 2);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var director = new MissionPresentationDirector(session);
        var camera = new RtsCamera(MinimalCameraSettings(), new TerrainHeightSampler(map.HeightField), new Vector2(1280, 720));
        TestHarness.Assert(camera.ActiveBoundary == 0, "the camera starts on border 0");
        StepFrame(session, director);
        session.Engine.SignalUiInteraction("go");
        StepFrame(session, director);
        director.ApplyCameraTo(camera, CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(camera.ActiveBoundary == 1, "MAP_SWITCH_BORDER(1) switched the camera's border");
        session.Engine.SignalUiInteraction("missing");
        StepFrame(session, director);
        director.ApplyCameraTo(camera, CnCNew.Tests.TestTime.FrameSeconds);
        var result = director.Counts().ToDictionary(count => count.Action)["MAP_SWITCH_BORDER"];
        TestHarness.Assert(camera.ActiveBoundary == 1 && result is { Emitted: 2, Forwarded: 1, Displayed: 1 } && director.Problems.ContainsKey("MAP_SWITCH_BORDER: the map has no border 7"), $"a border the map lacks is reported and leaves the camera where it was ({result})");
    }

    // ---------------------------------------------------------------------------------------------------- screen filters

    private static void ScreenFiltersFollowTheOriginalFrameCounts()
    {
        var target = Waypoint("Target", 1, 1500m, 1500m);
        var begin = Script("Begin", If("CONDITION_TRUE"), [Do("CAMERA_BW_MODE_BEGIN", Int(10))]);
        var end = OnFlag("End", "end", Do("CAMERA_BW_MODE_END", Int(10)));
        var jump = OnFlag("Jump", "jump", Do("CAMERA_MOTION_BLUR_JUMP", Named(MapScriptParameterType.Waypoint, "Target"), Bool(true)));
        var (map, fixture, terrain) = Mission([begin, end, jump], [target], 1, 200);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var director = new MissionPresentationDirector(session);
        var camera = new RtsCamera(MinimalCameraSettings(), new TerrainHeightSampler(map.HeightField), new Vector2(1280, 720));
        camera.CenterOn(new Vector2(500, 500));

        // Not read by a renderer yet: forwarded, never displayed.
        StepFrame(session, director);
        var headless = director.Counts().ToDictionary(count => count.Action)["CAMERA_BW_MODE_BEGIN"];
        TestHarness.Assert(headless is { Emitted: 1, Forwarded: 1, Displayed: 0 }, $"until a renderer asks for the filters the grey-scale is not displayed ({headless})");

        var levels = new List<float> { director.PostEffects.BlackWhite };
        for (var step = 0; step < 11; step++)
        {
            StepFrame(session, director);
            levels.Add(director.PostEffects.BlackWhite);
        }
        TestHarness.Assert(Math.Abs(levels[0] - 0.1f) < 1e-4f && Math.Abs(levels[4] - 0.5f) < 1e-4f && levels[9] == 1f && levels[11] == 1f, $"the grey-scale ramps in over its 10 frames, then holds ({string.Join(", ", levels.Select(value => value.ToString("F1")))})");
        TestHarness.Assert(director.Counts().ToDictionary(count => count.Action)["CAMERA_BW_MODE_BEGIN"].Displayed == 1, "reading the filters is what makes the grey-scale displayed");

        session.Engine.SignalUiInteraction("end");
        var down = new List<float>();
        for (var step = 0; step < 12; step++)
        {
            StepFrame(session, director);
            down.Add(director.PostEffects.BlackWhite);
        }
        TestHarness.Assert(down[0] < 1f && down[4] < 0.6f && down[^1] == 0f && !director.PostEffects.Any, $"CAMERA_BW_MODE_END ramps it out and turns the filter off ({string.Join(", ", down.Select(value => value.ToString("F1")))})");

        // The blur: +5 per logic frame to 60 (12 frames), the cut at the apex, -5 per frame (11 frames), then off. It replaces a grey-scale that is on.
        session.Engine.SignalUiInteraction("jump");
        var blur = new List<int>();
        var factors = new List<float>();
        var cutAt = -1;
        for (var step = 0; step < 26; step++)
        {
            StepFrame(session, director);
            var effects = director.PostEffects;
            blur.Add(effects.MotionBlurCount);
            factors.Add(effects.MotionBlurZoomFactor);
            director.ApplyCameraTo(camera, CnCNew.Tests.TestTime.FrameSeconds);
            if (cutAt < 0 && Vector2.Distance(camera.Pivot, new Vector2(1500, 1500)) < 1f) cutAt = step;
            TestHarness.Assert(effects.MotionBlurCount == 0 || effects.MotionBlurAdditive, "CAMERA_MOTION_BLUR_JUMP with saturate is the additive blur");
        }
        TestHarness.Assert(blur.Take(12).SequenceEqual(Enumerable.Range(1, 12).Select(index => index * 5)), $"the count rises by 5 per frame to 60 ({string.Join(" ", blur.Take(13))})");
        TestHarness.Assert(blur.Skip(12).Take(11).SequenceEqual(Enumerable.Range(1, 11).Select(index => 60 - index * 5)) && blur[23] == 0 && blur[25] == 0, $"and falls by 5 per frame until it ends ({string.Join(" ", blur.Skip(11))})");
        TestHarness.Assert(Math.Abs(factors[11] - MathF.Sqrt(0.1f)) < 1e-4f && Math.Abs(factors[5] - MathF.Sqrt(1f - 30f / 60f * 0.9f)) < 1e-4f, "the zoom factor is sqrt(1 - count / 60 * 0.9) as ScreenMotionBlurFilter::postRender computes it");
        TestHarness.Assert(cutAt == 11, $"the camera cuts to the waypoint at the apex, the frame the count reaches 60 (cut at step {cutAt})");
        TestHarness.Assert(Vector2.Distance(camera.Pivot, new Vector2(1500, 1500)) < 1f, "and stays there");

        // A blur ends a grey-scale (one view filter at a time).
        var mixed = Start([begin, jump], [target]);
        var mixedDirector = new MissionPresentationDirector(mixed);
        StepFrame(mixed, mixedDirector);
        TestHarness.Assert(mixedDirector.PostEffects.BlackWhite > 0f, "the grey-scale is on");
        mixed.Engine.SignalUiInteraction("jump");
        StepFrame(mixed, mixedDirector);
        TestHarness.Assert(mixedDirector.PostEffects is { BlackWhite: 0f, MotionBlurCount: > 0 }, "the blur replaced it");
    }

    // ---------------------------------------------------------------------------------------------------- real missions

    private static (LiveMission Live, MissionController Controller, FakeAudio Audio) LoadMission(GameData data, string map)
    {
        var run = MissionRun.Load(MissionContext.From(data), map, MissionOptions.Standard with { PresentationLatencyTicks = LiveMission.ClientPresentationLatencyFrames });
        var live = LiveMission.Create(data, run, new Vector2(1280, 720), label => label);
        var controller = new MissionController(live, GameSettings.Default);
        var audio = new FakeAudio();
        live.Director.AudioControl = audio;
        return (live, controller, audio);
    }

    private static void RealIntro(GameData data)
    {
        var (live, controller, audio) = LoadMission(data, "Maps/MD_USA01/MD_USA01.map");
        var director = live.Director;
        var maxShake = 0f;
        var maxGrey = 0f;
        var frozenFrames = 0;
        var clockLead = 0L;
        var boundaries = new SortedSet<int>();
        var flashed = false;
        for (var frame = 0; frame < 3900; frame++)
        {
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            _ = director.PostEffects;
            maxShake = Math.Max(maxShake, live.Camera.ShakeOffset.Length());
            maxGrey = Math.Max(maxGrey, director.PostEffects.BlackWhite);
            boundaries.Add(live.Camera.ActiveBoundary);
            if (live.Session.Engine.TimeFrozen) frozenFrames++;
            clockLead = Math.Max(clockLead, director.Clock - live.Tick);
            flashed |= live.Frame.Drawables.Any(drawable => drawable.HasFlashTint);
        }
        var counts = director.Counts().ToDictionary(count => count.Action);
        var report = string.Join("; ", counts.Values.Where(count => count.Action is "SCREEN_SHAKE" or "FREEZE_TIME" or "UNFREEZE_TIME" or "CAMERA_BW_MODE_BEGIN" or "MAP_SWITCH_BORDER" or "MUSIC_SET_VOLUME" or "SOUND_SET_VOLUME" or "NAMED_FLASH_WHITE" or "COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE").Select(count => count.ToString()));
        TestHarness.Assert(frozenFrames > 0 && clockLead > 0 && !live.Session.Engine.TimeFrozen, $"the intro froze the world for {frozenFrames} frames, the presentation clock led by {clockLead} ticks, and time ended unfrozen");
        TestHarness.Assert(counts["FREEZE_TIME"].Emitted > 0 && counts["FREEZE_TIME"].HandledElsewhere == counts["FREEZE_TIME"].Emitted, "FREEZE_TIME counts as handled by the simulation");
        TestHarness.Assert(maxShake > 0.5f && counts["SCREEN_SHAKE"].Emitted > 0 && counts["SCREEN_SHAKE"].Displayed == counts["SCREEN_SHAKE"].Emitted, $"the explosions of the intro shake the camera (max offset {maxShake:F2}; {report})");
        TestHarness.Assert(maxGrey > 0.99f && counts["CAMERA_BW_MODE_BEGIN"].Displayed == counts["CAMERA_BW_MODE_BEGIN"].Emitted && live.Director.PostEffects.BlackWhite == 0f, $"the opening shot is grey-scale and the filter is gone at the end (max {maxGrey:F2}; {report})");
        TestHarness.Assert(boundaries.Contains(0) && boundaries.Contains(1), $"the camera used border 1 during the intro and border 0 afterwards ({string.Join(",", boundaries)})");
        TestHarness.Assert(counts["MAP_SWITCH_BORDER"].Displayed == counts["MAP_SWITCH_BORDER"].Emitted, "every border switch reached the camera");
        TestHarness.Assert(audio.Calls.Count(call => call.StartsWith("override '", StringComparison.Ordinal)) == counts["AUDIO_OVERRIDE_VOLUME_TYPE"].Emitted + counts["AUDIO_RESTORE_VOLUME_ALL_TYPE"].Emitted + counts.GetValueOrDefault("AUDIO_RESTORE_VOLUME_TYPE").Emitted,
            "every volume override and restore of the intro reached the audio control");
        TestHarness.Assert(audio.Calls.Count(call => call.StartsWith("volume Sound", StringComparison.Ordinal)) == counts.GetValueOrDefault("SOUND_SET_VOLUME").Emitted && audio.Calls.Count(call => call.StartsWith("enabled", StringComparison.Ordinal)) > 0,
            "the sound volumes and the muted events reached the audio control");
        TestHarness.Assert(counts["NAMED_FLASH_WHITE"].Displayed > 0 && flashed, $"flashed objects were tinted in the projected frames ({report})");
        TestHarness.Assert(counts["COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE"].Emitted > 0 && counts["COMMANDBAR_REMOVE_BUTTON_OBJECTTYPE"].Displayed > 0 && controller.Hud.Input.CommandBar!.Script.OverrideCount > 0,
            $"the removed buttons are overrides of the control bar model ({report}; problems: {string.Join(", ", director.Problems.Keys.Where(key => key.StartsWith("COMMANDBAR", StringComparison.Ordinal)).Take(3))})");
        Console.WriteLine("  " + report);
    }

    private static void RealCommandBar(GameData data)
    {
        var (live, controller, _) = LoadMission(data, "Maps/MD_USA01/MD_USA01.map");
        for (var frame = 0; frame < 120; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var catalog = controller.Hud.Input.CommandBar!.Catalog;
        var human = live.World.Entities.Where(entity => entity.Owner == live.Human && !entity.EffectivelyDead).OrderBy(entity => entity.Id.Value).ToArray();
        // Any own object with a command set and at least one button: remove its first button by script, the bar must lose it; add it back into its slot, the bar must show it again.
        foreach (var entity in human)
        {
            if (!catalog.TryGetObjectSet(entity.Template.Name, out var setName) || !catalog.TryGetSet(setName, out var set)) continue;
            var slot = Enumerable.Range(0, CommandBarModel.GridSlots).FirstOrDefault(index => set.Slots[index] is { Kind: not GuiCommandKind.None }, -1);
            if (slot < 0) continue;
            var button = set.Slots[slot]!;
            controller.Hud.Input.Selection.Replace([entity.Id]);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            var before = controller.Hud.CommandBar[slot + 1];
            if (before is null || before.Button.Name != button.Name) continue;
            TestHarness.Assert(controller.RemoveCommandButton(button.Name, entity.Template.Name), "the script's removal is accepted for a known type and button");
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            TestHarness.Assert(controller.Hud.CommandBar[slot + 1] is null, $"'{button.Name}' is gone from slot {slot + 1} of the bar of '{entity.Template.Name}'");
            TestHarness.Assert(controller.AddCommandButton(button.Name, entity.Template.Name, slot + 1), "the script puts it back");
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            TestHarness.Assert(controller.Hud.CommandBar[slot + 1]?.Button.Name == button.Name, "and the bar shows it again");
            TestHarness.Assert(!controller.RemoveCommandButton("Command_ThatDoesNotExist", entity.Template.Name) && !controller.RemoveCommandButton(button.Name, "NoSuchObjectType"), "an unknown button or type is refused, never ignored");
            return;
        }
        throw new InvalidOperationException("no own object with a visible command button was found at the start of MD_USA01");
    }

    private static void RealScienceAndSelection(GameData data)
    {
        var (live, controller, _) = LoadMission(data, "Maps/MD_USA01/MD_USA01.map");
        for (var frame = 0; frame < 60; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var model = controller.Hud.Input.CommandBar!;
        var button = model.Catalog.Buttons.First(candidate => candidate.Kind == GuiCommandKind.PurchaseScience && candidate.Sciences.Length > 0);
        var science = button.Sciences[0];
        var entity = live.World.Entities.First(candidate => candidate.Owner == live.Human && !candidate.EffectivelyDead);
        var normal = model.Evaluate(live.World, live.Human, entity, button).Availability;
        TestHarness.Assert(controller.SetScienceAvailability(science, "Hidden") && model.Evaluate(live.World, live.Human, entity, button).Availability == ButtonAvailability.Hidden, "a hidden science is a hidden button");
        TestHarness.Assert(controller.SetScienceAvailability(science, "Disabled") && model.Evaluate(live.World, live.Human, entity, button) is { Availability: ButtonAvailability.Restricted, Reason: ButtonReason.NotPurchasable }, "a disabled science is a restricted button");
        TestHarness.Assert(controller.SetScienceAvailability(science, "Available") && model.Evaluate(live.World, live.Human, entity, button).Availability == normal, "available restores the ordinary rules");
        TestHarness.Assert(!controller.SetScienceAvailability(science, "Sometimes") && !controller.SetScienceAvailability("SCIENCE_Nonsense", "Hidden"), "an unknown availability or science is refused");

        var mine = live.World.Entities.Where(candidate => candidate.Owner == live.Human && !candidate.EffectivelyDead && CnCNew.Presentation.Input.EntityPicker.IsSelectable(candidate)).OrderBy(candidate => candidate.Id.Value).Take(2).ToArray();
        controller.Hud.Input.Selection.Replace(mine.Select(candidate => candidate.Id));
        TestHarness.Assert(controller.Hud.Input.Selection.Contains(mine[0].Id), "selectable by default");
        TestHarness.Assert(controller.SetObjectSelectable(mine[0].Id, false) && !controller.Hud.Input.Selection.Contains(mine[0].Id) && controller.Hud.Input.Selection.Contains(mine[1].Id), "an object the script made unselectable leaves the selection, the other stays");
        controller.Hud.Input.Selection.Replace(mine.Select(candidate => candidate.Id));
        TestHarness.Assert(!controller.Hud.Input.Selection.Contains(mine[0].Id), "and cannot be selected again");
        TestHarness.Assert(controller.SetObjectSelectable(mine[0].Id, true), "the script gives it back");
        controller.Hud.Input.Selection.Replace(mine.Select(candidate => candidate.Id));
        TestHarness.Assert(controller.Hud.Input.Selection.Contains(mine[0].Id), "selectable again");
        controller.SetIconUiEnabled(false);
        TestHarness.Assert(!controller.Hud.IconUiEnabled, "the icon switch reached the HUD");
    }

    private static void RealColorsAndFlashes(GameData data)
    {
        var (live, controller, _) = LoadMission(data, "Maps/MD_USA01/MD_USA01.map");
        for (var frame = 0; frame < 60; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var target = live.World.Entities.First(candidate => candidate.Owner == live.Human && !candidate.EffectivelyDead && live.Frame.Drawables.Any(drawable => drawable.Entity == candidate.Id));
        var effects = (IMissionObjectEffects)live;
        var before = live.Frame.Drawables.First(drawable => drawable.Entity == target.Id).TeamColor;
        TestHarness.Assert(effects.SetHouseColor(target.Id, 0xFFFF00FFu), "a drawn object takes a custom colour");
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var after = live.Frame.Drawables.First(drawable => drawable.Entity == target.Id).TeamColor;
        TestHarness.Assert(after.Red == 255 && after.Green == 0 && after.Blue == 255 && before != after, $"the drawable is drawn in the script's colour ({before} -> {after})");
        TestHarness.Assert(effects.SetHouseColor(target.Id, 0), "zero gives the owner's colour back");
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(live.Frame.Drawables.First(drawable => drawable.Entity == target.Id).TeamColor == before, "and it is the owner's colour again");

        TestHarness.Assert(effects.Flash(target.Id, Vector3.One, 1), "a drawn object flashes");
        var tints = new List<float>();
        for (var frame = 0; frame < 45; frame++)
        {
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            tints.Add(live.Frame.Drawables.First(drawable => drawable.Entity == target.Id).FlashTint.Length());
        }
        var flashes = tints.Zip(tints.Skip(1), (a, b) => (a, b)).Count(pair => pair.b > pair.a * 1.5f && pair.b > 0.1f);
        TestHarness.Assert(tints.Max() > 0.5f && flashes == 2 && tints[^1] < 0.01f, $"one second of flashing is two flashes of the colour envelope, and the tint is gone afterwards (flashes {flashes}, max {tints.Max():F2})");
        TestHarness.Assert(!effects.Flash(new EntityId(uint.MaxValue), null, 1) && !effects.SetHouseColor(new EntityId(uint.MaxValue), 1u), "an object that does not exist is not flashed");
    }

    private static void SuperweaponDrawRules()
    {
        var measure = new ShellTextMeasure();
        var owner = new PlayerId(1);
        var own = new WndColor(10, 200, 10, 255);
        SuperweaponLine Line(uint id, bool ready) => new(owner, new EntityId(id), "Power", "Nuke: ", ready ? "0:00" : "1:05", ready ? 0 : 65, ready);
        ImmutableArray<UiDrawCommand> Draw(ImmutableArray<SuperweaponLine> lines, long tick, int controlBarTop = 600) => SuperweaponTimers.Draw(lines, _ => own, tick, 1000, 800, controlBarTop, measure, TextFixtures.Style, TextFixtures.Options);

        var ready = Draw([Line(1, true)], 0);
        var waiting = Draw([Line(1, false)], 0);
        TestHarness.Assert(((UiTextDraw)ready[0]).Font.Bold && !((UiTextDraw)waiting[0]).Font.Bold, "only a ready power is bold");
        TestHarness.Assert(((UiTextDraw)ready[0]).Color == own && ((UiTextDraw)waiting[0]).Color == own, "a ready power starts in its owner's colour (m_superweaponUsedFlashColor starts true), a waiting one never flashes");
        TestHarness.Assert(((UiTextDraw)Draw([Line(1, true)], 14)[0]).Color == own && ((UiTextDraw)Draw([Line(1, true)], 15)[0]).Color == WndColor.White && ((UiTextDraw)Draw([Line(1, true)], 30)[0]).Color == own,
            "the flash changes every 15 logic frames (SuperweaponCountdownFlashDuration 500 ms)");
        TestHarness.Assert(((UiTextDraw)Draw([Line(1, false)], 15)[0]).Color == own, "a waiting power does not flash");
        TestHarness.Assert(SuperweaponTimers.Draw([], _ => own, 0, 1000, 800, 600, measure, TextFixtures.Style, TextFixtures.Options).IsEmpty, "no lines, no commands");

        var many = Enumerable.Range(1, 40).Select(id => Line((uint)id, false)).ToImmutableArray();
        var commands = Draw(many, 0);
        var last = (UiTextDraw)commands[^1];
        var limit = (int)(600 * SuperweaponTimers.BottomMargin);
        TestHarness.Assert(last.Text == "..." && commands.Length < 80 && commands.Cast<UiTextDraw>().Take(commands.Length - 1).All(draw => draw.Y < limit), $"the list stops above the control bar with an ellipsis ({commands.Length} commands)");
    }

    private static void RealSuperweaponList(GameData data)
    {
        var (live, controller, _) = LoadMission(data, "Maps/GC_NukeGeneral/GC_NukeGeneral.map");
        var text = data.LoadText("English");
        string? Label(string label) => text.TryGet(label, out var resolved) ? resolved : null;
        for (var frame = 0; frame < 60; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);

        // GC_NukeGeneral hides the five nuclear missile launchers of the enemy general at the start (NAMED_HIDE_SPECIAL_POWER_DISPLAY).
        var superweapons = controller.Hud.Superweapons;
        var hidden = live.Director.SpecialPowerDisplayHidden.ToArray();
        TestHarness.Assert(hidden.Length == 5, $"the script hid the display of five objects ({hidden.Length})");
        TestHarness.Assert(superweapons.Collect(live.World, Label).IsEmpty, "the hidden launchers show no line");

        superweapons.HiddenByScript = null;
        var lines = superweapons.Collect(live.World, Label);
        TestHarness.Assert(lines.Length == 5 && lines.All(line => line.Power == "SuperweaponNeutronMissile" && line.Player.Value == 2 && hidden.Contains(line.Object.Value)),
            $"unhidden, the five launchers of player 2 show one line each: {string.Join(", ", lines.Select(line => $"{line.Object.Value}:{line.Power}"))}");
        TestHarness.Assert(lines.All(line => line.Name.EndsWith(": ", StringComparison.Ordinal) && !line.Name.StartsWith("GUI:", StringComparison.Ordinal)), $"the name comes from the string table ({lines[0].Name})");
        TestHarness.Assert(superweapons.MissingLabels.IsEmpty, "every label was found");
        TestHarness.Assert(lines.All(line => line.Time == $"{line.SecondsLeft / 60}:{line.SecondsLeft % 60:00}" && line.SecondsLeft >= 0), $"the time reads m:ss ({lines[0].Time})");
        TestHarness.Assert(lines.Select(line => line.Object.Value).SequenceEqual(lines.Select(line => line.Object.Value).OrderBy(id => id)), "the lines are in creation order");

        var measure = new ShellTextMeasure();
        WndColor Colour(PlayerId player) => new((byte)(player.Value * 10), 20, 30, 255);
        var commands = SuperweaponTimers.Draw(lines, Colour, live.World.Tick, 1280, 720, 560, measure, controller.Hud.TextStyle, controller.Hud.ScaleOptions);
        TestHarness.Assert(commands.Length == 10 && commands.Cast<UiTextDraw>().All(draw => draw.Color == Colour(new PlayerId(2)) || draw.Color == WndColor.White), "two texts per line, in the owner's colour");
        var nameDraw = (UiTextDraw)commands[0];
        var timeDraw = (UiTextDraw)commands[1];
        TestHarness.Assert(nameDraw.X + measure.MeasureWidth(nameDraw.Text, nameDraw.Font) == (int)(0.90f * 1280) && timeDraw.X == (int)(0.90f * 1280) && timeDraw.Y == nameDraw.Y,
            "the name ends where the time starts, at 90 % of the width");
        TestHarness.Assert(commands.SequenceEqual(SuperweaponTimers.Draw(lines, Colour, live.World.Tick, 1280, 720, 560, measure, controller.Hud.TextStyle, controller.Hud.ScaleOptions)), "drawing is deterministic");

        superweapons.DisplayEnabled = false;
        TestHarness.Assert(superweapons.Collect(live.World, Label).IsEmpty, "DISABLE_SPECIAL_POWER_DISPLAY empties the list");
    }
}
