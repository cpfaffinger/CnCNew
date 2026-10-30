using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-82: a unit sees its victim where the victim's frame ends. At 30 Hz a victim has taken its whole step when the acquisition asks whether its player still sees it; above 30 Hz the question
/// read the place inside the step, and a victim that left the sight in that step was given up a frame later.
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
    /// A tank takes a runner as its victim while the runner walks out of the tank's sight. The frame the tank gives it up, and its acquisition state at the end of every frame of its own, are those
    /// of 30 Hz at 60 and 120 Hz. Before, above 30 Hz the tank still saw the runner inside the step that took it out of sight and gave it up a frame later.
    /// </summary>
    public static void VictimLeavesTheSightWhereItsFrameEnds()
    {
        List<string>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, VictimSightFiles(), TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "sight"));
            feeder.Spawn(1, "SightTank", owner: 1, x: 200, y: 200);
            feeder.Spawn(2, "SightRunner", owner: 2, x: 260, y: 200);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "SightTank");
            var runner = world.Entities.Single(value => value.Template.Name == "SightRunner");
            for (var tick = 0; tick < world.Frames(30); tick++) world.Step();
            Check.That(AcquisitionLine(tank, factor).Split(',')[3] == runner.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{rate}: the tank has taken the runner as its victim ({AcquisitionLine(tank, factor)}).");
            Check.That(feeder.SetMoveTarget(2, runner.Id.Value, 500, 200).Accepted, $"{rate}: the move is accepted.");

            var trace = new List<string>();
            for (var step = 0; step < world.Frames(400); step++)
            {
                world.Step();
                if ((world.Tick - tank.SpawnTick) % factor == 0) trace.Add($"{(world.Tick - tank.SpawnTick) / factor}:{AcquisitionLine(tank, factor)}");
            }

            Check.That(AcquisitionLine(tank, factor) == "none" || AcquisitionLine(tank, factor).Split(',')[3] == "0", $"{rate}: the runner is out of sight in the end ({AcquisitionLine(tank, factor)}).");
            if (reference is null) reference = trace;
            else
            {
                var first = Enumerable.Range(0, Math.Min(reference.Count, trace.Count)).FirstOrDefault(index => reference[index] != trace[index], -1);
                Check.That(first < 0, $"{rate}: the tank's acquisition is that of 30 Hz at the end of every frame of its own (first difference: {(first < 0 ? "-" : $"{trace[first]} against {reference[first]}")}).");
            }
        }
    }
}
