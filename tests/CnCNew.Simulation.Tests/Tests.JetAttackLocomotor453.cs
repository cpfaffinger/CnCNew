using System.Collections.Generic;
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
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #453: a jet keeps its attack locomotor set for its whole attack and after it, until another set is chosen, on real data.
static partial class Tests
{
    /// <summary>
    /// #453: <c>JetAIUpdate::update</c> renews <c>m_attackLocoExpireFrame</c> in every frame <c>OBJECT_STATUS_IS_ATTACKING</c> holds and chooses <c>AttackLocomotorType</c> while it lies ahead;
    /// once it ran out it chooses the return set when the jet returns for ammunition and nothing otherwise, so the attack set stays until a take-off or landing state chooses the normal one
    /// or the jet taxis. The shipped Aurora (<c>SET_SUPERSONIC</c>, <c>ReturnForAmmoLocomotorType = SET_SLUGGISH</c>) built at an airfield and ordered to attack a tank: it flies supersonic from its
    /// take-off to its drop, faster than its normal locomotor's speed, returns sluggish and taxis home; ordered to attack again and given a move order on the way, it stays supersonic while it
    /// idles in the air and returns, until its landing begins. Before, it was supersonic only for <c>AttackLocomotorPersistTime</c> after its shot. At 30, 60 and 120 Hz, in the same frames of
    /// the original.
    /// </summary>
    public static void RealJetKeepsItsAttackLocomotorSet(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var timelines = new List<(SimulationRate Rate, string Line)>();
        foreach (var rate in TargetRates)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 5, players, "real-jet-attack-set", fingerprint), catalog);
            ulong sequence = 0;
            SubmitResult Send(System.Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
            var stride = world.Time.TicksPerOriginalFrame;

            Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaAirfield", new PlayerId(1), Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, "The airfield spawns.");
            Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, "AmericaStrategyCenter", new PlayerId(1), Fixed.FromInt(5400), Fixed.FromInt(2350), Fixed.Zero, Fixed.Zero)).Accepted, "The strategy center spawns; it sees both tanks.");
            Check.That(Send(next => new SpawnCommand(new PlayerId(2), next, world.Tick, "ChinaTankBattleMaster", new PlayerId(2), Fixed.FromInt(5400), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero)).Accepted, "The tank spawns.");
            Check.That(Send(next => new SpawnCommand(new PlayerId(2), next, world.Tick, "ChinaTankBattleMaster", new PlayerId(2), Fixed.FromInt(5400), Fixed.FromInt(2700), Fixed.Zero, Fixed.Zero)).Accepted, "The second tank spawns.");
            world.Step();
            Check.That(world.Submit(new GiveMoneyCommand(PlayerId.System, ++sequence, world.Tick, new PlayerId(1), Fixed.FromInt(50000))).Accepted, "Money is granted.");
            world.Step();
            var airfield = world.Entities.First(value => value.Template.Name == "AmericaAirfield");
            var tanks = world.Entities.Where(value => value.Template.Name == "ChinaTankBattleMaster").OrderBy(value => value.Id.Value).ToArray();
            var queued = Send(next => new QueueProductionCommand(new PlayerId(1), next, world.Tick, airfield.Id, "AmericaJetAurora"));
            Check.That(queued.Accepted, $"{rate}: the Aurora is queued ({queued.Reason}).");
            Entity? aurora = null;
            for (var tick = 0; tick < world.Frames(30 * 120) && aurora is null; tick++)
            {
                world.Step();
                aurora = world.Entities.FirstOrDefault(value => value.Template.Name == "AmericaJetAurora");
            }
            Check.That(aurora is not null, $"{rate}: the airfield built the Aurora.");
            var ai = aurora!.Modules.OfType<JetAIUpdate>().Single();
            // Frames of the Aurora's own grid: the one the tick just stepped lies in, and the one the next tick lies in (where an order given now acts).
            long Frame() => (world.Tick - 1 - aurora.SpawnTick) / stride;
            long Next() => (world.Tick - aurora.SpawnTick) / stride;
            void ToOwnFrameStart()
            {
                while (!world.Time.BeginsFrameOf(aurora, world.Tick)) world.Step();
            }
            var normalSpeed = aurora.Template.Locomotor!.Speed;
            var supersonic = aurora.Template.LocomotorSets["SET_SUPERSONIC"];
            Check.That(supersonic.Speed > normalSpeed, $"{rate}: the supersonic locomotor is the faster one.");
            for (var tick = 0; tick < world.Frames(30 * 60) && !(ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco); tick++) world.Step();
            Check.That(ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco && ai.CurrentLocomotorSetName == "SET_TAXIING", $"{rate}: the Aurora stands on its space ({ai.Phase}, {ai.CurrentLocomotorSetName}).");

            // Sent up first: an attack order given to a parked jet is stored as its move and the victim given up while it taxis (the client's standing orders give it again).
            long Launch()
            {
                Check.That(Send(next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, aurora.Id, Fixed.FromInt(2400), Fixed.FromInt(1500))).Accepted, $"{rate}: the Aurora is sent up.");
                for (var tick = 0; tick < world.Frames(30 * 60) && !(ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None); tick++) world.Step();
                Check.That(ai.AllowAirLoco && ai.Phase == JetAIUpdate.JetPhase.None && ai.CurrentLocomotorSetName == "SET_NORMAL", $"{rate}: the Aurora flies with its normal set ({ai.Phase}, {ai.CurrentLocomotorSetName}).");
                return Frame();
            }
            var takeoff = Launch();

            // What the client's standing order does on an attack run (StandingOrders.UpdateAttack): fire whenever the weapon may, otherwise fly at the victim again.
            void Attack(Entity tank)
            {
                if (world.ChooseWeaponInView(aurora, tank, FireOrigin.Player).CanFire)
                {
                    if (!world.IsShotPending(aurora, tank.Id)) Send(next => new FireWeaponCommand(new PlayerId(1), next, world.Tick, aurora.Id, tank.Id));
                }
                else if (!aurora.HasMoveTarget) Send(next => new PursueCommand(new PlayerId(1), next, world.Tick, aurora.Id, tank.Id, tank.X, tank.Y));
            }

            // The attack: supersonic from the order to the drop.
            ToOwnFrameStart();
            var attackOrdered = Next();
            var ordered = Send(next => new PursueCommand(new PlayerId(1), next, world.Tick, aurora.Id, tanks[0].Id, tanks[0].X, tanks[0].Y));
            Check.That(ordered.Accepted, $"{rate}: the attack is ordered ({ordered.Reason}).");
            long firstSupersonic = -1, drop = -1, returnStart = -1, sluggish = -1, landed = -1;
            var otherSetInRun = 0;
            var fastest = Fixed.Zero;
            var fired = aurora.LastFireTick;
            for (var tick = 0; tick < world.Frames(30 * 180) && landed < 0; tick++)
            {
                if (drop < 0) Attack(tanks[0]);
                else if (sluggish < 0 && !aurora.HasMoveTarget && world.AcquiredVictimOf(aurora).IsValid) Send(next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, aurora.Id, aurora.X, aurora.Y));
                world.Step();
                var set = ai.CurrentLocomotorSetName;
                if (firstSupersonic < 0 && set == "SET_SUPERSONIC") firstSupersonic = Frame();
                if (drop < 0 && aurora.LastFireTick != fired) drop = Frame();
                if (firstSupersonic >= 0 && drop < 0)
                {
                    if (set != "SET_SUPERSONIC") otherSetInRun++;
                    fastest = Fixed.Max(fastest, aurora.Speed);
                }
                if (drop >= 0 && returnStart < 0 && ai.Phase == JetAIUpdate.JetPhase.ReturningForLanding) returnStart = Frame();
                if (drop >= 0 && sluggish < 0 && set == "SET_SLUGGISH") sluggish = Frame();
                if (sluggish >= 0 && !ai.AllowAirLoco) landed = Frame();
            }
            Check.That(drop > attackOrdered, $"{rate}: the Aurora drops its bomb (frame {drop}, ordered at {attackOrdered}).");
            Check.That(firstSupersonic == attackOrdered, $"{rate}: its attack set begins in the frame of the attack order (frame {firstSupersonic}, ordered in {attackOrdered}).");
            Check.That(otherSetInRun == 0, $"{rate}: it flies supersonic in every tick from then to its drop ({otherSetInRun} ticks with another set).");
            Check.That(fastest > normalSpeed, $"{rate}: on its run it flies faster than its normal locomotor ({fastest} against {normalSpeed} per tick).");
            Check.That(returnStart > drop && sluggish >= returnStart && landed > sluggish, $"{rate}: out of ammunition it returns with its sluggish set and lands (frames {returnStart}, {sluggish}, {landed}).");
            Check.That(ai.CurrentLocomotorSetName == "SET_TAXIING", $"{rate}: on the ground it taxis ({ai.CurrentLocomotorSetName}).");

            // An attack given up in the air: the attack set stays until the landing begins.
            for (var tick = 0; tick < world.Frames(30 * 120) && !(ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco); tick++) world.Step();
            Check.That(ai.Phase == JetAIUpdate.JetPhase.None && !ai.AllowAirLoco, $"{rate}: the Aurora reloaded on its space ({ai.Phase}).");
            var secondTakeoff = Launch();
            var firedBefore = aurora.LastFireTick;
            Check.That(Send(next => new PursueCommand(new PlayerId(1), next, world.Tick, aurora.Id, tanks[1].Id, tanks[1].X, tanks[1].Y)).Accepted, $"{rate}: the second attack is ordered.");
            for (var tick = 0; tick < world.Frames(30); tick++) world.Step();
            Check.That(ai.CurrentLocomotorSetName == "SET_SUPERSONIC" && aurora.LastFireTick == firedBefore,
                $"{rate}: a second after the order the Aurora flies at the second tank supersonic, not yet fired ({ai.CurrentLocomotorSetName}).");
            ToOwnFrameStart();
            var aborted = Next();
            Check.That(Send(next => new SetMoveTargetCommand(new PlayerId(1), next, world.Tick, aurora.Id, Fixed.FromInt(2000), Fixed.FromInt(3000))).Accepted, $"{rate}: the attack is given up for a move.");
            long landing = -1;
            var otherSetBeforeLanding = 0;
            string? setAtLanding = null;
            for (var tick = 0; tick < world.Frames(30 * 120) && landing < 0; tick++)
            {
                world.Step();
                if (ai.Phase == JetAIUpdate.JetPhase.Landing)
                {
                    landing = Frame();
                    setAtLanding = ai.CurrentLocomotorSetName;
                }
                else if (ai.CurrentLocomotorSetName != "SET_SUPERSONIC") otherSetBeforeLanding++;
            }
            Check.That(aurora.LastFireTick == firedBefore, $"{rate}: the Aurora did not fire after its attack was given up.");
            Check.That(!world.AcquiredVictimOf(aurora).IsValid, $"{rate}: it attacks nothing any more.");
            Check.That(landing > aborted + 60, $"{rate}: it idles in the air and returns to land (frame {landing}, given up at {aborted}).");
            Check.That(otherSetBeforeLanding == 0, $"{rate}: it stays supersonic from the abort to its landing ({otherSetBeforeLanding} ticks with another set).");
            Check.That(setAtLanding == "SET_NORMAL", $"{rate}: its landing chooses the normal set ({setAtLanding}).");

            timelines.Add((rate, $"supersonic {firstSupersonic - attackOrdered} frames after the order, sluggish {sluggish - returnStart} frames after the return began, normal at the landing {setAtLanding}"));
            System.Console.WriteLine($"       #453 {rate}: take-off {takeoff}, ordered {attackOrdered}, drop {drop}, return {returnStart}, landed {landed}; second take-off {secondTakeoff}, aborted {aborted}, landing {landing}");
        }
        foreach (var (rate, line) in timelines) System.Console.WriteLine($"       #453 {rate}: {line}");
        Check.That(timelines.Select(value => value.Line).Distinct().Count() == 1, "The Aurora's sets change in the same frames of its own, counted from their causes, at 30, 60 and 120 Hz.");
    }
}
