using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Terrain;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Ui.Runtime;

namespace CnCNew.Game.Tests;

/// <summary>
/// Headless tests of <see cref="MissionPresentationDirector"/> and <see cref="MissionUiBridge"/>
/// (<c>docs/HANDOFF-PHASE10.md</c> items 6 and 7): a scripted camera move completes exactly when its real duration
/// elapses and unblocks a <c>CAMERA_MOVEMENT_FINISHED</c>-gated follow-up script (the actual feedback loop the mission
/// scripts need, not just "it compiles"), radar events and on-screen text are captured with real positions/resolved
/// text, and selecting a named unit / signalling a UI interaction reach the engine's own conditions
/// (<c>NAMED_SELECTED</c>/<c>FLAG</c>). All of this runs on a tiny synthetic script/map, same builder idiom as
/// <see cref="CampaignTests"/>, so it needs no content directory.
/// </summary>
internal static class MissionPresentationTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(MissionPresentationTests));
        harness.Run("A scripted camera move completes at its real duration and unblocks CAMERA_MOVEMENT_FINISHED", CameraMoveCompletesOnItsRealDuration);
        harness.Run("Radar events and on-screen text are captured with real positions and resolved text", RadarAndCaptionOutputsAreCaptured);
        harness.Run("Selecting a named unit reaches NAMED_SELECTED; a UI interaction reaches FLAG", UiBridgeReachesTheEngine);
        harness.Run("A camera move pans a real RtsCamera once, not every frame; caption/placeholder text becomes draw commands", OverlayAndCameraApplication);
        harness.Run("Letterbox, input lock, cinematic text, counters and the pan over time are state and draw commands; displayed needs a host", CinematicOutputsAreShownAndCounted);
        return harness.Failures;
    }

    // ---------------------------------------------------------------------------------------------------- script builder (same idiom as CampaignTests)

    internal static MapScriptParameter Named(MapScriptParameterType type, string text) => new(type, 0, 0m, text, 0m, 0m, 0m);
    internal static MapScriptParameter Int(int value) => new(MapScriptParameterType.Int, value, 0m, string.Empty, 0m, 0m, 0m);
    internal static MapScriptParameter Bool(bool value) => new(MapScriptParameterType.Boolean, value ? 1 : 0, 0m, string.Empty, 0m, 0m, 0m);
    internal static MapScriptParameter Real(decimal value) => new(MapScriptParameterType.Real, 0, value, string.Empty, 0m, 0m, 0m);
    internal static MapScriptParameter Coord(decimal x, decimal y, decimal z) => new(MapScriptParameterType.Coord3D, 0, 0m, string.Empty, x, y, z);

    internal static MapCondition If(string name, params MapScriptParameter[] parameters) => new(MapScriptCatalog.ConditionTypeNames.IndexOf(name), name, [..parameters]);
    internal static MapScriptAction Do(string name, params MapScriptParameter[] parameters) => new(MapScriptCatalog.ScriptActionTypeNames.IndexOf(name), name, [..parameters]);

    internal static MapScript Script(string name, MapCondition condition, MapScriptAction[] actions, bool oneShot = true)
        => new(name, string.Empty, string.Empty, string.Empty, true, oneShot, true, true, true, false, 0, [new MapOrCondition([condition])], [..actions], []);

    internal static MapObject Waypoint(string name, int id, decimal x, decimal y) => new("*Waypoints/Waypoint", x, y, 0m, 0m, 0, new MapProperties([
        new MapProperty("waypointID", MapPropertyType.Int, id, 0m, string.Empty),
        new MapProperty("waypointName", MapPropertyType.Ascii, 0, 0m, name),
    ]));

    internal static MapObject Placed(string template, decimal x, decimal y, string team, string name) => new(template, x, y, 0m, 0m, 0, new MapProperties([
        new MapProperty("originalOwner", MapPropertyType.Ascii, 0, 0m, team),
        new MapProperty("objectName", MapPropertyType.Ascii, 0, 0m, name),
    ]));

    /// <summary>A 30x30-cell map with a human side, one team, the given waypoints/objects and <paramref name="scripts"/> in the human's list.</summary>
    /// <param name="rate">The tick rate of the fixture's catalog (the original's when null); the session started from it ticks at that rate.</param>
    internal static (MapData Map, SyntheticFixture Fixture, TerrainMap Terrain) Mission(MapScript[] scripts, MapObject[]? objects = null, int boundaries = 1, int cells = 30, SimulationRate? rate = null)
    {
        const int border = 1;
        var width = cells + 2 * border;
        var stride = (width + 7) / 8;
        var blend = new MapBlendTiles(8, [], [], [], [], [..new byte[stride * width]], stride, 0, 0, [], [], [], []);
        MapProperty Text(string key, string value) => new(key, MapPropertyType.Ascii, 0, 0m, value);
        var sides = ImmutableArray.Create(
            new MapSide(new MapProperties([Text("playerName", string.Empty)]), []),
            new MapSide(new MapProperties([Text("playerName", "Hero"), new MapProperty("playerIsHuman", MapPropertyType.Bool, 1, 0m, string.Empty)]), []));
        var teams = ImmutableArray.Create(new MapProperties([Text("teamName", "teamHero"), Text("teamOwner", "Hero"), new MapProperty("teamIsSingleton", MapPropertyType.Bool, 1, 0m, string.Empty)]));
        var map = new MapData("mission-presentation", new MapHeightField(width, width, border, [..Enumerable.Repeat(new MapBoundary(cells, cells), boundaries)], [..new byte[width * width]]), blend, MapProperties.Empty,
            sides, teams, [..objects ?? []], [], [], [new MapScriptList([], []), new MapScriptList([..scripts], [])], []);
        var fixture = new SyntheticFixture(rate ?? CnCNew.Tests.TestRate.Default);
        return (map, fixture, TerrainMap.Build(map, "mission-presentation"));
    }

    internal static MissionSession Start(MapScript[] scripts, MapObject[]? objects, MissionOptions? options = null, SimulationRate? rate = null)
    {
        var (map, fixture, terrain) = Mission(scripts, objects, rate: rate);
        return MissionSession.Start(options ?? MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
    }

    // ---------------------------------------------------------------------------------------------------- item 6: presentation playback

    private static void CameraMoveCompletesOnItsRealDuration()
    {
        const decimal durationSeconds = 5m;
        var waypoint = Waypoint("Target", 1, 400m, 500m);
        var intro = Script("Intro", If("CONDITION_TRUE"), [Do("MOVE_CAMERA_TO", Named(MapScriptParameterType.Waypoint, "Target"), Real(durationSeconds), Real(0), Real(0), Real(0))]);
        var followUp = Script("FollowUp", If("CAMERA_MOVEMENT_FINISHED"), [Do("SET_FLAG", Named(MapScriptParameterType.Flag, "camera_done"), Bool(true))], oneShot: false);

        // A huge fallback latency the real completion must beat; if the director never called CompletePresentation the
        // engine's own auto-expiry (ScriptEngine.Update) would not release the pending camera key until this many ticks,
        // which the test never runs for — proving the follow-up fires from the director's real duration, not a guess.
        var options = MissionOptions.Standard with { PresentationLatencyTicks = 1_000_000 };
        var session = Start([intro, followUp], null, options);
        var director = new MissionPresentationDirector(session);

        void StepOnce()
        {
            director.BeforeStep();
            session.Step();
            director.AfterStep();
        }

        var fireTick = session.World.Tick; // the engine tick Intro's MOVE_CAMERA_TO will run at
        StepOnce();
        TestHarness.Assert(director.CameraMoveActive, "the camera move is tracked as active right after it starts");
        TestHarness.Assert(session.Engine.PendingPresentation.Contains("<camera>"), "the engine itself still considers the camera move pending");
        TestHarness.Assert(!session.Engine.FlagValue("camera_done"), "the follow-up has not fired yet");

        // CompletePresentation must land before the Update() call of this exact engine tick to unblock that tick's
        // CAMERA_MOVEMENT_FINISHED check (BeforeStep()'s own doc comment explains why: same timing as the engine's own
        // PresentationLatencyTicks auto-expiry, which also runs at the top of Update, before that tick's scripts).
        var completionTick = fireTick + CnCNew.Tests.TestTime.Ticks(session.World, (long)durationSeconds);
        while (true)
        {
            var dueThisStep = session.World.Tick >= completionTick;
            if (!dueThisStep) TestHarness.Assert(!session.Engine.FlagValue("camera_done"), $"the follow-up fired too early, before engine tick {completionTick} (now {session.World.Tick})");
            StepOnce();
            if (dueThisStep) break;
        }
        TestHarness.Assert(!director.CameraMoveActive, "the director considers the move finished exactly at its real duration");
        TestHarness.Assert(!session.Engine.PendingPresentation.Contains("<camera>"), "CompletePresentation reached the engine");
        TestHarness.Assert(session.Engine.FlagValue("camera_done"), "CAMERA_MOVEMENT_FINISHED unblocked the follow-up script exactly when the camera move really finished, not from the 1,000,000-tick fallback");
    }

    private static void RadarAndCaptionOutputsAreCaptured()
    {
        var radar = Script("Radar", If("CONDITION_TRUE"), [Do("RADAR_CREATE_EVENT", Coord(123m, 456m, 0m), Int(3))]);
        var text = Script("Text", If("CONDITION_TRUE"), [Do("DISPLAY_TEXT", Named(MapScriptParameterType.ObjectType, "GUI:Test"))]);
        var session = Start([radar, text], null);
        var resolved = new Dictionary<string, string> { ["GUI:Test"] = "Resolved Caption Text" };
        var director = new MissionPresentationDirector(session, label => resolved.GetValueOrDefault(label, label)) { TextStyle = TextFixtures.Style, TextScaleOptions = TextFixtures.Options, TextMeasure = new ShellTextMeasure() };

        director.BeforeStep();
        session.Step();
        director.AfterStep();

        TestHarness.Assert(director.RadarBlips.Length == 1, $"one radar blip captured (got {director.RadarBlips.Length})");
        var blip = director.RadarBlips[0];
        TestHarness.Assert(Math.Abs(blip.X - 123f) < 0.01f && Math.Abs(blip.Y - 456f) < 0.01f && blip.EventType == 3, $"the blip carries the script's real position and event type (got {blip.X},{blip.Y},{blip.EventType})");
        TestHarness.Assert(director.Messages is { Lines: [{ Text: "Resolved Caption Text" }] }, $"DISPLAY_TEXT adds the resolved text, not the raw label, to the message lines (got '{string.Join(" | ", director.Messages?.Lines.Select(line => line.Text) ?? [])}')");
    }

    // ---------------------------------------------------------------------------------------------------- item 7: UI feedback into the VM

    private static void UiBridgeReachesTheEngine()
    {
        var selectScript = Script("OnSelect", If("NAMED_SELECTED", Named(MapScriptParameterType.Unit, "Hero1")), [Do("SET_COUNTER", Named(MapScriptParameterType.Counter, "selected_count"), Int(1))], oneShot: false);
        var flagScript = Script("OnFlag", If("FLAG", Named(MapScriptParameterType.Flag, "ui_flag"), Bool(true)), [Do("SET_COUNTER", Named(MapScriptParameterType.Counter, "flag_count"), Int(1))], oneShot: false);
        var hero = Placed("Barrel", 50m, 50m, "Hero", "Hero1");
        var session = Start([selectScript, flagScript], [hero]);
        session.Step(); // registers the placed object under its script name before anything reads it

        var heroId = session.Engine.NamedObjects.First(pair => pair.Key == "Hero1").Value;
        var bridge = new MissionUiBridge(session);

        TestHarness.Assert(session.Engine.CounterValue("selected_count") == 0, "not selected yet");
        bridge.OnSelectionChanged([new EntityId(heroId)]);
        session.Step();
        TestHarness.Assert(session.Engine.CounterValue("selected_count") == 1, "selecting the named unit reached ScriptEngine.SelectNamed and satisfied NAMED_SELECTED");

        TestHarness.Assert(session.Engine.CounterValue("flag_count") == 0, "no UI interaction signalled yet");
        bridge.NotifyUiInteraction("ui_flag");
        session.Step();
        TestHarness.Assert(session.Engine.CounterValue("flag_count") == 1, "signalling the UI interaction reached ScriptEngine.SignalUiInteraction and satisfied the FLAG condition");
    }

    // ---------------------------------------------------------------------------------------------------- overlay commands and camera application

    private static void CinematicOutputsAreShownAndCounted()
    {
        var waypoint = Waypoint("Target", 1, 150m, 150m);
        var intro = Script("Intro", If("CONDITION_TRUE"),
        [
            Do("CAMERA_LETTERBOX_BEGIN"),
            Do("DISABLE_INPUT"),
            Do("ROTATE_CAMERA", Real(0.5m), Real(4), Real(0), Real(0)),
            Do("DISPLAY_CINEMATIC_TEXT", Named(MapScriptParameterType.ObjectType, "GUI:Cine"), Named(MapScriptParameterType.ObjectType, "Arial - Size:12 [Bold]"), Int(4)),
            Do("SET_COUNTER", Named(MapScriptParameterType.Counter, "kills"), Int(7)),
            Do("DISPLAY_COUNTER", Named(MapScriptParameterType.Counter, "kills"), Named(MapScriptParameterType.ObjectType, "GUI:Kills")),
            Do("SOUND_PLAY_NAMED", Named(MapScriptParameterType.ObjectType, "SomeSound"), Named(MapScriptParameterType.Unit, "Nobody")),
            Do("SET_FPS_LIMIT", Int(20)),
        ]);
        var (map, fixture, terrain) = Mission([intro], [waypoint]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var director = new MissionPresentationDirector(session) { TextStyle = TextFixtures.Style, TextScaleOptions = TextFixtures.Options, TextMeasure = new ShellTextMeasure() };
        var disabled = 0;
        director.InputDisabled += () => disabled++;

        director.BeforeStep();
        session.Step();
        director.AfterStep();

        var counts = director.Counts().ToDictionary(count => count.Action);
        TestHarness.Assert(counts["CAMERA_LETTERBOX_BEGIN"] is { Emitted: 1, Forwarded: 1, Displayed: 0 }, "without a host that draws the overlay the letterbox is forwarded, not displayed");
        TestHarness.Assert(counts["SOUND_PLAY_NAMED"] is { Emitted: 1, Forwarded: 0, Displayed: 0 } && director.Problems.Keys.Any(key => key.StartsWith("SOUND_PLAY_NAMED", StringComparison.Ordinal)), "a sound without connected audio is counted as not played, with the reason");
        TestHarness.Assert(counts["SET_FPS_LIMIT"] is { Emitted: 1, Forwarded: 0, Displayed: 0 }, "an output nothing shows is counted as not displayed");

        TestHarness.Assert(director.Letterbox && disabled == 1 && director.InputLocked, "letterbox and input lock are state the host reads; the lock is announced once");
        var commands = director.BuildOverlayCommands(1280, 720);
        // W3DDisplay::renderLetterBox: no bars on a 16:9 screen, and on a 4:3 one they fade in from nothing (MissionTextTests.LetterboxFollowsTheOriginal has the numbers).
        TestHarness.Assert(!commands.OfType<UiFillRect>().Any() && !director.BuildOverlayCommands(800, 600).OfType<UiFillRect>().Any() && director.LetterboxFadeLevel == 0f, "the letterbox draws no bar at 16:9 and none in its first step at 4:3");
        TestHarness.Assert(commands.OfType<UiTextDraw>().Any(text => text.Text == "GUI:Cine"), "the cinematic text is drawn");
        TestHarness.Assert(commands.OfType<UiTextDraw>().Any(text => text.Text == "GUI:Kills 7"), $"the counter shows its label and value ({string.Join(" | ", commands.OfType<UiTextDraw>().Select(text => text.Text))})");

        var camera = new RtsCamera(MinimalCameraSettings(), new TerrainHeightSampler(map.HeightField), new Vector2(1280, 720));
        // The synthetic map is smaller than the camera's footprint, so the pivot cannot move on it; the half turn of ROTATE_CAMERA shows the same timing.
        camera.SetAngle(0f);
        director.ApplyCameraTo(camera, 2.0);
        TestHarness.Assert(Math.Abs(Math.Abs(camera.Angle) - MathF.PI / 2) < 0.05f, $"half way through its four seconds the half turn is a quarter turn (angle {camera.Angle})");
        director.ApplyCameraTo(camera, 0);
        var paused = camera.Angle;
        director.ApplyCameraTo(camera, 0);
        TestHarness.Assert(camera.Angle == paused, "a paused game does not advance the move");
        director.ApplyCameraTo(camera, 2.5);
        TestHarness.Assert(Math.Abs(Math.Abs(camera.Angle) - MathF.PI) < 0.05f, $"after its duration the camera has made the half turn (angle {camera.Angle})");
        camera.SetAngle(1f);
        director.ApplyCameraTo(camera, 1.0);
        TestHarness.Assert(camera.Angle == 1f, "a finished move leaves the camera to the player");

        counts = director.Counts().ToDictionary(count => count.Action);
        TestHarness.Assert(counts["CAMERA_LETTERBOX_BEGIN"].Displayed == 1 && counts["ROTATE_CAMERA"].Displayed == 1 && counts["DISABLE_INPUT"].Displayed == 1 && counts["DISPLAY_COUNTER"].Displayed == 1,
            "once the host drew the overlay, applied the camera and asked for the lock, those outputs count as displayed");
        TestHarness.Assert(director.Report().Any(line => line.StartsWith("mission outputs:", StringComparison.Ordinal)), "the report has a totals line");
    }

    internal static RtsCameraSettings MinimalCameraSettings() => new(
        PitchDegrees: -34f, YawDegrees: 0f, CameraHeight: 200f, MaxHeightAboveGround: 300f, MinHeightAboveGround: 100f,
        AdjustSpeed: 0.1f, ScrollAmountCutoff: 0.01f, EnforceMaxCameraHeight: true, HorizontalScrollSpeedFactor: 1f,
        VerticalScrollSpeedFactor: 1f, KeyboardScrollFactor: 1f, KeyboardRotateSpeed: 1f, MaxShakeIntensity: 1f, MaxShakeRange: 1f);

    private static void OverlayAndCameraApplication()
    {
        var waypoint = Waypoint("Target", 1, 150m, 150m);
        var intro = Script("Intro", If("CONDITION_TRUE"), [Do("MOVE_CAMERA_TO", Named(MapScriptParameterType.Waypoint, "Target"), Real(5), Real(0), Real(0), Real(0))]);
        var text = Script("Text", If("CONDITION_TRUE"), [Do("DISPLAY_TEXT", Named(MapScriptParameterType.ObjectType, "GUI:Overlay"))]);
        var speech = Script("Speech", If("CONDITION_TRUE"), [Do("SPEECH_PLAY", Named(MapScriptParameterType.ObjectType, "VoiceEvent01"))]);
        var (map, fixture, terrain) = Mission([intro, text, speech], [waypoint]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var director = new MissionPresentationDirector(session, label => label == "GUI:Overlay" ? "Resolved Overlay" : label) { TextStyle = TextFixtures.Style, TextScaleOptions = TextFixtures.Options, TextMeasure = new ShellTextMeasure() };

        director.BeforeStep();
        session.Step();
        director.AfterStep();

        var commands = director.BuildOverlayCommands(1280, 720);
        TestHarness.Assert(commands.OfType<UiTextDraw>().Any(c => c.Text == "Resolved Overlay"), "the caption's resolved text became a UiTextDraw command the existing UiRenderer already knows how to draw");
        TestHarness.Assert(commands.OfType<UiTextDraw>().Any(c => c.Text.Contains("VoiceEvent01")), "the speech placeholder names the sound event, not invented dialogue");

        // Start the camera off-target so the pan is observable (the synthetic map's center happens to coincide with the
        // waypoint otherwise, at 150,150 - which would make "it panned" and "it started there" indistinguishable).
        var camera = new RtsCamera(MinimalCameraSettings(), new TerrainHeightSampler(map.HeightField), new Vector2(1280, 720));
        camera.CenterOn(new Vector2(10, 10));
        TestHarness.Assert(director.CameraTarget is not null, "the camera move resolved its waypoint to a real target position");
        director.ApplyCameraTo(camera);
        TestHarness.Assert(Math.Abs(camera.Pivot.X - 150f) < 0.5f && Math.Abs(camera.Pivot.Y - 150f) < 0.5f,
            $"the camera panned to the waypoint's real position (got {camera.Pivot})");

        camera.CenterOn(new Vector2(200, 50)); // something else (e.g. the player) moved the camera since
        var movedTo = camera.Pivot; // CenterOn clamps to the map boundary; compare against what it actually settled on
        director.ApplyCameraTo(camera);
        TestHarness.Assert(camera.Pivot == movedTo, $"a second ApplyCameraTo call for the SAME still-active move does not re-pan (it already applied once; re-panning every frame would fight whatever moved the camera since) - expected {movedTo}, got {camera.Pivot}");
    }
}
