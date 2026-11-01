using System.Globalization;
using System.Numerics;
using CnCNew.Game.Campaign;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

namespace CnCNew.PresentationTool;

/// <summary>
/// What the scripted camera of a mission does, in numbers and without a window: the mission runs as a client runs it, and a line is printed every <c>--every</c> original frames and at every
/// camera output, with the pivot, the ground level, angle, zoom, script pitch, the eye and the point looked at, the ground under the eye and how far the view looks down (negative: up).
/// These are the quantities of <c>W3DView::buildCameraTransform</c>, to be held against what the original's formulas give (docs/SCRIPT-ENGINE.md section 7a).
/// </summary>
internal static class CameraScriptCommand
{
    public const string Usage = "camera-script <content-dir> --map <virtual-map-path> [--seconds 90] [--every 30] [--fps 60] [--tick-rate N] [--waypoints A,B,..]";

    public static int Run(string[] args)
    {
        if (args.Length < 1) throw new ArgumentException(Usage);
        var invariant = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = invariant;
        string? Option(string key) => Array.IndexOf(args, key) is var index and >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var mapPath = Option("--map") ?? throw new ArgumentException(Usage);
        var rate = Option("--tick-rate") is { } rateText ? SimulationRate.FromTicksPerSecond(int.Parse(rateText, invariant)) : GameData.DefaultSessionRate;
        var seconds = double.Parse(Option("--seconds") ?? "90", invariant);
        var every = int.Parse(Option("--every") ?? "30", invariant);
        var fps = int.Parse(Option("--fps") ?? "60", invariant);
        var data = GameData.Load(args[0], rate: rate);
        var challenge = ChallengeSides.OfDirectStart(data, mapPath);
        var run = MissionRun.Load(MissionContext.From(data), mapPath, MissionOptions.Standard with { HumanSideName = challenge.Side, HumanFaction = challenge.Faction, PresentationLatencyTicks = LiveMission.ClientPresentationLatencyFrames });
        var mission = LiveMission.Create(data, run, new Vector2(1280, 720));
        var camera = mission.Camera;
        Console.WriteLine(string.Create(invariant, $"camera-script: {mapPath}, {rate.TicksPerSecond} Hz, drawn at {fps} frames a second; offset height {camera.OffsetHeight:F2}, ground level {camera.GroundLevel:F2}, camera height {camera.Settings.CameraHeight}, max height {camera.Settings.MaxHeightAboveGround}, default pitch {camera.Settings.PitchDegrees}, yaw {camera.Settings.YawDegrees}"));
        foreach (var name in (Option("--waypoints") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var waypoint = run.Session.World.Terrain?.Waypoints.FirstOrDefault(point => point.Name == name);
            if (waypoint is null) { Console.WriteLine($"waypoint {name}: none"); continue; }
            var place = new Vector2((float)waypoint.X.ToDecimal(), (float)waypoint.Y.ToDecimal());
            Console.WriteLine(string.Create(invariant, $"waypoint {name}: ({place.X:F1}, {place.Y:F1}) ground {camera.GroundHeightAt(place):F2} largest zoom {camera.MaxZoomAt(place):F4}"));
        }
        var journalled = 0;
        var nextLine = 0L;
        var frameSeconds = 1.0 / fps;
        for (var step = 0L; step * frameSeconds < seconds; step++)
        {
            mission.Advance(frameSeconds);
            camera.Update((float)frameSeconds, false);
            var frame = mission.Director.FrameClock;
            var journal = mission.Director.Journal;
            var cameraOutput = false;
            for (; journalled < journal.Count; journalled++)
            {
                var entry = journal[journalled];
                if (!entry.Contains("CAMERA", StringComparison.Ordinal) && !entry.Contains("SCREEN_SHAKE", StringComparison.Ordinal) && !entry.Contains("BLUR", StringComparison.Ordinal) && !entry.Contains("VICTORY", StringComparison.Ordinal)) continue;
                Console.WriteLine($"  > {entry}");
                cameraOutput = true;
            }
            if (!cameraOutput && frame < nextLine) continue;
            nextLine = frame + every;
            var (eye, target) = (camera.Eye, camera.Target);
            var down = MathF.Atan2(eye.Z - target.Z, Vector2.Distance(new Vector2(eye.X, eye.Y), new Vector2(target.X, target.Y))) * 180f / MathF.PI;
            var ground = camera.GroundHeightAt(new Vector2(eye.X, eye.Y));
            Console.WriteLine(string.Create(invariant, $"frame {frame,6} pivot ({camera.Pivot.X,8:F1},{camera.Pivot.Y,8:F1}) ground {camera.GroundLevel,7:F2} angle {camera.Angle * 180f / MathF.PI,7:F1} zoom {camera.Zoom,6:F3} fx {camera.FxPitch,6:F2} eye ({eye.X,8:F1},{eye.Y,8:F1},{eye.Z,7:F1}) over ground {eye.Z - ground,7:F1} target z {target.Z,7:F1} looks down {down,6:F1}"));
        }
        return 0;
    }
}
