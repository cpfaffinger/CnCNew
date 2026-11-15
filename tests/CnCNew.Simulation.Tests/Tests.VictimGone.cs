using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Issue #366: tanks ordered to attack a building out of their range walk at it (by the client's pursuit, or by a limited attack's own approach); the building is destroyed by something else before they are in range. <c>AIAttackState::update</c> finds
/// the victim dead (<c>notifyVictimIsDead</c>), the state succeeds into <c>AI_IDLE</c>, and the tanks stand where they are - they do not drive on to where the building stood.
/// </summary>
static partial class Tests
{
    public static void RealAttackersStopWhenTheirVictimDies(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        Dictionary<uint, (Fixed X, Fixed Y)>? standAt30 = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "victim-gone");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-victim-gone", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            void Spawn(string template, int owner, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(new PlayerId(owner), ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            // The victim, far from the tanks; a building of the tanks' player beside it lets their player see it (an order names only what its player sees).
            Spawn("ChinaBarracks", 2, 1300, 800);
            Spawn("AmericaPowerPlant", 1, 1300, 1000);
            foreach (var y in new[] { 680, 740, 800, 860 }) Spawn("AmericaTankCrusader", 1, 150, y);
            for (var i = 0; i < world.Frames(2); i++) world.Step();
            var barracks = world.Entities.First(value => value.Template.Name == "ChinaBarracks");
            var tanks = world.Entities.Where(value => value.Template.Name == "AmericaTankCrusader").OrderBy(value => value.Id.Value).ToArray();
            Check.That(tanks.Length == 4, "Setup: four tanks.");
            Check.That(world.CanPlayerSee(new PlayerId(1), barracks), $"{rate}: setup - the tanks' player sees the barracks.");
            // The client's attack order on a victim out of range (StandingOrders.UpdateAttack): a pursuit to where the victim stands, given once for a victim that does not move.
            foreach (var tank in tanks[..3])
                Check.That(world.Submit(new PursueCommand(new PlayerId(1), ++sequence, world.Tick, tank.Id, barracks.Id, barracks.X, barracks.Y)).Accepted, "The attack order is accepted.");
            // An attack with a shot limit (a FIRE_WEAPON button) approaches by itself (#328).
            var limited = world.Submit(new FireWeaponCommand(new PlayerId(1), ++sequence, world.Tick, tanks[3].Id, barracks.Id, MaxShots: 3));
            Check.That(limited.Accepted, "The limited attack order is accepted: " + limited);
            for (var i = 0; i < world.Frames(150); i++) world.Step();
            foreach (var tank in tanks)
            {
                Check.That(tank.HasMoveTarget, $"{rate}: setup - tank {tank.Id.Value} is on its way to the barracks.");
                Check.That(tank.X < Fixed.FromInt(900), $"{rate}: setup - tank {tank.Id.Value} is still far out of range (x {tank.X}).");
            }
            // Something else destroys the barracks.
            Check.That(world.Submit(new KillCommand(PlayerId.System, ++sequence, world.Tick, barracks.Id)).Accepted, "The barracks is destroyed.");
            for (var i = 0; i < world.Frames(2); i++) world.Step();
            Check.That(barracks.EffectivelyDead, $"{rate}: the barracks is dead.");
            var stopped = new Dictionary<uint, (Fixed X, Fixed Y)>();
            foreach (var tank in tanks)
            {
                Check.That(!tank.HasMoveTarget, $"{rate}: tank {tank.Id.Value} stops when its victim is gone (it still drives to ({tank.MoveTargetX}, {tank.MoveTargetY}) from x {tank.X}).");
                stopped[tank.Id.Value] = (tank.X, tank.Y);
            }
            for (var i = 0; i < world.Frames(300); i++) world.Step();
            foreach (var tank in tanks)
            {
                var (x, y) = stopped[tank.Id.Value];
                var moved = ((tank.X - x).Abs() + (tank.Y - y).Abs()).Floor();
                Check.That(moved <= 2, $"{rate}: tank {tank.Id.Value} stands where it stopped (it moved {moved} afterwards, to x {tank.X}).");
            }
            // The same places at every rate.
            if (standAt30 is null) standAt30 = tanks.ToDictionary(value => value.Id.Value, value => (value.X, value.Y));
            else
                foreach (var tank in tanks)
                {
                    var at30 = standAt30[tank.Id.Value];
                    var gap = ((tank.X - at30.X).Abs() + (tank.Y - at30.Y).Abs()).Floor();
                    Check.That(gap <= 1, $"{rate}: tank {tank.Id.Value} stands at ({tank.X}, {tank.Y}), at 30 Hz at ({at30.X}, {at30.Y}).");
                }
        }
    }
}
