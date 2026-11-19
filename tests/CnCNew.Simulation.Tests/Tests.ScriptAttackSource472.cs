using CnCNew.Data.World;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Mission;

static partial class Tests
{
    /// <summary>
    /// Real data (#471, part 2): a script's attack on a named object does not care about relationships - <c>NAMED_ATTACK_NAMED</c> is a force-attack (<c>doNamedAttack</c>:
    /// <c>aiForceAttackObject</c>), and the plain <c>aiAttackObject</c> of <c>TEAM_ATTACK_NAMED</c> comes from a script, which <c>WeaponSet::getAbleToAttackSpecificObject</c> lets attack a
    /// non-enemy ("If scripts or AI order attacks, they don't care about relationships"). A Gattling tank is ordered at a Ranger of its own player: it fires at it and kills it, for a computer
    /// player and for a human one (the ordinary attack order of before ended at once). At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealScriptAttackIgnoresRelationships(string directory)
    {
        foreach (var (catalog, fingerprint) in RealCatalogsAtEveryRate(directory))
        {
            var rate = catalog.Rate;
            foreach (var (computer, action) in new[] { (true, "NAMED_ATTACK_NAMED"), (true, "TEAM_ATTACK_NAMED"), (false, "NAMED_ATTACK_NAMED"), (false, "TEAM_ATTACK_NAMED") })
            {
                // The tank on its player's own team, the Ranger of the same player on another of its teams.
                var (tankTeam, rangerTeam) = computer ? ("teamB", "Raiders") : ("teamA", "Squad");
                MapScript[] scripts =
                [
                    Scr.Script("Wait", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("wait"), Scr.Real(1m))], oneShot: true),
                    Scr.Script("Attack", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("wait")))],
                        [action == "NAMED_ATTACK_NAMED" ? Scr.Do(action, Scr.Unit("Tank"), Scr.Unit("Victim")) : Scr.Do(action, Scr.Team(tankTeam), Scr.Unit("Victim"))], oneShot: true),
                ];
                MapObject[] objects = [Placed("ChinaTankGattling", 150m, 200m, tankTeam, "Tank"), Placed("AmericaInfantryRanger", 260m, 200m, rangerTeam, "Victim")];
                var (map, _, terrain) = MissionMap(scripts, objects: objects, rate: rate);
                var session = MissionSession.Start(MissionOptions.Standard, catalog, fingerprint, terrain, map);
                var world = session.World;
                var tank = world.Entities.Single(value => value.Template.Name == "ChinaTankGattling");
                var ranger = world.Entities.Single(value => value.Template.Name == "AmericaInfantryRanger");
                var who = $"{(computer ? "computer's" : "human's")} {action}";
                var shots = 0;
                for (var tick = 0; tick < world.Frames(30 * 20) && !ranger.EffectivelyDead && !session.IsOver; tick++)
                    foreach (var simEvent in session.Step().Events)
                        if (simEvent.Kind == SimEventKind.WeaponFired && simEvent.Entity == tank.Id && simEvent.Other == ranger.Id) shots++;
                var where = $"{rate}: {who}: tank owner {tank.Owner.Value}, Ranger owner {ranger.Owner.Value} dead {ranger.EffectivelyDead}, shots {shots}, victim {world.AcquiredVictimOf(tank).Value}";
                System.Console.WriteLine($"       {where}");
                Check.That(tank.Owner == ranger.Owner, $"{where}: setup - the Ranger is the tank's player's own.");
                Check.That(shots > 0 && ranger.EffectivelyDead, $"{where}: the script's attack goes on at a victim that is no enemy, and kills it.");
            }
        }
    }

    /// <summary>
    /// Real data (#472): a script's attack goes into the fog - "Fog no longer prevents attacking at all" (<c>WeaponSet::getAbleToAttackSpecificObject</c>); only a player's order is held to
    /// what the player sees. A human player's Gattling tank is ordered at an enemy Ranger far beyond what the player sees (<c>NAMED_ATTACK_NAMED</c>, <c>TEAM_ATTACK_NAMED</c>,
    /// <c>NAMED_ATTACK_TEAM</c>): it drives there and kills it (the named attacks were refused as a target not visible and did nothing). At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealScriptAttackGoesIntoTheFog(string directory)
    {
        foreach (var (catalog, fingerprint) in RealCatalogsAtEveryRate(directory))
        {
            var rate = catalog.Rate;
            foreach (var action in new[] { "NAMED_ATTACK_NAMED", "TEAM_ATTACK_NAMED", "NAMED_ATTACK_TEAM" })
            {
                MapScript[] scripts =
                [
                    Scr.Script("Wait", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("wait"), Scr.Real(1m))], oneShot: true),
                    Scr.Script("Attack", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("wait")))],
                        [action switch
                        {
                            "NAMED_ATTACK_NAMED" => Scr.Do(action, Scr.Unit("Tank"), Scr.Unit("Victim")),
                            "TEAM_ATTACK_NAMED" => Scr.Do(action, Scr.Team("teamA"), Scr.Unit("Victim")),
                            _ => Scr.Do(action, Scr.Unit("Tank"), Scr.Team("teamB")),
                        }], oneShot: true),
                ];
                MapObject[] objects = [Placed("ChinaTankGattling", 60m, 200m, "teamA", "Tank"), Placed("AmericaInfantryRanger", 540m, 200m, "teamB", "Victim")];
                var (map, _, terrain) = MissionMap(scripts, objects: objects, rate: rate);
                var session = MissionSession.Start(MissionOptions.Standard, catalog, fingerprint, terrain, map);
                var world = session.World;
                var tank = world.Entities.Single(value => value.Template.Name == "ChinaTankGattling");
                var ranger = world.Entities.Single(value => value.Template.Name == "AmericaInfantryRanger");
                var armed = false;
                var ordered = false;
                var unseenAtOrder = false;
                var shots = 0;
                for (var tick = 0; tick < world.Frames(30 * 60) && !ranger.EffectivelyDead && !session.IsOver; tick++)
                {
                    foreach (var simEvent in session.Step().Events)
                        if (simEvent.Kind == SimEventKind.WeaponFired && simEvent.Entity == tank.Id && simEvent.Other == ranger.Id) shots++;
                    armed |= session.Engine.CounterValue("wait") > 0;
                    if (armed && !ordered && session.Engine.CounterValue("wait") <= 0) (ordered, unseenAtOrder) = (true, !world.CanPlayerSee(tank.Owner, ranger));
                }
                var where = $"{rate}: {action}: tank at ({tank.X.ToDecimal():0.#}, {tank.Y.ToDecimal():0.#}), Ranger dead {ranger.EffectivelyDead}, shots {shots}, unseen at the order {unseenAtOrder}";
                System.Console.WriteLine($"       {where}");
                Check.That(ordered && unseenAtOrder, $"{where}: setup - the human player does not see the Ranger when the script orders the attack.");
                Check.That(shots > 0 && ranger.EffectivelyDead, $"{where}: the script's attack drives into the fog and kills the Ranger.");
            }
        }
    }
}
