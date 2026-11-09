using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Presentation.Objects;
using CnCNew.W3D;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The owner's playtest of 9 November 2026 (finding 2): "the animation of the Chinook gathering supplies - lowering the net and waiting for it - is cut off and not finished". The first box that
/// comes aboard turns <c>DOCKING</c> into <c>DOCKING CARRYING</c> at once, and the net that the first state lowers (<c>AVChinook_A1</c>, backwards, at 0.75 speed) never reaches the ground. The
/// original's <c>W3DModelDraw::setModelState</c> lets the playing state finish when the new one names its transition key (<c>WaitForStateToFinishIfPossible</c>); this is that, on the real draw
/// module and the real clips. Runs only with <c>--content-dir &lt;dir&gt;</c>.
/// </summary>
internal static class ChinookNetAnimationTests
{
    private const double Step = 1.0 / 60.0;

    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP Chinook net animation tests (pass --content-dir <dir>)");
            return 0;
        }
        var content = ContentFileSystem.Open(args[index + 1]);
        var graph = DefinitionCompiler.Compile(new ContentSource(content), ModuleRegistry.Default,
            DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
        var catalog = ObjectVisualCatalog.Build(graph);
        var t = new TestHarness("ChinookNetAnimation");
        t.Run("the net that DOCKING lowers is lowered to its end before DOCKING CARRYING takes over, and CARRYING waits for it too", () => NetIsLoweredToItsEnd(content, catalog));
        t.Run("every Chinook of every general has the same rule, and the objects without a wait key are drawn in the state their conditions select", () => OthersUnchanged(catalog));
        return t.Failures;
    }

    private static DrawSpec ChinookDraw(ObjectVisualCatalog catalog, string name = "AmericaVehicleChinook")
    {
        Assert(catalog.TryGet(name, out var visual), name + " is in the catalog");
        return visual.Draws.Where(draw => draw.Kind == DrawModuleKind.Model).OrderByDescending(StateWaitFollower.Needed).First();
    }

    private static int State(DrawSpec draw, string conditions) => BestStateSelector.FindBest(draw, ModelConditionSet.Parse(conditions));

    /// <summary>The seconds a state's animation takes to its end (the clip it plays, at the state's speed).</summary>
    private static double Seconds(ContentFileSystem content, ModelStateSpec state)
    {
        var animation = state.Animations.Single(candidate => !candidate.IsIdle);
        using var stream = content.OpenRead($"Art/W3D/{animation.File}");
        var clip = W3dAssetFile.Read(stream).Animations.Single();
        var last = clip.FrameCount - 1;
        var speed = (double)state.AnimationSpeedMin;
        return last / (clip.FrameRate * speed);
    }

    private static void NetIsLoweredToItsEnd(ContentFileSystem content, ObjectVisualCatalog catalog)
    {
        var draw = ChinookDraw(catalog);
        var docking = State(draw, "DOCKING");
        var dockingCarrying = State(draw, "DOCKING CARRYING");
        var carrying = State(draw, "CARRYING");
        var none = State(draw, "");
        Assert(new[] { docking, dockingCarrying, carrying, none }.Distinct().Count() == 4, "the four states are four different ones");
        Assert(draw.States[docking].Mode == AnimationMode.OnceBackwards && draw.States[dockingCarrying].Mode == AnimationMode.Once, "DOCKING lowers the net backwards, DOCKING CARRYING plays forwards");
        Assert(StateWaitFollower.Needed(draw), "the Chinook's draw module has a wait key");
        var lowering = Seconds(content, draw.States[docking]);
        Assert(lowering > 3.0 && lowering < 30.0, $"lowering the net takes seconds, not a flash ({lowering:F2} s)");

        Console.WriteLine($"  Chinook net: lowering {lowering:F2} s, unloading {Seconds(content, draw.States[dockingCarrying]):F2} s");
        // The Chinook docks at a pile; the first box comes aboard 1.2 s later (SupplyWarehouseActionDelay).
        var follower = new StateWaitFollower(draw);
        var shown = follower.Follow(docking, true);
        Assert(shown == docking, "the first state is shown at once");
        var elapsed = 0.0;
        var firstBox = 1.2;
        Assert(lowering > firstBox + 1.0, $"the first box ({firstBox} s) comes aboard while the net is still on its way down ({lowering:F2} s): without the wait it is cut off");
        var stoppedEarly = false;
        var switchedAt = -1.0;
        for (; elapsed < lowering + 5.0; elapsed += Step)
        {
            var requested = elapsed < firstBox ? docking : dockingCarrying;
            var complete = elapsed >= lowering;
            var now = follower.Follow(requested, complete);
            if (now == dockingCarrying && switchedAt < 0) switchedAt = elapsed;
            if (elapsed < lowering && now != docking) stoppedEarly = true;
        }
        Assert(!stoppedEarly, "the net is lowered to its end: DOCKING stays until its animation has run out");
        Assert(switchedAt >= lowering - 2 * Step && switchedAt <= lowering + 2 * Step, $"DOCKING CARRYING takes over when the net has been lowered ({switchedAt:F2} s against {lowering:F2} s)");

        // The dock ends before the lowering is done (a short stay): CARRYING is requested; it waits for the pickup animation as well.
        follower = new StateWaitFollower(draw);
        follower.Follow(docking, true);
        var early = lowering / 2;
        Assert(follower.Follow(dockingCarrying, false) == docking, "the first box does not cut the net off");
        Assert(follower.Follow(carrying, false) == docking, "nor does the end of the dock: CARRYING waits for the net (TRANS_PickingUp)");
        Assert(follower.Follow(carrying, false) == docking && early > 0, "it goes on waiting while the net is on its way");
        Assert(follower.Follow(carrying, true) == carrying, "CARRYING is shown when the net is done, not DOCKING CARRYING that was asked for in between");

        // After the unloading state (key TRANS_Unloading) CARRYING does not wait: its wait key is TRANS_PickingUp.
        follower = new StateWaitFollower(draw);
        follower.Follow(dockingCarrying, true);
        Assert(follower.Follow(carrying, false) == carrying, "leaving DOCKING CARRYING for CARRYING is not delayed");

        // Delivering at the center: DOCKING CARRYING plays, the boxes go, DOCKING (empty) waits for TRANS_Unloading to run out.
        follower = new StateWaitFollower(draw);
        follower.Follow(dockingCarrying, true);
        Assert(follower.Follow(docking, false) == dockingCarrying, "the empty net (DOCKING) waits for the unloading animation");
        Assert(follower.Follow(docking, true) == docking, "and is shown when it has run out");

        // No wait where nothing names the key: the idle state follows at once.
        follower = new StateWaitFollower(draw);
        follower.Follow(carrying, true);
        Assert(follower.Follow(none, false) == none, "an object that sets down its load and stands is drawn at once");
    }

    private static void OthersUnchanged(ObjectVisualCatalog catalog)
    {
        foreach (var name in new[] { "AmericaVehicleChinook", "AFG_AmericaVehicleChinook", "Lazr_AmericaVehicleChinook", "SupW_AmericaVehicleChinook" })
            if (catalog.TryGet(name, out var visual))
                Assert(visual.Draws.Any(StateWaitFollower.Needed), name + " waits for its net");
        var plain = ChinookDraw(catalog, "AmericaTankCrusader");
        Assert(!StateWaitFollower.Needed(plain), "a Crusader names no wait key");
        var follower = new StateWaitFollower(plain);
        var state = BestStateSelector.FindBest(plain, ModelConditionSet.Parse("MOVING"));
        Assert(follower.Follow(state, false) == state, "the state is the requested one");
    }

    private sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }
}
