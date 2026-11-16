using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Presentation;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.W3D;

namespace CnCNew.Game.Tests;

/// <summary>
/// #385, the owner's playtest of 15 November 2026: the Scud Storm launched its missiles without its launch. In the original the attack of the strike (<c>AttackNugget</c>, an attack on the target place
/// with the secondary slot locked) puts the site into <c>AIAttackState</c>, which sets <c>ATTACKING</c>; the draw module plays its <c>TransitionState Trans_READY Trans_ATTACKING</c> (the missiles rise
/// during the weapon's three seconds of <c>PreAttackDelay</c>), shows <c>ATTACKING</c> with the nine missiles on <c>WeaponA01</c> to <c>WeaponA09</c> and hides each one as it is fired
/// (<c>ProjectileBoneFeedbackEnabledSlots = SECONDARY</c>), and lowers the empty rails with <c>Trans_ATTACKING Trans_READY</c> when the attack is over; each missile leaves its own rail with its exhaust.
/// The port never set <c>ATTACKING</c>, played no transition state and showed no clip on the model. A staged strike on a real map, read frame by frame.
/// </summary>
internal static class ScudStormLaunchTests
{
    private static readonly Vector2 Viewport = new(1280, 720);
    private const int Rounds = 9;

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ScudStormLaunchTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ScudStormLaunchTests with real data (pass --content-dir <dir>)");
            return harness.Failures;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        var strike = new Lazy<Strike>(() => Strike.Record(data));
        harness.Run("The site is ATTACKING from the order to its last missile and shows the raised state throughout, the lying one before and after", () => AttackingThroughout(strike.Value));
        harness.Run("The draw module raises the missiles with Trans_READY -> Trans_ATTACKING during the wind-up and lowers the rails with Trans_ATTACKING -> Trans_READY after the last", () => RaisedAndLowered(strike.Value));
        harness.Run("The rails show the rounds left: each fired missile is hidden, all nine while the clip reloads, all nine again once it has", () => RoundsOnTheRails(strike.Value));
        harness.Run("The transitions' own particle systems run while they play: goo while the missiles rise, the vent's steam only while the rails go down (#391)", () => TransitionSystems(strike.Value));
        harness.Run("Each of the nine missiles leaves its own rail, is drawn and carries its exhaust from its launch", () => MissilesFromTheirRails(strike.Value, data));
        return harness.Failures;
    }

    /// <summary>One drawn frame of the staged strike.</summary>
    private sealed record Sample(long Frame, bool Ordered, bool Attacking, bool AttackingCondition, int Selected, int Shown, int Hidden, int RuleCount, int Launched, bool Reloading,
        int MissilesAlive, int ExhaustSystems, int MissilesWithoutModel, int SteamVent, int Goo);

    private sealed record Launch(uint Id, long Frame, Vector3 Offset);

    private sealed class Strike
    {
        public required DrawSpec Draw { get; init; }
        public required List<Sample> Samples { get; init; }
        public required List<Launch> Launches { get; init; }
        public required Dictionary<int, double> Seconds { get; init; }
        public required int None { get; init; }
        public required int Attacking { get; init; }
        public required int RaiseTransition { get; init; }
        public required int LowerTransition { get; init; }
        public required float Heading { get; init; }

        public static Strike Record(GameData data)
        {
            var scenario = new DemoScenario("scud-storm", "scud-storm", "scud-storm", 900,
                [new ScenarioUnit("storm", "GLAScudStorm", 1, -300f, 0f), new ScenarioUnit("target", "AmericaBarracks", 2, 400f, 0f)],
                [new ScenarioOrder(30, "storm", ScenarioOrderKind.UsePower, X: 400f, Y: 0f, Template: "SuperweaponScudStorm")], 0f, 0f)
            { HumanFaction = "FactionGLA", OpponentFaction = "FactionAmerica", PowersReady = true };
            var scene = LiveScenario.Create(data, scenario, Viewport);
            var id = scene.Tags["storm"];
            TestHarness.Assert(data.Visuals.TryGet("GLAScudStorm", out var visual), "the Scud Storm has a visual");
            var draw = visual!.Draws[0];
            var none = BestStateSelector.FindBest(draw, ModelConditionSet.Empty);
            var attacking = BestStateSelector.FindBest(draw, ModelConditionSet.Parse("ATTACKING"));
            var raise = BestStateSelector.FindTransition(draw, none, attacking);
            var lower = BestStateSelector.FindTransition(draw, attacking, none);
            TestHarness.Assert(raise >= 0 && lower >= 0, $"the shipped module has both transitions ({raise}, {lower})");

            var library = new W3dAssetLibrary(path => data.Content.Exists(path) ? data.Content.OpenRead(path) : null);
            var seconds = new Dictionary<int, double>();
            double SecondsOf(int index)
            {
                if (seconds.TryGetValue(index, out var known)) return known;
                var state = ModelStateFollower.StateAt(draw, index)!;
                var spec = state.Animations.FirstOrDefault(animation => animation.File is not null);
                var binding = spec is null ? null : library.GetAnimation(spec.Name);
                var speed = state.AnimationSpeedMin > 0 ? (double)state.AnimationSpeedMin : 1.0;
                // isAnimationComplete: a ONCE clip has ended on its last frame; MANUAL and LOOP never end.
                var value = binding is null ? 0.0 : state.Mode is AnimationMode.Once or AnimationMode.OnceBackwards ? (binding.Animation.FrameCount - 1) / (binding.Animation.FrameRate * speed) : double.PositiveInfinity;
                return seconds[index] = value;
            }

            var follower = new ModelStateFollower(draw);
            var shown = -1;
            var elapsed = 0.0;
            var samples = new List<Sample>();
            var launches = new List<Launch>();
            var seen = new HashSet<uint>();
            var ordered = false;
            while (!scene.IsOver)
            {
                scene.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                elapsed += CnCNew.Tests.TestTime.FrameSeconds;
                var frame = scene.World.Tick / scene.World.Time.TicksPerOriginalFrame;
                var storm = scene.World.FindEntity(id)!;
                // The site's SpecialPowerCreate starts the power's clock when it is complete, after the staging made it ready: ready it again before the order.
                if (frame == 5)
                    foreach (var module in storm.Modules)
                        if (module is ISpecialPowerModule power) power.SetReadyFrame(scene.World.Tick);
                var drawable = scene.ObjectFrame.Drawables.First(item => item.Entity == id);
                var now = follower.Follow(drawable.Selections[0].StateIndex, elapsed >= SecondsOf(shown < 0 ? drawable.Selections[0].StateIndex : shown));
                if (now != shown) { shown = now; elapsed = 0.0; }
                var rules = ProjectileFeedback.Rules(draw, ModelStateFollower.StateAt(draw, shown)!, drawable.ProjectileClips);
                var attackingNow = scene.World.IsAttackingPlace(storm);
                ordered |= attackingNow;
                foreach (var missile in scene.World.Entities.Where(entity => entity.Template.Name == "ScudStormMissile"))
                    if (seen.Add(missile.Id.Value))
                        launches.Add(new Launch(missile.Id.Value, frame, new Vector3(FixedConvert.ToSingle(missile.X - storm.X), FixedConvert.ToSingle(missile.Y - storm.Y), FixedConvert.ToSingle(missile.Z - storm.Z))));
                var alive = scene.World.Entities.Where(entity => entity.Template.Name == "ScudStormMissile" && !entity.EffectivelyDead).ToList();
                var withoutModel = alive.Count(missile => scene.ObjectFrame.Drawables.FirstOrDefault(item => item.Entity == missile.Id)?.PrimaryModel is null);
                var clip = scene.World.ClipStatusOf(storm, 1);
                samples.Add(new Sample(frame, ordered, attackingNow, drawable.Conditions.Contains(ModelConditions.Attacking), drawable.Selections[0].StateIndex, shown,
                    rules.IsDefaultOrEmpty ? 0 : rules.Count(rule => rule.Hide), rules.IsDefaultOrEmpty ? 0 : rules.Length, seen.Count, clip is { Remaining: 0 },
                    alive.Count, scene.Fx.Particles.RunningTemplates.Count(name => name == "ScudMissileExhaust"), withoutModel,
                    scene.Fx.Particles.EmittingTemplates.Count(name => name == "SteamVent"), scene.Fx.Particles.EmittingTemplates.Count(name => name == "ScudStormBuildingGoo")));
            }
            foreach (var index in new[] { none, attacking, draw.States.Length + raise, draw.States.Length + lower }) SecondsOf(index);
            return new Strike
            {
                Draw = draw, Samples = samples, Launches = launches, Seconds = seconds, None = none, Attacking = attacking, RaiseTransition = draw.States.Length + raise,
                LowerTransition = draw.States.Length + lower, Heading = FixedConvert.ToSingle(scene.World.FindEntity(id)!.Angle),
            };
        }
    }

    private static void AttackingThroughout(Strike strike)
    {
        var during = strike.Samples.Where(sample => sample.Attacking).ToList();
        var before = strike.Samples.Where(sample => !sample.Ordered).ToList();
        var after = strike.Samples.SkipWhile(sample => !sample.Attacking).SkipWhile(sample => sample.Attacking).ToList();
        Console.WriteLine($"       attack frames {during.FirstOrDefault()?.Frame}..{during.LastOrDefault()?.Frame}, {strike.Launches.Count} missiles");
        TestHarness.Assert(during.Count > 90 && strike.Launches.Count == Rounds, $"the site attacks the place and fires its nine missiles ({during.Count} frames, {strike.Launches.Count})");
        TestHarness.Assert(before.All(sample => !sample.AttackingCondition && sample.Selected == strike.None), "before the order it lies around (NONE)");
        var lost = during.Where(sample => !sample.AttackingCondition || sample.Selected != strike.Attacking).Select(sample => sample.Frame).ToList();
        TestHarness.Assert(lost.Count == 0, $"through the wind-up and every shot it is ATTACKING and selects the raised state (not in frames {string.Join(", ", lost.Take(10))})");
        TestHarness.Assert(after.Count > 0 && after.All(sample => !sample.AttackingCondition && sample.Selected == strike.None), "after its last missile it is back to NONE");
    }

    private static void RaisedAndLowered(Strike strike)
    {
        var raise = strike.Seconds[strike.RaiseTransition];
        var lower = strike.Seconds[strike.LowerTransition];
        var raising = strike.Samples.Where(sample => sample.Shown == strike.RaiseTransition).ToList();
        var lowering = strike.Samples.Where(sample => sample.Shown == strike.LowerTransition).ToList();
        var firstAttack = strike.Samples.First(sample => sample.Attacking);
        var firstLaunch = strike.Launches.Min(launch => launch.Frame);
        Console.WriteLine($"       raise {raise:F2} s shown {raising.Count} frames from {raising.FirstOrDefault()?.Frame}; lower {lower:F2} s shown {lowering.Count} frames from {lowering.FirstOrDefault()?.Frame}; first launch {firstLaunch}");
        TestHarness.Assert(raise > 0.5 && lower > 0.5, $"both transitions play a clip ({raise:F2} s, {lower:F2} s)");
        var drawnFrames = (int)Math.Round(raise / CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(raising.Count > 0 && raising[0].Frame <= firstAttack.Frame + 1 && Math.Abs(raising.Count - drawnFrames) <= 2,
            $"the order raises the missiles at once, for the transition's clip ({raising.Count} frames, {drawnFrames} expected, from {raising.FirstOrDefault()?.Frame}, attack from {firstAttack.Frame})");
        // The weapon's PreAttackDelay (3 s, "give the missiles time to get up, since we can't watch the animation") holds the first shot while they rise; the clip of the rise is a tenth longer,
        // so in the original too the first missile leaves in the last moments of the transition.
        TestHarness.Assert(firstLaunch - raising[0].Frame >= 85 && firstLaunch - raising[0].Frame <= 95 && firstLaunch > raising[(int)(raising.Count * 0.85)].Frame,
            $"the first missile waits for the wind-up while they rise ({firstLaunch - raising[0].Frame} frames after the order)");
        var afterRaise = strike.Samples.SkipWhile(sample => sample.Shown != strike.RaiseTransition).SkipWhile(sample => sample.Shown == strike.RaiseTransition).TakeWhile(sample => sample.Attacking).ToList();
        TestHarness.Assert(afterRaise.Count > 0 && afterRaise.All(sample => sample.Shown == strike.Attacking), "then the raised state (ATTACKING) stands until the last missile");
        var lastAttack = strike.Samples.Last(sample => sample.Attacking);
        drawnFrames = (int)Math.Round(lower / CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(lowering.Count > 0 && lowering[0].Frame <= lastAttack.Frame + 2 && Math.Abs(lowering.Count - drawnFrames) <= 2,
            $"the end of the attack lowers the rails ({lowering.Count} frames, {drawnFrames} expected, from {lowering.FirstOrDefault()?.Frame}, attack until {lastAttack.Frame})");
        TestHarness.Assert(strike.Samples.Last().Shown == strike.None, "and the site lies around again");
    }

    /// <summary>
    /// #391: <c>W3DModelDraw::setModelState</c> stops the systems of the state it leaves and starts the <c>ParticleSysBone</c> systems of the one it enters, transition states included. The lying
    /// and the raised state blow <c>SteamLarge</c> and <c>SteamMedium</c>; <c>Trans_READY Trans_ATTACKING</c> adds the goo of <c>FXBone01</c> to <c>FXBone03</c>, and <c>Trans_ATTACKING Trans_READY</c>
    /// blows <c>SteamVent</c> from both steam bones instead - the only state that does. The attached effects follow the transitions in game time, the drawn module in drawn frames: a frame
    /// or two apart at either end.
    /// </summary>
    private static void TransitionSystems(Strike strike)
    {
        var lowering = strike.Samples.Where(sample => sample.Shown == strike.LowerTransition).ToList();
        var raising = strike.Samples.Where(sample => sample.Shown == strike.RaiseTransition).ToList();
        Console.WriteLine($"       vent steam in {strike.Samples.Count(sample => sample.SteamVent > 0)} frames, {lowering.Count} lowering; goo while raising {raising.Count(sample => sample.Goo >= 3)} of {raising.Count}");
        TestHarness.Assert(lowering.Count > 10, $"the rails go down ({lowering.Count} frames)");
        var inner = lowering.Skip(2).SkipLast(2).ToList();
        var missing = inner.Where(sample => sample.SteamVent != 2).Select(sample => $"{sample.Frame}:{sample.SteamVent}").ToList();
        TestHarness.Assert(missing.Count == 0, $"both vents steam while the rails go down ({string.Join(", ", missing.Take(10))})");
        var first = lowering[0].Frame;
        var last = lowering[^1].Frame;
        var outside = strike.Samples.Where(sample => (sample.Frame < first - 2 || sample.Frame > last + 2) && sample.SteamVent > 0).Select(sample => sample.Frame).ToList();
        TestHarness.Assert(outside.Count == 0, $"and at no other time ({string.Join(", ", outside.Take(10))})");
        var dry = raising.Skip(2).Where(sample => sample.Goo < 3).Select(sample => $"{sample.Frame}:{sample.Goo}").ToList();
        TestHarness.Assert(raising.Count > 10 && dry.Count == 0, $"the three goo bones drip while the missiles rise ({string.Join(", ", dry.Take(10))})");
    }

    private static void RoundsOnTheRails(Strike strike)
    {
        var before = strike.Samples.Where(sample => !sample.Ordered).ToList();
        TestHarness.Assert(before.All(sample => sample.RuleCount == Rounds && sample.Hidden == 0), "before the strike the nine missiles lie on their rails");
        var raised = strike.Samples.Where(sample => sample.Attacking && sample.Shown == strike.Attacking).ToList();
        var wrong = raised.Where(sample => sample.RuleCount != Rounds || sample.Hidden != Math.Min(sample.Launched, Rounds)).Select(sample => $"{sample.Frame}:{sample.Hidden}/{sample.Launched}").ToList();
        TestHarness.Assert(raised.Count > 0 && wrong.Count == 0, $"while it fires each launched missile is gone from the model ({string.Join(", ", wrong.Take(10))})");
        // Weapon::getRemainingAmmo is none while the emptied clip reloads (ClipReloadTime 10 s); the lowering clip (Trans_ATTACKING Trans_READY) names no launch bone, so it shows its own parts.
        var reloading = strike.Samples.Where(sample => sample.Reloading).ToList();
        TestHarness.Assert(reloading.Count > 250, $"the clip reloads after the strike ({reloading.Count} frames)");
        TestHarness.Assert(reloading.All(sample => sample.Shown != strike.LowerTransition || sample.RuleCount == 0), "the lowering clip is left to its own parts");
        var lying = ModelStateFollower.StateAt(strike.Draw, strike.None)!;
        var empty = ProjectileFeedback.Rules(strike.Draw, lying, [new ProjectileClip(1, 0, Rounds)]);
        TestHarness.Assert(empty.Length == Rounds && empty.All(rule => rule.Hide) && empty[0].SubObject == "weapona01" && empty[^1].SubObject == "weapona09",
            "a lying site whose clip reloads shows none of its nine (WeaponA01 to WeaponA09 hidden)");
        var reloaded = strike.Samples.SkipWhile(sample => !sample.Reloading).SkipWhile(sample => sample.Reloading).Where(sample => sample.Shown == strike.None).ToList();
        TestHarness.Assert(reloaded.Count > 0 && reloaded.All(sample => sample.Hidden == 0 && sample.RuleCount == Rounds), $"the reloaded clip lies on its rails again ({reloaded.Count} frames)");
        TestHarness.Assert(ProjectileFeedback.Rules(strike.Draw, lying, [new ProjectileClip(0, 0, Rounds)]).IsDefaultOrEmpty, "a slot the module does not show changes nothing");
    }

    private static void MissilesFromTheirRails(Strike strike, GameData data)
    {
        TestHarness.Assert(data.Templates.Geometry.WeaponLaunches.TryGetValue("GLAScudStorm", out var points) && points.Slots[1].Barrels.Length == Rounds,
            "the site's secondary slot has nine barrels (WeaponA01 to WeaponA09 of the raised state)");
        var barrels = points!.Slots[1].Barrels;
        var (cos, sin) = (MathF.Cos(strike.Heading), MathF.Sin(strike.Heading));
        var rails = barrels.Select(barrel => new Vector3(FixedConvert.ToSingle(barrel.X) * cos - FixedConvert.ToSingle(barrel.Y) * sin,
            FixedConvert.ToSingle(barrel.X) * sin + FixedConvert.ToSingle(barrel.Y) * cos, FixedConvert.ToSingle(barrel.Z))).ToList();
        var order = strike.Launches.Select(launch => rails.Select((rail, index) => (Index: index, Distance: Vector3.Distance(rail, launch.Offset))).MinBy(entry => entry.Distance)).ToList();
        Console.WriteLine($"       missiles on rails {string.Join(", ", order.Select(entry => $"{entry.Index + 1}@{entry.Distance:0.0}"))}");
        TestHarness.Assert(order.Select(entry => entry.Index).SequenceEqual(Enumerable.Range(0, Rounds)), "the k-th missile leaves the k-th rail, the first one hidden first");
        TestHarness.Assert(order.All(entry => entry.Distance < 12f), "each one where its rail is (within a frame of its flight)");
        var flying = strike.Samples.Where(sample => sample.MissilesAlive > 0).ToList();
        TestHarness.Assert(flying.All(sample => sample.MissilesWithoutModel == 0), "every missile is drawn while it flies");
        TestHarness.Assert(flying.All(sample => sample.ExhaustSystems >= sample.MissilesAlive), $"and carries its exhaust from its launch ({string.Join(", ", flying.Where(sample => sample.ExhaustSystems < sample.MissilesAlive).Take(5).Select(sample => $"{sample.Frame}:{sample.ExhaustSystems}/{sample.MissilesAlive}"))})");
    }
}
