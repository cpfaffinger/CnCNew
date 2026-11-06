using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// F-40: an attack on a victim that becomes stealthed and undetected. The original tests <c>cannotPossiblyAttackObject</c> (<c>getAbleToAttackSpecificObject(ATTACK_CONTINUED_TARGET)</c>,
/// whose stealth rule is <c>WeaponSet.cpp</c>'s "If an object is stealthed and hasn't been detected yet, then it is not a valid target to fire on") as a condition of the aim and the fire
/// state of <c>AttackStateMachine</c> in every frame the attacker's AI runs, and <c>TurretAIAimTurretState::update</c> asks the same; the approach and the pursuit ask it themselves because
/// they have no condition list. So the attack ends in the first frame the attacker sees the victim hidden, while it aims, reloads or winds up - not at the next approach.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> StealthEndFiles() => new Dictionary<string, string>(TurretAttackFiles())
    {
        ["Data/INI/Object/StealthEndFixture.ini"] = """
        Object StealthEndTurret
          KindOf = SELECTABLE VEHICLE
          VisionRange = 200.0
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TurretGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            Turret
              TurretTurnRate = 180
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        Object StealthEndPlain
          KindOf = SELECTABLE VEHICLE
          VisionRange = 200.0
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TurretGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
          End
        End
        """,
    };

    /// <summary>
    /// The victim is stealthed before frame <paramref name="stealthFrame"/> begins (never, when negative). Returns the frames of the attacker's shots, the first frame from
    /// <paramref name="stealthFrame"/> on at whose end the unit attacks nothing and its turret aims at nothing, and how the fire order of that frame was answered.
    /// </summary>
    private static (List<long> Shots, long Over, CommandRejection? OrderInStealthFrame) StealthEndRun(string template, bool ordered, long stealthFrame, long frames)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: StealthEndFiles());
        var terrain = NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        world.RevealMapPermanently(new PlayerId(1), true);
        // An attack a player orders is a stream of fire orders, one per logic frame of the original (StandingOrders); an acquired one is the unit's own.
        world.SelfAcquisition = !ordered;
        feeder.Spawn(1, template, owner: 1, x: 20, y: 20);
        feeder.Spawn(2, "RunnerTarget", owner: 2, x: 20, y: 100);
        world.Step();
        var unit = Slotted(world, template);
        var victim = Slotted(world, "RunnerTarget");
        var shots = new List<long>();
        var over = -1L;
        CommandRejection? answer = null;
        while (world.InFrames(world.Tick) < frames)
        {
            var frame = world.InFrames(world.Tick);
            if (world.Time.IsOriginalFrameBoundary(world.Tick))
            {
                if (frame == stealthFrame) world.SetStatus(victim.Id, 1UL << ObjectStatusBits.Index("STEALTHED"));
                if (ordered)
                {
                    var result = feeder.FireWeapon(1, unit.Id.Value, victim.Id.Value);
                    if (frame == stealthFrame) answer = result.Accepted ? 0 : result.Reason;
                }
            }

            foreach (var value in world.Step().Events)
                if (value.Kind == SimEventKind.WeaponFired && value.Entity == unit.Id) shots.Add(frame);
            if (stealthFrame >= 0 && frame >= stealthFrame && over < 0 && world.Time.IsOriginalFrameBoundary(world.Tick)
                && !world.AcquiredVictimOf(unit).IsValid && !world.AttackTargetOf(unit).IsValid)
                over = frame;
        }

        return (shots, over, answer);
    }

    public static void StealthedVictimEndsTheAttackInItsFrame()
    {
        const long Frames = 70;
        foreach (var template in new[] { "StealthEndTurret", "StealthEndPlain" })
        foreach (var ordered in new[] { false, true })
        {
            var kind = $"{template}, {(ordered ? "ordered" : "acquired")}";
            var (free, _, _) = StealthEndRun(template, ordered, -1, Frames);
            Check.That(free.Count(frame => frame >= 40) >= 3, $"{kind}: without stealth the unit keeps firing ({string.Join(",", free)}).");
            var dueInWindow = 0;
            for (var stealthFrame = 40L; stealthFrame < 50; stealthFrame++)
            {
                var (shots, over, answer) = StealthEndRun(template, ordered, stealthFrame, Frames);
                var expected = free.Where(frame => frame < stealthFrame).ToList();
                if (free.Contains(stealthFrame)) dueInWindow++;
                Check.That(shots.SequenceEqual(expected),
                    $"{kind}, stealthed before frame {stealthFrame}: every shot before that frame and none from it on, also one that is due in it ({string.Join(",", shots)}; free run {string.Join(",", free)}).");
                Check.That(over == stealthFrame, $"{kind}, stealthed before frame {stealthFrame}: the attack and the turret's aim are over at the end of that frame (over at {over}).");
                // The pre-check names the weapon's reload before the sight; the order of a frame a shot is due in is refused because the victim is not visible.
                if (ordered) Check.That(answer is { } refused && refused != 0 && (!free.Contains(stealthFrame) || refused == CommandRejection.TargetNotVisible),
                    $"{kind}: the fire order of frame {stealthFrame} is refused, as not visible when a shot was due ({answer}).");
            }

            Check.That(dueInWindow >= 1, $"{kind}: one of the ten frames is a frame a shot was due in ({dueInWindow}).");
        }
    }
}
