using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.W3D;

namespace CnCNew.Game.Tests;

/// <summary>
/// F-157, the owner's playtest of 7 November 2026: the doors of the Strategy Center played at the speed of their clips (9.0, 5.8 and 3.4 seconds for the three plans) where the original
/// stretches the state animation to the plan's animation time (<c>BattlePlanUpdate::setStatus</c>, <c>Drawable::setAnimationLoopDuration</c>, <c>W3DModelDraw::setCurAnimDurationInMsec</c>: the
/// frame rate multiplier becomes the clip's natural duration over the wanted one). On the shipped center, for every plan: the drawable asks for the plan's seven seconds while it unpacks and
/// while it packs and for nothing while the plan runs, and the real door clip, played at the multiplier the object layer takes from that, stands on its last frame when the time is over.
/// </summary>
internal static class DoorAnimationTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(DoorAnimationTests));
        harness.Run("The multiplier of a wanted duration: natural over wanted, none without one", Multiplier);
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP DoorAnimationTests with real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Real data: the Strategy Center's doors take the plan's seven seconds to open and to close", () => StrategyCenterDoors(data));
        return harness.Failures;
    }

    private static void Multiplier()
    {
        TestHarness.Assert(AnimationDurations.Speed(270, 30, 7f) is var nine && Math.Abs(nine - 9.0 / 7.0) < 1e-9, "a 9 second clip played in 7 seconds runs 9/7 as fast");
        TestHarness.Assert(AnimationDurations.Speed(270, 30, 0f) == 1.0 && AnimationDurations.Speed(0, 30, 7f) == 1.0 && AnimationDurations.Speed(10, 0, 7f) == 1.0, "no wanted duration, no frames or no rate: the clip's own speed");
    }

    private static void StrategyCenterDoors(GameData data)
    {
        var library = new W3dAssetLibrary(path => data.Content.Exists(path) ? data.Content.OpenRead(path) : null);
        var viewport = new System.Numerics.Vector2(1280, 720);
        foreach (var (plan, door) in new[] { (BattlePlanKind.Bombardment, 1), (BattlePlanKind.HoldTheLine, 2), (BattlePlanKind.SearchAndDestroy, 3) })
        {
            var scene = LiveScenario.Create(data, new DemoScenario("door-animation", "door", "door", 1500, [new ScenarioUnit("center", "AmericaStrategyCenter", 1, 0f, 0f)], [], 0f, 0f), viewport);
            var id = scene.Tags["center"];
            var center = scene.World.FindEntity(id)!;
            var module = center.Modules.OfType<BattlePlanUpdate>().Single();
            for (var frame = 0; frame < 10; frame++) scene.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            module.SelectPlan(plan);
            var wanted = new SortedSet<float>();
            var unpacking = new List<float>();
            var running = new List<float>();
            var packing = new List<float>();
            string? clip = null;
            double natural = 0;
            var phase = 0;
            while (!scene.IsOver && phase < 3)
            {
                scene.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                var drawable = scene.ObjectFrame.Drawables.First(item => item.Entity == id);
                switch (module.Status)
                {
                    case BattlePlanUpdate.TransitionStatus.Unpacking:
                        unpacking.Add(drawable.AnimationSeconds);
                        var opening = Enumerable.Range(0, Math.Min(drawable.Selections.Length, drawable.Visual.Draws.Length))
                            .Select(module => drawable.Selections[module].StateIndex is >= 0 and var chosen ? drawable.Visual.Draws[module].States[chosen] : null)
                            .FirstOrDefault(candidate => candidate is not null && candidate.Conditions.Any(set => set.Contains(StructureConditions.DoorOpening[door - 1])));
                        if (clip is null && opening is { } state)
                        {
                            var spec = state.Animations.FirstOrDefault(animation => animation.File is not null);
                            if (spec is not null && library.GetAnimation(spec.Name) is { } binding)
                            {
                                clip = spec.Name;
                                natural = binding.Animation.FrameCount / (double)binding.Animation.FrameRate;
                                TestHarness.Assert(state.Mode == AnimationMode.Once, $"door {door}: the opening clip plays once ({state.Mode})");
                                var frames = binding.Animation.FrameCount;
                                var rate = binding.Animation.FrameRate;
                                var speed = AnimationDurations.Speed(frames, rate, drawable.AnimationSeconds);
                                var end = W3dAnimationClock.GetFrame(W3dAnimationMode.Once, frames, rate, drawable.AnimationSeconds, speed);
                                var half = W3dAnimationClock.GetFrame(W3dAnimationMode.Once, frames, rate, drawable.AnimationSeconds / 2, speed);
                                TestHarness.Assert(end >= frames - 1 - 1e-3f, $"door {door}: after the plan's time the clip stands on its last frame ({end} of {frames - 1})");
                                TestHarness.Assert(Math.Abs(half - frames / 2f) < 1.5f, $"door {door}: and half way at half the time ({half})");
                                var own = W3dAnimationClock.GetFrame(W3dAnimationMode.Once, frames, rate, drawable.AnimationSeconds, 1.0);
                                TestHarness.Assert(Math.Abs(natural - drawable.AnimationSeconds) < 0.3 || own < frames - 1 - 1e-3f || natural < drawable.AnimationSeconds, $"door {door}: the clip's own speed would not end with the time ({own})");
                            }
                        }
                        break;
                    case BattlePlanUpdate.TransitionStatus.Active:
                        running.Add(drawable.AnimationSeconds);
                        if (phase == 0) { phase = 1; module.SelectPlan(BattlePlanKind.None); }
                        break;
                    case BattlePlanUpdate.TransitionStatus.Packing:
                        packing.Add(drawable.AnimationSeconds);
                        break;
                    case BattlePlanUpdate.TransitionStatus.Idle when phase == 1:
                        phase = 3;
                        break;
                }
                wanted.Add(drawable.AnimationSeconds);
            }
            Console.WriteLine($"       door {door}: clip {clip} natural {natural:F2} s; wanted {string.Join(", ", wanted)} s; unpacking {unpacking.Count} frames, running {running.Count}, packing {packing.Count}");
            TestHarness.Assert(unpacking.Count > 0 && unpacking.All(value => Math.Abs(value - 7f) < 1e-3f), $"door {door}: while the plan unpacks the drawable wants 7 seconds");
            TestHarness.Assert(running.Count > 0 && running.All(value => value == 0f), $"door {door}: and nothing while the plan runs");
            TestHarness.Assert(clip is not null, $"door {door}: the opening state has a clip");
            TestHarness.Assert(packing.Count > 0 && packing.All(value => Math.Abs(value - 7f) < 1e-3f), $"door {door}: while it packs 7 seconds again ({packing.Count} frames)");
        }
    }
}
