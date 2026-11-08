using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-140's rest, on the shipped data: the paths that fire outside an object's own attack state in this port, and what the original does with each of them.
/// <list type="bullet">
/// <item><description>A script's command button with a target (<c>Object::doCommandButtonAtObject</c>), <c>CleanupHazardUpdate::fireWhenReady</c> and
/// <c>AssistedTargetingUpdate::assistAttack</c> all end in <c>AIUpdateInterface::aiAttackObject</c>: the attack machine aims in the order's frame and fires in the next one, then on the even
/// frames. The port fired from the order's tick (a script) or from the module's own update.</description></item>
/// </list>
/// Every test runs the world at 30, 60 and 120 Hz and compares the shots in frames of the original.
/// </summary>
static partial class Tests
{
    private sealed record RealFireWorld(CnCNew.Data.Definitions.CompiledDefinitionGraph Graph, string Fingerprint);

    private static RealFireWorld OpenRealFireData(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        return new RealFireWorld(graph, fingerprint);
    }

    private static SimulationWorld NewRealFireWorld(RealFireWorld data, SimulationRate rate, bool computer)
    {
        var catalog = TemplateCatalog.Build(data.Graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "Shooter", IsComputer: computer), new PlayerConfiguration(new PlayerId(2), "Enemy"));
        return new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-fire-cycle", data.Fingerprint), catalog);
    }

    private static Entity SpawnReal(SimulationWorld world, ref ulong sequence, string template, int owner, int x, int y, bool step = true)
    {
        Check.That(world.Submit(new SpawnCommand(new PlayerId(owner), ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
        if (!step) return null!;
        world.Step();
        return world.Entities.Where(value => value.Template.Name == template).OrderByDescending(value => value.Id.Value).First();
    }

    /// <summary>The frames of the original, counted from <paramref name="start"/>, in which <paramref name="events"/> hold a shot of <paramref name="weapon"/> by one of <paramref name="shooters"/>.</summary>
    private static List<long> ShotFrames(SimulationWorld world, long start, IEnumerable<SimEvent> events, string? weapon, params EntityId[] shooters)
        => [..events.Where(value => value.Kind == SimEventKind.WeaponFired && shooters.Contains(value.Entity) && (weapon is null || value.Text == weapon)).Select(value => world.InFrames(value.Tick - start))];

    private static void BeginAtFrameOf(SimulationWorld world, Entity unit)
    {
        while (!world.Time.BeginsFrameOf(unit, world.Tick)) world.Step();
    }

    /// <summary>
    /// A script's command button with a target object (<c>NAMED_USE_COMMANDBUTTON_ABILITY_ON_NAMED</c> of a <c>FIRE_WEAPON</c> button) on a Ranger: <c>Object::doCommandButtonAtObject</c> ends in
    /// <c>aiAttackObject</c>, so the Ranger aims in the order's frame and fires in the next one - not in the order's tick.
    /// </summary>
    public static void RealScriptFireButtonGoesThroughTheAttackMachine(string directory)
    {
        var data = OpenRealFireData(directory);
        List<long>? first = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var world = NewRealFireWorld(data, rate, computer: false);
            ulong sequence = 0;
            var ranger = SpawnReal(world, ref sequence, "AmericaInfantryRanger", 1, 400, 400);
            var victim = SpawnReal(world, ref sequence, "AmericaVehicleDozer", 2, 460, 400);
            // The Ranger's view of the victim: the order of a script needs none, but the order is checked against what the Ranger can hit.
            BeginAtFrameOf(world, ranger);
            var start = world.Tick;
            Check.That(world.Submit(new FireWeaponCommand(new PlayerId(1), ++sequence, world.Tick, ranger.Id, victim.Id, WeaponSlot.Best, FireOrigin.Script)).Accepted, $"{rate}: the script's order is accepted.");
            var events = new List<SimEvent>();
            for (var tick = 0; tick < world.Frames(8); tick++) events.AddRange(world.Step().Events);
            var frames = ShotFrames(world, start, events, null, ranger.Id);
            System.Console.WriteLine($"       {rate}: the script's shot at frame {string.Join(",", frames)}");
            Check.That(frames.Count == 1, $"{rate}: a script's one-off button fires one shot ({frames.Count}).");
            Check.That(frames[0] >= 1, $"{rate}: the shot is the attack machine's - at the earliest in the frame after the order's (frame {frames[0]}).");
            first ??= frames;
            Check.That(frames.SequenceEqual(first), $"{rate}: the same frame as at 30 Hz ({string.Join(",", frames)} against {string.Join(",", first)}).");
        }
    }
}
