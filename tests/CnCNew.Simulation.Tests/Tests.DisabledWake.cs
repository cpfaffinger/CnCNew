using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-81: a disabled object's module that does not process the disable asks again. Above 30 Hz it asked again in the next tick, so the module woke inside a frame of its object when the
/// disable ended there, and every later wake of a module that sleeps by whole frames lay a share of a frame early: a horde member counted its neighbours half a frame before 30 Hz does.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> DisabledWakeFiles() => new Dictionary<string, string>(ConstructionFiles())
    {
        ["Data/INI/Object/DisabledWakeFixture.ini"] = """
        Object HordeGrunt
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = HordeUpdate Horde01
            UpdateRate = 1000
            RubOffRadius = 20
            Radius = 60
            KindOf = INFANTRY
            AlliesOnly = Yes
            ExactMatch = No
            Count = 3
            Action = HORDE
          End
        End
        """,
    };

    /// <summary>
    /// A soldier with a module that wakes once a second is disabled inside a frame and set free inside a later one. Its module wakes in the frames it wakes in at 30 Hz, on the frames of the
    /// soldier, at 60 and 120 Hz too. Before, above 30 Hz it woke in the tick the disable ended and from then on a share of a frame before the frame it wakes in at 30 Hz.
    /// </summary>
    public static void DisabledModuleWakesOnItsFrames()
    {
        List<long>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, DisabledWakeFiles());
            feeder.Spawn(1, "HordeGrunt", owner: 1, x: 100, y: 100);
            world.Step();
            var grunt = world.Entities.Single(value => value.Template.Name == "HordeGrunt");
            var horde = grunt.Modules.OfType<HordeUpdate>().Single();
            // Before the last tick of a frame of the soldier above 30 Hz, after the whole frame at 30 Hz: the module has been refused in that frame at every rate.
            void IntoFrame(int frame)
            {
                while (world.Tick < grunt.SpawnTick + (frame + 1) * factor - (factor > 1 ? 1 : 0)) world.Step();
            }

            IntoFrame(40);
            world.SetScriptDisable(grunt, Entity.ScriptDisabled, true);
            IntoFrame(100);
            world.SetScriptDisable(grunt, Entity.ScriptDisabled, false);
            var wakes = new List<long>();
            var last = horde.NextWake;
            while (wakes.Count < 5)
            {
                world.Step();
                if (horde.NextWake == last) continue;
                wakes.Add(last - grunt.SpawnTick);
                last = horde.NextWake;
            }

            Check.That(wakes.All(tick => tick % factor == 0), $"{rate}: the module wakes on the frames of the soldier ({string.Join(",", wakes)}).");
            var frames = wakes.Select(tick => tick / factor).ToList();
            if (reference is null) reference = frames;
            else Check.That(frames.SequenceEqual(reference), $"{rate}: the module wakes in the frames it wakes in at 30 Hz ({string.Join(",", frames)} against {string.Join(",", reference)}).");
        }
    }
}
