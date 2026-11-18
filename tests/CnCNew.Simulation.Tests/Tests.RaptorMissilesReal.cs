using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// The owner's playtest of release 0.0.1, on real data.
static partial class Tests
{
    /// <summary>
    /// #450: "The Raptor's missiles no longer home on their target." A Raptor flies in at speed and fires its clip at a Battlemaster driving across its path and at a MiG flying
    /// across it. <c>RaptorJetMissile</c> follows its victim (<c>TryToFollowTarget = Yes</c>, <c>MissileAIUpdate::projectileLaunchAtObjectOrPosition</c>): every missile keeps the
    /// victim it was fired at, they turn after it, and each hits it although the victim moved on during the flight. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealRaptorMissilesHomeOnAMovingTarget(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var targetName in new[] { "ChinaTankBattleMaster", "ChinaJetMIG" })
            {
                var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "America"), new PlayerConfiguration(new PlayerId(2), "China"));
                var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "raptor-homing");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "raptor-homing", fingerprint, terrain.Fingerprint), catalog, terrain);
                var feeder = new Feeder(world);
                Check.That(feeder.Spawn(1, "AmericaJetRaptor", owner: 1, x: 100, y: 600, angle: Fixed.Zero).Accepted, $"{rate} {targetName}: the Raptor spawns.");
                Check.That(feeder.Spawn(2, targetName, owner: 2, x: 900, y: 300).Accepted, $"{rate} {targetName}: the target spawns.");
                feeder.SetRelationship(1, 2, Relationship.Enemies);
                feeder.SetRelationship(2, 1, Relationship.Enemies);
                world.RevealMapPermanently(new PlayerId(1), true);
                world.Step();
                var target = world.Entities.Single(value => value.Template.Name == targetName);
                var jet = world.Entities.Single(value => value.Template.Name == "AmericaJetRaptor");
                // The target crosses the Raptor's path; the Raptor gets up to speed before it attacks.
                feeder.SetMoveTarget(2, target.Id.Value, 900, 1300);
                feeder.SetMoveTarget(1, jet.Id.Value, 1500, 600);
                for (var index = 0; index < world.Frames(75); index++) world.Step();

                var shots = 0;
                var hits = 0;
                // Per missile: its victim, its heading at the launch, the most its heading turned since, where the victim stood at the launch and how far it moved since.
                var missiles = new Dictionary<uint, (EntityId Victim, Fixed FirstAngle, Fixed Turned, Fixed TargetX, Fixed TargetY, Fixed TargetMoved)>();
                for (var index = 0; index < world.Frames(300) && !target.EffectivelyDead; index++)
                {
                    if (world.Time.IsOriginalFrameBoundary(world.Tick)) feeder.FireWeapon(1, jet.Id.Value, target.Id.Value);
                    var events = world.Step().Events;
                    shots += events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == jet.Id);
                    hits += events.Count(value => value.Kind == SimEventKind.EntityDamaged && value.Entity == target.Id && value.Other == jet.Id);
                    foreach (var missile in world.Entities.Where(value => value.Template.Name == "RaptorJetMissile" && !value.DestroyRequested))
                    {
                        var ai = missile.Modules.OfType<MissileAIUpdate>().Single();
                        if (!missiles.TryGetValue(missile.Id.Value, out var seen))
                        {
                            missiles[missile.Id.Value] = (ai.Victim, missile.Angle, Fixed.Zero, target.X, target.Y, Fixed.Zero);
                            continue;
                        }
                        var (dx, dy) = (target.X - seen.TargetX, target.Y - seen.TargetY);
                        missiles[missile.Id.Value] = seen with
                        {
                            Victim = ai.Victim,
                            Turned = Fixed.Max(seen.Turned, FixedTrig.AngleDifference(missile.Angle, seen.FirstAngle).Abs()),
                            TargetMoved = Fixed.Sqrt(dx * dx + dy * dy),
                        };
                    }
                }
                Check.That(shots >= 3 && missiles.Count == shots, $"{rate} {targetName}: the Raptor fires its missiles ({shots} shots, {missiles.Count} missiles).");
                Check.That(missiles.Values.All(value => value.Victim == target.Id), $"{rate} {targetName}: every missile follows the target it was fired at.");
                Check.That(missiles.Values.All(value => value.TargetMoved > Fixed.FromInt(20)),
                    $"{rate} {targetName}: the target moves on while the missiles fly ({string.Join(", ", missiles.Values.Select(value => value.TargetMoved.ToDecimal().ToString("0")))}).");
                Check.That(missiles.Values.Max(value => value.Turned) > Fixed.FromDecimal(0.2m),
                    $"{rate} {targetName}: the missiles turn after it ({string.Join(", ", missiles.Values.Select(value => value.Turned.ToDecimal().ToString("0.00")))} rad).");
                Check.That(hits == shots, $"{rate} {targetName}: every missile hits the moving target ({hits} hits of {shots} shots).");
            }
        }
    }
}
