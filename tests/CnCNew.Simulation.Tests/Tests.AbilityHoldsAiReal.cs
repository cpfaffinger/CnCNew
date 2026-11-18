using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// A real-data world for #441: a Crusader of player 1 at (<paramref name="tankX"/>, 600 + the two bounding radii + <paramref name="gap"/>) facing west, and <paramref name="actor"/> of player 2 at
    /// (330, 600) facing south, away from the tank's line. Player 1 sees player 2 as neutral throughout, so the tank never shoots; player 2 sees player 1 as neutral too until the caller
    /// says otherwise. Eight seconds have passed, so the actor's power has recharged after its creation, and the world stands at a frame boundary.
    /// </summary>
    private static (SimulationWorld World, Entity Tank, Entity Actor, ulong Sequence) RealAbilityHoldsAiWorld(RealFireWorld data, SimulationRate rate, string actor, int tankX, int gap)
    {
        var catalog = TemplateCatalog.Build(data.Graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", Team: 0), new PlayerConfiguration(new PlayerId(2), "West", Team: 0));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "ability-holds-ai");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-ability-holds-ai", data.Fingerprint, terrain.Fingerprint), catalog, terrain);
        ulong sequence = 0;
        const string tankName = "AmericaTankCrusader";
        Check.That(catalog.TryGet(tankName, out var tankTemplate), "the tank's template exists");
        Check.That(catalog.TryGet(actor, out var actorTemplate), "the actor's template exists");
        var tankY = Fixed.FromInt(600) + tankTemplate.BoundingCircleRadius + actorTemplate.BoundingCircleRadius + Fixed.FromInt(gap);
        Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, tankName, new PlayerId(1), Fixed.FromInt(tankX), tankY, Fixed.Zero, FixedTrig.Pi)).Accepted, "tank spawns");
        Check.That(world.Submit(new SpawnCommand(new PlayerId(2), ++sequence, world.Tick, actor, new PlayerId(2), Fixed.FromInt(330), Fixed.FromInt(600), Fixed.Zero, -FixedTrig.HalfPi)).Accepted, "actor spawns");
        world.SetRelationship(new PlayerId(1), new PlayerId(2), Relationship.Neutral);
        world.SetRelationship(new PlayerId(2), new PlayerId(1), Relationship.Neutral);
        for (var step = 0; step < world.Frames(8 * SimulationRate.OriginalFramesPerSecond); step++) world.Step();
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        return (world, world.Entities.Single(value => value.Template.Name == tankName), world.Entities.Single(value => value.Template.Name == actor), sequence);
    }

    /// <summary>
    /// #441 on real data: a Tank Hunter facing south is ordered to stick TNT on a Crusader that drives past it to the north-east and stops 20 beyond the hunter's line. The hunter walks to
    /// where the tank was (<c>approachTarget</c>), finds it within its start range there (the arrival slack) and turns on the spot to face it - more than 2.5 apart, beyond its launcher's
    /// minimum range. It turns at its locomotor's rate and acquires nothing until the charge sticks: the original's <c>startFacing</c> puts the AI into <c>AI_FACE_OBJECT</c>, which is not the
    /// idle state whose <c>getNextMoodTarget</c> picks victims. The port turned the unit inside the ability with its AI idle, so the hunter's own acquisition picked the tank and turned it as
    /// well - twice the turn rate. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealTankHunterTntFacesAtItsRateAndAcquiresNothing(string directory)
    {
        var data = OpenRealFireData(directory);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, tank, hunter, sequence) = RealAbilityHoldsAiWorld(data, rate, "ChinaInfantryTankHunter", 520, 20);
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++sequence, world.Tick, tank.Id, Fixed.FromInt(330), tank.Y)).Accepted, $"{rate}: the tank's move is accepted");
            // The order comes in the first tick of a frame, when the tank is 50 east of the hunter.
            for (var step = 0; step < world.Frames(30 * SimulationRate.OriginalFramesPerSecond) && !(tank.X <= Fixed.FromInt(380) && world.Time.IsOriginalFrameBoundary(world.Tick)); step++) world.Step();
            Check.That(tank.X <= Fixed.FromInt(380), $"{rate}: the tank drives towards the hunter's line");
            world.SetRelationship(new PlayerId(2), new PlayerId(1), Relationship.Enemies);
            var ratePerFrame = hunter.Template.Locomotor!.TurnRatePerTick * world.Time.TicksPerOriginalFrame;
            var charges = world.Entities.Count(value => value.Template.Name == "TNTStickyBomb");
            var order = world.Submit(new UseSpecialPowerCommand(new PlayerId(2), ++sequence, world.Tick, hunter.Id, "SpecialAbilityTankHunterTNTAttack", SpecialPowerTargetKind.Object, tank.Id, Fixed.Zero, Fixed.Zero, 0));
            Check.That(order.Accepted, $"{rate}: the TNT order is accepted ({order.Reason})");
            var (angle, x, y) = (hunter.Angle, hunter.X, hunter.Y);
            var (charged, facingFrames, faced) = (false, 0, Fixed.Zero);
            for (var step = 0; step < world.Frames(15 * SimulationRate.OriginalFramesPerSecond); step++)
            {
                world.Step();
                if (world.Entities.Count(value => value.Template.Name == "TNTStickyBomb") > charges) { charged = true; break; }
                Check.That(!world.AcquiredVictimOf(hunter).IsValid, $"{rate}: the Tank Hunter acquires nothing while its TNT ability runs (tick {world.Tick})");
                if (!world.Time.IsOriginalFrameBoundary(world.Tick)) continue;
                var delta = FixedTrig.AngleDifference(angle, hunter.Angle).Abs();
                Check.That(delta <= ratePerFrame + Fixed.FromRaw(16), $"{rate}: the Tank Hunter turns at most its rate in a frame ({delta} > {ratePerFrame} at tick {world.Tick})");
                if (delta > Fixed.Zero && hunter.X == x && hunter.Y == y) (facingFrames, faced) = (facingFrames + 1, faced + delta);
                (angle, x, y) = (hunter.Angle, hunter.X, hunter.Y);
            }
            Check.That(charged, $"{rate}: the Tank Hunter's charge sticks");
            Check.That(faced > Fixed.FromInt(2) && facingFrames >= 7, $"{rate}: the Tank Hunter faced the tank on the spot at its rate before the charge stuck ({facingFrames} frames, {faced})");
        }
    }

    /// <summary>
    /// #441 on real data: the scripts' idle check (<see cref="SimulationWorld.IsAiIdle"/>, <c>AIUpdateInterface::isIdle</c>) says "not idle" while a special ability turns its unit to its target
    /// (<c>AI_FACE_OBJECT</c>), unpacks it or packs it (<c>AI_BUSY</c>): a Black Lotus hacking a tank she faces away from. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealAbilityFacingAndPackingIsNotIdleToScripts(string directory)
    {
        var data = OpenRealFireData(directory);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, tank, lotus, sequence) = RealAbilityHoldsAiWorld(data, rate, "ChinaInfantryBlackLotus", 330, 40);
            world.SetRelationship(new PlayerId(2), new PlayerId(1), Relationship.Enemies);
            var order = world.Submit(new UseSpecialPowerCommand(new PlayerId(2), ++sequence, world.Tick, lotus.Id, "SpecialAbilityBlackLotusDisableVehicleHack", SpecialPowerTargetKind.Object, tank.Id, Fixed.Zero, Fixed.Zero, 0));
            Check.That(order.Accepted, $"{rate}: the hack is accepted ({order.Reason})");
            world.Step();
            var ability = lotus.Modules.OfType<SpecialAbilityUpdate>().Single(value => value.IsActive);
            var (facing, unpacking, packing, last) = (0, 0, 0, lotus.Angle);
            for (var step = 0; step < world.Frames(10 * SimulationRate.OriginalFramesPerSecond) && ability.IsActive; step++)
            {
                world.Step();
                var stage = ability.Stage;
                var turning = ability.IsActive && stage == AbilityStage.Approaching && lotus.Angle != last;
                last = lotus.Angle;
                if (!turning && stage is not (AbilityStage.Unpacking or AbilityStage.Packing)) continue;
                if (turning) facing++;
                else if (stage == AbilityStage.Unpacking) unpacking++;
                else packing++;
                Check.That(!world.IsAiIdle(lotus), $"{rate}: the Black Lotus is not idle to the scripts while her hack {(turning ? "turns her" : stage == AbilityStage.Unpacking ? "unpacks her" : "packs her")} (tick {world.Tick})");
            }
            Check.That(facing > 0 && unpacking > 0 && packing > 0, $"{rate}: the hack turned, unpacked and packed her (ticks {facing}, {unpacking}, {packing})");
            Check.That(!ability.IsActive && world.IsAiIdle(lotus), $"{rate}: the Black Lotus is idle once her hack is over");
        }
    }
}
