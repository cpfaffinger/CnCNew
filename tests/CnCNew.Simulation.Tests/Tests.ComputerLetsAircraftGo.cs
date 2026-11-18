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
    /// <summary>Stands in for the scripts: every unit hunts (<c>AI_HUNT</c>) or not, every team has the attitude.</summary>
    private sealed class ScriptStandIn(bool hunting, Attitude attitude) : IAcquisitionAdvisor
    {
        public bool TryAttackPriority(Entity attacker, Entity target, out int priority) { priority = 0; return false; }
        public Attitude AttitudeOf(Entity entity) => attitude;
        public bool IsHunting(Entity entity) => hunting;
    }

    /// <summary>
    /// Real data (#460): <c>AIAttackApproachTargetState::onEnter</c> and <c>updateInternal</c> fail the approach of a computer player's unit to a victim that is an aircraft in the air
    /// (<c>KINDOF_AIRCRAFT</c>, <c>isAirborneTarget</c>) unless the unit hunts (<c>AI_HUNT</c>) - "Don't chase aircraft, unless we're hunting". A computer player's Missile Defender
    /// beside a hovering enemy Chinook beyond its missiles' reach but within its vision times the computer's factor stays and does not fire, idle or guarding its place with an
    /// aggressive attitude; a hunting one walks into range and fires; a human player's stays. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealComputerLetsAnAircraftInTheAirGo(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var (computer, hunting, guarding) in new[] { (true, false, false), (true, true, false), (true, false, true), (false, false, false) })
            {
                var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", IsComputer: computer), new PlayerConfiguration(new PlayerId(2), "West"));
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 5, players, "real-let-aircraft-go", fingerprint), catalog);
                // An aggressive guard's inner range (150 x 1.1 x 1.5) reaches the Chinook; a normal one's would not.
                if (hunting || guarding) world.AcquisitionAdvisor = new ScriptStandIn(hunting, guarding ? Attitude.Aggressive : Attitude.Normal);
                ulong sequence = 0;
                SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
                var who = computer ? hunting ? "hunting computer" : guarding ? "aggressive guarding computer" : "computer" : "human";
                Check.That(Send(next => new SpawnCommand(new PlayerId(2), next, world.Tick, "AmericaVehicleChinook", new PlayerId(2), Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, $"{rate}: a Chinook spawns.");
                world.Step();
                var chinook = world.Entities.Single(value => value.Template.Name == "AmericaVehicleChinook");
                for (var tick = 0; tick < world.Frames(30 * 5) && !world.IsAirborneTarget(chinook); tick++) world.Step();
                System.Console.WriteLine($"       {rate}: chinook airborne {world.IsAirborneTarget(chinook)} z {chinook.Z.ToDecimal():0.#} r {chinook.Template.BoundingCircleRadius.ToDecimal():0.#}");
                Check.That(world.IsAirborneTarget(chinook), $"{rate}: the Chinook is in the air.");
                // Between the bounding circles 190: beyond the missiles' 175, within 150 x 1.333 of vision.
                var gap = Fixed.FromInt(190) + chinook.Template.BoundingCircleRadius;
                Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaInfantryMissileDefender", new PlayerId(1), chinook.X + gap, chinook.Y, Fixed.Zero, Fixed.Zero)).Accepted, $"{rate}: a Missile Defender spawns.");
                world.Step();
                var defender = world.Entities.Single(value => value.Template.Name == "AmericaInfantryMissileDefender");
                // A guard of its own place (AI_GUARD): its attack's approach fails as well.
                if (guarding) Check.That(Send(next => new GuardCommand(new PlayerId(1), next, world.Tick, [defender.Id], defender.X, defender.Y, EntityId.Invalid, GuardMode.Normal)).Accepted, $"{rate}: the guard order is taken.");
                var (startX, startY) = (defender.X, defender.Y);
                string Where() => $"{who} {defender.Id.Value}@({defender.X.ToDecimal():0.#}, {defender.Y.ToDecimal():0.#}) moving {defender.HasMoveTarget}, chinook ({chinook.X.ToDecimal():0.#}, {chinook.Y.ToDecimal():0.#}, {chinook.Z.ToDecimal():0.#})";
                var fired = false;
                var walked = false;
                for (var tick = 0; tick < world.Frames(30 * 15) && !fired; tick++)
                {
                    foreach (var simEvent in world.Step().Events)
                        if (simEvent.Kind == SimEventKind.WeaponFired && simEvent.Entity == defender.Id) fired = true;
                    // The guard order's move to the place the unit stands on is no walk: a step away from it is.
                    walked |= (defender.X - startX).Abs() + (defender.Y - startY).Abs() > Fixed.One;
                }
                System.Console.WriteLine($"       {rate}: {Where()}, walked {walked}, fired {fired}");
                if (computer && hunting)
                    Check.That(walked && fired, $"{rate}: the hunting computer's Missile Defender walks into range of the Chinook and fires ({Where()}).");
                else
                    Check.That(!walked && !fired, $"{rate}: the {who}'s Missile Defender lets the Chinook in the air go ({Where()}).");
            }
        }
    }
}
