using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// The script statuses "disabled" and "unpowered" (<c>Object::setScriptStatus</c>, <c>BuildAssistant::canMakeUnit</c>) and the panel-flag actions that set them
/// (<c>ScriptActions::changeObjectPanelFlagForSingleObject</c>). The port kept the unpowered status and read it nowhere, and handed the panel flags to the presentation.
/// </summary>
static partial class Tests
{
    /// <summary>
    /// A map's <c>objectPowered</c> no: the object is disabled as <c>DISABLED_SCRIPT_UNDERPOWERED</c> (a power plant takes its production out by its disabled edge), a factory refuses to
    /// queue (<c>CANMAKE_FACTORY_IS_DISABLED</c>, as for a script disable), a human player's dozer refuses to build while a computer player's builds ("the ai will validate, or cheat").
    /// </summary>
    public static void ScriptUnpoweredMakesNothing()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: ConstructionFiles());
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta", IsComputer: true)];
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.GiveMoney(1, 1000);
        feeder.GiveMoney(2, 1000);
        ulong sequence = 100;
        PlaceMapObjectCommand Place(string template, int owner, int x, int y, MapObjectFlags flags)
            => new(PlayerId.System, sequence++, world.Tick, template, new PlayerId(owner), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero, 100, flags, 0);
        Check.That(world.Submit(Place("Barracks", 1, 100, 100, MapObjectFlags.Unpowered)).Accepted && world.Submit(Place("Plant", 1, 200, 100, MapObjectFlags.Unpowered)).Accepted
                   && world.Submit(Place("Dozer", 1, 300, 100, MapObjectFlags.Unpowered)).Accepted && world.Submit(Place("Dozer", 2, 300, 300, MapObjectFlags.Unpowered)).Accepted,
            "Setup: the system places the unpowered objects.");
        world.Step();
        world.Step();
        Entity Of(string template, int owner) => world.Entities.Single(entity => entity.Template.Name == template && entity.Owner.Value == owner);
        var barracks = Of("Barracks", 1);
        var plant = Of("Plant", 1);
        Check.That(world.DisabledFlagsOf(barracks) == DisabledMask.ScriptUnderpowered && world.DisabledFlagsOf(plant) == DisabledMask.ScriptUnderpowered,
            $"An object a map leaves unpowered is disabled as DISABLED_SCRIPT_UNDERPOWERED ({world.DisabledFlagsOf(barracks)}).");
        Check.That(world.ProducedEnergyOf(new PlayerId(1)) == 0, $"A script-unpowered plant produces nothing: its disabled edge took it out ({world.ProducedEnergyOf(new PlayerId(1))}).");

        var queue = new QueueProductionCommand(new PlayerId(1), 1, world.Tick, barracks.Id, "Grunt");
        Check.That(world.Check(queue).Reason == CommandRejection.FactoryIsDisabled && feeder.QueueProduction(1, barracks.Id.Value, "Grunt").Reason == CommandRejection.FactoryIsDisabled,
            "An unpowered factory refuses to queue: CANMAKE_FACTORY_IS_DISABLED.");
        Check.That(feeder.Construct(1, Of("Dozer", 1).Id.Value, "Plant", 100, 300).Reason == CommandRejection.FactoryIsDisabled,
            "A human player's unpowered dozer builds nothing (DozerAIUpdate::construct asks canMakeUnit).");
        Check.That(feeder.Construct(2, Of("Dozer", 2).Id.Value, "Plant", 400, 300).Accepted, "A computer player's unpowered dozer is not asked.");

        world.SetScriptDisable(barracks, Entity.ScriptUnpowered, false);
        world.SetScriptDisable(plant, Entity.ScriptUnpowered, false);
        world.Step();
        Check.That(world.DisabledFlagsOf(barracks) == DisabledMask.None && world.ProducedEnergyOf(new PlayerId(1)) == 5, "Powered again, the plant produces.");
        Check.That(feeder.QueueProduction(1, barracks.Id.Value, "Grunt").Accepted, "Powered again, the factory queues.");
        world.SetScriptDisable(barracks, Entity.ScriptDisabled, true);
        Check.That(feeder.QueueProduction(1, barracks.Id.Value, "Grunt").Reason == CommandRejection.FactoryIsDisabled, "A script-disabled factory refuses for the same reason.");
        Check.That(world.RejectionCounts[(int)CommandRejection.FactoryIsDisabled] == 3, $"And every refusal is counted ({world.RejectionCounts[(int)CommandRejection.FactoryIsDisabled]}).");

        // ProductionUpdate processes only DISABLED_HELD: a queue that stands when the power is taken goes on when it comes back.
        var retail = new SyntheticFixture(TestRate.Default, files: RetailModuleFiles());
        var factoryWorld = NewWorld(retail);
        var factoryFeeder = new Feeder(factoryWorld);
        factoryFeeder.Spawn(1, "DefaultExitFactory", x: 300, y: 300);
        factoryWorld.Step();
        factoryFeeder.GiveMoney(1, 1000);
        factoryWorld.Step();
        var factory = factoryWorld.Entities.Single(entity => entity.Template.Name == "DefaultExitFactory");
        Check.That(factoryFeeder.QueueProduction(1, factory.Id.Value, "QuickUnit").Accepted, "Setup: the factory queues its unit.");
        factoryWorld.Step();
        factoryWorld.SetScriptDisable(factory, Entity.ScriptUnpowered, true);
        for (var tick = 0; tick < factoryWorld.Frames(90); tick++) factoryWorld.Step();
        Check.That(CountOf(factoryWorld, "QuickUnit") == 0, "An unpowered factory's queue stands: ProductionUpdate does not process DISABLED_SCRIPT_UNDERPOWERED.");
        factoryWorld.SetScriptDisable(factory, Entity.ScriptUnpowered, false);
        for (var tick = 0; tick < factoryWorld.Frames(90); tick++) factoryWorld.Step();
        Check.That(CountOf(factoryWorld, "QuickUnit") == 1, "Powered again, it makes its unit.");
    }

    /// <summary>
    /// <c>UNIT_AFFECT_OBJECT_PANEL_FLAGS</c> and <c>TEAM_AFFECT_OBJECT_PANEL_FLAGS</c>: "Enabled" and "Powered" off disable the object as a script disable and a script underpower, on
    /// again release it; "Indestructible", "Unsellable" and "Player Targetable" set what they name; "Selectable" goes to the presentation; a flag the original does not name is counted.
    /// </summary>
    public static void ScriptPanelFlags()
    {
        MapScriptAction Flag(string action, MapScriptParameter target, string flag, bool value) => Scr.Do(action, target, Scr.Named(MapScriptParameterType.ObjectPanelFlag, flag), Scr.Bool(value));
        const string Unit = "UNIT_AFFECT_OBJECT_PANEL_FLAGS", Team = "TEAM_AFFECT_OBJECT_PANEL_FLAGS";
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Off", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Flag(Unit, Scr.Unit("Bunker"), "Powered", false), Flag(Unit, Scr.Unit("Bunker"), "Enabled", false), Flag(Unit, Scr.Unit("Bunker"), "Indestructible", true),
                Flag(Unit, Scr.Unit("Bunker"), "Unsellable", true), Flag(Unit, Scr.Unit("Bunker"), "Player Targetable", true), Flag(Unit, Scr.Unit("Bunker"), "Selectable", false),
                Flag(Team, Scr.Team("Squad"), "Powered", false), Flag(Unit, Scr.Unit("Bunker"), "powered", true), Flag(Unit, Scr.Unit("Nobody"), "Enabled", false)], oneShot: true),
            Scr.Script("On", [Scr.And(Scr.If("FLAG", Scr.Flag("on"), Scr.Bool(true)))], [
                Flag(Unit, Scr.Unit("Bunker"), "Powered", true), Flag(Unit, Scr.Unit("Bunker"), "Enabled", true), Flag(Unit, Scr.Unit("Bunker"), "Indestructible", false),
                Flag(Team, Scr.Team("Squad"), "Powered", true)], oneShot: true),
        ], objects: [Placed("AlphaBunker", 100m, 60m, "teamA", "Bunker"), Placed("AlphaSoldier", 100m, 140m, "Squad", "Trooper1"), Placed("AlphaSoldier", 120m, 140m, "Squad", "Trooper2")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(10));
        var bunker = NamedIn(session, "Bunker")!;
        var troopers = new[] { NamedIn(session, "Trooper1")!, NamedIn(session, "Trooper2")! };
        Check.That(ScriptCoverage.ActionSupport(Unit) == ScriptSupport.Executed && ScriptCoverage.ActionSupport(Team) == ScriptSupport.Executed, "Both actions are executed by the VM.");
        Check.That(session.World.DisabledFlagsOf(bunker) == (DisabledMask.ScriptDisabled | DisabledMask.ScriptUnderpowered),
            $"Enabled and Powered off disable the object twice over ({session.World.DisabledFlagsOf(bunker)}).");
        Check.That(bunker.Body is CnCNew.Simulation.Modules.BuiltIn.ActiveBody { Indestructible: true }, "Indestructible is the body's.");
        Check.That((bunker.ScriptDisableMask & (Entity.ScriptUnsellable | Entity.ScriptTargetable)) == (Entity.ScriptUnsellable | Entity.ScriptTargetable), "Unsellable and Player Targetable are script statuses.");
        Check.That(troopers.All(trooper => session.World.DisabledFlagsOf(trooper) == DisabledMask.ScriptUnderpowered), "The team's flag reaches every member.");
        var outputs = session.Engine.DrainOutputs().Where(output => output.Action == Unit).ToArray();
        Check.That(outputs.Length == 1 && outputs[0].Arguments is ["Bunker", "Selectable", "0"], $"Only Selectable goes to the presentation ({string.Join(" | ", outputs.Select(output => string.Join(",", output.Arguments)))}).");
        Check.That(session.Engine.Diagnostics.ContainsKey($"{Unit}: unknown flag 'powered'"), $"A flag the original does not name (it compares exactly) changes nothing and is counted ({string.Join("; ", session.Engine.Diagnostics.Keys)}).");
        Check.That(session.Engine.MissingActionHits.Count == 0, "Nothing is missing.");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash(), "A restored mission matches.");

        session.Engine.SignalUiInteraction("on");
        Run(session, session.Frames(10));
        Check.That(session.World.DisabledFlagsOf(bunker) == DisabledMask.None && bunker.Body is CnCNew.Simulation.Modules.BuiltIn.ActiveBody { Indestructible: false },
            "On again, the object is enabled, powered and destructible.");
        Check.That(troopers.All(trooper => session.World.DisabledFlagsOf(trooper) == DisabledMask.None), "And the team is powered.");
    }

    /// <summary><c>Team::tryToRecruit</c> leaves out a unit whose AI is not recruitable: the panel flag "AI Recruitable" off keeps the nearest soldier out of the new team.</summary>
    public static void ScriptPanelFlagRecruitable()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Recruit", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("UNIT_AFFECT_OBJECT_PANEL_FLAGS", Scr.Unit("Free3"), Scr.Named(MapScriptParameterType.ObjectPanelFlag, "AI Recruitable"), Scr.Bool(false)),
                Scr.Do("RECRUIT_TEAM", Scr.Team("Strike"), Scr.Real(0m))], oneShot: true),
        ], objects: [Placed("BetaSoldier", 100m, 200m, "teamPlayerB", "Free1"), Placed("BetaSoldier", 120m, 200m, "teamPlayerB", "Free2"), Placed("BetaSoldier", 140m, 200m, "teamPlayerB", "Free3")]);
        MapProperties Strike(string name, string owner, string unit) => new([Scr.Text("teamName", name), Scr.Text("teamOwner", owner), Scr.Flag("teamIsSingleton", true), Scr.Text("teamHome", "Far"),
            new MapProperty("teamProductionPriority", MapPropertyType.Int, 5, 0m, string.Empty),
            Scr.Text("teamUnitType1", unit), new MapProperty("teamUnitMinCount1", MapPropertyType.Int, 1, 0m, string.Empty), new MapProperty("teamUnitMaxCount1", MapPropertyType.Int, 2, 0m, string.Empty)]);
        map = map with { Teams = [..map.Teams, Strike("Strike", PlayerB, "BetaSoldier"), new([Scr.Text("teamName", "teamPlayerB"), Scr.Text("teamOwner", PlayerB), Scr.Flag("teamIsSingleton", true)])] };
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(10));
        var strike = session.Engine.TeamInstances.First(team => team.Prototype == "Strike");
        var members = strike.Members.Select(id => session.World.FindEntity(new EntityId(id))!).ToArray();
        Check.That(members.Length == 2 && !members.Contains(NamedIn(session, "Free3")) && members.Contains(NamedIn(session, "Free1")) && members.Contains(NamedIn(session, "Free2")),
            $"The unrecruitable soldier stays out; the two others join ({members.Length} members; {string.Join("; ", session.Engine.Diagnostics.Keys)}).");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash(), "A restored mission keeps who is unrecruitable.");
    }
}
