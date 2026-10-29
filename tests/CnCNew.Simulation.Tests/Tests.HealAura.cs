using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-73, the heal aura (<c>AutoHealBehavior</c> with a <c>Radius</c>, the ambulance's): which object is inside the radius is decided where the frames end - above 30 Hz a mover that
/// had decided its frame stood inside its step, and the aura healed it when it had left the radius at 30 Hz (a dozer that drove away from an ambulance in a match).
/// </summary>
static partial class Tests
{
    private const string HealAuraIni = """
        Locomotor AuraLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 300.0
          Acceleration = 3000.0
          Braking = 3000.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object AuraMedic
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 5
            Radius = 100.0
            KindOf = VEHICLE
          End
        End
        Object AuraTruck
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL AuraLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    /// <summary>A hurt truck drives fast out of an aura that heals every frame: it is healed in the same frames at 30, 60 and 120 Hz, the last time in the frame before it leaves.</summary>
    public static void HealAuraJudgesTheRadiusWhereTheFramesEnd()
    {
        string? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/HealAura.ini"] = HealAuraIni });
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (_, _) => 0), "aura");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            // The truck first: it has its turn in a tick before the medic's, as the dozer had before the ambulance's.
            Check.That(feeder.Spawn(1, "AuraTruck", owner: 1, x: 235, y: 200).Accepted && feeder.Spawn(1, "AuraMedic", owner: 1, x: 200, y: 200).Accepted, $"{rate}: truck and medic spawn.");
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var truck = world.Entities.Single(value => value.Template.Name == "AuraTruck");
            Check.That(feeder.SetMoveTarget(1, truck.Id.Value, 500, 200).Accepted, $"{rate}: the truck drives off.");
            var heals = new List<long>();
            var health = truck.Body!.Health;
            for (var frame = 0; frame < 200; frame++)
            {
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                if (truck.Body.Health > health) heals.Add(frame);
                health = truck.Body.Health;
            }
            var line = string.Join(",", heals);
            Check.That(heals.Count >= 2, $"{rate}: the truck is healed while it is near ({line}).");
            if (reference is null) reference = line;
            else Check.That(line == reference, $"{rate}: healed in the frames 30 Hz heals it in ({line} against {reference}).");
        }
    }
}
