using System.Text.Json;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-80: at 30 Hz the step that ends the first leg of an exit path orders the second leg, and the acquisition of the same tick decides again. Above 30 Hz the second leg is ordered with the
/// last tick of the unit's frame, after that frame's acquisition, and the order cleared what that acquisition decided: the unit looked for victims a frame later from then on.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ExitAcquisitionFiles() => new Dictionary<string, string>(ConstructionFiles())
    {
        ["Data/INI/Object/ExitAcquisitionFixture.ini"] = """
        Weapon ExitTankGun
          PrimaryDamage = 1
          WeaponSpeed = 999999
          AttackRange = 120.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Object ExitTank
          KindOf = SELECTABLE VEHICLE
          VisionRange = 400.0
          Locomotor = SET_NORMAL GroundLoco
          BuildCost = 20
          BuildTime = 1
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ExitTankGun
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
            Turret
              TurretTurnRate = 180
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object ExitTarget
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>The fields of the unit's <c>acquisition</c> state, with its next look counted in the unit's own frames.</summary>
    private static string AcquisitionLine(Entity unit, int factor)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            unit.Describe(new JsonStateWriter(json));
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        if (!FindAcquisition(document.RootElement, out var acquisition)) return "none";
        var mode = acquisition.TryGetProperty("mode", out var value) ? value.GetInt64() : 0;
        var next = acquisition.TryGetProperty("nextCheck", out value) ? (value.GetInt64() - unit.SpawnTick) / factor : 0;
        var offset = acquisition.TryGetProperty("offset", out value) && value.GetBoolean();
        return $"{mode},{next},{offset},{acquisition.GetProperty("victim").GetInt64()}";
    }

    private static bool FindAcquisition(JsonElement element, out JsonElement found)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("acquisition"))
                {
                    found = property.Value;
                    return true;
                }
                if (FindAcquisition(property.Value, out found)) return true;
            }
        found = default;
        return false;
    }

    /// <summary>
    /// A factory sends a tank with a turret to a rally point past an enemy: the first leg ends in front of the factory, the second is ordered, the tank's turret looks for victims on the way
    /// and finds the enemy. Its acquisition state at the end of every frame of its own, and the frame it first has a victim, are those of 30 Hz at 60 and 120 Hz. Before, above 30 Hz the
    /// second leg's order cleared what the frame's acquisition had decided, and the tank decided again a frame later.
    /// </summary>
    public static void ExitPathAcquisitionIsTheSameAtEveryRate()
    {
        List<string>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, ExitAcquisitionFiles());
            world.RevealMapPermanently(new PlayerId(1), true);
            feeder.GiveMoney(1, 1000);
            feeder.Spawn(1, "ExitBarracks", owner: 1, x: 70, y: 100);
            feeder.Spawn(2, "ExitTarget", owner: 2, x: 340, y: 112);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var factory = ConstructionFind(world, "ExitBarracks").Id.Value;
            Check.That(feeder.SetRallyPoint(1, factory, 300, 112).Accepted, $"{rate}: the rally point is accepted.");
            Check.That(feeder.QueueProduction(1, factory, "ExitTank").Accepted, $"{rate}: the order is accepted.");

            var trace = new List<string>();
            Entity? tank = null;
            for (var step = 0; step < world.Frames(900); step++)
            {
                world.Step();
                world.VerifyIncrementalHash();
                tank ??= world.Entities.FirstOrDefault(value => value.Template.Name == "ExitTank");
                if (tank is null || (world.Tick - tank.SpawnTick) % factor != 0) continue;
                trace.Add($"{(world.Tick - tank.SpawnTick) / factor}:{AcquisitionLine(tank, factor)},{tank.X.Raw},{tank.Y.Raw}");
            }

            Check.That(tank is not null, $"{rate}: the tank was built.");
            Check.That(trace.Any(line => !line.Split(':')[1].StartsWith("none", StringComparison.Ordinal) && line.Split(',')[3] != "0"), $"{rate}: the tank found its victim on the way.");
            if (reference is null) reference = trace;
            else
            {
                var count = Math.Min(reference.Count, trace.Count);
                var first = Enumerable.Range(0, count).FirstOrDefault(index => reference[index] != trace[index], -1);
                Check.That(first < 0, $"{rate}: the tank's acquisition is that of 30 Hz at the end of every frame of its own (first difference: {(first < 0 ? "-" : $"{trace[first]} against {reference[first]}")}).");
            }
        }
    }
}
