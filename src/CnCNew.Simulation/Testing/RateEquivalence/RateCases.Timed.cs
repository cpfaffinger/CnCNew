using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED"), DetectedBit = ObjectStatusBits.Index("DETECTED");

    private const string StandardDetector = "ChinaGattlingCannon", StandardSneak = "GLAInfantryJarmenKell";

    private static RateCaseGroup TimedCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, Timed);
        var catalog = catalogs.Reference;

        // Special powers: the recharge every carrier starts with, one case per power.
        var powers = 0;
        foreach (var power in catalog.SpecialPowers.Powers.OrderBy(power => power.Name, StringComparer.Ordinal))
        {
            var marker = $"PowerName = {power.Name},";
            var carrier = catalog.Templates.Where(template => template.IsInstantiable && template.Modules.Any(module => DataText(module).Contains(marker, StringComparison.Ordinal)))
                .OrderBy(template => template.Name, StringComparer.Ordinal).FirstOrDefault();
            if (carrier is null) continue;
            powers++;
            var (carrierName, powerName) = (carrier.Name, power.Name);
            builder.Add($"recharge {powerName}@{carrierName}", scene => MeasureRecharge(scene, carrierName, powerName), carrierName);
        }
        builder.Note(string.Create(CultureInfo.InvariantCulture, $"{catalog.SpecialPowers.Powers.Length} special powers, {powers} carried by an instantiable object (the first carrier by name stands for each)"));

        // Lifetimes: every distinct LifetimeUpdate/DeletionUpdate.
        var (lifetimes, lifetimeCandidates) = Distinct(catalogs, "LifetimeUpdate", _ => true, "LifetimeUpdate");
        var (deletions, deletionCandidates) = Distinct(catalogs, "DeletionUpdate", _ => true, "DeletionUpdate");
        foreach (var template in lifetimes.Concat(deletions).DistinctBy(template => template.Name))
        {
            var name = template.Name;
            builder.Add($"lifetime {name}", scene => MeasureLifetime(scene, name), name);
        }
        builder.Note(string.Create(CultureInfo.InvariantCulture, $"{lifetimeCandidates} objects with a LifetimeUpdate and {deletionCandidates} with a DeletionUpdate, {lifetimes.Length} and {deletions.Length} distinct by their data"));

        // Slow death: every distinct set of SlowDeathBehavior modules.
        var (deaths, deathCandidates) = Distinct(catalogs, "SlowDeathBehavior", template => !IsAircraft(template) && !template.IsKindOf("PROJECTILE"), "SlowDeathBehavior");
        foreach (var template in deaths)
        {
            var name = template.Name;
            builder.Add($"slow death {name}", scene => MeasureSlowDeath(scene, name), name);
        }
        builder.Note(string.Create(CultureInfo.InvariantCulture, $"{deathCandidates} ground objects die a slow death (SlowDeathBehavior), {deaths.Length} distinct by the data of all their slow-death modules"));

        foreach (var helicopter in new[] { "AmericaVehicleComanche", "AmericaVehicleChinook", "ChinaVehicleHelix" })
            builder.Add($"helicopter slow death {helicopter}", scene => MeasureHelicopterDeath(scene, helicopter), helicopter);
        builder.Add("battle bus second life GLAVehicleBattleBus", scene => MeasureBattleBus(scene, "GLAVehicleBattleBus"), "GLAVehicleBattleBus");

        // Toppling: every distinct ToppleUpdate pushed over by a tank, every StructureToppleUpdate and StructureCollapseUpdate killed.
        var (topplers, toppleCandidates) = Distinct(catalogs, "ToppleUpdate", _ => true, "ToppleUpdate");
        foreach (var template in topplers)
        {
            var name = template.Name;
            builder.Add($"topple {name}", scene => MeasureTopple(scene, name), name, FieldTank);
        }
        builder.Note(string.Create(CultureInfo.InvariantCulture, $"{toppleCandidates} objects can be pushed over (ToppleUpdate), {topplers.Length} distinct by its data"));
        var (towers, _) = Distinct(catalogs, "StructureToppleUpdate", _ => true, "StructureToppleUpdate");
        foreach (var template in towers)
        {
            var name = template.Name;
            builder.Add($"structure topple {name}", scene => MeasureStructureTopple(scene, name), name, FieldSoldier);
        }
        var (collapsers, _) = Distinct(catalogs, "StructureCollapseUpdate", _ => true, "StructureCollapseUpdate");
        foreach (var template in collapsers)
        {
            var name = template.Name;
            builder.Add($"structure collapse {name}", scene => MeasureStructureCollapse(scene, name), name);
        }

        foreach (var mine in new[] { "ChinaStandardMine", "ChinaClusterMine", "ChinaEMPMine" })
            builder.Add($"mine {mine}", scene => MeasureMine(scene, mine), mine, "AmericaTankCrusader");

        // Stealth: every distinct StealthUpdate against one detector; every distinct detector against one sneaking unit.
        var (sneaks, sneakCandidates) = Distinct(catalogs, "StealthUpdate", template => !IsAircraft(template) && !template.IsKindOf("PROJECTILE") && !template.IsKindOf("MINE"), "StealthUpdate");
        foreach (var template in sneaks)
        {
            var name = template.Name;
            builder.Add($"stealth {name}", scene => MeasureStealth(scene, name), name, StandardDetector);
        }
        var (detectors, detectorCandidates) = Distinct(catalogs, "StealthDetectorUpdate", template => !IsAircraft(template) && !template.IsKindOf("PROJECTILE"), "StealthDetectorUpdate");
        foreach (var template in detectors)
        {
            var name = template.Name;
            builder.Add($"detector {name}", scene => MeasureDetector(scene, name), name, StandardSneak);
        }
        builder.Note(string.Create(CultureInfo.InvariantCulture,
            $"{sneakCandidates} ground objects with a StealthUpdate ({sneaks.Length} distinct), {detectorCandidates} with a StealthDetectorUpdate ({detectors.Length} distinct)"));

        builder.Add("vision AmericaInfantryRanger", scene => MeasureVision(scene, "AmericaInfantryRanger"), "AmericaInfantryRanger");
        foreach (var ping in new[] { "SpySatellitePing", "RadarVanPing" })
            builder.Add($"shroud clearing {ping}", scene => MeasureShroudPing(scene, ping), ping);
        foreach (var (unit, building) in new[] { ("AmericaInfantryRanger", "ChinaBarracks"), ("ChinaInfantryRedguard", "AmericaBarracks"), ("GLAInfantryRebel", "AmericaBarracks") })
            builder.Add($"capture {unit}", scene => MeasureCapture(scene, unit, building), unit, building);
        builder.Add("avalanche AvalancheLeadChunk", MeasureAvalanche, "AvalancheLeadChunk", "AvalancheChunk");
        foreach (var pulse in new[] { "EMPPulseEffectSpheroid", "EMPMineEffectSpheroid", "EMPPatriotEffectSpheroid", "MOABEffectSpheroid" })
            builder.Add($"emp {pulse}", scene => MeasureEmp(scene, pulse), pulse, FieldTank);
        builder.Add("firestorm FirestormSmall", scene => MeasureField(scene, "FirestormSmall"), "FirestormSmall", FieldTank, FieldSoldier);
        builder.Add("train TrainEngine", MeasureTrain, "TrainEngine");

        builder.NotCovered("timed: the sequences of the superweapons after their launch (scud storm volleys, particle cannon beam, A-10 and carpet bomb runs, artillery barrage, spectre gunship orbit, leaflet drop, cluster mines), battle plans, the bridge scaffold, the checkpoint gate, the wave guide and the smart bomb's homing");
        builder.NotCovered("timed: recharge after a use and shared timers after the first use (the recharge from creation is read from the module's ready tick, and stepped for the powers of at most 60 s)");
        builder.NotCovered("timed: stealth of aircraft, of mines and of garrisoned units; stealth granted by an upgrade (a unit that is not stealthed within 30 s appears unmeasured)");
        return builder.Done();
    }

    /// <summary>The ready tick a freshly created carrier holds for the power; for powers of at most a minute also the stepped wait until the module says ready.</summary>
    private static ImmutableArray<RateQuantity> MeasureRecharge(RateScene scene, string carrierName, string powerName)
    {
        var quantities = new RateQuantities(scene);
        var carrier = scene.Spawn(carrierName, 1, 2000, 2000);
        var power = PowerOf(scene, carrier, powerName);
        if (!scene.World.SpecialPowers.TryGet(powerName, out var info)) throw new InvalidOperationException($"Unknown special power {powerName}.");
        quantities.Exact("recharge from creation (ready tick)", Math.Max(0, power.AvailableOnFrame(scene.World) - carrier.SpawnTick));
        if (scene.Catalogs.Reference.SpecialPowers.TryGet(powerName, out var reference) && reference.ReloadTicks <= RateScene.Seconds(60) && !power.IsPaused && !info.SharedSynced)
            quantities.Exact("creation to ready (stepped)", Since(scene, carrier, () => power.IsReady(scene.World), RateScene.Seconds(90), readsClock: true));
        return quantities.Done();
    }

    /// <summary>
    /// The ticks from the creation of <paramref name="since"/> to the step after which <paramref name="condition"/> first held; 0 when it already holds after the
    /// frame of the original the object was created in, -1 when it never did.
    /// </summary>
    /// <param name="readsClock">
    /// The condition compares the world's clock with a tick (a ready tick, a deadline): after a step the clock already shows the next tick, so the condition
    /// holds for that tick, not for the one just stepped.
    /// </param>
    private static long Since(RateScene scene, Entity since, Func<bool> condition, long budgetFrames, bool readsClock = false)
    {
        if (condition()) return 0;
        if (scene.StepUntil(condition, budgetFrames) < 0) return -1;
        return readsClock ? scene.World.Tick - since.SpawnTick : scene.World.Tick - Time.TickOrder.Next - since.SpawnTick;
    }

    private static ImmutableArray<RateQuantity> MeasureLifetime(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        scene.Align();
        var start = scene.World.Tick;
        scene.Order(1, (owner, sequence, now) => new SpawnCommand(owner, sequence, now, name, owner, Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero));
        Entity? thing = null;
        var gone = scene.StepUntil(() =>
        {
            thing ??= scene.Find(name, start);
            return thing is not null && scene.Gone(thing);
        }, RateScene.Seconds(300));
        quantities.Exact("creation to end", gone);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureSlowDeath(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var thing = scene.Spawn(name, 1, 2000, 2000);
        scene.StepFrames(RateScene.Seconds(1));
        scene.Align();
        if (scene.Gone(thing)) return quantities.Exact("kill to removal", -1).Done();
        scene.Kill(thing);
        var gone = scene.StepUntil(() => scene.World.FindEntity(thing.Id) != thing, RateScene.Seconds(300));
        quantities.Exact("kill to removal", gone);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureHelicopterDeath(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var helicopter = scene.Spawn(name, 1, 2000, 2000);
        scene.StepFrames(RateScene.Seconds(8));
        scene.Align();
        var (startX, startY, startZ) = (helicopter.X, helicopter.Y, helicopter.Z);
        var killed = scene.World.Tick;
        scene.Kill(helicopter);
        long halfway = -1;
        var (lastX, lastY, lowest) = (startX, startY, startZ);
        var turned = Fixed.Zero;
        var heading = helicopter.Angle;
        var gone = scene.StepUntil(() =>
        {
            if (scene.World.FindEntity(helicopter.Id) != helicopter) return true;
            turned += FixedTrig.AngleDifference(heading, helicopter.Angle).Abs();
            heading = helicopter.Angle;
            (lastX, lastY) = (helicopter.X, helicopter.Y);
            lowest = Fixed.Min(lowest, helicopter.Z);
            if (halfway < 0 && helicopter.Z * 2 <= startZ) halfway = scene.World.Tick - Time.TickOrder.Next - killed;
            return false;
        }, RateScene.Seconds(120));
        quantities.Distance("height when killed", startZ);
        quantities.Event("kill to half the height", halfway);
        quantities.Distance("lowest height before removal", lowest);
        quantities.Distance("last position: distance from where it was killed", RateMeasure.Distance(startX, startY, lastX, lastY), gone >= 0);
        quantities.Angle("spin until removal (sum of heading changes)", turned);
        quantities.Event("kill to removal", gone);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureBattleBus(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var bus = scene.Spawn(name, 1, 2000, 2000);
        scene.StepFrames(RateScene.Seconds(1));
        scene.Align();
        // A lethal hit that can be resisted: the first death of the bus (its UndeadBody starts the second life and the bus is thrown). KillCommand deals
        // unresistable damage, which ends the bus on its first life at every rate and leaves nothing to measure.
        var lethal = (bus.Body?.MaxHealth ?? Fixed.One) * 10;
        scene.Order(0, (_, sequence, now) => new DamageCommand(PlayerId.System, sequence, now, bus.Id, lethal, DamageTypes.Explosion, DeathTypes.Normal));
        long up = -1, down = -1;
        var apex = Fixed.Zero;
        var steps = scene.Time.WholeSecondsToTicks(30);
        for (long step = 1; step <= steps; step++)
        {
            scene.Step();
            if (scene.World.FindEntity(bus.Id) != bus) break;
            apex = Fixed.Max(apex, bus.Z);
            if (up < 0 && bus.Z > Fixed.Zero) up = step;
            if (up >= 0 && down < 0 && bus.Z <= Fixed.Zero) down = step;
        }
        quantities.Count("alive after the first death", scene.Alive(bus) ? 1 : 0);
        quantities.Event("kill to leaving the ground", Elapsed(up));
        quantities.Event("time in the air", up >= 0 && down >= 0 ? down - up : -1);
        quantities.Distance("apex of the throw", apex);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureTopple(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var thing = scene.Spawn(name, 2, 2150, 2000);
        var tank = scene.Spawn(FieldTank, 1, 2000, 2000);
        var topple = thing.Modules.OfType<ToppleUpdate>().FirstOrDefault() ?? throw new InvalidOperationException($"{name} has no ToppleUpdate.");
        scene.Align();
        scene.Move(tank, 2400, 2000);
        long pushed = -1, down = -1;
        var angle = Fixed.Zero;
        var steps = scene.Time.WholeSecondsToTicks(40);
        for (long step = 1; step <= steps && down < 0; step++)
        {
            scene.Step();
            if (pushed < 0 && !topple.IsAbleToBeToppled) pushed = step;
            if (pushed >= 0) angle = Fixed.Max(angle, topple.ToppleAngle);
            if (pushed >= 0 && (topple.IsDown || scene.World.FindEntity(thing.Id) != thing)) down = step;
        }
        quantities.Event("order to pushed over", Elapsed(pushed));
        quantities.Event("fall: pushed to lying", pushed >= 0 && down >= 0 ? down - pushed : -1);
        quantities.Angle("angle fallen", angle, pushed >= 0);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureStructureTopple(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var tower = scene.Spawn(name, 2, 2000, 2000);
        // Eight bystanders of the other player around the tower: whoever stands where it falls is crushed by its weapon.
        var bystanders = new List<Entity>();
        for (var index = 0; index < 8; index++)
        {
            var angle = FixedTrig.TwoPi * index / Fixed.FromInt(8);
            bystanders.Add(scene.Spawn(FieldSoldier, 1, Fixed.FromInt(2000) + FixedTrig.Cos(angle) * 45, Fixed.FromInt(2000) + FixedTrig.Sin(angle) * 45));
        }
        var topple = tower.Modules.OfType<StructureToppleUpdate>().FirstOrDefault() ?? throw new InvalidOperationException($"{name} has no StructureToppleUpdate.");
        scene.Align();
        scene.Kill(tower);
        long done = -1;
        var hits = 0;
        var damage = Fixed.Zero;
        var steps = scene.Time.WholeSecondsToTicks(120);
        for (long step = 1; step <= steps && done < 0; step++)
        {
            foreach (var item in scene.Step().Events)
                if (item.Kind == SimEventKind.EntityDamaged && bystanders.Any(bystander => bystander.Id == item.Entity))
                {
                    hits++;
                    damage += Fixed.FromRaw(item.Value);
                }
            if (topple.IsToppled || scene.World.FindEntity(tower.Id) != tower) done = step;
        }
        quantities.Event("kill to lying flat", Elapsed(done));
        quantities.Angle("angle at the end", topple.Angle, done >= 0);
        quantities.Count("hits on the bystanders", hits);
        quantities.Amount("damage to the bystanders", damage);
        quantities.Count("bystanders dead", bystanders.Count(bystander => !scene.Alive(bystander)));
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureStructureCollapse(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var building = scene.Spawn(name, 2, 2000, 2000);
        var collapse = building.Modules.OfType<StructureCollapseUpdate>().FirstOrDefault() ?? throw new InvalidOperationException($"{name} has no StructureCollapseUpdate.");
        scene.Align();
        scene.Kill(building);
        var started = scene.StepUntil(() => collapse.IsCollapsing || collapse.IsCollapsed, RateScene.Seconds(120));
        var done = scene.StepUntil(() => collapse.IsCollapsed || scene.World.FindEntity(building.Id) != building, RateScene.Seconds(240));
        quantities.Exact("kill to the start of the collapse", started);
        // StepUntil counts from the step that executes an order given just before it. No order precedes the second call: its first step is already one
        // tick after the step in which the collapse was seen to start, so the ticks from that step to the end are its result and one.
        quantities.Event("collapse: start to down", started >= 0 && done >= 0 ? done + Time.TickOrder.Next : -1);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureMine(RateScene scene, string mineName)
    {
        var quantities = new RateQuantities(scene);
        var mine = scene.Spawn(mineName, 2, 2150, 2000);
        var tank = scene.Spawn("AmericaTankCrusader", 1, 2000, 2000);
        scene.StepFrames(RateScene.Seconds(2));
        scene.Align();
        scene.Move(tank, 2400, 2000);
        var log = new DamageLog(tank);
        long disabled = -1, mineGone = -1;
        var steps = scene.Time.WholeSecondsToTicks(40);
        for (long step = 1; step <= steps; step++)
        {
            log.Read(scene.Step(), Elapsed(step));
            if (disabled < 0 && tank.EmpDisabledUntil > scene.World.Tick) disabled = step;
            if (mineGone < 0 && scene.Gone(mine)) mineGone = step;
        }
        quantities.Event("order to first damage", log.Steps.Count > 0 ? log.Steps[0] : -1);
        quantities.Count("damage events", log.Steps.Count);
        quantities.Amount("damage to the tank", log.Total);
        quantities.Event("order to mine gone", Elapsed(mineGone));
        quantities.Event("order to tank disabled", Elapsed(disabled));
        quantities.Distance("tank position at the end", tank.X, scene.Alive(tank));
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureStealth(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var sneak = scene.Spawn(name, 1, 2000, 2000);
        var hidden = Since(scene, sneak, () => sneak.HasStatus(StealthedBit), RateScene.Seconds(30));
        quantities.Exact("creation to stealthed", hidden);
        if (hidden < 0 || !scene.Alive(sneak)) return quantities.Done();
        scene.StepFrames(RateScene.Seconds(1));
        scene.Align();

        // A detector of the other player appears 60 units away, stays ten seconds and is removed.
        var detector = scene.Spawn(StandardDetector, 2, 2060, 2000);
        var seen = Since(scene, detector, () => sneak.HasStatus(DetectedBit), RateScene.Seconds(20));
        quantities.Exact("detector appears to detected", seen);
        scene.StepFrames(RateScene.Seconds(10));
        scene.Align();
        scene.Kill(detector);
        var lost = scene.StepUntil(() => !sneak.HasStatus(DetectedBit), RateScene.Seconds(30));
        quantities.Exact("detector gone to hidden again", seen < 0 ? -1 : lost);

        // A walk: the stealth of units that only hide while they stand breaks and comes back.
        if (scene.Reference(name).Locomotor is null || !IsGroundDriver(scene.Reference(name))) return quantities.Done();
        scene.StepFrames(RateScene.Seconds(2));
        scene.Align();
        var broke = -1L;
        var track = new Track { Stride = scene.Rate.TicksPerOriginalFrame };
        track.Record(sneak);
        scene.Move(sneak, 2000, 2300);
        var budget = scene.Time.OriginalFrames(RateScene.Seconds(120));
        long arrived = -1;
        for (long step = 1; step <= budget && arrived < 0; step++)
        {
            scene.Step();
            if (broke < 0 && !sneak.HasStatus(StealthedBit)) broke = step;
            if (!sneak.HasMoveTarget) arrived = step;
        }
        quantities.Event("walk: order to stealth broken", Elapsed(broke));
        var again = arrived < 0 || broke < 0 ? -1 : sneak.HasStatus(StealthedBit) ? 0 : scene.StepUntil(() => sneak.HasStatus(StealthedBit), RateScene.Seconds(60));
        quantities.Event("walk: arrival to stealthed again", again);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureDetector(RateScene scene, string detectorName)
    {
        var quantities = new RateQuantities(scene);
        var sneak = scene.Spawn(StandardSneak, 1, 2000, 2000);
        var hidden = sneak.HasStatus(StealthedBit) ? 0 : scene.StepUntil(() => sneak.HasStatus(StealthedBit), RateScene.Seconds(30));
        if (hidden < 0) return quantities.Exact("detector appears to detected", -1).Done();
        scene.StepFrames(RateScene.Seconds(1));
        scene.Align();
        var detector = scene.Spawn(detectorName, 2, 2040, 2000);
        var seen = Since(scene, detector, () => sneak.HasStatus(DetectedBit), RateScene.Seconds(30));
        quantities.Exact("detector appears to detected", seen);
        if (seen < 0) return quantities.Done();
        // How often the mark is renewed: the times the status is lost and set again within 30 s are counted.
        var flickers = 0;
        var marked = true;
        var steps = scene.Time.WholeSecondsToTicks(30);
        for (long step = 1; step <= steps; step++)
        {
            scene.Step();
            var now = sneak.HasStatus(DetectedBit);
            if (now != marked) flickers++;
            marked = now;
        }
        quantities.Count("changes of the detected mark in 30 s", flickers);
        // The loop above starts one step after the detector's scan, which is a frame of the original at 30 Hz and a tick above it: without this the detector would be
        // removed one frame after its last scan at 30 Hz and one tick after it at 60 and 120 Hz (MeasureStealth aligns before its kill for the same reason).
        scene.Align();
        if (scene.Alive(detector)) scene.Kill(detector);
        var lost = !sneak.HasStatus(DetectedBit) ? 0 : scene.StepUntil(() => !sneak.HasStatus(DetectedBit), RateScene.Seconds(30));
        quantities.Exact("detector gone to hidden again", lost);
        return quantities.Done();
    }

    /// <summary>A soldier under the shroud: when the ground around him and ahead of his walk is seen (the shroud is refreshed every three frames of the original).</summary>
    private static ImmutableArray<RateQuantity> MeasureVision(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        scene.RevealMap = false;
        var world = scene.World;
        scene.Align();
        var start = world.Tick;
        scene.Order(1, (owner, sequence, now) => new SpawnCommand(owner, sequence, now, name, owner, Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero));
        // The shroud is a state of the frame's end (the world recomputes it after the last tick of a logic frame of the original): it is read there and counted from the frame's first step.
        var near = scene.StepUntilFrameEnd(() => world.IsPositionVisibleTo(scene.One, Fixed.FromInt(2050), Fixed.FromInt(2000)), RateScene.Seconds(10));
        quantities.Exact("creation to the ground 50 units away seen", near);
        var unit = scene.Find(name, start);
        if (unit is null) return quantities.Done();
        scene.StepFrames(RateScene.Seconds(1));
        scene.Align();
        scene.Move(unit, 2600, 2000);
        var far = scene.StepUntil(() => world.IsPositionVisibleTo(scene.One, Fixed.FromInt(2600), Fixed.FromInt(2000)), RateScene.Seconds(120));
        quantities.Event("walk: order to the goal 600 units away seen", far);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureShroudPing(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        scene.RevealMap = false;
        var world = scene.World;
        scene.Align();
        var start = world.Tick;
        scene.Order(1, (owner, sequence, now) => new SpawnCommand(owner, sequence, now, name, owner, Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero));
        Entity? ping = null;
        long centre = -1, gone = -1;
        var widest = 0;
        var steps = scene.Time.WholeSecondsToTicks(120);
        for (long step = 1; step <= steps && gone < 0; step++)
        {
            scene.Step();
            ping ??= scene.Find(name, start);
            // The shroud is a state of the frame's end: read there, counted from the frame's first step (at 30 Hz the step itself).
            if (centre < 0 && scene.Time.IsOriginalFrameBoundary(world.Tick) && world.IsPositionVisibleTo(scene.One, Fixed.FromInt(2000), Fixed.FromInt(2000)))
                centre = step - scene.Time.TicksPerOriginalFrame + TickOrder.Next;
            // The radius seen right now, probed every ten units east of the centre.
            var reach = 0;
            while (reach < 150 && world.IsPositionVisibleTo(scene.One, Fixed.FromInt(2000 + (reach + 1) * 10), Fixed.FromInt(2000))) reach++;
            widest = Math.Max(widest, reach);
            if (ping is not null && scene.Gone(ping)) gone = step;
        }
        quantities.Exact("creation to the centre seen", Elapsed(centre));
        quantities.Distance("widest radius seen (in steps of ten units)", Fixed.FromInt(widest * 10));
        quantities.Exact("lifetime", Elapsed(gone));
        return quantities.Done();
    }

    /// <summary>The staged capture: the unit walks to the building, plants its flag (the building flashes) and takes it over.</summary>
    private static ImmutableArray<RateQuantity> MeasureCapture(RateScene scene, string unitName, string buildingName)
    {
        var quantities = new RateQuantities(scene);
        var unit = scene.Spawn(unitName, 1, 1900, 2000);
        var building = scene.Spawn(buildingName, 2, 2100, 2000);
        scene.World.CompleteUpgrade(scene.One, null, "Upgrade_InfantryCaptureBuilding");
        scene.StepFrames(1);
        // A fresh unit recharges its capture power first; the recharge itself is a case of its own (recharge ...).
        foreach (var module in unit.Modules.OfType<ISpecialPowerModule>()) module.SetReadyFrame(scene.World.Tick);
        scene.World.MarkDirty(unit);
        scene.Align();
        scene.Order(1, (owner, sequence, now) => new CaptureCommand(owner, sequence, now, unit.Id, building.Id));
        var flashes = 0;
        long firstFlash = -1, lastFlash = -1, captured = -1;
        var steps = scene.Time.WholeSecondsToTicks(120);
        for (long step = 1; step <= steps && captured < 0; step++)
            foreach (var item in scene.Step().Events)
            {
                if (item.Kind == SimEventKind.CaptureFlash)
                {
                    flashes++;
                    if (firstFlash < 0) firstFlash = step;
                    lastFlash = step;
                }
                if (item.Kind == SimEventKind.StructureCaptured && item.Entity == building.Id) captured = step;
            }
        quantities.Event("order to first flash", Elapsed(firstFlash));
        quantities.Event("first to last flash", firstFlash >= 0 ? lastFlash - firstFlash : -1);
        quantities.Count("flashes of the building", flashes);
        quantities.Event("order to captured", Elapsed(captured));
        return quantities.Done();
    }

    /// <summary>The avalanche (TensileFormationUpdate): two chunks side by side, the lead one is hurt, the formation slides.</summary>
    private static ImmutableArray<RateQuantity> MeasureAvalanche(RateScene scene)
    {
        var quantities = new RateQuantities(scene);
        var lead = scene.Spawn("AvalancheLeadChunk", 2, 2000, 2000);
        var chunk = scene.Spawn("AvalancheChunk", 2, 2012, 2000);
        var formation = lead.Modules.OfType<TensileFormationUpdate>().FirstOrDefault() ?? throw new InvalidOperationException("AvalancheLeadChunk has no TensileFormationUpdate.");
        scene.StepFrames(RateScene.Seconds(4));
        scene.Align();
        var (leadX, leadY, chunkX, chunkY) = (lead.X, lead.Y, chunk.X, chunk.Y);
        if (lead.Body is { } body) scene.Hurt(lead, body.MaxHealth / Fixed.FromInt(2));
        var noticed = formation.Enabled ? 0 : scene.StepUntil(() => formation.Enabled, RateScene.Seconds(10));
        quantities.Exact("hurt to sliding", noticed);
        // The slide is over when the formation's life counter has stood still for two seconds.
        var life = formation.Life;
        long moved = 0;
        var steps = scene.Time.WholeSecondsToTicks(60);
        for (long step = 1; step <= steps && step - moved <= scene.Time.WholeSecondsToTicks(2); step++)
        {
            scene.Step();
            if (formation.Life == life) continue;
            life = formation.Life;
            moved = step;
        }
        quantities.Exact("slide duration", noticed < 0 ? -1 : moved);
        quantities.Distance("lead chunk: distance slid", RateMeasure.Distance(leadX, leadY, lead.X, lead.Y), noticed >= 0);
        quantities.Distance("second chunk: distance slid", RateMeasure.Distance(chunkX, chunkY, chunk.X, chunk.Y), noticed >= 0);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureEmp(RateScene scene, string pulseName)
    {
        var quantities = new RateQuantities(scene);
        var tank = scene.Spawn(FieldTank, 2, 2020, 2000);
        scene.Align();
        var start = scene.World.Tick;
        scene.Order(1, (owner, sequence, now) => new SpawnCommand(owner, sequence, now, pulseName, owner, Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero));
        Entity? pulse = null;
        long hit = -1, gone = -1;
        long until = 0;
        var steps = scene.Time.WholeSecondsToTicks(60);
        for (long step = 1; step <= steps && (gone < 0 || hit < 0 && step < scene.Time.WholeSecondsToTicks(20)); step++)
        {
            scene.Step();
            pulse ??= scene.Find(pulseName, start);
            if (hit < 0 && tank.EmpDisabledUntil > scene.World.Tick)
            {
                hit = step;
                until = tank.EmpDisabledUntil - start;
            }
            if (gone < 0 && pulse is not null && scene.Gone(pulse)) gone = step;
        }
        quantities.Exact("creation to the tank disabled", Elapsed(hit));
        quantities.Exact("creation to the end of the disabling", hit >= 0 ? until : -1);
        quantities.Exact("lifetime of the pulse", Elapsed(gone));
        return quantities.Done();
    }

    /// <summary>The retail train on a straight track of 3,000 units with a station after 2,000: setting off, top speed, braking for the station, the stop, setting off again.</summary>
    private static ImmutableArray<RateQuantity> MeasureTrain(RateScene scene)
    {
        var quantities = new RateQuantities(scene);
        scene.Waypoints = [("RailA", 500, 2000), ("RailB", 1500, 2000), ("RailCStation", 2500, 2000), ("RailD", 3500, 2000)];
        scene.Links = [(1, 2), (2, 3), (3, 4)];
        scene.Align();
        var start = scene.World.Tick;
        scene.Order(0, (_, sequence, now) => new SpawnCommand(PlayerId.System, sequence, now, "TrainEngine", PlayerId.System, Fixed.FromInt(510), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero));
        Entity? engine = null;
        RailroadBehavior? rail = null;
        long braking = -1, waiting = -1, leaving = -1, topAt = -1;
        var top = Fixed.Zero;
        var (lastX, stopX) = (Fixed.FromInt(510), Fixed.Zero);
        var carriages = 0;
        var steps = scene.Time.WholeSecondsToTicks(240);
        for (long step = 1; step <= steps && leaving < 0; step++)
        {
            scene.Step();
            engine ??= scene.Find("TrainEngine", start);
            if (engine is null) continue;
            rail ??= engine.Modules.OfType<RailroadBehavior>().FirstOrDefault();
            if (rail is null) break;
            // The first second is left out: the engine is put on its track in its first update.
            var speed = step <= scene.Time.WholeSecondsToTicks(1) ? Fixed.Zero : (engine.X - lastX).Abs() * scene.Rate.TicksPerSecond;
            lastX = engine.X;
            if (speed > top)
            {
                top = speed;
                topAt = step;
            }
            carriages = Math.Max(carriages, scene.World.Entities.Count(entity => entity.SpawnTick >= start && entity.Modules.Any(module => module is RailroadBehavior)) - 1);
            if (braking < 0 && rail.Conductor == RailroadBehavior.ConductorStates.ApplyBrakes) braking = step;
            if (waiting < 0 && rail.Conductor == RailroadBehavior.ConductorStates.WaitAtStation)
            {
                waiting = step;
                stopX = engine.X;
            }
            if (waiting >= 0 && rail.Conductor == RailroadBehavior.ConductorStates.Accelerate) leaving = step;
        }
        quantities.Count("carriages", carriages);
        quantities.Distance("top speed per second", top);
        quantities.Event("creation to top speed", Elapsed(topAt));
        quantities.Event("creation to braking for the station", Elapsed(braking));
        quantities.Event("creation to standing at the station", Elapsed(waiting));
        quantities.Distance("where it stands at the station", stopX, waiting >= 0);
        quantities.Exact("stop at the station", waiting >= 0 && leaving >= 0 ? leaving - waiting : -1);
        return quantities.Done();
    }
}
