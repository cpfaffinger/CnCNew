using System.Text.Json;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-139 (#136): the idle stamp of the dozer machine (<c>DozerPrimaryIdleState::update</c>, <c>m_idleTooLongTimestamp</c>) of a builder that dies in its frame's first tick after
/// its update. The 30 Hz look of that frame ran inside the update and wrote the stamp before the death; above 30 Hz the look's stamp is written where the frame ends, which a dead
/// builder never reaches.
/// </summary>
static partial class Tests
{
    private const string DyingDozerIni = """
        Object DyingDozer
          KindOf = SELECTABLE VEHICLE DOZER
          Locomotor = SET_NORMAL BoredLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
            BoredTime = 5000
            BoredRange = 150
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 5000
          End
        End
        """;

    /// <summary>When the dozer of <see cref="DozerKilledAfterItsUpdateKeepsTheStampOfItsFrame"/> is killed.</summary>
    private enum DozerDeath
    {
        /// <summary>Not at all: a living dozer, read where the frame ends.</summary>
        None,
        /// <summary>In the frame's first tick, before the updates (in the command phase): the frame's look never runs, at 30 Hz neither.</summary>
        BeforeUpdate,
        /// <summary>In the frame's first tick, after the updates (when target acquisition begins).</summary>
        AfterUpdate,
        /// <summary>After the frame's first tick, between two steps.</summary>
        AfterFirstTick,
    }

    /// <summary>Kills an object when a phase of the tick begins.</summary>
    private sealed class KillAtPhase(SimulationWorld world, EntityId target, TickPhase phase) : ITickPhaseObserver
    {
        public void PhaseStarted(TickPhase started)
        {
            if (started == phase) world.Kill(target);
        }

        public void PhaseEnded(TickPhase ended) { }
        public void UpdateStarted(CnCNew.Simulation.Modules.SimModule module) { }
        public void UpdateEnded(CnCNew.Simulation.Modules.SimModule module) { }
    }

    /// <summary>
    /// A dozer on its way (not idle, so every look stamps its frame) is killed in its 30th frame: after the update of the frame's first tick, or after that tick. Its stamp is that
    /// frame's at 30, 60 and 120 Hz - the frame a living dozer stamps; killed before that update it keeps the frame before's, and a dozer standing idle with no look due keeps the
    /// stamp it had. Before, above 30 Hz the dead dozer kept the stamp of the frame before.
    /// </summary>
    public static void DozerKilledAfterItsUpdateKeepsTheStampOfItsFrame()
    {
        // The stamp, counted in frames of the dozer's own grid; and the frame the dozer was killed in.
        (long Stamp, long Killed) Run(SimulationRate rate, bool moving, DozerDeath death)
        {
            var files = new Dictionary<string, string>(SyntheticFixture.Files)
            {
                ["Data/INI/Object/DozerBored.ini"] = DozerBoredIni,
                ["Data/INI/Object/DyingDozer.ini"] = DyingDozerIni,
            };
            var fixture = new SyntheticFixture(rate, files: files);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "DyingDozer", owner: 1, x: 100, y: 100).Accepted, "The dozer spawns.");
            world.Step();
            var dozer = world.Entities.Single(value => value.Template.Name == "DyingDozer");
            if (moving) Check.That(feeder.SetMoveTarget(1, dozer.Id.Value, 190, 100).Accepted, "The dozer is sent on.");
            var perFrame = world.Time.TicksPerOriginalFrame;
            // Before the first tick of the dozer's 30th frame.
            while ((world.Tick - dozer.SpawnTick) / perFrame < 30 || (world.Tick - dozer.SpawnTick) % perFrame != 0) world.Step();
            var killed = (world.Tick - dozer.SpawnTick) / perFrame;
            if (moving) Check.That(dozer.HasMoveTarget, $"{rate}: the dozer is still on its way.");
            if (death is DozerDeath.BeforeUpdate or DozerDeath.AfterUpdate)
                world.PhaseObserver = new KillAtPhase(world, dozer.Id, death == DozerDeath.BeforeUpdate ? TickPhase.Commands : TickPhase.Acquisition);
            world.Step();
            world.PhaseObserver = null;
            if (death == DozerDeath.AfterFirstTick) world.Kill(dozer.Id);
            if (death == DozerDeath.None)
            {
                // A living dozer is read at the end of the frame it would have been killed in: the stamp of that frame's look.
                while ((world.Tick - dozer.SpawnTick) % perFrame != 0) world.Step();
            }
            else
            {
                Check.That(dozer.EffectivelyDead, $"{rate}, {death}: the dozer is dead.");
                for (var step = 0; step < world.Frames(3); step++) world.Step();
                Check.That(world.FindEntity(dozer.Id) is not null, $"{rate}, {death}: the dying dozer is still there.");
            }
            return ((BoredSince(dozer) - dozer.SpawnTick) / perFrame, killed);
        }

        foreach (var moving in new[] { true, false })
        {
            var how = moving ? "on its way" : "idle";
            var reference = Run(SimulationRate.Hz30, moving, DozerDeath.None);
            Check.That(reference.Stamp == (moving ? reference.Killed : 0), $"30 Hz, {how}: the stamp of the living dozer ({reference.Stamp}, frame {reference.Killed}).");
            foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
                foreach (var death in new[] { DozerDeath.None, DozerDeath.BeforeUpdate, DozerDeath.AfterUpdate, DozerDeath.AfterFirstTick })
                {
                    // Killed before the frame's update, the dozer keeps the stamp of the frame before (on its way) or its first one (idle).
                    var expected = death == DozerDeath.BeforeUpdate && moving ? (reference.Stamp - 1, reference.Killed) : reference;
                    var result = Run(rate, moving, death);
                    Check.That(result == expected, $"{rate}, {how}, {death}: the stamp is the frame's of 30 Hz ({result} against {expected}).");
                }
        }
    }

    /// <summary>The idle stamp (<c>boredSince</c>) of a builder's dozer machine as its object describes it.</summary>
    private static long BoredSince(Entity builder)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            builder.Describe(new JsonStateWriter(json));
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return FindLong(document.RootElement, "boredSince") ?? throw new InvalidOperationException("The builder describes no boredSince.");
    }

    private static long? FindLong(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.Number) return property.Value.GetInt64();
                    if (FindLong(property.Value, name) is { } found) return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (FindLong(item, name) is { } found) return found;
                break;
        }
        return null;
    }
}
