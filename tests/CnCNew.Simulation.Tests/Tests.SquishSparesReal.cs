using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
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
    /// <summary>
    /// Probe on real data: a <paramref name="tank"/> of player 1 at x = 300 drives east through a <paramref name="victim"/> of player 2 at x = <paramref name="victimX"/>, which faces away from it and is
    /// given <paramref name="order"/> ("hijack", "tnt" or "none"; the TNT order a logic frame before the tank sets off). Whether the tank crushed it, took the tank, and whether a TNT charge was
    /// placed before it died.
    /// </summary>
    private static (bool Crushed, bool Taken, bool Charged) RealRunOver(RealFireWorld data, SimulationRate rate, string tank, string victim, string order, int victimX)
    {
        var catalog = TemplateCatalog.Build(data.Graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", Team: 0), new PlayerConfiguration(new PlayerId(2), "West", Team: 0));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "squish");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-squish", data.Fingerprint, terrain.Fingerprint), catalog, terrain);
        ulong sequence = 0;
        Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, tank, new PlayerId(1), Fixed.FromInt(300), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero)).Accepted, "tank spawns");
        Check.That(world.Submit(new SpawnCommand(new PlayerId(2), ++sequence, world.Tick, victim, new PlayerId(2), Fixed.FromInt(victimX), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero)).Accepted, "victim spawns");
        // The two stand as neutrals while the Tank Hunter's power recharges after its creation (ReloadTime 7.5 s): neither shoots at the other.
        world.SetRelationship(new PlayerId(1), new PlayerId(2), Relationship.Neutral);
        world.SetRelationship(new PlayerId(2), new PlayerId(1), Relationship.Neutral);
        for (var step = 0; step < world.Frames(8 * SimulationRate.OriginalFramesPerSecond); step++) world.Step();
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        world.SetRelationship(new PlayerId(1), new PlayerId(2), Relationship.Enemies);
        world.SetRelationship(new PlayerId(2), new PlayerId(1), Relationship.Enemies);
        var vehicle = world.Entities.Single(value => value.Template.Name == tank);
        var man = world.Entities.Single(value => value.Template.Name == victim);
        var charges = world.Entities.Count(value => value.Template.Name == "TNTStickyBomb");
        if (order == "tnt")
        {
            var tntOrder = world.Submit(new UseSpecialPowerCommand(new PlayerId(2), ++sequence, world.Tick, man.Id, "SpecialAbilityTankHunterTNTAttack", SpecialPowerTargetKind.Object, vehicle.Id, Fixed.Zero, Fixed.Zero, 0));
            Check.That(tntOrder.Accepted, $"the TNT order is accepted ({tntOrder.Reason})");
            for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++) world.Step();
        }
        Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++sequence, world.Tick, vehicle.Id, Fixed.FromInt(800), Fixed.FromInt(600))).Accepted, "the tank's move is accepted");
        if (order == "hijack") { var hijackOrder = world.Submit(new MoveAndEnterCommand(new PlayerId(2), ++sequence, world.Tick, man.Id, vehicle.Id)); Check.That(hijackOrder.Accepted, $"the hijack is accepted ({hijackOrder.Reason})"); }
        var (crushed, charged) = (false, false);
        for (var step = 0; step < world.Frames(20 * SimulationRate.OriginalFramesPerSecond); step++)
        {
            foreach (var value in world.Step().Events)
                if (value.Kind == SimEventKind.EntityDied && value.Entity == man.Id && value.Value == DeathTypes.Crushed) crushed = true;
            if (!charged && !crushed && world.Entities.Count(value => value.Template.Name == "TNTStickyBomb") > charges) charged = true;
        }
        return (crushed, vehicle.Owner == new PlayerId(2), charged);
    }

    /// <summary>
    /// #370 on real data: a Crusader and an Overlord run at a hijacker that runs at them to take them: it is not squished and takes the tank; one that stands idle is squished. A Tank Hunter
    /// placing TNT on such a tank is not squished before its charge sticks - the tank's shot while it turns to the tank does not make it walk off to answer it (#439: an ability that turns
    /// its unit holds the AI, which is then not idle and does not retaliate); an idle one is squished. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealSquishSparesHijackerAndTankHunter(string directory)
    {
        var data = OpenRealFireData(directory);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
            foreach (var tank in new[] { "AmericaTankCrusader", "ChinaTankOverlord" })
            {
                var what = $"{rate}: {tank}";
                var hijack = RealRunOver(data, rate, tank, "GLAInfantryHijacker", "hijack", 320);
                Check.That(!hijack.Crushed && hijack.Taken, $"{what}: a hijacker running at it is not squished and takes it (crushed {hijack.Crushed}, taken {hijack.Taken}).");
                var idle = RealRunOver(data, rate, tank, "GLAInfantryHijacker", "none", 320);
                Check.That(idle.Crushed && !idle.Taken, $"{what}: an idle hijacker it runs over is squished (crushed {idle.Crushed}).");
                var tnt = RealRunOver(data, rate, tank, "ChinaInfantryTankHunter", "tnt", 320);
                Check.That(tnt.Charged, $"{what}: a Tank Hunter placing TNT on it is not squished before its charge sticks (crushed {tnt.Crushed}).");
                Check.That(RealRunOver(data, rate, tank, "ChinaInfantryTankHunter", "none", 320).Crushed, $"{what}: an idle Tank Hunter it runs over is squished.");
            }
    }
}
