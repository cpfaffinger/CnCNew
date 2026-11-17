using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using CnCNew.W3D;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// Issue 368, the Nuke Cannon's packing on the shipped data (<c>DeployStyleAIUpdate</c> with <c>ManualDeployAnimations</c> and <c>TurretsMustCenterBeforePacking</c>), frame by frame
/// at 30, 60 and 120 Hz.
/// <para>
/// What the original does: a move order to the deployed cannon first turns its turret back to the natural angle at half the turn rate (<c>setMyState(ALIGNING_TURRETS)</c>;
/// <c>TurretAIRecenterTurretState::update</c> calls <c>friend_turnTowardsAngle(natural, 0.5f, 0)</c>), the cannon still deployed and its turret shown. Only then it packs
/// (<c>UNDEPLOY</c>, <c>MODELCONDITION_PACKING</c>), and in every logic frame of the packing <c>DeployStyleAIUpdate::update</c> holds the animation at <c>framesLeft</c>
/// (<c>Drawable::setAnimationFrame</c>, <c>ANIM_MODE_MANUAL</c>): the unpacking animation runs backwards, one of its frames per logic frame, over <c>PackTime</c> - as long as the unpacking
/// took. In <c>NVNukeCn.NVNukeCn</c> the roof (<c>TURRETFRONT</c>) and the gun (<c>TURRETBACK</c>, <c>ELEVATIONBARREL</c>) move over the same frames, 71 to 100, so the gun goes in while the
/// roof closes, in the first second of the packing; the body comes down and the outriggers fold in after it.
/// </para>
/// </summary>
internal static class DeployAnimationTests
{
    private const string Cannon = "ChinaVehicleNukeLauncher";

    /// <summary><c>TurretTurnRate = 80</c> of the cannon's turret, in radians per logic frame of the original.</summary>
    private const double TurnPerFrame = 80.0 / 30.0 * Math.PI / 180.0;

    public static int Run(string[] args)
    {
        var harness = new TestHarness("deploy-animation");
        var contentDir = ContentDir(args);
        if (contentDir is not null)
        {
            var content = ContentFileSystem.Open(contentDir);
            harness.Run("Real data: in the Nuke Cannon's animation the roof and the gun move over the same frames", () => AnimationData(content));
            var graph = DefinitionCompiler.Compile(new DeployRealSource(content), ModuleRegistry.Default,
                DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
            foreach (var hertz in new[] { 30, 60, 120 })
                harness.Run($"Real data at {hertz} Hz: the Nuke Cannon turns its turret back at half its turn rate, then packs backwards over PackTime, frame by frame",
                    () => PackFrames(graph, hertz));
        }
        return harness.Failures;
    }

    private static string? ContentDir(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--content-dir") return args[i + 1];
        return null;
    }

    /// <summary>The pivots that move while the gun goes in and the roof closes have their channels over the same frames, at the end of the animation.</summary>
    private static void AnimationData(ContentFileSystem content)
    {
        var library = new W3dAssetLibrary(path => content.Exists(path) ? content.OpenRead(path) : null);
        var binding = library.GetAnimation("NVNukeCn.NVNukeCn");
        Check(binding is not null, "The Nuke Cannon's animation loads.");
        var animation = binding!.Animation;
        Check(animation.FrameCount == 101, $"The animation has frames 0 to 100, one per logic frame of PackTime 3333 ms: {animation.FrameCount}.");
        var skeleton = library.GetModel("NVNukeCn").Skeleton;
        (int First, int Last) Span(string pivot)
        {
            var index = skeleton.BoneNames.IndexOf(skeleton.BoneNames.First(name => name.Equals(pivot, StringComparison.OrdinalIgnoreCase)));
            var channels = animation.Channels.OfType<W3dRawMotionChannel>().Where(channel => channel.Pivot == index).ToArray();
            Check(channels.Length > 0, $"{pivot} is animated.");
            return (channels.Min(channel => channel.FirstFrame), channels.Max(channel => channel.LastFrame));
        }
        var roof = Span("TURRETFRONT");
        var carriage = Span("TURRETBACK");
        var barrel = Span("ELEVATIONBARREL");
        Check(roof == carriage && roof == barrel && roof.Last == 100,
            $"Roof {roof}, gun carriage {carriage} and barrel {barrel} move over the same frames at the end of the animation, so the gun goes in while the roof closes.");
    }

    private static void PackFrames(CompiledDefinitionGraph graph, int hertz)
    {
        var rate = SimulationRate.FromTicksPerSecond(hertz);
        var templates = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", "FactionAmerica"), new PlayerConfiguration(new PlayerId(2), "West", "FactionChina"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, "deploy-animation", fingerprint), templates);
        var perFrame = world.Time.TicksPerOriginalFrame;
        var sequence = 0UL;
        Entity Spawn(string template, int owner, int x, int y)
        {
            Check(world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns.");
            world.Step();
            return world.Entities.Where(entity => entity.Template.Name == template).OrderBy(entity => entity.Id.Value).Last();
        }

        var cannon = Spawn(Cannon, 2, 3500, 3000);
        // The victim stands to the cannon's left: its turret turns a quarter circle to it and back. An unarmed dozer: a tank would answer the shot, walk up to where its gun reaches
        // between the two circles (#422) and damage the cannon, whose model would then show another state.
        var target = Spawn("AmericaVehicleDozer", 1, 3500, 3250);
        var deploy = cannon.Modules.OfType<DeployStyleAIUpdate>().Single();
        var projector = new FrameProjector(ObjectVisualCatalog.Build(graph));
        DrawableInstance Drawn()
        {
            projector.Advance(world);
            return projector.Project(1f).Drawables.Single(drawable => drawable.Entity == cannon.Id);
        }
        static ModelStateSpec StateOf(DrawableInstance drawable) => drawable.Visual.Draws[0].States[drawable.Selections[0].StateIndex];
        static bool Shows(DrawableInstance drawable, string subObject) =>
            StateOf(drawable).SubObjects.LastOrDefault(entry => entry.SubObject.Equals(subObject, StringComparison.OrdinalIgnoreCase)) is { Hide: false };
        double Angle() => (double)cannon.TurretOf(0)!.Value.Angle.ToDecimal();

        // Unpacking: the logic holds the animation at PackTime - framesLeft, from its first frame to its last.
        var unpacking = new List<float>();
        for (var i = 0; i < 600 * perFrame && !(deploy.IsDeployed && Angle() >= Math.PI / 2 - 1e-3); i++)
        {
            // A player's attack order stands until it is carried out (the cannon drops the shots it is asked for while it unpacks).
            world.Submit(new FireWeaponCommand(new PlayerId(2), ++sequence, world.Tick, cannon.Id, target.Id));
            world.Step();
            var drawn = Drawn();
            Check((drawn.AnimationFrame is not null) == deploy.IsUnpacking, $"The logic holds the animation exactly while the cannon unpacks (tick {world.Tick}).");
            if (deploy.IsUnpacking) unpacking.Add(drawn.AnimationFrame!.Value);
        }
        Check(deploy.IsDeployed && Angle() >= Math.PI / 2 - 1e-3, $"The cannon unpacked and turned its turret to the victim ({Angle()}).");
        var packFrames = deploy.PackTicks / perFrame;
        Check(packFrames == 100 && unpacking.Count == deploy.PackTicks, $"The unpacking takes PackTime, 100 logic frames ({unpacking.Count} ticks).");
        Check(unpacking[0] <= 1f && unpacking[^1] >= packFrames - 1 && unpacking.Zip(unpacking.Skip(1)).All(pair => pair.Second - pair.First == 1f / perFrame),
            $"The unpacking animation runs forwards, one of its frames per logic frame: {unpacking[0]} .. {unpacking[^1]}.");

        // The move order: the turret first turns back to its natural angle at half its turn rate, the cannon still deployed; then it packs.
        Check(world.Submit(new SetMoveTargetCommand(new PlayerId(2), ++sequence, world.Tick, cannon.Id, Fixed.FromInt(3300), Fixed.FromInt(3000))).Accepted, "The cannon is ordered to move.");
        var aligning = 0;
        var packing = new List<(long TicksLeft, float Frame, ModelStateSpec State)>();
        var previousAngle = Angle();
        for (var i = 0; i < 900 * perFrame && !deploy.IsPacked; i++)
        {
            world.Step();
            var drawn = Drawn();
            var angle = Angle();
            if (deploy.IsPacking)
            {
                if (packing.Count == 0) Check(angle == 0.0 && drawn.TurretYaw == 0f, $"The cannon packs only once its turret stands at the natural angle ({angle}).");
                // The frames left in the tick just stepped (m_frameToWaitForDeploy - now of that logic frame).
                packing.Add((deploy.DeployDoneTick - (world.Tick - 1), drawn.AnimationFrame ?? float.NaN, StateOf(drawn)));
                Check(!Shows(drawn, "TURRET01") && Shows(drawn, "TURRETFRONT") && Shows(drawn, "TURRETBACK"), "While it packs the animated roof and gun are shown and the turret is hidden.");
            }
            else if (!deploy.IsPacked)
            {
                aligning++;
                Check(drawn.AnimationFrame is null && Shows(drawn, "TURRET01") && !Shows(drawn, "TURRETFRONT"), "While its turret turns back the cannon stays deployed, its turret shown.");
                Check(previousAngle - angle <= TurnPerFrame / 2 / perFrame + 1e-6, $"The turret turns back at half its turn rate ({previousAngle} -> {angle}).");
            }
            previousAngle = angle;
        }
        Check(deploy.IsPacked, "The cannon packed.");
        // A quarter circle at half of 80 degrees a second takes 67.5 logic frames, and a frame more to see the turret there.
        var alignFrames = (double)aligning / perFrame;
        Check(alignFrames >= 67 && alignFrames <= 70, $"The turret takes a quarter circle at half its turn rate to come back: {alignFrames} logic frames.");
        Check(packing.Count == unpacking.Count, $"The packing takes PackTime, as long as the unpacking ({packing.Count} ticks, {unpacking.Count} unpacking).");
        Check(packing.All(entry => entry.State.Conditions.Any(set => set.ToString().Contains("PACKING"))), "The cannon shows its PACKING state while it packs.");
        Check(packing.All(entry => entry.Frame == (float)entry.TicksLeft / perFrame),
            $"Every tick of the packing holds the animation at the frames left ({string.Join(", ", packing.Take(4).Select(entry => $"{entry.TicksLeft}:{entry.Frame}"))}).");
        Check(packing[0].Frame >= packFrames - 1, $"The packing begins at the animation's last frame: {packing[0].Frame}.");
        Check(packing[^1].Frame <= 1f && packing.Zip(packing.Skip(1)).All(pair => pair.First.Frame - pair.Second.Frame == 1f / perFrame),
            $"The packing animation runs backwards, one of its frames per logic frame, to its first: {packing[^1].Frame}.");
        // The gun goes in and the roof closes over frames 100 to 71, the first 30 logic frames of the packing; the body comes down and the outriggers fold in after it.
        var gunIn = packing.FindIndex(entry => entry.Frame <= 70f);
        Check(Math.Abs(gunIn - 30 * perFrame) <= perFrame, $"The gun is in and the roof closed {gunIn} ticks into the packing ({30 * perFrame} expected).");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

file sealed class DeployRealSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
