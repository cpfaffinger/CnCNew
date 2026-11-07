using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-40's rest (<c>WeaponSet::getAbleToAttackSpecificObject</c>, <c>AIStates.cpp</c>): a forced attack is not kept off a stealthed victim of the attacker's own player
/// (<c>sameOwnerForceAttack</c>) nor off a <c>DISGUISER</c> in disguise; and a unit that attacks with a weapon of a continue range ignores stealth only until the attack's first shot
/// (<c>AIAttackState::onEnter</c> sets <c>OBJECT_STATUS_IGNORING_STEALTH</c>, <c>AIAttackFireWeaponState::update</c> clears it before it looks for the next victim).
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ForcedStealthFiles() => new Dictionary<string, string>(StealthEndFiles())
    {
        ["Data/INI/Object/ForcedStealthFixture.ini"] = """
        Object ForcedStealthTruck
          KindOf = SELECTABLE VEHICLE DISGUISER
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>Shots of a player's unit that is given a fire order in every logic frame of the original at a victim stealthed (and disguised) before frame 10, and the answers.</summary>
    private static (int Shots, int Accepted) ForcedStealthRun(string victimTemplate, int victimOwner, bool disguised, FireOrigin origin)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: ForcedStealthFiles());
        var terrain = NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        world.RevealMapPermanently(new PlayerId(1), true);
        world.SelfAcquisition = false;
        feeder.Spawn(1, "StealthEndPlain", owner: 1, x: 20, y: 20);
        feeder.Spawn(victimOwner, victimTemplate, owner: victimOwner, x: 20, y: 100);
        world.Step();
        var unit = Slotted(world, "StealthEndPlain");
        var victim = Slotted(world, victimTemplate);
        var hidden = 1UL << ObjectStatusBits.Index("STEALTHED");
        if (disguised) hidden |= 1UL << ObjectStatusBits.Index("DISGUISED");
        world.SetStatus(victim.Id, hidden);
        var (shots, accepted) = (0, 0);
        while (world.InFrames(world.Tick) < 70)
        {
            if (world.Time.IsOriginalFrameBoundary(world.Tick) && feeder.FireWeaponSlot(1, unit.Id.Value, victim.Id.Value, WeaponSlot.Best, origin).Accepted) accepted++;
            foreach (var value in world.Step().Events)
                if (value.Kind == SimEventKind.WeaponFired && value.Entity == unit.Id) shots++;
        }
        return (shots, accepted);
    }

    public static void ForcedAttackIgnoresStealthOfOwnAndDisguised()
    {
        // The own player's stealthed unit: the force-fire of a player goes on (sameOwnerForceAttack), as on the same unit seen.
        var own = ForcedStealthRun("RunnerTarget", 1, disguised: false, FireOrigin.Player);
        Check.That(own.Shots >= 3, $"A player's force-fire at its own stealthed unit goes on firing ({own.Shots} shots, {own.Accepted} orders accepted).");

        // A disguised truck of the enemy that looks like the player's own: the player's force-fire is accepted and fires; the order of a computer player is not (no force).
        var player = ForcedStealthRun("ForcedStealthTruck", 2, disguised: true, FireOrigin.Player);
        Check.That(player.Accepted > 0 && player.Shots >= 3, $"A player's force-fire at a disguised truck is accepted and fires ({player.Shots} shots, {player.Accepted} orders accepted).");
        var computer = ForcedStealthRun("ForcedStealthTruck", 2, disguised: true, FireOrigin.Ai);
        Check.That(computer.Accepted == 0 && computer.Shots == 0, $"A computer player's order at the disguised truck is refused ({computer.Shots} shots, {computer.Accepted} orders accepted).");

        // A stealthed enemy that is no disguiser stays out of reach of the force-fire too.
        var enemy = ForcedStealthRun("RunnerTarget", 2, disguised: false, FireOrigin.Player);
        Check.That(enemy.Shots == 0, $"A stealthed enemy is no victim of the force-fire ({enemy.Shots} shots).");
    }

    /// <summary>
    /// The bored dozer's continue range after its first shot: the next mine of the same player near the first is taken at once when it is seen (<see cref="DozerGoesOnToTheNextMine"/>),
    /// but not when it is stealthed and undetected - and the bored search does not find it either (<c>findMine</c> asks <c>PossibleToAttack</c> without <c>IGNORING_STEALTH</c>). The same
    /// frames at 30, 60 and 120 Hz.
    /// </summary>
    public static void ContinueRangeSeesStealthAfterTheFirstShot()
    {
        (long First, long Second)? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/MineClearing.ini"] = MineClearingIni });
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (_, _) => 0), "mines");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 2, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "McDozer", owner: 1, x: 300, y: 300).Accepted && feeder.Spawn(2, "McMine", owner: 2, x: 390, y: 300).Accepted
                && feeder.Spawn(2, "McMine", owner: 2, x: 400, y: 380).Accepted, $"{rate}: dozer and mines spawn.");
            world.Step();
            var dozer = world.Entities.Single(value => value.Template.Name == "McDozer");
            var first = world.Entities.Single(value => value.Template.Name == "McMine" && value.Y == Fixed.FromInt(300));
            var second = world.Entities.Single(value => value.Template.Name == "McMine" && value.Y == Fixed.FromInt(380));
            world.SetStatus(second.Id, 1UL << ObjectStatusBits.Index("STEALTHED"));
            long firstGone = -1, secondGone = -1;
            for (var step = 0; step < world.Frames(900) && secondGone < 0; step++)
            {
                world.Step();
                var frame = (world.Tick - dozer.SpawnTick + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
                if (firstGone < 0 && first.Body!.Health <= Fixed.Zero) firstGone = frame;
                if (secondGone < 0 && second.Body!.Health <= Fixed.Zero) secondGone = frame;
            }
            Check.That(firstGone > 0, $"{rate}: the bored dozer disarms the first mine ({firstGone}).");
            Check.That(secondGone < 0, $"{rate}: it does not go on to the stealthed second mine ({firstGone}, {secondGone}).");
            if (reference is null) reference = (firstGone, secondGone);
            else Check.That((firstGone, secondGone) == reference.Value, $"{rate}: the same frames as at 30 Hz (({firstGone}, {secondGone}) against {reference}).");
        }
    }
}
