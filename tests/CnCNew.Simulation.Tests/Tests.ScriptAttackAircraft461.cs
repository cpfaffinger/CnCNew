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
    /// <summary>
    /// Real data (#461): a script's attack on a named object (<c>NAMED_ATTACK_NAMED</c>: <c>aiForceAttackObject</c>; <c>TEAM_ATTACK_NAMED</c>: <c>groupAttackObject</c>) is the original's attack
    /// (#462), and its approach fails for a computer player's unit that would have to walk to an aircraft in the air (<c>AIAttackApproachTargetState</c>, "Don't chase aircraft, unless we're
    /// hunting", #460). The script orders a Missile Defender at a hovering Chinook beyond its missiles' reach: a computer player's stays where it is, fires nothing and is idle again; a
    /// human player's walks into reach and fires. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealScriptAttackLetsAnAircraftInTheAirGo(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            Check.That(catalog.TryGet("AmericaVehicleChinook", out var chinookTemplate), "The Chinook is a template of the data.");
            foreach (var (computer, action) in new[] { (true, "NAMED_ATTACK_NAMED"), (true, "TEAM_ATTACK_NAMED"), (false, "NAMED_ATTACK_NAMED") })
            {
                // PlayerA is the human, PlayerB the computer: the Missile Defender belongs to the computer's team or to the human's, the Chinook to the other side.
                var (gunnerTeam, heliTeam) = computer ? ("teamB", "teamA") : ("teamA", "teamB");
                // Between the bounding circles 190: beyond the missiles' 175, within 150 x 1.333 of vision. The Chinook hovers over the waypoint Mid (250, 200) once it has flown there.
                var gap = 190m + chinookTemplate.BoundingCircleRadius.ToDecimal();
                MapScript[] scripts =
                [
                    Scr.Script("Lift", [Scr.And(Scr.If("CONDITION_TRUE"))],
                        [Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Heli"), Scr.Waypoint("Mid")), Scr.Do("SET_MILLISECOND_TIMER", Scr.Counter("wait"), Scr.Real(8m))], oneShot: true),
                    Scr.Script("Attack", [Scr.And(Scr.If("TIMER_EXPIRED", Scr.Counter("wait")))],
                        [action == "NAMED_ATTACK_NAMED" ? Scr.Do(action, Scr.Unit("Gunner"), Scr.Unit("Heli")) : Scr.Do(action, Scr.Team(gunnerTeam), Scr.Unit("Heli"))], oneShot: true),
                ];
                MapObject[] objects = [Placed("AmericaVehicleChinook", 230m, 200m, heliTeam, "Heli"), Placed("AmericaInfantryMissileDefender", 250m + gap, 200m, gunnerTeam, "Gunner")];
                var (map, _, terrain) = MissionMap(scripts, objects: objects, rate: rate);
                var session = MissionSession.Start(MissionOptions.Standard, catalog, fingerprint, terrain, map);
                var world = session.World;
                var gunner = world.Entities.Single(value => value.Template.Name == "AmericaInfantryMissileDefender");
                var heli = world.Entities.Single(value => value.Template.Name == "AmericaVehicleChinook");
                var who = computer ? $"computer's ({action})" : $"human's ({action})";
                // The order is given when the timer the first script set has run out.
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
                {
                    Check.That(!walked && !fired && !gunner.HasMoveTarget && !world.AcquiredVictimOf(gunner).IsValid, $"{rate}: the {who} Missile Defender lets the Chinook in the air go ({Where()}).");
                }
                else
                {
                    Check.That(walked && fired, $"{rate}: the {who} Missile Defender walks into reach of the Chinook and fires ({Where()}).");
                }
            }
        }
    }
}
