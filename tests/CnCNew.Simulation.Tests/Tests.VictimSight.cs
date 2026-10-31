using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-82: a human player's unit sees its victim where the victim's frame ends - since F-17 only when it looks for one, the fog ends no attack. At 30 Hz a victim has taken its whole step when the look asks; above
/// 30 Hz the look read the place inside the step, and a victim that came into the sight in that step was taken a frame later.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> VictimSightFiles() => new Dictionary<string, string>(ExitAcquisitionFiles())
    {
        ["Data/INI/Object/VictimSightFixture.ini"] = """
        Weapon SightGun
          PrimaryDamage = 1
          WeaponSpeed = 999999
          AttackRange = 500.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Object SightTank
          KindOf = SELECTABLE VEHICLE
          VisionRange = 120.0
          ShroudClearingRange = 120.0
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SightGun
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
        Object SightRunner
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

    /// <summary>
    /// A runner walks into the sight of a human player's tank. The frame the tank takes it, and its acquisition state at the end of every frame of its own, are those of 30 Hz at 60 and 120 Hz.
    /// </summary>
    public static void VictimComesIntoTheSightWhereItsFrameEnds()
    {
        List<string>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, VictimSightFiles(), TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "sight"));
            feeder.Spawn(1, "SightTank", owner: 1, x: 200, y: 200);
            feeder.Spawn(2, "SightRunner", owner: 2, x: 420, y: 200);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "SightTank");
            var runner = world.Entities.Single(value => value.Template.Name == "SightRunner");
            for (var tick = 0; tick < world.Frames(30); tick++) world.Step();
            Check.That(AcquisitionLine(tank, factor) == "none" || AcquisitionLine(tank, factor).Split(',')[3] == "0", $"{rate}: the tank does not see the runner ({AcquisitionLine(tank, factor)}).");
            Check.That(feeder.SetMoveTarget(2, runner.Id.Value, 240, 200).Accepted, $"{rate}: the move is accepted.");

            var trace = new List<string>();
            for (var step = 0; step < world.Frames(400); step++)
            {
                world.Step();
                if ((world.Tick - tank.SpawnTick) % factor == 0) trace.Add($"{(world.Tick - tank.SpawnTick) / factor}:{AcquisitionLine(tank, factor)}");
            }

            Check.That(AcquisitionLine(tank, factor).Split(',')[3] == runner.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{rate}: the runner is the tank's victim in the end ({AcquisitionLine(tank, factor)}).");
            if (reference is null) reference = trace;
            else
            {
                var first = Enumerable.Range(0, Math.Min(reference.Count, trace.Count)).FirstOrDefault(index => reference[index] != trace[index], -1);
                Check.That(first < 0, $"{rate}: the tank's acquisition is that of 30 Hz at the end of every frame of its own (first difference: {(first < 0 ? "-" : $"{trace[first]} against {reference[first]}")}).");
            }
        }
    }
}
