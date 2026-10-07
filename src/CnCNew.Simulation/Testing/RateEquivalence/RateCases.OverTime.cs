using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    private const string FieldTank = "ChinaTankOverlord", FieldSoldier = "ChinaInfantryRedguard";

    /// <summary>The data of the named modules of a template as one text: two templates with the same text behave the same as far as these modules go.</summary>
    private static string ModuleKey(ObjectTemplate template, params string[] modules)
        => string.Join('|', template.Modules.Where(module => modules.Contains(module.Name)).Select(module => module.Name + ":" + DataText(module)));

    /// <summary>The data record of a module as text (its fields by name and value), with the invariant culture.</summary>
    private static string DataText(ModuleSpec module) => string.Create(CultureInfo.InvariantCulture, $"{module.Data}");

    private static bool HasModule(ObjectTemplate template, string name) => template.Modules.Any(module => module.Name == name);

    /// <summary>Templates that carry <paramref name="module"/>, one per distinct data of <paramref name="keyModules"/>, the first by name.</summary>
    private static (ObjectTemplate[] Chosen, int Candidates) Distinct(RateCatalogs catalogs, string module, Func<ObjectTemplate, bool> filter, params string[] keyModules)
    {
        var candidates = catalogs.Reference.Templates.Where(template => template.IsInstantiable && HasModule(template, module) && filter(template)).OrderBy(template => template.Name, StringComparer.Ordinal).ToArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return (candidates.Where(template => seen.Add(ModuleKey(template, keyModules))).ToArray(), candidates.Length);
    }

    private static RateCaseGroup OverTimeCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, OverTime);

        // Fields: an object that fires a weapon at its own place for its lifetime (poison, radiation, fire).
        var (fields, fieldCandidates) = Distinct(catalogs, "FireWeaponUpdate", template => template.DefaultWeaponSet is not { WeaponCount: > 0 } && (HasModule(template, "LifetimeUpdate") || HasModule(template, "DeletionUpdate")),
            "FireWeaponUpdate", "LifetimeUpdate", "DeletionUpdate");
        foreach (var field in fields)
        {
            var name = field.Name;
            builder.Add($"field {name}", scene => MeasureField(scene, name), name, FieldTank, FieldSoldier);
        }
        builder.Note(string.Create(CultureInfo.InvariantCulture, $"{fieldCandidates} field objects (FireWeaponUpdate with a lifetime, no weapon set), {fields.Length} distinct by their module data"));

        builder.Add("poison ChinaInfantryRedguard", MeasurePoison, FieldSoldier);

        // Fire: every distinct FlammableUpdate.
        var (flammables, flammableCandidates) = Distinct(catalogs, "FlammableUpdate", _ => true, "FlammableUpdate");
        foreach (var flammable in flammables)
        {
            var name = flammable.Name;
            builder.Add($"fire {name}", scene => MeasureFlammable(scene, name), name);
        }
        builder.Note(string.Create(CultureInfo.InvariantCulture, $"{flammableCandidates} objects burn (FlammableUpdate), {flammables.Length} distinct by its data"));

        // Healing and repair. Kind: exact where healer and patient stand, an event of movement where somebody walks or docks first.
        foreach (var (id, healer, patient, how, moves) in new[]
                 {
                     ("self heal ChinaStandardMine", "", "ChinaStandardMine", HealBy.Nothing, false),
                     ("self heal ChinaEMPMine", "", "ChinaEMPMine", HealBy.Nothing, false),
                     ("base regeneration AmericaCommandCenter", "", "AmericaCommandCenter", HealBy.Nothing, false),
                     ("base regeneration ChinaPowerPlant", "", "ChinaPowerPlant", HealBy.Nothing, false),
                     ("base regeneration GLABarracks", "", "GLABarracks", HealBy.Nothing, false),
                     ("heal radius AmericaVehicleMedic", "AmericaVehicleMedic", "AmericaInfantryRanger", HealBy.Nothing, false),
                     ("heal TechHospital", "TechHospital", "AmericaInfantryRanger", HealBy.Nothing, false),
                     ("heal TechRepairbay", "TechRepairbay", "AmericaTankCrusader", HealBy.Nothing, false),
                     ("repair marker level 1", "RepairVehiclesInArea_InvisibleMarker_Level1", "AmericaTankCrusader", HealBy.Nothing, false),
                     ("repair marker level 3", "RepairVehiclesInArea_InvisibleMarker_Level3", "AmericaTankCrusader", HealBy.Nothing, false),
                     ("propaganda ChinaSpeakerTower", "ChinaSpeakerTower", "ChinaInfantryRedguard", HealBy.Nothing, false),
                     ("dozer repair AmericaVehicleDozer", "AmericaVehicleDozer", "AmericaPowerPlant", HealBy.Repair, true),
                     ("worker repair GLAInfantryWorker", "GLAInfantryWorker", "GLABarracks", HealBy.Repair, true),
                     ("repair dock AmericaWarFactory", "AmericaWarFactory", "AmericaTankCrusader", HealBy.Dock, true),
                     ("repair dock TechRepairPad", "TechRepairPad", "AmericaTankCrusader", HealBy.Dock, true),
                     ("heal inside AmericaBarracks", "AmericaBarracks", "AmericaInfantryRanger", HealBy.Enter, true),
                     ("heal inside GLATunnelNetwork", "GLATunnelNetwork", "GLAInfantryRebel", HealBy.Enter, true),
                     ("heal inside ChinaVehicleTroopCrawler", "ChinaVehicleTroopCrawler", "ChinaInfantryRedguard", HealBy.Enter, true),
                 })
        {
            string[] needed = healer.Length > 0 ? [healer, patient] : [patient];
            builder.Add(id, scene => MeasureRecovery(scene, healer, patient, how, moves), needed);
        }
        builder.NotCovered("overtime: GarrisonContain healing in a civilian building, the hero and veterancy self-heal that an upgrade or a rank starts (AutoHealBehavior with StartsActive = No), fire spreading from one object to the next");
        return builder.Done();
    }

    /// <summary>What an observer sees of one victim over a run: every loss of health with its step.</summary>
    private sealed class DamageLog(Entity victim)
    {
        public Entity Victim => victim;
        public List<long> Steps { get; } = [];
        public Fixed Total { get; private set; }
        public long Died { get; private set; } = -1;

        public void Read(TickResult result, long step)
        {
            foreach (var item in result.Events)
            {
                if (item.Entity != victim.Id) continue;
                if (item.Kind == SimEventKind.EntityDamaged)
                {
                    Steps.Add(step);
                    Total += Fixed.FromRaw(item.Value);
                }
                else if (item.Kind == SimEventKind.EntityDied && Died < 0) Died = step;
            }
        }

        public void Report(RateQuantities quantities, string label, string span)
        {
            quantities.Exact($"{label}: first damage", Steps.Count > 0 ? Steps[0] : -1);
            quantities.Exact($"{label}: first to second damage", Steps.Count > 1 ? Steps[1] - Steps[0] : -1);
            quantities.Exact($"{label}: first to last damage", Steps.Count > 1 ? Steps[^1] - Steps[0] : -1);
            quantities.Count($"{label}: damage events in {span}", Steps.Count);
            quantities.Amount($"{label}: damage in {span}", Total);
            quantities.Exact($"{label}: death", Died);
        }
    }

    private static ImmutableArray<RateQuantity> MeasureField(RateScene scene, string fieldName)
    {
        var quantities = new RateQuantities(scene);
        var tank = scene.Spawn(FieldTank, 2, 2012, 2000);
        var soldier = scene.Spawn(FieldSoldier, 2, 1988, 2000);
        scene.Align();
        var start = scene.World.Tick;
        scene.Order(1, (owner, sequence, now) => new SpawnCommand(owner, sequence, now, fieldName, owner, Fixed.FromInt(2000), Fixed.FromInt(2000), Fixed.Zero, Fixed.Zero));
        Entity? field = null;
        long gone = -1;
        var logs = new[] { new DamageLog(tank), new DamageLog(soldier) };
        var steps = scene.Time.WholeSecondsToTicks(90);
        for (long step = 1; step <= steps; step++)
        {
            var result = scene.Step();
            field ??= scene.Find(fieldName, start);
            if (field is not null && gone < 0 && scene.Gone(field)) gone = step;
            foreach (var log in logs) log.Read(result, Elapsed(step));
        }
        quantities.Exact("field lifetime", Elapsed(gone));
        logs[0].Report(quantities, "tank", "90 s");
        logs[1].Report(quantities, "soldier", "90 s");
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasurePoison(RateScene scene)
    {
        var quantities = new RateQuantities(scene);
        var soldier = scene.Spawn(FieldSoldier, 2, 2000, 2000);
        scene.Align();
        scene.Order(0, (_, sequence, now) => new DamageCommand(PlayerId.System, sequence, now, soldier.Id, Fixed.FromInt(2), DamageTypes.Poison, 0));
        var log = new DamageLog(soldier);
        var steps = scene.Time.WholeSecondsToTicks(60);
        for (long step = 1; step <= steps; step++) log.Read(scene.Step(), Elapsed(step));
        log.Report(quantities, "poisoned soldier", "60 s");
        return quantities.Done();
    }

    private static readonly int AflameBit = ObjectStatusBits.Index("AFLAME"), BurnedBit = ObjectStatusBits.Index("BURNED");

    private static ImmutableArray<RateQuantity> MeasureFlammable(RateScene scene, string name)
    {
        var quantities = new RateQuantities(scene);
        var thing = scene.Spawn(name, 2, 2000, 2000);
        scene.Align();
        // Flame damage of 10 per frame of the original until the object burns, at most three seconds of it.
        var hits = 0;
        var log = new DamageLog(thing);
        long ignited = -1, out_ = -1;
        var steps = scene.Time.WholeSecondsToTicks(180);
        for (long step = 1; step <= steps && out_ < 0; step++)
        {
            if (ignited < 0 && scene.Alive(thing) && scene.Time.IsOriginalFrameBoundary(scene.World.Tick) && step <= scene.Time.WholeSecondsToTicks(3))
            {
                scene.Order(0, (_, sequence, now) => new DamageCommand(PlayerId.System, sequence, now, thing.Id, Fixed.FromInt(10), DamageTypes.Flame, 0));
                hits++;
            }
            var result = scene.Step();
            if (ignited < 0)
            {
                if (thing.HasStatus(AflameBit)) ignited = step;
                continue;
            }
            log.Read(result, step - ignited);
            if (!thing.HasStatus(AflameBit) || !scene.Alive(thing)) out_ = step - ignited;
        }
        quantities.Count("flame hits until it burns", hits, ignited >= 0);
        quantities.Exact("aflame duration", out_);
        quantities.Count("burned at the end", thing.HasStatus(BurnedBit) ? 1 : 0, ignited >= 0);
        if (ignited >= 0) log.Report(quantities, "burning object", "its fire");
        return quantities.Done();
    }

    private enum HealBy { Nothing, Repair, Dock, Enter }

    /// <summary>
    /// A patient at half health, alone or next to a healer, watched for two minutes. Health is compared once per frame of the original, so a heal that is
    /// spread over the ticks counts as often as one that comes once per frame.
    /// </summary>
    private static ImmutableArray<RateQuantity> MeasureRecovery(RateScene scene, string healerName, string patientName, HealBy how, bool moves)
    {
        var quantities = new RateQuantities(scene);
        var healer = healerName.Length > 0 ? scene.Spawn(healerName, 1, 2000, 2000) : null;
        var patient = scene.Spawn(patientName, 1, healer is null ? 2000 : how == HealBy.Nothing ? 2030 : 2150, 2000);
        var body = patient.Body ?? throw new InvalidOperationException($"{patientName} has no body.");
        scene.HurtTo(patient, 1, 2);
        scene.Align();
        var hurt = body.Health;
        switch (how)
        {
            case HealBy.Repair:
                scene.Order(1, (owner, sequence, now) => new RepairCommand(owner, sequence, now, healer!.Id, patient.Id));
                break;
            case HealBy.Dock:
                scene.Order(1, (owner, sequence, now) => new DockCommand(owner, sequence, now, patient.Id, healer!.Id));
                break;
            case HealBy.Enter:
                scene.Order(1, (owner, sequence, now) => new MoveAndEnterCommand(owner, sequence, now, patient.Id, healer!.Id));
                break;
        }

        var kind = moves ? RateQuantityKind.MovementEventTime : RateQuantityKind.ExactTime;
        long first = -1, second = -1, full = -1;
        var risingFrames = 0;
        var previous = hurt;
        var afterMinute = hurt;
        var steps = scene.Time.WholeSecondsToTicks(120);
        var minute = scene.Time.WholeSecondsToTicks(60);
        for (long step = 1; step <= steps; step++)
        {
            scene.Step();
            if (step == minute) afterMinute = body.Health;
            if (full < 0 && body.Health >= body.MaxHealth) full = step;
            if (!scene.Time.IsOriginalFrameBoundary(scene.World.Tick)) continue;
            if (body.Health > previous)
            {
                risingFrames++;
                if (risingFrames == 1) first = step;
                if (risingFrames == 2) second = step;
            }
            previous = body.Health;
        }
        quantities.Time("first frame with healing", kind, Elapsed(first));
        quantities.Time("second frame with healing", kind, Elapsed(second));
        quantities.Time("time to full health", kind, Elapsed(full));
        quantities.Count("frames with healing in 120 s", risingFrames);
        quantities.Amount("health regained in 60 s", afterMinute - hurt);
        return quantities.Done();
    }
}
