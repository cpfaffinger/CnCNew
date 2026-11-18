using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data, #445 (the owner, playing release 0.0.1): a jet built at an airfield with a rally point flew there. In the original an airfield's rally point is the helicopters'
    /// (<c>ParkingPlaceBehavior::exitObjectViaDoor</c>: only a <c>PRODUCED_AT_HELIPAD</c> object moves to <c>m_heliRallyPoint</c>; a jet follows its exit path to its parking space and
    /// waits there). Checked on the American, the Air Force general's and the Chinese airfield: the jet stays parked, the helicopter of the same airfield flies to the rally point.
    /// </summary>
    public static void RealAirfieldRallyPointIsForHelicoptersOnly(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var (airfieldName, jetName, heliName) in new[]
                 {
                     ("AmericaAirfield", "AmericaJetRaptor", "AmericaVehicleComanche"),
                     ("AirF_AmericaAirfield", "AirF_AmericaJetRaptor", "AirF_AmericaVehicleComanche"),
                     ("ChinaAirfield", "ChinaJetMIG", "ChinaVehicleHelix"),
                 })
        {
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-airfield-rally", fingerprint), catalog);
            ulong sequence = 0;
            SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
            Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, airfieldName, new PlayerId(1), Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted,
                $"{airfieldName} spawns.");
            world.Step();
            Check.That(world.Submit(new GiveMoneyCommand(PlayerId.System, 1, world.Tick, new PlayerId(1), Fixed.FromInt(50000))).Accepted, "Money is granted.");
            world.Step();
            var airfield = world.Entities.First(value => value.Template.Name == airfieldName);
            var (rallyX, rallyY) = (Fixed.FromInt(2600), Fixed.FromInt(2200));
            Check.That(Send(next => new SetRallyPointCommand(new PlayerId(1), next, world.Tick, airfield.Id, rallyX, rallyY)).Accepted, "The rally point is set.");
            var jetQueued = Send(next => new QueueProductionCommand(new PlayerId(1), next, world.Tick, airfield.Id, jetName));
            Check.That(jetQueued.Accepted, $"{jetName} is queued at {airfieldName} ({jetQueued.Reason}).");
            var heliQueued = Send(next => new QueueProductionCommand(new PlayerId(1), next, world.Tick, airfield.Id, heliName));
            Check.That(heliQueued.Accepted, $"{heliName} is queued at {airfieldName} ({heliQueued.Reason}).");
            Entity? Find(string name) => world.Entities.FirstOrDefault(value => value.Template.Name == name);
            // A jet that is sent somewhere takes off for it (and comes home later when it idles there), so every tick from its exit on is looked at.
            var jetLeftItsSpace = false;
            void Step()
            {
                world.Step();
                if (Find(jetName) is { } made && made.Modules.OfType<JetAIUpdate>().First() is var madeAi
                    && (madeAi.AllowAirLoco || madeAi.Phase is JetAIUpdate.JetPhase.AwaitTakeoff or JetAIUpdate.JetPhase.TaxiToTakeoff or JetAIUpdate.JetPhase.PauseBeforeTakeoff
                        or JetAIUpdate.JetPhase.TakingOff))
                    jetLeftItsSpace = true;
            }
            for (var tick = 0; tick < world.Frames(30 * 120) && (Find(jetName) is null || Find(heliName) is null); tick++) Step();
            // Long enough for the helicopter to reach the rally point and for a jet to take off for it.
            for (var tick = 0; tick < world.Frames(30 * 40); tick++) Step();
            var jet = Find(jetName);
            var heli = Find(heliName);
            Check.That(jet is not null && heli is not null, $"{airfieldName} produced its jet and its helicopter.");
            static long Distance(Entity entity, Fixed x, Fixed y)
            {
                var dx = (entity.X - x).ToDecimal();
                var dy = (entity.Y - y).ToDecimal();
                return (long)System.Math.Sqrt((double)(dx * dx + dy * dy));
            }
            var jetAi = jet!.Modules.OfType<JetAIUpdate>().First();
            System.Console.WriteLine($"       {airfieldName}: {jetName} at ({jet.X.ToDecimal():0.#}, {jet.Y.ToDecimal():0.#}) {Distance(jet, airfield.X, airfield.Y)} from the airfield, "
                                     + $"airborne {jetAi.AllowAirLoco}; {heliName} {Distance(heli!, rallyX, rallyY)} from the rally point");
            Check.That(!jetLeftItsSpace && !jetAi.AllowAirLoco && !jet.HasMoveTarget, $"{jetName} waits on its parking space instead of flying to the rally point.");
            Check.That(Distance(jet, airfield.X, airfield.Y) < 200, $"{jetName} stands on the airfield ({Distance(jet, airfield.X, airfield.Y)} from its centre).");
            Check.That(Distance(heli!, rallyX, rallyY) < 80, $"{heliName} flew to the rally point ({Distance(heli!, rallyX, rallyY)} from it).");
        }
    }
}
