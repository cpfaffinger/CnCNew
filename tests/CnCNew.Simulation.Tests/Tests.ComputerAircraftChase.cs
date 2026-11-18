using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data (#456): <c>AIIdleState::update</c> gives a unit the victim <c>getNextMoodTarget</c> finds with <c>aiAttackObject(..., CMD_FROM_AI)</c>, and its
    /// <c>AIAttackApproachTargetState</c> fails out of range only for a human player's unit (<c>isAllowedToChase</c>) - nothing there excludes an aircraft. A computer player's Comanche
    /// that hovers idle near a Battlemaster beyond its weapons' reach (but within its vision times the computer's guard-outer factor) flies to its approach target
    /// (<c>Weapon::computeApproachTarget</c>, #451) and fires from there; the port's chase left every aircraft out, so it stood. A human player's Comanche looks no further than its weapons
    /// reach and stays. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealComputerHelicopterChasesItsOwnVictim(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var computer in new[] { true, false })
            {
                var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", IsComputer: computer), new PlayerConfiguration(new PlayerId(2), "West"));
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 5, players, "real-heli-chase", fingerprint), catalog);
                ulong sequence = 0;
                SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
                var who = computer ? "computer" : "human";
                Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaVehicleComanche", new PlayerId(1), Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, $"{rate}: a Comanche spawns.");
                // 280 between the centres: beyond the cannon's and the missiles' reach between the bounding circles, within 200 x 1.333 of vision and the circles.
                Check.That(Send(next => new SpawnCommand(new PlayerId(2), next, world.Tick, "ChinaTankBattleMaster", new PlayerId(2), Fixed.FromInt(2280), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, $"{rate}: a Battlemaster spawns.");
                world.Step();
                var heli = world.Entities.Single(value => value.Template.Name == "AmericaVehicleComanche");
                var victim = world.Entities.Single(value => value.Template.Name == "ChinaTankBattleMaster");
                var (startX, startY) = (heli.X, heli.Y);
                decimal Gap(Fixed x, Fixed y) => (decimal)System.Math.Sqrt((double)(((x - victim.X) * (x - victim.X) + (y - victim.Y) * (y - victim.Y)).ToDecimal()));
                string Where() => $"{who} {heli.Id.Value}@({heli.X.ToDecimal():0.#}, {heli.Y.ToDecimal():0.#}) -> {(heli.HasMoveTarget ? $"({heli.MoveTargetX.ToDecimal():0.#}, {heli.MoveTargetY.ToDecimal():0.#})" : "none")} d {Gap(heli.X, heli.Y):0.#}";

                var fired = false;
                var approached = false;
                decimal goal = 0m;
                for (var tick = 0; tick < world.Frames(30 * 20) && !fired; tick++)
                {
                    foreach (var simEvent in world.Step().Events)
                        if (simEvent.Kind == SimEventKind.WeaponFired && simEvent.Entity == heli.Id) fired = true;
                    if (!approached && heli.HasMoveTarget)
                    {
                        approached = true;
                        goal = Gap(heli.MoveTargetX, heli.MoveTargetY);
                    }
                }
                System.Console.WriteLine($"       {rate}: {Where()}, fired {fired}, approach goal at {goal:0.#}");
                if (computer)
                {
                    Check.That(approached && goal >= 150m && goal <= 215m, $"{rate}: the computer's Comanche flies to a place within its range of its victim, not at it ({Where()}, goal {goal:0.#}).");
                    Check.That(fired && Gap(heli.X, heli.Y) <= 215m, $"{rate}: the computer's Comanche fires at its victim from its approach ({Where()}).");
                }
                else
                {
                    Check.That(!approached && !fired && heli.X == startX && heli.Y == startY, $"{rate}: the human's Comanche stays where it hovers and does not fire ({Where()}).");
                }
            }
        }
    }
}
