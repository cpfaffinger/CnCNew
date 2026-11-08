using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, the owner's playtest of 4 November 2026 (finding 15): the Particle Uplink Cannon and the Nuclear Missile silo are <c>POWERED</c> and consume 10 units; a player whose power is short
/// puts <c>DISABLED_UNDERPOWERED</c> on them (<c>Object::pauseAllSpecialPowers</c>: a disable of any type but HELD stops the countdown once, the end of the last one lets it go). The Scud Storm is
/// neither POWERED nor a consumer and runs on.
/// </summary>
static partial class Tests
{
    public static void RealSuperweaponsPauseInABrownOut(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var (template, power) in new[] { ("AmericaParticleCannonUplink", "SuperweaponParticleUplinkCannon"), ("ChinaNuclearMissileLauncher", "SuperweaponNeutronMissile") })
            {
                var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
                var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "sw");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "sw", fingerprint, terrain.Fingerprint), catalog, terrain);
                var feeder = new Feeder(world);
                Check.That(feeder.Spawn(1, template, owner: 1, x: 800, y: 800).Accepted, $"{rate} {template}: the silo spawns.");
                world.Step();
                var silo = world.FindEntity(new EntityId(1))!;
                var module = silo.Modules.OfType<SpecialPowerModuleBase>().First();
                Check.That(!world.HasSufficientPower(new PlayerId(1)) && (world.DisabledFlagsOf(silo) & DisabledMask.Underpowered) != DisabledMask.None, $"{rate} {template}: without a power plant the player is short of power and the silo is underpowered.");
                var ready = module.AvailableOnFrame(world);
                var seconds = world.Time.TicksPerSecond;
                // A hundred seconds in the brown-out: the clock stands.
                for (var index = 0; index < world.Frames(100 * 30); index++) world.Step();
                Check.That(module.IsPaused, $"{rate} {template}: the power's countdown is paused.");
                // Two plants end the shortage (each makes 5, the silo consumes 10).
                Check.That(feeder.Spawn(1, "AmericaPowerPlant", owner: 1, x: 500, y: 500).Accepted && feeder.Spawn(1, "AmericaPowerPlant", owner: 1, x: 500, y: 560).Accepted, $"{rate} {template}: the plants spawn.");
                world.Step();
                world.Step();
                Check.That(world.HasSufficientPower(new PlayerId(1)) && !module.IsPaused, $"{rate} {template}: the power is back and the countdown goes on.");
                // The silo was ready at 'ready' without the shortage; it is ready the hundred seconds later (one frame of edge detection each way), not before.
                var delay = module.AvailableOnFrame(world) - ready;
                var expected = 100 * seconds;
                var slack = 2L * world.Time.TicksPerOriginalFrame;
                Check.That(delay >= expected - slack && delay <= expected + slack, $"{rate} {template}: the ready tick moved by the hundred seconds of the shortage ({delay} ticks of {expected}).");
                Check.That(!module.IsReady(world), $"{rate} {template}: it is not ready yet.");
            }
            // The script statuses "disabled" and "unpowered" of a map object are disabled types like any other: a Scud Storm (no power consumer) stands still under either.
            // A shared-timer power is the player's own clock and is not paused by a disabled building - but the building cannot fire it.
            foreach (var (flags, label) in new[] { (CnCNew.Simulation.Commands.MapObjectFlags.Disabled, "disabled"), (CnCNew.Simulation.Commands.MapObjectFlags.Unpowered, "unpowered") })
            {
                var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
                var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "sw");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "sw", fingerprint, terrain.Fingerprint), catalog, terrain);
                Check.That(world.Submit(new CnCNew.Simulation.Commands.PlaceMapObjectCommand(PlayerId.System, 1, world.Tick, "GLAScudStorm", new PlayerId(1), CnCNew.Simulation.Numerics.Fixed.FromInt(800), CnCNew.Simulation.Numerics.Fixed.FromInt(800),
                    CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero, 100, flags, 0)).Accepted, $"{rate}: a {label} Scud Storm is placed.");
                world.Step();
                world.Step();
                var silo = world.Entities.Single(entity => entity.Template.Name == "GLAScudStorm");
                var module = silo.Modules.OfType<SpecialPowerModuleBase>().First();
                Check.That(world.HasSufficientPower(new PlayerId(1)) && module.IsPaused && !module.IsReady(world), $"{rate}: a {label} Scud Storm has its countdown paused although the player has power.");
            }
        }
    }
}
