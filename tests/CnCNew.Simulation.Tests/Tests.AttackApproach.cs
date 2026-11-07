using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// F-143: AIAttackApproachTargetState::computePath asks Pathfinder::findAttackPath for every mobile attacker whose weapon is not a contact weapon, not only for one that needs a
// line of sight: the search keeps the weapon's minimum range and never accepts the attacker's own cell.
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> AttackApproachFiles() => new Dictionary<string, string>(AcquisitionFiles())
    {
        ["Data/INI/Object/AttackApproachFixture.ini"] = """
        Locomotor AapTreads
          Surfaces = GROUND
          Speed = 30.0
          Acceleration = 300.0
          Braking = 300.0
          TurnRate = 360.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Weapon AapMortar
          PrimaryDamage = 10
          AttackRange = 120.0
          MinimumAttackRange = 60.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End

        Object AapMortarTruck
          KindOf = SELECTABLE VEHICLE CAN_ATTACK
          VisionRange = 300.0
          Locomotor = SET_NORMAL AapTreads
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AapMortar
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
        End

        Object AapTarget
          KindOf = SELECTABLE VEHICLE
          VisionRange = 100.0
          Locomotor = SET_NORMAL AapTreads
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>A computer player's mortar truck idle on the point where an enemy stands; returns the shots it fired over ten seconds, its distance to the victim and its arrivals.</summary>
    private static (int Shots, Fixed Distance, int Arrivals) MortarOnTopOfItsVictim(SimulationRate rate, bool withTerrain)
    {
        var fixture = new SyntheticFixture(rate, files: AttackApproachFiles());
        var terrain = withTerrain ? TerrainMap.Build(SyntheticMaps.Build(30, 30, 1), "attack-approach") : null;
        var world = terrain is null
            ? new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, ComputerAgainstHuman, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates)
            : new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, ComputerAgainstHuman, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "AapMortarTruck", owner: 1, x: 150, y: 150);
        feeder.Spawn(2, "AapTarget", owner: 2, x: 150, y: 150);
        world.Step();
        var mortar = world.Entities.First(value => value.Template.Name == "AapMortarTruck");
        var victim = world.Entities.First(value => value.Template.Name == "AapTarget");
        var shots = 0;
        var arrivals = 0;
        for (var step = 0; step < world.Frames(300); step++)
            foreach (var value in world.Step().Events)
            {
                if (value.Entity != mortar.Id) continue;
                if (value.Kind == SimEventKind.WeaponFired) shots++;
                if (value.Kind == SimEventKind.MoveArrived) arrivals++;
            }
        var dx = victim.X - mortar.X;
        var dy = victim.Y - mortar.Y;
        return (shots, Fixed.Sqrt(dx * dx + dy * dy), arrivals);
    }

    public static void AttackApproachKeepsTheMinimumRange()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
            foreach (var withTerrain in new[] { true, false })
            {
                var (shots, distance, arrivals) = MortarOnTopOfItsVictim(rate, withTerrain);
                var where = $"{rate}{(withTerrain ? "" : " without terrain")}";
                Check.That(shots > 0, $"{where}: a mortar on top of its victim moves out of its minimum range and fires ({shots} shots, {arrivals} arrivals).");
                Check.That(distance >= Fixed.FromInt(59) && distance <= Fixed.FromInt(120), $"{where}: it fires from within its range of 60 to 120, the minimum range read as the weapon reads it ({distance}).");
                Check.That(arrivals < 10, $"{where}: it does not begin its approach again in every frame ({arrivals} arrivals).");
            }
    }
}

// F-143 on the shipped data: the tunnel defenders of the retail reference match stood on the point of an enemy, their rocket (minimum range 5) found the victim out of range and the
// approach began again in every frame (11.5 million arrivals in a match).
static partial class Tests
{
    public static void RealTunnelDefenderOnItsVictim(string directory)
    {
        var content = CnCNew.Content.ContentFileSystem.Open(directory);
        var options = CnCNew.Data.Compilation.DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = CnCNew.Data.Compilation.MapOverlayMode.Exclude };
        var graph = CnCNew.Data.Compilation.DefinitionCompiler.Compile(new RealSource(content), CnCNew.Data.Registry.ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = System.Collections.Immutable.ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "Tunnel", IsComputer: true), new PlayerConfiguration(new PlayerId(2), "Enemy"));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
            foreach (var withTerrain in new[] { true, false })
        {
            var catalog = CnCNew.Simulation.Templates.TemplateCatalog.Build(graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            // The retail reference match runs on a world without terrain.
            var terrain = withTerrain ? TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "tunnel-defender") : null;
            var world = terrain is null
                ? new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-tunnel-defender", fingerprint), catalog)
                : new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-tunnel-defender", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            void Spawn(string template, int owner, int x, int y)
                => Check.That(world.Submit(new CnCNew.Simulation.Commands.SpawnCommand(new PlayerId(owner), ++sequence, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            Spawn("GLAInfantryTunnelDefender", 1, 400, 400);
            Spawn("AmericaVehicleDozer", 2, 400, 400);
            world.Step();
            var defender = world.Entities.First(value => value.Template.Name == "GLAInfantryTunnelDefender");
            var shots = 0;
            var arrivals = 0;
            for (var step = 0; step < world.Frames(300); step++)
                foreach (var value in world.Step().Events)
                {
                    if (value.Entity != defender.Id) continue;
                    if (value.Kind == CnCNew.Simulation.Events.SimEventKind.WeaponFired) shots++;
                    if (value.Kind == CnCNew.Simulation.Events.SimEventKind.MoveArrived) arrivals++;
                }
            Check.That(shots > 0, $"{rate}{(withTerrain ? "" : " without terrain")}: a tunnel defender on its victim's point steps out of the rocket's minimum range and fires ({shots} shots, {arrivals} arrivals).");
            Check.That(arrivals < 10, $"{rate}{(withTerrain ? "" : " without terrain")}: it does not begin its approach again in every frame ({arrivals} arrivals).");
        }
    }
}
