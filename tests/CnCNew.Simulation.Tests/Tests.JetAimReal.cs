using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, #436: a jet cannot turn on the spot, so <c>AIAttackAimAtTargetState::update</c> flies it at its victim (<c>setLocomotorGoalPositionExplicit</c>) and fires only when its heading is
/// within the weapon's <c>AcceptableAimDelta</c> of the bearing to the victim, at least <c>REL_THRESH</c> (0.035 rad). A Stealth Fighter (aim delta 0), a Raptor and a MiG (30 degrees) are
/// made in reach of a power plant heading away from it and ordered to fire, as the buff probe does (no move order): each turns round, fires its clip facing the plant, and every missile hits it.
/// Before, the jet fired at once heading away, and its missiles missed.
/// </summary>
static partial class Tests
{
    public static void RealJetFiresOnlyFacingItsVictim(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var cases = new[] { ("AmericaJetStealthFighter", "America", "China", "ChinaPowerPlant"), ("AmericaJetRaptor", "America", "China", "ChinaPowerPlant"), ("ChinaJetMIG", "China", "America", "AmericaPowerPlant") };
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var (jetName, side, enemySide, targetName) in cases)
            {
                var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), side), new PlayerConfiguration(new PlayerId(2), enemySide));
                var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "jet-aim");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "jet-aim", fingerprint, terrain.Fingerprint), catalog, terrain);
                var feeder = new Feeder(world);
                // The jet heads west, the plant lies 200 east of it: in reach of every one of the three weapons, past their minimum ranges, and straight behind the jet.
                Check.That(feeder.Spawn(1, jetName, owner: 1, x: 600, y: 600, angle: FixedTrig.Pi).Accepted, $"{rate} {jetName}: the jet spawns.");
                Check.That(feeder.Spawn(2, targetName, owner: 2, x: 800, y: 600).Accepted, $"{rate} {jetName}: the plant spawns.");
                feeder.SetRelationship(1, 2, Relationship.Enemies);
                feeder.SetRelationship(2, 1, Relationship.Enemies);
                world.RevealMapPermanently(new PlayerId(1), true);
                world.Step();
                var plant = world.Entities.Single(value => value.Template.Name == targetName);
                var jet = world.Entities.Single(value => value.Template.Name == jetName);
                var weapon = world.Weapons[jet.Template.WeaponSets[0].Weapons.First(value => value >= 0)];
                var allowed = Fixed.Max(weapon.AimDelta, Fixed.FromDecimal(0.035m));
                var shots = 0;
                var hits = 0;
                var worst = Fixed.Zero;
                for (var index = 0; index < world.Frames(900); index++)
                {
                    // The order of the buff probe and of the game layer's attack run: a fire order every frame, no move order - the attack itself flies the jet.
                    if (world.Time.IsOriginalFrameBoundary(world.Tick)) feeder.FireWeapon(1, jet.Id.Value, plant.Id.Value);
                    var events = world.Step().Events;
                    foreach (var fired in events.Where(value => value.Kind == SimEventKind.WeaponFired && value.Entity == jet.Id))
                    {
                        shots++;
                        // Where the jet's frame of the shot ends, as the aim judged it.
                        var tick = world.Tick - 1;
                        var (x, y, _) = jet.FrameEnd(tick);
                        var (plantX, plantY, _) = plant.FrameEnd(tick);
                        var off = FixedTrig.AngleDifference(jet.FrameEndAngle(tick), FixedTrig.Atan2(plantY - y, plantX - x)).Abs();
                        worst = Fixed.Max(worst, off);
                    }
                    hits += events.Count(value => value.Kind == SimEventKind.EntityDamaged && value.Entity == plant.Id && value.Other == jet.Id);
                }
                Check.That(shots >= 2, $"{rate} {jetName}: the jet fires its clip ({shots} shots).");
                Check.That(worst <= allowed, $"{rate} {jetName}: every shot leaves facing the plant (worst {worst.ToDecimal():0.####} rad, allowed {allowed.ToDecimal():0.####}).");
                Check.That(hits == shots, $"{rate} {jetName}: every missile hits the plant ({hits} hits of {shots} shots).");
            }
        }
    }
}
