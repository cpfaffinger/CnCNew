using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static (TemplateCatalog Catalog, string Fingerprint)[] RealCatalogsAtEveryRate(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        return [..new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 }
            .Select(rate => (TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product), fingerprint))];
    }

    /// <summary>
    /// Real data (#471): a script's attack on a team (<c>NAMED_ATTACK_TEAM</c>: <c>doNamedAttackTeam</c> -> <c>aiAttackTeam</c>; <c>TEAM_ATTACK_TEAM</c>: <c>doAttack</c> -> <c>groupAttackTeam</c>)
    /// is the original's <c>AIAttackSquadState</c>: the attacker chooses a member and attacks it with the ordinary attack, whose approach fails for a computer player's unit that would have to
    /// walk to an aircraft in the air (<c>AIAttackApproachTargetState</c>, "Don't chase aircraft, unless we're hunting", #460). The script orders a Missile Defender at the team of a hovering
    /// Chinook beyond its missiles' reach: a computer player's stays where it is and fires nothing (the script's attack-move of before marched it at the Chinook); a human player's walks into
    /// reach and fires. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealScriptTeamAttackLetsAnAircraftInTheAirGo(string directory)
    {
        foreach (var (catalog, fingerprint) in RealCatalogsAtEveryRate(directory))
        {
            var rate = catalog.Rate;
            Check.That(catalog.TryGet("AmericaVehicleChinook", out var chinookTemplate), "The Chinook is a template of the data.");
            foreach (var (computer, action) in new[] { (true, "NAMED_ATTACK_TEAM"), (true, "TEAM_ATTACK_TEAM"), (false, "NAMED_ATTACK_TEAM") })
            {
                // PlayerA is the human, PlayerB the computer: the Missile Defender belongs to the computer's team or to the human's, the Chinook to the other side's team.
                var (gunnerTeam, heliTeam) = computer ? ("teamB", "teamA") : ("teamA", "teamB");
                // Between the bounding circles 190: beyond the missiles' 175, within 150 x 1.333 of vision. The Chinook hovers over the waypoint Mid (250, 200) once it has flown there.
                var gap = 190m + chinookTemplate.BoundingCircleRadius.ToDecimal();
                MapScript[] scripts =
                [
                    Scr.Script("Lift", [Scr.And(Scr.If("CONDITION_TRUE"))],
                        [Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Heli"), Scr.Waypoint("Mid")), Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("wait"), Scr.Real(8m))], oneShot: true),
                    Scr.Script("Attack", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("wait")))],
                        [action == "NAMED_ATTACK_TEAM" ? Scr.Do(action, Scr.Unit("Gunner"), Scr.Team(heliTeam)) : Scr.Do(action, Scr.Team(gunnerTeam), Scr.Team(heliTeam))], oneShot: true),
                ];
                MapObject[] objects = [Placed("AmericaVehicleChinook", 230m, 200m, heliTeam, "Heli"), Placed("AmericaInfantryMissileDefender", 250m + gap, 200m, gunnerTeam, "Gunner")];
                var (map, _, terrain) = MissionMap(scripts, objects: objects, rate: rate);
                var session = MissionSession.Start(MissionOptions.Standard, catalog, fingerprint, terrain, map);
                var world = session.World;
                var gunner = world.Entities.Single(value => value.Template.Name == "AmericaInfantryMissileDefender");
                var heli = world.Entities.Single(value => value.Template.Name == "AmericaVehicleChinook");
                var who = computer ? $"computer's ({action})" : $"human's ({action})";
                var armed = false;
                var ordered = false;
                var (startX, startY) = (gunner.X, gunner.Y);
                var fired = false;
                var walked = false;
                var airborneAtOrder = false;
                for (var tick = 0; tick < world.Frames(30 * 25) && !fired && !session.IsOver; tick++)
                {
                    foreach (var simEvent in session.Step().Events)
                        if (simEvent.Kind == SimEventKind.WeaponFired && simEvent.Entity == gunner.Id) fired = true;
                    armed |= session.Engine.CounterValue("wait") > 0;
                    if (armed && !ordered && session.Engine.CounterValue("wait") <= 0) (ordered, airborneAtOrder) = (true, world.IsAirborneTarget(heli));
                    if (!ordered) (startX, startY) = (gunner.X, gunner.Y);
                    walked |= ordered && (gunner.X - startX).Abs() + (gunner.Y - startY).Abs() > Fixed.FromInt(5);
                }
                string Where() => $"{who} gunner at ({gunner.X.ToDecimal():0.#}, {gunner.Y.ToDecimal():0.#}) moving {gunner.HasMoveTarget} victim {world.AcquiredVictimOf(gunner).Value}, "
                                  + $"Chinook at ({heli.X.ToDecimal():0.#}, {heli.Y.ToDecimal():0.#}, {heli.Z.ToDecimal():0.#}) airborne {world.IsAirborneTarget(heli)}, walked {walked}, fired {fired}";
                System.Console.WriteLine($"       {rate}: {Where()}");
                Check.That(world.IsAirborneTarget(heli) && !heli.HasMoveTarget, $"{rate}: setup - the Chinook hovers ({Where()}).");
                Check.That(ordered && airborneAtOrder, $"{rate}: setup - the script's order came while the Chinook was in the air ({Where()}).");
                if (computer)
                    Check.That(!walked && !fired && !gunner.HasMoveTarget && !world.AcquiredVictimOf(gunner).IsValid, $"{rate}: the {who} Missile Defender lets the Chinook in the air go ({Where()}).");
                else
                    Check.That(walked && fired, $"{rate}: the {who} Missile Defender walks into reach of the Chinook and fires ({Where()}).");
            }
        }
    }

    /// <summary>
    /// Real data (#471): <c>AIAttackSquadState</c> goes from member to member - when the attack on the chosen member is over, the attacker chooses again (on normal the closest member,
    /// <c>chooseVictim</c>) and attacks that one, until nobody of the team is left; then it is idle. A computer player's Gattling tank ordered by <c>NAMED_ATTACK_TEAM</c> at a team of two
    /// Rangers kills the closer one, then the other, each with an attack order of its own (not an attack-move, which the script gave before), and then has no order. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealScriptTeamAttackGoesFromMemberToMember(string directory)
    {
        foreach (var (catalog, fingerprint) in RealCatalogsAtEveryRate(directory))
        {
            var rate = catalog.Rate;
            MapScript[] scripts =
            [
                // A second after the start, when the computer player sees the Rangers.
                Scr.Script("Wait", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("wait"), Scr.Real(1m))], oneShot: true),
                Scr.Script("Attack", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("wait")))], [Scr.Do("NAMED_ATTACK_TEAM", Scr.Unit("Tank"), Scr.Team("teamA"))], oneShot: true),
            ];
            MapObject[] objects =
            [
                Placed("ChinaTankGattling", 150m, 200m, "teamB", "Tank"),
                Placed("AmericaInfantryRanger", 330m, 170m, "teamA", "Near"),
                Placed("AmericaInfantryRanger", 380m, 260m, "teamA", "Far"),
            ];
            var (map, _, terrain) = MissionMap(scripts, objects: objects, rate: rate);
            var session = MissionSession.Start(MissionOptions.Standard, catalog, fingerprint, terrain, map);
            var world = session.World;
            var tank = world.Entities.Single(value => value.Template.Name == "ChinaTankGattling");
            var rangers = world.Entities.Where(value => value.Template.Name == "AmericaInfantryRanger").OrderBy(value => value.X.Raw).ToArray();
            var (near, far) = (rangers[0], rangers[1]);
            var pursuits = new List<(long Tick, uint Victim)>();
            long nearShot = -1, farShot = -1, nearDead = -1, farDead = -1;
            var attackMoves = new List<long>();
            session.CommandObserver = command =>
            {
                if (command is CnCNew.Simulation.Commands.PursueCommand pursue && pursue.Unit == tank.Id) pursuits.Add((world.Tick, pursue.Victim.Value));
                if (command is CnCNew.Simulation.Commands.AttackMoveCommand move && move.Unit == tank.Id) attackMoves.Add(world.Tick);
            };
            for (var tick = 0; tick < world.Frames(30 * 60) && (nearDead < 0 || farDead < 0) && !session.IsOver; tick++)
            {
                foreach (var simEvent in session.Step().Events)
                {
                    if (simEvent.Kind != SimEventKind.WeaponFired || simEvent.Entity != tank.Id) continue;
                    if (simEvent.Other == near.Id && nearShot < 0) nearShot = world.Tick;
                    if (simEvent.Other == far.Id && farShot < 0) farShot = world.Tick;
                }
                if (nearDead < 0 && near.EffectivelyDead) nearDead = world.Tick;
                if (farDead < 0 && far.EffectivelyDead) farDead = world.Tick;
            }
            // A few frames more: the attacker sees that nobody is left and is idle.
            for (var tick = 0; tick < world.Frames(30 * 2) && !session.IsOver; tick++) session.Step();
            string Where() => $"{rate}: tank at ({tank.X.ToDecimal():0.#}, {tank.Y.ToDecimal():0.#}) alive {!tank.EffectivelyDead} moving {tank.HasMoveTarget} victim {world.AcquiredVictimOf(tank).Value}, "
                              + $"near {near.Id.Value} shot {nearShot} dead {nearDead}, far {far.Id.Value} shot {farShot} dead {farDead}, pursuits [{string.Join(", ", pursuits.Select(p => $"{p.Victim}@{p.Tick}"))}], attack-moves [{string.Join(", ", attackMoves)}]";
            System.Console.WriteLine($"       {Where()}");
            Check.That(!tank.EffectivelyDead && nearDead >= 0 && farDead >= 0, $"{Where()}: the Gattling tank kills both Rangers.");
            Check.That(nearShot >= 0 && farShot > nearShot && farDead > nearDead, $"{Where()}: it attacks the closer Ranger first, then the other.");
            Check.That(attackMoves.Count == 0 && pursuits.Count >= 2 && pursuits[0].Victim == near.Id.Value && pursuits.Any(p => p.Victim == far.Id.Value && p.Tick >= nearDead),
                $"{Where()}: each Ranger is the victim of an attack order of its own, the second given when the first is dead.");
            Check.That(!world.AcquiredVictimOf(tank).IsValid && !tank.HasMoveTarget, $"{Where()}: with nobody of the team left the Gattling tank is idle.");
        }
    }
}
