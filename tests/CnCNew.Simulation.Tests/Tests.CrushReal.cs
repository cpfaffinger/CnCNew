using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>Probe: a vehicle ordered through a group of infantry on real data; how many of them were crushed.</summary>
    private static (int Dead, int Total, double TankX) CrushRun(RealFireWorld data, SimulationRate rate, string tank, string soldier, bool soldiersMove, string order, Relationship relation, bool across)
    {
        var catalog = TemplateCatalog.Build(data.Graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", Team: relation == Relationship.Allies ? 1 : 0), new PlayerConfiguration(new PlayerId(2), "West", Team: relation == Relationship.Allies ? 1 : 0));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "crush");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-crush", data.Fingerprint, terrain.Fingerprint), catalog, terrain);
        if (relation != Relationship.Allies) world.RelationshipOverride = (a, b) => a.Owner != b.Owner ? relation : null;
        ulong sequence = 0;
        Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, tank, new PlayerId(1), Fixed.FromInt(300), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero)).Accepted, "tank spawns");
        for (var index = 0; index < 5; index++)
            Check.That(world.Submit(new SpawnCommand(new PlayerId(2), ++sequence, world.Tick, soldier, new PlayerId(2), Fixed.FromInt(500), Fixed.FromInt(across ? 600 : 570 + index * 15), Fixed.Zero, Fixed.Zero)).Accepted, "soldier spawns");
        world.Step();
        var vehicle = world.Entities.Single(value => value.Template.Name == tank);
        var men = world.Entities.Where(value => value.Template.Name == soldier).ToArray();
        if (soldiersMove)
            foreach (var man in men)
                world.Submit(new SetMoveTargetCommand(new PlayerId(2), ++sequence, world.Tick, man.Id, Fixed.FromInt(520), Fixed.FromInt(660)));
        SimCommand command = order == "attackmove"
            ? new AttackMoveCommand(new PlayerId(1), ++sequence, world.Tick, vehicle.Id, Fixed.FromInt(800), Fixed.FromInt(600))
            : new SetMoveTargetCommand(new PlayerId(1), ++sequence, world.Tick, vehicle.Id, Fixed.FromInt(800), Fixed.FromInt(600));
        Check.That(world.Submit(command).Accepted, "order accepted");
        for (var tick = 0; tick < world.Frames(40 * SimulationRate.OriginalFramesPerSecond); tick++) world.Step();
        return (men.Count(value => value.EffectivelyDead), men.Length, (double)vehicle.X.ToDecimal());
    }

    /// <summary>
    /// Issue 350: a crusher's way does not go round what it can run over (<c>Object::canCrushOrSquish</c>, used by <c>Pathfinder::checkForMovement</c> for an enemy "fixed" unit and by <c>AIUpdateInterface::blockedBy</c>:
    /// "just run over them"). A tank ordered along a row of idle infantry that is not its ally's drives through it and crushes every soldier, whatever it is called and at every rate; a row of its ally's is gone round and
    /// stays alive. Of a column across its line, 15 units apart, only the soldier under its box dies.
    /// </summary>
    public static void RealTanksRunIdleInfantryOver(string directory)
    {
        var data = OpenRealFireData(directory);
        foreach (var tank in new[] { "AmericaTankCrusader", "ChinaTankBattleMaster", "ChinaTankOverlord" })
            foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
                foreach (var order in new[] { "move", "attackmove" })
                {
                    var (dead, total, x) = CrushRun(data, rate, tank, "AmericaInfantryRanger", false, order, Relationship.Neutral, across: true);
                    Check.That(dead == total && x > 790, $"{tank} {order} at {rate}: the row of {total} idle soldiers in its way is crushed ({dead} dead), the tank arrives (x={x:F0}).");
                    var (spread, spreadTotal, _) = CrushRun(data, rate, tank, "AmericaInfantryRanger", false, order, Relationship.Neutral, across: false);
                    // SquishCollide tests the tank's box against the soldier's geometry with radius 1 (geomCollidesWithGeom, #363): only the soldier on its line is under it; the ones 15
                    // and 30 off stand beside a box 18 to 26 wide, whose bounding circle they were inside.
                    Check.That(spread == 1, $"{tank} {order} at {rate}: of a column of soldiers across its line only the one under its box is crushed ({spread} of {spreadTotal}).");
                }
        var (allied, alliedTotal, alliedX) = CrushRun(data, SimulationRate.Hz30, "AmericaTankCrusader", "AmericaInfantryRanger", false, "move", Relationship.Allies, across: true);
        Check.That(allied == 0 && alliedX > 790, $"Allied infantry in the way is not crushed ({allied} of {alliedTotal} dead) and the tank goes round it (x={alliedX:F0}).");
    }
}
